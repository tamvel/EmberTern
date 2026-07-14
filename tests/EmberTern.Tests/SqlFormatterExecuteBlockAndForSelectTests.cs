using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// P8 final layout steps — EXECUTE BLOCK header and PSQL FOR loop.
/// <para>
/// <b>EXECUTE BLOCK</b> is a runnable statement (not persistent DDL), so its header is laid out like
/// every other executable statement: lowercased, with the input-parameter list on the "execute block"
/// line and RETURNS on its own line, both via the shared adaptive list builder (§F) — while a CREATE
/// definition header stays verbatim.
/// </para>
/// <para>
/// <b>FOR</b> loops (<c>FOR &lt;select&gt; INTO &lt;vars&gt; DO &lt;statement&gt;</c>) are laid out as
/// four structural parts: <c>for</c> / the clause-broken cursor query (indented) / <c>into …</c> /
/// <c>do</c> / the loop body — replacing the previous mangled single-line-emit behaviour.
/// </para>
/// </summary>
public class SqlFormatterExecuteBlockAndForSelectTests
{
    // ── EXECUTE BLOCK header ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExecuteBlock_ParamsAndReturns_LaidOutAndLowercased()
    {
        Assert.Equal(
            "execute block (a integer = ?, b varchar(20) = ?)\n"
            + "returns (r integer, s varchar(10))\n"
            + "as\n"
            + "begin\n"
            + "  r = a;\n"
            + "  suspend;\n"
            + "end",
            SqlFormatter.Format(
                "EXECUTE BLOCK (a INTEGER = ?, b VARCHAR(20) = ?) RETURNS (r INTEGER, s VARCHAR(10)) "
                + "AS BEGIN r = a; SUSPEND; END"));
    }

    [Fact]
    public void ExecuteBlock_ReturnsOnly_OnOwnLine()
    {
        Assert.Equal(
            "execute block\nreturns (r integer)\nas\nbegin\n  r = 1;\nend",
            SqlFormatter.Format("EXECUTE BLOCK RETURNS (R INTEGER) AS BEGIN R = 1; END"));
    }

    [Fact]
    public void ExecuteBlock_Bare_HeaderLowercased()
    {
        Assert.Equal(
            "execute block\nas\nbegin\n  x = 1;\nend",
            SqlFormatter.Format("EXECUTE BLOCK AS BEGIN X = 1; END"));
    }

    [Fact]
    public void ExecuteBlock_LongParamList_WrapsAdaptively()
    {
        var ps = string.Join(", ", Enumerable.Range(1, 12).Select(i => $"p_long_parameter_{i:D2} integer = ?"));
        var outp = SqlFormatter.Format($"EXECUTE BLOCK ({ps}) RETURNS (r INTEGER) AS BEGIN r = 1; END");
        var lines = outp.Split('\n');
        Assert.All(lines, l => Assert.True(l.Length <= 120, $"line exceeds 120 ({l.Length}): {l}"));
        Assert.StartsWith("execute block (", outp);
        Assert.Contains("returns (r integer)", lines);
    }

    [Fact]
    public void ExecuteBlock_LeadingComment_Preserved()
    {
        var outp = SqlFormatter.Format("-- run me\nEXECUTE BLOCK RETURNS (R INTEGER) AS BEGIN R = 1; END");
        Assert.StartsWith("-- run me\nexecute block", outp);
    }

    [Fact]
    public void ExecuteBlock_IsIdempotent()
    {
        var once = SqlFormatter.Format(
            "EXECUTE BLOCK (a INTEGER = ?) RETURNS (r INTEGER) AS BEGIN r = a; SUSPEND; END");
        Assert.Equal(once, SqlFormatter.Format(once));
    }

    // ── FOR loop ─────────────────────────────────────────────────────────────────────────────────
    //
    // FOR SELECT is one Firebird construct (like INSERT INTO): "for" glues to the query's first line,
    // the query is NOT extra-indented, and INTO / DO sit at the loop indent.

    [Fact]
    public void ForSelect_ForGluedToSelect_QueryAtLoopIndent()
    {
        Assert.Equal(
            "begin\n"
            + "  for select id, name\n"
            + "  from t\n"
            + "  where a = 1\n"
            + "  into :i, :n\n"
            + "  do\n"
            + "  begin\n"
            + "    suspend;\n"
            + "  end\n"
            + "end",
            SqlFormatter.Format(
                "begin for select id, name from t where a = 1 into :i, :n do begin suspend; end end"));
    }

    [Fact]
    public void ForSelect_SingleStatementBody_Indented()
    {
        Assert.Equal(
            "begin\n  for select id\n  from t\n  into :i\n  do\n    suspend;\nend",
            SqlFormatter.Format("begin for select id from t into :i do suspend; end"));
    }

    [Fact]
    public void ForSelect_LongColumnList_WrapsInsideLoop()
    {
        var cols = string.Join(", ", Enumerable.Range(1, 20).Select(i => $"long_column_name_{i:D2}"));
        var outp = SqlFormatter.Format(
            $"begin for select {cols} from t into :a do begin suspend; end end");
        var lines = outp.Split('\n');
        Assert.Contains("  for select long_column_name_01,", lines[1]); // "for select" glued
        Assert.Contains("  into :a", lines);
        Assert.Contains("  do", lines);
    }

    [Fact]
    public void ForExecuteStatement_LiteralPreserved()
    {
        var outp = SqlFormatter.Format(
            "begin for execute statement 'select 1 from rdb$database' into :x do begin suspend; end end");
        Assert.Contains("'select 1 from rdb$database'", outp);
        Assert.Contains("  for execute statement 'select 1 from rdb$database'", outp.Split('\n'));
        Assert.Contains("  into :x", outp.Split('\n'));
        Assert.Contains("  do", outp.Split('\n'));
    }

    [Fact]
    public void ForSelect_SubqueryInFrom_DoesNotLeakIntoOrDo()
    {
        // The subquery's own tokens are at paren depth > 0, so the loop's INTO/DO are still found
        // at top level and the inner query is not split apart.
        var outp = SqlFormatter.Format(
            "begin for select x from (select id as x from t) d into :i do begin suspend; end end");
        Assert.Contains("  into :i", outp.Split('\n'));
        Assert.Equal(outp, SqlFormatter.Format(outp));
    }

    [Theory]
    [InlineData("begin for select id, name from t where a = 1 into :i, :n do begin suspend; end end")]
    [InlineData("begin for select id from t into :i do suspend; end")]
    [InlineData("begin for execute statement 'select 1 from rdb$database' into :x do begin suspend; end end")]
    [InlineData("begin for select a from t1 into :a do for select b from t2 where c = :a into :b do begin suspend; end end")]
    public void ForLoop_IsIdempotent(string sql)
    {
        var once = SqlFormatter.Format(sql);
        Assert.Equal(once, SqlFormatter.Format(once));
    }

    // ── §0 lexeme preservation across both constructs ───────────────────────────────────────────

    [Theory]
    [InlineData("EXECUTE BLOCK (a INTEGER = ?, b VARCHAR(20) = ?) RETURNS (r INTEGER) AS BEGIN r = a; SUSPEND; END")]
    [InlineData("begin for select id, name from t where a = 1 into :i, :n do begin suspend; end end")]
    public void PreservesEveryLexeme(string sql)
        => Assert.Equal(Lexemes(sql), Lexemes(SqlFormatter.Format(sql)));

    private static List<string> Lexemes(string sql)
    {
        var list = new List<string>();
        foreach (var t in SqlLexer.Tokenize(sql))
        {
            foreach (var tr in t.LeadingTrivia)
                if (tr.Kind is TriviaKind.LineComment or TriviaKind.BlockComment) list.Add("c:" + tr.Text.TrimEnd());
            if (t.Kind == TokenKind.EndOfFile) continue;
            list.Add(t.Kind switch
            {
                TokenKind.Keyword or TokenKind.Identifier or TokenKind.Parameter => "w:" + t.Text.ToUpperInvariant(),
                _ => "x:" + t.Text,
            });
        }
        return list;
    }
}
