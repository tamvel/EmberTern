using System.IO;
using EmberTern.Core.Connections;
using EmberTern.Core.Settings;
using EmberTern.Core.Workspace;
using Xunit;

namespace EmberTern.Tests;

public class GridProfileStoreTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));

    [Fact]
    public void Get_ReturnsNull_WhenNoProfileSaved()
    {
        var dir = NewTempDir();
        try
        {
            var store = new GridProfileStore(dir);
            Assert.Null(store.Get("TableDetail.Fields"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_ThenGet_RoundTripsAcrossInstances()
    {
        var dir = NewTempDir();
        try
        {
            var profile = new GridProfile
            {
                GridId = "TableDetail.Fields",
                AutoFitColumns = false,
                ColumnOrder = { "Name", "Type", "Not Null" },
                ColumnWidths = { ["Name"] = 120.0, ["Type"] = 90.5 },
            };
            new GridProfileStore(dir).Save(profile);

            // Fresh instance forces a real reload from settings.dat.
            var loaded = new GridProfileStore(dir).Get("TableDetail.Fields");
            Assert.NotNull(loaded);
            Assert.False(loaded!.AutoFitColumns);
            Assert.Equal(new[] { "Name", "Type", "Not Null" }, loaded.ColumnOrder);
            Assert.Equal(120.0, loaded.ColumnWidths["Name"]);
            Assert.Equal(90.5, loaded.ColumnWidths["Type"]);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_UpsertsByGridId()
    {
        var dir = NewTempDir();
        try
        {
            var store = new GridProfileStore(dir);
            store.Save(new GridProfile { GridId = "QueryResults", AutoFitColumns = true });
            store.Save(new GridProfile { GridId = "QueryResults", AutoFitColumns = false });

            var loaded = new GridProfileStore(dir).Get("QueryResults");
            Assert.NotNull(loaded);
            Assert.False(loaded!.AutoFitColumns);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_KeepsMultipleProfilesIndependent()
    {
        var dir = NewTempDir();
        try
        {
            var store = new GridProfileStore(dir);
            store.Save(new GridProfile { GridId = "A", ColumnOrder = { "x" } });
            store.Save(new GridProfile { GridId = "B", ColumnOrder = { "y" } });

            var fresh = new GridProfileStore(dir);
            Assert.Equal(new[] { "x" }, fresh.Get("A")!.ColumnOrder);
            Assert.Equal(new[] { "y" }, fresh.Get("B")!.ColumnOrder);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_PreservesOtherSections()
    {
        var dir = NewTempDir();
        try
        {
            // Seed a connection + workspace via their own facades over the same file.
            var connections = new ConnectionProfileStore(dir);
            connections.Upsert(new ConnectionProfile { Name = "Prod", Host = "db1" });
            new WorkspaceStore(dir).Save(new WorkspaceState { QueryPanelVisible = false });

            // Now save a grid profile — must not clobber connections/workspace.
            new GridProfileStore(dir).Save(new GridProfile { GridId = "QueryResults", AutoFitColumns = false });

            Assert.Single(connections.LoadAll());
            Assert.Equal("Prod", connections.LoadAll()[0].Name);
            Assert.False(new WorkspaceStore(dir).Load()!.QueryPanelVisible);
            Assert.False(new GridProfileStore(dir).Get("QueryResults")!.AutoFitColumns);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
