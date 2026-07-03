using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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

        long returned = Math.Max(context.RowsReturned, 1);
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
                Title = string.Format(CultureInfo.CurrentCulture,
                    "Index {0} on {1} has low selectivity", culprit.Name, table.Table),
                Explanation = string.Format(CultureInfo.CurrentCulture,
                    "Likely cause: {0} was read through index {1}, but that index has low selectivity ({2}), "
                    + "so it read {3} index entries to return {4} rows — a low-selectivity index reads many rows "
                    + "the query then discards. Investigate whether a more selective access path fits this query.",
                    table.Table, culprit.Name, Sel(culprit.Selectivity), N(table.IndexReads), N(context.RowsReturned)),
                Evidence = new List<FindingEvidence>
                {
                    new("Index reads", N(table.IndexReads)),
                    new("Rows returned", N(context.RowsReturned)),
                    new("Index amplification", idxAmplification.ToString("0.#", CultureInfo.CurrentCulture) + "×"),
                    new("Index selectivity", Sel(culprit.Selectivity)),
                },
            });
        }

        return findings;
    }

    private static string Sel(double? s) => s?.ToString("0.####", CultureInfo.CurrentCulture) ?? "n/a";
    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
