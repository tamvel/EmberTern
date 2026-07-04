using EmberTern.App.ViewModels;
using EmberTern.Core.Performance;
using EmberTern.Core.Diagnostics;
using Xunit;

namespace EmberTern.Tests;

// Step 4b: App display of the non-result framing — verdict count + Table Access changes.
public class Step4DisplayTests
{
    private static PerformanceVerdict Verdict(bool hasResultSet, long returned, long changed) => new()
    {
        Grade = PerformanceGrade.Acceptable,
        Headline = "x",
        HasResultSet = hasResultSet,
        RowsReturned = returned,
        RowsChanged = changed,
    };

    [Fact]
    public void VerdictRowsText_Select_ShowsReturned()
        => Assert.Equal("285 rows", new VerdictViewModel(Verdict(true, 285, 0)).RowsText);

    [Fact]
    public void VerdictRowsText_NonResult_ShowsChanged()
    {
        Assert.Equal("8 rows changed", new VerdictViewModel(Verdict(false, 0, 8)).RowsText);
        Assert.Equal("1 row changed", new VerdictViewModel(Verdict(false, 0, 1)).RowsText);
    }

    [Fact]
    public void TableAccessBar_ChangesText_ListsNonZeroOps()
    {
        var bar = new TableAccessBarViewModel(new TableAccessStat("NAGL", 0, 0, Inserts: 3, Updates: 8, Deletes: 2), maxTotalReads: 1);
        Assert.True(bar.HasChanges);
        Assert.Equal("3 ins · 8 upd · 2 del", bar.ChangesText);
        Assert.Equal("", bar.ReadsText); // pure-DML row: no reads
    }

    [Fact]
    public void TableAccessBar_ReadOnly_NoChanges()
    {
        var bar = new TableAccessBarViewModel(new TableAccessStat("NAGL", 100, 0), maxTotalReads: 100);
        Assert.False(bar.HasChanges);
        Assert.Equal("", bar.ChangesText);
    }
}
