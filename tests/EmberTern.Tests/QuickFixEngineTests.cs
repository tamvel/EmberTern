using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.CodeActions;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage Q / Q1 — the pure-Core <see cref="QuickFixEngine"/> as a client of the
/// <see cref="SemanticModel"/> and of findings the <see cref="DiagnosticsEngine"/> already produced.
/// <para>
/// Two things are pinned, and the SECOND matters more: that a repair which can be named exactly is
/// offered exactly, and that everything else produces <b>silence</b>. A fix edits the user's code
/// (Architecture rule #11), so a wrong offer is far worse than a missing one — the silence tests are
/// the real contract.
/// </para>
/// <para>No window, no DB — a fake <see cref="ISqlMetadataProvider"/>, mirroring
/// <see cref="DiagnosticsEngineTests"/>.</para>
/// </summary>
public class QuickFixEngineTests
{
    // ── A tiny fluent fake metadata provider (mirrors DiagnosticsEngineTests) ─────────────────

    private sealed class FakeMetadata : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);

        public FakeMetadata Object(string name, SymbolKind kind)
        {
            _objects[name] = new ObjectMetadata(name, kind);
            return this;
        }

        public FakeMetadata Col(string table, string name, string type = "VARCHAR(50)")
        {
            if (!_objects.ContainsKey(table)) Object(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(new ColumnMetadata(name, type));
            return this;
        }

        public ObjectMetadata? FindObject(string name) => _objects.TryGetValue(name, out var o) ? o : null;

        public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
            => _cols.TryGetValue(tableOrView, out var c) ? c : Array.Empty<ColumnMetadata>();

        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
            => Array.Empty<RoutineParameterMetadata>();

        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    // The engine is driven exactly as the App drives it: analyse, then ask about a finding. Nothing is
    // hand-built, so a test can never assert against a diagnostic the engine would not really produce.
    private static (SemanticModel Model, IReadOnlyList<CodeAction> Actions) FixesFor(
        string sql, ISqlMetadataProvider? meta, DiagnosticCategory category)
    {
        var model = SemanticModel.Build(sql, meta);
        var diagnostic = DiagnosticsEngine.Analyze(model).FirstOrDefault(d => d.Category == category);
        return (model, QuickFixEngine.GetFixes(model, diagnostic));
    }

    // Two tables both carrying NAZWA: the binder cannot pick one, so the repair is "say which".
    private static FakeMetadata TwoTablesSharingNazwa()
        => new FakeMetadata()
            .Col("KONTRAHENT", "NAZWA")
            .Col("KONTRAHENT", "ID")
            .Col("TOWAR", "NAZWA")
            .Col("TOWAR", "ID");

    // ══ ET0005 — the offer ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AmbiguousColumn_OffersOneQualificationPerCandidateTable()
    {
        var (_, actions) = FixesFor(
            "select nazwa from kontrahent k join towar t on t.id = k.id",
            TwoTablesSharingNazwa(),
            DiagnosticCategory.AmbiguousColumn);

        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, a => a.Title.Contains("k.nazwa", StringComparison.Ordinal));
        Assert.Contains(actions, a => a.Title.Contains("t.nazwa", StringComparison.Ordinal));
    }

    [Fact]
    public void AmbiguousColumn_TheEditReplacesExactlyTheColumnSpan()
    {
        const string Sql = "select nazwa from kontrahent k join towar t on t.id = k.id";
        var (_, actions) = FixesFor(Sql, TwoTablesSharingNazwa(), DiagnosticCategory.AmbiguousColumn);

        var edit = Assert.Single(actions.First(a => a.Title.Contains("k.nazwa", StringComparison.Ordinal)).Edits);
        Assert.Equal(Sql.IndexOf("nazwa", StringComparison.Ordinal), edit.Start);
        Assert.Equal("nazwa".Length, edit.Length);
        Assert.Equal("k.nazwa", edit.NewText);

        // The drift guard must describe what is really there — otherwise the applier's whole safety net
        // is decorative.
        Assert.Equal("nazwa", edit.ExpectedOldText);
        Assert.Equal(edit.ExpectedOldText, Sql.Substring(edit.Start, edit.Length));
    }

    [Fact]
    public void AmbiguousColumn_KeepsTheCasingTheUserTyped()
    {
        // Symbol names are folded, so a naive fix would impose 'K.' on someone who wrote 'k'. Firebird
        // would accept it, but rewriting the user's casing is not the fix they asked for.
        const string Sql = "select Nazwa from kontrahent k join towar t on t.id = k.id";
        var (_, actions) = FixesFor(Sql, TwoTablesSharingNazwa(), DiagnosticCategory.AmbiguousColumn);

        var edit = Assert.Single(actions.First(a => a.Title.Contains("k.", StringComparison.Ordinal)).Edits);
        Assert.Equal("k.Nazwa", edit.NewText);
    }

    [Fact]
    public void AmbiguousColumn_ActionOrderIsDeterministic()
    {
        // The menu must not reshuffle between two identical asks (declaration order, always).
        const string Sql = "select nazwa from kontrahent k join towar t on t.id = k.id";
        var meta = TwoTablesSharingNazwa();

        var first = FixesFor(Sql, meta, DiagnosticCategory.AmbiguousColumn).Actions.Select(a => a.Title);
        var second = FixesFor(Sql, meta, DiagnosticCategory.AmbiguousColumn).Actions.Select(a => a.Title);

        Assert.Equal(first, second);
        Assert.Equal("Qualify as 'k.nazwa'", first.First());
    }

    // ══ Silence — the part that protects the user's code ══════════════════════════════════════

    [Fact]
    public void NoFixes_ForACategoryThatHasNoProducerYet()
    {
        // ET0001 has no producer until Q4. An unhandled category must yield nothing, never an
        // approximation from a neighbouring producer.
        var (_, actions) = FixesFor(
            "select * from nieistniejaca",
            new FakeMetadata().Col("KONTRAHENT", "NAZWA"),
            DiagnosticCategory.UnknownObject);

        Assert.Empty(actions);
    }

    [Fact]
    public void NoFixes_ForADefaultDiagnostic()
    {
        // FixesFor hands the engine `default` when the category was not produced — i.e. the App asking
        // about a finding that no longer exists. It must be a no-op, not an exception.
        var model = SemanticModel.Build("select 1 from rdb$database", new FakeMetadata());
        Assert.Empty(QuickFixEngine.GetFixes(model, default));
    }

    [Fact]
    public void NoFixes_ForANullModel()
    {
        Assert.Empty(QuickFixEngine.GetFixes(null!, default));
    }

    [Fact]
    public void NoFixes_WhenTheDiagnosticSpanDoesNotMatchTheModel()
    {
        // A stale diagnostic (built from older text) against the current model: the span no longer names
        // the reference it described. Offering an edit here would put text at an arbitrary offset — the
        // exact failure rule #11 exists to prevent.
        var model = SemanticModel.Build(
            "select nazwa from kontrahent k join towar t on t.id = k.id", TwoTablesSharingNazwa());
        var stale = new Diagnostic(
            3, 5, DiagnosticSeverity.Warning, "Ambiguous column 'nazwa'.", "ET0005",
            DiagnosticCategory.AmbiguousColumn);

        Assert.Empty(QuickFixEngine.GetFixes(model, stale));
    }

    [Fact]
    public void NoFixes_ForADerivedTable_WhoseColumnsCannotBeVerified()
    {
        // A column ambiguous between a real table and an aliased subquery: the subquery's projection is
        // not in the catalog, so "does it expose NAZWA?" cannot be answered without guessing. The real
        // table may still be offered; the derived one never is.
        var (_, actions) = FixesFor(
            "select nazwa from kontrahent k join (select nazwa from towar) d on 1 = 1",
            TwoTablesSharingNazwa(),
            DiagnosticCategory.AmbiguousColumn);

        // The positive half is what stops this from passing vacuously: if no ambiguity were detected at
        // all, "does not contain d." would hold for an empty list and prove nothing.
        Assert.Contains(actions, a => a.Title.Contains("k.", StringComparison.Ordinal));
        Assert.DoesNotContain(actions, a => a.Title.Contains("d.", StringComparison.Ordinal));
    }

    [Fact]
    public void NoFixes_ForATableThatDoesNotExposeTheColumn()
    {
        // Only tables that CERTAINLY carry the column are offered — a qualification that would not
        // compile is not a fix.
        var meta = new FakeMetadata()
            .Col("KONTRAHENT", "NAZWA").Col("KONTRAHENT", "ID")
            .Col("TOWAR", "NAZWA").Col("TOWAR", "ID")
            .Col("MAGAZYN", "ID");

        var (_, actions) = FixesFor(
            "select nazwa from kontrahent k join towar t on t.id = k.id join magazyn m on m.id = k.id",
            meta,
            DiagnosticCategory.AmbiguousColumn);

        Assert.Equal(2, actions.Count);
        Assert.DoesNotContain(actions, a => a.Title.Contains("m.", StringComparison.Ordinal));
    }

    // ══ Shape ════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EveryOfferedActionIsAtomicAndWellFormed()
    {
        var (_, actions) = FixesFor(
            "select nazwa from kontrahent k join towar t on t.id = k.id",
            TwoTablesSharingNazwa(),
            DiagnosticCategory.AmbiguousColumn);

        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Title));
            Assert.NotEmpty(action.Edits);
            foreach (var edit in action.Edits)
            {
                Assert.True(edit.Start >= 0);
                Assert.True(edit.Length >= 0);
                Assert.Equal(edit.Start + edit.Length, edit.End);
            }
        }
    }
}
