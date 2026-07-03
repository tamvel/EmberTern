using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EmberTern.Core.Performance;

/// <summary>Produces findings grounded in MEASURED per-table reads. Phase 2 scope: no
/// recommendations, no index suggestions — only honest observations of what the query
/// actually read. A full scan of a small table (few reads) is deliberately NOT flagged;
/// severity is driven by absolute sequential reads and read amplification, which is what
/// separates a costly scan from a harmless one (the "72 looks scary" fix, now measured
/// rather than guessed). Pure — no engine, no I/O. When the rule set grows this migrates
/// behind an IPerformanceRule engine without changing the report shape.</summary>
public static class PerformanceFindings
{
    // Absolute-reads thresholds (heuristic). A scan below the Low floor is not worth a
    // finding; amplification raises severity when a lot is read to return little.
    private const long HighReads = 50_000;
    private const long MediumReads = 5_000;
    private const long LowReads = 500;

    public static IReadOnlyList<Finding> Build(TableAccessProfile? access, long rowsReturned)
    {
        if (access is null || access.Tables.Count == 0)
        {
            return Array.Empty<Finding>();
        }

        var findings = new List<Finding>();
        long returned = Math.Max(rowsReturned, 1);

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

            findings.Add(new Finding
            {
                Kind = FindingKind.CostlyFullScan,
                Severity = severity,
                Table = table.Table,
                Title = string.Format(CultureInfo.CurrentCulture,
                    "Table {0} was scanned sequentially — {1} rows read", table.Table, N(table.SequentialReads)),
                Explanation = string.Format(CultureInfo.CurrentCulture,
                    "This table was read row-by-row (a full table scan), reading {0} rows to return {1}. "
                    + "A sequential scan reads every row, so it is often the largest cost in a slow query.",
                    N(table.SequentialReads), N(rowsReturned)),
                Evidence = new List<FindingEvidence>
                {
                    new("Sequential reads", N(table.SequentialReads)),
                    new("Index reads", N(table.IndexReads)),
                    new("Rows returned", N(rowsReturned)),
                    new("Read amplification", AmplificationText(table.SequentialReads, returned)),
                },
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
