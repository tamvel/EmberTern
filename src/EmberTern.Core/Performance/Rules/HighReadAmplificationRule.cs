using System;
using System.Collections.Generic;
using System.Globalization;
using EmberTern.Core.Localization;

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
        string amplificationText = amplification.ToString("0.#", CultureInfo.CurrentCulture);

        var evidence = new List<FindingEvidence>
        {
            // ⛔ `.Statement`, not `.Table`: this is every row the STATEMENT read, not one table's. The two
            // read identically in English and are different measurements — see PerfMessages' remarks.
            new(PerfMessages.EvidenceRowsReadStatement, N(read)),
            new(context.HasResultSet
                    ? PerfMessages.EvidenceRowsReturned
                    : PerfMessages.EvidenceRowsChanged,
                N(context.OutputRows)),
            new(PerfMessages.EvidenceReadAmplificationStatement, amplificationText + "×"),
        };
        if (subqueries > 0)
        {
            evidence.Add(new FindingEvidence(PerfMessages.EvidenceSubqueries,
                subqueries.ToString(CultureInfo.CurrentCulture)));
        }

        return new List<Finding>
        {
            new()
            {
                Kind = FindingKind.HighReadAmplification,
                Severity = amplification >= 100 ? FindingSeverity.High : FindingSeverity.Medium,
                Confidence = FindingConfidence.Medium,
                RuleId = Id,
                // ⭐ The verb is no longer a word with an English "s" glued on — it is which key we produce.
                Title = LocalizableMessage.Of(
                    context.HasResultSet
                        ? PerfMessages.HighAmplificationTitleSelect
                        : PerfMessages.HighAmplificationTitleChange,
                    amplificationText),
                Explanation = Explanation(context, subqueries, read, amplificationText),
                Evidence = evidence,
            },
        };
    }

    /// <summary>
    /// Four whole sentences where there used to be one with two holes punched in it: the output verb (twice,
    /// once conjugated) and a <c>" and N sub-quer{y|ies}"</c> tail that glued a MORPHEME on.
    /// <para>⚠ In the sub-query variants the count at <c>{0}</c> is the SUB-QUERY count, not the rows read —
    /// the argument rule takes the number the sentence's grammar depends on. Those two keys are plural
    /// families; the other two carry no inflected noun and stay flat.</para>
    /// </summary>
    private static LocalizableMessage Explanation(
        PerformanceContext context, int subqueries, long read, string amplificationText)
    {
        if (subqueries > 0)
        {
            return LocalizableMessage.Of(
                context.HasResultSet
                    ? PerfMessages.HighAmplificationExplanationSelectWithSubqueries
                    : PerfMessages.HighAmplificationExplanationChangeWithSubqueries,
                subqueries, read, context.OutputRows, amplificationText);
        }

        return LocalizableMessage.Of(
            context.HasResultSet
                ? PerfMessages.HighAmplificationExplanationSelect
                : PerfMessages.HighAmplificationExplanationChange,
            read, context.OutputRows, amplificationText);
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
