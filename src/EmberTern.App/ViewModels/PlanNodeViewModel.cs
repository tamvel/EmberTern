using System;
using System.Collections.Generic;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only wrapper over a parsed <see cref="PlanNode"/> for the (demoted,
/// advanced) plan tree. Immutable structure — rebuilt whenever a new report arrives. Its
/// <see cref="IsExpanded"/> is seeded so the path to every full table scan is open and the
/// inert sub-query branches stay collapsed, so opening the tree lands the user on the
/// full scan, not on a wall of "Sub-query" lines.</summary>
public sealed class PlanNodeViewModel
{
    public PlanNodeViewModel(PlanNode node)
    {
        Node = node;
        var kids = new List<PlanNodeViewModel>(node.Children.Count);
        bool descendantHasFullScan = false;
        foreach (var child in node.Children)
        {
            var childVm = new PlanNodeViewModel(child);
            kids.Add(childVm);
            descendantHasFullScan |= childVm.ContainsFullScan;
        }
        Children = kids;
        Segments = PlanTextSegments.Build(node);
        ContainsFullScan = node.IsSequentialScan || descendantHasFullScan;
        // Expand only branches that lead to a full scan; collapse everything else.
        IsExpanded = descendantHasFullScan;
    }

    public PlanNode Node { get; }

    public IReadOnlyList<PlanNodeViewModel> Children { get; }

    /// <summary>True for this node or any descendant being a full/sequential table scan.</summary>
    public bool ContainsFullScan { get; }

    /// <summary>Two-way bound to the TreeViewItem so the full-scan path auto-expands.</summary>
    public bool IsExpanded { get; set; }

    /// <summary>True for a full/sequential table scan — the plan tree marks these.</summary>
    public bool IsSequentialScan => Node.IsSequentialScan;

    /// <summary>The faithful raw plan text for this node.</summary>
    public string DisplayText => Node.RawText;

    /// <summary>The same text as <see cref="DisplayText"/>, split into coloured runs from the classification
    /// <see cref="PlanNode"/> already carries. ⚠ Concatenating the segments reproduces
    /// <see cref="DisplayText"/> exactly — pinned by <c>PlanTextSegmentTests</c>, because a presentation that
    /// silently alters the engine's own wording would be worse than one with no colour at all.</summary>
    public IReadOnlyList<PlanTextSegment> Segments { get; } = Array.Empty<PlanTextSegment>();

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
