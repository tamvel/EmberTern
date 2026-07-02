using System.Linq;
using EmberTern.Core.Query;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Faza 2 — SQL push-down builder for server-paged grids. Pins the parameterized
// WHERE + aggregate shapes and the Firebird operator mapping.
public class FirebirdGridSqlBuilderTests
{
    private static readonly QueryColumn[] Cols =
    {
        new("AMOUNT", typeof(decimal)),
        new("NAME", typeof(string)),
        new("CREATED", typeof(System.DateTime)),
    };

    private static GridFilter One(int col, string name, GridFilterOperator op, string? val, GridFilterCombine combine = GridFilterCombine.And)
        => new(new[] { new GridFilterCondition(col, name, op, val) }, combine);

    [Fact]
    public void EmptyFilter_ProducesEmptyClause()
    {
        var f = FirebirdGridSqlBuilder.BuildWhere(GridFilter.Empty, Cols);
        Assert.False(f.HasClause);
        Assert.Empty(f.Parameters);
    }

    [Fact]
    public void Equals_Numeric_ParameterizedAndTyped()
    {
        var f = FirebirdGridSqlBuilder.BuildWhere(One(0, "AMOUNT", GridFilterOperator.GreaterThan, "1000"), Cols);
        Assert.Equal("\"AMOUNT\" > @p0", f.WhereClause);
        var p = Assert.Single(f.Parameters);
        Assert.Equal("@p0", p.Name);
        Assert.Equal(1000m, p.Value);
    }

    [Fact]
    public void Contains_UsesContaining()
    {
        var f = FirebirdGridSqlBuilder.BuildWhere(One(1, "NAME", GridFilterOperator.Contains, "acme"), Cols);
        Assert.Equal("\"NAME\" CONTAINING @p0", f.WhereClause);
        Assert.Equal("acme", f.Parameters[0].Value);
    }

    [Fact]
    public void StartsWith_UsesStartingWith()
    {
        var f = FirebirdGridSqlBuilder.BuildWhere(One(1, "NAME", GridFilterOperator.StartsWith, "AC"), Cols);
        Assert.Equal("\"NAME\" STARTING WITH @p0", f.WhereClause);
        Assert.Equal("AC", f.Parameters[0].Value);
    }

    [Fact]
    public void EndsWith_UsesLikeWithEscapedPercentPrefix()
    {
        var f = FirebirdGridSqlBuilder.BuildWhere(One(1, "NAME", GridFilterOperator.EndsWith, "50%_x"), Cols);
        Assert.Equal("\"NAME\" LIKE @p0 ESCAPE '\\'", f.WhereClause);
        // Leading % is the wildcard; the operand's own %, _ are escaped.
        Assert.Equal("%50\\%\\_x", f.Parameters[0].Value);
    }

    [Fact]
    public void IsNull_And_IsNotNull_TakeNoParameter()
    {
        var n = FirebirdGridSqlBuilder.BuildWhere(One(1, "NAME", GridFilterOperator.IsNull, null), Cols);
        Assert.Equal("\"NAME\" IS NULL", n.WhereClause);
        Assert.Empty(n.Parameters);

        var nn = FirebirdGridSqlBuilder.BuildWhere(One(1, "NAME", GridFilterOperator.IsNotNull, null), Cols);
        Assert.Equal("\"NAME\" IS NOT NULL", nn.WhereClause);
    }

    [Fact]
    public void MultipleConditions_JoinedByCombine_WithDistinctParams()
    {
        var and = new GridFilter(new[]
        {
            new GridFilterCondition(1, "NAME", GridFilterOperator.Equals, "ACME"),
            new GridFilterCondition(0, "AMOUNT", GridFilterOperator.GreaterThan, "1000"),
        }, GridFilterCombine.And);
        var f = FirebirdGridSqlBuilder.BuildWhere(and, Cols);
        Assert.Equal("\"NAME\" = @p0 AND \"AMOUNT\" > @p1", f.WhereClause);
        Assert.Equal(2, f.Parameters.Count);

        var or = FirebirdGridSqlBuilder.BuildWhere(new GridFilter(and.Conditions, GridFilterCombine.Or), Cols);
        Assert.Equal("\"NAME\" = @p0 OR \"AMOUNT\" > @p1", or.WhereClause);
    }

    [Fact]
    public void UnconvertibleComparisonValue_IsSkipped()
    {
        // "abc" is not numeric → the AMOUNT comparison is dropped, leaving only NAME.
        var flt = new GridFilter(new[]
        {
            new GridFilterCondition(0, "AMOUNT", GridFilterOperator.Equals, "abc"),
            new GridFilterCondition(1, "NAME", GridFilterOperator.Equals, "X"),
        }, GridFilterCombine.And);
        var f = FirebirdGridSqlBuilder.BuildWhere(flt, Cols);
        Assert.Equal("\"NAME\" = @p0", f.WhereClause);
        Assert.Single(f.Parameters);
    }

    [Fact]
    public void QuotesInIdentifier_AreDoubled()
    {
        var cols = new[] { new QueryColumn("WEIRD\"COL", typeof(int)) };
        var f = FirebirdGridSqlBuilder.BuildWhere(One(0, "WEIRD\"COL", GridFilterOperator.Equals, "5"), cols);
        Assert.Equal("\"WEIRD\"\"COL\" = @p0", f.WhereClause);
    }

    [Theory]
    [InlineData(GridAggregate.Sum, "SUM(\"AMOUNT\")")]
    [InlineData(GridAggregate.Avg, "AVG(\"AMOUNT\")")]
    [InlineData(GridAggregate.Min, "MIN(\"AMOUNT\")")]
    [InlineData(GridAggregate.Max, "MAX(\"AMOUNT\")")]
    [InlineData(GridAggregate.Count, "COUNT(\"AMOUNT\")")]
    [InlineData(GridAggregate.CountDistinct, "COUNT(DISTINCT \"AMOUNT\")")]
    public void AggregateExpression_Shapes(GridAggregate agg, string expected)
        => Assert.Equal(expected, FirebirdGridSqlBuilder.AggregateExpression(agg, "AMOUNT"));

    // ── Reader SQL shape (internal statics) ───────────────────────────────
    [Fact]
    public void BuildDataPreviewSql_WithWhere_InsertsBeforeOrderByAndRows()
    {
        var sql = FirebirdTableDetailReader.BuildDataPreviewSql("T", 1, 200, "\"ID\" ASC", "\"STATUS\" = @p0");
        Assert.Equal("SELECT * FROM \"T\" WHERE \"STATUS\" = @p0 ORDER BY \"ID\" ASC ROWS 1 TO 200", sql);
    }

    [Fact]
    public void BuildDataPreviewSql_NoWhere_Unchanged()
    {
        var sql = FirebirdTableDetailReader.BuildDataPreviewSql("T", 1, 200, null);
        Assert.Equal("SELECT * FROM \"T\" ROWS 1 TO 200", sql);
    }

    [Fact]
    public void BuildRowCountSql_WithWhere()
    {
        var sql = FirebirdTableDetailReader.BuildRowCountSql("T", 50000, "\"STATUS\" = @p0");
        Assert.Equal("SELECT COUNT(*) FROM (SELECT FIRST 50000 1 AS X FROM \"T\" WHERE \"STATUS\" = @p0) sub", sql);
    }

    [Fact]
    public void BuildAggregateSql_WithAndWithoutWhere()
    {
        Assert.Equal("SELECT SUM(\"AMOUNT\") FROM \"T\"",
            FirebirdTableDetailReader.BuildAggregateSql("T", "AMOUNT", GridAggregate.Sum, null));
        Assert.Equal("SELECT COUNT(DISTINCT \"STATUS\") FROM \"T\" WHERE \"X\" = @p0",
            FirebirdTableDetailReader.BuildAggregateSql("T", "STATUS", GridAggregate.CountDistinct, "\"X\" = @p0"));
    }
}
