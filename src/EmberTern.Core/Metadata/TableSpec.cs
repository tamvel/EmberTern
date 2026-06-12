using System.Collections.Generic;

namespace EmberTern.Core.Metadata;

/// <summary>
/// What flavour of <c>CREATE TABLE</c> the user is requesting. Persistent is
/// the default; the two Temp variants emit
/// <c>CREATE GLOBAL TEMPORARY TABLE ... ON COMMIT {DELETE|PRESERVE} ROWS</c>.
/// </summary>
public enum TableKind
{
    Persistent,
    TempDeleteRows,
    TempPreserveRows,
}

/// <summary>
/// Inputs for <see cref="DdlGenerator.BuildCreateTable(string, TableSpec)"/>.
/// Plain mutable collections so the <c>CreateTableDialog</c> VM can bind and
/// mutate without ceremony.
/// </summary>
public sealed class TableSpec
{
    public TableKind Kind { get; set; } = TableKind.Persistent;

    /// <summary>Field definitions, in declaration order.</summary>
    public List<FieldDefinition> Fields { get; } = new();

    /// <summary>Optional table-level description — emitted as a separate
    /// <c>COMMENT ON TABLE</c> statement after the CREATE.</summary>
    public string? Description { get; set; }
}
