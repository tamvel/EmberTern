using System;
using System.Collections.Generic;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class FunctionDdlGeneratorTests
{
    private static List<ProcedureParameter> Args(params (string Name, string Type)[] items)
    {
        var list = new List<ProcedureParameter>();
        foreach (var (n, t) in items) list.Add(new ProcedureParameter { Name = n, TypeText = t });
        return list;
    }

    [Fact]
    public void Build_WithArgs_EmitsHeaderReturnsAndBody()
    {
        var sql = DdlGenerator.BuildCreateOrAlterFunction(
            "ADD_ONE", Args(("A", "INTEGER")), "INTEGER", deterministic: false,
            body: "BEGIN\n  RETURN A + 1;\nEND");

        Assert.Contains("CREATE OR ALTER FUNCTION ADD_ONE", sql);
        Assert.Contains("A INTEGER", sql);
        Assert.Contains("RETURNS INTEGER", sql);
        Assert.Contains("AS", sql);
        Assert.Contains("RETURN A + 1;", sql);
        Assert.DoesNotContain("DETERMINISTIC", sql);
    }

    [Fact]
    public void Build_NoArgs_OmitsArgumentBlock()
    {
        var sql = DdlGenerator.BuildCreateOrAlterFunction(
            "PI", Args(), "DOUBLE PRECISION", deterministic: false, body: "BEGIN RETURN 3.14; END");

        Assert.Contains("CREATE OR ALTER FUNCTION PI", sql);
        Assert.Contains("RETURNS DOUBLE PRECISION", sql);
        // No argument parens before RETURNS.
        Assert.DoesNotContain("(", sql.Substring(0, sql.IndexOf("RETURNS", StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_Deterministic_AppendsKeyword()
    {
        var sql = DdlGenerator.BuildCreateOrAlterFunction(
            "F", Args(), "INTEGER", deterministic: true, body: "BEGIN RETURN 1; END");
        Assert.Contains("RETURNS INTEGER DETERMINISTIC", sql);
    }

    [Fact]
    public void Build_DomainReturn()
    {
        var sql = DdlGenerator.BuildCreateOrAlterFunction(
            "F", Args(), "T_MONEY", deterministic: false, body: "BEGIN RETURN 0; END");
        Assert.Contains("RETURNS T_MONEY", sql);
    }

    [Fact]
    public void Build_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() =>
            DdlGenerator.BuildCreateOrAlterFunction(" ", Args(), "INTEGER", false, "BEGIN END"));

    [Fact]
    public void Build_EmptyReturnType_Throws()
        => Assert.Throws<ArgumentException>(() =>
            DdlGenerator.BuildCreateOrAlterFunction("F", Args(), " ", false, "BEGIN END"));

    [Fact]
    public void Build_RoundTripsThroughParser()
    {
        var sql = DdlGenerator.BuildCreateOrAlterFunction(
            "CALC", Args(("A", "INTEGER"), ("B", "NUMERIC(15,2)")), "NUMERIC(15,2)",
            deterministic: true, body: "BEGIN\n  RETURN A * B;\nEND");

        var sig = FunctionSignatureParser.Parse(sql);
        Assert.True(sig.Success);
        Assert.Equal("CALC", sig.Name);
        Assert.Equal(2, sig.Arguments.Count);
        Assert.Equal("A", sig.Arguments[0].Name);
        Assert.Equal("INTEGER", sig.Arguments[0].TypeText);
        Assert.Equal("B", sig.Arguments[1].Name);
        Assert.Equal("NUMERIC(15,2)", sig.Arguments[1].TypeText);
        Assert.Equal("NUMERIC(15,2)", sig.ReturnType);
        Assert.True(sig.Deterministic);
        Assert.StartsWith("BEGIN", sig.Body);
    }

    [Fact]
    public void BuildCommentFunction_EmitsCommentOnFunction()
    {
        var sql = DdlGenerator.BuildCommentFunction("MY_FN", "does a thing");
        Assert.Contains("COMMENT ON FUNCTION \"MY_FN\"", sql);
        Assert.Contains("does a thing", sql);
    }

    [Fact]
    public void BuildCommentFunction_NullComment_EmitsNull()
    {
        var sql = DdlGenerator.BuildCommentFunction("MY_FN", null);
        Assert.Contains("COMMENT ON FUNCTION \"MY_FN\"", sql);
        Assert.Contains("IS NULL", sql);
    }
}
