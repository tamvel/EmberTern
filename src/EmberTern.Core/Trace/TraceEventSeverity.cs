namespace EmberTern.Core.Trace;

/// <summary>
/// Presentation-independent severity of a trace event, assigned by the engine.
/// Drives the grid badge, error pinning (errors are never hidden by the quick
/// filter), and the slow-statement tint. Ordered so a higher value = more urgent.
/// </summary>
public enum TraceEventSeverity
{
    /// <summary>De-emphasised system/connection/transaction marker.</summary>
    System = 0,

    /// <summary>Ordinary execution.</summary>
    Normal = 1,

    /// <summary>Execution whose duration exceeded the session's slow threshold.</summary>
    Slow = 2,

    /// <summary>The event carried an error (raw <c>ERROR</c> / a failed statement).</summary>
    Error = 3,
}
