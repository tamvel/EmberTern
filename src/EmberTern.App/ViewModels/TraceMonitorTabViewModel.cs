using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Export;
using EmberTern.Core.Export;
using EmberTern.Core.Query;
using EmberTern.Core.Trace;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The Activity Monitor workspace tab. Owns a <see cref="FirebirdTraceService"/> and projects
/// its live event stream into ONE sacred chronological grid (<see cref="Rows"/>), with two
/// lenses (Transactions / Statements) that only highlight/scroll/optionally narrow — never
/// reorder. Fed by a UI-thread batch pump (coalesced ~100 ms) so a busy ERP database (100k+
/// events) stays smooth; memory is bounded by the service's ring buffer + this VM's display cap.
/// Chronology, hierarchy (flat depth indentation), and self-hide are first-class.
/// </summary>
public sealed partial class TraceMonitorTabViewModel : ViewModelBase, IAsyncDisposable
{
    /// <summary>Max rows retained in the master list (bounds VM memory alongside the service's
    /// ring buffer). Settable for tests.</summary>
    internal int DisplayCap { get; set; } = 50_000;

    private readonly FirebirdTraceService _service;
    private readonly Func<CancellationToken, Task<IReadOnlyList<long>>>? _resolveSelfIds;

    private readonly List<TraceEventRowViewModel> _all = new();          // master, chronological
    private readonly Queue<TraceEvent> _pending = new();                 // background → UI handoff
    private readonly object _pendingGate = new();
    private DispatcherTimer? _pump;

    /// <summary>Coalesces text-filter keystrokes (~350 ms, same UX as the sidebar filter) so a
    /// full clear+refilter of the ≤50k-row master list doesn't run on every keypress.</summary>
    private const int FilterDebounceMs = 350;
    private DispatcherTimer? _filterDebounce;

    private long? _lastBandTx = long.MinValue;
    private int _bandCounter;
    private Func<TraceEvent, bool>? _lensPredicate; // set when a lens item is selected

    public TraceMonitorTabViewModel(
        FirebirdTraceService service,
        Func<CancellationToken, Task<IReadOnlyList<long>>>? resolveSelfIds = null)
    {
        _service = service;
        _resolveSelfIds = resolveSelfIds;
        _service.EventsReceived += OnServiceEvents;
        _service.StateChanged += OnServiceStateChanged;

        // Conditional grid filter — the SAME shared panel every data grid uses (Add condition ·
        // operators · per-column). AM's grid is materialized (_all rows), so we apply client-side:
        // ApplyGridFilterAsync sets the filter and RebuildRows() re-runs the whole pipeline. Column
        // set is fixed (the trace columns), so SetColumns runs once here.
        GridFilterPanel = new FilterPanelViewModel { ApplyRequested = ApplyGridFilterAsync };
        GridFilterPanel.SetColumns(FilterColumns);
    }

    // ─── Conditional grid filter (shared FilterPanelViewModel over the TraceEvent columns) ───────
    // Fixed column set the "Add condition" rows pick from, mapping to ProjectRow's object?[] cells.
    // A parallel QueryColumn list (same names + CLR types) feeds GridFilterEvaluator so AM reuses the
    // exact filter engine every result grid uses (operators, categories, Firebird semantics).
    internal static readonly IReadOnlyList<GridColumnRef> FilterColumns = new[]
    {
        new GridColumnRef(0, "Time", typeof(DateTime)),
        new GridColumnRef(1, "Event", typeof(string)),
        new GridColumnRef(2, "Operation", typeof(string)),
        new GridColumnRef(3, "Object", typeof(string)),
        new GridColumnRef(4, "Duration", typeof(long)),
        new GridColumnRef(5, "Rows", typeof(long)),
        new GridColumnRef(6, "Reads", typeof(long)),
        new GridColumnRef(7, "Tx", typeof(long)),
        new GridColumnRef(8, "Session", typeof(long)),
        new GridColumnRef(9, "User", typeof(string)),
        new GridColumnRef(10, "Error", typeof(string)),
    };
    private static readonly IReadOnlyList<QueryColumn> FilterQueryColumns =
        FilterColumns.Select(c => new QueryColumn(c.Name, c.ClrType)).ToList();

    public FilterPanelViewModel GridFilterPanel { get; }
    private GridFilter _gridFilter = GridFilter.Empty;

    private Task ApplyGridFilterAsync(GridFilter filter)
    {
        _gridFilter = filter;
        RebuildRows();
        return Task.CompletedTask;
    }

    /// <summary>Project a row into the object?[] cells aligned with <see cref="FilterColumns"/> for
    /// the shared <see cref="GridFilterEvaluator"/>. Derived values reuse the row VM's cached
    /// Operation. Nullable numerics/strings stay null so IS NULL / comparison-vs-NULL work.</summary>
    internal static object?[] ProjectRow(TraceEventRowViewModel r)
    {
        var e = r.Event;
        return new object?[]
        {
            e.StartTime.DateTime,                                        // Time
            e.Kind.ToString(),                                           // Event  (e.g. "Trigger")
            r.Operation == TraceSqlOperation.None                        // Operation (e.g. "UPDATE")
                ? string.Empty : TraceSqlOperationClassifier.Label(r.Operation),
            e.Sql ?? e.ObjectName,                                       // Object
            e.Duration is { } d ? (long)d.TotalMilliseconds : null,      // Duration (ms)
            e.RowsFetched,                                               // Rows
            e.Reads,                                                     // Reads
            e.TransactionId,                                             // Tx
            e.AttachmentId,                                              // Session
            e.UserName,                                                  // User
            e.ErrorText,                                                 // Error
        };
    }

    /// <summary>The filtered chronological display (bound to the grid).</summary>
    public ObservableCollection<TraceEventRowViewModel> Rows { get; } = new();

    public ObservableCollection<TraceTransactionLensItem> TransactionLens { get; } = new();
    public ObservableCollection<TraceFingerprintLensItem> StatementLens { get; } = new();
    public TraceEventDetailViewModel Detail { get; } = new();

    [ObservableProperty] private TraceSessionState _state = TraceSessionState.Stopped;
    [ObservableProperty] private TraceGroupMode _groupMode = TraceGroupMode.None;
    [ObservableProperty] private bool _hideSelfActivity = true;
    [ObservableProperty] private bool _followTail = true;
    [ObservableProperty] private bool _showOnlySelected;

    /// <summary>V1.1 noise reduction: trace built-in + user function calls. OFF by default —
    /// the flood of built-in scalar functions (MOD, BIN_AND, …) is suppressed at the source.
    /// Applies on the NEXT Start (a live session's event mask is fixed).</summary>
    [ObservableProperty] private bool _includeFunctions;

    /// <summary>V1.1 SQL inlining: show captured parameter values inline in the detail SQL
    /// (<c>= ?</c> → <c>= 10036</c>). ON by default — that's what reverse-engineering wants.</summary>
    [ObservableProperty] private bool _showValues = true;

    // ── V1.2 event filter (a flyout, not toolbar buttons) — DISPLAY-level: hide captured rows by
    //    kind + (for statements) operation. Distinct from IncludeFunctions (SOURCE-level capture).
    //    All default true = no filtering. Errors are never hidden by this filter. ──
    [ObservableProperty] private bool _showStatementEvents = true;
    [ObservableProperty] private bool _showProcedureEvents = true;
    [ObservableProperty] private bool _showTriggerEvents = true;
    [ObservableProperty] private bool _showFunctionEvents = true;
    [ObservableProperty] private bool _showOpSelect = true;
    [ObservableProperty] private bool _showOpInsert = true;
    [ObservableProperty] private bool _showOpUpdate = true;
    [ObservableProperty] private bool _showOpDelete = true;
    [ObservableProperty] private bool _showOpExecute = true;
    [ObservableProperty] private bool _showOpDdl = true;
    [ObservableProperty] private bool _showOpOther = true;

    /// <summary>True when any event-type/operation checkbox is unchecked — drives the funnel's
    /// active-filter dot.</summary>
    public bool IsEventFilterActive =>
        !(ShowStatementEvents && ShowProcedureEvents && ShowTriggerEvents && ShowFunctionEvents
          && ShowOpSelect && ShowOpInsert && ShowOpUpdate && ShowOpDelete && ShowOpExecute && ShowOpDdl && ShowOpOther);
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private TraceEventRowViewModel? _selectedRow;
    [ObservableProperty] private object? _selectedLensItem;
    [ObservableProperty] private TraceQuickFilter _quickFilter = TraceQuickFilter.All;

    /// <summary>Display flag for the detail-panel maximize/restore glyph. The view code-behind owns
    /// the actual row sizing (mirrors the SQL editor's results-panel maximize).</summary>
    [ObservableProperty] private bool _isDetailMaximized;

    public bool IsGroupNone => GroupMode == TraceGroupMode.None;
    public bool IsTransactionLens => GroupMode == TraceGroupMode.Transaction;
    public bool IsStatementLens => GroupMode == TraceGroupMode.Statement;
    public bool IsLensOpen => GroupMode != TraceGroupMode.None;

    public bool IsQuickFilterAll => QuickFilter == TraceQuickFilter.All;
    public bool IsQuickFilterErrors => QuickFilter == TraceQuickFilter.Errors;
    public bool IsQuickFilterSlow => QuickFilter == TraceQuickFilter.Slow;

    /// <summary>Empty-state overlay: no session yet / waiting for activity / filter matched nothing.</summary>
    public bool ShowEmptyState => Rows.Count == 0;
    public string EmptyStateText
    {
        get
        {
            if (_all.Count == 0)
                return State is TraceSessionState.Running or TraceSessionState.Paused
                    ? UiStrings.TraceEmptyWaiting
                    : UiStrings.TraceEmptyHint;
            return UiStrings.TraceEmptyNoMatch;
        }
    }

    /// <summary>The active lens's items (transaction or fingerprint). ONE bound collection so the
    /// rail's single SelectedItem can't be clobbered by an inactive list (gotcha #75).</summary>
    public System.Collections.IEnumerable CurrentLens
        => GroupMode == TraceGroupMode.Statement ? StatementLens : TransactionLens;

    public int DisplayedCount => Rows.Count;
    public int TotalCount => _all.Count;
    public long DroppedCount => _service.DroppedCount;

    public string StatusText
    {
        get
        {
            var s = State switch
            {
                TraceSessionState.Running => "Recording",
                TraceSessionState.Paused => "Paused",
                TraceSessionState.Starting => "Starting…",
                TraceSessionState.Stopping => "Stopping…",
                TraceSessionState.Faulted => "Error",
                _ => "Stopped",
            };
            var dropped = DroppedCount > 0 ? $" · {DroppedCount} dropped" : string.Empty;
            return $"{s} · {DisplayedCount}/{TotalCount} events{dropped}";
        }
    }

    // ---- events consumed by the view / owner ----
    public event Action<TraceEventRowViewModel>? ScrollToRowRequested;
    public event Action<string>? DetailSqlChanged;   // push SQL to the read-only editor
    public event Action<string>? OpenInEditorRequested;
    public event Action<string>? CopyToClipboardRequested;   // generic clipboard write (Copy SQL + grid copy)

    // ================================================================ lifecycle

    public bool CanStart => State is TraceSessionState.Stopped or TraceSessionState.Faulted;
    public bool CanStop => State is TraceSessionState.Running or TraceSessionState.Paused;
    public bool CanPauseResume => State is TraceSessionState.Running or TraceSessionState.Paused;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!CanStart) return;
        IReadOnlyList<long> selfIds = Array.Empty<long>();
        if (_resolveSelfIds is not null)
        {
            try { selfIds = await _resolveSelfIds(CancellationToken.None).ConfigureAwait(true); }
            catch { /* self-hide is best-effort */ }
        }
        EnsurePump();
        await _service.StartAsync(BuildSessionConfig(), selfIds).ConfigureAwait(true);
        NotifyCommands();
    }

    /// <summary>The preset the next Start uses: the default (functions off) with the user's
    /// "Include function calls" opt-in applied. Internal so tests can assert the mapping.</summary>
    internal TraceSessionConfig BuildSessionConfig()
        => TraceSessionConfig.DefaultPreset with { IncludeFunctions = IncludeFunctions };

    [RelayCommand(CanExecute = nameof(CanPauseResume))]
    private async Task PauseResumeAsync()
    {
        if (State == TraceSessionState.Running) await _service.PauseAsync().ConfigureAwait(true);
        else if (State == TraceSessionState.Paused) await _service.ResumeAsync().ConfigureAwait(true);
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        await _service.StopAsync().ConfigureAwait(true);
        _pump?.Stop();
        NotifyCommands();
    }

    [RelayCommand]
    private void Clear()
    {
        _all.Clear();
        Rows.Clear();
        TransactionLens.Clear();
        StatementLens.Clear();
        _service.Clear();
        _lastBandTx = long.MinValue;
        _bandCounter = 0;
        SelectedLensItem = null;
        _lensPredicate = null;
        SelectedRow = null;
        RaiseCounts();
    }

    // ================================================================ live pump

    private void OnServiceEvents(object? sender, IReadOnlyList<TraceEvent> events)
    {
        if (events.Count == 0) return;
        lock (_pendingGate)
            foreach (var e in events)
                _pending.Enqueue(e);
    }

    private void OnServiceStateChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { State = _service.State; NotifyCommands(); RaiseCounts(); });

    private void EnsurePump()
    {
        if (_pump is not null) { _pump.Start(); return; }
        _pump = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => DrainPending());
        _pump.Start();
    }

    private void DrainPending()
    {
        List<TraceEvent>? batch = null;
        lock (_pendingGate)
        {
            if (_pending.Count == 0) return;
            batch = new List<TraceEvent>(_pending.Count);
            while (_pending.Count > 0) batch.Add(_pending.Dequeue());
        }
        Ingest(batch);
    }

    // ================================================================ testable core

    /// <summary>Appends a batch of events to the master list + filtered display, assigns the
    /// operation band, trims to the cap, and refreshes the lens. The batch-pump entry point;
    /// tests call it directly.</summary>
    internal void Ingest(IReadOnlyList<TraceEvent> batch)
    {
        foreach (var e in batch)
        {
            bool newTx = e.TransactionId != _lastBandTx;
            if (newTx) { _bandCounter++; _lastBandTx = e.TransactionId; }
            var row = new TraceEventRowViewModel(e, "TxBand" + (_bandCounter & 1)) { IsTransactionStart = newTx };
            _all.Add(row);
            if (RowPasses(row))
            {
                row.IsHighlighted = _lensPredicate?.Invoke(e) ?? false;
                Rows.Add(row);
            }
        }
        TrimToCap();
        if (GroupMode != TraceGroupMode.None) RecomputeLenses();
        RaiseCounts();
        if (FollowTail && Rows.Count > 0) ScrollToRowRequested?.Invoke(Rows[^1]);
    }

    private void TrimToCap()
    {
        while (_all.Count > DisplayCap)
        {
            var removed = _all[0];
            _all.RemoveAt(0);
            if (Rows.Count > 0 && ReferenceEquals(Rows[0], removed)) Rows.RemoveAt(0);
        }
    }

    // hide-self + free-text search + quick chip + show-only-selected + event-kind funnel.
    // NOTE the deliberate asymmetry: the STRUCTURED filters (quick chip / event-kind funnel) never
    // hide errors (safety — you can't lose an error by filtering by kind), but the FREE-TEXT box IS
    // applied to errors: an explicit text search is user intent to find specific text (incl. error
    // messages), so a non-matching error is hidden when the search box is non-empty.
    internal bool RowPasses(TraceEventRowViewModel r)
    {
        if (HideSelfActivity && r.IsSelfActivity) return false;
        if (QuickFilter == TraceQuickFilter.Errors && !r.IsError) return false;
        if (QuickFilter == TraceQuickFilter.Slow && !r.IsSlow) return false;
        if (FilterText.Length > 0 && !MatchesFilter(r.Event, FilterText)) return false;
        // Conditional grid filter ("Add condition") — an explicit user query, so (like the free-text
        // search) it applies to ALL rows including errors. Reuses the shared GridFilterEvaluator.
        if (!_gridFilter.IsEmpty && !GridFilterEvaluator.Matches(ProjectRow(r), _gridFilter, FilterQueryColumns)) return false;
        if (ShowOnlySelected && _lensPredicate is { } pred && !pred(r.Event)) return false;
        if (!r.IsError && !EventKindPasses(r)) return false; // event-type/operation flyout (errors always shown)
        return true;
    }

    private bool EventKindPasses(TraceEventRowViewModel r) => r.Event.Kind switch
    {
        TraceEventKind.Statement => ShowStatementEvents && OperationPasses(r.Operation),
        TraceEventKind.Procedure => ShowProcedureEvents,
        TraceEventKind.Trigger => ShowTriggerEvents,
        TraceEventKind.Function => ShowFunctionEvents,
        _ => true, // System / Connection / Transaction are not part of the flyout
    };

    private bool OperationPasses(TraceSqlOperation op) => op switch
    {
        TraceSqlOperation.Select => ShowOpSelect,
        TraceSqlOperation.Insert => ShowOpInsert,
        TraceSqlOperation.Update => ShowOpUpdate,
        TraceSqlOperation.Delete => ShowOpDelete,
        TraceSqlOperation.Execute => ShowOpExecute,
        TraceSqlOperation.Ddl => ShowOpDdl,
        TraceSqlOperation.Merge or TraceSqlOperation.Other => ShowOpOther,
        _ => true, // None (unclassifiable) is never hidden
    };

    // Any event-filter checkbox change re-filters the grid + refreshes the active-filter dot.
    partial void OnShowStatementEventsChanged(bool value) => OnEventFilterChanged();
    partial void OnShowProcedureEventsChanged(bool value) => OnEventFilterChanged();
    partial void OnShowTriggerEventsChanged(bool value) => OnEventFilterChanged();
    partial void OnShowFunctionEventsChanged(bool value) => OnEventFilterChanged();
    partial void OnShowOpSelectChanged(bool value) => OnEventFilterChanged();
    partial void OnShowOpInsertChanged(bool value) => OnEventFilterChanged();
    partial void OnShowOpUpdateChanged(bool value) => OnEventFilterChanged();
    partial void OnShowOpDeleteChanged(bool value) => OnEventFilterChanged();
    partial void OnShowOpExecuteChanged(bool value) => OnEventFilterChanged();
    partial void OnShowOpDdlChanged(bool value) => OnEventFilterChanged();
    partial void OnShowOpOtherChanged(bool value) => OnEventFilterChanged();

    private void OnEventFilterChanged()
    {
        OnPropertyChanged(nameof(IsEventFilterActive));
        RebuildRows();
    }

    [RelayCommand]
    private void ResetEventFilter()
    {
        ShowStatementEvents = ShowProcedureEvents = ShowTriggerEvents = ShowFunctionEvents = true;
        ShowOpSelect = ShowOpInsert = ShowOpUpdate = ShowOpDelete = ShowOpExecute = ShowOpDdl = ShowOpOther = true;
    }

    /// <summary>Free-text match over every field a user can see or reasonably search — full SQL,
    /// object/routine name, operation label, error message, session identity (user / role / process
    /// / host), and the numeric ids (transaction / attachment / process). Case-insensitive for text;
    /// numeric ids match by their invariant string. Pure — unit-tested.</summary>
    internal static bool MatchesFilter(TraceEvent e, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        return ContainsText(e.Sql, filter)
            || ContainsText(e.ObjectName, filter)
            || ContainsText(TraceEventRowViewModel.DisplayLabelFor(e), filter)
            || ContainsText(e.ErrorText, filter)
            || ContainsText(e.UserName, filter)
            || ContainsText(e.RoleName, filter)
            || ContainsText(e.ProcessName, filter)
            || ContainsText(e.RemoteAddress, filter)
            || ContainsNum(e.TransactionId, filter)
            || ContainsNum(e.AttachmentId, filter)
            || ContainsNum(e.ClientProcessId, filter);
    }

    private static bool ContainsText(string? s, string filter) =>
        s is not null && s.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsNum(long? n, string filter) =>
        n is { } v && v.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal);

    private static bool ContainsNum(int? n, string filter) =>
        n is { } v && v.ToString(CultureInfo.InvariantCulture).Contains(filter, StringComparison.Ordinal);

    internal void RebuildRows()
    {
        Rows.Clear();
        foreach (var r in _all)
            if (RowPasses(r)) Rows.Add(r);
        RaiseCounts();
    }

    internal void RecomputeLenses()
    {
        var events = _all
            .Where(r => !(HideSelfActivity && r.IsSelfActivity))
            .Select(r => r.Event);

        if (GroupMode == TraceGroupMode.Transaction)
        {
            TransactionLens.Clear();
            foreach (var g in TraceEventGrouper.GroupByTransaction(events))
                TransactionLens.Add(new TraceTransactionLensItem(g));
        }
        else if (GroupMode == TraceGroupMode.Statement)
        {
            StatementLens.Clear();
            foreach (var g in TraceEventGrouper.GroupByFingerprint(events))
                StatementLens.Add(new TraceFingerprintLensItem(g));
        }
    }

    private void ApplyHighlight()
    {
        foreach (var r in _all)
            r.IsHighlighted = _lensPredicate?.Invoke(r.Event) ?? false;
    }

    // ================================================================ property reactions

    partial void OnSelectedRowChanged(TraceEventRowViewModel? value)
    {
        if (value is null) Detail.Clear();
        else Detail.Update(value.Event, ShowValues);
        DetailSqlChanged?.Invoke(Detail.Sql); // cleaned + (when on) value-inlined, for the read-only editor
    }

    // Re-render the selected event's SQL with/without inlined parameter values.
    partial void OnShowValuesChanged(bool value)
    {
        if (SelectedRow is not { } row) return;
        Detail.Update(row.Event, value);
        DetailSqlChanged?.Invoke(Detail.Sql);
    }

    partial void OnSelectedLensItemChanged(object? value)
    {
        _lensPredicate = value switch
        {
            TraceTransactionLensItem tx => e => e.TransactionId == tx.TransactionId,
            TraceFingerprintLensItem fp => e => TraceStatementFingerprinter.Fingerprint(e.Sql) == fp.Fingerprint,
            _ => null,
        };
        ApplyHighlight();
        if (ShowOnlySelected) RebuildRows();

        var first = Rows.FirstOrDefault(r => _lensPredicate?.Invoke(r.Event) ?? false);
        if (first is not null) { SelectedRow = first; ScrollToRowRequested?.Invoke(first); }
    }

    partial void OnShowOnlySelectedChanged(bool value) => RebuildRows();
    partial void OnFilterTextChanged(string value) => ScheduleFilter();

    // Debounced re-filter — restart a one-shot ~350 ms timer on each keystroke, refilter when it settles.
    private void ScheduleFilter()
    {
        _filterDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FilterDebounceMs) };
        _filterDebounce.Tick -= OnFilterDebounceTick;
        _filterDebounce.Tick += OnFilterDebounceTick;
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }

    private void OnFilterDebounceTick(object? sender, EventArgs e)
    {
        _filterDebounce?.Stop();
        RebuildRows();
    }

    partial void OnQuickFilterChanged(TraceQuickFilter value)
    {
        OnPropertyChanged(nameof(IsQuickFilterAll));
        OnPropertyChanged(nameof(IsQuickFilterErrors));
        OnPropertyChanged(nameof(IsQuickFilterSlow));
        RebuildRows();
    }

    // Follow-tail: enabling it jumps to the newest row; the view auto-pauses it on manual scroll-up.
    partial void OnFollowTailChanged(bool value)
    {
        if (value && Rows.Count > 0) ScrollToRowRequested?.Invoke(Rows[^1]);
    }

    partial void OnHideSelfActivityChanged(bool value)
    {
        RebuildRows();
        if (GroupMode != TraceGroupMode.None) RecomputeLenses();
    }

    partial void OnGroupModeChanged(TraceGroupMode value)
    {
        OnPropertyChanged(nameof(IsGroupNone));
        OnPropertyChanged(nameof(IsTransactionLens));
        OnPropertyChanged(nameof(IsStatementLens));
        OnPropertyChanged(nameof(IsLensOpen));
        OnPropertyChanged(nameof(CurrentLens));
        SelectedLensItem = null;
        _lensPredicate = null;
        ApplyHighlight();
        if (ShowOnlySelected) { ShowOnlySelected = false; } // narrowing has no target once the lens closes
        if (value != TraceGroupMode.None) RecomputeLenses();
    }

    partial void OnStateChanged(TraceSessionState value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(EmptyStateText));
        NotifyCommands();
    }

    // ---- group-mode toggles (bound to the session-bar segmented control) ----
    [RelayCommand] private void ShowChronological() => GroupMode = TraceGroupMode.None;
    [RelayCommand] private void ShowTransactions() => GroupMode = TraceGroupMode.Transaction;
    [RelayCommand] private void ShowStatements() => GroupMode = TraceGroupMode.Statement;

    // ---- quick filter chips (All / Errors / Slow) ----
    [RelayCommand] private void ShowAll() => QuickFilter = TraceQuickFilter.All;
    [RelayCommand] private void ShowErrors() => QuickFilter = TraceQuickFilter.Errors;
    [RelayCommand] private void ShowSlow() => QuickFilter = TraceQuickFilter.Slow;

    // Re-arm follow-tail and jump to the newest row (the "↓ Latest" affordance).
    [RelayCommand]
    private void JumpToLatest()
    {
        if (!FollowTail) FollowTail = true;                 // OnFollowTailChanged scrolls
        else if (Rows.Count > 0) ScrollToRowRequested?.Invoke(Rows[^1]);
    }

    // ---- detail bridges ----
    [RelayCommand] private void CopySql() { if (Detail.HasSql) CopyToClipboardRequested?.Invoke(Detail.Sql); }
    [RelayCommand] private void OpenInEditor() { if (Detail.HasSql) OpenInEditorRequested?.Invoke(Detail.Sql); }

    // ---- grid copy (context menu) — reuses the shared clipboard channel, no new mechanism ----

    /// <summary>Copies one grid cell (by column header) to the clipboard.</summary>
    public void CopyCell(TraceEventRowViewModel? r, string? header)
    {
        if (r is null) return;
        CopyToClipboardRequested?.Invoke(CellText(r, header));
    }

    /// <summary>Copies the row as tab-separated values (matches the other grids' TSV copy).</summary>
    public void CopyRow(TraceEventRowViewModel? r)
    {
        if (r is null) return;
        CopyToClipboardRequested?.Invoke(RowTsv(r));
    }

    /// <summary>Copies the row with a leading header line — the Excel/Teams/e-mail paste target.</summary>
    public void CopyRowWithHeaders(TraceEventRowViewModel? r)
    {
        if (r is null) return;
        CopyToClipboardRequested?.Invoke(HeaderTsv() + "\n" + RowTsv(r));
    }

    /// <summary>Copies every currently-displayed (filtered) row with a header line — the bulk export
    /// for a bug report / spreadsheet.</summary>
    public void CopyAllWithHeaders()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(HeaderTsv());
        foreach (var r in Rows) sb.Append('\n').Append(RowTsv(r));
        CopyToClipboardRequested?.Invoke(sb.ToString());
    }

    /// <summary>Copies the row's SQL — cleaned + value-inlined per <see cref="ShowValues"/> for a
    /// statement, else the routine name (multi-line preserved).</summary>
    public void CopyRowSql(TraceEventRowViewModel? r)
    {
        if (r is null) return;
        var sql = RowSql(r.Event);
        if (sql.Length > 0) CopyToClipboardRequested?.Invoke(sql);
    }

    public bool CanExport => _all.Count > 0;

    /// <summary>Builds the shared-framework export source for the trace grid (materialized ring
    /// buffer, no re-fetch). Reuses the exact column set + row projection the conditional filter uses
    /// — the full field set (incl. Session / User / Error), richer than the visible grid columns.
    /// <see cref="ExportScope.CurrentView"/> = the filtered display; <see cref="ExportScope.AllRows"/>
    /// = the whole buffer; <see cref="ExportScope.SelectedRows"/> = the selected row (if any).</summary>
    public IExportDataSource? BuildExportSource()
    {
        if (_all.Count == 0) return null;
        var columns = FilterColumns.Select(c => new ExportColumn(c.Name, c.ClrType)).ToList();
        IReadOnlyList<object?[]> currentView = Rows.Select(ProjectRow).ToList();
        IReadOnlyList<object?[]> allRows = _all.Select(ProjectRow).ToList();
        IReadOnlyList<object?[]>? selected = SelectedRow is { } row ? new[] { ProjectRow(row) } : null;
        return new RowBufferExportSource(columns, currentView, allRows, selected, "activity_monitor");
    }

    // Header + row share the same column set as the grid (gutter excluded) so a paste lines up.
    private static string HeaderTsv() => string.Join('\t',
        UiStrings.TraceColSeq, UiStrings.TraceColTime, UiStrings.TraceColEvent, UiStrings.TraceColDuration,
        UiStrings.TraceColObject, UiStrings.TraceColRows, UiStrings.TraceColReads, UiStrings.TraceColTx);

    private string RowTsv(TraceEventRowViewModel r)
    {
        var e = r.Event;
        return string.Join('\t',
            r.Sequence.ToString(CultureInfo.InvariantCulture),
            r.TimeText, r.KindLabel, r.DurationText,
            Flatten(ObjectCell(r)), r.RowsText, r.ReadsText,
            e.TransactionId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
    }

    // The Object column as shown in the grid: error message for errors, else SQL / routine name.
    private string ObjectCell(TraceEventRowViewModel r)
        => r.IsError ? r.ObjectText : RowSql(r.Event);

    private string RowSql(TraceEvent e)
    {
        if (e.Kind != TraceEventKind.Statement)
            return e.ObjectName ?? string.Empty;
        var clean = TraceEventRowViewModel.CleanSql(e.Sql);
        return ShowValues && e.Parameters.Count > 0 ? TraceSqlInliner.Inline(clean, e.Parameters) : clean;
    }

    private string CellText(TraceEventRowViewModel r, string? header)
    {
        var e = r.Event;
        if (header == UiStrings.TraceColSeq) return r.Sequence.ToString(CultureInfo.InvariantCulture);
        if (header == UiStrings.TraceColTime) return r.TimeText;
        if (header == UiStrings.TraceColEvent) return r.KindLabel;
        if (header == UiStrings.TraceColDuration) return r.DurationText;
        if (header == UiStrings.TraceColRows) return r.RowsText;
        if (header == UiStrings.TraceColReads) return r.ReadsText;
        if (header == UiStrings.TraceColTx) return e.TransactionId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return Flatten(ObjectCell(r)); // Object column (or the gutter) → what the grid shows there
    }

    private static string Flatten(string s)
        => s.Replace("\r", " ").Replace("\n", " ").Trim();

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(DisplayedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(DroppedCount));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateText));
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanPauseResume));
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        PauseResumeCommand.NotifyCanExecuteChanged();
    }

    public async ValueTask DisposeAsync()
    {
        _pump?.Stop();
        _filterDebounce?.Stop();
        _service.EventsReceived -= OnServiceEvents;
        _service.StateChanged -= OnServiceStateChanged;
        await _service.DisposeAsync().ConfigureAwait(false);
    }
}
