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
    private const string CteBodyIndent = "    ";      // WITH-CTE body indent (IBExpert style)
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

        var result = string.Join("\n", parts);

        // §0 Formatter Safety (absolute backstop): if the whole formatted result differs from the input
        // by even one lexeme, refuse — return the input unchanged rather than emit anything lossy.
        return LexemesPreserved(root.Text, result) ? result : root.Text;
    }

    // ── Statement dispatch (100% AST-driven) ──────────────────────────────────────────────────

    private static string FormatStatement(string source, SqlStatement stmt) => stmt switch
    {
        // §0 safety valve: unrecognised or empty statements are reproduced verbatim.
        RawStatement or EmptyStatement => VerbatimStatement(source, stmt),

        // PSQL definitions (CREATE/ALTER/RECREATE PROCEDURE/TRIGGER/FUNCTION/PACKAGE): the header up
        // to the body's AS is kept verbatim (it is persistent DDL — the user's object definition — so
        // we do not reshape it), the body is block-structured.
        DdlStatement { IsPsqlDefinition: true } => FormatWithHeaderAndBody(source, stmt),

        // EXECUTE BLOCK — a runnable anonymous block, formatted like every other executable statement:
        // its header (input-parameter list + RETURNS list) is laid out via the shared adaptive builder
        // and lowercased, then the body is block-structured. Unlike a CREATE definition it is not
        // persistent DDL, so the lowercase-all layout applies. Unexpected header shapes fall back to the
        // verbatim-header path (safe; §0).
        ExecuteBlockStatement => FormatExecuteBlock(source, stmt),

        // A bare/DECLARE-led anonymous block (the body editor's text) — no header, whole body.
        AnonymousBlockStatement => FormatPsqlBody(Flatten(stmt.Tokens), header: null),

        // INSERT and UPDATE OR INSERT — "<verb> into <target> (cols)" then "values (…)" / "select …"
        // and (for UPDATE OR INSERT) "matching (…)" each on its own line, the lists laid out by the
        // shared adaptive builder (§F). One formatter for both (they differ only by the leading verb +
        // MATCHING); unrecognised shapes fall back to the generic emitter (safe; §0 net covers it).
        InsertStatement => FormatInsertFamily(Flatten(stmt.Tokens), headerLen: 2),        // insert into
        UpdateOrInsertStatement => FormatInsertFamily(Flatten(stmt.Tokens), headerLen: 4), // update or insert into

        // SELECT — a plain query goes through the clause-break emitter; a CTE-led "WITH … SELECT …"
        // query (the parser modelled its CTE clause into SelectStatement.With) is laid out as a
        // first-class construct by FormatWithClause. A WITH whose shape the parser could not model has
        // With == null and formats as a plain query (§0-safe; the lexeme net covers it regardless).
        SelectStatement { With: { } withClause } => FormatWithClause(withClause),
        SelectStatement => Emit(Flatten(stmt.Tokens)),

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

    // EXECUTE BLOCK: lay out the header — "execute block ( params )" (adaptive list) then
    // "returns ( cols )" (adaptive list, own line) then "as" on its own line — and block-structure the
    // body. Reuses the shared adaptive list builder + Emit (item content) + FormatPsqlBody (body), so
    // there is no parallel layout logic. Any header shape TryFormatExecuteBlockHeader does not fully
    // recognise falls back to the verbatim-header path — never guess, never lose (§0).
    private static string FormatExecuteBlock(string source, SqlStatement stmt)
    {
        int asIndex = FindTopLevelAs(stmt.Tokens);
        if (asIndex < 0) return FormatPsqlBody(Flatten(stmt.Tokens), header: null);

        var headerToks = new List<SqlToken>(asIndex);
        for (int k = 0; k < asIndex; k++) headerToks.Add(stmt.Tokens[k]);
        string? formattedHeader = TryFormatExecuteBlockHeader(Flatten(headerToks));

        string header;
        if (formattedHeader is null)
        {
            // Safe fallback — keep the header verbatim (the prior behaviour), leading comment re-attached.
            var asTok = stmt.Tokens[asIndex];
            header = source.Substring(stmt.Start, asTok.End - stmt.Start).TrimEnd();
            var lead = LeadingComments(stmt);
            if (lead.Length > 0) header = lead + "\n" + header;
        }
        else
        {
            header = formattedHeader;
        }

        var bodyTokens = new List<SqlToken>(stmt.Tokens.Count - asIndex - 1);
        for (int k = asIndex + 1; k < stmt.Tokens.Count; k++) bodyTokens.Add(stmt.Tokens[k]);
        return FormatPsqlBody(Flatten(bodyTokens), header);
    }

    // Formats the flattened EXECUTE BLOCK header tokens (everything before the body-opening AS) into
    // "execute block [(params)]\n[returns (cols)]\nas", or null when the shape is not the plain expected
    // one (so the caller keeps the header verbatim). Leading comments are preserved on their own lines.
    private static string? TryFormatExecuteBlockHeader(List<FToken> h)
    {
        int p = 0;
        StringBuilder? commentPrefix = null;
        while (p < h.Count && h[p].IsComment)
        {
            commentPrefix ??= new StringBuilder();
            if (commentPrefix.Length > 0) commentPrefix.Append('\n');
            commentPrefix.Append(h[p].Text);
            p++;
        }

        if (p + 1 >= h.Count || !IsWordTok(h[p], "EXECUTE") || !IsWordTok(h[p + 1], "BLOCK"))
            return null;

        var sb = new StringBuilder("execute block");
        int j = p + 2;

        // Optional input-parameter list, on the "execute block" line.
        if (j < h.Count && IsPunctTok(h[j], "("))
        {
            int close = MatchParen(h, j);
            if (close >= h.Count) return null; // unterminated — let the verbatim path keep it
            var ps = SplitTopLevelCommas(h, j + 1, close);
            int openColumn = sb.Length + 1; // where '(' lands after the joining space
            sb.Append(' ').Append(FormatAdaptiveList(ps, openColumn));
            j = close + 1;
        }

        // Optional RETURNS ( … ) on its own line.
        if (j < h.Count && IsWordTok(h[j], "RETURNS"))
        {
            if (j + 1 >= h.Count || !IsPunctTok(h[j + 1], "(")) return null;
            int open = j + 1;
            int close = MatchParen(h, open);
            if (close >= h.Count) return null;
            var rs = SplitTopLevelCommas(h, open + 1, close);
            const string head = "returns ";
            sb.Append('\n').Append(head).Append(FormatAdaptiveList(rs, head.Length));
            j = close + 1;
        }

        // Any leftover header token (an unexpected clause or a mid-header comment) → don't guess.
        if (j != h.Count) return null;

        sb.Append('\n').Append("as");
        return commentPrefix is null ? sb.ToString() : commentPrefix + "\n" + sb;
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

                // SELECT column list → laid out by the shared adaptive builder (inline while it fits,
                // else packed to width). This is the token-level home of long-line wrapping; there is no
                // separate string post-pass.
                if (phrase.Kind == PhraseKind.TopLevel && IsWordTok(meaningful[i], "SELECT"))
                {
                    i += EmitSelectColumnList(meaningful, i + 1, sb, ref prev);
                }
                continue;
            }

            // IN ( … ) value list → the same adaptive builder; a subquery is left to the clause break.
            if (IsWordTok(t, "IN") && i + 1 < meaningful.Count && IsPunctTok(meaningful[i + 1], "(")
                && !StartsSubquery(meaningful, i + 2))
            {
                i += EmitInList(meaningful, i, sb, ref prev) - 1;
                continue;
            }

            // Call argument list — "name ( … )" where name is an identifier/quoted-ident that is not a
            // style keyword (the same "glue name to (" rule NeedsSpaceBefore uses to recognise a call).
            // Its arguments ride the SAME shared adaptive builder as every other list — inline while they
            // fit, else packed under the '(' — so a long EXECUTE PROCEDURE / function call no longer sits
            // on one giant line. A subquery argument (name (SELECT …)) is left to the clause-break emitter.
            if ((t.Kind == FKind.Word || t.Kind == FKind.QuotedIdent)
                && !(t.Kind == FKind.Word && IsStyleKeyword(t.Text))
                && i + 1 < meaningful.Count && IsPunctTok(meaningful[i + 1], "(")
                && !StartsSubquery(meaningful, i + 2))
            {
                i += EmitCallArgList(meaningful, i, sb, ref prev) - 1;
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

    // Emits a SELECT column list (adaptive wrap) starting at <paramref name="start"/> (just past
    // SELECT). Leading DISTINCT/ALL are emitted inline as part of the header; the columns then run to
    // the first depth-0 clause break and are laid out by the shared bare-list builder, measured from
    // the current column (so the continuation aligns under the first column). Returns tokens consumed.
    private static int EmitSelectColumnList(List<FToken> tokens, int start, StringBuilder sb, ref FToken? prev)
    {
        int i = start;
        while (i < tokens.Count && tokens[i].Kind == FKind.Word
               && (tokens[i].Text.Equals("DISTINCT", StringComparison.OrdinalIgnoreCase)
                   || tokens[i].Text.Equals("ALL", StringComparison.OrdinalIgnoreCase)))
        {
            if (NeedsSpaceBefore(prev, tokens[i], sb)) sb.Append(' ');
            sb.Append(tokens[i].Text.ToLowerInvariant());
            prev = tokens[i];
            i++;
        }

        int listEnd = FindColumnListEnd(tokens, i);
        if (listEnd <= i) return i - start; // no columns (defensive)

        if (NeedsSpaceBefore(prev, tokens[i], sb)) sb.Append(' ');
        int openColumn = CurrentColumn(sb);
        var items = SplitTopLevelCommas(tokens, i, listEnd);
        sb.Append(FormatAdaptiveBareList(items, openColumn));
        prev = tokens[listEnd - 1];
        return listEnd - start;
    }

    // Emits "in ( … )" with the value list laid out by the shared adaptive builder, measured from the
    // '(' column. Returns tokens consumed (IN through the matching ')').
    private static int EmitInList(List<FToken> tokens, int inIdx, StringBuilder sb, ref FToken? prev)
    {
        if (NeedsSpaceBefore(prev, tokens[inIdx], sb)) sb.Append(' ');
        sb.Append("in");
        prev = tokens[inIdx];

        int open = inIdx + 1; // '('
        int close = MatchParen(tokens, open);
        var items = SplitTopLevelCommas(tokens, open + 1, close);

        if (NeedsSpaceBefore(prev, tokens[open], sb)) sb.Append(' ');
        int openColumn = CurrentColumn(sb);
        sb.Append(FormatAdaptiveList(items, openColumn));
        prev = close < tokens.Count ? tokens[close] : tokens[open];

        int last = close < tokens.Count ? close : tokens.Count - 1;
        return last - inIdx + 1;
    }

    // Emits a call "name ( … )" — the callee name glued to '(', the argument list laid out by the shared
    // adaptive builder measured from the '(' column (inline while it fits, else packed under it). The ONE
    // mechanism for every call's arguments: EXECUTE PROCEDURE, function/procedure calls, and any other
    // "identifier ( comma-list )" — no per-construct arg formatter. Returns tokens consumed (name through
    // the matching ')').
    private static int EmitCallArgList(List<FToken> tokens, int nameIdx, StringBuilder sb, ref FToken? prev)
    {
        if (NeedsSpaceBefore(prev, tokens[nameIdx], sb)) sb.Append(' ');
        sb.Append(MaybeLowercase(tokens[nameIdx]));
        prev = tokens[nameIdx];

        int open = nameIdx + 1; // '(' (glued to the name — no space)
        int close = MatchParen(tokens, open);
        var items = SplitTopLevelCommas(tokens, open + 1, close);

        int openColumn = CurrentColumn(sb); // column of '(', since it glues to the name
        sb.Append(FormatAdaptiveList(items, openColumn));
        prev = close < tokens.Count ? tokens[close] : tokens[open];

        int last = close < tokens.Count ? close : tokens.Count - 1;
        return last - nameIdx + 1;
    }

    private enum PhraseKind { None, TopLevel, Conjunction }
    private readonly record struct Phrase(PhraseKind Kind, int Length);

    private static Phrase MatchStructuralPhrase(List<FToken> tokens, int i)
    {
        var t = tokens[i];
        if (t.Kind != FKind.Word) return new Phrase(PhraseKind.None, 0);

        // Set operators — UNION [ALL] / INTERSECT / EXCEPT — each break onto their own line so a
        // compound query (and a CTE body) reads with each arm on its own line, at the base indent.
        // One mechanism, applied everywhere Emit runs (top level and inside CTE bodies), so there is
        // no CTE-specific union handling.
        if (string.Equals(t.Text, "UNION", StringComparison.OrdinalIgnoreCase))
        {
            bool all = i + 1 < tokens.Count && tokens[i + 1].Kind == FKind.Word
                       && string.Equals(tokens[i + 1].Text, "ALL", StringComparison.OrdinalIgnoreCase);
            return new Phrase(PhraseKind.TopLevel, all ? 2 : 1);
        }
        if (string.Equals(t.Text, "INTERSECT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Text, "EXCEPT", StringComparison.OrdinalIgnoreCase))
        {
            return new Phrase(PhraseKind.TopLevel, 1);
        }

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
    // The shared reflow for parenthesized lists: INSERT / VALUES / UPDATE OR INSERT / MATCHING / IN.
    private static string FormatAdaptiveList(List<List<FToken>> items, int openColumn)
    {
        var rendered = RenderListItems(items);
        var inline = "(" + string.Join(", ", rendered) + ")";
        if (rendered.Count <= 1 || openColumn + inline.Length <= MaxLineWidth) return inline;
        var indent = new string(' ', openColumn + 1);
        return PackWithContinuation(rendered, head: "(", continuationIndent: indent, tail: ")", startColumn: openColumn);
    }

    // Adaptive layout for a BARE (unparenthesised) comma list — the SELECT column list. Inline when it
    // fits at <paramref name="openColumn"/>, else packed with the continuation aligned under the first
    // item (openColumn spaces). Same packer/threshold as the parenthesised form; no surrounding parens.
    private static string FormatAdaptiveBareList(List<List<FToken>> items, int openColumn)
    {
        var rendered = RenderListItems(items);
        var inline = string.Join(", ", rendered);
        if (rendered.Count <= 1 || openColumn + inline.Length <= MaxLineWidth) return inline;
        var indent = new string(' ', openColumn);
        return PackWithContinuation(rendered, head: "", continuationIndent: indent, tail: null, startColumn: openColumn);
    }

    // The current output column (chars since the last newline) — the openColumn a caller passes to the
    // adaptive builders so wrapping is measured from where the list actually sits on its line.
    private static int CurrentColumn(StringBuilder sb)
    {
        for (int k = sb.Length - 1; k >= 0; k--)
        {
            if (sb[k] == '\n') return sb.Length - 1 - k;
        }
        return sb.Length;
    }

    // The end of a SELECT column list: the first depth-0 clause break (FROM/WHERE/GROUP BY/…, a
    // conjunction, a JOIN) or line comment — paren-aware, so a subquery's inner FROM does not end it.
    private static int FindColumnListEnd(List<FToken> tokens, int start)
    {
        int depth = 0;
        for (int k = start; k < tokens.Count; k++)
        {
            var t = tokens[k];
            if (t.Kind == FKind.Punctuation && t.Text == "(") { depth++; continue; }
            if (t.Kind == FKind.Punctuation && t.Text == ")") { if (depth > 0) depth--; continue; }
            if (depth == 0 && (t.IsComment || MatchStructuralPhrase(tokens, k).Length > 0)) return k;
        }
        return tokens.Count;
    }

    // True when an "IN ( … )" content is a subquery (IN (SELECT …) / IN (WITH …)) — which is left to the
    // normal clause break, never comma-wrapped as a value list.
    private static bool StartsSubquery(List<FToken> tokens, int k)
        => k < tokens.Count && (IsWordTok(tokens[k], "SELECT") || IsWordTok(tokens[k], "WITH"));

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

    // ── WITH-CTE layout (AST-driven) ─────────────────────────────────────────────────────────────
    //
    // The CTE structure is modelled by the parser (SelectStatement.With — a WithClause + CTE nodes),
    // so the formatter reads it from the AST and never re-parses CTEs itself. Layout is IBExpert-style:
    // each CTE's name (+ optional column list via the shared adaptive builder), "as (" on its own line,
    // the CTE body formatted by the shared emitter and indented, ")" on its own; multiple CTEs joined
    // "),"; the main query directly on the next line (one statement — no blank line). Set operators
    // inside a body/main break via Emit (MatchStructuralPhrase). A statement whose CTE clause the parser
    // could not cleanly model has With == null and is emitted as a plain query (§0-safe; the lexeme net
    // is the backstop regardless).
    private static string FormatWithClause(WithClause w)
    {
        var sb = new StringBuilder("with");
        if (w.IsRecursive) sb.Append(" recursive");

        for (int c = 0; c < w.Ctes.Count; c++)
        {
            var cte = w.Ctes[c];
            var nameLine = new StringBuilder(MaybeLowercaseWord(cte.NameToken));

            if (cte.ColumnTokens is { Count: > 0 } colTokens)
            {
                var flat = Flatten(colTokens);
                var cols = SplitTopLevelCommas(flat, 0, flat.Count);
                nameLine.Append(' ');
                nameLine.Append(FormatAdaptiveList(cols, nameLine.Length + (c == 0 ? 5 : 0)));
            }

            string body = IndentBlock(Emit(Flatten(cte.BodyTokens)), CteBodyIndent);

            sb.Append(c == 0 ? ' ' : '\n').Append(nameLine);
            sb.Append('\n').Append("as (");
            sb.Append('\n').Append(body);
            sb.Append('\n').Append(')');
            if (c < w.Ctes.Count - 1) sb.Append(',');
        }

        // Main query directly on the next line — a CTE query is ONE statement, not two.
        sb.Append('\n').Append(Emit(Flatten(w.MainQueryTokens)));
        return sb.ToString();
    }

    // The display text of a name token for the formatter: an unquoted identifier/keyword lowercased
    // (the default style), a quoted identifier preserved verbatim.
    private static string MaybeLowercaseWord(SqlToken t) => t.Kind switch
    {
        TokenKind.Identifier or TokenKind.Keyword => t.Text.ToLowerInvariant(),
        _ => t.Text,
    };

    // Prefixes every non-empty line of <paramref name="text"/> with <paramref name="indent"/>. Used to
    // indent a CTE body under its "as (". Trailing newlines are trimmed so no dangling blank line.
    private static string IndentBlock(string text, string indent)
    {
        var sb = new StringBuilder();
        var parts = text.TrimEnd('\n').Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            if (parts[i].Length > 0) sb.Append(indent).Append(parts[i]);
        }
        return sb.ToString();
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
            if (up == "WHILE")
            {
                AddPsqlEmit(lines, indent, CollectUntilWord(sig, ref i, "DO"));
                EmitPsqlBranch(sig, ref i, indent, lines);
                return;
            }
            if (up == "FOR")
            {
                EmitForSelect(sig, ref i, indent, lines);
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

    // Lays out a PSQL FOR loop — "FOR &lt;select|execute statement&gt; INTO &lt;vars&gt; DO &lt;statement&gt;".
    // FOR SELECT is treated as ONE Firebird construct (like INSERT INTO / UPDATE OR INSERT / EXECUTE
    // BLOCK / EXECUTE PROCEDURE): the "for" keyword prefixes the cursor query's first line — it is NOT
    // split onto its own line, and the query is NOT extra-indented under the loop. The query is formatted
    // by the shared Emit (so its SELECT/FROM/WHERE clause breaks + long-line wrapping match plain DML);
    // then "into &lt;vars&gt;" and "do" each on their own line at the loop indent, and the loop body via
    // the shared EmitPsqlBranch. The query, INTO and DO are found at paren depth 0 (a subquery's inner
    // clauses never leak out). Malformed input (no top-level DO) falls back to the generic statement path
    // — nothing is lost (§0). This is the ONE place FOR is laid out; the WHILE path (single-line
    // condition) stays separate because its "(cond) do" fits on the header line.
    private static void EmitForSelect(List<FToken> sig, ref int i, int indent, List<string> lines)
    {
        int depth = 0, intoIdx = -1, doIdx = -1;
        for (int k = i + 1; k < sig.Count; k++)
        {
            var t = sig[k];
            if (IsPunctTok(t, "(")) depth++;
            else if (IsPunctTok(t, ")")) { if (depth > 0) depth--; }
            else if (depth == 0)
            {
                if (intoIdx < 0 && IsWordTok(t, "INTO")) intoIdx = k;
                else if (IsWordTok(t, "DO")) { doIdx = k; break; }
            }
        }

        if (doIdx < 0)
        {
            // Not a well-formed FOR … DO (mid-edit / malformed) — emit generically, lossless.
            AddPsqlEmit(lines, indent, CollectPsqlStatement(sig, ref i));
            return;
        }

        int queryEnd = intoIdx >= 0 ? intoIdx : doIdx;
        var query = sig.GetRange(i + 1, queryEnd - (i + 1));
        // "for" glued to the query as one construct: prefix "for " to the query's first line, whole thing
        // at the loop indent.
        string forQuery = query.Count > 0 ? "for " + Emit(query).TrimEnd('\n') : "for";
        EmitPsqlLines(lines, indent, forQuery);

        if (intoIdx >= 0)
        {
            var into = sig.GetRange(intoIdx, doIdx - intoIdx); // "into <vars>", loop indent
            EmitPsqlLines(lines, indent, Emit(into));
        }

        AddPsqlLine(lines, indent, "do");

        i = doIdx + 1;
        EmitPsqlBranch(sig, ref i, indent, lines);
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
        EmitPsqlLines(lines, indent, FormatLeafStatement(stmt));
    }

    // Formats ONE leaf statement of a PSQL body by delegating to the SAME statement formatters used at
    // the top level — so an INSERT / UPDATE OR INSERT inside a procedure, trigger, or EXECUTE BLOCK
    // lays out identically to one at the top level. There is no parallel PSQL formatting of these
    // statements: the PSQL emitter owns only the block STRUCTURE (BEGIN/END, IF/WHILE/FOR indentation);
    // the statements themselves are formatted once, here. The only PSQL-specific case is SELECT … INTO
    // :vars (the singleton-select INTO clause on its own line); everything else is the generic
    // clause-break emitter. The uniform per-line indent applied by EmitPsqlLines preserves each
    // formatter's internal alignment.
    private static string FormatLeafStatement(List<FToken> stmt)
    {
        if (IsWordTok(stmt[0], "INSERT")) return FormatInsertFamily(stmt, 2);
        if (IsWordTok(stmt[0], "UPDATE") && stmt.Count > 1 && IsWordTok(stmt[1], "OR"))
            return FormatInsertFamily(stmt, 4);

        // SELECT … INTO :vars (PSQL singleton select) — the INTO clause on its own line.
        if (IsWordTok(stmt[0], "SELECT"))
        {
            int into = FindTopLevelWord(stmt, "INTO");
            if (into > 0)
                return Emit(stmt.GetRange(0, into)) + "\n" + Emit(stmt.GetRange(into, stmt.Count - into));
        }

        return Emit(stmt);
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

    // ── Long-line wrapping ──────────────────────────────────────────────────────────────────────
    //
    // There is ONE long-line wrapping mechanism and it lives at the TOKEN level, inside Emit: a SELECT
    // column list (EmitSelectColumnList) and an IN ( … ) value list (EmitInList) are laid out by the
    // shared adaptive list builders (FormatAdaptiveBareList / FormatAdaptiveList), which pack to width
    // via PackWithContinuation. The former string-level post-pass (WrapLongLines + its own char scanners
    // — SplitByTopLevelComma / FindInOpeningParen / FindMatchingClose / SkipString / SkipQuotedIdent /
    // LooksLikeSubquery) is gone: the token stream already carries the structure those scanners had to
    // re-derive from the rendered text, so it wrapped from precise columns and needed no heuristics.

    // Packs pre-rendered items onto lines up to MaxLineWidth, wrapping to a continuation indent aligned
    // under the first item — the ONE adaptive-reflow algorithm for every list the formatter wraps
    // (SELECT columns via head = "", the parenthesised INSERT/VALUES/IN lists via head = "("). "Adaptive":
    // as many items per line as fit; a new line only when the next item would overflow (readability, not
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
}
