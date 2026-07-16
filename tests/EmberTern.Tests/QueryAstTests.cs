using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap 6.9 / B2 — the query clause tree the parser now attaches to a plain (non-<c>WITH</c>)
/// <see cref="SelectStatement"/>: <see cref="SelectQuery"/> / <see cref="SetOperationQuery"/> with their
/// clauses (SELECT/FROM/WHERE/GROUP BY/HAVING/ORDER BY), the FROM list with join structure
/// (<see cref="TableReference"/> / <see cref="DerivedTable"/> / <see cref="JoinedTable"/>), and set
/// operations. Asserts the concrete node shapes and that spans map back to real source. Round-trip
/// byte-identity + tree well-formedness over the corpus are covered by
/// <see cref="StructuralAstDifferentialTests"/>; these tests pin the shapes.
/// </summary>
public class QueryAstTests
{
    private static QueryNode Query(string sql)
    {
        var root = SqlParser.Parse(sql).Root;
        var select = Assert.IsType<SelectStatement>(root.Statements[0]);
        Assert.NotNull(select.Query);
        return select.Query!;
    }

    private static SelectQuery Select(string sql) => Assert.IsType<SelectQuery>(Query(sql));

    // Every node's span must map to a non-empty, in-range slice of the exact source.
    private static void AssertSpansMapToSource(string sql, SqlNode node)
    {
        foreach (var d in node.DescendantNodesAndSelf())
        {
            Assert.True(d.Start >= 0 && d.End <= sql.Length, $"span [{d.Start},{d.End}) outside source len {sql.Length}");
            Assert.True(d.End >= d.Start, $"{d.GetType().Name} has a negative span");
        }
    }

    [Fact]
    public void PlainSelect_ProducesSelectAndFromClauses()
    {
        const string sql = "SELECT a, b FROM t WHERE x = 1 ORDER BY a";
        var q = Select(sql);
        Assert.NotNull(q.Select);
        Assert.NotNull(q.From);
        Assert.NotNull(q.Where);
        Assert.NotNull(q.OrderBy);
        Assert.Null(q.GroupBy);
        Assert.Null(q.Having);
        // Spans are token-precise (end at the last token, excluding trailing whitespace/trivia).
        Assert.Equal("SELECT a, b", sql.Substring(q.Select.Start, q.Select.Length));
        Assert.Equal("WHERE x = 1", sql.Substring(q.Where!.Start, q.Where.Length));
        Assert.Equal("ORDER BY a", sql.Substring(q.OrderBy!.Start, q.OrderBy.Length));
        AssertSpansMapToSource(sql, q);
    }

    [Fact]
    public void FromList_SingleTable_WithAlias()
    {
        var q = Select("SELECT k.id FROM kontrahent k");
        var item = Assert.IsType<TableReference>(q.From!.Items.Single());
        Assert.Equal("kontrahent", item.NameToken!.Text);
        Assert.Equal("k", item.AliasToken!.Text);
    }

    [Fact]
    public void FromList_CommaSeparated_YieldsMultipleItems()
    {
        var q = Select("SELECT * FROM a, b, c");
        Assert.Equal(3, q.From!.Items.Count);
        Assert.All(q.From.Items, i => Assert.IsType<TableReference>(i));
    }

    [Fact]
    public void Join_ProducesJoinedTable_WithOnCondition()
    {
        const string sql = "SELECT n.id FROM nagl n JOIN pozycje p ON p.id_nagl = n.id";
        var q = Select(sql);
        var join = Assert.IsType<JoinedTable>(q.From!.Items.Single());
        Assert.Equal(JoinKind.Inner, join.Kind);
        Assert.Equal("nagl", Assert.IsType<TableReference>(join.Left).NameToken!.Text);
        Assert.Equal("pozycje", Assert.IsType<TableReference>(join.Right).NameToken!.Text);
        Assert.NotNull(join.OnTokens);
    }

    [Fact]
    public void LeftOuterJoin_KindIsLeft()
    {
        var q = Select("SELECT 1 FROM a LEFT OUTER JOIN b ON a.id = b.id");
        Assert.Equal(JoinKind.Left, Assert.IsType<JoinedTable>(q.From!.Items.Single()).Kind);
    }

    [Fact]
    public void ChainedJoins_NestLeftAssociatively()
    {
        var q = Select("SELECT 1 FROM a JOIN b ON a.i = b.i JOIN c ON b.j = c.j");
        var outer = Assert.IsType<JoinedTable>(q.From!.Items.Single());
        Assert.Equal("c", Assert.IsType<TableReference>(outer.Right).NameToken!.Text);
        var inner = Assert.IsType<JoinedTable>(outer.Left); // (a JOIN b) is the left of the outer join
        Assert.Equal("a", Assert.IsType<TableReference>(inner.Left).NameToken!.Text);
        Assert.Equal("b", Assert.IsType<TableReference>(inner.Right).NameToken!.Text);
    }

    [Fact]
    public void GroupByHaving_AreModelled()
    {
        var q = Select("select n.id, count(*) from t n group by n.id having count(*) > 1");
        Assert.NotNull(q.GroupBy);
        Assert.NotNull(q.Having);
        Assert.Null(q.OrderBy);
    }

    [Fact]
    public void DerivedTable_IsRecursedInto_B3()
    {
        var q = Select("SELECT d.n FROM (SELECT id AS n FROM t) d WHERE d.n > 0");
        var derived = Assert.IsType<DerivedTable>(q.From!.Items.Single());
        Assert.Equal("d", derived.AliasToken!.Text);
        // B3: the subquery is a real QueryNode child, recursed into.
        var inner = Assert.IsType<SelectQuery>(derived.Query);
        Assert.NotNull(inner.From);
        Assert.Equal("t", Assert.IsType<TableReference>(inner.From!.Items.Single()).NameToken!.Text);
    }

    [Fact]
    public void Exists_ProducesExistsExpression_WithQueryChild()
    {
        var q = Select("SELECT * FROM t WHERE EXISTS (SELECT 1 FROM u WHERE u.tid = t.id)");
        var exists = Assert.Single(q.Where!.Children.OfType<ExistsExpression>());
        var inner = Assert.IsType<SelectQuery>(exists.Query);
        Assert.Equal("u", Assert.IsType<TableReference>(inner.From!.Items.Single()).NameToken!.Text);
    }

    [Fact]
    public void ScalarSubquery_InProjection_ProducesScalarSubquery()
    {
        var q = Select("SELECT id, (SELECT COUNT(*) FROM u WHERE u.tid = t.id) AS cnt FROM t");
        var scalar = Assert.Single(q.Select.Children.OfType<ScalarSubquery>());
        Assert.IsType<SelectQuery>(scalar.Query);
    }

    [Fact]
    public void ScalarSubquery_InWhere_IsNotMisreadAsExists()
    {
        var q = Select("SELECT * FROM t WHERE x = (SELECT MAX(y) FROM u)");
        Assert.Empty(q.Where!.Children.OfType<ExistsExpression>());
        Assert.Single(q.Where.Children.OfType<ScalarSubquery>());
    }

    [Fact]
    public void SubqueryNestedInFunctionArg_IsStillFound()
    {
        // The subquery lives inside COALESCE(...)'s parens — the scan descends through ordinary parens.
        var q = Select("SELECT COALESCE((SELECT MAX(y) FROM u), 0) AS m FROM t");
        Assert.Single(q.Select.Children.OfType<ScalarSubquery>());
    }

    [Fact]
    public void SetOperation_UnionAll_WithTrailingOrderBy()
    {
        const string sql = "SELECT a FROM t UNION ALL SELECT a FROM u ORDER BY 1";
        var setop = Assert.IsType<SetOperationQuery>(Query(sql));
        Assert.Equal(SetOperator.Union, setop.Operator);
        Assert.True(setop.All);
        Assert.IsType<SelectQuery>(setop.Left);
        Assert.IsType<SelectQuery>(setop.Right);
        // The trailing ORDER BY belongs to the whole set operation, not the right operand.
        Assert.NotNull(setop.OrderBy);
        Assert.Null(Assert.IsType<SelectQuery>(setop.Right).OrderBy);
        AssertSpansMapToSource(sql, setop);
    }

    [Fact]
    public void ChainedSetOperations_NestLeftAssociatively()
    {
        var top = Assert.IsType<SetOperationQuery>(Query("SELECT a FROM t UNION SELECT a FROM u EXCEPT SELECT a FROM v"));
        Assert.Equal(SetOperator.Except, top.Operator);
        Assert.IsType<SelectQuery>(top.Right);
        Assert.IsType<SetOperationQuery>(top.Left); // (t UNION u) is the left of the EXCEPT
    }

    [Fact]
    public void WithLedQuery_IsAWithQuery_WithRealBodyAndMainQueries()
    {
        // B3: a WITH-led query is a WithQuery whose CTE bodies and main query are real QueryNodes.
        var select = Assert.IsType<SelectStatement>(SqlParser.Parse("WITH c AS (SELECT 1 AS n FROM t) SELECT n FROM c").Root.Statements[0]);
        var wq = Assert.IsType<WithQuery>(select.Query);
        Assert.IsType<SelectQuery>(Assert.Single(wq.With.Ctes).Body);
        Assert.IsType<SelectQuery>(wq.Query);
    }

    [Fact]
    public void FromLessSelect_HasNullFrom()
    {
        var q = Select("SELECT 1 + 2");
        Assert.Null(q.From);
        Assert.NotNull(q.Select);
    }
}
