using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Object Explorer performance sprint — Etap 1: lazy COUNT-only load, local filter
/// (no whole-tree auto-expand), and IBExpert-style type-ahead navigation.
/// </summary>
public class TreePerfTests
{
    // ─── Type-ahead: FindTypeAheadIndex (pure) ────────────────────────────

    private static MetadataExplorerViewModel.TypeAheadEntry E(string text)
        => new(text, Array.Empty<object>(), text);

    private static IReadOnlyList<MetadataExplorerViewModel.TypeAheadEntry> Sample()
        => new[] { "ARTYKULY", "DOSTAWCY", "KONTRAHENCI", "KRAJE", "ZAMOWIENIA" }
            .Select(E).ToArray();

    [Fact]
    public void FindTypeAhead_EmptyBufferOrList_ReturnsMinusOne()
    {
        Assert.Equal(-1, MetadataExplorerViewModel.FindTypeAheadIndex(Sample(), 0, inclusive: false, ""));
        Assert.Equal(-1, MetadataExplorerViewModel.FindTypeAheadIndex(
            Array.Empty<MetadataExplorerViewModel.TypeAheadEntry>(), -1, inclusive: false, "A"));
    }

    [Fact]
    public void FindTypeAhead_FreshLetter_SearchesForwardExclusive()
    {
        // On ARTYKULY (0), a fresh "K" must advance to the next K-item (KONTRAHENCI=2).
        var i = MetadataExplorerViewModel.FindTypeAheadIndex(Sample(), 0, inclusive: false, "K");
        Assert.Equal(2, i);
    }

    [Fact]
    public void FindTypeAhead_RefiningBuffer_KeepsCurrentMatch()
    {
        // On KONTRAHENCI (2), refining to "KO" stays put (inclusive).
        var i = MetadataExplorerViewModel.FindTypeAheadIndex(Sample(), 2, inclusive: true, "KO");
        Assert.Equal(2, i);
    }

    [Fact]
    public void FindTypeAhead_SameLetterAgain_CyclesToNextMatch()
    {
        // On KONTRAHENCI (2), a fresh "K" again cycles to the next K-item (KRAJE=3).
        var i = MetadataExplorerViewModel.FindTypeAheadIndex(Sample(), 2, inclusive: false, "K");
        Assert.Equal(3, i);
    }

    [Fact]
    public void FindTypeAhead_WrapsAround()
    {
        // On KRAJE (3), "A" finds nothing forward (ZAMOWIENIA) → wraps to ARTYKULY (0).
        var i = MetadataExplorerViewModel.FindTypeAheadIndex(Sample(), 3, inclusive: false, "A");
        Assert.Equal(0, i);
    }

    [Fact]
    public void FindTypeAhead_IsCaseInsensitive()
    {
        var i = MetadataExplorerViewModel.FindTypeAheadIndex(Sample(), -1, inclusive: false, "kon");
        Assert.Equal(2, i);
    }

    [Fact]
    public void FindTypeAhead_NoMatch_ReturnsMinusOne()
    {
        Assert.Equal(-1, MetadataExplorerViewModel.FindTypeAheadIndex(Sample(), 0, inclusive: false, "ZZ"));
    }

    // ─── Type-ahead: NodeSearchText + BuildTypeAheadIndex ─────────────────

    [Fact]
    public void NodeSearchText_PerKind()
    {
        using var h = new Harness();
        var meta = h.Main.Metadata;

        var conn = new ConnectionNodeViewModel(new ConnectionProfile { Name = "ERP", Host = "x", Port = 3050 });
        Assert.Equal("ERP", MetadataExplorerViewModel.NodeSearchText(conn));

        var leaf = MetadataNodeViewModel.CreateLeaf(meta, new MetadataObject("KONTRAHENCI", MetadataObjectKind.Table));
        Assert.Equal("KONTRAHENCI", MetadataExplorerViewModel.NodeSearchText(leaf));

        var group = MetadataNodeViewModel.CreateGroup(meta, MetadataObjectKind.Table);
        Assert.Equal("Tables", MetadataExplorerViewModel.NodeSearchText(group));

        var placeholder = MetadataNodeViewModel.CreatePlaceholder(meta);
        Assert.Equal(string.Empty, MetadataExplorerViewModel.NodeSearchText(placeholder));
    }

    [Fact]
    public void BuildTypeAheadIndex_IncludesConnectionRows()
    {
        using var h = new Harness();
        h.Store.Upsert(new ConnectionProfile { Name = "Alpha", Host = "x", Port = 3050 });
        h.Store.Upsert(new ConnectionProfile { Name = "Beta", Host = "x", Port = 3050 });
        h.Main.ReloadConnections();

        var index = h.Main.Metadata.BuildTypeAheadIndex();
        Assert.Contains(index, e => e.Text == "Alpha");
        Assert.Contains(index, e => e.Text == "Beta");
        // Disconnected → no category/leaf rows, only the two connection nodes.
        Assert.Equal(2, index.Count);
    }

    // ─── Local filter: ApplyFilterToGroup (no whole-tree expand) ──────────

    private static MetadataNodeViewModel LoadedGroup(MetadataExplorerViewModel meta, params string[] leaves)
    {
        var group = MetadataNodeViewModel.CreateGroup(meta, MetadataObjectKind.Table);
        group.Children.Clear();
        foreach (var name in leaves)
        {
            group.Children.Add(MetadataNodeViewModel.CreateLeaf(meta, new MetadataObject(name, MetadataObjectKind.Table)));
        }
        group.MarkLoaded();
        return group;
    }

    [Fact]
    public void ApplyFilterToGroup_LoadedGroup_HidesNonMatchesAndExpandsOnMatch()
    {
        using var h = new Harness();
        var group = LoadedGroup(h.Main.Metadata, "KONTRAHENCI", "ARTYKULY");

        MetadataExplorerViewModel.ApplyFilterToGroup(group, hasFilter: true, "KON");

        Assert.True(group.IsVisible);
        Assert.True(group.IsExpanded); // a LOADED group with matches auto-expands to reveal them
        Assert.True(group.Children.Single(c => c.GroupLabel == "KONTRAHENCI").IsVisible);
        Assert.False(group.Children.Single(c => c.GroupLabel == "ARTYKULY").IsVisible);
    }

    [Fact]
    public void ApplyFilterToGroup_LoadedGroup_NoMatch_HidesGroup()
    {
        using var h = new Harness();
        var group = LoadedGroup(h.Main.Metadata, "KONTRAHENCI", "ARTYKULY");

        MetadataExplorerViewModel.ApplyFilterToGroup(group, hasFilter: true, "ZZZ");

        Assert.False(group.IsVisible);
    }

    [Fact]
    public void ApplyFilterToGroup_UnloadedGroup_StaysVisibleAndNeverAutoExpands()
    {
        // The "no whole-tree auto-expand" guarantee: an un-expanded (count-only) category
        // is never force-loaded/expanded by filtering — it stays visible so the user can
        // open it to load+filter, but IsExpanded MUST remain false.
        using var h = new Harness();
        var group = MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Table); // placeholder only, not loaded
        Assert.False(group.IsLoaded);

        MetadataExplorerViewModel.ApplyFilterToGroup(group, hasFilter: true, "KON");

        Assert.True(group.IsVisible);
        Assert.False(group.IsExpanded);
    }

    // ─── Lazy count-load ──────────────────────────────────────────────────

    [Fact]
    public async Task LoadCountAsync_WithoutConnection_IsNoOp()
    {
        using var h = new Harness();
        var group = MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Table);

        await h.Main.Metadata.LoadCountAsync(group);

        // No connection → no fetch, count stays unset, group stays unloaded & expandable.
        Assert.Null(group.Count);
        Assert.False(group.IsLoaded);
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
