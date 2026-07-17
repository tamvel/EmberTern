using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

// E3 — statement assembly from a proven target. Every Firebird rule pinned here was measured against a
// live engine (OVERRIDING SYSTEM VALUE, computed-column rejection, the DSQL length limit), not inferred.
public class SqlStatementBuilderTests
{
    private static ResolvedColumn Col(
        int index, string name, SqlValueKind kind = SqlValueKind.Integer,
        bool computed = false, bool pk = false, IdentityKind identity = IdentityKind.None)
        => new(index, name, kind) { IsComputed = computed, IsPrimaryKey = pk, Identity = identity };

    private static TargetResolution.Resolved Target(string table, params ResolvedColumn[] columns)
        => new(table, columns, new KeyResolution.Unavailable(
            ExportUnavailableReason.Of(ExportUnavailableCode.NoPrimaryKey)));

    private static string Insert(TargetResolution.Resolved target, params object?[] row)
    {
        var r = SqlStatementBuilder.BuildInsert(target, row);
        Assert.True(r.IsBuilt, $"expected a statement, got {r.Reason?.Code}");
        return r.Sql!;
    }

    private static ExportUnavailableReason Refused(TargetResolution.Resolved target, params object?[] row)
    {
        var r = SqlStatementBuilder.BuildInsert(target, row);
        Assert.False(r.IsBuilt, $"expected a refusal, got {r.Sql}");
        Assert.Null(r.Sql);
        return r.Reason!;
    }

    // ── The shape ────────────────────────────────────────────────────────────
    [Fact]
    public void An_Insert_Names_The_Table_And_Its_Base_Columns()
        => Assert.Equal(
            "INSERT INTO CUSTOMERS (CUSTOMER_ID, NAME) VALUES (1, 'John');",
            Insert(Target("CUSTOMERS", Col(0, "CUSTOMER_ID"), Col(1, "NAME", SqlValueKind.Text)), 1, "John"));

    // The values come from the ROW ARRAY by ResultIndex, not by position in the column list — a derived
    // expression sitting between two real columns must not shift them.
    [Fact]
    public void Values_Are_Taken_By_Result_Index_Not_By_Column_Position()
        => Assert.Equal(
            "INSERT INTO CUSTOMERS (CUSTOMER_ID, NAME) VALUES (1, 'John');",
            Insert(
                Target("CUSTOMERS", Col(0, "CUSTOMER_ID"), Col(2, "NAME", SqlValueKind.Text)),
                1, "skipped-derived-column", "John"));

    [Fact]
    public void A_Null_Value_Is_The_Null_Literal()
        => Assert.EndsWith("VALUES (1, NULL);",
            Insert(Target("T", Col(0, "ID"), Col(1, "TXT", SqlValueKind.Text)), 1, DBNull.Value),
            StringComparison.Ordinal);

    // ── Computed columns (measured: Firebird rejects writing one) ────────────
    [Fact]
    public void A_Computed_Column_Is_Excluded_From_The_Insert()
    {
        var sql = Insert(
            Target("RECT", Col(0, "W"), Col(1, "H"), Col(2, "AREA", computed: true)), 4, 5, 20);

        Assert.Equal("INSERT INTO RECT (W, H) VALUES (4, 5);", sql);
        Assert.DoesNotContain("AREA", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Result_Of_Only_Computed_Columns_Has_Nothing_To_Insert()
        => Assert.Equal(ExportUnavailableCode.NoWritableColumns,
            Refused(Target("RECT", Col(0, "AREA", computed: true)), 20).Code);

    // ── Identity (measured on a live FB5 engine) ─────────────────────────────
    // GENERATED ALWAYS rejects a plain INSERT naming the column: "OVERRIDING clause should be used…".
    // The lab's own PRODUCTS.PRODUCT_ID is one, so the naive INSERT fails on the user's own database.
    [Fact]
    public void A_Generated_Always_Identity_Gets_Overriding_System_Value()
        => Assert.Equal(
            "INSERT INTO PRODUCTS (PRODUCT_ID, NAME) OVERRIDING SYSTEM VALUE VALUES (7, 'Widget');",
            Insert(
                Target("PRODUCTS",
                    Col(0, "PRODUCT_ID", pk: true, identity: IdentityKind.Always),
                    Col(1, "NAME", SqlValueKind.Text)),
                7, "Widget"));

    // …and BY DEFAULT must NOT get the clause — it accepts a plain INSERT, so adding it would be noise
    // at best. This is the distinction that did not exist in the codebase before E3.
    [Fact]
    public void A_Generated_By_Default_Identity_Gets_No_Overriding_Clause()
    {
        var sql = Insert(
            Target("T", Col(0, "ID", identity: IdentityKind.ByDefault), Col(1, "A")), 5, 1);

        Assert.Equal("INSERT INTO T (ID, A) VALUES (5, 1);", sql);
        Assert.DoesNotContain("OVERRIDING", sql, StringComparison.Ordinal);
    }

    // We emit the clause rather than DROPPING the identity column: preserving the actual key is the
    // whole point of copying a row (rule #11 — never lose information).
    [Fact]
    public void An_Always_Identity_Column_Is_Kept_Not_Dropped()
        => Assert.Contains("PRODUCT_ID",
            Insert(Target("PRODUCTS", Col(0, "PRODUCT_ID", identity: IdentityKind.Always)), 7),
            StringComparison.Ordinal);

    // ── Identifiers ──────────────────────────────────────────────────────────
    // These names come from the CATALOG and are exact, so they must ROUND-TRIP. A table created as
    // "MixedCase" has catalog name MixedCase; emitting it bare would fold to MIXEDCASE and target a
    // different object. This is why the builder uses DdlGenerator.QuoteLight and NOT PresentIdentifier
    // (which the design named): PresentIdentifier's uppercase-and-bare rule is §0-safe only for a
    // USER-TYPED name, which Firebird would fold anyway.
    [Fact]
    public void A_Case_Sensitive_Catalog_Identifier_Is_Quoted_Verbatim_Never_Uppercased()
    {
        var sql = Insert(Target("MixedCase", Col(0, "my col")), 1);

        Assert.Equal("INSERT INTO \"MixedCase\" (\"my col\") VALUES (1);", sql);
        Assert.DoesNotContain("MIXEDCASE", sql, StringComparison.Ordinal); // the bug this guards
    }

    // The ordinary case stays bare and unquoted — the house style, and what makes generated SQL readable.
    [Fact]
    public void An_Ordinary_Upper_Case_Identifier_Stays_Bare()
        => Assert.Equal("INSERT INTO CUSTOMERS (CUSTOMER_ID) VALUES (1);",
            Insert(Target("CUSTOMERS", Col(0, "CUSTOMER_ID")), 1));

    // A lowercase catalog name is genuinely case-sensitive too — Firebird only produces one via a
    // quoted DDL identifier, so emitting it bare would fold it to upper and miss.
    [Fact]
    public void A_Lower_Case_Catalog_Identifier_Is_Quoted()
        => Assert.Equal("INSERT INTO \"lower\" (\"col\") VALUES (1);",
            Insert(Target("lower", Col(0, "col")), 1));

    // ── Value refusals propagate as a refusal, never a broken statement ──────
    [Fact]
    public void An_Unrenderable_Value_Refuses_The_Statement_And_Names_The_Column()
    {
        // A subnormal double: the engine ACCEPTS the literal and silently returns 0.
        var reason = Refused(Target("T", Col(0, "D", SqlValueKind.Float)), double.Epsilon);
        Assert.Equal(ExportUnavailableCode.ValueNotRenderable, reason.Code);
        Assert.Equal(new[] { "D" }, reason.Names);
    }

    [Fact]
    public void An_Unmapped_Declared_Type_Refuses_Only_When_The_Value_Is_Not_Null()
    {
        var target = Target("T", Col(0, "ARR", SqlValueKind.Unknown));

        // Row-dependent, and correctly so: NULL is faithful for any type.
        Assert.Equal("INSERT INTO T (ARR) VALUES (NULL);", Insert(target, DBNull.Value));
        Assert.Equal(ExportUnavailableCode.ValueNotRenderable, Refused(target, "something").Code);
    }

    [Fact]
    public void An_Oversized_Blob_Refuses_With_TooLarge()
        => Assert.Equal(ExportUnavailableCode.ValueTooLarge,
            Refused(Target("T", Col(0, "B", SqlValueKind.BinaryBlob)), new byte[40_000]).Code);

    // ── The statement budget (measured: ~65,535-char DSQL limit) ─────────────
    // THE reason a per-value ceiling is not sufficient: each blob passes its own check, and together
    // they overflow one statement. Only the assembled length can see this.
    [Fact]
    public void Several_Individually_Legal_Values_Can_Still_Overflow_One_Statement()
    {
        var target = Target("T",
            Col(0, "B1", SqlValueKind.BinaryBlob),
            Col(1, "B2", SqlValueKind.BinaryBlob),
            Col(2, "B3", SqlValueKind.BinaryBlob));

        var blob = new byte[20_000]; // 20 KB each — every one is under MaxBinaryBytes on its own
        Assert.True(SqlLiteralWriter.Write(blob, SqlValueKind.BinaryBlob).IsWritten);

        var reason = Refused(target, blob, blob, blob); // 3 × 40,000 hex chars ≫ 65,535
        Assert.Equal(ExportUnavailableCode.StatementTooLong, reason.Code);
    }

    [Fact]
    public void The_Statement_Limit_Is_The_Measured_Dsql_Limit()
        => Assert.Equal(65535, SqlStatementBuilder.MaxStatementLength);
}
