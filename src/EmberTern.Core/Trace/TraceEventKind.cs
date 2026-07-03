namespace EmberTern.Core.Trace;

/// <summary>
/// The category of a single Activity Monitor (trace) event, curated from the raw
/// Firebird trace event types. START/FINISH pairs from the raw stream
/// (<c>EXECUTE_PROCEDURE_START</c>/<c>_FINISH</c>, etc.) are folded by the engine
/// into ONE <see cref="TraceEvent"/> of the matching kind — the raw *_START marker
/// is never its own event. Reverse-engineering, performance, and error diagnosis
/// all key off this + <see cref="TraceEventSeverity"/>.
/// </summary>
public enum TraceEventKind
{
    /// <summary>A DSQL statement execution (raw <c>EXECUTE_STATEMENT_FINISH</c>). The workhorse.</summary>
    Statement,

    /// <summary>A stored procedure execution (folded <c>EXECUTE_PROCEDURE_START</c>+<c>_FINISH</c>).</summary>
    Procedure,

    /// <summary>A trigger execution (folded <c>EXECUTE_TRIGGER_START</c>+<c>_FINISH</c>).</summary>
    Trigger,

    /// <summary>A stored/PSQL function execution (folded <c>EXECUTE_FUNCTION_START</c>+<c>_FINISH</c>).</summary>
    Function,

    /// <summary>A connection lifecycle event (attach/detach). De-emphasised in the default view.</summary>
    Connection,

    /// <summary>A transaction lifecycle event (start/commit/rollback). De-emphasised in the default view.</summary>
    Transaction,

    /// <summary>A trace-session/system marker (e.g. <c>TRACE_INIT</c>). Hidden from the default statement view.</summary>
    System,
}
