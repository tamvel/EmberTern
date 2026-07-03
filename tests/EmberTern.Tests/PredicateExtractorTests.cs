using System.Linq;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class PredicateExtractorTests
{
    private static QueryPredicate Single(string sql)
        => Assert.Single(PredicateExtractor.Extract(sql));

    [Fact]
    public void SimpleWhere_ResolvesColumnOperatorRhsAndSoleTable()
    {
        var p = Single("SELECT * FROM NAGL WHERE ID = 5");
        Assert.Equal("ID", p.Column);
        Assert.Equal(SqlPredicateOperator.Equal, p.Operator);
        Assert.Equal("5", p.Rhs);
        Assert.Equal(SqlPredicateKind.Where, p.Kind);
        Assert.True(p.IsColumnBare);
        Assert.Equal("NAGL", p.Table);
        Assert.Null(p.Alias);
    }

    [Fact]
    public void QualifiedColumn_ResolvesAliasToTable()
    {
        var p = Single("SELECT * FROM NAGL n WHERE n.DATA >= '2020-01-01'");
        Assert.Equal("DATA", p.Column);
        Assert.Equal("N", p.Alias);
        Assert.Equal("NAGL", p.Table);
        Assert.Equal(SqlPredicateOperator.GreaterOrEqual, p.Operator);
        Assert.True(p.IsColumnBare);
    }

    [Fact]
    public void FunctionOnColumn_IsNotBare_ColumnStillIdentified()
    {
        var p = Single("SELECT * FROM NAGL WHERE UPPER(NAZWA) = 'X'");
        Assert.Equal("NAZWA", p.Column);
        Assert.False(p.IsColumnBare);
        Assert.Equal(SqlPredicateOperator.Equal, p.Operator);
    }

    [Fact]
    public void ArithmeticExpression_IsNotBare_ColumnIdentified()
    {
        var p = Single("SELECT * FROM T WHERE PRICE + 0 = 5");
        Assert.Equal("PRICE", p.Column);
        Assert.False(p.IsColumnBare);
    }

    [Fact]
    public void MultipleAndConjuncts_AllExtracted()
    {
        var ps = PredicateExtractor.Extract("SELECT * FROM T WHERE A = 1 AND B = 2");
        Assert.Equal(2, ps.Count);
        Assert.Contains(ps, p => p.Column == "A");
        Assert.Contains(ps, p => p.Column == "B");
    }

    [Fact]
    public void TopLevelOrFragment_IsSkipped()
        => Assert.Empty(PredicateExtractor.Extract("SELECT * FROM T WHERE A = 1 OR B = 2"));

    [Fact]
    public void AndWithParenthesizedOr_KeepsOnlyTheBareConjunct()
    {
        var ps = PredicateExtractor.Extract("SELECT * FROM T WHERE A = 1 AND (B = 2 OR C = 3)");
        var p = Assert.Single(ps);
        Assert.Equal("A", p.Column);
    }

    [Fact]
    public void JoinOn_AndWhere_AreBothCaptured()
    {
        var ps = PredicateExtractor.Extract("SELECT * FROM A a JOIN B b ON a.ID = b.AID WHERE a.X = 1");
        Assert.Contains(ps, p => p.Kind == SqlPredicateKind.JoinOn && p.Column == "ID" && p.Table == "A");
        Assert.Contains(ps, p => p.Kind == SqlPredicateKind.Where && p.Column == "X");
    }

    [Fact]
    public void IsNull_IsExtracted()
    {
        var p = Single("SELECT * FROM T WHERE NOTES IS NULL");
        Assert.Equal(SqlPredicateOperator.IsNull, p.Operator);
        Assert.Equal("NOTES", p.Column);
        Assert.True(p.IsColumnBare);
    }

    [Fact]
    public void InList_IsExtracted_SubqueryInnerPredicateIgnored()
    {
        var ps = PredicateExtractor.Extract("SELECT * FROM T WHERE ID IN (SELECT X FROM Y WHERE Z = 1)");
        var p = Assert.Single(ps);            // only the outer ID IN (...), not inner Z = 1
        Assert.Equal("ID", p.Column);
        Assert.Equal(SqlPredicateOperator.In, p.Operator);
    }

    [Fact]
    public void LikeLeadingWildcard_ExtractedAsLikeWithRhs()
    {
        var p = Single("SELECT * FROM T WHERE NAZWA LIKE '%x'");
        Assert.Equal(SqlPredicateOperator.Like, p.Operator);
        Assert.Equal("'%x'", p.Rhs);
    }

    [Fact]
    public void NoWhereOrOn_YieldsNothing()
        => Assert.Empty(PredicateExtractor.Extract("SELECT * FROM NAGL ORDER BY ID"));

    [Fact]
    public void MultiTableUnqualifiedColumn_TableUnresolved()
    {
        var p = Single("SELECT * FROM A, B WHERE X = 1");
        Assert.Equal("X", p.Column);
        Assert.Null(p.Table);   // ambiguous — two tables, unqualified column
    }
}
