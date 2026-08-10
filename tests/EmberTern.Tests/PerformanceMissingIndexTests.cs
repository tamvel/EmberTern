using System.Collections.Generic;
using EmberTern.App.Localization;
using System.Linq;
using EmberTern.Core.Performance;
using EmberTern.Core.Performance.Rules;
using EmberTern.Core.Diagnostics;
using Xunit;

namespace EmberTern.Tests;

// R2 (missing-index candidate) — biased toward silence. Every gate has a suppression test.
public class PerformanceMissingIndexTests
{
    private static TableAccessProfile Access(long seq, long idx = 0, string table = "T")
        => new()
        {
            Tables = new List<TableAccessStat> { new(table, seq, idx) },
            Method = CaptureMethod.MonAttachmentDelta,
        };

    private static CatalogModel Catalog(long? cardinality, params IndexModel[] indexes)
        => new() { Tables = new List<TableCatalogInfo> { new() { Table = "T", RowCountEstimate = cardinality, Indexes = indexes } } };

    private static PerformanceContext Ctx(TableAccessProfile access, long returned, CatalogModel? catalog, string sql)
    {
        var capture = new PerformanceCapture { Statement = new StatementIdentity { Sql = sql }, RowsReturned = returned };
        return PerformanceContextBuilder.Build(capture, plan: null, access: access, catalog: catalog);
    }

    private static IReadOnlyList<Finding> Run(TableAccessProfile access, long returned, CatalogModel? catalog, string sql)
        => new MissingIndexRule().Evaluate(Ctx(access, returned, catalog, sql));

    private const string CostlyScanSql = "SELECT * FROM T WHERE ID = 5";

    [Fact]
    public void Fires_WhenCostlyScan_SargablePredicate_NoIndex_LargeTable()
    {
        var f = Assert.Single(Run(Access(seq: 50_000), returned: 100, Catalog(500_000), CostlyScanSql));
        Assert.Equal(FindingKind.MissingIndexCandidate, f.Kind);
        Assert.Equal("R2", f.RuleId);
        Assert.Equal(FindingConfidence.Medium, f.Confidence);
        // ⭐ The title composes "{table}.{column}"; asserting the two DATA is stronger than asserting the
        // string they render into, and it survives translation.
        Assert.Contains("T", f.Title.Arguments);
        Assert.Contains("ID", f.Title.Arguments);
        // ⚠ The "no imperative / no DDL" product rule, kept here on the rendered sentence AND widened in C7
        // to every Performance entry in the catalog by NoPerfSentence_UsesImperativeOrDdlVocabulary — this
        // was one finding's worth of coverage for a rule the whole module claims.
        Assert.DoesNotContain("Create", Loc.Format(f.Explanation!));
        Assert.DoesNotContain("Add index", Loc.Format(f.Explanation!));
    }

    [Fact]
    public void Suppressed_ForTinyTable()
        => Assert.Empty(Run(Access(seq: 800), returned: 5, Catalog(800), CostlyScanSql)); // cardinality < 1000

    [Fact]
    public void Suppressed_WhenPlainIndexAlreadyExists()
        => Assert.Empty(Run(Access(seq: 50_000), 100,
            Catalog(500_000, new IndexModel { Name = "IX_ID", Columns = new[] { "ID" } }), CostlyScanSql));

    [Fact]
    public void Suppressed_WhenPartialIndexCoversColumn()
        => Assert.Empty(Run(Access(seq: 50_000), 100,
            Catalog(500_000, new IndexModel { Name = "IX_ID_P", Columns = new[] { "ID" }, Condition = "ID > 0" }), CostlyScanSql));

    [Fact]
    public void Suppressed_WhenExpressionIndexReferencesColumn()
        => Assert.Empty(Run(Access(seq: 50_000), 100,
            Catalog(500_000, new IndexModel { Name = "IX_ID_E", Expression = "UPPER(ID)" }), CostlyScanSql));

    [Fact]
    public void Suppressed_ForNonSargablePredicate()
        => Assert.Empty(Run(Access(seq: 50_000), 100, Catalog(500_000), "SELECT * FROM T WHERE UPPER(ID) = 5"));

    [Fact]
    public void Suppressed_WhenAmplificationLow()
        => Assert.Empty(Run(Access(seq: 50_000), returned: 40_000, Catalog(500_000), CostlyScanSql)); // amp 1.25×

    [Fact]
    public void Suppressed_ForNonSeekableOperator()
        => Assert.Empty(Run(Access(seq: 50_000), 100, Catalog(500_000), "SELECT * FROM T WHERE ID <> 5"));

    [Fact]
    public void Suppressed_WhenBelowSequentialFloor()
        => Assert.Empty(Run(Access(seq: 100), 1, Catalog(500_000), CostlyScanSql)); // seq < 500

    [Fact]
    public void Suppressed_WhenNoCatalog()
        => Assert.Empty(Run(Access(seq: 50_000), 100, catalog: null, CostlyScanSql)); // can't confirm "no index"

    [Fact]
    public void Confidence_Low_WhenCardinalityUnknownButCatalogPresent()
    {
        var f = Assert.Single(Run(Access(seq: 50_000), 100, Catalog(cardinality: null), CostlyScanSql));
        Assert.Equal(FindingConfidence.Low, f.Confidence);
    }

    [Fact]
    public void RangePredicate_AlsoFires()
    {
        var f = Assert.Single(Run(Access(seq: 50_000), 100, Catalog(500_000), "SELECT * FROM T WHERE DATA >= '2020-01-01'"));
        Assert.Contains("DATA", f.Title.Arguments);
    }
}
