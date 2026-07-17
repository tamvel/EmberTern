namespace EmberTern.Core.Sql.Debugging;

// Stage X — Firebird Debugger, milestone D1 (the debug engine core). The interpreter OWNS control flow;
// the server owns semantics. This whole namespace is pure Core: zero Avalonia, zero FirebirdSql. The only
// seam to the server is IDebugExecutor (a contract Core requires — the precedented rule-#2 exception, like
// ISqlMetadataProvider). D1 is proven with a fake executor, because control flow is the part we can get
// wrong. See docs/design/firebird-debugger.md §3.1/§3.7 and the implementation plan's D1 brief.

/// <summary>The debug session's lifecycle state.</summary>
public enum DebugState
{
    /// <summary>Created, not started — <see cref="DebugSession.Start"/> has not run.</summary>
    Ready,

    /// <summary>Stopped at a step point, awaiting the next command.</summary>
    Paused,

    /// <summary>Finished normally — the root frame ran to completion.</summary>
    Completed,

    /// <summary>Stopped on an exception. In D1 (seam a) an unhandled raise ends the session here; routing
    /// and frame unwinding are seam b (<c>ExceptionRouter</c>).</summary>
    Faulted,
}

/// <summary>A stepping command — the user's intent for the next move.</summary>
public enum StepKind
{
    /// <summary>Execute the current statement; descend into a call (push a simulated frame). Stop at the
    /// next step point (which may be inside the callee).</summary>
    Into,

    /// <summary>Execute the current statement; a call runs on the server (no descent). Stop at the next
    /// step point in the current frame.</summary>
    Over,

    /// <summary>Run the rest of the current frame at full speed; stop at the caller's next step point when
    /// the current frame returns.</summary>
    Out,

    /// <summary>Run until reaching a target statement span (Run To Cursor).</summary>
    RunToCursor,

    /// <summary>Move the instruction pointer to a target statement without executing anything in
    /// between (Set Next Statement).</summary>
    SetNext,

    /// <summary>Run until the session completes (or, in seam b, hits a breakpoint).</summary>
    Continue,
}

/// <summary>Why the session is currently paused (or ended).</summary>
public enum StopReason
{
    /// <summary>Not started yet.</summary>
    NotStarted,

    /// <summary>Stopped at the first step point after <see cref="DebugSession.Start"/>.</summary>
    Entry,

    /// <summary>Stopped after a step command reached the next step point.</summary>
    Step,

    /// <summary>Stopped at a breakpoint (seam b).</summary>
    Breakpoint,

    /// <summary>Stopped because a statement raised an exception.</summary>
    Exception,

    /// <summary>Stopped after a <c>SUSPEND</c> emitted a row.</summary>
    Suspend,

    /// <summary>The session ran to completion.</summary>
    Completed,
}

/// <summary>The outcome status of executing one statement on the server (via <see cref="IDebugExecutor"/>).</summary>
public enum ExecutionStatus
{
    /// <summary>Ran normally.</summary>
    Normal,

    /// <summary>Raised an exception (carried in <see cref="StatementOutcome.Error"/>).</summary>
    Raised,

    /// <summary>Executed a <c>SUSPEND</c> — emitted a row and paused the routine's output.</summary>
    Suspended,
}
