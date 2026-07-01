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
using EmberTern.Core.Security;
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
    private readonly FirebirdSecurityReader _securityReader;
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
        _securityReader = new FirebirdSecurityReader(_service, _metadataTransactionService);
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
        Metadata.NewObjectRequested += OnNewObjectRequested;
        Metadata.DeleteObjectRequested += OnDeleteObjectRequested;
        Metadata.ExecuteProcedureRequested += OnExecuteProcedureRequested;
        Metadata.RecompileGroupRequested += OnRecompileGroupRequested;
        Metadata.SetObjectActiveRequested += OnSetObjectActiveRequested;
        Metadata.BulkSetActiveRequested += OnBulkSetActiveRequested;
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
    [NotifyPropertyChangedFor(nameof(IsTriggerDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveTriggerDetail))]
    [NotifyPropertyChangedFor(nameof(IsFunctionDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveFunctionDetail))]
    [NotifyPropertyChangedFor(nameof(IsGeneratorDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveGeneratorDetail))]
    [NotifyPropertyChangedFor(nameof(IsDomainDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveDomainDetail))]
    [NotifyPropertyChangedFor(nameof(IsPackageDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(ActivePackageDetail))]
    [NotifyPropertyChangedFor(nameof(IsExceptionDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveExceptionDetail))]
    [NotifyPropertyChangedFor(nameof(IsIndexDetailTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveIndexDetail))]
    [NotifyPropertyChangedFor(nameof(IsSecurityManagerTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveSecurityManager))]
    [NotifyPropertyChangedFor(nameof(IsClosableTabActive))]
    [NotifyPropertyChangedFor(nameof(ShowEditorToolbar))]
    [NotifyPropertyChangedFor(nameof(ShowTransactionButtons))]
    [NotifyPropertyChangedFor(nameof(ShowDataTransactionButtons))]
    [NotifyPropertyChangedFor(nameof(ShowMetadataTransactionButtons))]
    [NotifyPropertyChangedFor(nameof(ShowQueryPanel))]
    [NotifyPropertyChangedFor(nameof(ActiveDdlText))]
    [NotifyPropertyChangedFor(nameof(ActiveTableDetail))]
    [NotifyPropertyChangedFor(nameof(ShowModeSection))]
    [NotifyPropertyChangedFor(nameof(ShowMainSection))]
    [NotifyPropertyChangedFor(nameof(ShowCollectionTools))]
    [NotifyPropertyChangedFor(nameof(ShowCollectionReorder))]
    [NotifyPropertyChangedFor(nameof(ShowHelperSection))]
    [NotifyPropertyChangedFor(nameof(ToolbarSep1Visible))]
    [NotifyPropertyChangedFor(nameof(ToolbarSep2Visible))]
    [NotifyPropertyChangedFor(nameof(ToolbarSep3Visible))]
    [NotifyPropertyChangedFor(nameof(ToolbarSep4Visible))]
    [NotifyCanExecuteChangedFor(nameof(AddCollectionItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCollectionItemCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCollectionItemUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveCollectionItemDownCommand))]
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
    /// <summary>True when the active workspace tab is a Trigger Detail tab.</summary>
    public bool IsTriggerDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TriggerDetail };
    public TriggerDetailTabViewModel? ActiveTriggerDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TriggerDetail } t ? t.TriggerDetail : null;
    /// <summary>True when the active workspace tab is a Function Detail tab.</summary>
    public bool IsFunctionDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.FunctionDetail };
    public FunctionDetailTabViewModel? ActiveFunctionDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.FunctionDetail } t ? t.FunctionDetail : null;
    /// <summary>True when the active workspace tab is a Generator Detail tab.</summary>
    public bool IsGeneratorDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.GeneratorDetail };
    public GeneratorDetailTabViewModel? ActiveGeneratorDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.GeneratorDetail } t ? t.GeneratorDetail : null;
    /// <summary>True when the active workspace tab is a Domain Detail tab.</summary>
    public bool IsDomainDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.DomainDetail };
    public DomainDetailTabViewModel? ActiveDomainDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.DomainDetail } t ? t.DomainDetail : null;
    /// <summary>True when the active workspace tab is a Package Detail tab.</summary>
    public bool IsPackageDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.PackageDetail };
    public PackageDetailTabViewModel? ActivePackageDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.PackageDetail } t ? t.PackageDetail : null;

    /// <summary>True when the active workspace tab is an Exception Detail tab.</summary>
    public bool IsExceptionDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.ExceptionDetail };
    public ExceptionDetailTabViewModel? ActiveExceptionDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.ExceptionDetail } t ? t.ExceptionDetail : null;

    /// <summary>True when the active workspace tab is an Index Detail tab.</summary>
    public bool IsIndexDetailTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.IndexDetail };
    public IndexDetailTabViewModel? ActiveIndexDetail
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.IndexDetail } t ? t.IndexDetail : null;

    /// <summary>True when the active workspace tab is a Security Manager tab.</summary>
    public bool IsSecurityManagerTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.SecurityManager };
    public SecurityManagerTabViewModel? ActiveSecurityManager
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.SecurityManager } t ? t.SecurityManager : null;

    // Drives the whole editor-toolbar Border's IsVisible so an empty command strip never
    // reserves space above the document tabs. True for every tab kind that exposes at
    // least one toolbar command; false when there is no active tab or the active tab has
    // no toolbar commands (currently only the Security Manager tab, which carries its own
    // in-view toolbar). Re-fires from the _selectedWorkspaceTab notify chain.
    public bool ShowEditorToolbar =>
        IsQueryTabActive
        || IsTableDetailTabActive
        || IsNewTableTabActive
        || IsViewDetailTabActive
        || IsProcedureDetailTabActive
        || IsTriggerDetailTabActive
        || IsFunctionDetailTabActive
        || IsGeneratorDetailTabActive
        || IsDomainDetailTabActive
        || IsPackageDetailTabActive
        || IsExceptionDetailTabActive
        || IsIndexDetailTabActive;
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
    // ProcedureDetail joins this set: Compile opens the working (metadata) transaction,
    // so Commit/Rollback must be reachable from those tabs too.
    // TriggerDetail is DELIBERATELY EXCLUDED: a trigger is never run manually and its
    // Compile auto-commits in an autonomous DDL tx (Phase A) — there's no working
    // transaction to Commit/Rollback from a trigger tab, so those buttons would be
    // misleading. The trigger toolbar is Compile + Format/Comment only.
    // FunctionDetail joins this set: Compile opens the working (metadata) transaction AND
    // Execute opens a data working transaction, so Commit/Rollback must be reachable.
    public bool ShowTransactionButtons => IsQueryTabActive || IsTableDetailTabActive || IsViewDetailTabActive || IsProcedureDetailTabActive || IsFunctionDetailTabActive;
    // Close-tab toolbar button targets *other* tabs (DDL, TableDetail, NewTable, ViewDetail, ProcedureDetail, TriggerDetail, FunctionDetail);
    // the anchored Query tab is never closable so the button hides when it's active.
    public bool IsClosableTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Ddl or WorkspaceTabKind.TableDetail or WorkspaceTabKind.NewTable or WorkspaceTabKind.ViewDetail or WorkspaceTabKind.ProcedureDetail or WorkspaceTabKind.TriggerDetail or WorkspaceTabKind.FunctionDetail or WorkspaceTabKind.GeneratorDetail or WorkspaceTabKind.DomainDetail or WorkspaceTabKind.PackageDetail or WorkspaceTabKind.ExceptionDetail or WorkspaceTabKind.IndexDetail };

    // ─── Unified editor toolbar — fixed 5-section model ───────────────────
    //
    // One toolbar, five sections in a fixed order for EVERY object editor:
    //   [ Mode ] | [ Main ] | [ Collection: + − | ↑ ↓ ] | [ Helper ] | [ Close ]
    // Each section + its leading separator collapse when empty for the active editor.
    // Section 3 routes Add/Remove/Move to the active editor's collection via the four
    // commands below; a future Trigger/Function/Package editor plugs a new case into
    // ActiveCollection() and gets the toolbar for free — no new layout pattern.

    // Section 1 — a mode toggle exists for Table (Grid-Edit), View + Procedure + Trigger + Function (Easy).
    public bool ShowModeSection => ShowFieldEditTools || IsViewDetailTabActive || IsProcedureDetailTabActive || IsTriggerDetailTabActive || IsFunctionDetailTabActive;
    // Section 2 — every editor has a primary action (Execute / Compile / Commit).
    public bool ShowMainSection => SelectedWorkspaceTab is not null;
    // Section 4 — helpers exist for SQL editor, View, Procedure, Trigger, Function, Package, and the Dane sub-tab.
    public bool ShowHelperSection => IsQueryTabActive || IsViewDetailTabActive || IsProcedureDetailTabActive || IsTriggerDetailTabActive || IsFunctionDetailTabActive || IsPackageDetailTabActive || IsDataTabActive;

    // A separator shows only between two non-empty adjacent sections.
    private bool HasFrom2 => ShowMainSection || ShowCollectionTools || ShowHelperSection || IsClosableTabActive;
    private bool HasFrom3 => ShowCollectionTools || ShowHelperSection || IsClosableTabActive;
    private bool HasFrom4 => ShowHelperSection || IsClosableTabActive;
    public bool ToolbarSep1Visible => ShowModeSection && HasFrom2;
    public bool ToolbarSep2Visible => ShowMainSection && HasFrom3;
    public bool ToolbarSep3Visible => ShowCollectionTools && HasFrom4;
    public bool ToolbarSep4Visible => ShowHelperSection && IsClosableTabActive;

    // Section 3 — collection-edit router. Resolves the active editor's Add/Remove/Move
    // commands (+ whether it supports ↑↓). null when no editable collection is active
    // (SQL editor, read-only system table, View/Procedure in Source mode, …).
    private (System.Windows.Input.ICommand Add, System.Windows.Input.ICommand Remove,
             System.Windows.Input.ICommand? Up, System.Windows.Input.ICommand? Down, bool Reorder)? ActiveCollection()
    {
        switch (SelectedWorkspaceTab?.Kind)
        {
            case WorkspaceTabKind.TableDetail when SelectedWorkspaceTab.TableDetail is { } t:
                if (ShowFieldEditTools)
                    return (t.AddFieldCommand, t.DropFieldCommand, t.MoveFieldUpCommand, t.MoveFieldDownCommand, true);
                if (ShowDataEditTools)
                    return (t.AddRowCommand, t.DeleteRowCommand, null, null, false);  // rows: no reorder
                return null;
            case WorkspaceTabKind.NewTable when SelectedWorkspaceTab.NewTable is { } n:
                return (n.AddFieldCommand, n.DeleteFieldCommand, n.MoveFieldUpCommand, n.MoveFieldDownCommand, true);
            case WorkspaceTabKind.ViewDetail when SelectedWorkspaceTab.ViewDetail is { EasyMode: true } v:
                return (v.AddColumnCommand, v.DeleteColumnCommand, v.MoveColumnUpCommand, v.MoveColumnDownCommand, true);
            case WorkspaceTabKind.ProcedureDetail when SelectedWorkspaceTab.ProcedureDetail is { EasyMode: true } p:
                return (p.AddCollectionItemCommand, p.RemoveCollectionItemCommand, p.MoveCollectionItemUpCommand, p.MoveCollectionItemDownCommand, true);
            case WorkspaceTabKind.TriggerDetail when SelectedWorkspaceTab.TriggerDetail is { EasyMode: true } tr:
                // Trigger Easy mode has a single editable collection — Variables.
                return (tr.AddVariableCommand, tr.DeleteVariableCommand, tr.MoveVariableUpCommand, tr.MoveVariableDownCommand, true);
            case WorkspaceTabKind.FunctionDetail when SelectedWorkspaceTab.FunctionDetail is { EasyMode: true } fn:
                // The Result sub-tab is a single record — no Add/Remove/Move there, so the
                // whole collection section is hidden; other sub-tabs route to the unified
                // commands (Arguments / Variables / Cursors / Subprograms).
                return fn.IsEasyCollectionEditable
                    ? (fn.AddCollectionItemCommand, fn.RemoveCollectionItemCommand, fn.MoveCollectionItemUpCommand, fn.MoveCollectionItemDownCommand, true)
                    : null;
            default:
                return null;
        }
    }

    public bool ShowCollectionTools => ActiveCollection() is not null;
    public bool ShowCollectionReorder => ActiveCollection() is { Reorder: true };

    private bool CanAddCollectionItem() => ActiveCollection()?.Add.CanExecute(null) ?? false;
    [RelayCommand(CanExecute = nameof(CanAddCollectionItem))]
    private void AddCollectionItem() => ActiveCollection()?.Add.Execute(null);

    private bool CanRemoveCollectionItem() => ActiveCollection()?.Remove.CanExecute(null) ?? false;
    [RelayCommand(CanExecute = nameof(CanRemoveCollectionItem))]
    private void RemoveCollectionItem() => ActiveCollection()?.Remove.Execute(null);

    private bool CanMoveCollectionItemUp() => ActiveCollection() is { Up: { } u } && u.CanExecute(null);
    [RelayCommand(CanExecute = nameof(CanMoveCollectionItemUp))]
    private void MoveCollectionItemUp() => ActiveCollection()?.Up?.Execute(null);

    private bool CanMoveCollectionItemDown() => ActiveCollection() is { Down: { } d } && d.CanExecute(null);
    [RelayCommand(CanExecute = nameof(CanMoveCollectionItemDown))]
    private void MoveCollectionItemDown() => ActiveCollection()?.Down?.Execute(null);

    // Re-raise all toolbar-section flags + router CanExecute. Called on tab change and
    // on the active editor's PropertyChanged (sub-tab / EasyMode / selection), so the
    // toolbar tracks the active editor's live state coarsely but correctly.
    private void RefreshToolbarSections()
    {
        OnPropertyChanged(nameof(ShowModeSection));
        OnPropertyChanged(nameof(ShowMainSection));
        OnPropertyChanged(nameof(ShowHelperSection));
        OnPropertyChanged(nameof(ShowCollectionTools));
        OnPropertyChanged(nameof(ShowCollectionReorder));
        OnPropertyChanged(nameof(ToolbarSep1Visible));
        OnPropertyChanged(nameof(ToolbarSep2Visible));
        OnPropertyChanged(nameof(ToolbarSep3Visible));
        OnPropertyChanged(nameof(ToolbarSep4Visible));
        AddCollectionItemCommand.NotifyCanExecuteChanged();
        RemoveCollectionItemCommand.NotifyCanExecuteChanged();
        MoveCollectionItemUpCommand.NotifyCanExecuteChanged();
        MoveCollectionItemDownCommand.NotifyCanExecuteChanged();
    }
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
        Diagnostics.ScrollTrace.Rebuild("ReloadConnections (RootNodes rebuilt)");
        // Detach old nodes before clearing so the service doesn't retain dead
        // subscribers via its ActiveConnectionChanged invocation list.
        foreach (var stale in Metadata.Connections)
        {
            stale.Detach();
        }
        Metadata.Connections.Clear();
        Metadata.RootNodes.Clear();
        // The name cache (filter + type-ahead) keys on the group VMs we're about to
        // discard — drop it so the next search rebuilds against the new tree.
        Metadata.InvalidateNameCache();

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
            ViewEasyMode = ViewEasyModePreference,
            TriggerEasyMode = TriggerEasyModePreference,
            FunctionEasyMode = FunctionEasyModePreference,
            BottomPanelTabIndex = SelectedBottomTabIndex,
            // ResultsMaximized is a layout flag owned by the View code-behind; it sets
            // it on the captured state in OnWindowClosing, like WindowBounds.
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
        ViewEasyModePreference = state.ViewEasyMode;
        TriggerEasyModePreference = state.TriggerEasyMode;
        FunctionEasyModePreference = state.FunctionEasyMode;
        SelectedBottomTabIndex = state.BottomPanelTabIndex;

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
            // The Security Manager is a live admin tool, reopened from the tree —
            // not persisted (it has no object DDL to fall back to).
            if (tab.Kind == WorkspaceTabKind.SecurityManager) continue;

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
                var td = tab.TableDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.TableDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = td is { } ? td.DdlText : tab.DdlText,
                    ActiveSubTabIndex = td?.ActiveSubTabIndex,
                    ActiveInnerSubTabIndex = td?.ConstraintsActiveSubTabIndex,
                    GridEditMode = td?.IsFieldEditMode,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.ViewDetail)
            {
                // Skip transient New View tabs (IsNew) — the view doesn't exist yet,
                // so restoring it would just fail to load. Persist real views as
                // ViewDetail so restore re-opens the full 6-tab surface (not DDL-only).
                if (tab.ViewDetail is { IsNew: true }) continue;
                var vd = tab.ViewDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.ViewDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = vd is { } ? vd.DdlText : tab.DdlText,
                    EasyMode = vd?.EasyMode,
                    ActiveSubTabIndex = vd?.ActiveSubTabIndex,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.ProcedureDetail)
            {
                // Skip transient New Procedure tabs (IsNew) — the procedure doesn't
                // exist yet. Persist real procedures as ProcedureDetail so restore
                // re-opens the full surface (not DDL-only).
                if (tab.ProcedureDetail is { IsNew: true }) continue;
                var pd = tab.ProcedureDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.ProcedureDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = pd is { } ? pd.DdlText : tab.DdlText,
                    EasyMode = pd?.EasyMode,
                    ActiveSubTabIndex = pd?.ActiveSubTabIndex,
                    ActiveInnerSubTabIndex = pd?.ActiveEasyCollectionIndex,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.TriggerDetail)
            {
                // Skip transient New Trigger tabs (IsNew) — the trigger doesn't exist
                // yet. Persist real triggers as TriggerDetail so restore re-opens the
                // full surface (not DDL-only).
                if (tab.TriggerDetail is { IsNew: true }) continue;
                var trd = tab.TriggerDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.TriggerDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = trd is { } ? trd.DdlText : tab.DdlText,
                    EasyMode = trd?.EasyMode,
                    ActiveSubTabIndex = trd?.ActiveSubTabIndex,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.FunctionDetail)
            {
                // Skip transient New Function tabs (IsNew) — the function doesn't exist
                // yet. Persist real functions as FunctionDetail so restore re-opens the
                // full surface (not DDL-only).
                if (tab.FunctionDetail is { IsNew: true }) continue;
                var fnd = tab.FunctionDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.FunctionDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = fnd is { } ? fnd.DdlText : tab.DdlText,
                    EasyMode = fnd?.EasyMode,
                    ActiveSubTabIndex = fnd?.ActiveSubTabIndex,
                    ActiveInnerSubTabIndex = fnd?.ActiveEasyCollectionIndex,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.GeneratorDetail)
            {
                // Skip transient New Generator tabs (IsNew) — the generator doesn't
                // exist yet. Persist real generators as GeneratorDetail so restore
                // re-opens the full surface (not DDL-only).
                if (tab.GeneratorDetail is { IsNew: true }) continue;
                var gd = tab.GeneratorDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.GeneratorDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = gd is { } ? gd.DdlText : tab.DdlText,
                    ActiveSubTabIndex = gd?.ActiveSubTabIndex,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.IndexDetail)
            {
                var ix = tab.IndexDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.IndexDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = ix is { } ? ix.DdlText : tab.DdlText,
                    ActiveSubTabIndex = ix?.ActiveSubTabIndex,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.DomainDetail)
            {
                // Skip transient New Domain tabs (IsNew) — the domain doesn't exist
                // yet. Persist real domains as DomainDetail so restore re-opens the
                // full surface (not DDL-only).
                if (tab.DomainDetail is { IsNew: true }) continue;
                var dd = tab.DomainDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.DomainDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = dd is { } ? dd.DdlText : tab.DdlText,
                    ActiveSubTabIndex = dd?.ActiveSubTabIndex,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.PackageDetail)
            {
                // Skip transient New Package tabs (IsNew) — the package doesn't exist
                // yet. Persist real packages as PackageDetail so restore re-opens the
                // full surface (not DDL-only).
                if (tab.PackageDetail is { IsNew: true }) continue;
                var pd = tab.PackageDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.PackageDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = pd is { } ? pd.DdlText : tab.DdlText,
                    ActiveSubTabIndex = pd?.ActiveSubTabIndex,
                });
            }
            else if (tab.Kind == WorkspaceTabKind.ExceptionDetail)
            {
                // Skip transient New Exception tabs (IsNew) — the exception doesn't
                // exist yet. Persist real exceptions as ExceptionDetail so restore
                // re-opens the full surface (not DDL-only).
                if (tab.ExceptionDetail is { IsNew: true }) continue;
                var ed = tab.ExceptionDetail;
                ws.Tabs.Add(new WorkspaceTab
                {
                    Kind = CoreTabKind.ExceptionDetail,
                    ObjectName = tab.ObjectName,
                    ObjectKind = tab.ObjectKind,
                    ConnectionProfileId = tab.ConnectionProfileId,
                    DdlText = ed is { } ? ed.DdlText : tab.DdlText,
                    ActiveSubTabIndex = ed?.ActiveSubTabIndex,
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
                // Per-tab UI state wins over defaults on a restored tab (hybrid model).
                if (tab.ActiveSubTabIndex is { } tdSub) detail.ActiveSubTabIndex = tdSub;
                if (tab.ActiveInnerSubTabIndex is { } tdInner) detail.ConstraintsActiveSubTabIndex = tdInner;
                if (tab.GridEditMode is { } tdEdit) detail.IsFieldEditMode = tdEdit;
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
                // Per-tab UI state wins over the global default (hybrid model). EasyMode
                // is guarded: OnEasyModeChanged no-ops on empty source, LoadAsync re-syncs.
                if (tab.EasyMode is { } vEasy && detail.CanUseEasyMode) detail.EasyMode = vEasy;
                if (tab.ActiveSubTabIndex is { } vSub) detail.ActiveSubTabIndex = vSub;
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
                // Per-tab UI state wins over the global default (hybrid model). EasyMode
                // is guarded: OnEasyModeChanged no-ops on empty source, LoadAsync re-syncs.
                if (tab.EasyMode is { } pEasy && detail.CanUseEasyMode) detail.EasyMode = pEasy;
                if (tab.ActiveSubTabIndex is { } pSub) detail.ActiveSubTabIndex = pSub;
                if (tab.ActiveInnerSubTabIndex is { } pInner) detail.ActiveEasyCollectionIndex = pInner;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateProcedureDetail(this, obj, detail, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.TriggerDetail
                  && tab.ObjectKind is { } trigKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Native TriggerDetail restore (no DDL-only fallback). Lazy-loads on
                // first activation via SelectTab. Cached DDL seeds the DDL tab.
                var obj = new MetadataObject(tab.ObjectName, trigKind);
                var detail = CreateTriggerDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                if (tab.EasyMode is { } trEasy && detail.CanUseEasyMode) detail.EasyMode = trEasy;
                if (tab.ActiveSubTabIndex is { } trSub) detail.ActiveSubTabIndex = trSub;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateTriggerDetail(this, obj, detail, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.FunctionDetail
                  && tab.ObjectKind is { } funcKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Native FunctionDetail restore (no DDL-only fallback). Lazy-loads on
                // first activation via SelectTab. Cached DDL seeds the DDL tab.
                var obj = new MetadataObject(tab.ObjectName, funcKind);
                var detail = CreateFunctionDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                if (tab.EasyMode is { } fnEasy && detail.CanUseEasyMode) detail.EasyMode = fnEasy;
                if (tab.ActiveSubTabIndex is { } fnSub) detail.ActiveSubTabIndex = fnSub;
                if (tab.ActiveInnerSubTabIndex is { } fnInner) detail.ActiveEasyCollectionIndex = fnInner;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateFunctionDetail(this, obj, detail, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.GeneratorDetail
                  && tab.ObjectKind is { } genKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Native GeneratorDetail restore (no DDL-only fallback). Lazy-loads on
                // first activation via SelectTab. Cached DDL seeds the DDL tab.
                var obj = new MetadataObject(tab.ObjectName, genKind);
                var detail = CreateGeneratorDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                if (tab.ActiveSubTabIndex is { } gnSub) detail.ActiveSubTabIndex = gnSub;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateGeneratorDetail(this, obj, detail, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.IndexDetail
                  && tab.ObjectKind is { } ixKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Native IndexDetail restore (no DDL-only fallback). Lazy-loads on
                // first activation via SelectTab. Cached DDL seeds the DDL tab.
                var obj = new MetadataObject(tab.ObjectName, ixKind);
                var detail = CreateIndexDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                if (tab.ActiveSubTabIndex is { } ixSub) detail.ActiveSubTabIndex = ixSub;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateIndexDetail(this, obj, detail, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.DomainDetail
                  && tab.ObjectKind is { } domKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Native DomainDetail restore (no DDL-only fallback). Lazy-loads on
                // first activation via SelectTab. Cached DDL seeds the DDL tab.
                var obj = new MetadataObject(tab.ObjectName, domKind);
                var detail = CreateDomainDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                if (tab.ActiveSubTabIndex is { } dmSub) detail.ActiveSubTabIndex = dmSub;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateDomainDetail(this, obj, detail, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.PackageDetail
                  && tab.ObjectKind is { } pkgKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Native PackageDetail restore (no DDL-only fallback). Lazy-loads on
                // first activation via SelectTab. Cached DDL seeds the DDL tab.
                var obj = new MetadataObject(tab.ObjectName, pkgKind);
                var detail = CreatePackageDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                if (tab.ActiveSubTabIndex is { } pkSub) detail.ActiveSubTabIndex = pkSub;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreatePackageDetail(this, obj, detail, tab.ConnectionProfileId));
            }
            else if (tab.Kind == CoreTabKind.ExceptionDetail
                  && tab.ObjectKind is { } excKind
                  && !string.IsNullOrEmpty(tab.ObjectName))
            {
                // Native ExceptionDetail restore (no DDL-only fallback). Lazy-loads on
                // first activation via SelectTab. Cached DDL seeds the DDL tab.
                var obj = new MetadataObject(tab.ObjectName, excKind);
                var detail = CreateExceptionDetail(obj);
                detail.DdlText = tab.DdlText ?? string.Empty;
                if (tab.ActiveSubTabIndex is { } exSub) detail.ActiveSubTabIndex = exSub;
                WorkspaceTabs.Add(WorkspaceTabViewModel.CreateExceptionDetail(this, obj, detail, tab.ConnectionProfileId));
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
        // WorkGuard: active transaction → Commit / Roll back / Cancel (default Roll
        // back); uncompiled tab work with no tx → discard confirm. The guard settles
        // the chosen transaction lanes before we return true.
        if (!await ConfirmDisconnectAsync().ConfigureAwait(true)) return;

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

    // Multi-outcome (N-button) sibling of ConfirmationRequested — Commit / Roll back /
    // Cancel (disconnect) and Cancel / Discard-and-exit (app close). Returns the chosen
    // ChoiceOption.Id or null when dismissed; with no handler (tests) → null = cancel,
    // which is the safe default (the guard branches that use it only fire with a live
    // transaction, which tests never have).
    public event Func<ChoiceRequest, Task<string?>>? ChoiceRequested;
    private Task<string?> RequestChoiceAsync(ChoiceRequest request)
        => ChoiceRequested?.Invoke(request) ?? Task.FromResult<string?>(null);

    // ─── Data-loss WorkGuard ───────────────────────────────────────────────
    //
    // One aggregation feeding three entry points: tab close (RequestCloseTabAsync),
    // disconnect (ConfirmDisconnectAsync), and app close (TryCloseApplicationAsync).
    // Unsaved CODE work (uncompiled new objects / modified source / queued structural
    // changes) lives only in the open tabs; transactions live on the server and can't
    // survive a restart, so they always need a conscious Commit/Roll-back decision.

    // Unsaved-work descriptors across the currently-open tabs (the active connection's).
    // Other connections' tabs are stashed serialized and hold no live uncompiled source.
    internal IReadOnlyList<UnsavedWorkItem> CollectUnsavedWork()
    {
        var items = new List<UnsavedWorkItem>();
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.UnsavedWork is { } item) items.Add(item);
        }
        return items;
    }

    private bool AnyTransactionActive
        => _transactionService.IsActive || _metadataTransactionService.IsActive;

    private void AppendActiveTransactionLines(System.Text.StringBuilder sb)
    {
        if (_transactionService.IsActive)
        {
            sb.AppendLine("  • " + string.Format(CultureInfo.CurrentCulture,
                UiStrings.UnsavedTransactionDataFormat, _transactionService.StatementCount));
        }
        if (_metadataTransactionService.IsActive && _service.MetadataIsIndependent)
        {
            sb.AppendLine("  • " + string.Format(CultureInfo.CurrentCulture,
                UiStrings.UnsavedTransactionMetadataFormat, _metadataTransactionService.StatementCount));
        }
    }

    /// <summary>
    /// App-close guard. Returns true if the app may close. Active transactions can't be
    /// saved across a restart, so they require a conscious decision now (rolled back on
    /// exit); uncompiled tab work is listed and lost on Discard (Increment 2 / auto-draft
    /// will make it survive, at which point only transactions block exit). Default/Esc =
    /// Cancel = stay open.
    /// </summary>
    public async Task<bool> TryCloseApplicationAsync()
    {
        var txActive = AnyTransactionActive;
        var unsaved = CollectUnsavedWork();
        if (!txActive && unsaved.Count == 0) return true;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(UiStrings.ExitUnsavedIntro);
        AppendActiveTransactionLines(sb);
        foreach (var it in unsaved) sb.AppendLine("  • " + it.Label);
        if (txActive)
        {
            sb.AppendLine();
            sb.Append(UiStrings.ExitUnsavedTransactionNote);
        }

        var id = await RequestChoiceAsync(new ChoiceRequest
        {
            Title = UiStrings.ExitUnsavedTitle,
            Message = sb.ToString().TrimEnd(),
            Options = new[]
            {
                new ChoiceOption { Id = "cancel", Label = UiStrings.ExitUnsavedCancel, IsDefault = true, IsCancel = true },
                new ChoiceOption { Id = "discard", Label = UiStrings.ExitUnsavedDiscard, IsDestructive = true },
            },
        }).ConfigureAwait(true);

        if (id != "discard") return false;
        if (txActive) await RollbackAllAsync().ConfigureAwait(true);
        return true;
    }

    // Disconnect guard. Returns true if the disconnect may proceed (after settling
    // transactions per the user's pick), false if cancelled.
    private async Task<bool> ConfirmDisconnectAsync()
    {
        var unsaved = CollectUnsavedWork();

        if (AnyTransactionActive)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(CultureInfo.CurrentCulture,
                UiStrings.DisconnectChoiceHeaderFormat, _service.ActiveProfile?.Name ?? string.Empty));
            AppendActiveTransactionLines(sb);
            if (unsaved.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format(CultureInfo.CurrentCulture,
                    UiStrings.DisconnectUnsavedDiscardNoteFormat, unsaved.Count));
            }
            sb.AppendLine();
            sb.Append(UiStrings.DisconnectChoiceQuestion);

            var id = await RequestChoiceAsync(new ChoiceRequest
            {
                Title = UiStrings.DisconnectChoiceTitle,
                Message = sb.ToString(),
                Options = new[]
                {
                    new ChoiceOption { Id = "commit", Label = UiStrings.DisconnectChoiceCommit },
                    new ChoiceOption { Id = "rollback", Label = UiStrings.DisconnectChoiceRollback, IsDefault = true },
                    new ChoiceOption { Id = "cancel", Label = UiStrings.DisconnectChoiceCancel, IsCancel = true },
                },
            }).ConfigureAwait(true);

            if (id is null or "cancel") return false;
            if (id == "commit") await CommitAllAsync().ConfigureAwait(true);
            else await RollbackAllAsync().ConfigureAwait(true);
            return true;
        }

        // No transaction, but uncompiled tab work would be lost (Increment 1 — no
        // drafts yet). Binary discard confirm.
        if (unsaved.Count > 0)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(UiStrings.DisconnectUnsavedIntro);
            foreach (var it in unsaved) sb.AppendLine("  • " + it.Label);
            return await RequestConfirmAsync(new ConfirmRequest
            {
                Title = UiStrings.DisconnectUnsavedTitle,
                Message = sb.ToString().TrimEnd(),
                ConfirmLabel = UiStrings.DisconnectUnsavedYes,
                CancelLabel = UiStrings.DialogCancel,
                IsDestructive = true,
            }).ConfigureAwait(true);
        }

        return true;
    }

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
        // Merged Domena/Kolumna picker: lazy column loader for the Table-column tab.
        newTableVm.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));

        // Best-effort fetch of available domains so the in-cell Domain combo has
        // something to offer. Failure surfaces as an empty list — non-fatal.
        try
        {
            var domains = await _metadataReader.ListDomainsAsync().ConfigureAwait(true);
            newTableVm.SetAvailableDomains(domains);
        }
        catch (MetadataReadException) { /* best effort */ }
        // Best-effort table list for the Table-column (TYPE OF COLUMN) tab.
        await LoadTableListAsync(newTableVm.SetAvailableTables).ConfigureAwait(true);

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
        // Start in Easy mode (approved target design): SourceText (the template) is
        // already set, so the toggle parses it into the editable name + column list +
        // body. The user can flip to Source at any time.
        detail.EasyMode = true;
        // Seeding the template marked the VM dirty; a brand-new untouched tab must not
        // prompt on close — clear it so only real edits flip it back.
        detail.ClearDirty();

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

    public bool CanCreateGenerator => _service.IsConnected;

    // New Generator: opens a Generator Detail tab in IsNew mode with an editable
    // name + default values (initial 0, increment 1). On Save success
    // OnGeneratorCreated refreshes the tree, closes this tab and reopens the real
    // generator.
    [RelayCommand(CanExecute = nameof(CanCreateGenerator))]
    private void NewGenerator()
    {
        var detail = new GeneratorDetailTabViewModel(UiStrings.NewGeneratorTabDefaultTitle, _tableDetailReader, _ddlReader, _ddlExecutor)
        {
            IsNew = true,
            EditableName = string.Empty,
            InitialValue = 0,
            Increment = 1,
            CurrentValue = 0,
        };
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.GeneratorCreated += name => OnGeneratorCreated(detail, name);
        // Seeding the defaults marked the VM dirty; a brand-new untouched tab must
        // not prompt on close — clear it so only real edits flip it back.
        detail.ClearDirty();

        var obj = new MetadataObject(UiStrings.NewGeneratorTabDefaultTitle, MetadataObjectKind.Generator);
        var tab = WorkspaceTabViewModel.CreateGeneratorDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private async void OnGeneratorCreated(GeneratorDetailTabViewModel detail, string? generatorName)
    {
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.NewGeneratorExecutedFormat, generatorName ?? string.Empty));
        await Metadata.RefreshAsync().ConfigureAwait(true);

        WorkspaceTabViewModel? newTab = null;
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.GeneratorDetail && ReferenceEquals(t.GeneratorDetail, detail))
            {
                newTab = t;
                break;
            }
        }
        if (newTab is not null) CloseTab(newTab);

        if (!string.IsNullOrEmpty(generatorName))
        {
            OnOpenDdlRequested(new MetadataObject(generatorName, MetadataObjectKind.Generator));
        }
    }

    public bool CanCreateDomain => _service.IsConnected;

    // New Domain: opens a Domain Detail tab in IsNew mode with an editable name +
    // default type (VARCHAR). On Save success OnDomainCreated refreshes the tree,
    // closes this tab and reopens the real domain.
    [RelayCommand(CanExecute = nameof(CanCreateDomain))]
    private void NewDomain()
    {
        var detail = new DomainDetailTabViewModel(UiStrings.NewDomainTabDefaultTitle, _tableDetailReader, _ddlExecutor)
        {
            IsNew = true,
            EditableName = string.Empty,
            DataType = "VARCHAR",
            Length = 50,
        };
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.DomainCreated += name => OnDomainCreated(detail, name);
        // Seeding the defaults marked the VM dirty; a brand-new untouched tab must
        // not prompt on close — clear it so only real edits flip it back.
        detail.ClearDirty();

        var obj = new MetadataObject(UiStrings.NewDomainTabDefaultTitle, MetadataObjectKind.Domain);
        var tab = WorkspaceTabViewModel.CreateDomainDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private async void OnDomainCreated(DomainDetailTabViewModel detail, string? domainName)
    {
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.NewDomainExecutedFormat, domainName ?? string.Empty));
        await Metadata.RefreshAsync().ConfigureAwait(true);

        WorkspaceTabViewModel? newTab = null;
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.DomainDetail && ReferenceEquals(t.DomainDetail, detail))
            {
                newTab = t;
                break;
            }
        }
        if (newTab is not null) CloseTab(newTab);

        if (!string.IsNullOrEmpty(domainName))
        {
            OnOpenDdlRequested(new MetadataObject(domainName, MetadataObjectKind.Domain));
        }
    }

    public bool CanCreatePackage => _service.IsConnected;

    // New Package: opens a Package Detail tab in IsNew mode seeded with header + body
    // templates (the user edits the SQL directly — no designer). On Compile success
    // OnPackageCreated refreshes the tree, closes this tab and reopens the real package.
    [RelayCommand(CanExecute = nameof(CanCreatePackage))]
    private void NewPackage()
    {
        var detail = new PackageDetailTabViewModel(UiStrings.NewPackageTabDefaultTitle, _tableDetailReader, _ddlReader, _ddlExecutor)
        {
            IsNew = true,
            HeaderSource = PackageDetailTabViewModel.NewPackageHeaderTemplate,
            BodySource = PackageDetailTabViewModel.NewPackageBodyTemplate,
        };
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.PackageCreated += name => OnPackageCreated(detail, name);
        // Seeding the templates marked the VM dirty; a brand-new untouched tab must
        // not prompt on close — clear it so only real edits flip it back.
        detail.ClearDirty();

        var obj = new MetadataObject(UiStrings.NewPackageTabDefaultTitle, MetadataObjectKind.Package);
        var tab = WorkspaceTabViewModel.CreatePackageDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private async void OnPackageCreated(PackageDetailTabViewModel detail, string? packageName)
    {
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.NewPackageExecutedFormat, packageName ?? string.Empty));
        await Metadata.RefreshAsync().ConfigureAwait(true);

        WorkspaceTabViewModel? newTab = null;
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.PackageDetail && ReferenceEquals(t.PackageDetail, detail))
            {
                newTab = t;
                break;
            }
        }
        if (newTab is not null) CloseTab(newTab);

        if (!string.IsNullOrEmpty(packageName))
        {
            OnOpenDdlRequested(new MetadataObject(packageName, MetadataObjectKind.Package));
        }
    }

    public bool CanCreateException => _service.IsConnected;

    // New Exception: opens an Exception Detail tab in IsNew mode with an editable
    // name + empty message. On Compile success OnExceptionCreated refreshes the tree,
    // closes this tab and reopens the real exception.
    [RelayCommand(CanExecute = nameof(CanCreateException))]
    private void NewException()
    {
        var detail = new ExceptionDetailTabViewModel(UiStrings.NewExceptionTabDefaultTitle, _tableDetailReader, _ddlExecutor)
        {
            IsNew = true,
            EditableName = string.Empty,
        };
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.ExceptionCreated += name => OnExceptionCreated(detail, name);
        // A brand-new untouched tab must not prompt on close.
        detail.ClearDirty();

        var obj = new MetadataObject(UiStrings.NewExceptionTabDefaultTitle, MetadataObjectKind.Exception);
        var tab = WorkspaceTabViewModel.CreateExceptionDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private async void OnExceptionCreated(ExceptionDetailTabViewModel detail, string? exceptionName)
    {
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.NewExceptionExecutedFormat, exceptionName ?? string.Empty));
        await Metadata.RefreshAsync().ConfigureAwait(true);

        WorkspaceTabViewModel? newTab = null;
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.ExceptionDetail && ReferenceEquals(t.ExceptionDetail, detail))
            {
                newTab = t;
                break;
            }
        }
        if (newTab is not null) CloseTab(newTab);

        if (!string.IsNullOrEmpty(exceptionName))
        {
            OnOpenDdlRequested(new MetadataObject(exceptionName, MetadataObjectKind.Exception));
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
        detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
        _ = LoadProcedureListsAsync(detail);
        detail.ObjectCreated += name => OnProcedureCreated(detail, name);
        // Start in Easy mode (approved target design): the template SourceText is parsed
        // into the editable name + Input/Output params + Variables/Cursors/Subprograms +
        // body. The user can flip to Source at any time.
        detail.EasyMode = true;
        // Seeding the template marked the VM dirty; a brand-new untouched tab must not
        // prompt on close — clear it so only real edits flip it back.
        detail.ClearDirty();

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

    public bool CanCreateTrigger => _service.IsConnected;

    // New Trigger: opens a Trigger Detail tab in IsNew mode, starting in Easy mode with
    // empty metadata. The user picks the table + events (the name auto-derives) and an
    // empty body, then presses Compile; on success OnTriggerCreated refreshes the tree,
    // closes this tab and reopens the real trigger.
    [RelayCommand(CanExecute = nameof(CanCreateTrigger))]
    private void NewTrigger()
    {
        var detail = new TriggerDetailTabViewModel(UiStrings.NewTriggerTabDefaultTitle, _tableDetailReader, _ddlReader, _ddlExecutor)
        {
            IsNew = true,
        };
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
        _ = LoadTriggerListsAsync(detail);
        // Sensible defaults: BEFORE INSERT, an empty body. The name auto-derives once
        // the user picks a table.
        detail.FiresInsert = true;
        detail.ExecutableBody = "BEGIN\nEND";
        detail.EasyMode = true;
        detail.ObjectCreated += name => OnTriggerCreated(detail, name);
        // Seeding marked the VM dirty; a brand-new untouched tab must not prompt on close.
        detail.ClearDirty();

        var obj = new MetadataObject(UiStrings.NewTriggerTabDefaultTitle, MetadataObjectKind.Trigger);
        var tab = WorkspaceTabViewModel.CreateTriggerDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private async void OnTriggerCreated(TriggerDetailTabViewModel detail, string? triggerName)
    {
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.NewTriggerExecutedFormat, triggerName ?? string.Empty));
        await Metadata.RefreshAsync().ConfigureAwait(true);

        WorkspaceTabViewModel? newTab = null;
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.TriggerDetail && ReferenceEquals(t.TriggerDetail, detail))
            {
                newTab = t;
                break;
            }
        }
        if (newTab is not null) CloseTab(newTab);

        if (!string.IsNullOrEmpty(triggerName))
        {
            OnOpenDdlRequested(new MetadataObject(triggerName, MetadataObjectKind.Trigger));
        }
    }

    public bool CanCreateFunction => _service.IsConnected;

    // New Function: opens a Function Detail tab in IsNew mode, starting in Easy mode with
    // the CREATE OR ALTER FUNCTION template parsed into the editable name + Arguments +
    // Result + body. On Compile success OnFunctionCreated refreshes the tree, closes this
    // tab and reopens the real function.
    [RelayCommand(CanExecute = nameof(CanCreateFunction))]
    private void NewFunction()
    {
        var detail = new FunctionDetailTabViewModel(UiStrings.NewFunctionTabDefaultTitle, _tableDetailReader, _ddlReader, _ddlExecutor)
        {
            IsNew = true,
            SourceText = FunctionDetailTabViewModel.NewFunctionTemplate,
        };
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.RunExecuteRequested = RunFunctionExecuteAsync;
        detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
        _ = LoadFunctionListsAsync(detail);
        detail.ObjectCreated += name => OnFunctionCreated(detail, name);
        detail.EasyMode = true;
        // Seeding marked the VM dirty; a brand-new untouched tab must not prompt on close.
        detail.ClearDirty();

        var obj = new MetadataObject(UiStrings.NewFunctionTabDefaultTitle, MetadataObjectKind.Function);
        var tab = WorkspaceTabViewModel.CreateFunctionDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private async void OnFunctionCreated(FunctionDetailTabViewModel detail, string? functionName)
    {
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.NewFunctionExecutedFormat, functionName ?? string.Empty));
        await Metadata.RefreshAsync().ConfigureAwait(true);

        WorkspaceTabViewModel? newTab = null;
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.FunctionDetail && ReferenceEquals(t.FunctionDetail, detail))
            {
                newTab = t;
                break;
            }
        }
        if (newTab is not null) CloseTab(newTab);

        if (!string.IsNullOrEmpty(functionName))
        {
            OnOpenDdlRequested(new MetadataObject(functionName, MetadataObjectKind.Function));
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
                // AllLeaves (not Children) so a filtered tree doesn't shrink the
                // autocomplete / name-resolution set.
                foreach (var leaf in group.AllLeaves)
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

    // Triggers open in the dedicated Trigger Detail surface (editable CREATE OR ALTER
    // TRIGGER source + Compile / Description / Dependencies / DDL, with Easy mode =
    // metadata header + Variables grid over a body editor), not a plain DDL tab.
    internal static bool OpensAsTriggerDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Trigger;

    // Functions open in the dedicated Function Detail surface (editable CREATE OR ALTER
    // FUNCTION source + Compile / Execute / Description / Dependencies / DDL, with Easy
    // mode = Arguments + single Result + Variables/Cursors/Subprograms over a body
    // editor), not a plain DDL tab. Separate predicate — it builds its own detail VM.
    internal static bool OpensAsFunctionDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Function;

    // Generators open in the dedicated Generator Detail surface (the editable
    // Generator form + Dependencies + DDL), not a plain DDL tab. Separate
    // predicate — it builds its own (form-based, no PSQL body) detail VM.
    internal static bool OpensAsGeneratorDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Generator;

    // Domains open in the dedicated Domain Detail surface (the definition form +
    // Description + Used By + DDL), not a plain DDL tab. Separate predicate — it
    // builds its own (form-based, no PSQL body) detail VM.
    internal static bool OpensAsDomainDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Domain;

    // Packages open in the dedicated Package Detail surface (editable header + body
    // source editors + Members + Description + Dependencies + DDL), not a plain DDL
    // tab. Separate predicate — it builds its own (two-source, no Easy mode) detail VM.
    internal static bool OpensAsPackageDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Package;

    // Exceptions open in the dedicated Exception Detail surface (the editable form
    // [name + message] + Description + Dependencies + DDL), not a plain DDL tab.
    // Separate predicate — it builds its own (form-based, no PSQL body) detail VM.
    internal static bool OpensAsExceptionDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Exception;

    // Indexes open in the dedicated Index Detail surface (the read-mostly properties
    // form + DDL), not a plain DDL tab. Separate predicate — it builds its own
    // (form-based, no PSQL body) detail VM. Index CREATION stays in Table Detail →
    // Indexes; there is no New-Index flow here.
    internal static bool OpensAsIndexDetail(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Index;

    // Users and roles open in the unified Security Manager (Users / Roles /
    // Membership / Privileges). Not a singleton — a tab is keyed by the context
    // object (Kind, Name); the context only sets the initial sub-tab + selection.
    internal static bool OpensAsSecurityManager(MetadataObjectKind kind)
        => kind is MetadataObjectKind.User or MetadataObjectKind.Role;

    // Single construction point for ViewDetail VMs — mirrors CreateTableDetail.
    // A view is read-only data (no inline editing) but its SQL source IS editable,
    // so the DDL executor is wired for Compile while no data editor is.
    // Last-used View Detail editor mode (false = Source, true = Easy), mirrored to
    // WorkspaceState.ViewEasyMode. Hybrid model: applied to each newly opened existing
    // view; a workspace-restored tab overrides it with its own per-tab value.
    internal bool ViewEasyModePreference { get; set; }

    internal ViewDetailTabViewModel CreateViewDetail(MetadataObject obj)
    {
        var detail = new ViewDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        // Restore the remembered mode (existing views only — New View sets Easy after).
        if (detail.CanUseEasyMode) detail.EasyMode = ViewEasyModePreference;
        detail.PropertyChanged += OnViewDetailPropertyChanged;
        return detail;
    }

    private void OnViewDetailPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewDetailTabViewModel.EasyMode)
            && sender is ViewDetailTabViewModel { CanUseEasyMode: true } d)
        {
            ViewEasyModePreference = d.EasyMode;
        }
    }

    // Single construction point for GeneratorDetail VMs — mirrors CreateViewDetail.
    // The generator form persists via Save (DDL executor wired); there is no Easy
    // mode, no data editor.
    internal GeneratorDetailTabViewModel CreateGeneratorDetail(MetadataObject obj)
    {
        var detail = new GeneratorDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.DeleteRequested += OnGeneratorDeleteRequested;
        return detail;
    }

    private async Task OnGeneratorDeleteRequested(GeneratorDetailTabViewModel detail)
    {
        try
        {
            await _ddlExecutor.ExecuteAsync(DdlGenerator.BuildDropSequence(detail.GeneratorName)).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }
        catch (InvalidOperationException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }

        // Close the tab for this generator, then refresh the tree so it disappears.
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.GeneratorDetail && ReferenceEquals(t.GeneratorDetail, detail))
            {
                CloseTab(t);
                break;
            }
        }
        await Metadata.RefreshAsync().ConfigureAwait(true);
    }

    // Single construction point for IndexDetail VMs — mirrors CreateGeneratorDetail.
    // An index is read-mostly: Compile applies Active / Description; Delete drops it
    // (non-constraint indexes only). No Easy mode, no data editor, no New flow.
    internal IndexDetailTabViewModel CreateIndexDetail(MetadataObject obj)
    {
        var detail = new IndexDetailTabViewModel(obj.Name, _tableDetailReader, _ddlExecutor);
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.DeleteRequested += OnIndexDeleteRequested;
        return detail;
    }

    private async Task OnIndexDeleteRequested(IndexDetailTabViewModel detail)
    {
        try
        {
            await _ddlExecutor.ExecuteAsync(DdlGenerator.BuildDropIndex(detail.IndexName)).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }
        catch (InvalidOperationException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }

        // Close the tab for this index, then refresh the tree so it disappears.
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.IndexDetail && ReferenceEquals(t.IndexDetail, detail))
            {
                CloseTab(t);
                break;
            }
        }
        await Metadata.RefreshAsync().ConfigureAwait(true);
    }

    // Single construction point for DomainDetail VMs — mirrors CreateGeneratorDetail.
    // The domain form persists via Save (DDL executor wired); there is no Easy mode,
    // no data editor.
    internal DomainDetailTabViewModel CreateDomainDetail(MetadataObject obj)
    {
        var detail = new DomainDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.DeleteRequested += OnDomainDeleteRequested;
        detail.RenameReopenRequested += newName => OnDomainRenamed(detail, newName);
        return detail;
    }

    // ALTER DOMAIN … TO … renames the object, so the tab (keyed on the old name) must
    // close and reopen under the new name — same shape as the New-domain reopen.
    private async void OnDomainRenamed(DomainDetailTabViewModel detail, string newName)
    {
        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.DomainRenamedFormat, newName));
        await Metadata.RefreshAsync().ConfigureAwait(true);

        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.DomainDetail && ReferenceEquals(t.DomainDetail, detail))
            {
                CloseTab(t);
                break;
            }
        }

        if (!string.IsNullOrEmpty(newName))
        {
            OnOpenDdlRequested(new MetadataObject(newName, MetadataObjectKind.Domain));
        }
    }

    private async Task OnDomainDeleteRequested(DomainDetailTabViewModel detail)
    {
        try
        {
            await _ddlExecutor.ExecuteAsync(DdlGenerator.BuildDropDomain(detail.DomainName)).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }
        catch (InvalidOperationException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }

        // Close the tab for this domain, then refresh the tree so it disappears.
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.DomainDetail && ReferenceEquals(t.DomainDetail, detail))
            {
                CloseTab(t);
                break;
            }
        }
        await Metadata.RefreshAsync().ConfigureAwait(true);
    }

    // Single construction point for PackageDetail VMs — mirrors CreateViewDetail.
    // A package's header + body source are editable (DDL executor wired for Compile);
    // there is no Easy mode and no data editor. Autocomplete on the editors comes from
    // SqlEditorBehavior (wired in the view against this MainWindowViewModel), so no
    // per-VM domain/table list is needed.
    internal PackageDetailTabViewModel CreatePackageDetail(MetadataObject obj)
    {
        var detail = new PackageDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.DeleteRequested += OnPackageDeleteRequested;
        return detail;
    }

    private async Task OnPackageDeleteRequested(PackageDetailTabViewModel detail)
    {
        try
        {
            await _ddlExecutor.ExecuteAsync(DdlGenerator.BuildDropPackage(detail.PackageName)).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }
        catch (InvalidOperationException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }

        // Close the tab for this package, then refresh the tree so it disappears.
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.PackageDetail && ReferenceEquals(t.PackageDetail, detail))
            {
                CloseTab(t);
                break;
            }
        }
        await Metadata.RefreshAsync().ConfigureAwait(true);
    }

    // Single construction point for ExceptionDetail VMs — mirrors CreateGeneratorDetail.
    // The exception form persists via Compile (DDL executor wired); there is no Easy
    // mode, no data editor.
    internal ExceptionDetailTabViewModel CreateExceptionDetail(MetadataObject obj)
    {
        var detail = new ExceptionDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.DeleteRequested += OnExceptionDeleteRequested;
        return detail;
    }

    // ─── Security Manager ──────────────────────────────────────────────────

    // View-fulfilled dialogs (MainWindow.axaml.cs owns the windows). Shared by the
    // Security Manager's own Add buttons AND the tree's Add user/role actions, so
    // dialog hosting lives in exactly one place.
    public event Func<UserInfo?, Task<UserEditResult?>>? UserEditDialogRequested;
    public event Func<Task<string?>>? NewRoleDialogRequested;

    internal Task<UserEditResult?> RequestUserEditDialogAsync(UserInfo? existing)
        => UserEditDialogRequested?.Invoke(existing) ?? Task.FromResult<UserEditResult?>(null);
    internal Task<string?> RequestNewRoleDialogAsync()
        => NewRoleDialogRequested?.Invoke() ?? Task.FromResult<string?>(null);

    internal SecurityManagerTabViewModel CreateSecurityManager(MetadataObject? context)
    {
        var manager = new SecurityManagerTabViewModel(_securityReader, _metadataReader, _ddlExecutor, context);
        manager.ConfirmationRequested += RequestConfirmAsync;
        manager.UserEditRequested += RequestUserEditDialogAsync;
        manager.NewRoleRequested += RequestNewRoleDialogAsync;
        return manager;
    }

    public bool CanOpenSecurityManager => _service.IsConnected;

    // Central Security Manager button (main toolbar): opens a context-less manager
    // (Users tab). Re-opens/focuses the context-less tab if one is already open.
    [RelayCommand(CanExecute = nameof(CanOpenSecurityManager))]
    private async Task OpenSecurityManager() => await OpenSecurityManagerAsync(null).ConfigureAwait(true);

    private async Task OpenSecurityManagerAsync(MetadataObject? context)
    {
        // Dedup by context identity (Kind, Name) — repeat opens of the same user/role
        // focus the existing tab; different contexts coexist (not a singleton).
        var ctxKind = context?.Kind;
        var ctxName = context?.Name ?? string.Empty;
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind == WorkspaceTabKind.SecurityManager
                && tab.ObjectKind == ctxKind
                && string.Equals(tab.ObjectName, ctxName, StringComparison.Ordinal))
            {
                SelectTab(tab);
                return;
            }
        }

        var manager = CreateSecurityManager(context);
        var newTab = WorkspaceTabViewModel.CreateSecurityManager(this, manager, context, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(newTab);
        SelectTab(newTab);
        await manager.EnsureLoadedAsync().ConfigureAwait(true);
        if (!string.IsNullOrEmpty(manager.ErrorMessage))
        {
            AddMessage(MessageSeverity.Error, manager.ErrorMessage);
            SelectedBottomTabIndex = 1;
        }
    }

    // Tree "Add user…": a standalone create-user form, then CREATE USER + tree
    // refresh (no Security Manager tab opens — matches the spec).
    private async void OnNewUserRequestedFromTree()
    {
        var result = await RequestUserEditDialogAsync(null).ConfigureAwait(true);
        if (result is null) return;
        var sql = SecurityDdlGenerator.BuildCreateUser(result.User, result.Password);
        if (!string.IsNullOrWhiteSpace(result.User.Description))
            sql += ";\n" + SecurityDdlGenerator.BuildCommentUser(result.User.UserName, result.User.Description);
        await ExecuteSecurityDdlAndRefreshTreeAsync(sql).ConfigureAwait(true);
    }

    // Tree "Add role…": a small name dialog, then CREATE ROLE + tree refresh
    // (IBExpert behaviour — nothing opens).
    private async void OnNewRoleRequestedFromTree()
    {
        var name = await RequestNewRoleDialogAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        await ExecuteSecurityDdlAndRefreshTreeAsync(SecurityDdlGenerator.BuildCreateRole(name)).ConfigureAwait(true);
    }

    private async Task ExecuteSecurityDdlAndRefreshTreeAsync(string sql)
    {
        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
            await Metadata.RefreshAsync().ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
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

    private async Task OnExceptionDeleteRequested(ExceptionDetailTabViewModel detail)
    {
        try
        {
            await _ddlExecutor.ExecuteAsync(DdlGenerator.BuildDropException(detail.ExceptionName)).ConfigureAwait(true);
        }
        catch (DdlExecutionException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }
        catch (InvalidOperationException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }

        // Close the tab for this exception, then refresh the tree so it disappears.
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.ExceptionDetail && ReferenceEquals(t.ExceptionDetail, detail))
            {
                CloseTab(t);
                break;
            }
        }
        await Metadata.RefreshAsync().ConfigureAwait(true);
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
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.RunExecuteRequested = RunProcedureExecuteAsync;
        // Lazy column loader for the Variables grid's merged Domain/Column picker.
        detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
        // Best-effort domain + table lists for the Variables grid (Easy mode).
        _ = LoadProcedureListsAsync(detail);
        // Restore the remembered mode (existing procedures only — New stays Source).
        if (detail.CanUseEasyMode) detail.EasyMode = ProcedureEasyModePreference;
        detail.PropertyChanged += OnProcedureDetailPropertyChanged;
        return detail;
    }

    private async Task LoadProcedureListsAsync(ProcedureDetailTabViewModel detail)
    {
        try
        {
            var domains = await _metadataReader.ListDomainsAsync().ConfigureAwait(true);
            detail.SetAvailableDomains(domains);
        }
        catch (MetadataReadException) { /* best effort — Domain tab just stays empty */ }
        try
        {
            var tables = await _metadataReader.ListAsync(MetadataObjectKind.Table).ConfigureAwait(true);
            detail.SetAvailableTables(tables.Select(t => t.Name));
        }
        catch (MetadataReadException) { /* best effort — Table column tab stays empty */ }
    }

    private void OnProcedureDetailPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProcedureDetailTabViewModel.EasyMode)
            && sender is ProcedureDetailTabViewModel { CanUseEasyMode: true } d)
        {
            ProcedureEasyModePreference = d.EasyMode;
        }
    }

    // Single construction point for TriggerDetail VMs — mirrors CreateProcedureDetail.
    // The trigger source IS editable (Compile), so the DDL executor is wired. The Easy
    // mode Variables grid needs the domain list and the Table picker needs the table
    // list — both loaded best-effort.
    internal bool TriggerEasyModePreference { get; set; }

    internal TriggerDetailTabViewModel CreateTriggerDetail(MetadataObject obj)
    {
        var detail = new TriggerDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        // Lazy column loader for the Variables grid's merged Domain/Column picker.
        detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
        _ = LoadTriggerListsAsync(detail);
        if (detail.CanUseEasyMode) detail.EasyMode = TriggerEasyModePreference;
        detail.PropertyChanged += OnTriggerDetailPropertyChanged;
        return detail;
    }

    private async Task LoadTriggerListsAsync(TriggerDetailTabViewModel detail)
    {
        try
        {
            var domains = await _metadataReader.ListDomainsAsync().ConfigureAwait(true);
            detail.SetAvailableDomains(domains);
        }
        catch (MetadataReadException) { /* best effort — combo just has "(none)" */ }
        try
        {
            var tables = await _metadataReader.ListAsync(MetadataObjectKind.Table).ConfigureAwait(true);
            detail.SetAvailableTables(tables.Select(t => t.Name));
        }
        catch (MetadataReadException) { /* best effort — empty picker */ }
    }

    private void OnTriggerDetailPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TriggerDetailTabViewModel.EasyMode)
            && sender is TriggerDetailTabViewModel { CanUseEasyMode: true } d)
        {
            TriggerEasyModePreference = d.EasyMode;
        }
    }

    // Single construction point for FunctionDetail VMs — mirrors CreateProcedureDetail.
    // The function source IS editable (Compile) and the function IS executable (Data
    // lane), so both the DDL executor and the Execute callback are wired.
    internal bool FunctionEasyModePreference { get; set; }

    internal FunctionDetailTabViewModel CreateFunctionDetail(MetadataObject obj)
    {
        var detail = new FunctionDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.RunExecuteRequested = RunFunctionExecuteAsync;
        detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
        _ = LoadFunctionListsAsync(detail);
        if (detail.CanUseEasyMode) detail.EasyMode = FunctionEasyModePreference;
        detail.PropertyChanged += OnFunctionDetailPropertyChanged;
        return detail;
    }

    private async Task LoadFunctionListsAsync(FunctionDetailTabViewModel detail)
    {
        try
        {
            var domains = await _metadataReader.ListDomainsAsync().ConfigureAwait(true);
            detail.SetAvailableDomains(domains);
        }
        catch (MetadataReadException) { /* best effort — Domain tab just stays empty */ }
        try
        {
            var tables = await _metadataReader.ListAsync(MetadataObjectKind.Table).ConfigureAwait(true);
            detail.SetAvailableTables(tables.Select(t => t.Name));
        }
        catch (MetadataReadException) { /* best effort — Table column tab stays empty */ }
    }

    private void OnFunctionDetailPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FunctionDetailTabViewModel.EasyMode)
            && sender is FunctionDetailTabViewModel { CanUseEasyMode: true } d)
        {
            FunctionEasyModePreference = d.EasyMode;
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

    // Runs an Execute Function statement (SELECT fn(...) FROM RDB$DATABASE) on the Data
    // lane with bound parameters. Same wrapping + lane as RunProcedureExecuteAsync.
    private async Task<ProcedureExecOutcome> RunFunctionExecuteAsync(string sql, IReadOnlyList<QueryParameter> parameters)
    {
        try
        {
            var result = await _executor.ExecuteAsync(sql, parameters).ConfigureAwait(true);
            AddMessage(MessageSeverity.Info, UiStrings.FunctionExecutedViaDataProfile);
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
        // Merged Domena/Kolumna picker (Pola): lazy column loader + best-effort table list
        // for the Table-column (TYPE OF COLUMN) tab. Domains load via the VM's own LoadAsync.
        if (writable)
        {
            detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
            _ = LoadTableListAsync(detail.SetAvailableTables);
        }
        return detail;
    }

    // Best-effort table list for a merged Domena/Kolumna picker's Table-column tab.
    private async Task LoadTableListAsync(Action<IEnumerable<string>> apply)
    {
        try
        {
            var tables = await _metadataReader.ListAsync(MetadataObjectKind.Table).ConfigureAwait(true);
            apply(tables.Select(t => t.Name));
        }
        catch (MetadataReadException) { /* best effort — Table column tab stays empty */ }
    }

    private async void OnOpenDdlRequested(MetadataObject obj)
    {
        // Focus an existing tab for the same object if one is already open —
        // double-clicking twice shouldn't pile up duplicates. Both Ddl and
        // TableDetail tabs key on (Kind, Name).
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind is WorkspaceTabKind.Ddl or WorkspaceTabKind.TableDetail or WorkspaceTabKind.ViewDetail or WorkspaceTabKind.ProcedureDetail or WorkspaceTabKind.TriggerDetail or WorkspaceTabKind.FunctionDetail or WorkspaceTabKind.GeneratorDetail or WorkspaceTabKind.DomainDetail or WorkspaceTabKind.PackageDetail or WorkspaceTabKind.ExceptionDetail or WorkspaceTabKind.IndexDetail
                && tab.ObjectKind == obj.Kind
                && string.Equals(tab.ObjectName, obj.Name, StringComparison.Ordinal))
            {
                SelectTab(tab);
                return;
            }
        }

        try
        {
            if (OpensAsSecurityManager(obj.Kind))
            {
                await OpenSecurityManagerAsync(obj).ConfigureAwait(true);
                return;
            }

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

            if (OpensAsTriggerDetail(obj.Kind))
            {
                var detail = CreateTriggerDetail(obj);
                var newTab = WorkspaceTabViewModel.CreateTriggerDetail(this, obj, detail, _service.ActiveProfile?.Id);
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

            if (OpensAsFunctionDetail(obj.Kind))
            {
                var detail = CreateFunctionDetail(obj);
                var newTab = WorkspaceTabViewModel.CreateFunctionDetail(this, obj, detail, _service.ActiveProfile?.Id);
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

            if (OpensAsGeneratorDetail(obj.Kind))
            {
                var detail = CreateGeneratorDetail(obj);
                var newTab = WorkspaceTabViewModel.CreateGeneratorDetail(this, obj, detail, _service.ActiveProfile?.Id);
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

            if (OpensAsDomainDetail(obj.Kind))
            {
                var detail = CreateDomainDetail(obj);
                var newTab = WorkspaceTabViewModel.CreateDomainDetail(this, obj, detail, _service.ActiveProfile?.Id);
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

            if (OpensAsPackageDetail(obj.Kind))
            {
                var detail = CreatePackageDetail(obj);
                var newTab = WorkspaceTabViewModel.CreatePackageDetail(this, obj, detail, _service.ActiveProfile?.Id);
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

            if (OpensAsExceptionDetail(obj.Kind))
            {
                var detail = CreateExceptionDetail(obj);
                var newTab = WorkspaceTabViewModel.CreateExceptionDetail(this, obj, detail, _service.ActiveProfile?.Id);
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

            if (OpensAsIndexDetail(obj.Kind))
            {
                var detail = CreateIndexDetail(obj);
                var newTab = WorkspaceTabViewModel.CreateIndexDetail(this, obj, detail, _service.ActiveProfile?.Id);
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
        else if (tab is { Kind: WorkspaceTabKind.TriggerDetail, TriggerDetail: { } triggerDetail }
            && _service.IsConnected)
        {
            _ = triggerDetail.EnsureLoadedAsync();
        }
        else if (tab is { Kind: WorkspaceTabKind.FunctionDetail, FunctionDetail: { } functionDetail }
            && _service.IsConnected)
        {
            _ = functionDetail.EnsureLoadedAsync();
        }
        else if (tab is { Kind: WorkspaceTabKind.GeneratorDetail, GeneratorDetail: { } generatorDetail }
            && _service.IsConnected)
        {
            _ = generatorDetail.EnsureLoadedAsync();
        }
        else if (tab is { Kind: WorkspaceTabKind.IndexDetail, IndexDetail: { } indexDetail }
            && _service.IsConnected)
        {
            _ = indexDetail.EnsureLoadedAsync();
        }
        else if (tab is { Kind: WorkspaceTabKind.DomainDetail, DomainDetail: { } domainDetail }
            && _service.IsConnected)
        {
            _ = domainDetail.EnsureLoadedAsync();
        }
        else if (tab is { Kind: WorkspaceTabKind.PackageDetail, PackageDetail: { } packageDetail }
            && _service.IsConnected)
        {
            _ = packageDetail.EnsureLoadedAsync();
        }
        else if (tab is { Kind: WorkspaceTabKind.ExceptionDetail, ExceptionDetail: { } exceptionDetail }
            && _service.IsConnected)
        {
            _ = exceptionDetail.EnsureLoadedAsync();
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

    // "New X" from a category node → the existing per-kind New command (one flow each).
    // User / Role route to the Security Manager add dialogs.
    private void OnNewObjectRequested(MetadataObjectKind kind)
    {
        switch (kind)
        {
            case MetadataObjectKind.Table: Fire(NewTableCommand); break;
            case MetadataObjectKind.View: Fire(NewViewCommand); break;
            case MetadataObjectKind.Procedure: Fire(NewProcedureCommand); break;
            case MetadataObjectKind.Trigger: Fire(NewTriggerCommand); break;
            case MetadataObjectKind.Function: Fire(NewFunctionCommand); break;
            case MetadataObjectKind.Package: Fire(NewPackageCommand); break;
            case MetadataObjectKind.Generator: Fire(NewGeneratorCommand); break;
            case MetadataObjectKind.Domain: Fire(NewDomainCommand); break;
            case MetadataObjectKind.Exception: Fire(NewExceptionCommand); break;
            case MetadataObjectKind.User: OnNewUserRequestedFromTree(); break;
            case MetadataObjectKind.Role: OnNewRoleRequestedFromTree(); break;
            // Index has no New (created inside Table Detail); SystemTable is read-only.
        }

        static void Fire(System.Windows.Input.ICommand cmd)
        {
            if (cmd.CanExecute(null)) cmd.Execute(null);
        }
    }

    // Generic leaf Delete → confirm → DROP → close open tabs → refresh tree.
    // Reuses DdlGenerator.BuildDrop per kind + the autonomous DDL executor. On error
    // (incl. dependency errors — we never auto-drop deps) the raw Firebird message shows.
    private async void OnDeleteObjectRequested(MetadataObject obj)
    {
        var noun = KindNoun(obj.Kind);
        var confirmed = await RequestConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.MetadataDeleteObjectConfirmTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.MetadataDeleteObjectConfirmFormat, noun, obj.Name),
            ConfirmLabel = UiStrings.MetadataDeleteObjectConfirmYes,
            CancelLabel = UiStrings.DialogCancel,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!confirmed) return;

        string sql;
        try
        {
            sql = DdlGenerator.BuildDrop(obj.Kind, obj.Name);
        }
        catch (ArgumentException)
        {
            return; // kind has no tree-drop path
        }

        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is DdlExecutionException or InvalidOperationException)
        {
            AddMessage(MessageSeverity.Error, string.Format(CultureInfo.CurrentCulture, UiStrings.MetadataDeleteObjectFailedFormat, noun, obj.Name, ex.Message));
            SelectedBottomTabIndex = 1;
            return;
        }

        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.MetadataDeleteObjectExecutedFormat, noun, obj.Name));
        CloseTabsForObject(obj.Kind, obj.Name);
        await Metadata.RefreshAsync().ConfigureAwait(true);
    }

    // Procedure leaf → open (or focus) its detail tab and fire its Execute command,
    // which shows the parameter dialog. Reuses the existing OnOpenDdlRequested + the
    // ProcedureDetailTabViewModel.ExecuteProcedureCommand — no parallel execute path.
    private void OnExecuteProcedureRequested(MetadataObject obj)
    {
        if (obj.Kind != MetadataObjectKind.Procedure) return;
        OnOpenDdlRequested(obj);
        var cmd = ActiveProcedureDetail?.ExecuteProcedureCommand;
        if (cmd is not null && cmd.CanExecute(null)) cmd.Execute(null);
    }

    // ─── Bulk operations (recompile / recompute stats / activate-deactivate) ──
    // One dialog-request event + one execution pipeline reused by every bulk op. The
    // dialog opens IMMEDIATELY with a live VM; rows + counters update as each object
    // completes; Cancel stops the remainder.
    public event Func<BatchResultsViewModel, Task>? BatchResultsRequested;

    // Opens the batch-results dialog up front, then streams each statement's outcome into
    // it (per-statement autonomous autocommit, continues past failures) so the user sees
    // live progress. preResults seeds rows that failed BEFORE the batch (e.g. a source
    // fetch failed during recompile). Cancel (or closing the dialog) stops the rest.
    private async Task RunBatchWithReportAsync(
        string title,
        IReadOnlyList<(string Object, string Operation, string Sql)> steps,
        bool refreshAfter,
        IReadOnlyList<BatchOperationResult>? preResults = null)
    {
        var pre = preResults ?? (IReadOnlyList<BatchOperationResult>)Array.Empty<BatchOperationResult>();
        if (steps.Count == 0 && pre.Count == 0)
        {
            AddMessage(MessageSeverity.Info, UiStrings.BatchNothingToDo);
            return;
        }

        var vm = new BatchResultsViewModel(title);
        vm.Begin(pre.Count + steps.Count);
        foreach (var p in pre)
        {
            vm.AddResult(p);
        }

        // Progress is constructed on the UI thread → Report marshals back to it, so the
        // live VM updates are UI-thread-safe while the executor runs on the thread pool.
        var progress = new Progress<(int Index, string? Error)>(p =>
            vm.AddResult(new BatchOperationResult(
                steps[p.Index].Object, steps[p.Index].Operation, p.Error is null, p.Error)));

        async Task RunAsync()
        {
            try
            {
                if (steps.Count > 0)
                {
                    await _ddlExecutor.ExecuteAutonomousBatchAsync(
                        steps.Select(s => s.Sql).ToList(), vm.CancellationToken, progress).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException) { /* user cancelled — already-run rows stand */ }
            catch (InvalidOperationException ex)
            {
                AddMessage(MessageSeverity.Error, ex.Message);
                SelectedBottomTabIndex = 1;
            }
            vm.Complete();
            if (refreshAfter && !vm.CancellationToken.IsCancellationRequested)
            {
                await Metadata.RefreshAsync().ConfigureAwait(true);
            }
        }

        var execTask = RunAsync();
        if (BatchResultsRequested is { } show)
        {
            await show(vm).ConfigureAwait(true); // modal — runs while the batch streams in
        }
        vm.RequestCancel();                       // dialog closed → stop any remaining work
        await execTask.ConfigureAwait(true);
    }

    // Single trigger activate/deactivate (from a trigger leaf).
    private async void OnSetObjectActiveRequested(MetadataObject obj, bool activate)
    {
        if (obj.Kind != MetadataObjectKind.Trigger) return;
        var sql = activate
            ? DdlGenerator.BuildAlterTriggerActive(obj.Name)
            : DdlGenerator.BuildAlterTriggerInactive(obj.Name);
        try
        {
            await _ddlExecutor.ExecuteAsync(sql).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is DdlExecutionException or InvalidOperationException)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }
        await Metadata.RefreshAsync().ConfigureAwait(true);
    }

    // Bulk trigger activate/deactivate over the visible (filtered) set or ALL. The reader
    // gives authoritative state so we skip triggers already in the target state (avoids
    // needless DDL commits — gotcha #109).
    private async void OnBulkSetActiveRequested(TriggerBulkRequest req)
    {
        if (req.Kind != MetadataObjectKind.Trigger) return;

        IReadOnlyList<MetadataObject> triggers;
        try
        {
            triggers = await _metadataReader.ListAsync(MetadataObjectKind.Trigger).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }

        IEnumerable<MetadataObject> scope = triggers;
        if (req.VisibleOnly)
        {
            var set = new HashSet<string>(req.VisibleNames, StringComparer.OrdinalIgnoreCase);
            scope = triggers.Where(t => set.Contains(t.Name));
        }

        var op = req.Activate ? UiStrings.BatchOpActivate : UiStrings.BatchOpDeactivate;
        var steps = scope
            .Where(t => t.IsActive != req.Activate) // skip already-in-state
            .Select(t => (t.Name, op, req.Activate
                ? DdlGenerator.BuildAlterTriggerActive(t.Name)
                : DdlGenerator.BuildAlterTriggerInactive(t.Name)))
            .ToList<(string, string, string)>();

        var title = req.Activate ? UiStrings.BatchTitleActivateTriggers : UiStrings.BatchTitleDeactivateTriggers;
        await RunBatchWithReportAsync(title, steps, refreshAfter: true).ConfigureAwait(true);
    }

    // Recompile every object of a kind (Procedure/Function/Trigger/Package) by re-running
    // its stored CREATE OR ALTER / RECREATE source. Source-fetch failures become failed
    // rows in the report (no SQL to run for them).
    private async void OnRecompileGroupRequested(MetadataObjectKind kind)
    {
        var (steps, preFailures) = await BuildRecompileStepsAsync(kind).ConfigureAwait(true);
        if (steps is null) return; // list failed — message already surfaced
        var title = string.Format(CultureInfo.CurrentCulture, UiStrings.BatchTitleRecompileFormat, KindNoun(kind));
        await RunBatchWithReportAsync(title, steps, refreshAfter: false, preFailures).ConfigureAwait(true);
    }

    // Shared source-fetch + step-build for recompile. Returns null steps when the object
    // list couldn't be read. Package emits two steps (header + body).
    private async Task<(List<(string, string, string)>? Steps, List<BatchOperationResult> PreFailures)>
        BuildRecompileStepsAsync(MetadataObjectKind kind)
    {
        var preFailures = new List<BatchOperationResult>();
        IReadOnlyList<MetadataObject> objs;
        try
        {
            objs = await _metadataReader.ListAsync(kind).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return (null, preFailures);
        }

        var op = UiStrings.BatchOpRecompile;
        var steps = new List<(string, string, string)>();
        foreach (var o in objs)
        {
            try
            {
                switch (kind)
                {
                    case MetadataObjectKind.Procedure:
                        steps.Add((o.Name, op, await _ddlReader.FetchProcedureSourceAsync(o).ConfigureAwait(true)));
                        break;
                    case MetadataObjectKind.Function:
                        steps.Add((o.Name, op, await _ddlReader.FetchFunctionSourceAsync(o).ConfigureAwait(true)));
                        break;
                    case MetadataObjectKind.Trigger:
                        steps.Add((o.Name, op, await _ddlReader.FetchTriggerSourceAsync(o).ConfigureAwait(true)));
                        break;
                    case MetadataObjectKind.Package:
                        steps.Add((o.Name, UiStrings.BatchOpRecompileHeader, await _ddlReader.FetchPackageHeaderSourceAsync(o).ConfigureAwait(true)));
                        steps.Add((o.Name, UiStrings.BatchOpRecompileBody, await _ddlReader.FetchPackageBodySourceAsync(o).ConfigureAwait(true)));
                        break;
                }
            }
            catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
            {
                preFailures.Add(new BatchOperationResult(o.Name, op, false, ex.Message));
            }
        }
        return (steps, preFailures);
    }

    // ─── Connection-node (database-wide) bulk ops ─────────────────────────────
    // Recompute selectivity statistics for every index (SET STATISTICS INDEX).
    internal async Task RecomputeAllIndexStatisticsAsync()
    {
        IReadOnlyList<MetadataObject> indexes;
        try
        {
            indexes = await _metadataReader.ListAsync(MetadataObjectKind.Index).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
            return;
        }
        var op = UiStrings.BatchOpRecomputeStatistics;
        var steps = indexes
            .Select(ix => (ix.Name, op, DdlGenerator.BuildSetIndexStatistics(ix.Name)))
            .ToList<(string, string, string)>();
        await RunBatchWithReportAsync(UiStrings.BatchTitleRecomputeStatistics, steps, refreshAfter: false).ConfigureAwait(true);
    }

    // Recompile every procedure, function, trigger and package.
    internal async Task RecompileAllObjectsAsync()
    {
        var allSteps = new List<(string, string, string)>();
        var allPre = new List<BatchOperationResult>();
        foreach (var kind in new[]
        {
            MetadataObjectKind.Procedure,
            MetadataObjectKind.Function,
            MetadataObjectKind.Trigger,
            MetadataObjectKind.Package,
        })
        {
            var (steps, pre) = await BuildRecompileStepsAsync(kind).ConfigureAwait(true);
            if (steps is not null) allSteps.AddRange(steps);
            allPre.AddRange(pre);
        }
        await RunBatchWithReportAsync(UiStrings.BatchTitleRecompileAll, allSteps, refreshAfter: false, allPre).ConfigureAwait(true);
    }

    // Singular lowercase noun for confirm/report messages.
    private static string KindNoun(MetadataObjectKind kind) => kind switch
    {
        MetadataObjectKind.Table => "table",
        MetadataObjectKind.View => "view",
        MetadataObjectKind.Procedure => "procedure",
        MetadataObjectKind.Trigger => "trigger",
        MetadataObjectKind.Function => "function",
        MetadataObjectKind.Generator => "generator",
        MetadataObjectKind.Domain => "domain",
        MetadataObjectKind.Package => "package",
        MetadataObjectKind.Exception => "exception",
        MetadataObjectKind.Role => "role",
        MetadataObjectKind.User => "user",
        MetadataObjectKind.Index => "index",
        MetadataObjectKind.SystemTable => "system table",
        _ => kind.ToString().ToLowerInvariant(),
    };

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
    /// User-initiated tab close. Confirms (Discard / Cancel) before discarding ANY
    /// tab that reports unsaved work — a New Table form, an uncompiled new view /
    /// procedure, a modified-but-not-compiled source, or a table designer with
    /// queued structural changes. Clean tabs (and DDL / read-only tabs, reopenable
    /// from the tree) close silently. Programmatic closes (post-compile,
    /// delete-table cleanup) call <see cref="CloseTab"/> directly and never prompt.
    /// </summary>
    public async Task RequestCloseTabAsync(WorkspaceTabViewModel tab)
    {
        if (tab.UnsavedWork is { } work)
        {
            var confirmed = await RequestConfirmAsync(new ConfirmRequest
            {
                Title = UiStrings.CloseTabUnsavedConfirmTitle,
                Message = string.Format(CultureInfo.CurrentCulture, UiStrings.CloseTabUnsavedConfirmFormat, work.Label),
                ConfirmLabel = UiStrings.CloseTabUnsavedConfirmYes,
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

    // The active View / Procedure / New Table editor, observed so the unified toolbar's
    // Collection section + section flags track its live state (EasyMode, sub-tab,
    // selection). TableDetail is observed via _trackedTableDetail above.
    private System.ComponentModel.INotifyPropertyChanged? _trackedCollectionChild;

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

        if (_trackedCollectionChild is not null)
        {
            _trackedCollectionChild.PropertyChanged -= OnCollectionChildChanged;
            _trackedCollectionChild = null;
        }
        System.ComponentModel.INotifyPropertyChanged? child = newValue?.Kind switch
        {
            WorkspaceTabKind.ViewDetail => newValue.ViewDetail,
            WorkspaceTabKind.ProcedureDetail => newValue.ProcedureDetail,
            WorkspaceTabKind.TriggerDetail => newValue.TriggerDetail,
            WorkspaceTabKind.FunctionDetail => newValue.FunctionDetail,
            WorkspaceTabKind.NewTable => newValue.NewTable,
            _ => null,
        };
        if (child is not null)
        {
            _trackedCollectionChild = child;
            _trackedCollectionChild.PropertyChanged += OnCollectionChildChanged;
        }

        RefreshToolbarSections();
    }

    private void OnCollectionChildChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => RefreshToolbarSections();

    private void OnTableDetailPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Any TableDetail change (sub-tab, selection, pending edits) can affect the
        // unified toolbar's Collection routing — refresh coarsely.
        RefreshToolbarSections();
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
        // New Trigger shares the same connection-state gate.
        OnPropertyChanged(nameof(CanCreateTrigger));
        NewTriggerCommand.NotifyCanExecuteChanged();
        // New Function shares the same connection-state gate.
        OnPropertyChanged(nameof(CanCreateFunction));
        NewFunctionCommand.NotifyCanExecuteChanged();
        // New Generator shares the same connection-state gate.
        OnPropertyChanged(nameof(CanCreateGenerator));
        NewGeneratorCommand.NotifyCanExecuteChanged();
        // New Domain shares the same connection-state gate.
        OnPropertyChanged(nameof(CanCreateDomain));
        NewDomainCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCreatePackage));
        NewPackageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanCreateException));
        NewExceptionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenSecurityManager));
        OpenSecurityManagerCommand.NotifyCanExecuteChanged();
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
