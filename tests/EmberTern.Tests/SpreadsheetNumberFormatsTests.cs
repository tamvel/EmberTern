using EmberTern.Office;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap I9 — "is this number a date?", the only question a workbook will not answer directly (I0 / design R3).
/// <para>
/// Pinned separately from the provider because it is a pure decision and because getting it wrong is silent: a
/// false positive turns money into dates, a false negative turns dates into five-digit serials. Neither reports
/// anything (§0.1).
/// </para>
/// </summary>
public class SpreadsheetNumberFormatsTests
{
    [Theory]
    [InlineData(14u)]  // m/d/yyyy
    [InlineData(15u)]
    [InlineData(22u)]  // m/d/yy h:mm
    [InlineData(45u)]  // mm:ss
    [InlineData(47u)]  // mmss.0
    public void BuiltInDateAndTimeFormats_AreDates(uint id)
        => Assert.True(SpreadsheetNumberFormats.IsDateFormat(id, null));

    [Theory]
    [InlineData(0u)]   // General
    [InlineData(1u)]   // 0
    [InlineData(2u)]   // 0.00
    [InlineData(9u)]   // 0%
    [InlineData(13u)]  // fractions — the id just below the date block
    [InlineData(23u)]  // just above it
    [InlineData(44u)]  // accounting
    public void BuiltInNumericFormats_AreNot(uint id)
        => Assert.False(SpreadsheetNumberFormats.IsDateFormat(id, null));

    [Theory]
    [InlineData("dd\\.mm\\.yyyy")]
    [InlineData("yyyy-mm-dd")]
    [InlineData("d mmmm yyyy")]
    [InlineData("hh:mm:ss")]
    [InlineData("[h]:mm")]        // elapsed time — a bracketed section that DOES mean time
    [InlineData("h:mm AM/PM")]
    public void CustomDateCodes_AreDates(string code)
        => Assert.True(SpreadsheetNumberFormats.IsDateFormat(164u, code));

    /// <summary>
    /// ⭐ The regression this class exists for. The format is the one I0 found in the user's real file and
    /// labelled "currency, not a date" — yet a <c>Contains('d')</c> test answers TRUE, because <c>[Red]</c>
    /// contains a <c>d</c>. I0's probe never caught it because no cell in that file used the style.
    /// </summary>
    [Theory]
    [InlineData("#,##0\\ [$€-1];[Red]\\-#,##0\\ [$€-1]")]  // the real file's own format
    [InlineData("[Red]0.00")]
    [InlineData("[Blue]#,##0")]
    [InlineData("0.00;[Red]-0.00")]
    [InlineData("[$-409]#,##0.00")]                        // locale marker
    [InlineData("[<100]0;0.0")]                            // condition
    public void ColourCurrencyAndConditionMarkup_IsNotADate(string code)
        => Assert.False(SpreadsheetNumberFormats.IsDateFormat(164u, code));

    /// <summary>Quoted literals and escapes are text, not tokens — <c>0 "dni"</c> is a count of days.</summary>
    [Theory]
    [InlineData("0 \"dni\"")]
    [InlineData("0\\d")]
    [InlineData("#,##0 \"szt.\"")]
    [InlineData("0 \"hours\"")]
    public void LiteralTextAndEscapes_AreNotTokens(string code)
        => Assert.False(SpreadsheetNumberFormats.IsDateFormat(164u, code));

    [Fact]
    public void General_IsNotADate() => Assert.False(SpreadsheetNumberFormats.IsDateFormat(164u, "General"));

    [Fact]
    public void AnAbsentCode_OnACustomId_IsNotADate()
        => Assert.False(SpreadsheetNumberFormats.IsDateFormat(164u, null));

    /// <summary>A malformed code is refused rather than guessed at (§0).</summary>
    [Fact]
    public void AnUnclosedBracket_IsNotADate()
        => Assert.False(SpreadsheetNumberFormats.IsDateFormat(164u, "[Red"));
}
