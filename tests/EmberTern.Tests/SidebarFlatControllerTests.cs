using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

// Phase 1 of the flat-sidebar migration: unit-tests the projection/splice logic of
// SidebarFlatController over a lightweight node so it's verifiable without the GUI or a DB.
public partial class SidebarFlatControllerTests
{
    private sealed partial class Node : ObservableObject
    {
        public string Name { get; init; } = "";
        public bool Expandable { get; init; }
        public ObservableCollection<object> Kids { get; } = new();

        [ObservableProperty] private bool _isExpanded;
        [ObservableProperty] private bool _isVisible = true;

        public override string ToString() => Name;
    }

    private static SidebarFlatController Make(ObservableCollection<object> roots) => new(
        roots,
        childrenSelector: n => ((Node)n).Kids,
        isContainer: n => ((Node)n).Expandable,          // structural
        hasChildren: n => ((Node)n).Kids.Count > 0,      // chevron
        isExpanded: n => ((Node)n).IsExpanded,
        setExpanded: (n, v) => ((Node)n).IsExpanded = v,
        isVisible: n => ((Node)n).IsVisible);

    private static Node N(string name, bool expandable = false) => new() { Name = name, Expandable = expandable };

    private static List<string> Labels(SidebarFlatController c) => c.Rows.Select(r => ((Node)r.Node).Name).ToList();

    [Fact]
    public void Build_CollapsedRoots_ShowsOnlyRoots()
    {
        var a = N("A", true); a.Kids.Add(N("A1"));
        var roots = new ObservableCollection<object> { a, N("B", true) };
        using var c = Make(roots);
        Assert.Equal(new[] { "A", "B" }, Labels(c));
        Assert.All(c.Rows, r => Assert.Equal(0, r.Depth));
    }

    [Fact]
    public void Build_InitiallyExpanded_IncludesChildren()
    {
        var a = N("A", true); a.IsExpanded = true; a.Kids.Add(N("A1")); a.Kids.Add(N("A2"));
        var roots = new ObservableCollection<object> { a };
        using var c = Make(roots);
        Assert.Equal(new[] { "A", "A1", "A2" }, Labels(c));
        Assert.Equal(1, c.Rows[1].Depth);
    }

    [Fact]
    public void Expand_SplicesChildrenIn()
    {
        var a = N("A", true); a.Kids.Add(N("A1")); a.Kids.Add(N("A2"));
        var roots = new ObservableCollection<object> { a, N("B") };
        using var c = Make(roots);
        a.IsExpanded = true;
        Assert.Equal(new[] { "A", "A1", "A2", "B" }, Labels(c));
    }

    [Fact]
    public void Collapse_RemovesDescendants()
    {
        var a = N("A", true); a.IsExpanded = true; a.Kids.Add(N("A1")); a.Kids.Add(N("A2"));
        var roots = new ObservableCollection<object> { a, N("B") };
        using var c = Make(roots);
        a.IsExpanded = false;
        Assert.Equal(new[] { "A", "B" }, Labels(c));
    }

    [Fact]
    public void NestedExpand_RecursesVisibleSubtree()
    {
        var a = N("A", true);
        var a1 = N("A1", true); a1.Kids.Add(N("A1a"));
        a.Kids.Add(a1);
        var roots = new ObservableCollection<object> { a };
        using var c = Make(roots);
        a1.IsExpanded = true; // not projected yet (A collapsed) → no change
        Assert.Equal(new[] { "A" }, Labels(c));
        a.IsExpanded = true;  // A expands; A1 already expanded → grandchild included
        Assert.Equal(new[] { "A", "A1", "A1a" }, Labels(c));
    }

    [Fact]
    public void LazyLoad_AddingChildToExpandedNode_AppearsViaCollectionChanged()
    {
        var a = N("A", true); a.IsExpanded = true;
        var roots = new ObservableCollection<object> { a };
        using var c = Make(roots);
        Assert.Equal(new[] { "A" }, Labels(c));
        a.Kids.Add(N("A1"));       // simulate lazy-load populate
        a.Kids.Add(N("A2"));
        Assert.Equal(new[] { "A", "A1", "A2" }, Labels(c));
    }

    [Fact]
    public void RemovingChildFromExpandedNode_RemovesRow()
    {
        var a = N("A", true); a.IsExpanded = true;
        var a1 = N("A1"); a.Kids.Add(a1); a.Kids.Add(N("A2"));
        var roots = new ObservableCollection<object> { a };
        using var c = Make(roots);
        a.Kids.Remove(a1);
        Assert.Equal(new[] { "A", "A2" }, Labels(c));
    }

    [Fact]
    public void RootsCollectionChange_Rebuilds()
    {
        var roots = new ObservableCollection<object> { N("A") };
        using var c = Make(roots);
        roots.Add(N("B"));
        Assert.Equal(new[] { "A", "B" }, Labels(c));
        roots.RemoveAt(0);
        Assert.Equal(new[] { "B" }, Labels(c));
    }

    [Fact]
    public void Filter_HiddenNode_ExcludedAfterRebuild()
    {
        var a = N("A", true); a.IsExpanded = true;
        var a1 = N("A1"); var a2 = N("A2");
        a.Kids.Add(a1); a.Kids.Add(a2);
        var roots = new ObservableCollection<object> { a };
        using var c = Make(roots);
        Assert.Equal(new[] { "A", "A1", "A2" }, Labels(c));
        a2.IsVisible = false;   // filter marks it hidden
        c.Rebuild();            // filter re-apply triggers a full rebuild
        Assert.Equal(new[] { "A", "A1" }, Labels(c));
    }

    [Fact]
    public void Toggle_FlipsExpansionAndSplices()
    {
        var a = N("A", true); a.Kids.Add(N("A1"));
        var roots = new ObservableCollection<object> { a };
        using var c = Make(roots);
        c.Toggle(c.Rows[0]);
        Assert.True(a.IsExpanded);
        Assert.Equal(new[] { "A", "A1" }, Labels(c));
        c.Toggle(c.Rows[0]);
        Assert.False(a.IsExpanded);
        Assert.Equal(new[] { "A" }, Labels(c));
    }

    [Fact]
    public void AddingFirstChild_MakesRowExpandable_AndEmptyingRemovesIt()
    {
        var a = N("A", expandable: true);   // a container, but no children yet
        var roots = new ObservableCollection<object> { a };
        using var c = Make(roots);
        Assert.False(c.Rows[0].IsExpandable, "no chevron for an empty container");
        a.Kids.Add(N("A1"));
        Assert.True(c.Rows[0].IsExpandable, "chevron appears once it has a child");
        a.Kids.Clear();
        Assert.False(c.Rows[0].IsExpandable, "chevron disappears when empty again");
    }

    [Fact]
    public void Row_MirrorsExpandableAndExpandedState()
    {
        var a = N("A", true); a.Kids.Add(N("A1"));
        var leaf = N("L");
        var roots = new ObservableCollection<object> { a, leaf };
        using var c = Make(roots);
        Assert.True(c.Rows[0].IsExpandable);
        Assert.False(c.Rows[0].IsExpanded);
        Assert.False(c.Rows[1].IsExpandable);
        a.IsExpanded = true;
        Assert.True(c.Rows[0].IsExpanded);
    }
}
