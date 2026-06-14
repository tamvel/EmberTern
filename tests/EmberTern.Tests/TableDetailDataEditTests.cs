using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

public class TableDetailDataEditTests
{
    private static QueryResult MakeResult()
    {
        var columns = new[]
        {
            new QueryColumn("ID", typeof(int)),
            new QueryColumn("NAME", typeof(string)),
        };
        var rows = new[]
        {
            new object?[] { 1, "Alice" },
            new object?[] { 2, "Bob" },
        };
        return new QueryResult { Columns = columns, Rows = rows };
    }

    private static TableDetailTabViewModel BuildVmWithFieldsAndData(params string[] pkColumns)
    {
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", IsPrimaryKey = pkColumns.Length > 0 && System.Array.IndexOf(pkColumns, "ID") >= 0 });
        vm.Fields.Add(new FieldInfo { Position = 1, Name = "NAME" });
        vm.RefreshPrimaryKeyColumns();
        vm.DataResult = MakeResult();
        return vm;
    }

    [Fact]
    public void DefaultState_NoEditor_CannotEditData()
    {
        var vm = new TableDetailTabViewModel("T");
        Assert.False(vm.CanEditData);
        Assert.True(vm.IsDataReadOnly);
        Assert.False(vm.AddRowCommand.CanExecute(null));
    }

    [Fact]
    public void RefreshPrimaryKeyColumns_DerivedFromFields()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", IsPrimaryKey = true });
        vm.Fields.Add(new FieldInfo { Position = 1, Name = "NAME" });
        vm.Fields.Add(new FieldInfo { Position = 2, Name = "EXTRA", IsPrimaryKey = true });
        vm.RefreshPrimaryKeyColumns();
        Assert.Equal(new[] { "ID", "EXTRA" }, vm.PrimaryKeyColumns);
        Assert.True(vm.HasPrimaryKey);
    }

    [Fact]
    public void NoPrimaryKey_HasPrimaryKeyFalse_HintShown()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "NAME" });
        vm.RefreshPrimaryKeyColumns();
        Assert.False(vm.HasPrimaryKey);
        Assert.Equal(UiStrings.DataEditNoPrimaryKeyHint, vm.EditModeHint);
    }

    [Fact]
    public void DataResultAssignment_PopulatesEditableRows()
    {
        var vm = BuildVmWithFieldsAndData("ID");
        Assert.Equal(2, vm.EditableRows.Count);
        Assert.Equal(1, vm.EditableRows[0][0]);
        Assert.Equal("Bob", vm.EditableRows[1][1]);
    }

    [Fact]
    public void DataResultAssignment_BuildsColumnIndex()
    {
        var vm = BuildVmWithFieldsAndData("ID");
        Assert.Equal(0, vm.ColumnIndex["ID"]);
        Assert.Equal(1, vm.ColumnIndex["NAME"]);
        // Case-insensitive.
        Assert.Equal(0, vm.ColumnIndex["id"]);
    }

    [Fact]
    public void DataResultAssignment_NoPk_NoSnapshots()
    {
        // No PK columns — Editor cannot do UPDATE/DELETE, only INSERT (the spec).
        var vm = BuildVmWithFieldsAndData();
        Assert.Empty(vm.PrimaryKeyColumns);
        Assert.Equal(2, vm.EditableRows.Count);
        // CanDeleteRow gates on PK + editor; without an editor wired up the
        // command is disabled in all cases.
        Assert.False(vm.CanDeleteRow);
    }

    [Fact]
    public void AddRow_NoEditor_CommandDisabled()
    {
        var vm = BuildVmWithFieldsAndData("ID");
        Assert.False(vm.CanAddRow);
        Assert.False(vm.AddRowCommand.CanExecute(null));
    }

    [Fact]
    public void DataResultReassignment_ClearsEditableRowsAndSnapshots()
    {
        var vm = BuildVmWithFieldsAndData("ID");
        Assert.Equal(2, vm.EditableRows.Count);

        // Reassigning DataResult to a new instance must rebuild — no stale rows.
        vm.DataResult = new QueryResult
        {
            Columns = new[] { new QueryColumn("ID", typeof(int)) },
            Rows = new[] { new object?[] { 42 } },
        };
        Assert.Single(vm.EditableRows);
        Assert.Equal(42, vm.EditableRows[0][0]);
    }

    [Fact]
    public void DataResult_NullOrEmpty_ClearsEditableRows()
    {
        var vm = BuildVmWithFieldsAndData("ID");
        Assert.Equal(2, vm.EditableRows.Count);
        vm.DataResult = null;
        Assert.Empty(vm.EditableRows);
    }

    [Fact]
    public async Task UpdateCellAsync_NoEditor_NoOp()
    {
        var vm = BuildVmWithFieldsAndData("ID");
        var row = vm.EditableRows[0];
        // Without an editor wired up this is a no-op (returns silently).
        await vm.UpdateCellAsync(row, 1, "Charlie");
        // Local mutation did NOT happen — we don't write through the row when
        // there's no editor, because then there'd be no way to commit anyway.
        Assert.Equal("Alice", row[1]);
    }

    [Fact]
    public void BuildKeyValuePairs_PairsByIndex()
    {
        var pairs = TableDetailTabViewModel.BuildKeyValuePairs(
            new[] { "ID", "REGION" },
            new object?[] { 1, "PL" });
        Assert.Equal(2, pairs.Count);
        Assert.Equal("ID", pairs[0].Key);
        Assert.Equal(1, pairs[0].Value);
        Assert.Equal("REGION", pairs[1].Key);
        Assert.Equal("PL", pairs[1].Value);
    }

    [Fact]
    public void IsNewRow_TrueAfterAddRow_FalseForExisting()
    {
        var vm = BuildVmWithFieldsAndData("ID");
        var existing = vm.EditableRows[0];
        // AddRow with no editor is a no-op (CanAddRow=false). To test the IsNewRow
        // path in isolation, just confirm the API returns false for existing rows
        // and that an unknown reference also returns false.
        Assert.False(vm.IsNewRow(existing));
        Assert.False(vm.IsNewRow(new object?[] { 99, "X" }));
    }

    [Fact]
    public void RebuildEditableRows_RefreshesPrimaryKeyFromFields()
    {
        // If Fields is populated but RefreshPrimaryKeyColumns was missed (e.g.
        // a refresh path bypassed LoadAsync), RebuildEditableRows must derive
        // PK on its own — otherwise the edit hint would falsely say "no PK".
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", IsPrimaryKey = true });
        vm.Fields.Add(new FieldInfo { Position = 1, Name = "NAME" });
        // Deliberately skip the explicit RefreshPrimaryKeyColumns call here.
        vm.DataResult = MakeResult();
        Assert.True(vm.HasPrimaryKey);
        Assert.Equal(new[] { "ID" }, vm.PrimaryKeyColumns);
        Assert.Equal(string.Empty, vm.EditModeHint);
    }

    [Fact]
    public void IsDataReadOnly_NoEditor_IsTrue()
    {
        var vm = new TableDetailTabViewModel("T");
        Assert.True(vm.IsDataReadOnly);
        Assert.False(vm.CanEditData);
    }

    // ─── PK detection from the authoritative PRIMARY KEY constraint ───────

    [Fact]
    public void PrimaryKeyColumnsFromConstraints_ReturnsPkConstraintFields()
    {
        var pk = TableDetailTabViewModel.PrimaryKeyColumnsFromConstraints(new[]
        {
            new ConstraintInfo { Name = "FK_X", ConstraintType = "FOREIGN KEY", Fields = "A" },
            new ConstraintInfo { Name = "PK_T", ConstraintType = "PRIMARY KEY", Fields = "ID, REGION" },
        });
        Assert.Equal(new[] { "ID", "REGION" }, pk);
    }

    [Fact]
    public void PrimaryKeyColumnsFromConstraints_NoPk_ReturnsEmpty()
    {
        var pk = TableDetailTabViewModel.PrimaryKeyColumnsFromConstraints(new[]
        {
            new ConstraintInfo { Name = "U", ConstraintType = "UNIQUE", Fields = "X" },
        });
        Assert.Empty(pk);
    }

    [Fact]
    public void RefreshPrimaryKeyColumns_DerivesFromConstraint_WhenFieldFlagMissing()
    {
        // The exact reported bug: FieldsSql's per-field PK flag missed the PK
        // (IsPrimaryKey=false on every field) yet the table HAS a PRIMARY KEY
        // constraint. PK detection must still succeed — otherwise the table is
        // wrongly stuck in "only INSERT available" while IBExpert allows UPDATE.
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", IsPrimaryKey = false });
        vm.Fields.Add(new FieldInfo { Position = 1, Name = "NAME" });
        vm.Constraints.Add(new ConstraintInfo { Name = "PK_T", ConstraintType = "PRIMARY KEY", Fields = "ID" });

        vm.RefreshPrimaryKeyColumns();

        Assert.True(vm.HasPrimaryKey);
        Assert.Equal(new[] { "ID" }, vm.PrimaryKeyColumns);
        Assert.Equal(string.Empty, vm.EditModeHint);
    }

    [Fact]
    public void RefreshPrimaryKeyColumns_FallsBackToFieldFlag_WhenNoConstraintLoaded()
    {
        // Before the Constraints step loads (or when the catalog truly only carries
        // the per-field flag), fall back to FieldInfo.IsPrimaryKey.
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", IsPrimaryKey = true });
        vm.RefreshPrimaryKeyColumns();
        Assert.True(vm.HasPrimaryKey);
        Assert.Equal(new[] { "ID" }, vm.PrimaryKeyColumns);
    }
}
