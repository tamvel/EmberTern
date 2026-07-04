namespace EmberTern.Core.Trace;

/// <summary>
/// The Activity Monitor's trace preset, as a pure Core DTO (a record, so callers can
/// derive a tweaked preset with <c>with</c>) — decoupled from the Firebird driver's
/// strongly-typed <c>FbDatabaseTraceConfiguration</c> so Core stays driver-free and a
/// V2 config editor is purely additive (settable props + future JSON round-trip). The
/// Firebird layer translates this into the driver's configuration object.
/// <para>
/// V1.1 ships one opinionated preset — <see cref="DefaultPreset"/> — plus a single
/// user-facing knob (<see cref="IncludeFunctions"/>, off by default). It never surfaces
/// a checkbox matrix (the explicit IBExpert anti-goal). The other knobs exist in the
/// model only so V2 can expose them without a schema change.
/// </para>
/// </summary>
public sealed record TraceSessionConfig
{
    /// <summary>Log DSQL statement executions (the workhorse; on by default).</summary>
    public bool IncludeStatements { get; init; } = true;

    /// <summary>Log stored-procedure executions.</summary>
    public bool IncludeProcedures { get; init; } = true;

    /// <summary>Log PSQL/stored-function executions. OFF by default (V1.1 noise reduction):
    /// on a real ERP the stream is dominated by built-in scalar functions (MOD, BIN_AND, …)
    /// that flood the buffer and drown the statements the user actually wants. Turning this
    /// off suppresses them at the SOURCE — the event mask never carries FUNCTION events — which
    /// also removes the ring-buffer-overflow risk. The user can consciously re-enable it (the
    /// "Include function calls" toggle) when profiling functions; note that user PSQL functions
    /// are hidden alongside the built-ins while this is off.</summary>
    public bool IncludeFunctions { get; init; }

    /// <summary>Log trigger executions.</summary>
    public bool IncludeTriggers { get; init; } = true;

    /// <summary>Log errors (always on in the default preset; error rows are pinned in the UI).</summary>
    public bool IncludeErrors { get; init; } = true;

    /// <summary>Log connection attach/detach. Off by default — noise for the ERP-reverse-engineering
    /// workflow; a de-emphasised lane in V2.</summary>
    public bool IncludeConnections { get; init; }

    /// <summary>Log transaction start/commit/rollback. Off by default; grouping already keys off the
    /// per-event transaction id without needing explicit transaction events.</summary>
    public bool IncludeTransactions { get; init; }

    /// <summary>Only record executions whose duration meets or exceeds this many milliseconds.
    /// 0 = record everything (the V1 default — the reverse-engineering workflow needs the fast
    /// statements too).</summary>
    public int TimeThresholdMs { get; init; }

    /// <summary>Cap on captured SQL text length (guards against pathological statements).</summary>
    public int MaxSqlLength { get; init; } = 65_536;

    /// <summary>Exclude EmberTern's own attachments from the stream. Enforced client-side by matching
    /// attachment ids (the driver config can only *include* one connection, not exclude ours), so this
    /// flag drives <see cref="TraceEvent.IsSelfActivity"/> filtering rather than the server config.</summary>
    public bool ExcludeSelfActivity { get; init; } = true;

    /// <summary>The opinionated V1.1 preset: statements + procedures + triggers + errors, NO
    /// function calls (noise reduction), no threshold, self-activity excluded. One button; the
    /// VM derives a copy with <c>with { IncludeFunctions = … }</c> when the user opts functions in.</summary>
    public static TraceSessionConfig DefaultPreset { get; } = new();
}
