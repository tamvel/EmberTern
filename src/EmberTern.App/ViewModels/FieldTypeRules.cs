using System;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Which base Firebird types carry which "(...)" arguments. Shared by every field-row
/// VM (Procedure/Trigger via <see cref="ProcedureFieldRowBase"/>, Table Detail
/// <see cref="FieldRowViewModel"/>, New Table <c>NewTableFieldRowViewModel</c>) so the
/// grids agree on when Size/Scale/Sub Type apply — and so changing the type away from a
/// size-bearing type clears the now-irrelevant Size/Scale/Sub Type cells.
/// </summary>
internal static class FieldTypeRules
{
    /// <summary>CHAR/VARCHAR/CSTRING (length) and NUMERIC/DECIMAL (precision) take a Size.</summary>
    public static bool UsesSize(string? baseType)
    {
        var b = baseType?.Trim().ToUpperInvariant();
        return b is "CHAR" or "VARCHAR" or "CSTRING" or "NUMERIC" or "DECIMAL";
    }

    /// <summary>Only NUMERIC/DECIMAL take a Scale.</summary>
    public static bool UsesScale(string? baseType)
    {
        var b = baseType?.Trim().ToUpperInvariant();
        return b is "NUMERIC" or "DECIMAL";
    }

    /// <summary>Only BLOB takes a Sub Type.</summary>
    public static bool UsesSubType(string? baseType)
        => string.Equals(baseType?.Trim(), "BLOB", StringComparison.OrdinalIgnoreCase);
}
