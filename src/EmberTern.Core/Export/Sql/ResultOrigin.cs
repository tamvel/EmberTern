using System;
using System.Collections.Generic;

namespace EmberTern.Core.Export.Sql;

/// <summary>
/// <b>Signal A, per column</b> — what the server itself says a result column came from, read from the
/// driver's <c>GetSchemaTable()</c> (its own XSQLDA). Alias-transparent: <c>select NAME as CUSTOMER_NAME
/// from CUSTOMERS</c> yields <c>BaseColumn = NAME</c>, so generation de-aliases through this and never
/// through the grid header.
/// </summary>
/// <param name="BaseTable">The base table the column came from, or null/empty for a derived expression.
/// <b>An empty value is the reliable "derived expression" signal</b> — <em>not</em>
/// <paramref name="IsComputed"/>, which the driver reports <c>false</c> for <c>CUSTOMER_ID * 2</c>.</param>
/// <param name="BaseColumn">The column's real name on <paramref name="BaseTable"/>. <b>Garbage when
/// <see cref="IsDerivedExpression"/></b> — the driver puts an operator name there (<c>MULTIPLY</c>,
/// <c>COUNT</c>, <c>CONSTANT</c>). Never emit it without checking.</param>
/// <param name="IsComputed">The driver's <c>IsExpression</c> — which, despite the name, reliably marks
/// only a <c>COMPUTED BY</c> column. That is the one thing the flag is good for, and it is what excludes
/// the column from INSERT/UPDATE (Firebird rejects writing one: <em>attempted update of read-only
/// column</em>).</param>
/// <param name="ValueKind">The column's declared Firebird type, reduced to what literal rendering needs.</param>
public sealed record ColumnOrigin(
    string? BaseTable,
    string? BaseColumn,
    bool IsComputed,
    SqlValueKind ValueKind)
{
    /// <summary>The column is an expression, not a table column — it has no base table, and its
    /// <see cref="BaseColumn"/> is an operator name rather than a column name.</summary>
    public bool IsDerivedExpression => string.IsNullOrEmpty(BaseTable);
}

/// <summary>
/// <b>Signal B as facts</b> — what the executed statement's AST says about the result's shape, reduced to
/// the questions the resolver asks. This is how a source declares B <em>without knowing about ASTs</em>;
/// <see cref="StatementShapeReader"/> produces it, <see cref="ResultOriginResolver"/> judges it.
/// <para>
/// B is not a nicety — it is the <b>only</b> signal that catches a UNION, where the server reports a
/// clean, key-complete single-table result for leg 1 alone and A would happily authorise an UPDATE
/// against the wrong table's rows. No amount of schema metadata detects that.
/// </para>
/// <para>The facts are of the <em>effective</em> query: a derived table is transparent (its inner shape
/// is analysed and folded in), so a UNION hiding inside <c>from (select … union all select …) x</c> is
/// still reported as a set operation.</para>
/// </summary>
public sealed record StatementShape
{
    /// <summary>The parser could not confidently model the statement (a <c>RawStatement</c>/<c>RawQuery</c>,
    /// a non-SELECT, or more than one statement). Uncertainty ⇒ do nothing, so this vetoes rather than
    /// defaulting to permit.</summary>
    public static readonly StatementShape NotUnderstood = new();

    /// <summary>False ⇒ every other fact here is meaningless and the result is refused.</summary>
    public bool IsUnderstood { get; init; }

    /// <summary>A <c>UNION</c> / <c>INTERSECT</c> / <c>EXCEPT</c> at any effective level.</summary>
    public bool IsSetOperation { get; init; }

    /// <summary>A <c>WITH</c>-led query. Resolving which table a CTE reference ultimately reads needs
    /// semantic name resolution the shape reader deliberately does not do <em>yet</em>, so this refuses
    /// rather than guesses. Unlike the facts around it, this one is a limit of EmberTern's analysis, not
    /// of the query — see <see cref="ExportUnavailableCode.CommonTableExpression"/> for the wording that
    /// obligation puts on the message.</summary>
    public bool IsWithQuery { get; init; }

    /// <summary>A <c>GROUP BY</c> — the row is an aggregate, not a table row. (An aggregate <em>without</em>
    /// a GROUP BY needs no fact here: its columns have no base table, so signal A refuses it.)</summary>
    public bool HasGroupBy { get; init; }

    /// <summary>The single FROM entry is a join. Also what catches a <b>self-join</b>, where one base
    /// table name hides two different row instances.</summary>
    public bool HasJoin { get; init; }

    /// <summary>Top-level (comma-separated) FROM entries. 1 is the only shape that can be resolved;
    /// 0 is a FROM-less SELECT, more than 1 is a cross product.</summary>
    public int FromItemCount { get; init; }
}

/// <summary>
/// <b>Signal B</b> — how a data source declares the shape its rows came from, without knowing about ASTs.
/// The three cases are the three kinds of grid EmberTern has:
/// <list type="bullet">
/// <item><see cref="DirectTable"/> — the grid <em>is</em> a table (Table Data). Strictly safer than a
/// statement: nothing is inferred, so B is trivially satisfied.</item>
/// <item><see cref="Statement"/> — the grid holds a statement's result (SQL Editor); B is the AST's
/// verdict on whether A can be trusted.</item>
/// <item><see cref="NotATable"/> — the rows are not a table's rows at all (procedure results). A
/// permanent, honest veto, not a missing feature.</item>
/// </list>
/// </summary>
public abstract record OriginShape
{
    private protected OriginShape() { }

    /// <summary>The rows are a named table's rows, first-hand.</summary>
    public sealed record DirectTable(string TableName) : OriginShape;

    /// <summary>The rows are a statement's result; <paramref name="Shape"/> is the AST's facts.</summary>
    public sealed record Statement(StatementShape Shape) : OriginShape;

    /// <summary>The rows can never be a table's rows — carry the reason so the UI can say why.</summary>
    public sealed record NotATable(ExportUnavailableReason Reason) : OriginShape;
}

/// <summary>Signals A and B together, as <b>facts supplied by the data source</b> — never a verdict. The
/// verdict is <see cref="ResultOriginResolver"/>'s, and it needs signal C (the catalog) too.</summary>
/// <param name="Columns">Per result column, in the result's own column order.</param>
/// <param name="Shape">Where the rows came from.</param>
public sealed record ResultOrigin(IReadOnlyList<ColumnOrigin> Columns, OriginShape Shape)
{
    /// <summary>A result with no provenance at all — the honest answer for a source that cannot supply
    /// it, and one the resolver always refuses.</summary>
    public static ResultOrigin None(ExportUnavailableReason reason)
        => new(Array.Empty<ColumnOrigin>(), new OriginShape.NotATable(reason));
}
