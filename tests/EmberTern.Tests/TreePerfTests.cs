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
/// Object Explorer performance sprint — lazy COUNT-only load, IBExpert-style filter
/// (per-category match counts, hide zeros, no auto-expand), and incremental type-ahead
/// over the full metadata (session name cache).
/// </summary>
public class TreePerfTests
{
    // ─── Type-ahead: FindFirstMatch (pure, incremental-from-start) ────────

    private static MetadataExplorerViewModel.TypeAheadEntry T(string text)
        => new(text, Array.Empty<object>(), null, null, null);

    private static IReadOnlyList<MetadataExplorerViewModel.TypeAheadEntry> Sample()
        => new[] { "ARTYKULY", "DOSTAWCY", "KONTRAHENCI", "KRAJE", "KONTAKT", "ZAMOWIENIA" }
            .Select(T).ToArray();

    [Fact]
    public void FindFirstMatch_EmptyBufferOrList_ReturnsMinusOne()
    {
        Assert.Equal(-1, MetadataExplorerViewModel.FindFirstMatch(Sample(), ""));
        Assert.Equal(-1, MetadataExplorerViewModel.FindFirstMatch(
            Array.Empty<MetadataExplorerViewModel.TypeAheadEntry>(), "A"));
    }

    [Fact]
    public void FindFirstMatch_FindsFirstInTreeOrder()
    {
        // "K" → first K-item in order = KONTRAHENCI (index 2), not KONTAKT/KRAJE.
        Assert.Equal(2, MetadataExplorerViewModel.FindFirstMatch(Sample(), "K"));
    }

    [Fact]
    public void FindFirstMatch_IncrementalBufferConvergesToOneItem()
    {
        // The whole point of #2: a growing buffer keeps refining toward KONTRAHENCI
        // (never an independent per-char jump). "KONTR" skips KONTAKT.
        Assert.Equal(2, MetadataExplorerViewModel.FindFirstMatch(Sample(), "K"));
        Assert.Equal(2, MetadataExplorerViewModel.FindFirstMatch(Sample(), "KO"));
        Assert.Equal(2, MetadataExplorerViewModel.FindFirstMatch(Sample(), "KON"));
        Assert.Equal(2, MetadataExplorerViewModel.FindFirstMatch(Sample(), "KONT"));
        Assert.Equal(2, MetadataExplorerViewModel.FindFirstMatch(Sample(), "KONTR")); // KONTAKT no longer matches
    }

    [Fact]
    public void FindFirstMatch_IsCaseInsensitive()
    {
        Assert.Equal(2, MetadataExplorerViewModel.FindFirstMatch(Sample(), "kontr"));
    }

    [Fact]
    public void FindFirstMatch_NoMatch_ReturnsMinusOne()
    {
        Assert.Equal(-1, MetadataExplorerViewModel.FindFirstMatch(Sample(), "ZZ"));
    }

    // ─── Pure match counting (filter) ─────────────────────────────────────

    [Fact]
    public void CountMatches_CountsCaseInsensitiveSubstrings()
    {
        var names = new[] { "WIDOK_A", "WIDOK_B", "ARTYKULY", "OWIDIUSZ" };
        Assert.Equal(3, MetadataExplorerViewModel.CountMatches(names, "wid")); // WIDOK_A, WIDOK_B, OWIDIUSZ
        Assert.Equal(2, MetadataExplorerViewModel.CountMatches(names, "WIDOK"));
        Assert.Equal(0, MetadataExplorerViewModel.CountMatches(names, "zzz"));
        Assert.Equal(0, MetadataExplorerViewModel.CountMatches(Array.Empty<string>(), "x"));
    }

    // ─── NodeSearchText ───────────────────────────────────────────────────

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
    public async Task BuildFullTypeAheadIndex_IncludesConnectionRows()
    {
        using var h = new Harness();
        h.Store.Upsert(new ConnectionProfile { Name = "Alpha", Host = "x", Port = 3050 });
        h.Store.Upsert(new ConnectionProfile { Name = "Beta", Host = "x", Port = 3050 });
        h.Main.ReloadConnections();

        var index = await h.Main.Metadata.BuildFullTypeAheadIndexAsync();
        // Disconnected → no categories/leaves, only the two connection rows.
        Assert.Contains(index, e => e.Text == "Alpha");
        Assert.Contains(index, e => e.Text == "Beta");
        Assert.Equal(2, index.Count);
    }

    // ─── IBExpert-style filter: ApplyFilterToGroup ────────────────────────

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
    public void ApplyFilterToGroup_LoadedGroup_ShowsMatchCount_HidesNonMatches_NoAutoExpand()
    {
        using var h = new Harness();
        var group = LoadedGroup(h.Main.Metadata, "KONTRAHENCI", "KONTAKT", "ARTYKULY");

        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: true, "KON");

        Assert.True(group.IsVisible);
        Assert.Equal(2, group.FilterMatchCount);                 // KONTRAHENCI + KONTAKT
        Assert.Equal("Tables (2)", group.DisplayLabel);          // label shows MATCH count
        Assert.False(group.IsExpanded);                          // #4: filter never auto-expands
        Assert.True(group.Children.Single(c => c.GroupLabel == "KONTRAHENCI").IsVisible);
        Assert.False(group.Children.Single(c => c.GroupLabel == "ARTYKULY").IsVisible);
    }

    [Fact]
    public void ApplyFilterToGroup_LoadedGroup_NoMatch_HidesGroup()
    {
        using var h = new Harness();
        var group = LoadedGroup(h.Main.Metadata, "KONTRAHENCI", "ARTYKULY");

        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: true, "ZZZ");

        Assert.False(group.IsVisible);
        Assert.Equal(0, group.FilterMatchCount);
    }

    [Fact]
    public void ApplyFilterToGroup_ClearFilter_RestoresVisibilityAndLabel()
    {
        using var h = new Harness();
        var group = LoadedGroup(h.Main.Metadata, "KONTRAHENCI", "ARTYKULY");
        group.Count = 2;

        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: true, "KON");
        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: false, "");

        Assert.Null(group.FilterMatchCount);
        Assert.True(group.IsVisible);
        Assert.Equal("Tables (2)", group.DisplayLabel);          // back to TOTAL count
        Assert.All(group.Children, c => Assert.True(c.IsVisible));
    }

    [Fact]
    public void ApplyFilterToGroup_UnloadedGroup_NeverAutoExpands()
    {
        // The "no whole-tree auto-expand" guarantee: a count-only category is never
        // force-expanded by filtering. (Without a name cache its match count is 0 → it
        // hides; the real per-category count comes from the cache in ApplyFilterAsync.)
        using var h = new Harness();
        var group = MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Table);
        Assert.False(group.IsLoaded);

        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: true, "KON");

        Assert.False(group.IsExpanded);
    }

    // ─── Lazy count-load ──────────────────────────────────────────────────

    [Fact]
    public async Task LoadCountAsync_WithoutConnection_IsNoOp()
    {
        using var h = new Harness();
        var group = MetadataNodeViewModel.CreateGroup(h.Main.Metadata, MetadataObjectKind.Table);

        await h.Main.Metadata.LoadCountAsync(group);

        Assert.Null(group.Count);
        Assert.False(group.IsLoaded);
    }

    [Fact]
    public void InvalidateNameCache_DoesNotThrow()
    {
        using var h = new Harness();
        h.Main.Metadata.InvalidateNameCache(); // idempotent, safe before any build
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
