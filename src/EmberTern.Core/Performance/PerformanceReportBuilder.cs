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
    private readonly PerformanceRuleEngine _ruleEngine;

    public PerformanceReportBuilder(PlanParser? planParser = null, PerformanceRuleEngine? ruleEngine = null)
    {
        _planParser = planParser ?? new PlanParser();
        _ruleEngine = ruleEngine ?? new PerformanceRuleEngine();
    }

    public PerformanceReport Build(PerformanceCapture capture, CatalogModel? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(capture);

        PlanTree? plan = capture.Plan is { } raw ? _planParser.Parse(raw) : null;
        var timings = capture.Timings;
        int fullScans = plan is null ? 0 : plan.EnumerateNodes().Count(n => n.IsSequentialScan);
        long rowsReturned = capture.HasResultSet ? capture.RowsReturned : 0;

        // Measured per-table reads (Phase 2): build the access profile. The advisor rule engine
        // (Phase 3a) then derives findings from the measured access + plan + predicates + catalog.
        TableAccessProfile? access = BuildAccess(capture);
        var context = PerformanceContextBuilder.Build(capture, plan, access, catalog);
        var findings = _ruleEngine.Evaluate(context);

        long? rowsRead = context.RowsRead;
        double? amplification = context.Amplification;

        var verdict = new PerformanceVerdict
        {
            Grade = Grade(timings, findings),
            Headline = BuildHeadline(capture, plan, fullScans),
            Duration = timings?.Total ?? TimeSpan.Zero,
            RowsReturned = rowsReturned,
            RowsRead = rowsRead,
            Amplification = amplification,
        };

        var details = new ExecutionDetails
        {
            Timings = timings,
            RawPlanText = capture.Plan?.PlanText,
            PlanDialect = capture.Plan?.Dialect,
            Method = capture.Method,
        };

        return new PerformanceReport
        {
            Verdict = verdict,
            Plan = plan,
            Access = access,
            Findings = findings,
            Details = details,
        };
    }

    private static TableAccessProfile? BuildAccess(PerformanceCapture capture)
    {
        if (!capture.ReadsAvailable)
        {
            return null;
        }
        var tables = capture.TableReads
            .Select(r => new TableAccessStat(r.Table, r.SeqReads, r.IdxReads))
            .OrderByDescending(t => t.SequentialReads)
            .ThenByDescending(t => t.TotalReads)
            .ToList();
        return new TableAccessProfile { Tables = tables, Method = capture.Method };
    }

    // Grade is time-based, but a High measured-reads finding raises it to at least
    // "Needs attention" — now we have evidence, not a guess.
    private static PerformanceGrade Grade(ExecutionTimings? timings, IReadOnlyList<Finding> findings)
    {
        var byTime = GradeByTime(timings);
        bool hasHigh = findings.Any(f => f.Severity == FindingSeverity.High);
        if (hasHigh && byTime is PerformanceGrade.Fast or PerformanceGrade.Acceptable or PerformanceGrade.Unknown)
        {
            return PerformanceGrade.NeedsAttention;
        }
        return byTime;
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
