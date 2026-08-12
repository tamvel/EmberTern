using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Completion;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// A <b>selectable procedure standing where a table stands</b> — <c>FROM MY_PROC(:a) y</c> — and the fact the
/// language layer did not know: <b>its columns are its OUTPUT parameters.</b>
///
/// <para>⚠⚠ <c>ResolveColumn</c> asked <see cref="ISqlMetadataProvider.GetColumns"/>, which for a procedure is
/// legitimately empty, so <b>every</b> <c>y.column</c> came back unresolved. The reported symptom was a false
/// <c>ET0002 "unknown column"</c> on a procedure that compiles (2026-08-12) — but the measurement found the
/// quieter half to be worse: <b>completion after <c>y.</c> offered zero items</b>, and Quick Info and
/// navigation had nothing either. That is why the fix went into RESOLUTION
/// (<c>FromSourceColumns</c>) and not into the diagnostics engine, and why this class asserts across the
/// consumers rather than on the squiggle alone: a diagnostics-side fix would have made the squiggle test pass
/// while leaving three features broken.</para>
///
/// <para>⭐ The four negative cases are the ones that make the fix a fix rather than a mute button: a genuine
/// typo still fires · an INPUT parameter is NOT a column · an unwarmed parameter list is silent (never a
/// flicker) · and a plain table is untouched.</para>
/// </summary>
public class SelectableProcedureColumnsTests
{
    // ── A fake snapshot that can answer about parameters, including "not warmed yet" ──────────────
    private sealed class Meta : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<RoutineParameterMetadata>> _params = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _paramsPending = new(StringComparer.OrdinalIgnoreCase);

        public Meta Col(string table, string name, string type)
        {
            if (!_objects.ContainsKey(table)) _objects[table] = new ObjectMetadata(table, SymbolKind.Table);
            if (!_cols.TryGetValue(table, out var l)) _cols[table] = l = new();
            l.Add(new ColumnMetadata(name, type));
            return this;
        }

        public Meta Out(string routine, string name, string type) => Param(routine, name, type, ParameterDirection.Output);
        public Meta In(string routine, string name, string type) => Param(routine, name, type, ParameterDirection.Input);

        private Meta Param(string routine, string name, string type, ParameterDirection dir)
        {
            if (!_objects.ContainsKey(routine)) _objects[routine] = new ObjectMetadata(routine, SymbolKind.Procedure);
            if (!_params.TryGetValue(routine, out var l)) _params[routine] = l = new();
            l.Add(new RoutineParameterMetadata(name, type, dir));
            return this;
        }

        /// <summary>Declares a procedure whose PARAMETERS ARE NOT WARMED YET — the real snapshot's state until
        /// the warm pass runs, and the state that must produce silence rather than squiggles (S-2's rule).</summary>
        public Meta ProcedureWithParametersPending(string routine)
        {
            _objects[routine] = new ObjectMetadata(routine, SymbolKind.Procedure);
            _paramsPending.Add(routine);
            return this;
        }

        public ObjectMetadata? FindObject(string name) => _objects.TryGetValue(name, out var o) ? o : null;
        public IReadOnlyList<ColumnMetadata> GetColumns(string t)
            => _cols.TryGetValue(t, out var c) ? c : Array.Empty<ColumnMetadata>();
        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string r)
            => _params.TryGetValue(r, out var p) ? p : Array.Empty<RoutineParameterMetadata>();
        public bool KnowsRoutineParameters(string r) => !_paramsPending.Contains(r);
        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    // The reported routine's shape: one input, several outputs — plus a real table, so the table path is
    // exercised by the same snapshot and cannot be broken silently by the procedure path.
    private static Meta Catalog() => new Meta()
        .In("WYLICZ", "P_ID_MELDUNEK", "INTEGER")
        .Out("WYLICZ", "ID_MELDUNEK", "INTEGER")
        .Out("WYLICZ", "CZAS_ZAMELDOWANY", "NUMERIC(18,4)")
        .Out("WYLICZ", "CZAS_1SZT", "NUMERIC(18,4)")
        .Col("MELDUNEK", "ID_MELDUNEK", "INTEGER");

    private static IReadOnlyList<Diagnostic> Analyze(string sql, ISqlMetadataProvider meta)
        => DiagnosticsEngine.Analyze(SemanticModel.Build(sql, meta));

    // ══ The reported defect ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void QualifiedColumnsOfASelectableProcedure_AreNotReportedUnknown()
    {
        const string sql = "select y.id_meldunek, y.czas_zameldowany, y.czas_1szt from wylicz(:p) y";

        Assert.Empty(Analyze(sql, Catalog()));
    }

    /// <summary>
    /// ⭐ The same fact one layer deeper — the columns RESOLVE, which is what makes Quick Info, navigation and
    /// find-references work too. Asserting only the absence of a diagnostic would also pass if the engine had
    /// merely been silenced.
    /// </summary>
    [Fact]
    public void QualifiedColumnsOfASelectableProcedure_Resolve()
    {
        const string sql = "select y.czas_1szt from wylicz(:p) y";

        var model = SemanticModel.Build(sql, Catalog());
        var column = Assert.Single(model.References, r => r.Role == ReferenceRole.Column);

        Assert.True(column.IsResolved, "y.czas_1szt must resolve to the procedure's output parameter");
        var sym = Assert.IsType<ColumnSymbol>(column.Symbol);
        Assert.Equal("CZAS_1SZT", sym.Name);
        Assert.Equal("NUMERIC(18,4)", sym.DataType);
        Assert.Equal("WYLICZ", sym.OwningTable);
    }

    /// <summary>The quiet half of the report, and the reason the fix is in resolution: <c>y.</c> offered
    /// NOTHING before it.</summary>
    [Fact]
    public void Completion_AfterTheAlias_OffersTheProceduresOutputColumns()
    {
        var sql = "select y." + "\n" + "from wylicz(:p) y";
        int caret = sql.IndexOf("y.", StringComparison.Ordinal) + 2;

        var result = CompletionEngine.GetCompletions(
            SemanticModel.Build(sql, Catalog()), caret, CompletionTrigger.Dot);

        Assert.True(result.IsDotContext);
        var names = result.Items.Select(i => i.DisplayText).ToList();
        Assert.Equal(new[] { "CZAS_1SZT", "CZAS_ZAMELDOWANY", "ID_MELDUNEK" }, names.OrderBy(n => n, StringComparer.Ordinal));

        // The type travels with the item, as it does for a table column — one column model, many consumers.
        Assert.Contains(result.Items, i => i.DisplayText == "CZAS_1SZT" && i.Detail == "NUMERIC(18,4)");
    }

    /// <summary>A bare (unqualified) column over a single selectable-procedure source resolves by the same
    /// one-owner rule the binder applies to a single table.</summary>
    [Fact]
    public void BareColumnOverASelectableProcedure_Resolves()
    {
        const string sql = "select czas_1szt from wylicz(:p) y";

        Assert.Empty(Analyze(sql, Catalog()));
        var column = Assert.Single(
            SemanticModel.Build(sql, Catalog()).References, r => r.Role == ReferenceRole.Column);
        Assert.True(column.IsResolved);
    }

    // ══ The negatives — what stops this being a mute button ══════════════════════════════════════

    [Fact]
    public void AGenuineTypoOnASelectableProcedure_IsStillFlagged()
    {
        const string sql = "select y.nie_ma_takiej from wylicz(:p) y";

        var d = Assert.Single(Analyze(sql, Catalog()));
        Assert.Equal("ET0002", d.Code);
        Assert.Equal(DiagnosticCategory.UnknownColumn, d.Category);
    }

    /// <summary>
    /// ⛔ An INPUT parameter is an argument of the invocation, never a column of the result. Offering it would
    /// be a wrong answer, not merely a noisy one — so it must still read as unknown.
    /// </summary>
    [Fact]
    public void AnInputParameter_IsNotAColumnOfTheResult()
    {
        const string sql = "select y.p_id_meldunek from wylicz(:p) y";

        var d = Assert.Single(Analyze(sql, Catalog()));
        Assert.Equal("ET0002", d.Code);
    }

    /// <summary>
    /// ⭐⭐ Parameters are warmed lazily, exactly like columns — so "empty" is undecidable and the engine must
    /// stay silent until the snapshot says it KNOWS. Without this the fix would have reproduced S-2's
    /// "everything is underlined for a moment, then the errors disappear" for procedures.
    /// </summary>
    [Fact]
    public void WhileTheParameterListIsUnwarmed_NothingIsFlagged()
    {
        const string sql = "select y.czas_1szt, y.whatever from wylicz(:p) y";

        Assert.Empty(Analyze(sql, new Meta().ProcedureWithParametersPending("WYLICZ")));
    }

    /// <summary>The table path is untouched: a real typo on a real table still fires, and a real column on a
    /// real table still resolves. The procedure branch must not have become the answer for everything.</summary>
    [Fact]
    public void ThePlainTablePath_IsUnchanged()
    {
        Assert.Empty(Analyze("select m.id_meldunek from meldunek m", Catalog()));

        var d = Assert.Single(Analyze("select m.nie_ma_takiej from meldunek m", Catalog()));
        Assert.Equal("ET0002", d.Code);
    }

    /// <summary>
    /// ⚠ Firebird admits <c>FROM MY_NOARG_PROC</c> with no parentheses, which parses as a plain
    /// <c>TableReference</c> — indistinguishable from a table in the TEXT. That is why the fix keys on the
    /// resolved CATALOG target rather than on the AST's <c>RoutineTableReference</c>: the structural signal
    /// would have missed this shape entirely.
    /// </summary>
    [Fact]
    public void ASelectableProcedureInvokedWithoutParentheses_ResolvesTheSameWay()
    {
        const string sql = "select y.czas_1szt from wylicz y";

        Assert.Empty(Analyze(sql, Catalog()));
    }
}
