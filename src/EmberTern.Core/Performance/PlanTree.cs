using System.Collections.Generic;

namespace EmberTern.Core.Performance;

/// <summary>A parsed execution plan for one statement. A statement can have several
/// roots (e.g. one per <c>Select Expression</c> / cursor), so roots is a list.</summary>
public sealed record PlanTree
{
    public required PlanDialect Dialect { get; init; }

    public IReadOnlyList<PlanNode> Roots { get; init; } = new List<PlanNode>();

    /// <summary>The unparsed plan text, retained for the expert "raw plan" view and as a
    /// faithful fallback when parsing is only partial.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>Depth-first enumeration of every node in every root.</summary>
    public IEnumerable<PlanNode> EnumerateNodes()
    {
        foreach (var root in Roots)
        {
            foreach (var node in Walk(root))
            {
                yield return node;
            }
        }
    }

    private static IEnumerable<PlanNode> Walk(PlanNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}
