using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.CodeActions;

/// <summary>
/// Picks the one name a misspelling almost certainly meant — the basis of the "Did you mean …?" quick
/// fixes (Stage Q / Q4).
/// <para>
/// <b>Exactly one, or nothing.</b> A fix rewrites the user's code, so a menu of plausible guesses is
/// the wrong answer: if two candidates are equally close, the tool does not know what was meant and says
/// nothing. That is the diagnostics engine's "prefer silence over false positives" rule at the higher bar
/// mutation demands (Architecture rule #11).
/// </para>
/// <para>Comparison is case-insensitive because Firebird folds unquoted identifiers, so a difference of
/// case alone is not a typo — it is the same name.</para>
/// </summary>
internal static class NameSuggestion
{
    /// <summary>
    /// The single candidate close enough to <paramref name="typed"/> to be offered, or <c>null</c> when
    /// none is close enough or more than one is.
    /// </summary>
    public static string? Best(string typed, IEnumerable<string> candidates)
    {
        if (string.IsNullOrEmpty(typed) || candidates is null) return null;

        int budget = Budget(typed.Length);
        string? best = null;
        int bestDistance = int.MaxValue;
        bool tied = false;

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            // Identical (ignoring case) is not a typo — it is the same identifier, and offering to
            // "fix" it would be nonsense.
            if (string.Equals(candidate, typed, StringComparison.OrdinalIgnoreCase)) return null;

            int d = Distance(typed, candidate, budget);
            if (d > budget) continue;

            if (d < bestDistance)
            {
                bestDistance = d;
                best = candidate;
                tied = false;
            }
            else if (d == bestDistance && !string.Equals(candidate, best, StringComparison.OrdinalIgnoreCase))
            {
                tied = true;
            }
        }

        return tied ? null : best;
    }

    /// <summary>
    /// Re-casts <paramref name="suggestion"/> in the style the user was writing in, rather than the
    /// style the catalog happens to store.
    /// <para>
    /// A quick fix should repair the mistake and change nothing else. Firebird folds unquoted
    /// identifiers, so <c>v_zmienna</c> and <c>V_ZMIENNA</c> are the SAME name — which means taking the
    /// catalog's spelling would be a gratuitous restyling of the user's code, not part of the repair.
    /// Case is applied per character, so a mixed style is preserved too (<c>V_ZmiennaX</c> →
    /// <c>V_Zmienna</c>); past the end of what they typed, the case of their last letter continues.
    /// </para>
    /// </summary>
    public static string MatchCase(string typed, string suggestion)
    {
        if (string.IsNullOrEmpty(typed) || string.IsNullOrEmpty(suggestion)) return suggestion;

        var result = new char[suggestion.Length];
        bool upper = char.IsUpper(typed[0]);
        for (int i = 0; i < suggestion.Length; i++)
        {
            // Only letters carry a case; an underscore or digit inherits whatever style was in force,
            // so `V_Zmienna` keeps its capital Z rather than being reset by the separator.
            if (i < typed.Length && char.IsLetter(typed[i])) upper = char.IsUpper(typed[i]);
            result[i] = upper ? char.ToUpperInvariant(suggestion[i]) : char.ToLowerInvariant(suggestion[i]);
        }
        return new string(result);
    }

    // How far wrong a name may be and still be recognisable. Short names get one edit: at three or four
    // characters, two edits can reach an unrelated identifier, and a confident wrong suggestion is worse
    // than none. Nothing below three characters is guessed at at all.
    private static int Budget(int length) => length switch
    {
        < 3 => 0,
        <= 4 => 1,
        _ => 2,
    };

    /// <summary>
    /// Levenshtein distance, case-insensitive, abandoned as soon as it cannot come in at or under
    /// <paramref name="budget"/> — the candidate lists here are whole-catalog sized, so the early exit
    /// is what keeps this cheap enough to run while a menu opens.
    /// </summary>
    internal static int Distance(string a, string b, int budget)
    {
        if (budget < 0) return int.MaxValue;
        if (Math.Abs(a.Length - b.Length) > budget) return int.MaxValue; // cannot be closed by edits

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowBest = current[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = char.ToUpperInvariant(a[i - 1]) == char.ToUpperInvariant(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
                if (current[j] < rowBest) rowBest = current[j];
            }
            if (rowBest > budget) return int.MaxValue; // no cell in this row can still reach the budget

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
