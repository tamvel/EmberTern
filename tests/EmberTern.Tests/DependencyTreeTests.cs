using System.Collections.Generic;
using System.Linq;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

public class DependencyTreeTests
{
    [Fact]
    public void DependencyGroupNode_HeaderFormat_IncludesCount()
    {
        var node = new DependencyGroupNode
        {
            ObjectType = "Table",
            Children = new[]
            {
                new DependencyLeafNode { Dependency = new DependencyInfo { ObjectName = "A", ObjectType = "Table" } },
                new DependencyLeafNode { Dependency = new DependencyInfo { ObjectName = "B", ObjectType = "Table" } },
                new DependencyLeafNode { Dependency = new DependencyInfo { ObjectName = "C", ObjectType = "Table" } },
            },
        };
        Assert.Equal("Table (3)", node.Header);
        Assert.Equal(3, node.Count);
    }

    [Fact]
    public void DependencyGroupNode_EmptyChildren_RendersZeroCount()
    {
        var node = new DependencyGroupNode { ObjectType = "View" };
        Assert.Equal("View (0)", node.Header);
        Assert.Equal(0, node.Count);
    }

    [Fact]
    public void BuildDependencyTree_AlwaysReturnsAllCategoriesInIbExpertOrder()
    {
        var tree = TableDetailTabViewModel.BuildDependencyTree(System.Array.Empty<DependencyInfo>());

        var expected = new[]
        {
            UiStrings.MetadataGroupDomains,
            UiStrings.MetadataGroupTables,
            UiStrings.MetadataGroupViews,
            UiStrings.MetadataGroupProcedures,
            UiStrings.MetadataGroupFunctions,
            UiStrings.MetadataGroupPackages,
            UiStrings.MetadataGroupTriggers,
            UiStrings.MetadataGroupExceptions,
            UiStrings.DependencyCategoryUdfs,
            UiStrings.MetadataGroupGenerators,
            UiStrings.MetadataGroupIndexes,
        };
        Assert.Equal(expected, tree.Select(g => g.ObjectType));
        Assert.All(tree, g => Assert.Empty(g.Children));
    }

    [Fact]
    public void BuildDependencyTree_PopulatesMatchingCategoriesAndLeavesOthersEmpty()
    {
        var deps = new List<DependencyInfo>
        {
            new() { ObjectName = "T_USERS",  ObjectType = "Table" },
            new() { ObjectName = "V_USERS",  ObjectType = "View" },
            new() { ObjectName = "T_ORDERS", ObjectType = "Table" },
            new() { ObjectName = "TRG_INS",  ObjectType = "Trigger" },
        };

        var tree = TableDetailTabViewModel.BuildDependencyTree(deps);

        // 11 categories always — non-matching ones come back with empty Children.
        Assert.Equal(11, tree.Count);

        var tables = tree.Single(g => g.ObjectType == UiStrings.MetadataGroupTables);
        Assert.Equal(2, tables.Count);
        Assert.Equal(new[] { "T_ORDERS", "T_USERS" }, tables.Children.Select(c => c.ObjectName));

        var views = tree.Single(g => g.ObjectType == UiStrings.MetadataGroupViews);
        Assert.Single(views.Children);

        var triggers = tree.Single(g => g.ObjectType == UiStrings.MetadataGroupTriggers);
        Assert.Single(triggers.Children);

        var domains = tree.Single(g => g.ObjectType == UiStrings.MetadataGroupDomains);
        Assert.Empty(domains.Children);
    }

    [Fact]
    public void BuildDependencyTree_GroupAndLeavesShareKindIconAndResourceKey()
    {
        var deps = new List<DependencyInfo>
        {
            new() { ObjectName = "V_USERS", ObjectType = "View" },
            new() { ObjectName = "V_ROLES", ObjectType = "View" },
        };

        var tree = TableDetailTabViewModel.BuildDependencyTree(deps);
        var viewGroup = tree.Single(g => g.ObjectType == UiStrings.MetadataGroupViews);

        Assert.Equal(MetadataNodeViewModel.IconFor(MetadataObjectKind.View), viewGroup.Icon);
        Assert.Equal(MetadataNodeViewModel.ResourceKeyFor(MetadataObjectKind.View), viewGroup.IconResourceKey);
        Assert.All(viewGroup.Children, leaf =>
        {
            Assert.Equal(MetadataNodeViewModel.IconFor(MetadataObjectKind.View), leaf.Icon);
            Assert.Equal(MetadataNodeViewModel.ResourceKeyFor(MetadataObjectKind.View), leaf.IconResourceKey);
        });
    }

    [Fact]
    public void BuildDependencyTree_UdfCategory_HasNoIconAndStaysEmpty()
    {
        var tree = TableDetailTabViewModel.BuildDependencyTree(System.Array.Empty<DependencyInfo>());
        var udf = tree.Single(g => g.ObjectType == UiStrings.DependencyCategoryUdfs);

        Assert.Empty(udf.Children);
        Assert.Equal(string.Empty, udf.Icon);
        Assert.Equal(string.Empty, udf.IconResourceKey);
    }

    [Fact]
    public void BuildDependencyTree_UnknownObjectTypesAreDropped()
    {
        // "Field" / "Object (42)" don't match any canonical category and aren't
        // surfaced as their own group — the tree stays at exactly the 11 IBExpert
        // categories, all empty.
        var deps = new List<DependencyInfo>
        {
            new() { ObjectName = "X", ObjectType = "Object (42)" },
            new() { ObjectName = "Y", ObjectType = "Field" },
        };

        var tree = TableDetailTabViewModel.BuildDependencyTree(deps);

        Assert.Equal(11, tree.Count);
        Assert.All(tree, g => Assert.Empty(g.Children));
    }

    [Theory]
    [InlineData("Table",     MetadataObjectKind.Table)]
    [InlineData("View",      MetadataObjectKind.View)]
    [InlineData("Trigger",   MetadataObjectKind.Trigger)]
    [InlineData("Procedure", MetadataObjectKind.Procedure)]
    [InlineData("Exception", MetadataObjectKind.Exception)]
    [InlineData("Generator", MetadataObjectKind.Generator)]
    [InlineData("Function",  MetadataObjectKind.Function)]
    [InlineData("Package",   MetadataObjectKind.Package)]
    [InlineData("Index",     MetadataObjectKind.Index)]
    [InlineData("User",      MetadataObjectKind.User)]
    [InlineData("Domain",    MetadataObjectKind.Domain)]
    public void MapObjectTypeToKind_KnownTypes(string objectType, MetadataObjectKind expected)
    {
        Assert.Equal(expected, TableDetailTabViewModel.MapObjectTypeToKind(objectType));
    }

    [Theory]
    [InlineData("Field")]
    [InlineData("Object (3)")]
    [InlineData("Object (99)")]
    [InlineData("Unknown")]
    [InlineData("UDF")]
    [InlineData("")]
    [InlineData(null)]
    public void MapObjectTypeToKind_NonOpenableTypes_ReturnsNull(string? objectType)
    {
        Assert.Null(TableDetailTabViewModel.MapObjectTypeToKind(objectType));
    }

    [Fact]
    public void RequestOpen_OpenableKind_FiresEventWithMetadataObject()
    {
        var vm = new TableDetailTabViewModel("USERS");
        MetadataObject? received = null;
        vm.OpenObjectRequested += obj => received = obj;

        vm.RequestOpen(new DependencyInfo { ObjectName = "V_USERS", ObjectType = "View" });

        Assert.NotNull(received);
        Assert.Equal("V_USERS", received!.Name);
        Assert.Equal(MetadataObjectKind.View, received.Kind);
    }

    [Fact]
    public void RequestOpen_DomainKind_FiresEvent()
    {
        var vm = new TableDetailTabViewModel("USERS");
        MetadataObject? received = null;
        vm.OpenObjectRequested += obj => received = obj;

        vm.RequestOpen(new DependencyInfo { ObjectName = "T_NAME", ObjectType = "Domain" });

        Assert.NotNull(received);
        Assert.Equal(MetadataObjectKind.Domain, received!.Kind);
    }

    [Fact]
    public void RequestOpen_LeafOverload_ForwardsToDependency()
    {
        var vm = new TableDetailTabViewModel("USERS");
        MetadataObject? received = null;
        vm.OpenObjectRequested += obj => received = obj;

        var leaf = new DependencyLeafNode
        {
            Dependency = new DependencyInfo { ObjectName = "P_DO_X", ObjectType = "Procedure" },
        };
        vm.RequestOpen(leaf);

        Assert.NotNull(received);
        Assert.Equal("P_DO_X", received!.Name);
        Assert.Equal(MetadataObjectKind.Procedure, received.Kind);
    }

    [Fact]
    public void RequestOpen_NonOpenableKind_DoesNotFireEvent()
    {
        var vm = new TableDetailTabViewModel("USERS");
        var fired = false;
        vm.OpenObjectRequested += _ => fired = true;

        vm.RequestOpen(new DependencyInfo { ObjectName = "COL1", ObjectType = "Field" });
        vm.RequestOpen(new DependencyInfo { ObjectName = "X", ObjectType = "Object (42)" });

        Assert.False(fired);
    }

    [Fact]
    public void RequestOpen_EmptyName_DoesNotFireEvent()
    {
        var vm = new TableDetailTabViewModel("USERS");
        var fired = false;
        vm.OpenObjectRequested += _ => fired = true;

        vm.RequestOpen(new DependencyInfo { ObjectName = "", ObjectType = "Table" });

        Assert.False(fired);
    }
}
