using System;
using System.Collections.Generic;
using EmberTern.App.ViewModels;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The ONE clipboard-text builder every data grid's copy actions go through (2026-08-07).
///
/// <para>⭐ Why it is tested here rather than only through <see cref="CopyGridTests"/> (which drives the SQL
/// Editor view model): those cases pin that <b>the SQL grid's output did not change</b> when the format moved
/// out of <c>MainWindowViewModel</c>, which is a different question from <b>what the format is</b>. Four more
/// grids now depend on the answer, and none of them goes through that view model.</para>
/// </summary>
public class GridCopyTextTests
{
    private static readonly IReadOnlyList<QueryColumn> Columns = new[]
    {
        new QueryColumn("ID", typeof(int)),
        new QueryColumn("NAME", typeof(string)),
    };

    private static readonly IReadOnlyList<object?[]> Rows = new[]
    {
        new object?[] { 1, "Cena detaliczna" },
        new object?[] { 2, "Cena hurtowa" },
    };

    private static string? Build(CopyGridMode mode, int rowIndex = 0, int columnIndex = 0)
        => GridCopyText.Build(mode, Columns, Rows, rowIndex >= 0 ? Rows[rowIndex] : null, columnIndex);

    [Fact]
    public void Cell_IsJustThatValue()
        => Assert.Equal("Cena hurtowa", Build(CopyGridMode.Cell, rowIndex: 1, columnIndex: 1));

    [Fact]
    public void Row_IsTabSeparated_WithNoHeader()
        => Assert.Equal("1\tCena detaliczna", Build(CopyGridMode.Row));

    [Fact]
    public void RowWithHeaders_PutsTheHeaderLineFirst()
        => Assert.Equal("ID\tNAME" + Environment.NewLine + "1\tCena detaliczna", Build(CopyGridMode.RowWithHeaders));

    [Fact]
    public void AllWithHeaders_EmitsTheHeaderThenEveryRow()
        => Assert.Equal(
            "ID\tNAME" + Environment.NewLine + "1\tCena detaliczna" + Environment.NewLine + "2\tCena hurtowa",
            Build(CopyGridMode.AllWithHeaders, rowIndex: -1, columnIndex: -1));

    [Fact]
    public void ANullCell_IsAnEmptyField_NotTheWordNull()
    {
        var rows = new[] { new object?[] { 1, null }, new object?[] { 2, DBNull.Value } };
        Assert.Equal("1\t", GridCopyText.Build(CopyGridMode.Row, Columns, rows, rows[0], 0));
        Assert.Equal("2\t", GridCopyText.Build(CopyGridMode.Row, Columns, rows, rows[1], 0));
    }

    /// <summary>
    /// ⚠ TSV cannot carry a tab or a newline inside a field — pasted into Excel it would silently shift the
    /// remaining values into the wrong columns. Matches <c>ClipboardTextExporter</c>'s convention (replace,
    /// do not quote), because Excel's clipboard paste does not honour quotes.
    /// </summary>
    [Fact]
    public void ARowFlattensTabsAndNewlinesInsideCells()
    {
        var rows = new[] { new object?[] { 1, "a\tb\r\nc" } };
        Assert.Equal("1\ta b  c", GridCopyText.Build(CopyGridMode.Row, Columns, rows, rows[0], 0));
    }

    /// <summary>
    /// ⭐⭐ THE ONE DELIBERATE ASYMMETRY, pinned so it is not "tidied up" into consistency: a single CELL is
    /// copied VERBATIM. Copying one cell means copying that value, and there are no neighbouring columns to
    /// keep aligned — flattening a multi-line VARCHAR there would corrupt exactly what the user asked for.
    /// </summary>
    [Fact]
    public void ACellKeepsItsOwnNewlines()
    {
        var rows = new[] { new object?[] { 1, "line one\r\nline two" } };
        Assert.Equal(
            "line one\r\nline two",
            GridCopyText.Build(CopyGridMode.Cell, Columns, rows, rows[0], 1));
    }

    [Fact]
    public void NoColumns_MeansNothingToCopy()
        => Assert.Null(GridCopyText.Build(CopyGridMode.AllWithHeaders, Array.Empty<QueryColumn>(), Rows, Rows[0], 0));

    /// <summary>
    /// ⚠ Null means "nothing to copy" and the caller must leave the clipboard alone — writing an empty string
    /// through would destroy what the user already had there. <c>GridClipboard.WriteAsync</c> owns that rule;
    /// these are the inputs that reach it.
    /// </summary>
    [Theory]
    [InlineData(CopyGridMode.Cell)]
    [InlineData(CopyGridMode.Row)]
    [InlineData(CopyGridMode.RowWithHeaders)]
    public void ARowScopedMode_WithNoTargetRow_IsNull(CopyGridMode mode)
        => Assert.Null(GridCopyText.Build(mode, Columns, Rows, row: null, columnIndex: 0));

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void ACellOutsideTheColumns_IsNull(int columnIndex)
        => Assert.Null(GridCopyText.Build(CopyGridMode.Cell, Columns, Rows, Rows[0], columnIndex));

    /// <summary>
    /// ⚠ "All" with no rows is still the header line, not null: the grid has a result, it is simply empty —
    /// which is a different fact from "there is nothing to copy" and worth pasting.
    /// </summary>
    [Fact]
    public void AllWithHeaders_OverAnEmptyResult_IsTheHeaderLine()
        => Assert.Equal(
            "ID\tNAME",
            GridCopyText.Build(CopyGridMode.AllWithHeaders, Columns, Array.Empty<object?[]>(), null, -1));
}
