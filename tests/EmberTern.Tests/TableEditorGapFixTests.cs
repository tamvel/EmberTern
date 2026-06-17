using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Gap-fixes after Constraint Management V1: #3 domain↔type sync, #4 field
/// dependency rules (Computed/Domain/Default/NotNull/PK/Autoincrement), #1 Drop
/// Foreign Key from Pola, #2 Unique/PK backing-index config (USING clause).
/// </summary>
public class TableEditorGapFixTests
{
    // ─── #3 + #4 : AddFieldDialogViewModel dependency model ───────────────

    private static AddFieldDialogViewModel MakeDialog(params (string name, string type)[] domains)
    {
        var list = new List<DomainSpec>();
        foreach (var (n, t) in domains) list.Add(new DomainSpec(n, t));
        return new AddFieldDialogViewModel("T", list, Array.Empty<string>());
    }

    [Fact]
    public void Domain_GovernsType_DisablesBasicTypeTab_AndShowsResolvedType()
    {
        var vm = MakeDialog(("T_KWOTA", "NUMERIC(15,2)"));
        Assert.True(vm.IsBasicTypeTabEnabled);
        vm.SelectedDomain = vm.Domains[0];
        Assert.True(vm.HasDomain);
        Assert.False(vm.IsBasicTypeTabEnabled);          // domain governs type
        Assert.Equal("NUMERIC(15,2)", vm.SelectedDomainType);  // #3 user sees real type
        Assert.True(vm.HasDomainType);
    }

    [Fact]
    public void Computed_DisablesTypeDomainDefaultAutoincPkNotNull()
    {
        var vm = MakeDialog();
        vm.ComputedExpression = "PRICE * QTY";
        Assert.True(vm.HasComputed);
        Assert.False(vm.IsDomainTabEnabled);
        Assert.False(vm.IsBasicTypeTabEnabled);
        Assert.False(vm.IsDefaultTabEnabled);
        Assert.False(vm.IsCheckTabEnabled);
        Assert.False(vm.IsAutoincTabEnabled);
        Assert.False(vm.IsPrimaryKeyEnabled);
        Assert.False(vm.IsNotNullEnabled);
        vm.ComputedExpression = "";
        Assert.True(vm.IsDomainTabEnabled);
        Assert.True(vm.IsBasicTypeTabEnabled);
    }

    [Fact]
    public void PrimaryKey_ForcesNotNull_AndDisablesNotNullToggle()
    {
        var vm = MakeDialog();
        Assert.True(vm.IsNotNullEnabled);
        vm.PrimaryKey = true;
        Assert.True(vm.NotNull);            // forced on
        Assert.False(vm.IsNotNullEnabled);  // can't be toggled off while PK
    }

    [Fact]
    public void Autoincrement_ClearsDefault_AndDisablesDefaultTab()
    {
        var vm = MakeDialog();
        vm.DefaultValue = "0";
        vm.AutoIncrementMode = AutoIncrementMode.Identity;
        Assert.True(vm.HasAutoincrement);
        Assert.Equal(string.Empty, vm.DefaultValue);   // cleared (#4)
        Assert.False(vm.IsDefaultTabEnabled);
    }

    // ─── #2 : computed columns emit ONLY COMPUTED BY (no CHECK / PK / etc.) ──

    [Fact]
    public void BuildAddField_Computed_OmitsCheckPkDefaultNotNull()
    {
        var def = new FieldDefinition
        {
            Name = "TOTAL",
            BasicType = "INTEGER",
            ComputedExpression = "PRICE * QTY",
            CheckExpression = "TOTAL > 0",
            DefaultValue = "0",
            NotNull = true,
            PrimaryKey = true,
            AutoIncrement = AutoIncrementMode.NewGenerator,
            GeneratorName = "GEN_X",
        };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.Contains("COMPUTED BY (PRICE * QTY)", sql);
        Assert.DoesNotContain("CHECK", sql);
        Assert.DoesNotContain("PRIMARY KEY", sql);
        Assert.DoesNotContain("DEFAULT", sql);
        Assert.DoesNotContain("NOT NULL", sql);
        Assert.DoesNotContain("GENERATOR", sql);   // no backing generator/trigger
        Assert.DoesNotContain("TRIGGER", sql);
    }

    [Fact]
    public void BuildCreateTable_ComputedField_OmitsCheckAndPk()
    {
        var spec = new TableSpec();
        spec.Fields.Add(new FieldDefinition { Name = "ID", BasicType = "INTEGER", NotNull = true, PrimaryKey = true });
        spec.Fields.Add(new FieldDefinition
        {
            Name = "TOTAL",
            BasicType = "INTEGER",
            ComputedExpression = "PRICE * QTY",
            CheckExpression = "TOTAL > 0",
            PrimaryKey = true,
        });
        var sql = DdlGenerator.BuildCreateTable("ORDERS", spec);
        Assert.Contains("\"TOTAL\" COMPUTED BY (PRICE * QTY)", sql);
        Assert.DoesNotContain("CHECK", sql);
        // The ID field is the only PK column — TOTAL must not appear in the PK list.
        Assert.Contains("PRIMARY KEY (\"ID\")", sql);
        Assert.DoesNotContain("\"ID\", \"TOTAL\"", sql);
    }

    [Fact]
    public void NewTableRow_Computed_ClearsConflictingValues()
    {
        var row = new NewTableFieldRowViewModel
        {
            DomainName = "T_X",
            Size = 50,
            Scale = 2,
            DefaultValue = "0",
            CheckExpression = "X > 0",
            NotNull = true,
            PrimaryKey = true,
            AutoIncrement = true,
        };
        row.ComputedExpression = "A + B";

        Assert.Null(row.DomainName);
        Assert.Null(row.Size);
        Assert.Null(row.Scale);
        Assert.Equal(string.Empty, row.DefaultValue);
        Assert.Equal(string.Empty, row.CheckExpression);
        Assert.False(row.NotNull);
        Assert.False(row.PrimaryKey);
        Assert.False(row.AutoIncrement);
    }

    // ─── #3 + #4 : New Table grid row ─────────────────────────────────────

    [Fact]
    public void NewTableRow_Domain_GovernsType_AndShowsDomainType()
    {
        var owner = new NewTableTabViewModel();
        owner.SetAvailableDomains(new[] { new DomainSpec("T_KWOTA", "NUMERIC(15,2)") });
        var row = new NewTableFieldRowViewModel(owner) { Type = "INTEGER" };
        Assert.True(row.IsTypeEnabled);
        Assert.Equal("INTEGER", row.EffectiveTypeDisplay);

        row.DomainName = "T_KWOTA";
        Assert.True(row.HasDomain);
        Assert.False(row.IsTypeEnabled);                 // domain governs type
        Assert.Equal("NUMERIC(15,2)", row.DomainType);
        Assert.Equal("NUMERIC(15,2)", row.EffectiveTypeDisplay);  // #3
    }

    [Fact]
    public void NewTableRow_PrimaryKey_ForcesNotNull()
    {
        var row = new NewTableFieldRowViewModel();
        row.PrimaryKey = true;
        Assert.True(row.NotNull);
        Assert.False(row.IsNotNullEnabled);
    }

    [Fact]
    public void NewTableRow_Autoincrement_ClearsDefault()
    {
        var row = new NewTableFieldRowViewModel { DefaultValue = "0" };
        row.AutoIncrement = true;
        Assert.Equal(string.Empty, row.DefaultValue);
    }

    // #1 — Computed By DISABLES every conflicting cell (not just clears once).
    [Fact]
    public void NewTableRow_Computed_DisablesAllConflictingCells()
    {
        var row = new NewTableFieldRowViewModel { Type = "VARCHAR" };
        // sanity — before computing, the type-related cells are enabled
        Assert.True(row.IsSizeEnabled);
        row.ComputedExpression = "A + B";

        Assert.False(row.IsSizeEnabled);
        Assert.False(row.IsPrecisionScaleEnabled);
        Assert.False(row.IsDefaultEnabled);
        Assert.False(row.IsCheckEnabled);
        Assert.False(row.IsCharsetEnabled);
        Assert.False(row.IsPkEnabled);
        Assert.False(row.IsAiEnabled);
        Assert.False(row.IsNotNullEnabled);
        Assert.False(row.IsTypeEnabled);
        Assert.False(row.IsDomainEnabled);
    }

    [Fact]
    public void NewTableRow_Domain_DisablesComputedAndSize()
    {
        var owner = new NewTableTabViewModel();
        owner.SetAvailableDomains(new[] { new DomainSpec("T_KWOTA", "NUMERIC(15,2)") });
        var row = new NewTableFieldRowViewModel(owner) { Type = "VARCHAR" };
        Assert.True(row.IsComputedEnabled);
        row.DomainName = "T_KWOTA";
        Assert.False(row.IsComputedEnabled);   // domain ↔ computed mutually exclusive
        Assert.False(row.IsSizeEnabled);        // domain governs the type
    }

    // #2 — setting ValidationMessage must raise HasValidationMessage so the
    // validation row's IsVisible updates (otherwise "click Compile, nothing").
    [Fact]
    public void NewTable_ValidationMessage_RaisesHasValidationMessage()
    {
        var vm = new NewTableTabViewModel();
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NewTableTabViewModel.HasValidationMessage)) raised = true;
        };
        vm.ValidationMessage = "boom";
        Assert.True(raised);
        Assert.True(vm.HasValidationMessage);
    }

    [Fact]
    public void NewTable_CompileWithEmptyName_SurfacesValidationMessage()
    {
        var vm = new NewTableTabViewModel { TableName = "" };
        Assert.False(vm.IsValid());
        Assert.True(vm.HasValidationMessage);
        Assert.NotEmpty(vm.ValidationMessage);
    }

    // ─── #3 + #4 : inline Pola row ────────────────────────────────────────

    [Fact]
    public void FieldRow_TypeCellDisabled_WhenDomainGoverns()
    {
        var owner = new TableDetailTabViewModel("T") { IsFieldEditMode = true };
        var row = new FieldRowViewModel(new FieldInfo { Name = "F", Type = "INTEGER" }, owner);
        Assert.True(row.IsTypeCellEditable);   // edit mode, no domain
        row.DomainName = "T_KWOTA";
        Assert.True(row.HasDomain);
        Assert.False(row.IsTypeCellEditable);  // domain governs type
    }

    // ─── #1 : resolve FK constraint for a field (Drop FK from Pola) ───────

    [Fact]
    public void ResolveForeignKeyConstraintForField_MatchesByFieldList()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.Constraints.Add(new ConstraintInfo { Name = "FK_A", ConstraintType = "FOREIGN KEY", Fields = "ID_NAGL" });
        vm.Constraints.Add(new ConstraintInfo { Name = "FK_B", ConstraintType = "FOREIGN KEY", Fields = "ID_X, ID_Y" });

        Assert.Equal("FK_A", vm.ResolveForeignKeyConstraintForField(new FieldInfo { Name = "ID_NAGL" }));
        Assert.Equal("FK_B", vm.ResolveForeignKeyConstraintForField(new FieldInfo { Name = "ID_Y" }));
        Assert.Null(vm.ResolveForeignKeyConstraintForField(new FieldInfo { Name = "NOPE" }));
        Assert.Null(vm.ResolveForeignKeyConstraintForField(null));
    }

    [Fact]
    public async Task DropFieldForeignKeyCommand_Confirmed_QueuesDropAndMarksConstraint()
    {
        using var service = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(service, null);
        var vm = new TableDetailTabViewModel("T", null, null, null, executor, null);
        vm.Constraints.Add(new ConstraintInfo { Name = "FK_A", ConstraintType = "FOREIGN KEY", Fields = "ID_NAGL" });
        vm.Fields.Add(new FieldInfo { Name = "ID_NAGL", IsForeignKey = true });
        vm.SelectedField = vm.Fields[0];
        vm.ConfirmationRequested += _ => Task.FromResult(true);

        Assert.True(vm.CanDropFieldForeignKey);
        await vm.DropFieldForeignKeyCommand.ExecuteAsync(null);

        // BUFFERED: confirmed → routes through the shared Drop Constraint path,
        // which QUEUES the drop and marks the live constraint pending-Dropped.
        // No DDL runs (Compile applies the batch), so no error.
        Assert.Null(vm.ErrorMessage);
        Assert.Single(vm.PendingChanges);
        Assert.Equal(EmberTern.Core.Metadata.PendingChangeKind.Dropped, vm.Constraints[0].PendingState);
    }

    // ─── #2 : USING [ASC|DESC] INDEX clause on PK / UNIQUE ────────────────

    [Fact]
    public void BuildAddUnique_WithIndexNameAndDescending()
    {
        var sql = DdlGenerator.BuildAddUnique("T", "UQ_T", new[] { "A" }, "MY_IX", descending: true);
        Assert.Equal("ALTER TABLE \"T\" ADD CONSTRAINT \"UQ_T\" UNIQUE (\"A\") USING DESC INDEX \"MY_IX\"", sql);
    }

    [Fact]
    public void BuildAddPrimaryKey_WithIndexNameAscending()
    {
        var sql = DdlGenerator.BuildAddPrimaryKey("T", "PK_T", new[] { "A" }, "MY_IX", descending: false);
        Assert.Equal("ALTER TABLE \"T\" ADD CONSTRAINT \"PK_T\" PRIMARY KEY (\"A\") USING ASC INDEX \"MY_IX\"", sql);
    }

    [Fact]
    public void BuildAddUnique_DescendingWithoutName_DefaultsIndexToConstraintName()
    {
        var sql = DdlGenerator.BuildAddUnique("T", "UQ_T", new[] { "A" }, indexName: null, descending: true);
        Assert.Equal("ALTER TABLE \"T\" ADD CONSTRAINT \"UQ_T\" UNIQUE (\"A\") USING DESC INDEX \"UQ_T\"", sql);
    }

    [Fact]
    public void BuildAddUnique_NoIndexNoDescending_OmitsUsingClause()
    {
        var sql = DdlGenerator.BuildAddUnique("T", "UQ_T", new[] { "A" });
        Assert.Equal("ALTER TABLE \"T\" ADD CONSTRAINT \"UQ_T\" UNIQUE (\"A\")", sql);
        Assert.DoesNotContain("USING", sql);
    }

    [Fact]
    public void ConstraintFieldDialog_BuildResult_CarriesIndexConfig()
    {
        var vm = new ConstraintFieldDialogViewModel(ConstraintFieldKind.Unique, "T", new[] { "A" });
        vm.Fields[0].IsSelected = true;
        vm.IndexName = "MY_IX";
        vm.Descending = true;
        var spec = vm.BuildResult();
        Assert.Equal("MY_IX", spec.IndexName);
        Assert.True(spec.Descending);
        Assert.Contains("USING DESC INDEX \"MY_IX\"", vm.DdlPreview);
    }

    // ─── #7 : table description editing ───────────────────────────────────

    [Fact]
    public void Description_EditableCopy_SyncsOnLoad()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.Description = "hello";   // simulates a load
        Assert.Equal("hello", vm.EditableDescription);
    }

    [Fact]
    public void Description_CanEdit_FalseWithoutExecutor()
    {
        var vm = new TableDetailTabViewModel("T");
        Assert.False(vm.CanEditDescription);
    }

    [Fact]
    public async Task SaveDescription_Queues_NoDdlNoError()
    {
        using var service = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(service, null);
        var vm = new TableDetailTabViewModel("T", null, null, null, executor, null);
        Assert.True(vm.CanEditDescription);
        vm.EditableDescription = "new desc";

        await vm.SaveDescriptionCommand.ExecuteAsync(null);

        // BUFFERED: queues a COMMENT ON TABLE change, no DDL runs → no error.
        Assert.Null(vm.ErrorMessage);
        Assert.Single(vm.PendingChanges);
    }

    [Fact]
    public async Task ClearDescription_EmptiesAndQueuesSave()
    {
        using var service = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(service, null);
        var vm = new TableDetailTabViewModel("T", null, null, null, executor, null);
        vm.EditableDescription = "to be cleared";

        await vm.ClearDescriptionCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.EditableDescription);
        // BUFFERED: queues the COMMENT ON TABLE … IS NULL change, no error.
        Assert.Null(vm.ErrorMessage);
        Assert.Single(vm.PendingChanges);
    }
}
