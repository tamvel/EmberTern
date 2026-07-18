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
/// additional stop condition of the run commands.
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
    private readonly List<IReadOnlyDictionary<string, object?>> _emittedRows = new();
    private int _nextFrameId;
    private IExecutableStatement? _currentStep;
    private DebugError? _error;

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

    /// <summary>The active breakpoints (offsets of step points). Mutable during the session — add or remove
    /// while paused; a run command stops at the next step point whose offset is set here.</summary>
    public BreakpointSet Breakpoints => _breakpoints;

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
        if (kind is StepKind.RunToCursor or StepKind.SetNext)
        {
            throw new ArgumentException("Use RunToCursor / SetNextStatement for targeted commands.", nameof(kind));
        }
        RunStepping(kind, targetOffset: null);
    }

    /// <summary>Runs until reaching the step point that begins at <paramref name="targetOffset"/> (or the
    /// session completes / faults). Calls execute in place (no descent), like Continue.</summary>
    public void RunToCursor(int targetOffset) => RunStepping(StepKind.RunToCursor, targetOffset);

    /// <summary>Moves the instruction pointer to the step point beginning at <paramref name="targetOffset"/>
    /// within the current frame, executing nothing in between. Returns false (leaving the session where it
    /// was) when no such step point is reachable in the current frame's active blocks. Cannot un-execute
    /// side effects already performed (spec §9.6).</summary>
    public bool SetNextStatement(int targetOffset)
    {
        EnsurePaused();
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
        int startDepth = _frames.Count;

        while (true)
        {
            if (ExecuteCurrent(kind))
            {
                // A statement / condition raised — route it through the handler stack (spec §3.6/§4.5).
                if (!ExceptionRouter.TryRoute(_frames, _error!, _executor))
                {
                    // Nothing caught it: every frame (root included) has been rolled back and popped.
                    _currentStep = null;
                    State = DebugState.Faulted;
                    StopReason = StopReason.Exception;
                    return;
                }
                // Caught: the router repositioned control to the matching handler's body. The exception is
                // handled, so the session is no longer faulted; fall through to stop/continue per the command.
                _error = null;
            }

            _currentStep = AdvanceToNextStepPoint();
            if (_currentStep is null)
            {
                State = DebugState.Completed;
                StopReason = StopReason.Completed;
                return;
            }

            bool atBreakpoint = _breakpoints.Contains(_currentStep.Start);
            if (atBreakpoint || StepPlanner.ShouldStop(kind, targetOffset, startDepth, _frames.Count, _currentStep))
            {
                State = DebugState.Paused;
                StopReason = atBreakpoint ? StopReason.Breakpoint : StopReason.Step;
                return;
            }
        }
    }

    // Executes the current step point, advancing the control stack (consuming a leaf / evaluating a
    // condition and pushing the taken branch / fetching a row and pushing the loop body / pushing a frame
    // for a step-into). Returns true when it raised (the caller then routes it through the ExceptionRouter).
    private bool ExecuteCurrent(StepKind kind)
    {
        var frame = _frames[^1];
        var step = _currentStep!;

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

            ApplyReturningValues(_frames[^1]);                              // bind the callee's outputs back (§5)
            _executor.LeaveFrameSavepoint(_frames[^1].SavepointName);       // normal frame exit (§4.5)
            _frames.RemoveAt(_frames.Count - 1);
        }
        return null;
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
        string? source, SemanticModel? model)
    {
        var frame = new Frame(
            _nextFrameId++, name, body, parent, lexicalParent, callSite, initialValues, outputParameterNames, source, model);
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
