using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Performance;
using EmberTern.Core.Performance.Rules;
using EmberTern.Core.Diagnostics;
using Xunit;

namespace EmberTern.Tests;

public class PerformanceReadsTests
{
    // R1 (CostlyFullScanRule) is the migrated home of the measured full-scan finding — run it
    // through the context the same way the report builder does.
    private static IReadOnlyList<Finding> R1(TableAccessProfile? access, long rowsReturned)
    {
        var capture = new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT ..." },
            RowsReturned = rowsReturned,
        };
        var context = PerformanceContextBuilder.Build(capture, plan: null, access: access, catalog: null);
        return new CostlyFullScanRule().Evaluate(context);
    }

    private static PerTableReadRow[] Rows(params (string t, long seq, long idx)[] rows)
        => rows.Select(r => new PerTableReadRow(r.t, r.seq, r.idx)).ToArray();

    // ---- TableStatsDiffer: row-change counters (Execution Metrics step 1) -----------
    [Fact]
    public void Diff_ComputesInsertUpdateDeleteDelta()
    {
        var before = new[] { new PerTableReadRow("NAGL", 0, 0, Inserts: 5, Updates: 2, Deletes: 1) };
        var after = new[] { new PerTableReadRow("NAGL", 0, 0, Inserts: 25, Updates: 12, Deletes: 4) };
        var d = Assert.Single(TableStatsDiffer.Diff(before, after));
        Assert.Equal(20, d.Inserts);
        Assert.Equal(10, d.Updates);
        Assert.Equal(3, d.Deletes);
        Assert.Equal(33, d.TotalChanges);
    }

    [Fact]
    public void Diff_KeepsPureDmlRow_WithNoReads()
    {
        // A DML / procedure that only wrote rows (0 reads) must NOT be dropped.
        var after = new[] { new PerTableReadRow("T", 0, 0, Inserts: 8) };
        var d = Assert.Single(TableStatsDiffer.Diff(Array.Empty<PerTableReadRow>(), after));
        Assert.Equal("T", d.Table);
        Assert.Equal(8, d.Inserts);
    }

    [Fact]
    public void Diff_ClampsNegativeChangeDeltas()
    {
        var before = new[] { new PerTableReadRow("T", 0, 0, Updates: 100) };
        var after = new[] { new PerTableReadRow("T", 0, 0, Updates: 40) }; // went down → clamp, then dropped
        Assert.Empty(TableStatsDiffer.Diff(before, after));
    }

    // ---- TableStatsDiffer -----------------------------------------------------------
    [Fact]
    public void Diff_SubtractsBeforeFromAfter()
    {
        var before = Rows(("NAGL", 10, 5), ("POZ", 0, 100));
        var after = Rows(("NAGL", 100_010, 5), ("POZ", 0, 177));
        var d = TableStatsDiffer.Diff(before, after).ToDictionary(r => r.Table);

        Assert.Equal(100_000, d["NAGL"].SeqReads);
        Assert.Equal(0, d["NAGL"].IdxReads);
        Assert.Equal(77, d["POZ"].IdxReads);
    }

    [Fact]
    public void Diff_DropsUnchangedTables()
    {
        var before = Rows(("A", 5, 5));
        var after = Rows(("A", 5, 5), ("B", 0, 3));
        var d = TableStatsDiffer.Diff(before, after);
        Assert.Single(d, r => r.Table == "B");
        Assert.DoesNotContain(d, r => r.Table == "A");
    }

    [Fact]
    public void Diff_NewTableInAfter_IsFullDelta()
    {
        var d = TableStatsDiffer.Diff(Rows(), Rows(("NEW", 40, 0)));
        Assert.Equal(40, Assert.Single(d).SeqReads);
    }

    [Fact]
    public void Diff_ClampsNegativeToZeroAndIsCaseInsensitive()
    {
        var before = Rows(("tbl", 100, 0));
        var after = Rows(("TBL", 50, 0)); // went down (shouldn't happen) → clamp, then dropped
        Assert.Empty(TableStatsDiffer.Diff(before, after));
    }

    // ---- PerformanceFindings --------------------------------------------------------
    [Fact]
    public void Findings_LargeSequentialScan_IsHigh()
    {
        var access = Profile(("INVOICE", 100_010, 0));
        var f = Assert.Single(R1(access, rowsReturned: 285));
        Assert.Equal(FindingKind.CostlyFullScan, f.Kind);
        Assert.Equal(FindingSeverity.High, f.Severity);
        Assert.Equal("INVOICE", f.Table);
        // ⭐ The DATUM, not the sentence around it (the portable form localization.md §4.2 recommends, and
        // the shape C5 strengthened its own assertions into).
        Assert.Contains("INVOICE", f.Title.Arguments);
        // ⛔ `.Table`, not `.Statement` — R1 measures ONE table. The two labels read identically in English.
        Assert.Contains(f.Evidence, e => e.Label == PerfMessages.EvidenceReadAmplificationTable);
    }

    [Fact]
    public void Findings_SmallScan_IsNotFlagged()
    {
        // The "72 looks scary" fix: a 72-row scan produces no finding.
        Assert.Empty(R1(Profile(("SMALL", 72, 0)), rowsReturned: 72));
    }

    [Fact]
    public void Findings_IndexOnlyAccess_NoFinding()
    {
        Assert.Empty(R1(Profile(("T", 0, 5000)), rowsReturned: 10));
    }

    [Fact]
    public void Findings_AmplificationRaisesSeverity()
    {
        // 5000 seq reads returning 1 row → amplification 5000 → High via the amp branch.
        var f = Assert.Single(R1(Profile(("T", 5_000, 0)), rowsReturned: 1));
        Assert.Equal(FindingSeverity.High, f.Severity);
    }

    [Fact]
    public void Findings_MidScan_ModerateReturn_IsMediumOrLow()
    {
        // 800 seq reads returning 800 rows (amp 1×) → Low (below Medium floor, no amp boost).
        var f = Assert.Single(R1(Profile(("T", 800, 0)), rowsReturned: 800));
        Assert.Equal(FindingSeverity.Low, f.Severity);
    }

    [Fact]
    public void Findings_NullOrEmptyAccess_NoFindings()
    {
        Assert.Empty(R1(null, 100));
        Assert.Empty(R1(new TableAccessProfile(), 100));
    }

    // ---- ReportBuilder with reads ---------------------------------------------------
    private static PerformanceCapture CaptureWith(IReadOnlyList<PerTableReadRow> reads, long rows, double ms = 500) =>
        new()
        {
            Statement = new StatementIdentity { Sql = "SELECT ..." },
            Plan = new RawPlanCapture(PlanDialect.Explain,
                "Select Expression\n    -> Table \"INVOICE\" Full Scan\n"),
            Timings = new ExecutionTimings { Execute = TimeSpan.FromMilliseconds(ms) },
            RowsReturned = rows,
            TableReads = reads,
            Method = CaptureMethod.MonAttachmentDelta,
        };

    [Fact]
    public void Report_WithReads_PopulatesAccessAmplificationAndFindings()
    {
        var report = new PerformanceReportBuilder().Build(
            CaptureWith(Rows(("INVOICE", 100_000, 0), ("LINE", 0, 78_000)), rows: 285));

        Assert.NotNull(report.Access);
        Assert.Equal("INVOICE", report.Access!.Tables[0].Table); // sorted seq-desc
        Assert.Equal(178_000, report.Verdict.RowsRead);
        Assert.Equal(178_000d / 285d, report.Verdict.Amplification!.Value, 3);
        Assert.Contains(report.Findings, f => f.Kind == FindingKind.CostlyFullScan && f.Table == "INVOICE");
        // A High reads-finding lifts a fast-by-time query to Needs attention.
        Assert.Equal(PerformanceGrade.NeedsAttention, report.Verdict.Grade);
    }

    [Fact]
    public void Report_WithoutReads_StaysPhase1()
    {
        var capture = new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT 1" },
            Plan = new RawPlanCapture(PlanDialect.Explain, "Select Expression\n    -> Table \"T\" Full Scan\n"),
            Timings = new ExecutionTimings { Execute = TimeSpan.FromMilliseconds(50) },
            RowsReturned = 3,
        };
        var report = new PerformanceReportBuilder().Build(capture);

        Assert.Null(report.Access);
        Assert.Empty(report.Findings);
        Assert.Null(report.Verdict.RowsRead);
        Assert.Null(report.Verdict.Amplification);
        Assert.Equal(PerformanceGrade.Fast, report.Verdict.Grade); // by time; no reads to bump it
    }

    private static TableAccessProfile Profile(params (string t, long seq, long idx)[] rows)
        => new()
        {
            Tables = rows.Select(r => new TableAccessStat(r.t, r.seq, r.idx)).ToList(),
            Method = CaptureMethod.MonAttachmentDelta,
        };
}
