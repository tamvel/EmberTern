using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql;

/// <summary>
/// The AST-based SQL/PSQL formatter — Etap 3 of the editor rebuild. It fully replaced the old
/// flat-token, heuristic formatter (the previous implementation and its own tokenizer/keyword
/// hashsets are gone; there is only one formatter and it is this class).
/// <para>
/// <b>Architecture (approved Variant A).</b> The <em>statement-level</em> decisions are driven
/// entirely by the parse tree, not by re-scanning heuristics: <see cref="SqlParser"/> tells us the
/// statement boundaries and the kind of each statement, so there is no more <c>IsPsql</c> /
/// <c>FindBodyStart</c> guessing and no separate keyword-classification tokenizer (the old O1
/// <c>SqlFormatter.Tokenize</c> is gone — this formatter rides the single <see cref="SqlLexer"/>).
/// Within a statement — which at the current "statement skeleton" AST depth is a flat token list —
/// the interior is laid out by a structure-aware token emitter reusing the proven, test-pinned
/// layout rules (clause breaks, JOIN/ON, AND/OR, view header, column/IN-list wrapping, and the
/// CASE-safe BEGIN/END PSQL block structuring). Deeper clause / expression / PSQL-body AST nodes
/// are a later etap; the formatter does not need them to format the skeleton correctly.
/// </para>
/// <para>
/// <b>§0 (Paramount Law) — never lose information.</b> A statement the parser could not classify is
/// a <see cref="RawStatement"/> and is emitted <b>verbatim</b> (byte-for-byte from its source span),
/// never reformatted or "cleaned up". Comments (line and block) are preserved from the lexer's
/// trivia and never dropped, moved unexpectedly, or altered. String literals, quoted identifiers and
/// numbers pass through untouched. Only whitespace and letter-case of unquoted words change.
/// </para>
/// <para>
/// <b>Deterministic + idempotent</b> (design principle #7): the same input always yields the same
/// output, and <c>Format(Format(x)) == Format(x)</c>. Indentation comes purely from the AST /
/// BEGIN-END structure and line breaks purely from clause keywords + <c>;</c> — never from the input
/// whitespace — so re-formatting reproduces the output exactly.
/// </para>
/// <para>
/// <b>Default style</b> (design §6): one opinionated IBExpert-inspired layout, lowercase-all
/// (matching the "lowercase all" preset ERP users prefer). A configurable style profile is deferred
/// to the future application configurator; the single default lives in the constants below.
/// </para>
/// <para>Pure — no Avalonia, no Firebird driver — and offline unit-testable.</para>
/// </summary>
public static class SqlFormatter
{
    // ── Default style (§6 — single opinionated default; config panel deferred) ────────────────
    private const string ConjunctionIndent = "  ";   // AND/OR sub-conjunction indent
    private const string ViewColumnIndent = "    ";   // CREATE VIEW column list indent
    private const int MaxLineWidth = 120;             // long-line wrap threshold (SELECT cols / IN list)
    private const int PsqlIndentSize = 2;             // spaces per PSQL nesting level

    /// <summary>Formats a SQL/PSQL script with the default style.</summary>
    public static string Format(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return sql ?? string.Empty;
        return Format(SqlParser.Parse(sql!).Root);
    }

    /// <summary>Formats an already-parsed script. Never throws; never loses information (§0).</summary>
    public static string Format(SqlScript root)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));

        var parts = new List<string>();
        foreach (var stmt in root.Statements)
        {
            var text = FormatStatement(root.Text, stmt);
            // §0 Formatter Safety (per statement): if formatting altered or lost ANY lexeme, keep
            // the statement verbatim instead — "leave the fragment unchanged" — so one malformed
            // statement never corrupts, and never blocks formatting of the rest of the script.
            if (!LexemesPreserved(stmt.Tokens, text))
            {
                text = VerbatimStatement(root.Text, stmt);
            }
            if (text.Length > 0) parts.Add(text);
        }

        AppendTrailingComments(root, parts); // trailing comments (on EOF) — never lose (§0)

        var joined = string.Join("\n", parts);
        var result = WrapLongLines(joined);

        // §0 Formatter Safety (absolute backstop): if the whole formatted result still differs from
        // the input by even one lexeme, refuse — return the input unchanged rather than emit anything
        // lossy. Covers the string-level long-line wrapping stage and any path not caught above.
        return LexemesPreserved(root.Text, result) ? result : root.Text;
    }

    // ── Statement dispatch (100% AST-driven) ──────────────────────────────────────────────────

    private static string FormatStatement(string source, SqlStatement stmt) => stmt switch
    {
        // §0 safety valve: unrecognised or empty statements are reproduced verbatim.
        RawStatement or EmptyStatement => VerbatimStatement(source, stmt),

        // PSQL definitions (CREATE/ALTER/RECREATE PROCEDURE/TRIGGER/FUNCTION/PACKAGE) and EXECUTE
        // BLOCK: the header up to the body's AS is kept verbatim (already well-formed), the body is
        // block-structured.
        DdlStatement { IsPsqlDefinition: true } => FormatWithHeaderAndBody(source, stmt),
        ExecuteBlockStatement => FormatWithHeaderAndBody(source, stmt),

        // A bare/DECLARE-led anonymous block (the body editor's text) — no header, whole body.
        AnonymousBlockStatement => FormatPsqlBody(Flatten(stmt.Tokens), header: null),

        // INSERT and UPDATE OR INSERT — "<verb> into <target> (cols)" then "values (…)" / "select …"
        // and (for UPDATE OR INSERT) "matching (…)" each on its own line, the lists laid out by the
        // shared adaptive builder (§F). One formatter for both (they differ only by the leading verb +
        // MATCHING); unrecognised shapes fall back to the generic emitter (safe; §0 net covers it).
        InsertStatement => FormatInsertFamily(Flatten(stmt.Tokens), headerLen: 2),        // insert into
        UpdateOrInsertStatement => FormatInsertFamily(Flatten(stmt.Tokens), headerLen: 4), // update or insert into

        // Everything else — all DML plus non-PSQL DDL, COMMENT, SET, GRANT/REVOKE, DECLARE,
        // EXECUTE PROCEDURE/STATEMENT — through the clause-break SQL emitter (which also handles the
        // CREATE VIEW header case internally, exactly as before).
        _ => Emit(Flatten(stmt.Tokens)),
    };

    // Verbatim source span, prefixed by any leading comments (which live in the first token's trivia
    // and are therefore outside the span). Never reformatted (§0).
    private static string VerbatimStatement(string source, SqlStatement stmt)
    {
        var verbatim = source.Substring(stmt.Start, stmt.Length);
        var comments = LeadingComments(stmt);
        return comments.Length == 0 ? verbatim : comments + "\n" + verbatim;
    }

    // Renders the leading comments of a statement's first token, each on its own line, or "".
    private static string LeadingComments(SqlStatement stmt)
    {
        if (stmt.Tokens.Count == 0) return string.Empty;
        StringBuilder? sb = null;
        foreach (var tr in stmt.Tokens[0].LeadingTrivia)
        {
            if (tr.Kind is TriviaKind.LineComment or TriviaKind.BlockComment)
            {
                sb ??= new StringBuilder();
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(tr.Kind == TriviaKind.LineComment ? tr.Text.TrimEnd() : tr.Text);
            }
        }
        return sb?.ToString() ?? string.Empty;
    }

    private static void AppendTrailingComments(SqlScript root, List<string> parts)
    {
        if (root.Tokens.Count == 0) return;
        var eof = root.Tokens[root.Tokens.Count - 1];
        if (eof.Kind != TokenKind.EndOfFile) return;
        foreach (var tr in eof.LeadingTrivia)
        {
            if (tr.Kind == TriviaKind.LineComment) parts.Add(tr.Text.TrimEnd());
            else if (tr.Kind == TriviaKind.BlockComment) parts.Add(tr.Text);
        }
    }

    // PSQL definition / EXECUTE BLOCK: keep the header (through the body-opening AS) verbatim, then
    // block-structure the body. The AS is found over tokens at paren depth 0 (so CAST(x AS y) and a
    // param-list AS are skipped). No top-level AS ⇒ format the whole thing as a body.
    private static string FormatWithHeaderAndBody(string source, SqlStatement stmt)
    {
        int asIndex = FindTopLevelAs(stmt.Tokens);
        if (asIndex < 0)
        {
            return FormatPsqlBody(Flatten(stmt.Tokens), header: null);
        }

        var asTok = stmt.Tokens[asIndex];
        string header = source.Substring(stmt.Start, asTok.End - stmt.Start).TrimEnd();
        // A comment BEFORE the definition keyword lives in the first token's leading trivia — outside
        // the header span (stmt.Start begins after trivia) — so re-attach it or it is lost (§0). The
        // no-AS branch above goes through Flatten, which already materialises leading comments.
        var lead = LeadingComments(stmt);
        if (lead.Length > 0) header = lead + "\n" + header;

        var bodyTokens = new List<SqlToken>(stmt.Tokens.Count - asIndex - 1);
        for (int k = asIndex + 1; k < stmt.Tokens.Count; k++) bodyTokens.Add(stmt.Tokens[k]);

        return FormatPsqlBody(Flatten(bodyTokens), header);
    }

    private static int FindTopLevelAs(IReadOnlyList<SqlToken> tokens)
    {
        int depth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.LParen) depth++;
            else if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && t.Kind == TokenKind.Keyword
                     && string.Equals(t.Text, "AS", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    // ── Format token stream (lexer tokens → the emitter's flat model, comments materialised) ────
    //
    // The interior emitters below operate on a simple flat stream (word / number / string /
    // quoted-ident / comment / punctuation) matching the shape the proven layout logic expects. This
    // is produced from the single SqlLexer's tokens: comments come from each token's leading trivia
    // (so they are never lost — §0), whitespace is dropped (the formatter re-emits its own), and a
    // per-token BlankBefore flag records an author blank line (≥2 newlines) for PSQL layout.

    private enum FKind { Word, Number, String, QuotedIdent, LineComment, BlockComment, Punctuation }

    private readonly record struct FToken(FKind Kind, string Text, bool BlankBefore)
    {
        public bool IsComment => Kind is FKind.LineComment or FKind.BlockComment;
    }

    private static List<FToken> Flatten(IReadOnlyList<SqlToken> tokens)
    {
        var list = new List<FToken>(tokens.Count);
        foreach (var t in tokens)
        {
            int newlineRun = 0;
            foreach (var tr in t.LeadingTrivia)
            {
                switch (tr.Kind)
                {
                    case TriviaKind.Whitespace:
                        newlineRun += CountNewlines(tr.Text);
                        break;
                    case TriviaKind.LineComment:
                        list.Add(new FToken(FKind.LineComment, tr.Text.TrimEnd(), newlineRun >= 2));
                        newlineRun = 0;
                        break;
                    case TriviaKind.BlockComment:
                        list.Add(new FToken(FKind.BlockComment, tr.Text, newlineRun >= 2));
                        newlineRun = 0;
                        break;
                }
            }
            list.Add(MapToken(t, newlineRun >= 2));
        }
        return list;
    }

    private static FToken MapToken(SqlToken t, bool blankBefore) => t.Kind switch
    {
        TokenKind.Keyword or TokenKind.Identifier => new FToken(FKind.Word, t.Text, blankBefore),
        TokenKind.QuotedIdentifier => new FToken(FKind.QuotedIdent, t.Text, blankBefore),
        TokenKind.StringLiteral => new FToken(FKind.String, t.Text, blankBefore),
        TokenKind.Number => new FToken(FKind.Number, t.Text, blankBefore),
        // Named parameters (:name / @name) behave like an identifier for spacing + lowercasing;
        // a positional '?' is punctuation.
        TokenKind.Parameter => t.Text == "?"
            ? new FToken(FKind.Punctuation, "?", blankBefore)
            : new FToken(FKind.Word, t.Text, blankBefore),
        _ => new FToken(FKind.Punctuation, t.Text, blankBefore), // Comma/Dot/Semicolon/(/)/Operator/Unknown
    };

    private static int CountNewlines(string s)
    {
        int n = 0;
        foreach (var c in s) if (c == '\n') n++;
        return n;
    }

    // ── Keyword / structural policy sets (formatting style, not the lexical catalog) ────────────
    //
    // These decide the LAYOUT (which keyword breaks a line, keeps a space before "("). They are the
    // formatter's own style policy — distinct from FirebirdSyntax (which classifies keyword vs
    // identifier for the lexer). Kept identical to the proven formatter so output is unchanged.

    private static readonly HashSet<string> TopLevelSingle = new(StringComparer.OrdinalIgnoreCase)
    { "SELECT", "FROM", "WHERE", "HAVING" };

    private static readonly Dictionary<string, string> TopLevelTwo = new(StringComparer.OrdinalIgnoreCase)
    { { "GROUP", "BY" }, { "ORDER", "BY" } };

    private static readonly HashSet<string> JoinModifiers = new(StringComparer.OrdinalIgnoreCase)
    { "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL" };

    private static readonly HashSet<string> Conjunctions = new(StringComparer.OrdinalIgnoreCase)
    { "AND", "OR" };

    // Lowercased but not line-broken; keep a space before a following "(" (so "in (", "values (",
    // "if (" stay spaced while function/type/identifier calls glue).
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
        "IF", "WHILE", "DO", "FOR", "SUSPEND", "EXIT", "LEAVE", "STATEMENT",
        "VARIABLE", "CURSOR", "OPEN", "CLOSE", "INTO", "RETURN", "RETURNS",
    };

    private static bool IsStyleKeyword(string word)
        => TopLevelSingle.Contains(word)
        || TopLevelTwo.ContainsKey(word)
        || Conjunctions.Contains(word)
        || JoinModifiers.Contains(word)
        || OtherKeywords.Contains(word)
        || string.Equals(word, "JOIN", StringComparison.OrdinalIgnoreCase)
        || string.Equals(word, "BY", StringComparison.OrdinalIgnoreCase);

    // ── DML / generic SQL emitter (clause breaks + view header) ────────────────────────────────

    private static string Emit(List<FToken> meaningful)
    {
        var sb = new StringBuilder();
        FToken? prev = null;

        for (int i = 0; i < meaningful.Count; i++)
        {
            var t = meaningful[i];

            var viewConsumed = TryEmitViewHeader(meaningful, i, sb, ref prev);
            if (viewConsumed > 0) { i += viewConsumed - 1; continue; }

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

            if (NeedsSpaceBefore(prev, t, sb)) sb.Append(' ');
            sb.Append(MaybeLowercase(t));
            prev = t;

            // A line comment runs to end-of-line — force a newline so following tokens are not
            // commented out (semantics-preserving; keeps the comment's position).
            if (t.Kind == FKind.LineComment)
            {
                sb.Append('\n');
                prev = null;
            }
        }

        return sb.ToString();
    }

    private enum PhraseKind { None, TopLevel, Conjunction }
    private readonly record struct Phrase(PhraseKind Kind, int Length);

    private static Phrase MatchStructuralPhrase(List<FToken> tokens, int i)
    {
        var t = tokens[i];
        if (t.Kind != FKind.Word) return new Phrase(PhraseKind.None, 0);

        if (Conjunctions.Contains(t.Text))
        {
            // "OR ALTER" (CREATE OR ALTER …) is a DDL phrase, not a boolean OR — must not break.
            if (string.Equals(t.Text, "OR", StringComparison.OrdinalIgnoreCase)
                && i + 1 < tokens.Count
                && tokens[i + 1].Kind == FKind.Word
                && string.Equals(tokens[i + 1].Text, "ALTER", StringComparison.OrdinalIgnoreCase))
            {
                return new Phrase(PhraseKind.None, 0);
            }
            return new Phrase(PhraseKind.Conjunction, 1);
        }

        if (TopLevelTwo.TryGetValue(t.Text, out var second)
            && i + 1 < tokens.Count
            && tokens[i + 1].Kind == FKind.Word
            && string.Equals(tokens[i + 1].Text, second, StringComparison.OrdinalIgnoreCase))
        {
            return new Phrase(PhraseKind.TopLevel, 2);
        }

        // JOIN with optional modifiers (LEFT, LEFT OUTER, …).
        if (JoinModifiers.Contains(t.Text)
            || string.Equals(t.Text, "JOIN", StringComparison.OrdinalIgnoreCase))
        {
            int k = i;
            while (k < tokens.Count && tokens[k].Kind == FKind.Word && JoinModifiers.Contains(tokens[k].Text)) k++;
            if (k < tokens.Count && tokens[k].Kind == FKind.Word
                && string.Equals(tokens[k].Text, "JOIN", StringComparison.OrdinalIgnoreCase))
            {
                return new Phrase(PhraseKind.TopLevel, k - i + 1);
            }
        }

        if (TopLevelSingle.Contains(t.Text)) return new Phrase(PhraseKind.TopLevel, 1);

        return new Phrase(PhraseKind.None, 0);
    }

    // Emits a CREATE [OR ALTER] VIEW header (name + column list one-per-line + AS on its own line).
    // Returns the number of tokens consumed, or 0 when token i is not a view header.
    private static int TryEmitViewHeader(List<FToken> tokens, int i, StringBuilder sb, ref FToken? prev)
    {
        var t = tokens[i];
        if (t.Kind != FKind.Word || !string.Equals(t.Text, "VIEW", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (i + 1 >= tokens.Count) return 0;
        var nameTok = tokens[i + 1];
        if (nameTok.Kind != FKind.Word && nameTok.Kind != FKind.QuotedIdent) return 0;

        if (NeedsSpaceBefore(prev, t, sb)) sb.Append(' ');
        sb.Append("view ");
        sb.Append(MaybeLowercase(nameTok));
        int j = i + 2;
        prev = nameTok;

        // Optional column list — one column per line via the shared paren-list builder (§F). The
        // per-kind loop this replaced is gone; INSERT / VALUES / UPDATE OR INSERT / EXECUTE BLOCK
        // ride the same builder.
        if (j < tokens.Count && tokens[j] is { Kind: FKind.Punctuation, Text: "(" })
        {
            int close = MatchParen(tokens, j);
            var items = SplitTopLevelCommas(tokens, j + 1, close);
            sb.Append(' ').Append(FormatBrokenList(items, ViewColumnIndent));
            prev = close < tokens.Count ? tokens[close] : nameTok;
            j = close < tokens.Count ? close + 1 : tokens.Count;
        }

        // Optional AS on its own line (the view-body separator).
        if (j < tokens.Count && tokens[j].Kind == FKind.Word
            && string.Equals(tokens[j].Text, "AS", StringComparison.OrdinalIgnoreCase))
        {
            TrimTrailingSpaces(sb);
            sb.Append('\n').Append("as");
            prev = tokens[j];
            j++;
        }

        return j - i;
    }

    // ── Shared parenthesized-list builder (§F) ──────────────────────────────────────────────────
    //
    // ONE mechanism for every "( item, item, … )" comma list the formatter lays out — the CREATE VIEW
    // column list today; INSERT column lists, VALUES lists, UPDATE OR INSERT lists, and EXECUTE BLOCK
    // parameter lists in the following P8 steps. It replaced the bespoke per-kind emitter each of those
    // would otherwise grow. Two knobs only: whether to break one-item-per-line, and the item indent
    // when broken. Item CONTENT is rendered by Emit — so spacing, lowercasing, function-call gluing,
    // and nested parens are identical to every other place SQL is emitted (no parallel item renderer).
    // The break decision (usually width-driven) stays with each CALLER, which knows its own context.

    // The FToken index matching the '(' at <paramref name="openIdx"/> (nesting-aware), or
    // <c>tokens.Count</c> when the list is unterminated (malformed / mid-edit — §0 keeps it lossless).
    private static int MatchParen(List<FToken> tokens, int openIdx)
    {
        int depth = 0;
        for (int k = openIdx; k < tokens.Count; k++)
        {
            if (tokens[k] is { Kind: FKind.Punctuation, Text: "(" }) depth++;
            else if (tokens[k] is { Kind: FKind.Punctuation, Text: ")" })
            {
                if (--depth == 0) return k;
            }
        }
        return tokens.Count;
    }

    // Splits [start, end) into top-level (nesting-aware) comma-separated item ranges; commas inside
    // nested parens stay within their item. A trailing/empty segment yields an empty item (preserved
    // verbatim by the §0 safety net) rather than being dropped.
    private static List<List<FToken>> SplitTopLevelCommas(List<FToken> tokens, int start, int end)
    {
        var items = new List<List<FToken>>();
        var current = new List<FToken>();
        int depth = 0;
        for (int k = start; k < end && k < tokens.Count; k++)
        {
            var t = tokens[k];
            if (t.Kind == FKind.Punctuation && t.Text == "(") depth++;
            else if (t.Kind == FKind.Punctuation && t.Text == ")") { if (depth > 0) depth--; }
            if (depth == 0 && t.Kind == FKind.Punctuation && t.Text == ",")
            {
                items.Add(current);
                current = new List<FToken>();
                continue;
            }
            current.Add(t);
        }
        items.Add(current);
        return items;
    }

    // Renders each item's tokens to its formatted string via Emit — the ONE item renderer shared by
    // both list layouts (so spacing/lowercasing/nesting is identical to plain SQL, no parallel path).
    private static List<string> RenderListItems(List<List<FToken>> items)
    {
        var rendered = new List<string>(items.Count);
        foreach (var it in items) rendered.Add(Emit(it).Trim());
        return rendered;
    }

    // One item per line, indented by <paramref name="itemIndent"/>, ')' glued to the last item — the
    // shipped CREATE VIEW column-list style (always vertical, regardless of width).
    private static string FormatBrokenList(List<List<FToken>> items, string itemIndent)
    {
        var rendered = RenderListItems(items);
        var sb = new StringBuilder("(");
        for (int k = 0; k < rendered.Count; k++)
        {
            sb.Append('\n').Append(itemIndent).Append(rendered[k]);
            if (k < rendered.Count - 1) sb.Append(',');
        }
        return sb.Append(')').ToString();
    }

    // Adaptive layout: inline "(a, b, c)" when it fits at <paramref name="openColumn"/>, else the items
    // packed across lines up to the width limit with the continuation aligned under the first item —
    // multiple items per line while they fit (length/readability-driven wrap, NOT one item per line).
    // The shared reflow for INSERT / VALUES / UPDATE OR INSERT / EXECUTE BLOCK / FOR SELECT lists.
    private static string FormatAdaptiveList(List<List<FToken>> items, int openColumn)
    {
        var rendered = RenderListItems(items);
        var inline = "(" + string.Join(", ", rendered) + ")";
        if (rendered.Count <= 1 || openColumn + inline.Length <= MaxLineWidth) return inline;
        var indent = new string(' ', openColumn + 1);
        return PackWithContinuation(rendered, head: "(", continuationIndent: indent, tail: ")", startColumn: openColumn);
    }

    // ── INSERT / UPDATE OR INSERT layout (§P8) — composes the shared list builder + Emit ─────────
    //
    // "<verb> into <target> (cols)" on one line, then "values (…)" (or "select …" / "default values")
    // on its own line, "matching (…)" (UPDATE OR INSERT) and "returning …" each on their own, ';'
    // glued. The column / value / matching lists ride the shared adaptive builder (§F: inline while
    // they fit, else packed to width); INSERT … SELECT reuses Emit for the query. ONE formatter for
    // both statement kinds — they differ only by the leading verb length (<paramref name="headerLen"/>:
    // 2 for "insert into", 4 for "update or insert into") and the MATCHING clause. Operates on the flat
    // token list (not the AST node) so the PSQL body emitter can delegate to it too. Any shape it
    // doesn't recognise falls back to the generic emitter — the §0 safety net guarantees no loss.
    private static string FormatInsertFamily(List<FToken> tokens, int headerLen)
    {
        int n = tokens.Count;
        if (n <= headerLen || !IsWordTok(tokens[headerLen - 1], "INTO"))
            return Emit(tokens);

        bool semi = IsPunctTok(tokens[n - 1], ";");
        int end = semi ? n - 1 : n;

        int boundary = FindInsertListOrSource(tokens, headerLen, end);
        if (boundary < 0) return Emit(tokens); // no column list and no known source → generic

        var sb = new StringBuilder();
        for (int h = 0; h < headerLen; h++)
        {
            if (h > 0) sb.Append(' ');
            sb.Append(tokens[h].Text.ToLowerInvariant());
        }
        sb.Append(' ').Append(Emit(tokens.GetRange(headerLen, boundary - headerLen)).Trim());
        int j = boundary;

        // Optional column list, on the same line as the target.
        if (j < end && IsPunctTok(tokens[j], "("))
        {
            int close = MatchParen(tokens, j);
            var cols = SplitTopLevelCommas(tokens, j + 1, Math.Min(close, end));
            int openColumn = sb.Length + 1; // where '(' lands after the joining space
            sb.Append(' ').Append(FormatAdaptiveList(cols, openColumn));
            j = close < end ? close + 1 : end;
        }

        // Source clause, on its own line.
        if (j < end && IsWordTok(tokens[j], "VALUES") && j + 1 < end && IsPunctTok(tokens[j + 1], "("))
        {
            int vOpen = j + 1;
            int close = MatchParen(tokens, vOpen);
            var vals = SplitTopLevelCommas(tokens, vOpen + 1, Math.Min(close, end));
            const string head = "values ";
            sb.Append('\n').Append(head).Append(FormatAdaptiveList(vals, head.Length));
            j = close < end ? close + 1 : end;
        }
        else if (j < end)
        {
            // INSERT … SELECT / WITH / DEFAULT VALUES — the query/body reuses the clause-break emitter.
            sb.Append('\n').Append(Emit(tokens.GetRange(j, end - j)));
            j = end;
        }

        // Trailing clauses, each on its own line: MATCHING (UPDATE OR INSERT) via the shared list
        // builder; RETURNING / anything else via Emit. Nothing is ever dropped.
        while (j < end)
        {
            if (IsWordTok(tokens[j], "MATCHING") && j + 1 < end && IsPunctTok(tokens[j + 1], "("))
            {
                int close = MatchParen(tokens, j + 1);
                var m = SplitTopLevelCommas(tokens, j + 2, Math.Min(close, end));
                const string head = "matching ";
                sb.Append('\n').Append(head).Append(FormatAdaptiveList(m, head.Length));
                j = close < end ? close + 1 : end;
            }
            else
            {
                sb.Append('\n').Append(Emit(tokens.GetRange(j, end - j)));
                j = end;
            }
        }

        if (semi) sb.Append(';');
        return sb.ToString();
    }

    // The first depth-0 '(' (the column list) or source keyword (VALUES/SELECT/WITH/DEFAULT) in
    // [start, end) — the boundary that ends the INSERT target. -1 when neither is present (⇒ generic).
    private static int FindInsertListOrSource(List<FToken> tokens, int start, int end)
    {
        for (int k = start; k < end; k++)
        {
            var t = tokens[k];
            if (t.Kind == FKind.Punctuation && t.Text == "(") return k;
            if (t.Kind == FKind.Word
                && (t.Text.Equals("VALUES", StringComparison.OrdinalIgnoreCase)
                    || t.Text.Equals("SELECT", StringComparison.OrdinalIgnoreCase)
                    || t.Text.Equals("WITH", StringComparison.OrdinalIgnoreCase)
                    || t.Text.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase)))
                return k;
        }
        return -1;
    }

    private static string MaybeLowercase(FToken t)
        // Lowercase words (keywords + identifiers + named parameters). Strings, quoted identifiers,
        // comments and numbers pass through verbatim.
        => t.Kind == FKind.Word ? t.Text.ToLowerInvariant() : t.Text;

    private static bool NeedsSpaceBefore(FToken? prev, FToken t, StringBuilder sb)
    {
        if (sb.Length == 0) return false;
        char last = sb[sb.Length - 1];
        if (last == '\n' || last == ' ') return false;
        if (last == '(' || last == '.' || last == ':') return false;

        if (t.Kind == FKind.Punctuation)
        {
            var p = t.Text;
            if (p == "," || p == ";" || p == ")" || p == "." || p == "::") return false;
            // Function call: no space between an identifier/function/type name and "(".
            if (p == "("
                && prev is { } pv
                && (pv.Kind == FKind.Word || pv.Kind == FKind.QuotedIdent)
                && !(pv.Kind == FKind.Word && IsStyleKeyword(pv.Text)))
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

    // ── PSQL body emitter (block-structured; CASE-safe) ────────────────────────────────────────
    //
    // Reuses Emit for every leaf statement (so spacing / lowercasing / :var gluing / SELECT clause
    // breaks are identical to DML mode) and adds only BEGIN/END indentation + control-flow layout.
    // A statement is collected up to its top-level ';', so a CASE…END (which has no ';') is consumed
    // WHOLE inside a statement and never mistaken for a block END.

    private static string FormatPsqlBody(List<FToken> body, string? header)
    {
        var lines = new List<string>();
        int i = 0;
        while (i < body.Count)
        {
            int before = i;
            EmitPsqlUnit(body, ref i, 0, lines);
            if (i == before) EmitStrayToken(body, ref i, 0, lines); // §0: never skip — emit verbatim
        }

        var bodyStr = string.Join("\n", lines);
        return string.IsNullOrEmpty(header) ? bodyStr : header + "\n" + bodyStr;
    }

    // Emits one PSQL "unit" (a leaf statement, or a compound: BEGIN block / IF / WHILE / FOR / local
    // subprogram) at <paramref name="indent"/>; advances i.
    private static void EmitPsqlUnit(List<FToken> sig, ref int i, int indent, List<string> lines)
    {
        while (i < sig.Count && sig[i].IsComment)
        {
            MaybeBlankLine(lines, sig[i].BlankBefore);
            AddPsqlLine(lines, indent, sig[i].Text);
            i++;
        }
        if (i >= sig.Count) return;
        if (IsWordTok(sig[i], "END")) return; // belongs to the enclosing BEGIN loop
        MaybeBlankLine(lines, sig[i].BlankBefore);

        var t = sig[i];
        if (t.Kind == FKind.Word)
        {
            var up = t.Text.ToUpperInvariant();
            if (up == "BEGIN")
            {
                AddPsqlLine(lines, indent, "begin");
                i++;
                while (i < sig.Count && !IsWordTok(sig[i], "END"))
                {
                    int before = i;
                    EmitPsqlUnit(sig, ref i, indent + 1, lines);
                    if (i == before) EmitStrayToken(sig, ref i, indent + 1, lines);
                }
                if (i < sig.Count && IsWordTok(sig[i], "END"))
                {
                    MaybeBlankLine(lines, sig[i].BlankBefore);
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
                EmitPsqlBranch(sig, ref i, indent, lines);
                while (i < sig.Count && sig[i].IsComment) { MaybeBlankLine(lines, sig[i].BlankBefore); AddPsqlLine(lines, indent, sig[i].Text); i++; }
                if (i < sig.Count && IsWordTok(sig[i], "ELSE"))
                {
                    i++;
                    AddPsqlLine(lines, indent, "else");
                    EmitPsqlBranch(sig, ref i, indent, lines);
                }
                return;
            }
            if (up == "WHILE" || up == "FOR")
            {
                AddPsqlEmit(lines, indent, CollectUntilWord(sig, ref i, "DO"));
                EmitPsqlBranch(sig, ref i, indent, lines);
                return;
            }
            if (up == "DECLARE"
                && i + 1 < sig.Count && sig[i + 1].Kind == FKind.Word
                && (sig[i + 1].Text.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase)
                    || sig[i + 1].Text.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase)))
            {
                AddPsqlEmit(lines, indent, CollectUntilWordExclusive(sig, ref i, "BEGIN"));
                int before = i;
                EmitPsqlUnit(sig, ref i, indent, lines); // the subprogram's BEGIN…END
                if (i == before) EmitStrayToken(sig, ref i, indent, lines);
                return;
            }
            // A packaged subprogram DEFINITION — a bare FUNCTION/PROCEDURE WITH a body, as found in a
            // PACKAGE BODY (gotcha #152). Emit the header up to the body BEGIN, then recurse for the
            // BEGIN…END block so the enclosing package-body loop stops only at the package's OWN END.
            if ((up == "FUNCTION" || up == "PROCEDURE") && IsSubprogramDefinition(sig, i))
            {
                AddPsqlEmit(lines, indent, CollectUntilWordExclusive(sig, ref i, "BEGIN"));
                int before = i;
                EmitPsqlUnit(sig, ref i, indent, lines); // the subprogram's BEGIN…END
                if (i == before) EmitStrayToken(sig, ref i, indent, lines);
                return;
            }
        }

        AddPsqlEmit(lines, indent, CollectPsqlStatement(sig, ref i));
    }

    private static void EmitPsqlBranch(List<FToken> sig, ref int i, int indent, List<string> lines)
    {
        while (i < sig.Count && sig[i].IsComment) { AddPsqlLine(lines, indent + 1, sig[i].Text); i++; }
        if (i < sig.Count && IsWordTok(sig[i], "BEGIN"))
        {
            EmitPsqlUnit(sig, ref i, indent, lines); // block aligned under the header
        }
        else
        {
            int before = i;
            EmitPsqlUnit(sig, ref i, indent + 1, lines); // single statement indented
            if (i == before) EmitStrayToken(sig, ref i, indent + 1, lines);
        }
    }

    private static void MaybeBlankLine(List<string> lines, bool hadBlank)
    {
        if (hadBlank && lines.Count > 0 && lines[lines.Count - 1].Length != 0)
            lines.Add(string.Empty);
    }

    private static List<FToken> CollectUntilWord(List<FToken> sig, ref int i, string word)
    {
        var list = new List<FToken>();
        while (i < sig.Count)
        {
            var t = sig[i];
            list.Add(t); i++;
            if (t.Kind == FKind.Word && t.Text.Equals(word, StringComparison.OrdinalIgnoreCase)) break;
        }
        return list;
    }

    private static List<FToken> CollectUntilWordExclusive(List<FToken> sig, ref int i, string word)
    {
        var list = new List<FToken>();
        while (i < sig.Count)
        {
            var t = sig[i];
            if (t.Kind == FKind.Word && t.Text.Equals(word, StringComparison.OrdinalIgnoreCase)) break;
            list.Add(t); i++;
        }
        return list;
    }

    // True when a leading FUNCTION/PROCEDURE begins a packaged subprogram DEFINITION (has an
    // AS … BEGIN … END body) vs a package-header forward declaration (… ; — no AS/body).
    private static bool IsSubprogramDefinition(List<FToken> sig, int i)
    {
        if (i + 1 >= sig.Count || sig[i + 1].Kind != FKind.Word) return false; // need a name
        int depth = 0;
        for (int k = i + 1; k < sig.Count; k++)
        {
            var t = sig[k];
            if (t.Kind == FKind.Punctuation && t.Text == "(") { depth++; continue; }
            if (t.Kind == FKind.Punctuation && t.Text == ")") { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (t.Kind == FKind.Punctuation && t.Text == ";") return false; // forward declaration
            if (t.Kind == FKind.Word)
            {
                if (t.Text.Equals("AS", StringComparison.OrdinalIgnoreCase)) return true;   // has a body
                if (t.Text.Equals("END", StringComparison.OrdinalIgnoreCase)) return false;
            }
        }
        return false;
    }

    // Collects one statement up to and INCLUDING its terminating top-level ';'. A CASE…END has no
    // ';' so it is collected whole (the END is NOT a block END).
    private static List<FToken> CollectPsqlStatement(List<FToken> sig, ref int i)
    {
        var list = new List<FToken>();
        while (i < sig.Count)
        {
            var t = sig[i];
            list.Add(t); i++;
            if (t.Kind == FKind.Punctuation && t.Text == ";") break;
        }
        return list;
    }

    private static void AddPsqlEmit(List<string> lines, int indent, List<FToken> stmt)
    {
        if (stmt.Count == 0) return;

        // SELECT … INTO :vars (PSQL singleton select) — put the INTO clause on its own line.
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

    private static int FindTopLevelWord(List<FToken> tokens, string word)
    {
        int depth = 0;
        for (int k = 0; k < tokens.Count; k++)
        {
            var t = tokens[k];
            if (t.Kind == FKind.Punctuation && t.Text == "(") depth++;
            else if (t.Kind == FKind.Punctuation && t.Text == ")") { if (depth > 0) depth--; }
            else if (depth == 0 && t.Kind == FKind.Word && t.Text.Equals(word, StringComparison.OrdinalIgnoreCase))
                return k;
        }
        return -1;
    }

    private static void AddPsqlLine(List<string> lines, int indent, string text)
        => lines.Add(new string(' ', indent * PsqlIndentSize) + text);

    // §0 Formatter Safety: a token the block structurer could not place (malformed / incomplete PSQL
    // — e.g. a stray/unmatched END mid-edit) is emitted VERBATIM on its own line and consumed, never
    // silently skipped. This makes the emitter lexeme-lossless by construction so the per-statement
    // safety check (LexemesPreserved) does not have to fall back to a fully-verbatim statement for the
    // common near-valid case. Words are lowercased like every other word; comments print as-is.
    private static void EmitStrayToken(List<FToken> sig, ref int i, int indent, List<string> lines)
    {
        if (i >= sig.Count) return;
        var t = sig[i];
        MaybeBlankLine(lines, t.BlankBefore);
        AddPsqlLine(lines, indent, t.IsComment ? t.Text : MaybeLowercase(t));
        i++;
    }

    private static bool IsWordTok(FToken t, string w)
        => t.Kind == FKind.Word && t.Text.Equals(w, StringComparison.OrdinalIgnoreCase);
    private static bool IsPunctTok(FToken t, string p) => t.Kind == FKind.Punctuation && t.Text == p;

    // ── §0 Formatter Safety — lexeme-preservation invariant ─────────────────────────────────────
    //
    // The formatter re-emits from a flattened token model rather than by copying the source span, so
    // (unlike the AST's byte-for-byte token overlay) each emit path is individually responsible for
    // reproducing every token. To make §0 (never lose the user's code) a CHECKED invariant rather than
    // a property we merely trust every path to uphold, formatting output is compared to its input, and
    // on any loss the source is kept verbatim (Format(SqlScript), two levels: per statement + per
    // script). This turns "the formatter must never drop a token" from a promise into a guarantee that
    // holds even for malformed / incomplete SQL the parser could not fully model.
    //
    // A "lexeme" is a significant token — words (keyword/identifier/parameter) compared
    // case-insensitively because the formatter lowercases unquoted words; strings, numbers, quoted
    // identifiers, and punctuation compared exactly — PLUS every comment (compared with the trailing
    // whitespace trimmed, since the formatter trims line-comment tails). Whitespace is deliberately
    // NOT compared: re-spacing is the formatter's entire job. For any well-formed input the two
    // sequences are identical (the formatter changes only whitespace and word case), so this guard
    // never rejects valid code — it fires only on a genuine loss, which is exactly when it must.

    private enum LexClass { Word, Verbatim, Comment }
    private readonly record struct Lexeme(LexClass Class, string Text);

    // True when the formatted output reproduces, lexeme-for-lexeme, the input token stream.
    private static bool LexemesPreserved(IReadOnlyList<SqlToken> input, string formatted)
        => LexemesEqual(LexemesOf(input), LexemesOf(SqlLexer.Tokenize(formatted)));

    // True when the whole formatted script reproduces, lexeme-for-lexeme, the whole input text.
    private static bool LexemesPreserved(string input, string formatted)
        => LexemesEqual(LexemesOf(SqlLexer.Tokenize(input)), LexemesOf(SqlLexer.Tokenize(formatted)));

    private static bool LexemesEqual(List<Lexeme> a, List<Lexeme> b)
    {
        if (a.Count != b.Count) return false;
        for (int k = 0; k < a.Count; k++)
        {
            if (a[k] != b[k]) return false;
        }
        return true;
    }

    private static List<Lexeme> LexemesOf(IReadOnlyList<SqlToken> tokens)
    {
        var list = new List<Lexeme>(tokens.Count * 2);
        foreach (var t in tokens)
        {
            foreach (var tr in t.LeadingTrivia)
            {
                if (tr.Kind is TriviaKind.LineComment or TriviaKind.BlockComment)
                {
                    list.Add(new Lexeme(LexClass.Comment, tr.Text.TrimEnd()));
                }
            }
            if (t.Kind == TokenKind.EndOfFile) continue; // sentinel; its trailing comments counted above
            list.Add(t.Kind switch
            {
                // Words are lowercased on output → compare case-insensitively.
                TokenKind.Keyword or TokenKind.Identifier or TokenKind.Parameter
                    => new Lexeme(LexClass.Word, t.Text.ToLowerInvariant()),
                // Strings / numbers / quoted identifiers / commas / dots / semicolons / parens /
                // operators / unknown pass through untouched → compare exactly.
                _ => new Lexeme(LexClass.Verbatim, t.Text),
            });
        }
        return list;
    }

    // ── Long-line wrapping (IBExpert style, 120-char threshold) ────────────────────────────────

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

        if (line.StartsWith("select ", StringComparison.Ordinal))
        {
            var wrapped = TryWrapSelectColumns(line);
            if (wrapped is not null) return wrapped;
        }

        var inWrapped = TryWrapInList(line);
        if (inWrapped is not null) return inWrapped;

        return line;
    }

    private static string? TryWrapSelectColumns(string line)
    {
        string head =
            line.StartsWith("select distinct ", StringComparison.Ordinal) ? "select distinct " :
            line.StartsWith("select all ", StringComparison.Ordinal) ? "select all " :
            "select ";

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
        if (LooksLikeSubquery(inner)) return null; // subqueries handled by the structural break

        var parts = SplitByTopLevelComma(inner);
        if (parts.Count < 2) return null;

        var head = line.Substring(0, parenOpen + 1);
        var tail = line.Substring(parenClose);
        var continuation = new string(' ', parenOpen + 1);

        return PackWithContinuation(parts, head, continuation, tail);
    }

    // Packs pre-rendered items onto lines up to MaxLineWidth, wrapping to a continuation indent aligned
    // under the first item — the ONE adaptive-reflow algorithm for every list the formatter wraps: the
    // string-level SELECT-column and IN-list wrapping (startColumn 0, head = the line prefix) AND the
    // token-level paren-list builder (startColumn = where '(' sits, head = "("). "Adaptive": as many
    // items per line as fit; a new line only when the next item would overflow (readability, not
    // one-item-per-line). <paramref name="startColumn"/> is the column at which <paramref name="head"/>
    // begins on the line, so the first line's budget is correct even when the list is not at column 0.
    private static string PackWithContinuation(
        List<string> parts, string head, string continuationIndent, string? tail, int startColumn = 0)
    {
        var sb = new StringBuilder();
        sb.Append(head);
        int curLen = startColumn + head.Length;
        bool atLineStart = false;

        for (int i = 0; i < parts.Count; i++)
        {
            var seg = parts[i].Trim();
            if (i < parts.Count - 1) seg += ",";

            bool isFirst = i == 0;
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

    private static int FindInOpeningParen(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == '\'') { i = SkipString(s, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(s, i); continue; }
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
            else if (c == ')') { depth--; if (depth == 0) return i; }
            i++;
        }
        return -1;
    }

    private static bool LooksLikeSubquery(string inner)
    {
        int i = 0;
        while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
        return inner.Length - i >= 7 && string.CompareOrdinal(inner, i, "select ", 0, 7) == 0;
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
        i++; // caller confirmed s[i] == '\''
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
        i++; // caller confirmed s[i] == '"'
        while (i < s.Length && s[i] != '"') i++;
        return i < s.Length ? i + 1 : i;
    }
}
