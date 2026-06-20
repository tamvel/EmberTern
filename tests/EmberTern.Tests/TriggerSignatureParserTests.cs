using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class TriggerSignatureParserTests
{
    [Fact]
    public void Parse_CanonicalForm()
    {
        var sig = TriggerSignatureParser.Parse(
            "CREATE OR ALTER TRIGGER MY_TRIG FOR CUSTOMERS\nACTIVE AFTER INSERT OR UPDATE POSITION 7\nAS\nBEGIN\nEND");

        Assert.True(sig.Success);
        Assert.Equal("MY_TRIG", sig.Name);
        Assert.Equal("CUSTOMERS", sig.Table);
        Assert.False(sig.IsBefore);
        Assert.True(sig.FiresInsert);
        Assert.True(sig.FiresUpdate);
        Assert.False(sig.FiresDelete);
        Assert.Equal(7, sig.Position);
        Assert.True(sig.Active);
        Assert.StartsWith("BEGIN", sig.Body);
    }

    [Fact]
    public void Parse_ReaderEmittedForm_InactiveBeforeFor()
    {
        // The DDL reader can emit "[INACTIVE] FOR table" (ACTIVE/INACTIVE before FOR).
        var sig = TriggerSignatureParser.Parse(
            "CREATE OR ALTER TRIGGER T INACTIVE FOR ORDERS BEFORE DELETE POSITION 0\nAS\nBEGIN END");

        Assert.True(sig.Success);
        Assert.Equal("ORDERS", sig.Table);
        Assert.True(sig.IsBefore);
        Assert.False(sig.FiresInsert);
        Assert.True(sig.FiresDelete);
        Assert.False(sig.Active);
        Assert.Equal(0, sig.Position);
    }

    [Fact]
    public void Parse_AllThreeEvents()
    {
        var sig = TriggerSignatureParser.Parse(
            "CREATE TRIGGER T FOR T1 BEFORE INSERT OR UPDATE OR DELETE AS BEGIN END");
        Assert.True(sig.Success);
        Assert.True(sig.FiresInsert);
        Assert.True(sig.FiresUpdate);
        Assert.True(sig.FiresDelete);
    }

    [Fact]
    public void Parse_NoPosition_DefaultsToZero()
    {
        var sig = TriggerSignatureParser.Parse("CREATE TRIGGER T FOR T1 AFTER UPDATE AS BEGIN END");
        Assert.True(sig.Success);
        Assert.Equal(0, sig.Position);
    }

    [Fact]
    public void Parse_QuotedIdentifiers_PreserveCase()
    {
        var sig = TriggerSignatureParser.Parse(
            "CREATE OR ALTER TRIGGER \"myTrig\" FOR \"myTable\" BEFORE INSERT AS BEGIN END");
        Assert.Equal("myTrig", sig.Name);
        Assert.Equal("myTable", sig.Table);
    }

    [Fact]
    public void Parse_AlterAndRecreate()
    {
        Assert.True(TriggerSignatureParser.Parse("ALTER TRIGGER T FOR X BEFORE INSERT AS BEGIN END").Success);
        Assert.True(TriggerSignatureParser.Parse("RECREATE TRIGGER T FOR X BEFORE INSERT AS BEGIN END").Success);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SELECT 1 FROM RDB$DATABASE")]
    [InlineData("CREATE PROCEDURE P AS BEGIN END")]
    [InlineData("CREATE TRIGGER T ON DATABASE AFTER CONNECT AS BEGIN END")] // DB-level: out of scope
    [InlineData("CREATE TRIGGER T FOR X AS BEGIN END")]                     // missing timing
    [InlineData("CREATE TRIGGER T BEFORE INSERT AS BEGIN END")]            // missing FOR table
    public void Parse_Failures(string? sql)
    {
        Assert.False(TriggerSignatureParser.Parse(sql).Success);
    }

    [Fact]
    public void Parse_BodyExcludesHeaderAs()
    {
        var sig = TriggerSignatureParser.Parse(
            "CREATE TRIGGER T FOR X BEFORE INSERT AS\nDECLARE VARIABLE V INTEGER;\nBEGIN\n  V = 1;\nEND");
        Assert.True(sig.Success);
        Assert.StartsWith("DECLARE VARIABLE V INTEGER;", sig.Body);
        Assert.Contains("BEGIN", sig.Body);
    }
}
