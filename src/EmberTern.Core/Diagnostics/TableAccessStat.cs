namespace EmberTern.Core.Diagnostics;

/// <summary>
/// Per-table record access for one operation: rows read sequentially (full scan) vs.
/// via an index, plus rows changed (insert/update/delete). Reads are the diagnostic
/// signal; changes show which tables a DML / procedure wrote (default 0 for a read-only
/// SELECT).
/// <para>
/// This is the ONE shared diagnostic value type — the minimal common foundation for the
/// diagnostic modules, NOT a framework. It has two real producers: Performance
/// (from a MON$ before/after read delta) and the Activity Monitor (mapped from a trace
/// per-table block via <c>RawTableRead.ToTableAccess()</c>). The measurement-specific
/// wrappers (<c>TableAccessProfile</c>, <c>CaptureMethod</c>, <c>PerTableReadRow</c>, the
/// differ) stay in <c>Core.Performance</c>; only this leaf is shared. Lifted here once the
/// trace parser became the genuine second consumer — not before.
/// </para>
/// </summary>
public sealed record TableAccessStat(
    string Table,
    long SequentialReads,
    long IndexReads,
    long Inserts = 0,
    long Updates = 0,
    long Deletes = 0)
{
    public long TotalReads => SequentialReads + IndexReads;

    public long TotalChanges => Inserts + Updates + Deletes;

    /// <summary>True when the table was read (at least partly) by a full/sequential scan.</summary>
    public bool IsSequential => SequentialReads > 0;
}
