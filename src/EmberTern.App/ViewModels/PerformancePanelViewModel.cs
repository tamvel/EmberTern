using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Hosts the Performance panel (SQL Editor bottom-panel sub-tab). Phase 1 fills
/// the verdict, the plan tree and the details drawer; the findings (zone ②) and table-access
/// (zone ③-left) areas show placeholder empty-states until later phases supply them.
///
/// The VM holds no Firebird types: the host wires <see cref="ProfileCallback"/> (run the
/// profiler + analyzer, return a report or null when there is nothing to profile) and
/// <see cref="ClipboardWriteRequested"/>. A profiling/execution error is surfaced inline
/// as <see cref="ErrorMessage"/> — the host converts Firebird exceptions to a message.</summary>
public sealed partial class PerformancePanelViewModel : ViewModelBase
{
    /// <summary>Runs the profile and returns the analyzed report, or null when there is
    /// nothing to profile (not connected / no query). Throws with a user-facing message
    /// on an execution/capture failure.</summary>
    public Func<CancellationToken, Task<PerformanceReport?>>? ProfileCallback { get; set; }

    public Func<string, Task>? ClipboardWriteRequested { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAndProfileCommand))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _isProfiling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private VerdictViewModel? _verdict;

    [ObservableProperty]
    private ExecutionDetailsViewModel? _details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    [NotifyPropertyChangedFor(nameof(ShowPlanEmpty))]
    private IReadOnlyList<PlanNodeViewModel> _planRoots = Array.Empty<PlanNodeViewModel>();

    public bool HasReport => Verdict is not null;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasPlan => PlanRoots.Count > 0;
    public bool ShowPlanEmpty => HasReport && !HasPlan;
    public bool ShowEmptyState => !HasReport && !IsProfiling && !HasError;

    private bool CanRunProfile => !IsProfiling;

    [RelayCommand(CanExecute = nameof(CanRunProfile))]
    private async Task RunAndProfileAsync(CancellationToken cancellationToken)
    {
        if (ProfileCallback is null)
        {
            return;
        }

        IsProfiling = true;
        ErrorMessage = null;
        try
        {
            var report = await ProfileCallback(cancellationToken).ConfigureAwait(true);
            if (report is null)
            {
                // Nothing to profile — clear to the empty prompt.
                Reset();
            }
            else
            {
                ApplyReport(report);
            }
        }
        catch (OperationCanceledException)
        {
            // user cancelled — leave prior state
        }
        catch (Exception ex)
        {
            Reset();
            ErrorMessage = ex.Message;
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

    private void ApplyReport(PerformanceReport report)
    {
        Verdict = new VerdictViewModel(report.Verdict);
        PlanRoots = PlanNodeViewModel.BuildRoots(report.Plan);
        Details = new ExecutionDetailsViewModel(report.Details);
    }

    private void Reset()
    {
        Verdict = null;
        Details = null;
        PlanRoots = Array.Empty<PlanNodeViewModel>();
    }
}
