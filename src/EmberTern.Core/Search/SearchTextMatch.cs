using System;

namespace EmberTern.Core.Search;

/// <summary>
/// Pure text-matching primitives shared by the name matcher and the source-body
/// count in <c>FirebirdMetadataSearchReader</c>. Substring semantics mirror
/// Firebird's <c>CONTAINING</c> (case-insensitive by default); whole-word applies
/// only where we control the match client-side (names), never to source.
/// </summary>
public static class SearchTextMatch
{
    /// <summary>True when <paramref name="text"/> contains <paramref name="term"/>.</summary>
    public static bool Contains(string? text, string? term, bool caseSensitive, bool wholeWord)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term)) return false;
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!wholeWord) return text!.IndexOf(term!, cmp) >= 0;

        int idx = 0;
        while ((idx = text!.IndexOf(term!, idx, cmp)) >= 0)
        {
            if (IsWholeWordAt(text, idx, term!.Length)) return true;
            idx += term!.Length;
        }
        return false;
    }

    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="term"/> in
    /// <paramref name="text"/>. Used for the per-object match count (e.g. PROC_A [4]).
    /// </summary>
    public static int CountOccurrences(string? text, string? term, bool caseSensitive, bool wholeWord = false)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term)) return 0;
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int count = 0, idx = 0;
        while ((idx = text!.IndexOf(term!, idx, cmp)) >= 0)
        {
            if (!wholeWord || IsWholeWordAt(text, idx, term!.Length)) count++;
            idx += term!.Length;
        }
        return count;
    }

    // A match at [start, start+len) is a whole word when the chars immediately
    // before and after are not identifier chars (letter/digit/underscore/$).
    private static bool IsWholeWordAt(string text, int start, int len)
    {
        bool leftOk = start == 0 || !IsIdentChar(text[start - 1]);
        int after = start + len;
        bool rightOk = after >= text.Length || !IsIdentChar(text[after]);
        return leftOk && rightOk;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}
