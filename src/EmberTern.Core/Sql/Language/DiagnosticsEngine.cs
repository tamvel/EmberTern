using System;
using System.Collections.Generic;
using System.Threading;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Language.Signatures;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// Stage 7 (Diagnostics) — the semantic diagnostics engine. A <b>pure Core</b> client of the
/// <see cref="SemanticModel"/>: given a built model it returns a conservative, de-duplicated,
/// deterministically-ordered <see cref="IReadOnlyList{Diagnostic}"/>. It is zero-Avalonia, offline, and
/// unit-testable; it knows nothing about the editor, rendering, squiggles, colours, or quick fixes.
/// <para>
/// It <b>computes nothing structural</b> — the parser is the single source of structure and the binder
/// has already resolved every identifier occurrence (<see cref="SymbolReference"/>). Diagnostics are a
/// <em>filter over that existing data</em>: one forward pass over <see cref="SemanticModel.References"/>
/// plus a bounded per-INSERT check that reuses the existing INSERT list reader
/// (<see cref="SignatureHelpEngine"/>). No second parse, no new index, no parallel scanner.
/// </para>
/// <para>
/// Governed by the project's paramount conservatism rule — <b>prefer silence over false positives</b>.
/// Where there is any doubt (no live metadata, an unknown table, a host parameter outside a routine body,
/// a malformed/multi-row list) the engine emits nothing. See
/// <see href="../../docs/design/editor-stage7-diagnostics.md">editor-stage7-diagnostics.md</see>.
/// </para>
/// <para>Milestones so far: <b>S1</b> — <see cref="DiagnosticCategory.UnknownObject"/> /
/// <see cref="DiagnosticCategory.UnknownColumn"/> / <see cref="DiagnosticCategory.UnresolvedVariable"/> /
/// <see cref="DiagnosticCategory.UnresolvedParameter"/>; <b>S2</b> —
/// <see cref="DiagnosticCategory.AmbiguousColumn"/> and
/// <see cref="DiagnosticCategory.InsertCountMismatch"/>.</para>
/// </summary>
public static class DiagnosticsEngine
{
    // Stable diagnostic codes — never reused/renumbered (they anchor filtering, tests, and future
    // quick-fix targeting).
    private const string CodeUnknownObject = "ET0001";
    private const string CodeUnknownColumn = "ET0002";
    private const string CodeUnresolvedVariable = "ET0003";
    private const string CodeUnresolvedParameter = "ET0004";
    private const string CodeAmbiguousColumn = "ET0005";
    private const string CodeInsertCountMismatch = "ET0006";
    private const string CodeUnknownCursor = "ET0007";
    private const string CodeSuspendOutsideSelectable = "ET0008";

    /// <summary>
    /// Analyses <paramref name="model"/> and returns its semantic diagnostics — conservative,
    /// de-duplicated, and stably ordered by (<see cref="Diagnostic.Start"/>,
    /// <see cref="Diagnostic.Length"/>, <see cref="Diagnostic.Code"/>). Deterministic: the same model
    /// yields the same list. Never throws on incomplete/invalid input (the model is error-tolerant);
    /// honours <paramref name="cancellationToken"/> by checking it as it scans.
    /// </summary>
    /// <param name="model">The built semantic model to analyse.</param>
    /// <param name="cancellationToken">Cancels an in-flight analysis (a newer edit superseding this one).</param>
    public static IReadOnlyList<Diagnostic> Analyze(SemanticModel model, CancellationToken cancellationToken = default)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        cancellationToken.ThrowIfCancellationRequested();

        // UnknownObject / UnknownColumn / AmbiguousColumn require a live metadata snapshot. With no
        // connection (EmptyMetadataProvider) every schema object and every column is unresolved by
        // construction, so flagging them would be pure noise — the conservatism rule forbids it.
        // Local-scope diagnostics (unresolved variable/parameter) and the pure-syntactic count mismatch
        // do NOT need metadata.
        bool hasMetadata = model.Metadata is not EmptyMetadataProvider;

        var results = new List<Diagnostic>();
        AnalyzeReferences(model, hasMetadata, results, cancellationToken);
        AnalyzeInsertCounts(model, results, cancellationToken);
        AnalyzeSuspendContext(model, results, cancellationToken);

        return Finalize(results);
    }

    // ── Reference-driven diagnostics (one forward pass over References) ────────────────────────

    private static void AnalyzeReferences(
        SemanticModel model, bool hasMetadata, List<Diagnostic> results, CancellationToken ct)
    {
        var references = model.References;

        // `previous` lets a qualified column (whose binder-emitted qualifier reference immediately
        // precedes it) be told apart from an ambiguous bare column — without a second pass or an index.
        SymbolReference? previous = null;

        // Built lazily on the first unresolved cursor reference: the names of every cursor declared
        // anywhere in the script. An unresolved cursor whose name IS declared somewhere is not a genuine
        // unknown cursor — it is a scope/segmentation artifact (e.g. the lenient parser splitting an
        // EXECUTE BLOCK's DECLARE section from its BEGIN…END). Staying silent there upholds the
        // conservatism rule; a genuinely undeclared cursor has no such declaration and is still flagged.
        HashSet<string>? declaredCursors = null;

        for (int i = 0; i < references.Count; i++)
        {
            if ((i & 0x3F) == 0) ct.ThrowIfCancellationRequested();

            var r = references[i];

            // A declaration occurrence is never itself a diagnostic (it introduces the name).
            if (r.IsDefinition || r.IsResolved)
            {
                previous = r;
                continue;
            }

            switch (r.Role)
            {
                case ReferenceRole.SchemaObject when hasMetadata:
                    results.Add(new Diagnostic(
                        r.Span.Start, r.Span.Length, DiagnosticSeverity.Warning,
                        $"Unknown object '{r.Text}'.", CodeUnknownObject, DiagnosticCategory.UnknownObject));
                    break;

                case ReferenceRole.Column when hasMetadata:
                    AddColumnDiagnostic(previous, r, results);
                    break;

                case ReferenceRole.Variable when IsInRoutineBody(model, r.Span.Start):
                    results.Add(new Diagnostic(
                        r.Span.Start, r.Span.Length, DiagnosticSeverity.Warning,
                        $"Unresolved variable '{r.Text}'.", CodeUnresolvedVariable, DiagnosticCategory.UnresolvedVariable));
                    break;

                case ReferenceRole.Parameter when IsInRoutineBody(model, r.Span.Start):
                    results.Add(new Diagnostic(
                        r.Span.Start, r.Span.Length, DiagnosticSeverity.Warning,
                        $"Unresolved parameter '{r.Text}'.", CodeUnresolvedParameter, DiagnosticCategory.UnresolvedParameter));
                    break;

                case ReferenceRole.Cursor:
                    // A cursor reference is recorded ONLY for a cursor operation (OPEN/FETCH/CLOSE), always
                    // in a PSQL body — no metadata needed. Flag it only when NO cursor of that name is
                    // declared anywhere in the script (else it is a scope/segmentation artifact — stay silent).
                    declaredCursors ??= CollectDeclaredCursorNames(model);
                    if (!declaredCursors.Contains(r.Text))
                    {
                        results.Add(new Diagnostic(
                            r.Span.Start, r.Span.Length, DiagnosticSeverity.Warning,
                            $"Unknown cursor '{r.Text}'.", CodeUnknownCursor, DiagnosticCategory.UnknownCursor));
                    }
                    break;
            }

            previous = r;
        }
    }

    // An unresolved Column reference is one of two shapes, told apart by its immediate predecessor (the
    // binder emits a member's qualifier reference right before it):
    //   • QUALIFIED (alias.col / NEW.col): predecessor is a resolved Qualifier/RecordAlias.
    //       – table resolved  ⇒ UnknownColumn  (table resolved, column absent)
    //       – table unknown   ⇒ silence (the column can't be checked; no cascade)
    //   • BARE: no preceding qualifier — the binder records a bare unresolved Column ONLY when the name
    //     matched a column on ≥2 in-scope tables (ambiguous) ⇒ AmbiguousColumn.
    private static void AddColumnDiagnostic(SymbolReference? previous, SymbolReference column, List<Diagnostic> results)
    {
        if (IsQualifiedColumnReference(previous, column))
        {
            if (QualifierResolvesTable(previous!))
            {
                results.Add(new Diagnostic(
                    column.Span.Start, column.Span.Length, DiagnosticSeverity.Warning,
                    $"Unknown column '{column.Text}'.", CodeUnknownColumn, DiagnosticCategory.UnknownColumn));
            }
            // else: qualified on an unknown table — stay silent.
            return;
        }

        // Bare unresolved column ⇒ ambiguous (the only way the binder records one).
        results.Add(new Diagnostic(
            column.Span.Start, column.Span.Length, DiagnosticSeverity.Warning,
            $"Ambiguous column '{column.Text}'.", CodeAmbiguousColumn, DiagnosticCategory.AmbiguousColumn));
    }

    // The column reference is a qualified access (alias.col / NEW.col): its predecessor is a resolved
    // Qualifier/RecordAlias sitting before it in source. (A qualifier reference is always immediately
    // followed by its own member reference, so a truly bare column never has a qualifier as predecessor.)
    private static bool IsQualifiedColumnReference(SymbolReference? previous, SymbolReference column)
    {
        if (previous is not { IsResolved: true } q) return false;
        if (q.Span.End > column.Span.Start) return false; // the qualifier must precede the member in source
        return q.Role is ReferenceRole.Qualifier or ReferenceRole.RecordAlias;
    }

    // The qualifier binds a KNOWN table (a table reference with a resolved target, or a NEW/OLD record
    // alias bound to a known table) — so an absent column on it is a genuine unknown column.
    private static bool QualifierResolvesTable(SymbolReference qualifier) => qualifier.Symbol switch
    {
        TableReferenceSymbol { Target: not null } => true,
        RecordAliasSymbol { TargetTable: not null } => true,
        _ => false,
    };

    // The names of every cursor the script declares (DECLARE … CURSOR / FOR … AS CURSOR), folded for
    // case-insensitive comparison. Collected once over AllSymbols (not References), only when needed.
    private static HashSet<string> CollectDeclaredCursorNames(SemanticModel model)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in model.AllSymbols)
        {
            if (s.Kind == SymbolKind.Cursor) set.Add(s.Name);
        }
        return set;
    }

    // True when the reference at <paramref name="offset"/> lies inside a PSQL routine body — a
    // CREATE PROCEDURE/FUNCTION/TRIGGER, EXECUTE BLOCK, or anonymous block scope (possibly through a
    // nested body query scope). This gate keeps host parameters (a `:id` in a plain DSQL query, or a bare
    // EXECUTE PROCEDURE … RETURNING_VALUES :a) from being flagged: only in a routine body must a `:name`
    // bind to a declared variable/parameter. Walks the scope tree, not the reference list.
    private static bool IsInRoutineBody(SemanticModel model, int offset)
    {
        for (var scope = model.ScopeAt(offset); scope is not null; scope = scope.Parent)
        {
            if (scope.Kind == ScopeKind.RoutineBody) return true;
        }
        return false;
    }

    // ── INSERT count mismatch (bounded per-INSERT check; reuses the SignatureHelpEngine list reader) ──

    private static void AnalyzeInsertCounts(SemanticModel model, List<Diagnostic> results, CancellationToken ct)
    {
        // Every INSERT reachable in the tree — top-level and reused inside a PSQL body (B5). A pure AST
        // traversal, not a token walk.
        foreach (var stmt in model.Syntax.Statements)
        {
            foreach (var node in stmt.DescendantNodesAndSelf())
            {
                if (node is not InsertStatement insert) continue;
                ct.ThrowIfCancellationRequested();

                if (SignatureHelpEngine.InsertColumnAndValueCounts(insert.Tokens) is not { } counts) continue;
                if (counts.Columns == counts.Values) continue;

                results.Add(new Diagnostic(
                    counts.ValuesStart, counts.ValuesLength, DiagnosticSeverity.Error,
                    $"INSERT column/value count mismatch: {counts.Columns} column(s), {counts.Values} value(s).",
                    CodeInsertCountMismatch, DiagnosticCategory.InsertCountMismatch));
            }
        }
    }

    // ── SUSPEND outside a selectable context (AST walk over trigger/function bodies) ──────────────

    private static void AnalyzeSuspendContext(SemanticModel model, List<Diagnostic> results, CancellationToken ct)
    {
        // Only the CERTAIN non-selectable contexts: a trigger (never selectable) and a PSQL function
        // (returns a scalar via RETURN). A procedure / EXECUTE BLOCK may be selectable, and a header-less
        // body-editor fragment (AnonymousBlockStatement) carries no context — both stay silent.
        foreach (var stmt in model.Syntax.Statements)
        {
            if (stmt is not DdlStatement { ObjectKind: DdlObjectKind.Trigger or DdlObjectKind.Function } ddl)
                continue;

            var context = ddl.ObjectKind == DdlObjectKind.Trigger ? "trigger" : "function";
            foreach (var node in ddl.DescendantNodes())
            {
                if (node is not PsqlLeafStatement { Kind: PsqlLeafKind.Suspend } leaf) continue;
                ct.ThrowIfCancellationRequested();
                results.Add(new Diagnostic(
                    leaf.Start, leaf.Length, DiagnosticSeverity.Warning,
                    $"SUSPEND is not valid in a {context}.",
                    CodeSuspendOutsideSelectable, DiagnosticCategory.SuspendOutsideSelectable));
            }
        }
    }

    // ── Determinism ────────────────────────────────────────────────────────────────────────────

    // Deterministic output: stable order by (Start, Length, Code), then adjacent-duplicate removal. The
    // triple (Start, Length, Code) uniquely determines a diagnostic here (same span + same code ⇒ same
    // category/severity/message), so dropping equal-adjacent entries removes any exact duplicate.
    private static IReadOnlyList<Diagnostic> Finalize(List<Diagnostic> results)
    {
        if (results.Count == 0) return Array.Empty<Diagnostic>();

        results.Sort(static (a, b) =>
        {
            int c = a.Start.CompareTo(b.Start);
            if (c != 0) return c;
            c = a.Length.CompareTo(b.Length);
            if (c != 0) return c;
            return string.CompareOrdinal(a.Code, b.Code);
        });

        var deduped = new List<Diagnostic>(results.Count) { results[0] };
        for (int i = 1; i < results.Count; i++)
        {
            var prev = deduped[^1];
            var cur = results[i];
            if (cur.Start == prev.Start && cur.Length == prev.Length && cur.Code == prev.Code) continue;
            deduped.Add(cur);
        }
        return deduped;
    }
}
