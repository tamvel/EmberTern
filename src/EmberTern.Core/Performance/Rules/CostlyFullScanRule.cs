using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Localization;

namespace EmberTern.Core.Performance.Rules;

/// <summary>R1 — a table read sequentially (full scan) at a cost worth flagging. Measured-first:
/// driven entirely by the MEASURED sequential reads + read amplification (the Phase-2 signal),
/// so a full scan of a small/cheap table is deliberately NOT flagged (the "72 looks scary" fix,
/// measured not guessed). High confidence — it is a direct measurement. When catalog cardinality
/// is known it adds a "% of table scanned" evidence line (the scale context) but never gates on
/// it (the reads alone are authoritative).</summary>
public sealed class CostlyFullScanRule : IPerformanceRule
{
    // Absolute-reads thresholds (heuristic); amplification raises severity when a lot is read
    // to return little. A scan below the Low floor isn't worth a finding.
    private const long HighReads = 50_000;
    private const long MediumReads = 5_000;
    private const long LowReads = 500;

    public string Id => "R1";

    public IReadOnlyList<Finding> Evaluate(PerformanceContext context)
    {
        var access = context.Access;
        if (access is null || access.Tables.Count == 0)
        {
            return Array.Empty<Finding>();
        }

        long returned = Math.Max(context.OutputRows, 1);
        var findings = new List<Finding>();

        foreach (var table in access.Tables
                     .Where(t => t.SequentialReads > 0)
                     .OrderByDescending(t => t.SequentialReads))
        {
            double amplification = (double)table.SequentialReads / returned;
            var severity = SeverityFor(table.SequentialReads, amplification);
            if (severity == FindingSeverity.Info)
            {
                continue; // small scan — not worth flagging
            }

            var evidence = new List<FindingEvidence>
            {
                new(PerfMessages.EvidenceSequentialReads, N(table.SequentialReads)),
                new(PerfMessages.EvidenceIndexReads, N(table.IndexReads)),
                new(context.HasResultSet
                        ? PerfMessages.EvidenceRowsReturned
                        : PerfMessages.EvidenceRowsChanged,
                    N(context.OutputRows)),
                new(PerfMessages.EvidenceReadAmplificationTable,
                    AmplificationText(table.SequentialReads, returned)),
            };

            // Scale context (never a gate): what fraction of the table the scan actually read.
            var cardinality = context.CatalogForTable(table.Table)?.RowCountEstimate;
            if (cardinality is { } rows && rows > 0)
            {
                double pct = Math.Min(100.0, (double)table.SequentialReads / rows * 100.0);
                evidence.Add(new FindingEvidence(PerfMessages.EvidenceApproxRowsInTable, N(rows)));
                evidence.Add(new FindingEvidence(PerfMessages.EvidencePercentOfTableScanned,
                    pct.ToString("0.#", CultureInfo.CurrentCulture) + "%"));
            }

            findings.Add(new Finding
            {
                Kind = FindingKind.CostlyFullScan,
                Severity = severity,
                Confidence = FindingConfidence.High,
                RuleId = Id,
                Table = table.Table,
                // ⚠ The count is argument {0} and the table {1} (ratified R3) — the English format references
                // them out of order and renders exactly as before.
                Title = LocalizableMessage.Of(
                    PerfMessages.CostlyFullScanTitle, table.SequentialReads, table.Table),
                Explanation = LocalizableMessage.Of(
                    context.HasResultSet
                        ? PerfMessages.CostlyFullScanExplanationSelect
                        : PerfMessages.CostlyFullScanExplanationChange,
                    table.SequentialReads, context.OutputRows),
                Evidence = evidence,
            });
        }

        return findings;
    }

    private static FindingSeverity SeverityFor(long seqReads, double amplification)
    {
        if (seqReads >= HighReads || (seqReads >= MediumReads && amplification >= 50))
        {
            return FindingSeverity.High;
        }
        if (seqReads >= MediumReads || (seqReads >= LowReads && amplification >= 20))
        {
            return FindingSeverity.Medium;
        }
        if (seqReads >= LowReads)
        {
            return FindingSeverity.Low;
        }
        return FindingSeverity.Info;
    }

    // ⚠ Evidence VALUES stay pre-formatted here (`N0` under the reader's culture), exactly as before — they
    // are data. A count that a SENTENCE carries travels raw instead, so `LocalizableMessage.TryGetCount` can
    // read it; its `N0` grouping moves into the resource value as `{0:N0}` and renders identically.
    private static string AmplificationText(long read, long returned)
        => (read == returned)
            ? "1×"
            : ((double)read / returned).ToString("0.#", CultureInfo.CurrentCulture) + "×";

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
