using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class FirebirdTableDetailReaderTests
{
    [Theory]
    [InlineData(7, null, null, null, null, "SMALLINT")]
    [InlineData(8, null, null, null, null, "INTEGER")]
    [InlineData(16, null, null, null, null, "BIGINT")]
    [InlineData(23, null, null, null, null, "BOOLEAN")]
    [InlineData(27, null, null, null, null, "DOUBLE PRECISION")]
    [InlineData(35, null, null, null, null, "TIMESTAMP")]
    [InlineData(45, null, null, null, null, "BLOB_ID")]
    [InlineData(261, null, null, null, null, "BLOB")]
    public void FormatFieldType_BasicTypes(int type, int? length, int? scale, int? precision, int? subType, string expected)
    {
        Assert.Equal(expected, FirebirdTableDetailReader.FormatFieldType(type, length, scale, precision, subType));
    }

    [Fact]
    public void FormatFieldType_Char_IncludesLength()
    {
        Assert.Equal("CHAR(10)", FirebirdTableDetailReader.FormatFieldType(14, 10, null, null, null));
    }

    [Fact]
    public void FormatFieldType_Varchar_IncludesLength()
    {
        Assert.Equal("VARCHAR(255)", FirebirdTableDetailReader.FormatFieldType(37, 255, null, null, null));
    }

    [Fact]
    public void FormatFieldType_Bigint_WithNegativeScale_IsNumeric()
    {
        Assert.Equal("NUMERIC(15,2)", FirebirdTableDetailReader.FormatFieldType(16, null, -2, 15, null));
    }

    [Fact]
    public void FormatFieldType_Bigint_WithNegativeScale_SubType2_IsDecimal()
    {
        Assert.Equal("DECIMAL(15,2)", FirebirdTableDetailReader.FormatFieldType(16, null, -2, 15, 2));
    }

    [Fact]
    public void FormatFieldType_UnknownType_FallsBackToTypeNumber()
    {
        Assert.Equal("TYPE_999", FirebirdTableDetailReader.FormatFieldType(999, null, null, null, null));
    }

    [Fact]
    public void FormatFieldType_Integer_ZeroScale_StaysInteger()
    {
        Assert.Equal("INTEGER", FirebirdTableDetailReader.FormatFieldType(8, null, 0, null, null));
    }

    [Theory]
    [InlineData("PRIMARY KEY", true)]
    [InlineData("primary key", true)]
    [InlineData("UNIQUE", false)]
    [InlineData("FOREIGN KEY", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsPrimaryConstraint_DetectsPrimaryKeyCaseInsensitively(string? constraintType, bool expected)
    {
        Assert.Equal(expected, FirebirdTableDetailReader.IsPrimaryConstraint(constraintType));
    }

    [Theory]
    [InlineData("DEFAULT 0", "0")]
    [InlineData("DEFAULT 'foo'", "'foo'")]
    [InlineData("default 42", "42")]
    [InlineData("0", "0")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void StripDefaultPrefix_RemovesLeadingDefaultKeyword(string? input, string? expected)
    {
        Assert.Equal(expected, FirebirdTableDetailReader.StripDefaultPrefix(input));
    }

    [Fact]
    public void FieldsSql_QueriesRdbRelationFields()
    {
        Assert.Contains("RDB$RELATION_FIELDS", FirebirdTableDetailReader.FieldsSql);
        Assert.Contains("RDB$FIELDS", FirebirdTableDetailReader.FieldsSql);
        Assert.Contains("ORDER BY rf.RDB$FIELD_POSITION", FirebirdTableDetailReader.FieldsSql);
        Assert.Contains("@tableName", FirebirdTableDetailReader.FieldsSql);
    }

    [Fact]
    public void IndexesSql_QueriesRdbIndicesWithListAggregate()
    {
        Assert.Contains("RDB$INDICES", FirebirdTableDetailReader.IndexesSql);
        Assert.Contains("RDB$INDEX_SEGMENTS", FirebirdTableDetailReader.IndexesSql);
        Assert.Contains("RDB$RELATION_CONSTRAINTS", FirebirdTableDetailReader.IndexesSql);
        Assert.Contains("LIST(", FirebirdTableDetailReader.IndexesSql);
        Assert.Contains("@tableName", FirebirdTableDetailReader.IndexesSql);
    }

    [Fact]
    public void ConstraintsSql_QueriesAllConstraintCatalogTables()
    {
        var sql = FirebirdTableDetailReader.ConstraintsSql;
        Assert.Contains("RDB$RELATION_CONSTRAINTS", sql);
        Assert.Contains("RDB$REF_CONSTRAINTS", sql);
        Assert.Contains("RDB$CHECK_CONSTRAINTS", sql);
        Assert.Contains("RDB$INDEX_SEGMENTS", sql);
        Assert.Contains("@tableName", sql);
        Assert.Contains("LEFT JOIN", sql);
    }

    [Fact]
    public void BuildConstraintInfo_PrimaryKey_LeavesRefFieldsEmpty()
    {
        var c = FirebirdTableDetailReader.BuildConstraintInfo(
            name: "PK_USERS  ",
            rawKind: "PRIMARY KEY",
            fields: "ID",
            refTable: null,
            refFields: null,
            checkSource: null);
        Assert.Equal("PK_USERS", c.Name);
        Assert.Equal("PRIMARY KEY", c.Kind);
        Assert.Equal("ID", c.Fields);
        Assert.Equal(string.Empty, c.RefTable);
        Assert.Equal(string.Empty, c.RefFields);
        Assert.Equal(string.Empty, c.CheckSource);
    }

    [Fact]
    public void BuildConstraintInfo_ForeignKey_FillsRefTableAndRefFields()
    {
        var c = FirebirdTableDetailReader.BuildConstraintInfo(
            name: "FK_ORDERS_CUSTOMER",
            rawKind: "FOREIGN KEY",
            fields: "CUSTOMER_ID",
            refTable: "CUSTOMERS",
            refFields: "ID",
            checkSource: null);
        Assert.Equal("FOREIGN KEY", c.Kind);
        Assert.Equal("CUSTOMERS", c.RefTable);
        Assert.Equal("ID", c.RefFields);
        Assert.Equal(string.Empty, c.CheckSource);
    }

    [Fact]
    public void BuildConstraintInfo_Check_FillsCheckSource()
    {
        var c = FirebirdTableDetailReader.BuildConstraintInfo(
            name: "CHK_AGE",
            rawKind: "CHECK",
            fields: null,
            refTable: null,
            refFields: null,
            checkSource: "CHECK (AGE >= 0)");
        Assert.Equal("CHECK", c.Kind);
        Assert.Equal("CHECK (AGE >= 0)", c.CheckSource);
        Assert.Equal(string.Empty, c.Fields);
    }

    [Fact]
    public void BuildConstraintInfo_Unique_MapsKindAndFields()
    {
        var c = FirebirdTableDetailReader.BuildConstraintInfo(
            name: "UQ_USERS_EMAIL",
            rawKind: "UNIQUE",
            fields: "EMAIL",
            refTable: null,
            refFields: null,
            checkSource: null);
        Assert.Equal("UNIQUE", c.Kind);
        Assert.Equal("EMAIL", c.Fields);
        Assert.Equal(string.Empty, c.RefTable);
    }

    [Fact]
    public void BuildConstraintInfo_NullsBecomeEmptyStrings()
    {
        var c = FirebirdTableDetailReader.BuildConstraintInfo(
            name: null,
            rawKind: null,
            fields: null,
            refTable: null,
            refFields: null,
            checkSource: null);
        Assert.Equal(string.Empty, c.Name);
        Assert.Equal(string.Empty, c.Kind);
        Assert.Equal(string.Empty, c.Fields);
        Assert.Equal(string.Empty, c.RefTable);
        Assert.Equal(string.Empty, c.RefFields);
        Assert.Equal(string.Empty, c.CheckSource);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Customer master data", "Customer master data")]
    [InlineData("   trimmed   ", "trimmed")]
    public void NormalizeDescription_TrimsAndDefaultsToEmpty(string? input, string expected)
    {
        Assert.Equal(expected, FirebirdTableDetailReader.NormalizeDescription(input));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("RDB$12", null)]
    [InlineData("RDB$1234567", null)]
    [InlineData("MY_DOMAIN", "MY_DOMAIN")]
    [InlineData("  USER_NAME_T  ", "USER_NAME_T")]
    public void NormalizeDomain_FiltersAnonymousBackingDomains(string? input, string? expected)
    {
        Assert.Equal(expected, FirebirdTableDetailReader.NormalizeDomain(input));
    }

    [Fact]
    public void FieldsSql_PullsPkFkDomainAndCharsetColumns()
    {
        var sql = FirebirdTableDetailReader.FieldsSql;
        Assert.Contains("PK_FLAG", sql);
        Assert.Contains("FK_FLAG", sql);
        Assert.Contains("'PRIMARY KEY'", sql);
        Assert.Contains("'FOREIGN KEY'", sql);
        Assert.Contains("rf.RDB$FIELD_SOURCE", sql);
        Assert.Contains("RDB$CHARACTER_SETS", sql);
    }

    [Fact]
    public void FieldInfo_InitializesNewProperties()
    {
        var f = new FieldInfo
        {
            Position = 0,
            Name = "ID",
            Type = "INTEGER",
            IsPrimaryKey = true,
            IsForeignKey = false,
            Domain = "MY_DOMAIN",
            ComputedSource = "(A + B)",
            Charset = "WIN1250",
        };
        Assert.True(f.IsPrimaryKey);
        Assert.False(f.IsForeignKey);
        Assert.Equal("MY_DOMAIN", f.Domain);
        Assert.Equal("(A + B)", f.ComputedSource);
        Assert.Equal("WIN1250", f.Charset);
    }

    [Fact]
    public void FieldInfo_NewPropertiesDefaultToFalseOrNull()
    {
        var f = new FieldInfo { Name = "X", Type = "INTEGER" };
        Assert.False(f.IsPrimaryKey);
        Assert.False(f.IsForeignKey);
        Assert.Null(f.Domain);
        Assert.Null(f.Charset);
        Assert.Null(f.ComputedSource);
    }

    [Theory]
    [InlineData("VARCHAR(255)", "VARCHAR")]
    [InlineData("NUMERIC(15,2)", "NUMERIC")]
    [InlineData("DECIMAL(18,4)", "DECIMAL")]
    [InlineData("CHAR(10)", "CHAR")]
    [InlineData("CSTRING(64)", "CSTRING")]
    [InlineData("INTEGER", "INTEGER")]
    [InlineData("SMALLINT", "SMALLINT")]
    [InlineData("BIGINT", "BIGINT")]
    [InlineData("DOUBLE PRECISION", "DOUBLE PRECISION")]
    [InlineData("TIMESTAMP WITH TIME ZONE", "TIMESTAMP WITH TIME ZONE")]
    [InlineData("", "")]
    public void BaseTypeName_StripsSizeSuffix(string input, string expected)
    {
        var f = new FieldInfo { Type = input };
        Assert.Equal(expected, f.BaseTypeName);
    }

    [Fact]
    public void IsUnique_DefaultsFalse()
    {
        var f = new FieldInfo { Name = "X", Type = "INTEGER" };
        Assert.False(f.IsUnique);
    }

    [Fact]
    public void ForeignKeyTable_DefaultsNull()
    {
        var f = new FieldInfo { Name = "X", Type = "INTEGER" };
        Assert.Null(f.ForeignKeyTable);
    }

    [Fact]
    public void FieldsSql_PullsUniqueAndForeignKeyTable()
    {
        var sql = FirebirdTableDetailReader.FieldsSql;
        Assert.Contains("UNQ_FLAG", sql);
        Assert.Contains("'UNIQUE'", sql);
        Assert.Contains("FK_TABLE", sql);
        Assert.Contains("RDB$REF_CONSTRAINTS", sql);
    }
}
