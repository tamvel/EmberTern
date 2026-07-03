using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Performance;

/// <summary>One raw per-table read counter row from MON$ (a snapshot or an already-computed
/// before/after delta). Produced by the Firebird layer, so it stays a plain DTO.</summary>
public sealed record PerTableReadRow(string Table, long SeqReads, long IdxReads);

/// <summary>Per-table access for one profiled statement: how many rows were read
/// sequentially (full scan) vs. via an index. The single most diagnostic signal.</summary>
public sealed record TableAccessStat(string Table, long SequentialReads, long IndexReads)
{
    public long TotalReads => SequentialReads + IndexReads;

    /// <summary>True when the table was read (at least partly) by a full/sequential scan.</summary>
    public bool IsSequential => SequentialReads > 0;
}

/// <summary>The per-table access profile for a statement, plus which capture strategy
/// produced it. Ordered most-sequential-first for the Table Access bars.</summary>
public sealed record TableAccessProfile
{
    public IReadOnlyList<TableAccessStat> Tables { get; init; } = Array.Empty<TableAccessStat>();

    public CaptureMethod Method { get; init; } = CaptureMethod.MonAttachmentDelta;

    /// <summary>Total rows read across every table (sequential + index). The numerator of
    /// the read-amplification ratio.</summary>
    public long TotalRowsRead => Tables.Sum(t => t.TotalReads);

    public long TotalSequentialReads => Tables.Sum(t => t.SequentialReads);
}

/// <summary>Computes the per-table read delta between a before and an after MON$ snapshot.
/// Pure — the heart of the measured-reads capture. Keeps only positive deltas (a counter
/// never goes down within a run; a table absent from "after" or unchanged is dropped).</summary>
public static class TableStatsDiffer
{
    public static IReadOnlyList<PerTableReadRow> Diff(
        IReadOnlyList<PerTableReadRow> before,
        IReadOnlyList<PerTableReadRow> after)
    {
        var baseline = new Dictionary<string, PerTableReadRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in before)
        {
            baseline[row.Table] = row;
        }

        var result = new List<PerTableReadRow>();
        foreach (var row in after)
        {
            long seq = row.SeqReads;
            long idx = row.IdxReads;
            if (baseline.TryGetValue(row.Table, out var pre))
            {
                seq -= pre.SeqReads;
                idx -= pre.IdxReads;
            }
            if (seq < 0)
            {
                seq = 0;
            }
            if (idx < 0)
            {
                idx = 0;
            }
            if (seq > 0 || idx > 0)
            {
                result.Add(new PerTableReadRow(row.Table, seq, idx));
            }
        }
        return result;
    }
}
