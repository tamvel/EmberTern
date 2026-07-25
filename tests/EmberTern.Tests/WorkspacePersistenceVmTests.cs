using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Controls;
using EmberTern.App.Debugging;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using Xunit;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;
using VmTabKind = EmberTern.App.ViewModels.WorkspaceTabKind;

namespace EmberTern.Tests;

public class WorkspacePersistenceVmTests
{
    [Fact]
    public void Ctor_NoActiveConnection_NoTabsVisible()
    {
        using var harness = new Harness();
        Assert.Empty(harness.Main.WorkspaceTabs);
        Assert.Null(harness.Main.SelectedWorkspaceTab);
    }

    [Fact]
    public void SimulateConnect_CreatesEmptyQueryTab_WhenProfileIsNew()
    {
        using var harness = new Harness();

        harness.Main.ApplyActiveConnectionChange("profileA");

        Assert.Single(harness.Main.WorkspaceTabs);
        Assert.Equal(VmTabKind.Query, harness.Main.WorkspaceTabs[0].Kind);
        Assert.Same(harness.Main.WorkspaceTabs[0], harness.Main.SelectedWorkspaceTab);
        Assert.Equal(string.Empty, harness.Main.QueryText);
    }

    [Fact]
    public void Disconnect_StashesTabs_ClearsWorkspace()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.QueryText = "select 42;";

        harness.Main.ApplyActiveConnectionChange(null);

        Assert.Empty(harness.Main.WorkspaceTabs);
        Assert.Null(harness.Main.SelectedWorkspaceTab);
        Assert.Equal(string.Empty, harness.Main.QueryText);
        // Stashed under the previous profile id.
        Assert.True(harness.Main.WorkspacesByConnection.ContainsKey("A"));
        Assert.Equal("select 42;", harness.Main.WorkspacesByConnection["A"].Tabs[0].SqlText);
    }

    [Fact]
    public void Reconnect_RestoresStashedTabs()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.QueryText = "select 42 from rdb$database;";
        var obj = new MetadataObject("MY_PROC", MetadataObjectKind.Procedure);
        harness.Main.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateDdl(harness.Main, obj, "CREATE PROCEDURE MY_PROC ...", "A"));

        harness.Main.ApplyActiveConnectionChange(null);
        harness.Main.ApplyActiveConnectionChange("A");

        Assert.Equal(2, harness.Main.WorkspaceTabs.Count);
        Assert.Equal("select 42 from rdb$database;", harness.Main.QueryText);
        Assert.Equal("MY_PROC", harness.Main.WorkspaceTabs[1].ObjectName);
        Assert.Equal("CREATE PROCEDURE MY_PROC ...", harness.Main.WorkspaceTabs[1].DdlText);
    }

    [Fact]
    public void Switch_FromAtoB_HidesAtabsLoadsB()
    {
        using var harness = new Harness();
        // Profile A has a single tab with SQL.
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.QueryText = "-- profile A\nselect 1;";

        // Switch directly to B without disconnect in between (defensive — current
        // service forbids this, but the VM must still cope).
        harness.Main.ApplyActiveConnectionChange("B");

        // B starts fresh with an empty Query tab.
        Assert.Single(harness.Main.WorkspaceTabs);
        Assert.Equal(string.Empty, harness.Main.QueryText);
        // A's tabs are stashed.
        Assert.Equal("-- profile A\nselect 1;", harness.Main.WorkspacesByConnection["A"].Tabs[0].SqlText);
    }

    [Fact]
    public void Switch_BackToA_RestoresA_DoesNotLoseB()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.QueryText = "from A";
        harness.Main.ApplyActiveConnectionChange("B");
        harness.Main.QueryText = "from B";
        harness.Main.ApplyActiveConnectionChange("A");

        Assert.Equal("from A", harness.Main.QueryText);
        Assert.Equal("from B", harness.Main.WorkspacesByConnection["B"].Tabs[0].SqlText);
    }

    [Fact]
    public void Capture_WhileConnected_IncludesActiveTabsInDict()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.QueryText = "captured-while-active";

        // Pretend the service still says A is active.
        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.ContainsKey("A"));
        Assert.Equal("captured-while-active", state.Workspaces["A"].Tabs[0].SqlText);
    }

    [Fact]
    public void Capture_AfterDisconnect_IncludesStashedTabs()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.QueryText = "stashed";
        harness.Main.ApplyActiveConnectionChange(null);

        var state = harness.Main.CaptureWorkspace();

        Assert.True(state.Workspaces.ContainsKey("A"));
        Assert.Equal("stashed", state.Workspaces["A"].Tabs[0].SqlText);
    }

    [Fact]
    public void Restore_LoadsDictButDoesNotPopulateTabs()
    {
        using var harness = new Harness();

        harness.Main.RestoreWorkspace(new WorkspaceState
        {
            LastActiveConnectionId = null,
            Workspaces =
            {
                ["A"] = new ConnectionWorkspace
                {
                    Tabs = { new WorkspaceTab { Kind = CoreTabKind.Query, SqlText = "persisted" } },
                },
            },
        });

        // Without an active connection, no tabs are visible yet.
        Assert.Empty(harness.Main.WorkspaceTabs);
        // But the dict picked them up.
        Assert.True(harness.Main.WorkspacesByConnection.ContainsKey("A"));

        // Connecting to A now restores those tabs.
        harness.Main.ApplyActiveConnectionChange("A");
        Assert.Single(harness.Main.WorkspaceTabs);
        Assert.Equal("persisted", harness.Main.QueryText);
    }

    [Fact]
    public void Restore_SelectsConnectionNodeForLastActiveProfile()
    {
        using var harness = new Harness();
        var a = new ConnectionProfile { Name = "A", Host = "h", Port = 3050 };
        var b = new ConnectionProfile { Name = "B", Host = "h", Port = 3050 };
        harness.Store.Upsert(a);
        harness.Store.Upsert(b);
        harness.Main.ReloadConnections();

        harness.Main.RestoreWorkspace(new WorkspaceState { LastActiveConnectionId = b.Id });

        Assert.NotNull(harness.Main.Metadata.SelectedConnection);
        Assert.Equal(b.Id, harness.Main.Metadata.SelectedConnection!.Profile.Id);
    }

    [Fact]
    public void DeleteProfile_DropsDictEntry()
    {
        using var harness = new Harness();
        var p = new ConnectionProfile { Name = "X", Host = "h", Port = 3050 };
        harness.Store.Upsert(p);
        harness.Main.ReloadConnections();
        // Stash some tabs under p.Id
        harness.Main.ApplyActiveConnectionChange(p.Id);
        harness.Main.QueryText = "doomed";
        harness.Main.ApplyActiveConnectionChange(null);
        Assert.True(harness.Main.WorkspacesByConnection.ContainsKey(p.Id));

        harness.Main.Delete(p);

        Assert.False(harness.Main.WorkspacesByConnection.ContainsKey(p.Id));
    }

    [Fact]
    public void CaptureRestore_FullCrossInstanceRoundtrip()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        harness.Main.QueryText = "select * from t;";
        var obj = new MetadataObject("MY_PROC", MetadataObjectKind.Procedure);
        var tab = WorkspaceTabViewModel.CreateDdl(harness.Main, obj, "CREATE PROCEDURE MY_PROC ...", "A");
        harness.Main.WorkspaceTabs.Add(tab);
        harness.Main.SelectedWorkspaceTab = tab;

        var captured = harness.Main.CaptureWorkspace();
        Assert.Equal(1, captured.Workspaces["A"].ActiveTabIndex);

        using var harness2 = new Harness();
        harness2.Main.RestoreWorkspace(captured);
        // Restore alone doesn't display tabs.
        Assert.Empty(harness2.Main.WorkspaceTabs);

        // Connect to A to bring them back.
        harness2.Main.ApplyActiveConnectionChange("A");
        Assert.Equal(2, harness2.Main.WorkspaceTabs.Count);
        Assert.Equal("select * from t;", harness2.Main.QueryText);
        Assert.Equal("MY_PROC", harness2.Main.WorkspaceTabs[1].ObjectName);
        Assert.Equal("CREATE PROCEDURE MY_PROC ...", harness2.Main.WorkspaceTabs[1].DdlText);
        Assert.Same(harness2.Main.WorkspaceTabs[1], harness2.Main.SelectedWorkspaceTab);
    }

    [Fact]
    public void Disconnect_ClearsResultsMessagesAndStats()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");

        // Simulate state left over from a successful query.
        harness.Main.CurrentResult = new EmberTern.Core.Query.QueryResult
        {
            Columns = new[] { new EmberTern.Core.Query.QueryColumn("c1", typeof(int)) },
            Rows = new[] { new object?[] { 1 } },
            Elapsed = System.TimeSpan.FromMilliseconds(10),
        };
        harness.Main.CurrentResultVersionTag = "v1";
        harness.Main.Messages.Add(new QueryMessageViewModel(MessageSeverity.Info, "ok"));
        harness.Main.QueryStatsText = "1 row in 10 ms";

        harness.Main.ApplyActiveConnectionChange(null);

        Assert.Null(harness.Main.CurrentResult);
        Assert.Empty(harness.Main.Messages);
        Assert.Equal(string.Empty, harness.Main.QueryStatsText);
        Assert.False(harness.Main.HasCurrentResult);
        Assert.True(harness.Main.ShowResultsEmptyHint);
        Assert.False(harness.Main.HasMessages);
    }

    [Fact]
    public void LoadForProfile_CorruptDictWithoutQueryTab_StillPresentsOne()
    {
        using var harness = new Harness();
        harness.Main.RestoreWorkspace(new WorkspaceState
        {
            Workspaces =
            {
                ["A"] = new ConnectionWorkspace
                {
                    // Pathological: only DDL tabs, no Query tab — make sure the user
                    // doesn't end up with a connection that has no editor.
                    Tabs =
                    {
                        new WorkspaceTab { Kind = CoreTabKind.Ddl, ObjectName = "X", ObjectKind = MetadataObjectKind.Table, DdlText = "DDL" },
                    },
                },
            },
        });

        harness.Main.ApplyActiveConnectionChange("A");

        Assert.Equal(2, harness.Main.WorkspaceTabs.Count);
        Assert.Equal(VmTabKind.Query, harness.Main.WorkspaceTabs[0].Kind);
    }

    [Fact]
    public void DebuggerTab_IsTransient_NotCaptured()
    {
        using var harness = new Harness();
        harness.Main.ApplyActiveConnectionChange("A");
        var dbg = new DebuggerTabViewModel(
            "SP_X",
            _ => Task.FromResult<string?>("create procedure sp_x as begin end"),
            new NoopLauncher());
        harness.Main.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateDebugger(
            harness.Main, dbg, "SP_X", "A", MetadataObjectKind.Procedure));

        var state = harness.Main.CaptureWorkspace();

        // The debugger tab is a transient session, not a document — only the Query tab is captured,
        // so nothing is "restored" on the next app launch.
        Assert.Single(state.Workspaces["A"].Tabs);
        Assert.Equal(CoreTabKind.Query, state.Workspaces["A"].Tabs[0].Kind);
        Assert.DoesNotContain(state.Workspaces["A"].Tabs, t => t.ObjectName == "SP_X");
    }

    // A launcher that is never invoked: the test only opens the tab, it never launches a session (no server).
    private sealed class NoopLauncher : IDebugSessionLauncher
    {
        public Task<DebugRunHandle> LaunchAsync(DebugLaunchSpec spec, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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
