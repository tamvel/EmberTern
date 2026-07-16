using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

/// <summary>
/// The abstract base of the reusable structural unit for a <b>query</b> — a SELECT with its clauses,
/// an optional leading <c>WITH</c>, and optional set-operations (<c>UNION</c>/<c>INTERSECT</c>/
/// <c>EXCEPT</c>). It is the single node type every place a query can nest resolves to: the body of a
/// <see cref="CommonTableExpression"/>, a derived table in a <c>FROM</c>, an <c>EXISTS</c> predicate,
/// a scalar subquery, and the query a <see cref="SelectStatement"/> wraps. Because a query node can
/// contain other query nodes, arbitrary nesting — including nested CTEs — falls out of the tree with
/// no extra machinery.
/// <para>
/// <b>Extension point — Etap 6.9 (Structural AST Deepening), added in milestone B0.</b> This is the
/// abstract base only; the concrete clause / <c>FROM</c>-item / subquery model is added in B2/B3 (see
/// <c>docs/design/editor-ast-deepening.md</c>). It exists from B0 so every consumer that will read
/// query structure — the binder, formatter, diagnostics, folding, breadcrumbs — has one stable type
/// to target, and so no consumer needs its own query token-walk.
/// </para>
/// <para>
/// §0 (Paramount Law) holds by construction: like every <see cref="SqlNode"/>, a query node is a
/// lightweight structural overlay on the lossless token stream — it never stores or reshapes source,
/// so it can never break the byte-for-byte round-trip regardless of how deeply it is modelled.
/// </para>
/// </summary>
public abstract class QueryNode : SqlNode
{
    private protected QueryNode(int start, int length, IReadOnlyList<SqlToken> tokens) : base(start, length)
    {
        Tokens = tokens;
    }

    /// <summary>The significant tokens this query spans (its full source range). Consumers read a query's
    /// content from here without re-scanning; the byte-for-byte round-trip still comes from the owning
    /// <see cref="SqlScript"/>'s flat token stream, never from these overlays (§0). Every query node —
    /// however deeply modelled — reproduces its exact source range through this slice, so a text-reproducing
    /// consumer (e.g. the formatter emitting a CTE body / main query) is never at the mercy of parse
    /// completeness.</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }
}
