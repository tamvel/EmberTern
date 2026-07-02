using System;
using System.Collections.Generic;
using System.Text;
using EmberTern.Core.Query;

namespace EmberTern.Firebird;

/// <summary>
/// Builds the parameterized <c>WHERE</c> clause and aggregate expressions for the
/// server-paged grids (Table Data / View Data). Operands are bound as
/// <c>FbParameter</c>s (never inlined) so the filter is injection-safe; the
/// operator mapping mirrors the client-side evaluator so materialized and
/// server-paged grids behave identically:
///   Contains → CONTAINING (case-insensitive substring),
///   StartsWith → STARTING WITH (case-sensitive prefix),
///   EndsWith → LIKE '%x' ESCAPE '\' (case-sensitive suffix),
///   Equals/ordering → =, &lt;&gt;, &lt;, &lt;=, &gt;, &gt;= (case-sensitive on text).
/// </summary>
public static class FirebirdGridSqlBuilder
{
    public sealed record GridSqlParameter(string Name, object Value);

    /// <summary><see cref="WhereClause"/> is the predicate WITHOUT a leading
    /// <c>WHERE</c> (empty when the filter is empty). Parameters bind 1:1.</summary>
    public sealed record GridSqlFilter(string WhereClause, IReadOnlyList<GridSqlParameter> Parameters)
    {
        public static readonly GridSqlFilter Empty = new(string.Empty, Array.Empty<GridSqlParameter>());
        public bool HasClause => WhereClause.Length > 0;
    }

    public static GridSqlFilter BuildWhere(GridFilter filter, IReadOnlyList<QueryColumn> columns)
    {
        if (filter is null || filter.IsEmpty) return GridSqlFilter.Empty;

        var sb = new StringBuilder();
        var parameters = new List<GridSqlParameter>();
        string joiner = filter.Combine == GridFilterCombine.Or ? " OR " : " AND ";
        int paramIndex = 0;

        foreach (var c in filter.Conditions)
        {
            if (c.ColumnIndex < 0 || c.ColumnIndex >= columns.Count) continue;

            string col = Quote(c.ColumnName);
            var category = GridColumnClassifier.Classify(columns[c.ColumnIndex].ClrType);
            string? fragment = BuildFragment(col, c, category, ref paramIndex, parameters);
            if (fragment is null) continue; // unconvertible value operand → skip (UI validates)

            if (sb.Length > 0) sb.Append(joiner);
            sb.Append(fragment);
        }

        return sb.Length == 0
            ? GridSqlFilter.Empty
            : new GridSqlFilter(sb.ToString(), parameters);
    }

    private static string? BuildFragment(
        string col,
        GridFilterCondition c,
        GridColumnCategory category,
        ref int paramIndex,
        List<GridSqlParameter> parameters)
    {
        switch (c.Operator)
        {
            case GridFilterOperator.IsNull:
                return $"{col} IS NULL";
            case GridFilterOperator.IsNotNull:
                return $"{col} IS NOT NULL";
        }

        string p = "@p" + paramIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        switch (c.Operator)
        {
            case GridFilterOperator.Contains:
                parameters.Add(new GridSqlParameter(p, c.Value ?? string.Empty));
                paramIndex++;
                return $"{col} CONTAINING {p}";

            case GridFilterOperator.StartsWith:
                parameters.Add(new GridSqlParameter(p, c.Value ?? string.Empty));
                paramIndex++;
                return $"{col} STARTING WITH {p}";

            case GridFilterOperator.EndsWith:
                parameters.Add(new GridSqlParameter(p, "%" + EscapeLike(c.Value ?? string.Empty)));
                paramIndex++;
                return $"{col} LIKE {p} ESCAPE '\\'";
        }

        // Comparison operators — bind a typed operand (decimal / DateTime / bool /
        // string) so Firebird compares in the column's domain.
        if (!GridValueConverter.TryConvert(c.Value, category, out var operand) || operand is null)
            return null;

        parameters.Add(new GridSqlParameter(p, operand));
        paramIndex++;

        string op = c.Operator switch
        {
            GridFilterOperator.Equals => "=",
            GridFilterOperator.NotEquals => "<>",
            GridFilterOperator.LessThan => "<",
            GridFilterOperator.LessOrEqual => "<=",
            GridFilterOperator.GreaterThan => ">",
            GridFilterOperator.GreaterOrEqual => ">=",
            _ => "=",
        };
        return $"{col} {op} {p}";
    }

    /// <summary>The SQL aggregate expression over a quoted column, e.g.
    /// <c>SUM("AMOUNT")</c> or <c>COUNT(DISTINCT "STATUS")</c>.</summary>
    public static string AggregateExpression(GridAggregate aggregate, string columnName)
    {
        string col = Quote(columnName);
        return aggregate switch
        {
            GridAggregate.Sum => $"SUM({col})",
            GridAggregate.Avg => $"AVG({col})",
            GridAggregate.Min => $"MIN({col})",
            GridAggregate.Max => $"MAX({col})",
            GridAggregate.Count => $"COUNT({col})",
            GridAggregate.CountDistinct => $"COUNT(DISTINCT {col})",
            _ => $"COUNT({col})",
        };
    }

    // Firebird identifier quoting: wrap in double quotes, double any internal ones.
    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    // Escape LIKE metacharacters (% _ and the escape char) with a backslash so an
    // EndsWith operand is matched literally.
    private static string EscapeLike(string value)
    {
        var sb = new StringBuilder(value.Length + 4);
        foreach (char ch in value)
        {
            if (ch is '%' or '_' or '\\') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
