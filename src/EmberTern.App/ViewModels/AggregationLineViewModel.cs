using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One aggregation chip: a fixed (column · function) pairing plus its computed
/// result. The column + function are chosen once when the chip is added (via the
/// bar's add-row) and never edited inline — the chip is a compact read-only pill
/// with an ✕ to remove it. The computation is delegated to the host (client-side
/// <see cref="GridAggregator"/> over the filtered rows, or a server-side
/// <c>SELECT agg(...)</c>) so the chip stays data-agnostic.
/// </summary>
public partial class AggregationLineViewModel : ObservableObject
{
    private readonly Func<GridColumnRef, GridAggregate, Task<object?>> _compute;
    private readonly Action<AggregationLineViewModel> _remove;

    public AggregationLineViewModel(
        GridColumnRef column,
        AggregateOption function,
        Func<GridColumnRef, GridAggregate, Task<object?>> compute,
        Action<AggregationLineViewModel> remove)
    {
        Column = column;
        Function = function;
        _compute = compute;
        _remove = remove;
    }

    public GridColumnRef Column { get; }
    public AggregateOption Function { get; }

    /// <summary>The chip caption before the "= result", e.g. "KWOTA · SUM".</summary>
    public string Label => $"{Column.Name} · {Function.Label}";

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private bool _isComputing;

    /// <summary>Recompute this chip against the host's current (filtered) data.</summary>
    [RelayCommand]
    public async Task ComputeAsync()
    {
        IsComputing = true;
        try
        {
            var value = await _compute(Column, Function.Aggregate).ConfigureAwait(true);
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
}
