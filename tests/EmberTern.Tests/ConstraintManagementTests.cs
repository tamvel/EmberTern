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
/// Constraint Management Sprint V1 — DDL builders, dialog VM validation, and
/// the Add/Drop VM operations (PK / FK reuse / Check / Unique). Live-DB paths
/// (a successful Add that survives to RefreshStructureAsync + selects the new
/// row) aren't covered here — they need a real Firebird; the disconnected
/// executor exercises the error-handling branch instead.
/// </summary>
public class ConstraintManagementTests
{
    // ─── DdlGenerator.BuildAddPrimaryKey ──────────────────────────────────

    [Fact]
    public void BuildAddPrimaryKey_SingleField()
    {
        var sql = DdlGenerator.BuildAddPrimaryKey("USERS", "PK_USERS", new[] { "ID" });
        Assert.Equal("ALTER TABLE \"USERS\" ADD CONSTRAINT \"PK_USERS\" PRIMARY KEY (\"ID\")", sql);
    }

    [Fact]
    public void BuildAddPrimaryKey_CompositeField()
    {
        var sql = DdlGenerator.BuildAddPrimaryKey("ORDER_LINE", "PK_OL", new[] { "ORDER_ID", "LINE_NO" });
        Assert.Equal("ALTER TABLE \"ORDER_LINE\" ADD CONSTRAINT \"PK_OL\" PRIMARY KEY (\"ORDER_ID\", \"LINE_NO\")", sql);
    }

    [Fact]
    public void BuildAddPrimaryKey_EscapesInternalQuotes()
    {
        var sql = DdlGenerator.BuildAddPrimaryKey("T\"X", "PK", new[] { "A\"B" });
        Assert.Contains("\"T\"\"X\"", sql);
        Assert.Contains("\"A\"\"B\"", sql);
    }

    [Theory]
    [InlineData("", "PK")]
    [InlineData("   ", "PK")]
    [InlineData("T", "")]
    [InlineData("T", "  ")]
    public void BuildAddPrimaryKey_ThrowsOnMissingTableOrName(string table, string name)
    {
        Assert.Throws<ArgumentException>(() =>
            DdlGenerator.BuildAddPrimaryKey(table, name, new[] { "ID" }));
    }

    [Fact]
    public void BuildAddPrimaryKey_ThrowsOnEmptyFields()
    {
        Assert.Throws<ArgumentException>(() =>
            DdlGenerator.BuildAddPrimaryKey("T", "PK", Array.Empty<string>()));
    }

    // ─── DdlGenerator.BuildAddUnique ──────────────────────────────────────

    [Fact]
    public void BuildAddUnique_SingleField()
    {
        var sql = DdlGenerator.BuildAddUnique("USERS", "UNQ_EMAIL", new[] { "EMAIL" });
        Assert.Equal("ALTER TABLE \"USERS\" ADD CONSTRAINT \"UNQ_EMAIL\" UNIQUE (\"EMAIL\")", sql);
    }

    [Fact]
    public void BuildAddUnique_CompositeField()
    {
        var sql = DdlGenerator.BuildAddUnique("T", "UNQ_T", new[] { "A", "B" });
        Assert.Equal("ALTER TABLE \"T\" ADD CONSTRAINT \"UNQ_T\" UNIQUE (\"A\", \"B\")", sql);
    }

    [Fact]
    public void BuildAddUnique_ThrowsOnEmptyFields()
    {
        Assert.Throws<ArgumentException>(() =>
            DdlGenerator.BuildAddUnique("T", "UNQ", new List<string>()));
    }

    // ─── DdlGenerator.BuildAddCheck ───────────────────────────────────────

    [Fact]
    public void BuildAddCheck_BareExpression_WrapsInCheck()
    {
        var sql = DdlGenerator.BuildAddCheck("T", "CHK_ID", "ID > 0");
        Assert.Equal("ALTER TABLE \"T\" ADD CONSTRAINT \"CHK_ID\" CHECK (ID > 0)", sql);
    }

    [Fact]
    public void BuildAddCheck_FullClause_PassedVerbatim()
    {
        var sql = DdlGenerator.BuildAddCheck("T", "CHK_ID", "CHECK (ID > 0)");
        Assert.Equal("ALTER TABLE \"T\" ADD CONSTRAINT \"CHK_ID\" CHECK (ID > 0)", sql);
    }

    [Fact]
    public void BuildAddCheck_FullClauseLowercase_PassedVerbatim()
    {
        var sql = DdlGenerator.BuildAddCheck("T", "C", "check (status in ('A','B'))");
        Assert.Equal("ALTER TABLE \"T\" ADD CONSTRAINT \"C\" check (status in ('A','B'))", sql);
    }

    [Fact]
    public void BuildAddCheck_ThrowsOnEmptyExpression()
    {
        Assert.Throws<ArgumentException>(() =>
            DdlGenerator.BuildAddCheck("T", "CHK", "   "));
    }

    [Fact]
    public void BuildAddCheck_ThrowsOnMissingNameOrTable()
    {
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAddCheck("", "CHK", "ID > 0"));
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildAddCheck("T", "", "ID > 0"));
    }

    // ─── DdlGenerator.BuildDropConstraint ─────────────────────────────────

    [Fact]
    public void BuildDropConstraint_Shape()
    {
        var sql = DdlGenerator.BuildDropConstraint("USERS", "FK_USERS_ROLE");
        Assert.Equal("ALTER TABLE \"USERS\" DROP CONSTRAINT \"FK_USERS_ROLE\"", sql);
    }

    [Fact]
    public void BuildDropConstraint_EscapesInternalQuotes()
    {
        var sql = DdlGenerator.BuildDropConstraint("T", "C\"X");
        Assert.Equal("ALTER TABLE \"T\" DROP CONSTRAINT \"C\"\"X\"", sql);
    }

    [Theory]
    [InlineData("", "C")]
    [InlineData("T", "")]
    [InlineData("T", "   ")]
    public void BuildDropConstraint_ThrowsOnMissingArgs(string table, string name)
    {
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildDropConstraint(table, name));
    }

    // ─── ConstraintFieldDialogViewModel (PK / Unique) ─────────────────────

    [Fact]
    public void FieldDialog_DefaultName_DependsOnKind()
    {
        var pk = new ConstraintFieldDialogViewModel(ConstraintFieldKind.PrimaryKey, "USERS", new[] { "ID" });
        Assert.Equal("PK_USERS", pk.ConstraintName);

        var unq = new ConstraintFieldDialogViewModel(ConstraintFieldKind.Unique, "USERS", new[] { "EMAIL" });
        Assert.Equal("UNQ_USERS", unq.ConstraintName);
    }

    [Fact]
    public void FieldDialog_IsValid_RequiresNameAndSelection()
    {
        var vm = new ConstraintFieldDialogViewModel(ConstraintFieldKind.PrimaryKey, "T", new[] { "ID", "NAME" });
        // Default name present, but no field selected yet.
        Assert.False(vm.IsValid());

        vm.Fields[0].IsSelected = true;
        Assert.True(vm.IsValid());

        vm.ConstraintName = "   ";
        Assert.False(vm.IsValid());
    }

    [Fact]
    public void FieldDialog_BuildResult_ReturnsSelectedNamesAndTrimmedName()
    {
        var vm = new ConstraintFieldDialogViewModel(ConstraintFieldKind.PrimaryKey, "T", new[] { "A", "B", "C" });
        vm.ConstraintName = "  PK_T  ";
        vm.Fields[0].IsSelected = true;
        vm.Fields[2].IsSelected = true;

        var result = vm.BuildResult();
        Assert.Equal("PK_T", result.Name);
        Assert.Equal(new[] { "A", "C" }, result.Fields);
    }

    [Fact]
    public void FieldDialog_DdlPreview_UsesKindKeyword()
    {
        var pk = new ConstraintFieldDialogViewModel(ConstraintFieldKind.PrimaryKey, "T", new[] { "ID" });
        pk.Fields[0].IsSelected = true;
        Assert.Contains("PRIMARY KEY", pk.DdlPreview);

        var unq = new ConstraintFieldDialogViewModel(ConstraintFieldKind.Unique, "T", new[] { "ID" });
        unq.Fields[0].IsSelected = true;
        Assert.Contains("UNIQUE", unq.DdlPreview);
        Assert.DoesNotContain("PRIMARY KEY", unq.DdlPreview);
    }

    [Fact]
    public void FieldDialog_DdlPreview_IncompleteWhenNoSelection()
    {
        var vm = new ConstraintFieldDialogViewModel(ConstraintFieldKind.PrimaryKey, "T", new[] { "ID" });
        Assert.Equal(UiStrings.ConstraintDdlPreviewIncomplete, vm.DdlPreview);
    }

    [Fact]
    public void FieldDialog_Accept_SetsResultAndRequestsClose()
    {
        var vm = new ConstraintFieldDialogViewModel(ConstraintFieldKind.PrimaryKey, "T", new[] { "ID" });
        vm.Fields[0].IsSelected = true;
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.AcceptCommand.Execute(null);

        Assert.True(closed);
        Assert.NotNull(vm.Result);
        Assert.Equal("PK_T", vm.Result!.Name);
    }

    [Fact]
    public void FieldDialog_AcceptInvalid_DoesNotClose()
    {
        var vm = new ConstraintFieldDialogViewModel(ConstraintFieldKind.PrimaryKey, "T", new[] { "ID" });
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.AcceptCommand.Execute(null); // no field selected → invalid

        Assert.False(closed);
        Assert.Null(vm.Result);
        Assert.True(vm.HasValidationMessage);
    }

    [Fact]
    public void FieldDialog_Cancel_NullResult()
    {
        var vm = new ConstraintFieldDialogViewModel(ConstraintFieldKind.Unique, "T", new[] { "ID" });
        vm.Fields[0].IsSelected = true;
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Null(vm.Result);
    }

    // ─── CheckConstraintDialogViewModel ───────────────────────────────────

    [Fact]
    public void CheckDialog_DefaultName()
    {
        var vm = new CheckConstraintDialogViewModel("USERS");
        Assert.Equal("CHK_USERS", vm.ConstraintName);
    }

    [Fact]
    public void CheckDialog_IsValid_RequiresNameAndExpression()
    {
        var vm = new CheckConstraintDialogViewModel("T");
        Assert.False(vm.IsValid()); // name present, expression empty

        vm.CheckExpression = "ID > 0";
        Assert.True(vm.IsValid());

        vm.ConstraintName = "";
        Assert.False(vm.IsValid());
    }

    [Fact]
    public void CheckDialog_BuildResult_Trims()
    {
        var vm = new CheckConstraintDialogViewModel("T")
        {
            ConstraintName = "  CHK_T  ",
            CheckExpression = "  ID > 0  ",
        };
        var result = vm.BuildResult();
        Assert.Equal("CHK_T", result.Name);
        Assert.Equal("ID > 0", result.Expression);
    }

    [Fact]
    public void CheckDialog_DdlPreview_ReflectsExpression()
    {
        var vm = new CheckConstraintDialogViewModel("T") { CheckExpression = "ID > 0" };
        Assert.Contains("CHECK (ID > 0)", vm.DdlPreview);
    }

    [Fact]
    public void CheckDialog_Accept_SetsResult()
    {
        var vm = new CheckConstraintDialogViewModel("T") { CheckExpression = "ID > 0" };
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.AcceptCommand.Execute(null);

        Assert.True(closed);
        Assert.NotNull(vm.Result);
        Assert.Equal("ID > 0", vm.Result!.Expression);
    }

    // ─── TableDetailTabViewModel Add/Drop wiring ──────────────────────────

    [Fact]
    public void CanManageConstraints_FalseWithoutExecutor()
    {
        var vm = new TableDetailTabViewModel("T");
        Assert.False(vm.CanManageConstraints);
        Assert.False(vm.AddPrimaryKeyCommand.CanExecute(null));
        Assert.False(vm.AddUniqueCommand.CanExecute(null));
        Assert.False(vm.AddCheckCommand.CanExecute(null));
    }

    [Fact]
    public void CanManageConstraints_TrueWithExecutor()
    {
        using var harness = new ExecutorHarness();
        Assert.True(harness.Vm.CanManageConstraints);
        Assert.True(harness.Vm.AddPrimaryKeyCommand.CanExecute(null));
        Assert.True(harness.Vm.AddUniqueCommand.CanExecute(null));
        Assert.True(harness.Vm.AddCheckCommand.CanExecute(null));
    }

    [Fact]
    public void ActiveConstraint_ResolvesPerInnerSubTab()
    {
        using var harness = new ExecutorHarness();
        var vm = harness.Vm;
        var pk = new ConstraintInfo { Name = "PK_T", ConstraintType = "PRIMARY KEY" };
        var fk = new ConstraintInfo { Name = "FK_T", ConstraintType = "FOREIGN KEY" };
        vm.SelectedPrimaryKey = pk;
        vm.SelectedForeignKey = fk;

        vm.ConstraintsActiveSubTabIndex = TableDetailTabViewModel.ConstraintsPrimaryKeyIndex;
        Assert.Same(pk, vm.ActiveConstraint);

        vm.ConstraintsActiveSubTabIndex = TableDetailTabViewModel.ConstraintsForeignKeysIndex;
        Assert.Same(fk, vm.ActiveConstraint);

        vm.ConstraintsActiveSubTabIndex = TableDetailTabViewModel.ConstraintsCheckIndex;
        Assert.Null(vm.ActiveConstraint); // nothing selected in Check sub-tab
    }

    [Fact]
    public void CanDropConstraint_GatedOnActiveSelection()
    {
        using var harness = new ExecutorHarness();
        var vm = harness.Vm;
        Assert.False(vm.CanDropConstraint);
        Assert.False(vm.DropConstraintCommand.CanExecute(null));

        vm.SelectedPrimaryKey = new ConstraintInfo { Name = "PK_T", ConstraintType = "PRIMARY KEY" };
        vm.ConstraintsActiveSubTabIndex = TableDetailTabViewModel.ConstraintsPrimaryKeyIndex;
        Assert.True(vm.CanDropConstraint);
        Assert.True(vm.DropConstraintCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteAddPrimaryKey_NoExecutor_NoOp()
    {
        var vm = new TableDetailTabViewModel("T");
        await vm.ExecuteAddPrimaryKeyAsync(new ConstraintFieldSpec("PK_T", new[] { "ID" }));
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAddPrimaryKey_NullSpec_NoOp()
    {
        using var harness = new ExecutorHarness();
        await harness.Vm.ExecuteAddPrimaryKeyAsync(null!);
        Assert.Null(harness.Vm.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAddPrimaryKey_Disconnected_SetsErrorNoThrow()
    {
        using var harness = new ExecutorHarness();
        // Executor present but no open connection — the Add is attempted, the
        // DDL builds fine, ExecuteAsync fails, and the failure surfaces as
        // ErrorMessage rather than an exception.
        await harness.Vm.ExecuteAddPrimaryKeyAsync(new ConstraintFieldSpec("PK_T", new[] { "ID" }));
        Assert.NotNull(harness.Vm.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAddCheck_Disconnected_SetsError()
    {
        using var harness = new ExecutorHarness();
        await harness.Vm.ExecuteAddCheckAsync(new CheckConstraintSpec("CHK_T", "ID > 0"));
        Assert.NotNull(harness.Vm.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteDropConstraint_EmptyName_NoOp()
    {
        using var harness = new ExecutorHarness();
        await harness.Vm.ExecuteDropConstraintAsync("   ");
        Assert.Null(harness.Vm.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteDropConstraint_Disconnected_SetsError()
    {
        using var harness = new ExecutorHarness();
        await harness.Vm.ExecuteDropConstraintAsync("FK_T");
        Assert.NotNull(harness.Vm.ErrorMessage);
    }

    [Fact]
    public async Task DropConstraintCommand_ConfirmFalse_DoesNotExecute()
    {
        using var harness = new ExecutorHarness();
        var vm = harness.Vm;
        vm.SelectedPrimaryKey = new ConstraintInfo { Name = "PK_T", ConstraintType = "PRIMARY KEY" };
        vm.ConstraintsActiveSubTabIndex = TableDetailTabViewModel.ConstraintsPrimaryKeyIndex;
        vm.ConfirmationRequested += _ => Task.FromResult(false);

        await vm.DropConstraintCommand.ExecuteAsync(null);

        // Confirm declined → ExecuteDropConstraintAsync never reached → no error.
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task DropConstraintCommand_ConfirmTrue_AttemptsDrop()
    {
        using var harness = new ExecutorHarness();
        var vm = harness.Vm;
        vm.SelectedPrimaryKey = new ConstraintInfo { Name = "PK_T", ConstraintType = "PRIMARY KEY" };
        vm.ConstraintsActiveSubTabIndex = TableDetailTabViewModel.ConstraintsPrimaryKeyIndex;
        vm.ConfirmationRequested += _ => Task.FromResult(true);

        await vm.DropConstraintCommand.ExecuteAsync(null);

        // Confirmed → drop attempted against disconnected executor → error set.
        Assert.NotNull(vm.ErrorMessage);
    }

    private sealed class ExecutorHarness : IDisposable
    {
        public ExecutorHarness()
        {
            Service = new FirebirdConnectionService();
            var executor = new FirebirdDdlExecutor(Service, null);
            Vm = new TableDetailTabViewModel("MY_T", null, null, null, executor, null);
        }

        public FirebirdConnectionService Service { get; }
        public TableDetailTabViewModel Vm { get; }

        public void Dispose() => Service.Dispose();
    }
}
