using System;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Buffered Table Designer (Priority 1 regression fix). The structure designer
/// must be "edit the model → Compile/Apply → auto-commit": a structural edit
/// (Add/Drop/Move field, constraint, index, description) mutates the in-memory
/// working model + queues a DDL change, and NOTHING reaches the database until
/// ⚡ Compile runs the whole batch in ONE autonomous transaction. These tests pin
/// that NO immediate DDL runs on a structural edit, the grids reflect the working
/// model (pending Added/Dropped/Modified), Compile drains the queue, and Discard
/// reverts to the catalog state.
/// </summary>
public class TableDesignerBufferedTests
{
    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            Service = new FirebirdConnectionService();
            // Disconnected executor: a buffered edit never touches it (no DDL on
            // click), so every Add/Drop/Move/edit completes with no error. Only
            // Compile would hit the executor.
            var executor = new FirebirdDdlExecutor(Service, null);
            Vm = new TableDetailTabViewModel("MY_T", null, null, null, executor, null);
            Vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", Type = "INTEGER", IsPrimaryKey = true });
            Vm.Fields.Add(new FieldInfo { Position = 1, Name = "NAZWA", Type = "VARCHAR(50)", Size = 50 });
        }

        public FirebirdConnectionService Service { get; }
        public TableDetailTabViewModel Vm { get; }
        public void Dispose() => Service.Dispose();
    }

    // ─── No immediate DDL: every structural edit only buffers ─────────────

    [Fact]
    public async Task AddField_QueuesAndShowsPendingRow_NoDdlNoError()
    {
        using var h = new Harness();
        var before = h.Vm.EditableFields.Count;

        await h.Vm.ExecuteAddFieldAsync(new FieldDefinition { Name = "NEW_COL", BasicType = "INTEGER" });

        Assert.Null(h.Vm.ErrorMessage);                       // nothing executed
        Assert.Single(h.Vm.PendingChanges);
        Assert.Equal(PendingDdlChangeKind.AddField, h.Vm.PendingChanges[0].Kind);
        Assert.Equal(before + 1, h.Vm.EditableFields.Count);  // working model shows it
        var added = h.Vm.EditableFields.Last();
        Assert.Equal("NEW_COL", added.Name);
        Assert.True(added.IsPendingAdded);
    }

    [Fact]
    public async Task DropField_MarksRowDropped_NoDdlNoError()
    {
        using var h = new Harness();
        await h.Vm.ExecuteDropFieldAsync("NAZWA");

        Assert.Null(h.Vm.ErrorMessage);
        Assert.Single(h.Vm.PendingChanges);
        Assert.Equal(PendingDdlChangeKind.DropField, h.Vm.PendingChanges[0].Kind);
        var row = h.Vm.EditableFields.First(r => r.Original.Name == "NAZWA");
        Assert.True(row.IsPendingDropped);   // kept visible, marked
    }

    [Fact]
    public async Task DropField_OnPendingAddedRow_UnAddsIt()
    {
        using var h = new Harness();
        await h.Vm.ExecuteAddFieldAsync(new FieldDefinition { Name = "TEMP", BasicType = "INTEGER" });
        Assert.Single(h.Vm.PendingChanges);
        var countWithAdd = h.Vm.EditableFields.Count;

        // Dropping a not-yet-compiled added column removes it entirely + un-queues.
        await h.Vm.ExecuteDropFieldAsync("TEMP");

        Assert.Empty(h.Vm.PendingChanges);
        Assert.Equal(countWithAdd - 1, h.Vm.EditableFields.Count);
        Assert.DoesNotContain(h.Vm.EditableFields, r => r.Name == "TEMP");
    }

    [Fact]
    public async Task EditField_QueuesAlter_MarksRowModified_NoError()
    {
        using var h = new Harness();
        var original = h.Vm.Fields.First(f => f.Name == "NAZWA");
        var target = new FieldDefinition { Name = "NAZWA", BasicType = "VARCHAR", Size = 100 };

        await h.Vm.ExecuteEditFieldAsync(original, target);

        Assert.Null(h.Vm.ErrorMessage);
        Assert.NotEmpty(h.Vm.PendingChanges);
        var row = h.Vm.EditableFields.First(r => r.Original.Name == "NAZWA");
        Assert.Equal(PendingChangeKind.Modified, row.PendingKind);
        Assert.Contains("100", row.TypeText);   // working-model value updated
    }

    [Fact]
    public async Task Move_ReordersWorkingModel_QueuesNoError()
    {
        using var h = new Harness();
        await h.Vm.ExecuteMoveAsync("NAZWA", 1);   // move NAZWA to position 1

        Assert.Null(h.Vm.ErrorMessage);
        Assert.Single(h.Vm.PendingChanges);
        Assert.Equal(PendingDdlChangeKind.MoveField, h.Vm.PendingChanges[0].Kind);
        Assert.Equal("NAZWA", h.Vm.EditableFields[0].Original.Name);   // reordered visibly
    }

    [Fact]
    public async Task AddConstraintAndIndex_ShowPendingRows_NoError()
    {
        using var h = new Harness();
        await h.Vm.ExecuteAddUniqueAsync(new ConstraintFieldSpec("UNQ_T", new[] { "NAZWA" }));
        await h.Vm.ExecuteAddIndexAsync(new IndexSpec("IX_T", new[] { "NAZWA" }, false, false, null));

        Assert.Null(h.Vm.ErrorMessage);
        Assert.Equal(2, h.Vm.PendingChanges.Count);
        Assert.Contains(h.Vm.Constraints, c => c.Name == "UNQ_T" && c.PendingState == PendingChangeKind.Added);
        Assert.Contains(h.Vm.Indexes, i => i.Name == "IX_T" && i.PendingState == PendingChangeKind.Added);
    }

    // ─── Compile / Discard ────────────────────────────────────────────────

    [Fact]
    public void CompileAndDiscard_Gates_TrackPendingChanges()
    {
        using var h = new Harness();
        Assert.False(h.Vm.CanCompile);
        Assert.False(h.Vm.CanDiscardPending);

        h.Vm.AddPendingAddField(new FieldDefinition { Name = "X", BasicType = "INTEGER" });

        Assert.True(h.Vm.CanCompile);          // executor present + pending
        Assert.True(h.Vm.CanDiscardPending);
    }

    [Fact]
    public async Task Compile_Disconnected_KeepsQueue_SetsError()
    {
        using var h = new Harness();
        await h.Vm.ExecuteAddFieldAsync(new FieldDefinition { Name = "C1", BasicType = "INTEGER" });
        Assert.Single(h.Vm.PendingChanges);

        // Compile applies the batch against the disconnected executor → the
        // whole atomic apply fails; the queue is retained so the user can retry.
        await h.Vm.CompileCommand.ExecuteAsync(null);

        Assert.NotNull(h.Vm.ErrorMessage);
        Assert.Single(h.Vm.PendingChanges);
    }

    [Fact]
    public void Discard_RevertsWorkingModelToCatalog()
    {
        using var h = new Harness();
        var baseFieldCount = h.Vm.EditableFields.Count;

        // Queue a spread of structural edits across the grids.
        h.Vm.ExecuteAddFieldAsync(new FieldDefinition { Name = "ADDED", BasicType = "INTEGER" });
        h.Vm.ExecuteDropFieldAsync("NAZWA");
        h.Vm.ExecuteAddUniqueAsync(new ConstraintFieldSpec("UNQ_T", new[] { "ID" }));
        h.Vm.ExecuteAddIndexAsync(new IndexSpec("IX_T", new[] { "ID" }, false, false, null));
        Assert.NotEmpty(h.Vm.PendingChanges);

        h.Vm.DiscardPendingChangesCommand.Execute(null);

        Assert.Empty(h.Vm.PendingChanges);
        // Fields back to the catalog set: the added row is gone, the dropped row
        // is un-marked (rebuilt clean from Fields).
        Assert.Equal(baseFieldCount, h.Vm.EditableFields.Count);
        Assert.DoesNotContain(h.Vm.EditableFields, r => r.Name == "ADDED");
        Assert.All(h.Vm.EditableFields, r => Assert.Equal(PendingChangeKind.None, r.PendingKind));
        // Pending-Added constraint + index removed.
        Assert.DoesNotContain(h.Vm.Constraints, c => c.Name == "UNQ_T");
        Assert.DoesNotContain(h.Vm.Indexes, i => i.Name == "IX_T");
    }

    [Fact]
    public async Task DropConstraint_OnLiveRow_MarksDropped_OnPendingAdded_UnAdds()
    {
        using var h = new Harness();
        // A live (catalog) constraint marked dropped stays visible + queued.
        h.Vm.Constraints.Add(new ConstraintInfo { Name = "FK_LIVE", ConstraintType = "FOREIGN KEY", Fields = "ID" });
        await h.Vm.ExecuteDropConstraintAsync("FK_LIVE");
        Assert.Single(h.Vm.PendingChanges);
        Assert.Equal(PendingChangeKind.Dropped, h.Vm.Constraints.First(c => c.Name == "FK_LIVE").PendingState);

        // Dropping a pending-Added constraint removes it + un-queues its add.
        await h.Vm.ExecuteAddUniqueAsync(new ConstraintFieldSpec("UNQ_NEW", new[] { "ID" }));
        Assert.Equal(2, h.Vm.PendingChanges.Count);
        await h.Vm.ExecuteDropConstraintAsync("UNQ_NEW");
        Assert.Single(h.Vm.PendingChanges);   // back to just the FK_LIVE drop
        Assert.DoesNotContain(h.Vm.Constraints, c => c.Name == "UNQ_NEW");
    }
}
