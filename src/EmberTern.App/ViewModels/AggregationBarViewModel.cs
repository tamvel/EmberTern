using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Host-agnostic aggregation bar shared by every data grid. Owns the aggregate
/// lines; each line computes through the host-supplied delegate (client-side over
/// the filtered rows, or a server-side aggregate query on the full/filtered set).
/// </summary>
public partial class AggregationBarViewModel : ObservableObject
{
    private readonly Func<GridColumnRef, GridAggregate, Task<object?>> _compute;
    private IReadOnlyList<GridColumnRef> _columns = Array.Empty<GridColumnRef>();

    public AggregationBarViewModel(Func<GridColumnRef, GridAggregate, Task<object?>> compute)
        => _compute = compute;

    public ObservableCollection<AggregationLineViewModel> Lines { get; } = new();

    [ObservableProperty]
    private bool _isBarOpen;

    public bool HasColumns => _columns.Count > 0;
    public bool ShowEmptyHint => Lines.Count == 0;

    public void SetColumns(IReadOnlyList<GridColumnRef> columns)
    {
        _columns = columns ?? Array.Empty<GridColumnRef>();
        Lines.Clear();
        OnPropertyChanged(nameof(HasColumns));
        OnPropertyChanged(nameof(ShowEmptyHint));
        AddLineCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddLine => _columns.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAddLine))]
    private async Task AddLineAsync()
    {
        var line = new AggregationLineViewModel(_columns, _compute, RemoveLine);
        Lines.Add(line);
        OnPropertyChanged(nameof(ShowEmptyHint));
        await line.ComputeAsync().ConfigureAwait(true);
    }

    private void RemoveLine(AggregationLineViewModel line)
    {
        Lines.Remove(line);
        OnPropertyChanged(nameof(ShowEmptyHint));
    }

    [RelayCommand]
    private void Toggle() => IsBarOpen = !IsBarOpen;

    /// <summary>Recompute every line — the host calls this after the filter or the
    /// underlying data changes. Sequential so a single <c>FbConnection</c> never
    /// runs concurrent aggregate commands (gotcha #31).</summary>
    public async Task RecomputeAllAsync()
    {
        foreach (var line in Lines)
            await line.ComputeAsync().ConfigureAwait(true);
    }
}
