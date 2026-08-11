using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Localization;
using EmberTern.Core.Sql;

namespace EmberTern.Core.Performance.Rules;

/// <summary>R3 — a non-sargable predicate (function/expression on the column, or a leading-
/// wildcard LIKE) that blocks an EXISTING index while the table was scanned expensively.
/// Measured-first + high-precision: fires only when (a) the predicate is non-sargable, (b) the
/// table was measured with a costly sequential scan, AND (c) the catalog shows a plain index
/// that covers the column as its leading segment — i.e. an index exists but can't be used, so
/// the remedy is a predicate REWRITE (distinct from a missing index, which is R2/Phase 3b).
/// Otherwise emits nothing. Medium confidence (the predicate parse is lightweight).</summary>
public sealed class NonSargablePredicateRule : IPerformanceRule
{
    private const long MinSeqReads = 500;

    public string Id => "R3";

    public IReadOnlyList<Finding> Evaluate(PerformanceContext context)
    {
        if (context.Access is null)
        {
            return Array.Empty<Finding>();
        }

        var findings = new List<Finding>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var verdict in context.Sargability.Where(v => !v.IsSargable))
        {
            var p = verdict.Predicate;
            if (p.Table is null)
            {
                continue;
            }

            var access = context.AccessForTable(p.Table);
            if (access is null || access.SequentialReads < MinSeqReads)
            {
                continue; // the non-sargable predicate didn't correlate with a costly scan
            }

            var index = context.CatalogForTable(p.Table)?.Indexes.FirstOrDefault(i => i.CoversLeading(p.Column));
            if (index is null)
            {
                continue; // no existing index it could have used → not a rewrite story (R2 territory)
            }

            if (!seen.Add(p.Table + "|" + p.Column + "|" + index.Name))
            {
                continue;
            }

            // ⭐ The issue used to be a CLAUSE substituted into the front of the sentence. Two whole-sentence
            // keys instead, chosen by the closed `SargabilityIssue` enum — the ratified enum→key shape. A
            // fragment glued to a fixed frame cannot be translated into a language that inflects.
            var explanationKey = verdict.Issue == SargabilityIssue.LeadingWildcardLike
                ? PerfMessages.NonSargableExplanationLeadingWildcardLike
                : PerfMessages.NonSargableExplanationFunctionOnColumn;

            findings.Add(new Finding
            {
                Kind = FindingKind.NonSargablePredicate,
                Severity = FindingSeverity.Medium,
                Confidence = FindingConfidence.Medium,
                RuleId = Id,
                Table = p.Table,
                Column = p.Column,
                Title = LocalizableMessage.Of(
                    PerfMessages.NonSargableTitle, p.Table, p.Column, index.Name),
                Explanation = LocalizableMessage.Of(
                    explanationKey,
                    access.SequentialReads, index.Name, p.Column, Condition(p), p.Table),
                Evidence = new List<FindingEvidence>
                {
                    new(PerfMessages.EvidenceCondition, Condition(p)),
                    new(PerfMessages.EvidenceExistingIndex, index.Name),
                    new(PerfMessages.EvidenceSequentialReads, N(access.SequentialReads)),
                },
            });
        }

        return findings;
    }

    private static string Condition(QueryPredicate p)
    {
        var op = p.Operator switch
        {
            SqlPredicateOperator.Equal => "=",
            SqlPredicateOperator.NotEqual => "<>",
            SqlPredicateOperator.Less => "<",
            SqlPredicateOperator.LessOrEqual => "<=",
            SqlPredicateOperator.Greater => ">",
            SqlPredicateOperator.GreaterOrEqual => ">=",
            SqlPredicateOperator.Like => "LIKE",
            SqlPredicateOperator.StartingWith => "STARTING WITH",
            SqlPredicateOperator.Containing => "CONTAINING",
            SqlPredicateOperator.In => "IN",
            SqlPredicateOperator.Between => "BETWEEN",
            SqlPredicateOperator.IsNull => "IS NULL",
            SqlPredicateOperator.IsNotNull => "IS NOT NULL",
            _ => "?",
        };
        return string.IsNullOrEmpty(p.Rhs) ? $"{p.LhsRaw} {op}" : $"{p.LhsRaw} {op} {p.Rhs}";
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
