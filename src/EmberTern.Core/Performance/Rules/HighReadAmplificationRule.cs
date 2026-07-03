using System;
using System.Collections.Generic;
using System.Globalization;

namespace EmberTern.Core.Performance.Rules;

/// <summary>R6 — the query reads far more rows than it returns, but NO single full table scan
/// dominates (the cost is spread across index reads / many sub-queries — the Szkoleniowa 72×
/// case). This is the advisor's answer when R1/R4 don't fire yet the query is expensive.
/// Measured-first: driven entirely by the Phase-2 amplification metric; suppressed when a
/// sequential scan is the majority of reads (that's R1's story). Medium confidence — the
/// amplification is directly measured, the "spread" interpretation is inferred.</summary>
public sealed class HighReadAmplificationRule : IPerformanceRule
{
    private const long MinRowsRead = 5_000;
    private const double MinAmplification = 25;
    private const double MaxSequentialShare = 0.5;

    public string Id => "R6";

    public IReadOnlyList<Finding> Evaluate(PerformanceContext context)
    {
        if (context.Access is null
            || context.RowsRead is not { } read
            || context.Amplification is not { } amplification)
        {
            return Array.Empty<Finding>();
        }
        if (read < MinRowsRead || amplification < MinAmplification)
        {
            return Array.Empty<Finding>();
        }
        // A single big sequential scan dominating the reads is R1's story, not diffuse amplification.
        if (context.Access.TotalSequentialReads >= MaxSequentialShare * read)
        {
            return Array.Empty<Finding>();
        }

        int subqueries = context.SubqueryCount;
        string spread = subqueries > 0
            ? string.Format(CultureInfo.CurrentCulture, " and {0} sub-quer{1}", subqueries, subqueries == 1 ? "y" : "ies")
            : string.Empty;

        var evidence = new List<FindingEvidence>
        {
            new("Rows read", N(read)),
            new("Rows returned", N(context.RowsReturned)),
            new("Read amplification", amplification.ToString("0.#", CultureInfo.CurrentCulture) + "×"),
        };
        if (subqueries > 0)
        {
            evidence.Add(new FindingEvidence("Sub-queries", subqueries.ToString(CultureInfo.CurrentCulture)));
        }

        return new List<Finding>
        {
            new()
            {
                Kind = FindingKind.HighReadAmplification,
                Severity = amplification >= 100 ? FindingSeverity.High : FindingSeverity.Medium,
                Confidence = FindingConfidence.Medium,
                RuleId = Id,
                Title = string.Format(CultureInfo.CurrentCulture,
                    "Query reads {0}× more rows than it returns", amplification.ToString("0.#", CultureInfo.CurrentCulture)),
                Explanation = string.Format(CultureInfo.CurrentCulture,
                    "This query read {0} rows to return {1} ({2}×). No single full table scan dominates — the cost "
                    + "is spread across index reads{3}. Likely cause: the query touches far more rows than it returns "
                    + "(broad joins or many sub-queries). Investigate the sub-queries and join breadth.",
                    N(read), N(context.RowsReturned), amplification.ToString("0.#", CultureInfo.CurrentCulture), spread),
                Evidence = evidence,
            },
        };
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
