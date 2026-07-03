using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Sql;

namespace EmberTern.Core.Performance.Rules;

/// <summary>R2 — a candidate missing-index opportunity. Deliberately the most conservative rule
/// (biased toward silence): it fires ONLY when every gate passes, and it produces an
/// investigation-oriented FINDING with NO DDL / CREATE INDEX / one-click fix (those are a later
/// phase). Gates:
///  1. Measured cost — the table shows a costly sequential scan (measured seq reads ≥ floor) AND
///     high read amplification (it returned far fewer rows than it scanned; low amplification
///     means the optimizer scanned because it returns most rows → an index wouldn't help).
///  2. Sargable, seekable predicate — the candidate column has a predicate the extractor
///     understood, classified sargable, with a seekable operator. A non-sargable predicate is
///     R3's story (rewrite), never R2.
///  3. No usable existing index — the catalog has no active index covering the column as its
///     leading segment (plain OR partial), and no expression index referencing it.
///  4. Table not tiny — a known small cardinality suppresses (indexing a tiny table is noise).
/// Confidence is Medium at best (Low when cardinality couldn't be confirmed) — never certainty.</summary>
public sealed class MissingIndexRule : IPerformanceRule
{
    private const long MinSeqReads = 500;
    private const double MinAmplification = 20;
    private const long MinTableRows = 1000;

    // Operators an index can actually seek on. NotEqual / IS [NOT] NULL / LIKE / CONTAINING are
    // excluded — an index wouldn't help them, so they're not a missing-index candidate.
    private static readonly HashSet<SqlPredicateOperator> Seekable = new()
    {
        SqlPredicateOperator.Equal,
        SqlPredicateOperator.Less,
        SqlPredicateOperator.LessOrEqual,
        SqlPredicateOperator.Greater,
        SqlPredicateOperator.GreaterOrEqual,
        SqlPredicateOperator.Between,
        SqlPredicateOperator.In,
        SqlPredicateOperator.StartingWith,
    };

    public string Id => "R2";

    public IReadOnlyList<Finding> Evaluate(PerformanceContext context)
    {
        if (context.Access is null)
        {
            return Array.Empty<Finding>();
        }

        long returned = Math.Max(context.RowsReturned, 1);
        var findings = new List<Finding>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var verdict in context.Sargability.Where(v => v.IsSargable))
        {
            var p = verdict.Predicate;
            if (p.Table is null || !Seekable.Contains(p.Operator))
            {
                continue;
            }

            // Gate 1 — measured costly scan with high amplification.
            var access = context.AccessForTable(p.Table);
            if (access is null || access.SequentialReads < MinSeqReads)
            {
                continue;
            }
            double amplification = (double)access.SequentialReads / returned;
            if (amplification < MinAmplification)
            {
                continue; // returned most of what it scanned — an index wouldn't help
            }

            // Without the catalog we cannot confirm gate 3 (no existing index) — stay silent
            // rather than risk claiming a missing index that already exists.
            var catalog = context.CatalogForTable(p.Table);
            if (catalog is null)
            {
                continue;
            }

            // Gate 4 — table not tiny (only when we could estimate it).
            if (catalog.RowCountEstimate is { } rows && rows < MinTableRows)
            {
                continue;
            }

            // Gate 3 — no usable existing index covers the column.
            if (HasCoverage(catalog, p.Column))
            {
                continue;
            }

            if (!seen.Add(p.Table + "|" + p.Column))
            {
                continue;
            }

            // Medium when cardinality confirmed the table isn't tiny; Low otherwise — never High.
            var confidence = catalog.RowCountEstimate is not null ? FindingConfidence.Medium : FindingConfidence.Low;

            var evidence = new List<FindingEvidence>
            {
                new("Filter", Condition(p)),
                new("Sequential reads", N(access.SequentialReads)),
                new("Rows returned", N(context.RowsReturned)),
                new("Read amplification", amplification.ToString("0.#", CultureInfo.CurrentCulture) + "×"),
            };
            if (catalog?.RowCountEstimate is { } card)
            {
                evidence.Add(new FindingEvidence("Approx. rows in table", N(card)));
            }

            findings.Add(new Finding
            {
                Kind = FindingKind.MissingIndexCandidate,
                Severity = FindingSeverity.Medium,
                Confidence = confidence,
                RuleId = Id,
                Table = p.Table,
                Column = p.Column,
                Title = string.Format(CultureInfo.CurrentCulture,
                    "Candidate index opportunity on {0}.{1}", p.Table, p.Column),
                Explanation = string.Format(CultureInfo.CurrentCulture,
                    "Potential contributor: {0} was read sequentially ({1} rows) to return {2}, and the filter on "
                    + "{3} ({4}) had no usable index to seek. This is a candidate index opportunity — investigate "
                    + "whether {3}'s selectivity and this query's frequency would justify one. No change is applied here.",
                    p.Table, N(access.SequentialReads), N(context.RowsReturned), p.Column, Condition(p)),
                Evidence = evidence,
            });
        }

        return findings;
    }

    // Existing coverage that makes a missing-index suggestion wrong (bias to silence):
    //  • an active plain/partial index whose LEADING segment is the column, or
    //  • an active expression index that references the column.
    private static bool HasCoverage(TableCatalogInfo catalog, string column)
    {
        foreach (var index in catalog.Indexes.Where(i => !i.IsInactive))
        {
            if (index.CoversLeading(column))
            {
                return true;
            }
            if (index.IsExpression && !string.IsNullOrEmpty(index.Expression)
                && SqlScanHelpers.ContainsWord(index.Expression!, column))
            {
                return true;
            }
        }
        return false;
    }

    private static string Condition(QueryPredicate p)
    {
        var op = p.Operator switch
        {
            SqlPredicateOperator.Equal => "=",
            SqlPredicateOperator.Less => "<",
            SqlPredicateOperator.LessOrEqual => "<=",
            SqlPredicateOperator.Greater => ">",
            SqlPredicateOperator.GreaterOrEqual => ">=",
            SqlPredicateOperator.Between => "BETWEEN",
            SqlPredicateOperator.In => "IN",
            SqlPredicateOperator.StartingWith => "STARTING WITH",
            _ => "?",
        };
        return string.IsNullOrEmpty(p.Rhs) ? $"{p.LhsRaw} {op}" : $"{p.LhsRaw} {op} {p.Rhs}";
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
