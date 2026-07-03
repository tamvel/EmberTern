using System;
using System.Linq;
using EmberTern.Core.Performance;
using Xunit;

namespace EmberTern.Tests;

public class PerformanceReportTests
{
    private static RawPlanCapture FullScanPlan =>
        new(PlanDialect.Explain, "Select Expression\n    -> Table \"HORSE\" Full Scan\n");

    private static RawPlanCapture IndexPlan =>
        new(PlanDialect.Explain,
            "Select Expression\n    -> Table \"H\" Access By ID\n        -> Bitmap\n            -> Index \"IX\" Unique Scan\n");

    private static ExecutionTimings Timings(double ms) =>
        new() { Prepare = TimeSpan.FromMilliseconds(2), Execute = TimeSpan.FromMilliseconds(ms) };

    private static PerformanceReport Analyze(PerformanceCapture capture) =>
        new PerformanceAnalyzer().Analyze(capture);

    [Fact]
    public void FullScanPlan_ParsedAndReportedNeutrally()
    {
        var report = Analyze(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT * FROM HORSE" },
            Plan = FullScanPlan,
            Timings = Timings(500),
            RowsReturned = 285,
        });

        Assert.NotNull(report.Plan);
        Assert.Equal("Plan contains 1 full table scan.", report.Verdict.Headline);
        Assert.Equal(285, report.Verdict.RowsReturned);
        // "72 looks scary" guard: a full scan alone does NOT force NeedsAttention — grade is by time.
        Assert.Equal(PerformanceGrade.Acceptable, report.Verdict.Grade);
        Assert.Null(report.Verdict.RowsRead);       // Phase 2
        Assert.Null(report.Verdict.Amplification);  // Phase 2
    }

    [Fact]
    public void IndexOnlyPlan_HeadlineSaysIndexes()
    {
        var report = Analyze(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT * FROM H WHERE ID = 1" },
            Plan = IndexPlan,
            Timings = Timings(30),
            RowsReturned = 1,
        });

        Assert.Equal("All table access in the plan uses indexes.", report.Verdict.Headline);
        Assert.Equal(PerformanceGrade.Fast, report.Verdict.Grade);
    }

    [Theory]
    [InlineData(40, PerformanceGrade.Fast)]
    [InlineData(500, PerformanceGrade.Acceptable)]
    [InlineData(2_000, PerformanceGrade.NeedsAttention)]
    [InlineData(10_000, PerformanceGrade.Slow)]
    public void Grade_IsByMeasuredTime(double executeMs, PerformanceGrade expected)
    {
        var report = Analyze(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT 1" },
            Plan = FullScanPlan,
            Timings = Timings(executeMs),
            RowsReturned = 0,
        });
        Assert.Equal(expected, report.Verdict.Grade);
    }

    [Fact]
    public void NoTimings_GradeUnknown()
    {
        var report = Analyze(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT 1" },
            Plan = FullScanPlan,
            Timings = null,
            RowsReturned = 0,
        });
        Assert.Equal(PerformanceGrade.Unknown, report.Verdict.Grade);
    }

    [Fact]
    public void NoPlan_DegradesGracefully()
    {
        var report = Analyze(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT 1" },
            Plan = null,
            Timings = Timings(20),
            RowsReturned = 3,
        });
        Assert.Null(report.Plan);
        Assert.Equal("Executed; execution plan unavailable.", report.Verdict.Headline);
        Assert.Null(report.Details.RawPlanText);
        Assert.Null(report.Details.PlanDialect);
    }

    [Fact]
    public void DmlStatement_ReportsRowsAffected()
    {
        var report = Analyze(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "UPDATE T SET X = 1" },
            Plan = null,
            Timings = Timings(15),
            RowsReturned = 0,
            RecordsAffected = 312,
        });
        Assert.Equal("312 rows affected.", report.Verdict.Headline);
        Assert.Equal(0, report.Verdict.RowsReturned);
    }

    [Fact]
    public void TwoFullScans_Pluralized()
    {
        var report = Analyze(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT * FROM A, B" },
            Plan = new RawPlanCapture(PlanDialect.Explain,
                "Select Expression\n" +
                "    -> Nested Loop Join (inner)\n" +
                "        -> Table \"A\" Full Scan\n" +
                "        -> Table \"B\" Full Scan\n"),
            Timings = Timings(300),
            RowsReturned = 10,
        });
        Assert.Equal("Plan contains 2 full table scans.", report.Verdict.Headline);
    }

    [Fact]
    public void Details_CarryRawPlanAndCaptureMethod()
    {
        var report = Analyze(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT * FROM HORSE" },
            Plan = FullScanPlan,
            Timings = Timings(100),
            RowsReturned = 5,
            Method = CaptureMethod.PlanOnly,
        });
        Assert.Equal(FullScanPlan.PlanText, report.Details.RawPlanText);
        Assert.Equal(PlanDialect.Explain, report.Details.PlanDialect);
        Assert.Equal(CaptureMethod.PlanOnly, report.Details.Method);
        Assert.NotNull(report.Details.Timings);
    }
}
