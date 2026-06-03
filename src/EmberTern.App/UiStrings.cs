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

    public const string TableDetailTabFields = "Pola";
    public const string TableDetailTabConstraints = "Ograniczenia";
    public const string TableDetailTabIndexes = "Indeksy";
    public const string TableDetailTabData = "Dane";
    public const string TableDetailTabDescription = "Opis";
    public const string TableDetailTabDdl = "DDL";
    public const string TableDetailLoadingHint = "Ładowanie szczegółów tabeli…";
    public const string TableDetailColumnPosition = "#";
    public const string TableDetailColumnName = "Nazwa";
    public const string TableDetailColumnType = "Typ";
    public const string TableDetailColumnSize = "Rozmiar";
    public const string TableDetailColumnScale = "Skala";
    public const string TableDetailColumnNotNull = "Not Null";
    public const string TableDetailColumnDefault = "Default";
    public const string TableDetailColumnDescription = "Opis";
    public const string TableDetailIndexFields = "Pola";
    public const string TableDetailIndexUnique = "Unikalny";
    public const string TableDetailIndexDescending = "Malejący";
    public const string TableDetailIndexPrimary = "PK";
    public const string TableDetailConstraintKind = "Typ";
    public const string TableDetailConstraintFields = "Pola";
    public const string TableDetailConstraintRefTable = "Tabela ref.";
    public const string TableDetailConstraintRefFields = "Pola ref.";
    public const string TableDetailConstraintCheck = "Warunek";
    public const string TableDetailDataPreviewHintFormat = "Pokazuję pierwsze {0} wierszy";
    public const string TableDetailDataLoadingHint = "Ładowanie danych…";
    public const string TableDetailDescriptionEmpty = "Brak opisu.";

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
