using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// FK wizard VM tests. Covers:
///   - default constraint name = FK_SRC_TGT (auto-derive on table-pick)
///   - constraint name user override sticks across table changes
///   - auto-mapping stage 1 (by name)
///   - auto-mapping stage 2 (by referenced PK when name-match incomplete)
///   - auto-mapping stage 3 (no-op when neither rule applies)
///   - validation cases (missing constraint name / table / fields / count)
///   - DDL preview reactivity
///   - BuildSpec round-trip via DdlGenerator.BuildAddForeignKey
/// </summary>
public class ForeignKeyDialogVmTests
{
    // Helper to build a VM with synchronous fake loaders. The loaders accept a
    // table name → returns canned field/PK list (case-insensitive table-name
    // match). Callers configure the lookups via the dictionaries.
    private static ForeignKeyDialogViewModel BuildVm(
        string sourceTable = "ZAMOWIENIA",
        IReadOnlyList<string>? sourceFields = null,
        IReadOnlyList<string>? availableTables = null,
        Dictionary<string, IReadOnlyList<string>>? referencedFieldsByTable = null,
        Dictionary<string, IReadOnlyList<string>>? primaryKeyByTable = null)
    {
        sourceFields ??= new[] { "ID", "ID_KONTRAHENTA", "DATA", "KWOTA" };
        availableTables ??= new[] { "KONTRAHENCI", "PRACOWNICY" };
        referencedFieldsByTable ??= new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase);
        primaryKeyByTable ??= new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase);

        Task<IReadOnlyList<string>> LoadFields(string table)
            => Task.FromResult(referencedFieldsByTable.TryGetValue(table, out var v) ? v : (IReadOnlyList<string>)System.Array.Empty<string>());
        Task<IReadOnlyList<string>> LoadPk(string table)
            => Task.FromResult(primaryKeyByTable.TryGetValue(table, out var v) ? v : (IReadOnlyList<string>)System.Array.Empty<string>());

        return new ForeignKeyDialogViewModel(sourceTable, sourceFields, availableTables, LoadFields, LoadPk);
    }

    [Fact]
    public void Initial_State_HasEmptyName_AndNoSelectedTable()
    {
        var vm = BuildVm();
        Assert.Equal(string.Empty, vm.ConstraintName);
        Assert.Null(vm.SelectedReferencedTable);
        Assert.False(vm.IsValid()); // empty name fails
    }

    [Fact]
    public void SelectingTable_AutoDerivesDefaultConstraintName()
    {
        var vm = BuildVm();
        vm.SelectedReferencedTable = "KONTRAHENCI";
        Assert.Equal("FK_ZAMOWIENIA_KONTRAHENCI", vm.ConstraintName);
    }

    [Fact]
    public void UserOverridesName_SticksAcrossTableChange()
    {
        var vm = BuildVm();
        vm.SelectedReferencedTable = "KONTRAHENCI";
        Assert.Equal("FK_ZAMOWIENIA_KONTRAHENCI", vm.ConstraintName);

        // User pins a custom name. Subsequent table change must NOT clobber it.
        vm.ConstraintName = "MY_CUSTOM_FK";
        vm.SelectedReferencedTable = "PRACOWNICY";
        Assert.Equal("MY_CUSTOM_FK", vm.ConstraintName);
    }

    [Fact]
    public async Task AutoMap_Stage1_ByName_AllMatch_PreSelects()
    {
        var vm = BuildVm(
            sourceFields: new[] { "ID_KONTRAHENTA" },
            referencedFieldsByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "ID", "ID_KONTRAHENTA", "NAZWA" },
            });
        // Select source field, then pick target table — auto-map should
        // pre-select the same-named ref field.
        vm.SourceFields.First(f => f.Name == "ID_KONTRAHENTA").IsSelected = true;
        vm.SelectedReferencedTable = "KONTRAHENCI";
        // Wait for the fire-and-forget load + mapping pass.
        await Task.Yield();

        Assert.Single(vm.ReferencedFields, f => f.IsSelected);
        Assert.Equal("ID_KONTRAHENTA", vm.ReferencedFields.First(f => f.IsSelected).Name);
    }

    [Fact]
    public async Task AutoMap_Stage2_ByPk_WhenNameMatchFails()
    {
        // Source field name doesn't appear in target → Stage 1 fails. But the
        // target has a single-column PK (matching count = 1) → Stage 2 picks it.
        var vm = BuildVm(
            sourceFields: new[] { "ID_KLIENT" },
            referencedFieldsByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "ID", "NAZWA" },
            },
            primaryKeyByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "ID" },
            });
        vm.SourceFields.First().IsSelected = true;
        vm.SelectedReferencedTable = "KONTRAHENCI";
        await Task.Yield();

        Assert.Single(vm.ReferencedFields, f => f.IsSelected);
        Assert.Equal("ID", vm.ReferencedFields.First(f => f.IsSelected).Name);
    }

    [Fact]
    public async Task AutoMap_Stage3_NoOp_WhenNeitherRuleApplies()
    {
        // Source has 2 fields; ref-table has 3 fields none of which match by
        // name; PK is a single column (count mismatch with selected source) →
        // no proposal. User picks manually.
        var vm = BuildVm(
            sourceFields: new[] { "X", "Y" },
            referencedFieldsByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "A", "B", "C" },
            },
            primaryKeyByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "A" }, // count=1 vs source count=2 — skip
            });
        vm.SourceFields[0].IsSelected = true;
        vm.SourceFields[1].IsSelected = true;
        vm.SelectedReferencedTable = "KONTRAHENCI";
        await Task.Yield();

        // No automatic selections on the ref side — user must pick.
        Assert.DoesNotContain(vm.ReferencedFields, f => f.IsSelected);
    }

    [Fact]
    public async Task AutoMap_MultiField_ByPk_PreservesOrder()
    {
        // Composite-PK target. Source-field count matches PK-field count;
        // Stage 1 fails (no name overlap). Stage 2 picks PK fields in PK
        // declaration order.
        var vm = BuildVm(
            sourceFields: new[] { "PARENT_A", "PARENT_B" },
            referencedFieldsByTable: new()
            {
                ["PARENT"] = new[] { "PK_A", "PK_B", "DATA" },
            },
            primaryKeyByTable: new()
            {
                ["PARENT"] = new[] { "PK_A", "PK_B" },
            },
            availableTables: new[] { "PARENT" });

        vm.SourceFields[0].IsSelected = true;
        vm.SourceFields[1].IsSelected = true;
        vm.SelectedReferencedTable = "PARENT";
        await Task.Yield();

        var selected = vm.ReferencedFields.Where(f => f.IsSelected).Select(f => f.Name).ToList();
        Assert.Equal(new[] { "PK_A", "PK_B" }, selected);
    }

    [Fact]
    public void Validation_MissingConstraintName_Fails()
    {
        var vm = BuildVm();
        vm.ConstraintName = string.Empty;
        Assert.False(vm.IsValid());
        Assert.Equal(UiStrings.ForeignKeyValidationConstraintNameRequired, vm.ValidationMessage);
    }

    [Fact]
    public void Validation_MissingReferencedTable_Fails()
    {
        var vm = BuildVm();
        vm.ConstraintName = "FK_X";
        Assert.False(vm.IsValid());
        Assert.Equal(UiStrings.ForeignKeyValidationReferencedTableRequired, vm.ValidationMessage);
    }

    [Fact]
    public async Task Validation_NoLocalFields_Fails()
    {
        var vm = BuildVm(referencedFieldsByTable: new()
        {
            ["KONTRAHENCI"] = new[] { "ID" },
        });
        vm.SelectedReferencedTable = "KONTRAHENCI";
        await Task.Yield();
        // Manually clear all selections on both sides
        foreach (var f in vm.SourceFields) f.IsSelected = false;
        foreach (var f in vm.ReferencedFields) f.IsSelected = false;
        Assert.False(vm.IsValid());
        Assert.Equal(UiStrings.ForeignKeyValidationLocalFieldsRequired, vm.ValidationMessage);
    }

    [Fact]
    public async Task Validation_CountMismatch_Fails()
    {
        var vm = BuildVm(
            sourceFields: new[] { "A", "B" },
            referencedFieldsByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "X", "Y", "Z" },
            });
        vm.SourceFields[0].IsSelected = true;
        vm.SourceFields[1].IsSelected = true;
        vm.SelectedReferencedTable = "KONTRAHENCI";
        await Task.Yield();
        // Clear auto-mapping then pick a different count on the ref side
        foreach (var f in vm.ReferencedFields) f.IsSelected = false;
        vm.ReferencedFields[0].IsSelected = true;
        Assert.False(vm.IsValid());
        Assert.Equal(UiStrings.ForeignKeyValidationFieldCountMismatch, vm.ValidationMessage);
    }

    [Fact]
    public async Task BuildSpec_RoundTripsThroughDdlGenerator()
    {
        // Full integration: VM → BuildSpec → BuildAddForeignKey. The DDL
        // produced is what the Execute path runs against the live FB.
        var vm = BuildVm(
            sourceFields: new[] { "ID_KONTRAHENT" },
            referencedFieldsByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "ID" },
            },
            primaryKeyByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "ID" },
            });
        vm.SourceFields[0].IsSelected = true;
        vm.SelectedReferencedTable = "KONTRAHENCI";
        await Task.Yield();
        vm.OnDeleteAction = vm.AvailableActions.First(a => a.Action == ForeignKeyAction.Cascade);

        Assert.True(vm.IsValid());
        var spec = vm.BuildSpec();
        Assert.Equal("FK_ZAMOWIENIA_KONTRAHENCI", spec.ConstraintName);
        Assert.Equal(new[] { "ID_KONTRAHENT" }, spec.LocalFields);
        Assert.Equal("KONTRAHENCI", spec.ReferencedTable);
        Assert.Equal(new[] { "ID" }, spec.ReferencedFields);
        Assert.Equal(ForeignKeyAction.Cascade, spec.OnDelete);

        var sql = DdlGenerator.BuildAddForeignKey("ZAMOWIENIA", spec);
        Assert.Contains("FK_ZAMOWIENIA_KONTRAHENCI", sql);
        Assert.Contains("ON DELETE CASCADE", sql);
    }

    [Fact]
    public async Task DdlPreview_LiveUpdatesOnEachChange()
    {
        var vm = BuildVm(
            sourceFields: new[] { "ID_KONTRAHENT" },
            referencedFieldsByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "ID" },
            },
            primaryKeyByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "ID" },
            });
        // Initial: incomplete → preview returns the "incomplete" hint.
        Assert.Equal(UiStrings.ForeignKeyDdlPreviewIncomplete, vm.DdlPreview);

        vm.SourceFields[0].IsSelected = true;
        vm.SelectedReferencedTable = "KONTRAHENCI";
        await Task.Yield();
        // After table-pick + auto-map: preview emits real DDL.
        Assert.Contains("ALTER TABLE", vm.DdlPreview);
        Assert.Contains("FOREIGN KEY", vm.DdlPreview);

        // Change OnUpdate — preview re-renders with the new clause.
        vm.OnUpdateAction = vm.AvailableActions.First(a => a.Action == ForeignKeyAction.SetNull);
        Assert.Contains("ON UPDATE SET NULL", vm.DdlPreview);
    }

    [Fact]
    public void AcceptCommand_SetsResultAndFiresClose()
    {
        var vm = BuildVm(
            referencedFieldsByTable: new()
            {
                ["KONTRAHENCI"] = new[] { "ID" },
            });
        vm.SelectedReferencedTable = "KONTRAHENCI";
        vm.SourceFields[0].IsSelected = true;
        vm.ReferencedFields[0].IsSelected = true;
        bool closed = false;
        vm.RequestClose += () => closed = true;
        vm.AcceptCommand.Execute(null);
        Assert.True(closed);
        Assert.NotNull(vm.Result);
        Assert.Equal("FK_ZAMOWIENIA_KONTRAHENCI", vm.Result!.ConstraintName);
    }

    [Fact]
    public void CancelCommand_NullsResultAndFiresClose()
    {
        var vm = BuildVm();
        bool closed = false;
        vm.RequestClose += () => closed = true;
        vm.CancelCommand.Execute(null);
        Assert.True(closed);
        Assert.Null(vm.Result);
    }
}
