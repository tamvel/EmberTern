using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Navigation;

/// <summary>
/// Answers "what is navigable under the caret, and where does it lead?" from a
/// <see cref="SemanticModel"/> — Etap 6 (design §5.8 / §10). A pure Core client of the language
/// front-end: it reads <b>only</b> the model (the <see cref="SymbolReference"/> at the offset and the
/// <see cref="Symbol"/> it resolved to) — never a name-based text search (which the old
/// <c>TryOpenDdlForWord</c> path did). Go-to-definition therefore follows real resolution: an alias
/// jumps to its table, a variable use to its DECLARE, a column to its owning table.
/// <para>Read-only, so §0 (never lose information) holds by construction — navigation never modifies
/// code. Error-tolerant: it never throws; a caret not on a resolvable identifier yields <c>null</c>.</para>
/// </summary>
public static class NavigationEngine
{
    /// <summary>The navigation target at <paramref name="offset"/>, or <c>null</c> when the caret is
    /// not on an identifier that resolves to something navigable (a keyword, whitespace, or an
    /// unresolved name). The App shows the Ctrl-hover affordance and enables Ctrl+Click only when
    /// this is non-null. Never throws.</summary>
    public static NavigationTarget? TargetAt(SemanticModel model, int offset)
    {
        if (model is null) return null;
        var reference = model.ReferenceAt(offset);
        if (reference?.Symbol is null) return null;
        return Classify(reference.Span, reference.Symbol);
    }

    /// <summary>Every occurrence in this script bound to the same symbol as the identifier at
    /// <paramref name="offset"/> — the local basis for find-references and rename-highlight. Empty
    /// when the offset is not on a resolved identifier. Includes the declaration occurrence.</summary>
    public static IReadOnlyList<TextSpan> LocalReferences(SemanticModel model, int offset)
    {
        var result = new List<TextSpan>();
        if (model is null) return result;
        var symbol = model.ReferenceAt(offset)?.Symbol;
        if (symbol is null) return result;
        foreach (var r in model.ReferencesTo(symbol)) result.Add(r.Span);
        return result;
    }

    /// <summary>The in-editor declaration span for the identifier at <paramref name="offset"/> when
    /// it is a local declaration (alias/variable/parameter/cursor/CTE), else <c>null</c>. A schema
    /// object is defined in the database, not the script, so it has no local definition span (use
    /// <see cref="TargetAt"/> → <see cref="NavigationTargetKind.SchemaObject"/> to open it).</summary>
    public static TextSpan? LocalDefinition(SemanticModel model, int offset)
    {
        var target = TargetAt(model, offset);
        return target?.Kind == NavigationTargetKind.LocalDefinition ? target.DefinitionSpan : null;
    }

    /// <summary>
    /// The <b>safe local</b> rename at <paramref name="offset"/>, or <c>null</c> when the caret is not
    /// on a renameable local — Etap 6 / M5 (design §10). Returns a rename ONLY for a FROM/JOIN alias
    /// (or derived-table alias), a PSQL variable/parameter, a cursor, or a CTE. A schema object, a
    /// column, a <c>NEW</c>/<c>OLD</c> record, or a table referenced by its own name yields
    /// <c>null</c> — so a rename can never alter a database object (§0). The result carries every
    /// occurrence (declaration + uses) bound to that exact symbol, so the App renames precisely those
    /// and nothing that merely shares the name. Never throws.
    /// </summary>
    public static NavigationRename? GetLocalRename(SemanticModel model, int offset)
    {
        if (model is null) return null;
        var symbol = model.ReferenceAt(offset)?.Symbol;
        if (symbol is null || !IsRenameableLocal(symbol)) return null;

        var spans = new List<TextSpan>();
        foreach (var r in model.ReferencesTo(symbol)) spans.Add(r.Span);
        if (spans.Count == 0) return null;

        return new NavigationRename(symbol, symbol.Name, spans);
    }

    // The only symbols a rename may touch: names that live entirely in the script. A table
    // referenced by its own name (IsAlias == false && IsDerived == false) is NOT renameable — that
    // identifier IS the schema object, so renaming it would either break the query or (worse) read
    // as a rename that does nothing real. NEW/OLD records are fixed PSQL keywords. Columns and
    // schema objects are database metadata — out of scope for a local rename (§0 / §10).
    private static bool IsRenameableLocal(Symbol symbol) => symbol switch
    {
        VariableSymbol => true,
        ParameterSymbol => true,
        CursorSymbol => true,
        CteSymbol => true,
        TableReferenceSymbol t => t.IsAlias || t.IsDerived,
        _ => false,
    };

    // ── Classification ─────────────────────────────────────────────────────────────────────────

    private static NavigationTarget? Classify(TextSpan span, Symbol symbol)
    {
        switch (symbol)
        {
            // A column → open its owning table (DataGrip-style "go to the table"). Calm-coloured, but
            // still navigable.
            case ColumnSymbol { OwningTable: { Length: > 0 } owner } col:
                return NavigationTarget.ForSchemaObject(span, col, owner, SymbolKind.Table);

            case SchemaObjectSymbol o:
                return NavigationTarget.ForSchemaObject(span, o, o.Name, o.Kind);

            case TableReferenceSymbol t:
                return ClassifyTableReference(span, t);

            // NEW / OLD → open the trigger's table.
            case RecordAliasSymbol { TargetTable: { Length: > 0 } table } rec:
                return NavigationTarget.ForSchemaObject(span, rec, table, SymbolKind.Table);

            // Locals declared in the script — jump to their declaration.
            case CteSymbol:
            case VariableSymbol:
            case ParameterSymbol:
            case CursorSymbol:
                return NavigationTarget.ForLocal(span, symbol, symbol.DeclarationSpan);

            default:
                return null;
        }
    }

    private static NavigationTarget? ClassifyTableReference(TextSpan span, TableReferenceSymbol t)
    {
        switch (t.Target)
        {
            // FROM alias of a real table/view → open that object.
            case SchemaObjectSymbol so:
                return NavigationTarget.ForSchemaObject(span, t, so.Name, so.Kind);

            // FROM referencing a CTE → jump to the CTE declaration in this script.
            case CteSymbol cte:
                return NavigationTarget.ForLocal(span, t, cte.DeclarationSpan);
        }

        // A named table whose object we couldn't resolve (metadata not loaded) — best-effort open by
        // name so Ctrl+Click still works; the App's name lookup handles a miss gracefully.
        if (!t.IsDerived && !string.IsNullOrEmpty(t.TargetName))
        {
            return NavigationTarget.ForSchemaObject(span, t, t.TargetName!, SymbolKind.Table);
        }

        // A derived table (aliased subquery) — its "definition" is its own declaration in the script.
        return NavigationTarget.ForLocal(span, t, t.DeclarationSpan);
    }
}
