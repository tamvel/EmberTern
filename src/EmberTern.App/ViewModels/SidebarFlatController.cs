using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Projects the metadata node hierarchy (connections / folders → categories → leaves) into a
/// single flat <see cref="Rows"/> list of the currently-visible rows, for a single-level
/// virtualized ListBox. This replaces the nested-VSP TreeView, whose per-item virtualization
/// made the scroll extent collapse/re-estimate during a random-access thumb drag.
///
/// Design: the flat list is a PROJECTION driven by each node's own <c>IsExpanded</c> — so
/// programmatic expansion (auto-expand-on-connect, restore-expand-state) and user toggles
/// both flow through the same path. Expand/collapse splice incrementally (the hot path — no
/// full rebuild). Structural container changes (connect populates categories, lazy-load
/// populates leaves, filter rebuilds a group's children) re-splice just that node's subtree.
/// Root changes / filter re-apply do a full rebuild. Leaves are never observed (only the
/// small set of containers: folders, connections, categories), so subscriptions stay cheap.
///
/// The node access is via delegates so the projection logic is unit-testable without
/// constructing real view-models or a live database.
/// </summary>
public sealed class SidebarFlatController : IDisposable
{
    private readonly IEnumerable _roots;
    private readonly Func<object, IEnumerable<object>?> _childrenSelector;
    private readonly Func<object, bool> _isExpandable;
    private readonly Func<object, bool> _isExpanded;
    private readonly Action<object, bool> _setExpanded;
    private readonly Func<object, bool> _isVisible;

    private readonly List<INotifyPropertyChanged> _watchedNodes = new();
    private readonly Dictionary<INotifyCollectionChanged, object> _watchedCollections = new();
    private bool _disposed;

    public ObservableCollection<SidebarRow> Rows { get; } = new();

    public SidebarFlatController(
        IEnumerable roots,
        Func<object, IEnumerable<object>?> childrenSelector,
        Func<object, bool> isExpandable,
        Func<object, bool> isExpanded,
        Action<object, bool> setExpanded,
        Func<object, bool>? isVisible = null)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _childrenSelector = childrenSelector;
        _isExpandable = isExpandable;
        _isExpanded = isExpanded;
        _setExpanded = setExpanded;
        _isVisible = isVisible ?? (_ => true);

        if (_roots is INotifyCollectionChanged incc)
        {
            incc.CollectionChanged += OnRootsChanged;
        }
        RebuildSubscriptions();
        RebuildRows();
    }

    /// <summary>Flip a node's expansion (used by the chevron). Drives the projection via the
    /// node's own IsExpanded change, so there's a single code path for user + programmatic.</summary>
    public void Toggle(SidebarRow? row)
    {
        if (row is null || !row.IsExpandable) return;
        _setExpanded(row.Node, !_isExpanded(row.Node));
    }

    /// <summary>Full re-projection. Called on root changes and after a filter re-apply
    /// (which changes node visibility / a group's children set).</summary>
    public void Rebuild()
    {
        RebuildSubscriptions();
        RebuildRows();
    }

    // ── Projection ───────────────────────────────────────────────────────────

    private void RebuildRows()
    {
        Rows.Clear();
        int at = 0;
        foreach (var root in Enumerate(_roots))
        {
            at = InsertNode(at, root, depth: 0);
        }
    }

    // Inserts a node (if visible) and its expanded, visible subtree starting at index `at`.
    // Returns the index just past the inserted block.
    private int InsertNode(int at, object node, int depth)
    {
        if (!IsRowVisible(node)) return at;
        Rows.Insert(at++, MakeRow(node, depth));
        if (_isExpandable(node) && _isExpanded(node))
        {
            foreach (var child in ChildrenOf(node))
            {
                at = InsertNode(at, child, depth + 1);
            }
        }
        return at;
    }

    private void OnExpandedChanged(object node)
    {
        int i = IndexOfNode(node);
        if (i < 0) return; // not currently projected (an ancestor is collapsed)
        var row = Rows[i];
        bool expanded = _isExpanded(node);
        row.IsExpanded = expanded;
        RemoveDescendants(i, row.Depth);
        if (expanded)
        {
            int at = i + 1;
            foreach (var child in ChildrenOf(node))
            {
                at = InsertNode(at, child, row.Depth + 1);
            }
        }
        // Newly-shown descendants may include containers to observe.
        RebuildSubscriptions();
    }

    // A container's Children changed (connect populated categories, lazy-load populated
    // leaves, filter rebuilt a group's children). Re-splice just that node's subtree.
    private void OnChildrenChanged(object? sender)
    {
        RebuildSubscriptions();
        if (sender is not INotifyCollectionChanged col
            || !_watchedCollections.TryGetValue(col, out var owner))
        {
            RebuildRows();
            return;
        }

        int i = IndexOfNode(owner);
        if (i < 0) return; // owner not shown → its children aren't shown either
        var row = Rows[i];
        RemoveDescendants(i, row.Depth);
        if (_isExpandable(owner) && _isExpanded(owner))
        {
            int at = i + 1;
            foreach (var child in ChildrenOf(owner))
            {
                at = InsertNode(at, child, row.Depth + 1);
            }
        }
    }

    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    // Remove the contiguous block of deeper-depth rows directly after index i.
    private void RemoveDescendants(int i, int depth)
    {
        while (i + 1 < Rows.Count && Rows[i + 1].Depth > depth)
        {
            Rows.RemoveAt(i + 1);
        }
    }

    private int IndexOfNode(object node)
    {
        for (int i = 0; i < Rows.Count; i++)
        {
            if (ReferenceEquals(Rows[i].Node, node)) return i;
        }
        return -1;
    }

    private SidebarRow MakeRow(object node, int depth)
        => new(node, depth, _isExpandable(node), _isExpanded(node));

    private IEnumerable<object> ChildrenOf(object node)
        => (_childrenSelector(node) ?? Enumerable.Empty<object>()).Where(IsRowVisible);

    private bool IsRowVisible(object node) => _isVisible(node);

    private static IEnumerable<object> Enumerate(IEnumerable source)
    {
        foreach (var o in source)
        {
            if (o is not null) yield return o;
        }
    }

    // ── Subscriptions (containers only — never leaves) ─────────────────────────

    private void RebuildSubscriptions()
    {
        DetachAll();
        foreach (var root in Enumerate(_roots))
        {
            WalkSubscribe(root);
        }
    }

    private void WalkSubscribe(object node)
    {
        if (node is INotifyPropertyChanged p)
        {
            p.PropertyChanged += OnNodePropertyChanged;
            _watchedNodes.Add(p);
        }
        var kids = _childrenSelector(node);
        if (kids is INotifyCollectionChanged col && !_watchedCollections.ContainsKey(col))
        {
            col.CollectionChanged += OnAnyChildrenChanged;
            _watchedCollections[col] = node;
        }
        // Recurse only into containers (expandable nodes) — leaves are never observed.
        if (kids is not null && _isExpandable(node))
        {
            foreach (var kid in kids)
            {
                if (kid is not null && _isExpandable(kid))
                {
                    WalkSubscribe(kid);
                }
            }
        }
    }

    private void DetachAll()
    {
        foreach (var p in _watchedNodes)
        {
            p.PropertyChanged -= OnNodePropertyChanged;
        }
        _watchedNodes.Clear();
        foreach (var col in _watchedCollections.Keys)
        {
            col.CollectionChanged -= OnAnyChildrenChanged;
        }
        _watchedCollections.Clear();
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is null) return;
        if (e.PropertyName == nameof(SidebarRow.IsExpanded))
        {
            OnExpandedChanged(sender);
        }
        // IsVisible (filter) is handled by the explicit Rebuild() the filter triggers.
    }

    private void OnAnyChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnChildrenChanged(sender);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_roots is INotifyCollectionChanged incc)
        {
            incc.CollectionChanged -= OnRootsChanged;
        }
        DetachAll();
    }
}
