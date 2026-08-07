using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Commands;
using EmberTern.App.Completion;
using EmberTern.App.Controls;
using EmberTern.App.Diagnostics;
using EmberTern.App.Export;
using EmberTern.App.Security;
using EmberTern.App.Settings;
using EmberTern.App.Sql;
using EmberTern.Core.Connections;
using EmberTern.Core.Export;
using EmberTern.Core.Metadata;
using EmberTern.Core.Performance;
using EmberTern.Core.Query;
using EmberTern.Core.Search;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Templates;
using EmberTern.Core.Workspace;
using EmberTern.Firebird;
using CoreSavedQuery = EmberTern.Core.Workspace.SavedQuery;
using CoreTabKind = EmberTern.Core.Workspace.WorkspaceTabKind;

namespace EmberTern.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConnectionProfileStore _store;
    private readonly FolderStore _folderStore;
    private readonly ParameterHistoryStore _parameterHistory;
    private readonly WatchStore _watchStore;
    private readonly EmberTern.Core.Import.ImportProfileStore _importProfiles;
    private readonly PreferencesService _preferences;
    private readonly SettingsPortability _portability;

    // ⭐ The ONE owner of "does the application hold anything uncommitted". Before I7.5 that question had a
    // single answer (the console transaction) and the guards asked it directly; Data Import's own
    // transaction made it a question with several, and the shell must not grow a list of module names to
    // answer it.
    private readonly PendingWorkRegistry _pendingWork = new();

    // The import tab is near-singleton, so there is at most one of each of these at a time.
    private ImportSessionConnection? _importSession;
    private ImportSessionWork? _importPendingWork;
    private FolderState _folderState = new();
    private readonly FirebirdConnectionService _service;
    // Data lane (connection #1): SQL Editor F5, data preview/edit.
    // THE user's working transaction — one, on the data attachment. Everything the user runs by
    // hand lives in it: SQL Editor F5 (queries AND DDL), table-data edits, Execute Procedure,
    // Script Executor. One Commit, one Rollback.
    private readonly TransactionService _transactionService;
    // The read-only metadata attachment (#2): catalog reads only, implicit per-command
    // transactions, owns no transaction. See MetadataLane.
    private readonly MetadataLane _metadataLane;
    // The SQL Editor is a classic SQL console: ONE executor, ONE attachment, no routing by
    // statement kind. (There used to be a second, "metadata" executor that F5 silently routed DDL
    // to — a hidden second transaction with its own Commit. That is gone.)
    private readonly FirebirdQueryExecutor _executor;
    private readonly FirebirdMetadataReader _metadataReader;
    private readonly FirebirdMetadataSearchReader _searchReader;
    private readonly FirebirdDdlReader _ddlReader;
    private readonly FirebirdSecurityReader _securityReader;
    private readonly FirebirdTableDetailReader _tableDetailReader;
    private readonly FirebirdDataEditor _dataEditor;
    private readonly FirebirdDdlExecutor _ddlExecutor;
    private readonly MetadataExportService _metadataExportService;
    // Performance Analysis (Phase 1): auto-available after execution (Option B) — the
    // panel builds the report from the LAST data-lane run (plan re-read on view, no
    // re-execution). Plan + timings only; no MON$/trace/advisor yet.
    private readonly FirebirdPlanReader _planReader;
    private readonly FirebirdPerfStatsReader _perfStatsReader;
    private readonly FirebirdSessionReader _sessionReader;
    private readonly FirebirdCatalogReader _catalogReader;
    private readonly PerformanceAnalyzer _performanceAnalyzer;
    // Script Executor: splits a script via the driver's FbScript (no custom parser) and runs
    // it as ONE data-lane transaction (no autocommit in Manual mode).
    private readonly FirebirdScriptParser _scriptParser = new();
    private readonly FirebirdScriptExecutor _scriptExecutor;
    private long? _dataAttachmentId;
    private bool _performanceTabActive;
    private const int PerformanceBottomTabIndex = 3; // Results=0, Messages=1, Output=2, Performance=3

    // SQL-template engine (Drag & Drop snippets). The registry answers the drop flyout
    // by object kind with no metadata read; the builder loads a dropped object's metadata
    // only after the user picks a template.
    private readonly SqlTemplateRegistry _templateRegistry = SqlTemplateCatalog.CreateRegistry();
    private SnippetContextBuilder? _snippetContextBuilder;
    private CancellationTokenSource? _executionCts;
    // True when the OPEN user transaction has run at least one DDL/DCL statement. Uncommitted DDL
    // is deliberately invisible to the read-only metadata attachment (classic console semantics —
    // a new object appears in the tree only after Commit), so the tree must be reloaded when this
    // transaction settles. Cleared on settle. Set from SqlStatementClassifier, which is now a
    // refresh hint only — it no longer routes execution.
    private bool _transactionChangedSchema;
    // The SQL (+ bound params) of the last data-lane result-set run, so "Load all rows" can re-run
    // exactly THAT statement as Full — never whatever is now in the editor.
    private string? _lastResultSql;
    private IReadOnlyList<QueryParameter>? _lastResultParameters;
    // Set immediately before assigning a Load-all Full result so OnCurrentResultChanged keeps the
    // client-side view state (filter/sort/aggregation) instead of resetting it.
    private bool _preserveViewStateOnNextResult;
    private TransactionState _previousTransactionState = TransactionState.Idle;
    // Set just before a Commit/Rollback settles, read by OnTransactionStateChanged to decide
    // whether the post-settle refresh runs. See DecidePostTransactionRefresh.
    private bool _lastTransactionSettleWasRollback;
    // Snapshot of _transactionChangedSchema taken at settle time (the flag itself is cleared as
    // soon as the transaction ends, but the state-changed handler runs afterwards, possibly on
    // another thread).
    private bool _settledTransactionChangedSchema;
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
    // Profiles whose STORED tabs must not be materialised in this session (§7.5, "restore open tabs" off).
    // Populated at restore time and emptied one id at a time, so a reconnect later in the SAME session still
    // restores the tabs this session built. The dictionary above is untouched either way — it is what capture
    // writes back at close.
    private readonly HashSet<string> _workspaceRestoreSuppressed = new(StringComparer.Ordinal);
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
        // Same settings.dat + protector as the other section facades (gotcha #88) so
        // Execute Procedure/Function parameter history persists in the shared file.
        _parameterHistory = new ParameterHistoryStore(
            System.IO.Path.GetDirectoryName(store.FilePath)!, store.Protector);
        // Same shared settings.dat — debugger Watch expressions persist per routine (Stage X / D5).
        _watchStore = new WatchStore(
            System.IO.Path.GetDirectoryName(store.FilePath)!, store.Protector);
        // Same shared settings.dat — the Data Import module's implicit "last used" configuration (§4.8.4), which
        // is the same store its named profiles will use in I11.
        _importProfiles = new EmberTern.Core.Import.ImportProfileStore(
            System.IO.Path.GetDirectoryName(store.FilePath)!, store.Protector);
        // Same shared settings.dat — the scalar user preferences (Settings Center, etap 2/3). Wrapped in the
        // App's ONE PreferencesService because the store's Save takes a whole Preferences by design: two
        // holders of a snapshot would overwrite each other's fields (the titlebar theme toggle vs an open
        // Settings Center). The service loads here and is handed to both.
        _preferences = new PreferencesService(new PreferencesStore(
            System.IO.Path.GetDirectoryName(store.FilePath)!, store.Protector));
        // ⭐ Pasek zakładek reaguje na apply-on-change. Subskrypcja jest TUTAJ, a nie w widoku, bo widok
        // dostaje `PropertyChanged` z tego VM i nie musi znać drugiego źródła zdarzeń — to ta sama zasada
        // co przy motywie: jedno miejsce zamienia preferencję na skutek. ⚠ Serwis żyje tak długo jak ten
        // VM (oba są własnością okna głównego), więc wypisanie się nie ma czego chronić.
        _preferences.Changed += (_, _) => RaiseTabStripPreferencesChanged();
        // Settings export / import (etap 5b). Same settings.dat + protector again, and the app version comes from
        // AppInfo because Core cannot see it and must not be able to branch on it (§15.3a). AfterImport is this
        // view model's own refresh: the import rewrites the file several in-memory holders were loaded from.
        _portability = new SettingsPortability(
            new ApplicationSettingsStore(
                System.IO.Path.GetDirectoryName(store.FilePath)!, store.Protector),
            _preferences,
            AppInfo.Version,
            ApplyImportedSettings);
        _folderState = _folderStore.Load();
        // Settings health (audit A-03). Read ONCE, here, because the answer is a property of the file on disk
        // at startup and does not change while we run — and because it must be read BEFORE anything in this
        // session writes, so what the user is told reflects the file they actually arrived with.
        CaptureSettingsHealth(store);
        _service = service;
        _transactionService = transactionService;
        // Catalog reads run on the read-only metadata attachment with implicit per-command
        // transactions, so browsing never pins objects in — or is blocked by — the user's
        // working transaction. It owns no transaction of its own (it degrades onto the data
        // connection if the second attachment can't open; MetadataLane handles that).
        _metadataLane = new MetadataLane(_service, _transactionService);
        _executor = new FirebirdQueryExecutor(_service, _transactionService);
        // The TableDetail reader splits per method: structure → metadata lane, data preview → the
        // user's transaction on the data lane (so the user sees their own uncommitted rows).
        _metadataReader = new FirebirdMetadataReader(_service, _metadataLane);
        _searchReader = new FirebirdMetadataSearchReader(_service, _metadataLane);
        _ddlReader = new FirebirdDdlReader(_service, _metadataLane);
        _securityReader = new FirebirdSecurityReader(_service, _metadataLane);
        _tableDetailReader = new FirebirdTableDetailReader(_service, _metadataLane, _transactionService);
        // The single authority for a complete portable object script (structure + COMMENT ON,
        // no grants) — used by both the Export button and the read-only DDL tab.
        _metadataExportService = new MetadataExportService(_ddlReader, _tableDetailReader);
        _dataEditor = new FirebirdDataEditor(_service, _transactionService);
        // Snippet metadata loaders wired to the TableDetail reader (metadata lane).
        _snippetContextBuilder = new SnippetContextBuilder(
            (name, ct) => _tableDetailReader.GetFieldsAsync(name, ct),
            (name, ct) => _tableDetailReader.GetConstraintsAsync(name, ct),
            (name, type, ct) => _tableDetailReader.GetProcedureParametersAsync(name, type, ct),
            async (name, ct) => await _tableDetailReader.GetFunctionSignatureAsync(name, ct).ConfigureAwait(true));
        // Object-editor Compile ONLY — it runs on the dedicated DDL attachment (autonomous,
        // auto-committed, WAIT-bounded / Developer Mode). It never touches the user's transaction;
        // the TransactionService is passed only for the degraded-mode guard (no DDL attachment →
        // it shares the data connection, where one-tx-per-connection applies).
        _ddlExecutor = new FirebirdDdlExecutor(_service, _transactionService);
        // Performance profiling runs on the data lane (same attachment as F5). Plan is
        // read best-effort; a profiled run executes under the user's transaction.
        _planReader = new FirebirdPlanReader(_service, _transactionService);
        // Per-table reads (Phase 2): stats read on the metadata lane, attachment id on the
        // data lane — so before/after snapshots stay fresh and never touch the user's tx.
        _perfStatsReader = new FirebirdPerfStatsReader(_service, _metadataLane, _transactionService);
        // The data-lane MON$/CURRENT_CONNECTION read must attach to the user's pending transaction
        // or the driver rejects the command (gotcha #173); the metadata lane uses an implicit tx.
        _sessionReader = new FirebirdSessionReader(_service, _transactionService, _metadataLane);
        // Catalog (indexes/selectivity/cardinality) for the advisor — read on the metadata lane
        // for the profiled query's tables when the Performance panel builds (Phase 3a).
        _catalogReader = new FirebirdCatalogReader(_service, _metadataLane);
        // Script Executor runs on the DATA lane because it IS the user working transaction
        // (long-lived, manual Commit/Rollback; one tx per connection, gotcha #89) — not co-location.
        _scriptExecutor = new FirebirdScriptExecutor(_service, _transactionService);
        _performanceAnalyzer = new PerformanceAnalyzer();
        // The SQL Editor gets its OWN Performance context (its own captured run). Procedure/
        // Function detail tabs each get their own via the factory — no shared global panel.
        SqlEditorPerformance = new HostPerformanceContext(
            BuildPerformanceReportAsync,
            text => ClipboardWriteRequested is { } write ? write(text) : Task.CompletedTask);
        Metadata = new MetadataExplorerViewModel(_service, _metadataReader);
        Metadata.OpenDdlRequested += OnOpenDdlRequested;
        Metadata.CopyNameRequested += OnCopyNameRequested;
        Metadata.StatusReported += OnMetadataStatusReported;
        Metadata.NewObjectRequested += OnNewObjectRequested;
        Metadata.DeleteObjectRequested += OnDeleteObjectRequested;
        Metadata.ExecuteProcedureRequested += OnExecuteProcedureRequested;
        Metadata.DebugProcedureRequested += OnDebugProcedureRequested;
        Metadata.DebugTriggerRequested += OnDebugTriggerRequested;
        Metadata.DebugFunctionRequested += OnDebugFunctionRequested;
        Metadata.RecompileGroupRequested += OnRecompileGroupRequested;
        Metadata.SetObjectActiveRequested += OnSetObjectActiveRequested;
        Metadata.BulkSetActiveRequested += OnBulkSetActiveRequested;
        // A manual metadata refresh means the SCHEMA may have moved, so the per-object caches every open
        // editor's semantic model is built from must go. Without this a refresh rebuilt those models against
        // the same stale columns, and a newly added column stayed "unknown" for the rest of the session on
        // every open tab (S-2). See MetadataExplorerViewModel.SchemaInvalidated for the ordering.
        Metadata.SchemaInvalidated += InvalidateObjectCaches;
        Messages = new ObservableCollection<QueryMessageViewModel>();
        // Workspace tabs start empty — no Query tab until a connection becomes active.
        // Each ConnectionProfile owns its own Query+DDL tab list via _workspacesByConnection.
        WorkspaceTabs = new ObservableCollection<WorkspaceTabViewModel>();
        // Seam 6d — one wiring point for "an editor in this workspace compiled its object", rather than the
        // several dozen places that add a tab. A tab kind added later is covered without anyone remembering to.
        WorkspaceTabs.CollectionChanged += OnWorkspaceTabsChanged;
        SavedQueries = new ObservableCollection<SavedQueryViewModel>();
        SavedQueries.CollectionChanged += (_, _) =>
        {
            DeleteSelectedQueryCommand.NotifyCanExecuteChanged();
            ClearAllQueriesCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(HasSavedQueries));
            OnPropertyChanged(nameof(ShowQueryPanelEmptyHint));
        };
        // Shared filter panel + aggregation bar for the SQL Results grid. Materialized
        // path: filter/aggregate/sort/page all run client-side over CurrentResult.Rows.
        ResultFilterPanel = new FilterPanelViewModel { ApplyRequested = ApplyResultFilterAsync };
        ResultAggregationBar = new AggregationBarViewModel(ComputeResultAggregateAsync);
        _service.ActiveConnectionChanged += OnActiveConnectionChanged;
        _service.ActiveProfileUpdated += OnActiveProfileUpdated;
        // ⭐ Faza 3 ładowania połączenia (prefetch kategorii) karmi sekcję postępu — a jej koniec jest
        // jedynym miejscem, w którym gaśnie także faza 1 (§19.34). `Metadata` jest jedna na aplikację,
        // więc nie ma tu czego agregować: jedna subskrypcja, zdejmowana nigdy, bo żyje tyle co okno.
        Metadata.PropertyChanged += OnMetadataProgressChanged;
        _transactionService.TransactionStateChanged += OnTransactionStateChanged;
        // The console's transaction is a pending-work source like any other — registered once, here, so the
        // guards never name it again.
        _pendingWork.Register(new ConsoleTransactionWork(_transactionService));
        ReloadConnections();
        UpdateStatusFromConnection();
    }

    internal ConnectionProfileStore Store => _store;
    internal FirebirdConnectionService Service => _service;
    internal TransactionService TransactionService => _transactionService;
    internal ParameterHistoryStore ParameterHistory => _parameterHistory;

    public ObservableCollection<QueryMessageViewModel> Messages { get; }
    public ObservableCollection<WorkspaceTabViewModel> WorkspaceTabs { get; }
    public ObservableCollection<SavedQueryViewModel> SavedQueries { get; }
    public MetadataExplorerViewModel Metadata { get; }

    /// <summary>
    /// The app's ONE owner of the scalar user preferences, over the shared <c>settings.dat</c>.
    /// <para>Exposed because three surfaces outside this view model need the same instance: the startup theme
    /// read in <c>App</c>, the titlebar theme toggle in <c>MainWindow</c>'s code-behind (which stays
    /// code-behind per architecture rule #1 — it moves a <c>string</c>, not a UI type), and Settings Center.
    /// A second instance anywhere would clobber fields, because the store persists a whole
    /// <see cref="Preferences"/> at a time.</para>
    /// </summary>
    public PreferencesService Preferences => _preferences;

    /// <summary>
    /// Settings export / import (etap 5b), over the same <c>settings.dat</c> and protector as every other
    /// facade. Its <c>AfterImport</c> is wired here because the in-memory holders an import invalidates are this
    /// view model's — see <see cref="ApplyImportedSettings"/>.
    /// </summary>
    public SettingsPortability Portability => _portability;

    /// <summary>
    /// Brings this view model back into step with a <c>settings.dat</c> that an import has just rewritten.
    ///
    /// <para>⚠ <b>The two holders here were found by reading the code, not by guessing.</b>
    /// <c>_folderState</c> is loaded once in the constructor and mutated in place all session, so without the
    /// reload the next folder edit would write the pre-import layout back. Connections have no snapshot —
    /// <see cref="ReloadConnections"/> re-reads them — but the tree still has to be rebuilt to show the imported
    /// profiles, and it reads <c>_folderState</c>, which is why the order below is not arbitrary.</para>
    ///
    /// <para>An active connection keeps the parameters it connected with; a changed profile takes effect on its
    /// next connect. That is not new behaviour — editing a profile mid-session has always worked this way — and
    /// the import surface says so rather than blocking on it.</para>
    /// </summary>
    public void ApplyImportedSettings()
    {
        _folderState = _folderStore.Load();
        ReloadConnections();
    }

    /// <summary>
    /// ⚠ True when this session must NOT capture its workspace on exit, because an import replaced the stored
    /// one. Read by <c>MainWindow.OnClosing</c>; see <see cref="SettingsPortability.ImportedWorkspaces"/> for why
    /// suppressing that one write is what makes the import honest.
    /// </summary>
    public bool SuppressWorkspaceCaptureOnClose => _portability.ImportedWorkspaces;

    /// <summary>
    /// The casing style the <b>Format SQL</b> action must use right now, read from the live preferences
    /// through the one boundary mapping (<see cref="FormatterStylePreference"/>).
    ///
    /// <para>⚠ <b>Computed per call, never cached.</b> Apply-on-change means the preference can change while
    /// tabs are open; a captured <see cref="FormatterStyle"/> would go stale silently and the next Format SQL
    /// would use the previous setting — the same clobbering shape §13.1 removed by having ONE
    /// <see cref="PreferencesService"/>. Reading it at the moment of the action costs two enum comparisons.</para>
    ///
    /// <para>⚠ <b>Scope: the Format SQL action only</b> (design §14). SQL that EmberTern <i>composes</i>
    /// (Copy as INSERT / UPDATE, the <c>.sql</c> exporters) or renders <i>read-only</i> (the Trace event
    /// detail) keeps <see cref="FormatterStyle.Default"/>, on ratified Q1's reasoning: the formatter reformats
    /// the user's own text, whereas generated statements are EmberTern's output.</para>
    /// </summary>
    public FormatterStyle FormatterStyle => FormatterStylePreference.From(_preferences.Current);

    // ── Pasek zakładek — dwa tryby (M3.3b / product-polish §8.2) ─────────────────────────────────────
    //
    // ⭐ VM odpowiada wyłącznie na pytanie „KTÓRY tryb i ile wierszy" — jak to zrealizować (kierunki
    // przewijania, wysokość, licznik przepełnienia) jest sprawą widoku. To ta sama granica, którą trzyma
    // `FormatterStyle`: preferencja jest faktem, jej skutek jest mechanizmem.
    //
    // ⚠ Czytane PER WYWOŁANIE, nigdy zapamiętane — apply-on-change znaczy, że użytkownik może przełączyć
    // tryb przy otwartych zakładkach. Zmianę ogłasza `RaiseTabStripPreferencesChanged`, wywoływane
    // z jednego miejsca, w którym aplikacja reaguje na `PreferencesService.Changed`.

    /// <summary>Czy pasek zakładek układa zakładki w wielu wierszach (domyślnie) — §8.2.</summary>
    public bool IsTabStripMultiRow => string.Equals(
        _preferences.Current.TabStripMode,
        PreferenceOptions.TabStripModeMultiRow,
        StringComparison.Ordinal);

    /// <summary>Dopełnienie <see cref="IsTabStripMultiRow"/>. Istnieje, bo XAML nie ma negacji w wiązaniu
    /// kompilowanym tak czytelnej jak nazwana właściwość, a obie strony bramkują realne elementy.</summary>
    public bool IsTabStripSingleRow => !IsTabStripMultiRow;

    /// <summary>Ile wierszy może urosnąć pasek, zanim zacznie się przewijać (§8.2). Czytane tylko
    /// w trybie wielowierszowym; wartość przeżywa przełączenie trybu tam i z powrotem.</summary>
    public int TabStripMaxRows => _preferences.Current.TabStripMaxRows;

    /// <summary>Ogłasza, że preferencje paska zakładek mogły się zmienić.</summary>
    internal void RaiseTabStripPreferencesChanged()
    {
        OnPropertyChanged(nameof(IsTabStripMultiRow));
        OnPropertyChanged(nameof(IsTabStripSingleRow));
        OnPropertyChanged(nameof(TabStripMaxRows));
    }

    /// <summary>The SQL Editor's own Performance context (its captured run + panel). Procedure/
    /// Function detail tabs each own a separate <see cref="HostPerformanceContext"/> — nothing is
    /// shared, so a run in one place never shows up in another.</summary>
    internal HostPerformanceContext SqlEditorPerformance { get; }

    /// <summary>Performance Analysis panel for the SQL Editor bottom-panel sub-tab.</summary>
    public PerformancePanelViewModel Performance => SqlEditorPerformance.Panel;

    /// <summary>Diagnostics panel (Stage 7 / S4) for the SQL Editor bottom-panel sub-tab — a view of the
    /// DiagnosticsEngine's findings for the editor's document. Fed by the View's <c>DiagnosticsPanelBinder</c>
    /// from the editor's cached, version-matched diagnostics (the VM computes nothing). Named
    /// <c>DiagnosticsPanel</c>, not <c>Diagnostics</c>, because this class resolves that name to the
    /// <see cref="EmberTern.App.Diagnostics"/> namespace (ScrollTrace / RefreshTrace).</summary>
    public DiagnosticsPanelViewModel DiagnosticsPanel { get; } = new();

    /// <summary>Builds a per-run Performance report (plan + reads + advisor) from a captured
    /// execution. Shared by every <see cref="HostPerformanceContext"/> — the readers live here, the
    /// captured data lives in the context.</summary>
    internal HostPerformanceContext CreatePerformanceContext()
        => new(BuildPerformanceReportAsync,
               text => ClipboardWriteRequested is { } write ? write(text) : Task.CompletedTask);

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
    [NotifyPropertyChangedFor(nameof(IsTraceMonitorTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveTraceMonitor))]
    [NotifyPropertyChangedFor(nameof(IsSessionManagerTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveSessionManager))]
    [NotifyPropertyChangedFor(nameof(IsGlobalSearchTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveGlobalSearch))]
    [NotifyPropertyChangedFor(nameof(IsScriptExecutorTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveScriptExecutor))]
    [NotifyPropertyChangedFor(nameof(IsDataImportTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveDataImport))]
    [NotifyPropertyChangedFor(nameof(IsDebuggerTabActive))]
    [NotifyPropertyChangedFor(nameof(ActiveDebugger))]
    [NotifyPropertyChangedFor(nameof(IsClosableTabActive))]
    [NotifyPropertyChangedFor(nameof(CanExportDdl))]
    [NotifyCanExecuteChangedFor(nameof(ExportDdlCommand))]
    [NotifyPropertyChangedFor(nameof(ShowEditorToolbar))]
    [NotifyPropertyChangedFor(nameof(ShowTransactionButtons))]
    [NotifyPropertyChangedFor(nameof(ShowDataTransactionButtons))]
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

    public bool IsTraceMonitorTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TraceMonitor };
    public TraceMonitorTabViewModel? ActiveTraceMonitor
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.TraceMonitor } t ? t.TraceMonitor : null;
    public bool IsSessionManagerTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.SessionManager };
    public SessionManagerTabViewModel? ActiveSessionManager
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.SessionManager } t ? t.SessionManager : null;

    public bool IsScriptExecutorTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.ScriptExecutor };
    public ScriptExecutorTabViewModel? ActiveScriptExecutor
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.ScriptExecutor } t ? t.ScriptExecutor : null;

    public bool IsDataImportTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.DataImport };
    public DataImportTabViewModel? ActiveDataImport
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.DataImport } t ? t.DataImport : null;

    public bool IsGlobalSearchTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.GlobalSearch };
    public GlobalSearchTabViewModel? ActiveGlobalSearch
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.GlobalSearch } t ? t.GlobalSearch : null;

    public bool IsDebuggerTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Debugger };
    public DebuggerTabViewModel? ActiveDebugger
        => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Debugger } t ? t.Debugger : null;

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
    // ⭐⭐ M3.2a ROZWAŻYŁO PRZENIESIENIE TEJ PARY na prawą krawędź paska (zasięg transakcyjny, a przy
    // okazji największa pojedyncza różnica geometryczna między rodzajami zakładek — 68 px) i użytkownik
    // ODRZUCIŁ to po obejrzeniu w działającej aplikacji: Commit, Rollback i Execute tworzą jedną grupę
    // logiczną i szuka się ich razem (product-polish.md §19.11). ⛔ Nie przenosić — ani na prawo, ani do
    // paska statusu (§8.4.5). ⭐ Reguła, która z tego została: GRUPA SEMANTYCZNA BIJE STABILNOŚĆ POZYCJI.
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
    //
    // ⭐⭐ M3.2a — MODEL SEKCJI GWARANTUJE KOLEJNOŚĆ, ALE NIE POZYCJĘ (product-polish.md §19.10/§19.11).
    // Wszystkie sekcje leżą w jednym poziomym StackPanelu, więc szerokość każdej to suma jej widocznych
    // dzieci i każda zmiana stanu przesuwa wszystko na prawo od siebie. Zmierzone Procedure vs Trigger:
    // przy x=212 px jedna zakładka ma **Execute**, druga **Comment**.
    //
    // ⛔⛔ WSZYSTKIE TRZY RUCHY M3.2a PO STRONIE TOOLBARA ZOSTAŁY WYCOFANE PRZEZ UŻYTKOWNIKA po obejrzeniu
    // w działającej aplikacji (§19.11 + §19.12) — mimo że każdy działał tak, jak zaprojektowano:
    //   • wspólna podłoga Execute/Cancel  — rozdymała akcję główną ponad jej treść (R5 od drugiej strony),
    //   • Commit/Rollback dokowane do prawej — rozbijały grupę, której szuka się razem (⭐ GRUPA
    //     SEMANTYCZNA BIJE STABILNOŚĆ POZYCJI),
    //   • rezerwacja slotu sekcji 1 — zostawiała pustą dziurę w SQL Editorze (⭐ R13: NIE REZERWUJEMY
    //     MIEJSCA NA ELEMENT, KTÓRY W DANYM KONTEKŚCIE NIGDY SIĘ NIE POJAWI).
    // ⛔ Nie przywracać żadnego z nich. Przesuwanie się paska przy zmianie rodzaju zakładki oraz przy
    // F5 jest ŚWIADOMIE ZAAKCEPTOWANYM kompromisem: zwarty i naturalny układ wygrywa z nieruchomym.
    //
    // ⛔ Pikselowa tożsamość MIĘDZY rodzajami zakładek również nie jest celem — Procedure, Trigger
    // i Table mają inną semantykę i mogą mieć inny zestaw narzędzi. Jedyny sposób na pełną zgodność to
    // rezerwacja najgorszego przypadku każdej sekcji: zmierzone ~617 px stałej rezerwy, czyli ~500 px
    // dziur na uboższych zakładkach — czyli R13 pomnożone przez pięć.
    // Section 3 routes Add/Remove/Move to the active editor's collection via the four
    // commands below; a future Trigger/Function/Package editor plugs a new case into
    // ActiveCollection() and gets the toolbar for free — no new layout pattern.

    // Section 1 — a mode toggle exists for Table (Grid-Edit), View + Procedure + Trigger + Function (Easy).
    public bool ShowModeSection => ShowFieldEditTools || IsViewDetailTabActive || IsProcedureDetailTabActive || IsTriggerDetailTabActive || IsFunctionDetailTabActive;
    // Section 2 — every editor has a primary action (Execute / Compile / Commit).
    public bool ShowMainSection => SelectedWorkspaceTab is not null;
    // Section 4 — helpers exist for SQL editor, View, Procedure, Trigger, Function, Package.
    // (Dane's refresh + pagination moved into the sub-tab's own grid toolbar, so the Data
    // tab no longer contributes a helper section — otherwise its separator would orphan.)
    public bool ShowHelperSection => IsQueryTabActive || IsViewDetailTabActive || IsProcedureDetailTabActive || IsTriggerDetailTabActive || IsFunctionDetailTabActive || IsPackageDetailTabActive;

    // A separator shows only between two non-empty adjacent sections.
    private bool HasFrom2 => ShowMainSection || ShowCollectionTools || ShowHelperSection || IsClosableTabActive;
    private bool HasFrom3 => ShowCollectionTools || ShowHelperSection || IsClosableTabActive;
    private bool HasFrom4 => ShowHelperSection || IsClosableTabActive;
    public bool ToolbarSep1Visible => ShowModeSection && HasFrom2;
    public bool ToolbarSep2Visible => ShowMainSection && HasFrom3;
    public bool ToolbarSep3Visible => ShowCollectionTools && HasFrom4;
    public bool ToolbarSep4Visible => ShowHelperSection && IsClosableTabActive;

    /// <summary>
    /// One editable collection, as the toolbar and the router see it.
    ///
    /// <para><see cref="Noun"/> is what the surface calls its items — "field", "row", "parameter". It exists
    /// so the toolbar can say <b>"New field"</b> where the context menu says "New field", instead of the
    /// generic "Add item" the two used to disagree about. One noun per collection, named once here.</para>
    ///
    /// <para><see cref="Edit"/> and <see cref="Up"/>/<see cref="Down"/> are null for collections that have no
    /// such action — a grid whose rows are edited in place needs no Edit dialog, and an unordered collection
    /// no reorder. The toolbar hides what is null rather than greying it.</para>
    /// </summary>
    private sealed record CollectionCommands(
        string Noun,
        System.Windows.Input.ICommand Add,
        System.Windows.Input.ICommand Remove,
        System.Windows.Input.ICommand? Edit = null,
        System.Windows.Input.ICommand? Up = null,
        System.Windows.Input.ICommand? Down = null);

    // Section 3 — collection-edit router. Resolves the active editor's New/Edit/Delete/Move commands and the
    // noun its items go by. null when no editable collection is active (SQL editor, read-only system table,
    // View/Procedure in Source mode, …).
    private CollectionCommands? ActiveCollection()
    {
        switch (SelectedWorkspaceTab?.Kind)
        {
            case WorkspaceTabKind.TableDetail when SelectedWorkspaceTab.TableDetail is { } t:
                if (ShowFieldEditTools)
                    return new(UiStrings.CollectionNounField, t.AddFieldCommand, t.DropFieldCommand,
                        t.EditFieldCommand, t.MoveFieldUpCommand, t.MoveFieldDownCommand);
                if (ShowDataEditTools)
                    // Rows are edited in the grid itself and have no natural order, so no Edit and no ↑↓.
                    return new(UiStrings.CollectionNounRow, t.AddRowCommand, t.DeleteRowCommand);
                return null;
            case WorkspaceTabKind.NewTable when SelectedWorkspaceTab.NewTable is { } n:
                return new(UiStrings.CollectionNounField, n.AddFieldCommand, n.DeleteFieldCommand,
                    Up: n.MoveFieldUpCommand, Down: n.MoveFieldDownCommand);
            case WorkspaceTabKind.ViewDetail when SelectedWorkspaceTab.ViewDetail is { EasyMode: true } v:
                return new(UiStrings.CollectionNounColumn, v.AddColumnCommand, v.DeleteColumnCommand,
                    Up: v.MoveColumnUpCommand, Down: v.MoveColumnDownCommand);
            case WorkspaceTabKind.ProcedureDetail when SelectedWorkspaceTab.ProcedureDetail is { EasyMode: true } p:
                return new(UiStrings.CollectionNounItem, p.AddCollectionItemCommand, p.RemoveCollectionItemCommand,
                    Up: p.MoveCollectionItemUpCommand, Down: p.MoveCollectionItemDownCommand);
            case WorkspaceTabKind.TriggerDetail when SelectedWorkspaceTab.TriggerDetail is { EasyMode: true } tr:
                // Trigger Easy mode has a single editable collection — Variables.
                return new(UiStrings.CollectionNounVariable, tr.AddVariableCommand, tr.DeleteVariableCommand,
                    Up: tr.MoveVariableUpCommand, Down: tr.MoveVariableDownCommand);
            case WorkspaceTabKind.FunctionDetail when SelectedWorkspaceTab.FunctionDetail is { EasyMode: true } fn:
                // The Result sub-tab is a single record — no Add/Remove/Move there, so the
                // whole collection section is hidden; other sub-tabs route to the unified
                // commands (Arguments / Variables / Cursors / Subprograms).
                return fn.IsEasyCollectionEditable
                    ? new(UiStrings.CollectionNounItem, fn.AddCollectionItemCommand, fn.RemoveCollectionItemCommand,
                        Up: fn.MoveCollectionItemUpCommand, Down: fn.MoveCollectionItemDownCommand)
                    : null;
            default:
                return null;
        }
    }

    public bool ShowCollectionTools => ActiveCollection() is not null;
    public bool ShowCollectionReorder => ActiveCollection() is { Up: not null };
    /// <summary>The toolbar's Edit button exists only where the collection has an Edit action — the table
    /// fields grid, whose items open a dialog. It was missing entirely while the context menu offered it.</summary>
    public bool ShowCollectionEdit => ActiveCollection() is { Edit: not null };

    // The toolbar's collection tooltips. Computed rather than constants because they name the collection's own
    // noun AND carry the gesture from the catalog: "New field · F3" on the fields grid, "New parameter · F3"
    // on a procedure's arguments. Before this the toolbar said "Add item" while the menu beside it said
    // "New field" — the same command described two ways on one surface.
    public string CollectionAddTooltip => CollectionTip(CommandId.CollectionAdd, UiStrings.CollectionNewFormat);
    public string CollectionEditTooltip => CollectionTip(CommandId.CollectionEdit, UiStrings.CollectionEditFormat);
    public string CollectionRemoveTooltip => CollectionTip(CommandId.CollectionRemove, UiStrings.CollectionDeleteFormat);

    private string CollectionTip(CommandId id, string format)
    {
        var noun = ActiveCollection()?.Noun ?? UiStrings.CollectionNounItem;
        return CommandTip.For(id, string.Format(CultureInfo.CurrentCulture, format, noun));
    }

    private bool CanAddCollectionItem() => ActiveCollection()?.Add.CanExecute(null) ?? false;
    [RelayCommand(CanExecute = nameof(CanAddCollectionItem))]
    private void AddCollectionItem() => ActiveCollection()?.Add.Execute(null);

    private bool CanEditCollectionItem() => ActiveCollection() is { Edit: { } e } && e.CanExecute(null);
    [RelayCommand(CanExecute = nameof(CanEditCollectionItem))]
    private void EditCollectionItem() => ActiveCollection()?.Edit?.Execute(null);

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
        OnPropertyChanged(nameof(ShowCollectionEdit));
        // The tooltips name the ACTIVE collection's noun, so they change with the sub-tab exactly as the
        // buttons' visibility does — they have to be re-raised here or the strip would say "New field" while
        // pointing at a procedure's parameters.
        OnPropertyChanged(nameof(CollectionAddTooltip));
        OnPropertyChanged(nameof(CollectionEditTooltip));
        OnPropertyChanged(nameof(CollectionRemoveTooltip));
        OnPropertyChanged(nameof(ToolbarSep1Visible));
        OnPropertyChanged(nameof(ToolbarSep2Visible));
        OnPropertyChanged(nameof(ToolbarSep3Visible));
        OnPropertyChanged(nameof(ToolbarSep4Visible));
        AddCollectionItemCommand.NotifyCanExecuteChanged();
        EditCollectionItemCommand.NotifyCanExecuteChanged();
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
    [NotifyPropertyChangedFor(nameof(ShowResultsNotice))]
    [NotifyPropertyChangedFor(nameof(ResultsNoticeText))]
    [NotifyPropertyChangedFor(nameof(ShowLoadAllButton))]
    [NotifyPropertyChangedFor(nameof(CanExportResults))]
    [NotifyCanExecuteChangedFor(nameof(LoadAllRowsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportResultsCommand))]
    private string _currentResultVersionTag = string.Empty;

    // ── Results grid: client-side paging + 3-state sort ───────────────────
    //
    // Reuses the page-state SHAPE of TableDetailTabViewModel's pagination
    // (1-based page / First-Prev-Next-Last + Has*Page + hint), but the SQL
    // editor's result set is already materialized (capped at 5000 rows by the
    // executor), so paging + sorting run in-memory over CurrentResult.Rows —
    // no re-query. Sorting reuses the shared RowIndexComparer (object?[] by
    // column index). 3-state cycle: asc → desc → none (original order).

    // ⚠ This used to read "same default page size as the Table Data View (DataPreviewRowLimit)". That stopped
    // being true in Settings Center etap 6: DataPreviewRowLimit now follows the user's Data page size (§7.7)
    // while this stays 200 — deliberately, because ratified Q9 scopes that setting to the two SERVER-PAGED
    // grids. This one pages an already-materialized, already-capped result in memory, which is a different
    // question, so the two numbers are no longer the same number and must not be re-coupled by a comment.
    public const int ResultPageSize = 200;

    private List<object?[]> _sortedRows = new();
    private int? _resultSortColumn;       // null = no sort (original row order)
    private bool _resultSortDescending;
    private int _resultPage = 1;          // 1-based
    private int _selectedResultRowInPage = -1; // selection within the current page; -1 = none

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

    // IBExpert-style "Record N of M": absolute 1-based position of the selected
    // row across the full (sorted) result. When nothing is selected but rows
    // exist, falls back to "M rows". Empty when there are no rows.
    public string ResultRecordInfo
    {
        get
        {
            int total = _sortedRows.Count;
            if (total == 0) return string.Empty;
            // A truncated Preview loaded only the first N; the true total is unknown → "N+ (preview)"
            // so the fragment is unmissable even away from the notice bar.
            bool preview = CurrentResult is { Truncated: true };
            if (_selectedResultRowInPage >= 0)
            {
                int global = (_resultPage - 1) * ResultPageSize + _selectedResultRowInPage + 1;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    preview ? UiStrings.RecordPositionPreviewFormat : UiStrings.RecordPositionFormat,
                    global, total);
            }
            return string.Format(
                CultureInfo.CurrentCulture,
                preview ? UiStrings.RecordCountPreviewFormat : UiStrings.RecordCountFormat,
                total);
        }
    }

    // Called by the view when the results grid selection changes.
    public void SetResultSelectedRow(int indexInPage)
    {
        if (_selectedResultRowInPage == indexInPage) return;
        _selectedResultRowInPage = indexInPage;
        OnPropertyChanged(nameof(ResultRecordInfo));
    }

    // ── Results grid: shared filter panel + aggregation bar (client-side) ─────
    // Materialized set → filter/aggregate run in-memory over CurrentResult.Rows.
    public FilterPanelViewModel ResultFilterPanel { get; }
    public AggregationBarViewModel ResultAggregationBar { get; }
    private GridFilter _resultFilter = GridFilter.Empty;

    // Filter the materialized rows in-place (identical semantics to the SQL
    // push-down path via GridFilterEvaluator). Empty filter → all rows.
    private List<object?[]> ApplyResultFilter(IReadOnlyList<object?[]> rows)
    {
        if (_resultFilter.IsEmpty || CurrentResult is null) return new List<object?[]>(rows);
        var cols = CurrentResult.Columns;
        var list = new List<object?[]>();
        foreach (var r in rows)
            if (GridFilterEvaluator.Matches(r, _resultFilter, cols)) list.Add(r);
        return list;
    }

    // Host callback for the filter panel: re-slice from page 1 over the filtered
    // set, then recompute the aggregation lines against the new filtered rows.
    private Task ApplyResultFilterAsync(GridFilter filter)
    {
        _resultFilter = filter;
        _resultPage = 1;
        RebuildResultView();
        return ResultAggregationBar.RecomputeAllAsync();
    }

    // Host callback for the aggregation bar: compute over the filtered set (all
    // pages), not just the current page. _sortedRows already holds the filtered rows.
    private Task<object?> ComputeResultAggregateAsync(GridColumnRef col, GridAggregate agg)
        => Task.FromResult(GridAggregator.Compute(_sortedRows, col.Index, agg, col.ClrType));

    // New result set → drop filter + sort + return to page 1, re-point the panels
    // at the new columns, then recompute the view.
    partial void OnCurrentResultChanged(QueryResult? value)
    {
        // "Load all rows" replaces the truncated preview with the full set of the SAME query
        // and identical columns — keep the client-side view state (filter/sort/aggregation) the
        // user set on the preview and just re-slice over the larger row set. Re-pointing the
        // panels at columns would clear the filter conditions, so skip it on this path.
        if (_preserveViewStateOnNextResult)
        {
            _preserveViewStateOnNextResult = false;
            RebuildResultView();
            return;
        }

        _resultFilter = GridFilter.Empty;
        _resultSortColumn = null;
        _resultSortDescending = false;
        _resultPage = 1;
        var cols = GridColumnRef.From(value is { HasResultSet: true } ? value.Columns : null);
        ResultFilterPanel.SetColumns(cols);
        ResultAggregationBar.SetColumns(cols);
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
            // Filter first (client-side), then sort + page over the filtered set.
            var list = ApplyResultFilter(rows);
            if (_resultSortColumn is int col)
            {
                var comparer = new RowIndexComparer(col);
                list.Sort((a, b) => _resultSortDescending ? comparer.Compare(b, a) : comparer.Compare(a, b));
            }
            _sortedRows = list;

            if (list.Count == 0)
            {
                _resultPage = 1;
                PagedResultRows = Array.Empty<object?[]>();
            }
            else
            {
                if (_resultPage > TotalResultPages) _resultPage = TotalResultPages;
                if (_resultPage < 1) _resultPage = 1;

                int start = (_resultPage - 1) * ResultPageSize;
                int count = Math.Min(ResultPageSize, list.Count - start);
                PagedResultRows = count > 0 ? list.GetRange(start, count) : Array.Empty<object?[]>();
            }
        }

        // Re-slicing the page drops any grid selection; reset the record pointer.
        _selectedResultRowInPage = -1;

        OnPropertyChanged(nameof(PagedResultRows));
        OnPropertyChanged(nameof(HasResultPreviousPage));
        OnPropertyChanged(nameof(HasResultNextPage));
        OnPropertyChanged(nameof(ResultPage));
        OnPropertyChanged(nameof(ResultPaginationHint));
        OnPropertyChanged(nameof(ResultRecordInfo));
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

    /// <summary>Shared live execution timer — ticks while the SQL Editor, Execute Procedure, or
    /// Execute Function is running (all three run through this VM). Started/stopped automatically
    /// (SQL Editor: <see cref="OnIsExecutingChanged"/>; proc/func: <see cref="RunExecutableWithMetricsAsync"/>).</summary>
    public ExecutionTimer ExecutionTimer { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExecute))]
    [NotifyPropertyChangedFor(nameof(ShowExecuteButton))]
    [NotifyPropertyChangedFor(nameof(ShowCancelButton))]
    [NotifyPropertyChangedFor(nameof(ShowResultsNotice))]
    [NotifyPropertyChangedFor(nameof(ShowLoadAllButton))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryFullCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadAllRowsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelQueryCommand))]
    [NotifyPropertyChangedFor(nameof(RailBrushKey))]
    private bool _isExecuting;

    /// <summary>Set the moment Cancel is clicked, cleared when the run unwinds. Without it the
    /// button looks inert while the server is aborting the statement, so the user clicks again
    /// and again (the reported symptom). It disables Cancel and switches the status text, so the
    /// first click visibly registers.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelQueryCommand))]
    private bool _isCancelling;

    /// <summary>Cancel is clickable exactly once per run.</summary>
    public bool CanCancelQuery => IsExecuting && !IsCancelling;

    /// <summary>
    /// Sekcja postępu paska statusu (§8.4.6). ⭐ Jest GLOBALNA — operacja widoczna niezależnie od
    /// aktywnej zakładki. Tu trafia stan „operacja TRWA"; wynik zakończonej operacji zostaje
    /// w <see cref="QueryStatsText"/>, które jest lokalne dla edytora SQL (§19.7.1).
    /// </summary>
    public StatusProgressViewModel Progress { get; } = new();

    // Drive the live elapsed timer off IsExecuting so every SQL Editor exit path (success, error,
    // cancel, finally) starts/stops it with no scattering.
    //
    // ⭐ Od M3.1f ten sam punkt prowadzi sekcję postępu, i z tego samego powodu: `IsExecuting` jest
    // JEDYNYM miejscem, przez które przechodzi każde wejście i wyjście z wykonania — sukces, błąd,
    // anulowanie i `finally`. Podpięcie postępu tutaj oznacza, że nie da się dodać ścieżki wyjścia,
    // która zostawi zapalony pasek. ⛔ Nie wołać `Progress.Begin/End` z gałęzi wykonania.
    partial void OnIsExecutingChanged(bool value)
    {
        if (value) ExecutionTimer.Start();
        else ExecutionTimer.Stop();
        if (!value) IsCancelling = false;   // every exit path resets the cancel latch

        // ⚠ Od M3b.1 ten punkt nie zapala sekcji SAM — mówi „przelicz", a rozstrzyga
        // `UpdateProgressSection` (drabinka priorytetów, trzy źródła). Rola `IsExecuting` jako
        // jedynego leja wejścia/wyjścia zostaje bez zmian: wyjście z wykonania nadal gasi sekcję tą
        // jedną drogą, o ile żadne inne źródło nie jest w toku.
        // ⚠ Szczegół etykiety czyszczony w OBIE strony: przy wejściu, żeby nowe zapytanie nie startowało
        // z licznikiem poprzedniego, i przy wyjściu, żeby zakończone nie zostawiło tekstu następnemu.
        _queryProgressDetail = null;

        // ⚠ Komenda anulowania jest PRZEKAZYWANA, nie odtwarzana (patrz `ResolveProgressSection`): pasek
        // statusu i przycisk w toolbarze naciskają ten sam obiekt `CancelQueryCommand`, więc zatrzask
        // `IsCancelling` gasi oba naraz.
        UpdateProgressSection();

        OnPropertyChanged(nameof(CanCancelQuery));
    }

    partial void OnIsCancellingChanged(bool value) => OnPropertyChanged(nameof(CanCancelQuery));

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private int _selectedBottomTabIndex;

    [ObservableProperty]
    private string _queryStatsText = string.Empty;

    // ⭐⭐ SEKCJA 2 PASKA STATUSU — komunikat aplikacji (product-polish.md §8.4.3).
    //
    // ⚠⚠ To POWSTAŁO Z ROZDZIELENIA jednej właściwości i tam leżała prawdziwa treść defektu H‑4.
    // Audyt opisał go jako „stan połączenia i komunikaty mają identyczną wagę wizualną" — w rzeczywistości
    // były TĄ SAMĄ WŁAŚCIWOŚCIĄ (`StatusText`): `UpdateStatusFromConnection` pisało do niej „Connected to X",
    // a `SetError` treść wyjątku, więc nadpisywały się nawzajem. Żadne stylowanie nie mogło tego naprawić,
    // bo nie było czego stylować osobno. Sekcja 1 czyta dziś `ConnectionDisplayName`/`ConnectionEndpoint`.
    //
    // ⚠ Severity jest ENUMEM, nie boolem (`IsStatusError` zniknął): §8.4.4 wymaga ikony i barwy per severity,
    // a mapowanie czytamy z `MessageBanner` — tego samego, którym maluje się log Messages w edytorze SQL.
    // ⛔ Nie dopisywać tu drugiej definicji severity.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    [NotifyPropertyChangedFor(nameof(RailBrushKey))]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessageBrushKey))]
    [NotifyPropertyChangedFor(nameof(StatusMessageGeometryKey))]
    [NotifyPropertyChangedFor(nameof(RailBrushKey))]
    private MessageSeverity _statusMessageSeverity = MessageSeverity.Info;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string StatusMessageBrushKey => MessageBanner.BrushKeyFor(StatusMessageSeverity);

    public string StatusMessageGeometryKey => MessageBanner.GeometryKeyFor(StatusMessageSeverity);

    public string AppTitle => UiStrings.AppTitle;
    public string AppSubtitle => UiStrings.AppSubtitle;

    /// <summary>
    /// The status bar's product + version chip.
    ///
    /// <para>⭐ It reads <see cref="AppInfo"/>, i.e. the assembly, i.e. <c>&lt;Version&gt;</c> in
    /// <c>Directory.Build.props</c> — the same single source the About window uses. It replaced the literal
    /// <c>"EmberTern 0.1.0"</c> that had been typed into <c>MainWindow.axaml</c>: a second source of truth that
    /// had gone stale and then contradicted About on screen, in front of the user, which is exactly how a
    /// hand-typed copy fails (gotcha #284). <c>AppInfoTests</c> now fails on any version literal in this
    /// project.</para>
    /// </summary>
    // ⛔ `AppVersionChip` USUNIĘTY w M3.1b — decyzja D3: nazwa aplikacji i numer wersji nie należą do paska
    // statusu, tylko do okna About, gdzie są tematem. Pasek statusu odpowiada na pytanie „co się dzieje
    // TERAZ", a wersja produktu nie zmienia się nigdy w trakcie sesji. `AppInfo` pozostaje jedynym źródłem
    // wersji i czyta ją About; gwarancje `AppInfoTests` (żadnego literału wersji w kodzie) są nietknięte.
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

    // ⭐⭐ SEKCJA 1 PASKA STATUSU — tożsamość połączenia (product-polish.md §8.4.3).
    //
    // ⚠ Od M3.1b to jest JEDYNE miejsce w aplikacji, które odpowiada na pytanie „gdzie jestem podłączony?".
    // Nazwa połączenia i badge DEV MODE zostały USUNIĘTE z paska tytułu — decyzja użytkownika (2026-08-02):
    // „jedna informacja powinna mieć jednego właściciela… pasek tytułu będzie odpowiadał wyłącznie za
    // nawigację i polecenia". ⛔ Nie przywracać ich na górę; to nie jest brak, tylko rozstrzygnięcie (§19.3).
    //
    // Rozdział ról jest częścią wymagania (§8.4.4): nazwa niesie `Text.Status` SemiBold i pełny kontrast,
    // endpoint `Text.Caption` w kolorze drugorzędnym. Kropka stanu odpowiada za „połączony / nie".
    public string ConnectionDisplayName
        => HasActiveConnection ? ActiveConnectionName : UiStrings.StatusBarDisconnected;

    // ⚠ Etykieta niesie SEPARATOR, a nie sam endpoint, i to nie jest przemycanie prezentacji do VM:
    // nazwa i endpoint renderują się jako DWA RUNY W JEDNYM `TextBlocku` (§19.3.1), więc nie ma między
    // nimi `Spacing` kontenera, który mógłby je rozdzielić. Odstęp musi być częścią tekstu. Kropka
    // środkowa odwzorowuje makietę z §8.4.3: `● Szkoleniowa · localhost:3050 · DEV`.
    public string ConnectionEndpointLabel
        => _service.ActiveProfile is { } p ? $" · {p.Host}:{p.Port}" : string.Empty;

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐ RAIL STATUS BARA (§8.4.1–§8.4.2)
    //
    // Rail odpowiada na pytanie „co aplikacja ROBI teraz" i pokazuje DOKŁADNIE JEDEN stan — ten
    // o najwyższym priorytecie spośród aktywnych. Chipy (§8.4.5, M3.1d–e) odpowiadają na inne pytanie
    // („co jest PRAWDĄ teraz") i współistnieją dowolnie. ⛔ Nie mieszać tych dwóch ról.
    //
    // ⚠ Kolor NIGDY nie jest jedynym nośnikiem (§10): każdy stan ma też tekst albo ikonę w sekcji 2.
    // Rail jest wzmocnieniem czytelnym kątem oka, a nie komunikatem.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Klucz pędzla railu — stan o najwyższym priorytecie spośród aktywnych (§8.4.2).
    /// Kolejność jest uzasadniona, nie dowolna: błąd wygrywa ze wszystkim, bo wymaga reakcji;
    /// debug wygrywa z wykonywaniem, bo krokowanie JEST wykonywaniem i „wykonuję" byłoby prawdziwe
    /// i bezużyteczne; trace przegrywa z wykonywaniem, bo jest tłem pracy, a nie jej treścią.
    /// </summary>
    public string RailBrushKey =>
        HasStatusMessage && StatusMessageSeverity == MessageSeverity.Error ? "ErrorBrush"        // 5
        : HasStatusMessage && StatusMessageSeverity == MessageSeverity.Warning ? "WarningBrush"  // 4
        : IsDebugSessionLive ? "DebugCurrentLineBarBrush"                                        // 3
        : IsExecuting ? "AccentBrush"                                                            // 2
        : IsTraceSessionLive ? "IconColor_Query"                                                 // 1
        : "BorderBrush";                                                                         // 0 — separator

    /// <summary>
    /// Czy GDZIEKOLWIEK żyje sesja debuggera. ⚠ To NIE jest `IsDebuggerTabActive`, które znaczy
    /// „ta zakładka jest wybrana" — rail i chip mają być prawdziwe także wtedy, gdy użytkownik patrzy
    /// na inną zakładkę (§8.4: czas życia sygnału to „dopóki warunek jest prawdziwy"). Żywa sesja to
    /// `Busy` albo `Paused`; `Completed`/`Faulted` są terminalne (stan zostaje widoczny do inspekcji,
    /// ale sesji już nie ma), a `Preparing`/`ReadyToLaunch`/`Idle`/`Editing` jej jeszcze albo już nie mają.
    /// </summary>
    public bool IsDebugSessionLive => WorkspaceTabs
        .Any(t => t.Debugger is { Phase: DebuggerPhase.Busy or DebuggerPhase.Paused });

    /// <summary>
    /// Czy GDZIEKOLWIEK żyje sesja Trace — czyli istnieje sesja na serwerze: wszystko poza
    /// `Stopped` i `Faulted`.
    /// </summary>
    public bool IsTraceSessionLive => WorkspaceTabs
        .Any(t => t.TraceMonitor is { State: TraceSessionState.Starting or TraceSessionState.Running
                                          or TraceSessionState.Paused or TraceSessionState.Stopping });

    // ⚠⚠ Subskrypcje MUSZĄ być zdejmowane. Agregacja czyta stan zakładek, więc zakładka zamknięta, ale
    // wciąż podpięta, trzymałaby rail zapalony (a od M3b.1 także pasek postępu) po czymś, czego już nie
    // ma. `Reset` (czyli `Clear()` przy rozłączeniu) NIE niesie `OldItems`, dlatego trzymamy własny zbiór
    // podpiętych zakładek — bez niego odpięcie przy rozłączeniu byłoby niewykonalne.
    //
    // ⭐ NAZWA MÓWI „AKTYWNOŚĆ", NIE „RAIL", I OD M3b.1 JEST TO JUŻ TRZECI KONSUMENT tej jednej
    // agregacji: rail (jeden stan o najwyższym priorytecie), chipy (współistniejące fakty) i sekcja
    // postępu (jedna operacja, drabinka priorytetów). `RaiseActivityChanged` nosiło tę nazwę od M3.1e
    // właśnie z tego powodu; reszta mechanizmu została przy „rail" i była już historią, nie
    // odpowiedzialnością. ⛔ Nie zakładać drugiego zbioru subskrypcji dla postępu — jeden zbiór, jeden
    // punkt odpinania, więc nie da się dodać źródła, które przeżyje swoją zakładkę.
    private readonly HashSet<WorkspaceTabViewModel> _activitySources = [];

    private void WireActivitySource(WorkspaceTabViewModel tab)
    {
        if (tab.Debugger is null && tab.TraceMonitor is null
            && tab.DataImport is null && tab.ScriptExecutor is null) return;
        if (!_activitySources.Add(tab)) return;

        if (tab.Debugger is { } dbg) dbg.PropertyChanged += OnActivitySourceChanged;
        if (tab.TraceMonitor is { } trace) trace.PropertyChanged += OnActivitySourceChanged;
        if (tab.DataImport is { } import) import.PropertyChanged += OnActivitySourceChanged;
        if (tab.ScriptExecutor is { } script) script.PropertyChanged += OnActivitySourceChanged;
        RaiseActivityChanged();
    }

    private void UnwireActivitySource(WorkspaceTabViewModel tab)
    {
        if (!_activitySources.Remove(tab)) return;

        if (tab.Debugger is { } dbg) dbg.PropertyChanged -= OnActivitySourceChanged;
        if (tab.TraceMonitor is { } trace) trace.PropertyChanged -= OnActivitySourceChanged;
        if (tab.DataImport is { } import) import.PropertyChanged -= OnActivitySourceChanged;
        if (tab.ScriptExecutor is { } script) script.PropertyChanged -= OnActivitySourceChanged;
        RaiseActivityChanged();
    }

    /// <summary>
    /// Faza 3 ładowania połączenia. ⭐ Jej zakończenie gasi RÓWNIEŻ fazę 1 — i to jest to jedno miejsce,
    /// które domyka cały odcinek udanego połączenia, bo prefetch ma swoje `finally` i nie da się go pominąć.
    /// </summary>
    private void OnMetadataProgressChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MetadataExplorerViewModel.IsLoadingMetadata)
                               or nameof(MetadataExplorerViewModel.MetadataCategoriesLoaded))) return;

        if (e.PropertyName == nameof(MetadataExplorerViewModel.IsLoadingMetadata)
            && !Metadata.IsLoadingMetadata)
        {
            IsConnecting = false;
        }

        UpdateProgressSection();
    }

    private void OnActivitySourceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Tylko te właściwości zmieniają odpowiedź; reszta zdarzeń zakładki nas nie dotyczy.
        // ⚠ Rail i chipy czytają dwie pierwsze; sekcja postępu pozostałe. Filtr jest wspólny, bo
        // `RaiseActivityChanged` przelicza całość — rozdzielenie go dałoby dwa filtry do utrzymania
        // przy jednym zdarzeniu.
        if (e.PropertyName is nameof(DebuggerTabViewModel.Phase)
                           or nameof(TraceMonitorTabViewModel.State)
                           or nameof(DataImportTabViewModel.IsRunning)
                           or nameof(DataImportTabViewModel.IsRecalculating)
                           or nameof(DataImportTabViewModel.ProgressRowsRead)
                           or nameof(DataImportTabViewModel.ProgressPercent)
                           or nameof(DataImportTabViewModel.IsProgressIndeterminate)
                           or nameof(ScriptExecutorTabViewModel.IsRunning)
                           or nameof(ScriptExecutorTabViewModel.CompletedStatementCount))
        {
            RaiseActivityChanged();
        }
    }

    // ⚠ Nazwa mówi „aktywność", nie „rail", i to jest celowe: od M3.1e ta sama agregacja karmi DWÓCH
    // konsumentów o różnych rolach — rail (jeden stan, ten o najwyższym priorytecie) i chipy
    // (współistniejące fakty). Od M3b.1 jest TRZECI: sekcja postępu. Nazwa `RaiseRailChanged` byłaby
    // od tej iteracji historią, nie odpowiedzialnością.
    private void RaiseActivityChanged()
    {
        OnPropertyChanged(nameof(IsDebugSessionLive));
        OnPropertyChanged(nameof(IsTraceSessionLive));
        OnPropertyChanged(nameof(RailBrushKey));
        OnPropertyChanged(nameof(DebugChipTooltip));
        OnPropertyChanged(nameof(TraceChipTooltip));
        UpdateProgressSection();
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐ SEKCJA POSTĘPU — ARBITRAŻ ŹRÓDEŁ (§8.4.6, M3b.1)
    //
    // M3.1f dostarczyło infrastrukturę i JEDNO źródło, więc `OnIsExecutingChanged` wołało
    // `Progress.Begin/End` wprost. Przy trzech źródłach to przestaje wystarczać: potrzebny jest jeden
    // punkt, który rozstrzyga, KTÓRA operacja jest w sekcji widoczna. Dlatego od tej iteracji każde
    // źródło mówi tylko „przelicz", a odpowiedź składa wyłącznie `UpdateProgressSection`.
    // ⛔ Nie wołać `Progress.Begin`/`Report`/`End` z żadnego innego miejsca — drugi pisarz to drugi
    // właściciel stanu sekcji, a stan sekcji jest jeden.
    //
    // ⭐ DRABINKA PRIORYTETÓW (ratyfikowana przez użytkownika 2026-08-04). Sekcja odpowiada na pytanie
    // „NA CO CZEKAM TERAZ", i to jest całe uzasadnienie kolejności:
    //   3. połączenie / ładowanie metadanych — dopóki nie skończy, nie działa nic innego (M3b.2);
    //   2. zapytanie i skrypt — operacja interaktywna, dopiero co uruchomiona: to jest to, na co
    //      użytkownik faktycznie czeka, i kończy się szybko, oddając sekcję;
    //   1. import — długie tło; ma WŁASNĄ transakcję (I7.5), własny pasek i własny Cancel w swojej
    //      zakładce, więc pozostaje w pełni sterowalny bez paska statusu.
    // ⚠ Zapytanie i skrypt są JEDNYM szczeblem celowo, nie przez niedopatrzenie: konkurują o linię
    // Data (`FirebirdScriptExecutor.RunAsync` odmawia przy otwartej transakcji), więc nie nakładają się
    // w sposób, dla którego warto wymyślać regułę. ⛔ Nie pisać reguły dla przypadku, którego nie da
    // się osiągnąć — byłaby to bezczynna gałąź udająca decyzję projektową.
    //
    // ⚠⚠ Model niesie JEDNĄ operację, więc druga trwająca jest w sekcji niewidoczna — i to jest wybór,
    // nie ubytek: przeskakiwanie sekcji między zadaniami byłoby myląca, a licznik ukrytych operacji
    // dokładałby element, którego §8.4.6 nie przewiduje (decyzja użytkownika). Operacja niewidoczna
    // pozostaje widoczna i anulowalna na SWOJEJ powierzchni.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Które źródło zajmuje sekcję postępu. `None` znaczy „sekcja zgaszona".</summary>
    private enum ProgressOwner { None, Connecting, ConnectionLoading, Query, Script, Import, ImportReading }

    private ProgressOwner _progressOwner = ProgressOwner.None;

    // Szczegół etykiety zapytania (liczba wczytanych wierszy / „Cancelling…"), zapamiętany TUTAJ, bo
    // producenci postępu wołają się częściej niż zmienia się właściciel sekcji, a `Report` nie umie
    // odtworzyć tekstu, którego nie dostał. ⚠ Czyszczone przy każdym wejściu w wykonanie — inaczej
    // następne zapytanie startowałoby z licznikiem poprzedniego.
    private string? _queryProgressDetail;

    /// <summary>
    /// JEDYNY punkt, który zapala, aktualizuje i gasi sekcję postępu. Wywoływany przez
    /// <see cref="RaiseActivityChanged"/>, więc każde źródło trafia tu tą samą drogą.
    /// <para>⭐ Rozróżnienie „zmiana właściciela" vs „raport tego samego właściciela" jest konieczne:
    /// <c>Begin</c> resetuje tryb, procent i komendę anulowania, więc wołany przy każdym raporcie
    /// zgasiłby tryb procentowy w połowie operacji, która go właśnie ustawiła.</para>
    /// </summary>
    private void UpdateProgressSection()
    {
        var (owner, label, percent, cancel) = ResolveProgressSection();

        if (owner == ProgressOwner.None)
        {
            if (_progressOwner != ProgressOwner.None)
            {
                _progressOwner = ProgressOwner.None;
                Progress.End();
            }
            return;
        }

        if (owner != _progressOwner)
        {
            _progressOwner = owner;
            Progress.Begin(label, cancel);
        }

        if (percent is { } value) Progress.Report(label, value);
        else Progress.Report(label);
    }

    // Czysty wybór: kto zajmuje sekcję i co mówi. Rozdzielony od stosowania, żeby drabinkę priorytetów
    // dało się sprawdzić testem bez `StatusProgressViewModel` i bez żywego wykonania.
    private (ProgressOwner Owner, string Label, double? Percent, ICommand? Cancel) ResolveProgressSection()
    {
        // Szczebel 3 — ładowanie połączenia (§19.34). Najwyżej, bo dopóki nie skończy, nie działa nic
        // innego: nie ma drzewa, uzupełniania ani diagnostyki.
        // ⭐ DWIE flagi, JEDEN szczebel. Faza 1 (otwarcie dołączeń) i faza 3 (prefetch kategorii) mają
        // każda własne `finally`, więc żadna nie może zostać zapalona — a użytkownik widzi jeden ciągły
        // pasek, bo pomiędzy nimi leży faza 2, która blokuje wątek UI i w której NIC się nie odmalowuje.
        // ⚠ Faza 2 nie ma własnej etykiety świadomie: odmalowanie następuje PRZED nią, więc napis
        // ustawiony na jej początku pojawiłby się dopiero po jej zakończeniu, czyli gdy jest już
        // nieprawdziwy. Zamiast martwego UI zostaje etykieta fazy 1 (decyzja użytkownika 2026-08-04).
        if (Metadata.IsLoadingMetadata)
        {
            var total = Metadata.MetadataCategoriesTotal;
            var done = Metadata.MetadataCategoriesLoaded;
            return (ProgressOwner.ConnectionLoading,
                    string.Format(CultureInfo.CurrentCulture, UiStrings.StatusProgressMetadataFormat, done, total),
                    total > 0 ? done * 100d / total : null,
                    null);
        }

        if (IsConnecting)
        {
            return (ProgressOwner.Connecting, UiStrings.StatusProgressConnecting, null, null);
        }

        // Szczebel 2 — operacja interaktywna. Zapytanie: tryb nieokreślony, bo strumieniowy odczyt nie
        // zna sumy wierszy, dopóki nie skończy (§19.7.2).
        if (IsExecuting)
        {
            return (ProgressOwner.Query,
                    _queryProgressDetail ?? UiStrings.ExecutingStatus,
                    null,
                    CancelQueryCommand);
        }

        // Szczebel 2 — skrypt. ⭐ Jedyne źródło ze ŚCISŁĄ sumą, więc pierwszy żywy konsument ścieżki
        // procentowej, która od M3.1f nie miała żadnego (§19.7.2).
        if (WorkspaceTabs.Select(t => t.ScriptExecutor).FirstOrDefault(s => s is { IsRunning: true }) is { } script)
        {
            var total = script.RunStatementTotal;
            var done = script.CompletedStatementCount;
            return (ProgressOwner.Script,
                    string.Format(CultureInfo.CurrentCulture, UiStrings.StatusProgressScriptFormat, done, total),
                    total > 0 ? done * 100d / total : null,
                    script.StopCommand);
        }

        // Szczebel 1 — import. ⚠ Suma jest tylko SZACUNKIEM (`SourceSchema.EstimatedRows`) i bywa
        // nieznana; wtedy `IsProgressIndeterminate` jest prawdą i procentu nie podajemy, bo pasek
        // stojący na zerze udawałby „0% zrobione".
        if (WorkspaceTabs.Select(t => t.DataImport).FirstOrDefault(i => i is { IsRunning: true }) is { } import)
        {
            return (ProgressOwner.Import,
                    string.Format(CultureInfo.CurrentCulture, UiStrings.StatusProgressImportFormat, import.ProgressRowsRead),
                    import.IsProgressIndeterminate ? null : import.ProgressPercent,
                    import.CancelRunCommand);
        }

        // Szczebel 1 — odczyt źródła przed importem (M3b.1c). ⭐ Ten sam szczebel co import, bo to ta sama
        // powierzchnia w innej fazie, i PONIŻEJ samego importu, bo trwający import jest ważniejszy niż
        // przeliczanie konfiguracji.
        // ⚠ Bez komendy anulowania: łańcuch ma własny CTS, ale użytkownik nie ma dla niego przycisku, a
        // wymyślanie go tutaj byłoby dodaniem funkcji pod pozorem podłączenia postępu. `HasCancel` = false.
        // ⚠ Tryb nieokreślony — ten odcinek nie zna żadnej sumy (§19.32).
        if (WorkspaceTabs.Select(t => t.DataImport).FirstOrDefault(i => i is { IsRecalculating: true }) is { } reading)
        {
            return (ProgressOwner.ImportReading,
                    reading.Source.UseFile
                        ? UiStrings.StatusProgressImportReadingFile
                        : UiStrings.StatusProgressImportReadingClipboard,
                    null,
                    null);
        }

        return (ProgressOwner.None, string.Empty, null, null);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐ CHIPY TRACE I DEBUGGERA (§8.4.3 sekcja 3, §8.4.5) — M3.1e
    //
    // Ten sam podział własności, który M3.1d wprowadziło dla transakcji, obowiązuje tu bez zmian:
    //   • sekcja 2 pokazuje `ActiveDebugger.StatusText` — kontekst AKTYWNEJ zakładki („Paused, linia 14"),
    //   • chip odpowiada GLOBALNIE: „gdzieś żyje sesja debuggera", także gdy patrzysz na inną zakładkę.
    // ⛔ To nie jest redundancja — bramka `IsDebuggerTabActive` czyni tamten nośnik niewidocznym
    // dokładnie tam, gdzie fakt globalny ma znaczenie (§0.1.2).
    //
    // ⚠⚠ CHIPY NIE DZIEDZICZĄ PĘDZLI RAILU, i to jest decyzja poparta pomiarem, nie estetyką.
    // `DebugCurrentLineBarBrush` (rail debuggera, §19.4.1) jest PÓŁPRZEZROCZYSTY (α 0,90 / 0,80) i na
    // tle `PanelBrush` daje 3,77:1 w Dark. Jako 2 px rail przechodzi (próg §10 dla elementu UI to 3:1);
    // jako TEKST 10 px nie przechodzi (próg 4,5:1). Ten sam token, dwa progi, dwa werdykty.
    // Chip debuggera bierze więc `AccentIconBrush` — 5,17:1 Dark / 4,81:1 Light — czyli dokładnie ten
    // kolor, który trójkąt `DebuggerIcon` i tak nosi, dzięki czemu znak i napis czytają się jako
    // JEDEN element. Trace zostaje na `IconColor_Query` (8,03:1 / 6,58:1), bo ten próg spełnia.
    // ⛔ Nie „ujednolicać" tego z railem bez ponownego policzenia kontrastu.
    //
    // ⏸ Pełna semantyka kolorów aktywności (SQL / Debugger / Trace / Import — każdy własny, jednoznaczny
    // kolor) pozostaje odłożona do M3b i bramy §13.3 (§19.4.4). Tutaj rozstrzygamy wyłącznie tyle, ile
    // trzeba, żeby chip był czytelny.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    // ⚠ Widoczność chipów bierze się WPROST z `IsDebugSessionLive` / `IsTraceSessionLive` — bez aliasów
    // typu `ShowDebugChip`. Alias byłby czwartą właściwością do podniesienia w `RaiseActivityChanged`,
    // a jego pominięcie NIE zawodzi buildu ani testu: chip po prostu nigdy się nie pojawia. Warunek
    // pokazania jest tu tożsamy z faktem, więc druga nazwa nie niesie nic poza ryzykiem.
    // (Chip transakcji ma `ShowTransactionChip`, bo tam warunek jest ZŁOŻONY: aktywna LUB błąd.)

    /// <summary>
    /// Szczegóły żywych sesji debuggera — nazwa procedury plus status każdej z nich.
    /// ⭐ Tekst statusu czyta z <c>DebuggerTabViewModel.StatusText</c>, czyli z tego samego producenta,
    /// którym karmi się sekcja 2. Etykieta chipa niesie FAKT, tooltip niesie SZCZEGÓŁ — i żaden z nich
    /// nie jest drugą definicją stanu debuggera.
    /// </summary>
    public string DebugChipTooltip => string.Join(
        Environment.NewLine,
        WorkspaceTabs
            .Where(t => t.Debugger is { Phase: DebuggerPhase.Busy or DebuggerPhase.Paused })
            .Select(t => string.IsNullOrWhiteSpace(t.ObjectName)
                ? t.Debugger!.StatusText
                : $"{t.ObjectName} — {t.Debugger!.StatusText}"));

    /// <summary>
    /// Szczegóły żywej sesji Trace — reużywa <c>TraceMonitorTabViewModel.StatusText</c>
    /// („Recording · 12/40 events”), który już istnieje i już mapuje <c>TraceSessionState</c> na tekst.
    /// ⛔ Zero drugiego mapowania stanu Trace w aplikacji.
    /// </summary>
    public string TraceChipTooltip => string.Join(
        Environment.NewLine,
        WorkspaceTabs
            .Where(t => t.TraceMonitor is { State: TraceSessionState.Starting or TraceSessionState.Running
                                                or TraceSessionState.Paused or TraceSessionState.Stopping })
            .Select(t => t.TraceMonitor!.StatusText));

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐ CHIP TRANSAKCJI (§8.4.5) — najważniejsza informacja w pasku
    //
    // Chip odpowiada GLOBALNIE na pytanie „czy aplikacja ma otwartą transakcję i od jak dawna?".
    // Pasek nad wynikami edytora SQL odpowiada na inne, LOKALNE: „ile instrukcji poszło w tej
    // transakcji". ⭐ To nie jest redundancja, tylko dwa poziomy informacji (decyzja użytkownika,
    // 2026-08-02): przechodząc do debuggera albo edytora obiektu nadal chcę wiedzieć, że mam otwartą
    // transakcję — ale licznik instrukcji przestaje mnie wtedy obchodzić, bo nie pracuję w edytorze.
    //
    // ⚠⚠ CHIP NIGDY NIE JEST PRZYCISKIEM (§8.4.5). Commit i Rollback zostają w toolbarze pod F6 /
    // Shift+F6. Operacja nieodwracalna nie może stać w miejscu, które użytkownik czyta kątem oka.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Moment otwarcia bieżącej transakcji — albo <c>null</c>, gdy żadna nie jest otwarta.
    /// ⭐ Mierzymy CZAS PO STRONIE KLIENTA i to jest rozstrzygnięcie z iteracji 0 (§19.0.2):
    /// `TransactionService` nie ma znacznika czasu, ale ma zdarzenie `TransactionStateChanged`,
    /// które ten VM już subskrybuje. Zero zapytań do `MON$`, zero round-tripów, zero zmian w Core
    /// i w warstwie Firebird. ⚠ Konsekwencja przyjęta świadomie: to czas od chwili, w której
    /// EmberTern otworzył transakcję, a nie odczyt z serwera — dla pytania „jak długo TO trzymam"
    /// jest to właściwa odpowiedź.
    /// </summary>
    private DateTimeOffset? _transactionStartedAt;

    private DispatcherTimer? _transactionChipTimer;

    public bool ShowTransactionChip => IsTransactionActive || IsTransactionError;

    public string TransactionChipBrushKey => IsTransactionError ? "ErrorBrush" : "TransactionActiveBrush";

    /// <summary>Tooltip chipa — profil i lane (§8.4.5). Tekst chipa zostaje krótki; szczegóły na żądanie.</summary>
    public string TransactionChipTooltip => DataTransactionProfileTooltip;

    /// <summary>Treść chipa: etykieta + czas trwania, np. „Transakcja · 2 min".</summary>
    public string TransactionChipText => _transactionStartedAt is { } started
        ? string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.StatusBarTransactionChipFormat,
            FormatTransactionDuration(DateTimeOffset.UtcNow - started))
        : UiStrings.StatusBarTransactionChipBare;

    /// <summary>
    /// Czas trwania w postaci zgrubnej i czytelnej kątem oka. ⭐ Funkcja CZYSTA — bierze `TimeSpan`,
    /// nie zegar — więc daje się przetestować bez timera i bez czekania (gotcha #251: wyzwalacz
    /// oparty wyłącznie na timerze jest nieosiągalny dla testu headless).
    /// ⚠ Zgrubna świadomie: pasek statusu czyta się kątem oka, a „02:37.4" wymagałoby czytania.
    /// </summary>
    internal static string FormatTransactionDuration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        if (elapsed.TotalMinutes < 1)
            return string.Format(CultureInfo.CurrentCulture, UiStrings.DurationSecondsFormat, (int)elapsed.TotalSeconds);

        if (elapsed.TotalHours < 1)
            return string.Format(CultureInfo.CurrentCulture, UiStrings.DurationMinutesFormat, (int)elapsed.TotalMinutes);

        return string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.DurationHoursFormat,
            (int)elapsed.TotalHours,
            elapsed.Minutes);
    }

    // ⚠ Timer odświeża WYŁĄCZNIE wyświetlany tekst — fakt (`_transactionStartedAt`) jest od niego
    // niezależny i testowalny. Chodzi co sekundę i tylko wtedy, gdy transakcja jest otwarta; przy
    // Idle jest zatrzymywany, żeby aplikacja nie tykała w nieskończoność po zamkniętej transakcji.
    private void UpdateTransactionChipTimer()
    {
        if (ShowTransactionChip)
        {
            _transactionChipTimer ??= new DispatcherTimer(
                TimeSpan.FromSeconds(1),
                DispatcherPriority.Background,
                (_, _) => OnPropertyChanged(nameof(TransactionChipText)));
            _transactionChipTimer.Start();
        }
        else
        {
            _transactionChipTimer?.Stop();
        }
    }

    // Title-bar transaction-profile block: two stacked lines, each a static lane label
    // ("Data:" / "Meta:") plus the full profile name in a lane-colored badge. These
    // expose the badge text (profile name only); the label prefix is static in XAML.
    public string DataProfileName => TransactionProfileCatalog.LabelFor(DataProfile);

    public string MetadataProfileName => TransactionProfileCatalog.LabelFor(MetadataProfile);

    public string DataTransactionProfileTooltip
        => string.Format(UiStrings.TransactionProfileDataChipTooltipFormat, TransactionProfileCatalog.LabelFor(DataProfile));

    public string MetadataTransactionProfileTooltip
        => string.Format(UiStrings.TransactionProfileMetadataChipTooltipFormat, TransactionProfileCatalog.LabelFor(MetadataProfile));

    // ⚠ The chips report the ENFORCED profile, not the persisted one (audit A-09).
    //
    // TransactionService.ResolveActiveProfile() hard-returns ReadCommitted and says why: the per-connection TPB
    // profile is not user-configurable, and a stored legacy value — table stability, which locks whole tables —
    // must never silently make the SQL console WAIT or block other sessions. The enum and the persisted fields
    // are vestigial, pending their own removal pass.
    //
    // These properties used to read ConnectionProfile.DataTransactionProfile / MetadataTransactionProfile, i.e.
    // the persisted value. That was harmless only by luck: both default to ReadCommitted, so the display
    // happened to agree with reality. But Migrate_1_2 copies a v1 file's single TransactionProfile straight into
    // DataTransactionProfile — so a user upgrading from v1 with "Table Stability" saw a chip claiming Table
    // Stability while every transaction ran Read Committed. A status chip whose whole job is to tell the user
    // how their transactions behave must not be able to lie about it; reading the enforced value makes that
    // structural rather than coincidental, and it will keep being true when the vestigial fields are deleted.
    private static Core.Connections.TransactionProfile DataProfile
        => TransactionService.EnforcedProfile;

    private static Core.Connections.TransactionProfile MetadataProfile
        => TransactionService.EnforcedProfile;

    // Title-bar "DEV MODE" badge: shown only when the active connection has Developer
    // Mode on (DDL waits for in-use objects instead of failing fast).
    public bool IsDeveloperModeActive => _service.ActiveProfile?.DeveloperMode == true;

    public bool HasCurrentResult => CurrentResult is { HasResultSet: true };
    public bool ShowResultsEmptyHint => !HasCurrentResult;

    // ── Truncated-Preview / ceiling notice bar (A.6) ──────────────────────────
    // A loud, actionable notice pinned above the results grid. Hidden while a run is
    // in flight (the status text + timer + Cancel carry "loading" feedback instead).
    //   Preview hit its limit → "Showing the first N rows…" + a working [Load all rows].
    //   Full hit the hard ceiling → a plain safety-limit message, no action (nothing more
    //   to safely load; the smart soft threshold + refined messaging are Etap 2).
    public bool ShowResultsNotice =>
        !IsExecuting && CurrentResult is { Truncated: true } or { CeilingHit: true };

    public string ResultsNoticeText => CurrentResult switch
    {
        { Truncated: true } r => string.Format(CultureInfo.CurrentCulture, UiStrings.ResultsTruncatedFormat, r.Rows.Count),
        { CeilingHit: true } r => string.Format(CultureInfo.CurrentCulture, UiStrings.ResultsCeilingFormat, r.Rows.Count),
        _ => string.Empty,
    };

    // [Load all rows] appears only for a truncated Preview (a ceiling-hit Full has nothing
    // more to safely load). Hidden while executing so it can't be clicked mid-load.
    public bool ShowLoadAllButton => !IsExecuting && CurrentResult is { Truncated: true };

    public bool HasMessages => Messages.Count > 0;
    public bool ShowMessagesEmptyHint => !HasMessages;

    public bool CanExecute => !IsExecuting;

    // --- THE user transaction. One, on the data attachment. One Commit, one Rollback. ---
    // (There was a second, "metadata" transaction here, fed by the SQL Editor silently routing DDL
    //  onto the metadata attachment. Routing is gone, so this is now the whole model.)
    public bool IsTransactionIdle => _transactionService.IsIdle;
    public bool IsTransactionActive => _transactionService.IsActive;
    public bool IsTransactionError => _transactionService.IsError;
    public bool HasExecutedInTransaction => _transactionService.HasExecutedStatements;
    public string TransactionBarText => BuildTransactionBarText(_transactionService);

    // The Commit/Rollback pair is reachable wherever the user can start a transaction — and stays
    // visible while one is open, wherever they navigate.
    public bool ShowDataTransactionButtons
        => IsQueryTabActive || IsDataTabActive || IsTransactionActive || IsTransactionError;

    // ⭐ Od M3.1d ten pasek niesie WYŁĄCZNIE kontekst lokalny edytora SQL — liczbę instrukcji
    // wykonanych w transakcji. FAKT „transakcja jest otwarta" przejął chip w pasku statusu (§8.4.5),
    // który jest widoczny także wtedy, gdy użytkownik pracuje w debuggerze albo w edytorze obiektu,
    // a ten pasek jest bramkowany `IsQueryTabActive` i wtedy go nie ma.
    //
    // > Użytkownik (2026-08-02): „to nie jest zbędna redundancja, tylko dwa różne poziomy informacji…
    // > licznik instrukcji przestaje być istotny, gdy nie pracuję już w SQL Editorze".
    //
    // ⛔ Nie przywracać tu kropki stanu ani etykiety „Active Transaction" — to byłby drugi właściciel
    // tego samego faktu (§0.1.2).
    private static string BuildTransactionBarText(TransactionService tx) => tx.State switch
    {
        TransactionState.Error => UiStrings.TransactionBarError,
        _ when tx.HasExecutedStatements
            => string.Format(UiStrings.TransactionStatementCountFormat, tx.StatementCount),
        _ => string.Empty,
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
            // ⚠ The four *EasyMode flags are gone from WorkspaceState (etap 6 / §7.6) — they are
            // Preferences.*EasyModeDefault now. And note what is NOT conditional here: capture always runs,
            // whatever RestoreWorkspaceOnStartup says. Gating capture would mean that turning restore back on
            // resurrected a workspace from whenever it was last enabled (§7.5).
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

        // ⭐ §7.5 — "restore open tabs on startup", off. The dictionary above is loaded ANYWAY, and that is the
        // load-bearing part: it is what CaptureWorkspace writes back at close, so skipping the load would silently
        // erase every OTHER connection's stored tabs AND saved queries — rule-#11 data loss disguised as a
        // preference. What the setting suppresses is materialising those tabs into the UI, once per profile, the
        // first time this session opens it. Later in the same session a reconnect restores what this session
        // built, which is why the suppression is a set of ids that empties rather than a standing flag.
        if (!_preferences.Current.RestoreWorkspaceOnStartup)
        {
            foreach (var id in _workspacesByConnection.Keys)
            {
                _workspaceRestoreSuppressed.Add(id);
            }
        }

        IsQueryPanelVisible = state.QueryPanelVisible;
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
            // Live tools + transient sessions are never persisted. A Debugger tab is a
            // transient debug session (rolled back on close), not a document — it must not
            // be captured (else an empty tab is "restored" on the next launch of the app).
            // Live-tool tabs are session-transient and are never persisted. Data Import belongs here for the
            // same reason the Script Executor does — and omitting it would not merely fail to restore the tab,
            // it would fall through and be captured as a Ddl tab, so the next launch would "restore" an empty
            // one (the exact bug the Debugger tab had).
            if (tab.Kind is WorkspaceTabKind.SecurityManager or WorkspaceTabKind.TraceMonitor or WorkspaceTabKind.SessionManager or WorkspaceTabKind.GlobalSearch or WorkspaceTabKind.ScriptExecutor or WorkspaceTabKind.DataImport or WorkspaceTabKind.Debugger) continue;

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
        // ⭐ §7.5 — the ONE place "restore open tabs" is honoured, and it removes the id as it reads it: the
        // setting is about STARTUP, so a connection switch later in the same session must restore the tabs this
        // session built rather than starting clean again.
        var suppressed = _workspaceRestoreSuppressed.Remove(profileId);

        if (!_workspacesByConnection.TryGetValue(profileId, out var ws))
        {
            // First time we see this connection — start with a single empty Query tab.
            ws = new ConnectionWorkspace
            {
                Tabs = { new WorkspaceTab { Kind = CoreTabKind.Query, SqlText = string.Empty } },
            };
        }

        // ⚠ Only the TABS are skipped. LoadSavedQueriesFor below still runs on the very same stored workspace,
        // because a connection's saved queries are the user's own content — "start me clean" is about a stale tab
        // strip, not about discarding SQL they named and kept. The Query tab still appears (the defensive insert
        // below), and QueryText comes back from the active saved query.
        var storedTabs = suppressed ? Array.Empty<WorkspaceTab>() : (IReadOnlyList<WorkspaceTab>)ws.Tabs;

        var queryTabAdded = false;
        QueryText = string.Empty;
        foreach (var tab in storedTabs)
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

        var activeIndex = suppressed ? 0 : ws.ActiveTabIndex;
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
        foreach (var t in WorkspaceTabs)
        {
            if (t.Kind == WorkspaceTabKind.TraceMonitor && t.TraceMonitor is { } monitor)
                _ = monitor.DisposeAsync(); // stop live trace sessions on disconnect (best-effort)
            else if (t.Kind == WorkspaceTabKind.SessionManager && t.SessionManager is { } sm)
                _ = sm.DisposeAsync(); // stop the MON$ poll timer on disconnect (best-effort)
            else if (t.Kind == WorkspaceTabKind.ScriptExecutor && t.ScriptExecutor is { } se)
                se.Detach(); // unsubscribe from the transaction-state event
            else if (t.Kind == WorkspaceTabKind.DataImport)
                ReleaseImportSession(); // the import's attachment must not outlive the profile (I7.5)
            else if (t.Kind == WorkspaceTabKind.Debugger && t.Debugger is { } dbg)
                _ = dbg.DisposeAsync(); // roll back + close the debug session's attachment (§4.4) — a debug tab is bound to this DB (best-effort)
        }
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
        CopyAllMessagesCommand.NotifyCanExecuteChanged();
        ClearMessagesCommand.NotifyCanExecuteChanged();

        QueryStatsText = string.Empty;

        // Drop the SQL Editor's last profiled run so its Performance panel doesn't show a stale
        // report from a previous connection. (Procedure/Function contexts are discarded when
        // their tabs close on disconnect.)
        _dataAttachmentId = null;
        SqlEditorPerformance.Clear();
    }

    /// <summary>
    /// Czy trwa nawiązywanie połączenia — faza 1 ładowania (§19.34).
    /// <para>
    /// ⚠⚠ <b>Gaszone NIE w <c>finally</c> tej metody, i to jest wybór, nie przeoczenie.</b> Ładowanie
    /// połączenia ma trzy fazy w dwóch klasach: (1) otwarcie dołączeń tutaj, (2) odtworzenie zakładek
    /// w <see cref="ApplyActiveConnectionChange"/> — <b>synchronicznie na wątku UI</b>, (3) prefetch
    /// kategorii w węźle połączenia. Gaszenie na wyjściu z fazy 1 zostawiłoby lukę na fazę 2, a w niej
    /// pasek zgasłby i zapalił się ponownie — użytkownik ratyfikował utrzymanie etykiety fazy 1
    /// (2026-08-04), więc flaga żyje aż faza 3 ją przejmie.
    /// </para>
    /// <para>
    /// ⭐ Nie ma tu ryzyka wiecznie zapalonego paska, bo <b>każda</b> droga wyjścia gasi flagę:
    /// nieudane połączenie — w <c>catch</c> poniżej (faza 2 ani 3 wtedy nie nastąpią);
    /// rozłączenie i brak profilu — w <see cref="ApplyActiveConnectionChange"/>;
    /// udane połączenie — w <c>finally</c> prefetchu, razem z fazą 3.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool _isConnecting;

    // ⭐ Jeden punkt przeliczenia sekcji, dokładnie jak `OnIsExecutingChanged` dla zapytania. Rozsypane
    // wywołania `UpdateProgressSection()` przy każdym przypisaniu flagi byłyby czwartą rzeczą do
    // pamiętania — i pierwszy test, który ustawił flagę wprost, natychmiast to pokazał.
    partial void OnIsConnectingChanged(bool value) => UpdateProgressSection();

    public async Task ConnectAsync(ConnectionProfile profile)
    {
        try
        {
            ClearError();
            IsConnecting = true;
            await _service.ConnectAsync(profile).ConfigureAwait(true);
        }
        catch (ConnectionFailedException ex)
        {
            // Faza 2 ani 3 już nie nastąpią, więc to jedyne miejsce, które może tu zgasić pasek.
            IsConnecting = false;
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
    {
        // A Save-and-close / Save-and-disconnect batch is itself shown in a MODAL "Saving changes"
        // dialog, and every dialog in this app is ShowDialog(MainWindow). An editor that asks for
        // confirmation mid-batch would therefore open a second modal owned by an already-disabled
        // window: unreachable, so its SaveAsync never returns and the batch hangs at 0/N with an
        // empty grid (QA, 2026-07-25). Proceed instead of asking — same reasoning as the
        // "recompile dependents?" suppression below, and the only confirm reachable from any
        // SaveAsync is the debugger's "saving ends the debug session", which during a shutdown or
        // disconnect is moot: the session is being torn down either way (§4.4).
        if (_bulkSaveInProgress) return Task.FromResult(true);
        return ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);
    }

    /// <summary>
    /// Tells the user something they must not miss and waits for them to acknowledge it. The shared confirm
    /// dialog with no Cancel — a report of what already happened, where there is nothing left to decline.
    /// </summary>
    private Task RequestAcknowledgeAsync(string title, string message)
        => RequestConfirmAsync(new ConfirmRequest
        {
            Title = title,
            Message = message,
            ConfirmLabel = UiStrings.DialogOk,
            CancelLabel = string.Empty,
        });

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
    // One aggregation feeding FOUR entry points: tab close (RequestCloseTabAsync),
    // disconnect (ConfirmDisconnectAsync), app close (TryCloseApplicationAsync) and — since
    // M3.3c — the tab context menu's bulk closes (CloseTabsAsync).
    // Unsaved CODE work (uncompiled new objects / modified source / queued structural
    // changes) lives only in the open tabs; transactions live on the server and can't
    // survive a restart, so they always need a conscious Commit/Roll-back decision.
    //
    // ⭐⭐ M3.3c NADAŁO TYM TRZEM METODOM ZASIĘG, i to jest jedyna zmiana, jakiej bramka
    // potrzebowała. „Zamknij zakładki po prawej" dotyczy PODZBIORU, a metody iterowały po
    // wszystkich — bez zasięgu czwarte wejście musiałoby albo ominąć bramkę, albo pytać
    // o pracę w zakładkach, których nie zamyka. ⚠ `scope == null` ZNACZY „wszystkie", więc
    // trzy istniejące wejścia są nietknięte i nie było potrzeby dotykać ich kodu.
    // ⛔ Nie budować drugiej ścieżki „zapisz wiele zakładek" — ta jest przetestowana.

    // Unsaved-work descriptors across the currently-open tabs (the active connection's).
    // Other connections' tabs are stashed serialized and hold no live uncompiled source.
    internal IReadOnlyList<UnsavedWorkItem> CollectUnsavedWork(
        IReadOnlyCollection<WorkspaceTabViewModel>? scope = null)
    {
        var items = new List<UnsavedWorkItem>();
        foreach (var tab in scope ?? (IReadOnlyCollection<WorkspaceTabViewModel>)WorkspaceTabs)
        {
            if (tab.UnsavedWork is { } item) items.Add(item);
        }
        return items;
    }

    // True when at least one tab IN SCOPE holds unsaved work AND can compile it (every object
    // editor can). Drives whether the WorkGuard offers "Save …" alongside Discard/Cancel.
    private bool HasSavableDirtyEditors(IReadOnlyCollection<WorkspaceTabViewModel>? scope = null)
    {
        foreach (var tab in scope ?? (IReadOnlyCollection<WorkspaceTabViewModel>)WorkspaceTabs)
            if (tab.UnsavedWork is not null && tab.SavableEditor is not null) return true;
        return false;
    }

    // Compiles every dirty object editor through the shared group-recompilation results
    // pipeline (one row per object, continue-on-error, live progress) — reusing the exact
    // pipeline behind "Recompile group/dependents", only launched differently and driving
    // each editor's own SaveAsync instead of re-running stored source. Returns true only if
    // EVERY dirty editor saved — the WorkGuard then proceeds with the close/disconnect. On
    // any failure (or a user cancel) it returns false: the caller stays open and the first
    // failed tab is selected so its error is in view. Saves auto-commit per object (DDL
    // lane), so a mid-batch failure does NOT undo the ones already saved — only the failing
    // objects remain to fix and retry. Save order = tab order (a deliberate v1 simplification;
    // dependency-derived order is a possible future refinement, not required here).
    private async Task<bool> SaveDirtyEditorsAsync(
        IReadOnlyCollection<WorkspaceTabViewModel>? scope = null)
    {
        var savable = new List<(WorkspaceTabViewModel Tab, ISavableObjectEditor Editor)>();
        foreach (var tab in scope ?? (IReadOnlyCollection<WorkspaceTabViewModel>)WorkspaceTabs)
            if (tab.UnsavedWork is not null && tab.SavableEditor is { } editor)
                savable.Add((tab, editor));
        if (savable.Count == 0) return true;

        // Tally the outcome from the execution loop directly (NOT from the dialog VM's
        // counters, which are updated via IProgress and may lag): this is what decides
        // whether the close/disconnect may proceed.
        var succeeded = 0;
        WorkspaceTabViewModel? firstFailedTab = null;

        _bulkSaveInProgress = true;
        try
        {
            await RunBatchWithReportAsync(
                UiStrings.SaveDirtyEditorsBatchTitle,
                (_, _) => Task.FromResult(new BatchPlan(
                    savable.Select(s => (s.Tab.BaseTitle, UiStrings.BatchOpSave, string.Empty))
                           .ToList<(string, string, string)>(),
                    Array.Empty<BatchOperationResult>())),
                refreshAfter: false,
                executeAsync: async (steps, progress, ct) =>
                {
                    for (int i = 0; i < savable.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        EditorSaveResult result;
                        try
                        {
                            result = await savable[i].Editor.SaveAsync(ct).ConfigureAwait(true);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex) when (ex is DdlExecutionException or InvalidOperationException)
                        {
                            result = new EditorSaveResult(false, ex.Message);
                        }
                        if (result.Success) succeeded++;
                        else if (firstFailedTab is null) firstFailedTab = savable[i].Tab;
                        progress.Report((i, result.Success ? null : (result.Error ?? UiStrings.SaveDirtyEditorsUnknownError)));
                    }
                }).ConfigureAwait(true);
        }
        finally
        {
            _bulkSaveInProgress = false;
        }

        var allSaved = succeeded == savable.Count;
        if (!allSaved && firstFailedTab is not null) SelectTab(firstFailedTab);
        return allSaved;
    }

    // Asked of the registry, never of a named module — that is the whole point of it existing.
    private bool AnyTransactionActive => _pendingWork.HasWork;

    private void AppendActiveTransactionLines(System.Text.StringBuilder sb)
    {
        foreach (var line in _pendingWork.Describe()) sb.AppendLine("  • " + line);
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

        var options = new List<ChoiceOption>
        {
            new ChoiceOption { Id = "cancel", Label = UiStrings.ExitUnsavedCancel, IsDefault = true, IsCancel = true },
        };
        if (HasSavableDirtyEditors())
            options.Add(new ChoiceOption { Id = "save", Label = UiStrings.ExitUnsavedSave });
        options.Add(new ChoiceOption { Id = "discard", Label = UiStrings.ExitUnsavedDiscard, IsDestructive = true });

        var id = await RequestChoiceAsync(new ChoiceRequest
        {
            Title = UiStrings.ExitUnsavedTitle,
            Message = sb.ToString().TrimEnd(),
            Options = options.ToArray(),
        }).ConfigureAwait(true);

        if (id == "save")
        {
            // Compile every dirty editor first; a single failure keeps the app open
            // (SaveDirtyEditorsAsync selects the offending tab + shows its error).
            if (!await SaveDirtyEditorsAsync().ConfigureAwait(true)) return false;
            if (txActive) await RollbackAllAsync().ConfigureAwait(true);
            return true;
        }
        if (id != "discard") return false;
        if (txActive) await RollbackAllAsync().ConfigureAwait(true);
        return true;
    }

    // Disconnect guard. Two phases, each independent: first offer to Save/Discard the
    // unsaved metadata editors, then settle any active data transaction. Returns true if
    // the disconnect may proceed, false if the user cancelled (or a Save failed).
    private async Task<bool> ConfirmDisconnectAsync()
    {
        // Phase 1 — unsaved metadata editors: Save all / Discard / Cancel. "Save" compiles
        // every dirty editor through the shared batch dialog; a failure keeps us connected
        // (SaveDirtyEditorsAsync selects the offending tab + shows its error). "Discard"
        // just proceeds — the open tabs are torn down on the actual disconnect regardless.
        if (HasSavableDirtyEditors())
        {
            var unsaved = CollectUnsavedWork();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(CultureInfo.CurrentCulture,
                UiStrings.DisconnectSaveHeaderFormat, _service.ActiveProfile?.Name ?? string.Empty));
            foreach (var it in unsaved) sb.AppendLine("  • " + it.Label);
            sb.AppendLine();
            sb.Append(UiStrings.DisconnectSaveQuestion);

            var id = await RequestChoiceAsync(new ChoiceRequest
            {
                Title = UiStrings.DisconnectSaveTitle,
                Message = sb.ToString(),
                Options = new[]
                {
                    new ChoiceOption { Id = "save", Label = UiStrings.DisconnectSaveConfirm, IsDefault = true },
                    new ChoiceOption { Id = "discard", Label = UiStrings.DisconnectSaveDiscard, IsDestructive = true },
                    new ChoiceOption { Id = "cancel", Label = UiStrings.DisconnectChoiceCancel, IsCancel = true },
                },
            }).ConfigureAwait(true);

            if (id is null or "cancel") return false;
            if (id == "save" && !await SaveDirtyEditorsAsync().ConfigureAwait(true)) return false;
        }

        // Phase 2 — active data transaction: Commit / Roll back (default) / Cancel.
        if (AnyTransactionActive)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(CultureInfo.CurrentCulture,
                UiStrings.DisconnectChoiceHeaderFormat, _service.ActiveProfile?.Name ?? string.Empty));
            AppendActiveTransactionLines(sb);
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

        return true;
    }

    public event Func<string, Task>? ClipboardWriteRequested;

    // Asks the view to open a Save-file picker for the DDL export. The view returns the
    // chosen absolute path, or null when the user cancels. The VM (not the view) builds the
    // script and writes the file, so the Avalonia StorageProvider stays in the view and the
    // portable-DDL policy + file write stay here.
    public event Func<SaveFileRequest, Task<string?>>? SaveFileRequested;

    // Asks the view to open the shared Export dialog for a grid's data source. The view builds the
    // dialog (its own StorageProvider / Clipboard) and returns the completed ExportOutcome, or null
    // on cancel. The VM reports the outcome to the Messages log.
    public event Func<ExportDialogRequest, Task<ExportOutcome?>>? ExportRequested;

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

    // ---- SQL templates (Drag & Drop) ---------------------------------------
    // Instant, metadata-free menu for a dropped object's kind + the target editor's
    // insertion context (PSQL-only templates are hidden in a plain SQL editor).
    internal IReadOnlyList<SqlTemplateDescriptor> SnippetTemplatesFor(
        MetadataObjectKind kind, SnippetInsertionContext insertion)
        => _templateRegistry.DescriptorsForKind(kind, insertion);

    // Drag-start gate: is this object kind draggable onto an editor at all (any context)?
    internal bool HasSnippetTemplates(MetadataObjectKind kind)
        => _templateRegistry.HasTemplatesForKind(kind);

    // Loads the dropped object's metadata (once, via the TableDetail reader) into a
    // SnippetContext. The drop target caches this per drop so hover-previews AND the final
    // insert share a single metadata read. Returns null when the builder isn't wired.
    internal async Task<SnippetContext?> BuildSnippetContextAsync(
        MetadataObject obj,
        SnippetInsertionContext insertion = SnippetInsertionContext.PlainSql,
        CancellationToken ct = default)
    {
        if (_snippetContextBuilder is null) return null;
        return await _snippetContextBuilder.BuildAsync(obj, insertion, ct).ConfigureAwait(true);
    }

    // Pure, synchronous generation from an already-loaded context (used for both the
    // preview tooltip and the insertion).
    internal SqlSnippet GenerateSnippet(SnippetContext context, string templateId)
        => _templateRegistry.Generate(templateId, context);

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

    // ─── New-object change safety ────────────────────────────────────────────────────────────────
    //
    // The five editors below author their object as CREATE OR ALTER (Procedure/Function/Trigger from the
    // DDL generator, View when the user writes one, Package from its header template), which means a New
    // tab whose name happens to match an existing object OVERWRITES that object instead of failing — no
    // concurrency required, one user and a name collision are enough. ObjectChangeGate refuses that write;
    // this is where it gets the ability to find out, asking the catalog through the SAME authority the
    // object tree uses (FirebirdMetadataReader.ExistsAsync, deliberately built over ListAsync so the gate
    // and the tree cannot disagree about what exists).
    //
    // One helper, so all five New flows ask the question identically. Left unwired the gate reports
    // "unverifiable" and refuses — the safe direction, but it means every New flow must pass this.
    private Func<string, CancellationToken, Task<bool>> ObjectExistsProbeFor(MetadataObjectKind kind)
        => (name, ct) => _metadataReader.ExistsAsync(kind, name, ct);

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
        detail.ObjectExistsProbe = ObjectExistsProbeFor(MetadataObjectKind.View);
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
        detail.ObjectExistsProbe = ObjectExistsProbeFor(MetadataObjectKind.Package);
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
        detail.ObjectExistsProbe = ObjectExistsProbeFor(MetadataObjectKind.Procedure);
        detail.ObjectCreated += outcome => _ = OnProcedureCreated(detail, outcome);
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

    private Task OnProcedureCreated(ProcedureDetailTabViewModel detail, SourceObjectDetailTabViewModel.ObjectCompileOutcome outcome)
        => OnObjectMaterializedAsync(detail, MetadataObjectKind.Procedure, UiStrings.NewProcedureExecutedFormat, outcome);

    /// <summary>
    /// An object now exists under a name the tab that compiled it is NOT bound to — a New-tab create, or a
    /// rename. ⭐ ONE handler for both and for all three object kinds, because the work is identical (refresh
    /// the tree, close the originating tab, open what now exists) and only the WORDING differs. Three copies
    /// of it existed before this; adding the rename disclosure to each would have made three copies of the one
    /// message the user must not miss.
    /// </summary>
    private async Task OnObjectMaterializedAsync(
        object detail,
        MetadataObjectKind kind,
        string createdFormat,
        SourceObjectDetailTabViewModel.ObjectCompileOutcome outcome)
    {
        if (outcome.IsRename)
        {
            var notice = string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ObjectRenameNotSupportedFormat,
                outcome.Name ?? string.Empty,
                outcome.PreviousName ?? string.Empty);

            // Both surfaces on purpose. The MODAL is what guarantees the user learns that a second object is
            // now in their database — they did not ask for one, and the tab is about to switch out from under
            // them, so a message they might scroll past later is not enough. The Messages entry is the
            // durable record of the same fact, copyable, after the dialog is gone.
            AddMessage(MessageSeverity.Warning, notice);
            await RequestAcknowledgeAsync(UiStrings.ObjectRenameNotSupportedTitle, notice).ConfigureAwait(true);
        }
        else
        {
            AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, createdFormat, outcome.Name ?? string.Empty));
        }

        await Metadata.RefreshAsync().ConfigureAwait(true);

        // Close the tab that compiled it. ⚠ CloseTab, not RequestCloseTabAsync: on a rename the buffer is
        // still "dirty" against the ORIGINAL object (nothing reloaded it), so the confirming close would ask
        // the user to save work that has just been saved — under the old name, which is the one thing that
        // must not happen.
        if (FindTabForDetail(detail) is { } originating) CloseTab(originating);

        // Open what now exists, so the user is never left looking at a tab bound to the old name — the
        // "Compile did nothing" symptom. Falls back to the tree when the name could not be parsed.
        if (!string.IsNullOrEmpty(outcome.Name))
        {
            OnOpenDdlRequested(new MetadataObject(outcome.Name, kind));
        }
    }

    /// <summary>The workspace tab hosting this detail view model, whichever of the three kinds it is.</summary>
    private WorkspaceTabViewModel? FindTabForDetail(object detail)
    {
        foreach (var t in WorkspaceTabs)
        {
            if (ReferenceEquals(t.ProcedureDetail, detail)
                || ReferenceEquals(t.TriggerDetail, detail)
                || ReferenceEquals(t.FunctionDetail, detail))
            {
                return t;
            }
        }
        return null;
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
        detail.ObjectExistsProbe = ObjectExistsProbeFor(MetadataObjectKind.Trigger);
        detail.ObjectCreated += outcome => _ = OnTriggerCreated(detail, outcome);
        // Seeding marked the VM dirty; a brand-new untouched tab must not prompt on close.
        detail.ClearDirty();

        var obj = new MetadataObject(UiStrings.NewTriggerTabDefaultTitle, MetadataObjectKind.Trigger);
        var tab = WorkspaceTabViewModel.CreateTriggerDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private Task OnTriggerCreated(TriggerDetailTabViewModel detail, SourceObjectDetailTabViewModel.ObjectCompileOutcome outcome)
        => OnObjectMaterializedAsync(detail, MetadataObjectKind.Trigger, UiStrings.NewTriggerExecutedFormat, outcome);

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
        detail.ObjectExistsProbe = ObjectExistsProbeFor(MetadataObjectKind.Function);
        detail.ObjectCreated += outcome => _ = OnFunctionCreated(detail, outcome);
        detail.EasyMode = true;
        // Seeding marked the VM dirty; a brand-new untouched tab must not prompt on close.
        detail.ClearDirty();

        var obj = new MetadataObject(UiStrings.NewFunctionTabDefaultTitle, MetadataObjectKind.Function);
        var tab = WorkspaceTabViewModel.CreateFunctionDetail(this, obj, detail, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
    }

    private Task OnFunctionCreated(FunctionDetailTabViewModel detail, SourceObjectDetailTabViewModel.ObjectCompileOutcome outcome)
        => OnObjectMaterializedAsync(detail, MetadataObjectKind.Function, UiStrings.NewFunctionExecutedFormat, outcome);

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

    // Performance Analysis build callback (Option B), now PER-HOST: the captured run (sql +
    // result + reads) is passed in by the calling HostPerformanceContext instead of read from a
    // shared global, so the SQL Editor / each Procedure / each Function analyzes only its OWN
    // last execution. Reads the plan (prepare-only, no re-execution) + advisor catalog. Returns
    // null when there is no run. Firebird exceptions become a plain message (VM stays Firebird-free).
    internal async Task<PerformanceReport?> BuildPerformanceReportAsync(
        string? sql, QueryResult? result, IReadOnlyList<PerTableReadRow>? capturedReads, CancellationToken cancellationToken)
    {
        if (result is null || string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        RawPlanCapture? plan = null;
        TimeSpan? prepare = null;
        if (_service.IsConnected)
        {
            try
            {
                var planResult = await _planReader.GetPlanAsync(sql!, cancellationToken).ConfigureAwait(true);
                plan = planResult.Plan;
                prepare = planResult.PrepareTime > TimeSpan.Zero ? planResult.PrepareTime : null;
            }
            catch (PerformanceCaptureException)
            {
                // Plan unavailable — the report still shows timing + rows.
            }
        }

        var reads = capturedReads ?? Array.Empty<PerTableReadRow>();
        var capture = new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = sql! },
            Plan = plan,
            Timings = new ExecutionTimings { Prepare = prepare, Execute = result.Elapsed },
            RowsReturned = result.Rows.Count,
            Truncated = result.Truncated,
            RecordsAffected = result.RecordsAffected,
            TableReads = reads,
            Method = reads.Count > 0 ? CaptureMethod.MonAttachmentDelta : CaptureMethod.PlanOnly,
        };

        // Advisor catalog (Phase 3a): indexes/selectivity/cardinality for the tables the query
        // actually read. Best-effort on the metadata lane — any failure degrades to the
        // measured-reads findings that don't need the catalog. Scoped to the measured tables.
        CatalogModel? catalog = null;
        var tables = reads.Select(r => r.Table).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (_service.IsConnected && tables.Count > 0)
        {
            try
            {
                catalog = await _catalogReader.CaptureAsync(tables, cancellationToken).ConfigureAwait(true);
            }
            catch (PerformanceCaptureException)
            {
                // Catalog unavailable — advisor still runs on measured reads alone.
            }
        }

        return _performanceAnalyzer.Analyze(capture, catalog);
    }

    // Performance tab visibility drives lazy analysis (Option B): build on first view
    // while stale. When the tab is active it also ARMS per-table read capture on execute.
    partial void OnSelectedBottomTabIndexChanged(int value)
    {
        _performanceTabActive = value == PerformanceBottomTabIndex;
        Performance.SetVisible(_performanceTabActive);
    }

    // Per-table read snapshot on the metadata lane (best-effort — any failure disables
    // reads for this run and the panel degrades to plan + timings).
    private async Task<IReadOnlyList<PerTableReadRow>?> TrySnapshotReadsAsync()
    {
        try
        {
            _dataAttachmentId ??= await _perfStatsReader.GetDataAttachmentIdAsync().ConfigureAwait(true);
            return await _perfStatsReader.SnapshotAsync(_dataAttachmentId.Value).ConfigureAwait(true);
        }
        catch (PerformanceCaptureException)
        {
            return null;
        }
    }

    // Sum one change counter across the captured per-table delta (0 when reads weren't captured).
    private static long SumChanges(IReadOnlyList<PerTableReadRow>? rows, Func<PerTableReadRow, long> selector)
        => rows is null ? 0 : rows.Sum(selector);

    // Total rows read (sequential + index) across the captured per-table delta.
    private static long SumReads(IReadOnlyList<PerTableReadRow>? rows)
        => rows is null ? 0 : rows.Sum(r => r.SeqReads + r.IdxReads);

    // The ONE data-lane execution path with Execution Metrics: bracket the run with
    // before/after MON$ snapshots and return the per-table read/change delta alongside the
    // result. Used by the SQL Editor (RunExecuteAsync) AND Procedure/Function Detail
    // (RunProcedure/FunctionExecuteAsync) so every place a statement runs surfaces the same
    // diagnostics. The execution error propagates (QueryExecutionException); the snapshots
    // are best-effort — any MON$ failure degrades to a null delta (RecordsAffected fallback).
    private Task<(QueryResult Result, IReadOnlyList<PerTableReadRow>? Reads)> ExecuteWithMetricsAsync(
        string sql, IReadOnlyList<QueryParameter>? parameters, CancellationToken cancellationToken)
        => ExecuteWithMetricsAsync(Request(sql, ExecutionIntent.Preview, parameters), null, null, cancellationToken);

    /// <summary>
    /// ⭐ The ONE place an <see cref="ExecutionRequest"/> is given the user's row limits (§7.2).
    ///
    /// <para><c>ExecutionDefaults</c> was written for this moment — <i>"they live here (never as scattered
    /// literals) so that moving them into user settings later […] is a one-line change at the call site that
    /// fills an ExecutionRequest"</i> — and it was right: the limits already travel as VALUES on the request,
    /// so nothing below this method reads a global and <see cref="EmberTern.Core.Sql.SqlFormatter"/>-style
    /// ambient reads never arise. Building the request in one place rather than editing four call sites is what
    /// makes a fifth execution surface inherit the limits instead of quietly shipping on the defaults.</para>
    ///
    /// <para>⚠ <b><see cref="ExecutionRequest.FullSafetyCeiling"/> is deliberately left at its default</b>
    /// (ratified Q9). It is a memory backstop, not a preference: a user who raised it to 50 M would get an
    /// out-of-memory crash instead of a truncated grid.</para>
    ///
    /// <para>⚠ Read from the live preferences per call, never captured — apply-on-change means the value moves
    /// while the window is open (the §14.2e rule the formatter's style provider follows).</para>
    /// </summary>
    internal ExecutionRequest Request(
        string sql, ExecutionIntent intent, IReadOnlyList<QueryParameter>? parameters)
    {
        var preferences = _preferences.Current;
        return new ExecutionRequest
        {
            Sql = sql,
            Intent = intent,
            Parameters = parameters,
            PreviewLimit = preferences.PreviewRowLimit,
            SoftThreshold = preferences.FullLoadPromptThreshold,
        };
    }

    // Streaming-aware overload: runs the request (Preview or Full) through the executor with the
    // before/after MON$ read delta, reporting streamed progress for a Full load's live counter and
    // routing the Full soft-threshold "keep loading?" decision (onSoftThreshold).
    private async Task<(QueryResult Result, IReadOnlyList<PerTableReadRow>? Reads)> ExecuteWithMetricsAsync(
        ExecutionRequest request, IProgress<long>? progress, Func<long, Task<bool>>? onSoftThreshold, CancellationToken cancellationToken)
    {
        var before = await TrySnapshotReadsAsync().ConfigureAwait(true);
        var result = await _executor.ExecuteAsync(request, progress, onSoftThreshold, cancellationToken).ConfigureAwait(true);
        IReadOnlyList<PerTableReadRow>? reads = null;
        if (before is not null)
        {
            var after = await TrySnapshotReadsAsync().ConfigureAwait(true);
            if (after is not null)
            {
                reads = TableStatsDiffer.Diff(before, after);
            }
        }
        return (result, reads);
    }

    // Assemble the work summary (rows changed + rows read) from an execution's result +
    // measured read delta. Shared by every execution entry point.
    private static ExecutionSummary BuildExecutionSummary(QueryResult result, IReadOnlyList<PerTableReadRow>? reads)
        => new()
        {
            Inserts = SumChanges(reads, static r => r.Inserts),
            Updates = SumChanges(reads, static r => r.Updates),
            Deletes = SumChanges(reads, static r => r.Deletes),
            RowsRead = SumReads(reads),
            ReadsMeasured = reads is not null,
            RecordsAffected = result.RecordsAffected,
            Elapsed = result.Elapsed,
            ChangesMeasured = reads is not null,
        };

    // Column cache for ALIAS./TABLE. autocomplete. Keyed by uppercase table
    // name; cleared on disconnect (in ClearWorkspaceTabs path via
    // ApplyActiveConnectionChange). A separate "table doesn't exist or has no
    // columns" sentinel is unnecessary — the empty list is the right answer in
    // both cases, and a re-request only costs one tiny round-trip.
    private readonly Dictionary<string, IReadOnlyList<ColumnSpec>> _columnCache =
        new(StringComparer.OrdinalIgnoreCase);
    // Warmed rich Quick Info detail per object — description / function return type / trigger header
    // (Package 5, Stage B/C). Populated lazily by WarmReferencedAsync for the objects the current
    // statement references, fed into the snapshot so Quick Info reads it without a display-time query.
    // Cleared on disconnect alongside the column cache.
    private readonly Dictionary<string, Completion.ObjectDetail> _objectDetailCache =
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
    /// The dot-completion qualifier resolution moved to the editor's
    /// <c>SqlCompletionController</c> in Etap 0 (design §7/§15): it resolves
    /// against the per-editor cached alias map, off the keystroke, rather than
    /// re-scanning the whole document here on every dot. This VM only exposes the
    /// inputs — <see cref="EnumerateTableLikeNames"/> (known names) and the
    /// column cache below.
    /// </summary>
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
    /// Warms (loads + caches) everything Quick Info / completion needs for the objects
    /// <paramref name="names"/> the current statement references — <b>columns</b> for table-like
    /// objects (Sprint 1 / point b) <b>and</b> the rich detail (description, a function's return type,
    /// a trigger's header — Package 5, Stage B/C). Returns <c>true</c> when at least one thing was newly
    /// loaded, the signal the editor's language service uses to rebuild the model against the
    /// now-complete snapshot. Already-cached items are skipped, so this converges (the next call warms
    /// nothing → <c>false</c>). Best-effort — a failure leaves an item uncached for a later retry.
    /// Columns stay lazy at the catalog level; only the referenced objects are warmed.
    /// </summary>
    internal async Task<bool> WarmReferencedAsync(IReadOnlyList<string> names, CancellationToken ct = default)
    {
        if (names is null || names.Count == 0 || !_service.IsConnected) return false;
        bool loadedAny = false;
        foreach (var name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            var obj = TryResolveLoadedObject(name);

            // Columns for table-like objects. A bare FROM table not yet in the loaded set is still
            // warmed best-effort (obj is null) so Sprint 1's behaviour is preserved.
            if ((obj is null || IsTableLikeKind(obj.Kind)) && !_columnCache.ContainsKey(name))
            {
                await EnsureColumnsAsync(name, ct).ConfigureAwait(true);
                if (_columnCache.ContainsKey(name)) loadedAny = true;
            }

            // Routine parameters for referenced procedures/functions — so Quick Info shows the full
            // signature and Signature Help / Parameter Helper work WITHOUT the user typing "(". Same
            // cache the signature-help path fills; the pipeline just fills it proactively now.
            if (obj is { Kind: MetadataObjectKind.Procedure or MetadataObjectKind.Function }
                && !_routineParameterCache.ContainsKey(name))
            {
                await EnsureRoutineParametersAsync(name, ct).ConfigureAwait(true);
                if (_routineParameterCache.ContainsKey(name)) loadedAny = true;
            }

            // Rich detail needs the known kind (which reader to call). Cache the attempt (even a null
            // result) so a description-less object isn't re-queried forever.
            if (obj is not null && !_objectDetailCache.ContainsKey(name))
            {
                _objectDetailCache[name] = await LoadObjectDetailAsync(obj, ct).ConfigureAwait(true);
                loadedAny = true;
            }
        }
        return loadedAny;
    }

    private static bool IsTableLikeKind(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Table or MetadataObjectKind.View or MetadataObjectKind.SystemTable;

    // Loads the rich Quick Info detail for one object by dispatching to the existing per-kind detail
    // readers (reuse before create — no new SQL). Best-effort: a read failure yields whatever was
    // gathered so far, cached so it isn't retried on every model rebuild.
    private async Task<Completion.ObjectDetail> LoadObjectDetailAsync(MetadataObject obj, CancellationToken ct)
    {
        string? description = null;
        string? returnType = null;
        Core.Sql.Language.Semantics.TriggerDetail? trigger = null;
        Core.Sql.Language.Semantics.GeneratorDetail? generator = null;
        try
        {
            switch (obj.Kind)
            {
                case MetadataObjectKind.Table:
                case MetadataObjectKind.View:
                case MetadataObjectKind.SystemTable:
                    description = await _tableDetailReader.GetDescriptionAsync(obj.Name, ct).ConfigureAwait(true);
                    break;
                case MetadataObjectKind.Procedure:
                    description = await _tableDetailReader.GetProcedureDescriptionAsync(obj.Name, ct).ConfigureAwait(true);
                    break;
                case MetadataObjectKind.Function:
                    description = await _tableDetailReader.GetFunctionDescriptionAsync(obj.Name, ct).ConfigureAwait(true);
                    var sig = await _tableDetailReader.GetFunctionSignatureAsync(obj.Name, ct).ConfigureAwait(true);
                    returnType = sig.ReturnType;
                    break;
                case MetadataObjectKind.Trigger:
                    description = await _tableDetailReader.GetTriggerDescriptionAsync(obj.Name, ct).ConfigureAwait(true);
                    var h = await _tableDetailReader.GetTriggerHeaderAsync(obj.Name, ct).ConfigureAwait(true);
                    if (!h.IsDatabaseTrigger)
                    {
                        trigger = new Core.Sql.Language.Semantics.TriggerDetail(
                            NullIfEmpty(h.Table), h.IsBefore, h.FiresInsert, h.FiresUpdate, h.FiresDelete, h.Position, h.Active);
                    }
                    break;
                case MetadataObjectKind.Package:
                    description = await _tableDetailReader.GetPackageDescriptionAsync(obj.Name, ct).ConfigureAwait(true);
                    break;
                case MetadataObjectKind.Generator:
                    var gi = await _tableDetailReader.GetGeneratorInfoAsync(obj.Name, ct).ConfigureAwait(true);
                    description = gi.Description;
                    // Static definition facts only — never gi.CurrentValue (dynamic).
                    generator = new Core.Sql.Language.Semantics.GeneratorDetail(gi.InitialValue, gi.Increment);
                    break;
                case MetadataObjectKind.Domain:
                    description = (await _tableDetailReader.GetDomainInfoAsync(obj.Name, ct).ConfigureAwait(true)).Description;
                    break;
                case MetadataObjectKind.Exception:
                    description = (await _tableDetailReader.GetExceptionInfoAsync(obj.Name, ct).ConfigureAwait(true)).Description;
                    break;
            }
        }
        catch (MetadataReadException)
        {
            // Best-effort — keep whatever we got.
        }
        return new Completion.ObjectDetail(NullIfEmpty(description), NullIfEmpty(returnType), trigger, generator);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Routine parameters (procedures + functions), keyed by name; cleared on disconnect alongside
    // the column cache. Feeds the editor's Signature Help (Etap 5 / M6) via the metadata snapshot.
    private readonly Dictionary<string, IReadOnlyList<RoutineParameterMetadata>> _routineParameterCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Drops every per-object cache that was read from the current schema — columns, routine parameters and
    /// the warmed Quick Info detail. The ONE place those three are dropped together, so a caller cannot
    /// forget one of them (and a fourth such cache, added later, has one obvious home).
    /// <para>
    /// Two callers, one meaning: switching CONNECTION (the caches describe a different database) and a manual
    /// METADATA REFRESH (the schema may have moved under them — S-2, 2026-08-05). The refresh case is the one
    /// that was missing: a refresh rebuilt every open editor's semantic model against the same stale columns,
    /// so a column added to a table stayed "unknown" for the rest of the session on every open tab.
    /// </para>
    /// <para>
    /// ⚠ Safe to drop mid-session only because <c>ISqlMetadataProvider.KnowsColumns</c> now lets diagnostics
    /// tell "not loaded yet" from "absent". An empty cache used to read as "this table has no such column",
    /// so clearing it would have squiggled every qualified column until the warm pass refilled it.
    /// </para>
    /// </summary>
    private void InvalidateObjectCaches()
    {
        _columnCache.Clear();
        _routineParameterCache.Clear();
        _objectDetailCache.Clear();
    }

    /// <summary>Synchronous cache read for the signature-help snapshot (M6). Null when not loaded.</summary>
    internal IReadOnlyList<RoutineParameterMetadata>? TryGetCachedRoutineParameters(string routineName)
        => _routineParameterCache.TryGetValue(routineName, out var ps) ? ps : null;

    /// <summary>
    /// Loads (and caches) a procedure's or function's parameters from Firebird — inputs and outputs
    /// for a procedure, the argument list for a function (all input). Safe to call repeatedly.
    /// Returns an empty list when no connection is active, the routine isn't a loaded proc/function,
    /// or the read fails. Mirror of <see cref="EnsureColumnsAsync"/>; the editor warms this on a
    /// signature-help cache miss, then rebuilds the snapshot.
    /// </summary>
    internal async Task<IReadOnlyList<RoutineParameterMetadata>> EnsureRoutineParametersAsync(
        string routineName, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(routineName)) return Array.Empty<RoutineParameterMetadata>();
        if (_routineParameterCache.TryGetValue(routineName, out var cached)) return cached;
        if (!_service.IsConnected) return Array.Empty<RoutineParameterMetadata>();

        var obj = TryResolveLoadedObject(routineName);
        if (obj is null) return Array.Empty<RoutineParameterMetadata>();

        try
        {
            var list = new List<RoutineParameterMetadata>();
            if (obj.Kind == MetadataObjectKind.Procedure)
            {
                var inputs = await _tableDetailReader.GetProcedureParametersAsync(routineName, 0, ct).ConfigureAwait(true);
                var outputs = await _tableDetailReader.GetProcedureParametersAsync(routineName, 1, ct).ConfigureAwait(true);
                foreach (var p in inputs) list.Add(ToRoutineParam(p, ParameterDirection.Input));
                foreach (var p in outputs) list.Add(ToRoutineParam(p, ParameterDirection.Output));
            }
            else if (obj.Kind == MetadataObjectKind.Function)
            {
                var sig = await _tableDetailReader.GetFunctionSignatureAsync(routineName, ct).ConfigureAwait(true);
                foreach (var a in sig.Arguments) list.Add(ToRoutineParam(a, ParameterDirection.Input));
            }
            else
            {
                return Array.Empty<RoutineParameterMetadata>();
            }

            _routineParameterCache[routineName] = list;
            return list;
        }
        catch (MetadataReadException)
        {
            return Array.Empty<RoutineParameterMetadata>();
        }
    }

    private static RoutineParameterMetadata ToRoutineParam(ProcedureParameterInfo p, ParameterDirection direction)
        => new(p.Name, p.Type, direction)
        {
            Nullable = !p.NotNull,
            DefaultValue = p.DefaultValue,
            Description = p.Description,
        };

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
    /// Builds an immutable <see cref="ISqlMetadataProvider"/> snapshot of the active connection's
    /// loaded objects + currently-cached columns, for the editor's semantic model (Etap 5 / M1,
    /// design §22.1). Must be called on the UI thread — it reads the metadata tree and the column
    /// cache; the returned snapshot is detached and safe to consume off-thread.
    /// </summary>
    internal ISqlMetadataProvider CreateMetadataSnapshot()
        => AppMetadataSnapshot.Build(EnumerateLoadedObjects(), _columnCache, _routineParameterCache, _objectDetailCache);

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

    /// <summary>
    /// Ctrl+Click go-to-definition (Etap 6 / M4) for a schema object the Navigation Engine resolved
    /// from the semantic model. Prefers the <b>authoritative kind</b> from loaded metadata — so a
    /// column of a view (which the engine reports as a Table owner) still opens as a View — and only
    /// falls back to <paramref name="fallbackKind"/> when the object isn't in the loaded set. Returns
    /// false only for an empty name.
    /// </summary>
    public bool TryOpenSchemaObject(string? name, MetadataObjectKind fallbackKind)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var obj = TryResolveLoadedObject(name) ?? new MetadataObject(name, fallbackKind);
        OnOpenDdlRequested(obj);
        return true;
    }

    /// <summary>
    /// Fetches an object's reconstructed DDL/source for Peek Definition (Etap 6 / M5) — a read-only,
    /// no-tab inline preview in the editor. Resolves the object's authoritative kind from loaded
    /// metadata (so it's reconstructed correctly), falling back to <paramref name="fallbackKind"/>
    /// when the object isn't loaded. Best-effort: returns null on any failure so a peek never
    /// crashes the editor. Read-only — §0 holds by construction.
    /// </summary>
    internal async Task<string?> FetchObjectDefinitionAsync(string name, MetadataObjectKind fallbackKind)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var obj = TryResolveLoadedObject(name) ?? new MetadataObject(name, fallbackKind);
        try
        {
            return await _ddlReader.FetchDdlAsync(obj).ConfigureAwait(true);
        }
        catch (MetadataReadException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
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
    // The stated default mode for a newly opened View editor (§7.6). Read from the app's ONE
    // PreferencesService at the moment a tab is built — never captured — so the setting takes effect on the next
    // editor opened, exactly as apply-on-change promises.
    //
    // ⚠ It is deliberately READ-ONLY now. Until etap 6 this was a settable flag that the editor's own Easy
    // toggle wrote back, which is why opening a view in Easy mode because of something done to a DIFFERENT view
    // looked like a bug. Switching mode inside an editor is a per-tab action; the default changes only in
    // Settings Center.
    internal bool ViewEasyModeDefault => _preferences.Current.ViewEasyModeDefault;

    internal ViewDetailTabViewModel CreateViewDetail(MetadataObject obj)
    {
        var detail = new ViewDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.CompiledExistingObject += () => _ = OfferRecompileDependentsAsync(obj);
        // The stated data page size (§7.7) — the same seed CreateTableDetail applies, for the same Data grid.
        detail.PageSize = _preferences.Current.DataPageSize;
        // Apply the stated default (existing views only — New View sets Easy after).
        if (detail.CanUseEasyMode) detail.EasyMode = ViewEasyModeDefault;
        return detail;
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

        // Close this object's tab(s) via the single authority, then refresh the tree.
        CloseTabsForObject(MetadataObjectKind.Generator, detail.GeneratorName);
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

        // Close this object's tab(s) via the single authority, then refresh the tree.
        CloseTabsForObject(MetadataObjectKind.Index, detail.IndexName);
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

        // Close this object's tab(s) via the single authority, then refresh the tree.
        CloseTabsForObject(MetadataObjectKind.Domain, detail.DomainName);
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
        detail.CompiledExistingObject += () => _ = OfferRecompileDependentsAsync(obj);
        // D11 seam C — "Debug procedure…" on a package member launches it as a debug root via the one path.
        detail.DebugMemberRequested += member => OpenDebuggerForPackageMember(
            detail.PackageName, member.Name, member.Kind == PackageMemberKind.Function);
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

        // Close this object's tab(s) via the single authority, then refresh the tree.
        CloseTabsForObject(MetadataObjectKind.Package, detail.PackageName);
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

    // ---- Activity Monitor (live database trace) ----

    public bool CanOpenTraceMonitor => _service.IsConnected;

    [RelayCommand(CanExecute = nameof(CanOpenTraceMonitor))]
    private void OpenTraceMonitor()
    {
        // Near-singleton per connection: focus the existing monitor tab if one is open.
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind == WorkspaceTabKind.TraceMonitor)
            {
                SelectTab(tab);
                return;
            }
        }

        var service = new FirebirdTraceService(_service);
        var monitor = new TraceMonitorTabViewModel(service, ResolveSelfAttachmentIdsAsync);
        monitor.OpenInEditorRequested += OnTraceOpenInEditor;
        monitor.CopyToClipboardRequested += sql => ClipboardWriteRequested?.Invoke(sql);

        var newTab = WorkspaceTabViewModel.CreateTraceMonitor(this, monitor, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(newTab);
        SelectTab(newTab);
    }

    private async Task<IReadOnlyList<long>> ResolveSelfAttachmentIdsAsync(CancellationToken ct)
    {
        try { return new[] { await _perfStatsReader.GetDataAttachmentIdAsync(ct).ConfigureAwait(true) }; }
        catch { return Array.Empty<long>(); }
    }

    // Analyze in Performance = open in the editor (as a Saved Query) + reveal the Performance
    // bottom tab. It is NOT auto-run — the statement belongs to another session; the user runs
    // it (F5), which feeds the Performance analysis. Consistent with the Activity Monitor bridge.
    private void OnSessionAnalyzeInPerformance(string sql)
    {
        OnTraceOpenInEditor(sql);
        SelectedBottomTabIndex = PerformanceBottomTabIndex;
    }

    // ---- Session Manager (live database sessions / transactions / health) ----

    public bool CanOpenSessionManager => _service.IsConnected;

    [RelayCommand(CanExecute = nameof(CanOpenSessionManager))]
    private void OpenSessionManager()
    {
        // Near-singleton per connection: focus the existing manager tab if one is open.
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind == WorkspaceTabKind.SessionManager)
            {
                SelectTab(tab);
                return;
            }
        }

        var manager = new SessionManagerTabViewModel(_sessionReader);
        manager.ConfirmationRequested += RequestConfirmAsync;
        manager.CopyToClipboardRequested += text => ClipboardWriteRequested?.Invoke(text);
        // Integration bridges reuse the trace Open-in-SQL-Editor path (drops the statement as a
        // Saved Query — never auto-runs another session's SQL). Analyze also reveals Performance.
        manager.OpenInEditorRequested += OnTraceOpenInEditor;
        manager.AnalyzeInPerformanceRequested += OnSessionAnalyzeInPerformance;

        var newTab = WorkspaceTabViewModel.CreateSessionManager(this, manager, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(newTab);
        SelectTab(newTab);
    }

    // ---- Script Executor (run a multi-statement script in one transaction) ----

    public bool CanOpenScriptExecutor => _service.IsConnected;

    [RelayCommand(CanExecute = nameof(CanOpenScriptExecutor))]
    private void OpenScriptExecutor()
    {
        // Near-singleton per connection: focus the existing script tab if one is open.
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind == WorkspaceTabKind.ScriptExecutor)
            {
                SelectTab(tab);
                return;
            }
        }

        var script = new ScriptExecutorTabViewModel(_scriptParser, _scriptExecutor, _transactionService);
        script.CopyToClipboardRequested += text => ClipboardWriteRequested?.Invoke(text);

        var newTab = WorkspaceTabViewModel.CreateScriptExecutor(this, script, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(newTab);
        SelectTab(newTab);
    }

    // ---- Data Import (clipboard / TXT / CSV / XLSX into a table) ----

    public bool CanOpenDataImport => _service.IsConnected;

    /// <summary>The view supplies the file picker and the clipboard read — the VM never touches an Avalonia
    /// dialog or clipboard type (rule #1). Mirrors <see cref="GlobalSearchRequested"/>.</summary>
    public event Func<Task<string?>>? ImportFilePickRequested;

    public event Func<Task<string?>>? ImportClipboardReadRequested;

    /// <summary>
    /// Remembered height + collapse state of the Data Import bottom panel, held here because the import tab
    /// is transient: the view that owns the splitter may be gone by the time the workspace is saved, so the
    /// value cannot live on it. <see cref="MainWindow"/> reads and seeds these exactly as it does
    /// <c>ResultsPanelHeight</c>; an import tab picks them up when it opens and writes them back as the user
    /// drags.
    /// <para>
    /// Deliberately NOT part of <c>ImportConfiguration</c> — a panel height is a layout preference, not a
    /// decision about an import (§4.8.2).
    /// </para>
    /// </summary>
    public double ImportPanelHeight { get; set; } = DefaultImportPanelHeight;

    public bool ImportPanelCollapsed { get; set; }

    private const double DefaultImportPanelHeight = 190;

    [RelayCommand(CanExecute = nameof(CanOpenDataImport))]
    private async Task OpenDataImportAsync()
    {
        // Near-singleton per connection, exactly like the Script Executor: the same import is run over and
        // over, so a second tab would be two states for one job.
        foreach (var tab in WorkspaceTabs)
        {
            if (tab.Kind == WorkspaceTabKind.DataImport)
            {
                SelectTab(tab);
                return;
            }
        }

        // The environment facts readiness needs are read as DELEGATES rather than snapshotted, so the strip
        // reflects the connection and transaction as they are now — not as they were when the tab opened.
        // The connection name is read the same way, for the same reason: it is what band H states about
        // where the rows are going.
        // The table list and the target's shape are read on the METADATA lane (read-only, implicit
        // per-command transactions) — nothing about choosing a target may touch the user's working
        // transaction. Passed as delegates, like the environment facts above, so the VM stays testable
        // without a database and no Firebird type reaches a ViewModel (rule #1).
        var targetReader = new FirebirdImportTargetReader(_metadataReader, _metadataLane);

        // ⭐ I7.5: the module's OWN attachment and OWN transaction, on the same fundament the debugger uses.
        // It used to write into THE user working transaction, which meant its Commit could also persist
        // whatever the SQL Editor had left uncommitted — a button must do exactly what it says, so the
        // possibility was removed rather than warned about (design §4.5 as amended).
        ImportSessionConnection importSession;
        try
        {
            importSession = await _service.CreateImportSessionAsync().ConfigureAwait(true);
        }
        catch (ConnectionFailedException ex)
        {
            SetError(ex.Message);
            return;
        }

        var targetPreparer = new FirebirdImportTargetPreparer(importSession);
        var connectionId = _service.ActiveProfile?.Id;

        var environment = new DataImportEnvironment(
            () => _service.IsConnected,
            () => _service.ActiveProfile?.Name ?? string.Empty)
        {
            // ⭐ The CONNECTION charset, not the column's: I0 measured that a character it cannot represent is
            // stored as '?' with no error at all, even into a UTF8 column (design R1).
            ConnectionCharset = () => _service.ActiveProfile?.Charset ?? string.Empty,

            // ⭐ The clipboard is a SOURCE the recalculation chain reads, so it has to be answerable before the
            // tab's first chain run — which is why it belongs in the environment and not, like the file picker,
            // in an event wired after construction. That is what lets a surface opened on a clipboard
            // configuration read the clipboard by itself.
            ReadClipboardAsync = ImportClipboardReadRequested is null
                ? null
                : () => ImportClipboardReadRequested.Invoke(),

            ListTablesAsync = async ct =>
            {
                var tables = await _metadataReader.ListAsync(MetadataObjectKind.Table, ct).ConfigureAwait(false);
                return tables.Select(t => t.Name).ToList();
            },
            ReadTargetAsync = (table, ct) => targetReader.ReadTargetAsync(table, ct),

            // ⭐ I8 — the new table's CREATE (and its DROP on failure) go to the **Ddl** lane: autonomous,
            // auto-committed, WAIT-bounded. Not a preference — gotcha #213: a Firebird transaction cannot use
            // an object whose DDL it has not committed, so creating the table inside the import's own
            // transaction would make every INSERT fail with "table unknown". The price, that a Rollback cannot
            // remove the table, is stated in the Target section, in the readiness strip and in the report
            // (§0.5). It is the SAME executor the object editors compile through — nothing new (§4.6).
            CreateTableAsync = (sql, ct) => _ddlExecutor.ExecuteAsync(sql, ct),
            DropTableAsync = (sql, ct) => _ddlExecutor.ExecuteAsync(sql, ct),

            // ⭐ The tree learns about the table from the operation that made it, not from a full refresh.
            // A 21st `Metadata.RefreshAsync()` would have been correct in a minute and would have cost a
            // catalog re-read of all 13 categories plus a quadratic re-projection of every expanded one —
            // over a second of frozen UI to discover a single table this code already knows the name of.
            TableCreated = name =>
                Metadata.ApplyObjectAddedInPlace(new MetadataObject(name, MetadataObjectKind.Table)),
            TableDropped = name =>
                Metadata.ApplyObjectRemovedInPlace(new MetadataObject(name, MetadataObjectKind.Table)),


            // Batched is the only mode that finishes a transaction on its own, so it is the only one that gets
            // the decorator — Manual and AutoCommitOnSuccess run through byte-identical code (§4.5).
            CreateWriter = configuration =>
            {
                var writer = new FirebirdImportWriter(importSession, configuration.ErrorPolicy);
                return configuration.Transaction == EmberTern.Core.Import.ImportTransactionMode.Batched
                    ? new BatchedCommitImportWriter(writer, importSession, configuration.CommitEveryRows)
                    : writer;
            },
            CountTargetRowsAsync = (table, ct) => targetPreparer.CountRowsAsync(table, ct),
            EmptyTargetAsync = (table, ct) => targetPreparer.EmptyAsync(table, ct),
            CommitAsync = () => importSession.CommitAsync(),
            RollbackAsync = () => importSession.RollbackAsync(),

            // "Last used" is the implicit profile (§4.8.4) — the same store the named ones use, which is the
            // whole point: I11 built nothing new for them, it only gave the store's list a name column and a UI.
            LoadLastUsed = () => _importProfiles.GetLastUsed(connectionId),
            SaveLastUsed = configuration => _importProfiles.SaveLastUsed(connectionId, configuration),

            // Named profiles (etap I11). The connection id is resolved HERE and never reaches the surface:
            // which database this is, is not a decision about how to read a file (§4.8.2).
            ListProfiles = () => _importProfiles.ListNamed(connectionId),
            SaveProfile = (name, configuration) => _importProfiles.SaveNamed(connectionId, name, configuration),
            RenameProfile = (id, name) => _importProfiles.Rename(id, name),
            DeleteProfile = id => _importProfiles.Delete(id),
        };

        var import = new DataImportTabViewModel(environment);

        if (ImportFilePickRequested is not null)
            import.FilePickRequested += () => ImportFilePickRequested.Invoke();
        import.CopyToClipboardRequested += text => ClipboardWriteRequested?.Invoke(text);

        // Hand the tab the remembered panel layout, and follow it back as the user drags. The tab is
        // near-singleton and transient, so this VM is where the value outlives it.
        import.BottomPanelHeight = ImportPanelHeight;
        import.IsBottomPanelCollapsed = ImportPanelCollapsed;
        import.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DataImportTabViewModel.BottomPanelHeight)) ImportPanelHeight = import.BottomPanelHeight;
            else if (e.PropertyName == nameof(DataImportTabViewModel.IsBottomPanelCollapsed)) ImportPanelCollapsed = import.IsBottomPanelCollapsed;
        };

        // While the tab lives, its unsettled rows are the application's business. Teardown goes through the
        // same per-kind dispatch every other tool tab uses (below) — the registry answers "is anything
        // uncommitted", the dispatch answers "whose resource is this to close".
        _importSession = importSession;
        _importPendingWork = new ImportSessionWork(importSession);
        _pendingWork.Register(_importPendingWork);

        var newTab = WorkspaceTabViewModel.CreateDataImport(this, import, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(newTab);
        SelectTab(newTab);
    }

    /// <summary>
    /// Ends the import's session: it stops being a pending-work source and its attachment is closed (which
    /// rolls back anything unsettled).
    /// <para>
    /// ⚠ By the time this runs the decision must already have been taken. The close and disconnect guards ask
    /// the registry FIRST and offer Commit / Rollback / Cancel; this is the teardown that follows, not a place
    /// where data is silently discarded.
    /// </para>
    /// </summary>
    private void ReleaseImportSession()
    {
        if (_importPendingWork is not null)
        {
            _pendingWork.Unregister(_importPendingWork);
            _importPendingWork = null;
        }

        var session = _importSession;
        _importSession = null;
        if (session is not null) _ = session.DisposeAsync();
    }

    // ---- Global Search (metadata names + source bodies) ----

    public bool CanOpenGlobalSearch => _service.IsConnected;

    // Raised by OpenGlobalSearch — the view shows GlobalSearchDialog and returns the
    // query (or null on cancel). VM stays free of Avalonia dialog types.
    public event Func<Task<MetadataSearchQuery?>>? GlobalSearchRequested;

    [RelayCommand(CanExecute = nameof(CanOpenGlobalSearch))]
    private async Task OpenGlobalSearchAsync()
    {
        if (GlobalSearchRequested is null) return;
        var query = await GlobalSearchRequested.Invoke().ConfigureAwait(true);
        if (query is null || string.IsNullOrWhiteSpace(query.Term)) return;

        // A fresh tab per phrase — never overwrites earlier results.
        var vm = new GlobalSearchTabViewModel(this, _searchReader, _ddlReader, query);
        var tab = WorkspaceTabViewModel.CreateGlobalSearch(this, vm, query.Term, _service.ActiveProfile?.Id);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
        await vm.RunAsync().ConfigureAwait(true);
    }

    // Non-destructive: a traced statement lands as a NEW Saved Query (never overwrites the
    // editor's current content). The previously-edited query is preserved as its own Saved
    // Query — selecting the new one just swaps the editor to it.
    private void OnTraceOpenInEditor(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return;
        var query = WorkspaceTabs.FirstOrDefault(t => t.Kind == WorkspaceTabKind.Query);
        if (query is not null) SelectTab(query);
        IsQueryPanelVisible = true; // reveal the panel so the new query is visible
        var sq = new SavedQueryViewModel(Guid.NewGuid().ToString("N"), BuildTraceQueryName(sql), sql, this);
        SavedQueries.Add(sq);
        SelectedSavedQuery = sq;
    }

    private static string BuildTraceQueryName(string sql)
    {
        var flat = System.Text.RegularExpressions.Regex.Replace(sql.Trim(), @"\s+", " ");
        if (flat.Length > 40) flat = flat[..40] + "…";
        return "Trace: " + flat;
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

        // Close this object's tab(s) via the single authority, then refresh the tree.
        CloseTabsForObject(MetadataObjectKind.Exception, detail.ExceptionName);
        await Metadata.RefreshAsync().ConfigureAwait(true);
    }

    // Single construction point for ProcedureDetail VMs — mirrors CreateViewDetail.
    // The procedure source IS editable (Compile), so the DDL executor is wired;
    // there is no data editor (procedures have no Data tab).
    /// <inheritdoc cref="ViewEasyModeDefault"/>
    internal bool ProcedureEasyModeDefault => _preferences.Current.ProcedureEasyModeDefault;

    internal ProcedureDetailTabViewModel CreateProcedureDetail(MetadataObject obj)
    {
        var detail = new ProcedureDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.CompiledExistingObject += () => _ = OfferRecompileDependentsAsync(obj);
        detail.RunExecuteRequested = RunProcedureExecuteAsync;
        // Editor-toolbar Debug entry point (Stage X / D5) — reuses the one debugger-launch path.
        detail.DebugRequested = () => OpenDebuggerForObject(detail.ProcedureName, MetadataObjectKind.Procedure);
        // Its OWN Performance context — analyzes only this procedure tab's Execute.
        detail.PerformanceContext = CreatePerformanceContext();
        // Lazy column loader for the Variables grid's merged Domain/Column picker.
        detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
        // Best-effort domain + table lists for the Variables grid (Easy mode).
        _ = LoadProcedureListsAsync(detail);
        // Apply the stated default (existing procedures only — New stays Source).
        if (detail.CanUseEasyMode) detail.EasyMode = ProcedureEasyModeDefault;
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

    // Single construction point for TriggerDetail VMs — mirrors CreateProcedureDetail.
    // The trigger source IS editable (Compile), so the DDL executor is wired. The Easy
    // mode Variables grid needs the domain list and the Table picker needs the table
    // list — both loaded best-effort.
    /// <inheritdoc cref="ViewEasyModeDefault"/>
    internal bool TriggerEasyModeDefault => _preferences.Current.TriggerEasyModeDefault;

    internal TriggerDetailTabViewModel CreateTriggerDetail(MetadataObject obj)
    {
        var detail = new TriggerDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.CompiledExistingObject += () => _ = OfferRecompileDependentsAsync(obj);
        // Editor-toolbar Debug entry point (Stage X / D10) — reuses the one debugger-launch path.
        detail.DebugRequested = () => OpenDebuggerForObject(detail.TriggerName, MetadataObjectKind.Trigger);
        // Lazy column loader for the Variables grid's merged Domain/Column picker.
        detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
        _ = LoadTriggerListsAsync(detail);
        if (detail.CanUseEasyMode) detail.EasyMode = TriggerEasyModeDefault;
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

    // Single construction point for FunctionDetail VMs — mirrors CreateProcedureDetail.
    // The function source IS editable (Compile) and the function IS executable (Data
    // lane), so both the DDL executor and the Execute callback are wired.
    /// <inheritdoc cref="ViewEasyModeDefault"/>
    internal bool FunctionEasyModeDefault => _preferences.Current.FunctionEasyModeDefault;

    internal FunctionDetailTabViewModel CreateFunctionDetail(MetadataObject obj)
    {
        var detail = new FunctionDetailTabViewModel(
            obj.Name,
            _tableDetailReader,
            _ddlReader,
            _ddlExecutor);
        detail.OpenObjectRequested += OnOpenDdlRequested;
        detail.ConfirmationRequested += RequestConfirmAsync;
        detail.CompiledExistingObject += () => _ = OfferRecompileDependentsAsync(obj);
        detail.RunExecuteRequested = RunFunctionExecuteAsync;
        // Editor-toolbar Debug entry point (D-function) — reuses the one debugger-launch path; the function
        // launches as the debug root (DebugLaunchSpec.IsFunction → function root frame + Return group).
        detail.DebugRequested = () => OpenDebuggerForObject(detail.FunctionName, MetadataObjectKind.Function);
        // Its OWN Performance context — analyzes only this function tab's Execute.
        detail.PerformanceContext = CreatePerformanceContext();
        detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
        _ = LoadFunctionListsAsync(detail);
        if (detail.CanUseEasyMode) detail.EasyMode = FunctionEasyModeDefault;
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

    // Runs an Execute Procedure statement on the Data lane with bound parameters
    // (no literal embedding). Wraps the result/error so the procedure tab can show
    // it in its own Result region. EXECUTE PROCEDURE/SELECT are Data-lane per the
    // classifier — auto-begins the data working tx; the user Commits/Rolls back.
    private Task<ProcedureExecOutcome> RunProcedureExecuteAsync(string sql, IReadOnlyList<QueryParameter> parameters)
        => RunExecutableWithMetricsAsync(sql, parameters, UiStrings.ProcedureExecutedViaDataProfile);

    // Runs an Execute Function statement (SELECT fn(...) FROM RDB$DATABASE) on the Data
    // lane with bound parameters. Same wrapping + lane as RunProcedureExecuteAsync.
    private Task<ProcedureExecOutcome> RunFunctionExecuteAsync(string sql, IReadOnlyList<QueryParameter> parameters)
        => RunExecutableWithMetricsAsync(sql, parameters, UiStrings.FunctionExecutedViaDataProfile);

    // Shared Procedure/Function execution: the SAME metrics path the SQL Editor uses
    // (before/after MON$ delta → reads + INSERT/UPDATE/DELETE). Returns the work-summary AND the
    // per-table reads in the outcome so the calling detail tab records them into ITS OWN
    // Performance context (no shared global panel) and shows the per-table exec breakdown.
    private async Task<ProcedureExecOutcome> RunExecutableWithMetricsAsync(
        string sql, IReadOnlyList<QueryParameter> parameters, string executedViaMessage)
    {
        ExecutionTimer.Start(); // proc/func have no IsExecuting flag — drive the live timer directly
        try
        {
            var (result, reads) = await ExecuteWithMetricsAsync(sql, parameters, CancellationToken.None).ConfigureAwait(true);
            AddMessage(MessageSeverity.Info, executedViaMessage);
            return new ProcedureExecOutcome(result, null, BuildExecutionSummary(result, reads), reads);
        }
        catch (QueryExecutionException ex)
        {
            return new ProcedureExecOutcome(null, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new ProcedureExecOutcome(null, ex.Message);
        }
        finally
        {
            ExecutionTimer.Stop();
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
        // The stated data page size (§7.7). Seeded here, at the one construction point, exactly like the
        // Source/Easy default — the grid's own page-size box still overrides it for that grid, which is what
        // makes this a default rather than a limit.
        detail.PageSize = _preferences.Current.DataPageSize;
        // Merged Domena/Kolumna picker (Pola): lazy column loader + best-effort table list
        // for the Table-column (TYPE OF COLUMN) tab. Domains load via the VM's own LoadAsync.
        if (writable)
        {
            detail.ColumnsLoader = new DelegateColumnsLoader(t => EnsureColumnsAsync(t));
            _ = LoadTableListAsync(detail.SetAvailableTables);
            // Copy-as-INSERT/UPDATE on the Dane grid — the SAME catalog snapshot + column warmer the SQL
            // Editor's coordinator uses, so both grids resolve through one mechanism (E6).
            detail.EnableSqlCopy(
                CreateMetadataSnapshot,
                async t => await EnsureColumnsAsync(t).ConfigureAwait(true));
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
        if (tab.Kind == WorkspaceTabKind.TraceMonitor && tab.TraceMonitor is { } monitor)
            _ = monitor.DisposeAsync(); // stop the live trace session (best-effort)
        else if (tab.Kind == WorkspaceTabKind.SessionManager && tab.SessionManager is { } sm)
            _ = sm.DisposeAsync(); // stop the MON$ poll timer (best-effort)
        else if (tab.Kind == WorkspaceTabKind.ScriptExecutor && tab.ScriptExecutor is { } se)
            se.Detach(); // unsubscribe from the transaction-state event
        else if (tab.Kind == WorkspaceTabKind.Debugger && tab.Debugger is { } dbg)
            _ = dbg.DisposeAsync(); // roll back + close the debug session's attachment (§4.4, best-effort)
        else if (tab.Kind == WorkspaceTabKind.DataImport)
            ReleaseImportSession(); // roll back + close the import's own attachment (I7.5)

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

    // Sidebar "Debug procedure…" (Stage X / D4). Opens a debugger tab for a standalone procedure and kicks
    // its preparation (fetch source → parse → launch panel). NOT a singleton — the same procedure may be
    // debugged in two tabs (two sessions). The tab's read-only source + launch panel + stepping live in the
    // child DebuggerTabViewModel; the debug session's own attachment/transaction is opened only on Launch.
    private void OnDebugProcedureRequested(MetadataObject obj)
    {
        if (obj.Kind != MetadataObjectKind.Procedure) return;
        OpenDebuggerForObject(obj.Name, MetadataObjectKind.Procedure);
    }

    private void OnDebugTriggerRequested(MetadataObject obj)
    {
        if (obj.Kind != MetadataObjectKind.Trigger) return;
        OpenDebuggerForObject(obj.Name, MetadataObjectKind.Trigger);
    }

    private void OnDebugFunctionRequested(MetadataObject obj)
    {
        if (obj.Kind != MetadataObjectKind.Function) return;
        OpenDebuggerForObject(obj.Name, MetadataObjectKind.Function);
    }

    // The one debugger-launch path, reused by the sidebar "Debug procedure…" / "Debug trigger…" and the
    // procedure/trigger editor's Debug toolbar button (Stage X / D5, D10) — the buttons are only additional
    // entry points, not new logic. The kind selects how the source is fetched (procedure vs trigger) and, for a
    // trigger, the columns provider types its NEW/OLD context grid.
    private void OpenDebuggerForObject(string name, MetadataObjectKind kind)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!_service.IsConnected)
        {
            AddMessage(MessageSeverity.Error, UiStrings.DebuggerNoConnection);
            return;
        }

        var launcher = new EmberTern.App.Debugging.FirebirdDebugSessionLauncher(_service);
        var debugger = new DebuggerTabViewModel(
            name,
            ct => FetchObjectDefinitionAsync(name, kind),
            launcher,
            _parameterHistory,
            _service.ActiveProfile?.Id,
            _watchStore,
            columnsProvider: (t, ct) => EnsureColumnsAsync(t, ct),
            // The stated default isolation (§7.3). Read at construction, through the ONE boundary that turns a
            // stored key into a DebugIsolation — the launch panel's Advanced selector still overrides it per run.
            defaultIsolation: DebuggerIsolationPreference.From(_preferences.Current));

        // Seam 5b — the debugger tab saves + compiles the routine it is debugging through the SAME Ddl-lane
        // executor and the SAME confirmation dialog every object editor uses; no second save mechanism.
        debugger.DdlExecutor = _ddlExecutor;
        debugger.ConfirmationRequested += RequestConfirmAsync;

        var tab = WorkspaceTabViewModel.CreateDebugger(this, debugger, name, _service.ActiveProfile?.Id, kind);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
        _ = debugger.PrepareAsync();
    }

    // Debugger launch for a PACKAGE member (Stage X / D11 seam C — procedures; Seam D — functions) — the Package
    // editor's Members-tab Debug entry point. Reuses the ONE launch path (OpenDebuggerForObject's shape): the
    // source provider reconstructs the member as a standalone CREATE PROCEDURE/FUNCTION (by kind) and the package
    // name is threaded into the launch so the executor builds a package root frame (sibling-call resolution +
    // package-keyed catalog params). A FUNCTION member reconstructs as CREATE FUNCTION → the VM sets IsFunction →
    // the launcher builds a packaged function root (D1). The tab title is the qualified PKG.MEMBER name; the
    // frame name is the member name (matching how a stepped-into package member is named, seam B).
    internal void OpenDebuggerForPackageMember(string packageName, string memberName, bool isFunction)
    {
        if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(memberName)) return;
        if (!_service.IsConnected)
        {
            AddMessage(MessageSeverity.Error, UiStrings.DebuggerNoConnection);
            return;
        }

        var launcher = new EmberTern.App.Debugging.FirebirdDebugSessionLauncher(_service);
        var debugger = new DebuggerTabViewModel(
            memberName,
            ct => FetchPackageMemberSourceAsync(packageName, memberName, isFunction, ct),
            launcher,
            _parameterHistory,
            _service.ActiveProfile?.Id,
            _watchStore,
            columnsProvider: (t, ct) => EnsureColumnsAsync(t, ct),
            packageName: packageName,
            // Same stated default as the other entry point (§7.3) — a package member's launch panel opens with
            // the user's isolation for the same reason a standalone routine's does.
            defaultIsolation: DebuggerIsolationPreference.From(_preferences.Current));

        // No DdlExecutor here on purpose (Seam 5b): a package member's source is RECONSTRUCTED as a
        // standalone CREATE PROCEDURE/FUNCTION so the engine can frame it — compiling that text would create
        // a standalone routine instead of altering the package. Editing a package member stays the Package
        // editor's job; the VM refuses to save a package tab regardless, this just never offers it.
        var title = string.Format(CultureInfo.CurrentCulture, "{0}.{1}", packageName, memberName);
        var tab = WorkspaceTabViewModel.CreateDebugger(
            this, debugger, title, _service.ActiveProfile?.Id,
            isFunction ? MetadataObjectKind.Function : MetadataObjectKind.Procedure);
        WorkspaceTabs.Add(tab);
        SelectTab(tab);
        _ = debugger.PrepareAsync();
    }

    // Source provider for a package member launch: the member reconstructed as a standalone CREATE
    // PROCEDURE/FUNCTION by kind (the same reconstruction the step-into path uses, via the one shared SqlParser
    // reconstructor). A FUNCTION reconstructs as CREATE FUNCTION so the VM detects IsFunction. Returns null on a
    // read failure / missing member → the VM reports "source unavailable".
    private async Task<string?> FetchPackageMemberSourceAsync(
        string packageName, string memberName, bool isFunction, CancellationToken cancellationToken)
    {
        var kind = isFunction
            ? EmberTern.Core.Sql.Language.Ast.SubroutineKind.Function
            : EmberTern.Core.Sql.Language.Ast.SubroutineKind.Procedure;
        try
        {
            return await _ddlReader.FetchPackageMemberSourceAsync(packageName, memberName, kind, cancellationToken).ConfigureAwait(true);
        }
        catch (MetadataReadException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // ─── Bulk operations (recompile / recompute stats / activate-deactivate) ──
    // One dialog-request event + one execution pipeline reused by every bulk op. The
    // dialog opens IMMEDIATELY with a live VM; rows + counters update as each object
    // completes; Cancel stops the remainder.
    public event Func<BatchResultsViewModel, Task>? BatchResultsRequested;

    // A prepared bulk operation: the SQL steps to run + rows that already failed during
    // preparation (e.g. a source fetch failed during recompile → no SQL to run for them).
    private readonly record struct BatchPlan(
        IReadOnlyList<(string Object, string Operation, string Sql)> Steps,
        IReadOnlyList<BatchOperationResult> PreResults);

    // Opens the batch-results dialog IMMEDIATELY (so feedback is instant even on large
    // schemas), runs <paramref name="prepareAsync"/> INSIDE it — reporting live progress
    // into the dialog's preparing view while the object list + per-object SQL are built —
    // then switches to the execution view and streams each statement's outcome (autonomous
    // per-statement autocommit, continues past failures). Cancel (or closing the dialog)
    // stops both the preparation loop and the remaining execution. The preparing phase is
    // what removes the pre-sprint 10–15s "app looks busy, nothing shown" gap: preparation
    // (especially recompile's one-source-fetch-per-object loop) now happens with the dialog
    // already on screen.
    private async Task<BatchResultsViewModel> RunBatchWithReportAsync(
        string title,
        Func<BatchResultsViewModel, CancellationToken, Task<BatchPlan>> prepareAsync,
        bool refreshAfter,
        Func<IReadOnlyList<(string Object, string Operation, string Sql)>,
             IProgress<(int Index, string? Error)>, CancellationToken, Task>? executeAsync = null)
    {
        var vm = new BatchResultsViewModel(title); // opens in IsPreparing=true

        async Task RunAsync()
        {
            var prepSw = System.Diagnostics.Stopwatch.StartNew();
            BatchPlan plan;
            try
            {
                plan = await prepareAsync(vm, vm.CancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                vm.Complete(); // cancelled before any step ran
                return;
            }
            catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException or DdlExecutionException)
            {
                // Preparation couldn't even build the plan (e.g. the object list query
                // failed). Surface it in the dialog AND the Messages log (unchanged).
                vm.FailPreparation(ex.Message);
                AddMessage(MessageSeverity.Error, ex.Message);
                return;
            }

            var steps = plan.Steps;
            var pre = plan.PreResults;
            BatchTrace.LogPrepareDone(steps.Count, pre.Count, prepSw.ElapsedMilliseconds);

            vm.Begin(pre.Count + steps.Count); // preparing → execution view
            foreach (var p in pre)
            {
                vm.AddResult(p);
            }

            // Progress is constructed on the UI thread → Report marshals back to it, so the
            // live VM updates are UI-thread-safe while the executor runs on the thread pool.
            var progress = new Progress<(int Index, string? Error)>(p =>
                vm.AddResult(new BatchOperationResult(
                    steps[p.Index].Object, steps[p.Index].Operation, p.Error is null, p.Error)));

            var execSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (steps.Count > 0)
                {
                    // Default: run each step's SQL on the DDL lane (recompile / activate / recompute).
                    // A caller may override the execution strategy (e.g. Save-and-close drives each
                    // dirty editor's own SaveAsync) — the reporting/progress shell is identical either way.
                    if (executeAsync is not null)
                        await executeAsync(steps, progress, vm.CancellationToken).ConfigureAwait(true);
                    else
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
            BatchTrace.LogExecDone(steps.Count, execSw.ElapsedMilliseconds, vm.CancellationToken.IsCancellationRequested);
            vm.Complete();
            if (steps.Count == 0 && pre.Count == 0)
            {
                AddMessage(MessageSeverity.Info, UiStrings.BatchNothingToDo);
            }
            if (refreshAfter && !vm.CancellationToken.IsCancellationRequested)
            {
                await Metadata.RefreshAsync().ConfigureAwait(true);
            }
        }

        var execTask = RunAsync();
        if (BatchResultsRequested is { } show)
        {
            await show(vm).ConfigureAwait(true); // modal — runs while prep + batch stream in
        }
        vm.RequestCancel();                       // dialog closed → stop any remaining work
        await execTask.ConfigureAwait(true);
        return vm;
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
        // Targeted in-place flip — NOT a full RefreshAsync (which would reproject the whole tree and
        // lose scroll/selection). The DDL succeeded, so the state is now `activate`.
        Metadata.ApplyTriggerActiveStateInPlace(new[] { obj.Name }, activate);
    }

    // Bulk trigger activate/deactivate over the visible (filtered) set or ALL. The reader
    // gives authoritative state so we skip triggers already in the target state (avoids
    // needless DDL commits — gotcha #109). The dialog opens first; this enumerate+build
    // runs inside its preparing view.
    private async void OnBulkSetActiveRequested(TriggerBulkRequest req)
    {
        if (req.Kind != MetadataObjectKind.Trigger) return;

        // "Selected" is a user-picked subset (possibly large or accidental) → show the count and
        // confirm before running. "All"/"Visible" keep their existing behaviour (no pre-confirm —
        // the live batch dialog shows the count as it runs), so those paths are unaffected.
        if (req.Scope == BatchOperationScope.Selected)
        {
            var confirmed = await RequestConfirmAsync(new ConfirmRequest
            {
                Title = req.Activate ? UiStrings.BatchConfirmActivateSelectedTitle : UiStrings.BatchConfirmDeactivateSelectedTitle,
                Message = string.Format(CultureInfo.CurrentCulture,
                    req.Activate ? UiStrings.BatchConfirmActivateSelectedFormat : UiStrings.BatchConfirmDeactivateSelectedFormat,
                    req.Names.Count),
                ConfirmLabel = req.Activate ? UiStrings.BatchOpActivate : UiStrings.BatchOpDeactivate,
                CancelLabel = UiStrings.DialogCancel,
                IsDestructive = false,
            }).ConfigureAwait(true);
            if (!confirmed) return;
        }

        var title = req.Activate ? UiStrings.BatchTitleActivateTriggers : UiStrings.BatchTitleDeactivateTriggers;
        // refreshAfter:false — a full RefreshAsync reprojects the whole tree and loses scroll/selection
        // (the reported jump). Instead reflect the change IN PLACE afterwards (no reproject → keeps
        // scroll/selection/expansion) for EVERY trigger whose step succeeded. Using SuccessfulObjects
        // (not req.Names) is correct under partial-failure AND cancel (only actually-changed triggers
        // flip) — the results are all applied by the time the modal batch dialog closes. (NB: don't
        // gate this on report.CancellationToken — RunBatchWithReportAsync always RequestCancel()s as
        // cleanup, so that token is always cancelled here.)
        var report = await RunBatchWithReportAsync(
            title, (vm, ct) => BuildTriggerBulkPlanAsync(req, vm, ct), refreshAfter: false).ConfigureAwait(true);
        Metadata.ApplyTriggerActiveStateInPlace(report.SuccessfulObjects, req.Activate);
    }

    private async Task<BatchPlan> BuildTriggerBulkPlanAsync(TriggerBulkRequest req, BatchResultsViewModel vm, CancellationToken ct)
    {
        vm.ReportPreparation(string.Format(CultureInfo.CurrentCulture, UiStrings.BatchPreparingListFormat, "triggers"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var triggers = await _metadataReader.ListAsync(MetadataObjectKind.Trigger, ct).ConfigureAwait(true);
        BatchTrace.LogListEnumerate("Trigger", triggers.Count, sw.ElapsedMilliseconds);

        vm.ReportPreparation(UiStrings.BatchPreparingBuildList);
        var targets = ResolveTriggerBulkTargets(triggers, req.Scope, req.Names, req.Activate);

        var op = req.Activate ? UiStrings.BatchOpActivate : UiStrings.BatchOpDeactivate;
        var steps = targets
            .Select(t => (t.Name, op, req.Activate
                ? DdlGenerator.BuildAlterTriggerActive(t.Name)
                : DdlGenerator.BuildAlterTriggerInactive(t.Name)))
            .ToList<(string, string, string)>();

        return new BatchPlan(steps, Array.Empty<BatchOperationResult>());
    }

    /// <summary>Pure: the trigger objects a bulk op should touch — scope filter (All = every trigger;
    /// Visible/Selected = only those in <paramref name="names"/>, case-insensitive) then skip any
    /// already in the target state. Same logic for All/Visible as before "Selected" was added.</summary>
    internal static IReadOnlyList<MetadataObject> ResolveTriggerBulkTargets(
        IReadOnlyList<MetadataObject> allTriggers, BatchOperationScope scope, IReadOnlyList<string> names, bool activate)
    {
        IEnumerable<MetadataObject> scoped = allTriggers;
        if (scope != BatchOperationScope.All)
        {
            var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            scoped = allTriggers.Where(t => set.Contains(t.Name));
        }
        return scoped.Where(t => t.IsActive != activate).ToList(); // skip already-in-state
    }

    // Recompile every object of a kind (Procedure/Function/Trigger/Package) by re-running
    // its stored CREATE OR ALTER / RECREATE source. Source-fetch failures become failed
    // rows in the report (no SQL to run for them).
    private async void OnRecompileGroupRequested(MetadataObjectKind kind)
    {
        var title = string.Format(CultureInfo.CurrentCulture, UiStrings.BatchTitleRecompileFormat, KindNoun(kind));
        await RunBatchWithReportAsync(title, async (vm, ct) =>
        {
            var (steps, pre) = await BuildRecompileStepsAsync(kind, vm, ct).ConfigureAwait(true);
            return new BatchPlan(steps, pre);
        }, refreshAfter: false).ConfigureAwait(true);
    }

    // Shared source-fetch + step-build for recompile — the dominant preparation cost: one
    // source-fetch round-trip PER object (the reason recompile felt slow before the dialog
    // opened up front). Reports live "Loading procedures 143 / 1965" progress into the
    // preparing view and observes cancellation between objects. Throws
    // MetadataReadException / InvalidOperationException if the object LIST can't be read
    // (the caller turns that into a preparation error); per-object source-fetch failures
    // are captured as failed rows. Package emits two steps (header + body).
    private async Task<(List<(string, string, string)> Steps, List<BatchOperationResult> PreFailures)>
        BuildRecompileStepsAsync(MetadataObjectKind kind, BatchResultsViewModel vm, CancellationToken ct)
    {
        var noun = KindNoun(kind) + "s";
        vm.ReportPreparation(string.Format(CultureInfo.CurrentCulture, UiStrings.BatchPreparingListFormat, noun));
        var listSw = System.Diagnostics.Stopwatch.StartNew();
        var objs = await _metadataReader.ListAsync(kind, ct).ConfigureAwait(true);
        BatchTrace.LogListEnumerate(kind.ToString(), objs.Count, listSw.ElapsedMilliseconds);
        return await BuildRecompileStepsForObjectsAsync(objs, noun, vm, ct).ConfigureAwait(true);
    }

    // Builds recompile steps for a SPECIFIC set of objects, fetching each by ITS OWN kind —
    // reused by "Recompile all/group" (a uniform-kind list) and "Recompile dependents" (a
    // mixed-kind list). Per-object source-fetch failures become failed rows; Package emits two
    // steps (header + body).
    private async Task<(List<(string, string, string)> Steps, List<BatchOperationResult> PreFailures)>
        BuildRecompileStepsForObjectsAsync(IReadOnlyList<MetadataObject> objects, string noun, BatchResultsViewModel vm, CancellationToken ct)
    {
        var preFailures = new List<BatchOperationResult>();
        var op = UiStrings.BatchOpRecompile;
        var steps = new List<(string, string, string)>();
        var fetchSw = System.Diagnostics.Stopwatch.StartNew();
        var i = 0;
        foreach (var o in objects)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            vm.ReportPreparation(i, objects.Count, string.Format(
                CultureInfo.CurrentCulture, UiStrings.BatchPreparingLoadFormat, noun, i, objects.Count));
            try
            {
                switch (o.Kind)
                {
                    case MetadataObjectKind.Procedure:
                        steps.Add((o.Name, op, await _ddlReader.FetchProcedureSourceAsync(o, ct).ConfigureAwait(true)));
                        break;
                    case MetadataObjectKind.Function:
                        steps.Add((o.Name, op, await _ddlReader.FetchFunctionSourceAsync(o, ct).ConfigureAwait(true)));
                        break;
                    case MetadataObjectKind.Trigger:
                        steps.Add((o.Name, op, await _ddlReader.FetchTriggerSourceAsync(o, ct).ConfigureAwait(true)));
                        break;
                    case MetadataObjectKind.View:
                        steps.Add((o.Name, op, await _ddlReader.FetchViewSourceAsync(o, ct).ConfigureAwait(true)));
                        break;
                    case MetadataObjectKind.Package:
                        steps.Add((o.Name, UiStrings.BatchOpRecompileHeader, await _ddlReader.FetchPackageHeaderSourceAsync(o, ct).ConfigureAwait(true)));
                        steps.Add((o.Name, UiStrings.BatchOpRecompileBody, await _ddlReader.FetchPackageBodySourceAsync(o, ct).ConfigureAwait(true)));
                        break;
                }
            }
            catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
            {
                preFailures.Add(new BatchOperationResult(o.Name, op, false, ex.Message));
            }
        }
        BatchTrace.LogSourceFetch(noun, steps.Count, preFailures.Count, fetchSw.ElapsedMilliseconds);
        return (steps, preFailures);
    }

    // ─── Recompile Dependents (Part 2) — offered after a successful Compile, never automatic ──
    /// <summary>View shows the checklist dialog and returns the selection (null on Skip/Cancel).</summary>
    public event Func<RecompileDependentsRequest, Task<RecompileDependentsResult?>>? RecompileDependentsRequested;

    // Session-scoped "don't ask again" — set when the user checks that box in the dialog.
    private bool _suppressRecompileOffer;

    // True only while SaveDirtyEditorsAsync is compiling the dirty editors during a
    // "Save and close / Save and disconnect" — suppresses the per-object "recompile
    // dependents?" offer that each compile would otherwise trigger mid-shutdown.
    private bool _bulkSaveInProgress;

    // Raised by a source-object editor after a successful Compile of an EXISTING object.
    private async Task OfferRecompileDependentsAsync(MetadataObject compiled)
    {
        // During a Save-and-close batch every dirty editor compiles in turn; each fires
        // CompiledExistingObject, which would otherwise pop the "recompile dependents?"
        // dialog mid-shutdown. Suppress it for the duration of the batch.
        if (_bulkSaveInProgress) return;
        if (_suppressRecompileOffer) return;
        if (RecompileDependentsRequested is not { } ask) return;

        IReadOnlyList<DependencyInfo> dependents;
        try
        {
            dependents = await GetDependentsAsync(compiled, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
        {
            return; // the compile already succeeded — never block on a failed dependents probe
        }

        var candidates = RecompilableDependents(dependents, compiled);
        if (candidates.Count == 0) return;

        var result = await ask(new RecompileDependentsRequest(compiled, candidates)).ConfigureAwait(true);
        if (result is null) return; // skipped / cancelled
        if (result.DontAskAgain) _suppressRecompileOffer = true;
        if (result.Selected.Count == 0) return;

        await RunBatchWithReportAsync(
            string.Format(CultureInfo.CurrentCulture, UiStrings.RecompileDependentsBatchTitleFormat, compiled.Name),
            async (vm, ct) =>
            {
                var (steps, pre) = await BuildRecompileStepsForObjectsAsync(result.Selected, "dependents", vm, ct).ConfigureAwait(true);
                return new BatchPlan(steps, pre);
            },
            refreshAfter: false).ConfigureAwait(true);
    }

    // Routes to the right dependency reader by kind; returns the "depended on by" list.
    private async Task<IReadOnlyList<DependencyInfo>> GetDependentsAsync(MetadataObject compiled, CancellationToken ct)
    {
        var empty = (IReadOnlyList<DependencyInfo>)Array.Empty<DependencyInfo>();
        var (_, dependedOnBy) = compiled.Kind switch
        {
            MetadataObjectKind.Procedure => await _tableDetailReader.GetProcedureDependenciesAsync(compiled.Name, ct).ConfigureAwait(true),
            MetadataObjectKind.Function => await _tableDetailReader.GetFunctionDependenciesAsync(compiled.Name, ct).ConfigureAwait(true),
            MetadataObjectKind.Trigger => await _tableDetailReader.GetTriggerDependenciesAsync(compiled.Name, ct).ConfigureAwait(true),
            MetadataObjectKind.Package => await _tableDetailReader.GetPackageDependenciesAsync(compiled.Name, ct).ConfigureAwait(true),
            MetadataObjectKind.View => await _tableDetailReader.GetDependenciesAsync(compiled.Name, ct).ConfigureAwait(true),
            _ => (empty, empty),
        };
        return dependedOnBy;
    }

    // Pure: filters a "depended on by" list to the RECOMPILABLE kinds
    // (Procedure/Function/Trigger/Package/View), maps to MetadataObject, de-dupes, and drops a
    // self-dependency on the object just compiled. Testable without a database.
    internal static IReadOnlyList<MetadataObject> RecompilableDependents(
        IReadOnlyList<DependencyInfo> dependents, MetadataObject compiled)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<MetadataObject>();
        foreach (var d in dependents)
        {
            if (string.IsNullOrEmpty(d.ObjectName)) continue;
            if (TableDetailTabViewModel.MapObjectTypeToKind(d.ObjectType) is not { } kind) continue;
            if (!IsRecompilableKind(kind)) continue;
            if (kind == compiled.Kind && string.Equals(d.ObjectName, compiled.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(kind + "|" + d.ObjectName)) result.Add(new MetadataObject(d.ObjectName, kind));
        }
        return result;
    }

    internal static bool IsRecompilableKind(MetadataObjectKind kind)
        => kind is MetadataObjectKind.Procedure or MetadataObjectKind.Function
            or MetadataObjectKind.Trigger or MetadataObjectKind.Package or MetadataObjectKind.View;

    // ─── Connection-node (database-wide) bulk ops ─────────────────────────────
    // Recompute selectivity statistics for every index (SET STATISTICS INDEX).
    internal Task RecomputeAllIndexStatisticsAsync()
        => RunBatchWithReportAsync(UiStrings.BatchTitleRecomputeStatistics, BuildRecomputeStatsPlanAsync, refreshAfter: false);

    private async Task<BatchPlan> BuildRecomputeStatsPlanAsync(BatchResultsViewModel vm, CancellationToken ct)
    {
        vm.ReportPreparation(string.Format(CultureInfo.CurrentCulture, UiStrings.BatchPreparingListFormat, "indexes"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var indexes = await _metadataReader.ListAsync(MetadataObjectKind.Index, ct).ConfigureAwait(true);
        BatchTrace.LogListEnumerate("Index", indexes.Count, sw.ElapsedMilliseconds);

        vm.ReportPreparation(UiStrings.BatchPreparingBuildList);
        var op = UiStrings.BatchOpRecomputeStatistics;
        var steps = indexes
            .Select(ix => (ix.Name, op, DdlGenerator.BuildSetIndexStatistics(ix.Name)))
            .ToList<(string, string, string)>();
        return new BatchPlan(steps, Array.Empty<BatchOperationResult>());
    }

    // Recompile every procedure, function, trigger and package. A per-kind LIST failure is
    // recorded as a failed row and the remaining kinds still proceed (unchanged resilience);
    // cancellation propagates out to abort the whole preparation.
    internal Task RecompileAllObjectsAsync()
        => RunBatchWithReportAsync(UiStrings.BatchTitleRecompileAll, BuildRecompileAllPlanAsync, refreshAfter: false);

    private async Task<BatchPlan> BuildRecompileAllPlanAsync(BatchResultsViewModel vm, CancellationToken ct)
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
            ct.ThrowIfCancellationRequested();
            try
            {
                var (steps, pre) = await BuildRecompileStepsAsync(kind, vm, ct).ConfigureAwait(true);
                allSteps.AddRange(steps);
                allPre.AddRange(pre);
            }
            catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
            {
                allPre.Add(new BatchOperationResult(KindNoun(kind) + "s", UiStrings.BatchOpRecompile, false, ex.Message));
            }
        }
        return new BatchPlan(allSteps, allPre);
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
    /// The single authority for closing every workspace tab that represents a given
    /// object (kind, name) after the object is dropped from within EmberTern. Covers
    /// the read-only DDL tab plus every object Detail tab kind — the same kind-set the
    /// open/focus dedup in <see cref="OnOpenDdlRequested"/> uses, so a tab that could be
    /// opened for an object can also be closed for it. Internal so tests can drive it
    /// without standing up the DROP path. Tab-close semantics match the user clicking ×.
    /// (This handles ONLY deletions performed inside EmberTern; external/other-session
    /// deletes are deliberately out of scope for this sprint.)
    /// </summary>
    // ─── Keeping sibling tabs current after a compile (Seam 6d) ────────────
    //
    // Compiling an object updates the database, but nothing used to tell the OTHER tabs showing that object,
    // so a routine saved from the debugger (or from any editor) left a second tab sitting on the old text
    // until the next reconnect. There is no stale cache behind this — the readers read live — it was simply a
    // missing notification, which is what these two members supply.

    // Subscribes each newly-added tab's "I compiled my object" notification. The editors already raise it
    // (CompiledExistingObject); this is the subscriber they never had. Nothing unsubscribes on close because
    // nothing needs to: the delegate lives on the tab's own editor, so tab and handler are collected together.
    private void OnWorkspaceTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // ⚠ Pozycje menu zakładki bramkują się SKŁADEM tej kolekcji, więc każda jej zmiana musi je
        //   przeliczyć — inaczej „Zamknij po prawej" zostaje aktywne po zamknięciu ostatniej zakładki.
        //   Podpięte TUTAJ, w jednym istniejącym punkcie, a nie przy ~39 miejscach dodających zakładkę.
        RaiseTabMenuCanExecuteChanged();

        // ⚠ `Reset` (czyli `Clear()`, m.in. przy rozłączeniu) nie niesie ANI `NewItems`, ANI `OldItems`.
        // Dlatego odpinamy po własnym zbiorze — inaczej zamknięte zakładki dalej zapalałyby rail.
        // ⭐ Od M3b.1 ta sama droga gasi sekcję postępu: zamknięcie zakładki z trwającym importem (albo
        // rozłączenie, które czyści wszystkie) przelicza sekcję i nie zostawia zapalonego paska po
        // operacji, której nośnik już nie istnieje.
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
        {
            foreach (var tab in _activitySources.ToList()) UnwireActivitySource(tab);
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is WorkspaceTabViewModel tab) UnwireActivitySource(tab);
            }
        }

        if (e.NewItems is null)
        {
            // Zakładka zniknęła — odpowiedź railu mogła się zmienić nawet bez nowych źródeł.
            RaiseActivityChanged();
            return;
        }

        foreach (var item in e.NewItems)
        {
            if (item is not WorkspaceTabViewModel tab) continue;
            WireObjectCompiled(tab);
            WireActivitySource(tab);
        }
    }

    // The per-kind reach into the editor that raises the notification — the same shape as
    // WorkspaceTabViewModel.SavableEditor / UnsavedWork, so the three switches stay recognisably one idea.
    // Only the editors that actually raise it appear here; the rest are refresh TARGETS only.
    private void WireObjectCompiled(WorkspaceTabViewModel tab)
    {
        switch (tab.Kind)
        {
            case WorkspaceTabKind.ViewDetail when tab.ViewDetail is { } view:
                view.CompiledExistingObject += () => RefreshOtherTabsForObject(tab);
                break;
            case WorkspaceTabKind.PackageDetail when tab.PackageDetail is { } package:
                package.CompiledExistingObject += () => RefreshOtherTabsForObject(tab);
                break;
            case WorkspaceTabKind.ProcedureDetail when tab.ProcedureDetail is { } proc:
                proc.CompiledExistingObject += () => RefreshOtherTabsForObject(tab);
                break;
            case WorkspaceTabKind.TriggerDetail when tab.TriggerDetail is { } trigger:
                trigger.CompiledExistingObject += () => RefreshOtherTabsForObject(tab);
                break;
            case WorkspaceTabKind.FunctionDetail when tab.FunctionDetail is { } func:
                func.CompiledExistingObject += () => RefreshOtherTabsForObject(tab);
                break;
            case WorkspaceTabKind.Debugger when tab.Debugger is { } debugger:
                // The debugger saves through the same Ddl lane as every editor and reports it the same way —
                // it is not a special case here, just another tab that can compile its object.
                debugger.CompiledExistingObject += () => RefreshOtherTabsForObject(tab);
                break;
        }
    }

    /// <summary>Reloads every OTHER tab showing the same object as <paramref name="source"/>, after that tab
    /// compiled it. Keyed on (kind, name) exactly as <see cref="CloseTabsForObject"/> and the open/focus dedup
    /// in <see cref="OnOpenDdlRequested"/> are, so "the tabs for this object" means one thing everywhere.
    /// <para><b>A tab with unsaved work is left alone.</b> Refreshing reloads from the database and resets the
    /// dirty state, so refreshing a dirty sibling would silently destroy edits the user has not saved — stale
    /// text is a nuisance, discarded work is rule #11. That tab keeps its own edits and its own Save.</para>
    /// <para>A debugger tab is never a refresh target: reloading it would reset the source its session was
    /// built from (and tear down a live session), which is the Draft model's business, not this seam's.</para></summary>
    internal void RefreshOtherTabsForObject(WorkspaceTabViewModel source)
    {
        foreach (var tab in TabsNeedingRefreshAfterCompile(source)) _ = tab.RefreshAsync();
    }

    /// <summary>Which tabs <see cref="RefreshOtherTabsForObject"/> reloads — the decision, separated from the
    /// doing so it can be asserted without a database (a reload against no connection changes nothing
    /// observable, so testing the action alone would prove nothing). See that method for why each exclusion
    /// exists.</summary>
    internal IReadOnlyList<WorkspaceTabViewModel> TabsNeedingRefreshAfterCompile(WorkspaceTabViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.ObjectKind is not { } kind || string.IsNullOrEmpty(source.ObjectName))
        {
            return Array.Empty<WorkspaceTabViewModel>();
        }

        var result = new List<WorkspaceTabViewModel>();
        foreach (var tab in WorkspaceTabs)
        {
            if (ReferenceEquals(tab, source)) continue;
            if (tab.Kind == WorkspaceTabKind.Debugger) continue;
            if (tab.ObjectKind != kind || !string.Equals(tab.ObjectName, source.ObjectName, StringComparison.Ordinal))
            {
                continue;
            }
            if (tab.UnsavedWork is not null) continue; // never overwrite work the user has not saved (#11)

            result.Add(tab);
        }
        return result;
    }

    internal void CloseTabsForObject(MetadataObjectKind kind, string name)
    {
        // Snapshot — CloseTab mutates WorkspaceTabs.
        var doomed = WorkspaceTabs
            .Where(t => t.Kind is WorkspaceTabKind.Ddl
                             or WorkspaceTabKind.TableDetail
                             or WorkspaceTabKind.ViewDetail
                             or WorkspaceTabKind.ProcedureDetail
                             or WorkspaceTabKind.TriggerDetail
                             or WorkspaceTabKind.FunctionDetail
                             or WorkspaceTabKind.GeneratorDetail
                             or WorkspaceTabKind.DomainDetail
                             or WorkspaceTabKind.PackageDetail
                             or WorkspaceTabKind.ExceptionDetail
                             or WorkspaceTabKind.IndexDetail
                        && t.ObjectKind == kind
                        && string.Equals(t.ObjectName, name, StringComparison.Ordinal))
            .ToList();
        foreach (var tab in doomed) CloseTab(tab);
    }

    // ─── Export DDL to .sql ────────────────────────────────────────────────

    /// <summary>
    /// The active tab's object (kind, name) when it is a REAL, existing, DDL-bearing object
    /// — else null. Excludes non-object tabs (Query / Security / Trace / Session / Search /
    /// New Table) and in-progress New objects (a New View/Procedure/… has a placeholder name
    /// but no catalog object yet). This is the single gate for the Export button.
    /// </summary>
    private (MetadataObjectKind Kind, string Name)? ActiveExportableObject
    {
        get
        {
            var tab = SelectedWorkspaceTab;
            if (tab?.ObjectKind is not { } kind || string.IsNullOrEmpty(tab.ObjectName))
                return null;

            var isNew = tab.Kind switch
            {
                WorkspaceTabKind.ViewDetail => tab.ViewDetail?.IsNew ?? false,
                WorkspaceTabKind.ProcedureDetail => tab.ProcedureDetail?.IsNew ?? false,
                WorkspaceTabKind.TriggerDetail => tab.TriggerDetail?.IsNew ?? false,
                WorkspaceTabKind.FunctionDetail => tab.FunctionDetail?.IsNew ?? false,
                WorkspaceTabKind.PackageDetail => tab.PackageDetail?.IsNew ?? false,
                WorkspaceTabKind.DomainDetail => tab.DomainDetail?.IsNew ?? false,
                WorkspaceTabKind.GeneratorDetail => tab.GeneratorDetail?.IsNew ?? false,
                WorkspaceTabKind.ExceptionDetail => tab.ExceptionDetail?.IsNew ?? false,
                _ => false,
            };
            if (isNew) return null;

            var isDdlBearing = tab.Kind is WorkspaceTabKind.Ddl
                or WorkspaceTabKind.TableDetail or WorkspaceTabKind.ViewDetail or WorkspaceTabKind.ProcedureDetail
                or WorkspaceTabKind.TriggerDetail or WorkspaceTabKind.FunctionDetail or WorkspaceTabKind.PackageDetail
                or WorkspaceTabKind.DomainDetail or WorkspaceTabKind.GeneratorDetail or WorkspaceTabKind.ExceptionDetail
                or WorkspaceTabKind.IndexDetail;
            return isDdlBearing ? (kind, tab.ObjectName!) : null;
        }
    }

    /// <summary>Drives the Export toolbar button's visibility/enablement.</summary>
    public bool CanExportDdl => ActiveExportableObject is not null;

    // Export the active object's complete portable DDL to a .sql file. Regenerates from the
    // DB via MetadataExportService (never the cached tab text) so the file is always the
    // complete, comment-inclusive, grant-free script. Written UTF-8 WITHOUT BOM (a BOM breaks
    // isql / IBExpert on the first statement).
    [RelayCommand(CanExecute = nameof(CanExportDdl))]
    private async Task ExportDdlAsync()
    {
        if (ActiveExportableObject is not { } target || SaveFileRequested is not { } requestSave)
            return;

        var obj = new MetadataObject(target.Name, target.Kind);
        var path = await requestSave(new SaveFileRequest(
            UiStrings.ExportDdlDialogTitle, $"{target.Name}.sql", UiStrings.ExportDdlFilterName, ".sql"))
            .ConfigureAwait(true);
        if (string.IsNullOrEmpty(path))
            return; // user cancelled

        string script;
        try
        {
            script = await _metadataExportService.BuildObjectScriptAsync(obj).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
        {
            AddMessage(MessageSeverity.Error, string.Format(CultureInfo.CurrentCulture, UiStrings.ExportDdlFailedFormat, target.Name, ex.Message));
            SelectedBottomTabIndex = 1;
            return;
        }

        try
        {
            await SqlFileWriter.WriteAsync(path, script).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            AddMessage(MessageSeverity.Error, string.Format(CultureInfo.CurrentCulture, UiStrings.ExportDdlFailedFormat, target.Name, ex.Message));
            SelectedBottomTabIndex = 1;
            return;
        }

        AddMessage(MessageSeverity.Info, string.Format(CultureInfo.CurrentCulture, UiStrings.ExportDdlSucceededFormat, target.Name, path));
    }

    // ── Data export (Export Framework, Etap 3) ────────────────────────────────
    // The SQL results grid's Export entry points (banner "Export all…", results toolbar icon,
    // right-click "Export…") all open the shared Export dialog with an AllRows default scope
    // ("completeness"); the dialog lets the user switch to the visible-rows view.
    public bool CanExportResults => HasCurrentResult;

    [RelayCommand(CanExecute = nameof(CanExportResults))]
    private Task ExportResultsAsync() => OpenExportDialogAsync(ExportScope.AllRows);

    // Builds the SQL-results data source from current state: the filtered/sorted view (CurrentView),
    // the complete materialized rows, the truncated flag, and — for a truncated preview — a Full
    // streaming re-fetch of the same query so "All rows" is complete without materializing it here.
    internal QueryResultExportSource? BuildResultsExportSource()
    {
        if (CurrentResult is not { HasResultSet: true } r) return null;

        var currentView = _sortedRows.ToList();       // stable snapshot of the filtered + sorted view
        // The grid holds an incomplete set when the preview was truncated OR a Full run hit the safety
        // ceiling — in both cases "All rows" must re-fetch the whole query (streaming past the ceiling).
        var isPartial = r.Truncated || r.CeilingHit;
        Func<CancellationToken, IAsyncEnumerable<object?[]>>? streamAll = null;
        if (_lastResultSql is { } sql)
        {
            var parameters = _lastResultParameters;
            streamAll = ct => _executor.StreamAsync(
                Request(sql, ExecutionIntent.Full, parameters), ct);
        }

        return new QueryResultExportSource(
            r.Columns, currentView, r.Rows, isPartial, streamAll, UiStrings.ExportDefaultFileName);
    }

    private async Task OpenExportDialogAsync(ExportScope defaultScope)
    {
        if (BuildResultsExportSource() is not { } source || ExportRequested is not { } raise) return;

        var outcome = await raise(new ExportDialogRequest(source, defaultScope)).ConfigureAwait(true);
        if (outcome is null) return; // cancelled

        var message = outcome.Format == ExportFormat.Clipboard
            ? string.Format(CultureInfo.CurrentCulture, UiStrings.ExportCopiedFormat, outcome.RowCount)
            : string.Format(CultureInfo.CurrentCulture, UiStrings.ExportSavedFormat, outcome.RowCount, outcome.FilePath);
        AddMessage(MessageSeverity.Info, message);
    }

    // F5 / Ctrl+Enter — the single Execute. The lane is chosen automatically from the
    // SQL: data statements (SELECT/INSERT/UPDATE/DELETE/MERGE/EXECUTE …) run on the
    // Data lane (connection #1, data profile); DDL/DCL (CREATE/ALTER/DROP/COMMENT/
    // GRANT/…) on the Metadata lane (connection #2, metadata profile). Ambiguous input
    // falls back to Data — the safest lane. There is no manual lane override by design.
    // ─── Smart SQL parameters (Part 3) ────────────────────────────────────────
    /// <summary>The view shows the reused Execute dialog and returns the ordered bound values
    /// (null on Cancel). VM stays free of Avalonia dialog types.</summary>
    public event Func<SmartParametersRequest, Task<IReadOnlyList<object?>?>>? SmartParametersRequested;

    /// <summary>Raised when the user creates a new query (the Saved Queries "+" / New query) so the
    /// view can move keyboard focus straight into the SQL editor — the user can type immediately
    /// without an extra click. The view marshals + focuses; the VM stays free of Avalonia types.</summary>
    public event Action? EditorFocusRequested;

    // Types each scanned parameter from the catalog: the input parameter whose ARGUMENT SLOT the placeholder
    // fills. Anything not provably a whole argument stays "Unknown" — never a guessed type (the user's rule).
    //
    // ⚠⚠ TWO DEFECTS LIVED HERE, AND THE SECOND IS WHY THE FIRST FIX CHANGED NOTHING THE USER COULD SEE
    // (report 2026-08-03, re-opened after that fix):
    //
    //   1. It required `catalog.Count == names.Count`. A placeholder is not an input parameter, so counting them
    //      is not the question — a call that omits parameters carrying DEFAULTS, repeats a placeholder, or uses
    //      `RETURNING_VALUES :r` breaks the equality while being perfectly typeable, and lost EVERY type at once.
    //
    //   2. ⭐⭐ It enumerated STATEMENT SHAPES. First `EXECUTE PROCEDURE` only; then also a `SELECT` whose FROM
    //      held a call. Each version left the next syntax silently unhandled — a PSQL `FOR SELECT … FROM P(…)
    //      INTO …`, an `INSERT … SELECT … FROM P(…)`, a CTE body, `MERGE … USING P(…)`, a call in any subquery —
    //      and the user reported the same defect three times, each time on a shape the last fix had not listed.
    //
    // ⭐⭐ THE FIX IS THAT THE MODEL NOW ANSWERS THE QUESTION. `IRoutineInvocation` makes "a routine is invoked
    // here, with these argument spans" a fact the AST carries, so this method asks the TREE
    // (`SqlParameterScanner.MapNamesToArgumentSlots`) and knows nothing about statements at all. Every shape
    // above — and every shape added later — is reached by the same walk. ⛔ If a call is ever not found, the
    // parser is not modelling it; fix it there, never with a branch here.
    //
    // ⚠ Typing is resolved PER PLACEHOLDER, because one statement can have several type sources: two selectable
    // procedures joined, a call inside a subquery, or a table being written to. Each source names its own owner,
    // so the catalog is read per owner and cached for the statement.
    private async Task<IReadOnlyList<(string Name, string TypeText)>> BuildSmartParamSpecsAsync(
        string sql, IReadOnlyList<string> names)
    {
        var sources = SqlParameterScanner.ResolveTypeSources(sql, names);
        var cache = new MetadataTypeCache();
        var specs = new List<(string Name, string TypeText)>(names.Count);

        for (int i = 0; i < names.Count; i++)
        {
            specs.Add((names[i], await TypeForSourceAsync(sources[i], cache).ConfigureAwait(true)));
        }

        return specs;
    }

    // Per-statement memo of what was read, so one owner costs one catalog round trip however many placeholders
    // point at it — and an owner that cannot be read is remembered as unreadable rather than retried per value.
    private sealed class MetadataTypeCache
    {
        public Dictionary<string, IReadOnlyList<ProcedureParameterInfo>> Routines { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, IReadOnlyList<FieldInfo>> Tables { get; } = new(StringComparer.Ordinal);
    }

    // The declared type the MODEL proved this placeholder has, or Unknown.
    //
    // ⭐ Switches on the KIND OF SOURCE, never on the kind of statement — that is the whole point of
    // ParameterTypeSource. Two sources exist because a type has two provable origins in Firebird DML (a routine's
    // input parameter, a table's column); a third would arrive as a new AST fact plus one arm here, not as a
    // statement branch anywhere.
    private async Task<string> TypeForSourceAsync(
        SqlParameterScanner.ParameterTypeSource source, MetadataTypeCache cache)
    {
        if (!source.IsResolved) return UiStrings.SmartParamUnknownType;
        var owner = source.Owner!;

        switch (source.Kind)
        {
            case SqlParameterScanner.TypeSourceKind.RoutineParameter:
            {
                if (!cache.Routines.TryGetValue(owner, out var parameters))
                {
                    parameters = await ReadOrEmptyAsync(
                        () => _tableDetailReader.GetProcedureParametersAsync(owner, 0, CancellationToken.None))
                        .ConfigureAwait(true);
                    cache.Routines[owner] = parameters;
                }

                // A slot the routine does not have is Unknown — a call with more arguments than the routine has
                // parameters is the user's error to see from Firebird, not ours to paper over with the wrong one.
                return source.Slot < parameters.Count
                    ? parameters[source.Slot].Type
                    : UiStrings.SmartParamUnknownType;
            }

            case SqlParameterScanner.TypeSourceKind.TableColumn:
            {
                if (!cache.Tables.TryGetValue(owner, out var columns))
                {
                    columns = await ReadOrEmptyAsync(
                        () => _tableDetailReader.GetFieldsAsync(owner, CancellationToken.None))
                        .ConfigureAwait(true);
                    cache.Tables[owner] = columns;
                }

                var column = columns.FirstOrDefault(
                    c => string.Equals(c.Name, source.ColumnName, StringComparison.OrdinalIgnoreCase));
                return column?.Type ?? UiStrings.SmartParamUnknownType;
            }

            default:
                return UiStrings.SmartParamUnknownType;
        }
    }

    // A metadata read whose failure means "no type", never an exception into the execute path (rule: never guess).
    private static async Task<IReadOnlyList<T>> ReadOrEmptyAsync<T>(Func<Task<IReadOnlyList<T>>> read)
    {
        try
        {
            return await read().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is MetadataReadException or InvalidOperationException)
        {
            return Array.Empty<T>();
        }
    }

    internal static IReadOnlyList<QueryParameter> BuildQueryParameters(
        IReadOnlyList<string> names, IReadOnlyList<object?> values)
    {
        var list = new List<QueryParameter>(names.Count);
        for (int i = 0; i < names.Count && i < values.Count; i++)
        {
            list.Add(new QueryParameter("@" + names[i], values[i]));
        }
        return list;
    }

    // Deterministic per-statement history key (FNV-1a over the trimmed SQL) so re-running the same
    // ad-hoc query recalls its last values across sessions (string.GetHashCode isn't stable in .NET Core).
    internal static string SmartParamsHistoryKey(string sql)
    {
        const ulong offset = 14695981039346656037UL, prime = 1099511628211UL;
        ulong hash = offset;
        foreach (var ch in (sql ?? string.Empty).Trim()) { hash ^= ch; hash *= prime; }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The command for a GLOBAL-scope <see cref="CommandId"/> — the ones that mean the same thing wherever
    /// the user is. Tab-scoped ids are the selected tab's business and are resolved by
    /// <see cref="WorkspaceTabViewModel.ResolveCommand"/>; ids backed by a view action (focusing the sidebar
    /// filter, opening an editor's Find bar) have no view-model command and are dispatched by the router.
    ///
    /// <para>⚠ This is where the old <c>GoCommand</c> went. It interpreted F5 itself and ended every route
    /// with "anything else → Execute Query", which is how F5 came to run the SQL editor's text from tabs
    /// that have nothing to execute. "Which tab" is now a declared scope, so no command has to guess.</para>
    /// </summary>
    internal ICommand? ResolveCommand(CommandId id) => id switch
    {
        CommandId.GlobalSearch => OpenGlobalSearchCommand,

        // F6 / Shift+F6 — the SAME commands the toolbar's Commit / Rollback buttons bind, and gated by the
        // same CanCommitAll / CanRollbackAll, so the key can never settle a transaction the button refuses.
        CommandId.Commit => CommitAllCommand,
        CommandId.Rollback => RollbackAllCommand,

        // Ctrl+W — the confirming close, so a tab with unsaved work still offers Save / Discard / Cancel.
        CommandId.CloseTab => CloseActiveTabCommand,

        // F3 / F8 inside a grid, through the application's EXISTING unified collection router. Its
        // ActiveCollection() already answers "which collection is the user editing" and returns null when
        // there is none — which is why these need no per-grid knowledge and no tab-kind list.
        CommandId.CollectionAdd => AddCollectionItemCommand,
        CommandId.CollectionEdit => EditCollectionItemCommand,
        CommandId.CollectionRemove => RemoveCollectionItemCommand,

        // CommandScope.Tree belongs to the Object Explorer, which owns the tree's selection.
        CommandId.NewObject or CommandId.DeleteObject or CommandId.RefreshMetadata
            => Metadata.ResolveCommand(id),

        _ => null,
    };

    [RelayCommand(CanExecute = nameof(CanExecute))]
    public Task ExecuteQueryAsync() => RunExecuteAsync(ExecutionIntent.Preview);

    // Shift+F5 — direct Full from a cold start (a quiet power path; the primary, intended route
    // to a full read remains the truncated-Preview notice bar's [Load all rows]).
    [RelayCommand(CanExecute = nameof(CanExecute))]
    public Task ExecuteQueryFullAsync() => RunExecuteAsync(ExecutionIntent.Full);

    private async Task RunExecuteAsync(ExecutionIntent intent)
    {
        // If the user has highlighted a fragment in the editor, execute only that;
        // otherwise execute the whole editor content (legacy behaviour).
        var sql = ResolveActiveSql();
        if (IsExecuting || string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        // The SQL Editor is a classic SQL console: EVERY statement — SELECT, DML, DDL, GRANT —
        // runs on the ONE data attachment inside the ONE user transaction, NOWAIT, settled by the
        // single Commit / Rollback. No routing by statement kind, no hidden second transaction.
        // The classifier survives only as a refresh HINT (does this transaction change the schema,
        // so should the metadata tree be reloaded on Commit?) — it no longer steers execution.
        var changesSchema = SqlStatementClassifier.Classify(sql) == SqlStatementCategory.Schema;

        if (!_service.IsConnected)
        {
            AddMessage(MessageSeverity.Error, UiStrings.NoConnectionMessage);
            SelectedBottomTabIndex = 1;
            return;
        }

        // Smart SQL parameters (Part 3): if the statement has :name / @name placeholders (and is
        // NOT an EXECUTE BLOCK, whose :vars are locals), collect their values first via the reused
        // Execute dialog, then run the rewritten (@name) SQL with bound parameters. Cancelling the
        // dialog aborts the run.
        var executeSql = sql;
        IReadOnlyList<QueryParameter>? parameters = null;
        if (SmartParametersRequested is { } askParams && !SqlParameterScanner.IsExecuteBlock(sql))
        {
            var (rewritten, names) = SqlParameterScanner.RewriteToDriverMarkers(sql);
            if (names.Count > 0)
            {
                var specs = await BuildSmartParamSpecsAsync(sql, names).ConfigureAwait(true);
                var values = await askParams(new SmartParametersRequest(specs, SmartParamsHistoryKey(sql))).ConfigureAwait(true);
                if (values is null) return; // user cancelled the parameter dialog
                parameters = BuildQueryParameters(names, values);
                executeSql = rewritten;
            }
        }

        IsExecuting = true;
        // ⭐ M3.1f: „Executing…" to POSTĘP i mieszka w sekcji 4 (ustawia je `OnIsExecutingChanged`).
        // Tutaj CZYŚCIMY wynik poprzedniego wykonania — zostawiony wisiałby jako „143 rows in 46 ms"
        // obok paska mówiącego, że trwa coś innego, czyli kłamałby o bieżącej chwili.
        QueryStatsText = string.Empty;
        ClearError();
        _executionCts = new CancellationTokenSource();

        try
        {
            QueryResult result;
            IReadOnlyList<PerTableReadRow>? reads = null;
            {
                // Full reports streamed progress (live "Loading… N rows" counter) + the soft-threshold
                // "keep loading?" prompt; Preview needs neither.
                var progress = intent == ExecutionIntent.Full ? MakeLoadProgress() : null;
                var onSoft = intent == ExecutionIntent.Full ? MakeSoftThresholdCallback() : null;
                (result, reads) = await ExecuteWithMetricsAsync(
                    Request(executeSql, intent, parameters),
                    progress, onSoft, _executionCts.Token).ConfigureAwait(true);
                // A DDL/DCL statement in this transaction means the schema changes on Commit — so
                // the metadata tree must be reloaded then (uncommitted DDL is deliberately NOT
                // visible to the read-only metadata attachment: classic console semantics).
                if (changesSchema) _transactionChangedSchema = true;
                // Remember this statement so the notice bar's [Load all rows] can re-run it as Full.
                _lastResultSql = executeSql;
                _lastResultParameters = parameters;
                // Performance panel (Option B): remember THIS SQL Editor run so its own panel can
                // analyze it on view — no re-execution, and no leaking into proc/func contexts.
                SqlEditorPerformance.Record(executeSql, result, reads);
            }

            CurrentResult = result;
            CurrentResultVersionTag = Guid.NewGuid().ToString("N");
            // These rows are a different result now: the cached provenance describes the PREVIOUS
            // statement, and reusing it would resolve this grid against the wrong table.
            ResetSqlCopy();

            var ms = (long)result.Elapsed.TotalMilliseconds;
            if (result.HasResultSet)
            {
                QueryStatsText = string.Format(UiStrings.RowsFetchedFormat, result.Rows.Count, ms);
                AddMessage(MessageSeverity.Info, QueryStatsText);
                if (result.Truncated || result.CeilingHit)
                {
                    AddMessage(MessageSeverity.Warning, ResultsNoticeText);
                }
                // Keep the Performance tab in view when the user is watching it (so the
                // panel updates and the next run re-captures reads); otherwise show Results.
                if (!_performanceTabActive)
                {
                    SelectedBottomTabIndex = 0;
                }
            }
            else
            {
                // IBExpert-style work summary: inserted/updated/deleted from the MON$ delta
                // (works for EXECUTE PROCEDURE/BLOCK/DML), falling back to the driver's total.
                // Status bar stays the concise aggregate; the Messages entry adds the SAME
                // per-table breakdown the Procedure/Function panels show (one shared model —
                // ExecutionActivity), so the detail level is consistent wherever a run is launched.
                QueryStatsText = BuildExecutionSummary(result, reads).BuildMessage();
                var perTable = ExecutionActivity.BuildLogLines(reads);
                var messageText = perTable.Count > 0
                    ? QueryStatsText + "\n" + string.Join("\n", perTable)
                    : QueryStatsText;
                AddMessage(MessageSeverity.Info, messageText);
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

    /// <summary>
    /// Cancels the running statement. Cancelling the CTS is only half the job: it aborts the
    /// awaiting task but cannot interrupt a statement Firebird is still executing — the executor
    /// registers <c>FbCommand.Cancel()</c> (fb_cancel_operation) on this token, which is what
    /// actually stops the server. We also latch <see cref="IsCancelling"/> so the button reports
    /// that the click landed instead of looking inert (which is why it felt like it needed
    /// several clicks — the extra clicks were no-ops on an already-cancelled CTS).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelQuery))]
    private void CancelQuery()
    {
        if (_executionCts is not { IsCancellationRequested: false } cts) return;
        IsCancelling = true;
        // ⚠ Nie `Progress.Report` wprost — od M3b.1 sekcję zapisuje wyłącznie `UpdateProgressSection`.
        _queryProgressDetail = UiStrings.CancellingStatus;
        UpdateProgressSection();
        cts.Cancel();
    }

    // Swap the truncated preview for the full result WITHOUT resetting the client-side view
    // state (filter/sort/aggregation) — the columns are identical and it's the same query.
    // Internal so the preserve-view-state behaviour is unit-testable without a live executor.
    internal void ApplyFullResult(QueryResult result)
    {
        _preserveViewStateOnNextResult = true;
        CurrentResult = result;
        CurrentResultVersionTag = Guid.NewGuid().ToString("N");
    }

    // A Full load's live "Loading… N rows" counter. Created on the UI thread → Progress<T>
    // marshals its callbacks back here, so QueryStatsText is written on the UI thread.
    // ⭐ Od M3.1f licznik ładowania idzie do SEKCJI POSTĘPU, nie do `QueryStatsText`. To trzecie
    // zastosowanie tego samego podziału (§19.5.1, §19.6.1): „Loading… N rows" to POSTĘP, więc należy do
    // powierzchni globalnej; „143 rows in 46 ms" to WYNIK i zostaje przy edytorze SQL.
    // ⚠ Tryb pozostaje NIEOKREŚLONY, bo strumieniowy odczyt nie zna sumy wierszy — nie ma z czego
    // policzyć procentu i udawanie go byłoby zmyślaniem (§19.7.2).
    //
    // ⚠ Od M3b.1 licznik NAZYWA swoją operację (`StatusProgressQueryRowsFormat`, nie
    // `ResultsLoadingFormat`): sekcja ma trzy źródła, więc samo „Loading… 12 345 rows" nie mówi, czy to
    // zapytanie, skrypt, czy import. ⚠ I nie zapisuje sekcji wprost — odkłada szczegół i prosi
    // `UpdateProgressSection` o przeliczenie, żeby pisarz sekcji pozostał jeden.
    private IProgress<long> MakeLoadProgress()
        => new Progress<long>(n =>
        {
            _queryProgressDetail = string.Format(
                CultureInfo.CurrentCulture, UiStrings.StatusProgressQueryRowsFormat, n);
            UpdateProgressSection();
        });

    // Soft-threshold choice ids (returned by the "keep loading?" dialog; wording can change freely).
    internal const string LoadAllKeepChoiceId = "keep";
    internal const string LoadAllStopChoiceId = "stop";

    // Pure decision (Etap 2): ONLY an explicit "Keep loading" continues the Full read. "Stop here",
    // Esc, or dismissing the dialog → stop and keep the partial (the safe, non-destructive default).
    internal static bool ShouldKeepLoading(string? choiceId) => choiceId == LoadAllKeepChoiceId;

    // The Full soft-threshold callback: invoked by the streaming executor (possibly off the UI thread)
    // once SoftThreshold rows are read and more remain. Marshals the modal "keep loading?" prompt onto
    // the UI thread via a TaskCompletionSource so the executor can await the user's decision regardless
    // of which thread its read loop is running on.
    private Func<long, Task<bool>> MakeSoftThresholdCallback()
        => loaded =>
        {
            var tcs = new TaskCompletionSource<bool>();
            Dispatcher.UIThread.Post(async () =>
            {
                try { tcs.SetResult(await PromptKeepLoadingAsync(loaded).ConfigureAwait(true)); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        };

    private async Task<bool> PromptKeepLoadingAsync(long loaded)
    {
        var id = await RequestChoiceAsync(new ChoiceRequest
        {
            Title = UiStrings.LoadAllThresholdTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.LoadAllThresholdMessageFormat, loaded),
            Options = new[]
            {
                new ChoiceOption { Id = LoadAllKeepChoiceId, Label = UiStrings.LoadAllThresholdKeep, IsDefault = true },
                new ChoiceOption { Id = LoadAllStopChoiceId, Label = UiStrings.LoadAllThresholdStop, IsCancel = true },
            },
        }).ConfigureAwait(true);
        return ShouldKeepLoading(id);
    }

    public bool CanLoadAllRows =>
        !IsExecuting && _service.IsConnected && _lastResultSql is not null
        && CurrentResult is { Truncated: true };

    // Truncated-Preview bar action: re-run the SAME statement that produced the current preview
    // as Full, streaming the whole result into the grid (progress counter + Cancel), guarded by
    // the hard FullSafetyCeiling. Client-side view state (filter/sort/aggregation) is preserved.
    [RelayCommand(CanExecute = nameof(CanLoadAllRows))]
    private async Task LoadAllRows()
    {
        if (_lastResultSql is null || IsExecuting) return;
        if (!_service.IsConnected)
        {
            AddMessage(MessageSeverity.Error, UiStrings.NoConnectionMessage);
            SelectedBottomTabIndex = 1;
            return;
        }

        IsExecuting = true;
        ClearError();
        _executionCts = new CancellationTokenSource();
        QueryStatsText = string.Empty;   // wynik poprzedniego wykonania — patrz komentarz w ExecuteQueryAsync
        try
        {
            var (result, reads) = await ExecuteWithMetricsAsync(
                Request(_lastResultSql, ExecutionIntent.Full, _lastResultParameters),
                MakeLoadProgress(), MakeSoftThresholdCallback(), _executionCts.Token).ConfigureAwait(true);
            SqlEditorPerformance.Record(_lastResultSql, result, reads);

            ApplyFullResult(result); // replaces the preview, keeping the client-side view state

            var ms = (long)result.Elapsed.TotalMilliseconds;
            QueryStatsText = string.Format(UiStrings.RowsFetchedFormat, result.Rows.Count, ms);
            AddMessage(MessageSeverity.Info, QueryStatsText);
            if (result.CeilingHit)
            {
                AddMessage(MessageSeverity.Warning, ResultsNoticeText);
            }
            // Aggregation lines were computed over the preview; recompute over the full set.
            await ResultAggregationBar.RecomputeAllAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Leave CurrentResult (the preview) untouched — the notice bar reappears so the
            // user can retry. No half-loaded state.
            QueryStatsText = UiStrings.QueryCancelledMessage;
            AddMessage(MessageSeverity.Warning, UiStrings.QueryCancelledMessage);
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

    // BuildExecutedViaMessage lived here: it logged "Executed via Data/Metadata profile (…)" on
    // every F5, because the auto-router silently chose an attachment and transaction and the user
    // would otherwise have had no way to know which. There is one attachment and one transaction
    // now, so there is nothing to disclose — the message is gone with the routing that needed it.

    [RelayCommand(CanExecute = nameof(CanFormatSql))]
    private void FormatSql()
    {
        if (!IsQueryTabActive) return;
        var selected = SelectedQueryTextProvider?.Invoke();
        var hasSelection = !string.IsNullOrEmpty(selected);
        var source = hasSelection ? selected! : QueryText;
        if (string.IsNullOrEmpty(source)) return;

        var formatted = SqlFormatter.Format(source, FormatterStyle);
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
    /// User-initiated tab close. Prompts before discarding ANY tab that reports unsaved work — a New Table
    /// form, an uncompiled new view / procedure, a modified-but-not-compiled source, a table designer with
    /// queued structural changes, or (since Seam 5c) an edited routine in a debugger tab. Clean tabs (and
    /// DDL / read-only tabs, reopenable from the tree) close silently. Programmatic closes (post-compile,
    /// delete-table cleanup) call <see cref="CloseTab"/> directly and never prompt.
    /// <para>
    /// The prompt is three-way — <b>Save / Discard / Cancel</b> — whenever the tab has somewhere to save,
    /// matching disconnect and app-close instead of forcing "discard or stay". Save routes through the
    /// editor's own <see cref="ISavableObjectEditor.SaveAsync"/> (the one save path); a failed save keeps
    /// the tab open and selected, with the error already on its own surface. A tab that reports unsaved
    /// work but cannot save it still gets the honest Discard / Cancel pair.
    /// </para>
    /// </summary>
    public async Task RequestCloseTabAsync(WorkspaceTabViewModel tab)
    {
        if (tab.UnsavedWork is { } work)
        {
            var message = string.Format(
                CultureInfo.CurrentCulture, UiStrings.CloseTabUnsavedConfirmFormat, work.Label);

            if (tab.SavableEditor is { } editor)
            {
                var id = await RequestChoiceAsync(new ChoiceRequest
                {
                    Title = UiStrings.CloseTabUnsavedConfirmTitle,
                    Message = message,
                    Options = new[]
                    {
                        new ChoiceOption { Id = "save", Label = UiStrings.CloseTabUnsavedSave, IsDefault = true },
                        new ChoiceOption
                        {
                            Id = "discard", Label = UiStrings.CloseTabUnsavedConfirmYes, IsDestructive = true,
                        },
                        new ChoiceOption { Id = "cancel", Label = UiStrings.DialogCancel, IsCancel = true },
                    },
                }).ConfigureAwait(true);

                if (id is null or "cancel") return;
                if (id == "save")
                {
                    var result = await editor.SaveAsync().ConfigureAwait(true);

                    // Proceed only if the work is genuinely GONE, not merely if the save reported success.
                    // This is the one place where trusting a wrong "success" would silently destroy the
                    // user's code, so it is verified against the same UnsavedWork the prompt was built from
                    // rather than taken on faith from eleven separate editor adapters.
                    if (!result.Success || tab.UnsavedWork is not null)
                    {
                        // Stay open on the offending tab — the editor has already surfaced the reason.
                        SelectTab(tab);
                        return;
                    }
                }
            }
            else
            {
                var confirmed = await RequestConfirmAsync(new ConfirmRequest
                {
                    Title = UiStrings.CloseTabUnsavedConfirmTitle,
                    Message = message,
                    ConfirmLabel = UiStrings.CloseTabUnsavedConfirmYes,
                    CancelLabel = UiStrings.DialogCancel,
                    IsDestructive = true,
                }).ConfigureAwait(true);
                if (!confirmed) return;
            }
        }
        CloseTab(tab);
    }

    // ─── Menu kontekstowe zakładki — zamykanie masowe (M3.3c / §8.3) ───────────────────────
    //
    // ⚠⚠ REGUŁA #11 OBOWIĄZUJE BEZWZGLĘDNIE, I TO JEST TA METODA. „Zamknij wszystkie" nie może po
    // cichu odrzucić nieskompilowanej pracy w ośmiu edytorach, więc CZTERY pozycje menu schodzą się
    // tutaj i przechodzą przez ISTNIEJĄCĄ bramkę Save / Discard / Cancel — czwarte jej wejście,
    // obok zamknięcia zakładki, rozłączenia i zamknięcia aplikacji.
    //
    // ⭐ Bramka jest AGREGUJĄCA, a nie „N pytań po kolei", i tak być musi: pytanie zadane osiem razy
    // pod rząd nie jest bramką, tylko przeszkodą, którą użytkownik przeklika bez czytania. Ten sam
    // kształt ma rozłączenie i zamknięcie aplikacji — jedno okno wymienia wszystkie zakładki z pracą.
    //
    // ⚠ ZASIĘG JEST ARGUMENTEM, nie założeniem: „Zamknij po prawej" dotyczy podzbioru, więc bramka
    // pyta wyłącznie o pracę w zakładkach, KTÓRE FAKTYCZNIE ZOSTANĄ ZAMKNIĘTE. Pytanie o cudzą pracę
    // byłoby fałszywe, a pominięcie własnej — utratą danych.
    //
    // ⚠ Po „Zapisz" nie ufamy zgłoszonemu sukcesowi: `SaveDirtyEditorsAsync` zwraca false, gdy
    // którykolwiek edytor nie zapisał, i sam zaznacza winną zakładkę z jej błędem. Wtedy nie
    // zamykamy NICZEGO — częściowe zamknięcie po nieudanym zapisie byłoby najgorszym wynikiem.
    private async Task CloseTabsAsync(IReadOnlyList<WorkspaceTabViewModel> targets, string messageFormat)
    {
        var closable = targets.Where(t => t.IsClosable).ToArray();
        if (closable.Length == 0) return;

        var unsaved = CollectUnsavedWork(closable);
        if (unsaved.Count > 0)
        {
            var message = string.Format(
                CultureInfo.CurrentCulture,
                messageFormat,
                string.Join(Environment.NewLine, unsaved.Select(u => "• " + u.Label)));

            var options = new List<ChoiceOption>();
            if (HasSavableDirtyEditors(closable))
                options.Add(new ChoiceOption
                {
                    Id = "save", Label = UiStrings.TabsCloseUnsavedSave, IsDefault = true,
                });
            options.Add(new ChoiceOption
            {
                Id = "discard", Label = UiStrings.TabsCloseUnsavedDiscard, IsDestructive = true,
            });
            options.Add(new ChoiceOption
            {
                Id = "cancel", Label = UiStrings.DialogCancel, IsCancel = true, IsDefault = options.Count == 1,
            });

            var id = await RequestChoiceAsync(new ChoiceRequest
            {
                Title = UiStrings.TabsCloseUnsavedTitle,
                Message = message,
                Options = options,
            }).ConfigureAwait(true);

            if (id is null or "cancel") return;
            if (id == "save" && !await SaveDirtyEditorsAsync(closable).ConfigureAwait(true)) return;
        }

        // ⚠ Zamykamy po ROZSTRZYGNIĘCIU bramki, więc `CloseTab` (bezpytaniowe), a nie
        //   `RequestCloseTabAsync` — inaczej użytkownik dostałby drugie pytanie o to samo.
        foreach (var tab in closable) CloseTab(tab);
    }

    /// <summary>Zakładki, których dotyczy „Zamknij pozostałe" — wszystkie zamykalne poza wskazaną.</summary>
    private IReadOnlyList<WorkspaceTabViewModel> TabsOtherThan(WorkspaceTabViewModel keep)
        => WorkspaceTabs.Where(t => !ReferenceEquals(t, keep)).ToArray();

    /// <summary>
    /// Zakładki na PRAWO od wskazanej. ⚠ „Na prawo" znaczy dalej w kolekcji, i to jest prawdziwe
    /// w obu trybach paska: w wielowierszowym „dalej" to kolejny wiersz, co czyta się tak samo.
    /// </summary>
    private IReadOnlyList<WorkspaceTabViewModel> TabsRightOf(WorkspaceTabViewModel anchor)
    {
        var index = WorkspaceTabs.IndexOf(anchor);
        return index < 0
            ? Array.Empty<WorkspaceTabViewModel>()
            : WorkspaceTabs.Skip(index + 1).ToArray();
    }

    /// <summary>
    /// Zakładki bez niezapisanej pracy. ⭐ Ta jedna pozycja menu NIE POTRZEBUJE bramki — z definicji
    /// nie ma czego stracić — i właśnie dlatego jest w menu: daje sposób na uprzątnięcie paska bez
    /// odpowiadania na jakiekolwiek pytanie.
    /// </summary>
    private IReadOnlyList<WorkspaceTabViewModel> UnmodifiedTabs()
        => WorkspaceTabs.Where(t => t.UnsavedWork is null).ToArray();

    // ─── Menu kontekstowe zakładki — komendy (M3.3c / §8.3) ────────────────────────────────
    //
    // ⚠ Każda bierze zakładkę PARAMETREM, a nie z `SelectedWorkspaceTab`. Menu kontekstowe otwiera się
    // nad zakładką, która NIE MUSI być aktywna — czytanie zaznaczenia zamykałoby cudzy dokument, i to
    // jest dokładnie ten defekt, przed którym broni gotcha #16/#99 przy siatkach.

    // ⭐⭐ KAŻDA POZYCJA MA WŁASNE `CanExecute` — żadna nie jest ani martwa, ani zawsze aktywna.
    // Menu, w którym „Zamknij po prawej" jest klikalne na ostatniej zakładce, uczy, że polecenie nie
    // działa; menu, w którym „Odśwież" stoi na zakładce SQL Editora, uczy tego samego. ⚠ Predykaty
    // pytają o STAN, na który patrzy użytkownik otwierając menu, a nie o zaznaczenie.

    private static bool CanTabMenuClose(WorkspaceTabViewModel? tab) => tab is { IsClosable: true };

    [RelayCommand(CanExecute = nameof(CanTabMenuClose))]
    private Task TabMenuCloseAsync(WorkspaceTabViewModel? tab)
        => tab is null ? Task.CompletedTask : RequestCloseTabAsync(tab);

    private bool CanTabMenuCloseOthers(WorkspaceTabViewModel? tab)
        => tab is not null && TabsOtherThan(tab).Any(t => t.IsClosable);

    [RelayCommand(CanExecute = nameof(CanTabMenuCloseOthers))]
    private Task TabMenuCloseOthersAsync(WorkspaceTabViewModel? tab)
        => tab is null
            ? Task.CompletedTask
            : CloseTabsAsync(TabsOtherThan(tab), UiStrings.TabsCloseUnsavedFormat);

    private bool CanTabMenuCloseAll() => WorkspaceTabs.Any(t => t.IsClosable);

    [RelayCommand(CanExecute = nameof(CanTabMenuCloseAll))]
    private Task TabMenuCloseAllAsync()
        => CloseTabsAsync(WorkspaceTabs.ToArray(), UiStrings.TabsCloseUnsavedFormat);

    /// <summary>⚠ Wyłączone na OSTATNIEJ zakładce — nie ma czego zamknąć po prawej.</summary>
    private bool CanTabMenuCloseToTheRight(WorkspaceTabViewModel? tab)
        => tab is not null && TabsRightOf(tab).Any(t => t.IsClosable);

    [RelayCommand(CanExecute = nameof(CanTabMenuCloseToTheRight))]
    private Task TabMenuCloseToTheRightAsync(WorkspaceTabViewModel? tab)
        => tab is null
            ? Task.CompletedTask
            : CloseTabsAsync(TabsRightOf(tab), UiStrings.TabsCloseUnsavedFormat);

    /// <summary>⚠ Wyłączone, gdy nie ma ani jednej zamykalnej niezmodyfikowanej zakładki.</summary>
    private bool CanTabMenuCloseUnmodified() => UnmodifiedTabs().Any(t => t.IsClosable);

    /// <summary>⭐ Nie przechodzi przez bramkę, bo z definicji nie ma czego stracić — patrz
    /// <see cref="UnmodifiedTabs"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanTabMenuCloseUnmodified))]
    private Task TabMenuCloseUnmodifiedAsync()
        => CloseTabsAsync(UnmodifiedTabs(), UiStrings.TabsCloseUnsavedFormat);

    /// <summary>⚠ Tylko rodzaje, które NAPRAWDĘ się odświeżają (<c>WorkspaceTabViewModel.CanRefresh</c> —
    /// piąty członek tej samej rodziny per-kind), i tylko gdy nie ma niezapisanej pracy.</summary>
    private static bool CanTabMenuRefresh(WorkspaceTabViewModel? tab)
        => tab is { CanRefresh: true, UnsavedWork: null };

    /// <summary>Przeładowuje zakładkę z bazy. ⚠ Zakładka z niezapisaną pracą NIE jest odświeżana —
    /// `RefreshAsync` przeładowuje źródło i czyści dirty, więc odświeżenie brudnej zakładki
    /// zniszczyłoby edycję (reguła #11, to samo wykluczenie co w Seam 6d).</summary>
    [RelayCommand(CanExecute = nameof(CanTabMenuRefresh))]
    private async Task TabMenuRefreshAsync(WorkspaceTabViewModel? tab)
    {
        if (tab is null || tab.UnsavedWork is not null) return;
        await tab.RefreshAsync().ConfigureAwait(true);
    }

    /// <summary>⚠ Zakładki narzędziowe (SQL Editor, Trace, Import…) nie mają nazwy obiektu.</summary>
    private static bool CanTabMenuCopyObjectName(WorkspaceTabViewModel? tab)
        => tab?.ObjectName is { Length: > 0 };

    [RelayCommand(CanExecute = nameof(CanTabMenuCopyObjectName))]
    private async Task TabMenuCopyObjectNameAsync(WorkspaceTabViewModel? tab)
    {
        if (tab?.ObjectName is not { Length: > 0 } name) return;
        if (ClipboardWriteRequested is { } write) await write(name).ConfigureAwait(true);
    }

    /// <summary>⚠ Wymaga RODZAJU i NAZWY — bez obu nie ma czego szukać w drzewie — oraz połączenia.</summary>
    private bool CanTabMenuRevealInExplorer(WorkspaceTabViewModel? tab)
        => tab is { ObjectKind: not null, ObjectName: { Length: > 0 } } && HasActiveConnection;

    /// <summary>⭐ Zaznacza obiekt w Metadata Explorerze <b>i przewija listę tak, żeby był widoczny</b> —
    /// samo zaznaczenie poza ekranem jest nieodróżnialne od braku reakcji.</summary>
    [RelayCommand(CanExecute = nameof(CanTabMenuRevealInExplorer))]
    private async Task TabMenuRevealInExplorerAsync(WorkspaceTabViewModel? tab)
    {
        if (tab is not { ObjectKind: { } kind, ObjectName: { Length: > 0 } name }) return;
        await Metadata.RevealObjectAsync(kind, name).ConfigureAwait(true);
    }

    /// <summary>Skrót do Settings Center, prosto na kategorię „Tabs" (decyzja D8).</summary>
    [RelayCommand]
    private void TabMenuSettings() => SettingsRequested?.Invoke(SettingsCatalog.CategoryTabs);

    /// <summary>Prosi widok o otwarcie Settings Center na wskazanej kategorii.</summary>
    public event Action<string>? SettingsRequested;

    /// <summary>
    /// ⚠⚠ Odświeża stan WŁĄCZENIA pozycji menu zakładki. Ich `CanExecute` zależy od SKŁADU kolekcji
    /// (ile jest zakładek, czy któraś jest na prawo, czy któraś jest czysta), a `[RelayCommand]` sam
    /// z siebie o zmianie kolekcji nic nie wie — bez tego „Zamknij po prawej" zostałoby aktywne po
    /// zamknięciu ostatniej zakładki, czyli dokładnie martwe polecenie, którego menu ma nie mieć.
    /// </summary>
    internal void RaiseTabMenuCanExecuteChanged()
    {
        TabMenuCloseCommand.NotifyCanExecuteChanged();
        TabMenuCloseOthersCommand.NotifyCanExecuteChanged();
        TabMenuCloseAllCommand.NotifyCanExecuteChanged();
        TabMenuCloseToTheRightCommand.NotifyCanExecuteChanged();
        TabMenuCloseUnmodifiedCommand.NotifyCanExecuteChanged();
        TabMenuRefreshCommand.NotifyCanExecuteChanged();
        TabMenuCopyObjectNameCommand.NotifyCanExecuteChanged();
        TabMenuRevealInExplorerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// The SQL Editor grid's clipboard text. ⭐ The FORMAT is <see cref="GridCopyText"/>'s — the one builder
    /// the four other data grids use — so this grid cannot drift from them. What stays here is only the
    /// index-based signature its own call site and tests use.
    /// </summary>
    public string? BuildCopyText(CopyGridMode mode, int rowIndex, int columnIndex)
    {
        if (CurrentResult is not { HasResultSet: true } r) return null;
        // ⚠ AllWithHeaders is reached with rowIndex -1 (there is no target row), so an out-of-range index
        // resolves to "no row" rather than to a null result — the mode decides whether that matters.
        var row = rowIndex >= 0 && rowIndex < r.Rows.Count ? r.Rows[rowIndex] : null;
        return GridCopyText.Build(mode, r.Columns, r.Rows, row, columnIndex);
    }

    // ── Copy as INSERT / UPDATE ───────────────────────────────────────────────
    // The shared SqlCopyController owns the availability flags + tooltips + build flow; this VM keeps ONE
    // controller for the SQL Editor grid (the same controller the Table Data grid uses — one mechanism,
    // not a re-derived copy). Its coordinator caches the ~7 ms provenance capture, so the first right-click
    // pays for it and every later question is free; Reset() re-arms it when a new statement runs.
    private SqlCopyController? _sqlCopy;

    /// <summary>The SQL Editor grid's copy controller. Bound by the results context menu
    /// (<c>SqlCopy.CanCopyAsInsert</c> / <c>SqlCopy.CopyAsInsertTooltip</c>).</summary>
    public SqlCopyController SqlCopy
    {
        get => _sqlCopy ??= new SqlCopyController(new SqlCopyCoordinator(
            () => _lastResultSql,
            CreateMetadataSnapshot,
            () => _executor)
        {
            WarmColumns = async table => await EnsureColumnsAsync(table).ConfigureAwait(true),
        });
        // Test seam only: lets a headless test inject a controller whose coordinator resolves without a
        // live connection, so the SQL-Editor copy path (build → clipboard → message) can be exercised.
        internal set => _sqlCopy = value;
    }

    /// <summary>Drops the cached provenance — the rows are a different result now.</summary>
    private void ResetSqlCopy() => SqlCopy.Reset();

    /// <summary>Re-evaluates whether the SQL copy actions are available. Called when the grid's context
    /// menu opens — that gesture is what makes the lazy capture "on demand" without ever touching the F5
    /// path, where the same 7 ms would be an across-the-board regression.</summary>
    public Task RefreshSqlCopyAvailabilityAsync(CancellationToken cancellationToken = default)
        => SqlCopy.RefreshAvailabilityAsync(CurrentResult is { HasResultSet: true }, cancellationToken);

    /// <summary>Copies the right-clicked <b>row</b> as an INSERT or UPDATE. Takes the row object itself —
    /// exactly like the Table Data grid — rather than an index into <see cref="CurrentResult"/>: the view
    /// already holds the clicked <c>object?[]</c>, so re-deriving its index (a reference lookup that a
    /// re-fetch/re-page can silently miss) only adds a failure mode. Re-checks availability through the
    /// controller rather than trusting the menu's enabled state — that flag is a hint computed a moment
    /// ago, this is the authority, and a wrong statement must never reach the clipboard.</summary>
    public async Task<bool> CopyRowAsSqlAsync(ExportFormat format, object?[]? row, CancellationToken cancellationToken = default)
    {
        // Never fail silently: if the row could not be captured, say so rather than doing nothing (the
        // symptom that hid an earlier row-resolution bug).
        if (row is null)
        {
            AddMessage(MessageSeverity.Warning, UiStrings.GridCopyNoRow);
            return false;
        }

        var built = await SqlCopy.BuildFormattedAsync(format, row, cancellationToken).ConfigureAwait(true);
        if (!built.IsBuilt)
        {
            // Say why, in the Messages panel. The user asked for something EmberTern will not do; silence
            // would leave them to guess, which is exactly what naming the obstacle exists to prevent.
            AddMessage(MessageSeverity.Warning, built.Text);
            return false;
        }

        if (ClipboardWriteRequested is { } write)
        {
            await write(built.Text).ConfigureAwait(true);
        }

        AddMessage(MessageSeverity.Info, string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.GridCopiedToClipboardFormat,
            format == ExportFormat.UpdateScript ? UiStrings.GridCopiedUpdateLabel : UiStrings.GridCopiedInsertLabel));
        return true;
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
        }
        else if (e.PropertyName == nameof(TableDetailTabViewModel.IsFieldsSubTabActive))
        {
            OnPropertyChanged(nameof(IsFieldsTabActive));
            OnPropertyChanged(nameof(ShowFieldEditTools));
            OnPropertyChanged(nameof(ShowTransactionButtons));
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
        // Land the cursor in the editor so the user can start typing the new query immediately.
        EditorFocusRequested?.Invoke();
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

    // ─── Commit / Rollback — ONE transaction, one pair of buttons ─────────────
    //
    // There is exactly one user transaction (the data attachment), so there is nothing to route
    // and nothing to reconcile. This used to be a dual-lane model — Commit had to settle "every
    // lane that has an open transaction", because F5 silently routed DDL onto a second,
    // metadata transaction the user never asked for. With routing gone, CanCommitAll /
    // DecideCommitLanes / DecideRollbackLanes / the per-lane Commit+Rollback command pairs all
    // collapse into this.

    public bool CanCommitAll => _transactionService.IsActive;

    public bool CanRollbackAll => _transactionService.IsActive || _transactionService.IsError;

    [RelayCommand(CanExecute = nameof(CanCommitAll))]
    // "Commit everything" at disconnect/exit settles every source, not just the console: the user was shown
    // every line and answered about all of them at once. The TOOLBAR's Commit stays deliberately narrower —
    // it is the console's button, and making it settle the import would re-create, in the other direction,
    // exactly the cross-module commit I7.5 removed.
    private Task CommitAllAsync() => _pendingWork.CommitAllAsync();

    [RelayCommand(CanExecute = nameof(CanRollbackAll))]
    private Task RollbackAllAsync() => _pendingWork.RollbackAllAsync();

    [RelayCommand]
    private Task CommitAsync() => CommitTransactionAsync();

    [RelayCommand]
    private Task RollbackAsync() => RollbackTransactionAsync();

    private async Task CommitTransactionAsync()
    {
        var tx = _transactionService;
        var lane = UiStrings.TransactionLaneData;

        var count = tx.StatementCount;
        // A plain data commit needs NO refresh — the UI is already current, and a blanket reload
        // here is what caused the post-commit storm (gotcha #119). But a commit that ran DDL DOES:
        // uncommitted DDL is invisible to the read-only metadata attachment, so this is the moment
        // the new/changed object actually becomes visible and the tree must be reloaded.
        _lastTransactionSettleWasRollback = false;
        _settledTransactionChangedSchema = _transactionChangedSchema;
        Diagnostics.RefreshTrace.Log("Commit", $"lane={lane} statements={count} schemaChanged={_settledTransactionChangedSchema}");
        try
        {
            await tx.CommitAsync().ConfigureAwait(true);
            _transactionChangedSchema = false;
            AddMessage(MessageSeverity.Info, string.Format(UiStrings.TransactionLaneCommittedFormat, lane, count));
        }
        catch (TransactionFailedException ex)
        {
            AddMessage(MessageSeverity.Error, ex.Message);
            SelectedBottomTabIndex = 1;
        }
    }

    private async Task RollbackTransactionAsync()
    {
        var tx = _transactionService;
        var lane = UiStrings.TransactionLaneData;
        var count = tx.StatementCount;
        // Rollback ⇒ post-settle refresh runs to revert the in-memory / optimistic state.
        _lastTransactionSettleWasRollback = true;
        _settledTransactionChangedSchema = _transactionChangedSchema;
        Diagnostics.RefreshTrace.Log("Rollback", $"lane={lane} statements={count} (refresh to revert)");
        await tx.RollbackAsync().ConfigureAwait(true);
        _transactionChangedSchema = false;
        AddMessage(MessageSeverity.Info, string.Format(UiStrings.TransactionLaneRolledBackFormat, lane, count));
    }

    private void AddMessage(MessageSeverity severity, string text)
    {
        Messages.Add(new QueryMessageViewModel(severity, text));
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(ShowMessagesEmptyHint));
        CopyAllMessagesCommand.NotifyCanExecuteChanged();
        ClearMessagesCommand.NotifyCanExecuteChanged();
    }

    // Copy the whole Messages log as text (timestamp + message per line). Selecting text
    // within a single message + Ctrl+C is handled natively by the SelectableTextBlock.
    [RelayCommand(CanExecute = nameof(HasMessages))]
    private async Task CopyAllMessagesAsync()
    {
        if (ClipboardWriteRequested is { } write)
        {
            await write(BuildMessagesClipboardText(Messages)).ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(HasMessages))]
    private void ClearMessages()
    {
        Messages.Clear();
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(ShowMessagesEmptyHint));
        CopyAllMessagesCommand.NotifyCanExecuteChanged();
        ClearMessagesCommand.NotifyCanExecuteChanged();
    }

    // Tab-separated timestamp + text per line — pastes cleanly into a log / editor.
    internal static string BuildMessagesClipboardText(IEnumerable<QueryMessageViewModel> messages)
        => string.Join("\n", messages.Select(m => m.TimestampLabel + "\t" + m.Text));

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

            // ⚠ Rozłączenie w trakcie ładowania: faza 3 już nie nastąpi, więc pasek gaśnie tutaj.
            // Bez tego rozłączenie w połowie prefetchu zostawiłoby zapalony pasek (§19.34).
            IsConnecting = false;
        }
        else
        {
            LoadWorkspaceFor(newProfileId);
        }

        // Column + routine-parameter caches belong to the previous schema — drop
        // them on any switch so that "X.column" / a routine signature against a
        // same-named object in another DB doesn't surface stale metadata.
        InvalidateObjectCaches();

        UpdateStatusFromConnection();
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ActiveConnectionName));
        OnPropertyChanged(nameof(HasActiveConnection));
        OnPropertyChanged(nameof(DataProfileName));
        OnPropertyChanged(nameof(MetadataProfileName));
        OnPropertyChanged(nameof(DataTransactionProfileTooltip));
        OnPropertyChanged(nameof(MetadataTransactionProfileTooltip));
        OnPropertyChanged(nameof(IsDeveloperModeActive));
        OnPropertyChanged(nameof(ShowDataTransactionButtons));
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
        OnPropertyChanged(nameof(CanOpenTraceMonitor));
        OpenTraceMonitorCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenSessionManager));
        OpenSessionManagerCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenGlobalSearch));
        OpenGlobalSearchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenScriptExecutor));
        OpenScriptExecutorCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenDataImport));
        OpenDataImportCommand.NotifyCanExecuteChanged();
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

    // Pure decision so it's unit-testable without a live connection. Keyed on WHAT THE TRANSACTION
    // DID, not on which lane it ran on — there is only one lane now.
    //
    // A DML-only COMMIT needs NO refresh: the UI already shows the committed state (data edits
    // paint optimistically). Refreshing after a commit only opens an extra transaction; on a
    // database with an ON TRANSACTION COMMIT trigger (e.g. the user's XXX_WS_TRANS_ON_COMMIT audit
    // trigger → GET_NAGL_WERDYSP → hundreds of BIN_AND/MOD calls) that extra commit re-fires the
    // trigger — the "massive activity after commit" (gotcha #119). So DML commit ⇒ None.
    //
    // A DDL transaction changes the schema, so it needs a Structure refresh on EITHER outcome:
    // committed → the object becomes visible for the first time (uncommitted DDL is invisible to
    // the read-only metadata attachment); rolled back → it must disappear again.
    //
    // A DML ROLLBACK must reload the data preview, to revert the optimistic grid writes.
    internal static PostTransactionRefresh DecidePostTransactionRefresh(
        bool settled, bool schemaChanged, bool wasRollback)
    {
        if (!settled) return PostTransactionRefresh.None;
        if (schemaChanged) return PostTransactionRefresh.Structure;
        return wasRollback ? PostTransactionRefresh.DataOnly : PostTransactionRefresh.None;
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
        // ONE transaction now, so one state machine — no lane reconciliation.
        var current = _transactionService.State;
        var becameActive = _previousTransactionState != TransactionState.Active && current == TransactionState.Active;
        // Active → Idle means a Commit or Rollback just completed; the on-screen
        // TableDetail tabs may be out of sync with the live catalog (rollback
        // reverts ALTERs fired in the tx; commit confirms them) — refresh each.
        var settled = _previousTransactionState == TransactionState.Active && current == TransactionState.Idle;
        _previousTransactionState = current;
        var schemaChanged = _settledTransactionChangedSchema;

        void Apply()
        {
            if (becameActive)
            {
                AddMessage(MessageSeverity.Info, string.Format(UiStrings.TransactionLaneStartedFormat, UiStrings.TransactionLaneData));
            }
            // Route the post-transaction refresh by WHAT THE TRANSACTION DID, not by which lane it
            // ran on (there is only one). A DML-only COMMIT needs no refresh — the UI is already
            // current, and a blanket structure reload here re-runs 8 metadata round-trips and
            // re-fires any ON TRANSACTION_COMMIT trigger (the refresh storm, gotcha #119). A
            // transaction that ran DDL changes the schema, so it needs a structure refresh on
            // EITHER outcome: on commit the object becomes visible for the first time (uncommitted
            // DDL is invisible to the read-only metadata attachment), on rollback it disappears.
            var refresh = DecidePostTransactionRefresh(settled, schemaChanged, _lastTransactionSettleWasRollback);
            RunScopedPostTransactionRefresh(refresh);
            // A DDL commit/rollback also changes the object TREE (new/dropped objects), which the
            // scoped TableDetail refresh above doesn't cover.
            if (settled && schemaChanged) _ = Metadata.RefreshAsync();
            if (settled) _settledTransactionChangedSchema = false;
            // COMMIT: optimistic in-grid values are now committed (= correct), so no reload is
            // needed — just clear the per-tab pending-edit flags. (A ROLLBACK reloads the edited
            // tabs below, which clears their flags.)
            if (settled && !_lastTransactionSettleWasRollback)
            {
                foreach (var tab in WorkspaceTabs)
                    if (tab.TableDetail is { } committed) committed.HasPendingDataEdits = false;
            }

            // ⭐ Znacznik czasu chipa (§8.4.5) — jedyne miejsce, w którym powstaje i ginie. `becameActive`
            // i `settled` są już policzone wyżej, więc chip nie potrzebuje własnej maszyny stanów.
            if (becameActive) _transactionStartedAt = DateTimeOffset.UtcNow;
            if (settled) _transactionStartedAt = null;

            OnPropertyChanged(nameof(IsTransactionIdle));
            OnPropertyChanged(nameof(IsTransactionActive));
            OnPropertyChanged(nameof(IsTransactionError));
            OnPropertyChanged(nameof(HasExecutedInTransaction));
            OnPropertyChanged(nameof(TransactionBarText));
            OnPropertyChanged(nameof(ShowTransactionChip));
            OnPropertyChanged(nameof(TransactionChipBrushKey));
            OnPropertyChanged(nameof(TransactionChipText));
            UpdateTransactionChipTimer();
            OnPropertyChanged(nameof(ShowDataTransactionButtons));
            CommitCommand.NotifyCanExecuteChanged();
            RollbackCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanCommitAll));
            OnPropertyChanged(nameof(CanRollbackAll));
            CommitAllCommand.NotifyCanExecuteChanged();
            RollbackAllCommand.NotifyCanExecuteChanged();

            // The Query tab carries the transaction-active marker.
            var anyExecuted = HasExecutedInTransaction;
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

    // ─── Settings health (audit A-03) ────────────────────────────────────────────────────────────
    //
    // When settings.dat cannot be read, ApplicationSettingsStore.Save REFUSES for the rest of the session
    // rather than replace data it cannot see. That is the right behaviour and it loses nothing — but it is
    // invisible, and invisible is not honest: the user would go on arranging grids, saving queries and adding
    // connections, and none of it would persist. So the one thing this layer owes them is to say so.
    //
    // Warning, not Error: nothing is broken and nothing was lost. Something is being PREVENTED, and the file
    // in question is most likely perfectly good data belonging to another Windows account.

    [ObservableProperty]
    private string _settingsHealthMessage = string.Empty;

    /// <summary>True while the settings-health banner should be up — i.e. the file needs attention and the user
    /// has not dismissed the notice.</summary>
    [ObservableProperty]
    private bool _showSettingsHealthWarning;

    /// <summary>Dismisses the notice for this session. It does not resolve anything, and deliberately does not
    /// pretend to: saving stays refused. It exists so a user who has read the message and decided to carry on
    /// is not nagged.</summary>
    [RelayCommand]
    private void DismissSettingsHealthWarning() => ShowSettingsHealthWarning = false;

    private void CaptureSettingsHealth(ConnectionProfileStore store)
    {
        Core.Settings.SettingsLoadResult health;
        try
        {
            health = store.CheckSettingsHealth();
        }
        catch (Exception)
        {
            // Reading the health must never be the thing that stops the app from starting. Staying silent here
            // is acceptable in a way that staying silent about a KNOWN-bad file is not: we have no finding to
            // report, only a failure to look.
            return;
        }

        if (!health.NeedsAttention) return;

        SettingsHealthMessage = string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.SettingsUnreadableWarningFormat,
            store.FilePath,
            health.Diagnostic ?? string.Empty);
        ShowSettingsHealthWarning = true;
    }

    // ⚠ Nie pisze już żadnego tekstu — sekcja 1 czyta stan wprost z serwisu. Zadaniem tej metody jest
    // POWIADOMIĆ o zmianie tożsamości i zgasić komunikat, który dotyczył poprzedniego połączenia.
    // Gaszenie zachowuje zastane zachowanie: dopóki obie rzeczy dzieliły `StatusText`, zmiana połączenia
    // nadpisywała błąd — i jest to samo w sobie słuszne, bo komunikat opisuje kontekst, którego już nie ma.
    private void UpdateStatusFromConnection()
    {
        OnPropertyChanged(nameof(ConnectionDisplayName));
        OnPropertyChanged(nameof(ConnectionEndpointLabel));
        ClearError();
    }

    private void SetError(string message)
    {
        StatusMessageSeverity = MessageSeverity.Error;
        StatusMessage = message;
    }

    private void ClearError() => StatusMessage = string.Empty;
}
