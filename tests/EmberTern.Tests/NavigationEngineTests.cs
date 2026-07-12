using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language.Navigation;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The NavigationEngine (Etap 6 / M2, design §5.8 / §10) — resolves the navigable thing under the
/// caret from the Semantic Model (real resolution, NOT a name search): schema objects → open;
/// aliases/CTEs/variables/params → local definition. Pure Core, offline (a fake
/// <see cref="ISqlMetadataProvider"/>). Ctrl+hover/click glue is App (M4).
/// </summary>
public class NavigationEngineTests
{
    private sealed class FakeMetadata : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);

        public FakeMetadata Object(string name, SymbolKind kind)
        {
            _objects[name] = new ObjectMetadata(name, kind);
            return this;
        }

        public FakeMetadata Col(string table, string name, string type = "INTEGER")
        {
            if (!_objects.ContainsKey(table)) Object(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(new ColumnMetadata(name, type));
            return this;
        }

        public ObjectMetadata? FindObject(string name) => _objects.TryGetValue(name, out var o) ? o : null;
        public IReadOnlyList<ColumnMetadata> GetColumns(string t) => _cols.TryGetValue(t, out var c) ? c : Array.Empty<ColumnMetadata>();
        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string r) => Array.Empty<RoutineParameterMetadata>();
        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    private static NavigationTarget? At(string sql, int offset, ISqlMetadataProvider? meta = null)
        => NavigationEngine.TargetAt(SemanticModel.Build(sql, meta), offset);

    // ── Schema objects → open ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TableInFrom_OpensTable()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        const string sql = "select * from kontrahent";
        var t = At(sql, sql.IndexOf("kontrahent", StringComparison.Ordinal) + 2, meta);

        Assert.NotNull(t);
        Assert.Equal(NavigationTargetKind.SchemaObject, t!.Kind);
        Assert.Equal("KONTRAHENT", t.ObjectName);
        Assert.Equal(SymbolKind.Table, t.ObjectKind);
    }

    [Fact]
    public void ProcedureName_OpensProcedure()
    {
        var meta = new FakeMetadata().Object("ADD_ORDER", SymbolKind.Procedure);
        const string sql = "execute procedure add_order";
        var t = At(sql, sql.IndexOf("add_order", StringComparison.Ordinal) + 2, meta);
        Assert.Equal(NavigationTargetKind.SchemaObject, t!.Kind);
        Assert.Equal("ADD_ORDER", t.ObjectName);
        Assert.Equal(SymbolKind.Procedure, t.ObjectKind);
    }

    [Fact]
    public void AliasDeclaration_OpensTargetTable()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        const string sql = "select k.id from kontrahent k";
        // The alias `k` at end (its declaration).
        var t = At(sql, sql.Length - 1, meta);
        Assert.Equal(NavigationTargetKind.SchemaObject, t!.Kind);
        Assert.Equal("KONTRAHENT", t.ObjectName);
    }

    [Fact]
    public void AliasQualifier_OpensTargetTable()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "ID");
        const string sql = "select k.id from kontrahent k";
        var t = At(sql, sql.IndexOf("k.", StringComparison.Ordinal), meta); // the `k` before the dot
        Assert.Equal(NavigationTargetKind.SchemaObject, t!.Kind);
        Assert.Equal("KONTRAHENT", t.ObjectName);
    }

    [Fact]
    public void Column_OpensOwningTable()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql = "select k.nazwa from kontrahent k";
        var t = At(sql, sql.IndexOf("nazwa", StringComparison.Ordinal) + 1, meta);
        Assert.Equal(NavigationTargetKind.SchemaObject, t!.Kind);
        Assert.Equal("KONTRAHENT", t.ObjectName);
        Assert.Equal(SymbolKind.Table, t.ObjectKind);
    }

    [Fact]
    public void TableByName_NoMetadata_BestEffortByName()
    {
        // No metadata → the table isn't resolved, but a named FROM table still navigates by name.
        const string sql = "select * from kontrahent";
        var t = At(sql, sql.IndexOf("kontrahent", StringComparison.Ordinal) + 2, null);
        Assert.NotNull(t);
        Assert.Equal(NavigationTargetKind.SchemaObject, t!.Kind);
        Assert.Equal("KONTRAHENT", t.ObjectName);
    }

    [Fact]
    public void RecordAlias_OpensTriggerTable()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql = "create trigger tr for kontrahent before insert as begin if (new.nazwa is null) then exception; end";
        var t = At(sql, sql.IndexOf("new.nazwa", StringComparison.Ordinal), meta);
        Assert.Equal(NavigationTargetKind.SchemaObject, t!.Kind);
        Assert.Equal("KONTRAHENT", t.ObjectName);
    }

    // ── Locals → jump to declaration ───────────────────────────────────────────────────────────

    [Fact]
    public void VariableUse_JumpsToDeclaration()
    {
        const string sql = "create procedure p as declare variable total integer; begin total = 1; end";
        int declOffset = sql.IndexOf("total integer", StringComparison.Ordinal);
        int useOffset = sql.IndexOf("total = 1", StringComparison.Ordinal);
        var t = At(sql, useOffset + 1, null);

        Assert.NotNull(t);
        Assert.Equal(NavigationTargetKind.LocalDefinition, t!.Kind);
        Assert.NotNull(t.DefinitionSpan);
        Assert.Equal(declOffset, t.DefinitionSpan!.Value.Start);
    }

    [Fact]
    public void ParameterUse_JumpsToDeclaration()
    {
        const string sql = "create procedure p (id integer) as begin id = id + 1; end";
        int declOffset = sql.IndexOf("id integer", StringComparison.Ordinal);
        var t = At(sql, sql.IndexOf("id = id", StringComparison.Ordinal), null);
        Assert.Equal(NavigationTargetKind.LocalDefinition, t!.Kind);
        Assert.Equal(declOffset, t.DefinitionSpan!.Value.Start);
    }

    [Fact]
    public void CteReference_JumpsToDeclaration()
    {
        const string sql = "with t (a) as (select 1 from rdb$database) select a from t";
        int declOffset = sql.IndexOf("t (a)", StringComparison.Ordinal);
        var t = At(sql, sql.LastIndexOf("from t", StringComparison.Ordinal) + 5, null);
        Assert.NotNull(t);
        Assert.Equal(NavigationTargetKind.LocalDefinition, t!.Kind);
        Assert.Equal(declOffset, t.DefinitionSpan!.Value.Start);
    }

    // ── Reference span + local references ──────────────────────────────────────────────────────

    [Fact]
    public void ReferenceSpan_CoversTheIdentifier()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        const string sql = "select * from kontrahent";
        int start = sql.IndexOf("kontrahent", StringComparison.Ordinal);
        var t = At(sql, start + 3, meta);
        Assert.Equal(start, t!.ReferenceSpan.Start);
        Assert.Equal("kontrahent".Length, t.ReferenceSpan.Length);
    }

    [Fact]
    public void LocalReferences_GroupsAliasOccurrences()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "ID");
        const string sql = "select k.id from kontrahent k";
        var model = SemanticModel.Build(sql, meta);
        var refs = NavigationEngine.LocalReferences(model, sql.IndexOf("k.", StringComparison.Ordinal));
        // The alias appears twice: the qualifier `k.` and the declaration `kontrahent k`.
        Assert.True(refs.Count >= 2);
    }

    [Fact]
    public void LocalDefinition_Helper_ReturnsSpanForLocals_NullForObjects()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        const string objectSql = "select * from kontrahent";
        var objModel = SemanticModel.Build(objectSql, meta);
        Assert.Null(NavigationEngine.LocalDefinition(objModel, objectSql.IndexOf("kontrahent", StringComparison.Ordinal) + 2));

        const string localSql = "create procedure p as declare variable total integer; begin total = 1; end";
        var localModel = SemanticModel.Build(localSql);
        Assert.NotNull(NavigationEngine.LocalDefinition(localModel, localSql.IndexOf("total = 1", StringComparison.Ordinal)));
    }

    // ── Non-navigable ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void KeywordOrWhitespace_ReturnsNull()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        const string sql = "select * from kontrahent";
        Assert.Null(At(sql, 0, meta));                                     // "select"
        Assert.Null(At(sql, sql.IndexOf(" from", StringComparison.Ordinal), meta)); // whitespace
    }

    [Fact]
    public void UnresolvedBareColumn_NoMetadata_ReturnsNull()
    {
        // A bare column with no metadata doesn't resolve → nothing navigable.
        const string sql = "select nazwa from kontrahent k";
        Assert.Null(At(sql, sql.IndexOf("nazwa", StringComparison.Ordinal) + 1, null));
    }

    [Fact]
    public void GarbageInput_NeverThrows()
    {
        foreach (var sql in new[] { "", "   ", "select", "((((", "create procedure", "'x" })
        {
            var model = SemanticModel.Build(sql);
            for (int i = 0; i <= sql.Length; i++)
            {
                _ = NavigationEngine.TargetAt(model, i);
                _ = NavigationEngine.LocalReferences(model, i);
                _ = NavigationEngine.LocalDefinition(model, i);
                _ = NavigationEngine.GetLocalRename(model, i);
            }
        }
    }

    // ── Safe local rename (M5, §0 / §10) ─────────────────────────────────────────────────────────

    private static NavigationRename? Rename(string sql, int offset, ISqlMetadataProvider? meta = null)
        => NavigationEngine.GetLocalRename(SemanticModel.Build(sql, meta), offset);

    [Fact]
    public void GetLocalRename_Alias_ReturnsAllOccurrences()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "ID");
        const string sql = "select k.id from kontrahent k";
        // On the qualifier `k.`
        var rn = Rename(sql, sql.IndexOf("k.", StringComparison.Ordinal), meta);
        Assert.NotNull(rn);
        Assert.Equal(SymbolKind.TableReference, rn!.Kind);
        // Declaration `kontrahent k` + the qualifier `k.` → at least 2 occurrences.
        Assert.True(rn.Occurrences.Count >= 2);
    }

    [Fact]
    public void GetLocalRename_Variable_ReturnsDeclarationAndUses()
    {
        const string sql = "create procedure p as declare variable total integer; begin total = total + 1; end";
        var rn = Rename(sql, sql.IndexOf("total = total", StringComparison.Ordinal));
        Assert.NotNull(rn);
        Assert.Equal(SymbolKind.Variable, rn!.Kind);
        // declaration + two uses on the assignment line.
        Assert.True(rn.Occurrences.Count >= 3);
    }

    [Fact]
    public void GetLocalRename_Parameter_IsRenameable()
    {
        const string sql = "create procedure p (amount integer) as begin amount = amount + 1; end";
        var rn = Rename(sql, sql.IndexOf("amount = amount", StringComparison.Ordinal));
        Assert.NotNull(rn);
        Assert.Equal(SymbolKind.Parameter, rn!.Kind);
    }

    [Fact]
    public void GetLocalRename_Cte_IsRenameable()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "ID");
        const string sql = "with c as (select id from kontrahent) select * from c";
        var rn = Rename(sql, sql.IndexOf("c as", StringComparison.Ordinal), meta);
        Assert.NotNull(rn);
        Assert.Equal(SymbolKind.Cte, rn!.Kind);
    }

    [Fact]
    public void GetLocalRename_SchemaObject_ReturnsNull()
    {
        // A table referenced by its own name is NOT renameable (it denotes a DB object) — §0.
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        const string sql = "select * from kontrahent";
        Assert.Null(Rename(sql, sql.IndexOf("kontrahent", StringComparison.Ordinal) + 2, meta));
    }

    [Fact]
    public void GetLocalRename_Column_ReturnsNull()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "NAZWA");
        const string sql = "select k.nazwa from kontrahent k";
        Assert.Null(Rename(sql, sql.IndexOf("nazwa", StringComparison.Ordinal) + 1, meta));
    }

    [Fact]
    public void GetLocalRename_NewOldRecord_ReturnsNull()
    {
        var meta = new FakeMetadata().Object("ORDERS", SymbolKind.Table).Col("ORDERS", "STATUS");
        const string sql =
            "create trigger t for orders before update as begin new.status = old.status; end";
        Assert.Null(Rename(sql, sql.IndexOf("new.status", StringComparison.Ordinal) + 1, meta));
        Assert.Null(Rename(sql, sql.IndexOf("old.status", StringComparison.Ordinal) + 1, meta));
    }

    [Fact]
    public void GetLocalRename_KeywordOrWhitespace_ReturnsNull()
    {
        const string sql = "select * from kontrahent k";
        Assert.Null(Rename(sql, 0));                                            // "select"
        Assert.Null(Rename(sql, sql.IndexOf(" from", StringComparison.Ordinal))); // whitespace
    }
}
