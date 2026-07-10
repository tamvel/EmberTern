using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

/// <summary>
/// The high-level kind of a top-level <see cref="SqlStatement"/>. Mirrors the concrete statement
/// node types so callers and tests can switch on a single discriminator without a type test.
/// </summary>
public enum StatementKind
{
    /// <summary><c>SELECT …</c> / <c>WITH … SELECT …</c>.</summary>
    Select,

    /// <summary><c>INSERT …</c>.</summary>
    Insert,

    /// <summary><c>UPDATE …</c> (not <c>UPDATE OR INSERT</c>).</summary>
    Update,

    /// <summary><c>UPDATE OR INSERT INTO …</c>.</summary>
    UpdateOrInsert,

    /// <summary><c>DELETE …</c>.</summary>
    Delete,

    /// <summary><c>MERGE …</c>.</summary>
    Merge,

    /// <summary><c>EXECUTE BLOCK …</c>.</summary>
    ExecuteBlock,

    /// <summary><c>EXECUTE PROCEDURE …</c>.</summary>
    ExecuteProcedure,

    /// <summary><c>EXECUTE STATEMENT …</c> (or a bare <c>EXECUTE …</c>).</summary>
    ExecuteStatement,

    /// <summary>A DDL statement — <c>CREATE</c> / <c>CREATE OR ALTER</c> / <c>ALTER</c> /
    /// <c>RECREATE</c> / <c>DROP</c> of a schema object.</summary>
    Ddl,

    /// <summary><c>COMMENT ON …</c>.</summary>
    Comment,

    /// <summary><c>SET …</c> (GENERATOR / STATISTICS / TERM / TRANSACTION / …).</summary>
    Set,

    /// <summary><c>GRANT …</c>.</summary>
    Grant,

    /// <summary><c>REVOKE …</c>.</summary>
    Revoke,

    /// <summary>A top-level <c>DECLARE …</c> (external function / filter).</summary>
    Declare,

    /// <summary>A top-level anonymous PSQL block — a bare <c>BEGIN … END</c> (optionally led by a
    /// DECLARE section / local subprogram) that is not part of a CREATE/EXECUTE definition. Formatted
    /// as a PSQL body; this is the shape the procedure/function/trigger <em>body editor</em> holds.</summary>
    AnonymousBlock,

    /// <summary>An empty statement — a lone terminator <c>;</c>.</summary>
    Empty,

    /// <summary>An unrecognised statement, preserved verbatim (the §0 safety valve).</summary>
    Raw,
}

/// <summary>
/// The abstract base of every top-level statement node. A statement holds the significant
/// <see cref="Tokens"/> that make it up (including a trailing <c>;</c> terminator when one was
/// consumed) so it can reproduce its own source and so later etaps can deepen the parse without
/// re-lexing.
/// <para>
/// In Etap 2 (the "statement skeleton" depth) each statement is classified into its own node
/// type but its interior is kept verbatim in <see cref="Tokens"/> — deeper clause / expression /
/// PSQL-body structure is added in later etaps, driven by what the formatter and semantic model
/// need. Statement nodes are therefore leaves (<see cref="Children"/> is empty) for now.
/// </para>
/// </summary>
public abstract class SqlStatement : SqlNode
{
    private static readonly IReadOnlyList<SqlNode> NoChildren = Array.Empty<SqlNode>();

    /// <summary>The significant tokens that make up this statement, in source order, including a
    /// trailing terminator <c>;</c> token when the statement was terminated by one.</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }

    /// <summary>The statement's high-level kind.</summary>
    public abstract StatementKind Kind { get; }

    private protected SqlStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length)
    {
        Tokens = tokens;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => NoChildren;
}
