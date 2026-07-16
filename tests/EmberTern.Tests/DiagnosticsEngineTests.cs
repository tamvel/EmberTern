using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage 7 (Diagnostics) — Milestone S1. The pure-Core <see cref="DiagnosticsEngine"/> as a client of the
/// <see cref="SemanticModel"/>: it emits <c>UnknownObject</c> / <c>UnknownColumn</c> (metadata-gated) and
/// unresolved variable/parameter (routine-body-gated), stays silent under
/// <see cref="EmptyMetadataProvider"/>, excludes ambiguous columns and host parameters (the
/// "prefer silence over false positives" rule), and returns deterministic, de-duplicated, cancellable
/// results. No window, no DB — a fake <see cref="ISqlMetadataProvider"/>.
/// </summary>
public class DiagnosticsEngineTests
{
    // ── A tiny fluent fake metadata provider (mirrors SemanticModelTests) ─────────────────────

    private sealed class FakeMetadata : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<RoutineParameterMetadata>> _params = new(StringComparer.OrdinalIgnoreCase);

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

        public ObjectMetadata? FindObject(string name)
            => _objects.TryGetValue(name, out var o) ? o : null;

        public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
            => _cols.TryGetValue(tableOrView, out var c) ? c : Array.Empty<ColumnMetadata>();

        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
            => _params.TryGetValue(routine, out var p) ? p : Array.Empty<RoutineParameterMetadata>();

        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    private static IReadOnlyList<Diagnostic> Analyze(string sql, ISqlMetadataProvider? meta = null)
        => DiagnosticsEngine.Analyze(SemanticModel.Build(sql, meta));

    // ══ UnknownObject ════════════════════════════════════════════════════════════════════════

    // A referenced schema object the live metadata does not know (an unresolved SchemaObject ref — the
    // binder records one for EXECUTE PROCEDURE of an unknown procedure).
    [Fact]
    public void UnknownObject_UnknownProcedure_IsFlagged()
    {
        var meta = new FakeMetadata().Object("KNOWN_PROC", SymbolKind.Procedure);
        const string sql = "execute procedure sp_missing(:x)";

        var d = Assert.Single(Analyze(sql, meta));
        Assert.Equal(DiagnosticCategory.UnknownObject, d.Category);
        Assert.Equal("ET0001", d.Code);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        // Span points exactly at the offending name.
        Assert.Equal(sql.IndexOf("sp_missing", StringComparison.Ordinal), d.Start);
        Assert.Equal("sp_missing".Length, d.Length);
    }

    [Fact]
    public void UnknownObject_KnownProcedure_IsNotFlagged()
    {
        var meta = new FakeMetadata().Object("SP_OK", SymbolKind.Procedure);
        Assert.Empty(Analyze("execute procedure sp_ok(:x)", meta));
    }

    // ══ UnknownColumn ══════════════════════════════════════════════════════════════════════════

    // Table resolved correctly, column not: a qualified reference alias.col on a known table whose
    // column set genuinely lacks the column.
    [Fact]
    public void UnknownColumn_QualifiedColumnMissingOnResolvedTable_IsFlagged()
    {
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql = "select k.nazwa, k.qty from kontrahent k";

        var d = Assert.Single(Analyze(sql, meta));
        Assert.Equal(DiagnosticCategory.UnknownColumn, d.Category);
        Assert.Equal("ET0002", d.Code);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(sql.IndexOf("qty", StringComparison.Ordinal), d.Start);
        Assert.Equal("qty".Length, d.Length);
    }

    // The known column on the same table must never be flagged.
    [Fact]
    public void UnknownColumn_KnownColumn_IsNotFlagged()
    {
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        Assert.Empty(Analyze("select k.nazwa from kontrahent k", meta));
    }

    // Guard the conservatism boundary: an AMBIGUOUS bare column (present on ≥2 FROM tables) must NEVER be
    // reported as UnknownColumn (it is AmbiguousColumn — S2). This pins the discriminator so the two
    // categories never cross-contaminate.
    [Fact]
    public void UnknownColumn_AmbiguousBareColumn_IsNotReportedAsUnknownColumn()
    {
        var meta = new FakeMetadata()
            .Col("A", "ID", "INTEGER")
            .Col("B", "ID", "INTEGER");
        var diags = Analyze("select id from a, b", meta);
        Assert.DoesNotContain(diags, d => d.Category == DiagnosticCategory.UnknownColumn);
    }

    // The column can't be checked when the TABLE itself is unknown — stay silent (no cascade).
    [Fact]
    public void UnknownColumn_UnknownTable_IsNotFlagged()
    {
        var meta = new FakeMetadata().Object("KONTRAHENT", SymbolKind.Table); // no columns for NOSUCH
        Assert.Empty(Analyze("select n.qty from nosuch n", meta));
    }

    // ══ Unresolved variable / parameter (local scope, no connection needed) ═══════════════════

    // An undeclared :name inside a PSQL routine body binds to nothing — flagged as an unresolved
    // variable (Firebird references a variable/parameter identically at the use site, so the binder
    // tags an undeclared one as a variable).
    [Fact]
    public void UnresolvedVariable_UndeclaredLocalInRoutineBody_IsFlagged()
    {
        const string sql = "execute block as begin v = :undeclared; end";

        var d = Assert.Single(Analyze(sql)); // no metadata needed
        Assert.Equal(DiagnosticCategory.UnresolvedVariable, d.Category);
        Assert.Equal("ET0003", d.Code);
        Assert.Equal(sql.IndexOf(":undeclared", StringComparison.Ordinal), d.Start);
    }

    // Declared parameters and variables resolve and must produce NO diagnostics — the parameter path is
    // silent when correct. (An undeclared :b in the same body is the one flagged.)
    [Fact]
    public void Parameters_DeclaredParametersResolve_OnlyUndeclaredIsFlagged()
    {
        const string sql =
            "create procedure calc (a integer)\n" +
            "returns (r integer)\n" +
            "as\n" +
            "begin\n" +
            "  r = a + :b;\n" +
            "end";

        var diags = Analyze(sql); // local scope — no metadata
        var d = Assert.Single(diags);
        // The declared input parameter `a` and output `r` resolve (not flagged); only :b is unresolved.
        Assert.Equal(DiagnosticCategory.UnresolvedVariable, d.Category);
        Assert.Equal(sql.IndexOf(":b", StringComparison.Ordinal), d.Start);
    }

    // A `:name` OUTSIDE a routine body is a host/bind parameter, never a diagnostic. A bare
    // EXECUTE PROCEDURE … RETURNING_VALUES :a records an unresolved local ref in SCRIPT scope; the
    // routine-body gate must exclude it.
    [Fact]
    public void HostParameter_InBareExecuteProcedure_IsNotFlagged()
    {
        var meta = new FakeMetadata().Object("P", SymbolKind.Procedure);
        var diags = Analyze("execute procedure p returning_values :a", meta);

        Assert.DoesNotContain(diags, x =>
            x.Category is DiagnosticCategory.UnresolvedVariable or DiagnosticCategory.UnresolvedParameter);
    }

    // ══ EmptyMetadataProvider — silence for connection-gated categories ═══════════════════════

    // With no metadata every schema object and column is unresolved by construction; the engine must
    // emit NO UnknownObject / UnknownColumn diagnostics.
    [Fact]
    public void EmptyMetadataProvider_EmitsNoObjectOrColumnDiagnostics()
    {
        const string sql =
            "select k.qty from kontrahent k;\n" +
            "execute procedure sp_missing(:x)";

        // Default overload uses EmptyMetadataProvider.
        Assert.Empty(DiagnosticsEngine.Analyze(SemanticModel.Build(sql)));
    }

    // Local-scope diagnostics still fire without any metadata (they don't need a connection).
    [Fact]
    public void EmptyMetadataProvider_StillEmitsLocalScopeDiagnostics()
    {
        var d = Assert.Single(DiagnosticsEngine.Analyze(
            SemanticModel.Build("execute block as begin v = :undeclared; end")));
        Assert.Equal(DiagnosticCategory.UnresolvedVariable, d.Category);
    }

    // ══ Multiple diagnostics at once ═════════════════════════════════════════════════════════

    [Fact]
    public void MultipleDiagnostics_OfEachKind_AreAllReported()
    {
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql =
            "select k.qty from kontrahent k;\n" +      // UnknownColumn (qty)
            "execute procedure sp_missing(:z);\n" +    // UnknownObject (sp_missing)
            "execute block as begin v = :undeclared; end"; // UnresolvedVariable (:undeclared)

        var diags = Analyze(sql, meta);

        Assert.Equal(3, diags.Count);
        Assert.Contains(diags, d => d.Category == DiagnosticCategory.UnknownColumn);
        Assert.Contains(diags, d => d.Category == DiagnosticCategory.UnknownObject);
        Assert.Contains(diags, d => d.Category == DiagnosticCategory.UnresolvedVariable);
    }

    // ══ Determinism, ordering, de-duplication ════════════════════════════════════════════════

    [Fact]
    public void Results_AreOrderedByStart()
    {
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql =
            "select k.qty from kontrahent k;\n" +
            "execute procedure sp_missing(:z);\n" +
            "execute block as begin v = :undeclared; end";

        var diags = Analyze(sql, meta);

        for (int i = 1; i < diags.Count; i++)
        {
            Assert.True(diags[i - 1].Start <= diags[i].Start, "diagnostics must be sorted by Start");
        }
    }

    [Fact]
    public void Results_AreDeterministic_AcrossRuns()
    {
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql =
            "select k.qty from kontrahent k;\n" +
            "execute procedure sp_missing(:z);\n" +
            "execute block as begin v = :undeclared; end";

        var model = SemanticModel.Build(sql, meta);
        var first = DiagnosticsEngine.Analyze(model);
        var second = DiagnosticsEngine.Analyze(model);

        Assert.Equal(first, second); // record-struct sequence equality
    }

    [Fact]
    public void Results_ContainNoDuplicates()
    {
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        const string sql =
            "select k.qty from kontrahent k;\n" +
            "execute procedure sp_missing(:z);\n" +
            "execute block as begin v = :undeclared; end";

        var diags = Analyze(sql, meta);

        var keys = diags.Select(d => (d.Start, d.Length, d.Code)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // ══ Cancellation ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Analyze_HonoursCancellation()
    {
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        var model = SemanticModel.Build("select k.qty from kontrahent k", meta);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => DiagnosticsEngine.Analyze(model, cts.Token));
    }

    [Fact]
    public void Analyze_NotCancelled_Completes()
    {
        var meta = new FakeMetadata().Col("KONTRAHENT", "NAZWA", "VARCHAR(50)");
        var model = SemanticModel.Build("select k.qty from kontrahent k", meta);

        var diags = DiagnosticsEngine.Analyze(model, CancellationToken.None);
        Assert.Single(diags);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  S2 — AmbiguousColumn
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // A bare column present on ≥2 FROM tables — the binder can't pick one, so it is ambiguous.
    [Fact]
    public void AmbiguousColumn_BareColumnOnTwoTables_IsFlagged()
    {
        var meta = new FakeMetadata()
            .Col("A", "ID", "INTEGER")
            .Col("B", "ID", "INTEGER");
        const string sql = "select id from a, b";

        var d = Assert.Single(Analyze(sql, meta));
        Assert.Equal(DiagnosticCategory.AmbiguousColumn, d.Category);
        Assert.Equal("ET0005", d.Code);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(sql.IndexOf("id", StringComparison.Ordinal), d.Start);
        Assert.Equal("id".Length, d.Length);
    }

    // Qualifying the column removes the ambiguity — no diagnostic.
    [Fact]
    public void AmbiguousColumn_QualifiedColumn_IsNotFlagged()
    {
        var meta = new FakeMetadata()
            .Col("A", "ID", "INTEGER")
            .Col("B", "ID", "INTEGER");
        Assert.Empty(Analyze("select a.id from a, b", meta));
    }

    // The column exists on only one of the tables — it resolves; not ambiguous.
    [Fact]
    public void AmbiguousColumn_ColumnOnSingleTable_IsNotFlagged()
    {
        var meta = new FakeMetadata()
            .Col("A", "ID", "INTEGER")
            .Col("B", "NAME", "VARCHAR(50)");
        Assert.Empty(Analyze("select id from a, b", meta));
    }

    // Without metadata the binder records no column match at all, so nothing is ambiguous — silence.
    [Fact]
    public void AmbiguousColumn_EmptyMetadata_IsNotFlagged()
    {
        Assert.Empty(DiagnosticsEngine.Analyze(SemanticModel.Build("select id from a, b")));
    }

    // Regression: an ambiguous bare column must NOT be reported as UnknownColumn, and a genuine
    // unknown qualified column must NOT be reported as AmbiguousColumn — the two stay distinct.
    [Fact]
    public void AmbiguousColumn_AndUnknownColumn_StayDistinct()
    {
        var meta = new FakeMetadata()
            .Col("A", "ID", "INTEGER")
            .Col("B", "ID", "INTEGER");
        const string sql = "select id, a.qty from a, b";

        var diags = Analyze(sql, meta);
        Assert.Equal(2, diags.Count);
        Assert.Contains(diags, x => x.Category == DiagnosticCategory.AmbiguousColumn
            && x.Start == sql.IndexOf("id", StringComparison.Ordinal));
        Assert.Contains(diags, x => x.Category == DiagnosticCategory.UnknownColumn
            && x.Start == sql.IndexOf("qty", StringComparison.Ordinal));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  S2 — InsertCountMismatch
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // Fewer values than columns — the value list is underlined. Needs no metadata (pure list count).
    [Fact]
    public void InsertCountMismatch_FewerValuesThanColumns_IsFlagged()
    {
        const string sql = "insert into t (a, b, c) values (1, 2)";

        var d = Assert.Single(DiagnosticsEngine.Analyze(SemanticModel.Build(sql)));
        Assert.Equal(DiagnosticCategory.InsertCountMismatch, d.Category);
        Assert.Equal("ET0006", d.Code);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        // Span is the VALUES list "(1, 2)".
        Assert.Equal(sql.IndexOf("(1", StringComparison.Ordinal), d.Start);
        Assert.Equal("(1, 2)".Length, d.Length);
    }

    [Fact]
    public void InsertCountMismatch_MoreValuesThanColumns_IsFlagged()
    {
        var d = Assert.Single(DiagnosticsEngine.Analyze(
            SemanticModel.Build("insert into t (a) values (1, 2)")));
        Assert.Equal(DiagnosticCategory.InsertCountMismatch, d.Category);
    }

    [Fact]
    public void InsertCountMismatch_MatchingCounts_IsNotFlagged()
    {
        Assert.Empty(DiagnosticsEngine.Analyze(SemanticModel.Build("insert into t (a, b) values (1, 2)")));
    }

    // A comma inside a function call / nested paren is content, not a separator — counts stay 2 vs 2.
    [Fact]
    public void InsertCountMismatch_CommasInsideFunctionCall_AreNotCounted()
    {
        Assert.Empty(DiagnosticsEngine.Analyze(
            SemanticModel.Build("insert into t (a, b) values (coalesce(x, 0), 2)")));
    }

    // No explicit column list ⇒ nothing to compare against ⇒ silence.
    [Fact]
    public void InsertCountMismatch_NoExplicitColumnList_IsNotChecked()
    {
        Assert.Empty(DiagnosticsEngine.Analyze(SemanticModel.Build("insert into t values (1, 2, 3)")));
    }

    // INSERT … SELECT is not a columns↔VALUES comparison (projection count is a separate concern) ⇒ silence.
    [Fact]
    public void InsertCountMismatch_InsertSelect_IsNotChecked()
    {
        var meta = new FakeMetadata().Col("S", "X", "INTEGER").Col("S", "Y", "INTEGER");
        Assert.Empty(Analyze("insert into t (a, b) select x, y from s", meta));
    }

    // Firebird has no multi-row VALUES; unusual/malformed input stays silent (never guess).
    [Fact]
    public void InsertCountMismatch_MultiRowValues_IsNotChecked()
    {
        Assert.Empty(DiagnosticsEngine.Analyze(SemanticModel.Build("insert into t (a) values (1), (2)")));
    }

    // A malformed list (trailing comma) is not cleanly parseable ⇒ silence.
    [Fact]
    public void InsertCountMismatch_MalformedColumnList_IsNotChecked()
    {
        Assert.Empty(DiagnosticsEngine.Analyze(SemanticModel.Build("insert into t (a, ) values (1)")));
    }

    // A dotted target name (schema.table) is handled — the column list still lines up with VALUES.
    [Fact]
    public void InsertCountMismatch_DottedTargetName_IsFlagged()
    {
        var d = Assert.Single(DiagnosticsEngine.Analyze(
            SemanticModel.Build("insert into s.t (a, b) values (1)")));
        Assert.Equal(DiagnosticCategory.InsertCountMismatch, d.Category);
    }

    // An INSERT reused inside a PSQL body (Etap 6.9 / B5) is reached by the AST traversal too.
    [Fact]
    public void InsertCountMismatch_InsideProcedureBody_IsFlagged()
    {
        const string sql =
            "create procedure p\n" +
            "as\n" +
            "begin\n" +
            "  insert into t (a, b) values (1);\n" +
            "end";

        var d = Assert.Single(DiagnosticsEngine.Analyze(SemanticModel.Build(sql)));
        Assert.Equal(DiagnosticCategory.InsertCountMismatch, d.Category);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  S2 — combined determinism / no-regression
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // S1 and S2 categories together: ordered by Start, no duplicates, deterministic across runs.
    [Fact]
    public void S1AndS2_Combined_AreOrderedDeterministicAndUnique()
    {
        var meta = new FakeMetadata()
            .Col("A", "ID", "INTEGER")
            .Col("B", "ID", "INTEGER");
        const string sql =
            "insert into t (a, b, c) values (1, 2);\n" +   // InsertCountMismatch
            "select id from a, b;\n" +                      // AmbiguousColumn
            "execute procedure sp_missing(:z)";             // UnknownObject

        var model = SemanticModel.Build(sql, meta);
        var first = DiagnosticsEngine.Analyze(model);
        var second = DiagnosticsEngine.Analyze(model);

        Assert.Equal(first, second); // deterministic
        Assert.Equal(3, first.Count);
        Assert.Contains(first, d => d.Category == DiagnosticCategory.InsertCountMismatch);
        Assert.Contains(first, d => d.Category == DiagnosticCategory.AmbiguousColumn);
        Assert.Contains(first, d => d.Category == DiagnosticCategory.UnknownObject);

        for (int i = 1; i < first.Count; i++)
            Assert.True(first[i - 1].Start <= first[i].Start, "sorted by Start");

        var keys = first.Select(d => (d.Start, d.Length, d.Code)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  S6 — UnknownCursor
    // ══════════════════════════════════════════════════════════════════════════════════════════

    // A cursor operation naming a cursor that is declared nowhere in the script. No metadata needed.
    [Fact]
    public void UnknownCursor_UndeclaredCursor_IsFlagged()
    {
        const string sql = "execute block as begin open nosuch; end";

        var d = Assert.Single(DiagnosticsEngine.Analyze(SemanticModel.Build(sql)));
        Assert.Equal(DiagnosticCategory.UnknownCursor, d.Category);
        Assert.Equal("ET0007", d.Code);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(sql.IndexOf("nosuch", StringComparison.Ordinal), d.Start);
    }

    // OPEN / FETCH / CLOSE are each recognised — three usage sites, three diagnostics.
    [Fact]
    public void UnknownCursor_OpenFetchClose_AllFlagged()
    {
        const string sql = "execute block as begin open x; fetch x; close x; end";

        var diags = DiagnosticsEngine.Analyze(SemanticModel.Build(sql));
        Assert.Equal(3, diags.Count);
        Assert.All(diags, d => Assert.Equal(DiagnosticCategory.UnknownCursor, d.Category));
    }

    // A properly declared cursor (CREATE PROCEDURE, kept whole) resolves — no diagnostic.
    [Fact]
    public void UnknownCursor_DeclaredCursor_IsNotFlagged()
    {
        const string sql =
            "create procedure p\n" +
            "as\n" +
            "declare c cursor for (select 1 from rdb$database);\n" +
            "begin\n" +
            "  open c;\n" +
            "  close c;\n" +
            "end";
        Assert.Empty(DiagnosticsEngine.Analyze(SemanticModel.Build(sql)));
    }

    // Conservatism guard: even when statement segmentation splits an EXECUTE BLOCK's DECLARE section
    // from its BEGIN…END (so the cursor use can't resolve locally), a cursor of that name IS declared
    // somewhere in the script — so it must NOT be reported as unknown (no false positive).
    [Fact]
    public void UnknownCursor_DeclaredButMisSplit_IsNotFlagged()
    {
        const string sql =
            "execute block\n" +
            "as\n" +
            "declare c cursor for (select 1 from rdb$database);\n" +
            "begin\n" +
            "  open c;\n" +
            "end";
        var diags = DiagnosticsEngine.Analyze(SemanticModel.Build(sql));
        Assert.DoesNotContain(diags, d => d.Category == DiagnosticCategory.UnknownCursor);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  S6 — SuspendOutsideSelectable
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SuspendOutsideSelectable_InTrigger_IsFlagged()
    {
        const string sql =
            "create trigger tr for t after insert as\n" +
            "begin\n" +
            "  suspend;\n" +
            "end";

        var d = Assert.Single(DiagnosticsEngine.Analyze(SemanticModel.Build(sql)));
        Assert.Equal(DiagnosticCategory.SuspendOutsideSelectable, d.Category);
        Assert.Equal("ET0008", d.Code);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal(sql.IndexOf("suspend", StringComparison.Ordinal), d.Start);
    }

    [Fact]
    public void SuspendOutsideSelectable_InFunction_IsFlagged()
    {
        const string sql =
            "create function f returns integer as\n" +
            "begin\n" +
            "  suspend;\n" +
            "  return 1;\n" +
            "end";

        var d = Assert.Single(DiagnosticsEngine.Analyze(SemanticModel.Build(sql)));
        Assert.Equal(DiagnosticCategory.SuspendOutsideSelectable, d.Category);
    }

    // A procedure may be selectable — SUSPEND there is not flagged (prefer silence).
    [Fact]
    public void SuspendOutsideSelectable_InProcedure_IsNotFlagged()
    {
        const string sql =
            "create procedure p returns (x integer) as\n" +
            "begin\n" +
            "  suspend;\n" +
            "end";
        Assert.Empty(DiagnosticsEngine.Analyze(SemanticModel.Build(sql)));
    }

    // An EXECUTE BLOCK may be selectable — SUSPEND there is not flagged.
    [Fact]
    public void SuspendOutsideSelectable_InExecuteBlock_IsNotFlagged()
    {
        const string sql =
            "execute block returns (x integer) as\n" +
            "begin\n" +
            "  suspend;\n" +
            "end";
        Assert.Empty(DiagnosticsEngine.Analyze(SemanticModel.Build(sql)));
    }
}
