using System.IO;
using EmberTern.Core.Metadata;
using EmberTern.Core.Workspace;
using Xunit;

namespace EmberTern.Tests;

public class WorkspaceStoreTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + System.Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsNull_WhenFileDoesNotExist()
    {
        var dir = NewTempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsNull_WhenFileIsCorrupt()
    {
        var dir = NewTempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            File.WriteAllText(store.FilePath, "{ this is not valid JSON ::: ");
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsNull_WhenFileIsEmpty()
    {
        var dir = NewTempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            File.WriteAllText(store.FilePath, string.Empty);
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_Then_Load_RoundtripsAllFields()
    {
        var dir = NewTempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            var state = new WorkspaceState
            {
                WindowBounds = new WindowBounds
                {
                    X = 100, Y = 200, Width = 1400, Height = 900, WindowState = "Maximized",
                },
                LastActiveConnectionId = "abc123",
                Workspaces =
                {
                    ["abc123"] = new ConnectionWorkspace
                    {
                        ActiveTabIndex = 1,
                        Tabs =
                        {
                            new WorkspaceTab { Kind = WorkspaceTabKind.Query, SqlText = "SELECT 1 FROM RDB$DATABASE;" },
                            new WorkspaceTab
                            {
                                Kind = WorkspaceTabKind.Ddl,
                                ObjectName = "MY_TABLE",
                                ObjectKind = MetadataObjectKind.Table,
                                ConnectionProfileId = "abc123",
                                DdlText = "CREATE TABLE MY_TABLE (...);",
                            },
                        },
                    },
                    ["def456"] = new ConnectionWorkspace
                    {
                        ActiveTabIndex = 0,
                        Tabs =
                        {
                            new WorkspaceTab { Kind = WorkspaceTabKind.Query, SqlText = "-- second profile\nselect * from t;" },
                        },
                    },
                },
            };

            store.Save(state);
            var reloaded = store.Load();

            Assert.NotNull(reloaded);
            Assert.Equal(100, reloaded!.WindowBounds!.X);
            Assert.Equal("Maximized", reloaded.WindowBounds.WindowState);
            Assert.Equal("abc123", reloaded.LastActiveConnectionId);
            Assert.Equal(2, reloaded.Workspaces.Count);

            var ws1 = reloaded.Workspaces["abc123"];
            Assert.Equal(1, ws1.ActiveTabIndex);
            Assert.Equal(2, ws1.Tabs.Count);
            Assert.Equal(WorkspaceTabKind.Query, ws1.Tabs[0].Kind);
            Assert.Equal("SELECT 1 FROM RDB$DATABASE;", ws1.Tabs[0].SqlText);
            Assert.Equal("MY_TABLE", ws1.Tabs[1].ObjectName);
            Assert.Equal(MetadataObjectKind.Table, ws1.Tabs[1].ObjectKind);
            Assert.Equal("CREATE TABLE MY_TABLE (...);", ws1.Tabs[1].DdlText);

            var ws2 = reloaded.Workspaces["def456"];
            Assert.Single(ws2.Tabs);
            Assert.Equal("-- second profile\nselect * from t;", ws2.Tabs[0].SqlText);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_Then_Load_RoundtripsSavedQueriesAndPanelVisibility()
    {
        var dir = NewTempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            var state = new WorkspaceState
            {
                QueryPanelVisible = false,
                Workspaces =
                {
                    ["pid"] = new ConnectionWorkspace
                    {
                        Tabs = { new WorkspaceTab { Kind = WorkspaceTabKind.Query, SqlText = "select 1;" } },
                        SavedQueries =
                        {
                            new SavedQuery { Id = "q1", Name = "Customers", SqlText = "select * from CUSTOMERS;" },
                            new SavedQuery { Id = "q2", Name = "Orders", SqlText = "select * from ORDERS;" },
                        },
                        ActiveSavedQueryId = "q2",
                    },
                },
            };

            store.Save(state);
            var reloaded = store.Load();

            Assert.NotNull(reloaded);
            Assert.False(reloaded!.QueryPanelVisible);
            var ws = reloaded.Workspaces["pid"];
            Assert.Equal(2, ws.SavedQueries.Count);
            Assert.Equal("q1", ws.SavedQueries[0].Id);
            Assert.Equal("Customers", ws.SavedQueries[0].Name);
            Assert.Equal("select * from CUSTOMERS;", ws.SavedQueries[0].SqlText);
            Assert.Equal("q2", ws.ActiveSavedQueryId);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_DefaultsForLegacyStateWithoutSavedQueries()
    {
        // A workspace.json written before this milestone has no SavedQueries / ActiveSavedQueryId
        // / QueryPanelVisible fields. System.Text.Json should fill the defaults (empty list, null,
        // true) without throwing.
        var dir = NewTempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            File.WriteAllText(store.FilePath, """
                {
                  "Workspaces": {
                    "pid": {
                      "Tabs": [ { "Kind": "Query", "SqlText": "select 1;" } ],
                      "ActiveTabIndex": 0
                    }
                  }
                }
                """);

            var state = store.Load();
            Assert.NotNull(state);
            Assert.True(state!.QueryPanelVisible);
            var ws = state.Workspaces["pid"];
            Assert.Empty(ws.SavedQueries);
            Assert.Null(ws.ActiveSavedQueryId);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_WritesEnumsAsStrings_ForwardCompatible()
    {
        // Persisting enums as strings means schema changes (renaming/adding kinds)
        // don't silently break old workspace files. Catch regressions in JsonOptions.
        var dir = NewTempDir();
        try
        {
            var store = new WorkspaceStore(dir);
            store.Save(new WorkspaceState
            {
                Workspaces =
                {
                    ["pid"] = new ConnectionWorkspace
                    {
                        Tabs = { new WorkspaceTab { Kind = WorkspaceTabKind.Ddl, ObjectKind = MetadataObjectKind.Trigger } },
                    },
                },
            });

            var json = File.ReadAllText(store.FilePath);
            Assert.Contains("\"Ddl\"", json);
            Assert.Contains("\"Trigger\"", json);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
