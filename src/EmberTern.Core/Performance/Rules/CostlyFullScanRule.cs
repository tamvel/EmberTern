using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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

        long returned = Math.Max(context.RowsReturned, 1);
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
                new("Sequential reads", N(table.SequentialReads)),
                new("Index reads", N(table.IndexReads)),
                new("Rows returned", N(context.RowsReturned)),
                new("Read amplification", AmplificationText(table.SequentialReads, returned)),
            };

            // Scale context (never a gate): what fraction of the table the scan actually read.
            var cardinality = context.CatalogForTable(table.Table)?.RowCountEstimate;
            if (cardinality is { } rows && rows > 0)
            {
                double pct = Math.Min(100.0, (double)table.SequentialReads / rows * 100.0);
                evidence.Add(new FindingEvidence("Approx. rows in table", N(rows)));
                evidence.Add(new FindingEvidence("% of table scanned", pct.ToString("0.#", CultureInfo.CurrentCulture) + "%"));
            }

            findings.Add(new Finding
            {
                Kind = FindingKind.CostlyFullScan,
                Severity = severity,
                Confidence = FindingConfidence.High,
                RuleId = Id,
                Table = table.Table,
                Title = string.Format(CultureInfo.CurrentCulture,
                    "Table {0} was scanned sequentially — {1} rows read", table.Table, N(table.SequentialReads)),
                Explanation = string.Format(CultureInfo.CurrentCulture,
                    "This table was read row-by-row (a full table scan), reading {0} rows to return {1}. "
                    + "A sequential scan reads every row, so it is often the largest cost in a slow query. "
                    + "Likely cause: no index served the filter on this table.",
                    N(table.SequentialReads), N(context.RowsReturned)),
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

    private static string AmplificationText(long read, long returned)
        => (read == returned)
            ? "1×"
            : ((double)read / returned).ToString("0.#", CultureInfo.CurrentCulture) + "×";

    private static string N(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
