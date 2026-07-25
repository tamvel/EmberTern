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
    /// <summary><c>LEAVE [label];</c> or its legacy synonym <c>BREAK;</c> — both map here (one "break the
    /// loop" leaf kind); an optional loop label is retained in the tokens but not modelled structurally.</summary>
    Leave,
    /// <summary><c>POST_EVENT …;</c>.</summary>
    PostEvent,
    /// <summary><c>EXCEPTION name […];</c>.</summary>
    Exception,
    /// <summary><c>RETURN …;</c> (function / EXECUTE BLOCK).</summary>
    Return,
    /// <summary>Anything else (an unrecognised fragment — the §0 valve). A well-formed <c>WHEN … DO …</c>
    /// exception handler is a <see cref="WhenHandler"/> node and a well-formed local
    /// <c>DECLARE PROCEDURE/FUNCTION</c> is a <see cref="SubroutineDeclaration"/> node (Stage X / D9); only a
    /// malformed / unrecognised <c>WHEN</c> or sub-routine header falls back here.</summary>
    Other,
}

/// <summary>
/// A <c>BEGIN … END</c> block — the PSQL container. Holds an optional <see cref="Declarations"/> section
/// (the <c>DECLARE VARIABLE/CURSOR</c>s that precede the outermost <c>BEGIN</c> of a routine /
/// <c>EXECUTE BLOCK</c> body; empty for a nested block, and empty for an anonymous block whose leading
/// <c>DECLARE</c>s are separate top-level statements), the <see cref="Statements"/> between
/// <c>BEGIN</c> and <c>END</c>, and an optional trailing <see cref="Handlers"/> section (the
/// <c>WHEN … DO</c> exception handlers, Stage X / P1). The <c>BEGIN</c>/<c>END</c> keyword tokens belong
/// to this node's own <see cref="PsqlStatement.Tokens"/>, not to a child.
/// </summary>
public sealed class BlockStatement : PsqlStatement
{
    private readonly SqlNode[] _children;

    public BlockStatement(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        IReadOnlyList<PsqlStatement> declarations,
        IReadOnlyList<SqlNode> statements,
        IReadOnlyList<WhenHandler> handlers,
        IReadOnlyList<SubroutineDeclaration>? localRoutines = null)
        : base(start, length, tokens)
    {
        Declarations = declarations;
        LocalRoutines = localRoutines ?? System.Array.Empty<SubroutineDeclaration>();
        Statements = statements;
        Handlers = handlers;

        // Children in source order: the declaration section (DECLARE VARIABLE/CURSOR + local sub-routines,
        // all preceding the outermost BEGIN) first, then the statements and handlers. Firebird permits a
        // DECLARE VARIABLE and a local DECLARE PROCEDURE/FUNCTION in either order, so the two declaration
        // lists are MERGED by source position (Stage X / D9) — not concatenated — or a variable declared
        // after a sub-routine would break source order. In well-formed PSQL every statement precedes every
        // handler, so the statement tail is likewise a merge by source position (a malformed trailing WHEN —
        // a lossless Other leaf that lands in Statements — can interleave). Non-decreasing source order is the
        // well-formedness invariant (StructuralAstDifferentialTests).
        _children = new SqlNode[declarations.Count + LocalRoutines.Count + statements.Count + handlers.Count];
        int k = 0;
        int di = 0, ri = 0;
        while (di < declarations.Count || ri < LocalRoutines.Count)
        {
            bool takeDecl = ri >= LocalRoutines.Count
                || (di < declarations.Count && declarations[di].Start <= LocalRoutines[ri].Start);
            _children[k++] = takeDecl ? (SqlNode)declarations[di++] : LocalRoutines[ri++];
        }
        int si = 0, hi = 0;
        while (si < statements.Count || hi < handlers.Count)
        {
            bool takeStatement = hi >= handlers.Count
                || (si < statements.Count && statements[si].Start <= handlers[hi].Start);
            _children[k++] = takeStatement ? statements[si++] : handlers[hi++];
        }
    }

    /// <summary>The <c>DECLARE VARIABLE/CURSOR</c> declarations preceding the outermost <c>BEGIN</c>
    /// (in source order); empty for a nested block or an anonymous block.</summary>
    public IReadOnlyList<PsqlStatement> Declarations { get; }

    /// <summary>The <c>DECLARE PROCEDURE/FUNCTION … AS BEGIN … END</c> local sub-routine declarations of this
    /// (routine / <c>EXECUTE BLOCK</c>) body, in source order — Stage X / D9 (the flagship). Each is a named
    /// unit carrying its own <see cref="SubroutineDeclaration.Body"/>, so the debugger interprets a local
    /// routine as a real frame (spec §6) instead of the interpreter stepping through its body as if it were
    /// the enclosing routine's main flow. Empty for a body with no sub-routines, and always empty for a nested
    /// block (Firebird declares sub-routines only at a routine's top level). They sit between
    /// <see cref="Declarations"/> and <see cref="Statements"/> in <see cref="Children"/>. Additive overlay;
    /// §0 round-trip is unchanged (the tokens still live in the flat stream).</summary>
    public IReadOnlyList<SubroutineDeclaration> LocalRoutines { get; }

    /// <summary>The statements between <c>BEGIN</c> and the handler section (or <c>END</c>), in source
    /// order. A statement is either a PSQL construct (a nested <see cref="BlockStatement"/> /
    /// <see cref="IfStatement"/> / <see cref="WhileStatement"/> / <see cref="ForSelectStatement"/> /
    /// <see cref="PsqlLeafStatement"/>) or — for an embedded DSQL statement (SELECT / INSERT / UPDATE /
    /// DELETE / MERGE / EXECUTE) — the reused top-level statement node (Etap 6.9 / B5, design §3.2),
    /// carrying its full query structure; hence the element type is <see cref="SqlNode"/>.</summary>
    public IReadOnlyList<SqlNode> Statements { get; }

    /// <summary>The <c>WHEN … DO</c> exception handlers that trail the block's statements (before
    /// <c>END</c>), in declaration order; empty when the block has none (Stage X / P1). Each is one
    /// <c>WHEN</c> clause — a <see cref="WhenHandler"/> whose ordered conditions the interpreter matches in
    /// declaration order (design §3.6). A malformed / unrecognised <c>WHEN</c> is NOT a handler — it stays
    /// a lossless <see cref="PsqlLeafStatement"/> (<see cref="PsqlLeafKind.Other"/>) in
    /// <see cref="Statements"/>.</summary>
    public IReadOnlyList<WhenHandler> Handlers { get; }

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
        IReadOnlyList<SqlNode>? conditionExpressions = null,
        CallExpression? conditionCall = null)
        : base(start, length, tokens)
    {
        Then = then;
        Else = @else;
        ConditionExpressions = conditionExpressions ?? Array.Empty<SqlNode>();
        ConditionCall = conditionCall;
        _children = Pack(ConditionExpressions, then, @else);
    }

    /// <summary>The structurally-meaningful expressions embedded in the condition — a subquery
    /// (<c>IF (EXISTS (…))</c>) or a CASE (Etap 6.9 / B3–B4). Empty when the condition has none. In source
    /// order they precede the branches.</summary>
    public IReadOnlyList<SqlNode> ConditionExpressions { get; }

    /// <summary>The lone call forming the <b>entire</b> condition (<c>IF (f(x)) THEN</c>) — Stage X / D9
    /// seam c (§6.4); null when the condition is anything else (a comparison, a compound boolean, a
    /// non-call). Lets the debugger Step Into a local function whose result decides the branch. Whether
    /// <see cref="CallExpression.Name"/> is an in-scope local function is the debugger's decision. Additive
    /// overlay; the tokens still round-trip (§0).</summary>
    public CallExpression? ConditionCall { get; }

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
        IReadOnlyList<SqlNode>? conditionExpressions = null,
        CallExpression? conditionCall = null)
        : base(start, length, tokens)
    {
        Body = body;
        ConditionExpressions = conditionExpressions ?? Array.Empty<SqlNode>();
        ConditionCall = conditionCall;
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

    /// <summary>The lone call forming the <b>entire</b> loop condition (<c>WHILE (f(x)) DO</c>) — Stage X / D9
    /// seam c (§6.4); null otherwise. Lets the debugger Step Into a local function whose result decides each
    /// iteration. Whether <see cref="CallExpression.Name"/> is an in-scope local function is the debugger's
    /// decision. Additive overlay; the tokens still round-trip (§0).</summary>
    public CallExpression? ConditionCall { get; }

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
        int start, int length, IReadOnlyList<SqlToken> tokens, SqlNode? body, QueryNode? query = null,
        IReadOnlyList<string>? intoTargets = null, string? cursorName = null)
        : base(start, length, tokens)
    {
        Body = body;
        Query = query;
        IntoTargets = intoTargets ?? Array.Empty<string>();
        CursorName = cursorName;
        _children = AstChildren.Of(query, body);
    }

    /// <summary>The cursor query the loop iterates (Etap 6.9 / B3.1) — a real <see cref="QueryNode"/>, or
    /// null for a <c>FOR EXECUTE STATEMENT …</c> loop or an unrecognised cursor. An additive overlay;
    /// <see cref="PsqlStatement.Tokens"/> still round-trips (§0).</summary>
    public QueryNode? Query { get; }

    /// <summary>The loop body (a block or a single statement — a PSQL construct or a reused DSQL statement
    /// node, B5), or null on malformed input.</summary>
    public SqlNode? Body { get; }

    /// <summary>The <c>INTO &lt;var-list&gt;</c> target variable names, in order, folded to the resolution
    /// convention (unquoted upper-cased; quoted kept as written) so they key straight into the frame's
    /// values. Empty when the loop has no <c>INTO</c> (a <c>… AS CURSOR c DO … FETCH</c> loop, or malformed).
    /// The Cursor Bridge (D6) maps each fetched result column to these targets positionally. An additive
    /// overlay produced by the parser (Stage X / D6a); <see cref="PsqlStatement.Tokens"/> still round-trips.</summary>
    public IReadOnlyList<string> IntoTargets { get; }

    /// <summary>The <c>AS CURSOR &lt;name&gt;</c> cursor name (folded), or null when the loop is unnamed.
    /// Used by D6 to detect a positioned <c>WHERE CURRENT OF &lt;name&gt;</c> in the body — a §F boundary the
    /// Cursor Bridge cannot honour (a separately-opened DSQL cursor name is not visible cross-context, probed
    /// live on FB3/FB5). Additive overlay; the tokens still round-trip.</summary>
    public string? CursorName { get; }

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

/// <summary>Whether a <see cref="SubroutineDeclaration"/> is a local <c>PROCEDURE</c> (called with
/// <c>EXECUTE PROCEDURE</c>, may have <c>RETURNS (…)</c> output parameters) or a local <c>FUNCTION</c>
/// (called in an expression, returns a single value via <c>RETURN</c>).</summary>
public enum SubroutineKind
{
    /// <summary>A local <c>DECLARE PROCEDURE name (…) [RETURNS (…)] AS BEGIN … END</c>.</summary>
    Procedure,
    /// <summary>A local <c>DECLARE FUNCTION name (…) RETURNS &lt;type&gt; AS BEGIN … END</c>.</summary>
    Function,
}

/// <summary>
/// A local <c>DECLARE PROCEDURE/FUNCTION name (…) [RETURNS …] AS &lt;body&gt;</c> sub-routine declaration —
/// Stage X / D9 (the flagship). This node models the STRUCTURE the debugger needs: a named unit whose
/// <see cref="Body"/> is a real <see cref="BlockStatement"/> the interpreter runs as a nested frame (spec
/// §6.2a — "a local routine is not a special case; it is a frame whose lexical parent is the declaring
/// frame"). Before D9 the header was a lossless <see cref="PsqlLeafKind.Other"/> leaf and the body a bare
/// sibling <see cref="BlockStatement"/> mixed into the enclosing body's <see cref="BlockStatement.Statements"/>
/// — so the interpreter would have stepped onto the (unrunnable) header and through the body as main flow.
/// Grouping them here (into <see cref="BlockStatement.LocalRoutines"/>, out of <see cref="BlockStatement.Statements"/>)
/// fixes that.
/// <para>
/// The node's span (and its <see cref="PsqlStatement.Tokens"/>) cover the <b>whole</b> sub-routine — its
/// header AND its <see cref="Body"/> — like every other compound node (a <see cref="BlockStatement"/> /
/// <see cref="IfStatement"/> also span their children), so the <see cref="Body"/> child's span nests inside
/// (the well-formedness invariant). The header is the token run before the body's <c>BEGIN</c> (from
/// <c>DECLARE</c> up to, exclusive, the body block — or up to the terminating <c>;</c> of a forward
/// declaration): it carries the parameter / <c>RETURNS</c> lists, which the binder and the debugger's
/// signature extractor read verbatim from those tokens (R2/R3) rather than as re-modelled tree fields —
/// mirroring how a <see cref="DeclareVariableStatement"/> keeps its type in tokens. Additive overlay; §0
/// round-trip is unchanged.
/// </para>
/// </summary>
public sealed class SubroutineDeclaration : PsqlStatement
{
    private readonly SqlNode[] _children;

    public SubroutineDeclaration(
        int start, int length, IReadOnlyList<SqlToken> tokens, SubroutineKind kind, string? name, BlockStatement? body)
        : base(start, length, tokens)
    {
        Kind = kind;
        Name = name;
        Body = body;
        _children = body is null ? System.Array.Empty<SqlNode>() : new SqlNode[] { body };
    }

    /// <summary>Whether this is a local procedure or a local function.</summary>
    public SubroutineKind Kind { get; }

    /// <summary>The sub-routine's name (unquoted upper-cased to match resolution; quoted kept as written),
    /// or null when it could not be read (mid-edit).</summary>
    public string? Name { get; }

    /// <summary>The sub-routine's body block (the <c>BEGIN … END</c> after its header's <c>AS</c>), or null
    /// for a forward declaration (<c>DECLARE PROCEDURE name (…) [RETURNS …];</c> with no body — the real
    /// definition supplies the body). The interpreter runs this as a nested frame (spec §6).</summary>
    public BlockStatement? Body { get; }

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
        int start, int length, IReadOnlyList<SqlToken> tokens, PsqlLeafKind kind,
        IReadOnlyList<SqlNode>? expressions = null,
        CallExpression? rhsCall = null, string? assignmentTarget = null)
        : base(start, length, tokens)
    {
        Kind = kind;
        _children = expressions ?? Array.Empty<SqlNode>();
        RhsCall = rhsCall;
        AssignmentTarget = assignmentTarget;
    }

    /// <summary>The coarse role of this leaf (a hint for consumers; never affects the round-trip).</summary>
    public PsqlLeafKind Kind { get; }

    /// <summary>The lone-call operand of this leaf when it is exactly <c>v = f(args)</c> (an
    /// <see cref="PsqlLeafKind.Assignment"/> whose whole RHS is a call) or <c>RETURN f(args)</c> (a
    /// <see cref="PsqlLeafKind.Return"/> whose whole operand is a call) — Stage X / D9 seam c (§6.4). Null for
    /// every other leaf, and null when the RHS/operand is not <b>exactly</b> a lone call (a trailing operator,
    /// a second call, a proper sub-expression ⇒ the debugger steps over). Whether <see cref="CallExpression.Name"/>
    /// is an in-scope local function is the debugger's decision. Additive overlay; the tokens still round-trip
    /// (§0).</summary>
    public CallExpression? RhsCall { get; }

    /// <summary>For an <see cref="PsqlLeafKind.Assignment"/> leaf whose RHS is a lone call
    /// (<see cref="RhsCall"/> non-null), the folded bare-identifier target the return value is delivered into
    /// (<c>v</c> in <c>v = f(x)</c>). Null for a <c>RETURN</c> leaf (its value flows to the enclosing frame's
    /// return, not a named target) and when the target is dotted (<c>NEW.col = f(x)</c> — not recognised, a D10
    /// concern). Precedent for a folded name in the AST: <see cref="ExecuteProcedureStatement.ProcedureName"/>
    /// / <see cref="ExecuteProcedureStatement.ReturningTargets"/>.</summary>
    public string? AssignmentTarget { get; }

    /// <inheritdoc/>
    /// <remarks>The structurally-meaningful expressions embedded in the leaf's interior (Etap 6.9 / B3–B4)
    /// — a scalar subquery / <c>EXISTS</c> in an expression, a <c>CASE</c> in an assignment or <c>RETURN</c>
    /// — each owning its real query / arms. Empty for a leaf with none. The ordinary expression content
    /// stays in <see cref="PsqlStatement.Tokens"/> (structural-depth boundary); a DML/<c>SELECT</c> leaf's
    /// full clause structure is modelled by promoting it to a reused DML statement node (B5), not here.</remarks>
    public override IReadOnlyList<SqlNode> Children => _children;
}

// ── Exception handlers — Stage X / P1 (debugger prerequisite; design §3.6) ──────────────────────────
//
// A BEGIN…END block may end with a WHEN…DO handler section. Until P1 these were an unstructured
// PsqlLeafKind.Other token bag, so the future debugger's interpreter — which OWNS exception control flow
// (like IF/WHILE) — had nothing to read. P1 makes them readable from the tree: one WhenHandler per WHEN
// clause, each carrying an ordered list of conditions the interpreter matches in declaration order.
// Additive structural overlay only: the binder consumes these nodes; the formatter is untouched (its PSQL
// layout is token-based) and the byte-for-byte round-trip still comes from the lossless token stream (§0).
// Formatter convergence is deliberately out of P1 scope (build grammar depth only when a feature needs it).

/// <summary>The kind of a single <see cref="WhenCondition"/> — the error class one condition of a
/// <c>WHEN … DO</c> handler matches. Recognised by the leading keyword of the grammar (never guessed from
/// text): a <c>WHEN</c> whose condition keyword is none of these is not a handler and falls back to the
/// <see cref="PsqlLeafKind.Other"/> valve.</summary>
public enum WhenHandlerKind
{
    /// <summary><c>WHEN ANY</c> — matches any exception.</summary>
    Any,
    /// <summary><c>WHEN EXCEPTION &lt;name&gt;</c> — a named user exception.</summary>
    ExceptionName,
    /// <summary><c>WHEN GDSCODE &lt;errcode&gt;</c> — a Firebird GDS error code (symbolic name or number).</summary>
    GdsCode,
    /// <summary><c>WHEN SQLCODE &lt;number&gt;</c> — a legacy SQLCODE.</summary>
    SqlCode,
    /// <summary><c>WHEN SQLSTATE '&lt;code&gt;'</c> — an SQLSTATE string literal.</summary>
    SqlState,
}

/// <summary>
/// One condition of a <see cref="WhenHandler"/> — a <see cref="Kind"/> plus its operand (kept, like every
/// leaf interior, in <see cref="Tokens"/>). Firebird lets a single <c>WHEN</c> list several conditions,
/// comma-separated, sharing one <c>DO</c> body (<c>WHEN GDSCODE a, GDSCODE b, EXCEPTION c DO …</c>), so a
/// handler owns an ordered list of these. For an <see cref="WhenHandlerKind.ExceptionName"/> condition the
/// user-exception name is surfaced in <see cref="ExceptionName"/> (the binder references it as a schema
/// object); the other kinds' operands (a gds/sql code, an SQLSTATE literal) are not schema references and
/// stay only in <see cref="Tokens"/>.
/// </summary>
public sealed class WhenCondition : SqlNode
{
    public WhenCondition(
        int start, int length, IReadOnlyList<SqlToken> tokens, WhenHandlerKind kind, string? exceptionName)
        : base(start, length)
    {
        Tokens = tokens;
        Kind = kind;
        ExceptionName = exceptionName;
    }

    /// <summary>The condition's tokens — its keyword (<c>ANY</c>/<c>EXCEPTION</c>/<c>GDSCODE</c>/
    /// <c>SQLCODE</c>/<c>SQLSTATE</c>) and operand (§0 backing).</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }

    /// <summary>The error class this condition matches.</summary>
    public WhenHandlerKind Kind { get; }

    /// <summary>For a <see cref="WhenHandlerKind.ExceptionName"/> condition, the folded user-exception name
    /// (or null when the name is absent mid-edit); null for every other kind.</summary>
    public string? ExceptionName { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => Array.Empty<SqlNode>();
}

/// <summary>
/// A <c>WHEN &lt;condition&gt; [, &lt;condition&gt; …] DO &lt;compound_statement&gt;</c> exception handler —
/// one <c>WHEN</c> clause (Stage X / P1, design §3.6). Its <see cref="Conditions"/> are matched by the
/// interpreter in declaration order (left-to-right within the clause; the clauses themselves top-to-bottom
/// in <see cref="BlockStatement.Handlers"/>); its <see cref="Body"/> is the compound statement run when a
/// condition matches (a <see cref="BlockStatement"/>, a single PSQL construct, or a reused DSQL statement
/// node — like any other body, B5). The handler clause is not itself a step point; its body statements are.
/// A <c>WHEN</c> the parser cannot recognise as this shape is NOT a handler — it stays a lossless
/// <see cref="PsqlLeafStatement"/> (<see cref="PsqlLeafKind.Other"/>).
/// </summary>
public sealed class WhenHandler : PsqlStatement
{
    private readonly SqlNode[] _children;

    public WhenHandler(
        int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<WhenCondition> conditions, SqlNode? body)
        : base(start, length, tokens)
    {
        Conditions = conditions;
        Body = body;
        _children = new SqlNode[conditions.Count + (body is null ? 0 : 1)];
        int k = 0;
        for (int i = 0; i < conditions.Count; i++) _children[k++] = conditions[i];
        if (body is not null) _children[k] = body;
    }

    /// <summary>The clause's conditions, in declaration order (at least one for a well-formed handler).</summary>
    public IReadOnlyList<WhenCondition> Conditions { get; }

    /// <summary>The handler body — the compound statement run on a match (a block or a single statement),
    /// or null on malformed / mid-edit input.</summary>
    public SqlNode? Body { get; }

    /// <inheritdoc/>
    /// <remarks>The <see cref="Conditions"/> (in source order) then the <see cref="Body"/> — source order,
    /// since conditions precede the <c>DO</c> body.</remarks>
    public override IReadOnlyList<SqlNode> Children => _children;
}
