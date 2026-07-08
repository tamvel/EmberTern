using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Export;
using EmberTern.App.ViewModels;
using EmberTern.Core.Export;
using EmberTern.Core.Query;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Etap 5 — the reusable export adapters (RowBufferExportSource for materialized VM-row grids;
// ServerPagedExportSource for server-paged grids) + the per-module BuildExportSource factories.
// Each module supplies only an adapter; all export logic stays in the shared framework.
public class ExportConsumersTests
{
    private static readonly ExportColumn[] Cols = { new("A", typeof(string)), new("B", typeof(int)) };

    private static object?[][] Rows(int n) => Enumerable.Range(0, n).Select(i => new object?[] { "r" + i, i }).ToArray();

    // ── RowBufferExportSource ────────────────────────────────────────────────
    [Fact]
    public void RowBuffer_WithoutSelection_OffersViewAndAll()
    {
        var src = new RowBufferExportSource(Cols, Rows(2), Rows(5), selectedRows: null, "buf");
        Assert.True(src.Capabilities.Supports(ExportScope.CurrentView));
        Assert.True(src.Capabilities.Supports(ExportScope.AllRows));
        Assert.False(src.Capabilities.Supports(ExportScope.SelectedRows));
        Assert.Equal(RowEstimate.Exact(2), src.Capabilities.EstimateFor(ExportScope.CurrentView));
        Assert.Equal(RowEstimate.Exact(5), src.Capabilities.EstimateFor(ExportScope.AllRows));
        Assert.Equal("buf", src.Capabilities.DefaultBaseFileName);
    }

    [Fact]
    public void RowBuffer_WithSelection_OffersSelected()
    {
        var src = new RowBufferExportSource(Cols, Rows(2), Rows(5), selectedRows: Rows(1), "buf");
        Assert.True(src.Capabilities.Supports(ExportScope.SelectedRows));
        Assert.Equal(RowEstimate.Exact(1), src.Capabilities.EstimateFor(ExportScope.SelectedRows));
    }

    [Fact]
    public async Task RowBuffer_GetRows_ReturnsThePerScopeList()
    {
        var src = new RowBufferExportSource(Cols, Rows(2), Rows(5), selectedRows: Rows(1), "buf");
        Assert.Equal(2, (await Collect(src, ExportScope.CurrentView)).Count);
        Assert.Equal(5, (await Collect(src, ExportScope.AllRows)).Count);
        Assert.Single(await Collect(src, ExportScope.SelectedRows));
    }

    // ── ServerPagedExportSource ──────────────────────────────────────────────
    [Fact]
    public async Task ServerPaged_CurrentView_YieldsTheCurrentPage()
    {
        var src = new ServerPagedExportSource(Cols, Rows(3), RowEstimate.Unknown,
            (_, _, _) => throw new InvalidOperationException("should not fetch for CurrentView"), 5, "t");
        Assert.Equal(3, (await Collect(src, ExportScope.CurrentView)).Count);
    }

    [Fact]
    public async Task ServerPaged_AllRows_PagesThroughUntilShortPage()
    {
        var pagesRequested = new List<int>();
        // total 12 rows, page size 5 → pages 1(5) + 2(5) + 3(2, short → stop)
        var src = new ServerPagedExportSource(Cols, Rows(5), RowEstimate.Exact(12),
            (page, size, _) =>
            {
                pagesRequested.Add(page);
                int start = (page - 1) * size;
                int count = Math.Max(0, Math.Min(size, 12 - start));
                return Task.FromResult<IReadOnlyList<object?[]>>(Rows(count));
            },
            5, "t");

        var all = await Collect(src, ExportScope.AllRows);
        Assert.Equal(12, all.Count);
        Assert.Equal(new[] { 1, 2, 3 }, pagesRequested);
    }

    [Fact]
    public async Task ServerPaged_AllRows_ExactMultiple_StopsOnEmptyPage()
    {
        var pagesRequested = new List<int>();
        // total 10, page size 5 → 1(5) + 2(5) + 3(0, empty short page → stop)
        var src = new ServerPagedExportSource(Cols, Rows(5), RowEstimate.Exact(10),
            (page, size, _) =>
            {
                pagesRequested.Add(page);
                int start = (page - 1) * size;
                int count = Math.Max(0, Math.Min(size, 10 - start));
                return Task.FromResult<IReadOnlyList<object?[]>>(Rows(count));
            },
            5, "t");

        Assert.Equal(10, (await Collect(src, ExportScope.AllRows)).Count);
        Assert.Equal(new[] { 1, 2, 3 }, pagesRequested);
    }

    // ── Table / View Detail BuildDataExportSource ────────────────────────────
    [Fact]
    public void TableDetail_BuildDataExportSource_MapsColumnsAndEstimate()
    {
        using var svc = new FirebirdConnectionService();
        var vm = new TableDetailTabViewModel("NAGL", new FirebirdTableDetailReader(svc), null)
        {
            DataResult = DataQueryResult(3),
            LastKnownRowCount = 42,
        };
        Assert.True(vm.CanExportData);

        var src = vm.BuildDataExportSource();
        Assert.NotNull(src);
        Assert.Equal(new[] { "A", "B" }, src!.Columns.Select(c => c.Name));
        Assert.Equal(RowEstimate.Exact(3), src.Capabilities.EstimateFor(ExportScope.CurrentView));
        Assert.Equal(RowEstimate.Exact(42), src.Capabilities.EstimateFor(ExportScope.AllRows));
        Assert.Equal("NAGL", src.Capabilities.DefaultBaseFileName);
    }

    [Fact]
    public void TableDetail_AllRowsEstimate_ApproximateAtCap_UnknownWhenNoCount()
    {
        using var svc = new FirebirdConnectionService();
        var reader = new FirebirdTableDetailReader(svc);

        var capped = new TableDetailTabViewModel("T", reader, null)
        {
            DataResult = DataQueryResult(1),
            LastKnownRowCount = TableDetailTabViewModel.RowCountCap,
        };
        Assert.Equal(RowEstimate.Approximate(TableDetailTabViewModel.RowCountCap),
            capped.BuildDataExportSource()!.Capabilities.EstimateFor(ExportScope.AllRows));

        var unknown = new TableDetailTabViewModel("T", reader, null) { DataResult = DataQueryResult(1) };
        Assert.Equal(RowEstimate.Unknown, unknown.BuildDataExportSource()!.Capabilities.EstimateFor(ExportScope.AllRows));
    }

    [Fact]
    public void TableDetail_NoReaderOrNoResult_CannotExport()
    {
        var noReader = new TableDetailTabViewModel("T") { DataResult = DataQueryResult(1) };
        Assert.False(noReader.CanExportData);
        Assert.Null(noReader.BuildDataExportSource());

        using var svc = new FirebirdConnectionService();
        var noResult = new TableDetailTabViewModel("T", new FirebirdTableDetailReader(svc), null);
        Assert.False(noResult.CanExportData);
        Assert.Null(noResult.BuildDataExportSource());
    }

    [Fact]
    public async Task TableDetail_CurrentViewScope_YieldsTheCurrentPage()
    {
        using var svc = new FirebirdConnectionService();
        var vm = new TableDetailTabViewModel("T", new FirebirdTableDetailReader(svc), null)
        {
            DataResult = DataQueryResult(4),
        };
        Assert.Equal(4, (await Collect(vm.BuildDataExportSource()!, ExportScope.CurrentView)).Count);
    }

    [Fact]
    public void ViewDetail_BuildDataExportSource_MapsColumnsAndEstimate()
    {
        using var svc = new FirebirdConnectionService();
        var vm = new ViewDetailTabViewModel("V_SALES", new FirebirdTableDetailReader(svc), null, null)
        {
            DataResult = DataQueryResult(2),
            LastKnownRowCount = 7,
        };
        Assert.True(vm.CanExportData);

        var src = vm.BuildDataExportSource();
        Assert.NotNull(src);
        Assert.Equal(new[] { "A", "B" }, src!.Columns.Select(c => c.Name));
        Assert.Equal(RowEstimate.Exact(2), src.Capabilities.EstimateFor(ExportScope.CurrentView));
        Assert.Equal(RowEstimate.Exact(7), src.Capabilities.EstimateFor(ExportScope.AllRows));
        Assert.Equal("V_SALES", src.Capabilities.DefaultBaseFileName);
    }

    [Fact]
    public void ViewDetail_NoResult_CannotExport()
    {
        using var svc = new FirebirdConnectionService();
        var vm = new ViewDetailTabViewModel("V", new FirebirdTableDetailReader(svc), null, null);
        Assert.False(vm.CanExportData);
        Assert.Null(vm.BuildDataExportSource());
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static QueryResult DataQueryResult(int rows) => new()
    {
        Columns = new[] { new QueryColumn("A", typeof(string)), new QueryColumn("B", typeof(int)) },
        Rows = Rows(rows),
    };

    private static async Task<List<object?[]>> Collect(IExportDataSource src, ExportScope scope)
    {
        var list = new List<object?[]>();
        await foreach (var r in src.GetRowsAsync(scope, CancellationToken.None)) list.Add(r);
        return list;
    }
}
