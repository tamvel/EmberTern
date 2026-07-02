using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EmberTern.Core.Query;

/// <summary>
/// Client-side filter evaluation over materialized <c>object?[]</c> rows. Matches
/// Firebird semantics so the result is identical to the SQL push-down path:
/// Contains = case-insensitive substring (FB CONTAINING), StartsWith =
/// case-sensitive prefix (FB STARTING WITH), EndsWith = case-sensitive suffix
/// (LIKE '%x'), Equals/NotEquals/ordering on text = case-sensitive (FB '='),
/// and any comparison against NULL is false (SQL 3-valued logic).
/// </summary>
public static class GridFilterEvaluator
{
    public static bool Matches(object?[] row, GridFilter filter, IReadOnlyList<QueryColumn> columns)
    {
        if (filter.IsEmpty) return true;

        bool result = filter.Combine == GridFilterCombine.And;
        foreach (var c in filter.Conditions)
        {
            bool one = EvaluateCondition(row, c, columns);
            if (filter.Combine == GridFilterCombine.And)
            {
                if (!one) return false;
            }
            else
            {
                if (one) return true;
            }
        }
        return result;
    }

    private static bool EvaluateCondition(object?[] row, GridFilterCondition c, IReadOnlyList<QueryColumn> columns)
    {
        if (c.ColumnIndex < 0 || c.ColumnIndex >= row.Length || c.ColumnIndex >= columns.Count)
            return false;

        object? raw = row[c.ColumnIndex];
        if (raw is DBNull) raw = null;

        if (c.Operator == GridFilterOperator.IsNull) return raw is null;
        if (c.Operator == GridFilterOperator.IsNotNull) return raw is not null;

        // Every other operator against NULL is false (matches SQL UNKNOWN → not selected).
        if (raw is null) return false;

        var category = GridColumnClassifier.Classify(columns[c.ColumnIndex].ClrType);

        // Text operators live in the string domain regardless of the raw type.
        switch (c.Operator)
        {
            case GridFilterOperator.Contains:
                return AsString(raw).IndexOf(c.Value ?? string.Empty, StringComparison.InvariantCultureIgnoreCase) >= 0;
            case GridFilterOperator.StartsWith:
                return AsString(raw).StartsWith(c.Value ?? string.Empty, StringComparison.Ordinal);
            case GridFilterOperator.EndsWith:
                return AsString(raw).EndsWith(c.Value ?? string.Empty, StringComparison.Ordinal);
        }

        if (!GridValueConverter.TryConvert(c.Value, category, out var operand) || operand is null)
            return false;
        var cell = GridValueConverter.Canonicalize(raw, category);
        if (cell is null) return false;

        int cmp = CompareCanonical(cell, operand, category);
        return c.Operator switch
        {
            GridFilterOperator.Equals => cmp == 0,
            GridFilterOperator.NotEquals => cmp != 0,
            GridFilterOperator.LessThan => cmp < 0,
            GridFilterOperator.LessOrEqual => cmp <= 0,
            GridFilterOperator.GreaterThan => cmp > 0,
            GridFilterOperator.GreaterOrEqual => cmp >= 0,
            _ => false,
        };
    }

    private static string AsString(object v) => v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;

    private static int CompareCanonical(object cell, object operand, GridColumnCategory category)
    {
        switch (category)
        {
            case GridColumnCategory.Numeric:
                return decimal.Compare((decimal)ToCanonicalOrString(cell, category), (decimal)operand);
            case GridColumnCategory.Temporal:
                return DateTime.Compare((DateTime)ToCanonicalOrString(cell, category), (DateTime)operand);
            case GridColumnCategory.Boolean:
                return ((bool)cell).CompareTo((bool)operand);
            default:
                return string.CompareOrdinal(AsString(cell), AsString(operand));
        }
    }

    // cell was already canonicalized; if canonicalization fell back to string
    // (loose typing) treat both as strings.
    private static object ToCanonicalOrString(object cell, GridColumnCategory category)
    {
        if (category == GridColumnCategory.Numeric && cell is decimal) return cell;
        if (category == GridColumnCategory.Temporal && cell is DateTime) return cell;
        return cell;
    }
}

/// <summary>
/// Client-side aggregation over materialized rows. COUNT counts non-null values
/// (SQL <c>COUNT(col)</c>); COUNT DISTINCT counts distinct non-null values. SUM/
/// AVG use <see cref="decimal"/> for exact numerics and <see cref="double"/> for
/// float/double columns. MIN/MAX order numerics, dates, then text (ordinal).
/// Returns the raw computed value (VM formats for display); null when the
/// aggregate does not apply to the column category or there are no values.
/// </summary>
public static class GridAggregator
{
    public static object? Compute(
        IReadOnlyList<object?[]> rows,
        int columnIndex,
        GridAggregate aggregate,
        Type? clrType)
    {
        var category = GridColumnClassifier.Classify(clrType);
        if (!GridColumnClassifier.AggregatesFor(category).Contains(aggregate)) return null;

        var values = new List<object>();
        foreach (var row in rows)
        {
            if (columnIndex < 0 || columnIndex >= row.Length) continue;
            var v = row[columnIndex];
            if (v is null or DBNull) continue;
            values.Add(v);
        }

        switch (aggregate)
        {
            case GridAggregate.Count:
                return (long)values.Count;
            case GridAggregate.CountDistinct:
                return (long)values
                    .Select(v => GridValueConverter.Canonicalize(v, category))
                    .Where(v => v is not null)
                    .Distinct()
                    .Count();
        }

        if (values.Count == 0) return null;

        switch (category)
        {
            case GridColumnCategory.Numeric:
                return AggregateNumeric(values, aggregate, clrType);
            case GridColumnCategory.Temporal:
                return AggregateTemporal(values, aggregate);
            default: // Text / Boolean / Other → only Min/Max meaningful here
                return AggregateText(values, aggregate);
        }
    }

    private static object? AggregateNumeric(List<object> values, GridAggregate aggregate, Type? clrType)
    {
        var t = Nullable.GetUnderlyingType(clrType ?? typeof(object)) ?? clrType;
        bool floating = t == typeof(float) || t == typeof(double);

        if (floating)
        {
            var nums = values.Select(v => Convert.ToDouble(v, CultureInfo.InvariantCulture)).ToList();
            return aggregate switch
            {
                GridAggregate.Sum => nums.Sum(),
                GridAggregate.Avg => nums.Average(),
                GridAggregate.Min => nums.Min(),
                GridAggregate.Max => nums.Max(),
                _ => (object?)null,
            };
        }
        else
        {
            var nums = values.Select(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture)).ToList();
            return aggregate switch
            {
                GridAggregate.Sum => nums.Sum(),
                GridAggregate.Avg => nums.Average(),
                GridAggregate.Min => nums.Min(),
                GridAggregate.Max => nums.Max(),
                _ => (object?)null,
            };
        }
    }

    private static object? AggregateTemporal(List<object> values, GridAggregate aggregate)
    {
        var dts = values.Select(v => Convert.ToDateTime(v, CultureInfo.InvariantCulture)).ToList();
        return aggregate switch
        {
            GridAggregate.Min => dts.Min(),
            GridAggregate.Max => dts.Max(),
            _ => (object?)null,
        };
    }

    private static object? AggregateText(List<object> values, GridAggregate aggregate)
    {
        var strs = values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty).ToList();
        return aggregate switch
        {
            GridAggregate.Min => strs.OrderBy(s => s, StringComparer.Ordinal).First(),
            GridAggregate.Max => strs.OrderBy(s => s, StringComparer.Ordinal).Last(),
            _ => (object?)null,
        };
    }
}
