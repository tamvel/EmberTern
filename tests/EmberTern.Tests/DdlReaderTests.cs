using System.Text;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class DdlReaderTests
{
    [Theory]
    [InlineData("CUSTOMERS", "CUSTOMERS")]
    [InlineData("MyTable", "\"MyTable\"")]
    [InlineData("INVOICE_LINE_1", "INVOICE_LINE_1")]
    [InlineData("WITH SPACE", "\"WITH SPACE\"")]
    [InlineData("HAS\"QUOTE", "\"HAS\"\"QUOTE\"")]
    [InlineData("", "\"\"")]
    [InlineData("RDB$RELATIONS", "RDB$RELATIONS")]
    [InlineData("1STARTS_DIGIT", "\"1STARTS_DIGIT\"")]
    public void Quote_QuotesOnlyWhenNeeded(string input, string expected)
    {
        Assert.Equal(expected, FirebirdDdlReader.Quote(input));
    }

    [Fact]
    public void FormatType_SmallintIntegerBigint_PlainTypeNames()
    {
        Assert.Equal("SMALLINT", FirebirdDdlReader.FormatType(7, 0, 2, null, 0, null));
        Assert.Equal("INTEGER", FirebirdDdlReader.FormatType(8, 0, 4, null, 0, null));
        Assert.Equal("BIGINT", FirebirdDdlReader.FormatType(16, 0, 8, null, 0, null));
    }

    [Fact]
    public void FormatType_NumericAndDecimal_RoundTripPrecisionAndScale()
    {
        // NUMERIC(10,2) is typically stored as BIGINT (type=16) with sub_type=1, precision=10, scale=-2.
        Assert.Equal("NUMERIC(10,2)", FirebirdDdlReader.FormatType(16, 1, 8, 10, -2, null));
        Assert.Equal("DECIMAL(18,4)", FirebirdDdlReader.FormatType(16, 2, 8, 18, -4, null));
        // No scale → just precision
        Assert.Equal("NUMERIC(5)", FirebirdDdlReader.FormatType(7, 1, 2, 5, 0, null));
    }

    [Fact]
    public void FormatType_CharVarchar_UseCharacterLength()
    {
        Assert.Equal("VARCHAR(50)", FirebirdDdlReader.FormatType(37, 0, 200, null, 0, 50));
        Assert.Equal("CHAR(10)", FirebirdDdlReader.FormatType(14, 0, 40, null, 0, 10));
        // Fallback to field_length when character_length is null
        Assert.Equal("VARCHAR(30)", FirebirdDdlReader.FormatType(37, 0, 30, null, 0, null));
    }

    [Fact]
    public void FormatType_DateTimeBoolean()
    {
        Assert.Equal("DATE", FirebirdDdlReader.FormatType(12, 0, null, null, null, null));
        Assert.Equal("TIME", FirebirdDdlReader.FormatType(13, 0, null, null, null, null));
        Assert.Equal("TIMESTAMP", FirebirdDdlReader.FormatType(35, 0, null, null, null, null));
        Assert.Equal("BOOLEAN", FirebirdDdlReader.FormatType(23, 0, 1, null, null, null));
        Assert.Equal("DOUBLE PRECISION", FirebirdDdlReader.FormatType(27, 0, 8, null, null, null));
    }

    [Fact]
    public void FormatType_BlobBranchesOnSubType()
    {
        Assert.Equal("BLOB SUB_TYPE TEXT", FirebirdDdlReader.FormatType(261, 1, 8, null, null, null));
        Assert.Equal("BLOB SUB_TYPE BINARY", FirebirdDdlReader.FormatType(261, 0, 8, null, null, null));
        Assert.Equal("BLOB SUB_TYPE 5", FirebirdDdlReader.FormatType(261, 5, 8, null, null, null));
    }

    [Fact]
    public void FormatType_UnknownType_FallsBackToComment()
    {
        var s = FirebirdDdlReader.FormatType(999, 0, null, null, null, null);
        Assert.Contains("field_type=999", s);
    }

    [Theory]
    [InlineData(1, "BEFORE INSERT")]
    [InlineData(2, "AFTER INSERT")]
    [InlineData(3, "BEFORE UPDATE")]
    [InlineData(4, "AFTER UPDATE")]
    [InlineData(5, "BEFORE DELETE")]
    [InlineData(6, "AFTER DELETE")]
    public void DescribeTriggerType_SingleEventEncodings(short t, string expected)
    {
        Assert.Equal(expected, FirebirdDdlReader.DescribeTriggerType(t));
    }

    [Theory]
    [InlineData(17, "BEFORE INSERT OR UPDATE")]
    [InlineData(25, "BEFORE INSERT OR DELETE")]
    [InlineData(27, "BEFORE UPDATE OR DELETE")]
    [InlineData(113, "BEFORE INSERT OR UPDATE OR DELETE")]
    public void DescribeTriggerType_MultiEventEncodings(short t, string expected)
    {
        Assert.Equal(expected, FirebirdDdlReader.DescribeTriggerType(t));
    }

    [Fact]
    public void DescribeTriggerType_NullReturnsNull()
    {
        Assert.Null(FirebirdDdlReader.DescribeTriggerType(null));
    }

    [Fact]
    public void DescribeTriggerType_DbLevelTriggers_ReturnNullForFallback()
    {
        // DB-level triggers use codes >= 8192. We don't decode them, just fall back.
        Assert.Null(FirebirdDdlReader.DescribeTriggerType(8192));
        Assert.Null(FirebirdDdlReader.DescribeTriggerType(8193));
    }

    [Theory]
    [InlineData("WI-V3.0.7.33374 Firebird 3.0", 3)]
    [InlineData("WI-V5.0.0.1306 Firebird 5.0", 5)]
    [InlineData("WI-V2.5.9.27139 Firebird 2.5", 2)]
    [InlineData("WI-V4.0.4.3010 Firebird 4.0", 4)]
    [InlineData("Firebird 5.0", 5)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("garbage", 0)]
    public void ParseServerMajor_ExtractsMajorVersion(string? version, int expected)
    {
        Assert.Equal(expected, FirebirdDdlReader.ParseServerMajor(version));
    }

    [Fact]
    public void SqlForTableColumns_ReferencesRequiredTables()
    {
        var sql = FirebirdDdlReader.SqlForTableColumns;
        Assert.Contains("RDB$RELATION_FIELDS", sql);
        Assert.Contains("RDB$FIELDS", sql);
        Assert.Contains("RDB$FIELD_POSITION", sql);
    }

    [Fact]
    public void SqlForTableColumns_ReadsComputedSourceFromDomainTable()
    {
        // RDB$COMPUTED_SOURCE lives on RDB$FIELDS (alias f.), NOT RDB$RELATION_FIELDS (rf.).
        // Pinning this guards against the regression that broke table DDL on every FB version.
        var sql = FirebirdDdlReader.SqlForTableColumns;
        Assert.Contains("f.RDB$COMPUTED_SOURCE", sql);
        Assert.DoesNotContain("rf.RDB$COMPUTED_SOURCE", sql);
    }

    [Fact]
    public void DecodeBytes_ValidUtf8_DecodedAsUtf8()
    {
        // "każda" — UTF-8 byte sequence
        var utf8 = new byte[] { 0x6B, 0x61, 0xC5, 0xBC, 0x64, 0x61 };
        var result = FirebirdDdlReader.DecodeBytes(utf8, utf8.Length, Encoding.GetEncoding("windows-1250"));
        Assert.Equal("każda", result);
    }

    [Fact]
    public void DecodeBytes_Win1250Bytes_FallsBackToFallbackEncoding()
    {
        // "KAŻDEJ" — WIN1250 bytes (0xAF is a standalone high byte → invalid UTF-8)
        var win1250 = new byte[] { 0x4B, 0x41, 0xAF, 0x44, 0x45, 0x4A };
        var result = FirebirdDdlReader.DecodeBytes(win1250, win1250.Length, Encoding.GetEncoding("windows-1250"));
        Assert.Equal("KAŻDEJ", result);
    }

    [Fact]
    public void DecodeBytes_AsciiOnly_BothEncodingsAgree()
    {
        var ascii = Encoding.ASCII.GetBytes("BEGIN END");
        var result = FirebirdDdlReader.DecodeBytes(ascii, ascii.Length, Encoding.GetEncoding("windows-1250"));
        Assert.Equal("BEGIN END", result);
    }

    [Fact]
    public void BuildRoleDdl_EmitsCreateRoleStatement()
    {
        Assert.Equal("CREATE ROLE ADMIN;\n", FirebirdDdlReader.BuildRoleDdl("ADMIN"));
        // Quoting kicks in for lowercase identifiers.
        Assert.Equal("CREATE ROLE \"mixedCase\";\n", FirebirdDdlReader.BuildRoleDdl("mixedCase"));
    }

    [Theory]
    [InlineData("DOMAIN", "MY_DOMAIN")]
    [InlineData("PACKAGE", "PKG_UTIL")]
    [InlineData("USER", "ALICE")]
    [InlineData("INDEX", "IDX_CUSTOMERS_NAME")]
    public void BuildPlaceholderDdl_EmitsCommentBlock(string keyword, string name)
    {
        var ddl = FirebirdDdlReader.BuildPlaceholderDdl(keyword, name);
        Assert.StartsWith("/*", ddl);
        Assert.Contains(keyword, ddl);
        Assert.Contains(name, ddl);
        Assert.EndsWith("*/\n", ddl);
    }

    [Theory]
    [InlineData("UTF8", "utf-8")]
    [InlineData("WIN1250", "windows-1250")]
    [InlineData("WIN1252", "windows-1252")]
    [InlineData("ISO8859_2", "iso-8859-2")]
    [InlineData("UNICODE_FSS", "utf-8")]
    [InlineData("NONE", "utf-8")]      // safe default
    [InlineData("", "utf-8")]
    [InlineData(null, "utf-8")]
    public void CharsetCatalog_ResolveMapsFirebirdToDotNetEncoding(string? fbCharset, string expectedWebName)
    {
        var encoding = CharsetCatalog.Resolve(fbCharset);
        Assert.Equal(expectedWebName, encoding.WebName);
    }
}
