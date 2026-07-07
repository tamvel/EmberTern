using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The "Selected" trigger bulk scope + multi-select plumbing: the pure scope/target resolver
/// (All / Visible / Selected, skip-already-in-state), the sidebar-selection → trigger extraction,
/// and the explorer VM's Selected commands + count gating. Pure logic — no live DB / no ListBox.
/// </summary>
public class TriggerBulkSelectionTests
{
    private static MetadataObject Trig(string name, bool active) =>
        new(name, MetadataObjectKind.Trigger) { IsActive = active };

    // ─── ResolveTriggerBulkTargets (pure scope + skip-already-in-state) ───────────────────

    [Fact]
    public void Resolve_All_TargetsEveryTriggerNeedingTheChange()
    {
        var all = new[] { Trig("A", true), Trig("B", false), Trig("C", false) };
        // Activate: A is already active → skipped; B, C flip on.
        var targets = MainWindowViewModel.ResolveTriggerBulkTargets(all, BatchOperationScope.All, Array.Empty<string>(), activate: true);
        Assert.Equal(new[] { "B", "C" }, targets.Select(t => t.Name));
    }

    [Fact]
    public void Resolve_Visible_FiltersByNames()
    {
        var all = new[] { Trig("A", true), Trig("B", true), Trig("C", true) };
        var targets = MainWindowViewModel.ResolveTriggerBulkTargets(
            all, BatchOperationScope.Visible, new[] { "A", "C" }, activate: false);
        Assert.Equal(new[] { "A", "C" }, targets.Select(t => t.Name)); // B not in the visible set
    }

    [Fact]
    public void Resolve_Selected_FiltersByNames_CaseInsensitive()
    {
        var all = new[] { Trig("TR_A", true), Trig("TR_B", true) };
        var targets = MainWindowViewModel.ResolveTriggerBulkTargets(
            all, BatchOperationScope.Selected, new[] { "tr_a" }, activate: false);
        Assert.Equal(new[] { "TR_A" }, targets.Select(t => t.Name));
    }

    [Fact]
    public void Resolve_Selected_EmptySelection_TargetsNothing()
    {
        var all = new[] { Trig("A", true), Trig("B", true) };
        Assert.Empty(MainWindowViewModel.ResolveTriggerBulkTargets(all, BatchOperationScope.Selected, Array.Empty<string>(), activate: false));
    }

    [Fact]
    public void Resolve_Selected_SkipsAlreadyInState()
    {
        var all = new[] { Trig("A", true), Trig("B", false) };
        // Deactivate the selected {A, B}: A is active → flips; B already inactive → skipped.
        var targets = MainWindowViewModel.ResolveTriggerBulkTargets(all, BatchOperationScope.Selected, new[] { "A", "B" }, activate: false);
        Assert.Equal(new[] { "A" }, targets.Select(t => t.Name));
    }

    [Fact]
    public void Resolve_Selected_SingleTrigger()
    {
        var all = new[] { Trig("A", false), Trig("B", false) };
        var targets = MainWindowViewModel.ResolveTriggerBulkTargets(all, BatchOperationScope.Selected, new[] { "B" }, activate: true);
        Assert.Equal(new[] { "B" }, targets.Select(t => t.Name));
    }

    [Fact]
    public void Resolve_AllAndVisible_Unaffected_BySelectedScopeAddition()
    {
        // Regression pin: All ignores names; Visible filters by names — unchanged behaviour.
        var all = new[] { Trig("A", false), Trig("B", false), Trig("C", false) };
        Assert.Equal(3, MainWindowViewModel.ResolveTriggerBulkTargets(all, BatchOperationScope.All, new[] { "A" }, activate: true).Count);
        Assert.Equal(new[] { "A", "B" },
            MainWindowViewModel.ResolveTriggerBulkTargets(all, BatchOperationScope.Visible, new[] { "A", "B" }, activate: true).Select(t => t.Name));
    }

    // ─── ExtractSelectedTriggers (sidebar rows → trigger objects) ─────────────────────────

    [Fact]
    public void Extract_Empty_ReturnsEmpty()
        => Assert.Empty(MetadataExplorerViewModel.ExtractSelectedTriggers(Array.Empty<SidebarRow>()));

    [Fact]
    public void Extract_KeepsOnlyTriggerLeaves_IgnoresGroupsAndOtherKindsAndNonNodes()
    {
        using var h = new Harness();
        var m = h.Main.Metadata;
        var rows = new[]
        {
            Row(MetadataNodeViewModel.CreateGroup(m, MetadataObjectKind.Trigger)),        // group — not a leaf
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_A", true))),                 // trigger leaf ✓
            Row(MetadataNodeViewModel.CreateLeaf(m, new MetadataObject("SP_X", MetadataObjectKind.Procedure))), // wrong kind
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_B", false))),                // trigger leaf ✓
            new SidebarRow("not-a-node", 0, false, false),                               // non-node object
        };

        var triggers = MetadataExplorerViewModel.ExtractSelectedTriggers(rows);
        Assert.Equal(new[] { "TR_A", "TR_B" }, triggers.Select(t => t.Name)); // order preserved
    }

    // ─── SetSelectedTriggers + commands (count gating + request shape) ────────────────────

    [Fact]
    public void SetSelected_DrivesCountAndCommandGating()
    {
        using var h = new Harness();
        var m = h.Main.Metadata;

        Assert.False(m.HasSelectedTriggers);
        Assert.Equal(0, m.SelectedTriggerCount);
        Assert.False(m.ActivateSelectedTriggersCommand.CanExecute(null));
        Assert.False(m.DeactivateSelectedTriggersCommand.CanExecute(null));

        m.SetSelectedTriggers(new[]
        {
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_A", true))),
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_B", true))),
            Row(MetadataNodeViewModel.CreateLeaf(m, new MetadataObject("SP_X", MetadataObjectKind.Procedure))), // ignored
        });

        Assert.True(m.HasSelectedTriggers);
        Assert.Equal(2, m.SelectedTriggerCount); // only the two trigger leaves
        Assert.True(m.ActivateSelectedTriggersCommand.CanExecute(null));
        Assert.True(m.DeactivateSelectedTriggersCommand.CanExecute(null));

        m.SetSelectedTriggers(Array.Empty<SidebarRow>());
        Assert.False(m.HasSelectedTriggers);
        Assert.False(m.ActivateSelectedTriggersCommand.CanExecute(null));
    }

    [Fact]
    public void DeactivateSelectedCommand_RaisesRequest_WithSelectedScopeAndNames()
    {
        using var h = new Harness();
        var m = h.Main.Metadata;
        TriggerBulkRequest? req = null;
        m.BulkSetActiveRequested += r => req = r;

        m.SetSelectedTriggers(new[]
        {
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_A", true))),
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_B", true))),
        });
        m.DeactivateSelectedTriggersCommand.Execute(null);

        Assert.NotNull(req);
        Assert.Equal(BatchOperationScope.Selected, req!.Scope);
        Assert.False(req.Activate);
        Assert.Equal(MetadataObjectKind.Trigger, req.Kind);
        Assert.Equal(new[] { "TR_A", "TR_B" }, req.Names);
    }

    private static SidebarRow Row(MetadataNodeViewModel node) => new(node, depth: 2, isExpandable: false, isExpanded: false);

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
