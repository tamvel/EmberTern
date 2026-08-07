using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmberTern.Core.Query;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The ONE builder of clipboard text for the data grids' <b>Copy cell / Copy row / Copy row with headers /
/// Copy all with headers</b> actions. Pure and static — it takes a column set, a row set and the target, and
/// returns text; it knows nothing about clipboards, view models or grids.
///
/// <para>⭐⭐ WHY IT EXISTS (2026-08-07). Those four operations shipped on the SQL Editor grid only, as private
/// members of <c>MainWindowViewModel</c>. The user asked for the same copying in every data grid — Table Data,
/// View Data, Procedure and Function results — and all five hold the same shape (a <see cref="QueryResult"/>'s
/// <c>Columns</c> + <c>object?[]</c> rows), so the alternative was four more copies of one TSV convention.
/// A second copy of a format is a format free to drift, and the drift is invisible: each grid would keep
/// working, just differently.</para>
///
/// <para>⚠ EACH CALLER SUPPLIES ITS OWN ROW SET, and that is the point of taking <paramref name="allRows"/>
/// rather than a whole result. "All" means <em>the rows that grid is showing</em>, which is not the same
/// object for every grid: Table Data must pass its writable <c>EditableRows</c> mirror, because a row the user
/// added or deleted in the session exists only there — copying <c>DataResult.Rows</c> would emit rows that are
/// no longer on screen and omit ones that are.</para>
///
/// <para>⚠ THE TARGET IS THE ROW OBJECT, NEVER A ROW INDEX. Every caller already holds the right-clicked
/// <c>object?[]</c>, and re-deriving its index against a sorted / filtered / paged view is a failure mode with
/// no upside — the same reasoning already recorded on the Copy-as-INSERT path, where a reference lookup once
/// silently dropped the copy.</para>
///
/// <para>⚠ TSV CONVENTION, matching <c>ClipboardTextExporter</c>: cells are TAB-separated and an embedded TAB
/// / CR / LF becomes a space rather than being quoted, because Excel's clipboard paste does not honour quotes.
/// ⭐ <b>A single CELL is the deliberate exception — it is returned unescaped</b>, because copying one cell
/// means copying that value, and a multi-line VARCHAR flattened to spaces would be a silent corruption of what
/// the user asked for. There are no columns to keep aligned in a one-cell copy.</para>
/// </summary>
public static class GridCopyText
{
    /// <summary>
    /// Builds the clipboard text, or <c>null</c> when the request cannot be served (no columns, no target row
    /// for a row-scoped mode, an out-of-range column). ⚠ Null means "nothing to copy" and callers must treat
    /// it as such — writing an empty string to the clipboard would silently destroy whatever was there.
    /// </summary>
    /// <param name="mode">Which of the four copy operations.</param>
    /// <param name="columns">The result's columns — the header line, and the bound for <paramref name="columnIndex"/>.</param>
    /// <param name="allRows">The rows this grid is showing; used by <see cref="CopyGridMode.AllWithHeaders"/> only.</param>
    /// <param name="row">The right-clicked row; ignored by <see cref="CopyGridMode.AllWithHeaders"/>.</param>
    /// <param name="columnIndex">The right-clicked column's DATA index; used by <see cref="CopyGridMode.Cell"/> only.</param>
    public static string? Build(
        CopyGridMode mode,
        IReadOnlyList<QueryColumn> columns,
        IReadOnlyList<object?[]> allRows,
        object?[]? row,
        int columnIndex)
    {
        if (columns is null || columns.Count == 0) return null;

        switch (mode)
        {
            case CopyGridMode.Cell:
            {
                if (row is null) return null;
                if (columnIndex < 0 || columnIndex >= columns.Count || columnIndex >= row.Length) return null;
                // Unescaped on purpose — see the class remarks.
                return FormatCell(row[columnIndex]);
            }

            case CopyGridMode.Row:
            {
                if (row is null) return null;
                return FormatRow(row);
            }

            case CopyGridMode.RowWithHeaders:
            {
                if (row is null) return null;
                return HeaderLine(columns) + Environment.NewLine + FormatRow(row);
            }

            case CopyGridMode.AllWithHeaders:
            {
                var sb = new StringBuilder();
                sb.Append(HeaderLine(columns));
                foreach (var r in allRows ?? Array.Empty<object?[]>())
                {
                    sb.Append(Environment.NewLine);
                    sb.Append(FormatRow(r));
                }
                return sb.ToString();
            }

            default:
                return null;
        }
    }

    private static string HeaderLine(IReadOnlyList<QueryColumn> columns)
        => string.Join('\t', columns.Select(c => EscapeCell(c.Name)));

    private static string FormatRow(object?[] row)
        => string.Join('\t', row.Select(FormatCell).Select(EscapeCell));

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DBNull => string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    // TSV cells with an embedded tab or newline would break column alignment when pasted into
    // Excel / IBExpert. Match the IBExpert convention: replace them with spaces. Quoting/escaping is not
    // standard for TSV consumers.
    private static string EscapeCell(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0) return value;
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
