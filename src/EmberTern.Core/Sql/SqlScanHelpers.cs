using System.Collections.Generic;

namespace EmberTern.Core.Sql;

/// <summary>
/// Small string-scanning primitives shared by the procedure signature parser and
/// body scanner. Pure, no grammar — the same lightweight approach as
/// <see cref="SqlStatementClassifier"/> / <see cref="SqlFormatter"/>. Trivia =
/// whitespace + line (<c>--</c>) and block (<c>/* */</c>) comments. Quoted runs
/// are single-quoted string literals (<c>'…'</c>, <c>''</c> escape) and
/// double-quoted identifiers (<c>"…"</c>, <c>""</c> escape).
/// </summary>
internal static class SqlScanHelpers
{
    public static bool IsIdentifierChar(char c)
        => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    public static void SkipTrivia(string s, ref int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                i += 2;
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i = i + 1 < s.Length ? i + 2 : s.Length;
                continue;
            }
            break;
        }
    }

    /// <summary>If the cursor sits on a quote char, skips the whole quoted run
    /// (handling the doubled-quote escape) and returns true; otherwise false.</summary>
    public static bool TrySkipQuoted(string s, ref int i)
    {
        if (i >= s.Length) return false;
        char q = s[i];
        if (q != '\'' && q != '"') return false;
        i++;
        while (i < s.Length)
        {
            if (s[i] == q)
            {
                if (i + 1 < s.Length && s[i + 1] == q) { i += 2; continue; } // escaped
                i++;
                return true;
            }
            i++;
        }
        return true; // unterminated — consumed to end
    }

    /// <summary>Reads an identifier run (letters/digits/_/$). Does not handle
    /// quoting — use for keyword matching. Advances past the run.</summary>
    public static string ReadWord(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && IsIdentifierChar(s[i])) i++;
        return s.Substring(start, i - start);
    }

    /// <summary>Reads a quoted (<c>"…"</c>) or unquoted identifier, returning the
    /// unquoted name (internal <c>""</c> collapsed to <c>"</c>), or null when the
    /// cursor isn't on an identifier. Advances past it.</summary>
    public static string? ReadIdentifier(string s, ref int i)
    {
        if (i >= s.Length) return null;
        if (s[i] == '"')
        {
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                if (s[i] == '"')
                {
                    if (i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                    i++;
                    return sb.ToString();
                }
                sb.Append(s[i]);
                i++;
            }
            return sb.ToString();
        }
        if (!IsIdentifierChar(s[i])) return null;
        var w = ReadWord(s, ref i);
        return w.Length == 0 ? null : w;
    }

    /// <summary>Matches a keyword case-insensitively at the cursor (must be
    /// followed by a non-identifier boundary). On match, advances past it and
    /// returns true; otherwise leaves the cursor and returns false.</summary>
    public static bool TryKeyword(string s, ref int i, string keyword)
    {
        int j = i;
        for (int k = 0; k < keyword.Length; k++)
        {
            if (j >= s.Length) return false;
            if (char.ToUpperInvariant(s[j]) != char.ToUpperInvariant(keyword[k])) return false;
            j++;
        }
        // boundary: next char must not continue an identifier
        if (j < s.Length && IsIdentifierChar(s[j])) return false;
        i = j;
        return true;
    }

    /// <summary>With the cursor on <c>'('</c>, returns the text inside the matching
    /// parens (string-aware, nesting-aware) and advances past the closing
    /// <c>')'</c>. Returns null when the parens are unbalanced.</summary>
    public static string? ReadParenBlock(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '(') return null;
        i++;
        int start = i;
        int depth = 1;
        while (i < s.Length)
        {
            if (TrySkipQuoted(s, ref i)) continue;
            char c = s[i];
            if (c == '(') { depth++; i++; continue; }
            if (c == ')')
            {
                depth--;
                if (depth == 0)
                {
                    var inner = s.Substring(start, i - start);
                    i++;
                    return inner;
                }
                i++;
                continue;
            }
            i++;
        }
        return null;
    }

    /// <summary>Consumes up to and including the next top-level <c>;</c>
    /// (string-aware, paren-aware), returning the text before it.</summary>
    public static string ReadUntilSemicolon(string s, ref int i)
    {
        int start = i;
        int depth = 0;
        while (i < s.Length)
        {
            if (TrySkipQuoted(s, ref i)) continue;
            char c = s[i];
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (c == ';' && depth == 0)
            {
                var text = s.Substring(start, i - start);
                i++;
                return text;
            }
            i++;
        }
        return s.Substring(start);
    }

    /// <summary>Splits a comma-separated list at top level (string-aware,
    /// paren-aware). Empty/whitespace segments are dropped.</summary>
    public static IReadOnlyList<string> SplitTopLevelCommas(string s)
    {
        var parts = new List<string>();
        int start = 0;
        int depth = 0;
        int i = 0;
        while (i < s.Length)
        {
            if (TrySkipQuoted(s, ref i)) continue;
            char c = s[i];
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (c == ',' && depth == 0)
            {
                AddIfNotBlank(parts, s.Substring(start, i - start));
                start = i + 1;
            }
            i++;
        }
        AddIfNotBlank(parts, s.Substring(start));
        return parts;
    }

    /// <summary>True when <paramref name="word"/> appears as a whole identifier
    /// token in <paramref name="text"/> (case-insensitive, skipping quoted runs).</summary>
    public static bool ContainsWord(string text, string word)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (TrySkipQuoted(text, ref i)) continue;
            if (IsIdentifierChar(text[i]))
            {
                var w = ReadWord(text, ref i);
                if (string.Equals(w, word, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            else i++;
        }
        return false;
    }

    private static void AddIfNotBlank(List<string> list, string seg)
    {
        var t = seg.Trim();
        if (t.Length > 0) list.Add(t);
    }

    // ─── CASE-aware BEGIN…END block scanning ──────────────────────────────────
    //
    // A bare BEGIN/END counter is corrupted by CASE … END (the CASE's END drops the
    // counter with no matching BEGIN), truncating a block before its real END — the
    // recurring defect class behind gotchas #117 / #128. The scanner below counts
    // BOTH BEGIN and CASE as openers (CASE only once we're inside the BEGIN block)
    // and END as the closer, and skips string literals + comments, so a CASE … END
    // (or a BEGIN/END/CASE keyword inside a literal or comment) never ends the block
    // early. Shared so every BEGIN…END region scanner uses one correct implementation.

    /// <summary>
    /// Locates the outermost <c>BEGIN … END</c> block starting at/after
    /// <paramref name="from"/>. CASE counts as a nested opener (so a <c>CASE … END</c>
    /// inside the block doesn't close it early); string literals and comments are
    /// skipped. On success returns true and sets <paramref name="contentStart"/> /
    /// <paramref name="contentEnd"/> to the range strictly between the outer
    /// <c>BEGIN</c> and its matching <c>END</c>, and <paramref name="afterBlock"/> to
    /// the index just past that <c>END</c> and an optional trailing <c>;</c>.
    /// </summary>
    private static bool TryScanBeginEndBlock(
        string s, int from, out int contentStart, out int contentEnd, out int afterBlock)
    {
        contentStart = contentEnd = afterBlock = -1;
        int i = from;
        int depth = 0;
        int cStart = -1;
        while (i < s.Length)
        {
            SkipTrivia(s, ref i);
            if (i >= s.Length) break;
            if (TrySkipQuoted(s, ref i)) continue;
            if (!IsIdentifierChar(s[i])) { i++; continue; }

            int tokStart = i;
            var u = ReadWord(s, ref i).ToUpperInvariant();
            if (u == "BEGIN")
            {
                if (depth == 0) cStart = i; // content begins right after the outer BEGIN
                depth++;
            }
            else if (u == "CASE")
            {
                if (cStart >= 0) depth++; // CASE only nests once we're inside the block
            }
            else if (u == "END")
            {
                if (cStart >= 0 && depth == 1)
                {
                    contentStart = cStart;
                    contentEnd = tokStart;
                    SkipTrivia(s, ref i);
                    if (i < s.Length && s[i] == ';') i++;
                    afterBlock = i;
                    return true;
                }
                if (depth > 0) depth--;
            }
        }
        return false;
    }

    /// <summary>Returns the content range strictly between the outermost
    /// <c>BEGIN</c> and its matching <c>END</c> (CASE-aware, string + comment aware),
    /// or null when there is no top-level <c>BEGIN … END</c>.</summary>
    public static (int Start, int End)? FindOuterBeginEndContent(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        return TryScanBeginEndBlock(text!, 0, out int cs, out int ce, out _)
            ? (cs, ce)
            : null;
    }

    /// <summary>Advances the cursor past the next <c>BEGIN … END</c> block (CASE-aware,
    /// string + comment aware) and an optional trailing <c>;</c>. When there is no
    /// <c>BEGIN … END</c> at/after the cursor, advances to the end of the string.</summary>
    public static void SkipToEndOfBlock(string s, ref int i)
    {
        i = TryScanBeginEndBlock(s, i, out _, out _, out int afterBlock)
            ? afterBlock
            : s.Length;
    }
}
