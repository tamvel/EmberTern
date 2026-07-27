using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The two halves of the post-I8 metadata session:
/// <list type="number">
/// <item>an object the application itself created/dropped shows up (or goes away) in the tree WITHOUT a full
/// <c>RefreshAsync</c> — the Data Import "the new table is not in the tree" bug; and</item>
/// <item>the bulk guard the FILTER path always had is now on the LOAD path too, so replacing a category's
/// leaves costs one re-projection instead of one per leaf.</item>
/// </list>
/// Both are exercised through the REAL <see cref="SidebarFlatController"/> projection the sidebar binds to —
/// asserting on <c>Children</c> alone would pass even if nothing reached the screen, which is exactly the
/// shape of the bug being fixed.
/// </summary>
public class MetadataInPlaceUpdateTests
{
    // ── The tree actually shows what the application created ────────────────────────────────────────────

    [Fact]
    public void CreatedTable_AppearsInTheSidebar_WithoutARefresh()
    {
        using var h = new Harness("ARTYKULY", "KONTRAHENCI", "ZAMOWIENIA");

        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("IMPORT_NOWA", MetadataObjectKind.Table));

        Assert.Contains(h.Tables.AllLeaves, l => l.GroupLabel == "IMPORT_NOWA");
        // The user-visible half: it is in the FLAT projection the sidebar ListBox binds to.
        Assert.Contains(h.Metadata.SidebarRows,
            r => r.Node is MetadataNodeViewModel { GroupLabel: "IMPORT_NOWA" });
        Assert.Equal(4, h.Tables.Count);
        Assert.Equal("Tables (4)", h.Tables.DisplayLabel);
    }

    [Fact]
    public void CreatedTable_LandsAtItsSortedPosition()
    {
        using var h = new Harness("ARTYKULY", "KONTRAHENCI", "ZAMOWIENIA");

        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("BILANS", MetadataObjectKind.Table));

        Assert.Equal(
            new[] { "ARTYKULY", "BILANS", "KONTRAHENCI", "ZAMOWIENIA" },
            h.Tables.Children.Select(c => c.GroupLabel).ToArray());
    }

    [Fact]
    public void CreatedTable_ReportedTwice_IsAddedOnce()
    {
        using var h = new Harness("ARTYKULY");

        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("NOWA", MetadataObjectKind.Table));
        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("nowa", MetadataObjectKind.Table));

        Assert.Equal(2, h.Tables.AllLeaves.Count);
        Assert.Equal(2, h.Tables.Count);
    }

    [Fact]
    public void CreatedTable_DoesNotTouchAnotherKindsCategory()
    {
        using var h = new Harness("ARTYKULY");
        var views = h.GroupOf(MetadataObjectKind.View);
        var countBefore = views.Count;

        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("NOWA", MetadataObjectKind.Table));

        Assert.Equal(countBefore, views.Count);
        Assert.Empty(views.AllLeaves);
    }

    [Fact]
    public void DroppedTable_LeavesTheTreeTheSameWayItEntered()
    {
        using var h = new Harness("ARTYKULY", "KONTRAHENCI");
        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("NOWA", MetadataObjectKind.Table));

        h.Metadata.ApplyObjectRemovedInPlace(new MetadataObject("NOWA", MetadataObjectKind.Table));

        Assert.DoesNotContain(h.Tables.AllLeaves, l => l.GroupLabel == "NOWA");
        Assert.DoesNotContain(h.Metadata.SidebarRows,
            r => r.Node is MetadataNodeViewModel { GroupLabel: "NOWA" });
        Assert.Equal(2, h.Tables.Count);
    }

    [Fact]
    public void DroppedTable_ThatWasNeverThere_ChangesNothing()
    {
        using var h = new Harness("ARTYKULY");

        h.Metadata.ApplyObjectRemovedInPlace(new MetadataObject("GHOST", MetadataObjectKind.Table));

        Assert.Single(h.Tables.AllLeaves);
        Assert.Equal(1, h.Tables.Count);
    }

    [Fact]
    public void UnloadedCategory_OnlyItsCountMoves()
    {
        // A category the user never expanded shows a COUNT and no leaves. There is nothing to insert into,
        // but the number on screen must stay true until the first expand fetches the real list.
        using var h = new Harness();
        var indexes = h.GroupOf(MetadataObjectKind.Index);
        indexes.Count = 7;

        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("IDX_NEW", MetadataObjectKind.Index));
        Assert.Equal(8, indexes.Count);
        Assert.Empty(indexes.AllLeaves);

        h.Metadata.ApplyObjectRemovedInPlace(new MetadataObject("IDX_NEW", MetadataObjectKind.Index));
        Assert.Equal(7, indexes.Count);
    }

    [Fact]
    public void DisconnectedConnection_IsNotTouched()
    {
        using var h = new Harness("ARTYKULY");
        h.Node.IsConnected = false;

        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("NOWA", MetadataObjectKind.Table));

        Assert.DoesNotContain(h.Tables.AllLeaves, l => l.GroupLabel == "NOWA");
    }

    // ── The active filter keeps telling the truth ───────────────────────────────────────────────────────

    [Fact]
    public void WithAFilterActive_ANonMatchingNewTableIsHeldBackFromTheDisplay()
    {
        using var h = new Harness("KONTRAHENCI", "KONTAKT", "ARTYKULY");
        h.Metadata.FilterText = "KON";
        h.Metadata.ApplyFilterToGroup(h.Tables, hasFilter: true, "KON");
        Assert.Equal(2, h.Tables.FilterMatchCount);

        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("IMPORT_X", MetadataObjectKind.Table));

        // In the master list (the total is 4) but not on screen — the filter says KON.
        Assert.Equal(4, h.Tables.AllLeaves.Count);
        Assert.Equal(4, h.Tables.Count);
        Assert.DoesNotContain(h.Tables.Children, c => c.GroupLabel == "IMPORT_X");
        Assert.Equal(2, h.Tables.FilterMatchCount);
    }

    [Fact]
    public void WithAFilterActive_AMatchingNewTableShowsAndBumpsTheMatchCount()
    {
        using var h = new Harness("KONTRAHENCI", "ARTYKULY");
        h.Metadata.FilterText = "KON";
        h.Metadata.ApplyFilterToGroup(h.Tables, hasFilter: true, "KON");

        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("KONTO", MetadataObjectKind.Table));

        Assert.Contains(h.Tables.Children, c => c.GroupLabel == "KONTO");
        Assert.Equal(2, h.Tables.FilterMatchCount);
        Assert.Equal("Tables (2)", h.Tables.DisplayLabel);   // label shows MATCHES while filtering
        Assert.True(h.Tables.IsVisible);
    }

    [Fact]
    public void CreatedTable_IsAnnouncedToTheEditors()
    {
        // Open editors rebuild their semantic model off this signal — without it the new table would not
        // resolve for highlighting / Ctrl-nav until something else bumped the generation.
        using var h = new Harness("ARTYKULY");
        var before = h.Metadata.ObjectsGeneration;
        var raised = 0;
        h.Metadata.ObjectsChanged += () => raised++;

        h.Metadata.ApplyObjectAddedInPlace(new MetadataObject("NOWA", MetadataObjectKind.Table));

        Assert.Equal(1, raised);
        Assert.True(h.Metadata.ObjectsGeneration > before);
    }

    // ── Layer 1: the bulk guard on the load path ────────────────────────────────────────────────────────

    [Fact]
    public void ReplacingLeavesUnderTheBulkGuard_ReProjectsOnce_NotOncePerLeaf()
    {
        // The measured defect: SetLeaves is Clear + one Add per object, and every Add re-splices the whole
        // child block while the category is expanded — Θ(N²) row operations on the UI thread. Under the
        // guard the projection is suspended and rebuilt once, so the work becomes linear. Counting the
        // sidebar's own collection notifications is the discriminator: quadratic vs linear in N.
        const int N = 200;
        using var h = new Harness();
        var leaves = Enumerable.Range(0, N)
            .Select(i => MetadataNodeViewModel.CreateLeaf(
                h.Metadata, new MetadataObject($"T_{i:D4}", MetadataObjectKind.Table)))
            .ToList();

        var unguarded = h.CountSidebarNotifications(() => h.Tables.SetLeaves(leaves));
        Assert.True(unguarded > N * 10,
            $"expected the unguarded replace to be quadratic; saw only {unguarded} notifications for {N} leaves");

        var guarded = h.CountSidebarNotifications(() =>
        {
            h.Metadata.BeginSidebarBulkUpdate();
            try { h.Tables.SetLeaves(leaves); }
            finally { h.Metadata.EndSidebarBulkUpdate(); }
        });

        Assert.True(guarded < N * 4,
            $"expected the guarded replace to be linear; saw {guarded} notifications for {N} leaves");
        // And the projection is CORRECT afterwards — a suspended projection that forgot to catch up would
        // be a far worse bug than the one being fixed.
        Assert.Equal(N, h.Metadata.SidebarRows.Count(
            r => r.Node is MetadataNodeViewModel { IsGroup: false } m && m.GroupLabel.StartsWith("T_", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheBulkGuardNests()
    {
        // RefreshAsync wraps the whole 13-category loop and each LoadGroupAsync wraps itself; the inner
        // EndUpdate must NOT re-project early or the outer loop pays per category again.
        using var h = new Harness();
        var leaf = MetadataNodeViewModel.CreateLeaf(
            h.Metadata, new MetadataObject("SOLO", MetadataObjectKind.Table));

        var notifications = h.CountSidebarNotifications(() =>
        {
            h.Metadata.BeginSidebarBulkUpdate();
            h.Metadata.BeginSidebarBulkUpdate();
            h.Tables.SetLeaves(new[] { leaf });
            h.Metadata.EndSidebarBulkUpdate();          // inner: still suspended
            var afterInner = h.Metadata.SidebarRows.Count(
                r => r.Node is MetadataNodeViewModel { GroupLabel: "SOLO" });
            Assert.Equal(0, afterInner);
            h.Metadata.EndSidebarBulkUpdate();          // outer: one re-projection
        });

        Assert.Contains(h.Metadata.SidebarRows, r => r.Node is MetadataNodeViewModel { GroupLabel: "SOLO" });
        Assert.True(notifications > 0);
    }

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A connected connection node with its 13 categories, projected through the real sidebar
    /// controller, with the Table category loaded and expanded — the state the bug was reported in.</summary>
    private sealed class Harness : IDisposable
    {
        public Harness(params string[] tables)
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Store = new ConnectionProfileStore(TempDir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(Store, Service);

            var profile = new ConnectionProfile { Name = "LAB", Host = "localhost", Port = 3050 };
            Node = new ConnectionNodeViewModel(profile, Main);
            Metadata.Connections.Add(Node);
            Metadata.RootNodes.Add(Node);

            // Building the categories is what connecting does; the loads inside it no-op because the
            // service is not actually connected, which is precisely what makes this testable without a DB.
            Node.IsConnected = true;
            Node.IsExpanded = true;

            Tables = GroupOf(MetadataObjectKind.Table);
            if (tables.Length > 0)
            {
                Tables.SetLeaves(tables.Select(name =>
                    MetadataNodeViewModel.CreateLeaf(Metadata, new MetadataObject(name, MetadataObjectKind.Table))));
                Tables.MarkLoaded();
            }
            Tables.Count = tables.Length;
            Tables.IsExpanded = true;
        }

        public string TempDir { get; }
        public ConnectionProfileStore Store { get; }
        public FirebirdConnectionService Service { get; }
        public MainWindowViewModel Main { get; }
        public MetadataExplorerViewModel Metadata => Main.Metadata;
        public ConnectionNodeViewModel Node { get; }
        public MetadataNodeViewModel Tables { get; }

        public MetadataNodeViewModel GroupOf(MetadataObjectKind kind)
            => Node.Children.First(c => c.IsGroup && c.Kind == kind);

        /// <summary>Runs an action and returns how many changes the flat projection published — the direct
        /// measure of "did this mutation storm the ListBox".</summary>
        public int CountSidebarNotifications(Action action)
        {
            var count = 0;
            NotifyCollectionChangedEventHandler handler = (_, _) => count++;
            Metadata.SidebarRows.CollectionChanged += handler;
            try { action(); }
            finally { Metadata.SidebarRows.CollectionChanged -= handler; }
            return count;
        }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
