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
using EmberTern.Core.Diagnostics;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>One auto-refresh interval option for the toolbar combo (0 = off).</summary>
public sealed record SessionRefreshOption(string Label, int Seconds);

/// <summary>Sessions-grid quick filter driven by the Health-Bar counter chips.</summary>
public enum SessionQuickFilter { All, GcRisk, LongTx, Heavy }

/// <summary>
/// The Session Manager workspace tab (the Diagnostics Center → Sessions/Transactions). Owns a
/// <see cref="FirebirdSessionReader"/>, polls MON$ on a <see cref="DispatcherTimer"/> (each poll a
/// fresh snapshot), runs the <see cref="SessionHealthAnalyzer"/>, and projects the result into the
/// Health Bar + Sessions grid + Transactions / Warnings tabs. Diagnose-first: the verdict + counters
/// answer at the top; selecting a session filters the Transactions tab (one selection model).
/// Mirrors the Activity Monitor shell + conventions; reuses ConfirmDialog for Cancel/Disconnect.
/// </summary>
public sealed partial class SessionManagerTabViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly FirebirdSessionReader _reader;

    private readonly List<SessionRowViewModel> _allSessions = new();
    private readonly List<TransactionRowViewModel> _allTransactions = new();
    private readonly Dictionary<long, string> _sessionLabels = new();

    private DispatcherTimer? _timer;
    private CancellationTokenSource? _cts;
    private bool _refreshing;
    private bool _disposed;
    private long? _selectedAttachmentId; // preserve selection across polls

    public SessionManagerTabViewModel(FirebirdSessionReader reader)
    {
        _reader = reader;
        SelectedRefreshOption = RefreshOptions[1]; // 5s default
        StartTimer();
        _ = RefreshCoreAsync();
    }

    // --- bound collections ---
    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();
    public ObservableCollection<TransactionRowViewModel> Transactions { get; } = new();
    public ObservableCollection<SessionWarningViewModel> Warnings { get; } = new();

    public IReadOnlyList<SessionRefreshOption> RefreshOptions { get; } = new[]
    {
        new SessionRefreshOption("Off", 0),
        new SessionRefreshOption("3s", 3),
        new SessionRefreshOption("5s", 5),
        new SessionRefreshOption("10s", 10),
        new SessionRefreshOption("30s", 30),
    };

    [ObservableProperty] private SessionRefreshOption? _selectedRefreshOption;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelStatementCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    private SessionRowViewModel? _selectedSession;

    [ObservableProperty] private TransactionRowViewModel? _selectedTransaction;
    [ObservableProperty] private bool _hideSelfActivity = true;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private SessionQuickFilter _quickFilter = SessionQuickFilter.All;

    /// <summary>Display flag for the bottom-panel maximize glyph (view code-behind owns the sizing).</summary>
    [ObservableProperty] private bool _isDetailMaximized;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _showPrivilegeBanner;

    // --- verdict (Health-Bar dot) ---
    [ObservableProperty] private string _gradeText = "—";
    [ObservableProperty] private string _gradeBrushKey = "SubtleForegroundBrush";
    [ObservableProperty] private string _headline = string.Empty;

    // --- Health-Bar counters ---
    [ObservableProperty] private int _sessionCount;
    [ObservableProperty] private int _transactionCount;
    [ObservableProperty] private int _longTransactionCount;
    [ObservableProperty] private int _gcRiskCount;
    [ObservableProperty] private int _heavyCount;
    [ObservableProperty] private string _oldestActiveLagText = "0";

    public bool IsFilterAll => QuickFilter == SessionQuickFilter.All;
    public bool IsFilterGcRisk => QuickFilter == SessionQuickFilter.GcRisk;
    public bool IsFilterLongTx => QuickFilter == SessionQuickFilter.LongTx;
    public bool IsFilterHeavy => QuickFilter == SessionQuickFilter.Heavy;

    public bool ShowSessionsEmpty => Sessions.Count == 0;
    public bool HasWarnings => Warnings.Count > 0;

    public bool IsSessionFilterActive => SelectedSession is not null;
    public string SessionFilterText => SelectedSession is { } s
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.SessionManagerTransactionsFilteredFormat, s.AttachmentId)
        : string.Empty;

    // Wired by MainWindowViewModel to the shared ConfirmDialog + clipboard channel.
    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;
    public event Action<string>? CopyToClipboardRequested;

    // --- polling lifecycle ---
    private void StartTimer()
    {
        _timer?.Stop();
        var seconds = SelectedRefreshOption?.Seconds ?? 0;
        if (seconds <= 0)
        {
            _timer = null;
            return;
        }
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _timer.Tick += (_, _) => _ = RefreshCoreAsync();
        _timer.Start();
    }

    partial void OnSelectedRefreshOptionChanged(SessionRefreshOption? value) => StartTimer();

    partial void OnHideSelfActivityChanged(bool value) => ApplyFilters();
    partial void OnFilterTextChanged(string value) => ApplyFilters();

    partial void OnQuickFilterChanged(SessionQuickFilter value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterGcRisk));
        OnPropertyChanged(nameof(IsFilterLongTx));
        OnPropertyChanged(nameof(IsFilterHeavy));
        ApplyFilters();
    }

    public bool HasSelectedSession => SelectedSession is not null;

    partial void OnSelectedSessionChanged(SessionRowViewModel? value)
    {
        _selectedAttachmentId = value?.AttachmentId;
        OnPropertyChanged(nameof(HasSelectedSession));
        OnPropertyChanged(nameof(IsSessionFilterActive));
        OnPropertyChanged(nameof(SessionFilterText));
        ApplyTransactionFilter();
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshCoreAsync().ConfigureAwait(true);

    [RelayCommand]
    private void FilterAll() => QuickFilter = SessionQuickFilter.All;
    [RelayCommand]
    private void FilterGcRisk() => QuickFilter = QuickFilter == SessionQuickFilter.GcRisk ? SessionQuickFilter.All : SessionQuickFilter.GcRisk;
    [RelayCommand]
    private void FilterLongTx() => QuickFilter = QuickFilter == SessionQuickFilter.LongTx ? SessionQuickFilter.All : SessionQuickFilter.LongTx;
    [RelayCommand]
    private void FilterHeavy() => QuickFilter = QuickFilter == SessionQuickFilter.Heavy ? SessionQuickFilter.All : SessionQuickFilter.Heavy;

    [RelayCommand]
    private void ClearSessionFilter() => SelectedSession = null;

    private bool CanCancelStatement => SelectedSession is { ActiveStatementId: not null };

    [RelayCommand(CanExecute = nameof(CanCancelStatement))]
    private async Task CancelStatement()
    {
        if (SelectedSession is not { ActiveStatementId: { } statementId } session) return;
        var ok = await ConfirmAsync(
            UiStrings.SessionManagerCancelStatementConfirmTitle,
            string.Format(CultureInfo.CurrentCulture, UiStrings.SessionManagerCancelStatementConfirmFormat, session.AttachmentId),
            UiStrings.SessionManagerCancelStatementConfirmYes).ConfigureAwait(true);
        if (!ok) return;

        try
        {
            var error = await _reader.CancelStatementAsync(statementId).ConfigureAwait(true);
            StatusText = error ?? string.Format(CultureInfo.CurrentCulture, UiStrings.SessionManagerCancelStatementDone, session.AttachmentId);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        await RefreshCoreAsync().ConfigureAwait(true);
    }

    private bool CanDisconnect => SelectedSession is { IsSelf: false };

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task Disconnect()
    {
        if (SelectedSession is not { } session || session.IsSelf) return;
        var ok = await ConfirmAsync(
            UiStrings.SessionManagerDisconnectConfirmTitle,
            string.Format(CultureInfo.CurrentCulture, UiStrings.SessionManagerDisconnectConfirmFormat, session.AttachmentId, session.User),
            UiStrings.SessionManagerDisconnectConfirmYes,
            destructive: true).ConfigureAwait(true);
        if (!ok) return;

        try
        {
            var error = await _reader.DisconnectSessionAsync(session.AttachmentId).ConfigureAwait(true);
            StatusText = error ?? string.Format(CultureInfo.CurrentCulture, UiStrings.SessionManagerDisconnectDone, session.AttachmentId);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        await RefreshCoreAsync().ConfigureAwait(true);
    }

    private bool CanCopy => SelectedSession is not null;

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void Copy()
    {
        if (SelectedSession is not { } s) return;
        CopyToClipboardRequested?.Invoke(UiStrings.SessionManagerCopyHeaders + "\n" + s.ToTsv());
    }

    // --- refresh core ---
    private async Task RefreshCoreAsync()
    {
        if (_disposed || _refreshing) return;
        _refreshing = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            var own = await _reader.GetOwnAttachmentIdsAsync(ct).ConfigureAwait(true);
            var sessions = await _reader.ReadSessionsAsync(own, ct).ConfigureAwait(true);
            var transactions = await _reader.ReadTransactionsAsync(ct).ConfigureAwait(true);
            var database = await _reader.ReadDatabaseStateAsync(ct).ConfigureAwait(true);
            if (_disposed) return;

            var report = SessionHealthAnalyzer.Analyze(sessions, transactions, database, DateTime.Now);
            BuildFromReport(sessions, transactions, report, DateTime.Now);
            StatusText = string.Format(CultureInfo.CurrentCulture, UiStrings.SessionManagerLastRefreshFormat, DateTime.Now);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer refresh / disposal — ignore
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void BuildFromReport(
        IReadOnlyList<SessionInfo> sessions,
        IReadOnlyList<TransactionInfo> transactions,
        SessionHealthReport report,
        DateTime referenceTime)
    {
        // masters
        _allSessions.Clear();
        _sessionLabels.Clear();
        foreach (var s in sessions)
        {
            _allSessions.Add(new SessionRowViewModel(s, report.EntryFor(s.AttachmentId)));
            var name = string.IsNullOrWhiteSpace(s.ApplicationName) ? s.User : s.ApplicationName;
            _sessionLabels[s.AttachmentId] = $"{s.AttachmentId} · {name}";
        }

        _allTransactions.Clear();
        foreach (var t in transactions)
        {
            var label = _sessionLabels.TryGetValue(t.AttachmentId, out var l) ? l : t.AttachmentId.ToString(CultureInfo.InvariantCulture);
            _allTransactions.Add(new TransactionRowViewModel(t, report.EntryForTransaction(t.TransactionId), label, referenceTime));
        }

        // verdict + counters
        GradeText = report.Verdict.Grade switch
        {
            HealthGrade.Healthy => UiStrings.SessionManagerGradeHealthy,
            HealthGrade.Watch => UiStrings.SessionManagerGradeWatch,
            _ => UiStrings.SessionManagerGradeAtRisk,
        };
        GradeBrushKey = report.Verdict.Grade switch
        {
            HealthGrade.Healthy => "SuccessIconBrush",
            HealthGrade.Watch => "WarningBrush",
            _ => "DangerIconBrush",
        };
        Headline = report.Verdict.Headline;
        SessionCount = report.Counters.Sessions;
        TransactionCount = report.Counters.Transactions;
        LongTransactionCount = report.Counters.LongTransactions;
        GcRiskCount = report.Counters.GcRisks;
        HeavyCount = report.Counters.HeavyUsers;
        OldestActiveLagText = report.Counters.OldestActiveLag.ToString("N0", CultureInfo.CurrentCulture);

        // warnings
        Warnings.Clear();
        foreach (var f in report.Findings)
        {
            Warnings.Add(new SessionWarningViewModel(f));
        }
        OnPropertyChanged(nameof(HasWarnings));

        ShowPrivilegeBanner = _allSessions.Count > 0 && _allSessions.All(s => s.IsSelf);

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var text = FilterText?.Trim().ToLowerInvariant() ?? string.Empty;
        IEnumerable<SessionRowViewModel> query = _allSessions;
        if (HideSelfActivity)
        {
            query = query.Where(s => !s.IsSelf);
        }
        query = QuickFilter switch
        {
            SessionQuickFilter.GcRisk => query.Where(s => s.Risk == SessionRisk.GcBlocker),
            SessionQuickFilter.LongTx => query.Where(s => s.Risk == SessionRisk.LongTransaction),
            SessionQuickFilter.Heavy => query.Where(s => s.IsHeavy),
            _ => query,
        };
        if (text.Length > 0)
        {
            query = query.Where(s => s.FilterKey.Contains(text));
        }

        Sessions.Clear();
        foreach (var s in query)
        {
            Sessions.Add(s);
        }

        // restore selection by id
        var restored = _selectedAttachmentId is { } id ? Sessions.FirstOrDefault(s => s.AttachmentId == id) : null;
        if (!ReferenceEquals(restored, SelectedSession))
        {
            SelectedSession = restored;
        }

        OnPropertyChanged(nameof(ShowSessionsEmpty));
        ApplyTransactionFilter();
    }

    private void ApplyTransactionFilter()
    {
        IEnumerable<TransactionRowViewModel> query = _allTransactions;
        if (HideSelfActivity)
        {
            var selfIds = _allSessions.Where(s => s.IsSelf).Select(s => s.AttachmentId).ToHashSet();
            query = query.Where(t => !selfIds.Contains(t.AttachmentId));
        }
        if (SelectedSession is { } s)
        {
            query = query.Where(t => t.AttachmentId == s.AttachmentId);
        }

        Transactions.Clear();
        foreach (var t in query)
        {
            Transactions.Add(t);
        }
    }

    private Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool destructive = false)
        => ConfirmationRequested?.Invoke(new ConfirmRequest
        {
            Title = title,
            Message = message,
            ConfirmLabel = confirmLabel,
            IsDestructive = destructive,
        }) ?? Task.FromResult(true);

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _timer?.Stop();
        _timer = null;
        _cts?.Cancel();
        _cts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
