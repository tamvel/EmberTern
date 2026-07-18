using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Debugging;
using EmberTern.Core.Settings;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>The lifecycle phase a debug tab is in — drives which surface (launch panel vs. debug view)
/// shows and which commands are enabled.</summary>
public enum DebuggerPhase
{
    /// <summary>Fetching + parsing the routine source (the launch panel is not ready yet).</summary>
    Preparing,

    /// <summary>The launch panel is shown; the user sets parameters + isolation and starts the session.</summary>
    ReadyToLaunch,

    /// <summary>A launch (or a step) is in flight on the server — commands are disabled.</summary>
    Busy,

    /// <summary>Stopped at a step point, awaiting a stepping command.</summary>
    Paused,

    /// <summary>The session ran to completion (transaction rolled back).</summary>
    Completed,

    /// <summary>The session ended on an unhandled exception (transaction rolled back).</summary>
    Faulted,

    /// <summary>The user stopped the session, or preparation/launch failed — the launch panel is shown again.</summary>
    Idle,
}

/// <summary>
/// The Firebird debugger tab (Stage X / D4 — the MVP). A <b>thin presentation layer</b> over the pure-Core
/// interpreter (<see cref="DebugSession"/>) driven, on the server, by D2's executor via
/// <see cref="IDebugSessionLauncher"/>. It owns the launch panel (typed parameters + isolation + pre-flight,
/// reusing the Smart-Parameters infrastructure — <see cref="ExecuteProcedureDialogViewModel"/>), the stepping
/// commands, the read-only current-line / breakpoint state the renderers paint, and the basic variables list.
/// It never evaluates an expression, coerces a type, or re-parses SQL — parsing is the one Core call
/// (<see cref="SqlParser"/>/<see cref="SemanticModel"/>), everything else goes through the session/executor.
/// <para>
/// Every engine call blocks on a wire operation (the sync-over-async executor), so stepping runs on a
/// background thread (<see cref="Task.Run{T}(Func{T})"/>); the awaiting continuation resumes on the UI thread,
/// where the observable state is updated. The debug transaction is rolled back and the attachment closed on
/// <see cref="DisposeAsync"/> (tab close) — the default contract of a debug run (§4.4).
/// </para>
/// <para><b>D4 scope:</b> standalone procedures only. Step-into resolves to nothing yet (a call runs on the
/// server = step-over, 100% faithful §5.3); triggers/packages/local routines/cursors and the Watches/Immediate
/// surfaces are later milestones.</para>
/// </summary>
public sealed partial class DebuggerTabViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<string?>> _sourceProvider;
    private readonly IDebugSessionLauncher _launcher;

    // Parsed once during preparation (the strict whole-routine parse — gotcha #238), then reused at launch.
    private string? _source;
    private BlockStatement? _body;
    private SemanticModel? _model;
    private IReadOnlyList<IExecutableStatement> _stepPoints = Array.Empty<IExecutableStatement>();

    // The live run (null before launch / after stop). The session is already Started when set.
    private DebugRunHandle? _run;
    private DebugSession? Session => _run?.Session;

    // Breakpoint step-point offsets, kept here so they survive across launch/restart; mirrored into the live
    // session's BreakpointSet while it exists.
    private readonly HashSet<int> _breakpoints = new();

    internal DebuggerTabViewModel(
        string routineName,
        Func<CancellationToken, Task<string?>> sourceProvider,
        IDebugSessionLauncher launcher,
        ParameterHistoryStore? historyStore = null,
        string? connectionId = null,
        WatchStore? watchStore = null)
    {
        RoutineName = routineName ?? throw new ArgumentNullException(nameof(routineName));
        _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _historyStore = historyStore;
        _connectionId = connectionId;
        _watchStore = watchStore;
        Preflight = new ObservableCollection<DebugPreflightItem>();
        Variables = new ObservableCollection<DebugVariableRowViewModel>();
        ExecutedSql = new ObservableCollection<DebugExecutedSqlRowViewModel>();
        Watches = new ObservableCollection<WatchRowViewModel>();
        StatusText = UiStrings.DebuggerLaunchPreparing;
        LoadWatches();
    }

    private readonly ParameterHistoryStore? _historyStore;
    private readonly string? _connectionId;
    private readonly WatchStore? _watchStore;

    /// <summary>The routine being debugged (a standalone procedure in D4).</summary>
    public string RoutineName { get; }

    /// <summary>The read-only routine source shown in the editor (set once preparation completes).</summary>
    [ObservableProperty]
    private string _sourceText = string.Empty;

    /// <summary>The launch-panel parameter editor (reuses Smart Parameters: typed rows + history +
    /// validation + resolve). Null until preparation resolves the routine's input parameters.</summary>
    [ObservableProperty]
    private ExecuteProcedureDialogViewModel? _parameters;

    /// <summary>The pre-flight report (§9.2 / §4.6) shown on the launch panel.</summary>
    public ObservableCollection<DebugPreflightItem> Preflight { get; }

    /// <summary>The current frame's variables (basic list — D4).</summary>
    public ObservableCollection<DebugVariableRowViewModel> Variables { get; }

    /// <summary>The Executed SQL audit log (D5, spec §10.3) — every expression evaluation / Immediate run,
    /// newest first. The trust anchor of a simulator (§F): the generated harness SQL is kept visible.</summary>
    public ObservableCollection<DebugExecutedSqlRowViewModel> ExecutedSql { get; }

    private const int ExecutedSqlCap = 200;

    /// <summary>The Immediate window input — an expression (default) or a statement (see
    /// <see cref="ImmediateAsStatement"/>) evaluated against the current frame (D5, spec §9.5).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EvaluateImmediateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearImmediateCommand))]
    [NotifyPropertyChangedFor(nameof(HasImmediateInput))]
    private string _immediateInput = string.Empty;

    /// <summary>True when the Immediate input has text — drives the inline Clear (✕) affordance.</summary>
    public bool HasImmediateInput => !string.IsNullOrEmpty(ImmediateInput);

    /// <summary>When set, the Immediate input is run as a PSQL statement against the live frame (its
    /// write-back is applied to the frame); otherwise it is evaluated as an expression (spec §9.5).</summary>
    [ObservableProperty]
    private bool _immediateAsStatement;

    /// <summary>True while a session is live and there are audit rows to show.</summary>
    public bool HasExecutedSql => ExecutedSql.Count > 0;

    /// <summary>The most recent evaluation (newest row) — shown inline on the Immediate tab so it stays a
    /// self-contained REPL, while the Executed SQL tab keeps the full audit history. Presentation only.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLatestEvaluation))]
    private DebugExecutedSqlRowViewModel? _latestEvaluation;

    public bool HasLatestEvaluation => LatestEvaluation is not null;

    /// <summary>The Watches (D5 seam b, spec §9.5) — expressions re-evaluated after every step through the
    /// one engine (<see cref="DebugSession.Evaluate"/>). Persisted per routine via <see cref="WatchStore"/>.</summary>
    public ObservableCollection<WatchRowViewModel> Watches { get; }

    public bool HasWatches => Watches.Count > 0;

    /// <summary>The new-watch input.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddWatchCommand))]
    [NotifyPropertyChangedFor(nameof(HasWatchInput))]
    private string _watchInput = string.Empty;

    public bool HasWatchInput => !string.IsNullOrWhiteSpace(WatchInput);

    /// <summary>Isolation options for the launch selector: index 0 = Read Committed, 1 = Snapshot (§4.2).</summary>
    public IReadOnlyList<string> IsolationOptions { get; } = new[]
    {
        UiStrings.DebuggerIsolationReadCommitted,
        UiStrings.DebuggerIsolationSnapshot,
    };

    [ObservableProperty]
    private int _selectedIsolationIndex;

    private DebugIsolation Isolation =>
        SelectedIsolationIndex == 1 ? DebugIsolation.Snapshot : DebugIsolation.ReadCommitted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLaunchPanelVisible))]
    [NotifyPropertyChangedFor(nameof(IsDebugViewVisible))]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(HasVariables))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepIntoCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOverCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestartCommand))]
    [NotifyCanExecuteChangedFor(nameof(EvaluateImmediateCommand))]
    private DebuggerPhase _phase = DebuggerPhase.Preparing;

    [ObservableProperty]
    private string _statusText;

    /// <summary>True when preparation or launch produced a blocking problem (no step points / no source);
    /// the launch button is disabled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    private bool _launchBlocked;

    // The step point the session is paused on (null when not paused) — the CurrentLineRenderer reads these.
    public int? CurrentStart { get; private set; }
    public int? CurrentLength { get; private set; }

    /// <summary>The breakpoint step-point offsets — the BreakpointMargin reads this.</summary>
    public IReadOnlyCollection<int> BreakpointOffsets => _breakpoints;

    /// <summary>Raised when the current-statement marker or the breakpoint set changes, so the view can
    /// repaint the renderers (via <c>TextView.Redraw()</c>, never <c>InvalidateVisual()</c> — gotcha #223).</summary>
    public event EventHandler? DebugMarkersChanged;

    public bool IsLaunchPanelVisible => Phase is DebuggerPhase.Preparing or DebuggerPhase.ReadyToLaunch or DebuggerPhase.Idle;
    public bool IsDebugViewVisible => !IsLaunchPanelVisible;
    public bool IsPaused => Phase == DebuggerPhase.Paused;
    public bool HasVariables => Variables.Count > 0;

    /// <summary>Presentation state (not debug logic): whether the bottom tabbed panel (Immediate / Executed
    /// SQL / Watches, and future Call Stack / Breakpoints / Output) is collapsed so the editor + Variables get
    /// the full height. The view owns the row-height mechanics (mirrors the SQL results panel).</summary>
    [ObservableProperty]
    private bool _isBottomPanelCollapsed;

    /// <summary>Collapses / expands the bottom tabbed panel.</summary>
    [RelayCommand]
    private void ToggleBottomPanel() => IsBottomPanelCollapsed = !IsBottomPanelCollapsed;

    // ── Preparation ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Fetches + parses the routine, derives the launch panel (parameters + pre-flight). Kicked once
    /// by the owner after the tab is created (kept off the ctor so failures surface as tab state, not throws).</summary>
    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        Phase = DebuggerPhase.Preparing;
        StatusText = UiStrings.DebuggerLaunchPreparing;

        string? source;
        try
        {
            source = await _sourceProvider(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            FailPreparation(string.Format(CultureInfo.CurrentCulture, UiStrings.DebuggerStatusLaunchFailedFormat, ex.Message));
            return;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            FailPreparation(string.Format(CultureInfo.CurrentCulture, UiStrings.DebuggerSourceUnavailableFormat, RoutineName));
            return;
        }

        _source = source;
        SourceText = source;

        // The strict whole-routine parse: CREATE PROCEDURE stays ONE DdlStatement whose Body is bound with its
        // declares in scope, so body identifiers resolve to Variable/Parameter symbols (gotcha #238). Built
        // without metadata — DiagnosticsEngine's object/column categories stay silent (conservative), which is
        // exactly right for a routine that already compiled.
        _model = SemanticModel.Build(SqlParser.Parse(source).Root);
        var ddl = _model.Syntax.Statements.OfType<DdlStatement>().FirstOrDefault(d => d.Body is not null);
        _body = ddl?.Body;
        _stepPoints = _body is null
            ? Array.Empty<IExecutableStatement>()
            : _body.DescendantNodesAndSelf().OfType<IExecutableStatement>().ToList();

        BuildParameters();
        BuildPreflight(hasStepPoints: _stepPoints.Count > 0 && _body is not null);

        Phase = DebuggerPhase.ReadyToLaunch;
        StatusText = UiStrings.DebuggerStatusReady;
    }

    private void BuildParameters()
    {
        // Input parameters, in declaration order, from the semantic model (never re-parsed). Typed rows +
        // history come from the reused Smart-Parameters VM.
        var inputs = _model!.AllSymbols
            .OfType<ParameterSymbol>()
            .Where(p => p.Direction == ParameterDirection.Input)
            .OrderBy(p => p.DeclarationSpan?.Start ?? int.MaxValue)
            .Select(p => (p.Name, TypeText: p.DataType ?? "VARCHAR"))
            .ToList();

        Parameters = new ExecuteProcedureDialogViewModel(
            inputs, RoutineName, _connectionId, objectKind: "Procedure", historyStore: _historyStore);
    }

    private void BuildPreflight(bool hasStepPoints)
    {
        Preflight.Clear();
        foreach (var item in DebugPreflight.Scan(_model!, _source!, hasStepPoints))
        {
            Preflight.Add(item);
        }
        LaunchBlocked = Preflight.Any(i => i.IsBlocking);
    }

    private void FailPreparation(string status)
    {
        Phase = DebuggerPhase.Idle;
        LaunchBlocked = true;
        StatusText = status;
    }

    // ── Launch ────────────────────────────────────────────────────────────────────────────────────

    private bool CanLaunch => Phase is DebuggerPhase.ReadyToLaunch or DebuggerPhase.Idle
                              && !LaunchBlocked && _body is not null && Parameters is not null;

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (_body is null || _model is null || _source is null || Parameters is null) return;

        // Reuse the Smart-Parameters resolve/validate/record path: AcceptCommand validates time fields, sets
        // Result to the ordered bound values, and records the set into history ("last used" for Restart).
        Parameters.AcceptCommand.Execute(null);
        if (Parameters.Result is null) return; // validation error — stay on the launch panel

        var rootValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Parameters.Params.Count; i++)
        {
            rootValues[Parameters.Params[i].Name] = Parameters.Result[i];
        }

        var spec = new DebugLaunchSpec(_source, _body, _model, RoutineName, rootValues, Isolation);

        ClearExecutedSql(); // a fresh session starts a fresh audit log
        Phase = DebuggerPhase.Busy;
        StatusText = UiStrings.DebuggerStatusRunning;
        try
        {
            _run = await Task.Run(() => _launcher.LaunchAsync(spec)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _run = null;
            Phase = DebuggerPhase.Idle;
            StatusText = string.Format(CultureInfo.CurrentCulture, UiStrings.DebuggerStatusLaunchFailedFormat, ex.Message);
            return;
        }

        ApplyBreakpointsToSession();
        await EvaluateWatchesAsync().ConfigureAwait(true); // show watch values immediately at entry
        RefreshFromSession();
    }

    // ── Stepping ──────────────────────────────────────────────────────────────────────────────────

    private bool CanStep => Phase == DebuggerPhase.Paused && Session is not null;

    [RelayCommand(CanExecute = nameof(CanStep))]
    private Task ContinueAsync() => RunStepAsync(s => s.Step(StepKind.Continue));

    [RelayCommand(CanExecute = nameof(CanStep))]
    private Task StepIntoAsync() => RunStepAsync(s => s.Step(StepKind.Into));

    [RelayCommand(CanExecute = nameof(CanStep))]
    private Task StepOverAsync() => RunStepAsync(s => s.Step(StepKind.Over));

    [RelayCommand(CanExecute = nameof(CanStep))]
    private Task StepOutAsync() => RunStepAsync(s => s.Step(StepKind.Out));

    /// <summary>Run To Cursor: runs until the step point at (or first after) <paramref name="caretOffset"/>.
    /// A no-op when the offset does not map to a step point.</summary>
    public Task RunToCursorAsync(int caretOffset)
    {
        if (!CanStep) return Task.CompletedTask;
        var target = StepPointAtOrAfter(caretOffset);
        return target is null ? Task.CompletedTask : RunStepAsync(s => s.RunToCursor(target.Value));
    }

    private async Task RunStepAsync(Action<DebugSession> step)
    {
        var session = Session;
        if (session is null || Phase != DebuggerPhase.Paused) return;

        Phase = DebuggerPhase.Busy;
        StatusText = UiStrings.DebuggerStatusRunning;
        SetCurrentMarker(null, null);
        try
        {
            await Task.Run(() => step(session)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Phase = DebuggerPhase.Faulted;
            StatusText = string.Format(CultureInfo.CurrentCulture, UiStrings.DebuggerStatusFaultedFormat, ex.Message);
            RefreshVariables();
            ResetWatches();
            return;
        }
        await EvaluateWatchesAsync().ConfigureAwait(true); // auto re-evaluate every watch at the new pause (§9.5)
        RefreshFromSession();
    }

    // ── Expression evaluation (Evaluate / Immediate — D5, spec §9.5) ────────────────────────────────

    private bool CanEvaluate => Phase == DebuggerPhase.Paused && Session is not null
                                && !string.IsNullOrWhiteSpace(ImmediateInput);

    /// <summary>Evaluates the Immediate input against the current frame (expression by default, or a statement
    /// when <see cref="ImmediateAsStatement"/> is set). The result lands in the Executed SQL audit log. The
    /// input is intentionally <b>kept</b> after evaluation so the user can tweak and re-run the same
    /// expression (the common debugger workflow) — <see cref="ClearImmediateCommand"/> clears it on demand.</summary>
    [RelayCommand(CanExecute = nameof(CanEvaluate))]
    private async Task EvaluateImmediateAsync()
    {
        var kind = ImmediateAsStatement ? EvaluationKind.Statement : EvaluationKind.Expression;
        await EvaluateFragmentAsync(ImmediateInput, kind).ConfigureAwait(true);
    }

    /// <summary>Clears the Immediate input (the inline ✕ affordance).</summary>
    [RelayCommand(CanExecute = nameof(HasImmediateInput))]
    private void ClearImmediate() => ImmediateInput = string.Empty;

    /// <summary>Evaluate (Shift+F9): evaluates a source selection as an expression against the current frame.
    /// Routes through the SAME engine as the Immediate window (decision 6 — one engine, three surfaces); the
    /// result lands in the same Executed SQL log.</summary>
    public Task EvaluateSelectionAsync(string fragment) => EvaluateFragmentAsync(fragment, EvaluationKind.Expression);

    // The one App-side evaluation path shared by Immediate and Evaluate(Shift+F9). It never evaluates
    // anything itself — the engine is DebugSession.Evaluate (Core), run off the UI thread (the executor is
    // sync-over-async, like stepping). Phase → Busy for the duration gives mutual exclusion with stepping via
    // the existing state machine (a step can't start while Busy, and evaluation requires Paused), so the
    // non-thread-safe DebugSession is never touched concurrently.
    private async Task EvaluateFragmentAsync(string fragment, EvaluationKind kind)
    {
        var session = Session;
        if (session is null || Phase != DebuggerPhase.Paused || string.IsNullOrWhiteSpace(fragment))
        {
            return;
        }

        Phase = DebuggerPhase.Busy;
        EvaluationResult? result = null;
        string? failure = null;
        try
        {
            result = await Task.Run(() => session.Evaluate(fragment, kind)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }

        AddExecutedSql(failure is not null
            ? DebugExecutedSqlRowViewModel.ForException(fragment, failure)
            : DebugExecutedSqlRowViewModel.ForResult(fragment, kind, result!));

        // The session is still paused at the same step; a statement may have changed the frame, so re-evaluate
        // the watches too, then restore Paused + refresh the frame (the live write-back).
        await EvaluateWatchesAsync().ConfigureAwait(true);
        RefreshFromSession();
    }

    private void AddExecutedSql(DebugExecutedSqlRowViewModel row)
    {
        ExecutedSql.Insert(0, row); // newest first
        while (ExecutedSql.Count > ExecutedSqlCap)
        {
            ExecutedSql.RemoveAt(ExecutedSql.Count - 1);
        }
        LatestEvaluation = row;
        OnPropertyChanged(nameof(HasExecutedSql));
    }

    private void ClearExecutedSql()
    {
        LatestEvaluation = null;
        if (ExecutedSql.Count == 0) return;
        ExecutedSql.Clear();
        OnPropertyChanged(nameof(HasExecutedSql));
    }

    // ── Watches (D5 seam b — expressions re-evaluated after every step, §9.5) ───────────────────────

    /// <summary>Adds the <see cref="WatchInput"/> as a watch (flagged if not a pure expression), persists the
    /// list, and evaluates it immediately when paused. The watch is an expression re-evaluated after every
    /// step through the one engine — no separate evaluation mechanism (D5 risk #1).</summary>
    [RelayCommand(CanExecute = nameof(HasWatchInput))]
    private async Task AddWatchAsync()
    {
        var expression = WatchInput.Trim();
        if (expression.Length == 0) return;

        var row = new WatchRowViewModel(expression, WatchSideEffectDetector.HasSideEffect(expression));
        Watches.Add(row);
        WatchInput = string.Empty;
        OnPropertyChanged(nameof(HasWatches));
        SaveWatches();

        // Show a value at once if we are stopped at a frame; otherwise it stays "—" until the next pause.
        if (Phase == DebuggerPhase.Paused)
        {
            await EvaluateWatchesAsync().ConfigureAwait(true);
            RefreshFromSession();
        }
    }

    /// <summary>Removes a watch and persists the list.</summary>
    [RelayCommand]
    private void RemoveWatch(WatchRowViewModel? row)
    {
        if (row is null || !Watches.Remove(row)) return;
        OnPropertyChanged(nameof(HasWatches));
        SaveWatches();
    }

    private void LoadWatches()
    {
        var saved = _watchStore?.Get(_connectionId, RoutineName) ?? Array.Empty<string>();
        foreach (var expression in saved)
        {
            Watches.Add(new WatchRowViewModel(expression, WatchSideEffectDetector.HasSideEffect(expression)));
        }
        OnPropertyChanged(nameof(HasWatches));
    }

    private void SaveWatches()
        => _watchStore?.Save(_connectionId, RoutineName, Watches.Select(w => w.Expression).ToList());

    // Re-evaluates every watch against the current frame, off the UI thread (each is a wire op, like a step).
    // Callers invoke it only while Phase == Busy (right after a pause-producing engine op), so the
    // non-thread-safe DebugSession is never touched concurrently. When the session is not paused (completed /
    // faulted), the watches reset to the "—" placeholder — there is no live frame to evaluate against.
    private async Task EvaluateWatchesAsync()
    {
        var session = Session;
        if (session is null || Watches.Count == 0) return;

        if (session.State != DebugState.Paused)
        {
            foreach (var w in Watches) w.Reset();
            return;
        }

        var expressions = Watches.Select(w => w.Expression).ToList();
        var results = await Task.Run(() => EvaluateWatchExpressions(session, expressions)).ConfigureAwait(true);
        for (int i = 0; i < results.Count && i < Watches.Count; i++)
        {
            Watches[i].Apply(results[i]);
        }
    }

    // Evaluates each expression (in order) as an expression through the one engine. Runs on a background
    // thread; the session stays paused throughout (the caller holds Phase == Busy, so no step intervenes).
    private static IReadOnlyList<EvaluationResult?> EvaluateWatchExpressions(
        DebugSession session, IReadOnlyList<string> expressions)
    {
        var results = new List<EvaluationResult?>(expressions.Count);
        foreach (var expression in expressions)
        {
            try { results.Add(session.Evaluate(expression, EvaluationKind.Expression)); }
            catch { results.Add(null); }
        }
        return results;
    }

    private void ResetWatches()
    {
        foreach (var w in Watches) w.Reset();
    }

    // ── Stop / Restart ──────────────────────────────────────────────────────────────────────────

    private bool CanStopOrRestart => _run is not null
        || Phase is DebuggerPhase.Completed or DebuggerPhase.Faulted;

    [RelayCommand(CanExecute = nameof(CanStopOrRestart))]
    private async Task StopAsync()
    {
        await TeardownRunAsync().ConfigureAwait(true);
        SetCurrentMarker(null, null);
        Variables.Clear();
        OnPropertyChanged(nameof(HasVariables));
        ClearExecutedSql();
        ResetWatches(); // keep the (persisted) watch rows, clear their live values
        Phase = DebuggerPhase.Idle;
        StatusText = UiStrings.DebuggerStatusStopped;
    }

    [RelayCommand(CanExecute = nameof(CanStopOrRestart))]
    private async Task RestartAsync()
    {
        await TeardownRunAsync().ConfigureAwait(true);
        SetCurrentMarker(null, null);
        Variables.Clear();
        OnPropertyChanged(nameof(HasVariables));
        // Reuse the last parameter values (§9.3 — Restart re-runs without re-prompting).
        await LaunchAsync().ConfigureAwait(true);
    }

    // ── Breakpoints ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Toggles a breakpoint at the step point at (or first after) <paramref name="caretOffset"/>.
    /// Breakpoints snap to a real step unit (an <see cref="IExecutableStatement"/>, spec §9.6); a click that
    /// maps to no step point is a no-op.</summary>
    public void ToggleBreakpointAt(int caretOffset)
    {
        var target = StepPointAtOrAfter(caretOffset);
        if (target is null) return;

        if (!_breakpoints.Remove(target.Value))
        {
            _breakpoints.Add(target.Value);
        }
        Session?.Breakpoints.Toggle(target.Value);
        DebugMarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyBreakpointsToSession()
    {
        var session = Session;
        if (session is null) return;
        foreach (var offset in _breakpoints)
        {
            session.Breakpoints.Add(offset);
        }
    }

    // The step point whose span begins at or first after the offset (breakpoints snap to a step unit). Chosen
    // as the earliest step point whose start is >= the offset, so a click anywhere on/above a statement's line
    // lands on it. Returns null when nothing is at/after the offset.
    private int? StepPointAtOrAfter(int offset)
    {
        int? best = null;
        foreach (var sp in _stepPoints)
        {
            if (sp.Start >= offset && (best is null || sp.Start < best.Value))
            {
                best = sp.Start;
            }
        }
        // If nothing is at/after, snap to the enclosing step point that contains the offset (last one starting
        // before it), so a click on a statement's own text still toggles it.
        if (best is null)
        {
            foreach (var sp in _stepPoints)
            {
                if (sp.Start <= offset && offset < sp.Start + sp.Length && (best is null || sp.Start > best.Value))
                {
                    best = sp.Start;
                }
            }
        }
        return best;
    }

    // ── Reading session state into observable surface ───────────────────────────────────────────

    private void RefreshFromSession()
    {
        var session = Session;
        if (session is null) return;

        switch (session.State)
        {
            case DebugState.Paused:
                var step = session.CurrentStatement;
                SetCurrentMarker(step?.Start, step?.Length);
                Phase = DebuggerPhase.Paused;
                StatusText = string.Format(
                    CultureInfo.CurrentCulture, UiStrings.DebuggerStatusPausedFormat,
                    step is null ? 0 : LineOf(step.Start), StopReasonText(session.StopReason));
                break;

            case DebugState.Completed:
                SetCurrentMarker(null, null);
                Phase = DebuggerPhase.Completed;
                StatusText = UiStrings.DebuggerStatusCompleted;
                break;

            case DebugState.Faulted:
                SetCurrentMarker(null, null);
                Phase = DebuggerPhase.Faulted;
                var err = session.CurrentError;
                StatusText = string.Format(
                    CultureInfo.CurrentCulture, UiStrings.DebuggerStatusFaultedFormat,
                    err?.Message ?? err?.ExceptionName ?? "?");
                break;

            default:
                Phase = DebuggerPhase.Busy;
                break;
        }

        RefreshVariables();
    }

    private void RefreshVariables()
    {
        Variables.Clear();
        var session = Session;
        var frame = session?.CurrentFrame;
        if (frame is not null && _model is not null)
        {
            foreach (var row in BuildVariableRows(frame))
            {
                Variables.Add(row);
            }
        }
        OnPropertyChanged(nameof(HasVariables));
    }

    private IEnumerable<DebugVariableRowViewModel> BuildVariableRows(Frame frame)
    {
        // Parameters first (in declaration order), then locals — the semantic model is the roster; the frame
        // holds the live values. A variable defined nowhere in the model but present in the frame is skipped
        // (D4's roster is the declared symbols; richer discovery is D7).
        var symbols = _model!.AllSymbols
            .Where(s => s is ParameterSymbol or VariableSymbol)
            .OrderBy(s => s is ParameterSymbol ? 0 : 1)
            .ThenBy(s => s.DeclarationSpan?.Start ?? int.MaxValue);

        foreach (var s in symbols)
        {
            bool hasValue = frame.TryResolveValue(s.Name, out var value);
            string kind = s is ParameterSymbol
                ? UiStrings.DebuggerVariableKindParameter
                : UiStrings.DebuggerVariableKindLocal;
            yield return new DebugVariableRowViewModel(s.Name, kind, s.DataType, hasValue, value);
        }
    }

    private void SetCurrentMarker(int? start, int? length)
    {
        CurrentStart = start;
        CurrentLength = length;
        DebugMarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    private int LineOf(int offset)
    {
        var src = _source;
        if (src is null || offset <= 0) return 1;
        int line = 1;
        int end = Math.Min(offset, src.Length);
        for (int i = 0; i < end; i++)
        {
            if (src[i] == '\n') line++;
        }
        return line;
    }

    private static string StopReasonText(StopReason reason) => reason switch
    {
        StopReason.Entry => UiStrings.DebuggerStopReasonEntry,
        StopReason.Breakpoint => UiStrings.DebuggerStopReasonBreakpoint,
        _ => UiStrings.DebuggerStopReasonStep,
    };

    private async Task TeardownRunAsync()
    {
        var run = _run;
        _run = null;
        if (run is not null)
        {
            try { await run.DisposeAsync().ConfigureAwait(true); }
            catch { /* best-effort rollback/close */ }
        }
    }

    public async ValueTask DisposeAsync() => await TeardownRunAsync().ConfigureAwait(false);
}
