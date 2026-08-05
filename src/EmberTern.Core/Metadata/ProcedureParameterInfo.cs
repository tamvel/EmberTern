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
    /// <summary>Formatted SQL type, e.g. <c>VARCHAR(50)</c> / <c>BLOB SUB_TYPE 0</c>. Always the
    /// <b>resolved base</b> type, including when <see cref="Domain"/> governs it — so the grid can show
    /// the effective type beside the domain name, exactly as the table field grid does.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// User-defined domain name when the parameter is domain-typed, else <c>null</c> (an inline type's
    /// anonymous <c>RDB$n</c> backing domain is not one).
    /// <para>
    /// ⭐ Additive, and deliberately a SEPARATE field rather than a domain name folded into
    /// <see cref="Type"/> — that is the shape <see cref="ColumnSpec.Domain"/> has always had for table
    /// columns, and reusing it means the parameter grid needs no second convention. It also sidesteps a
    /// real corruption risk: a domain name folded into the type text would have to be quoted, and the
    /// row model re-quotes a domain it holds, so a case-sensitive domain could come back double-quoted.
    /// </para>
    /// <para>
    /// ⚠⚠ Its absence was a rule #11 defect, not a missing nicety (S-1b, 2026-08-05): the Easy-mode
    /// parameter grid loads from here and Compile reassembles the whole <c>CREATE OR ALTER</c> from the
    /// grid, so a domain that did not survive the READ was written back as its base type — destroying
    /// the domain link in the database for a user who only edited the body.
    /// </para>
    /// </summary>
    public string? Domain { get; init; }

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
    /// <summary>Formatted return type, e.g. <c>INTEGER</c> / <c>NUMERIC(15,2)</c> — always the resolved
    /// base type, even when <see cref="ReturnDomain"/> governs it.</summary>
    public string ReturnType { get; init; } = string.Empty;

    /// <summary>User-defined domain name when the function returns a domain, else <c>null</c>.
    /// <para>⚠ Measured on FB5: <c>RDB$FUNCTION_ARGUMENTS</c> carries the domain on the
    /// <c>RDB$RETURN_ARGUMENT</c> position as well as on the input arguments, so the domain was being
    /// lost in BOTH places — <c>RETURNS D_NAME</c> came back as <c>RETURNS VARCHAR(60)</c>.</para></summary>
    public string? ReturnDomain { get; init; }

    public bool Deterministic { get; init; }
}
