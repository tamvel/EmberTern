using System;
using System.Linq;
using EmberTern.Core.Export;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

// E4 — UPDATE generation. The design's §6.6 decision table, made executable. The rule under test is one
// sentence: the WHERE clause is built from a key VERIFIED COMPLETE against the catalog, or the statement
// is not offered. Everything here is a way of failing that rule.
public class SqlUpdateBuilderTests
{
    private static ResolvedColumn Col(
        int index, string name, SqlValueKind kind = SqlValueKind.Integer,
        bool computed = false, bool pk = false, IdentityKind identity = IdentityKind.None)
        => new(index, name, kind) { IsComputed = computed, IsPrimaryKey = pk, Identity = identity };

    private static TargetResolution.Resolved WithKey(string table, params ResolvedColumn[] columns)
        => new(table, columns, new KeyResolution.Verified(columns.Where(c => c.IsPrimaryKey).ToArray()));

    private static TargetResolution.Resolved WithoutKey(
        string table, ExportUnavailableCode code, string[] names, params ResolvedColumn[] columns)
        => new(table, columns, new KeyResolution.Unavailable(ExportUnavailableReason.Of(code, names)));

    private static string Update(TargetResolution.Resolved target, params object?[] row)
    {
        var r = SqlStatementBuilder.BuildUpdate(target, row);
        Assert.True(r.IsBuilt, $"expected a statement, got {r.Reason?.Code}");
        return r.Sql!;
    }

    private static ExportUnavailableReason Refused(TargetResolution.Resolved target, params object?[] row)
    {
        var r = SqlStatementBuilder.BuildUpdate(target, row);
        Assert.False(r.IsBuilt, $"expected a refusal, got {r.Sql}");
        return r.Reason!;
    }

    // ── The happy path ───────────────────────────────────────────────────────
    [Fact]
    public void An_Update_Sets_The_Non_Key_Columns_And_Matches_On_The_Key()
        => Assert.Equal(
            "UPDATE CUSTOMERS SET NAME = 'John', CITY = 'London' WHERE CUSTOMER_ID = 1;",
            Update(
                WithKey("CUSTOMERS",
                    Col(0, "CUSTOMER_ID", pk: true),
                    Col(1, "NAME", SqlValueKind.Text),
                    Col(2, "CITY", SqlValueKind.Text)),
                1, "John", "London"));

    // A composite key becomes a conjunction — every column of it, which is the whole point.
    [Fact]
    public void A_Composite_Key_Matches_On_Every_One_Of_Its_Columns()
        => Assert.Equal(
            "UPDATE ORDER_ITEMS SET QTY = 5 WHERE ORDER_ID = 1 AND LINE_NO = 2;",
            Update(
                WithKey("ORDER_ITEMS",
                    Col(0, "ORDER_ID", pk: true),
                    Col(1, "LINE_NO", pk: true),
                    Col(2, "QTY")),
                1, 2, 5));

    // The key column is matched on, never assigned — assigning what you match on is noise, and for an
    // identity column Firebird may reject it outright.
    [Fact]
    public void A_Key_Column_Is_Not_In_The_Set_List()
    {
        var sql = Update(
            WithKey("CUSTOMERS", Col(0, "CUSTOMER_ID", pk: true), Col(1, "NAME", SqlValueKind.Text)), 1, "John");

        Assert.DoesNotContain("SET CUSTOMER_ID", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE CUSTOMER_ID = 1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Computed_Column_Is_Never_Set()
    {
        var sql = Update(
            WithKey("RECT", Col(0, "ID", pk: true), Col(1, "W"), Col(2, "AREA", computed: true)), 1, 4, 20);

        Assert.Equal("UPDATE RECT SET W = 4 WHERE ID = 1;", sql);
        Assert.DoesNotContain("AREA", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Null_Is_Set_With_Equals_Null_Which_Is_Correct_In_A_Set_Clause()
        => Assert.Equal("UPDATE T SET TXT = NULL WHERE ID = 1;",
            Update(WithKey("T", Col(0, "ID", pk: true), Col(1, "TXT", SqlValueKind.Text)), 1, DBNull.Value));

    // ── §6.6's refusals ──────────────────────────────────────────────────────
    // The headline: a partial composite PK. `WHERE ORDER_ID = 1` on a PK of (ORDER_ID, LINE_NO) matches
    // every line of the order — measured as 2 rows on the lab's own shape — and SUCCEEDS while doing it.
    [Fact]
    public void A_Partial_Primary_Key_Refuses_And_Names_The_Missing_Column()
    {
        var reason = Refused(
            WithoutKey("ORDER_ITEMS", ExportUnavailableCode.IncompletePrimaryKey, new[] { "LINE_NO" },
                Col(0, "ORDER_ID", pk: true), Col(1, "QTY")),
            1, 10);

        Assert.Equal(ExportUnavailableCode.IncompletePrimaryKey, reason.Code);
        Assert.Equal(new[] { "LINE_NO" }, reason.Names); // points at the fix, rather than a generic no
    }

    [Fact]
    public void A_Table_With_No_Primary_Key_Refuses()
        => Assert.Equal(ExportUnavailableCode.NoPrimaryKey,
            Refused(
                WithoutKey("LOG_ENTRY", ExportUnavailableCode.NoPrimaryKey, Array.Empty<string>(),
                    Col(0, "TXT", SqlValueKind.Text)),
                "hello").Code);

    // A result of key columns only has nothing to SET — `UPDATE T SET  WHERE ID = 1` is not a statement.
    [Fact]
    public void A_Result_Of_Only_Key_Columns_Has_Nothing_To_Set()
        => Assert.Equal(ExportUnavailableCode.NoWritableColumns,
            Refused(WithKey("T", Col(0, "ID", pk: true)), 1).Code);

    [Fact]
    public void A_Result_Whose_Only_Non_Key_Column_Is_Computed_Has_Nothing_To_Set()
        => Assert.Equal(ExportUnavailableCode.NoWritableColumns,
            Refused(WithKey("RECT", Col(0, "ID", pk: true), Col(1, "AREA", computed: true)), 1, 20).Code);

    // A PK is NOT NULL by definition, so this cannot fire for one — asserted anyway, because `= NULL`
    // is never true and the check becomes load-bearing the moment UNIQUE keys are allowed (where NULLs
    // are legal and several rows can share them).
    [Fact]
    public void A_Null_Key_Value_Refuses_Rather_Than_Emitting_Equals_Null()
    {
        var reason = Refused(
            WithKey("T", Col(0, "ID", pk: true), Col(1, "TXT", SqlValueKind.Text)), DBNull.Value, "x");

        Assert.Equal(ExportUnavailableCode.KeyValueIsNull, reason.Code);
        Assert.Equal(new[] { "ID" }, reason.Names);
    }

    [Fact]
    public void An_Unrenderable_Set_Value_Refuses_The_Whole_Statement()
        => Assert.Equal(ExportUnavailableCode.ValueNotRenderable,
            Refused(WithKey("T", Col(0, "ID", pk: true), Col(1, "D", SqlValueKind.Float)), 1, double.Epsilon).Code);

    // ── Availability gating (what the menu shows) ────────────────────────────
    [Fact]
    public void Insert_Is_Available_Where_Update_Is_Not()
    {
        // The single most common shape: a real table, but the key was not selected.
        var resolution = WithoutKey("CUSTOMERS", ExportUnavailableCode.IncompletePrimaryKey, new[] { "CUSTOMER_ID" },
            Col(0, "NAME", SqlValueKind.Text));

        Assert.True(SqlFormatAvailability.ForInsert(resolution).IsAvailable);

        var update = SqlFormatAvailability.ForUpdate(resolution);
        Assert.False(update.IsAvailable);
        Assert.Equal(ExportUnavailableCode.IncompletePrimaryKey, update.Reason!.Code);
    }

    [Fact]
    public void An_Unresolved_Result_Refuses_Both_Formats_With_The_Same_Reason()
    {
        var unresolved = new TargetResolution.Unavailable(
            ExportUnavailableReason.Of(ExportUnavailableCode.SetOperation));

        Assert.Equal(ExportUnavailableCode.SetOperation, SqlFormatAvailability.ForInsert(unresolved).Reason!.Code);
        Assert.Equal(ExportUnavailableCode.SetOperation, SqlFormatAvailability.ForUpdate(unresolved).Reason!.Code);
    }

    [Fact]
    public void A_Fully_Resolved_Result_Offers_Both()
    {
        var resolution = WithKey("CUSTOMERS", Col(0, "CUSTOMER_ID", pk: true), Col(1, "NAME", SqlValueKind.Text));

        Assert.True(SqlFormatAvailability.ForInsert(resolution).IsAvailable);
        Assert.True(SqlFormatAvailability.ForUpdate(resolution).IsAvailable);
    }

    // Availability and generation must agree: anything ForUpdate calls available must actually build.
    // Otherwise the menu offers an action that then fails, which is worse than a greyed item.
    [Fact]
    public void Whatever_Availability_Offers_The_Builder_Can_Actually_Produce()
    {
        var resolution = WithKey("ORDER_ITEMS",
            Col(0, "ORDER_ID", pk: true), Col(1, "LINE_NO", pk: true), Col(2, "QTY"));

        Assert.True(SqlFormatAvailability.ForUpdate(resolution).IsAvailable);
        Assert.True(SqlStatementBuilder.BuildUpdate(resolution, new object?[] { 1, 2, 5 }).IsBuilt);

        Assert.True(SqlFormatAvailability.ForInsert(resolution).IsAvailable);
        Assert.True(SqlStatementBuilder.BuildInsert(resolution, new object?[] { 1, 2, 5 }).IsBuilt);
    }
}
