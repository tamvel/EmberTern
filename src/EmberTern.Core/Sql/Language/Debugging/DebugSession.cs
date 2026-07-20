using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// The debug engine core (milestone D1) — a client-side interpreter of PSQL <b>control flow</b> over the
/// AST, driving the server only through <see cref="IDebugExecutor"/>. It never evaluates an expression,
/// coerces a type, or decides a boolean: it walks blocks / <c>IF</c> / <c>WHILE</c> / <c>FOR</c> / leaves,
/// asks the executor to run each step point, and decides — as a pure function of (AST, frames, breakpoints,
/// command) — what to execute next and where to stop. Frames form a real call stack (spec §5); each frame
/// gets a SAVEPOINT on entry and its release on normal exit (spec §4.5). Pure Core: zero Avalonia, zero
/// FirebirdSql.
/// <para>
/// A raised statement/condition is routed through the <see cref="ExceptionRouter"/>: it unwinds frames (each
/// unhandled frame rolled back to its savepoint, §4.5) until a <c>WHEN … DO</c> handler matches, then
/// resumes at that handler's body; when nothing catches, every frame — the root included — is rolled back
/// and the session <see cref="DebugState.Faulted"/>s. Breakpoints (<see cref="Breakpoints"/>) are an
/// additional stop condition of the run commands. <see cref="BreakOnException"/> (D12, spec §9.8.1) inserts
/// one more stop point <i>before</i> that routing: a raise pauses at the raising statement, and the next
/// resume routes it through the very same path — the break is a pause, never an alternative handler.
/// </para>
/// </summary>
public sealed class DebugSession
{
    private readonly IDebugExecutor _executor;
    private readonly BlockStatement _rootBody;
    private readonly string _rootName;
    private readonly IReadOnlyDictionary<string, object?>? _rootValues;
    private readonly string? _rootSource;
    private readonly SemanticModel? _rootModel;
    private readonly List<Frame> _frames = new();
    private readonly BreakpointSet _breakpoints = new();
    private readonly DataBreakpointSet _dataBreakpoints = new();
    private readonly List<IReadOnlyDictionary<string, object?>> _emittedRows = new();
    private int _nextFrameId;
    private IExecutableStatement? _currentStep;
    private DebugError? _error;
    private Frame? _rootFrame;                 // retained so the terminal (Completed) snapshot survives frame-pop
    private IExecutableStatement? _lastStatement; // the last step point executed before normal completion
    private IExecutableStatement? _faultStatement; // the step point that raised the unhandled exception
    private List<Frame>? _faultStack;          // the call stack (innermost last) captured at the unhandled fault
    private (IExecutableStatement? Step, List<Frame> Stack)? _pendingRaise; // a raise held at a Break-on-Exception pause, routed on the next resume (§9.8.1)
    private DebugError? _conditionError;       // a conditional breakpoint whose condition RAISED on the stop that paused us (§9.8.2); cleared on resume
    private DataBreakpoint? _dataBreakpointHit; // the watched variable whose change paused us (§9.8.4); cleared on resume

    /// <summary>Creates a session over <paramref name="rootBody"/>. <paramref name="rootValues"/> seeds the
    /// root frame's initial values — the routine's <b>input parameter</b> arguments supplied at launch (§9.3):
    /// the root frame has no caller to provide them, so the launch does, exactly as a callee frame receives a
    /// call's arguments. Null (the default) starts every variable unassigned.</summary>
    public DebugSession(
        BlockStatement rootBody,
        IDebugExecutor executor,
        string? rootName = null,
        IReadOnlyDictionary<string, object?>? rootValues = null,
        string? rootSource = null,
        SemanticModel? rootModel = null)
    {
        _rootBody = rootBody ?? throw new ArgumentNullException(nameof(rootBody));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _rootName = string.IsNullOrEmpty(rootName) ? "(anonymous block)" : rootName!;
        _rootValues = rootValues;
        _rootSource = rootSource;
        _rootModel = rootModel;
        State = DebugState.Ready;
        StopReason = StopReason.NotStarted;
    }

    /// <summary>The session lifecycle state.</summary>
    public DebugState State { get; private set; }

    /// <summary>Why the session is currently paused / ended.</summary>
    public StopReason StopReason { get; private set; }

    /// <summary>The step point the session is paused ON (about to execute), or null when not paused.</summary>
    public IExecutableStatement? CurrentStatement => _currentStep;

    /// <summary>The innermost (current) frame, or null before <see cref="Start"/> / after completion.</summary>
    public Frame? CurrentFrame => _frames.Count > 0 ? _frames[^1] : null;

    /// <summary>The terminal frame after the routine ran to <see cref="DebugState.Completed"/> — the root frame,
    /// <b>retained</b> after its pop so its FINAL values (the last statement's write-back, callees'
    /// <c>RETURNING_VALUES</c>, a trigger's final NEW/OLD) can still be inspected. Null until the session
    /// completes (and for a fault — a fault rolls every frame back to its savepoint, so the values would be
    /// meaningless). Lets the UI keep the last state visible instead of the session "vanishing" at completion.</summary>
    public Frame? FinalFrame => State == DebugState.Completed ? _rootFrame : null;

    /// <summary>The last step point that executed before normal completion (the routine's final line), or null
    /// (not completed / an empty body). The UI marks it as the "here execution ended" line at
    /// <see cref="DebugState.Completed"/>.</summary>
    public IExecutableStatement? LastStatement => State == DebugState.Completed ? _lastStatement : null;

    /// <summary>The step point that raised the <b>unhandled</b> exception the session <see cref="DebugState.Faulted"/>
    /// on — the line the UI marks so the user sees exactly where it failed. Null unless faulted. The exception
    /// unwind rolls back each frame's <i>DB</i> savepoint but never its client-side variable values, so the
    /// frame snapshot below still shows the values as they were at the moment of the error (spec §4.5).</summary>
    public IExecutableStatement? FaultStatement => State == DebugState.Faulted ? _faultStatement : null;

    /// <summary>The innermost frame at the moment of the unhandled fault — the frame whose statement raised, with
    /// its variables as they were then. Null unless faulted. Retained after the unwind popped it (like
    /// <see cref="FinalFrame"/>), so the UI can keep the last state visible instead of clearing.</summary>
    public Frame? FaultFrame => State == DebugState.Faulted && _faultStack is { Count: > 0 } ? _faultStack[^1] : null;

    /// <summary>The call stack captured at the unhandled fault, innermost frame first — for the Call Stack panel
    /// in the Faulted state (the live <see cref="CallStack"/> is empty after the unwind). Empty unless faulted.</summary>
    public IReadOnlyList<Frame> FaultStack
    {
        get
        {
            if (State != DebugState.Faulted || _faultStack is null) return Array.Empty<Frame>();
            var stack = new Frame[_faultStack.Count];
            for (int i = 0; i < _faultStack.Count; i++) stack[i] = _faultStack[_faultStack.Count - 1 - i];
            return stack;
        }
    }

    /// <summary>The call stack, innermost frame first (spec §5 — frames are data).</summary>
    public IReadOnlyList<Frame> CallStack
    {
        get
        {
            var stack = new Frame[_frames.Count];
            for (int i = 0; i < _frames.Count; i++) stack[i] = _frames[_frames.Count - 1 - i];
            return stack;
        }
    }

    /// <summary>The current frame's depth (1 = root); 0 before start / after completion.</summary>
    public int Depth => _frames.Count;

    /// <summary>The error the session faulted on, or null (also null after a <c>WHEN</c> handler caught).</summary>
    public DebugError? CurrentError => _error;

    /// <summary>The active breakpoints — <see cref="Breakpoint"/> stop-policy objects keyed by step-point
    /// offset (D12). Mutable during the session: add / remove while paused, and set a condition or hit-count
    /// policy on an entry (<see cref="BreakpointSet.GetOrAdd"/>). A run command stops at the next step point
    /// whose breakpoint's policy is met (condition TRUE + hit count reached); a plain breakpoint stops every
    /// time.</summary>
    public BreakpointSet Breakpoints => _breakpoints;

    /// <summary>The error a conditional breakpoint's condition RAISED on the stop decision that paused the
    /// session (D12, spec §9.8.2 / §F): a broken condition never silently skips its breakpoint — the session
    /// stops (<see cref="StopReason.Breakpoint"/>) and the error is surfaced here so the user can fix it. Null
    /// unless the current pause was caused by a failed condition; cleared on the next resume.</summary>
    public DebugError? BreakpointConditionError => _conditionError;

    /// <summary>The data breakpoints — watch a variable, break when it changes (D12, spec §9.8.4). Mutable
    /// during the session; the change is detected locally (snapshot before a step, diff after) by
    /// <see cref="DataBreakpointSet"/>.</summary>
    public DataBreakpointSet DataBreakpoints => _dataBreakpoints;

    /// <summary>The watched variable whose change paused the session (<see cref="StopReason.DataBreakpoint"/>),
    /// or null when the current pause was not a data breakpoint. Cleared on the next resume. Lets the UI say
    /// which variable changed.</summary>
    public DataBreakpoint? DataBreakpointHit => _dataBreakpointHit;

    /// <summary>When true, a raised exception <b>pauses</b> the session at the raising statement — frame
    /// intact, <see cref="DebugState.Paused"/> with <see cref="StopReason.Exception"/> — <i>before</i> the
    /// raise is routed through the handler stack (spec §9.8.1). This is a stop point, <b>not</b> a fault and
    /// <b>not</b> a second exception mechanism: the next resume command routes the held raise through the
    /// <see cref="ExceptionRouter"/> along the <i>exact same</i> path it would have taken unbroken — a
    /// matching <c>WHEN … DO</c> catches it (the session continues at the handler), or nothing does and the
    /// session <see cref="DebugState.Faulted"/>s. Default false (route immediately — the pre-D12 behaviour).
    /// May be toggled at any time during the session (armed / disarmed while paused).</summary>
    public bool BreakOnException { get; set; }

    /// <summary>True while the session is paused on a Break-on-Exception stop (an exception has been raised
    /// but not yet routed — <see cref="BreakOnException"/>). The UI distinguishes this from an ordinary
    /// <see cref="StopReason.Step"/> pause so it can label it and offer "continue to route the exception".</summary>
    public bool IsPausedOnException => State == DebugState.Paused && _pendingRaise is not null;

    /// <summary>Rows emitted by <c>SUSPEND</c> so far, in order.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> EmittedRows => _emittedRows;

    /// <summary>Begins the session: pushes the root frame (with its entry savepoint) and pauses at the
    /// first step point (or completes immediately for an empty body).</summary>
    public void Start()
    {
        if (State != DebugState.Ready)
        {
            throw new InvalidOperationException("The debug session has already been started.");
        }

        PushFrame(_rootName, _rootBody, parent: null, lexicalParent: null, callSite: null,
            initialValues: _rootValues, outputParameterNames: null, source: _rootSource, model: _rootModel);
        _rootFrame = _frames[^1]; // retained for the terminal (Completed) snapshot, even after its pop
        _currentStep = AdvanceToNextStepPoint();
        if (_currentStep is null)
        {
            State = DebugState.Completed;
            StopReason = StopReason.Completed;
        }
        else
        {
            State = DebugState.Paused;
            StopReason = StopReason.Entry;
        }
    }

    /// <summary>Steps by a movement command (<see cref="StepKind.Into"/> / <see cref="StepKind.Over"/> /
    /// <see cref="StepKind.Out"/> / <see cref="StepKind.Continue"/>). Use <see cref="RunToCursor"/> and
    /// <see cref="SetNextStatement"/> for the targeted commands.</summary>
    public void Step(StepKind kind)
    {
        if (kind is StepKind.RunToCursor or StepKind.SetNext or StepKind.RunToSuspend)
        {
            throw new ArgumentException(
                "Use RunToCursor / SetNextStatement / RunToSuspend for their dedicated commands.", nameof(kind));
        }
        RunStepping(kind, targetOffset: null);
    }

    /// <summary>Runs until reaching the step point that begins at <paramref name="targetOffset"/> (or the
    /// session completes / faults). Calls execute in place (no descent), like Continue.</summary>
    public void RunToCursor(int targetOffset) => RunStepping(StepKind.RunToCursor, targetOffset);

    /// <summary>Runs at full speed (calls execute in place, like Continue) until the next <c>SUSPEND</c>
    /// emits a row, then pauses at the step point after it (<see cref="StopReason.Suspend"/>) — a selectable
    /// procedure's "give me the next row" (D12, spec §9.8). Resume it again for the following row; with no
    /// further <c>SUSPEND</c> the routine runs to completion. Breakpoints / data breakpoints still apply.
    /// The emitted rows accumulate in <see cref="EmittedRows"/>.</summary>
    public void RunToSuspend() => RunStepping(StepKind.RunToSuspend, targetOffset: null);

    /// <summary>Moves the instruction pointer to the step point beginning at <paramref name="targetOffset"/>
    /// within the current frame, executing nothing in between. Returns false (leaving the session where it
    /// was) when no such step point is reachable in the current frame's active blocks. Cannot un-execute
    /// side effects already performed (spec §9.6).</summary>
    public bool SetNextStatement(int targetOffset)
    {
        EnsurePaused();
        // Repositioning the IP explicitly abandons a Break-on-Exception raise held for routing: the user has
        // chosen a new control point, so the held exception is dropped (never routed on the next resume) and
        // is no longer the current error.
        _pendingRaise = null;
        _error = null;
        var frame = _frames[^1];
        var control = frame.Control;
        // Innermost active SequenceActivation that directly holds a step point at the target — pop back to
        // it and reposition. Covers moving within the current block or back to an enclosing one; it cannot
        // jump into a branch/loop body not currently entered (documented D1 limit).
        for (int i = control.Count - 1; i >= 0; i--)
        {
            if (control[i] is not SequenceActivation seq)
            {
                continue;
            }
            for (int j = 0; j < seq.Items.Count; j++)
            {
                if (seq.Items[j] is IExecutableStatement e && e.Start == targetOffset)
                {
                    while (!ReferenceEquals(frame.Top, seq))
                    {
                        frame.Pop();
                    }
                    seq.Index = j;
                    _currentStep = AdvanceToNextStepPoint();
                    State = _currentStep is null ? DebugState.Completed : DebugState.Paused;
                    StopReason = _currentStep is null ? StopReason.Completed : StopReason.Step;
                    return _currentStep is not null;
                }
            }
        }
        return false;
    }

    // ── Expression evaluation (spec §9.5 — Evaluate / Watches / Immediate) ────────────────────────

    /// <summary>Evaluates a <b>user-supplied fragment</b> against the current frame — the one engine behind
    /// the Evaluate / Watches / Immediate surfaces (decision 6). Requires the session be <b>Paused</b> (there
    /// is a live frame only while paused). An <see cref="EvaluationKind.Expression"/> yields a value and
    /// mutates nothing; an <see cref="EvaluationKind.Statement"/> runs against the live frame and its
    /// write-back is applied to that frame (the Immediate window operates on the live frame, spec §9.5). It
    /// is pure orchestration — the server work is the executor's harness (the same mechanism as a step); this
    /// method never evaluates, coerces, or interprets anything itself. The returned result carries the
    /// generated SQL for the Executed-SQL audit (§10.3).</summary>
    public EvaluationResult Evaluate(string fragment, EvaluationKind kind)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            throw new ArgumentException("The fragment to evaluate must be non-empty.", nameof(fragment));
        }
        EnsurePaused();

        var frame = _frames[^1];
        int scopeOffset = _currentStep?.Start ?? frame.Body.Start;
        var request = new EvaluationRequest(fragment.Trim(), kind, scopeOffset);

        var result = _executor.Evaluate(request, frame);
        if (kind == EvaluationKind.Statement && result.Success && result.Writes is { Count: > 0 })
        {
            ApplyWrites(frame, result.Writes); // the Immediate window operates on the live frame (§9.5)
        }
        return result;
    }

    // Applies a statement/cursor write-back set to the frame, routing each variable to the frame that OWNS it
    // up the lexical (closure) scope chain — a write to a captured outer variable lands in the declaring frame,
    // a write to a local lands here (spec §6.2b). For a non-closure frame (no lexical parent) every write lands
    // here, identical to a direct FrameValues.Apply.
    private static void ApplyWrites(Frame frame, IReadOnlyDictionary<string, object?>? writes)
    {
        if (writes is null) return;
        foreach (var kv in writes) frame.SetResolvedValue(kv.Key, kv.Value);
    }

    // ── The stepping loop ────────────────────────────────────────────────────────────────────────

    private void RunStepping(StepKind kind, int? targetOffset)
    {
        EnsurePaused();
        _conditionError = null;   // a fresh stop decision re-sets it if a condition raises again
        _dataBreakpointHit = null; // …likewise for a data breakpoint
        int startDepth = _frames.Count;

        while (true)
        {
            IReadOnlyDictionary<string, object?>? dataBefore = null; // watched values before this step (§9.8.4)
            int frameIdBefore = -1;
            int rowsBefore = _emittedRows.Count; // to detect a SUSPEND emitted by this step (run-to-SUSPEND)

            if (_pendingRaise is { } pending)
            {
                // Resuming from a Break-on-Exception pause (spec §9.8.1): route the held raise NOW — the SAME
                // ExceptionRouter path an un-broken raise takes (a pause, not a second handler). Nothing is
                // executed here; the statement already ran and raised before we paused, so the control stack is
                // exactly what it was at the raise. On catch, fall through to advance/stop just like the
                // immediate path below.
                _pendingRaise = null;
                if (RouteRaisedException(pending.Step, pending.Stack))
                {
                    return; // nothing caught it → Faulted (terminal)
                }
            }
            else
            {
                // Snapshot the watched variables in the frame about to execute this step — the "before" side of
                // the local data-breakpoint diff (DataBreakpointSet owns the detection; the loop only pairs this
                // snapshot with the after-check below).
                if (_dataBreakpoints.Count > 0)
                {
                    var frame = _frames[^1];
                    dataBefore = _dataBreakpoints.Snapshot(frame);
                    frameIdBefore = frame.Id;
                }

                if (ExecuteCurrent(kind))
                {
                    // A statement / condition raised. With Break-on-Exception armed, PAUSE here — before routing,
                    // frame intact — so the user sees where it raised; the next resume routes it (above). Snapshot
                    // the faulting line + call stack now, BEFORE any routing, exactly as the immediate route does.
                    if (BreakOnException)
                    {
                        _pendingRaise = (_currentStep, new List<Frame>(_frames));
                        State = DebugState.Paused;
                        StopReason = StopReason.Exception;
                        return;
                    }
                    // Disarmed: route immediately through the same one path (the pre-D12 behaviour).
                    if (RouteRaisedException(_currentStep, new List<Frame>(_frames)))
                    {
                        return; // nothing caught it → Faulted (terminal)
                    }
                }
            }

            bool suspended = _emittedRows.Count > rowsBefore; // a SUSPEND emitted a row during this step

            var justExecuted = _currentStep; // the step ExecuteCurrent just ran (before we advance past it)
            _currentStep = AdvanceToNextStepPoint();
            if (_currentStep is null)
            {
                _lastStatement = justExecuted; // the routine's final executed line — kept for the terminal marker
                State = DebugState.Completed;
                StopReason = StopReason.Completed;
                return;
            }

            // Data breakpoint (§9.8.4): a watched variable changed during the step just executed. Checked only
            // when the innermost frame is unchanged — a step-into/out crosses scopes, so the identity gate
            // prevents false positives (a cross-frame change on return is a documented boundary), mirroring the
            // Variables change-highlight. A changed watch wins over a line/step stop (the more specific reason).
            if (dataBefore is not null && _frames[^1].Id == frameIdBefore
                && _dataBreakpoints.FindChanged(dataBefore, _frames[^1]) is { } changed)
            {
                _dataBreakpointHit = changed;
                State = DebugState.Paused;
                StopReason = StopReason.DataBreakpoint;
                return;
            }

            // Run to next SUSPEND (§9.8): the run mode's target event. A SUSPEND emitted a row during the step
            // just executed — pause at the next step point (so the user can inspect the row + frame, then resume
            // for the following row). Placed right after the data-breakpoint check, mirroring it: both are "the
            // step just executed produced an event" stops that win over a line/breakpoint stop, and both share
            // the same coincidence boundary (a breakpoint exactly on the post-event step point is not separately
            // reported here; data breakpoint stays first, so a watched change on the same step is the more
            // specific reason). Only the RunToSuspend mode reacts — every other mode keeps the pre-C2 behaviour
            // (a SUSPEND during Continue just emits its row and keeps running).
            if (kind == StepKind.RunToSuspend && suspended)
            {
                State = DebugState.Paused;
                StopReason = StopReason.Suspend;
                return;
            }

            bool atBreakpoint = ShouldBreakAt(_currentStep);
            if (atBreakpoint || StepPlanner.ShouldStop(kind, targetOffset, startDepth, _frames.Count, _currentStep))
            {
                State = DebugState.Paused;
                StopReason = atBreakpoint ? StopReason.Breakpoint : StopReason.Step;
                return;
            }
        }
    }

    // Routes the current raise (_error) through the handler stack — the ONE exception-control-flow path
    // (spec §3.6/§4.5), whether the raise routes immediately or after a Break-on-Exception pause. The
    // faulting line + call-stack snapshot are captured BEFORE routing (passed in) and retained only if the
    // session faults: TryRoute pops and DB-rolls-back each unhandled frame, but never touches a frame's
    // client-side variable values, so the snapshot preserves the state at the moment of the error for the
    // Faulted terminal view. Returns true when nothing caught it and the session Faulted (the caller stops);
    // false when a WHEN … DO handler caught it — the router has repositioned control to the handler body,
    // _error is cleared, and the caller falls through to advance/stop per its command.
    private bool RouteRaisedException(IExecutableStatement? faultStep, List<Frame> stackAtFault)
    {
        if (!ExceptionRouter.TryRoute(_frames, _error!, _executor))
        {
            // Nothing caught it: every frame (root included) has been rolled back and popped.
            _faultStatement = faultStep;
            _faultStack = stackAtFault;
            _currentStep = null;
            State = DebugState.Faulted;
            StopReason = StopReason.Exception;
            return true;
        }
        // Caught: the router repositioned control to the matching handler's body. The exception is handled,
        // so the session is no longer faulted.
        _error = null;
        return false;
    }

    // The full D12 stop decision for a breakpoint at this step point (spec §9.8.2). A plain breakpoint stops
    // every time. A conditional one stops only when its boolean condition — evaluated through the SAME engine
    // as an IF/WHILE header and Evaluate/Watches (no second evaluator) — is TRUE, and only when its hit-count
    // policy is met at the resulting tally. A condition that yields NULL/false does not count and does not
    // stop (three-valued logic, as IF); one that RAISES stops and surfaces the error (never silently skipped,
    // §F). Returns false when no breakpoint is set here. The condition is evaluated against the frame about to
    // execute the step point (_frames[^1]) — the correct frame by construction.
    private bool ShouldBreakAt(IExecutableStatement step)
    {
        var bp = _breakpoints.Get(step.Start);
        if (bp is null)
        {
            return false;
        }

        bool conditionSatisfied = true;
        if (bp.HasCondition)
        {
            var outcome = _executor.EvaluateCondition(bp.Condition!.Trim(), step.Start, _frames[^1]);
            if (outcome.Error is not null)
            {
                _conditionError = outcome.Error; // a broken condition stops the session so the user can fix it
                return true;
            }
            conditionSatisfied = outcome.Value == true; // NULL / false → not-true
        }
        return bp.ShouldBreak(conditionSatisfied);
    }

    // Executes the current step point, advancing the control stack (consuming a leaf / evaluating a
    // condition and pushing the taken branch / fetching a row and pushing the loop body / pushing a frame
    // for a step-into). Returns true when it raised (the caller then routes it through the ExceptionRouter).
    private bool ExecuteCurrent(StepKind kind)
    {
        var frame = _frames[^1];
        var step = _currentStep!;

        // (1) Step Into a local FUNCTION whose call is the ENTIRE operand of a value-consuming position
        // (§6.4 — assignment RHS / RETURN operand / whole IF or WHILE condition). Recognition + the
        // position-specific return continuation are decided in ONE place (RecognizeStepInto); when the call
        // resolves to an in-scope local function, push a function frame that delivers its RETURN value to the
        // caller position on normal return. The caller's control flow is NOT advanced / branched now — the
        // continuation does that on return (ApplyReturnContinuation), so the assignment / branch fires exactly
        // once. An unresolved call (stored / built-in / package) falls through and runs on the server = a
        // 100%-faithful step-over (§5.3/§6.4). Step Over/Out/Continue ignore the call entirely (also fall through).
        if (kind == StepKind.Into
            && FunctionReturnContinuation.RecognizeStepInto(step) is { } into
            && _executor.ResolveFunction(into.Call, frame) is { } fn)
        {
            PushFrame(fn.Name, fn.Body, parent: frame, lexicalParent: fn.LexicalParent, callSite: step,
                fn.InitialValues, fn.OutputParameterNames, fn.Source, fn.Model,
                returnType: fn.ReturnType, returnContinuation: into.Continuation);
            return false;
        }

        // (2) A RETURN <expr> inside a FUNCTION frame is computed by the Expression Harness (a bare RETURN is
        // invalid inside EXECUTE BLOCK), records the frame's return value, and terminates the frame; its
        // continuation then delivers the value to the caller position (AdvanceToNextStepPoint). A step-into-able
        // RETURN f(x) was already handled by (1); this covers a plain RETURN <expr> and a stepped-over one.
        if (frame.IsFunctionFrame && step is PsqlLeafStatement { Kind: PsqlLeafKind.Return })
        {
            var ret = _executor.EvaluateReturn(step, frame);
            if (ret.Error is not null) { _error = ret.Error; return true; }
            frame.SetReturnValue(ret.Value);
            frame.TerminateForReturn();
            return false;
        }

        switch (step)
        {
            case IfStatement iff:
            {
                var cond = _executor.EvaluateCondition(iff, frame);
                if (cond.Error is not null) { _error = cond.Error; return true; }
                AdvanceSequence(frame);
                frame.PushBranch(cond.Value == true ? iff.Then : iff.Else);
                return false;
            }

            case WhileStatement w:
            {
                var cond = _executor.EvaluateCondition(w, frame);
                if (cond.Error is not null) { _error = cond.Error; return true; }
                if (cond.Value == true) frame.PushBranch(w.Body);
                else frame.Pop(); // WhileActivation done
                return false;
            }

            case ForSelectStatement f:
            {
                var fa = (ForActivation)frame.Top!;
                if (!fa.Opened) { fa.Cursor = _executor.OpenCursor(f, frame); fa.Opened = true; }
                var row = fa.Cursor!.FetchNext();
                if (row is not null) { ApplyWrites(frame, row); frame.PushBranch(f.Body); }
                else { fa.Cursor.Close(); frame.Pop(); } // ForActivation done
                return false;
            }

            default:
            {
                // A leaf / DML / EXECUTE PROCEDURE step point. Step Into a resolvable call pushes a frame;
                // every other case (Over/Out/Continue/RunToCursor, or an unresolvable call, or a non-call)
                // runs the statement on the server.
                if (kind == StepKind.Into
                    && step is ExecuteProcedureStatement
                    && _executor.ResolveRoutine(step, frame) is { } routine)
                {
                    AdvanceSequence(frame); // consume the call in the caller's block
                    PushFrame(routine.Name, routine.Body, parent: frame, lexicalParent: routine.LexicalParent,
                        callSite: step, routine.InitialValues, routine.OutputParameterNames, routine.Source, routine.Model);
                    return false;
                }

                var outcome = _executor.ExecuteStatement(step, frame);
                AdvanceSequence(frame);
                switch (outcome.Status)
                {
                    case ExecutionStatus.Raised:
                        _error = outcome.Error;
                        return true;
                    case ExecutionStatus.Suspended:
                        if (outcome.Writes is not null) _emittedRows.Add(outcome.Writes);
                        return false;
                    default:
                        ApplyWrites(frame, outcome.Writes);
                        return false;
                }
            }
        }
    }

    // The current leaf/IF step point sits at the top SequenceActivation's current index — consume it.
    private static void AdvanceSequence(Frame frame)
    {
        if (frame.Top is SequenceActivation seq)
        {
            seq.Index++;
        }
    }

    // Navigates to the next step point, popping (and releasing the savepoint of) any completed frame so the
    // caller resumes past its call. Null when every frame has completed.
    private IExecutableStatement? AdvanceToNextStepPoint()
    {
        while (_frames.Count > 0)
        {
            var step = _frames[^1].NextStepPoint();
            if (step is not null) return step;

            var completed = _frames[^1];
            ApplyReturningValues(completed);                    // a procedure callee's outputs → RETURNING_VALUES (§5)
            _executor.LeaveFrameSavepoint(completed.SavepointName); // normal frame exit (§4.5)
            _frames.RemoveAt(_frames.Count - 1);
            ApplyReturnContinuation(completed);                 // a function callee's RETURN value → caller position (§6.4)
        }
        return null;
    }

    // On a FUNCTION frame's NORMAL return (§6.4, D9 seam c), deliver its RETURN value to the caller statement
    // that stepped into it — the ONE place the four continuation variants resume the caller's control flow,
    // generalising ApplyReturningValues (a procedure delivers named outputs; a function delivers one value per
    // the call position). A non-function frame (root / procedure / EXECUTE BLOCK) has no continuation → no-op.
    // An unhandled unwind never reaches here (the ExceptionRouter rolls those frames back), so a raised function
    // never fires its continuation — identical to a procedure.
    private void ApplyReturnContinuation(Frame completed)
    {
        if (completed.ReturnContinuation is not { } continuation) return;
        if (completed.Parent is not { } caller) return;
        object? value = completed.ReturnValue;

        switch (continuation)
        {
            case FunctionReturnContinuation.AssignTo assign:
                caller.SetResolvedValue(assign.Target, value); // v = f(x)
                AdvanceSequence(caller);                        // consume the assignment leaf (not advanced at push)
                break;

            case FunctionReturnContinuation.SetFrameReturn:
                caller.SetReturnValue(value);                   // RETURN f(x): becomes the caller's own return value
                caller.TerminateForReturn();                    // the caller's RETURN completes → its continuation fires next
                break;

            case FunctionReturnContinuation.BranchIf branch:
                AdvanceSequence(caller);                        // consume the IF header (not advanced at push)
                caller.PushBranch(value as bool? == true ? branch.Node.Then : branch.Node.Else);
                break;

            case FunctionReturnContinuation.DecideWhile loop:
                if (value as bool? == true) caller.PushBranch(loop.Node.Body); // enter the body this iteration
                else caller.Pop();                                             // the WhileActivation is done
                break;
        }
    }

    // On a callee frame's NORMAL return, copy its output parameters into the caller's RETURNING_VALUES targets
    // (spec §5 — a real EXECUTE PROCEDURE binds its outputs into the caller's variables; a simulated frame
    // reconstructs that client-side from the callee's own values). Positional: the i-th output parameter →
    // the i-th RETURNING_VALUES target. A no-op for the root, a call with no RETURNING_VALUES, or a routine
    // with no outputs; an unhandled unwind never reaches here (the ExceptionRouter rolls those frames back).
    // Zips to the shorter list on a malformed pair — never throws (§0 tolerance).
    private static void ApplyReturningValues(Frame completed)
    {
        if (completed.Parent is not { } caller) return;
        if (completed.CallSite is not ExecuteProcedureStatement call) return;

        var targets = call.ReturningTargets;
        var outputs = completed.OutputParameterNames;
        int n = Math.Min(targets.Count, outputs.Count);
        for (int i = 0; i < n; i++)
        {
            object? value = completed.Values.TryGet(outputs[i], out var v) ? v : null;
            caller.SetResolvedValue(targets[i], value);
        }
    }

    private void PushFrame(
        string name, BlockStatement body, Frame? parent, Frame? lexicalParent, IExecutableStatement? callSite,
        IReadOnlyDictionary<string, object?>? initialValues, IReadOnlyList<string>? outputParameterNames,
        string? source, SemanticModel? model,
        string? returnType = null, FunctionReturnContinuation? returnContinuation = null)
    {
        var frame = new Frame(
            _nextFrameId++, name, body, parent, lexicalParent, callSite, initialValues, outputParameterNames,
            source, model, returnType, returnContinuation);
        _frames.Add(frame);
        _executor.EnterFrameSavepoint(frame.SavepointName); // SAVEPOINT on frame entry (§4.5)
    }

    private void EnsurePaused()
    {
        if (State != DebugState.Paused)
        {
            throw new InvalidOperationException($"Cannot step: the session is {State}, not Paused.");
        }
    }
}
