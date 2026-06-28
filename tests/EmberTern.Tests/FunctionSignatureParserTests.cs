using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class FunctionSignatureParserTests
{
    [Fact]
    public void Parse_CanonicalForm()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION ADD_ONE (A INTEGER)\nRETURNS INTEGER\nAS\nBEGIN\n  RETURN A + 1;\nEND");

        Assert.True(sig.Success);
        Assert.Equal("ADD_ONE", sig.Name);
        Assert.Single(sig.Arguments);
        Assert.Equal("A", sig.Arguments[0].Name);
        Assert.Equal("INTEGER", sig.Arguments[0].TypeText);
        Assert.Equal("INTEGER", sig.ReturnType);
        Assert.False(sig.Deterministic);
        Assert.StartsWith("BEGIN", sig.Body);
    }

    [Fact]
    public void Parse_CreateOrAlter()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE OR ALTER FUNCTION F RETURNS INTEGER AS BEGIN RETURN 0; END");
        Assert.True(sig.Success);
        Assert.Equal("F", sig.Name);
        Assert.Equal("INTEGER", sig.ReturnType);
    }

    [Fact]
    public void Parse_NoArguments()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION PI RETURNS DOUBLE PRECISION AS BEGIN RETURN 3.14; END");
        Assert.True(sig.Success);
        Assert.Empty(sig.Arguments);
        Assert.Equal("DOUBLE PRECISION", sig.ReturnType);
    }

    [Fact]
    public void Parse_EmptyArgumentList()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION F() RETURNS INTEGER AS BEGIN RETURN 1; END");
        Assert.True(sig.Success);
        Assert.Empty(sig.Arguments);
    }

    [Fact]
    public void Parse_MultipleArguments_WithNotNullAndDefault()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION CALC (A INTEGER NOT NULL, B NUMERIC(15,2) = 0)\nRETURNS NUMERIC(15,2)\nAS\nBEGIN\n  RETURN A * B;\nEND");

        Assert.True(sig.Success);
        Assert.Equal(2, sig.Arguments.Count);
        Assert.Equal("A", sig.Arguments[0].Name);
        Assert.True(sig.Arguments[0].NotNull);
        Assert.Equal("B", sig.Arguments[1].Name);
        Assert.Equal("NUMERIC(15,2)", sig.Arguments[1].TypeText);
        Assert.Equal("0", sig.Arguments[1].DefaultValue);
        Assert.Equal("NUMERIC(15,2)", sig.ReturnType);
    }

    [Fact]
    public void Parse_ReturnVarcharWithParens()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION GREET (N VARCHAR(50)) RETURNS VARCHAR(100) AS BEGIN RETURN 'hi'; END");
        Assert.True(sig.Success);
        Assert.Equal("VARCHAR(100)", sig.ReturnType);
        Assert.Equal("VARCHAR(50)", sig.Arguments[0].TypeText);
    }

    [Fact]
    public void Parse_ReturnDomain()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION F RETURNS T_MONEY AS BEGIN RETURN 0; END");
        Assert.True(sig.Success);
        Assert.Equal("T_MONEY", sig.ReturnType);
    }

    [Fact]
    public void Parse_ReturnTypeOfColumn()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION F RETURNS TYPE OF COLUMN CUSTOMERS.NAME AS BEGIN RETURN NULL; END");
        Assert.True(sig.Success);
        Assert.Equal("TYPE OF COLUMN CUSTOMERS.NAME", sig.ReturnType);
    }

    [Fact]
    public void Parse_Deterministic()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION F RETURNS INTEGER DETERMINISTIC AS BEGIN RETURN 1; END");
        Assert.True(sig.Success);
        Assert.True(sig.Deterministic);
        Assert.Equal("INTEGER", sig.ReturnType);
    }

    [Fact]
    public void Parse_NotDeterministic_StripsFlagAndKeepsType()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION F RETURNS INTEGER NOT DETERMINISTIC AS BEGIN RETURN 1; END");
        Assert.True(sig.Success);
        Assert.False(sig.Deterministic);
        Assert.Equal("INTEGER", sig.ReturnType);
    }

    [Fact]
    public void Parse_QuotedName_PreservesCase()
    {
        var sig = FunctionSignatureParser.Parse(
            "CREATE FUNCTION \"myFn\" RETURNS INTEGER AS BEGIN RETURN 1; END");
        Assert.True(sig.Success);
        Assert.Equal("myFn", sig.Name);
    }

    [Fact]
    public void Parse_UnquotedName_FoldsUppercase()
    {
        var sig = FunctionSignatureParser.Parse(
            "create function lower_name returns integer as begin return 1; end");
        Assert.True(sig.Success);
        Assert.Equal("LOWER_NAME", sig.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CREATE PROCEDURE P AS BEGIN END")]              // not a function
    [InlineData("CREATE FUNCTION F AS BEGIN END")]               // no RETURNS
    [InlineData("CREATE FUNCTION F RETURNS INTEGER")]            // no AS (e.g. external/UDF)
    [InlineData("CREATE FUNCTION RETURNS INTEGER AS BEGIN END")] // no name
    public void Parse_Failures(string? sql) => Assert.False(FunctionSignatureParser.Parse(sql).Success);
}
