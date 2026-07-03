namespace EmberTern.Core.Performance;

/// <summary>Identifies the statement (or PSQL cursor) an analysis pertains to.
/// Phase 1 only produces <see cref="StatementRole.Query"/> instances; the object/source
/// fields are reserved for the procedure/function breakdown in later phases.</summary>
public sealed record StatementIdentity
{
    public required string Sql { get; init; }

    public StatementRole Role { get; init; } = StatementRole.Query;

    public string? ObjectName { get; init; }

    public int? SourceLine { get; init; }

    public int? SourceColumn { get; init; }
}
