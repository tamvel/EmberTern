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

    // ---- ExecuteDrop (drag & drop) -----------------------------------------

    [Fact]
    public void ExecuteDrop_ConnectionIntoFolder_MapsAndPersists()
    {
        var p = MakeProfile("Alpha");
        var folder = _main.CreateFolder("Production");
        _main.ReloadConnections();

        var connVm = _main.Metadata.Connections.Single(c => c.Profile.Id == p.Id);
        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();

        _main.ExecuteDrop(connVm, folderVm, DropPosition.Into);

        Assert.Equal(folder.Id, _main.FolderState.ConnectionFolderMap[p.Id]);
        var refreshedFolder = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        Assert.Contains(refreshedFolder.Connections, c => c.Profile.Id == p.Id);
        // Persisted to disk.
        Assert.Equal(folder.Id, new FolderStore(_tempDir).Load().ConnectionFolderMap[p.Id]);
    }

    [Fact]
    public void ExecuteDrop_ConnectionIntoSameFolder_IsNoOp()
    {
        var p = MakeProfile("Alpha");
        var folder = _main.CreateFolder("Production");
        _main.FolderState.ConnectionFolderMap[p.Id] = folder.Id;
        _main.ReloadConnections();

        var connVm = _main.Metadata.Connections.Single(c => c.Profile.Id == p.Id);
        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();

        // Same folder — no sort order should be written.
        _main.ExecuteDrop(connVm, folderVm, DropPosition.Into);

        Assert.Equal(folder.Id, _main.FolderState.ConnectionFolderMap[p.Id]);
        Assert.False(_main.FolderState.ConnectionSortOrders.ContainsKey(p.Id));
    }

    [Fact]
    public void ExecuteDrop_ConnectionReorder_Before_AtRoot()
    {
        // Build a known root order: Alpha, Bravo, Charlie.
        MakeProfile("Alpha");
        MakeProfile("Bravo");
        MakeProfile("Charlie");
        _main.ReloadConnections();

        var alpha = _main.Metadata.Connections.Single(c => c.Profile.Name == "Alpha");
        var charlie = _main.Metadata.Connections.Single(c => c.Profile.Name == "Charlie");

        // Drop Charlie BEFORE Alpha → order: Charlie, Alpha, Bravo.
        _main.ExecuteDrop(charlie, alpha, DropPosition.Before);

        var rootNames = _main.Metadata.RootNodes.OfType<ConnectionNodeViewModel>()
            .Select(c => c.Profile.Name).ToList();
        Assert.Equal(new[] { "Charlie", "Alpha", "Bravo" }, rootNames);
    }

    [Fact]
    public void ExecuteDrop_ConnectionReorder_After_AtRoot()
    {
        MakeProfile("Alpha");
        MakeProfile("Bravo");
        MakeProfile("Charlie");
        _main.ReloadConnections();

        var alpha = _main.Metadata.Connections.Single(c => c.Profile.Name == "Alpha");
        var charlie = _main.Metadata.Connections.Single(c => c.Profile.Name == "Charlie");

        // Drop Alpha AFTER Charlie → order: Bravo, Charlie, Alpha.
        _main.ExecuteDrop(alpha, charlie, DropPosition.After);

        var rootNames = _main.Metadata.RootNodes.OfType<ConnectionNodeViewModel>()
            .Select(c => c.Profile.Name).ToList();
        Assert.Equal(new[] { "Bravo", "Charlie", "Alpha" }, rootNames);
    }

    [Fact]
    public void ExecuteDrop_ConnectionFromRoot_OntoConnectionInFolder_MovesIntoFolder()
    {
        var loose = MakeProfile("Alpha");
        var member = MakeProfile("Bravo");
        var folder = _main.CreateFolder("Production");
        _main.FolderState.ConnectionFolderMap[member.Id] = folder.Id;
        _main.ReloadConnections();

        var alphaVm = _main.Metadata.Connections.Single(c => c.Profile.Id == loose.Id);
        var bravoVm = _main.Metadata.Connections.Single(c => c.Profile.Id == member.Id);

        // Dropping Alpha before Bravo (which lives in folder) moves Alpha into that folder.
        _main.ExecuteDrop(alphaVm, bravoVm, DropPosition.Before);

        Assert.Equal(folder.Id, _main.FolderState.ConnectionFolderMap[loose.Id]);
        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        Assert.Equal(new[] { "Alpha", "Bravo" },
            folderVm.Connections.Select(c => c.Profile.Name).ToArray());
    }

    [Fact]
    public void ExecuteDrop_ConnectionFromFolder_OntoRootConnection_MovesToRoot()
    {
        var rootP = MakeProfile("Alpha");
        var memberP = MakeProfile("Bravo");
        var folder = _main.CreateFolder("Production");
        _main.FolderState.ConnectionFolderMap[memberP.Id] = folder.Id;
        _main.ReloadConnections();

        var alphaVm = _main.Metadata.Connections.Single(c => c.Profile.Id == rootP.Id);
        var bravoVm = _main.Metadata.Connections.Single(c => c.Profile.Id == memberP.Id);

        // Dropping Bravo AFTER Alpha at root pulls Bravo out of the folder.
        _main.ExecuteDrop(bravoVm, alphaVm, DropPosition.After);

        Assert.False(_main.FolderState.ConnectionFolderMap.ContainsKey(memberP.Id));
        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        Assert.Empty(folderVm.Connections);
        // Both at root, Bravo after Alpha.
        var rootNames = _main.Metadata.RootNodes.OfType<ConnectionNodeViewModel>()
            .Select(c => c.Profile.Name).ToList();
        Assert.Equal(new[] { "Alpha", "Bravo" }, rootNames);
    }

    [Fact]
    public void ExecuteDrop_FolderReorder_Before()
    {
        _main.CreateFolder("Banana");
        _main.CreateFolder("Apple");
        _main.CreateFolder("Cherry");
        _main.ReloadConnections();

        var apple = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Name == "Apple");
        var cherry = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Name == "Cherry");

        // Initial order (creation order, since CreateFolder bumps SortOrder per call):
        // Banana(0), Apple(1), Cherry(2). Drop Cherry BEFORE Apple → Banana, Cherry, Apple.
        _main.ExecuteDrop(cherry, apple, DropPosition.Before);

        var rootNames = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>()
            .Select(f => f.Name).ToList();
        Assert.Equal(new[] { "Banana", "Cherry", "Apple" }, rootNames);
    }

    [Fact]
    public void ExecuteDrop_FolderOntoFolderMemberConnection_IsNoOp()
    {
        var memberP = MakeProfile("Alpha");
        var folder = _main.CreateFolder("Production");
        var other = _main.CreateFolder("Other");
        _main.FolderState.ConnectionFolderMap[memberP.Id] = folder.Id;
        _main.ReloadConnections();

        var otherVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Name == "Other");
        var memberVm = _main.Metadata.Connections.Single(c => c.Profile.Id == memberP.Id);

        var beforeMap = new System.Collections.Generic.Dictionary<string, string>(_main.FolderState.ConnectionFolderMap);

        // Folders can only live at root — dropping a folder onto a folder-member
        // connection (whose container is a folder) should be rejected.
        _main.ExecuteDrop(otherVm, memberVm, DropPosition.Before);

        Assert.Equal(beforeMap, _main.FolderState.ConnectionFolderMap);
    }

    [Fact]
    public void ExecuteDrop_SourceEqualsTarget_IsNoOp()
    {
        var p = MakeProfile("Alpha");
        _main.ReloadConnections();
        var vm = _main.Metadata.Connections.Single();

        _main.ExecuteDrop(vm, vm, DropPosition.Before);

        Assert.False(_main.FolderState.ConnectionSortOrders.ContainsKey(p.Id));
    }

    // ---- Expand state persistence -----------------------------------------

    [Fact]
    public void NewFolder_DefaultsToExpanded_AndIsInExpandedSet()
    {
        var folder = _main.CreateFolder("Production");

        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        Assert.True(folderVm.IsExpanded);
        Assert.Contains(folder.Id, _main.FolderState.ExpandedNodeIds);
        // Persisted to disk.
        Assert.Contains(folder.Id, new FolderStore(_tempDir).Load().ExpandedNodeIds);
    }

    [Fact]
    public void FolderCollapse_RemovesIdFromSet_AndPersists()
    {
        var folder = _main.CreateFolder("Production");
        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();

        folderVm.IsExpanded = false;

        Assert.DoesNotContain(folder.Id, _main.FolderState.ExpandedNodeIds);
        Assert.DoesNotContain(folder.Id, new FolderStore(_tempDir).Load().ExpandedNodeIds);
    }

    [Fact]
    public void ConnectionExpand_AddsToSet_AndPersists()
    {
        var p = MakeProfile("Alpha");
        _main.ReloadConnections();
        var vm = _main.Metadata.Connections.Single();
        Assert.False(vm.IsExpanded);
        Assert.DoesNotContain(p.Id, _main.FolderState.ExpandedNodeIds);

        vm.IsExpanded = true;

        Assert.Contains(p.Id, _main.FolderState.ExpandedNodeIds);
        Assert.Contains(p.Id, new FolderStore(_tempDir).Load().ExpandedNodeIds);
    }

    [Fact]
    public void ReloadConnections_RestoresFolderCollapseState()
    {
        var folder = _main.CreateFolder("Production");
        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        folderVm.IsExpanded = false;
        Assert.False(folderVm.IsExpanded);

        // Reload (simulates what drag/drop does) — new VM instance, default would
        // be _isExpanded=true. Restore must pull it back to false.
        _main.ReloadConnections();
        var newVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Id == folder.Id);
        Assert.False(newVm.IsExpanded);
    }

    [Fact]
    public void ReloadConnections_RestoresConnectionExpandState_InsideFolder()
    {
        var p = MakeProfile("Alpha");
        var folder = _main.CreateFolder("Production");
        _main.FolderState.ConnectionFolderMap[p.Id] = folder.Id;
        _main.FolderState.ExpandedNodeIds.Add(p.Id);
        _main.ReloadConnections();

        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        var connVm = folderVm.Connections.Single();
        Assert.True(connVm.IsExpanded);
    }

    [Fact]
    public void DragReload_PreservesExpandState()
    {
        // Set up two folders, expand state recorded explicitly: A expanded, B collapsed.
        var fA = _main.CreateFolder("Alpha");
        var fB = _main.CreateFolder("Beta");
        var bVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Id == fB.Id);
        bVm.IsExpanded = false;

        // Trigger a drag-style reload by reordering the two folders.
        var aVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Id == fA.Id);
        _main.ExecuteDrop(bVm, aVm, DropPosition.Before);

        // After reload: A still expanded, B still collapsed.
        var aAfter = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Id == fA.Id);
        var bAfter = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Id == fB.Id);
        Assert.True(aAfter.IsExpanded);
        Assert.False(bAfter.IsExpanded);
    }

    [Fact]
    public void CaptureExpandState_MirrorsVmStateIntoSet()
    {
        var fA = _main.CreateFolder("Alpha");
        var fB = _main.CreateFolder("Beta");
        // Manually muck with state without going through setter so we can verify capture syncs it.
        _main.FolderState.ExpandedNodeIds.Clear();
        _main.FolderState.ExpandedNodeIds.Add(fA.Id);
        _main.FolderState.ExpandedNodeIds.Add(fB.Id);

        _main.CaptureExpandState();

        // VM defaults to expanded → set should contain both ids.
        Assert.Contains(fA.Id, _main.FolderState.ExpandedNodeIds);
        Assert.Contains(fB.Id, _main.FolderState.ExpandedNodeIds);
    }

    [Fact]
    public void RestoreExpandState_AppliesSetVerbatim()
    {
        var fA = _main.CreateFolder("Alpha");
        var fB = _main.CreateFolder("Beta");
        // Force B out of the set so restore should collapse it.
        _main.FolderState.ExpandedNodeIds.Remove(fB.Id);

        _main.RestoreExpandState();

        var aVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Id == fA.Id);
        var bVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single(f => f.Id == fB.Id);
        Assert.True(aVm.IsExpanded);
        Assert.False(bVm.IsExpanded);
    }

    [Fact]
    public void RestoreExpandState_DoesNotCollapseExpandedConnectionMissingFromSet()
    {
        // Simulates a freshly-connected node: expanded in the VM but (for the sake of
        // the test) absent from ExpandedNodeIds at the moment a restore pass runs.
        // RestoreExpandState must leave it expanded — connections are only ever
        // force-true, never force-false.
        var p = MakeProfile("Alpha");
        _main.ReloadConnections();
        var vm = _main.Metadata.Connections.Single();
        vm.IsExpanded = true;                              // adds p.Id to the set...
        _main.FolderState.ExpandedNodeIds.Remove(p.Id);    // ...remove it to set up the scenario.

        _main.RestoreExpandState();

        Assert.True(vm.IsExpanded);
    }

    [Fact]
    public void RestoreExpandState_CollapsesFolderMissingFromSet()
    {
        // Counterpart to the connection case: folders DO get force-collapsed when
        // absent (their default is expanded, so absence must mean collapsed).
        var folder = _main.CreateFolder("Production");
        var vm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        Assert.True(vm.IsExpanded);
        _main.FolderState.ExpandedNodeIds.Remove(folder.Id);

        _main.RestoreExpandState();

        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void RestoreExpandState_SuppressesSaves()
    {
        // Build a state where reload would flip multiple nodes; verify the on-disk
        // set matches what we set up — no spurious "current vm value" overwrites.
        MakeProfile("Alpha");
        _main.CreateFolder("Production");
        // Manually engineer set so legacy migration is already done.
        _main.FolderState.ExpandStateInitialized = true;
        // Hand-craft: folder collapsed, connection expanded.
        _main.FolderState.ExpandedNodeIds.Clear();
        _main.FolderState.ExpandedNodeIds.Add(_main.Metadata.Connections.Single().Profile.Id);
        _folderStore.Save(_main.FolderState);

        _main.ReloadConnections();

        // After restore: folder should be collapsed (absent), connection expanded (present).
        var folderVm = _main.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
        var connVm = _main.Metadata.Connections.Single();
        Assert.False(folderVm.IsExpanded);
        Assert.True(connVm.IsExpanded);

        // The on-disk set should still contain only the connection id (no auto-add of folder).
        var ondisk = new FolderStore(_tempDir).Load();
        Assert.DoesNotContain(folderVm.Id, ondisk.ExpandedNodeIds);
        Assert.Contains(connVm.Profile.Id, ondisk.ExpandedNodeIds);
    }

    [Fact]
    public void LegacyMigration_SeedsExistingFoldersIntoSet()
    {
        // Hand-write a legacy folders.json (no ExpandStateInitialized, no ExpandedNodeIds).
        var legacyFolder = new FolderEntry { Name = "Legacy", SortOrder = 0 };
        _folderStore.Save(new FolderState
        {
            Folders = { legacyFolder },
            ExpandStateInitialized = false,
        });

        // Fresh VM loads from disk.
        var freshService = new FirebirdConnectionService();
        try
        {
            var fresh = new MainWindowViewModel(_store, freshService, new TransactionService(freshService), new FolderStore(_tempDir));
            fresh.ConfirmationRequested += _ => Task.FromResult(true);

            Assert.True(fresh.FolderState.ExpandStateInitialized);
            Assert.Contains(legacyFolder.Id, fresh.FolderState.ExpandedNodeIds);
            var folderVm = fresh.Metadata.RootNodes.OfType<FolderNodeViewModel>().Single();
            Assert.True(folderVm.IsExpanded);
        }
        finally
        {
            freshService.Dispose();
        }
    }

    [Fact]
    public void ExecuteDrop_OrderPersistsAcrossReload()
    {
        MakeProfile("Alpha");
        MakeProfile("Bravo");
        MakeProfile("Charlie");
        _main.ReloadConnections();
        var alpha = _main.Metadata.Connections.Single(c => c.Profile.Name == "Alpha");
        var charlie = _main.Metadata.Connections.Single(c => c.Profile.Name == "Charlie");

        _main.ExecuteDrop(charlie, alpha, DropPosition.Before);

        // Fresh VM observes the same root order.
        var freshFolderStore = new FolderStore(_tempDir);
        var freshService = new FirebirdConnectionService();
        try
        {
            var fresh = new MainWindowViewModel(_store, freshService, new TransactionService(freshService), freshFolderStore);
            var rootNames = fresh.Metadata.RootNodes.OfType<ConnectionNodeViewModel>()
                .Select(c => c.Profile.Name).ToList();
            Assert.Equal(new[] { "Charlie", "Alpha", "Bravo" }, rootNames);
        }
        finally
        {
            freshService.Dispose();
        }
    }
}
