using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Core.Search;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

public partial class MetadataExplorerViewModel : ViewModelBase
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly FirebirdMetadataReader _reader;
    private readonly SidebarFlatController _sidebar;

    public MetadataExplorerViewModel(FirebirdConnectionService connectionService, FirebirdMetadataReader reader)
    {
        _connectionService = connectionService;
        _reader = reader;
        Connections = new ObservableCollection<ConnectionNodeViewModel>();
        RootNodes = new ObservableCollection<object>();
        // Flat projection of RootNodes for the single-VSP sidebar ListBox (replaces the
        // nested-VSP TreeView). Created once; it tracks RootNodes.CollectionChanged so it
        // survives ReloadConnections (which clears + refills the same instance).
        _sidebar = new SidebarFlatController(
            RootNodes,
            childrenSelector: SidebarChildren,
            isContainer: SidebarIsContainer,
            hasChildren: SidebarHasChildren,
            isExpanded: SidebarExpanded,
            setExpanded: SidebarSetExpanded,
            isVisible: SidebarVisible);
        // Refresh is only meaningful while a database is connected. The event fires
        // on the async-continuation thread (gotcha #11), so marshal the CanExecute
        // re-evaluation onto the UI thread.
        _connectionService.ActiveConnectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RefreshCommand.NotifyCanExecuteChanged);
    }

    // Refresh only makes sense with an active connection (matches the enable/disable
    // behaviour of the other connection-dependent toolbar actions).
    private bool CanRefresh => _connectionService.IsConnected;

    // Flat list of every loaded ConnectionNodeViewModel, regardless of whether
    // the node currently sits inside a folder or at the root. Populated by
    // MainWindowViewModel.ReloadConnections alongside RootNodes.
    public ObservableCollection<ConnectionNodeViewModel> Connections { get; }

    // The tree's actual ItemsSource — a mix of FolderNodeViewModel and
    // root-level ConnectionNodeViewModel instances, ordered by SortOrder.
    public ObservableCollection<object> RootNodes { get; }

    // The flattened, single-level projection the sidebar ListBox binds to. Same nodes,
    // same order, only the currently-visible (expanded) rows — a stable-extent single VSP.
    public ObservableCollection<SidebarRow> SidebarRows => _sidebar.Rows;

    // Chevron click → flip the underlying node's expansion (drives the projection).
    public void ToggleSidebarRow(SidebarRow? row) => _sidebar.Toggle(row);

    // Node-access delegates for the flat controller (kept here so the node-type knowledge
    // stays with the explorer that owns the hierarchy).
    private static IEnumerable<object>? SidebarChildren(object node) => node switch
    {
        FolderNodeViewModel f => f.Connections,
        ConnectionNodeViewModel c => c.Children,
        MetadataNodeViewModel m when m.IsGroup => m.Children,
        _ => null,
    };

    // Structural: can this node host children? (drives subscription + recursion — a category
    // is a container even while empty, so its lazy populate is observed.)
    private static bool SidebarIsContainer(object node) => node switch
    {
        FolderNodeViewModel => true,
        ConnectionNodeViewModel => true,
        MetadataNodeViewModel m => m.IsGroup,
        _ => false,
    };

    // Does the node currently HAVE children? (drives the chevron — no expander for an empty
    // category, a disconnected connection, or a folder with no connections.)
    private static bool SidebarHasChildren(object node) => node switch
    {
        FolderNodeViewModel f => f.Connections.Count > 0,
        ConnectionNodeViewModel c => c.Children.Count > 0,
        MetadataNodeViewModel m => m.IsGroup && m.Children.Any(x => !x.IsPlaceholder),
        _ => false,
    };

    private static bool SidebarExpanded(object node) => node switch
    {
        FolderNodeViewModel f => f.IsExpanded,
        ConnectionNodeViewModel c => c.IsExpanded,
        MetadataNodeViewModel m => m.IsExpanded,
        _ => false,
    };

    private static void SidebarSetExpanded(object node, bool value)
    {
        switch (node)
        {
            case FolderNodeViewModel f: f.IsExpanded = value; break;
            case ConnectionNodeViewModel c: c.IsExpanded = value; break;
            case MetadataNodeViewModel m: m.IsExpanded = value; break;
        }
    }

    // Only metadata nodes are hidden by the filter (zero-match categories / non-matching
    // leaves); connections and folders are always shown.
    private static bool SidebarVisible(object node) => node is not MetadataNodeViewModel m || m.IsVisible;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectSelectedCommand))]
    private ConnectionNodeViewModel? _selectedConnection;

    partial void OnSelectedConnectionChanged(ConnectionNodeViewModel? oldValue, ConnectionNodeViewModel? newValue)
    {
        // Toolbar Connect/Disconnect/Reconnect enabled-state depends on the selected
        // node's IsConnected. Resubscribe on selection change so flips invalidate
        // CanExecute on those commands.
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedConnectionPropertyChanged;
        }
        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedConnectionPropertyChanged;
        }
    }

    private void OnSelectedConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionNodeViewModel.IsConnected))
        {
            ConnectSelectedCommand.NotifyCanExecuteChanged();
            DisconnectSelectedCommand.NotifyCanExecuteChanged();
            ReconnectSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string FilterPlaceholder => UiStrings.MetadataFilterPlaceholder;
    public string RefreshTooltip => UiStrings.MetadataRefreshTooltip;

    public event Action<MetadataObject>? OpenDdlRequested;
    public event Action<string>? CopyNameRequested;
    public event Action<string>? StatusReported;
    // Tree object-lifecycle dispatch. The owner (MainWindowViewModel) REUSES its existing
    // New*/detail-editor/DROP/Execute flows — these are just the tree's entry points.
    public event Action<MetadataObjectKind>? NewObjectRequested;
    public event Action<MetadataObject>? DeleteObjectRequested;
    public event Action<MetadataObject>? ExecuteProcedureRequested;
    public event Action<MetadataObjectKind>? RecompileGroupRequested;
    // Single trigger activate/deactivate (bool = activate).
    public event Action<MetadataObject, bool>? SetObjectActiveRequested;
    // Bulk trigger activate/deactivate over the visible (filtered) set or all.
    public event Action<TriggerBulkRequest>? BulkSetActiveRequested;

    /// <summary>Reflect a trigger activate/deactivate in the tree WITHOUT a full <see cref="RefreshAsync"/>
    /// — flip the matching LOADED trigger leaves in place. No collection change → no reproject, so the
    /// sidebar keeps its scroll position, selection, and expanded groups (the whole point: single/batch
    /// trigger ops no longer make the tree jump). <paramref name="names"/> null = every loaded trigger
    /// leaf (scope All); otherwise only the named ones (case-insensitive). Unloaded groups have no leaf
    /// nodes yet → nothing to update (they fetch fresh state on first expand). The schema is unchanged
    /// (same triggers, only active flags), so the name cache and filter are left intact.</summary>
    internal void ApplyTriggerActiveStateInPlace(IEnumerable<string>? names, bool active)
    {
        var set = names is null ? null : new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var connection in Connections)
        {
            if (!connection.IsConnected)
            {
                continue;
            }
            foreach (var group in connection.Children)
            {
                if (!group.IsGroup || group.Kind != MetadataObjectKind.Trigger)
                {
                    continue;
                }
                foreach (var leaf in group.AllLeaves)
                {
                    if (leaf.Object is { } o && (set is null || set.Contains(o.Name)))
                    {
                        leaf.SetActiveState(active);
                    }
                }
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        Diagnostics.RefreshTrace.Log("RefreshTree", "begin");
        // Only connected nodes have anything to refresh.
        foreach (var connection in Connections)
        {
            if (!connection.IsConnected)
            {
                continue;
            }

            foreach (var group in connection.Children)
            {
                if (!group.IsGroup)
                {
                    continue;
                }

                // Lazy model: a group is either LOADED (user expanded it → it holds the
                // real leaf list) or NOT loaded (still showing only its COUNT). Reload
                // the full list for loaded/expanded groups; for the rest just re-fetch
                // the COUNT so the "(N)" label stays current without dragging the whole
                // list back. LoadGroupAsync clears+repopulates only AFTER its fetch
                // succeeds, so a transient error keeps the old data instead of blanking.
                if (group.IsLoaded || group.IsExpanded)
                {
                    await LoadGroupAsync(group).ConfigureAwait(true);
                }
                else
                {
                    await LoadCountAsync(group).ConfigureAwait(true);
                }
            }
        }

        // Schema may have changed — drop the cached object-name index so the next
        // filter / type-ahead refetches.
        InvalidateNameCache();
        await ApplyFilterAsync().ConfigureAwait(true);
        Diagnostics.RefreshTrace.Log("RefreshTree", "end");
    }

    /// <summary>
    /// Fetches ONLY the object count for a category and stamps it on the group label
    /// (<c>Tables (2356)</c>) without loading the leaf list. Called once per category
    /// right after connect (see <see cref="ConnectionNodeViewModel.LoadCategoriesAsync"/>)
    /// so the user gets the full category breakdown immediately while the potentially
    /// thousands-strong leaf lists stay deferred to first expansion. Never calls
    /// <see cref="ApplyFilter"/> — counts load with an empty filter at connect, and
    /// re-running the filter per category would be O(n·categories) for no benefit.
    /// </summary>
    internal async Task LoadCountAsync(MetadataNodeViewModel group)
    {
        if (!group.IsGroup || group.IsLoaded || group.IsLoading)
        {
            return;
        }

        if (!_connectionService.IsConnected)
        {
            return;
        }

        try
        {
            group.Count = await _reader.CountAsync(group.Kind).ConfigureAwait(true);
        }
        catch (MetadataReadException)
        {
            // Unsupported on this FB version (e.g. Packages/Users on 2.5) or no
            // privilege — leave the count blank; the category stays expandable to retry.
        }
        catch (InvalidOperationException)
        {
        }
    }

    internal async Task LoadGroupAsync(MetadataNodeViewModel group)
    {
        if (!group.IsGroup || group.IsLoading)
        {
            return;
        }

        if (!_connectionService.IsConnected)
        {
            return;
        }

        group.IsLoading = true;
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var objects = await _reader.ListAsync(group.Kind).ConfigureAwait(true);
            Diagnostics.ScrollTrace.Rebuild($"LoadGroup {group.Kind} ({objects.Count} leaves — Children rebuilt)");
            // SetLeaves loads the master list AND sets Children to the full set; the
            // active filter (if any) is re-applied just below via ApplyFilterToGroup.
            group.SetLeaves(objects.Select(obj => MetadataNodeViewModel.CreateLeaf(this, obj)));
            group.Count = objects.Count;
            group.MarkLoaded();
            sw.Stop();
            Diagnostics.PerfTrace.LogGroupLoad(group.Kind.ToString(), objects.Count, sw.ElapsedMilliseconds);

            // Filter ONLY the group we just loaded — never the whole tree. The old global
            // ApplyFilter() here was the cause of the "expanding one category expands the
            // others" bug (#4): loading a category re-ran the global filter, which
            // re-expanded every other loaded matching group. A single group's filtering
            // touches no siblings and changes no other branch's expand state.
            var filter = (FilterText ?? string.Empty).Trim();
            if (filter.Length > 0)
            {
                ApplyFilterToGroup(group, hasFilter: true, filter);
            }
        }
        catch (MetadataReadException ex)
        {
            StatusReported?.Invoke(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            StatusReported?.Invoke(ex.Message);
        }
        finally
        {
            group.IsLoading = false;
        }
    }

    internal void RequestOpenDdl(MetadataObject obj) => OpenDdlRequested?.Invoke(obj);
    internal void RequestCopyName(string name) => CopyNameRequested?.Invoke(name);
    internal void RequestNewObject(MetadataObjectKind kind) => NewObjectRequested?.Invoke(kind);
    internal void RequestDeleteObject(MetadataObject obj) => DeleteObjectRequested?.Invoke(obj);
    internal void RequestExecuteProcedure(MetadataObject obj) => ExecuteProcedureRequested?.Invoke(obj);
    internal void RequestRecompileGroup(MetadataObjectKind kind) => RecompileGroupRequested?.Invoke(kind);
    internal void RequestSetObjectActive(MetadataObject obj, bool activate) => SetObjectActiveRequested?.Invoke(obj, activate);
    internal void RequestBulkSetActive(TriggerBulkRequest request) => BulkSetActiveRequested?.Invoke(request);

    // ── Multi-select trigger bulk ("Selected" scope) ──────────────────────────────────────────
    // The sidebar ListBox is the source of the multi-selection; the view pushes it here on every
    // SelectionChanged. Held on this singleton so the Selected commands + their count are available
    // no matter which node's context menu is open. Not persisted (a rebuild/filter clears it).
    private IReadOnlyList<MetadataObject> _selectedTriggers = Array.Empty<MetadataObject>();

    /// <summary>How many trigger leaves are currently multi-selected — the count shown in the
    /// "Activate/Deactivate selected" confirmation and used to gate those commands.</summary>
    public int SelectedTriggerCount => _selectedTriggers.Count;
    public bool HasSelectedTriggers => _selectedTriggers.Count > 0;

    /// <summary>Called by the view on every sidebar selection change with the selected rows.</summary>
    internal void SetSelectedTriggers(IEnumerable<SidebarRow> selectedRows)
    {
        _selectedTriggers = ExtractSelectedTriggers(selectedRows);
        OnPropertyChanged(nameof(SelectedTriggerCount));
        OnPropertyChanged(nameof(HasSelectedTriggers));
        ActivateSelectedTriggersCommand.NotifyCanExecuteChanged();
        DeactivateSelectedTriggersCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Pure: the distinct trigger <see cref="MetadataObject"/>s among the selected rows
    /// (connection / folder / category / placeholder / non-trigger leaves are ignored).</summary>
    internal static IReadOnlyList<MetadataObject> ExtractSelectedTriggers(IEnumerable<SidebarRow> rows)
        => rows.Select(r => r.Node)
               .OfType<MetadataNodeViewModel>()
               .Where(n => n.IsTriggerLeaf && n.Object is not null)
               .Select(n => n.Object!)
               .ToList();

    [RelayCommand(CanExecute = nameof(HasSelectedTriggers))]
    private void ActivateSelectedTriggers() => RequestSelectedTriggerBulk(activate: true);

    [RelayCommand(CanExecute = nameof(HasSelectedTriggers))]
    private void DeactivateSelectedTriggers() => RequestSelectedTriggerBulk(activate: false);

    private void RequestSelectedTriggerBulk(bool activate)
    {
        var names = _selectedTriggers.Select(t => t.Name).ToList();
        if (names.Count == 0) return;
        RequestBulkSetActive(new TriggerBulkRequest(
            MetadataObjectKind.Trigger, activate, BatchOperationScope.Selected, names));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
    private void EditSelected() => SelectedConnection?.EditCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
    private void CopySelected() => SelectedConnection?.CopyCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelectedConnection))]
    private void DeleteSelected() => SelectedConnection?.DeleteCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanConnectSelected))]
    private void ConnectSelected() => SelectedConnection?.ConnectCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanDisconnectSelected))]
    private void DisconnectSelected() => SelectedConnection?.DisconnectCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanDisconnectSelected))]
    private void ReconnectSelected() => SelectedConnection?.ReconnectCommand.Execute(null);

    private bool HasSelectedConnection() => SelectedConnection is not null;
    private bool CanConnectSelected() => SelectedConnection is { IsConnected: false };
    private bool CanDisconnectSelected() => SelectedConnection is { IsConnected: true };

    // ─── Filter debounce ──────────────────────────────────────────────────
    // TextBox.Text writes the source on every keystroke; without debounce, ApplyFilter
    // ran per character — on a big schema that's a visible stutter while typing. We
    // coalesce keystrokes into one ApplyFilter ~350 ms after the user stops (300 too
    // twitchy for fast typists, 500 reads as laggy). The timer is created lazily and
    // guarded: in unit tests / headless there's no dispatcher loop, so we fall back to
    // applying synchronously (keeps the old immediate behaviour the tests rely on).
    private const int FilterDebounceMs = 350;
    private DispatcherTimer? _filterDebounce;

    partial void OnFilterTextChanged(string value) => ScheduleFilter();

    private void ScheduleFilter()
    {
        try
        {
            if (_filterDebounce is null)
            {
                _filterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FilterDebounceMs) };
                _filterDebounce.Tick += (_, _) =>
                {
                    _filterDebounce!.Stop();
                    _ = ApplyFilterAsync();
                };
            }

            _filterDebounce.Stop();
            _filterDebounce.Start();
        }
        catch
        {
            // No usable dispatcher (unit tests / headless) — apply immediately.
            _ = ApplyFilterAsync();
        }
    }

    // ─── Session name cache (shared by filter + type-ahead) ───────────────
    // Lazy load gives the tree its fast connect, but it also means NOTHING is loaded to
    // search when the user filters or types. So the first filter / type-ahead builds a
    // flat name index per category — object NAMES only (strings, not VMs): cheap memory,
    // no layout cost, one round-trip per category, cached for the session. This is what
    // lets the filter show "Views (1)" for an un-expanded category and lets type-ahead
    // find an object in a category that was never expanded. Invalidated on
    // disconnect / reload / refresh (schema may have changed). Keyed by the group VM,
    // which is rebuilt on every ReloadConnections — hence the aggressive invalidation.
    private Dictionary<MetadataNodeViewModel, IReadOnlyList<string>>? _nameCache;
    private Task? _nameCacheTask;

    internal void InvalidateNameCache()
    {
        _nameCache = null;
        _nameCacheTask = null;
    }

    // Idempotent: the first caller starts the build, later callers join the same Task
    // (gotcha #23). After it completes, _nameCache is populated.
    internal Task EnsureNameCacheAsync() => _nameCacheTask ??= BuildNameCacheAsync();

    private async Task BuildNameCacheAsync()
    {
        var cache = new Dictionary<MetadataNodeViewModel, IReadOnlyList<string>>();
        foreach (var connection in Connections)
        {
            if (!connection.IsConnected)
            {
                continue;
            }
            foreach (var group in connection.Children)
            {
                if (!group.IsGroup)
                {
                    continue;
                }
                try
                {
                    var objects = await _reader.ListAsync(group.Kind).ConfigureAwait(true);
                    var names = new List<string>(objects.Count);
                    foreach (var o in objects)
                    {
                        names.Add(o.Name);
                    }
                    cache[group] = names;
                }
                catch (MetadataReadException) { cache[group] = Array.Empty<string>(); }
                catch (InvalidOperationException) { cache[group] = Array.Empty<string>(); }
            }
        }
        _nameCache = cache;
    }

    // Name search over the already-loaded name cache (zero DB round-trips) — the
    // "names" half of Global Search. Ensures the cache first, then runs the pure
    // MetadataNameSearch matcher over every group, keyed by kind. The source / field /
    // message half lives in FirebirdMetadataSearchReader. Only groups whose kind the
    // query includes contribute.
    internal async Task<IReadOnlyList<MetadataSearchHit>> SearchNamesAsync(MetadataSearchQuery query)
    {
        if (!query.MatchNames || string.IsNullOrWhiteSpace(query.Term))
            return Array.Empty<MetadataSearchHit>();
        await EnsureNameCacheAsync().ConfigureAwait(true);
        var cache = _nameCache;
        if (cache is null) return Array.Empty<MetadataSearchHit>();
        var groups = cache.Select(kv => (kv.Key.Kind, kv.Value));
        return MetadataNameSearch.MatchAll(groups, query);
    }

    // ─── Filter ───────────────────────────────────────────────────────────
    // IBExpert-style: while a filter is active, each category shows its MATCH count
    // ("Views (1)") and categories with zero matches HIDE — so the user sees where the
    // hits are without expanding anything. Match counts for un-expanded categories come
    // from the name cache (no list load); loaded categories also hide their non-matching
    // leaves in place. Crucially we NEVER auto-expand: opening a category is the user's
    // explicit action (see #4). Cleared filter restores every category + leaf to visible
    // and the total-count label.
    private int _filterGeneration;

    internal async Task ApplyFilterAsync()
    {
        Diagnostics.ScrollTrace.Rebuild("ApplyFilter (leaf collections rebuilt)");
        var generation = ++_filterGeneration;
        var filter = (FilterText ?? string.Empty).Trim();
        var hasFilter = filter.Length > 0;

        if (hasFilter)
        {
            // Need the name cache to count matches in un-expanded categories.
            await EnsureNameCacheAsync().ConfigureAwait(true);
            if (generation != _filterGeneration)
            {
                return; // superseded by a newer keystroke
            }
        }

        // Suspend the flat projection while the filter rebuilds each group's Children
        // item-by-item, then re-project ONCE (EndUpdate → Rebuild). Without this, clearing a
        // filter with a big category expanded would splice per restored leaf (O(n²)). The
        // final projection hides zero-match categories and shows the matching leaves.
        _sidebar.BeginUpdate();
        try
        {
            foreach (var connection in Connections)
            {
                foreach (var group in connection.Children)
                {
                    if (group.IsGroup)
                    {
                        ApplyFilterToGroup(group, hasFilter, filter);
                    }
                }
            }
        }
        finally
        {
            _sidebar.EndUpdate();
        }
    }

    // Internal so tests can drive the loaded-group path directly. For an un-expanded
    // group the match count comes from the name cache (when built); the group's leaves
    // are NOT loaded by filtering.
    internal void ApplyFilterToGroup(MetadataNodeViewModel group, bool hasFilter, string filter)
    {
        if (!hasFilter)
        {
            group.FilterMatchCount = null;
            group.IsVisible = true;
            // Restore the full leaf set (only meaningful for a loaded group).
            if (group.IsLoaded)
            {
                group.ApplyLeafFilter(null);
            }
            return;
        }

        int matches;
        if (group.IsLoaded)
        {
            // Loaded: rebuild Children to ONLY the matching leaves — do NOT hide
            // non-matches in place. A hidden-but-present leaf still occupies a VSP slot
            // the panel must realize/measure, corrupting the scroll extent on large
            // categories (the scroll-lag root cause). Match count = displayed rows.
            group.ApplyLeafFilter(leaf => !leaf.IsPlaceholder
                && leaf.GroupLabel.Contains(filter, StringComparison.OrdinalIgnoreCase));
            matches = group.Children.Count;
        }
        else
        {
            // Un-expanded: count from the name cache without loading the leaf list.
            matches = _nameCache is not null && _nameCache.TryGetValue(group, out var names)
                ? CountMatches(names, filter)
                : 0;
        }

        group.FilterMatchCount = matches;
        group.IsVisible = matches > 0;
        // NO auto-expand: the user opens the category they want; opening one branch
        // must never change another branch's expand state (#4).
    }

    // Pure substring match count (case-insensitive), matching the leaf-filter predicate.
    internal static int CountMatches(IEnumerable<string> names, string filter)
    {
        var count = 0;
        foreach (var name in names)
        {
            if (name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }
        return count;
    }
}

/// <summary>The set of objects a bulk activate/deactivate applies to: every object of the kind
/// (<see cref="All"/>), the current filter result (<see cref="Visible"/>), or the user's manual
/// multi-selection (<see cref="Selected"/>).</summary>
public enum BatchOperationScope { All, Visible, Selected }

/// <summary>
/// A bulk activate/deactivate request raised from a trigger category node (All/Visible) or from
/// the sidebar multi-selection (Selected). <paramref name="Scope"/> chooses the target set;
/// <paramref name="Names"/> carries the explicit object names for <see cref="BatchOperationScope.Visible"/>
/// and <see cref="BatchOperationScope.Selected"/> (empty for <see cref="BatchOperationScope.All"/>,
/// which the owner resolves from the reader). Only <see cref="MetadataObjectKind.Trigger"/> is used
/// today, but the shape is kind-agnostic.
/// </summary>
public sealed record TriggerBulkRequest(
    MetadataObjectKind Kind,
    bool Activate,
    BatchOperationScope Scope,
    IReadOnlyList<string> Names);
