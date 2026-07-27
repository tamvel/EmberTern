using System;

namespace EmberTern.Office;

/// <summary>
/// Answers the one question a workbook will not answer directly: <b>is this number a date?</b>
/// <para>
/// I0 measured that a date cell is indistinguishable from any other number — <c>DataType</c> is absent and
/// <c>CellValue</c> is a serial like <c>45000</c>. The ONLY signal is the cell's number format (design R3), so
/// this class is where "45000 is 2023-03-15" is decided, and it is deliberately separate from the reader: it is
/// a pure function of (format id, format code) and is unit-tested without a file.
/// </para>
/// <para>
/// ⭐ <b>Why this is not the three-line version.</b> The I0 probe asked
/// <c>code.Contains('d') || code.Contains('y') || …</c> over the raw format code. Run against the real user
/// file's own custom format —
/// <c>#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]</c>, which I0 itself labelled <em>currency, not a date</em> — that test
/// answers TRUE, because <c>[Red]</c> contains a <c>d</c>. The probe never noticed because no cell in that file
/// used the style; a production reader would have turned a money column into dates, silently, which is §0.1's
/// worst class of failure. So a format code is parsed rather than searched: quoted literals, escapes and
/// bracketed sections (colour, currency, locale, condition) are skipped, and only the tokens that genuinely mean
/// a date or time part count.
/// </para>
/// </summary>
internal static class SpreadsheetNumberFormats
{
    /// <summary>
    /// True when a numeric cell carrying this format should be read as a date/time.
    /// </summary>
    /// <param name="numberFormatId">The style's <c>NumberFormatId</c>.</param>
    /// <param name="formatCode">The custom format code when <paramref name="numberFormatId"/> is a custom id
    /// (≥ 164) and the workbook declares one; <c>null</c> otherwise.</param>
    public static bool IsDateFormat(uint numberFormatId, string? formatCode)
    {
        // Built-in date/time formats. The ranges REK-6 point 4 ratified — deliberately not widened here: the
        // East-Asian built-ins carry explicit format codes in every file measured, so they are caught by the
        // code path below without guessing at ids nobody verified.
        if (numberFormatId is >= 14 and <= 22) return true;
        if (numberFormatId is >= 45 and <= 47) return true;

        return !string.IsNullOrEmpty(formatCode) && SectionMentionsDateOrTime(FirstSection(formatCode));
    }

    /// <summary>
    /// The positive-number section of a format code. A code may carry up to four <c>;</c>-separated sections
    /// (positive; negative; zero; text) and they all describe the same underlying value, so the first is
    /// representative — and using it avoids <c>[Red]</c>-style decoration in the negative section, which is where
    /// the naive test went wrong.
    /// </summary>
    private static ReadOnlySpan<char> FirstSection(string code)
    {
        for (var i = 0; i < code.Length; i++)
        {
            switch (code[i])
            {
                case '"':
                    i = SkipQuoted(code, i);
                    break;
                case '\\':
                case '_':
                case '*':
                    i++; // the next character is escaped / a width or fill placeholder — never a token
                    break;
                case '[':
                    i = SkipBracketed(code, i);
                    break;
                case ';':
                    return code.AsSpan(0, i);
            }
        }
        return code.AsSpan();
    }

    private static bool SectionMentionsDateOrTime(ReadOnlySpan<char> section)
    {
        for (var i = 0; i < section.Length; i++)
        {
            var c = section[i];
            switch (c)
            {
                case '"':
                    // Literal text: 0 "dni" is a count of days, not a date.
                    while (++i < section.Length && section[i] != '"') { }
                    break;

                case '\\':
                case '_':
                case '*':
                    i++;
                    break;

                case '[':
                {
                    var close = section[(i + 1)..].IndexOf(']');
                    if (close < 0) return false; // malformed code — refuse rather than guess (§0)
                    var inner = section.Slice(i + 1, close);
                    // [h] / [mm] / [ss] are ELAPSED-time tokens and do mean time. Everything else in brackets is
                    // decoration: [Red], [$€-1], [<100], [$-409].
                    if (IsElapsedTimeToken(inner)) return true;
                    i += close + 1;
                    break;
                }

                // Date/time tokens. 'm' counts because it is month or minute — both are date/time, and neither
                // occurs unescaped in a numeric format.
                case 'y' or 'Y' or 'd' or 'D' or 'h' or 'H' or 's' or 'S' or 'm' or 'M':
                    return true;

                case 'a' or 'A':
                    // AM/PM markers. Anything else starting with 'a' is not a token.
                    if (StartsWithIgnoreCase(section[i..], "am/pm") || StartsWithIgnoreCase(section[i..], "a/p"))
                        return true;
                    break;
            }
        }
        return false;
    }

    /// <summary>True for <c>[h]</c>, <c>[hh]</c>, <c>[m]</c>, <c>[ss]</c> … — a run of one elapsed-time letter.</summary>
    private static bool IsElapsedTimeToken(ReadOnlySpan<char> inner)
    {
        if (inner.Length == 0) return false;

        var first = char.ToLowerInvariant(inner[0]);
        if (first is not ('h' or 'm' or 's')) return false;

        foreach (var c in inner)
        {
            if (char.ToLowerInvariant(c) != first) return false;
        }
        return true;
    }

    private static bool StartsWithIgnoreCase(ReadOnlySpan<char> span, string value)
        => span.StartsWith(value, StringComparison.OrdinalIgnoreCase);

    private static int SkipQuoted(string code, int openIndex)
    {
        for (var i = openIndex + 1; i < code.Length; i++)
        {
            if (code[i] == '"') return i;
        }
        return code.Length;
    }

    private static int SkipBracketed(string code, int openIndex)
    {
        for (var i = openIndex + 1; i < code.Length; i++)
        {
            if (code[i] == ']') return i;
        }
        return code.Length;
    }
}
