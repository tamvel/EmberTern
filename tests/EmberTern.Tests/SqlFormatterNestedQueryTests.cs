using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap 6.9 formatter convergence — the intended layout changes the AST-walking query core enables:
/// multi-level nested-query indentation (derived tables, EXISTS, scalar subqueries, IN(SELECT) as
/// expanded-paren blocks) and adaptive CASE layout (simple/short → inline; multi-WHEN / over-width →
/// a WHEN/THEN/ELSE block). These deliberately differ from the pre-convergence flattened output (there
/// were no exact tests pinning nesting/CASE before — they were loose or absent). Every assertion also
/// proves the §0 lexeme net did NOT fire (it would return the input verbatim) and pairs with an
/// idempotency check.
/// </summary>
public class SqlFormatterNestedQueryTests
{
    private static void Idempotent(string sql)
    {
        var once = SqlFormatter.Format(sql);
        Assert.Equal(once, SqlFormatter.Format(once));
    }

    // ── Nested queries — expanded-paren blocks ─────────────────────────────────────────────────

    [Fact]
    public void InSubquery_ExpandsAsBlock()
    {
        Assert.Equal(
            "select a\nfrom t\nwhere x in (\n    select id\n    from other\n    where val > 1\n)",
            SqlFormatter.Format("SELECT a FROM t WHERE x IN (SELECT id FROM other WHERE val > 1)"));
    }

    [Fact]
    public void ExistsSubquery_ExpandsAsBlock()
    {
        Assert.Equal(
            "select *\nfrom t\nwhere exists (\n    select 1\n    from u\n    where u.tid = t.id\n)",
            SqlFormatter.Format("SELECT * FROM t WHERE EXISTS (SELECT 1 FROM u WHERE u.tid = t.id)"));
    }

    [Fact]
    public void ScalarSubqueryInProjection_ExpandsAsBlock_ProjectionItemsOwnLayout()
    {
        Assert.Equal(
            "select\n       id,\n       (\n           select count(*)\n           from u\n           where u.tid = t.id\n       ) as cnt\nfrom t",
            SqlFormatter.Format("SELECT id, (SELECT COUNT(*) FROM u WHERE u.tid = t.id) AS cnt FROM t"));
    }

    [Fact]
    public void DerivedTable_ExpandsAsBlock_AliasFollowsCloseParen()
    {
        Assert.Equal(
            "select d.n\nfrom (\n    select id as n\n    from t\n    where x = 1\n) d\nwhere d.n > 0",
            SqlFormatter.Format("SELECT d.n FROM (SELECT id AS n FROM t WHERE x = 1) d WHERE d.n > 0"));
    }

    [Fact]
    public void TwoDerivedTablesJoined_EachBlock_JoinKeywordPreserved()
    {
        Assert.Equal(
            "select p.n\nfrom (\n    select n\n    from t\n) p\njoin (\n    select n\n    from u\n) q on p.n = q.n",
            SqlFormatter.Format("SELECT p.n FROM (SELECT n FROM t) p JOIN (SELECT n FROM u) q ON p.n = q.n"));
    }

    [Fact]
    public void ExistsInJoinOn_ExpandsUnderConjunctionIndent()
    {
        Assert.Equal(
            "select 1\nfrom t a\njoin u b on a.id = b.id\n  and exists (\n      select 1\n      from v\n      where v.k = a.id\n  )",
            SqlFormatter.Format("SELECT 1 FROM t a JOIN u b ON a.id = b.id AND EXISTS (SELECT 1 FROM v WHERE v.k = a.id)"));
    }

    [Fact]
    public void SubqueryInFunctionArgument_NestsDeeper_NotAtArgumentColumn()
    {
        // Regression: a subquery inside a function-call argument used to flatten to the argument's own
        // column (no depth increase) because the shared list builder rendered items without the AST
        // structural children. It must now splice + nest one level deeper than its paren.
        Assert.Equal(
            "select\n       coalesce(\n                (\n                    select max(x)\n                    from u\n                ),\n                0) as m\nfrom t",
            SqlFormatter.Format("select coalesce((select max(x) from u), 0) as m from t"));
    }

    [Fact]
    public void SubqueryInCaseArm_NestsUnderTheArm()
    {
        // Regression: a subquery in a WHEN … THEN result used to dedent its closing paren (the arm's
        // multi-line continuation was not shifted by the arm indent).
        Assert.Equal(
            "select\n       case\n         when x > 0 then (\n             select max(y)\n             from u\n             where u.k = t.id\n         )\n         else 0\n       end as m\nfrom t",
            SqlFormatter.Format("select case when x > 0 then (select max(y) from u where u.k = t.id) else 0 end as m from t"));
    }

    [Fact]
    public void NestedIndentation_IsIdempotent()
    {
        Idempotent("SELECT a FROM t WHERE x IN (SELECT id FROM other WHERE val > 1)");
        Idempotent("SELECT id, (SELECT COUNT(*) FROM u WHERE u.tid = t.id) AS cnt FROM t");
        Idempotent("SELECT d.n FROM (SELECT id AS n FROM t WHERE x = 1) d WHERE d.n > 0");
        Idempotent("SELECT p.n FROM (SELECT n FROM t) p JOIN (SELECT n FROM u) q ON p.n = q.n");
        Idempotent("SELECT 1 FROM t a JOIN u b ON a.id = b.id AND EXISTS (SELECT 1 FROM v WHERE v.k = a.id)");
        Idempotent("select coalesce((select max(x) from u), 0) as m from t");
        Idempotent("select case when x > 0 then (select max(y) from u where u.k = t.id) else 0 end as m from t");
        Idempotent("select * from (select * from (select * from (select y from t) a) b) c");
    }

    // ── CASE — adaptive ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SearchedCase_MultiWhen_LaysOutAsBlock()
    {
        Assert.Equal(
            "select\n       case\n         when x > 0 then 'pos'\n         when x < 0 then 'neg'\n         else 'zero'\n       end as s\nfrom t",
            SqlFormatter.Format("SELECT CASE WHEN x > 0 THEN 'pos' WHEN x < 0 THEN 'neg' ELSE 'zero' END AS s FROM t"));
    }

    [Fact]
    public void SimpleCase_MultiWhen_LaysOutAsBlock_OperandOnCaseLine()
    {
        Assert.Equal(
            "select\n       case x\n         when 1 then 'one'\n         when 2 then 'two'\n         else '?'\n       end as s\nfrom t",
            SqlFormatter.Format("SELECT CASE X WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE '?' END AS S FROM T"));
    }

    [Fact]
    public void CaseBlockItem_DoesNotForceNeighboursOnePerLine()
    {
        // The CASE expands as a block; the ordinary neighbours (b, c) still pack adaptively.
        Assert.Equal(
            "select\n       a,\n       case\n         when x > 0 then 'pos'\n         when x < 0 then 'neg'\n         else 'zero'\n       end as s,\n       b, c\nfrom t",
            SqlFormatter.Format("SELECT a, CASE WHEN x > 0 THEN 'pos' WHEN x < 0 THEN 'neg' ELSE 'zero' END AS s, b, c FROM t"));
    }

    [Fact]
    public void SingleWhenCase_StaysInline()
    {
        // A simple single-WHEN CASE that fits stays on one line (user directive: don't expand trivial CASE).
        Assert.Equal(
            "update t set s = case when x is null then 0 else x end\nwhere id = 1",
            SqlFormatter.Format("UPDATE T SET S = CASE WHEN X IS NULL THEN 0 ELSE X END WHERE ID = 1"));
    }

    [Fact]
    public void Case_IsIdempotent()
    {
        Idempotent("SELECT CASE WHEN x > 0 THEN 'pos' WHEN x < 0 THEN 'neg' ELSE 'zero' END AS s FROM t");
        Idempotent("SELECT a, CASE WHEN x > 0 THEN 'pos' WHEN x < 0 THEN 'neg' ELSE 'zero' END AS s, b, c FROM t");
        Idempotent("SELECT CASE X WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE '?' END AS S FROM T");
        Idempotent("UPDATE T SET S = CASE WHEN X IS NULL THEN 0 ELSE X END WHERE ID = 1");
        Idempotent("SELECT CASE WHEN A THEN CASE WHEN B THEN 1 ELSE 2 END ELSE 3 END AS N FROM T");
    }
}
