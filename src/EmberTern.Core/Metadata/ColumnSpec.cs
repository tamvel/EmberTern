namespace EmberTern.Core.Metadata;

/// <summary>
/// Column name + formatted SQL type (e.g. "INTEGER", "VARCHAR(50)",
/// "NUMERIC(15,2)"), plus the user-defined domain (when the column is domain-typed)
/// and its nullability. Used by the SQL editor autocomplete to render
/// "COLUMN_NAME : TYPE : DOMAIN" entries after <c>ALIAS.</c> / <c>TABLE.</c> and to
/// feed the completion detail pane (Etap 6 P2).
/// </summary>
/// <param name="Name">Column name (catalog-cased).</param>
/// <param name="Type">Formatted SQL type.</param>
/// <param name="Domain">User-defined domain name when the column is domain-typed,
/// else <c>null</c> (anonymous <c>RDB$…</c> backing domains are normalized away).</param>
/// <param name="NotNull"><c>true</c> when the column is declared NOT NULL.</param>
public sealed record ColumnSpec(string Name, string Type, string? Domain = null, bool NotNull = false);
