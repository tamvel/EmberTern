using System;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I4: the pure parts of the Firebird writer, pinned without a server.
/// <para>
/// Both things tested here decide whether a run works at all, and both fail in ways that look like something
/// else: a missing <c>OVERRIDING SYSTEM VALUE</c> fails on the very first row with a message about a clause the
/// user never wrote, and an inverted <c>MultiError</c> silently changes what the chosen error policy DOES. The
/// live probe proves them against the engine; these prove them cheaply on every build.
/// </para>
/// </summary>
public class ImportFirebirdWriterTests
{
    private static ColumnSpec Col(string name, IdentityKind identity = IdentityKind.None)
        => new(name, "INTEGER", null, false) { Identity = identity };

    private static ImportTarget Target(params ColumnSpec[] columns)
        => new("ORDERS", columns, Array.Empty<string>());

    private static ColumnMapping Map(string column, int index)
        => new() { TargetColumnName = column, SourceFieldName = column, SourceFieldIndex = index };

    // ── The INSERT ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildInsertSql_NamesOnlyTheMappedColumns_InOrder()
    {
        var sql = FirebirdImportWriter.BuildInsertSql(
            Target(Col("A"), Col("B"), Col("C")),
            new[] { Map("A", 0), Map("C", 1) });

        Assert.Equal(@"INSERT INTO ""ORDERS"" (""A"", ""C"") VALUES (@v0, @v1)", sql);
    }

    /// <summary>
    /// ⭐ Firebird REJECTS an INSERT naming a <c>GENERATED ALWAYS</c> identity column without this clause, so
    /// its absence is not a cosmetic gap — it is a run that dies on row 1 with a message about syntax the user
    /// never wrote (design R10). The fact comes from <see cref="ColumnSpec.Identity"/>, which is precisely why
    /// that enum keeps ALWAYS and BY DEFAULT apart instead of collapsing them into a bool.
    /// </summary>
    [Fact]
    public void BuildInsertSql_EmitsOverridingSystemValue_ForAGeneratedAlwaysIdentity()
    {
        var sql = FirebirdImportWriter.BuildInsertSql(
            Target(Col("ID", IdentityKind.Always), Col("B")),
            new[] { Map("ID", 0), Map("B", 1) });

        Assert.Equal(
            @"INSERT INTO ""ORDERS"" (""ID"", ""B"") OVERRIDING SYSTEM VALUE VALUES (@v0, @v1)", sql);
    }

    /// <summary>A BY DEFAULT identity may be named freely — emitting the clause there would be as wrong as
    /// omitting it above.</summary>
    [Fact]
    public void BuildInsertSql_DoesNotEmitTheClause_ForAByDefaultIdentity()
    {
        var sql = FirebirdImportWriter.BuildInsertSql(
            Target(Col("ID", IdentityKind.ByDefault)), new[] { Map("ID", 0) });

        Assert.DoesNotContain("OVERRIDING", sql, StringComparison.Ordinal);
    }

    /// <summary>An ALWAYS identity that is NOT mapped needs nothing: the column is not in the field list, so
    /// the engine generates it.</summary>
    [Fact]
    public void BuildInsertSql_DoesNotEmitTheClause_WhenTheIdentityIsNotMapped()
    {
        var sql = FirebirdImportWriter.BuildInsertSql(
            Target(Col("ID", IdentityKind.Always), Col("B")), new[] { Map("B", 0) });

        Assert.DoesNotContain("OVERRIDING", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInsertSql_QuotesIdentifiers_AndDoublesAnInternalQuote()
    {
        var target = new ImportTarget(@"ODD""NAME", new[] { Col(@"A""B") }, Array.Empty<string>());
        var sql = FirebirdImportWriter.BuildInsertSql(target, new[] { Map(@"A""B", 0) });

        Assert.Equal(@"INSERT INTO ""ODD""""NAME"" (""A""""B"") VALUES (@v0)", sql);
    }

    // ── The error policy → driver flag ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Measured in I0 (§2.3), and pinned here because getting it backwards is invisible until it matters:
    /// <c>StopOnFirstError</c> would sail past every bad row, and <c>SkipInvalidRows</c> would stop dead on the
    /// first one. The policy the user chose is enforced by the server round trip rather than re-implemented in
    /// a client-side loop.
    /// </summary>
    [Theory]
    [InlineData(ImportErrorPolicy.StopOnFirstError, false)]
    [InlineData(ImportErrorPolicy.SkipInvalidRows, true)]
    public void MultiError_MapsOneToOneOntoTheErrorPolicy(ImportErrorPolicy policy, bool expected)
        => Assert.Equal(expected, FirebirdImportWriter.MultiErrorFor(policy));
}
