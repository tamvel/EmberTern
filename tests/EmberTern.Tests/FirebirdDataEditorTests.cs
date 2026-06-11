using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class FirebirdDataEditorTests
{
    [Fact]
    public void BuildUpdateSql_SinglePk_QuotesAndBindsCorrectly()
    {
        var sql = FirebirdDataEditor.BuildUpdateSql("ADRES", "MIEJSCOWOSC", new[] { "ID_ADRES" });
        Assert.Equal("UPDATE \"ADRES\" SET \"MIEJSCOWOSC\" = @newValue WHERE \"ID_ADRES\" = @pk0", sql);
    }

    [Fact]
    public void BuildUpdateSql_CompositePk_JoinsWithAnd()
    {
        var sql = FirebirdDataEditor.BuildUpdateSql("ORDER_ITEMS", "QTY", new[] { "ORDER_ID", "LINE_NO" });
        Assert.Equal(
            "UPDATE \"ORDER_ITEMS\" SET \"QTY\" = @newValue WHERE \"ORDER_ID\" = @pk0 AND \"LINE_NO\" = @pk1",
            sql);
    }

    [Fact]
    public void BuildUpdateSql_EscapesInternalQuotes()
    {
        var sql = FirebirdDataEditor.BuildUpdateSql("MY\"TABLE", "COL\"X", new[] { "PK\"" });
        Assert.Equal(
            "UPDATE \"MY\"\"TABLE\" SET \"COL\"\"X\" = @newValue WHERE \"PK\"\"\" = @pk0",
            sql);
    }

    [Fact]
    public void BuildInsertSql_SingleColumn_QuotesCorrectly()
    {
        var sql = FirebirdDataEditor.BuildInsertSql("ADRES", new[] { "NAZWA" });
        Assert.Equal("INSERT INTO \"ADRES\" (\"NAZWA\") VALUES (@v0)", sql);
    }

    [Fact]
    public void BuildInsertSql_MultipleColumns_PositionalParams()
    {
        var sql = FirebirdDataEditor.BuildInsertSql("ADRES", new[] { "ID", "NAZWA", "KOD" });
        Assert.Equal(
            "INSERT INTO \"ADRES\" (\"ID\", \"NAZWA\", \"KOD\") VALUES (@v0, @v1, @v2)",
            sql);
    }

    [Fact]
    public void BuildDeleteSql_SinglePk_BindsCorrectly()
    {
        var sql = FirebirdDataEditor.BuildDeleteSql("ADRES", new[] { "ID_ADRES" });
        Assert.Equal("DELETE FROM \"ADRES\" WHERE \"ID_ADRES\" = @pk0", sql);
    }

    [Fact]
    public void BuildDeleteSql_CompositePk_JoinsWithAnd()
    {
        var sql = FirebirdDataEditor.BuildDeleteSql("ORDER_ITEMS", new[] { "ORDER_ID", "LINE_NO" });
        Assert.Equal(
            "DELETE FROM \"ORDER_ITEMS\" WHERE \"ORDER_ID\" = @pk0 AND \"LINE_NO\" = @pk1",
            sql);
    }
}
