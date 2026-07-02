using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One editable filter condition: column + operator + value. The available
/// operators follow the selected column's type category, and the value editor is
/// chosen by the operator via <see cref="ValueEditorKind"/> (None for null-checks,
/// Single otherwise) — so future List/Range/DateRange operators plug in by adding
/// an editor kind, not by rebuilding the panel.
/// </summary>
public partial class FilterConditionRowViewModel : ObservableObject
{
    private readonly Action<FilterConditionRowViewModel> _remove;

    public FilterConditionRowViewModel(
        IReadOnlyList<GridColumnRef> columns,
        Action<FilterConditionRowViewModel> remove,
        GridColumnRef? initialColumn = null,
        GridFilterOperator? initialOperator = null,
        string? initialValue = null)
    {
        Columns = columns;
        _remove = remove;
        _selectedColumn = initialColumn ?? (columns.Count > 0 ? columns[0] : null);
        _availableOperators = BuildOperators(_selectedColumn);
        _selectedOperator = PickOperator(_availableOperators, initialOperator);
        _value = initialValue;
    }

    public IReadOnlyList<GridColumnRef> Columns { get; }

    [ObservableProperty]
    private GridColumnRef? _selectedColumn;

    [ObservableProperty]
    private IReadOnlyList<OperatorOption> _availableOperators;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueEditorKind))]
    [NotifyPropertyChangedFor(nameof(ShowValueEditor))]
    private OperatorOption? _selectedOperator;

    [ObservableProperty]
    private string? _value;

    public ValueEditorKind ValueEditorKind =>
        SelectedOperator is { } o && GridColumnClassifier.OperatorTakesValue(o.Operator)
            ? ValueEditorKind.Single
            : ValueEditorKind.None;

    public bool ShowValueEditor => ValueEditorKind == ValueEditorKind.Single;

    partial void OnSelectedColumnChanged(GridColumnRef? value)
    {
        // Column type may change which operators are valid — rebuild + keep the
        // current operator if still applicable, else fall back to the first.
        var current = SelectedOperator?.Operator;
        AvailableOperators = BuildOperators(value);
        SelectedOperator = PickOperator(AvailableOperators, current);
    }

    /// <summary>Builds a <see cref="GridFilterCondition"/>, or null when the row is
    /// incomplete (no column, or a value-taking operator with an empty value).</summary>
    public GridFilterCondition? TryBuild()
    {
        if (SelectedColumn is null || SelectedOperator is null) return null;
        var op = SelectedOperator.Operator;
        bool takesValue = GridColumnClassifier.OperatorTakesValue(op);
        if (takesValue && string.IsNullOrEmpty(Value)) return null;
        return new GridFilterCondition(
            SelectedColumn.Index,
            SelectedColumn.Name,
            op,
            takesValue ? Value : null);
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    private static IReadOnlyList<OperatorOption> BuildOperators(GridColumnRef? column)
        => GridFilterCatalog.OperatorOptionsFor(column?.Category ?? GridColumnCategory.Other);

    private static OperatorOption? PickOperator(IReadOnlyList<OperatorOption> options, GridFilterOperator? desired)
    {
        if (options.Count == 0) return null;
        if (desired is { } d)
        {
            foreach (var o in options)
                if (o.Operator == d) return o;
        }
        return options[0];
    }
}
