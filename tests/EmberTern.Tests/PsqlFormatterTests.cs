using System;
using System.Linq;
using EmberTern.Core.Sql;
using EmberTern.Firebird;
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

    // ─── PACKAGE BODY (packaged subprogram definitions) — gotcha #152 ──────
    //
    // A PACKAGE BODY's top-level items are FUNCTION/PROCEDURE definitions (name(...)
    // [RETURNS …] AS [DECLARE …;]* BEGIN … END), NOT statements and NOT terminated by
    // ';'. The formatter previously collected only to the first inner ';', then took a
    // subprogram's END as the package-body END → it dropped the trailing ENDs → invalid
    // PSQL. These pin that the structure is preserved (every BEGIN/END kept + balanced)
    // and the result is idempotent.

    private const string LabPackageBody =
        "RECREATE PACKAGE BODY PKG_ORDERS\nAS\nBEGIN\n" +
        "  FUNCTION ORDER_TOTAL(P_ORDER_ID INTEGER) RETURNS NUMERIC(15,2)\n  AS\n" +
        "    DECLARE VARIABLE V_TOTAL NUMERIC(15,2);\n  BEGIN\n" +
        "    SELECT COALESCE(SUM(LINE_TOTAL), 0) FROM ORDER_ITEMS WHERE ORDER_ID = :P_ORDER_ID INTO :V_TOTAL;\n" +
        "    RETURN V_TOTAL;\n  END\n" +
        "  PROCEDURE RECALC_ORDER(P_ORDER_ID INTEGER)\n  AS\n  BEGIN\n" +
        "    UPDATE ORDERS SET TOTAL_AMOUNT = PKG_ORDERS.ORDER_TOTAL(:P_ORDER_ID) WHERE ORDER_ID = :P_ORDER_ID;\n  END\nEND";

    private static int CountWord(string s, string word)
    {
        int n = 0, i = 0;
        while (true)
        {
            i = s.IndexOf(word, i, StringComparison.OrdinalIgnoreCase);
            if (i < 0) break;
            bool leftOk = i == 0 || !(char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_');
            int end = i + word.Length;
            bool rightOk = end >= s.Length || !(char.IsLetterOrDigit(s[end]) || s[end] == '_');
            if (leftOk && rightOk) n++;
            i = end;
        }
        return n;
    }

    [Fact]
    public void PackageBody_PreservesAllBeginEnd_AndBalances()
    {
        var outp = SqlFormatter.Format(LabPackageBody);

        // No END dropped: 3 BEGIN (package + 2 routines) and 3 END, balanced.
        Assert.Equal(CountWord(LabPackageBody, "begin"), CountWord(outp, "begin"));
        Assert.Equal(CountWord(LabPackageBody, "end"), CountWord(outp, "end"));
        Assert.Equal(CountWord(outp, "begin"), CountWord(outp, "end"));
        Assert.Equal(3, CountWord(outp, "end"));

        // Both packaged routines survive.
        Assert.Contains("order_total", outp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recalc_order", outp, StringComparison.OrdinalIgnoreCase);

        // The formatted body is still ONE statement to the DDL splitter (compilable shape).
        Assert.Single(FirebirdDdlExecutor.SplitStatements(outp));
    }

    [Fact]
    public void PackageBody_IsIdempotent()
    {
        var once = SqlFormatter.Format(LabPackageBody);
        var twice = SqlFormatter.Format(once);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void PackageBody_SimpleFunction_BodyOnItsOwnLines_NotCollapsed()
    {
        var outp = SqlFormatter.Format(
            "RECREATE PACKAGE BODY P\nAS\nBEGIN\n" +
            "  FUNCTION ADD_NUMBERS(A INTEGER, B INTEGER) RETURNS INTEGER\n  AS\n  BEGIN\n    RETURN A + B;\n  END\nEND");
        // 2 BEGIN / 2 END, balanced — the function END is NOT consumed as the package END.
        Assert.Equal(2, CountWord(outp, "begin"));
        Assert.Equal(2, CountWord(outp, "end"));
        Assert.Single(FirebirdDdlExecutor.SplitStatements(outp));
    }

    [Fact]
    public void PackageHeader_ForwardDecls_StayOneStatement()
    {
        var outp = SqlFormatter.Format(
            "CREATE OR ALTER PACKAGE PKG_ORDERS\nAS\nBEGIN\n" +
            "  FUNCTION  ORDER_TOTAL(P_ORDER_ID INTEGER) RETURNS NUMERIC(15,2);\n" +
            "  PROCEDURE RECALC_ORDER(P_ORDER_ID INTEGER);\nEND");
        // Header forward-decls have no body — one BEGIN / one END (the package's).
        Assert.Equal(1, CountWord(outp, "begin"));
        Assert.Equal(1, CountWord(outp, "end"));
        Assert.Single(FirebirdDdlExecutor.SplitStatements(outp));
    }

    [Fact]
    public void StandaloneProcedure_Unaffected_ByPackageBranch()
    {
        // A regular procedure body (no packaged subprograms) still formats with one
        // balanced BEGIN/END — the new FUNCTION/PROCEDURE branch must not misfire.
        var outp = SqlFormatter.Format(
            "CREATE OR ALTER PROCEDURE SP_X(A INTEGER)\nAS\nBEGIN\n  IF (A > 0) THEN\n    A = 1;\nEND");
        Assert.Equal(1, CountWord(outp, "begin"));
        Assert.Equal(1, CountWord(outp, "end"));
    }
}
