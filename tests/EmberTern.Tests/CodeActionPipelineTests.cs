using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.CodeActions;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage Q — the END-TO-END pipeline <c>SemanticModel → DiagnosticsEngine → QuickFixEngine</c> over
/// realistic queries, as the editor really runs it (lenient parse, live-shaped metadata).
/// <para>
/// Added after a QA report that "ET0005 quick fixes do not work": Q1 pinned the engine and Q2/Q3 pinned
/// their own pieces, but <b>nothing pinned the chain</b>, which is exactly the gap where a component can
/// be tested-but-uncalled and look like a regression later (gotcha #233). Tracing that report through
/// these stages is what proved the Core half sound and narrowed the fault to presentation.
/// </para>
/// </summary>
public class CodeActionPipelineTests
{
    private sealed class Meta : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);

        public Meta Col(string table, string name)
        {
            if (!_objects.ContainsKey(table)) _objects[table] = new ObjectMetadata(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
            list.Add(new ColumnMetadata(name, "INTEGER"));
            return this;
        }

        public ObjectMetadata? FindObject(string name) => _objects.TryGetValue(name, out var o) ? o : null;
        public IReadOnlyList<ColumnMetadata> GetColumns(string t)
            => _cols.TryGetValue(t, out var c) ? c : Array.Empty<ColumnMetadata>();
        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string r)
            => Array.Empty<RoutineParameterMetadata>();
        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    private static Meta TwoTablesSharingIdRozliczenie()
        => new Meta()
            .Col("ROZLICZENIE", "ID_ROZLICZENIE").Col("ROZLICZENIE", "KWOTA")
            .Col("POZYCJA", "ID_ROZLICZENIE").Col("POZYCJA", "ILOSC");

    [Theory]
    // explicit JOIN with aliases
    [InlineData("select id_rozliczenie from rozliczenie r join pozycja p on p.id_rozliczenie = r.id_rozliczenie", "r.", "p.")]
    // no aliases at all — the tables qualify by their own names
    [InlineData("select id_rozliczenie from rozliczenie join pozycja on 1 = 1", "rozliczenie.", "pozycja.")]
    // comma join, ambiguous column not first in the select list
    [InlineData("select r.kwota, id_rozliczenie from rozliczenie r, pozycja p", "r.", "p.")]
    public void AmbiguousColumn_ReachesTheEngine_AndYieldsOneQualificationPerTable(
        string sql, string firstQualifier, string secondQualifier)
    {
        var model = SemanticModel.Build(sql, TwoTablesSharingIdRozliczenie());

        var diagnostics = DiagnosticsEngine.Analyze(model);
        var ambiguous = Assert.Single(diagnostics, d => d.Category == DiagnosticCategory.AmbiguousColumn);
        Assert.Equal("ET0005", ambiguous.Code);
        Assert.Equal("id_rozliczenie", sql.Substring(ambiguous.Start, ambiguous.Length));

        var actions = QuickFixEngine.GetFixes(model, ambiguous);
        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, a => a.Title.Contains(firstQualifier + "id_rozliczenie", StringComparison.Ordinal));
        Assert.Contains(actions, a => a.Title.Contains(secondQualifier + "id_rozliczenie", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleTableQuery_CannotProduceAnAmbiguity_SoOffersNothing()
    {
        // The shape from the QA report: one table, an unknown column ⇒ ET0002, which has no producer
        // until Q4. Offering nothing here is correct behaviour, not a broken pipeline — pinned so the
        // distinction is not re-litigated from a screenshot.
        const string Sql = "select id_rozliczenie, id from rozliczenie";
        var model = SemanticModel.Build(Sql, TwoTablesSharingIdRozliczenie());

        var diagnostics = DiagnosticsEngine.Analyze(model);
        Assert.DoesNotContain(diagnostics, d => d.Category == DiagnosticCategory.AmbiguousColumn);
        foreach (var d in diagnostics) Assert.Empty(QuickFixEngine.GetFixes(model, d));
    }
}
