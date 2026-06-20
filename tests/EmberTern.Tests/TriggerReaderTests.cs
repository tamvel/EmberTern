using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Internal Firebird trigger-reader helpers (accessible via InternalsVisibleTo).
public class TriggerReaderTests
{
    [Theory]
    [InlineData("as\nbegin x; end", "begin x; end")]
    [InlineData("AS\r\nBEGIN END", "BEGIN END")]
    [InlineData("AS begin end", "begin end")]
    [InlineData("declare variable v integer;\nbegin end", "declare variable v integer;\nbegin end")]
    [InlineData("begin end", "begin end")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void StripLeadingAs_RemovesOnlyLeadingAsWord(string? input, string expected)
    {
        Assert.Equal(expected, FirebirdDdlReader.StripLeadingAs(input).Trim());
    }

    [Fact]
    public void StripLeadingAs_DoesNotStripAsInsideWord()
    {
        // "ASSIGN" must not be treated as a leading AS.
        Assert.StartsWith("ASSIGN", FirebirdDdlReader.StripLeadingAs("ASSIGN x = 1;").Trim());
    }

    [Theory]
    // type, isBefore, insert, update, delete
    [InlineData(1, true, true, false, false)]   // BEFORE INSERT
    [InlineData(2, false, true, false, false)]  // AFTER INSERT
    [InlineData(3, true, false, true, false)]   // BEFORE UPDATE
    [InlineData(4, false, false, true, false)]  // AFTER UPDATE
    [InlineData(5, true, false, false, true)]   // BEFORE DELETE
    [InlineData(6, false, false, false, true)]  // AFTER DELETE
    [InlineData(17, true, true, true, false)]   // BEFORE INSERT OR UPDATE
    [InlineData(18, false, true, true, false)]  // AFTER INSERT OR UPDATE
    [InlineData(113, true, true, true, true)]   // BEFORE INSERT OR UPDATE OR DELETE
    [InlineData(114, false, true, true, true)]  // AFTER INSERT OR UPDATE OR DELETE
    public void DecodeTriggerHeader_DecodesTimingAndEvents(int type, bool isBefore, bool ins, bool upd, bool del)
    {
        var (b, i, u, d) = FirebirdTableDetailReader.DecodeTriggerHeader(type);
        Assert.Equal(isBefore, b);
        Assert.Equal(ins, i);
        Assert.Equal(upd, u);
        Assert.Equal(del, d);
    }

    [Theory]
    [InlineData(8192)]   // DB-level / DDL trigger — out of scope
    [InlineData(0)]
    public void DecodeTriggerHeader_NonRelationTrigger_NoEvents(int type)
    {
        var (_, i, u, d) = FirebirdTableDetailReader.DecodeTriggerHeader(type);
        Assert.False(i);
        Assert.False(u);
        Assert.False(d);
    }
}
