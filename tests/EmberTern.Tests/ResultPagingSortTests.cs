using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Query;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Part 3 — SQL editor Results grid: client-side paging + 3-state column sort.
// Pure VM logic (no UI / DB), built on the same isolated-store harness the other
// VM tests use.
public class ResultPagingSortTests
{
    [Fact]
    public void NewResult_StartsOnPageOne_Unsorted()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Numbers(10);

        Assert.Equal(1, h.Main.ResultPage);
        Assert.Equal(-1, h.Main.ResultSortColumnIndex);
        Assert.False(h.Main.HasResultPreviousPage);
        // 10 rows, page size 200 → single page.
        Assert.False(h.Main.HasResultNextPage);
        Assert.Equal(10, h.Main.PagedResultRows.Count);
    }

    [Fact]
    public void Paging_SlicesByPageSize_AndNavigates()
    {
        using var h = new Harness();
        int size = MainWindowViewModel.ResultPageSize;
        h.Main.CurrentResult = Numbers(size * 2 + 5); // 3 pages

        Assert.Equal(size, h.Main.PagedResultRows.Count);
        Assert.True(h.Main.HasResultNextPage);
        Assert.False(h.Main.HasResultPreviousPage);
        Assert.Equal(0, h.Main.PagedResultRows[0][0]); // first row of page 1

        h.Main.ResultNextPageCommand.Execute(null);
        Assert.Equal(2, h.Main.ResultPage);
        Assert.Equal(size, h.Main.PagedResultRows.Count);
        Assert.Equal(size, h.Main.PagedResultRows[0][0]); // first row of page 2

        h.Main.ResultLastPageCommand.Execute(null);
        Assert.Equal(3, h.Main.ResultPage);
        Assert.Equal(5, h.Main.PagedResultRows.Count); // remainder
        Assert.False(h.Main.HasResultNextPage);
        Assert.True(h.Main.HasResultPreviousPage);

        h.Main.ResultFirstPageCommand.Execute(null);
        Assert.Equal(1, h.Main.ResultPage);
        Assert.Equal(0, h.Main.PagedResultRows[0][0]);
    }

    [Fact]
    public void CycleSort_ThreeStates_AscDescNone()
    {
        using var h = new Harness();
        // Rows out of order on column 0: 3,1,2
        h.Main.CurrentResult = new QueryResult
        {
            Columns = new[] { new QueryColumn("N", typeof(int)) },
            Rows = new object?[][]
            {
                new object?[] { 3 },
                new object?[] { 1 },
                new object?[] { 2 },
            },
        };

        // 1st click → ascending
        h.Main.CycleResultSort(0);
        Assert.Equal(0, h.Main.ResultSortColumnIndex);
        Assert.False(h.Main.ResultSortDescending);
        Assert.Equal(new object?[] { 1, 2, 3 }, h.Main.PagedResultRows.Select(r => r[0]));

        // 2nd click → descending
        h.Main.CycleResultSort(0);
        Assert.True(h.Main.ResultSortDescending);
        Assert.Equal(new object?[] { 3, 2, 1 }, h.Main.PagedResultRows.Select(r => r[0]));

        // 3rd click → no sort (original order restored)
        h.Main.CycleResultSort(0);
        Assert.Equal(-1, h.Main.ResultSortColumnIndex);
        Assert.Equal(new object?[] { 3, 1, 2 }, h.Main.PagedResultRows.Select(r => r[0]));
    }

    [Fact]
    public void CycleSort_DifferentColumn_RestartsAtAscending()
    {
        using var h = new Harness();
        h.Main.CurrentResult = new QueryResult
        {
            Columns = new[] { new QueryColumn("A", typeof(int)), new QueryColumn("B", typeof(int)) },
            Rows = new object?[][]
            {
                new object?[] { 2, 9 },
                new object?[] { 1, 8 },
            },
        };

        h.Main.CycleResultSort(0);
        h.Main.CycleResultSort(0); // col 0 desc
        Assert.True(h.Main.ResultSortDescending);

        h.Main.CycleResultSort(1); // switch to col 1 → ascending
        Assert.Equal(1, h.Main.ResultSortColumnIndex);
        Assert.False(h.Main.ResultSortDescending);
        Assert.Equal(new object?[] { 8, 9 }, h.Main.PagedResultRows.Select(r => r[1]));
    }

    [Fact]
    public void NewResult_ResetsSortAndPage()
    {
        using var h = new Harness();
        int size = MainWindowViewModel.ResultPageSize;
        h.Main.CurrentResult = Numbers(size * 2);
        h.Main.ResultNextPageCommand.Execute(null);
        Assert.Equal(2, h.Main.ResultPage);

        // Sorting returns to page 1 (the top row changes).
        h.Main.CycleResultSort(0);
        Assert.Equal(0, h.Main.ResultSortColumnIndex);
        Assert.Equal(1, h.Main.ResultPage);

        // A new result set resets both sort and page.
        h.Main.CurrentResult = Numbers(3);
        Assert.Equal(1, h.Main.ResultPage);
        Assert.Equal(-1, h.Main.ResultSortColumnIndex);
    }

    [Fact]
    public void NullResult_EmptyPagedRows()
    {
        using var h = new Harness();
        h.Main.CurrentResult = null;
        Assert.Empty(h.Main.PagedResultRows);
        Assert.False(h.Main.HasResultNextPage);
        Assert.False(h.Main.HasResultPreviousPage);
    }

    // ── Record N of M ─────────────────────────────────────────────────────
    [Fact]
    public void RecordInfo_NoResult_IsEmpty()
    {
        using var h = new Harness();
        h.Main.CurrentResult = null;
        Assert.Equal(string.Empty, h.Main.ResultRecordInfo);
    }

    [Fact]
    public void RecordInfo_RowsButNoSelection_ShowsCount()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Numbers(10);
        Assert.Equal("10 rows", h.Main.ResultRecordInfo);
    }

    [Fact]
    public void RecordInfo_SelectionOnFirstPage_IsAbsolutePosition()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Numbers(10);
        h.Main.SetResultSelectedRow(3); // 0-based within page → 4th record
        Assert.Equal("Record 4 of 10", h.Main.ResultRecordInfo);
    }

    [Fact]
    public void RecordInfo_SelectionOnSecondPage_AddsPageOffset()
    {
        using var h = new Harness();
        int size = MainWindowViewModel.ResultPageSize;
        h.Main.CurrentResult = Numbers(size * 2);
        h.Main.ResultNextPageCommand.Execute(null); // page 2
        h.Main.SetResultSelectedRow(0);             // first row of page 2
        Assert.Equal($"Record {size + 1} of {size * 2}", h.Main.ResultRecordInfo);
    }

    [Fact]
    public void RecordInfo_PageChange_ClearsSelection()
    {
        using var h = new Harness();
        int size = MainWindowViewModel.ResultPageSize;
        h.Main.CurrentResult = Numbers(size * 2);
        h.Main.SetResultSelectedRow(5);
        Assert.Equal("Record 6 of " + (size * 2), h.Main.ResultRecordInfo);

        h.Main.ResultNextPageCommand.Execute(null); // re-slice drops selection
        Assert.Equal($"{size * 2} rows", h.Main.ResultRecordInfo);
    }

    private static QueryResult Numbers(int count)
    {
        var rows = new object?[count][];
        for (int i = 0; i < count; i++) rows[i] = new object?[] { i };
        return new QueryResult
        {
            Columns = new[] { new QueryColumn("N", typeof(int)) },
            Rows = rows,
        };
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Store = new ConnectionProfileStore(TempDir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(Store, Service);
        }

        public string TempDir { get; }
        public ConnectionProfileStore Store { get; }
        public FirebirdConnectionService Service { get; }
        public MainWindowViewModel Main { get; }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}

// Shared object?[] row comparer (used by the Results grid sort + the Table Data View).
public class RowIndexComparerTests
{
    [Fact]
    public void Compare_SameComparableType_OrdersByValue()
    {
        var c = new RowIndexComparer(0);
        Assert.True(c.Compare(new object?[] { 1 }, new object?[] { 2 }) < 0);
        Assert.True(c.Compare(new object?[] { 5 }, new object?[] { 2 }) > 0);
        Assert.Equal(0, c.Compare(new object?[] { 3 }, new object?[] { 3 }));
    }

    [Fact]
    public void Compare_NullsSortFirst()
    {
        var c = new RowIndexComparer(0);
        Assert.True(c.Compare(new object?[] { null }, new object?[] { 1 }) < 0);
        Assert.True(c.Compare(new object?[] { 1 }, new object?[] { null }) > 0);
        Assert.Equal(0, c.Compare(new object?[] { null }, new object?[] { null }));
    }

    [Fact]
    public void Compare_MixedTypes_FallsBackToStringCompare()
    {
        var c = new RowIndexComparer(0);
        // int vs string → string compare of "10" vs "9" → "10" < "9"
        Assert.True(c.Compare(new object?[] { 10 }, new object?[] { "9" }) < 0);
    }

    [Fact]
    public void Compare_IndexBeyondRow_TreatedAsNull()
    {
        var c = new RowIndexComparer(2);
        Assert.Equal(0, c.Compare(new object?[] { 1 }, new object?[] { 2 }));
    }
}
