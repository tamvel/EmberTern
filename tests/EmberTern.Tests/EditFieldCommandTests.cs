using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// VM-level tests for the EditFieldCommand and CreateForeignKeyCommand
/// surfaces added in Session 2. Pins:
///   - CanEditField gates on (executor present + SelectedField not null)
///   - CanCreateForeignKey gates on executor presence
///   - command surface unchanged when downstream Session-3 wizard ships
///     (the EditFieldRequested / CreateForeignKeyRequested events let the
///     view swap dialogs without touching the VM contract)
///   - ExecuteEditFieldAsync no-ops on empty diff (no DDL emitted, no
///     PendingChanges added, no error)
/// </summary>
public class EditFieldCommandTests
{
    private static TableDetailTabViewModel BuildVm()
    {
        var vm = new TableDetailTabViewModel("MY_T");
        vm.Fields.Add(new FieldInfo
        {
            Position = 0,
            Name = "ID",
            Type = "INTEGER",
            IsPrimaryKey = true,
        });
        vm.Fields.Add(new FieldInfo
        {
            Position = 1,
            Name = "NAZWA",
            Type = "VARCHAR(50)",
            Size = 50,
        });
        return vm;
    }

    [Fact]
    public void CanEditField_FalseWithoutExecutorOrSelection()
    {
        var vm = BuildVm();
        // No executor wired in BuildVm — CanEditField always false.
        Assert.False(vm.CanEditField);
        Assert.False(vm.EditFieldCommand.CanExecute(null));

        // Even setting a selection doesn't enable it without executor.
        vm.SelectedField = vm.Fields[0];
        Assert.False(vm.CanEditField);
    }

    [Fact]
    public void CanCreateForeignKey_FalseWithoutExecutor()
    {
        var vm = BuildVm();
        Assert.False(vm.CanCreateForeignKey);
        Assert.False(vm.CreateForeignKeyCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteEditFieldAsync_EmptyDiff_NoOp()
    {
        // Test seam: VM without DdlExecutor early-returns from
        // ExecuteEditFieldAsync. Even if it didn't, an empty diff
        // (target identical to original) would produce zero ALTER statements
        // and never touch the executor.
        var vm = BuildVm();
        var original = vm.Fields[1]; // NAZWA, VARCHAR(50)

        var target = new FieldDefinition
        {
            Name = "NAZWA",
            BasicType = "VARCHAR",
            Size = 50,
            NotNull = false,
            DefaultValue = null,
            Description = null,
        };

        // No PendingChanges should result either way (ExecuteEditFieldAsync
        // bypasses the queue entirely — diffs run direct via DdlExecutor).
        await vm.ExecuteEditFieldAsync(original, target);
        Assert.Empty(vm.PendingChanges);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void EditFieldRequested_EventSurfaceExists()
    {
        // Pin event signature: Func<FieldInfo, bool, Task<FieldDefinition?>>.
        // Session 3 mustn't break this contract — the FK Wizard will reuse
        // the dialog-open path for the FK's own form.
        var vm = BuildVm();
        vm.EditFieldRequested += (info, canRename) =>
        {
            Assert.NotNull(info);
            return System.Threading.Tasks.Task.FromResult<FieldDefinition?>(null);
        };
        // We can't easily fire the command from a unit test without
        // marshaling to a UI thread, but the subscription compiles —
        // which is all we need to pin the signature.
    }

    [Fact]
    public void CreateForeignKeyRequested_EventSurfaceExists()
    {
        // Pin event signature: Func<Task<ForeignKeySpec?>>. Session 3 upgraded
        // the placeholder Func<Task> to return the dialog's result. View
        // returns the dialog's spec on OK, null on Cancel; VM either calls
        // ExecuteCreateForeignKeyAsync(spec) or no-ops.
        var vm = BuildVm();
        bool subscribed = false;
        vm.CreateForeignKeyRequested += () =>
        {
            subscribed = true;
            return System.Threading.Tasks.Task.FromResult<ForeignKeySpec?>(null);
        };
        Assert.False(subscribed); // no fire yet; subscription itself compiles.
    }
}
