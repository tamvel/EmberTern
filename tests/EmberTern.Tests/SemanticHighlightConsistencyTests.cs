using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language.Highlighting;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// P5a consistency pins (design §28) — a schema object must be highlighted the SAME way regardless
/// of the statement kind or position it is used in. The reported symptom was "an object is coloured
/// in FROM but not in UPDATE / a trigger's FOR / …". Rather than eyeball it live, these tests drive
/// every statement kind through the real Semantic Model + classifier and assert the object identifier
/// resolves to <see cref="SemanticHighlightClass.SchemaObject"/> with the right kind — so any binder
/// gap (a position that fails to record the schema-object reference) is caught headlessly and pinned.
/// </summary>
public class SemanticHighlightConsistencyTests
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

    private static FakeMetadata Schema() => new FakeMetadata()
        .Object("KONTRAHENT", SymbolKind.Table).Col("KONTRAHENT", "ID").Col("KONTRAHENT", "NAZWA", "VARCHAR(50)")
        .Object("ZRODLO", SymbolKind.Table).Col("ZRODLO", "ID").Col("ZRODLO", "NAZWA", "VARCHAR(50)")
        .Object("ADD_ORDER", SymbolKind.Procedure);

    // Classify the object occurrence at the n-th (1-based) case-insensitive occurrence of `word`.
    private static SemanticHighlight ClassifyObject(string sql, string word, int occurrence = 1)
    {
        var model = SemanticModel.Build(sql, Schema());
        int idx = -1;
        for (int n = 0; n < occurrence; n++)
        {
            idx = sql.IndexOf(word, idx + 1, StringComparison.OrdinalIgnoreCase);
            Assert.True(idx >= 0, $"occurrence {occurrence} of '{word}' not found");
        }
        // Aim at the middle of the identifier so end-inclusive edge cases don't matter.
        var reference = model.ReferenceAt(idx + word.Length / 2);
        return SemanticHighlightClassifier.Classify(reference!);
    }

    private static void AssertTable(string sql, string word = "kontrahent", int occurrence = 1)
    {
        var h = ClassifyObject(sql, word, occurrence);
        Assert.Equal(SemanticHighlightClass.SchemaObject, h.Class);
        Assert.Equal(SymbolKind.Table, h.ObjectKind);
    }

    [Fact] public void Select_TableIsObject()      => AssertTable("select * from kontrahent");
    [Fact] public void Update_TableIsObject()      => AssertTable("update kontrahent set nazwa = 'x' where id = 1");
    [Fact] public void Insert_TableIsObject()      => AssertTable("insert into kontrahent (nazwa) values ('x')");
    [Fact] public void UpdateOrInsert_TableIsObject() => AssertTable("update or insert into kontrahent (id, nazwa) values (1, 'x')");
    [Fact] public void Delete_TableIsObject()      => AssertTable("delete from kontrahent where id = 1");

    [Fact]
    public void Merge_TargetAndSourceAreObjects()
    {
        const string sql = "merge into kontrahent k using zrodlo z on k.id = z.id when matched then update set k.nazwa = z.nazwa";
        AssertTable(sql, "kontrahent");
        AssertTable(sql, "zrodlo");
    }

    [Fact]
    public void ExecuteProcedure_ProcIsObject()
    {
        const string sql = "execute procedure add_order";
        var h = ClassifyObject(sql, "add_order");
        Assert.Equal(SemanticHighlightClass.SchemaObject, h.Class);
        Assert.Equal(SymbolKind.Procedure, h.ObjectKind);
    }

    // ══ The same five statement kinds INSIDE a routine body ══════════════════════════════════════
    //
    // ⚠⚠ THE GAP THIS SUITE HAD, and it is worth reading before adding a case to any consistency suite.
    // The five DML rows above were pinned at the TOP LEVEL, and the routine-body rows below were pinned
    // with a QUERY (a FOR SELECT / a scalar subquery). Two dimensions — statement KIND × whether it stands
    // in a routine BODY — and each was varied only while the other was held fixed, so the crossing was
    // never tested. That is exactly where the defect lived: until 2026-08-10 the PSQL body binder never
    // declared a DML statement's target, so `update kontrahent` resolved in a script and resolved NOTHING
    // in a procedure. The class doc promised "regardless of the statement kind or position" — and "position"
    // had silently meant "clause position", never "nesting".
    //
    // ⭐ A suite that varies one dimension at a time reads as exhaustive and is not. When a rule is stated
    // over two independent axes, cross them.

    [Fact] public void PsqlBody_Update_TableIsObject() =>
        AssertTable("create procedure p as begin update kontrahent set nazwa = 'x' where id = 1; end");

    [Fact] public void PsqlBody_Insert_TableIsObject() =>
        AssertTable("create procedure p as begin insert into kontrahent (nazwa) values ('x'); end");

    [Fact] public void PsqlBody_UpdateOrInsert_TableIsObject() =>
        AssertTable("create procedure p as begin update or insert into kontrahent (id, nazwa) values (1, 'x'); end");

    [Fact] public void PsqlBody_Delete_TableIsObject() =>
        AssertTable("create procedure p as begin delete from kontrahent where id = 1; end");

    [Fact]
    public void PsqlBody_Merge_TargetAndSourceAreObjects()
    {
        const string sql = "create procedure p as begin merge into kontrahent k using zrodlo z "
            + "on k.id = z.id when matched then update set k.nazwa = z.nazwa; end";
        AssertTable(sql, "kontrahent");
        AssertTable(sql, "zrodlo");
    }

    [Fact]
    public void PsqlBody_TriggerUpdate_TableIsObject() =>
        // A trigger body is the same BlockStatement tree; pinned separately because a trigger's own FOR
        // target is bound by a DIFFERENT path (above), so a green FOR row says nothing about its body.
        AssertTable("create trigger tr for zrodlo before insert as begin "
            + "update kontrahent set nazwa = 'x' where id = new.id; end", "kontrahent");

    [Fact]
    public void PsqlBody_UpdateInsideAForLoop_TableIsObject() =>
        // The reported shape: the DML sits in the DO block of a FOR SELECT, i.e. two levels down.
        AssertTable("create procedure p as declare variable i integer; begin "
            + "for select id from zrodlo into :i do begin "
            + "update kontrahent set nazwa = 'x' where id = :i; end end", "kontrahent");

    [Fact] public void ExecuteBlock_BodyTableIsObject() =>
        AssertTable("execute block returns (x integer) as begin for select id from kontrahent into :x do suspend; end");

    [Fact] public void ExecuteBlock_BodyUpdateTableIsObject() =>
        AssertTable("execute block as begin update kontrahent set nazwa = 'x' where id = 1; end");

    [Fact] public void CreateProcedure_BodyTableIsObject() =>
        AssertTable("create procedure p returns (x integer) as begin for select id from kontrahent into :x do begin x = id; end end");

    [Fact] public void CreateFunction_BodyTableIsObject() =>
        AssertTable("create function f returns integer as begin return (select count(*) from kontrahent); end");

    // The reported gap: a trigger's FOR <table> target must be coloured like any other table use.
    [Fact] public void CreateTrigger_ForTableIsObject() =>
        AssertTable("create trigger tr for kontrahent before insert as begin new.nazwa = 'x'; end");

    [Fact] public void CreateView_BodyTableIsObject() =>
        AssertTable("create view v as select id, nazwa from kontrahent");

    // A view in FROM must colour + navigate exactly like a table (both go through BindNamedTable →
    // ResolveObject). Pins that the binder records the schema-object reference given the metadata —
    // so the live "FROM view doesn't highlight" report is a metadata-availability/staleness issue
    // (the model built before the view's category finished loading), not a binder gap.
    [Fact]
    public void Select_FromView_ViewIsObject()
    {
        const string sql = "select * from myview";
        var model = SemanticModel.Build(sql, Schema().Object("MYVIEW", SymbolKind.View));
        int idx = sql.IndexOf("myview", StringComparison.OrdinalIgnoreCase);
        var reference = model.ReferenceAt(idx + 3);
        Assert.NotNull(reference);
        var h = SemanticHighlightClassifier.Classify(reference!);
        Assert.Equal(SemanticHighlightClass.SchemaObject, h.Class);
        Assert.Equal(SymbolKind.View, h.ObjectKind);
    }

    // A selectable procedure in FROM (SELECT … FROM PROC(:a, :b)) — the proc name must be a
    // schema-object reference too.
    [Fact]
    public void Select_FromSelectableProc_ProcIsObject()
    {
        const string sql = "select status, opisdd from myproc(:a, :b)";
        var model = SemanticModel.Build(sql, Schema().Object("MYPROC", SymbolKind.Procedure));
        int idx = sql.IndexOf("myproc", StringComparison.OrdinalIgnoreCase);
        var reference = model.ReferenceAt(idx + 3);
        Assert.NotNull(reference);
        var h = SemanticHighlightClassifier.Classify(reference!);
        Assert.Equal(SemanticHighlightClass.SchemaObject, h.Class);
        Assert.Equal(SymbolKind.Procedure, h.ObjectKind);
    }
}
