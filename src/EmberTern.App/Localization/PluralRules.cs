using System;
using System.Collections.Generic;

namespace EmberTern.App.Localization;

/// <summary>
/// Which grammatical form of a counted sentence a language wants for a given number.
///
/// <para>The names are CLDR's, deliberately: they are a published, stable vocabulary that translators and
/// tooling already share, so nothing here is EmberTern's invention. ⚠ Not every language uses every member —
/// English uses two, Polish three — and a language never "runs out": <see cref="Other"/> is the category
/// every rule set falls back to.</para>
/// </summary>
internal enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

/// <summary>
/// The plural mechanism of etap C6: <b>given a count, which variant of a message key to resolve</b>.
///
/// <para>⭐⭐ <b>This is the whole mechanism, and its smallness is the point.</b> Word order, sentence
/// assembly and fragment concatenation — the other three things the old code did wrong — are not solved
/// here: they are solved by the ordinary D‑3 rule that <i>one sentence is one key</i>, which hands the whole
/// sentence to the translator. What a key alone cannot express is that Polish needs three forms of
/// <c>"{0} wiersz(e/y)"</c> where English needs two, and that is all this class decides.</para>
///
/// <para>⛔ <b>There is no language name anywhere in this file, and there must never be one.</b> A rule set is
/// named after the GRAMMAR it implements (<see cref="OneOther"/>, <see cref="OneFewMany"/>), because several
/// languages share one shape — French and Spanish are both <c>one-other</c>, Russian and Czech are both
/// <c>one-few-many</c>. Naming a set after a language would be false at its second consumer, and it would
/// re-create exactly the per-language branch <c>NoCode_BranchesOnAParticularLanguage</c> exists to forbid.
/// The link from a culture to its rule set is DATA: each <c>Strings[.culture].resx</c> declares it under
/// <see cref="RuleSetKey"/>.</para>
///
/// <para>⚠ <b>What "a new language needs no code" means here, stated exactly rather than generously.</b> A
/// language whose grammar is already modelled needs none — it names an existing rule set and ships its
/// entries. A language whose plural grammar is genuinely new (Arabic's six categories, Irish's five) needs a
/// new rule set, which is code. That is honest: a new ALGORITHM is not a translation. ⛔ The alternative —
/// a rule expression parsed out of the resource at run time — was considered and rejected: this repository
/// has already paid for a mini-language evaluated on a path that must not throw (gotcha in
/// <c>TreeDiagnostics</c>, where the tool written to keep the app alive became the thing that killed it).
/// </para>
/// </summary>
internal static class PluralRules
{
    /// <summary>
    /// The resource entry in which a culture names its rule set. ⚠ Present in the neutral (English) set, so
    /// every culture has an answer even before it is translated; a satellite overrides it.
    /// </summary>
    public const string RuleSetKey = "Localization.PluralRuleSet";

    /// <summary>Two forms, singular at exactly one. English, German, Dutch, French*, Spanish, Italian…</summary>
    public const string OneOther = "one-other";

    /// <summary>Three forms: singular, a "few" band, and the rest. Polish, Russian, Ukrainian, Czech…</summary>
    public const string OneFewMany = "one-few-many";

    /// <summary>
    /// What an unnamed or unrecognised rule set falls back to.
    /// <para>⚠ It is <see cref="OneOther"/>, not "throw" and not "no plural at all", for the same reason
    /// <c>Loc.Text</c> returns the key rather than throwing: failing to pick a grammatical form must never
    /// end a session, and a two-form guess renders a readable sentence in every language that has entries.
    /// The build-time answer is <c>EveryShippedCulture_NamesAKnownRuleSet</c>, which turns a missing or
    /// misspelt declaration into a red test rather than a quiet mis-rendering.</para>
    /// </summary>
    public const string Fallback = OneOther;

    private static readonly Dictionary<string, Func<long, PluralCategory>> Sets =
        new(StringComparer.Ordinal)
        {
            // n = 1 → one; everything else → other.
            [OneOther] = n => n == 1 ? PluralCategory.One : PluralCategory.Other,

            // CLDR's cardinal rule for the Slavic three-form shape, over integers:
            //   one  — exactly 1
            //   few  — ends in 2, 3 or 4, EXCEPT the teens 12, 13, 14
            //   many — everything else, including 0 and 5…21
            // ⚠ The teen exclusion is the part that is easy to drop and impossible to notice in English:
            // without it 12/13/14 read as "12 wiersze" instead of "12 wierszy".
            [OneFewMany] = n =>
            {
                if (n == 1) return PluralCategory.One;

                var abs = n < 0 ? -n : n;   // ⚠ a negative count is nonsense here, but the rule must not
                                            // return a different form for it than for its magnitude.
                var last = abs % 10;
                var lastTwo = abs % 100;
                return last is >= 2 and <= 4 && lastTwo is < 12 or > 14
                    ? PluralCategory.Few
                    : PluralCategory.Many;
            },
        };

    /// <summary>Every rule set this build implements, for the guards.</summary>
    public static IReadOnlyCollection<string> KnownRuleSets => Sets.Keys;

    /// <summary>The category <paramref name="count"/> falls into under <paramref name="ruleSet"/>.</summary>
    /// <param name="ruleSet">A name from <see cref="KnownRuleSets"/>; anything else uses <see cref="Fallback"/>.</param>
    /// <param name="count">The count the sentence is about — argument {0} of the message (ratified R3).</param>
    public static PluralCategory CategoryFor(string? ruleSet, long count)
    {
        if (ruleSet is null || !Sets.TryGetValue(ruleSet, out var rule))
        {
            rule = Sets[Fallback];
        }

        return rule(count);
    }

    /// <summary>Whether this build implements <paramref name="ruleSet"/>.</summary>
    public static bool IsKnown(string? ruleSet) => ruleSet is not null && Sets.ContainsKey(ruleSet);

    /// <summary>
    /// The catalog suffix for a category — <c>"Query.Exec.RowsInserted"</c> + <c>"."</c> + <c>"one"</c>.
    /// <para>⚠ Lower-case CLDR spelling, so a translator reads in the resource exactly the word the
    /// specification uses.</para>
    /// </summary>
    public static string SuffixFor(PluralCategory category) => category switch
    {
        PluralCategory.Zero => "zero",
        PluralCategory.One => "one",
        PluralCategory.Two => "two",
        PluralCategory.Few => "few",
        PluralCategory.Many => "many",
        _ => "other",
    };

    /// <summary>Every category <paramref name="ruleSet"/> can actually produce, for the completeness guard.</summary>
    /// <remarks>
    /// ⭐ Derived by RUNNING the rule over a range rather than transcribed beside it. A second, hand-written
    /// list of "which categories does one-few-many use" is a premise that breaks the day the rule moves
    /// and reports something its own name does not describe (gotcha #333).
    /// </remarks>
    public static IReadOnlyCollection<PluralCategory> CategoriesOf(string ruleSet)
    {
        var seen = new HashSet<PluralCategory>();
        for (long n = 0; n <= 200; n++)
        {
            seen.Add(CategoryFor(ruleSet, n));
        }

        return seen;
    }
}
