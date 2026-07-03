using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EmberTern.Core.Performance.Rules;

/// <summary>R5 — an accessed table has an index whose selectivity statistics were never computed
/// (Firebird's -1 sentinel, normalized to null), so the optimizer may misestimate and pick a
/// poor plan. Catalog-driven, but gated on a MEASURED access (≥ a read floor, so an empty/never-
/// touched table isn't flagged) and corroborated when the table was also scanned sequentially.
/// Low confidence (Medium when corroborated). Investigation-oriented — it points at possibly
/// stale stats, it does NOT recommend an action.</summary>
public sealed class StaleStatisticsRule : IPerformanceRule
{
    private const long MinReads = 500;

    public string Id => "R5";

    public IReadOnlyList<Finding> Evaluate(PerformanceContext context)
    {
        if (context.Access is null)
        {
            return Array.Empty<Finding>();
        }

        var findings = new List<Finding>();
        foreach (var table in context.Access.Tables.Where(t => t.TotalReads >= MinReads))
        {
            var catalog = context.CatalogForTable(table.Table);
            if (catalog is null)
            {
                continue;
            }

            // Plain, active indexes with no computed selectivity (expression indexes have no
            // segment columns → excluded).
            var stale = catalog.Indexes
                .Where(i => !i.IsInactive && i.Selectivity is null && i.Columns.Count > 0)
                .Select(i => i.Name)
                .ToList();
            if (stale.Count == 0)
            {
                continue;
            }

            bool corroborated = table.SequentialReads > 0;
            findings.Add(new Finding
            {
                Kind = FindingKind.StaleStatistics,
                Severity = corroborated ? FindingSeverity.Medium : FindingSeverity.Low,
                Confidence = corroborated ? FindingConfidence.Medium : FindingConfidence.Low,
                RuleId = Id,
                Table = table.Table,
                Title = string.Format(CultureInfo.CurrentCulture,
                    "Index statistics on {0} may be out of date", table.Table),
                Explanation = string.Format(CultureInfo.CurrentCulture,
                    "Index {0} on {1} {2} no computed selectivity statistics, so the optimizer may misestimate "
                    + "and choose a poor plan{3}. Investigate whether this table's index statistics are up to date.",
                    string.Join(", ", stale), table.Table, stale.Count == 1 ? "has" : "have",
                    corroborated ? "; this table was also read sequentially" : string.Empty),
                Evidence = new List<FindingEvidence>
                {
                    new("Indexes without statistics", string.Join(", ", stale)),
                    new("Rows read", N(table.TotalReads)),
                },
            });
        }

        return findings;
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
