using System.Linq;
using EmberTern.Core.Metadata;
using EmberTern.Core.Search;
using Xunit;

namespace EmberTern.Tests;

public class MetadataSearchResultsTests
{
    private static MetadataSearchHit Hit(MetadataObjectKind kind, string name, SearchMatchLocation loc, int count, string? detail = null)
        => new(kind, name, loc, count, detail);

    [Fact]
    public void Group_MergesNameAndSourceForSameObject_SumsCount()
    {
        var groups = MetadataSearchResults.Group(new[]
        {
            Hit(MetadataObjectKind.Procedure, "P1", SearchMatchLocation.Name, 1),
            Hit(MetadataObjectKind.Procedure, "P1", SearchMatchLocation.Source, 3),
        });

        var leaf = Assert.Single(Assert.Single(groups).Leaves);
        Assert.Equal("P1", leaf.ObjectName);
        Assert.Equal(4, leaf.MatchCount);
        Assert.Equal(SearchMatchLocation.Source, leaf.Location); // Source beats Name for display
    }

    [Fact]
    public void Group_FieldHits_AreSeparateLeavesUnderTables()
    {
        var groups = MetadataSearchResults.Group(new[]
        {
            Hit(MetadataObjectKind.Table, "TOWARY", SearchMatchLocation.FieldName, 7, "ID_MAGAZYN"),
            Hit(MetadataObjectKind.Table, "TOWARY", SearchMatchLocation.FieldName, 1, "MAGAZYN_X"),
            Hit(MetadataObjectKind.Table, "TOWARY", SearchMatchLocation.Name, 1),
        });

        var tables = Assert.Single(groups);
        Assert.Equal(MetadataObjectKind.Table, tables.Kind);
        Assert.Equal(3, tables.Leaves.Count); // name leaf + 2 field leaves
        Assert.Contains(tables.Leaves, l => l.DetailName == "ID_MAGAZYN" && l.MatchCount == 7);
        Assert.Contains(tables.Leaves, l => l.DetailName is null && l.Location == SearchMatchLocation.Name);
    }

    [Fact]
    public void Group_OrdersGroupsInDisplayOrder()
    {
        var groups = MetadataSearchResults.Group(new[]
        {
            Hit(MetadataObjectKind.Exception, "E", SearchMatchLocation.Message, 1),
            Hit(MetadataObjectKind.Procedure, "P", SearchMatchLocation.Source, 1),
            Hit(MetadataObjectKind.Table, "T", SearchMatchLocation.Name, 1),
        });

        var kinds = groups.Select(g => g.Kind).ToList();
        Assert.Equal(new[] { MetadataObjectKind.Procedure, MetadataObjectKind.Table, MetadataObjectKind.Exception }, kinds);
    }

    [Fact]
    public void Group_LeavesSortedByNameThenField()
    {
        var groups = MetadataSearchResults.Group(new[]
        {
            Hit(MetadataObjectKind.Table, "B", SearchMatchLocation.FieldName, 1, "Z"),
            Hit(MetadataObjectKind.Table, "B", SearchMatchLocation.FieldName, 1, "A"),
            Hit(MetadataObjectKind.Table, "A", SearchMatchLocation.Name, 1),
        });

        var labels = groups.Single().Leaves.Select(l => l.ObjectName + "." + (l.DetailName ?? "")).ToList();
        Assert.Equal(new[] { "A.", "B.A", "B.Z" }, labels);
    }

    [Fact]
    public void Group_DropsKindsNotInDisplayOrder()
    {
        var groups = MetadataSearchResults.Group(new[]
        {
            Hit(MetadataObjectKind.Index, "IX", SearchMatchLocation.Name, 1), // not a Global Search kind
        });
        Assert.Empty(groups);
    }

    [Fact]
    public void Group_Empty_ReturnsEmpty()
        => Assert.Empty(MetadataSearchResults.Group(System.Array.Empty<MetadataSearchHit>()));

    [Fact]
    public void Group_SkipsBlankObjectNames()
    {
        var groups = MetadataSearchResults.Group(new[] { Hit(MetadataObjectKind.View, "", SearchMatchLocation.Name, 1) });
        Assert.Empty(groups);
    }

    [Fact]
    public void Group_FieldNameLocation_WinsOverName()
    {
        // Same table+field arriving as both Name-ish and FieldName → FieldName location kept.
        var groups = MetadataSearchResults.Group(new[]
        {
            Hit(MetadataObjectKind.Table, "T", SearchMatchLocation.FieldName, 2, "F"),
        });
        var leaf = groups.Single().Leaves.Single();
        Assert.Equal(SearchMatchLocation.FieldName, leaf.Location);
        Assert.Equal("F", leaf.DetailName);
    }
}
