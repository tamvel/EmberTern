using System.Collections.Generic;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

// Lookup logic that drives the SQL-editor double-click → open-DDL behaviour.
// UI is tested manually; here we cover the pure resolution: given a list of
// loaded metadata objects and a word, find the matching object.
public class OpenDdlOnDoubleClickTests
{
    [Fact]
    public void ResolveByName_EmptyName_ReturnsNull()
    {
        var objs = new[] { new MetadataObject("FOO", MetadataObjectKind.Table) };
        Assert.Null(MainWindowViewModel.ResolveByName(objs, null));
        Assert.Null(MainWindowViewModel.ResolveByName(objs, string.Empty));
    }

    [Fact]
    public void ResolveByName_NoMatch_ReturnsNull()
    {
        var objs = new[]
        {
            new MetadataObject("FOO", MetadataObjectKind.Table),
            new MetadataObject("BAR", MetadataObjectKind.View),
        };
        Assert.Null(MainWindowViewModel.ResolveByName(objs, "BAZ"));
    }

    [Fact]
    public void ResolveByName_ExactMatch_Returns()
    {
        var foo = new MetadataObject("FOO", MetadataObjectKind.Table);
        var bar = new MetadataObject("BAR", MetadataObjectKind.View);
        var hit = MainWindowViewModel.ResolveByName(new[] { foo, bar }, "BAR");
        Assert.Same(bar, hit);
    }

    [Fact]
    public void ResolveByName_CaseInsensitive()
    {
        // Firebird names are SHOUTY_SNAKE_CASE in the catalog, but the user might
        // double-click "customers" in lowercase or "Customers" mixed.
        var obj = new MetadataObject("CUSTOMERS", MetadataObjectKind.Table);
        Assert.Same(obj, MainWindowViewModel.ResolveByName(new[] { obj }, "customers"));
        Assert.Same(obj, MainWindowViewModel.ResolveByName(new[] { obj }, "Customers"));
        Assert.Same(obj, MainWindowViewModel.ResolveByName(new[] { obj }, "CUSTOMERS"));
    }

    [Fact]
    public void ResolveByName_FirstWinsOnDuplicate()
    {
        // Firebird allows a trigger named after a table — when both share a name,
        // the lookup picks the first one in the input. EnumerateLoadedObjects
        // walks categories in CategoryOrder (Tables first), so for the UI a table
        // wins over a trigger sharing its name. Pin that with explicit ordering.
        var table = new MetadataObject("ACCT", MetadataObjectKind.Table);
        var trigger = new MetadataObject("ACCT", MetadataObjectKind.Trigger);
        var hit = MainWindowViewModel.ResolveByName(new[] { table, trigger }, "ACCT");
        Assert.Same(table, hit);
        Assert.Equal(MetadataObjectKind.Table, hit!.Kind);
    }

    [Fact]
    public void ResolveByName_EmptyInputList_ReturnsNull()
    {
        Assert.Null(MainWindowViewModel.ResolveByName(new List<MetadataObject>(), "ANYTHING"));
    }

    [Fact]
    public void ResolveByName_MatchAcrossKinds()
    {
        // No table named like a procedure — make sure the lookup still finds the
        // procedure when nothing earlier matched.
        var objs = new[]
        {
            new MetadataObject("TBL1", MetadataObjectKind.Table),
            new MetadataObject("MY_PROC", MetadataObjectKind.Procedure),
            new MetadataObject("GEN_ID", MetadataObjectKind.Generator),
        };
        var hit = MainWindowViewModel.ResolveByName(objs, "MY_PROC");
        Assert.NotNull(hit);
        Assert.Equal(MetadataObjectKind.Procedure, hit!.Kind);
    }
}
