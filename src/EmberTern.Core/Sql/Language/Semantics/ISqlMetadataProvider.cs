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

    /// <summary>
    /// Whether this snapshot <b>knows</b> the column set of <paramref name="tableOrView"/> — i.e. whether an
    /// empty <see cref="GetColumns"/> result means "this object has no such column" rather than "its columns
    /// have not been loaded yet".
    /// <para>
    /// ⭐⭐ IT EXISTS BECAUSE AN EMPTY RESULT IS NOT A DECIDABLE SIGNAL, and the doc comment above says so in
    /// its own words: <em>"when the object is unknown OR has no columns loaded yet"</em>. Those are opposite
    /// facts, and a consumer that must not guess needs to tell them apart. Columns are loaded lazily, so at
    /// the moment a tab opens the snapshot typically knows the object and none of its columns — and
    /// <c>DiagnosticsEngine</c> read that as "the column does not exist", squiggling practically every
    /// qualified column in the document until the warm pass finished and the model was rebuilt. That is the
    /// reported "everything is underlined for a moment, then the errors disappear" (S-2, 2026-08-05), and it
    /// was a breach of the engine's own conservatism rule ("prefer silence over false positives") built into
    /// the contract rather than into the engine.
    /// </para>
    /// <para>
    /// ⚠ The default is <c>true</c> — "unless a provider says otherwise, assume it knows" — so every
    /// implementation that cannot distinguish the two states keeps today's behaviour exactly, and a
    /// provider must opt IN to reporting ignorance. A default of <c>false</c> would silence a real
    /// UnknownColumn everywhere instead, which is the worse failure: a diagnostic that never fires is
    /// indistinguishable from a diagnostic that does not exist.
    /// </para>
    /// <para>
    /// ⚠ It answers about a <b>named object</b>, not about the snapshot as a whole: columns arrive
    /// per object, so "am I ready" has no useful global answer.
    /// </para>
    /// </summary>
    bool KnowsColumns(string tableOrView) => true;

    /// <summary>The parameters of the procedure or function named <paramref name="routine"/>
    /// (inputs and outputs; direction is on each row). Empty when unknown.</summary>
    IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine);

    /// <summary>Every schema object the snapshot knows about — the material for baseline
    /// completion ("list all tables/views/procedures/…"). Point lookups (<see cref="FindObject"/>)
    /// serve the binder; this enumeration serves the completion engine (Etap 5). Returns an empty
    /// list when the snapshot has no catalog (e.g. no active connection).</summary>
    IReadOnlyList<ObjectMetadata> AllObjects();
}

/// <summary>Rich-but-optional metadata about a schema object. The positional fields are the always-cheap
/// essentials (name + kind); the init-only properties are the richer Quick Info facts an implementation
/// fills when it has warmed them (Package 5) — a description, a function's return type, a trigger's
/// header. They default to <c>null</c>, so a provider that only knows name+kind is still valid.</summary>
/// <param name="Name">The object name (catalog-cased).</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Description">Comment/description, when known.</param>
/// <param name="Owner">Owner/creator, when known.</param>
public sealed record ObjectMetadata(
    string Name,
    SymbolKind Kind,
    string? Description = null,
    string? Owner = null)
{
    /// <summary>A function's formatted return type (e.g. <c>INTEGER</c>, <c>VARCHAR(50)</c>), else
    /// <c>null</c>. Meaningful only for <see cref="SymbolKind.Function"/>.</summary>
    public string? ReturnType { get; init; }

    /// <summary>Header facts for a <see cref="SymbolKind.Trigger"/> (table, timing, events, position,
    /// active), else <c>null</c>.</summary>
    public TriggerDetail? Trigger { get; init; }

    /// <summary>Static definition facts for a <see cref="SymbolKind.Sequence"/> (start value +
    /// increment — never the dynamic current value), else <c>null</c>.</summary>
    public GeneratorDetail? Generator { get; init; }
}

/// <summary>Rich-but-optional header facts of a relation trigger, for Quick Info (Package 5, Stage C).
/// Decoded from the catalog by the host; the language layer only renders it.</summary>
/// <param name="Table">The table the trigger fires on, or <c>null</c> for a DB-level/DDL trigger.</param>
/// <param name="IsBefore"><c>true</c> = BEFORE, <c>false</c> = AFTER.</param>
/// <param name="FiresInsert">Fires on INSERT.</param>
/// <param name="FiresUpdate">Fires on UPDATE.</param>
/// <param name="FiresDelete">Fires on DELETE.</param>
/// <param name="Position">Firing position (RDB$TRIGGER_SEQUENCE).</param>
/// <param name="Active"><c>true</c> when the trigger is active (not <c>INACTIVE</c>).</param>
public sealed record TriggerDetail(
    string? Table,
    bool IsBefore,
    bool FiresInsert,
    bool FiresUpdate,
    bool FiresDelete,
    int Position,
    bool Active);

/// <summary>Rich-but-optional static definition facts of a generator/sequence, for Quick Info
/// (Package 5). <b>Never</b> the current value (dynamic) — only the creation-time start and increment.</summary>
/// <param name="StartValue">The sequence's initial value (<c>START WITH</c>); 0 by default / on FB2.5.</param>
/// <param name="Increment">The step (<c>INCREMENT BY</c>); 1 by default / on FB2.5.</param>
public sealed record GeneratorDetail(long StartValue, long Increment);

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

    /// <summary>How the column's identity value is generated. ALWAYS vs BY DEFAULT decides whether
    /// generated DML must emit <c>OVERRIDING SYSTEM VALUE</c> — see
    /// <see cref="EmberTern.Core.Metadata.IdentityKind"/>.</summary>
    public EmberTern.Core.Metadata.IdentityKind Identity { get; init; }

    /// <summary><c>true</c> when the column is an identity column. Derived from <see cref="Identity"/>,
    /// never stored beside it.</summary>
    public bool IsIdentity => Identity != EmberTern.Core.Metadata.IdentityKind.None;
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
