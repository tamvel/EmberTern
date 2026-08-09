using System.Collections.Generic;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Performance;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The plan tree's colouring — driven entirely by the classification <see cref="PlanNode"/> already carries.
///
/// <para>⭐⭐ The load-bearing test here is <see cref="EverySegmentation_ReproducesTheRawTextExactly"/>. The
/// others describe intent; that one is a correctness net. Colouring works by SPLITTING the engine's own text,
/// so if a split ever loses, reorders or invents a character, the user reads a plan the server did not
/// print — a worse outcome than no colour at all, and one that no screenshot would reveal.</para>
///
/// <para>⚠ These are pure tests: <c>PlanTextSegments</c> returns resource KEYS, never brushes, so nothing
/// here needs a headless Avalonia session. That is a property of the design (architecture rule #1), not a
/// convenience — and it keeps the fragile headless partition from growing.</para>
///
/// <para>⛔ SCOPE: only the object NAME and a FULL SCAN carry colour. A follow-up variant that also
/// differentiated access methods and receded the <c>Table</c>/<c>Index</c> verbs was built, rendered and
/// withdrawn — the two neutral text levels are only 1,78:1 apart in Dark. Do not re-add tests for it.</para>
/// </summary>
public class PlanTextSegmentTests
{
    /// <summary>Real descriptor lines, taken from an Explain plan of the shape the Performance panel shows.</summary>
    public static TheoryData<string> RealPlanLines() =>
    [
        "Select Expression",
        "Sort (record length: 860, key length: 8)",
        "Unique Sort (record length: 2412, key length: 1636)",
        "Filter",
        "Nested Loop Join (outer)",
        "Nested Loop Join (inner)",
        "Table \"TECHNOLOGIA\" as \"T\" Access By ID",
        "Table \"KARTOTEKA\" as \"PR\" Access By ID",
        "Index \"MK_TECHNOLOGIA_STATUS\" Range Scan (full match)",
        "Index \"PK_KARTOTEKA\" Unique Scan",
        "Bitmap",
        "Bitmap Or",
        "Table \"ORDERS\" Full Scan",
        "Procedure \"SP_REPORT\" Scan",
        "",
        "something the parser has never seen",
    ];

    /// <summary>
    /// ⭐⭐ THE INVARIANT: concatenating the segments reproduces the node's raw text, byte for byte.
    /// Everything else about the colouring is a preference; this is the part that must not be wrong.
    /// </summary>
    [Theory]
    [MemberData(nameof(RealPlanLines))]
    public void EverySegmentation_ReproducesTheRawTextExactly(string rawText)
    {
        foreach (var node in NodesFor(rawText))
        {
            var joined = string.Concat(PlanTextSegments.Build(node).Select(s => s.Text));
            Assert.Equal(node.RawText, joined);
        }
    }

    /// <summary>A table node paints its NAME with the table kind's colour — the same key the Metadata
    /// Explorer uses, so the two surfaces cannot drift apart.</summary>
    [Fact]
    public void ATableNode_PaintsItsNameWithTheTableKindColour()
    {
        var node = Node("Table \"TECHNOLOGIA\" as \"T\" Access By ID",
            AccessMethod.AccessById, tableName: "TECHNOLOGIA", detail: "Access By ID");

        var segments = PlanTextSegments.Build(node);
        var name = Assert.Single(segments, s => s.Text.Contains("TECHNOLOGIA"));

        Assert.Equal("IconColor_Table", name.BrushKey);
        Assert.Equal("\"TECHNOLOGIA\"", name.Text);
    }

    /// <summary>An index node likewise — and the two keys must DIFFER, because telling a table from an index
    /// at a glance is the whole reason to colour a plan.</summary>
    [Fact]
    public void AnIndexNode_PaintsItsNameWithADifferentColourThanATable()
    {
        var index = PlanTextSegments.Build(Node("Index \"MK_STATUS\" Range Scan (full match)",
            AccessMethod.IndexScan, indexName: "MK_STATUS", detail: "Range Scan (full match)"));
        var table = PlanTextSegments.Build(Node("Table \"ORDERS\" Access By ID",
            AccessMethod.AccessById, tableName: "ORDERS", detail: "Access By ID"));

        var indexKey = index.Single(s => s.Text.Contains("MK_STATUS")).BrushKey;
        var tableKey = table.Single(s => s.Text.Contains("ORDERS")).BrushKey;

        Assert.Equal("IconColor_Index", indexKey);
        Assert.NotEqual(tableKey, indexKey);
    }

    /// <summary>The ALIAS is not a second object: it recedes, so exactly one thing in a row reads as a name.</summary>
    [Fact]
    public void AnAlias_DoesNotCompeteWithTheObjectName()
    {
        var segments = PlanTextSegments.Build(Node("Table \"TECHNOLOGIA\" as \"T\" Access By ID",
            AccessMethod.AccessById, tableName: "TECHNOLOGIA", detail: "Access By ID"));

        var alias = Assert.Single(segments, s => s.Text == "\"T\"");
        Assert.Equal(PlanTextSegments.DetailBrushKey, alias.BrushKey);
    }

    /// <summary>The qualifier recedes; the verb keeps the ordinary reading colour. Together with the name
    /// that is three levels, which is what turns a wall of monospace into a scannable structure.</summary>
    [Fact]
    public void TheQualifierRecedes_AndTheVerbDoesNot()
    {
        var segments = PlanTextSegments.Build(Node("Sort (record length: 860, key length: 8)",
            AccessMethod.Sort, detail: "(record length: 860, key length: 8)"));

        Assert.Equal(PlanTextSegments.KeywordBrushKey, segments[0].BrushKey);
        Assert.Equal("Sort ", segments[0].Text);
        Assert.Equal(PlanTextSegments.DetailBrushKey, segments[1].BrushKey);
    }

    /// <summary>
    /// ⛔ A full scan keeps the whole row in the warning colour, undivided.
    /// ⚠ This is a guard against an "improvement" that would make the most important row LESS visible by
    /// splitting it into three quieter colours; it is the one node kind the reader is hunting for.
    /// </summary>
    [Fact]
    public void AFullScan_StaysOneWarningRow()
    {
        var node = Node("Table \"ORDERS\" Full Scan", AccessMethod.FullScan,
            tableName: "ORDERS", detail: "Full Scan");

        var segment = Assert.Single(PlanTextSegments.Build(node));
        Assert.Equal("Table \"ORDERS\" Full Scan", segment.Text);
        Assert.Equal(PlanTextSegments.SequentialScanBrushKey, segment.BrushKey);
    }

    /// <summary>An unrecognised node renders faithfully in one ordinary colour rather than being cut at a
    /// guessed boundary. The parser keeps <c>RawText</c> for exactly this case; the view must not undo that.</summary>
    [Fact]
    public void AnUnrecognisedNode_RendersFaithfullyAndUncoloured()
    {
        var node = Node("something the parser has never seen", AccessMethod.Unknown,
            detail: "something the parser has never seen");

        var segments = PlanTextSegments.Build(node);
        Assert.Equal("something the parser has never seen", string.Concat(segments.Select(s => s.Text)));
        Assert.All(segments, s => Assert.NotEqual("IconColor_Table", s.BrushKey));
    }

    /// <summary>
    /// ⚠ The split derives its boundary from `Detail` being a SUFFIX of the raw text. If that stops holding,
    /// the node must render whole rather than be cut in the wrong place — verified here with a deliberately
    /// inconsistent node, the shape a future parser change could produce.
    /// </summary>
    [Fact]
    public void ADetailThatIsNotASuffix_LeavesTheTextUncut()
    {
        var node = Node("Sort (record length: 860)", AccessMethod.Sort, detail: "something else entirely");

        var segments = PlanTextSegments.Build(node);
        Assert.Equal("Sort (record length: 860)", string.Concat(segments.Select(s => s.Text)));
    }

    /// <summary>
    /// ⛔ THE RAW PLAN IS NOT TOUCHED BY THE COLOURING, AND COPY STILL CARRIES IT VERBATIM.
    ///
    /// <para>The plan is shown twice in that section — as a coloured tree and as the engine's own text —
    /// and only the tree gained colour. The raw block stays a faithful monospace transcript, which is what
    /// makes it the escape hatch when the parser does not recognise a node.</para>
    ///
    /// <para>⚠⚠ MEASURED, AND IT CORRECTS A PREMISE WORTH KNOWING: the Copy button beside the "Raw plan"
    /// label does NOT copy only the raw plan. It copies the expert-drawer payload — timings, capture method,
    /// then the plan under a heading — as its own docstring says. That is pre-existing behaviour, deliberately
    /// left alone here; this test pins the part that matters either way, namely that the raw plan travels
    /// through it <b>unaltered</b>.</para>
    /// </summary>
    [Fact]
    public void TheRawPlanTravelsThroughCopyUnaltered()
    {
        const string raw = "Select Expression\n  -> Table \"ORDERS\" Full Scan";
        var vm = new ExecutionDetailsViewModel(new ExecutionDetails
        {
            RawPlanText = raw,
            PlanDialect = PlanDialect.Explain,
            Method = CaptureMethod.MonAttachmentDelta,
        });

        Assert.Equal(raw, vm.RawPlanText);
        Assert.Contains(raw, vm.CopyText, System.StringComparison.Ordinal);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every plausible classification of one raw line — the invariant must hold for all of them,
    /// including combinations the parser would not actually produce.</summary>
    private static IEnumerable<PlanNode> NodesFor(string rawText)
    {
        yield return Node(rawText, AccessMethod.Unknown);
        yield return Node(rawText, AccessMethod.Sort, detail: "(record length: 860, key length: 8)");
        yield return Node(rawText, AccessMethod.AccessById, tableName: "TECHNOLOGIA", detail: "Access By ID");
        yield return Node(rawText, AccessMethod.IndexScan, indexName: "MK_TECHNOLOGIA_STATUS",
            detail: "Range Scan (full match)");
        yield return Node(rawText, AccessMethod.FullScan, tableName: "ORDERS", detail: "Full Scan");
        yield return Node(rawText, AccessMethod.ProcedureScan, tableName: "SP_REPORT", detail: "Scan");
    }

    private static PlanNode Node(
        string rawText,
        AccessMethod method,
        string? tableName = null,
        string? indexName = null,
        string? detail = null)
        => new()
        {
            Method = method,
            RawText = rawText,
            TableName = tableName,
            IndexName = indexName,
            Detail = detail,
        };
}
