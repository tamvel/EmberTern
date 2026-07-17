using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

// E2 (SQL Data Export) — the provenance verdict. Every §1.2/§1.3 trap measured against the live engine
// gets a test here, because each one is a shape where the naive implementation SUCCEEDS against the
// wrong rows. That asymmetry is the milestone: a malformed statement fails loudly and harmlessly; a
// statement built from a partial key or a UNION's first leg does not fail at all.
public class ResultOriginResolverTests
{
    // ── fixtures ─────────────────────────────────────────────────────────────
    private sealed class FakeCatalog : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);

        public FakeCatalog Object(string name, SymbolKind kind)
        {
            _objects[name] = new ObjectMetadata(name, kind);
            return this;
        }

        public FakeCatalog Col(string table, string name, bool pk = false, bool computed = false)
        {
            if (!_objects.ContainsKey(table)) Object(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(new ColumnMetadata(name, "INTEGER") { IsPrimaryKey = pk, IsComputed = computed });
            return this;
        }

        public ObjectMetadata? FindObject(string name)
            => _objects.TryGetValue(name, out var o) ? o : null;

        public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
            => _cols.TryGetValue(tableOrView, out var c) ? c : Array.Empty<ColumnMetadata>();

        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
            => Array.Empty<RoutineParameterMetadata>();

        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToArray();
    }

    // The lab's shapes: CUSTOMERS (single-column PK) and ORDER_ITEMS (composite PK) — the two that
    // matter for §6.
    private static FakeCatalog Lab() => new FakeCatalog()
        .Col("CUSTOMERS", "CUSTOMER_ID", pk: true)
        .Col("CUSTOMERS", "NAME")
        .Col("CUSTOMERS", "CITY")
        .Col("ORDER_ITEMS", "ORDER_ID", pk: true)
        .Col("ORDER_ITEMS", "LINE_NO", pk: true)
        .Col("ORDER_ITEMS", "QTY")
        .Col("PRODUCTS", "PRODUCT_ID", pk: true)
        .Col("PRODUCTS", "NAME")
        .Col("LOG_ENTRY", "TXT")                       // a table with no PK at all
        .Col("RECT", "W").Col("RECT", "H").Col("RECT", "AREA", computed: true)
        .Object("SP_CUSTOMER_ORDERS", SymbolKind.Procedure)
        .Object("V_ORDER_DETAILS", SymbolKind.View);

    private static ColumnOrigin From(string table, string column, SqlValueKind kind = SqlValueKind.Integer)
        => new(table, column, IsComputed: false, kind);

    private static ColumnOrigin Derived(string operatorName = "MULTIPLY")
        => new(null, operatorName, IsComputed: false, SqlValueKind.Integer);

    private static StatementShape SingleTable => new()
    {
        IsUnderstood = true,
        FromItemCount = 1,
    };

    private static ResultOrigin Origin(StatementShape shape, params ColumnOrigin[] columns)
        => new(columns, new OriginShape.Statement(shape));

    private static TargetResolution.Resolved Resolved(ResultOrigin origin, ISqlMetadataProvider? catalog = null)
    {
        var r = ResultOriginResolver.Resolve(origin, catalog ?? Lab());
        return Assert.IsType<TargetResolution.Resolved>(r);
    }

    private static ExportUnavailableReason Refused(ResultOrigin origin, ISqlMetadataProvider? catalog = null)
    {
        var r = ResultOriginResolver.Resolve(origin, catalog ?? Lab());
        return Assert.IsType<TargetResolution.Unavailable>(r).Reason;
    }

    // ── The happy path ───────────────────────────────────────────────────────
    [Fact]
    public void A_Single_Table_Result_With_A_Complete_Pk_Resolves()
    {
        var r = Resolved(Origin(SingleTable, From("CUSTOMERS", "CUSTOMER_ID"), From("CUSTOMERS", "NAME")));

        Assert.Equal("CUSTOMERS", r.Table);
        Assert.Equal(new[] { "CUSTOMER_ID", "NAME" }, r.Columns.Select(c => c.BaseColumn));
        var key = Assert.IsType<KeyResolution.Verified>(r.PrimaryKey);
        Assert.Equal(new[] { "CUSTOMER_ID" }, key.Columns.Select(c => c.BaseColumn));
    }

    // The grid header is display only: `select NAME as CUSTOMER_NAME` must generate (NAME), never
    // (CUSTOMER_NAME) — the alias exists on no table. This is why generation reads BaseColumn.
    [Fact]
    public void An_Aliased_Column_Resolves_To_Its_Base_Column_Not_Its_Header()
    {
        var r = Resolved(Origin(SingleTable, From("CUSTOMERS", "NAME")));
        Assert.Equal("NAME", Assert.Single(r.Columns).BaseColumn);
    }

    // ── §1.2 — the participation-flag trap ───────────────────────────────────
    // `select ORDER_ID, QTY from ORDER_ITEMS`: the driver reports ORDER_ID IsKey=True, but the PK is
    // (ORDER_ID, LINE_NO). A WHERE built from it hits every line of the order — and succeeds.
    [Fact]
    public void A_Partially_Projected_Composite_Pk_Is_Refused_Never_Narrowed()
    {
        var r = Resolved(Origin(SingleTable, From("ORDER_ITEMS", "ORDER_ID"), From("ORDER_ITEMS", "QTY")));

        Assert.Equal("ORDER_ITEMS", r.Table); // INSERT is still fine — only the KEY is unavailable
        var key = Assert.IsType<KeyResolution.Unavailable>(r.PrimaryKey);
        Assert.Equal(ExportUnavailableCode.IncompletePrimaryKey, key.Reason.Code);
        Assert.Equal(new[] { "LINE_NO" }, key.Reason.Names); // names the actual obstacle
    }

    [Fact]
    public void A_Fully_Projected_Composite_Pk_Verifies_Every_Column()
    {
        var r = Resolved(Origin(SingleTable,
            From("ORDER_ITEMS", "ORDER_ID"), From("ORDER_ITEMS", "LINE_NO"), From("ORDER_ITEMS", "QTY")));

        var key = Assert.IsType<KeyResolution.Verified>(r.PrimaryKey);
        Assert.Equal(new[] { "ORDER_ID", "LINE_NO" }, key.Columns.Select(c => c.BaseColumn));
    }

    [Fact]
    public void A_Table_With_No_Primary_Key_Has_No_Verified_Key()
    {
        var r = Resolved(Origin(SingleTable, From("LOG_ENTRY", "TXT", SqlValueKind.Text)));
        var key = Assert.IsType<KeyResolution.Unavailable>(r.PrimaryKey);
        Assert.Equal(ExportUnavailableCode.NoPrimaryKey, key.Reason.Code);
    }

    [Fact]
    public void A_Result_Without_Its_Tables_Key_Column_Has_No_Verified_Key()
    {
        var r = Resolved(Origin(SingleTable, From("CUSTOMERS", "NAME"), From("CUSTOMERS", "CITY")));
        var key = Assert.IsType<KeyResolution.Unavailable>(r.PrimaryKey);
        Assert.Equal(ExportUnavailableCode.IncompletePrimaryKey, key.Reason.Code);
        Assert.Equal(new[] { "CUSTOMER_ID" }, key.Reason.Names);
    }

    // ── §1.3 — shapes that masquerade as a clean single-table result ─────────
    // THE case for signal B: the server reports a clean, key-complete CUSTOMERS result for leg 1 alone.
    // Nothing in the schema metadata can detect this — only the AST.
    [Fact]
    public void A_Union_Is_Refused_Even_Though_Provenance_Looks_Clean()
    {
        var origin = Origin(
            new StatementShape { IsUnderstood = true, IsSetOperation = true },
            From("CUSTOMERS", "CUSTOMER_ID"), From("CUSTOMERS", "NAME"));

        Assert.Equal(ExportUnavailableCode.SetOperation, Refused(origin).Code);
    }

    // A self-join reports ONE base table name for TWO different row instances, so counting distinct
    // names cannot catch it — the join itself is the veto.
    [Fact]
    public void A_Self_Join_Is_Refused_Despite_Reporting_One_Table()
    {
        var origin = Origin(
            new StatementShape { IsUnderstood = true, FromItemCount = 1, HasJoin = true },
            From("CUSTOMERS", "CUSTOMER_ID"), From("CUSTOMERS", "CUSTOMER_ID"));

        Assert.Equal(ExportUnavailableCode.Join, Refused(origin).Code);
    }

    [Fact]
    public void A_Multi_Table_Join_Is_Refused_And_Names_The_Tables()
    {
        var origin = Origin(
            new StatementShape { IsUnderstood = true, FromItemCount = 1, HasJoin = true },
            From("ORDERS", "ORDER_ID"), From("CUSTOMERS", "NAME"));

        var reason = Refused(origin);
        Assert.Equal(ExportUnavailableCode.Join, reason.Code);
        Assert.Equal(new[] { "ORDERS", "CUSTOMERS" }, reason.Names);
    }

    [Fact]
    public void A_Cross_Product_Is_Refused_And_Names_The_Tables()
    {
        var origin = Origin(
            new StatementShape { IsUnderstood = true, FromItemCount = 2 },
            From("ORDERS", "ORDER_ID"), From("CUSTOMERS", "NAME"));

        var reason = Refused(origin);
        Assert.Equal(ExportUnavailableCode.MultipleSourceTables, reason.Code);
        Assert.Equal(new[] { "ORDERS", "CUSTOMERS" }, reason.Names);
    }

    // `select CUSTOMER_ID, CUSTOMER_ID as AGAIN` would emit INSERT … (CUSTOMER_ID, CUSTOMER_ID).
    [Fact]
    public void Two_Columns_Sharing_One_Base_Column_Are_Refused()
    {
        var origin = Origin(SingleTable, From("CUSTOMERS", "CUSTOMER_ID"), From("CUSTOMERS", "CUSTOMER_ID"));
        var reason = Refused(origin);
        Assert.Equal(ExportUnavailableCode.DuplicateSourceColumn, reason.Code);
        Assert.Equal(new[] { "CUSTOMER_ID" }, reason.Names);
    }

    [Fact]
    public void An_Aggregate_Result_Is_Refused()
    {
        var origin = Origin(
            new StatementShape { IsUnderstood = true, FromItemCount = 1, HasGroupBy = true },
            From("ORDERS", "CUSTOMER_ID"), Derived("COUNT"));

        Assert.Equal(ExportUnavailableCode.Aggregate, Refused(origin).Code);
    }

    // A procedure and a view are indistinguishable from a table by schema metadata alone — only the
    // catalog knows. (Views are refused outright at this stage; updatable-view analysis is not done.)
    [Fact]
    public void A_Procedure_Result_Is_Refused_As_Not_A_Table()
    {
        var origin = Origin(SingleTable, From("SP_CUSTOMER_ORDERS", "ORDER_ID"));
        var reason = Refused(origin);
        Assert.Equal(ExportUnavailableCode.NotATable, reason.Code);
        Assert.Equal(SymbolKind.Procedure, reason.ObjectKind); // so the message can say "is a procedure"
    }

    [Fact]
    public void A_View_Result_Is_Refused_As_Not_A_Table()
    {
        var origin = Origin(SingleTable, From("V_ORDER_DETAILS", "ORDER_ID"));
        var reason = Refused(origin);
        Assert.Equal(ExportUnavailableCode.NotATable, reason.Code);
        Assert.Equal(SymbolKind.View, reason.ObjectKind);
    }

    // ── Derived expressions (§1.1) ───────────────────────────────────────────
    // A derived column's BaseColumn is an operator name (MULTIPLY/COUNT/CONSTANT) — garbage. It is not a
    // table column, so it is skipped, not emitted and not a veto.
    [Fact]
    public void A_Derived_Expression_Column_Is_Skipped_Not_Emitted()
    {
        var r = Resolved(Origin(SingleTable,
            From("CUSTOMERS", "CUSTOMER_ID"), Derived("MULTIPLY"), From("CUSTOMERS", "NAME")));

        Assert.Equal(new[] { "CUSTOMER_ID", "NAME" }, r.Columns.Select(c => c.BaseColumn));
        Assert.DoesNotContain(r.Columns, c => c.BaseColumn == "MULTIPLY");
    }

    // The ResultIndex must survive the skip — it indexes the ROW ARRAY, so an off-by-one here would
    // write the wrong value into the right column.
    [Fact]
    public void A_Skipped_Column_Does_Not_Shift_The_Row_Indexes_Of_Its_Neighbours()
    {
        var r = Resolved(Origin(SingleTable,
            From("CUSTOMERS", "CUSTOMER_ID"), Derived(), From("CUSTOMERS", "NAME")));

        Assert.Equal(0, r.Columns[0].ResultIndex);
        Assert.Equal(2, r.Columns[1].ResultIndex); // NOT 1 — index 1 is the derived column
    }

    [Fact]
    public void A_Result_Of_Only_Derived_Expressions_Is_Refused()
    {
        var origin = Origin(SingleTable, Derived("CONSTANT"), Derived("COUNT"));
        Assert.Equal(ExportUnavailableCode.NoSourceTable, Refused(origin).Code);
    }

    // ── Computed columns (§1.4) ──────────────────────────────────────────────
    // Firebird rejects writing one — "attempted update of read-only column". The catalog is the
    // authority; the driver's IsExpression is a second opinion, and either is enough.
    [Fact]
    public void A_Computed_Column_Is_Marked_From_The_Catalog_Even_When_The_Driver_Is_Silent()
    {
        var r = Resolved(Origin(SingleTable, From("RECT", "W"), From("RECT", "AREA")));
        Assert.False(r.Columns.Single(c => c.BaseColumn == "W").IsComputed);
        Assert.True(r.Columns.Single(c => c.BaseColumn == "AREA").IsComputed);
    }

    [Fact]
    public void A_Computed_Column_Is_Marked_From_The_Driver_Even_When_The_Catalog_Is_Silent()
    {
        var catalog = new FakeCatalog().Col("RECT", "W").Col("RECT", "AREA"); // catalog says nothing
        var origin = Origin(SingleTable,
            new ColumnOrigin("RECT", "AREA", IsComputed: true, SqlValueKind.Integer));

        Assert.True(Resolved(origin, catalog).Columns.Single().IsComputed);
    }

    // ── Signal B's own failure modes ─────────────────────────────────────────
    [Fact]
    public void A_Statement_The_Parser_Could_Not_Model_Is_Refused()
    {
        var origin = Origin(StatementShape.NotUnderstood, From("CUSTOMERS", "CUSTOMER_ID"));
        Assert.Equal(ExportUnavailableCode.StatementNotUnderstood, Refused(origin).Code);
    }

    [Fact]
    public void A_With_Query_Is_Refused()
    {
        var origin = Origin(
            new StatementShape { IsUnderstood = true, IsWithQuery = true }, From("CUSTOMERS", "CUSTOMER_ID"));
        Assert.Equal(ExportUnavailableCode.CommonTableExpression, Refused(origin).Code);
    }

    // ── Signal C's own failure modes ─────────────────────────────────────────
    [Fact]
    public void An_Object_The_Catalog_Does_Not_Know_Is_Refused()
    {
        var origin = Origin(SingleTable, From("NOPE", "X"));
        var reason = Refused(origin);
        Assert.Equal(ExportUnavailableCode.UnknownObject, reason.Code);
        Assert.Equal(new[] { "NOPE" }, reason.Names);
    }

    // A real table ALWAYS has columns, so an empty column list means "not warmed yet", never "no
    // columns". Reporting that as NoPrimaryKey would be a confident lie about the user's schema, and
    // the caller's correct response (warm and retry) differs from every other refusal.
    [Fact]
    public void A_Known_Table_With_No_Columns_Loaded_Reports_A_Cold_Catalog_Not_A_Missing_Key()
    {
        var catalog = new FakeCatalog().Object("CUSTOMERS", SymbolKind.Table); // known, but not warmed
        var origin = Origin(SingleTable, From("CUSTOMERS", "CUSTOMER_ID"));

        var reason = Refused(origin, catalog);
        Assert.Equal(ExportUnavailableCode.CatalogNotLoaded, reason.Code);
        Assert.NotEqual(ExportUnavailableCode.NoPrimaryKey, reason.Code);
    }

    // Stale metadata (a column added by an uncommitted DDL is invisible to the metadata attachment)
    // must refuse, not silently omit the column.
    [Fact]
    public void A_Base_Column_Missing_From_The_Catalog_Is_Refused()
    {
        var origin = Origin(SingleTable, From("CUSTOMERS", "CUSTOMER_ID"), From("CUSTOMERS", "ADDED_TODAY"));
        var reason = Refused(origin);
        Assert.Equal(ExportUnavailableCode.UnknownSourceColumn, reason.Code);
        Assert.Equal(new[] { "ADDED_TODAY" }, reason.Names);
    }

    // ── Table Data — the strictly-safer source ───────────────────────────────
    // The grid IS the table: no statement, nothing to infer, so B is satisfied by construction.
    [Fact]
    public void A_Direct_Table_Source_Needs_No_Statement_Analysis()
    {
        var origin = new ResultOrigin(
            new[] { From("CUSTOMERS", "CUSTOMER_ID"), From("CUSTOMERS", "NAME") },
            new OriginShape.DirectTable("CUSTOMERS"));

        var r = Resolved(origin);
        Assert.Equal("CUSTOMERS", r.Table);
        Assert.IsType<KeyResolution.Verified>(r.PrimaryKey);
    }

    // ── Procedure results — a permanent, honest veto ─────────────────────────
    [Fact]
    public void A_Source_That_Declares_Itself_Not_A_Table_Is_Refused_With_Its_Own_Reason()
    {
        var origin = ResultOrigin.None(ExportUnavailableReason.Of(ExportUnavailableCode.NotATable));
        Assert.Equal(ExportUnavailableCode.NotATable, Refused(origin).Code);
    }
}
