using System;

namespace EmberTern.Core.Metadata;

/// <summary>
/// Full read of a single index (by name) for the dedicated Index Detail surface.
/// Distinct from <see cref="IndexInfo"/> (the Table Detail grid row): it carries the
/// owning <see cref="Table"/>, the <see cref="Description"/> (COMMENT ON INDEX), and
/// <see cref="IsSystem"/>, and its <see cref="ConstraintType"/> captures UNIQUE in
/// addition to PRIMARY/FOREIGN KEY (the grid model narrows to PK/FK).
///
/// An index is almost entirely read-only in Firebird — verified on FB 5.0.3: the only
/// mutable properties are Active/Inactive (plain indexes only; PK/FK/UNIQUE backing
/// indexes reject deactivation) and the description (COMMENT ON INDEX). Everything
/// structural (fields, UNIQUE, sort direction, expression) requires DROP + CREATE;
/// there is no ALTER for it, and no rename (ALTER INDEX … TO is unsupported).
/// </summary>
public sealed class IndexDetailInfo
{
    public string Name { get; init; } = string.Empty;
    public string Table { get; init; } = string.Empty;
    /// <summary>Comma-joined indexed column names (from RDB$INDEX_SEGMENTS). Empty
    /// for an expression index — see <see cref="Expression"/>.</summary>
    public string Fields { get; init; } = string.Empty;
    public bool IsUnique { get; init; }
    public bool IsDescending { get; init; }
    public bool IsActive { get; init; } = true;
    /// <summary>Index selectivity (RDB$STATISTICS). Null when never computed
    /// (Firebird's -1 sentinel is normalized to null by the reader).</summary>
    public double? Statistics { get; init; }
    /// <summary>RDB$EXPRESSION_SOURCE (already parenthesized, e.g. "(UPPER(NAME))")
    /// for an expression index; null/empty for the usual field-list index.</summary>
    public string? Expression { get; init; }
    /// <summary>"PRIMARY KEY", "FOREIGN KEY", "UNIQUE", or "" for a plain user index.
    /// Drives <see cref="IsConstraintBacked"/>.</summary>
    public string ConstraintType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    /// <summary>RDB$SYSTEM_FLAG ≠ 0.</summary>
    public bool IsSystem { get; init; }

    /// <summary>True when this index backs a PRIMARY KEY / UNIQUE / FOREIGN KEY
    /// constraint. Firebird manages such indexes through the constraint — they
    /// cannot be deactivated or dropped directly (verified on FB 5.0.3), so the
    /// Active toggle and Drop action are disabled for them in the UI.</summary>
    public bool IsConstraintBacked => !string.IsNullOrEmpty(ConstraintType);
}
