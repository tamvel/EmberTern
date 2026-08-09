using System.Collections.Generic;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

public class GridLayoutOrderingTests
{
    [Fact]
    public void NoSavedOrder_ReturnsCurrentUnchanged()
    {
        var current = new[] { "A", "B", "C" };
        Assert.Equal(current, GridLayoutOrdering.OrderedNames(current, null));
        Assert.Equal(current, GridLayoutOrdering.OrderedNames(current, new List<string>()));
    }

    [Fact]
    public void ExactSavedOrder_ReordersToMatch()
    {
        var current = new[] { "A", "B", "C" };
        var saved = new[] { "C", "A", "B" };
        Assert.Equal(new[] { "C", "A", "B" }, GridLayoutOrdering.OrderedNames(current, saved));
    }

    [Fact]
    public void NewColumns_AppendInCurrentOrderAfterSaved()
    {
        // Saved knows B,A; current also has C,D added since — they keep their order at the end.
        var current = new[] { "A", "B", "C", "D" };
        var saved = new[] { "B", "A" };
        Assert.Equal(new[] { "B", "A", "C", "D" }, GridLayoutOrdering.OrderedNames(current, saved));
    }

    [Fact]
    public void RemovedColumns_AreSkipped()
    {
        // Saved references "X" which no longer exists in current.
        var current = new[] { "A", "B" };
        var saved = new[] { "X", "B", "A" };
        Assert.Equal(new[] { "B", "A" }, GridLayoutOrdering.OrderedNames(current, saved));
    }

    [Fact]
    public void DuplicateSavedEntries_CollapseToOne()
    {
        var current = new[] { "A", "B" };
        var saved = new[] { "A", "A", "B" };
        Assert.Equal(new[] { "A", "B" }, GridLayoutOrdering.OrderedNames(current, saved));
    }

    // ── Whose order is it? (user report 2026-08-03: result columns out of SELECT order) ──────────
    //
    // The ordering above is correct and was never the defect. What was wrong is that it ran at all on a grid
    // whose columns the user cannot arrange: the SQL editor's result grid shares ONE profile across every query
    // (GridId="QueryResults"), so an earlier result's column order was replayed onto the next result's columns.
    //
    // ⭐ The rule that fixed it is read from the grid — "order is remembered only where the user can set it" —
    // and it is pinned here because the failure mode of losing it is invisible in tests and immediate on screen.
    // A bare DataGrid needs no headless session, so this stays in the ordinary partition.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ColumnOrder_IsRemembered_OnlyWhereTheUserCanReorder(bool canReorder)
    {
        var grid = new Avalonia.Controls.DataGrid { CanUserReorderColumns = canReorder };
        Assert.Equal(canReorder, EmberTern.App.Behaviors.GridLayoutBehavior.RemembersOrder(grid));
    }
}
