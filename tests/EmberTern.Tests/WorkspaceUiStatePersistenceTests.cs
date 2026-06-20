using System;
using System.IO;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using Xunit;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;

namespace EmberTern.Tests;

// Per-tab editor UI state (Source/Easy mode, active sub-tabs, grid-edit) + global
// UI prefs (View/Procedure Easy default, results-maximized, bottom-panel tab) must
// survive a restart. Hybrid model: a workspace-restored tab uses its own per-tab
// value; a freshly opened object uses the global preference.
public class WorkspaceUiStatePersistenceTests
{
    // ── Capture side: SnapshotCurrentTabs writes the per-tab UI fields ──────────

    [Fact]
    public void Capture_ViewDetailTab_PersistsEasyModeAndSubTab()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("V_ORDERS", MetadataObjectKind.View);
        var detail = h.Main.CreateViewDetail(obj);
        detail.EasyMode = true;
        detail.ActiveSubTabIndex = 2;
        h.Main.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateViewDetail(h.Main, obj, detail, "A"));

        var tab = h.Main.CaptureWorkspace().Workspaces["A"].Tabs[1];
        Assert.Equal(CoreTabKind.ViewDetail, tab.Kind);
        Assert.True(tab.EasyMode);
        Assert.Equal(2, tab.ActiveSubTabIndex);
    }

    [Fact]
    public void Capture_ProcedureDetailTab_PersistsEasyModeSubTabAndInner()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("SP_BALANCE", MetadataObjectKind.Procedure);
        var detail = h.Main.CreateProcedureDetail(obj);
        detail.EasyMode = true;
        detail.ActiveSubTabIndex = 1;
        detail.ActiveEasyCollectionIndex = 3;
        h.Main.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateProcedureDetail(h.Main, obj, detail, "A"));

        var tab = h.Main.CaptureWorkspace().Workspaces["A"].Tabs[1];
        Assert.Equal(CoreTabKind.ProcedureDetail, tab.Kind);
        Assert.True(tab.EasyMode);
        Assert.Equal(1, tab.ActiveSubTabIndex);
        Assert.Equal(3, tab.ActiveInnerSubTabIndex);
    }

    [Fact]
    public void Capture_TableDetailTab_PersistsSubTabConstraintsAndEditMode()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");

        var obj = new MetadataObject("ORDERS", MetadataObjectKind.Table);
        var detail = h.Main.CreateTableDetail(obj);
        detail.ActiveSubTabIndex = TableDetailTabViewModel.ConstraintsSubTabIndex;
        detail.ConstraintsActiveSubTabIndex = TableDetailTabViewModel.ConstraintsForeignKeysIndex;
        detail.IsFieldEditMode = true;
        h.Main.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateTableDetail(h.Main, obj, detail, "A"));

        var tab = h.Main.CaptureWorkspace().Workspaces["A"].Tabs[1];
        Assert.Equal(CoreTabKind.TableDetail, tab.Kind);
        Assert.Equal(TableDetailTabViewModel.ConstraintsSubTabIndex, tab.ActiveSubTabIndex);
        Assert.Equal(TableDetailTabViewModel.ConstraintsForeignKeysIndex, tab.ActiveInnerSubTabIndex);
        Assert.True(tab.GridEditMode);
    }

    // ── Restore side: LoadWorkspaceFor applies the per-tab UI fields ────────────

    [Fact]
    public void Restore_ViewDetailTab_AppliesEasyModeAndSubTab()
    {
        var captured = CaptureWithViewTab(easyMode: true, subTab: 2);

        using var h = new Harness();
        h.Main.RestoreWorkspace(captured);
        h.Main.ApplyActiveConnectionChange("A");

        var detail = h.Main.WorkspaceTabs[1].ViewDetail!;
        Assert.True(detail.EasyMode);
        Assert.Equal(2, detail.ActiveSubTabIndex);
    }

    [Fact]
    public void Restore_TableDetailTab_AppliesSubTabConstraintsAndEditMode()
    {
        WorkspaceState captured;
        using (var h1 = new Harness())
        {
            h1.Main.ApplyActiveConnectionChange("A");
            var obj = new MetadataObject("ORDERS", MetadataObjectKind.Table);
            var detail = h1.Main.CreateTableDetail(obj);
            detail.ActiveSubTabIndex = TableDetailTabViewModel.IndexesSubTabIndex;
            detail.ConstraintsActiveSubTabIndex = TableDetailTabViewModel.ConstraintsCheckIndex;
            detail.IsFieldEditMode = true;
            h1.Main.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateTableDetail(h1.Main, obj, detail, "A"));
            // Keep the Query tab active so restore's SelectTab doesn't lazy-load the table.
            captured = h1.Main.CaptureWorkspace();
        }

        using var h2 = new Harness();
        h2.Main.RestoreWorkspace(captured);
        h2.Main.ApplyActiveConnectionChange("A");

        var restored = h2.Main.WorkspaceTabs[1].TableDetail!;
        Assert.Equal(TableDetailTabViewModel.IndexesSubTabIndex, restored.ActiveSubTabIndex);
        Assert.Equal(TableDetailTabViewModel.ConstraintsCheckIndex, restored.ConstraintsActiveSubTabIndex);
        Assert.True(restored.IsFieldEditMode);
    }

    [Fact]
    public void Restore_PerTabEasyMode_OverridesGlobalPreference()
    {
        // Tab was saved in Easy mode; the global default is Source. The restored tab
        // must come back Easy (per-tab wins — hybrid model).
        var captured = CaptureWithViewTab(easyMode: true, subTab: 0);
        captured.ViewEasyMode = false; // global default = Source

        using var h = new Harness();
        h.Main.RestoreWorkspace(captured);
        Assert.False(h.Main.ViewEasyModePreference); // global seed is Source
        h.Main.ApplyActiveConnectionChange("A");

        Assert.True(h.Main.WorkspaceTabs[1].ViewDetail!.EasyMode);
    }

    // ── Global UI preferences ───────────────────────────────────────────────────

    [Fact]
    public void Capture_GlobalUiPrefs_RoundTrip()
    {
        using var h = new Harness();
        h.Main.ViewEasyModePreference = true;
        h.Main.ProcedureEasyModePreference = true;
        h.Main.IsQueryPanelVisible = false;
        h.Main.SelectedBottomTabIndex = 2;

        var state = h.Main.CaptureWorkspace();
        Assert.True(state.ViewEasyMode);
        Assert.True(state.ProcedureEasyMode);
        Assert.False(state.QueryPanelVisible);
        Assert.Equal(2, state.BottomPanelTabIndex);

        using var h2 = new Harness();
        h2.Main.RestoreWorkspace(state);
        Assert.True(h2.Main.ViewEasyModePreference);
        Assert.True(h2.Main.ProcedureEasyModePreference);
        Assert.False(h2.Main.IsQueryPanelVisible);
        Assert.Equal(2, h2.Main.SelectedBottomTabIndex);
    }

    [Fact]
    public void CreateViewDetail_AppliesGlobalEasyModePreference_ForFreshlyOpened()
    {
        using var h = new Harness();
        h.Main.ViewEasyModePreference = true;

        var detail = h.Main.CreateViewDetail(new MetadataObject("V_X", MetadataObjectKind.View));

        Assert.True(detail.EasyMode);
    }

    // Automated equivalent of the manual smoke test: build the full scenario, persist
    // through the REAL WorkspaceStore (settings.dat on disk), reload in a fresh VM, and
    // verify every per-tab + global UI state came back. This exercises the exact
    // Save/Load + Capture/Restore path the app runs on close/launch — minus the live
    // FbConnection (the detail tabs' lazy load needs a real DB; the Query tab stays
    // active so restore's SelectTab never triggers it).
    [Fact]
    public void EndToEnd_FullScenario_SurvivesDiskRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "embertern-e2e-" + Guid.NewGuid().ToString("N"));
        try
        {
            // ── Session 1: build, capture, persist to disk ──
            using (var h1 = new Harness(dir))
            {
                h1.Main.ApplyActiveConnectionChange("A");

                AddView(h1.Main, "V_EASY", easy: true, subTab: 1);
                AddView(h1.Main, "V_SOURCE", easy: false, subTab: 0);

                // Easy procedure parked on the Variables collection sub-tab (index 2).
                AddProc(h1.Main, "P_EASY", easy: true, easyCollection: 2);
                AddProc(h1.Main, "P_SOURCE", easy: false, easyCollection: 0);

                AddTable(h1.Main, "T1",
                    subTab: TableDetailTabViewModel.ConstraintsSubTabIndex,
                    constraints: TableDetailTabViewModel.ConstraintsUniqueIndex,
                    gridEdit: true);

                h1.Main.SelectedBottomTabIndex = 1; // Messages
                // Keep the Query tab active so restore's SelectTab doesn't lazy-load details.
                h1.Main.SelectedWorkspaceTab = h1.Main.WorkspaceTabs[0];

                var state = h1.Main.CaptureWorkspace();
                state.ResultsMaximized = true; // owned by the View code-behind; simulate it
                new WorkspaceStore(dir).Save(state);
            }

            // ── Session 2: reload from disk, restore, reconnect ──
            var reloaded = new WorkspaceStore(dir).Load();
            Assert.NotNull(reloaded);
            Assert.True(reloaded!.ResultsMaximized);
            Assert.Equal(1, reloaded.BottomPanelTabIndex);

            using var h2 = new Harness(dir);
            h2.Main.RestoreWorkspace(reloaded);
            Assert.Equal(1, h2.Main.SelectedBottomTabIndex);
            h2.Main.ApplyActiveConnectionChange("A");

            // Query + 2 views + 2 procedures + 1 table.
            Assert.Equal(6, h2.Main.WorkspaceTabs.Count);

            var vEasy = FindDetail(h2.Main, "V_EASY").ViewDetail!;
            var vSource = FindDetail(h2.Main, "V_SOURCE").ViewDetail!;
            Assert.True(vEasy.EasyMode);
            Assert.Equal(1, vEasy.ActiveSubTabIndex);
            Assert.False(vSource.EasyMode);

            var pEasy = FindDetail(h2.Main, "P_EASY").ProcedureDetail!;
            var pSource = FindDetail(h2.Main, "P_SOURCE").ProcedureDetail!;
            Assert.True(pEasy.EasyMode);
            Assert.Equal(2, pEasy.ActiveEasyCollectionIndex);
            Assert.False(pSource.EasyMode);

            var t = FindDetail(h2.Main, "T1").TableDetail!;
            Assert.Equal(TableDetailTabViewModel.ConstraintsSubTabIndex, t.ActiveSubTabIndex);
            Assert.Equal(TableDetailTabViewModel.ConstraintsUniqueIndex, t.ConstraintsActiveSubTabIndex);
            Assert.True(t.IsFieldEditMode);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static WorkspaceTabViewModel FindDetail(MainWindowViewModel m, string name)
        => m.WorkspaceTabs.First(t => t.ObjectName == name);

    private static void AddView(MainWindowViewModel m, string name, bool easy, int subTab)
    {
        var obj = new MetadataObject(name, MetadataObjectKind.View);
        var d = m.CreateViewDetail(obj);
        d.EasyMode = easy;
        d.ActiveSubTabIndex = subTab;
        m.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateViewDetail(m, obj, d, "A"));
    }

    private static void AddProc(MainWindowViewModel m, string name, bool easy, int easyCollection)
    {
        var obj = new MetadataObject(name, MetadataObjectKind.Procedure);
        var d = m.CreateProcedureDetail(obj);
        d.EasyMode = easy;
        d.ActiveEasyCollectionIndex = easyCollection;
        m.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateProcedureDetail(m, obj, d, "A"));
    }

    private static void AddTable(MainWindowViewModel m, string name, int subTab, int constraints, bool gridEdit)
    {
        var obj = new MetadataObject(name, MetadataObjectKind.Table);
        var d = m.CreateTableDetail(obj);
        d.ActiveSubTabIndex = subTab;
        d.ConstraintsActiveSubTabIndex = constraints;
        d.IsFieldEditMode = gridEdit;
        m.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateTableDetail(m, obj, d, "A"));
    }

    private static WorkspaceState CaptureWithViewTab(bool easyMode, int subTab)
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        var obj = new MetadataObject("V_ORDERS", MetadataObjectKind.View);
        var detail = h.Main.CreateViewDetail(obj);
        detail.EasyMode = easyMode;
        detail.ActiveSubTabIndex = subTab;
        h.Main.WorkspaceTabs.Add(WorkspaceTabViewModel.CreateViewDetail(h.Main, obj, detail, "A"));
        return h.Main.CaptureWorkspace();
    }

    private sealed class Harness : IDisposable
    {
        private readonly bool _ownsDir;

        // dir == null: private random temp dir, deleted on Dispose. dir != null: a
        // caller-owned dir shared across two sessions (for the disk round-trip) — the
        // caller cleans it up so session 1's Dispose doesn't wipe it before session 2.
        public Harness(string? dir = null)
        {
            _ownsDir = dir is null;
            TempDir = dir ?? Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
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
            if (!_ownsDir) return;
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
