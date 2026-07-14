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
    [NotifyPropertyChangedFor(nameof(StatusBrushKey))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshMetadataCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecomputeAllStatisticsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RecompileAllObjectsCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isExpanded;

    // The shared TreeViewItem container style (MainWindow.axaml) binds IsVisible via a
    // ReflectionBinding across ALL node types — the same reason IsExpanded lives on all
    // three (gotcha #38). Only MetadataNodeViewModel actually toggles IsVisible (zero-match
    // category hide); a connection row is never filter-hidden, so this is a stable true.
    // Without it the ReflectionBinding fails on EVERY realized connection container ("no
    // property accessor for IsVisible on ConnectionNodeViewModel"), flooding during
    // virtualization and re-asserting container visibility mid-measure → VSP layout cycle
    // → scroll-extent never settles → thumb fights/snaps back (gotcha #156).
    public bool IsVisible => true;

    // Drag/drop visual + state markers. IsDragging follows the row that the user
    // grabbed (cursor + suppression); IsDropTarget highlights the row under the
    // pointer with the AccentMutedBrush overlay. Both are driven by code-behind
    // pointer handlers — no DragDrop API (unreliable for TreeView in Avalonia 12).
    [ObservableProperty]
    private bool _isDragging;

    [ObservableProperty]
    private bool _isDropTarget;

    public string DisplayName => $"{Profile.Name} ({Profile.Host}:{Profile.Port})";

    public string StatusIndicator => IsConnected ? "●" : "○";

    // Theme-resource key for the connection (server) SvgIcon's color, resolved through
    // IconBrushConverter: green when connected, subtle/grey when not. Replaces the old
    // ●/○ status glyph — status now reads from the icon's color. Notified off IsConnected.
    public string StatusBrushKey => IsConnected ? "ConnectedBrush" : "SubtleForegroundBrush";

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

        // Prefetch: on connect, load the FULL object list for every category (not just the
        // count) so expanding a category is instant — data ready up front, UI renders on
        // demand ("lazy rendering OK, lazy loading NO"). This is safe now that the sidebar is
        // a single-VSP flat list: only visible rows are realized, so creating the leaf VMs up
        // front no longer freezes the UI the way the old nested-VSP TreeView did (it realized
        // ~2k TreeViewItems per category). LoadGroupAsync marks the category loaded, so a
        // later expand renders from memory and never re-queries. Sequential because the
        // FbConnection services one command at a time (Task.WhenAll throws).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var cat in categories)
        {
            await metadata.LoadGroupAsync(cat).ConfigureAwait(true);
        }
        sw.Stop();
        Diagnostics.PerfTrace.LogCategoryLoad(Profile.Name, categories.Count, sw.ElapsedMilliseconds);

        // Prefetch is complete — every category's objects are loaded. Fire the definitive "metadata
        // ready" event so every open SQL editor does its final rebuild + full warm and publishes one
        // complete Semantic Model (Package 5 closure). This is the authoritative completion signal, not
        // the per-category debounce the editor also listens to.
        metadata.NotifyMetadataReady();

        // Re-assert expanded after the categories exist. OnIsConnectedChanged already
        // set IsExpanded=true synchronously; this is a plain idempotent confirmation
        // now that the TreeViewItem→VM binding is sound (see the MainWindow.axaml
        // container-style fix + gotcha #38). No Dispatcher posts, no false→true toggle:
        // those were compensating for a broken binding that could never propagate, not
        // a real realization race — proven by ConnectionExpandBindingProbe.
        IsExpanded = true;
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

    // Copy duplicates the profile then opens the new copy in the edit dialog straight away —
    // the user almost always wants to rename / re-point the fresh copy (IBExpert parity).
    [RelayCommand]
    private void Copy()
    {
        var clone = _owner?.Copy(Profile);
        if (clone is not null) _owner?.RequestEdit(clone);
    }

    // Goes through the confirming wrapper — connection delete is HIGH risk
    // (config + saved queries + workspace state, irreversible).
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_owner is not null) await _owner.DeleteWithConfirmationAsync(Profile).ConfigureAwait(true);
    }

    // Sort the siblings around this node. If this node is inside a folder, sorts
    // that folder's connections; if at root, sorts root-level connections and
    // folders together. Owner persists the new SortOrder values.
    [RelayCommand]
    private void SortAscending() => _owner?.SortSiblingsOf(this, ascending: true);

    [RelayCommand]
    private void SortDescending() => _owner?.SortSiblingsOf(this, ascending: false);

    // ─── Database-wide operations (only on the connection node) ────────────────
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task RefreshMetadataAsync() => _owner?.Metadata.RefreshAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task RecomputeAllStatisticsAsync() => _owner?.RecomputeAllIndexStatisticsAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private Task RecompileAllObjectsAsync() => _owner?.RecompileAllObjectsAsync() ?? Task.CompletedTask;

    private bool CanConnect() => !IsConnected;
    private bool CanDisconnect() => IsConnected;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && IsConnected && !_categoriesBuilt)
        {
            _ = LoadCategoriesAsync();
        }
        // Persist user-initiated expand/collapse. Owner gates on its own suppression
        // flag during ReloadConnections so this doesn't fire a save for every node
        // we touch while restoring state.
        _owner?.OnNodeExpansionChanged(Profile.Id, value);
    }

    partial void OnIsConnectedChanged(bool value)
    {
        // "Rozwinięte = połączone" — auto-expand on successful connect so the user sees
        // their categories without an extra click. Disconnect path handles collapse in
        // UpdateConnectedState before we get here (IsExpanded is already false by then).
        // A plain synchronous set is enough now that the TreeViewItem→VM IsExpanded
        // binding is sound (MainWindow.axaml single-container-style fix, gotcha #38);
        // LoadCategoriesAsync re-asserts it once the categories exist.
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

        // The session object-name cache (filter + type-ahead) is tied to the connected
        // schema — drop it on any connect/disconnect so the next search rebuilds it.
        _owner?.Metadata?.InvalidateNameCache();

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
