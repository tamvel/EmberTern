using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Completion case must follow the user's ACTUAL writing style, not just the character before the
/// caret. The reported bug: after a qualifier dot the typed prefix is empty, so the dot read as the
/// start of a fresh word and the catalog's UPPERCASE won — dropping ID_KONTRAHENT into an
/// all-lowercase query.
/// </summary>
public class SqlCaseStyleTests
{
    // ── The reported scenario, end to end ──
    private const string LowerQuery = "select *\nfrom kontrahent k\nwhere k.";

    [Fact]
    public void AfterDot_InLowercaseQuery_CompletesLowercase()
    {
        var style = SqlCaseStyleDetector.Detect(LowerQuery);
        Assert.Equal(SqlCaseStyle.Lower, style);
        // Empty typed prefix — exactly what the completion segment holds right after "k."
        Assert.Equal("id_kontrahent", CaseMatcher.Match("", "ID_KONTRAHENT", style));
    }

    [Fact]
    public void AfterDot_InUppercaseQuery_CompletesUppercase()
    {
        const string upper = "SELECT *\nFROM KONTRAHENT K\nWHERE K.";
        var style = SqlCaseStyleDetector.Detect(upper);
        Assert.Equal(SqlCaseStyle.Upper, style);
        Assert.Equal("ID_KONTRAHENT", CaseMatcher.Match("", "ID_KONTRAHENT", style));
    }

    // ── The typed prefix still wins when it has letters (unchanged, strongest signal) ──
    [Theory]
    [InlineData("id_", "id_kontrahent")]   // typing lowercase in a lowercase doc
    [InlineData("ID_", "ID_KONTRAHENT")]   // typing UPPERCASE overrides a lowercase doc
    public void TypedPrefix_OverridesDocumentStyle(string typed, string expected)
        => Assert.Equal(expected, CaseMatcher.Match(typed, "ID_KONTRAHENT", SqlCaseStyle.Lower));

    // ── The detector ──
    [Fact]
    public void Identifiers_OutvoteKeywords()
    {
        // Lowercase keywords but UPPERCASE identifiers → the user writes identifiers uppercase.
        var style = SqlCaseStyleDetector.Detect("select * from KONTRAHENT K where K.");
        Assert.Equal(SqlCaseStyle.Upper, style);
    }

    [Fact]
    public void KeywordsAreTheFallback_WhenNoIdentifiersYet()
    {
        Assert.Equal(SqlCaseStyle.Lower, SqlCaseStyleDetector.Detect("select "));
        Assert.Equal(SqlCaseStyle.Upper, SqlCaseStyleDetector.Detect("SELECT "));
    }

    [Fact]
    public void MixedCaseWords_VoteForNothing()
        => Assert.Equal(SqlCaseStyle.Unknown, SqlCaseStyleDetector.Detect("Select * From Kontrahent"));

    [Fact]
    public void StringLiteralsAndComments_AreNotCounted()
    {
        // The lexer excludes them, so the lowercase words inside must not swing the vote.
        var style = SqlCaseStyleDetector.Detect("SELECT * FROM KONTRAHENT WHERE NAZWA = 'abc def' -- ala ma kota");
        Assert.Equal(SqlCaseStyle.Upper, style);
    }

    [Fact]
    public void EmptyOrUnknown_KeepsCatalogCasing()
    {
        Assert.Equal(SqlCaseStyle.Unknown, SqlCaseStyleDetector.Detect(""));
        Assert.Equal("ID_KONTRAHENT", CaseMatcher.Match("", "ID_KONTRAHENT", SqlCaseStyle.Unknown));
    }
}
