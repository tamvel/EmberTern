using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Query;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Faza 4 + 5 — host wiring of the shared filter panel + aggregation bar into the
// five data grids. The materialized grids (SQL / Procedure / Function results) run
// filter + aggregate fully in-memory, so they're end-to-end testable here. The
// server-paged grids (Table / View Data) push filter + aggregate to SQL (needs a
// live FB), so only their in-memory pieces are exercised: column sync, filter-active
// state, and the "Record N of M" formatting.
public class GridFilterHostWiringTests
{
    private static QueryResult Numbers(int count)
    {
        var rows = new object?[count][];
        for (int i = 0; i < count; i++) rows[i] = new object?[] { i };
        return new QueryResult { Columns = new[] { new QueryColumn("N", typeof(int)) }, Rows = rows };
    }

    private static void AddCondition(FilterPanelViewModel panel, int columnIndex, GridFilterOperator op, string? value)
    {
        panel.AddConditionCommand.Execute(null);
        var row = panel.Conditions[^1];
        row.SelectedColumn = row.Columns[columnIndex];
        row.SelectedOperator = row.AvailableOperators.First(o => o.Operator == op);
        row.Value = value;
    }

    // ── SQL Results (materialized) ────────────────────────────────────────────
    [Fact]
    public async Task SqlResults_Filter_NarrowsRowsAndRecordInfo()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Numbers(5); // 0..4

        AddCondition(h.Main.ResultFilterPanel, 0, GridFilterOperator.GreaterThan, "2");
        await h.Main.ResultFilterPanel.ApplyCommand.ExecuteAsync(null);

        // Only 3, 4 survive.
        Assert.Equal(2, h.Main.PagedResultRows.Count);
        Assert.All(h.Main.PagedResultRows, r => Assert.True((int)r[0]! > 2));
        Assert.Equal("2 rows", h.Main.ResultRecordInfo);
        Assert.True(h.Main.ResultFilterPanel.IsFilterActive);
    }

    [Fact]
    public async Task SqlResults_Aggregate_ComputesOverFilteredRows()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Numbers(5); // 0..4

        AddCondition(h.Main.ResultFilterPanel, 0, GridFilterOperator.GreaterOrEqual, "2");
        await h.Main.ResultFilterPanel.ApplyCommand.ExecuteAsync(null);

        await h.Main.ResultAggregationBar.AddLineCommand.ExecuteAsync(null);
        var line = h.Main.ResultAggregationBar.Lines[0];
        line.SelectedFunction = line.AvailableFunctions.First(f => f.Aggregate == GridAggregate.Sum);
        // Auto-recompute on function change; 2+3+4 over the filtered set.
        Assert.Equal("9", line.ResultText);
    }

    [Fact]
    public async Task SqlResults_NewResult_ResetsFilter()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Numbers(5);
        AddCondition(h.Main.ResultFilterPanel, 0, GridFilterOperator.GreaterThan, "2");
        await h.Main.ResultFilterPanel.ApplyCommand.ExecuteAsync(null);
        Assert.True(h.Main.ResultFilterPanel.IsFilterActive);

        h.Main.CurrentResult = Numbers(3); // new result set
        Assert.Empty(h.Main.ResultFilterPanel.Conditions);
        Assert.False(h.Main.ResultFilterPanel.IsFilterActive);
        Assert.Equal(3, h.Main.PagedResultRows.Count);
    }

    // ── Procedure Results (materialized) ──────────────────────────────────────
    [Fact]
    public async Task ProcedureResults_Filter_NarrowsRowsAndAggregate()
    {
        var vm = new ProcedureDetailTabViewModel("P");
        vm.ExecResult = Numbers(6); // 0..5

        AddCondition(vm.ExecFilterPanel, 0, GridFilterOperator.LessThan, "3");
        await vm.ExecFilterPanel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.PagedExecRows.Count); // 0,1,2
        Assert.Equal("3 rows", vm.ExecRecordInfo);

        await vm.ExecAggregationBar.AddLineCommand.ExecuteAsync(null);
        var line = vm.ExecAggregationBar.Lines[0];
        line.SelectedFunction = line.AvailableFunctions.First(f => f.Aggregate == GridAggregate.Sum);
        Assert.Equal("3", line.ResultText); // 0+1+2
    }

    // ── Function Results (materialized) ───────────────────────────────────────
    [Fact]
    public async Task FunctionResults_Filter_NarrowsRows()
    {
        var vm = new FunctionDetailTabViewModel("F");
        vm.ExecResult = Numbers(4); // 0..3

        AddCondition(vm.ExecFilterPanel, 0, GridFilterOperator.GreaterOrEqual, "2");
        await vm.ExecFilterPanel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.PagedExecRows.Count); // 2,3
        Assert.Equal("2 rows", vm.ExecRecordInfo);
    }

    // ── Table Data (server-paged) — in-memory pieces ──────────────────────────
    [Fact]
    public void TableData_SetResult_PopulatesFilterColumns()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.DataResult = Numbers(3);
        Assert.Single(vm.DataFilterPanel.Columns);
        Assert.Equal("N", vm.DataFilterPanel.Columns[0].Name);
    }

    [Fact]
    public void TableData_RecordInfo_FormatsWithRowCount()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.DataResult = Numbers(200);
        vm.LastKnownRowCount = 1000;

        Assert.Equal("1000 rows", vm.DataRecordInfo); // no selection
        vm.SetDataSelectedRow(3);
        Assert.Equal("Record 4 of 1000", vm.DataRecordInfo);
    }

    [Fact]
    public void TableData_RecordInfo_MarksCappedCount()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.DataResult = Numbers(200);
        vm.LastKnownRowCount = TableDetailTabViewModel.RowCountCap;
        Assert.Equal($"{TableDetailTabViewModel.RowCountCap}+ rows", vm.DataRecordInfo);
    }

    [Fact]
    public void TableData_SameColumns_KeepsConditions()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.DataResult = Numbers(3);
        AddCondition(vm.DataFilterPanel, 0, GridFilterOperator.GreaterThan, "1");
        Assert.Single(vm.DataFilterPanel.Conditions);

        // A same-column reload (e.g. a filtered re-fetch) must NOT wipe the conditions.
        vm.DataResult = Numbers(5);
        Assert.Single(vm.DataFilterPanel.Conditions);
    }

    // ── View Data (server-paged) — in-memory pieces ───────────────────────────
    [Fact]
    public void ViewData_SetResult_PopulatesColumns_AndRecordInfo()
    {
        var vm = new ViewDetailTabViewModel("V");
        vm.DataResult = Numbers(200);
        Assert.Single(vm.DataFilterPanel.Columns);

        vm.LastKnownRowCount = 500;
        vm.SetDataSelectedRow(0);
        Assert.Equal("Record 1 of 500", vm.DataRecordInfo);
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
