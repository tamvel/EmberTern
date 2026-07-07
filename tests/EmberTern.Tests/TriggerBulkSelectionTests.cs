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

    [Fact]
    public void TriggerLeaf_ForwardsSelectedCommand_IsOwnerInstance_AndGates()
    {
        // Regression pin for gotcha #180, now on the LEAF: the "Selected" ContextMenu items moved
        // onto the trigger leaves, and they bind the forwarded owner command by DataContext
        // inheritance (an ElementName binding can't cross the ContextMenu's popup namescope → the
        // command resolved to null → clicking did nothing). The leaf must expose the SAME command
        // instance so its HasSelectedTriggers gating works.
        using var h = new Harness();
        var m = h.Main.Metadata;
        var leaf = MetadataNodeViewModel.CreateLeaf(m, Trig("TR_ONE", false));

        Assert.Same(m.ActivateSelectedTriggersCommand, leaf.ActivateSelectedTriggersCommand);
        Assert.Same(m.DeactivateSelectedTriggersCommand, leaf.DeactivateSelectedTriggersCommand);
        Assert.False(leaf.ActivateSelectedTriggersCommand.CanExecute(null)); // nothing selected

        m.SetSelectedTriggers(new[] { Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_ONE", false))) });
        Assert.True(leaf.ActivateSelectedTriggersCommand.CanExecute(null));
    }

    [Fact]
    public void TriggerLeaf_ForwardsSelectedCommand_MultiTrigger_NamesAll()
    {
        // Regression pin for Problem B ("Deactivate acts on only one"): the leaf's forwarded "Selected"
        // command must carry EVERY selected trigger, not just one. The live bug was the view collapsing
        // the multi-selection on right-click (Problem A) so _selectedTriggers held 0 or 1 by the time
        // the command ran; with the selection preserved, all three are named.
        using var h = new Harness();
        var m = h.Main.Metadata;
        TriggerBulkRequest? req = null;
        m.BulkSetActiveRequested += r => req = r;

        var leaf = MetadataNodeViewModel.CreateLeaf(m, Trig("TR_TARGET", true));
        m.SetSelectedTriggers(new[]
        {
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_A", true))),
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_B", true))),
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_C", true))),
        });

        Assert.True(leaf.DeactivateSelectedTriggersCommand.CanExecute(null));
        leaf.DeactivateSelectedTriggersCommand.Execute(null);

        Assert.NotNull(req);
        Assert.Equal(BatchOperationScope.Selected, req!.Scope);
        Assert.Equal(new[] { "TR_A", "TR_B", "TR_C" }, req.Names);
    }

    [Fact]
    public void TriggerLeaf_MultiSelection_ShowsSelectedOpsWithCount_AndHidesSingleOps()
    {
        // The "Selected" bulk op moved onto the selected trigger leaves: with >1 trigger selected,
        // any trigger leaf's context menu offers "Activate/Deactivate selected (N)" and hides the
        // single-object ops (so it's reachable without scrolling back to the Triggers group header).
        using var h = new Harness();
        var m = h.Main.Metadata;
        var leaf = MetadataNodeViewModel.CreateLeaf(m, Trig("TR_TARGET", true)); // active trigger leaf

        // No multi-selection → single ops visible, Selected ops hidden.
        Assert.False(leaf.ShowSelectedTriggerOps);
        Assert.True(leaf.ShowDeactivate); // active → single Deactivate shown
        Assert.True(leaf.CanEditLeaf);
        Assert.True(leaf.CanDeleteLeaf);
        Assert.True(leaf.ShowCopyNameLeaf);

        // >1 trigger selected → Selected ops (with the count) visible, single ops hidden.
        m.SetSelectedTriggers(new[]
        {
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_A", true))),
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_B", true))),
            Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_C", true))),
        });
        Assert.True(leaf.ShowSelectedTriggerOps);
        Assert.Contains("3", leaf.ActivateSelectedTriggersLabel);
        Assert.Contains("3", leaf.DeactivateSelectedTriggersLabel);
        Assert.False(leaf.ShowActivate);
        Assert.False(leaf.ShowDeactivate);
        Assert.False(leaf.CanEditLeaf);
        Assert.False(leaf.CanDeleteLeaf);
        Assert.False(leaf.ShowCopyNameLeaf);

        // Back to a single selection → single ops return, Selected ops hidden.
        m.SetSelectedTriggers(new[] { Row(MetadataNodeViewModel.CreateLeaf(m, Trig("TR_A", true))) });
        Assert.False(leaf.ShowSelectedTriggerOps);
        Assert.True(leaf.ShowDeactivate);
        Assert.True(leaf.CanEditLeaf);
    }

    [Fact]
    public void SetActiveState_FlipsLeafInPlace_UpdatingInactiveDisplayAndSingleOps()
    {
        // The targeted in-place flip that replaces RefreshAsync after a single/batch activate-deactivate
        // (no collection change → the tree doesn't reproject → scroll/selection survive). Only the
        // IsActive-derived display changes; the object identity/name stay.
        using var h = new Harness();
        var m = h.Main.Metadata;
        var leaf = MetadataNodeViewModel.CreateLeaf(m, Trig("TR_X", true)); // active

        Assert.False(leaf.IsInactive);
        Assert.True(leaf.ShowDeactivate);
        Assert.DoesNotContain("(inactive)", leaf.DisplayLabel);

        leaf.SetActiveState(false);
        Assert.True(leaf.IsInactive);
        Assert.Contains("(inactive)", leaf.DisplayLabel);
        Assert.True(leaf.ShowActivate);
        Assert.False(leaf.ShowDeactivate);
        Assert.Equal("TR_X", leaf.Object!.Name); // identity preserved

        leaf.SetActiveState(false); // idempotent no-op
        Assert.True(leaf.IsInactive);

        leaf.SetActiveState(true); // back to active
        Assert.False(leaf.IsInactive);
        Assert.True(leaf.ShowDeactivate);
    }

    [Fact]
    public void BatchResults_SuccessfulObjects_ReturnsOnlyNonFailedNames()
    {
        // The batch trigger flip relies on SuccessfulObjects to reflect ONLY the actually-changed
        // triggers in the tree (regression: the batch flip was gated on an always-true Cancellation
        // token → nothing updated). This pins the source list.
        var b = new BatchResultsViewModel("t");
        b.Begin(3);
        b.AddResult(new BatchOperationResult("TR_A", "op", Success: true, Error: null));
        b.AddResult(new BatchOperationResult("TR_B", "op", Success: false, Error: "boom"));
        b.AddResult(new BatchOperationResult("TR_C", "op", Success: true, Error: null));

        Assert.Equal(new[] { "TR_A", "TR_C" }, b.SuccessfulObjects);
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
