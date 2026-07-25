using System.Collections.Generic;

namespace EmberTern.Core.Sql.Debugging;

// Stage X — Firebird Debugger, milestone D2 seam (b): the Evaluation Harness input/output model (spec §3.2).
// The harness is the ONE server mechanism (§3.3): every step, condition, watch and evaluation is the same
// generated anonymous EXECUTE BLOCK with a different fragment. HarnessBuilder is a PURE function
// HarnessRequest → HarnessResult — zero Avalonia, zero FirebirdSql. The pieces it assembles (the fragment
// text, each variable's verbatim declaration and base type, the current values, the sub-routine
// declarations, the read/write set) are INPUTS: the Firebird executor (seam c) derives them from metadata +
// the frame; tests supply them directly. That decoupling is what keeps the §3.4 rules unit-testable without
// a live server.

/// <summary>Whether the harness runs a <see cref="HarnessMode.Statement"/> (a PSQL statement, verbatim) or
/// evaluates an <see cref="HarnessMode.Expression"/> (a boolean condition / a watch expression) into a
/// result column.</summary>
public enum HarnessMode
{
    /// <summary>Run the fragment as a statement (a step / a DML leaf).</summary>
    Statement,

    /// <summary>Evaluate the fragment as an expression into the result column (a condition / a watch).</summary>
    Expression,
}

/// <summary>
/// One frame variable as the harness needs it. <see cref="Declaration"/> is the <b>verbatim</b> source
/// declaration (R3 — domain, <c>TYPE OF</c>, <c>NOT NULL</c>, <c>CHECK</c>, default: all of it, copied from
/// source); <see cref="BaseType"/> is the underlying <b>base type</b> used for a harness parameter /
/// <c>RETURNS</c> column (R2 — never a domain, so a base-typed param skips domain re-validation on injection
/// and a legitimately-<c>NULL</c> variable survives write-back). <see cref="Value"/> + <see cref="HasValue"/>
/// are the current value to inject (R1 — a <c>NULL</c>/absent value is never injected: a declared variable is
/// already <c>NULL</c>).
/// </summary>
public sealed record HarnessVariable(
    string Name,
    string Declaration,
    string BaseType,
    object? Value = null,
    bool HasValue = false)
{
    /// <summary>True when this variable has a non-null current value to inject (R1: only these are injected).</summary>
    public bool IsInjectable => HasValue && Value is not null;
}

/// <summary>
/// The request for one harness (spec §3.2/§3.4/§3.5). Carries the verbatim <see cref="Fragment"/>, the frame
/// <see cref="Variables"/> (declared verbatim), the <see cref="Reads"/> to inject and <see cref="Writes"/> to
/// return (from <see cref="ReadWriteSetAnalyzer"/>, R4), and the in-scope <see cref="SubRoutines"/> (R5 —
/// carried in full regardless of the read set). In <see cref="HarnessMode.Expression"/> mode
/// <see cref="ExpressionResultType"/> is the base type of the evaluated value's result column.
/// </summary>
public sealed record HarnessRequest
{
    /// <summary>The verbatim statement (Statement mode) or expression (Expression mode) to run.</summary>
    public required string Fragment { get; init; }

    /// <summary>Statement vs expression mode.</summary>
    public HarnessMode Mode { get; init; } = HarnessMode.Statement;

    /// <summary>The base type of the evaluated value's result column (Expression mode only; e.g.
    /// <c>BOOLEAN</c> for an <c>IF</c>/<c>WHILE</c> condition).</summary>
    public string? ExpressionResultType { get; init; }

    /// <summary>The frame variables to declare (verbatim, R3). Must include every variable the fragment
    /// references; the executor passes the frame's in-scope declarations.</summary>
    public IReadOnlyList<HarnessVariable> Variables { get; init; } = System.Array.Empty<HarnessVariable>();

    /// <summary>The variable names to inject (the read set, R4) — a non-null value among these is bound as a
    /// base-typed parameter and assigned into its variable (R1/R2).</summary>
    public IReadOnlyList<string> Reads { get; init; } = System.Array.Empty<string>();

    /// <summary>The variable names to return for write-back (the write set, R4).</summary>
    public IReadOnlyList<string> Writes { get; init; } = System.Array.Empty<string>();

    /// <summary>The in-scope sub-routine declarations, verbatim (R5 — carried in full, always).</summary>
    public IReadOnlyList<string> SubRoutines { get; init; } = System.Array.Empty<string>();
}

/// <summary>One write-back binding: the harness <c>RETURNS</c> column that carries a variable's post-run
/// value, and the frame variable to write it into.</summary>
public sealed record HarnessWriteBack(string Column, string Variable);

/// <summary>
/// The built harness (spec §3.2). <see cref="Sql"/> is the anonymous <c>EXECUTE BLOCK</c>;
/// <see cref="Parameters"/> are the values to bind to its <c>?</c> placeholders, in order (only the injected
/// non-null reads — R1); <see cref="WriteBacks"/> map each <c>RETURNS</c> write-back column to its frame
/// variable; <see cref="ResultColumn"/> is the evaluated value's column name in Expression mode (null in
/// Statement mode).
/// </summary>
public sealed record HarnessResult(
    string Sql,
    IReadOnlyList<object?> Parameters,
    IReadOnlyList<HarnessWriteBack> WriteBacks,
    string? ResultColumn);
