using System;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The "Revert / Discard changes" button next to Compile on every object editor
/// (Table designer + View / Procedure / Trigger source editors), with a
/// confirmation so an accidental click never throws away uncompiled work.
///
/// View / Procedure / Trigger revert = reload from the database (RefreshAsync),
/// available only for an EXISTING object with edits (IsDirty &amp;&amp; !IsNew). The
/// actual reload needs a live FB, so these tests pin the enabled-state gate +
/// that the confirmation is raised with a destructive, named request. The Table
/// discard is purely in-memory, so its confirm/cancel effect is fully observable.
/// </summary>
public class RevertChangesTests
{
    // ─── View ──────────────────────────────────────────────────────────────

    [Fact]
    public void View_CanRevert_OnlyWhenDirtyAndExisting()
    {
        var vm = new ViewDetailTabViewModel("V_X");
        Assert.False(vm.CanRevertChanges);          // clean

        vm.SourceText = "SELECT 1 FROM RDB$DATABASE";
        Assert.True(vm.CanRevertChanges);           // dirty + existing
    }

    [Fact]
    public void View_New_CannotRevert_EvenWhenDirty()
    {
        var vm = new ViewDetailTabViewModel("NEW_VIEW") { IsNew = true };
        vm.SourceText = "SELECT 1 FROM RDB$DATABASE"; // dirty
        Assert.True(vm.IsDirty);
        Assert.False(vm.CanRevertChanges);          // a new object has no DB state to revert to
    }

    [Fact]
    public async Task View_Revert_RequestsDestructiveNamedConfirmation()
    {
        var vm = new ViewDetailTabViewModel("V_SALES") { SourceText = "SELECT 1 FROM RDB$DATABASE" };
        ConfirmRequest? seen = null;
        vm.ConfirmationRequested += r => { seen = r; return Task.FromResult(false); }; // cancel

        await vm.RevertChangesCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.True(seen!.IsDestructive);
        Assert.Contains("V_SALES", seen.Message);
    }

    [Fact]
    public async Task View_Revert_NoConfirmHandler_DoesNotThrow()
    {
        // No handler → RequestConfirmAsync proceeds (default true) → RefreshAsync is a
        // no-op with null readers. Must not throw.
        var vm = new ViewDetailTabViewModel("V_X") { SourceText = "SELECT 1 FROM RDB$DATABASE" };
        await vm.RevertChangesCommand.ExecuteAsync(null);
    }

    // ─── Procedure ─────────────────────────────────────────────────────────

    [Fact]
    public void Procedure_CanRevert_OnlyWhenDirtyAndExisting()
    {
        var vm = new ProcedureDetailTabViewModel("SP_X");
        Assert.False(vm.CanRevertChanges);

        vm.SourceText = "CREATE OR ALTER PROCEDURE SP_X AS BEGIN END";
        Assert.True(vm.CanRevertChanges);
    }

    [Fact]
    public void Procedure_New_CannotRevert_EvenWhenDirty()
    {
        var vm = new ProcedureDetailTabViewModel("NEW_PROC") { IsNew = true };
        vm.SourceText = "CREATE OR ALTER PROCEDURE NEW_PROC AS BEGIN END";
        Assert.True(vm.IsDirty);
        Assert.False(vm.CanRevertChanges);
    }

    [Fact]
    public async Task Procedure_Revert_RequestsDestructiveNamedConfirmation()
    {
        var vm = new ProcedureDetailTabViewModel("SP_BALANCE") { SourceText = "CREATE OR ALTER PROCEDURE SP_BALANCE AS BEGIN END" };
        ConfirmRequest? seen = null;
        vm.ConfirmationRequested += r => { seen = r; return Task.FromResult(false); };

        await vm.RevertChangesCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.True(seen!.IsDestructive);
        Assert.Contains("SP_BALANCE", seen.Message);
    }

    // ─── Trigger ───────────────────────────────────────────────────────────

    [Fact]
    public void Trigger_CanRevert_OnlyWhenDirtyAndExisting()
    {
        var vm = new TriggerDetailTabViewModel("TR_X");
        Assert.False(vm.CanRevertChanges);

        vm.SourceText = "CREATE OR ALTER TRIGGER TR_X FOR T BEFORE INSERT AS BEGIN END";
        Assert.True(vm.CanRevertChanges);
    }

    [Fact]
    public void Trigger_New_CannotRevert_EvenWhenDirty()
    {
        var vm = new TriggerDetailTabViewModel("NEW_TRG") { IsNew = true };
        vm.SourceText = "CREATE OR ALTER TRIGGER NEW_TRG FOR T BEFORE INSERT AS BEGIN END";
        Assert.True(vm.IsDirty);
        Assert.False(vm.CanRevertChanges);
    }

    [Fact]
    public async Task Trigger_Revert_RequestsDestructiveNamedConfirmation()
    {
        var vm = new TriggerDetailTabViewModel("TR_AUDIT") { SourceText = "CREATE OR ALTER TRIGGER TR_AUDIT FOR T BEFORE INSERT AS BEGIN END" };
        ConfirmRequest? seen = null;
        vm.ConfirmationRequested += r => { seen = r; return Task.FromResult(false); };

        await vm.RevertChangesCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.True(seen!.IsDestructive);
        Assert.Contains("TR_AUDIT", seen.Message);
    }

    // ─── Table designer discard (in-memory, fully observable) ──────────────

    private static TableDetailTabViewModel NewTableVmWithPending(FirebirdConnectionService service, out TableDetailTabViewModel vm)
    {
        // Disconnected executor: buffered edits never touch it. PendingChanges is filled
        // by a queued (not executed) structural edit.
        var executor = new FirebirdDdlExecutor(service, null);
        vm = new TableDetailTabViewModel("MY_T", null, null, null, executor, null);
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", Type = "INTEGER", IsPrimaryKey = true });
        return vm;
    }

    [Fact]
    public async Task TableDiscard_Cancelled_KeepsPendingChanges()
    {
        using var service = new FirebirdConnectionService();
        NewTableVmWithPending(service, out var vm);
        await vm.ExecuteAddFieldAsync(new FieldDefinition { Name = "ADDED", BasicType = "INTEGER" });
        Assert.NotEmpty(vm.PendingChanges);

        ConfirmRequest? seen = null;
        vm.ConfirmationRequested += r => { seen = r; return Task.FromResult(false); }; // user clicks Cancel

        await vm.DiscardPendingChangesCommand.ExecuteAsync(null);

        Assert.NotNull(seen);
        Assert.True(seen!.IsDestructive);
        Assert.Contains("MY_T", seen.Message);
        Assert.NotEmpty(vm.PendingChanges);   // nothing discarded
    }

    [Fact]
    public async Task TableDiscard_Confirmed_ClearsPendingChanges()
    {
        using var service = new FirebirdConnectionService();
        NewTableVmWithPending(service, out var vm);
        await vm.ExecuteAddFieldAsync(new FieldDefinition { Name = "ADDED", BasicType = "INTEGER" });
        Assert.NotEmpty(vm.PendingChanges);

        vm.ConfirmationRequested += _ => Task.FromResult(true); // user confirms

        await vm.DiscardPendingChangesCommand.ExecuteAsync(null);

        Assert.Empty(vm.PendingChanges);
    }
}
