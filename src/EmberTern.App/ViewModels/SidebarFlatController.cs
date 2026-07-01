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
/// virtualized ListBox. Replaces the nested-VSP TreeView, whose per-item virtualization made
/// the scroll extent collapse/re-estimate during a random-access thumb drag.
///
/// The flat list is a PROJECTION driven by each node's own <c>IsExpanded</c> — user toggles
/// and programmatic expansion (auto-expand-on-connect, restore-expand-state) flow through the
/// same path. Expand/collapse splice incrementally (the hot path). Two node notions are kept
/// distinct: a node is a <em>container</em> (structural — can host children, so it's observed
/// for lazy populate) independently of whether it currently <em>has children</em> (drives the
/// chevron). Only containers are observed (never leaves); a category's leaves arriving in bulk
/// (connect-time prefetch) only refresh the chevron, they don't re-walk subscriptions.
///
/// Node access is via delegates so the projection is unit-testable without real view-models.
/// </summary>
public sealed class SidebarFlatController : IDisposable
{
    private readonly IEnumerable _roots;
    private readonly Func<object, IEnumerable<object>?> _childrenSelector;
    private readonly Func<object, bool> _isContainer;   // structural: can host children (observe + recurse)
    private readonly Func<object, bool> _hasChildren;   // currently has children (chevron)
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
        Func<object, bool> isContainer,
        Func<object, bool> hasChildren,
        Func<object, bool> isExpanded,
        Action<object, bool> setExpanded,
        Func<object, bool>? isVisible = null)
    {
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _childrenSelector = childrenSelector;
        _isContainer = isContainer;
        _hasChildren = hasChildren;
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

    /// <summary>Flip a node's expansion (chevron). Drives the projection via the node's own
    /// IsExpanded change — a single path for user + programmatic expansion.</summary>
    public void Toggle(SidebarRow? row)
    {
        if (row is null || !row.IsExpandable) return;
        _setExpanded(row.Node, !_isExpanded(row.Node));
    }

    /// <summary>Full re-projection (root changes, filter re-apply).</summary>
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

    // Insert a node (if visible) + its expanded, visible subtree at index `at`; return the
    // index just past the inserted block.
    private int InsertNode(int at, object node, int depth)
    {
        if (!_isVisible(node)) return at;
        Rows.Insert(at++, MakeRow(node, depth));
        if (_isExpanded(node))
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
        if (i < 0) return; // not projected (an ancestor is collapsed)
        var row = Rows[i];
        row.IsExpanded = _isExpanded(node);
        RemoveDescendants(i, row.Depth);
        if (row.IsExpanded)
        {
            int at = i + 1;
            foreach (var child in ChildrenOf(node))
            {
                at = InsertNode(at, child, row.Depth + 1);
            }
        }
    }

    // A watched container's children changed: connect populates a connection's categories,
    // connect-time prefetch bulk-populates a category's leaves, or the filter rebuilds a
    // group's children. Refresh the owner's chevron; re-walk subscriptions only if a CONTAINER
    // entered (leaves are never observed — this keeps bulk leaf-populate from thrashing); and
    // re-splice only when the owner is shown AND expanded.
    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is not INotifyCollectionChanged col
            || !_watchedCollections.TryGetValue(col, out var owner))
        {
            Rebuild();
            return;
        }

        int i = IndexOfNode(owner);
        if (i >= 0)
        {
            Rows[i].IsExpandable = _hasChildren(owner);
        }

        if (AddedContainers(e))
        {
            RebuildSubscriptions();
        }

        if (i >= 0 && _isExpanded(owner))
        {
            var row = Rows[i];
            RemoveDescendants(i, row.Depth);
            int at = i + 1;
            foreach (var child in ChildrenOf(owner))
            {
                at = InsertNode(at, child, row.Depth + 1);
            }
        }
    }

    // True if the change could have introduced a new container to observe. A Reset is
    // ambiguous (Clear during a bulk replace) → re-walk to be safe; that fires once per
    // SetLeaves, while the per-leaf Adds that follow carry non-container items → skipped.
    private bool AddedContainers(NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) return true;
        if (e.NewItems is null) return false;
        foreach (var it in e.NewItems)
        {
            if (it is not null && _isContainer(it)) return true;
        }
        return false;
    }

    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

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
        => new(node, depth, _hasChildren(node), _isExpanded(node));

    private IEnumerable<object> ChildrenOf(object node)
        => (_childrenSelector(node) ?? Enumerable.Empty<object>()).Where(_isVisible);

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
        // Recurse only into child CONTAINERS — leaves are never observed. (The child
        // collection above is still watched, so a container's leaves populating is detected.)
        if (kids is not null && _isContainer(node))
        {
            foreach (var kid in kids)
            {
                if (kid is not null && _isContainer(kid))
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
        => OnChildrenChanged(sender, e);

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
