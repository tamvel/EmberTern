using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Styling;
using EmberTern.App;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Settings Center etap 3 — the window's content, as a projection of <see cref="SettingsCatalog"/> over the
/// app's one <see cref="PreferencesService"/>.
/// <para>
/// Three of these are about the design's load-bearing rules rather than about behaviour:
/// <see cref="EveryEnumeratedOptionHasALabel"/> (Core owns the keys, App owns the words — a key without a
/// label ships a blank row), <see cref="ChangingOneSetting_LeavesEveryOtherPreferenceAlone"/> (the page
/// composes with <c>with</c>, so a preference it does not render is not reset to its default), and
/// <see cref="ARefusedSave_IsSaidOutLoud_AndTheChoiceStillHoldsForTheSession"/> (a settings dialog that
/// appears to accept a change and persists nothing is the worst possible place for that silence).
/// </para>
/// </summary>
public class SettingsCenterVmTests
{
    private static SecretProtector FakeProtector() =>
        new(s => "ENC:" + s, s => s.StartsWith("ENC:", StringComparison.Ordinal)
            ? s.Substring(4)
            : throw new FormatException("not an ENC: blob"));

    // Encrypts fine but can never decrypt — precisely DPAPI on the wrong Windows account, which is the state
    // ApplicationSettingsStore.Save refuses to write over (audit A-03).
    private static SecretProtector UndecryptableProtector() =>
        new(s => "ENC:" + s, _ => throw new InvalidOperationException("Key not valid for use in specified state."));

    private static void InTempDir(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));
        try { body(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    private static SettingsCenterViewModel VmOver(string dir, SecretProtector? protector = null)
        => new(new PreferencesService(new PreferencesStore(dir, protector)));

    // ─── THE CATALOG ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Core owns an option's KEY because it is persisted and validated; App owns its display label. Without
    /// this test, adding an option to a <c>PreferenceOptionSet</c> ships a row with a blank caption — the same
    /// silent-omission shape the icon-consistency and command-title tests were written for.
    /// </summary>
    [Fact]
    public void EveryEnumeratedOptionHasALabel()
    {
        foreach (var setting in SettingsCatalog.Settings.Where(s => s.Options is not null))
        {
            Assert.NotNull(setting.OptionLabels);

            foreach (var key in setting.Options!.Values)
            {
                Assert.True(setting.OptionLabels!.TryGetValue(key, out var label),
                    $"{setting.Id}: option '{key}' has no label");
                Assert.False(string.IsNullOrWhiteSpace(label), $"{setting.Id}: option '{key}' has a blank label");
            }

            // And nothing the other way either: a label for a key Core does not declare is a row that can
            // never be rendered, i.e. a leftover.
            var stray = setting.OptionLabels!.Keys.Except(setting.Options.Values, StringComparer.Ordinal).ToArray();
            Assert.True(stray.Length == 0, $"{setting.Id}: labels for unknown options: {string.Join(", ", stray)}");
        }
    }

    [Fact]
    public void TheCatalogIsWellFormed()
    {
        Assert.NotEmpty(SettingsCatalog.Categories);

        var duplicateSettings = SettingsCatalog.Settings
            .GroupBy(s => s.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        Assert.True(duplicateSettings.Length == 0, "duplicate setting ids: " + string.Join(", ", duplicateSettings));

        foreach (var setting in SettingsCatalog.Settings)
        {
            Assert.Contains(SettingsCatalog.Categories, c => c.Id == setting.CategoryId);
            Assert.False(string.IsNullOrWhiteSpace(setting.Label));
            Assert.False(string.IsNullOrWhiteSpace(setting.Description));
            Assert.NotEmpty(setting.Keywords);
        }

        // ⚠ A category with no settings is indistinguishable from a defect in QA (gotcha #233), which is why a
        // category ships WITH its page rather than ahead of it.
        Assert.All(SettingsCatalog.Categories, c => Assert.NotEmpty(SettingsCatalog.SettingsIn(c.Id)));
    }

    /// <summary>
    /// ⛔ The same condition <c>CommandCatalog</c>'s descriptor table is held to: the words live in
    /// <c>UiStrings</c> and the option keys in <c>PreferenceOptions</c>, so the table itself names no strings.
    /// Scoped to the table (the static constructor) — the file's prose and its <c>Matches</c> helper are
    /// documentation and code, not values the app renders.
    /// </summary>
    [Fact]
    public void TheCatalogTableContainsNoStringLiterals()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Settings", "SettingsCatalog.cs"));

        var table = Regex.Match(source, @"static SettingsCatalog\(\)\s*\{(.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(table.Success, "could not locate the SettingsCatalog table");

        var offenders = table.Groups[1].Value
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal) && l.Contains('"', StringComparison.Ordinal))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "SettingsCatalog's table must name no strings of its own. Found: " + string.Join(" | ", offenders));
    }

    // ─── OPTIONS COME FROM CORE ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Design §5.2.2: the legal values are declared once, in Core, and the UI generates its items from
    /// there. A hand-typed list in XAML drifts silently in the dangerous direction — the user picks an option
    /// the validator rejects, it appears to work, and it reverts on the next load with nothing failing.
    /// </summary>
    [Fact]
    public void TheOptionsRenderedAreExactlyTheOnesCoreDeclares()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);

            Assert.Equal(PreferenceOptions.Theme.Values, vm.Theme.Options.Select(o => o.Key).ToArray());
            Assert.Equal(PreferenceOptions.Language.Values, vm.Language.Options.Select(o => o.Key).ToArray());

            // The labels are UiStrings', not Core's keys.
            Assert.Equal(UiStrings.SettingsThemeDark,
                vm.Theme.Options.Single(o => o.Key == PreferenceOptions.ThemeDark).Label);
            Assert.Equal(UiStrings.SettingsLanguageEnglish,
                vm.Language.Options.Single(o => o.Key == PreferenceOptions.LanguageEnglish).Label);
        });
    }

    /// <summary>
    /// ⭐ Language is a REAL preference from day one whose catalog happens to hold one row — not a stub. Its
    /// value round-trips, and a value from a build that knew more languages resolves to a selectable option
    /// rather than leaving the box blank.
    /// </summary>
    [Fact]
    public void Language_IsStoredValidatedAndAlwaysSelectable()
    {
        InTempDir(dir =>
        {
            // Plant a code this build does not know, through the raw store (which does not normalize).
            new ApplicationSettingsStore(dir).Save(new ApplicationSettings
            {
                UserSettings = { Preferences = new Preferences { Language = "kl" } },
            });

            var vm = VmOver(dir);

            Assert.Equal(PreferenceOptions.LanguageEnglish, vm.Language.Value);
            Assert.NotNull(vm.Language.SelectedOption);
            Assert.Equal(PreferenceOptions.LanguageEnglish, vm.Language.SelectedOption!.Key);
        });
    }

    // ─── APPLY ON CHANGE ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ChangingAValue_PersistsImmediately_WithNothingToConfirm()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            Assert.Equal(PreferenceOptions.ThemeDark, vm.Theme.Value);

            vm.Theme.Value = PreferenceOptions.ThemeLight;

            // No OK, no Apply, no dialog result: the file already holds it.
            Assert.Equal(PreferenceOptions.ThemeLight, new PreferencesStore(dir).Load().Theme);
            Assert.False(vm.ShowSaveRefusal);
        });
    }

    /// <summary>The radio path: a <c>RadioButton</c> writes <c>IsSelected</c>, and only a transition to true is
    /// a decision — the group unchecking its siblings must not commit anything.</summary>
    [Fact]
    public void SelectingAnOptionRow_IsWhatCommits()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);

            vm.Theme.Options.Single(o => o.Key == PreferenceOptions.ThemeLight).IsSelected = true;

            Assert.Equal(PreferenceOptions.ThemeLight, vm.Theme.Value);
            Assert.Equal(PreferenceOptions.ThemeLight, new PreferencesStore(dir).Load().Theme);

            // The group's own unchecking of the previous row is not a second decision.
            Assert.False(vm.Theme.Options.Single(o => o.Key == PreferenceOptions.ThemeDark).IsSelected);
            Assert.Equal(PreferenceOptions.ThemeLight, new PreferencesStore(dir).Load().Theme);
        });
    }

    /// <summary>
    /// ⭐ The page composes with <c>source with { … }</c>, never a fresh <c>Preferences</c>. A fresh instance
    /// would silently reset every preference this window does not render — the formatter's two casing settings
    /// today — turning "I changed the theme" into "my formatter settings disappeared".
    /// </summary>
    [Fact]
    public void ChangingOneSetting_LeavesEveryOtherPreferenceAlone()
    {
        InTempDir(dir =>
        {
            new PreferencesStore(dir).Save(new Preferences
            {
                FormatterKeywordCase = PreferenceOptions.CaseUpper,
                FormatterIdentifierCase = PreferenceOptions.CaseUpper,
            });

            VmOver(dir).Theme.Value = PreferenceOptions.ThemeLight;

            var after = new PreferencesStore(dir).Load();
            Assert.Equal(PreferenceOptions.ThemeLight, after.Theme);
            Assert.Equal(PreferenceOptions.CaseUpper, after.FormatterKeywordCase);
            Assert.Equal(PreferenceOptions.CaseUpper, after.FormatterIdentifierCase);
        });
    }

    // ─── THE REFUSAL MUST BE SPOKEN ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <c>Save</c> refuses silently over a settings.dat this build could not read. Silence is right for the
    /// app's incidental writers; here it is wrong. And the choice still holds for the session — a refusal
    /// means the FILE cannot be written, not that the choice was invalid.
    /// </summary>
    [Fact]
    public void ARefusedSave_IsSaidOutLoud_AndTheChoiceStillHoldsForTheSession()
    {
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings());
            var path = Path.Combine(dir, "settings.dat");
            var original = File.ReadAllText(path);

            var vm = VmOver(dir, UndecryptableProtector());
            vm.Theme.Value = PreferenceOptions.ThemeLight;

            Assert.True(vm.ShowSaveRefusal);
            Assert.False(string.IsNullOrWhiteSpace(vm.SaveRefusalMessage));
            Assert.Contains("Refusing to overwrite", vm.SaveRefusalMessage, StringComparison.Ordinal);

            // The session honours it; the file the store could not read is untouched.
            Assert.Equal(PreferenceOptions.ThemeLight, vm.Theme.Value);
            Assert.Equal(original, File.ReadAllText(path));
        });
    }

    [Fact]
    public void ASuccessfulSave_ClearsTheBanner()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            vm.Theme.Value = PreferenceOptions.ThemeLight;
            Assert.False(vm.ShowSaveRefusal);
            Assert.Equal(string.Empty, vm.SaveRefusalMessage);
        });
    }

    // ─── SEARCH ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Search_FiltersSettingsCategoriesAndKeywords()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            Assert.True(vm.Theme.IsVisible);
            Assert.True(vm.Language.IsVisible);
            Assert.True(vm.HasMatches);

            // By label, case-insensitively.
            vm.SearchText = "THEME";
            Assert.True(vm.Theme.IsVisible);
            Assert.False(vm.Language.IsVisible);
            Assert.True(vm.HasMatches);

            // ⚠ By keyword — the words a user types when they do not know our label. This is also why the
            // matcher is a substring test and NOT CompletionMatcher, whose prefix-first philosophy would
            // refuse the next case.
            vm.SearchText = "colour";
            Assert.True(vm.Theme.IsVisible);
            Assert.False(vm.Language.IsVisible);

            // Substring, not prefix.
            vm.SearchText = "anguag";
            Assert.True(vm.Language.IsVisible);
            Assert.False(vm.Theme.IsVisible);

            // By category title — the whole page survives rather than emptying.
            vm.SearchText = UiStrings.SettingsCategoryGeneral;
            Assert.True(vm.Theme.IsVisible);
            Assert.True(vm.Language.IsVisible);

            // Nothing matches ⇒ an explained empty state, not an empty window.
            vm.SearchText = "zzzz-no-such-setting";
            Assert.Empty(vm.Categories);
            Assert.False(vm.HasMatches);
            Assert.Null(vm.SelectedCategory);
            Assert.False(vm.IsGeneralPageVisible);

            // Clearing restores everything, and lands back on a page.
            vm.SearchText = string.Empty;
            Assert.Equal(SettingsCatalog.Categories.Count, vm.Categories.Count);
            Assert.True(vm.HasMatches);
            Assert.True(vm.IsGeneralPageVisible);
        });
    }

    [Fact]
    public void Search_NeverLeavesTheRightPaneBlankWhileTheListHasEntries()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            vm.SearchText = "theme";

            Assert.NotEmpty(vm.Categories);
            Assert.NotNull(vm.SelectedCategory);
            Assert.Contains(vm.SelectedCategory!, vm.Categories);
        });
    }

    // ─── THEME KEY ↔ VARIANT, THE ONE MAPPING ───────────────────────────────────────────────

    /// <summary>
    /// The single translation between a stored key and Avalonia's variant. It is pinned because three surfaces
    /// use it — startup, the titlebar toggle and the Settings radio — and a second mapping would show up as a
    /// theme that applies from one of them and not the others.
    /// </summary>
    [Fact]
    public void ThemePreference_MapsBothWays_AndFallsBackToDark()
    {
        Assert.Equal(ThemeVariant.Dark, ThemePreference.VariantFor(PreferenceOptions.ThemeDark));
        Assert.Equal(ThemeVariant.Light, ThemePreference.VariantFor(PreferenceOptions.ThemeLight));

        Assert.Equal(PreferenceOptions.ThemeDark, ThemePreference.KeyFor(ThemeVariant.Dark));
        Assert.Equal(PreferenceOptions.ThemeLight, ThemePreference.KeyFor(ThemeVariant.Light));

        Assert.Equal(PreferenceOptions.ThemeLight, ThemePreference.Toggle(PreferenceOptions.ThemeDark));
        Assert.Equal(PreferenceOptions.ThemeDark, ThemePreference.Toggle(PreferenceOptions.ThemeLight));

        // ⚠ Dark is the fallback, matching PreferenceOptions.Theme.Default and App.axaml's bootstrap value —
        // never ThemeVariant.Default, which would follow the OS theme (design §2.1).
        Assert.Equal(ThemeVariant.Dark, ThemePreference.VariantFor(null));
        Assert.Equal(ThemeVariant.Dark, ThemePreference.VariantFor("chartreuse"));
        Assert.Equal(PreferenceOptions.ThemeLight, ThemePreference.Toggle("chartreuse"));
    }

    // ─── THE ONE SERVICE ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The reason <see cref="PreferencesService"/> exists: the store persists a whole <c>Preferences</c>, so
    /// two snapshot holders overwrite each other. This is the concrete case — the titlebar toggle writes
    /// Theme, then Settings Center writes Language.
    /// </summary>
    [Fact]
    public void OneService_MeansTheToggleAndTheWindowCannotClobberEachOther()
    {
        InTempDir(dir =>
        {
            var service = new PreferencesService(new PreferencesStore(dir));
            var vm = new SettingsCenterViewModel(service);

            // The titlebar toggle's write, through the same service.
            service.Apply(service.Current with { Theme = ThemePreference.Toggle(service.Current.Theme) });
            Assert.Equal(PreferenceOptions.ThemeLight, service.Current.Theme);

            // The window now writes a different preference. With its own snapshot it would carry the pre-toggle
            // Theme back to disk; through the one service it does not.
            vm.Language.Value = PreferenceOptions.LanguageEnglish;
            vm.Theme.Value = PreferenceOptions.ThemeLight;

            var stored = new PreferencesStore(dir).Load();
            Assert.Equal(PreferenceOptions.ThemeLight, stored.Theme);
            Assert.Equal(PreferenceOptions.LanguageEnglish, stored.Language);
        });
    }

    [Fact]
    public void TheServiceRaisesChanged_WhichIsWhatPaintsTheTheme()
    {
        InTempDir(dir =>
        {
            var service = new PreferencesService(new PreferencesStore(dir));
            var raised = 0;
            service.Changed += (_, _) => raised++;

            service.Apply(service.Current with { Theme = PreferenceOptions.ThemeLight });

            Assert.Equal(1, raised);
            Assert.Equal(PreferenceOptions.ThemeLight, service.Current.Theme);
        });
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
