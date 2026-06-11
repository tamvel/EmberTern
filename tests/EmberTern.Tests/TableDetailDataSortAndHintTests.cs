using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

public class TableDetailDataSortAndHintTests
{
    private static TableDetailTabViewModel NewVm() => new("DUMMY_TABLE");

    private static QueryResult MakeResult(int rowCount)
    {
        var rows = new List<object?[]>(rowCount);
        for (int i = 0; i < rowCount; i++)
        {
            rows.Add(new object?[] { i });
        }
        return new QueryResult
        {
            Columns = new List<QueryColumn> { new("ID", typeof(int)) },
            Rows = rows,
        };
    }

    [Fact]
    public void DataPreviewHint_NoResult_ShowsPageOneZeroRows()
    {
        var vm = NewVm();
        Assert.Equal(
            string.Format(UiStrings.TableDetailDataPagedHintFormat, 1, 0),
            vm.DataPreviewHint);
    }

    [Fact]
    public void DataPreviewHint_UnderPageSize_ShowsActualCount()
    {
        var vm = NewVm();
        vm.DataResult = MakeResult(42);
        Assert.Equal(
            string.Format(UiStrings.TableDetailDataPagedHintFormat, 1, 42),
            vm.DataPreviewHint);
    }

    [Fact]
    public void DataPreviewHint_AtPageSize_ShowsActualCount()
    {
        var vm = NewVm();
        vm.DataResult = MakeResult(TableDetailTabViewModel.DataPreviewRowLimit);
        Assert.Equal(
            string.Format(
                UiStrings.TableDetailDataPagedHintFormat,
                1,
                TableDetailTabViewModel.DataPreviewRowLimit),
            vm.DataPreviewHint);
    }

    [Fact]
    public void DataPreviewHint_NonDefaultPage_ShowsCurrentPage()
    {
        var vm = NewVm();
        vm.CurrentPage = 3;
        vm.DataResult = MakeResult(42);
        Assert.Equal(
            string.Format(UiStrings.TableDetailDataPagedHintFormat, 3, 42),
            vm.DataPreviewHint);
    }

    [Fact]
    public void Pagination_DefaultState_NoPrevPage_NoNextPage_BeforeFetch()
    {
        var vm = NewVm();
        Assert.Equal(1, vm.CurrentPage);
        Assert.Equal(TableDetailTabViewModel.DataPreviewRowLimit, vm.PageSize);
        Assert.False(vm.HasPreviousPage);
        Assert.False(vm.HasNextPage);
    }

    [Fact]
    public void Pagination_FullPage_HasNextPage_HeuristicTrue()
    {
        // Without LastKnownRowCount, a full page is the "maybe more" signal.
        var vm = NewVm();
        vm.DataResult = MakeResult(TableDetailTabViewModel.DataPreviewRowLimit);
        Assert.True(vm.HasNextPage);
    }

    [Fact]
    public void Pagination_PartialPage_HasNextPage_False()
    {
        var vm = NewVm();
        vm.DataResult = MakeResult(50); // smaller than PageSize
        Assert.False(vm.HasNextPage);
    }

    [Fact]
    public void Pagination_LastKnownRowCount_GovernsHasNextPage()
    {
        // After GoToLast's COUNT probe, the authoritative count overrides the
        // "full page" heuristic. CurrentPage*PageSize < LastKnownRowCount → more.
        var vm = NewVm();
        vm.DataResult = MakeResult(200);
        vm.LastKnownRowCount = 350;
        Assert.True(vm.HasNextPage);
        vm.CurrentPage = 2;
        Assert.False(vm.HasNextPage); // page 2 covers rows 201..400, beyond the 350 known
    }

    [Fact]
    public void Pagination_PageSize_ClampsToMax()
    {
        var vm = NewVm();
        vm.PageSize = 99999;
        Assert.Equal(TableDetailTabViewModel.MaxPageSize, vm.PageSize);
    }

    [Fact]
    public void DataPreviewHint_WithSortColumn_AppendsSortedBy()
    {
        var vm = NewVm();
        vm.DataResult = MakeResult(10);
        vm.SortColumn = "ID";
        vm.SortDescending = false;
        Assert.Contains("ID", vm.DataPreviewHint);
        Assert.Contains("↑", vm.DataPreviewHint);
    }

    [Fact]
    public void DataPreviewHint_WithSortColumnDescending_ShowsDownArrow()
    {
        var vm = NewVm();
        vm.DataResult = MakeResult(10);
        vm.SortColumn = "NAME";
        vm.SortDescending = true;
        Assert.Contains("↓", vm.DataPreviewHint);
        Assert.DoesNotContain("↑", vm.DataPreviewHint);
    }

    // ApplyColumnSortAsync without a wired reader is a no-op except for the
    // state machine (SortColumn / SortDescending). The reader path itself is
    // exercised against a live DB in smoke testing — here we just pin the
    // unsorted → asc → desc → unsorted cycle.

    [Fact]
    public async Task ApplyColumnSortAsync_FirstClick_SetsAscending()
    {
        var vm = NewVm();
        await vm.ApplyColumnSortAsync("ID");
        Assert.Equal("ID", vm.SortColumn);
        Assert.False(vm.SortDescending);
    }

    [Fact]
    public async Task ApplyColumnSortAsync_SecondClickSameColumn_TogglesDescending()
    {
        var vm = NewVm();
        await vm.ApplyColumnSortAsync("ID");
        await vm.ApplyColumnSortAsync("ID");
        Assert.Equal("ID", vm.SortColumn);
        Assert.True(vm.SortDescending);
    }

    [Fact]
    public async Task ApplyColumnSortAsync_ThirdClickSameColumn_ClearsSort()
    {
        var vm = NewVm();
        await vm.ApplyColumnSortAsync("ID");
        await vm.ApplyColumnSortAsync("ID");
        await vm.ApplyColumnSortAsync("ID");
        Assert.Null(vm.SortColumn);
        Assert.False(vm.SortDescending);
    }

    [Fact]
    public async Task ApplyColumnSortAsync_DifferentColumn_ResetsToAscendingOnNewColumn()
    {
        var vm = NewVm();
        await vm.ApplyColumnSortAsync("ID");
        await vm.ApplyColumnSortAsync("ID"); // now desc on ID
        await vm.ApplyColumnSortAsync("NAME"); // switch
        Assert.Equal("NAME", vm.SortColumn);
        Assert.False(vm.SortDescending);
    }

    [Fact]
    public async Task ApplyColumnSortAsync_EmptyColumnName_NoOps()
    {
        var vm = NewVm();
        await vm.ApplyColumnSortAsync(string.Empty);
        Assert.Null(vm.SortColumn);
        Assert.False(vm.SortDescending);
    }
}
