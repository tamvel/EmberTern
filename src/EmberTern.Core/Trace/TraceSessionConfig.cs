namespace EmberTern.Core.Trace;

/// <summary>
/// The Activity Monitor's trace preset, as a pure Core DTO — decoupled from the
/// Firebird driver's strongly-typed <c>FbDatabaseTraceConfiguration</c> so Core
/// stays driver-free and a V2 config editor is purely additive (settable props +
/// future JSON round-trip). The (M2) Firebird layer translates this into the
/// driver's configuration object.
/// <para>
/// V1 ships exactly one preset — <see cref="DefaultPreset"/> — and never surfaces
/// a checkbox matrix (the explicit IBExpert anti-goal). The knobs exist in the
/// model only so V2 can expose them without a schema change.
/// </para>
/// </summary>
public sealed class TraceSessionConfig
{
    /// <summary>Log DSQL statement executions (the workhorse; on by default).</summary>
    public bool IncludeStatements { get; init; } = true;

    /// <summary>Log stored-procedure executions.</summary>
    public bool IncludeProcedures { get; init; } = true;

    /// <summary>Log PSQL/stored-function executions.</summary>
    public bool IncludeFunctions { get; init; } = true;

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

    /// <summary>The single opinionated V1 preset: statements + procedures + functions + triggers +
    /// errors, no threshold, self-activity excluded. One button, no configuration.</summary>
    public static TraceSessionConfig DefaultPreset { get; } = new();
}
