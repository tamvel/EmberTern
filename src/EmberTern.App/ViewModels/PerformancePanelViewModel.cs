using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Hosts the Performance panel (SQL Editor bottom-panel sub-tab).
///
/// Option B workflow: performance is a VIEW of the last real execution, not a separate
/// run. The host marks the panel stale after each query (no re-execution — the timing and
/// row count come from the run the user already did), and the analysis is built lazily the
/// first time the panel is shown while stale (a cheap prepare-only plan capture). "Refresh"
/// re-reads the plan for the last query; it never re-runs the query.
///
/// Information hierarchy: a plain-language summary (grade + which table is full-scanned +
/// timing) is primary; the execution plan is demoted into a collapsed "advanced" section
/// with the raw plan folded in. Findings + table-access are Phase-2 homes shown as subtle
/// placeholders. The VM holds no Firebird types — the host supplies the build callback and
/// converts Firebird exceptions to a message.</summary>
public sealed partial class PerformancePanelViewModel : ViewModelBase
{
    /// <summary>Builds the analyzed report from the LAST executed query (reads its plan;
    /// does not re-run it). Returns null when there is nothing to analyze (no run yet /
    /// not connected). Throws with a user-facing message on a capture failure.</summary>
    public Func<CancellationToken, Task<PerformanceReport?>>? BuildCallback { get; set; }

    public Func<string, Task>? ClipboardWriteRequested { get; set; }

    private bool _isVisible;
    private bool _isStale;

    /// <summary>The last analyzed report, kept so a language change can re-render it (see
    /// <see cref="RefreshLocalizedText"/>). ⚠ Held for that reason alone — nothing re-analyzes it.</summary>
    private PerformanceReport? _lastReport;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isProfiling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowReadsNudge))]
    private VerdictViewModel? _verdict;

    [ObservableProperty]
    private ExecutionDetailsViewModel? _details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    private IReadOnlyList<PlanNodeViewModel> _planRoots = Array.Empty<PlanNodeViewModel>();

    // Plain-language primary summary (derived from the report by PerformanceInsight).
    [ObservableProperty] private string? _gradeLine;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlanLead))]
    private string? _planLead;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoiseSummary))]
    private string? _noiseSummary;

    [ObservableProperty] private bool _showForwardPointer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTiming))]
    private string? _timingText;

    // Phase 2: measured reads. ReadsAvailable is true when the run was profiled with
    // per-table reads (distinguishes "no costly scans" from "reads not captured").
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReadsNudge))]
    [NotifyPropertyChangedFor(nameof(FindingsNoneVisible))]
    private bool _readsAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReadsNudge))]
    private bool _planHasFullScan;

    /// <summary>Reads-based findings for the Findings zone.</summary>
    public ObservableCollection<FindingViewModel> Findings { get; } = new();

    /// <summary>Red/blue per-table bars for the Table Access zone.</summary>
    public ObservableCollection<TableAccessBarViewModel> TableAccess { get; } = new();

    public bool HasFindings => Findings.Count > 0;
    public bool HasTableAccess => TableAccess.Count > 0;

    /// <summary>Nudge to measure reads: there's a full scan but this run wasn't profiled
    /// with per-table reads (the Performance tab wasn't open when it ran).</summary>
    public bool ShowReadsNudge => HasReport && !ReadsAvailable && PlanHasFullScan;

    /// <summary>Reads were measured and nothing costly was found — the "all good" note.</summary>
    public bool FindingsNoneVisible => ReadsAvailable && !HasFindings;

    public bool HasReport => Verdict is not null;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasPlan => PlanRoots.Count > 0;
    public bool ShowEmptyState => !HasReport && !IsProfiling && !HasError;
    public bool HasPlanLead => !string.IsNullOrEmpty(PlanLead);
    public bool HasNoiseSummary => !string.IsNullOrEmpty(NoiseSummary);
    public bool HasTiming => !string.IsNullOrEmpty(TimingText);

    /// <summary>Called by the host after each executed query. Marks the analysis stale;
    /// if the panel is currently shown, refreshes immediately.</summary>
    public void MarkStale()
    {
        _isStale = true;
        if (_isVisible)
        {
            TriggerRefresh();
        }
    }

    /// <summary>Called by the host when the Performance tab is shown/hidden. On becoming
    /// visible while stale, the analysis is built lazily (cheap plan capture).</summary>
    public void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (visible && _isStale)
        {
            TriggerRefresh();
        }
    }

    private void TriggerRefresh() => _ = EnsureFreshAsync();

    private bool CanRefresh => !IsProfiling;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync()
    {
        _isStale = true;
        return EnsureFreshAsync();
    }

    private async Task EnsureFreshAsync()
    {
        if (IsProfiling || !_isStale || BuildCallback is null)
        {
            return;
        }

        IsProfiling = true;
        ErrorMessage = null;
        try
        {
            var report = await BuildCallback(CancellationToken.None).ConfigureAwait(true);
            if (report is null)
            {
                Reset();
            }
            else
            {
                ApplyReport(report);
            }
            _isStale = false;
        }
        catch (OperationCanceledException)
        {
            // leave prior state
        }
        catch (Exception ex)
        {
            Reset();
            ErrorMessage = ex.Message;
            _isStale = false;
        }
        finally
        {
            IsProfiling = false;
        }
    }

    [RelayCommand]
    private async Task CopyDetailsAsync()
    {
        if (Details is { } d && ClipboardWriteRequested is { } write)
        {
            await write(d.CopyText).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Re-render the current report in the language the app has just switched to.
    ///
    /// <para>⭐ <b>Rebuilding the whole projection is SAFE here, and that is a measurement rather than a
    /// convenience</b> — the difference from C5's diagnostics panel, which had to refresh rows in place. That
    /// panel binds a SELECTING list and skips rebuilds through an <c>Unchanged</c> gate, so a rebuild would
    /// have lost the user's selection and the gate would have eaten the refresh anyway. The Findings zone is a
    /// plain <c>ItemsControl</c> with no selection and <see cref="ApplyReport"/> has no such gate, so this is
    /// C1's "rebuild the cards" shape.</para>
    ///
    /// <para>⚠ It also thaws four App strings that were frozen BEFORE this etap: <c>GradeLine</c>,
    /// <c>PlanLead</c>, <c>NoiseSummary</c> and <c>TimingText</c> are stored <c>[ObservableProperty]</c>
    /// values, so notifying them re-read the same finished English — #353 for the fourth time.</para>
    ///
    /// <para>⚠ Phrased below without naming the language-change event, and deliberately so: the guard that
    /// forbids a view model to read the language preference scans this file as TEXT, so quoting that
    /// identifier here — even in order to say we do not use it — would turn the explanation into a failure.</para>
    ///
    /// <para>⛔ The panel deliberately does NOT subscribe to that event itself: there is one
    /// panel per host (the SQL editor and every open Procedure / Function tab), so a subscription would be one
    /// live registration per tab with nothing disposing it. It is CALLED from its owner instead.</para>
    /// </summary>
    internal void RefreshLocalizedText()
    {
        if (_lastReport is { } report)
        {
            ApplyReport(report);
        }
    }

    private void ApplyReport(PerformanceReport report)
    {
        _lastReport = report;
        Verdict = new VerdictViewModel(report.Verdict);
        Details = new ExecutionDetailsViewModel(report.Details);
        PlanRoots = PlanNodeViewModel.BuildRoots(report.Plan);
        GradeLine = PerformanceInsight.GradeLine(report);
        PlanLead = PerformanceInsight.PlanLead(report);
        NoiseSummary = PerformanceInsight.NoiseSummary(report);
        ShowForwardPointer = PerformanceInsight.ShowForwardPointer(report);
        TimingText = Details.HasTimings ? Details.TimingsText : null;

        Findings.Clear();
        foreach (var finding in report.Findings)
        {
            Findings.Add(new FindingViewModel(finding));
        }

        TableAccess.Clear();
        if (report.Access is { } access && access.Tables.Count > 0)
        {
            long maxTotal = access.Tables.Max(t => t.TotalReads);
            foreach (var table in access.Tables)
            {
                TableAccess.Add(new TableAccessBarViewModel(table, maxTotal));
            }
        }

        PlanHasFullScan = report.Plan?.EnumerateNodes().Any(n => n.IsSequentialScan) ?? false;
        ReadsAvailable = report.Access is not null;
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(HasTableAccess));
        OnPropertyChanged(nameof(FindingsNoneVisible));
    }

    private void Reset()
    {
        _lastReport = null;
        Verdict = null;
        Details = null;
        PlanRoots = Array.Empty<PlanNodeViewModel>();
        GradeLine = null;
        PlanLead = null;
        NoiseSummary = null;
        ShowForwardPointer = false;
        TimingText = null;
        Findings.Clear();
        TableAccess.Clear();
        ReadsAvailable = false;
        PlanHasFullScan = false;
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(HasTableAccess));
        OnPropertyChanged(nameof(FindingsNoneVisible));
    }
}
