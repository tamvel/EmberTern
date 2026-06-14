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
}
