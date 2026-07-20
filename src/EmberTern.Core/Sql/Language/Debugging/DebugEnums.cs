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

    /// <summary>Stopped on an <b>unhandled</b> exception — no <c>WHEN … DO</c> handler in any frame matched,
    /// so the <see cref="ExceptionRouter"/> rolled every frame back to its savepoint (spec §4.5) and the
    /// session ended here. A raise that a handler catches does NOT reach this state.</summary>
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

    /// <summary>Run until the session completes (or hits a breakpoint / an unhandled exception).</summary>
    Continue,

    /// <summary>Run at full speed (calls execute in place, like Continue) until the next <c>SUSPEND</c>
    /// emits a row — a selectable procedure's "give me the next row" (D12, spec §9.8). Stops with
    /// <see cref="StopReason.Suspend"/> at the step point after the <c>SUSPEND</c>; breakpoints and data
    /// breakpoints still apply as additional stop conditions, and with no further <c>SUSPEND</c> the routine
    /// runs to completion. The result grid over <see cref="DebugSession.EmittedRows"/> is a UI concern.</summary>
    RunToSuspend,

    /// <summary>Run at full speed (like Continue) until the <b>innermost enclosing loop</b> — the loop the
    /// caret is currently inside — is left, then stop at the first step point after it (D13 "Continue Until
    /// Loop Exit"). "Left" covers every exit path: the loop condition going false / the cursor being
    /// exhausted, an <c>EXIT</c>, and an unlabeled <c>LEAVE</c>/<c>BREAK</c>. The stop is the loop-lifecycle
    /// event (the loop activation leaving the control stack), detected in <see cref="DebugSession"/> — not a
    /// movement decision, so <see cref="StepPlanner"/> returns false for it. Breakpoints inside the loop
    /// still win (the pre-execute gate). Only valid while <see cref="DebugSession.IsInsideLoop"/>.</summary>
    RunToLoopExit,

    /// <summary>Run at full speed (like Continue) until the innermost enclosing loop <b>begins its next
    /// iteration</b>, then stop at the first step point of that iteration's body (D13 "Next Iteration"); if
    /// the loop exits first (no next iteration) it stops after the loop exactly as
    /// <see cref="RunToLoopExit"/>. The stop is the loop entering a further iteration (its iteration counter
    /// incrementing) or leaving the control stack — a loop-lifecycle event detected in
    /// <see cref="DebugSession"/>, so <see cref="StepPlanner"/> returns false for it. Breakpoints inside the
    /// loop still win. Only valid while <see cref="DebugSession.IsInsideLoop"/>.</summary>
    RunToNextIteration,
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

    /// <summary>Stopped because the next step point's offset is in <see cref="DebugSession.Breakpoints"/>.</summary>
    Breakpoint,

    /// <summary>An exception is why the session stopped. Paired with <see cref="DebugState.Faulted"/> it is
    /// terminal — an unhandled raise rolled every frame back (the pre-D12 meaning). Paired with
    /// <see cref="DebugState.Paused"/> it is a <b>Break-on-Exception</b> stop (D12, spec §9.8.1): the raise
    /// is paused at its statement, frame intact, and will be routed on the next resume. The pair
    /// (<see cref="DebugState"/>, this) tells the two apart; see <see cref="DebugSession.IsPausedOnException"/>.</summary>
    Exception,

    /// <summary>Stopped after a <c>SUSPEND</c> emitted a row.</summary>
    Suspend,

    /// <summary>Stopped because a <b>watched variable changed</b> during the step just executed — a data
    /// breakpoint (D12, spec §9.8.4). The variable is <see cref="DebugSession.DataBreakpointHit"/>.</summary>
    DataBreakpoint,

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
