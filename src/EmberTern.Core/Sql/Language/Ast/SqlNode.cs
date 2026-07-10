using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Sql.Language.Ast;

/// <summary>
/// The abstract base of every node in the SQL/PSQL syntax tree — Etap 2 of the editor rebuild.
/// <para>
/// A node carries only an <b>absolute source span</b> (<see cref="Start"/> / <see cref="Length"/>)
/// and its ordered child nodes. It never stores the source string; the tree is a lightweight
/// structural overlay on top of the lossless token stream held by the root <see cref="SqlScript"/>.
/// This is the load-bearing §0 (Paramount Law) design decision: the byte-for-byte round-trip
/// guarantee comes from the token stream, NOT from the completeness of the tree — so even a
/// <see cref="RawStatement"/> or a shallowly-modelled statement always reproduces its source
/// exactly. Grammar depth can grow in later etaps without ever risking information loss.
/// </para>
/// <para>Immutable. Pure — no Avalonia, no Firebird driver — and offline unit-testable.</para>
/// </summary>
public abstract class SqlNode
{
    /// <summary>Absolute source offset where this node begins (at its first significant token,
    /// i.e. after that token's leading trivia).</summary>
    public int Start { get; }

    /// <summary>Length of this node's source span, in characters.</summary>
    public int Length { get; }

    /// <summary>Absolute source offset just past this node's span.</summary>
    public int End => Start + Length;

    private protected SqlNode(int start, int length)
    {
        Start = start < 0 ? 0 : start;
        Length = length < 0 ? 0 : length;
    }

    /// <summary>This node's direct child nodes, in source order. A leaf returns an empty list.</summary>
    public abstract IReadOnlyList<SqlNode> Children { get; }

    /// <summary>True when <paramref name="offset"/> lies within this node's half-open span
    /// <c>[Start, End)</c>.</summary>
    public bool SpanContains(int offset) => offset >= Start && offset < End;

    /// <summary>
    /// The deepest node whose span contains <paramref name="offset"/> (half-open <c>[Start, End)</c>),
    /// or null when the offset lies outside this node. Runs in O(depth) for the current tree shape.
    /// </summary>
    public SqlNode? NodeAt(int offset)
    {
        if (!SpanContains(offset))
        {
            return null;
        }

        foreach (var child in Children)
        {
            var hit = child.NodeAt(offset);
            if (hit is not null)
            {
                return hit;
            }
        }

        return this;
    }

    /// <summary>All descendant nodes, depth-first, in source order (excluding this node).</summary>
    public IEnumerable<SqlNode> DescendantNodes()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var deeper in child.DescendantNodes())
            {
                yield return deeper;
            }
        }
    }

    /// <summary>This node followed by all its descendants, depth-first (source order).</summary>
    public IEnumerable<SqlNode> DescendantNodesAndSelf()
    {
        yield return this;
        foreach (var d in DescendantNodes())
        {
            yield return d;
        }
    }

    /// <summary>All descendants of type <typeparamref name="T"/>, depth-first (source order).</summary>
    public IEnumerable<T> Descendants<T>() where T : SqlNode => DescendantNodes().OfType<T>();
}
