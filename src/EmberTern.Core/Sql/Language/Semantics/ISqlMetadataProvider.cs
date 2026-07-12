using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>
/// The database-metadata snapshot the semantic model binds against. Deliberately <b>synchronous and
/// pure</b>: the semantic model is a Core component with no driver access, so it consumes whatever
/// the host has already cached and never blocks on I/O. The App implements this over its existing
/// metadata caches/readers (Etap 5 wiring); Core ships the interface, its DTOs, and
/// <see cref="EmptyMetadataProvider"/>. When metadata is unavailable the model still binds every
/// local scope and records references — schema symbols simply stay unresolved (the model is
/// metadata-optional and error-tolerant).
/// <para>
/// The DTOs carry rich-but-optional facts so future Quick Info can grow into them without changing
/// this interface: an implementation may populate only what it has (e.g. name + type for columns)
/// and leave the rest <c>null</c>/<c>false</c>.
/// </para>
/// </summary>
public interface ISqlMetadataProvider
{
    /// <summary>Looks up a schema object by name (case-insensitive), or returns <c>null</c> when
    /// no such object is known.</summary>
    ObjectMetadata? FindObject(string name);

    /// <summary>The columns of the table or view named <paramref name="tableOrView"/>. Returns an
    /// empty list when the object is unknown or has no columns loaded yet.</summary>
    IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView);

    /// <summary>The parameters of the procedure or function named <paramref name="routine"/>
    /// (inputs and outputs; direction is on each row). Empty when unknown.</summary>
    IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine);

    /// <summary>Every schema object the snapshot knows about — the material for baseline
    /// completion ("list all tables/views/procedures/…"). Point lookups (<see cref="FindObject"/>)
    /// serve the binder; this enumeration serves the completion engine (Etap 5). Returns an empty
    /// list when the snapshot has no catalog (e.g. no active connection).</summary>
    IReadOnlyList<ObjectMetadata> AllObjects();
}

/// <summary>Rich-but-optional metadata about a schema object.</summary>
/// <param name="Name">The object name (catalog-cased).</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Description">Comment/description, when known.</param>
/// <param name="Owner">Owner/creator, when known.</param>
public sealed record ObjectMetadata(
    string Name,
    SymbolKind Kind,
    string? Description = null,
    string? Owner = null);

/// <summary>Rich-but-optional metadata about a column. An implementation may fill only
/// <see cref="Name"/> + <see cref="Type"/> today.</summary>
/// <param name="Name">Column name (catalog-cased).</param>
/// <param name="Type">Formatted SQL type, e.g. <c>VARCHAR(50)</c>.</param>
public sealed record ColumnMetadata(string Name, string Type)
{
    public string? Domain { get; init; }
    public bool? Nullable { get; init; }
    public string? DefaultValue { get; init; }
    public string? Description { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsForeignKey { get; init; }
    public string? ForeignKeyTable { get; init; }
    public bool IsComputed { get; init; }
    public bool IsIdentity { get; init; }
}

/// <summary>Rich-but-optional metadata about a routine parameter.</summary>
/// <param name="Name">Parameter name (catalog-cased).</param>
/// <param name="Type">Formatted SQL type.</param>
/// <param name="Direction">Input or output.</param>
public sealed record RoutineParameterMetadata(
    string Name,
    string Type,
    ParameterDirection Direction)
{
    public bool? Nullable { get; init; }
    public string? DefaultValue { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// The null-object metadata provider — knows nothing. Used when the model is built without a
/// connection (or in tests that only exercise local-scope binding). Every lookup returns
/// "not found"; the semantic model still binds aliases, variables, parameters, CTEs, cursors, and
/// records references — schema symbols just stay unresolved.
/// </summary>
public sealed class EmptyMetadataProvider : ISqlMetadataProvider
{
    /// <summary>The shared instance.</summary>
    public static readonly EmptyMetadataProvider Instance = new();

    private EmptyMetadataProvider() { }

    public ObjectMetadata? FindObject(string name) => null;

    public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
        => System.Array.Empty<ColumnMetadata>();

    public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
        => System.Array.Empty<RoutineParameterMetadata>();

    public IReadOnlyList<ObjectMetadata> AllObjects() => System.Array.Empty<ObjectMetadata>();
}
