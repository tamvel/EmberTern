using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.App;
using EmberTern.App.Controls;
using EmberTern.App.Settings;
using EmberTern.App.Views;
using EmberTern.Core.Settings;
using EmberTern.Core.Settings.Export;
using EmberTern.App.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace EmberTern.Tests;

/// <summary>
/// Settings Center etap 3 — the REAL window, with its real compiled bindings and the app's real styles.
///
/// <para>These exist because the view-model tests cannot see the half that broke in every previous sprint:
/// whether the options a page renders actually come from Core's catalog, and whether selecting one reaches the
/// file. "Added" is not "paints" (gotcha #251), and a compiled binding that hands a control nothing fails
/// silently.</para>
///
/// <para>⚠ It joins <see cref="HeadlessCollection"/> and never adds its own class fixture — xunit creates an
/// <c>IClassFixture</c> once per test CLASS, and a second <c>HeadlessUnitTestSession</c> in one process is what
/// gotchas #94 / #226 / #286 forbid.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SettingsCenterViewTests
{
    private readonly HeadlessUnitTestSession _session;
    private readonly ITestOutputHelper _out;

    public SettingsCenterViewTests(HeadlessSessionFixture fixture, ITestOutputHelper output)
    {
        _session = fixture.Session;
        _out = output;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-settings-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SettingsPortability PortabilityOver(
        string dir, PreferencesService service, EmberTern.Core.Security.SecretProtector? protector = null)
        => new(new ApplicationSettingsStore(dir, protector), service, "9.9.9-test");

    /// <summary>
    /// The window renders the theme radios and the language list from Core's option sets, with UiStrings'
    /// words — and selecting a radio commits through the one service to the shared settings file.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ThePageRendersCoresOptions_AndSelectingOneReachesTheFile()
    {
        await _session.Dispatch(() =>
        {
            var dir = NewTempDir();
            try
            {
                var service = new PreferencesService(new PreferencesStore(dir));
                var window = new SettingsWindow(service, PortabilityOver(dir, service));
                window.Show();
                Dispatcher.UIThread.RunJobs();

                // ⚠ Scoped by GroupName, not "every radio in the window": since etap 4 there is a second
                // page with radios of its own. Filtering by group makes this assertion independent of
                // whether a hidden page realises its ItemsControl items (it does not today — incidental).
                var radios = window.GetVisualDescendants().OfType<RadioButton>()
                    .Where(r => r.GroupName == "SettingsTheme").ToList();
                _out.WriteLine("theme radios: " + string.Join(" | ", radios.Select(r => $"{r.Content}={r.IsChecked}")));

                // One radio per Core option — never a hand-typed pair in XAML (design §5.2.2).
                Assert.Equal(PreferenceOptions.Theme.Values.Count, radios.Count);
                Assert.Contains(radios, r => Equals(r.Content, UiStrings.SettingsThemeDark));
                Assert.Contains(radios, r => Equals(r.Content, UiStrings.SettingsThemeLight));

                // The stored value is what is checked on open — Dark on a fresh install.
                var dark = radios.Single(r => Equals(r.Content, UiStrings.SettingsThemeDark));
                var light = radios.Single(r => Equals(r.Content, UiStrings.SettingsThemeLight));
                Assert.True(dark.IsChecked);
                Assert.False(light.IsChecked);

                // The language list is a real, enabled control over a real catalog with one row today.
                var languages = window.GetVisualDescendants().OfType<ComboBox>()
                    .Single(c => c.Name == "LanguageBox");
                Assert.True(languages.IsEnabled);
                Assert.Equal(PreferenceOptions.Language.Values.Count, languages.ItemCount);
                Assert.NotNull(languages.SelectedItem);

                // Apply on change: no OK button, and after one click the file already holds it.
                light.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(PreferenceOptions.ThemeLight, service.Current.Theme);
                Assert.Equal(PreferenceOptions.ThemeLight, new PreferencesStore(dir).Load().Theme);
                Assert.False(dark.IsChecked);

                window.Close();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }, System.Threading.CancellationToken.None);
    }

    /// <summary>
    /// Search filters what is on screen, and an empty result explains itself instead of leaving a blank pane.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task SearchFiltersTheRenderedRows_AndExplainsAnEmptyResult()
    {
        await _session.Dispatch(() =>
        {
            var dir = NewTempDir();
            try
            {
                var service = new PreferencesService(new PreferencesStore(dir));
                var window = new SettingsWindow(service, PortabilityOver(dir, service));
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var groups = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("settings-group")).ToList();

                // Every category's rows live in the visual tree; only the selected page's are RENDERED.
                // ⚠ IsEffectivelyVisible, not IsVisible: a row on a hidden page still has its own
                // IsVisible == true (its search filter matched), so IsVisible alone would count all four
                // and this test would stop measuring what it is named for.
                // General: Theme, Language, Import/export. SQL Formatter: keyword case, identifier case.
                Assert.Equal(5, groups.Count);
                Assert.Equal(3, groups.Count(g => g.IsEffectivelyVisible));

                var search = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchBox");
                var categories = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "CategoryList");

                search.Text = "colour";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, groups.Count(g => g.IsEffectivelyVisible));
                Assert.Equal(1, categories.ItemCount);

                search.Text = "zzzz-no-such-setting";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, categories.ItemCount);

                var empty = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => Equals(t.Text, UiStrings.SettingsNoMatch));
                Assert.True(empty.IsVisible);

                search.Text = string.Empty;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(3, groups.Count(g => g.IsEffectivelyVisible));
                Assert.False(empty.IsVisible);

                window.Close();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }, System.Threading.CancellationToken.None);
    }

    /// <summary>
    /// The refusal banner is the shared <c>MessageBanner</c>, docked, and it is genuinely on screen when the
    /// store declines to write — the one place in the app where that silence is wrong (design §5.5).
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ARefusedSave_ShowsTheSharedBanner()
    {
        await _session.Dispatch(() =>
        {
            var dir = NewTempDir();
            try
            {
                // A file this build cannot decrypt — DPAPI on the wrong Windows account. Save refuses over it.
                var readable = new EmberTern.Core.Security.SecretProtector(s => "ENC:" + s, s => s.Substring(4));
                new ApplicationSettingsStore(dir, readable).Save(new ApplicationSettings());
                var undecryptable = new EmberTern.Core.Security.SecretProtector(
                    s => "ENC:" + s,
                    _ => throw new InvalidOperationException("Key not valid for use in specified state."));

                var refusing = new PreferencesService(new PreferencesStore(dir, undecryptable));
                var window = new SettingsWindow(refusing, PortabilityOver(dir, refusing, undecryptable));
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var banner = window.GetVisualDescendants().OfType<MessageBanner>().Single();
                Assert.False(banner.IsVisible);

                var light = window.GetVisualDescendants().OfType<RadioButton>()
                    .Single(r => Equals(r.Content, UiStrings.SettingsThemeLight));
                light.IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.True(banner.IsVisible);
                Assert.Equal(MessageSeverity.Warning, banner.Severity);
                Assert.False(string.IsNullOrWhiteSpace(banner.Message));
                _out.WriteLine("banner: " + banner.Message);

                window.Close();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }, System.Threading.CancellationToken.None);
    }

    /// <summary>
    /// Etap 4 — the SQL Formatter page renders both casing rows from Core's ONE option set, and selecting an
    /// option reaches the file.
    ///
    /// <para>⭐ The load-bearing assertion is the LAST one: the two rows must be able to hold DIFFERENT values.
    /// Both draw on the same option set, so both render radios labelled the same way — and a RadioButton group
    /// is keyed by <c>GroupName</c>. Had the two rows shared one group name, checking "UPPER CASE" for keywords
    /// would silently uncheck the identifier row, and the two settings could never differ. That is a defect no
    /// view-model test can see.</para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task TheFormatterPage_RendersBothRows_AndTheyAreIndependent()
    {
        await _session.Dispatch(() =>
        {
            var dir = NewTempDir();
            try
            {
                var service = new PreferencesService(new PreferencesStore(dir));
                var window = new SettingsWindow(service, PortabilityOver(dir, service));
                window.Show();
                Dispatcher.UIThread.RunJobs();

                // Select the SQL Formatter category the way the user does.
                var categories = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "CategoryList");
                Assert.Equal(2, categories.ItemCount);
                categories.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();

                var keyword = window.GetVisualDescendants().OfType<RadioButton>()
                    .Where(r => r.GroupName == "SettingsFormatterKeywordCase").ToList();
                var identifier = window.GetVisualDescendants().OfType<RadioButton>()
                    .Where(r => r.GroupName == "SettingsFormatterIdentifierCase").ToList();
                _out.WriteLine("keyword: " + string.Join(" | ", keyword.Select(r => $"{r.Content}={r.IsChecked}")));

                // One radio per Core option, per row — never a hand-typed pair in XAML (design §5.2.2).
                Assert.Equal(PreferenceOptions.Casing.Values.Count, keyword.Count);
                Assert.Equal(PreferenceOptions.Casing.Values.Count, identifier.Count);
                Assert.Contains(keyword, r => Equals(r.Content, UiStrings.SettingsCaseLower));
                Assert.Contains(keyword, r => Equals(r.Content, UiStrings.SettingsCaseUpper));

                // A fresh install is lower/lower — the byte-identical-output default.
                Assert.True(keyword.Single(r => Equals(r.Content, UiStrings.SettingsCaseLower)).IsChecked);
                Assert.True(identifier.Single(r => Equals(r.Content, UiStrings.SettingsCaseLower)).IsChecked);

                // Apply on change: one click and the file already holds it.
                keyword.Single(r => Equals(r.Content, UiStrings.SettingsCaseUpper)).IsChecked = true;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(PreferenceOptions.CaseUpper, service.Current.FormatterKeywordCase);
                Assert.Equal(PreferenceOptions.CaseUpper, new PreferencesStore(dir).Load().FormatterKeywordCase);

                // ⭐ The two rows are independent groups — the identifier row did NOT follow.
                Assert.Equal(PreferenceOptions.CaseLower, service.Current.FormatterIdentifierCase);
                Assert.True(identifier.Single(r => Equals(r.Content, UiStrings.SettingsCaseLower)).IsChecked);

                window.Close();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }, System.Threading.CancellationToken.None);
    }

    /// <summary>
    /// The Avalonia half of the theme preference: the ONE mapping actually repaints the application.
    /// <para>⚠ The variant is restored afterwards — this collection shares one <c>Application</c>, and leaving
    /// it flipped would hand the next test a different palette.</para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ApplyingAThemePreference_RepaintsTheApplication()
    {
        await _session.Dispatch(() =>
        {
            var app = Application.Current;
            Assert.NotNull(app);
            var original = app!.RequestedThemeVariant;
            try
            {
                ThemePreference.Apply(PreferenceOptions.ThemeLight);
                Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);

                ThemePreference.Apply(PreferenceOptions.ThemeDark);
                Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);

                // Unrecognised falls back to Dark, never ThemeVariant.Default (which follows the OS theme).
                ThemePreference.Apply("chartreuse");
                Assert.Equal(ThemeVariant.Dark, app.RequestedThemeVariant);
            }
            finally
            {
                app!.RequestedThemeVariant = original;
            }
        }, System.Threading.CancellationToken.None);
    }

    // ─── Etap 5b — export / import ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The General page offers the three portability commands, and the Import / export row is searchable like any
    /// other — which is the whole reason an ACTION row is in the catalog rather than hand-placed in XAML
    /// (design §5.4).
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task TheGeneralPageOffersExportImportAndTheFolder_AndSearchFindsThem()
    {
        await _session.Dispatch(() =>
        {
            var dir = NewTempDir();
            try
            {
                var service = new PreferencesService(new PreferencesStore(dir));
                var window = new SettingsWindow(service, PortabilityOver(dir, service));
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
                Assert.Contains(buttons, b => b.Name == "ExportSettingsButton" && b.IsEffectivelyVisible);
                Assert.Contains(buttons, b => b.Name == "ImportSettingsButton" && b.IsEffectivelyVisible);
                Assert.Contains(buttons, b => b.Name == "OpenSettingsFolderButton" && b.IsEffectivelyVisible);

                // Searching a word the user would actually type: it is in the row's keywords, not its label.
                var search = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchBox");
                search.Text = "backup";
                Dispatcher.UIThread.RunJobs();

                var groups = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("settings-group")).ToList();
                Assert.Equal(1, groups.Count(g => g.IsEffectivelyVisible));
                Assert.Contains(buttons, b => b.Name == "ExportSettingsButton" && b.IsEffectivelyVisible);

                window.Close();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }, System.Threading.CancellationToken.None);
    }

    /// <summary>
    /// ⭐⭐ §6.3.3's corollary, seen from the UI: <b>a file rejected in phase one never produces a passphrase
    /// field.</b>
    ///
    /// <para>Three rejections, each a different cause with its own message: a PDF (not our file at all), a real
    /// <c>settings.dat</c> (ratified Q13's whole point — with a shared magic this would have been the file the
    /// user was asked for a passphrase about), and an export from a newer build. In all three the passphrase group
    /// must be invisible and the contents group must be invisible, because a passphrase prompt is an implicit
    /// claim that the file is readable given the right one.</para>
    ///
    /// <para>⚠ It asserts on the real window's real bindings rather than on the view model's booleans, because the
    /// defect this guards against is a passphrase box left visible by a binding nobody wired.</para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task APhaseOneRejection_NeverShowsThePassphraseField()
    {
        await _session.Dispatch(() =>
        {
            var dir = NewTempDir();
            try
            {
                var service = new PreferencesService(new PreferencesStore(dir));
                var portability = PortabilityOver(dir, service);

                // (a) not our file at all.
                var pdf = Path.Combine(dir, "brochure.pdf");
                File.WriteAllBytes(pdf, new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37, 0x0A, 0x00 });

                // (b) a genuine settings.dat — same product, different format.
                new ApplicationSettingsStore(dir).Save(new ApplicationSettings());
                var settingsDat = Path.Combine(dir, "settings.dat");

                // (c) ours, but from a build that knows more than we do.
                var future = Path.Combine(dir, "future" + SettingsExportFormat.FileExtension);
                File.WriteAllText(future,
                    SettingsExportFormat.Magic + "\t999\t9.9.9\taes256-passphrase\tPBKDF2-SHA256\t1000\tc2FsdA==\npayload");

                foreach (var (path, expected) in new[]
                         {
                             (pdf, SettingsImportStatus.NotAnExportFile),
                             (settingsDat, SettingsImportStatus.NotAnExportFile),
                             (future, SettingsImportStatus.FutureFormatVersion),
                         })
                {
                    Assert.Equal(expected, portability.Inspect(path).Status);

                    var dialog = new SettingsImportDialog(portability);
                    dialog.Show();
                    Dispatcher.UIThread.RunJobs();

                    var vm = (SettingsImportDialogViewModel)dialog.DataContext!;
                    vm.PickFile(path);
                    Dispatcher.UIThread.RunJobs();

                    var passphrase = dialog.GetVisualDescendants().OfType<Border>()
                        .Single(b => b.Name == "PassphraseGroup");
                    var contents = dialog.GetVisualDescendants().OfType<Border>()
                        .Single(b => b.Name == "ContentsGroup");
                    var banner = dialog.GetVisualDescendants().OfType<MessageBanner>().Single();
                    _out.WriteLine($"{Path.GetFileName(path)} → {banner.Message}");

                    Assert.False(passphrase.IsVisible);
                    Assert.False(contents.IsVisible);
                    Assert.True(banner.IsVisible);
                    Assert.Equal(MessageSeverity.Error, banner.Severity);
                    Assert.False(string.IsNullOrWhiteSpace(banner.Message));

                    dialog.Close();
                }
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }, System.Threading.CancellationToken.None);
    }

    /// <summary>
    /// The whole user journey through the real windows: export a file, then import it back and see the passphrase
    /// step arrive only after the file has been accepted, the contents listed, and the settings written.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task TheExportDialogWritesAFile_AndTheImportDialogTakesItBack()
    {
        await _session.Dispatch(() =>
        {
            var dir = NewTempDir();
            var target = NewTempDir();
            try
            {
                // A source installation with a theme worth carrying.
                var source = new PreferencesService(new PreferencesStore(dir));
                source.Apply(source.Current with { Theme = PreferenceOptions.ThemeLight });

                var file = Path.Combine(target, "carried" + SettingsExportFormat.FileExtension);
                var export = new SettingsExportDialog(PortabilityOver(dir, source));
                export.Show();
                Dispatcher.UIThread.RunJobs();

                var exportVm = (SettingsExportDialogViewModel)export.DataContext!;
                exportVm.Passphrase = "correct horse battery";
                // ⚠ The primary button stays disabled until the confirmation matches — a typo would produce a
                // permanently unreadable file, and this is the only moment it can be caught.
                var runButton = export.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportButton");
                Dispatcher.UIThread.RunJobs();
                Assert.False(runButton.IsEnabled);

                exportVm.PassphraseConfirmation = "correct horse battery";
                Dispatcher.UIThread.RunJobs();
                Assert.True(runButton.IsEnabled);

                exportVm.ExportTo(file);
                Assert.True(exportVm.Completed);
                Assert.True(File.Exists(file));
                export.Close();

                // A different installation, on Dark, imports it.
                var destination = new PreferencesService(new PreferencesStore(target));
                Assert.Equal(PreferenceOptions.ThemeDark, destination.Current.Theme);

                var import = new SettingsImportDialog(PortabilityOver(target, destination));
                import.Show();
                Dispatcher.UIThread.RunJobs();

                var importVm = (SettingsImportDialogViewModel)import.DataContext!;
                var passphraseGroup = import.GetVisualDescendants().OfType<Border>()
                    .Single(b => b.Name == "PassphraseGroup");
                var contentsGroup = import.GetVisualDescendants().OfType<Border>()
                    .Single(b => b.Name == "ContentsGroup");

                // Nothing is offered before a file is chosen.
                Assert.False(passphraseGroup.IsVisible);

                importVm.PickFile(file);
                Dispatcher.UIThread.RunJobs();
                Assert.True(passphraseGroup.IsVisible);
                Assert.False(contentsGroup.IsVisible);

                importVm.Passphrase = "correct horse battery";
                importVm.Open();
                Dispatcher.UIThread.RunJobs();
                Assert.True(contentsGroup.IsVisible);
                Assert.True(importVm.OffersPreferences);

                importVm.ApplySelected();
                Dispatcher.UIThread.RunJobs();
                Assert.True(importVm.Completed);

                // ⭐ Both halves: the file holds it, and the live service does too — the second is the trap, since
                // the import wrote settings.dat behind the service's back.
                Assert.Equal(PreferenceOptions.ThemeLight, new PreferencesStore(target).Load().Theme);
                Assert.Equal(PreferenceOptions.ThemeLight, destination.Current.Theme);

                import.Close();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            }
        }, System.Threading.CancellationToken.None);
    }

    /// <summary>
    /// ⭐⭐ <b>Etap 5b's first QA finding, asserted where it actually failed — on the painted control.</b>
    ///
    /// <para>Mistyping the confirmation disables Export and the dialog says why, but the reason was rendered in
    /// <c>SubtleForegroundBrush</c> — which is what <see cref="MessageSeverity.Info"/> looks like — so a genuine
    /// input error read as a hint and was routinely missed. A view-model test cannot see this: the string was
    /// always right. So this resolves the brush the <b>TextBlock</b> ended up with and requires it to be the very
    /// object <c>ErrorBrush</c> resolves to in the live theme.</para>
    ///
    /// <para>⚠ It also asserts the <i>other</i> direction — a passphrase that has merely not been typed yet is a
    /// Warning, not an Error. Painting every blocked state red would make the dialog red from the moment it opens,
    /// and a colour that is always on carries no information.</para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task AMistypedConfirmation_ReadsAsAnErrorAndNotAsAHint()
    {
        await _session.Dispatch(() =>
        {
            var dir = NewTempDir();
            try
            {
                var service = new PreferencesService(new PreferencesStore(dir));
                var dialog = new SettingsExportDialog(PortabilityOver(dir, service));
                dialog.Show();
                Dispatcher.UIThread.RunJobs();

                var vm = (SettingsExportDialogViewModel)dialog.DataContext!;
                var hint = dialog.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => ReferenceEquals(t.DataContext, vm)
                                 && t.Text == vm.Blocked.Text
                                 && !string.IsNullOrEmpty(t.Text));

                // Nothing typed yet: outstanding, not wrong.
                Assert.Equal(MessageSeverity.Warning, vm.Blocked.Severity);
                Assert.Equal(BrushIn(dialog, "WarningBrush"), hint.Foreground);

                vm.Passphrase = "correct horse battery";
                vm.PassphraseConfirmation = "correct horse batteru";
                Dispatcher.UIThread.RunJobs();

                var button = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportButton");
                Assert.False(button.IsEnabled);

                _out.WriteLine($"blocked: {vm.Blocked.Severity} — {vm.Blocked.Text}");
                Assert.Equal(UiStrings.SettingsExportPassphraseMismatch, vm.Blocked.Text);
                Assert.Equal(MessageSeverity.Error, vm.Blocked.Severity);

                // ⭐ The assertion the defect could not survive: the painted foreground IS the error brush, and is
                // no longer the subtle one every other secondary line uses.
                Assert.Equal(BrushIn(dialog, "ErrorBrush"), hint.Foreground);
                Assert.NotEqual(BrushIn(dialog, "SubtleForegroundBrush"), hint.Foreground);

                // Matching resolves the gate and the row disappears rather than lingering as stale advice.
                vm.PassphraseConfirmation = "correct horse battery";
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.IsEnabled);
                Assert.False(vm.Blocked.IsVisible);

                dialog.Close();
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }, System.Threading.CancellationToken.None);
    }

    /// <summary>
    /// ⚠ Etap 5b's second QA finding, pinned structurally: <b>both settings dialogs scroll their body.</b>
    ///
    /// <para>The window ceiling (<c>GrowingDialogBehavior</c>) needs a real desktop, so what a headless test can
    /// prove is the half that makes the ceiling survivable — that the body is inside a <see cref="ScrollViewer"/>
    /// and the footer buttons are OUTSIDE it, so capping the window scrolls the sections rather than clipping the
    /// primary button out of reach. A body without this pairing is the defect all over again on a shorter
    /// screen.</para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task BothSettingsDialogs_ScrollTheirBodyAndKeepTheirButtonsOutsideIt()
    {
        await _session.Dispatch(() =>
        {
            var dir = NewTempDir();
            try
            {
                var service = new PreferencesService(new PreferencesStore(dir));
                var portability = PortabilityOver(dir, service);

                foreach (var (window, buttonName) in new (Window, string)[]
                         {
                             (new SettingsExportDialog(portability), "ExportButton"),
                             (new SettingsImportDialog(portability), "ImportButton"),
                         })
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();

                    var scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
                        .FirstOrDefault(s => s.FindAncestorOfType<Window>() is not null
                                             && s.Content is StackPanel);
                    Assert.NotNull(scroller);

                    var primary = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == buttonName);
                    // The button must not live inside the scroller, or scrolling could carry it away.
                    Assert.Null(primary.FindAncestorOfType<ScrollViewer>());

                    _out.WriteLine($"{window.GetType().Name}: body scrolls, {buttonName} is outside it");
                    window.Close();
                }
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }, System.Threading.CancellationToken.None);
    }

    /// <summary>Resolves a theme brush the same way <c>IconBrushConverter</c> does — by key AND theme variant,
    /// never by key alone (gotcha #250).</summary>
    private static object? BrushIn(Window window, string key)
        => Application.Current!.Resources.TryGetResource(key, window.ActualThemeVariant, out var brush)
            ? brush
            : null;
}
