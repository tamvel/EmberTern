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

        ApplyFilter();
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
            ApplyFilter();
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
                    ApplyFilter();
                };
            }

            _filterDebounce.Stop();
            _filterDebounce.Start();
        }
        catch
        {
            // No usable dispatcher (unit tests / headless) — apply immediately.
            ApplyFilter();
        }
    }

    // Internal so tests can drive filtering deterministically (bypassing the debounce
    // timer, which never ticks without a dispatcher loop).
    internal void ApplyFilter()
    {
        var filter = (FilterText ?? string.Empty).Trim();
        var hasFilter = filter.Length > 0;

        foreach (var connection in Connections)
        {
            foreach (var group in connection.Children)
            {
                ApplyFilterToGroup(group, hasFilter, filter);
            }
        }
    }

    // Internal so tests can assert the "local filter, no auto-expand of unloaded groups"
    // contract directly without wiring a whole connection tree.
    internal static void ApplyFilterToGroup(MetadataNodeViewModel group, bool hasFilter, string filter)
    {
        var anyVisibleChild = false;
        foreach (var leaf in group.Children)
        {
            if (leaf.IsPlaceholder)
            {
                // Placeholder only exists so the chevron renders. Hide it when filtering.
                leaf.IsVisible = !hasFilter;
                continue;
            }
            var match = !hasFilter || leaf.GroupLabel.Contains(filter, StringComparison.OrdinalIgnoreCase);
            leaf.IsVisible = match;
            if (match)
            {
                anyVisibleChild = true;
            }
        }

        if (hasFilter)
        {
            // Hide groups with no matches; auto-expand groups with matches so user sees results.
            // If group hasn't been loaded yet, leave visible so the user can expand to load+filter.
            group.IsVisible = !group.IsLoaded || anyVisibleChild;
            if (group.IsLoaded && anyVisibleChild)
            {
                group.IsExpanded = true;
            }
        }
        else
        {
            group.IsVisible = true;
        }
    }

    // ─── Type-ahead (IBExpert-style) ──────────────────────────────────────
    // A second, filter-independent search. With a node selected and focus on the tree,
    // the user types letters into a transient buffer (the filter box stays empty); we
    // jump to the next node whose name starts with the buffer, searching forward from
    // the current selection and wrapping. On a hit the View expands the path to the
    // match and selects it. Scope is every LOADED node (descend into loaded containers
    // even when collapsed, so a leaf in a collapsed-but-loaded category is reachable and
    // its path gets expanded). Unloaded categories have no real leaves to find — the
    // user expands them (or uses the filter box, which loads-on-expand) first.

    internal sealed record TypeAheadEntry(object Node, IReadOnlyList<object> Ancestors, string Text);

    /// <summary>
    /// Pre-order flatten of the navigable tree (folders → connections → categories →
    /// leaves) in visual order, skipping placeholders. Each entry carries its expandable
    /// ancestor chain so the View can open the path to a match. Built fresh per keystroke
    /// — cheap relative to a key press, and always reflects the current load/expand state.
    /// </summary>
    internal List<TypeAheadEntry> BuildTypeAheadIndex()
    {
        var list = new List<TypeAheadEntry>();
        var ancestors = new List<object>();
        foreach (var root in RootNodes)
        {
            WalkTypeAhead(root, ancestors, list);
        }
        return list;
    }

    private static void WalkTypeAhead(object node, List<object> ancestors, List<TypeAheadEntry> acc)
    {
        var text = NodeSearchText(node);
        if (text.Length > 0)
        {
            acc.Add(new TypeAheadEntry(node, ancestors.ToArray(), text));
        }

        var children = ChildrenOf(node);
        if (children is null)
        {
            return;
        }

        ancestors.Add(node);
        foreach (var child in children)
        {
            WalkTypeAhead(child, ancestors, acc);
        }
        ancestors.RemoveAt(ancestors.Count - 1);
    }

    private static IEnumerable<object>? ChildrenOf(object node) => node switch
    {
        FolderNodeViewModel f => f.Connections,
        ConnectionNodeViewModel c => c.Children,
        // Only descend into a loaded group (its children are real leaves, not the
        // "Loading…" placeholder). Skip the placeholder either way.
        MetadataNodeViewModel { IsGroup: true } m => m.Children.Where(x => !x.IsPlaceholder),
        _ => null,
    };

    // Text matched against the type-ahead buffer. Connection matches by profile name
    // (not the "(host:port)" suffix); group by its raw label; leaf by its object name.
    internal static string NodeSearchText(object node) => node switch
    {
        ConnectionNodeViewModel c => c.Profile.Name,
        FolderNodeViewModel f => f.Name,
        MetadataNodeViewModel m => m.IsPlaceholder ? string.Empty : m.GroupLabel,
        _ => string.Empty,
    };

    /// <summary>
    /// Index of the next entry whose text starts with <paramref name="buffer"/>, scanning
    /// forward from <paramref name="currentIndex"/> and wrapping. <paramref name="inclusive"/>
    /// controls whether the current selection itself can match: a fresh single letter
    /// searches exclusively (so repeating it cycles through items starting with that
    /// letter), while refining an existing buffer searches inclusively (the current match
    /// usually still satisfies the longer prefix and stays put). Returns -1 on no match.
    /// Pure — unit-tested without any UI.
    /// </summary>
    internal static int FindTypeAheadIndex(
        IReadOnlyList<TypeAheadEntry> index, int currentIndex, bool inclusive, string buffer)
    {
        if (index.Count == 0 || string.IsNullOrEmpty(buffer))
        {
            return -1;
        }

        var n = index.Count;
        var begin = currentIndex < 0 ? 0 : currentIndex + (inclusive ? 0 : 1);
        for (var step = 0; step < n; step++)
        {
            var i = ((begin + step) % n + n) % n;
            if (index[i].Text.StartsWith(buffer, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }
}
