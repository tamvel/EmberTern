using System;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I2: the strict value converter, and the type resolver underneath it.
/// <para>
/// What these tests really pin is §0.1: <b>a value the module cannot convert with certainty is a row error,
/// never a guess.</b> Several cases below assert a FAILURE for input that a lenient parser would happily
/// accept — <c>"1.5"</c> under a comma decimal separator is the headline one. Those are not gaps; they are the
/// feature, and loosening one of them silently rewrites the user's data.
/// </para>
/// </summary>
public class ImportValueConverterTests
{
    private static readonly ImportCultureOptions Pl = new();
    private static readonly ImportCultureOptions En = new() { DecimalSeparator = '.' };

    private static ColumnSpec Col(string type, bool notNull = false) => new("C", type, null, notNull);

    private static ImportValueResult Convert(object? raw, string type, ImportCultureOptions? culture = null)
        => ImportValueConverter.Convert(raw, Col(type), culture ?? Pl);

    // ── The type resolver ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SMALLINT", SqlValueKind.Integer)]
    [InlineData("INTEGER", SqlValueKind.Integer)]
    [InlineData("BIGINT", SqlValueKind.Integer)]
    [InlineData("NUMERIC(15,2)", SqlValueKind.Decimal)]
    [InlineData("DECIMAL(9,3)", SqlValueKind.Decimal)]
    [InlineData("FLOAT", SqlValueKind.Float)]
    [InlineData("DOUBLE PRECISION", SqlValueKind.Float)]
    [InlineData("CHAR(3)", SqlValueKind.Text)]
    [InlineData("VARCHAR(20)", SqlValueKind.Text)]
    [InlineData("DATE", SqlValueKind.Date)]
    [InlineData("TIME", SqlValueKind.Time)]
    [InlineData("TIMESTAMP", SqlValueKind.Timestamp)]
    [InlineData("BOOLEAN", SqlValueKind.Boolean)]
    [InlineData("BLOB SUB_TYPE TEXT", SqlValueKind.TextBlob)]
    [InlineData("BLOB SUB_TYPE BINARY", SqlValueKind.BinaryBlob)]
    public void Resolve_MapsTheTypesTheCatalogEmits(string type, SqlValueKind expected)
        => Assert.Equal(expected, ImportTargetType.Resolve(type).Kind);

    /// <summary>
    /// ⭐ The Unknown set is a DECISION, and it must match the export side's: a type export refuses to write is
    /// a type import must refuse to fill. A zoned timestamp read through the plain-TIMESTAMP path would lose
    /// its offset silently.
    /// </summary>
    [Theory]
    [InlineData("INT128")]
    [InlineData("TIMESTAMP WITH TIME ZONE")]
    [InlineData("TIME WITH TIME ZONE")]
    [InlineData("BLOB SUB_TYPE 2")]
    [InlineData("")]
    public void Resolve_RefusesTypesWithNoFaithfulImportPath(string type)
        => Assert.False(ImportTargetType.Resolve(type).IsSupported);

    [Fact]
    public void Resolve_ReadsSizeAndScale()
    {
        var numeric = ImportTargetType.Resolve("NUMERIC(15,2)");
        Assert.Equal(15, numeric.Size);
        Assert.Equal(2, numeric.Scale);
        Assert.Equal(15, numeric.NumericPrecision);
        Assert.Equal(2, numeric.NumericScale);

        var varchar = ImportTargetType.Resolve("VARCHAR(20)");
        Assert.Equal(20, varchar.MaxTextLength);

        // A text BLOB is unbounded — inventing a limit it does not have would refuse legal data.
        Assert.Null(ImportTargetType.Resolve("BLOB SUB_TYPE TEXT").MaxTextLength);
    }

    [Theory]
    [InlineData("SMALLINT", short.MinValue, short.MaxValue)]
    [InlineData("INTEGER", int.MinValue, int.MaxValue)]
    [InlineData("BIGINT", long.MinValue, long.MaxValue)]
    public void Resolve_KnowsEachIntegerWidthsRange(string type, long min, long max)
        => Assert.Equal((min, max), ImportTargetType.Resolve(type).IntegerRange);

    // ── NULL ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Null_And_DBNull_BecomeSqlNull()
    {
        Assert.True(Convert(null, "INTEGER").IsSuccess);
        Assert.Null(Convert(null, "INTEGER").Value);
        Assert.Null(Convert(DBNull.Value, "INTEGER").Value);
    }

    /// <summary>An empty field is the ABSENCE of a value, so it becomes NULL rather than "not a number" — but
    /// for a text column <c>""</c> is a legitimate value distinct from NULL and passes through untouched.</summary>
    [Fact]
    public void EmptyText_IsNullForANonTextColumn_ButSurvivesForAText()
    {
        Assert.Null(Convert("", "INTEGER").Value);
        Assert.Null(Convert("   ", "NUMERIC(9,2)").Value);
        Assert.Null(Convert("", "DATE").Value);

        Assert.Equal("", Convert("", "VARCHAR(10)").Value);
    }

    // ── Integers ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Integer_ParsesAndTakesTheColumnsOwnWidth()
    {
        Assert.Equal((short)100, Convert("100", "SMALLINT").Value);
        Assert.Equal(42, Convert("42", "INTEGER").Value);
        Assert.Equal(-42, Convert("-42", "INTEGER").Value);
        Assert.Equal(9_223_372_036_854_775_807L, Convert("9223372036854775807", "BIGINT").Value);
    }

    [Theory]
    [InlineData("SMALLINT", "40000")]
    [InlineData("INTEGER", "3000000000")]
    public void Integer_OutOfRange_IsNotCalledMalformed(string type, string text)
    {
        // The text IS a number; what is wrong is the column. Reporting "not an integer" would send the user
        // to fix data that is perfectly fine.
        var result = Convert(text, type);
        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorKind.ValueOutOfRange, result.Kind);
    }

    [Theory]
    [InlineData("11 88x")]   // the real I0 file's one bad cell in an otherwise numeric column
    [InlineData("12.5")]
    [InlineData("12,5")]
    [InlineData("abc")]
    public void Integer_RefusesAnythingThatIsNotAWholeNumber(string text)
        => Assert.Equal(ImportErrorKind.NotAnInteger, Convert(text, "INTEGER").Kind);

    [Fact]
    public void Integer_HonoursTheDeclaredThousandsSeparator_AndOnlyThat()
    {
        var spaced = new ImportCultureOptions { ThousandsSeparator = ' ' };
        Assert.Equal(1234, ImportValueConverter.Convert("1 234", Col("INTEGER"), spaced).Value);

        // Undeclared ⇒ not accepted. A grouped number read as a bare one would change its magnitude.
        Assert.False(Convert("1 234", "INTEGER").IsSuccess);
    }

    // ── Exact numerics — the §0.1 headline ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ THE case the whole design is built around. Under a comma decimal separator, <c>"1.5"</c> is not 1.5
    /// and not 15 — it is an ERROR. A converter that "helpfully" tried the other separator would silently turn
    /// one number into a different one, which is the corruption §0.1 exists to forbid.
    /// </summary>
    [Fact]
    public void Decimal_UsesTheDeclaredSeparator_AndRefusesTheOther()
    {
        Assert.Equal(1.5m, Convert("1,5", "NUMERIC(15,2)").Value);
        Assert.Equal(ImportErrorKind.NotANumber, Convert("1.5", "NUMERIC(15,2)").Kind);

        Assert.Equal(1.5m, Convert("1.5", "NUMERIC(15,2)", En).Value);
        Assert.Equal(ImportErrorKind.NotANumber, Convert("1,5", "NUMERIC(15,2)", En).Kind);
    }

    [Fact]
    public void Float_ParsesToTheColumnsOwnWidth()
    {
        Assert.Equal(1.5d, Convert("1,5", "DOUBLE PRECISION").Value);
        Assert.Equal(1.5f, Convert("1,5", "FLOAT").Value);
    }

    /// <summary>A FLOAT is single precision, so a value beyond its range would become Infinity — silent
    /// corruption, and therefore refused.</summary>
    [Fact]
    public void Float_RefusesAValueThatWouldBecomeInfinity()
        => Assert.Equal(ImportErrorKind.ValueOutOfRange, Convert(1e300d, "FLOAT").Kind);

    // ── Text ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Text_PassesThroughVerbatim()
    {
        Assert.Equal("GN-375-GTO", Convert("GN-375-GTO", "VARCHAR(20)").Value);
        // Length is the validator's question, not the converter's — one owner each.
        Assert.True(Convert(new string('x', 500), "VARCHAR(20)").IsSuccess);
    }

    /// <summary>A spreadsheet's numeric code column mapped to VARCHAR must render the same way on every
    /// machine — this text can end up IN the database, so it never depends on regional settings.</summary>
    [Fact]
    public void Text_RendersNativeValuesUnderInvariantCulture()
    {
        Assert.Equal("11881", Convert(11881d, "VARCHAR(20)").Value);
        Assert.Equal("1.5", Convert(1.5d, "VARCHAR(20)").Value);
        Assert.Equal("2026-04-03", Convert(new DateTime(2026, 4, 3), "VARCHAR(20)").Value);
    }

    // ── Booleans ────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1", true)]
    [InlineData("T", true)]
    [InlineData("tak", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("nie", false)]
    [InlineData("FALSE", false)]
    public void Boolean_ReadsTheDeclaredTokens(string text, bool expected)
        => Assert.Equal(expected, Convert(text, "BOOLEAN").Value);

    [Fact]
    public void Boolean_RefusesAnUnknownToken()
        => Assert.Equal(ImportErrorKind.NotABoolean, Convert("maybe", "BOOLEAN").Kind);

    // ── Dates — the second silent-corruption trap ───────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <c>03.04.2026</c> is 3 April or 4 March depending ONLY on the declared field order. The converter
    /// tries exactly one order; trying both and taking whichever parses is precisely the guess §0.4 forbids.
    /// </summary>
    [Fact]
    public void Date_ReadsTheDeclaredFieldOrder_Only()
    {
        var dmy = Convert("03.04.2026", "DATE");
        Assert.Equal(new DateTime(2026, 4, 3), dmy.Value);

        var mdy = Convert("03.04.2026", "DATE", new ImportCultureOptions { DateOrder = DateFieldOrder.Mdy });
        Assert.Equal(new DateTime(2026, 3, 4), mdy.Value);
    }

    [Fact]
    public void Date_Iso_IgnoresTheSeparatorSetting()
    {
        var iso = new ImportCultureOptions { DateOrder = DateFieldOrder.Iso };
        Assert.Equal(new DateTime(2026, 4, 3), ImportValueConverter.Convert("2026-04-03", Col("DATE"), iso).Value);

        // …and an ISO date is NOT silently accepted when DMY was declared.
        Assert.Equal(ImportErrorKind.NotADateTime, Convert("2026-04-03", "DATE").Kind);
    }

    /// <summary>A DATE column has no time part, so text carrying one does not match a date under the declared
    /// settings — refused rather than having its time chopped off.</summary>
    [Fact]
    public void Date_RefusesTextThatCarriesATime()
        => Assert.Equal(ImportErrorKind.NotADateTime, Convert("03.04.2026 14:02", "DATE").Kind);

    [Fact]
    public void Timestamp_ReadsDateAndTime_AndABareDateAsMidnight()
    {
        Assert.Equal(new DateTime(2026, 4, 3, 14, 2, 33), Convert("03.04.2026 14:02:33", "TIMESTAMP").Value);
        Assert.Equal(new DateTime(2026, 4, 3, 14, 2, 33), Convert("03.04.2026T14:02:33", "TIMESTAMP").Value);
        Assert.Equal(new DateTime(2026, 4, 3), Convert("03.04.2026", "TIMESTAMP").Value);
    }

    [Fact]
    public void Time_ReadsATimeOfDay()
    {
        Assert.Equal(new TimeSpan(14, 2, 33), Convert("14:02:33", "TIME").Value);
        Assert.Equal(new TimeSpan(14, 2, 0), Convert("14:02", "TIME").Value);
        Assert.Equal(ImportErrorKind.NotADateTime, Convert("nope", "TIME").Kind);
    }

    // ── Native values (a spreadsheet cell already carries a type) ────────────────────────────────────────

    [Fact]
    public void Native_NumberIntoAnIntegerColumn()
        => Assert.Equal(11881, Convert(11881d, "INTEGER").Value);

    /// <summary>11.5 into an INTEGER column: the value IS a number, so "not an integer" would misdescribe it —
    /// what is actually wrong is that writing it would drop the fraction.</summary>
    [Fact]
    public void Native_FractionIntoAnIntegerColumn_IsReportedAsLoss()
        => Assert.Equal(ImportErrorKind.PrecisionWouldBeLost, Convert(11.5d, "INTEGER").Kind);

    [Fact]
    public void Native_DateWithATimeIntoADateColumn_IsReportedAsLoss()
    {
        Assert.Equal(new DateTime(2026, 4, 3), Convert(new DateTime(2026, 4, 3), "DATE").Value);
        Assert.Equal(
            ImportErrorKind.PrecisionWouldBeLost,
            Convert(new DateTime(2026, 4, 3, 14, 2, 0), "DATE").Kind);
    }

    [Fact]
    public void Native_BooleanAndBlob()
    {
        Assert.Equal(true, Convert(true, "BOOLEAN").Value);

        var bytes = new byte[] { 1, 2, 3 };
        Assert.Same(bytes, Convert(bytes, "BLOB SUB_TYPE BINARY").Value);

        // Text into a binary BLOB would need an invented encoding or hex convention. Refused.
        Assert.Equal(ImportErrorKind.UnsupportedTargetType, Convert("abc", "BLOB SUB_TYPE BINARY").Kind);
    }

    // ── Unsupported target types ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnsupportedColumnType_IsRefusedLoudly()
    {
        var result = Convert("1", "INT128");
        Assert.False(result.IsSuccess);
        Assert.Equal(ImportErrorKind.UnsupportedTargetType, result.Kind);
    }

    // ── The raw value is kept for the report ────────────────────────────────────────────────────────────

    /// <summary>§0.6: the report shows what the user ACTUALLY has, not a post-conversion approximation.</summary>
    [Fact]
    public void AFailureCarriesTheSourceTextVerbatim()
        => Assert.Equal("11 88x", Convert("11 88x", "INTEGER").RawText);
}
