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
