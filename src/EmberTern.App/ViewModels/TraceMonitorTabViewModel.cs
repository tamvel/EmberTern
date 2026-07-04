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
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private TraceEventRowViewModel? _selectedRow;
    [ObservableProperty] private object? _selectedLensItem;
    [ObservableProperty] private TraceQuickFilter _quickFilter = TraceQuickFilter.All;

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
    public event Action<string>? CopySqlRequested;

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
        await _service.StartAsync(TraceSessionConfig.DefaultPreset, selfIds).ConfigureAwait(true);
        NotifyCommands();
    }

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

    // hide-self + text filter (errors are never hidden by the quick filter) + show-only-selected
    internal bool RowPasses(TraceEventRowViewModel r)
    {
        if (HideSelfActivity && r.IsSelfActivity) return false;
        if (QuickFilter == TraceQuickFilter.Errors && !r.IsError) return false;
        if (QuickFilter == TraceQuickFilter.Slow && !r.IsSlow) return false;
        if (!r.IsError && FilterText.Length > 0 && !MatchesText(r)) return false;
        if (ShowOnlySelected && _lensPredicate is { } pred && !pred(r.Event)) return false;
        return true;
    }

    private bool MatchesText(TraceEventRowViewModel r)
    {
        var f = FilterText;
        return r.ObjectText.Contains(f, StringComparison.OrdinalIgnoreCase)
            || r.KindLabel.Contains(f, StringComparison.OrdinalIgnoreCase)
            || (r.TransactionId?.ToString(CultureInfo.InvariantCulture).Contains(f, StringComparison.Ordinal) ?? false);
    }

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
        else Detail.Update(value.Event);
        DetailSqlChanged?.Invoke(Detail.Sql); // cleaned (separators stripped) for the read-only editor
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
    partial void OnFilterTextChanged(string value) => RebuildRows();

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
    [RelayCommand] private void CopySql() { if (Detail.HasSql) CopySqlRequested?.Invoke(Detail.Sql); }
    [RelayCommand] private void OpenInEditor() { if (Detail.HasSql) OpenInEditorRequested?.Invoke(Detail.Sql); }

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
        _service.EventsReceived -= OnServiceEvents;
        _service.StateChanged -= OnServiceStateChanged;
        await _service.DisposeAsync().ConfigureAwait(false);
    }
}
