namespace EmberTern.Core.Metadata;

/// <summary>
/// A Firebird user-defined domain as surfaced in the Domain Detail editor. Plain
/// init-only POCO, zero Avalonia deps. The structured parts (DataType / Length /
/// Precision / Scale / SubType / CharacterSet / Collation / Default / Check /
/// NotNull) drive both the read-only form display and <see cref="DdlGenerator.BuildCreateDomain"/>.
///
/// Firebird ALTER DOMAIN can only change DEFAULT, the CHECK constraint, NOT NULL,
/// the type, and the name — NEVER the CHARACTER SET or COLLATION (verified on
/// FB3 + FB5: <c>ALTER DOMAIN … SET CHARACTER SET / COLLATE</c> → SQL error -104).
/// So charset/collation are fixed at creation. EmberTern's editable scope is
/// Default / Check / Description; the rest is editable only in the New flow.
/// </summary>
public sealed class DomainInfo
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Base type name without the (size[,scale]) / SUB_TYPE suffix
    /// (e.g. "VARCHAR", "INTEGER", "NUMERIC", "BLOB").</summary>
    public string DataType { get; init; } = string.Empty;

    /// <summary>Character length for CHAR/VARCHAR/CSTRING (RDB$CHARACTER_LENGTH,
    /// not the byte length); null for non-char types.</summary>
    public int? Length { get; init; }

    /// <summary>NUMERIC/DECIMAL precision; null otherwise.</summary>
    public int? Precision { get; init; }

    /// <summary>NUMERIC/DECIMAL scale (positive); null otherwise.</summary>
    public int? Scale { get; init; }

    /// <summary>BLOB sub-type; null for non-BLOB types.</summary>
    public int? SubType { get; init; }

    /// <summary>Character set name (char/blob types), or null.</summary>
    public string? CharacterSet { get; init; }

    /// <summary>Collation name (char types), or null.</summary>
    public string? Collation { get; init; }

    /// <summary>Default value source with the leading "DEFAULT " keyword stripped,
    /// or null when the domain has no default.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>The CHECK constraint source (RDB$VALIDATION_SOURCE, e.g.
    /// "CHECK (VALUE &gt; 0)"), or null when the domain has no check.</summary>
    public string? CheckConstraint { get; init; }

    public bool NotNull { get; init; }

    public string Description { get; init; } = string.Empty;
}
