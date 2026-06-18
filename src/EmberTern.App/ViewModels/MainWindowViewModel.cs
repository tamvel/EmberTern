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
using EmberTern.App.Security;
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
    // Data lane (connection #1): SQL Editor F5, data preview/edit.
    private readonly TransactionService _transactionService;
    // Metadata lane (connection #2): DDL from the structure editor, Shift+F5, metadata
    // browsing. Falls back to the data lane when the second attachment is unavailable.
    private readonly TransactionService _metadataTransactionService;
    // F5 auto-routes to one of these by statement kind (SqlStatementClassifier):
    // data DML/reads → _executor (data lane); DDL/DCL → _metadataExecutor (metadata
    // lane). There is no manual lane override (Shift+F5 was removed).
    private readonly FirebirdQueryExecutor _executor;          // data lane
    private readonly FirebirdQueryExecutor _metadataExecutor;  // metadata lane
    private readonly FirebirdMetadataReader _metadataReader;
    private readonly FirebirdDdlReader _ddlReader;
    private readonly FirebirdTableDetailReader _tableDetailReader;
    private readonly FirebirdDataEditor _dataEditor;
    private readonly FirebirdDdlExecutor _ddlExecutor;
    private CancellationTokenSource? _executionCts;
    private TransactionState _previousTransactionState = TransactionState.Idle;
    private TransactionState _previousMetadataTransactionState = TransactionState.Idle;
    // Set just before a Commit/Rollback settles a lane, read by OnTransactionStateChanged
    // to decide whether the post-settle refresh runs (commit → no refresh, rollback →
    // refresh to revert). See DecidePostTransactionRefresh for why a commit must NOT refresh.
    private bool _lastTransactionSettleWasRollback;
    // Most-recently-activated-last ordering of open tabs. Drives "return to the tab
    // I came from" when the active tab is closed (e.g. open a table → jump to a
    // procedure from its dependencies → close the procedure → land back on the
    // table, not a random index-neighbour). Pruned on close, cleared on bulk reset.
    private readonly List<WorkspaceTabViewModel> _tabActivationHistory = new();
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

    // Set during ReloadConnections while we apply saved expand state to freshly
    // built nodes. Without this, each IsExpanded write would echo through
    // OnNodeExpansionChanged → PersistFolderState — N saves per reload, all
    // redundant with the value we just read out of the set.
    private bool _suppressExpandSave;

    public MainWindowViewModel()
        : this(new ConnectionProfileStore(DpapiSecretProtector.Create()), new FirebirdConnectionService())
    {
    }

    public MainWindowViewModel(ConnectionProfileStore store, FirebirdConnectionService service)
        : this(store, service, new TransactionService(service))
    {
    }

    public MainWindowViewModel(ConnectionProfileStore store, FirebirdConnectionService service, TransactionService transactionService)
        // Thread the connection store's protector into FolderStore so every facade over
        // the shared settings.dat encrypts consistently — an Identity-protector save here
        // would write the unified file unencrypted and clobber the DPAPI-protected one.
        : this(store, service, transactionService, new FolderStore(System.IO.Path.GetDirectoryName(store.FilePath)!, store.Protector))
    {
    }

    public MainWindowViewModel(ConnectionProfileStore store, FirebirdConnectionService service, TransactionService transactionService, FolderStore folderStore)
    {
        _store = store;
        _folderStore = folderStore;
        _folderState = _folderStore.Load();
        _service = service;
        _transactionService = transactionService;
        // Metadata working tx is ALWAYS the safe NOWAIT default (TPB profiles are no
        // longer user-configurable — Developer Mode's WAIT applies only to the DDL
        // executor path, not this working tx). Degrades to the data lane when the
        // second attachment is unavailable (fallback) so metadata work still functions.
        _metadataTransactionService = new TransactionService(
            _service,
            ConnectionRole.Metadata,
            _ => Core.Connections.TransactionProfile.ReadCommitted,
            fallback: _transactionService);
        _executor = new FirebirdQueryExecutor(_service, _transactionService);
        _metadataExecutor = new FirebirdQueryExecutor(_service, _metadataTransactionService);
        // Browsing (metadata reader, DDL reader, TableDetail structure reads) runs on
        // the metadata lane so it doesn't pin objects in the data working tx. The
        // TableDetail reader splits per method: structure → metadata, data preview → data.
        _metadataReader = new FirebirdMetadataReader(_service, _metadataTransactionService);
        _ddlReader = new FirebirdDdlReader(_service, _metadataTransactionService);
        _tableDetailReader = new FirebirdTableDetailReader(_service, _metadataTransactionService, _transactionService);
        _dataEditor = new FirebirdDataEditor(_service, _transactionService);
        // Krok 1: DDL/Compile executes on the MAIN (data) connection — the same
        // attachment Execute Procedure / F5 use — so a Compile of a just-executed
        // object no longer hits "object is in use" (cross-attachment self-block). It
        // auto-commits with an explicit NOWAIT TPB; the DATA TransactionService is
        // passed so the executor can require the data working tx to be settled first
        // (gotcha #89: one FbConnection, one transaction at a time).
        _ddlExecutor = new FirebirdDdlExecutor(_service, _transactionService);
        Metadata = new MetadataExplorerViewModel(_service, _metadataReader);
        Metadata.OpenDdlRequested += OnOpenDdlRequested;
        Metadata.CopyNameRequested += OnCopyNameRequested;
        Metadata.StatusReported += OnMetadataStatusReported;
        Metadata.NewTableRequested += OnNewTableRequestedFromTree;
        Metadata.DeleteTableRequested += OnDeleteTableRequested;
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
        _service.ActiveProfileUpdated += OnActiveProfileUpdated;
        _transactionService.TransactionStateChanged += OnTransactionStateChanged;
        _metadataTransactionService.TransactionStateChanged += OnTransactionStateChanged;
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
    [NotifyPropertyChangedFor(nameof(IsDataTabActive))]
    [NotifyPropertyChangedFor(nameof(IsFieldsTabActive))]
    [NotifyPropertyChangedFor(nameof(ShowFieldEditTools))]
    [NotifyPropertyChangedFor(nameof(ShowStructureCompileTools))]
    [NotifyPropertyChangedFor(nameof(ShowDataEditTools))]
    [NotifyPropertyChangedFor(nameof(IsNewTableTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveNewTable))]
    [NotifyPropertyChangedFor(nameof(IsViewDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveViewDetail))]
    [NotifyPropertyChangedFor(nameof(IsProcedureDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveProcedureDetail))]
    [NotifyPropertyChangedFor(nameof(IsClosableTabActive))]
    [NotifyPropertyChangedFor(nameof(ShowTransactionButtons))]
    [NotifyPropertyChangedFor(nameof(ShowDataTransactionButtons))]
    [NotifyPropertyChangedFor(nameof(ShowMetadataTransactionButtons))]
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
    [NotifyCanExecuteChangedFor(nameof(RefreshDataPreviewCommand))]
    private WorkspaceTabViewModel? _selectedWorkspaceTab;

    public bool IsQueryTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Query };
    public bool IsDdlTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Ddl };
    public bool IsTableDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TableDetail };
    /// <summary>True when the active workspace tab is a New Table tab.</summary>
    public bool IsNewTableTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.NewTable };
    public NewTableTabViewModel? ActiveNewTable
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.NewTable } t ? t.NewTable : null;
    /// <summary>True when the active workspace tab is a View Detail tab.</summary>
    public bool IsViewDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.ViewDetail };
    public ViewDetailTabViewModel? ActiveViewDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.ViewDetail } t ? t.ViewDetail : null;
    /// <summary>True when the active workspace tab is a Procedure Detail tab.</summary>
    public bool IsProcedureDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.ProcedureDetail };
    public ProcedureDetailTabViewModel? ActiveProcedureDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.ProcedureDetail } t ? t.ProcedureDetail : null;
    // True when the Dane sub-tab is the active sub-tab inside an active TableDetail
    // tab. Drives the Refresh button visibility and joins IsQueryTabActive to
    // share Commit/Rollback. Updates flow from TableDetailTabViewModel.PropertyChanged
    // — see HookTableDetailEvents.
    public bool IsDataTabActive
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TableDetail, TableDetail: { IsDataSubTabActive: true } };
    // True when the Pola sub-tab is active inside a TableDetail tab. Drives the
    // main toolbar's ⚡ ＋ − ↑ ↓ structural-edit buttons.
    public bool IsFieldsTabActive
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TableDetail, TableDetail: { IsFieldsSubTabActive: true } };
    // Structural-edit affordances (⚡ Compile / edit-toggle / ＋ Add / − Drop / ↑↓ Move
    // field) are shown only when the Pola sub-tab is active AND the tab can actually
    // edit structure. "Can edit structure" is the existing capability gate
    // (TableDetailTabViewModel.CanAddField => a DDL executor was wired) — the single
    // source of truth, no IsReadOnly flag. System-table TableDetail tabs are built
    // without the executor (see CreateTableDetail), so these stay HIDDEN — a read-only
    // object category, not a normal table with greyed-out controls.
    public bool ShowFieldEditTools => IsFieldsTabActive && (ActiveTableDetail?.CanAddField ?? false);
    // ⚡ Compile + Discard apply/abandon the buffered structural changes. Unlike the
    // Pola-specific Add/Drop/Move cluster (ShowFieldEditTools), these are valid on
    // EVERY TableDetail sub-tab (a constraint queued on Ograniczenia or an index on
    // Indeksy must still be Compilable), so they gate on the whole tab + the edit
    // capability — not on the Pola sub-tab being active.
    public bool ShowStructureCompileTools => IsTableDetailTabActive && (ActiveTableDetail?.CanAddField ?? false);
    // Data-edit affordances (＋ Add row / − Delete row). Shown only when the Dane
    // sub-tab is active AND a data editor was wired (CanEditData). Refresh +
    // pagination stay on IsDataTabActive — they're read paths and remain visible for
    // read-only tables.
    public bool ShowDataEditTools => IsDataTabActive && (ActiveTableDetail?.CanEditData ?? false);
    // Commit / Rollback are shown on any sub-tab where structural or data changes
    // can be committed: the Query tab (F5 statements), the Dane sub-tab (inline
    // INSERT/UPDATE/DELETE), and the Pola sub-tab (Add Field / Drop Field run
    // immediately in the user's working tx — the user needs the same Commit /
    // Rollback escape hatch).
    // Commit / Rollback must be reachable from EVERY TableDetail sub-tab, not
    // just Pola / Dane: Add/Drop Index (Indeksy), Save/Clear description (Opis)
    // and Add/Drop constraint (Ograniczenia) all open the working transaction,
    // so the user needs the Commit/Rollback buttons there to finalize (#3).
    // IsTableDetailTabActive covers all sub-tabs (it's keyed on the workspace
    // tab kind, not the inner sub-tab).
    // ViewDetail joins this set: Compile opens the working (metadata) transaction,
    // so Commit/Rollback must be reachable from a View Detail tab too.
    // ProcedureDetail joins this set: Compile opens the working (metadata)
    // transaction, so Commit/Rollback must be reachable from a Procedure Detail tab.
    public bool ShowTransactionButtons => IsQueryTabActive || IsTableDetailTabActive || IsViewDetailTabActive || IsProcedureDetailTabActive;
    // Close-tab toolbar button targets *other* tabs (DDL, TableDetail, NewTable, ViewDetail, ProcedureDetail);
    // the anchored Query tab is never closable so the button hides when it's active.
    public bool IsClosableTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Ddl or WorkspaceTabKind.TableDetail or WorkspaceTabKind.NewTable or WorkspaceTabKind.ViewDetail or WorkspaceTabKind.ProcedureDetail };
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

    // ── Results grid: client-side paging + 3-state sort ───────────────────
    //
    // Reuses the page-state SHAPE of TableDetailTabViewModel's pagination
    // (1-based page / First-Prev-Next-Last + Has*Page + hint), but the SQL
    // editor's result set is already materialized (capped at 5000 rows by the
    // executor), so paging + sorting run in-memory over CurrentResult.Rows —
    // no re-query. Sorting reuses the shared RowIndexComparer (object?[] by
    // column index). 3-state cycle: asc → desc → none (original order).

    // Same default page size as the Table Data View (DataPreviewRowLimit).
    public const int ResultPageSize = 200;

    private List<object?[]> _sortedRows = new();
    private int? _resultSortColumn;       // null = no sort (original row order)
    private bool _resultSortDescending;
    private int _resultPage = 1;          // 1-based

    // Bumped on every paging / sort change so the code-behind re-slices the
    // grid's ItemsSource (and repaints sort arrows) without a full column
    // rebuild — CurrentResultVersionTag stays reserved for structure changes.
    [ObservableProperty]
    private string _resultPageVersionTag = string.Empty;

    // The slice of (sorted) rows for the current page. The grid binds here.
    public IReadOnlyList<object?[]> PagedResultRows { get; private set; } = Array.Empty<object?[]>();

    // -1 = unsorted. Read by the code-behind to paint the ▲/▼ header arrow.
    public int ResultSortColumnIndex => _resultSortColumn ?? -1;
    public bool ResultSortDescending => _resultSortDescending;

    public int ResultPage => _resultPage;
    private int TotalResultPages =>
        _sortedRows.Count == 0 ? 1 : (_sortedRows.Count + ResultPageSize - 1) / ResultPageSize;

    public bool HasResultPreviousPage => _resultPage > 1;
    public bool HasResultNextPage => _resultPage < TotalResultPages;

    public string ResultPaginationHint => HasCurrentResult
        ? string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.ResultsPaginationHintFormat,
            _resultPage,
            TotalResultPages,
            _sortedRows.Count)
        : string.Empty;

    // New result set → drop sort + return to page 1, then recompute the view.
    partial void OnCurrentResultChanged(QueryResult? value)
    {
        _resultSortColumn = null;
        _resultSortDescending = false;
        _resultPage = 1;
        RebuildResultView();
    }

    // Re-sort (if a sort column is set) + clamp the page + slice the current
    // page out. Bumps ResultPageVersionTag so the grid re-binds. Pure VM logic;
    // unit-tested.
    internal void RebuildResultView()
    {
        var rows = CurrentResult?.Rows;
        if (rows is null || rows.Count == 0)
        {
            _sortedRows = new List<object?[]>();
            _resultPage = 1;
            PagedResultRows = Array.Empty<object?[]>();
        }
        else
        {
            var list = new List<object?[]>(rows);
            if (_resultSortColumn is int col)
            {
                var comparer = new RowIndexComparer(col);
                list.Sort((a, b) => _resultSortDescending ? comparer.Compare(b, a) : comparer.Compare(a, b));
            }
            _sortedRows = list;

            if (_resultPage > TotalResultPages) _resultPage = TotalResultPages;
            if (_resultPage < 1) _resultPage = 1;

            int start = (_resultPage - 1) * ResultPageSize;
            int count = Math.Min(ResultPageSize, list.Count - start);
            PagedResultRows = count > 0 ? list.GetRange(start, count) : Array.Empty<object?[]>();
        }

        OnPropertyChanged(nameof(PagedResultRows));
        OnPropertyChanged(nameof(HasResultPreviousPage));
        OnPropertyChanged(nameof(HasResultNextPage));
        OnPropertyChanged(nameof(ResultPage));
        OnPropertyChanged(nameof(ResultPaginationHint));
        ResultFirstPageCommand.NotifyCanExecuteChanged();
        ResultPreviousPageCommand.NotifyCanExecuteChanged();
        ResultNextPageCommand.NotifyCanExecuteChanged();
        ResultLastPageCommand.NotifyCanExecuteChanged();
        ResultPageVersionTag = Guid.NewGuid().ToString("N");
    }

    // Header click: 3-state cycle on the given column index.
    //   unsorted → ascending → descending → unsorted.
    // Clicking a different column starts fresh at ascending. Returns to page 1
    // because the row that was at the top changes.
    internal void CycleResultSort(int columnIndex)
    {
        if (CurrentResult is not { HasResultSet: true }) return;
        if (columnIndex < 0) return;

        if (_resultSortColumn != columnIndex)
        {
            _resultSortColumn = columnIndex;
            _resultSortDescending = false;
        }
        else if (!_resultSortDescending)
        {
            _resultSortDescending = true;
        }
        else
        {
            _resultSortColumn = null;
            _resultSortDescending = false;
        }

        _resultPage = 1;
        OnPropertyChanged(nameof(ResultSortColumnIndex));
        OnPropertyChanged(nameof(ResultSortDescending));
        RebuildResultView();
    }

    [RelayCommand(CanExecute = nameof(HasResultPreviousPage))]
    private void ResultFirstPage()
    {
        _resultPage = 1;
        RebuildResultView();
    }

    [RelayCommand(CanExecute = nameof(HasResultPreviousPage))]
    private void ResultPreviousPage()
    {
        if (_resultPage <= 1) return;
        _resultPage--;
        RebuildResultView();
    }

    [RelayCommand(CanExecute = nameof(HasResultNextPage))]
    private void ResultNextPage()
    {
        if (!HasResultNextPage) return;
        _resultPage++;
        RebuildResultView();
    }

    [RelayCommand(CanExecute = nameof(HasResultNextPage))]
    private void ResultLastPage()
    {
        _resultPage = TotalResultPages;
        RebuildResultView();
    }

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

    // Title-bar transaction-profile block: two stacked lines, each a static lane label
    // ("Data:" / "Meta:") plus the full profile name in a lane-colored badge. These
    // expose the badge text (profile name only); the label prefix is static in XAML.
    public string DataProfileName => TransactionProfileCatalog.LabelFor(DataProfile);

    public string MetadataProfileName => TransactionProfileCatalog.LabelFor(MetadataProfile);

    public string DataTransactionProfileTooltip
        => string.Format(UiStrings.TransactionProfileDataChipTooltipFormat, TransactionProfileCatalog.LabelFor(DataProfile));

    public string MetadataTransactionProfileTooltip
        => string.Format(UiStrings.TransactionProfileMetadataChipTooltipFormat, TransactionProfileCatalog.LabelFor(MetadataProfile));

    private Core.Connections.TransactionProfile DataProfile
        => _service.ActiveProfile?.DataTransactionProfile ?? Core.Connections.TransactionProfile.ReadCommitted;

    private Core.Connections.TransactionProfile MetadataProfile
        => _service.ActiveProfile?.MetadataTransactionProfile ?? Core.Connections.TransactionProfile.ReadCommitted;

    // Title-bar "DEV MODE" badge: shown only when the active connection has Developer
    // Mode on (DDL waits for in-use objects instead of failing fast).
    public bool IsDeveloperModeActive => _service.ActiveProfile?.DeveloperMode == true;

    public bool HasCurrentResult => CurrentResult is { HasResultSet: true };
    public bool ShowResultsEmptyHint => !HasCurrentResult;
    public bool ShowTruncatedBanner => CurrentResult is { Truncated: true };
    public string TruncatedBannerText => CurrentResult is { } r
        ? string.Format(UiStrings.ResultsTruncatedFormat, r.Rows.Count)
        : string.Empty;

    public bool HasMessages => Messages.Count > 0;
    public bool ShowMessagesEmptyHint => !HasMessages;

    public bool CanExecute => !IsExecuting;

    // --- Data lane transaction state (SQL Editor F5, data preview/edit) ---
    public bool IsTransactionIdle => _transactionService.IsIdle;
    public bool IsTransactionActive => _transactionService.IsActive;
    public bool IsTransactionError => _transactionService.IsError;
    public bool HasExecutedInTransaction => _transactionService.HasExecutedStatements;
    public string TransactionBarText => BuildTransactionBarText(UiStrings.TransactionDataBarPrefix, _transactionService);

    // --- Metadata lane transaction state (DDL, Shift+F5) ---
    public bool IsMetadataTransactionIdle => _metadataTransactionService.IsIdle;
    public bool IsMetadataTransactionActive => _metadataTransactionService.IsActive;
    public bool IsMetadataTransactionError => _metadataTransactionService.IsError;
    public bool HasExecutedInMetadataTransaction => _metadataTransactionService.HasExecutedStatements;
    public string MetadataTransactionBarText => BuildTransactionBarText(UiStrings.TransactionMetadataBarPrefix, _metadataTransactionService);

    // The metadata lane is only shown as a separate group when it has its own
    // attachment; in degraded mode it aliases the data lane and showing both would
    // duplicate the same transaction.
    public bool MetadataLaneIndependent => _service.MetadataIsIndependent;

    // Commit/Rollback button groups appear where their lane is reachable OR active:
    // Data on the Query / Dane surfaces; Metadata on the structure surfaces (and on the
    // Query tab once a Shift+F5 metadata tx is open).
    public bool ShowDataTransactionButtons => IsQueryTabActive || IsDataTabActive || IsTransactionActive || IsTransactionError;
    public bool ShowMetadataTransactionButtons
        => MetadataLaneIndependent
           && (IsTableDetailTabActive || IsNewTableTabActive || IsQueryTabActive
               || IsMetadataTransactionActive || IsMetadataTransactionError);

    private static string BuildTransactionBarText(string lanePrefix, TransactionService tx) => tx.State switch
    {
        TransactionState.Active when tx.HasExecutedStatements
            => $"{lanePrefix}: {UiStrings.TransactionBarActive} · {string.Format(UiStrings.TransactionStatementCountFormat, tx.StatementCount)}",
        TransactionState.Active => $"{lanePrefix}: {UiStrings.TransactionBarActive}",
        TransactionState.Error => $"{lanePrefix}: {UiStrings.TransactionBarError}",
        _ => $"{lanePrefix}: {UiStrings.TransactionBarInactive}",
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

        // Restore user-chosen expand state to the freshly rebuilt nodes. Runs after
        // RootNodes is fully populated (so all VM instances exist) and under the
        // suppress flag (so each IsExpanded write doesn't echo back as a save).
        RestoreExpandState();
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

    // Called by Folder/Connection node VMs when their IsExpanded flips. Mirrors the
    // change into _folderState.ExpandedNodeIds and saves. The suppress flag is set
    // by ReloadConnections while it pushes the persisted state into the new node
    // instances — that path mutates IsExpanded but is not a user action, so the
    // save must not fire (the on-disk set is already authoritative).
    public void OnNodeExpansionChanged(string nodeId, bool expanded)
    {
        if (_suppressExpandSave) return;
        if (string.IsNullOrEmpty(nodeId)) return;
        var changed = expanded
            ? _folderState.ExpandedNodeIds.Add(nodeId)
            : _folderState.ExpandedNodeIds.Remove(nodeId);
        if (changed) PersistFolderState();
    }

    // Sync the persisted set with the live tree. Exposed for tests; the on-change
    // hook keeps the two in sync during normal use, but this is handy for forcing
    // a refresh before ReloadConnections rebuilds the VMs.
    public void CaptureExpandState()
    {
        foreach (var node in Metadata.RootNodes)
        {
            switch (node)
            {
                case FolderNodeViewModel f:
                    Apply(f.Id, f.IsExpanded);
                    foreach (var c in f.Connections) Apply(c.Profile.Id, c.IsExpanded);
                    break;
                case ConnectionNodeViewModel c:
                    Apply(c.Profile.Id, c.IsExpanded);
                    break;
            }
        }

        void Apply(string id, bool expanded)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (expanded) _folderState.ExpandedNodeIds.Add(id);
            else _folderState.ExpandedNodeIds.Remove(id);
        }
    }

    // Push the persisted expand set onto the freshly rebuilt RootNodes —
    // verbatim: presence in set => IsExpanded=true; absence => false. This
    // assumes the set has been "initialized" (every node the user wants
    // expanded is explicitly present); MaybeMigrateExpandState handles the
    // one-time seeding of legacy data so folders don't all collapse on
    // first launch after the feature ships. Wrapped in _suppressExpandSave
    // so the IsExpanded writes don't echo back as re-saves.
    public void RestoreExpandState()
    {
        MaybeMigrateExpandState();

        _suppressExpandSave = true;
        try
        {
            foreach (var node in Metadata.RootNodes)
            {
                switch (node)
                {
                    case FolderNodeViewModel f:
                        ApplyFolder(f);
                        foreach (var c in f.Connections) ApplyConnection(c);
                        break;
                    case ConnectionNodeViewModel c:
                        ApplyConnection(c);
                        break;
                }
            }
        }
        finally
        {
            _suppressExpandSave = false;
        }

        // Folders default to IsExpanded=true, so the only way to persist a *collapse*
        // is to force false when the id is absent — hence verbatim (presence => true,
        // absence => false).
        void ApplyFolder(FolderNodeViewModel f)
        {
            var wanted = _folderState.ExpandedNodeIds.Contains(f.Id);
            if (f.IsExpanded != wanted) f.IsExpanded = wanted;
        }

        // Connections default to IsExpanded=false, so absence already means collapsed —
        // we only ever need to force *true*, never false. Critically, NOT forcing false
        // means a freshly-connected node (which auto-expands on connect and may not yet
        // be in the set when a concurrent ReloadConnections runs) never gets clobbered
        // back to collapsed by a restore pass.
        void ApplyConnection(ConnectionNodeViewModel c)
        {
            if (_folderState.ExpandedNodeIds.Contains(c.Profile.Id) && !c.IsExpanded)
            {
                c.IsExpanded = true;
            }
        }
    }

    // One-time migration when loading a pre-feature folders.json: folders were
    // default-expanded with no persistence, so we seed the set with every folder
    // id we know about. After this runs, the set is fully authoritative —
    // subsequent runs honor explicit user collapses (which remove from the set).
    private void MaybeMigrateExpandState()
    {
        if (_folderState.ExpandStateInitialized) return;
        var added = false;
        foreach (var f in _folderState.Folders)
        {
            if (_folderState.ExpandedNodeIds.Add(f.Id)) added = true;
        }
        _folderState.ExpandStateInitialized = true;
        if (added || _folderState.Folders.Count == 0)
        {
            // Always persist the flag flip even for the no-folders case so the
            // migration doesn't re-run on every launch (cheap I/O, but principled).
            PersistFolderState();
        }
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
        // New folders default to expanded (FolderNodeViewModel._isExpanded = true).
        // Seed the set so that default survives across restarts — without this,
        // RestoreExpandState would treat the absence as "collapsed" and the user's
        // freshly-created folder would slam shut on the next ReloadConnections.
        _folderState.ExpandedNodeIds.Add(entry.Id);
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

    // Drag-and-drop drop handler. Source and target are sidebar VMs
    // (ConnectionNodeViewModel or FolderNodeViewModel). Same persistence path
    // as SortSiblingsOf — mutates _folderState, calls PersistFolderState +
    // ReloadConnections.
    //
    // Valid combinations:
    //   Connection → Folder, Into            : move connection into folder (membership change).
    //   Connection → Connection, Before/After: reorder within target's container (may also change folder).
    //   Folder     → Folder, Before/After    : reorder folders at root.
    //   Folder     → Connection, Before/After: only when target is a root connection; folder goes to root.
    //
    // Anything else (e.g., dropping a folder Into a connection, source == target,
    // dropping a connection into the folder it's already in) is a no-op.
    public void ExecuteDrop(object dragSource, object dropTarget, DropPosition position)
    {
        if (ReferenceEquals(dragSource, dropTarget)) return;

        // Drop a connection INTO a folder: membership change + place at end of folder.
        if (dragSource is ConnectionNodeViewModel sIntoConn
            && dropTarget is FolderNodeViewModel folderInto
            && position == DropPosition.Into)
        {
            if (_folderState.ConnectionFolderMap.TryGetValue(sIntoConn.Profile.Id, out var curFolderId)
                && curFolderId == folderInto.Id)
            {
                // Already in this folder — no-op.
                return;
            }

            _folderState.ConnectionFolderMap[sIntoConn.Profile.Id] = folderInto.Id;
            // Place at the end of the folder's current members.
            var maxOrder = -1;
            foreach (var c in Metadata.Connections)
            {
                if (c.Profile.Id == sIntoConn.Profile.Id) continue;
                if (_folderState.ConnectionFolderMap.TryGetValue(c.Profile.Id, out var fid)
                    && fid == folderInto.Id)
                {
                    var order = GetConnectionSortOrder(c.Profile.Id);
                    if (order > maxOrder) maxOrder = order;
                }
            }
            _folderState.ConnectionSortOrders[sIntoConn.Profile.Id] = maxOrder + 1;
            PersistFolderState();
            ReloadConnections();
            return;
        }

        // Before/After reorder paths.
        if (position is DropPosition.Before or DropPosition.After)
        {
            // The target's container determines where the source lands:
            //   target = folder            → root (folders only live at root)
            //   target = connection        → that connection's folder (or root if unmapped)
            string? targetContainer = null;
            if (dropTarget is ConnectionNodeViewModel tConn
                && _folderState.ConnectionFolderMap.TryGetValue(tConn.Profile.Id, out var tFid)
                && !string.IsNullOrEmpty(tFid))
            {
                targetContainer = tFid;
            }

            // Folders can only live at root — refuse to drop a folder into a folder-member context.
            if (dragSource is FolderNodeViewModel && targetContainer is not null) return;

            ReorderForDrop(dragSource, dropTarget, position, targetContainer);
        }
    }

    private void ReorderForDrop(object dragSource, object dropTarget, DropPosition position, string? containerFolderId)
    {
        var sourceIsFolder = dragSource is FolderNodeViewModel;
        var targetIsFolder = dropTarget is FolderNodeViewModel;
        var sourceId = dragSource switch
        {
            ConnectionNodeViewModel c => c.Profile.Id,
            FolderNodeViewModel f => f.Entry.Id,
            _ => string.Empty,
        };
        var targetId = dropTarget switch
        {
            ConnectionNodeViewModel c => c.Profile.Id,
            FolderNodeViewModel f => f.Entry.Id,
            _ => string.Empty,
        };
        if (sourceId.Length == 0 || targetId.Length == 0) return;

        // Connection moving between containers: update its folder map first so the
        // sibling list we build below reflects the post-move state.
        if (!sourceIsFolder)
        {
            if (containerFolderId is null) _folderState.ConnectionFolderMap.Remove(sourceId);
            else _folderState.ConnectionFolderMap[sourceId] = containerFolderId;
        }

        // Build the ordered sibling list for the target's container, EXCLUDING source.
        var siblings = new List<(string id, bool isFolder, int sort, string name)>();
        if (containerFolderId is null)
        {
            // Root: folders + root-level connections, sorted by SortOrder/Name.
            foreach (var f in _folderState.Folders)
            {
                if (sourceIsFolder && f.Id == sourceId) continue;
                siblings.Add((f.Id, true, f.SortOrder, f.Name));
            }
            foreach (var c in Metadata.Connections)
            {
                if (!sourceIsFolder && c.Profile.Id == sourceId) continue;
                if (_folderState.ConnectionFolderMap.TryGetValue(c.Profile.Id, out var fid)
                    && !string.IsNullOrEmpty(fid)) continue;
                siblings.Add((c.Profile.Id, false, GetConnectionSortOrder(c.Profile.Id), c.Profile.Name));
            }
        }
        else
        {
            // Folder members.
            foreach (var c in Metadata.Connections)
            {
                if (!sourceIsFolder && c.Profile.Id == sourceId) continue;
                if (!_folderState.ConnectionFolderMap.TryGetValue(c.Profile.Id, out var fid)
                    || fid != containerFolderId) continue;
                siblings.Add((c.Profile.Id, false, GetConnectionSortOrder(c.Profile.Id), c.Profile.Name));
            }
        }

        siblings.Sort((a, b) =>
        {
            var s = a.sort.CompareTo(b.sort);
            return s != 0 ? s : string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase);
        });

        var targetIndex = siblings.FindIndex(s => s.id == targetId && s.isFolder == targetIsFolder);
        if (targetIndex < 0) return;

        var insertIndex = position == DropPosition.After ? targetIndex + 1 : targetIndex;

        // Look up source name for stability in case of future tiebreaks (sort field becomes a contiguous index here).
        var sourceName = dragSource switch
        {
            ConnectionNodeViewModel c => c.Profile.Name,
            FolderNodeViewModel f => f.Name,
            _ => string.Empty,
        };
        siblings.Insert(insertIndex, (sourceId, sourceIsFolder, 0, sourceName));

        // Renumber 0..N — this is what the next ReloadConnections will read back.
        for (var i = 0; i < siblings.Count; i++)
        {
            var s = siblings[i];
            if (s.isFolder)
            {
                var entry = _folderState.Folders.Find(f => f.Id == s.id);
                if (entry is not null) entry.SortOrder = i;
            }
            else
            {
                _folderState.ConnectionSortOrders[s.id] = i;
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
            ProcedureEasyMode = ProcedureEasyModePreference,
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
        ProcedureEasyModePreference = state.ProcedureEasyMode;

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
            else if (tab.Kind == WorkspaceTabKind.ViewDetail)
            {
                // Skip transient New View tabs (IsNew) — the view doesn't exist yet,
                // so restoring it would just fail to load. Persist real views as
                // ViewDetail so restore re-opens the full 6-tab surface (not DDL-only).
                if (tab.ViewDetail is { IsNew: true }) continue;
                var ddl = tab.ViewDetail is { } vd ? vd.DdlText : tab.DdlText;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.ViewDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = ddl,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.ProcedureDetail)
            {
                // Skip transient New Procedure tabs (IsNew) — the procedure doesn't
                // exist yet. Persist real procedures as ProcedureDetail so restore
                // re-opens the full surface (not DDL-only).
                if (tab.ProcedureDetail is { IsNew: true }) continue;
                var ddl = tab.ProcedureDetail is { } pd ? pd.DdlText : tab.DdlText;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.ProcedureDetail,
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
                // Same factory as the direct-open path — system-table tabs restore
                // read-only (no data editor / DDL executor) exactly as they opened.
                var detail = CreateTableDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateTableDetail(this, obj, detail, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.ViewDetail
                  && tab.ObjectKind is { } viewKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Native ViewDetail restore (no DDL-only fallback). Lazy-loads on
                // first activation via SelectTab — same anti-race pattern as
                // TableDetail. Cached DDL seeds the DDL tab before fetch returns.
                var obj = new MetadataObject(tab.ObjectName, viewKind);
                var detail = CreateViewDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateViewDetail(this, obj, detail, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.ProcedureDetail
                  && tab.ObjectKind is { } procKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Native ProcedureDetail restore (no DDL-only fallback). Lazy-loads
                // on first activation via SelectTab. Cached DDL seeds the DDL tab
                // before fetch returns.
                var obj = new MetadataObject(tab.ObjectName, procKind);
                var detail = CreateProcedureDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateProcedureDetail(this, obj, detail, tab.ConnectionProfileId));
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
        _tabActivationHistory.Clear();
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
        // Either lane (data or metadata) may hold an open working transaction; both are
        // rolled back on disconnect. Confirm if either is active.
        if (_transactionService.IsActive || _metadataTransactionService.IsActive)
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
            if (_metadataTransactionService.IsActive)
            {
                await _metadataTransactionService.RollbackAsync().ConfigureAwait(true);
            }
            if (_transactionService.IsActive)
            {
                await _transactionService.RollbackAsync().ConfigureAwait(true);
            }
        }

        ClearError();
        await _service.DisconnectAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// User-facing connection delete. Confirms first (HIGH-risk: saved
    /// settings, per-connection saved queries, and workspace state are all
    /// dropped irreversibly), then performs the removal. Routed from the
    /// connection node's Delete command and the sidebar toolbar's
    /// Delete-selected button.
    /// </summary>
    public async Task DeleteWithConfirmationAsync(ConnectionProfile profile)
    {
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.ConnectionDeleteConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.ConnectionDeleteConfirmFormat, profile.Name),
            ConfirmLabel = UiStrings.ConnectionDeleteConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        Delete(profile);
    }

    /// <summary>
    /// Raw connection removal — no confirmation. Used by
    /// <see cref="DeleteWithConfirmationAsync"/> after the user confirms, and
    /// directly by tests. UI never calls this without going through the
    /// confirming wrapper.
    /// </summary>
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
            DataTransactionProfile = profile.DataTransactionProfile,
            MetadataTransactionProfile = profile.MetadataTransactionProfile,
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

    // ─── New Table workspace tab + executor exposure ──────────────────────
    //
    // New Table button creates a workspace tab (WorkspaceTabKind.NewTable)
    // hosting NewTableTabViewModel. The user fills in the form progressively;
    // ⚡ Compile from the main toolbar fires the DDL via FirebirdDdlExecutor
    // (in the user's working tx), refreshes the tree, then closes the tab.
    // Multiple New Table tabs can coexist.

    internal FirebirdDdlExecutor DdlExecutor => _ddlExecutor;
    internal FirebirdMetadataReader MetadataReader => _metadataReader;
    // Exposed for the FK wizard's view-side callbacks (ref-table column /
    // PK lookups). The reader's open methods are session-scoped via
    // CommandLock so concurrent reads from the wizard + main load are safe.
    internal FirebirdTableDetailReader TableDetailReader => _tableDetailReader;

    public bool CanCreateTable => _service.IsConnected;

    [RelayCommand(CanExecute = nameof(CanCreateTable))]
    private async Task NewTableAsync()
    {
        var newTableVm = new NewTableTabViewModel(this);
        newTableVm.CompileRequested += OnNewTableCompileRequested;

        // Best-effort fetch of available domains so the in-cell Domain combo has
        // something to offer. Failure surfaces as an empty list — non-fatal.
        try
        {
            var domains = await _metadataReader.ListDomainsAsync().ConfigureAwait(true);
            newTableVm.SetAvailableDomains(domains);
        }
        catch (MetadataReadException) { /* best effort */ }

        var tab = WorkspaceTabViewModel.CreateNewTable(this, newTableVm, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private async Task OnNewTableCompileRequested(NewTableTabViewModel newTable)
    {
        // Identifiers in Firebird DDL are case-significant unless quoted; we
        // emit them upper-cased (canonical RDB$ form) here as a belt-and-braces
        // pass after the UI-side UPPERCASE coercion. Doesn't hurt if the VM
        // already uppercased — same string in, same string out.
        var trimmed = newTable.TableName.Trim().ToUpperInvariant();
        try
        {
            var sql = DdlGenerator.BuildCreateTable(trimmed, newTable.BuildSpec());
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
            AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.NewTableExecutedFormat, trimmed));
            await Metadata.RefreshAsync().ConfigureAwait(true);

            // Keep the user in context: close the New Table tab and open a
            // TableDetail tab on the freshly-created table. Goes through
            // OnOpenDdlRequested so dedup + tree refresh share the same path
            // as a double-click on the sidebar leaf.
            WorkspaceTabViewModel? newTab = null;
            foreach (var t in WorkspaceTabs)
            {
                if (t.Kind == WorkspaceTabKind.NewTable && ReferenceEquals(t.NewTable, newTable))
                {
                    newTab = t;
                    break;
                }
            }
            if (newTab is not null) CloseTab(newTab);
            // OnOpenDdlRequested is async void (event-handler shape) — fire-and-forget.
            // The TableDetail tab is added + selected synchronously inside the
            // method; EnsureLoadedAsync runs as a background continuation.
            OnOpenDdlRequested(new MetadataObject(trimmed, MetadataObjectKind.Table));
        }
        catch (DdlExecutionException ex)
        {
            newTable.ValidationMessage = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            newTable.ValidationMessage = ex.Message;
        }
        catch (ArgumentException ex)
        {
            // BuildCreateTable validation gap (empty name etc.) — IsValid should
            // catch these first, but surface anything that slips through rather
            // than letting the async command swallow it silently (#3).
            newTable.ValidationMessage = ex.Message;
        }
    }

    public bool CanCreateView => _service.IsConnected;

    // New View: opens a View Detail tab in IsNew mode with the SQL tab seeded
    // from the CREATE VIEW template. No visual designer — the user edits SQL
    // directly and presses Compile (per milestone scope). The other tabs stay
    // empty until the view is created; on Compile success OnViewCreated refreshes
    // the tree, closes this tab and reopens the real view.
    [RelayCommand(CanExecute = nameof(CanCreateView))]
    private void NewView()
    {
        var detail = new ViewDetailTabViewModel(UiStrings.NewViewTabDefaultTitle, _tableDetailReader, _ddlReader, _ddlExecutor)
        {
            IsNew = true,
            SourceText = ViewDetailTabViewModel.NewViewTemplate,
        };
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ViewCreated += name => OnViewCreated(detail, name);

        var obj = new MetadataObject(UiStrings.NewViewTabDefaultTitle, MetadataObjectKind.View);
        var tab = WorkspaceTabViewModel.CreateViewDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private async void OnViewCreated(ViewDetailTabViewModel detail, string? viewName)
    {
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.NewViewExecutedFormat, viewName ?? string.Empty));
        await Metadata.RefreshAsync().ConfigureAwait(true);

        // Close the New View tab.
        WorkspaceTabViewModel? newTab = null;
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.ViewDetail && ReferenceEquals(t.ViewDetail, detail))
            {
                newTab = t;
                break;
            }
        }
        if (newTab is not null) CloseTab(newTab);

        // Reopen the freshly-created view as a normal (existing) ViewDetail tab
        // when we could parse its name; otherwise the user finds it in the tree.
        if (!string.IsNullOrEmpty(viewName))
        {
            OnOpenDdlRequested(new MetadataObject(viewName, MetadataObjectKind.View));
        }
    }

    public bool CanCreateProcedure => _service.IsConnected;

    // New Procedure: opens a Procedure Detail tab in IsNew mode with the Editor
    // seeded from the CREATE OR ALTER PROCEDURE template. No visual designer — the
    // user edits SQL directly and presses Compile (per milestone scope). The other
    // tabs stay empty until the procedure is created; on Compile success
    // OnProcedureCreated refreshes the tree, closes this tab and reopens the real
    // procedure.
    [RelayCommand(CanExecute = nameof(CanCreateProcedure))]
    private void NewProcedure()
    {
        var detail = new ProcedureDetailTabViewModel(UiStrings.NewProcedureTabDefaultTitle, _tableDetailReader, _ddlReader, _ddlExecutor)
        {
            IsNew = true,
            SourceText = ProcedureDetailTabViewModel.NewProcedureTemplate,
        };
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.RunExecuteRequested = RunProcedureExecuteAsync;
        detail.ProcedureCreated += name => OnProcedureCreated(detail, name);

        var obj = new MetadataObject(UiStrings.NewProcedureTabDefaultTitle, MetadataObjectKind.Procedure);
        var tab = WorkspaceTabViewModel.CreateProcedureDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private async void OnProcedureCreated(ProcedureDetailTabViewModel detail, string? procedureName)
    {
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.NewProcedureExecutedFormat, procedureName ?? string.Empty));
        await Metadata.RefreshAsync().ConfigureAwait(true);

        // Close the New Procedure tab.
        WorkspaceTabViewModel? newTab = null;
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.ProcedureDetail && ReferenceEquals(t.ProcedureDetail, detail))
            {
                newTab = t;
                break;
            }
        }
        if (newTab is not null) CloseTab(newTab);

        // Reopen the freshly-created procedure as a normal (existing) tab when we
        // could parse its name; otherwise the user finds it in the tree.
        if (!string.IsNullOrEmpty(procedureName))
        {
            OnOpenDdlRequested(new MetadataObject(procedureName, MetadataObjectKind.Procedure));
        }
    }

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

    // Kinds that open in the rich TableDetail view instead of a plain DDL tab.
    // Table-shaped kinds only: a user table (writable) and a system table
    // (read-only). Future external tables would slot in here. Views / procedures /
    // triggers / functions / packages are structurally different and stay DDL tabs
    // (or get their own detail view in a later milestone) — do NOT force them
    // through TableDetail.
    internal static bool OpensAsTableDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Table or MetadataObjectKind.SystemTable;

    // Views open in the dedicated View Detail surface (editable SQL source +
    // Fields / Dependencies / Data / Description / DDL), not a plain DDL tab.
    // Kept as a separate predicate from OpensAsTableDetail because the two build
    // structurally different detail VMs.
    internal static bool OpensAsViewDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.View;

    // Procedures open in the dedicated Procedure Detail surface (editable
    // CREATE OR ALTER PROCEDURE source + Compile / Description / Dependencies /
    // DDL, with Input/Output parameter grids under the editor), not a plain DDL
    // tab. Separate predicate from the view/table ones because it builds a
    // structurally different detail VM.
    internal static bool OpensAsProcedureDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Procedure;

    // Single construction point for ViewDetail VMs — mirrors CreateTableDetail.
    // A view is read-only data (no inline editing) but its SQL source IS editable,
    // so the DDL executor is wired for Compile while no data editor is.
    internal ViewDetailTabViewModel CreateViewDetail(MetadataObject obj)
    {
        var detail = new ViewDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        return detail;
    }

    // Single construction point for ProcedureDetail VMs — mirrors CreateViewDetail.
    // The procedure source IS editable (Compile), so the DDL executor is wired;
    // there is no data editor (procedures have no Data tab).
    // Last-used Procedure Detail editor mode (false = Source, true = Easy),
    // mirrored to WorkspaceState.ProcedureEasyMode. Applied to each newly opened
    // existing procedure; updated when the user toggles a procedure's mode so the
    // preference follows them across procedures and app restarts.
    internal bool ProcedureEasyModePreference { get; set; }

    internal ProcedureDetailTabViewModel CreateProcedureDetail(MetadataObject obj)
    {
        var detail = new ProcedureDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.RunExecuteRequested = RunProcedureExecuteAsync;
        // Best-effort domain list for the Variables grid's Domain combo (Easy mode).
        _ = LoadProcedureDomainsAsync(detail);
        // Restore the remembered mode (existing procedures only — New stays Source).
        if (detail.CanUseEasyMode) detail.EasyMode = ProcedureEasyModePreference;
        detail.PropertyChanged += OnProcedureDetailPropertyChanged;
        return detail;
    }

    private async Task LoadProcedureDomainsAsync(ProcedureDetailTabViewModel detail)
    {
        try
        {
            var domains = await _metadataReader.ListDomainsAsync().ConfigureAwait(true);
            detail.SetAvailableDomains(domains);
        }
        catch (MetadataReadException) { /* best effort — combo just has "(none)" */ }
    }

    private void OnProcedureDetailPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcedureDetailTabViewModel.EasyMode)
            && sender is ProcedureDetailTabViewModel { CanUseEasyMode: true } d)
        {
            ProcedureEasyModePreference = d.EasyMode;
        }
    }

    // Runs an Execute Procedure statement on the Data lane with bound parameters
    // (no literal embedding). Wraps the result/error so the procedure tab can show
    // it in its own Result region. EXECUTE PROCEDURE/SELECT are Data-lane per the
    // classifier — auto-begins the data working tx; the user Commits/Rolls back.
    private async Task<ProcedureExecOutcome> RunProcedureExecuteAsync(string sql, IReadOnlyList<QueryParameter> parameters)
    {
        try
        {
            var result = await _executor.ExecuteAsync(sql, parameters).ConfigureAwait(true);
            AddMessage(MessageSeverity.Info, UiStrings.ProcedureExecutedViaDataProfile);
            return new ProcedureExecOutcome(result, null);
        }
        catch (QueryExecutionException ex)
        {
            return new ProcedureExecOutcome(null, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new ProcedureExecOutcome(null, ex.Message);
        }
    }

    // The SINGLE construction point for TableDetail VMs. Capability is decided here
    // and only here, keyed on the object kind: a writable user table gets the data
    // editor + DDL executor; a read-only kind (system table) gets NEITHER. The
    // existing capability gates (CanEditData / CanAddField / CanManageConstraints /
    // CanManageIndexes / CanCompile, all of which derive from the presence of those
    // services) then turn every edit affordance off — no second source of truth, no
    // IsReadOnly flag. Both the direct-open path (OnOpenDdlRequested) and the
    // workspace-restore path (LoadWorkspaceFor) go through here, so the two cannot
    // diverge — read-only capabilities are guaranteed identical whether a system
    // table is opened fresh or restored from disk.
    internal TableDetailTabViewModel CreateTableDetail(MetadataObject obj)
    {
        bool writable = obj.Kind == MetadataObjectKind.Table;
        var detail = new TableDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            writable ? _dataEditor : null,
            writable ? _ddlExecutor : null,
            _metadataReader);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        return detail;
    }

    private async void OnOpenDdlRequested(MetadataObject obj)
    {
        // Focus an existing tab for the same object if one is already open —
        // double-clicking twice shouldn't pile up duplicates. Both Ddl and
        // TableDetail tabs key on (Kind, Name).
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind is WorkspaceTabKind.Ddl or WorkspaceTabKind.TableDetail or WorkspaceTabKind.ViewDetail or WorkspaceTabKind.ProcedureDetail
                && tab.ObjectKind == obj.Kind
                && string.Equals(tab.ObjectName, obj.Name, StringComparison.Ordinal))
            {
                SelectTab(tab);
                return;
            }
        }

        try
        {
            if (OpensAsTableDetail(obj.Kind))
            {
                var detail = CreateTableDetail(obj);
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

            if (OpensAsViewDetail(obj.Kind))
            {
                var detail = CreateViewDetail(obj);
                var newTab = WorkspaceTabViewModel.CreateViewDetail(this, obj, detail, _service.ActiveProfile?.Id);
                WorkspaceTabs.Add(newTab);
                SelectTab(newTab);
                await detail.EnsureLoadedAsync().ConfigureAwait(true);
                if (!string.IsNullOrEmpty(detail.ErrorMessage))
                {
                    AddMessage(MessageSeverity.Error, detail.ErrorMessage);
                    SelectedBottomTabIndex = 1;
                }
                return;
            }

            if (OpensAsProcedureDetail(obj.Kind))
            {
                var detail = CreateProcedureDetail(obj);
                var newTab = WorkspaceTabViewModel.CreateProcedureDetail(this, obj, detail, _service.ActiveProfile?.Id);
                WorkspaceTabs.Add(newTab);
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
        // Record activation order so CloseTab can return to the previously-active tab.
        // Move-to-end semantics: re-activating an already-visited tab makes it the
        // most recent.
        _tabActivationHistory.Remove(tab);
        _tabActivationHistory.Add(tab);

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
        else if (tab is { Kind: WorkspaceTabKind.ViewDetail, ViewDetail: { } viewDetail }
            && _service.IsConnected)
        {
            _ = viewDetail.EnsureLoadedAsync();
        }
        else if (tab is { Kind: WorkspaceTabKind.ProcedureDetail, ProcedureDetail: { } procedureDetail }
            && _service.IsConnected)
        {
            _ = procedureDetail.EnsureLoadedAsync();
        }
    }

    internal void CloseTab(WorkspaceTabViewModel tab)
    {
        if (!tab.IsClosable) return;
        var index = WorkspaceTabs.IndexOf(tab);
        if (index < 0) return;
        var wasSelected = SelectedWorkspaceTab == tab;
        WorkspaceTabs.RemoveAt(index);
        _tabActivationHistory.Remove(tab);

        if (wasSelected && WorkspaceTabs.Count > 0)
        {
            // Return to the most-recently-active tab that's still open (the tab the
            // user came from), not the arbitrary index-neighbour. Falls back to the
            // index-neighbour only when history has nothing usable left.
            var target = PreviousActiveTab() ?? WorkspaceTabs[Math.Min(index, WorkspaceTabs.Count - 1)];
            SelectTab(target);
        }
    }

    /// <summary>
    /// True while the SQL-editor results panel is maximized. Drives the tab-strip
    /// toggle button's icon (maximize vs. restore). Set by the view's
    /// <c>ToggleResultsMaximized</c> — the actual row sizing lives in code-behind
    /// (GridLength isn't a VM concern), this is just the bound display state.
    /// </summary>
    [ObservableProperty]
    private bool _isResultsMaximized;

    internal void SetResultsMaximized(bool value) => IsResultsMaximized = value;

    // The newest still-open tab in the activation history (the closing tab has
    // already been pruned, so the last entry is the prior activation). Skips any
    // stale entries that are no longer in WorkspaceTabs.
    private WorkspaceTabViewModel? PreviousActiveTab()
    {
        for (int i = _tabActivationHistory.Count - 1; i >= 0; i--)
        {
            var candidate = _tabActivationHistory[i];
            if (WorkspaceTabs.Contains(candidate)) return candidate;
        }
        return null;
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

    // Tables-category context menu → New Table. Reuses the existing
    // NewTableCommand so there's one New-Table flow (workspace tab + Compile).
    private void OnNewTableRequestedFromTree()
    {
        if (NewTableCommand.CanExecute(null)) NewTableCommand.Execute(null);
    }

    // Table leaf context menu → Delete Table. Confirm → DROP TABLE → refresh
    // tree + close any open tabs for that table. On error, surface the raw
    // Firebird message (incl. dependency errors — we never auto-drop deps).
    private async void OnDeleteTableRequested(MetadataObject obj)
    {
        if (obj.Kind != MetadataObjectKind.Table) return;

        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.MetadataDeleteTableConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.MetadataDeleteTableConfirmFormat, obj.Name),
            ConfirmLabel = UiStrings.MetadataDeleteTableConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;

        var sql = DdlGenerator.BuildDropTable(obj.Name);
        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            AddMessage(MessageSeverity.Error, string.Format(CultureInfo.CurrentCulture, UiStrings.MetadataDeleteTableFailedFormat, obj.Name, ex.Message));
            SelectedBottomTabIndex = 1;
            return;
        }
        catch (InvalidOperationException ex)
        {
            AddMessage(MessageSeverity.Error, string.Format(CultureInfo.CurrentCulture, UiStrings.MetadataDeleteTableFailedFormat, obj.Name, ex.Message));
            SelectedBottomTabIndex = 1;
            return;
        }

        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.MetadataDeleteTableExecutedFormat, obj.Name));

        // Close any open Ddl / TableDetail tabs that target this table.
        CloseTabsForObject(obj.Kind, obj.Name);

        // Refresh the metadata tree so the table disappears from the navigator.
        await Metadata.RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Closes every workspace tab (DDL or TableDetail) keyed on the given
    /// (kind, name). Internal so tests can drive it without standing up the
    /// DROP path. Tab-close semantics match the user clicking ×.
    /// </summary>
    internal void CloseTabsForObject(MetadataObjectKind kind, string name)
    {
        // Snapshot — CloseTab mutates WorkspaceTabs.
        var doomed = WorkspaceTabs
            .Where(t => t.Kind is WorkspaceTabKind.Ddl or WorkspaceTabKind.TableDetail
                        && t.ObjectKind == kind
                        && string.Equals(t.ObjectName, name, StringComparison.Ordinal))
            .ToList();
        foreach (var tab in doomed) CloseTab(tab);
    }

    // F5 / Ctrl+Enter — the single Execute. The lane is chosen automatically from the
    // SQL: data statements (SELECT/INSERT/UPDATE/DELETE/MERGE/EXECUTE …) run on the
    // Data lane (connection #1, data profile); DDL/DCL (CREATE/ALTER/DROP/COMMENT/
    // GRANT/…) on the Metadata lane (connection #2, metadata profile). Ambiguous input
    // falls back to Data — the safest lane. There is no manual lane override by design.
    [RelayCommand(CanExecute = nameof(CanExecute))]
    public Task ExecuteQueryAsync() => RunExecuteAsync();

    private async Task RunExecuteAsync()
    {
        // If the user has highlighted a fragment in the editor, execute only that;
        // otherwise execute the whole editor content (legacy behaviour).
        var sql = ResolveActiveSql();
        if (IsExecuting || string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        // Auto-route by statement kind. Ambiguous → Data (read_committed + nowait,
        // never blocks). See SqlStatementClassifier for the EXECUTE BLOCK reasoning.
        var metadata = SqlStatementClassifier.Classify(sql) == StatementLane.Metadata;

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

        // Always log which lane/profile the auto-router chose, so the user never has to
        // guess which transaction this statement ran under.
        var executor = metadata ? _metadataExecutor : _executor;
        AddMessage(MessageSeverity.Info, BuildExecutedViaMessage(metadata));

        try
        {
            var result = await executor.ExecuteAsync(sql, _executionCts.Token).ConfigureAwait(true);
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

    // "Executed via Data profile (Read Committed)." / "Executed via Metadata profile
    // (Read Write Table Stability)." — surfaced in the Messages log on every execute.
    private string BuildExecutedViaMessage(bool metadata)
    {
        var lane = metadata ? UiStrings.TransactionLaneMetadata : UiStrings.TransactionLaneData;
        var profile = metadata
            ? (_service.ActiveProfile?.MetadataTransactionProfile ?? Core.Connections.TransactionProfile.ReadCommitted)
            : (_service.ActiveProfile?.DataTransactionProfile ?? Core.Connections.TransactionProfile.ReadCommitted);
        return string.Format(UiStrings.ExecutedViaProfileFormat, lane, TransactionProfileCatalog.LabelFor(profile));
    }

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
    private async Task ClearActiveEditorAsync()
    {
        // Only meaningful for Query tabs — DDL tabs are read-only. CanExecute gate
        // ensures the button is greyed out on DDL.
        if (!IsQueryTabActive) return;
        // Nothing to lose → clear silently (also avoids a pointless prompt when
        // the editor is already empty).
        if (string.IsNullOrEmpty(QueryText)) return;

        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.ClearEditorConfirmTitle,
            Message = UiStrings.ClearEditorConfirmMessage,
            ConfirmLabel = UiStrings.ClearEditorConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;
        QueryText = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanCloseActiveTab))]
    private async Task CloseActiveTabAsync()
    {
        if (SelectedWorkspaceTab is { IsClosable: true } tab)
        {
            await RequestCloseTabAsync(tab).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// User-initiated tab close. Confirms before discarding a New Table tab
    /// that has unsaved form content (DDL / TableDetail tabs are reopenable
    /// from the tree, so they close silently). Programmatic closes
    /// (post-compile, delete-table cleanup) call <see cref="CloseTab"/>
    /// directly and never prompt.
    /// </summary>
    public async Task RequestCloseTabAsync(WorkspaceTabViewModel tab)
    {
        if (tab.Kind == WorkspaceTabKind.NewTable && tab.NewTable is { HasContent: true } form)
        {
            var confirmed = await RequestConfirmAsync(new ConfirmRequest
            {
                Title = UiStrings.NewTableCloseConfirmTitle,
                Message = string.Format(CultureInfo.CurrentCulture, UiStrings.NewTableCloseConfirmFormat, form.DisplayTitle),
                ConfirmLabel = UiStrings.NewTableCloseConfirmYes,
                CancelLabel = UiStrings.DialogCancel,
                IsDestructive = true,
            }).ConfigureAwait(true);
            if (!confirmed) return;
        }
        CloseTab(tab);
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

    // Bridge: TableDetailTabViewModel.IsDataSubTabActive → MainWindowViewModel.IsDataTabActive.
    // We subscribe to PropertyChanged on the inner VM so changing sub-tabs
    // inside a TableDetail tab flips the toolbar without the user having to
    // switch outer tabs first.
    private TableDetailTabViewModel? _trackedTableDetail;

    partial void OnSelectedWorkspaceTabChanged(WorkspaceTabViewModel? oldValue, WorkspaceTabViewModel? newValue)
    {
        if (_trackedTableDetail is not null)
        {
            _trackedTableDetail.PropertyChanged -= OnTableDetailPropertyChanged;
            _trackedTableDetail = null;
        }
        if (newValue is { Kind: WorkspaceTabKind.TableDetail, TableDetail: { } td })
        {
            _trackedTableDetail = td;
            _trackedTableDetail.PropertyChanged += OnTableDetailPropertyChanged;
        }
    }

    private void OnTableDetailPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TableDetailTabViewModel.IsDataSubTabActive))
        {
            OnPropertyChanged(nameof(IsDataTabActive));
            OnPropertyChanged(nameof(ShowDataEditTools));
            OnPropertyChanged(nameof(ShowTransactionButtons));
            RefreshDataPreviewCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(TableDetailTabViewModel.IsFieldsSubTabActive))
        {
            OnPropertyChanged(nameof(IsFieldsTabActive));
            OnPropertyChanged(nameof(ShowFieldEditTools));
            OnPropertyChanged(nameof(ShowTransactionButtons));
        }
    }

    public bool CanRefreshDataPreview => IsDataTabActive;

    [RelayCommand(CanExecute = nameof(CanRefreshDataPreview))]
    private async Task RefreshDataPreviewAsync()
    {
        if (SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TableDetail, TableDetail: { } td })
        {
            await td.ReloadDataPreviewAsync().ConfigureAwait(true);
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

    // ─── Unified Commit / Rollback (single user-facing pair) ─────────────
    //
    // The user sees ONE Commit + ONE Rollback. They never choose a lane: the app
    // auto-routed each statement to the Data or Metadata lane (SqlStatementClassifier),
    // so Commit settles every lane that has an open transaction and Rollback reverts
    // every lane that's active or in error. When both lanes are active, both are
    // committed / rolled back. The per-lane commands below remain as building blocks
    // (CommitAll/RollbackAll call CommitLaneAsync/RollbackLaneAsync), and the disconnect
    // path still rolls back both lanes directly.

    public bool CanCommitAll
    {
        get { var (d, m) = DecideCommitLanes(_transactionService.IsActive, _service.MetadataIsIndependent, _metadataTransactionService.IsActive); return d || m; }
    }

    public bool CanRollbackAll
    {
        get
        {
            var (d, m) = DecideRollbackLanes(
                _transactionService.IsActive, _transactionService.IsError,
                _service.MetadataIsIndependent, _metadataTransactionService.IsActive, _metadataTransactionService.IsError);
            return d || m;
        }
    }

    // Pure lane-selection decisions — unit-testable without a live transaction.
    // Metadata is only its own lane when independent; otherwise it delegates to the
    // data lane, so acting on it again would be a redundant no-op.
    internal static (bool data, bool metadata) DecideCommitLanes(bool dataActive, bool metadataIndependent, bool metadataActive)
        => (dataActive, metadataIndependent && metadataActive);

    internal static (bool data, bool metadata) DecideRollbackLanes(
        bool dataActive, bool dataError, bool metadataIndependent, bool metadataActive, bool metadataError)
        => (dataActive || dataError, metadataIndependent && (metadataActive || metadataError));

    [RelayCommand(CanExecute = nameof(CanCommitAll))]
    private async Task CommitAllAsync()
    {
        var (commitData, commitMeta) = DecideCommitLanes(
            _transactionService.IsActive, _service.MetadataIsIndependent, _metadataTransactionService.IsActive);
        if (commitData) await CommitLaneAsync(_transactionService, UiStrings.TransactionLaneData).ConfigureAwait(true);
        if (commitMeta) await CommitLaneAsync(_metadataTransactionService, UiStrings.TransactionLaneMetadata).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanRollbackAll))]
    private async Task RollbackAllAsync()
    {
        var (rollbackData, rollbackMeta) = DecideRollbackLanes(
            _transactionService.IsActive, _transactionService.IsError,
            _service.MetadataIsIndependent, _metadataTransactionService.IsActive, _metadataTransactionService.IsError);
        if (rollbackData) await RollbackLaneAsync(_transactionService, UiStrings.TransactionLaneData).ConfigureAwait(true);
        if (rollbackMeta) await RollbackLaneAsync(_metadataTransactionService, UiStrings.TransactionLaneMetadata).ConfigureAwait(true);
    }

    // Data lane (SQL Editor F5, data preview/edit).
    [RelayCommand]
    private Task CommitAsync() => CommitLaneAsync(_transactionService, UiStrings.TransactionLaneData);

    [RelayCommand]
    private Task RollbackAsync() => RollbackLaneAsync(_transactionService, UiStrings.TransactionLaneData);

    // Metadata lane (DDL from the structure editor, Shift+F5).
    [RelayCommand]
    private Task CommitMetadataAsync() => CommitLaneAsync(_metadataTransactionService, UiStrings.TransactionLaneMetadata);

    [RelayCommand]
    private Task RollbackMetadataAsync() => RollbackLaneAsync(_metadataTransactionService, UiStrings.TransactionLaneMetadata);

    private async Task CommitLaneAsync(TransactionService tx, string lane)
    {
        var count = tx.StatementCount;
        // Commit ⇒ no post-settle refresh (UI already current); see DecidePostTransactionRefresh.
        _lastTransactionSettleWasRollback = false;
        Diagnostics.RefreshTrace.Log("Commit", $"lane={lane} statements={count} (no post-commit refresh)");
        try
        {
            await tx.CommitAsync().ConfigureAwait(true);
            AddMessage(MessageSeverity.Info, string.Format(UiStrings.TransactionLaneCommittedFormat, lane, count));
        }
        catch (TransactionFailedException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
        }
    }

    private async Task RollbackLaneAsync(TransactionService tx, string lane)
    {
        var count = tx.StatementCount;
        // Rollback ⇒ post-settle refresh runs to revert the in-memory / optimistic state.
        _lastTransactionSettleWasRollback = true;
        Diagnostics.RefreshTrace.Log("Rollback", $"lane={lane} statements={count} (refresh to revert)");
        await tx.RollbackAsync().ConfigureAwait(true);
        AddMessage(MessageSeverity.Info, string.Format(UiStrings.TransactionLaneRolledBackFormat, lane, count));
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

    // The active connection's profile was edited in place (same Id) — no connection
    // switch, so we DON'T run ApplyActiveConnectionChange (which would stash/reload the
    // workspace and clear the column cache). Just repaint everything that reads
    // ActiveProfile: status bar text + the title-bar transaction-profile chips/tooltips.
    private void OnActiveProfileUpdated(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateStatusFromConnection();
            OnPropertyChanged(nameof(ActiveConnectionName));
            OnPropertyChanged(nameof(HasActiveConnection));
            OnPropertyChanged(nameof(DataProfileName));
            OnPropertyChanged(nameof(MetadataProfileName));
            OnPropertyChanged(nameof(DataTransactionProfileTooltip));
            OnPropertyChanged(nameof(MetadataTransactionProfileTooltip));
            OnPropertyChanged(nameof(IsDeveloperModeActive));
        });
    }

    /// <summary>
    /// Persists an edited connection profile and, when it is the currently-active
    /// connection, swaps the connection service's in-memory profile so the status bar
    /// and the NEXT transaction immediately use the new settings (the active transaction
    /// keeps its parameters until commit/rollback — no autocommit, rule #3). Always
    /// rebuilds the tree so the new name/host show in the sidebar.
    /// </summary>
    public void ApplyEditedProfile(ConnectionProfile profile)
    {
        _store.Upsert(profile);
        _service.UpdateActiveProfile(profile); // no-op unless it's the active connection
        ReloadConnections();
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
        OnPropertyChanged(nameof(DataProfileName));
        OnPropertyChanged(nameof(MetadataProfileName));
        OnPropertyChanged(nameof(DataTransactionProfileTooltip));
        OnPropertyChanged(nameof(MetadataTransactionProfileTooltip));
        OnPropertyChanged(nameof(IsDeveloperModeActive));
        OnPropertyChanged(nameof(MetadataLaneIndependent));
        OnPropertyChanged(nameof(ShowDataTransactionButtons));
        OnPropertyChanged(nameof(ShowMetadataTransactionButtons));
        OnPropertyChanged(nameof(CanCreateTable));
        NewTableCommand.NotifyCanExecuteChanged();
        // New View shares the same connection-state gate as New Table; without
        // this re-notification its command stayed at its construction-time
        // CanExecute (false) and the toolbar button never enabled on connect.
        OnPropertyChanged(nameof(CanCreateView));
        NewViewCommand.NotifyCanExecuteChanged();
        // New Procedure shares the same connection-state gate.
        OnPropertyChanged(nameof(CanCreateProcedure));
        NewProcedureCommand.NotifyCanExecuteChanged();
    }

    internal IReadOnlyDictionary<string, ConnectionWorkspace> WorkspacesByConnection
        => _workspacesByConnection;

    // Scope of the refresh a TableDetail tab needs after a transaction settles.
    internal enum PostTransactionRefresh
    {
        None,
        DataOnly,   // data-lane commit/rollback — reload the data preview only
        Structure,  // metadata-lane commit/rollback — full structure reload (DDL)
    }

    // Pure decision so it's unit-testable without a live connection.
    //
    // A COMMIT needs NO refresh: the UI already shows the committed state — the
    // structure editor calls RefreshStructureAsync when it APPLIES the ALTER (before
    // the user commits), and data edits paint optimistically. Refreshing after a
    // commit only opens an extra transaction (TRA_95413 in the trace); on a database
    // with an ON TRANSACTION COMMIT trigger (e.g. the user's XXX_WS_TRANS_ON_COMMIT
    // audit trigger → GET_NAGL_WERDYSP → hundreds of BIN_AND/MOD calls) that extra
    // commit re-fires the trigger — the "massive activity after commit". So commit ⇒ None.
    //
    // A ROLLBACK MUST refresh: the in-memory model / optimistic grid writes have to be
    // reverted to the real (rolled-back) DB state. Metadata rollback → full structure
    // reload; data rollback → data preview reload. Metadata wins when both settle.
    internal static PostTransactionRefresh DecidePostTransactionRefresh(bool dataSettled, bool metadataSettled, bool wasRollback)
    {
        if (!wasRollback) return PostTransactionRefresh.None;
        return metadataSettled ? PostTransactionRefresh.Structure
             : dataSettled ? PostTransactionRefresh.DataOnly
             : PostTransactionRefresh.None;
    }

    // Re-entrancy guard so two coalesced settle events can't start two overlapping
    // post-transaction refresh batches (gotcha #119).
    private bool _postTxRefreshInFlight;

    // Applies the post-transaction refresh — SCOPED, never a blanket fan-out over every
    // open TableDetail tab (that was the refresh storm: each tab's structure reload runs
    // ~7 heavy RDB$ queries, each an implicit auto-committed tx, each re-firing the user's
    // ON TRANSACTION_COMMIT audit trigger — gotcha #119).
    //   • Structure (metadata rollback, e.g. a raw F5 ALTER) → refresh ONLY the active
    //     TableDetail tab (the object the user is on). Structure edits via the editor are
    //     autonomous (Phase A) and self-refresh at apply-time, so other tabs are untouched.
    //   • DataOnly (data rollback) → reload the preview ONLY for tabs that actually had
    //     data edits in this transaction (revert optimistic writes); all others untouched.
    /// <summary>One scoped refresh target: which TableDetail tab to refresh and whether
    /// it's a structure (true) or data-preview (false) refresh.</summary>
    internal readonly record struct RefreshTarget(TableDetailTabViewModel Detail, bool Structure);

    /// <summary>
    /// PURE selection of which TableDetail tabs a post-transaction refresh touches —
    /// the heart of the refresh-storm fix (gotcha #119), unit-testable without a live tx.
    /// NEVER a blanket fan-out over every open tab:
    ///   • Structure → ONLY the active TableDetail tab (the object the user is on).
    ///   • DataOnly  → ONLY tabs with pending data edits (revert optimistic writes).
    /// </summary>
    internal static IReadOnlyList<RefreshTarget> SelectRefreshTargets(
        PostTransactionRefresh refresh,
        IReadOnlyList<WorkspaceTabViewModel> tabs,
        WorkspaceTabViewModel? activeTab)
    {
        var targets = new List<RefreshTarget>();
        if (refresh == PostTransactionRefresh.Structure)
        {
            if (activeTab is { Kind: WorkspaceTabKind.TableDetail, TableDetail: { } active })
                targets.Add(new RefreshTarget(active, Structure: true));
        }
        else if (refresh == PostTransactionRefresh.DataOnly)
        {
            foreach (var tab in tabs)
                if (tab.Kind == WorkspaceTabKind.TableDetail && tab.TableDetail is { HasPendingDataEdits: true } edited)
                    targets.Add(new RefreshTarget(edited, Structure: false));
        }
        return targets;
    }

    private async void RunScopedPostTransactionRefresh(PostTransactionRefresh refresh)
    {
        if (refresh == PostTransactionRefresh.None || _postTxRefreshInFlight) return;
        var targets = SelectRefreshTargets(refresh, WorkspaceTabs, SelectedWorkspaceTab);
        if (targets.Count == 0) return;
        _postTxRefreshInFlight = true;
        try
        {
            Diagnostics.RefreshTrace.Log("Transaction", $"scoped {refresh} refresh of {targets.Count} tab(s) (NOT a blanket fan-out)");
            var tasks = new List<Task>(targets.Count);
            foreach (var t in targets)
                tasks.Add(t.Structure ? t.Detail.RefreshAfterTransactionAsync() : t.Detail.RefreshDataAfterTransactionAsync());
            await Task.WhenAll(tasks).ConfigureAwait(true);
        }
        catch
        {
            // Each refresh surfaces its own error via SafeLoadAsync → ErrorMessage/DataError.
        }
        finally
        {
            _postTxRefreshInFlight = false;
        }
    }

    private void OnTransactionStateChanged(object? sender, EventArgs e)
    {
        // TransactionStateChanged fires on whichever thread the Begin/Commit/
        // Rollback async work completed on — NOT the UI thread (gotcha #11).
        // The transition booleans are computed synchronously here (safe scalar
        // reads, and correct even if two events coalesce before the dispatcher
        // drains), then the side-effects — which mutate UI-bound collections
        // (AddMessage) and kick off RefreshAfterTransactionAsync (clears +
        // repopulates the TableDetail Fields/EditableFields collections) — are
        // marshalled onto the UI thread. Running those off-thread is what broke
        // the DataGrid binding layer and left the grid unresponsive ("UI locked"
        // after reorder+commit, #6).
        // One handler, both lanes (the data + metadata services share it). In degraded
        // mode the metadata service delegates to the data one, so we treat it as inert
        // (Idle) to avoid double-counting the same transaction.
        var metaIndependent = _service.MetadataIsIndependent;

        var dataCurrent = _transactionService.State;
        var dataBecameActive = _previousTransactionState != TransactionState.Active && dataCurrent == TransactionState.Active;
        // Active → Idle means a Commit or Rollback just completed; the on-screen
        // TableDetail tabs may be out of sync with the live catalog (rollback
        // reverts ALTERs fired in the tx; commit confirms them) — refresh each.
        var dataCommittedOrRolledBack = _previousTransactionState == TransactionState.Active && dataCurrent == TransactionState.Idle;
        _previousTransactionState = dataCurrent;

        var metaCurrent = metaIndependent ? _metadataTransactionService.State : TransactionState.Idle;
        var metaBecameActive = _previousMetadataTransactionState != TransactionState.Active && metaCurrent == TransactionState.Active;
        var metaCommittedOrRolledBack = _previousMetadataTransactionState == TransactionState.Active && metaCurrent == TransactionState.Idle;
        _previousMetadataTransactionState = metaCurrent;

        void Apply()
        {
            if (dataBecameActive)
            {
                AddMessage(MessageSeverity.Info, string.Format(UiStrings.TransactionLaneStartedFormat, UiStrings.TransactionLaneData));
            }
            if (metaBecameActive)
            {
                AddMessage(MessageSeverity.Info, string.Format(UiStrings.TransactionLaneStartedFormat, UiStrings.TransactionLaneMetadata));
            }
            // Route the post-transaction refresh by LANE. A METADATA-lane commit/
            // rollback may have changed the schema (DDL) → full structure refresh.
            // A DATA-lane commit/rollback is DML-only (data edits go through the data
            // lane, DDL through the metadata lane) → the schema is unchanged, so a full
            // structure reload is wasted work: it re-runs 8 metadata round-trips
            // (incl. the heavy dependencies query), freezing the UI, and while it tears
            // down + rebuilds the Fields model it transiently surfaces "Table has no
            // primary key". A DATA-lane refresh reloads ONLY the data preview, keeping
            // Fields/PK intact. metaCommitted wins when both coalesce (its full reload
            // already re-reads the data preview too).
            var refresh = DecidePostTransactionRefresh(dataCommittedOrRolledBack, metaCommittedOrRolledBack, _lastTransactionSettleWasRollback);
            RunScopedPostTransactionRefresh(refresh);
            // Data COMMIT: optimistic in-grid values are now committed (= correct), so no
            // reload is needed — just clear the per-tab pending-edit flags. (A data
            // ROLLBACK reloads the edited tabs below, which clears their flags.)
            if (dataCommittedOrRolledBack && !_lastTransactionSettleWasRollback)
            {
                foreach (var tab in WorkspaceTabs)
                    if (tab.TableDetail is { } committed) committed.HasPendingDataEdits = false;
            }

            OnPropertyChanged(nameof(IsTransactionIdle));
            OnPropertyChanged(nameof(IsTransactionActive));
            OnPropertyChanged(nameof(IsTransactionError));
            OnPropertyChanged(nameof(HasExecutedInTransaction));
            OnPropertyChanged(nameof(TransactionBarText));
            OnPropertyChanged(nameof(IsMetadataTransactionIdle));
            OnPropertyChanged(nameof(IsMetadataTransactionActive));
            OnPropertyChanged(nameof(IsMetadataTransactionError));
            OnPropertyChanged(nameof(HasExecutedInMetadataTransaction));
            OnPropertyChanged(nameof(MetadataTransactionBarText));
            OnPropertyChanged(nameof(ShowDataTransactionButtons));
            OnPropertyChanged(nameof(ShowMetadataTransactionButtons));
            CommitCommand.NotifyCanExecuteChanged();
            RollbackCommand.NotifyCanExecuteChanged();
            CommitMetadataCommand.NotifyCanExecuteChanged();
            RollbackMetadataCommand.NotifyCanExecuteChanged();
            // Unified pair — enabled state follows whichever lane(s) are active/error.
            OnPropertyChanged(nameof(CanCommitAll));
            OnPropertyChanged(nameof(CanRollbackAll));
            CommitAllCommand.NotifyCanExecuteChanged();
            RollbackAllCommand.NotifyCanExecuteChanged();

            // The Query tab carries the transaction-active marker. It shows when EITHER
            // lane has executed statements (F5 → data, Shift+F5 → metadata).
            var anyExecuted = HasExecutedInTransaction || HasExecutedInMetadataTransaction;
            foreach (var tab in WorkspaceTabs)
            {
                if (tab.Kind == WorkspaceTabKind.Query)
                {
                    tab.ShowActiveTransactionMarker = anyExecuted;
                    break;
                }
            }
        }

        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
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
