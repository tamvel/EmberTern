using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using Xunit;
using CoreSavedQuery = EmberTern.Core.Workspace.SavedQuery;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;

namespace EmberTern.Tests;

public class SavedQueryVmTests
{
    [Fact]
    public void Connect_BootstrapsQuery1_WhenNoSavedQueriesPresent()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");

        Assert.Single(h.Main.SavedQueries);
        Assert.Equal("Query 1", h.Main.SavedQueries[0].Name);
        Assert.Same(h.Main.SavedQueries[0], h.Main.SelectedSavedQuery);
        Assert.Equal(string.Empty, h.Main.QueryText);
    }

    [Fact]
    public void Connect_LoadsExistingSavedQueries_AndSelectsActive()
    {
        using var h = new Harness();
        h.Main.RestoreWorkspace(new WorkspaceState
        {
            Workspaces =
            {
                ["A"] = new ConnectionWorkspace
                {
                    Tabs = { new WorkspaceTab { Kind = CoreTabKind.Query, SqlText = "ignored" } },
                    SavedQueries =
                    {
                        new CoreSavedQuery { Id = "q1", Name = "Customers", SqlText = "select * from CUSTOMERS;" },
                        new CoreSavedQuery { Id = "q2", Name = "Orders", SqlText = "select * from ORDERS;" },
                    },
                    ActiveSavedQueryId = "q2",
                },
            },
        });

        h.Main.ApplyActiveConnectionChange("A");

        Assert.Equal(2, h.Main.SavedQueries.Count);
        Assert.Equal("q2", h.Main.SelectedSavedQuery?.Id);
        Assert.Equal("select * from ORDERS;", h.Main.QueryText);
    }

    [Fact]
    public void LegacyWorkspace_NoSavedQueries_BootstrapsFromTabSqlText()
    {
        using var h = new Harness();
        h.Main.RestoreWorkspace(new WorkspaceState
        {
            Workspaces =
            {
                ["A"] = new ConnectionWorkspace
                {
                    Tabs = { new WorkspaceTab { Kind = CoreTabKind.Query, SqlText = "legacy editor text" } },
                },
            },
        });

        h.Main.ApplyActiveConnectionChange("A");

        Assert.Single(h.Main.SavedQueries);
        Assert.Equal("Query 1", h.Main.SavedQueries[0].Name);
        Assert.Equal("legacy editor text", h.Main.SavedQueries[0].SqlText);
        Assert.Equal("legacy editor text", h.Main.QueryText);
    }

    [Fact]
    public void EditingQueryText_WritesBackToActiveSavedQuery()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");

        h.Main.QueryText = "select 42 from rdb$database;";

        Assert.Equal("select 42 from rdb$database;", h.Main.SelectedSavedQuery!.SqlText);
    }

    [Fact]
    public void SelectingDifferentSavedQuery_LoadsItsTextIntoEditor()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "first";
        h.Main.NewQueryCommand.Execute(null);
        h.Main.QueryText = "second";

        // Flip back to the first one
        h.Main.SelectedSavedQuery = h.Main.SavedQueries[0];

        Assert.Equal("first", h.Main.QueryText);
        Assert.Equal("second", h.Main.SavedQueries[1].SqlText);
    }

    [Fact]
    public void NewQuery_PicksNextNumber_AfterExisting()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        // Bootstrap already gave us "Query 1".

        h.Main.NewQueryCommand.Execute(null);
        h.Main.NewQueryCommand.Execute(null);

        Assert.Equal(3, h.Main.SavedQueries.Count);
        Assert.Equal("Query 1", h.Main.SavedQueries[0].Name);
        Assert.Equal("Query 2", h.Main.SavedQueries[1].Name);
        Assert.Equal("Query 3", h.Main.SavedQueries[2].Name);
    }

    [Fact]
    public void NewQuery_IgnoresRenamedQueries_WhenPickingNumber()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.SavedQueries[0].Name = "Customer report";

        h.Main.NewQueryCommand.Execute(null);

        // Only renamed queries exist → next number is 1.
        Assert.Equal("Query 1", h.Main.SavedQueries[1].Name);
    }

    [Fact]
    public void NewQuery_DisabledWithoutActiveWorkspace()
    {
        using var h = new Harness();
        Assert.False(h.Main.NewQueryCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeleteSelected_RemovesAndSelectsNeighbor()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.NewQueryCommand.Execute(null);
        h.Main.NewQueryCommand.Execute(null);
        var middle = h.Main.SavedQueries[1];
        h.Main.SelectedSavedQuery = middle;

        await h.Main.DeleteSelectedQueryCommand.ExecuteAsync(null);

        Assert.Equal(2, h.Main.SavedQueries.Count);
        Assert.DoesNotContain(middle, h.Main.SavedQueries);
        Assert.NotNull(h.Main.SelectedSavedQuery);
    }

    [Fact]
    public async Task DeleteLastQuery_ReBootstrapsQuery1()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "doomed";

        await h.Main.DeleteSelectedQueryCommand.ExecuteAsync(null);

        // List is never empty — must always present a target for the next keystroke.
        Assert.Single(h.Main.SavedQueries);
        Assert.Equal("Query 1", h.Main.SavedQueries[0].Name);
        Assert.Equal(string.Empty, h.Main.SavedQueries[0].SqlText);
        Assert.Same(h.Main.SavedQueries[0], h.Main.SelectedSavedQuery);
        Assert.Equal(string.Empty, h.Main.QueryText);
    }

    [Fact]
    public async Task ClearAll_RemovesAllAndCreatesFreshQuery1()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.NewQueryCommand.Execute(null);
        h.Main.NewQueryCommand.Execute(null);

        await h.Main.ClearAllQueriesCommand.ExecuteAsync(null);

        Assert.Single(h.Main.SavedQueries);
        Assert.Equal("Query 1", h.Main.SavedQueries[0].Name);
        Assert.Same(h.Main.SavedQueries[0], h.Main.SelectedSavedQuery);
    }

    [Fact]
    public void ToggleQueryPanel_FlipsVisibility()
    {
        using var h = new Harness();
        Assert.True(h.Main.IsQueryPanelVisible);

        h.Main.ToggleQueryPanelCommand.Execute(null);
        Assert.False(h.Main.IsQueryPanelVisible);

        h.Main.ToggleQueryPanelCommand.Execute(null);
        Assert.True(h.Main.IsQueryPanelVisible);
    }

    [Fact]
    public void Disconnect_StashesSavedQueriesIntoDict()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "first";
        h.Main.NewQueryCommand.Execute(null);
        h.Main.QueryText = "second";

        h.Main.ApplyActiveConnectionChange(null);

        var stashed = h.Main.WorkspacesByConnection["A"];
        Assert.Equal(2, stashed.SavedQueries.Count);
        Assert.Equal("second", stashed.SavedQueries[1].SqlText);
        Assert.Equal(stashed.SavedQueries[1].Id, stashed.ActiveSavedQueryId);
    }

    [Fact]
    public void Reconnect_RestoresSavedQueriesAndActiveSelection()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "first";
        h.Main.NewQueryCommand.Execute(null);
        h.Main.QueryText = "second";

        h.Main.ApplyActiveConnectionChange(null);
        h.Main.ApplyActiveConnectionChange("A");

        Assert.Equal(2, h.Main.SavedQueries.Count);
        Assert.Equal("second", h.Main.SelectedSavedQuery?.SqlText);
        Assert.Equal("second", h.Main.QueryText);
    }

    [Fact]
    public void Capture_SerializesSavedQueriesAndPanelVisibility()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "hello";
        h.Main.IsQueryPanelVisible = false;

        var state = h.Main.CaptureWorkspace();

        Assert.False(state.QueryPanelVisible);
        var ws = state.Workspaces["A"];
        Assert.Single(ws.SavedQueries);
        Assert.Equal("hello", ws.SavedQueries[0].SqlText);
        Assert.Equal(ws.SavedQueries[0].Id, ws.ActiveSavedQueryId);
    }

    [Fact]
    public void Restore_ReadsPanelVisibility()
    {
        using var h = new Harness();

        h.Main.RestoreWorkspace(new WorkspaceState { QueryPanelVisible = false });

        Assert.False(h.Main.IsQueryPanelVisible);
    }

    [Fact]
    public void Switch_BetweenConnections_EachHasIndependentSavedQueries()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "from A";
        h.Main.NewQueryCommand.Execute(null);
        h.Main.QueryText = "from A second";

        h.Main.ApplyActiveConnectionChange("B");
        Assert.Single(h.Main.SavedQueries);
        Assert.Equal(string.Empty, h.Main.QueryText);
        h.Main.QueryText = "from B";

        h.Main.ApplyActiveConnectionChange("A");
        Assert.Equal(2, h.Main.SavedQueries.Count);
        Assert.Equal("from A second", h.Main.QueryText);

        // B is still preserved in the dict.
        Assert.Equal("from B", h.Main.WorkspacesByConnection["B"].SavedQueries[0].SqlText);
    }

    [Fact]
    public void BeginRename_SeedsEditingNameAndFlipsFlag()
    {
        var sq = new SavedQueryViewModel("id1", "Customers", "select 1;");

        sq.BeginRenameCommand.Execute(null);

        Assert.True(sq.IsRenaming);
        Assert.False(sq.IsNotRenaming);
        Assert.Equal("Customers", sq.EditingName);
        Assert.Equal("Customers", sq.Name);
    }

    [Fact]
    public void CommitRename_AppliesEditingNameAndExitsRenameMode()
    {
        var sq = new SavedQueryViewModel("id1", "Customers", "select 1;");
        sq.BeginRenameCommand.Execute(null);
        sq.EditingName = "  Customer report  ";

        sq.CommitRenameCommand.Execute(null);

        Assert.False(sq.IsRenaming);
        Assert.Equal("Customer report", sq.Name);
    }

    [Fact]
    public void CommitRename_BlankInput_KeepsOriginalName()
    {
        var sq = new SavedQueryViewModel("id1", "Customers", "select 1;");
        sq.BeginRenameCommand.Execute(null);
        sq.EditingName = "   ";

        sq.CommitRenameCommand.Execute(null);

        Assert.False(sq.IsRenaming);
        Assert.Equal("Customers", sq.Name);
    }

    [Fact]
    public void CancelRename_RevertsWithoutMutatingName()
    {
        var sq = new SavedQueryViewModel("id1", "Customers", "select 1;");
        sq.BeginRenameCommand.Execute(null);
        sq.EditingName = "Different";

        sq.CancelRenameCommand.Execute(null);

        Assert.False(sq.IsRenaming);
        Assert.Equal("Customers", sq.Name);
    }

    [Fact]
    public async Task DeleteCommandOnVm_RoutesThroughOwnerAndRemoves()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        // Auto-confirm — match the existing DeleteSelectedQuery test pattern.
        h.Main.ConfirmationRequested += _ => Task.FromResult(true);
        h.Main.NewQueryCommand.Execute(null);
        var target = h.Main.SavedQueries[1];

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)target.DeleteCommand).ExecuteAsync(null);

        Assert.DoesNotContain(target, h.Main.SavedQueries);
    }

    [Fact]
    public void DeleteCommandOnBareVm_IsNoOpWithoutOwner()
    {
        // SavedQueryViewModel constructed without an owner (tests, design-time) must
        // not crash when DeleteCommand fires — DeleteSavedQueryAsync requires the owner.
        var sq = new SavedQueryViewModel("id1", "Name", "select 1;");
        // Should complete without throwing.
        var task = ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)sq.DeleteCommand).ExecuteAsync(null);
        Assert.True(task.IsCompleted);
    }

    [Fact]
    public void RenamedSavedQuery_PersistsThroughCaptureWorkspace()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        var sq = h.Main.SavedQueries[0];
        sq.BeginRenameCommand.Execute(null);
        sq.EditingName = "Customer report";
        sq.CommitRenameCommand.Execute(null);

        var state = h.Main.CaptureWorkspace();

        Assert.Equal("Customer report", state.Workspaces["A"].SavedQueries[0].Name);
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Store = new ConnectionProfileStore(TempDir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(Store, Service);
        }

        public string TempDir { get; }
        public ConnectionProfileStore Store { get; }
        public FirebirdConnectionService Service { get; }
        public MainWindowViewModel Main { get; }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
