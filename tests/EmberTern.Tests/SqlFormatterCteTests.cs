using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// WITH / CTE formatting (P8 follow-up). The formatter lays out a common-table-expression query
/// IBExpert-style: each CTE's name (+ optional column list) on its own line, "as (" on its own,
/// the CTE body indented and formatted by the shared emitter, ")" on its own, multiple CTEs joined
/// "),", and the main query after a blank line. Set operators (UNION [ALL] / INTERSECT / EXCEPT)
/// break onto their own line via the one shared structural-phrase mechanism — inside CTE bodies and
/// at the top level alike. All exact-output assertions double as proof the §0 safety net did NOT fire
/// (it would return the input verbatim). Idempotency + losslessness of the WITH corpus is pinned by
/// <see cref="SqlFormatterInvariantsTests"/>.
/// </summary>
public class SqlFormatterCteTests
{
    [Fact]
    public void SingleCte_LaidOutWithIndentedBody()
    {
        Assert.Equal(
            "with c\nas (\n    select id\n    from t\n)\n\nselect *\nfrom c",
            SqlFormatter.Format("with c as (select id from t) select * from c"));
    }

    [Fact]
    public void CteBody_UnionAll_BreaksOntoOwnLine()
    {
        Assert.Equal(
            "with r\nas (\n    select id\n    from a\n    union all\n    select id\n    from b\n)\n\nselect *\nfrom r",
            SqlFormatter.Format("with r as (select id from a union all select id from b) select * from r"));
    }

    [Fact]
    public void MultipleCtes_JoinedWithComma()
    {
        Assert.Equal(
            "with a\nas (\n    select 1\n    from t\n),\nb\nas (\n    select 2\n    from u\n)\n\nselect *\nfrom a",
            SqlFormatter.Format("with a as (select 1 from t), b as (select 2 from u) select * from a"));
    }

    [Fact]
    public void Cte_WithExplicitColumnList()
    {
        Assert.Equal(
            "with c (x, y)\nas (\n    select a, b\n    from t\n)\n\nselect *\nfrom c",
            SqlFormatter.Format("with c (x, y) as (select a, b from t) select * from c"));
    }

    [Fact]
    public void Cte_Recursive()
    {
        Assert.Equal(
            "with recursive r\nas (\n    select 1\n    from t\n)\n\nselect *\nfrom r",
            SqlFormatter.Format("with recursive r as (select 1 from t) select * from r"));
    }

    [Fact]
    public void TopLevelUnionAll_BreaksOntoOwnLine()
    {
        // The set-operator break is a general Emit mechanism, not CTE-specific.
        Assert.Equal(
            "select a\nfrom t\nunion all\nselect a\nfrom u",
            SqlFormatter.Format("select a from t union all select a from u"));
    }

    [Fact]
    public void TopLevelUnionAndIntersect_Break()
    {
        Assert.Equal(
            "select a\nfrom t\nunion\nselect a\nfrom u\nintersect\nselect a\nfrom v",
            SqlFormatter.Format("select a from t union select a from u intersect select a from v"));
    }

    [Theory]
    [InlineData("with c as (select id from t) select * from c")]
    [InlineData("with r as (select id from a union all select id from b) select * from r")]
    [InlineData("with a as (select 1 from t), b as (select 2 from u) select * from a")]
    [InlineData("with c (x, y) as (select a, b from t) select * from c")]
    [InlineData("with recursive r as (select 1 from t) select * from r")]
    [InlineData("select a from t union all select a from u")]
    public void Format_IsIdempotent(string sql)
    {
        var once = SqlFormatter.Format(sql);
        Assert.Equal(once, SqlFormatter.Format(once));
    }
}
