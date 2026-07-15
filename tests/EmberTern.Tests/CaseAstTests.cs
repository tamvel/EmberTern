using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap 6.9 / B4 — <c>CASE … END</c> is a real <see cref="CaseExpression"/> node (simple + searched,
/// in a SELECT expression and in a PSQL statement), with its <see cref="WhenClause"/> arms, and any
/// query / nested CASE inside an arm reachable as a real child. Round-trip byte-identity + tree
/// well-formedness over the corpus are covered by <see cref="StructuralAstDifferentialTests"/>; these
/// pin the shapes. (Formatter CASE layout is a later convergence step — B4 is producer-only.)
/// </summary>
public class CaseAstTests
{
    private static SqlStatement First(string sql) => SqlParser.Parse(sql).Root.Statements[0];

    private static CaseExpression FirstCaseIn(SqlNode node)
        => node.DescendantNodesAndSelf().OfType<CaseExpression>().First();

    [Fact]
    public void SearchedCase_InProjection_IsModelled()
    {
        var sel = Assert.IsType<SelectStatement>(First("SELECT CASE WHEN x > 0 THEN 'p' WHEN x < 0 THEN 'n' ELSE 'z' END AS s FROM t"));
        var kase = FirstCaseIn(sel);
        Assert.True(kase.IsSearched);
        Assert.Equal(2, kase.Whens.Count);
        Assert.Contains("CASE", kase.Tokens.Select(t => t.Text.ToUpperInvariant()));
        Assert.Contains("END", kase.Tokens.Select(t => t.Text.ToUpperInvariant()));
    }

    [Fact]
    public void SimpleCase_HasOperand_IsNotSearched()
    {
        var sel = Assert.IsType<SelectStatement>(First("SELECT CASE x WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE '?' END AS s FROM t"));
        var kase = FirstCaseIn(sel);
        Assert.False(kase.IsSearched);
        Assert.Equal(2, kase.Whens.Count);
    }

    [Fact]
    public void CaseWithoutElse_HasNoElseButStillModelled()
    {
        var sel = Assert.IsType<SelectStatement>(First("SELECT CASE WHEN x THEN 1 END AS s FROM t"));
        var kase = FirstCaseIn(sel);
        Assert.Single(kase.Whens);
    }

    [Fact]
    public void ScalarSubqueryInsideCaseBranch_IsReachableAsQueryNode()
    {
        var sel = Assert.IsType<SelectStatement>(First("SELECT CASE WHEN x > 0 THEN (SELECT MAX(y) FROM u WHERE u.k = t.id) ELSE 0 END AS m FROM t"));
        var kase = FirstCaseIn(sel);
        // The subquery is a child of the WHEN arm (its THEN result), owning a real QueryNode.
        var scalar = kase.DescendantNodes().OfType<ScalarSubquery>().Single();
        Assert.IsType<SelectQuery>(scalar.Query);
        Assert.Equal("u", Assert.IsType<TableReference>(Assert.IsType<SelectQuery>(scalar.Query).From!.Items.Single()).NameToken!.Text);
    }

    [Fact]
    public void NestedCase_IsRecursed()
    {
        var sel = Assert.IsType<SelectStatement>(First("SELECT CASE WHEN a THEN CASE WHEN b THEN 1 ELSE 2 END ELSE 3 END AS n FROM t"));
        var outer = sel.Query!.DescendantNodesAndSelf().OfType<CaseExpression>().First();
        // The outer CASE contains an inner CASE inside its first WHEN arm.
        var inner = outer.Whens[0].DescendantNodes().OfType<CaseExpression>().Single();
        Assert.NotSame(outer, inner);
        Assert.Single(inner.Whens);
    }

    [Fact]
    public void Case_InWhereClause_IsAttachedToWhere()
    {
        var sel = Assert.IsType<SelectStatement>(First("SELECT * FROM t WHERE CASE WHEN a THEN 1 ELSE 0 END = 1"));
        var q = Assert.IsType<SelectQuery>(sel.Query);
        Assert.Single(q.Where!.Children.OfType<CaseExpression>());
    }

    [Fact]
    public void Case_InUpdateSet_IsReachable()
    {
        var upd = Assert.IsType<UpdateStatement>(First("UPDATE t SET s = CASE WHEN x IS NULL THEN 0 ELSE x END WHERE id = 1"));
        Assert.Single(upd.Subqueries.OfType<CaseExpression>());
    }

    [Fact]
    public void Case_InPsqlAssignment_IsReachable()
    {
        var body = Assert.IsType<AnonymousBlockStatement>(First("begin v = case when a > 0 then 1 else 0 end; end")).Body!;
        var leaf = Assert.IsType<PsqlLeafStatement>(body.Statements.Single());
        Assert.Equal(PsqlLeafKind.Assignment, leaf.Kind);
        Assert.Single(leaf.Children.OfType<CaseExpression>());
    }

    [Fact]
    public void MalformedCase_WithoutEnd_StaysLosslessAndUnmodelled()
    {
        // No matching END → the CASE is not turned into a node, but the round-trip is exact (§0).
        const string sql = "SELECT CASE WHEN x THEN 1 FROM t";
        var root = SqlParser.Parse(sql).Root;
        Assert.Equal(sql, root.ToSourceString());
    }
}
