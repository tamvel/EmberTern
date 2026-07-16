using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language.Completion;
using EmberTern.Core.Sql.Language.Constructs;
using EmberTern.Core.Sql.Language.Ergonomics;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The CompletionEngine (Etap 5 / M2) — baseline candidate list from the Semantic Model + metadata
/// snapshot: keywords + every known schema object + the symbols in scope at the caret (aliases,
/// variables, parameters, CTEs, cursors, NEW/OLD). Pure Core, offline (a fake
/// <see cref="ISqlMetadataProvider"/>). Dot/column completion (M3) and positional ranking (M4) are
/// pinned in their own milestones.
/// </summary>
public class CompletionEngineTests
{
    // ── A tiny fluent fake metadata provider (mirrors SemanticModelTests) ─────────────────────
    private sealed class FakeMetadata : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);

        public FakeMetadata Object(string name, SymbolKind kind)
        {
            _objects[name] = new ObjectMetadata(name, kind);
            return this;
        }

        public FakeMetadata Col(string table, string name, string type)
            => Col(table, name, type, domain: null);

        public FakeMetadata Col(string table, string name, string type, string? domain)
        {
            if (!_objects.ContainsKey(table)) Object(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(new ColumnMetadata(name, type) { Domain = domain });
            return this;
        }

        public ObjectMetadata? FindObject(string name)
            => _objects.TryGetValue(name, out var o) ? o : null;

        public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
            => _cols.TryGetValue(tableOrView, out var c) ? c : Array.Empty<ColumnMetadata>();

        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
            => Array.Empty<RoutineParameterMetadata>();

        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    private static IReadOnlyList<CompletionItem> Complete(
        string sql, int offset, ISqlMetadataProvider? meta = null,
        CompletionTrigger trigger = CompletionTrigger.Explicit)
    {
        var model = SemanticModel.Build(sql, meta);
        return CompletionEngine.GetCompletions(model, offset, trigger).Items;
    }

    private static bool Has(IReadOnlyList<CompletionItem> items, string insert, CompletionItemKind kind)
        => items.Any(i => string.Equals(i.InsertText, insert, StringComparison.OrdinalIgnoreCase) && i.Kind == kind);

    // ── Keywords are always present ──────────────────────────────────────────────────────────

    [Fact]
    public void Keywords_AlwaysPresent_EvenWithoutMetadata()
    {
        var items = Complete("sel", 3);
        Assert.Contains(items, i => i.Kind == CompletionItemKind.Keyword);
        Assert.True(Has(items, "FROM", CompletionItemKind.Keyword));
    }

    // ── One responsibility, one owner ────────────────────────────────────────────────────────

    // The editor has three tools and each owns its own keystrokes: IntelliSense predicts NAMES, Language
    // Completion finishes CONSTRUCTS (Tab + a shown hint), Typing Ergonomics maintains PAIRS (begin…end).
    // A word another tool owns must not also appear here — otherwise the developer types "wher" and gets
    // WHERE in the list AND "⇥ where" beside it, or types "begin" and is offered a keyword that a pairing
    // rule is responsible for. Asserted over each owner's OWN declaration rather than a hand-written list
    // of words, so it keeps holding for rows added later — a copy kept in step by hand is what drifts.
    [Fact]
    public void ConstructTriggerWords_AreNotAlsoOfferedAsKeywords()
    {
        var items = Complete("select 1 from t ", 16);
        Assert.NotEmpty(LanguageConstructCatalog.OwnedWords);
        foreach (var owned in LanguageConstructCatalog.OwnedWords)
        {
            Assert.False(
                Has(items, owned, CompletionItemKind.Keyword),
                $"'{owned}' is owned by Language Completion and must not be an IntelliSense keyword too.");
        }
    }

    [Fact]
    public void KeywordPairWords_AreNotOfferedAsKeywords()
    {
        var items = Complete("select 1 from t ", 16);
        Assert.NotEmpty(KeywordPairCatalog.OwnedWords);
        foreach (var owned in KeywordPairCatalog.OwnedWords)
        {
            Assert.False(
                Has(items, owned, CompletionItemKind.Keyword),
                $"'{owned}' is a Typing Ergonomics delimiter pair and must not be an IntelliSense keyword.");
        }
    }

    // The converse, so the exclusion can never quietly widen into "IntelliSense lost its keywords":
    // everything no other tool owns is still offered, including the trailing words of multi-word
    // constructs (BY, INTO), which trigger nothing on their own.
    [Fact]
    public void UnownedKeywords_AreStillOffered()
    {
        var items = Complete("select 1 from t ", 16);
        foreach (var kw in new[] { "FROM", "JOIN", "ON", "AND", "VALUES", "CREATE", "BY", "INTO", "THEN", "DO" })
        {
            Assert.True(Has(items, kw, CompletionItemKind.Keyword), $"'{kw}' should still be offered.");
        }
    }

    // ── Loaded schema objects are listed ─────────────────────────────────────────────────────

    [Fact]
    public void LoadedObjects_ArePresent()
    {
        var meta = new FakeMetadata()
            .Object("KONTRAHENT", SymbolKind.Table)
            .Object("V_ORDERS", SymbolKind.View)
            .Object("SP_BALANCE", SymbolKind.Procedure);
        var items = Complete("select ", 7, meta);

        Assert.True(Has(items, "KONTRAHENT", CompletionItemKind.Table));
        Assert.True(Has(items, "V_ORDERS", CompletionItemKind.View));
        Assert.True(Has(items, "SP_BALANCE", CompletionItemKind.Procedure));
    }

    [Fact]
    public void UnknownKindObjects_AreSkipped()
    {
        // A user maps to SymbolKind.Unknown in the snapshot — not SQL-referenceable, so not listed.
        var meta = new FakeMetadata().Object("SYSDBA", SymbolKind.Unknown);
        var items = Complete("select ", 7, meta);
        Assert.DoesNotContain(items, i => string.Equals(i.InsertText, "SYSDBA", StringComparison.OrdinalIgnoreCase));
    }

    // ── In-scope symbols: query aliases ──────────────────────────────────────────────────────

    [Fact]
    public void Query_ListsFromAliases()
    {
        const string sql = "select  from kontrahent k join nagl n on n.id = k.id";
        // Caret in the (empty) SELECT list — the FROM aliases are in scope.
        var offset = sql.IndexOf("select ", StringComparison.Ordinal) + 7;
        var items = Complete(sql, offset);

        Assert.True(Has(items, "K", CompletionItemKind.TableAlias));
        Assert.True(Has(items, "N", CompletionItemKind.TableAlias));
    }

    // ── In-scope symbols: PSQL body variables + parameters ───────────────────────────────────

    [Fact]
    public void PsqlBody_ListsParametersAndVariables()
    {
        const string sql =
            "create procedure p (in_id integer) returns (out_v integer) as\n" +
            "declare variable tmp integer;\n" +
            "begin\n" +
            "  tmp = :in_id;\n" +
            "  out_v = tmp;\n" +
            "end";
        var offset = sql.IndexOf("tmp = :in_id", StringComparison.Ordinal);
        var items = Complete(sql, offset);

        Assert.True(Has(items, "IN_ID", CompletionItemKind.Parameter));
        Assert.True(Has(items, "OUT_V", CompletionItemKind.Parameter));
        Assert.True(Has(items, "TMP", CompletionItemKind.Variable));
    }

    [Fact]
    public void TriggerBody_ListsNewOldRecords()
    {
        const string sql =
            "create trigger tr for kontrahent active before insert position 0 as\n" +
            "begin\n" +
            "  new.id = 1;\n" +
            "end";
        var offset = sql.IndexOf("new.id", StringComparison.Ordinal);
        var items = Complete(sql, offset);

        Assert.True(Has(items, "NEW", CompletionItemKind.RecordAlias));
        Assert.True(Has(items, "OLD", CompletionItemKind.RecordAlias));
    }

    // ── Ordering: in-scope + objects rank above keywords ─────────────────────────────────────

    [Fact]
    public void InScopeAliases_And_Objects_RankAboveKeywords()
    {
        const string sql = "select  from kontrahent k";
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        var offset = sql.IndexOf("select ", StringComparison.Ordinal) + 7;
        var items = Complete(sql, offset, meta);

        double alias = items.First(i => i.Kind == CompletionItemKind.TableAlias).SortPriority;
        double table = items.First(i => i.Kind == CompletionItemKind.Table).SortPriority;
        double keyword = items.First(i => i.Kind == CompletionItemKind.Keyword).SortPriority;
        Assert.True(alias > keyword);
        Assert.True(table > keyword);
    }

    // ── Robustness ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";;;")]
    [InlineData("select from where )(")]
    [InlineData("create procedure p as begin")] // unterminated
    public void GarbageOrEmpty_NeverThrows_AndAlwaysHasKeywords(string sql)
    {
        var ex = Record.Exception(() =>
        {
            var items = Complete(sql, Math.Min(1, sql.Length));
            // Keywords are always available (the baseline never returns an empty dead list).
            Assert.Contains(items, i => i.Kind == CompletionItemKind.Keyword);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void NullModel_ReturnsEmpty()
    {
        var result = CompletionEngine.GetCompletions(null!, 0);
        Assert.True(result.IsEmpty);
    }

    // ── Dot / qualifier → columns (M3) ───────────────────────────────────────────────────────

    private static FakeMetadata KontrahentWithColumns() => new FakeMetadata()
        .Col("KONTRAHENT", "ID_KONTRAHENT", "INTEGER")
        .Col("KONTRAHENT", "NAZWA", "VARCHAR(80)")
        .Col("KONTRAHENT", "NIP", "VARCHAR(15)");

    [Fact]
    public void Dot_AliasQualifier_ListsColumns_AndTarget()
    {
        const string sql = "select k. from kontrahent k";
        var offset = sql.IndexOf("k.", StringComparison.Ordinal) + 2; // right after the dot
        var model = SemanticModel.Build(sql, KontrahentWithColumns());
        var result = CompletionEngine.GetCompletions(model, offset);

        Assert.True(result.IsDotContext);
        Assert.Equal("KONTRAHENT", result.DotTargetTable);
        Assert.True(Has(result.Items, "NAZWA", CompletionItemKind.Column));
        Assert.True(Has(result.Items, "NIP", CompletionItemKind.Column));
        Assert.All(result.Items, i => Assert.Equal(CompletionItemKind.Column, i.Kind));
        // Column detail carries the type (for the App's ": TYPE" suffix).
        Assert.Equal("VARCHAR(80)", result.Items.First(i => i.InsertText == "NAZWA").Detail);
    }

    [Theory]
    [InlineData("select * from kontrahent k where k.")]
    [InlineData("select *\nfrom kontrahent k\nwhere k.")]
    [InlineData("update kontrahent k set k.nazwa = 1 where k.")]
    public void Dot_AtEndOfStatement_ResolvesAliasColumns(string sql)
    {
        // Regression: a caret at the very end of a statement (the most common completion position)
        // must still resolve the FROM alias. The query scope's span ends at the trailing '.', so a
        // half-open ScopeAt would fall back to the Script scope and lose the alias (Target=<null>).
        var model = SemanticModel.Build(sql, KontrahentWithColumns());
        var result = CompletionEngine.GetCompletions(model, sql.Length, CompletionTrigger.Dot);

        Assert.True(result.IsDotContext);
        Assert.Equal("KONTRAHENT", result.DotTargetTable);
        Assert.True(Has(result.Items, "NAZWA", CompletionItemKind.Column));
    }

    [Fact]
    public void Dot_Column_CarriesRichSymbolWithDomain()
    {
        // P2: the engine attaches a rich ColumnSymbol (type + domain + owning table) so the App
        // renders "NAME : TYPE : DOMAIN" in the row and the full facts in the detail pane from one
        // source — no second lookup, no duplicated model.
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table)
            .Col("KONTRAHENT", "NRDOK", "VARCHAR(10)", "T_OZNNUMERACJI");
        const string sql = "select k. from kontrahent k";
        var offset = sql.IndexOf("k.", StringComparison.Ordinal) + 2;
        var result = CompletionEngine.GetCompletions(SemanticModel.Build(sql, meta), offset, CompletionTrigger.Dot);

        var item = result.Items.First(i => i.InsertText == "NRDOK");
        var col = Assert.IsType<ColumnSymbol>(item.Symbol);
        Assert.Equal("KONTRAHENT", col.OwningTable);
        Assert.Equal("T_OZNNUMERACJI", col.Domain);
        Assert.Equal("VARCHAR(10)", col.DataType);
    }

    [Fact]
    public void Dot_WithPartialPrefix_StillListsAllColumns()
    {
        const string sql = "select k.na from kontrahent k";
        var offset = sql.IndexOf("k.na", StringComparison.Ordinal) + 4; // after "na"
        var model = SemanticModel.Build(sql, KontrahentWithColumns());
        var result = CompletionEngine.GetCompletions(model, offset);

        Assert.True(result.IsDotContext);
        // The engine returns all columns; the App filters by the typed prefix.
        Assert.True(Has(result.Items, "NAZWA", CompletionItemKind.Column));
        Assert.True(Has(result.Items, "NIP", CompletionItemKind.Column));
    }

    [Fact]
    public void Dot_TableNameQualifier_ListsColumns()
    {
        const string sql = "select kontrahent. from kontrahent";
        var offset = sql.IndexOf("kontrahent.", StringComparison.Ordinal) + "kontrahent.".Length;
        var model = SemanticModel.Build(sql, KontrahentWithColumns());
        var result = CompletionEngine.GetCompletions(model, offset);

        Assert.True(result.IsDotContext);
        Assert.Equal("KONTRAHENT", result.DotTargetTable);
        Assert.True(Has(result.Items, "ID_KONTRAHENT", CompletionItemKind.Column));
    }

    [Fact]
    public void Dot_TriggerNewRecord_ListsTableColumns()
    {
        const string sql =
            "create trigger tr for kontrahent active before insert position 0 as\n" +
            "begin\n" +
            "  new. \n" +
            "end";
        var offset = sql.IndexOf("new.", StringComparison.Ordinal) + 4;
        var model = SemanticModel.Build(sql, KontrahentWithColumns());
        var result = CompletionEngine.GetCompletions(model, offset);

        Assert.True(result.IsDotContext);
        Assert.Equal("KONTRAHENT", result.DotTargetTable);
        Assert.True(Has(result.Items, "NAZWA", CompletionItemKind.Column));
    }

    [Fact]
    public void Dot_UnknownQualifier_ReturnsEmptyDotContext()
    {
        const string sql = "select zzz. from kontrahent k";
        var offset = sql.IndexOf("zzz.", StringComparison.Ordinal) + 4;
        var model = SemanticModel.Build(sql, KontrahentWithColumns());
        var result = CompletionEngine.GetCompletions(model, offset);

        Assert.True(result.IsDotContext);      // it IS a dot position…
        Assert.Null(result.DotTargetTable);    // …but the qualifier didn't resolve
        Assert.True(result.IsEmpty);           // so no baseline fallback after a "."
    }

    [Fact]
    public void Dot_ResolvedButNoCachedColumns_ReportsTargetForWarming()
    {
        // The alias resolves to KONTRAHENT, but no columns are cached for it yet — the App uses
        // DotTargetTable to warm the column cache, then re-runs.
        const string sql = "select k. from kontrahent k";
        var offset = sql.IndexOf("k.", StringComparison.Ordinal) + 2;
        var model = SemanticModel.Build(sql, new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table));
        var result = CompletionEngine.GetCompletions(model, offset);

        Assert.True(result.IsDotContext);
        Assert.Equal("KONTRAHENT", result.DotTargetTable);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void NotADotContext_ReturnsBaseline_NotDot()
    {
        // Caret between the qualifier and where a dot would go — plain identifier, baseline list.
        const string sql = "select k from kontrahent k";
        var offset = sql.IndexOf("select k", StringComparison.Ordinal) + "select k".Length;
        var result = CompletionEngine.GetCompletions(SemanticModel.Build(sql), offset);

        Assert.False(result.IsDotContext);
        Assert.Contains(result.Items, i => i.Kind == CompletionItemKind.Keyword);
    }

    // ── Positional context ranking (M4) ──────────────────────────────────────────────────────

    // The context boost is large (100) so a boosted kind clearly out-ranks the base priorities.
    // "boosted" ⇒ priority well above the baseline ceiling (~4); "not boosted" ⇒ at/near baseline.
    private static double PriorityOf(IReadOnlyList<CompletionItem> items, string insert, CompletionItemKind kind)
        => items.First(i => string.Equals(i.InsertText, insert, StringComparison.OrdinalIgnoreCase) && i.Kind == kind).SortPriority;

    [Fact]
    public void AfterFrom_TablesRankFirst()
    {
        const string sql = "select 1 from tab";
        var meta = new FakeMetadata().Object("TAB", SymbolKind.Table);
        var offset = sql.Length; // caret typing the table name after FROM
        var items = CompletionEngine.GetCompletions(SemanticModel.Build(sql, meta), offset).Items;

        Assert.True(PriorityOf(items, "TAB", CompletionItemKind.Table) > 50, "table should be boosted after FROM");
        // A keyword is not boosted here — the table out-ranks it decisively. (JOIN, not SELECT: construct
        // trigger words are owned by Language Completion and are no longer keyword items at all.)
        Assert.True(PriorityOf(items, "TAB", CompletionItemKind.Table)
                    > PriorityOf(items, "JOIN", CompletionItemKind.Keyword) + 10);
    }

    [Fact]
    public void ExpressionPosition_AliasBoosted_TableNot()
    {
        const string sql = "select  from tab k";
        var meta = new FakeMetadata().Object("TAB", SymbolKind.Table);
        var offset = sql.IndexOf("select ", StringComparison.Ordinal) + 7; // in the SELECT list
        var items = CompletionEngine.GetCompletions(SemanticModel.Build(sql, meta), offset).Items;

        Assert.True(PriorityOf(items, "K", CompletionItemKind.TableAlias) > 50, "alias boosted in expression position");
        Assert.True(PriorityOf(items, "TAB", CompletionItemKind.Table) < 50, "a table is not an expression value");
    }

    [Fact]
    public void AfterExecuteProcedure_ProceduresRankFirst()
    {
        const string sql = "execute procedure ";
        var meta = new FakeMetadata().Object("SP_X", SymbolKind.Procedure);
        var items = CompletionEngine.GetCompletions(SemanticModel.Build(sql, meta), sql.Length).Items;

        Assert.True(PriorityOf(items, "SP_X", CompletionItemKind.Procedure) > 50, "procedure boosted after EXECUTE PROCEDURE");
    }

    [Fact]
    public void CreateProcedure_DoesNotBoostProcedures()
    {
        // "PROCEDURE" also follows CREATE — but there the user names a NEW procedure, so existing
        // procedures must NOT be boosted (the boost is gated on the ExecuteProcedure statement kind).
        const string sql = "create procedure ";
        var meta = new FakeMetadata().Object("SP_X", SymbolKind.Procedure);
        var items = CompletionEngine.GetCompletions(SemanticModel.Build(sql, meta), sql.Length).Items;

        Assert.True(PriorityOf(items, "SP_X", CompletionItemKind.Procedure) < 50);
    }

    [Fact]
    public void NoAnchor_DegradesToBaseline_NothingBoosted()
    {
        const string sql = "select 1 from tab";
        var meta = new FakeMetadata().Object("TAB", SymbolKind.Table);
        var items = CompletionEngine.GetCompletions(SemanticModel.Build(sql, meta), 0).Items;

        Assert.All(items, i => Assert.True(i.SortPriority < 50, "no positional boost at offset 0"));
        Assert.Contains(items, i => i.Kind == CompletionItemKind.Keyword);
    }

    // ── Unqualified single-table column completion (implicit target, 2026-07-13) ─────────────

    private static FakeMetadata RozliczenieWithColumns() => new FakeMetadata()
        .Col("ROZLICZENIE", "ID_ROZLICZENIE", "INTEGER")
        .Col("ROZLICZENIE", "KWOTAWN", "NUMERIC(15,2)");

    [Fact]
    public void SingleTable_NoAlias_Where_OffersColumnsWithoutQualifier()
    {
        // The headline ask: FROM ROZLICZENIE WHERE | offers the table's columns with no alias/dot.
        const string sql = "select * from rozliczenie where ";
        var items = Complete(sql, sql.Length, RozliczenieWithColumns());
        Assert.True(Has(items, "ID_ROZLICZENIE", CompletionItemKind.Column));
        Assert.True(Has(items, "KWOTAWN", CompletionItemKind.Column));
    }

    [Fact]
    public void SingleTable_WithAlias_Where_OffersColumnsWithoutDot()
    {
        // The alias is neither required nor the discriminator — a single in-scope table is enough,
        // even before the user types a qualifier.
        const string sql = "select * from rozliczenie r where ";
        var items = Complete(sql, sql.Length, RozliczenieWithColumns());
        Assert.True(Has(items, "ID_ROZLICZENIE", CompletionItemKind.Column));
    }

    [Fact]
    public void SingleTable_Where_PartialColumnPrefix_OffersColumns()
    {
        const string sql = "select * from rozliczenie where id_ro";
        var items = Complete(sql, sql.Length, RozliczenieWithColumns());
        Assert.True(Has(items, "ID_ROZLICZENIE", CompletionItemKind.Column));
    }

    [Fact]
    public void SingleTable_SelectList_OffersColumns()
    {
        const string sql = "select  from rozliczenie";
        var offset = sql.IndexOf("select ", StringComparison.Ordinal) + 7; // caret in the SELECT list
        var items = Complete(sql, offset, RozliczenieWithColumns());
        Assert.True(Has(items, "KWOTAWN", CompletionItemKind.Column));
    }

    [Fact]
    public void SingleTable_ImplicitColumn_CarriesRichSymbol()
    {
        // Same rich ColumnSymbol as the dot path (one source), so the App renders the same facts.
        const string sql = "select * from rozliczenie where ";
        var items = Complete(sql, sql.Length, RozliczenieWithColumns());
        var item = items.First(i => i.InsertText == "ID_ROZLICZENIE" && i.Kind == CompletionItemKind.Column);
        var col = Assert.IsType<ColumnSymbol>(item.Symbol);
        Assert.Equal("ROZLICZENIE", col.OwningTable);
    }

    [Fact]
    public void TwoTables_DoesNotOfferUnqualifiedColumns()
    {
        // 2+ tables in scope → the user must qualify; no ambiguous name dump.
        var meta = new FakeMetadata()
            .Col("A", "AID", "INTEGER")
            .Col("B", "BID", "INTEGER");
        const string sql = "select * from a, b where ";
        var items = Complete(sql, sql.Length, meta);
        Assert.False(Has(items, "AID", CompletionItemKind.Column));
        Assert.False(Has(items, "BID", CompletionItemKind.Column));
    }

    [Fact]
    public void TablePosition_AfterJoin_DoesNotOfferColumns()
    {
        // Table position (after JOIN) is not an expression anchor — never offer columns there even
        // with a single table already in scope.
        const string sql = "select * from rozliczenie join ";
        var items = Complete(sql, sql.Length, RozliczenieWithColumns());
        Assert.False(Has(items, "ID_ROZLICZENIE", CompletionItemKind.Column));
    }

    [Fact]
    public void NoTableInScope_DoesNotOfferColumns()
    {
        // Expression position but no FROM table → nothing to imply.
        const string sql = "select  ";
        var items = Complete(sql, sql.IndexOf("select ", StringComparison.Ordinal) + 7, RozliczenieWithColumns());
        Assert.False(Has(items, "ID_ROZLICZENIE", CompletionItemKind.Column));
    }

    // ── No duplicate (name, kind) items ──────────────────────────────────────────────────────

    [Fact]
    public void Items_AreDedupedByNameAndKind()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table);
        var items = Complete("select ", 7, meta);
        var dupes = items
            .GroupBy(i => (i.InsertText.ToUpperInvariant(), i.Kind))
            .Where(g => g.Count() > 1)
            .ToList();
        Assert.Empty(dupes);
    }
}
