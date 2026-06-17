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
}
