using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language;

// Etap 6.9 / B2 + B3 — the query sub-parser. Turns a SELECT/WITH statement's significant-token slice into
// a QueryNode tree: the clause skeleton (SELECT/FROM/WHERE/GROUP BY/HAVING/ORDER BY), the FROM list with
// its join structure (TableReference / DerivedTable / JoinedTable), set operations (UNION/INTERSECT/EXCEPT,
// left-associative), and — B3 — every NESTED query: a derived table's body, an EXISTS / scalar subquery, a
// CTE body, and a WITH main query, each a real QueryNode. The model is FULLY RECURSIVE: ParseQueryRange
// calls itself for any nested query, so a QueryNode may contain QueryNodes to any depth with no separate
// machinery. This is an ADDITIVE structural overlay — the owning SelectStatement keeps its full token
// slice, so §0 round-trip is untouched. Never throws.
//
// Depth = STRUCTURAL depth. Clause interiors (projection items, predicates, ORDER BY terms, a join's ON
// condition) stay in the owning node's Tokens — ordinary arithmetic/boolean expressions are opaque
// fragments (design #2). Only structurally-meaningful constructs become nodes; EXISTS / scalar subquery /
// derived table / CTE / CASE (B4) do, ordinary expressions do not. The embedded-expression scan
// (ParseEmbeddedExpressions) that each clause runs finds both the subqueries (B3) and the CASE
// expressions (B4) in its interior, recursively.
//
// Two invariants hold BY CONSTRUCTION: every node's span is TokenSpan of the exact range it covers (child
// spans nest + are source-ordered — pinned by StructuralAstDifferentialTests) and no token is dropped
// (round-trip is token-based). A range not cleanly recognised as a query becomes a RawQuery (the
// query-level §0 valve); ParseQueryRange therefore never returns null, so a nested-query slot is never
// left empty when a consumer needs the text.
public static partial class SqlParser
{
    // A parsed SELECT core (SELECT … HAVING, excluding a trailing ORDER BY / set operator) plus its token
    // range [Lo, Hi). ORDER BY is attached by the caller — to the SelectQuery for a plain query, or to
    // the SetOperationQuery for a set operation (where it applies to the whole).
    private readonly struct SelectCore
    {
        public SelectCore(
            int lo, int hi, SelectClause select, FromClause? from,
            WhereClause? where, GroupByClause? groupBy, HavingClause? having)
        {
            Lo = lo; Hi = hi; Select = select; From = from; Where = where; GroupBy = groupBy; Having = having;
        }

        public int Lo { get; }
        public int Hi { get; }
        public SelectClause Select { get; }
        public FromClause? From { get; }
        public WhereClause? Where { get; }
        public GroupByClause? GroupBy { get; }
        public HavingClause? Having { get; }
    }

    /// <summary>Parses a top-level SELECT/WITH statement's significant-token slice into a
    /// <see cref="QueryNode"/> — a <see cref="WithQuery"/> for a <c>WITH</c>-led query (null when the CTE
    /// clause is not cleanly recognised → the statement formats as a plain query, §0-safe), else a
    /// <see cref="SelectQuery"/>/<see cref="SetOperationQuery"/> for a plain SELECT (the trailing statement
    /// terminator <c>;</c> is excluded from the query). Returns null for a non-query slice. Never throws.</summary>
    internal static QueryNode? ParseSelectStatementQuery(IReadOnlyList<SqlToken> slice)
    {
        int n = slice.Count;
        if (n == 0) return null;
        if (Kw(At(slice, 0), "WITH")) return ParseWithQuery(slice, 0, n); // null → treated as plain query
        int hi = n;
        while (hi > 0 && slice[hi - 1].Kind == TokenKind.Semicolon) hi--; // ';' is a terminator, not the query
        if (hi == 0 || !Kw(At(slice, 0), "SELECT")) return null;
        return ParseSetQuery(slice, 0, hi);
    }

    // Parses ANY query occupying [lo, hi) — a plain SELECT, a set operation, or a WITH-led query — into a
    // QueryNode whose Tokens reproduce the exact range [lo, hi). Recursive: nested queries route back here.
    // Never returns null (a RawQuery is the §0 valve).
    private static QueryNode ParseQueryRange(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        if (lo >= hi) return new RawQuery(TokenSpanStart(t, lo, hi), TokenSpanLen(t, lo, hi), Sub(t, lo, hi));

        // Unwrap a fully-parenthesised query — ( <query> ) — so a redundantly-parenthesised operand /
        // CTE body / subquery still models structurally.
        if (t[lo].Kind == TokenKind.LParen && MatchParenTok(t, lo, hi) == hi - 1)
            return ParseQueryRange(t, lo + 1, hi - 1);

        if (Kw(t[lo], "WITH"))
            return (QueryNode?)ParseWithQuery(t, lo, hi) ?? RawQueryOf(t, lo, hi);
        if (Kw(t[lo], "SELECT"))
            return ParseSetQuery(t, lo, hi);
        return RawQueryOf(t, lo, hi);
    }

    private static RawQuery RawQueryOf(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        var (s, l) = TokenSpan(t, lo, hi);
        return new RawQuery(s, l, Sub(t, lo, hi));
    }

    // ── WITH / CTE (B3: CTE bodies + main query are real QueryNodes) ─────────────────────────────────

    // WITH [RECURSIVE] name [(cols)] AS ( <query> ) [, …] <main query>. Best-effort: any shape not cleanly
    // recognised returns null (the caller treats it as a plain query / RawQuery — §0-safe, tokens untouched).
    private static WithQuery? ParseWithQuery(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        if (lo >= hi || !Kw(t[lo], "WITH")) return null;
        int i = lo + 1;
        bool recursive = false;
        if (i < hi && IsWordText(t[i], "RECURSIVE")) { recursive = true; i++; }

        var ctes = new List<CommonTableExpression>();
        while (true)
        {
            if (i >= hi) return null;
            var nameTok = t[i];
            if (nameTok.Kind is not (TokenKind.Identifier or TokenKind.QuotedIdentifier)) return null;
            int k = i + 1;

            IReadOnlyList<SqlToken>? colTokens = null;
            if (k < hi && t[k].Kind == TokenKind.LParen)
            {
                int cclose = MatchParenTok(t, k, hi);
                if (cclose >= hi) return null;
                colTokens = Sub(t, k + 1, cclose);
                k = cclose + 1;
            }

            if (k >= hi || !Kw(t[k], "AS")) return null;
            k++;
            if (k >= hi || t[k].Kind != TokenKind.LParen) return null;
            int bodyOpen = k;
            int bodyClose = MatchParenTok(t, bodyOpen, hi);
            if (bodyClose >= hi) return null;

            var body = ParseQueryRange(t, bodyOpen + 1, bodyClose); // recurse — nested CTEs fall out
            int cteStart = nameTok.Start, cteEnd = t[bodyClose].End;
            ctes.Add(new CommonTableExpression(cteStart, cteEnd - cteStart, nameTok, colTokens, body));
            i = bodyClose + 1;

            if (i < hi && t[i].Kind == TokenKind.Comma) { i++; continue; }
            break;
        }

        if (ctes.Count == 0 || i >= hi) return null; // no CTEs / no main query (mid-edit) → plain
        var main = ParseQueryRange(t, i, hi); // the main query keeps the range verbatim (incl a trailing ';')

        var lastCte = ctes[ctes.Count - 1];
        var withClause = new WithClause(t[lo].Start, lastCte.End - t[lo].Start, recursive, ctes);
        var (ws, wl) = TokenSpan(t, lo, hi);
        return new WithQuery(ws, wl, Sub(t, lo, hi), withClause, main);
    }

    // ── Plain query / set operation ─────────────────────────────────────────────────────────────────

    // A SELECT query over [lo, hi): one SELECT core, optionally chained by set operators, with an optional
    // trailing ORDER BY (belongs to the whole query / set operation). The returned node's Tokens cover the
    // FULL [lo, hi) — a text consumer (e.g. the formatter emitting a CTE body / main query) relies on a
    // query node reproducing its whole source range, including a trailing PLAN/ROWS/';'.
    private static QueryNode ParseSetQuery(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        int i = lo;
        var cores = new List<SelectCore>();
        var ops = new List<(SetOperator Op, bool All)>();

        cores.Add(ParseSelectCore(t, ref i, hi));
        while (i < hi && TryReadSetOperator(t, i, out var op, out bool all, out int opLen))
        {
            int afterOp = i + opLen;
            if (afterOp >= hi) break; // a dangling set operator (mid-edit / lenient split) — no right
                                      // operand follows; leave its tokens in the node's range (§0) rather
                                      // than fold a degenerate empty SelectQuery.
            i = afterOp;
            cores.Add(ParseSelectCore(t, ref i, hi));
            ops.Add((op, all));
        }

        OrderByClause? orderBy = null;
        if (i < hi && Kw(At(t, i), "ORDER") && Kw(At(t, i + 1), "BY"))
        {
            int obLo = i, obHi = FindQueryTailBoundary(t, i + 2, hi);
            orderBy = MakeOrderBy(t, obLo, obHi);
            i = obHi;
        }

        if (cores.Count == 1)
            return BuildSelectQuery(t, cores[0], lo, hi, orderBy);

        // Fold set operations left-associatively; inner nodes get their natural span, the OUTERMOST
        // (returned) node covers the full [lo, hi), and the trailing ORDER BY hangs on it.
        QueryNode acc = BuildSelectQuery(t, cores[0], cores[0].Lo, cores[0].Hi, orderBy: null);
        for (int k = 0; k < ops.Count; k++)
        {
            var right = BuildSelectQuery(t, cores[k + 1], cores[k + 1].Lo, cores[k + 1].Hi, orderBy: null);
            bool isLast = k == ops.Count - 1;
            int end = isLast ? hi : cores[k + 1].Hi;
            var (s, l) = TokenSpan(t, lo, end);
            acc = new SetOperationQuery(s, l, Sub(t, lo, end), acc, ops[k].Op, ops[k].All, right, isLast ? orderBy : null);
        }
        return acc;
    }

    // Builds a SelectQuery from a core, its clauses covering their precise sub-ranges but the query node
    // itself spanning [nodeLo, nodeHi) (so its Tokens reproduce that exact range).
    private static SelectQuery BuildSelectQuery(
        IReadOnlyList<SqlToken> t, SelectCore core, int nodeLo, int nodeHi, OrderByClause? orderBy)
    {
        var (s, l) = TokenSpan(t, nodeLo, nodeHi);
        return new SelectQuery(
            s, l, Sub(t, nodeLo, nodeHi), core.Select, core.From, core.Where, core.GroupBy, core.Having, orderBy);
    }

    // Parses SELECT … [FROM …] [WHERE …] [GROUP BY …] [HAVING …], stopping at the first depth-0 ORDER BY /
    // set operator / query tail (PLAN/ROWS/…) or hi. Advances i to that boundary. Each non-FROM clause is
    // scanned for embedded subquery expressions (EXISTS / scalar), which become its children (B3).
    private static SelectCore ParseSelectCore(IReadOnlyList<SqlToken> t, ref int i, int hi)
    {
        int lo = i;
        int fromIdx = -1, whereIdx = -1, groupIdx = -1, havingIdx = -1;
        int coreEnd = hi;

        int depth = 0;
        for (int k = lo; k < hi; k++)
        {
            var kind = t[k].Kind;
            if (kind == TokenKind.LParen) { depth++; continue; }
            if (kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth != 0 || k == lo) continue; // depth>0 is a subquery; k==lo is the leading SELECT

            if (Kw(t[k], "FROM")) { if (fromIdx < 0) fromIdx = k; continue; }
            if (Kw(t[k], "WHERE")) { if (whereIdx < 0) whereIdx = k; continue; }
            if (Kw(t[k], "GROUP") && Kw(At(t, k + 1), "BY")) { if (groupIdx < 0) groupIdx = k; continue; }
            if (Kw(t[k], "HAVING")) { if (havingIdx < 0) havingIdx = k; continue; }
            if (IsCoreEnd(t, k)) { coreEnd = k; break; }
        }

        var bounds = new List<(int Idx, int Kind)> { (lo, 0) };
        if (fromIdx >= 0) bounds.Add((fromIdx, 1));
        if (whereIdx >= 0) bounds.Add((whereIdx, 2));
        if (groupIdx >= 0) bounds.Add((groupIdx, 3));
        if (havingIdx >= 0) bounds.Add((havingIdx, 4));
        bounds.Sort((a, b) => a.Idx.CompareTo(b.Idx));

        SelectClause? select = null;
        FromClause? from = null;
        WhereClause? where = null;
        GroupByClause? groupBy = null;
        HavingClause? having = null;
        for (int b = 0; b < bounds.Count; b++)
        {
            int segLo = bounds[b].Idx;
            int segHi = b + 1 < bounds.Count ? bounds[b + 1].Idx : coreEnd;
            var (s, l) = TokenSpan(t, segLo, segHi);
            var toks = Sub(t, segLo, segHi);
            switch (bounds[b].Kind)
            {
                case 0: select = new SelectClause(s, l, toks, ParseEmbeddedExpressions(t, segLo, segHi)); break;
                case 1: from = new FromClause(s, l, toks, ParseFromItems(t, segLo, segHi)); break;
                case 2: where = new WhereClause(s, l, toks, ParseEmbeddedExpressions(t, segLo, segHi)); break;
                case 3: groupBy = new GroupByClause(s, l, toks, ParseEmbeddedExpressions(t, segLo, segHi)); break;
                case 4: having = new HavingClause(s, l, toks, ParseEmbeddedExpressions(t, segLo, segHi)); break;
            }
        }

        i = coreEnd;
        return new SelectCore(lo, coreEnd, select!, from, where, groupBy, having);
    }

    private static OrderByClause MakeOrderBy(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        var (s, l) = TokenSpan(t, lo, hi);
        return new OrderByClause(s, l, Sub(t, lo, hi), ParseEmbeddedExpressions(t, lo, hi));
    }

    // ── Embedded structural expressions (B3 subqueries + B4 CASE) ─────────────────────────────────────

    // Finds the structurally-significant expressions embedded in an expression token range [lo, hi) — the
    // subqueries (EXISTS ( query ) and any other ( query ) in an expression position — scalar / IN /
    // quantified, B3) and the CASE expressions (B4) — returning one node each in source order. It DESCENDS
    // through ordinary parens (function args, grouping) so a construct nested inside them is still found,
    // but it jumps PAST a subquery's parens and PAST a CASE's END: the interior of each is modelled by that
    // node's own recursion (ParseQueryRange for a subquery, ParseCaseExpression for a CASE), so nothing is
    // captured twice.
    private static IReadOnlyList<SqlNode> ParseEmbeddedExpressions(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        List<SqlNode>? nodes = null;
        int k = lo;
        while (k < hi)
        {
            if (t[k].Kind == TokenKind.LParen && BeginsQueryParen(t, k, hi))
            {
                int close = MatchParenTok(t, k, hi);
                int innerHi = close < hi ? close : hi;
                var inner = ParseQueryRange(t, k + 1, innerHi);
                int after = close < hi ? close + 1 : hi;

                bool isExists = k - 1 >= lo && Kw(t[k - 1], "EXISTS");
                int nodeLo = isExists ? k - 1 : k;
                var (s, l) = TokenSpan(t, nodeLo, after);
                var toks = Sub(t, nodeLo, after);
                SqlNode node = isExists
                    ? new ExistsExpression(s, l, toks, inner)
                    : new ScalarSubquery(s, l, toks, inner);

                (nodes ??= new List<SqlNode>()).Add(node);
                k = after;
            }
            else if (Kw(t[k], "CASE"))
            {
                int endIdx = MatchCaseEnd(t, k, hi); // index of the matching END, or hi if unbalanced
                int after = endIdx < hi ? endIdx + 1 : hi;
                (nodes ??= new List<SqlNode>()).Add(ParseCaseExpression(t, k, endIdx, after));
                k = after;
            }
            else
            {
                k++; // an ordinary token, or a non-subquery '(' we descend through
            }
        }
        return nodes ?? (IReadOnlyList<SqlNode>)System.Array.Empty<SqlNode>();
    }

    // ── CASE expression (B4) ──────────────────────────────────────────────────────────────────────────

    // The index of the END matching the CASE at `caseIdx`, counting nested CASE…END (the only construct
    // that opens/closes an END in an expression — a subquery is a SELECT, never a bare BEGIN/END, and a
    // PSQL leaf's tokens stop at its ';' before any block END). Returns hi if unbalanced (§0: the CASE is
    // then simply not modelled; its tokens stay in the owning node).
    private static int MatchCaseEnd(IReadOnlyList<SqlToken> t, int caseIdx, int hi)
    {
        int depth = 0;
        for (int k = caseIdx; k < hi; k++)
        {
            if (Kw(t[k], "CASE")) depth++;
            else if (Kw(t[k], "END")) { if (--depth == 0) return k; }
        }
        return hi;
    }

    // Builds a CaseExpression spanning [caseIdx, after) whose interior is [caseIdx+1, endIdx). Simple vs
    // searched is decided by whether an operand precedes the first WHEN. Each arm (WHEN…THEN…result) and
    // the operand / ELSE result are scanned for nested structural expressions (subqueries / nested CASE),
    // so the model stays fully recursive; ordinary expression content stays in the node's tokens.
    private static CaseExpression ParseCaseExpression(IReadOnlyList<SqlToken> t, int caseIdx, int endIdx, int after)
    {
        int contentLo = caseIdx + 1;
        int contentHi = endIdx <= t.Count ? endIdx : t.Count;
        if (contentHi < contentLo) contentHi = contentLo;

        // Locate the WHEN / ELSE boundaries at this CASE's own level (paren-depth 0 and nested-CASE-depth 0).
        var whenIdx = new List<int>();
        int elseIdx = -1;
        int parenDepth = 0, caseDepth = 0;
        for (int k = contentLo; k < contentHi; k++)
        {
            var kind = t[k].Kind;
            if (kind == TokenKind.LParen) { parenDepth++; continue; }
            if (kind == TokenKind.RParen) { if (parenDepth > 0) parenDepth--; continue; }
            if (parenDepth != 0) continue;
            if (Kw(t[k], "CASE")) { caseDepth++; continue; }
            if (Kw(t[k], "END")) { if (caseDepth > 0) caseDepth--; continue; }
            if (caseDepth != 0) continue;
            if (Kw(t[k], "WHEN")) whenIdx.Add(k);
            else if (Kw(t[k], "ELSE") && elseIdx < 0) elseIdx = k;
        }

        int firstWhen = whenIdx.Count > 0 ? whenIdx[0] : (elseIdx >= 0 ? elseIdx : contentHi);
        bool isSearched = firstWhen == contentLo; // no operand before the first WHEN/ELSE
        int elseOrEnd = elseIdx >= 0 ? elseIdx : contentHi;

        var children = new List<SqlNode>();
        // Operand (simple CASE) — structural sub-nodes only.
        if (firstWhen > contentLo) children.AddRange(ParseEmbeddedExpressions(t, contentLo, firstWhen));

        var whens = new List<WhenClause>(whenIdx.Count);
        for (int w = 0; w < whenIdx.Count; w++)
        {
            int armLo = whenIdx[w];
            int armHi = w + 1 < whenIdx.Count ? whenIdx[w + 1] : elseOrEnd;
            var (s, l) = TokenSpan(t, armLo, armHi);
            var arm = new WhenClause(s, l, Sub(t, armLo, armHi), ParseEmbeddedExpressions(t, armLo, armHi));
            whens.Add(arm);
            children.Add(arm);
        }

        // ELSE result — structural sub-nodes only.
        if (elseIdx >= 0) children.AddRange(ParseEmbeddedExpressions(t, elseIdx, contentHi));

        var (ns, nl) = TokenSpan(t, caseIdx, after);
        return new CaseExpression(ns, nl, Sub(t, caseIdx, after), isSearched, whens, children);
    }

    // True when the '(' at lparen opens a subquery — its first inner significant token is SELECT or WITH.
    private static bool BeginsQueryParen(IReadOnlyList<SqlToken> t, int lparen, int hi)
    {
        int n = lparen + 1;
        return n < hi && (Kw(t[n], "SELECT") || Kw(t[n], "WITH"));
    }

    // ── FROM list + joins ─────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<FromItem> ParseFromItems(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        var items = new List<FromItem>();
        int j = lo + 1; // past FROM
        while (j < hi)
        {
            int before = j;
            items.Add(ParseFromItem(t, ref j, hi));
            if (j == before) j++; // anti-stall (defensive)
            if (j < hi && t[j].Kind == TokenKind.Comma) { j++; continue; }
            break;
        }
        return items;
    }

    private static FromItem ParseFromItem(IReadOnlyList<SqlToken> t, ref int j, int hi)
    {
        int itemLo = j;
        var left = ParsePrimaryFromItem(t, ref j, hi);
        while (j < hi && IsJoinStart(t, j))
        {
            j += ReadJoinKind(t, j, out var kind);
            var right = ParsePrimaryFromItem(t, ref j, hi);

            IReadOnlyList<SqlToken>? onTokens = null;
            IReadOnlyList<SqlNode>? onSubqueries = null;
            if (j < hi && Kw(t[j], "ON"))
            {
                int onLo = j;
                int onHi = FindJoinConditionEnd(t, j + 1, hi);
                onTokens = Sub(t, onLo, onHi);
                onSubqueries = ParseEmbeddedExpressions(t, onLo, onHi); // EXISTS/scalar in the ON condition
                j = onHi;
            }
            else if (j < hi && Kw(t[j], "USING"))
            {
                int uLo = j;
                j++;
                if (j < hi && t[j].Kind == TokenKind.LParen) j = MatchParenTok(t, j, hi) + 1;
                onTokens = Sub(t, uLo, j);
            }

            var (s, l) = TokenSpan(t, itemLo, j);
            left = new JoinedTable(s, l, Sub(t, itemLo, j), left, kind, right, onTokens, onSubqueries);
        }
        return left;
    }

    // A table reference or a derived table (a parenthesised subquery, recursed into — B3). Never reads or
    // advances past `hi` (a mid-typed "FROM a JOIN" with nothing after must not run off the segment).
    private static FromItem ParsePrimaryFromItem(IReadOnlyList<SqlToken> t, ref int j, int hi)
    {
        int lo = j;
        if (j < hi && t[j].Kind == TokenKind.LParen)
        {
            int close = MatchParenTok(t, j, hi);
            int innerHi = close < hi ? close : hi;
            QueryNode? inner = BeginsQueryParen(t, lo, hi) ? ParseQueryRange(t, lo + 1, innerHi) : null;
            j = close < hi ? close + 1 : hi;
            var derivedAlias = ReadAlias(t, ref j, hi);
            var (ds, dl) = TokenSpan(t, lo, j);
            return new DerivedTable(ds, dl, Sub(t, lo, j), inner, derivedAlias);
        }

        SqlToken? nameTok = j < hi && IsIdentifier(t[j]) ? t[j] : null;
        SqlToken? qualifierTok = null;
        if (j < hi) j++;
        while (j + 1 < hi && t[j].Kind == TokenKind.Dot && IsIdentifier(t[j + 1]))
        {
            qualifierTok = nameTok;
            nameTok = t[j + 1];
            j += 2;
        }

        // ⭐⭐ A SELECTABLE PROCEDURE CALL — `FROM MY_PROC(a, b) [[AS] alias]`. This block used to be absent
        // entirely: the name was read and the alias read next, so an argument list was not just unmodelled but
        // DROPPED — the entry's own tokens ended at the name, and the alias after `)` was lost with it. Every
        // consumer that needed the arguments re-scanned the SQL text instead, once per statement syntax, which is
        // how parameter typing came to work for `EXECUTE PROCEDURE`, then for `SELECT … FROM P(…)`, and for
        // nothing else (Contract #1: structure belongs in the parser, not in a consumer's token scan).
        //
        // ⚠ Only a name FOLLOWED BY '(' is an invocation. `FROM MY_PROC` (a legal no-argument selectable call)
        // is indistinguishable from a table here, and guessing would make every table look like a call.
        if (nameTok is not null && j < hi && t[j].Kind == TokenKind.LParen)
        {
            int close = MatchParenTok(t, j, hi);
            if (close < hi)
            {
                var args = ReadCallArgumentList(t, j, close + 1);
                j = close + 1;
                var routineAlias = ReadAlias(t, ref j, hi);
                var (rs, rl) = TokenSpan(t, lo, j);
                return new RoutineTableReference(
                    rs, rl, Sub(t, lo, j), nameTok, routineAlias,
                    FoldNameToken(nameTok),
                    qualifierTok is null ? null : FoldNameToken(qualifierTok),
                    args);
            }
        }

        var alias = ReadAlias(t, ref j, hi);
        var (s, l) = TokenSpan(t, lo, j);
        return new TableReference(s, l, Sub(t, lo, j), nameTok, alias);
    }

    private static SqlToken? ReadAlias(IReadOnlyList<SqlToken> t, ref int j, int hi)
    {
        if (j < hi && Kw(t[j], "AS"))
        {
            j++;
            if (j < hi && IsIdentifier(t[j])) return t[j++];
            return null;
        }
        if (j < hi && IsIdentifier(t[j])) return t[j++];
        return null;
    }

    private static int FindJoinConditionEnd(IReadOnlyList<SqlToken> t, int from, int hi)
    {
        int depth = 0;
        for (int k = from; k < hi; k++)
        {
            var kind = t[k].Kind;
            if (kind == TokenKind.LParen) { depth++; continue; }
            if (kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            if (kind == TokenKind.Comma || IsJoinStart(t, k)) return k;
        }
        return hi;
    }

    private static bool IsJoinStart(IReadOnlyList<SqlToken> t, int j)
        => Kw(t[j], "JOIN") || Kw(t[j], "INNER") || Kw(t[j], "LEFT") || Kw(t[j], "RIGHT")
           || Kw(t[j], "FULL") || Kw(t[j], "CROSS") || Kw(t[j], "NATURAL");

    private static int ReadJoinKind(IReadOnlyList<SqlToken> t, int j, out JoinKind kind)
    {
        int k = j;
        bool natural = false;
        if (Kw(At(t, k), "NATURAL")) { natural = true; k++; }

        kind = JoinKind.Inner;
        if (Kw(At(t, k), "INNER")) { kind = JoinKind.Inner; k++; }
        else if (Kw(At(t, k), "LEFT")) { kind = JoinKind.Left; k++; if (Kw(At(t, k), "OUTER")) k++; }
        else if (Kw(At(t, k), "RIGHT")) { kind = JoinKind.Right; k++; if (Kw(At(t, k), "OUTER")) k++; }
        else if (Kw(At(t, k), "FULL")) { kind = JoinKind.Full; k++; if (Kw(At(t, k), "OUTER")) k++; }
        else if (Kw(At(t, k), "CROSS")) { kind = JoinKind.Cross; k++; }

        if (Kw(At(t, k), "JOIN")) k++;
        if (natural) kind = JoinKind.Natural;
        return k - j >= 1 ? k - j : 1;
    }

    // ── Boundary detection ─────────────────────────────────────────────────────────────────────────

    private static bool IsCoreEnd(IReadOnlyList<SqlToken> t, int k)
        => (Kw(t[k], "ORDER") && Kw(At(t, k + 1), "BY"))
           || Kw(t[k], "UNION") || Kw(t[k], "INTERSECT") || Kw(t[k], "EXCEPT")
           // A PSQL singleton-select's INTO <vars> is not part of the query (a top-level DSQL SELECT never
           // has one) — ending the core before it keeps the INTO targets outside the QueryNode, where the
           // PSQL binder resolves them as local variables.
           || Kw(t[k], "INTO")
           || IsQueryTail(t, k);

    private static bool IsQueryTail(IReadOnlyList<SqlToken> t, int k)
        => Kw(t[k], "PLAN") || Kw(t[k], "ROWS") || Kw(t[k], "OFFSET") || Kw(t[k], "FETCH")
           || (Kw(t[k], "FOR") && Kw(At(t, k + 1), "UPDATE"))
           || (Kw(t[k], "WITH") && Kw(At(t, k + 1), "LOCK"));

    private static int FindQueryTailBoundary(IReadOnlyList<SqlToken> t, int from, int hi)
    {
        int depth = 0;
        for (int k = from; k < hi; k++)
        {
            var kind = t[k].Kind;
            if (kind == TokenKind.LParen) { depth++; continue; }
            if (kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth == 0 && IsQueryTail(t, k)) return k;
        }
        return hi;
    }

    private static bool TryReadSetOperator(IReadOnlyList<SqlToken> t, int i, out SetOperator op, out bool all, out int len)
    {
        op = SetOperator.Union;
        all = false;
        len = 0;
        if (Kw(t[i], "UNION")) op = SetOperator.Union;
        else if (Kw(t[i], "INTERSECT")) op = SetOperator.Intersect;
        else if (Kw(t[i], "EXCEPT")) op = SetOperator.Except;
        else return false;

        len = 1;
        if (Kw(At(t, i + 1), "ALL")) { all = true; len = 2; }
        else if (Kw(At(t, i + 1), "DISTINCT")) { len = 2; }
        return true;
    }

    // An identifier (name) token — an unquoted or quoted identifier (never a catalogued keyword).
    private static bool IsIdentifier(SqlToken t)
        => t.Kind is TokenKind.Identifier or TokenKind.QuotedIdentifier;

    private static int TokenSpanStart(IReadOnlyList<SqlToken> t, int lo, int hi) => TokenSpan(t, lo, hi).Start;
    private static int TokenSpanLen(IReadOnlyList<SqlToken> t, int lo, int hi) => TokenSpan(t, lo, hi).Length;
}
