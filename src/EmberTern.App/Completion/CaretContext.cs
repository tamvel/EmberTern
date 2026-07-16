using System;
using AvaloniaEdit.Document;
using EmberTern.Core.Sql;

namespace EmberTern.App.Completion;

/// <summary>
/// Reads the identifier / dot context immediately before a caret <b>directly
/// from the AvaloniaEdit document</b>, without materializing the whole editor
/// text on every keystroke (design §7.5 / §15.5). It performs a small bounded
/// backward scan and returns <b>document-absolute</b> offsets, so callers can
/// place a <c>CompletionWindow</c> segment exactly where the Core string-based
/// helpers would have.
/// </summary>
/// <remarks>
/// The scanning rules mirror <see cref="SqlCompletionContext.GetCurrentWord"/> /
/// <see cref="SqlCompletionContext.GetDotContext"/> exactly — same identifier
/// predicate (<see cref="SqlCompletionContext.IsIdentifierChar"/>), same dot /
/// qualifier walk, same uppercase of the qualifier. Only the <i>input</i>
/// differs (a bounded slice of the document rather than the entire string), so
/// results are identical for any realistic identifier. This equivalence is
/// pinned by <c>CaretContextTests</c>.
///
/// Works off <see cref="ITextSource"/> (implemented by both
/// <see cref="TextDocument"/> and <see cref="StringTextSource"/>), which keeps
/// it unit-testable without a window.
/// </remarks>
internal static class CaretContext
{
    // A qualifier + partial identifier is never remotely this long (Firebird
    // identifiers are ≤31/63 chars). The cap bounds the per-keystroke scan so a
    // pathological unbroken run can't turn it into an O(n) walk.
    private const int MaxLookBack = 512;

    /// <summary>
    /// The identifier run ending at <paramref name="caret"/>, with
    /// document-absolute offsets. Empty when the caret isn't adjacent to an
    /// identifier character. Mirror of <see cref="SqlCompletionContext.GetCurrentWord"/>.
    /// </summary>
    public static CurrentWord GetCurrentWord(ITextSource source, int caret)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (caret < 0 || caret > source.TextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(caret));
        }

        int start = caret;
        int limit = Math.Max(0, caret - MaxLookBack);
        while (start > limit && SqlCompletionContext.IsIdentifierChar(source.GetCharAt(start - 1)))
        {
            start--;
        }

        int length = caret - start;
        var text = length == 0 ? string.Empty : source.GetText(start, length);
        return new CurrentWord(start, length, text);
    }

    /// <summary>
    /// Detects a <c>QUALIFIER.[prefix]</c> context ending at
    /// <paramref name="caret"/> and returns document-absolute offsets, or null
    /// when the caret isn't in a dot context. Mirror of
    /// <see cref="SqlCompletionContext.GetDotContext"/> (qualifier uppercased).
    /// </summary>
    public static DotContext? GetDotContext(ITextSource source, int caret)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (caret < 0 || caret > source.TextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(caret));
        }

        // The partial identifier being typed after the dot.
        int prefixEnd = caret;
        int prefixStart = prefixEnd;
        int prefixLimit = Math.Max(0, prefixEnd - MaxLookBack);
        while (prefixStart > prefixLimit && SqlCompletionContext.IsIdentifierChar(source.GetCharAt(prefixStart - 1)))
        {
            prefixStart--;
        }

        // Expect a '.' immediately before the prefix.
        if (prefixStart == 0 || source.GetCharAt(prefixStart - 1) != '.')
        {
            return null;
        }

        // The qualifier identifier left of the dot.
        int qualEnd = prefixStart - 1;
        int qualStart = qualEnd;
        int qualLimit = Math.Max(0, qualEnd - MaxLookBack);
        while (qualStart > qualLimit && SqlCompletionContext.IsIdentifierChar(source.GetCharAt(qualStart - 1)))
        {
            qualStart--;
        }

        if (qualStart == qualEnd)
        {
            // No qualifier (e.g. ". something") — not a dot context.
            return null;
        }

        var qualifier = source.GetText(qualStart, qualEnd - qualStart).ToUpperInvariant();
        var prefix = prefixEnd == prefixStart
            ? string.Empty
            : source.GetText(prefixStart, prefixEnd - prefixStart);
        return new DotContext(qualifier, prefixStart, prefixEnd - prefixStart, prefix);
    }
}
