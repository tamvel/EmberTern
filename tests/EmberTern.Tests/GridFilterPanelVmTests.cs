using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

// Faza 3 — shared, host-agnostic filter panel + aggregation bar VMs (pure, no
// UI / DB). Host wiring (client vs server) is exercised via the callbacks.
public class GridFilterPanelVmTests
{
    private static readonly GridColumnRef Amount = new(0, "AMOUNT", typeof(decimal));
    private static readonly GridColumnRef Name = new(1, "NAME", typeof(string));
    private static readonly GridColumnRef Created = new(2, "CREATED", typeof(DateTime));
    private static IReadOnlyList<GridColumnRef> Cols => new[] { Amount, Name, Created };

    // ── Condition row ─────────────────────────────────────────────────────
    [Fact]
    public void Row_Operators_FollowColumnCategory()
    {
        var row = new FilterConditionRowViewModel(Cols, _ => { });
        // default = first column (AMOUNT, numeric) → ordering ops, no Contains
        Assert.DoesNotContain(row.AvailableOperators, o => o.Operator == GridFilterOperator.Contains);
        Assert.Contains(row.AvailableOperators, o => o.Operator == GridFilterOperator.GreaterThan);

        row.SelectedColumn = Name; // text → Contains appears
        Assert.Contains(row.AvailableOperators, o => o.Operator == GridFilterOperator.Contains);
    }

    [Fact]
    public void Row_ChangingColumn_KeepsCompatibleOperator_ElseResets()
    {
        var row = new FilterConditionRowViewModel(Cols, _ => { });
        row.SelectedColumn = Name;
        row.SelectedOperator = row.AvailableOperators.First(o => o.Operator == GridFilterOperator.Contains);

        row.SelectedColumn = Amount; // numeric has no Contains → falls back to first op
        Assert.NotNull(row.SelectedOperator);
        Assert.NotEqual(GridFilterOperator.Contains, row.SelectedOperator!.Operator);
    }

    [Fact]
    public void Row_ValueEditorKind_NoneForNullOps_SingleOtherwise()
    {
        var row = new FilterConditionRowViewModel(Cols, _ => { });
        row.SelectedColumn = Name;
        row.SelectedOperator = row.AvailableOperators.First(o => o.Operator == GridFilterOperator.Equals);
        Assert.Equal(ValueEditorKind.Single, row.ValueEditorKind);
        Assert.True(row.ShowValueEditor);

        row.SelectedOperator = row.AvailableOperators.First(o => o.Operator == GridFilterOperator.IsNull);
        Assert.Equal(ValueEditorKind.None, row.ValueEditorKind);
        Assert.False(row.ShowValueEditor);
    }

    [Fact]
    public void Row_TryBuild_NullWhenIncomplete_BuildsWhenComplete()
    {
        var row = new FilterConditionRowViewModel(Cols, _ => { });
        row.SelectedColumn = Amount;
        row.SelectedOperator = row.AvailableOperators.First(o => o.Operator == GridFilterOperator.GreaterThan);
        Assert.Null(row.TryBuild()); // value empty

        row.Value = "1000";
        var c = row.TryBuild();
        Assert.NotNull(c);
        Assert.Equal(0, c!.ColumnIndex);
        Assert.Equal(GridFilterOperator.GreaterThan, c.Operator);
        Assert.Equal("1000", c.Value);
    }

    [Fact]
    public void Row_TryBuild_NullOp_IgnoresValue()
    {
        var row = new FilterConditionRowViewModel(Cols, _ => { });
        row.SelectedColumn = Name;
        row.SelectedOperator = row.AvailableOperators.First(o => o.Operator == GridFilterOperator.IsNull);
        row.Value = "ignored";
        var c = row.TryBuild();
        Assert.NotNull(c);
        Assert.Null(c!.Value);
    }

    // ── Filter panel ──────────────────────────────────────────────────────
    [Fact]
    public void Panel_AddCondition_RequiresColumns()
    {
        var p = new FilterPanelViewModel();
        Assert.False(p.AddConditionCommand.CanExecute(null));
        p.SetColumns(Cols);
        Assert.True(p.AddConditionCommand.CanExecute(null));
        p.AddConditionCommand.Execute(null);
        Assert.Single(p.Conditions);
        Assert.False(p.ShowEmptyHint);
    }

    [Fact]
    public async Task Panel_Apply_BuildsFilter_InvokesHost_SetsActive()
    {
        GridFilter? applied = null;
        var p = new FilterPanelViewModel { ApplyRequested = f => { applied = f; return Task.CompletedTask; } };
        p.SetColumns(Cols);
        p.AddConditionCommand.Execute(null);
        var row = p.Conditions[0];
        row.SelectedColumn = Name;
        row.SelectedOperator = row.AvailableOperators.First(o => o.Operator == GridFilterOperator.Equals);
        row.Value = "ACME";

        await p.ApplyCommand.ExecuteAsync(null);

        Assert.NotNull(applied);
        Assert.True(p.IsFilterActive);
        var c = Assert.Single(applied!.Conditions);
        Assert.Equal("NAME", c.ColumnName);
        Assert.Equal(GridFilterCombine.And, applied.Combine);
    }

    [Fact]
    public async Task Panel_MatchAny_ProducesOrCombine()
    {
        GridFilter? applied = null;
        var p = new FilterPanelViewModel { ApplyRequested = f => { applied = f; return Task.CompletedTask; }, MatchAny = true };
        p.SetColumns(Cols);
        p.AddConditionCommand.Execute(null);
        p.Conditions[0].Value = "1"; // numeric AMOUNT default op takes value
        await p.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(GridFilterCombine.Or, applied!.Combine);
    }

    [Fact]
    public async Task Panel_Clear_EmptiesAndAppliesEmpty()
    {
        GridFilter? applied = null;
        var p = new FilterPanelViewModel { ApplyRequested = f => { applied = f; return Task.CompletedTask; } };
        p.SetColumns(Cols);
        p.AddConditionCommand.Execute(null);
        p.Conditions[0].Value = "5";
        await p.ApplyCommand.ExecuteAsync(null);
        Assert.True(p.IsFilterActive);

        await p.ClearCommand.ExecuteAsync(null);
        Assert.Empty(p.Conditions);
        Assert.False(p.IsFilterActive);
        Assert.True(applied!.IsEmpty);
    }

    [Fact]
    public async Task Panel_ApplyFromCell_AddsPresetConditionAndApplies()
    {
        GridFilter? applied = null;
        var p = new FilterPanelViewModel { ApplyRequested = f => { applied = f; return Task.CompletedTask; } };
        p.SetColumns(Cols);

        await p.ApplyFromCellAsync(1, GridFilterOperator.Equals, "ACME");

        Assert.True(p.IsPanelOpen);
        Assert.True(p.IsFilterActive);
        var c = Assert.Single(applied!.Conditions);
        Assert.Equal("NAME", c.ColumnName);
        Assert.Equal("ACME", c.Value);
    }

    [Fact]
    public void Panel_SetColumns_ResetsConditionsAndFilter()
    {
        var p = new FilterPanelViewModel();
        p.SetColumns(Cols);
        p.AddConditionCommand.Execute(null);
        Assert.Single(p.Conditions);

        p.SetColumns(Cols); // e.g. new result set
        Assert.Empty(p.Conditions);
        Assert.False(p.IsFilterActive);
    }

    // ── Aggregation bar (column-first chips) ──────────────────────────────
    [Fact]
    public void AggregationBar_AddAggregate_ComputesViaCallback()
    {
        var calls = new List<(GridColumnRef Col, GridAggregate Agg)>();
        var bar = new AggregationBarViewModel((col, agg) =>
        {
            calls.Add((col, agg));
            return Task.FromResult<object?>(1234L);
        });
        bar.SetColumns(Cols);

        // Adding a chip computes it immediately (synchronous callback).
        var line = bar.AddAggregate(Amount, GridAggregate.Sum);

        Assert.Same(line, Assert.Single(bar.Lines));
        Assert.Equal("1234", line.ResultText);
        Assert.Equal("AMOUNT · " + GridFilterCatalog.Label(GridAggregate.Sum), line.Label);
        Assert.Single(calls);
        Assert.False(bar.ShowEmptyHint);
    }

    [Fact]
    public void AggregationBar_AddRow_FunctionsFollowColumnCategory()
    {
        var bar = new AggregationBarViewModel((_, _) => Task.FromResult<object?>(null));
        bar.SetColumns(Cols);

        // Default add-row column = first (AMOUNT, numeric) → SUM available.
        Assert.Equal(Amount, bar.SelectedMenuColumn);
        Assert.Contains(bar.MenuFunctions, f => f.Aggregate == GridAggregate.Sum);

        bar.SelectedMenuColumn = Name; // text → no SUM, but COUNT DISTINCT
        Assert.DoesNotContain(bar.MenuFunctions, f => f.Aggregate == GridAggregate.Sum);
        Assert.Contains(bar.MenuFunctions, f => f.Aggregate == GridAggregate.CountDistinct);
    }

    [Fact]
    public void AggregationBar_PickingFunction_AutoAddsChip()
    {
        var bar = new AggregationBarViewModel((_, _) => Task.FromResult<object?>(9L));
        bar.SetColumns(Cols);
        bar.SelectedMenuColumn = Name;

        // Picking a function IS the add action — no separate "+ Add" step.
        bar.SelectedMenuFunction = bar.MenuFunctions.First(f => f.Aggregate == GridAggregate.CountDistinct);

        var line = Assert.Single(bar.Lines);
        Assert.Equal(Name, line.Column);
        Assert.Equal(GridAggregate.CountDistinct, line.Function.Aggregate);
        Assert.Equal("9", line.ResultText);
        // The picker resets to its placeholder so the same function can be re-picked.
        Assert.Null(bar.SelectedMenuFunction);
    }

    [Fact]
    public void AggregationBar_ColumnChange_ResetsFunctionAndAddsNothing()
    {
        var bar = new AggregationBarViewModel((_, _) => Task.FromResult<object?>(1L));
        bar.SetColumns(Cols);

        // Selecting a column alone never adds a chip (the function stays on placeholder).
        bar.SelectedMenuColumn = Amount;
        Assert.Null(bar.SelectedMenuFunction);
        Assert.Empty(bar.Lines);
        Assert.True(bar.ShowEmptyHint);
    }

    [Fact]
    public async Task AggregationBar_RecomputeAll_RefreshesEveryChip()
    {
        int calls = 0;
        var bar = new AggregationBarViewModel((_, _) => { calls++; return Task.FromResult<object?>(7L); });
        bar.SetColumns(Cols);
        bar.AddAggregate(Amount, GridAggregate.Sum); // calls=1
        bar.AddAggregate(Amount, GridAggregate.Avg); // calls=2

        await bar.RecomputeAllAsync(); // +2

        Assert.Equal(4, calls);
        Assert.All(bar.Lines, l => Assert.Equal("7", l.ResultText));
    }

    [Fact]
    public void AggregationBar_Remove_DropsChip()
    {
        var bar = new AggregationBarViewModel((_, _) => Task.FromResult<object?>(1L));
        bar.SetColumns(Cols);
        var line = bar.AddAggregate(Amount, GridAggregate.Sum);
        Assert.False(bar.ShowEmptyHint);

        line.RemoveCommand.Execute(null);

        Assert.Empty(bar.Lines);
        Assert.True(bar.ShowEmptyHint);
    }

    [Fact]
    public void AggregationBar_NullResult_ShowsPlaceholder()
    {
        var bar = new AggregationBarViewModel((_, _) => Task.FromResult<object?>(null));
        bar.SetColumns(Cols);
        bar.AddAggregate(Amount, GridAggregate.Sum);
        Assert.Equal(UiStrings.AggregationNullResult, bar.Lines[0].ResultText);
    }
}
