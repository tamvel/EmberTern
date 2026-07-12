using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>The kind of a lexical <see cref="Scope"/>.</summary>
public enum ScopeKind
{
    /// <summary>The root scope of the whole script.</summary>
    Script,

    /// <summary>A query scope — a top-level SELECT or a subquery. Declares its FROM/JOIN table
    /// references (and the CTE names visible to it).</summary>
    Query,

    /// <summary>A DML statement scope — INSERT / UPDATE / DELETE / MERGE. Declares its target
    /// (and source) table references.</summary>
    Dml,

    /// <summary>A routine body — a CREATE PROCEDURE/FUNCTION/TRIGGER definition, an EXECUTE BLOCK,
    /// or a bare anonymous PSQL block. Declares parameters, local variables, cursors, and (for a
    /// trigger) the NEW/OLD record aliases.</summary>
    RoutineBody,

    /// <summary>A nested <c>BEGIN … END</c> block inside a routine body.</summary>
    Block,
}

/// <summary>
/// A lexical scope in the semantic model — a nested region that declares symbols and resolves names
/// by walking outward to its parent. Scopes form a tree rooted at the script scope; name resolution
/// (<see cref="Resolve"/>) and visibility (<see cref="VisibleSymbols"/>) fall out of the tree, so
/// shadowing and name collisions across scopes are handled naturally without a global symbol list.
/// <para>
/// Name comparison is case-insensitive (<see cref="StringComparer.OrdinalIgnoreCase"/>), matching
/// how the editor already resolves identifiers; unquoted names are stored folded to upper-case, so
/// an unquoted reference resolves regardless of the case the user typed.
/// </para>
/// </summary>
public sealed class Scope
{
    private readonly List<Symbol> _symbols = new();
    private readonly List<Scope> _children = new();

    internal Scope(ScopeKind kind, Scope? parent, TextSpan span, SqlStatement? statement)
    {
        Kind = kind;
        Parent = parent;
        Span = span;
        Statement = statement;
    }

    /// <summary>The scope's kind.</summary>
    public ScopeKind Kind { get; }

    /// <summary>The enclosing scope, or <c>null</c> for the root script scope.</summary>
    public Scope? Parent { get; }

    /// <summary>The source region this scope covers. Refined by the binder as it discovers the
    /// scope's true extent.</summary>
    public TextSpan Span { get; internal set; }

    /// <summary>The top-level statement this scope belongs to, when applicable.</summary>
    public SqlStatement? Statement { get; }

    /// <summary>Symbols declared directly in this scope (not those inherited from ancestors).</summary>
    public IReadOnlyList<Symbol> Symbols => _symbols;

    /// <summary>Child scopes, in source order.</summary>
    public IReadOnlyList<Scope> Children => _children;

    // ── Binding-time mutation (internal) ─────────────────────────────────────────────────────

    /// <summary>Declares <paramref name="symbol"/> in this scope and wires its
    /// <see cref="Symbol.Scope"/> back-link. A later declaration of the same name shadows the
    /// earlier one within this scope (last-wins for <see cref="LookupLocal"/>).</summary>
    internal void Declare(Symbol symbol)
    {
        symbol.Scope = this;
        _symbols.Add(symbol);
    }

    internal Scope NewChild(ScopeKind kind, TextSpan span, SqlStatement? statement)
    {
        var child = new Scope(kind, this, span, statement);
        _children.Add(child);
        return child;
    }

    // ── Resolution (public) ──────────────────────────────────────────────────────────────────

    /// <summary>The symbol named <paramref name="name"/> declared directly in this scope
    /// (case-insensitive), or <c>null</c>. When a name is declared more than once here the most
    /// recent declaration wins.</summary>
    public Symbol? LookupLocal(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (int i = _symbols.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_symbols[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return _symbols[i];
            }
        }
        return null;
    }

    /// <summary>The symbol named <paramref name="name"/> visible from this scope — this scope
    /// first, then each ancestor outward — or <c>null</c> when unresolved. Inner declarations
    /// shadow outer ones.</summary>
    public Symbol? Resolve(string? name)
        => LookupLocal(name) ?? Parent?.Resolve(name);

    /// <summary>Every symbol visible from this scope (this scope's symbols plus, non-shadowed,
    /// each ancestor's), inner-most first. A name declared in an inner scope hides the same name
    /// in an outer scope.</summary>
    public IEnumerable<Symbol> VisibleSymbols()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var s = this; s is not null; s = s.Parent)
        {
            foreach (var sym in s._symbols)
            {
                if (seen.Add(sym.Name))
                {
                    yield return sym;
                }
            }
        }
    }

    /// <summary>The deepest descendant scope (this one or a child) whose <see cref="Span"/>
    /// contains <paramref name="offset"/>. Falls back to this scope when no child contains it.
    /// <para>
    /// Containment is inclusive at the END (<c>Start ≤ offset ≤ End</c>), unlike the half-open
    /// <see cref="TextSpan.Contains"/>, so a caret at the very end of a statement/block still resolves
    /// <em>into</em> it — the most common completion position (e.g. <c>… where n.|</c> at end of text),
    /// which would otherwise fall back to the enclosing scope and lose the FROM aliases. At a shared
    /// boundary between two siblings (<c>stmt1;|stmt2</c>) the later-starting child wins (the caret is
    /// at the start of the next statement).
    /// </para></summary>
    public Scope ScopeAt(int offset)
    {
        Scope? match = null;
        foreach (var child in _children)
        {
            if (offset >= child.Span.Start && offset <= child.Span.End
                && (match is null || child.Span.Start >= match.Span.Start))
            {
                match = child;
            }
        }
        return match?.ScopeAt(offset) ?? this;
    }

    /// <summary>This scope and all its descendants, depth-first (source order).</summary>
    public IEnumerable<Scope> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        {
            foreach (var d in child.DescendantsAndSelf())
            {
                yield return d;
            }
        }
    }

    public override string ToString() => $"{Kind} {Span}";
}
