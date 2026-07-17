using EmberTern.Core.Export.Sql;
using Xunit;

namespace EmberTern.Tests;

// E2 — signal B, read off the real AST (no fakes: these run the actual SqlParser, so a parser change
// that silently stopped modelling a shape would surface here rather than as a wrong INSERT).
public class StatementShapeReaderTests
{
    private static StatementShape Shape(string sql)
    {
        var s = Assert.IsType<OriginShape.Statement>(StatementShapeReader.Read(sql));
        return s.Shape;
    }

    // ── The one resolvable shape ─────────────────────────────────────────────
    [Theory]
    [InlineData("select CUSTOMER_ID, NAME from CUSTOMERS")]
    [InlineData("select c.CUSTOMER_ID as CID from CUSTOMERS c")]
    [InlineData("select * from CUSTOMERS where CITY = 'London' order by NAME")]
    [InlineData("select first 10 * from CUSTOMERS")]
    [InlineData("select distinct CITY from CUSTOMERS")]
    public void A_Plain_Single_Table_Select_Is_Understood_And_Unvetoed(string sql)
    {
        var s = Shape(sql);
        Assert.True(s.IsUnderstood);
        Assert.Equal(1, s.FromItemCount);
        Assert.False(s.IsSetOperation);
        Assert.False(s.HasJoin);
        Assert.False(s.HasGroupBy);
        Assert.False(s.IsWithQuery);
    }

    // ── The UNION trap — the reason signal B exists ──────────────────────────
    [Theory]
    [InlineData("select CUSTOMER_ID, NAME from CUSTOMERS union all select PRODUCT_ID, NAME from PRODUCTS")]
    [InlineData("select CUSTOMER_ID from CUSTOMERS union select PRODUCT_ID from PRODUCTS")]
    [InlineData("select CUSTOMER_ID from CUSTOMERS intersect select PRODUCT_ID from PRODUCTS")]
    [InlineData("select CUSTOMER_ID from CUSTOMERS except select PRODUCT_ID from PRODUCTS")]
    public void A_Set_Operation_Is_Reported(string sql) => Assert.True(Shape(sql).IsSetOperation);

    // ── Joins ────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("select o.ORDER_ID, c.NAME from ORDERS o join CUSTOMERS c on c.CUSTOMER_ID = o.CUSTOMER_ID")]
    [InlineData("select a.CUSTOMER_ID, b.CUSTOMER_ID from CUSTOMERS a join CUSTOMERS b on a.REF = b.CUSTOMER_ID")]
    [InlineData("select o.ORDER_ID from ORDERS o left join CUSTOMERS c on c.CUSTOMER_ID = o.CUSTOMER_ID")]
    public void A_Join_Is_Reported(string sql)
    {
        var s = Shape(sql);
        Assert.True(s.HasJoin);
        Assert.Equal(1, s.FromItemCount); // a join is ONE from entry — which is why HasJoin is separate
    }

    [Fact]
    public void A_Comma_Cross_Product_Is_Reported_As_Several_From_Items()
        => Assert.Equal(2, Shape("select a.ID, b.ID from CUSTOMERS a, PRODUCTS b").FromItemCount);

    // ── Aggregates ───────────────────────────────────────────────────────────
    [Fact]
    public void A_Group_By_Is_Reported()
        => Assert.True(Shape("select CUSTOMER_ID, count(*) from ORDERS group by CUSTOMER_ID").HasGroupBy);

    // ── Derived tables are transparent, not a veto (§1.3) ────────────────────
    // The driver reports the INNER query's real base table here, so this genuinely is one table's rows.
    [Fact]
    public void A_Derived_Table_Over_One_Table_Stays_Resolvable()
    {
        var s = Shape("select * from (select CUSTOMER_ID, NAME from CUSTOMERS) x");
        Assert.True(s.IsUnderstood);
        Assert.Equal(1, s.FromItemCount);
        Assert.False(s.IsSetOperation);
        Assert.False(s.HasJoin);
    }

    // …but the same wrapper can HIDE a union or a join, and the driver would still report a clean
    // single-table result. The inner shape must surface through the wrapper.
    [Fact]
    public void A_Union_Hidden_Inside_A_Derived_Table_Still_Surfaces()
        => Assert.True(Shape(
            "select * from (select CUSTOMER_ID from CUSTOMERS union all select PRODUCT_ID from PRODUCTS) x")
            .IsSetOperation);

    [Fact]
    public void A_Join_Hidden_Inside_A_Derived_Table_Still_Surfaces()
        => Assert.True(Shape(
            "select * from (select o.ORDER_ID, c.NAME from ORDERS o join CUSTOMERS c on c.CUSTOMER_ID = o.CUSTOMER_ID) x")
            .HasJoin);

    [Fact]
    public void A_Nested_Derived_Table_Recurses_All_The_Way_Down()
        => Assert.True(Shape(
            "select * from (select * from (select CUSTOMER_ID from CUSTOMERS union all select PRODUCT_ID from PRODUCTS) y) x")
            .IsSetOperation);

    // An outer GROUP BY over an innocent derived table must not be lost by the recursion.
    [Fact]
    public void An_Outer_Group_By_Over_A_Derived_Table_Is_Kept()
        => Assert.True(Shape("select CITY, count(*) from (select CITY from CUSTOMERS) x group by CITY").HasGroupBy);

    // ── WITH ─────────────────────────────────────────────────────────────────
    [Fact]
    public void A_With_Query_Is_Reported()
        => Assert.True(Shape("with X as (select CUSTOMER_ID from CUSTOMERS) select * from X").IsWithQuery);

    // ── Not understood ⇒ refuse (uncertainty ⇒ do nothing) ───────────────────
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("insert into CUSTOMERS (NAME) values ('x')")]     // not a SELECT
    [InlineData("execute procedure SP_CUSTOMER_ORDERS(1)")]
    [InlineData("select CUSTOMER_ID from CUSTOMERS; select NAME from PRODUCTS")] // two statements
    [InlineData("this is not sql at all")]
    public void Anything_But_One_Modelled_Select_Is_Not_Understood(string sql)
        => Assert.False(Shape(sql).IsUnderstood);

    // The shape reader is NOT a syntax validator, and must not be mistaken for one: a grid only ever
    // holds the result of SQL the engine already accepted and ran. So the error-tolerant parser happily
    // models even `select from where` as a degenerate SELECT — and it is refused for the honest reason
    // (it has no source table), not for being nonsense. Signal A agrees independently: a statement that
    // never ran has no columns and therefore no base tables.
    [Fact]
    public void A_Degenerate_Select_Is_Modelled_But_Has_No_Source_Table()
    {
        var s = Shape("select from where");
        Assert.True(s.IsUnderstood);
        Assert.Equal(0, s.FromItemCount); // ⇒ ResultOriginResolver refuses with NoSourceTable
    }

    [Fact]
    public void A_From_Less_Select_Has_No_Source_Table()
    {
        var s = Shape("select 1 as LITERAL from RDB$DATABASE");
        Assert.True(s.IsUnderstood);
        Assert.Equal(1, s.FromItemCount); // RDB$DATABASE is a table reference like any other

        Assert.Equal(0, Shape("select 1").FromItemCount);
    }

    [Fact]
    public void Read_Never_Throws_On_Junk()
    {
        Assert.NotNull(StatementShapeReader.Read("select ((("));
        Assert.NotNull(StatementShapeReader.Read(null));
        Assert.NotNull(StatementShapeReader.Read("with"));
    }
}
