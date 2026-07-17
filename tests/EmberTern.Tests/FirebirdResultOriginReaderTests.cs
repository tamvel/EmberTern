using System;
using System.Data;
using EmberTern.Core.Export.Sql;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

// E2 — the GetSchemaTable() → ColumnOrigin translation. The schema tables here are built to match, row
// for row, what a live Firebird 5.0 engine actually returned (probe §6): the column set is the driver's
// real one and the ProviderType values are the measured ints, so these are not invented fixtures.
public class FirebirdResultOriginReaderTests
{
    // The driver's real schema-table shape, verified against the engine.
    private static DataTable SchemaTable()
    {
        var t = new DataTable();
        t.Columns.Add("ColumnName", typeof(string));
        t.Columns.Add("ProviderType", typeof(int));
        t.Columns.Add("IsKey", typeof(bool));
        t.Columns.Add("IsUnique", typeof(bool));
        t.Columns.Add("IsExpression", typeof(bool));
        t.Columns.Add("BaseTableName", typeof(string));
        t.Columns.Add("BaseColumnName", typeof(string));
        return t;
    }

    private static void Add(
        DataTable t, string name, FbDbType type, string baseTable, string baseColumn,
        bool isExpression = false, bool isKey = false)
        => t.Rows.Add(name, (int)type, isKey, false, isExpression, baseTable, baseColumn);

    // ── The measured ProviderType ints ───────────────────────────────────────
    // These are the exact values the engine returned. If the driver ever renumbered FbDbType, this is
    // where it would surface — quietly reading the wrong type is a silent-corruption path.
    [Theory]
    [InlineData(10, SqlValueKind.Integer)]    // Integer
    [InlineData(11, SqlValueKind.Decimal)]    // Numeric
    [InlineData(16, SqlValueKind.Text)]       // VarChar
    [InlineData(5, SqlValueKind.Date)]        // Date
    [InlineData(14, SqlValueKind.Time)]       // Time
    [InlineData(15, SqlValueKind.Timestamp)]  // TimeStamp
    [InlineData(3, SqlValueKind.Boolean)]     // Boolean
    [InlineData(2, SqlValueKind.BinaryBlob)]  // Binary
    [InlineData(13, SqlValueKind.TextBlob)]   // Text
    public void The_Measured_ProviderType_Ints_Map_To_The_Right_Kinds(int providerType, SqlValueKind expected)
    {
        var t = SchemaTable();
        t.Rows.Add("C", providerType, false, false, false, "T", "C");

        Assert.Equal(expected, Assert.Single(FirebirdResultOriginReader.ReadColumnOrigins(t)).ValueKind);
    }

    // DATE=5 and TIMESTAMP=15 are different ints — which is the whole reason the declared type is read
    // from ProviderType rather than inferred from the CLR type (both are DateTime).
    [Fact]
    public void Date_And_Timestamp_Are_Distinguished_By_The_Declared_Type()
    {
        var t = SchemaTable();
        Add(t, "D", FbDbType.Date, "T", "D");
        Add(t, "TS", FbDbType.TimeStamp, "T", "TS");

        var origins = FirebirdResultOriginReader.ReadColumnOrigins(t);
        Assert.Equal(SqlValueKind.Date, origins[0].ValueKind);
        Assert.Equal(SqlValueKind.Timestamp, origins[1].ValueKind);
    }

    // ── Provenance ───────────────────────────────────────────────────────────
    [Fact]
    public void An_Aliased_Column_Carries_Its_Base_Column_Not_Its_Header()
    {
        var t = SchemaTable();
        Add(t, "CID", FbDbType.Integer, "CUSTOMERS", "CUSTOMER_ID"); // select c.CUSTOMER_ID as CID

        var o = Assert.Single(FirebirdResultOriginReader.ReadColumnOrigins(t));
        Assert.Equal("CUSTOMERS", o.BaseTable);
        Assert.Equal("CUSTOMER_ID", o.BaseColumn);
        Assert.False(o.IsDerivedExpression);
    }

    // Measured: `select CUSTOMER_ID * 2 as DOUBLED` → BaseTableName empty, BaseColumnName "MULTIPLY",
    // IsExpression FALSE. So the EMPTY BASE TABLE is the derived-expression signal, and IsExpression is
    // not — getting this backwards would emit a column literally named MULTIPLY into an INSERT.
    [Theory]
    [InlineData("MULTIPLY")]
    [InlineData("COUNT")]
    [InlineData("CONSTANT")]
    public void An_Empty_Base_Table_Marks_A_Derived_Expression_Even_Though_IsExpression_Is_False(string operatorName)
    {
        var t = SchemaTable();
        t.Rows.Add("DOUBLED", (int)FbDbType.Integer, false, false, /*IsExpression:*/ false, "", operatorName);

        var o = Assert.Single(FirebirdResultOriginReader.ReadColumnOrigins(t));
        Assert.True(o.IsDerivedExpression);
        Assert.False(o.IsComputed); // the driver really does say false here
        Assert.Null(o.BaseTable);
    }

    // IsExpression=true is meaningful for exactly one thing: a COMPUTED BY column, which Firebird
    // refuses to write ("attempted update of read-only column").
    [Fact]
    public void IsExpression_On_A_Real_Column_Marks_It_Computed()
    {
        var t = SchemaTable();
        Add(t, "AREA", FbDbType.Integer, "RECT", "AREA", isExpression: true);

        var o = Assert.Single(FirebirdResultOriginReader.ReadColumnOrigins(t));
        Assert.True(o.IsComputed);
        Assert.False(o.IsDerivedExpression); // it IS a real column — just not a writable one
    }

    // ── Unknown ⇒ refuse, never guess ────────────────────────────────────────
    [Fact]
    public void An_Unmapped_Declared_Type_Becomes_Unknown_And_Therefore_Refuses()
    {
        var t = SchemaTable();
        Add(t, "TS_TZ", FbDbType.TimeStampTZ, "T", "TS_TZ"); // FB4 WITH TIME ZONE

        Assert.Equal(SqlValueKind.Unknown, Assert.Single(FirebirdResultOriginReader.ReadColumnOrigins(t)).ValueKind);
    }

    [Fact]
    public void A_ProviderType_Outside_The_Enum_Becomes_Unknown_Rather_Than_An_Undefined_Type()
    {
        var t = SchemaTable();
        t.Rows.Add("WAT", 9999, false, false, false, "T", "WAT");

        Assert.Equal(SqlValueKind.Unknown, Assert.Single(FirebirdResultOriginReader.ReadColumnOrigins(t)).ValueKind);
    }

    [Fact]
    public void A_Missing_Or_Null_ProviderType_Becomes_Unknown()
    {
        var t = SchemaTable();
        t.Rows.Add("C", DBNull.Value, false, false, false, "T", "C");
        Assert.Equal(SqlValueKind.Unknown, Assert.Single(FirebirdResultOriginReader.ReadColumnOrigins(t)).ValueKind);

        var bare = new DataTable();
        bare.Columns.Add("ColumnName", typeof(string));
        bare.Rows.Add("C");
        Assert.Equal(SqlValueKind.Unknown, Assert.Single(FirebirdResultOriginReader.ReadColumnOrigins(bare)).ValueKind);
    }

    // ── Order ────────────────────────────────────────────────────────────────
    // Origins are positional — they line up with the row array by index, so any reordering here would
    // write the right value into the wrong column.
    [Fact]
    public void Origins_Are_Returned_In_Result_Column_Order()
    {
        var t = SchemaTable();
        Add(t, "A", FbDbType.Integer, "T", "A");
        Add(t, "B", FbDbType.VarChar, "T", "B");
        Add(t, "C", FbDbType.Date, "T", "C");

        var origins = FirebirdResultOriginReader.ReadColumnOrigins(t);
        Assert.Equal(new[] { "A", "B", "C" }, new[] { origins[0].BaseColumn, origins[1].BaseColumn, origins[2].BaseColumn });
    }

    // ── The whole chain, on the measured UNION shape ─────────────────────────
    // The engine reports a clean, key-complete CUSTOMERS result for `… from CUSTOMERS union all … from
    // PRODUCTS` — leg 1 only (measured). This pins that the capture faithfully reproduces that lie, so
    // the AST veto is what saves us, exactly as designed.
    [Fact]
    public void The_Capture_Reproduces_The_Unions_Misleading_Provenance_Faithfully()
    {
        var t = SchemaTable();
        Add(t, "CUSTOMER_ID", FbDbType.Integer, "CUST", "CUSTOMER_ID", isKey: true);
        Add(t, "NAME", FbDbType.VarChar, "CUST", "NAME");

        var origins = FirebirdResultOriginReader.ReadColumnOrigins(t);
        Assert.All(origins, o => Assert.Equal("CUST", o.BaseTable)); // PRODUCTS is nowhere to be seen

        // …and the resolver refuses it anyway, because signal B saw the set operation.
        var origin = new ResultOrigin(origins, new OriginShape.Statement(
            new StatementShape { IsUnderstood = true, IsSetOperation = true }));
        var refused = Assert.IsType<TargetResolution.Unavailable>(
            ResultOriginResolver.Resolve(origin, EmberTern.Core.Sql.Language.Semantics.EmptyMetadataProvider.Instance));
        Assert.Equal(ExportUnavailableCode.SetOperation, refused.Reason.Code);
    }
}
