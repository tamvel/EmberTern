using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Language.Signatures;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The SignatureHelpEngine (Etap 5 / M6, design §8 / §5.10) — active-parameter help for call/DML
/// sites, produced purely from the Semantic Model + a fake <see cref="ISqlMetadataProvider"/>
/// (offline). Covers EXECUTE PROCEDURE (with/without parens), a function call in an expression,
/// INSERT column-list / VALUES / INSERT…SELECT, and UPDATE SET.
/// </summary>
public class SignatureHelpEngineTests
{
    // ── A fluent fake with routine parameters + columns ──────────────────────────────────────
    private sealed class FakeMetadata : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<RoutineParameterMetadata>> _routines = new(StringComparer.OrdinalIgnoreCase);

        public FakeMetadata Object(string name, SymbolKind kind)
        {
            _objects[name] = new ObjectMetadata(name, kind);
            return this;
        }

        public FakeMetadata Col(string table, string name, string type)
        {
            if (!_objects.ContainsKey(table)) Object(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(new ColumnMetadata(name, type));
            return this;
        }

        public FakeMetadata Proc(string name, params (string n, string t)[] inputs)
        {
            Object(name, SymbolKind.Procedure);
            _routines[name] = inputs.Select(i => new RoutineParameterMetadata(i.n, i.t, ParameterDirection.Input)).ToList();
            return this;
        }

        public FakeMetadata Func(string name, params (string n, string t)[] args)
        {
            Object(name, SymbolKind.Function);
            _routines[name] = args.Select(a => new RoutineParameterMetadata(a.n, a.t, ParameterDirection.Input)).ToList();
            return this;
        }

        public ObjectMetadata? FindObject(string name) => _objects.TryGetValue(name, out var o) ? o : null;
        public IReadOnlyList<ColumnMetadata> GetColumns(string t) => _cols.TryGetValue(t, out var c) ? c : Array.Empty<ColumnMetadata>();
        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string r) => _routines.TryGetValue(r, out var p) ? p : Array.Empty<RoutineParameterMetadata>();
        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    // Place a '|' at the caret; the engine is queried at that offset.
    private static SignatureInfo? Sig(string sqlWithCaret, ISqlMetadataProvider? meta = null)
    {
        int caret = sqlWithCaret.IndexOf('|');
        Assert.True(caret >= 0, "test SQL must contain a '|' caret marker");
        var sql = sqlWithCaret.Remove(caret, 1);
        var model = SemanticModel.Build(sql, meta);
        return SignatureHelpEngine.GetSignature(model, caret);
    }

    private static string[] Names(SignatureInfo s) => s.Parameters.Select(p => p.Name).ToArray();

    // ── EXECUTE PROCEDURE ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExecuteProcedure_WithParens_ActiveByComma()
    {
        var meta = new FakeMetadata().Proc("SP_ADD", ("A", "INTEGER"), ("B", "INTEGER"));
        var sig = Sig("execute procedure sp_add(1, 2|)", meta);
        Assert.NotNull(sig);
        Assert.Equal("SP_ADD", sig!.Label);
        Assert.Equal(SignatureKind.Procedure, sig.Kind);
        Assert.Equal(1, sig.ActiveParameter);
        Assert.Equal(new[] { "A", "B" }, Names(sig));
        Assert.Equal("INTEGER", sig.Parameters[0].Type);
    }

    [Fact]
    public void ExecuteProcedure_FirstArg_ActiveZero()
    {
        var meta = new FakeMetadata().Proc("SP_ADD", ("A", "INTEGER"), ("B", "INTEGER"));
        var sig = Sig("execute procedure sp_add(1|, 2)", meta);
        Assert.NotNull(sig);
        Assert.Equal(0, sig!.ActiveParameter);
    }

    [Fact]
    public void ExecuteProcedure_NoParens_ActiveByComma()
    {
        var meta = new FakeMetadata().Proc("SP_ADD", ("A", "INTEGER"), ("B", "INTEGER"));
        var sig = Sig("execute procedure sp_add 1, 2|", meta);
        Assert.NotNull(sig);
        Assert.Equal("SP_ADD", sig!.Label);
        Assert.Equal(1, sig.ActiveParameter);
    }

    [Fact]
    public void UnknownRoutine_NoSignature()
    {
        var meta = new FakeMetadata().Proc("SP_ADD", ("A", "INTEGER"));
        Assert.Null(Sig("execute procedure nope(1, 2|)", meta));
    }

    [Fact]
    public void NoMetadata_NoSignature_NoThrow()
        => Assert.Null(Sig("execute procedure sp_add(1, 2|)"));

    // ── Function call in an expression ───────────────────────────────────────────────────────

    [Fact]
    public void FunctionCall_InExpression()
    {
        var meta = new FakeMetadata().Func("MY_FN", ("X", "INTEGER"), ("Y", "INTEGER"));
        var sig = Sig("select my_fn(10, 20|) from rdb$database", meta);
        Assert.NotNull(sig);
        Assert.Equal("MY_FN", sig!.Label);
        Assert.Equal(SignatureKind.Function, sig.Kind);
        Assert.Equal(1, sig.ActiveParameter);
    }

    [Fact]
    public void NestedFunctionCall_InnermostWins()
    {
        var meta = new FakeMetadata().Func("MY_FN", ("X", "INTEGER")).Col("T", "A", "INTEGER");
        var sig = Sig("insert into t (a) values (my_fn(9|))", meta);
        Assert.NotNull(sig);
        Assert.Equal(SignatureKind.Function, sig!.Kind);
        Assert.Equal("MY_FN", sig.Label);
        Assert.Equal(0, sig.ActiveParameter);
    }

    // ── INSERT ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Insert_ValuesList_MapsToExplicitColumns()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("T", "B", "VARCHAR(10)");
        var sig = Sig("insert into t (a, b) values (1, 2|)", meta);
        Assert.NotNull(sig);
        Assert.Equal(SignatureKind.Insert, sig!.Kind);
        Assert.Equal("T", sig.Label);
        Assert.Equal(1, sig.ActiveParameter);
        Assert.Equal(new[] { "A", "B" }, Names(sig));
        Assert.Equal("VARCHAR(10)", sig.Parameters[1].Type);
    }

    [Fact]
    public void Insert_ColumnList_ShowsTableColumns()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("T", "B", "VARCHAR(10)").Col("T", "C", "DATE");
        var sig = Sig("insert into t (a, b|) values (1, 2)", meta);
        Assert.NotNull(sig);
        Assert.Equal(SignatureKind.Insert, sig!.Kind);
        Assert.Equal(1, sig.ActiveParameter);
        Assert.Equal(new[] { "A", "B", "C" }, Names(sig));
    }

    [Fact]
    public void Insert_NoExplicitColumns_UsesTableColumns()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("T", "B", "INTEGER").Col("T", "C", "INTEGER");
        var sig = Sig("insert into t values (10, 20|)", meta);
        Assert.NotNull(sig);
        Assert.Equal(1, sig!.ActiveParameter);
        Assert.Equal(new[] { "A", "B", "C" }, Names(sig));
    }

    [Fact]
    public void Insert_Select_ProjectionMapsToTargetColumns()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("T", "B", "INTEGER");
        var sig = Sig("insert into t (a, b) select x, y| from s", meta);
        Assert.NotNull(sig);
        Assert.Equal(SignatureKind.Insert, sig!.Kind);
        Assert.Equal(1, sig.ActiveParameter);
        Assert.Equal(new[] { "A", "B" }, Names(sig));
    }

    // UPDATE OR INSERT has the same INTO + column-list + VALUES shape as INSERT → same helper.
    [Fact]
    public void UpdateOrInsert_ValuesList_MapsToColumns()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("T", "B", "VARCHAR(10)");
        var sig = Sig("update or insert into t (a, b) values (1, 2|)", meta);
        Assert.NotNull(sig);
        Assert.Equal(SignatureKind.Insert, sig!.Kind);
        Assert.Equal("T", sig.Label);
        Assert.Equal(1, sig.ActiveParameter);
        Assert.Equal(new[] { "A", "B" }, Names(sig));
    }

    [Fact]
    public void InsertTarget_UpdateOrInsert_ReturnsTable()
        => Assert.Equal("T", InsertTarget("update or insert into t values (1, 2|)"));

    // ── UPDATE SET ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_Set_AssignmentActiveByComma()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("T", "B", "VARCHAR(5)");
        var sig = Sig("update t set a = 1, b = 2|", meta);
        Assert.NotNull(sig);
        Assert.Equal(SignatureKind.Update, sig!.Kind);
        Assert.Equal("T", sig.Label);
        Assert.Equal(1, sig.ActiveParameter);
        Assert.Equal(new[] { "A", "B" }, Names(sig));
        Assert.Equal("VARCHAR(5)", sig.Parameters[1].Type);
    }

    [Fact]
    public void Update_Set_FirstAssignment_ActiveZero()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("T", "B", "INTEGER");
        var sig = Sig("update t set a = 1|, b = 2", meta);
        Assert.NotNull(sig);
        Assert.Equal(0, sig!.ActiveParameter);
    }

    [Fact]
    public void Update_InWhere_NoSignature()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER");
        Assert.Null(Sig("update t set a = 1 where a = 9|", meta));
    }

    // ── TryGetInsertTargetTable — the warm hook behind the double-click INSERT/VALUES helper ────
    //
    // On a fresh editor the target table's columns aren't cached, so GetSignature returns null for an
    // INSERT paren (no column list to build) and the double-click helper would silently not appear.
    // TryGetInsertTargetTable returns the table name regardless, so the App can warm the columns,
    // rebuild, and retry GetSignature.

    private static string? InsertTarget(string sqlWithCaret, ISqlMetadataProvider? meta = null)
    {
        int caret = sqlWithCaret.IndexOf('|');
        Assert.True(caret >= 0, "test SQL must contain a '|' caret marker");
        var sql = sqlWithCaret.Remove(caret, 1);
        return SignatureHelpEngine.TryGetInsertTargetTable(SemanticModel.Build(sql, meta), caret);
    }

    [Fact]
    public void InsertTarget_ValuesParen_NoColumnsCached_StillReturnsTable()
    {
        // No metadata at all → GetSignature can't build the column list (returns null)…
        Assert.Null(Sig("insert into orders values (1, 2|)"));
        // …but the target table is still recoverable so the App can warm it.
        Assert.Equal("ORDERS", InsertTarget("insert into orders values (1, 2|)"));
    }

    [Fact]
    public void InsertTarget_ColumnListParen_ReturnsTable()
        => Assert.Equal("ORDERS", InsertTarget("insert into orders (a, b|) values (1, 2)"));

    [Fact]
    public void InsertTarget_InsertSelectProjection_ReturnsTable()
        => Assert.Equal("ORDERS", InsertTarget("insert into orders (a, b) select x, y| from s"));

    [Fact]
    public void InsertTarget_NestedFunctionArg_IsNotAnInsertPosition()
        => Assert.Null(InsertTarget("insert into orders values (my_fn(9|))"));

    [Theory]
    [InlineData("select * from t|")]
    [InlineData("update t set a = 1|")]
    [InlineData("insert into t| values (1)")] // caret on the table name, before any column/value paren
    public void InsertTarget_NonInsertPositions_ReturnNull(string sqlWithCaret)
        => Assert.Null(InsertTarget(sqlWithCaret));

    // ── Non-sites / robustness ───────────────────────────────────────────────────────────────

    [Fact]
    public void PlainSelect_NoSignature()
        => Assert.Null(Sig("select * from t|", new FakeMetadata().Col("T", "A", "INTEGER")));

    [Theory]
    [InlineData("|")]
    [InlineData("   |")]
    [InlineData("garbage (|")]
    [InlineData("execute procedure |")]
    [InlineData("insert into |")]
    public void GarbageOrIncomplete_NeverThrows(string sqlWithCaret)
    {
        var ex = Record.Exception(() => Sig(sqlWithCaret, new FakeMetadata()));
        Assert.Null(ex);
    }
}
