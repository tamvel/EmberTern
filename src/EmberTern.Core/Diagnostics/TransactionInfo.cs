namespace EmberTern.Core.Diagnostics;

/// <summary>
/// One live transaction — a <c>MON$TRANSACTIONS</c> row, isolation already decoded to
/// words by the reader. Raw facts only; ages / GC-impact are derived by
/// <see cref="SessionHealthAnalyzer"/> against the database-wide state. Pure.
/// </summary>
public sealed record TransactionInfo
{
    public required long TransactionId { get; init; }

    public required long AttachmentId { get; init; }

    /// <summary><c>MON$STATE</c>: 0 = idle, 1 = active.</summary>
    public int StateCode { get; init; }

    public bool IsActive => StateCode == 1;

    /// <summary><c>MON$TIMESTAMP</c> — when the transaction started (server local time).</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary><c>MON$ISOLATION_MODE</c>: 0 consistency, 1 concurrency(snapshot),
    /// 2 read-committed rec_version, 3 no_rec_version, 4 read_consistency.</summary>
    public int IsolationModeCode { get; init; }

    /// <summary>Human-readable isolation, decoded by the reader (e.g. "Snapshot").</summary>
    public string IsolationMode { get; init; } = string.Empty;

    public bool ReadOnly { get; init; }

    public bool AutoCommit { get; init; }

    public bool AutoUndo { get; init; }

    /// <summary>This transaction's own view of the Oldest Interesting Transaction.</summary>
    public long OldestTransaction { get; init; }

    /// <summary>This transaction's own view of the Oldest Active Transaction.</summary>
    public long OldestActive { get; init; }

    /// <summary><c>MON$SNAPSHOT_NUMBER</c> (Firebird 4+); null on earlier engines.</summary>
    public long? SnapshotNumber { get; init; }

    /// <summary>A snapshot (concurrency) or consistency transaction — the isolations that
    /// pin a stable view and therefore hold back garbage collection. Read-committed does not.</summary>
    public bool IsSnapshot => IsolationModeCode is 0 or 1;
}

/// <summary>
/// Database-wide transaction markers from <c>MON$DATABASE</c> — the canonical source of
/// the transaction gap. GC and record-version bloat are governed by these: nothing newer
/// than <see cref="OldestSnapshot"/> can be garbage-collected while an old transaction lives.
/// </summary>
public sealed record DatabaseTransactionState
{
    /// <summary>Oldest Interesting Transaction (OIT).</summary>
    public long OldestTransaction { get; init; }

    /// <summary>Oldest Active Transaction (OAT) — the GC gatekeeper.</summary>
    public long OldestActive { get; init; }

    /// <summary>Oldest Snapshot (OST).</summary>
    public long OldestSnapshot { get; init; }

    /// <summary>The id the next transaction will receive.</summary>
    public long NextTransaction { get; init; }

    /// <summary>How far the oldest active transaction lags behind the present — the headline
    /// "transaction gap". A large lag ≈ many transactions' worth of record versions pinned.</summary>
    public long OldestActiveLag => Math.Max(0, NextTransaction - OldestActive);

    /// <summary>OAT − OIT — the sweep gap (grows toward the sweep interval).</summary>
    public long SweepGap => Math.Max(0, OldestActive - OldestTransaction);
}
