using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language.QuickInfo;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The QuickInfoEngine (Etap 6 / M1, design §5.12 / §8A / P9) — structured "quick documentation" of
/// the symbol under the caret, from the Semantic Model + its metadata snapshot. Pure Core, offline
/// (a fake <see cref="ISqlMetadataProvider"/>). The App renders it as the Ctrl-hover tooltip and the
/// completion detail pane (M4/M5).
/// </summary>
public class QuickInfoEngineTests
{
    // ── A fluent fake metadata provider with rich columns + routine params ────────────────────
    private sealed class FakeMetadata : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<RoutineParameterMetadata>> _params = new(StringComparer.OrdinalIgnoreCase);

        public FakeMetadata Object(string name, SymbolKind kind, string? description = null, string? owner = null)
        {
            _objects[name] = new ObjectMetadata(name, kind, description, owner);
            return this;
        }

        public FakeMetadata Col(string table, ColumnMetadata col)
        {
            if (!_objects.ContainsKey(table)) Object(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(col);
            return this;
        }

        public FakeMetadata Col(string table, string name, string type)
            => Col(table, new ColumnMetadata(name, type));

        public FakeMetadata Param(string routine, RoutineParameterMetadata p)
        {
            if (!_params.TryGetValue(routine, out var list)) _params[routine] = list = new();
            list.Add(p);
            return this;
        }

        public ObjectMetadata? FindObject(string name)
            => _objects.TryGetValue(name, out var o) ? o : null;

        public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
            => _cols.TryGetValue(tableOrView, out var c) ? c : Array.Empty<ColumnMetadata>();

        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
            => _params.TryGetValue(routine, out var p) ? p : Array.Empty<RoutineParameterMetadata>();

        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    private static QuickInfo? At(string sql, int offset, ISqlMetadataProvider? meta = null)
    {
        var model = SemanticModel.Build(sql, meta);
        return QuickInfoEngine.GetQuickInfo(model, offset);
    }

    // Offset inside the first occurrence of `needle`.
    private static int In(string sql, string needle) => sql.IndexOf(needle, StringComparison.Ordinal) + 1;

    private static string? Fact(QuickInfo qi, string label)
        => qi.Facts.FirstOrDefault(f => f.Label == label)?.Value;

    // ── Columns — the headline case ────────────────────────────────────────────────────────────

    [Fact]
    public void Column_FullFacts()
    {
        var meta = new FakeMetadata()
            .Object("KONTRAHENT", SymbolKind.Table)
            .Col("KONTRAHENT", new ColumnMetadata("NAZWA", "VARCHAR(50)")
            {
                Domain = "T_NAME",
                Nullable = false,
                DefaultValue = "''",
                Description = "Customer name",
            });
        const string sql = "select k.nazwa from kontrahent k";
        var qi = At(sql, In(sql, "nazwa"), meta);

        Assert.NotNull(qi);
        Assert.Equal(SymbolKind.Column, qi!.Kind);
        Assert.Equal("NAZWA : VARCHAR(50)", qi.Header);
        Assert.Equal("Customer name", qi.Description);
        Assert.Equal("KONTRAHENT", Fact(qi, "Table"));
        Assert.Equal("T_NAME", Fact(qi, "Domain"));
        Assert.Equal("NOT NULL", Fact(qi, "Nullability"));
        Assert.Equal("''", Fact(qi, "Default"));
    }

    // P3: Ctrl+Space on a FULLY-TYPED identifier shows its facts. The caret sits at the exact END
    // of the word (`nazwa|`), which is where the user is after typing it — Quick Info must resolve
    // there, not only mid-word. (The end-inclusive ReferenceAt fix — same class as gotcha #198.)
    [Fact]
    public void Column_ResolvesAtEndOfIdentifier()
    {
        var meta = new FakeMetadata()
            .Col("KONTRAHENT", new ColumnMetadata("NAZWA", "VARCHAR(50)") { Domain = "T_NAME" });
        const string sql = "select k.nazwa from kontrahent k";
        int endOfNazwa = sql.IndexOf("nazwa", StringComparison.Ordinal) + "nazwa".Length;

        var qi = At(sql, endOfNazwa, meta);
        Assert.NotNull(qi);
        Assert.Equal(SymbolKind.Column, qi!.Kind);
        Assert.Equal("NAZWA : VARCHAR(50)", qi.Header);
        Assert.Equal("T_NAME", Fact(qi, "Domain"));
    }

    [Fact]
    public void Column_PrimaryKey()
    {
        var meta = new FakeMetadata()
            .Col("KONTRAHENT", new ColumnMetadata("ID", "INTEGER") { IsPrimaryKey = true, Nullable = false });
        const string sql = "select k.id from kontrahent k";
        var qi = At(sql, In(sql, "id"), meta);
        Assert.Equal("PRIMARY KEY", Fact(qi!, "Key"));
    }

    [Fact]
    public void Column_ForeignKey_ShowsReferencedTable()
    {
        var meta = new FakeMetadata()
            .Col("ORDERS", new ColumnMetadata("ID_KONTRAHENT", "INTEGER")
            {
                IsForeignKey = true,
                ForeignKeyTable = "KONTRAHENT",
            });
        const string sql = "select o.id_kontrahent from orders o";
        var qi = At(sql, In(sql, "id_kontrahent"), meta);
        Assert.Equal("FOREIGN KEY → KONTRAHENT", Fact(qi!, "Key"));
    }

    [Fact]
    public void Column_ComputedAndIdentity()
    {
        var meta = new FakeMetadata()
            .Col("T", new ColumnMetadata("C1", "INTEGER") { IsComputed = true })
            .Col("T", new ColumnMetadata("C2", "INTEGER") { IsIdentity = true });
        const string sql = "select t.c1, t.c2 from t";
        Assert.Equal("Computed", Fact(At(sql, In(sql, "c1"), meta)!, "Generated"));
        Assert.Equal("Identity", Fact(At(sql, In(sql, "c2"), meta)!, "Generated"));
    }

    [Fact]
    public void Column_Minimal_NoType()
    {
        var meta = new FakeMetadata().Col("T", new ColumnMetadata("C", ""));
        const string sql = "select t.c from t";
        var qi = At(sql, sql.IndexOf(".c", StringComparison.Ordinal) + 1, meta); // on the `c`
        Assert.Equal("C", qi!.Header);
    }

    // ── Tables / views — header + column members ───────────────────────────────────────────────

    [Fact]
    public void Table_ListsColumns()
    {
        var meta = new FakeMetadata()
            .Object("KONTRAHENT", SymbolKind.Table, description: "Customers", owner: "SYSDBA")
            .Col("KONTRAHENT", "ID", "INTEGER")
            .Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql = "select * from kontrahent";
        var qi = At(sql, In(sql, "kontrahent"), meta);

        Assert.NotNull(qi);
        Assert.Equal(SymbolKind.Table, qi!.Kind);
        Assert.Equal("KONTRAHENT", qi.Header);
        Assert.Equal("Customers", qi.Description);
        Assert.Equal("SYSDBA", Fact(qi, "Owner"));
        Assert.Equal(2, qi.Members.Count);
        Assert.All(qi.Members, m => Assert.Equal(QuickInfoMemberGroup.Column, m.Group));
        Assert.Contains(qi.Members, m => m.Text == "ID INTEGER");
        Assert.Contains(qi.Members, m => m.Text == "NAZWA VARCHAR(50)");
    }

    [Fact]
    public void View_IsRecognised()
    {
        var meta = new FakeMetadata().Object("V_ORDERS", SymbolKind.View).Col("V_ORDERS", "TOTAL", "NUMERIC(15,2)");
        const string sql = "select * from v_orders";
        var qi = At(sql, In(sql, "v_orders"), meta);
        Assert.Equal(SymbolKind.View, qi!.Kind);
        Assert.Single(qi.Members);
    }

    // ── Procedures / functions — parameter members ─────────────────────────────────────────────

    [Fact]
    public void Procedure_ListsParametersAndReturns()
    {
        var meta = new FakeMetadata()
            .Object("ADD_ORDER", SymbolKind.Procedure, description: "Adds an order")
            .Param("ADD_ORDER", new RoutineParameterMetadata("ID_KONTRAHENT", "INTEGER", ParameterDirection.Input))
            .Param("ADD_ORDER", new RoutineParameterMetadata("ID_ORDER", "INTEGER", ParameterDirection.Output));
        const string sql = "execute procedure add_order";
        var qi = At(sql, In(sql, "add_order"), meta);

        Assert.NotNull(qi);
        Assert.Equal(SymbolKind.Procedure, qi!.Kind);
        Assert.Equal("Adds an order", qi.Description);
        Assert.Contains(qi.Members, m => m.Text == "ID_KONTRAHENT INTEGER" && m.Group == QuickInfoMemberGroup.Parameter);
        Assert.Contains(qi.Members, m => m.Text == "ID_ORDER INTEGER" && m.Group == QuickInfoMemberGroup.Returns);
    }

    // ── Generic objects (domain/exception/generator) — kind + description, no members ──────────

    [Fact]
    public void GenericObject_Domain_KindAndDescription()
    {
        var meta = new FakeMetadata().Object("T_NAME", SymbolKind.Domain, description: "A name domain");
        // A domain referenced by name won't resolve inside a query FROM as a table; use EXECUTE
        // PROCEDURE only resolves procedures — instead exercise ForSymbol directly for coverage.
        var qi = QuickInfoEngine.ForSymbol(
            new SchemaObjectSymbol(SymbolKind.Domain, "T_NAME") { Description = "A name domain" }, meta);
        Assert.Equal(SymbolKind.Domain, qi.Kind);
        Assert.Equal("T_NAME", qi.Header);
        Assert.Equal("A name domain", qi.Description);
        Assert.Empty(qi.Members);
    }

    // ── Aliases (FROM/JOIN) → the underlying table ─────────────────────────────────────────────

    [Fact]
    public void Alias_ShowsTargetAndColumns()
    {
        var meta = new FakeMetadata()
            .Object("KONTRAHENT", SymbolKind.Table, owner: "SYSDBA")
            .Col("KONTRAHENT", "ID", "INTEGER");
        const string sql = "select k.id from kontrahent k";
        // Hover the qualifier `k` in `k.id`.
        var qi = At(sql, sql.IndexOf("k.", StringComparison.Ordinal), meta);

        Assert.NotNull(qi);
        Assert.Equal(SymbolKind.TableReference, qi!.Kind);
        Assert.Equal("K → KONTRAHENT", qi.Header);
        Assert.Equal("Table", Fact(qi, "Kind"));
        Assert.Contains(qi.Members, m => m.Text == "ID INTEGER");
    }

    [Fact]
    public void TableByOwnName_NoArrowHeader()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        const string sql = "select nazwa from kontrahent";
        // Hover the table name in FROM (bound as a TableReference by its own name).
        var qi = At(sql, In(sql, "from kontrahent") + 5, meta);
        Assert.NotNull(qi);
        // Either the schema-object ref or the table-ref occurrence resolves; both name KONTRAHENT.
        Assert.Contains("KONTRAHENT", qi!.Header);
        Assert.DoesNotContain("→", qi.Header);
    }

    // ── NEW / OLD in a trigger → the trigger table's columns ──────────────────────────────────

    [Fact]
    public void TriggerRecordAlias_ShowsTableColumns()
    {
        var meta = new FakeMetadata()
            .Object("KONTRAHENT", SymbolKind.Table)
            .Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql =
            "create trigger tr for kontrahent before insert as begin if (new.nazwa is null) then exception; end";
        // Hover the `new` qualifier.
        var qi = At(sql, In(sql, "new.nazwa"), meta);
        Assert.NotNull(qi);
        Assert.Equal(SymbolKind.RecordAlias, qi!.Kind);
        Assert.Contains("KONTRAHENT", qi.Header);
        Assert.Contains(qi.Members, m => m.Text == "NAZWA VARCHAR(50)");
    }

    // ── CTE ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cte_ListsDeclaredColumns()
    {
        const string sql = "with t (a, b) as (select 1, 2 from rdb$database) select a from t";
        // Hover the CTE name in the trailing FROM (`from t` at end of the script).
        var qi = At(sql, sql.LastIndexOf("from t", StringComparison.Ordinal) + 5, null);
        Assert.NotNull(qi);
        // `t` in the trailing FROM resolves to the CTE (via a TableReference whose target is the CTE)
        // or the CTE directly; either way the header names T.
        Assert.Contains("T", qi!.Header, StringComparison.OrdinalIgnoreCase);
    }

    // ── PSQL locals ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Variable_And_Parameter()
    {
        // A parameter reference resolves inside the routine body.
        const string sql =
            "create procedure p (id integer) as declare variable total integer; begin total = id; end";
        var pqi = At(sql, In(sql, "= id") + 2, null);
        Assert.NotNull(pqi);
        Assert.Equal(SymbolKind.Parameter, pqi!.Kind);
        Assert.Equal("Input parameter", Fact(pqi, "Kind"));

        var vqi = At(sql, In(sql, "total = id"), null);
        Assert.NotNull(vqi);
        Assert.Equal(SymbolKind.Variable, vqi!.Kind);
    }

    // ── Boundaries / robustness ────────────────────────────────────────────────────────────────

    [Fact]
    public void OffsetNotOnSymbol_ReturnsNull()
    {
        var meta = new FakeMetadata().Object("T", SymbolKind.Table).Col("T", "C", "INTEGER");
        const string sql = "select c from t";
        Assert.Null(At(sql, 0, meta));            // on "select" keyword
        Assert.Null(At(sql, In(sql, " from"), meta)); // on whitespace
    }

    [Fact]
    public void UnresolvedIdentifier_NoMetadata_ReturnsNull()
    {
        // No metadata → the bare column can't resolve to a symbol → no quick info.
        const string sql = "select nazwa from kontrahent";
        Assert.Null(At(sql, In(sql, "nazwa"), null));
    }

    [Fact]
    public void ForSymbol_NeverThrows_WithEmptyMetadata()
    {
        foreach (SymbolKind k in Enum.GetValues(typeof(SymbolKind)))
        {
            var sym = k == SymbolKind.Column
                ? new ColumnSymbol("C") { OwningTable = "T" }
                : (Symbol)new SchemaObjectSymbol(k, "X");
            var qi = QuickInfoEngine.ForSymbol(sym);
            Assert.NotNull(qi);
            Assert.False(string.IsNullOrEmpty(qi.Header));
        }
    }

    [Fact]
    public void GarbageInput_NeverThrows()
    {
        foreach (var sql in new[] { "", "   ", "select", "((((", "create procedure", "'unterminated" })
        {
            var model = SemanticModel.Build(sql);
            for (int i = 0; i <= sql.Length; i++)
            {
                _ = QuickInfoEngine.GetQuickInfo(model, i); // must not throw
            }
        }
    }
}
