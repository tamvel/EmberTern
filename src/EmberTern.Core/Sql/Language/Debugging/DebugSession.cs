using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

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
    private readonly List<Frame> _frames = new();
    private readonly BreakpointSet _breakpoints = new();
    private readonly List<IReadOnlyDictionary<string, object?>> _emittedRows = new();
    private int _nextFrameId;
    private IExecutableStatement? _currentStep;
    private DebugError? _error;

    public DebugSession(BlockStatement rootBody, IDebugExecutor executor, string? rootName = null)
    {
        _rootBody = rootBody ?? throw new ArgumentNullException(nameof(rootBody));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _rootName = string.IsNullOrEmpty(rootName) ? "(anonymous block)" : rootName!;
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

        PushFrame(_rootName, _rootBody, parent: null, callSite: null, initialValues: null);
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
                if (row is not null) { frame.Values.Apply(row); frame.PushBranch(f.Body); }
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
                    PushFrame(routine.Name, routine.Body, parent: frame, callSite: step, routine.InitialValues);
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
                        frame.Values.Apply(outcome.Writes);
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

            _executor.LeaveFrameSavepoint(_frames[^1].SavepointName); // normal frame exit (§4.5)
            _frames.RemoveAt(_frames.Count - 1);
        }
        return null;
    }

    private void PushFrame(
        string name, BlockStatement body, Frame? parent, IExecutableStatement? callSite,
        IReadOnlyDictionary<string, object?>? initialValues)
    {
        var frame = new Frame(_nextFrameId++, name, body, parent, callSite, initialValues);
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
