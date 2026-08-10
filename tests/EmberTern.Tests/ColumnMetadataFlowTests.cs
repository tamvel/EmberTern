using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.App.Completion;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Language.QuickInfo;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Package 5, Stage A: the enriched <see cref="ColumnSpec"/> must flow, field-for-field,
/// through the App's metadata snapshot (<see cref="AppMetadataSnapshot.GetColumns"/>) into
/// the semantic <see cref="ColumnMetadata"/> the Quick Info engine renders. This pins the one
/// wiring seam Stage A adds — the Firebird reader and the Quick Info engine already carry the
/// rich fields and are covered by their own tests (<c>MetadataReaderTests</c> /
/// <c>QuickInfoEngineTests</c>); here we prove the snapshot no longer drops them. One column
/// model, many consumers.
/// </summary>
public class ColumnMetadataFlowTests
{
    private static AppMetadataSnapshot BuildSnapshot(string table, params ColumnSpec[] cols)
    {
        var objects = new[] { new MetadataObject(table, MetadataObjectKind.Table) };
        var columnCache = new Dictionary<string, IReadOnlyList<ColumnSpec>>(StringComparer.OrdinalIgnoreCase)
        {
            [table] = cols,
        };
        return AppMetadataSnapshot.Build(objects, columnCache);
    }

    [Fact]
    public void GetColumns_MapsEveryRichField()
    {
        var spec = new ColumnSpec("ID_KONTRAHENT", "INTEGER", Domain: "T_ID", NotNull: true)
        {
            DefaultValue = "0",
            Description = "Customer FK",
            IsForeignKey = true,
            ForeignKeyTable = "KONTRAHENT",
        };
        var snapshot = BuildSnapshot("ORDERS", spec);

        var col = Assert.Single(snapshot.GetColumns("ORDERS"));
        Assert.Equal("ID_KONTRAHENT", col.Name);
        Assert.Equal("INTEGER", col.Type);
        Assert.Equal("T_ID", col.Domain);
        Assert.False(col.Nullable);            // NotNull=true → Nullable=false
        Assert.Equal("0", col.DefaultValue);
        Assert.Equal("Customer FK", col.Description);
        Assert.True(col.IsForeignKey);
        Assert.Equal("KONTRAHENT", col.ForeignKeyTable);
        Assert.False(col.IsPrimaryKey);
        Assert.False(col.IsComputed);
        Assert.False(col.IsIdentity);
    }

    [Fact]
    public void GetColumns_PrimaryKeyIdentityComputed_Flow()
    {
        var pk = new ColumnSpec("ID", "INTEGER", NotNull: true) { IsPrimaryKey = true, Identity = IdentityKind.ByDefault };
        var comp = new ColumnSpec("FULLNAME", "VARCHAR(100)") { IsComputed = true };
        var snapshot = BuildSnapshot("KONTRAHENT", pk, comp);

        var cols = snapshot.GetColumns("KONTRAHENT");
        Assert.Equal(2, cols.Count);
        Assert.True(cols[0].IsPrimaryKey);
        Assert.True(cols[0].IsIdentity);
        Assert.False(cols[0].Nullable);
        Assert.True(cols[1].IsComputed);
    }

    [Fact]
    public void GetColumns_NullableWhenNotNullFalse()
    {
        var snapshot = BuildSnapshot("T", new ColumnSpec("C", "INTEGER"));
        var col = Assert.Single(snapshot.GetColumns("T"));
        Assert.True(col.Nullable);
    }

    // End-to-end: the rich column reaches QuickInfoEngine through the real ISqlMetadataProvider
    // the editor uses (AppMetadataSnapshot), not a hand-rolled fake — so this exercises the exact
    // path a Ctrl+hover takes.
    [Fact]
    public void RichColumn_ReachesQuickInfo_EndToEnd()
    {
        var spec = new ColumnSpec("NAZWA", "VARCHAR(50)", Domain: "T_NAME", NotNull: true)
        {
            Description = "Customer name",
        };
        var snapshot = BuildSnapshot("KONTRAHENT", spec);

        const string sql = "select k.nazwa from kontrahent k";
        var model = SemanticModel.Build(sql, snapshot);
        var qi = QuickInfoEngine.GetQuickInfo(model, sql.IndexOf("nazwa", StringComparison.Ordinal) + 1);

        Assert.NotNull(qi);
        Assert.Equal(SymbolKind.Column, qi!.Kind);
        Assert.Equal("NAZWA : VARCHAR(50)", qi.Header);
        Assert.Equal("Customer name", qi.Description);
        Assert.Equal("T_NAME", qi.Facts.First(f => f.Label == QuickInfoMessages.Domain).Value);
        Assert.Equal("NOT NULL", qi.Facts.First(f => f.Label == QuickInfoMessages.Nullability).Value);
    }

    [Fact]
    public void RichColumn_ForeignKey_ReachesQuickInfo_EndToEnd()
    {
        var spec = new ColumnSpec("ID_KONTRAHENT", "INTEGER")
        {
            IsForeignKey = true,
            ForeignKeyTable = "KONTRAHENT",
        };
        var snapshot = BuildSnapshot("ORDERS", spec);

        const string sql = "select o.id_kontrahent from orders o";
        var model = SemanticModel.Build(sql, snapshot);
        var qi = QuickInfoEngine.GetQuickInfo(model, sql.IndexOf("id_kontrahent", StringComparison.Ordinal) + 1);

        Assert.NotNull(qi);
        Assert.Equal("FOREIGN KEY → KONTRAHENT", qi!.Facts.First(f => f.Label == QuickInfoMessages.Key).Value);
    }

    // ── Stage B/C: warmed object detail flows through the snapshot into Quick Info ────────────────

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<ColumnSpec>> NoColumns =
        new Dictionary<string, IReadOnlyList<ColumnSpec>>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void FindObject_MergesWarmedDetail()
    {
        var objects = new[]
        {
            new MetadataObject("CALC", MetadataObjectKind.Function),
            new MetadataObject("TR_X", MetadataObjectKind.Trigger),
        };
        var detail = new Dictionary<string, ObjectDetail>(StringComparer.OrdinalIgnoreCase)
        {
            ["CALC"] = new ObjectDetail("Computes a value", "NUMERIC(15,2)", null),
            ["TR_X"] = new ObjectDetail("Audit trail", null,
                new TriggerDetail("KONTRAHENT", IsBefore: true, FiresInsert: true, FiresUpdate: false, FiresDelete: false, Position: 3, Active: true)),
        };
        var snap = AppMetadataSnapshot.Build(objects, NoColumns, null, detail);

        var calc = snap.FindObject("CALC");
        Assert.Equal("Computes a value", calc!.Description);
        Assert.Equal("NUMERIC(15,2)", calc.ReturnType);

        var tr = snap.FindObject("TR_X");
        Assert.Equal("Audit trail", tr!.Description);
        Assert.Equal("KONTRAHENT", tr.Trigger!.Table);
        Assert.True(tr.Trigger.IsBefore);
        Assert.Equal(3, tr.Trigger.Position);
    }

    [Fact]
    public void FindObject_WithoutWarmedDetail_ReturnsBareObject()
    {
        var snap = AppMetadataSnapshot.Build(new[] { new MetadataObject("T", MetadataObjectKind.Table) }, NoColumns);
        var t = snap.FindObject("T");
        Assert.NotNull(t);
        Assert.Null(t!.Description);
        Assert.Null(t.ReturnType);
        Assert.Null(t.Trigger);
    }

    [Fact]
    public void FunctionReturnType_ReachesQuickInfo_EndToEnd()
    {
        var objects = new[] { new MetadataObject("CALC", MetadataObjectKind.Function) };
        var detail = new Dictionary<string, ObjectDetail>(StringComparer.OrdinalIgnoreCase)
        {
            ["CALC"] = new ObjectDetail("Computes a value", "NUMERIC(15,2)", null),
        };
        var snap = AppMetadataSnapshot.Build(objects, NoColumns, null, detail);

        const string sql = "select calc(1) from rdb$database";
        var model = SemanticModel.Build(sql, snap);
        var qi = QuickInfoEngine.GetQuickInfo(model, sql.IndexOf("calc", StringComparison.Ordinal) + 1);

        Assert.NotNull(qi);
        Assert.Equal(SymbolKind.Function, qi!.Kind);
        Assert.Equal("Computes a value", qi.Description);
        Assert.Equal("NUMERIC(15,2)", qi.Facts.First(f => f.Label == QuickInfoMessages.Returns).Value);
    }

    [Fact]
    public void TriggerFacts_ReachQuickInfo_EndToEnd()
    {
        var objects = new[]
        {
            new MetadataObject("KONTRAHENT", MetadataObjectKind.Table),
            new MetadataObject("TR_AUDIT", MetadataObjectKind.Trigger),
        };
        var detail = new Dictionary<string, ObjectDetail>(StringComparer.OrdinalIgnoreCase)
        {
            ["TR_AUDIT"] = new ObjectDetail("Audit trail", null,
                new TriggerDetail("KONTRAHENT", IsBefore: false, FiresInsert: true, FiresUpdate: true, FiresDelete: true, Position: 0, Active: true)),
        };
        var snap = AppMetadataSnapshot.Build(objects, NoColumns, null, detail);

        // The trigger name resolves as a schema object in its CREATE TRIGGER header.
        const string sql = "create trigger tr_audit for kontrahent after insert as begin end";
        var model = SemanticModel.Build(sql, snap);
        var qi = QuickInfoEngine.GetQuickInfo(model, sql.IndexOf("tr_audit", StringComparison.Ordinal) + 2);

        Assert.NotNull(qi);
        Assert.Equal(SymbolKind.Trigger, qi!.Kind);
        Assert.Equal("KONTRAHENT", qi.Facts.First(f => f.Label == QuickInfoMessages.Table).Value);
        Assert.Equal("AFTER INSERT OR UPDATE OR DELETE", qi.Facts.First(f => f.Label == QuickInfoMessages.Fires).Value);
    }

    // ══ Column readiness (S-2, 2026-08-05) ═══════════════════════════════════════════════════════
    //
    // ⭐ The snapshot is the ONE place the App can answer "not loaded yet" honestly, because the cache
    // DICTIONARY distinguishes a missing key from a present-but-empty entry while GetColumns collapses both
    // to an empty list. The information existed all along and was thrown away one layer too early — which is
    // what let DiagnosticsEngine report every unwarmed column as unknown.

    [Fact]
    public void KnowsColumns_False_WhenTheObjectHasNotBeenWarmed()
    {
        // The object is known (it came from a loaded category); its columns have not been read yet — the
        // state EVERY object is in at the moment a tab opens.
        var snap = AppMetadataSnapshot.Build(
            new[] { new MetadataObject("ORDERS", MetadataObjectKind.Table) },
            new Dictionary<string, IReadOnlyList<ColumnSpec>>(StringComparer.OrdinalIgnoreCase));

        Assert.NotNull(snap.FindObject("ORDERS"));
        Assert.Empty(snap.GetColumns("ORDERS"));
        Assert.False(snap.KnowsColumns("ORDERS"));
    }

    [Fact]
    public void KnowsColumns_True_OnceWarmed_EvenWhenTheAnswerIsNoColumns()
    {
        // ⚠ A present-but-EMPTY entry counts as KNOWN. The warm pass caches what it read, so an object whose
        // read legitimately returned nothing has been ANSWERED — treating it as pending would silence a
        // genuine typo on that object for the rest of the session.
        var warmedEmpty = new Dictionary<string, IReadOnlyList<ColumnSpec>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ORDERS"] = Array.Empty<ColumnSpec>(),
        };
        var snap = AppMetadataSnapshot.Build(
            new[] { new MetadataObject("ORDERS", MetadataObjectKind.Table) }, warmedEmpty);

        Assert.True(snap.KnowsColumns("ORDERS"));
        Assert.True(BuildSnapshot("KONTRAHENT", new ColumnSpec("NAZWA", "VARCHAR(60)")).KnowsColumns("KONTRAHENT"));
    }

    [Fact]
    public void KnowsColumns_IsCaseInsensitive_LikeEveryOtherLookupHere()
    {
        var snap = BuildSnapshot("KONTRAHENT", new ColumnSpec("NAZWA", "VARCHAR(60)"));
        Assert.True(snap.KnowsColumns("kontrahent"));
    }
}
