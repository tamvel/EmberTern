using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Localization;

namespace EmberTern.Core.Performance.Rules;

/// <summary>R4 — a table read through an index that returns far more rows than the query keeps.
/// Measured-first: the primary signal is the MEASURED index-read amplification (many index reads
/// for few returned rows) on a purely index-accessed table; the catalog only explains WHY (the
/// used index has low selectivity). Fires only when the plan→index→catalog chain confirms a
/// low-selectivity index — otherwise it emits nothing (prefer no finding). Medium confidence
/// (the read→index attribution is inferred, not directly measured per index).</summary>
public sealed class LowSelectivityIndexRule : IPerformanceRule
{
    private const long MinIndexReads = 1_000;
    private const double MinIndexAmplification = 10;
    private const double PoorSelectivity = 0.1; // >= 0.1 ⇒ ≤ 10 distinct keys

    public string Id => "R4";

    public IReadOnlyList<Finding> Evaluate(PerformanceContext context)
    {
        if (context.Access is null)
        {
            return Array.Empty<Finding>();
        }

        long returned = Math.Max(context.OutputRows, 1);
        var findings = new List<Finding>();

        foreach (var table in context.Access.Tables
                     .Where(t => t.SequentialReads == 0 && t.IndexReads >= MinIndexReads))
        {
            double idxAmplification = (double)table.IndexReads / returned;
            if (idxAmplification < MinIndexAmplification)
            {
                continue;
            }

            var catalog = context.CatalogForTable(table.Table);
            if (catalog is null)
            {
                continue; // can't confirm the cause — don't guess
            }

            IndexModel? culprit = null;
            foreach (var indexName in context.PlanIndexesForTable(table.Table))
            {
                var ix = catalog.Indexes.FirstOrDefault(i => string.Equals(i.Name, indexName, StringComparison.OrdinalIgnoreCase));
                if (ix?.Selectivity is { } sel && sel >= PoorSelectivity)
                {
                    culprit = ix;
                    break;
                }
            }
            if (culprit is null)
            {
                continue; // no confirmed low-selectivity index the plan actually used
            }

            findings.Add(new Finding
            {
                Kind = FindingKind.LowSelectivityIndex,
                Severity = idxAmplification >= 100 ? FindingSeverity.High : FindingSeverity.Medium,
                Confidence = FindingConfidence.Medium,
                RuleId = Id,
                Table = table.Table,
                Title = LocalizableMessage.Of(
                    PerfMessages.LowSelectivityTitle, culprit.Name, table.Table),
                Explanation = LocalizableMessage.Of(
                    context.HasResultSet
                        ? PerfMessages.LowSelectivityExplanationSelect
                        : PerfMessages.LowSelectivityExplanationChange,
                    table.IndexReads, table.Table, culprit.Name, Sel(culprit.Selectivity), context.OutputRows),
                Evidence = new List<FindingEvidence>
                {
                    new(PerfMessages.EvidenceIndexReads, N(table.IndexReads)),
                    new(context.HasResultSet
                            ? PerfMessages.EvidenceRowsReturned
                            : PerfMessages.EvidenceRowsChanged,
                        N(context.OutputRows)),
                    new(PerfMessages.EvidenceIndexAmplification,
                        idxAmplification.ToString("0.#", CultureInfo.CurrentCulture) + "×"),
                    new(PerfMessages.EvidenceIndexSelectivity, Sel(culprit.Selectivity)),
                },
            });
        }

        return findings;
    }

    /// <summary>
    /// ⛔ <b>The <c>"n/a"</c> arm is UNREACHABLE, and that is measured rather than assumed — which is why it
    /// is not a message key.</b> <c>culprit</c> is only ever assigned inside
    /// <c>ix?.Selectivity is { } sel &amp;&amp; sel &gt;= PoorSelectivity</c>, so every call below passes a
    /// non-null selectivity. Keying an English abbreviation nobody can see would be localizing dead text.
    /// <para>⭐ Named exemption with a PINNED premise, the C4b/C5 shape:
    /// <c>TheSelectivityFallback_IsStillUnreachable</c> asserts that this rule never produces a finding whose
    /// index has no selectivity, so the day the gate changes the test fails and asks for a key (#322).</para>
    /// </summary>
    private static string Sel(double? s) => s?.ToString("0.####", CultureInfo.CurrentCulture) ?? "n/a";
    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
