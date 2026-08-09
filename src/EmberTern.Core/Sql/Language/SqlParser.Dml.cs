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
            // No (column, value) pairs: an INSERT … SELECT supplies values as a query's columns, not as spans.
            return new InsertStatement(start, length, slice, source, returning, TargetTableName(slice, 2));
        }

        // INSERT … VALUES / DEFAULT VALUES: no source query; scalar subqueries can appear in the value
        // expressions / RETURNING. Scanning from the start is safe (the column list holds no subquery).
        var subs = ParseEmbeddedExpressions(slice, 1, hi);
        return new InsertStatement(
            start, length, slice, sourceQuery: null, subs,
            TargetTableName(slice, 2), PairColumnsWithValues(slice, 2, end));
    }

    // UPDATE target [alias] SET … [WHERE …] [RETURNING …] — subqueries live in SET / WHERE / RETURNING.
    private static UpdateStatement BuildUpdate(IReadOnlyList<SqlToken> slice, int start, int length)
        => new(start, length, slice,
            ParseEmbeddedExpressions(slice, 1, slice.Count),
            TargetTableName(slice, 1),
            PairSetAssignments(slice, TrimTrailingSemicolons(slice, slice.Count)));

    // UPDATE OR INSERT INTO target (cols) VALUES (…) [MATCHING (…)] [RETURNING …].
    private static UpdateOrInsertStatement BuildUpdateOrInsert(
        IReadOnlyList<SqlToken> slice, int start, int length)
    {
        int end = TrimTrailingSemicolons(slice, slice.Count);
        return new UpdateOrInsertStatement(
            start, length, slice,
            ParseEmbeddedExpressions(slice, 1, slice.Count),
            TargetTableName(slice, 4),                       // UPDATE OR INSERT INTO <target>
            PairColumnsWithValues(slice, 4, end));
    }

    // ── Column ↔ value pairing (IColumnValueTarget producers) ────────────────────────────────────────
    //
    // ⭐ Added 2026-08-03 on the user's directive that a placeholder's TYPE must come from the model wherever the
    // model can prove it — "argument procedury → typ parametru, INSERT/UPDATE → typ kolumny" — and explicitly NOT
    // from a series of per-statement branches in a consumer. These two helpers are the producers; the consumer
    // walks IColumnValueTarget and never learns which syntax produced a pair.

    // The table a DML statement writes to. <paramref name="nameIdx"/> is where the name stands after the verb
    // (INSERT INTO → 2, UPDATE → 1, UPDATE OR INSERT INTO → 4). A dotted name folds to its last segment, matching
    // the catalog convention every other name in the parser uses.
    private static string? TargetTableName(IReadOnlyList<SqlToken> t, int nameIdx)
    {
        if (nameIdx >= t.Count) return null;
        var name = t[nameIdx];
        if (!IsIdentifier(name)) return null;

        int j = nameIdx + 1;
        while (j + 1 < t.Count && t[j].Kind == TokenKind.Dot && IsIdentifier(t[j + 1]))
        {
            name = t[j + 1];
            j += 2;
        }
        return FoldNameToken(name);
    }

    // `(c1, c2, …) VALUES (v1, v2, …)` → positional pairs. Shared by INSERT and UPDATE OR INSERT because the
    // shape is identical — the same reason SqlFormatter lays both out through one FormatInsertFamily.
    //
    // ⚠ Returns nothing unless BOTH lists are present and their lengths MATCH. A mismatch is a statement Firebird
    // will reject anyway, and pairing the prefix would attach types to values whose column is not yet decided
    // (rule #11: never guess). An insert with no column list yields nothing too — matching values to columns would
    // need the table's catalog order, which is a lookup, not a fact about the text.
    private static IReadOnlyList<ColumnValue> PairColumnsWithValues(
        IReadOnlyList<SqlToken> t, int afterTarget, int end)
    {
        int open = afterTarget;
        while (open < end && t[open].Kind != TokenKind.LParen)
        {
            // A VALUES/SELECT/DEFAULT before any '(' means there is no column list.
            if (IsBodyWord(t[open], "VALUES") || IsBodyWord(t[open], "SELECT") || IsBodyWord(t[open], "DEFAULT"))
            {
                return Array.Empty<ColumnValue>();
            }
            open++;
        }
        if (open >= end) return Array.Empty<ColumnValue>();

        int close = MatchParenTok(t, open, end);
        if (close >= end) return Array.Empty<ColumnValue>();

        var columns = ReadColumnNameList(t, open + 1, close);
        if (columns.Count == 0) return Array.Empty<ColumnValue>();

        int values = FirstTopLevelWordIndex(t, close + 1, end, "VALUES");
        if (values >= end) return Array.Empty<ColumnValue>();

        int vOpen = values + 1;
        if (vOpen >= end || t[vOpen].Kind != TokenKind.LParen) return Array.Empty<ColumnValue>();
        int vClose = MatchParenTok(t, vOpen, end);
        if (vClose >= end) return Array.Empty<ColumnValue>();

        var spans = ReadCallArgumentList(t, vOpen, vClose + 1);
        if (spans.Count != columns.Count) return Array.Empty<ColumnValue>();

        var pairs = new List<ColumnValue>(columns.Count);
        for (int i = 0; i < columns.Count; i++)
        {
            pairs.Add(new ColumnValue(columns[i], spans[i].Start, spans[i].Length));
        }
        return pairs;
    }

    // `SET col = <expr> [, col = <expr>]…` up to a top-level WHERE / RETURNING / PLAN / ORDER — pairs by
    // ADJACENCY, which is why IColumnValueTarget carries pairs rather than two parallel lists.
    //
    // ⚠ The WHERE predicate is deliberately excluded: at structural depth a predicate is a token fragment, so
    // `WHERE col = :p` is not a modelled pairing. Its placeholder stays untyped rather than typed on a guess.
    private static IReadOnlyList<ColumnValue> PairSetAssignments(IReadOnlyList<SqlToken> t, int end)
    {
        int set = FirstTopLevelWordIndex(t, 1, end, "SET");
        if (set >= end) return Array.Empty<ColumnValue>();

        int stop = FirstTopLevelWordIndex(t, set + 1, end, "WHERE", "RETURNING", "PLAN", "ORDER");
        var pairs = new List<ColumnValue>();

        int i = set + 1;
        while (i < stop)
        {
            // <name> [. <name>] '=' <expr up to a depth-0 comma or the stop>
            if (!IsIdentifier(t[i])) { i++; continue; }
            var nameTok = t[i];
            int j = i + 1;
            while (j + 1 < stop && t[j].Kind == TokenKind.Dot && IsIdentifier(t[j + 1]))
            {
                nameTok = t[j + 1];
                j += 2;
            }
            // '=' lexes as an Operator, so the assignment is recognised by its TEXT.
            if (j >= stop || t[j].Kind != TokenKind.Operator || t[j].Text != "=") { i = j + 1; continue; }

            int valueLo = j + 1;
            int valueHi = valueLo;
            int depth = 0;
            while (valueHi < stop)
            {
                var kind = t[valueHi].Kind;
                if (kind == TokenKind.LParen) depth++;
                else if (kind == TokenKind.RParen) depth--;
                else if (depth == 0 && kind == TokenKind.Comma) break;
                valueHi++;
            }

            if (valueHi > valueLo)
            {
                var first = t[valueLo];
                var last = t[valueHi - 1];
                pairs.Add(new ColumnValue(
                    FoldNameToken(nameTok) ?? nameTok.Text,
                    first.Start,
                    last.Start + last.Length - first.Start));
            }

            i = valueHi + 1;
        }

        return pairs;
    }

    // A bare comma-separated identifier list inside parens — the column list of an INSERT / UPDATE OR INSERT.
    // Anything that is not a plain (optionally dotted) identifier makes the whole list unusable: a column list
    // is names only, so a surprise there means this is not the list we think it is.
    private static IReadOnlyList<string> ReadColumnNameList(IReadOnlyList<SqlToken> t, int lo, int hi)
    {
        var names = new List<string>();
        int i = lo;
        while (i < hi)
        {
            if (!IsIdentifier(t[i])) return Array.Empty<string>();
            var nameTok = t[i];
            i++;
            while (i + 1 < hi && t[i].Kind == TokenKind.Dot && IsIdentifier(t[i + 1]))
            {
                nameTok = t[i + 1];
                i += 2;
            }
            var folded = FoldNameToken(nameTok);
            if (folded is null) return Array.Empty<string>();
            names.Add(folded);

            if (i < hi)
            {
                if (t[i].Kind != TokenKind.Comma) return Array.Empty<string>();
                i++;
            }
        }
        return names;
    }

    // DELETE FROM target [WHERE …] [RETURNING …].
    private static DeleteStatement BuildDelete(IReadOnlyList<SqlToken> slice, int start, int length)
        => new(start, length, slice, ParseEmbeddedExpressions(slice, 1, slice.Count));

    // MERGE INTO target [alias] USING { table | (query) } src ON (…) WHEN … THEN … .
    private static MergeStatement BuildMerge(IReadOnlyList<SqlToken> slice, int start, int length)
    {
        int hi = slice.Count;
        int usingIdx = FirstTopLevelWordIndex(slice, 1, hi, "USING");

        QueryNode? source = null;
        FromItem? sourceItem = null;
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
                // ⭐ A BARE source is now modelled too — `USING <name>[(args)] [[AS] alias]`. This branch used to
                // just note "bare table source" and move on, which left `MERGE … USING MY_PROC(:a) s` the one
                // remaining place a routine could be invoked with the tree unaware of it (2026-08-03).
                // ⚠ Parsed by the same ParsePrimaryFromItem a FROM entry uses, so a routine source becomes the
                // same RoutineTableReference and no second notion of "a source" is introduced. The ON/WHEN scan
                // starts past whatever it consumed, so the source is never also re-found as a subquery.
                int end = FirstTopLevelWordIndex(slice, s, hi, "ON");
                int j = s;
                sourceItem = j < end ? ParsePrimaryFromItem(slice, ref j, end) : null;
                subScanFrom = j > s ? j : usingIdx + 1;
            }
        }

        var subs = ParseEmbeddedExpressions(slice, subScanFrom, hi);
        return new MergeStatement(start, length, slice, source, subs, sourceItem);
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
