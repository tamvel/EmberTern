using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Performance;
using Xunit;

namespace EmberTern.Tests;

// Pins the Summary ↔ Verdict ↔ Findings consistency contract: once per-table reads are
// measured, the plain-language lead must agree with the Findings zone and never repeat the
// Phase-1 plan heuristic ("full scan … often why a query is slow") that measurement disproves.
public class PerformanceInsightTests
{
    private static PerTableReadRow[] Rows(params (string t, long seq, long idx)[] rows)
        => rows.Select(r => new PerTableReadRow(r.t, r.seq, r.idx)).ToArray();

    private static PerformanceReport Build(IReadOnlyList<PerTableReadRow>? reads, long returned, string fullScanTable, double ms = 1140) =>
        new PerformanceReportBuilder().Build(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT ..." },
            Plan = new RawPlanCapture(PlanDialect.Explain,
                $"Select Expression\n    -> Table \"{fullScanTable}\" Full Scan\n"),
            Timings = new ExecutionTimings { Execute = TimeSpan.FromMilliseconds(ms) },
            RowsReturned = returned,
            TableReads = reads ?? Array.Empty<PerTableReadRow>(),
            Method = reads is null ? CaptureMethod.PlanOnly : CaptureMethod.MonAttachmentDelta,
        });

    [Fact]
    public void Lead_ReadsMeasured_NoCostlyScan_AgreesWithFindings_NotPlanHeuristic()
    {
        // The screenshot case: NAGL full-scanned but only 285 rows read → not costly.
        var report = Build(Rows(("NAGL", 285, 3135), ("POZ", 0, 7535)), returned: 285, fullScanTable: "NAGL");

        Assert.NotNull(report.Access);          // reads were measured
        Assert.Empty(report.Findings);          // nothing costly → matches green Findings note
        var lead = PerformanceInsight.PlanLead(report);

        Assert.StartsWith("No costly full table scans were measured", lead);
        Assert.DoesNotContain("often why a query is slow", lead); // the Phase-1 heuristic must NOT fire
        Assert.DoesNotContain("largest measured cost", lead);      // not the costly-scan variant
    }

    [Fact]
    public void Lead_ReadsMeasured_CostlyScan_NamesTheMeasuredTable()
    {
        var report = Build(Rows(("INVOICE", 100_000, 0)), returned: 285, fullScanTable: "INVOICE");

        Assert.Contains(report.Findings, f => f.Kind == FindingKind.CostlyFullScan && f.Table == "INVOICE");
        var lead = PerformanceInsight.PlanLead(report);

        Assert.Contains("INVOICE", lead);
        Assert.Contains("row by row", lead);
        Assert.DoesNotContain("No costly", lead);
    }

    [Fact]
    public void Lead_NoReads_FallsBackToPlanHeuristic()
    {
        var report = Build(reads: null, returned: 3, fullScanTable: "T");

        Assert.Null(report.Access); // no measured reads → Phase-1 fallback
        var lead = PerformanceInsight.PlanLead(report);

        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture,
            UiStrings.PerformanceLeadFullScanSingle, "T"), lead);
    }
}
