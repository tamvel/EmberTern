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
        // DB-level triggers use codes >= 8192; relation-trigger decoding falls back
        // (they're described by DescribeDatabaseTriggerEvent instead).
        Assert.Null(FirebirdDdlReader.DescribeTriggerType(8192));
        Assert.Null(FirebirdDdlReader.DescribeTriggerType(8193));
    }

    [Theory]
    [InlineData(8192L, "ON CONNECT")]
    [InlineData(8193L, "ON DISCONNECT")]
    [InlineData(8194L, "ON TRANSACTION START")]
    [InlineData(8195L, "ON TRANSACTION COMMIT")]
    [InlineData(8196L, "ON TRANSACTION ROLLBACK")]
    public void DescribeDatabaseTriggerEvent_MapsDbLevelCodes(long t, string expected)
    {
        Assert.Equal(expected, FirebirdDdlReader.DescribeDatabaseTriggerEvent(t));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1L)]    // relation trigger — not a DB-level event
    [InlineData(3L)]
    [InlineData(8197L)] // out of the DB-level range
    public void DescribeDatabaseTriggerEvent_NonDbLevel_ReturnsNull(long? t)
    {
        Assert.Null(FirebirdDdlReader.DescribeDatabaseTriggerEvent(t));
    }

    [Theory]
    [InlineData(5, "", " AND RDB$PACKAGE_NAME IS NULL ")]
    [InlineData(5, "pp.", " AND pp.RDB$PACKAGE_NAME IS NULL ")]
    [InlineData(4, "fa.", " AND fa.RDB$PACKAGE_NAME IS NULL ")]
    [InlineData(3, "", " AND RDB$PACKAGE_NAME IS NULL ")]
    [InlineData(2, "pp.", "")]   // FB2.5 has no RDB$PACKAGE_NAME column — emit nothing.
    [InlineData(0, "", "")]
    public void StandalonePackageFilter_GatesOnServerMajor(int serverMajor, string alias, string expected)
    {
        Assert.Equal(expected, FirebirdDdlReader.StandalonePackageFilter(serverMajor, alias));
    }

    [Fact]
    public void InsertBeforeOrderBy_PlacesClauseBeforeOrderBy()
    {
        var sql = "SELECT X FROM T WHERE A = @name AND B = @pt ORDER BY N";
        var result = FirebirdDdlReader.InsertBeforeOrderBy(sql, " AND T.RDB$PACKAGE_NAME IS NULL ");
        // The filter must land inside the WHERE, not after ORDER BY (which would be a syntax error).
        Assert.Contains("@pt AND T.RDB$PACKAGE_NAME IS NULL  ORDER BY N", result);
        Assert.True(result.IndexOf("RDB$PACKAGE_NAME") < result.IndexOf("ORDER BY"));
    }

    [Fact]
    public void InsertBeforeOrderBy_NoOrderBy_Appends()
    {
        var sql = "SELECT X FROM T WHERE A = @name";
        var result = FirebirdDdlReader.InsertBeforeOrderBy(sql, " AND RDB$PACKAGE_NAME IS NULL ");
        Assert.Equal("SELECT X FROM T WHERE A = @name AND RDB$PACKAGE_NAME IS NULL ", result);
    }

    [Fact]
    public void InsertBeforeOrderBy_EmptyClause_ReturnsUnchanged()
    {
        var sql = "SELECT X FROM T WHERE A = @name ORDER BY N";
        Assert.Equal(sql, FirebirdDdlReader.InsertBeforeOrderBy(sql, string.Empty));
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

    // ══ Domain-typed routine signatures (S-1b, 2026-08-05) ═══════════════════════════════════════
    //
    // ⭐⭐ These pin a rule #11 (never lose information) decision. A parameter declared `P_CODE D_CODE`
    // stores 'D_CODE' in RDB$FIELD_SOURCE while a plain one stores an anonymous 'RDB$n' (measured on
    // live FB5); the reconstruction used to resolve BOTH to the base type, so the domain was discarded
    // on READ — and the object editors reassemble the whole CREATE OR ALTER from what the read returned,
    // which turned "open a procedure, edit the body, Compile" into a silent rewrite of every
    // domain-typed parameter as its base type. That is gotcha #175's shape.

    [Theory]
    [InlineData("D_CODE", true)]
    [InlineData("d_code", true)]
    [InlineData("  D_CODE  ", true)]
    [InlineData("RDB$134", false)]
    [InlineData("rdb$134", false)]   // Firebird folds, so the predicate must be case-insensitive
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsUserDomain_TellsANamedDomainFromAnAnonymousBackingOne(string? fieldSource, bool expected)
    {
        Assert.Equal(expected, FirebirdDdlReader.IsUserDomain(fieldSource));
    }

    [Fact]
    public void TypeTextForField_PrefersTheDomainOverTheResolvedBaseType()
    {
        Assert.Equal("D_CODE", FirebirdDdlReader.TypeTextForField("D_CODE", "CHAR(8)"));
        Assert.Equal("CHAR(8)", FirebirdDdlReader.TypeTextForField("RDB$134", "CHAR(8)"));
        Assert.Equal("CHAR(8)", FirebirdDdlReader.TypeTextForField(null, "CHAR(8)"));
    }

    [Fact]
    public void TypeTextForField_QuotesADomainThatNeedsIt_AndLeavesShoutyCaseBare()
    {
        // Quote's lighter convention: a SHOUTY_CASE name stays bare (matching the catalog and isql -x),
        // a case-sensitive one is preserved verbatim and quoted — never uppercased, so its identity is
        // never changed (§0).
        Assert.Equal("D_CODE", FirebirdDdlReader.TypeTextForField("D_CODE", "CHAR(8)"));
        Assert.Equal("\"mixedCase\"", FirebirdDdlReader.TypeTextForField("mixedCase", "CHAR(8)"));
    }

    // ⭐⭐ THE NULLABILITY SOURCE FOLLOWS THE TYPE SOURCE, and the table below is MEASURED on FB5
    // (2026-08-05), not chosen:
    //
    //   A D_NAME            (domain is NOT NULL)  → own flag NULL, domain flag 1
    //   B D_CODE            (nullable domain)     → own flag NULL, domain flag NULL
    //   C D_CODE NOT NULL   (explicit)            → own flag 1,    domain flag NULL
    //   D INTEGER NOT NULL  (inline type)         → own flag 1,    domain flag NULL
    //
    // So when the emitted type is the DOMAIN NAME the domain already carries its own NOT NULL and only
    // the parameter's own flag may add one — otherwise `A D_NAME` would be reconstructed as
    // `A D_NAME NOT NULL`, a clause the original declaration never had. When the emitted type is the
    // BASE type the domain's flag MUST be materialised, or the reconstruction loses the constraint.
    [Theory]
    // domain-typed: only the parameter's OWN flag counts
    [InlineData("D_NAME", null, (short)1, false)]   // domain is NOT NULL → it says so itself
    [InlineData("D_CODE", (short)1, null, true)]    // parameter declared NOT NULL explicitly
    [InlineData("D_CODE", null, null, false)]
    // inline type: the domain flag is the type's own, so it must be emitted
    [InlineData("RDB$134", (short)1, null, true)]
    [InlineData("RDB$134", null, (short)1, true)]
    [InlineData("RDB$134", null, null, false)]
    public void EmitsNotNull_FollowsTheTypeSource(string fieldSource, short? own, short? domain, bool expected)
    {
        Assert.Equal(expected, FirebirdDdlReader.EmitsNotNull(fieldSource, own, domain));
    }

    [Fact]
    public void SqlForProcedureParams_CarriesTheFieldSourceAndBothNullFlagsSeparately()
    {
        var sql = FirebirdDdlReader.SqlForProcedureParams;
        var selectList = sql[..sql.IndexOf("FROM RDB$PROCEDURE_PARAMETERS", StringComparison.Ordinal)];
        // ⚠ Asserted on the SELECT LIST, not the whole query: the JOIN predicate already names
        // pp.RDB$FIELD_SOURCE (that join is what reaches the base type, i.e. what loses the domain), so
        // a Contains over the whole string passes with the defect present. It did, in the first draft.
        Assert.Contains("pp.RDB$FIELD_SOURCE", selectList);
        // Both flags, separately — EmitsNotNull cannot make its decision from a COALESCE.
        Assert.Contains("pp.RDB$NULL_FLAG", selectList);
        Assert.Contains("f.RDB$NULL_FLAG", selectList);
        Assert.DoesNotContain("COALESCE(pp.RDB$NULL_FLAG", selectList);
    }

    [Fact]
    public void SqlForFunctionArgs_CarriesTheFieldSourceAndBothNullFlagsSeparately()
    {
        var sql = FirebirdDdlReader.SqlForFunctionArgs;
        var selectList = sql[..sql.IndexOf("FROM RDB$FUNCTION_ARGUMENTS", StringComparison.Ordinal)];
        Assert.Contains("fa.RDB$FIELD_SOURCE", selectList);
        Assert.Contains("fa.RDB$NULL_FLAG", selectList);
        Assert.Contains("f.RDB$NULL_FLAG", selectList);
        Assert.DoesNotContain("COALESCE(fa.RDB$NULL_FLAG", selectList);
    }

    [Fact]
    public void SqlForProcedureParams_KeepsThePackageFilterInsertable()
    {
        // The standalone-vs-packaged filter is spliced in before ORDER BY (a packaged namesake would
        // otherwise double the parameter list → -901 "duplicate specification"). Extracting the query
        // into a const must not have broken that splice point.
        var filtered = FirebirdDdlReader.InsertBeforeOrderBy(
            FirebirdDdlReader.SqlForProcedureParams, FirebirdDdlReader.StandalonePackageFilter(5, "pp."));
        Assert.Contains("RDB$PACKAGE_NAME IS NULL", filtered);
        Assert.True(
            filtered.IndexOf("RDB$PACKAGE_NAME IS NULL", StringComparison.Ordinal)
            < filtered.IndexOf("ORDER BY", StringComparison.Ordinal),
            "the package filter must be spliced BEFORE ORDER BY or the WHERE clause is left invalid");
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
