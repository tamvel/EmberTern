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
                var window = new SettingsWindow(service);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var radios = window.GetVisualDescendants().OfType<RadioButton>().ToList();
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
                var window = new SettingsWindow(new PreferencesService(new PreferencesStore(dir)));
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var groups = window.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("settings-group")).ToList();
                Assert.Equal(2, groups.Count);
                Assert.All(groups, g => Assert.True(g.IsVisible));

                var search = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchBox");
                var categories = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "CategoryList");

                search.Text = "colour";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, groups.Count(g => g.IsVisible));
                Assert.Equal(1, categories.ItemCount);

                search.Text = "zzzz-no-such-setting";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, categories.ItemCount);

                var empty = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => Equals(t.Text, UiStrings.SettingsNoMatch));
                Assert.True(empty.IsVisible);

                search.Text = string.Empty;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, groups.Count(g => g.IsVisible));
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

                var window = new SettingsWindow(new PreferencesService(new PreferencesStore(dir, undecryptable)));
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
}
