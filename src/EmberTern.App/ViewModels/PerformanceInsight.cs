using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Derives the plain-language summary shown in the primary zone of the Performance
/// panel from a Phase-1 <see cref="PerformanceReport"/> (parsed plan + timings only).
/// Pure presentation — no analysis, no rules, no recommendations. It renders the facts the
/// plan already contains (which tables are full-scanned, how many sub-queries) in words a
/// non-Firebird developer can act on. Lives in the App layer so Core stays unchanged.</summary>
internal static class PerformanceInsight
{
    /// <summary>The grade line, e.g. "Needs attention — this query took 1.79 s."</summary>
    public static string GradeLine(PerformanceReport report)
    {
        var duration = FormatDuration(report.Verdict.Duration.TotalMilliseconds);
        return report.Verdict.Grade switch
        {
            PerformanceGrade.Fast => string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceGradeFast, duration),
            PerformanceGrade.Acceptable => string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceGradeAcceptable, duration),
            PerformanceGrade.NeedsAttention => string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceGradeNeedsAttention, duration),
            PerformanceGrade.Slow => string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceGradeSlow, duration),
            _ => UiStrings.PerformanceGradeUnknown,
        };
    }

    /// <summary>The plain-language plan lead — names the full-scanned table(s), or states
    /// that everything uses indexes. Empty when no plan was captured.</summary>
    public static string PlanLead(PerformanceReport report)
    {
        if (report.Plan is not { } plan)
        {
            return string.Empty;
        }
        var tables = FullScanTables(plan);
        if (tables.Count == 0)
        {
            return UiStrings.PerformanceLeadNoFullScan;
        }
        var names = string.Join(", ", tables);
        return tables.Count == 1
            ? string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceLeadFullScanSingle, names)
            : string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceLeadFullScanMultiple, names);
    }

    /// <summary>The "it also evaluates N sub-queries" noise summary, or null when the plan
    /// has no sub-query roots.</summary>
    public static string? NoiseSummary(PerformanceReport report)
    {
        if (report.Plan is not { } plan)
        {
            return null;
        }
        int subqueries = plan.Roots.Count(IsSubqueryRoot);
        return subqueries switch
        {
            <= 0 => null,
            1 => UiStrings.PerformanceNoiseSubqueriesSingle,
            _ => string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceNoiseSubqueriesMultiple, subqueries),
        };
    }

    /// <summary>Show the "confirmation arrives later" note only when there is a full scan
    /// whose impact Phase 1 cannot yet size.</summary>
    public static bool ShowForwardPointer(PerformanceReport report)
        => report.Plan is { } plan && FullScanTables(plan).Count > 0;

    private static IReadOnlyList<string> FullScanTables(PlanTree plan)
    {
        var seen = new List<string>();
        foreach (var node in plan.EnumerateNodes())
        {
            if (node.IsSequentialScan && !string.IsNullOrEmpty(node.TableName) && !seen.Contains(node.TableName!))
            {
                seen.Add(node.TableName!);
            }
        }
        return seen;
    }

    private static bool IsSubqueryRoot(PlanNode root)
        => root.RawText.StartsWith("Sub-query", StringComparison.OrdinalIgnoreCase);

    private static string FormatDuration(double ms) => ms < 1000
        ? ms.ToString("0", CultureInfo.CurrentCulture) + " ms"
        : (ms / 1000.0).ToString("0.00", CultureInfo.CurrentCulture) + " s";
}
