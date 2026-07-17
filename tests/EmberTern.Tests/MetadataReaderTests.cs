using System.Threading.Tasks;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class MetadataReaderTests
{
    [Theory]
    [InlineData("RDB$RELATIONS")]
    [InlineData("rdb$database")]
    [InlineData("MON$ATTACHMENTS")]
    [InlineData("SEC$USERS")]
    public void IsSystemName_ReturnsTrueForSystemPrefixes(string name)
    {
        Assert.True(FirebirdMetadataReader.IsSystemName(name));
    }

    [Theory]
    [InlineData("CUSTOMERS")]
    [InlineData("MyTable")]
    [InlineData("INVOICE_LINES")]
    [InlineData("PROC_CALC_BALANCE")]
    public void IsSystemName_ReturnsFalseForUserObjects(string name)
    {
        Assert.False(FirebirdMetadataReader.IsSystemName(name));
    }

    [Fact]
    public void DomainsSql_SelectsTypeColumns()
    {
        var sql = FirebirdMetadataReader.DomainsSql;
        Assert.Contains("RDB$FIELDS", sql);
        Assert.Contains("RDB$FIELD_TYPE", sql);
        Assert.Contains("RDB$FIELD_LENGTH", sql);
        Assert.Contains("RDB$FIELD_SCALE", sql);
        Assert.Contains("RDB$FIELD_PRECISION", sql);
        Assert.Contains("RDB$FIELD_SUB_TYPE", sql);
        // Faza 2: Not Null + charset for the rich domain list.
        Assert.Contains("RDB$NULL_FLAG", sql);
        Assert.Contains("RDB$CHARACTER_SETS", sql);
        Assert.Contains("RDB$CHARACTER_SET_NAME", sql);
    }

    [Fact]
    public void IsSystemName_ReturnsTrueForEmpty()
    {
        Assert.True(FirebirdMetadataReader.IsSystemName(""));
        Assert.True(FirebirdMetadataReader.IsSystemName(null!));
    }

    [Theory]
    [InlineData(MetadataObjectKind.Table)]
    [InlineData(MetadataObjectKind.View)]
    [InlineData(MetadataObjectKind.Procedure)]
    [InlineData(MetadataObjectKind.Trigger)]
    [InlineData(MetadataObjectKind.Function)]
    [InlineData(MetadataObjectKind.Generator)]
    [InlineData(MetadataObjectKind.Domain)]
    [InlineData(MetadataObjectKind.Package)]
    [InlineData(MetadataObjectKind.Exception)]
    [InlineData(MetadataObjectKind.Role)]
    [InlineData(MetadataObjectKind.Index)]
    public void SqlFor_FiltersSystemFlag(MetadataObjectKind kind)
    {
        var sql = FirebirdMetadataReader.SqlFor(kind);
        Assert.Contains("RDB$SYSTEM_FLAG", sql);
        Assert.Contains("COALESCE", sql);
    }

    [Fact]
    public void SqlFor_SystemTable_InvertsSystemFlagFilter()
    {
        var sql = FirebirdMetadataReader.SqlFor(MetadataObjectKind.SystemTable);
        // SystemTable is the *inverse* category: we want system-owned rows.
        Assert.Contains("RDB$SYSTEM_FLAG = 1", sql);
        Assert.Contains("RDB$VIEW_BLR IS NULL", sql);
        Assert.Contains("RDB$RELATIONS", sql);
    }

    [Fact]
    public void SqlFor_User_QueriesSecUsers()
    {
        var sql = FirebirdMetadataReader.SqlFor(MetadataObjectKind.User);
        Assert.Contains("SEC$USERS", sql);
        Assert.Contains("SEC$USER_NAME", sql);
    }

    [Theory]
    [InlineData(MetadataObjectKind.Domain, "RDB$FIELDS", "RDB$FIELD_NAME")]
    [InlineData(MetadataObjectKind.Package, "RDB$PACKAGES", "RDB$PACKAGE_NAME")]
    [InlineData(MetadataObjectKind.Exception, "RDB$EXCEPTIONS", "RDB$EXCEPTION_NAME")]
    [InlineData(MetadataObjectKind.Role, "RDB$ROLES", "RDB$ROLE_NAME")]
    [InlineData(MetadataObjectKind.Index, "RDB$INDICES", "RDB$INDEX_NAME")]
    public void SqlFor_NewKinds_UseCorrectSystemTable(MetadataObjectKind kind, string expectedTable, string expectedColumn)
    {
        var sql = FirebirdMetadataReader.SqlFor(kind);
        Assert.Contains(expectedTable, sql);
        Assert.Contains(expectedColumn, sql);
    }

    [Fact]
    public void BypassSystemNameFilter_OnlySystemTableBypasses()
    {
        Assert.True(FirebirdMetadataReader.BypassSystemNameFilter(MetadataObjectKind.SystemTable));
        foreach (MetadataObjectKind kind in System.Enum.GetValues<MetadataObjectKind>())
        {
            if (kind == MetadataObjectKind.SystemTable) continue;
            Assert.False(FirebirdMetadataReader.BypassSystemNameFilter(kind));
        }
    }

    [Theory]
    [InlineData(MetadataObjectKind.Procedure)]
    [InlineData(MetadataObjectKind.Function)]
    public void SqlFor_Fb3Plus_ExcludesPackagedRoutines(MetadataObjectKind kind)
    {
        // On FB3+ packaged routines share the catalog with standalone ones — the
        // top-level Functions/Procedures list must exclude them (else a packaged
        // namesake shows as a duplicate). Gated on the server major.
        Assert.Contains("RDB$PACKAGE_NAME IS NULL", FirebirdMetadataReader.SqlFor(kind, 5));
        Assert.Contains("RDB$PACKAGE_NAME IS NULL", FirebirdMetadataReader.CountSqlFor(kind, 5));
        // FB2.5 has no RDB$PACKAGE_NAME column — the filter must NOT be emitted there.
        Assert.DoesNotContain("RDB$PACKAGE_NAME", FirebirdMetadataReader.SqlFor(kind, 2));
        Assert.DoesNotContain("RDB$PACKAGE_NAME", FirebirdMetadataReader.CountSqlFor(kind, 2));
        // The 1-arg overload (used by the shape tests) stays FB2.5-safe too.
        Assert.DoesNotContain("RDB$PACKAGE_NAME", FirebirdMetadataReader.SqlFor(kind));
    }

    [Theory]
    [InlineData(MetadataObjectKind.Table)]
    [InlineData(MetadataObjectKind.Trigger)]
    [InlineData(MetadataObjectKind.Index)]
    public void SqlFor_Fb3Plus_NonRoutineKinds_Unfiltered(MetadataObjectKind kind)
    {
        // The package filter applies ONLY to standalone procedures/functions.
        Assert.DoesNotContain("RDB$PACKAGE_NAME", FirebirdMetadataReader.SqlFor(kind, 5));
    }

    [Fact]
    public void SqlFor_TableVsView_DistinguishesByViewBlr()
    {
        var tables = FirebirdMetadataReader.SqlFor(MetadataObjectKind.Table);
        var views = FirebirdMetadataReader.SqlFor(MetadataObjectKind.View);

        Assert.Contains("RDB$VIEW_BLR IS NULL", tables);
        Assert.Contains("RDB$VIEW_BLR IS NOT NULL", views);
        Assert.Contains("RDB$RELATIONS", tables);
        Assert.Contains("RDB$RELATIONS", views);
    }

    [Theory]
    [InlineData(MetadataObjectKind.Procedure, "RDB$PROCEDURES", "RDB$PROCEDURE_NAME")]
    [InlineData(MetadataObjectKind.Trigger, "RDB$TRIGGERS", "RDB$TRIGGER_NAME")]
    [InlineData(MetadataObjectKind.Function, "RDB$FUNCTIONS", "RDB$FUNCTION_NAME")]
    [InlineData(MetadataObjectKind.Generator, "RDB$GENERATORS", "RDB$GENERATOR_NAME")]
    public void SqlFor_UsesCorrectSystemTable(MetadataObjectKind kind, string expectedTable, string expectedColumn)
    {
        var sql = FirebirdMetadataReader.SqlFor(kind);
        Assert.Contains(expectedTable, sql);
        Assert.Contains(expectedColumn, sql);
    }

    [Fact]
    public void SqlFor_OrdersByName()
    {
        foreach (MetadataObjectKind kind in System.Enum.GetValues<MetadataObjectKind>())
        {
            var sql = FirebirdMetadataReader.SqlFor(kind);
            Assert.Contains("ORDER BY", sql);
        }
    }

    [Fact]
    public async Task ListAsync_WithoutConnection_Throws()
    {
        using var service = new FirebirdConnectionService();
        var reader = new FirebirdMetadataReader(service);

        await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => reader.ListAsync(MetadataObjectKind.Table));
    }

    // ─── COUNT-only (lazy-load) ───────────────────────────────────────────

    [Fact]
    public void CountSqlFor_AllKinds_AreCountStarWithoutOrderBy()
    {
        foreach (MetadataObjectKind kind in System.Enum.GetValues<MetadataObjectKind>())
        {
            var sql = FirebirdMetadataReader.CountSqlFor(kind);
            Assert.Contains("COUNT(*)", sql);
            // A bare COUNT(*) has no ORDER BY — sorting the rows we never fetch is waste.
            Assert.DoesNotContain("ORDER BY", sql);
        }
    }

    [Theory]
    [InlineData(MetadataObjectKind.Table, "RDB$RELATIONS")]
    [InlineData(MetadataObjectKind.View, "RDB$RELATIONS")]
    [InlineData(MetadataObjectKind.Procedure, "RDB$PROCEDURES")]
    [InlineData(MetadataObjectKind.Trigger, "RDB$TRIGGERS")]
    [InlineData(MetadataObjectKind.Function, "RDB$FUNCTIONS")]
    [InlineData(MetadataObjectKind.Generator, "RDB$GENERATORS")]
    [InlineData(MetadataObjectKind.Domain, "RDB$FIELDS")]
    [InlineData(MetadataObjectKind.Package, "RDB$PACKAGES")]
    [InlineData(MetadataObjectKind.Exception, "RDB$EXCEPTIONS")]
    [InlineData(MetadataObjectKind.Role, "RDB$ROLES")]
    [InlineData(MetadataObjectKind.User, "SEC$USERS")]
    [InlineData(MetadataObjectKind.Index, "RDB$INDICES")]
    [InlineData(MetadataObjectKind.SystemTable, "RDB$RELATIONS")]
    public void CountSqlFor_UsesCorrectCatalogTable(MetadataObjectKind kind, string expectedTable)
    {
        Assert.Contains(expectedTable, FirebirdMetadataReader.CountSqlFor(kind));
    }

    [Fact]
    public void CountSqlFor_Domain_ExcludesAnonymousBackingDomains()
    {
        // RDB$FIELDS holds one anonymous RDB$xxx domain per inline column type; the
        // count must strip them server-side or it reports thousands more than the
        // displayed user-domain list (which IsSystemName strips client-side).
        var sql = FirebirdMetadataReader.CountSqlFor(MetadataObjectKind.Domain);
        Assert.Contains("NOT STARTING WITH 'RDB$'", sql);
    }

    [Fact]
    public void CountSqlFor_SystemTable_InvertsSystemFlag()
    {
        var sql = FirebirdMetadataReader.CountSqlFor(MetadataObjectKind.SystemTable);
        Assert.Contains("RDB$SYSTEM_FLAG = 1", sql);
        Assert.Contains("RDB$VIEW_BLR IS NULL", sql);
    }

    [Theory]
    [InlineData(MetadataObjectKind.Table)]
    [InlineData(MetadataObjectKind.View)]
    [InlineData(MetadataObjectKind.Procedure)]
    [InlineData(MetadataObjectKind.Domain)]
    [InlineData(MetadataObjectKind.Index)]
    public void CountSqlFor_FiltersSystemFlag(MetadataObjectKind kind)
    {
        var sql = FirebirdMetadataReader.CountSqlFor(kind);
        Assert.Contains("RDB$SYSTEM_FLAG", sql);
        Assert.Contains("COALESCE", sql);
    }

    [Fact]
    public async Task CountAsync_WithoutConnection_Throws()
    {
        using var service = new FirebirdConnectionService();
        var reader = new FirebirdMetadataReader(service);

        await Assert.ThrowsAsync<System.InvalidOperationException>(
            () => reader.CountAsync(MetadataObjectKind.Table));
    }

    // ─── Enriched columns query (Package 5, Stage A) ──────────────────────

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void ColumnsSqlFor_Fb3Plus_ProjectsTheRawIdentityType(int serverMajor)
    {
        // FB3+ reads RDB$IDENTITY_TYPE — and projects its RAW VALUE, not a 0/1 "is it one?" flag: the
        // value IS the ALWAYS (0) / BY DEFAULT (1) distinction, and Firebird rejects an INSERT naming a
        // GENERATED ALWAYS column without OVERRIDING SYSTEM VALUE. Collapsing it to a bool — as this
        // query used to — discarded the only fact that tells generated DML which form is legal.
        var sql = FirebirdMetadataReader.ColumnsSqlFor(serverMajor);
        Assert.Contains("RDB$IDENTITY_TYPE", sql);
        Assert.Contains("AS IDENTITY_TYPE", sql);
        Assert.DoesNotContain("CASE WHEN rf.RDB$IDENTITY_TYPE IS NOT NULL", sql); // the discarded-fact form
    }

    [Fact]
    public void ColumnsSqlFor_Fb25_DoesNotReferenceIdentityTypeColumn()
    {
        // RDB$IDENTITY_TYPE does not exist on FB2.5 — referencing it would throw
        // (gotcha #146). The identity slot must be projected as a constant instead,
        // so the reader ordinals stay identical across versions.
        var sql = FirebirdMetadataReader.ColumnsSqlFor(2);
        Assert.DoesNotContain("RDB$IDENTITY_TYPE", sql);
        // NULL, not 0: on FB3+ the projected value is the identity TYPE, where 0 means ALWAYS. A
        // constant 0 here would report every FB2.5 column as a GENERATED ALWAYS identity.
        Assert.Contains("CAST(NULL AS SMALLINT) AS IDENTITY_TYPE", sql);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void ColumnsSqlFor_ProjectsRichColumnFacts(int serverMajor)
    {
        // Every rich-but-optional Quick Info fact the reader maps must be projected,
        // regardless of server version.
        var sql = FirebirdMetadataReader.ColumnsSqlFor(serverMajor);
        Assert.Contains("RDB$DEFAULT_SOURCE", sql);
        Assert.Contains("RDB$DESCRIPTION", sql);
        Assert.Contains("RDB$COMPUTED_SOURCE", sql);
        Assert.Contains("'PRIMARY KEY'", sql);   // PK flag subquery
        Assert.Contains("'FOREIGN KEY'", sql);   // FK flag + FK-target subqueries
        Assert.Contains("RDB$REF_CONSTRAINTS", sql); // FK-target table resolution
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void ColumnsSqlFor_KeepsLightContract(int serverMajor)
    {
        // The enrichment must not change the query's binding contract: still keyed by
        // @name and still ordered by field position (so the dropdown/Quick Info column
        // order matches the table's declared order).
        var sql = FirebirdMetadataReader.ColumnsSqlFor(serverMajor);
        Assert.Contains("@name", sql);
        Assert.Contains("ORDER BY rf.RDB$FIELD_POSITION", sql);
        Assert.Contains("RDB$RELATION_FIELDS", sql);
    }
}
