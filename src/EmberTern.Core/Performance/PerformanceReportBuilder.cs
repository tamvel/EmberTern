using System;
using System.Linq;

namespace EmberTern.Core.Performance;

/// <summary>Turns a raw <see cref="PerformanceCapture"/> into a <see cref="PerformanceReport"/>:
/// parses the plan, computes the verdict, assembles the details drawer. Pure.
///
/// Phase-1 grading is deliberately conservative and honest: the grade is a coarse proxy
/// derived from the measured wall-clock time (the only signal available without per-table
/// reads), and a full table scan is reported as a neutral FACT, never as an alarm — a full
/// scan of a tiny table is fine, and we cannot tell scale until per-table reads land in
/// Phase 2. This prevents the "72 looks scary" false alarm from day one.</summary>
public sealed class PerformanceReportBuilder
{
    private readonly PlanParser _planParser;

    public PerformanceReportBuilder(PlanParser? planParser = null)
        => _planParser = planParser ?? new PlanParser();

    public PerformanceReport Build(PerformanceCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        PlanTree? plan = capture.Plan is { } raw ? _planParser.Parse(raw) : null;
        var timings = capture.Timings;
        int fullScans = plan is null ? 0 : plan.EnumerateNodes().Count(n => n.IsSequentialScan);

        var verdict = new PerformanceVerdict
        {
            Grade = GradeByTime(timings),
            Headline = BuildHeadline(capture, plan, fullScans),
            Duration = timings?.Total ?? TimeSpan.Zero,
            RowsReturned = capture.HasResultSet ? capture.RowsReturned : 0,
            RowsRead = null,        // Phase 2 (per-table reads)
            Amplification = null,   // Phase 2
        };

        var details = new ExecutionDetails
        {
            Timings = timings,
            RawPlanText = capture.Plan?.PlanText,
            PlanDialect = capture.Plan?.Dialect,
            Method = capture.Method,
        };

        return new PerformanceReport { Verdict = verdict, Plan = plan, Details = details };
    }

    private static PerformanceGrade GradeByTime(ExecutionTimings? timings)
    {
        if (timings is null)
        {
            return PerformanceGrade.Unknown;
        }
        var ms = timings.Total.TotalMilliseconds;
        return ms switch
        {
            < 100 => PerformanceGrade.Fast,
            < 1_000 => PerformanceGrade.Acceptable,
            < 5_000 => PerformanceGrade.NeedsAttention,
            _ => PerformanceGrade.Slow,
        };
    }

    private static string BuildHeadline(PerformanceCapture capture, PlanTree? plan, int fullScans)
    {
        if (!capture.HasResultSet)
        {
            int affected = capture.RecordsAffected ?? 0;
            return affected == 1 ? "1 row affected." : $"{affected} rows affected.";
        }
        if (plan is null)
        {
            return "Executed; execution plan unavailable.";
        }
        return fullScans switch
        {
            0 => "All table access in the plan uses indexes.",
            1 => "Plan contains 1 full table scan.",
            _ => $"Plan contains {fullScans} full table scans.",
        };
    }
}
