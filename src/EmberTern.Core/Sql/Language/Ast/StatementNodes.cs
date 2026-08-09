using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

// The concrete top-level statement node types (Etap 2, "statement skeleton" depth). Every §5.4
// statement kind is its own node type from the start; each keeps its interior verbatim in
// SqlStatement.Tokens and exposes only the structured facts a current consumer needs. Later etaps
// deepen individual nodes (clauses, expressions, PSQL bodies) without changing this taxonomy.

/// <summary><c>SELECT …</c> or a CTE-led <c>WITH … SELECT …</c> query.</summary>
public sealed class SelectStatement : SqlStatement, IExecutableStatement
{
    public SelectStatement(int start, int length, IReadOnlyList<SqlToken> tokens, QueryNode? query = null)
        : base(start, length, tokens) => Query = query;

    public override StatementKind Kind => StatementKind.Select;

    /// <summary>The parsed query tree (Etap 6.9 / B2 + B3) — the single structural representation of the
    /// statement's query: a <see cref="SelectQuery"/> / <see cref="SetOperationQuery"/> for a plain query,
    /// or a <see cref="WithQuery"/> (its CTE bodies + main query all real <see cref="QueryNode"/>s) for a
    /// <c>WITH</c>-led query. <c>null</c> only when the shape was not a recognisable query (§0: treated as
    /// a plain query, tokens untouched). An additive overlay; <see cref="SqlStatement.Tokens"/> still
    /// round-trips the source.</summary>
    public QueryNode? Query { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children =>
        Query is not null ? new SqlNode[] { Query } : base.Children;
}

/// <summary><c>INSERT …</c> (INTO … VALUES / SELECT / DEFAULT VALUES).</summary>
public sealed class InsertStatement : SqlStatement, IExecutableStatement, IColumnValueTarget
{
    private readonly SqlNode[] _children;

    public InsertStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        QueryNode? sourceQuery = null,
        IReadOnlyList<SqlNode>? subqueries = null,
        string? targetTable = null,
        IReadOnlyList<ColumnValue>? columnValues = null)
        : base(start, length, tokens)
    {
        SourceQuery = sourceQuery;
        Subqueries = subqueries ?? Array.Empty<SqlNode>();
        TargetTable = targetTable;
        ColumnValues = columnValues ?? Array.Empty<ColumnValue>();
        _children = AstChildren.Of(sourceQuery, subqueries);
    }

    /// <inheritdoc />
    public string? TargetTable { get; }

    /// <summary><inheritdoc cref="IColumnValueTarget.ColumnValues" path="/summary"/>
    /// <para>Paired POSITIONALLY from <c>(cols)</c> against <c>VALUES (…)</c>. Empty for
    /// <c>INSERT … SELECT</c> (the values are a query's columns, not spans) and for an insert written without a
    /// column list.</para></summary>
    public IReadOnlyList<ColumnValue> ColumnValues { get; }

    /// <summary>The source query of an <c>INSERT … SELECT / WITH … SELECT</c> (Etap 6.9 / B3.1) — a real
    /// <see cref="QueryNode"/>. Null for an <c>INSERT … VALUES</c> / <c>… DEFAULT VALUES</c>. An additive
    /// overlay; <see cref="SqlStatement.Tokens"/> still round-trips (§0).</summary>
    public QueryNode? SourceQuery { get; }

    /// <summary>Structurally-meaningful subquery expressions embedded in the statement OUTSIDE the source
    /// query (a scalar subquery in a <c>VALUES</c> / <c>RETURNING</c> expression) — each an
    /// <see cref="ExistsExpression"/> / <see cref="ScalarSubquery"/> owning a real
    /// <see cref="QueryNode"/> (B3.1). Empty when there are none.</summary>
    public IReadOnlyList<SqlNode> Subqueries { get; }

    public override StatementKind Kind => StatementKind.Insert;

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary><c>UPDATE … SET … [WHERE …]</c>.</summary>
public sealed class UpdateStatement : SqlStatement, IExecutableStatement, IColumnValueTarget
{
    private readonly SqlNode[] _children;

    public UpdateStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        IReadOnlyList<SqlNode>? subqueries = null,
        string? targetTable = null,
        IReadOnlyList<ColumnValue>? columnValues = null)
        : base(start, length, tokens)
    {
        Subqueries = subqueries ?? Array.Empty<SqlNode>();
        TargetTable = targetTable;
        ColumnValues = columnValues ?? Array.Empty<ColumnValue>();
        _children = AstChildren.Of(null, subqueries);
    }

    /// <inheritdoc />
    public string? TargetTable { get; }

    /// <summary><inheritdoc cref="IColumnValueTarget.ColumnValues" path="/summary"/>
    /// <para>The <c>SET col = &lt;expr&gt;</c> assignments — paired by adjacency rather than by position, which is
    /// why the interface carries pairs. ⚠ The <c>WHERE</c> predicate is NOT included: at the structural-depth
    /// boundary a predicate is a token fragment, so <c>WHERE col = :p</c> is not a modelled pairing and its
    /// placeholder stays untyped rather than guessed.</para></summary>
    public IReadOnlyList<ColumnValue> ColumnValues { get; }

    /// <summary>Subquery expressions embedded in the <c>SET</c> / <c>WHERE</c> / <c>RETURNING</c>
    /// expressions (Etap 6.9 / B3.1) — each owning a real <see cref="QueryNode"/>. Empty when none.</summary>
    public IReadOnlyList<SqlNode> Subqueries { get; }

    public override StatementKind Kind => StatementKind.Update;

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary><c>UPDATE OR INSERT INTO … VALUES …</c> (Firebird's upsert).</summary>
public sealed class UpdateOrInsertStatement : SqlStatement, IExecutableStatement, IColumnValueTarget
{
    private readonly SqlNode[] _children;

    public UpdateOrInsertStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        IReadOnlyList<SqlNode>? subqueries = null,
        string? targetTable = null,
        IReadOnlyList<ColumnValue>? columnValues = null)
        : base(start, length, tokens)
    {
        Subqueries = subqueries ?? Array.Empty<SqlNode>();
        TargetTable = targetTable;
        ColumnValues = columnValues ?? Array.Empty<ColumnValue>();
        _children = AstChildren.Of(null, subqueries);
    }

    /// <inheritdoc />
    public string? TargetTable { get; }

    /// <summary><inheritdoc cref="IColumnValueTarget.ColumnValues" path="/summary"/>
    /// <para>Paired POSITIONALLY from <c>(cols)</c> against <c>VALUES (…)</c> — the same shape as
    /// <see cref="InsertStatement"/>, which is why one producer serves both. The <c>MATCHING (…)</c> list names
    /// columns but supplies no values, so it contributes no pairs.</para></summary>
    public IReadOnlyList<ColumnValue> ColumnValues { get; }

    /// <summary>Subquery expressions embedded in the <c>VALUES</c> / <c>RETURNING</c> expressions
    /// (Etap 6.9 / B3.1) — each owning a real <see cref="QueryNode"/>. Empty when none.</summary>
    public IReadOnlyList<SqlNode> Subqueries { get; }

    public override StatementKind Kind => StatementKind.UpdateOrInsert;

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary><c>DELETE FROM … [WHERE …]</c>.</summary>
public sealed class DeleteStatement : SqlStatement, IExecutableStatement
{
    private readonly SqlNode[] _children;

    public DeleteStatement(
        int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<SqlNode>? subqueries = null)
        : base(start, length, tokens)
    {
        Subqueries = subqueries ?? Array.Empty<SqlNode>();
        _children = AstChildren.Of(null, subqueries);
    }

    /// <summary>Subquery expressions embedded in the <c>WHERE</c> / <c>RETURNING</c> expressions
    /// (Etap 6.9 / B3.1) — each owning a real <see cref="QueryNode"/>. Empty when none.</summary>
    public IReadOnlyList<SqlNode> Subqueries { get; }

    public override StatementKind Kind => StatementKind.Delete;

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary><c>MERGE INTO … USING … ON … WHEN …</c>.</summary>
public sealed class MergeStatement : SqlStatement, IExecutableStatement
{
    private readonly SqlNode[] _children;

    public MergeStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        QueryNode? sourceQuery = null,
        IReadOnlyList<SqlNode>? subqueries = null,
        FromItem? sourceItem = null)
        : base(start, length, tokens)
    {
        SourceQuery = sourceQuery;
        SourceItem = sourceItem;
        Subqueries = subqueries ?? Array.Empty<SqlNode>();
        // The two source forms are mutually exclusive by construction (parenthesised query vs bare reference),
        // and the source precedes the ON/WHEN subqueries in the text — so one head child, in source order.
        _children = AstChildren.Of((SqlNode?)sourceItem ?? sourceQuery, subqueries);
    }

    /// <summary>The <c>USING ( &lt;query&gt; )</c> source query (Etap 6.9 / B3.1) — a real
    /// <see cref="QueryNode"/>. Null when the source is a bare table/view/routine reference, which is
    /// <see cref="SourceItem"/> instead.</summary>
    public QueryNode? SourceQuery { get; }

    /// <summary>
    /// The <c>USING &lt;name&gt;[(args)] [[AS] alias]</c> source when it is NOT parenthesised — a
    /// <see cref="TableReference"/>, or a <see cref="RoutineTableReference"/> when it is a selectable-procedure
    /// call. Null when the source is a parenthesised query (<see cref="SourceQuery"/>) or unreadable.
    ///
    /// <para>⭐ Added 2026-08-03 with <see cref="IRoutineInvocation"/>: this source was previously not modelled at
    /// all (the parser noted "bare table source" and moved on), so <c>MERGE … USING MY_PROC(:a) s</c> was the one
    /// remaining place a routine could be invoked without the tree knowing. Parsed by the same
    /// <c>ParsePrimaryFromItem</c> a <c>FROM</c> entry uses, so it is the same node kinds and no second notion of
    /// "a source" exists.</para>
    /// </summary>
    public FromItem? SourceItem { get; }

    /// <summary>Subquery expressions embedded OUTSIDE the source — in the <c>ON</c> / <c>WHEN</c>
    /// conditions and the <c>UPDATE SET</c> / <c>INSERT VALUES</c> expressions (B3.1) — each owning a real
    /// <see cref="QueryNode"/>. Empty when none.</summary>
    public IReadOnlyList<SqlNode> Subqueries { get; }

    public override StatementKind Kind => StatementKind.Merge;

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary><c>EXECUTE BLOCK … AS BEGIN … END</c> — an anonymous PSQL block executed as a query.</summary>
public sealed class ExecuteBlockStatement : SqlStatement
{
    public ExecuteBlockStatement(
        int start, int length, IReadOnlyList<SqlToken> tokens, BlockStatement? body = null)
        : base(start, length, tokens) => Body = body;

    /// <summary>The parsed PSQL body tree (Etap 6.9 / B1) — the <c>BEGIN … END</c> after the block's
    /// <c>AS</c>, with its declarations and control flow. An additive overlay; the token slice still
    /// round-trips (§0). Null when there was no recognisable body.</summary>
    public BlockStatement? Body { get; }

    public override StatementKind Kind => StatementKind.ExecuteBlock;

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children =>
        Body is null ? base.Children : new SqlNode[] { Body };
}

/// <summary><c>EXECUTE PROCEDURE name [(args)] [RETURNING_VALUES …]</c>.</summary>
public sealed class ExecuteProcedureStatement : SqlStatement, IExecutableStatement, IRoutineInvocation
{
    /// <summary><see cref="IRoutineInvocation.RoutineName"/> — the same value as
    /// <see cref="ProcedureName"/>, under the name every call site uses regardless of syntax.</summary>
    string? IRoutineInvocation.RoutineName => ProcedureName;

    public ExecuteProcedureStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        string? procedureName,
        IReadOnlyList<CallArgument>? arguments = null,
        IReadOnlyList<string>? returningTargets = null,
        string? packageName = null)
        : base(start, length, tokens)
    {
        ProcedureName = procedureName;
        PackageName = packageName;
        Arguments = arguments ?? Array.Empty<CallArgument>();
        ReturningTargets = returningTargets ?? Array.Empty<string>();
    }

    /// <summary>The invoked <b>routine's</b> name (the part after the dot for a package-qualified call) —
    /// an unquoted name is upper-cased to match the catalog, a quoted name keeps its case — or null when it
    /// could not be read. For <c>EXECUTE PROCEDURE PKG.PROC</c> this is <c>PROC</c> and <see cref="PackageName"/>
    /// is <c>PKG</c>.</summary>
    public string? ProcedureName { get; }

    /// <summary>The package qualifier of a <c>EXECUTE PROCEDURE PKG.PROC</c> call (folded like
    /// <see cref="ProcedureName"/>), or <c>null</c> for an unqualified call (Stage X / D11). Additive overlay;
    /// the full slice still round-trips (§0). The debugger resolves a package routine from these two parts;
    /// an unqualified sibling call inside a package frame carries a null package and is resolved against the
    /// frame's package (Seam B).</summary>
    public string? PackageName { get; }

    /// <summary>The call's positional arguments, in order (Stage X / D8) — each the source span of one
    /// argument expression, which a debugger step-into evaluates verbatim in the <b>caller's</b> frame to
    /// seed the callee's input parameters. Empty for a no-argument call. The arguments' ordinary expression
    /// interior stays in <see cref="SqlStatement.Tokens"/> (structural-depth boundary; this carries only the
    /// spans the debugger slices); additive overlay — the tokens still round-trip (§0).</summary>
    public IReadOnlyList<CallArgument> Arguments { get; }

    /// <summary>The <c>RETURNING_VALUES</c> target variable names, in order, folded to the resolution
    /// convention (unquoted upper-cased; the leading <c>:</c>/<c>@</c> of a parameter stripped) so they key
    /// straight into a frame's values (Stage X / D8) — the caller variables the callee's output parameters
    /// are written into on return. Empty when the call has no <c>RETURNING_VALUES</c>. Additive overlay;
    /// <see cref="SqlStatement.Tokens"/> still round-trips (§0).</summary>
    public IReadOnlyList<string> ReturningTargets { get; }

    public override StatementKind Kind => StatementKind.ExecuteProcedure;
}

/// <summary>One positional argument of a routine invocation — the source span of the argument expression
/// (Stage X / D8). A debugger step-into slices this span and evaluates it in the caller frame; the ordinary
/// expression content is not modelled deeper (structural-depth boundary). Not a tree child (it carries only a
/// span, like <see cref="ForSelectStatement.IntoTargets"/>).</summary>
public sealed record CallArgument(int Start, int Length);

/// <summary>
/// A node that <b>invokes a routine with a positional argument list</b> — the ONE question the model answers
/// about a call, whatever syntax made it.
///
/// <para>⭐⭐ <b>Why this exists, and why it is an interface on the AST rather than a helper somewhere else.</b>
/// Firebird has several unrelated syntaxes for "call this procedure": <c>EXECUTE PROCEDURE P(a, b)</c>, and a
/// SELECTABLE procedure standing where a table would (<c>select … from P(a, b)</c>) — which then appears inside
/// a plain <c>SELECT</c>, a PSQL <c>FOR SELECT … INTO</c>, an <c>INSERT … SELECT</c>, a CTE body, a
/// <c>MERGE … USING</c>, a cursor declaration, or any subquery in any of those. Consumers that ask
/// <i>"which statement kind is this?"</i> therefore have to enumerate syntaxes, and each new one is a silent
/// gap: the Smart-Parameters dialog typed <c>EXECUTE PROCEDURE</c> only, then <c>SELECT … FROM P(…)</c> only,
/// and each fix left the next syntax broken (user report 2026-08-03, three rounds).</para>
///
/// <para>⭐ With the invocation modelled, every consumer asks the TREE instead:
/// <c>root.DescendantNodes().OfType&lt;IRoutineInvocation&gt;()</c>. Nesting and statement kind stop being its
/// business — the parser already hangs each embedded query off the statement that owns it, so every syntax above
/// is reached by the same walk, including ones added later.</para>
///
/// <para>⚠ Implemented only by nodes that ARE invocations, never by one that merely might be: a plain table in a
/// <c>FROM</c> clause is a <see cref="TableReference"/>, a selectable-procedure call is a
/// <see cref="RoutineTableReference"/>. The type carries the distinction, so no consumer has to filter on
/// "arguments is empty" — which would conflate a no-argument call with a table.</para>
/// </summary>
/// <summary>One value written into a named column — the column's name and the source span of the expression
/// supplying it. Not a tree child (a span, like <see cref="CallArgument"/>).</summary>
public sealed record ColumnValue(string ColumnName, int Start, int Length);

/// <summary>
/// A node that <b>writes values into named columns of a table</b> — the second thing the model can prove about
/// where a value's TYPE comes from.
///
/// <para>⭐⭐ <b>Why this sits beside <see cref="IRoutineInvocation"/> rather than inside it.</b> A placeholder's
/// declared type has exactly two provable origins in Firebird DML: it is an argument of a routine (type = that
/// input parameter's), or it is a value written into a column (type = that column's). Those are different
/// metadata, so they are different facts — but a consumer must not care which statement syntax produced either,
/// which is why both are interfaces walked off the tree. ⛔ The rule the user set: no series of per-statement
/// branches. If the AST can prove the pairing, the type is available; if it cannot, the value stays untyped.</para>
///
/// <para>⚠ Modelled as (column, value-span) PAIRS, not as two parallel lists, and that is what lets one interface
/// serve shapes that pair differently: <c>INSERT … (cols) VALUES (…)</c> and <c>UPDATE OR INSERT</c> pair
/// POSITIONALLY, while <c>UPDATE … SET col = expr</c> pairs by writing them adjacently. The producer resolves the
/// pairing; the consumer never learns there was more than one way to do it.</para>
///
/// <para>⚠ A pairing that cannot be established is simply absent — an <c>INSERT</c> with no column list
/// (<c>INSERT INTO T VALUES (…)</c>) yields nothing, because matching values to columns would then require the
/// table's catalog order, which is a lookup, not a fact about the text (rule #11: never guess).</para>
/// </summary>
public interface IColumnValueTarget
{
    /// <summary>The table being written to, folded to the catalog convention, or null when unreadable.</summary>
    string? TargetTable { get; }

    /// <summary>The (column, value-span) pairs this statement establishes, in source order.</summary>
    IReadOnlyList<ColumnValue> ColumnValues { get; }
}

public interface IRoutineInvocation
{
    /// <summary>The invoked routine's name, folded to the catalog convention (an unquoted name upper-cased, a
    /// quoted one kept verbatim), or null when it could not be read.</summary>
    string? RoutineName { get; }

    /// <summary>The package qualifier for <c>PKG.PROC</c>, or null for an unqualified call.</summary>
    string? PackageName { get; }

    /// <summary>The positional arguments, in source order. Empty for a call written with no argument list.</summary>
    IReadOnlyList<CallArgument> Arguments { get; }
}

/// <summary><c>EXECUTE STATEMENT …</c> (or a bare <c>EXECUTE …</c> that is neither BLOCK nor PROCEDURE).</summary>
public sealed class ExecuteStatementStatement : SqlStatement, IExecutableStatement
{
    public ExecuteStatementStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.ExecuteStatement;
}

/// <summary>The verb of a <see cref="DdlStatement"/>.</summary>
public enum DdlVerb
{
    Create,
    CreateOrAlter,
    Alter,
    Recreate,
    Drop,
}

/// <summary>The kind of schema object a <see cref="DdlStatement"/> targets. <see cref="Unknown"/>
/// covers anything the header scan did not recognise (the interior stays verbatim regardless).</summary>
public enum DdlObjectKind
{
    Unknown,
    Table,
    View,
    Index,
    Sequence,
    Generator,
    Procedure,
    Function,
    Trigger,
    Domain,
    Exception,
    Role,
    Package,
    Collation,
    Filter,
    ExternalFunction,
}

/// <summary>
/// A DDL statement — <c>CREATE</c> / <c>CREATE OR ALTER</c> / <c>ALTER</c> / <c>RECREATE</c> /
/// <c>DROP</c> of a schema object. The header facts (<see cref="Verb"/>, <see cref="ObjectKind"/>,
/// <see cref="ObjectName"/>) are read best-effort; the full definition (and any PSQL body) stays
/// verbatim in <see cref="SqlStatement.Tokens"/> until later etaps deepen it.
/// </summary>
public sealed class DdlStatement : SqlStatement
{
    public DdlStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        DdlVerb verb,
        DdlObjectKind objectKind,
        string? objectName,
        bool isPsqlDefinition,
        BlockStatement? body = null,
        QueryNode? query = null)
        : base(start, length, tokens)
    {
        Verb = verb;
        ObjectKind = objectKind;
        ObjectName = objectName;
        IsPsqlDefinition = isPsqlDefinition;
        Body = body;
        Query = query;
    }

    /// <summary>The parsed PSQL body tree (Etap 6.9 / B1) for a <c>CREATE/ALTER/RECREATE</c> of a
    /// <c>PROCEDURE/FUNCTION/TRIGGER</c> — the <c>BEGIN … END</c> after the header's <c>AS</c>, with its
    /// declarations and control flow. Null for non-PSQL DDL (and for a <c>PACKAGE</c>, whose body is a
    /// list of subprograms — a later milestone). An additive overlay; <see cref="SqlStatement.Tokens"/>
    /// still round-trips the source (§0).</summary>
    public BlockStatement? Body { get; }

    /// <summary>The view body query of a <c>CREATE/CREATE OR ALTER/ALTER/RECREATE VIEW … AS &lt;query&gt;</c>
    /// (Etap 6.9 / B3.1) — a real <see cref="QueryNode"/>. Null for every non-VIEW DDL (and mutually
    /// exclusive with <see cref="Body"/>: a view has no PSQL body, a routine has no view query). An
    /// additive overlay; <see cref="SqlStatement.Tokens"/> still round-trips (§0).</summary>
    public QueryNode? Query { get; }

    /// <summary>The DDL verb.</summary>
    public DdlVerb Verb { get; }

    /// <summary>The targeted object kind (best-effort; <see cref="DdlObjectKind.Unknown"/> when unrecognised).</summary>
    public DdlObjectKind ObjectKind { get; }

    /// <summary>The targeted object name (best-effort) — an unquoted name upper-cased, a quoted
    /// name in its literal case — or null when it could not be read.</summary>
    public string? ObjectName { get; }

    /// <summary>True for a PSQL definition whose body carries its own semicolons — a
    /// <c>CREATE/ALTER/RECREATE</c> of a <c>PROCEDURE / TRIGGER / FUNCTION / PACKAGE</c>. Matches
    /// the statement-segmentation rule that keeps such a definition whole.</summary>
    public bool IsPsqlDefinition { get; }

    public override StatementKind Kind => StatementKind.Ddl;

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children =>
        Body is not null ? new SqlNode[] { Body }
        : Query is not null ? new SqlNode[] { Query }
        : base.Children;
}

/// <summary><c>COMMENT ON … IS …</c>.</summary>
public sealed class CommentStatement : SqlStatement
{
    public CommentStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Comment;
}

/// <summary><c>SET …</c> — a session/structural directive (GENERATOR, STATISTICS, TERM, TRANSACTION, …).</summary>
public sealed class SetStatement : SqlStatement
{
    public SetStatement(int start, int length, IReadOnlyList<SqlToken> tokens, string? target)
        : base(start, length, tokens)
    {
        Target = target;
    }

    /// <summary>The word after <c>SET</c> (e.g. <c>GENERATOR</c>, <c>STATISTICS</c>, <c>TERM</c>,
    /// <c>TRANSACTION</c>) in its source case, or null when absent.</summary>
    public string? Target { get; }

    public override StatementKind Kind => StatementKind.Set;
}

/// <summary><c>GRANT …</c>.</summary>
public sealed class GrantStatement : SqlStatement
{
    public GrantStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Grant;
}

/// <summary><c>REVOKE …</c>.</summary>
public sealed class RevokeStatement : SqlStatement
{
    public RevokeStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Revoke;
}

/// <summary>A top-level <c>DECLARE …</c> — a <c>DECLARE EXTERNAL FUNCTION</c> / <c>DECLARE FILTER</c>
/// declaration (a <c>DECLARE</c> inside a PSQL body is part of an enclosing definition, not a
/// top-level statement).</summary>
public sealed class DeclareStatement : SqlStatement
{
    public DeclareStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Declare;
}

/// <summary>
/// A top-level anonymous PSQL block — a bare <c>BEGIN … END</c>, optionally preceded by a DECLARE
/// section and/or local <c>DECLARE PROCEDURE/FUNCTION</c> subprograms — that is not the body of a
/// <c>CREATE/ALTER/RECREATE</c> definition or an <c>EXECUTE BLOCK</c>. This is the exact shape the
/// procedure/function/trigger <b>body editor</b> holds (gotcha #114: the stored body is the
/// <c>DECLARE … BEGIN … END</c> without the CREATE header), so the formatter must lay it out as a
/// PSQL body rather than fall back to a verbatim <see cref="RawStatement"/>. Added in Etap 3 (the
/// one small, formatter-driven AST refinement — statement boundaries are unchanged, so the DDL
/// splitter is unaffected).
/// </summary>
public sealed class AnonymousBlockStatement : SqlStatement
{
    public AnonymousBlockStatement(
        int start, int length, IReadOnlyList<SqlToken> tokens, BlockStatement? body = null)
        : base(start, length, tokens) => Body = body;

    /// <summary>The parsed PSQL body tree (Etap 6.9 / B1) — a <see cref="BlockStatement"/> modelling
    /// the <c>BEGIN … END</c> structure, control flow and executable-leaf spans. An additive structural
    /// overlay: <see cref="SqlStatement.Tokens"/> still reproduces the source byte-for-byte. Null only
    /// if the body parser was not run (defensive; the parser always supplies it).</summary>
    public BlockStatement? Body { get; }

    public override StatementKind Kind => StatementKind.AnonymousBlock;

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children =>
        Body is null ? base.Children : new SqlNode[] { Body };
}

/// <summary>An empty statement — a lone terminator <c>;</c> (or a run of them). Carries no content.</summary>
public sealed class EmptyStatement : SqlStatement
{
    public EmptyStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Empty;
}

/// <summary>
/// An unrecognised statement, preserved verbatim in <see cref="SqlStatement.Tokens"/> — the §0
/// (Paramount Law) safety valve. EmberTern never loses or reshapes SQL it cannot classify; the
/// formatter and every other feature treat a raw statement as opaque and re-emit it unchanged.
/// </summary>
public sealed class RawStatement : SqlStatement
{
    public RawStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Raw;
}
