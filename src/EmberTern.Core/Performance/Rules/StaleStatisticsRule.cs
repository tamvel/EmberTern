using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Localization;

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
                Title = LocalizableMessage.Of(PerfMessages.StaleStatisticsTitle, table.Table),
                // ⭐ Two things stopped being glued together here. The `has`/`have` choice was English VERB
                // agreement decided by a `?:` in Core — it is now a plural FAMILY the reader's own culture
                // resolves. The corroboration clause was a tail welded onto the sentence — it is now its own
                // whole-sentence key. ⚠ The count is argument {0} (ratified R3), which is what lets the
                // family work at all.
                Explanation = LocalizableMessage.Of(
                    corroborated
                        ? PerfMessages.StaleStatisticsExplanationCorroborated
                        : PerfMessages.StaleStatisticsExplanation,
                    (long)stale.Count, string.Join(", ", stale), table.Table),
                Evidence = new List<FindingEvidence>
                {
                    new(PerfMessages.EvidenceIndexesWithoutStatistics, string.Join(", ", stale)),
                    // ⛔ `.Table`, not `.Statement`: this is THIS table's reads. R6's identically-worded row
                    // is the whole statement's.
                    new(PerfMessages.EvidenceRowsReadTable, N(table.TotalReads)),
                },
            });
        }

        return findings;
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
