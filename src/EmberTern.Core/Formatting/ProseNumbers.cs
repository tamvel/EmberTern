using System.Text.RegularExpressions;

namespace EmberTern.Core.Formatting;

/// <summary>
/// Keeps a grouped number from being broken across lines inside wrapping prose.
///
/// <para>⭐ <b>The problem is real and was seen on a render:</b> <c>"Between 1 and 1 000 000."</c> wrapped as
/// <c>"Between 1 and 1"</c> / <c>"000 000."</c>, which reads as two numbers rather than one. A thousands
/// separator written as an ordinary space is a legal break opportunity, so the layout engine is behaving
/// correctly — the text is what is wrong.</para>
///
/// <para>⚠ <b>Deliberately not solved by disabling wrapping</b> (the description must still wrap) and not by
/// editing one string (the next description with a number would break the same way). The rule is about a
/// SHAPE — a space between two digits — so it holds for every present and future text without naming any of
/// them.</para>
///
/// <para>⭐ It replaces only the separator INSIDE a number: <c>"1 and 1 000 000"</c> keeps its ordinary,
/// breakable spaces around <c>and</c>, so a long range still wraps where it reads naturally, while
/// <c>1 000 000</c> travels as one unit.</para>
/// </summary>
public static partial class ProseNumbers
{
    /// <summary>U+00A0. Renders exactly like a space and carries no line-break opportunity.</summary>
    private const char NoBreakSpace = ' ';

    /// <summary>A space with a digit on both sides — a thousands separator, never a word gap.</summary>
    [GeneratedRegex(@"(?<=\d) (?=\d)")]
    private static partial Regex GroupSeparator();

    /// <summary>
    /// Returns <paramref name="text"/> with every in-number space turned into a non-breaking space.
    /// <para>⚠ Presentation only — apply it to what is DISPLAYED, never to a value that is searched, stored or
    /// compared, or a user typing an ordinary space would stop matching it.</para>
    /// </summary>
    public static string KeepNumbersWhole(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : GroupSeparator().Replace(text, NoBreakSpace.ToString());
}
