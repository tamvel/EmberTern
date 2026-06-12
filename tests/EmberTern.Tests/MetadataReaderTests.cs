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
}
