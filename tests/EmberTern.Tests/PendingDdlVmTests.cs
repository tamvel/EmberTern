using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

public class PendingDdlVmTests
{
    private static TableDetailTabViewModel BuildVm()
    {
        var vm = new TableDetailTabViewModel("MY_TABLE");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", IsPrimaryKey = true });
        vm.Fields.Add(new FieldInfo { Position = 1, Name = "NAZWA" });
        vm.Fields.Add(new FieldInfo { Position = 2, Name = "OPIS" });
        return vm;
    }

    [Fact]
    public void NewVm_HasNoPendingChanges()
    {
        var vm = BuildVm();
        Assert.Empty(vm.PendingChanges);
        Assert.False(vm.HasPendingChanges);
        Assert.False(vm.CanCompile); // no executor wired
    }

    [Fact]
    public void AddPendingAddField_AppendsAddDdl()
    {
        var vm = BuildVm();
        vm.AddPendingAddField(new FieldDefinition
        {
            Name = "NEW_COL",
            BasicType = "INTEGER",
        });
        Assert.Single(vm.PendingChanges);
        Assert.Equal(PendingDdlChangeKind.AddField, vm.PendingChanges[0].Kind);
        Assert.Contains("ALTER TABLE \"MY_TABLE\" ADD \"NEW_COL\" INTEGER", vm.PendingChanges[0].Sql);
        Assert.True(vm.HasPendingChanges);
    }

    [Fact]
    public void DdlWithPendingPreview_EmptyWithoutPending()
    {
        var vm = BuildVm();
        vm.DdlText = "CREATE TABLE FOO ()";
        Assert.Equal("CREATE TABLE FOO ()", vm.DdlWithPendingPreview);
    }

    [Fact]
    public void DdlWithPendingPreview_AppendsCommentAndStatements()
    {
        var vm = BuildVm();
        vm.DdlText = "CREATE TABLE FOO ()";
        vm.AddPendingAddField(new FieldDefinition { Name = "NEW_COL", BasicType = "INTEGER" });

        var preview = vm.DdlWithPendingPreview;
        Assert.Contains("CREATE TABLE FOO ()", preview);
        Assert.Contains("-- Pending changes:", preview);
        Assert.Contains("ADD \"NEW_COL\"", preview);
    }

    [Fact]
    public void MoveFieldUp_QueuesAlterStatement()
    {
        var vm = BuildVm();
        // Select OPIS (index 2) — Move Up means new pos 2 (1-based)
        vm.SelectedField = vm.Fields[2];
        vm.MoveFieldUpCommand.Execute(null);

        Assert.Single(vm.PendingChanges);
        Assert.Equal(PendingDdlChangeKind.MoveField, vm.PendingChanges[0].Kind);
        Assert.Contains("ALTER \"OPIS\" POSITION 2", vm.PendingChanges[0].Sql);
    }

    [Fact]
    public void MoveFieldDown_QueuesAlterStatement()
    {
        var vm = BuildVm();
        // Select ID (index 0) — Move Down means new pos 2.
        vm.SelectedField = vm.Fields[0];
        vm.MoveFieldDownCommand.Execute(null);

        Assert.Single(vm.PendingChanges);
        Assert.Contains("ALTER \"ID\" POSITION 2", vm.PendingChanges[0].Sql);
    }

    [Fact]
    public void MoveFieldUp_OnFirstField_IsNoOp()
    {
        var vm = BuildVm();
        vm.SelectedField = vm.Fields[0];
        Assert.False(vm.CanMoveFieldUp || vm.MoveFieldUpCommand.CanExecute(null));
        Assert.Empty(vm.PendingChanges);
    }

    [Fact]
    public void MoveFieldDown_OnLastField_IsNoOp()
    {
        var vm = BuildVm();
        vm.SelectedField = vm.Fields[^1];
        Assert.False(vm.CanMoveFieldDown || vm.MoveFieldDownCommand.CanExecute(null));
        Assert.Empty(vm.PendingChanges);
    }

    [Fact]
    public void HasPendingChanges_Notification_TracksCollection()
    {
        var vm = BuildVm();
        var notified = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TableDetailTabViewModel.HasPendingChanges)) notified++; };
        vm.AddPendingAddField(new FieldDefinition { Name = "X", BasicType = "INTEGER" });
        Assert.True(notified >= 1);
    }
}
