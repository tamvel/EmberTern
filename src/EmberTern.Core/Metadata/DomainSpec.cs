using System;
using System.Globalization;

namespace EmberTern.Core.Metadata;

/// <summary>
/// A user-defined domain plus its formatted SQL type (e.g. "VARCHAR(80)") and the
/// extra attributes needed to render a rich picker row (Name | Type | Size | Scale |
/// Not Null | Charset) and to mirror the resolved type into a field's cells when the
/// domain is chosen. <see cref="Size"/>/<see cref="Scale"/>/<see cref="BaseType"/> are
/// parsed from <see cref="Type"/> so they always match the formatted string.
/// The two-arg ctor (Name, Type) is preserved so existing call sites keep working.
/// </summary>
public sealed record DomainSpec(string Name, string Type, bool NotNull = false, string? Charset = null)
{
    /// <summary>Base type name without the (size[,scale]) / SUB_TYPE suffix (e.g. "VARCHAR").</summary>
    public string BaseType
    {
        get
        {
            var paren = Type.IndexOf('(');
            if (paren >= 0) return Type[..paren].Trim();
            // BLOB SUB_TYPE n → "BLOB"
            var sub = Type.IndexOf(" SUB_TYPE", StringComparison.OrdinalIgnoreCase);
            return (sub >= 0 ? Type[..sub] : Type).Trim();
        }
    }

    /// <summary>First "(...)" argument — length for CHAR/VARCHAR, precision for
    /// NUMERIC/DECIMAL — or null when the type carries none.</summary>
    public int? Size => Arg(0);

    /// <summary>Second "(...)" argument — scale for NUMERIC/DECIMAL — or null.</summary>
    public int? Scale => Arg(1);

    private int? Arg(int index)
    {
        var open = Type.IndexOf('(');
        if (open < 0) return null;
        var close = Type.IndexOf(')', open + 1);
        if (close < 0) return null;
        var parts = Type.Substring(open + 1, close - open - 1).Split(',');
        if (index >= parts.Length) return null;
        return int.TryParse(parts[index].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
