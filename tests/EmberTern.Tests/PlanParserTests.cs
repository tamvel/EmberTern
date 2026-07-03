using System.Linq;
using EmberTern.Core.Performance;
using Xunit;

namespace EmberTern.Tests;

public class PlanParserTests
{
    private static PlanTree ParseExplain(string text)
        => new PlanParser().Parse(new RawPlanCapture(PlanDialect.Explain, text));

    private static PlanTree ParseLegacy(string text)
        => new PlanParser().Parse(new RawPlanCapture(PlanDialect.Legacy, text));

    [Fact]
    public void Empty_ProducesEmptyTree()
    {
        var tree = ParseExplain(string.Empty);
        Assert.Empty(tree.Roots);
        Assert.Equal(PlanDialect.Explain, tree.Dialect);
    }

    [Fact]
    public void SingleFullScan_UnderAggregate()
    {
        // From the book: SELECT COUNT(*) FROM HORSE
        var tree = ParseExplain(
            "Select Expression\n" +
            "    -> Aggregate\n" +
            "        -> Table \"HORSE\" Full Scan\n");

        var root = Assert.Single(tree.Roots);
        Assert.Equal(AccessMethod.SelectExpression, root.Method);
        var agg = Assert.Single(root.Children);
        Assert.Equal(AccessMethod.Aggregate, agg.Method);
        var scan = Assert.Single(agg.Children);
        Assert.Equal(AccessMethod.FullScan, scan.Method);
        Assert.Equal("HORSE", scan.TableName);
        Assert.True(scan.IsSequentialScan);
    }

    [Fact]
    public void RealFb5Shape_AccessByIdBitmapIndex()
    {
        // Captured verbatim from FB 5.0.3 this design cycle.
        var tree = ParseExplain(
            "Select Expression\n" +
            "    -> Filter\n" +
            "        -> Table \"RDB$RELATIONS\" Access By ID\n" +
            "            -> Bitmap\n" +
            "                -> Index \"RDB$INDEX_1\" Range Scan (lower bound: 1/1)\n");

        var root = Assert.Single(tree.Roots);
        var filter = Assert.Single(root.Children);
        Assert.Equal(AccessMethod.Filter, filter.Method);
        var table = Assert.Single(filter.Children);
        Assert.Equal(AccessMethod.AccessById, table.Method);
        Assert.Equal("RDB$RELATIONS", table.TableName);
        Assert.False(table.IsSequentialScan);
        var bitmap = Assert.Single(table.Children);
        Assert.Equal(AccessMethod.Bitmap, bitmap.Method);
        var index = Assert.Single(bitmap.Children);
        Assert.Equal(AccessMethod.IndexScan, index.Method);
        Assert.Equal("RDB$INDEX_1", index.IndexName);
        Assert.Contains("Range Scan", index.Detail);
    }

    [Fact]
    public void NestedLoopJoin_HasThreeChildStreams()
    {
        var tree = ParseExplain(
            "Select Expression\n" +
            "    -> Nested Loop Join (inner)\n" +
            "        -> Table \"PROJECT\" Full Scan\n" +
            "        -> Filter\n" +
            "            -> Table \"EMPLOYEE_PROJECT\" Access By ID\n" +
            "                -> Bitmap\n" +
            "                    -> Index \"RDB$FOREIGN16\" Range Scan (full match)\n" +
            "        -> Filter\n" +
            "            -> Table \"EMPLOYEE\" Access By ID\n" +
            "                -> Bitmap\n" +
            "                    -> Index \"RDB$PRIMARY7\" Unique Scan\n");

        var join = Assert.Single(tree.Roots).Children.Single();
        Assert.Equal(AccessMethod.NestedLoopJoin, join.Method);
        Assert.Equal(3, join.Children.Count);
        Assert.Equal(AccessMethod.FullScan, join.Children[0].Method);
        Assert.Equal("PROJECT", join.Children[0].TableName);
        Assert.Equal(AccessMethod.Filter, join.Children[1].Method);
        Assert.Equal(AccessMethod.Filter, join.Children[2].Method);

        // Exactly one sequential scan in the whole plan (PROJECT).
        Assert.Single(tree.EnumerateNodes(), n => n.IsSequentialScan);
        // Unique scan detail preserved on the deepest index node.
        var unique = tree.EnumerateNodes().Single(n => n.IndexName == "RDB$PRIMARY7");
        Assert.Contains("Unique Scan", unique.Detail);
    }

    [Fact]
    public void HashJoin_And_RecordBuffer_Recognized()
    {
        var tree = ParseExplain(
            "Select Expression\n" +
            "    -> Hash Join (inner)\n" +
            "        -> Table \"A\" Full Scan\n" +
            "        -> Record Buffer (record length: 33)\n" +
            "            -> Table \"B\" Full Scan\n");

        var join = Assert.Single(tree.Roots).Children.Single();
        Assert.Equal(AccessMethod.HashJoin, join.Method);
        Assert.Equal(AccessMethod.RecordBuffer, join.Children[1].Method);
        Assert.Equal(2, tree.EnumerateNodes().Count(n => n.IsSequentialScan));
    }

    [Fact]
    public void Alias_IsExtracted()
    {
        var tree = ParseExplain(
            "Select Expression\n" +
            "    -> Table \"RDB$RELATIONS\" as \"R\" Full Scan\n");

        var scan = tree.Roots.Single().Children.Single();
        Assert.Equal("RDB$RELATIONS", scan.TableName);
        Assert.Equal("R", scan.Alias);
        Assert.Equal(AccessMethod.FullScan, scan.Method);
    }

    [Fact]
    public void MultipleRoots_ProcedureCursors()
    {
        var tree = ParseExplain(
            "Select Expression (line 10, column 6)\n" +
            "    -> Procedure \"SHOW_LANGS\" Scan\n" +
            "Select Expression (line 5, column 2)\n" +
            "    -> Table \"JOB\" Full Scan\n");

        Assert.Equal(2, tree.Roots.Count);
        Assert.Contains("line 10", tree.Roots[0].Detail);
        var proc = tree.Roots[0].Children.Single();
        Assert.Equal(AccessMethod.ProcedureScan, proc.Method);
        Assert.Equal("SHOW_LANGS", proc.TableName);
        Assert.Equal(AccessMethod.FullScan, tree.Roots[1].Children.Single().Method);
    }

    [Fact]
    public void Fb6MetricsLine_AttachesToNextNode()
    {
        // Forward-compat: FB6 prints the bracket line immediately before its node.
        var tree = ParseExplain(
            "Select Expression\n" +
            "    [cardinality=120.0, cost=121.5]\n" +
            "    -> Table \"RDB$RELATIONS\" Full Scan\n");

        var scan = tree.Roots.Single().Children.Single();
        Assert.NotNull(scan.Metrics);
        Assert.Equal(120.0, scan.Metrics!.Cardinality);
        Assert.Equal(121.5, scan.Metrics!.Cost);
    }

    [Fact]
    public void SupportedEngines_HaveNoMetrics()
    {
        var tree = ParseExplain(
            "Select Expression\n" +
            "    -> Table \"HORSE\" Full Scan\n");
        Assert.All(tree.EnumerateNodes(), n => Assert.Null(n.Metrics));
    }

    [Fact]
    public void UnknownNode_IsTolerated_RawTextPreserved()
    {
        var tree = ParseExplain(
            "Select Expression\n" +
            "    -> Some Future Node Type (weird)\n");

        var node = tree.Roots.Single().Children.Single();
        Assert.Equal(AccessMethod.Unknown, node.Method);
        Assert.Equal("Some Future Node Type (weird)", node.RawText);
    }

    [Fact]
    public void Legacy_Natural_ProducesFullScanLeaf()
    {
        var tree = ParseLegacy("PLAN (HORSE NATURAL)");
        Assert.Equal(PlanDialect.Legacy, tree.Dialect);
        var leaf = tree.Roots.Single().Children.Single();
        Assert.Equal(AccessMethod.FullScan, leaf.Method);
        Assert.Equal("HORSE", leaf.TableName);
        Assert.True(leaf.IsSequentialScan);
    }

    [Fact]
    public void Legacy_Join_MixedAccess()
    {
        var tree = ParseLegacy("PLAN JOIN (PROJECT NATURAL, EMPLOYEE INDEX (RDB$PRIMARY7))");
        var leaves = tree.Roots.Single().Children;
        Assert.Equal(2, leaves.Count);
        Assert.Equal(AccessMethod.FullScan, leaves[0].Method);
        Assert.Equal("PROJECT", leaves[0].TableName);
        Assert.Equal(AccessMethod.IndexScan, leaves[1].Method);
        Assert.Equal("EMPLOYEE", leaves[1].TableName);
        Assert.Equal("RDB$PRIMARY7", leaves[1].IndexName);
        Assert.Single(tree.EnumerateNodes(), n => n.IsSequentialScan);
    }

    [Fact]
    public void RawText_IsRetainedOnTree()
    {
        const string plan = "Select Expression\n    -> Table \"HORSE\" Full Scan\n";
        Assert.Equal(plan, ParseExplain(plan).RawText);
    }
}
