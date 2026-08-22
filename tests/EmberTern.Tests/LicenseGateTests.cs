using System;
using System.IO;
using System.Threading.Tasks;
using EmberTern.App.Licensing;
using EmberTern.Core.Connections;
using EmberTern.Core.Settings;
using EmberTern.Firebird;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>L4b — what the licence actually PREVENTS.</b>
///
/// <para>⚠⚠ <b>Read the <c>#if DEBUG</c> pairs before concluding anything from a green run.</b> The gate is a
/// compile-time <c>const</c> (design §16.5, decision D15), so a <c>Debug</c> run can only ever prove the
/// <c>Debug</c> arm — that nothing is blocked. ⭐ The refusals below are proven by the <b>Release</b> run,
/// which is why running the suite in <c>-c Release</c> is part of L4b's acceptance rather than hygiene. A
/// test that quietly asserted "it throws" would fail in <c>Debug</c>; one that asserted only "it does not"
/// would prove the feature absent.</para>
///
/// <para>⭐ Every licence here is really signed and verified through the real <c>LicenseVerifier</c>, with
/// the fixture's OWN trusted-key table, never the shipped <c>TrustedKeys.Production</c>.</para>
/// </summary>
public sealed class LicenseGateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "etlic-gate", Guid.NewGuid().ToString("N"));

    private readonly LicenseFixtures _fixtures = new();

    public LicenseGateTests()
    {
        Directory.CreateDirectory(UserDirectory);
        Directory.CreateDirectory(MachineDirectory);
        Directory.CreateDirectory(SettingsDirectory);
    }

    private string UserDirectory => Path.Combine(_root, "user");

    private string MachineDirectory => Path.Combine(_root, "machine");

    private string SettingsDirectory => Path.Combine(_root, "settings");

    // ── The four openers, one predicate ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>An expired licence stops every path that opens a new attachment — Test connection included.</b>
    ///
    /// <para>⛔ The absence of an exception for Test connection is the ratified point (2026-08-15), and it is
    /// stated as a prohibition because it is the exception someone would otherwise be tempted to make: a
    /// working Test connection on an expired licence is most of what a developer needs a connection for.</para>
    /// </summary>
    [Fact]
    public async Task AnExpiredLicence_RefusesEveryOpener_IncludingTestConnection()
    {
        var seam = SeamOver(Expired());

        Assert.Equal(LicenseStatus.Expired, LicenceOver(Expired()).Verdict.Status);

#if DEBUG
        // The Debug arm: nothing is ever blocked, so the openers are not even reached. ⛔ Do not "fix" this
        // by calling them — with the gate off they would try to reach a real database.
        Assert.True(seam.Allows);
#else
        Assert.False(seam.Allows);

        var profile = new ConnectionProfile { Name = "P", Host = "localhost", DatabasePath = "x.fdb" };

        // ⭐⭐ Each refuses SYNCHRONOUSLY, before the driver is touched — the Task is never created, which is
        //    the property under test rather than an implementation detail: a refusal that happened INSIDE the
        //    task would mean the attachment attempt had already begun.
        // ⚠ Hence the void-lambda form with a discard. `Assert.Throws(Func<Task>)` is obsolete precisely
        //   because it cannot observe an exception a Task carries — here there is no task to carry one, and
        //   `ThrowsAsync` would assert something weaker.
        Assert.Throws<LicenseBlockedException>(() => { _ = seam.OpenAsync(profile); });
        Assert.Throws<LicenseBlockedException>(() => { _ = seam.TestAsync(profile); });
        Assert.Throws<LicenseBlockedException>(() => { _ = seam.OpenDebugSessionAsync(default); });
        Assert.Throws<LicenseBlockedException>(() => { _ = seam.OpenImportSessionAsync(); });
#endif

        await Task.CompletedTask;
    }

    /// <summary>
    /// The refusal carries the VERDICT, so the display site can say what happened in the user's language.
    ///
    /// <para>⚠⚠ This is the Phase-5 shape (design §17.3): the exception's own <c>Message</c> is a developer
    /// breadcrumb and is not translated. <c>LicenseSurfaceLocalizationTests</c> pins the sentence the user
    /// actually reads.</para>
    /// </summary>
    [Fact]
    public void ARefusal_CarriesTheVerdict_NotJustAMessage()
    {
#if !DEBUG
        var seam = SeamOver(Expired());
        var profile = new ConnectionProfile { Name = "P", Host = "localhost", DatabasePath = "x.fdb" };

        var ex = Assert.Throws<LicenseBlockedException>(() => { _ = seam.TestAsync(profile); });

        Assert.Equal(LicenseStatus.Expired, ex.Verdict.Status);
        Assert.NotNull(ex.Verdict.Payload);
#endif
        Assert.True(true);
    }

    /// <summary>
    /// ⭐ A licence in its grace period is FULL function (design §7): the whole point of the 14 days is that a
    /// purchase-order delay does not become a work stoppage.
    /// </summary>
    [Fact]
    public void AGraceLicence_StillOpensConnections()
    {
        var licence = _fixtures.Issue(Now.AddYears(-1), Now.AddYears(-1), Now.AddDays(-3));

        Assert.Equal(LicenseStatus.Grace, LicenceOver(licence).Verdict.Status);
        Assert.True(SeamOver(licence).Allows);
    }

    [Fact]
    public void AValidLicence_OpensConnections()
    {
        var licence = _fixtures.Valid(Now);

        Assert.Equal(LicenseStatus.Valid, LicenceOver(licence).Verdict.Status);
        Assert.True(SeamOver(licence).Allows);
    }

    /// <summary>
    /// ⭐ The gated states (§7) also stop new connections — <c>IsBlocked</c> is a STRONGER condition than
    /// "no new databases", so <c>AllowsConnecting</c> is the conjunction and not just the expiry rule.
    /// </summary>
    [Theory]
    [InlineData("unlicensed")]
    [InlineData("invalid")]
    [InlineData("notyetvalid")]
    public void AGatedState_AlsoRefusesConnections(string which)
    {
        var licence = which switch
        {
            "unlicensed" => null,
            "invalid" => LicenseFixtures.Tamper(_fixtures.Valid(Now)),
            _ => _fixtures.Issue(Now, Now.AddDays(30), Now.AddYears(1)),
        };

        var service = LicenceOver(licence);

#if DEBUG
        Assert.True(service.AllowsConnecting, "A Debug build never blocks.");
#else
        Assert.True(service.IsBlocked);
        Assert.False(service.AllowsConnecting);
#endif
    }

    /// <summary>
    /// ⭐⭐ <b>Settings ▸ Licence is reachable in every state, including the blocked ones.</b> It is the way
    /// OUT of <c>Expired</c> and <c>Unlicensed</c> — a gate that also hid the screen for fixing the licence
    /// would be a trap, and this is the assertion that stops one being built.
    /// </summary>
    [Fact]
    public void TheLicencePage_DescribesEvenABlockedLicence()
    {
        foreach (var licence in new[] { null, LicenseFixtures.Tamper(_fixtures.Valid(Now)), Expired() })
        {
            var page = new EmberTern.App.ViewModels.LicenseSettingsViewModel(LicenceOver(licence));

            Assert.False(string.IsNullOrWhiteSpace(page.StatusHeadline));
            Assert.False(string.IsNullOrWhiteSpace(page.StatusExplanation));
        }
    }

    /// <summary>An expired licence still shows its licensee and dates — that is exactly what support asks for.</summary>
    [Fact]
    public void AnExpiredLicence_StillShowsItsDetails()
    {
        var page = new EmberTern.App.ViewModels.LicenseSettingsViewModel(LicenceOver(Expired()));

        Assert.True(page.HasDetails);
        Assert.Equal(LicenseFixtures.Licensee, page.Licensee);
        Assert.False(string.IsNullOrWhiteSpace(page.ValidUntil));
        Assert.False(string.IsNullOrWhiteSpace(page.LicenseId));
    }

    /// <summary>
    /// ⭐ The About marker follows the BUILD, not a setting — and it disappears entirely from a Release
    /// binary, which is why users can never see the untranslated sentence (design §16.5).
    /// </summary>
    [Fact]
    public void TheAboutDebugMarker_FollowsTheBuildConfiguration()
    {
        var about = new EmberTern.App.ViewModels.AboutViewModel(LicenceOver(_fixtures.Valid(Now)));

#if DEBUG
        Assert.True(about.ShowDebugGateMarker);
        Assert.False(string.IsNullOrWhiteSpace(about.DebugGateMarker));
#else
        Assert.False(about.ShowDebugGateMarker);
#endif
    }

    [Fact]
    public void About_NamesTheLicensee_AndHidesTheLineWhenThereIsNone()
    {
        var licensed = new EmberTern.App.ViewModels.AboutViewModel(LicenceOver(_fixtures.Valid(Now)));
        Assert.True(licensed.HasLicensee);
        Assert.Contains(LicenseFixtures.Licensee, licensed.LicensedToText, StringComparison.Ordinal);

        var unlicensed = new EmberTern.App.ViewModels.AboutViewModel(LicenceOver(null));
        Assert.False(unlicensed.HasLicensee);
        Assert.Equal(string.Empty, unlicensed.LicensedToText);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private string Expired() => _fixtures.Issue(Now.AddYears(-2), Now.AddYears(-2), Now.AddDays(-60));

    /// <summary>A service over a fresh temp location holding <paramref name="licence"/> (or nothing).</summary>
    private LicenseService LicenceOver(string? licence)
    {
        var directory = Path.Combine(_root, "case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        if (licence is not null)
        {
            File.WriteAllText(Path.Combine(directory, LicenseConstants.StoredFileName), licence);
        }

        var service = new LicenseService(
            new LicenseLocation(directory, MachineDirectory),
            new ApplicationSettingsStore(SettingsDirectory),
            _fixtures.TrustedKeys,
            () => Now);

        service.Refresh();
        return service;
    }

    /// <summary>
    /// ⚠ A real <see cref="FirebirdConnectionService"/>, deliberately: the seam must refuse BEFORE the driver
    /// is touched, and handing it a live service is what makes that assertion mean something. Nothing here
    /// ever reaches a database — in Release the guard throws first, and in Debug the openers are not called.
    /// </summary>
    private LicensedConnections SeamOver(string? licence)
        => new(new FirebirdConnectionService(), LicenceOver(licence));

    public void Dispose()
    {
        _fixtures.Dispose();

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
