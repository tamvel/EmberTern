using System;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The §0 differential harness for Etap 6.9 (Structural AST Deepening). Its job is to guard every
/// later milestone (B1–B5): as the parser is deepened to emit real query / PSQL / expression nodes,
/// these invariants MUST keep holding, or a deepening step silently broke something.
/// <list type="bullet">
/// <item><b>Round-trip byte-identity</b> — <c>Parse(x).Root.ToSourceString() == x</c> (strict AND
/// lenient). This is the machine-checkable §0 (Paramount Law) guarantee: the token stream reproduces
/// the source exactly, independent of how deeply the tree models it. If a future deepening ever drops,
/// reorders, or rewrites a token, this fails.</item>
/// <item><b>Tree well-formedness</b> — every child node's span nests inside its parent's, children are
/// in non-decreasing source order, and the whole tree enumerates without throwing. Trivially true for
/// the shallow B0 tree; it gains teeth the moment B1/B2/B3 start attaching <see cref="QueryNode"/> /
/// <see cref="PsqlStatement"/> children.</item>
/// <item><b>Extension-point contract</b> — the new abstractions exist with the intended shape, so B1+
/// can build on them without surprise.</item>
/// </list>
/// The formatter's own §0 invariants (idempotency, token/comment preservation) stay in
/// <see cref="SqlFormatterInvariantsTests"/>; both suites draw from <see cref="SqlTestCorpus"/>.
/// </summary>
public class StructuralAstDifferentialTests
{
    [Theory]
    [MemberData(nameof(SqlTestCorpus.AllData), MemberType = typeof(SqlTestCorpus))]
    public void Parse_Strict_RoundTripsByteForByte(string sql)
        => Assert.Equal(sql, SqlParser.Parse(sql).Root.ToSourceString());

    [Theory]
    [MemberData(nameof(SqlTestCorpus.AllData), MemberType = typeof(SqlTestCorpus))]
    public void Parse_Lenient_RoundTripsByteForByte(string sql)
        => Assert.Equal(sql, SqlParser.Parse(sql, lenient: true).Root.ToSourceString());

    [Theory]
    [MemberData(nameof(SqlTestCorpus.AllData), MemberType = typeof(SqlTestCorpus))]
    public void Ast_IsWellFormed_Strict(string sql)
        => AssertWellFormed(SqlParser.Parse(sql).Root);

    [Theory]
    [MemberData(nameof(SqlTestCorpus.AllData), MemberType = typeof(SqlTestCorpus))]
    public void Ast_IsWellFormed_Lenient(string sql)
        => AssertWellFormed(SqlParser.Parse(sql, lenient: true).Root);

    // Every child span must nest within its parent and appear in non-decreasing source order; the
    // whole tree must enumerate without throwing. This is the structural contract every deepened node
    // (QueryNode / PsqlStatement subtrees added in B1+) has to satisfy.
    private static void AssertWellFormed(SqlNode node)
    {
        int prevStart = int.MinValue;
        foreach (var child in node.Children)
        {
            Assert.True(
                child.Start >= node.Start && child.End <= node.End,
                $"child span [{child.Start},{child.End}) escapes parent [{node.Start},{node.End}) ({child.GetType().Name} in {node.GetType().Name})");
            Assert.True(
                child.Start >= prevStart,
                $"children out of source order in {node.GetType().Name} (child {child.GetType().Name} at {child.Start} after {prevStart})");
            prevStart = child.Start;
            AssertWellFormed(child);
        }

        // NodeAt / Descendants must never throw on any node in the tree.
        _ = node.NodeAt(node.Start);
        foreach (var _ in node.DescendantNodesAndSelf()) { }
    }

    // ── Extension-point contract (Etap 6.9 / B0) ────────────────────────────────────────────────

    [Fact]
    public void QueryNode_IsAbstractSqlNode()
    {
        Assert.True(typeof(QueryNode).IsAbstract);
        Assert.True(typeof(SqlNode).IsAssignableFrom(typeof(QueryNode)));
    }

    [Fact]
    public void PsqlStatement_IsAbstractSqlNode()
    {
        Assert.True(typeof(PsqlStatement).IsAbstract);
        Assert.True(typeof(SqlNode).IsAssignableFrom(typeof(PsqlStatement)));
    }

    [Fact]
    public void ExecutableStatement_IsAnInterface_ExposingSpan()
    {
        Assert.True(typeof(IExecutableStatement).IsInterface);
        // The span members exist so a debugger/consumer can read a step's location through the marker.
        Assert.NotNull(typeof(IExecutableStatement).GetProperty(nameof(IExecutableStatement.Start)));
        Assert.NotNull(typeof(IExecutableStatement).GetProperty(nameof(IExecutableStatement.Length)));
        Assert.NotNull(typeof(IExecutableStatement).GetProperty(nameof(IExecutableStatement.End)));
    }
}
