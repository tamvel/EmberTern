using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class FolderVmTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConnectionProfileStore _store;
    private readonly FolderStore _folderStore;
    private readonly FirebirdConnectionService _service;
    private readonly TransactionService _tx;
    private readonly MainWindowViewModel _main;

    public FolderVmTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "embertern-foldervm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new ConnectionProfileStore(_tempDir);
        _folderStore = new FolderStore(_tempDir);
        _service = new FirebirdConnectionService();
        _tx = new TransactionService(_service);
        _main = new MainWindowViewModel(_store, _service, _tx, _folderStore);
        // Auto-confirm delete dialogs so tests don't need to wire UI.
        _main.ConfirmationRequested += _ => Task.FromResult(true);
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private ConnectionProfile MakeProfile(string name)
    {
        var p = new ConnectionProfile { Name = name, Host = "h", Port = 1, DatabasePath = "db" };
        _store.Upsert(p);
        return p;
    }

    [Fact]
    public void CreateFolder_AddsFolderAndPersists()
    {
        var folder = _main.CreateFolder("Production");

        Assert.Single(_main.FolderState.Folders);
        Assert.Equal("Production", folder.Name);
        // Persisted to disk.
        Assert.Equal("Production", new FolderStore(_tempDir).Load().Folders[0].Name);
        // Appears in RootNodes.
        Assert.Contains(_main.Metadata.RootNodes, n => n is FolderNodeViewModel f && f.Id == folder.Id);
    }

    [Fact]
    public void CreateFolder_BlankName_FallsBackToDefault()
    {
        _main.CreateFolder("   ");

        Assert.Single(_main.FolderState.Folders);
        Assert.False(string.IsNullOrWhiteSpace(_main.FolderState.Folders[0].Name));
    }

    [Fact]
    public void Reload_PutsMappedConnectionsIntoFolders_AndOthersAtRoot()
    {
        var p1 = MakeProfile("Alpha");
        var p2 = MakeProfile("Beta");
        var p3 = MakeProfile("Gamma");

        var folder = _main.CreateFolder("Production");
        _folderStore.Save(new FolderState
        {
            Folders = { folder },
            ConnectionFolderMap = { [p1.Id] = folder.Id, [p3.Id] = folder.Id },
        });
        _main.FolderState.Folders.Clear();
        _main.FolderState.Folders.Add(folder);
        _main.FolderState.ConnectionFolderMap[p1.Id] = folder.Id;
        _main.FolderState.ConnectionFolderMap[p3.Id] = folder.Id;
        _main.ReloadConnections();

        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        Assert.Equal(2, folderVm.Connections.Count);
        Assert.Contains(folderVm.Connections, c => c.Profile.Id == p1.Id);
        Assert.Contains(folderVm.Connections, c => c.Profile.Id == p3.Id);

        // p2 sits at root, alongside the folder.
        var rootConnections = _main.Metadata.RootNodes.OfType<ConnectionNodeViewModel>().ToList();
        Assert.Single(rootConnections);
        Assert.Equal(p2.Id, rootConnections[0].Profile.Id);
    }

    [Fact]
    public async Task DeleteFolder_MovesChildrenBackToRoot()
    {
        var p1 = MakeProfile("Alpha");
        var folder = _main.CreateFolder("Production");
        _main.FolderState.ConnectionFolderMap[p1.Id] = folder.Id;
        _main.ReloadConnections();

        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        await _main.DeleteFolderAsync(folderVm);

        Assert.Empty(_main.FolderState.Folders);
        Assert.False(_main.FolderState.ConnectionFolderMap.ContainsKey(p1.Id));
        Assert.Contains(_main.Metadata.RootNodes,
            n => n is ConnectionNodeViewModel c && c.Profile.Id == p1.Id);
    }

    [Fact]
    public void RenameFolder_PersistsNewName()
    {
        var folder = _main.CreateFolder("Old");
        var vm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();

        vm.BeginRenameCommand.Execute(null);
        vm.EditingName = "New";
        vm.CommitRenameCommand.Execute(null);

        Assert.Equal("New", vm.Name);
        // Persisted to disk.
        Assert.Equal("New", new FolderStore(_tempDir).Load().Folders[0].Name);
    }

    [Fact]
    public void RenameFolder_BlankInput_KeepsName()
    {
        var folder = _main.CreateFolder("Keep");
        var vm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();

        vm.BeginRenameCommand.Execute(null);
        vm.EditingName = "   ";
        vm.CommitRenameCommand.Execute(null);

        Assert.Equal("Keep", vm.Name);
    }

    [Fact]
    public void SortAscending_RootMixesFoldersAndConnectionsByName()
    {
        MakeProfile("Zeta");
        MakeProfile("Alpha");
        _main.CreateFolder("Mango");
        _main.CreateFolder("Banana");
        _main.ReloadConnections();

        // Pivot on any root-level connection.
        var pivot = _main.Metadata.Connections.First(c => c.Profile.Name == "Alpha");
        _main.SortSiblingsOf(pivot, ascending: true);

        var rootNames = _main.Metadata.RootNodes.Select(NameOf).ToList();
        Assert.Equal(new[] { "Alpha", "Banana", "Mango", "Zeta" }, rootNames);
    }

    [Fact]
    public void SortDescending_RootMixesFoldersAndConnectionsByName()
    {
        MakeProfile("Zeta");
        MakeProfile("Alpha");
        _main.CreateFolder("Mango");
        _main.CreateFolder("Banana");
        _main.ReloadConnections();

        var pivot = _main.Metadata.Connections.First(c => c.Profile.Name == "Alpha");
        _main.SortSiblingsOf(pivot, ascending: false);

        var rootNames = _main.Metadata.RootNodes.Select(NameOf).ToList();
        Assert.Equal(new[] { "Zeta", "Mango", "Banana", "Alpha" }, rootNames);
    }

    [Fact]
    public void SortAscending_InsideFolder_OnlyAffectsThatFolder()
    {
        var p1 = MakeProfile("Charlie");
        var p2 = MakeProfile("Alpha");
        var p3 = MakeProfile("Bravo");
        var folder = _main.CreateFolder("Production");
        _main.FolderState.ConnectionFolderMap[p1.Id] = folder.Id;
        _main.FolderState.ConnectionFolderMap[p2.Id] = folder.Id;
        _main.FolderState.ConnectionFolderMap[p3.Id] = folder.Id;
        _main.ReloadConnections();

        var pivot = _main.Metadata.Connections.First(c => c.Profile.Name == "Charlie");
        _main.SortSiblingsOf(pivot, ascending: true);

        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        var inFolder = folderVm.Connections.Select(c => c.Profile.Name).ToList();
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, inFolder);
    }

    [Fact]
    public void SortPersists_AndSurvivesReload()
    {
        MakeProfile("Zeta");
        MakeProfile("Alpha");
        _main.ReloadConnections();

        var pivot = _main.Metadata.Connections.First(c => c.Profile.Name == "Alpha");
        _main.SortSiblingsOf(pivot, ascending: true);

        // Fresh VM from disk should observe the same order.
        var freshFolderStore = new FolderStore(_tempDir);
        var freshService = new FirebirdConnectionService();
        try
        {
            var fresh = new MainWindowViewModel(_store, freshService, new TransactionService(freshService), freshFolderStore);
            var rootNames = fresh.Metadata.RootNodes.OfType<ConnectionNodeViewModel>()
                .Select(c => c.Profile.Name).ToList();
            Assert.Equal(new[] { "Alpha", "Zeta" }, rootNames);
        }
        finally
        {
            freshService.Dispose();
        }
    }

    [Fact]
    public void PlaceConnectionInFolder_MapsAndReloadsTree()
    {
        var p1 = MakeProfile("Alpha");
        var folder = _main.CreateFolder("Production");

        _main.PlaceConnectionInFolder(p1.Id, folder.Id);

        Assert.Equal(folder.Id, _main.FolderState.ConnectionFolderMap[p1.Id]);
        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        Assert.Contains(folderVm.Connections, c => c.Profile.Id == p1.Id);
        // Persisted to disk.
        Assert.Equal(folder.Id, new FolderStore(_tempDir).Load().ConnectionFolderMap[p1.Id]);
    }

    [Fact]
    public void PlaceConnectionInFolder_NullFolder_MovesToRoot()
    {
        var p1 = MakeProfile("Alpha");
        var folder = _main.CreateFolder("Production");
        _main.PlaceConnectionInFolder(p1.Id, folder.Id);
        Assert.True(_main.FolderState.ConnectionFolderMap.ContainsKey(p1.Id));

        _main.PlaceConnectionInFolder(p1.Id, null);

        Assert.False(_main.FolderState.ConnectionFolderMap.ContainsKey(p1.Id));
        Assert.Contains(_main.Metadata.RootNodes,
            n => n is ConnectionNodeViewModel c && c.Profile.Id == p1.Id);
    }

    [Fact]
    public void SortInsideFolder_LeavesRootOrderUntouched()
    {
        // Root: two connections (will tiebreak to alphabetical because no explicit
        // SortOrders are set). Folder: two members in arbitrary order.
        MakeProfile("Zeta");
        MakeProfile("Alpha");
        var p1 = MakeProfile("Charlie");
        var p2 = MakeProfile("Bravo");
        var folder = _main.CreateFolder("Production");
        _main.FolderState.ConnectionFolderMap[p1.Id] = folder.Id;
        _main.FolderState.ConnectionFolderMap[p2.Id] = folder.Id;
        _main.ReloadConnections();

        var rootBefore = _main.Metadata.RootNodes
            .OfType<ConnectionNodeViewModel>()
            .Select(c => c.Profile.Name)
            .ToList();

        // Sort INSIDE the folder.
        var pivot = _main.Metadata.Connections.First(c => c.Profile.Name == "Charlie");
        _main.SortSiblingsOf(pivot, ascending: true);

        // Folder is sorted...
        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        Assert.Equal(new[] { "Bravo", "Charlie" },
            folderVm.Connections.Select(c => c.Profile.Name).ToArray());

        // ...root order unchanged...
        var rootAfter = _main.Metadata.RootNodes
            .OfType<ConnectionNodeViewModel>()
            .Select(c => c.Profile.Name)
            .ToList();
        Assert.Equal(rootBefore, rootAfter);

        // ...and the folder-sort path didn't touch root-connection sort orders or
        // FolderEntry.SortOrder. Only folder members' sort orders should have entries.
        foreach (var name in new[] { "Zeta", "Alpha" })
        {
            var id = _main.Metadata.Connections.First(c => c.Profile.Name == name).Profile.Id;
            Assert.False(_main.FolderState.ConnectionSortOrders.ContainsKey(id),
                $"Root connection {name} sort order should not have been touched by a folder sort.");
        }
        Assert.Equal(0, folder.SortOrder);
    }

    [Fact]
    public void SortAtRoot_LeavesFolderMembersUntouched()
    {
        // Folder with 3 members in input order (Charlie, Alpha, Bravo).
        var p1 = MakeProfile("Charlie");
        var p2 = MakeProfile("Alpha");
        var p3 = MakeProfile("Bravo");
        var folder = _main.CreateFolder("Production");
        _main.FolderState.ConnectionFolderMap[p1.Id] = folder.Id;
        _main.FolderState.ConnectionFolderMap[p2.Id] = folder.Id;
        _main.FolderState.ConnectionFolderMap[p3.Id] = folder.Id;
        // Plus a root connection so we have a sortable pivot.
        var pRoot = MakeProfile("Zeta");
        _main.ReloadConnections();

        // Sort at root — folder members should not be reordered.
        _main.SortSiblingsOf(
            _main.Metadata.RootNodes.OfType<ConnectionNodeViewModel>().Single(c => c.Profile.Name == "Zeta"),
            ascending: true);

        // No ConnectionSortOrders entry was set for any folder member.
        foreach (var name in new[] { "Charlie", "Alpha", "Bravo" })
        {
            var id = _main.Metadata.Connections.First(c => c.Profile.Name == name).Profile.Id;
            Assert.False(_main.FolderState.ConnectionSortOrders.ContainsKey(id),
                $"Folder member {name} sort order was touched by a root sort.");
        }
    }

    [Fact]
    public void DeleteConnection_RemovesStaleFolderMapping()
    {
        var p1 = MakeProfile("Alpha");
        var folder = _main.CreateFolder("Production");
        _main.FolderState.ConnectionFolderMap[p1.Id] = folder.Id;
        _main.ReloadConnections();
        Assert.True(_main.FolderState.ConnectionFolderMap.ContainsKey(p1.Id));

        _main.Delete(p1);

        Assert.False(_main.FolderState.ConnectionFolderMap.ContainsKey(p1.Id));
    }

    private static string NameOf(object node) => node switch
    {
        FolderNodeViewModel f => f.Name,
        ConnectionNodeViewModel c => c.Profile.Name,
        _ => string.Empty,
    };
}
