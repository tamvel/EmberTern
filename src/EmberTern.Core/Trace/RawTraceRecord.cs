using System;
using System.Collections.Generic;
using EmberTern.Core.Diagnostics;

namespace EmberTern.Core.Trace;

/// <summary>
/// A faithful, loss-free parse of ONE raw Firebird trace block (one header line +
/// its body) exactly as it arrived on the Services-API <c>ServiceOutput</c> stream.
/// Unlike <see cref="TraceEvent"/> (the curated, folded, grid-ready model), a raw
/// record is 1:1 with the stream: a <c>*_START</c> and its <c>*_FINISH</c> are two
/// separate records. This is the detail-panel source of truth and the input the
/// folding/mapping stage (<see cref="TraceLogParser.Parse(string)"/>) consumes.
/// <para>
/// <see cref="TableReads"/> is the natural second consumer of a table-access model
/// (the first being Performance's per-table reads) — the trigger, when it exists,
/// for lifting a shared type into <c>Core.Diagnostics</c>. Until then it stays a
/// faithful local parse artifact.
/// </para>
/// </summary>
public sealed record RawTraceRecord
{
    /// <summary>The raw event token, verbatim (e.g. <c>EXECUTE_STATEMENT_FINISH</c>,
    /// <c>EXECUTE_PROCEDURE_START</c>, <c>TRACE_INIT</c>).</summary>
    public required string RawEventType { get; init; }

    /// <summary>The block's header timestamp (parsed offset-0; the stream has no timezone).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Server process id from the header <c>(pid:token)</c>.</summary>
    public int ServerProcessId { get; init; }

    /// <summary>The header's second hex token — a per-connection/thread context handle. Preserved
    /// because it is the key a future call-hierarchy pass uses to nest events fired on the same
    /// execution context.</summary>
    public string ContextToken { get; init; } = string.Empty;

    // ---- attachment line: "<db> (ATT_n, user:role, charset, protocol:ip/port)" (db path dropped) ----
    public long? AttachmentId { get; init; }
    public string? UserName { get; init; }
    public string? RoleName { get; init; }
    public string? Charset { get; init; }
    public string? RemoteAddress { get; init; }

    // ---- process line: "<path>:<pid>" ----
    public string? ProcessName { get; init; }
    public int? ClientProcessId { get; init; }

    // ---- transaction line: "(TRA_n, <params>)" ----
    public long? TransactionId { get; init; }
    public string? TransactionParams { get; init; }

    // ---- payload ----
    public long? StatementId { get; init; }
    public string? Sql { get; init; }
    public string? ObjectName { get; init; }
    public string? TriggerEvent { get; init; }
    public IReadOnlyList<RawTraceParam> Parameters { get; init; } = Array.Empty<RawTraceParam>();
    public IReadOnlyList<RawTraceParam> ReturnValues { get; init; } = Array.Empty<RawTraceParam>();

    // ---- perf line: "<ms> ms[, R read(s)][, W write(s)][, F fetch(es)][, M mark(s)]" ----
    public long? RecordsFetched { get; init; }
    public long? DurationMs { get; init; }
    public long? PageReads { get; init; }
    public long? Writes { get; init; }
    public long? Fetches { get; init; }
    public long? Marks { get; init; }

    /// <summary>Per-table record-access block (Natural/Index/…). Empty when the block was absent.</summary>
    public IReadOnlyList<RawTableRead> TableReads { get; init; } = Array.Empty<RawTableRead>();

    /// <summary>Error/status text if the block reported one; null otherwise. (Firebird error-event
    /// format was not present in the sample capture — parsing is best-effort and needs a real
    /// error fixture to harden; see the M2 notes in CLAUDE.md.)</summary>
    public string? ErrorText { get; init; }
}

/// <summary>An input parameter or a function return value, as printed by the trace
/// (<c>paramN = &lt;type&gt;, "&lt;value&gt;"</c>). <see cref="Value"/> is null when the trace
/// printed <c>&lt;NULL&gt;</c>.</summary>
public sealed record RawTraceParam(int Index, string DataType, string? Value);

/// <summary>One row of a statement's per-table access block. Column names mirror Firebird's
/// (the SQL keyword collision is why "Index" is surfaced as <see cref="Indexed"/>).</summary>
public sealed record RawTableRead
{
    public required string TableName { get; init; }
    public long Natural { get; init; }
    public long Indexed { get; init; }
    public long Update { get; init; }
    public long Insert { get; init; }
    public long Delete { get; init; }
    public long Backout { get; init; }
    public long Purge { get; init; }
    public long Expunge { get; init; }

    /// <summary>Record reads that surface in the curated grid: natural (full-scan) + indexed.</summary>
    public long RecordReads => Natural + Indexed;

    /// <summary>Maps this faithful trace row to the shared <see cref="TableAccessStat"/> (the curated
    /// diagnostic leaf reused by Performance's Table Access bars). The housekeeping counters
    /// (Backout/Purge/Expunge) are trace-only detail and are intentionally dropped.</summary>
    public TableAccessStat ToTableAccess() => new(TableName, Natural, Indexed, Insert, Update, Delete);
}
