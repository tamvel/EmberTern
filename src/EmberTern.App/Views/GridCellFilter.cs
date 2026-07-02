using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Query;

namespace EmberTern.App.Views;

/// <summary>
/// One right-clicked data cell resolved for a "filter from cell" action:
/// its column index, the cell's textual value, whether it was NULL, and the
/// column's type category (drives which context-menu items apply).
/// </summary>
public readonly record struct GridCellFilterContext(
    int ColumnIndex, string? Value, bool IsNull, GridColumnCategory Category);

/// <summary>
/// Shared "filter from cell" plumbing reused by every data grid (SQL / Procedure /
/// Function results, Table / View data). It resolves the clicked cell (gotcha #99 —
/// via the dedicated <see cref="DataGridCellPointerPressedEventArgs"/>, no reflection)
/// and maps the three context-menu verbs to a preset filter condition. Each host
/// adds its own 3 MenuItems (grids already carry their own ContextMenu — Copy /
/// Set NULL — so we append rather than own the menu) and calls
/// <see cref="FilterPanelViewModel.ApplyFromCellAsync"/> with the mapped triple.
/// </summary>
public static class GridCellFilter
{
    /// <summary>Resolve the right-clicked cell against the grid's row data + the
    /// column set. Returns null when the press isn't on a data cell. Prefers the
    /// column's boxed data index in <c>Column.Tag</c> (robust to column reorder);
    /// falls back to display order when unstamped.</summary>
    public static GridCellFilterContext? Resolve(
        DataGrid grid,
        DataGridCellPointerPressedEventArgs e,
        IReadOnlyList<GridColumnRef> columns)
    {
        if (e.Row?.DataContext is not object?[] row) return null;
        if (e.Column is null) return null;

        int index = e.Column.Tag is int tag ? tag : grid.Columns.IndexOf(e.Column);
        if (index < 0 || index >= row.Length) return null;

        var cell = row[index];
        bool isNull = cell is null or DBNull;
        string? value = isNull ? null : FormatCellValue(cell!);
        var category = index < columns.Count ? columns[index].Category : GridColumnCategory.Other;
        return new GridCellFilterContext(index, value, isNull, category);
    }

    // The filter value must ROUND-TRIP: the string we produce here is later parsed
    // back (GridValueConverter.TryConvert) for the comparison / SQL parameter. For a
    // DateTime cell, Convert.ToString uses the "G" format, which DROPS sub-second
    // precision — a Firebird TIMESTAMP with a fraction then never equals its own
    // truncated value, so "Filter by value" on a timestamp found 0 rows. Emit an
    // invariant, sub-second-preserving form (fraction only when non-zero, so a
    // whole-second timestamp stays clean and matches the grid display).
    internal static string? FormatCellValue(object cell)
    {
        if (cell is DateTime dt)
        {
            bool hasFraction = dt.Ticks % TimeSpan.TicksPerSecond != 0;
            return dt.ToString(
                hasFraction ? "yyyy-MM-dd HH:mm:ss.FFFFFFF" : "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);
        }
        return Convert.ToString(cell, CultureInfo.CurrentCulture);
    }

    /// <summary>"Filter by value": = value, or IS NULL for a null cell.</summary>
    public static (int ColumnIndex, GridFilterOperator Op, string? Value) FilterByValue(GridCellFilterContext ctx)
        => ctx.IsNull
            ? (ctx.ColumnIndex, GridFilterOperator.IsNull, null)
            : (ctx.ColumnIndex, GridFilterOperator.Equals, ctx.Value);

    /// <summary>"Exclude value": ≠ value, or IS NOT NULL for a null cell.</summary>
    public static (int ColumnIndex, GridFilterOperator Op, string? Value) ExcludeValue(GridCellFilterContext ctx)
        => ctx.IsNull
            ? (ctx.ColumnIndex, GridFilterOperator.IsNotNull, null)
            : (ctx.ColumnIndex, GridFilterOperator.NotEquals, ctx.Value);

    /// <summary>"Filter: contains" — text columns only (see <see cref="SupportsContains"/>);
    /// returns null for a null cell.</summary>
    public static (int ColumnIndex, GridFilterOperator Op, string? Value)? Contains(GridCellFilterContext ctx)
        => ctx.IsNull ? null : (ctx.ColumnIndex, GridFilterOperator.Contains, ctx.Value);

    /// <summary>Contains only makes sense on text cells (CONTAINING is a string op).</summary>
    public static bool SupportsContains(GridCellFilterContext ctx)
        => ctx.Category == GridColumnCategory.Text && !ctx.IsNull;
}
