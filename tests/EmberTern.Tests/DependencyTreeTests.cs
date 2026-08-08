using System;
using System.IO;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// ⭐⭐ <b>PRZEMIANOWANY W M4.2b, i to nie kosmetyka.</b> Test nazywał się
    /// <c>…InIbExpertOrder</c> i przepisywał jedenaście nazw z tablicy, którą pilnował — czyli
    /// potwierdzał, że kod jest taki, jaki jest. Po decyzji użytkownika kolejność kategorii jest
    /// WSPÓLNA z drzewem połączenia, więc nazwa zaczęłaby kłamać, a asercja przez przepisanie i tak
    /// nie umiałaby złapać defektu, który się zdarzył: <b>dwie osobne tablice, które się rozjechały</b>
    /// (#315 — strażnik zielony albo czerwony z powodu, którego jego nazwa nie opisuje).
    /// </summary>
    /// <remarks>
    /// ⚠ Dlatego asercja jest RELACYJNA: wspólne kategorie muszą stać w tej samej KOLEJNOŚCI WZGLĘDNEJ
    /// co w drzewie połączenia. Taki test przeżywa zmianę kolejności kanonicznej (jest wtedy nadal
    /// prawdziwy) i pada dokładnie wtedy, gdy któreś z drzew zacznie mieć własną listę.
    /// </remarks>
    [Fact]
    public void BuildDependencyTree_OrdersSharedCategories_LikeTheConnectionTree()
    {
        var categories = TableDetailTabViewModel.CategoryOrder;
        var canonical = MetadataCategoryOrder.All.ToList();

        // ⚠ Porównanie idzie po `Kind`, a nie po `ObjectType`: ten drugi niesie ETYKIETĘ WYŚWIETLANĄ
        //   („Tables"), więc zestawianie go z nazwą enuma odpowiadałoby na inne pytanie (#285).
        //   Po usunięciu „UDF" każda kategoria ma `Kind` — sprawdzane niżej.
        var positions = categories
            .Where(c => c.Kind is not null)
            .Select(c => canonical.IndexOf(c.Kind!.Value))
            .ToList();

        Assert.NotEmpty(positions);
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.OrderBy(p => p), positions);

        // ⛔ Po usunięciu „UDF" (decyzja użytkownika) KAŻDA kategoria ma swój `Kind`, więc lista jest
        // wyłącznie zawężeniem kolejności kanonicznej — zero pozycji wstawianych lokalnie.
        Assert.All(categories, c => Assert.NotNull(c.Kind));

        // Każda kategoria pojawia się nawet pusta — to jest niezależne od kolejności i nadal obowiązuje.
        var tree = TableDetailTabViewModel.BuildDependencyTree(System.Array.Empty<DependencyInfo>());
        Assert.All(tree, g => Assert.Empty(g.Children));
    }

    /// <summary>
    /// ⭐ Właściwa anty-regresja dla defektu, który zgłosił użytkownik: <b>dwa drzewa nie mogą mieć dwóch
    /// list</b>. Test czyta ŹRÓDŁO, bo pytanie brzmi „czy istnieje druga tablica", a nie „czy dziś dają
    /// ten sam wynik" — dwie listy, które dziś się zgadzają, przechodzą każdą asercję o wyniku.
    /// </summary>
    [Fact]
    public void NeitherTree_DeclaresItsOwnCategoryOrder()
    {
        var appRoot = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "ViewModels");

        var offenders = Directory
            .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "MetadataCategoryOrder.cs")
            .Where(f => Regex.IsMatch(
                File.ReadAllText(f),
                @"CategoryOrder\s*=\s*new\[\]|MetadataObjectKind\[\]\s*CategoryOrder"))
            .Select(f => Path.GetFileName(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Drzewo deklaruje własną tablicę kolejności kategorii:\n  " + string.Join("\n  ", offenders)
            + "\n\n⭐ Kanoniczna kolejność jest JEDNA (`MetadataCategoryOrder.All`); drzewo deklaruje CO "
            + "pokazuje, nigdy W JAKIEJ KOLEJNOŚCI.\n⚠ Dokładnie ten defekt zgłosił użytkownik przy odbiorze "
            + "M4.2b: Trigger, Function, Generator, Domain i Package stały w innym miejscu w każdym z drzew, "
            + "wyłącznie dlatego, że każdy mechanizm miał własną listę.");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
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

        // 10 kategorii zawsze — niepasujące wracają z pustymi dziećmi.
        Assert.Equal(10, tree.Count);

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

        Assert.Equal(10, tree.Count);
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
