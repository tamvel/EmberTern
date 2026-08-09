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
public enum SessionQuickFilter { All, GcRisk, LongTx }

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

    private const double GapBarWidth = 280;

    private readonly List<SessionRowViewModel> _allSessions = new();
    private readonly List<TransactionRowViewModel> _allTransactions = new();
    private readonly Dictionary<long, string> _sessionLabels = new();
    private SessionHealthReport? _report; // last analysis, for scoped Session-Details rebuilds

    private DispatcherTimer? _timer;
    private CancellationTokenSource? _cts;
    private bool _refreshing;
    private bool _disposed;
    private long? _selectedAttachmentId;      // preserve selection across polls
    private bool _suppressSelectionTracking;  // true while rebuilding Sessions (Clear nulls the grid's selection)

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

    /// <summary>Findings scoped to the selected session (Session Details → Warnings).</summary>
    public ObservableCollection<SessionWarningViewModel> SelectedSessionWarnings { get; } = new();

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
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenInEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeInPerformanceCommand))]
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
    [ObservableProperty] private string _oldestActiveLagText = "0";

    // --- transaction-gap gauge (scale-before-alarm: the OAT lag measured against the GC-danger
    //     budget, NOT the internal OIT→Next proportion — so a small/normal gap reads as a calm
    //     sliver, and only a materially large gap turns orange/red) ---
    [ObservableProperty] private bool _hasGap;
    [ObservableProperty] private double _gapFillWidth;               // fill px = lag / danger budget
    [ObservableProperty] private string _gapValueText = "0";         // the gap count (severity-coloured)
    [ObservableProperty] private string _gapSeverityBrushKey = "SubtleForegroundBrush";
    [ObservableProperty] private string _gapStatusText = string.Empty; // plain-language UiStrings.SessionManagerWhatItMeans
    [ObservableProperty] private string _gapScaleMaxText = "0";      // right-hand scale label (the danger line)

    // --- Session Details: plain-language "why it matters" ---
    [ObservableProperty] private string _selectedSessionWhyItMatters = string.Empty;
    public bool HasSelectedSessionWhyItMatters => SelectedSessionWhyItMatters.Length > 0;
    public bool HasSelectedSessionWarnings => SelectedSessionWarnings.Count > 0;

    partial void OnSelectedSessionWhyItMattersChanged(string value)
        => OnPropertyChanged(nameof(HasSelectedSessionWhyItMatters));

    public bool IsFilterAll => QuickFilter == SessionQuickFilter.All;
    public bool IsFilterGcRisk => QuickFilter == SessionQuickFilter.GcRisk;
    public bool IsFilterLongTx => QuickFilter == SessionQuickFilter.LongTx;

    public bool ShowSessionsEmpty => Sessions.Count == 0;
    public bool HasWarnings => Warnings.Count > 0;

    public bool IsSessionFilterActive => SelectedSession is not null;
    public string SessionFilterText => SelectedSession is { } s
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.SessionManagerTransactionsFilteredFormat, s.AttachmentId)
        : string.Empty;

    // Wired by MainWindowViewModel to the shared ConfirmDialog + clipboard channel + bridges.
    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;
    public event Action<string>? CopyToClipboardRequested;
    public event Action<string>? OpenInEditorRequested;
    public event Action<string>? AnalyzeInPerformanceRequested;

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
        ApplyFilters();
    }

    public bool HasSelectedSession => SelectedSession is not null;

    partial void OnSelectedSessionChanged(SessionRowViewModel? value)
    {
        // Only a USER selection updates the remembered id — a rebuild-induced null must not wipe it.
        if (!_suppressSelectionTracking)
        {
            _selectedAttachmentId = value?.AttachmentId;
        }
        OnPropertyChanged(nameof(HasSelectedSession));
        OnPropertyChanged(nameof(IsSessionFilterActive));
        OnPropertyChanged(nameof(SessionFilterText));
        RebuildSelectedSessionDetail();
        ApplyTransactionFilter();
    }

    /// <summary>Scoped Session-Details content: this session's warnings + the plain-language
    /// "why it matters" explanation. Rebuilt on selection change and after each refresh.</summary>
    private void RebuildSelectedSessionDetail()
    {
        SelectedSessionWarnings.Clear();
        var sel = SelectedSession;
        if (sel is not null && _report is not null)
        {
            foreach (var f in _report.Findings.Where(f => f.AttachmentId == sel.AttachmentId))
            {
                SelectedSessionWarnings.Add(new SessionWarningViewModel(f));
            }
        }
        OnPropertyChanged(nameof(HasSelectedSessionWarnings));

        SelectedSessionWhyItMatters = sel?.Risk switch
        {
            SessionRisk.GcBlocker => UiStrings.SessionManagerWhyGc,
            SessionRisk.LongTransaction => UiStrings.SessionManagerWhyLongTx,
            _ => string.Empty,
        };
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
    private void ClearSessionFilter() => SelectedSession = null;

    // Cancel Statement is deferred to V2 — for V1, Disconnect is the one clear, low-surprise
    // administrative action (Cancel Statement is Firebird-internals-y and risky to misread).
    // FirebirdSessionReader.CancelStatementAsync stays for that future revival.

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

    // Integration bridges — reuse the existing SQL-editor / Performance workflows. Neither
    // executes the statement (it's another session's running SQL): both drop it into the SQL
    // editor as a Saved Query so the user consciously runs it (F5), which then feeds Performance.
    private bool CanOpenStatement => SelectedSession is { HasStatement: true };

    [RelayCommand(CanExecute = nameof(CanOpenStatement))]
    private void OpenInEditor()
    {
        if (SelectedSession is { CurrentStatement.Length: > 0 } s) OpenInEditorRequested?.Invoke(s.CurrentStatement);
    }

    [RelayCommand(CanExecute = nameof(CanOpenStatement))]
    private void AnalyzeInPerformance()
    {
        if (SelectedSession is { CurrentStatement.Length: > 0 } s) AnalyzeInPerformanceRequested?.Invoke(s.CurrentStatement);
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
        _report = report;

        // masters
        _allSessions.Clear();
        _sessionLabels.Clear();
        foreach (var s in sessions)
        {
            _allSessions.Add(new SessionRowViewModel(s, report.EntryFor(s.AttachmentId), referenceTime));
            var name = string.IsNullOrWhiteSpace(s.ApplicationName) ? s.User : s.ApplicationName;
            _sessionLabels[s.AttachmentId] = $"{s.AttachmentId} · {name}";
        }

        BuildGapBar(report.Database);

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
            _ => query,
        };
        if (text.Length > 0)
        {
            query = query.Where(s => s.FilterKey.Contains(text));
        }

        // Preserve the selected attachment across the rebuild. Sessions.Clear() nulls the grid's
        // SelectedItem, which fires OnSelectedSessionChanged(null) — so we snapshot the target id
        // first and suppress selection-tracking so that callback can't wipe it before we restore.
        var targetId = _selectedAttachmentId;
        _suppressSelectionTracking = true;
        try
        {
            Sessions.Clear();
            foreach (var s in query)
            {
                Sessions.Add(s);
            }
            SelectedSession = targetId is { } id ? Sessions.FirstOrDefault(s => s.AttachmentId == id) : null;
        }
        finally
        {
            _suppressSelectionTracking = false;
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

    // A "GC-budget gauge": the fill is the OAT lag measured against the danger threshold
    // (SessionHealthOptions.LargeGapThreshold — the SAME line the health engine uses to flag a
    // GC risk), NOT the OIT→Next proportion. Rationale (educate, don't scare): a normal editing
    // workflow produces a tiny lag (e.g. 59) that is nowhere near the 10,000-transaction danger
    // line, so it renders as a barely-there calm sliver — consistent with the "Healthy / GC Risk 0"
    // verdict. Colour only escalates (grey → orange → red) as the lag approaches / crosses the line.
    private void BuildGapBar(DatabaseTransactionState db)
    {
        HasGap = db.OldestTransaction > 0 && db.NextTransaction > 0
                 && db.NextTransaction > db.OldestTransaction;
        if (!HasGap)
        {
            GapFillWidth = 0;
            return;
        }

        long lag = Math.Max(0, db.OldestActiveLag);
        long danger = SessionHealthOptions.Default.LargeGapThreshold; // 10,000 — the GC-risk line
        var (frac, severityKey, statusText) = ResolveGapGauge(lag, danger);

        // A nonzero lag always shows a small sliver so it's never invisible; a zero lag shows nothing.
        GapFillWidth = lag <= 0 ? 0 : Math.Max(3, frac * GapBarWidth);
        GapSeverityBrushKey = severityKey;
        GapStatusText = statusText;
        GapValueText = lag.ToString("N0", CultureInfo.CurrentCulture);
        GapScaleMaxText = danger.ToString("N0", CultureInfo.CurrentCulture);
    }

    /// <summary>Pure gap-gauge decision (scale-before-alarm): the fill fraction of the danger
    /// budget plus a severity brush key + plain-language status. Aligned with the engine's
    /// <see cref="SessionHealthOptions.LargeGapThreshold"/> so the gauge and the Health Bar
    /// verdict can never contradict — calm/grey below half the budget, orange approaching it,
    /// red at/over it.</summary>
    internal static (double Fraction, string SeverityBrushKey, string StatusText) ResolveGapGauge(long lag, long danger)
    {
        double frac = danger > 0 ? Math.Clamp((double)lag / danger, 0, 1) : 0;
        if (lag >= danger)
            return (frac, "DangerIconBrush", UiStrings.SessionManagerGapStatusCritical);
        if (lag >= danger / 2)
            return (frac, "WarningBrush", UiStrings.SessionManagerGapStatusWatch);
        return (frac, "SubtleForegroundBrush", UiStrings.SessionManagerGapStatusHealthy);
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
