using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Styling;
using EmberTern.App;
using EmberTern.App.Controls;
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
    {
        var service = new PreferencesService(new PreferencesStore(dir, protector));
        return new SettingsCenterViewModel(service, PortabilityOver(dir, service, protector));
    }

    /// <summary>
    /// The export/import seam over the same directory and protector, as the app wires it (gotcha #88).
    /// <para>The app version is a synthetic one on purpose: Core takes it as an input it cannot derive, which is
    /// what makes "diagnostics only, never branched on" structural (§15.3a).</para>
    /// </summary>
    private static SettingsPortability PortabilityOver(
        string dir, PreferencesService service, SecretProtector? protector = null)
        => new(new ApplicationSettingsStore(dir, protector), service, "9.9.9-test");

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

    // ─── ETAP 6 — THE TWO NEW ROW KINDS ─────────────────────────────────────────────────────

    /// <summary>
    /// Every catalog row is projected as the row type its declared <see cref="SettingValueKind"/> asks for, and a
    /// numeric row always has the range it is bounded by.
    /// <para>⚠ The range check is what keeps the cast in the view model's construction honest: a
    /// <c>Number</c> row with no range would throw at window-open time, which is a defect no view-model test
    /// exercising only the rows it knows about would find.</para>
    /// </summary>
    [Fact]
    public void EveryRowKind_HasItsRangeAndIsProjectedAsItsType()
    {
        foreach (var setting in SettingsCatalog.Settings)
        {
            switch (setting.ValueKind)
            {
                case SettingValueKind.Number:
                    Assert.NotNull(setting.Range);
                    Assert.Null(setting.Options);
                    break;
                case SettingValueKind.Toggle:
                    Assert.Null(setting.Range);
                    Assert.Null(setting.Options);
                    break;
                default:
                    // An enumerated PREFERENCE draws on an option set; an ACTION row draws on nothing.
                    Assert.Null(setting.Range);
                    Assert.Equal(setting.Kind == SettingKind.Preference, setting.Options is not null);
                    break;
            }
        }

        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            Assert.IsType<BooleanSettingViewModel>(vm.RestoreWorkspace);
            Assert.IsType<NumericSettingViewModel>(vm.PreviewRowLimit);
            Assert.IsType<PreferenceSettingViewModel>(vm.DebuggerIsolation);
            Assert.IsType<SettingActionViewModel>(vm.ImportExport);
        });
    }

    /// <summary>A checkbox is discrete, so it commits the moment it is clicked — like a radio, unlike a
    /// field.</summary>
    [Fact]
    public void AToggle_CommitsImmediately()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            Assert.True(vm.RestoreWorkspace.Value);

            vm.RestoreWorkspace.Value = false;

            Assert.False(new PreferencesStore(dir).Load().RestoreWorkspaceOnStartup);
        });
    }

    /// <summary>
    /// ⭐⭐ <b>The blur-or-Enter commit path (design §5.5.1) — the debt §16.8 recorded for this etap.</b>
    ///
    /// <para>Typing must persist NOTHING. Every save reads + decrypts + rewrites the whole <c>settings.dat</c>,
    /// and — the part that is not performance — <c>TryAtomicWrite</c> keeps exactly ONE generation of
    /// <c>settings.dat.bak</c>, so a per-keystroke save would roll the single hand-recovery backup through four
    /// generations while somebody is editing settings.</para>
    /// </summary>
    [Fact]
    public void ANumericField_PersistsNothingWhileTyping_AndCommitsOnce()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            var store = new PreferencesStore(dir);

            // Keystroke by keystroke: "2", "25", "250".
            foreach (var text in new[] { "2", "25", "250" })
            {
                vm.PreviewRowLimit.EditText = text;
                Assert.Equal(
                    PreferenceOptions.PreviewRowLimit.Default,
                    store.Load().PreviewRowLimit);
                Assert.Equal(PreferenceOptions.PreviewRowLimit.Default, vm.PreviewRowLimit.Value);
            }

            vm.PreviewRowLimit.Commit();   // what LostFocus / Enter calls

            Assert.Equal(250, vm.PreviewRowLimit.Value);
            Assert.Equal(250, store.Load().PreviewRowLimit);
        });
    }

    /// <summary>
    /// Out of range clamps and the field ECHOES the stored number back, because the store would clamp it anyway
    /// — a field still displaying <c>50000000</c> over a stored <c>1000000</c> would simply be lying.
    /// </summary>
    [Fact]
    public void ANumericField_ClampsAndShowsWhatWasStored()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            var max = PreferenceOptions.PreviewRowLimit.Maximum;

            vm.PreviewRowLimit.EditText = "50000000";
            vm.PreviewRowLimit.Commit();

            Assert.Equal(max, vm.PreviewRowLimit.Value);
            Assert.Equal(max.ToString(System.Globalization.CultureInfo.CurrentCulture), vm.PreviewRowLimit.EditText);
            Assert.Equal(max, new PreferencesStore(dir).Load().PreviewRowLimit);

            vm.PreviewRowLimit.EditText = "0";
            vm.PreviewRowLimit.Commit();
            Assert.Equal(PreferenceOptions.PreviewRowLimit.Minimum, vm.PreviewRowLimit.Value);
        });
    }

    /// <summary>
    /// ⭐ <b>Non-numeric text never lands in the first place</b> — the field refuses it, keeping the digits the
    /// user had already typed.
    ///
    /// <para>⚠ This test used to be <c>ANumericField_RevertsUnparseableText</c>, asserting that
    /// <c>"not a number"</c> was accepted into <c>EditText</c> and undone by <c>Commit</c>. Its assertions
    /// still pass unchanged under the gate — which is exactly why it was rewritten rather than left alone: a
    /// test that passes for a reason it no longer describes stops being evidence. The behaviour it documented
    /// was also the weaker one (the user lost the whole entry and was told nothing), and it is what QA asked
    /// to change.</para>
    /// </summary>
    [Fact]
    public void ANumericField_RefusesEveryShapeThatCouldNotBecomeANumber()
    {
        InTempDir(dir =>
        {
            var row = VmOver(dir).PreviewRowLimit;

            foreach (var rejected in new[]
                     {
                         "not a number", "12a", "a12", "12 34", "1.5", "1,5",
                         "-1",           // this range is positive, so a sign is not a legal keystroke
                         "١٢٣",           // Unicode digits: char.IsDigit would admit these, and they do not parse
                         "12345678901",  // longer than any int
                     })
            {
                Assert.False(row.AcceptsText(rejected), rejected);
            }
        });
    }

    /// <summary>
    /// ⚠ The gate judges a <b>PARTIAL</b> entry, so everything on the way to a number passes: an empty field
    /// (the user is retyping) and an over-range value.
    ///
    /// <para>⭐ <b>Over-range is deliberately NOT refused</b> — typing <c>50000000</c> into a field whose maximum
    /// is a million is the user saying "as many as possible", and clamping is §17.1's documented answer. An
    /// earlier cut capped the length at the <i>maximum's</i> digits and silently made that impossible, which is
    /// why the cap is <c>int</c>'s width instead.</para>
    /// </summary>
    [Fact]
    public void ANumericField_AllowsEveryStepTowardsANumber_IncludingOverRange()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);

            foreach (var accepted in new[] { "", "5", "50", "500", "50000000", "9999999999" })
            {
                Assert.True(vm.PreviewRowLimit.AcceptsText(accepted), accepted);
            }

            vm.PreviewRowLimit.EditText = "50000000";
            vm.PreviewRowLimit.Commit();
            Assert.Equal(PreferenceOptions.PreviewRowLimit.Maximum, vm.PreviewRowLimit.Value);

            // ⚠ Ten digits above int.MaxValue: accepted, and it must still mean "the maximum" rather than
            // reverting — which is why Commit parses as long before clamping.
            vm.PreviewRowLimit.EditText = "9999999999";
            vm.PreviewRowLimit.Commit();
            Assert.Equal(PreferenceOptions.PreviewRowLimit.Maximum, vm.PreviewRowLimit.Value);

            // An empty field commits to nothing rather than to zero — Commit's remaining backstop, since ""
            // legitimately reaches it (clearing is an allowed step) and is not a number.
            vm.PreviewRowLimit.EditText = string.Empty;
            vm.PreviewRowLimit.Commit();
            Assert.Equal(PreferenceOptions.PreviewRowLimit.Maximum, vm.PreviewRowLimit.Value);
            Assert.Equal(
                PreferenceOptions.PreviewRowLimit.Maximum.ToString(System.Globalization.CultureInfo.CurrentCulture),
                vm.PreviewRowLimit.EditText);
        });
    }

    /// <summary>
    /// ⚠ <b>`EditText` itself stays tolerant, and that is a decision.</b> Vetoing in the setter was tried and
    /// measured to fail twice: Avalonia's two-way binding ignores a `PropertyChanged` raised while it is
    /// pushing target → source (so the refused text stayed on screen), and it would have made **paste** worse —
    /// `Commit` would find the model already correct, notify nothing, and leave the pasted junk in the field
    /// permanently. This test pins the paste path: junk gets in, and blur/Enter cleans it.
    /// </summary>
    [Fact]
    public void ANumericField_PastedJunkIsUndoneAtCommit()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            vm.PreviewRowLimit.EditText = "1234";
            vm.PreviewRowLimit.Commit();

            vm.PreviewRowLimit.EditText = "not a number";   // what a paste does — no TextInput to refuse
            Assert.Equal("not a number", vm.PreviewRowLimit.EditText);

            vm.PreviewRowLimit.Commit();

            Assert.Equal(1234, vm.PreviewRowLimit.Value);
            Assert.Equal("1234", vm.PreviewRowLimit.EditText);
            Assert.Equal(1234, new PreferencesStore(dir).Load().PreviewRowLimit);
        });
    }

    /// <summary>
    /// ⚠ The sign is admitted from the RANGE, not assumed away. Every range this build ships is positive, so
    /// without this case the negative branch would be untested code that a future negative-minimum preference
    /// would discover the hard way — as a field nobody could type into.
    /// </summary>
    [Fact]
    public void ANumericField_AdmitsASignExactlyWhenItsRangeDoes()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);

            // Positive range (the shipped case): a sign is not a legal keystroke at all.
            Assert.False(vm.PreviewRowLimit.AcceptsText("-"));
            Assert.False(vm.PreviewRowLimit.AcceptsText("-5"));

            // A range that admits negatives accepts the lone "-" as a prefix, then the digits.
            var signed = new NumericSettingViewModel(
                SettingsCatalog.Settings.First(s => s.Id == SettingsCatalog.SettingPreviewRowLimit),
                "Editor",
                new PreferenceRange(minimum: -10, maximum: 10, @default: 0),
                0);

            Assert.True(signed.AcceptsText("-"));
            Assert.True(signed.AcceptsText("-5"));
            Assert.False(signed.AcceptsText("-a"));
            Assert.False(signed.AcceptsText("5-"));
        });
    }

    /// <summary>A commit that changes nothing writes nothing — blur fires on every focus change, so an idle
    /// tab-through must not cost a full encrypted rewrite of the file.</summary>
    [Fact]
    public void ANumericField_CommittingAnUnchangedValue_WritesNothing()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            vm.DataPageSize.Commit();       // never edited

            var path = Path.Combine(dir, "settings.dat");
            Assert.False(File.Exists(path));
        });
    }

    /// <summary>Every etap-6 row reaches the file, one row at a time, leaving the others alone — the
    /// <c>ValueOf</c>/<c>FlagOf</c>/<c>NumberOf</c> → <c>Compose</c> mapping proved whole rather than
    /// spot-checked.</summary>
    [Fact]
    public void EveryEtap6Row_ReachesTheFile()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);

            vm.RestoreWorkspace.Value = false;
            vm.ProcedureEasyMode.Value = true;
            vm.ViewEasyMode.Value = true;
            vm.TriggerEasyMode.Value = true;
            vm.FunctionEasyMode.Value = true;
            vm.GridAutoFitColumns.Value = false;
            vm.DebuggerIsolation.Value = PreferenceOptions.DebuggerIsolationSnapshot;
            vm.PreviewRowLimit.EditText = "111";
            vm.PreviewRowLimit.Commit();
            vm.FullLoadPromptThreshold.EditText = "2222";
            vm.FullLoadPromptThreshold.Commit();
            vm.DataPageSize.EditText = "333";
            vm.DataPageSize.Commit();

            var after = new PreferencesStore(dir).Load();
            Assert.False(after.RestoreWorkspaceOnStartup);
            Assert.True(after.ProcedureEasyModeDefault);
            Assert.True(after.ViewEasyModeDefault);
            Assert.True(after.TriggerEasyModeDefault);
            Assert.True(after.FunctionEasyModeDefault);
            Assert.False(after.GridAutoFitColumns);
            Assert.Equal(PreferenceOptions.DebuggerIsolationSnapshot, after.DebuggerIsolation);
            Assert.Equal(111, after.PreviewRowLimit);
            Assert.Equal(2222, after.FullLoadPromptThreshold);
            Assert.Equal(333, after.DataPageSize);

            // And nothing this page also renders was disturbed on the way.
            Assert.Equal(PreferenceOptions.ThemeDark, after.Theme);
            Assert.Equal(PreferenceOptions.CaseLower, after.FormatterKeywordCase);
        });
    }

    /// <summary>
    /// ⭐ „Maximum rows" znika w trybie jednowierszowym — interfejs nie pokazuje ustawień, które w danym
    /// trybie nic nie robią (decyzja użytkownika, 2026-08-03).
    ///
    /// <para>⚠⚠ Druga asercja jest ważniejsza od pierwszej: <b>ukrycie wiersza nie może skasować wartości.</b>
    /// Ukrycie ustawienia i porzucenie go to dwie różne rzeczy, a mylą się bardzo łatwo — wystarczyłoby, żeby
    /// `Compose` przestało czytać niewidoczny wiersz i liczba znikałaby z pliku po każdym przełączeniu trybu.
    /// Test przechodzi przez PEŁNY obieg: ustaw → przełącz → wróć.</para>
    /// </summary>
    [Fact]
    public void TabStripMaxRows_IsHiddenInSingleRow_ButItsValueSurvives()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);

            vm.TabStripMode.Value = PreferenceOptions.TabStripModeMultiRow;
            vm.TabStripMaxRows.EditText = "7";
            vm.TabStripMaxRows.Commit();
            Assert.True(vm.ShowTabStripMaxRows);

            // ⚠⚠ Sama wartość właściwości NIE WYSTARCZY — czytana wprost jest poprawna nawet wtedy, gdy nic
            //   o niej nie mówi, a wiązanie w widoku odpytuje ją WYŁĄCZNIE po `PropertyChanged`. To jest ta
            //   sama luka, przez którą w §19.2 poprawny styl nigdy się nie namalował: mechanizm był dobry,
            //   brakowało sygnału. Dlatego notyfikacja jest tu asercją, a nie założeniem.
            var announced = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsCenterViewModel.ShowTabStripMaxRows)) announced = true;
            };

            vm.TabStripMode.Value = PreferenceOptions.TabStripModeSingleRow;
            Assert.False(vm.ShowTabStripMaxRows);
            Assert.True(announced, "Zmiana trybu nie ogłosiła ShowTabStripMaxRows — widok nie odpyta.");

            vm.TabStripMode.Value = PreferenceOptions.TabStripModeMultiRow;
            Assert.True(vm.ShowTabStripMaxRows);
            Assert.Equal(7, vm.TabStripMaxRows.Value);

            // I to samo, co przetrwało w pamięci, musi być na dysku — inaczej „zachowana" znaczyłoby
            // wyłącznie „do zamknięcia okna".
            Assert.Equal(7, new PreferencesStore(dir).Load().TabStripMaxRows);
        });
    }

    /// <summary>
    /// ⚠ Tryb i filtr wyszukiwania to DWIE niezależne przyczyny ukrycia tego samego wiersza, więc żadna nie
    /// może nadpisywać drugiej: fraza pasująca do wiersza nie ma go wskrzesić w trybie, w którym nic nie robi.
    /// </summary>
    [Fact]
    public void TabStripMaxRows_StaysHiddenInSingleRow_EvenWhenTheSearchMatchesIt()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            vm.TabStripMode.Value = PreferenceOptions.TabStripModeSingleRow;

            vm.SearchText = "maximum rows";

            Assert.True(vm.TabStripMaxRows.IsVisible);   // filtr go przepuszcza…
            Assert.False(vm.ShowTabStripMaxRows);        // …a tryb i tak go ukrywa
        });
    }

    /// <summary>Every category the catalog declares has a page the window can show — otherwise selecting it
    /// leaves the right pane blank, which is what a missing <c>IsVisible</c> property looks like.</summary>
    [Fact]
    public void EveryCategory_HasAPageVisibilityProperty()
    {
        InTempDir(dir =>
        {
            var vm = VmOver(dir);
            var visibilities = new Func<bool>[]
            {
                () => vm.IsGeneralPageVisible,
                () => vm.IsEditorPageVisible,
                () => vm.IsGridPageVisible,
                () => vm.IsTabsPageVisible,
                () => vm.IsDebuggerPageVisible,
                () => vm.IsFormatterPageVisible,
            };

            Assert.Equal(SettingsCatalog.Categories.Count, visibilities.Length);

            foreach (var category in vm.Categories.ToArray())
            {
                vm.SelectedCategory = category;
                Assert.Equal(1, visibilities.Count(v => v()));
            }
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
            var vm = new SettingsCenterViewModel(service, PortabilityOver(dir, service));

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

    /// <summary>
    /// ⚠ <b>A latent defect the QA round exposed: the "select at least one thing" reason could never appear.</b>
    /// Five of the seven section flags notified <c>CanExport</c> but not the reason property, so unticking every
    /// box disabled Export while the hint went on describing the previous state — the exact failure the hint
    /// exists to prevent, with a green build. The test drives it through <c>PropertyChanged</c> rather than
    /// reading the property directly, because reading it always computed the right answer; only a listener sees
    /// whether the UI was ever told.
    /// </summary>
    [Fact]
    public void UntickingEverySection_AnnouncesWhyExportIsBlocked()
    {
        InTempDir(dir =>
        {
            var vm = ExportVmOver(dir);
            var announced = 0;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsExportDialogViewModel.Blocked)) announced++;
            };

            // The defaults ARE §6.3.4's classification, so the dialog opens with sections ticked and the
            // outstanding step is the passphrase.
            Assert.Equal(MessageSeverity.Warning, vm.Blocked.Severity);
            Assert.Equal(UiStrings.SettingsExportPassphraseMissing, vm.Blocked.Text);

            // ⚠ Connections is unticked FIRST and Preferences LAST on purpose: Connections was the one flag that
            // already announced (it had a handler for the password rule), so a test that ended on it would have
            // passed against the defect. The last step here is one of the five that were silent.
            vm.Connections = false;
            vm.GridProfiles = false;
            vm.Folders = false;

            var beforeTheLastOne = announced;
            vm.Preferences = false;

            Assert.False(vm.CanExport);
            Assert.True(announced > beforeTheLastOne, "the reason changed and nothing was told about it");
            Assert.Equal(UiStrings.SettingsExportNothingSelected, vm.Blocked.Text);
            // Emptied on purpose ⇒ a wrong state, not an outstanding step.
            Assert.Equal(MessageSeverity.Error, vm.Blocked.Severity);
            Assert.Equal("ErrorBrush", vm.Blocked.BrushKey);
        });
    }

    /// <summary>
    /// The import dialog's twin, and its deliberate limit: the reason appears for the one state a user reaches by
    /// mistake — the file is open and every box has been unticked — and stays <b>silent</b> before that. A dialog
    /// whose first control is <i>Choose file…</i> does not need a line telling the user to choose a file, and
    /// premature validation is its own UX defect.
    /// </summary>
    [Fact]
    public void TheImportHint_AppearsOnlyWhenAnOpenedFileHasNothingTicked()
    {
        InTempDir(dir =>
        {
            var vm = ImportVmOver(dir);

            Assert.False(vm.Blocked.IsVisible);

            // Simulate what Open() produces: the file carried preferences, and the user unticks them.
            vm.IsOpened = true;
            vm.TakePreferences = true;
            Assert.True(vm.CanImport);
            Assert.False(vm.Blocked.IsVisible);

            vm.TakePreferences = false;
            Assert.False(vm.CanImport);
            Assert.True(vm.Blocked.IsVisible);
            Assert.Equal(UiStrings.SettingsImportNothingSelected, vm.Blocked.Text);
            Assert.Equal(MessageSeverity.Error, vm.Blocked.Severity);
        });
    }

    /// <summary>⭐ The colour and the icon are taken from <c>MessageBanner</c>'s shared map, never restated — so a
    /// gate hint and a banner cannot disagree about what an error looks like. Pinned because a literal here would
    /// compile, render plausibly, and drift the day the map changes.</summary>
    [Fact]
    public void AGateHint_ReadsItsColourAndIconFromTheSharedSeverityMap()
    {
        foreach (var hint in new[]
                 {
                     DialogGateHint.Error("wrong"),
                     DialogGateHint.Pending("outstanding"),
                 })
        {
            Assert.Equal(MessageBanner.BrushKeyFor(hint.Severity), hint.BrushKey);
            Assert.Equal(MessageBanner.GeometryKeyFor(hint.Severity), hint.GeometryKey);
            Assert.True(hint.IsVisible);
        }

        Assert.False(DialogGateHint.None.IsVisible);
    }

    private static SettingsExportDialogViewModel ExportVmOver(string dir)
        => new(PortabilityOver(dir));

    private static SettingsImportDialogViewModel ImportVmOver(string dir)
        => new(PortabilityOver(dir));

    private static SettingsPortability PortabilityOver(string dir)
        => new(new ApplicationSettingsStore(dir), new PreferencesService(new PreferencesStore(dir)), "9.9.9-test");

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
