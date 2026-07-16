using EmberTern.Core.Sql;

namespace EmberTern.Core.Sql.Language.Constructs;

/// <summary>
/// The exact document edit that expands an armed construct: replace <see cref="Length"/> characters at
/// <see cref="Start"/> with <see cref="InsertText"/>, then place the caret at
/// <see cref="Start"/> + <see cref="CaretOffset"/>. Pure data — the App applies it verbatim.
/// </summary>
public sealed record ExpansionEdit(int Start, int Length, string InsertText, int CaretOffset);

/// <summary>
/// Turns a resolved <see cref="ConstructMatch"/> into the concrete <see cref="ExpansionEdit"/> — including
/// the one <b>decision</b> the App would otherwise be tempted to make itself: casing. The construct's
/// canonical (lowercase) expansion is cased to match what the developer just typed (via
/// <see cref="CaseMatcher"/> / <see cref="SqlCaseStyleDetector"/>), so <c>IF</c>+Tab yields
/// <c>IF () THEN</c> and <c>if</c>+Tab yields <c>if () then</c>. Pure Core; the App only applies the edit.
/// </summary>
public static class ConstructExpansion
{
    /// <summary>Builds the edit for <paramref name="match"/> at <paramref name="caret"/> in
    /// <paramref name="text"/>. The caret offset is preserved because casing never changes length.</summary>
    public static ExpansionEdit For(string text, int caret, ConstructMatch match)
    {
        int start = caret - match.PrefixLength;
        var typed = text.Substring(start, match.PrefixLength);
        var style = SqlCaseStyleDetector.Detect(text);
        var insert = CaseMatcher.Match(typed, match.Construct.Expansion, style);
        return new ExpansionEdit(start, match.PrefixLength, insert, match.Construct.CaretOffset);
    }
}
