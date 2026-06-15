using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
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

                // Re-fetch every category that already held data. Categories are
                // eager-loaded on connect but stay COLLAPSED, so the previous code —
                // which only reloaded EXPANDED groups and reset everything else to a
                // bare placeholder — wiped the "(N)" count off every collapsed category
                // and never brought it back (the user saw all counters vanish after a
                // refresh). Reloading the loaded/expanded ones restores the counts in
                // place; LoadGroupAsync only clears + repopulates AFTER its fetch
                // succeeds, so a transient error keeps the old data instead of blanking
                // it. Categories that never loaded (errored / unsupported on this FB
                // version) reset to a placeholder for the user to expand-and-retry.
                if (group.IsLoaded || group.IsExpanded)
                {
                    await LoadGroupAsync(group).ConfigureAwait(true);
                }
                else
                {
                    ResetGroupToPlaceholder(group);
                }
            }
        }

        ApplyFilter();
        Diagnostics.RefreshTrace.Log("RefreshTree", "end");
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
            var objects = await _reader.ListAsync(group.Kind).ConfigureAwait(true);
            group.Children.Clear();
            foreach (var obj in objects)
            {
                group.Children.Add(MetadataNodeViewModel.CreateLeaf(this, obj));
            }
            group.Count = objects.Count;
            group.MarkLoaded();
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

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
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

    private static void ApplyFilterToGroup(MetadataNodeViewModel group, bool hasFilter, string filter)
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

    private void ResetGroupToPlaceholder(MetadataNodeViewModel group)
    {
        group.Children.Clear();
        group.Count = null;
        group.MarkUnloaded();
        group.Children.Add(MetadataNodeViewModel.CreatePlaceholder(this));
    }
}
