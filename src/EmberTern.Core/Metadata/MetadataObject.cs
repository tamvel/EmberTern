namespace EmberTern.Core.Metadata;

public enum MetadataObjectKind
{
    Table,
    View,
    Procedure,
    Trigger,
    Function,
    Generator,
    Domain,
    Package,
    Exception,
    Role,
    User,
    Index,
    SystemTable,
}

public sealed record MetadataObject(string Name, MetadataObjectKind Kind)
{
    /// <summary>
    /// Activation state for the object kinds Firebird actually gives one: triggers
    /// (<c>RDB$TRIGGER_INACTIVE</c>) and indexes (<c>RDB$INDEX_INACTIVE</c>).
    /// <c>null</c> for every other kind — procedures/functions/packages have no
    /// inactive state and must never be shown a fake one. Optional init property so
    /// existing <c>new MetadataObject(name, kind)</c> call sites are unaffected.
    /// </summary>
    public bool? IsActive { get; init; }
}
