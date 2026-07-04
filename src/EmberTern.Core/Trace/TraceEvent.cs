using System;
using System.Collections.Generic;
using EmberTern.Core.Diagnostics;

namespace EmberTern.Core.Trace;

/// <summary>
/// One parsed Activity Monitor event — the single data model behind ALL three
/// planned presentations: the Grid View, the Call Tree View, and a future
/// Timeline View (Chrome-DevTools / SQL-Profiler style). Nothing here is
/// Avalonia- or Firebird-specific; it is produced by the (M2) trace parser from
/// the raw Services-API text stream.
/// <para>
/// Timeline-readiness is deliberate: every event carries a span
/// (<see cref="StartTime"/> .. <see cref="SpanEnd"/>) and a hierarchy link
/// (<see cref="ParentEventId"/> + <see cref="Depth"/>), so a future timeline can
/// be laid out with no model change — only a new view. Do not remove the
/// span/hierarchy fields; the whole three-view promise rests on them.
/// </para>
/// </summary>
public sealed record TraceEvent
{
    /// <summary>Stable, monotonically increasing id assigned on arrival. Referenced by
    /// <see cref="ParentEventId"/> to build the call tree.</summary>
    public required long Id { get; init; }

    /// <summary>1-based arrival sequence (the grid's <c>#</c> column). Distinct from
    /// <see cref="Id"/> only conceptually; the engine may keep them equal.</summary>
    public required long Sequence { get; init; }

    public required TraceEventKind Kind { get; init; }

    public TraceEventSeverity Severity { get; init; } = TraceEventSeverity.Normal;

    // ---- Timeline span (Grid shows Duration; Timeline lays out Start..SpanEnd) ----

    /// <summary>When the event began. For folded START/FINISH pairs this is the START
    /// timestamp; for a bare <c>*_FINISH</c> it is the finish minus <see cref="Duration"/>
    /// (or the raw timestamp when no duration is known).</summary>
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>Explicit end timestamp when the raw stream provides one; otherwise null and
    /// <see cref="SpanEnd"/> derives it from <see cref="Duration"/>.</summary>
    public DateTimeOffset? EndTime { get; init; }

    /// <summary>Measured execution time (Firebird's "N ms"), when known. Null for events
    /// that report no duration (e.g. system markers).</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Milliseconds since the previous event in arrival order (IBExpert's
    /// "Time delta"). A grid convenience computed by the engine; null for the first event.</summary>
    public long? DeltaMs { get; init; }

    // ---- Call hierarchy (Call Tree + Timeline nesting) ----

    /// <summary>The <see cref="Id"/> of the owning event (e.g. the statement that fired this
    /// trigger/function), or null for a top-level event.</summary>
    public long? ParentEventId { get; init; }

    /// <summary>Nesting depth for call-tree indentation / timeline lanes (0 = top level).</summary>
    public int Depth { get; init; }

    // ---- Payload ----

    /// <summary>The SQL text for a <see cref="TraceEventKind.Statement"/>; null for routine-only events.</summary>
    public string? Sql { get; init; }

    /// <summary>Normalised SQL (parameters/literals stripped) used to group identical statements.
    /// Set by the engine's fingerprinter; null until computed.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>Procedure / trigger / function name for routine events; null for plain statements.</summary>
    public string? ObjectName { get; init; }

    // ---- Context ----

    /// <summary>Firebird transaction id (<c>TRA_n</c>) — the grouping key for "by transaction".</summary>
    public long? TransactionId { get; init; }

    /// <summary>Firebird attachment id (<c>ATT_n</c>) — used to compute <see cref="IsSelfActivity"/>.</summary>
    public long? AttachmentId { get; init; }

    /// <summary>Per-connection/thread context handle from the raw header (the hex token). Carried so a
    /// future call-hierarchy pass can nest events executed on the same context without re-parsing.
    /// Not shown in the grid.</summary>
    public string? ContextToken { get; init; }

    /// <summary>True when this event belongs to one of EmberTern's own attachments (data/metadata
    /// lanes). Hidden by default so the monitored ERP's activity is not drowned by self-noise.</summary>
    public bool IsSelfActivity { get; init; }

    // ---- Summary metrics (the full per-table breakdown is a detail-panel concern, added later) ----

    /// <summary>Records fetched, when reported.</summary>
    public long? RowsFetched { get; init; }

    /// <summary>Total record reads (natural + indexed) summarised for the grid; null when not reported.</summary>
    public long? Reads { get; init; }

    /// <summary>Error text for an error-bearing event; null otherwise.</summary>
    public string? ErrorText { get; init; }

    // ---- Detail-panel payload (not shown in the grid; curated from the source record) ----

    /// <summary>Input parameters, for the detail panel. Empty when none.</summary>
    public IReadOnlyList<RawTraceParam> Parameters { get; init; } = Array.Empty<RawTraceParam>();

    /// <summary>Per-table access (sequential vs indexed record reads) for the detail panel's Table
    /// Access bars — the SAME <see cref="TableAccessStat"/> the Performance module renders. Empty when
    /// the event reported no per-table block.</summary>
    public IReadOnlyList<TableAccessStat> TableAccess { get; init; } = Array.Empty<TableAccessStat>();

    /// <summary>Page reads / writes / fetches from the perf line, for the detail timing section.</summary>
    public long? PageReads { get; init; }
    public long? Writes { get; init; }
    public long? Fetches { get; init; }

    // ---- Computed helpers (no stored state) ----

    /// <summary>The effective end of the event's span: the explicit <see cref="EndTime"/> if present,
    /// otherwise <see cref="StartTime"/> plus <see cref="Duration"/> (or the instant itself when
    /// neither is known). Never earlier than <see cref="StartTime"/>. This is the value a timeline
    /// uses as the bar's right edge.</summary>
    public DateTimeOffset SpanEnd
    {
        get
        {
            if (EndTime is { } end)
                return end >= StartTime ? end : StartTime;
            return StartTime + (Duration ?? TimeSpan.Zero);
        }
    }

    /// <summary>True when the event has a measurable, non-zero span worth drawing as a bar.</summary>
    public bool HasSpan => SpanEnd > StartTime;
}
