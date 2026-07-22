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
using EmberTern.Core.Metadata;
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
    // Loads a table's columns (types) for the trigger NEW/OLD launch grids + Variables Context group (D10).
    // Null for a procedure/function launch (no trigger context is ever built).
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ColumnSpec>>>? _columnsProvider;

    // Parsed once during preparation (the strict whole-routine parse — gotcha #238), then reused at launch.
    private string? _source;
    private BlockStatement? _body;
    private SemanticModel? _model;
    private IReadOnlyList<IExecutableStatement> _stepPoints = Array.Empty<IExecutableStatement>();

    // The live run (null before launch / after stop). The session is already Started when set.
    private DebugRunHandle? _run;
    private DebugSession? Session => _run?.Session;

    // Breakpoints kept here (the Core stop-policy objects themselves, not bare offsets) so their conditions +
    // hit-count policies survive launch/restart; mirrored into the live session's BreakpointSet while it exists.
    // Reusing the Core BreakpointSet as the VM's own store is what lets the Breakpoints panel be a pure view of
    // the domain objects (D12 Seam E) — the panel edits these Breakpoint objects directly, decisions stay in Core.
    private readonly BreakpointSet _breakpoints = new();

    // Data breakpoints (D12, spec §9.8.4) — the Core set is the store; a small name→display map keeps a friendly
    // label for the panel (a trigger context row is watched by its synthetic name). Mirrored to the session.
    private readonly DataBreakpointSet _dataBreakpoints = new();
    private readonly Dictionary<string, string> _dataBreakpointNames = new(StringComparer.OrdinalIgnoreCase);

    internal DebuggerTabViewModel(
        string routineName,
        Func<CancellationToken, Task<string?>> sourceProvider,
        IDebugSessionLauncher launcher,
        ParameterHistoryStore? historyStore = null,
        string? connectionId = null,
        WatchStore? watchStore = null,
        Func<string, CancellationToken, Task<IReadOnlyList<ColumnSpec>>>? columnsProvider = null,
        string? packageName = null)
    {
        RoutineName = routineName ?? throw new ArgumentNullException(nameof(routineName));
        _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _historyStore = historyStore;
        _connectionId = connectionId;
        _watchStore = watchStore;
        _columnsProvider = columnsProvider;
        _packageName = packageName;
        Preflight = new ObservableCollection<DebugPreflightItem>();
        Variables = new ObservableCollection<DebugVariableRowViewModel>();
        VariableGroups = new ObservableCollection<DebugVariableGroupViewModel>();
        _pinnedGroup = new DebugVariableGroupViewModel(UiStrings.DebuggerVariableGroupPinned);
        _contextGroup = new DebugVariableGroupViewModel(UiStrings.DebuggerVariableGroupContext);
        _parametersGroup = new DebugVariableGroupViewModel(UiStrings.DebuggerVariableGroupParameters);
        _localsGroup = new DebugVariableGroupViewModel(UiStrings.DebuggerVariableGroupLocals);
        ExecutedSql = new ObservableCollection<DebugExecutedSqlRowViewModel>();
        Watches = new ObservableCollection<WatchRowViewModel>();
        CallStack = new ObservableCollection<DebugFrameRowViewModel>();
        BreakpointRows = new ObservableCollection<BreakpointRowViewModel>();
        DataBreakpointRows = new ObservableCollection<DataBreakpointRowViewModel>();
        SuspendRows = new ObservableCollection<object?[]>();
        Breadcrumbs = new ObservableCollection<string>();
        StatusText = UiStrings.DebuggerLaunchPreparing;
        LoadWatches();
    }

    private readonly ParameterHistoryStore? _historyStore;
    private readonly string? _connectionId;
    private readonly WatchStore? _watchStore;

    // The package a package-member root belongs to (D11 seam C) — non-null ONLY when this tab debugs a package
    // member launched directly (the source provider returns the member reconstructed as a standalone CREATE
    // PROCEDURE; this threads the package name into the launch spec so the executor sets up the package frame —
    // sibling-call resolution + package-keyed catalog params). Null for every standalone routine / trigger.
    private readonly string? _packageName;

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

    /// <summary>The current frame's variables — the flat roster (all rows, unfiltered, stable identity per
    /// frame). The grouped/filtered presentation is <see cref="VariableGroups"/> over these same instances.</summary>
    public ObservableCollection<DebugVariableRowViewModel> Variables { get; }

    /// <summary>The Variables panel's grouped, pinned and filtered presentation (Pinned / Context / Parameters /
    /// Locals — spec §9.4). References the same row instances as <see cref="Variables"/>; empty groups are hidden.
    /// (Context = a debugged trigger's NEW/OLD columns, D10, present only in trigger mode; Cursors needs cursor
    /// surfacing — deliberately not shipped as an empty group.)</summary>
    public ObservableCollection<DebugVariableGroupViewModel> VariableGroups { get; }

    // Persistent group instances (reused across pauses so IsExpanded survives a step-by-step rebuild).
    private readonly DebugVariableGroupViewModel _pinnedGroup;
    private readonly DebugVariableGroupViewModel _contextGroup; // trigger NEW/OLD (D10) — only in trigger mode
    private readonly DebugVariableGroupViewModel _parametersGroup;
    private readonly DebugVariableGroupViewModel _localsGroup;

    // Trigger-debug state (Stage X / D10), populated during preparation when the routine is a relation trigger.
    // The launch panel shows the trigger editor instead of plain parameters; the launched context feeds the
    // Variables Context group. All null / false for a procedure/function launch (D4–D9 paths untouched).
    private IReadOnlyList<ContextColumn> _triggerColumns = Array.Empty<ContextColumn>();
    private IReadOnlyDictionary<string, string> _triggerColumnTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private TriggerContext? _activeTrigger; // the context launched with (drives Context-row availability)

    /// <summary>True when the routine being debugged is a relation trigger (Stage X / D10) — the launch panel
    /// shows NEW/OLD context editors instead of procedure parameters, and Variables gains a Context group.</summary>
    [ObservableProperty]
    private bool _isTriggerMode;

    /// <summary>The trigger launch editor (NEW/OLD context values + action selector, spec §8.1). Null until
    /// preparation identifies a debuggable trigger; null for a procedure/function.</summary>
    [ObservableProperty]
    private TriggerContextEditorViewModel? _triggerEditor;

    // Change-highlighting state: the innermost frame's values as of the previous STEP, and the frame they
    // belonged to. A new innermost-frame identity resets the baseline so the first pause in a frame marks
    // nothing "changed". Only the step path updates this — browsing a caller frame never disturbs it.
    private System.Collections.Generic.Dictionary<string, object?>? _previousValues;
    private int? _previousFrameId;

    // The frame the Variables roster (row identity) currently reflects. Separate from the change baseline
    // above: browsing to a caller rebuilds the roster for that frame, but must NOT reset the step baseline.
    private int? _rosterFrameId;

    // The frame the UI is inspecting (spec §5.2) — drives SourceText, the current-line marker and the
    // Variables roster. Defaults to the innermost (current) frame on every pause; the call stack / breadcrumbs
    // / Ctrl+Alt+Up/Down repoint it to a caller without touching the engine (navigation, not execution).
    private Frame? _selectedFrame;
    // Guards the two selection controls (call-stack SelectedItem, breadcrumb SelectedIndex) from re-entering
    // selection while ApplySelectedFrame syncs them — the frame is the one truth, the controls only mirror it.
    private bool _syncingFrameSelection;

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

    /// <summary>The call stack (Stage X / D8, spec §5.2) — frames innermost-first, rebuilt each pause. A
    /// callee reached by Step Into carries the simulated-frame indicator (§5.3). Selecting a row repoints the
    /// editor source, current-line marker and Variables to that frame (<see cref="SelectedFrameRow"/>); the
    /// breadcrumb bar and Ctrl+Alt+Up/Down are the other two ways in — all route through the one
    /// <see cref="SelectFrame"/>. It reads the engine's <see cref="DebugSession.CallStack"/>; navigation never
    /// touches the session (it is structural, not execution).</summary>
    public ObservableCollection<DebugFrameRowViewModel> CallStack { get; }

    /// <summary>True while there is a call stack to show (a live paused session with at least one frame).</summary>
    public bool HasCallStack => CallStack.Count > 0;

    // ── Breakpoints panel (D12 Seam E) — a pure VIEW of the Core Breakpoint / DataBreakpoint objects ───────

    /// <summary>The line breakpoints as editable projections of the Core <see cref="Breakpoint"/> objects in
    /// <see cref="_breakpoints"/> (D12, spec §9.8). Each row edits its wrapped breakpoint's condition / hit-count
    /// directly; the panel holds no policy logic. Rebuilt whenever the breakpoint set changes.</summary>
    public ObservableCollection<BreakpointRowViewModel> BreakpointRows { get; }

    /// <summary>The data breakpoints as read-only projections of the Core <see cref="DataBreakpoint"/> objects
    /// (spec §9.8.4 — "break when this variable changes"). Added via the Variables "Break when changes" gesture.</summary>
    public ObservableCollection<DataBreakpointRowViewModel> DataBreakpointRows { get; }

    public bool HasBreakpoints => BreakpointRows.Count > 0;
    public bool HasDataBreakpoints => DataBreakpointRows.Count > 0;
    public bool HasAnyBreakpoints => HasBreakpoints || HasDataBreakpoints;

    /// <summary>Break on exception (spec §9.8.1): when set, a raise PAUSES at the raising statement before it is
    /// routed to a handler. A pure mirror of <see cref="DebugSession.BreakOnException"/> — the pause + routing
    /// logic is entirely in Core; this only reflects the toggle to the live session (and applies it at launch).
    /// Persists across launch/restart because it lives on the VM, not the (recreated) session.</summary>
    [ObservableProperty]
    private bool _breakOnException;

    partial void OnBreakOnExceptionChanged(bool value)
    {
        if (Session is { } s) s.BreakOnException = value;
    }

    // ── Results grid (D12 Seam E2) — a view of the session's SUSPEND-emitted rows (spec §9.8) ──────────────

    /// <summary>The rows emitted by <c>SUSPEND</c> so far (Run to next <c>SUSPEND</c>, or any run that passed a
    /// <c>SUSPEND</c>), each as an <c>object?[]</c> aligned to <see cref="SuspendColumns"/> — bound to the
    /// Results DataGrid. A pure projection of <see cref="DebugSession.EmittedRows"/> (the engine produces the
    /// rows; this only shapes them for the grid). Rows only ever grow within a run; cleared on a fresh launch.</summary>
    public ObservableCollection<object?[]> SuspendRows { get; }

    private readonly List<string> _suspendColumns = new();

    /// <summary>The result columns (the routine's <c>RETURNS</c> names, from the first emitted row). The view
    /// builds the DataGrid columns from these on <see cref="SuspendColumnsChanged"/>.</summary>
    public IReadOnlyList<string> SuspendColumns => _suspendColumns;

    public bool HasSuspendRows => SuspendRows.Count > 0;

    /// <summary>Raised when the result columns change (the first SUSPEND row of a run, or a clear) so the view
    /// rebuilds the DataGrid columns — mirrors the main result grid's structure-change rebuild.</summary>
    public event EventHandler? SuspendColumnsChanged;

    // Projects the session's emitted SUSPEND rows into the grid-friendly SuspendRows/SuspendColumns. Called on
    // every pause/terminal refresh; rows only grow within a run, so it appends new rows (rebuilding columns only
    // when the structure first appears or changes). The engine owns the rows; this never computes a value.
    private void RebuildSuspendRows()
    {
        var rows = Session?.EmittedRows;
        if (rows is null || rows.Count == 0)
        {
            if (SuspendRows.Count > 0 || _suspendColumns.Count > 0)
            {
                SuspendRows.Clear();
                _suspendColumns.Clear();
                SuspendColumnsChanged?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(HasSuspendRows));
            }
            return;
        }

        var cols = rows[0].Keys.ToList();
        if (!_suspendColumns.SequenceEqual(cols, StringComparer.OrdinalIgnoreCase))
        {
            _suspendColumns.Clear();
            _suspendColumns.AddRange(cols);
            SuspendRows.Clear();
            SuspendColumnsChanged?.Invoke(this, EventArgs.Empty); // the view rebuilds the DataGrid columns
        }

        for (int r = SuspendRows.Count; r < rows.Count; r++) // append only the newly-emitted rows
        {
            var arr = new object?[_suspendColumns.Count];
            for (int i = 0; i < _suspendColumns.Count; i++)
                arr[i] = rows[r].TryGetValue(_suspendColumns[i], out var v) ? v : null;
            SuspendRows.Add(arr);
        }
        OnPropertyChanged(nameof(HasSuspendRows));
    }

    private void ClearSuspendRows()
    {
        if (SuspendRows.Count == 0 && _suspendColumns.Count == 0) return;
        SuspendRows.Clear();
        _suspendColumns.Clear();
        SuspendColumnsChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(HasSuspendRows));
    }

    /// <summary>The selected call-stack row (bound two-way to the Call Stack list's SelectedItem). A user pick
    /// routes to <see cref="SelectFrame"/>; a programmatic sync (a new pause / breadcrumb / keyboard) sets it
    /// under <see cref="_syncingFrameSelection"/>.</summary>
    [ObservableProperty]
    private DebugFrameRowViewModel? _selectedFrameRow;

    /// <summary>The id of the inspected frame (spec §5.2) — the current-line marker + Variables reflect it.</summary>
    [ObservableProperty]
    private int _selectedFrameId = -1;

    /// <summary>The breadcrumb path (Stage X / D8) — the call stack read <b>outermost→innermost</b>
    /// (ROOT › … › current), mirroring the stack (the reverse of <see cref="CallStack"/>). Fed to the shared
    /// <see cref="EmberTern.App.Controls.BreadcrumbBar"/>; clicking a crumb selects that frame.</summary>
    public ObservableCollection<string> Breadcrumbs { get; }

    /// <summary>The selected breadcrumb index (bound two-way to the breadcrumb bar). Maps to a frame through
    /// the outermost→innermost order; a user pick routes to <see cref="SelectFrame"/>.</summary>
    [ObservableProperty]
    private int _selectedBreadcrumbIndex = -1;

    partial void OnSelectedFrameRowChanged(DebugFrameRowViewModel? value)
    {
        if (_syncingFrameSelection || value is null) return;
        SelectFrame(value.FrameId);
    }

    partial void OnSelectedBreadcrumbIndexChanged(int value)
    {
        if (_syncingFrameSelection || value < 0 || value >= Breadcrumbs.Count) return;
        // Breadcrumbs are outermost→innermost; CallStack is innermost-first. Map across.
        int callIndex = CallStack.Count - 1 - value;
        if (callIndex >= 0 && callIndex < CallStack.Count) SelectFrame(CallStack[callIndex].FrameId);
    }

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
    [NotifyPropertyChangedFor(nameof(IsFaulted))]
    [NotifyPropertyChangedFor(nameof(HasVariables))]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepIntoCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOverCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunToSuspendCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunToLoopExitCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunToNextIterationCommand))]
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

    /// <summary>The breakpoint step-point offsets — the BreakpointMargin reads this. Breakpoints belong to the
    /// launched (root) routine; while the editor shows a <em>different</em> frame's source (a stepped-into
    /// callee, or a selected caller other than the root) the offsets are in a different coordinate space, so
    /// none are surfaced (nested-routine breakpoints are a later milestone — D12). Stepping still works fully;
    /// only breakpoint editing + Run-To-Cursor are root-source-scoped.</summary>
    public IReadOnlyCollection<int> BreakpointOffsets => IsViewingRootSource ? _breakpoints.Offsets : Array.Empty<int>();

    // True while the editor shows the launched routine's own source — i.e. no frame is selected yet, or the
    // selected frame IS the root (its body is the parsed root body). Breakpoints + Run-To-Cursor act only then.
    private bool IsViewingRootSource
        => _selectedFrame is null || (_body is not null && ReferenceEquals(_selectedFrame.Body, _body));

    /// <summary>Raised when the current-statement marker or the breakpoint set changes, so the view can
    /// repaint the renderers (via <c>TextView.Redraw()</c>, never <c>InvalidateVisual()</c> — gotcha #223).</summary>
    public event EventHandler? DebugMarkersChanged;

    public bool IsLaunchPanelVisible => Phase is DebuggerPhase.Preparing or DebuggerPhase.ReadyToLaunch or DebuggerPhase.Idle;
    public bool IsDebugViewVisible => !IsLaunchPanelVisible;
    public bool IsPaused => Phase == DebuggerPhase.Paused;

    /// <summary>True when the session ended on an unhandled exception — the view paints the status line in the
    /// error colour so the fault is immediately noticeable.</summary>
    public bool IsFaulted => Phase == DebuggerPhase.Faulted;

    public bool HasVariables => Variables.Count > 0;

    // ── D15.2 Seam C — Error Bar ────────────────────────────────────────────────────────────────
    // A faulted session (or a Break-on-Exception pause) surfaces the raw Firebird error in its OWN
    // thin row below the toolbar — never crammed into the toolbar status (which keeps a fixed height).
    // Pure presentation over the engine's DebugError: this VM only projects the message + owns the
    // bar's expand/dismiss view-state; Copy (clipboard) lives in the view. The bar shows while there is
    // an error message and the user has not dismissed it; a new fault/pause re-shows it (SetError clears
    // the dismiss), and leaving the error state clears it (ClearError).

    /// <summary>The full error text shown in the Error Bar (empty ⇒ no error). Set on a fault / an
    /// unexpected engine crash / a Break-on-Exception pause; cleared when the session leaves that state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorBar))]
    private string _errorDetail = string.Empty;

    /// <summary>Error Bar shows the full message by default (Firebird errors are short — 2–6 lines); the
    /// height is capped + scrolled for the rare long one. Collapse is the opt-in "safety valve" that shrinks
    /// it to a single ellipsised line to reclaim editor space. Defaults to expanded (full).</summary>
    [ObservableProperty]
    private bool _isErrorExpanded = true;

    private bool _errorDismissed;

    /// <summary>The Error Bar is visible while there is an error message and it has not been dismissed.</summary>
    public bool ShowErrorBar => !_errorDismissed && ErrorDetail.Length > 0;

    // Enter an error state: show the bar (un-dismissed, full message) with the given text. A new error always
    // re-expands to the full message (a previous manual collapse does not carry over).
    private void SetError(string detail)
    {
        ErrorDetail = detail ?? string.Empty; // NotifyPropertyChangedFor raises ErrorDetail + ShowErrorBar
        IsErrorExpanded = true;
        _errorDismissed = false;
        OnPropertyChanged(nameof(ShowErrorBar));
    }

    // Leave the error state: hide the bar. Cheap no-op when already clear.
    private void ClearError()
    {
        if (ErrorDetail.Length == 0 && !_errorDismissed) return;
        _errorDismissed = false;
        ErrorDetail = string.Empty; // raises ErrorDetail + ShowErrorBar
    }

    [RelayCommand]
    private void ToggleErrorExpanded() => IsErrorExpanded = !IsErrorExpanded;

    [RelayCommand]
    private void DismissError()
    {
        _errorDismissed = true;
        OnPropertyChanged(nameof(ShowErrorBar));
    }

    // A readable one-string description of a DebugError for the bar (prefer the server message; the
    // exception name / SQLSTATE / GDS are fallbacks). Never parses the message — just picks the best field.
    private static string DescribeError(DebugError? e)
    {
        if (e is null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(e.Message)) return e.Message!.Trim();
        if (!string.IsNullOrWhiteSpace(e.ExceptionName)) return e.ExceptionName!;
        if (!string.IsNullOrWhiteSpace(e.SqlState)) return $"SQLSTATE {e.SqlState}";
        if (e.GdsCode is { } g) return $"GDS {g}";
        return UiStrings.DebuggerErrorUnknown;
    }

    /// <summary>Type-to-filter for the Variables panel (by name, case-insensitive contains — mirrors the
    /// sidebar). Presentation only: it re-groups the existing roster, it never re-reads the frame.</summary>
    [ObservableProperty]
    private string _variableFilter = string.Empty;

    partial void OnVariableFilterChanged(string value) => RebuildVariableGroups();

    /// <summary>Presentation state (not debug logic): whether the bottom tabbed panel (Immediate / Executed
    /// SQL / Watches, and future Call Stack / Breakpoints / Output) is collapsed so the editor + Variables get
    /// the full height. The view owns the row-height mechanics (mirrors the SQL results panel).</summary>
    [ObservableProperty]
    private bool _isBottomPanelCollapsed;

    /// <summary>Collapses / expands the bottom tabbed panel.</summary>
    [RelayCommand]
    private void ToggleBottomPanel() => IsBottomPanelCollapsed = !IsBottomPanelCollapsed;

    /// <summary>Presentation state (D15.3 Seam B): whether the launch panel's Advanced section (transaction
    /// isolation) is expanded. Collapsed by default — most users never change isolation, so it stays out of
    /// the main Launch flow. Mirrors <see cref="IsBottomPanelCollapsed"/>: a pure view-state toggle, no logic.</summary>
    [ObservableProperty]
    private bool _isAdvancedExpanded;

    /// <summary>Expands / collapses the launch panel's Advanced (isolation) section.</summary>
    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedExpanded = !IsAdvancedExpanded;

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

        // A relation trigger launches with NEW/OLD context editors instead of parameters (§8.1); a procedure/
        // function keeps the plain parameter panel. TriggerHeaderReader refuses a DB-level / DDL trigger.
        if (ddl is not null && ddl.ObjectKind == DdlObjectKind.Trigger)
        {
            if (!await TryPrepareTriggerAsync(ddl, cancellationToken).ConfigureAwait(true)) return;
        }
        else
        {
            BuildParameters();
        }

        BuildPreflight(hasStepPoints: _stepPoints.Count > 0 && _body is not null);

        Phase = DebuggerPhase.ReadyToLaunch;
        StatusText = UiStrings.DebuggerStatusReady;

        // No-decision fast path (D15.3 Seam C): if the user has nothing to decide before launching — a
        // non-trigger routine with no input parameters and a clean pre-flight — skip the launch panel
        // entirely (Debug → Preparing → session). Any parameter, trigger context (NEW/OLD), or pre-flight
        // note (a §4.6 data-safety warning is a decision) keeps the panel shown. Isolation is NOT a required
        // decision (it defaults to Read Committed and lives in Advanced), so it never blocks the fast path.
        if (ShouldAutoLaunch()) await LaunchAsync().ConfigureAwait(true);
    }

    /// <summary>The launch panel offers no decision — a non-trigger routine with no input parameters and a
    /// clean pre-flight — so launching is immediate (D15.3 Seam C). Only valid right after preparation
    /// (ReadyToLaunch), never from Idle after a Stop (the user deliberately stopped).</summary>
    private bool ShouldAutoLaunch()
        => Phase == DebuggerPhase.ReadyToLaunch
           && !IsTriggerMode
           && Parameters is { Params.Count: 0 }
           && Preflight.Count == 0
           && CanLaunch;

    // Prepares the trigger launch editor (Stage X / D10): reads the header facts (target table / timing / DML
    // events) from the AST via the Core reader, derives the referenced NEW/OLD columns (reference-driven, never
    // a text scan), types them from the target-table catalog, and builds the dumb editor VM. Returns false (and
    // fails preparation) for a DB-level / DDL trigger — those have no target table or DML event (§8.1, out of
    // scope). All availability/predicate rules stay in Core (TriggerContext); this only wires the UI to them.
    private async Task<bool> TryPrepareTriggerAsync(DdlStatement ddl, CancellationToken cancellationToken)
    {
        var header = TriggerHeaderReader.Read(ddl);
        if (header is null || ddl.Body is null)
        {
            FailPreparation(UiStrings.DebuggerTriggerOutOfScope);
            return false;
        }

        var columns = ContextSubstitution.BuildColumns(_model!, new TextSpan(ddl.Body.Start, ddl.Body.Length));
        var columnTypes = await LoadTriggerColumnTypesAsync(header.TargetTable, cancellationToken).ConfigureAwait(true);
        _triggerColumns = columns;
        _triggerColumnTypes = columnTypes;
        TriggerEditor = new TriggerContextEditorViewModel(header, columns, columnTypes, _connectionId, _historyStore);
        IsTriggerMode = true;
        return true;
    }

    // The base types of the trigger's target-table columns (folded name → type), for the NEW/OLD launch grids
    // and the Variables Context group. Best-effort — a missing provider / read leaves a column untyped (a plain
    // text box). Never re-derives Firebird semantics; it only labels the entry controls.
    private async Task<IReadOnlyDictionary<string, string>> LoadTriggerColumnTypesAsync(
        string table, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_columnsProvider is null) return map;
        try
        {
            var cols = await _columnsProvider(table, cancellationToken).ConfigureAwait(true);
            foreach (var c in cols) map[c.Name.ToUpperInvariant()] = c.Type;
        }
        catch { /* best-effort — an untyped column falls back to a text box */ }
        return map;
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
                              && !LaunchBlocked && _body is not null
                              && (Parameters is not null || (IsTriggerMode && TriggerEditor is not null));

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (_body is null || _model is null || _source is null) return;

        // Collect the root-frame seed + (for a trigger) its context. A trigger uses the NEW/OLD editors; a
        // procedure/function uses the plain parameter grid. Either path returns null on a validation error, so
        // we stay on the launch panel.
        var launch = IsTriggerMode ? BuildTriggerLaunch() : BuildParameterLaunch();
        if (launch is not (var rootValues, var trigger)) return;

        _activeTrigger = trigger; // drives the Variables Context group (available NEW/OLD rows)
        // The session SHARES the VM's breakpoint / data-breakpoint sets (D12) — so a breakpoint on the FIRST
        // statement is active from Start (honored by the same stop-decision as every statement), and the panel
        // edits the very objects the engine consults (no mirroring). BreakOnException seeds the session toggle.
        var spec = new DebugLaunchSpec(
            _source, _body, _model, RoutineName, rootValues, Isolation, trigger, _packageName,
            _breakpoints, _dataBreakpoints, BreakOnException);

        ClearExecutedSql();  // a fresh session starts a fresh audit log
        ClearSuspendRows();  // …and a fresh (empty) result set
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

        RebuildBreakpointPanel(); // the panel reflects the (persisted) breakpoints now that a run exists
        await EvaluateWatchesAsync().ConfigureAwait(true); // show watch values immediately at entry
        RefreshFromSession();
    }

    // A procedure/function launch: the root-frame seed comes from the plain parameter grid (name → value), no
    // trigger context. Returns null on a validation error (Result stays null) so the launch panel stays shown.
    // Reuses the Smart-Parameters resolve/validate/record path: AcceptCommand validates time fields, sets Result
    // to the ordered bound values, and records the set into history ("last used" for Restart).
    private (IReadOnlyDictionary<string, object?> RootValues, TriggerContext? Trigger)? BuildParameterLaunch()
    {
        if (Parameters is null) return null;
        Parameters.AcceptCommand.Execute(null);
        if (Parameters.Result is null) return null;

        var rootValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Parameters.Params.Count; i++)
        {
            rootValues[Parameters.Params[i].Name] = Parameters.Result[i];
        }
        return (rootValues, null);
    }

    // A trigger launch (Stage X / D10, §8.1): the root-frame seed is the entered NEW/OLD values keyed by their
    // synthetic frame variables, plus the Core TriggerContext for the picked action. All the NEW/OLD availability
    // + synthetic mapping lives in the (dumb) editor VM over Core; this only ferries it into the launch spec.
    private (IReadOnlyDictionary<string, object?> RootValues, TriggerContext? Trigger)? BuildTriggerLaunch()
    {
        if (TriggerEditor is null) return null;
        if (!TriggerEditor.Accept()) return null; // a shown NEW/OLD grid failed validation
        var context = TriggerEditor.BuildTriggerContext();
        return (TriggerEditor.CollectRootValues(context), context);
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

    /// <summary>Run to next <c>SUSPEND</c> (D12, spec §9.8): runs full speed until the next <c>SUSPEND</c>
    /// emits a row (a selectable procedure's "give me the next row"), then pauses. The emitted rows collect in
    /// the Results grid. A non-selectable routine simply runs to completion. Pure delegation to the engine.</summary>
    [RelayCommand(CanExecute = nameof(CanStep))]
    private Task RunToSuspendAsync() => RunStepAsync(s => s.RunToSuspend());

    // Loop fast-forward (D13) — enabled only while paused INSIDE a loop (the engine's own gate,
    // DebugSession.IsInsideLoop). Both commands are pure delegation to the engine's stop policies; no business
    // logic lives here. Re-evaluated on every Phase change (each step cycles Busy→Paused), so the buttons
    // enable/disable as stepping enters and leaves loops.
    private bool CanFastForward => Phase == DebuggerPhase.Paused && Session is { IsInsideLoop: true };

    /// <summary>Continue Until Loop Exit (D13): runs full speed until the innermost enclosing loop is left by any
    /// path (condition false / cursor exhausted / <c>LEAVE</c>/<c>BREAK</c>/<c>EXIT</c>), then pauses just after
    /// it (or completes if the loop was the routine's last action). Pure delegation to the engine.</summary>
    [RelayCommand(CanExecute = nameof(CanFastForward))]
    private Task RunToLoopExitAsync() => RunStepAsync(s => s.RunToLoopExit());

    /// <summary>Next Iteration (D13): runs full speed until the innermost enclosing loop begins its next
    /// iteration, then pauses at that iteration's first step point; if the loop exits first it pauses after the
    /// loop, like Continue Until Loop Exit. Pure delegation to the engine.</summary>
    [RelayCommand(CanExecute = nameof(CanFastForward))]
    private Task RunToNextIterationAsync() => RunStepAsync(s => s.RunToNextIteration());

    /// <summary>Run To Cursor: runs until the step point at (or first after) <paramref name="caretOffset"/>.
    /// A no-op when the offset does not map to a step point.</summary>
    public Task RunToCursorAsync(int caretOffset)
    {
        if (!CanStep || !IsViewingRootSource) return Task.CompletedTask; // targets root step points (see above)
        var target = StepPointAtOrAfter(caretOffset);
        return target is null ? Task.CompletedTask : RunStepAsync(s => s.RunToCursor(target.Value));
    }

    private async Task RunStepAsync(Action<DebugSession> step)
    {
        var session = Session;
        if (session is null || Phase != DebuggerPhase.Paused) return;

        Phase = DebuggerPhase.Busy;
        StatusText = UiStrings.DebuggerStatusRunning;
        ClearError(); // resuming (incl. routing a held raise) — drop any shown error until the run settles
        SetCurrentMarker(null, null);
        try
        {
            await Task.Run(() => step(session)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Phase = DebuggerPhase.Faulted;
            SetError(ex.Message);
            StatusText = UiStrings.DebuggerStatusFaulted;
            ClearVariables(); // an unexpected engine crash: no live frame to inspect
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
        ClearVariables();
        ClearExecutedSql();
        ClearSuspendRows();
        ClearError();
        ResetWatches(); // keep the (persisted) watch rows, clear their live values
        Phase = DebuggerPhase.Idle;
        StatusText = UiStrings.DebuggerStatusStopped;
    }

    [RelayCommand(CanExecute = nameof(CanStopOrRestart))]
    private async Task RestartAsync()
    {
        await TeardownRunAsync().ConfigureAwait(true);
        SetCurrentMarker(null, null);
        ClearVariables();
        ClearError();
        // Reuse the last parameter values (§9.3 — Restart re-runs without re-prompting).
        await LaunchAsync().ConfigureAwait(true);
    }

    // ── Breakpoints ───────────────────────────────────────────────────────────────────────────────

    /// <summary>Toggles a breakpoint at the step point at (or first after) <paramref name="caretOffset"/>.
    /// Breakpoints snap to a real step unit (an <see cref="IExecutableStatement"/>, spec §9.6); a click that
    /// maps to no step point is a no-op.</summary>
    public void ToggleBreakpointAt(int caretOffset)
    {
        if (!IsViewingRootSource) return; // breakpoints are the root routine's; a callee/caller view is D12
        var target = StepPointAtOrAfter(caretOffset);
        if (target is null) return;

        _breakpoints.Toggle(target.Value); // add a plain breakpoint, or remove the existing one — the session
                                           // SHARES this set, so the change is live with no mirroring
        RebuildBreakpointPanel();
        DebugMarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    // Rebuilds the Breakpoints-panel rows from the VM's Core sets — the panel is a pure projection, so this is
    // called whenever the sets change (toggle / add / remove / launch). Line breakpoints are ordered by offset;
    // each row wraps its Core Breakpoint. The session shares these sets, so a row's condition / hit-count edit
    // is live on the engine's own object — no callback, no sync.
    private void RebuildBreakpointPanel()
    {
        BreakpointRows.Clear();
        foreach (var offset in _breakpoints.Offsets.OrderBy(o => o))
        {
            var bp = _breakpoints.Get(offset);
            if (bp is null) continue;
            BreakpointRows.Add(new BreakpointRowViewModel(bp, LineOf(offset)));
        }

        DataBreakpointRows.Clear();
        foreach (var name in _dataBreakpoints.Variables)
        {
            DataBreakpointRows.Add(new DataBreakpointRowViewModel(
                name, _dataBreakpointNames.TryGetValue(name, out var display) ? display : name));
        }

        OnPropertyChanged(nameof(HasBreakpoints));
        OnPropertyChanged(nameof(HasDataBreakpoints));
        OnPropertyChanged(nameof(HasAnyBreakpoints));
    }

    /// <summary>Adds a data breakpoint on a variable (the Variables "Break when changes" gesture, spec §9.8.4) —
    /// the session breaks when that variable's value changes across a step. The Core <see cref="DataBreakpointSet"/>
    /// owns the change detection; this only registers the watch and mirrors it to the live session.</summary>
    [RelayCommand]
    private void AddDataBreakpoint(DebugVariableRowViewModel? row)
    {
        if (row is null) return;
        if (_dataBreakpoints.Add(row.ResolveName)) // the session shares this set → live, no mirroring
        {
            _dataBreakpointNames[row.ResolveName] = row.Name; // friendly label (NEW.col / plain name)
            RebuildBreakpointPanel();
        }
    }

    /// <summary>Removes a line breakpoint from the Breakpoints panel (and the editor gutter + live session, which
    /// shares the set).</summary>
    [RelayCommand]
    private void RemoveBreakpoint(BreakpointRowViewModel? row)
    {
        if (row is null) return;
        _breakpoints.Remove(row.Offset);
        RebuildBreakpointPanel();
        DebugMarkersChanged?.Invoke(this, EventArgs.Empty); // the gutter dot goes away
    }

    /// <summary>Removes a data breakpoint from the Breakpoints panel (and the live session, which shares the set).</summary>
    [RelayCommand]
    private void RemoveDataBreakpoint(DataBreakpointRowViewModel? row)
    {
        if (row is null) return;
        _dataBreakpoints.Remove(row.WatchedName);
        _dataBreakpointNames.Remove(row.WatchedName);
        RebuildBreakpointPanel();
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

        RebuildSuspendRows(); // reflect any newly-emitted SUSPEND rows (Run-to-SUSPEND, or a run past a SUSPEND)

        switch (session.State)
        {
            case DebugState.Paused:
                // Reset the inspected frame to the innermost (current) frame on every pause; the call stack /
                // breadcrumbs / keyboard repoint it afterwards without a step.
                _selectedFrame = session.CurrentFrame;
                Phase = DebuggerPhase.Paused;
                var step = session.CurrentStatement;
                int line = step is null ? 0 : LineOf(session.CurrentFrame?.Source ?? _source, step.Start);
                StatusText = string.Format(
                    CultureInfo.CurrentCulture, UiStrings.DebuggerStatusPausedFormat,
                    line, PausedReasonText(session));
                // Break-on-Exception pause: the raise is held, frame intact — surface its message in the
                // Error Bar. Any other (ordinary) pause has no error, so the bar clears.
                if (session.IsPausedOnException) SetError(DescribeError(session.CurrentError));
                else ClearError();
                RebuildCallStack();
                RebuildBreadcrumbs();
                if (session.CurrentFrame is { } current) ApplySelectedFrame(current, computeChanges: true);
                return;

            case DebugState.Completed:
                // The session ran to the end. Rather than clearing (which made the session "vanish"), keep the
                // terminal snapshot visible: the last executed line stays marked, and Variables / Context / Call
                // Stack show the FINAL frame values. Stepping is disabled (CanStep needs Paused); Restart / Stop
                // stay enabled, and Stop is what finally tears the session down. The engine retains the terminal
                // frame + last statement for exactly this (DebugSession.FinalFrame / LastStatement).
                Phase = DebuggerPhase.Completed;
                StatusText = UiStrings.DebuggerStatusCompleted;
                ClearError();
                ShowCompletedState(session);
                return;

            case DebugState.Faulted:
                // Like Completed, keep the state visible instead of clearing: stop ON the faulting line (marked),
                // with Variables / Context / Call Stack showing the values AT the error. The DB effects are rolled
                // back (§4.5), but the client-side variable values at the raise survive — the useful debugging
                // info. Stepping is disabled; Restart / Stop stay enabled, and Stop tears the session down.
                Phase = DebuggerPhase.Faulted;
                // The full Firebird message goes to the Error Bar (its own row); the status line stays a
                // short, fixed-height headline (D15.2 Seam C — no more cramming the message in-row).
                SetError(DescribeError(session.CurrentError));
                StatusText = UiStrings.DebuggerStatusFaulted;
                ShowFaultState(session);
                return;

            default:
                Phase = DebuggerPhase.Busy;
                return;
        }
    }

    // Renders the terminal (Completed) state from the engine's retained snapshot (spec §5 — frames are data):
    // the final frame's values (last write-back / RETURNING_VALUES / trigger NEW-OLD), a single-frame call stack,
    // and the closing END of the block MARKED — so it reads as "execution finished here", not "about to run the
    // last statement" (IBExpert-like). Pure read of the retained frame; the session is not touched (it is torn
    // down only by Stop / Restart). An empty body (nothing ran) shows no marker and no variables.
    private void ShowCompletedState(DebugSession session)
    {
        var frame = session.FinalFrame;
        if (frame is null)
        {
            SetCurrentMarker(null, null);
            ClearVariables();
            return;
        }

        _selectedFrame = frame;
        SelectedFrameId = frame.Id;
        SourceText = frame.Source ?? _source ?? string.Empty;

        // The closing END of the routine's block (the terminal frame's own body). Fall back to the last executed
        // step point only if the END can't be located (malformed) — but never a callee-space offset (#243).
        var end = EndMarkerOf(frame.Body);
        if (end is null && session.LastStatement is { } last && _stepPoints.Contains(last))
        {
            end = (last.Start, last.Length);
        }

        RebuildTerminalCallStack(new[] { frame }, end?.Start ?? -1);
        ShowFrameVariables(frame, computeChanges: false); // final values, no change-highlight
        SetCurrentMarker(end?.Start, end?.Length);
    }

    // Renders the terminal (Faulted) state: stop ON the faulting line (marked), showing the innermost fault
    // frame's Variables / Context and the whole call stack AT the fault — the engine retained both (the live
    // stack is empty after the unwind). The faulting statement is in the innermost frame's own source, so the
    // marker aligns with the shown source. Mirrors ShowCompletedState; the difference is the stack (multi-frame)
    // and the marked line (the raise point, not END).
    private void ShowFaultState(DebugSession session)
    {
        var frame = session.FaultFrame;
        if (frame is null)
        {
            SetCurrentMarker(null, null);
            ClearVariables();
            return;
        }

        var stmt = session.FaultStatement;
        _selectedFrame = frame;
        SelectedFrameId = frame.Id;
        SourceText = frame.Source ?? _source ?? string.Empty;

        RebuildCallStackFrom(session.FaultStack, stmt?.Start ?? -1);
        RebuildBreadcrumbsFrom(session.FaultStack);
        ShowFrameVariables(frame, computeChanges: false);
        SetCurrentMarker(stmt?.Start, stmt?.Length);
        SyncSelectionToInnermost(frame.Id);
    }

    // The offset span of the block's closing END keyword (the last `end` token of the routine body), or null if
    // it can't be found. The marker lands here at Completed so the user sees execution finished at END.
    private static (int Start, int Length)? EndMarkerOf(BlockStatement? block)
    {
        if (block is null) return null;
        var tokens = block.Tokens;
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            if (string.Equals(tokens[i].Text, "end", StringComparison.OrdinalIgnoreCase))
            {
                return (tokens[i].Start, tokens[i].Length);
            }
        }
        return null;
    }

    // The terminal (Completed) call stack is a single frame — the root the routine ended in; reuse the shared
    // builder with the END line as the position.
    private void RebuildTerminalCallStack(IReadOnlyList<Frame> stack, int innermostOffset)
    {
        RebuildCallStackFrom(stack, innermostOffset);
        RebuildBreadcrumbsFrom(stack);
        if (CallStack.Count > 0) SyncSelectionToInnermost(CallStack[0].FrameId);
    }

    private void SyncSelectionToInnermost(int frameId)
    {
        _syncingFrameSelection = true;
        SelectedFrameRow = CallStack.FirstOrDefault(r => r.FrameId == frameId);
        SelectedBreadcrumbIndex = BreadcrumbIndexForFrame(frameId);
        _syncingFrameSelection = false;
    }

    // Rebuilds the Call Stack rows from the engine's call stack (innermost-first). Each frame shows its
    // routine, its position line (the current statement for the innermost frame; for a caller, the call site
    // of its child — the statement in THIS frame's own source that pushed the frame below it, spec §5.2) and
    // the simulated-frame indicator (a callee reached by Step Into = interpreted, §5.3). Cleared when the
    // session is not paused (no live stack). It reads the session, never drives it; selection is applied by
    // ApplySelectedFrame (the one place source / marker / variables / selection are set together).
    private void RebuildCallStack()
    {
        var session = Session;
        if (session is null || session.State != DebugState.Paused)
        {
            CallStack.Clear();
            OnPropertyChanged(nameof(HasCallStack));
            return;
        }
        RebuildCallStackFrom(session.CallStack, session.CurrentStatement?.Start ?? -1);
    }

    // Builds the Call Stack rows from a stack (innermost-first) + the innermost frame's current-position offset.
    // Each frame shows its routine, its position line (the innermost frame → its current/fault/END offset; a
    // caller → the call site of its child, a statement in THIS frame's own source, spec §5.2) and the
    // simulated-frame indicator (a callee reached by Step Into = interpreted, §5.3). Shared by the live pause and
    // the terminal (Completed / Faulted) snapshots — the only difference is which stack + innermost offset.
    private void RebuildCallStackFrom(IReadOnlyList<Frame> stack, int innermostOffset)
    {
        CallStack.Clear();
        for (int i = 0; i < stack.Count; i++)
        {
            var frame = stack[i];
            bool isCurrent = i == 0;
            int offset = isCurrent ? innermostOffset : (stack[i - 1].CallSite?.Start ?? -1);
            int line = offset >= 0 ? LineOf(frame.Source, offset) : 0;
            string lineText = line > 0
                ? string.Format(CultureInfo.CurrentCulture, UiStrings.DebuggerCallStackLineFormat, line)
                : string.Empty;
            CallStack.Add(new DebugFrameRowViewModel(
                frame.Id, frame.RoutineName, lineText, isCurrent, isSimulated: frame.CallSite is not null));
        }
        OnPropertyChanged(nameof(HasCallStack));
    }

    // Builds the breadcrumb path — the call stack read outermost→innermost (the reverse of CallStack), so it
    // reads left-to-right as the call chain (ROOT › … › current). Mirrors the stack; a follow of the same data.
    private void RebuildBreadcrumbs()
    {
        var session = Session;
        if (session is null || session.State != DebugState.Paused)
        {
            Breadcrumbs.Clear();
            SelectedBreadcrumbIndex = -1;
            return;
        }
        RebuildBreadcrumbsFrom(session.CallStack);
    }

    private void RebuildBreadcrumbsFrom(IReadOnlyList<Frame> stack)
    {
        Breadcrumbs.Clear();
        for (int i = stack.Count - 1; i >= 0; i--) Breadcrumbs.Add(stack[i].RoutineName);
    }

    // ── Frame selection (spec §5.2 — the call stack, breadcrumbs and Ctrl+Alt+Up/Down all route here) ──────

    /// <summary>Ctrl+Alt+Up (delta -1) / Ctrl+Alt+Down (delta +1): moves the frame selection up/down the Call
    /// Stack list (which shows the innermost frame at the top). A no-op with no stack. Sets the selected row,
    /// whose change handler routes to <see cref="SelectFrame"/>.</summary>
    public void MoveFrameSelection(int delta)
    {
        if (CallStack.Count == 0) return;
        int index = SelectedFrameRow is null ? 0 : CallStack.IndexOf(SelectedFrameRow);
        if (index < 0) index = 0;
        int next = Math.Clamp(index + delta, 0, CallStack.Count - 1);
        if (next != index) SelectedFrameRow = CallStack[next];
    }

    // A user picked a frame (call-stack row / breadcrumb / keyboard). Repoints the inspection view to it; a
    // no-op when not paused or already selected. Navigation only — it never touches the session.
    private void SelectFrame(int frameId)
    {
        if (_syncingFrameSelection) return;
        var session = Session;
        if (session is null || session.State != DebugState.Paused) return;
        var frame = FindFrame(session, frameId);
        if (frame is null || ReferenceEquals(frame, _selectedFrame)) return;
        ApplySelectedFrame(frame, computeChanges: false);
    }

    // The ONE place source + current-line marker + Variables + both selection controls are set together, so a
    // frame and everything mirroring it can never disagree. computeChanges is true only on a step pause (the
    // innermost frame) — browsing a caller shows values with no change-highlight and never disturbs the step
    // baseline.
    private void ApplySelectedFrame(Frame frame, bool computeChanges)
    {
        _selectedFrame = frame;
        SelectedFrameId = frame.Id;

        var (offset, length) = FramePosition(frame);
        // Source first (the view sets the editor text synchronously on the property change), then the marker
        // (its offset is in THIS frame's source, so the renderer reads the freshly-set document).
        SourceText = frame.Source ?? _source ?? string.Empty;
        SetCurrentMarker(offset, length);
        ShowFrameVariables(frame, computeChanges);

        _syncingFrameSelection = true;
        SelectedFrameRow = CallStack.FirstOrDefault(r => r.FrameId == frame.Id);
        SelectedBreadcrumbIndex = BreadcrumbIndexForFrame(frame.Id);
        _syncingFrameSelection = false;
    }

    // The frame's current execution position, in its OWN source's coordinate space: the innermost frame → the
    // current step point; a caller → the call site of its child (a statement in this frame). Null when unknown.
    private (int? Offset, int? Length) FramePosition(Frame frame)
    {
        var session = Session;
        if (session is null) return (null, null);
        if (ReferenceEquals(frame, session.CurrentFrame))
        {
            var step = session.CurrentStatement;
            return (step?.Start, step?.Length);
        }
        var stack = session.CallStack; // innermost first
        for (int i = 0; i < stack.Count; i++)
        {
            if (stack[i].Id == frame.Id)
            {
                var callSite = i > 0 ? stack[i - 1].CallSite : null; // the call in THIS frame that pushed its child
                return (callSite?.Start, callSite?.Length);
            }
        }
        return (null, null);
    }

    private int BreadcrumbIndexForFrame(int frameId)
    {
        // CallStack innermost-first → breadcrumb index (outermost→innermost) = Count-1 - callStackIndex.
        for (int i = 0; i < CallStack.Count; i++)
        {
            if (CallStack[i].FrameId == frameId) return CallStack.Count - 1 - i;
        }
        return -1;
    }

    private static Frame? FindFrame(DebugSession session, int frameId)
    {
        foreach (var f in session.CallStack)
        {
            if (f.Id == frameId) return f;
        }
        return null;
    }

    /// <summary>Peek Frame (spec §5): a frame's routine source + the line it is currently at, for an inline
    /// preview (double-click a call-stack row) without changing the inspected frame. Null when not paused /
    /// the frame is gone. Pure read of the session's frames.</summary>
    public DebugFramePeek? GetFramePeek(int frameId)
    {
        var session = Session;
        if (session is null || session.State != DebugState.Paused) return null;
        var frame = FindFrame(session, frameId);
        if (frame is null) return null;
        string source = frame.Source ?? _source ?? string.Empty;
        var (offset, _) = FramePosition(frame);
        int line = offset is { } o ? LineOf(source, o) : 0;
        return new DebugFramePeek(frame.RoutineName, source, line);
    }

    // Shows a frame's variables. The roster (row identity) is rebuilt only when the inspected frame changes;
    // within a frame the rows are updated IN PLACE (so pins / expansion / selection survive). The roster comes
    // from the frame's OWN model (its declared parameters + locals, spec §5.2), the frame holds the live
    // values. computeChanges (the step path, innermost frame) highlights values that changed since the frame's
    // previous step and re-baselines; browsing a caller (computeChanges false) never disturbs the step baseline.
    private void ShowFrameVariables(Frame frame, bool computeChanges)
    {
        var model = frame.Model ?? _model;
        if (model is null)
        {
            ClearVariables();
            return;
        }

        // A different inspected frame → fresh roster. Trigger NEW/OLD context rows belong only to the trigger
        // ROOT frame (a stepped-into stored/local callee has no NEW/OLD in scope — spec §8.1).
        if (frame.Id != _rosterFrameId)
        {
            bool isRootTriggerFrame = IsTriggerMode && _body is not null && ReferenceEquals(frame.Body, _body);
            BuildRoster(model, isRootTriggerFrame);
            _rosterFrameId = frame.Id;
        }

        // Values (and the change baseline) resolve through the frame by ResolveName — the synthetic frame
        // variable for a context row, the plain name for a parameter/local.
        bool haveBaseline = computeChanges && _previousValues is not null && _previousFrameId == frame.Id;
        foreach (var row in Variables)
        {
            bool hasValue = frame.TryResolveValue(row.ResolveName, out var value);
            bool changed = haveBaseline
                && _previousValues!.TryGetValue(row.ResolveName, out var prev)
                && !ValuesEqual(prev, hasValue ? value : null);
            row.Update(hasValue, value, changed);
        }

        if (computeChanges)
        {
            // The step baseline for the NEXT step is this frame's current values.
            _previousValues = new System.Collections.Generic.Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var row in Variables)
            {
                _previousValues[row.ResolveName] = frame.TryResolveValue(row.ResolveName, out var v) ? v : null;
            }
            _previousFrameId = frame.Id;
        }

        RebuildVariableGroups();
    }

    // Rebuilds the flat roster from the frame's semantic model (parameters first in declaration order, then
    // locals) — the model is the roster, the frame holds the live values. A variable present in the frame but
    // declared nowhere in the model is not shown (the declared symbols are the roster).
    private void BuildRoster(SemanticModel model, bool includeContext)
    {
        Variables.Clear();

        // Trigger NEW/OLD context columns first (only the referenced ones, and only those AVAILABLE for the
        // simulated event — spec §8.1). Each resolves through its synthetic frame variable (ResolveName); the
        // display name is NEW.col / OLD.col.
        if (includeContext)
        {
            foreach (var c in _triggerColumns)
            {
                if (c.Record == TriggerRecord.New && _activeTrigger?.NewAvailable != true) continue;
                if (c.Record == TriggerRecord.Old && _activeTrigger?.OldAvailable != true) continue;
                var kind = c.Record == TriggerRecord.New ? DebugVariableKind.ContextNew : DebugVariableKind.ContextOld;
                string display = (c.Record == TriggerRecord.New ? "NEW." : "OLD.") + c.Column;
                string? type = _triggerColumnTypes.TryGetValue(c.Column, out var t) ? t : null;
                Variables.Add(new DebugVariableRowViewModel(display, kind, type, resolveName: c.Synthetic));
            }
        }

        var symbols = model.AllSymbols
            .Where(s => s is ParameterSymbol or VariableSymbol)
            .OrderBy(s => s is ParameterSymbol ? 0 : 1)
            .ThenBy(s => s.DeclarationSpan?.Start ?? int.MaxValue);

        foreach (var s in symbols)
        {
            var kind = s switch
            {
                ParameterSymbol { Direction: ParameterDirection.Output } => DebugVariableKind.ParameterOut,
                ParameterSymbol => DebugVariableKind.ParameterIn,
                _ => DebugVariableKind.Local,
            };
            Variables.Add(new DebugVariableRowViewModel(s.Name, kind, s.DataType));
        }
        OnPropertyChanged(nameof(HasVariables));
    }

    // Places the roster's rows into the Pinned / Parameters / Locals groups, applying the name filter. Reuses
    // the persistent group instances (so IsExpanded survives) and shows only non-empty groups. Presentation
    // only — it reads the existing rows, never the frame.
    private void RebuildVariableGroups()
    {
        _pinnedGroup.Rows.Clear();
        _contextGroup.Rows.Clear();
        _parametersGroup.Rows.Clear();
        _localsGroup.Rows.Clear();

        string filter = VariableFilter?.Trim() ?? string.Empty;
        foreach (var row in Variables)
        {
            if (filter.Length > 0 && row.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var target = row.IsPinned ? _pinnedGroup
                : row.Kind is DebugVariableKind.ContextNew or DebugVariableKind.ContextOld ? _contextGroup
                : row.Kind == DebugVariableKind.Local ? _localsGroup
                : _parametersGroup;
            target.Rows.Add(row);
        }

        SyncGroupVisibility(_pinnedGroup);
        SyncGroupVisibility(_contextGroup);
        SyncGroupVisibility(_parametersGroup);
        SyncGroupVisibility(_localsGroup);
    }

    // Keeps a group in VariableGroups iff it has rows, preserving the fixed order (Pinned, Context, Parameters,
    // Locals).
    private void SyncGroupVisibility(DebugVariableGroupViewModel group)
    {
        bool present = VariableGroups.Contains(group);
        if (group.Rows.Count > 0 && !present)
        {
            int index = OrderIndex(group);
            int at = 0;
            foreach (var g in VariableGroups)
            {
                if (OrderIndex(g) > index) break;
                at++;
            }
            VariableGroups.Insert(at, group);
        }
        else if (group.Rows.Count == 0 && present)
        {
            VariableGroups.Remove(group);
        }
    }

    private int OrderIndex(DebugVariableGroupViewModel group)
        => group == _pinnedGroup ? 0
        : group == _contextGroup ? 1
        : group == _parametersGroup ? 2
        : 3;

    // Pin / unpin a variable to the top group (session-scoped; not a Watch — §9.5).
    [RelayCommand]
    private void TogglePin(DebugVariableRowViewModel? row)
    {
        if (row is null) return;
        row.IsPinned = !row.IsPinned;
        RebuildVariableGroups();
    }

    // ── Inline edit (spec §9.4 — "trivial here: the frame is client-side truth") ────────────────────
    // Setting the value IS trivial (frame.SetResolvedValue); the only real work is parsing the typed text.
    // We validate the shape at edit time (below); the real domain CHECK still surfaces on the next injection
    // (§3.4) — never guessed, never silently coerced.

    [RelayCommand]
    private void BeginEdit(DebugVariableRowViewModel? row)
    {
        if (row is null || !IsPaused || !row.IsEditable) return;
        row.BeginEdit();
    }

    [RelayCommand]
    private static void CancelEdit(DebugVariableRowViewModel? row) => row?.CancelEdit();

    [RelayCommand]
    private void CommitEdit(DebugVariableRowViewModel? row)
    {
        if (row is null || !row.IsEditing) return;
        var frame = Session?.CurrentFrame;
        if (frame is null) { row.CancelEdit(); return; }

        if (!TryParseEditedValue(row.EditText, row.RawValue, row.TypeText, out var value))
        {
            row.HasEditError = true; // stay in edit mode; the shape didn't parse for the declared type
            return;
        }

        // Client-side truth: write it into the frame (the next harness injection re-reads it), then reflect
        // it in the row without marking a step-change, and re-baseline so the next step compares correctly. A
        // trigger context row writes through its synthetic frame variable (ResolveName).
        frame.SetResolvedValue(row.ResolveName, value);
        row.Update(hasValue: true, value, changed: false);
        row.CancelEdit();
        _previousValues ??= new System.Collections.Generic.Dictionary<string, object?>(System.StringComparer.OrdinalIgnoreCase);
        _previousValues[row.ResolveName] = value;
    }

    // Best-effort typed parse of the edited text for the declared type (InvariantCulture, matching the harness
    // literal convention). Prefers the current value's CLR type; otherwise classifies by the type name. An
    // empty box (or "<null>") clears to null. Returns false when the text does not parse for the type — the
    // caller keeps the box open and flags the error rather than injecting a guessed value (§F).
    private static bool TryParseEditedValue(string? text, object? current, string typeText, out object? value)
    {
        value = null;
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0 || string.Equals(text, UiStrings.DebuggerVariableNull, System.StringComparison.OrdinalIgnoreCase))
            return true; // null

        var ci = CultureInfo.InvariantCulture;
        // Prefer the runtime type the frame already holds; fall back to a coarse type-name classification.
        var t = current?.GetType();
        string type = typeText.ToUpperInvariant();

        bool Is(params string[] names) => System.Array.Exists(names, n => type.StartsWith(n, System.StringComparison.Ordinal));

        try
        {
            if (t == typeof(int) || (t is null && Is("INTEGER", "INT")))
            { if (int.TryParse(text, System.Globalization.NumberStyles.Integer, ci, out var i)) { value = i; return true; } return false; }
            if (t == typeof(long) || (t is null && Is("BIGINT")))
            { if (long.TryParse(text, System.Globalization.NumberStyles.Integer, ci, out var l)) { value = l; return true; } return false; }
            if (t == typeof(short) || (t is null && Is("SMALLINT")))
            { if (short.TryParse(text, System.Globalization.NumberStyles.Integer, ci, out var s)) { value = s; return true; } return false; }
            if (t == typeof(decimal) || (t is null && Is("NUMERIC", "DECIMAL", "DECFLOAT")))
            { if (decimal.TryParse(text, System.Globalization.NumberStyles.Number, ci, out var m)) { value = m; return true; } return false; }
            if (t == typeof(double) || (t is null && Is("DOUBLE", "FLOAT")))
            { if (double.TryParse(text, System.Globalization.NumberStyles.Float, ci, out var d)) { value = d; return true; } return false; }
            if (t == typeof(float))
            { if (float.TryParse(text, System.Globalization.NumberStyles.Float, ci, out var f)) { value = f; return true; } return false; }
            if (t == typeof(bool) || (t is null && Is("BOOLEAN")))
            { if (bool.TryParse(text, out var b)) { value = b; return true; } return false; }
            if (t == typeof(System.DateTime) || (t is null && Is("DATE", "TIME", "TIMESTAMP")))
            { if (System.DateTime.TryParse(text, ci, System.Globalization.DateTimeStyles.None, out var dt)) { value = dt; return true; } return false; }
            // CHAR/VARCHAR/TEXT and anything else: take the text verbatim.
            value = text;
            return true;
        }
        catch (System.FormatException) { return false; }
        catch (System.OverflowException) { return false; }
    }

    private void ClearVariables()
    {
        Variables.Clear();
        _pinnedGroup.Rows.Clear();
        _contextGroup.Rows.Clear();
        _parametersGroup.Rows.Clear();
        _localsGroup.Rows.Clear();
        VariableGroups.Clear();
        _previousValues = null;
        _previousFrameId = null;
        _rosterFrameId = null;
        _selectedFrame = null;
        SelectedFrameId = -1;
        CallStack.Clear();
        Breadcrumbs.Clear();
        _syncingFrameSelection = true;
        SelectedFrameRow = null;
        SelectedBreadcrumbIndex = -1;
        _syncingFrameSelection = false;
        OnPropertyChanged(nameof(HasVariables));
        OnPropertyChanged(nameof(HasCallStack));
    }

    // Null-tolerant value equality for change-highlighting (null and DBNull are equivalent "no value").
    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is System.DBNull) a = null;
        if (b is System.DBNull) b = null;
        if (a is null || b is null) return a is null && b is null;
        return a.Equals(b);
    }

    private void SetCurrentMarker(int? start, int? length)
    {
        CurrentStart = start;
        CurrentLength = length;
        DebugMarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    private int LineOf(int offset) => LineOf(_source, offset);

    private static int LineOf(string? src, int offset)
    {
        if (src is null || offset <= 0) return 1;
        int line = 1;
        int end = Math.Min(offset, src.Length);
        for (int i = 0; i < end; i++)
        {
            if (src[i] == '\n') line++;
        }
        return line;
    }

    // The paused-status reason text (D12): the specific reason for THIS pause, enriched from the session where
    // one variable/error makes it clearer. A broken breakpoint condition and a data-breakpoint hit are surfaced
    // by name so the user sees WHY it stopped (a broken condition must never be silent — §F).
    private static string PausedReasonText(DebugSession session)
    {
        if (session.BreakpointConditionError is { } ce)
        {
            return string.Format(CultureInfo.CurrentCulture,
                UiStrings.DebuggerStopReasonConditionErrorFormat, ce.Message ?? ce.ExceptionName ?? "?");
        }
        if (session.StopReason == StopReason.DataBreakpoint && session.DataBreakpointHit is { } hit)
        {
            return string.Format(CultureInfo.CurrentCulture, UiStrings.DebuggerStopReasonDataChangedFormat, hit.Variable);
        }
        return StopReasonText(session.StopReason);
    }

    private static string StopReasonText(StopReason reason) => reason switch
    {
        StopReason.Entry => UiStrings.DebuggerStopReasonEntry,
        StopReason.Breakpoint => UiStrings.DebuggerStopReasonBreakpoint,
        StopReason.Exception => UiStrings.DebuggerStopReasonException,
        StopReason.Suspend => UiStrings.DebuggerStopReasonSuspend,
        StopReason.DataBreakpoint => UiStrings.DebuggerStopReasonDataBreakpoint,
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

/// <summary>The content of a Peek Frame preview (Stage X / D8, spec §5): a frame's routine name, its full
/// source and the 1-based line it is currently executing (0 = unknown).</summary>
public sealed record DebugFramePeek(string RoutineName, string Source, int CurrentLine);
