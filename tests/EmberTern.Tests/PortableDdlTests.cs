using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the pure portable-DDL composition used by the metadata export + the DDL tab:
/// structure + COMMENT ON, comment-only-when-present (no IS NULL noise), no GRANT/REVOKE,
/// Polish characters preserved, statements terminated but never double-terminated.
/// (The reader orchestration in MetadataExportService is DB-smoke.)
/// </summary>
public class PortableDdlTests
{
    // ─── ObjectComment ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ObjectComment_BlankDescription_ReturnsNull(string? description)
    {
        Assert.Null(PortableDdl.ObjectComment(MetadataObjectKind.Procedure, "P", description));
    }

    [Theory]
    [InlineData(MetadataObjectKind.Table, "COMMENT ON TABLE")]
    [InlineData(MetadataObjectKind.SystemTable, "COMMENT ON TABLE")]
    [InlineData(MetadataObjectKind.View, "COMMENT ON VIEW")]
    [InlineData(MetadataObjectKind.Procedure, "COMMENT ON PROCEDURE")]
    [InlineData(MetadataObjectKind.Function, "COMMENT ON FUNCTION")]
    [InlineData(MetadataObjectKind.Trigger, "COMMENT ON TRIGGER")]
    [InlineData(MetadataObjectKind.Package, "COMMENT ON PACKAGE")]
    [InlineData(MetadataObjectKind.Domain, "COMMENT ON DOMAIN")]
    [InlineData(MetadataObjectKind.Generator, "COMMENT ON SEQUENCE")]
    [InlineData(MetadataObjectKind.Exception, "COMMENT ON EXCEPTION")]
    [InlineData(MetadataObjectKind.Index, "COMMENT ON INDEX")]
    public void ObjectComment_EmitsRightKindKeyword(MetadataObjectKind kind, string expectedPrefix)
    {
        var s = PortableDdl.ObjectComment(kind, "OBJ", "hello");
        Assert.NotNull(s);
        Assert.StartsWith(expectedPrefix, s);
        Assert.Contains("IS 'hello'", s);
        Assert.DoesNotContain("IS NULL", s);
    }

    [Theory]
    [InlineData(MetadataObjectKind.Role)]
    [InlineData(MetadataObjectKind.User)]
    public void ObjectComment_KindWithoutCommentConcept_ReturnsNull(MetadataObjectKind kind)
    {
        Assert.Null(PortableDdl.ObjectComment(kind, "R", "desc"));
    }

    [Fact]
    public void ObjectComment_EscapesSingleQuotes()
    {
        var s = PortableDdl.ObjectComment(MetadataObjectKind.Table, "T", "it's a test");
        Assert.Contains("IS 'it''s a test'", s);
    }

    [Fact]
    public void ObjectComment_PreservesPolishCharacters()
    {
        const string polish = "Kolumna zawiera adres zamówień — żółć";
        var s = PortableDdl.ObjectComment(MetadataObjectKind.Table, "T", polish);
        Assert.Contains(polish, s);
    }

    // ─── Compose ──────────────────────────────────────────────────────────

    [Fact]
    public void Compose_StructureOnly_EnsuresTrailingSemicolon()
    {
        Assert.Equal("CREATE SEQUENCE \"G\";", PortableDdl.Compose("CREATE SEQUENCE \"G\""));
    }

    [Fact]
    public void Compose_DoesNotDoubleTerminate()
    {
        Assert.Equal("CREATE SEQUENCE \"G\";", PortableDdl.Compose("CREATE SEQUENCE \"G\";"));
    }

    [Fact]
    public void Compose_AppendsCommentAfterBlankLine_BothTerminated()
    {
        var result = PortableDdl.Compose(
            "CREATE OR ALTER PROCEDURE \"P\" AS BEGIN SUSPEND; END",
            new[] { "COMMENT ON PROCEDURE \"P\" IS 'x'" });

        Assert.Equal(
            "CREATE OR ALTER PROCEDURE \"P\" AS BEGIN SUSPEND; END;\n\nCOMMENT ON PROCEDURE \"P\" IS 'x';",
            result);
    }

    [Fact]
    public void Compose_KeepsMultiStatementStructureVerbatim()
    {
        const string structure = "CREATE TABLE \"T\" (...);\nALTER TABLE \"T\" ADD CONSTRAINT \"PK\" PRIMARY KEY (\"ID\");";
        var result = PortableDdl.Compose(structure, new[] { "COMMENT ON TABLE \"T\" IS 'c'" });

        Assert.StartsWith(structure, result);
        Assert.Contains("\n\nCOMMENT ON TABLE \"T\" IS 'c';", result);
    }

    [Fact]
    public void Compose_SkipsNullAndBlankTrailingStatements()
    {
        var result = PortableDdl.Compose(
            "CREATE TABLE \"T\" (...);",
            new string?[] { null, "   ", "COMMENT ON TABLE \"T\" IS 'c'", "" });

        Assert.Equal("CREATE TABLE \"T\" (...);\n\nCOMMENT ON TABLE \"T\" IS 'c';", result);
    }

    [Fact]
    public void Compose_TableWithColumnComments_OrdersStructureThenTableThenColumns()
    {
        var result = PortableDdl.Compose(
            "CREATE TABLE \"T\" (...);",
            new[]
            {
                PortableDdl.ObjectComment(MetadataObjectKind.Table, "T", "table doc"),
                DdlGenerator.BuildCommentColumn("T", "ID", "id doc"),
                DdlGenerator.BuildCommentColumn("T", "NAME", "name doc"),
            });

        var iStruct = result.IndexOf("CREATE TABLE", System.StringComparison.Ordinal);
        var iTable = result.IndexOf("COMMENT ON TABLE", System.StringComparison.Ordinal);
        var iId = result.IndexOf("\"ID\"", System.StringComparison.Ordinal);
        var iName = result.IndexOf("\"NAME\"", System.StringComparison.Ordinal);
        Assert.True(iStruct < iTable && iTable < iId && iId < iName);
    }

    [Fact]
    public void Compose_NeverEmitsGrantOrRevoke()
    {
        var result = PortableDdl.Compose(
            "CREATE OR ALTER PROCEDURE \"P\" AS BEGIN END",
            new[] { PortableDdl.ObjectComment(MetadataObjectKind.Procedure, "P", "doc") });

        Assert.DoesNotContain("GRANT", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("REVOKE", result, System.StringComparison.OrdinalIgnoreCase);
    }
}
