using System;

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
    /// <summary>True when this column is auto-incremented — either a native
    /// FB3+ identity column (<c>RDB$IDENTITY_TYPE IS NOT NULL</c>) or backed
    /// by a legacy BEFORE INSERT trigger that calls <c>GEN_ID</c> with this
    /// field as target.</summary>
    public bool IsAutoIncrement { get; init; }

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
    public bool IsActive { get; init; } = true;
    public double? Statistics { get; init; }
    // RDB$EXPRESSION_SOURCE — populated only for expression indexes (computed
    // over an expression instead of a field list). Null/empty for the usual case.
    public string? Expression { get; init; }
    // "PRIMARY KEY", "FOREIGN KEY", or "" for plain indexes. Derived in the
    // reader by joining RDB$INDICES against RDB$RELATION_CONSTRAINTS. UNIQUE
    // constraints are NOT surfaced here — their backing indexes are flagged
    // through IsUnique instead.
    public string IndexType { get; init; } = string.Empty;

    public bool IsPrimary
        => string.Equals(IndexType, "PRIMARY KEY", StringComparison.OrdinalIgnoreCase);

    public bool IsForeignKeyIndex
        => string.Equals(IndexType, "FOREIGN KEY", StringComparison.OrdinalIgnoreCase);
}

public sealed record DependencyInfo
{
    public string ObjectName { get; init; } = string.Empty;
    public string ObjectType { get; init; } = string.Empty;
    public string? FieldName { get; init; }
}

public sealed class ConstraintInfo
{
    public string Name { get; init; } = string.Empty;
    // PRIMARY KEY / FOREIGN KEY / CHECK / UNIQUE — the value of
    // RDB$RELATION_CONSTRAINTS.RDB$CONSTRAINT_TYPE, trimmed.
    public string ConstraintType { get; init; } = string.Empty;
    public string Fields { get; init; } = string.Empty;
    public string RefTable { get; init; } = string.Empty;
    public string RefFields { get; init; } = string.Empty;
    // RDB$TRIGGER_SOURCE for CHECK constraints (wraps as "CHECK (...)").
    public string CheckClause { get; init; } = string.Empty;
    // Backing index name for PK / UNIQUE / FK; empty for CHECK.
    public string IndexName { get; init; } = string.Empty;
    // RDB$REF_CONSTRAINTS.RDB$UPDATE_RULE / RDB$DELETE_RULE for FK constraints
    // ("RESTRICT", "CASCADE", "SET NULL", "SET DEFAULT", "NO ACTION"); empty
    // for non-FK rows.
    public string UpdateRule { get; init; } = string.Empty;
    public string DeleteRule { get; init; } = string.Empty;
    // Sort direction of the backing index (PK / UNIQUE / FK rows). false ⇒
    // RDB$INDEX_TYPE = 0 (ASC, the default), true ⇒ 1 (DESC). Always false for
    // CHECK constraints (no backing index).
    public bool IsDescending { get; init; }

    // Single-string display of UPDATE / DELETE rules for the FK grid's
    // "Warunek" column. RESTRICT is the SQL default — suppress it from the
    // display so only meaningful rules render. Returns empty when both rules
    // are absent or both default.
    public string ForeignKeyRule
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(2);
            if (!IsDefaultRule(UpdateRule)) parts.Add($"ON UPDATE {UpdateRule.Trim()}");
            if (!IsDefaultRule(DeleteRule)) parts.Add($"ON DELETE {DeleteRule.Trim()}");
            return string.Join(", ", parts);
        }
    }

    private static bool IsDefaultRule(string? rule)
        => string.IsNullOrWhiteSpace(rule)
           || string.Equals(rule.Trim(), "RESTRICT", StringComparison.OrdinalIgnoreCase);
}
