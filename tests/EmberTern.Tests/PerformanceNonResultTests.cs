using System;
using System.Linq;
using EmberTern.Core.Performance;
using EmberTern.Core.Diagnostics;
using Xunit;

namespace EmberTern.Tests;

// Step 4: a non-result statement (DML / EXECUTE PROCEDURE / BLOCK) is framed around rows
// CHANGED, not "returned 0" — amplification = reads ÷ changes, findings say "to change N".
public class PerformanceNonResultTests
{
    private static PerformanceCapture NonResult(long seq, long updates, int affected = 8, double ms = 500) => new()
    {
        Statement = new StatementIdentity { Sql = "UPDATE NAGL SET X = 1 WHERE Y = 2" },
        Plan = new RawPlanCapture(PlanDialect.Explain, "Select Expression\n    -> Table \"NAGL\" Full Scan\n"),
        Timings = new ExecutionTimings { Execute = TimeSpan.FromMilliseconds(ms) },
        RecordsAffected = affected,   // non-null ⇒ HasResultSet == false
        TableReads = new[] { new PerTableReadRow("NAGL", seq, 0, Inserts: 0, Updates: updates, Deletes: 0) },
        Method = CaptureMethod.MonAttachmentDelta,
    };

    [Fact]
    public void Context_NonResult_OutputIsRowsChanged()
    {
        var ctx = PerformanceContextBuilder.Build(NonResult(seq: 100_000, updates: 8), plan: null, access: Access(100_000, 8), catalog: null);
        Assert.False(ctx.HasResultSet);
        Assert.Equal(8, ctx.RowsChanged);
        Assert.Equal(8, ctx.OutputRows);
        Assert.Equal("change", ctx.OutputVerb);
        Assert.Equal("Rows changed", ctx.OutputRowsLabel);
        Assert.Equal(100_000d / 8d, ctx.Amplification!.Value, 3); // reads ÷ changes, not ÷ 0
    }

    private static TableAccessProfile Access(long seq, long _upd)
        => new() { Tables = new[] { new TableAccessStat("NAGL", seq, 0) }, Method = CaptureMethod.MonAttachmentDelta };

    [Fact]
    public void Report_NonResult_VerdictAndFinding_FramedAroundChanges()
    {
        var report = new PerformanceReportBuilder().Build(NonResult(seq: 100_000, updates: 8));

        Assert.False(report.Verdict.HasResultSet);
        Assert.Equal(8, report.Verdict.RowsChanged);
        Assert.Equal(100_000d / 8d, report.Verdict.Amplification!.Value, 3);

        var scan = Assert.Single(report.Findings, f => f.Kind == FindingKind.CostlyFullScan);
        Assert.Contains("to change 8", scan.Explanation);
        Assert.DoesNotContain("to return", scan.Explanation);
        Assert.Contains(scan.Evidence, e => e.Label == "Rows changed");
        Assert.DoesNotContain(scan.Evidence, e => e.Label == "Rows returned");
    }

    [Fact]
    public void Report_Select_StillFramedAroundReturned()
    {
        var capture = new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT * FROM NAGL" },
            Plan = new RawPlanCapture(PlanDialect.Explain, "Select Expression\n    -> Table \"NAGL\" Full Scan\n"),
            Timings = new ExecutionTimings { Execute = TimeSpan.FromMilliseconds(500) },
            RowsReturned = 285,
            TableReads = new[] { new PerTableReadRow("NAGL", 100_000, 0) },
            Method = CaptureMethod.MonAttachmentDelta,
        };
        var report = new PerformanceReportBuilder().Build(capture);
        var scan = Assert.Single(report.Findings, f => f.Kind == FindingKind.CostlyFullScan);
        Assert.Contains("to return 285", scan.Explanation);
        Assert.Contains(scan.Evidence, e => e.Label == "Rows returned");
    }
}
