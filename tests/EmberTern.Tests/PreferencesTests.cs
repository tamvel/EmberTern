using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    /// <summary>⭐ The language catalog: one row today, and adding Polish must be one row here — not a window
    /// change, a view-model change or a binding change.</summary>
    [Fact]
    public void LanguageCatalog_HasExactlyEnglish_AndDefaultsToIt()
    {
        Assert.Equal(new[] { "en" }, PreferenceOptions.Language.Values);
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
    /// ⭐ <b>Language is validated from day one, although nothing consumes it.</b> It is precisely because it
    /// has no reader that it is the property most likely to be left unvalidated "until it matters", and the
    /// localization milestone is far enough away that a bad value would be thoroughly entrenched by then.
    /// </summary>
    [Fact]
    public void Validate_NormalizesLanguage_EvenThoughNothingReadsItYet()
    {
        Assert.Equal("en", PreferencesStore.Validate(new Preferences { Language = "pl" }).Language);
        Assert.Equal("en", PreferencesStore.Validate(new Preferences { Language = "" }).Language);
    }

    // ─── THE GUARD THAT KEEPS THIS TRUE AT FORTY PREFERENCES ────────────────────────────────

    /// <summary>
    /// Every property of <see cref="Preferences"/> is declared below with the decision taken about it:
    /// the option set it is normalized against, or <c>null</c> for a value with no fixed legal set.
    /// <para>
    /// ⚠ <b>Adding a property to <c>Preferences</c> fails this test until it is added here.</b> That is the
    /// whole point — <c>Validate</c> uses <c>source with { … }</c>, so an unlisted property passes through
    /// <i>unvalidated</i> rather than loudly breaking. For a free-text preference that is correct; for an
    /// enumerated one it is a bug that no other test can see. This forces the author to decide which it is.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, PreferenceOptionSet?> ValidatedProperties = new()
    {
        [nameof(Preferences.Theme)] = PreferenceOptions.Theme,
        [nameof(Preferences.Language)] = PreferenceOptions.Language,
        [nameof(Preferences.FormatterKeywordCase)] = PreferenceOptions.Casing,
        [nameof(Preferences.FormatterIdentifierCase)] = PreferenceOptions.Casing,
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
    /// The other direction: a property this table claims is normalized really is. Writes garbage into one
    /// property at a time and requires <c>Validate</c> to have replaced it with that set's default — so a
    /// property listed above but forgotten inside <c>Validate</c> is caught too.
    /// </summary>
    [Fact]
    public void EveryEnumeratedPreference_IsActuallyNormalizedByValidate()
    {
        foreach (var (name, set) in ValidatedProperties)
        {
            if (set is null) continue;   // no fixed legal set — nothing to normalize against, by decision

            var property = typeof(Preferences).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
            var subject = new Preferences();
            property.SetValue(subject, "value-no-build-ever-wrote");

            var actual = (string?)property.GetValue(PreferencesStore.Validate(subject));

            Assert.Equal(set.Default, actual);
        }
    }
}
