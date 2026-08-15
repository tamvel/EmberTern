using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmberTern.App.Licensing;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Settings;
using EmberTern.Firebird;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>L4b — Architecture rule 12 for every licence SURFACE, pinned the way Phase 5 taught us to pin it.</b>
///
/// <para>⚠⚠ <b>The failure mode is not a missing entry — it is a PERFECT entry that nothing reads.</b> Phase 5
/// shipped correct Polish and English for the charset guard and still showed a Polish user a fully English
/// paragraph, with a green build and green tests, because the value was wrapped on the way out and the display
/// site read <c>ex.Message</c>. Design §17.3 says licensing has the identical shape and will repeat it if it
/// is not planned against.</para>
///
/// <para>⭐ <b>So nothing here reads a resource key.</b> Every assertion goes through the property the XAML
/// actually binds — <c>LicenseActivationViewModel.Message</c>, <c>LicenseSettingsViewModel.StatusExplanation</c>,
/// <c>MainWindowViewModel.LicenseBannerMessage</c>, <c>AboutViewModel.LicensedToText</c> — in BOTH languages,
/// and asserts the two differ. A translated resource nobody resolves therefore fails here rather than reaching
/// a customer. <c>LicenseTextTests</c> does the same for the verdict vocabulary L4a shipped; this covers the
/// surfaces L4b added on top of it.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class LicenseSurfaceLocalizationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "etlic-l10n", Guid.NewGuid().ToString("N"));

    private readonly LicenseFixtures _fixtures = new();

    public LicenseSurfaceLocalizationTests()
    {
        Directory.CreateDirectory(UserDirectory);
        Directory.CreateDirectory(MachineDirectory);
        Directory.CreateDirectory(SettingsDirectory);
    }

    private string UserDirectory => Path.Combine(_root, "user");

    private string MachineDirectory => Path.Combine(_root, "machine");

    private string SettingsDirectory => Path.Combine(_root, "settings");

    // ── The connection refusal ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The sentence a blocked connection shows, resolved through <see cref="LicenseText.ConnectionRefused"/> —
    /// the member every refusal site calls — for every state that can refuse.
    ///
    /// <para>⚠⚠ <b>The length assertion runs the other way now, and that is the user's correction of
    /// 2026-08-15.</b> This used to demand a LONG refusal (Explain plus a second sentence). Seen running, it
    /// was ~250 characters in the status bar: ellipsised, reading as a technical dump, and repeating the
    /// banner above it word for word. ⭐ The status bar says what is BLOCKED; the banner and the activation
    /// window say what to DO. <c>TheConnectionRefusal_FitsTheStatusBar</c> measures the consequence.</para>
    /// </summary>
    [Theory]
    [InlineData(LicenseStatus.Expired)]
    [InlineData(LicenseStatus.Unlicensed)]
    [InlineData(LicenseStatus.NotYetValid)]
    [InlineData(LicenseStatus.Invalid)]
    [InlineData(LicenseStatus.VersionNotCovered)]
    public void TheConnectionRefusal_IsWrittenInBothLanguages(LicenseStatus status)
    {
        var verdict = RefusingVerdict(status);

        var english = InLanguage("en", () => LicenseText.ConnectionRefused(verdict));
        var polish = InLanguage("pl", () => LicenseText.ConnectionRefused(verdict));

        Assert.NotEqual(english, polish);
        foreach (var sentence in new[] { english, polish })
        {
            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.DoesNotContain("{", sentence, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// ⭐ Each state gets its OWN refusal: an expired licence and one this build cannot read call for
    /// different actions, and a single sentence covering both would say neither.
    /// </summary>
    [Fact]
    public void EachRefusingState_HasItsOwnSentence()
    {
        var states = new[]
        {
            LicenseStatus.Expired, LicenseStatus.Unlicensed,
            LicenseStatus.NotYetValid, LicenseStatus.Invalid, LicenseStatus.VersionNotCovered,
        };

        var verdicts = states.Select(RefusingVerdict).ToList();
        var sentences = new List<string>();

        InLanguage("pl", () =>
        {
            sentences.AddRange(verdicts.Select(LicenseText.ConnectionRefused));
            return string.Empty;
        });

        Assert.Equal(states.Length, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// ⭐⭐ <b>The guard that would have caught the Phase-5 defect at the source.</b>
    ///
    /// <para>Every site that catches a <c>LicenseBlockedException</c> must render the VERDICT through
    /// <c>LicenseText</c>. ⛔ Reading <c>ex.Message</c> would compile, look right in English to an English
    /// reader, and ship an untranslated developer breadcrumb to a Polish customer — with every localization
    /// test still green, because the resource entry would be perfect and simply unread.</para>
    ///
    /// <para>⚠ Comments are stripped first (design §37.2): this rule's own documentation names the member it
    /// forbids, and a guard that fires on the prose explaining it is a guard that gets suppressed.</para>
    /// </summary>
    [Fact]
    public void NoRefusalSite_RendersTheExceptionMessageInsteadOfTheVerdict()
    {
        var offenders = new List<string>();
        var sites = 0;

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot(), "src", "EmberTern.App"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            var text = StripComments(File.ReadAllText(file));

            foreach (Match m in Regex.Matches(
                         text,
                         @"catch\s*\(\s*(?:[\w.]*\.)?LicenseBlockedException\s+(?<var>\w+)\s*\)\s*\{(?<body>[^}]*)\}",
                         RegexOptions.Singleline))
            {
                sites++;
                var body = m.Groups["body"].Value;
                var variable = m.Groups["var"].Value;

                if (Regex.IsMatch(body, $@"\b{Regex.Escape(variable)}\s*\.\s*Message\b"))
                {
                    offenders.Add($"{Path.GetFileName(file)} reads {variable}.Message");
                }

                if (!body.Contains("LicenseText.", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} does not resolve the verdict through LicenseText");
                }
            }
        }

        // ⭐⭐ The anti-vacuity assertion, and it is the important one. A regex that silently matched NOTHING
        //    would report perfect compliance forever — the exact shape of the L4a finding where a tampering
        //    test mutated nothing and reported the absence of a failure as a success (§37.3). Connect, Test
        //    connection and the import session each catch this exception today.
        Assert.True(sites >= 3,
            $"Only {sites} refusal sites were found, so this guard is not looking at the code it claims to "
            + "guard. Either the catches moved, or the pattern stopped matching them.");

        Assert.True(offenders.Count == 0,
            "A licence refusal must be rendered from the VERDICT at display time, never from the exception's "
            + "own message — that one is an untranslated developer breadcrumb (design §17.3):\n  "
            + string.Join("\n  ", offenders));
    }

    // ── The activation window ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every message the activation window can show, driven through the view model exactly as a user drives
    /// it, in both languages.
    /// </summary>
    [Theory]
    [InlineData("nothing")]
    [InlineData("garbage")]
    [InlineData("notNewer")]
    [InlineData("differentLicence")]
    [InlineData("unreadableFile")]
    [InlineData("installed")]
    public void EveryActivationMessage_IsWrittenInBothLanguages(string scenario)
    {
        var english = InLanguage("en", () => RunActivation(scenario));
        var polish = InLanguage("pl", () => RunActivation(scenario));

        Assert.False(string.IsNullOrWhiteSpace(english), $"{scenario} says nothing in English.");
        Assert.False(string.IsNullOrWhiteSpace(polish), $"{scenario} says nothing in Polish.");

        // ⭐ The assertion that catches "a perfect entry nothing reads".
        Assert.NotEqual(english, polish);
        Assert.DoesNotContain("{0}", polish, StringComparison.Ordinal);
    }

    // ── Settings ▸ Licence ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheLicencePage_SpeaksBothLanguages()
    {
        var service = Service(_fixtures.Valid(Now));

        foreach (var read in new Func<LicenseSettingsViewModel, string>[]
                 {
                     p => p.StatusHeadline,
                     p => p.StatusExplanation,
                 })
        {
            var english = InLanguage("en", () => read(new LicenseSettingsViewModel(service)));
            var polish = InLanguage("pl", () => read(new LicenseSettingsViewModel(service)));

            Assert.NotEqual(english, polish);
        }
    }

    /// <summary>
    /// ⭐ The page is built ONCE and re-read after the language changes — the shape that actually ships,
    /// and the one gotcha #353 is about: a value assigned in the constructor stays in the language the
    /// window opened in while everything around it moves.
    /// </summary>
    [Fact]
    public void TheLicencePage_FollowsALanguageChange_WithoutBeingRebuilt()
    {
        var page = new LicenseSettingsViewModel(Service(_fixtures.Valid(Now)));

        var english = InLanguage("en", () => page.StatusExplanation);
        var polish = InLanguage("pl", () => page.StatusExplanation);

        Assert.NotEqual(english, polish);
    }

    // ── About ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheAboutLicenceLine_SpeaksBothLanguages()
    {
        var about = new AboutViewModel(Service(_fixtures.Valid(Now)));

        var english = InLanguage("en", () => about.LicensedToText);
        var polish = InLanguage("pl", () => about.LicensedToText);

        Assert.NotEqual(english, polish);
        foreach (var line in new[] { english, polish })
        {
            Assert.Contains(LicenseFixtures.Licensee, line, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", line, StringComparison.Ordinal);
        }
    }

    // ── The main-window banner ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The banner through the property <c>MainWindow.axaml</c> binds, on a real
    /// <see cref="MainWindowViewModel"/> — not through <c>LicenseText</c> directly, because "the property is
    /// wired to the right member" is exactly the half that broke in Phase 5.
    /// </summary>
    [Fact]
    public void TheMainWindowBanner_SpeaksBothLanguages_AndOnlyShowsWhenItHasSomethingToSay()
    {
        var profiles = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(profiles);

        using var service = new FirebirdConnectionService();
        using var transactions = new TransactionService(service);

        var expired = new MainWindowViewModel(
            new ConnectionProfileStore(profiles), service, transactions, Service(Expired()));

        Assert.True(expired.ShowLicenseBanner);
        Assert.Equal(EmberTern.App.Controls.MessageSeverity.Error, expired.LicenseBannerSeverity);
        Assert.False(expired.LicenseBannerIsDismissible);

        var english = InLanguage("en", () => expired.LicenseBannerMessage);
        var polish = InLanguage("pl", () => expired.LicenseBannerMessage);
        Assert.NotEqual(english, polish);

        // ⭐ A comfortably valid licence says NOTHING — no nag, no "you are licensed" confirmation (§17.1).
        var quiet = new MainWindowViewModel(
            new ConnectionProfileStore(profiles), service, transactions, Service(_fixtures.Valid(Now)));

        Assert.False(quiet.ShowLicenseBanner);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Drives the activation view model into one message state and answers what it says.</summary>
    private string RunActivation(string scenario)
    {
        var directory = Path.Combine(_root, "case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var installed = scenario is "notNewer" or "differentLicence"
            ? _fixtures.Issue(Now, Now.AddDays(-1), Now.AddYears(1), "lid-installed")
            : null;

        if (installed is not null)
        {
            File.WriteAllText(Path.Combine(directory, LicenseConstants.StoredFileName), installed);
        }

        var service = new LicenseService(
            new LicenseLocation(directory, MachineDirectory),
            new ApplicationSettingsStore(SettingsDirectory),
            _fixtures.TrustedKeys,
            () => Now);
        service.Refresh();

        var vm = new LicenseActivationViewModel(service);

        switch (scenario)
        {
            case "nothing":
                break;
            case "garbage":
                vm.PasteText = "not a licence at all";
                break;
            case "notNewer":
                vm.PasteText = installed!;
                break;
            case "differentLicence":
                vm.PasteText = _fixtures.Issue(Now, Now.AddDays(-1), Now.AddYears(1), "lid-other");
                break;
            case "unreadableFile":
                vm.OfferFile(Path.Combine(directory, "no-such-file.etlic"));
                return vm.Message;
            default:
                vm.PasteText = _fixtures.Valid(Now);
                break;
        }

        vm.ActivateCommand.Execute(null);
        return vm.Message;
    }

    private string Expired() => _fixtures.Issue(Now.AddYears(-2), Now.AddYears(-2), Now.AddDays(-60));

    /// <summary>A really-signed licence verified into the requested refusing state.</summary>
    private LicenseVerdict RefusingVerdict(LicenseStatus status) => status switch
    {
        LicenseStatus.Expired => Verdict(Expired()),
        LicenseStatus.Unlicensed => LicenseVerdict.Unlicensed,
        LicenseStatus.NotYetValid => Verdict(_fixtures.Issue(Now, Now.AddDays(30), Now.AddYears(1))),
        LicenseStatus.VersionNotCovered => Verdict(
            _fixtures.Issue(Now, Now.AddDays(-1), Now.AddYears(1), maintenanceUntil: Now.AddYears(-5))),
        _ => Verdict(LicenseFixtures.Tamper(_fixtures.Valid(Now))),
    };

    private LicenseVerdict Verdict(string licence) => Service(licence).Verdict;

    private LicenseService Service(string? licence)
    {
        var directory = Path.Combine(_root, "svc-" + Guid.NewGuid().ToString("N"));
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
    /// ⚠ Restores the previous language in a <c>finally</c>. <c>Loc.Apply</c> mutates PROCESS-GLOBAL state and
    /// broadcasts to every live subscriber, which is why this class joins <c>HeadlessCollection</c>.
    /// </summary>
    private static string InLanguage(string language, Func<string> read)
    {
        var previous = Loc.Culture.TwoLetterISOLanguageName;
        try
        {
            Loc.Apply(language);
            return read();
        }
        finally
        {
            Loc.Apply(previous);
        }
    }

    private static string StripComments(string source) =>
        Regex.Replace(
            Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
            @"//[^\r\n]*", string.Empty);

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate the repository root from the test binary.");
        return dir!.FullName;
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
