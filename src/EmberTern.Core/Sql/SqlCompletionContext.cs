using System;

namespace EmberTern.Core.Sql;

// Pure helpers that drive the SQL editor's autocomplete trigger. Lives in Core
// so the rules ("what counts as the current identifier", "when to auto-pop")
// are unit-testable without standing up AvaloniaEdit.
public static class SqlCompletionContext
{
    public const int AutoTriggerMinLength = 3;

    /// <summary>
    /// Returns the identifier that ends at <paramref name="caretOffset"/> in
    /// <paramref name="text"/>. An identifier is a maximal run of letters,
    /// digits and underscores (no leading-digit restriction — Firebird allows
    /// names like "1NF_TABLE" in quoted form, and for completion purposes a
    /// leading-digit token still gives a useful prefix to filter against).
    /// Returns an empty token at <paramref name="caretOffset"/> when the caret
    /// is not adjacent to identifier characters (e.g. after whitespace or
    /// punctuation).
    /// </summary>
    public static CurrentWord GetCurrentWord(string text, int caretOffset)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (caretOffset < 0 || caretOffset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(caretOffset));
        }

        int start = caretOffset;
        while (start > 0 && IsIdentifierChar(text[start - 1]))
        {
            start--;
        }
        var length = caretOffset - start;
        var value = length == 0 ? string.Empty : text.Substring(start, length);
        return new CurrentWord(start, length, value);
    }

    /// <summary>
    /// Whether the completion window should auto-pop after the user typed a
    /// character. True when the current word has reached the minimum length
    /// AND is a pure identifier (no surprises from quoted strings or numbers).
    /// </summary>
    public static bool ShouldAutoTrigger(string currentWord)
    {
        if (string.IsNullOrEmpty(currentWord)) return false;
        if (currentWord.Length < AutoTriggerMinLength) return false;
        // Reject pure numeric runs ("123") — those aren't completable identifiers.
        bool anyLetter = false;
        foreach (var c in currentWord)
        {
            if (!IsIdentifierChar(c)) return false;
            if (char.IsLetter(c) || c == '_') anyLetter = true;
        }
        return anyLetter;
    }

    public static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Returns the identifier covering <paramref name="offset"/> in
    /// <paramref name="text"/>. Walks both directions from the offset, so a
    /// caret anywhere inside or at either edge of an identifier yields the
    /// full word. Empty when the offset doesn't touch any identifier char on
    /// either side (e.g. between two spaces, or right after a comma).
    /// Used by the SQL editor's double-click handler.
    /// </summary>
    public static CurrentWord GetWordAt(string text, int offset)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (offset < 0 || offset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        int start = offset;
        while (start > 0 && IsIdentifierChar(text[start - 1]))
        {
            start--;
        }
        int end = offset;
        while (end < text.Length && IsIdentifierChar(text[end]))
        {
            end++;
        }
        var length = end - start;
        var value = length == 0 ? string.Empty : text.Substring(start, length);
        return new CurrentWord(start, length, value);
    }

    /// <summary>
    /// Detects whether the caret is positioned right after <c>QUALIFIER.</c>
    /// (possibly followed by a partial identifier) and returns the qualifier
    /// plus the partial-identifier prefix. Returns null when the caret isn't
    /// in a dot context — caller should fall back to plain word completion.
    /// </summary>
    /// <remarks>
    /// Examples (<c>|</c> is the caret):
    ///   <list type="bullet">
    ///     <item><c>N.|</c> → qualifier "N", prefix "" at offset 2.</item>
    ///     <item><c>N.ID|</c> → qualifier "N", prefix "ID" at offset 2.</item>
    ///     <item><c>FROM NAGL N JOIN POZ P ON P.|</c> → qualifier "P", empty prefix.</item>
    ///     <item><c>WHERE x = 1|</c> → null (no dot in scope).</item>
    ///   </list>
    /// Qualifier is uppercased to match Firebird's catalog naming.
    /// </remarks>
    public static DotContext? GetDotContext(string text, int caretOffset)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (caretOffset < 0 || caretOffset > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(caretOffset));
        }

        // Walk back through any identifier characters (the partial prefix).
        int prefixEnd = caretOffset;
        int prefixStart = prefixEnd;
        while (prefixStart > 0 && IsIdentifierChar(text[prefixStart - 1]))
        {
            prefixStart--;
        }

        // Expect a '.' immediately before the prefix.
        if (prefixStart == 0 || text[prefixStart - 1] != '.')
        {
            return null;
        }

        // Walk back through the qualifier identifier.
        int qualEnd = prefixStart - 1;
        int qualStart = qualEnd;
        while (qualStart > 0 && IsIdentifierChar(text[qualStart - 1]))
        {
            qualStart--;
        }

        if (qualStart == qualEnd)
        {
            // No qualifier (e.g. ". something") — not a dot context.
            return null;
        }

        var qualifier = text.Substring(qualStart, qualEnd - qualStart).ToUpperInvariant();
        var prefix = prefixEnd == prefixStart
            ? string.Empty
            : text.Substring(prefixStart, prefixEnd - prefixStart);
        return new DotContext(qualifier, prefixStart, prefixEnd - prefixStart, prefix);
    }
}

public readonly record struct CurrentWord(int Start, int Length, string Text)
{
    public int End => Start + Length;
    public bool IsEmpty => Length == 0;
}

/// <summary>
/// Result of <see cref="SqlCompletionContext.GetDotContext"/>: the identifier
/// immediately to the left of the caret's dot, plus the partial identifier
/// being typed after it.
/// </summary>
/// <param name="Qualifier">The name left of the dot (uppercase, no quotes).</param>
/// <param name="PrefixStart">Document offset of the first prefix char.</param>
/// <param name="PrefixLength">Length of the prefix (0 immediately after the dot).</param>
/// <param name="Prefix">The literal prefix text, verbatim from the source.</param>
public readonly record struct DotContext(string Qualifier, int PrefixStart, int PrefixLength, string Prefix)
{
    public int PrefixEnd => PrefixStart + PrefixLength;
}

