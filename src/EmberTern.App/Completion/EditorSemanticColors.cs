using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Completion;

/// <summary>
/// Maps a resolved <see cref="SymbolKind"/> to the theme resource key of the brush that colours it
/// in the editor. Schema objects reuse the metadata tree's per-kind <c>IconColor_*</c> palette so an
/// object's editor colour matches its tree icon (design §9.2 — "coloured object = navigable").
/// <para>
/// Shared by the semantic highlighter (paints identifier occurrences) and the Quick Info surface
/// (colours a card header by kind), so the two never drift. Kept in the App layer because the
/// resource-key strings are a theme-dictionary concern, not a Core one.
/// </para>
/// </summary>
internal static class EditorSemanticColors
{
    /// <summary>The <c>IconColor_*</c> theme resource key for a navigable schema-object kind, or
    /// <c>null</c> for a kind that has no dedicated palette entry (falls back to the default
    /// foreground).</summary>
    public static string? ObjectBrushKey(SymbolKind kind) => kind switch
    {
        SymbolKind.Table => "IconColor_Table",
        SymbolKind.View => "IconColor_View",
        SymbolKind.SystemTable => "IconColor_SystemTable",
        SymbolKind.Procedure => "IconColor_Procedure",
        SymbolKind.Function => "IconColor_Function",
        SymbolKind.Trigger => "IconColor_Trigger",
        SymbolKind.Domain => "IconColor_Domain",
        SymbolKind.Exception => "IconColor_Exception",
        SymbolKind.Sequence => "IconColor_Generator",
        SymbolKind.Package => "IconColor_Package",
        SymbolKind.Index => "IconColor_Index",
        SymbolKind.Role => "IconColor_Role",
        _ => null,
    };
}
