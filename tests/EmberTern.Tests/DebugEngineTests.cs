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

        public FakeExecutor RoutineAt(int start, DebugRoutine routine)
        {
            _routines[start] = routine;
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

        public IDebugCursor OpenCursor(ForSelectStatement loop, Frame frame)
        {
            var rows = _cursorRows.TryGetValue(loop.Start, out var r) ? r : new List<IReadOnlyDictionary<string, object?>>();
            var cursor = new FakeCursor(rows);
            Cursors.Add(cursor);
            return cursor;
        }

        public DebugRoutine? ResolveRoutine(IExecutableStatement call, Frame frame)
            => _routines.TryGetValue(call.Start, out var routine) ? routine : null;

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
    public void ScopeChain_InnerFrameResolvesAndWritesOuterVariable()
    {
        // The scope-chain mechanism the flagship (D9) local routines build on: a callee frame's lexical
        // parent is its caller, so it resolves and can write-back a variable defined in the parent.
        const string sql = "begin a = 1; execute procedure p; end";
        var callee = new DebugRoutine("P", Body(CalleeSql));
        var exec = new FakeExecutor()
            .Outcome(Off(sql, "a = 1"), StatementOutcome.Normal(Row("V_OUTER", 5)))
            .RoutineAt(Off(sql, "execute procedure p"), callee);
        var s = new DebugSession(Body(sql), exec, rootName: "ROOT");
        s.Start();
        s.Step(StepKind.Into); // a = 1; → ROOT frame gets V_OUTER = 5
        s.Step(StepKind.Into); // step INTO p → callee frame, parent = ROOT

        var child = s.CurrentFrame!;
        Assert.Equal("P", child.RoutineName);
        Assert.True(child.TryResolveValue("V_OUTER", out var v)); // resolves up the chain to ROOT
        Assert.Equal(5, v);
        Assert.False(child.TryResolveValue("NOPE", out _));

        child.SetResolvedValue("V_OUTER", 99);          // closure write reaches the defining (ROOT) frame
        var root = s.CallStack[^1];                       // CallStack is innermost-first → last is ROOT
        Assert.Equal("ROOT", root.RoutineName);
        Assert.Equal(99, root.Values.Get("V_OUTER"));
        Assert.False(child.Values.Contains("V_OUTER"));   // not shadowed locally
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
}
