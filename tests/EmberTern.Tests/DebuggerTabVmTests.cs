using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Debugging;
using EmberTern.App.ViewModels;
using EmberTern.Core.Settings;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, milestone D4 (the debugger tab MVP). These drive
/// <see cref="DebuggerTabViewModel"/> against a fake <see cref="IDebugSessionLauncher"/> that builds a
/// <see cref="DebugSession"/> over a scripted fake <see cref="IDebugExecutor"/> — no server, no UI — the same
/// approach as <c>DebugEngineTests</c>. They pin the VM's lifecycle wiring: preparation derives the launch
/// parameters + pre-flight, launch pauses at entry, stepping advances the current statement, Continue runs to
/// completion, an unhandled raise faults, Stop tears the run down, and breakpoints snap to a step point.
/// </summary>
public class DebuggerTabVmTests
{
    private const string Sql = """
        create procedure sp_test (a integer, b integer) returns (r integer) as
        declare v integer;
        begin
          v = a + b;
          r = v;
        end
        """;

    // ── Fakes ─────────────────────────────────────────────────────────────────────────────────────

    // Default-Normal executor: every leaf runs normally (optionally with scripted writes), every condition
    // is true; no cursors, no step-into. Records savepoint ops. Mirrors DebugEngineTests' scripted fake.
    private sealed class FakeExecutor : IDebugExecutor
    {
        private readonly Dictionary<int, IReadOnlyDictionary<string, object?>> _writes = new();
        private readonly HashSet<int> _raises = new();

        public FakeExecutor Write(int start, IReadOnlyDictionary<string, object?> writes) { _writes[start] = writes; return this; }
        public FakeExecutor Raise(int start) { _raises.Add(start); return this; }

        public StatementOutcome ExecuteStatement(IExecutableStatement s, Frame frame)
        {
            if (_raises.Contains(s.Start)) return StatementOutcome.Raised(new DebugError(ExceptionName: "E_TEST", Message: "boom"));
            return _writes.TryGetValue(s.Start, out var w) ? StatementOutcome.Normal(w) : StatementOutcome.Normal();
        }

        public ConditionOutcome EvaluateCondition(IExecutableStatement owner, Frame frame) => ConditionOutcome.True;

        private readonly Dictionary<string, EvaluationResult> _evals = new(StringComparer.OrdinalIgnoreCase);
        public List<EvaluationRequest> Evaluations { get; } = new();
        public FakeExecutor Eval(string fragment, EvaluationResult result) { _evals[fragment] = result; return this; }
        public EvaluationResult Evaluate(EvaluationRequest request, Frame frame)
        {
            Evaluations.Add(request);
            return _evals.TryGetValue(request.Fragment, out var r)
                ? r
                : EvaluationResult.Ok($"/*eval*/ {request.Fragment}", request.Fragment, null);
        }

        public IDebugCursor OpenCursor(ForSelectStatement loop, Frame frame) => throw new NotSupportedException();

        // Step-into: a scripted callee (set via WithCallee) resolved for any EXECUTE PROCEDURE step point,
        // else null (run in place = step-over). Enough to exercise the multi-frame VM (call stack, per-frame
        // roster/source, breadcrumbs, frame nav) without a server.
        private DebugRoutine? _callee;
        public FakeExecutor WithCallee(DebugRoutine callee) { _callee = callee; return this; }
        public DebugRoutine? ResolveRoutine(IExecutableStatement call, Frame frame)
            => call is ExecuteProcedureStatement ? _callee : null;

        // D9 seam c — the VM tests never step into a local function; the null resolver keeps every call a
        // step-over, and EvaluateReturn is then unreachable (no function frame is ever pushed).
        public DebugRoutine? ResolveFunction(CallExpression call, Frame frame) => null;
        public ReturnOutcome EvaluateReturn(IExecutableStatement returnStatement, Frame frame)
            => throw new NotSupportedException();

        public void EnterFrameSavepoint(string name) { }
        public void LeaveFrameSavepoint(string name) { }
        public void RollbackFrameSavepoint(string name) { }
    }

    private sealed class FakeLauncher : IDebugSessionLauncher
    {
        private readonly IDebugExecutor _executor;
        public bool Disposed { get; private set; }
        public DebugLaunchSpec? LastSpec { get; private set; }

        public FakeLauncher(IDebugExecutor executor) => _executor = executor;

        public Task<DebugRunHandle> LaunchAsync(DebugLaunchSpec spec, CancellationToken cancellationToken = default)
        {
            LastSpec = spec;
            var session = new DebugSession(
                spec.Body, _executor, spec.RoutineName, spec.RootValues, spec.Source, spec.Model);
            session.Start();
            return Task.FromResult(new DebugRunHandle(session, () => { Disposed = true; return ValueTask.CompletedTask; }));
        }
    }

    private static DebuggerTabViewModel Vm(string sql, IDebugExecutor executor, out FakeLauncher launcher)
    {
        launcher = new FakeLauncher(executor);
        return new DebuggerTabViewModel("SP_TEST", _ => Task.FromResult<string?>(sql), launcher);
    }

    private static int Off(string sub) => Sql.IndexOf(sub, StringComparison.Ordinal);

    // ── Preparation ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Prepare_DerivesInputParameters_AndReadiesLaunch()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();

        Assert.Equal(DebuggerPhase.ReadyToLaunch, vm.Phase);
        Assert.False(vm.LaunchBlocked);
        Assert.NotNull(vm.Parameters);
        Assert.Equal(new[] { "A", "B" }, vm.Parameters!.Params.Select(p => p.Name));
        Assert.Equal(Sql, vm.SourceText);
    }

    [Fact]
    public async Task Prepare_Preflight_FlagsAutonomousTransactionAndGenerator()
    {
        const string sql = """
            create procedure sp_side as
            declare v integer;
            begin
              v = gen_id(g_seq, 1);
              in autonomous transaction do
                insert into audit (msg) values ('x');
            end
            """;
        var vm = new DebuggerTabViewModel("SP_SIDE", _ => Task.FromResult<string?>(sql), new FakeLauncher(new FakeExecutor()));
        await vm.PrepareAsync();

        Assert.Contains(vm.Preflight, i => i.Message == EmberTern.App.UiStrings.DebuggerPreflightGenerator);
        Assert.Contains(vm.Preflight, i => i.Message == EmberTern.App.UiStrings.DebuggerPreflightAutonomousTx);
    }

    [Fact]
    public async Task Prepare_UnsteppableSource_BlocksLaunch()
    {
        var vm = new DebuggerTabViewModel("X", _ => Task.FromResult<string?>("this is not a routine"),
            new FakeLauncher(new FakeExecutor()));
        await vm.PrepareAsync();

        Assert.True(vm.LaunchBlocked);
        Assert.Contains(vm.Preflight, i => i.IsBlocking);
    }

    [Fact]
    public async Task Prepare_MissingSource_FailsGracefully()
    {
        var vm = new DebuggerTabViewModel("X", _ => Task.FromResult<string?>(null), new FakeLauncher(new FakeExecutor()));
        await vm.PrepareAsync();

        Assert.Equal(DebuggerPhase.Idle, vm.Phase);
        Assert.True(vm.LaunchBlocked);
    }

    // ── Launch / stepping ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Launch_StartsSession_PausedAtEntry_WithVariables()
    {
        var vm = Vm(Sql, new FakeExecutor(), out var launcher);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.True(vm.IsDebugViewVisible);
        Assert.Equal(Off("v = a + b"), vm.CurrentStart);
        Assert.NotNull(launcher.LastSpec);
        // The roster is the declared symbols: params A, B, output R, local V.
        Assert.Contains(vm.Variables, r => r.Name == "A");
        Assert.Contains(vm.Variables, r => r.Name == "V");
    }

    [Fact]
    public async Task CallStack_ShowsRootFrame_WhilePaused_ClearedOnStop()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        // Paused at entry → a single-frame call stack: the launched routine, current, not simulated.
        Assert.True(vm.HasCallStack);
        var frame = Assert.Single(vm.CallStack);
        Assert.Equal("SP_TEST", frame.RoutineName);
        Assert.True(frame.IsCurrent);
        Assert.False(frame.IsSimulated);          // the root frame is not a step-into simulation (§5.3)
        Assert.False(string.IsNullOrEmpty(frame.LineText)); // line computed from the frame's own source

        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.HasCallStack);
        Assert.Empty(vm.CallStack);
    }

    [Fact]
    public async Task StepOver_AdvancesCurrentStatement_ThenCompletes()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        await vm.StepOverCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Equal(Off("r = v"), vm.CurrentStart);

        await vm.StepOverCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
        Assert.Null(vm.CurrentStart);
    }

    [Fact]
    public async Task Continue_RunsToCompletion()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
    }

    [Fact]
    public async Task StepOver_AppliesWriteBack_ToVariables()
    {
        var writes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["V"] = 15 };
        var vm = Vm(Sql, new FakeExecutor().Write(Off("v = a + b"), writes), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        await vm.StepOverCommand.ExecuteAsync(null);

        var v = vm.Variables.First(r => r.Name == "V");
        Assert.Equal("15", v.ValueText);
    }

    // ── D7 — Variables window (grouping / change-highlight / pins / filter) ─────────────────────────

    [Fact]
    public async Task Variables_AreGrouped_ByKind()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        var parameters = vm.VariableGroups.Single(g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupParameters);
        var locals = vm.VariableGroups.Single(g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupLocals);
        // Params A, B and the OUTPUT param R are in Parameters; the local V is in Locals.
        Assert.Equal(new[] { "A", "B", "R" }, parameters.Rows.Select(r => r.Name));
        Assert.Equal(new[] { "V" }, locals.Rows.Select(r => r.Name));
        // The output parameter is distinguished from the inputs.
        Assert.Equal(DebugVariableKind.ParameterOut, parameters.Rows.Single(r => r.Name == "R").Kind);
        Assert.Equal(DebugVariableKind.ParameterIn, parameters.Rows.Single(r => r.Name == "A").Kind);
    }

    [Fact]
    public async Task StepOver_HighlightsChangedVariable_Only()
    {
        var writes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["V"] = 15 };
        var vm = Vm(Sql, new FakeExecutor().Write(Off("v = a + b"), writes), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        // Nothing is "changed" on first entry into the frame (no baseline yet).
        Assert.All(vm.Variables, r => Assert.False(r.IsChanged));

        await vm.StepOverCommand.ExecuteAsync(null);
        Assert.True(vm.Variables.First(r => r.Name == "V").IsChanged);
        Assert.False(vm.Variables.First(r => r.Name == "A").IsChanged);
    }

    [Fact]
    public async Task TogglePin_MovesVariable_ToPinnedGroup()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        var v = vm.Variables.First(r => r.Name == "V");
        vm.TogglePinCommand.Execute(v);

        var pinned = vm.VariableGroups.Single(g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupPinned);
        Assert.Same(v, pinned.Rows.Single());
        // The pinned group sorts to the very top.
        Assert.Same(pinned, vm.VariableGroups.First());
        // V is no longer in Locals (which, now empty, is hidden entirely).
        Assert.DoesNotContain(vm.VariableGroups, g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupLocals);
    }

    [Fact]
    public async Task VariableFilter_NarrowsGroups_ByName()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        vm.VariableFilter = "V";
        // Only the local V matches; the Parameters group (A/B/R) is now empty and hidden.
        Assert.DoesNotContain(vm.VariableGroups, g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupParameters);
        var locals = vm.VariableGroups.Single(g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupLocals);
        Assert.Equal(new[] { "V" }, locals.Rows.Select(r => r.Name));

        // Clearing the filter restores every group.
        vm.VariableFilter = string.Empty;
        Assert.Contains(vm.VariableGroups, g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupParameters);
    }

    [Fact]
    public async Task InlineEdit_CommitsValidValue_ToTheFrame()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        var v = vm.Variables.First(r => r.Name == "V");
        vm.BeginEditCommand.Execute(v);
        Assert.True(v.IsEditing);

        v.EditText = "123";
        vm.CommitEditCommand.Execute(v);

        Assert.False(v.IsEditing);
        Assert.False(v.HasEditError);
        Assert.Equal("123", v.ValueText);
        // Stepping over "r = v" now reads the injected V (the frame holds it) — proven via the write-back path
        // is unnecessary here; the row reflects the committed value, which is the client-side frame truth.
    }

    [Fact]
    public async Task InlineEdit_RejectsUnparsableValue_ForTypedVariable()
    {
        var vm = Vm(Sql, new FakeExecutor().Write(Off("v = a + b"), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["V"] = 7 }), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        await vm.StepOverCommand.ExecuteAsync(null); // V is now an int (7)

        var v = vm.Variables.First(r => r.Name == "V");
        vm.BeginEditCommand.Execute(v);
        v.EditText = "not-an-integer";
        vm.CommitEditCommand.Execute(v);

        // Shape validation at edit time: rejected, still editing, value unchanged (§F — never a guessed value).
        Assert.True(v.IsEditing);
        Assert.True(v.HasEditError);
        Assert.Equal("7", v.ValueText);
    }

    [Fact]
    public async Task InlineEdit_BinaryBlob_IsNotEditable()
    {
        var vm = Vm(Sql, new FakeExecutor().Write(Off("v = a + b"), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["V"] = new byte[] { 1, 2, 3 } }), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        await vm.StepOverCommand.ExecuteAsync(null);

        var v = vm.Variables.First(r => r.Name == "V");
        Assert.False(v.IsEditable);
        vm.BeginEditCommand.Execute(v);
        Assert.False(v.IsEditing); // begin is a no-op for a binary BLOB (it is viewed, not text-edited)
    }

    [Fact]
    public async Task UnhandledRaise_Faults()
    {
        var vm = Vm(Sql, new FakeExecutor().Raise(Off("v = a + b")), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Faulted, vm.Phase);
    }

    // ── Stop / breakpoints ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Stop_TearsDownRun_AndClears()
    {
        var vm = Vm(Sql, new FakeExecutor(), out var launcher);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.True(launcher.Disposed);
        Assert.Equal(DebuggerPhase.Idle, vm.Phase);
        Assert.Empty(vm.Variables);
        Assert.Null(vm.CurrentStart);
    }

    [Fact]
    public async Task ToggleBreakpoint_SnapsToStepPointStart()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();

        // A caret anywhere on the "r = v;" line snaps to that statement's start offset.
        vm.ToggleBreakpointAt(Off("r = v") + 2);
        Assert.Contains(Off("r = v"), vm.BreakpointOffsets);

        vm.ToggleBreakpointAt(Off("r = v") + 2);
        Assert.DoesNotContain(Off("r = v"), vm.BreakpointOffsets);
    }

    [Fact]
    public async Task Breakpoint_StopsContinue_AtTheMarkedStatement()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        vm.ToggleBreakpointAt(Off("r = v"));
        await vm.LaunchCommand.ExecuteAsync(null);

        // Paused at entry (v = a + b); Continue must stop at the breakpoint on r = v.
        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Equal(Off("r = v"), vm.CurrentStart);
    }

    // ── Expression evaluation (Evaluate / Immediate — D5, §9.5) ────────────────────────────────────

    private static async Task<DebuggerTabViewModel> LaunchedAsync(IDebugExecutor executor)
    {
        var vm = Vm(Sql, executor, out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Immediate_Expression_AppendsResultToExecutedSql_AndKeepsInput()
    {
        var exec = new FakeExecutor().Eval("a + b", EvaluationResult.Ok("EXECUTE BLOCK ...", 7, null));
        var vm = await LaunchedAsync(exec);

        vm.ImmediateInput = "a + b";
        await vm.EvaluateImmediateCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.True(vm.HasExecutedSql);
        var row = Assert.Single(vm.ExecutedSql);
        Assert.Equal("a + b", row.Fragment);
        Assert.Equal("7", row.ResultText);
        Assert.False(row.IsError);
        Assert.Equal("EXECUTE BLOCK ...", row.Sql); // the harness is kept for the §10.3 audit
        Assert.Equal("a + b", vm.ImmediateInput);   // kept — so the user can tweak and re-run
    }

    [Fact]
    public async Task ClearImmediate_EmptiesTheInput()
    {
        var vm = await LaunchedAsync(new FakeExecutor());
        vm.ImmediateInput = "a + b";
        Assert.True(vm.HasImmediateInput);
        Assert.True(vm.ClearImmediateCommand.CanExecute(null));

        vm.ClearImmediateCommand.Execute(null);
        Assert.Empty(vm.ImmediateInput);
        Assert.False(vm.HasImmediateInput);
        Assert.False(vm.ClearImmediateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Immediate_Statement_FlagsSideEffect_AndUpdatesLiveVariables()
    {
        var writes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["V"] = 99 };
        var exec = new FakeExecutor().Eval("v = 99", EvaluationResult.Ok("EXECUTE BLOCK ...", null, writes));
        var vm = await LaunchedAsync(exec);

        vm.ImmediateAsStatement = true;
        vm.ImmediateInput = "v = 99";
        await vm.EvaluateImmediateCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.ExecutedSql);
        Assert.True(row.HasSideEffect);
        var v = vm.Variables.First(r => r.Name == "V");
        Assert.Equal("99", v.ValueText); // the live frame was updated (§9.5)
        // The engine got Statement mode.
        Assert.Equal(EvaluationKind.Statement, exec.Evaluations.Single().Kind);
    }

    [Fact]
    public async Task Immediate_ServerError_ShowsErrorRow_AndKeepsInput()
    {
        var exec = new FakeExecutor().Eval("bad expr", EvaluationResult.Failed("EXECUTE BLOCK ...", new DebugError(Message: "boom")));
        var vm = await LaunchedAsync(exec);

        vm.ImmediateInput = "bad expr";
        await vm.EvaluateImmediateCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.ExecutedSql);
        Assert.True(row.IsError);
        Assert.Equal("boom", row.ResultText);
        Assert.Equal("bad expr", vm.ImmediateInput); // kept so the user can edit and retry
    }

    [Fact]
    public async Task EvaluateSelection_RoutesThroughTheSameEngine()
    {
        var exec = new FakeExecutor().Eval("a", EvaluationResult.Ok("EXECUTE BLOCK ...", 1, null));
        var vm = await LaunchedAsync(exec);

        await vm.EvaluateSelectionAsync("a"); // Shift+F9 path (selection)

        var req = Assert.Single(exec.Evaluations);
        Assert.Equal(EvaluationKind.Expression, req.Kind);
        Assert.Equal("1", vm.ExecutedSql.Single().ResultText);
    }

    [Fact]
    public async Task Immediate_CannotEvaluate_WhenNotPaused()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync(); // ReadyToLaunch, not paused
        vm.ImmediateInput = "a + b";
        Assert.False(vm.EvaluateImmediateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Stop_ClearsExecutedSql()
    {
        var exec = new FakeExecutor().Eval("a", EvaluationResult.Ok("sql", 1, null));
        var vm = await LaunchedAsync(exec);
        vm.ImmediateInput = "a";
        await vm.EvaluateImmediateCommand.ExecuteAsync(null);
        Assert.True(vm.HasExecutedSql);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.HasExecutedSql);
        Assert.Empty(vm.ExecutedSql);
    }

    // ── Watches (D5 seam b, §9.5) ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddWatch_WhenPaused_EvaluatesImmediately_AndClearsInput()
    {
        var exec = new FakeExecutor().Eval("a + b", EvaluationResult.Ok("sql", 42, null));
        var vm = await LaunchedAsync(exec);

        vm.WatchInput = "a + b";
        await vm.AddWatchCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Watches);
        Assert.Equal("a + b", row.Expression);
        Assert.True(row.Evaluated);
        Assert.Equal("42", row.ValueText);
        Assert.False(row.IsError);
        Assert.Empty(vm.WatchInput);
        Assert.True(vm.HasWatches);
    }

    [Fact]
    public async Task Watch_ReEvaluates_AfterEachStep()
    {
        var exec = new FakeExecutor().Eval("v", EvaluationResult.Ok("sql", 1, null));
        var vm = await LaunchedAsync(exec);
        vm.WatchInput = "v";
        await vm.AddWatchCommand.ExecuteAsync(null);
        Assert.Equal("1", vm.Watches[0].ValueText);

        exec.Eval("v", EvaluationResult.Ok("sql", 2, null)); // the value changes at the next frame
        await vm.StepOverCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Equal("2", vm.Watches[0].ValueText); // auto re-evaluated at the new pause
    }

    [Fact]
    public async Task AddWatch_FlagsSideEffect_ForNonPureExpression()
    {
        var vm = await LaunchedAsync(new FakeExecutor());
        vm.WatchInput = "update t set x = 1";
        await vm.AddWatchCommand.ExecuteAsync(null);

        Assert.True(vm.Watches[0].HasSideEffect);
    }

    [Fact]
    public async Task RemoveWatch_RemovesTheRow()
    {
        var vm = await LaunchedAsync(new FakeExecutor());
        vm.WatchInput = "a";
        await vm.AddWatchCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.Watches);

        vm.RemoveWatchCommand.Execute(row);
        Assert.Empty(vm.Watches);
        Assert.False(vm.HasWatches);
    }

    [Fact]
    public async Task Stop_ResetsWatchValues_ButKeepsRows()
    {
        var exec = new FakeExecutor().Eval("a", EvaluationResult.Ok("sql", 5, null));
        var vm = await LaunchedAsync(exec);
        vm.WatchInput = "a";
        await vm.AddWatchCommand.ExecuteAsync(null);
        Assert.True(vm.Watches[0].Evaluated);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.Single(vm.Watches);             // the (persisted) row is kept
        Assert.False(vm.Watches[0].Evaluated); // its live value is reset
    }

    // ── Bottom-panel layout (presentation) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ToggleBottomPanel_FlipsCollapsedState()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        Assert.False(vm.IsBottomPanelCollapsed);

        vm.ToggleBottomPanelCommand.Execute(null);
        Assert.True(vm.IsBottomPanelCollapsed);
        vm.ToggleBottomPanelCommand.Execute(null);
        Assert.False(vm.IsBottomPanelCollapsed);
    }

    [Fact]
    public async Task LatestEvaluation_TracksNewestEvaluation_AndClearsOnStop()
    {
        var exec = new FakeExecutor().Eval("a + b", EvaluationResult.Ok("sql", 7, null));
        var vm = await LaunchedAsync(exec);
        Assert.False(vm.HasLatestEvaluation);

        vm.ImmediateInput = "a + b";
        await vm.EvaluateImmediateCommand.ExecuteAsync(null);
        Assert.True(vm.HasLatestEvaluation);
        Assert.Equal("a + b", vm.LatestEvaluation!.Fragment);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.HasLatestEvaluation);
    }

    // ── D8 seam (c) part 2 — call stack navigation + per-frame roster/source ────────────────────────

    // A root that immediately calls a stored callee, and the callee source. Stepping Into the call pushes a
    // second frame, so the VM has a real A→B stack to navigate.
    private const string RootSql = """
        create procedure sp_root (a integer) returns (r integer) as
        begin
          execute procedure sp_leaf(:a) returning_values :r;
        end
        """;

    private const string LeafSql = """
        create procedure sp_leaf (p integer) returns (q integer) as
        declare w integer;
        begin
          q = p;
          w = q;
        end
        """;

    // Builds a fake executor whose ResolveRoutine returns SP_LEAF (parsed into a real body + model + source),
    // so a Step Into pushes a genuine second frame carrying the callee's own model (the roster the panel
    // projects, spec §5.2) and source.
    private static FakeExecutor NestedExecutor()
    {
        var leafModel = SemanticModel.Build(SqlParser.Parse(LeafSql).Root);
        var leafBody = leafModel.Syntax.Statements.OfType<DdlStatement>().First(d => d.Body is not null).Body!;
        var callee = new DebugRoutine(
            "SP_LEAF", leafBody, initialValues: null, outputParameterNames: new[] { "Q" },
            lexicalParent: null, source: LeafSql, model: leafModel);
        return new FakeExecutor().WithCallee(callee);
    }

    private static async Task<DebuggerTabViewModel> LaunchedNestedAsync()
    {
        var launcher = new FakeLauncher(NestedExecutor());
        var vm = new DebuggerTabViewModel("SP_ROOT", _ => Task.FromResult<string?>(RootSql), launcher);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null); // paused at the EXECUTE PROCEDURE (the first step point)
        await vm.StepIntoCommand.ExecuteAsync(null); // step into SP_LEAF → a second frame
        return vm;
    }

    [Fact]
    public async Task StepInto_PushesCalleeFrame_SwitchesSourceAndRoster()
    {
        var vm = await LaunchedNestedAsync();

        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        // Two frames, innermost-first: SP_LEAF (current, simulated) then SP_ROOT (caller).
        Assert.Equal(2, vm.CallStack.Count);
        Assert.Equal("SP_LEAF", vm.CallStack[0].RoutineName);
        Assert.True(vm.CallStack[0].IsCurrent);
        Assert.True(vm.CallStack[0].IsSimulated);       // reached by Step Into (§5.3)
        Assert.Equal("SP_ROOT", vm.CallStack[1].RoutineName);
        Assert.False(vm.CallStack[1].IsCurrent);

        // The editor now shows the CALLEE's source, and Variables project the CALLEE's own roster.
        Assert.Equal(LeafSql, vm.SourceText);
        Assert.Contains(vm.Variables, r => r.Name == "P");
        Assert.Contains(vm.Variables, r => r.Name == "W");
        Assert.DoesNotContain(vm.Variables, r => r.Name == "A"); // A/R belong to the caller, not this frame

        // Breadcrumbs mirror the stack, outermost→innermost, current = last.
        Assert.Equal(new[] { "SP_ROOT", "SP_LEAF" }, vm.Breadcrumbs);
        Assert.Equal(1, vm.SelectedBreadcrumbIndex);
    }

    [Fact]
    public async Task SelectingCallerFrame_RepointsSourceRosterAndMarker()
    {
        var vm = await LaunchedNestedAsync();

        // Select the caller (SP_ROOT) row.
        vm.SelectedFrameRow = vm.CallStack[1];

        Assert.Equal(RootSql, vm.SourceText);                                  // source switched back
        Assert.Contains(vm.Variables, r => r.Name == "A");                     // caller's roster
        Assert.DoesNotContain(vm.Variables, r => r.Name == "P");
        // The current-line marker is the call site (where SP_ROOT called SP_LEAF), in SP_ROOT's own source.
        Assert.Equal(RootSql.IndexOf("execute procedure", StringComparison.Ordinal), vm.CurrentStart);
        Assert.Equal(0, vm.SelectedBreadcrumbIndex);                           // SP_ROOT = outermost crumb
    }

    [Fact]
    public async Task MoveFrameSelection_WalksTheStack_BothDirections()
    {
        var vm = await LaunchedNestedAsync(); // selection starts at the innermost frame (SP_LEAF)

        vm.MoveFrameSelection(+1); // down the list → the caller SP_ROOT
        Assert.Equal(RootSql, vm.SourceText);
        Assert.Same(vm.CallStack[1], vm.SelectedFrameRow);

        vm.MoveFrameSelection(-1); // back up → the callee SP_LEAF
        Assert.Equal(LeafSql, vm.SourceText);
        Assert.Same(vm.CallStack[0], vm.SelectedFrameRow);
    }

    [Fact]
    public async Task Breakpoints_AreRootScoped_HiddenWhileViewingACallee()
    {
        var launcher = new FakeLauncher(NestedExecutor());
        var vm = new DebuggerTabViewModel("SP_ROOT", _ => Task.FromResult<string?>(RootSql), launcher);
        await vm.PrepareAsync();
        vm.ToggleBreakpointAt(RootSql.IndexOf("execute procedure", StringComparison.Ordinal));
        Assert.Single(vm.BreakpointOffsets); // set while viewing the root

        await vm.LaunchCommand.ExecuteAsync(null);
        await vm.StepIntoCommand.ExecuteAsync(null); // now viewing the callee
        Assert.Empty(vm.BreakpointOffsets);          // root breakpoints are not surfaced on the callee source

        vm.SelectedFrameRow = vm.CallStack[1];        // back to the root frame
        Assert.Single(vm.BreakpointOffsets);          // visible again
    }

    [Fact]
    public async Task StepInto_PeekFrame_ReturnsCalleeSource()
    {
        var vm = await LaunchedNestedAsync();

        var peek = vm.GetFramePeek(vm.CallStack[0].FrameId);
        Assert.NotNull(peek);
        Assert.Equal("SP_LEAF", peek!.RoutineName);
        Assert.Equal(LeafSql, peek.Source);
        Assert.True(peek.CurrentLine > 0);
    }

    [Fact]
    public async Task Watches_Persist_PerRoutine_AcrossVmInstances()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WatchStore(dir);
            var vm1 = new DebuggerTabViewModel(
                "SP_TEST", _ => Task.FromResult<string?>(Sql), new FakeLauncher(new FakeExecutor()),
                historyStore: null, connectionId: "c1", watchStore: store);
            await vm1.PrepareAsync();
            await vm1.LaunchCommand.ExecuteAsync(null);
            vm1.WatchInput = "a + b";
            await vm1.AddWatchCommand.ExecuteAsync(null);

            // A fresh VM for the same (connection, routine) loads the persisted watch in its ctor.
            var vm2 = new DebuggerTabViewModel(
                "SP_TEST", _ => Task.FromResult<string?>(Sql), new FakeLauncher(new FakeExecutor()),
                historyStore: null, connectionId: "c1", watchStore: store);
            Assert.Single(vm2.Watches);
            Assert.Equal("a + b", vm2.Watches[0].Expression);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
