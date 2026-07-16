namespace EmberTern.Core.Metadata;

/// <summary>
/// Column name + formatted SQL type (e.g. "INTEGER", "VARCHAR(50)",
/// "NUMERIC(15,2)"), plus the user-defined domain (when the column is domain-typed)
/// and its nullability. Used by the SQL editor autocomplete to render
/// "COLUMN_NAME : TYPE : DOMAIN" entries after <c>ALIAS.</c> / <c>TABLE.</c> and to
/// feed the completion detail pane and Quick Info (Etap 6 P2 / Package 5).
/// <para>
/// The positional fields (<see cref="Name"/>/<see cref="Type"/>/<see cref="Domain"/>/
/// <see cref="NotNull"/>) are the light, hot-path essentials; the init-only properties
/// below are the <b>rich-but-optional</b> Quick Info facts (Package 5, Stage A) — a key
/// classification, a default/computed expression, a description — filled by
/// <see cref="EmberTern.Firebird"/>'s enriched columns query. They default to empty/false
/// so every existing positional call site (<c>new ColumnSpec(name, type)</c>) is
/// unaffected, and a provider that only knows name+type still produces a valid spec.
/// </para>
/// </summary>
/// <param name="Name">Column name (catalog-cased).</param>
/// <param name="Type">Formatted SQL type.</param>
/// <param name="Domain">User-defined domain name when the column is domain-typed,
/// else <c>null</c> (anonymous <c>RDB$…</c> backing domains are normalized away).</param>
/// <param name="NotNull"><c>true</c> when the column is declared NOT NULL.</param>
public sealed record ColumnSpec(string Name, string Type, string? Domain = null, bool NotNull = false)
{
    /// <summary>The column's default-value expression (the <c>DEFAULT</c>/<c>=</c> prefix
    /// stripped), else <c>null</c>.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>The column's comment/description (<c>COMMENT ON COLUMN</c>), else <c>null</c>.</summary>
    public string? Description { get; init; }

    /// <summary><c>true</c> when the column participates in the table's PRIMARY KEY.</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary><c>true</c> when the column participates in a FOREIGN KEY.</summary>
    public bool IsForeignKey { get; init; }

    /// <summary>The referenced table for a foreign-key column, else <c>null</c>.</summary>
    public string? ForeignKeyTable { get; init; }

    /// <summary><c>true</c> when the column is <c>COMPUTED BY</c>.</summary>
    public bool IsComputed { get; init; }

    /// <summary><c>true</c> when the column is an identity column (FB3+
    /// <c>GENERATED … AS IDENTITY</c>).</summary>
    public bool IsIdentity { get; init; }
}
