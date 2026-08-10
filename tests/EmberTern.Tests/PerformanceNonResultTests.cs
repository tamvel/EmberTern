using System;
using System.Linq;
using EmberTern.App.Localization;
using EmberTern.Core.Localization;
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
        // ⚠ C7 (D‑3): `OutputVerb` / `OutputRowsLabel` no longer exist — the context stopped choosing
        // EmberTern's WORDS. What it still decides is the FACT the rules branch on, and that is what is
        // asserted here; which sentence follows from it is asserted below and in
        // TheDmlAndSelectVariants_AreTheSameSentenceWithADifferentVerb.
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

        // ⭐ The rule now picks a KEY instead of weaving a verb into a sentence, so the assertion is on the
        // choice, not on the English that follows from it.
        Assert.Equal(PerfMessages.CostlyFullScanExplanationChange, scan.Explanation!.Key);
        Assert.Equal(PerfMessages.EvidenceRowsChanged, Assert.Single(
            scan.Evidence, e => e.Label.Value.EndsWith("RowsChanged", StringComparison.Ordinal)).Label);
        Assert.DoesNotContain(scan.Evidence, e => e.Label == PerfMessages.EvidenceRowsReturned);

        // ⚠ …and the rendered English still says it, which is what the pre-C7 assertion measured.
        Assert.Contains("to change 8", Loc.Format(scan.Explanation));
        Assert.DoesNotContain("to return", Loc.Format(scan.Explanation));
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

        Assert.Equal(PerfMessages.CostlyFullScanExplanationSelect, scan.Explanation!.Key);
        Assert.Contains(scan.Evidence, e => e.Label == PerfMessages.EvidenceRowsReturned);
        Assert.Contains("to return 285", Loc.Format(scan.Explanation));
    }

    /// <summary>
    /// ⭐ <b>G7's per-sentence half: the two variants are the SAME sentence with a different verb.</b> The
    /// point of removing <c>OutputVerb</c> was that one sentence could no longer serve both — so the guard
    /// that matters is that the pair still says the same thing about the same numbers, and differs only where
    /// the verb sits. Comparing the rendered halves catches a translator (or a careless edit) turning one of
    /// them into a different sentence, which no key-level check can see.
    /// </summary>
    [Fact]
    public void TheDmlAndSelectVariants_AreTheSameSentenceWithADifferentVerb()
    {
        var change = Loc.Format(LocalizableMessage.Of(
            PerfMessages.CostlyFullScanExplanationChange, 100_000L, 8L));
        var select = Loc.Format(LocalizableMessage.Of(
            PerfMessages.CostlyFullScanExplanationSelect, 100_000L, 8L));

        Assert.NotEqual(select, change);
        Assert.Equal(select.Replace(" to return ", " to change "), change);
    }
}
