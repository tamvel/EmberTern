using EmberTern.Core.Metadata;

namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>
/// Pure mapping from the metadata-tree object kind (<see cref="MetadataObjectKind"/>) to the
/// semantic-model <see cref="SymbolKind"/>. Both enums are Core, so this lives in Core — the App's
/// <c>ISqlMetadataProvider</c> snapshot uses it to translate the loaded metadata objects into the
/// vocabulary the semantic model and completion engine speak. Kept as a single source of truth so
/// the two enums can't drift apart in scattered <c>switch</c>es across the App.
/// </summary>
public static class MetadataSymbolMap
{
    /// <summary>Maps a metadata object kind to its semantic <see cref="SymbolKind"/>. A
    /// <see cref="MetadataObjectKind.User"/> has no schema-object symbol kind (users are a security
    /// concept, not something a SQL identifier resolves to), so it maps to
    /// <see cref="SymbolKind.Unknown"/> and callers may skip it.</summary>
    public static SymbolKind ToSymbolKind(this MetadataObjectKind kind) => kind switch
    {
        MetadataObjectKind.Table => SymbolKind.Table,
        MetadataObjectKind.View => SymbolKind.View,
        MetadataObjectKind.SystemTable => SymbolKind.SystemTable,
        MetadataObjectKind.Procedure => SymbolKind.Procedure,
        MetadataObjectKind.Function => SymbolKind.Function,
        MetadataObjectKind.Trigger => SymbolKind.Trigger,
        MetadataObjectKind.Domain => SymbolKind.Domain,
        MetadataObjectKind.Exception => SymbolKind.Exception,
        MetadataObjectKind.Generator => SymbolKind.Sequence,
        MetadataObjectKind.Role => SymbolKind.Role,
        MetadataObjectKind.Package => SymbolKind.Package,
        MetadataObjectKind.Index => SymbolKind.Index,
        _ => SymbolKind.Unknown,
    };
}
