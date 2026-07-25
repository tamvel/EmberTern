using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

// Structural expression nodes — Etap 6.9 milestone B4. The design's structural-depth boundary keeps
// ordinary expressions (arithmetic, boolean, function args) as opaque token fragments, but a handful of
// expression constructs ARE structurally meaningful and become nodes: EXISTS / scalar subqueries
// (QueryNodes.cs, B3) and CASE (here, B4). A CASE is modelled so the formatter can lay out its
// WHEN/THEN/ELSE (a later convergence step), diagnostics can reason about it (e.g. an unreachable ELSE),
// and — like every other node — any query nested in a CASE branch stays reachable as a real QueryNode.
//
// §0: like every SqlNode these are lightweight structural overlays on the lossless token stream — the
// byte-for-byte round-trip comes from SqlScript.Tokens, never from these nodes. A CASE whose END the
// parser cannot match is simply not turned into a node (its tokens stay in the owning clause/leaf),
// never lost.

/// <summary>One <c>WHEN &lt;condition&gt; THEN &lt;result&gt;</c> arm of a <see cref="CaseExpression"/>.
/// The condition and result are ordinary expressions kept in <see cref="Tokens"/> (structural-depth
/// boundary); a query or CASE nested inside either is a structural <see cref="Children"/> node (recursive,
/// so a CASE in a THEN or a scalar subquery in a WHEN is reachable).</summary>
public sealed class WhenClause : SqlNode
{
    private readonly IReadOnlyList<SqlNode> _children;

    public WhenClause(int start, int length, IReadOnlyList<SqlToken> tokens, IReadOnlyList<SqlNode>? children)
        : base(start, length)
    {
        Tokens = tokens;
        _children = children ?? Array.Empty<SqlNode>();
    }

    /// <summary>The arm's tokens — <c>WHEN … THEN …</c> through the result, up to the next arm / ELSE /
    /// END.</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary>A <c>CASE … END</c> expression (Etap 6.9 / B4) — both the <b>simple</b> form
/// (<c>CASE &lt;operand&gt; WHEN v THEN r … [ELSE r] END</c>) and the <b>searched</b> form
/// (<c>CASE WHEN cond THEN r … [ELSE r] END</c>), in a SELECT expression or a PSQL statement. Its arms are
/// <see cref="Whens"/> (<see cref="WhenClause"/>s); the operand (simple form) and the ELSE result stay
/// token fragments (structural-depth boundary) but any query / nested CASE inside the operand, an arm, or
/// the ELSE is a structural <see cref="Children"/> node — so the model stays fully recursive. The
/// <c>CASE</c>/<c>END</c> keywords and the operand/ELSE tokens belong to this node's <see cref="Tokens"/>.
/// </summary>
public sealed class CaseExpression : SqlNode
{
    private readonly IReadOnlyList<SqlNode> _children;

    public CaseExpression(
        int start,
        int length,
        IReadOnlyList<SqlToken> tokens,
        bool isSearched,
        IReadOnlyList<WhenClause> whens,
        IReadOnlyList<SqlNode> children)
        : base(start, length)
    {
        Tokens = tokens;
        IsSearched = isSearched;
        Whens = whens;
        _children = children;
    }

    /// <summary>The CASE expression's tokens — <c>CASE</c> through <c>END</c> (§0 backing).</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }

    /// <summary><c>true</c> for a searched CASE (<c>CASE WHEN …</c>); <c>false</c> for a simple CASE with a
    /// leading operand (<c>CASE &lt;operand&gt; WHEN …</c>).</summary>
    public bool IsSearched { get; }

    /// <summary>The <c>WHEN … THEN …</c> arms, in source order (at least one for a well-formed CASE).</summary>
    public IReadOnlyList<WhenClause> Whens { get; }

    /// <inheritdoc/>
    /// <remarks>The operand's structural sub-nodes (simple form), then the <see cref="Whens"/>, then the
    /// ELSE result's structural sub-nodes — in source order.</remarks>
    public override IReadOnlyList<SqlNode> Children => _children;
}

/// <summary>
/// A lone call operand — <c>name(args)</c> — recognised in a <b>value-consuming position where the call is
/// the ENTIRE operand</b> (Stage X / D9 seam c, design §6.4): an assignment RHS (<c>v = f(x)</c>), a
/// <c>RETURN</c> operand (<c>RETURN f(x)</c>), or a whole <c>IF</c>/<c>WHILE</c> condition
/// (<c>IF (f(x)) THEN</c>). It lets the debugger Step Into a local <b>function</b>'s body without evaluating
/// any surrounding expression — the four positions consume the return value directly, so a proper
/// sub-expression (<c>f(x)+1</c>, <c>a AND f(x)</c>, <c>f</c> inside <c>VALUES(…)</c>) is deliberately NOT
/// modelled here; it stays a token fragment and steps over (a permanent §F boundary). The parser models only
/// "a lone call" — whether <see cref="Name"/> resolves to an in-scope local function is the debugger's
/// decision (there is no catalog in the parser), so a stored / built-in / package call is modelled the same
/// and simply resolves to step-over at run time.
/// <para>
/// <see cref="Arguments"/> reuses D8's <see cref="CallArgument"/> span record — a step-into slices and
/// evaluates each argument in the <b>caller</b> frame to seed the callee's input parameters; the argument
/// interior stays a token fragment (structural-depth boundary). This is an additive structural overlay
/// referenced by typed properties on the owning leaf/branch (like <see cref="CallArgument"/> it is not a tree
/// child); the tokens still round-trip (§0).
/// </para>
/// </summary>
public sealed class CallExpression : SqlNode
{
    public CallExpression(int start, int length, string? name, IReadOnlyList<CallArgument>? arguments)
        : base(start, length)
    {
        Name = name;
        Arguments = arguments ?? Array.Empty<CallArgument>();
    }

    /// <summary>The called routine's name — an unquoted name upper-cased to match resolution, a quoted name
    /// kept in its case (folded like <see cref="ExecuteProcedureStatement.ProcedureName"/>); null when it
    /// could not be read (mid-edit).</summary>
    public string? Name { get; }

    /// <summary>The call's positional arguments, in order — each the source span of one argument expression
    /// (reused from D8's <see cref="CallArgument"/>). Empty for a no-argument call.</summary>
    public IReadOnlyList<CallArgument> Arguments { get; }

    /// <inheritdoc/>
    public override IReadOnlyList<SqlNode> Children => Array.Empty<SqlNode>();
}
