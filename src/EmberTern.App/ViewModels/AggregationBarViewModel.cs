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
/// Host-agnostic aggregation bar shared by every data grid. IBExpert-style:
/// a compact add-row (column + type-aware function pickers + "＋ Add") produces
/// aggregate <b>chips</b> that accumulate in a wrapping strip — several aggregates
/// coexist without opening extra panels. Each chip computes through the
/// host-supplied delegate (client-side over the filtered rows, or a server-side
/// aggregate query on the full/filtered set); the host calls
/// <see cref="RecomputeAllAsync"/> after the filter changes so results stay live.
/// </summary>
public partial class AggregationBarViewModel : ObservableObject
{
    private readonly Func<GridColumnRef, GridAggregate, Task<object?>> _compute;
    private IReadOnlyList<GridColumnRef> _columns = Array.Empty<GridColumnRef>();

    public AggregationBarViewModel(Func<GridColumnRef, GridAggregate, Task<object?>> compute)
        => _compute = compute;

    /// <summary>The active aggregate chips.</summary>
    public ObservableCollection<AggregationLineViewModel> Lines { get; } = new();

    [ObservableProperty]
    private bool _isBarOpen;

    // --- Add-row: pick a column (persistent context), then a type-appropriate
    //     function — picking the function IS the add action (no separate button). ---

    [ObservableProperty]
    private GridColumnRef? _selectedMenuColumn;

    [ObservableProperty]
    private IReadOnlyList<AggregateOption> _menuFunctions = Array.Empty<AggregateOption>();

    // Null = the placeholder ("pick to add"). Picking a function auto-creates a chip
    // and resets this back to null, so the same function can be picked again.
    [ObservableProperty]
    private AggregateOption? _selectedMenuFunction;

    public bool HasColumns => _columns.Count > 0;
    public bool ShowEmptyHint => Lines.Count == 0;

    /// <summary>The current column set (used by the host's cell-driven adds + tests).</summary>
    public IReadOnlyList<GridColumnRef> Columns => _columns;

    /// <summary>Point the bar at a new result's columns. Clears existing chips (they
    /// referenced the previous column set) and re-seeds the add-row pickers.</summary>
    public void SetColumns(IReadOnlyList<GridColumnRef> columns)
    {
        _columns = columns ?? Array.Empty<GridColumnRef>();
        Lines.Clear();
        // Notify the ComboBox ItemsSource (Columns) BEFORE seeding its SelectedItem
        // (SelectedMenuColumn) — a selection set against a stale ItemsSource gets
        // clobbered back to null (gotcha #71). Seeding the column re-derives
        // MenuFunctions via the hook; the function stays on its placeholder (null).
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(HasColumns));
        OnPropertyChanged(nameof(ShowEmptyHint));
        SelectedMenuColumn = _columns.Count > 0 ? _columns[0] : null;
    }

    // The function menu follows the selected column's type category. Reset the
    // function to its placeholder so the user consciously picks the stat (and a
    // column change never accidentally adds a chip).
    partial void OnSelectedMenuColumnChanged(GridColumnRef? value)
    {
        MenuFunctions = GridFilterCatalog.AggregateOptionsFor(value?.Category ?? GridColumnCategory.Other);
        SelectedMenuFunction = null;
    }

    // Picking a function IS the add action: create + compute the chip immediately,
    // then reset the picker to its placeholder (so re-picking the same function adds
    // another). Setting SelectedMenuFunction = null re-enters here but returns on the
    // null guard, so there's no recursion.
    partial void OnSelectedMenuFunctionChanged(AggregateOption? value)
    {
        if (value is null || SelectedMenuColumn is null) return;
        var column = SelectedMenuColumn;
        SelectedMenuFunction = null;
        AddAggregate(column, value);
    }

    /// <summary>Add a chip for (column, function) and kick off its computation.
    /// Public so the cell-driven path and tests can drive it directly.</summary>
    public AggregationLineViewModel AddAggregate(GridColumnRef column, AggregateOption function)
    {
        var line = new AggregationLineViewModel(column, function, _compute, RemoveLine);
        Lines.Add(line);
        OnPropertyChanged(nameof(ShowEmptyHint));
        line.ComputeCommand.Execute(null);
        return line;
    }

    /// <summary>Convenience overload: resolve the aggregate's label from the column's
    /// type-valid functions (falls back to a plain label if not in the menu).</summary>
    public AggregationLineViewModel AddAggregate(GridColumnRef column, GridAggregate aggregate)
    {
        var option = GridFilterCatalog.AggregateOptionsFor(column.Category)
                         .FirstOrDefault(o => o.Aggregate == aggregate)
                     ?? new AggregateOption(aggregate, GridFilterCatalog.Label(aggregate));
        return AddAggregate(column, option);
    }

    private void RemoveLine(AggregationLineViewModel line)
    {
        Lines.Remove(line);
        OnPropertyChanged(nameof(ShowEmptyHint));
    }

    /// <summary>Recompute every chip — the host calls this after the filter or the
    /// underlying data changes. Sequential so a single <c>FbConnection</c> never
    /// runs concurrent aggregate commands (gotcha #31).</summary>
    public async Task RecomputeAllAsync()
    {
        foreach (var line in Lines)
            await line.ComputeAsync().ConfigureAwait(true);
    }
}
