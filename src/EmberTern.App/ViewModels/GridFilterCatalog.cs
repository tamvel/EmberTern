using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>An operator paired with its display label (ComboBox item).</summary>
public sealed record OperatorOption(GridFilterOperator Operator, string Label);

/// <summary>An aggregate paired with its display label (ComboBox item).</summary>
public sealed record AggregateOption(GridAggregate Aggregate, string Label);

/// <summary>Maps operators / aggregates to labels and builds the type-aware
/// option lists the pickers bind to. Adding a future operator is a new
/// <see cref="Label(GridFilterOperator)"/> arm + a classifier entry — the
/// catalog and panel don't otherwise change.</summary>
public static class GridFilterCatalog
{
    public static string Label(GridFilterOperator op) => op switch
    {
        GridFilterOperator.Equals => UiStrings.FilterOpEquals,
        GridFilterOperator.NotEquals => UiStrings.FilterOpNotEquals,
        GridFilterOperator.LessThan => UiStrings.FilterOpLessThan,
        GridFilterOperator.LessOrEqual => UiStrings.FilterOpLessOrEqual,
        GridFilterOperator.GreaterThan => UiStrings.FilterOpGreaterThan,
        GridFilterOperator.GreaterOrEqual => UiStrings.FilterOpGreaterOrEqual,
        GridFilterOperator.Contains => UiStrings.FilterOpContains,
        GridFilterOperator.StartsWith => UiStrings.FilterOpStartsWith,
        GridFilterOperator.EndsWith => UiStrings.FilterOpEndsWith,
        GridFilterOperator.IsNull => UiStrings.FilterOpIsNull,
        GridFilterOperator.IsNotNull => UiStrings.FilterOpIsNotNull,
        _ => op.ToString(),
    };

    public static string Label(GridAggregate agg) => agg switch
    {
        GridAggregate.Sum => UiStrings.AggregateSum,
        GridAggregate.Avg => UiStrings.AggregateAvg,
        GridAggregate.Count => UiStrings.AggregateCount,
        GridAggregate.CountDistinct => UiStrings.AggregateCountDistinct,
        GridAggregate.Min => UiStrings.AggregateMin,
        GridAggregate.Max => UiStrings.AggregateMax,
        _ => agg.ToString(),
    };

    public static IReadOnlyList<OperatorOption> OperatorOptionsFor(GridColumnCategory category)
        => GridColumnClassifier.OperatorsFor(category)
            .Select(o => new OperatorOption(o, Label(o)))
            .ToList();

    public static IReadOnlyList<AggregateOption> AggregateOptionsFor(GridColumnCategory category)
        => GridColumnClassifier.AggregatesFor(category)
            .Select(a => new AggregateOption(a, Label(a)))
            .ToList();
}
