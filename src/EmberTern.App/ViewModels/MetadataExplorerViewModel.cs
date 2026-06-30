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
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

public partial class MetadataExplorerViewModel : ViewModelBase
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly FirebirdMetadataReader _reader;

    public MetadataExplorerViewModel(FirebirdConnectionService connectionService, FirebirdMetadataReader reader)
    {
        _connectionService = connectionService;
        _reader = reader;
        Connections = new ObservableCollection<ConnectionNodeViewModel>();
        RootNodes = new ObservableCollection<object>();
    }

    // Flat list of every loaded ConnectionNodeViewModel, regardless of whether
    // the node currently sits inside a folder or at the root. Populated by
    // MainWindowViewModel.ReloadConnections alongside RootNodes.
    public ObservableCollection<ConnectionNodeViewModel> Connections { get; }

    // The tree's actual ItemsSource — a mix of FolderNodeViewModel and
    // root-level ConnectionNodeViewModel instances, ordered by SortOrder.
    public ObservableCollection<object> RootNodes { get; }

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
    // Table context-menu actions. The owner (MainWindowViewModel) reuses its
    // existing New Table / Delete flows — these are just the dispatch points.
    public event Action? NewTableRequested;
    public event Action<MetadataObject>? DeleteTableRequested;
    // Security tree actions: Users / Roles category "Add" dispatch points.
    public event Action? NewUserRequested;
    public event Action? NewRoleRequested;

    [RelayCommand]
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
            group.Children.Clear();
            foreach (var obj in objects)
            {
                group.Children.Add(MetadataNodeViewModel.CreateLeaf(this, obj));
            }
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
    internal void RequestNewTable() => NewTableRequested?.Invoke();
    internal void RequestDeleteTable(MetadataObject obj) => DeleteTableRequested?.Invoke(obj);
    internal void RequestNewUser() => NewUserRequested?.Invoke();
    internal void RequestNewRole() => NewRoleRequested?.Invoke();

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

    // Internal so tests can drive the loaded-group path directly. For an un-expanded
    // group the match count comes from the name cache (when built); the group's leaves
    // are NOT loaded by filtering.
    internal void ApplyFilterToGroup(MetadataNodeViewModel group, bool hasFilter, string filter)
    {
        if (!hasFilter)
        {
            group.FilterMatchCount = null;
            group.IsVisible = true;
            foreach (var leaf in group.Children)
            {
                leaf.IsVisible = true;
            }
            return;
        }

        int matches;
        if (group.IsLoaded)
        {
            // Loaded: filter leaves in place (hide non-matches) and count visible ones.
            matches = 0;
            foreach (var leaf in group.Children)
            {
                if (leaf.IsPlaceholder)
                {
                    leaf.IsVisible = false;
                    continue;
                }
                var m = leaf.GroupLabel.Contains(filter, StringComparison.OrdinalIgnoreCase);
                leaf.IsVisible = m;
                if (m)
                {
                    matches++;
                }
            }
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
