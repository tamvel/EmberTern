namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>The semantic role an identifier occurrence plays.</summary>
public enum ReferenceRole
{
    /// <summary>A reference to a schema object (a table/view/procedure/… name in a statement).</summary>
    SchemaObject,

    /// <summary>A FROM/JOIN table reference occurrence (the alias/table as it appears in FROM).</summary>
    TableReference,

    /// <summary>The qualifier before a dot — the <c>k</c> in <c>k.nazwa</c>.</summary>
    Qualifier,

    /// <summary>A column reference (qualified or bare).</summary>
    Column,

    /// <summary>A reference to a PSQL variable.</summary>
    Variable,

    /// <summary>A reference to a routine parameter.</summary>
    Parameter,

    /// <summary>A reference to a cursor.</summary>
    Cursor,

    /// <summary>A NEW/OLD record-alias occurrence.</summary>
    RecordAlias,

    /// <summary>A trigger context predicate occurrence — <c>INSERTING</c> / <c>UPDATING</c> /
    /// <c>DELETING</c>.</summary>
    ContextVariable,

    /// <summary>An occurrence whose role could not be determined.</summary>
    Unknown,
}

/// <summary>
/// One occurrence of an identifier in the script, with its source span, the text as written, the
/// <see cref="Symbol"/> it binds to (or <c>null</c> when unresolved — the model is error-tolerant),
/// its <see cref="ReferenceRole"/>, and whether this occurrence is the declaration itself.
/// <para>
/// This is the atom future features build on: hovering / go-to-definition map a caret offset to the
/// reference here and follow <see cref="Symbol"/>; find-references / rename gather every
/// <see cref="SymbolReference"/> bound to the same symbol; semantic highlighting colours by
/// <see cref="Role"/> and resolution status. It never mutates code, so §0 (never lose information)
/// holds by construction.
/// </para>
/// </summary>
/// <param name="Span">Where the identifier occurs in the source.</param>
/// <param name="Text">The identifier text exactly as written.</param>
/// <param name="Symbol">The symbol it binds to, or <c>null</c> when it could not be resolved.</param>
/// <param name="Role">The semantic role of the occurrence.</param>
/// <param name="IsDefinition"><c>true</c> when this occurrence is the symbol's declaration.</param>
public sealed record SymbolReference(
    TextSpan Span,
    string Text,
    Symbol? Symbol,
    ReferenceRole Role,
    bool IsDefinition = false)
{
    /// <summary><c>true</c> when the occurrence bound to a symbol.</summary>
    public bool IsResolved => Symbol is not null;
}
