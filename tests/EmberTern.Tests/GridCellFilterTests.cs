using System;
using System.Globalization;
using EmberTern.App.Views;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

// Bug fix: "Filter by value" on a Timestamp cell found 0 rows. The cell-derived
// filter value must ROUND-TRIP through GridValueConverter.TryConvert so the exact
// comparison matches the row it came from — Convert.ToString's "G" format dropped
// sub-second precision, so a Firebird TIMESTAMP with a fraction never equalled its
// own truncated value.
public class GridCellFilterTests
{
    private static readonly QueryColumn[] Cols = { new("DZIEN", typeof(DateTime)) };

    [Fact]
    public void FormatCellValue_WholeSecondDateTime_HasNoFraction()
    {
        var dt = new DateTime(2018, 7, 3, 14, 50, 0);
        Assert.Equal("2018-07-03 14:50:00", GridCellFilter.FormatCellValue(dt));
    }

    [Fact]
    public void FormatCellValue_SubSecondDateTime_PreservesFraction()
    {
        var dt = new DateTime(2018, 7, 3, 14, 50, 0).AddTicks(1234567);
        var s = GridCellFilter.FormatCellValue(dt);
        Assert.Equal("2018-07-03 14:50:00.1234567", s);

        // Round-trips exactly through the shared converter.
        Assert.True(GridValueConverter.TryConvert(s, GridColumnCategory.Temporal, out var back));
        Assert.Equal(dt, Assert.IsType<DateTime>(back));
    }

    [Fact]
    public void FormatCellValue_NonDateTime_UsesCurrentCulture()
    {
        Assert.Equal("42", GridCellFilter.FormatCellValue(42));
        Assert.Equal("hi", GridCellFilter.FormatCellValue("hi"));
    }

    // The end-to-end bug scenario: a row whose TIMESTAMP has a sub-second fraction,
    // "Filter by value" = Equals with the cell-derived value → MUST match that row.
    [Fact]
    public void FilterByValue_SubSecondTimestamp_MatchesItsOwnRow()
    {
        var raw = new DateTime(2018, 7, 3, 14, 50, 0).AddTicks(1234567);
        var row = new object?[] { raw };
        var value = GridCellFilter.FormatCellValue(raw);

        var filter = new GridFilter(
            new[] { new GridFilterCondition(0, "DZIEN", GridFilterOperator.Equals, value) },
            GridFilterCombine.And);

        Assert.True(GridFilterEvaluator.Matches(row, filter, Cols), "the filtered cell must match its own row");
    }

    // Regression pin: the OLD behaviour (Convert.ToString "G" — no sub-seconds) would
    // NOT match a sub-second row, which is exactly the reported 0-rows bug.
    [Fact]
    public void FilterByValue_TruncatedTimestampValue_MissesSubSecondRow()
    {
        var raw = new DateTime(2018, 7, 3, 14, 50, 0).AddTicks(1234567);
        var row = new object?[] { raw };
        var truncated = Convert.ToString(raw, CultureInfo.CurrentCulture); // the old, precision-losing form

        var filter = new GridFilter(
            new[] { new GridFilterCondition(0, "DZIEN", GridFilterOperator.Equals, truncated) },
            GridFilterCombine.And);

        Assert.False(GridFilterEvaluator.Matches(row, filter, Cols), "the truncated value must NOT match (documents the old bug)");
    }
}
