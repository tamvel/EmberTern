using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language;

// Etap 6.9 / B3.1 — statement-level query attachment. Closes the "queries embedded in OTHER statements"
// gap left after B3 (editor-ast-deepening.md §12 #1): a query that is a clause of a DML / DDL statement —
// an INSERT source, a MERGE USING source, a CREATE VIEW body — or a subquery embedded in an
// UPDATE/DELETE/MERGE expression, becomes a real QueryNode instead of a token blob. Together with the
// B1 PSQL FOR-SELECT cursor + DECLARE CURSOR query (SqlParser.Psql.cs), this makes the parser the single
// structural source for every query reachable from a top-level statement or a PSQL body — with the one
// documented exception of a DML/SELECT statement appearing as a PSQL body LEAF (still a PsqlLeafStatement
// until B5 promotes leaves to reused DML nodes; §12).
//
// It reuses the B2/B3 query machinery verbatim: ParseQueryRange for a principal (source/body) query and
// ParseEmbeddedExpressions for incidental scalar/EXISTS subqueries. It is an ADDITIVE structural overlay —
// each statement keeps its full token slice, so §0 round-trip is untouched; the binder + formatter still
// token-walk these statements (convergence is deferred by user directive — build the whole model first).
// No parallel representation is created: the embedded query is modelled ONCE as a QueryNode; the
// statement's Tokens are the lossless §0 backing every node carries, not a second structural model.
//
// No principal query and its incidental subqueries ever overlap: a bare source query (INSERT … SELECT,
// VIEW AS SELECT) is parsed to its precise range and the subquery scan runs only over the region OUTSIDE
// it; a parenthesised source (MERGE USING (…)) is skipped by scanning past its closing paren — so the
// USING query is not also re-found as a scalar subquery.
public static partial class SqlParser
{
    // INSERT INTO target [(cols)] { SELECT/WITH … | VALUES(…) | DEFAULT VALUES } [RETURNING …].
    private static InsertStatement BuildInsert(IReadOnlyList<SqlToken> slice, int start, int length)
    {
        int hi = slice.Count;
        int end = TrimTrailingSemicolons(slice, hi);
        int body = FirstTopLevelWordIndex(slice, 1, end, "SELECT", "WITH", "VALUES", "DEFAULT");

        if (body < end && (IsBodyWord(slice[body], "SELECT") || IsBodyWord(slice[body], "WITH")))
        {
            // INSERT … SELECT: the source query runs to a top-level RETURNING (or the end); the only
            // subqueries OUTSIDE it are in the RETURNING list.
            int srcEnd = FirstTopLevelWordIndex(slice, body, end, "RETURNING");
            var source = ParseQueryRange(slice, body, srcEnd);
            var returning = ParseEmbeddedExpressions(slice, srcEnd, hi);
            return new InsertStatement(start, length, slice, source, returning);
        }

        // INSERT … VALUES / DEFAULT VALUES: no source query; scalar subqueries can appear in the value
        // expressions / RETURNING. Scanning from the start is safe (the column list holds no subquery).
        var subs = ParseEmbeddedExpressions(slice, 1, hi);
        return new InsertStatement(start, length, slice, sourceQuery: null, subs);
    }

    // UPDATE target [alias] SET … [WHERE …] [RETURNING …] — subqueries live in SET / WHERE / RETURNING.
    private static UpdateStatement BuildUpdate(IReadOnlyList<SqlToken> slice, int start, int length)
        => new(start, length, slice, ParseEmbeddedExpressions(slice, 1, slice.Count));

    // UPDATE OR INSERT INTO target (cols) VALUES (…) [MATCHING (…)] [RETURNING …].
    private static UpdateOrInsertStatement BuildUpdateOrInsert(IReadOnlyList<SqlToken> slice, int start, int length)
        => new(start, length, slice, ParseEmbeddedExpressions(slice, 1, slice.Count));

    // DELETE FROM target [WHERE …] [RETURNING …].
    private static DeleteStatement BuildDelete(IReadOnlyList<SqlToken> slice, int start, int length)
        => new(start, length, slice, ParseEmbeddedExpressions(slice, 1, slice.Count));

    // MERGE INTO target [alias] USING { table | (query) } src ON (…) WHEN … THEN … .
    private static MergeStatement BuildMerge(IReadOnlyList<SqlToken> slice, int start, int length)
    {
        int hi = slice.Count;
        int usingIdx = FirstTopLevelWordIndex(slice, 1, hi, "USING");

        QueryNode? source = null;
        int subScanFrom = 1;
        if (usingIdx < hi)
        {
            int s = usingIdx + 1;
            if (s < hi && slice[s].Kind == TokenKind.LParen && BeginsQueryParen(slice, s, hi))
            {
                int close = MatchParenTok(slice, s, hi);
                int innerHi = close < hi ? close : hi;
                source = ParseQueryRange(slice, s + 1, innerHi);
                subScanFrom = close < hi ? close + 1 : hi; // past the source parens (never re-find it)
            }
            else
            {
                subScanFrom = usingIdx + 1; // bare table source — scan the ON / WHEN region after it
            }
        }

        var subs = ParseEmbeddedExpressions(slice, subScanFrom, hi);
        return new MergeStatement(start, length, slice, source, subs);
    }

    // The view body query of CREATE/CREATE OR ALTER/ALTER/RECREATE VIEW … AS <query>. Null when there is
    // no top-level AS (mid-edit) or the body does not begin as a query (§0-safe — tokens untouched).
    private static QueryNode? ParseViewBodyQuery(IReadOnlyList<SqlToken> slice)
    {
        int asIdx = FindTopLevelAsIndex(slice);
        if (asIdx < 0) return null;
        int lo = asIdx + 1;
        int hi = TrimTrailingSemicolons(slice, slice.Count);
        if (lo >= hi) return null;
        // A view body is SELECT / WITH / a parenthesised query. Anything else (unusual / mid-typed) stays
        // unmodeled rather than producing a RawQuery for a non-query body.
        if (!(IsBodyWord(slice[lo], "SELECT") || IsBodyWord(slice[lo], "WITH") || slice[lo].Kind == TokenKind.LParen))
            return null;
        return ParseQueryRange(slice, lo, hi);
    }

    // ── Shared range helpers ─────────────────────────────────────────────────────────────────────

    // The exclusive end with any trailing terminator ';' tokens trimmed off (a query / body ends before
    // the statement terminator).
    private static int TrimTrailingSemicolons(IReadOnlyList<SqlToken> t, int hi)
    {
        while (hi > 0 && t[hi - 1].Kind == TokenKind.Semicolon) hi--;
        return hi;
    }

    // The index of the first depth-0 token in [from, hi) whose text matches any of <paramref name="words"/>
    // (keyword OR identifier), or hi. Paren-depth aware, so a keyword inside a column list / subquery is
    // never mistaken for the top-level clause boundary.
    private static int FirstTopLevelWordIndex(IReadOnlyList<SqlToken> t, int from, int hi, params string[] words)
    {
        int depth = 0;
        for (int k = from; k < hi; k++)
        {
            var kind = t[k].Kind;
            if (kind == TokenKind.LParen) { depth++; continue; }
            if (kind == TokenKind.RParen) { if (depth > 0) depth--; continue; }
            if (depth != 0) continue;
            foreach (var w in words)
            {
                if (IsBodyWord(t[k], w)) return k;
            }
        }
        return hi;
    }
}
