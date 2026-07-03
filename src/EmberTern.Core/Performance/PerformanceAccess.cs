using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Performance;

/// <summary>One raw per-table activity row from MON$ (a snapshot, or an already-computed
/// before/after delta): record reads (sequential + index) AND row changes (insert / update /
/// delete). All from the same <c>MON$RECORD_STATS</c> row. The change counters (default 0) are
/// additive — they let one delta describe a SELECT, a DML, or an EXECUTE PROCEDURE/BLOCK's
/// internal work. Produced by the Firebird layer, so it stays a plain DTO.</summary>
public sealed record PerTableReadRow(
    string Table,
    long SeqReads,
    long IdxReads,
    long Inserts = 0,
    long Updates = 0,
    long Deletes = 0)
{
    public long TotalReads => SeqReads + IdxReads;

    /// <summary>Rows written (inserted + updated + deleted) against this table.</summary>
    public long TotalChanges => Inserts + Updates + Deletes;
}

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

/// <summary>Computes the per-table activity delta (reads + row changes) between a before and an
/// after MON$ snapshot. Pure — the heart of the measured capture. Keeps only positive deltas (a
/// counter never goes down within a run; a table absent from "after" or with no change is
/// dropped) — so a pure DML/procedure row (0 reads, but inserts/updates/deletes &gt; 0) is
/// retained, which is what makes procedure/DML execution metrics work.</summary>
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
            long ins = row.Inserts;
            long upd = row.Updates;
            long del = row.Deletes;
            if (baseline.TryGetValue(row.Table, out var pre))
            {
                seq -= pre.SeqReads;
                idx -= pre.IdxReads;
                ins -= pre.Inserts;
                upd -= pre.Updates;
                del -= pre.Deletes;
            }
            seq = Clamp(seq);
            idx = Clamp(idx);
            ins = Clamp(ins);
            upd = Clamp(upd);
            del = Clamp(del);
            if (seq > 0 || idx > 0 || ins > 0 || upd > 0 || del > 0)
            {
                result.Add(new PerTableReadRow(row.Table, seq, idx, ins, upd, del));
            }
        }
        return result;
    }

    private static long Clamp(long v) => v < 0 ? 0 : v;
}
