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
    public void NoFixes_ForACategoryThatHasNoProducer()
    {
        // ET0006 has no producer and is not meant to: repairing an INSERT count mismatch needs to know
        // WHICH column or value the user meant to add or drop, which is unknowable (design §8). An
        // unhandled category must yield nothing, never an approximation from a neighbouring producer.
        var (_, actions) = FixesFor(
            "insert into kontrahent (id, nazwa) values (1)",
            new FakeMetadata().Col("KONTRAHENT", "ID").Col("KONTRAHENT", "NAZWA"),
            DiagnosticCategory.InsertCountMismatch);

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

    // ══ Q4 — "Did you mean …?" (ET0001/2/3/4) ════════════════════════════════════════════════
    //
    // Four categories, ONE producer: they differ only in where the candidate names come from. Adding
    // them touched QuickFixEngine and a new pure NameSuggestion — no UI, no applier, no diagnostics —
    // which is the extensibility claim Q1 made, tested rather than asserted.

    // ET0001 is emitted for an EXECUTE PROCEDURE of an unknown routine — an unknown table in FROM is
    // deliberately NOT flagged (the binder models it as an unresolved table reference, and the engine
    // stays silent). The fix shapes follow the diagnostic, not the other way round.
    [Fact]
    public void UnknownObject_OffersTheOneCloseCatalogName()
    {
        var (_, actions) = FixesFor(
            "execute procedure sp_kontrahen",
            new FakeMetadata().Object("SP_KONTRAHENT", SymbolKind.Procedure).Object("SP_TOWAR", SymbolKind.Procedure),
            DiagnosticCategory.UnknownObject);

        var action = Assert.Single(actions);
        // The catalog holds SP_KONTRAHENT; the user is writing in lower case, so the fix does too.
        Assert.Equal("Did you mean 'sp_kontrahent'?", action.Title);
        var edit = Assert.Single(action.Edits);
        Assert.Equal("sp_kontrahent", edit.NewText);
        Assert.Equal("sp_kontrahen", edit.ExpectedOldText);
    }

    [Fact]
    public void UnknownColumn_OffersAColumnOfTHATTable_NotTheWholeCatalog()
    {
        // NAZWAA is one edit from KONTRAHENT.NAZWA and also from TOWAR.NAZWA — but only the qualified
        // table's columns are candidates, so there is exactly one and it is offered.
        var meta = new FakeMetadata()
            .Col("KONTRAHENT", "NAZWA").Col("KONTRAHENT", "ID")
            .Col("TOWAR", "OPIS");

        var (_, actions) = FixesFor("select k.nazwaa from kontrahent k", meta, DiagnosticCategory.UnknownColumn);

        var action = Assert.Single(actions);
        Assert.Equal("Did you mean 'nazwa'?", action.Title);   // the user's case, not the catalog's
        Assert.DoesNotContain(actions, a => a.Title.Contains("opis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnresolvedVariable_OffersADeclaredLocalInScope()
    {
        const string Sql = "create procedure p returns (r integer) as declare variable v_total integer; begin r = :v_totl; end";

        var (_, actions) = FixesFor(Sql, null, DiagnosticCategory.UnresolvedVariable);

        var action = Assert.Single(actions);
        Assert.Equal("Did you mean 'v_total'?", action.Title);   // the user's case, not the catalog's
        var edit = Assert.Single(action.Edits);
        // The reference span INCLUDES the ':' sigil, and the replacement must keep it: inside an
        // embedded DSQL statement ':v' is a variable while 'v' is a COLUMN, so dropping it would
        // silently change what the code means.
        Assert.Equal(":v_totl", edit.ExpectedOldText);
        Assert.Equal(":v_total", edit.NewText);
    }

    [Theory]
    // A fix repairs the mistake and changes nothing else. Firebird folds unquoted identifiers, so the
    // catalog's spelling and the user's are the SAME name — importing the catalog's would be a
    // gratuitous restyling of their code, not part of the repair.
    [InlineData("v_zmiennax", ":v_zmienna")]
    [InlineData("V_ZMIENNAX", ":V_ZMIENNA")]
    [InlineData("V_ZmiennaX", ":V_Zmienna")]
    public void UnresolvedVariable_KeepsTheUsersCapitalisation(string typed, string expectedReplacement)
    {
        // The declaration is stored folded (V_ZMIENNA) — the suggestion must not import that spelling.
        var sql = "create procedure p returns (r integer) as declare variable v_zmienna integer; begin r = :"
                  + typed + "; end";

        var (_, actions) = FixesFor(sql, null, DiagnosticCategory.UnresolvedVariable);

        var edit = Assert.Single(Assert.Single(actions).Edits);
        Assert.Equal(expectedReplacement, edit.NewText);
    }

    // ── Silence: the half that protects the user's code ──────────────────────────────────────

    [Fact]
    public void NoSuggestion_WhenTwoCandidatesAreEquallyClose()
    {
        // KONTRAHENT_A and KONTRAHENT_B are both one edit away. The tool does not know which was meant,
        // so it says nothing rather than picking one.
        var (_, actions) = FixesFor(
            "execute procedure sp_kontrahent_",
            new FakeMetadata().Object("SP_KONTRAHENT_A", SymbolKind.Procedure).Object("SP_KONTRAHENT_B", SymbolKind.Procedure),
            DiagnosticCategory.UnknownObject);

        Assert.Empty(actions);
    }

    [Fact]
    public void NoSuggestion_WhenNothingIsCloseEnough()
    {
        var (_, actions) = FixesFor(
            "execute procedure zupelnie_inna_nazwa",
            new FakeMetadata().Object("SP_KONTRAHENT", SymbolKind.Procedure),
            DiagnosticCategory.UnknownObject);

        Assert.Empty(actions);
    }

    [Fact]
    public void NoSuggestion_ForAVeryShortName()
    {
        // At two characters almost anything is "one edit away" — a confident wrong rewrite is worse
        // than no offer.
        var (_, actions) = FixesFor(
            "execute procedure ab",
            new FakeMetadata().Object("AC", SymbolKind.Procedure),
            DiagnosticCategory.UnknownObject);

        Assert.Empty(actions);
    }

    [Theory]
    // The three single-edit shapes a typo actually takes, each driven through the real engine rather
    // than a helper: NameSuggestion is internal to Core on purpose, and its behaviour only matters here.
    [InlineData("sp_kontrahen", "sp_kontrahent")]     // deletion
    [InlineData("sp_kontrahentt", "sp_kontrahent")]   // insertion
    [InlineData("sp_kontrahenx", "sp_kontrahent")]    // substitution
    public void UnknownObject_RecognisesTheSingleEditTypoShapes(string typed, string expected)
    {
        var (_, actions) = FixesFor(
            "execute procedure " + typed,
            new FakeMetadata().Object("SP_KONTRAHENT", SymbolKind.Procedure),
            DiagnosticCategory.UnknownObject);

        Assert.Equal($"Did you mean '{expected}'?", Assert.Single(actions).Title);
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
