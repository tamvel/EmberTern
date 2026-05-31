using System;
using System.Collections.Generic;
using System.Text;

namespace EmberTern.Core.Sql;

// Minimal SQL formatter — pure string processing, no external deps.
//
// Rules:
//   - Lowercase recognised SQL keywords; preserve everything else verbatim
//     (string literals, quoted identifiers, comments, non-keyword identifiers).
//   - Break a new line before each clause keyword: SELECT / FROM / WHERE /
//     HAVING / GROUP BY / ORDER BY and any JOIN form (LEFT/RIGHT/INNER/OUTER/
//     CROSS/FULL + JOIN).
//   - AND / OR start a new line indented two spaces (sub-conjunction inside a
//     WHERE / HAVING / ON clause).
//
// Anything inside string literals ('...'), quoted identifiers ("..."), line
// comments (-- ...) or block comments (/* ... */) is treated as opaque text:
// no lowercase, no line breaks.
public static class SqlFormatter
{
    private const string ConjunctionIndent = "  ";

    // Wrap threshold. Lines longer than this trigger a post-emit wrap pass that
    // packs SELECT column lists / IN (...) value lists onto multiple lines, with
    // continuation lines aligned IBExpert-style:
    //   - SELECT continuation indents to column 7 (under the first column).
    //   - IN continuation indents to one past the opening '('.
    // Both pack multiple items per line up to MaxLineWidth.
    private const int MaxLineWidth = 120;

    private static readonly HashSet<string> TopLevelSingle = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "HAVING",
    };

    // First word of a two-word top-level phrase → required second word.
    private static readonly Dictionary<string, string> TopLevelTwo = new(StringComparer.OrdinalIgnoreCase)
    {
        { "GROUP", "BY" },
        { "ORDER", "BY" },
    };

    // Modifiers that can precede JOIN (one or two of them, e.g. LEFT OUTER JOIN).
    private static readonly HashSet<string> JoinModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL",
    };

    private static readonly HashSet<string> Conjunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR",
    };

    // Keywords lowercased but not line-broken.
    private static readonly HashSet<string> OtherKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ON", "AS", "IN", "IS", "NOT", "NULL", "LIKE", "BETWEEN", "EXISTS",
        "DISTINCT", "ASC", "DESC", "UNION", "ALL", "INTERSECT", "EXCEPT",
        "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE",
        "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX", "PROCEDURE",
        "TRIGGER", "FUNCTION", "GENERATOR", "SEQUENCE", "WITH", "RETURNING",
        "CASE", "WHEN", "THEN", "ELSE", "END", "BEGIN", "DECLARE", "EXECUTE",
        "BLOCK", "TRUE", "FALSE", "PRIMARY", "KEY", "FOREIGN", "REFERENCES",
        "UNIQUE", "CHECK", "DEFAULT", "CONSTRAINT", "FETCH", "FIRST", "ROWS",
        "ONLY", "ROW", "USING", "PLAN",
    };

    private static readonly string[] MultiCharOps =
    {
        "<=", ">=", "<>", "!=", "||", "::",
    };

    public static string Format(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return sql;

        var tokens = Tokenize(sql);
        var emitted = Emit(tokens);
        return WrapLongLines(emitted);
    }

    private static string WrapLongLines(string sql)
    {
        var lines = sql.Split('\n');
        var sb = new StringBuilder(sql.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(WrapLine(lines[i]));
        }
        return sb.ToString();
    }

    private static string WrapLine(string line)
    {
        if (line.Length <= MaxLineWidth) return line;

        // SELECT column list — produced by Emit as "select <cols>" on a single line
        // (FROM, WHERE, etc. are already on subsequent lines via the structural break).
        if (line.StartsWith("select ", StringComparison.Ordinal))
        {
            var wrapped = TryWrapSelectColumns(line);
            if (wrapped is not null) return wrapped;
        }

        // IN (...) value list — match " in (" outside strings/quoted idents.
        var inWrapped = TryWrapInList(line);
        if (inWrapped is not null) return inWrapped;

        return line;
    }

    private static string? TryWrapSelectColumns(string line)
    {
        // Determine the "select " (or "select distinct " / "select all ") header.
        // The continuation indent matches the header length so wrapped columns sit
        // directly under the first column.
        string head;
        if (line.StartsWith("select distinct ", StringComparison.Ordinal))
        {
            head = "select distinct ";
        }
        else if (line.StartsWith("select all ", StringComparison.Ordinal))
        {
            head = "select all ";
        }
        else
        {
            head = "select ";
        }

        var body = line.Substring(head.Length);
        var parts = SplitByTopLevelComma(body);
        if (parts.Count < 2) return null;

        var continuation = new string(' ', head.Length);
        return PackWithContinuation(parts, head, continuation, tail: null);
    }

    private static string? TryWrapInList(string line)
    {
        int parenOpen = FindInOpeningParen(line);
        if (parenOpen < 0) return null;
        int parenClose = FindMatchingClose(line, parenOpen);
        if (parenClose < 0) return null;

        var inner = line.Substring(parenOpen + 1, parenClose - parenOpen - 1);
        // Don't wrap subqueries — only literal/expression value lists.
        if (LooksLikeSubquery(inner)) return null;

        var parts = SplitByTopLevelComma(inner);
        if (parts.Count < 2) return null;

        // Head includes everything up to and including the opening "(", so the first
        // value sits flush against the paren. Continuation lines align to one char
        // past "(". Tail (")" plus anything after) stays inline with the last value.
        var head = line.Substring(0, parenOpen + 1);
        var tail = line.Substring(parenClose);
        var continuation = new string(' ', parenOpen + 1);

        return PackWithContinuation(parts, head, continuation, tail);
    }

    // Emits parts as a comma-separated list, starting with `head` on the first line
    // and wrapping at MaxLineWidth onto continuation lines indented with
    // `continuationIndent`. `tail` (if any) is appended after the last part with no
    // separator — used for the inline ")" of an IN wrap.
    private static string PackWithContinuation(
        List<string> parts,
        string head,
        string continuationIndent,
        string? tail)
    {
        var sb = new StringBuilder();
        sb.Append(head);
        int curLen = head.Length;
        bool atLineStart = false;

        for (int i = 0; i < parts.Count; i++)
        {
            var seg = parts[i].Trim();
            if (i < parts.Count - 1) seg += ",";

            bool isFirst = i == 0;
            // Sticky-on-line-start so the wrap only fires between items.
            int needed = (isFirst || atLineStart) ? seg.Length : 1 + seg.Length;

            if (!isFirst && !atLineStart && curLen + needed > MaxLineWidth)
            {
                sb.Append('\n').Append(continuationIndent);
                curLen = continuationIndent.Length;
                atLineStart = true;
                needed = seg.Length;
            }

            if (!isFirst && !atLineStart) sb.Append(' ');
            sb.Append(seg);
            curLen += needed;
            atLineStart = false;
        }

        if (tail is not null) sb.Append(tail);
        return sb.ToString();
    }

    // Locate the "(" that opens an IN (...) value list. Returns the position of the
    // paren or -1. Skips matches inside strings / quoted identifiers.
    private static int FindInOpeningParen(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\'') { i = SkipString(s, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(s, i); continue; }
            // Match " in (" — keyword boundary on the left is a space, on the right
            // a literal "(". Sufficient because Emit always inserts " in (" with a
            // single space (no tabs in our output).
            if (i + 4 < s.Length
                && s[i] == ' '
                && (s[i + 1] == 'i' || s[i + 1] == 'I')
                && (s[i + 2] == 'n' || s[i + 2] == 'N')
                && s[i + 3] == ' '
                && s[i + 4] == '(')
            {
                return i + 4;
            }
            i++;
        }
        return -1;
    }

    private static int FindMatchingClose(string s, int openIdx)
    {
        int depth = 1;
        int i = openIdx + 1;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\'') { i = SkipString(s, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(s, i); continue; }
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
            i++;
        }
        return -1;
    }

    private static bool LooksLikeSubquery(string inner)
    {
        // After Emit, a subquery starts with "select " (lowercased keyword + space).
        // Skip leading whitespace defensively.
        int i = 0;
        while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
        return inner.Length - i >= 7
            && string.CompareOrdinal(inner, i, "select ", 0, 7) == 0;
    }

    private static List<string> SplitByTopLevelComma(string s)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\'') { i = SkipString(s, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(s, i); continue; }
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { depth--; i++; continue; }
            if (c == ',' && depth == 0)
            {
                result.Add(s.Substring(start, i - start));
                start = i + 1;
            }
            i++;
        }
        result.Add(s.Substring(start));
        return result;
    }

    private static int SkipString(string s, int i)
    {
        // Caller has confirmed s[i] == '\''.
        i++;
        while (i < s.Length)
        {
            if (s[i] == '\'')
            {
                if (i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; }
                return i + 1;
            }
            i++;
        }
        return i;
    }

    private static int SkipQuotedIdent(string s, int i)
    {
        // Caller has confirmed s[i] == '"'.
        i++;
        while (i < s.Length && s[i] != '"') i++;
        return i < s.Length ? i + 1 : i;
    }

    private enum TokenKind
    {
        Word,
        Number,
        String,
        QuotedIdent,
        LineComment,
        BlockComment,
        Punctuation,
        Whitespace,
        Newline,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string s)
    {
        var result = new List<Token>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];

            if (c == '\r' || c == '\n')
            {
                int start = i;
                if (c == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
                i++;
                result.Add(new Token(TokenKind.Newline, s.Substring(start, i - start)));
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                int start = i;
                while (i < s.Length && char.IsWhiteSpace(s[i]) && s[i] != '\r' && s[i] != '\n') i++;
                result.Add(new Token(TokenKind.Whitespace, s.Substring(start, i - start)));
                continue;
            }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                int start = i;
                while (i < s.Length && s[i] != '\r' && s[i] != '\n') i++;
                result.Add(new Token(TokenKind.LineComment, s.Substring(start, i - start)));
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int start = i;
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                if (i + 1 < s.Length) i += 2;
                else i = s.Length;
                result.Add(new Token(TokenKind.BlockComment, s.Substring(start, i - start)));
                continue;
            }
            if (c == '\'')
            {
                int start = i++;
                while (i < s.Length)
                {
                    if (s[i] == '\'')
                    {
                        // Doubled '' is an escaped quote inside the literal.
                        if (i + 1 < s.Length && s[i + 1] == '\'') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                result.Add(new Token(TokenKind.String, s.Substring(start, i - start)));
                continue;
            }
            if (c == '"')
            {
                int start = i++;
                while (i < s.Length && s[i] != '"') i++;
                if (i < s.Length) i++;
                result.Add(new Token(TokenKind.QuotedIdent, s.Substring(start, i - start)));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$')) i++;
                result.Add(new Token(TokenKind.Word, s.Substring(start, i - start)));
                continue;
            }
            if (char.IsDigit(c))
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.')) i++;
                result.Add(new Token(TokenKind.Number, s.Substring(start, i - start)));
                continue;
            }

            // Try multi-char operators before falling back to single-char punctuation.
            var multi = MatchMultiCharOp(s, i);
            if (multi is not null)
            {
                result.Add(new Token(TokenKind.Punctuation, multi));
                i += multi.Length;
                continue;
            }

            result.Add(new Token(TokenKind.Punctuation, s[i].ToString()));
            i++;
        }
        return result;
    }

    private static string? MatchMultiCharOp(string s, int i)
    {
        foreach (var op in MultiCharOps)
        {
            if (i + op.Length <= s.Length
                && string.CompareOrdinal(s, i, op, 0, op.Length) == 0)
            {
                return op;
            }
        }
        return null;
    }

    private static string Emit(List<Token> tokens)
    {
        // Drop the source whitespace — we re-emit our own.
        var meaningful = new List<Token>(tokens.Count);
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.Whitespace || t.Kind == TokenKind.Newline) continue;
            meaningful.Add(t);
        }

        var sb = new StringBuilder();
        Token? prev = null;

        for (int i = 0; i < meaningful.Count; i++)
        {
            var t = meaningful[i];

            var phrase = MatchStructuralPhrase(meaningful, i);
            if (phrase.Length > 0)
            {
                if (sb.Length > 0)
                {
                    TrimTrailingSpaces(sb);
                    if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
                    if (phrase.Kind == PhraseKind.Conjunction) sb.Append(ConjunctionIndent);
                }
                for (int k = 0; k < phrase.Length; k++)
                {
                    if (k > 0) sb.Append(' ');
                    sb.Append(meaningful[i + k].Text.ToLowerInvariant());
                }
                prev = meaningful[i + phrase.Length - 1];
                i += phrase.Length - 1;
                continue;
            }

            if (NeedsSpaceBefore(prev, t, sb))
            {
                sb.Append(' ');
            }
            sb.Append(MaybeLowercase(t));
            prev = t;
        }

        return sb.ToString();
    }

    private enum PhraseKind { None, TopLevel, Conjunction }
    private readonly record struct Phrase(PhraseKind Kind, int Length);

    private static Phrase MatchStructuralPhrase(List<Token> tokens, int i)
    {
        var t = tokens[i];
        if (t.Kind != TokenKind.Word) return new Phrase(PhraseKind.None, 0);

        if (Conjunctions.Contains(t.Text)) return new Phrase(PhraseKind.Conjunction, 1);

        if (TopLevelTwo.TryGetValue(t.Text, out var second))
        {
            if (i + 1 < tokens.Count
                && tokens[i + 1].Kind == TokenKind.Word
                && string.Equals(tokens[i + 1].Text, second, StringComparison.OrdinalIgnoreCase))
            {
                return new Phrase(PhraseKind.TopLevel, 2);
            }
        }

        // JOIN with optional modifiers (LEFT, LEFT OUTER, etc.).
        if (JoinModifiers.Contains(t.Text)
            || string.Equals(t.Text, "JOIN", StringComparison.OrdinalIgnoreCase))
        {
            int k = i;
            while (k < tokens.Count
                && tokens[k].Kind == TokenKind.Word
                && JoinModifiers.Contains(tokens[k].Text))
            {
                k++;
            }
            if (k < tokens.Count
                && tokens[k].Kind == TokenKind.Word
                && string.Equals(tokens[k].Text, "JOIN", StringComparison.OrdinalIgnoreCase))
            {
                return new Phrase(PhraseKind.TopLevel, k - i + 1);
            }
        }

        if (TopLevelSingle.Contains(t.Text)) return new Phrase(PhraseKind.TopLevel, 1);

        return new Phrase(PhraseKind.None, 0);
    }

    private static string MaybeLowercase(Token t)
    {
        // Lowercase every word — keywords AND identifiers (table/column/alias/
        // function names). Strings ('...'), quoted identifiers ("..."), and
        // comments are NOT Word tokens, so they pass through untouched.
        // This matches IBExpert's "lowercase all" formatting preset, which the
        // user prefers for daily ERP work.
        return t.Kind == TokenKind.Word ? t.Text.ToLowerInvariant() : t.Text;
    }

    private static bool IsKeyword(string word)
        => TopLevelSingle.Contains(word)
        || TopLevelTwo.ContainsKey(word)
        || Conjunctions.Contains(word)
        || JoinModifiers.Contains(word)
        || OtherKeywords.Contains(word)
        || string.Equals(word, "JOIN", StringComparison.OrdinalIgnoreCase)
        || string.Equals(word, "BY", StringComparison.OrdinalIgnoreCase);

    private static bool NeedsSpaceBefore(Token? prev, Token t, StringBuilder sb)
    {
        if (sb.Length == 0) return false;
        char last = sb[sb.Length - 1];
        if (last == '\n' || last == ' ') return false;
        if (last == '(' || last == '.' || last == ':') return false;

        if (t.Kind == TokenKind.Punctuation)
        {
            var p = t.Text;
            if (p == "," || p == ";" || p == ")" || p == "." || p == "::") return false;
            // Function call: no space between identifier and the opening paren.
            if (p == "("
                && prev is { } pv
                && (pv.Kind == TokenKind.Word || pv.Kind == TokenKind.QuotedIdent)
                && !(pv.Kind == TokenKind.Word && IsKeyword(pv.Text)))
            {
                return false;
            }
        }

        return true;
    }

    private static void TrimTrailingSpaces(StringBuilder sb)
    {
        while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
    }
}
