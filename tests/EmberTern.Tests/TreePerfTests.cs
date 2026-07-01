using System;
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
/// Object Explorer performance sprint — lazy COUNT-only load and the IBExpert-style filter
/// (per-category match counts, hide zeros, no auto-expand) backed by the session name cache.
/// (Type-ahead was replaced by type-to-filter, which is View-side code-behind.)
/// </summary>
public class TreePerfTests
{
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

    // ─── IBExpert-style filter: ApplyFilterToGroup ────────────────────────

    private static MetadataNodeViewModel LoadedGroup(MetadataExplorerViewModel meta, params string[] leaves)
    {
        var group = MetadataNodeViewModel.CreateGroup(meta, MetadataObjectKind.Table);
        // SetLeaves populates the unfiltered master list AND Children (root-cause fix:
        // the filter rebuilds Children to matches only, rather than hiding leaves in place).
        group.SetLeaves(leaves.Select(name =>
            MetadataNodeViewModel.CreateLeaf(meta, new MetadataObject(name, MetadataObjectKind.Table))));
        group.MarkLoaded();
        return group;
    }

    [Fact]
    public void ApplyFilterToGroup_LoadedGroup_ShowsOnlyMatches_NoAutoExpand()
    {
        using var h = new Harness();
        var group = LoadedGroup(h.Main.Metadata, "KONTRAHENCI", "KONTAKT", "ARTYKULY");

        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: true, "KON");

        Assert.True(group.IsVisible);
        Assert.Equal(2, group.FilterMatchCount);                 // KONTRAHENCI + KONTAKT
        Assert.Equal("Tables (2)", group.DisplayLabel);          // label shows MATCH count
        Assert.False(group.IsExpanded);                          // #4: filter never auto-expands
        // Children holds ONLY the matches — non-matches are removed, not hidden in place.
        Assert.Equal(2, group.Children.Count);
        Assert.Contains(group.Children, c => c.GroupLabel == "KONTRAHENCI");
        Assert.DoesNotContain(group.Children, c => c.GroupLabel == "ARTYKULY");
        // The full set is preserved for autocomplete / bulk-"all" / clear.
        Assert.Equal(3, group.AllLeaves.Count);
    }

    [Fact]
    public void ApplyFilterToGroup_LoadedGroup_NoMatch_HidesGroup()
    {
        using var h = new Harness();
        var group = LoadedGroup(h.Main.Metadata, "KONTRAHENCI", "ARTYKULY");

        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: true, "ZZZ");

        Assert.False(group.IsVisible);
        Assert.Equal(0, group.FilterMatchCount);
        Assert.Empty(group.Children);
    }

    [Fact]
    public void ApplyFilterToGroup_ClearFilter_RestoresFullSetAndLabel()
    {
        using var h = new Harness();
        var group = LoadedGroup(h.Main.Metadata, "KONTRAHENCI", "ARTYKULY");
        group.Count = 2;

        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: true, "KON");
        h.Main.Metadata.ApplyFilterToGroup(group, hasFilter: false, "");

        Assert.Null(group.FilterMatchCount);
        Assert.True(group.IsVisible);
        Assert.Equal("Tables (2)", group.DisplayLabel);          // back to TOTAL count
        Assert.Equal(2, group.Children.Count);                   // full set restored
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
