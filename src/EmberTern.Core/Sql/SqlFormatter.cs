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
/// <b>Architecture (Etap 6.9 — AST convergence).</b> Both statement-level AND intra-statement layout
/// are driven by the parse tree. Statement dispatch switches on the node kind (no <c>IsPsql</c> /
/// <c>FindBodyStart</c> guessing). Within a query, an AST-walking layout core (<c>EmitQuery</c>) lays
/// out each clause on its own line and recurses into nested queries (derived table / EXISTS / scalar
/// subquery / IN(SELECT)) as expanded-paren <em>blocks</em>, so multi-level queries indent naturally; a
/// <c>CaseExpression</c> lays out WHEN/THEN/ELSE adaptively (inline when simple, a block when multi-WHEN
/// or over-width). The token emitter (<c>Emit</c>) remains — as the renderer for a clause/expression
/// <em>interior</em> (the structural-depth boundary keeps ordinary expressions as token fragments) and
/// for the constructs the parser deliberately does not model structurally (UPDATE SET / DELETE / MERGE
/// clause layout, PACKAGE bodies) — and it splices the embedded structural child nodes (subqueries /
/// CASE) it is given by span. The PSQL body keeps its proven token-based block structurer (BEGIN/END,
/// IF/WHILE/FOR nesting — robust to malformed input and PACKAGE bodies the parser leaves unmodelled),
/// but delegates each leaf's CONTENT to the same AST-driven formatters as the top level. There is one
/// layout mechanism per construct — no parallel AST + token walker for the same construct.
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

        // CREATE / ALTER / RECREATE VIEW … AS <query> — the header (through AS) keeps its shipped layout,
        // the body query is laid out by the AST-walking core (so a WITH / nested subquery in the body
        // indents naturally). Mutually exclusive with the PSQL-definition branch above.
        DdlStatement { Query: { } vq } => FormatViewStatement(source, stmt, vq),

        // EXECUTE BLOCK — a runnable anonymous block, formatted like every other executable statement:
        // its header (input-parameter list + RETURNS list) is laid out via the shared adaptive builder
        // and lowercased, then the body is block-structured. Unlike a CREATE definition it is not
        // persistent DDL, so the lowercase-all layout applies. Unexpected header shapes fall back to the
        // verbatim-header path (safe; §0).
        ExecuteBlockStatement => FormatExecuteBlock(source, stmt),

        // A bare/DECLARE-led anonymous block (the body editor's text) — no header, whole body.
        AnonymousBlockStatement ab => FormatPsqlBody(Flatten(ab.Tokens), header: null, ab.Body),

        // INSERT and UPDATE OR INSERT — "<verb> into <target> (cols)" then "values (…)" / "select …"
        // and (for UPDATE OR INSERT) "matching (…)" each on its own line, the lists laid out by the
        // shared adaptive builder (§F). One formatter for both (they differ only by the leading verb +
        // MATCHING); unrecognised shapes fall back to the generic emitter (safe; §0 net covers it).
        InsertStatement ins => FormatInsertFamily(Flatten(stmt.Tokens), 2, ins.SourceQuery, ins.Subqueries),
        UpdateOrInsertStatement uoi => FormatInsertFamily(Flatten(stmt.Tokens), 4, null, uoi.Subqueries),

        // UPDATE / DELETE / MERGE have no clause-node model (an intentional structural-depth boundary), so
        // they keep the token clause emitter — but their embedded subqueries (EXISTS / scalar in
        // SET/WHERE/ON/WHEN, and a MERGE USING (…) source query) are spliced from the AST as blocks.
        UpdateStatement u => Emit(Flatten(stmt.Tokens), u.Subqueries),
        DeleteStatement d => Emit(Flatten(stmt.Tokens), d.Subqueries),
        MergeStatement m => Emit(Flatten(stmt.Tokens), m.Children),

        // SELECT — a plain query goes through the clause-break emitter; a CTE-led "WITH … SELECT …"
        // query (the parser modelled it as a WithQuery on SelectStatement.Query) is laid out as a
        // first-class construct by FormatWithClause. A WITH whose shape the parser could not model has
        // Query == null (or a non-WithQuery) and formats as a plain query (§0-safe; the lexeme net covers
        // it regardless).
        SelectStatement { Query: WithQuery wq } => FormatWithClause(wq),
        // A plain (non-WITH) query is laid out by the AST-walking core: clauses per line, nested queries
        // as expanded-paren blocks, CASE laid out. The query node excludes the statement terminator ';',
        // so re-attach it (glued) when the statement carried one.
        SelectStatement { Query: { } q } => WithSemicolon(EmitQuery(q), stmt),
        SelectStatement => Emit(Flatten(stmt.Tokens)),

        // Everything else — all DML plus non-PSQL DDL, COMMENT, SET, GRANT/REVOKE, DECLARE,
        // EXECUTE PROCEDURE/STATEMENT — through the clause-break SQL emitter (which also handles the
        // CREATE VIEW header case internally, exactly as before).
        _ => Emit(Flatten(stmt.Tokens)),
    };

    // Re-attaches the statement terminator ';' (glued) to a query rendered from its QueryNode, which
    // excludes the terminator. A trailing PLAN/ROWS/… lives inside the query node's tokens, so only ';'
    // is handled here.
    private static string WithSemicolon(string queryText, SqlStatement stmt)
    {
        for (int k = stmt.Tokens.Count - 1; k >= 0; k--)
        {
            if (stmt.Tokens[k].Kind == TokenKind.EndOfFile) continue;
            return stmt.Tokens[k].Kind == TokenKind.Semicolon ? queryText + ";" : queryText;
        }
        return queryText;
    }

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
        var bodyNode = (stmt as DdlStatement)?.Body;
        int asIndex = FindTopLevelAs(stmt.Tokens);
        if (asIndex < 0)
        {
            return FormatPsqlBody(Flatten(stmt.Tokens), header: null, bodyNode);
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

        return FormatPsqlBody(Flatten(bodyTokens), header, bodyNode);
    }

    // CREATE/ALTER/RECREATE VIEW … AS <query>: keep the shipped header layout (name + column list +
    // "as" on its own line — via the token view-header emitter) and lay out the body with the AST-walking
    // query core, so a WITH-led / nested-subquery / set-operation view body indents naturally. The header
    // is everything before the body query's first token (which includes the "as"); the body is the query
    // node. §0: the trailing ';' (if any) is re-attached; the header still round-trips its own tokens.
    private static string FormatViewStatement(string source, SqlStatement stmt, QueryNode query)
    {
        var headerToks = new List<SqlToken>();
        foreach (var t in stmt.Tokens) if (t.Start < query.Start) headerToks.Add(t);
        if (headerToks.Count == 0) return Emit(Flatten(stmt.Tokens)); // defensive — no header
        return WithSemicolon(Emit(Flatten(headerToks)) + "\n" + EmitQuery(query), stmt);
    }

    // EXECUTE BLOCK: lay out the header — "execute block ( params )" (adaptive list) then
    // "returns ( cols )" (adaptive list, own line) then "as" on its own line — and block-structure the
    // body. Reuses the shared adaptive list builder + Emit (item content) + FormatPsqlBody (body), so
    // there is no parallel layout logic. Any header shape TryFormatExecuteBlockHeader does not fully
    // recognise falls back to the verbatim-header path — never guess, never lose (§0).
    private static string FormatExecuteBlock(string source, SqlStatement stmt)
    {
        var bodyNode = (stmt as ExecuteBlockStatement)?.Body;
        int asIndex = FindTopLevelAs(stmt.Tokens);
        if (asIndex < 0) return FormatPsqlBody(Flatten(stmt.Tokens), header: null, bodyNode);

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
        return FormatPsqlBody(Flatten(bodyTokens), header, bodyNode);
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

    // A flattened token carries its absolute source span (<see cref="Start"/>/<see cref="End"/>) so the
    // AST-walking layout core (EmitQuery + the structural splice in Emit) can locate, by position, where an
    // embedded structural child node (a subquery / CASE) begins and ends within a token fragment. Synthesized
    // FTokens (none today) would carry 0/0; every real one comes from a lexer token or a comment trivia.
    private readonly record struct FToken(FKind Kind, string Text, bool BlankBefore, int Start = 0, int End = 0)
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
                        list.Add(new FToken(FKind.LineComment, tr.Text.TrimEnd(), newlineRun >= 2, tr.Start, tr.End));
                        newlineRun = 0;
                        break;
                    case TriviaKind.BlockComment:
                        list.Add(new FToken(FKind.BlockComment, tr.Text, newlineRun >= 2, tr.Start, tr.End));
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
        TokenKind.Keyword or TokenKind.Identifier => new FToken(FKind.Word, t.Text, blankBefore, t.Start, t.End),
        TokenKind.QuotedIdentifier => new FToken(FKind.QuotedIdent, t.Text, blankBefore, t.Start, t.End),
        TokenKind.StringLiteral => new FToken(FKind.String, t.Text, blankBefore, t.Start, t.End),
        TokenKind.Number => new FToken(FKind.Number, t.Text, blankBefore, t.Start, t.End),
        // Named parameters (:name / @name) behave like an identifier for spacing + lowercasing;
        // a positional '?' is punctuation.
        TokenKind.Parameter => t.Text == "?"
            ? new FToken(FKind.Punctuation, "?", blankBefore, t.Start, t.End)
            : new FToken(FKind.Word, t.Text, blankBefore, t.Start, t.End),
        _ => new FToken(FKind.Punctuation, t.Text, blankBefore, t.Start, t.End), // Comma/Dot/Semicolon/(/)/Operator/Unknown
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

    // Emits a token fragment (a whole statement for a not-yet-migrated kind, or a clause/expression
    // interior for a migrated query). <paramref name="structuralChildren"/> — when supplied by the
    // AST-walking layout core — are the embedded structural nodes (a subquery / CASE) that live inside this
    // fragment; each is emitted as a laid-out block (expanded-paren query / CASE layout) in place of its
    // tokens, so nested structure follows the AST rather than being re-flattened. When null (list items,
    // UPDATE/DELETE/MERGE/generic statements that have no clause node), NO splice runs and the output is the
    // pure token layout — byte-identical to before the convergence.
    private static string Emit(List<FToken> meaningful, IReadOnlyList<SqlNode>? structuralChildren = null)
    {
        var sb = new StringBuilder();
        FToken? prev = null;
        var splices = StructuralSplices(structuralChildren);
        int nextSplice = 0;

        for (int i = 0; i < meaningful.Count; i++)
        {
            var t = meaningful[i];

            // Structural splice — an embedded subquery / CASE node begins at this token: lay it out as a
            // block and skip the tokens it covers. The ONE place the token emitter defers to AST structure.
            if (nextSplice < splices.Count && !t.IsComment && t.Start == splices[nextSplice].Start)
            {
                var node = splices[nextSplice].Node;
                EmitStructuralChild(node, sb, ref prev);
                int k = i;
                while (k + 1 < meaningful.Count && meaningful[k + 1].Start < node.End) k++;
                i = k;
                nextSplice++;
                continue;
            }

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
                i += EmitInList(meaningful, i, sb, ref prev, structuralChildren) - 1;
                nextSplice = SkipConsumedSplices(splices, nextSplice, meaningful[i]);
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
                i += EmitCallArgList(meaningful, i, sb, ref prev, structuralChildren) - 1;
                nextSplice = SkipConsumedSplices(splices, nextSplice, meaningful[i]);
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

    // Advances the splice cursor past any embedded structural node whose tokens a list builder already
    // consumed (and laid out from the AST) — so the outer loop does not try to re-splice it.
    private static int SkipConsumedSplices(List<(int Start, SqlNode Node)> splices, int next, FToken lastConsumed)
    {
        while (next < splices.Count && splices[next].Start <= lastConsumed.Start) next++;
        return next;
    }

    // Emits "in ( … )" with the value list laid out by the shared adaptive builder, measured from the
    // '(' column. A scalar subquery in a value expression is spliced from <paramref name="children"/>.
    // Returns tokens consumed (IN through the matching ')').
    private static int EmitInList(List<FToken> tokens, int inIdx, StringBuilder sb, ref FToken? prev, IReadOnlyList<SqlNode>? children)
    {
        if (NeedsSpaceBefore(prev, tokens[inIdx], sb)) sb.Append(' ');
        sb.Append("in");
        prev = tokens[inIdx];

        int open = inIdx + 1; // '('
        int close = MatchParen(tokens, open);
        var items = SplitTopLevelCommas(tokens, open + 1, close);

        if (NeedsSpaceBefore(prev, tokens[open], sb)) sb.Append(' ');
        int openColumn = CurrentColumn(sb);
        sb.Append(FormatAdaptiveList(items, openColumn, children));
        prev = close < tokens.Count ? tokens[close] : tokens[open];

        int last = close < tokens.Count ? close : tokens.Count - 1;
        return last - inIdx + 1;
    }

    // Emits a call "name ( … )" — the callee name glued to '(', the argument list laid out by the shared
    // adaptive builder measured from the '(' column (inline while it fits, else packed under it). The ONE
    // mechanism for every call's arguments: EXECUTE PROCEDURE, function/procedure calls, and any other
    // "identifier ( comma-list )" — no per-construct arg formatter. Returns tokens consumed (name through
    // the matching ')').
    private static int EmitCallArgList(List<FToken> tokens, int nameIdx, StringBuilder sb, ref FToken? prev, IReadOnlyList<SqlNode>? children)
    {
        if (NeedsSpaceBefore(prev, tokens[nameIdx], sb)) sb.Append(' ');
        sb.Append(MaybeLowercase(tokens[nameIdx]));
        prev = tokens[nameIdx];

        int open = nameIdx + 1; // '(' (glued to the name — no space)
        int close = MatchParen(tokens, open);
        var items = SplitTopLevelCommas(tokens, open + 1, close);

        int openColumn = CurrentColumn(sb); // column of '(', since it glues to the name
        // A subquery / CASE embedded in an argument is spliced from the structural children (so it nests
        // instead of flattening to the argument column).
        sb.Append(FormatAdaptiveList(items, openColumn, children));
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
    // <paramref name="children"/>, when supplied, are the list's embedded structural nodes (a scalar
    // subquery in a VALUES expression); each item splices the ones inside its own span.
    private static List<string> RenderListItems(List<List<FToken>> items, IReadOnlyList<SqlNode>? children = null)
    {
        var rendered = new List<string>(items.Count);
        foreach (var it in items)
            rendered.Add(Emit(it, children is null ? null : ItemChildren(children, it)).Trim());
        return rendered;
    }

    // One item per line, indented by <paramref name="itemIndent"/>, ')' glued to the last item — the
    // shipped CREATE VIEW column-list style (always vertical, regardless of width).
    private static string FormatBrokenList(List<List<FToken>> items, string itemIndent)
        => FormatBrokenListRendered(RenderListItems(items), itemIndent);

    // One pre-rendered item per line (each block item shifted to the item indent), ')' glued to the last.
    private static string FormatBrokenListRendered(List<string> rendered, string itemIndent)
    {
        var sb = new StringBuilder("(");
        for (int k = 0; k < rendered.Count; k++)
        {
            sb.Append('\n').Append(itemIndent);
            AppendBlock(sb, rendered[k], itemIndent.Length);
            if (k < rendered.Count - 1) sb.Append(',');
        }
        return sb.Append(')').ToString();
    }

    // Adaptive layout: inline "(a, b, c)" when it fits at <paramref name="openColumn"/>, else the items
    // packed across lines up to the width limit with the continuation aligned under the first item —
    // multiple items per line while they fit (length/readability-driven wrap, NOT one item per line).
    // The shared reflow for parenthesized lists: INSERT / VALUES / UPDATE OR INSERT / MATCHING / IN.
    private static string FormatAdaptiveList(List<List<FToken>> items, int openColumn, IReadOnlyList<SqlNode>? children = null)
    {
        var rendered = RenderListItems(items, children);
        // A block (multi-line) item — e.g. a scalar subquery in a VALUES list — forces the vertical
        // one-per-line layout so the block reads cleanly, rather than an inline join.
        foreach (var r in rendered) if (r.Contains('\n')) return FormatBrokenListRendered(rendered, new string(' ', openColumn + 1));
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
    private static string FormatInsertFamily(
        List<FToken> tokens, int headerLen, QueryNode? sourceQuery = null, IReadOnlyList<SqlNode>? subqueries = null)
    {
        int n = tokens.Count;
        if (n <= headerLen || !IsWordTok(tokens[headerLen - 1], "INTO"))
            return Emit(tokens, subqueries);

        bool semi = IsPunctTok(tokens[n - 1], ";");
        int end = semi ? n - 1 : n;

        int boundary = FindInsertListOrSource(tokens, headerLen, end);
        if (boundary < 0) return Emit(tokens, subqueries); // no column list and no known source → generic

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
            // Value expressions may hold a scalar subquery — splice it via the list's structural children.
            sb.Append('\n').Append(head).Append(FormatAdaptiveList(vals, head.Length, subqueries));
            j = close < end ? close + 1 : end;
        }
        else if (j < end && sourceQuery is not null)
        {
            // INSERT … SELECT / WITH — the source query is laid out by the AST-walking core (so its own
            // nested queries indent). RETURNING (if any) is handled by the trailing loop below.
            sb.Append('\n').Append(EmitQuery(sourceQuery));
            int nj = j;
            while (nj < end && tokens[nj].Start < sourceQuery.End) nj++;
            j = nj;
        }
        else if (j < end)
        {
            // DEFAULT VALUES / a source shape the parser did not model as a query — token layout (§0).
            sb.Append('\n').Append(Emit(tokens.GetRange(j, end - j), subqueries));
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
                sb.Append('\n').Append(Emit(tokens.GetRange(j, end - j), subqueries));
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

    // ── AST-walking query layout core (Etap 6.9 formatter convergence) ──────────────────────────
    //
    // A QueryNode lays out its clauses (each on its own line at the query's indent) and RECURSES into
    // nested queries (derived table / EXISTS / scalar subquery / IN(SELECT)) as expanded-paren BLOCKS, so
    // multi-level queries indent naturally instead of flattening to column 0. Everything here renders
    // COLUMN-0-relative; nesting is composed by uniformly shifting a rendered block right (AppendBlock /
    // IndentBlock) — so a flat query is byte-identical to the old token layout, while a nested query gains
    // real indentation, and idempotency holds because the layout is a pure function of the tree. The
    // structural-depth boundary keeps clause interiors as token fragments: each clause is emitted by the
    // shared token emitter (Emit) with its embedded structural children spliced in as blocks.

    private const string QueryIndentUnit = "    "; // one query-nesting level (IBExpert style, == CteBodyIndent)
    private const string CaseArmIndent = "  ";     // WHEN/ELSE indent under CASE
    private const int MaxNestColumn = 40;           // cap runaway nesting width (deep queries stop indenting)

    // The embedded structural child nodes (a subquery / CASE) of a clause/expression fragment, sorted by
    // source start — the splice points the token emitter defers to. FROM items and WhenClauses are NOT
    // splice points (they drive their own layout); only query-in-expression and CASE nodes are.
    private static List<(int Start, SqlNode Node)> StructuralSplices(IReadOnlyList<SqlNode>? children)
    {
        var list = new List<(int, SqlNode)>();
        if (children is null) return list;
        foreach (var c in children)
            if (c is SubqueryExpression or CaseExpression or QueryNode)
                list.Add((c.Start, c));
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    // Emits one embedded structural child (a subquery block or a CASE) in place of its tokens.
    private static void EmitStructuralChild(SqlNode node, StringBuilder sb, ref FToken? prev)
    {
        int lineIndent = Math.Min(CurrentLineIndent(sb), MaxNestColumn);
        switch (node)
        {
            case ExistsExpression ex:
                EmitSubqueryBlock(sb, "exists", ex.Query, ex.Tokens, lineIndent, ref prev);
                break;
            case SubqueryExpression sub: // ScalarSubquery / IN / quantified
                EmitSubqueryBlock(sb, null, sub.Query, sub.Tokens, lineIndent, ref prev);
                break;
            case CaseExpression ce:
                EmitCaseChild(sb, ce, lineIndent, ref prev);
                break;
            case QueryNode q: // a bare query whose parens live in the surrounding tokens (MERGE USING (…))
                EmitBareQueryBlock(sb, q, lineIndent, ref prev);
                break;
        }
    }

    // A bare query node whose enclosing "(" was already emitted by the token path and whose ")" follows in
    // the token stream (MERGE's USING (query) source): lay the query on indented lines so "(" ends its line
    // and ")" returns to the current indent — the expanded-paren block, sharing the tokens' own parens.
    private static void EmitBareQueryBlock(StringBuilder sb, QueryNode q, int lineIndent, ref FToken? prev)
    {
        string inner = EmitQuery(q);
        if (inner.Contains('\n'))
        {
            sb.Append('\n').Append(IndentBlock(inner, new string(' ', lineIndent + QueryIndentUnit.Length)));
            sb.Append('\n').Append(new string(' ', lineIndent));
        }
        else
        {
            sb.Append(inner);
        }
        prev = null; // the following ')' glues with no space
    }

    // A subquery rendered as "[keyword] ( <query> )": inline when the inner query is single-line, else an
    // expanded-paren block ("(" ends the current line, the inner query indented one level, ")" back at the
    // current line's indent). Falls back to a verbatim inline emit when the parser did not model the query.
    private static void EmitSubqueryBlock(
        StringBuilder sb, string? keyword, QueryNode? query, IReadOnlyList<SqlToken> tokens,
        int lineIndent, ref FToken? prev)
    {
        if (query is null)
        {
            if (NeedsSpaceBeforeStructural(sb)) sb.Append(' ');
            sb.Append(Emit(Flatten(tokens)).Trim());
            prev = new FToken(FKind.Punctuation, ")", false);
            return;
        }

        if (keyword is not null)
        {
            if (NeedsSpaceBeforeStructural(sb)) sb.Append(' ');
            sb.Append(keyword);
        }
        if (NeedsSpaceBeforeStructural(sb)) sb.Append(' ');

        string inner = EmitQuery(query);
        string block = inner.Contains('\n')
            ? "(\n" + IndentBlock(inner, QueryIndentUnit) + "\n)"
            : "(" + inner + ")";
        AppendBlock(sb, block, lineIndent);
        prev = new FToken(FKind.Punctuation, ")", false);
    }

    // A CASE rendered inline (simple: ≤1 WHEN, single line, fits) or as a WHEN/THEN/ELSE block.
    private static void EmitCaseChild(StringBuilder sb, CaseExpression ce, int lineIndent, ref FToken? prev)
    {
        string inline = EmitCaseInline(ce);
        int startCol = CurrentColumn(sb) + (NeedsSpaceBeforeStructural(sb) ? 1 : 0);
        bool block = ce.Whens.Count > 1 || inline.Contains('\n') || startCol + inline.Length > MaxLineWidth;

        if (!block)
        {
            if (NeedsSpaceBeforeStructural(sb)) sb.Append(' ');
            sb.Append(inline);
            prev = new FToken(FKind.Word, "end", false);
            return;
        }

        // A block CASE owns its lines. If the current line already has content, move CASE to a fresh line
        // at the line's indent, so WHEN/ELSE sit under CASE (CASE at lineIndent, WHEN at lineIndent+2).
        if (CurrentColumn(sb) > lineIndent)
        {
            TrimTrailingSpaces(sb);
            sb.Append('\n').Append(new string(' ', lineIndent));
        }
        AppendBlock(sb, EmitCaseBlock(ce), lineIndent);
        prev = new FToken(FKind.Word, "end", false);
    }

    // The inline single-line CASE rendering (pure token layout — identical to the pre-convergence inline
    // form). A nested SELECT inside an arm breaks to a newline here, so the caller then chooses the block.
    private static string EmitCaseInline(CaseExpression ce) => Emit(Flatten(ce.Tokens)).Trim();

    // The block CASE rendering (column-0 relative): "case [operand]" / one "when … then …" per line / an
    // "else …" line / "end". Each arm/operand/ELSE interior is the shared token emitter with that region's
    // embedded structural children spliced (so a subquery / nested CASE in a branch lays out as a block).
    private static string EmitCaseBlock(CaseExpression ce)
    {
        var toks = ce.Tokens;
        int endStart = toks.Count > 0 ? toks[toks.Count - 1].Start : ce.End; // the END token
        var whens = ce.Whens;
        var sb = new StringBuilder("case");

        int afterCase = toks.Count > 0 ? toks[0].End : ce.Start;
        // Operand of a simple CASE (between CASE and the first WHEN / ELSE / END).
        int firstBoundary = whens.Count > 0 ? whens[0].Start : endStart;
        var operand = SliceSpan(toks, afterCase, firstBoundary);
        if (operand.Count > 0)
        {
            sb.Append(' ');
            AppendBlock(sb, Emit(Flatten(operand), SpliceChildrenIn(ce.Children, afterCase, firstBoundary)).Trim(), 0);
        }

        // Each arm / the ELSE result on its own line at the arm indent. AppendBlock shifts a multi-line
        // arm's continuation (a spliced subquery / nested CASE block) by the arm indent too, so its lines
        // stay under the arm instead of dedenting.
        foreach (var w in whens)
        {
            sb.Append('\n').Append(CaseArmIndent);
            AppendBlock(sb, Emit(Flatten(w.Tokens), w.Children).Trim(), CaseArmIndent.Length);
        }

        int elseFrom = whens.Count > 0 ? whens[whens.Count - 1].End : afterCase;
        var elseRegion = SliceSpan(toks, elseFrom, endStart);
        if (elseRegion.Count > 0)
        {
            sb.Append('\n').Append(CaseArmIndent);
            AppendBlock(sb, Emit(Flatten(elseRegion), SpliceChildrenIn(ce.Children, elseFrom, endStart)).Trim(), CaseArmIndent.Length);
        }

        sb.Append('\n').Append("end");
        return sb.ToString();
    }

    // The tokens of <paramref name="toks"/> whose start lies in [lo, hi).
    private static List<SqlToken> SliceSpan(IReadOnlyList<SqlToken> toks, int lo, int hi)
    {
        var list = new List<SqlToken>();
        foreach (var t in toks) if (t.Start >= lo && t.Start < hi) list.Add(t);
        return list;
    }

    // The structural children whose span lies within [lo, hi) (for splicing a sub-region's interior).
    private static List<SqlNode> SpliceChildrenIn(IReadOnlyList<SqlNode> children, int lo, int hi)
    {
        var list = new List<SqlNode>();
        foreach (var c in children)
            if ((c is SubqueryExpression or CaseExpression) && c.Start >= lo && c.End <= hi) list.Add(c);
        return list;
    }

    // ── Query dispatch + clause layout ───────────────────────────────────────────────────────────

    // Lays out a query node column-0-relative. The single structural entry the formatter recurses through
    // for every nested query.
    private static string EmitQuery(QueryNode q) => q switch
    {
        SelectQuery sq => EmitSelectQuery(sq),
        SetOperationQuery so => EmitSetOperation(so),
        WithQuery wq => FormatWithClause(wq),
        _ => Emit(Flatten(q.Tokens)), // RawQuery / unmodeled — verbatim token layout (§0)
    };

    private static string EmitSelectQuery(SelectQuery sq)
    {
        var lines = new List<string> { EmitProjection(sq.Select) };
        if (sq.From is not null) lines.Add(EmitFromClause(sq.From));
        if (sq.Where is not null) lines.Add(Emit(Flatten(sq.Where.Tokens), sq.Where.Children));
        if (sq.GroupBy is not null) lines.Add(Emit(Flatten(sq.GroupBy.Tokens), sq.GroupBy.Children));
        if (sq.Having is not null) lines.Add(Emit(Flatten(sq.Having.Tokens), sq.Having.Children));
        if (sq.OrderBy is not null) lines.Add(Emit(Flatten(sq.OrderBy.Tokens), sq.OrderBy.Children));
        return string.Join("\n", lines);
    }

    // A set operation — each operand query on its own lines, the operator (union [all] / intersect /
    // except) on its own line between them (matching the shipped set-op layout), a trailing ORDER BY last.
    private static string EmitSetOperation(SetOperationQuery so)
    {
        var sb = new StringBuilder();
        sb.Append(EmitQuery(so.Left));
        sb.Append('\n').Append(SetOperatorText(so.Operator));
        if (so.All) sb.Append(" all");
        sb.Append('\n').Append(EmitQuery(so.Right));
        if (so.OrderBy is not null) sb.Append('\n').Append(Emit(Flatten(so.OrderBy.Tokens), so.OrderBy.Children));
        return sb.ToString();
    }

    private static string SetOperatorText(SetOperator op) => op switch
    {
        SetOperator.Union => "union",
        SetOperator.Intersect => "intersect",
        SetOperator.Except => "except",
        _ => "union",
    };

    // The SELECT clause. With no embedded structural children the projection is the shared token layout
    // (byte-identical to before). Otherwise each projection item owns its layout — a CASE / scalar-subquery
    // item expands as a block while ordinary items pack adaptively — without forcing its neighbours
    // one-per-line (user directive: a complex item formats itself, it does not change its neighbours' policy).
    private static string EmitProjection(SelectClause sc)
    {
        if (sc.Children.Count == 0) return Emit(Flatten(sc.Tokens));

        var f = Flatten(sc.Tokens);
        // Header = "select" + a leading DISTINCT/ALL run (FIRST n / SKIP n stay with the first item — the
        // pre-convergence behaviour); items start after it.
        int h = 1;
        while (h < f.Count && f[h].Kind == FKind.Word
               && (f[h].Text.Equals("DISTINCT", StringComparison.OrdinalIgnoreCase)
                   || f[h].Text.Equals("ALL", StringComparison.OrdinalIgnoreCase)))
            h++;
        var header = new StringBuilder("select");
        for (int k = 1; k < h; k++) header.Append(' ').Append(f[k].Text.ToLowerInvariant());

        var items = SplitTopLevelCommas(f, h, f.Count);
        var rendered = new List<string>(items.Count);
        bool anyBlock = false;
        foreach (var it in items)
        {
            var itemChildren = ItemChildren(sc.Children, it);
            var r = Emit(it, itemChildren).Trim();
            rendered.Add(r);
            if (r.Contains('\n')) anyBlock = true;
        }

        int projCol = header.Length + 1; // where the first item sits (after "select [mods] ")
        if (!anyBlock)
            return header + " " + JoinAdaptive(rendered, projCol);

        // Block mode: "select [mods]" on its own line, items at projCol; a block item on its own line(s),
        // single-line items packed adaptively.
        return header + "\n" + PackProjectionItems(rendered, projCol);
    }

    // The structural children (subquery / CASE) whose span falls inside a projection item's token range.
    private static List<SqlNode> ItemChildren(IReadOnlyList<SqlNode> children, List<FToken> item)
    {
        int lo = int.MaxValue, hi = 0;
        foreach (var t in item) { if (t.Start < lo) lo = t.Start; if (t.End > hi) hi = t.End; }
        return lo <= hi ? SpliceChildrenIn(children, lo, hi) : new List<SqlNode>();
    }

    // Packs pre-rendered items at column <paramref name="col"/>: single-line items pack multiple-per-line
    // up to the width limit; a multi-line (block) item takes its own line(s) and forces the next item onto
    // a new line. Deterministic + idempotent (a pure function of the rendered items + column).
    private static string PackProjectionItems(List<string> rendered, int col)
    {
        var indent = new string(' ', col);
        var sb = new StringBuilder();
        bool lineStart = true;
        int cur = col;
        for (int i = 0; i < rendered.Count; i++)
        {
            bool last = i == rendered.Count - 1;
            var seg = rendered[i];
            bool isBlock = seg.Contains('\n');

            if (isBlock)
            {
                if (!lineStart || sb.Length > 0) { sb.Append('\n'); }
                sb.Append(indent);
                AppendBlock(sb, seg, col);
                if (!last) sb.Append(',');
                lineStart = false;
                cur = MaxLineWidth + 1; // force the next item to a new line
                continue;
            }

            var piece = last ? seg : seg + ",";
            if (lineStart)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(indent).Append(piece);
                cur = col + piece.Length;
            }
            else if (cur + 1 + piece.Length <= MaxLineWidth)
            {
                sb.Append(' ').Append(piece);
                cur += 1 + piece.Length;
            }
            else
            {
                sb.Append('\n').Append(indent).Append(piece);
                cur = col + piece.Length;
            }
            lineStart = false;
        }
        return sb.ToString();
    }

    // Inline "a, b, c" when it fits at <paramref name="col"/>, else the shared width-packer aligned under
    // the first item (used by the no-block projection path — the same rule as FormatAdaptiveBareList but
    // over pre-rendered items, so a spliced inline subquery is not re-flattened).
    private static string JoinAdaptive(List<string> rendered, int col)
    {
        var inline = string.Join(", ", rendered);
        if (rendered.Count <= 1 || col + inline.Length <= MaxLineWidth) return inline;
        return PackWithContinuation(rendered, head: "", continuationIndent: new string(' ', col), tail: null, startColumn: col);
    }

    // The FROM clause. Byte-identical token layout unless it contains a block-worthy derived table or an
    // embedded subquery/CASE (in a join's ON), in which case it is laid out structurally: comma entries on
    // the FROM line, JOINs each on their own line (source join keywords preserved verbatim), a derived
    // table expanded as a paren block.
    private static string EmitFromClause(FromClause fc)
    {
        // Lay out structurally when the FROM holds any derived table or embedded subquery/CASE — so a
        // nested query is a real block (EmitDerivedTable itself decides inline vs. expanded by its content).
        // A plain FROM (table refs + joins, no nested query) keeps the byte-identical token layout.
        bool structural = false;
        foreach (var n in fc.DescendantNodes())
            if (n is DerivedTable or SubqueryExpression or CaseExpression) { structural = true; break; }
        if (!structural) return Emit(Flatten(fc.Tokens));

        var sb = new StringBuilder("from ");
        for (int i = 0; i < fc.Items.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(EmitFromItem(fc.Items[i]));
        }
        return sb.ToString();
    }

    private static string EmitFromItem(FromItem item) => item switch
    {
        DerivedTable dt => EmitDerivedTable(dt),
        JoinedTable jt => EmitJoinedTable(jt),
        _ => Emit(Flatten(item.Tokens)).Trim(), // TableReference
    };

    private static string EmitDerivedTable(DerivedTable dt)
    {
        var f = Flatten(dt.Tokens);
        int open = f.Count > 0 && f[0] is { Kind: FKind.Punctuation, Text: "(" } ? 0 : -1;
        int close = open >= 0 ? MatchParen(f, open) : -1;
        if (dt.Query is null || close < 0)
            return Emit(Flatten(dt.Tokens)).Trim();

        string inner = EmitQuery(dt.Query);
        string alias = close + 1 < f.Count ? Emit(f.GetRange(close + 1, f.Count - close - 1)).Trim() : string.Empty;
        if (!inner.Contains('\n'))
            return alias.Length > 0 ? "(" + inner + ") " + alias : "(" + inner + ")";

        var block = new StringBuilder("(\n").Append(IndentBlock(inner, QueryIndentUnit)).Append("\n)");
        if (alias.Length > 0) block.Append(' ').Append(alias);
        return block.ToString();
    }

    private static string EmitJoinedTable(JoinedTable jt)
    {
        var sb = new StringBuilder(EmitFromItem(jt.Left));
        // Join keywords sit between the left item and the right item — emit them verbatim (lowercased) so
        // "left outer join" / "natural join" etc. are preserved exactly (JoinKind would drop OUTER/INNER).
        var kw = Emit(Flatten(SliceSpan(jt.Tokens, jt.Left.End, jt.Right.Start))).Trim();
        sb.Append('\n').Append(kw);
        if (kw.Length > 0) sb.Append(' ');
        sb.Append(EmitFromItem(jt.Right));
        if (jt.OnTokens is { Count: > 0 })
        {
            var onChildren = new List<SqlNode>();
            foreach (var c in jt.Children) if (c is SubqueryExpression or CaseExpression) onChildren.Add(c);
            sb.Append(' ').Append(Emit(Flatten(jt.OnTokens), onChildren));
        }
        return sb.ToString();
    }

    // ── Block/indent helpers ───────────────────────────────────────────────────────────────────────

    // The leading-space count of the current (last) line in the buffer.
    private static int CurrentLineIndent(StringBuilder sb)
    {
        int start = 0;
        for (int k = sb.Length - 1; k >= 0; k--) { if (sb[k] == '\n') { start = k + 1; break; } }
        int n = 0;
        for (int k = start; k < sb.Length && sb[k] == ' '; k++) n++;
        return n;
    }

    // Appends a (possibly multi-line) block: its first line continues the current buffer position; every
    // subsequent line is shifted right by <paramref name="contIndent"/> spaces (on top of its own relative
    // indentation). This composes nesting — a block rendered column-0-relative lands at the caller's indent.
    private static void AppendBlock(StringBuilder sb, string block, int contIndent)
    {
        var prefix = new string(' ', contIndent);
        int lineStart = 0;
        for (int i = 0; i < block.Length; i++)
        {
            if (block[i] == '\n')
            {
                sb.Append(block, lineStart, i - lineStart).Append('\n');
                // shift the next line right, unless it is empty
                int j = i + 1;
                if (j < block.Length && block[j] != '\n') sb.Append(prefix);
                lineStart = i + 1;
            }
        }
        sb.Append(block, lineStart, block.Length - lineStart);
    }

    private static bool NeedsSpaceBeforeStructural(StringBuilder sb)
    {
        if (sb.Length == 0) return false;
        char last = sb[sb.Length - 1];
        return last != '\n' && last != ' ' && last != '(';
    }

    // ── WITH-CTE layout (AST-driven) ─────────────────────────────────────────────────────────────
    //
    // The CTE structure is modelled by the parser (SelectStatement.Query is a WithQuery — a WithClause of
    // CTE nodes + the main query, all real QueryNodes since B3), so the formatter reads it from the AST and
    // never re-parses CTEs itself. Layout is IBExpert-style: each CTE's name (+ optional column list via
    // the shared adaptive builder), "as (" on its own line, the CTE body laid out by the AST-walking query
    // core (EmitQuery) and indented, ")" on its own; multiple CTEs joined "),"; the main query directly on
    // the next line (one statement — no blank line). Because the body and the main query recurse through
    // EmitQuery, a nested subquery / derived table / set operation inside a CTE indents naturally (a flat
    // CTE body stays byte-identical to the pre-convergence layout). A statement whose CTE clause the parser
    // could not cleanly model has Query == null / non-WithQuery and is emitted as a plain query (§0-safe;
    // the lexeme net is the backstop regardless).
    private static string FormatWithClause(WithQuery wq)
    {
        var sb = new StringBuilder("with");
        if (wq.With.IsRecursive) sb.Append(" recursive");

        var ctes = wq.With.Ctes;
        for (int c = 0; c < ctes.Count; c++)
        {
            var cte = ctes[c];
            var nameLine = new StringBuilder(MaybeLowercaseWord(cte.NameToken));

            if (cte.ColumnTokens is { Count: > 0 } colTokens)
            {
                var flat = Flatten(colTokens);
                var cols = SplitTopLevelCommas(flat, 0, flat.Count);
                nameLine.Append(' ');
                nameLine.Append(FormatAdaptiveList(cols, nameLine.Length + (c == 0 ? 5 : 0)));
            }

            string body = IndentBlock(EmitQuery(cte.Body), CteBodyIndent);

            sb.Append(c == 0 ? ' ' : '\n').Append(nameLine);
            sb.Append('\n').Append("as (");
            sb.Append('\n').Append(body);
            sb.Append('\n').Append(')');
            if (c < ctes.Count - 1) sb.Append(',');
        }

        // Main query directly on the next line — a CTE query is ONE statement, not two.
        sb.Append('\n').Append(EmitQuery(wq.Query));
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

    private static string FormatPsqlBody(List<FToken> body, string? header, BlockStatement? bodyNode = null)
    {
        var leaves = BuildLeafIndex(bodyNode);
        var lines = new List<string>();
        int i = 0;
        while (i < body.Count)
        {
            int before = i;
            EmitPsqlUnit(body, ref i, 0, lines, leaves);
            if (i == before) EmitStrayToken(body, ref i, 0, lines); // §0: never skip — emit verbatim
        }

        var bodyStr = string.Join("\n", lines);
        return string.IsNullOrEmpty(header) ? bodyStr : header + "\n" + bodyStr;
    }

    // Indexes a parsed PSQL body's leaf/FOR-SELECT statement nodes by source start, so the (token-based)
    // block structurer can hand each leaf's content to the AST-aware formatters — a DML/SELECT leaf lays
    // out with its query structure (nested indentation), a PSQL leaf splices its embedded CASE/subqueries,
    // and a FOR SELECT cursor query is laid out by the query core. Empty when the body was not parsed
    // (a PACKAGE body, or malformed input) — then the emitter uses the pure token layout, unchanged.
    private static Dictionary<int, SqlNode> BuildLeafIndex(BlockStatement? bodyNode)
    {
        var map = new Dictionary<int, SqlNode>();
        if (bodyNode is null) return map;
        foreach (var n in bodyNode.DescendantNodesAndSelf())
            if (n is InsertStatement or UpdateStatement or UpdateOrInsertStatement or DeleteStatement
                or MergeStatement or SelectStatement or PsqlLeafStatement or ForSelectStatement
                or IfStatement or WhileStatement)
                map.TryAdd(n.Start, n);
        return map;
    }

    // Emits one PSQL "unit" (a leaf statement, or a compound: BEGIN block / IF / WHILE / FOR / local
    // subprogram) at <paramref name="indent"/>; advances i.
    private static void EmitPsqlUnit(List<FToken> sig, ref int i, int indent, List<string> lines, IReadOnlyDictionary<int, SqlNode> leaves)
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
                    EmitPsqlUnit(sig, ref i, indent + 1, lines, leaves);
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
                AddPsqlEmit(lines, indent, CollectUntilWord(sig, ref i, "THEN"), leaves);
                EmitPsqlBranch(sig, ref i, indent, lines, leaves);
                while (i < sig.Count && sig[i].IsComment) { MaybeBlankLine(lines, sig[i].BlankBefore); AddPsqlLine(lines, indent, sig[i].Text); i++; }
                if (i < sig.Count && IsWordTok(sig[i], "ELSE"))
                {
                    i++;
                    AddPsqlLine(lines, indent, "else");
                    EmitPsqlBranch(sig, ref i, indent, lines, leaves);
                }
                return;
            }
            if (up == "WHILE")
            {
                AddPsqlEmit(lines, indent, CollectUntilWord(sig, ref i, "DO"), leaves);
                EmitPsqlBranch(sig, ref i, indent, lines, leaves);
                return;
            }
            if (up == "FOR")
            {
                EmitForSelect(sig, ref i, indent, lines, leaves);
                return;
            }
            if (up == "DECLARE"
                && i + 1 < sig.Count && sig[i + 1].Kind == FKind.Word
                && (sig[i + 1].Text.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase)
                    || sig[i + 1].Text.Equals("FUNCTION", StringComparison.OrdinalIgnoreCase)))
            {
                AddPsqlEmit(lines, indent, CollectUntilWordExclusive(sig, ref i, "BEGIN"), leaves);
                int before = i;
                EmitPsqlUnit(sig, ref i, indent, lines, leaves); // the subprogram's BEGIN…END
                if (i == before) EmitStrayToken(sig, ref i, indent, lines);
                return;
            }
            // A packaged subprogram DEFINITION — a bare FUNCTION/PROCEDURE WITH a body, as found in a
            // PACKAGE BODY (gotcha #152). Emit the header up to the body BEGIN, then recurse for the
            // BEGIN…END block so the enclosing package-body loop stops only at the package's OWN END.
            if ((up == "FUNCTION" || up == "PROCEDURE") && IsSubprogramDefinition(sig, i))
            {
                AddPsqlEmit(lines, indent, CollectUntilWordExclusive(sig, ref i, "BEGIN"), leaves);
                int before = i;
                EmitPsqlUnit(sig, ref i, indent, lines, leaves); // the subprogram's BEGIN…END
                if (i == before) EmitStrayToken(sig, ref i, indent, lines);
                return;
            }
        }

        AddPsqlEmit(lines, indent, CollectPsqlStatement(sig, ref i), leaves);
    }

    private static void EmitPsqlBranch(List<FToken> sig, ref int i, int indent, List<string> lines, IReadOnlyDictionary<int, SqlNode> leaves)
    {
        while (i < sig.Count && sig[i].IsComment) { AddPsqlLine(lines, indent + 1, sig[i].Text); i++; }
        if (i < sig.Count && IsWordTok(sig[i], "BEGIN"))
        {
            EmitPsqlUnit(sig, ref i, indent, lines, leaves); // block aligned under the header
        }
        else
        {
            int before = i;
            EmitPsqlUnit(sig, ref i, indent + 1, lines, leaves); // single statement indented
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
    private static void EmitForSelect(List<FToken> sig, ref int i, int indent, List<string> lines, IReadOnlyDictionary<int, SqlNode> leaves)
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
            AddPsqlEmit(lines, indent, CollectPsqlStatement(sig, ref i), leaves);
            return;
        }

        int queryEnd = intoIdx >= 0 ? intoIdx : doIdx;
        var query = sig.GetRange(i + 1, queryEnd - (i + 1));
        // "for" glued to the cursor query as one construct: prefix "for " to the query's first line, whole
        // thing at the loop indent. The cursor query is laid out by the AST-walking core when the parser
        // modelled it (ForSelectStatement.Query — so a nested subquery in the cursor indents), else the
        // token emitter (FOR EXECUTE STATEMENT / an unmodeled cursor).
        string cursor;
        if (query.Count == 0) cursor = string.Empty;
        else if (leaves.TryGetValue(sig[i].Start, out var node) && node is ForSelectStatement { Query: { } fq })
            cursor = EmitQuery(fq);
        else cursor = Emit(query).TrimEnd('\n');
        string forQuery = query.Count > 0 ? "for " + cursor : "for";
        EmitPsqlLines(lines, indent, forQuery);

        if (intoIdx >= 0)
        {
            var into = sig.GetRange(intoIdx, doIdx - intoIdx); // "into <vars>", loop indent
            EmitPsqlLines(lines, indent, Emit(into));
        }

        AddPsqlLine(lines, indent, "do");

        i = doIdx + 1;
        EmitPsqlBranch(sig, ref i, indent, lines, leaves);
    }

    private static void MaybeBlankLine(List<string> lines, bool hadBlank)
    {
        if (hadBlank && lines.Count > 0 && lines[lines.Count - 1].Length != 0)
            lines.Add(string.Empty);
    }

    // Collects up to and including the first top-level <paramref name="word"/> (THEN for IF, DO for WHILE),
    // skipping any nested CASE … END — so a CASE in the condition (its own THEN) does not prematurely end
    // an IF/WHILE header.
    private static List<FToken> CollectUntilWord(List<FToken> sig, ref int i, string word)
    {
        var list = new List<FToken>();
        int caseDepth = 0;
        while (i < sig.Count)
        {
            var t = sig[i];
            list.Add(t); i++;
            if (t.Kind != FKind.Word) continue;
            if (t.Text.Equals("CASE", StringComparison.OrdinalIgnoreCase)) caseDepth++;
            else if (t.Text.Equals("END", StringComparison.OrdinalIgnoreCase)) { if (caseDepth > 0) caseDepth--; }
            else if (caseDepth == 0 && t.Text.Equals(word, StringComparison.OrdinalIgnoreCase)) break;
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

    private static void AddPsqlEmit(List<string> lines, int indent, List<FToken> stmt, IReadOnlyDictionary<int, SqlNode> leaves)
    {
        if (stmt.Count == 0) return;
        EmitPsqlLines(lines, indent, FormatLeafStatement(stmt, leaves));
    }

    // Formats ONE leaf statement of a PSQL body. When the parser modelled the leaf (its span is in the
    // leaf index) it is laid out from the AST — a DML/SELECT leaf gets its full query structure (nested
    // indentation), a PSQL leaf / IF-WHILE header splices its embedded CASE / subqueries — so PSQL bodies
    // enjoy the same AST layout as top-level statements. When it was not modelled (a PACKAGE body, or
    // malformed input where the leaf index is empty) it falls back to the token layout, unchanged. There is
    // no parallel PSQL formatting: the PSQL emitter owns only the block STRUCTURE (BEGIN/END, IF/WHILE/FOR
    // indentation); each leaf's CONTENT is formatted by the same code paths as at the top level.
    private static string FormatLeafStatement(List<FToken> stmt, IReadOnlyDictionary<int, SqlNode> leaves)
        => leaves.TryGetValue(stmt[0].Start, out var node)
            ? FormatAstLeaf(node, stmt)
            : FormatLeafStatementTokens(stmt);

    // The AST-aware leaf renderer: the collected tokens (which the block structurer guarantees are the
    // leaf's complete range, incl. its ';') are formatted by the same per-kind formatters as a top-level
    // statement, driven by the leaf node's structural facts (source query / embedded subqueries / CASE).
    private static string FormatAstLeaf(SqlNode node, List<FToken> stmt) => node switch
    {
        InsertStatement ins => FormatInsertFamily(stmt, 2, ins.SourceQuery, ins.Subqueries),
        UpdateOrInsertStatement uoi => FormatInsertFamily(stmt, 4, null, uoi.Subqueries),
        UpdateStatement u => Emit(stmt, u.Subqueries),
        DeleteStatement d => Emit(stmt, d.Subqueries),
        MergeStatement m => Emit(stmt, m.Children),
        SelectStatement s => FormatSelectLeaf(stmt, s.Query),
        IfStatement f => Emit(stmt, f.ConditionExpressions),      // "if (cond) then" — splice CASE/subquery
        WhileStatement w => Emit(stmt, w.ConditionExpressions),   // "while (cond) do"
        PsqlLeafStatement leaf => Emit(stmt, leaf.Children),      // assignment / RETURN — splice CASE/subquery
        _ => FormatLeafStatementTokens(stmt),
    };

    // A PSQL SELECT … [INTO :vars] leaf: the query part is laid out by the AST-walking core (so a nested
    // subquery in it indents), the INTO clause stays on its own line, a trailing ';' is glued.
    private static string FormatSelectLeaf(List<FToken> stmt, QueryNode? query)
    {
        if (query is null) return FormatLeafStatementTokens(stmt);
        int into = FindTopLevelWord(stmt, "INTO");
        // The PSQL block structurer already emitted this leaf's LEADING comment (it precedes the leaf's
        // first token in the flattened body). EmitQuery would re-materialise it from the query's
        // first-token trivia — a duplicate that trips the §0 lexeme net and reverts the whole statement to
        // verbatim. Strip exactly those leading comments from the head (the structurer's copy remains, so
        // nothing is lost or duplicated). Only this SELECT-leaf path is affected: the DML leaves render
        // from the comment-free token list, and an INTERNAL subquery comment is emitted once (its enclosing
        // Emit skips the spliced node's tokens).
        string head = StripLeadingLeafComments(EmitQuery(query), query.Tokens);
        if (into > 0) return head + "\n" + Emit(stmt.GetRange(into, stmt.Count - into));
        // No INTO — glue any trailing tokens after the query (the terminating ';').
        int qEnd = stmt.Count;
        for (int k = 0; k < stmt.Count; k++) { if (stmt[k].Start >= query.End) { qEnd = k; break; } }
        return qEnd >= stmt.Count ? head : head + Emit(stmt.GetRange(qEnd, stmt.Count - qEnd));
    }

    // Removes, from the start of a rendered leaf, exactly the leading comments that live on the leaf's
    // first token's trivia (which the PSQL block structurer has already emitted separately). Matching the
    // exact comment text at the head is safe — it is the leaf's own leading trivia, not content.
    private static string StripLeadingLeafComments(string rendered, IReadOnlyList<SqlToken> tokens)
    {
        if (tokens.Count == 0) return rendered;
        foreach (var tr in tokens[0].LeadingTrivia)
        {
            if (tr.Kind is not (TriviaKind.LineComment or TriviaKind.BlockComment)) continue;
            var text = tr.Kind == TriviaKind.LineComment ? tr.Text.TrimEnd() : tr.Text;
            var trimmed = rendered.TrimStart('\n', ' ');
            if (trimmed.StartsWith(text, StringComparison.Ordinal))
                rendered = trimmed.Substring(text.Length);
        }
        return rendered.TrimStart('\n', ' ');
    }

    // The token-only leaf layout — the pre-convergence behaviour, kept for a leaf the parser did not model
    // (PACKAGE body / malformed input). SELECT … INTO :vars splits the INTO onto its own line.
    private static string FormatLeafStatementTokens(List<FToken> stmt)
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
