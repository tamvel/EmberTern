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

    // Indent for a CREATE [OR ALTER] VIEW column list — IBExpert puts each output
    // column on its own line indented four spaces under the view name.
    private const string ViewColumnIndent = "    ";

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
        // PSQL control/keywords — lowercased + (critically) NOT glued to a following
        // "(" like a function call, so "if (…)", "while (…)" keep their space.
        "IF", "WHILE", "DO", "FOR", "SUSPEND", "EXIT", "LEAVE", "STATEMENT",
        "VARIABLE", "CURSOR", "OPEN", "CLOSE", "INTO", "RETURN", "RETURNS",
    };

    private static readonly string[] MultiCharOps =
    {
        "<=", ">=", "<>", "!=", "||", "::",
    };

    public static string Format(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return sql;

        var tokens = Tokenize(sql);
        if (IsPsql(tokens))
        {
            return FormatPsql(sql, tokens);
        }
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

            // CREATE [OR ALTER] VIEW <name> [(col, col, …)] AS — format the header
            // IBExpert-style: name + space + "(", each column on its own indented
            // line, ")" glued to the last column, then "as" on its own line.
            var viewConsumed = TryEmitViewHeader(meaningful, i, sb, ref prev);
            if (viewConsumed > 0)
            {
                i += viewConsumed - 1;
                continue;
            }

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

            // A line comment runs to end-of-line — force a newline after it so the
            // tokens that followed it on the source line don't get commented out
            // (semantics-preserving; also keeps the comment's position). Block
            // comments are self-delimiting and stay inline.
            if (t.Kind == TokenKind.LineComment)
            {
                sb.Append('\n');
                prev = null;
            }
        }

        return sb.ToString();
    }

    // Emits a CREATE [OR ALTER] VIEW header when token `i` is the VIEW keyword
    // followed by an identifier. Returns the number of tokens consumed (name +
    // optional column list + optional AS), or 0 when token `i` is not a view
    // header. Pure structural formatting — strings / quoted identifiers / comments
    // are still passed through MaybeLowercase (which only touches Word tokens).
    private static int TryEmitViewHeader(List<Token> tokens, int i, StringBuilder sb, ref Token? prev)
    {
        var t = tokens[i];
        if (t.Kind != TokenKind.Word || !string.Equals(t.Text, "VIEW", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (i + 1 >= tokens.Count) return 0;
        var nameTok = tokens[i + 1];
        if (nameTok.Kind != TokenKind.Word && nameTok.Kind != TokenKind.QuotedIdent) return 0;

        if (NeedsSpaceBefore(prev, t, sb)) sb.Append(' ');
        sb.Append("view ");
        sb.Append(MaybeLowercase(nameTok));
        int j = i + 2;
        prev = nameTok;

        // Optional column list: "(" col ["," col]* ")".
        if (j < tokens.Count && tokens[j] is { Kind: TokenKind.Punctuation, Text: "(" })
        {
            sb.Append(" (");
            j++;
            int depth = 1;
            bool needIndent = true;
            Token? colPrev = null;
            while (j < tokens.Count && depth > 0)
            {
                var ct = tokens[j];
                if (ct.Kind == TokenKind.Punctuation && ct.Text == "(")
                {
                    if (needIndent) { sb.Append('\n').Append(ViewColumnIndent); needIndent = false; }
                    depth++;
                    sb.Append('(');
                }
                else if (ct.Kind == TokenKind.Punctuation && ct.Text == ")")
                {
                    depth--;
                    sb.Append(')');
                    colPrev = ct;
                    j++;
                    if (depth == 0) break;
                    continue;
                }
                else if (depth == 1 && ct.Kind == TokenKind.Punctuation && ct.Text == ",")
                {
                    TrimTrailingSpaces(sb);
                    sb.Append(',');
                    needIndent = true;
                }
                else
                {
                    if (needIndent) { sb.Append('\n').Append(ViewColumnIndent); needIndent = false; }
                    else if (NeedsSpaceBefore(colPrev, ct, sb)) sb.Append(' ');
                    sb.Append(MaybeLowercase(ct));
                }
                colPrev = ct;
                j++;
            }
            prev = j > 0 ? tokens[j - 1] : prev;
        }

        // Optional AS on its own line (the view-body separator). Column-alias AS
        // (e.g. "x.id as foo") is untouched — it never follows the view header.
        if (j < tokens.Count
            && tokens[j].Kind == TokenKind.Word
            && string.Equals(tokens[j].Text, "AS", StringComparison.OrdinalIgnoreCase))
        {
            TrimTrailingSpaces(sb);
            sb.Append('\n').Append("as");
            prev = tokens[j];
            j++;
        }

        return j - i;
    }

    private enum PhraseKind { None, TopLevel, Conjunction }
    private readonly record struct Phrase(PhraseKind Kind, int Length);

    private static Phrase MatchStructuralPhrase(List<Token> tokens, int i)
    {
        var t = tokens[i];
        if (t.Kind != TokenKind.Word) return new Phrase(PhraseKind.None, 0);

        if (Conjunctions.Contains(t.Text))
        {
            // "OR ALTER" (as in CREATE OR ALTER VIEW/PROCEDURE/TRIGGER) is a DDL
            // phrase, not a boolean OR — it must NOT break onto its own indented
            // line. Without this guard the OR-conjunction rule turned
            // "create or alter view …" into "create\n  or alter view …".
            if (string.Equals(t.Text, "OR", StringComparison.OrdinalIgnoreCase)
                && i + 1 < tokens.Count
                && tokens[i + 1].Kind == TokenKind.Word
                && string.Equals(tokens[i + 1].Text, "ALTER", StringComparison.OrdinalIgnoreCase))
            {
                return new Phrase(PhraseKind.None, 0);
            }
            return new Phrase(PhraseKind.Conjunction, 1);
        }

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

    // ─── PSQL (procedures / triggers / functions / EXECUTE BLOCK) ──────────
    //
    // One formatter, two modes. Plain SQL/DML → Emit (clause breaks). PSQL → a
    // recursive, block-structured layout that REUSES Emit for every leaf statement
    // (so spacing / lowercasing / :var gluing / SELECT clause breaks are identical
    // to the SQL editor) and adds only BEGIN/END indentation + control-flow layout.
    //
    // CASE … END safety: a statement is collected up to its top-level ';', so a
    // CASE…END (which has no ';') is consumed WHOLE inside the statement and handed
    // to Emit as inline text — the BEGIN/END block loop never sees a CASE's END.
    //
    // Idempotency: indentation comes purely from BEGIN/END/IF/WHILE/FOR structure
    // and statement breaks purely from ';' — never from existing whitespace — so
    // re-formatting the output reproduces it.

    private const int PsqlIndentSize = 2;

    private static bool IsPsql(List<Token> tokens)
    {
        // A top-level BEGIN is a definitive PSQL signal (BEGIN never appears in DML).
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.Word && t.Text.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        // Header-only object with no visible BEGIN (rare) — check the leading words.
        int j = 0;
        var w0 = NextSignificantWordUpper(tokens, ref j);
        if (w0 is "CREATE" or "RECREATE" or "ALTER")
        {
            var w1 = NextSignificantWordUpper(tokens, ref j);
            if (w1 == "OR") w1 = NextSignificantWordUpper(tokens, ref j);
            if (w1 == "ALTER") w1 = NextSignificantWordUpper(tokens, ref j);
            return w1 is "PROCEDURE" or "TRIGGER" or "FUNCTION" or "PACKAGE";
        }
        if (w0 == "EXECUTE")
        {
            return NextSignificantWordUpper(tokens, ref j) == "BLOCK";
        }
        return false;
    }

    private static string? NextSignificantWordUpper(List<Token> tokens, ref int j)
    {
        while (j < tokens.Count)
        {
            var t = tokens[j++];
            if (t.Kind == TokenKind.Word) return t.Text.ToUpperInvariant();
            if (t.Kind is TokenKind.Whitespace or TokenKind.Newline
                or TokenKind.LineComment or TokenKind.BlockComment) continue;
            return null; // a non-word significant token before any word
        }
        return null;
    }

    private static string FormatPsql(string sql, List<Token> tokens)
    {
        // Keep a CREATE … AS header verbatim (it's already well-formed when generated
        // by DdlGenerator); structure only the body after the top-level AS.
        int bodyStart = FindBodyStart(sql);
        string header = bodyStart > 0 ? sql.Substring(0, bodyStart).TrimEnd() : string.Empty;
        var bodyTokens = bodyStart > 0 ? Tokenize(sql.Substring(bodyStart)) : tokens;

        // Significant tokens (comments kept) + a parallel flag marking where the
        // author left a blank line (≥2 newlines) before the token. Those logical
        // separators are re-emitted (collapsed to a single blank line) so the
        // formatter normalizes structure without flattening readability.
        var sig = new List<Token>(bodyTokens.Count);
        var blank = new List<bool>(bodyTokens.Count);
        int newlines = 0;
        foreach (var t in bodyTokens)
        {
            if (t.Kind == TokenKind.Newline) { newlines++; continue; }
            if (t.Kind == TokenKind.Whitespace) continue;
            sig.Add(t);
            blank.Add(newlines >= 2);
            newlines = 0;
        }

        var lines = new List<string>();
        int i = 0;
        while (i < sig.Count)
        {
            int before = i;
            EmitPsqlUnit(sig, blank, ref i, 0, lines);
            if (i == before) i++; // never stall on an unexpected token
        }

        var body = string.Join("\n", lines);
        var result = header.Length > 0 ? header + "\n" + body : body;
        return WrapLongLines(result);
    }

    // Returns the index in <paramref name="sql"/> just past the top-level AS that
    // separates a CREATE … header from its PSQL body, or 0 when there's no header.
    private static int FindBodyStart(string sql)
    {
        int i = 0;
        var first = ReadWordRaw(sql, ref i);
        if (!(first.Equals("CREATE", StringComparison.OrdinalIgnoreCase)
              || first.Equals("RECREATE", StringComparison.OrdinalIgnoreCase)
              || first.Equals("ALTER", StringComparison.OrdinalIgnoreCase)))
            return 0;

        int depth = 0;
        int k = 0;
        while (k < sql.Length)
        {
            char c = sql[k];
            if (c == '\'') { k = SkipString(sql, k); continue; }
            if (c == '"') { k = SkipQuotedIdent(sql, k); continue; }
            if (c == '-' && k + 1 < sql.Length && sql[k + 1] == '-')
            { while (k < sql.Length && sql[k] != '\n') k++; continue; }
            if (c == '/' && k + 1 < sql.Length && sql[k + 1] == '*')
            { k += 2; while (k + 1 < sql.Length && !(sql[k] == '*' && sql[k + 1] == '/')) k++; k = k + 1 < sql.Length ? k + 2 : sql.Length; continue; }
            if (c == '(') { depth++; k++; continue; }
            if (c == ')') { if (depth > 0) depth--; k++; continue; }
            if (char.IsLetter(c) || c == '_')
            {
                int s = k;
                while (k < sql.Length && (char.IsLetterOrDigit(sql[k]) || sql[k] == '_' || sql[k] == '$')) k++;
                if (depth == 0 && k - s == 2
                    && string.Compare(sql, s, "AS", 0, 2, StringComparison.OrdinalIgnoreCase) == 0)
                    return k;
                continue;
            }
            k++;
        }
        return 0;
    }

    private static string ReadWordRaw(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$')) i++;
        return s.Substring(start, i - start);
    }

    // Emits one PSQL "unit" (a leaf statement, or a compound: BEGIN block / IF /
    // WHILE / FOR / local subprogram) at <paramref name="indent"/>; advances i.
    private static void EmitPsqlUnit(List<Token> sig, List<bool> blank, ref int i, int indent, List<string> lines)
    {
        while (i < sig.Count && IsCommentTok(sig[i]))
        {
            MaybeBlankLine(lines, blank[i]);
            AddPsqlLine(lines, indent, sig[i].Text);
            i++;
        }
        if (i >= sig.Count) return;
        if (IsWordTok(sig[i], "END")) return; // belongs to the enclosing BEGIN loop
        MaybeBlankLine(lines, blank[i]);

        var t = sig[i];
        if (t.Kind == TokenKind.Word)
        {
            var up = t.Text.ToUpperInvariant();
            if (up == "BEGIN")
            {
                AddPsqlLine(lines, indent, "begin");
                i++;
                while (i < sig.Count && !IsWordTok(sig[i], "END"))
                {
                    int before = i;
                    EmitPsqlUnit(sig, blank, ref i, indent + 1, lines);
                    if (i == before) i++;
                }
                if (i < sig.Count && IsWordTok(sig[i], "END"))
                {
                    MaybeBlankLine(lines, blank[i]);
                    i++;
                    var end = "end";
                    if (i < sig.Count && IsPunctTok(sig[i], ";")) { end = "end;"; i++; }
                    AddPsqlLine(lines, indent, end);
                }
                return;
            }
            if (up == "IF")
            {
                AddPsqlEmit(lines, indent, CollectUntilWord(sig, ref i, "THEN"));
                EmitPsqlBranch(sig, blank, ref i, indent, lines);
                while (i < sig.Count && IsCommentTok(sig[i])) { MaybeBlankLine(lines, blank[i]); AddPsqlLine(lines, indent, sig[i].Text); i++; }
                if (i < sig.Count && IsWordTok(sig[i], "ELSE"))
                {
                    i++;
                    AddPsqlLine(lines, indent, "else");
                    EmitPsqlBranch(sig, blank, ref i, indent, lines);
                }
                return;
            }
            if (up == "WHILE" || up == "FOR")
            {
                AddPsqlEmit(lines, indent, CollectUntilWord(sig, ref i, "DO"));
                EmitPsqlBranch(sig, blank, ref i, indent, lines);
                return;
            }
            if (up == "DECLARE"
                && i + 1 < sig.Count && sig[i + 1].Kind == TokenKind.Word
                && (sig[i + 1].Text.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase)
                    || sig[i + 1].Text.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase)))
            {
                AddPsqlEmit(lines, indent, CollectUntilWordExclusive(sig, ref i, "BEGIN"));
                int before = i;
                EmitPsqlUnit(sig, blank, ref i, indent, lines); // the subprogram's BEGIN…END
                if (i == before) i++;
                return;
            }
        }

        AddPsqlEmit(lines, indent, CollectPsqlStatement(sig, ref i));
    }

    private static void EmitPsqlBranch(List<Token> sig, List<bool> blank, ref int i, int indent, List<string> lines)
    {
        while (i < sig.Count && IsCommentTok(sig[i])) { AddPsqlLine(lines, indent + 1, sig[i].Text); i++; }
        if (i < sig.Count && IsWordTok(sig[i], "BEGIN"))
        {
            EmitPsqlUnit(sig, blank, ref i, indent, lines); // block aligned under the header
        }
        else
        {
            int before = i;
            EmitPsqlUnit(sig, blank, ref i, indent + 1, lines); // single statement indented
            if (i == before) i++;
        }
    }

    // Re-emit an author blank line (collapsed to one) before a unit — but never as
    // the first output line and never two in a row.
    private static void MaybeBlankLine(List<string> lines, bool hadBlank)
    {
        if (hadBlank && lines.Count > 0 && lines[lines.Count - 1].Length != 0)
            lines.Add(string.Empty);
    }

    // Collects tokens up to and INCLUDING the next Word == <paramref name="word"/>.
    private static List<Token> CollectUntilWord(List<Token> sig, ref int i, string word)
    {
        var list = new List<Token>();
        while (i < sig.Count)
        {
            var t = sig[i];
            list.Add(t); i++;
            if (t.Kind == TokenKind.Word && t.Text.Equals(word, StringComparison.OrdinalIgnoreCase)) break;
        }
        return list;
    }

    // Collects tokens up to (NOT including) the next Word == <paramref name="word"/>.
    private static List<Token> CollectUntilWordExclusive(List<Token> sig, ref int i, string word)
    {
        var list = new List<Token>();
        while (i < sig.Count)
        {
            var t = sig[i];
            if (t.Kind == TokenKind.Word && t.Text.Equals(word, StringComparison.OrdinalIgnoreCase)) break;
            list.Add(t); i++;
        }
        return list;
    }

    // Collects one statement up to and INCLUDING its terminating top-level ';'. A
    // CASE…END has no ';' so it is collected whole (the END is NOT a block END).
    private static List<Token> CollectPsqlStatement(List<Token> sig, ref int i)
    {
        var list = new List<Token>();
        while (i < sig.Count)
        {
            var t = sig[i];
            list.Add(t); i++;
            if (t.Kind == TokenKind.Punctuation && t.Text == ";") break;
        }
        return list;
    }

    private static void AddPsqlEmit(List<string> lines, int indent, List<Token> stmt)
    {
        if (stmt.Count == 0) return;

        // SELECT … INTO :vars (PSQL singleton select) — put the INTO clause on its
        // own line, IBExpert-style. Only when the statement starts with SELECT, so
        // INSERT INTO is unaffected; the INTO must be top-level (not in a subquery).
        if (IsWordTok(stmt[0], "SELECT"))
        {
            int into = FindTopLevelWord(stmt, "INTO");
            if (into > 0)
            {
                EmitPsqlLines(lines, indent, Emit(stmt.GetRange(0, into)));
                EmitPsqlLines(lines, indent, Emit(stmt.GetRange(into, stmt.Count - into)));
                return;
            }
        }

        EmitPsqlLines(lines, indent, Emit(stmt));
    }

    private static void EmitPsqlLines(List<string> lines, int indent, string emitted)
    {
        var prefix = new string(' ', indent * PsqlIndentSize);
        foreach (var ln in emitted.TrimEnd('\n').Split('\n'))
        {
            lines.Add(ln.Length == 0 ? string.Empty : prefix + ln);
        }
    }

    private static int FindTopLevelWord(List<Token> tokens, string word)
    {
        int depth = 0;
        for (int k = 0; k < tokens.Count; k++)
        {
            var t = tokens[k];
            if (t.Kind == TokenKind.Punctuation && t.Text == "(") depth++;
            else if (t.Kind == TokenKind.Punctuation && t.Text == ")") { if (depth > 0) depth--; }
            else if (depth == 0 && t.Kind == TokenKind.Word && t.Text.Equals(word, StringComparison.OrdinalIgnoreCase))
                return k;
        }
        return -1;
    }

    private static void AddPsqlLine(List<string> lines, int indent, string text)
        => lines.Add(new string(' ', indent * PsqlIndentSize) + text);

    private static bool IsWordTok(Token t, string w)
        => t.Kind == TokenKind.Word && t.Text.Equals(w, StringComparison.OrdinalIgnoreCase);
    private static bool IsPunctTok(Token t, string p) => t.Kind == TokenKind.Punctuation && t.Text == p;
    private static bool IsCommentTok(Token t) => t.Kind is TokenKind.LineComment or TokenKind.BlockComment;
}
