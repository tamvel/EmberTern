using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Derives the plain-language summary shown in the primary zone of the Performance
/// panel from a <see cref="PerformanceReport"/>. When per-table reads were measured the lead
/// is derived from the MEASURED findings, so it always agrees with the Findings zone (a plan
/// full scan that measurement proves cheap must not be reported as "why it's slow"); with no
/// measured reads it falls back to the parsed plan (Phase-1 behaviour). Pure presentation —
/// no analysis, no rules, no recommendations. Lives in the App layer so Core stays unchanged.</summary>
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

    /// <summary>The plain-language lead. When per-table reads were measured it is derived
    /// from the measured findings (so it agrees with the Findings zone); otherwise it falls
    /// back to naming the plan's full-scanned table(s). Empty when no plan was captured.</summary>
    public static string PlanLead(PerformanceReport report)
    {
        // Measured reads available → the lead comes from the measurement, never the plan.
        // The plan can show a full scan that the measured reads prove cheap; reporting that
        // as "why it's slow" would contradict the green "no costly scans" finding.
        if (report.Access is not null)
        {
            return MeasuredLead(report);
        }

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

    // Lead derived from measured reads: name the costly scan(s) the findings identified, or
    // — when nothing costly was measured — state that plainly, agreeing with the Findings zone.
    private static string MeasuredLead(PerformanceReport report)
    {
        var costly = report.Findings
            .Where(f => f.Kind == FindingKind.CostlyFullScan && !string.IsNullOrEmpty(f.Table))
            .Select(f => f.Table!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (costly.Count > 0)
        {
            var names = string.Join(", ", costly);
            return costly.Count == 1
                ? string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceMeasuredCostlyScanSingle, names)
                : string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceMeasuredCostlyScanMultiple, names);
        }

        long read = report.Verdict.RowsRead ?? report.Access!.TotalRowsRead;
        return string.Format(CultureInfo.CurrentCulture, UiStrings.PerformanceMeasuredNoCostlyScan,
            read.ToString("N0", CultureInfo.CurrentCulture),
            report.Verdict.RowsReturned.ToString("N0", CultureInfo.CurrentCulture));
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
