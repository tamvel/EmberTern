using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap 6.9 formatter convergence — PSQL bodies now lay out their leaf statements through the AST: a
/// DML/SELECT leaf gets its full query structure (nested-query indentation), a FOR SELECT cursor query
/// is laid out by the query core, an IF/WHILE condition and an assignment splice their embedded CASE /
/// subqueries. The block STRUCTURE (BEGIN/END, IF/WHILE/FOR nesting) is unchanged. Each case is idempotent
/// and (implicitly) §0-lossless.
/// </summary>
public class SqlFormatterPsqlAstTests
{
    private static void Idempotent(string sql)
    {
        var once = SqlFormatter.Format(sql);
        Assert.Equal(once, SqlFormatter.Format(once));
    }

    [Fact]
    public void ForSelectCursor_WithNestedSubquery_IndentsInsideLoop()
    {
        Assert.Equal(
            "create or alter procedure p returns (r integer) as\n"
            + "begin\n"
            + "  for select id\n"
            + "  from (\n"
            + "      select id\n"
            + "      from t\n"
            + "      where x in (\n"
            + "          select k\n"
            + "          from u\n"
            + "      )\n"
            + "  ) d\n"
            + "  into :r\n"
            + "  do\n"
            + "  begin\n"
            + "    suspend;\n"
            + "  end\n"
            + "end",
            SqlFormatter.Format(
                "create or alter procedure p returns (r integer) as begin "
                + "for select id from (select id from t where x in (select k from u)) d into :r "
                + "do begin suspend; end end"));
    }

    [Fact]
    public void InsertSelectInBody_LaysOutWithNestedBlock()
    {
        Assert.Equal(
            "create procedure p as\n"
            + "begin\n"
            + "  insert into t (a, b)\n"
            + "  select x, y\n"
            + "  from s\n"
            + "  where z in (\n"
            + "      select k\n"
            + "      from u\n"
            + "  );\n"
            + "end",
            SqlFormatter.Format(
                "create procedure p as begin insert into t (a, b) select x, y from s where z in (select k from u); end"));
    }

    [Fact]
    public void MultiWhenCaseInAssignment_LaysOutAsBlock()
    {
        Assert.Equal(
            "begin\n"
            + "  update t set s =\n"
            + "  case\n"
            + "    when x > 0 then 'p'\n"
            + "    when x < 0 then 'n'\n"
            + "    else 'z'\n"
            + "  end\n"
            + "  where id = 1;\n"
            + "end",
            SqlFormatter.Format(
                "begin update t set s = case when x > 0 then 'p' when x < 0 then 'n' else 'z' end where id = 1; end"));
    }

    [Fact]
    public void IfWithCaseCondition_StaysInline_HeaderNotBrokenAtCaseThen()
    {
        // Regression: CollectUntilWord must skip a nested CASE's own THEN when finding the IF's THEN.
        Assert.Equal(
            "begin\n  if (case when b then 1 else 0 end = 1) then\n    suspend;\nend",
            SqlFormatter.Format("begin if (case when b then 1 else 0 end = 1) then suspend; end"));
    }

    [Fact]
    public void PsqlAst_IsIdempotent()
    {
        Idempotent("create or alter procedure p returns (r integer) as begin for select id from (select id from t where x in (select k from u)) d into :r do begin suspend; end end");
        Idempotent("create procedure p as begin insert into t (a, b) select x, y from s where z in (select k from u); end");
        Idempotent("begin update t set s = case when x > 0 then 'p' when x < 0 then 'n' else 'z' end where id = 1; end");
        Idempotent("begin if (case when b then 1 else 0 end = 1) then suspend; end");
        Idempotent("begin v = case when a > 0 then 1 when a < 0 then -1 else 0 end; end");
    }
}
