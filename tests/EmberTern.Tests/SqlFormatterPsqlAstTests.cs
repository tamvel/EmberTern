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
    public void BarePsqlControlFlowFragment_FormatsAsAnonymousBlock_WithNesting()
    {
        // A bare IF/WHILE/FOR fragment (no enclosing BEGIN…END — e.g. a selection pasted out of a routine
        // body) is recognised as an anonymous PSQL body and formatted (with full nested-query layout in the
        // condition), instead of falling to a verbatim RawStatement.
        Assert.Equal(
            "if (exists (\n    select 1\n    from u\n    where u.k = :p\n)) then\n  wynik = 1;",
            SqlFormatter.Format("if (exists (select 1 from u where u.k = :p)) then wynik = 1;"));
    }

    [Fact]
    public void SelectIntoLeaf_WithLeadingComment_NotDuplicated()
    {
        // Regression: a SELECT … INTO leaf inside a PSQL body carries its leading comment as trivia on its
        // first token. The block structurer emits that comment once; the AST leaf renderer (EmitQuery) must
        // NOT re-emit it — a duplicate would change the lexeme stream and trip the §0 net, reverting the
        // whole routine to verbatim (the real-world "the procedure didn't format at all" symptom).
        var outp = SqlFormatter.Format("begin\n  -- pick one\n  select a from t where id = :p into :x;\nend");
        var occurrences = outp.Split("-- pick one").Length - 1;
        Assert.Equal(1, occurrences);                        // comment appears exactly once (not duplicated)
        Assert.Contains("  select a", outp);                 // and the leaf did format (not fall to verbatim)
        Assert.Contains("  where id = :p", outp);
        Assert.Equal(outp, SqlFormatter.Format(outp));       // idempotent
    }

    [Fact]
    public void RoutineBody_WithCommentedSelectInto_And_ElseIfExistsWith_FormatsNotVerbatim()
    {
        // The reduced shape of the reported procedure: a commented SELECT…INTO, then an ELSE-IF whose
        // condition is EXISTS(WITH…). It must format (nested), not revert to verbatim.
        const string sql =
            "create procedure p returns (wynik smallint) as begin " +
            "-- pobranie\n select first 1 e.lp from t e where e.a = 1 into :v; " +
            "if (a = 1) then wynik = 1; " +
            "else if (exists (with h as (select k from (select k from u) d) select 1 from h)) then wynik = 1; " +
            "suspend; end";
        var outp = SqlFormatter.Format(sql);
        Assert.DoesNotContain("create procedure p returns (wynik smallint) as begin -- pobranie", outp); // not verbatim
        Assert.Contains("\n        with h\n", outp);          // the inner WITH nested under the ELSE-IF
        Assert.Equal(outp, SqlFormatter.Format(outp));        // idempotent
    }

    [Fact]
    public void PsqlAst_IsIdempotent()
    {
        Idempotent("if (exists (select 1 from u where u.k = :p)) then wynik = 1;");
        Idempotent("while (x < (select max(n) from t)) do x = x + 1;");
        Idempotent("for select id from (select id from t where x > 0) d into :i do suspend;");
        Idempotent("create or alter procedure p returns (r integer) as begin for select id from (select id from t where x in (select k from u)) d into :r do begin suspend; end end");
        Idempotent("create procedure p as begin insert into t (a, b) select x, y from s where z in (select k from u); end");
        Idempotent("begin update t set s = case when x > 0 then 'p' when x < 0 then 'n' else 'z' end where id = 1; end");
        Idempotent("begin if (case when b then 1 else 0 end = 1) then suspend; end");
        Idempotent("begin v = case when a > 0 then 1 when a < 0 then -1 else 0 end; end");
    }
}
