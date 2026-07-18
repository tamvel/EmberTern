using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Debugging;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language.Ast;
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
        public DebugRoutine? ResolveRoutine(IExecutableStatement call, Frame frame) => null;
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
            var session = new DebugSession(spec.Body, _executor, spec.RoutineName, spec.RootValues);
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
}
