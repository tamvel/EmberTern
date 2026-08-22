using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.App;
using EmberTern.App.Controls;
using EmberTern.App.Licensing;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Core.Settings;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// L4b — the licence surfaces as they actually render.
///
/// <para>⚠⚠ <b>Every test here returns its <c>Task</c> and awaits <c>Dispatch</c>.</b> Gotcha #374: the
/// expression-bodied <c>void</c> form compiles while DISCARDING the task, so xUnit never awaits it and no
/// assertion in the body can fail the test. Five such tests shipped in the License Manager and one stage's UI
/// claims rested on them. ⭐ This file was proved alive by injecting <c>Assert.Fail</c> into a body and
/// watching it go red.</para>
///
/// <para>⚠ One shared <c>HeadlessUnitTestSession</c> per process, through <c>HeadlessCollection</c> — never a
/// per-class <c>IClassFixture</c>, which silently creates a second session (#94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class LicenseSurfaceViewTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly HeadlessUnitTestSession _session;
    private readonly LicenseFixtures _fixtures = new();

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "etlic-view", Guid.NewGuid().ToString("N"));

    public LicenseSurfaceViewTests(HeadlessSessionFixture fixture)
    {
        _session = fixture.Session;
        Directory.CreateDirectory(MachineDirectory);
        Directory.CreateDirectory(SettingsDirectory);
    }

    private string MachineDirectory => Path.Combine(_root, "machine");

    private string SettingsDirectory => Path.Combine(_root, "settings");

    // ── The activation window ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The window opens quiet: no banner, and no Replace button — that one appears only when a DIFFERENT
    /// licence id is offered, because replacing is a decision rather than a default (§16.4).
    /// </summary>
    [Fact]
    public async Task TheActivationWindow_OpensWithNothingToSay()
    {
        await _session.Dispatch(() =>
        {
            var window = new LicenseActivationWindow(Service(null));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.False(Banner(window).IsVisible);
            Assert.False(ButtonNamed(window, "ReplaceButton").IsVisible);
            Assert.True(ButtonNamed(window, "ActivateButton").IsVisible);
        }, CancellationToken.None);
    }

    /// <summary>
    /// ⭐ The rendered banner carries the view model's sentence — the binding, not just the view model. This
    /// is the half that broke in Phase 5: a correct value nothing displayed.
    /// </summary>
    [Fact]
    public async Task ARefusedArtifact_PutsItsExplanationOnTheRenderedBanner()
    {
        await _session.Dispatch(() =>
        {
            var window = new LicenseActivationWindow(Service(null));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var vm = Assert.IsType<LicenseActivationViewModel>(window.DataContext);
            vm.PasteText = "not a licence";
            vm.ActivateCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var banner = Banner(window);

            Assert.True(banner.IsVisible);
            Assert.Equal(MessageSeverity.Error, banner.Severity);
            Assert.Equal(vm.Message, banner.Message);
            Assert.False(string.IsNullOrWhiteSpace(banner.Message));
        }, CancellationToken.None);
    }

    /// <summary>The Replace button appears exactly when the offered licence carries a different id.</summary>
    [Fact]
    public async Task OfferingADifferentLicence_RevealsTheReplaceButton()
    {
        await _session.Dispatch(() =>
        {
            var installed = _fixtures.Issue(Now, Now.AddDays(-1), Now.AddYears(1), "lid-installed");
            var window = new LicenseActivationWindow(Service(installed));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var vm = Assert.IsType<LicenseActivationViewModel>(window.DataContext);
            vm.PasteText = _fixtures.Issue(Now, Now.AddDays(-1), Now.AddYears(1), "lid-other");
            vm.ActivateCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(ButtonNamed(window, "ReplaceButton").IsVisible);
        }, CancellationToken.None);
    }

    // ── Settings ▸ Licence ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Licence page renders the verdict and the licence's own facts, and it is reachable like any other
    /// category — including when the licence is one that would block the application.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheLicencePage_RendersTheVerdictAndTheFacts(bool expired)
    {
        await _session.Dispatch(() =>
        {
            var directory = NewCaseDirectory();
            var licence = expired
                ? _fixtures.Issue(Now.AddYears(-2), Now.AddYears(-2), Now.AddDays(-60))
                : _fixtures.Valid(Now);

            var preferences = new PreferencesService(new PreferencesStore(directory));
            var window = new SettingsWindow(
                preferences,
                new SettingsPortability(new ApplicationSettingsStore(directory), preferences, "9.9.9-test"),
                Service(licence),
                SettingsCatalog.CategoryLicense);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var vm = Assert.IsType<SettingsCenterViewModel>(window.DataContext);
            Assert.True(vm.IsLicensePageVisible);

            var banner = window.GetVisualDescendants().OfType<MessageBanner>()
                .Single(b => b.Name == "LicenseStatusBanner");

            Assert.Equal(vm.LicensePage.StatusExplanation, banner.Message);
            Assert.False(string.IsNullOrWhiteSpace(banner.Message));
            Assert.Equal(
                expired ? MessageSeverity.Error : MessageSeverity.Success,
                banner.Severity);

            // ⭐ An expired licence still shows its licensee and dates — that is exactly what support asks for.
            Assert.True(vm.LicensePage.HasDetails);
            Assert.Equal(LicenseFixtures.Licensee, vm.LicensePage.Licensee);

            var rendered = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            Assert.Contains(LicenseFixtures.Licensee, rendered);
        }, CancellationToken.None);
    }

    /// <summary>
    /// ⭐⭐ The way OUT of a blocked state: Update licence is on screen and enabled whatever the verdict is.
    /// A gate that also hid the screen for fixing the licence would be a trap (§7).
    /// </summary>
    [Fact]
    public async Task TheLicencePage_OffersUpdateLicence_EvenWithNoLicenceAtAll()
    {
        await _session.Dispatch(() =>
        {
            var directory = NewCaseDirectory();
            var preferences = new PreferencesService(new PreferencesStore(directory));
            var window = new SettingsWindow(
                preferences,
                new SettingsPortability(new ApplicationSettingsStore(directory), preferences, "9.9.9-test"),
                Service(null),
                SettingsCatalog.CategoryLicense);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var update = ButtonNamed(window, "UpdateLicenseButton");

            Assert.True(update.IsVisible);
            Assert.True(update.IsEffectivelyEnabled);

            // ⚠ Copy licence id is hidden with no payload — a button that would copy an empty string is worse
            //   than no button. Copy details stays, because the details are what support asks for.
            Assert.False(ButtonNamed(window, "CopyLicenseIdButton").IsVisible);
            Assert.True(ButtonNamed(window, "CopyLicenseDetailsButton").IsVisible);
        }, CancellationToken.None);
    }

    /// <summary>The Licence category is in the navigation, from the catalog like every other one.</summary>
    [Fact]
    public async Task TheLicenceCategory_IsInTheNavigation()
    {
        await _session.Dispatch(() =>
        {
            var directory = NewCaseDirectory();
            var preferences = new PreferencesService(new PreferencesStore(directory));
            var window = new SettingsWindow(
                preferences,
                new SettingsPortability(new ApplicationSettingsStore(directory), preferences, "9.9.9-test"),
                Service(_fixtures.Valid(Now)));

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var vm = Assert.IsType<SettingsCenterViewModel>(window.DataContext);

            Assert.Contains(vm.Categories, c => c.Id == SettingsCatalog.CategoryLicense);
            Assert.Equal(UiStrings.SettingsCategoryLicense,
                vm.Categories.Single(c => c.Id == SettingsCatalog.CategoryLicense).Title);
        }, CancellationToken.None);
    }

    // ── The status bar ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The refusal fits the status bar, measured with the engine the product lays out with.</b>
    ///
    /// <para>⚠⚠ <b>This test exists because the first version did not fit.</b> The refusal was originally the
    /// verdict's full explanation plus a second sentence repeating what to do — about 250 characters — and the
    /// user saw it running: ellipsised across the whole window, reading as a technical dump, and repeating the
    /// banner above it word for word. ⭐ The status bar states what is BLOCKED; the banner and the activation
    /// window say what to DO.</para>
    ///
    /// <para>⭐ It states the PROPERTY — "nothing is cut" — rather than a character count, and it measures the
    /// real <c>MainWindow</c> at a typical width in BOTH languages, because Polish is the longer one and a
    /// budget derived from English would pass while the shipped language clipped. ⛔ Do not weaken this into a
    /// length assertion: a longer translation is exactly what would break the property while satisfying the
    /// number. Same method as <c>ConfirmDialogLayoutTests</c>.</para>
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("pl")]
    public async Task TheConnectionRefusal_FitsTheStatusBar_AtATypicalWindowWidth(string language)
    {
        // ⚠ Read out of the shipped .resx rather than switching the app's language: Loc.Apply mutates
        //   PROCESS-GLOBAL state and broadcasts to every live subscriber, and the text is what is being
        //   measured — taking the text and leaving the global state alone is both safer and more direct
        //   (the reasoning ConfirmDialogLayoutTests records).
        var longest = LongestShippedRefusal(language);
        Assert.False(string.IsNullOrWhiteSpace(longest));

        await _session.Dispatch(() =>
        {
            var profiles = NewCaseDirectory();
            using var connections = new EmberTern.Firebird.FirebirdConnectionService();
            using var transactions = new EmberTern.Firebird.TransactionService(connections);

            var vm = new MainWindowViewModel(
                new EmberTern.Core.Connections.ConnectionProfileStore(profiles),
                connections,
                transactions,
                Service(null));

            var window = new MainWindow { DataContext = vm };

            // 1280×800 — the smallest desktop the design work treats as typical (product-polish M5's
            // 1366×768 working area is wider still, so this is the tighter of the two).
            window.Width = 1280;
            window.Height = 800;
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // ⚠ Set on the view model exactly as `SetError` does — message AND severity, because the severity
            //   icon is a sibling in the same row and takes width the label then does not get.
            //   ⛔ No test-only seam was added to the product for this: these are the two properties the
            //   status bar binds, and driving them is driving the surface.
            vm.StatusMessage = longest;
            vm.StatusMessageSeverity = MessageSeverity.Error;
            Dispatcher.UIThread.RunJobs();

            window.Measure(new Avalonia.Size(window.Width, window.Height));
            window.Arrange(new Avalonia.Rect(0, 0, window.Width, window.Height));
            Dispatcher.UIThread.RunJobs();

            var label = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text == longest);

            Assert.True(label.IsVisible);

            // ⚠⚠ MEASURED AGAINST THE COLUMN, NOT AGAINST THE LABEL'S OWN BOUNDS — and the first version of
            //    this test got that wrong, passed on the 250-character sentence the user had just reported as
            //    cut, and proved nothing. The message row is a HORIZONTAL StackPanel, which hands its children
            //    their full desired width unconditionally; the label is therefore never "given" less than it
            //    wants, `Bounds.Width == DesiredSize.Width` always, and the overflow simply runs off the end
            //    of the flexible column. ⭐ What decides whether the user sees the whole sentence is the width
            //    of column 1 of the status-bar grid, so that is what this compares against.
            var row = Assert.IsType<StackPanel>(label.Parent);
            var grid = row.GetVisualAncestors().OfType<Grid>()
                .First(g => g.ColumnDefinitions.Count == 4);

            // A fresh UNCONSTRAINED measure: what the row WANTS, margin and severity icon included. Measuring
            // inside the layout pass answers a different question.
            row.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
            var wanted = row.DesiredSize.Width;
            var available = grid.ColumnDefinitions[1].ActualWidth;

            // ⚠ Measured 2026-08-15 at 1280 px, disconnected: the row needs 841 px in English and 874 px in
            //   Polish against 1081 px of column — about 200 px of headroom, which is roughly 35 characters
            //   of connection name in column 0 before it starts to bite. The sentence the user reported cut
            //   needed 2 854 px, i.e. 2.6× the column.
            Assert.True(available > 0, "The status bar was not laid out, so this measured nothing.");
            Assert.True(
                wanted <= available + 0.5,
                $"[{language}] the licence refusal does not fit the status bar: the message row needs "
                + $"{wanted:F1} px and column 1 gives it {available:F1} px at a {window.Width:F0} px window. "
                + $"The text was: \"{longest}\". Shorten the sentence — do NOT widen the window, shrink the "
                + "font, or lean on the ellipsis.");

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>The longest refusal a shipped language declares — the one that decides whether the row fits.</summary>
    private static string LongestShippedRefusal(string language)
    {
        var file = language == "en" ? "Strings.resx" : $"Strings.{language}.resx";
        var path = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Localization", file);

        return System.Xml.Linq.XDocument.Load(path).Root!
            .Elements("data")
            .Where(d => (d.Attribute("name")?.Value ?? string.Empty).StartsWith("LicenseRefused", StringComparison.Ordinal))
            .Select(d => d.Element("value")!.Value)
            .OrderByDescending(v => v.Length)
            .First();
    }

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

    // ── About ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// About names the licensee, and the Debug marker follows the build configuration — it is not in a
    /// <c>Release</c> binary at all, because <c>GateEnabled</c> is a compile-time <c>const</c> (§16.5).
    /// </summary>
    [Fact]
    public async Task About_RendersTheLicenceLine_AndTheDebugMarkerFollowsTheBuild()
    {
        await _session.Dispatch(() =>
        {
            var window = new AboutWindow(Service(_fixtures.Valid(Now)));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var vm = Assert.IsType<AboutViewModel>(window.DataContext);
            var texts = window.GetVisualDescendants().OfType<TextBlock>().ToList();

            var licence = texts.Single(t => t.Text == vm.LicensedToText);
            Assert.True(licence.IsVisible);
            Assert.Contains(LicenseFixtures.Licensee, licence.Text!, StringComparison.Ordinal);

            var marker = texts.Single(t => t.Text == vm.DebugGateMarker);
#if DEBUG
            Assert.True(marker.IsVisible);
#else
            Assert.False(marker.IsVisible);
#endif
        }, CancellationToken.None);
    }

    /// <summary>With no licence the About line is absent entirely — never a label with nothing after it.</summary>
    [Fact]
    public async Task About_HidesTheLicenceLine_WhenThereIsNoLicence()
    {
        await _session.Dispatch(() =>
        {
            var window = new AboutWindow(Service(null));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var vm = Assert.IsType<AboutViewModel>(window.DataContext);

            Assert.False(vm.HasLicensee);
            Assert.DoesNotContain(
                window.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsVisible && t.Text == vm.LicensedToText && !string.IsNullOrEmpty(t.Text));
        }, CancellationToken.None);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static MessageBanner Banner(Window window)
        => window.GetVisualDescendants().OfType<MessageBanner>().Single(b => b.Name == "ActivationBanner");

    private static Button ButtonNamed(Window window, string name)
        => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private string NewCaseDirectory()
    {
        var directory = Path.Combine(_root, "case-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private LicenseService Service(string? licence)
    {
        var directory = NewCaseDirectory();

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
