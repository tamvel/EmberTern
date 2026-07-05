using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// SQL-shape pins for the server-side search reader. The live CONTAINING run needs a
// real Firebird (manual smoke, per the DB-path precedent); these lock the query shape,
// the source columns, the system-flag filter, the FB-version gate, and FB compatibility.
public class FirebirdMetadataSearchReaderTests
{
    [Fact]
    public void ProcedureSourceSql_SearchesProcedureSourceViaContaining()
    {
        var sql = FirebirdMetadataSearchReader.ProcedureSourceSql;
        Assert.Contains("RDB$PROCEDURES", sql);
        Assert.Contains("RDB$PROCEDURE_SOURCE CONTAINING @term", sql);
        Assert.Contains("COALESCE(RDB$SYSTEM_FLAG, 0) = 0", sql);
    }

    [Fact]
    public void ViewSourceSql_OnlyViews_ViaViewSource()
    {
        var sql = FirebirdMetadataSearchReader.ViewSourceSql;
        Assert.Contains("RDB$VIEW_BLR IS NOT NULL", sql);
        Assert.Contains("RDB$VIEW_SOURCE CONTAINING @term", sql);
    }

    [Fact]
    public void TriggerSourceSql_SearchesTriggerSource()
    {
        var sql = FirebirdMetadataSearchReader.TriggerSourceSql;
        Assert.Contains("RDB$TRIGGERS", sql);
        Assert.Contains("RDB$TRIGGER_SOURCE CONTAINING @term", sql);
    }

    [Fact]
    public void FunctionSourceSql_UsesFb3OnlyFunctionSource()
    {
        var sql = FirebirdMetadataSearchReader.FunctionSourceSql;
        Assert.Contains("RDB$FUNCTIONS", sql);
        Assert.Contains("RDB$FUNCTION_SOURCE CONTAINING @term", sql);
    }

    [Fact]
    public void PackageSourceSql_SearchesBothHeaderAndBody()
    {
        var sql = FirebirdMetadataSearchReader.PackageSourceSql;
        Assert.Contains("RDB$PACKAGES", sql);
        Assert.Contains("RDB$PACKAGE_HEADER_SOURCE CONTAINING @term", sql);
        Assert.Contains("RDB$PACKAGE_BODY_SOURCE CONTAINING @term", sql);
        Assert.Contains(" OR ", sql);
    }

    [Fact]
    public void ExceptionMessageSql_SearchesMessageText()
    {
        var sql = FirebirdMetadataSearchReader.ExceptionMessageSql;
        Assert.Contains("RDB$EXCEPTIONS", sql);
        Assert.Contains("RDB$MESSAGE CONTAINING @term", sql);
    }

    [Fact]
    public void TableFieldSql_SearchesColumnNames_UserTablesOnly()
    {
        var sql = FirebirdMetadataSearchReader.TableFieldSql;
        Assert.Contains("RDB$RELATION_FIELDS", sql);
        Assert.Contains("rf.RDB$FIELD_NAME CONTAINING @term", sql);
        // Excludes views (fields belong to tables) and system relations.
        Assert.Contains("RDB$VIEW_BLR IS NULL", sql);
        Assert.Contains("COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0", sql);
        // Returns table + field so the hit can point at both.
        Assert.Contains("rf.RDB$RELATION_NAME", sql);
    }

    [Fact]
    public void AllSourceQueries_ParameterizeTheTerm_NoLiteralInjection()
    {
        foreach (var sql in new[]
        {
            FirebirdMetadataSearchReader.ProcedureSourceSql,
            FirebirdMetadataSearchReader.ViewSourceSql,
            FirebirdMetadataSearchReader.TriggerSourceSql,
            FirebirdMetadataSearchReader.FunctionSourceSql,
            FirebirdMetadataSearchReader.PackageSourceSql,
            FirebirdMetadataSearchReader.ExceptionMessageSql,
            FirebirdMetadataSearchReader.TableFieldSql,
        })
        {
            Assert.Contains("@term", sql);
        }
    }

    [Theory]
    [InlineData(MetadataObjectKind.Function, true)]
    [InlineData(MetadataObjectKind.Package, true)]
    [InlineData(MetadataObjectKind.Procedure, false)]
    [InlineData(MetadataObjectKind.View, false)]
    [InlineData(MetadataObjectKind.Trigger, false)]
    [InlineData(MetadataObjectKind.Exception, false)]
    [InlineData(MetadataObjectKind.Table, false)]
    public void RequiresFb3_OnlyFunctionAndPackage(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, FirebirdMetadataSearchReader.RequiresFb3(kind));
}
