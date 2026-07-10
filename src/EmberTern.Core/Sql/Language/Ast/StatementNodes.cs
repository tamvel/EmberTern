using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

// The concrete top-level statement node types (Etap 2, "statement skeleton" depth). Every §5.4
// statement kind is its own node type from the start; each keeps its interior verbatim in
// SqlStatement.Tokens and exposes only the structured facts a current consumer needs. Later etaps
// deepen individual nodes (clauses, expressions, PSQL bodies) without changing this taxonomy.

/// <summary><c>SELECT …</c> or a CTE-led <c>WITH … SELECT …</c> query.</summary>
public sealed class SelectStatement : SqlStatement
{
    public SelectStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Select;
}

/// <summary><c>INSERT …</c> (INTO … VALUES / SELECT / DEFAULT VALUES).</summary>
public sealed class InsertStatement : SqlStatement
{
    public InsertStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Insert;
}

/// <summary><c>UPDATE … SET … [WHERE …]</c>.</summary>
public sealed class UpdateStatement : SqlStatement
{
    public UpdateStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Update;
}

/// <summary><c>UPDATE OR INSERT INTO … VALUES …</c> (Firebird's upsert).</summary>
public sealed class UpdateOrInsertStatement : SqlStatement
{
    public UpdateOrInsertStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.UpdateOrInsert;
}

/// <summary><c>DELETE FROM … [WHERE …]</c>.</summary>
public sealed class DeleteStatement : SqlStatement
{
    public DeleteStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Delete;
}

/// <summary><c>MERGE INTO … USING … ON … WHEN …</c>.</summary>
public sealed class MergeStatement : SqlStatement
{
    public MergeStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.Merge;
}

/// <summary><c>EXECUTE BLOCK … AS BEGIN … END</c> — an anonymous PSQL block executed as a query.</summary>
public sealed class ExecuteBlockStatement : SqlStatement
{
    public ExecuteBlockStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.ExecuteBlock;
}

/// <summary><c>EXECUTE PROCEDURE name [(args)] [RETURNING_VALUES …]</c>.</summary>
public sealed class ExecuteProcedureStatement : SqlStatement
{
    public ExecuteProcedureStatement(int start, int length, IReadOnlyList<SqlToken> tokens, string? procedureName)
        : base(start, length, tokens)
    {
        ProcedureName = procedureName;
    }

    /// <summary>The invoked procedure's name — an unquoted name is upper-cased to match the
    /// catalog, a quoted name keeps its case — or null when it could not be read.</summary>
    public string? ProcedureName { get; }

    public override StatementKind Kind => StatementKind.ExecuteProcedure;
}

/// <summary><c>EXECUTE STATEMENT …</c> (or a bare <c>EXECUTE …</c> that is neither BLOCK nor PROCEDURE).</summary>
public sealed class ExecuteStatementStatement : SqlStatement
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
        bool isPsqlDefinition)
        : base(start, length, tokens)
    {
        Verb = verb;
        ObjectKind = objectKind;
        ObjectName = objectName;
        IsPsqlDefinition = isPsqlDefinition;
    }

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
    public AnonymousBlockStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length, tokens) { }

    public override StatementKind Kind => StatementKind.AnonymousBlock;
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
