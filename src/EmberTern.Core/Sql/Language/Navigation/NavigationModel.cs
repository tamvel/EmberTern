using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Navigation;

/// <summary>What kind of "go to definition" the identifier under the caret leads to.</summary>
public enum NavigationTargetKind
{
    /// <summary>A database schema object (table/view/procedure/…): the App opens its DDL / detail
    /// tab. Given by <see cref="NavigationTarget.ObjectName"/> + <see cref="NavigationTarget.ObjectKind"/>.</summary>
    SchemaObject,

    /// <summary>A local declaration in the same script (alias, PSQL variable/parameter, cursor, CTE):
    /// the App jumps the caret to <see cref="NavigationTarget.DefinitionSpan"/> — no DB round-trip.</summary>
    LocalDefinition,
}

/// <summary>
/// The navigable thing under the caret — Etap 6 (design §5.8 / §10). Produced by
/// <see cref="NavigationEngine"/> from the <see cref="SemanticModel"/>. The App uses
/// <see cref="ReferenceSpan"/> for the Ctrl-hover underline, and on Ctrl+Click either opens the
/// schema object (<see cref="NavigationTargetKind.SchemaObject"/>) or jumps to the local declaration
/// (<see cref="NavigationTargetKind.LocalDefinition"/>). Pure data — no Avalonia; read-only, so §0
/// holds by construction (navigation never modifies code).
/// </summary>
public sealed class NavigationTarget
{
    private NavigationTarget(
        NavigationTargetKind kind,
        TextSpan referenceSpan,
        Symbol symbol,
        string? objectName,
        SymbolKind objectKind,
        TextSpan? definitionSpan)
    {
        Kind = kind;
        ReferenceSpan = referenceSpan;
        Symbol = symbol;
        ObjectName = objectName;
        ObjectKind = objectKind;
        DefinitionSpan = definitionSpan;
    }

    /// <summary>How to navigate.</summary>
    public NavigationTargetKind Kind { get; }

    /// <summary>The span of the identifier under the caret — the App underlines this on Ctrl+hover.</summary>
    public TextSpan ReferenceSpan { get; }

    /// <summary>The symbol the identifier resolved to (for the tooltip / quick info).</summary>
    public Symbol Symbol { get; }

    /// <summary>For <see cref="NavigationTargetKind.SchemaObject"/>: the DB object name to open
    /// (catalog-folded). <c>null</c> otherwise.</summary>
    public string? ObjectName { get; }

    /// <summary>For <see cref="NavigationTargetKind.SchemaObject"/>: the object's kind (so the App
    /// routes to the right detail view). <see cref="SymbolKind.Unknown"/> otherwise.</summary>
    public SymbolKind ObjectKind { get; }

    /// <summary>For <see cref="NavigationTargetKind.LocalDefinition"/>: the in-editor declaration to
    /// jump to. <c>null</c> otherwise (or when the local declaration span is unknown).</summary>
    public TextSpan? DefinitionSpan { get; }

    internal static NavigationTarget ForSchemaObject(
        TextSpan referenceSpan, Symbol symbol, string objectName, SymbolKind objectKind)
        => new(NavigationTargetKind.SchemaObject, referenceSpan, symbol, objectName, objectKind, definitionSpan: null);

    internal static NavigationTarget ForLocal(TextSpan referenceSpan, Symbol symbol, TextSpan? definitionSpan)
        => new(NavigationTargetKind.LocalDefinition, referenceSpan, symbol, objectName: null, SymbolKind.Unknown, definitionSpan);
}

/// <summary>
/// A <b>safe, local</b> rename opportunity — Etap 6 / M5 (design §10). Produced by
/// <see cref="NavigationEngine.GetLocalRename"/> only for a symbol that lives entirely inside the
/// script and can be renamed without touching the database: a FROM/JOIN <em>alias</em> (or a
/// derived-table alias), a PSQL variable/parameter, a cursor, or a CTE. It carries every occurrence
/// bound to that one symbol (declaration included) so the App can replace them atomically.
/// <para>
/// This is the §0 (never lose information / never corrupt metadata) contract in code: the engine
/// returns a rename ONLY when it is certain the identifier denotes a local — a schema object, a
/// column, a <c>NEW</c>/<c>OLD</c> record, or a table referenced by its own name yields <c>null</c>,
/// so the App simply cannot start a rename that would alter a database object or the wrong text. The
/// occurrences come from the binder's exact symbol resolution (<see cref="SemanticModel.ReferencesTo"/>),
/// so a name that collides with an unrelated identifier is never swept in. Pure data — no Avalonia.
/// </para>
/// </summary>
public sealed class NavigationRename
{
    internal NavigationRename(Symbol symbol, string currentName, IReadOnlyList<RenameOccurrence> occurrences)
    {
        Symbol = symbol;
        CurrentName = currentName;
        Occurrences = occurrences;
    }

    /// <summary>The local symbol being renamed.</summary>
    public Symbol Symbol { get; }

    /// <summary>What the symbol denotes (variable, parameter, cursor, CTE, table alias).</summary>
    public SymbolKind Kind => Symbol.Kind;

    /// <summary>The symbol's current (catalog-folded) name — informational; the App prefills the
    /// rename box from the identifier text at the caret so the as-written casing is preserved.</summary>
    public string CurrentName { get; }

    /// <summary>Every occurrence bound to the symbol — declaration and all uses — in source order.
    /// The App turns these into one atomic set of edits. Never empty for a returned rename.</summary>
    public IReadOnlyList<RenameOccurrence> Occurrences { get; }
}

/// <summary>
/// One occurrence of the symbol being renamed: where it is, and <b>what the binder saw there</b>.
/// <para>
/// <see cref="Text"/> is the identifier exactly as written — which is not the same as
/// <see cref="NavigationRename.CurrentName"/>, the catalog-FOLDED name (a variable declared
/// <c>V_Total</c> folds to <c>V_TOTAL</c>). Carrying it means the App can state, per occurrence, what it
/// expects to find there, so the applier's drift check compares against the MODEL's belief rather than
/// re-reading the document and comparing it with itself. That is what lets rename share one mutation
/// path with Quick Fixes instead of keeping its own verification loop
/// (<see href="../../docs/design/editor-quick-fixes.md">editor-quick-fixes.md</see> §2.2).
/// </para>
/// </summary>
/// <param name="Span">Where the occurrence sits in the source.</param>
/// <param name="Text">The identifier as written at that span.</param>
public readonly record struct RenameOccurrence(TextSpan Span, string Text);
