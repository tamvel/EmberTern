using System.Collections.Generic;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only wrapper over a parsed <see cref="PlanNode"/> for the plan tree.
/// Immutable — rebuilt whenever a new report arrives.</summary>
public sealed class PlanNodeViewModel
{
    public PlanNodeViewModel(PlanNode node)
    {
        Node = node;
        var kids = new List<PlanNodeViewModel>(node.Children.Count);
        foreach (var child in node.Children)
        {
            kids.Add(new PlanNodeViewModel(child));
        }
        Children = kids;
    }

    public PlanNode Node { get; }

    public IReadOnlyList<PlanNodeViewModel> Children { get; }

    /// <summary>True for a full/sequential table scan — the plan tree marks these.</summary>
    public bool IsSequentialScan => Node.IsSequentialScan;

    /// <summary>The label shown for this node: the object name (table/index/procedure)
    /// when present, else the access-method text, always the faithful raw text.</summary>
    public string DisplayText => Node.RawText;

    /// <summary>A short access badge, e.g. "Full Scan", "Unique Scan" — the qualifier the
    /// engine printed, or the method name when it printed none.</summary>
    public string? AccessBadge => Node.Detail;

    public static IReadOnlyList<PlanNodeViewModel> BuildRoots(PlanTree? tree)
    {
        var roots = new List<PlanNodeViewModel>();
        if (tree is not null)
        {
            foreach (var root in tree.Roots)
            {
                roots.Add(new PlanNodeViewModel(root));
            }
        }
        return roots;
    }
}
