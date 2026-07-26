using System.Text;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I2: the row validator (pipeline step 4) and the connection-charset guard.
/// <para>
/// The charset cases are the most important tests in the module. I0 measured that a character absent from the
/// CONNECTION charset is written as <c>?</c> with <b>no error at all</b> — even into a UTF8 column — so a
/// validator built on a default .NET encoder would confirm the value "fits" and reproduce the exact corruption
/// it exists to prevent (design R1). <see cref="Guard_WithoutTheExceptionFallback_WouldSilentlyCorrupt"/> pins
/// that failure mode itself, so nobody can "simplify" the fallback away without a test telling them what they
/// just re-enabled.
/// </para>
/// </summary>
public class ImportRowValidatorTests
{
    private static readonly ImportBehaviorOptions Strict = new();
    private static readonly ImportBehaviorOptions Trimming = new() { TrimTooLongValues = true };

    private static ColumnSpec Col(string type, bool notNull = false, string? defaultValue = null)
        => new("C", type, null, notNull) { DefaultValue = defaultValue };

    private static ImportValueResult Validate(
        object? value, ColumnSpec column, ImportBehaviorOptions? behavior = null, string? charset = null)
        => ImportRowValidator.Validate(
            value, column, behavior ?? Strict,
            charset is null ? null : ImportCharsetGuard.Strict(charset));

    // ── NOT NULL ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Null_IntoANullableColumn_IsFine()
        => Assert.True(Validate(null, Col("INTEGER")).IsSuccess);

    [Fact]
    public void Null_IntoANotNullColumn_IsRefused()
        => Assert.Equal(ImportErrorKind.NullNotAllowed, Validate(null, Col("INTEGER", notNull: true)).Kind);

    /// <summary>
    /// ⭐ The subtle one. A MAPPED column appears in the INSERT's field list, so a null value is written AS
    /// NULL and the column's DEFAULT never applies — the row fails on the server. So nullability is checked
    /// here even when the column has a default. ("An UNMAPPED column with a default is fine" is a different
    /// question entirely, answered once, by the mapping planner, before any row is read.)
    /// </summary>
    [Fact]
    public void Null_IntoANotNullColumnThatHasADefault_IsStillRefused()
    {
        var column = Col("INTEGER", notNull: true, defaultValue: "0");
        Assert.Equal(ImportErrorKind.NullNotAllowed, Validate(null, column).Kind);
    }

    // ── Length ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Text_WithinTheLimit_Passes()
        => Assert.True(Validate("abcde", Col("VARCHAR(5)")).IsSuccess);

    [Fact]
    public void Text_OverTheLimit_IsAnErrorByDefault()
    {
        var result = Validate("abcdefg", Col("VARCHAR(5)"));
        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorKind.ValueTooLong, result.Kind);
        Assert.Equal("abcdefg", result.RawText);
    }

    /// <summary>§0.2: trimming exists only as an explicit choice, and every shortened row is still reported —
    /// carrying the ORIGINAL value, because a report showing the truncated text would hide the loss.</summary>
    [Fact]
    public void Text_OverTheLimit_IsShortenedOnlyWhenAskedFor_AndSaysSo()
    {
        var result = Validate("abcdefg", Col("VARCHAR(5)"), Trimming);

        Assert.True(result.IsSuccess);
        Assert.True(result.WasTrimmed);
        Assert.Equal("abcde", result.Value);
        Assert.Equal("abcdefg", result.RawText);
    }

    [Fact]
    public void TextBlob_HasNoLengthLimit()
        => Assert.True(Validate(new string('x', 100_000), Col("BLOB SUB_TYPE TEXT")).IsSuccess);

    // ── Precision and scale ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Rounding 1.555 to 1.56 on the way in is exactly the silent conversion §0.1 forbids. There is
    /// deliberately no "round it anyway" option — adding one is a design decision, not an implementation
    /// detail.</summary>
    [Fact]
    public void Decimal_WithMoreDecimalsThanTheColumnKeeps_IsRefused()
        => Assert.Equal(
            ImportErrorKind.PrecisionWouldBeLost, Validate(1.555m, Col("NUMERIC(15,2)")).Kind);

    /// <summary>⭐ 1.50 and 1.5 are the SAME number. Comparing stored scale rather than value would refuse a
    /// perfectly exact figure — a false positive on ordinary money data.</summary>
    [Fact]
    public void Decimal_WithATrailingZero_IsNotAPrecisionLoss()
    {
        Assert.True(Validate(1.50m, Col("NUMERIC(15,1)")).IsSuccess);
        Assert.True(Validate(1.500m, Col("NUMERIC(15,2)")).IsSuccess);
    }

    [Fact]
    public void Decimal_BeyondTheColumnsPrecision_IsOutOfRange()
    {
        // NUMERIC(5,2) holds at most 999.99.
        Assert.Equal(ImportErrorKind.ValueOutOfRange, Validate(12345.67m, Col("NUMERIC(5,2)")).Kind);
        Assert.True(Validate(999.99m, Col("NUMERIC(5,2)")).IsSuccess);
    }

    [Fact]
    public void Decimal_WithNoDeclaredScale_KeepsWholeNumbersOnly()
    {
        Assert.True(Validate(12m, Col("NUMERIC(9)")).IsSuccess);
        Assert.Equal(ImportErrorKind.PrecisionWouldBeLost, Validate(12.5m, Col("NUMERIC(9)")).Kind);
    }

    // ── Connection charset (design R1 / REK-2) ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ The failure mode this whole guard exists for, pinned as a test so it cannot be re-introduced by
    /// "simplifying" the encoder. A default .NET encoder SUBSTITUTES an unrepresentable character — exactly as
    /// the Firebird connection does — and the round trip below proves it turns real data into <c>?</c> with no
    /// error anywhere. That is why <see cref="ImportCharsetGuard.Strict"/> must use
    /// <c>EncoderFallback.ExceptionFallback</c>.
    /// </summary>
    [Fact]
    public void Guard_WithoutTheExceptionFallback_WouldSilentlyCorrupt()
    {
        var lenient = Encoding.GetEncoding("windows-1250");
        var round = lenient.GetString(lenient.GetBytes("Ж"));

        Assert.Equal("?", round);       // the corruption, reproduced
        Assert.NotEqual("Ж", round);
    }

    [Fact]
    public void Charset_RefusesACharacterTheConnectionCannotCarry()
    {
        var result = Validate("Ж", Col("VARCHAR(20)"), charset: "WIN1250");

        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorKind.NotRepresentableInConnectionCharset, result.Kind);
    }

    /// <summary>Polish text IS representable in WIN1250 — the guard must not fire on the user's everyday
    /// data, or it would be turned off within a day.</summary>
    [Fact]
    public void Charset_AcceptsWhatTheConnectionCanCarry()
        => Assert.True(Validate("Zażółć gęślą jaźń", Col("VARCHAR(50)"), charset: "WIN1250").IsSuccess);

    [Fact]
    public void Charset_Utf8CarriesEverything()
        => Assert.True(Validate("Ж 中 😀", Col("VARCHAR(50)"), charset: "UTF8").IsSuccess);

    /// <summary>⭐ The measurement that surprised everyone: the CONNECTION charset decides, not the column's.
    /// A UTF8 column reached over a WIN1250 connection still receives <c>?</c>, so the check is driven by the
    /// connection alone.</summary>
    [Fact]
    public void Charset_IsDecidedByTheConnection_NotByTheColumn()
    {
        var wide = Col("BLOB SUB_TYPE TEXT");
        Assert.False(Validate("Ж", wide, charset: "WIN1250").IsSuccess);
        Assert.True(Validate("Ж", wide, charset: "UTF8").IsSuccess);
    }

    [Fact]
    public void Charset_CheckIsSkippedWhenNoConnectionEncodingIsSupplied()
        => Assert.True(Validate("Ж", Col("VARCHAR(20)")).IsSuccess);

    [Fact]
    public void Charset_CountsUnrepresentableSamplesForTheReadinessStrip()
    {
        var encoding = ImportCharsetGuard.Strict("WIN1250");
        var count = ImportCharsetGuard.CountUnrepresentable(
            new[] { "ok", "Ж", null, "też ok", "中" }, encoding);

        Assert.Equal(2, count);
        Assert.Equal(0, ImportCharsetGuard.CountUnrepresentable(new[] { "Ж" }, ImportCharsetGuard.Strict("UTF8")));
    }

    // ── Trimming interacts with the charset check in the right order ────────────────────────────────────

    /// <summary>The charset check runs on the value that will actually be SENT, so a trim that removes the
    /// offending character must clear the error rather than the other way round.</summary>
    [Fact]
    public void TrimmedValue_IsTheOneCharsetCheckedAgainst()
    {
        var result = Validate("abcdeЖ", Col("VARCHAR(5)"), Trimming, charset: "WIN1250");

        Assert.True(result.IsSuccess);
        Assert.Equal("abcde", result.Value);
    }
}
