using System;
using System.Collections.Generic;

namespace EmberTern.LicenseManager.Localization;

/// <summary>A CLDR cardinal plural category.</summary>
/// <remarks>
/// ⚠ The full CLDR set is declared even though the two rule sets below produce only three of them, so that
/// a language with a dual or a zero form needs a rule set rather than a change to this enum.
/// </remarks>
internal enum PluralCategory
{
    /// <summary>Languages with a distinct zero form.</summary>
    Zero,

    /// <summary>Exactly one.</summary>
    One,

    /// <summary>Languages with a dual form.</summary>
    Two,

    /// <summary>The Slavic "few".</summary>
    Few,

    /// <summary>The Slavic "many".</summary>
    Many,

    /// <summary>CLDR's catch-all — every language has it.</summary>
    Other,
}

/// <summary>
/// Which plural form a count takes, per GRAMMAR rather than per language.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>A rule set names a grammar, never a language</b> (<c>one-other</c>, <c>one-few-many</c>).
/// Several languages share one shape — French and Spanish are <c>one-other</c>, Russian and Czech are
/// <c>one-few-many</c> — so a language-shaped name would be false at the second consumer, and it would
/// recreate the per-language branch that <c>NoCode_BranchesOnAParticularLanguage</c> forbids, one layer
/// further out. ⛔ Do not name a rule set "polish".</para>
///
/// <para>⭐ <b>The rendered catalog declares its own rule set</b> under <see cref="RuleSetKey"/>, so a
/// producer never states whether its sentence needs plural forms — that is a fact about the language, and
/// English may hold a flat entry where Polish declares three variants of the same key.</para>
///
/// <para>⚠ <b>What "a new language needs no code" means, stated strictly rather than generously:</b> a
/// language whose grammar is already modelled needs a catalog row and a <c>.resx</c>. A language with a
/// genuinely new grammar (Arabic has six categories, Irish five) needs a new rule set — that is, CODE. That
/// is honest: a new ALGORITHM is not a translation. ⛔ The alternative — a rule parsed from the resource at
/// run time — was considered and rejected in the product for a reason this repository has already paid
/// once: a mini-language evaluated on a path that has no right to throw.</para>
///
/// <para>⚠ Mirrored from EmberTern's <c>PluralRules</c>. The License Manager has at least nine counted
/// sentences waiting in L8.4 (seats, selected licences, issues on record, days to expiry), and the family
/// resolution lives inside <see cref="Loc"/> — so the shape has to be right before 300 texts are migrated
/// onto it, not after.</para>
/// </remarks>
internal static class PluralRules
{
    /// <summary>The catalog entry in which a culture declares its grammar.</summary>
    public const string RuleSetKey = "Localization.PluralRuleSet";

    /// <summary>English and most of Western Europe.</summary>
    public const string OneOther = "one-other";

    /// <summary>The Slavic three-form shape — Polish, Czech, Russian.</summary>
    public const string OneFewMany = "one-few-many";

    /// <summary>What an undeclared or unknown rule set falls back to.</summary>
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
            //   without it 12/13/14 would read "12 licencje" instead of "12 licencji".
            [OneFewMany] = n =>
            {
                if (n == 1)
                {
                    return PluralCategory.One;
                }

                // ⚠ A negative count is nonsense in this application, but the rule must not answer
                //   differently for a number than for its magnitude.
                var abs = n < 0 ? -n : n;
                var last = abs % 10;
                var lastTwo = abs % 100;

                return last is >= 2 and <= 4 && lastTwo is < 12 or > 14
                    ? PluralCategory.Few
                    : PluralCategory.Many;
            },
        };

    /// <summary>Every grammar this build models.</summary>
    public static IReadOnlyCollection<string> KnownRuleSets => Sets.Keys;

    /// <summary>The category <paramref name="count"/> takes under <paramref name="ruleSet"/>.</summary>
    /// <remarks>⚠ An unknown rule set uses <see cref="Fallback"/> rather than throwing.</remarks>
    public static PluralCategory CategoryFor(string? ruleSet, long count)
    {
        if (ruleSet is null || !Sets.TryGetValue(ruleSet, out var rule))
        {
            rule = Sets[Fallback];
        }

        return rule(count);
    }

    /// <summary>Whether <paramref name="ruleSet"/> is one this build models.</summary>
    public static bool IsKnown(string? ruleSet) => ruleSet is not null && Sets.ContainsKey(ruleSet);

    /// <summary>The key suffix a category uses in the catalog.</summary>
    public static string SuffixFor(PluralCategory category) => category switch
    {
        PluralCategory.Zero => "zero",
        PluralCategory.One => "one",
        PluralCategory.Two => "two",
        PluralCategory.Few => "few",
        PluralCategory.Many => "many",
        _ => "other",
    };

    /// <summary>Every category <paramref name="ruleSet"/> can actually produce.</summary>
    /// <remarks>
    /// ⭐ Measured by asking the rule, not by declaring the answer beside it — a declared list is a second
    /// copy of the rule and drifts from it silently. It is what
    /// <c>EveryPluralFamily_IsCompleteInEveryShippedCulture</c> checks a family against.
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
