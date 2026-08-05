using System;
using System.IO;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stabilization sprint S-2 (2026-08-05), second half: a manual metadata refresh must DROP the per-object
/// caches every open editor's semantic model is built from.
///
/// <para>⭐⭐ What was wrong. <c>MetadataExplorerViewModel.RefreshAsync</c> dropped the object-NAME index
/// (<c>InvalidateNameCache</c>) and nothing else. The column / routine-parameter / object-detail caches were
/// cleared only when the user switched CONNECTION. So a refresh faithfully reloaded the tree, raised
/// <c>ObjectsChanged</c>, and rebuilt every open editor's model — against the same stale columns. A column
/// added to a table stayed "unknown" for the rest of the session, on every open tab, no matter how many times
/// the user refreshed. That is the reported "diagnostics do not refresh after a metadata refresh".</para>
///
/// <para>⚠ Why a SOURCE guard. The defect is a MISSING SUBSCRIPTION, and a missing subscription has no
/// observable behaviour to assert against without a live connection, a populated tree and several open
/// editors — the shape that makes a headless test construct <c>MainWindow</c> and hang the suite. What can be
/// checked exactly is the wiring: the refresh announces the invalidation, and the side that owns the caches
/// listens. Both halves are asserted, because either one alone is silently inert — an event nobody raises and
/// an event nobody handles look identical from the other end.</para>
///
/// <para>⚠ Ordering is asserted too, and it is not cosmetic: the per-category <c>ObjectsChanged</c> signals
/// fire DURING the reload and each schedules a model rebuild, so a cache cleared after the reload would be
/// cleared behind a rebuild that had already read it. The announcement must come first.</para>
///
/// <para>⚠ This class reads source files and constructs nothing, so it belongs to the MAIN test partition.</para>
/// </summary>
public class MetadataCacheInvalidationTests
{
    [Fact]
    public void RefreshAsync_AnnouncesSchemaInvalidation_BeforeReloadingAnything()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "ViewModels", "MetadataExplorerViewModel.cs"));

        var refreshAt = source.IndexOf("public async Task RefreshAsync()", StringComparison.Ordinal);
        Assert.True(refreshAt > 0, "RefreshAsync was renamed — re-point this guard rather than deleting it.");

        var body = source[refreshAt..];
        var invalidateAt = body.IndexOf("SchemaInvalidated?.Invoke()", StringComparison.Ordinal);
        Assert.True(invalidateAt > 0, "RefreshAsync must announce SchemaInvalidated — see S-2.");

        // Before the first group reload, so no rebuild can read a cache that is about to be dropped.
        var firstLoadAt = body.IndexOf("LoadGroupAsync(group)", StringComparison.Ordinal);
        Assert.True(firstLoadAt > 0);
        Assert.True(
            invalidateAt < firstLoadAt,
            "SchemaInvalidated must be raised BEFORE the reload: the per-category ObjectsChanged signals each "
            + "schedule a model rebuild, so a cache dropped afterwards is dropped behind a rebuild that read it.");
    }

    [Fact]
    public void MainWindowViewModel_SubscribesSchemaInvalidation_AndDropsAllThreeCaches()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "ViewModels", "MainWindowViewModel.cs"));

        Assert.Contains("Metadata.SchemaInvalidated += InvalidateObjectCaches", source);

        // ⭐ One place drops all three, so a caller cannot forget one — and a fourth such cache added later
        // has one obvious home. Asserted on the METHOD, not on the call sites, for that reason.
        var methodAt = source.IndexOf("private void InvalidateObjectCaches()", StringComparison.Ordinal);
        Assert.True(methodAt > 0, "InvalidateObjectCaches was renamed — re-point this guard.");
        var method = source.Substring(methodAt, Math.Min(600, source.Length - methodAt));
        Assert.Contains("_columnCache.Clear()", method);
        Assert.Contains("_routineParameterCache.Clear()", method);
        Assert.Contains("_objectDetailCache.Clear()", method);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
