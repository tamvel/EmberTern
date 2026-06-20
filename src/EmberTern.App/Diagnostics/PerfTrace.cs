using System;
using System.Globalization;
using EmberTern.Firebird;

namespace EmberTern.App.Diagnostics;

/// <summary>
/// Lightweight before/after instrumentation for the Object Explorer load path,
/// added during the tree-performance sprint to quantify the lazy-load change
/// (eager full-load of every object → COUNT-only on connect, lists on expand).
///
/// Writes one timestamped line per event to the shared
/// <c>%TEMP%\EmberTern-debug.log</c>, but ONLY when the environment variable
/// <c>EMBERTERN_PERF_DIAG</c> is set — zero cost otherwise.
///
/// Measurement procedure (run against the live FB schema):
///   1. set <c>EMBERTERN_PERF_DIAG=1</c>, launch, connect.
///   2. read the <c>PERF [category-load]</c> line — that's the connect→usable-tree
///      time (count fetch for all categories) plus managed-heap size right after.
///   3. expand a big category — the <c>PERF [group-load]</c> line is the
///      first-expansion cost for that category's full leaf list.
/// Compare the same lines on the pre-change build to quantify the gain.
/// </summary>
internal static class PerfTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("EMBERTERN_PERF_DIAG") is not null;

    public static bool IsEnabled => Enabled;

    /// <summary>Connect → category COUNTs loaded for all categories.</summary>
    public static void LogCategoryLoad(string profile, int categoryCount, long elapsedMs)
    {
        if (!Enabled) return;
        FirebirdDiagnostics.AppendDebugLog(string.Format(
            CultureInfo.InvariantCulture,
            "PERF [category-load] profile='{0}' categories={1} countFetchMs={2} managedHeapKB={3}",
            profile, categoryCount, elapsedMs, ManagedHeapKb()));
    }

    /// <summary>First expansion of a category → full leaf list loaded.</summary>
    public static void LogGroupLoad(string kind, int leafCount, long elapsedMs)
    {
        if (!Enabled) return;
        FirebirdDiagnostics.AppendDebugLog(string.Format(
            CultureInfo.InvariantCulture,
            "PERF [group-load] kind={0} leaves={1} loadMs={2} managedHeapKB={3}",
            kind, leafCount, elapsedMs, ManagedHeapKb()));
    }

    private static long ManagedHeapKb() => GC.GetTotalMemory(forceFullCollection: false) / 1024;
}
