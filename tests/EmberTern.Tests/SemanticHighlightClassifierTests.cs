using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language.Highlighting;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The SemanticHighlightClassifier (Etap 6 / M3, design §9) — the pure Core half of semantic
/// highlighting: SymbolReference → {SchemaObject(kind) | Column | Local | None}. The App maps the
/// class to a theme brush and paints. Offline (a fake <see cref="ISqlMetadataProvider"/>).
/// </summary>
public class SemanticHighlightClassifierTests
{
    private sealed class FakeMetadata : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);

        public FakeMetadata Object(string name, SymbolKind kind) { _objects[name] = new ObjectMetadata(name, kind); return this; }
        public FakeMetadata Col(string t, string n, string ty = "INTEGER")
        {
            if (!_objects.ContainsKey(t)) Object(t, SymbolKind.Table);
            if (!_cols.TryGetValue(t, out var l)) _cols[t] = l = new();
            l.Add(new ColumnMetadata(n, ty));
            return this;
        }
        public ObjectMetadata? FindObject(string name) => _objects.TryGetValue(name, out var o) ? o : null;
        public IReadOnlyList<ColumnMetadata> GetColumns(string t) => _cols.TryGetValue(t, out var c) ? c : Array.Empty<ColumnMetadata>();
        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string r) => Array.Empty<RoutineParameterMetadata>();
        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    private static SemanticHighlight ClassifyAt(string sql, int offset, ISqlMetadataProvider? meta = null)
    {
        var model = SemanticModel.Build(sql, meta);
        var reference = model.ReferenceAt(offset);
        return SemanticHighlightClassifier.Classify(reference!);
    }

    // ── Schema objects colour by kind (reuse the tree palette) ────────────────────────────────

    [Fact]
    public void TableName_IsSchemaObject_Table()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        const string sql = "select * from kontrahent";
        var h = ClassifyAt(sql, sql.IndexOf("kontrahent", StringComparison.Ordinal) + 2, meta);
        Assert.Equal(SemanticHighlightClass.SchemaObject, h.Class);
        Assert.Equal(SymbolKind.Table, h.ObjectKind);
    }

    [Fact]
    public void ProcedureName_IsSchemaObject_Procedure()
    {
        var meta = new FakeMetadata().Object("ADD_ORDER", SymbolKind.Procedure);
        const string sql = "execute procedure add_order";
        var h = ClassifyAt(sql, sql.IndexOf("add_order", StringComparison.Ordinal) + 2, meta);
        Assert.Equal(SemanticHighlightClass.SchemaObject, h.Class);
        Assert.Equal(SymbolKind.Procedure, h.ObjectKind);
    }

    // ── Columns → Column ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Column_IsColumn()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql = "select k.nazwa from kontrahent k";
        var h = ClassifyAt(sql, sql.IndexOf("nazwa", StringComparison.Ordinal) + 1, meta);
        Assert.Equal(SemanticHighlightClass.Column, h.Class);
    }

    // ── Locals → Local ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Alias_IsLocal()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "ID");
        const string sql = "select k.id from kontrahent k";
        // The alias declaration `k` at the very end.
        var h = ClassifyAt(sql, sql.Length - 1, meta);
        Assert.Equal(SemanticHighlightClass.Local, h.Class);
    }

    [Fact]
    public void AliasQualifier_IsLocal()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "ID");
        const string sql = "select k.id from kontrahent k";
        var h = ClassifyAt(sql, sql.IndexOf("k.", StringComparison.Ordinal), meta);
        Assert.Equal(SemanticHighlightClass.Local, h.Class);
    }

    [Fact]
    public void VariableAndParameter_AreLocal()
    {
        const string sql = "create procedure p (id integer) as declare variable total integer; begin total = id; end";
        Assert.Equal(SemanticHighlightClass.Local, ClassifyAt(sql, sql.IndexOf("total = id", StringComparison.Ordinal), null).Class);
        Assert.Equal(SemanticHighlightClass.Local, ClassifyAt(sql, sql.IndexOf("= id", StringComparison.Ordinal) + 2, null).Class);
    }

    // ── Trigger context variables (NEW/OLD/INSERTING/UPDATING/DELETING) → ContextVariable ────────

    [Fact]
    public void RecordAlias_IsContextVariable()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql = "create trigger tr for kontrahent before insert as begin if (new.nazwa is null) then exception; end";
        var h = ClassifyAt(sql, sql.IndexOf("new.nazwa", StringComparison.Ordinal), meta);
        Assert.Equal(SemanticHighlightClass.ContextVariable, h.Class);
    }

    [Theory]
    [InlineData("inserting")]
    [InlineData("updating")]
    [InlineData("deleting")]
    public void TriggerPredicate_IsContextVariable(string predicate)
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        string sql = $"create trigger tr for kontrahent before insert or update or delete as begin if ({predicate}) then exception; end";
        var h = ClassifyAt(sql, sql.IndexOf("(" + predicate, StringComparison.Ordinal) + 1, meta);
        Assert.Equal(SemanticHighlightClass.ContextVariable, h.Class);
    }

    [Fact]
    public void TriggerBodyOnly_WithAmbientTriggerContext_ClassifiesContextVariables()
    {
        // The trigger DETAIL editor edits only the body ("begin … end") — no CREATE TRIGGER header —
        // so the model can't bind the trigger scope from the text. The VM seeds the trigger context
        // as AMBIENT symbols (NEW/OLD record aliases + INSERTING/UPDATING/DELETING predicates); this
        // pins that they then resolve and classify as ContextVariable, exactly as in the full text.
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        Symbol[] ambient =
        {
            new RecordAliasSymbol("NEW") { TargetTable = "KONTRAHENT" },
            new RecordAliasSymbol("OLD") { TargetTable = "KONTRAHENT" },
            new TriggerPredicateSymbol("INSERTING"),
            new TriggerPredicateSymbol("UPDATING"),
            new TriggerPredicateSymbol("DELETING"),
        };
        const string body = "begin if (updating) then exit; if (new.nazwa is null) then exit; end";
        var model = SemanticModel.Build(body, meta, ambient);

        var updating = model.ReferenceAt(body.IndexOf("updating", StringComparison.Ordinal) + 2);
        Assert.Equal(SemanticHighlightClass.ContextVariable, SemanticHighlightClassifier.Classify(updating!).Class);

        var neu = model.ReferenceAt(body.IndexOf("new.nazwa", StringComparison.Ordinal) + 1);
        Assert.Equal(SemanticHighlightClass.ContextVariable, SemanticHighlightClassifier.Classify(neu!).Class);
    }

    [Fact]
    public void TriggerPredicate_OutsideTrigger_IsNotColoured()
    {
        // INSERTING is not a reserved word — used as a bare identifier in a plain procedure body it
        // must NOT be mistaken for the trigger predicate (no trigger scope declares it there).
        const string sql = "create procedure p as declare variable inserting integer; begin inserting = 1; end";
        var model = SemanticModel.Build(sql);
        var reference = model.ReferenceAt(sql.IndexOf("inserting = 1", StringComparison.Ordinal));
        // It resolves as a local variable (declared), never as a context variable.
        Assert.Equal(SemanticHighlightClass.Local, SemanticHighlightClassifier.Classify(reference!).Class);
    }

    // ── Nothing to colour ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnresolvedReference_IsNone()
    {
        // A bare column with no metadata records no reference at all → Classify(null) is None.
        const string sql = "select nazwa from kontrahent";
        var model = SemanticModel.Build(sql);
        var reference = model.ReferenceAt(sql.IndexOf("nazwa", StringComparison.Ordinal) + 1);
        Assert.Equal(SemanticHighlightClass.None, SemanticHighlightClassifier.Classify(reference!).Class);
    }

    [Fact]
    public void ClassifySymbol_NullOrKeyword_IsNone()
    {
        Assert.Equal(SemanticHighlightClass.None, SemanticHighlightClassifier.ClassifySymbol(null).Class);
    }

    [Fact]
    public void EverySymbolKind_ClassifiesWithoutThrowing()
    {
        // Schema-object kinds → SchemaObject; column → Column; the rest handled or None.
        foreach (SymbolKind k in Enum.GetValues(typeof(SymbolKind)))
        {
            Symbol sym = k == SymbolKind.Column ? new ColumnSymbol("C") : new SchemaObjectSymbol(k, "X");
            _ = SemanticHighlightClassifier.ClassifySymbol(sym); // must not throw
        }
    }
}
