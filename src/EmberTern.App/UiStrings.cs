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
    // Table context menu (metadata tree, Session 5 UX sprint)
    public const string MetadataContextNewTable = "New Table";
    public const string MetadataContextOpenTable = "Open";
    public const string MetadataContextDesignTable = "Design Table";
    public const string MetadataContextDeleteTable = "Delete Table";
    public const string MetadataDeleteTableConfirmTitle = "Delete table";
    public const string MetadataDeleteTableConfirmFormat = "Are you sure you want to delete table {0}?";
    public const string MetadataDeleteTableConfirmYes = "Delete";
    public const string MetadataDeleteTableExecutedFormat = "Table {0} deleted.";
    public const string MetadataDeleteTableFailedFormat = "Could not delete table {0}: {1}";
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
    // Lane-qualified transaction strings (C2 — Data / Metadata working transactions).
    public const string TransactionLaneData = "Data";
    public const string TransactionLaneMetadata = "Metadata";
    public const string TransactionLaneStartedFormat = "{0} transaction started.";
    public const string TransactionLaneCommittedFormat = "{0} transaction committed ({1} statement(s)).";
    public const string TransactionLaneRolledBackFormat = "{0} transaction rolled back ({1} statement(s)).";
    public const string TransactionDataBarPrefix = "Data";
    public const string TransactionMetadataBarPrefix = "Meta";
    public const string TransactionCommitDataTooltip = "Commit data transaction";
    public const string TransactionRollbackDataTooltip = "Roll back data transaction";
    public const string TransactionCommitMetadataTooltip = "Commit metadata transaction";
    public const string TransactionRollbackMetadataTooltip = "Roll back metadata transaction";
    // Unified single-pair tooltips — the app commits/rolls back whichever lane(s) are open.
    public const string TransactionCommitTooltip = "Commit";
    public const string TransactionRollbackTooltip = "Roll back";
    // Execution-lane feedback: which profile the auto-router chose for a statement.
    // {0} = lane (Data/Metadata), {1} = profile label (e.g. "Read Committed").
    public const string ExecutedViaProfileFormat = "Executed via {0} profile ({1}).";
    // Legacy binary disconnect-confirm strings — superseded by the DisconnectChoice*
    // set below (Commit / Roll back / Cancel). Kept only to avoid churn; not referenced.
    public const string DisconnectConfirmTitle = "Active transaction";
    public const string DisconnectConfirmMessage = "Disconnecting will roll back the active transaction.\n\nDisconnect anyway?";
    public const string DisconnectConfirmYes = "Disconnect";
    public const string DisconnectConfirmNo = "Cancel";

    // ─── Data-loss WorkGuard ───────────────────────────────────────────────
    // Unsaved-work summary lines (one per affected tab / transaction lane).
    public const string UnsavedNewTableFormat = "New table (not yet created) — {0}";
    public const string UnsavedNewViewFormat = "New view (not yet created) — {0}";
    public const string UnsavedNewProcedureFormat = "New procedure (not yet created) — {0}";
    public const string UnsavedModifiedViewFormat = "View {0} — uncompiled changes";
    public const string UnsavedModifiedProcedureFormat = "Procedure {0} — uncompiled changes";
    public const string UnsavedNewTriggerFormat = "New trigger (not yet created) — {0}";
    public const string UnsavedModifiedTriggerFormat = "Trigger {0} — uncompiled changes";
    public const string UnsavedPendingStructureFormat = "Table {0} — uncompiled structural changes";
    public const string UnsavedTransactionDataFormat = "Data transaction — {0} pending statement(s)";
    public const string UnsavedTransactionMetadataFormat = "Metadata transaction — {0} pending statement(s)";

    // Tab close (binary Discard / Cancel). {0} = the tab's unsaved-work label.
    public const string CloseTabUnsavedConfirmTitle = "Unsaved changes";
    public const string CloseTabUnsavedConfirmFormat = "{0}\n\nClosing this tab discards these changes.";
    public const string CloseTabUnsavedConfirmYes = "Discard and close";

    // Disconnect with an active transaction (3-way choice; default Roll back).
    public const string DisconnectChoiceTitle = "Active transaction";
    public const string DisconnectChoiceHeaderFormat = "Connection \"{0}\" has an active transaction:";
    public const string DisconnectChoiceQuestion = "What should happen before disconnecting?";
    public const string DisconnectChoiceCommit = "Commit and disconnect";
    public const string DisconnectChoiceRollback = "Roll back and disconnect";
    public const string DisconnectChoiceCancel = "Cancel";
    public const string DisconnectUnsavedDiscardNoteFormat = "Uncompiled changes in {0} tab(s) will be discarded.";

    // Disconnect with uncompiled tab work but no transaction (binary).
    public const string DisconnectUnsavedTitle = "Unsaved changes";
    public const string DisconnectUnsavedIntro = "Disconnecting will discard uncompiled changes in:";
    public const string DisconnectUnsavedYes = "Discard and disconnect";

    // App close with unsaved work / active transactions (2-way; default Cancel).
    public const string ExitUnsavedTitle = "Unsaved work";
    public const string ExitUnsavedIntro = "Exiting now will lose the following:";
    public const string ExitUnsavedTransactionNote = "Active transactions will be rolled back.";
    public const string ExitUnsavedDiscard = "Discard and exit";
    public const string ExitUnsavedCancel = "Cancel";

    public const string BottomTabMessages = "Messages";
    public const string BottomTabResults = "Results";
    public const string BottomTabOutput = "Output";

    public const string StatusBarReady = "Ready";
    public const string StatusBarConnectedTo = "Connected to";
    public const string StatusBarDisconnected = "Disconnected";

    public const string ThemeToggleTooltip = "Toggle dark / light theme";
    public const string SidebarToggleTooltip = "Show / hide the connections panel";
    public const string SidebarExpandTooltip = "Show the connections panel";
    public const string ResultsPanelMaximizeTooltip = "Maximize / restore results (double-click the splitter)";

    // Max length for a connection profile name. 60 chars comfortably holds
    // "ENV - Client - Database"-style names while keeping the titlebar chip and
    // sidebar rows from being pushed off-screen by an abusive name.
    public const int ConnectionNameMaxLength = 60;

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
    public const string DialogFieldTransactionProfile = "Transaction profile";
    public const string DialogFieldDataTransactionProfile = "Data transaction profile";
    public const string DialogFieldMetadataTransactionProfile = "Metadata transaction profile";
    public const string DialogTestConnection = "Test connection";
    public const string DialogSave = "Save";
    public const string DialogCancel = "Cancel";
    public const string DialogBrowse = "Browse…";

    // Developer Mode — the single user-facing switch that replaces the TPB profile
    // pickers. No transaction terminology is exposed (NOWAIT/WAIT/consistency are
    // implementation details).
    public const string DialogFieldDeveloperMode = "Developer Mode";
    public const string DeveloperModeDescription = "Lets you modify procedures, functions, triggers and other objects that are in use by active sessions. DDL operations may wait for the object to be released instead of returning an error immediately.";
    public const string DeveloperModeBadge = "DEV MODE";
    public const string DeveloperModeBadgeTooltip = "Developer Mode is on — DDL waits briefly for in-use objects instead of failing immediately.";

    // Transaction profile labels (IBExpert terms — kept in English on purpose).
    public const string TransactionProfileReadCommitted = "Read Committed";
    public const string TransactionProfileSnapshot = "Snapshot";
    public const string TransactionProfileReadOnlyTableStability = "Read Only Table Stability";
    public const string TransactionProfileReadWriteTableStability = "Read Write Table Stability";
    // Per-profile one-line descriptions shown under the picker.
    public const string TransactionProfileReadCommittedDesc = "Sees committed changes from other transactions. Safe default for everyday work.";
    public const string TransactionProfileSnapshotDesc = "Stable snapshot of the database taken at transaction start. Does not see later commits.";
    public const string TransactionProfileReadOnlyTableStabilityDesc = "Read-only with table stability (consistency). Warning: locks whole tables and can block other users.";
    public const string TransactionProfileReadWriteTableStabilityDesc = "Read-write with table stability (consistency). Warning: locks whole tables and can block other users.";
    // Title-bar transaction-profile block (C2): two stacked lines, each a static lane
    // label + the full profile name in a lane-colored badge. Vertical layout keeps the
    // block narrow while the full name stays readable without hovering.
    public const string TransactionProfileDataLabel = "Data:";
    public const string TransactionProfileMetadataLabel = "Meta:";
    public const string TransactionProfileDataChipTooltipFormat = "Data lane: {0}";
    public const string TransactionProfileMetadataChipTooltipFormat = "Metadata lane: {0}";

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
    // Context-menu toggle for grid column layout — when checked, columns auto-size to
    // content and manual widths aren't remembered; when unchecked, manual widths persist.
    public const string GridAutoFitColumns = "Auto-fit columns";

    public const string ResultsEmptyHint = "Run a query to see results.";
    public const string MessagesEmptyHint = "No messages yet.";
    public const string ExecutingStatus = "Executing query…";
    public const string NoConnectionMessage = "Connect to a database first.";
    public const string QueryCancelledMessage = "Query cancelled.";
    public const string AffectedRowsFormat = "{0} rows affected in {1} ms";
    public const string ResultsTruncatedFormat = "Results limited to {0} rows.";
    public const string RowsFetchedFormat = "{0} rows in {1} ms";
    // {0} = current page, {1} = total pages, {2} = total rows in the result set.
    public const string ResultsPaginationHintFormat = "Page {0} of {1} · {2} rows";

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
    public const string TableDetailColumnAutoIncrement = "AI";
    public const string TableDetailColumnAutoIncrementTooltip = "Auto-increment (IDENTITY column or BEFORE INSERT trigger using GEN_ID)";

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
    // Cell context-menu: set the right-clicked cell to NULL. Enabled only for
    // nullable, non-computed columns; routes through the same UpdateCellAsync
    // path as a manual edit.
    public const string DataEditSetNull = "Set NULL";

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

    // Connection deletion — HIGH risk (config + per-connection saved queries +
    // workspace state all gone, irreversible). Message phrased per the user's
    // spec; matches the English of the table-delete + saved-query confirms.
    public const string ConnectionDeleteConfirmTitle = "Delete connection";
    public const string ConnectionDeleteConfirmFormat =
        "Are you sure you want to delete connection '{0}'?\n\n" +
        "• Saved connection settings will be lost.\n" +
        "• Saved queries linked to this connection will be removed.\n" +
        "• This operation cannot be undone.";
    public const string ConnectionDeleteConfirmYes = "Delete";

    // Clear-editor confirmation (only shown when the editor has text to lose).
    public const string ClearEditorConfirmTitle = "Clear editor";
    public const string ClearEditorConfirmMessage =
        "Clear the SQL editor? The current query text will be lost.";
    public const string ClearEditorConfirmYes = "Clear";

    // Closing a New Table tab with unsaved form content.
    public const string NewTableCloseConfirmTitle = "Discard new table";
    public const string NewTableCloseConfirmFormat =
        "Discard the unsaved table '{0}'? The form has not been compiled.";
    public const string NewTableCloseConfirmYes = "Discard";

    public const string ConnectionContextSort = "Sortuj węzły";
    public const string ConnectionContextSortAscending = "Rosnąco (A→Z)";
    public const string ConnectionContextSortDescending = "Malejąco (Z→A)";

    public const string FolderContextAddConnection = "Dodaj połączenie";

    public const string QueryContextRename = "Zmień nazwę";
    public const string QueryContextDelete = "Usuń";
    public const string QueryRenameIcon = "✎";
    public const string QueryRenameTooltip = "Zmień nazwę zapytania";

    // ─── Table structure editing (New Table + Pola edit toolbar + AddFieldDialog) ───
    // Two glyphs: ▦ matches the metadata tree's Table icon (see MetadataNodeViewModel
    // IconFor) so the toolbar visually rhymes with the sidebar; ＋ signals "add".
    public const string ToolbarNewTableIcon = "▦＋";
    public const string ToolbarNewTableTooltip = "New Table";
    public const string ToolbarToggleFieldEditIcon = "▦✎";
    public const string ToolbarToggleFieldEditTooltip = "Toggle inline field editing";
    public const string NewTableDialogTitle = "Create Table";
    public const string NewTableDialogTableNameLabel = "Table name";
    public const string NewTableDialogTableKindLabel = "Table kind";
    public const string NewTableKindPersistent = "Persistent";
    public const string NewTableKindTempDelete = "Temp : DELETE ROWS";
    public const string NewTableKindTempPreserve = "Temp : PRESERVE ROWS";
    public const string NewTableTabFields = "Fields";
    public const string NewTableTabDescription = "Description";
    public const string NewTableDescriptionLabel = "Table description (COMMENT ON TABLE)";
    public const string NewTableDdlLabel = "Live DDL preview";
    public const string NewTableDialogCompile = "Compile";
    public const string NewTableNamePlaceholder = "MY_TABLE";
    public const string NewTableAddRowTooltip = "Add field";
    public const string NewTableDeleteRowTooltip = "Remove selected field";
    public const string NewTableMoveUpTooltip = "Move selected field up";
    public const string NewTableMoveDownTooltip = "Move selected field down";
    public const string NewTableValidationNameRequired = "Table name is required.";
    public const string NewTableValidationAtLeastOneField = "At least one field is required.";
    public const string NewTableFieldName = "Name";
    public const string NewTableFieldType = "Type";
    public const string NewTableFieldSize = "Size";
    public const string NewTableFieldScale = "Scale";
    public const string NewTableFieldNotNull = "Not Null";
    public const string NewTableFieldDefault = "Default";
    public const string NewTableFieldPk = "PK";
    public const string NewTableFieldAi = "AI";
    public const string NewTableFieldDescription = "Description";
    public const string NewTableFieldDomain = "Domain";
    public const string NewTableFieldComputed = "Computed";
    public const string NewTableFieldCheck = "Check";
    public const string NewTableFieldCharset = "Charset";
    public const string NewTableTabDefaultTitle = "New Table";
    public const string NewTableExecutedFormat = "CREATE TABLE \"{0}\" executed.";

    // ─── View Detail (View Detail V1) ───────────────────────────────────────
    public const string ViewDetailTabSql = "Editor";
    public const string ViewDetailTabFields = "Fields";
    public const string ViewDetailTabDependencies = "Dependencies";
    public const string ViewDetailTabData = "Data";
    public const string ViewDetailTabDescription = "Description";
    public const string ViewDetailTabDdl = "DDL";
    public const string ViewDetailDescriptionEmpty = "No description.";
    public const string ViewDetailLoadingHint = "Loading view…";
    public const string ToolbarNewViewTooltip = "New View";
    public const string ViewCompileIcon = "⚡";
    public const string ViewCompileTooltip = "Compile view (CREATE OR ALTER VIEW)";
    public const string ViewCompileFailedFormat = "Compile failed: {0}";
    public const string NewViewTabDefaultTitle = "New View";
    public const string NewViewExecutedFormat = "View \"{0}\" created.";

    // View Detail Easy mode (mirrors the Procedure Detail Source/Easy toggle).
    // Unified toolbar Collection section (routes to the active editor's collection —
    // fields / columns / params / variables / …). Generic labels: the router decides
    // which collection the action applies to.
    public const string CollectionAddTooltip = "Add item";
    public const string CollectionRemoveTooltip = "Remove item";
    public const string CollectionMoveUpTooltip = "Move up";
    public const string CollectionMoveDownTooltip = "Move down";

    public const string ViewModeToggleTooltip = "Toggle Source / Easy mode";
    public const string ViewParseFailedNotice =
        "Could not parse the view source into the column list + SELECT body — staying on the last structured model. Switch to Source mode to edit the full statement.";
    public const string ViewNameHeader = "View name";
    public const string ViewColumnsHeader = "Columns";
    public const string ViewColumnAddTooltip = "Add column";
    public const string ViewColumnDeleteTooltip = "Delete column";
    public const string ViewColumnMoveUpTooltip = "Move column up";
    public const string ViewColumnMoveDownTooltip = "Move column down";
    public const string ViewColumnName = "Name";
    public const string ViewBodyHeader = "SELECT body";

    // ─── Procedure Detail (Procedure Detail V1) ─────────────────────────────
    public const string ProcedureNameHeader = "Procedure name";
    public const string ProcedureDetailTabEditor = "Editor";
    public const string ProcedureDetailTabDescription = "Description";
    public const string ProcedureDetailTabDependencies = "Dependencies";
    public const string ProcedureDetailTabDdl = "DDL";
    public const string ProcedureDetailParamInputFormat = "Input ({0})";
    public const string ProcedureDetailParamOutputFormat = "Output ({0})";
    public const string ProcedureDetailLoadingHint = "Loading procedure…";
    public const string ProcedureCompileTooltip = "Compile procedure (CREATE OR ALTER PROCEDURE)";
    public const string ProcedureCompileFailedFormat = "Compile failed: {0}";
    public const string ToolbarNewProcedureTooltip = "New Procedure";
    public const string NewProcedureTabDefaultTitle = "New Procedure";
    public const string NewProcedureExecutedFormat = "Procedure \"{0}\" created.";

    // ─── Procedure Detail V1.1 (modes, locals, execute, comment) ────────────
    public const string ProcedureDetailTabResult = "Result";
    public const string ProcedureDetailLocalsVariablesFormat = "Variables ({0})";
    public const string ProcedureDetailLocalsCursorsFormat = "Cursors ({0})";
    public const string ProcedureDetailLocalsSubprogramsFormat = "Subprograms ({0})";
    public const string ProcedureDetailLocalsColumnDetail = "Detail";
    public const string ProcedureParseFailedNotice =
        "Couldn't parse the source into structured form — Easy mode is showing the last loaded state. Edit in Source mode, or fix the header.";
    public const string ProcedureModeSourceLabel = "Source";
    public const string ProcedureModeEasyLabel = "Easy";
    public const string ProcedureModeToggleTooltip = "Toggle Source / Easy mode";
    public const string ProcedureExecuteTooltip = "Execute procedure";
    public const string ProcedureCommentTooltip = "Comment body — disable the procedure body (/* */)";
    public const string ProcedureUncommentTooltip = "Uncomment body — re-enable the procedure body";
    public const string ProcedureParamAddTooltip = "Add parameter";
    public const string ProcedureParamDeleteTooltip = "Delete parameter";
    public const string ProcedureParamMoveUpTooltip = "Move parameter up";
    public const string ProcedureParamMoveDownTooltip = "Move parameter down";
    public const string ProcedureExecRowsFormat = "{0} row(s) returned.";
    public const string ProcedureExecCompleted = "Procedure executed.";
    // {0} = count, {1} = elapsed ms.
    public const string ProcedureExecInfoRowsFormat = "Executed in {1} ms · {0} row(s) returned";
    public const string ProcedureExecInfoAffectedFormat = "Executed in {1} ms · {0} row(s) affected";
    public const string ProcedureExecInfoCompletedFormat = "Executed in {0} ms · completed";
    public const string ProcedureExecutedViaDataProfile = "Executed procedure via Data profile.";
    public const string ProcedureExecEmptyHint = "Run Execute to see results.";

    // New-element templates (FB-valid PSQL) used when adding a cursor / subprogram.
    public const string ProcedureSnippetVariable = "declare variable NewVariable integer;\n";
    public const string ProcedureSnippetCursor = "DECLARE NEW_CURSOR CURSOR FOR (\n    SELECT /* columns */\n    FROM /* table */\n);";
    public const string ProcedureSnippetSubprogram = "DECLARE PROCEDURE NEW_PROCEDURE\nAS\nBEGIN\n    /* body */\nEND";
    public const string ProcedureSnippetFunction = "DECLARE FUNCTION NEW_FUNCTION\nRETURNS INTEGER\nAS\nBEGIN\n    /* body */\n    RETURN 0;\nEND";
    public const string ProcedureLocalsSourceEmptyHint = "Select an item to edit its source.";

    // New-subprogram kind prompt (Procedure / Function).
    public const string SubprogramKindDialogTitle = "New Subprogram";
    public const string SubprogramKindDialogPrompt = "Create a procedure or a function?";
    public const string SubprogramKindProcedure = "Procedure";
    public const string SubprogramKindFunction = "Function";

    // Variable / parameter grid column headers not already present.
    public const string ProcedureFieldTypeOf = "TYPE OF";
    public const string ProcedureFieldSubType = "Sub Type";
    public const string ProcedureFieldCharset = "Charset";
    public const string ProcedureFieldCollate = "Collate";
    public const string ProcedureFieldDescription = "Description";
    public const string ProcedureCursorScroll = "Scroll";

    // Local-element editor toolbars (Variables / Cursors / Subprograms — model-backed).
    public const string ProcedureLocalAddTooltip = "Add";
    public const string ProcedureLocalDeleteTooltip = "Delete";
    public const string ProcedureLocalMoveUpTooltip = "Move up";
    public const string ProcedureLocalMoveDownTooltip = "Move down";

    // Execute Procedure parameter dialog
    public const string ProcedureExecuteDialogTitle = "Execute Procedure";
    public const string ProcedureExecuteDialogColumnName = "Parameter";
    public const string ProcedureExecuteDialogColumnType = "Type";
    public const string ProcedureExecuteDialogColumnValue = "Value";
    public const string ProcedureExecuteDialogColumnNull = "NULL";
    public const string ProcedureExecuteDialogRun = "Execute";
    public const string ProcedureExecuteDialogCancel = "Cancel";

    // ─── Trigger Detail ─────────────────────────────────────────────────────
    public const string TriggerNameHeader = "Trigger name";
    public const string TriggerTableHeader = "Table";
    public const string TriggerTimingHeader = "Timing";
    public const string TriggerEventsHeader = "Events";
    public const string TriggerEventInsert = "INSERT";
    public const string TriggerEventUpdate = "UPDATE";
    public const string TriggerEventDelete = "DELETE";
    public const string TriggerPositionHeader = "Position";
    public const string TriggerActive = "Active";
    public const string TriggerDetailLoadingHint = "Loading trigger…";
    public const string TriggerCompileTooltip = "Compile trigger (CREATE OR ALTER TRIGGER)";
    public const string TriggerCompileFailedFormat = "Compile failed: {0}";
    public const string TriggerModeToggleTooltip = "Toggle Source / Easy mode";
    public const string TriggerParseFailedNotice =
        "Couldn't parse the source into structured form — Easy mode is showing the last loaded state. Edit in Source mode, or fix the header.";
    public const string TriggerTableRequiredNotice = "Select the table the trigger fires for before compiling.";
    public const string TriggerEventRequiredNotice = "Select at least one event (INSERT / UPDATE / DELETE) before compiling.";
    public const string ToolbarNewTriggerTooltip = "New Trigger";
    public const string NewTriggerTabDefaultTitle = "New Trigger";
    public const string NewTriggerExecutedFormat = "Trigger \"{0}\" created.";

    public const string FieldEditCompileIcon = "⚡";
    public const string FieldEditCompileTooltip = "Compile pending changes (apply DDL + auto-commit)";
    public const string FieldEditDiscardTooltip = "Discard pending changes";
    public const string FieldEditAddIcon = "+";
    public const string FieldEditAddTooltip = "Add field";
    public const string FieldEditDropIcon = "−";
    public const string FieldEditDropTooltip = "Drop selected field";
    public const string FieldEditMoveUpIcon = "↑";
    public const string FieldEditMoveUpTooltip = "Move field up";
    public const string FieldEditMoveDownIcon = "↓";
    public const string FieldEditMoveDownTooltip = "Move field down";
    public const string FieldEditDropConfirmTitle = "Drop field";
    public const string FieldEditDropConfirmFormat = "Drop field \"{0}\"? The ALTER TABLE … DROP runs immediately in the active transaction — use Rollback to undo.";
    public const string FieldEditDropConfirmYes = "Drop";
    public const string FieldEditPendingHeader = "-- Pending changes:";
    public const string FieldEditCompileFailedFormat = "Compile failed: {0}";
    public const string FieldEditDescriptionAddFormat = "Add field {0}";
    public const string FieldEditDescriptionDropFormat = "Drop field {0}";
    public const string FieldEditDescriptionMoveFormat = "Move field {0} to position {1}";
    public const string FieldEditDescriptionRenameFormat = "Rename field {0} → {1}";
    public const string FieldEditDescriptionSetNotNullFormat = "Set NOT NULL on {0}";
    public const string FieldEditDescriptionDropNotNullFormat = "Drop NOT NULL on {0}";
    public const string FieldEditDescriptionSetDefaultFormat = "Set DEFAULT on {0}";
    public const string FieldEditDescriptionDropDefaultFormat = "Drop DEFAULT on {0}";
    public const string FieldEditDescriptionAlterTypeFormat = "Alter type of {0} to {1}";
    public const string FieldEditDescriptionCommentFormat = "Comment column {0}";
    public const string FieldEditRenameBlockedFormat = "Cannot rename {0} — column is referenced by other database objects.";

    public const string AddFieldDialogTitle = "Add Field";
    public const string AddFieldDialogEditTitleFormat = "Edit Field — {0}";
    public const string AddFieldRenameBlockedHint = "Renaming is disabled — this field has incoming dependencies (triggers / views / check constraints).";

    // Pola context menu + shortcuts
    public const string FieldsContextMenuAdd = "New field";
    public const string FieldsContextMenuEdit = "Edit field";
    public const string FieldsContextMenuDrop = "Delete field";
    public const string FieldsContextMenuCreateForeignKey = "Create foreign key…";
    public const string FieldEditEditIcon = "✎";
    public const string FieldEditEditTooltip = "Edit selected field (F2)";
    public const string FieldEditForeignKeyIcon = "⛓";
    public const string FieldEditForeignKeyTooltip = "Create foreign key…";

    // Field dependencies panel (Pola sub-tab, Session 4)
    public const string FieldDependenciesHeader = "Field dependencies";
    public const string FieldDependenciesNoSelection = "Select a field to see its dependencies.";
    public const string FieldDependenciesEmpty = "This field has no dependencies.";
    public const string FieldDependenciesColumnType = "Type";
    public const string FieldDependenciesColumnName = "Name";
    public const string FieldDependenciesColumnInsert = "Insert";
    public const string FieldDependenciesColumnUpdate = "Update";

    // Foreign Key wizard (Session 3 full implementation)
    public const string ForeignKeyDialogTitle = "Create Foreign Key";
    public const string ForeignKeyDialogHeader = "Create Foreign Key";
    public const string ForeignKeyDialogClose = "Close";
    public const string ForeignKeyDialogCreate = "Create";
    public const string ForeignKeyConstraintNameLabel = "Constraint name";
    public const string ForeignKeySourceTableLabel = "Source table";
    public const string ForeignKeySourceFieldsLabel = "Source fields";
    public const string ForeignKeyReferencedTableLabel = "Referenced table";
    public const string ForeignKeyReferencedFieldsLabel = "Referenced fields";
    public const string ForeignKeyReferencedFieldsHint = "Select fields in the same order as the source. Equal-named source fields are pre-selected automatically.";
    public const string ForeignKeyOnUpdateLabel = "ON UPDATE";
    public const string ForeignKeyOnDeleteLabel = "ON DELETE";
    public const string ForeignKeyActionNoAction = "NO ACTION";
    public const string ForeignKeyActionCascade = "CASCADE";
    public const string ForeignKeyActionSetNull = "SET NULL";
    public const string ForeignKeyDdlPreviewLabel = "DDL preview";
    public const string ForeignKeyDdlPreviewIncomplete = "-- Pick a referenced table and at least one field on each side to preview the DDL.";
    public const string ForeignKeyValidationConstraintNameRequired = "Constraint name is required.";
    public const string ForeignKeyValidationReferencedTableRequired = "Pick a referenced table.";
    public const string ForeignKeyValidationLocalFieldsRequired = "Pick at least one source field.";
    public const string ForeignKeyValidationReferencedFieldsRequired = "Pick at least one referenced field.";
    public const string ForeignKeyValidationFieldCountMismatch = "Source and referenced field counts must match.";
    public const string ForeignKeyExecuteFailedFormat = "Failed to create foreign key: {0}";

    // ─── Constraint management (Constraint Management Sprint V1) ──────────
    // Shared dialog chrome
    public const string ConstraintNameLabel = "Constraint name";
    public const string ConstraintFieldsLabel = "Fields";
    public const string ConstraintDialogCreate = "Create";
    public const string ConstraintDdlPreviewLabel = "DDL preview";
    public const string ConstraintDdlPreviewIncomplete = "-- Fill in the constraint name and select at least one field to preview the DDL.";
    public const string ConstraintValidationNameRequired = "Constraint name is required.";
    public const string ConstraintValidationFieldsRequired = "Select at least one field.";
    public const string ConstraintExecuteFailedFormat = "Failed to apply constraint change: {0}";
    // Primary Key / Unique field-picker dialog
    public const string PrimaryKeyDialogTitle = "Add Primary Key";
    public const string PrimaryKeyDialogHeader = "Add Primary Key";
    public const string UniqueDialogTitle = "Add Unique Constraint";
    public const string UniqueDialogHeader = "Add Unique Constraint";
    // Check dialog
    public const string CheckConstraintDialogTitle = "Add Check Constraint";
    public const string CheckConstraintDialogHeader = "Add Check Constraint";
    public const string CheckConstraintExpressionLabel = "CHECK condition";
    public const string CheckConstraintExpressionWatermark = "e.g. ID > 0  (or  CHECK (ID > 0))";
    public const string CheckConstraintValidationExpressionRequired = "Check expression is required.";
    // Context-menu actions
    public const string ConstraintMenuAddPrimaryKey = "Add Primary Key";
    public const string ConstraintMenuDropPrimaryKey = "Drop Primary Key";
    public const string ConstraintMenuAddForeignKey = "Add Foreign Key";
    public const string ConstraintMenuDropForeignKey = "Drop Foreign Key";
    public const string ConstraintMenuAddCheck = "Add Check Constraint";
    public const string ConstraintMenuDropCheck = "Drop Check Constraint";
    public const string ConstraintMenuAddUnique = "Add Unique Constraint";
    public const string ConstraintMenuDropUnique = "Drop Unique Constraint";
    // Drop confirmation
    public const string ConstraintDropConfirmTitle = "Drop constraint";
    public const string ConstraintDropConfirmFormat = "Are you sure you want to drop constraint '{0}'?";
    public const string ConstraintDropConfirmYes = "Drop";

    // Optional USING [ASC|DESC] INDEX clause for PK / UNIQUE (Constraint config).
    public const string ConstraintIndexNameLabel = "Index name (optional)";
    public const string ConstraintDescendingLabel = "Descending index";

    // Pola sub-tab: Drop Foreign Key context-menu entry (routes through the
    // shared Drop Constraint path; the FK constraint is resolved from the
    // selected field).
    public const string FieldsContextMenuDropForeignKey = "Drop Foreign Key";

    // ─── Index Management V1 ──────────────────────────────────────────────
    public const string IndexDialogTitle = "Add Index";
    public const string IndexDialogHeader = "Add Index";
    public const string IndexDialogCreate = "Create";
    public const string IndexNameLabel = "Index name";
    public const string IndexFieldsLabel = "Fields";
    public const string IndexUniqueLabel = "Unique";
    public const string IndexDescendingLabel = "Descending";
    public const string IndexComputedLabel = "Computed by (optional — expression index)";
    public const string IndexDdlPreviewLabel = "DDL preview";
    public const string IndexDdlPreviewIncomplete = "-- Fill in the index name and select at least one field (or enter a COMPUTED BY expression).";
    public const string IndexValidationNameRequired = "Index name is required.";
    public const string IndexValidationFieldsRequired = "Select at least one field, or enter a COMPUTED BY expression.";
    public const string IndexMenuAdd = "Add Index";
    public const string IndexMenuDrop = "Drop Index";
    public const string IndexDropConfirmTitle = "Drop index";
    public const string IndexDropConfirmFormat = "Are you sure you want to drop index '{0}'?";
    public const string IndexDropConfirmYes = "Drop";
    public const string IndexExecuteFailedFormat = "Failed to apply index change: {0}";
    // Shown when the user tries to drop an index that backs a PK / FK / UNIQUE
    // constraint — those are managed via the Ograniczenia tab.
    public const string IndexConstraintBackedFormat = "Index '{0}' backs a constraint — drop the constraint from the Ograniczenia tab instead.";
    // SET STATISTICS INDEX — recompute index selectivity (single + all).
    public const string IndexMenuRecomputeStatistics = "Recompute statistics";
    public const string IndexMenuRecomputeAllStatistics = "Recompute all statistics";
    public const string IndexStatsRecomputedOneFormat = "Recomputed statistics for index '{0}'.";
    public const string IndexStatsRecomputedAllFormat = "Recomputed statistics for {0} of {1} index(es).";
    public const string IndexStatsRecomputeFailedFormat = "Failed to recompute statistics for: {0} ({1})";

    // ─── Table description editing (Opis tab) ─────────────────────────────
    public const string TableDescriptionEditLabel = "Table description";
    public const string TableDescriptionSaveIcon = "💾";
    public const string TableDescriptionSave = "Save";
    public const string TableDescriptionClear = "Clear";
    public const string TableDescriptionSaveFailedFormat = "Failed to save description: {0}";

    public const string AddFieldFieldName = "Field name";
    public const string AddFieldNotNull = "Not Null";
    public const string AddFieldPrimaryKey = "Primary Key";
    public const string AddFieldTabDomain = "Domain";
    public const string AddFieldTabBasicType = "Basic type";
    public const string AddFieldTabDefault = "Default";
    public const string AddFieldTabCheck = "Check";
    public const string AddFieldTabComputed = "Computed by";
    public const string AddFieldTabAutoinc = "Autoincrement";
    public const string AddFieldTabDescription = "Description";
    public const string AddFieldTabDdl = "DDL";
    public const string AddFieldDomainLabel = "Existing domain";
    public const string AddFieldDomainHint = "Leave blank to use a basic type instead.";
    public const string AddFieldClearDomain = "Clear";
    // Sentinel label shown at the top of inline Domain combos so the user can
    // clear a previously-picked domain back to a basic type (#5). Real Firebird
    // domains can't be named with parentheses, so this never collides.
    public const string DomainNoneOption = "(none)";
    public const string AddFieldBasicTypeLabel = "SQL type";
    public const string AddFieldSizeLabel = "Size";
    public const string AddFieldPrecisionLabel = "Precision";
    public const string AddFieldScaleLabel = "Scale";
    public const string AddFieldBlobSubTypeLabel = "BLOB subtype";
    public const string AddFieldDefaultLabel = "Default value (raw SQL — e.g. 0, 'text', CURRENT_TIMESTAMP)";
    public const string AddFieldCheckLabel = "CHECK expression (e.g. VALUE > 0)";
    public const string AddFieldComputedLabel = "COMPUTED BY expression (e.g. ILOSC * CENA)";
    public const string AddFieldAutoincNone = "None";
    public const string AddFieldAutoincIdentity = "Use internal sequence (GENERATED BY DEFAULT AS IDENTITY)";
    public const string AddFieldAutoincExisting = "Use existing generator";
    public const string AddFieldAutoincNew = "Create new generator";
    public const string AddFieldGeneratorNameLabel = "Generator name";
    public const string AddFieldTriggerNameLabel = "Trigger name (auto-named when blank)";
    public const string AddFieldDescriptionLabel = "Column description";
    public const string AddFieldDialogOk = "OK";
    public const string AddFieldValidationNameRequired = "Field name is required.";
}
