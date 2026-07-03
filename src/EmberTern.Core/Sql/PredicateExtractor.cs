using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Sql;

/// <summary>Extracts top-level WHERE conjuncts and JOIN ON conditions from a SQL statement as
/// <see cref="QueryPredicate"/>s. A LIGHTWEIGHT scanner (reuses <see cref="SqlScanHelpers"/> +
/// <see cref="SqlAliasResolver"/>), NOT a grammar — it deliberately handles only the common,
/// unambiguous shapes and EMITS NOTHING when unsure, so the advisor never fires on a
/// misparse. Out of scope (skipped): OR-joined conjuncts, subquery/derived-table predicates,
/// CASE, multi-column expressions, and anything whose left side isn't a single identifiable
/// column. Pure Core.</summary>
public static class PredicateExtractor
{
    // Top-level words that end a WHERE / ON region.
    private static readonly HashSet<string> RegionTerminators = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "GROUP", "HAVING", "ORDER", "PLAN", "UNION", "INTERSECT", "EXCEPT",
        "RETURNING", "ROWS", "FETCH", "OFFSET", "LIMIT", "INTO", "FOR", "WITH",
        "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL", "ON",
    };

    // Words inside an expression LHS that are never the column (keywords / type names).
    private static readonly HashSet<string> LhsStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AS", "FROM", "FOR", "AND", "OR", "NOT", "NULL", "TRUE", "FALSE", "DISTINCT",
        "COLLATE", "CASE", "WHEN", "THEN", "ELSE", "END", "CAST", "USING",
    };

    public static IReadOnlyList<QueryPredicate> Extract(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Array.Empty<QueryPredicate>();
        }

        var aliases = SqlAliasResolver.ParseAliases(sql!);
        var distinctTables = aliases.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string? soleTable = distinctTables.Count == 1 ? distinctTables[0] : null;

        var result = new List<QueryPredicate>();
        foreach (var (kind, text) in ScanClauses(sql!))
        {
            foreach (var conjunct in SplitConjuncts(text))
            {
                var predicate = ParseConjunct(conjunct, kind, aliases, soleTable);
                if (predicate is not null)
                {
                    result.Add(predicate);
                }
            }
        }
        return result;
    }

    // ── Clause regions ────────────────────────────────────────────────────────
    private static List<(SqlPredicateKind Kind, string Text)> ScanClauses(string sql)
    {
        var clauses = new List<(SqlPredicateKind, string)>();
        int i = 0;
        int depth = 0;
        while (i < sql.Length)
        {
            SqlScanHelpers.SkipTrivia(sql, ref i);
            if (i >= sql.Length) break;
            if (SqlScanHelpers.TrySkipQuoted(sql, ref i)) continue;
            char c = sql[i];
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { if (depth > 0) depth--; i++; continue; }
            if (SqlScanHelpers.IsIdentifierChar(c))
            {
                var word = SqlScanHelpers.ReadWord(sql, ref i);
                if (depth == 0 && word.Equals("WHERE", StringComparison.OrdinalIgnoreCase))
                {
                    clauses.Add((SqlPredicateKind.Where, ReadRegion(sql, ref i)));
                }
                else if (depth == 0 && word.Equals("ON", StringComparison.OrdinalIgnoreCase))
                {
                    clauses.Add((SqlPredicateKind.JoinOn, ReadRegion(sql, ref i)));
                }
                continue;
            }
            i++;
        }
        return clauses;
    }

    private static string ReadRegion(string sql, ref int i)
    {
        int start = i;
        int depth = 0;
        while (i < sql.Length)
        {
            if (SqlScanHelpers.TrySkipQuoted(sql, ref i)) continue;
            char c = sql[i];
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { if (depth > 0) depth--; i++; continue; }
            if (depth == 0)
            {
                if (c == ';') break;
                if (SqlScanHelpers.IsIdentifierChar(c))
                {
                    int wordStart = i;
                    var word = SqlScanHelpers.ReadWord(sql, ref i);
                    if (RegionTerminators.Contains(word))
                    {
                        i = wordStart; // leave the terminator for the outer scanner
                        break;
                    }
                    continue;
                }
            }
            i++;
        }
        return sql.Substring(start, i - start);
    }

    // ── Conjunct split (top-level AND; skip OR-joined fragments) ────────────────
    private static IEnumerable<string> SplitConjuncts(string region)
    {
        foreach (var fragment in SplitTopLevel(region, "AND"))
        {
            // A fragment that itself contains a top-level OR is part of an OR expression —
            // ambiguous for a per-predicate advisor, so skip it (prefer no finding).
            if (!SplitTopLevel(fragment, "OR").Skip(1).Any())
            {
                yield return fragment;
            }
        }
    }

    private static List<string> SplitTopLevel(string text, string keyword)
    {
        var parts = new List<string>();
        int i = 0;
        int depth = 0;
        int start = 0;
        while (i < text.Length)
        {
            if (SqlScanHelpers.TrySkipQuoted(text, ref i)) continue;
            char c = text[i];
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { if (depth > 0) depth--; i++; continue; }
            if (depth == 0 && SqlScanHelpers.IsIdentifierChar(c))
            {
                int wordStart = i;
                var word = SqlScanHelpers.ReadWord(text, ref i);
                if (word.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(text.Substring(start, wordStart - start));
                    start = i;
                }
                continue;
            }
            i++;
        }
        parts.Add(text.Substring(start));
        return parts;
    }

    // ── Conjunct → predicate ────────────────────────────────────────────────────
    private static QueryPredicate? ParseConjunct(
        string conjunct, SqlPredicateKind kind,
        IReadOnlyDictionary<string, string> aliases, string? soleTable)
    {
        var split = FindOperator(conjunct);
        if (split is not { } op)
        {
            return null;
        }

        var lhs = conjunct.Substring(0, op.LhsEnd).Trim();
        var rhs = op.RhsStart >= conjunct.Length ? string.Empty : conjunct.Substring(op.RhsStart).Trim();
        if (lhs.Length == 0)
        {
            return null;
        }

        var column = AnalyzeLhs(lhs);
        if (column is not { } col)
        {
            return null;
        }

        string? table = col.Alias is { } a
            ? (aliases.TryGetValue(a, out var t) ? t : null)
            : soleTable;

        return new QueryPredicate
        {
            Column = col.Column,
            Alias = col.Alias,
            Table = table,
            Operator = op.Operator,
            Rhs = rhs,
            Kind = kind,
            IsColumnBare = col.IsBare,
            LhsRaw = lhs,
        };
    }

    private readonly record struct OperatorSplit(SqlPredicateOperator Operator, int LhsEnd, int RhsStart);

    private static OperatorSplit? FindOperator(string text)
    {
        int i = 0;
        int depth = 0;
        while (i < text.Length)
        {
            if (SqlScanHelpers.TrySkipQuoted(text, ref i)) continue;
            char c = text[i];
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { if (depth > 0) depth--; i++; continue; }
            if (depth != 0) { i++; continue; }

            if (c is '<' or '>' or '=' or '!')
            {
                int opStart = i;
                while (i < text.Length && text[i] is '<' or '>' or '=' or '!') i++;
                var sym = text.Substring(opStart, i - opStart);
                var mapped = MapSymbol(sym);
                if (mapped is null) return null; // unrecognized operator glyph → skip
                return new OperatorSplit(mapped.Value, opStart, i);
            }

            if (SqlScanHelpers.IsIdentifierChar(c))
            {
                int wordStart = i;
                var word = SqlScanHelpers.ReadWord(text, ref i).ToUpperInvariant();
                switch (word)
                {
                    case "IS":
                        return ParseIs(text, i, wordStart);
                    case "LIKE": return new OperatorSplit(SqlPredicateOperator.Like, wordStart, i);
                    case "IN": return new OperatorSplit(SqlPredicateOperator.In, wordStart, i);
                    case "BETWEEN": return new OperatorSplit(SqlPredicateOperator.Between, wordStart, i);
                    case "CONTAINING": return new OperatorSplit(SqlPredicateOperator.Containing, wordStart, i);
                    case "STARTING":
                        SqlScanHelpers.SkipTrivia(text, ref i);
                        SqlScanHelpers.TryKeyword(text, ref i, "WITH");
                        return new OperatorSplit(SqlPredicateOperator.StartingWith, wordStart, i);
                    default:
                        continue; // part of the LHS
                }
            }
            i++;
        }
        return null;
    }

    private static OperatorSplit ParseIs(string text, int afterIs, int isStart)
    {
        int j = afterIs;
        SqlScanHelpers.SkipTrivia(text, ref j);
        bool not = SqlScanHelpers.TryKeyword(text, ref j, "NOT");
        SqlScanHelpers.SkipTrivia(text, ref j);
        SqlScanHelpers.TryKeyword(text, ref j, "NULL");
        return new OperatorSplit(not ? SqlPredicateOperator.IsNotNull : SqlPredicateOperator.IsNull, isStart, j);
    }

    private static SqlPredicateOperator? MapSymbol(string sym) => sym switch
    {
        "=" => SqlPredicateOperator.Equal,
        "<>" or "!=" => SqlPredicateOperator.NotEqual,
        "<" => SqlPredicateOperator.Less,
        "<=" => SqlPredicateOperator.LessOrEqual,
        ">" => SqlPredicateOperator.Greater,
        ">=" => SqlPredicateOperator.GreaterOrEqual,
        _ => null,
    };

    private readonly record struct LhsColumn(string Column, string? Alias, bool IsBare);

    private static LhsColumn? AnalyzeLhs(string lhs)
    {
        // Bare "(qualifier.)?column" — the whole LHS is one identifier path.
        int j = 0;
        SqlScanHelpers.SkipTrivia(lhs, ref j);
        var id1 = SqlScanHelpers.ReadIdentifier(lhs, ref j);
        if (id1 is not null)
        {
            SqlScanHelpers.SkipTrivia(lhs, ref j);
            if (j < lhs.Length && lhs[j] == '.')
            {
                j++;
                SqlScanHelpers.SkipTrivia(lhs, ref j);
                var id2 = SqlScanHelpers.ReadIdentifier(lhs, ref j);
                if (id2 is not null)
                {
                    SqlScanHelpers.SkipTrivia(lhs, ref j);
                    if (j == lhs.Length)
                    {
                        return new LhsColumn(id2, id1.ToUpperInvariant(), IsBare: true);
                    }
                }
            }
            else if (j == lhs.Length)
            {
                return new LhsColumn(id1, null, IsBare: true);
            }
        }

        // Expression / function: the column is the first (qualifier.)?identifier that isn't a
        // function name (followed by '(') and isn't a keyword/type.
        return FindColumnInExpression(lhs);
    }

    private static LhsColumn? FindColumnInExpression(string lhs)
    {
        int i = 0;
        while (i < lhs.Length)
        {
            if (SqlScanHelpers.TrySkipQuoted(lhs, ref i)) continue;
            if (!SqlScanHelpers.IsIdentifierChar(lhs[i]) && lhs[i] != '"') { i++; continue; }

            var id = SqlScanHelpers.ReadIdentifier(lhs, ref i);
            if (id is null) { i++; continue; }

            int save = i;
            SqlScanHelpers.SkipTrivia(lhs, ref i);
            if (i < lhs.Length && lhs[i] == '(')
            {
                // function name — skip it (its parens are scanned as normal chars next)
                continue;
            }
            if (i < lhs.Length && lhs[i] == '.')
            {
                i++;
                SqlScanHelpers.SkipTrivia(lhs, ref i);
                var col = SqlScanHelpers.ReadIdentifier(lhs, ref i);
                if (col is not null)
                {
                    return new LhsColumn(col, id.ToUpperInvariant(), IsBare: false);
                }
                continue;
            }
            i = save;
            if (!LhsStopWords.Contains(id))
            {
                return new LhsColumn(id, null, IsBare: false);
            }
        }
        return null;
    }
}
