using System.Collections.Generic;

namespace EmberTern.Core.Metadata;

/// <summary>
/// One input or output parameter of a stored procedure, sourced from
/// <c>RDB$PROCEDURE_PARAMETERS</c> joined to <c>RDB$FIELDS</c>. Direction is
/// implied by which list the row lands in (the reader queries input and output
/// separately), so this carries no direction flag. Read-only metadata — the
/// Procedure Detail parameter grids never edit it.
/// </summary>
public sealed class ProcedureParameterInfo
{
    /// <summary>1-based display position within its direction (in / out).</summary>
    public int Position { get; init; }
    public string Name { get; init; } = string.Empty;
    /// <summary>Formatted SQL type, e.g. <c>VARCHAR(50)</c> / <c>BLOB SUB_TYPE 0</c>.</summary>
    public string Type { get; init; } = string.Empty;
    public bool NotNull { get; init; }
    public string? DefaultValue { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// A function's catalog signature, sourced from <c>RDB$FUNCTIONS</c> +
/// <c>RDB$FUNCTION_ARGUMENTS</c>. <see cref="Arguments"/> are the input arguments
/// (the return value is split off via <c>RDB$RETURN_ARGUMENT</c>); <see cref="ReturnType"/>
/// is the single formatted return type. Read-only metadata — the Function Detail
/// Arguments/Result grids load from this and edit a separate row model.
/// </summary>
public sealed class FunctionSignatureInfo
{
    public IReadOnlyList<ProcedureParameterInfo> Arguments { get; init; } = System.Array.Empty<ProcedureParameterInfo>();
    /// <summary>Formatted return type, e.g. <c>INTEGER</c> / <c>NUMERIC(15,2)</c>.</summary>
    public string ReturnType { get; init; } = string.Empty;
    public bool Deterministic { get; init; }
}
