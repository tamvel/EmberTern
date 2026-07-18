using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// The error-tolerant Firebird SQL/PSQL parser — Etap 2 of the editor rebuild. It turns the
/// lossless token stream from <see cref="SqlLexer"/> into a <see cref="SqlScript"/>: an ordered
/// list of top-level statements, each classified into its own <see cref="SqlStatement"/> node.
/// <para>
/// <b>Error tolerance (§4.2 #1):</b> the parser never throws and never returns null. Every byte of
/// the input lands in exactly one statement; a statement whose leading keyword it does not
/// recognise becomes a <see cref="RawStatement"/> (verbatim — the §0 safety valve), not an error.
/// </para>
/// <para>
/// <b>Depth (Etap 2, "statement skeleton"):</b> statements are classified but their interiors are
/// kept verbatim in <see cref="SqlStatement.Tokens"/>. Clause / expression / PSQL-body structure
/// is added in later etaps. This keeps the §0 round-trip guaranteed by the token stream, never by
/// grammar completeness.
/// </para>
/// <para>
/// <b>Single source of truth for statement boundaries.</b> The segmentation here is the one
/// authority for "what is a statement" — the DDL executor's splitter rides it (via
/// <see cref="SqlStatementSplitter"/>) rather than carrying its own scanner. The boundary rules
/// mirror the long-standing PSQL-aware splitter exactly (gotchas #55/#117/#128/#140/#152): a
/// plain statement ends at the next top-level <c>;</c> (BEGIN/CASE/END-depth and string/comment
/// aware — strings and comments are already opaque as tokens/trivia); a <c>CREATE/ALTER/RECREATE</c>
/// of a <c>PROCEDURE/TRIGGER/FUNCTION/PACKAGE</c> is kept whole from its header <c>AS</c> through
/// the <c>END</c> that closes the outermost <c>BEGIN</c>, so its DECLARE-section semicolons never
/// split it.
/// </para>
/// <para>Pure — no Avalonia, no Firebird driver — and offline unit-testable.</para>
/// <para>
/// <b>Extension point — Etap 6.9 (Structural AST Deepening).</b> The type is <c>partial</c> so the
/// deeper, construct-specific sub-parsers land in their own files as they are built — the PSQL body
/// tree in <c>SqlParser.Psql.cs</c> (milestone B1) and the query/clause tree in
/// <c>SqlParser.Query.cs</c> (B2/B3) — without touching this segmentation core. Deepening only ever
/// <em>adds</em> child nodes to the statements produced here (via <see cref="QueryNode"/> /
/// <see cref="PsqlStatement"/>); it never changes the statement boundaries this file owns, so the §0
/// round-trip and the DDL-splitter that rides these boundaries are unaffected. See
/// <c>docs/design/editor-ast-deepening.md</c>.
/// </para>
/// </summary>
public static partial class SqlParser
{
    private static readonly IReadOnlyList<Diagnostic> NoDiagnostics = Array.Empty<Diagnostic>();

    /// <summary>Parses <paramref name="text"/> into a <see cref="SqlScript"/>. Never throws.</summary>
    public static ParseResult Parse(string text) => Parse(text, lenient: false);

    /// <summary>Parses <paramref name="text"/> into a <see cref="SqlScript"/>. Never throws.</summary>
    /// <param name="lenient">When <c>true</c>, a plain statement ALSO ends at a top-level statement-start
    /// keyword (<c>SELECT/INSERT/UPDATE/DELETE/MERGE/EXECUTE/CREATE/…</c>) even without a separating
    /// <c>;</c> — so an editor script whose statements are only newline-separated is analysed
    /// per-statement instead of collapsing into one. This is for the <b>read-only semantic model
    /// only</b> (a mis-split degrades IntelliSense, never data — §0): the strict, <c>;</c>-only
    /// segmentation the DDL/script executors ride (<see cref="SqlStatementSplitter"/>, gotcha #192)
    /// is <see cref="Parse(string)"/> and is left byte-identical.</param>
    public static ParseResult Parse(string text, bool lenient)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        var tokens = SqlLexer.Tokenize(text);

        // Significant tokens only (trivia is attached; the trailing EndOfFile token is the sentinel).
        var sig = new List<SqlToken>(tokens.Count);
        foreach (var t in tokens)
        {
            if (t.Kind != TokenKind.EndOfFile) sig.Add(t);
        }

        var statements = new List<SqlStatement>();
        int n = text.Length;
        int idx = 0;
        while (idx < sig.Count)
        {
            int startIdx = idx;
            int startChar = sig[startIdx].Start;

            (int endIdxExcl, int endChar) = IsPsqlDefinitionStart(sig, startIdx)
                ? ScanPsql(sig, startIdx, n)
                : lenient ? ScanPlainLenient(sig, startIdx, n) : ScanPlain(sig, startIdx, n);

            var slice = sig.GetRange(startIdx, endIdxExcl - startIdx);
            statements.Add(Classify(slice, startChar, endChar - startChar));
            idx = endIdxExcl;
        }

        return new ParseResult(new SqlScript(text, tokens, statements), NoDiagnostics);
    }

    // ── Statement segmentation (the O5 boundary authority) ────────────────────────────────────

    // Plain statement: ends at the next top-level ';' (BEGIN/CASE/END-depth aware). Returns the
    // exclusive token index and the char offset just past the ';' — or (all-consumed, text length)
    // when it runs to the end without a terminator, matching the legacy char scanner exactly.
    private static (int EndIdxExcl, int EndChar) ScanPlain(IReadOnlyList<SqlToken> sig, int start, int n)
    {
        int i = start, depth = 0;
        while (i < sig.Count)
        {
            var t = sig[i];
            if (t.Kind == TokenKind.Semicolon && depth == 0)
            {
                return (i + 1, t.End);
            }
            if (Kw(t, "BEGIN") || Kw(t, "CASE")) depth++;
            else if (Kw(t, "END")) { if (depth > 0) depth--; }
            i++;
        }
        return (sig.Count, n);
    }

    // Lenient plain-statement scan (semantic model only): ends at the next top-level ';' OR at a
    // top-level statement-start keyword that begins a NEW statement even though no ';' separated them
    // (the "several statements, no semicolons, one editor" case). Paren + BEGIN/CASE/END depth aware,
    // so a subquery SELECT / a function-call arg / an EXECUTE BLOCK body is never mistaken for a new
    // statement. The strict ScanPlain above is left untouched (it is the executor boundary authority).
    private static (int EndIdxExcl, int EndChar) ScanPlainLenient(IReadOnlyList<SqlToken> sig, int start, int n)
    {
        int i = start, depth = 0;
        // Context for the continuation guards: whether a later top-level SELECT is THIS statement's own
        // source (INSERT … SELECT), and whether we are inside a MERGE (whose WHEN … THEN
        // INSERT/UPDATE/DELETE are continuations, not new statements).
        var lead = KeywordUpperAt(sig, start);
        bool insertAwaitingSource = lead == "INSERT";
        bool withAwaitingMain = lead == "WITH";
        bool isMerge = lead == "MERGE";
        SqlToken? prev = null;
        while (i < sig.Count)
        {
            var t = sig[i];
            if (t.Kind == TokenKind.Semicolon && depth == 0) return (i + 1, t.End);
            if (t.Kind == TokenKind.LParen) { depth++; prev = t; i++; continue; }
            if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; prev = t; i++; continue; }
            if (Kw(t, "BEGIN") || Kw(t, "CASE")) { depth++; prev = t; i++; continue; }
            if (Kw(t, "END")) { if (depth > 0) depth--; prev = t; i++; continue; }

            if (depth == 0 && i > start && t.Kind == TokenKind.Keyword
                && IsLenientStatementStart(t, prev, isMerge, ref insertAwaitingSource, ref withAwaitingMain))
            {
                return (i, t.Start); // a new top-level statement begins here — end this one before it
            }

            if (insertAwaitingSource && Kw(t, "VALUES")) insertAwaitingSource = false;
            prev = t;
            i++;
        }
        return (sig.Count, n);
    }

    // True when the depth-0 keyword <paramref name="t"/> (not the statement's own first token) begins a
    // NEW top-level statement rather than continuing the current one. Conservative — the ambiguous SET
    // (UPDATE … SET) is deliberately NOT a boundary, and the known continuations are suppressed.
    private static bool IsLenientStatementStart(
        SqlToken t, SqlToken? prev, bool isMerge, ref bool insertAwaitingSource, ref bool withAwaitingMain)
    {
        switch (t.Text.ToUpperInvariant())
        {
            case "SELECT":
            case "WITH":
                // Not a new statement when it is: the main query of a WITH … CTE list (the SELECT after
                // the last CTE's ')'), the source of an INSERT (INSERT … SELECT), the right side of a
                // set operation (UNION/EXCEPT/INTERSECT SELECT), or a CREATE VIEW … AS SELECT.
                if (withAwaitingMain) { withAwaitingMain = false; return false; }
                if (insertAwaitingSource) { insertAwaitingSource = false; return false; }
                if (IsPrevKeyword(prev, "UNION", "EXCEPT", "INTERSECT", "AS")) return false;
                return true;

            case "INSERT":
            case "UPDATE":
            case "DELETE":
                // MERGE … WHEN [NOT] MATCHED THEN INSERT/UPDATE/DELETE, and UPDATE OR INSERT, are
                // continuations of the current statement, not new statements.
                if (isMerge) return false;
                if (IsPrevKeyword(prev, "OR", "THEN", "MATCHED")) return false;
                return true;

            case "EXECUTE":
            case "MERGE":
            case "CREATE":
            case "ALTER":
            case "DROP":
            case "RECREATE":
                return true;

            default:
                return false;
        }
    }

    private static bool IsPrevKeyword(SqlToken? prev, params string[] keywords)
    {
        if (prev is null || prev.Kind != TokenKind.Keyword) return false;
        var u = prev.Text.ToUpperInvariant();
        foreach (var k in keywords)
        {
            if (u == k) return true;
        }
        return false;
    }

    // The upper-cased text of the word token at <paramref name="index"/> (keyword/identifier), else null.
    private static string? KeywordUpperAt(IReadOnlyList<SqlToken> sig, int index)
    {
        var t = At(sig, index);
        return t.Kind is TokenKind.Keyword or TokenKind.Identifier ? t.Text.ToUpperInvariant() : null;
    }

    // PSQL definition: one statement, body semicolons included. Phase 1 (before AS): skip balanced
    // parens (so an AS inside CAST(x AS y) / a param list is not the body separator) and end at a
    // top-level ';' (a bodyless UDR/EXTERNAL header). Phase 2 (after AS): track BEGIN/CASE/END depth
    // and end at the END closing the outermost BEGIN — peeking past a FB3 subprogram's END.
    private static (int EndIdxExcl, int EndChar) ScanPsql(IReadOnlyList<SqlToken> sig, int start, int n)
    {
        int i = start, depth = 0;
        bool pastAs = false, bodyOpened = false;
        while (i < sig.Count)
        {
            var t = sig[i];

            if (!pastAs)
            {
                if (t.Kind == TokenKind.LParen) { i = SkipParens(sig, i); continue; }
                if (Kw(t, "AS")) { pastAs = true; i++; continue; }
                if (t.Kind == TokenKind.Semicolon) return (i + 1, t.End); // header, no PSQL body
                i++;
                continue;
            }

            if (Kw(t, "BEGIN")) { depth++; bodyOpened = true; i++; continue; }
            if (Kw(t, "CASE")) { if (depth > 0) depth++; i++; continue; }
            if (Kw(t, "END"))
            {
                i++; // past END
                if (depth > 0)
                {
                    depth--;
                    if (depth == 0 && bodyOpened)
                    {
                        // A subprogram's END (more DECLAREs / the main BEGIN follow) → keep scanning.
                        if (i < sig.Count && (Kw(sig[i], "BEGIN") || Kw(sig[i], "DECLARE"))) continue;
                        if (i < sig.Count && sig[i].Kind == TokenKind.Semicolon) return (i + 1, sig[i].End);
                        return (i, sig[i - 1].End); // main body closed, no ';': end right after END
                    }
                }
                continue;
            }
            i++;
        }
        return (sig.Count, n);
    }

    // With the cursor on '(', returns the token index just past the matching ')' (nesting-aware;
    // strings/comments are already opaque tokens/trivia).
    private static int SkipParens(IReadOnlyList<SqlToken> sig, int i)
    {
        int depth = 0;
        while (i < sig.Count)
        {
            var kind = sig[i].Kind;
            if (kind == TokenKind.LParen) { depth++; i++; continue; }
            if (kind == TokenKind.RParen) { depth--; i++; if (depth == 0) return i; continue; }
            i++;
        }
        return i;
    }

    // CREATE [OR ALTER] | ALTER | RECREATE  +  PROCEDURE | TRIGGER | FUNCTION | PACKAGE.
    // (ALTER TABLE / CREATE VIEW … AS SELECT / CREATE GENERATOR etc. are NOT PSQL definitions.)
    private static bool IsPsqlDefinitionStart(IReadOnlyList<SqlToken> sig, int start)
    {
        int j = start;
        if (Kw(At(sig, j), "CREATE"))
        {
            j++;
            if (Kw(At(sig, j), "OR"))
            {
                j++;
                if (!Kw(At(sig, j), "ALTER")) return false;
                j++;
            }
        }
        else if (Kw(At(sig, j), "RECREATE")) j++;
        else if (Kw(At(sig, j), "ALTER")) j++;
        else return false;

        var t = At(sig, j);
        return Kw(t, "PROCEDURE") || Kw(t, "TRIGGER") || Kw(t, "FUNCTION") || Kw(t, "PACKAGE");
    }

    // ── Classification into the typed statement nodes ─────────────────────────────────────────

    private static SqlStatement Classify(IReadOnlyList<SqlToken> slice, int start, int length)
    {
        if (slice.Count == 0)
        {
            return new RawStatement(start, length, slice); // defensive — a scan always consumes ≥1 token
        }

        var first = slice[0];
        if (first.Kind == TokenKind.Semicolon)
        {
            return new EmptyStatement(start, length, slice);
        }

        string? word = first.Kind is TokenKind.Keyword or TokenKind.Identifier ? first.Text : null;
        if (word is null)
        {
            return new RawStatement(start, length, slice);
        }

        switch (word.ToUpperInvariant())
        {
            case "SELECT":
            case "WITH":
                // The statement's query is modelled as a single recursive QueryNode (Etap 6.9 / B2 + B3):
                // a SelectQuery / SetOperationQuery for a plain query, or a WithQuery (CTE bodies + main
                // query all real QueryNodes) for a WITH-led query. Best-effort — an unrecognised shape
                // yields null and the statement is treated as a plain query (§0-safe; the tokens are
                // untouched, so the byte-for-byte round-trip is unaffected).
                return new SelectStatement(start, length, slice, ParseSelectStatementQuery(slice));
            case "INSERT":
                // The source query (INSERT … SELECT/WITH) and any embedded value/RETURNING subqueries are
                // modelled as real QueryNodes (Etap 6.9 / B3.1); §0-safe additive overlay over slice.
                return BuildInsert(slice, start, length);
            case "UPDATE":
                return IsUpdateOrInsert(slice)
                    ? BuildUpdateOrInsert(slice, start, length)
                    : BuildUpdate(slice, start, length);
            case "DELETE":
                return BuildDelete(slice, start, length);
            case "MERGE":
                return BuildMerge(slice, start, length);
            case "EXECUTE":
                return ClassifyExecute(slice, start, length);
            case "CREATE":
            case "ALTER":
            case "RECREATE":
            case "DROP":
                return BuildDdl(slice, start, length);
            case "COMMENT":
                return new CommentStatement(start, length, slice);
            case "SET":
                return new SetStatement(start, length, slice, WordValueAt(slice, 1));
            case "GRANT":
                return new GrantStatement(start, length, slice);
            case "REVOKE":
                return new RevokeStatement(start, length, slice);
            case "BEGIN":
            // A bare PSQL control-flow fragment with no enclosing BEGIN…END — an IF / WHILE / FOR loop
            // pasted or selected out of a routine body. It is valid formattable PSQL (not unparseable
            // input), so recognise it as an anonymous PSQL body: the body sub-parser handles the no-BEGIN
            // shape (a bare statement list), so it formats + binds as PSQL instead of falling to a verbatim
            // RawStatement. The statement segmenter already delimits it (top-level ';' with BEGIN/CASE/END
            // depth). §0-safe additive overlay; the token slice still round-trips.
            case "IF":
            case "WHILE":
            case "FOR":
                // A bare anonymous PSQL block (a formattable body, not unparseable input). Etap 6.9/B1:
                // the block's structure (BEGIN/END, IF/WHILE/FOR, declares, leaf spans) is parsed into a
                // BlockStatement child — an additive overlay; the token slice still round-trips (§0).
                return new AnonymousBlockStatement(start, length, slice, ParseAnonymousBlockBody(slice));
            case "DECLARE":
                // A top-level DECLARE that runs into a BEGIN is a PSQL body fragment (a DECLARE
                // section + local subprograms + main block, e.g. the body editor's text), NOT a
                // top-level DECLARE EXTERNAL FUNCTION / DECLARE FILTER (which has no BEGIN).
                return ContainsBeginKeyword(slice)
                    ? new AnonymousBlockStatement(start, length, slice, ParseAnonymousBlockBody(slice))
                    : new DeclareStatement(start, length, slice);
            default:
                return new RawStatement(start, length, slice);
        }
    }

    // True when the slice contains a BEGIN keyword token (a valid DECLARE EXTERNAL FUNCTION /
    // DECLARE FILTER never does). Used to tell a top-level declaration from a PSQL body fragment.
    private static bool ContainsBeginKeyword(IReadOnlyList<SqlToken> slice)
    {
        foreach (var t in slice)
        {
            if (Kw(t, "BEGIN")) return true;
        }
        return false;
    }

    private static bool IsUpdateOrInsert(IReadOnlyList<SqlToken> slice)
        => slice.Count >= 3 && Kw(slice[1], "OR") && Kw(slice[2], "INSERT");

    private static SqlStatement ClassifyExecute(IReadOnlyList<SqlToken> slice, int start, int length)
    {
        if (slice.Count >= 2)
        {
            if (Kw(slice[1], "BLOCK"))
                return new ExecuteBlockStatement(start, length, slice, ParseRoutineBody(slice));
            if (Kw(slice[1], "PROCEDURE"))
            {
                var (args, returning) = ReadProcedureCallParts(slice);
                return new ExecuteProcedureStatement(start, length, slice, ReadProcedureName(slice), args, returning);
            }
        }
        return new ExecuteStatementStatement(start, length, slice);
    }

    // EXECUTE PROCEDURE <name> — unquoted name upper-cased (catalog convention), quoted name kept
    // in its literal case; null when there is no readable identifier.
    private static string? ReadProcedureName(IReadOnlyList<SqlToken> slice)
    {
        if (slice.Count < 3) return null;
        var t = slice[2];
        return t.Kind switch
        {
            TokenKind.QuotedIdentifier => t.Value,
            TokenKind.Keyword or TokenKind.Identifier => t.Text.ToUpperInvariant(),
            _ => null,
        };
    }

    // EXECUTE PROCEDURE <name> [ ( ] <arg>, … [ ) ] [ RETURNING_VALUES [ ( ] <var>, … [ ) ] ] — the call's
    // positional arguments (as source spans, for a debugger step-into to evaluate in the caller frame) and
    // its RETURNING_VALUES targets (folded variable names). Additive AST overlay (Stage X / D8); the slice's
    // tokens still round-trip (§0). Both sections tolerate the optional surrounding parens Firebird allows.
    private static (IReadOnlyList<CallArgument> Args, IReadOnlyList<string> Returning) ReadProcedureCallParts(
        IReadOnlyList<SqlToken> slice)
    {
        int hi = slice.Count;
        while (hi > 0 && slice[hi - 1].Kind == TokenKind.Semicolon) hi--; // ';' is a terminator, not a part

        // Index just past the (possibly dotted, e.g. PACKAGE.PROC) procedure name.
        int i = 3; // slice[0]=EXECUTE, slice[1]=PROCEDURE, slice[2]=name
        while (i < hi && At(slice, i).Kind == TokenKind.Dot) { i++; if (i < hi) i++; }
        if (i >= hi) return (Array.Empty<CallArgument>(), Array.Empty<string>());

        int rv = FindReturningValuesTop(slice, i, hi);
        var args = ReadCallArgumentList(slice, i, rv);
        var returning = rv < hi
            ? ReadCallReturningTargets(slice, rv + 1, hi)
            : (IReadOnlyList<string>)Array.Empty<string>();
        return (args, returning);
    }

    // The index of the top-level (paren-depth 0) RETURNING_VALUES keyword, or hi. RETURNING_VALUES is not a
    // catalogued keyword — it lexes as an identifier — so it is matched by text (mirrors the binder).
    private static int FindReturningValuesTop(IReadOnlyList<SqlToken> t, int from, int hi)
    {
        int depth = 0;
        for (int i = from; i < hi; i++)
        {
            var kind = t[i].Kind;
            if (kind == TokenKind.LParen) depth++;
            else if (kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && IsWordText(t[i], "RETURNING_VALUES")) return i;
        }
        return hi;
    }

    // Splits the argument range [lo, hi) into per-argument source spans at paren-depth-0 commas, stripping a
    // single fully-enclosing paren pair first (EXECUTE PROCEDURE P (a, b) vs. EXECUTE PROCEDURE P a, b).
    private static IReadOnlyList<CallArgument> ReadCallArgumentList(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        if (lo >= hi) return Array.Empty<CallArgument>();
        if (t[lo].Kind == TokenKind.LParen)
        {
            int close = MatchParenTok(t, lo, hi);
            if (close == hi - 1) { lo++; hi = close; } // the parens enclose the whole list — strip them
        }

        var args = new List<CallArgument>();
        int depth = 0, segStart = lo;
        for (int k = lo; k <= hi; k++)
        {
            bool atEnd = k == hi;
            if (!atEnd)
            {
                var kind = t[k].Kind;
                if (kind == TokenKind.LParen) { depth++; continue; }
                if (kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
                if (!(depth == 0 && kind == TokenKind.Comma)) continue;
            }
            if (k > segStart)
            {
                int s = t[segStart].Start;
                args.Add(new CallArgument(s, t[k - 1].End - s));
            }
            segStart = k + 1;
        }
        return args.Count == 0 ? Array.Empty<CallArgument>() : args;
    }

    // Splits the RETURNING_VALUES target range [lo, hi) at paren-depth-0 commas and reads each segment's
    // folded variable name (bare / :name / @name / quoted). Reuses the one INTO-target reader (ForTargetName).
    private static IReadOnlyList<string> ReadCallReturningTargets(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        if (lo >= hi) return Array.Empty<string>();
        if (t[lo].Kind == TokenKind.LParen)
        {
            int close = MatchParenTok(t, lo, hi);
            if (close == hi - 1) { lo++; hi = close; }
        }

        var names = new List<string>();
        int depth = 0, segStart = lo;
        for (int k = lo; k <= hi; k++)
        {
            bool atEnd = k == hi;
            if (!atEnd)
            {
                var kind = t[k].Kind;
                if (kind == TokenKind.LParen) { depth++; continue; }
                if (kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
                if (!(depth == 0 && kind == TokenKind.Comma)) continue;
            }
            var name = ForTargetName(t, segStart, k);
            if (name is not null) names.Add(name);
            segStart = k + 1;
        }
        return names.Count == 0 ? Array.Empty<string>() : names;
    }

    private static DdlStatement BuildDdl(IReadOnlyList<SqlToken> slice, int start, int length)
    {
        // Verb + the index just past the verb phrase.
        DdlVerb verb;
        int afterVerb;
        if (Kw(slice[0], "CREATE"))
        {
            if (slice.Count >= 3 && Kw(slice[1], "OR") && Kw(slice[2], "ALTER"))
            {
                verb = DdlVerb.CreateOrAlter;
                afterVerb = 3;
            }
            else
            {
                verb = DdlVerb.Create;
                afterVerb = 1;
            }
        }
        else if (Kw(slice[0], "RECREATE")) { verb = DdlVerb.Recreate; afterVerb = 1; }
        else if (Kw(slice[0], "DROP")) { verb = DdlVerb.Drop; afterVerb = 1; }
        else { verb = DdlVerb.Alter; afterVerb = 1; }

        bool isPsql = IsPsqlDefinitionStart(slice, 0);

        // Best-effort object kind + name: skip modifier keywords, read the object keyword, then the
        // next identifier (skipping IF [NOT] EXISTS). Not consumed by any Etap-2 client; a miss just
        // leaves Unknown/null — the interior stays verbatim regardless.
        int j = afterVerb;
        while (j < slice.Count && IsDdlModifier(slice[j])) j++;

        var objectKind = DdlObjectKind.Unknown;
        if (j < slice.Count)
        {
            objectKind = MapObjectKind(slice[j]);
            if (objectKind != DdlObjectKind.Unknown) j++;
        }

        while (j < slice.Count && IsExistenceGuard(slice[j])) j++;
        string? objectName = j < slice.Count ? ReadIdentifierName(slice[j]) : null;

        // Etap 6.9/B1: a PSQL procedure/function/trigger definition gets its BEGIN…END body parsed into
        // a Body tree (after the header's top-level AS). Additive — the token slice still round-trips
        // (§0). PACKAGE (a list of subprograms) and non-PSQL DDL keep a null body.
        var body = isPsql && objectKind is DdlObjectKind.Procedure or DdlObjectKind.Function or DdlObjectKind.Trigger
            ? ParseRoutineBody(slice)
            : null;

        // Etap 6.9/B3.1: a CREATE/ALTER/RECREATE VIEW … AS <query> gets its body modelled as a real
        // QueryNode (mutually exclusive with a PSQL body). Additive; the slice still round-trips (§0).
        var query = objectKind == DdlObjectKind.View && verb != DdlVerb.Drop
            ? ParseViewBodyQuery(slice)
            : null;

        return new DdlStatement(start, length, slice, verb, objectKind, objectName, isPsql, body, query);
    }

    private static bool IsDdlModifier(SqlToken t)
    {
        // Some spellings (e.g. DESCENDING) are not catalogued keywords and lex as identifiers, so
        // match by text over both word kinds — this is best-effort header sugar, never an object name.
        if (t.Kind != TokenKind.Keyword && t.Kind != TokenKind.Identifier) return false;
        return t.Text.ToUpperInvariant() switch
        {
            "UNIQUE" or "ASC" or "ASCENDING" or "DESC" or "DESCENDING"
                or "GLOBAL" or "TEMPORARY" or "EXTERNAL" => true,
            _ => false,
        };
    }

    private static bool IsExistenceGuard(SqlToken t)
    {
        if (t.Kind != TokenKind.Keyword) return false;
        var u = t.Text.ToUpperInvariant();
        return u is "IF" or "NOT" or "EXISTS";
    }

    private static DdlObjectKind MapObjectKind(SqlToken t)
    {
        if (t.Kind != TokenKind.Keyword && t.Kind != TokenKind.Identifier) return DdlObjectKind.Unknown;
        return t.Text.ToUpperInvariant() switch
        {
            "TABLE" => DdlObjectKind.Table,
            "VIEW" => DdlObjectKind.View,
            "INDEX" => DdlObjectKind.Index,
            "SEQUENCE" => DdlObjectKind.Sequence,
            "GENERATOR" => DdlObjectKind.Generator,
            "PROCEDURE" => DdlObjectKind.Procedure,
            "FUNCTION" => DdlObjectKind.Function,
            "TRIGGER" => DdlObjectKind.Trigger,
            "DOMAIN" => DdlObjectKind.Domain,
            "EXCEPTION" => DdlObjectKind.Exception,
            "ROLE" => DdlObjectKind.Role,
            "PACKAGE" => DdlObjectKind.Package,
            "COLLATION" => DdlObjectKind.Collation,
            "FILTER" => DdlObjectKind.Filter,
            _ => DdlObjectKind.Unknown,
        };
    }

    private static string? ReadIdentifierName(SqlToken t) => t.Kind switch
    {
        TokenKind.QuotedIdentifier => t.Value,
        TokenKind.Identifier => t.Text.ToUpperInvariant(),
        // A bare keyword as an object name is unusual; keep it verbatim rather than guess.
        _ => null,
    };

    // The word value of the token at <paramref name="index"/> (its text; a quoted identifier's
    // decoded name), or null when it is not a word token.
    private static string? WordValueAt(IReadOnlyList<SqlToken> slice, int index)
    {
        if (index < 0 || index >= slice.Count) return null;
        var t = slice[index];
        return t.Kind switch
        {
            TokenKind.Keyword or TokenKind.Identifier => t.Text,
            TokenKind.QuotedIdentifier => t.Value,
            _ => null,
        };
    }

    // ── Token predicates ──────────────────────────────────────────────────────────────────────

    private static readonly SqlToken NoToken =
        new(TokenKind.EndOfFile, 0, 0, string.Empty, Array.Empty<SqlTrivia>());

    private static SqlToken At(IReadOnlyList<SqlToken> sig, int index)
        => index >= 0 && index < sig.Count ? sig[index] : NoToken;

    // A keyword token whose text equals <paramref name="keyword"/> (case-insensitive). Only
    // unquoted, catalogued keywords lex as TokenKind.Keyword, so a quoted "BEGIN" (a
    // QuotedIdentifier) never matches — mirroring the legacy scanner, which skipped quoted runs
    // before its keyword check.
    private static bool Kw(SqlToken t, string keyword)
        => t.Kind == TokenKind.Keyword && string.Equals(t.Text, keyword, StringComparison.OrdinalIgnoreCase);

    // A word token (keyword OR identifier) whose text matches — for words that may lex either way
    // (RECURSIVE is not always catalogued).
    private static bool IsWordText(SqlToken t, string text)
        => (t.Kind == TokenKind.Keyword || t.Kind == TokenKind.Identifier)
           && string.Equals(t.Text, text, StringComparison.OrdinalIgnoreCase);

    // The index of the ')' matching the '(' at <paramref name="open"/> (nesting-aware), or
    // <paramref name="hi"/> when unbalanced.
    private static int MatchParenTok(IReadOnlyList<SqlToken> t, int open, int hi)
    {
        int depth = 0;
        for (int k = open; k < hi; k++)
        {
            if (t[k].Kind == TokenKind.LParen) depth++;
            else if (t[k].Kind == TokenKind.RParen) { if (--depth == 0) return k; }
        }
        return hi;
    }

    private static List<SqlToken> Sub(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        var list = new List<SqlToken>(hi > lo ? hi - lo : 0);
        for (int k = lo; k < hi && k < t.Count; k++) list.Add(t[k]);
        return list;
    }

    // The absolute span [firstToken.Start, lastToken.End) of the token range [lo, hi) — the one helper
    // every deepened sub-parser (PSQL body, query/clause tree) uses so a node's span is always the exact
    // range it consumed (children therefore nest + stay in source order by construction). Empty range →
    // a zero-length span at the range's start.
    private static (int Start, int Length) TokenSpan(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        if (lo >= hi || lo >= t.Count) return (lo >= 0 && lo < t.Count ? t[lo].Start : 0, 0);
        int start = t[lo].Start;
        int end = t[hi - 1].End;
        return (start, end - start);
    }
}
