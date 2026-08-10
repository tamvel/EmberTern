using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Performance;
using EmberTern.Core.Performance.Rules;
using EmberTern.Core.Diagnostics;
using Xunit;

namespace EmberTern.Tests;

public class PerformanceRulesTests
{
    private static TableAccessProfile Access(params (string t, long seq, long idx)[] rows)
        => new()
        {
            Tables = rows.Select(r => new TableAccessStat(r.t, r.seq, r.idx)).ToList(),
            Method = CaptureMethod.MonAttachmentDelta,
        };

    private static PlanTree IndexPlan(string table, string index)
        => new()
        {
            Dialect = PlanDialect.Explain,
            Roots = new List<PlanNode> { new() { Method = AccessMethod.IndexScan, TableName = table, IndexName = index } },
        };

    private static CatalogModel Catalog(params IndexModel[] indexes)
        => new() { Tables = new List<TableCatalogInfo> { new() { Table = "T", Indexes = indexes } } };

    private static IndexModel Ix(string name, string col, double? selectivity)
        => new() { Name = name, Columns = new[] { col }, Selectivity = selectivity };

    private static PerformanceContext Ctx(
        TableAccessProfile? access, long returned,
        PlanTree? plan = null, CatalogModel? catalog = null, string sql = "SELECT 1 FROM T")
    {
        var capture = new PerformanceCapture { Statement = new StatementIdentity { Sql = sql }, RowsReturned = returned };
        return PerformanceContextBuilder.Build(capture, plan, access, catalog);
    }

    // ── R4 low-selectivity index ─────────────────────────────────────────────
    [Fact]
    public void R4_Fires_WhenIndexAmplifiedAndCatalogSelectivityPoor()
    {
        var ctx = Ctx(Access(("T", 0, 8000)), returned: 285,
            plan: IndexPlan("T", "IX_T"), catalog: Catalog(Ix("IX_T", "STATUS", 0.5)));
        var f = Assert.Single(new LowSelectivityIndexRule().Evaluate(ctx));
        Assert.Equal(FindingKind.LowSelectivityIndex, f.Kind);
        Assert.Equal("R4", f.RuleId);
        Assert.Contains("IX_T", f.Title.Arguments);
    }

    [Fact]
    public void R4_Silent_WhenSelectivityGood_OrNoCatalog_OrLowAmplification_OrSequential()
    {
        Assert.Empty(new LowSelectivityIndexRule().Evaluate(
            Ctx(Access(("T", 0, 8000)), 285, IndexPlan("T", "IX_T"), Catalog(Ix("IX_T", "C", 0.001))))); // good selectivity
        Assert.Empty(new LowSelectivityIndexRule().Evaluate(
            Ctx(Access(("T", 0, 8000)), 285, IndexPlan("T", "IX_T"), catalog: null)));                    // no catalog
        Assert.Empty(new LowSelectivityIndexRule().Evaluate(
            Ctx(Access(("T", 0, 8000)), 8000, IndexPlan("T", "IX_T"), Catalog(Ix("IX_T", "C", 0.5)))));   // amp 1×
        Assert.Empty(new LowSelectivityIndexRule().Evaluate(
            Ctx(Access(("T", 4000, 8000)), 285, IndexPlan("T", "IX_T"), Catalog(Ix("IX_T", "C", 0.5))))); // has seq → R1's
    }

    // ── R3 non-sargable predicate ────────────────────────────────────────────
    [Fact]
    public void R3_Fires_WhenNonSargableOnScannedTableWithExistingIndex()
    {
        var ctx = Ctx(Access(("T", 2000, 0)), returned: 10,
            catalog: Catalog(Ix("IX_NAZWA", "NAZWA", 0.01)),
            sql: "SELECT * FROM T WHERE UPPER(NAZWA) = 'X'");
        var f = Assert.Single(new NonSargablePredicateRule().Evaluate(ctx));
        Assert.Equal(FindingKind.NonSargablePredicate, f.Kind);
        Assert.Contains("IX_NAZWA", f.Title.Arguments);
        Assert.Contains("NAZWA", f.Title.Arguments);
    }

    [Fact]
    public void R3_Silent_WhenNoExistingIndex_OrTableNotScanned_OrSargable()
    {
        Assert.Empty(new NonSargablePredicateRule().Evaluate(
            Ctx(Access(("T", 2000, 0)), 10, catalog: Catalog(Ix("IX_OTHER", "OTHER", 0.01)),
                sql: "SELECT * FROM T WHERE UPPER(NAZWA) = 'X'")));                    // no index on the column
        Assert.Empty(new NonSargablePredicateRule().Evaluate(
            Ctx(Access(("T", 100, 0)), 10, catalog: Catalog(Ix("IX_NAZWA", "NAZWA", 0.01)),
                sql: "SELECT * FROM T WHERE UPPER(NAZWA) = 'X'")));                    // not scanned (seq < 500)
        Assert.Empty(new NonSargablePredicateRule().Evaluate(
            Ctx(Access(("T", 2000, 0)), 10, catalog: Catalog(Ix("IX_NAZWA", "NAZWA", 0.01)),
                sql: "SELECT * FROM T WHERE NAZWA = 'X'")));                           // sargable (bare)
    }

    // ── R6 high read amplification ───────────────────────────────────────────
    [Fact]
    public void R6_Fires_WhenAmplifiedWithoutDominantScan()
    {
        var f = Assert.Single(new HighReadAmplificationRule().Evaluate(Ctx(Access(("T", 0, 20000)), returned: 285)));
        Assert.Equal(FindingKind.HighReadAmplification, f.Kind);
        Assert.Equal("R6", f.RuleId);
    }

    [Fact]
    public void R6_Silent_WhenSequentialDominates_OrLowAmplification_OrSmallRead()
    {
        Assert.Empty(new HighReadAmplificationRule().Evaluate(Ctx(Access(("T", 20000, 0)), 285)));  // scan dominates → R1
        Assert.Empty(new HighReadAmplificationRule().Evaluate(Ctx(Access(("T", 0, 20000)), 20000))); // amp 1×
        Assert.Empty(new HighReadAmplificationRule().Evaluate(Ctx(Access(("T", 0, 1000)), 10)));      // read < 5000
    }

    // ── R5 stale statistics ──────────────────────────────────────────────────
    [Fact]
    public void R5_Fires_WhenAccessedTableHasIndexWithoutStatistics()
    {
        var ctx = Ctx(Access(("T", 0, 2000)), returned: 100, catalog: Catalog(Ix("IX_T", "C", selectivity: null)));
        var f = Assert.Single(new StaleStatisticsRule().Evaluate(ctx));
        Assert.Equal(FindingKind.StaleStatistics, f.Kind);
        Assert.Contains("IX_T", f.Explanation!.Arguments);
    }

    [Fact]
    public void R5_Silent_WhenStatsPresent_OrBelowReadFloor_OrNoCatalog()
    {
        Assert.Empty(new StaleStatisticsRule().Evaluate(
            Ctx(Access(("T", 0, 2000)), 100, catalog: Catalog(Ix("IX_T", "C", 0.1)))));   // stats present
        Assert.Empty(new StaleStatisticsRule().Evaluate(
            Ctx(Access(("T", 0, 100)), 100, catalog: Catalog(Ix("IX_T", "C", null)))));   // below read floor
        Assert.Empty(new StaleStatisticsRule().Evaluate(Ctx(Access(("T", 0, 2000)), 100)));// no catalog
    }

    // ── Engine integration ───────────────────────────────────────────────────
    [Fact]
    public void Engine_RunsAllRules_OrdersMostSevereFirst()
    {
        // A costly scan (R1 High) + a stale-stats index (R5 Low) on the same table.
        var ctx = Ctx(Access(("T", 60000, 0)), returned: 285,
            catalog: Catalog(Ix("IX_T", "C", selectivity: null)));
        var findings = new PerformanceRuleEngine().Evaluate(ctx);
        Assert.True(findings.Count >= 2);
        Assert.Equal(FindingSeverity.High, findings[0].Severity);       // R1 first
        Assert.Contains(findings, f => f.RuleId == "R1");
        Assert.Contains(findings, f => f.RuleId == "R5");
    }

    [Fact]
    public void R1_AddsPercentScanned_WhenCatalogCardinalityKnown()
    {
        var capture = new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT * FROM INVOICE" },
            RowsReturned = 285,
            TableReads = new[] { new PerTableReadRow("INVOICE", 100_000, 0) },
            Method = CaptureMethod.MonAttachmentDelta,
        };
        var catalog = new CatalogModel
        {
            Tables = new List<TableCatalogInfo> { new() { Table = "INVOICE", RowCountEstimate = 200_000 } },
        };
        var report = new PerformanceReportBuilder().Build(capture, catalog);
        var scan = Assert.Single(report.Findings, f => f.Kind == FindingKind.CostlyFullScan);
        Assert.Contains(scan.Evidence, e => e.Label == PerfMessages.EvidencePercentOfTableScanned);
    }
}
