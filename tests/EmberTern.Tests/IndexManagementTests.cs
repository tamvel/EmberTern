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
/// Index Management V1 — DdlGenerator builders, the Add-Index dialog VM, the
/// constraint-backed drop guard, and the disconnected-executor error paths.
/// Live-DB success paths (Add survives to RefreshStructureAsync + selects the
/// new index) need a real Firebird; the disconnected executor exercises the
/// error branch instead, matching ConstraintManagementTests.
/// </summary>
public class IndexManagementTests
{
    // ─── DdlGenerator.BuildCreateIndex / BuildDropIndex ───────────────────

    [Fact]
    public void BuildCreateIndex_PlainSingleField()
    {
        var sql = DdlGenerator.BuildCreateIndex("USERS", "IDX_USERS_NAME", new[] { "NAME" }, unique: false, descending: false);
        Assert.Equal("CREATE INDEX \"IDX_USERS_NAME\" ON \"USERS\" (\"NAME\")", sql);
    }

    [Fact]
    public void BuildCreateIndex_UniqueComposite()
    {
        var sql = DdlGenerator.BuildCreateIndex("T", "IX", new[] { "A", "B" }, unique: true, descending: false);
        Assert.Equal("CREATE UNIQUE INDEX \"IX\" ON \"T\" (\"A\", \"B\")", sql);
    }

    [Fact]
    public void BuildCreateIndex_Descending()
    {
        var sql = DdlGenerator.BuildCreateIndex("T", "IX", new[] { "A" }, unique: false, descending: true);
        Assert.Equal("CREATE DESCENDING INDEX \"IX\" ON \"T\" (\"A\")", sql);
    }

    [Fact]
    public void BuildCreateIndex_UniqueDescending()
    {
        var sql = DdlGenerator.BuildCreateIndex("T", "IX", new[] { "A" }, unique: true, descending: true);
        Assert.Equal("CREATE UNIQUE DESCENDING INDEX \"IX\" ON \"T\" (\"A\")", sql);
    }

    [Fact]
    public void BuildCreateIndex_ComputedExpression_IgnoresFields()
    {
        var sql = DdlGenerator.BuildCreateIndex("T", "IX", Array.Empty<string>(), unique: false, descending: false, computedExpression: "UPPER(NAME)");
        Assert.Equal("CREATE INDEX \"IX\" ON \"T\" COMPUTED BY (UPPER(NAME))", sql);
    }

    [Fact]
    public void BuildCreateIndex_EscapesInternalQuotes()
    {
        var sql = DdlGenerator.BuildCreateIndex("T\"X", "I\"X", new[] { "A\"B" }, false, false);
        Assert.Contains("\"T\"\"X\"", sql);
        Assert.Contains("\"I\"\"X\"", sql);
        Assert.Contains("\"A\"\"B\"", sql);
    }

    [Theory]
    [InlineData("", "IX")]
    [InlineData("   ", "IX")]
    [InlineData("T", "")]
    [InlineData("T", "  ")]
    public void BuildCreateIndex_ThrowsOnMissingTableOrName(string table, string name)
    {
        Assert.Throws<ArgumentException>(() =>
            DdlGenerator.BuildCreateIndex(table, name, new[] { "A" }, false, false));
    }

    [Fact]
    public void BuildCreateIndex_ThrowsWhenNoFieldsAndNoExpression()
    {
        Assert.Throws<ArgumentException>(() =>
            DdlGenerator.BuildCreateIndex("T", "IX", Array.Empty<string>(), false, false));
    }

    [Fact]
    public void BuildDropIndex_Quotes()
    {
        Assert.Equal("DROP INDEX \"IDX_X\"", DdlGenerator.BuildDropIndex("IDX_X"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildDropIndex_ThrowsOnEmpty(string name)
    {
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildDropIndex(name));
    }

    // ─── DdlGenerator.BuildSetIndexStatistics (Recompute statistics) ──────

    [Fact]
    public void BuildSetIndexStatistics_Quotes()
    {
        Assert.Equal("SET STATISTICS INDEX \"IDX_X\"", DdlGenerator.BuildSetIndexStatistics("IDX_X"));
    }

    [Fact]
    public void BuildSetIndexStatistics_EscapesInternalQuotes()
    {
        Assert.Equal("SET STATISTICS INDEX \"I\"\"X\"", DdlGenerator.BuildSetIndexStatistics("I\"X"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildSetIndexStatistics_ThrowsOnEmpty(string name)
    {
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildSetIndexStatistics(name));
    }

    // ─── IndexDialogViewModel ─────────────────────────────────────────────

    [Fact]
    public void Dialog_DefaultName_FromTable()
    {
        var vm = new IndexDialogViewModel("USERS", new[] { "ID", "NAME" });
        Assert.Equal("IDX_USERS", vm.ConstraintName);
    }

    [Fact]
    public void Dialog_IsValid_RequiresNameAndFieldOrExpression()
    {
        var vm = new IndexDialogViewModel("T", new[] { "A" });
        Assert.False(vm.IsValid());          // name present (default) but no field selected
        vm.Fields[0].IsSelected = true;
        Assert.True(vm.IsValid());
    }

    [Fact]
    public void Dialog_ComputedExpression_DisablesFieldPicker_AndValidatesWithoutFields()
    {
        var vm = new IndexDialogViewModel("T", new[] { "A" });
        Assert.True(vm.IsFieldPickerEnabled);
        vm.ComputedExpression = "UPPER(A)";
        Assert.True(vm.HasComputed);
        Assert.False(vm.IsFieldPickerEnabled);
        Assert.True(vm.IsValid());           // expression index needs no field
    }

    [Fact]
    public void Dialog_BuildResult_CarriesFlags()
    {
        var vm = new IndexDialogViewModel("T", new[] { "A", "B" });
        vm.ConstraintName = "MY_IX";
        vm.Fields[0].IsSelected = true;
        vm.Unique = true;
        vm.Descending = true;
        var spec = vm.BuildResult();
        Assert.Equal("MY_IX", spec.Name);
        Assert.Equal(new[] { "A" }, spec.Fields);
        Assert.True(spec.Unique);
        Assert.True(spec.Descending);
        Assert.Null(spec.ComputedExpression);
    }

    [Fact]
    public void Dialog_DdlPreview_TracksState()
    {
        var vm = new IndexDialogViewModel("T", new[] { "A" });
        vm.Fields[0].IsSelected = true;
        Assert.Contains("CREATE INDEX", vm.DdlPreview);
        vm.Unique = true;
        Assert.Contains("UNIQUE", vm.DdlPreview);
    }

    [Fact]
    public void Dialog_Accept_SetsResult_Cancel_Nulls()
    {
        var vm = new IndexDialogViewModel("T", new[] { "A" });
        vm.Fields[0].IsSelected = true;
        var closed = 0;
        vm.RequestClose += () => closed++;
        vm.AcceptCommand.Execute(null);
        Assert.NotNull(vm.Result);
        Assert.Equal(1, closed);

        var vm2 = new IndexDialogViewModel("T", new[] { "A" });
        vm2.CancelCommand.Execute(null);
        Assert.Null(vm2.Result);
    }

    // ─── Constraint-backed drop guard (IsConstraintBackedIndex) ───────────

    [Fact]
    public void IsConstraintBacked_PrimaryAndForeignKeyIndexes()
    {
        var vm = new TableDetailTabViewModel("T");
        Assert.True(vm.IsConstraintBackedIndex(new IndexInfo { Name = "PK_X", IndexType = "PRIMARY KEY" }));
        Assert.True(vm.IsConstraintBackedIndex(new IndexInfo { Name = "FK_X", IndexType = "FOREIGN KEY" }));
        Assert.False(vm.IsConstraintBackedIndex(new IndexInfo { Name = "IDX_PLAIN" }));
        Assert.False(vm.IsConstraintBackedIndex(null));
    }

    [Fact]
    public void IsConstraintBacked_UniqueConstraintBackingIndex_MatchedByIndexName()
    {
        var vm = new TableDetailTabViewModel("T");
        // A UNIQUE constraint's backing index has IsUnique=true but IndexType=""
        // — it's recognized via the constraint's IndexName.
        vm.Constraints.Add(new ConstraintInfo { Name = "UQ_X", ConstraintType = "UNIQUE", IndexName = "UQ_IX" });
        Assert.True(vm.IsConstraintBackedIndex(new IndexInfo { Name = "UQ_IX", IsUnique = true }));
        // A plain unique index NOT backing a constraint is droppable.
        Assert.False(vm.IsConstraintBackedIndex(new IndexInfo { Name = "OTHER_IX", IsUnique = true }));
    }

    // ─── VM Add/Drop — disconnected executor error paths ──────────────────

    [Fact]
    public async Task ExecuteAddIndex_Buffered_QueuesPendingAddedNoError()
    {
        using var harness = new ExecutorHarness();
        await harness.Vm.ExecuteAddIndexAsync(new IndexSpec("IX", new[] { "A" }, false, false, null));
        // BUFFERED: queues CREATE INDEX + a pending-Added row, no DDL → no error.
        Assert.Null(harness.Vm.ErrorMessage);
        Assert.Single(harness.Vm.PendingChanges);
        Assert.Contains(harness.Vm.Indexes, i => i.Name == "IX" && i.PendingState == PendingChangeKind.Added);
    }

    [Fact]
    public async Task ExecuteDropIndex_Buffered_QueuesNoError()
    {
        using var harness = new ExecutorHarness();
        await harness.Vm.ExecuteDropIndexAsync("IX");
        // BUFFERED: queues DROP INDEX, no DDL → no error.
        Assert.Null(harness.Vm.ErrorMessage);
        Assert.Single(harness.Vm.PendingChanges);
    }

    [Fact]
    public async Task ExecuteDropIndex_EmptyName_NoOp()
    {
        using var harness = new ExecutorHarness();
        await harness.Vm.ExecuteDropIndexAsync("  ");
        Assert.Null(harness.Vm.ErrorMessage);
    }

    [Fact]
    public async Task DropIndexCommand_ConstraintBacked_BlockedWithMessage()
    {
        using var harness = new ExecutorHarness();
        var vm = harness.Vm;
        vm.Constraints.Add(new ConstraintInfo { Name = "PK_T", ConstraintType = "PRIMARY KEY", IndexName = "PK_IX" });
        vm.SelectedIndex = new IndexInfo { Name = "PK_IX", IndexType = "PRIMARY KEY" };

        await vm.DropIndexCommand.ExecuteAsync(null);

        // Blocked before any DDL — the message names the index but isn't the
        // generic "failed to apply" execution error.
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("PK_IX", vm.ErrorMessage);
    }

    [Fact]
    public async Task DropIndexCommand_PlainIndex_ConfirmFalse_DoesNotExecute()
    {
        using var harness = new ExecutorHarness();
        var vm = harness.Vm;
        vm.SelectedIndex = new IndexInfo { Name = "IDX_PLAIN" };
        vm.ConfirmationRequested += _ => Task.FromResult(false);

        await vm.DropIndexCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
    }

    // ─── Recompute statistics (Przelicz statystykę / wszystkie) ──────────

    [Fact]
    public void CanRecomputeIndexStatistics_RequiresExecutorAndSelection()
    {
        // No executor → never available.
        var noExec = new TableDetailTabViewModel("T");
        Assert.False(noExec.CanRecomputeIndexStatistics);

        using var harness = new ExecutorHarness();
        Assert.False(harness.Vm.CanRecomputeIndexStatistics);     // no selection yet
        harness.Vm.SelectedIndex = new IndexInfo { Name = "IDX_A" };
        Assert.True(harness.Vm.CanRecomputeIndexStatistics);
    }

    [Fact]
    public void CanRecomputeAllIndexStatistics_RequiresExecutorAndIndexes()
    {
        var noExec = new TableDetailTabViewModel("T");
        Assert.False(noExec.CanRecomputeAllIndexStatistics);

        using var harness = new ExecutorHarness();
        Assert.False(harness.Vm.CanRecomputeAllIndexStatistics);  // no indexes
        harness.Vm.Indexes.Add(new IndexInfo { Name = "IDX_A" });
        Assert.True(harness.Vm.CanRecomputeAllIndexStatistics);
    }

    [Fact]
    public async Task RecomputeAll_Disconnected_ContinuesPastFailures_ReportsEachAndCount()
    {
        using var harness = new ExecutorHarness();
        var vm = harness.Vm;

        await vm.RecomputeStatisticsForAsync(new[] { "IDX_A", "IDX_B", "IDX_C" }, single: false);

        // Every index was attempted despite each one failing (disconnected executor)
        // — completion line reports 0 of 3, error lists all three names.
        Assert.NotNull(vm.StatusMessage);
        Assert.Contains("0 of 3", vm.StatusMessage);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("IDX_A", vm.ErrorMessage);
        Assert.Contains("IDX_B", vm.ErrorMessage);
        Assert.Contains("IDX_C", vm.ErrorMessage);
    }

    [Fact]
    public async Task RecomputeStatisticsFor_EmptyList_NoOp()
    {
        using var harness = new ExecutorHarness();
        await harness.Vm.RecomputeStatisticsForAsync(System.Array.Empty<string>(), single: false);
        Assert.Null(harness.Vm.StatusMessage);
        Assert.Null(harness.Vm.ErrorMessage);
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
