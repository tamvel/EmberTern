using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, milestone D1: the debug engine core. These tests drive
/// <see cref="DebugSession"/> against a scripted fake <see cref="IDebugExecutor"/> — no server, no UI —
/// which is the point: control flow is the part the client OWNS, so it is where correctness is cheapest to
/// pin. They cover step ordering over block / IF / WHILE / FOR / leaf, nested frames (Into/Over/Out),
/// Continue, Run To Cursor, Set Next Statement, savepoint enter/release at frame boundaries, the scope
/// chain, SUSPEND rows, and — seam b — exception routing (handler matching per WHEN form, propagation +
/// frame unwind with savepoint rollback, an unhandled raise faulting after rolling every frame back,
/// re-raise, a handler not catching its own body's exception, cursor cleanup on unwind) and breakpoints.
/// </summary>
public class DebugEngineTests
{
    // ── Test harness ─────────────────────────────────────────────────────────────────────────────

    private static BlockStatement Body(string sql)
    {
        var root = SqlParser.Parse(sql).Root;
        var block = Assert.IsType<AnonymousBlockStatement>(root.Statements[0]);
        Assert.NotNull(block.Body);
        return block.Body!;
    }

    private static int Off(string sql, string sub)
    {
        int i = sql.IndexOf(sub, StringComparison.Ordinal);
        Assert.True(i >= 0, $"substring not found: {sub}");
        return i;
    }

    private static string Text(string sql, IExecutableStatement e) => sql.Substring(e.Start, e.Length);

    // Steps repeatedly with `kind`, collecting the source text of each step point paused on (starting at
    // Entry), until the session is no longer Paused. All step points share `sql`'s offsets (single frame).
    private static List<string> Trace(DebugSession s, string sql, StepKind kind)
    {
        s.Start();
        var trace = new List<string>();
        int guard = 0;
        while (s.State == DebugState.Paused)
        {
            Assert.True(guard++ < 1000, "runaway stepping");
            trace.Add(Text(sql, s.CurrentStatement!));
            s.Step(kind);
        }
        return trace;
    }

    private static Dictionary<string, object?> Row(string name, object? value)
        => new(StringComparer.OrdinalIgnoreCase) { [name] = value };

    // A scripted fake executor: records every call, and returns scripted condition results / statement
    // outcomes / cursor rows / routine resolutions keyed by the AST node's source Start offset.
    private sealed class FakeExecutor : IDebugExecutor
    {
        private readonly bool _defaultCondition;
        private readonly Dictionary<int, Queue<bool>> _conds = new();
        private readonly Dictionary<int, Queue<StatementOutcome>> _outcomes = new();
        private readonly Dictionary<int, List<IReadOnlyDictionary<string, object?>>> _cursorRows = new();
        private readonly Dictionary<int, DebugRoutine> _routines = new();
        private readonly HashSet<int> _localClosures = new();

        public FakeExecutor(bool defaultCondition = true) => _defaultCondition = defaultCondition;

        public List<int> Executed { get; } = new();
        public List<int> ConditionsEvaluated { get; } = new();
        public List<string> Savepoints { get; } = new();
        public List<FakeCursor> Cursors { get; } = new();

        public FakeExecutor Cond(int start, params bool[] values)
        {
            _conds[start] = new Queue<bool>(values);
            return this;
        }

        public FakeExecutor Outcome(int start, StatementOutcome outcome)
        {
            if (!_outcomes.TryGetValue(start, out var q)) _outcomes[start] = q = new Queue<StatementOutcome>();
            q.Enqueue(outcome);
            return this;
        }

        public FakeExecutor CursorAt(int start, params IReadOnlyDictionary<string, object?>[] rows)
        {
            _cursorRows[start] = rows.ToList();
            return this;
        }

        // Registers a callee for step-into at `start`. asLocalClosure = true makes it a LOCAL sub-routine
        // (its frame's lexical parent = the caller frame, a closure — the D9 mechanism); the default is a
        // stored routine (a closed scope — the D8 default, lexical parent null).
        public FakeExecutor RoutineAt(int start, DebugRoutine routine, bool asLocalClosure = false)
        {
            _routines[start] = routine;
            if (asLocalClosure) _localClosures.Add(start);
            return this;
        }

        public StatementOutcome ExecuteStatement(IExecutableStatement statement, Frame frame)
        {
            Executed.Add(statement.Start);
            if (_outcomes.TryGetValue(statement.Start, out var q) && q.Count > 0) return q.Dequeue();
            return StatementOutcome.Normal();
        }

        public ConditionOutcome EvaluateCondition(IExecutableStatement owner, Frame frame)
        {
            ConditionsEvaluated.Add(owner.Start);
            if (_conds.TryGetValue(owner.Start, out var q) && q.Count > 0) return ConditionOutcome.Of(q.Dequeue());
            return ConditionOutcome.Of(_defaultCondition);
        }

        // ── D12 breakpoint conditions (a user-supplied boolean fragment) ──
        // Scripted per fragment as a queue of outcomes (null value = NULL condition); an unscripted fragment
        // defaults to TRUE (an "always break" conditional needs no script). CondFragmentRaises scripts an error.
        private readonly Dictionary<string, Queue<ConditionOutcome>> _condFragments = new(StringComparer.OrdinalIgnoreCase);
        public List<string> ConditionFragmentsEvaluated { get; } = new();

        public FakeExecutor CondFragment(string fragment, params bool?[] values)
        {
            _condFragments[fragment] = new Queue<ConditionOutcome>(
                values.Select(v => v is bool b ? ConditionOutcome.Of(b) : new ConditionOutcome(null)));
            return this;
        }

        public FakeExecutor CondFragmentRaises(string fragment, DebugError error)
        {
            _condFragments[fragment] = new Queue<ConditionOutcome>(new[] { ConditionOutcome.Raised(error) });
            return this;
        }

        public ConditionOutcome EvaluateCondition(string fragment, int scopeOffset, Frame frame)
        {
            ConditionFragmentsEvaluated.Add(fragment);
            if (_condFragments.TryGetValue(fragment, out var q) && q.Count > 0) return q.Dequeue();
            return ConditionOutcome.True;
        }

        // ── D5 evaluation surface ──
        private readonly Dictionary<string, EvaluationResult> _evals = new(StringComparer.OrdinalIgnoreCase);
        public List<EvaluationRequest> Evaluations { get; } = new();

        public FakeExecutor EvalReturns(string fragment, EvaluationResult result) { _evals[fragment] = result; return this; }

        public EvaluationResult Evaluate(EvaluationRequest request, Frame frame)
        {
            Evaluations.Add(request);
            if (_evals.TryGetValue(request.Fragment, out var scripted)) return scripted;
            return EvaluationResult.Ok($"/*eval*/ {request.Fragment}", request.Fragment, null);
        }

        public IDebugCursor OpenCursor(ForSelectStatement loop, Frame frame)
        {
            var rows = _cursorRows.TryGetValue(loop.Start, out var r) ? r : new List<IReadOnlyDictionary<string, object?>>();
            var cursor = new FakeCursor(rows);
            Cursors.Add(cursor);
            return cursor;
        }

        public DebugRoutine? ResolveRoutine(IExecutableStatement call, Frame frame)
        {
            if (!_routines.TryGetValue(call.Start, out var routine)) return null;
            // A local sub-routine closes over its declaring (caller) frame — rebuild it with that lexical
            // parent (D9 mechanism). A stored routine keeps a null lexical parent (a closed scope — D8).
            return _localClosures.Contains(call.Start)
                ? new DebugRoutine(routine.Name, routine.Body, routine.InitialValues, routine.OutputParameterNames, lexicalParent: frame)
                : routine;
        }

        // ── D9 seam c: local-function step-into (§6.4) ──
        private readonly Dictionary<string, DebugRoutine> _functions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Queue<object?>> _returns = new();
        public List<string> FunctionsResolved { get; } = new();
        public List<int> ReturnsEvaluated { get; } = new();

        // Registers a local FUNCTION resolvable by name for step-into. Its frame closes over the resolving
        // (declaring) frame — a closure, spec §6 — mirroring the real local-function resolution.
        public FakeExecutor FunctionNamed(string name, DebugRoutine routine) { _functions[name] = routine; return this; }

        // Scripts the value(s) EvaluateReturn yields for the RETURN leaf at `start`, one per RETURN execution
        // (so a WHILE condition function can return true then false across iterations).
        public FakeExecutor ReturnAt(int start, params object?[] values)
        {
            _returns[start] = new Queue<object?>(values);
            return this;
        }

        public DebugRoutine? ResolveFunction(CallExpression call, Frame frame)
        {
            if (call.Name is null || !_functions.TryGetValue(call.Name, out var routine)) return null;
            FunctionsResolved.Add(call.Name);
            return new DebugRoutine(routine.Name, routine.Body, routine.InitialValues, routine.OutputParameterNames,
                lexicalParent: frame, returnType: routine.ReturnType ?? "integer");
        }

        public ReturnOutcome EvaluateReturn(IExecutableStatement returnStatement, Frame frame)
        {
            ReturnsEvaluated.Add(returnStatement.Start);
            if (_returns.TryGetValue(returnStatement.Start, out var q) && q.Count > 0) return ReturnOutcome.Of(q.Dequeue());
            return ReturnOutcome.Of(null);
        }

        public void EnterFrameSavepoint(string name) => Savepoints.Add("enter:" + name);

        public void LeaveFrameSavepoint(string name) => Savepoints.Add("leave:" + name);

        public void RollbackFrameSavepoint(string name) => Savepoints.Add("rollback:" + name);
    }

    private sealed class FakeCursor : IDebugCursor
    {
        private readonly List<IReadOnlyDictionary<string, object?>> _rows;
        private int _index;

        public FakeCursor(List<IReadOnlyDictionary<string, object?>> rows) => _rows = rows;

        public bool Closed { get; private set; }

        public IReadOnlyDictionary<string, object?>? FetchNext()
            => _index < _rows.Count ? _rows[_index++] : null;

        public void Close() => Closed = true;
    }

    // ── Step ordering: leaves / IF / WHILE / FOR ───────────────────────────────────────────────────

    [Fact]
    public void LeafSequence_StepsInSourceOrder()
    {
        const string sql = "begin a = 1; b = 2; c = 3; end";
        var s = new DebugSession(Body(sql), new FakeExecutor());
        Assert.Equal(new[] { "a = 1;", "b = 2;", "c = 3;" }, Trace(s, sql, StepKind.Into));
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Equal(StopReason.Completed, s.StopReason);
    }

    [Fact]
    public void StartPausesAtFirstStepPoint_WithEntryReason()
    {
        const string sql = "begin a = 1; b = 2; end";
        var s = new DebugSession(Body(sql), new FakeExecutor());
        s.Start();
        Assert.Equal(DebugState.Paused, s.State);
        Assert.Equal(StopReason.Entry, s.StopReason);
        Assert.Equal("a = 1;", Text(sql, s.CurrentStatement!));
        Assert.Equal(1, s.Depth);
    }

    [Fact]
    public void EmptyBody_CompletesImmediately()
    {
        const string sql = "begin end";
        var s = new DebugSession(Body(sql), new FakeExecutor());
        s.Start();
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Null(s.CurrentStatement);
    }

    [Fact]
    public void Completed_RetainsTerminalFrame_AndLastStatement()
    {
        // The UI keeps the last state visible at Completed (instead of the session "vanishing"): the engine
        // retains the terminal (root) frame + the last executed line, while the LIVE frame is still popped.
        const string sql = "begin a = 1; b = 2; end";
        var s = new DebugSession(Body(sql), new FakeExecutor());
        s.Start();

        Assert.Equal(DebugState.Paused, s.State);
        Assert.Null(s.FinalFrame);    // no terminal snapshot while the session is running
        Assert.Null(s.LastStatement);

        s.Step(StepKind.Over); // a = 1 → paused at b = 2
        s.Step(StepKind.Over); // b = 2 → completed

        Assert.Equal(DebugState.Completed, s.State);
        Assert.Null(s.CurrentFrame);  // the live frame is popped — the existing contract is unchanged
        Assert.NotNull(s.FinalFrame); // …but the terminal frame is retained for inspection
        Assert.Equal("b = 2;", Text(sql, s.LastStatement!)); // the routine's final executed line
    }

    [Fact]
    public void If_TrueBranch_IsTaken_ElseSkipped()
    {
        const string sql = "begin if (c) then t = 1; else f = 2; end";
        var exec = new FakeExecutor().Cond(Off(sql, "if (c)"), true);
        var s = new DebugSession(Body(sql), exec);
        // Step points: the IF (its span covers the whole if/else construct), then the taken THEN leaf.
        Assert.Equal(new[] { "if (c) then t = 1; else f = 2;", "t = 1;" }, Trace(s, sql, StepKind.Into));
        Assert.Contains(Off(sql, "t = 1"), exec.Executed);
        Assert.DoesNotContain(Off(sql, "f = 2"), exec.Executed);
    }

    [Fact]
    public void If_FalseBranch_TakesElse()
    {
        const string sql = "begin if (c) then t = 1; else f = 2; end";
        var exec = new FakeExecutor().Cond(Off(sql, "if (c)"), false);
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Into); // execute the IF header
        Assert.Equal("f = 2;", Text(sql, s.CurrentStatement!));
        s.Step(StepKind.Into);
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Contains(Off(sql, "f = 2"), exec.Executed);
        Assert.DoesNotContain(Off(sql, "t = 1"), exec.Executed);
    }

    [Fact]
    public void If_NoElse_FalseCondition_SkipsToAfter()
    {
        const string sql = "begin if (c) then t = 1; z = 9; end";
        var exec = new FakeExecutor().Cond(Off(sql, "if (c)"), false);
        var s = new DebugSession(Body(sql), exec);
        Assert.Equal(new[] { "if (c) then t = 1;", "z = 9;" }, Trace(s, sql, StepKind.Into));
        Assert.DoesNotContain(Off(sql, "t = 1"), exec.Executed);
    }

    [Fact]
    public void While_ReEvaluatesHeaderEachIteration()
    {
        const string sql = "begin while (c) do x = 1; end";
        int whileAt = Off(sql, "while (c)");
        var exec = new FakeExecutor().Cond(whileAt, true, true, false);
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        int bodyRuns = 0, headerVisits = 0;
        int guard = 0;
        while (s.State == DebugState.Paused)
        {
            Assert.True(guard++ < 100);
            if (s.CurrentStatement!.Start == whileAt) headerVisits++;
            else if (s.CurrentStatement.Start == Off(sql, "x = 1")) bodyRuns++;
            s.Step(StepKind.Into);
        }
        Assert.Equal(3, headerVisits); // true, true, false
        Assert.Equal(2, bodyRuns);
        Assert.Equal(3, exec.ConditionsEvaluated.Count(x => x == whileAt));
        Assert.Equal(DebugState.Completed, s.State);
    }

    [Fact]
    public void ForSelect_IteratesRows_AppliesInto_ClosesCursor()
    {
        const string sql = "begin for select id from t into :i do suspend; end";
        int forAt = Off(sql, "for select");
        int suspendAt = Off(sql, "suspend");
        var exec = new FakeExecutor()
            .CursorAt(forAt, Row("I", 1), Row("I", 2))
            .Outcome(suspendAt, StatementOutcome.Suspended(Row("I", 1)))
            .Outcome(suspendAt, StatementOutcome.Suspended(Row("I", 2)));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        int guard = 0;
        while (s.State == DebugState.Paused) { Assert.True(guard++ < 100); s.Step(StepKind.Into); }
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Equal(2, exec.Executed.Count(x => x == suspendAt)); // body ran once per row
        Assert.Equal(2, s.EmittedRows.Count);                       // two SUSPEND rows
        Assert.True(exec.Cursors.Single().Closed);                  // cursor closed at end
    }

    [Fact]
    public void ForSelect_NoRows_SkipsBody()
    {
        const string sql = "begin for select id from t into :i do suspend; end";
        var exec = new FakeExecutor().CursorAt(Off(sql, "for select") /* no rows */);
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        while (s.State == DebugState.Paused) s.Step(StepKind.Into);
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Empty(exec.Executed);            // body never ran
        Assert.True(exec.Cursors.Single().Closed);
    }

    [Fact]
    public void NestedBlock_IsStructural_NotAStepPoint()
    {
        const string sql = "begin a = 1; begin b = 2; c = 3; end d = 4; end";
        var s = new DebugSession(Body(sql), new FakeExecutor());
        Assert.Equal(new[] { "a = 1;", "b = 2;", "c = 3;", "d = 4;" }, Trace(s, sql, StepKind.Into));
    }

    // ── Nested frames: Step Into / Over / Out ───────────────────────────────────────────────────────

    private const string CalleeSql = "begin q1 = 1; q2 = 2; end";

    [Fact]
    public void StepInto_Call_PushesFrame_WithSavepoint()
    {
        const string sql = "begin a = 1; execute procedure p; b = 2; end";
        var callee = new DebugRoutine("P", Body(CalleeSql));
        var exec = new FakeExecutor().RoutineAt(Off(sql, "execute procedure p"), callee);
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        Assert.Equal("a = 1;", Text(sql, s.CurrentStatement!));
        s.Step(StepKind.Into); // to the call
        Assert.Equal("execute procedure p;", Text(sql, s.CurrentStatement!));

        s.Step(StepKind.Into); // step INTO the call → push frame P
        Assert.Equal(2, s.Depth);
        Assert.Equal("P", s.CurrentFrame!.RoutineName);
        Assert.Equal("q1 = 1;", Text(CalleeSql, s.CurrentStatement!));
        // Root frame entered first, then callee frame — both got an entry savepoint.
        Assert.Equal(new[] { "enter:ET_DBG_FRAME_0", "enter:ET_DBG_FRAME_1" }, exec.Savepoints);
        // The call itself was NOT executed on the server (we descended instead).
        Assert.DoesNotContain(Off(sql, "execute procedure p"), exec.Executed);
    }

    [Fact]
    public void CalleeFrame_CompletesAndPops_ReleasingSavepoint_CallerResumes()
    {
        const string sql = "begin execute procedure p; b = 2; end";
        var callee = new DebugRoutine("P", Body(CalleeSql));
        var exec = new FakeExecutor().RoutineAt(Off(sql, "execute procedure p"), callee);
        var s = new DebugSession(Body(sql), exec);
        s.Start();                 // at the call
        s.Step(StepKind.Into);     // into P → q1 = 1;
        Assert.Equal(2, s.Depth);
        s.Step(StepKind.Into);     // q2 = 2;
        Assert.Equal("q2 = 2;", Text(CalleeSql, s.CurrentStatement!));
        s.Step(StepKind.Into);     // callee completes → pop → caller's b = 2;
        Assert.Equal(1, s.Depth);
        Assert.Equal("b = 2;", Text(sql, s.CurrentStatement!));
        // Callee frame's savepoint was released on its normal exit.
        Assert.Contains("leave:ET_DBG_FRAME_1", exec.Savepoints);
        s.Step(StepKind.Into);     // b = 2; → complete
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Contains("leave:ET_DBG_FRAME_0", exec.Savepoints); // root released last
    }

    [Fact]
    public void StepOver_Call_ExecutesInPlace_NoFrame()
    {
        const string sql = "begin execute procedure p; b = 2; end";
        var callee = new DebugRoutine("P", Body(CalleeSql));
        var exec = new FakeExecutor().RoutineAt(Off(sql, "execute procedure p"), callee);
        var s = new DebugSession(Body(sql), exec);
        s.Start(); // at the call
        s.Step(StepKind.Over); // run the call on the server, stay in this frame
        Assert.Equal(1, s.Depth);
        Assert.Equal("b = 2;", Text(sql, s.CurrentStatement!));
        Assert.Contains(Off(sql, "execute procedure p"), exec.Executed); // executed, not descended
        Assert.Single(exec.Savepoints);          // only the root frame's entry — no callee frame
    }

    [Fact]
    public void StepInto_UnresolvableCall_ExecutesInPlace()
    {
        const string sql = "begin execute procedure p; b = 2; end";
        var exec = new FakeExecutor(); // no routine registered → ResolveRoutine returns null
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Into); // can't descend → execute in place
        Assert.Equal(1, s.Depth);
        Assert.Contains(Off(sql, "execute procedure p"), exec.Executed);
    }

    [Fact]
    public void StepOut_RunsToFrameReturn()
    {
        const string sql = "begin execute procedure p; b = 2; end";
        var callee = new DebugRoutine("P", Body(CalleeSql));
        var exec = new FakeExecutor().RoutineAt(Off(sql, "execute procedure p"), callee);
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Into); // into P (q1 = 1;)
        Assert.Equal(2, s.Depth);
        s.Step(StepKind.Out);  // run the rest of P, return to caller
        Assert.Equal(1, s.Depth);
        Assert.Equal("b = 2;", Text(sql, s.CurrentStatement!));
        // The callee body ran fully on the server during Step Out (both q1 and q2); the caller's b = 2;
        // has NOT run yet (we stop ON it).
        Assert.Equal(2, exec.Executed.Count);
    }

    // ── Continue / Run To Cursor / Set Next Statement ─────────────────────────────────────────────

    [Fact]
    public void Continue_RunsToCompletion()
    {
        const string sql = "begin a = 1; b = 2; c = 3; end";
        var exec = new FakeExecutor();
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Continue);
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Equal(3, exec.Executed.Count);
    }

    [Fact]
    public void RunToCursor_StopsAtTarget()
    {
        const string sql = "begin a = 1; b = 2; c = 3; d = 4; end";
        var exec = new FakeExecutor();
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.RunToCursor(Off(sql, "c = 3"));
        Assert.Equal(DebugState.Paused, s.State);
        Assert.Equal("c = 3;", Text(sql, s.CurrentStatement!));
        // a=1 and b=2 ran; c=3 has not yet (we stop ON it).
        Assert.Equal(new[] { Off(sql, "a = 1"), Off(sql, "b = 2") }, exec.Executed);
    }

    [Fact]
    public void SetNextStatement_MovesForwardWithinBlock_SkippingStatements()
    {
        const string sql = "begin a = 1; b = 2; c = 3; end";
        var exec = new FakeExecutor();
        var s = new DebugSession(Body(sql), exec);
        s.Start(); // at a = 1;
        Assert.True(s.SetNextStatement(Off(sql, "c = 3")));
        Assert.Equal("c = 3;", Text(sql, s.CurrentStatement!));
        s.Step(StepKind.Into);
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Equal(new[] { Off(sql, "c = 3") }, exec.Executed); // a and b were skipped
    }

    [Fact]
    public void SetNextStatement_MovesBackward_ReRunsStatement()
    {
        const string sql = "begin a = 1; b = 2; end";
        var exec = new FakeExecutor();
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Into); // now at b = 2;
        Assert.Equal("b = 2;", Text(sql, s.CurrentStatement!));
        Assert.True(s.SetNextStatement(Off(sql, "a = 1"))); // jump back to a = 1;
        Assert.Equal("a = 1;", Text(sql, s.CurrentStatement!));
    }

    [Fact]
    public void SetNextStatement_UnreachableTarget_ReturnsFalse_LeavesSessionPut()
    {
        const string sql = "begin a = 1; b = 2; end";
        var s = new DebugSession(Body(sql), new FakeExecutor());
        s.Start();
        Assert.False(s.SetNextStatement(9999));
        Assert.Equal(DebugState.Paused, s.State);
        Assert.Equal("a = 1;", Text(sql, s.CurrentStatement!));
    }

    // ── Exceptions: unhandled fault + SUSPEND + scope chain ──────────────────────────────────────

    [Fact]
    public void RaisedStatement_NoHandler_FaultsSession_RollsBackFrame()
    {
        const string sql = "begin a = 1; b = 2; end";
        var error = new DebugError(ExceptionName: "MY_EXC", Message: "boom");
        var exec = new FakeExecutor().Outcome(Off(sql, "b = 2"), StatementOutcome.Raised(error));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Into); // a = 1;
        s.Step(StepKind.Into); // b = 2; raises → no handler → fault
        Assert.Equal(DebugState.Faulted, s.State);
        Assert.Equal(StopReason.Exception, s.StopReason);
        Assert.Equal("MY_EXC", s.CurrentError!.ExceptionName);
        Assert.Null(s.CurrentStatement);
        // The unhandled root frame was rolled back to its savepoint (§4.5), never released.
        Assert.Contains("rollback:ET_DBG_FRAME_0", exec.Savepoints);
        Assert.DoesNotContain("leave:ET_DBG_FRAME_0", exec.Savepoints);
    }

    [Fact]
    public void Faulted_RetainsFaultingLine_AndFrameSnapshot()
    {
        // The UI keeps the fault visible: the engine retains the faulting statement + the call stack at the raise
        // (the frame's client-side values survive the DB rollback, spec §4.5), even though the live stack is
        // popped — so the user sees WHERE it failed and the variable values at that moment.
        const string sql = "begin a = 1; b = 2; end";
        var exec = new FakeExecutor().Outcome(Off(sql, "b = 2"), StatementOutcome.Raised(new DebugError(ExceptionName: "X")));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        Assert.Null(s.FaultStatement); // nothing retained while paused
        Assert.Null(s.FaultFrame);

        s.Step(StepKind.Into); // a = 1;
        s.Step(StepKind.Into); // b = 2; → unhandled fault

        Assert.Equal(DebugState.Faulted, s.State);
        Assert.Empty(s.CallStack);          // the live stack is popped (existing contract, unchanged)
        Assert.NotNull(s.FaultFrame);        // …but the fault snapshot is retained
        Assert.Equal("b = 2;", Text(sql, s.FaultStatement!)); // the exact faulting line
        Assert.Equal("(anonymous block)", Assert.Single(s.FaultStack).RoutineName);
    }

    [Fact]
    public void Suspend_RecordsEmittedRow()
    {
        const string sql = "begin suspend; end";
        var exec = new FakeExecutor().Outcome(Off(sql, "suspend"), StatementOutcome.Suspended(Row("R", 42)));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Into);
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Equal(42, Assert.Single(s.EmittedRows)["R"]);
    }

    [Fact]
    public void Assignment_WriteBack_IsAppliedToFrameValues()
    {
        const string sql = "begin a = 1; end";
        var exec = new FakeExecutor().Outcome(Off(sql, "a = 1"), StatementOutcome.Normal(Row("A", 7)));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        var frame = s.CurrentFrame!;
        s.Step(StepKind.Into);
        Assert.True(frame.Values.TryGet("A", out var v));
        Assert.Equal(7, v);
    }

    [Fact]
    public void StoredCallee_IsAClosedScope_DoesNotSeeCallerVariables()
    {
        // A called STORED routine is a closed scope (spec §6): its only inputs are its parameters, so its
        // frame does NOT chain to the caller's variables (D8 — the LexicalParent split). It is still on the
        // call stack (Parent = caller) for stepping, navigation and exception propagation.
        const string sql = "begin a = 1; execute procedure p; end";
        var callee = new DebugRoutine("P", Body(CalleeSql));
        var exec = new FakeExecutor()
            .Outcome(Off(sql, "a = 1"), StatementOutcome.Normal(Row("V_OUTER", 5)))
            .RoutineAt(Off(sql, "execute procedure p"), callee); // stored (default) → closed scope
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        s.Step(StepKind.Into); // a = 1; → ROOT frame gets V_OUTER = 5
        s.Step(StepKind.Into); // step INTO the stored callee

        var child = s.CurrentFrame!;
        Assert.Equal("P", child.RoutineName);
        Assert.Null(child.LexicalParent);                       // closed scope — no lexical parent
        Assert.Equal("ROOT", child.Parent!.RoutineName);        // but the caller IS its call-stack parent
        Assert.False(child.TryResolveValue("V_OUTER", out _));  // the caller's variable is not visible
    }

    [Fact]
    public void LocalCallee_IsAClosure_ResolvesAndWritesOuterVariable()
    {
        // The scope-chain mechanism the flagship (D9) local routines build on: a LOCAL sub-routine's frame's
        // lexical parent is its declaring (caller) frame, so it resolves and can write back an outer variable.
        // (Contrast StoredCallee_IsAClosedScope: a stored routine — the D8 default — has NO lexical parent.)
        const string sql = "begin a = 1; execute procedure p; end";
        var callee = new DebugRoutine("P", Body(CalleeSql));
        var exec = new FakeExecutor()
            .Outcome(Off(sql, "a = 1"), StatementOutcome.Normal(Row("V_OUTER", 5)))
            .RoutineAt(Off(sql, "execute procedure p"), callee, asLocalClosure: true);
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        s.Step(StepKind.Into); // a = 1; → ROOT frame gets V_OUTER = 5
        s.Step(StepKind.Into); // step INTO the local sub-routine → lexical parent = ROOT

        var child = s.CurrentFrame!;
        Assert.Equal("P", child.RoutineName);
        Assert.Equal("ROOT", child.LexicalParent!.RoutineName);
        Assert.True(child.TryResolveValue("V_OUTER", out var v)); // resolves up the chain to ROOT
        Assert.Equal(5, v);
        Assert.False(child.TryResolveValue("NOPE", out _));

        child.SetResolvedValue("V_OUTER", 99);          // closure write reaches the defining (ROOT) frame
        var root = s.CallStack[^1];                       // CallStack is innermost-first → last is ROOT
        Assert.Equal("ROOT", root.RoutineName);
        Assert.Equal(99, root.Values.Get("V_OUTER"));
        Assert.False(child.Values.Contains("V_OUTER"));   // not shadowed locally
    }

    [Fact]
    public void StepInto_LocalClosure_StatementWriteBack_RoutesToTheDeclaringFrame()
    {
        // D9 seam b: the INTERPRETER routes a stepped statement's write-back through the closure chain — a
        // write to a captured OUTER variable lands in the declaring (parent) frame, not as a spurious local in
        // the callee frame. (LocalCallee_IsAClosure above proves the Frame mechanism; this proves the engine
        // uses it when applying a statement outcome.)
        const string sql = "begin acc = 1; execute procedure bump; end";
        const string calleeSql = "begin\n  acc = acc + 10;\nend"; // distinct offsets from the root (no fake-key clash)
        var callee = new DebugRoutine("BUMP", Body(calleeSql));
        var exec = new FakeExecutor()
            .Outcome(Off(sql, "acc = 1"), StatementOutcome.Normal(Row("ACC", 5)))
            .RoutineAt(Off(sql, "execute procedure bump"), callee, asLocalClosure: true)
            .Outcome(Off(calleeSql, "acc = acc + 10"), StatementOutcome.Normal(Row("ACC", 15)));
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        var root = s.CurrentFrame!; // hold the ROOT frame — the write must persist there after it pops
        Assert.Equal("ROOT", root.RoutineName);
        s.Step(StepKind.Into); // acc = 1 → ROOT.ACC = 5
        s.Step(StepKind.Into); // step INTO BUMP (closure over ROOT)
        var child = s.CurrentFrame!;
        Assert.Equal("BUMP", child.RoutineName);

        s.Step(StepKind.Into); // run acc = acc + 10 inside BUMP → write-back ACC=15 must route to ROOT

        Assert.Equal(15, root.Values.Get("ACC")); // the closure write reached the parent frame
        Assert.False(child.Values.Contains("ACC")); // NOT captured as a callee local
    }

    [Fact]
    public void StepInto_ReturningValues_WritesCalleeOutputsIntoCallerVariables()
    {
        // EXECUTE PROCEDURE P RETURNING_VALUES :x, :y — on P's NORMAL return, P's output parameters (O1, O2)
        // are bound positionally into the caller's :x / :y (spec §5). Proven with a scripted stored callee
        // whose outputs are set by its own body.
        const string sql = "begin execute procedure p returning_values :x, :y; end";
        const string calleeSql = "begin o1 = 10; o2 = 20; end";
        var callee = new DebugRoutine("P", Body(calleeSql), outputParameterNames: new[] { "O1", "O2" });
        var exec = new FakeExecutor()
            .RoutineAt(Off(sql, "execute procedure p"), callee)
            .Outcome(Off(calleeSql, "o1 = 10"), StatementOutcome.Normal(Row("O1", 10)))
            .Outcome(Off(calleeSql, "o2 = 20"), StatementOutcome.Normal(Row("O2", 20)));
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        var root = s.CurrentFrame!;                 // the ROOT (caller) frame
        s.Step(StepKind.Into);                      // into P → o1 = 10;
        Assert.Equal(2, s.Depth);
        s.Step(StepKind.Into);                      // o1 = 10; (callee O1 = 10)
        s.Step(StepKind.Into);                      // o2 = 20; then P returns → RETURNING_VALUES write-back
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Equal(10, root.Values.Get("X"));     // :x ← O1
        Assert.Equal(20, root.Values.Get("Y"));     // :y ← O2
    }

    // ── Exception routing (seam b): handler matching, propagation, unwind, re-raise ────────────────

    // Runs the raising statement, then steps once more so the caught handler body is reached; returns the
    // session for assertions. Uses Step Into so we stop at the handler's first statement.
    private static DebugSession Raise(string sql, string raiseSub, DebugError error, out FakeExecutor exec)
    {
        exec = new FakeExecutor().Outcome(Off(sql, raiseSub), StatementOutcome.Raised(error));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        while (s.State == DebugState.Paused && s.CurrentStatement!.Start != Off(sql, raiseSub))
        {
            s.Step(StepKind.Into);
        }
        s.Step(StepKind.Into); // execute the raising statement → route
        return s;
    }

    [Fact]
    public void WhenAny_CatchesInSameBlock_PriorStatementsSurvive_FrameNotRolledBack()
    {
        const string sql = "begin a = 1; b = 2; when any do c = 3; end";
        var s = Raise(sql, "b = 2", new DebugError(ExceptionName: "X"), out var exec);
        Assert.Equal(DebugState.Paused, s.State);
        Assert.Equal("c = 3;", Text(sql, s.CurrentStatement!)); // resumed at the handler body
        Assert.Null(s.CurrentError);                            // caught → no longer faulted
        s.Step(StepKind.Into);                                  // run the handler → block/frame completes
        Assert.Equal(DebugState.Completed, s.State);
        // A WHEN-handled block is NOT rolled back (§4.5 — prior statements survive); the frame is released.
        Assert.DoesNotContain("rollback:ET_DBG_FRAME_0", exec.Savepoints);
        Assert.Contains("leave:ET_DBG_FRAME_0", exec.Savepoints);
    }

    [Fact]
    public void WhenExceptionName_Matches_ByName()
    {
        const string sql = "begin r = 1; when exception my_exc do h = 1; end";
        var s = Raise(sql, "r = 1", new DebugError(ExceptionName: "MY_EXC"), out _);
        Assert.Equal("h = 1;", Text(sql, s.CurrentStatement!));
    }

    [Fact]
    public void WhenExceptionName_DoesNotMatch_OtherException_Faults()
    {
        const string sql = "begin r = 1; when exception my_exc do h = 1; end";
        var s = Raise(sql, "r = 1", new DebugError(ExceptionName: "OTHER_EXC"), out var exec);
        Assert.Equal(DebugState.Faulted, s.State);
        Assert.Contains("rollback:ET_DBG_FRAME_0", exec.Savepoints);
    }

    [Fact]
    public void WhenGdsCode_Matches_ByNumber_And_BySymbol()
    {
        const string byNumber = "begin r = 1; when gdscode 335544345 do h = 1; end";
        var s1 = Raise(byNumber, "r = 1", new DebugError(GdsCode: 335544345), out _);
        Assert.Equal("h = 1;", Text(byNumber, s1.CurrentStatement!));

        const string bySymbol = "begin r = 1; when gdscode lock_conflict do h = 1; end";
        var s2 = Raise(bySymbol, "r = 1", new DebugError(GdsCode: 335544345, GdsCodeSymbol: "lock_conflict"), out _);
        Assert.Equal("h = 1;", Text(bySymbol, s2.CurrentStatement!));
    }

    [Fact]
    public void WhenSqlCode_Matches_SignedNumber()
    {
        const string sql = "begin r = 1; when sqlcode -913 do h = 1; end";
        var s = Raise(sql, "r = 1", new DebugError(SqlCode: -913), out _);
        Assert.Equal("h = 1;", Text(sql, s.CurrentStatement!));
    }

    [Fact]
    public void WhenSqlState_Matches_StringLiteral()
    {
        const string sql = "begin r = 1; when sqlstate '40001' do h = 1; end";
        var s = Raise(sql, "r = 1", new DebugError(SqlState: "40001"), out _);
        Assert.Equal("h = 1;", Text(sql, s.CurrentStatement!));
    }

    [Fact]
    public void MultiConditionWhen_MatchesAnyListedCondition()
    {
        // WHEN GDSCODE 1, EXCEPTION MY_EXC DO … — the second condition catches (declaration order).
        const string sql = "begin r = 1; when gdscode 1, exception my_exc do h = 1; end";
        var s = Raise(sql, "r = 1", new DebugError(ExceptionName: "MY_EXC"), out _);
        Assert.Equal("h = 1;", Text(sql, s.CurrentStatement!));
    }

    [Fact]
    public void Exception_PropagatesToCaller_RollsBackCalleeFrame_ThenCallerCatches()
    {
        // NB: the fake executor keys outcomes by a node's Start offset, which is shared across frames' own
        // coordinate spaces — so the root must NOT run a leaf at the callee's raising offset. Starting the
        // root with the call (the raising `r = 1` at the callee's offset is never executed in the root frame,
        // only descended into) keeps the two frames' scripted outcomes unambiguous.
        const string sql = "begin execute procedure p; when any do h = 1; end";
        const string calleeSql = "begin r = 1; end";
        var callee = new DebugRoutine("P", Body(calleeSql));
        var exec = new FakeExecutor()
            .RoutineAt(Off(sql, "execute procedure p"), callee)
            .Outcome(Off(calleeSql, "r = 1"), StatementOutcome.Raised(new DebugError(ExceptionName: "X")));
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();             // at execute procedure p
        s.Step(StepKind.Into); // into P → r = 1;
        Assert.Equal(2, s.Depth);
        s.Step(StepKind.Into); // r = 1; raises → P has no handler → propagate to ROOT's WHEN ANY
        Assert.Equal(1, s.Depth);
        Assert.Equal("h = 1;", Text(sql, s.CurrentStatement!));
        // The callee frame was rolled back to its savepoint on unhandled exit; the catching root frame was not.
        Assert.Contains("rollback:ET_DBG_FRAME_1", exec.Savepoints);
        Assert.DoesNotContain("rollback:ET_DBG_FRAME_0", exec.Savepoints);
    }

    [Fact]
    public void ReRaiseInHandler_PropagatesOut_HandlerDoesNotCatchItsOwnBody()
    {
        // Inner block's WHEN ANY does a bare EXCEPTION; (re-raise) — it must NOT re-catch, it must propagate
        // to the outer block's WHEN ANY (HandlerActive guard + propagation across nested blocks, one frame).
        const string sql = "begin begin r = 1; when any do exception; end when any do h = 1; end";
        var exec = new FakeExecutor()
            .Outcome(Off(sql, "r = 1"), StatementOutcome.Raised(new DebugError(ExceptionName: "X")))
            .Outcome(Off(sql, "exception;"), StatementOutcome.Raised(new DebugError(ExceptionName: "X")));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        Assert.Equal("r = 1;", Text(sql, s.CurrentStatement!));
        s.Step(StepKind.Into); // r raises → inner WHEN ANY catches → at "exception;"
        Assert.Equal("exception;", Text(sql, s.CurrentStatement!));
        s.Step(StepKind.Into); // exception; re-raises → inner cannot re-catch → outer WHEN ANY catches
        Assert.Equal("h = 1;", Text(sql, s.CurrentStatement!));
        Assert.Null(s.CurrentError);
        // Same frame throughout — no frame rollback (block-level handling; prior statements survive).
        Assert.DoesNotContain("rollback:ET_DBG_FRAME_0", exec.Savepoints);
    }

    [Fact]
    public void ForSelect_BodyRaisesUnhandled_ClosesCursor_RollsBackFrame()
    {
        const string sql = "begin for select id from t into :i do r = 1; end";
        var exec = new FakeExecutor()
            .CursorAt(Off(sql, "for select"), Row("I", 1))
            .Outcome(Off(sql, "r = 1"), StatementOutcome.Raised(new DebugError(ExceptionName: "X")));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        while (s.State == DebugState.Paused && s.CurrentStatement!.Start != Off(sql, "r = 1")) s.Step(StepKind.Into);
        s.Step(StepKind.Into); // r = 1; raises, no handler → fault
        Assert.Equal(DebugState.Faulted, s.State);
        Assert.True(exec.Cursors.Single().Closed);              // the abandoned cursor was closed on unwind
        Assert.Contains("rollback:ET_DBG_FRAME_0", exec.Savepoints);
    }

    [Fact]
    public void ForSelect_BodyRaisesHandled_ClosesCursor_OnUnwindToCatchingBlock()
    {
        const string sql = "begin for select id from t into :i do r = 1; when any do h = 1; end";
        var exec = new FakeExecutor()
            .CursorAt(Off(sql, "for select"), Row("I", 1))
            .Outcome(Off(sql, "r = 1"), StatementOutcome.Raised(new DebugError(ExceptionName: "X")));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        while (s.State == DebugState.Paused && s.CurrentStatement!.Start != Off(sql, "r = 1")) s.Step(StepKind.Into);
        s.Step(StepKind.Into); // r raises → WHEN ANY catches; the loop is abandoned, its cursor closed
        Assert.Equal("h = 1;", Text(sql, s.CurrentStatement!));
        Assert.True(exec.Cursors.Single().Closed);
        Assert.DoesNotContain("rollback:ET_DBG_FRAME_0", exec.Savepoints); // handled in-frame → no rollback
    }

    // ── D12: Break on Exception (a pause before the ExceptionRouter, spec §9.8.1) ───────────────────

    [Fact]
    public void BreakOnException_DefaultsFalse_RaiseRoutesImmediately()
    {
        // Default (disarmed): a raise routes immediately, exactly as before D12 — never a break pause.
        const string sql = "begin a = 1; b = 2; end";
        var exec = new FakeExecutor().Outcome(Off(sql, "b = 2"), StatementOutcome.Raised(new DebugError(ExceptionName: "X")));
        var s = new DebugSession(Body(sql), exec);
        Assert.False(s.BreakOnException);
        s.Start();
        s.Step(StepKind.Into); // a = 1;
        Assert.False(s.IsPausedOnException);
        s.Step(StepKind.Into); // b = 2; raises → routed immediately → Faulted (no break pause)
        Assert.Equal(DebugState.Faulted, s.State);
        Assert.False(s.IsPausedOnException);
    }

    [Fact]
    public void BreakOnException_PausesAtRaisingStatement_NotFaulted_FrameIntact()
    {
        // Armed: the raise PAUSES at its statement — a stop point, not a session fault. The frame is intact
        // (still on the stack, not rolled back), the raising line is current, and the error is inspectable.
        const string sql = "begin a = 1; b = 2; end";
        var exec = new FakeExecutor().Outcome(Off(sql, "b = 2"), StatementOutcome.Raised(new DebugError(ExceptionName: "MY_EXC")));
        var s = new DebugSession(Body(sql), exec) { BreakOnException = true };
        s.Start();
        s.Step(StepKind.Into); // a = 1;
        s.Step(StepKind.Into); // b = 2; raises → break-on-exception PAUSE (before routing)
        Assert.Equal(DebugState.Paused, s.State);
        Assert.Equal(StopReason.Exception, s.StopReason);
        Assert.True(s.IsPausedOnException);
        Assert.Equal("b = 2;", Text(sql, s.CurrentStatement!)); // paused ON the raising line
        Assert.NotNull(s.CurrentFrame);                          // frame intact
        Assert.Equal("MY_EXC", s.CurrentError!.ExceptionName);
        // Nothing has been routed yet: the frame's savepoint is neither released nor rolled back.
        Assert.DoesNotContain("rollback:ET_DBG_FRAME_0", exec.Savepoints);
        Assert.DoesNotContain("leave:ET_DBG_FRAME_0", exec.Savepoints);
        // The terminal (Faulted) snapshot fields stay empty — this is a pause, not a fault.
        Assert.Null(s.FaultStatement);
    }

    [Fact]
    public void BreakOnException_Continue_RoutesUnhandled_FaultsIdenticallyToImmediate()
    {
        // Resuming from the break routes the held raise through the SAME path. With no handler, the outcome is
        // byte-for-byte the immediate fault: Faulted, the same faulting line retained, the same frame rollback.
        const string sql = "begin a = 1; b = 2; end";

        DebugSession Run(bool breakOn, out FakeExecutor exec)
        {
            exec = new FakeExecutor().Outcome(Off(sql, "b = 2"), StatementOutcome.Raised(new DebugError(ExceptionName: "X")));
            var s = new DebugSession(Body(sql), exec) { BreakOnException = breakOn };
            s.Start();
            s.Step(StepKind.Into); // a = 1;
            s.Step(StepKind.Into); // b = 2; → immediate fault, OR break pause
            if (breakOn)
            {
                Assert.True(s.IsPausedOnException);
                s.Step(StepKind.Continue); // resume → route the held raise
            }
            return s;
        }

        var immediate = Run(false, out var immExec);
        var broken = Run(true, out var brkExec);

        Assert.Equal(DebugState.Faulted, immediate.State);
        Assert.Equal(DebugState.Faulted, broken.State);
        Assert.Equal(StopReason.Exception, broken.StopReason);
        Assert.False(broken.IsPausedOnException);                         // routed → no longer holding it
        Assert.Equal("b = 2;", Text(sql, broken.FaultStatement!));        // same faulting line as immediate
        Assert.Equal(Text(sql, immediate.FaultStatement!), Text(sql, broken.FaultStatement!));
        Assert.Equal(immExec.Savepoints, brkExec.Savepoints);            // identical savepoint trace = one path
    }

    [Fact]
    public void BreakOnException_Resume_RoutesCaught_RepositionsToHandlerBody()
    {
        // A caught raise, resumed from the break, routes to the matching WHEN … DO handler — the router
        // repositions control to the handler body and the exception is cleared, exactly as an un-broken raise.
        const string sql = "begin r = 1; when exception my_exc do h = 1; end";
        var exec = new FakeExecutor().Outcome(Off(sql, "r = 1"), StatementOutcome.Raised(new DebugError(ExceptionName: "MY_EXC")));
        var s = new DebugSession(Body(sql), exec) { BreakOnException = true };
        s.Start();
        s.Step(StepKind.Into); // r = 1; raises → break pause
        Assert.True(s.IsPausedOnException);
        s.Step(StepKind.Into); // resume (Step) → route → caught → stop at the handler body
        Assert.Equal(DebugState.Paused, s.State);
        Assert.False(s.IsPausedOnException);
        Assert.Null(s.CurrentError);                            // caught → cleared
        Assert.Equal("h = 1;", Text(sql, s.CurrentStatement!)); // repositioned to the handler
        // A WHEN-handled block is not rolled back (§4.5).
        Assert.DoesNotContain("rollback:ET_DBG_FRAME_0", exec.Savepoints);
    }

    [Fact]
    public void BreakOnException_CaughtRouting_IsIdenticalWithBreakOnOrOff()
    {
        // The strongest "one path" proof: a caught raise reaches the SAME terminal state, the SAME emitted
        // rows and the SAME savepoint trace whether Break-on-Exception is off (immediate) or on (break, then
        // continue to completion). Break-on-Exception adds a pause, nothing else.
        const string sql = "begin r = 1; when exception my_exc do h = 1; suspend; end";

        (DebugState State, List<string> Savepoints, int Rows) Run(bool breakOn)
        {
            var exec = new FakeExecutor()
                .Outcome(Off(sql, "r = 1"), StatementOutcome.Raised(new DebugError(ExceptionName: "MY_EXC")))
                .Outcome(Off(sql, "suspend"), StatementOutcome.Suspended(Row("R", 1)));
            var s = new DebugSession(Body(sql), exec) { BreakOnException = breakOn };
            s.Start();
            int guard = 0;
            while (s.State == DebugState.Paused)
            {
                Assert.True(guard++ < 100, "runaway");
                s.Step(StepKind.Continue);
            }
            return (s.State, exec.Savepoints, s.EmittedRows.Count);
        }

        var off = Run(false);
        var on = Run(true);
        Assert.Equal(DebugState.Completed, off.State);
        Assert.Equal(off.State, on.State);
        Assert.Equal(off.Savepoints, on.Savepoints);
        Assert.Equal(off.Rows, on.Rows);
    }

    [Fact]
    public void BreakOnException_SetNextStatement_DropsHeldRaise()
    {
        // Repositioning the IP while paused on a broken raise abandons it: it is no longer held (a later
        // resume will not route it) and is no longer the current error.
        const string sql = "begin a = 1; b = 2; c = 3; end";
        var exec = new FakeExecutor().Outcome(Off(sql, "b = 2"), StatementOutcome.Raised(new DebugError(ExceptionName: "X")));
        var s = new DebugSession(Body(sql), exec) { BreakOnException = true };
        s.Start();
        s.Step(StepKind.Into); // a = 1;
        s.Step(StepKind.Into); // b = 2; raises → break pause
        Assert.True(s.IsPausedOnException);
        Assert.True(s.SetNextStatement(Off(sql, "c = 3"))); // reposition to c = 3;
        Assert.False(s.IsPausedOnException);
        Assert.Null(s.CurrentError);
        Assert.Equal("c = 3;", Text(sql, s.CurrentStatement!));
        s.Step(StepKind.Continue); // runs c = 3; to completion — the dropped raise is NOT routed
        Assert.Equal(DebugState.Completed, s.State);
        Assert.DoesNotContain("rollback:ET_DBG_FRAME_0", exec.Savepoints);
    }

    // ── Breakpoints (seam b) ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Continue_StopsAtBreakpoint()
    {
        const string sql = "begin a = 1; b = 2; c = 3; end";
        var exec = new FakeExecutor();
        var s = new DebugSession(Body(sql), exec);
        s.Breakpoints.Add(Off(sql, "b = 2"));
        s.Start();
        s.Step(StepKind.Continue);
        Assert.Equal(DebugState.Paused, s.State);
        Assert.Equal(StopReason.Breakpoint, s.StopReason);
        Assert.Equal("b = 2;", Text(sql, s.CurrentStatement!));
        Assert.Equal(new[] { Off(sql, "a = 1") }, exec.Executed); // a ran; b not yet (we stop ON it)
    }

    [Fact]
    public void Continue_PastBreakpoint_ContinuesToCompletion()
    {
        const string sql = "begin a = 1; b = 2; c = 3; end";
        var exec = new FakeExecutor();
        var s = new DebugSession(Body(sql), exec);
        s.Breakpoints.Add(Off(sql, "b = 2"));
        s.Start();
        s.Step(StepKind.Continue); // stop at b
        s.Step(StepKind.Continue); // resume from b → runs to completion (no re-stop on the current one)
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Equal(3, exec.Executed.Count);
    }

    [Fact]
    public void Breakpoint_Removed_DoesNotStop()
    {
        const string sql = "begin a = 1; b = 2; c = 3; end";
        var exec = new FakeExecutor();
        var s = new DebugSession(Body(sql), exec);
        int bp = Off(sql, "b = 2");
        Assert.True(s.Breakpoints.Toggle(bp));   // now set
        Assert.False(s.Breakpoints.Toggle(bp));  // now cleared
        s.Start();
        s.Step(StepKind.Continue);
        Assert.Equal(DebugState.Completed, s.State);
    }

    [Fact]
    public void Breakpoint_InsideCallee_StopsWhileContinuing()
    {
        const string sql = "begin execute procedure p; b = 2; end";
        var callee = new DebugRoutine("P", Body(CalleeSql)); // "begin q1 = 1; q2 = 2; end"
        var exec = new FakeExecutor().RoutineAt(Off(sql, "execute procedure p"), callee);
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Into);                        // into P → q1 = 1;
        s.Breakpoints.Add(Off(CalleeSql, "q2 = 2"));  // breakpoint inside the callee frame
        s.Step(StepKind.Continue);
        Assert.Equal(StopReason.Breakpoint, s.StopReason);
        Assert.Equal("q2 = 2;", Text(CalleeSql, s.CurrentStatement!));
        Assert.Equal(2, s.Depth);
    }

    // ── D12: Conditional breakpoints + hit counts (spec §9.8.2) ─────────────────────────────────────

    [Fact]
    public void HitCountPolicy_IsMetAt_PerKind()
    {
        Assert.True(HitCountPolicy.Always.IsMetAt(1));
        Assert.True(HitCountPolicy.Always.IsMetAt(9));

        Assert.False(HitCountPolicy.Exactly(3).IsMetAt(2));
        Assert.True(HitCountPolicy.Exactly(3).IsMetAt(3));
        Assert.False(HitCountPolicy.Exactly(3).IsMetAt(4));

        Assert.False(HitCountPolicy.AtLeast(3).IsMetAt(2));
        Assert.True(HitCountPolicy.AtLeast(3).IsMetAt(3));
        Assert.True(HitCountPolicy.AtLeast(3).IsMetAt(9));

        Assert.False(HitCountPolicy.Multiple(2).IsMetAt(1));
        Assert.True(HitCountPolicy.Multiple(2).IsMetAt(2));
        Assert.False(HitCountPolicy.Multiple(2).IsMetAt(3));
        Assert.True(HitCountPolicy.Multiple(2).IsMetAt(4));
    }

    [Fact]
    public void Breakpoint_UnsatisfiedCondition_DoesNotCountOrBreak()
    {
        var bp = new Breakpoint(0);
        Assert.False(bp.ShouldBreak(conditionSatisfied: false));
        Assert.Equal(0, bp.Hits); // a false / NULL condition never counts as a hit
    }

    [Fact]
    public void Breakpoint_HitCount_CountsSatisfiedArrivals_BreaksAtPolicy()
    {
        var bp = new Breakpoint(0) { HitCount = HitCountPolicy.Exactly(2) };
        Assert.False(bp.ShouldBreak(true)); // Hits 1
        Assert.True(bp.ShouldBreak(true));  // Hits 2 → Exactly(2) met
        Assert.False(bp.ShouldBreak(true)); // Hits 3
        Assert.Equal(3, bp.Hits);
    }

    [Fact]
    public void BreakpointSet_AddThenGetOrAdd_ReturnsTheSamePolicyObject()
    {
        var set = new BreakpointSet();
        Assert.True(set.Add(10));
        var bp = set.Get(10);
        Assert.NotNull(bp);
        Assert.Same(bp, set.GetOrAdd(10));            // GetOrAdd returns the existing entry
        bp!.Condition = "x > 0";
        Assert.False(set.Add(10));                    // Add on an existing offset is a no-op…
        Assert.Equal("x > 0", set.Get(10)!.Condition); // …it keeps the configured policy
    }

    [Fact]
    public void ConditionalBreakpoint_StopsOnlyWhenConditionTrue()
    {
        // Condition false on arrival 1 (no count, no stop), true on arrival 2 (counts, stops).
        const string sql = "begin for select id from t into :i do x = 1; end";
        var exec = new FakeExecutor()
            .CursorAt(Off(sql, "for select"), Row("I", 1), Row("I", 2), Row("I", 3))
            .CondFragment("i > 1", false, true);
        var s = new DebugSession(Body(sql), exec);
        var bp = s.Breakpoints.GetOrAdd(Off(sql, "x = 1"));
        bp.Condition = "i > 1";
        s.Start();
        s.Step(StepKind.Continue);
        Assert.Equal(DebugState.Paused, s.State);
        Assert.Equal(StopReason.Breakpoint, s.StopReason);
        Assert.Equal(1, bp.Hits);                                        // only the condition-true arrival counted
        Assert.Equal(new[] { "i > 1", "i > 1" }, exec.ConditionFragmentsEvaluated); // evaluated on BOTH arrivals
    }

    [Fact]
    public void HitCountBreakpoint_Exactly_BreaksOnNthArrival()
    {
        const string sql = "begin for select id from t into :i do x = 1; end";
        var exec = new FakeExecutor().CursorAt(Off(sql, "for select"),
            Row("I", 1), Row("I", 2), Row("I", 3), Row("I", 4), Row("I", 5));
        var s = new DebugSession(Body(sql), exec);
        var bp = s.Breakpoints.GetOrAdd(Off(sql, "x = 1"));
        bp.HitCount = HitCountPolicy.Exactly(3);
        s.Start();
        s.Step(StepKind.Continue);
        Assert.Equal(StopReason.Breakpoint, s.StopReason);
        Assert.Equal(3, bp.Hits); // one Continue stopped on exactly the 3rd arrival
    }

    [Fact]
    public void HitCountBreakpoint_Multiple_BreaksEveryN()
    {
        const string sql = "begin for select id from t into :i do x = 1; end";
        var exec = new FakeExecutor().CursorAt(Off(sql, "for select"),
            Row("I", 1), Row("I", 2), Row("I", 3), Row("I", 4), Row("I", 5), Row("I", 6));
        var s = new DebugSession(Body(sql), exec);
        var bp = s.Breakpoints.GetOrAdd(Off(sql, "x = 1"));
        bp.HitCount = HitCountPolicy.Multiple(2);
        s.Start();
        s.Step(StepKind.Continue); Assert.Equal(2, bp.Hits); // stop at arrival 2
        s.Step(StepKind.Continue); Assert.Equal(4, bp.Hits); // stop at arrival 4
        s.Step(StepKind.Continue); Assert.Equal(6, bp.Hits); // stop at arrival 6
        s.Step(StepKind.Continue);                            // no more rows → completes
        Assert.Equal(DebugState.Completed, s.State);
    }

    [Fact]
    public void ConditionAndHitCount_Combined_HitCountRunsOverConditionTrueArrivals()
    {
        // Condition gates first; the hit count counts only condition-TRUE arrivals. Condition true on arrivals
        // 2,3,4; hit-count Exactly(2) → stop on the 2nd condition-true arrival.
        const string sql = "begin for select id from t into :i do x = 1; end";
        var exec = new FakeExecutor()
            .CursorAt(Off(sql, "for select"), Row("I", 1), Row("I", 2), Row("I", 3), Row("I", 4))
            .CondFragment("i > 1", false, true, true, true);
        var s = new DebugSession(Body(sql), exec);
        var bp = s.Breakpoints.GetOrAdd(Off(sql, "x = 1"));
        bp.Condition = "i > 1";
        bp.HitCount = HitCountPolicy.Exactly(2);
        s.Start();
        s.Step(StepKind.Continue);
        Assert.Equal(StopReason.Breakpoint, s.StopReason);
        Assert.Equal(2, bp.Hits); // arrivals 2 and 3 counted; stopped on the 2nd condition-true arrival
    }

    [Fact]
    public void ConditionalBreakpoint_ConditionRaises_StopsAndSurfacesError()
    {
        // A condition that raises never silently skips the breakpoint — the session stops ON the line and the
        // error is surfaced (spec §F), so the user can fix the condition.
        const string sql = "begin a = 1; b = 2; end";
        var exec = new FakeExecutor().CondFragmentRaises("bad expr", new DebugError(ExceptionName: "E_COND", Message: "bad"));
        var s = new DebugSession(Body(sql), exec);
        s.Breakpoints.GetOrAdd(Off(sql, "b = 2")).Condition = "bad expr";
        s.Start();
        s.Step(StepKind.Continue);
        Assert.Equal(DebugState.Paused, s.State);
        Assert.Equal(StopReason.Breakpoint, s.StopReason);
        Assert.Equal("E_COND", s.BreakpointConditionError!.ExceptionName);
        Assert.Equal("b = 2;", Text(sql, s.CurrentStatement!));     // stopped ON the line…
        Assert.DoesNotContain(Off(sql, "b = 2"), exec.Executed);    // …b = 2 not executed
    }

    [Fact]
    public void ConditionalBreakpoint_NullCondition_DoesNotStop()
    {
        const string sql = "begin a = 1; b = 2; end";
        var exec = new FakeExecutor().CondFragment("x", new bool?[] { null }); // condition → NULL
        var s = new DebugSession(Body(sql), exec);
        s.Breakpoints.GetOrAdd(Off(sql, "b = 2")).Condition = "x";
        s.Start();
        s.Step(StepKind.Continue);
        Assert.Equal(DebugState.Completed, s.State); // NULL is not-true (three-valued logic) → never stops
    }

    [Fact]
    public void PlainBreakpoint_StillStopsEveryArrival_AndNeverTouchesTheConditionEngine()
    {
        // A plain Add breakpoint (no condition, Always) behaves exactly as pre-D12: it stops every time and
        // never invokes the condition evaluator (no needless server round-trip).
        const string sql = "begin for select id from t into :i do x = 1; end";
        var exec = new FakeExecutor().CursorAt(Off(sql, "for select"), Row("I", 1), Row("I", 2));
        var s = new DebugSession(Body(sql), exec);
        s.Breakpoints.Add(Off(sql, "x = 1"));
        s.Start();
        s.Step(StepKind.Continue);
        Assert.Equal("x = 1;", Text(sql, s.CurrentStatement!)); // arrival 1
        s.Step(StepKind.Continue);
        Assert.Equal("x = 1;", Text(sql, s.CurrentStatement!)); // arrival 2
        s.Step(StepKind.Continue);
        Assert.Equal(DebugState.Completed, s.State);
        Assert.Empty(exec.ConditionFragmentsEvaluated); // no condition → the D5 engine is never touched
    }

    // ── D5: expression evaluation (Evaluate / Watches / Immediate — one engine, §9.5) ──────────────

    [Fact]
    public void Evaluate_Expression_ReturnsValue_AndDoesNotMutateFrame()
    {
        const string sql = "begin a = 1; b = 2; end";
        var exec = new FakeExecutor().EvalReturns("a + b", EvaluationResult.Ok("/*sql*/", 3, null));
        var s = new DebugSession(Body(sql), exec);
        s.Start(); // paused at entry

        var result = s.Evaluate("a + b", EvaluationKind.Expression);

        Assert.True(result.Success);
        Assert.Equal(3, result.Value);
        Assert.False(result.HadWriteBack);
        // The request carried Expression mode and the current step's offset for in-scope resolution.
        var req = Assert.Single(exec.Evaluations);
        Assert.Equal(EvaluationKind.Expression, req.Kind);
        Assert.Equal(s.CurrentStatement!.Start, req.ScopeOffset);
    }

    [Fact]
    public void Evaluate_Statement_AppliesWriteBack_ToLiveFrame()
    {
        const string sql = "begin a = 1; end";
        var writes = Row("V", 42);
        var exec = new FakeExecutor().EvalReturns("v = 42", EvaluationResult.Ok("/*sql*/", null, writes));
        var s = new DebugSession(Body(sql), exec);
        s.Start();

        var result = s.Evaluate("v = 42", EvaluationKind.Statement);

        Assert.True(result.HadWriteBack);
        Assert.True(s.CurrentFrame!.TryResolveValue("V", out var v));
        Assert.Equal(42, v); // the Immediate statement mutated the live frame (§9.5)
    }

    [Fact]
    public void Evaluate_ExpressionMode_NeverAppliesWriteBack()
    {
        const string sql = "begin a = 1; end";
        // Even if the executor (wrongly) returned writes, an Expression must not mutate the frame.
        var exec = new FakeExecutor().EvalReturns("a", EvaluationResult.Ok("/*sql*/", 1, Row("A", 999)));
        var s = new DebugSession(Body(sql), exec);
        s.Start();

        s.Evaluate("a", EvaluationKind.Expression);

        Assert.False(s.CurrentFrame!.TryResolveValue("A", out _)); // untouched
    }

    [Fact]
    public void Evaluate_WhenNotPaused_Throws()
    {
        const string sql = "begin a = 1; end";
        var s = new DebugSession(Body(sql), new FakeExecutor());
        // Not started → not paused.
        Assert.Throws<InvalidOperationException>(() => s.Evaluate("a", EvaluationKind.Expression));
    }

    [Fact]
    public void Evaluate_EmptyFragment_Throws()
    {
        const string sql = "begin a = 1; end";
        var s = new DebugSession(Body(sql), new FakeExecutor());
        s.Start();
        Assert.Throws<ArgumentException>(() => s.Evaluate("   ", EvaluationKind.Expression));
    }

    // ── D9 seam c (§6.4): Step Into a local FUNCTION in the four value-consuming positions ─────────────
    // The interpreter descends into a local function only where its call is the ENTIRE operand (recognised
    // in ONE place, FunctionReturnContinuation.RecognizeStepInto), delivers its RETURN value to the caller
    // position client-side (a generalisation of RETURNING_VALUES), evaluates RETURN via the Expression Harness
    // (never the Statement Harness), and steps over an unresolved / stepped-over call. Fake-driven — no server.

    [Fact]
    public void StepInto_LocalFunction_Assignment_DeliversReturnValueToTarget_AndSavepoints()
    {
        const string sql = "begin r = f(x); done = 1; end";
        const string fSql = "begin\n  return 42;\nend";
        var exec = new FakeExecutor()
            .FunctionNamed("F", new DebugRoutine("F", Body(fSql), returnType: "integer"))
            .ReturnAt(Off(fSql, "return 42"), 42);
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        var root = s.CurrentFrame!;
        Assert.Equal("r = f(x);", Text(sql, s.CurrentStatement!));

        s.Step(StepKind.Into); // step INTO f → push a function frame
        Assert.Equal(2, s.Depth);
        Assert.Equal("F", s.CurrentFrame!.RoutineName);
        Assert.True(s.CurrentFrame!.LexicalParent is not null); // a local function closes over its declarer
        Assert.Equal("return 42;", Text(fSql, s.CurrentStatement!));

        s.Step(StepKind.Into); // run RETURN 42 → deliver 42 into R, resume the caller past the assignment
        Assert.Equal(1, s.Depth);
        Assert.Equal("done = 1;", Text(sql, s.CurrentStatement!));
        Assert.Equal(42, root.Values.Get("R"));
        Assert.Equal(new[] { "F" }, exec.FunctionsResolved);
        Assert.Contains(Off(fSql, "return 42"), exec.ReturnsEvaluated);
        // The assignment was delivered client-side — the leaf itself never ran on the server (§6.4).
        Assert.DoesNotContain(Off(sql, "r = f(x)"), exec.Executed);
        // Function frame got an entry savepoint and its release on normal return (§4.5).
        Assert.Contains("enter:ET_DBG_FRAME_1", exec.Savepoints);
        Assert.Contains("leave:ET_DBG_FRAME_1", exec.Savepoints);
    }

    [Fact]
    public void StepInto_LocalFunction_NestedReturn_PropagatesThroughFrames()
    {
        // r = g(a); where g's body is `RETURN f(b);` and f's body is `RETURN 7;` — stepping into g then into f
        // must propagate 7 up: f → g's return value → the caller's R. Distinct source shapes ⇒ distinct offsets.
        const string sql = "begin r = g(a); done = 1; end";
        const string gSql = "begin\n  return f(b);\nend";
        const string fSql = "begin\n\n   return 7;\nend";
        var exec = new FakeExecutor()
            .FunctionNamed("G", new DebugRoutine("G", Body(gSql), returnType: "integer"))
            .FunctionNamed("F", new DebugRoutine("F", Body(fSql), returnType: "integer"))
            .ReturnAt(Off(fSql, "return 7"), 7);
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        var root = s.CurrentFrame!;

        s.Step(StepKind.Into); // into G → return f(b);
        Assert.Equal(2, s.Depth);
        Assert.Equal("return f(b);", Text(gSql, s.CurrentStatement!));

        s.Step(StepKind.Into); // step INTO f (RETURN f(b)) → return 7;
        Assert.Equal(3, s.Depth);
        Assert.Equal("F", s.CurrentFrame!.RoutineName);

        s.Step(StepKind.Into); // run RETURN 7 → propagate to G's return → deliver to R; both frames unwind
        Assert.Equal(1, s.Depth);
        Assert.Equal(7, root.Values.Get("R"));
        Assert.Equal("done = 1;", Text(sql, s.CurrentStatement!));
        Assert.Equal(new[] { "G", "F" }, exec.FunctionsResolved);
    }

    [Fact]
    public void StepInto_LocalFunction_IfCondition_TakesThenBranch_WhenTrue()
    {
        const string sql = "begin if (f(x)) then taken = 1; else other = 2; end";
        const string fSql = "begin\n  return 1;\nend";
        var exec = new FakeExecutor()
            .FunctionNamed("F", new DebugRoutine("F", Body(fSql), returnType: "boolean"))
            .ReturnAt(Off(fSql, "return 1"), true);
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        Assert.StartsWith("if (f(x)) then", Text(sql, s.CurrentStatement!));

        s.Step(StepKind.Into); // into the condition function
        Assert.Equal(2, s.Depth);
        s.Step(StepKind.Into); // RETURN true → decide the branch, back in the caller
        Assert.Equal(1, s.Depth);
        Assert.Equal("taken = 1;", Text(sql, s.CurrentStatement!));
    }

    [Fact]
    public void StepInto_LocalFunction_IfCondition_TakesElseBranch_WhenFalse()
    {
        const string sql = "begin if (f(x)) then taken = 1; else other = 2; end";
        const string fSql = "begin\n  return 0;\nend";
        var exec = new FakeExecutor()
            .FunctionNamed("F", new DebugRoutine("F", Body(fSql), returnType: "boolean"))
            .ReturnAt(Off(fSql, "return 0"), false);
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        s.Step(StepKind.Into); // into f
        s.Step(StepKind.Into); // RETURN false → ELSE
        Assert.Equal(1, s.Depth);
        Assert.Equal("other = 2;", Text(sql, s.CurrentStatement!));
    }

    [Fact]
    public void StepInto_LocalFunction_WhileCondition_IteratesUntilFalse()
    {
        const string sql = "begin while (f(x)) do cnt = cnt + 1; end";
        const string fSql = "begin\n  return c;\nend";
        var exec = new FakeExecutor()
            .FunctionNamed("F", new DebugRoutine("F", Body(fSql), returnType: "boolean"))
            .ReturnAt(Off(fSql, "return c"), true, true, false); // two iterations, then stop
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        var body = Off(sql, "cnt = cnt + 1");
        int guard = 0;
        s.Start();
        while (s.State == DebugState.Paused) { Assert.True(guard++ < 100); s.Step(StepKind.Into); }

        Assert.Equal(DebugState.Completed, s.State);
        Assert.Equal(3, exec.FunctionsResolved.Count);      // condition evaluated once per iteration decision
        Assert.Equal(2, exec.Executed.FindAll(o => o == body).Count); // body ran on the two true iterations
    }

    [Fact]
    public void StepInto_LocalFunction_Unresolved_StepsOverInPlace()
    {
        // f is not a registered local function ⇒ ResolveFunction returns null ⇒ the whole assignment runs on
        // the server (step-over, 100% faithful) — no frame is pushed.
        const string sql = "begin r = f(x); done = 1; end";
        var exec = new FakeExecutor(); // no FunctionNamed
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Into);
        Assert.Equal(1, s.Depth);
        Assert.Contains(Off(sql, "r = f(x)"), exec.Executed); // executed in place, not descended
        Assert.Empty(exec.FunctionsResolved);                 // ResolveFunction was asked but found nothing
    }

    [Fact]
    public void StepOver_LocalFunctionCall_RunsInPlace_EvenWhenResolvable()
    {
        // Step Over ignores the call entirely — even a resolvable local function runs on the server.
        const string sql = "begin r = f(x); done = 1; end";
        const string fSql = "begin\n  return 5;\nend";
        var exec = new FakeExecutor().FunctionNamed("F", new DebugRoutine("F", Body(fSql), returnType: "integer"));
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Over);
        Assert.Equal(1, s.Depth);
        Assert.Contains(Off(sql, "r = f(x)"), exec.Executed);
        Assert.Empty(exec.FunctionsResolved); // never attempted — Step Over doesn't recognise the call
    }

    [Fact]
    public void StepInto_UnresolvedIfConditionCall_EvaluatesConditionOnServer()
    {
        // IF whose condition is a call to a NON-local function: falls through to EvaluateCondition (the server
        // evaluates the whole condition) — no frame, no client-side branch decision.
        const string sql = "begin if (f(x)) then a = 1; end";
        var exec = new FakeExecutor(defaultCondition: true); // no FunctionNamed → ResolveFunction null
        var s = new DebugSession(Body(sql), exec);
        s.Start();
        s.Step(StepKind.Into);
        Assert.Equal(1, s.Depth);
        Assert.Contains(Off(sql, "if (f(x)) then"), exec.ConditionsEvaluated);
    }

    [Fact]
    public void StepInto_LocalFunction_ThatRaises_DoesNotFireTheContinuation()
    {
        // A raising function unwinds via the ExceptionRouter (savepoint rollback); its return continuation must
        // NOT fire — the assignment target is never written — identical to a raising procedure.
        const string sql = "begin r = f(x); after = 1; end";
        const string fSql = "begin\n  bad = 1;\nend";
        var exec = new FakeExecutor()
            .FunctionNamed("F", new DebugRoutine("F", Body(fSql), returnType: "integer"))
            .Outcome(Off(fSql, "bad = 1"), StatementOutcome.Raised(new DebugError(ExceptionName: "E_BOOM")));
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        var root = s.CurrentFrame!;
        s.Step(StepKind.Into); // into f → bad = 1;
        Assert.Equal(2, s.Depth);
        s.Step(StepKind.Into); // bad = 1; raises → no handler → fault; continuation must NOT run

        Assert.Equal(DebugState.Faulted, s.State);
        Assert.False(root.Values.Contains("R")); // the assignment continuation never fired
        Assert.Contains("rollback:ET_DBG_FRAME_1", exec.Savepoints); // the function frame was rolled back
    }

    [Fact]
    public void StepInto_LocalFunction_WithPlainReturnValue_UsesEvaluateReturn_NotStatementHarness()
    {
        // RETURN <expr> where <expr> is NOT a call goes through EvaluateReturn (Expression Harness), never
        // ExecuteStatement (Statement Harness) — a bare RETURN is invalid inside EXECUTE BLOCK.
        const string sql = "begin r = f(x); end";
        const string fSql = "begin\n  return a + 1;\nend";
        var exec = new FakeExecutor()
            .FunctionNamed("F", new DebugRoutine("F", Body(fSql), returnType: "integer"))
            .ReturnAt(Off(fSql, "return a + 1"), 100);
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        var root = s.CurrentFrame!;
        s.Step(StepKind.Into); // into f → return a + 1;
        s.Step(StepKind.Into); // evaluate the RETURN operand → deliver to R
        Assert.Equal(100, root.Values.Get("R"));
        Assert.Contains(Off(fSql, "return a + 1"), exec.ReturnsEvaluated);
        Assert.DoesNotContain(Off(fSql, "return a + 1"), exec.Executed); // NOT run as a statement
    }
}
