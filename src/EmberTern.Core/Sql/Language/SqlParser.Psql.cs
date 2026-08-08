using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language;

// Etap 6.9 / B1 — the PSQL body sub-parser. Turns the significant-token slice of an anonymous PSQL
// block (BEGIN … END, the shape the procedure/function/trigger BODY editors hold — gotcha #114) into a
// BlockStatement tree: blocks, IF/WHILE/FOR control flow, DECLARE variable/cursor declarations, and
// executable-leaf spans. This is an ADDITIVE structural overlay — the owning AnonymousBlockStatement
// keeps its full token slice, so §0 round-trip is untouched (SqlScript.Tokens reproduces the source).
//
// It deliberately MIRRORS the formatter's EmitPsqlUnit recognition (BEGIN/IF/WHILE/FOR/leaf, first-token
// THEN/DO scan, collect-to-';' leaf) so the tree matches established behaviour. Two invariants hold BY
// CONSTRUCTION, independent of grammar-recognition fidelity: every node's span is [firstToken.Start,
// lastConsumedToken.End) of the exact range it consumed — so child spans always nest and appear in
// source order — and no token is ever dropped (round-trip is token-based). Anything not recognised as a
// compound construct becomes a PsqlLeafStatement (the PSQL-level §0 valve). Never throws.
//
// Scope: wired into AnonymousBlockStatement, EXECUTE BLOCK, and CREATE PROCEDURE/FUNCTION/TRIGGER
// definition bodies (the latter reuse this parser after skipping their header to the body). A local
// DECLARE PROCEDURE/FUNCTION sub-routine is now a real SubroutineDeclaration node in
// BlockStatement.LocalRoutines, carrying its own body block (Stage X / D9 — so the debugger interprets it
// as a nested frame instead of stepping through its body as the enclosing routine's main flow). A PACKAGE
// body's subprogram list and the deep DML/query interior of leaves are separate milestones; until then
// they ride the leaf valve, losslessly.
public static partial class SqlParser
{
    /// <summary>Parses an anonymous PSQL block's significant-token slice (the <c>BEGIN … END</c> shape a
    /// body editor holds) into a <see cref="BlockStatement"/> tree. Never throws.</summary>
    internal static BlockStatement ParseAnonymousBlockBody(IReadOnlyList<SqlToken> sig)
    {
        int i = 0;
        return ParsePsqlBlockBody(sig, ref i, isTopLevel: true);
    }

    /// <summary>Parses the PSQL body of a routine / <c>EXECUTE BLOCK</c> definition: skips the header to
    /// the top-level <c>AS</c>, then parses the declaration section + <c>BEGIN … END</c> after it. Returns
    /// the body <see cref="BlockStatement"/> (spanning from just after <c>AS</c>). Never throws; if there
    /// is no top-level <c>AS</c> (mid-edit) the whole slice is parsed leniently.</summary>
    internal static BlockStatement ParseRoutineBody(IReadOnlyList<SqlToken> sig)
    {
        int asIdx = FindTopLevelAsIndex(sig);
        int i = asIdx >= 0 ? asIdx + 1 : 0;
        return ParsePsqlBlockBody(sig, ref i, isTopLevel: true);
    }

    // A block body: an optional DECLARE section (top level only — a routine / EXECUTE BLOCK body's
    // declarations precede its BEGIN), then a BEGIN … END whose inner units become the block's statements.
    // At the top level, anything before/after is folded in leniently so no token is orphaned. A nested
    // call (from ParsePsqlUnit) is entered only on a BEGIN and carries no declarations.
    private static BlockStatement ParsePsqlBlockBody(
        IReadOnlyList<SqlToken> sig, ref int i, bool isTopLevel, int nodeStart = -1)
    {
        int lo = nodeStart >= 0 ? nodeStart : i;
        var declarations = new List<PsqlStatement>();
        var localRoutines = new List<SubroutineDeclaration>();
        if (isTopLevel)
        {
            ParseDeclarationSection(sig, ref i, declarations, localRoutines);
        }

        var statements = new List<SqlNode>();
        var handlers = new List<WhenHandler>();
        if (i < sig.Count && IsBodyWord(sig[i], "BEGIN"))
        {
            i++; // consume BEGIN
            while (i < sig.Count && !IsBodyWord(sig[i], "END"))
            {
                int before = i;
                ParseBodyUnit(sig, ref i, statements, handlers);
                if (i == before) i++; // anti-stall (defensive; ParseBodyUnit always advances)
            }
            if (i < sig.Count && IsBodyWord(sig[i], "END"))
            {
                i++; // consume END
                if (i < sig.Count && sig[i].Kind == TokenKind.Semicolon) i++; // optional terminator
            }
            if (isTopLevel)
            {
                // Fold any stray trailing tokens after END (rare / mid-edit) rather than orphan them.
                while (i < sig.Count)
                {
                    int before = i;
                    ParseBodyUnit(sig, ref i, statements, handlers);
                    if (i == before) i++;
                }
            }
        }
        else if (isTopLevel)
        {
            // No BEGIN (mid-edit / body-only fragment) — parse whatever there is as statements/handlers.
            while (i < sig.Count)
            {
                int before = i;
                ParseBodyUnit(sig, ref i, statements, handlers);
                if (i == before) i++;
            }
        }

        var (start, length) = TokenSpan(sig, lo, i);
        return new BlockStatement(start, length, Sub(sig, lo, i), declarations, statements, handlers, localRoutines);
    }

    // The declaration section preceding a routine / EXECUTE BLOCK / sub-routine body's outermost BEGIN:
    // DECLARE VARIABLE / DECLARE CURSOR declarations and DECLARE PROCEDURE/FUNCTION local sub-routines
    // (Stage X / D9), in source order — Firebird permits either order. Consumes every leading DECLARE and
    // stops at the first non-DECLARE token (the BEGIN, or end of input). Each parse advances i, so no stall.
    private static void ParseDeclarationSection(
        IReadOnlyList<SqlToken> sig, ref int i,
        List<PsqlStatement> declarations, List<SubroutineDeclaration> localRoutines)
    {
        while (i < sig.Count && IsBodyWord(sig[i], "DECLARE"))
        {
            if (IsLocalRoutineStart(sig, i)) localRoutines.Add(ParseSubroutineDeclaration(sig, ref i));
            else declarations.Add(ParsePsqlDeclaration(sig, ref i));
        }
    }

    // A local sub-routine's body: its own DECLARE section (local variables — and, defensively for
    // losslessness, any nested sub-routine, which Firebird actually rejects) then a single BEGIN … END.
    // Parsed BLOCK-SCOPED — it stops at its own matching END and is NOT lenient, so it never swallows the
    // enclosing routine's main BEGIN (the way the isTopLevel path folds trailing tokens would). Never throws.
    private static BlockStatement ParseScopedBlockBody(IReadOnlyList<SqlToken> sig, ref int i)
    {
        int lo = i;
        var declarations = new List<PsqlStatement>();
        var localRoutines = new List<SubroutineDeclaration>();
        ParseDeclarationSection(sig, ref i, declarations, localRoutines);

        var statements = new List<SqlNode>();
        var handlers = new List<WhenHandler>();
        if (i < sig.Count && IsBodyWord(sig[i], "BEGIN"))
        {
            i++; // consume BEGIN
            while (i < sig.Count && !IsBodyWord(sig[i], "END"))
            {
                int before = i;
                ParseBodyUnit(sig, ref i, statements, handlers);
                if (i == before) i++;
            }
            if (i < sig.Count && IsBodyWord(sig[i], "END"))
            {
                i++; // consume END
                if (i < sig.Count && sig[i].Kind == TokenKind.Semicolon) i++; // optional terminator
            }
        }
        var (start, length) = TokenSpan(sig, lo, i);
        return new BlockStatement(start, length, Sub(sig, lo, i), declarations, statements, handlers, localRoutines);
    }

    // A local DECLARE PROCEDURE/FUNCTION name (…) [RETURNS …] AS <body> sub-routine (Stage X / D9, the
    // flagship). Its span covers the WHOLE sub-routine (header + body) like every other compound node — the
    // Body child nests inside. The header ends at the first depth-0 AS (⇒ a body follows) or the first
    // depth-0 ';' (⇒ a forward declaration, no body); parens shield a parameter default's own AS/';'. The
    // body (declarations + BEGIN … END) is parsed block-scoped (ParseScopedBlockBody), so a sub-routine's
    // own local variable declarations — which each end in ';' — are NOT mistaken for the header's forward-
    // declaration terminator. Never throws; always advances i.
    private static SubroutineDeclaration ParseSubroutineDeclaration(IReadOnlyList<SqlToken> sig, ref int i)
    {
        int lo = i; // at DECLARE (a local sub-routine, D9) OR at PROCEDURE/FUNCTION (a package body member, D11)
        bool hasDeclare = IsBodyWord(sig[i], "DECLARE");
        int kindIdx = hasDeclare ? i + 1 : i; // the PROCEDURE|FUNCTION keyword
        var kind = kindIdx < sig.Count && IsBodyWord(sig[kindIdx], "FUNCTION")
            ? SubroutineKind.Function : SubroutineKind.Procedure;
        string? name = PsqlNameAt(sig, kindIdx + 1); // [DECLARE] PROCEDURE|FUNCTION <name>

        int depth = 0, asIdx = -1, semiIdx = -1;
        int k = kindIdx;
        for (; k < sig.Count; k++)
        {
            var t = sig[k];
            if (t.Kind == TokenKind.LParen) { depth++; continue; }
            if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (IsBodyWord(t, "AS")) { asIdx = k; break; }
            if (t.Kind == TokenKind.Semicolon) { semiIdx = k; break; } // forward declaration
        }

        BlockStatement? body = null;
        if (asIdx >= 0)
        {
            i = asIdx + 1; // consume through AS; the body (declarations + BEGIN … END) follows
            body = ParseScopedBlockBody(sig, ref i);
        }
        else if (semiIdx >= 0)
        {
            i = semiIdx + 1; // forward declaration — consume through its ';'
        }
        else
        {
            i = sig.Count; // mid-edit: no AS and no ';' — fold the rest into the header, losslessly
        }
        if (i == lo) i++; // never stall (defensive)

        var (start, length) = TokenSpan(sig, lo, i);
        return new SubroutineDeclaration(start, length, Sub(sig, lo, i), kind, name, body);
    }

    // Parses one body unit at i and routes it: a recognised WHEN … DO exception handler goes to
    // <paramref name="handlers"/>; everything else — a statement, or a malformed/unrecognised WHEN that
    // fell back to the §0 Other leaf — goes to <paramref name="statements"/>. Advances i by ≥1.
    private static void ParseBodyUnit(
        IReadOnlyList<SqlToken> sig, ref int i, List<SqlNode> statements, List<WhenHandler> handlers)
    {
        if (IsBodyWord(sig[i], "WHEN"))
        {
            var node = ParseWhenHandler(sig, ref i);
            if (node is WhenHandler wh) handlers.Add(wh);
            else statements.Add(node); // malformed / unrecognised WHEN → lossless Other leaf
            return;
        }
        statements.Add(ParsePsqlUnit(sig, ref i));
    }

    // One PSQL statement (a compound: BEGIN/IF/WHILE/FOR; an embedded DSQL statement reused node; or a
    // PSQL-only leaf). Advances i by ≥1 token. Returns SqlNode because an embedded DSQL leaf resolves to a
    // reused top-level statement node (SqlStatement), not a PsqlStatement (B5).
    private static SqlNode ParsePsqlUnit(IReadOnlyList<SqlToken> sig, ref int i)
        => ParsePsqlUnit(sig, ref i, nodeStart: -1);

    // ⭐⭐ <paramref name="nodeStart"/> exists for STATEMENT PREFIXES — a loop LABEL (`retry: while …`) and
    // the autonomous-transaction wrapper (`IN AUTONOMOUS TRANSACTION DO …`). Both precede an ordinary
    // statement without changing WHAT statement it is, so the prefix is consumed here and its start index is
    // handed to the chosen parser, which then covers the prefix in the node's own span and tokens.
    //
    // ⚠ Keeping the prefix inside the node is not cosmetic. The binder's contract is that every body token
    // belongs to exactly ONE node; returning a node that starts after the prefix would orphan those tokens
    // and (through PsqlLeafStatement's §0 valve) is exactly the shape that lost them before.
    //
    // ⛔ THE BUG THIS FIXES IS STRUCTURAL, NOT COSMETIC, and it is gotcha #301's shape one construct further
    // along. Dispatching on the first token alone meant a prefixed statement fell through to ParsePsqlLeaf,
    // which ends at the FIRST semicolon — so `retry: while (i < 10) do begin i = i + 1; leave retry; end`
    // was cut in half at the assignment's `;`. The leaf then contained a top-level `=`, so ClassifyLeaf
    // called it an Assignment, which is a position where an unresolved bare name IS flagged: the label was
    // reported as an unknown variable (ET0003) and the loop body was never modelled at all.
    private static SqlNode ParsePsqlUnit(IReadOnlyList<SqlToken> sig, ref int i, int nodeStart)
    {
        int prefixed = nodeStart;
        if (prefixed < 0 && TryConsumeStatementPrefix(sig, ref i, out int prefixStart))
        {
            prefixed = prefixStart;
            if (i >= sig.Count) return ParsePsqlLeaf(sig, ref i, prefixed); // prefix with nothing after it
        }

        var t = sig[i];
        if (t.Kind is TokenKind.Keyword or TokenKind.Identifier)
        {
            var up = t.Text.ToUpperInvariant();
            switch (up)
            {
                case "BEGIN": return ParsePsqlBlockBody(sig, ref i, isTopLevel: false, prefixed);
                case "IF": return ParsePsqlIf(sig, ref i, prefixed);
                case "WHILE": return ParsePsqlWhile(sig, ref i, prefixed);
                case "FOR": return ParsePsqlFor(sig, ref i, prefixed);
            }

            // A local sub-routine (DECLARE PROCEDURE/FUNCTION …) appearing at a statement position — valid
            // Firebird declares these only in the pre-BEGIN section (ParseDeclarationSection consumes them
            // there), so reaching one here is mid-edit or malformed; model it uniformly as a
            // SubroutineDeclaration (header + body) so a stray one is never split into an orphan header + a
            // bare body sibling. Lands in the block's Statements (SubroutineDeclaration is a PsqlStatement).
            if (up == "DECLARE" && i + 1 < sig.Count
                && (IsBodyWord(sig[i + 1], "PROCEDURE") || IsBodyWord(sig[i + 1], "FUNCTION")))
            {
                return ParseSubroutineDeclaration(sig, ref i);
            }
        }

        return ParsePsqlLeaf(sig, ref i, prefixed);
    }

    // Consumes a statement PREFIX at i, reporting where it began. Two forms, both defined by the Language
    // Reference as decorating a following statement:
    //   • `<label> :`                      — a loop/block label (Firebird 2.5+), targeted by LEAVE <label>.
    //   • `IN AUTONOMOUS TRANSACTION DO`   — runs the following statement in its own transaction.
    // False (and i untouched) when there is no prefix, which is the overwhelmingly common case.
    //
    // ⚠⚠ BOTH FORMS REQUIRE A COMPOUND STATEMENT TO FOLLOW, and that is a deliberate narrowing rather than
    // an omission. The defect being fixed is a leaf swallowing a semicolon that belongs to a nested
    // statement, which can only happen when the following statement HAS nested statements. A prefixed
    // single statement (`IN AUTONOMOUS TRANSACTION DO INSERT …;`) already ends at its own `;`, so the leaf
    // covers it exactly — and leaving that case alone avoids the one shape this mechanism cannot express:
    // an embedded DSQL statement is re-classified from its own first token (Classify), so a node covering
    // the prefix would have to disagree with the tokens it was classified from.
    private static bool TryConsumeStatementPrefix(IReadOnlyList<SqlToken> sig, ref int i, out int prefixStart)
    {
        prefixStart = i;
        int len = FirebirdGrammar.StatementPrefixLength(sig, i);
        if (len == 0) return false;
        i += len;
        return true;
    }

    // ⚠ `kw` (the IF keyword) and `lo` (where the NODE starts) are separate because a statement prefix may
    // precede the keyword: every scan is relative to the keyword, every span to the node. The same split
    // applies to ParsePsqlWhile / ParsePsqlFor below.
    private static SqlNode ParsePsqlIf(IReadOnlyList<SqlToken> sig, ref int i, int nodeStart = -1)
    {
        int kw = i;
        int lo = nodeStart >= 0 ? nodeStart : kw;
        int thenIdx = FindBodyWord(sig, kw + 1, "THEN");
        if (thenIdx < 0) return ParsePsqlLeaf(sig, ref i, lo); // malformed IF — lossless leaf
        var conditions = ParseEmbeddedExpressions(sig, kw + 1, thenIdx); // subquery / CASE in the condition
        var conditionCall = TryReadConditionCall(sig, kw + 1, thenIdx); // whole-condition lone call (§6.4)
        i = thenIdx + 1;
        var thenBranch = ParsePsqlUnit(sig, ref i);
        SqlNode? elseBranch = null;
        if (i < sig.Count && IsBodyWord(sig[i], "ELSE"))
        {
            i++; // consume ELSE
            elseBranch = ParsePsqlUnit(sig, ref i);
        }
        var (start, length) = TokenSpan(sig, lo, i);
        return new IfStatement(start, length, Sub(sig, lo, i), thenBranch, elseBranch, conditions, conditionCall);
    }

    private static SqlNode ParsePsqlWhile(IReadOnlyList<SqlToken> sig, ref int i, int nodeStart = -1)
    {
        int kw = i;
        int lo = nodeStart >= 0 ? nodeStart : kw;
        int doIdx = FindBodyWord(sig, kw + 1, "DO");
        if (doIdx < 0) return ParsePsqlLeaf(sig, ref i, lo);
        var conditions = ParseEmbeddedExpressions(sig, kw + 1, doIdx); // subquery / CASE in the condition
        var conditionCall = TryReadConditionCall(sig, kw + 1, doIdx); // whole-condition lone call (§6.4)
        i = doIdx + 1;
        var body = ParsePsqlUnit(sig, ref i);
        var (start, length) = TokenSpan(sig, lo, i);
        return new WhileStatement(start, length, Sub(sig, lo, i), body, conditions, conditionCall);
    }

    // FOR <select|execute statement> [INTO <vars>] [AS CURSOR c] DO <body> — the DO is located at paren
    // depth 0 so a subquery's inner clauses never leak out (same rule the formatter uses). The cursor
    // query becomes a real QueryNode (B3.1).
    private static SqlNode ParsePsqlFor(IReadOnlyList<SqlToken> sig, ref int i, int nodeStart = -1)
    {
        int kw = i;
        int lo = nodeStart >= 0 ? nodeStart : kw;
        int depth = 0, doIdx = -1;
        for (int k = kw + 1; k < sig.Count; k++)
        {
            var t = sig[k];
            if (t.Kind == TokenKind.LParen) depth++;
            else if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && IsBodyWord(t, "DO")) { doIdx = k; break; }
        }
        if (doIdx < 0) return ParsePsqlLeaf(sig, ref i, lo);

        var cursor = ParseForCursorQuery(sig, kw + 1, doIdx); // B3.1: FOR SELECT/WITH → real QueryNode
        var (intoTargets, cursorName) = ParseForIntoAndCursor(sig, kw + 1, doIdx); // D6a
        i = doIdx + 1;
        var body = ParsePsqlUnit(sig, ref i);
        var (start, length) = TokenSpan(sig, lo, i);
        return new ForSelectStatement(start, length, Sub(sig, lo, i), body, cursor, intoTargets, cursorName);
    }

    // The [INTO <var-list>] and [AS CURSOR <name>] clauses of a FOR loop, from the range [lo, doIdx) after
    // FOR up to DO (both located at paren depth 0 so a subquery's own clauses never leak out — the same rule
    // ParseForCursorQuery uses to end the query). Order-independent: Firebird permits either INTO or AS CURSOR
    // first. INTO targets are the depth-0 comma-separated variable references up to the next depth-0 boundary
    // (the other clause, or DO); each is folded to the resolution convention so it keys into the frame values.
    // A missing clause yields (empty, null) — additive, never throws (D6a).
    private static (IReadOnlyList<string> Into, string? Cursor) ParseForIntoAndCursor(
        IReadOnlyList<SqlToken> sig, int lo, int doIdx)
    {
        int intoIdx = -1, cursorNameIdx = -1;
        int depth = 0;
        for (int k = lo; k < doIdx; k++)
        {
            var t = sig[k];
            if (t.Kind == TokenKind.LParen) { depth++; continue; }
            if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (intoIdx < 0 && IsBodyWord(t, "INTO")) intoIdx = k;
            else if (cursorNameIdx < 0 && IsBodyWord(t, "AS") && k + 1 < doIdx && IsBodyWord(sig[k + 1], "CURSOR"))
                cursorNameIdx = k + 2;
        }

        string? cursor = cursorNameIdx >= 0 && cursorNameIdx < doIdx ? PsqlNameAt(sig, cursorNameIdx) : null;

        IReadOnlyList<string> into = Array.Empty<string>();
        if (intoIdx >= 0)
        {
            // The INTO list ends at the next depth-0 boundary after it (AS CURSOR, if it follows) or DO.
            int hi = doIdx;
            if (cursorNameIdx > intoIdx) hi = cursorNameIdx - 2; // back up to the AS token
            into = ParseForIntoTargets(sig, intoIdx + 1, hi);
        }
        return (into, cursor);
    }

    // Splits the INTO target range [lo, hi) at paren depth 0 by commas and reads each segment's variable name
    // (a bare identifier, a :name/@name parameter, or a quoted identifier). Folds to the resolution convention.
    private static IReadOnlyList<string> ParseForIntoTargets(IReadOnlyList<SqlToken> sig, int lo, int hi)
    {
        var names = new List<string>();
        int depth = 0, segStart = lo;
        for (int k = lo; k <= hi; k++)
        {
            bool atEnd = k == hi;
            if (!atEnd)
            {
                var t = sig[k];
                if (t.Kind == TokenKind.LParen) { depth++; continue; }
                if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
                if (!(depth == 0 && t.Kind == TokenKind.Comma)) continue;
            }
            var name = ForTargetName(sig, segStart, k);
            if (name is not null) names.Add(name);
            segStart = k + 1;
        }
        return names.Count == 0 ? Array.Empty<string>() : names;
    }

    // The variable name of one INTO target segment [lo, hi) — the first name-ish token, folded. Handles the
    // :name / @name parameter form (the colon/at is stripped) in addition to bare and quoted identifiers.
    private static string? ForTargetName(IReadOnlyList<SqlToken> sig, int lo, int hi)
    {
        for (int k = lo; k < hi; k++)
        {
            var t = sig[k];
            switch (t.Kind)
            {
                case TokenKind.QuotedIdentifier: return t.Value;
                case TokenKind.Identifier or TokenKind.Keyword: return t.Text.ToUpperInvariant();
                case TokenKind.Parameter: return t.Text.TrimStart(':', '@').ToUpperInvariant();
            }
        }
        return null;
    }

    // The cursor query of a FOR loop — the range from just after FOR to the first depth-0 INTO or
    // AS CURSOR (or DO). Only a SELECT/WITH cursor becomes a QueryNode; FOR EXECUTE STATEMENT (dynamic)
    // yields null. A column alias's own AS (at depth 0) is NOT a boundary — only AS immediately followed
    // by CURSOR is.
    private static QueryNode? ParseForCursorQuery(IReadOnlyList<SqlToken> sig, int lo, int doIdx)
    {
        if (lo >= doIdx || !(IsBodyWord(sig[lo], "SELECT") || IsBodyWord(sig[lo], "WITH"))) return null;
        int depth = 0, hi = doIdx;
        for (int k = lo; k < doIdx; k++)
        {
            var t = sig[k];
            if (t.Kind == TokenKind.LParen) { depth++; continue; }
            if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (IsBodyWord(t, "INTO")) { hi = k; break; }
            if (IsBodyWord(t, "AS") && k + 1 < doIdx && IsBodyWord(sig[k + 1], "CURSOR")) { hi = k; break; }
        }
        return ParseQueryRange(sig, lo, hi);
    }

    // ── Exception handlers (Stage X / P1, design §3.6) ───────────────────────────────────────────
    //
    // A WHEN <condition> [, <condition> …] DO <compound_statement> handler at a block's statement
    // position. A WHEN token can only begin an exception handler here: a CASE's WHEN and a MERGE's WHEN
    // live INSIDE a leaf/DSQL statement, which ParsePsqlLeaf/Classify consume whole (to their ';' / END),
    // so they never surface at a body-unit position. Returns a WhenHandler when the shape is recognised;
    // otherwise falls back to a lossless PsqlLeafStatement (Other) — the §0 valve, mirroring how
    // ParsePsqlIf falls back on a missing THEN. Never throws; always advances i.
    private static SqlNode ParseWhenHandler(IReadOnlyList<SqlToken> sig, ref int i)
    {
        int lo = i; // at WHEN
        int doIdx = FindWhenDoIndex(sig, i + 1);
        if (doIdx < 0) return ParsePsqlLeaf(sig, ref i); // no depth-0 DO before the next WHEN/END → Other

        var conditions = ParseWhenConditions(sig, lo + 1, doIdx);
        if (conditions is null) return ParsePsqlLeaf(sig, ref i); // condition list not grammar → Other

        i = doIdx + 1; // consume DO
        SqlNode? body = i < sig.Count ? ParsePsqlUnit(sig, ref i) : null; // compound statement (or mid-edit end)
        var (start, length) = TokenSpan(sig, lo, i);
        return new WhenHandler(start, length, Sub(sig, lo, i), conditions, body);
    }

    // The index of the DO ending a WHEN clause's condition list — the first depth-0 DO at/after `from`,
    // stopping (⇒ -1) at a depth-0 END or a depth-0 WHEN (the next clause) or end of input first. The
    // condition list has no DO of its own, so the first depth-0 DO is unambiguously this clause's.
    private static int FindWhenDoIndex(IReadOnlyList<SqlToken> sig, int from)
    {
        int depth = 0;
        for (int k = from; k < sig.Count; k++)
        {
            var t = sig[k];
            if (t.Kind == TokenKind.LParen) { depth++; continue; }
            if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (IsBodyWord(t, "DO")) return k;
            if (IsBodyWord(t, "END") || IsBodyWord(t, "WHEN")) return -1;
        }
        return -1;
    }

    // Parses the condition list of a WHEN clause — the token range [lo, hi) between WHEN and DO — into
    // WhenConditions (comma-separated, in declaration order). Returns null when the range is empty or ANY
    // segment's leading keyword is not a recognised condition form (ANY / EXCEPTION / GDSCODE / SQLCODE /
    // SQLSTATE), so the whole handler then falls back to the Other valve (never a partly-guessed node).
    private static List<WhenCondition>? ParseWhenConditions(IReadOnlyList<SqlToken> sig, int lo, int hi)
    {
        if (lo >= hi) return null;
        var conditions = new List<WhenCondition>();
        int depth = 0, segStart = lo;
        for (int k = lo; k <= hi; k++)
        {
            if (k < hi)
            {
                var kind = sig[k].Kind;
                if (kind == TokenKind.LParen) { depth++; continue; }
                if (kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
                if (!(depth == 0 && kind == TokenKind.Comma)) continue;
            }
            if (k > segStart)
            {
                var cond = ParseOneWhenCondition(sig, segStart, k);
                if (cond is null) return null; // an unrecognised segment fails the whole handler
                conditions.Add(cond);
            }
            segStart = k + 1;
        }
        return conditions.Count > 0 ? conditions : null;
    }

    // One WHEN condition over [lo, hi) — recognised strictly by its leading keyword (never guessed from
    // text). Null when the keyword is not one of the five condition forms. The operand (exception name,
    // gds/sql code, sqlstate literal) stays in the condition's tokens; only an EXCEPTION name is surfaced
    // (the binder references it as a schema object).
    private static WhenCondition? ParseOneWhenCondition(IReadOnlyList<SqlToken> sig, int lo, int hi)
    {
        if (lo >= hi) return null;
        var lead = sig[lo];
        if (lead.Kind is not (TokenKind.Keyword or TokenKind.Identifier)) return null;

        WhenHandlerKind kind;
        string? exceptionName = null;
        switch (lead.Text.ToUpperInvariant())
        {
            case "ANY": kind = WhenHandlerKind.Any; break;
            case "EXCEPTION": kind = WhenHandlerKind.ExceptionName; exceptionName = PsqlNameAt(sig, lo + 1); break;
            case "GDSCODE": kind = WhenHandlerKind.GdsCode; break;
            case "SQLCODE": kind = WhenHandlerKind.SqlCode; break;
            case "SQLSTATE": kind = WhenHandlerKind.SqlState; break;
            default: return null;
        }
        var (start, length) = TokenSpan(sig, lo, hi);
        return new WhenCondition(start, length, Sub(sig, lo, hi), kind, exceptionName);
    }

    // A leaf statement — collected up to and INCLUDING its terminating top-level ';' (a CASE…END has no
    // ';' so it is collected whole, exactly like the formatter's CollectPsqlStatement).
    //
    // B5: a leaf that is an embedded DSQL statement (SELECT / INSERT / UPDATE / DELETE / MERGE / EXECUTE)
    // is the SAME node the top-level parser produces — routed through Classify so it carries its full
    // query structure (SourceQuery / clauses / subqueries from B2/B3/B3.1), design §3.2. There is thus ONE
    // representation of, say, an INSERT-with-source, whether it sits at the top level or inside a body.
    // A PSQL-only leaf (assignment, SUSPEND, EXIT, LEAVE, POST_EVENT, EXCEPTION, RETURN, a subprogram
    // header, an unrecognised fragment) stays a PsqlLeafStatement; its interior is scanned for embedded
    // structural expressions (a scalar/EXISTS subquery, a CASE — B3/B4) so a query or CASE inside an
    // assignment / RETURN stays reachable.
    private static SqlNode ParsePsqlLeaf(IReadOnlyList<SqlToken> sig, ref int i, int nodeStart = -1)
    {
        int scanFrom = i;
        int lo = nodeStart >= 0 ? nodeStart : i;
        while (i < sig.Count)
        {
            var t = sig[i];
            i++;
            if (t.Kind == TokenKind.Semicolon) break;
        }
        var (start, length) = TokenSpan(sig, lo, i);
        var slice = Sub(sig, lo, i);
        // ⚠ Classification reads from the STATEMENT, not from the node: with a prefix consumed
        // (`IN AUTONOMOUS TRANSACTION DO INSERT …`) the node begins at the prefix while the thing being
        // classified begins after it, and asking about the prefix would recognise nothing.
        if (IsEmbeddedDsqlStart(sig, scanFrom)) return Classify(slice, start, length);
        var kind = ClassifyLeaf(sig, scanFrom, i);
        int hi = i;
        if (hi > lo && sig[hi - 1].Kind == TokenKind.Semicolon) hi--; // exclude the ';' terminator from operand scans
        var (rhsCall, assignTarget) = ReadLeafCall(sig, scanFrom, hi, kind);
        return new PsqlLeafStatement(
            start, length, slice, kind, ParseEmbeddedExpressions(sig, lo, i), rhsCall, assignTarget);
    }

    // A step-into-able local-FUNCTION call (§6.4, D9 seam c): the lone-call RHS of an assignment (with its
    // bare target) or the lone-call operand of a RETURN, over the leaf's token range [lo, hi) (the trailing
    // ';' already excluded). STRICT — anything but exactly `name(args)` as the WHOLE RHS / operand leaves both
    // null ⇒ the debugger steps over. Only Assignment / Return leaves are considered.
    private static (CallExpression? Call, string? Target) ReadLeafCall(
        IReadOnlyList<SqlToken> sig, int lo, int hi, PsqlLeafKind kind)
    {
        if (kind == PsqlLeafKind.Return)
            return (TryReadLoneCall(sig, lo + 1, hi), null); // RETURN <operand>

        if (kind == PsqlLeafKind.Assignment)
        {
            int eq = FindTopLevelAssign(sig, lo, hi);
            if (eq != lo + 1) return (null, null); // target must be a single bare identifier (not NEW.col — D10)
            var t = sig[lo];
            string? target = t.Kind switch
            {
                TokenKind.QuotedIdentifier => t.Value,
                TokenKind.Identifier => t.Text.ToUpperInvariant(),
                _ => null,
            };
            if (target is null) return (null, null);
            var call = TryReadLoneCall(sig, eq + 1, hi);
            return call is null ? (null, null) : (call, target);
        }
        return (null, null);
    }

    // The index of the first paren-depth-0 assignment operator '=' in [lo, hi), or -1. Matches the classifier's
    // '=' convention (by text) so a comparison '=' nested in a parenthesised sub-expression is never taken.
    private static int FindTopLevelAssign(IReadOnlyList<SqlToken> sig, int lo, int hi)
    {
        int depth = 0;
        for (int k = lo; k < hi; k++)
        {
            var t = sig[k];
            if (t.Kind == TokenKind.LParen) depth++;
            else if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && t.Text == "=") return k;
        }
        return -1;
    }

    // Recognises the ENTIRE condition of an IF/WHILE header (the range [lo, hi) between the keyword and
    // THEN/DO) as a lone call (§6.4). Firebird wraps the condition in parens, so a single fully-enclosing pair
    // is stripped first; the remainder must then be exactly `name(args)`. Null otherwise ⇒ step-over.
    private static CallExpression? TryReadConditionCall(IReadOnlyList<SqlToken> sig, int lo, int hi)
    {
        if (lo >= hi) return null;
        if (sig[lo].Kind == TokenKind.LParen)
        {
            int close = MatchParenTok(sig, lo, hi);
            if (close == hi - 1) { lo++; hi = close; } // strip the enclosing condition parens
        }
        return TryReadLoneCall(sig, lo, hi);
    }

    // A lone call `name(args)` over exactly [lo, hi): a name token, then '(', whose matching ')' is the LAST
    // token (nothing trails). Null otherwise — a trailing operator (`f(x)+1`), a second call, or a dotted
    // callee (`PKG.F(x)` — sig[lo+1] is '.', not '(') all leave it unrecognised ⇒ step-over (strict by design,
    // §6.4; under-recognition is always safe). Arguments reuse the D8 call-argument slicer.
    private static CallExpression? TryReadLoneCall(IReadOnlyList<SqlToken> sig, int lo, int hi)
    {
        if (hi - lo < 3) return null; // need at least: name ( )
        var nameTok = sig[lo];
        string? name = nameTok.Kind switch
        {
            TokenKind.QuotedIdentifier => nameTok.Value,
            TokenKind.Identifier => nameTok.Text.ToUpperInvariant(),
            _ => null,
        };
        if (name is null) return null;
        if (sig[lo + 1].Kind != TokenKind.LParen) return null;
        int close = MatchParenTok(sig, lo + 1, hi);
        if (close != hi - 1) return null; // the ')' must be the last token — nothing may follow the call
        var args = ReadCallArgumentList(sig, lo + 1, hi); // strips the enclosing parens, splits at depth-0 commas
        var (start, length) = TokenSpan(sig, lo, hi);
        return new CallExpression(start, length, name, args);
    }

    // True when the leaf beginning at `lo` is an embedded DSQL statement whose node the top-level parser
    // already builds (with its query structure) — SELECT / WITH / INSERT / UPDATE / DELETE / MERGE /
    // EXECUTE. Everything else is a PSQL-only construct (an assignment `x = …`, SUSPEND, EXIT, …).
    private static bool IsEmbeddedDsqlStart(IReadOnlyList<SqlToken> sig, int lo)
    {
        var t = At(sig, lo);
        if (t.Kind is not (TokenKind.Keyword or TokenKind.Identifier)) return false;
        return t.Text.ToUpperInvariant() switch
        {
            "SELECT" or "WITH" or "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "EXECUTE" => true,
            _ => false,
        };
    }

    // A DECLARE VARIABLE / DECLARE CURSOR declaration, collected to its terminating ';'.
    private static PsqlStatement ParsePsqlDeclaration(IReadOnlyList<SqlToken> sig, ref int i)
    {
        int lo = i;
        bool isCursor = false;
        while (i < sig.Count)
        {
            var t = sig[i];
            if (IsBodyWord(t, "CURSOR")) isCursor = true;
            i++;
            if (t.Kind == TokenKind.Semicolon) break;
        }
        var (start, length) = TokenSpan(sig, lo, i);
        var tokens = Sub(sig, lo, i);
        if (isCursor)
        {
            // DECLARE <name> CURSOR FOR ( <query> ) — the name is the token right after DECLARE; the
            // cursor query becomes a real QueryNode (B3.1).
            return new DeclareCursorStatement(start, length, tokens, PsqlNameAt(sig, lo + 1), ParseDeclareCursorQuery(sig, lo, i));
        }
        // DECLARE [VARIABLE] <name> <type> … — skip an optional VARIABLE keyword.
        int nameIdx = lo + 1;
        if (nameIdx < sig.Count && IsBodyWord(sig[nameIdx], "VARIABLE")) nameIdx++;
        return new DeclareVariableStatement(start, length, tokens, PsqlNameAt(sig, nameIdx));
    }

    // The query of a DECLARE … CURSOR FOR [(] <query> [)] declaration over the collected range [lo, hi).
    // Handles both the parenthesised (canonical) and bare (FOR SELECT …) forms; null when unrecognised.
    private static QueryNode? ParseDeclareCursorQuery(IReadOnlyList<SqlToken> sig, int lo, int hi)
    {
        int forIdx = -1, depth = 0;
        for (int k = lo; k < hi; k++)
        {
            var t = sig[k];
            if (t.Kind == TokenKind.LParen) { depth++; continue; }
            if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth == 0 && IsBodyWord(t, "FOR")) { forIdx = k; break; }
        }
        if (forIdx < 0) return null;

        int p = forIdx + 1;
        if (p >= hi) return null;
        if (sig[p].Kind == TokenKind.LParen && BeginsQueryParen(sig, p, hi))
        {
            int close = MatchParenTok(sig, p, hi);
            return ParseQueryRange(sig, p + 1, close < hi ? close : hi);
        }
        if (IsBodyWord(sig[p], "SELECT") || IsBodyWord(sig[p], "WITH"))
        {
            int end = hi;
            while (end > p && sig[end - 1].Kind == TokenKind.Semicolon) end--; // drop the declaration ';'
            return ParseQueryRange(sig, p, end);
        }
        return null;
    }

    // ── Classification / helpers ────────────────────────────────────────────────────────────────

    // A DECLARE PROCEDURE/FUNCTION local sub-routine (Stage X / D9) — as opposed to a DECLARE VARIABLE/CURSOR.
    private static bool IsLocalRoutineStart(IReadOnlyList<SqlToken> sig, int i)
        => IsBodyWord(sig[i], "DECLARE")
           && i + 1 < sig.Count
           && (IsBodyWord(sig[i + 1], "PROCEDURE") || IsBodyWord(sig[i + 1], "FUNCTION"));

    // First top-level (paren-depth 0) AS keyword separating a routine/EXECUTE-BLOCK header from its body;
    // -1 if none. A CAST(x AS type) AS sits at depth > 0, so it is never mistaken for the body separator.
    private static int FindTopLevelAsIndex(IReadOnlyList<SqlToken> sig)
    {
        int depth = 0;
        for (int k = 0; k < sig.Count; k++)
        {
            var kind = sig[k].Kind;
            if (kind == TokenKind.LParen) depth++;
            else if (kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && IsBodyWord(sig[k], "AS")) return k;
        }
        return -1;
    }

    // The identifier name at index idx — quoted kept as written, unquoted upper-cased (resolution
    // convention); null when the token is not a name.
    private static string? PsqlNameAt(IReadOnlyList<SqlToken> sig, int idx)
    {
        if (idx < 0 || idx >= sig.Count) return null;
        var t = sig[idx];
        return t.Kind switch
        {
            TokenKind.QuotedIdentifier => t.Value,
            TokenKind.Identifier or TokenKind.Keyword => t.Text.ToUpperInvariant(),
            _ => null,
        };
    }

    // Classifies a PSQL-ONLY leaf (the embedded DSQL statements — SELECT/INSERT/UPDATE/DELETE/MERGE/EXECUTE
    // — are routed to Classify and never reach here; see ParsePsqlLeaf / IsEmbeddedDsqlStart).
    private static PsqlLeafKind ClassifyLeaf(IReadOnlyList<SqlToken> sig, int lo, int hi)
    {
        if (lo >= hi || lo >= sig.Count) return PsqlLeafKind.Other;
        var f = sig[lo];
        if (f.Kind is TokenKind.Keyword or TokenKind.Identifier)
        {
            switch (f.Text.ToUpperInvariant())
            {
                case "SUSPEND": return PsqlLeafKind.Suspend;
                case "EXIT": return PsqlLeafKind.Exit;
                case "LEAVE": return PsqlLeafKind.Leave;
                case "BREAK": return PsqlLeafKind.Leave; // legacy synonym of unlabeled LEAVE (same leaf kind)
                case "POST_EVENT": return PsqlLeafKind.PostEvent;
                case "EXCEPTION": return PsqlLeafKind.Exception;
                case "RETURN": return PsqlLeafKind.Return;
            }
        }
        // A bare-word-led leaf containing a top-level '=' is an assignment (var = … / NEW.col = …).
        for (int k = lo; k < hi; k++)
        {
            if (sig[k].Text == "=") return PsqlLeafKind.Assignment;
        }
        return PsqlLeafKind.Other;
    }

    // A word token (keyword OR identifier — a PSQL word not in the catalog lexes as Identifier) whose
    // text matches, case-insensitively. Mirrors the formatter's IsWordTok.
    private static bool IsBodyWord(SqlToken t, string word)
        => t.Kind is TokenKind.Keyword or TokenKind.Identifier
           && string.Equals(t.Text, word, System.StringComparison.OrdinalIgnoreCase);

    // First index ≥ from whose token is the given word; -1 if none. Linear, no depth tracking — matches
    // the formatter's CollectUntilWord (THEN/DO). B3 can make this depth/CASE-aware if a consumer needs.
    private static int FindBodyWord(IReadOnlyList<SqlToken> sig, int from, string word)
    {
        for (int k = from; k < sig.Count; k++)
        {
            if (IsBodyWord(sig[k], word)) return k;
        }
        return -1;
    }
}
