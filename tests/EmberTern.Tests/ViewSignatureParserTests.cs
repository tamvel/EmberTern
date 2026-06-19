using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

// ViewSignatureParser splits CREATE [OR ALTER] VIEW name [(cols)] AS body for the
// View Detail Easy-mode round-trip. DdlGenerator.BuildCreateOrAlterView is the
// inverse. These pin both forms the user requires — with and without an explicit
// column list — and that Source → Easy → Source loses no information.
public class ViewSignatureParserTests
{
    // ─── Parse ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_NoColumnList_Succeeds()
    {
        var sig = ViewSignatureParser.Parse("CREATE VIEW V_TEST AS SELECT 1 AS X FROM RDB$DATABASE");
        Assert.True(sig.Success);
        Assert.Equal("V_TEST", sig.Name);
        Assert.False(sig.OrAlter);
        Assert.Empty(sig.Columns);
        Assert.Equal("SELECT 1 AS X FROM RDB$DATABASE", sig.Body);
    }

    [Fact]
    public void Parse_WithColumnList_Succeeds()
    {
        var sig = ViewSignatureParser.Parse("CREATE VIEW V_TEST (ID, NAME) AS SELECT id, name FROM t");
        Assert.True(sig.Success);
        Assert.Equal("V_TEST", sig.Name);
        Assert.Equal(new[] { "ID", "NAME" }, sig.Columns);
        Assert.Equal("SELECT id, name FROM t", sig.Body);
    }

    [Fact]
    public void Parse_OrAlter_IsTracked()
    {
        var sig = ViewSignatureParser.Parse("CREATE OR ALTER VIEW V (A) AS SELECT a FROM t");
        Assert.True(sig.Success);
        Assert.True(sig.OrAlter);
        Assert.Equal(new[] { "A" }, sig.Columns);
    }

    [Fact]
    public void Parse_UnquotedNames_FoldToUpper()
    {
        var sig = ViewSignatureParser.Parse("create view my_view (id, name) as select 1 from t");
        Assert.True(sig.Success);
        Assert.Equal("MY_VIEW", sig.Name);
        Assert.Equal(new[] { "ID", "NAME" }, sig.Columns);
    }

    [Fact]
    public void Parse_QuotedNames_PreserveCase()
    {
        var sig = ViewSignatureParser.Parse("CREATE VIEW \"MixedView\" (\"Id\", \"Name\") AS SELECT 1 FROM t");
        Assert.True(sig.Success);
        Assert.Equal("MixedView", sig.Name);
        Assert.Equal(new[] { "Id", "Name" }, sig.Columns);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SELECT 1 FROM RDB$DATABASE")]
    [InlineData("CREATE TABLE T (ID INTEGER)")]
    [InlineData("CREATE OR REPLACE VIEW V AS SELECT 1")]   // OR REPLACE isn't OR ALTER
    [InlineData("CREATE VIEW V (ID")]                       // unbalanced parens
    public void Parse_NonView_Fails(string sql)
        => Assert.False(ViewSignatureParser.Parse(sql).Success);

    [Fact]
    public void Parse_BodyKeepsInnerAs()
    {
        // The AS in "1 AS X" belongs to the body, not the header — only the first
        // top-level AS after the (optional) column list terminates the header.
        var sig = ViewSignatureParser.Parse("CREATE VIEW V AS SELECT 1 AS X, 2 AS Y FROM t");
        Assert.True(sig.Success);
        Assert.Equal("SELECT 1 AS X, 2 AS Y FROM t", sig.Body);
    }

    // ─── Round-trip (Source → Easy → Source loses no information) ──────────

    private static string RoundTrip(string sql)
    {
        var sig = ViewSignatureParser.Parse(sql);
        Assert.True(sig.Success);
        return DdlGenerator.BuildCreateOrAlterView(sig.Name!, sig.Columns, sig.Body, sig.OrAlter);
    }

    [Fact]
    public void RoundTrip_NoColumnList_PreservesShape()
    {
        const string sql = "CREATE VIEW V_TEST AS\nSELECT 1 AS X FROM RDB$DATABASE";
        var rebuilt = RoundTrip(sql);

        // Header gained no column list — the no-parens form is preserved.
        Assert.Equal("CREATE VIEW V_TEST", rebuilt.Split('\n')[0].TrimEnd());

        var sig = ViewSignatureParser.Parse(rebuilt);
        Assert.True(sig.Success);
        Assert.Equal("V_TEST", sig.Name);
        Assert.False(sig.OrAlter);
        Assert.Empty(sig.Columns);
        Assert.Equal("SELECT 1 AS X FROM RDB$DATABASE", sig.Body);

        // Idempotent: a second round-trip is byte-for-byte stable.
        Assert.Equal(rebuilt, RoundTrip(rebuilt));
    }

    [Fact]
    public void RoundTrip_WithColumnList_PreservesShape()
    {
        const string sql = "CREATE VIEW V_TEST (ID, NAME) AS\nSELECT id, name FROM t";
        var rebuilt = RoundTrip(sql);

        var sig = ViewSignatureParser.Parse(rebuilt);
        Assert.True(sig.Success);
        Assert.Equal("V_TEST", sig.Name);
        Assert.False(sig.OrAlter);
        Assert.Equal(new[] { "ID", "NAME" }, sig.Columns);
        Assert.Equal("SELECT id, name FROM t", sig.Body);

        Assert.Equal(rebuilt, RoundTrip(rebuilt));
    }

    [Fact]
    public void RoundTrip_OrAlter_PreservesVerb()
    {
        const string sql = "CREATE OR ALTER VIEW V (A, B) AS SELECT a, b FROM t";
        var rebuilt = RoundTrip(sql);
        Assert.StartsWith("CREATE OR ALTER VIEW", rebuilt);
        Assert.True(ViewSignatureParser.Parse(rebuilt).OrAlter);
        Assert.Equal(rebuilt, RoundTrip(rebuilt));
    }
}
