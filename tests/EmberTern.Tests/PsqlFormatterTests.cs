using System;
using System.Linq;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

// PSQL mode of the single shared SqlFormatter: block-structured layout for
// procedure/trigger/function bodies, reusing the DML formatter for each leaf
// statement. The headline requirement is CASE…END must NOT be mistaken for a
// BEGIN…END block, and formatting must be idempotent + semantics-preserving.
public class PsqlFormatterTests
{
    private static int CountLines(string s, string trimmedEquals)
        => s.Split('\n').Count(l => l.Trim() == trimmedEquals);

    [Fact]
    public void SimpleBody_IndentsStatements()
    {
        var outp = SqlFormatter.Format("begin x = 1; y = 2; end");
        var lines = outp.Split('\n');
        Assert.Equal("begin", lines[0]);
        Assert.Equal("  x = 1;", lines[1]);
        Assert.Equal("  y = 2;", lines[2]);
        Assert.Equal("end", lines[3]);
    }

    [Fact]
    public void NestedBeginEnd_IndentsByDepth()
    {
        var outp = SqlFormatter.Format("begin begin a = 1; end b = 2; end");
        var lines = outp.Split('\n');
        Assert.Equal("begin", lines[0]);
        Assert.Equal("  begin", lines[1]);
        Assert.Equal("    a = 1;", lines[2]);
        Assert.Equal("  end", lines[3]);
        Assert.Equal("  b = 2;", lines[4]);
        Assert.Equal("end", lines[5]);
    }

    [Fact]
    public void CaseEnd_StaysInline_NotABlock()
    {
        // The CASE's END must NOT increment/close a block — exactly one block
        // begin and one block end.
        var outp = SqlFormatter.Format(
            "begin x = case when a = 1 then 10 else 20 end; suspend; end");
        Assert.Contains("x = case when a = 1 then 10 else 20 end;", outp);
        Assert.Equal(1, CountLines(outp, "begin"));
        Assert.Equal(1, CountLines(outp, "end"));
    }

    [Fact]
    public void CaseEnd_InsideNestedBlock()
    {
        var outp = SqlFormatter.Format(
            "begin while (a < 10) do begin x = case when a then 1 else 2 end; a = a + 1; end end");
        // 2 begins (outer + while body), 2 ends — the CASE END does not add a block.
        Assert.Equal(2, CountLines(outp, "begin"));
        Assert.Equal(2, CountLines(outp, "end"));
        Assert.Contains("case when a then 1 else 2 end;", outp);
    }

    [Fact]
    public void IfElse_BranchesIndented()
    {
        var outp = SqlFormatter.Format("begin if (a = 1) then b = 2; else b = 3; end");
        var lines = outp.Split('\n');
        Assert.Contains("  if (a = 1) then", lines);
        Assert.Contains("    b = 2;", lines);
        Assert.Contains("  else", lines);
        Assert.Contains("    b = 3;", lines);
    }

    [Fact]
    public void IfThen_BlockBranch()
    {
        var outp = SqlFormatter.Format("begin if (a = 1) then begin b = 2; end end");
        var lines = outp.Split('\n');
        Assert.Contains("  if (a = 1) then", lines);
        Assert.Contains("  begin", lines);
        Assert.Contains("    b = 2;", lines);
    }

    [Fact]
    public void While_HeaderAndBody()
    {
        var outp = SqlFormatter.Format("begin while (:dzien <= :datado) do begin x = 1; end end");
        Assert.Contains("  while (:dzien <= :datado) do", outp.Split('\n'));
    }

    [Fact]
    public void SelectInto_OnOwnLine()
    {
        var outp = SqlFormatter.Format(
            "begin select coalesce(sum(x), 0) from t where a = 1 into :s; end");
        var lines = outp.Split('\n');
        // SELECT is directly inside the outer BEGIN → one indent level (2 spaces).
        Assert.Contains("  select coalesce(sum(x), 0)", lines);
        Assert.Contains("  from t", lines);
        Assert.Contains("  into :s;", lines);   // INTO on its own line
    }

    [Fact]
    public void ForSelect_Header()
    {
        var outp = SqlFormatter.Format("begin for select id from t into :i do begin suspend; end end");
        Assert.Contains("do", outp);
        Assert.Contains("suspend;", outp);
    }

    [Fact]
    public void ExecuteStatement_LiteralPreserved()
    {
        var outp = SqlFormatter.Format(
            "begin execute statement 'update t set x = 1 where id = 2'; end");
        // The SQL string literal is opaque — preserved verbatim, not reformatted.
        Assert.Contains("'update t set x = 1 where id = 2'", outp);
        Assert.Equal(1, CountLines(outp, "begin"));
        Assert.Equal(1, CountLines(outp, "end"));
    }

    [Fact]
    public void LocalSubprogram_Indented()
    {
        var outp = SqlFormatter.Format(
            "declare procedure sub (p integer) as begin p = p + 1; end begin x = 1; end");
        var lines = outp.Split('\n');
        // The name glues to its param list like a call (valid FB) — cosmetic only.
        Assert.Contains("declare procedure sub(p integer) as", lines);
        // Two begins / two ends: the subprogram block + the main block.
        Assert.Equal(2, CountLines(outp, "begin"));
    }

    [Fact]
    public void DeclareSection_OnePerLine()
    {
        var outp = SqlFormatter.Format(
            "declare variable x integer; declare variable y varchar(5); begin x = 1; end");
        var lines = outp.Split('\n');
        Assert.Equal("declare variable x integer;", lines[0]);
        Assert.Equal("declare variable y varchar(5);", lines[1]);
        Assert.Equal("begin", lines[2]);
    }

    [Fact]
    public void Comments_PreservedAndDoNotCommentOutCode()
    {
        var line = SqlFormatter.Format("begin x = 1; -- note\n y = 2; end");
        // The line comment must keep its position and NOT swallow "y = 2;".
        Assert.Contains("-- note", line);
        Assert.Contains("y = 2;", line);

        var block = SqlFormatter.Format("begin /* hdr */ x = 1; end");
        Assert.Contains("/* hdr */", block);
    }

    [Fact]
    public void FullSource_HeaderPreserved_BodyStructured()
    {
        var outp = SqlFormatter.Format(
            "CREATE OR ALTER PROCEDURE P\nRETURNS (R INTEGER)\nAS\nBEGIN R = 0; SUSPEND; END");
        Assert.StartsWith("CREATE OR ALTER PROCEDURE P", outp);
        Assert.Contains("begin", outp);
        Assert.Contains("  r = 0;", outp);
        Assert.Contains("  suspend;", outp);
    }

    [Theory]
    [InlineData("begin x = 1; y = 2; end")]
    [InlineData("begin if (a = 1) then b = 2; else b = 3; end")]
    [InlineData("begin while (a < 10) do begin x = case when a then 1 else 2 end; a = a + 1; end end")]
    [InlineData("begin select coalesce(sum(x), 0) from t where a = 1 and b = 2 into :s; suspend; end")]
    [InlineData("declare variable x integer; begin execute statement 'select 1 from rdb$database'; end")]
    [InlineData("declare procedure sub (p integer) as begin p = p + 1; end begin x = 1; end")]
    public void Idempotent(string sql)
    {
        var once = SqlFormatter.Format(sql);
        var twice = SqlFormatter.Format(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void PreservesLogicalBlankLines()
    {
        var src = "begin\n  x = 1;\n\n  while (a < 2) do\n  begin\n    y = 2;\n  end\nend";
        var outp = SqlFormatter.Format(src);
        Assert.Contains("\n\n", outp);                       // an author blank line survived
        Assert.Equal(outp, SqlFormatter.Format(outp));       // still idempotent with blanks
    }

    [Fact]
    public void CollapsesMultipleBlankLinesToOne()
    {
        var outp = SqlFormatter.Format("begin\n  x = 1;\n\n\n\n  y = 2;\nend");
        Assert.Contains("\n\n", outp);                       // one blank kept
        Assert.DoesNotContain("\n\n\n", outp);               // 2+ blanks collapsed to one
    }

    [Fact]
    public void PlainSelect_StillUsesDmlMode_NotPsql()
    {
        // No BEGIN, not a CREATE PROC/TRIGGER/FUNCTION → DML mode (clause breaks),
        // unchanged behaviour.
        var outp = SqlFormatter.Format("select a, b from t where x = 1");
        Assert.Contains("select a, b", outp);
        Assert.Contains("from t", outp.Split('\n'));
        Assert.Contains("where x = 1", outp.Split('\n'));
        Assert.DoesNotContain("begin", outp);
    }
}
