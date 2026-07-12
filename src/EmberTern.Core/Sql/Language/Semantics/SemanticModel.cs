using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>
/// The semantic model of a script — Etap 4 of the editor rebuild. It binds the error-tolerant AST
/// (<see cref="SqlScript"/>) to <em>meaning</em>: a tree of lexical <see cref="Scope"/>s, the
/// <see cref="Symbol"/>s declared in them (table aliases, columns, PSQL variables/parameters, CTEs,
/// cursors, NEW/OLD record aliases, referenced schema objects), and every identifier occurrence as
/// a <see cref="SymbolReference"/> resolved (or not) to a symbol.
/// <para>
/// This is the stable, reusable foundation the later etaps build on — completion, navigation,
/// quick-info, diagnostics, find-references, rename, and any future AI/LSP all query this model.
/// Its public shape is deliberately decoupled from how the binder reads statement interiors
/// (token-walk today; a deeper AST later): the binder internals may change without changing this
/// API or any consumer.
/// </para>
/// <para>Pure — no Avalonia, no Firebird driver. Read-only, so §0 (never lose information) holds by
/// construction. Error-tolerant: it never throws on incomplete or invalid input, and works with or
/// without live metadata (see <see cref="ISqlMetadataProvider"/> / <see cref="EmptyMetadataProvider"/>).</para>
/// </summary>
public sealed class SemanticModel
{
    private readonly IReadOnlyList<SymbolReference> _references;

    internal SemanticModel(
        SqlScript syntax,
        ISqlMetadataProvider metadata,
        Scope rootScope,
        IReadOnlyList<Symbol> allSymbols,
        IReadOnlyList<SymbolReference> references)
    {
        Syntax = syntax;
        Metadata = metadata;
        RootScope = rootScope;
        AllSymbols = allSymbols;
        _references = references;
    }

    /// <summary>The AST this model was built from.</summary>
    public SqlScript Syntax { get; }

    /// <summary>The metadata snapshot used to resolve schema objects and columns.</summary>
    public ISqlMetadataProvider Metadata { get; }

    /// <summary>The root scope of the script.</summary>
    public Scope RootScope { get; }

    /// <summary>Every symbol declared by the script (aliases, variables, parameters, CTEs, cursors,
    /// record aliases, and the schema objects it introduces via DDL). Not the whole database
    /// catalog — only what the script itself brings into scope.</summary>
    public IReadOnlyList<Symbol> AllSymbols { get; }

    /// <summary>Every identifier occurrence the binder recorded, in source order.</summary>
    public IReadOnlyList<SymbolReference> References => _references;

    // ── Offset-driven queries (the surface future features consume) ──────────────────────────

    /// <summary>The deepest scope whose span contains <paramref name="offset"/>.</summary>
    public Scope ScopeAt(int offset) => RootScope.ScopeAt(offset);

    /// <summary>The identifier reference whose span contains <paramref name="offset"/>, or
    /// <c>null</c> when the offset is not on a recorded identifier. When occurrences overlap the
    /// most specific (shortest) one wins.
    /// <para>Containment is inclusive at the END (<c>Start ≤ offset ≤ End</c>), unlike the half-open
    /// <see cref="TextSpan.Contains"/> — the caret sitting at the very end of a fully-typed identifier
    /// (<c>nrdokwew|</c>) is the most common Quick-Info / go-to-definition / completion position and
    /// must still resolve to that identifier. Same insight as <see cref="Scope.ScopeAt"/> (gotcha
    /// #198); the shortest-span tie-break keeps a shared boundary deterministic (the tighter reference
    /// wins).</para></summary>
    public SymbolReference? ReferenceAt(int offset)
    {
        SymbolReference? best = null;
        foreach (var r in _references)
        {
            var span = r.Span;
            if (offset >= span.Start && offset <= span.End
                && (best is null || span.Length < best.Span.Length))
            {
                best = r;
            }
        }
        return best;
    }

    /// <summary>The symbol the identifier at <paramref name="offset"/> binds to, or <c>null</c>.</summary>
    public Symbol? ResolveAt(int offset) => ReferenceAt(offset)?.Symbol;

    /// <summary>All symbols visible at <paramref name="offset"/> (the containing scope plus its
    /// ancestors, inner declarations shadowing outer). The raw material for context-aware
    /// completion in a later etap.</summary>
    public IReadOnlyList<Symbol> SymbolsInScope(int offset)
    {
        var result = new List<Symbol>();
        foreach (var s in ScopeAt(offset).VisibleSymbols())
        {
            result.Add(s);
        }
        return result;
    }

    /// <summary>Every recorded occurrence bound to <paramref name="symbol"/> (including its
    /// declaration) — the local basis for find-references / rename.</summary>
    public IReadOnlyList<SymbolReference> ReferencesTo(Symbol symbol)
    {
        var result = new List<SymbolReference>();
        if (symbol is null) return result;
        foreach (var r in _references)
        {
            if (ReferenceEquals(r.Symbol, symbol))
            {
                result.Add(r);
            }
        }
        return result;
    }

    // ── Construction ─────────────────────────────────────────────────────────────────────────

    /// <summary>Builds the semantic model for an already-parsed script.</summary>
    /// <param name="syntax">The AST.</param>
    /// <param name="metadata">The metadata snapshot, or <c>null</c> to bind local scope only.</param>
    public static SemanticModel Build(SqlScript syntax, ISqlMetadataProvider? metadata = null)
    {
        if (syntax is null) throw new ArgumentNullException(nameof(syntax));
        return SemanticBinder.Bind(syntax, metadata ?? EmptyMetadataProvider.Instance);
    }

    /// <summary>Parses <paramref name="sql"/> and builds its semantic model.</summary>
    /// <param name="sql">The script text.</param>
    /// <param name="metadata">The metadata snapshot, or <c>null</c> to bind local scope only.</param>
    public static SemanticModel Build(string sql, ISqlMetadataProvider? metadata = null)
    {
        if (sql is null) throw new ArgumentNullException(nameof(sql));
        // Lenient segmentation: analyse every statement even when they are only newline-separated (no
        // ';'). Read-only model, so an over-split can at most weaken IntelliSense, never corrupt code
        // (§0); the executors keep the strict ';'-only Parse. Fixes "only the first statement is
        // coloured / navigable" for a multi-statement editor without semicolons.
        return Build(SqlParser.Parse(sql, lenient: true).Root, metadata);
    }
}
