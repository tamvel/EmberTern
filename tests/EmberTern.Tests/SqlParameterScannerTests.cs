using System.Linq;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the Smart-Parameters scanner — extraction is lexer-based (no regex): literals,
/// comments and quoted identifiers are skipped; <c>::</c> is the cast operator.</summary>
public class SqlParameterScannerTests
{
    [Fact]
    public void Scan_ExtractsColonAndAtNames_InOrder()
    {
        var p = SqlParameterScanner.Scan("select * from t where a = :id and b = @code");
        Assert.Equal(new[] { "id", "code" }, p.Select(x => x.Name).ToArray());
        Assert.Equal(':', p[0].Marker);
        Assert.Equal('@', p[1].Marker);
    }

    [Fact]
    public void Scan_SkipsLiteralsCommentsQuotedIdents_AndCast()
    {
        const string sql =
            "select cast(x as integer), ':notparam', \"col:name\" -- :nocomment\n" +
            "/* @noblock */ from t where a = :real and n = x::int";
        var names = SqlParameterScanner.Scan(sql).Select(x => x.Name).ToArray();
        Assert.Equal(new[] { "real" }, names); // literal / comment / quoted-ident / :: all skipped
    }

    [Fact]
    public void Scan_LoneMarkersAndDigits_AreNotParameters()
    {
        // ':' before a space, '@' before a digit, and a bare '::' are not parameters.
        Assert.Empty(SqlParameterScanner.Scan("select a : b, @1, x::y from t"));
    }

    [Fact]
    public void Scan_ExecuteProcedureColonNames()
    {
        var names = SqlParameterScanner.Scan("execute procedure xxx_test(:id_kontrahent, :id_magazyn, :tryb)")
            .Select(x => x.Name).ToArray();
        Assert.Equal(new[] { "id_kontrahent", "id_magazyn", "tryb" }, names);
    }

    [Fact]
    public void RewriteToDriverMarkers_ColonToAt_AndNormalizesCase()
    {
        var (sql, names) = SqlParameterScanner.RewriteToDriverMarkers(
            "select * from t where a = :Id and b = :ID and c = @other");

        // :Id and :ID collapse to one param (first spelling wins); marker becomes @.
        Assert.Equal(new[] { "Id", "other" }, names.ToArray());
        Assert.Contains("a = @Id", sql);
        Assert.Contains("b = @Id", sql); // :ID normalized to the first occurrence @Id
        Assert.Contains("c = @other", sql);
    }

    [Fact]
    public void RewriteToDriverMarkers_LiteralWithColon_Untouched()
    {
        var (sql, names) = SqlParameterScanner.RewriteToDriverMarkers("update t set note = ':keep' where id = :id");
        Assert.Equal(new[] { "id" }, names.ToArray());
        Assert.Contains("':keep'", sql);          // literal left alone
        Assert.Contains("id = @id", sql);
    }

    [Fact]
    public void RewriteToDriverMarkers_NoParams_ReturnsSqlUnchanged()
    {
        const string sql = "select 1 from rdb$database";
        var (rewritten, names) = SqlParameterScanner.RewriteToDriverMarkers(sql);
        Assert.Equal(sql, rewritten);
        Assert.Empty(names);
    }

    [Theory]
    [InlineData("execute block returns (x integer) as begin x = :local; suspend; end", true)]
    [InlineData("  EXECUTE  BLOCK (a int = ?) as begin end", true)]
    [InlineData("execute procedure p(:a)", false)]
    [InlineData("select :a from t", false)]
    public void IsExecuteBlock_DetectsBlock(string sql, bool expected)
        => Assert.Equal(expected, SqlParameterScanner.IsExecuteBlock(sql));

    [Theory]
    [InlineData("execute procedure xxx_test(:a, :b)", "XXX_TEST")]
    [InlineData("EXECUTE PROCEDURE Recalc", "RECALC")]
    [InlineData("select * from t", null)]
    [InlineData("execute block as begin end", null)]
    public void TryExtractExecuteProcedureName(string sql, string? expected)
        => Assert.Equal(expected, SqlParameterScanner.TryExtractExecuteProcedureName(sql));
}
