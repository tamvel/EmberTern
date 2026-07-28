using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Commands;
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

        // A statement that BLOCKS until the test releases it — the only way to hold the VM in Phase == Busy
        // (a wire operation genuinely in flight) and act while the engine is running, which is what the
        // edit-during-a-step rule has to survive. Entered is volatile: it is set on the engine's background
        // thread and read from the test thread.
        private int? _blockAt;
        private ManualResetEventSlim? _blockGate;
        private volatile bool _entered;
        public bool Entered => _entered;
        public ManualResetEventSlim BlockAt(int start)
        {
            _blockAt = start;
            return _blockGate = new ManualResetEventSlim(false);
        }

        public StatementOutcome ExecuteStatement(IExecutableStatement s, Frame frame)
        {
            if (_blockAt == s.Start && _blockGate is { } gate)
            {
                _entered = true;
                gate.Wait(TimeSpan.FromSeconds(10)); // bounded: a hung test fails rather than hangs the suite
            }
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

        // D9 seam c — the VM tests never step INTO a local function; the null resolver keeps every call a
        // step-over. EvaluateReturn IS reached for a FUNCTION-root frame (D-function): it yields a scripted
        // value (default null) — the value the Return row surfaces at completion.
        public DebugRoutine? ResolveFunction(CallExpression call, Frame frame) => null;
        private object? _returnValue;
        public FakeExecutor WithReturn(object? value) { _returnValue = value; return this; }
        public ReturnOutcome EvaluateReturn(IExecutableStatement returnStatement, Frame frame)
            => ReturnOutcome.Of(_returnValue);

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
                spec.Breakpoints, spec.DataBreakpoints,
                // Mirror the production launcher: a function root carries a RETURNS type (making it a function
                // frame). The real launcher passes executor.RootReturnType; the fake has no catalog, so a fixed
                // non-null type is enough to exercise the function-root RETURN path.
                rootReturnType: spec.IsFunction ? "integer" : null);
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

    // ── D-function: launching a standalone FUNCTION as the debug root (Seam C1 — App launch wiring) ──

    private const string FunctionSql = """
        create function fn_test (a integer) returns integer as
        declare v integer;
        begin
          v = a + 1;
          return v;
        end
        """;

    [Fact]
    public async Task Launch_Function_SetsIsFunctionOnSpec()
    {
        // C1: the VM detects DdlObjectKind.Function and threads it to DebugLaunchSpec.IsFunction, so the
        // launcher builds a function-root executor + function root frame (the Firebird side, Seam B).
        var vm = Vm(FunctionSql, new FakeExecutor(), out var launcher);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.NotNull(launcher.LastSpec);
        Assert.True(launcher.LastSpec!.IsFunction);
    }

    [Fact]
    public async Task Launch_Procedure_SpecIsNotFunction()
    {
        // A procedure root must NOT be flagged as a function (additive — every existing launch unchanged).
        var vm = Vm(Sql, new FakeExecutor(), out var launcher);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.NotNull(launcher.LastSpec);
        Assert.False(launcher.LastSpec!.IsFunction);
    }

    [Fact]
    public async Task Function_ReturnRow_PendingWhilePaused_ThenValueOnCompletion()
    {
        // C2a: a function root shows a synthetic "Return" row — "not returned yet" while stepping, then the
        // returned value once RETURN runs (the session completes at RETURN). Real state only, no prediction.
        var vm = Vm(FunctionSql, new FakeExecutor().WithReturn(42), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        var ret = vm.Variables.SingleOrDefault(r => r.Kind == DebugVariableKind.Return);
        Assert.NotNull(ret);                                        // the Return row exists for a function
        Assert.Equal(EmberTern.App.UiStrings.DebuggerReturnPending, ret!.ValueText); // pending while paused

        await vm.StepOverCommand.ExecuteAsync(null);                // v = a + 1  → return v
        await vm.StepOverCommand.ExecuteAsync(null);                // return v   → completes
        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
        Assert.Equal("42", ret.ValueText);                          // the returned value is now shown
    }

    [Fact]
    public async Task Procedure_HasNoReturnRow()
    {
        // The Return group/row is function-only (like the trigger Context group) — a procedure never shows it.
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Variables, r => r.Kind == DebugVariableKind.Return);
    }

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

    // ── F5 = CommandId.Go, the active tab's main action ─────────────────────────────────────────────
    //
    // These used to drive DebuggerTabViewModel.RequestGoAsync() and MainWindowViewModel.GoCommand. Both are
    // gone: F5 is declared once in CommandCatalog at Tab scope, and the debugger's answer to it is its own
    // GoCommand. The behaviour asserted below is unchanged — only the entry point moved.

    // From the launch panel, Go Starts Debugging.
    [Fact]
    public async Task Go_FromLaunchPanel_StartsDebugging()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        Assert.Equal(DebuggerPhase.ReadyToLaunch, vm.Phase);

        await vm.GoCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.Paused, vm.Phase); // launched, paused at entry
    }

    // While paused, Go Continues (runs to completion here — no breakpoints).
    [Fact]
    public async Task Go_WhilePaused_Continues()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.GoCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);

        await vm.GoCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
    }

    // In a non-actionable phase Go must REFUSE rather than no-op silently. The distinction is new and it is
    // the point of the gate: an unavailable command leaves the keystroke unhandled, so the router can fall
    // through to a less specific scope instead of swallowing F5. (Restart-on-F5 for a finished session is
    // still deliberately out of scope.)
    [Fact]
    public async Task Go_WhenCompleted_CannotExecute()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.GoCommand.ExecuteAsync(null);   // launch
        await vm.GoCommand.ExecuteAsync(null);   // continue → Completed
        Assert.Equal(DebuggerPhase.Completed, vm.Phase);

        Assert.False(vm.GoCommand.CanExecute(null));

        await vm.GoCommand.ExecuteAsync(null);   // F5 again — still nothing to do, and harmless
        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
    }

    // Tab-scope resolution: with a Debugger tab selected, CommandId.Go resolves to the DEBUGGER's Go, not to
    // Execute Query. This is the fix for "F5 ran Execute Query while a debugger tab was open", now expressed
    // as a declared scope rather than a command that interpreted F5 itself.
    [Fact]
    public async Task Go_ResolvesToTheDebugger_WhenItsTabIsSelected()
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

        var tab = WorkspaceTabViewModel.CreateDebugger(main, debugger, "SP_TEST", null, MetadataObjectKind.Procedure);
        main.WorkspaceTabs.Add(tab);
        main.SelectTab(tab);
        Assert.True(main.IsDebuggerTabActive);
        Assert.Same(debugger, main.ActiveDebugger);

        Assert.Same(debugger.GoCommand, tab.ResolveCommand(CommandId.Go));

        await debugger.GoCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.Paused, debugger.Phase);
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

        // Collapse is the opt-in one-line "safety valve" (the shared MessageBanner's own chevron toggles this
        // two-way-bound state); collapsing never changes the message text.
        vm.IsErrorExpanded = false;
        Assert.Equal("boom", vm.ErrorDetail);
        vm.IsErrorExpanded = true;
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

    // ── Seam 5a — the source editor is a normal editor, at every phase ──────────────────────────────

    [Fact]
    public async Task SourceEdit_SurvivesStepping_AndMarksTheTabDirty()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        Assert.False(vm.IsSourceDirty);       // freshly loaded == what the database holds
        Assert.True(vm.IsSourceEditable);     // editable before a session exists

        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.True(vm.IsSourceEditable);     // ... and while a session is live/paused (ratified: no edit-lock)

        // Stepping rewrites the DISPLAY on every frame change; it must never clobber the edit buffer. Proven
        // here BEFORE any edit, because an edit now ends the session (see the rule tests below) — so this is
        // the display/buffer separation on its own, with no stepping left to do afterwards.
        await vm.StepOverCommand.ExecuteAsync(null);
        Assert.Equal(Sql, vm.SourceText);
        Assert.False(vm.IsSourceDirty);

        var edited = Sql + "\n-- touched";
        vm.ApplySourceEdit(edited);
        Assert.True(vm.IsSourceDirty);
        Assert.Equal(edited, vm.SourceText);

        // Editing back to the original text is not "dirty" — dirty is a diff, not a flag.
        vm.ApplySourceEdit(Sql);
        Assert.False(vm.IsSourceDirty);
    }

    [Fact]
    public async Task SourceEdit_RejectedWhileViewingACalleeFrame_AndRestoredOnReturn()
    {
        var vm = await LaunchedNestedAsync(); // paused inside the callee SP_LEAF

        // The editor is showing ANOTHER routine's source — this tab cannot save it, so it is read-only and
        // a stray edit can never land in the root buffer.
        Assert.False(vm.IsSourceEditable);
        vm.ApplySourceEdit("nonsense");
        Assert.Equal(LeafSql, vm.SourceText);
        Assert.False(vm.IsSourceDirty);

        // The session is untouched by the rejected edit — a read-only frame view cannot end a session.
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);
        Assert.Equal(2, vm.CallStack.Count);

        // Back on the root frame: editable again, and NOW an edit ends the session (the rule below).
        vm.SelectedFrameRow = vm.CallStack[1];
        Assert.True(vm.IsSourceEditable);

        var edited = RootSql + "\n-- touched";
        vm.ApplySourceEdit(edited);
        Assert.Equal(edited, vm.SourceText);
        Assert.True(vm.IsSourceDirty);
        Assert.Equal(DebuggerPhase.Editing, vm.Phase);
    }

    // ── The rule: a session runs the code it was started from ───────────────────────────────────────
    //
    // Reported after several days of real use: editing during a session left the debugger stepping through the
    // text it launched with, while the editor showed something else. The rule adopted (IBExpert's): the first
    // change to the text ends the session there and then. This does not yet START a session on the new text —
    // that is the next seam; what these pin is that no step can ever run stale code again.

    [Fact]
    public async Task SourceEdit_DuringALiveSession_EndsIt_AndKeepsTheUserOnTheCode()
    {
        var vm = Vm(Sql, new FakeExecutor(), out var launcher);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase);

        vm.ApplySourceEdit(Sql + "\n-- typed");

        Assert.Equal(DebuggerPhase.Editing, vm.Phase);
        Assert.True(launcher.Disposed);          // rolled back + the attachment closed (§4.4)
        Assert.False(vm.IsLaunchPanelVisible);   // the user stays on the CODE, not sent to the parameter form
        Assert.True(vm.IsDebugViewVisible);
        Assert.True(vm.IsSourceEditable);        // …and can keep typing
        Assert.Null(vm.CurrentStart);            // no marker: nothing is executing
        Assert.False(vm.HasVariables);
        Assert.False(vm.HasCallStack);
    }

    [Fact]
    public async Task SourceEdit_ThatEndedTheSession_DisablesEveryDebuggingCommand()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        vm.ImmediateInput = "v";                 // an evaluation is armed while paused…
        Assert.True(vm.EvaluateImmediateCommand.CanExecute(null));

        vm.ApplySourceEdit(Sql + "\n-- typed");

        // …and every one of them needs a session, so they all go with it — no per-button work.
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.False(vm.StepIntoCommand.CanExecute(null));
        Assert.False(vm.StepOverCommand.CanExecute(null));
        Assert.False(vm.StepOutCommand.CanExecute(null));
        Assert.False(vm.RunToSuspendCommand.CanExecute(null));
        Assert.False(vm.EvaluateImmediateCommand.CanExecute(null));

        // Stop stays available (the way back to the launch panel), and Restart is how you get a session on the
        // edited text — deliberately a command, never something that happens by itself.
        Assert.True(vm.StopCommand.CanExecute(null));
        Assert.True(vm.RestartCommand.CanExecute(null));
    }

    [Fact]
    public async Task SourceEdit_EndsTheSessionPermanently_EvenIfTheTextIsUndone()
    {
        // Ratified: ending the session is a PERMANENT state. Undoing the edit back to identical text must not
        // resurrect it — Restart may become available again, but only as a deliberate new start.
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        vm.ApplySourceEdit(Sql + "\n-- typed");
        Assert.Equal(DebuggerPhase.Editing, vm.Phase);

        vm.ApplySourceEdit(Sql);              // Ctrl+Z all the way back
        Assert.False(vm.IsSourceDirty);       // dirty is a diff, not a flag…
        Assert.Equal(DebuggerPhase.Editing, vm.Phase); // …but the session does NOT come back
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.True(vm.RestartCommand.CanExecute(null)); // only the deliberate way back
    }

    [Fact]
    public async Task SourceEdit_WhileAStepIsInFlight_EndsTheSessionOnReturn_WithoutFaulting()
    {
        // The engine is not thread-safe: tearing the attachment down under a running step makes that step
        // throw on return, which the step path would otherwise report as a FAULT — a fabricated error on top
        // of something the user did on purpose. The teardown therefore waits for the wire operation's tail.
        var executor = new FakeExecutor();
        using var gate = executor.BlockAt(Off("v = a + b"));
        var vm = Vm(Sql, executor, out var launcher);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        var step = vm.StepOverCommand.ExecuteAsync(null); // deliberately NOT awaited — it blocks in the engine
        Assert.True(SpinWait.SpinUntil(() => executor.Entered, TimeSpan.FromSeconds(5)));
        Assert.Equal(DebuggerPhase.Busy, vm.Phase);

        vm.ApplySourceEdit(Sql + "\n-- typed mid-step");
        Assert.Equal(DebuggerPhase.Busy, vm.Phase); // not torn down under the running engine
        Assert.False(launcher.Disposed);

        gate.Set();
        await step;

        Assert.Equal(DebuggerPhase.Editing, vm.Phase); // ended by the step's own tail
        Assert.True(launcher.Disposed);
        Assert.False(vm.IsFaulted);
        Assert.False(vm.ShowErrorBar); // no fabricated error
    }

    [Fact]
    public async Task SourceEdit_AfterTheSessionCompleted_KeepsTheInspectionState_ButDropsTheMarker()
    {
        // A terminal session executes nothing, so there is no stale code to run and the retained frame stays
        // inspectable — you want the final/fault values WHILE fixing the code. Only the position marker goes:
        // it points into text that has just moved.
        var vm = Vm(Sql, new FakeExecutor(), out var launcher);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);
        await vm.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(DebuggerPhase.Completed, vm.Phase);
        Assert.NotNull(vm.CurrentStart); // the END marker
        Assert.True(vm.HasVariables);

        vm.ApplySourceEdit(Sql + "\n-- typed");

        Assert.Equal(DebuggerPhase.Completed, vm.Phase); // NOT re-ended: there was nothing left to end
        Assert.False(launcher.Disposed);                 // the retained frame is still there to read
        Assert.True(vm.HasVariables);
        Assert.Null(vm.CurrentStart);                    // …but the marker no longer describes this text
    }

    // ── Restart runs the DRAFT — no compile, no write to the database ───────────────────────────────
    //
    // The debugger never asks the server to run the compiled routine: it interprets the AST and runs each
    // statement through a harness that never names it. So a session can be built from the edited text, and
    // Restart is how you get one — while Save stays the only operation that touches the database.

    [Fact]
    public async Task Restart_RunsTheEditedText_WithoutCompilingIt()
    {
        var vm = Vm(Sql, new FakeExecutor(), out var launcher);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        var edited = Sql.Replace("v = a + b;", "v = a + b + 1;", StringComparison.Ordinal);
        vm.ApplySourceEdit(edited);            // the session ends here
        await vm.RestartCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.Paused, vm.Phase);        // a NEW session, paused at entry
        Assert.Equal(edited, launcher.LastSpec!.Source);     // …built from the DRAFT, not the compiled text
        Assert.Null(vm.DdlExecutor);                         // and nothing was compiled: no DDL path involved
        Assert.True(vm.IsSourceDirty);                       // the database still holds the original
    }

    [Fact]
    public async Task Restart_StepPointsAndBreakpointsDescribeTheDraft()
    {
        // The parse follows the buffer, so a statement that exists only in the draft is a real step point and
        // can carry a breakpoint. Before Seam B the snapping read the database text.
        var vm = Vm(Sql, new FakeExecutor(), out var launcher);
        await vm.PrepareAsync();

        var edited = Sql.Replace("  r = v;", "  v = v * 2;\n  r = v;", StringComparison.Ordinal);
        vm.ApplySourceEdit(edited);

        int added = edited.IndexOf("v = v * 2;", StringComparison.Ordinal);
        vm.ToggleBreakpointAt(added);
        Assert.Contains(added, vm.BreakpointOffsets); // snapped to a statement that only the draft has

        await vm.RestartCommand.ExecuteAsync(null);
        Assert.Equal(edited, launcher.LastSpec!.Source);
        Assert.Contains(added, launcher.LastSpec!.Breakpoints!.Offsets); // and the session shares it
    }

    [Fact]
    public async Task SourceEdit_KeepsBreakpointsAboveIt_AndDropsThoseBelow()
    {
        // The bytes before the first divergence are identical, so a breakpoint there still points at exactly
        // the statement it was set on — a proof, not a guess (§0). Below the edit an offset may have shifted,
        // and we do not pretend to know where to, so it goes. This is what keeps breakpoints usable across the
        // Edit → Restart → Test loop instead of clearing them on every keystroke.
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        int first = Off("v = a + b");
        int second = Off("r = v");
        vm.ToggleBreakpointAt(first);
        vm.ToggleBreakpointAt(second);
        Assert.Equal(2, vm.BreakpointOffsets.Count);

        // Lengthen the FIRST statement: everything below it shifts.
        vm.ApplySourceEdit(Sql.Replace("v = a + b;", "v = a + b + 1;", StringComparison.Ordinal));

        // The first breakpoint's statement still STARTS where it did (the edit is inside it, past its start),
        // so it is still the statement the user marked — kept.
        Assert.Contains(first, vm.BreakpointOffsets);
        // The second moved by an amount we refuse to guess — dropped rather than left pointing at the gap.
        Assert.DoesNotContain(second, vm.BreakpointOffsets);
    }

    [Fact]
    public async Task Launch_AndRestart_AreBlockedWhenTheROUTINE_HEADER_Changed()
    {
        // The one thing a draft-sourced session still takes from the catalog is the root parameter list, which
        // describes the COMPILED header. So an edit that reaches the header blocks until it is saved — the
        // interim Seam C removes by reading the layout from the AST header instead.
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();

        vm.ApplySourceEdit(Sql.Replace("(a integer, b integer)", "(a integer, b integer, c integer)", StringComparison.Ordinal));

        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.False(vm.RestartCommand.CanExecute(null));

        // A BODY edit of the same size does not — that is the debugging loop and it must stay open.
        vm.ApplySourceEdit(Sql.Replace("v = a + b;", "v = a + b + 1;", StringComparison.Ordinal));
        Assert.True(vm.LaunchCommand.CanExecute(null));
    }

    [Fact]
    public async Task Preflight_IsReRunAgainstTheDraft()
    {
        // The pre-flight described the database text before Seam B; a §4.6 boundary introduced by the edit
        // (here: an autonomous transaction, which survives the debug rollback) has to be reported before the
        // session the user is about to start.
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        Assert.Empty(vm.Preflight);

        vm.ApplySourceEdit(Sql.Replace(
            "  r = v;",
            "  in autonomous transaction do r = v;",
            StringComparison.Ordinal));
        await vm.RestartCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.Preflight);
    }

    [Fact]
    public async Task Restart_WhenTheDraftAsksSomethingNew_SendsTheUserBackToTheLaunchPanel()
    {
        // A trigger's launch panel is built from the NEW/OLD columns its BODY references, so a body edit CAN
        // change what the user has to decide. Then Restart must not silently reuse the old form.
        var vm = TriggerVm(TriggerSql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        vm.ApplySourceEdit(TriggerSql.Replace(
            "new.total = old.total + 1;",
            "new.total = old.total + 1;\n  new.status = 'X';",
            StringComparison.Ordinal));
        await vm.RestartCommand.ExecuteAsync(null);

        Assert.Equal(DebuggerPhase.ReadyToLaunch, vm.Phase); // a new decision — the panel asks it
        Assert.True(vm.IsLaunchPanelVisible);
    }

    [Fact]
    public async Task Restart_WhenTheDraftAsksSomethingNew_KeepsTheValuesItCanProve()
    {
        // The panel is rebuilt because the body now reads one more NEW column — but TOTAL is still the same
        // column of the same table, so the value entered for it survives. Re-entering everything because one
        // more field appeared is exactly the tax this rule exists to remove.
        var vm = TriggerVm(TriggerSql, new FakeExecutor(), out _);
        await vm.PrepareAsync();

        var total = vm.TriggerEditor!.NewParameters.Params.Single(
            p => string.Equals(p.Name, "TOTAL", StringComparison.OrdinalIgnoreCase));
        total.IsNull = false;
        total.NumericValue = 42m;

        vm.ApplySourceEdit(TriggerSql.Replace(
            "new.total = old.total + 1;",
            "new.total = old.total + 1;\n  new.status = 'X';",
            StringComparison.Ordinal));
        await vm.RestartCommand.ExecuteAsync(null);

        var rebuilt = vm.TriggerEditor!.NewParameters.Params.Single(
            p => string.Equals(p.Name, "TOTAL", StringComparison.OrdinalIgnoreCase));
        Assert.NotSame(total, rebuilt);                          // the panel really was rebuilt
        Assert.Equal(42m, rebuilt.NumericValue);                 // …and the proven value came with it
        Assert.Equal(ValueOrigin.Restored, rebuilt.Origin);      // kept, never inferred — a column is its name

        // The column the body only just started reading has never been asked for, so it is the user's to fill.
        var status = vm.TriggerEditor!.NewParameters.Params.Single(
            p => string.Equals(p.Name, "STATUS", StringComparison.OrdinalIgnoreCase));
        Assert.True(status.IsNull);
        Assert.Equal(ValueOrigin.Entered, status.Origin);
    }

    // ── Seam 5b — save + compile from the debugger tab ──────────────────────────────────────────────

    // The tests below wire a DDL executor over a NOT-connected service: enough to make the tab savable,
    // and its ExecuteAsync fails the way a real compile error does (an exception the save path maps into
    // the Error Bar). The SUCCESS path needs a live server and is covered by manual QA on the lab.

    [Fact]
    public async Task Save_WithoutADdlExecutor_IsUnavailable()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        vm.ApplySourceEdit(Sql + "\n-- touched");

        Assert.False(vm.CanSaveSource);
        var result = await vm.SaveAsync();
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Save_OnAPackageMember_IsRefused_EvenWithAnExecutor()
    {
        // §0 / rule #11: a package member's source is RECONSTRUCTED as a standalone CREATE PROCEDURE, so
        // compiling it would create a standalone routine instead of altering the package. The refusal lives
        // in the VM, not only in the wiring — handing this tab an executor must not change that.
        using var service = new FirebirdConnectionService();
        var launcher = new FakeLauncher(new FakeExecutor());
        var vm = new DebuggerTabViewModel(
            "PUB_RUN", _ => Task.FromResult<string?>(Sql), launcher, packageName: "PKG_DBG")
        {
            DdlExecutor = new FirebirdDdlExecutor(service),
        };
        await vm.PrepareAsync();
        vm.ApplySourceEdit(Sql + "\n-- touched");

        Assert.False(vm.CanSaveSource);
        var result = await vm.SaveAsync();
        Assert.False(result.Success);
        Assert.True(vm.IsSourceDirty); // the edit is kept — refusing to save never discards work
    }

    [Fact]
    public async Task Save_CleanTab_IsANoOp_AndNeverTouchesTheSession()
    {
        using var service = new FirebirdConnectionService();
        var vm = Vm(Sql, new FakeExecutor(), out _);
        vm.DdlExecutor = new FirebirdDdlExecutor(service);
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        var result = await vm.SaveAsync();

        Assert.True(result.Success);
        Assert.Equal(DebuggerPhase.Paused, vm.Phase); // nothing to save ⇒ no warning, no teardown
    }

    [Fact]
    public async Task Save_AfterAnEditEndedTheSession_NeedsNoWarning_AndKeepsTheResumeIntent()
    {
        // The "saving ends the running session" warning is now all but unreachable by design: saving requires
        // a dirty buffer, and the edit that made it dirty already ended the session. (The one remaining window
        // is a Ctrl+S landing while a step is still on the wire — the guard is kept for it, not deleted.)
        using var service = new FirebirdConnectionService();
        var vm = Vm(Sql, new FakeExecutor(), out _);
        vm.DdlExecutor = new FirebirdDdlExecutor(service); // offline ⇒ the compile itself fails
        bool warned = false;
        vm.ConfirmationRequested += _ => { warned = true; return Task.FromResult(true); };
        await vm.PrepareAsync();
        await vm.LaunchCommand.ExecuteAsync(null);

        var edited = Sql + "\n-- touched";
        vm.ApplySourceEdit(edited);                  // ← the session ends HERE
        Assert.Equal(DebuggerPhase.Editing, vm.Phase);

        var result = await vm.SaveAsync();

        Assert.False(warned);                        // nothing left to warn about
        Assert.False(result.Success);                // the (offline) compile failed
        Assert.True(vm.ShowErrorBar);                // and the failure is in the shared Error Bar
        Assert.Equal(edited, vm.SourceText);         // the user's text is never discarded on failure
        Assert.True(vm.IsSourceDirty);

        // QA 2026-07-25 — this used to land in Idle, which shows the LAUNCH PANEL: the editor vanished and
        // the user could not fix the code the server had just rejected. A refused save now keeps the source
        // on screen, editable, with the work still reported so the close guard keeps refusing to close.
        Assert.Equal(DebuggerPhase.Editing, vm.Phase);
        Assert.False(vm.IsLaunchPanelVisible);
        Assert.True(vm.IsDebugViewVisible);
        Assert.True(vm.IsSourceEditable);
        Assert.True(vm.CanSaveSource);               // Save is still armed for the corrected text
        Assert.NotNull(vm.GetUnsavedWork());
    }

    [Fact]
    public async Task Editing_AfterARefusedSave_IsNotADeadEnd()
    {
        // QA 2026-07-25 — this phase used to disable Stop AND Restart (no session, phase not listed in
        // CanStopOrRestart), so the only way out was a successful save. It must behave like the editor after
        // a finished session: a way back to the launch panel, and a way to run the last compiled version.
        using var service = new FirebirdConnectionService();
        var vm = Vm(Sql, new FakeExecutor(), out _);
        vm.DdlExecutor = new FirebirdDdlExecutor(service); // offline ⇒ the compile fails
        await vm.PrepareAsync();
        vm.ApplySourceEdit(Sql + "\n-- touched");
        await vm.SaveAsync();
        Assert.Equal(DebuggerPhase.Editing, vm.Phase);

        Assert.True(vm.StopCommand.CanExecute(null));   // back to the launch panel
        Assert.True(vm.CanSaveSource);                  // …and saving the corrected text is still offered
        Assert.False(vm.ContinueCommand.CanExecute(null)); // stepping needs a session — still disabled
        Assert.False(vm.StepIntoCommand.CanExecute(null));

        // …and Restart can start a session on the text as it stands, refused compile or not: it does not need
        // the database to agree with the editor.
        Assert.True(vm.RestartCommand.CanExecute(null));
    }

    // ── Save → compile → resume: is the launch configuration the user made still the right one? ──────
    //
    // The auto-relaunch itself needs a compile that SUCCEEDS, i.e. a live server (FirebirdDdlExecutor is
    // sealed), so it is covered by manual QA on the lab. What is pinned here is the decision that gates it:
    // the launch signature — the object kind + ordered input parameters, or a trigger's header + referenced
    // NEW/OLD columns — read from the same parsed model the launch panel is built from.

    // The signature is a pure reading of the parsed model, so it is exercised the way the save path reads it
    // — over the text as it stands — without a test-only hook into the save.
    private static async Task<string> SignatureOfAsync(string sql)
    {
        var vm = Vm(sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        return vm.BuildLaunchSignature();
    }

    [Fact]
    public async Task LaunchSignature_IsStableAcrossAnEditThatKeepsTheSignature()
    {
        // Body-only change: same kind, same parameters ⇒ the configuration the user already made still
        // applies, so a save may rebuild the session without asking them anything again.
        var before = await SignatureOfAsync(Sql);
        var after = await SignatureOfAsync(Sql.Replace("v = a + b;", "v = a + b + 1;", StringComparison.Ordinal));

        Assert.Equal(before, after);
        Assert.NotEqual(string.Empty, before);
    }

    [Theory]
    // a new parameter — a value the user has never been asked for
    [InlineData("create procedure sp_test (a integer, b integer, c integer) returns (r integer) as")]
    // a renamed parameter — the old values no longer describe the new signature
    [InlineData("create procedure sp_test (a integer, bb integer) returns (r integer) as")]
    // a retyped parameter — the entered value may not even be valid for the new type
    [InlineData("create procedure sp_test (a integer, b varchar(10)) returns (r integer) as")]
    // a dropped parameter
    [InlineData("create procedure sp_test (a integer) returns (r integer) as")]
    public async Task LaunchSignature_ChangesWheneverTheUserWouldHaveANewDecision(string newHeader)
    {
        var before = await SignatureOfAsync(Sql);
        var after = await SignatureOfAsync(Sql.Replace(
            "create procedure sp_test (a integer, b integer) returns (r integer) as",
            newHeader,
            StringComparison.Ordinal));

        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task LaunchSignature_IsEmpty_WhenTheTextIsNotADebuggableRoutine()
    {
        // Empty never compares equal, so an unreadable save always falls back to the launch panel rather
        // than resuming a session against something we could not parse.
        Assert.Equal(string.Empty, await SignatureOfAsync("select 1 from rdb$database"));
    }

    [Fact]
    public async Task LaunchSignature_DistinguishesAProcedureFromAFunction()
    {
        // The object kind is part of the configuration: a routine edited into a function needs the panel
        // rebuilt (and the Return group appears), so it must never compare equal.
        Assert.NotEqual(await SignatureOfAsync(Sql), await SignatureOfAsync(FunctionSql));
    }

    [Fact]
    public async Task Save_ThatFails_WithNoLiveSession_StillKeepsTheSourceOnScreen()
    {
        // Same rule from the launch-panel side: a refused save must not leave the tab on the parameter
        // form. Nothing to stop here, so the phase moves ReadyToLaunch → Editing purely to keep the
        // code visible.
        using var service = new FirebirdConnectionService();
        var vm = Vm(Sql, new FakeExecutor(), out _);
        vm.DdlExecutor = new FirebirdDdlExecutor(service); // offline ⇒ the compile fails
        await vm.PrepareAsync();
        Assert.Equal(DebuggerPhase.ReadyToLaunch, vm.Phase);

        vm.ApplySourceEdit(Sql + "\n-- touched");
        var result = await vm.SaveAsync();

        Assert.False(result.Success);
        Assert.Equal(DebuggerPhase.Editing, vm.Phase);
        Assert.False(vm.IsLaunchPanelVisible);
        Assert.True(vm.IsSourceEditable);
    }

    // ── Seam 5c — the debugger tab participates in the close/disconnect WorkGuard ───────────────────

    [Fact]
    public async Task UnsavedWork_IsReportedOnlyWhileTheSourceIsDirty()
    {
        var vm = Vm(Sql, new FakeExecutor(), out _);
        await vm.PrepareAsync();
        Assert.Null(vm.GetUnsavedWork()); // clean tab closes silently, as before

        vm.ApplySourceEdit(Sql + "\n-- touched");
        var work = vm.GetUnsavedWork();
        Assert.NotNull(work);
        Assert.Equal(UnsavedWorkKind.ModifiedSource, work!.Kind);
        Assert.Contains("SP_TEST", work.Label);

        vm.ApplySourceEdit(Sql); // edited back → nothing to lose
        Assert.Null(vm.GetUnsavedWork());
    }

    [Fact]
    public async Task PackageMemberTab_ReportsUnsavedWork_ButIsNeverOfferedSave()
    {
        // The work is real (so Discard/Cancel still guards it) but Save must not be offered — that DDL
        // would create a standalone routine instead of altering the package.
        using var service = new FirebirdConnectionService();
        var launcher = new FakeLauncher(new FakeExecutor());
        var vm = new DebuggerTabViewModel(
            "PUB_RUN", _ => Task.FromResult<string?>(Sql), launcher, packageName: "PKG_DBG")
        {
            DdlExecutor = new FirebirdDdlExecutor(service),
        };
        await vm.PrepareAsync();
        vm.ApplySourceEdit(Sql + "\n-- touched");

        Assert.NotNull(vm.GetUnsavedWork());
        Assert.False(vm.IsSavable);
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
