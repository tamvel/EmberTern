using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Live view of a bulk operation for the batch-results dialog: opens immediately, appends
/// a row per object as it completes, keeps live Processed / Total / Success / Failed /
/// Duration counters, supports Cancel, All/Success/Failed filtering, and Copy All / Copy
/// Failed. Reused by every bulk op (recompile / recompute statistics / activate-deactivate).
/// </summary>
public partial class BatchResultsViewModel : ViewModelBase
{
    private readonly List<BatchResultRowViewModel> _all = new();
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private readonly CancellationTokenSource _cts = new();
    private DispatcherTimer? _timer;

    public BatchResultsViewModel(string title)
    {
        Title = title;
        VisibleRows = new ObservableCollection<BatchResultRowViewModel>();
    }

    public string Title { get; }

    /// <summary>The filtered rows the DataGrid binds to (All / Success / Failed).</summary>
    public ObservableCollection<BatchResultRowViewModel> VisibleRows { get; }

    /// <summary>Token the executor observes; cancelled by the Cancel command / dialog close.</summary>
    public CancellationToken CancellationToken => _cts.Token;

    [ObservableProperty] private int _total;
    [ObservableProperty] private int _processed;
    [ObservableProperty] private int _successCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _durationText = "00:00:00";
    // 0 = All, 1 = Success, 2 = Failed (bound to the filter ComboBox SelectedIndex).
    [ObservableProperty] private int _selectedFilterIndex;

    /// <summary>Live footer line: Processed / Total, Success, Failed, Duration.</summary>
    public string StatusSummary => string.Format(
        CultureInfo.CurrentCulture, UiStrings.BatchResultsLiveSummaryFormat,
        Processed, Total, SuccessCount, FailedCount, DurationText);

    partial void OnProcessedChanged(int value) => OnPropertyChanged(nameof(StatusSummary));
    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(StatusSummary));
    partial void OnSuccessCountChanged(int value) => OnPropertyChanged(nameof(StatusSummary));
    partial void OnFailedCountChanged(int value) => OnPropertyChanged(nameof(StatusSummary));
    partial void OnDurationTextChanged(string value) => OnPropertyChanged(nameof(StatusSummary));
    partial void OnSelectedFilterIndexChanged(int value) => RebuildVisible();

    // Raised by CopyAll / CopyFailed with the TSV text; the dialog writes the clipboard
    // (the VM holds no Avalonia clipboard type).
    public event Action<string>? CopyRequested;

    public void Begin(int total)
    {
        Total = total;
        IsRunning = true;
        _stopwatch.Restart();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => DurationText = Format(_stopwatch.Elapsed);
        _timer.Start();
    }

    public void AddResult(BatchOperationResult result)
    {
        var row = new BatchResultRowViewModel(result);
        _all.Add(row);
        Processed++;
        if (row.IsFailed) FailedCount++; else SuccessCount++;
        if (PassesFilter(row)) VisibleRows.Add(row);
    }

    public void Complete()
    {
        IsRunning = false;
        _stopwatch.Stop();
        _timer?.Stop();
        _timer = null;
        DurationText = Format(_stopwatch.Elapsed);
    }

    public void RequestCancel()
    {
        if (!_cts.IsCancellationRequested) _cts.Cancel();
    }

    [RelayCommand]
    private void Cancel() => RequestCancel();

    [RelayCommand]
    private void CopyAll() => CopyRequested?.Invoke(BuildClipboardText(failedOnly: false));

    [RelayCommand]
    private void CopyFailed() => CopyRequested?.Invoke(BuildClipboardText(failedOnly: true));

    // Tab-separated (Object, Operation, Result, [Error]) — pastes cleanly into Excel /
    // a task list. "Copy Failed" is the common one: a ready error worklist.
    public string BuildClipboardText(bool failedOnly)
    {
        var sb = new StringBuilder();
        foreach (var r in _all)
        {
            if (failedOnly && !r.IsFailed) continue;
            sb.Append(r.Object).Append('\t').Append(r.Operation).Append('\t').Append(r.Result);
            if (r.IsFailed && r.Error.Length > 0) sb.Append('\t').Append(r.Error);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private bool PassesFilter(BatchResultRowViewModel r) => SelectedFilterIndex switch
    {
        1 => !r.IsFailed,
        2 => r.IsFailed,
        _ => true,
    };

    private void RebuildVisible()
    {
        VisibleRows.Clear();
        foreach (var r in _all)
        {
            if (PassesFilter(r)) VisibleRows.Add(r);
        }
    }

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}

public sealed class BatchResultRowViewModel
{
    public BatchResultRowViewModel(BatchOperationResult r)
    {
        Object = r.Object;
        Operation = r.Operation;
        IsFailed = !r.Success;
        Result = r.Success ? UiStrings.BatchResultOk : UiStrings.BatchResultFailed;
        Error = r.Error ?? string.Empty;
    }

    public string Object { get; }
    public string Operation { get; }
    public string Result { get; }
    public string Error { get; }
    public bool IsFailed { get; }
}
