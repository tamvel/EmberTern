namespace EmberTern.Core.Metadata;

/// <summary>
/// Column name + formatted SQL type (e.g. "INTEGER", "VARCHAR(50)",
/// "NUMERIC(15,2)"). Used by the SQL editor autocomplete to render
/// "COLUMN_NAME : TYPE" entries after <c>ALIAS.</c> / <c>TABLE.</c>.
/// </summary>
public sealed record ColumnSpec(string Name, string Type);
