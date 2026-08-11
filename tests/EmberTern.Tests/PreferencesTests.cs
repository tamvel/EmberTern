using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EmberTern.Core.Query;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Settings Center etap 2 — the <c>Preferences</c> defaults contract, the option catalog, and normalization.
/// Pure: no files, no store, no I/O. <see cref="PreferencesStoreTests"/> covers persistence.
/// <para>
/// <b>The load-bearing test is <see cref="NewPreferences_SurvivesValidation_Unchanged"/></b> — the ratified
/// contract that <c>new Preferences()</c> is valid unconditionally. Everything else here describes the rules
/// around it, and <see cref="EveryPreference_IsAccountedForInValidation"/> is what makes those rules survive
/// the arrival of the 5th, 20th and 40th preference.
/// </para>
/// </summary>
public class PreferencesTests
{
    // ─── THE DEFAULTS CONTRACT ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The ratified pin (design §5.2.1/4): the model's own initializers must be values the validator
    /// accepts. It fails the day someone gives a property a default the option catalog does not offer — for
    /// instance <c>Language = "pl"</c> while the catalog still has one row. Neither half looks wrong alone.
    /// <para>
    /// ⚠ The comparison has to be a REAL one. On a plain class <c>Equals</c> is reference equality and this
    /// would pass vacuously — pinning nothing while looking authoritative. <c>Preferences</c> is a
    /// <c>record</c> for exactly this reason, and the assertions below prove the comparison is by value:
    /// two distinct instances that compare equal, and a mutated one that does not.
    /// </para>
    /// </summary>
    [Fact]
    public void NewPreferences_SurvivesValidation_Unchanged()
    {
        var fresh = new Preferences();
        var validated = PreferencesStore.Validate(fresh);

        Assert.Equal(fresh, validated);

        // Prove the equality above is structural, not reference identity — otherwise the assertion is empty.
        Assert.NotSame(fresh, validated);
        Assert.Equal(new Preferences(), new Preferences());
        Assert.NotEqual(new Preferences(), new Preferences { Theme = PreferenceOptions.ThemeLight });
    }

    /// <summary>Every property is valid straight out of the initializer, with no bootstrap step — which is
    /// what lets every consumer, test and "restore defaults" path use <c>new Preferences()</c> without a null
    /// check.</summary>
    [Fact]
    public void NewPreferences_CarriesALegalValueInEveryProperty()
    {
        var fresh = new Preferences();

        Assert.Contains(fresh.Theme, PreferenceOptions.Theme.Values);
        Assert.Contains(fresh.Language, PreferenceOptions.Language.Values);
        Assert.Contains(fresh.FormatterKeywordCase, PreferenceOptions.Casing.Values);
        Assert.Contains(fresh.FormatterIdentifierCase, PreferenceOptions.Casing.Values);
    }

    /// <summary>The shipped defaults reproduce today's behaviour exactly: Dark, because <c>App.axaml</c>
    /// hard-codes it, and Lower for both casings, so formatter output stays byte-identical.</summary>
    [Fact]
    public void ShippedDefaults_ReproduceTodaysBehaviour()
    {
        var fresh = new Preferences();

        Assert.Equal(PreferenceOptions.ThemeDark, fresh.Theme);
        Assert.Equal(PreferenceOptions.LanguageEnglish, fresh.Language);
        Assert.Equal(PreferenceOptions.CaseLower, fresh.FormatterKeywordCase);
        Assert.Equal(PreferenceOptions.CaseLower, fresh.FormatterIdentifierCase);
    }

    // ─── THE OPTION CATALOG ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// An option set's default must be one of its own values. Violating it gives a preference that silently
    /// refuses to hold its own default — normalized away on every single load, for no reason the user can see.
    /// </summary>
    [Fact]
    public void EveryOptionSet_ContainsItsOwnDefault()
    {
        foreach (var set in PreferenceOptions.All)
        {
            Assert.Contains(set.Default, set.Values);
        }
    }

    /// <summary>The invariant above is structural, not merely tested — the constructor refuses the bad
    /// combination, so it cannot reach a build.</summary>
    [Fact]
    public void OptionSet_RefusesADefaultThatIsNotOneOfItsValues()
    {
        Assert.Throws<ArgumentException>(() => new PreferenceOptionSet(new[] { "A", "B" }, @default: "C"));
        Assert.Throws<ArgumentException>(() => new PreferenceOptionSet(Array.Empty<string>(), @default: "A"));
    }

    /// <summary>
    /// ⭐ The language catalog. It was written when the only row was English, to pin that adding Polish would
    /// be ONE row here — not a window change, a view-model change or a binding change. Polish arrived in the
    /// PL stage and that prediction held, so the test now pins what it was really protecting.
    ///
    /// <para>⚠ <b>The DEFAULT is the load-bearing half.</b> A catalog row is additive, but the default decides
    /// what every existing installation renders in: `Preferences.Language`'s initializer reads
    /// <c>Language.Default</c>, so moving it would silently switch the UI language of every user who never
    /// chose one. The set of values is checked as a superset-with-exact-membership rather than by count, so a
    /// third language is one row plus one line here.</para>
    /// </summary>
    [Fact]
    public void LanguageCatalog_CarriesEnglishAndPolish_AndDefaultsToEnglish()
    {
        Assert.Equal(new[] { "en", "pl" }, PreferenceOptions.Language.Values);
        Assert.Equal("en", PreferenceOptions.Language.Default);
    }

    // ─── NORMALIZATION ──────────────────────────────────────────────────────────────────────

    /// <summary>Total and silent: an unrecognised value is corrected, never rejected, and never throws.
    /// A value from a build that knew more options than this one lands here.</summary>
    [Fact]
    public void Normalize_UnrecognisedValue_BecomesTheDefault()
    {
        foreach (var set in PreferenceOptions.All)
        {
            Assert.Equal(set.Default, set.Normalize("something-no-build-ever-wrote"));
        }
    }

    [Fact]
    public void Normalize_NullOrBlank_BecomesTheDefault()
    {
        foreach (var set in PreferenceOptions.All)
        {
            Assert.Equal(set.Default, set.Normalize(null));
            Assert.Equal(set.Default, set.Normalize(string.Empty));
            Assert.Equal(set.Default, set.Normalize("   "));
        }
    }

    /// <summary>
    /// A value the user clearly meant is corrected to the catalog's spelling rather than thrown away — the
    /// hand-edited-file case. Resetting it instead would be data loss with extra steps.
    /// </summary>
    [Theory]
    [InlineData("dark", PreferenceOptions.ThemeDark)]
    [InlineData("LIGHT", PreferenceOptions.ThemeLight)]
    [InlineData("  Light  ", PreferenceOptions.ThemeLight)]
    public void Normalize_RecognisesAValueLoosely_AndReturnsTheCatalogSpelling(string stored, string expected)
    {
        Assert.Equal(expected, PreferenceOptions.Theme.Normalize(stored));
    }

    [Fact]
    public void Normalize_KeepsALegalNonDefaultValue()
    {
        Assert.Equal(PreferenceOptions.ThemeLight, PreferenceOptions.Theme.Normalize(PreferenceOptions.ThemeLight));
        Assert.Equal(PreferenceOptions.CaseUpper, PreferenceOptions.Casing.Normalize(PreferenceOptions.CaseUpper));
    }

    // ─── VALIDATE ───────────────────────────────────────────────────────────────────────────

    /// <summary>Reachable from a real file: <c>"Preferences": null</c> deserializes to null even though the
    /// property is non-nullable. Total normalization means answering that too, not throwing on it.</summary>
    [Fact]
    public void Validate_Null_YieldsDefaults()
    {
        Assert.Equal(new Preferences(), PreferencesStore.Validate(null));
    }

    /// <summary>Idempotent, which is what lets the same method run on read and on write without the two
    /// fighting.</summary>
    [Fact]
    public void Validate_IsIdempotent()
    {
        var garbage = new Preferences
        {
            Theme = "chartreuse",
            Language = "kl",
            FormatterKeywordCase = "TitleCase",
            FormatterIdentifierCase = "Preserve",
        };

        var once = PreferencesStore.Validate(garbage);
        Assert.Equal(once, PreferencesStore.Validate(once));
    }

    [Fact]
    public void Validate_KeepsEveryLegalValue()
    {
        var chosen = new Preferences
        {
            Theme = PreferenceOptions.ThemeLight,
            Language = PreferenceOptions.LanguageEnglish,
            FormatterKeywordCase = PreferenceOptions.CaseUpper,
            FormatterIdentifierCase = PreferenceOptions.CaseUpper,
        };

        Assert.Equal(chosen, PreferencesStore.Validate(chosen));
    }

    /// <summary>Each property is normalized against ITS OWN option set — a garbage theme must not pick up a
    /// casing value, and one bad property must not disturb the three good ones.</summary>
    [Fact]
    public void Validate_NormalizesOnlyTheOffendingProperty()
    {
        var validated = PreferencesStore.Validate(new Preferences
        {
            Theme = "chartreuse",
            Language = PreferenceOptions.LanguageEnglish,
            FormatterKeywordCase = PreferenceOptions.CaseUpper,
            FormatterIdentifierCase = PreferenceOptions.CaseUpper,
        });

        Assert.Equal(PreferenceOptions.ThemeDark, validated.Theme);
        Assert.Equal(PreferenceOptions.CaseUpper, validated.FormatterKeywordCase);
        Assert.Equal(PreferenceOptions.CaseUpper, validated.FormatterIdentifierCase);
    }

    /// <summary>
    /// ⭐ <b>Language is validated, and now it has a second legal value to prove it does not over-normalize.</b>
    /// The original version fed <c>"pl"</c> as the UNRECOGNISED case, because at the time it was one — so the
    /// fixture stopped meaning what it said the moment Polish shipped. It reads a genuinely unknown code now.
    ///
    /// <para>⚠ The <c>"pl"</c> row is the half worth having: normalization is silent and total, so a catalog
    /// row that got lost would not throw — every Polish installation would simply come back in English at the
    /// next load, which is precisely the failure the language preference exists to prevent.</para>
    /// </summary>
    [Fact]
    public void Validate_NormalizesLanguage_ButKeepsEveryLanguageTheCatalogKnows()
    {
        Assert.Equal("pl", PreferencesStore.Validate(new Preferences { Language = "pl" }).Language);
        Assert.Equal("en", PreferencesStore.Validate(new Preferences { Language = "kl" }).Language);
        Assert.Equal("en", PreferencesStore.Validate(new Preferences { Language = "" }).Language);
    }

    // ─── THE GUARD THAT KEEPS THIS TRUE AT FORTY PREFERENCES ────────────────────────────────

    /// <summary>
    /// Every property of <see cref="Preferences"/> is declared below with the decision taken about it: the
    /// option set or numeric range it is normalized against, or <c>null</c> for a value with no illegal state.
    /// <para>
    /// ⚠ <b>Adding a property to <c>Preferences</c> fails this test until it is added here.</b> That is the
    /// whole point — <c>Validate</c> uses <c>source with { … }</c>, so an unlisted property passes through
    /// <i>unvalidated</i> rather than loudly breaking. For a boolean that is correct (there is no illegal
    /// <c>bool</c>); for an enumerated or numeric one it is a bug no other test can see. This forces the author
    /// to decide which it is.
    /// </para>
    /// <para>
    /// ⚠ Etap 6 made the decision three-way rather than two-way, and that matters: a NUMBER has no option set
    /// but is not therefore unconstrained — its bounds are its legal set, and "carry it over untouched" would
    /// let a hand-edited <c>0</c> row limit make the SQL editor return nothing.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, object?> ValidatedProperties = new()
    {
        [nameof(Preferences.Theme)] = PreferenceOptions.Theme,
        [nameof(Preferences.Language)] = PreferenceOptions.Language,
        [nameof(Preferences.FormatterKeywordCase)] = PreferenceOptions.Casing,
        [nameof(Preferences.FormatterIdentifierCase)] = PreferenceOptions.Casing,
        [nameof(Preferences.DebuggerIsolation)] = PreferenceOptions.DebuggerIsolation,

        [nameof(Preferences.PreviewRowLimit)] = PreferenceOptions.PreviewRowLimit,
        [nameof(Preferences.FullLoadPromptThreshold)] = PreferenceOptions.FullLoadPromptThreshold,
        [nameof(Preferences.DataPageSize)] = PreferenceOptions.DataPageSize,
        [nameof(Preferences.TabStripMode)] = PreferenceOptions.TabStripMode,
        [nameof(Preferences.TabStripMaxRows)] = PreferenceOptions.TabStripMaxRows,

        // Booleans: no illegal value exists, so there is nothing for Validate to correct. Recorded as a
        // decision rather than omitted — that is what this table is for.
        [nameof(Preferences.RestoreWorkspaceOnStartup)] = null,
        [nameof(Preferences.ProcedureEasyModeDefault)] = null,
        [nameof(Preferences.ViewEasyModeDefault)] = null,
        [nameof(Preferences.TriggerEasyModeDefault)] = null,
        [nameof(Preferences.FunctionEasyModeDefault)] = null,
        [nameof(Preferences.GridAutoFitColumns)] = null,
    };

    [Fact]
    public void EveryPreference_IsAccountedForInValidation()
    {
        var declared = typeof(Preferences)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ValidatedProperties.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            declared);
    }

    /// <summary>
    /// The other direction: a property this table claims is normalized really is. Writes an ILLEGAL value into
    /// one property at a time and requires <c>Validate</c> to have corrected it — so a property listed above but
    /// forgotten inside <c>Validate</c> is caught too.
    /// <para>⚠ What counts as illegal, and as corrected, depends on the shape: an unknown key becomes the option
    /// set's default, whereas an out-of-range number is CLAMPED rather than reset (a stored 50 000 000 means "as
    /// many as possible", and answering it with the shipped 5 000 would be data loss with extra steps).</para>
    /// </summary>
    [Fact]
    public void EveryConstrainedPreference_IsActuallyNormalizedByValidate()
    {
        foreach (var (name, constraint) in ValidatedProperties)
        {
            var property = typeof(Preferences).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
            var subject = new Preferences();

            switch (constraint)
            {
                case PreferenceOptionSet set:
                    property.SetValue(subject, "value-no-build-ever-wrote");
                    Assert.Equal(set.Default, (string?)property.GetValue(PreferencesStore.Validate(subject)));
                    break;

                case PreferenceRange range:
                    // Both edges, because a one-sided clamp is a real and easy mistake.
                    property.SetValue(subject, range.Maximum + 1_000);
                    Assert.Equal(range.Maximum, (int)property.GetValue(PreferencesStore.Validate(subject))!);

                    property.SetValue(subject, range.Minimum - 1_000);
                    Assert.Equal(range.Minimum, (int)property.GetValue(PreferencesStore.Validate(subject))!);
                    break;

                default:
                    // null — nothing to normalize against, by decision (a bool has no illegal value).
                    break;
            }
        }
    }

    // ─── NUMERIC PREFERENCES (etap 6) ───────────────────────────────────────────────────────

    /// <summary>The numeric mirror of <see cref="EveryOptionSet_ContainsItsOwnDefault"/>, for the same
    /// reason: a default outside its own range would be clamped on every load, so the preference would appear
    /// to reset itself.</summary>
    [Fact]
    public void EveryRange_ContainsItsOwnDefault()
    {
        foreach (var range in PreferenceOptions.AllRanges)
        {
            Assert.True(range.Contains(range.Default));
            Assert.True(range.Minimum <= range.Maximum);
        }
    }

    /// <summary>And structural, not merely tested — the constructor refuses the bad combination.</summary>
    [Fact]
    public void Range_RefusesADefaultOutsideItself_AndAnInvertedRange()
    {
        Assert.Throws<ArgumentException>(() => new PreferenceRange(1, 10, @default: 11));
        Assert.Throws<ArgumentException>(() => new PreferenceRange(1, 10, @default: 0));
        Assert.Throws<ArgumentException>(() => new PreferenceRange(10, 1, @default: 5));
    }

    /// <summary>Clamped at both edges, never reset, and never throwing — total and silent, exactly as an
    /// enumerated preference is.</summary>
    [Fact]
    public void Range_Normalize_Clamps()
    {
        var range = new PreferenceRange(10, 100, @default: 50);

        Assert.Equal(10, range.Normalize(0));
        Assert.Equal(10, range.Normalize(int.MinValue));
        Assert.Equal(100, range.Normalize(1_000));
        Assert.Equal(100, range.Normalize(int.MaxValue));
        Assert.Equal(42, range.Normalize(42));
    }

    /// <summary>
    /// ⭐ The shipped numeric defaults ARE the constants they replace, not copies of them —
    /// <c>ExecutionDefaults</c> was written for exactly this moment, and the two page-size literals that used to
    /// live in the App's Table and View view models now come from here.
    /// </summary>
    [Fact]
    public void ShippedNumericDefaults_AreTheConstantsTheyReplace()
    {
        Assert.Equal(ExecutionDefaults.PreviewLimit, PreferenceOptions.PreviewRowLimit.Default);
        Assert.Equal((int)ExecutionDefaults.FullSoftThreshold, PreferenceOptions.FullLoadPromptThreshold.Default);
        Assert.Equal(200, PreferenceOptions.DataPageSize.Default);
        Assert.Equal(1000, PreferenceOptions.DataPageSize.Maximum);
    }

    /// <summary>
    /// ⭐ The soft threshold cannot be configured up to or past the hard memory ceiling — which is what keeps
    /// <c>ExecutionModesTests</c>' <c>soft &lt; ceiling</c> invariant true for a USER-CHOSEN value and not only
    /// for the shipped one. <c>FullSafetyCeiling</c> itself is deliberately not configurable (ratified Q9): a
    /// configurable memory backstop is not a backstop.
    /// </summary>
    [Fact]
    public void TheFullLoadPromptCannotReachTheHardSafetyCeiling()
    {
        Assert.True(PreferenceOptions.FullLoadPromptThreshold.Maximum < ExecutionDefaults.FullSafetyCeiling);
        Assert.Equal(
            (int)ExecutionDefaults.FullSafetyCeiling,
            PreferenceOptions.PreviewRowLimit.Maximum);

        // And no range admits a nonsensical zero or negative row count.
        foreach (var range in PreferenceOptions.AllRanges)
        {
            Assert.True(range.Minimum >= 1);
        }
    }

    /// <summary>
    /// The shipped defaults for the etap-6 booleans reproduce today's behaviour: tabs are restored, editors open
    /// in Source, and an unadjusted grid auto-fits — the three values that were hard-coded before this etap.
    /// </summary>
    [Fact]
    public void ShippedBooleanDefaults_ReproduceTodaysBehaviour()
    {
        var fresh = new Preferences();

        Assert.True(fresh.RestoreWorkspaceOnStartup);
        Assert.False(fresh.ProcedureEasyModeDefault);
        Assert.False(fresh.ViewEasyModeDefault);
        Assert.False(fresh.TriggerEasyModeDefault);
        Assert.False(fresh.FunctionEasyModeDefault);
        Assert.True(fresh.GridAutoFitColumns);
        Assert.Equal(PreferenceOptions.DebuggerIsolationReadCommitted, fresh.DebuggerIsolation);
    }

    /// <summary>
    /// ⭐ The tab strip's two RATIFIED numbers (product-polish §8.2, decisions D5/D7), pinned explicitly.
    ///
    /// <para>⚠ The generic theories above already prove that <c>TabStripMode</c> normalizes and
    /// <c>TabStripMaxRows</c> clamps — but they are indifferent to WHICH values those are, so neither would
    /// notice the default quietly becoming single-row or five. These are user decisions, and a user decision
    /// that lives only in a comment is one refactor away from being someone's opinion.</para>
    ///
    /// <para>⭐ The default matters more than it looks: <c>MultiRow</c> is the mode in which <b>no tab is ever
    /// hidden behind a menu</b>, which is the ratified difference from Visual Studio. Shipping
    /// <c>SingleRow</c> by default would silently reverse that.</para>
    /// </summary>
    [Fact]
    public void TabStripDefaults_AreTheRatifiedOnes()
    {
        var fresh = new Preferences();

        Assert.Equal(PreferenceOptions.TabStripModeMultiRow, fresh.TabStripMode);
        Assert.Equal(3, fresh.TabStripMaxRows);

        Assert.Equal(1, PreferenceOptions.TabStripMaxRows.Minimum);
        Assert.Equal(10, PreferenceOptions.TabStripMaxRows.Maximum);

        // ⚠ Exactly two modes. A third would need a third layout in the view, and the guard is here rather
        // than in the view because that is where it can be stated as a fact rather than as a hope.
        Assert.Equal(
            new[] { PreferenceOptions.TabStripModeMultiRow, PreferenceOptions.TabStripModeSingleRow },
            PreferenceOptions.TabStripMode.Values);
    }

    /// <summary>
    /// ⚠ The row limit SURVIVES a round trip through single-row mode — a mode is a view of the same workspace,
    /// so switching away and back must not quietly reset the number the user chose.
    /// <para>Cheap to state and easy to break: the tempting "reset the limit when it stops applying" would look
    /// like tidiness and read to the user as lost settings.</para>
    /// </summary>
    [Fact]
    public void TabStripMaxRows_SurvivesASwitchToSingleRowAndBack()
    {
        var chosen = new Preferences { TabStripMaxRows = 7 };

        var single = PreferencesStore.Validate(chosen with { TabStripMode = PreferenceOptions.TabStripModeSingleRow });
        Assert.Equal(7, single.TabStripMaxRows);

        var back = PreferencesStore.Validate(single with { TabStripMode = PreferenceOptions.TabStripModeMultiRow });
        Assert.Equal(7, back.TabStripMaxRows);
    }
}
