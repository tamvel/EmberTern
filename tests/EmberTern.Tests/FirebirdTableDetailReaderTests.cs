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
    // Procedure/function parameter defaults are stored with the "= value" form; strip the
    // "=" so Source regeneration doesn't produce the un-compilable "SMALLINT = = 1".
    [InlineData("= 1", "1")]
    [InlineData("=1", "1")]
    [InlineData("= 'x'", "'x'")]
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
            checkSource: null,
            indexName: "PK_USERS_IDX");
        Assert.Equal("PK_USERS", c.Name);
        Assert.Equal("PRIMARY KEY", c.ConstraintType);
        Assert.Equal("ID", c.Fields);
        Assert.Equal(string.Empty, c.RefTable);
        Assert.Equal(string.Empty, c.RefFields);
        Assert.Equal(string.Empty, c.CheckClause);
        Assert.Equal("PK_USERS_IDX", c.IndexName);
        Assert.False(c.IsDescending);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void BuildConstraintInfo_MapsIndexDirectionToIsDescending(int? indexDirection, bool expected)
    {
        var c = FirebirdTableDetailReader.BuildConstraintInfo(
            name: "PK", rawKind: "PRIMARY KEY", fields: "ID",
            refTable: null, refFields: null, checkSource: null,
            indexName: "PK_IDX", indexDirection: indexDirection);
        Assert.Equal(expected, c.IsDescending);
    }

    [Fact]
    public void BuildConstraintInfo_ForeignKey_FillsRulesAndDirection()
    {
        var c = FirebirdTableDetailReader.BuildConstraintInfo(
            name: "FK", rawKind: "FOREIGN KEY", fields: "X",
            refTable: "T", refFields: "ID", checkSource: null,
            indexName: "FK_IDX",
            updateRule: "CASCADE", deleteRule: "SET NULL",
            indexDirection: 1);
        Assert.Equal("CASCADE", c.UpdateRule);
        Assert.Equal("SET NULL", c.DeleteRule);
        Assert.True(c.IsDescending);
    }

    [Fact]
    public void ConstraintsSql_JoinsRdbIndicesForIndexType()
    {
        var sql = FirebirdTableDetailReader.ConstraintsSql;
        Assert.Contains("RDB$INDICES", sql);
        Assert.Contains("idx.RDB$INDEX_TYPE", sql);
        Assert.Contains("idx.RDB$INDEX_NAME = rc.RDB$INDEX_NAME", sql);
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
            checkSource: null,
            indexName: "FK_ORDERS_CUSTOMER_IDX",
            updateRule: "CASCADE",
            deleteRule: "SET NULL");
        Assert.Equal("FOREIGN KEY", c.ConstraintType);
        Assert.Equal("CUSTOMERS", c.RefTable);
        Assert.Equal("ID", c.RefFields);
        Assert.Equal(string.Empty, c.CheckClause);
        Assert.Equal("CASCADE", c.UpdateRule);
        Assert.Equal("SET NULL", c.DeleteRule);
        Assert.Equal("ON UPDATE CASCADE, ON DELETE SET NULL", c.ForeignKeyRule);
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
        Assert.Equal("CHECK", c.ConstraintType);
        Assert.Equal("CHECK (AGE >= 0)", c.CheckClause);
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
            checkSource: null,
            indexName: "UQ_USERS_EMAIL_IDX");
        Assert.Equal("UNIQUE", c.ConstraintType);
        Assert.Equal("EMAIL", c.Fields);
        Assert.Equal(string.Empty, c.RefTable);
        Assert.Equal("UQ_USERS_EMAIL_IDX", c.IndexName);
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
        Assert.Equal(string.Empty, c.ConstraintType);
        Assert.Equal(string.Empty, c.Fields);
        Assert.Equal(string.Empty, c.RefTable);
        Assert.Equal(string.Empty, c.RefFields);
        Assert.Equal(string.Empty, c.CheckClause);
        Assert.Equal(string.Empty, c.IndexName);
        Assert.Equal(string.Empty, c.UpdateRule);
        Assert.Equal(string.Empty, c.DeleteRule);
        Assert.Equal(string.Empty, c.ForeignKeyRule);
        Assert.False(c.IsDescending);
    }

    [Fact]
    public void ForeignKeyRule_SuppressesDefaultRestrict()
    {
        var c = FirebirdTableDetailReader.BuildConstraintInfo(
            name: "FK", rawKind: "FOREIGN KEY", fields: "X",
            refTable: "T", refFields: "ID", checkSource: null,
            updateRule: "RESTRICT", deleteRule: "CASCADE");
        Assert.Equal("ON DELETE CASCADE", c.ForeignKeyRule);
    }

    [Fact]
    public void ForeignKeyRule_BothRestrictRendersEmpty()
    {
        var c = FirebirdTableDetailReader.BuildConstraintInfo(
            name: "FK", rawKind: "FOREIGN KEY", fields: "X",
            refTable: "T", refFields: "ID", checkSource: null,
            updateRule: "RESTRICT", deleteRule: "RESTRICT");
        Assert.Equal(string.Empty, c.ForeignKeyRule);
    }

    [Fact]
    public void IndexesSql_IncludesNewColumns()
    {
        var sql = FirebirdTableDetailReader.IndexesSql;
        Assert.Contains("RDB$INDEX_INACTIVE", sql);
        Assert.Contains("RDB$STATISTICS", sql);
        Assert.Contains("RDB$EXPRESSION_SOURCE", sql);
        Assert.Contains("'PRIMARY KEY'", sql);
        Assert.Contains("'FOREIGN KEY'", sql);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("UNIQUE", "")]
    [InlineData("CHECK", "")]
    [InlineData("PRIMARY KEY", "PRIMARY KEY")]
    [InlineData("primary key", "PRIMARY KEY")]
    [InlineData("FOREIGN KEY", "FOREIGN KEY")]
    [InlineData("foreign key", "FOREIGN KEY")]
    [InlineData("  PRIMARY KEY  ", "PRIMARY KEY")]
    public void NormalizeIndexType_MapsConstraintType(string? constraintType, string expected)
    {
        Assert.Equal(expected, FirebirdTableDetailReader.NormalizeIndexType(constraintType));
    }

    [Theory]
    // Firebird stores -1 in RDB$STATISTICS for indexes with no computed selectivity
    // (freshly created / empty table). That sentinel must surface as null (blank cell),
    // never as a literal "-1". Any negative is treated as the sentinel.
    [InlineData(-1.0, null)]
    [InlineData(-0.5, null)]
    [InlineData(null, null)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.0001234, 0.0001234)]
    [InlineData(1.0, 1.0)]
    public void NormalizeStatistics_MapsNegativeSentinelToNull(double? raw, double? expected)
    {
        Assert.Equal(expected, FirebirdTableDetailReader.NormalizeStatistics(raw));
    }

    [Fact]
    public void IndexInfo_NewPropertiesDefaultsAreSensible()
    {
        var idx = new IndexInfo();
        Assert.Equal(string.Empty, idx.Name);
        Assert.Equal(string.Empty, idx.Fields);
        Assert.False(idx.IsUnique);
        Assert.False(idx.IsDescending);
        Assert.True(idx.IsActive);
        Assert.Null(idx.Statistics);
        Assert.Null(idx.Expression);
        Assert.Equal(string.Empty, idx.IndexType);
        Assert.False(idx.IsPrimary);
        Assert.False(idx.IsForeignKeyIndex);
    }

    [Fact]
    public void IndexInfo_IsPrimary_DerivesFromIndexType()
    {
        var pk = new IndexInfo { IndexType = "PRIMARY KEY" };
        Assert.True(pk.IsPrimary);
        Assert.False(pk.IsForeignKeyIndex);

        var fk = new IndexInfo { IndexType = "FOREIGN KEY" };
        Assert.False(fk.IsPrimary);
        Assert.True(fk.IsForeignKeyIndex);

        var plain = new IndexInfo { IndexType = string.Empty };
        Assert.False(plain.IsPrimary);
        Assert.False(plain.IsForeignKeyIndex);
    }

    [Fact]
    public void IndexInfo_IsPrimary_IsCaseInsensitive()
    {
        var idx = new IndexInfo { IndexType = "primary key" };
        Assert.True(idx.IsPrimary);
    }

    [Fact]
    public void IndexInfo_NewPropertiesRoundtripInit()
    {
        var idx = new IndexInfo
        {
            Name = "IDX_USER_EMAIL",
            Fields = "EMAIL",
            IsUnique = true,
            IsDescending = false,
            IsActive = false,
            Statistics = 0.123456,
            Expression = "UPPER(EMAIL)",
            IndexType = "FOREIGN KEY",
        };
        Assert.Equal("IDX_USER_EMAIL", idx.Name);
        Assert.False(idx.IsActive);
        Assert.Equal(0.123456, idx.Statistics);
        Assert.Equal("UPPER(EMAIL)", idx.Expression);
        Assert.True(idx.IsForeignKeyIndex);
    }

    [Fact]
    public void ConstraintsSql_IncludesIndexNameAndFkRules()
    {
        var sql = FirebirdTableDetailReader.ConstraintsSql;
        Assert.Contains("rc.RDB$INDEX_NAME", sql);
        Assert.Contains("fk.RDB$UPDATE_RULE", sql);
        Assert.Contains("fk.RDB$DELETE_RULE", sql);
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

    [Fact]
    public void DependencyInfo_DefaultsAreSensible()
    {
        var d = new DependencyInfo();
        Assert.Equal(string.Empty, d.ObjectName);
        Assert.Equal(string.Empty, d.ObjectType);
        Assert.Null(d.FieldName);
    }

    [Fact]
    public void DependencyInfo_InitRoundtripsValues()
    {
        var d = new DependencyInfo
        {
            ObjectName = "V_USERS",
            ObjectType = "View",
            FieldName = "ID",
        };
        Assert.Equal("V_USERS", d.ObjectName);
        Assert.Equal("View", d.ObjectType);
        Assert.Equal("ID", d.FieldName);
    }

    [Theory]
    [InlineData(0, "Table")]
    [InlineData(1, "View")]
    [InlineData(2, "Trigger")]
    [InlineData(5, "Procedure")]
    [InlineData(7, "Exception")]
    [InlineData(8, "User")]
    [InlineData(9, "Domain")]
    [InlineData(10, "Index")]
    [InlineData(14, "Generator")]
    [InlineData(15, "Function")]
    [InlineData(18, "Package")]
    public void MapObjectType_KnownCodes(int code, string expected)
    {
        Assert.Equal(expected, FirebirdTableDetailReader.MapObjectType(code));
    }

    [Theory]
    [InlineData(3, "Object (3)")]
    [InlineData(99, "Object (99)")]
    [InlineData(-1, "Object (-1)")]
    public void MapObjectType_UnknownCodesFallBack(int code, string expected)
    {
        Assert.Equal(expected, FirebirdTableDetailReader.MapObjectType(code));
    }

    [Fact]
    public void MapObjectType_NullReturnsEmpty()
    {
        Assert.Equal(string.Empty, FirebirdTableDetailReader.MapObjectType(null));
    }

    [Fact]
    public void DependsOnSql_UnionsRelationFieldsAndDependencies()
    {
        // DependsOn = (a) user-defined domains the table references via
        // RDB$RELATION_FIELDS.RDB$FIELD_SOURCE (type hardcoded to 9 = "Domain"),
        // UNION ALL with (b) RDB$DEPENDENCIES rows where the table is the
        // dependent (computed cols, defaults, etc.).
        var sql = FirebirdTableDetailReader.DependsOnSql;
        Assert.Contains("RDB$RELATION_FIELDS", sql);
        Assert.Contains("rf.RDB$FIELD_SOURCE", sql);
        Assert.Contains("RDB$RELATION_NAME", sql);
        Assert.Contains("CAST(9 AS INTEGER)", sql);
        Assert.Contains("UNION ALL", sql);
        Assert.Contains("RDB$DEPENDENCIES", sql);
        Assert.Contains("TRIM(d.RDB$DEPENDENT_NAME) = @t2", sql);
        Assert.Contains("d.RDB$DEPENDED_ON_NAME", sql);
        Assert.Contains("d.RDB$DEPENDED_ON_TYPE", sql);
        Assert.Contains("d.RDB$PACKAGE_NAME IS NULL", sql);
        Assert.Contains("DISTINCT", sql);
    }

    [Fact]
    public void DependsOnSql_ExcludesAnonymousBackingDomains()
    {
        // The RDB$FIELD_SOURCE branch must filter the RDB$<n> anonymous backing
        // domains FB synthesizes for inline column types — only user domains
        // should appear in the "Domains" category.
        var sql = FirebirdTableDetailReader.DependsOnSql;
        Assert.Contains("rf.RDB$FIELD_SOURCE NOT STARTING WITH 'RDB$'", sql);
    }

    [Fact]
    public void DependsOnSql_ExcludesRelationTypeRows()
    {
        // Related Tables come exclusively from FK queries — RDB$DEPENDENCIES
        // branches must not return type-0 (Relation) rows.
        var sql = FirebirdTableDetailReader.DependsOnSql;
        Assert.Contains("d.RDB$DEPENDED_ON_TYPE <> 0", sql);
    }

    [Fact]
    public void DependedOnBySql_ExcludesRelationTypeRows()
    {
        var sql = FirebirdTableDetailReader.DependedOnBySql;
        Assert.Contains("d.RDB$DEPENDENT_TYPE <> 0", sql);
        // Indirect branch keeps Views only via the VIEW_BLR gate.
        Assert.Contains("r.RDB$VIEW_BLR IS NOT NULL", sql);
    }

    [Fact]
    public void FkOutgoingSql_QueriesRefConstraintsOnly()
    {
        var sql = FirebirdTableDetailReader.FkOutgoingSql;
        Assert.Contains("RDB$REF_CONSTRAINTS", sql);
        Assert.Contains("RDB$RELATION_CONSTRAINTS", sql);
        Assert.Contains("rc.RDB$CONST_NAME_UQ", sql);
        Assert.Contains("rc.RDB$CONSTRAINT_NAME", sql);
        Assert.Contains("TRIM(fk.RDB$RELATION_NAME) = @tableName", sql);
        Assert.Contains("pk.RDB$RELATION_NAME", sql);
        Assert.DoesNotContain("RDB$DEPENDENCIES", sql);
    }

    [Fact]
    public void FkIncomingSql_QueriesRefConstraintsOnly()
    {
        var sql = FirebirdTableDetailReader.FkIncomingSql;
        Assert.Contains("RDB$REF_CONSTRAINTS", sql);
        Assert.Contains("RDB$RELATION_CONSTRAINTS", sql);
        Assert.Contains("rc.RDB$CONST_NAME_UQ", sql);
        Assert.Contains("rc.RDB$CONSTRAINT_NAME", sql);
        Assert.Contains("TRIM(pk.RDB$RELATION_NAME) = @tableName", sql);
        Assert.Contains("fk.RDB$RELATION_NAME", sql);
        Assert.DoesNotContain("RDB$DEPENDENCIES", sql);
    }

    [Fact]
    public void DependedOnBySql_FiltersByDependedOnAndSelectsDependent()
    {
        // "Used by" — restricted to dependencies where the depended-on side is
        // a relation (RDB$DEPENDED_ON_TYPE = 0) named @tableName.
        // RDB$DEPENDENT_TYPE = 3 (computed field) is excluded as anonymous noise.
        var sql = FirebirdTableDetailReader.DependedOnBySql;
        Assert.Contains("RDB$DEPENDENCIES", sql);
        Assert.Contains("d.RDB$DEPENDED_ON_TYPE = 0", sql);
        Assert.Contains("TRIM(d.RDB$DEPENDED_ON_NAME) = @tableName", sql);
        Assert.Contains("d.RDB$DEPENDENT_TYPE <> 3", sql);
        Assert.Contains("d.RDB$DEPENDENT_NAME", sql);
        Assert.Contains("d.RDB$DEPENDENT_TYPE", sql);
        Assert.Contains("d.RDB$FIELD_NAME", sql);
        Assert.Contains("DISTINCT", sql);
    }

    [Fact]
    public void BuildDataPreviewSql_FirstPage_EmitsRowsOneToPageSize()
    {
        var sql = FirebirdTableDetailReader.BuildDataPreviewSql("NAGL", 1, 200, null);
        Assert.Equal("SELECT * FROM \"NAGL\" ROWS 1 TO 200", sql);
    }

    [Fact]
    public void BuildDataPreviewSql_OrderBy_AppendsBeforeRows()
    {
        // ORDER BY must precede ROWS in Firebird SQL grammar.
        var sql = FirebirdTableDetailReader.BuildDataPreviewSql("NAGL", 1, 200, "\"ID\" ASC");
        Assert.Equal("SELECT * FROM \"NAGL\" ORDER BY \"ID\" ASC ROWS 1 TO 200", sql);
    }

    [Fact]
    public void BuildDataPreviewSql_WhitespaceOrderBy_DoesNotAppend()
    {
        var sql = FirebirdTableDetailReader.BuildDataPreviewSql("T", 1, 10, "   ");
        Assert.Equal("SELECT * FROM \"T\" ROWS 1 TO 10", sql);
    }

    [Fact]
    public void BuildDataPreviewSql_QuotedTableName_DoublesInternalQuotes()
    {
        // Defence against pathological table names with embedded quotes —
        // matches the existing identifier-quoting convention used elsewhere
        // (e.g. FirebirdDdlReader.Quote).
        var sql = FirebirdTableDetailReader.BuildDataPreviewSql("A\"B", 1, 5, null);
        Assert.Equal("SELECT * FROM \"A\"\"B\" ROWS 1 TO 5", sql);
    }

    [Fact]
    public void BuildDataPreviewSql_TrimsOrderBy()
    {
        // Caller may pass slightly padded ORDER BY clauses; we don't want a
        // double space in the emitted SQL.
        var sql = FirebirdTableDetailReader.BuildDataPreviewSql("T", 1, 5, "  \"ID\" DESC  ");
        Assert.Equal("SELECT * FROM \"T\" ORDER BY \"ID\" DESC ROWS 1 TO 5", sql);
    }

    [Theory]
    [InlineData(1, 200, 1, 200)]
    [InlineData(2, 200, 201, 400)]
    [InlineData(3, 50, 101, 150)]
    [InlineData(0, 100, 1, 100)]      // page < 1 clamps to 1
    [InlineData(-5, 100, 1, 100)]     // negative also clamps to 1
    public void ComputeRowRange_ReturnsOneBasedInclusive(int page, int pageSize, int expectedStart, int expectedEnd)
    {
        var (start, end) = FirebirdTableDetailReader.ComputeRowRange(page, pageSize);
        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedEnd, end);
    }

    [Fact]
    public void BuildRowCountSql_WrapsTableInFirstCappedDerivedTable()
    {
        var sql = FirebirdTableDetailReader.BuildRowCountSql("NAGL", 50000);
        Assert.Equal("SELECT COUNT(*) FROM (SELECT FIRST 50000 1 AS X FROM \"NAGL\") sub", sql);
    }

    [Fact]
    public void BuildRowCountSql_QuotedTableName_DoublesInternalQuotes()
    {
        var sql = FirebirdTableDetailReader.BuildRowCountSql("A\"B", 50000);
        Assert.Equal("SELECT COUNT(*) FROM (SELECT FIRST 50000 1 AS X FROM \"A\"\"B\") sub", sql);
    }
}
