using System;
using System.Globalization;
using EmberTern.Firebird;

namespace EmberTern.App.Diagnostics;

/// <summary>
/// Live instrumentation for the sidebar-tree scroll-jump investigation. Writes one
/// timestamped line per scroll change (and per tree rebuild) to the shared
/// <c>%TEMP%\EmberTern-debug.log</c>, but ONLY when <c>EMBERTERN_SCROLL_DIAG</c> is set —
/// zero cost otherwise.
///
/// Purpose: confirm on the REAL app whether the "scrollbar fights / snaps back" is
/// Avalonia's <c>VirtualizingStackPanel</c> re-ESTIMATING the scroll extent as you drag
/// the thumb (an <c>extentH</c> that keeps changing while <c>offsetY</c> moves) — versus
/// EmberTern rebuilding the tree collection mid-scroll (a <c>[rebuild]</c> line
/// interleaved with the scroll lines). Both are captured so the log is decisive.
///
/// Procedure: set <c>EMBERTERN_SCROLL_DIAG=1</c>, launch, connect, expand a large
/// category (no filter), drag the scrollbar down, then read <c>%TEMP%\EmberTern-debug.log</c>.
///   - extentH changing during the drag  → Avalonia VSP extent re-estimation (control-level).
///   - a [rebuild] line during the drag   → EmberTern code rebuilt the tree (our bug).
/// </summary>
internal static class ScrollTrace
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("EMBERTERN_SCROLL_DIAG") is not null;

    public static bool IsEnabled => Enabled;

    /// <summary>One scroll change: current geometry + the deltas from this change.</summary>
    public static void Scroll(double offsetY, double extentH, double viewportH, double offsetDeltaY, double extentDeltaY)
    {
        if (!Enabled) return;
        FirebirdDiagnostics.AppendDebugLog(string.Format(
            CultureInfo.InvariantCulture,
            "SCROLL offsetY={0:0.0} extentH={1:0.0} viewportH={2:0.0} dOffset={3:0.0} dExtent={4:0.0}{5}",
            offsetY, extentH, viewportH, offsetDeltaY, extentDeltaY,
            Math.Abs(extentDeltaY) > 0.5 ? "  <-- EXTENT RE-ESTIMATED DURING SCROLL" : string.Empty));
    }

    /// <summary>A tree-collection rebuild — if this appears mid-drag, it's our bug, not the control.</summary>
    public static void Rebuild(string what)
    {
        if (!Enabled) return;
        FirebirdDiagnostics.AppendDebugLog("SCROLL [rebuild] " + what);
    }
}
