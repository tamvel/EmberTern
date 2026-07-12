using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Highlighting;

/// <summary>The semantic class a resolved identifier occurrence paints as — the "accent" layer of
/// the two-layer highlighting system (design §9.2). The App maps each class to a theme brush:
/// <see cref="SchemaObject"/> reuses the metadata tree's per-kind <c>IconColor_*</c> palette (so an
/// object's colour in the editor matches its icon in the tree — teaching "coloured object =
/// navigable"), while <see cref="Column"/> and <see cref="Local"/> get their own calm/low-chroma
/// editor tokens.</summary>
public enum SemanticHighlightClass
{
    /// <summary>Not semantically coloured — the lexical (XSHD) layer shows through. Keywords,
    /// punctuation, and identifiers that did not resolve to a symbol.</summary>
    None,

    /// <summary>A navigable database object (table/view/procedure/function/trigger/domain/exception/
    /// sequence/package/index/role). <see cref="SemanticHighlight.ObjectKind"/> carries which one.</summary>
    SchemaObject,

    /// <summary>A table/view column — a calm, frequent-use colour (must not shout, §9.2).</summary>
    Column,

    /// <summary>A local scope name — a FROM/JOIN alias, PSQL variable/parameter, cursor, CTE, or a
    /// NEW/OLD record alias. A distinct low-chroma "local" treatment signalling "not a DB object".</summary>
    Local,
}

/// <summary>The classification of one identifier occurrence for semantic highlighting. Pure value.</summary>
/// <param name="Class">The semantic class.</param>
/// <param name="ObjectKind">For <see cref="SemanticHighlightClass.SchemaObject"/>, which kind (drives
/// the per-kind colour); <see cref="SymbolKind.Unknown"/> otherwise.</param>
public readonly record struct SemanticHighlight(SemanticHighlightClass Class, SymbolKind ObjectKind)
{
    public static readonly SemanticHighlight None = new(SemanticHighlightClass.None, SymbolKind.Unknown);
}

/// <summary>
/// Classifies a resolved <see cref="SymbolReference"/> into a <see cref="SemanticHighlight"/> — the
/// pure Core half of semantic highlighting (Etap 6 / M3, design §9). Keeps the "grammar/semantic
/// logic in Core, App is glue" rule: the App's colorizer walks the model's references, calls
/// <see cref="Classify"/>, and paints the resolved brush. Read-only — §0 holds by construction.
/// </summary>
public static class SemanticHighlightClassifier
{
    /// <summary>The highlight class for a reference, keyed off the <see cref="Symbol"/> it resolved
    /// to. An unresolved occurrence (no symbol) is <see cref="SemanticHighlight.None"/> so only the
    /// lexical layer shows — high-precision colouring, never guessing.</summary>
    public static SemanticHighlight Classify(SymbolReference reference)
        => reference is null ? SemanticHighlight.None : ClassifySymbol(reference.Symbol);

    /// <summary>The highlight class for a symbol directly (a schema object, column, or local).</summary>
    public static SemanticHighlight ClassifySymbol(Symbol? symbol) => symbol switch
    {
        ColumnSymbol => new SemanticHighlight(SemanticHighlightClass.Column, SymbolKind.Unknown),

        // A schema object referenced by its own name → colour by kind (reuses the tree palette).
        SchemaObjectSymbol o => new SemanticHighlight(SemanticHighlightClass.SchemaObject, o.Kind),

        // FROM/JOIN aliases, PSQL locals, CTEs, cursors, NEW/OLD — the "local scope" treatment.
        TableReferenceSymbol => Local,
        VariableSymbol => Local,
        ParameterSymbol => Local,
        CursorSymbol => Local,
        CteSymbol => Local,
        RecordAliasSymbol => Local,

        _ => SemanticHighlight.None,
    };

    private static readonly SemanticHighlight Local = new(SemanticHighlightClass.Local, SymbolKind.Unknown);
}
