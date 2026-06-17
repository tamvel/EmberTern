using System.Linq;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

// Procedure Detail V1.1 Core: the bounded header parser, the body scanner
// (variables/cursors/subprograms), and the CREATE OR ALTER PROCEDURE reassembly
// generator. All pure — no DB.
public class ProcedureSourceTests
{
    // ─── ProcedureSignatureParser ─────────────────────────────────────────

    [Fact]
    public void Parse_FullSignature()
    {
        var sig = ProcedureSignatureParser.Parse(
            "CREATE OR ALTER PROCEDURE P (A INTEGER, B VARCHAR(10) NOT NULL) RETURNS (R INTEGER) AS BEGIN R = A; SUSPEND; END");

        Assert.True(sig.Success);
        Assert.Equal("P", sig.Name);
        Assert.Equal(2, sig.Inputs.Count);
        Assert.Equal("A", sig.Inputs[0].Name);
        Assert.Equal("INTEGER", sig.Inputs[0].TypeText);
        Assert.False(sig.Inputs[0].NotNull);
        Assert.Equal("B", sig.Inputs[1].Name);
        Assert.Equal("VARCHAR(10)", sig.Inputs[1].TypeText);
        Assert.True(sig.Inputs[1].NotNull);
        Assert.Single(sig.Outputs);
        Assert.Equal("R", sig.Outputs[0].Name);
        Assert.StartsWith("BEGIN", sig.Body);
    }

    [Theory]
    [InlineData("CREATE PROCEDURE P (A INTEGER = 0) AS BEGIN END", "0")]
    [InlineData("CREATE PROCEDURE P (A INTEGER DEFAULT 5) AS BEGIN END", "5")]
    [InlineData("CREATE PROCEDURE P (A INTEGER) AS BEGIN END", null)]
    public void Parse_Default(string sql, string? expected)
    {
        var sig = ProcedureSignatureParser.Parse(sql);
        Assert.True(sig.Success);
        Assert.Equal(expected, sig.Inputs[0].DefaultValue);
    }

    [Fact]
    public void Parse_NoParams()
    {
        var sig = ProcedureSignatureParser.Parse("CREATE PROCEDURE P AS BEGIN END");
        Assert.True(sig.Success);
        Assert.Empty(sig.Inputs);
        Assert.Empty(sig.Outputs);
        Assert.Equal("BEGIN END", sig.Body);
    }

    [Fact]
    public void Parse_BodyPreservesDeclarations()
    {
        var sig = ProcedureSignatureParser.Parse(
            "CREATE PROCEDURE P AS DECLARE VARIABLE X INTEGER; BEGIN X = 1; END");
        Assert.True(sig.Success);
        Assert.StartsWith("DECLARE VARIABLE X INTEGER;", sig.Body);
    }

    [Fact]
    public void Parse_FoldsUnquotedName_PreservesQuoted()
    {
        Assert.Equal("SP_BAR", ProcedureSignatureParser.Parse("create or alter procedure sp_bar as begin end").Name);
        Assert.Equal("MixedCase", ProcedureSignatureParser.Parse("CREATE PROCEDURE \"MixedCase\" AS BEGIN END").Name);
    }

    [Theory]
    [InlineData("CREATE TABLE T (X INTEGER)")]
    [InlineData("SELECT * FROM FOO")]
    [InlineData("CREATE PROCEDURE P (A INTEGER)")]   // no AS
    [InlineData("")]
    [InlineData(null)]
    public void Parse_Fail(string? sql)
        => Assert.False(ProcedureSignatureParser.Parse(sql).Success);

    // ─── BuildCreateOrAlterProcedure + round-trip ─────────────────────────

    [Fact]
    public void Build_Shape()
    {
        var sql = DdlGenerator.BuildCreateOrAlterProcedure(
            "P",
            new[] { new ProcedureParameter { Name = "A", TypeText = "INTEGER" } },
            new[] { new ProcedureParameter { Name = "R", TypeText = "INTEGER" } },
            "BEGIN\n  R = A;\n  SUSPEND;\nEND");

        Assert.Contains("CREATE OR ALTER PROCEDURE P", sql);
        Assert.Contains("RETURNS", sql);
        Assert.Contains("AS", sql);
        Assert.Contains("BEGIN", sql);
    }

    [Fact]
    public void Build_NoParams_OmitsParens()
    {
        var sql = DdlGenerator.BuildCreateOrAlterProcedure(
            "P", System.Array.Empty<ProcedureParameter>(), System.Array.Empty<ProcedureParameter>(), "BEGIN END");
        Assert.DoesNotContain("RETURNS", sql);
        // No input param block — "AS" comes right after the name line.
        Assert.Contains("CREATE OR ALTER PROCEDURE P", sql);
    }

    [Fact]
    public void Build_EmptyName_Throws()
        => Assert.Throws<System.ArgumentException>(() =>
            DdlGenerator.BuildCreateOrAlterProcedure("", System.Array.Empty<ProcedureParameter>(), System.Array.Empty<ProcedureParameter>(), "BEGIN END"));

    [Fact]
    public void Build_Then_Parse_RoundTrips()
    {
        var inputs = new[]
        {
            new ProcedureParameter { Name = "A", TypeText = "INTEGER" },
            new ProcedureParameter { Name = "B", TypeText = "VARCHAR(10)", NotNull = true, DefaultValue = "'x'" },
        };
        var outputs = new[] { new ProcedureParameter { Name = "R", TypeText = "NUMERIC(18,4)" } };
        var body = "BEGIN\n  R = A;\n  SUSPEND;\nEND";

        var sql = DdlGenerator.BuildCreateOrAlterProcedure("P", inputs, outputs, body);
        var sig = ProcedureSignatureParser.Parse(sql);

        Assert.True(sig.Success);
        Assert.Equal("P", sig.Name);
        Assert.Equal(2, sig.Inputs.Count);
        Assert.Equal("VARCHAR(10)", sig.Inputs[1].TypeText);
        Assert.True(sig.Inputs[1].NotNull);
        Assert.Equal("'x'", sig.Inputs[1].DefaultValue);
        Assert.Single(sig.Outputs);
        Assert.Equal("NUMERIC(18,4)", sig.Outputs[0].TypeText);
        Assert.Contains("SUSPEND", sig.Body);
    }

    // ─── ProcedureBodyScanner ─────────────────────────────────────────────

    [Fact]
    public void Scan_Variables()
    {
        var m = ProcedureBodyScanner.Scan(
            "DECLARE VARIABLE X INTEGER;\nDECLARE Y VARCHAR(5);\nBEGIN\n  X = 1;\nEND");
        Assert.Equal(2, m.Variables.Count);
        Assert.Equal("X", m.Variables[0].Name);
        Assert.Equal("Y", m.Variables[1].Name);
        Assert.Empty(m.Cursors);
        Assert.Empty(m.Subprograms);
    }

    [Fact]
    public void Scan_Cursor()
    {
        var m = ProcedureBodyScanner.Scan(
            "DECLARE C CURSOR FOR (SELECT ID FROM T);\nBEGIN\n  OPEN C;\nEND");
        Assert.Single(m.Cursors);
        Assert.Equal("C", m.Cursors[0].Name);
    }

    [Fact]
    public void Scan_Subprogram_AndStopsAtMainBegin()
    {
        var m = ProcedureBodyScanner.Scan(
            "DECLARE VARIABLE X INTEGER;\n" +
            "DECLARE PROCEDURE SUB (P INTEGER) AS BEGIN P = P + 1; END\n" +
            "BEGIN\n  DECLARE VARIABLE INNER INTEGER;\n  X = 1;\nEND");
        Assert.Single(m.Subprograms);
        Assert.Equal("SUB", m.Subprograms[0].Name);
        Assert.Equal("PROCEDURE", m.Subprograms[0].Detail);
        // Only the top-level X — the INNER declared after the main BEGIN is not listed.
        Assert.Single(m.Variables);
        Assert.Equal("X", m.Variables[0].Name);
    }

    [Fact]
    public void Scan_Empty_NoLocals()
    {
        var m = ProcedureBodyScanner.Scan("BEGIN END");
        Assert.Empty(m.Variables);
        Assert.Empty(m.Cursors);
        Assert.Empty(m.Subprograms);
    }

    // ─── V1.3: source capture (split-view editors) ────────────────────────

    [Fact]
    public void Scan_CapturesSource()
    {
        var m = ProcedureBodyScanner.Scan(
            "declare variable x integer;\n" +
            "declare c cursor for (select 1 from rdb$database);\n" +
            "declare procedure sub as begin end\n" +
            "begin x = 1; end");
        Assert.Equal("declare variable x integer;", m.Variables[0].Source);
        Assert.Contains("cursor for", m.Cursors[0].Source);
        Assert.StartsWith("declare procedure sub", m.Subprograms[0].Source);
        Assert.Contains("begin end", m.Subprograms[0].Source);
    }

    // ─── V1.3: Comment Body / Uncomment Body (disable/enable body) ────────

    [Fact]
    public void FindOuterBodyContent_BetweenBeginEnd()
    {
        var range = ProcedureBodyScanner.FindOuterBodyContent("begin x = 1; end");
        Assert.NotNull(range);
        var content = "begin x = 1; end".Substring(range!.Value.Start, range.Value.End - range.Value.Start);
        Assert.Contains("x = 1;", content);
        Assert.DoesNotContain("begin", content.ToLowerInvariant());
        Assert.DoesNotContain("end", content.ToLowerInvariant());
    }

    [Fact]
    public void FindOuterBodyContent_NoBeginEnd_Null()
        => Assert.Null(ProcedureBodyScanner.FindOuterBodyContent("select 1 from rdb$database"));

    [Fact]
    public void CommentBody_WrapsBody_Idempotent()
    {
        var body = "begin\n    a = 1;\n    b = 2;\nend";
        var commented = ProcedureBodyScanner.CommentBody(body);
        Assert.NotNull(commented);
        Assert.Contains("/*", commented!);
        Assert.Contains("*/", commented);
        Assert.Contains("a = 1;", commented);     // inner statements preserved
        Assert.StartsWith("begin", commented);
        Assert.EndsWith("end", commented.TrimEnd());

        // Already wrapped → no-op.
        Assert.Null(ProcedureBodyScanner.CommentBody(commented));
    }

    [Fact]
    public void UncommentBody_StripsOuterWrapper()
    {
        var body = "begin\n    a = 1;\nend";
        var commented = ProcedureBodyScanner.CommentBody(body)!;
        var restored = ProcedureBodyScanner.UncommentBody(commented);
        Assert.NotNull(restored);
        Assert.DoesNotContain("/*", restored!);
        Assert.DoesNotContain("*/", restored);
        Assert.Contains("a = 1;", restored);
    }

    [Fact]
    public void UncommentBody_NotWrapped_Null()
        => Assert.Null(ProcedureBodyScanner.UncommentBody("begin a = 1; end"));

    [Fact]
    public void CommentBody_WorksOnFullSource()
    {
        // Source mode: the body BEGIN…END after the header is found + wrapped.
        var src = "create or alter procedure p\nas\nbegin\n    a = 1;\nend";
        var commented = ProcedureBodyScanner.CommentBody(src);
        Assert.NotNull(commented);
        Assert.Contains("/*", commented!);
        Assert.Contains("a = 1;", commented);
        Assert.StartsWith("create or alter procedure", commented);
    }
}
