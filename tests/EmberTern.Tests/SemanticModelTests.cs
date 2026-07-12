using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The Semantic Model / binder (Etap 4). Pins: the <b>two-phase Query binder</b> (the headline fix —
/// a column qualifier resolves against a FROM alias even though the SELECT list is textually before
/// FROM), local-scope binding (aliases, CTEs, PSQL variables/params, cursors, NEW/OLD), metadata-
/// backed column resolution, nested subquery/correlation scopes, DML target binding, error tolerance
/// (never throws on garbage/incomplete input), metadata-optional operation, and the public
/// offset-driven query API. Pure — no window, no DB (a fake <see cref="ISqlMetadataProvider"/>).
/// </summary>
public class SemanticModelTests
{
    // ── A tiny fluent fake metadata provider ─────────────────────────────────────────────────

    private sealed class FakeMetadata : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<RoutineParameterMetadata>> _params = new(StringComparer.OrdinalIgnoreCase);

        public FakeMetadata Object(string name, SymbolKind kind, string? description = null)
        {
            _objects[name] = new ObjectMetadata(name, kind, description);
            return this;
        }

        public FakeMetadata Col(string table, string name, string type)
        {
            if (!_objects.ContainsKey(table)) Object(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(new ColumnMetadata(name, type));
            return this;
        }

        public FakeMetadata Param(string routine, string name, string type, ParameterDirection dir)
        {
            if (!_params.TryGetValue(routine, out var list)) _params[routine] = list = new();
            list.Add(new RoutineParameterMetadata(name, type, dir));
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

    private static SemanticModel Build(string sql, ISqlMetadataProvider? meta = null)
        => SemanticModel.Build(sql, meta);

    private static SymbolReference? RefAt(SemanticModel m, string sql, string needle, int from = 0)
        => m.ReferenceAt(sql.IndexOf(needle, from, StringComparison.Ordinal));

    // ══ Multi-statement, no semicolons — analyse EVERY statement (QA Fix Sprint) ═════════════════

    // The reported bug: several independent statements in one editor, separated only by newlines (no
    // ';'). Only the FIRST was coloured/navigable because the ';'-only segmentation collapsed the whole
    // document into one statement. The lenient segmentation (semantic model only) must split them, so
    // objects in the LATER statements resolve too. Directly answers the user's questions: how many
    // statements the parser returns, and whether the model carries references from all of them.
    [Fact]
    public void MultipleStatements_WithoutSemicolons_AreEachAnalysed()
    {
        var meta = new FakeMetadata()
            .Object("SP_FIRST", SymbolKind.Procedure)
            .Object("MY_PROC", SymbolKind.Procedure)
            .Object("MY_VIEW", SymbolKind.View);
        const string sql =
            "execute procedure sp_first(:id)\n" +
            "execute procedure my_proc(:a, :b)\n" +
            "select status from my_proc(:a, :b)\n" +
            "select * from my_view";
        var model = Build(sql, meta);

        // #1 the parser (lenient) returns FOUR statements, not one merged blob.
        Assert.Equal(4, model.Syntax.Statements.Count);

        // #3 references from the LATER statements resolve — not just the first.
        // The view in the 4th statement:
        var view = RefAt(model, sql, "my_view");
        var viewSym = Assert.IsType<SchemaObjectSymbol>(view!.Symbol);
        Assert.Equal(SymbolKind.View, viewSym.Kind);

        // The selectable procedure used in FROM in the 3rd statement:
        int fromProc = sql.IndexOf("from my_proc", StringComparison.Ordinal) + "from ".Length;
        var proc = model.ReferenceAt(fromProc);
        var procSym = Assert.IsType<SchemaObjectSymbol>(proc!.Symbol);
        Assert.Equal(SymbolKind.Procedure, procSym.Kind);
    }

    // INSERT … SELECT must NOT be split at its SELECT source (a false boundary would break the INSERT).
    [Fact]
    public void InsertSelect_WithoutSemicolon_StaysOneStatement()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("S", "X", "INTEGER");
        const string sql = "insert into t (a) select x from s";
        var model = Build(sql, meta);
        Assert.Single(model.Syntax.Statements);
    }

    // ══ Headline: the two-phase Query binder ═════════════════════════════════════════════════

    // The reported bug: a column qualifier in the SELECT list must resolve against a FROM alias even
    // though the SELECT list is textually BEFORE the FROM. A single left-to-right pass could not do
    // this. Metadata-optional: the qualifier resolves to the table reference with no catalog at all.
    [Fact]
    public void QualifierBeforeFrom_ResolvesToTableAlias_WithoutMetadata()
    {
        const string sql = "select k.nazwa\nfrom kontrahent k";
        var m = Build(sql);

        var qualifier = RefAt(m, sql, "k."); // the "k" in "k.nazwa" (SELECT list, before FROM)
        Assert.NotNull(qualifier);
        Assert.Equal(ReferenceRole.Qualifier, qualifier!.Role);
        Assert.True(qualifier.IsResolved, "the qualifier must resolve — this is the two-phase fix");
        var tref = Assert.IsType<TableReferenceSymbol>(qualifier.Symbol);
        Assert.Equal("K", tref.Name);
        Assert.Equal("KONTRAHENT", tref.TargetName);
        Assert.True(tref.IsAlias);
    }

    [Fact]
    public void QualifierBeforeFrom_ResolvesColumn_WithMetadata()
    {
        const string sql = "select k.nazwa from kontrahent k";
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(80)");
        var m = Build(sql, meta);

        var col = RefAt(m, sql, "nazwa");
        Assert.NotNull(col);
        Assert.Equal(ReferenceRole.Column, col!.Role);
        var sym = Assert.IsType<ColumnSymbol>(col.Symbol);
        Assert.Equal("NAZWA", sym.Name);
        Assert.Equal("KONTRAHENT", sym.OwningTable);
        Assert.Equal("VARCHAR(80)", sym.DataType);
    }

    // ══ FROM / JOIN table references ═════════════════════════════════════════════════════════

    [Fact]
    public void TableWithoutAlias_IsReferencedByItsOwnName()
    {
        const string sql = "select * from kontrahent";
        var m = Build(sql);
        var def = m.AllSymbols.OfType<TableReferenceSymbol>().Single();
        Assert.Equal("KONTRAHENT", def.Name);
        Assert.Equal("KONTRAHENT", def.TargetName);
        Assert.False(def.IsAlias);
    }

    [Fact]
    public void KnownTable_ResolvesTargetToSchemaObject()
    {
        const string sql = "select * from kontrahent k";
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table, "customers");
        var m = Build(sql, meta);

        var tref = m.AllSymbols.OfType<TableReferenceSymbol>().Single();
        var obj = Assert.IsType<SchemaObjectSymbol>(tref.Target);
        Assert.Equal(SymbolKind.Table, obj.Kind);
        Assert.Equal("customers", obj.Description);

        // the table token itself is recorded as a schema-object reference
        var tableRef = RefAt(m, sql, "kontrahent");
        Assert.Equal(ReferenceRole.SchemaObject, tableRef!.Role);
        Assert.Same(obj, tableRef.Symbol);
    }

    [Fact]
    public void Join_BothTablesInScope_QualifiedColumnsResolve()
    {
        const string sql = "select n.id, k.nazwa from nagl n join kontrahent k on n.kid = k.id";
        var meta = new FakeMetadata()
            .Col("NAGL", "ID", "INTEGER").Col("NAGL", "KID", "INTEGER")
            .Col("KONTRAHENT", "ID", "INTEGER").Col("KONTRAHENT", "NAZWA", "VARCHAR(80)");
        var m = Build(sql, meta);

        var refs = m.AllSymbols.OfType<TableReferenceSymbol>().Select(r => r.Name).OrderBy(x => x);
        Assert.Equal(new[] { "K", "N" }, refs);

        // the qualified column k.nazwa resolves against the joined KONTRAHENT
        var col = m.ReferenceAt(sql.IndexOf("nazwa", StringComparison.Ordinal))!;
        var colSym = Assert.IsType<ColumnSymbol>(col.Symbol);
        Assert.Equal("KONTRAHENT", colSym.OwningTable);
    }

    // ══ Bare (unqualified) column resolution — high precision ════════════════════════════════

    [Fact]
    public void BareColumn_ResolvesWhenExactlyOneTableOwnsIt()
    {
        const string sql = "select nazwa from kontrahent k";
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(80)");
        var m = Build(sql, meta);

        var col = RefAt(m, sql, "nazwa")!;
        Assert.Equal(ReferenceRole.Column, col.Role);
        Assert.Equal("NAZWA", ((ColumnSymbol)col.Symbol!).Name);
    }

    [Fact]
    public void BareColumn_Ambiguous_IsRecordedUnresolved()
    {
        const string sql = "select id from nagl n, kontrahent k";
        var meta = new FakeMetadata().Col("NAGL", "ID", "INTEGER").Col("KONTRAHENT", "ID", "INTEGER");
        var m = Build(sql, meta);

        var col = RefAt(m, sql, "id")!;
        Assert.Equal(ReferenceRole.Column, col.Role);
        Assert.Null(col.Symbol); // ambiguous across two in-scope tables → unresolved
    }

    [Fact]
    public void BareColumn_WithoutMetadata_IsNotRecorded()
    {
        const string sql = "select nazwa from kontrahent k";
        var m = Build(sql); // no metadata → bare columns stay high-precision (unrecorded)
        Assert.Null(RefAt(m, sql, "nazwa"));
    }

    // ══ CTEs ═════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Cte_IsDeclared_AndReferencedFromMainFrom()
    {
        const string sql = "with recent as (select id from nagl) select * from recent r";
        var m = Build(sql);

        var cte = m.AllSymbols.OfType<CteSymbol>().Single();
        Assert.Equal("RECENT", cte.Name);
        Assert.NotNull(cte.QueryScope);

        // FROM recent → its table reference resolves its target to the CTE symbol
        var tref = m.AllSymbols.OfType<TableReferenceSymbol>().Single(r => r.TargetName == "RECENT");
        Assert.Same(cte, tref.Target);
    }

    // ══ Nested subquery scope + correlation ══════════════════════════════════════════════════

    [Fact]
    public void CorrelatedSubquery_InnerScopeSeesOuterTable()
    {
        const string sql =
            "select * from nagl a where a.id in (select b.nid from bruk b where b.nid = a.id)";
        var m = Build(sql);

        // there is a nested Query scope for the subquery
        int inside = sql.IndexOf("b.nid", StringComparison.Ordinal);
        var innerScope = m.ScopeAt(inside);
        Assert.Equal(ScopeKind.Query, innerScope.Kind);

        // the inner scope sees both b (local) and a (outer) — correlation
        var names = m.SymbolsInScope(inside).OfType<TableReferenceSymbol>().Select(r => r.Name).ToHashSet();
        Assert.Contains("B", names);
        Assert.Contains("A", names);

        // the correlated qualifier "a" (last occurrence, inside the subquery) resolves to the outer table
        int corr = sql.LastIndexOf("a.id", StringComparison.Ordinal);
        var q = m.ReferenceAt(corr)!;
        Assert.Equal(ReferenceRole.Qualifier, q.Role);
        Assert.Equal("A", ((TableReferenceSymbol)q.Symbol!).Name);
    }

    [Fact]
    public void DerivedTable_GetsItsOwnScope()
    {
        const string sql = "select * from (select id from nagl) d";
        var m = Build(sql);

        var derived = m.AllSymbols.OfType<TableReferenceSymbol>().Single(r => r.IsDerived);
        Assert.Equal("D", derived.Name);
        Assert.True(derived.IsAlias);

        // a child Query scope exists for the derived subquery
        Assert.Contains(m.RootScope.DescendantsAndSelf(), s => s.Kind == ScopeKind.Query
            && s.Symbols.OfType<TableReferenceSymbol>().Any(r => r.TargetName == "NAGL"));
    }

    // ══ DML ══════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Update_TargetAliasResolves()
    {
        const string sql = "update emp e set e.sal = 1 where e.id = 2";
        var m = Build(sql);

        var tref = m.AllSymbols.OfType<TableReferenceSymbol>().Single();
        Assert.Equal("E", tref.Name);
        Assert.Equal("EMP", tref.TargetName);

        var q = RefAt(m, sql, "e.sal")!;
        Assert.Equal(ReferenceRole.Qualifier, q.Role);
        Assert.Same(tref, q.Symbol);
    }

    [Fact]
    public void InsertSelect_BindsSourceTable_TwoPhase()
    {
        const string sql = "insert into dst (a) select s.a from src s";
        var meta = new FakeMetadata().Col("SRC", "A", "INTEGER");
        var m = Build(sql, meta);

        // the SELECT's source table is collected, and its qualified column resolves
        Assert.Contains(m.AllSymbols.OfType<TableReferenceSymbol>(), r => r.TargetName == "SRC");
        var col = m.ReferenceAt(sql.LastIndexOf("s.a", StringComparison.Ordinal) + 2)!; // the "a" after "s."
        Assert.Equal(ReferenceRole.Column, col.Role);
        Assert.Equal("SRC", ((ColumnSymbol)col.Symbol!).OwningTable);
    }

    [Fact]
    public void Delete_TargetFollowsFrom()
    {
        const string sql = "delete from emp e where e.id = 1";
        var m = Build(sql);
        var tref = m.AllSymbols.OfType<TableReferenceSymbol>().Single();
        Assert.Equal("E", tref.Name);
        Assert.Equal("EMP", tref.TargetName);
    }

    // ══ PSQL ═════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Procedure_DeclaresParamsVariables_AndBindsParamRefs()
    {
        const string sql =
            "create procedure p (in_id integer) returns (out_v integer) as\n" +
            "declare variable tmp integer;\n" +
            "begin\n" +
            "  tmp = :in_id;\n" +
            "  out_v = tmp;\n" +
            "  suspend;\n" +
            "end";
        var m = Build(sql);

        var scope = m.RootScope.DescendantsAndSelf().Single(s => s.Kind == ScopeKind.RoutineBody);
        Assert.Contains(scope.Symbols, s => s is ParameterSymbol { Name: "IN_ID", Direction: ParameterDirection.Input });
        Assert.Contains(scope.Symbols, s => s is ParameterSymbol { Name: "OUT_V", Direction: ParameterDirection.Output });
        Assert.Contains(scope.Symbols, s => s is VariableSymbol { Name: "TMP" });

        // :in_id use resolves to the input parameter
        var use = RefAt(m, sql, ":in_id")!;
        Assert.Equal(ReferenceRole.Parameter, use.Role);
        Assert.Equal("IN_ID", use.Symbol!.Name);
    }

    [Fact]
    public void Procedure_DeclaresCursor()
    {
        const string sql =
            "create procedure p as\n" +
            "declare c cursor for (select id from nagl);\n" +
            "begin\n  suspend;\nend";
        var m = Build(sql);
        var scope = m.RootScope.DescendantsAndSelf().Single(s => s.Kind == ScopeKind.RoutineBody);
        Assert.Contains(scope.Symbols, s => s is CursorSymbol { Name: "C" });
    }

    [Fact]
    public void Trigger_NewOldResolveToTableColumns()
    {
        const string sql =
            "create trigger tr for kontrahent active before insert position 0 as\n" +
            "begin\n  new.nazwa = old.nazwa;\nend";
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(80)");
        var m = Build(sql, meta);

        var scope = m.RootScope.DescendantsAndSelf().Single(s => s.Kind == ScopeKind.RoutineBody);
        Assert.Contains(scope.Symbols, s => s is RecordAliasSymbol { Name: "NEW", TargetTable: "KONTRAHENT" });
        Assert.Contains(scope.Symbols, s => s is RecordAliasSymbol { Name: "OLD", TargetTable: "KONTRAHENT" });

        // NEW qualifier resolves to the record alias; its member resolves to the table's column
        var q = RefAt(m, sql, "new.nazwa")!;
        Assert.Equal(ReferenceRole.RecordAlias, q.Role);
        var col = m.ReferenceAt(sql.IndexOf("new.nazwa", StringComparison.Ordinal) + 4)!;
        Assert.Equal("KONTRAHENT", ((ColumnSymbol)col.Symbol!).OwningTable);
    }

    [Fact]
    public void ExecuteBlock_DeclaresParamsAndBindsBody()
    {
        const string sql =
            "execute block (a integer = ?) returns (v integer) as\n" +
            "begin\n  v = :a;\nend";
        var m = Build(sql);
        var scope = m.RootScope.DescendantsAndSelf().Single(s => s.Kind == ScopeKind.RoutineBody);
        Assert.Contains(scope.Symbols, s => s is ParameterSymbol { Name: "A" });
        Assert.Contains(scope.Symbols, s => s is ParameterSymbol { Name: "V" });
    }

    [Fact]
    public void ExecuteProcedure_ReferencesTheProcedure()
    {
        const string sql = "execute procedure sp_recalc(1, 2)";
        var meta = new FakeMetadata().Object("SP_RECALC", SymbolKind.Procedure);
        var m = Build(sql, meta);
        var r = RefAt(m, sql, "sp_recalc")!;
        Assert.Equal(ReferenceRole.SchemaObject, r.Role);
        Assert.Equal(SymbolKind.Procedure, r.Symbol!.Kind);
    }

    [Fact]
    public void CreateView_BindsItsQuery_AndDeclaresTheView()
    {
        const string sql = "create view v (a) as select k.nazwa from kontrahent k";
        var m = Build(sql);

        Assert.Contains(m.AllSymbols, s => s is SchemaObjectSymbol { Kind: SymbolKind.View, Name: "V" });
        var q = RefAt(m, sql, "k.")!;
        Assert.Equal(ReferenceRole.Qualifier, q.Role);
        Assert.Equal("K", q.Symbol!.Name);
    }

    // ══ Public API: find-references + scope queries ══════════════════════════════════════════

    [Fact]
    public void ReferencesTo_GroupsAllOccurrencesOfATableAlias()
    {
        const string sql = "select k.a, k.b from kontrahent k where k.a > 0";
        var m = Build(sql);
        var tref = m.AllSymbols.OfType<TableReferenceSymbol>().Single();

        var occurrences = m.ReferencesTo(tref);
        // the FROM definition + three qualifier uses (k.a, k.b, k.a)
        Assert.Equal(4, occurrences.Count);
        Assert.Single(occurrences, o => o.IsDefinition);
        Assert.Equal(3, occurrences.Count(o => o.Role == ReferenceRole.Qualifier));
    }

    // P3 root cause: the caret sitting at the very END of a fully-typed identifier is the most
    // common Quick-Info / go-to / completion position and MUST resolve to that identifier. Half-open
    // containment excluded it (gotcha #198 for scopes; the same insight for references here).
    [Fact]
    public void ReferenceAt_IsInclusiveAtEndOfIdentifier()
    {
        const string sql = "select k.nazwa from kontrahent k";
        var m = Build(sql);

        // The trailing alias `k` sits at the very END of the statement — the caret at sql.Length
        // must resolve to it (half-open containment returned null here, breaking end-of-line P3).
        var tail = m.ReferenceAt(sql.Length);
        Assert.NotNull(tail);
        var tref = Assert.IsType<TableReferenceSymbol>(tail!.Symbol);
        Assert.Equal("K", tref.Name);

        // ...and the qualifier `k` in `k.nazwa` resolves at its end offset (right before the dot).
        int endOfQualifier = sql.IndexOf("k.", StringComparison.Ordinal) + 1;
        Assert.NotNull(m.ReferenceAt(endOfQualifier));
    }

    [Fact]
    public void ScopeAt_ReturnsDeepestContainingScope()
    {
        const string sql = "select * from a where x in (select 1 from b)";
        var m = Build(sql);
        var outer = m.ScopeAt(sql.IndexOf("from a", StringComparison.Ordinal));
        var inner = m.ScopeAt(sql.IndexOf("from b", StringComparison.Ordinal));
        Assert.Equal(ScopeKind.Query, outer.Kind);
        Assert.Equal(ScopeKind.Query, inner.Kind);
        Assert.NotSame(outer, inner);
        Assert.Same(outer, inner.Parent);
    }

    // ══ Error tolerance + metadata-optional ══════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    [InlineData("-- only a comment")]
    [InlineData("FROBNICATE THE WIDGET")]
    [InlineData("select 'unterminated")]
    [InlineData("create procedure p as begin")]        // unterminated body
    [InlineData("select k.nazwa from")]                 // truncated FROM
    [InlineData("update")]                               // bare keyword
    [InlineData("with c as ( select from")]              // broken CTE
    [InlineData(";;;")]
    public void NeverThrows_OnIncompleteOrGarbageInput(string sql)
    {
        var ex = Record.Exception(() =>
        {
            var m = Build(sql, new FakeMetadata());
            // exercise the query surface too — these must not throw either
            _ = m.ScopeAt(0);
            _ = m.ReferenceAt(0);
            _ = m.SymbolsInScope(0);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void EmptyScript_ProducesEmptyModel()
    {
        var m = Build("");
        Assert.Empty(m.AllSymbols);
        Assert.Empty(m.References);
        Assert.Equal(ScopeKind.Script, m.RootScope.Kind);
    }

    [Fact]
    public void WithoutMetadata_LocalScopeStillBinds_SchemaObjectsStayUnresolved()
    {
        const string sql = "select k.nazwa from kontrahent k";
        var m = Build(sql); // EmptyMetadataProvider

        // local alias binds
        var tref = m.AllSymbols.OfType<TableReferenceSymbol>().Single();
        Assert.Equal("K", tref.Name);
        Assert.Null(tref.Target); // no catalog → the underlying object is unresolved

        // the qualifier still resolves to the alias (local), the column stays unresolved
        Assert.True(RefAt(m, sql, "k.")!.IsResolved);
        Assert.False(m.ReferenceAt(sql.IndexOf("nazwa", StringComparison.Ordinal))!.IsResolved);
    }

    [Fact]
    public void MultiStatement_BindsEachStatement()
    {
        const string sql = "select a.x from t1 a; update t2 b set b.y = 1;";
        var m = Build(sql);
        var names = m.AllSymbols.OfType<TableReferenceSymbol>().Select(r => r.Name).OrderBy(x => x);
        Assert.Equal(new[] { "A", "B" }, names);
    }
}
