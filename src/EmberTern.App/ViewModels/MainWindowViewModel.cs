using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Query;
using EmberTern.Core.Sql;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using CoreSavedQuery = EmberTern.Core.Workspace.SavedQuery;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;

namespace EmberTern.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConnectionProfileStore _store;
    private readonly FolderStore _folderStore;
    private FolderState _folderState = new();
    private readonly FirebirdConnectionService _service;
    private readonly TransactionService _transactionService;
    private readonly FirebirdQueryExecutor _executor;
    private readonly FirebirdMetadataReader _metadataReader;
    private readonly FirebirdDdlReader _ddlReader;
    private readonly FirebirdTableDetailReader _tableDetailReader;
    private CancellationTokenSource? _executionCts;
    private TransactionState _previousTransactionState = TransactionState.Idle;
    // Per-connection tabs. Key = ConnectionProfile.Id. Populated on disconnect/switch
    // (stashing the active connection's tabs) and drained on connect (restoring them
    // into WorkspaceTabs). Survives the lifetime of the VM and is persisted via
    // CaptureWorkspace/RestoreWorkspace.
    private readonly Dictionary<string, ConnectionWorkspace> _workspacesByConnection = new();
    // Set during connection switches / saved-query selection to suppress the
    // QueryText <-> SelectedSavedQuery.SqlText feedback loop. Without this, loading
    // a saved query into the editor would echo the same text back into the same
    // saved query, and switching connections would corrupt SavedQuery cells on the
    // way out.
    private bool _suppressSavedQuerySync;

    public MainWindowViewModel()
        : this(new ConnectionProfileStore(), new FirebirdConnectionService())
    {
    }

    public MainWindowViewModel(ConnectionProfileStore store, FirebirdConnectionService service)
        : this(store, service, new TransactionService(service))
    {
    }

    public MainWindowViewModel(ConnectionProfileStore store, FirebirdConnectionService service, TransactionService transactionService)
        : this(store, service, transactionService, new FolderStore(System.IO.Path.GetDirectoryName(store.FilePath)!))
    {
    }

    public MainWindowViewModel(ConnectionProfileStore store, FirebirdConnectionService service, TransactionService transactionService, FolderStore folderStore)
    {
        _store = store;
        _folderStore = folderStore;
        _folderState = _folderStore.Load();
        _service = service;
        _transactionService = transactionService;
        _executor = new FirebirdQueryExecutor(_service, _transactionService);
        _metadataReader = new FirebirdMetadataReader(_service, _transactionService);
        _ddlReader = new FirebirdDdlReader(_service, _transactionService);
        _tableDetailReader = new FirebirdTableDetailReader(_service, _transactionService);
        Metadata = new MetadataExplorerViewModel(_service, _metadataReader);
        Metadata.OpenDdlRequested += OnOpenDdlRequested;
        Metadata.CopyNameRequested += OnCopyNameRequested;
        Metadata.StatusReported += OnMetadataStatusReported;
        Messages = new ObservableCollection<QueryMessageViewModel>();
        // Workspace tabs start empty — no Query tab until a connection becomes active.
        // Each ConnectionProfile owns its own Query+DDL tab list via _workspacesByConnection.
        WorkspaceTabs = new ObservableCollection<WorkspaceTabViewModel>();
        SavedQueries = new ObservableCollection<SavedQueryViewModel>();
        SavedQueries.CollectionChanged += (_, _) =>
        {
            DeleteSelectedQueryCommand.NotifyCanExecuteChanged();
            ClearAllQueriesCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasSavedQueries));
            OnPropertyChanged(nameof(ShowQueryPanelEmptyHint));
        };
        _service.ActiveConnectionChanged += OnActiveConnectionChanged;
        _transactionService.TransactionStateChanged += OnTransactionStateChanged;
        ReloadConnections();
        UpdateStatusFromConnection();
    }

    internal ConnectionProfileStore Store => _store;
    internal FirebirdConnectionService Service => _service;
    internal TransactionService TransactionService => _transactionService;

    public ObservableCollection<QueryMessageViewModel> Messages { get; }
    public ObservableCollection<WorkspaceTabViewModel> WorkspaceTabs { get; }
    public ObservableCollection<SavedQueryViewModel> SavedQueries { get; }
    public MetadataExplorerViewModel Metadata { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveWorkspace))]
    [NotifyCanExecuteChangedFor(nameof(NewQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearAllQueriesCommand))]
    private string? _activeWorkspaceProfileId;

    public bool HasActiveWorkspace => ActiveWorkspaceProfileId is not null;
    public bool HasSavedQueries => SavedQueries.Count > 0;
    public bool ShowQueryPanelEmptyHint => !HasSavedQueries;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedQueryCommand))]
    private SavedQueryViewModel? _selectedSavedQuery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowQueryPanel))]
    private bool _isQueryPanelVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueryTabActive))]
    [NotifyPropertyChangedFor(nameof(IsDdlTabActive))]
    [NotifyPropertyChangedFor(nameof(IsTableDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(IsClosableTabActive))]
    [NotifyPropertyChangedFor(nameof(ShowQueryPanel))]
    [NotifyPropertyChangedFor(nameof(ActiveDdlText))]
    [NotifyPropertyChangedFor(nameof(ActiveTableDetail))]
    [NotifyPropertyChangedFor(nameof(ShowExecuteButton))]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    [NotifyPropertyChangedFor(nameof(CanClearActiveEditor))]
    [NotifyPropertyChangedFor(nameof(CanCloseActiveTab))]
    [NotifyPropertyChangedFor(nameof(CanFormatSql))]
    [NotifyCanExecuteChangedFor(nameof(ClearActiveEditorCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseActiveTabCommand))]
    [NotifyCanExecuteChangedFor(nameof(FormatSqlCommand))]
    private WorkspaceTabViewModel? _selectedWorkspaceTab;

    public bool IsQueryTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Query };
    public bool IsDdlTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Ddl };
    public bool IsTableDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TableDetail };
    // Close-tab toolbar button targets *other* tabs (DDL or TableDetail); the
    // anchored Query tab is never closable so the button hides when it's active.
    public bool IsClosableTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Ddl or WorkspaceTabKind.TableDetail };
    // Saved Queries panel is only meaningful while the Query tab is active.
    // When a DDL or TableDetail tab is active the panel collapses regardless of
    // the user's IsQueryPanelVisible toggle preference (the preference itself
    // is preserved — toggling back to the Query tab restores the previous shown/hidden state).
    public bool ShowQueryPanel => IsQueryPanelVisible && IsQueryTabActive;
    public string ActiveDdlText => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Ddl } d ? d.DdlText : string.Empty;
    public TableDetailTabViewModel? ActiveTableDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TableDetail } t ? t.TableDetail : null;
    // Execute / Cancel are mutually exclusive within a query tab; on a DDL tab both hide.
    // Clear / Close stay visible regardless and gate via IsEnabled so the user always sees
    // them but understands they don't apply (e.g. Close on the anchored SQL Editor tab).
    public bool ShowExecuteButton => IsQueryTabActive && !IsExecuting;
    public bool ShowCancelButton => IsQueryTabActive && IsExecuting;
    public bool CanClearActiveEditor => IsQueryTabActive;
    public bool CanCloseActiveTab => SelectedWorkspaceTab is { IsClosable: true };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessages))]
    [NotifyPropertyChangedFor(nameof(ShowMessagesEmptyHint))]
    private QueryResult? _currentResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentResult))]
    [NotifyPropertyChangedFor(nameof(ShowResultsEmptyHint))]
    [NotifyPropertyChangedFor(nameof(ShowTruncatedBanner))]
    [NotifyPropertyChangedFor(nameof(TruncatedBannerText))]
    private string _currentResultVersionTag = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    [NotifyPropertyChangedFor(nameof(ShowExecuteButton))]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    private bool _isExecuting;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private int _selectedBottomTabIndex;

    [ObservableProperty]
    private string _queryStatsText = string.Empty;

    [ObservableProperty]
    private string _statusText = UiStrings.StatusBarReady;

    [ObservableProperty]
    private bool _isStatusError;

    public string AppTitle => UiStrings.AppTitle;
    public string AppSubtitle => UiStrings.AppSubtitle;
    public string CommitLabel => UiStrings.TransactionCommit;
    public string RollbackLabel => UiStrings.TransactionRollback;
    public string MessagesLabel => UiStrings.BottomTabMessages;
    public string ResultsLabel => UiStrings.BottomTabResults;
    public string OutputLabel => UiStrings.BottomTabOutput;
    public string ThemeToggleTooltip => UiStrings.ThemeToggleTooltip;
    public string ExecuteLabel => UiStrings.ToolbarExecute;
    public string CancelLabel => UiStrings.ToolbarCancel;
    public string ExecuteHint => UiStrings.ToolbarExecuteHint;
    public string ResultsEmptyHint => UiStrings.ResultsEmptyHint;
    public string MessagesEmptyHint => UiStrings.MessagesEmptyHint;

    public bool IsConnected => _service.IsConnected;
    public string ActiveConnectionName => _service.ActiveProfile?.Name ?? string.Empty;
    public bool HasActiveConnection => _service.ActiveProfile is not null;

    public bool HasCurrentResult => CurrentResult is { HasResultSet: true };
    public bool ShowResultsEmptyHint => !HasCurrentResult;
    public bool ShowTruncatedBanner => CurrentResult is { Truncated: true };
    public string TruncatedBannerText => CurrentResult is { } r
        ? string.Format(UiStrings.ResultsTruncatedFormat, r.Rows.Count)
        : string.Empty;

    public bool HasMessages => Messages.Count > 0;
    public bool ShowMessagesEmptyHint => !HasMessages;

    public bool CanExecute => !IsExecuting;

    public bool IsTransactionIdle => _transactionService.IsIdle;
    public bool IsTransactionActive => _transactionService.IsActive;
    public bool IsTransactionError => _transactionService.IsError;
    public bool HasExecutedInTransaction => _transactionService.HasExecutedStatements;
    public string TransactionBarText => _transactionService.State switch
    {
        TransactionState.Active when _transactionService.HasExecutedStatements
            => $"{UiStrings.TransactionBarActive} · {string.Format(UiStrings.TransactionStatementCountFormat, _transactionService.StatementCount)}",
        TransactionState.Active => UiStrings.TransactionBarActive,
        TransactionState.Error => UiStrings.TransactionBarError,
        _ => UiStrings.TransactionBarInactive,
    };
    public void ReloadConnections()
    {
        // Detach old nodes before clearing so the service doesn't retain dead
        // subscribers via its ActiveConnectionChanged invocation list.
        foreach (var stale in Metadata.Connections)
        {
            stale.Detach();
        }
        Metadata.Connections.Clear();
        Metadata.RootNodes.Clear();

        var profiles = _store.LoadAll();
        var nodesById = new Dictionary<string, ConnectionNodeViewModel>(StringComparer.Ordinal);
        foreach (var profile in profiles)
        {
            var node = new ConnectionNodeViewModel(profile, this);
            Metadata.Connections.Add(node);
            nodesById[profile.Id] = node;
        }

        // Drop folder-map entries whose connection profiles no longer exist —
        // keeps folders.json from growing forever after profile deletions.
        var staleMappings = new List<string>();
        foreach (var kvp in _folderState.ConnectionFolderMap)
        {
            if (!nodesById.ContainsKey(kvp.Key)) staleMappings.Add(kvp.Key);
        }
        foreach (var id in staleMappings) _folderState.ConnectionFolderMap.Remove(id);

        // Drop stale per-connection sort orders too, same rationale.
        var staleSorts = new List<string>();
        foreach (var kvp in _folderState.ConnectionSortOrders)
        {
            if (!nodesById.ContainsKey(kvp.Key)) staleSorts.Add(kvp.Key);
        }
        foreach (var id in staleSorts) _folderState.ConnectionSortOrders.Remove(id);

        // Build folder VMs keyed by id, in their persisted sort order.
        var foldersById = new Dictionary<string, FolderNodeViewModel>(StringComparer.Ordinal);
        var folderVms = new List<FolderNodeViewModel>();
        foreach (var entry in _folderState.Folders)
        {
            var f = new FolderNodeViewModel(entry, this);
            foldersById[entry.Id] = f;
            folderVms.Add(f);
        }

        // Place each connection into its folder (when mapped + folder exists), otherwise root.
        var rootConnections = new List<ConnectionNodeViewModel>();
        foreach (var node in Metadata.Connections)
        {
            if (_folderState.ConnectionFolderMap.TryGetValue(node.Profile.Id, out var folderId)
                && !string.IsNullOrEmpty(folderId)
                && foldersById.TryGetValue(folderId, out var folder))
            {
                folder.Connections.Add(node);
            }
            else
            {
                rootConnections.Add(node);
            }
        }

        // Within each folder, sort children by persisted SortOrder then Name.
        foreach (var f in folderVms)
        {
            var sorted = f.Connections
                .OrderBy(c => GetConnectionSortOrder(c.Profile.Id))
                .ThenBy(c => c.Profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            f.Connections.Clear();
            foreach (var c in sorted) f.Connections.Add(c);
        }

        // RootNodes: folders + root connections mixed, sorted by SortOrder, then Name.
        // Folders use FolderEntry.SortOrder; root connections use ConnectionSortOrders.
        var rootEntries = new List<(int sort, string name, object node)>();
        foreach (var f in folderVms)
        {
            rootEntries.Add((f.Entry.SortOrder, f.Name, f));
        }
        foreach (var c in rootConnections)
        {
            rootEntries.Add((GetConnectionSortOrder(c.Profile.Id), c.Profile.Name, c));
        }
        rootEntries.Sort((a, b) =>
        {
            var bySort = a.sort.CompareTo(b.sort);
            return bySort != 0 ? bySort : string.Compare(a.name, b.name, System.StringComparison.CurrentCultureIgnoreCase);
        });

        foreach (var entry in rootEntries)
        {
            Metadata.RootNodes.Add(entry.node);
        }
    }

    private int GetConnectionSortOrder(string profileId)
        => _folderState.ConnectionSortOrders.TryGetValue(profileId, out var v) ? v : 0;

    internal FolderState FolderState => _folderState;
    internal FolderStore FolderStore => _folderStore;

    public void PersistFolderState()
    {
        try
        {
            _folderStore.Save(_folderState);
        }
        catch (System.IO.IOException) { /* best effort */ }
        catch (System.UnauthorizedAccessException) { /* best effort */ }
    }

    public FolderEntry CreateFolder(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed)) trimmed = "New folder";

        var nextSort = 0;
        foreach (var f in _folderState.Folders)
        {
            if (f.SortOrder >= nextSort) nextSort = f.SortOrder + 1;
        }
        foreach (var kvp in _folderState.ConnectionSortOrders)
        {
            // Only consider root-level connections for the nextSort horizon — folder
            // members don't compete with root-level entries.
            if (!_folderState.ConnectionFolderMap.ContainsKey(kvp.Key)
                && kvp.Value >= nextSort) nextSort = kvp.Value + 1;
        }

        var entry = new FolderEntry { Name = trimmed, SortOrder = nextSort };
        _folderState.Folders.Add(entry);
        PersistFolderState();
        ReloadConnections();
        return entry;
    }

    public async Task DeleteFolderAsync(FolderNodeViewModel folder)
    {
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.FolderDeleteConfirmTitle,
            Message = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.FolderDeleteConfirmFormat, folder.Name),
            ConfirmLabel = UiStrings.FolderDeleteConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;

        // Move children back to root before removing the folder entry.
        var toUnmap = new List<string>();
        foreach (var kvp in _folderState.ConnectionFolderMap)
        {
            if (kvp.Value == folder.Id) toUnmap.Add(kvp.Key);
        }
        foreach (var id in toUnmap) _folderState.ConnectionFolderMap.Remove(id);

        _folderState.Folders.RemoveAll(f => f.Id == folder.Id);
        PersistFolderState();
        ReloadConnections();
    }

    // Sort the siblings of the given connection node. Connection inside a folder
    // → sort that folder's connections; connection at root → sort the mixed
    // folders + root connections together by Name. Updates persisted sort orders
    // and reloads the tree.
    public void SortSiblingsOf(ConnectionNodeViewModel node, bool ascending)
    {
        if (_folderState.ConnectionFolderMap.TryGetValue(node.Profile.Id, out var folderId)
            && !string.IsNullOrEmpty(folderId))
        {
            // Folder member — sort the connections that share this folder.
            var members = new List<ConnectionNodeViewModel>();
            foreach (var c in Metadata.Connections)
            {
                if (_folderState.ConnectionFolderMap.TryGetValue(c.Profile.Id, out var fid)
                    && fid == folderId)
                {
                    members.Add(c);
                }
            }
            members.Sort((a, b) => string.Compare(a.Profile.Name, b.Profile.Name, System.StringComparison.CurrentCultureIgnoreCase));
            if (!ascending) members.Reverse();
            for (int i = 0; i < members.Count; i++)
            {
                _folderState.ConnectionSortOrders[members[i].Profile.Id] = i;
            }
        }
        else
        {
            // Root sibling — sort folders + root connections together.
            var rootEntries = new List<(string name, bool isFolder, string id)>();
            foreach (var f in _folderState.Folders)
            {
                rootEntries.Add((f.Name, true, f.Id));
            }
            foreach (var c in Metadata.Connections)
            {
                if (!_folderState.ConnectionFolderMap.TryGetValue(c.Profile.Id, out var fid)
                    || string.IsNullOrEmpty(fid))
                {
                    rootEntries.Add((c.Profile.Name, false, c.Profile.Id));
                }
            }
            rootEntries.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.CurrentCultureIgnoreCase));
            if (!ascending) rootEntries.Reverse();
            for (int i = 0; i < rootEntries.Count; i++)
            {
                if (rootEntries[i].isFolder)
                {
                    var entry = _folderState.Folders.Find(f => f.Id == rootEntries[i].id);
                    if (entry is not null) entry.SortOrder = i;
                }
                else
                {
                    _folderState.ConnectionSortOrders[rootEntries[i].id] = i;
                }
            }
        }

        PersistFolderState();
        ReloadConnections();
    }

    public WorkspaceState CaptureWorkspace()
    {
        // Make sure the currently-displayed tabs are mirrored into the dict before
        // we serialize it — they were last persisted at the previous connection-switch
        // and have drifted since then.
        if (ActiveWorkspaceProfileId is not null)
        {
            _workspacesByConnection[ActiveWorkspaceProfileId] = SnapshotCurrentTabs();
        }

        return new WorkspaceState
        {
            Workspaces = new Dictionary<string, ConnectionWorkspace>(_workspacesByConnection),
            LastActiveConnectionId = _service.ActiveProfile?.Id,
            QueryPanelVisible = IsQueryPanelVisible,
        };
    }

    public void RestoreWorkspace(WorkspaceState state)
    {
        _workspacesByConnection.Clear();
        foreach (var kvp in state.Workspaces)
        {
            _workspacesByConnection[kvp.Key] = kvp.Value;
        }

        IsQueryPanelVisible = state.QueryPanelVisible;

        // Workspace tabs stay empty at startup — there's no active connection yet.
        // The user's first Connect call will pull the matching entry out of the dict
        // via OnActiveConnectionChanged. We still pre-select the matching tree node
        // as a convenience.
        if (!string.IsNullOrEmpty(state.LastActiveConnectionId))
        {
            foreach (var node in Metadata.Connections)
            {
                if (string.Equals(node.Profile.Id, state.LastActiveConnectionId, StringComparison.Ordinal))
                {
                    Metadata.SelectedConnection = node;
                    break;
                }
            }
        }
    }

    private ConnectionWorkspace SnapshotCurrentTabs()
    {
        // Make sure the live editor content is committed to the active SavedQuery
        // before we read the SavedQueries list out — otherwise unsaved keystrokes
        // disappear at the connection-switch boundary.
        if (SelectedSavedQuery is { } active)
        {
            active.SqlText = QueryText;
        }

        var ws = new ConnectionWorkspace
        {
            ActiveTabIndex = SelectedWorkspaceTab is null ? 0 : WorkspaceTabs.IndexOf(SelectedWorkspaceTab),
            ActiveSavedQueryId = SelectedSavedQuery?.Id,
        };
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind == WorkspaceTabKind.Query)
            {
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.Query,
                    SqlText = QueryText,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.TableDetail)
            {
                // Persist as TableDetail so restore re-opens with the 3-sub-tab view
                // instead of degrading to DDL-only. Fields/Indexes aren't serialized —
                // they're re-fetched from the live DB after Connect. Cached DDL is
                // kept so the DDL sub-tab paints immediately even before fetch finishes.
                var ddl = tab.TableDetail is { } td ? td.DdlText : tab.DdlText;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.TableDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = ddl,
                });
            }
            else
            {
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.Ddl,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = tab.DdlText,
                });
            }
        }
        foreach (var sq in SavedQueries)
        {
            ws.SavedQueries.Add(new CoreSavedQuery
            {
                Id = sq.Id,
                Name = sq.Name,
                SqlText = sq.SqlText,
            });
        }
        return ws;
    }

    private void LoadWorkspaceFor(string profileId)
    {
        if (!_workspacesByConnection.TryGetValue(profileId, out var ws))
        {
            // First time we see this connection — start with a single empty Query tab.
            ws = new ConnectionWorkspace
            {
                Tabs = { new WorkspaceTab { Kind = CoreTabKind.Query, SqlText = string.Empty } },
            };
        }

        var queryTabAdded = false;
        QueryText = string.Empty;
        foreach (var tab in ws.Tabs)
        {
            if (tab.Kind == CoreTabKind.Query && !queryTabAdded)
            {
                QueryText = tab.SqlText ?? string.Empty;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateQuery(this));
                queryTabAdded = true;
            }
            else if (tab.Kind == CoreTabKind.Ddl
                  && tab.ObjectKind is { } kind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                var obj = new MetadataObject(tab.ObjectName, kind);
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateDdl(this, obj, tab.DdlText ?? string.Empty, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.TableDetail
                  && tab.ObjectKind is { } detailKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Rebuild the detail VM with the live readers. Seed DdlText from the
                // cached value so the DDL sub-tab is non-empty before fetch returns.
                // NO eager LoadAsync here — multiple TableDetail tabs would race
                // against the single FbConnection and silently fail. Instead,
                // SelectTab triggers EnsureLoadedAsync on first activation, so the
                // restored-active tab loads automatically and inactive tabs load
                // lazily when the user clicks them.
                var obj = new MetadataObject(tab.ObjectName, detailKind);
                var detail = new TableDetailTabViewModel(obj.Name, _tableDetailReader, _ddlReader)
                {
                    DdlText = tab.DdlText ?? string.Empty,
                };
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateTableDetail(this, obj, detail, tab.ConnectionProfileId));
            }
        }

        // Defensive: connection workspace must always present a Query tab while the
        // connection is active — corrupted dict entries shouldn't strand the user
        // with a DDL-only tab strip.
        if (!queryTabAdded)
        {
            WorkspaceTabs.Insert(0, WorkspaceTabViewModel.CreateQuery(this));
        }

        var activeIndex = ws.ActiveTabIndex;
        if (activeIndex < 0 || activeIndex >= WorkspaceTabs.Count)
        {
            activeIndex = 0;
        }
        SelectTab(WorkspaceTabs[activeIndex]);

        LoadSavedQueriesFor(ws);
    }

    private void LoadSavedQueriesFor(ConnectionWorkspace ws)
    {
        _suppressSavedQuerySync = true;
        try
        {
            SavedQueries.Clear();
            foreach (var sq in ws.SavedQueries)
            {
                SavedQueries.Add(new SavedQueryViewModel(
                    string.IsNullOrEmpty(sq.Id) ? Guid.NewGuid().ToString("N") : sq.Id,
                    string.IsNullOrEmpty(sq.Name) ? string.Format(CultureInfo.InvariantCulture, UiStrings.QueryDefaultNameFormat, SavedQueries.Count + 1) : sq.Name,
                    sq.SqlText ?? string.Empty,
                    this));
            }

            // Bootstrap a "Query 1" on first Connect after this milestone (or for any
            // legacy workspace where SavedQueries has never been written). Seed it from
            // whatever's in the editor so the migration is lossless: the user keeps the
            // SQL they had before, now bound to a named saved query.
            if (SavedQueries.Count == 0)
            {
                SavedQueries.Add(new SavedQueryViewModel(
                    Guid.NewGuid().ToString("N"),
                    string.Format(CultureInfo.InvariantCulture, UiStrings.QueryDefaultNameFormat, 1),
                    QueryText,
                    this));
            }

            SavedQueryViewModel? active = null;
            if (!string.IsNullOrEmpty(ws.ActiveSavedQueryId))
            {
                active = SavedQueries.FirstOrDefault(q => q.Id == ws.ActiveSavedQueryId);
            }
            active ??= SavedQueries[0];
            SelectedSavedQuery = active;
            // Suppression covers the OnSelectedSavedQueryChanged echo too — caller is
            // responsible for syncing QueryText explicitly below.
            QueryText = active.SqlText;
        }
        finally
        {
            _suppressSavedQuerySync = false;
        }
    }

    private void ClearWorkspaceTabs()
    {
        WorkspaceTabs.Clear();
        SelectedWorkspaceTab = null;
        QueryText = string.Empty;
        _suppressSavedQuerySync = true;
        try
        {
            SelectedSavedQuery = null;
            SavedQueries.Clear();
        }
        finally
        {
            _suppressSavedQuerySync = false;
        }
    }

    private void ClearResultsAndMessages()
    {
        // Drop the result set itself first; bumping the version tag retriggers
        // PopulateResultGrid in the code-behind, which clears DataGrid columns
        // and ItemsSource when CurrentResult is null.
        CurrentResult = null;
        CurrentResultVersionTag = Guid.NewGuid().ToString("N");

        Messages.Clear();
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(ShowMessagesEmptyHint));

        QueryStatsText = string.Empty;
    }

    public async Task ConnectAsync(ConnectionProfile profile)
    {
        try
        {
            ClearError();
            await _service.ConnectAsync(profile).ConfigureAwait(true);
        }
        catch (ConnectionFailedException ex)
        {
            SetError(ex.Message);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_transactionService.IsActive)
        {
            var confirmed = await RequestConfirmAsync(new ConfirmRequest
            {
                Title = UiStrings.DisconnectConfirmTitle,
                Message = UiStrings.DisconnectConfirmMessage,
                ConfirmLabel = UiStrings.DisconnectConfirmYes,
                CancelLabel = UiStrings.DisconnectConfirmNo,
                IsDestructive = true,
            }).ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }
            await _transactionService.RollbackAsync().ConfigureAwait(true);
        }

        ClearError();
        await _service.DisconnectAsync().ConfigureAwait(true);
    }

    public void Delete(ConnectionProfile profile)
    {
        if (_service.ActiveProfile?.Id == profile.Id)
        {
            _ = DisconnectAsync();
        }
        _workspacesByConnection.Remove(profile.Id);
        _store.Delete(profile.Id);
        ReloadConnections();
    }

    public ConnectionProfile Copy(ConnectionProfile profile)
    {
        var clone = new ConnectionProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = profile.Name + " (Copy)",
            Host = profile.Host,
            Port = profile.Port,
            DatabasePath = profile.DatabasePath,
            Username = profile.Username,
            Password = profile.Password,
            Charset = profile.Charset,
            Dialect = profile.Dialect,
            ClientLibraryPath = profile.ClientLibraryPath,
        };
        _store.Upsert(clone);
        ReloadConnections();
        return clone;
    }

    public event System.Action<ConnectionProfile>? EditRequested;
    public void RequestEdit(ConnectionProfile profile) => EditRequested?.Invoke(profile);

    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;
    private Task<bool> RequestConfirmAsync(ConfirmRequest request)
        => ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);

    public event Func<string, Task>? ClipboardWriteRequested;

    // Asks the view to open the New Connection dialog. When folderId is non-null,
    // the resulting connection is slotted into that folder; otherwise it lands at
    // the root. Fired from FolderNodeViewModel.AddConnectionCommand for the folder
    // right-click "Dodaj połączenie" entry and from MainWindow.OnNewConnectionClick
    // (which resolves the folder context from the tree selection).
    public event Func<string?, Task>? AddConnectionRequested;
    public Task RequestAddConnectionAsync(string? folderId)
        => AddConnectionRequested?.Invoke(folderId) ?? Task.CompletedTask;

    // Persist a freshly-added connection into a folder. Called by the view after
    // the dialog returns with a profile; isolated here so tests can drive the
    // folder-placement logic without standing up the dialog.
    public void PlaceConnectionInFolder(string profileId, string? folderId)
    {
        if (string.IsNullOrEmpty(profileId)) return;
        if (string.IsNullOrEmpty(folderId))
        {
            _folderState.ConnectionFolderMap.Remove(profileId);
        }
        else
        {
            _folderState.ConnectionFolderMap[profileId] = folderId;
        }
        PersistFolderState();
        ReloadConnections();
    }

    // Set by the view to read the SQL editor's current selection. Returns null or empty
    // when there is no selection. Used to scope Execute and Format SQL to the selected
    // fragment when one exists; falls back to the full editor text otherwise.
    public Func<string?>? SelectedQueryTextProvider { get; set; }

    // Set by the view. If there's a non-empty selection in the SQL editor, the view
    // replaces just that range with the given text and re-selects it; otherwise it
    // overwrites the whole editor content. Used by the Format SQL command.
    public Action<string>? ReplaceSelectedOrAllText { get; set; }

    // Resolves the SQL text that an Execute/Format operation should act on: the active
    // selection when non-whitespace, otherwise the full editor content. Internal so the
    // tests can drive it without standing up the view.
    internal string ResolveActiveSql()
    {
        var selected = SelectedQueryTextProvider?.Invoke();
        return string.IsNullOrWhiteSpace(selected) ? QueryText : selected!;
    }

    // Column cache for ALIAS./TABLE. autocomplete. Keyed by uppercase table
    // name; cleared on disconnect (in ClearWorkspaceTabs path via
    // ApplyActiveConnectionChange). A separate "table doesn't exist or has no
    // columns" sentinel is unnecessary — the empty list is the right answer in
    // both cases, and a re-request only costs one tiny round-trip.
    private readonly Dictionary<string, IReadOnlyList<ColumnSpec>> _columnCache =
        new(StringComparer.OrdinalIgnoreCase);
    // Names of tables/views that the metadata categories have surfaced, so the
    // dot autocomplete knows which qualifier looks up real columns vs. is just
    // an unresolved alias. Refreshed each call from the metadata tree — cheap
    // for the tree sizes we expect.
    internal IReadOnlyCollection<string> EnumerateTableLikeNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in EnumerateLoadedObjects())
        {
            if (obj.Kind is MetadataObjectKind.Table
                          or MetadataObjectKind.View
                          or MetadataObjectKind.SystemTable)
            {
                names.Add(obj.Name);
            }
        }
        return names;
    }

    /// <summary>
    /// Pure resolution: given the editor text + caret, returns the table that
    /// should be queried for columns at the dot, or null when the qualifier
    /// can't be mapped. Aliases come from FROM/JOIN scanning of the editor text;
    /// table-name qualifiers (e.g. <c>NAGL.</c>) match against the loaded
    /// metadata category names. Doesn't touch the database — column data is
    /// fetched separately via <see cref="EnsureColumnsAsync"/>.
    /// </summary>
    internal string? ResolveDotTable(string text, int caretOffset)
    {
        var dot = SqlCompletionContext.GetDotContext(text, caretOffset);
        if (dot is null) return null;
        var tables = EnumerateTableLikeNames();
        return SqlAliasResolver.ResolveTableForQualifier(text, dot.Value.Qualifier, tables);
    }

    internal IReadOnlyList<ColumnSpec>? TryGetCachedColumns(string tableName)
        => _columnCache.TryGetValue(tableName, out var cols) ? cols : null;

    /// <summary>
    /// Loads (and caches) the columns for the given table from Firebird. Safe
    /// to call repeatedly — subsequent calls return the cached list. Returns
    /// an empty list when no connection is active or the table doesn't exist.
    /// </summary>
    internal async Task<IReadOnlyList<ColumnSpec>> EnsureColumnsAsync(string tableName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(tableName)) return Array.Empty<ColumnSpec>();
        if (_columnCache.TryGetValue(tableName, out var cached)) return cached;
        if (!_service.IsConnected) return Array.Empty<ColumnSpec>();

        try
        {
            var cols = await _metadataReader.ListColumnsAsync(tableName, ct).ConfigureAwait(true);
            _columnCache[tableName] = cols;
            return cols;
        }
        catch (MetadataReadException)
        {
            // Don't poison the cache on transient errors — let the next attempt retry.
            return Array.Empty<ColumnSpec>();
        }
    }

    /// <summary>
    /// Collects already-loaded schema-object leaves from the active connection's
    /// metadata tree. Returned in the order metadata categories were realized; the
    /// SQL editor's autocomplete sorts them on display. Empty when no connection
    /// is active or when nothing has been loaded yet — autocomplete will still
    /// surface keywords on its own.
    /// </summary>
    internal IReadOnlyList<MetadataObject> EnumerateLoadedObjects()
    {
        var list = new List<MetadataObject>();
        var activeId = _service.ActiveProfile?.Id;
        if (string.IsNullOrEmpty(activeId)) return list;

        foreach (var node in Metadata.Connections)
        {
            if (!node.IsConnected) continue;
            if (!string.Equals(node.Profile.Id, activeId, StringComparison.Ordinal)) continue;

            foreach (var group in node.Children)
            {
                if (!group.IsGroup) continue;
                foreach (var leaf in group.Children)
                {
                    if (leaf.IsActionable && leaf.Object is { } obj)
                    {
                        list.Add(obj);
                    }
                }
            }
        }
        return list;
    }

    /// <summary>
    /// Pure name-based lookup over a list of loaded metadata objects. Case-insensitive;
    /// first match wins, which mirrors <see cref="ConnectionNodeViewModel"/>'s category
    /// ordering (Tables > Views > Procedures > … > SystemTables) when the input comes
    /// from <see cref="EnumerateLoadedObjects"/>. So if a trigger and a table share a
    /// name, double-clicking the identifier opens the table — sensible default.
    /// </summary>
    internal static MetadataObject? ResolveByName(IEnumerable<MetadataObject> objects, string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var obj in objects)
        {
            if (string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return obj;
            }
        }
        return null;
    }

    /// <summary>
    /// Looks up <paramref name="name"/> against the active connection's loaded
    /// metadata. Returns null when there's no active connection, nothing's been
    /// loaded yet, or no object matches.
    /// </summary>
    internal MetadataObject? TryResolveLoadedObject(string? name)
        => ResolveByName(EnumerateLoadedObjects(), name);

    /// <summary>
    /// Double-click-in-editor entry point. If <paramref name="word"/> matches a
    /// loaded metadata object, opens (or focuses) its DDL tab — same code path as
    /// the metadata-tree double-click. Returns true when a match was found and the
    /// DDL open was kicked off; false when there's no match (caller leaves the
    /// editor's default word-select behaviour in place).
    /// </summary>
    public bool TryOpenDdlForWord(string? word)
    {
        var obj = TryResolveLoadedObject(word);
        if (obj is null) return false;
        OnOpenDdlRequested(obj);
        return true;
    }

    private async void OnOpenDdlRequested(MetadataObject obj)
    {
        // Focus an existing tab for the same object if one is already open —
        // double-clicking twice shouldn't pile up duplicates. Both Ddl and
        // TableDetail tabs key on (Kind, Name).
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind is WorkspaceTabKind.Ddl or WorkspaceTabKind.TableDetail
                && tab.ObjectKind == obj.Kind
                && string.Equals(tab.ObjectName, obj.Name, StringComparison.Ordinal))
            {
                SelectTab(tab);
                return;
            }
        }

        try
        {
            if (obj.Kind == MetadataObjectKind.Table)
            {
                var detail = new TableDetailTabViewModel(obj.Name, _tableDetailReader, _ddlReader);
                var newTab = WorkspaceTabViewModel.CreateTableDetail(this, obj, detail, _service.ActiveProfile?.Id);
                WorkspaceTabs.Add(newTab);
                // SelectTab kicks off EnsureLoadedAsync as a side-effect; we await
                // the same task here so we can surface any post-load error.
                SelectTab(newTab);
                await detail.EnsureLoadedAsync().ConfigureAwait(true);
                if (!string.IsNullOrEmpty(detail.ErrorMessage))
                {
                    AddMessage(MessageSeverity.Error, detail.ErrorMessage);
                    SelectedBottomTabIndex = 1;
                }
                return;
            }

            var ddl = await _ddlReader.FetchDdlAsync(obj).ConfigureAwait(true);
            var ddlTab = WorkspaceTabViewModel.CreateDdl(this, obj, ddl, _service.ActiveProfile?.Id);
            WorkspaceTabs.Add(ddlTab);
            SelectTab(ddlTab);
        }
        catch (MetadataReadException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
        }
        catch (InvalidOperationException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
        }
    }

    internal void SelectTab(WorkspaceTabViewModel tab)
    {
        if (SelectedWorkspaceTab == tab) return;
        foreach (var t in WorkspaceTabs) t.IsSelected = false;
        tab.IsSelected = true;
        SelectedWorkspaceTab = tab;

        // Lazy-load TableDetail content on first activation. Restored tabs and
        // background tabs stay empty until the user clicks them — eager loading
        // every restored TableDetail tab races against the single-statement
        // FbConnection and only the first wins. EnsureLoadedAsync is idempotent
        // and returns the running task, so this fire-and-forget can be awaited
        // by other code paths (e.g. OnOpenDdlRequested) for completion.
        if (tab is { Kind: WorkspaceTabKind.TableDetail, TableDetail: { } detail }
            && _service.IsConnected)
        {
            _ = detail.EnsureLoadedAsync();
        }
    }

    internal void CloseTab(WorkspaceTabViewModel tab)
    {
        if (!tab.IsClosable) return;
        var index = WorkspaceTabs.IndexOf(tab);
        if (index < 0) return;
        WorkspaceTabs.RemoveAt(index);
        if (SelectedWorkspaceTab == tab && WorkspaceTabs.Count > 0)
        {
            SelectTab(WorkspaceTabs[Math.Min(index, WorkspaceTabs.Count - 1)]);
        }
    }

    private void OnCopyNameRequested(string name)
    {
        if (ClipboardWriteRequested is { } write)
        {
            _ = write(name);
        }
        AddMessage(MessageSeverity.Info, string.Format(UiStrings.MetadataNameCopiedFormat, name));
    }

    private void OnMetadataStatusReported(string message)
    {
        AddMessage(MessageSeverity.Error, message);
        SelectedBottomTabIndex = 1;
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    public async Task ExecuteQueryAsync()
    {
        // If the user has highlighted a fragment in the editor, execute only that;
        // otherwise execute the whole editor content (legacy behaviour).
        var sql = ResolveActiveSql();
        if (IsExecuting || string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        if (!_service.IsConnected)
        {
            AddMessage(MessageSeverity.Error, UiStrings.NoConnectionMessage);
            SelectedBottomTabIndex = 1;
            return;
        }

        IsExecuting = true;
        QueryStatsText = UiStrings.ExecutingStatus;
        ClearError();
        _executionCts = new CancellationTokenSource();

        try
        {
            var result = await _executor.ExecuteAsync(sql, _executionCts.Token).ConfigureAwait(true);
            CurrentResult = result;
            CurrentResultVersionTag = Guid.NewGuid().ToString("N");

            var ms = (long)result.Elapsed.TotalMilliseconds;
            if (result.HasResultSet)
            {
                QueryStatsText = string.Format(UiStrings.RowsFetchedFormat, result.Rows.Count, ms);
                AddMessage(MessageSeverity.Info, QueryStatsText);
                if (result.Truncated)
                {
                    AddMessage(MessageSeverity.Warning, TruncatedBannerText);
                }
                SelectedBottomTabIndex = 0;
            }
            else
            {
                QueryStatsText = string.Format(UiStrings.AffectedRowsFormat, result.RecordsAffected ?? 0, ms);
                AddMessage(MessageSeverity.Info, QueryStatsText);
                SelectedBottomTabIndex = 1;
            }
        }
        catch (OperationCanceledException)
        {
            QueryStatsText = UiStrings.QueryCancelledMessage;
            AddMessage(MessageSeverity.Warning, UiStrings.QueryCancelledMessage);
            SelectedBottomTabIndex = 1;
        }
        catch (QueryExecutionException ex)
        {
            QueryStatsText = string.Empty;
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
        }
        finally
        {
            _executionCts?.Dispose();
            _executionCts = null;
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private void CancelQuery() => _executionCts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanFormatSql))]
    private void FormatSql()
    {
        if (!IsQueryTabActive) return;
        var selected = SelectedQueryTextProvider?.Invoke();
        var hasSelection = !string.IsNullOrEmpty(selected);
        var source = hasSelection ? selected! : QueryText;
        if (string.IsNullOrEmpty(source)) return;

        var formatted = SqlFormatter.Format(source);
        if (string.Equals(formatted, source, StringComparison.Ordinal)) return;

        if (ReplaceSelectedOrAllText is { } replace)
        {
            replace(formatted);
        }
        else if (!hasSelection)
        {
            // No view callback (tests / headless) — fall back to overwriting QueryText
            // so the formatter still acts on the full editor content. With an active
            // selection but no callback we can't replace a sub-range, so we no-op.
            QueryText = formatted;
        }
    }

    public bool CanFormatSql => IsQueryTabActive;

    [RelayCommand(CanExecute = nameof(CanClearActiveEditor))]
    private void ClearActiveEditor()
    {
        // Only meaningful for Query tabs — DDL tabs are read-only. CanExecute gate
        // ensures the button is greyed out on DDL.
        if (IsQueryTabActive)
        {
            QueryText = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCloseActiveTab))]
    private void CloseActiveTab()
    {
        if (SelectedWorkspaceTab is { IsClosable: true } tab)
        {
            CloseTab(tab);
        }
    }

    public string? BuildCopyText(CopyGridMode mode, int rowIndex, int columnIndex)
    {
        if (CurrentResult is not { HasResultSet: true } r) return null;
        var rows = r.Rows;
        var cols = r.Columns;
        if (cols.Count == 0) return null;

        switch (mode)
        {
            case CopyGridMode.Cell:
            {
                if (rowIndex < 0 || rowIndex >= rows.Count) return null;
                if (columnIndex < 0 || columnIndex >= cols.Count) return null;
                return FormatCell(rows[rowIndex][columnIndex]);
            }
            case CopyGridMode.Row:
            {
                if (rowIndex < 0 || rowIndex >= rows.Count) return null;
                return FormatRow(rows[rowIndex]);
            }
            case CopyGridMode.RowWithHeaders:
            {
                if (rowIndex < 0 || rowIndex >= rows.Count) return null;
                var headers = string.Join('\t', cols.Select(c => EscapeCell(c.Name)));
                return headers + Environment.NewLine + FormatRow(rows[rowIndex]);
            }
            case CopyGridMode.AllWithHeaders:
            {
                var headers = string.Join('\t', cols.Select(c => EscapeCell(c.Name)));
                var sb = new System.Text.StringBuilder();
                sb.Append(headers);
                foreach (var row in rows)
                {
                    sb.Append(Environment.NewLine);
                    sb.Append(FormatRow(row));
                }
                return sb.ToString();
            }
            default:
                return null;
        }
    }

    public async Task<bool> CopyGridAsync(CopyGridMode mode, int rowIndex, int columnIndex)
    {
        var text = BuildCopyText(mode, rowIndex, columnIndex);
        if (text is null) return false;
        if (ClipboardWriteRequested is { } write)
        {
            await write(text).ConfigureAwait(true);
        }
        var label = mode switch
        {
            CopyGridMode.Cell => UiStrings.GridCopiedCellLabel,
            CopyGridMode.Row => UiStrings.GridCopiedRowLabel,
            CopyGridMode.RowWithHeaders => UiStrings.GridCopiedRowLabel,
            CopyGridMode.AllWithHeaders => string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.GridCopiedRowsFormat,
                CurrentResult?.Rows.Count ?? 0),
            _ => string.Empty,
        };
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.GridCopiedToClipboardFormat, label));
        return true;
    }

    private static string FormatRow(object?[] row)
        => string.Join('\t', row.Select(FormatCell).Select(EscapeCell));

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        System.DBNull => string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    // TSV cells with embedded tab or newline would break the column alignment when
    // pasted into Excel/IBExpert. Match the IBExpert convention: replace them with
    // spaces. Quoting/escaping isn't standard for TSV consumers.
    private static string EscapeCell(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0) return value;
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    partial void OnQueryTextChanged(string value)
    {
        // Editor keystrokes flow back into the active SavedQuery so persistence
        // captures the latest text without an explicit Save action.
        if (_suppressSavedQuerySync) return;
        if (SelectedSavedQuery is { } sq && !ReferenceEquals(sq.SqlText, value))
        {
            sq.SqlText = value;
        }
    }

    partial void OnSelectedSavedQueryChanged(SavedQueryViewModel? value)
    {
        if (_suppressSavedQuerySync) return;
        if (value is null) return;
        _suppressSavedQuerySync = true;
        try
        {
            QueryText = value.SqlText;
        }
        finally
        {
            _suppressSavedQuerySync = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreateSavedQuery))]
    private void NewQuery()
    {
        var name = string.Format(CultureInfo.InvariantCulture, UiStrings.QueryDefaultNameFormat, NextQueryNumber());
        var sq = new SavedQueryViewModel(Guid.NewGuid().ToString("N"), name, string.Empty, this);
        SavedQueries.Add(sq);
        SelectedSavedQuery = sq;
    }

    public bool CanCreateSavedQuery => HasActiveWorkspace;

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedQuery))]
    private Task DeleteSelectedQueryAsync()
        => SelectedSavedQuery is { } sq ? DeleteSavedQueryAsync(sq) : Task.CompletedTask;

    public bool CanDeleteSelectedQuery => SelectedSavedQuery is not null;

    // Confirm + delete a specific saved query. Invoked by both the toolbar
    // "trash" button (via DeleteSelectedQueryAsync) and the per-row context
    // menu / hover button (via SavedQueryViewModel.DeleteCommand).
    public async Task DeleteSavedQueryAsync(SavedQueryViewModel sq)
    {
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.QueryDeleteConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.QueryDeleteConfirmFormat, sq.Name),
            ConfirmLabel = UiStrings.QueryDeleteConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        RemoveSavedQuery(sq);
    }

    [RelayCommand(CanExecute = nameof(CanClearAllQueries))]
    private async Task ClearAllQueriesAsync()
    {
        if (SavedQueries.Count == 0) return;
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.QueryClearAllConfirmTitle,
            Message = UiStrings.QueryClearAllConfirmMessage,
            ConfirmLabel = UiStrings.QueryClearAllConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;

        _suppressSavedQuerySync = true;
        try
        {
            SelectedSavedQuery = null;
            SavedQueries.Clear();
            QueryText = string.Empty;
        }
        finally
        {
            _suppressSavedQuerySync = false;
        }
        // Always leave the user with one usable Query 1 — an empty panel + empty
        // editor would strand them without a target for the next keystroke.
        var bootstrap = new SavedQueryViewModel(
            Guid.NewGuid().ToString("N"),
            string.Format(CultureInfo.InvariantCulture, UiStrings.QueryDefaultNameFormat, 1),
            string.Empty,
            this);
        SavedQueries.Add(bootstrap);
        SelectedSavedQuery = bootstrap;
    }

    public bool CanClearAllQueries => HasActiveWorkspace && SavedQueries.Count > 0;

    [RelayCommand]
    private void ToggleQueryPanel() => IsQueryPanelVisible = !IsQueryPanelVisible;

    private void RemoveSavedQuery(SavedQueryViewModel sq)
    {
        var index = SavedQueries.IndexOf(sq);
        if (index < 0) return;
        var wasSelected = ReferenceEquals(SelectedSavedQuery, sq);
        SavedQueries.RemoveAt(index);

        if (!wasSelected) return;

        if (SavedQueries.Count == 0)
        {
            // Re-bootstrap so the editor always has a target.
            var bootstrap = new SavedQueryViewModel(
                Guid.NewGuid().ToString("N"),
                string.Format(CultureInfo.InvariantCulture, UiStrings.QueryDefaultNameFormat, 1),
                string.Empty,
                this);
            SavedQueries.Add(bootstrap);
            SelectedSavedQuery = bootstrap;
        }
        else
        {
            SelectedSavedQuery = SavedQueries[Math.Min(index, SavedQueries.Count - 1)];
        }
    }

    private int NextQueryNumber()
    {
        var max = 0;
        foreach (var sq in SavedQueries)
        {
            // Parse trailing digits off "Query N" — anything renamed by the user is
            // skipped, so the next number is "max-existing-Query-N + 1".
            const string prefix = "Query ";
            if (sq.Name.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(sq.Name.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                && n > max)
            {
                max = n;
            }
        }
        return max + 1;
    }

    [RelayCommand]
    private async Task CommitAsync()
    {
        var count = _transactionService.StatementCount;
        try
        {
            await _transactionService.CommitAsync().ConfigureAwait(true);
            AddMessage(MessageSeverity.Info, string.Format(UiStrings.TransactionCommittedFormat, count));
        }
        catch (TransactionFailedException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
        }
    }

    [RelayCommand]
    private async Task RollbackAsync()
    {
        var count = _transactionService.StatementCount;
        await _transactionService.RollbackAsync().ConfigureAwait(true);
        AddMessage(MessageSeverity.Info, string.Format(UiStrings.TransactionRolledBackFormat, count));
    }

    private void AddMessage(MessageSeverity severity, string text)
    {
        Messages.Add(new QueryMessageViewModel(severity, text));
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(ShowMessagesEmptyHint));
    }

    private void OnActiveConnectionChanged(object? sender, EventArgs e)
    {
        // The service fires this event on whichever thread its async work completed
        // on. Mutating WorkspaceTabs (ObservableCollection) requires the UI thread,
        // so marshal everything.
        var newId = _service.ActiveProfile?.Id;
        Dispatcher.UIThread.Post(() => ApplyActiveConnectionChange(newId));
    }

    // Internal so tests can drive the connection-switch flow without a live FbConnection.
    internal void ApplyActiveConnectionChange(string? newProfileId)
    {
        // Stash the soon-to-be-hidden tabs into the dict under the previous profile
        // id so a future reconnect can restore them verbatim.
        if (ActiveWorkspaceProfileId is not null && WorkspaceTabs.Count > 0)
        {
            _workspacesByConnection[ActiveWorkspaceProfileId] = SnapshotCurrentTabs();
        }

        ClearWorkspaceTabs();
        ActiveWorkspaceProfileId = newProfileId;

        if (newProfileId is null)
        {
            // Results, messages, and query stats all belong to the connection
            // that produced them — disconnecting must drop them too so the next
            // connect doesn't surface stale rows or success/error toasts.
            ClearResultsAndMessages();
        }
        else
        {
            LoadWorkspaceFor(newProfileId);
        }

        // Column cache belongs to the previous schema — drop it on any switch
        // so that "X.column" against a same-named table in another DB doesn't
        // surface stale columns.
        _columnCache.Clear();

        UpdateStatusFromConnection();
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ActiveConnectionName));
        OnPropertyChanged(nameof(HasActiveConnection));
    }

    internal IReadOnlyDictionary<string, ConnectionWorkspace> WorkspacesByConnection
        => _workspacesByConnection;

    private void OnTransactionStateChanged(object? sender, EventArgs e)
    {
        var current = _transactionService.State;
        if (_previousTransactionState != TransactionState.Active && current == TransactionState.Active)
        {
            AddMessage(MessageSeverity.Info, UiStrings.TransactionStartedMessage);
        }
        _previousTransactionState = current;

        OnPropertyChanged(nameof(IsTransactionIdle));
        OnPropertyChanged(nameof(IsTransactionActive));
        OnPropertyChanged(nameof(IsTransactionError));
        OnPropertyChanged(nameof(HasExecutedInTransaction));
        OnPropertyChanged(nameof(TransactionBarText));

        // The Query tab carries the transaction-active marker. It only exists while
        // a connection is active; when none is, there's no marker to set.
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind == WorkspaceTabKind.Query)
            {
                tab.ShowActiveTransactionMarker = HasExecutedInTransaction;
                break;
            }
        }
    }

    private void UpdateStatusFromConnection()
    {
        var active = _service.ActiveProfile;
        StatusText = active is null
            ? UiStrings.StatusBarDisconnected
            : $"{UiStrings.StatusBarConnectedTo} {active.Name}";
        IsStatusError = false;
    }

    private void SetError(string message)
    {
        StatusText = message;
        IsStatusError = true;
    }

    private void ClearError()
    {
        if (IsStatusError)
        {
            UpdateStatusFromConnection();
        }
    }
}
