using System;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

public class TriggerDdlGeneratorTests
{
    // ─── BuildTriggerName (auto-name mapping per spec) ────────────────────

    [Theory]
    [InlineData("CUSTOMERS", true, true, false, false, 10, "CUSTOMERS_BI10")]
    [InlineData("CUSTOMERS", false, false, true, false, 100, "CUSTOMERS_AU100")]
    [InlineData("ORDERS", true, true, true, true, 50, "ORDERS_BIUD50")]
    [InlineData("CUSTOMERS", false, true, true, true, 100, "CUSTOMERS_AIUD100")]
    [InlineData("STANMAG", true, false, true, false, 99, "STANMAG_BU99")]
    [InlineData("T", true, false, true, true, 0, "T_BUD0")]
    public void BuildTriggerName_Maps(string table, bool isBefore, bool ins, bool upd, bool del, int pos, string expected)
    {
        Assert.Equal(expected, DdlGenerator.BuildTriggerName(table, isBefore, ins, upd, del, pos));
    }

    // ─── BuildCreateOrAlterTrigger ────────────────────────────────────────

    [Fact]
    public void BuildCreateOrAlterTrigger_FullShape()
    {
        var sql = DdlGenerator.BuildCreateOrAlterTrigger(
            "MY_TRIG", "CUSTOMERS", isBefore: true, insert: true, update: true, delete: false,
            position: 5, active: true, body: "BEGIN\n  /* x */\nEND");

        Assert.Contains("CREATE OR ALTER TRIGGER MY_TRIG FOR CUSTOMERS", sql);
        Assert.Contains("ACTIVE BEFORE INSERT OR UPDATE POSITION 5", sql);
        Assert.Contains("AS", sql);
        Assert.Contains("BEGIN", sql);
        Assert.DoesNotContain("DELETE", sql);
    }

    [Fact]
    public void BuildCreateOrAlterTrigger_Inactive_AfterDelete()
    {
        var sql = DdlGenerator.BuildCreateOrAlterTrigger(
            "T", "X", isBefore: false, insert: false, update: false, delete: true,
            position: 0, active: false, body: "BEGIN END");
        Assert.Contains("INACTIVE AFTER DELETE POSITION 0", sql);
    }

    [Fact]
    public void BuildCreateOrAlterTrigger_QuotesLowercaseIdentifiers()
    {
        var sql = DdlGenerator.BuildCreateOrAlterTrigger(
            "myTrig", "myTable", isBefore: true, insert: true, update: false, delete: false,
            position: 0, active: true, body: "BEGIN END");
        Assert.Contains("\"myTrig\"", sql);
        Assert.Contains("\"myTable\"", sql);
    }

    [Fact]
    public void BuildCreateOrAlterTrigger_EmptyBody_FallsBackToBeginEnd()
    {
        var sql = DdlGenerator.BuildCreateOrAlterTrigger(
            "T", "X", isBefore: true, insert: true, update: false, delete: false,
            position: 0, active: true, body: "");
        Assert.Contains("BEGIN\nEND", sql);
    }

    [Theory]
    [InlineData("", "X", true)]
    [InlineData("T", "", true)]
    [InlineData("T", "X", false)] // no events
    public void BuildCreateOrAlterTrigger_Validates(string name, string table, bool anyEvent)
    {
        Assert.Throws<ArgumentException>(() => DdlGenerator.BuildCreateOrAlterTrigger(
            name, table, isBefore: true, insert: anyEvent, update: false, delete: false,
            position: 0, active: true, body: "BEGIN END"));
    }

    // ─── BuildCommentTrigger ──────────────────────────────────────────────

    [Fact]
    public void BuildCommentTrigger_WithText()
    {
        var sql = DdlGenerator.BuildCommentTrigger("MY_TRIG", "audit hook");
        Assert.Equal("COMMENT ON TRIGGER \"MY_TRIG\" IS 'audit hook'", sql);
    }

    [Fact]
    public void BuildCommentTrigger_Null_EmitsIsNull()
    {
        var sql = DdlGenerator.BuildCommentTrigger("MY_TRIG", null);
        Assert.Equal("COMMENT ON TRIGGER \"MY_TRIG\" IS NULL", sql);
    }

    [Fact]
    public void BuildCommentTrigger_EscapesQuotes()
    {
        var sql = DdlGenerator.BuildCommentTrigger("T", "it's a hook");
        Assert.Contains("'it''s a hook'", sql);
    }
}
