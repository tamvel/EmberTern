using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

// The concrete PSQL body statement nodes — Etap 6.9 milestone B1 (structural depth). They model the
// STRUCTURE of a PSQL body (blocks, control flow, declarations, executable-leaf spans) so the binder,
// debugger, folding and breadcrumbs read it from the tree instead of each re-scanning tokens. A leaf's
// INTERIOR (an assignment's expression, a DML statement's clauses) stays in Tokens for now — the query
// tree (B2/B3) deepens the DML leaves later; ordinary expressions stay token fragments by design
// (structural depth). §0: every node is a structural overlay on the lossless token stream (round-trip
// comes from SqlScript.Tokens), and anything the parser cannot recognise becomes a PsqlLeafStatement —
// the PSQL-level analogue of RawStatement.

/// <summary>The coarse role of a <see cref="PsqlLeafStatement"/> — a cheap classification by leading
/// keyword, a hint for consumers (e.g. a diagnostic that flags <c>SUSPEND</c> outside a selectable
/// context) that never affects the round-trip. Only PSQL-specific leaves reach a
/// <see cref="PsqlLeafStatement"/>; an embedded DSQL statement (SELECT / INSERT / UPDATE / DELETE / MERGE
/// / EXECUTE) is the reused top-level statement node instead (Etap 6.9 / B5), so there is no DML/SELECT
/// leaf kind.</summary>
public enum PsqlLeafKind
{
    /// <summary><c>var = expr;</c> (or <c>NEW.col = …</c>).</summary>
    Assignment,
    /// <summary><c>SUSPEND;</c>.</summary>
    Suspend,
    /// <summary><c>EXIT;</c>.</summary>
    Exit,
    /// <summary><c>LEAVE [label];</c>.</summary>
    Leave,
    /// <summary><c>POST_EVENT …;</c>.</summary>
    PostEvent,
    /// <summary><c>EXCEPTION name […];</c>.</summary>
    Exception,
    /// <summary><c>RETURN …;</c> (function / EXECUTE BLOCK).</summary>
    Return,
    /// <summary>Anything else (incl. a subprogram header, a <c>WHEN … DO …</c> handler leaf, or an
    /// unrecognised fragment — the §0 valve).</summary>
    Other,
}

/// <summary>
/// A <c>BEGIN … END</c> block — the PSQL container. Holds an optional <see cref="Declarations"/> section
/// (the <c>DECLARE VARIABLE/CURSOR</c>s that precede the outermost <c>BEGIN</c> of a routine /
/// <c>EXECUTE BLOCK</c> body; empty for a nested block, and empty for an anonymous block whose leading
/// <c>DECLARE</c>s are separate top-level statements) and the <see cref="Statements"/> between
/// <c>BEGIN</c> and <c>END</c>. The <c>BEGIN</c>/<c>END</c> keyword tokens belong to this node's own
/// <see cref="PsqlStatement.Tokens"/>, not to a child.
/// </summary>
public sealed class BlockStatement : PsqlStatement
{
    private readonly SqlNode[] _children;

    public BlockStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        IReadOnlyList<PsqlStatement> declarations,
        IReadOnlyList<SqlNode> statements)
        : base(start, length, tokens)
    {
        Declarations = declarations;
        Statements = statements;
        _children = new SqlNode[declarations.Count + statements.Count];
        int k = 0;
        foreach (var d in declarations) _children[k++] = d;
        foreach (var s in statements) _children[k++] = s;
    }

    /// <summary>The <c>DECLARE VARIABLE/CURSOR</c> declarations preceding the outermost <c>BEGIN</c>
    /// (in source order); empty for a nested block or an anonymous block.</summary>
    public IReadOnlyList<PsqlStatement> Declarations { get; }

    /// <summary>The statements between <c>BEGIN</c> and <c>END</c>, in source order. A statement is either
    /// a PSQL construct (a nested <see cref="BlockStatement"/> / <see cref="IfStatement"/> /
    /// <see cref="WhileStatement"/> / <see cref="ForSelectStatement"/> / <see cref="PsqlLeafStatement"/>) or
    /// — for an embedded DSQL statement (SELECT / INSERT / UPDATE / DELETE / MERGE / EXECUTE) — the reused
    /// top-level statement node (Etap 6.9 / B5, design §3.2), carrying its full query structure; hence the
    /// element type is <see cref="SqlNode"/>.</summary>
    public IReadOnlyList<SqlNode> Statements { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary><c>IF (cond) THEN &lt;then&gt; [ELSE &lt;else&gt;]</c>. The condition stays in
/// <see cref="PsqlStatement.Tokens"/> (expression depth is out of B1 scope); the branches are child
/// statements (each a block or a single statement).</summary>
public sealed class IfStatement : PsqlStatement, IExecutableStatement
{
    private readonly SqlNode[] _children;

    public IfStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        SqlNode? then,
        SqlNode? @else,
        IReadOnlyList<SqlNode>? conditionExpressions = null)
        : base(start, length, tokens)
    {
        Then = then;
        Else = @else;
        ConditionExpressions = conditionExpressions ?? Array.Empty<SqlNode>();
        _children = Pack(ConditionExpressions, then, @else);
    }

    /// <summary>The structurally-meaningful expressions embedded in the condition — a subquery
    /// (<c>IF (EXISTS (…))</c>) or a CASE (Etap 6.9 / B3–B4). Empty when the condition has none. In source
    /// order they precede the branches.</summary>
    public IReadOnlyList<SqlNode> ConditionExpressions { get; }

    /// <summary>The THEN branch (a block or a single statement — a PSQL construct or a reused DSQL
    /// statement node, B5), or null on malformed input.</summary>
    public SqlNode? Then { get; }

    /// <summary>The ELSE branch (a block, a single statement, or a chained <see cref="IfStatement"/>),
    /// or null when there is no ELSE.</summary>
    public SqlNode? Else { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;

    private static SqlNode[] Pack(IReadOnlyList<SqlNode> head, SqlNode? a, SqlNode? b)
    {
        int n = head.Count + (a is null ? 0 : 1) + (b is null ? 0 : 1);
        if (n == 0) return Array.Empty<SqlNode>();
        var arr = new SqlNode[n];
        int k = 0;
        for (int i = 0; i < head.Count; i++) arr[k++] = head[i];
        if (a is not null) arr[k++] = a;
        if (b is not null) arr[k++] = b;
        return arr;
    }
}

/// <summary><c>WHILE (cond) DO &lt;body&gt;</c>. The condition stays in
/// <see cref="PsqlStatement.Tokens"/> (with its embedded subqueries/CASE as
/// <see cref="ConditionExpressions"/>); the body is a child statement.</summary>
public sealed class WhileStatement : PsqlStatement, IExecutableStatement
{
    private readonly SqlNode[] _children;

    public WhileStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        SqlNode? body,
        IReadOnlyList<SqlNode>? conditionExpressions = null)
        : base(start, length, tokens)
    {
        Body = body;
        ConditionExpressions = conditionExpressions ?? Array.Empty<SqlNode>();
        var head = ConditionExpressions;
        if (head.Count == 0)
        {
            _children = body is null ? Array.Empty<SqlNode>() : new SqlNode[] { body };
        }
        else
        {
            var arr = new SqlNode[head.Count + (body is null ? 0 : 1)];
            int k = 0;
            for (int i = 0; i < head.Count; i++) arr[k++] = head[i];
            if (body is not null) arr[k] = body;
            _children = arr;
        }
    }

    /// <summary>The structurally-meaningful expressions embedded in the condition (subquery / CASE);
    /// empty when none. In source order they precede the body.</summary>
    public IReadOnlyList<SqlNode> ConditionExpressions { get; }

    /// <summary>The loop body (a block or a single statement — a PSQL construct or a reused DSQL statement
    /// node, B5), or null on malformed input.</summary>
    public SqlNode? Body { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary><c>FOR &lt;select&gt; [INTO &lt;vars&gt;] DO &lt;body&gt;</c>. The cursor query is a real
/// <see cref="Query"/> (<see cref="QueryNode"/>) from Etap 6.9 / B3.1 (null for a
/// <c>FOR EXECUTE STATEMENT …</c>, whose statement is dynamic/string, or when unrecognised); the INTO
/// targets stay in <see cref="PsqlStatement.Tokens"/> (ordinary expression depth); the loop body is a
/// child statement. Children are the cursor query then the body, in source order.</summary>
public sealed class ForSelectStatement : PsqlStatement, IExecutableStatement
{
    private readonly SqlNode[] _children;

    public ForSelectStatement(
        int start, int length, IReadOnlyList<SqlToken> tokens, SqlNode? body, QueryNode? query = null)
        : base(start, length, tokens)
    {
        Body = body;
        Query = query;
        _children = AstChildren.Of(query, body);
    }

    /// <summary>The cursor query the loop iterates (Etap 6.9 / B3.1) — a real <see cref="QueryNode"/>, or
    /// null for a <c>FOR EXECUTE STATEMENT …</c> loop or an unrecognised cursor. An additive overlay;
    /// <see cref="PsqlStatement.Tokens"/> still round-trips (§0).</summary>
    public QueryNode? Query { get; }

    /// <summary>The loop body (a block or a single statement — a PSQL construct or a reused DSQL statement
    /// node, B5), or null on malformed input.</summary>
    public SqlNode? Body { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary>A <c>DECLARE [VARIABLE] name type [= default];</c> local-variable declaration.</summary>
public sealed class DeclareVariableStatement : PsqlStatement
{
    public DeclareVariableStatement(int start, int length, IReadOnlyList<SqlToken> tokens, string? name)
        : base(start, length, tokens) => Name = name;

    /// <summary>The declared variable's name (unquoted upper-cased to match resolution; quoted kept as
    /// written), or null when it could not be read.</summary>
    public string? Name { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => Array.Empty<SqlNode>();
}

/// <summary>A <c>DECLARE name CURSOR FOR (select);</c> cursor declaration. The cursor's query is a real
/// <see cref="Query"/> (<see cref="QueryNode"/>) from Etap 6.9 / B3.1.</summary>
public sealed class DeclareCursorStatement : PsqlStatement
{
    private readonly SqlNode[] _children;

    public DeclareCursorStatement(
        int start, int length, IReadOnlyList<SqlToken> tokens, string? name, QueryNode? query = null)
        : base(start, length, tokens)
    {
        Name = name;
        Query = query;
        _children = query is null ? Array.Empty<SqlNode>() : new SqlNode[] { query };
    }

    /// <summary>The declared cursor's name, or null when it could not be read.</summary>
    public string? Name { get; }

    /// <summary>The cursor's <c>FOR ( &lt;query&gt; )</c> query (Etap 6.9 / B3.1) — a real
    /// <see cref="QueryNode"/>, or null when unrecognised. An additive overlay;
    /// <see cref="PsqlStatement.Tokens"/> still round-trips (§0).</summary>
    public QueryNode? Query { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary>
/// An executable leaf statement — an assignment, <c>EXECUTE</c>, <c>SUSPEND</c>, <c>EXCEPTION</c>, a DML
/// statement, a subprogram header, an unrecognised fragment, etc. — carrying its own source span so a
/// debugger can stop on it. Its interior stays in <see cref="PsqlStatement.Tokens"/> (deepened later for
/// DML by the query tree); <see cref="Kind"/> is a cheap leading-keyword hint. This is also the
/// PSQL-level §0 valve: whatever the body parser does not recognise as a compound construct lands here,
/// verbatim in its tokens.
/// </summary>
public sealed class PsqlLeafStatement : PsqlStatement, IExecutableStatement
{
    private readonly IReadOnlyList<SqlNode> _children;

    public PsqlLeafStatement(
        int start, int length, IReadOnlyList<SqlToken> tokens, PsqlLeafKind kind, IReadOnlyList<SqlNode>? expressions = null)
        : base(start, length, tokens)
    {
        Kind = kind;
        _children = expressions ?? Array.Empty<SqlNode>();
    }

    /// <summary>The coarse role of this leaf (a hint for consumers; never affects the round-trip).</summary>
    public PsqlLeafKind Kind { get; }

    /// <inheritdoc/>
    /// <remarks>The structurally-meaningful expressions embedded in the leaf's interior (Etap 6.9 / B3–B4)
    /// — a scalar subquery / <c>EXISTS</c> in an expression, a <c>CASE</c> in an assignment or <c>RETURN</c>
    /// — each owning its real query / arms. Empty for a leaf with none. The ordinary expression content
    /// stays in <see cref="PsqlStatement.Tokens"/> (structural-depth boundary); a DML/<c>SELECT</c> leaf's
    /// full clause structure is modelled by promoting it to a reused DML statement node (B5), not here.</remarks>
    public override IReadOnlyList<SqlNode> Children => _children;
}
