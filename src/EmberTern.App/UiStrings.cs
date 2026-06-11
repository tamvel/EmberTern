namespace EmberTern.App;

internal static class UiStrings
{
    public const string AppTitle = "EmberTern";
    public const string AppSubtitle = "Firebird Developer Workbench";

    public const string SidebarMetadataHeader = "Metadata";
    public const string SidebarConnectionsHeader = "Connections";
    public const string SidebarPlaceholderEmpty = "No connection yet";
    public const string SidebarTabMetadata = "Metadata";
    public const string SidebarTabConnections = "Connections";

    public const string MetadataGroupTables = "Tables";
    public const string MetadataGroupViews = "Views";
    public const string MetadataGroupProcedures = "Procedures";
    public const string MetadataGroupTriggers = "Triggers";
    public const string MetadataGroupFunctions = "Functions";
    public const string MetadataGroupGenerators = "Generators";
    public const string MetadataGroupDomains = "Domains";
    public const string MetadataGroupPackages = "Packages";
    public const string MetadataGroupExceptions = "Exceptions";
    public const string MetadataGroupRoles = "Roles";
    public const string MetadataGroupUsers = "Users";
    public const string MetadataGroupIndexes = "Indexes";
    public const string MetadataGroupSystemTables = "System Tables";
    public const string MetadataNotConnectedHint = "Connect to a database to browse its objects.";
    public const string MetadataFilterPlaceholder = "Filter objects…";
    public const string MetadataRefreshTooltip = "Refresh metadata";
    public const string MetadataContextOpenDdl = "View DDL";
    public const string MetadataContextCopyName = "Copy Name";
    public const string MetadataNameCopiedFormat = "Copied “{0}” to clipboard.";
    public const string MetadataLoadingPlaceholder = "Loading…";
    public const string TabCloseTooltip = "Close tab";

    public const string ConnectionConnect = "Connect";
    public const string ConnectionDisconnect = "Disconnect";
    public const string ConnectionDelete = "Delete";
    public const string ConnectionNew = "+ New Connection";
    public const string ConnectionsEmptyHint = "No connections yet.\nClick “+ New Connection” to add one.";

    public const string WorkspaceTabUntitled = "SQL Editor";
    public const string WorkspaceEditorPlaceholder = "-- Connect to a database to start writing SQL";

    public const string TransactionBarInactive = "No transaction";
    public const string TransactionBarActive = "Active Transaction";
    public const string TransactionBarError = "Transaction Error";
    public const string TransactionCommit = "Commit";
    public const string TransactionRollback = "Rollback";
    public const string TransactionStatementCountFormat = "{0} statement(s)";
    public const string TransactionStartedMessage = "Transaction started.";
    public const string TransactionCommittedFormat = "Transaction committed ({0} statement(s)).";
    public const string TransactionRolledBackFormat = "Transaction rolled back ({0} statement(s)).";
    public const string DisconnectConfirmTitle = "Active transaction";
    public const string DisconnectConfirmMessage = "Disconnecting will roll back the active transaction.\n\nDisconnect anyway?";
    public const string DisconnectConfirmYes = "Disconnect";
    public const string DisconnectConfirmNo = "Cancel";

    public const string BottomTabMessages = "Messages";
    public const string BottomTabResults = "Results";
    public const string BottomTabOutput = "Output";

    public const string StatusBarReady = "Ready";
    public const string StatusBarConnectedTo = "Connected to";
    public const string StatusBarDisconnected = "Disconnected";

    public const string ThemeToggleTooltip = "Toggle dark / light theme";

    public const string DialogNewConnectionTitle = "New Connection";
    public const string DialogEditConnectionTitle = "Edit Connection";
    public const string ConnectionEdit = "Edit";
    public const string DialogSectionGeneral = "Connection";
    public const string DialogSectionAdvanced = "Advanced";
    public const string DialogFieldName = "Name";
    public const string DialogFieldHost = "Host";
    public const string DialogFieldPort = "Port";
    public const string DialogFieldDatabasePath = "Database path";
    public const string DialogFieldUsername = "Username";
    public const string DialogFieldPassword = "Password";
    public const string DialogFieldCharset = "Charset";
    public const string DialogFieldDialect = "Dialect";
    public const string DialogFieldClientLibrary = "Client library (fbclient.dll)";
    public const string DialogFieldClientLibraryHint = "Leave empty to use the default. Set when connecting to a Firebird version different from the default client (e.g. Firebird 3 server while Firebird 5 client is on PATH).";
    public const string DialogTestConnection = "Test connection";
    public const string DialogSave = "Save";
    public const string DialogCancel = "Cancel";
    public const string DialogBrowse = "Browse…";

    public const string TestInProgress = "Testing connection…";
    public const string TestSuccess = "Connection successful.";

    public const string ValidationNameRequired = "Name is required.";
    public const string ValidationDatabaseRequired = "Database path is required.";

    public const string ToolbarExecute = "Execute";
    public const string ToolbarCancel = "Cancel";
    public const string ToolbarExecuteHint = "F5";
    public const string ToolbarClearEditor = "Clear";
    public const string ToolbarClearEditorIcon = "🗑";
    public const string ToolbarClearEditorTooltip = "Clear editor content";
    public const string ToolbarCloseTab = "Close tab";
    public const string ToolbarCloseTabIcon = "✕";
    public const string ToolbarCloseTabTooltip = "Close active tab";
    public const string ToolbarNewQueryIcon = "+";
    public const string ToolbarNewQueryTooltip = "New saved query";
    public const string ToolbarToggleQueryPanelIcon = "▤";
    public const string ToolbarToggleQueryPanelTooltip = "Show / hide saved queries panel";
    public const string ToolbarFormatSqlIcon = "⎄";
    public const string ToolbarFormatSqlTooltip = "Format SQL (Alt+F)";
    public const string ToolbarRefreshDataIcon = "↺";
    public const string ToolbarRefreshDataTooltip = "Refresh data preview";

    public const string QueryPanelHeader = "Saved Queries";
    public const string QueryPanelEmptyHint = "No saved queries yet.";
    public const string QueryDefaultNameFormat = "Query {0}";
    public const string QueryDeleteTooltip = "Delete selected query";
    public const string QueryClearAllTooltip = "Clear all saved queries";
    public const string QueryDeleteConfirmTitle = "Delete saved query";
    public const string QueryDeleteConfirmFormat = "Delete “{0}”?";
    public const string QueryDeleteConfirmYes = "Delete";
    public const string QueryClearAllConfirmTitle = "Clear saved queries";
    public const string QueryClearAllConfirmMessage = "Clear all saved queries for this connection?";
    public const string QueryClearAllConfirmYes = "Clear all";
    public const string GridCopyCell = "Copy cell";
    public const string GridCopyRow = "Copy row";
    public const string GridCopyRowWithHeaders = "Copy row with headers";
    public const string GridCopyAllWithHeaders = "Copy all with headers";
    public const string GridCopiedToClipboardFormat = "Copied {0} to clipboard.";
    public const string GridCopiedCellLabel = "cell";
    public const string GridCopiedRowLabel = "row";
    public const string GridCopiedRowsFormat = "{0} rows";

    public const string ResultsEmptyHint = "Run a query to see results.";
    public const string MessagesEmptyHint = "No messages yet.";
    public const string ExecutingStatus = "Executing query…";
    public const string NoConnectionMessage = "Connect to a database first.";
    public const string QueryCancelledMessage = "Query cancelled.";
    public const string AffectedRowsFormat = "{0} rows affected in {1} ms";
    public const string ResultsTruncatedFormat = "Results limited to {0} rows.";
    public const string RowsFetchedFormat = "{0} rows in {1} ms";

    // Main tab names stay Polish — pre-existing intentional choice.
    public const string TableDetailTabFields = "Pola";
    public const string TableDetailTabConstraints = "Ograniczenia";
    public const string TableDetailTabIndexes = "Indeksy";
    public const string TableDetailTabDependencies = "Zależności";
    public const string TableDetailDependsOnHeader = "Depends on";
    public const string TableDetailDependedOnByHeader = "Used by";
    public const string DependencyCategoryUdfs = "UDFs";
    public const string TableDetailDependencyType = "Type";
    public const string TableDetailDependencyName = "Name";
    public const string TableDetailDependencyField = "Field";
    public const string TableDetailTabData = "Dane";
    public const string TableDetailTabDescription = "Opis";
    public const string TableDetailTabDdl = "DDL";
    public const string TableDetailConstraintSubTabPrimaryKey = "Primary Key";
    public const string TableDetailConstraintSubTabForeignKey = "Foreign Keys";
    public const string TableDetailConstraintSubTabCheck = "Check";
    public const string TableDetailConstraintSubTabUnique = "Unique";

    public const string TableDetailLoadingHint = "Loading table details…";
    public const string TableDetailColumnPosition = "#";
    public const string TableDetailColumnName = "Name";
    public const string TableDetailColumnType = "Type";
    public const string TableDetailColumnSize = "Size";
    public const string TableDetailColumnScale = "Scale";
    public const string TableDetailColumnNotNull = "Not Null";
    public const string TableDetailColumnDefault = "Default";
    public const string TableDetailColumnDescription = "Description";
    public const string TableDetailColumnPrimaryKey = "Primary key";
    public const string TableDetailColumnForeignKey = "Foreign key";
    public const string TableDetailColumnUnique = "Unique";
    public const string TableDetailColumnDomain = "Domain";
    public const string TableDetailColumnForeignKeyTable = "FK Table";
    public const string TableDetailColumnComputed = "Computed";
    public const string TableDetailColumnCharset = "Charset";

    public const string TableDetailIndexType = "Type";
    public const string TableDetailIndexFields = "Fields";
    public const string TableDetailIndexExpression = "Expression";
    public const string TableDetailIndexUnique = "Unique";
    public const string TableDetailIndexDescending = "Descending";
    public const string TableDetailIndexPrimary = "PK";
    public const string TableDetailIndexActive = "Active";
    public const string TableDetailIndexStatistics = "Statistics";

    public const string TableDetailConstraintFields = "Fields";
    public const string TableDetailConstraintRefTable = "Ref. table";
    public const string TableDetailConstraintRefFields = "Ref. fields";
    public const string TableDetailConstraintUpdateRule = "Update rule";
    public const string TableDetailConstraintDeleteRule = "Delete rule";
    public const string TableDetailConstraintSource = "Source";
    public const string TableDetailConstraintIndexName = "Index name";
    public const string TableDetailConstraintSort = "Sort";
    public const string TableDetailConstraintSortAscending = "Ascending";
    public const string TableDetailConstraintSortDescending = "Descending";

    public const string TableDetailDataPagedHintFormat = "Page {0} · Showing {1} rows";
    public const string TableDetailDataPreviewSortedByFormat = " · sorted by {0} {1}";

    public const string TableDetailPaginationFirstIcon = "⏮";
    public const string TableDetailPaginationPreviousIcon = "◀";
    public const string TableDetailPaginationNextIcon = "▶";
    public const string TableDetailPaginationLastIcon = "⏭";
    public const string TableDetailPaginationFirstTooltip = "First page";
    public const string TableDetailPaginationPreviousTooltip = "Previous page";
    public const string TableDetailPaginationNextTooltip = "Next page";
    public const string TableDetailPaginationLastTooltip = "Last page";
    public const string TableDetailDataPreviewNullPlaceholder = "<null>";
    public const string TableDetailDataLoadingHint = "Loading data…";
    public const string TableDetailDescriptionEmpty = "No description.";

    public const string DataEditAddRowIcon = "+";
    public const string DataEditAddRowTooltip = "Add new row";
    public const string DataEditDeleteRowIcon = "−";
    public const string DataEditDeleteRowTooltip = "Delete selected row";
    public const string DataEditDeleteConfirmTitle = "Delete row";
    public const string DataEditDeleteConfirmMessage = "Delete the selected row? This becomes part of the current transaction — use Rollback to revert.";
    public const string DataEditDeleteConfirmYes = "Delete";
    public const string DataEditNoPrimaryKeyHint = "Table has no primary key — only INSERT is available.";
    public const string DataEditNotConnectedHint = "Connect to a database to edit data.";

    public const string BlobEditorTitle = "Edit BLOB";
    public const string BlobEditorBinaryPlaceholder = "Binary BLOB ({0} bytes) — cannot be edited as text.";
    public const string BlobEditorButtonIcon = "…";
    public const string BlobEditorButtonTooltip = "Edit BLOB content";
    public const string BlobEditorOk = "OK";

    public const string FolderNewTooltip = "Nowy katalog";
    public const string FolderNewIcon = "📁";
    public const string FolderNodeIcon = "📁";
    public const string FolderDialogTitle = "Nowy katalog";
    public const string FolderDialogNameLabel = "Nazwa katalogu";
    public const string FolderDialogCreate = "Utwórz";
    public const string FolderContextRename = "Zmień nazwę";
    public const string FolderContextDelete = "Usuń katalog";
    public const string FolderDeleteConfirmTitle = "Usuń katalog";
    public const string FolderDeleteConfirmFormat = "Usunąć katalog „{0}”? Połączenia z tego katalogu wrócą do korzenia drzewa.";
    public const string FolderDeleteConfirmYes = "Usuń";
    public const string FolderDefaultName = "Nowy katalog";

    public const string ConnectionContextSort = "Sortuj węzły";
    public const string ConnectionContextSortAscending = "Rosnąco (A→Z)";
    public const string ConnectionContextSortDescending = "Malejąco (Z→A)";

    public const string FolderContextAddConnection = "Dodaj połączenie";

    public const string QueryContextRename = "Zmień nazwę";
    public const string QueryContextDelete = "Usuń";
    public const string QueryRenameIcon = "✎";
    public const string QueryRenameTooltip = "Zmień nazwę zapytania";
}
