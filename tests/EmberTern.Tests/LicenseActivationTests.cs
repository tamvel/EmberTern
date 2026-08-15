using System;
using System.IO;
using EmberTern.App.Controls;
using EmberTern.App.Licensing;
using EmberTern.App.ViewModels;
using EmberTern.Core.Settings;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// L4b — the activation flow (design §5), through the view model the window binds to.
///
/// <para>⭐ <b>Three gestures, ONE buffer.</b> A dropped file, a browsed file and a paste all end up in
/// <c>PasteText</c>, which is what <c>Activate</c> acts on — so a paste cannot be verified by different code
/// from a drop, and the user can see what they are about to install.</para>
///
/// <para>⭐ <b>Every success here is a verdict read back FROM DISK.</b> <c>LicenseStore.Install</c> takes the
/// verifier as a parameter and returns what it read after writing, so there is no way to report success for a
/// write that half-succeeded (§5, Architecture rule 11).</para>
/// </summary>
public sealed class LicenseActivationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "etlic-activation", Guid.NewGuid().ToString("N"));

    private readonly LicenseFixtures _fixtures = new();

    public LicenseActivationTests()
    {
        Directory.CreateDirectory(UserDirectory);
        Directory.CreateDirectory(MachineDirectory);
        Directory.CreateDirectory(SettingsDirectory);
    }

    private string UserDirectory => Path.Combine(_root, "user");

    private string MachineDirectory => Path.Combine(_root, "machine");

    private string SettingsDirectory => Path.Combine(_root, "settings");

    private string InstalledPath => Path.Combine(UserDirectory, LicenseConstants.StoredFileName);

    // ── Nothing to act on ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PressingActivateWithAnEmptyBuffer_SaysSo_AndInstallsNothing()
    {
        var vm = Activation(out _);

        vm.ActivateCommand.Execute(null);

        Assert.True(vm.HasMessage);
        Assert.Equal(MessageSeverity.Warning, vm.Severity);
        Assert.False(vm.IsActivated);
        Assert.False(File.Exists(InstalledPath));
    }

    // ── A file that is not a licence ─────────────────────────────────────────────────────────────────

    [Fact]
    public void AnArtifactThatIsNotALicence_IsRefusedWithAnExplanation()
    {
        var vm = Activation(out _);
        vm.PasteText = "this is somebody's e-mail signature, not a licence";

        vm.ActivateCommand.Execute(null);

        Assert.Equal(MessageSeverity.Error, vm.Severity);
        Assert.False(vm.IsActivated);
        Assert.False(File.Exists(InstalledPath));

        // ⛔ Never "License validation failed (code 7)" — the message is the whole support experience (§17.3).
        Assert.DoesNotContain("LicenseFailure", vm.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", vm.Message, StringComparison.Ordinal);
        Assert.True(vm.Message.Length > 40, "A refusal has to say what happened, why, and what to do now.");
    }

    /// <summary>
    /// ⭐ An ALTERED licence is refused, and by the tampering that actually exists.
    ///
    /// <para>⚠⚠ The tampering edits the ENCODING, not the text: the payload is base64url, so a text replace of
    /// the licensee's name matches nothing and the "altered" licence verifies perfectly — the L4a finding
    /// (§37.3) where a test reported the absence of a failure as a success.</para>
    /// </summary>
    [Fact]
    public void AnAlteredLicence_IsRefused()
    {
        var vm = Activation(out _);
        vm.PasteText = LicenseFixtures.Tamper(_fixtures.Valid(Now));

        vm.ActivateCommand.Execute(null);

        Assert.Equal(MessageSeverity.Error, vm.Severity);
        Assert.False(vm.IsActivated);
        Assert.False(File.Exists(InstalledPath));
    }

    // ── The happy path ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void APastedLicence_IsInstalled_AndVerifiedBackFromDisk()
    {
        var vm = Activation(out var service);
        vm.PasteText = _fixtures.Valid(Now);

        vm.ActivateCommand.Execute(null);

        Assert.True(vm.IsActivated);
        Assert.Equal(MessageSeverity.Success, vm.Severity);
        Assert.True(File.Exists(InstalledPath));

        // ⭐ The service now answers from the file, not from what was pasted a moment ago.
        Assert.Equal(LicenseStatus.Valid, service.Verdict.Status);
        Assert.Equal(InstalledPath, service.SourcePath);
        Assert.Contains(LicenseFixtures.Licensee, vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADroppedFile_IsReadIntoTheOneBuffer()
    {
        var vm = Activation(out _);
        var dropped = Path.Combine(_root, LicenseConstants.DeliveredFileName);
        File.WriteAllText(dropped, _fixtures.Valid(Now));

        vm.OfferFile(dropped);

        Assert.Equal(File.ReadAllText(dropped), vm.PasteText);

        vm.ActivateCommand.Execute(null);
        Assert.True(vm.IsActivated);
    }

    [Fact]
    public void AFileThatCannotBeRead_SaysSo_AndLeavesTheBufferAlone()
    {
        var vm = Activation(out _);
        vm.PasteText = "kept";

        vm.OfferFile(Path.Combine(_root, "there-is-no-such-file.etlic"));

        Assert.Equal(MessageSeverity.Error, vm.Severity);
        Assert.Equal("kept", vm.PasteText);
        Assert.False(vm.IsActivated);
    }

    // ── The freshness rule (§16.4) ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Re-importing the file you already have is a NO-OP, not a downgrade. It is what makes renewal
    /// idempotent, and it is the mistake a customer actually makes: opening last year's e-mail.
    /// </summary>
    [Fact]
    public void OfferingTheSameLicenceAgain_ChangesNothing()
    {
        var licence = _fixtures.Valid(Now);
        var vm = Activation(out _);

        vm.PasteText = licence;
        vm.ActivateCommand.Execute(null);
        Assert.True(vm.IsActivated);

        var again = Activation(out _, existing: licence);
        again.PasteText = licence;
        again.ActivateCommand.Execute(null);

        Assert.False(again.IsActivated);
        Assert.Equal(MessageSeverity.Warning, again.Severity);
    }

    [Fact]
    public void ARenewalWithTheSameIdAndANewerIssueDate_IsInstalled()
    {
        var original = _fixtures.Issue(Now, Now.AddDays(-1), Now.AddDays(20), "lid-renewal");
        var renewed = _fixtures.Issue(Now.AddDays(1), Now.AddDays(-1), Now.AddYears(1), "lid-renewal");

        var vm = Activation(out var service, existing: original);
        vm.PasteText = renewed;

        vm.ActivateCommand.Execute(null);

        Assert.True(vm.IsActivated);
        Assert.Equal(Now.AddYears(1), service.Verdict.Payload!.ExpiresAt);
    }

    /// <summary>
    /// ⭐ Moving a machine onto a DIFFERENT licence is legitimate — but it is a decision, not a default, so it
    /// takes an explicit confirmation rather than silently replacing what is installed.
    /// </summary>
    [Fact]
    public void ADifferentLicence_NeedsAnExplicitReplace()
    {
        var installed = _fixtures.Issue(Now, Now.AddDays(-1), Now.AddYears(1), "lid-one");
        var other = _fixtures.Issue(Now, Now.AddDays(-1), Now.AddYears(1), "lid-two");

        var vm = Activation(out var service, existing: installed);
        vm.PasteText = other;

        vm.ActivateCommand.Execute(null);

        Assert.True(vm.NeedsReplaceConfirmation);
        Assert.False(vm.IsActivated);
        Assert.Equal("lid-one", service.Verdict.Payload!.LicenseId);

        vm.ReplaceCommand.Execute(null);

        Assert.True(vm.IsActivated);
        Assert.Equal("lid-two", service.Verdict.Payload!.LicenseId);
    }

    /// <summary>
    /// ⭐ A post-dated renewal is STORED even though it is not yet usable — refusing it would make renewing
    /// early impossible, which is exactly when a well-organised customer renews (L4a decision 2).
    /// </summary>
    [Fact]
    public void APostDatedRenewal_IsAccepted_EvenThoughItIsNotYetValid()
    {
        var vm = Activation(out var service);
        vm.PasteText = _fixtures.Issue(Now, Now.AddDays(30), Now.AddYears(1));

        vm.ActivateCommand.Execute(null);

        Assert.True(vm.IsActivated);
        Assert.Equal(LicenseStatus.NotYetValid, service.Verdict.Status);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private LicenseActivationViewModel Activation(out LicenseService service, string? existing = null)
    {
        if (existing is not null)
        {
            File.WriteAllText(InstalledPath, existing);
        }

        service = new LicenseService(
            new LicenseLocation(UserDirectory, MachineDirectory),
            new ApplicationSettingsStore(SettingsDirectory),
            _fixtures.TrustedKeys,
            () => Now);

        service.Refresh();
        return new LicenseActivationViewModel(service);
    }

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
