namespace EmberTern.Core.Metadata;

public sealed class FieldInfo
{
    public int Position { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public int? Size { get; init; }
    public int? Scale { get; init; }
    public bool NotNull { get; init; }
    public string? DefaultValue { get; init; }
    public string? ComputedSource { get; init; }
    public string? Description { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsForeignKey { get; init; }
    public bool IsUnique { get; init; }
    public string? Domain { get; init; }
    public string? Charset { get; init; }
    public string? ForeignKeyTable { get; init; }

    // Firebird stores RDB$FIELD_POSITION as 0-based; the grid shows 1-based.
    public int DisplayPosition => Position + 1;

    // The grid's Typ column shows just the type name — size/scale are in their
    // own columns. "VARCHAR(255)" → "VARCHAR", "NUMERIC(15,2)" → "NUMERIC",
    // "DOUBLE PRECISION" → "DOUBLE PRECISION" (no parens, passes through).
    public string BaseTypeName
    {
        get
        {
            if (string.IsNullOrEmpty(Type)) return string.Empty;
            var paren = Type.IndexOf('(');
            return paren < 0 ? Type : Type.Substring(0, paren).TrimEnd();
        }
    }
}

public sealed class IndexInfo
{
    public string Name { get; init; } = string.Empty;
    public string Fields { get; init; } = string.Empty;
    public bool IsUnique { get; init; }
    public bool IsDescending { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed class ConstraintInfo
{
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Fields { get; init; } = string.Empty;
    public string RefTable { get; init; } = string.Empty;
    public string RefFields { get; init; } = string.Empty;
    public string CheckSource { get; init; } = string.Empty;
}
