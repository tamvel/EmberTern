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
}
