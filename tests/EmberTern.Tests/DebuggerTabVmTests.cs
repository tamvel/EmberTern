using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Debugging;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
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
    // internal (not private) so the headless view regression test in ConnectionExpandBindingProbe can reuse
    // this exact scripted executor — one test double, no parallel implementation.
    internal sealed class FakeExecutor : IDebugExecutor
    {
        private readonly Dictionary<int, IReadOnlyDictionary<string, object?>> _writes = new();
        private readonly HashSet<int> _raises = new();

        public FakeExecutor Write(int start, IReadOnlyDictionary<string, object?> writes) { _writes[start] = writes; return this; }
        public FakeExecutor Raise(int start) { _raises.Add(start); return this; }

        // SUSPEND rows scripted per offset (a queue — a loop re-executes the same SUSPEND) for the D12 Seam E2
        // run-to-SUSPEND / Results-grid tests.
        private readonly Dictionary<int, Queue<StatementOutcome>> _suspends = new();
        public FakeExecutor Suspend(int start, params IReadOnlyDictionary<string, object?>[] rows)
        {
            _suspends[start] = new Queue<StatementOutcome>(rows.Select(r => StatementOutcome.Suspended(r)));
            return this;
        }

        public StatementOutcome ExecuteStatement(IExecutableStatement s, Frame frame)
        {
            if (_raises.Contains(s.Start)) return StatementOutcome.Raised(new DebugError(ExceptionName: "E_TEST", Message: "boom"));
            if (_suspends.TryGetValue(s.Start, out var sq) && sq.Count > 0) return sq.Dequeue();
            return _writes.TryGetValue(s.Start, out var w) ? StatementOutcome.Normal(w) : StatementOutcome.Normal();
        }

        // WHILE/IF conditions scripted per offset (a queue) — else default true; lets a loop run a bounded number
        // of iterations for the run-to-SUSPEND test.
        private readonly Dictionary<int, Queue<bool>> _conds = new();
        public FakeExecutor Cond(int start, params bool[] values) { _conds[start] = new Queue<bool>(values); return this; }
        public ConditionOutcome EvaluateCondition(IExecutableStatement owner, Frame frame)
            => _conds.TryGetValue(owner.Start, out var q) && q.Count > 0 ? ConditionOutcome.Of(q.Dequeue()) : ConditionOutcome.True;

        // A breakpoint condition (a string fragment through the one engine) — scriptable per fragment so a
        // D12 conditional-breakpoint test can prove the condition set on a panel row reached the engine.
        private readonly Dictionary<string, bool> _stringConds = new(StringComparer.OrdinalIgnoreCase);
        public FakeExecutor CondString(string fragment, bool value) { _stringConds[fragment] = value; return this; }
        public ConditionOutcome EvaluateCondition(string fragment, int scopeOffset, Frame frame)
            => _stringConds.TryGetValue(fragment, out var v) ? ConditionOutcome.Of(v) : ConditionOutcome.True;

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

    internal sealed class FakeLauncher : IDebugSessionLauncher
    {
        private readonly IDebugExecutor _executor;
        public bool Disposed { get; private set; }
        public DebugLaunchSpec? LastSpec { get; private set; }

        public FakeLauncher(IDebugExecutor executor) => _executor = executor;

        public Task<DebugRunHandle> LaunchAsync(DebugLaunchSpec spec, CancellationToken cancellationToken = default)
        {
            LastSpec = spec;
            // Mirror the production launcher: share the spec's breakpoint / data-breakpoint sets and seed
            // BreakOnException before Start, so the session honours a breakpoint on the first statement from entry.
            var session = new DebugSession(
                spec.Body, _executor, spec.RoutineName, spec.RootValues, spec.Source, spec.Model,
                spec.Breakpoints, spec.DataBreakpoints);
            session.BreakOnException = spec.BreakOnException;
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

    // D15.3 Seam C — a routine with no decision to make (non-trigger, no input parameters, clean pre-flight)
    // launches straight through: Prepare goes Debug → session, skipping the launch panel (no Launch click).
    [Fact]
    public async Task Prepare_NoParametersCleanPreflight_AutoLaunches()
    {
        const string sql = """
            create procedure sp_noargs returns (r integer) as
            declare v integer;
            begin
              v = 1;
              r = v;
            end
            """;
        var vm = new DebuggerTabViewModel("SP_NOARGS", _ => Task.FromResult<string?>(sql), new FakeLauncher(new FakeExecutor()));
        await vm.PrepareAsync();

        // Went straight to the running session (paused at entry), never resting on the launch panel.
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.True(vm.IsDebugViewVisible);
        Assert.False(vm.IsLaunchPanelVisible);
        Assert.Empty(vm.Parameters!.Params);
    }

    // A routine WITH parameters keeps the launch panel — the user has a decision (the argument values).
    [Fact]
    public async Task Prepare_WithParameters_DoesNotAutoLaunch()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();

        Assert.Equal(DebuggerPhase.ReadyToLaunch, vm.Phase);
        Assert.True(vm.IsLaunchPanelVisible);
    }

    // ── F5 = the application-level "Go" router (D15.3) ──────────────────────────────────────────────

    // The debugger's response to F5: from the launch panel it Starts Debugging.
    [Fact]
    public async Task RequestGo_FromLaunchPanel_StartsDebugging()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        Assert.Equal(DebuggerPhase.ReadyToLaunch, vm.Phase);

        await vm.RequestGoAsync();

        Assert.Equal(DebuggerPhase.Paused, vm.Phase); // launched, paused at entry
    }

    // The debugger's response to F5: while paused it Continues (runs to completion here — no breakpoints).
    [Fact]
    public async Task RequestGo_WhilePaused_Continues()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.RequestGoAsync();
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);

        await vm.RequestGoAsync();

        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
    }

    // The debugger's response to F5 is a no-op in a non-actionable phase (e.g. a finished session) — F5 must
    // not throw or mis-fire. (Restart-on-F5 for a finished session is deliberately out of scope for now.)
    [Fact]
    public async Task RequestGo_WhenCompleted_IsNoOp()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.RequestGoAsync();   // launch
        await vm.RequestGoAsync();   // continue → Completed
        Assert.Equal(DebuggerPhase.Completed, vm.Phase);

        await vm.RequestGoAsync();   // F5 again — nothing to do

        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
    }

    // The WINDOW-level Go router (bound to F5) dispatches by the active workspace tab: with a Debugger tab
    // active it routes to the debugger (Start Debugging), NOT Execute Query. This is the fix for "F5 ran
    // Execute Query while a debugger tab was open" — the routing decision now lives in one window-level command
    // instead of being contested by a focus-dependent local key handler.
    [Fact]
    public async Task GoCommand_WithDebuggerTabActive_RoutesToDebugger()
    {
        var dir = Path.Combine(Path.GetTempPath(), "et-go-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var store = new ConnectionProfileStore(dir);
        using var service = new FirebirdConnectionService();
        var main = new MainWindowViewModel(store, service);

        var debugger = new DebuggerTabViewModel("SP_TEST", _ => Task.FromResult<string?>(Sql),
            new FakeLauncher(new FakeExecutor()));
        await debugger.PrepareAsync();
        Assert.Equal(DebuggerPhase.ReadyToLaunch, debugger.Phase);

        var tab = WorkspaceTabViewModel.CreateDebugger(main, debugger, "SP_TEST", null);
        main.WorkspaceTabs.Add(tab);
        main.SelectTab(tab);
        Assert.True(main.IsDebuggerTabActive);
        Assert.Same(debugger, main.ActiveDebugger);

        await main.GoCommand.ExecuteAsync(null); // F5

        Assert.Equal(DebuggerPhase.Paused, debugger.Phase); // launched via the router, not Execute Query
    }

    // A pre-flight note (a §4.6 data-safety warning is a decision) keeps the panel even with no parameters.
    [Fact]
    public async Task Prepare_NoParametersButPreflightNote_DoesNotAutoLaunch()
    {
        const string sql = """
            create procedure sp_side_noargs as
            declare v integer;
            begin
              v = gen_id(g_seq, 1);
            end
            """;
        var vm = new DebuggerTabViewModel("SP_SIDE_NOARGS", _ => Task.FromResult<string?>(sql), new FakeLauncher(new FakeExecutor()));
        await vm.PrepareAsync();

        Assert.Equal(DebuggerPhase.ReadyToLaunch, vm.Phase);
        Assert.True(vm.IsLaunchPanelVisible);
        Assert.NotEmpty(vm.Preflight);
    }

    // D15.3 Seam C boundary — a parameter with a DEFAULT (Firebird's "optional parameter": both `= v` and
    // `DEFAULT v` forms) is still a decision (accept the default OR override it), so the Fast Path must NOT
    // fire — the panel stays and the parameter is offered. Pins that the model surfaces defaulted params as
    // input parameters (if it dropped them, Params.Count would be 0 and the routine would wrongly auto-launch).
    [Fact]
    public async Task Prepare_ParametersWithDefaults_ShowPanel_NoAutoLaunch()
    {
        const string sql = """
            create procedure sp_defaults (a integer = 5, b integer default 10) returns (r integer) as
            begin
              r = a + b;
            end
            """;
        var vm = new DebuggerTabViewModel("SP_DEFAULTS", _ => Task.FromResult<string?>(sql), new FakeLauncher(new FakeExecutor()));
        await vm.PrepareAsync();

        Assert.Equal(DebuggerPhase.ReadyToLaunch, vm.Phase); // panel shown, NOT auto-launched
        Assert.True(vm.IsLaunchPanelVisible);
        Assert.Equal(new[] { "A", "B" }, vm.Parameters!.Params.Select(p => p.Name)); // both defaulted params offered
    }

    // D15.3 Seam B — the Advanced (transaction-isolation) section is collapsed by default (isolation is out
    // of the main flow), and toggles open on demand.
    [Fact]
    public void AdvancedSection_IsCollapsedByDefault_AndToggles()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        Assert.False(vm.IsAdvancedExpanded);
        vm.ToggleAdvancedCommand.Execute(null);
        Assert.True(vm.IsAdvancedExpanded);
        vm.ToggleAdvancedCommand.Execute(null);
        Assert.False(vm.IsAdvancedExpanded);
    }

    // D15.3 Seam D — Quick Relaunch was delivered by REUSE (Smart Parameters + ParameterHistoryStore + Seam C's
    // F5), not a separate implementation. These two tests prove the reuse works through the debugger's own path.

    // Pre-fill: opening the debugger launch form auto-applies the newest recorded parameter set for the routine
    // (so a repeat debug — even in a fresh tab / after an app restart — starts with the last-used arguments).
    [Fact]
    public async Task Prepare_PreFillsLaunchForm_WithNewestHistorySet()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EmberTern-dbg-" + Guid.NewGuid().ToString("N"));
        try
        {
            var inputs = new (string, string)[] { ("A", "INTEGER"), ("B", "INTEGER") };
            // Record a set under the SAME key the debugger uses (connection "c1", kind Procedure, name SP_TEST).
            var seed = new ExecuteProcedureDialogViewModel(inputs, "SP_TEST", "c1", "Procedure", new ParameterHistoryStore(dir));
            seed.Params[0].IsNull = false; seed.Params[0].NumericValue = 7m;
            seed.Params[1].IsNull = false; seed.Params[1].NumericValue = 9m;
            seed.AcceptCommand.Execute(null);

            var vm = new DebuggerTabViewModel("SP_TEST", _ => Task.FromResult<string?>(Sql),
                new FakeLauncher(new FakeExecutor()), new ParameterHistoryStore(dir), "c1");
            await vm.PrepareAsync();

            Assert.True(vm.Parameters!.HasHistory);
            Assert.Same(vm.Parameters.History[0], vm.Parameters.SelectedHistory); // newest auto-selected
            Assert.Equal(7m, vm.Parameters.Params[0].NumericValue);               // …and applied to the form
            Assert.Equal(9m, vm.Parameters.Params[1].NumericValue);
        }
        finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true); }
    }

    // Restart reuses the last set of values (no re-prompt): the relaunch spec carries the same arguments.
    [Fact]
    public async Task Restart_ReusesLastParameterValues()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EmberTern-dbg-" + Guid.NewGuid().ToString("N"));
        try
        {
            var inputs = new (string, string)[] { ("A", "INTEGER"), ("B", "INTEGER") };
            var seed = new ExecuteProcedureDialogViewModel(inputs, "SP_TEST", "c1", "Procedure", new ParameterHistoryStore(dir));
            seed.Params[0].IsNull = false; seed.Params[0].NumericValue = 7m;
            seed.Params[1].IsNull = false; seed.Params[1].NumericValue = 9m;
            seed.AcceptCommand.Execute(null);

            var launcher = new FakeLauncher(new FakeExecutor());
            var vm = new DebuggerTabViewModel("SP_TEST", _ => Task.FromResult<string?>(Sql),
                launcher, new ParameterHistoryStore(dir), "c1");
            await vm.PrepareAsync();
            await vm.LaunchCommand.ExecuteAsync(null);

            Assert.Equal(7L, Convert.ToInt64(launcher.LastSpec!.RootValues["A"]));
            Assert.Equal(9L, Convert.ToInt64(launcher.LastSpec!.RootValues["B"]));

            await vm.RestartCommand.ExecuteAsync(null); // re-run without re-prompting

            Assert.Equal(7L, Convert.ToInt64(launcher.LastSpec!.RootValues["A"]));
            Assert.Equal(9L, Convert.ToInt64(launcher.LastSpec!.RootValues["B"]));
        }
        finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, recursive: true); }
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
        // Completed keeps the terminal snapshot visible (not cleared): the closing END is marked (execution
        // finished there — IBExpert-like) and the frame's variables remain — the session no longer "vanishes".
        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
        Assert.Equal(Off("end"), vm.CurrentStart); // the block's END is highlighted
        Assert.NotEmpty(vm.Variables);
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
    public async Task Completed_PreservesTerminalState_ThenStopClears()
    {
        // R is written on the routine's last statement (r = v) — the terminal snapshot must reflect it.
        var vm = Vm(Sql, new FakeExecutor().Write(
            Off("r = v"), new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["R"] = 15 }), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null); // run to the end

        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
        // The session does NOT vanish: the closing END is marked, variables + a single-frame call stack retained.
        Assert.Equal(Off("end"), vm.CurrentStart);
        Assert.NotEmpty(vm.Variables);
        Assert.True(vm.HasCallStack);
        Assert.Equal("SP_TEST", Assert.Single(vm.CallStack).RoutineName);
        Assert.Equal("15", vm.Variables.First(r => r.Name == "R").ValueText); // final write-back visible
        // Stepping disabled; only Restart / Stop remain enabled.
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.False(vm.StepIntoCommand.CanExecute(null));
        Assert.False(vm.StepOverCommand.CanExecute(null));
        Assert.False(vm.StepOutCommand.CanExecute(null));
        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.True(vm.RestartCommand.CanExecute(null));

        // Stop is what finally tears the session down + clears the state.
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Idle, vm.Phase);
        Assert.Null(vm.CurrentStart);
        Assert.Empty(vm.Variables);
        Assert.False(vm.HasCallStack);
    }

    [Fact]
    public async Task Completed_Trigger_KeepsContextGroupVisible()
    {
        var vm = TriggerVm(TriggerSql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        vm.TriggerEditor!.NewParameters.Params[0].IsNull = false;
        vm.TriggerEditor.NewParameters.Params[0].NumericValue = 100m;
        await vm.LaunchCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null); // run the trigger body to the end

        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
        // The trigger's Context (NEW/OLD) group is still shown at completion (not cleared).
        Assert.Contains(vm.VariableGroups, g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupContext);
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

    private static List<string> InlineNames(DebuggerTabViewModel vm)
        => vm.InlineValues.Select(a => a.Text.Split(" = ")[0]).ToList();

    [Fact]
    public async Task InlineValues_ShowVariablesUsedInCurrentStatement_AnchoredOnCurrentLine()
    {
        // D15.5 Seam B — PRIMARY set = variables the current statement USES, shown even when unchanged. At
        // entry we are paused on `v = a + b`, which uses V, A, B (but not R). Anchored on the current line.
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        var names = InlineNames(vm);
        Assert.Contains("V", names);
        Assert.Contains("A", names);
        Assert.Contains("B", names);
        Assert.DoesNotContain("R", names); // not used in `v = a + b`, and unchanged
        Assert.All(vm.InlineValues, a => Assert.Equal(vm.CurrentStart, a.AnchorOffset));
    }

    [Fact]
    public async Task InlineValues_ExcludeChangedNotUsed()
    {
        // D15.5 Seam B — final policy (ratified after QA 2026-07-23): show ONLY variables the current statement
        // uses; a variable changed by the previous step but NOT used in the current statement is NOT shown (it
        // added noise). Step over `v = a` (changes V), pausing on `r = a`, which uses R and A but not V → V is
        // absent even though it just changed.
        const string sql = """
            create procedure p (a integer) returns (r integer) as
            declare v integer;
            begin
              v = a;
              r = a;
            end
            """;
        var writes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["V"] = 7 };
        var vm = Vm(sql, new FakeExecutor().Write(sql.IndexOf("v = a", StringComparison.Ordinal), writes), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        await vm.StepOverCommand.ExecuteAsync(null); // execute `v = a` → paused on `r = a`

        // Sanity: V really did change (so this proves the policy, not a missing change).
        Assert.True(vm.Variables.First(r => r.Name == "V").IsChanged);

        var names = InlineNames(vm);
        Assert.Contains("A", names);           // used
        Assert.Contains("R", names);           // used
        Assert.DoesNotContain("V", names);     // changed but NOT used → excluded
    }

    [Fact]
    public async Task InlineValues_Empty_WhenNotPaused()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.InlineValues); // paused: the entry statement's used variables

        await vm.StopCommand.ExecuteAsync(null); // teardown → not paused
        Assert.Empty(vm.InlineValues);
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
    public async Task UnhandledRaise_Faults_StopsOnFaultingLine_PreservesState()
    {
        var vm = Vm(Sql, new FakeExecutor().Raise(Off("v = a + b")), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.Faulted, vm.Phase);
        Assert.True(vm.IsFaulted); // drives the red status line
        // D15.2 Seam C — the full Firebird message goes to the Error Bar (its own row), not the status line.
        Assert.True(vm.ShowErrorBar);
        Assert.Equal("boom", vm.ErrorDetail);
        Assert.DoesNotContain("boom", vm.StatusText); // status is a short, fixed-height headline
        Assert.True(vm.IsErrorExpanded); // the full message shows by default (FB errors are short)
        // Stops ON the faulting statement (not cleared): marker + variables + call stack all preserved.
        Assert.Equal(Off("v = a + b"), vm.CurrentStart);
        Assert.NotEmpty(vm.Variables);
        Assert.True(vm.HasCallStack);
        Assert.Equal("SP_TEST", Assert.Single(vm.CallStack).RoutineName);
        // Stepping disabled; only Restart / Stop remain enabled.
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.False(vm.StepIntoCommand.CanExecute(null));
        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.True(vm.RestartCommand.CanExecute(null));

        // Stop finally clears everything (incl. the Error Bar).
        await vm.StopCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Idle, vm.Phase);
        Assert.False(vm.IsFaulted);
        Assert.False(vm.ShowErrorBar);
        Assert.Equal(string.Empty, vm.ErrorDetail);
        Assert.Null(vm.CurrentStart);
        Assert.Empty(vm.Variables);
        Assert.False(vm.HasCallStack);
    }

    // D15.2 Seam C — the Error Bar's expand + dismiss view-state, and re-showing on a fresh fault.
    [Fact]
    public async Task ErrorBar_ExpandAndDismiss_AreViewState_AndReshowOnNewFault()
    {
        var vm = Vm(Sql, new FakeExecutor().Raise(Off("v = a + b")), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);

        Assert.True(vm.ShowErrorBar);
        Assert.True(vm.IsErrorExpanded); // full message shown by default

        // Collapse is the opt-in one-line "safety valve"; the toggle never changes the message text.
        vm.ToggleErrorExpandedCommand.Execute(null);
        Assert.False(vm.IsErrorExpanded);
        Assert.Equal("boom", vm.ErrorDetail);
        vm.ToggleErrorExpandedCommand.Execute(null);
        Assert.True(vm.IsErrorExpanded);

        // Dismiss hides the bar but keeps the faulted state (marker/variables untouched).
        vm.DismissErrorCommand.Execute(null);
        Assert.False(vm.ShowErrorBar);
        Assert.True(vm.IsFaulted);

        // A fresh run + fault re-shows the bar, re-expanded to the full message (dismiss + a previous
        // manual collapse do not carry over).
        await vm.RestartCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.True(vm.ShowErrorBar);
        Assert.Equal("boom", vm.ErrorDetail);
        Assert.True(vm.IsErrorExpanded);
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

    // ── D12 Seam E — Breakpoints panel + Break-on-Exception + data breakpoints (spec §9.8) ──────────

    // Reproduces the reported QA scenario as closely as a VM test can: a CREATE PROCEDURE with a DECLARE, a
    // breakpoint set on the FIRST executed statement via the GUTTER line-start offset (leading whitespace). Under
    // the ratified model the stop decision belongs to the RUN command (before executing the statement about to
    // run), so launch pauses at Entry and the breakpoint on the first statement fires on the first Continue.
    private const string DeclLoopSql = """
        create or alter procedure sp_loopy (n integer) returns (idx integer, acc integer) as
        declare variable i integer;
        begin
          i = 0;
          acc = 0;
          idx = i;
        end
        """;

    [Fact]
    public async Task Breakpoint_OnFirstStatement_GutterOffset_HonoredOnFirstContinue()
    {
        var vm = Vm(DeclLoopSql, new FakeExecutor(), out _);
        await vm.PrepareAsync();

        int stmtOff = DeclLoopSql.IndexOf("i = 0", StringComparison.Ordinal);
        int lineStart = DeclLoopSql.LastIndexOf('\n', stmtOff) + 1; // the gutter passes the LINE start
        vm.ToggleBreakpointAt(lineStart);
        Assert.Contains(stmtOff, vm.BreakpointOffsets); // maps to the first statement's start

        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Contains("entry", vm.StatusText, StringComparison.OrdinalIgnoreCase); // launch pauses at Entry

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Equal(stmtOff, vm.CurrentStart); // stops ON the first statement (before executing it)
        Assert.Contains("breakpoint", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Breakpoint_OnFirstStatement_SetAfterLaunch_HonoredOnRestartThenContinue()
    {
        // The EXACT reported sequence: launch (paused at entry on the first statement, no breakpoint yet), set a
        // breakpoint on that first statement via the gutter, then Restart. Restart pauses at Entry (the run
        // command owns the decision), and the first Continue stops ON the first statement as a Breakpoint.
        var vm = Vm(DeclLoopSql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.Contains("entry", vm.StatusText, StringComparison.OrdinalIgnoreCase); // no bp yet → entry

        int stmtOff = DeclLoopSql.IndexOf("i = 0", StringComparison.Ordinal);
        int lineStart = DeclLoopSql.LastIndexOf('\n', stmtOff) + 1;
        vm.ToggleBreakpointAt(lineStart); // set on the first statement while paused there

        await vm.RestartCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Contains("entry", vm.StatusText, StringComparison.OrdinalIgnoreCase); // restart also pauses at Entry

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Equal(stmtOff, vm.CurrentStart);
        Assert.Contains("breakpoint", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Breakpoint_OnFirstStatement_IsHonoredOnFirstContinue()
    {
        // Regression (QA): a breakpoint on the FIRST executed statement must be honored — the run command makes
        // the stop decision before executing the statement about to run, so the first Continue stops ON the first
        // statement (the old post-execute check ran it and skipped ahead). Launch itself pauses at Entry.
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        vm.ToggleBreakpointAt(Off("v = a + b")); // the first statement, set before launch
        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.Contains("entry", vm.StatusText, StringComparison.OrdinalIgnoreCase);

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Equal(Off("v = a + b"), vm.CurrentStart);           // stopped ON it (before executing it)
        Assert.Contains("breakpoint", vm.StatusText, StringComparison.OrdinalIgnoreCase); // honored as a breakpoint
    }

    [Fact]
    public async Task NoBreakpoint_FirstPause_IsEntry()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.Contains("entry", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BreakpointsPanel_ReflectsCoreBreakpoints_AndRemoveClearsThem()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        vm.ToggleBreakpointAt(Off("r = v"));
        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.True(vm.HasBreakpoints);
        var row = Assert.Single(vm.BreakpointRows);
        Assert.Equal(Off("r = v"), row.Offset);

        vm.RemoveBreakpointCommand.Execute(row);
        Assert.Empty(vm.BreakpointRows);
        Assert.DoesNotContain(Off("r = v"), vm.BreakpointOffsets); // gone from the gutter too
    }

    [Fact]
    public async Task BreakpointRow_HitCountKind_TogglesOperandEnabled()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        vm.ToggleBreakpointAt(Off("r = v"));
        await vm.LaunchCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.BreakpointRows);

        Assert.False(row.IsHitCountValueEnabled);          // Always (default) — no operand
        row.HitCountKindIndex = (int)HitCountKind.Exactly;
        Assert.True(row.IsHitCountValueEnabled);           // Exactly — operand N enabled
    }

    [Fact]
    public async Task ConditionalBreakpoint_SetViaPanel_DoesNotStop_WhenConditionFalse()
    {
        // The condition set on the panel row is forwarded to the Core Breakpoint and evaluated by the engine;
        // a false condition means the (otherwise-plain) breakpoint does not stop — proving it reached the engine.
        var exec = new FakeExecutor().CondString("r = 99", false);
        var vm = Vm(Sql, exec, out _);
        await vm.PrepareAsync();
        vm.ToggleBreakpointAt(Off("r = v"));
        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Single(vm.BreakpointRows).Condition = "r = 99";
        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Completed, vm.Phase); // condition false → ran past the breakpoint to the end
    }

    [Fact]
    public async Task ConditionalBreakpoint_SetViaPanel_Stops_WhenConditionTrue()
    {
        var exec = new FakeExecutor().CondString("r = 99", true);
        var vm = Vm(Sql, exec, out _);
        await vm.PrepareAsync();
        vm.ToggleBreakpointAt(Off("r = v"));
        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Single(vm.BreakpointRows).Condition = "r = 99";
        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Equal(Off("r = v"), vm.CurrentStart);
    }

    [Fact]
    public async Task DataBreakpoint_AddedViaGesture_BreaksWhenTheVariableChanges()
    {
        // V changes at "v = a + b" (not the last statement); a data breakpoint on V (added via the Variables
        // "Break when changes" gesture) stops the session at the step AFTER the change.
        var exec = new FakeExecutor().Write(Off("v = a + b"),
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["V"] = 7 });
        var vm = Vm(Sql, exec, out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null); // paused at entry (v = a + b)

        var vRow = vm.Variables.First(r => string.Equals(r.Name, "V", StringComparison.OrdinalIgnoreCase));
        vm.AddDataBreakpointCommand.Execute(vRow);
        Assert.True(vm.HasDataBreakpoints);
        var dbRow = Assert.Single(vm.DataBreakpointRows);
        Assert.Equal("V", dbRow.DisplayName);

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);          // stopped on the V change, not completed
        Assert.Equal(Off("r = v"), vm.CurrentStart);           // paused just after the change

        vm.RemoveDataBreakpointCommand.Execute(dbRow);
        Assert.Empty(vm.DataBreakpointRows);
    }

    [Fact]
    public async Task BreakOnException_PausesAtRaise_BeforeRouting_ThenFaultsOnResume()
    {
        var exec = new FakeExecutor().Raise(Off("r = v"));
        var vm = Vm(Sql, exec, out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        vm.BreakOnException = true; // mirrors to the live session

        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);   // paused AT the raising statement, NOT faulted
        Assert.Equal(Off("r = v"), vm.CurrentStart);

        await vm.ContinueCommand.ExecuteAsync(null);     // resume → routes the raise → unhandled → faulted
        Assert.Equal(DebuggerPhase.Faulted, vm.Phase);
    }

    [Fact]
    public async Task WithoutBreakOnException_RaiseFaultsImmediately()
    {
        var exec = new FakeExecutor().Raise(Off("r = v"));
        var vm = Vm(Sql, exec, out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        await vm.ContinueCommand.ExecuteAsync(null); // no break-on-exception → routed immediately → faulted
        Assert.Equal(DebuggerPhase.Faulted, vm.Phase);
    }

    // ── D12 Seam E2 — Run to next SUSPEND + the Results grid projection (spec §9.8) ──────────────────

    private const string SelSql = """
        create procedure sp_sel (n integer) returns (i integer) as
        begin
          i = 0;
          while (i < n) do
          begin
            i = i + 1;
            suspend;
          end
        end
        """;

    private static Dictionary<string, object?> RowI(int i)
        => new(StringComparer.OrdinalIgnoreCase) { ["I"] = i };

    [Fact]
    public async Task RunToSuspend_CollectsEmittedRows_IntoResultsProjection()
    {
        // Run to next SUSPEND yields one row per resume; each emitted row is projected into SuspendRows/
        // SuspendColumns (the Results grid) — a pure projection of DebugSession.EmittedRows.
        int whileAt = SelSql.IndexOf("while", StringComparison.Ordinal);
        int suspendAt = SelSql.IndexOf("suspend", StringComparison.Ordinal);
        var exec = new FakeExecutor().Cond(whileAt, true, true, false).Suspend(suspendAt, RowI(1), RowI(2));
        var vm = Vm(SelSql, exec, out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        bool columnsChanged = false;
        vm.SuspendColumnsChanged += (_, _) => columnsChanged = true;

        await vm.RunToSuspendCommand.ExecuteAsync(null); // first SUSPEND
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.True(vm.HasSuspendRows);
        Assert.Equal(new[] { "I" }, vm.SuspendColumns);
        Assert.True(columnsChanged);
        Assert.Equal(1, Assert.Single(vm.SuspendRows)[0]);

        await vm.RunToSuspendCommand.ExecuteAsync(null); // second SUSPEND
        Assert.Equal(2, vm.SuspendRows.Count);
        Assert.Equal(2, vm.SuspendRows[1][0]);

        await vm.RunToSuspendCommand.ExecuteAsync(null); // no further SUSPEND → completes, rows unchanged
        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
        Assert.Equal(2, vm.SuspendRows.Count);
    }

    [Fact]
    public async Task RunToSuspend_GatedToPaused_AndClearedOnStop()
    {
        int whileAt = SelSql.IndexOf("while", StringComparison.Ordinal);
        int suspendAt = SelSql.IndexOf("suspend", StringComparison.Ordinal);
        var exec = new FakeExecutor().Cond(whileAt, true, false).Suspend(suspendAt, RowI(1));
        var vm = Vm(SelSql, exec, out _);
        await vm.PrepareAsync();
        Assert.False(vm.RunToSuspendCommand.CanExecute(null)); // not launched → disabled

        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.True(vm.RunToSuspendCommand.CanExecute(null)); // paused → enabled
        await vm.RunToSuspendCommand.ExecuteAsync(null);
        Assert.True(vm.HasSuspendRows);

        await vm.StopCommand.ExecuteAsync(null);
        Assert.False(vm.HasSuspendRows);   // the result set is cleared on Stop
        Assert.Empty(vm.SuspendColumns);
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

    // ── D10 Seam C — trigger launch (NEW/OLD context) ────────────────────────────────────────────────

    private const string TriggerSql = """
        create trigger tr_test for orders active before update position 0 as
        begin
          new.total = old.total + 1;
        end
        """;

    private static readonly IReadOnlyList<ColumnSpec> OrdersColumns = new[]
    {
        new ColumnSpec("TOTAL", "NUMERIC(15,2)"),
        new ColumnSpec("STATUS", "VARCHAR(20)"),
    };

    private static DebuggerTabViewModel TriggerVm(
        string sql, IDebugExecutor executor, out FakeLauncher launcher,
        Func<string, CancellationToken, Task<IReadOnlyList<ColumnSpec>>>? columns = null)
    {
        launcher = new FakeLauncher(executor);
        return new DebuggerTabViewModel(
            "TR_TEST", _ => Task.FromResult<string?>(sql), launcher,
            columnsProvider: columns ?? ((_, _) => Task.FromResult(OrdersColumns)));
    }

    [Fact]
    public async Task Prepare_Trigger_EntersTriggerMode_WithReferencedContextColumns()
    {
        var vm = TriggerVm(TriggerSql, new FakeExecutor(), out _);
        await vm.PrepareAsync();

        Assert.Equal(DebuggerPhase.ReadyToLaunch, vm.Phase);
        Assert.True(vm.IsTriggerMode);
        Assert.Null(vm.Parameters);                 // a trigger has no procedure parameters
        Assert.NotNull(vm.TriggerEditor);
        // BEFORE UPDATE ⇒ both records available; only the referenced column (TOTAL) is a row.
        Assert.True(vm.TriggerEditor!.NewAvailable);
        Assert.True(vm.TriggerEditor.OldAvailable);
        Assert.Equal("TOTAL", Assert.Single(vm.TriggerEditor.NewParameters.Params).Name);
        Assert.Equal("NUMERIC(15,2)", vm.TriggerEditor.NewParameters.Params[0].TypeText); // typed from the catalog
    }

    [Fact]
    public async Task Launch_Trigger_SeedsSyntheticRootValues_AndCarriesTriggerContext()
    {
        var vm = TriggerVm(TriggerSql, new FakeExecutor(), out var launcher);
        await vm.PrepareAsync();

        vm.TriggerEditor!.NewParameters.Params[0].IsNull = false;
        vm.TriggerEditor.NewParameters.Params[0].NumericValue = 100m;
        vm.TriggerEditor.OldParameters.Params[0].IsNull = false;
        vm.TriggerEditor.OldParameters.Params[0].NumericValue = 50m;

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.NotNull(launcher.LastSpec);
        var trigger = launcher.LastSpec!.Trigger;
        Assert.NotNull(trigger);                                  // the launch carries the trigger context (§8.1)
        Assert.Equal(TriggerEvent.Update, trigger!.Event);
        // The NEW/OLD values are seeded onto their synthetic frame variables (ET_CTX_i).
        Assert.Equal(100m, launcher.LastSpec.RootValues["ET_CTX_0"]);
        Assert.Equal(50m, launcher.LastSpec.RootValues["ET_CTX_1"]);
    }

    [Fact]
    public async Task Launch_Trigger_ShowsContextGroup_WithNewAndOldRows()
    {
        var vm = TriggerVm(TriggerSql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        vm.TriggerEditor!.NewParameters.Params[0].IsNull = false;
        vm.TriggerEditor.NewParameters.Params[0].NumericValue = 100m;
        await vm.LaunchCommand.ExecuteAsync(null);

        var context = vm.VariableGroups.Single(g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupContext);
        Assert.Contains(context.Rows, r => r.Name == "NEW.TOTAL" && r.Kind == DebugVariableKind.ContextNew);
        Assert.Contains(context.Rows, r => r.Name == "OLD.TOTAL" && r.Kind == DebugVariableKind.ContextOld);
        // The NEW.TOTAL row resolves its live value through the synthetic frame variable it was seeded with.
        Assert.Equal("100", context.Rows.Single(r => r.Name == "NEW.TOTAL").ValueText);
    }

    [Fact]
    public async Task Prepare_DatabaseLevelTrigger_IsOutOfScope_BlocksLaunch()
    {
        const string onConnect = """
            create trigger tr_conn active on connect position 0 as
            begin
              exit;
            end
            """;
        var vm = TriggerVm(onConnect, new FakeExecutor(), out _);
        await vm.PrepareAsync();

        Assert.False(vm.IsTriggerMode);
        Assert.True(vm.LaunchBlocked);
        Assert.Equal(DebuggerPhase.Idle, vm.Phase);
        Assert.Equal(EmberTern.App.UiStrings.DebuggerTriggerOutOfScope, vm.StatusText);
    }

    [Fact]
    public async Task Launch_InsertTrigger_OmitsOldRows_FromContextGroup()
    {
        // A multi-action BEFORE INSERT OR UPDATE trigger referencing OLD.STATUS; launched as INSERT ⇒ OLD is
        // unavailable, so the OLD context row is not shown (matches the launch panel hiding the OLD grid).
        const string biu = """
            create trigger tr_biu for orders active before insert or update position 0 as
            begin
              new.status = old.status;
            end
            """;
        var vm = TriggerVm(biu, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        Assert.True(vm.TriggerEditor!.HasMultipleActions);
        vm.TriggerEditor.SelectedActionIndex = 0; // INSERT

        await vm.LaunchCommand.ExecuteAsync(null);

        var context = vm.VariableGroups.Single(g => g.Header == EmberTern.App.UiStrings.DebuggerVariableGroupContext);
        Assert.Contains(context.Rows, r => r.Name == "NEW.STATUS");
        Assert.DoesNotContain(context.Rows, r => r.Name == "OLD.STATUS"); // OLD unavailable for INSERT (§8.1)
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
