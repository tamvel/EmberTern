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

public sealed record MetadataObject(string Name, MetadataObjectKind Kind);
