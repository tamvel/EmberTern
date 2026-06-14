using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// "Set NULL" cell context-menu (Dane tab). The action routes through the SAME
/// <see cref="TableDetailTabViewModel.UpdateCellAsync"/> path a manual edit uses —
/// these tests pin the nullability gate, the no-editor / not-nullable no-ops, and
/// that a nullable cell actually goes through the update mechanism. The disconnected
/// editor exercises the error branch (a live success path needs a real Firebird).
/// </summary>
public class TableDetailSetNullTests
{
    private static QueryResult Make4ColResult() => new()
    {
        Columns = new[]
        {
            new QueryColumn("ID", typeof(int)),
            new QueryColumn("NAME", typeof(string)),
            new QueryColumn("NOTE", typeof(string)),
            new QueryColumn("CALC", typeof(int)),
        },
        Rows = new[] { new object?[] { 1, "Alice", "hi", 7 } },
    };

    private static TableDetailTabViewModel BuildVm(FirebirdDataEditor? editor = null)
    {
        var vm = new TableDetailTabViewModel("T", null, null, editor, null, null);
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", IsPrimaryKey = true, NotNull = true });
        vm.Fields.Add(new FieldInfo { Position = 1, Name = "NAME" });                              // nullable
        vm.Fields.Add(new FieldInfo { Position = 2, Name = "NOTE", NotNull = true });              // NOT NULL
        vm.Fields.Add(new FieldInfo { Position = 3, Name = "CALC", ComputedSource = "1 + 1" });    // computed
        vm.RefreshPrimaryKeyColumns();
        vm.DataResult = Make4ColResult();
        return vm;
    }

    [Fact]
    public void IsColumnNullable_OnlyForNullableNonPkNonComputedColumns()
    {
        var vm = BuildVm();
        Assert.False(vm.IsColumnNullable(0)); // ID — primary key
        Assert.True(vm.IsColumnNullable(1));  // NAME — nullable
        Assert.False(vm.IsColumnNullable(2)); // NOTE — NOT NULL
        Assert.False(vm.IsColumnNullable(3)); // CALC — computed
        Assert.False(vm.IsColumnNullable(99)); // out of range
        Assert.False(vm.IsColumnNullable(-1));
    }

    [Fact]
    public async Task SetCellNullAsync_NoEditor_NoOp()
    {
        var vm = BuildVm(); // no editor
        var row = vm.EditableRows[0];
        await vm.SetCellNullAsync(row, 1);
        Assert.Equal("Alice", row[1]); // unchanged — nothing to commit through
    }

    [Fact]
    public async Task SetCellNullAsync_NotNullColumn_NoOp()
    {
        using var service = new FirebirdConnectionService();
        var editor = new FirebirdDataEditor(service, new TransactionService(service));
        var vm = BuildVm(editor);
        var row = vm.EditableRows[0];

        // NOTE (index 2) is NOT NULL — the guard blocks before any update.
        await vm.SetCellNullAsync(row, 2);

        Assert.Equal("hi", row[2]);
        Assert.False(vm.HasEditStatusMessage);
    }

    [Fact]
    public async Task SetCellNullAsync_NullableColumn_RoutesThroughUpdateMechanism()
    {
        using var service = new FirebirdConnectionService();
        var editor = new FirebirdDataEditor(service, new TransactionService(service));
        var vm = BuildVm(editor);
        var row = vm.EditableRows[0];

        // NAME (index 1) is nullable → goes through UpdateCellAsync → the (disconnected)
        // editor fails → the value is reverted and an edit-status error is surfaced.
        // The surfaced error proves we used the real update path, not a separate one.
        await vm.SetCellNullAsync(row, 1);

        Assert.True(vm.HasEditStatusMessage);
        Assert.Equal("Alice", vm.EditableRows[0][1]); // reverted (clone carries the original)
    }

    [Fact]
    public void DataGrid_CellPointerPressedEventArgs_ExposesRowAndColumn()
    {
        // "Set NULL" resolves the right-clicked cell via the DataGrid.CellPointerPressed
        // event (public Row + Column on the args) instead of internal-member reflection.
        // Pin those members so an Avalonia upgrade that renames them fails loudly here
        // rather than silently leaving the menu item disabled.
        var argsType = typeof(Avalonia.Controls.DataGrid).Assembly
            .GetType("Avalonia.Controls.DataGridCellPointerPressedEventArgs");
        Assert.NotNull(argsType);

        var column = argsType!.GetProperty("Column");
        var row = argsType.GetProperty("Row");
        var pointerArgs = argsType.GetProperty("PointerPressedEventArgs");
        Assert.NotNull(column);
        Assert.NotNull(row);
        Assert.NotNull(pointerArgs);
        Assert.True(typeof(Avalonia.Controls.DataGridColumn).IsAssignableFrom(column!.PropertyType));

        var evt = typeof(Avalonia.Controls.DataGrid).GetEvent("CellPointerPressed");
        Assert.NotNull(evt);
    }
}
