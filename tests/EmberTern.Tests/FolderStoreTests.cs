using System;
using System.IO;
using EmberTern.Core.Connections;
using Xunit;

namespace EmberTern.Tests;

public class FolderStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FolderStore _store;

    public FolderStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "embertern-folderstore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new FolderStore(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyState()
    {
        var state = _store.Load();

        Assert.NotNull(state);
        Assert.Empty(state.Folders);
        Assert.Empty(state.ConnectionFolderMap);
        Assert.Empty(state.ConnectionSortOrders);
    }

    [Fact]
    public void Load_EmptyFile_ReturnsEmptyState()
    {
        File.WriteAllText(_store.FilePath, "");

        var state = _store.Load();

        Assert.Empty(state.Folders);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsEmptyState()
    {
        File.WriteAllText(_store.FilePath, "{ not valid json");

        var state = _store.Load();

        Assert.Empty(state.Folders);
        Assert.Empty(state.ConnectionFolderMap);
    }

    [Fact]
    public void RoundTrip_PreservesFoldersAndConnectionMap()
    {
        var state = new FolderState
        {
            Folders =
            {
                new FolderEntry { Id = "f1", Name = "Production", SortOrder = 0 },
                new FolderEntry { Id = "f2", Name = "Development", SortOrder = 1 },
            },
            ConnectionFolderMap =
            {
                ["c-alpha"] = "f1",
                ["c-beta"] = "f2",
                ["c-gamma"] = "f1",
            },
            ConnectionSortOrders =
            {
                ["c-alpha"] = 0,
                ["c-beta"] = 0,
                ["c-gamma"] = 1,
                ["c-orphan"] = 3,
            },
        };

        _store.Save(state);
        var roundTripped = _store.Load();

        Assert.Equal(2, roundTripped.Folders.Count);
        Assert.Equal("Production", roundTripped.Folders[0].Name);
        Assert.Equal(0, roundTripped.Folders[0].SortOrder);
        Assert.Equal("Development", roundTripped.Folders[1].Name);
        Assert.Equal(1, roundTripped.Folders[1].SortOrder);

        Assert.Equal("f1", roundTripped.ConnectionFolderMap["c-alpha"]);
        Assert.Equal("f2", roundTripped.ConnectionFolderMap["c-beta"]);
        Assert.Equal("f1", roundTripped.ConnectionFolderMap["c-gamma"]);

        Assert.Equal(3, roundTripped.ConnectionSortOrders["c-orphan"]);
        Assert.Equal(1, roundTripped.ConnectionSortOrders["c-gamma"]);
    }

    [Fact]
    public void FolderEntry_DefaultsGenerateUniqueId()
    {
        var a = new FolderEntry();
        var b = new FolderEntry();

        Assert.NotEqual(a.Id, b.Id);
        Assert.False(string.IsNullOrEmpty(a.Id));
    }
}
