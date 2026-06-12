using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

public class InlineFieldEditTests
{
    private static (TableDetailTabViewModel vm, FieldRowViewModel row) BuildVmWithSingleField()
    {
        var vm = new TableDetailTabViewModel("MY_TABLE");
        vm.Fields.Add(new FieldInfo
        {
            Position = 0,
            Name = "OLD_NAME",
            Type = "INTEGER",
            NotNull = false,
            DefaultValue = null,
            Description = null,
        });
        // EditableFields mirrors Fields via the CollectionChanged hook.
        var row = vm.EditableFields[0];
        return (vm, row);
    }

    [Fact]
    public void NewVm_EditableFieldsMirrorsFields()
    {
        var (vm, row) = BuildVmWithSingleField();
        Assert.Equal("OLD_NAME", row.Name);
        Assert.Equal("INTEGER", row.TypeText);
        Assert.Single(vm.EditableFields);
    }

    [Fact]
    public void Rename_NoDeps_QueuesAlterTo()
    {
        var (vm, row) = BuildVmWithSingleField();
        row.Name = "NEW_NAME";
        vm.EnqueueRowEdits(row);

        var pending = Assert.Single(vm.PendingChanges);
        Assert.Contains("ALTER TABLE \"MY_TABLE\" ALTER \"OLD_NAME\" TO \"NEW_NAME\"", pending.Sql);
    }

    [Fact]
    public void Rename_WithIncomingDeps_IsBlocked_AndReverted()
    {
        var (vm, row) = BuildVmWithSingleField();
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TRIG_X", ObjectType = "Trigger", FieldName = "OLD_NAME" });
        row.Name = "NEW_NAME";
        vm.EnqueueRowEdits(row);

        Assert.Empty(vm.PendingChanges);
        Assert.Equal("OLD_NAME", row.Name); // reverted
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public void NotNull_Toggle_QueuesSetThenDrop()
    {
        var (vm, row) = BuildVmWithSingleField();
        row.NotNull = true;
        vm.EnqueueRowEdits(row);
        Assert.Contains("SET NOT NULL", vm.PendingChanges[^1].Sql);
    }

    [Fact]
    public void Default_SetFromNull_QueuesSetDefault()
    {
        var (vm, row) = BuildVmWithSingleField();
        row.DefaultValue = "0";
        vm.EnqueueRowEdits(row);
        Assert.Contains("SET DEFAULT 0", vm.PendingChanges[^1].Sql);
    }

    [Fact]
    public void Default_DropFromExisting_QueuesDropDefault()
    {
        var vm = new TableDetailTabViewModel("MY_TABLE");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "F", Type = "INTEGER", DefaultValue = "0" });
        var row = vm.EditableFields[0];
        row.DefaultValue = string.Empty;
        vm.EnqueueRowEdits(row);
        Assert.Contains("DROP DEFAULT", vm.PendingChanges[^1].Sql);
    }

    [Fact]
    public void Type_Change_QueuesAlterType()
    {
        var (vm, row) = BuildVmWithSingleField();
        row.TypeText = "BIGINT";
        vm.EnqueueRowEdits(row);
        Assert.Contains("ALTER TABLE \"MY_TABLE\" ALTER \"OLD_NAME\" TYPE BIGINT", vm.PendingChanges[^1].Sql);
    }

    [Fact]
    public void Description_Change_QueuesCommentOnColumn()
    {
        var (vm, row) = BuildVmWithSingleField();
        row.Description = "primary identifier";
        vm.EnqueueRowEdits(row);
        Assert.Contains("COMMENT ON COLUMN \"MY_TABLE\".\"OLD_NAME\" IS 'primary identifier'", vm.PendingChanges[^1].Sql);
    }

    [Fact]
    public void IsModified_FlipsOnEdit_RestoredOnReset()
    {
        var (vm, row) = BuildVmWithSingleField();
        Assert.False(row.IsModified);
        row.NotNull = true;
        Assert.True(row.IsModified);
        row.NotNull = false;
        Assert.False(row.IsModified);
    }

    [Fact]
    public void CanRenameField_FalseWhenDepReferencesIt()
    {
        var (vm, _) = BuildVmWithSingleField();
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TRIG_X", ObjectType = "Trigger", FieldName = "OLD_NAME" });
        Assert.False(vm.CanRenameField("OLD_NAME"));
        Assert.True(vm.CanRenameField("OTHER_FIELD"));
    }
}
