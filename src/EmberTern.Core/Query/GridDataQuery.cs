using System;
using System.Collections.Generic;
using System.Globalization;

namespace EmberTern.Core.Query;

// Shared, UI-agnostic model for the data-grid filtering + aggregation system.
// The SAME model drives both the client-side path (materialized grids: SQL /
// Procedure / Function results) and the SQL push-down path (server-paged grids:
// Table Data / View Data), so the user sees identical behaviour regardless of
// which strategy runs underneath (the difference lives only in the executor).

/// <summary>Comparison operators available in a grid filter condition.</summary>
public enum GridFilterOperator
{
    Equals,
    NotEquals,
    LessThan,
    LessOrEqual,
    GreaterThan,
    GreaterOrEqual,
    Contains,
    StartsWith,
    EndsWith,
    IsNull,
    IsNotNull,
}

/// <summary>How multiple conditions in a filter are joined.</summary>
public enum GridFilterCombine
{
    And,
    Or,
}

/// <summary>Column-type aggregates. Applicability depends on the column category.</summary>
public enum GridAggregate
{
    Sum,
    Avg,
    Count,
    CountDistinct,
    Min,
    Max,
}

/// <summary>
/// Broad type category of a column, derived from its CLR type. Drives which
/// operators and aggregates make sense (numeric supports SUM/AVG and ordering;
/// text supports CONTAINING; BLOB supports only null checks / COUNT, etc.).
/// </summary>
public enum GridColumnCategory
{
    Numeric,
    Temporal,
    Text,
    Boolean,
    Other,
}

/// <summary>A single filter condition against one column.</summary>
/// <param name="ColumnIndex">0-based position in the result's column list.</param>
/// <param name="ColumnName">Column name (used to build the SQL WHERE).</param>
/// <param name="Operator">Comparison operator.</param>
/// <param name="Value">
/// User-entered text operand. Ignored for <see cref="GridFilterOperator.IsNull"/>
/// / <see cref="GridFilterOperator.IsNotNull"/>. Converted to the column's type at
/// evaluation / parameter-binding time.
/// </param>
public sealed record GridFilterCondition(
    int ColumnIndex,
    string ColumnName,
    GridFilterOperator Operator,
    string? Value);

/// <summary>An immutable set of conditions joined by a single combine mode.</summary>
public sealed class GridFilter
{
    public static readonly GridFilter Empty =
        new(Array.Empty<GridFilterCondition>(), GridFilterCombine.And);

    public GridFilter(IReadOnlyList<GridFilterCondition> conditions, GridFilterCombine combine)
    {
        Conditions = conditions ?? Array.Empty<GridFilterCondition>();
        Combine = combine;
    }

    public IReadOnlyList<GridFilterCondition> Conditions { get; }
    public GridFilterCombine Combine { get; }
    public bool IsEmpty => Conditions.Count == 0;
}

/// <summary>Classifies CLR types into <see cref="GridColumnCategory"/> and exposes
/// the operator / aggregate menus valid for each category.</summary>
public static class GridColumnClassifier
{
    public static GridColumnCategory Classify(Type? clrType)
    {
        if (clrType is null) return GridColumnCategory.Other;
        var t = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (t == typeof(bool)) return GridColumnCategory.Boolean;

        if (t == typeof(byte) || t == typeof(sbyte) ||
            t == typeof(short) || t == typeof(ushort) ||
            t == typeof(int) || t == typeof(uint) ||
            t == typeof(long) || t == typeof(ulong) ||
            t == typeof(decimal) || t == typeof(float) || t == typeof(double))
            return GridColumnCategory.Numeric;

        if (t == typeof(DateTime) || t == typeof(DateTimeOffset) ||
            t == typeof(TimeSpan) || t == typeof(DateOnly) || t == typeof(TimeOnly))
            return GridColumnCategory.Temporal;

        if (t == typeof(string) || t == typeof(char) || t == typeof(Guid))
            return GridColumnCategory.Text;

        return GridColumnCategory.Other;
    }

    private static readonly GridFilterOperator[] NullOps =
        { GridFilterOperator.IsNull, GridFilterOperator.IsNotNull };

    private static readonly GridFilterOperator[] OrderingOps =
    {
        GridFilterOperator.Equals, GridFilterOperator.NotEquals,
        GridFilterOperator.LessThan, GridFilterOperator.LessOrEqual,
        GridFilterOperator.GreaterThan, GridFilterOperator.GreaterOrEqual,
        GridFilterOperator.IsNull, GridFilterOperator.IsNotNull,
    };

    private static readonly GridFilterOperator[] TextOps =
    {
        GridFilterOperator.Equals, GridFilterOperator.NotEquals,
        GridFilterOperator.Contains, GridFilterOperator.StartsWith, GridFilterOperator.EndsWith,
        GridFilterOperator.IsNull, GridFilterOperator.IsNotNull,
    };

    private static readonly GridFilterOperator[] EqualityOps =
    {
        GridFilterOperator.Equals, GridFilterOperator.NotEquals,
        GridFilterOperator.IsNull, GridFilterOperator.IsNotNull,
    };

    public static IReadOnlyList<GridFilterOperator> OperatorsFor(GridColumnCategory category) => category switch
    {
        GridColumnCategory.Numeric => OrderingOps,
        GridColumnCategory.Temporal => OrderingOps,
        GridColumnCategory.Text => TextOps,
        GridColumnCategory.Boolean => EqualityOps,
        _ => NullOps,
    };

    private static readonly GridAggregate[] NumericAggs =
    {
        GridAggregate.Sum, GridAggregate.Avg, GridAggregate.Min, GridAggregate.Max,
        GridAggregate.Count, GridAggregate.CountDistinct,
    };

    private static readonly GridAggregate[] OrderableAggs =
    {
        GridAggregate.Min, GridAggregate.Max, GridAggregate.Count, GridAggregate.CountDistinct,
    };

    private static readonly GridAggregate[] CountAggs =
    {
        GridAggregate.Count, GridAggregate.CountDistinct,
    };

    private static readonly GridAggregate[] CountOnly = { GridAggregate.Count };

    public static IReadOnlyList<GridAggregate> AggregatesFor(GridColumnCategory category) => category switch
    {
        GridColumnCategory.Numeric => NumericAggs,
        GridColumnCategory.Temporal => OrderableAggs,
        GridColumnCategory.Text => OrderableAggs,
        GridColumnCategory.Boolean => CountAggs,
        _ => CountOnly,
    };

    /// <summary>True when the operator takes a value operand (false for null checks).</summary>
    public static bool OperatorTakesValue(GridFilterOperator op)
        => op is not (GridFilterOperator.IsNull or GridFilterOperator.IsNotNull);
}

/// <summary>
/// Parses a user-entered filter string into a canonical comparable value per
/// column category. Shared by the client-side evaluator (comparisons) and the
/// SQL builder (typed <c>FbParameter</c> values) so both paths interpret the
/// operand identically. Numeric → <see cref="decimal"/>, Temporal →
/// <see cref="DateTime"/>, Boolean → <see cref="bool"/>, Text → the string.
/// </summary>
public static class GridValueConverter
{
    public static bool TryConvert(string? text, GridColumnCategory category, out object? value)
    {
        value = null;
        if (text is null) return false;

        switch (category)
        {
            case GridColumnCategory.Numeric:
                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var d) ||
                    decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                {
                    value = d;
                    return true;
                }
                return false;

            case GridColumnCategory.Temporal:
                if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt) ||
                    DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                {
                    value = dt;
                    return true;
                }
                return false;

            case GridColumnCategory.Boolean:
                var s = text.Trim();
                if (bool.TryParse(s, out var b)) { value = b; return true; }
                if (s is "1") { value = true; return true; }
                if (s is "0") { value = false; return true; }
                return false;

            default: // Text / Other
                value = text;
                return true;
        }
    }

    /// <summary>Coerce a raw grid cell value to the canonical comparable for its
    /// category. Cells arrive from the driver already typed; DBNull → null.</summary>
    public static object? Canonicalize(object? cell, GridColumnCategory category)
    {
        if (cell is null || cell is DBNull) return null;
        try
        {
            return category switch
            {
                GridColumnCategory.Numeric => Convert.ToDecimal(cell, CultureInfo.InvariantCulture),
                GridColumnCategory.Temporal => Convert.ToDateTime(cell, CultureInfo.InvariantCulture),
                GridColumnCategory.Boolean => Convert.ToBoolean(cell, CultureInfo.InvariantCulture),
                _ => cell.ToString(),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            // Value that doesn't fit its category (mixed/loose typing) → compare as text.
            return cell.ToString();
        }
    }
}
