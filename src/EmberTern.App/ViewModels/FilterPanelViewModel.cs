using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Host-agnostic filter panel shared by every data grid. Owns the editable
/// condition rows + the AND/OR combine mode, builds an immutable
/// <see cref="GridFilter"/>, and asks the host to apply it via
/// <see cref="ApplyRequested"/> (the host decides client-side LINQ vs SQL
/// push-down — the panel never touches data). The built <see cref="GridFilter"/>
/// is a clean DTO, so future Save/Recent filters just persist/restore it.
/// </summary>
public partial class FilterPanelViewModel : ObservableObject
{
    private IReadOnlyList<GridColumnRef> _columns = Array.Empty<GridColumnRef>();

    /// <summary>Host applies the filter (client or server). Panel awaits it so a
    /// server re-query completes before the UI settles.</summary>
    public Func<GridFilter, Task>? ApplyRequested { get; set; }

    public ObservableCollection<FilterConditionRowViewModel> Conditions { get; } = new();

    // false = AND (match all), true = OR (match any).
    [ObservableProperty]
    private bool _matchAny;

    [ObservableProperty]
    private bool _isPanelOpen;

    // True while a non-empty filter is applied (drives the toolbar's "active" look).
    [ObservableProperty]
    private bool _isFilterActive;

    public bool HasColumns => _columns.Count > 0;
    public bool ShowEmptyHint => Conditions.Count == 0;

    /// <summary>Point the panel at a new result's columns. Clears any existing
    /// conditions (they referenced the previous column set).</summary>
    public void SetColumns(IReadOnlyList<GridColumnRef> columns)
    {
        _columns = columns ?? Array.Empty<GridColumnRef>();
        Conditions.Clear();
        IsFilterActive = false;
        OnPropertyChanged(nameof(HasColumns));
        OnPropertyChanged(nameof(ShowEmptyHint));
        AddConditionCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddCondition => _columns.Count > 0;

    [RelayCommand(CanExecute = nameof(CanAddCondition))]
    private void AddCondition() => AddRow();

    private FilterConditionRowViewModel AddRow(
        GridColumnRef? column = null, GridFilterOperator? op = null, string? value = null)
    {
        var row = new FilterConditionRowViewModel(_columns, RemoveRow, column, op, value);
        Conditions.Add(row);
        OnPropertyChanged(nameof(ShowEmptyHint));
        return row;
    }

    private void RemoveRow(FilterConditionRowViewModel row)
    {
        Conditions.Remove(row);
        OnPropertyChanged(nameof(ShowEmptyHint));
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var filter = BuildFilter();
        IsFilterActive = !filter.IsEmpty;
        if (ApplyRequested is { } apply) await apply(filter).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        Conditions.Clear();
        OnPropertyChanged(nameof(ShowEmptyHint));
        IsFilterActive = false;
        if (ApplyRequested is { } apply) await apply(GridFilter.Empty).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Toggle() => IsPanelOpen = !IsPanelOpen;

    /// <summary>Assembles the current rows into an immutable filter (skips
    /// incomplete rows).</summary>
    public GridFilter BuildFilter()
    {
        var conditions = Conditions
            .Select(c => c.TryBuild())
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();
        return conditions.Count == 0
            ? GridFilter.Empty
            : new GridFilter(conditions, MatchAny ? GridFilterCombine.Or : GridFilterCombine.And);
    }

    /// <summary>"Filter by value" / "Exclude value" / "contains" from a grid cell:
    /// adds a preset condition, opens the panel, and applies immediately.</summary>
    public Task ApplyFromCellAsync(int columnIndex, GridFilterOperator op, string? value)
    {
        var column = _columns.FirstOrDefault(c => c.Index == columnIndex);
        if (column is null) return Task.CompletedTask;
        AddRow(column, op, value);
        IsPanelOpen = true;
        return ApplyAsync();
    }
}
