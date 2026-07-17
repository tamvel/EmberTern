using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Export.Sql;

/// <summary>
/// Why EmberTern will not generate DML for a result. <b>Every one of these is a sentence the user should
/// read</b> — the design's rule is a disabled menu item <em>that says why</em>, naming the actual
/// obstacle, because that teaches the tool's model instead of leaving the user to guess. It is also
/// strictly more information than the placeholder-INSERT alternative conveys.
/// <para>
/// <b>These codes are three different kinds of claim, and the message must not blur them</b> — each
/// implies a different next action for the user, which is the whole reason for saying why:
/// <list type="bullet">
/// <item><b>Inherent to the query</b> (<see cref="SetOperation"/>, <see cref="MultipleSourceTables"/>,
/// <see cref="Join"/>, <see cref="Aggregate"/>, <see cref="NoSourceTable"/>,
/// <see cref="DuplicateSourceColumn"/>, <see cref="IncompletePrimaryKey"/>, <see cref="NoWritableColumns"/>,
/// <see cref="KeyValueIsNull"/>, and <see cref="NotATable"/> for a procedure) — the <em>result</em> genuinely
/// is not one table's rows, or genuinely cannot identify one. No future version changes this. → the user
/// rewrites the query (or selects the key columns).</item>
/// <item><b>A current EmberTern limitation</b> (<see cref="CommonTableExpression"/>,
/// <see cref="StatementNotUnderstood"/>, and <see cref="NotATable"/> for a view while updatable-view
/// analysis is not done) — the query is fine; EmberTern's analysis is not deep enough yet. → the user
/// waits, or works around it. <b>Never word these as a property of SQL.</b></item>
/// <item><b>Transient</b> (<see cref="CatalogNotLoaded"/>, <see cref="UnknownSourceColumn"/>) — nothing is
/// wrong with anything; the metadata is cold or stale. → retry, or commit the DDL.</item>
/// </list>
/// Two that do not sit neatly in the three, and are worth knowing rather than forcing:
/// <list type="bullet">
/// <item><see cref="NoPrimaryKey"/> — inherent, but a fact about the <em>schema</em> rather than the
/// query: the table really has no primary key, and unlike <see cref="IncompletePrimaryKey"/> no rewrite
/// helps. Word the two differently — one invites an action, the other does not.</item>
/// <item><see cref="ValueNotRenderable"/> — depends on the <em>data</em>, and is both kinds at once: an
/// unmapped declared type is an EmberTern limitation (we could map DECFLOAT one day), while a subnormal
/// double genuinely has no literal (the engine silently zeroes it). It is also <b>row-dependent</b> —
/// the same column holding NULL copies fine — which no other reason here is.</item>
/// </list>
/// <see cref="ValueTooLarge"/> and <see cref="StatementTooLong"/> are inherent: no literal form exists,
/// and no future version invents one.
/// </para>
/// </summary>
public enum ExportUnavailableCode
{
    /// <summary>The parser could not confidently model the statement, or it is not a single SELECT.
    /// Uncertainty ⇒ do nothing (architecture rule #11).</summary>
    StatementNotUnderstood,

    /// <summary>A UNION / INTERSECT / EXCEPT. <b>The server reports only the first leg's provenance</b>,
    /// so a generated statement would target a real but wrong table. Only the AST can see this.</summary>
    SetOperation,

    /// <summary>The result combines more than one table, so no single INSERT/UPDATE can be right — and
    /// the column list would be wrong too, not just the table name. Names = the tables involved.</summary>
    MultipleSourceTables,

    /// <summary>The result is a join. One base table name can hide two row instances (a self-join), so
    /// even a single-name join is unresolvable. Names = the tables involved.</summary>
    Join,

    /// <summary>The row is an aggregate (GROUP BY), not a table row.</summary>
    Aggregate,

    /// <summary>
    /// A WITH-led query — <b>a current limitation of EmberTern's provenance analysis, not a statement
    /// that CTEs are unsupported.</b> Tracing which table a CTE reference ultimately reads needs semantic
    /// name resolution the shape reader deliberately does not do yet; the query itself is perfectly
    /// ordinary, and a CTE over a single table is exactly as resolvable in principle as the same query
    /// written inline.
    /// <para>
    /// <b>Wording rule for the App message (E5): say what EmberTern cannot do, not what SQL cannot be.</b>
    /// "EmberTern cannot yet trace which table this CTE reads" — never "CTEs are not supported". The
    /// distinction matters: the other refusals here describe a property of the <em>result</em> (a UNION
    /// really cannot be attributed to one table; a join really does hide two row instances), whereas this
    /// one describes a property of <em>EmberTern</em>, and the user should be able to tell which they are
    /// looking at. Revisit once name resolution is richer.
    /// </para>
    /// </summary>
    CommonTableExpression,

    /// <summary>No column in the result comes from a base table — every one is a literal, an aggregate,
    /// or another derived expression.</summary>
    NoSourceTable,

    /// <summary>Two result columns map to the same base column (<c>select ID, ID as AGAIN from T</c>),
    /// which would emit an invalid duplicate column list. Names = the duplicated column.</summary>
    DuplicateSourceColumn,

    /// <summary>The catalog does not know the base object at all. Names = the object.</summary>
    UnknownObject,

    /// <summary>The base object is not a table — a procedure, or a view. <see cref="SymbolKind"/> carries
    /// which, so the message can name it. Names = the object.</summary>
    NotATable,

    /// <summary>The object is a known table but its columns are not loaded yet, so nothing can be
    /// <em>verified</em>. Distinct from every other reason on purpose: it is transient, and the caller's
    /// correct response is to warm the metadata and ask again — never to report "no primary key".</summary>
    CatalogNotLoaded,

    /// <summary>A base column in the result is not in the catalog's column list — provenance and catalog
    /// disagree, which usually means the cached metadata is stale (e.g. a column added by DDL that is not
    /// committed yet, and so is invisible to the metadata attachment). Names = the column.</summary>
    UnknownSourceColumn,

    /// <summary>The table has no primary key at all, so a single row cannot be identified.</summary>
    NoPrimaryKey,

    /// <summary>The table's primary key is only <em>partly</em> in the result. This is the single most
    /// important refusal in the milestone: the driver reports <c>IsKey=True</c> for the projected half of
    /// a composite key, and a WHERE built from it silently updates every row that shares it. Names = the
    /// missing key columns.</summary>
    IncompletePrimaryKey,

    /// <summary>Nothing in the result can be written — every projected column is computed or derived.</summary>
    NoWritableColumns,

    /// <summary>A key column's value is NULL in this row, so it cannot identify it. Names = the column.</summary>
    KeyValueIsNull,

    /// <summary>A value in this row has no faithful Firebird literal — an unmapped declared type
    /// (ARRAY, DECFLOAT, INT128, <c>WITH TIME ZONE</c>), or a value no literal of its type can carry
    /// (a subnormal double, which the engine silently turns into 0). Names = the column. Refused rather
    /// than approximated: an approximate literal is silent corruption. Row-dependent — a NULL in the
    /// same column is perfectly copyable.</summary>
    ValueNotRenderable,

    /// <summary>A value is too large for any single literal (a blob beyond ~32 KB). Names = the column.
    /// Truncating would be silent corruption, so the statement is not offered at all.</summary>
    ValueTooLarge,

    /// <summary>The assembled statement exceeds Firebird's ~65,535-character DSQL limit — several large
    /// values that are each individually fine. Names = the table. This is why a per-value ceiling is
    /// necessary but not sufficient; only the assembled length can see it.</summary>
    StatementTooLong,
}

/// <summary>
/// A structured refusal — a code plus the data a message needs. <b>Never a message</b>: Core has no UI
/// strings (rule #1) and there is no <c>.resx</c> (rule #6), so App maps this onto <c>UiStrings</c>.
/// </summary>
/// <param name="Code">Which obstacle.</param>
public sealed record ExportUnavailableReason(ExportUnavailableCode Code)
{
    /// <summary>The objects or columns this reason is about — the tables a result combines, the missing
    /// key columns, the duplicated column, the object that is not a table. Empty when the code alone
    /// says everything.</summary>
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();

    /// <summary>What the base object actually is, for <see cref="ExportUnavailableCode.NotATable"/> —
    /// so the message can say "is a procedure" or "is a view" rather than "is not a table".</summary>
    public SymbolKind? ObjectKind { get; init; }

    public static ExportUnavailableReason Of(ExportUnavailableCode code) => new(code);

    public static ExportUnavailableReason Of(ExportUnavailableCode code, params string[] names)
        => new(code) { Names = names };
}
