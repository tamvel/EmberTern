using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Hover;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The post-Stage-7 <see cref="HoverInfoEngine"/> — the ONE hover surface: the diagnostic explaining a
/// squiggle, the semantic Quick Info for a symbol, or both as sections of a single card. Pure Core,
/// offline (a fake <see cref="ISqlMetadataProvider"/>), no window.
/// <para>
/// The engine performs no analysis: the diagnostics are an <b>input</b>, so these tests feed it the real
/// <see cref="DiagnosticsEngine"/>'s output exactly the way the editor feeds it the cached list.
/// </para>
/// </summary>
public class HoverInfoEngineTests
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

        public FakeMetadata Col(string table, string name, string type)
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

    // Composes the way the editor does: build the model, run the engine once, hand the RESULT to the hover.
    private static HoverInfo? Hover(string sql, int offset, ISqlMetadataProvider? meta = null)
    {
        var model = SemanticModel.Build(sql, meta);
        return HoverInfoEngine.GetHover(model, DiagnosticsEngine.Analyze(model), offset);
    }

    // ══ The headline case — a squiggle explains itself ═══════════════════════════════════════

    /// <summary>
    /// THE reason this feature exists: hovering an unknown object shows why it is underlined.
    /// <para>
    /// It is also the trap that makes the feature non-obvious — the reference did NOT resolve, so there
    /// is no symbol and no Quick Info at all. A symbol-gated hover (which is what the Ctrl+hover tooltip
    /// was) returns nothing here, i.e. precisely where ET0001 fires. Hence the gate is
    /// "resolved symbol OR diagnostic".
    /// </para>
    /// </summary>
    [Fact]
    public void UnknownObject_HoverExplainsTheSquiggle_EvenWithNoQuickInfo()
    {
        var meta = new FakeMetadata().Object("KNOWN_PROC", SymbolKind.Procedure);
        const string sql = "execute procedure sp_missing(:x)";
        int offset = sql.IndexOf("sp_missing", StringComparison.Ordinal) + 2;

        var hover = Hover(sql, offset, meta);

        Assert.NotNull(hover);
        Assert.True(hover!.HasDiagnostics);
        Assert.Equal("ET0001", Assert.Single(hover.Diagnostics).Code);
        // The trap, pinned: no semantic section here — the whole point of not gating on the symbol.
        Assert.Null(hover.Info);
        Assert.Null(QuickInfoEngine_GetQuickInfo(sql, offset, meta));
    }

    private static object? QuickInfoEngine_GetQuickInfo(string sql, int offset, ISqlMetadataProvider meta)
        => EmberTern.Core.Sql.Language.QuickInfo.QuickInfoEngine.GetQuickInfo(SemanticModel.Build(sql, meta), offset);

    // ══ D7 — data tips (a DebugValue section fed by a lookup) ════════════════════════════════

    private const string DebugSql = "execute block as declare v integer; begin v = :v; end";

    [Fact]
    public void DebugValue_DataTip_ShownForVariable_WhenLookupProvided()
    {
        int offset = DebugSql.IndexOf(":v", StringComparison.Ordinal) + 1; // the V reference (colon form)
        var model = SemanticModel.Build(DebugSql);
        Func<string, DebugHoverValue?> lookup = name =>
            string.Equals(name, "V", StringComparison.OrdinalIgnoreCase) ? new DebugHoverValue("V", "42", false) : null;

        var hover = HoverInfoEngine.GetHover(model, DiagnosticsEngine.Analyze(model), offset, lookup);

        Assert.NotNull(hover);
        Assert.NotNull(hover!.DebugValue);
        Assert.Equal("42", hover.DebugValue!.ValueText);
    }

    [Fact]
    public void DebugValue_Absent_WithoutLookup()
    {
        int offset = DebugSql.IndexOf(":v", StringComparison.Ordinal) + 1;
        var model = SemanticModel.Build(DebugSql);

        var hover = HoverInfoEngine.GetHover(model, DiagnosticsEngine.Analyze(model), offset);

        Assert.Null(hover?.DebugValue); // hover itself may be null — either way, no data tip
    }

    [Fact]
    public void DebugValue_NotShown_ForNonVariableOffset()
    {
        // A type keyword, not a variable/parameter occurrence — the gate is the reference role, not the lookup.
        int offset = DebugSql.IndexOf("integer", StringComparison.Ordinal);
        var model = SemanticModel.Build(DebugSql);
        Func<string, DebugHoverValue?> always = _ => new DebugHoverValue("?", "should-not-appear", false);

        var hover = HoverInfoEngine.GetHover(model, DiagnosticsEngine.Analyze(model), offset, always);

        Assert.Null(hover?.DebugValue);
    }

    /// <summary>An undeclared local in a routine body — a local-scope diagnostic, no metadata needed.</summary>
    [Fact]
    public void UnresolvedVariable_HoverExplainsTheSquiggle()
    {
        const string sql = "create procedure loc returns (a integer) as begin a = :undeclared_one; end";
        int offset = sql.IndexOf("undeclared_one", StringComparison.Ordinal) + 1;

        var hover = Hover(sql, offset);

        Assert.NotNull(hover);
        Assert.Equal("ET0003", Assert.Single(hover!.Diagnostics).Code);
    }

    // ══ The semantic section still works on its own ══════════════════════════════════════════

    /// <summary>A clean, resolved symbol — today's Quick Info, now on plain hover (this absorbs P5d).</summary>
    [Fact]
    public void ResolvedSymbol_WithNoDiagnostic_ShowsQuickInfoOnly()
    {
        var meta = new FakeMetadata().Col("T", "X", "INTEGER");
        const string sql = "select k.x from t k";
        int offset = sql.IndexOf("from t", StringComparison.Ordinal) + 5;

        var hover = Hover(sql, offset, meta);

        Assert.NotNull(hover);
        Assert.NotNull(hover!.Info);
        Assert.False(hover.HasDiagnostics);
    }

    /// <summary>Nothing under the pointer ⇒ no card at all (never an empty popup).</summary>
    [Fact]
    public void OffsetWithNothingToSay_ReturnsNull()
    {
        var meta = new FakeMetadata().Col("T", "X", "INTEGER");

        // On the SELECT keyword — not an identifier, not squiggled.
        Assert.Null(Hover("select k.x from t k", 2, meta));
    }

    [Fact]
    public void NullModel_ReturnsNull()
        => Assert.Null(HoverInfoEngine.GetHover(null!, Array.Empty<Diagnostic>(), 0));

    [Fact]
    public void NullDiagnostics_AreTreatedAsEmpty_NotACrash()
    {
        const string sql = "select k.x from t k";
        var model = SemanticModel.Build(sql, new FakeMetadata().Col("T", "X", "INTEGER"));

        var hover = HoverInfoEngine.GetHover(model, null!, sql.IndexOf("from t", StringComparison.Ordinal) + 5);

        Assert.NotNull(hover);      // the semantic section still resolves
        Assert.False(hover!.HasDiagnostics);
    }

    // ══ Both sections ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// When a span has both, ONE hover carries both — never two competing popups.
    /// <para>
    /// Reaching this state takes some doing, which is itself the point: nearly every diagnostic sits on
    /// something UNRESOLVED (an unknown object/column/variable/cursor), so it has no Quick Info by
    /// construction. <c>ET0006</c> is the exception — it spans the VALUES list, which can legitimately
    /// contain resolved symbols. "Both sections" is the rare path, not the design centre.
    /// </para>
    /// </summary>
    [Fact]
    public void ResolvedSymbolInsideADiagnosticSpan_CarriesBothSections()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("T", "X", "INTEGER");
        // 2 columns vs 1 value ⇒ ET0006 over "((select x from t))"; the T inside the subquery resolves.
        const string sql = "insert into t (a, b) values ((select x from t))";
        int offset = sql.LastIndexOf("from t", StringComparison.Ordinal) + 5;

        var model = SemanticModel.Build(sql, meta);
        var diagnostics = DiagnosticsEngine.Analyze(model);
        Assert.Contains(diagnostics, d => d.Code == "ET0006");   // guards the fixture, not the hover

        var hover = HoverInfoEngine.GetHover(model, diagnostics, offset);

        Assert.NotNull(hover);
        Assert.True(hover!.HasDiagnostics);
        Assert.NotNull(hover.Info);
    }

    // ══ Offset conventions ═══════════════════════════════════════════════════════════════════

    /// <summary>Hit-testing is inclusive at the span end and mirrors <c>SemanticModel.ReferenceAt</c>
    /// (gotcha #198) — reusing the model's convention is what keeps the two sections agreeing about
    /// what "here" means.</summary>
    [Fact]
    public void DiagnosticHitTest_IsInclusiveAtSpanEnd()
    {
        const string sql = "create procedure loc returns (a integer) as begin a = :undeclared_one; end";
        var model = SemanticModel.Build(sql);
        var diagnostics = DiagnosticsEngine.Analyze(model);
        var d = Assert.Single(diagnostics);

        Assert.NotNull(HoverInfoEngine.GetHover(model, diagnostics, d.Start));       // at the start
        Assert.NotNull(HoverInfoEngine.GetHover(model, diagnostics, d.End));         // AT the end — inclusive
        Assert.Null(HoverInfoEngine.GetHover(model, diagnostics, d.End + 1));        // past it — nothing
    }

    /// <summary>The applicable span is the NARROWEST section's, so the App re-queries when the pointer
    /// leaves the thing being described rather than holding a stale card across a statement-wide span.</summary>
    [Fact]
    public void ApplicableSpan_IsTheNarrowestSection()
    {
        var meta = new FakeMetadata().Col("T", "A", "INTEGER").Col("T", "X", "INTEGER");
        const string sql = "insert into t (a, b) values ((select x from t))";
        int tableOffset = sql.LastIndexOf("from t", StringComparison.Ordinal) + 5;

        var model = SemanticModel.Build(sql, meta);
        var diagnostics = DiagnosticsEngine.Analyze(model);
        var mismatch = diagnostics.Single(d => d.Code == "ET0006");
        var hover = HoverInfoEngine.GetHover(model, diagnostics, tableOffset)!;

        // The wide ET0006 covers this table reference, but the card describes the reference — so moving
        // the pointer off it re-queries instead of holding a card that no longer matches.
        Assert.True(hover.Span.Length < mismatch.Length);
        Assert.True(hover.Span.Contains(tableOffset) || hover.Span.End == tableOffset);
    }

    // ══ No analysis — enforced by the signature ══════════════════════════════════════════════

    /// <summary>
    /// The engine is a pure lookup: it shows the diagnostics it was HANDED and never recomputes. Feeding
    /// it a list the analyser would not have produced proves it never consults the analyser — the
    /// no-new-analysis rule is structural (an input parameter), not a convention.
    /// </summary>
    [Fact]
    public void Diagnostics_AreAnInput_NeverRecomputed()
    {
        var model = SemanticModel.Build("select k.x from t k", new FakeMetadata().Col("T", "X", "INTEGER"));
        Assert.Empty(DiagnosticsEngine.Analyze(model));   // the real engine finds nothing here

        var injected = new[] { new Diagnostic(0, 6, DiagnosticSeverity.Error, "injected", "ZZ9999") };
        var hover = HoverInfoEngine.GetHover(model, injected, 2);

        Assert.Equal("ZZ9999", Assert.Single(hover!.Diagnostics).Code);
    }

    /// <summary>Diagnostics keep the engine's order, so the hover, the squiggles and the panel all
    /// present the same findings the same way.</summary>
    [Fact]
    public void OverlappingDiagnostics_KeepEngineOrder()
    {
        var model = SemanticModel.Build("select 1 from t");
        var ordered = new[]
        {
            new Diagnostic(0, 20, DiagnosticSeverity.Error, "first", "ET0006"),
            new Diagnostic(0, 20, DiagnosticSeverity.Warning, "second", "ET0009"),
        };

        var hover = HoverInfoEngine.GetHover(model, ordered, 3);

        Assert.Equal(new[] { "ET0006", "ET0009" }, hover!.Diagnostics.Select(d => d.Code));
    }
}
