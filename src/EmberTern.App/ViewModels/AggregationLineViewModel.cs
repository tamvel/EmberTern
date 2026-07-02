using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One aggregation line: column + function → result. The function menu follows
/// the column's type category; the actual computation is delegated to the host
/// (client-side <see cref="GridAggregator"/> over the filtered rows, or a
/// server-side <c>SELECT agg(...)</c>) so the line stays data-agnostic.
/// </summary>
public partial class AggregationLineViewModel : ObservableObject
{
    private readonly Func<GridColumnRef, GridAggregate, Task<object?>> _compute;
    private readonly Action<AggregationLineViewModel> _remove;

    public AggregationLineViewModel(
        IReadOnlyList<GridColumnRef> columns,
        Func<GridColumnRef, GridAggregate, Task<object?>> compute,
        Action<AggregationLineViewModel> remove)
    {
        Columns = columns;
        _compute = compute;
        _remove = remove;
        _selectedColumn = columns.Count > 0 ? columns[0] : null;
        _availableFunctions = BuildFunctions(_selectedColumn);
        _selectedFunction = _availableFunctions.Count > 0 ? _availableFunctions[0] : null;
    }

    public IReadOnlyList<GridColumnRef> Columns { get; }

    [ObservableProperty]
    private GridColumnRef? _selectedColumn;

    [ObservableProperty]
    private IReadOnlyList<AggregateOption> _availableFunctions;

    [ObservableProperty]
    private AggregateOption? _selectedFunction;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private bool _isComputing;

    partial void OnSelectedColumnChanged(GridColumnRef? value)
    {
        var current = SelectedFunction?.Aggregate;
        AvailableFunctions = BuildFunctions(value);
        SelectedFunction = PickFunction(AvailableFunctions, current);
        ResultText = string.Empty;
    }

    partial void OnSelectedFunctionChanged(AggregateOption? value) => ResultText = string.Empty;

    /// <summary>Recompute this line against the host's current (filtered) data.</summary>
    [RelayCommand]
    public async Task ComputeAsync()
    {
        if (SelectedColumn is null || SelectedFunction is null) return;
        IsComputing = true;
        try
        {
            var value = await _compute(SelectedColumn, SelectedFunction.Aggregate).ConfigureAwait(true);
            ResultText = Format(value);
        }
        catch (Exception)
        {
            ResultText = UiStrings.AggregationErrorResult;
        }
        finally
        {
            IsComputing = false;
        }
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    private static string Format(object? value) => value switch
    {
        null => UiStrings.AggregationNullResult,
        DateTime dt => dt.ToString(CultureInfo.CurrentCulture),
        IFormattable f => f.ToString(null, CultureInfo.CurrentCulture),
        _ => value.ToString() ?? UiStrings.AggregationNullResult,
    };

    private static IReadOnlyList<AggregateOption> BuildFunctions(GridColumnRef? column)
        => GridFilterCatalog.AggregateOptionsFor(column?.Category ?? GridColumnCategory.Other);

    private static AggregateOption? PickFunction(IReadOnlyList<AggregateOption> options, GridAggregate? desired)
    {
        if (options.Count == 0) return null;
        if (desired is { } d)
        {
            foreach (var o in options)
                if (o.Aggregate == d) return o;
        }
        return options[0];
    }
}
