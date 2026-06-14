using System;
using System.IO;
using System.Linq;
using EmberTern.Core.Connections;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using EmberTern.Core.Workspace;
using Xunit;

namespace EmberTern.Tests;

public class ApplicationSettingsStoreTests
{
    // Reversible, human-readable stand-in for DPAPI: "x" -> "ENC:x". Lets the at-rest
    // tests assert the whole-file protector was applied without depending on platform crypto.
    private static SecretProtector FakeProtector() =>
        new(s => "ENC:" + s, s => s.StartsWith("ENC:", StringComparison.Ordinal)
            ? s.Substring(4)
            : throw new FormatException("not an ENC: blob"));

    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_ReturnsNull_WhenNothingToLoad()
    {
        var dir = NewTempDir();
        try
        {
            Assert.Null(new ApplicationSettingsStore(dir).Load());
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
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath, string.Empty);
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
            // Identity protector → the stored bytes ARE the JSON; write garbage.
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath, "{ this is not valid JSON ::: ");
            Assert.Null(store.Load());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsNull_AndDoesNotOverwrite_WhenUndecryptable()
    {
        var dir = NewTempDir();
        try
        {
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings
            {
                Connections = { new ConnectionProfile { Name = "A" } },
            });
            var before = File.ReadAllText(new ApplicationSettingsStore(dir).FilePath);

            var throwing = new SecretProtector(s => s, _ => throw new InvalidOperationException("nope"));
            var store = new ApplicationSettingsStore(dir, throwing);
            Assert.Null(store.Load());

            // The unreadable file is left intact (may decrypt on the right machine).
            Assert.Equal(before, File.ReadAllText(store.FilePath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_WholeFile_IsEncrypted_NotRawJson()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ApplicationSettingsStore(dir, FakeProtector());
            store.Save(new ApplicationSettings { Connections = { new ConnectionProfile { Name = "A", Password = "p" } } });

            var onDisk = File.ReadAllText(store.FilePath);
            // Container header first, then the encrypted payload — the raw file is never plain JSON.
            Assert.StartsWith(SettingsFileContainer.Magic, onDisk);
            Assert.Contains("ENC:", onDisk);
            Assert.False(onDisk.TrimStart().StartsWith("{", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RoundTrips_WholeAggregate()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ApplicationSettingsStore(dir, FakeProtector());
            var settings = new ApplicationSettings
            {
                Connections =
                {
                    new ConnectionProfile { Name = "Prod", DatabasePath = "/db/p.fdb", Password = "secret", DataTransactionProfile = TransactionProfile.Snapshot, MetadataTransactionProfile = TransactionProfile.ReadWriteTableStability },
                },
                Folders =
                {
                    Folders = { new FolderEntry { Id = "f1", Name = "ERP", SortOrder = 0 } },
                    ConnectionFolderMap = { ["c1"] = "f1" },
                },
                Workspace =
                {
                    QueryPanelVisible = false,
                    LastActiveConnectionId = "c1",
                    Workspaces =
                    {
                        ["c1"] = new ConnectionWorkspace
                        {
                            Tabs = { new WorkspaceTab { Kind = WorkspaceTabKind.Query, SqlText = "select 1;" } },
                            SavedQueries = { new SavedQuery { Id = "q1", Name = "All", SqlText = "select * from T;" } },
                            ActiveSavedQueryId = "q1",
                        },
                    },
                },
                UserSettings =
                {
                    GridProfiles =
                    {
                        new GridProfile
                        {
                            GridId = "TableDetail.Fields",
                            ColumnWidths = { ["Name"] = 120.5, ["Type"] = 80 },
                            ColumnOrder = { "Name", "Type" },
                            AutoFitColumns = true,
                        },
                    },
                    Appearance = { ThemeVariant = "Dark", AccentColor = "#2D6BBF" },
                },
            };

            store.Save(settings);
            var r = store.Load();

            Assert.NotNull(r);
            Assert.Equal(ApplicationSettingsStore.CurrentSchemaVersion, r!.SchemaVersion);

            Assert.Single(r.Connections);
            Assert.Equal("Prod", r.Connections[0].Name);
            Assert.Equal("secret", r.Connections[0].Password);
            Assert.Equal(TransactionProfile.Snapshot, r.Connections[0].DataTransactionProfile);
            Assert.Equal(TransactionProfile.ReadWriteTableStability, r.Connections[0].MetadataTransactionProfile);

            Assert.Equal("ERP", r.Folders.Folders[0].Name);
            Assert.Equal("f1", r.Folders.ConnectionFolderMap["c1"]);

            Assert.False(r.Workspace.QueryPanelVisible);
            Assert.Equal("c1", r.Workspace.LastActiveConnectionId);
            Assert.Equal("select 1;", r.Workspace.Workspaces["c1"].Tabs[0].SqlText);
            Assert.Equal("All", r.Workspace.Workspaces["c1"].SavedQueries[0].Name);

            var gp = Assert.Single(r.UserSettings.GridProfiles);
            Assert.Equal("TableDetail.Fields", gp.GridId);
            Assert.Equal(120.5, gp.ColumnWidths["Name"]);
            Assert.Equal(new[] { "Name", "Type" }, gp.ColumnOrder);
            Assert.True(gp.AutoFitColumns);
            Assert.Equal("Dark", r.UserSettings.Appearance.ThemeVariant);
            Assert.Equal("#2D6BBF", r.UserSettings.Appearance.AccentColor);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migration_LegacySingleTransactionProfile_InSettingsDat_SplitsToDataLane()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            // A v1 settings.dat (Identity protector → readable JSON) whose connection
            // carries the pre-split single "TransactionProfile" field.
            var store = new ApplicationSettingsStore(dir);
            File.WriteAllText(store.FilePath,
                "{\"SchemaVersion\":1,\"Connections\":[{\"Name\":\"Old\",\"DatabasePath\":\"/db/x.fdb\",\"TransactionProfile\":\"ReadWriteTableStability\"}]}");

            var r = store.Load();

            Assert.NotNull(r);
            Assert.Single(r!.Connections);
            // Variant A: old value → Data, Metadata stays the safe ReadCommitted.
            Assert.Equal(TransactionProfile.ReadWriteTableStability, r.Connections[0].DataTransactionProfile);
            Assert.Equal(TransactionProfile.ReadCommitted, r.Connections[0].MetadataTransactionProfile);
            // The legacy shim is cleared so it is never re-written.
            Assert.Null(r.Connections[0].LegacyTransactionProfile);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migration_LegacyV0Array_Connections()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "connections.json"),
                "[{\"Name\":\"Legacy\",\"DatabasePath\":\"/db/x.fdb\",\"Password\":\"plain\"}]");

            var store = new ApplicationSettingsStore(dir, FakeProtector());
            var r = store.Load();

            Assert.NotNull(r);
            Assert.Equal("Legacy", r!.Connections[0].Name);
            Assert.Equal("plain", r.Connections[0].Password);
            Assert.False(File.Exists(Path.Combine(dir, "connections.json")));
            var onDisk = File.ReadAllText(store.FilePath);
            Assert.StartsWith(SettingsFileContainer.Magic, onDisk);
            Assert.Contains("ENC:", onDisk);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migration_LegacyV1Container_DecryptsProtectedPassword()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            // v1 connections.json: container with ProtectedPassword (FakeProtector form).
            File.WriteAllText(Path.Combine(dir, "connections.json"),
                "{\"SchemaVersion\":1,\"Connections\":[{\"Name\":\"V1\",\"DatabasePath\":\"/db/x.fdb\",\"ProtectedPassword\":\"ENC:hunter2\"}]}");

            var store = new ApplicationSettingsStore(dir, FakeProtector());
            var r = store.Load();

            Assert.NotNull(r);
            Assert.Equal("V1", r!.Connections[0].Name);
            Assert.Equal("hunter2", r.Connections[0].Password);
            Assert.False(File.Exists(Path.Combine(dir, "connections.json")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migration_AllThreeLegacyFiles_AreMergedAndDeleted()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "connections.json"),
                "[{\"Id\":\"c1\",\"Name\":\"Legacy\",\"DatabasePath\":\"/db/x.fdb\",\"Password\":\"plain\"}]");
            File.WriteAllText(Path.Combine(dir, "folders.json"),
                "{\"Folders\":[{\"Id\":\"f1\",\"Name\":\"ERP\",\"SortOrder\":0}],\"ConnectionFolderMap\":{\"c1\":\"f1\"}}");
            File.WriteAllText(Path.Combine(dir, "workspace.json"),
                "{\"QueryPanelVisible\":false,\"Workspaces\":{\"c1\":{\"Tabs\":[{\"Kind\":\"Query\",\"SqlText\":\"select 1;\"}]}}}");

            var store = new ApplicationSettingsStore(dir);
            var r = store.Load();

            Assert.NotNull(r);
            Assert.Equal("Legacy", r!.Connections[0].Name);
            Assert.Equal("ERP", r.Folders.Folders[0].Name);
            Assert.Equal("f1", r.Folders.ConnectionFolderMap["c1"]);
            Assert.False(r.Workspace.QueryPanelVisible);
            Assert.Equal("select 1;", r.Workspace.Workspaces["c1"].Tabs[0].SqlText);

            Assert.False(File.Exists(Path.Combine(dir, "connections.json")));
            Assert.False(File.Exists(Path.Combine(dir, "folders.json")));
            Assert.False(File.Exists(Path.Combine(dir, "workspace.json")));
            Assert.True(File.Exists(store.FilePath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migration_IsIdempotent_AcrossStoreInstances()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "connections.json"),
                "[{\"Name\":\"Legacy\",\"DatabasePath\":\"/db/x.fdb\",\"Password\":\"plain\"}]");

            // First instance migrates + deletes the legacy file.
            new ApplicationSettingsStore(dir, FakeProtector()).Load();
            // A fresh instance reads the unified file (no legacy file to re-migrate).
            var r = new ApplicationSettingsStore(dir, FakeProtector()).Load();

            Assert.NotNull(r);
            Assert.Single(r!.Connections);
            Assert.Equal("plain", r.Connections[0].Password);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
