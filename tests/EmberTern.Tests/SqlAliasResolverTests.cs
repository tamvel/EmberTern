using System.Collections.Generic;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

public class SqlAliasResolverTests
{
    [Fact]
    public void Empty_ReturnsEmptyMap()
    {
        var map = SqlAliasResolver.ParseAliases(string.Empty);
        Assert.Empty(map);
    }

    [Fact]
    public void From_TableOnly_MapsTableToItself()
    {
        var map = SqlAliasResolver.ParseAliases("SELECT * FROM NAGL");
        Assert.True(map.ContainsKey("NAGL"));
        Assert.Equal("NAGL", map["NAGL"]);
    }

    [Fact]
    public void From_TableWithAlias_NoAsKeyword()
    {
        var map = SqlAliasResolver.ParseAliases("SELECT * FROM NAGL N");
        Assert.Equal("NAGL", map["N"]);
        // Without an alias on the underlying table, the table-name key should
        // NOT also be populated when an alias is present (otherwise typing
        // "NAGL." after aliasing it as N would surface stale columns).
        Assert.False(map.ContainsKey("NAGL"));
    }

    [Fact]
    public void From_TableWithAsAlias()
    {
        var map = SqlAliasResolver.ParseAliases("SELECT * FROM NAGL AS N");
        Assert.Equal("NAGL", map["N"]);
    }

    [Fact]
    public void From_CommaSeparatedTables_WithMixedAliases()
    {
        var map = SqlAliasResolver.ParseAliases(
            "SELECT * FROM NAGL N, POZYCJE P, KONTRAHENCI");
        Assert.Equal("NAGL", map["N"]);
        Assert.Equal("POZYCJE", map["P"]);
        Assert.Equal("KONTRAHENCI", map["KONTRAHENCI"]);
    }

    [Fact]
    public void Join_AliasResolvesToTable()
    {
        var map = SqlAliasResolver.ParseAliases(
            "SELECT * FROM NAGL N JOIN POZYCJE P ON P.ID_NAGL = N.ID");
        Assert.Equal("NAGL", map["N"]);
        Assert.Equal("POZYCJE", map["P"]);
    }

    [Fact]
    public void LeftOuterJoin_HandledLikePlainJoin()
    {
        var map = SqlAliasResolver.ParseAliases(
            "SELECT * FROM NAGL N LEFT OUTER JOIN POZYCJE P ON P.X = N.Y");
        Assert.Equal("NAGL", map["N"]);
        Assert.Equal("POZYCJE", map["P"]);
    }

    [Fact]
    public void CaseInsensitive_LookupReturnsUppercaseTable()
    {
        var map = SqlAliasResolver.ParseAliases("select * from nagl n");
        // Aliases are uppercased internally; lookup is case-insensitive.
        Assert.True(map.ContainsKey("N"));
        Assert.True(map.ContainsKey("n"));
        Assert.Equal("NAGL", map["N"]);
    }

    [Fact]
    public void Subquery_ScopedAliasesAreIgnored()
    {
        var map = SqlAliasResolver.ParseAliases(
            "SELECT * FROM NAGL N WHERE N.ID IN (SELECT P.ID_NAGL FROM POZYCJE P)");
        // Outer scope alias is recorded.
        Assert.Equal("NAGL", map["N"]);
        // Subquery alias is NOT in the outer scope.
        Assert.False(map.ContainsKey("P"));
    }

    [Fact]
    public void StringLiterals_AreSkipped()
    {
        // The "FROM" inside the literal must not start a table-list parse.
        var map = SqlAliasResolver.ParseAliases(
            "SELECT 'FROM x AS y' FROM NAGL N");
        Assert.Single(map);
        Assert.Equal("NAGL", map["N"]);
    }

    [Fact]
    public void LineAndBlockComments_AreSkipped()
    {
        var map = SqlAliasResolver.ParseAliases(@"
            -- FROM commented OUT
            SELECT * /* FROM also commented */ FROM NAGL N");
        Assert.Equal("NAGL", map["N"]);
    }

    [Fact]
    public void Update_ParsesTableAlias()
    {
        var map = SqlAliasResolver.ParseAliases("UPDATE NAGL N SET N.X = 1");
        Assert.Equal("NAGL", map["N"]);
    }

    [Fact]
    public void QuotedIdentifier_PreservedAsIs()
    {
        var map = SqlAliasResolver.ParseAliases(
            "SELECT * FROM \"My Table\" \"My Alias\"");
        // Quoted identifiers keep their literal case.
        Assert.Equal("My Table", map["My Alias"]);
    }

    [Fact]
    public void TerminatorKeyword_DoesNotBecomeAlias()
    {
        // "WHERE" follows the table directly — must NOT be treated as the alias.
        var map = SqlAliasResolver.ParseAliases("SELECT * FROM NAGL WHERE X = 1");
        Assert.Equal("NAGL", map["NAGL"]);
        Assert.False(map.ContainsKey("WHERE"));
    }

    [Fact]
    public void JoinTerminator_DoesNotBecomeAlias()
    {
        var map = SqlAliasResolver.ParseAliases(
            "SELECT * FROM NAGL JOIN POZYCJE ON NAGL.ID = POZYCJE.ID_NAGL");
        Assert.Equal("NAGL", map["NAGL"]);
        Assert.Equal("POZYCJE", map["POZYCJE"]);
    }

    // -- ResolveTableForQualifier (composes ParseAliases + table-name lookup) -

    [Fact]
    public void Resolve_AliasMapsToKnownTable()
    {
        var known = new[] { "NAGL", "POZYCJE" };
        var t = SqlAliasResolver.ResolveTableForQualifier(
            "SELECT * FROM NAGL N JOIN POZYCJE P ON P.X = N.Y",
            "N", known);
        Assert.Equal("NAGL", t);
    }

    [Fact]
    public void Resolve_DirectTableNameQualifier()
    {
        var known = new[] { "NAGL", "POZYCJE" };
        // No alias used — typing "NAGL." should resolve directly.
        var t = SqlAliasResolver.ResolveTableForQualifier(
            "SELECT * FROM NAGL",
            "NAGL", known);
        Assert.Equal("NAGL", t);
    }

    [Fact]
    public void Resolve_UnknownAlias_ReturnsNull()
    {
        var known = new[] { "NAGL", "POZYCJE" };
        var t = SqlAliasResolver.ResolveTableForQualifier(
            "SELECT * FROM NAGL N",
            "X", known);
        Assert.Null(t);
    }

    [Fact]
    public void Resolve_AliasToTableNotInSchema_ReturnsNull()
    {
        // Alias resolves to a table name the schema doesn't have — we don't
        // fabricate a column fetch against a nonsense table.
        var known = new[] { "NAGL" };
        var t = SqlAliasResolver.ResolveTableForQualifier(
            "SELECT * FROM GHOSTS G", "G", known);
        Assert.Null(t);
    }

    [Fact]
    public void Resolve_AliasShadowsTableName()
    {
        // POZYCJE is also a known table, but here it's used as alias for NAGL.
        // The alias map wins because the qualifier matches an alias entry.
        var known = new[] { "NAGL", "POZYCJE" };
        var t = SqlAliasResolver.ResolveTableForQualifier(
            "SELECT * FROM NAGL POZYCJE",
            "POZYCJE", known);
        // Direct table-name match wins (POZYCJE is itself a table). Documents
        // the chosen tie-break: typing "POZYCJE." against an aliased reference
        // surfaces POZYCJE's columns rather than NAGL's. Less surprising in the
        // common case (PK/FK schemas where aliasing as an existing table name
        // is rare).
        Assert.Equal("POZYCJE", t);
    }

    [Fact]
    public void Resolve_CaseInsensitive()
    {
        var known = new[] { "NAGL" };
        Assert.Equal("NAGL",
            SqlAliasResolver.ResolveTableForQualifier(
                "select * from nagl n", "n", known));
    }

    // -- ResolveTableForQualifier (pre-computed alias map — the cached editor path) --

    [Fact]
    public void ResolveMap_AliasMapsToKnownTable()
    {
        var aliases = SqlAliasResolver.ParseAliases(
            "SELECT * FROM NAGL N JOIN POZYCJE P ON P.X = N.Y");
        var known = new[] { "NAGL", "POZYCJE" };
        Assert.Equal("NAGL", SqlAliasResolver.ResolveTableForQualifier(aliases, "N", known));
        Assert.Equal("POZYCJE", SqlAliasResolver.ResolveTableForQualifier(aliases, "P", known));
    }

    [Fact]
    public void ResolveMap_DirectTableNameQualifier_NeedsNoAliasEntry()
    {
        // A fully-qualified TABLE.column resolves even against an empty map.
        var empty = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        var known = new[] { "NAGL", "POZYCJE" };
        Assert.Equal("NAGL", SqlAliasResolver.ResolveTableForQualifier(empty, "NAGL", known));
    }

    [Fact]
    public void ResolveMap_UnknownQualifier_ReturnsNull()
    {
        var aliases = SqlAliasResolver.ParseAliases("SELECT * FROM NAGL N");
        var known = new[] { "NAGL" };
        Assert.Null(SqlAliasResolver.ResolveTableForQualifier(aliases, "X", known));
    }

    [Fact]
    public void ResolveMap_AliasToTableNotInSchema_ReturnsNull()
    {
        var aliases = SqlAliasResolver.ParseAliases("SELECT * FROM GHOSTS G");
        var known = new[] { "NAGL" };
        Assert.Null(SqlAliasResolver.ResolveTableForQualifier(aliases, "G", known));
    }

    [Fact]
    public void ResolveMap_MatchesSqlOverload()
    {
        // The (sql,…) overload must be exactly the map overload fed ParseAliases(sql).
        const string sql = "SELECT * FROM NAGL N JOIN POZYCJE P ON P.X = N.Y";
        var known = new[] { "NAGL", "POZYCJE" };
        var viaSql = SqlAliasResolver.ResolveTableForQualifier(sql, "P", known);
        var viaMap = SqlAliasResolver.ResolveTableForQualifier(
            SqlAliasResolver.ParseAliases(sql), "P", known);
        Assert.Equal(viaSql, viaMap);
    }
}
