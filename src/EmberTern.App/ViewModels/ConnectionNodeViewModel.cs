using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

public partial class ConnectionNodeViewModel : ViewModelBase
{
    private static readonly MetadataObjectKind[] CategoryOrder =
    {
        MetadataObjectKind.Table,
        MetadataObjectKind.View,
        MetadataObjectKind.Procedure,
        MetadataObjectKind.Trigger,
        MetadataObjectKind.Function,
        MetadataObjectKind.Generator,
        MetadataObjectKind.Domain,
        MetadataObjectKind.Package,
        MetadataObjectKind.Exception,
        MetadataObjectKind.Role,
        MetadataObjectKind.User,
        MetadataObjectKind.Index,
        MetadataObjectKind.SystemTable,
    };

    private readonly MainWindowViewModel? _owner;
    private readonly FirebirdConnectionService? _service;
    private bool _categoriesBuilt;

    public ConnectionNodeViewModel(ConnectionProfile profile, MainWindowViewModel? owner = null)
    {
        Profile = profile;
        _owner = owner;
        _service = owner?.Service;
        Children = new ObservableCollection<MetadataNodeViewModel>();

        if (_service is not null)
        {
            _service.ActiveConnectionChanged += OnActiveConnectionChanged;
            UpdateConnectedState();
        }
    }

    public ConnectionProfile Profile { get; }

    public ObservableCollection<MetadataNodeViewModel> Children { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIndicator))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isExpanded;

    public string DisplayName => $"{Profile.Name} ({Profile.Host}:{Profile.Port})";

    public string StatusIndicator => IsConnected ? "●" : "○";

    /// <summary>
    /// Unsubscribes from <see cref="FirebirdConnectionService.ActiveConnectionChanged"/>.
    /// Call before discarding the node (e.g., during a <c>ReloadConnections</c> rebuild)
    /// so the service doesn't retain dead nodes via its event invocation list.
    /// </summary>
    public void Detach()
    {
        if (_service is not null)
        {
            _service.ActiveConnectionChanged -= OnActiveConnectionChanged;
        }
    }

    public async Task LoadCategoriesAsync()
    {
        if (_categoriesBuilt)
        {
            return;
        }

        var metadata = _owner?.Metadata;
        if (metadata is null)
        {
            return;
        }

        Children.Clear();
        var categories = new System.Collections.Generic.List<MetadataNodeViewModel>(CategoryOrder.Length);
        foreach (var kind in CategoryOrder)
        {
            var cat = MetadataNodeViewModel.CreateGroup(metadata, kind);
            Children.Add(cat);
            categories.Add(cat);
        }
        _categoriesBuilt = true;

        // Auto-expand the root connection node. The straight assignment-then-await pattern
        // didn't take on first connect (works on subsequent reconnects, suggesting a
        // TreeViewItem-container-not-yet-realized race the first time around). Belt-and-
        // braces: set synchronously here so the TwoWay binding sees the change while the
        // VM tree is still on the UI thread untouched-by-await, and also re-post at
        // Background priority after eager-load — Background is lower than Loaded, so it
        // runs after every layout/render/loaded round triggered by the children mutations.
        // If the synchronous set lands, the post is a no-op (same-value short-circuit).
        IsExpanded = true;

        // Eager-load each category sequentially so counts (Tables (2158), ...) show
        // right after connect — matches IBExpert UX. Cannot Task.WhenAll because the
        // FirebirdClient FbConnection only services one command at a time; concurrent
        // commands throw. Each LoadGroupAsync opens its own short-lived ReadCommitted tx.
        // Categories stay collapsed (IsExpanded defaults to false); only the root
        // connection node is auto-expanded.
        foreach (var cat in categories)
        {
            await metadata.LoadGroupAsync(cat).ConfigureAwait(true);
        }

        // Backup post: if the synchronous set above missed (container not yet realized,
        // binding not yet subscribed to VM.PropertyChanged), force a change notification
        // by toggling false→true via two dispatcher rounds. Toggling, not just re-setting,
        // because CommunityToolkit's [ObservableProperty] short-circuits same-value sets —
        // a bare second IsExpanded=true would not fire PropertyChanged.
        Dispatcher.UIThread.Post(() =>
        {
            if (IsExpanded)
            {
                IsExpanded = false;
            }
            Dispatcher.UIThread.Post(() => IsExpanded = true, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private Task ConnectAsync() => _owner?.ConnectAsync(Profile) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task DisconnectAsync() => _owner?.DisconnectAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task ReconnectAsync()
    {
        if (_owner is null)
        {
            return;
        }

        // Disconnect path may pop a confirm dialog if a tx is active. If the user
        // cancels, the service stays connected — bail without trying to connect.
        await _owner.DisconnectAsync().ConfigureAwait(true);
        if (_owner.Service.IsConnected)
        {
            return;
        }
        await _owner.ConnectAsync(Profile).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Edit() => _owner?.RequestEdit(Profile);

    [RelayCommand]
    private void Copy() => _owner?.Copy(Profile);

    [RelayCommand]
    private void Delete() => _owner?.Delete(Profile);

    // Sort the siblings around this node. If this node is inside a folder, sorts
    // that folder's connections; if at root, sorts root-level connections and
    // folders together. Owner persists the new SortOrder values.
    [RelayCommand]
    private void SortAscending() => _owner?.SortSiblingsOf(this, ascending: true);

    [RelayCommand]
    private void SortDescending() => _owner?.SortSiblingsOf(this, ascending: false);

    private bool CanConnect() => !IsConnected;
    private bool CanDisconnect() => IsConnected;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && IsConnected && !_categoriesBuilt)
        {
            _ = LoadCategoriesAsync();
        }
    }

    partial void OnIsConnectedChanged(bool value)
    {
        // "Rozwinięte = połączone" — auto-expand on successful connect so the user sees
        // their categories without an extra click. Disconnect path handles collapse in
        // UpdateConnectedState before we get here (IsExpanded is already false by then).
        // Set IsExpanded synchronously here: LoadCategoriesAsync also sets it but bails
        // early when owner/metadata is null (unit-test scenarios), so the sync set is
        // the single source of truth for the connected→expanded invariant.
        if (value)
        {
            IsExpanded = true;
            _ = LoadCategoriesAsync();
        }
    }

    private void OnActiveConnectionChanged(object? sender, EventArgs e)
    {
        // ConnectAsync/DisconnectAsync raise this from the await-continuation thread,
        // not the UI thread. UpdateConnectedState flips IsConnected → IsExpanded,
        // touches Children, and triggers compiled bindings that demand the UI thread.
        // Marshal back via Dispatcher; ConnectAsync itself stays free of dispatcher noise.
        Dispatcher.UIThread.Post(UpdateConnectedState);
    }

    private void UpdateConnectedState()
    {
        var active = _service?.ActiveProfile;
        var nowConnected = active is not null && active.Id == Profile.Id;

        if (nowConnected == IsConnected)
        {
            return;
        }

        if (nowConnected)
        {
            // Transitioning to connected: seed a placeholder so the chevron renders
            // (Avalonia hides the expander when HasItems=false). OnIsConnectedChanged
            // then flips IsExpanded → LoadCategoriesAsync replaces placeholder with
            // the metadata category nodes (one per MetadataObjectKind).
            Children.Clear();
            SeedPlaceholder();
            IsConnected = true;
        }
        else
        {
            // Transitioning to disconnected: collapse and drop ALL children. No chevron,
            // no expansion possible — matches the "rozwinięte = połączone" invariant.
            IsExpanded = false;
            Children.Clear();
            _categoriesBuilt = false;
            IsConnected = false;
        }
    }

    private void SeedPlaceholder()
    {
        var metadata = _owner?.Metadata;
        if (metadata is not null)
        {
            Children.Add(MetadataNodeViewModel.CreatePlaceholder(metadata));
        }
    }
}
