using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

// View Detail Easy mode (mirrors the Procedure Detail Source/Easy toggle): the
// SQL tab splits into an editable name-only column list + an AS-SELECT body editor;
// Source mode is the full statement. These drive the VM directly (no readers).
public class ViewEasyModeTests
{
    private static ViewDetailTabViewModel ExistingView(string name = "V_TEST")
        => new(name);

    [Fact]
    public void New_View_CanUseEasyMode_StartsInEasyWithEditableName()
    {
        // Approved target design: a new view CAN use Easy mode and starts there.
        var vm = new ViewDetailTabViewModel("NEW_VIEW")
        {
            IsNew = true,
            SourceText = ViewDetailTabViewModel.NewViewTemplate,
        };
        Assert.True(vm.CanUseEasyMode);

        vm.EasyMode = true; // New View starts in Easy (set by the New View flow)
        Assert.True(vm.EasyMode);
        Assert.Null(vm.ErrorMessage);
        // Template parsed into the editable name + column list + body.
        Assert.Equal("NEW_VIEW", vm.EditableViewName);
        Assert.NotEmpty(vm.Columns);

        // Editing the name flows into the compiled SQL (dirty/compile #3).
        vm.EditableViewName = "CUSTOMER_VIEW";
        Assert.Contains("CUSTOMER_VIEW", vm.BuildCompileSql());
    }

    [Fact]
    public void New_View_Easy_EditsFlowIntoCompileSql()
    {
        var vm = new ViewDetailTabViewModel("NEW_VIEW")
        {
            IsNew = true,
            SourceText = ViewDetailTabViewModel.NewViewTemplate,
        };
        vm.EasyMode = true;

        // Name edit.
        vm.EditableViewName = "ORDERS_V";
        // Column add.
        vm.AddColumnCommand.Execute(null);
        vm.Columns[^1].Name = "TOTAL";
        // Body edit.
        vm.EditableBody = "SELECT id, total FROM orders";

        var sql = vm.BuildCompileSql();
        Assert.Contains("ORDERS_V", sql);
        Assert.Contains("TOTAL", sql);
        Assert.Contains("SELECT id, total FROM orders", sql);

        // Column remove is reflected too.
        vm.SelectedColumn = vm.Columns[^1];
        vm.DeleteColumnCommand.Execute(null);
        Assert.DoesNotContain("TOTAL", vm.BuildCompileSql());
    }

    [Fact]
    public void Existing_View_DefaultsToSourceMode()
    {
        var vm = ExistingView();
        Assert.True(vm.CanUseEasyMode);
        Assert.False(vm.EasyMode);
        Assert.True(vm.IsSourceMode);
    }

    [Fact]
    public void ToggleToEasy_ParsesSourceIntoColumnsAndBody()
    {
        var vm = ExistingView();
        vm.SourceText = "CREATE OR ALTER VIEW V_TEST (ID, NAME) AS SELECT id, name FROM t";

        vm.EasyMode = true;

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(new[] { "ID", "NAME" }, System.Linq.Enumerable.Select(vm.Columns, c => c.Name));
        Assert.Equal("SELECT id, name FROM t", vm.EditableBody);
    }

    [Fact]
    public void ToggleToEasy_NoColumnList_LeavesColumnsEmpty()
    {
        var vm = ExistingView();
        vm.SourceText = "CREATE VIEW V_TEST AS SELECT 1 AS X FROM RDB$DATABASE";

        vm.EasyMode = true;

        Assert.Empty(vm.Columns);
        Assert.Equal("SELECT 1 AS X FROM RDB$DATABASE", vm.EditableBody);
    }

    [Fact]
    public void ToggleBack_RebuildsSourceFromModel()
    {
        var vm = ExistingView();
        vm.SourceText = "CREATE OR ALTER VIEW V_TEST (ID, NAME) AS SELECT id, name FROM t";

        vm.EasyMode = true;
        vm.EasyMode = false; // Easy → Source

        var sig = ViewSignatureParser.Parse(vm.SourceText);
        Assert.True(sig.Success);
        Assert.Equal("V_TEST", sig.Name);
        Assert.True(sig.OrAlter);
        Assert.Equal(new[] { "ID", "NAME" }, sig.Columns);
        Assert.Equal("SELECT id, name FROM t", sig.Body);
    }

    [Theory]
    [InlineData("CREATE VIEW V_TEST AS SELECT 1 AS X FROM RDB$DATABASE")]
    [InlineData("CREATE VIEW V_TEST (ID, NAME) AS SELECT id, name FROM t")]
    [InlineData("CREATE OR ALTER VIEW V_TEST (A) AS SELECT a FROM t")]
    public void RoundTrip_ThroughVm_PreservesDefinition(string source)
    {
        var vm = ExistingView();
        vm.SourceText = source;

        vm.EasyMode = true;   // Source → Easy
        vm.EasyMode = false;  // Easy → Source

        var before = ViewSignatureParser.Parse(source);
        var after = ViewSignatureParser.Parse(vm.SourceText);
        Assert.True(after.Success);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(before.OrAlter, after.OrAlter);
        Assert.Equal(before.Columns, after.Columns);
        Assert.Equal(before.Body, after.Body);
    }

    [Fact]
    public void ToggleToEasy_UnparseableSource_KeepsModelAndNotifies()
    {
        var vm = ExistingView();
        vm.SourceText = "this is not a view definition";

        vm.EasyMode = true;

        Assert.Equal(UiStrings.ViewParseFailedNotice, vm.ErrorMessage);
        Assert.Empty(vm.Columns); // last-good model untouched
    }

    [Fact]
    public void BuildCompileSql_UsesSourceOrEasyByMode()
    {
        var vm = ExistingView();
        vm.SourceText = "CREATE OR ALTER VIEW V_TEST (ID) AS SELECT id FROM t";

        Assert.Equal(vm.SourceText, vm.BuildCompileSql()); // Source mode → raw text

        vm.EasyMode = true;
        var easySql = vm.BuildCompileSql();
        Assert.Contains("CREATE OR ALTER VIEW", easySql);
        Assert.Contains("ID", easySql);
        Assert.Contains("SELECT id FROM t", easySql);
    }

    // ─── Column commands ───────────────────────────────────────────────────

    [Fact]
    public void AddColumn_AppendsAndSelects()
    {
        var vm = ExistingView();
        vm.AddColumnCommand.Execute(null);
        vm.AddColumnCommand.Execute(null);

        Assert.Equal(2, vm.Columns.Count);
        Assert.Equal("COLUMN_1", vm.Columns[0].Name);
        Assert.Equal("COLUMN_2", vm.Columns[1].Name);
        Assert.Same(vm.Columns[1], vm.SelectedColumn);
    }

    [Fact]
    public void DeleteColumn_RemovesSelectedAndPicksNeighbour()
    {
        var vm = ExistingView();
        vm.AddColumnCommand.Execute(null);
        vm.AddColumnCommand.Execute(null);
        vm.SelectedColumn = vm.Columns[0];

        vm.DeleteColumnCommand.Execute(null);

        Assert.Single(vm.Columns);
        Assert.Equal("COLUMN_2", vm.Columns[0].Name);
        Assert.Same(vm.Columns[0], vm.SelectedColumn);
    }

    [Fact]
    public void MoveColumn_ReordersList()
    {
        var vm = ExistingView();
        vm.AddColumnCommand.Execute(null);
        vm.AddColumnCommand.Execute(null);
        vm.SelectedColumn = vm.Columns[1];

        vm.MoveColumnUpCommand.Execute(null);
        Assert.Equal(new[] { "COLUMN_2", "COLUMN_1" }, System.Linq.Enumerable.Select(vm.Columns, c => c.Name));

        vm.MoveColumnDownCommand.Execute(null);
        Assert.Equal(new[] { "COLUMN_1", "COLUMN_2" }, System.Linq.Enumerable.Select(vm.Columns, c => c.Name));
    }

    [Fact]
    public void ColumnName_FoldsToUpperOnEdit()
    {
        var col = new ViewColumnRowViewModel("ID");
        col.Name = "total_value";
        Assert.Equal("TOTAL_VALUE", col.Name);
    }
}
