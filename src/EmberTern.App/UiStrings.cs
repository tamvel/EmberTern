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
    // Generic metadata-tree context menu (Metadata Tree & Context Menu sprint).
    public const string MetadataContextNewFormat = "New {0}";
    public const string MetadataContextEdit = "Edit";
    public const string MetadataContextOpen = "Open";
    public const string MetadataContextDelete = "Delete";
    public const string MetadataContextExecuteProcedure = "Execute procedure";
    public const string MetadataContextActivate = "Activate";
    public const string MetadataContextDeactivate = "Deactivate";
    // Trigger-group Activate/Deactivate submenus are scoped by these — Visible (current filter set)
    // or All. ("Selected" moved onto the selected trigger leaves — see the *SelectedFormat below.)
    public const string MetadataContextScopeVisible = "Visible";
    public const string MetadataContextScopeAll = "All";
    // Shown directly on a selected trigger leaf's context menu when >1 trigger is multi-selected, so
    // the bulk op is reachable without scrolling back to the Triggers group header. {0} = count.
    public const string MetadataContextActivateSelectedFormat = "Activate selected ({0})";
    public const string MetadataContextDeactivateSelectedFormat = "Deactivate selected ({0})";
    public const string MetadataContextRecompileAllFormat = "Recompile all {0}s";
    public const string MetadataInactiveSuffix = " (inactive)";
    // Generic delete (all deletable kinds) — {0}=kind noun, {1}=object name.
    public const string MetadataDeleteObjectConfirmTitle = "Delete object";
    public const string MetadataDeleteObjectConfirmFormat = "Are you sure you want to delete {0} “{1}”? This cannot be undone.";
    public const string MetadataDeleteObjectConfirmYes = "Delete";
    public const string MetadataDeleteObjectExecutedFormat = "Deleted {0} “{1}”.";
    public const string MetadataDeleteObjectFailedFormat = "Could not delete {0} “{1}”: {2}";
    // Connection (database) node — database-wide operations.
    public const string ConnectionContextRefresh = "Refresh metadata";
    public const string ConnectionContextRecomputeStats = "Recompute statistics (all indexes)";
    public const string ConnectionContextRecompile = "Recompile all objects";
    // Bulk-operation execution + report.
    public const string BatchNothingToDo = "Nothing to do — every object is already in the requested state.";
    public const string BatchOpActivate = "Activate";
    public const string BatchOpDeactivate = "Deactivate";
    public const string BatchOpRecompile = "Recompile";
    public const string BatchOpRecompileHeader = "Recompile header";
    public const string BatchOpRecompileBody = "Recompile body";
    public const string BatchOpRecomputeStatistics = "Recompute statistics";
    public const string BatchOpSave = "Save";
    public const string BatchTitleActivateTriggers = "Activate triggers";
    public const string BatchTitleDeactivateTriggers = "Deactivate triggers";
    // "Selected" scope confirmation — {0} = number of selected triggers.
    public const string BatchConfirmActivateSelectedTitle = "Activate selected triggers";
    public const string BatchConfirmActivateSelectedFormat = "Activate {0} selected trigger(s)?";
    public const string BatchConfirmDeactivateSelectedTitle = "Deactivate selected triggers";
    public const string BatchConfirmDeactivateSelectedFormat = "Deactivate {0} selected trigger(s)?";
    public const string BatchTitleRecompileFormat = "Recompile {0}s";
    public const string BatchTitleRecompileAll = "Recompile all objects";
    public const string BatchTitleRecomputeStatistics = "Recompute index statistics";
    // Save-and-close / Save-and-disconnect: compile every dirty object editor (shared batch dialog).
    public const string SaveDirtyEditorsBatchTitle = "Saving changes";
    public const string SaveDirtyEditorsUnknownError = "Compilation failed.";
    public const string BatchResultsColumnObject = "Object";
    public const string BatchResultsColumnOperation = "Operation";
    public const string BatchResultsColumnResult = "Result";
    public const string BatchResultsColumnError = "Error";
    public const string BatchResultOk = "OK";
    public const string BatchResultFailed = "Failed";
    // Live footer: Processed / Total, Success, Failed, Duration (hh:mm:ss).
    public const string BatchResultsLiveSummaryFormat = "Processed: {0} / {1}    Success: {2}    Failed: {3}    Duration: {4}";
    public const string BatchResultsFilterLabel = "Show:";
    public const string BatchResultsFilterAll = "All";
    public const string BatchResultsFilterSuccess = "Success";
    public const string BatchResultsFilterFailed = "Failed";
    public const string BatchResultsCopyAll = "Copy All";
    public const string BatchResultsCopyFailed = "Copy Failed";
    public const string BatchResultsCancel = "Cancel";

    // ─── Script Executor ──────────────────────────────────────────────────────
    public const string ScriptExecutorTabTitle = "Script Executor";
    public const string ToolbarScriptExecutorTooltip = "Script Executor (migrations & multi-object DDL)";
    public const string ScriptRun = "Run";
    public const string ScriptRunTooltip = "Run the whole script in one transaction (F5)";
    public const string ScriptStopTooltip = "Stop after the current statement";
    public const string ScriptCommit = "Commit";
    public const string ScriptCommitTooltip = "Commit the open script transaction";
    public const string ScriptRollback = "Rollback";
    public const string ScriptRollbackTooltip = "Roll back the open script transaction";
    public const string ScriptTransactionLabel = "Transaction:";
    public const string ScriptModeManual = "Manual (review, then commit)";
    public const string ScriptModeAutoCommit = "Auto-commit on success";
    public const string ScriptStopOnError = "Stop on error";
    public const string ScriptOpenTooltip = "Open a .sql script…";
    public const string ScriptSaveTooltip = "Save the script to a .sql file…";
    public const string ScriptStatusOpenedFormat = "Opened {0}.";
    public const string ScriptStatusSavedFormat = "Saved {0}.";
    public const string ScriptStatusFileErrorFormat = "File error: {0}";

    // ─── Recompile Dependents (Part 2) ────────────────────────────────────────
    public const string RecompileDependentsTitle = "Recompile dependents";
    public const string RecompileDependentsHeaderFormat = "Recompile objects that depend on {0}?";
    public const string RecompileDependentsHint =
        "This change may affect the objects below. Nothing is recompiled unless you choose to.";
    public const string RecompileDependentsSelectAll = "Select all";
    public const string RecompileDependentsSelectNone = "Select none";
    public const string RecompileDependentsDontAskAgain = "Don't ask again this session";
    public const string RecompileDependentsRecompile = "Recompile selected";
    public const string RecompileDependentsSkip = "Skip";
    public const string RecompileDependentsBatchTitleFormat = "Recompile dependents of {0}";

    // ─── Smart SQL Parameters (Part 3) ────────────────────────────────────────
    // Shown in the parameter dialog's Type column when the type can't be resolved from the
    // catalog — we show "Unknown", never a guessed type (a plain text input is used).
    public const string SmartParamUnknownType = "Unknown";
    public const string ScriptTransactionOpenMarker = "● Transaction open — review, then Commit or Rollback";
    // Result grid column headers.
    public const string ScriptColumnLine = "#";
    public const string ScriptColumnStatement = "Statement";
    public const string ScriptColumnType = "Type";
    public const string ScriptColumnResult = "Result";
    public const string ScriptColumnRows = "Rows";
    public const string ScriptColumnDuration = "Duration";
    public const string ScriptColumnError = "Error";
    // Status line.
    public const string ScriptStatusReady = "Ready. Paste or type a script, then Run.";
    public const string ScriptStatusRunning = "Running…";
    public const string ScriptStatusNothingToRun = "Nothing to run — the script has no statements.";
    public const string ScriptStatusCancelled = "Cancelled. The transaction is still open — Commit or Rollback.";
    public const string ScriptStatusCommitted = "Committed.";
    public const string ScriptStatusRolledBack = "Rolled back.";
    public const string ScriptStatusParseErrorFormat = "Could not parse the script: {0}";
    public const string ScriptStatusDisallowedFormat =
        "Cannot run — remove the transaction-control / session statements: {0}";
    // Run gate: a transaction is already open and must be settled before a script runs.
    public const string ScriptBlockOwnTxOpen =
        "This script's previous run left a transaction open. Commit or Roll back (buttons above) before running again.";
    public const string ScriptBlockExternalTxOpen =
        "A transaction is already open (e.g. an uncommitted SQL Editor statement). Commit or roll back that transaction before running a script.";
    public const string ScriptStatusManualSummaryFormat =
        "{0} succeeded, {1} failed in {2}. Transaction open — Commit or Rollback.";
    public const string ScriptStatusAutoSummaryFormat = "{0} {1} succeeded, {2} failed in {3}.";
    public const string BatchResultsClose = "Close";
    // Preparation phase — the dialog opens here immediately so feedback is instant while
    // the object list + per-object SQL are still being built (Batch Operations UX sprint).
    public const string BatchPreparing = "Preparing operation…";
    public const string BatchPreparingBuildList = "Building operation list…";
    public const string BatchPreparingListFormat = "Loading {0}…";          // {0} = plural noun, count unknown
    public const string BatchPreparingLoadFormat = "Loading {0} {1} / {2}";  // e.g. "Loading procedures 143 / 1965"
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
    public const string TransactionLaneStartedFormat = "{0} transaction started.";
    public const string TransactionLaneCommittedFormat = "{0} transaction committed ({1} statement(s)).";
    public const string TransactionLaneRolledBackFormat = "{0} transaction rolled back ({1} statement(s)).";
    public const string TransactionCommitDataTooltip = "Commit data transaction";
    public const string TransactionRollbackDataTooltip = "Roll back data transaction";
    // Unified single-pair tooltips — the app commits/rolls back whichever lane(s) are open.
    public const string TransactionCommitTooltip = "Commit";
    public const string TransactionRollbackTooltip = "Roll back";
    // Execution-lane feedback: which profile the auto-router chose for a statement.
    // {0} = lane (Data/Metadata), {1} = profile label (e.g. "Read Committed").
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
    public const string UnsavedNewFunctionFormat = "New function (not yet created) — {0}";
    public const string UnsavedModifiedFunctionFormat = "Function {0} — uncompiled changes";
    public const string UnsavedNewGeneratorFormat = "New generator (not yet created) — {0}";
    public const string UnsavedModifiedGeneratorFormat = "Generator {0} — unsaved changes";
    public const string UnsavedNewDomainFormat = "New domain (not yet created) — {0}";
    public const string UnsavedModifiedDomainFormat = "Domain {0} — unsaved changes";
    public const string UnsavedNewPackageFormat = "New package (not yet created) — {0}";
    public const string UnsavedModifiedPackageFormat = "Package {0} — uncompiled changes";
    public const string UnsavedNewExceptionFormat = "New exception (not yet created) — {0}";
    public const string UnsavedModifiedExceptionFormat = "Exception {0} — unsaved changes";
    public const string UnsavedModifiedIndexFormat = "Index {0} — unsaved changes";
    public const string UnsavedPendingStructureFormat = "Table {0} — uncompiled structural changes";
    public const string UnsavedTransactionDataFormat = "Data transaction — {0} pending statement(s)";

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

    // Disconnect with unsaved metadata editors (Phase 1: Save / Discard / Cancel; default Save).
    public const string DisconnectSaveTitle = "Unsaved changes";
    public const string DisconnectSaveHeaderFormat = "Connection \"{0}\" has unsaved changes in these editors:";
    public const string DisconnectSaveQuestion = "Save them before disconnecting?";
    public const string DisconnectSaveConfirm = "Save and disconnect";
    public const string DisconnectSaveDiscard = "Discard and disconnect";

    // App close with unsaved work / active transactions (default Cancel; "Save and exit"
    // appears when there are unsaved editors to compile).
    public const string ExitUnsavedTitle = "Unsaved work";
    public const string ExitUnsavedIntro = "Exiting now will lose the following:";
    public const string ExitUnsavedTransactionNote = "Active transactions will be rolled back.";
    public const string ExitUnsavedSave = "Save and exit";
    public const string ExitUnsavedDiscard = "Discard and exit";
    public const string ExitUnsavedCancel = "Cancel";

    public const string BottomTabMessages = "Messages";
    public const string BottomTabResults = "Results";
    public const string BottomTabOutput = "Output";
    public const string BottomTabDiagnostics = "Diagnostics";

    // Diagnostics panel (Stage 7 / S4) — a view of the DiagnosticsEngine's findings for the SQL editor.
    public const string DiagnosticsEmptyHint = "No diagnostics — nothing to report for this document.";
    public const string DiagnosticsLocationFormat = "Ln {0}, Col {1}";
    public const string DiagnosticSeverityError = "Error";
    public const string DiagnosticSeverityWarning = "Warning";
    public const string DiagnosticSeverityInfo = "Info";

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
    public const string DeveloperModeDescription = "Lets you modify procedures, functions, triggers and other objects that are in use by active sessions: compiling waits for the object to be released instead of returning an error immediately.\n\nAffects how objects are compiled — not how your SQL runs. It applies when you compile an object in its editor, and when the Script Executor runs a script that only creates or changes objects. The SQL Editor is not affected: it runs every statement in your working transaction, which never waits, so a query or an update can never be left hanging on someone else's lock.";
    public const string DeveloperModeBadge = "DEV MODE";
    public const string DeveloperModeBadgeTooltip = "Developer Mode is on — compiling an object waits for other sessions to release it instead of failing immediately. Does not affect the SQL Editor.";

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
    // Tooltip on the single Execute button — surfaces the Shift+F5 full-read power path (Variant A+D:
    // one button, no split-button, no second Execute button).
    public const string ToolbarExecuteTooltip = "Execute  ·  F5 preview  ·  Shift+F5 all rows";
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

    // ── Copy as INSERT / UPDATE ───────────────────────────────────────────────
    // A disabled item here always carries a REASON (see SqlCopyReasonText): naming the actual obstacle
    // teaches the tool's model, and it is strictly more information than the alternative — generating
    // INSERT INTO TABLE_NAME (…) for the user to fix — could ever convey.
    public const string GridCopyAsInsert = "Copy as INSERT";
    public const string GridCopyAsUpdate = "Copy as UPDATE";
    public const string GridCopiedInsertLabel = "row as INSERT";
    public const string GridCopiedUpdateLabel = "row as UPDATE";
    public const string GridCopyNoRow = "Copy as SQL: right-click a data row first — no row is selected.";

    // The reasons. Three kinds of claim, and the wording keeps them apart on purpose: what the QUERY
    // cannot be, what EMBERTERN cannot do yet, and what is merely not ready.
    public const string SqlCopyUnavailablePrefix = "unavailable";
    public const string SqlCopyReasonSetOperation =
        "the result is a UNION, so EmberTern cannot tell which table each row came from";
    public const string SqlCopyReasonMultipleTablesFormat = "the result combines {0} tables ({1})";
    public const string SqlCopyReasonJoinFormat =
        "the result is a join ({0}), so a row is not one table's row";
    public const string SqlCopyReasonAggregate = "the rows are aggregates, not table rows";
    public const string SqlCopyReasonNoSourceTable = "no column in the result comes from a table";
    public const string SqlCopyReasonDuplicateColumnFormat =
        "{0} appears twice in the result";
    public const string SqlCopyReasonUnknownObjectFormat = "{0} is not in the catalog";
    public const string SqlCopyReasonNotATableFormat = "{0} is a {1}, not a table";
    public const string SqlCopyReasonViewFormat =
        "{0} is a view; EmberTern does not generate DML for views yet";
    // A CURRENT LIMITATION of EmberTern's analysis — never worded as a property of SQL.
    public const string SqlCopyReasonCte =
        "EmberTern cannot yet trace which table a CTE reads";
    public const string SqlCopyReasonNotUnderstood =
        "EmberTern could not analyse this statement";
    // TRANSIENT — the user's response is to wait, not to change anything.
    public const string SqlCopyReasonCatalogNotLoadedFormat =
        "{0}'s metadata is still loading";
    public const string SqlCopyReasonUnknownColumnFormat =
        "{0} is not in the cached metadata — commit the DDL, or reconnect to refresh it";
    public const string SqlCopyReasonNoPrimaryKeyFormat =
        "{0} has no primary key, so a single row cannot be identified";
    public const string SqlCopyReasonIncompletePkFormat =
        "this needs the complete primary key; {0} is not in the result";
    public const string SqlCopyReasonNoWritableColumnsFormat = "{0} has no writable columns here";
    public const string SqlCopyReasonKeyValueIsNullFormat = "{0} is NULL in this row and cannot identify it";
    public const string SqlCopyReasonValueNotRenderableFormat =
        "{0}'s value has no exact SQL literal, and EmberTern will not write an approximation";
    public const string SqlCopyReasonValueTooLargeFormat = "{0}'s value is too large for an SQL literal";
    public const string SqlCopyReasonStatementTooLongFormat =
        "the statement would exceed Firebird's size limit";
    // Context-menu toggle for grid column layout — when checked, columns auto-size to
    // content and manual widths aren't remembered; when unchecked, manual widths persist.
    public const string GridAutoFitColumns = "Auto-fit columns";

    public const string ResultsEmptyHint = "Run a query to see results.";
    public const string MessagesEmptyHint = "No messages yet.";
    public const string ExecutingStatus = "Executing query…";
    public const string CancellingStatus = "Cancelling…";
    // Live execution-timer indicator (SQL Editor / Execute Procedure/Function / Script Executor).
    // One cohesive label — {0} = mm:ss.f elapsed.
    public const string ExecutionElapsedFormat = "Elapsed: {0}";
    public const string NoConnectionMessage = "Connect to a database first.";
    public const string QueryCancelledMessage = "Query cancelled.";
    public const string AffectedRowsFormat = "{0} rows affected in {1} ms";
    // Truncated-Preview notice bar — loud + actionable (A.6). {0} = rows loaded so far
    // (thousands-separated — these strings front large full reads).
    public const string ResultsTruncatedFormat = "Showing the first {0:N0} rows — the full result is larger.";
    // Full hit the hard safety ceiling. {0} = ceiling row count.
    public const string ResultsCeilingFormat = "Stopped at {0:N0} rows — a safety limit, not the end of the result. Narrow the query to see the rest.";
    // Live counter shown in the status area while a Full / Load-all read streams. {0} = rows so far.
    public const string ResultsLoadingFormat = "Loading… {0:N0} rows";
    public const string ToolbarLoadAllRows = "Load all rows";
    // Smart soft-threshold prompt (Etap 2) — asked once mid-stream when a Full load crosses the soft
    // threshold and more rows remain. {0} = rows loaded so far.
    public const string LoadAllThresholdTitle = "Large result";
    public const string LoadAllThresholdMessageFormat = "Loaded {0:N0} rows so far and there's more. Keep loading the whole result into memory?";
    public const string LoadAllThresholdKeep = "Keep loading";
    public const string LoadAllThresholdStop = "Stop here";
    public const string RowsFetchedFormat = "{0:N0} rows in {1} ms";
    public const string MessagesCopyAll = "Copy all";
    public const string MessagesClear = "Clear messages";
    // {0} = current page, {1} = total pages, {2} = total rows in the result set.
    public const string ResultsPaginationHintFormat = "Page {0} of {1} · {2} rows";
    // Record position (IBExpert-style). {0} = 1-based absolute position of the
    // selected row in the full (sorted) result, {1} = total row count.
    public const string RecordPositionFormat = "Record {0} of {1}";
    // Shown when the grid has rows but none is selected. {0} = total row count.
    public const string RecordCountFormat = "{0} rows";
    // Preview variants — the true total is unknown (only the first N were loaded), so "N+" + a
    // "(preview)" marker makes the fragment unmissable even away from the notice bar. Thousands-
    // separated (preview counts can be large, e.g. a 250,000-row soft-stop).
    public const string RecordPositionPreviewFormat = "Record {0:N0} of {1:N0}+ (preview)";
    public const string RecordCountPreviewFormat = "{0:N0}+ rows (preview)";

    // ── Grid filtering + aggregation (shared across all data grids) ──
    // Operator labels (filter condition rows).
    public const string FilterOpEquals = "=";
    public const string FilterOpNotEquals = "≠";
    public const string FilterOpLessThan = "<";
    public const string FilterOpLessOrEqual = "≤";
    public const string FilterOpGreaterThan = ">";
    public const string FilterOpGreaterOrEqual = "≥";
    public const string FilterOpContains = "contains";
    public const string FilterOpStartsWith = "starts with";
    public const string FilterOpEndsWith = "ends with";
    public const string FilterOpIsNull = "is null";
    public const string FilterOpIsNotNull = "is not null";
    // Aggregate labels.
    public const string AggregateSum = "SUM";
    public const string AggregateAvg = "AVG";
    public const string AggregateCount = "COUNT";
    public const string AggregateCountDistinct = "COUNT DISTINCT";
    public const string AggregateMin = "MIN";
    public const string AggregateMax = "MAX";
    // Filter panel chrome.
    public const string FilterToggleTooltip = "Filter";
    public const string FilterPanelTitle = "Filter";
    public const string FilterAddCondition = "Add condition";
    public const string FilterApply = "Apply";
    public const string FilterClear = "Clear";
    public const string FilterMatchAll = "Match all (AND)";
    public const string FilterMatchAny = "Match any (OR)";
    public const string FilterEmptyHint = "No conditions — add one to filter the results.";
    public const string FilterRemoveConditionTooltip = "Remove condition";
    // Filter-from-cell context menu.
    public const string FilterByValue = "Filter by value";
    public const string FilterExcludeValue = "Exclude value";
    public const string FilterContainsValue = "Filter: contains…";
    // Aggregation bar chrome.
    public const string AggregationToggleTooltip = "Aggregations";
    public const string AggregationBarTitle = "Aggregations";
    public const string AggregationAddLine = "Add aggregate";
    // Placeholder on the function picker — picking a function adds the aggregate chip.
    public const string AggregationFunctionPlaceholder = "Add aggregate…";
    public const string AggregationEmptyHint = "No aggregates — add one to compute over the results.";
    public const string AggregationRemoveLineTooltip = "Remove aggregate";
    public const string AggregationRecomputeTooltip = "Recompute";
    public const string AggregationNullResult = "∅";
    public const string AggregationErrorResult = "error";

    // Main tab names — English (the app is English-language; the earlier
    // "keep Polish" choice was reversed 2026-07-02).
    public const string TableDetailTabFields = "Fields";
    public const string TableDetailTabConstraints = "Constraints";
    public const string TableDetailTabIndexes = "Indexes";
    public const string TableDetailTabDependencies = "Dependencies";
    public const string TableDetailDependsOnHeader = "Depends on";
    public const string TableDetailDependedOnByHeader = "Used by";
    public const string DependencyCategoryUdfs = "UDFs";
    public const string TableDetailDependencyType = "Type";
    public const string TableDetailDependencyName = "Name";
    public const string TableDetailDependencyField = "Field";
    public const string TableDetailTabData = "Data";
    public const string TableDetailTabDescription = "Description";
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

    public const string FolderNewTooltip = "New folder";
    public const string FolderNewIcon = "📁";
    public const string FolderNodeIcon = "📁";
    public const string FolderDialogTitle = "New folder";
    public const string FolderDialogNameLabel = "Folder name";
    public const string FolderDialogCreate = "Create";
    public const string FolderContextRename = "Rename";
    public const string FolderContextDelete = "Delete folder";
    public const string FolderDeleteConfirmTitle = "Delete folder";
    public const string FolderDeleteConfirmFormat = "Delete folder \"{0}\"? Connections in this folder will move back to the tree root.";
    public const string FolderDeleteConfirmYes = "Delete";
    public const string FolderDefaultName = "New folder";

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

    public const string ConnectionContextSort = "Sort nodes";
    public const string ConnectionContextSortAscending = "Ascending (A→Z)";
    public const string ConnectionContextSortDescending = "Descending (Z→A)";

    public const string FolderContextAddConnection = "Add connection";

    public const string QueryContextRename = "Rename";
    public const string QueryContextDelete = "Delete";
    public const string QueryRenameIcon = "✎";
    public const string QueryRenameTooltip = "Rename query";

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

    // ─── Package Detail ─────────────────────────────────────────────────────
    public const string PackageDetailTabPackage = "Package";
    public const string PackageDetailTabBody = "Body";
    public const string PackageDetailTabMembers = "Members";
    public const string PackageDetailTabDependencies = "Dependencies";
    public const string PackageDetailTabDescription = "Description";
    public const string PackageDetailTabDdl = "DDL";
    public const string PackageDetailDescriptionEmpty = "No description.";
    public const string PackageDetailMembersEmpty = "This package has no members.";
    public const string PackageDetailLoadingHint = "Loading package…";
    public const string PackageDetailDependsOnHeader = "Depends on";
    public const string PackageDetailDependedOnByHeader = "Used by";
    public const string ToolbarNewPackageTooltip = "New Package";
    public const string PackageCompileTooltip = "Compile package (header then body)";
    public const string PackageCompileHeaderFailedFormat = "Header compile failed: {0}";
    public const string PackageCompileBodyFailedFormat = "Body compile failed: {0}";
    public const string NewPackageTabDefaultTitle = "New Package";
    public const string NewPackageExecutedFormat = "Package \"{0}\" created.";
    public const string PackageDeleteConfirmTitle = "Delete package";
    public const string PackageDeleteConfirmFormat = "Are you sure you want to delete package {0}? This drops the header and its body.";
    public const string PackageDeleteConfirmYes = "Delete";

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

    // ─── Generator Detail ──────────────────────────────────────────────────
    public const string GeneratorDetailTabGenerator = "Generator";
    public const string GeneratorDetailTabDependencies = "Dependencies";
    public const string GeneratorDetailTabDdl = "DDL";
    public const string GeneratorNameHeader = "Name";
    public const string GeneratorCurrentValueHeader = "Current value";
    public const string GeneratorInitialValueHeader = "Initial value";
    public const string GeneratorIncrementHeader = "Increment";
    public const string GeneratorDescriptionHeader = "Description";
    public const string GeneratorLoadingHint = "Loading generator…";
    public const string GeneratorRefreshCurrentValueTooltip = "Refresh current value (re-read from database)";
    public const string ToolbarNewGeneratorTooltip = "New Generator";
    public const string GeneratorCompileTooltip = "Compile generator (CREATE / ALTER SEQUENCE)";
    public const string GeneratorCompileFailedFormat = "Compile failed: {0}";
    public const string GeneratorDeleteTooltip = "Delete generator";
    public const string GeneratorDeleteConfirmTitle = "Delete generator";
    public const string GeneratorDeleteConfirmFormat = "Drop generator \"{0}\"? This cannot be undone.";
    public const string GeneratorDeleteConfirmYes = "Delete";
    public const string NewGeneratorTabDefaultTitle = "New Generator";
    public const string NewGeneratorExecutedFormat = "Generator \"{0}\" created.";

    // ─── Exception Detail ──────────────────────────────────────────────────
    public const string ExceptionDetailTabException = "Exception";
    public const string ExceptionDetailTabDescription = "Description";
    public const string ExceptionDetailTabDependencies = "Dependencies";
    public const string ExceptionDetailTabDdl = "DDL";
    public const string ExceptionNameHeader = "Name";
    public const string ExceptionMessageHeader = "Message";
    public const string ExceptionDescriptionEditLabel = "Exception description";
    public const string ExceptionLoadingHint = "Loading exception…";
    public const string ToolbarNewExceptionTooltip = "New Exception";
    public const string ExceptionCompileTooltip = "Compile exception (CREATE / ALTER EXCEPTION)";
    public const string ExceptionCompileFailedFormat = "Compile failed: {0}";
    public const string ExceptionDeleteTooltip = "Delete exception";
    public const string ExceptionDeleteConfirmTitle = "Delete exception";
    public const string ExceptionDeleteConfirmFormat = "Drop exception \"{0}\"? This cannot be undone.";
    public const string ExceptionDeleteConfirmYes = "Delete";
    public const string NewExceptionTabDefaultTitle = "New Exception";
    public const string NewExceptionExecutedFormat = "Exception \"{0}\" created.";

    // ─── Index Detail ──────────────────────────────────────────────────────
    public const string IndexDetailTabIndex = "Index";
    public const string IndexDetailTabDdl = "DDL";
    public const string IndexNameHeader = "Name";
    public const string IndexTableHeader = "Table";
    public const string IndexConstraintTypeHeader = "Constraint type";
    public const string IndexConstraintTypeNoneWatermark = "— (plain index)";
    // Shown when the index backs a constraint, so it's immediately obvious WHY the
    // Active toggle and Drop action are disabled. {0} = PRIMARY KEY / UNIQUE / FOREIGN KEY.
    public const string IndexConstraintBackedNoteFormat =
        "Backs a {0} constraint — Firebird manages it through the constraint, so it can't be deactivated or dropped directly. Use Table Detail → Constraints.";
    public const string IndexFieldsHeader = "Fields";
    public const string IndexUniqueHeader = "Unique";
    public const string IndexSortDirectionHeader = "Sort direction";
    public const string IndexStatisticsHeader = "Statistics";
    public const string IndexStatisticsNoneWatermark = "(not computed)";
    public const string IndexActiveHeader = "Active";
    public const string IndexDescriptionHeader = "Description";
    public const string IndexLoadingHint = "Loading index…";
    public const string IndexNotFoundFormat = "Index \"{0}\" not found.";
    public const string IndexCompileTooltip = "Compile index changes (ALTER INDEX / COMMENT ON INDEX)";
    public const string IndexCompileFailedFormat = "Compile failed: {0}";
    public const string IndexRecomputeStatisticsTooltip = "Recompute statistics (SET STATISTICS INDEX)";
    public const string IndexDeleteTooltip = "Delete index";
    public const string IndexDeleteConfirmTitle = "Delete index";
    public const string IndexDeleteConfirmFormat = "Drop index \"{0}\"? This cannot be undone.";
    public const string IndexDeleteConfirmYes = "Delete";

    // ─── Domain Detail ───────────────────────────────────────────────────────
    public const string DomainDetailTabDomain = "Domain";
    public const string DomainDetailTabDescription = "Description";
    public const string DomainDetailTabUsedBy = "Used By";
    public const string DomainDetailTabDdl = "DDL";
    public const string DomainNameHeader = "Name";
    public const string DomainDataTypeHeader = "Data type";
    public const string DomainLengthHeader = "Length";
    public const string DomainPrecisionHeader = "Precision";
    public const string DomainScaleHeader = "Scale";
    public const string DomainSubTypeHeader = "Sub type";
    public const string DomainCharacterSetHeader = "Character set";
    public const string DomainCollationHeader = "Collation";
    public const string DomainDefaultHeader = "Default value";
    public const string DomainCheckHeader = "Check constraint";
    public const string DomainNotNullHeader = "Not null";
    public const string DomainLoadingHint = "Loading domain…";
    public const string ToolbarNewDomainTooltip = "New Domain";
    public const string DomainCompileTooltip = "Compile domain (CREATE / ALTER DOMAIN)";
    public const string DomainCompileFailedFormat = "Compile failed: {0}";
    public const string DomainRenamedFormat = "Domain renamed to \"{0}\".";
    public const string DomainDeleteTooltip = "Delete domain";
    public const string DomainDeleteConfirmTitle = "Delete domain";
    public const string DomainDeleteConfirmFormat = "Drop domain \"{0}\"? This cannot be undone.";
    public const string DomainDeleteConfirmYes = "Delete";
    public const string NewDomainTabDefaultTitle = "New Domain";
    public const string NewDomainExecutedFormat = "Domain \"{0}\" created.";

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

    // Performance sub-tab in Procedure/Function Detail — hosts the per-tab PerformancePanelView.
    public const string DetailTabPerformance = "Performance";

    // Heading above the per-table breakdown in the expanded exec-info panel.
    public const string ExecutionSummaryHeader = "Execution summary";

    // Clean styled line for a run that changed nothing (read-only) in the expanded exec-info panel
    // — reads/returned rows live in the collapsed header + the Performance tab, not here.
    public const string ExecutionSummaryNoChanges = "No data was changed by this execution.";
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

    // ─── Function Detail ────────────────────────────────────────────────────
    // Reuses the Procedure strings for the shared surface (mode toggle, Variables/
    // Cursors/Subprograms headers, Comment/Uncomment, exec-info, snippets, Dependencies
    // / DDL tab labels). Only the function-specific differences live here.
    public const string FunctionDetailArgumentsFormat = "Arguments ({0})";
    public const string FunctionDetailTabResult = "Result";            // Easy-mode return-type metadata
    public const string FunctionDetailExecuteResultTab = "Execute Result"; // runtime execution output
    public const string FunctionDetailReturnTypeLabel = "Return type";
    public const string FunctionDetailDeterministicLabel = "Deterministic";
    public const string FunctionDetailLoadingHint = "Loading function…";
    public const string FunctionCompileTooltip = "Compile function (CREATE OR ALTER FUNCTION)";
    public const string FunctionCompileFailedFormat = "Compile failed: {0}";
    public const string FunctionExecuteTooltip = "Execute function";
    public const string FunctionExecutedViaDataProfile = "Executed function via Data profile.";
    public const string FunctionResultRequiredNotice = "A function must declare a return type (Result).";
    public const string FunctionParseFailedNotice =
        "Couldn't parse the source into structured form — Easy mode is showing the last loaded state. Edit in Source mode, or fix the header.";
    public const string ToolbarNewFunctionTooltip = "New Function";
    public const string NewFunctionTabDefaultTitle = "New Function";
    public const string NewFunctionExecutedFormat = "Function \"{0}\" created.";
    public const string FunctionArgumentAddTooltip = "Add argument";
    public const string FunctionArgumentDeleteTooltip = "Delete argument";
    public const string FunctionArgumentMoveUpTooltip = "Move argument up";
    public const string FunctionArgumentMoveDownTooltip = "Move argument down";

    // New-subprogram kind prompt (Procedure / Function).
    public const string SubprogramKindDialogTitle = "New Subprogram";
    public const string SubprogramKindDialogPrompt = "Create a procedure or a function?";
    public const string SubprogramKindProcedure = "Procedure";
    public const string SubprogramKindFunction = "Function";

    // Merged Domain/Column picker (Faza 4) — one cell replacing the separate Domain +
    // TYPE OF columns. Tab 1 = the domain list; tab 2 = a table→column picker (TYPE OF COLUMN).
    public const string FieldTypeSourceHeader = "Domain / Column";
    public const string FieldTypeSourceDomainTab = "Domain";
    public const string FieldTypeSourceColumnTab = "Column";

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
    public const string ProcedureExecuteDialogHistoryLabel = "History";
    public const string ProcedureExecuteDialogHistoryEmpty = "No previous executions";
    public const string ProcedureExecuteDialogTimeWatermark = "HH:MM:SS";
    public const string ProcedureExecuteDialogTimeInvalid = "Invalid time format — use HH:MM:SS (e.g. 08:00:00).";

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
    // Confirmation before discarding the table designer's buffered structural changes —
    // an accidental click must not silently throw away uncompiled work.
    public const string FieldEditDiscardConfirmTitle = "Discard changes";
    public const string FieldEditDiscardConfirmFormat = "Discard all pending structural changes to \"{0}\"? Uncompiled changes will be lost.";
    public const string FieldEditDiscardConfirmYes = "Discard";

    // ─── Revert (View / Procedure / Trigger source editors) ─────────────────
    // The source-editor analog of the table designer's "discard pending changes":
    // reload the object from the database, throwing away uncompiled edits. The
    // confirmation guards against an accidental click losing work. Every object
    // editor must expose this button — see the editor contract in CLAUDE.md.
    public const string RevertChangesTooltip = "Revert changes (reload from database)";
    public const string RevertChangesConfirmTitle = "Revert changes";
    public const string RevertChangesConfirmFormat = "Discard your unsaved changes to \"{0}\" and reload it from the database? Uncompiled changes will be lost.";
    public const string RevertChangesConfirmYes = "Revert";
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
    // Per-object description-tab mini-headers — each editor names its own object type
    // (the shared TableDescriptionEditLabel said "Table description" everywhere, which
    // was wrong for the View/Procedure/Function/Trigger/Package editors).
    public const string ViewDescriptionEditLabel = "View description";
    public const string ProcedureDescriptionEditLabel = "Procedure description";
    public const string FunctionDescriptionEditLabel = "Function description";
    public const string TriggerDescriptionEditLabel = "Trigger description";
    public const string PackageDescriptionEditLabel = "Package description";
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

    // ─── Security Manager ──────────────────────────────────────────────────
    public const string SecurityManagerTabTitle = "Security Manager";
    public const string SecurityManagerTabTitleFormat = "Security · {0}";
    public const string SecurityTabUsers = "Users";
    public const string SecurityTabRoles = "Roles";
    public const string SecurityTabMembership = "Membership";
    public const string SecurityTabPrivileges = "Privileges";
    public const string SecurityGranteeUser = "User";
    public const string SecurityGranteeRole = "Role";

    // Common toolbar
    public const string SecurityAdd = "Add";
    public const string SecurityEdit = "Edit";
    public const string SecurityDelete = "Delete";
    public const string SecurityRefresh = "Refresh";
    public const string SecurityDeleteConfirm = "Delete";

    // Users pane
    public const string SecurityUsersHeader = "Server users";
    public const string SecurityUsersHint = "Users are global to the Firebird server (security database), not to this database.";
    public const string SecurityColUserName = "User name";
    public const string SecurityColFirstName = "First name";
    public const string SecurityColMiddleName = "Middle name";
    public const string SecurityColLastName = "Last name";
    public const string SecurityColActive = "Active";
    public const string SecurityColAdmin = "Admin";
    public const string SecurityColDescription = "Description";
    public const string SecurityColPlugin = "Plugin";
    public const string SecurityAddUser = "Add user";
    public const string SecurityEditUser = "Edit user";
    public const string SecurityDeleteUser = "Delete user";
    public const string SecurityDeleteUserTitle = "Delete user";
    public const string SecurityDeleteUserMessage = "Drop server user '{0}'? This removes the login from the Firebird server.";

    // Roles pane
    public const string SecurityRolesHeader = "Roles";
    public const string SecurityColRoleName = "Role name";
    public const string SecurityColOwner = "Owner";
    public const string SecurityAddRole = "Add role";
    public const string SecurityDropRole = "Drop role";
    public const string SecurityDropRoleTitle = "Drop role";
    public const string SecurityDropRoleMessage = "Drop role '{0}'? Memberships and grants for this role are lost.";
    public const string SecurityRoleDescriptionLabel = "Role description";
    public const string SecuritySaveDescription = "Save description";

    // Membership pane
    public const string SecurityMembershipHeader = "Role membership";
    public const string SecurityGranteeLabel = "Grantee";
    public const string SecurityColMember = "Member";
    public const string SecurityColAdminOption = "Admin option";
    public const string SecurityMembershipHint = "Click a cell to cycle: not a member → member → member with admin option. Admin option lets the member grant the role onward.";
    // Membership direction switch (feature A) + tri-state cell (feature B)
    public const string SecurityDirectionLabel = "Show";
    public const string SecurityDirectionMemberOf = "Member of";
    public const string SecurityDirectionMembers = "Members";
    public const string SecurityRolePickerLabel = "Role";
    public const string SecurityColMembership = "Membership";
    public const string SecurityColMemberName = "User / Role";
    public const string SecurityMembershipLegend = "✓ member     ✓+ with admin option     ·     click a cell to cycle";

    // Privileges pane
    public const string SecurityPrivilegesHeader = "Object privileges";
    public const string SecurityCategoryLabel = "Objects";
    public const string SecurityFilterWatermark = "Filter objects…";
    public const string SecurityWithGrantOption = "Grant with GRANT OPTION";
    public const string SecurityColObject = "Object";
    public const string SecurityColAll = "All";
    public const string SecurityColSelect = "Select";
    public const string SecurityColInsert = "Insert";
    public const string SecurityColUpdate = "Update";
    public const string SecurityColDelete = "Delete";
    public const string SecurityColReferences = "References";
    public const string SecurityColExecute = "Execute";
    public const string SecurityColUsage = "Usage";
    // Per-column header trio (grant / grant + option / revoke this privilege for all visible rows).
    public const string SecurityColGrantTip = "Grant to all visible objects";
    public const string SecurityColGrantOptionTip = "Grant to all visible objects with grant option";
    public const string SecurityColRevokeTip = "Revoke from all visible objects";
    public const string SecurityColumnsHeader = "Columns";
    public const string SecurityColumnsForFormat = "Columns — {0}";
    public const string SecurityColColumn = "Column";
    public const string SecurityColumnHint = "Select a table above to manage its column privileges.";

    // Privileges — bulk operations (row / column / all visible) + tri-state legend.
    // The same three glyphs appear at every scope: ✓ grant, ✓+ grant with grant option, ✕ revoke.
    public const string SecurityGrantGlyph = "✓";
    public const string SecurityGrantOptionGlyph = "✓+";
    public const string SecurityRevokeGlyph = "✕";
    public const string SecurityPrivilegeLegend = "✓ granted     ✓+ with grant option     ·     click a cell to cycle";
    // All-visible toolbar
    public const string SecurityBulkAllLabel = "All visible:";
    public const string SecurityBulkGrantAll = "Grant all";
    public const string SecurityBulkGrantAllOption = "Grant all + option";
    public const string SecurityBulkRevokeAll = "Revoke all";
    public const string SecurityBulkGrantAllTip = "Grant all privileges to every visible object";
    public const string SecurityBulkGrantAllOptionTip = "Grant all privileges (with grant option) to every visible object";
    public const string SecurityBulkRevokeAllTip = "Revoke all privileges from every visible object";
    // Row scope (hover trio + right-click menu)
    public const string SecurityRowGrantAll = "Grant all privileges";
    public const string SecurityRowGrantAllOption = "Grant all privileges with grant option";
    public const string SecurityRowRevokeAll = "Revoke all privileges";
    public const string SecurityRowGrantTip = "Grant all privileges to this object";
    public const string SecurityRowGrantOptionTip = "Grant all privileges (with grant option) to this object";
    public const string SecurityRowRevokeTip = "Revoke all privileges from this object";
    // Confirmation for the broadest destructive op
    public const string SecurityRevokeAllConfirmTitle = "Revoke all privileges";
    public const string SecurityRevokeAllConfirmFormat = "Revoke all privileges from {0} on all {1} visible object(s)? You can re-grant them afterward.";
    public const string SecurityRevokeAllConfirmYes = "Revoke all";

    // User dialog
    public const string SecurityUserDialogAddTitle = "Create user";
    public const string SecurityUserDialogEditTitle = "Edit user — {0}";
    public const string SecurityUserNameLabel = "User name";
    public const string SecurityPasswordLabel = "Password";
    public const string SecurityConfirmPasswordLabel = "Confirm password";
    public const string SecurityPasswordEditHint = "Leave blank to keep the current password.";
    public const string SecurityActiveLabel = "Active";
    public const string SecurityAdministratorLabel = "Administrator";
    public const string SecurityDialogOk = "OK";
    public const string SecurityDialogCancel = "Cancel";

    // Role dialog
    public const string SecurityRoleDialogTitle = "Create role";
    public const string SecurityRoleNameLabel = "Role name";
    public const string SecurityRoleNameWatermark = "Enter a new role name";

    // Tree context menu
    public const string MetadataContextNewUser = "Add user…";
    public const string MetadataContextNewRole = "Add role…";
    public const string MetadataContextOpenSecurity = "Open in Security Manager";
    public const string MetadataContextDeleteUser = "Delete user";
    public const string MetadataContextDropRole = "Drop role";

    // Toolbar (New User / New Role buttons)
    public const string ToolbarNewUserTooltip = "New user";
    public const string ToolbarNewRoleTooltip = "New role";
    public const string ToolbarSecurityManagerTooltip = "Security Manager (users, roles, privileges)";
    public const string ToolbarActivityMonitorTooltip = "Activity Monitor — live database trace";

    // ── Activity Monitor (Database Trace) ──
    public const string TraceMonitorTabTitle = "Activity Monitor";
    public const string TraceStart = "Start";
    public const string TraceStop = "Stop";
    public const string TracePauseResume = "Pause / Resume";
    public const string TracePause = "Pause";
    public const string TraceResume = "Resume";
    public const string TraceClear = "Clear";
    public const string TraceGroupNone = "Events";
    public const string TraceGroupTransaction = "Transactions";
    public const string TraceGroupStatement = "Statements";
    public const string TraceHideSelf = "Hide EmberTern's own activity";
    public const string TraceFollowTail = "Follow tail";
    public const string TraceShowOnlySelected = "Show only selected";
    public const string TraceFilterWatermark = "Search rows…";
    public const string TraceColSeq = "#";
    public const string TraceColTime = "Time";
    public const string TraceColDelta = "Δ ms";
    public const string TraceColEvent = "Event";
    public const string TraceColDuration = "Duration";
    public const string TraceColObject = "Object";
    public const string TraceColRows = "Rows";
    public const string TraceColReads = "Reads";
    public const string TraceColTx = "Tx";
    // Quick filter chips (All / Errors / Slow)
    public const string TraceFilterAll = "All";
    public const string TraceFilterErrors = "Errors";
    public const string TraceFilterSlow = "Slow";
    // Toolbar toggle tooltips
    public const string TraceHideSelfTip = "Hide EmberTern's own activity";
    public const string TraceFollowTailTip = "Follow tail — auto-scroll to the newest event";
    public const string TraceShowOnlySelectedTip = "Show only the selected transaction / statement";
    public const string TraceIncludeFunctionsTip = "Include function calls (built-in + user) — floods the stream; applies on next Start";
    public const string TraceDetailShowValuesTip = "Show parameter values inline in the SQL";
    public const string TraceDetailMaximizeTip = "Maximize / restore the detail panel";
    public const string TraceJumpLatest = "Jump to latest";
    // Event filter flyout (display-level; distinct from the source-level Include-Functions capture toggle)
    public const string TraceFilterEventsTip = "Filter events by type and operation";
    public const string TraceGridFilterTip = "Filter rows by column conditions (Duration > 100, Object contains …)";
    public const string TraceFilterSectionTypes = "Event types";
    public const string TraceFilterSectionOperations = "Operations (statements)";
    public const string TraceFilterStatements = "Statements";
    public const string TraceFilterProcedures = "Procedures";
    public const string TraceFilterTriggers = "Triggers";
    public const string TraceFilterFunctions = "Functions";
    public const string TraceFilterOpSelect = "SELECT";
    public const string TraceFilterOpInsert = "INSERT";
    public const string TraceFilterOpUpdate = "UPDATE";
    public const string TraceFilterOpDelete = "DELETE";
    public const string TraceFilterOpExecute = "EXECUTE";
    public const string TraceFilterOpDdl = "DDL";
    public const string TraceFilterReset = "Reset";
    // Detail sections
    public const string TraceDetailParameters = "Parameters";
    public const string TraceDetailTableAccess = "Table access";
    public const string TraceDetailTiming = "Timing";
    public const string TraceDetailSession = "Session";
    public const string TraceDetailCopySql = "Copy SQL";
    public const string TraceDetailOpenInEditor = "Open in SQL Editor";
    public const string TraceDetailNoSelection = "Select an event to see its detail.";
    public const string TraceEmptyHint = "Press Start to begin monitoring database activity.";
    public const string TraceEmptyWaiting = "Waiting for database activity…";
    public const string TraceEmptyPaused = "Paused — press Start to resume monitoring.";
    public const string TraceEmptyNoMatch = "No events match the current filter.";

    // Performance Analysis (Phase 1 — plan + timings)
    public const string PerformanceTabHeader = "Performance";
    public const string PerformanceRefresh = "Refresh";
    public const string PerformanceRefreshTooltip = "Re-analyze the last executed query (re-reads the plan; does not re-run the query)";
    public const string PerformanceProfilingHint = "Analyzing…";
    public const string PerformanceEmptyHint = "Execute a query, then open this tab to see its performance analysis.";
    // Primary plain-language summary (interpolated in PerformanceInsight)
    public const string PerformanceGradeFast = "Fast — this query ran in {0}.";
    public const string PerformanceGradeAcceptable = "This query ran in {0}.";
    public const string PerformanceGradeNeedsAttention = "Needs attention — this query took {0}.";
    public const string PerformanceGradeSlow = "Slow — this query took {0}.";
    public const string PerformanceGradeUnknown = "Executed.";
    public const string PerformanceLeadFullScanSingle = "It reads table {0} in full (a full table scan). A full scan reads every row, which is often why a query is slow.";
    public const string PerformanceLeadFullScanMultiple = "It reads tables {0} in full (full table scans). A full scan reads every row, which is often why a query is slow.";
    public const string PerformanceLeadNoFullScan = "All table access in the plan uses indexes — no full table scans.";
    // Measurement-derived lead (used instead of the plan heuristic once per-table reads exist,
    // so the summary always agrees with the Findings zone).
    public const string PerformanceMeasuredCostlyScanSingle = "It reads table {0} row by row (a full table scan) — the largest measured cost in this query.";
    public const string PerformanceMeasuredCostlyScanMultiple = "It reads tables {0} row by row (full table scans) — the largest measured cost in this query.";
    public const string PerformanceMeasuredNoCostlyScan = "No costly full table scans were measured — it read {0} rows to return {1}.";
    public const string PerformanceMeasuredNoCostlyScanChanges = "No costly full table scans were measured — it read {0} rows to change {1}.";
    public const string PerformanceNoiseSubqueriesSingle = "It also evaluates 1 sub-query (see the execution plan below).";
    public const string PerformanceNoiseSubqueriesMultiple = "It also evaluates {0} sub-queries (see the execution plan below).";
    public const string PerformanceForwardPointer = "Per-table read analysis — which confirms whether this is the cause — arrives in a later phase.";
    // Advanced (execution plan)
    public const string PerformancePlanAdvancedHeader = "Execution plan (advanced)";
    public const string PerformanceTimingLabel = "Timing";
    public const string PerformanceCaptureLabel = "Capture";
    public const string PerformancePlanDialectLabel = "Plan form";
    public const string PerformanceRawPlanLabel = "Raw plan";
    public const string PerformanceCopy = "Copy";
    // Phase 2 — measured per-table reads (Findings + Table Access zones)
    public const string PerformanceReadsNotMeasured = "This run wasn't measured for per-table reads. Re-run the query with this tab open to measure whether the full scan is actually costly.";
    public const string PerformanceFindingsHeader = "Findings";
    public const string PerformanceFindingsNone = "Per-table reads measured — no costly full scans found.";
    public const string PerformanceFindingsFuture = "Index recommendations and fix suggestions arrive in a later phase.";
    public const string PerformanceAccessHeader = "Table access";
    public const string PerformanceAccessLegend = "Red = sequential (full scan) reads · Blue = indexed reads";

    // ── Session Manager (live sessions / transactions / health) ──
    public const string SessionManagerTabTitle = "Session Manager";
    public const string ToolbarSessionManagerTooltip = "Session Manager — live sessions, transactions & database health";

    // toolbar
    public const string SessionManagerRefreshTip = "Refresh now";
    public const string SessionManagerAutoRefreshTip = "Auto-refresh interval";
    public const string SessionManagerDisconnectTip = "Disconnect session (hard — rolls back its work)";
    public const string SessionManagerCopyTip = "Copy selected session";
    public const string SessionManagerHideSelfTip = "Hide EmberTern's own sessions";
    public const string SessionManagerFilterWatermark = "Filter user / app / host…";
    public const string SessionManagerMaximizeTip = "Maximize / restore panel";

    // health bar
    public const string SessionManagerCountSessions = "Sessions";
    public const string SessionManagerCountTransactions = "Transactions";
    public const string SessionManagerCountLongTx = "Long Tx";
    public const string SessionManagerCountGcRisk = "GC Risk";
    public const string SessionManagerCountOatLag = "Tx Gap";
    public const string SessionManagerPrivilegeBanner =
        "Showing your own sessions only — connect as SYSDBA or a user with MONITOR ANY ATTACHMENT to see all sessions.";
    public const string SessionManagerGradeHealthy = "Healthy";
    public const string SessionManagerGradeWatch = "Watch";
    public const string SessionManagerGradeAtRisk = "At risk";

    // sessions grid
    public const string SessionColHealth = "Health";
    public const string SessionManagerHealthHealthy = "Healthy";
    public const string SessionManagerHealthWarning = "Warning — long-running transaction";
    public const string SessionManagerHealthGcRisk = "GC risk — blocking garbage collection";
    public const string SessionManagerHealthSelf = "EmberTern (this tool)";
    public const string SessionManagerHealthSystem = "System / internal (Firebird)";
    public const string SessionColId = "ID";
    public const string SessionColUser = "User";
    public const string SessionColApplication = "Application";
    public const string SessionColHost = "Host";
    public const string SessionColState = "State";
    public const string SessionColTx = "Tx";
    public const string SessionColOldestTx = "Oldest Tx";
    public const string SessionColLoad = "Load";
    public const string SessionsEmpty = "No sessions match the current filter.";

    // transactions tab
    public const string SessionManagerTabTransactions = "Transactions";
    public const string SessionManagerTransactionsFilteredFormat = "Filtered by session {0}";
    public const string TxColId = "Tx ID";
    public const string TxColSession = "Session";
    public const string TxColState = "State";
    public const string TxColAge = "Age";
    public const string TxColIsolation = "Isolation";
    public const string TxColReadOnly = "Read only";
    public const string TxColGcImpact = "GC impact";

    // session details tab (lightweight in M3)
    public const string SessionManagerTabDetails = "Session Details";
    public const string SessionManagerDetailsNoSelection = "Select a session to see its details.";
    public const string SessionManagerDetailStatement = "Current statement";
    public const string SessionManagerDetailNoStatement = "No active statement.";

    // warnings tab
    public const string SessionManagerTabWarnings = "Warnings";
    public const string SessionManagerNoWarnings = "No health issues detected.";
    public const string SessionManagerWarningWhatToCheck = "What to check";

    // session details (M4) — sections + plain-language diagnostics
    public const string SessionManagerGeneralHeader = "General";
    public const string SessionManagerActivityHeader = "Activity (since connect)";
    public const string SessionManagerRoleLabel = "Role";
    public const string SessionManagerConnectedLabel = "Connected";
    public const string SessionManagerActivitySeqReads = "Sequential reads";
    public const string SessionManagerActivityIdxReads = "Index reads";
    public const string SessionManagerActivityInserts = "Inserts";
    public const string SessionManagerActivityUpdates = "Updates";
    public const string SessionManagerActivityDeletes = "Deletes";
    public const string SessionManagerWhyHeader = "Why it matters";
    public const string SessionManagerWhyGc =
        "One of this session's transactions is the oldest active transaction in the database. Until it " +
        "finishes, Firebird must keep every row version created since it started — so obsolete versions " +
        "build up (database bloat) and reads gradually slow down. This is most often a reporting/BI " +
        "connection, or a screen left open mid-edit. Committing or ending that transaction lets garbage " +
        "collection catch up.";
    public const string SessionManagerWhyLongTx =
        "This session holds a transaction that has been open for a long time. A long-lived snapshot keeps " +
        "a stable view of the data, which holds back garbage collection of newer row versions. Committing " +
        "or restarting it releases that hold.";

    // integration bridges
    public const string SessionManagerOpenInEditor = "Open in SQL Editor";
    public const string SessionManagerOpenInEditorTip = "Open this statement in the SQL Editor as a new saved query";
    public const string SessionManagerAnalyze = "Analyze in Performance";
    public const string SessionManagerAnalyzeTip =
        "Open in the SQL Editor and reveal the Performance tab — run it (F5) to analyze (it is not run automatically)";
    public const string SessionManagerCurrentStatementHeader = "Current statement";

    // transactions grid — always-on Health dot (mirrors the Sessions grid)
    public const string SessionManagerTxHealthGcBlocker = "Blocking garbage collection — the oldest active transaction";
    public const string SessionManagerTxHealthLong = "Long-running transaction";
    public const string SessionManagerTxHealthNormal = "Normal transaction";

    // transaction-gap gauge (measured against the GC-danger budget — educate, don't alarm)
    public const string SessionManagerGapCaption = "Transaction gap";
    public const string SessionManagerGapExplain =
        "How far the oldest active transaction lags behind the newest — the record versions Firebird must keep from garbage collection. Shown against the point where it starts to matter.";
    public const string SessionManagerGapScaleMin = "0";
    public const string SessionManagerGapScaleMaxFormat = "GC risk near {0}";
    public const string SessionManagerGapStatusHealthy = "Well within the safe range.";
    public const string SessionManagerGapStatusWatch = "Getting large — check for a long-running transaction.";
    public const string SessionManagerGapStatusCritical = "Very large — a transaction is blocking garbage collection.";

    // context menu
    public const string SessionManagerMenuDisconnect = "Disconnect session";
    public const string SessionManagerMenuCopy = "Copy";

    // confirmations + status (VM)
    public const string SessionManagerDisconnectConfirmTitle = "Disconnect session";
    public const string SessionManagerDisconnectConfirmFormat =
        "Disconnect session {0} ({1})? Its uncommitted work will be rolled back.";
    public const string SessionManagerDisconnectConfirmYes = "Disconnect";
    public const string SessionManagerDisconnectDone = "Disconnect requested for session {0}.";
    public const string SessionManagerCopyHeaders = "ID\tUser\tApplication\tHost\tState\tTx\tOldest Tx\tLoad";
    public const string SessionManagerLastRefreshFormat = "Last refresh {0:HH:mm:ss}";

    // Global Search (Etap 3 — Search Results)
    public const string ToolbarGlobalSearchTooltip = "Global Search (Ctrl+Shift+F)";

    // Export DDL to .sql (portable object script — structure + comments, no grants).
    public const string ToolbarExportDdlTooltip = "Export DDL to .sql";
    public const string ExportDdlDialogTitle = "Export DDL to SQL file";
    public const string ExportDdlFilterName = "SQL scripts";
    public const string ExportDdlSucceededFormat = "Exported \"{0}\" to {1}.";
    public const string ExportDdlFailedFormat = "Export of \"{0}\" failed: {1}";

    // Data export (Export Framework) — the shared Export dialog + its entry points on the SQL
    // results grid (banner "Export all…", toolbar icon, right-click "Export…").
    public const string ExportDialogTitle = "Export";
    public const string ExportResultsMenuItem = "Export…";
    public const string ExportResultsTooltip = "Export results…";
    public const string ExportAllRowsButton = "Export all…";
    public const string ExportFormatLabel = "Format";
    public const string ExportFormatExcel = "Excel (.xlsx)";
    public const string ExportFormatCsv = "CSV (.csv)";
    public const string ExportFormatText = "Text (.txt)";
    public const string ExportFormatClipboard = "Clipboard";
    public const string ExportExcelFilterName = "Excel workbooks";
    public const string ExportScopeLabel = "Rows to export";
    public const string ExportScopeCurrentView = "Visible rows";
    public const string ExportScopeAllRows = "All rows";
    public const string ExportScopeSelected = "Selected";
    public const string ExportScopeCountFormat = "({0:N0})";
    public const string ExportScopeCountApproxFormat = "(~{0:N0})";
    public const string ExportOptionsLabel = "Options";
    public const string ExportDelimiterLabel = "Delimiter";
    public const string ExportDelimiterSemicolon = "Semicolon ( ; )";
    public const string ExportDelimiterComma = "Comma ( , )";
    public const string ExportDelimiterPipe = "Pipe ( | )";
    public const string ExportDelimiterTab = "Tab";
    public const string ExportEncodingUtf8Bom = "UTF-8 with BOM (Excel)";
    public const string ExportIncludeHeader = "Include header row";
    public const string ExportCultureInvariant = "Use invariant number / date format";
    public const string ExportButton = "Export";
    public const string ExportPreparing = "Preparing…";
    public const string ExportProgressFormat = "Exporting… {0:N0} rows";
    public const string ExportErrorFormat = "Export failed: {0}";
    public const string ExportCsvFilterName = "CSV files";
    public const string ExportTextFilterName = "Text files";
    public const string ExportDefaultFileName = "query_result";
    public const string ExportSavedFormat = "Exported {0:N0} rows to {1}.";
    public const string ExportCopiedFormat = "Copied {0:N0} rows to the clipboard.";
    public const string GlobalSearchDialogTitle = "Global Search";
    public const string GlobalSearchTermLabel = "Search for";
    public const string GlobalSearchTermWatermark = "text to find in metadata…";
    public const string GlobalSearchMatchNames = "In names";
    public const string GlobalSearchMatchSource = "In source";
    public const string GlobalSearchCaseSensitive = "Case sensitive";
    public const string GlobalSearchWholeWord = "Whole word";
    public const string GlobalSearchScopeHint =
        "Searches procedures, functions, triggers, views, packages, tables (and their fields), domains, generators and exceptions in the active connection.";
    public const string GlobalSearchDialogFind = "Find";
    public const string GlobalSearchDialogCancel = "Cancel";
    public const string GlobalSearchTabTitleFormat = "Search: {0}";
    public const string GlobalSearchSearching = "Searching…";
    public const string GlobalSearchNoResults = "No matches for '{0}'.";
    public const string GlobalSearchResultCount = "{0} object(s) matched '{1}'.";
    public const string GlobalSearchPreviewHint = "Select a result to preview its source.";
    public const string GlobalSearchPreviewError = "Could not load source: {0}";

    // Editor context menu + Find/Replace (Etap 1 — Global Search / Editor Find)
    public const string EditorMenuUndo = "Undo";
    public const string EditorMenuRedo = "Redo";
    public const string EditorMenuCut = "Cut";
    public const string EditorMenuCopy = "Copy";
    public const string EditorMenuPaste = "Paste";
    public const string EditorMenuSelectAll = "Select All";
    public const string EditorMenuFind = "Find…";
    public const string EditorMenuReplace = "Replace…";
    public const string EditorMenuComment = "Comment";
    public const string EditorMenuUncomment = "Uncomment";
    public const string EditorMenuFormat = "Format SQL";

    // ─── Debugger (Stage X / D4 — Debugger tab MVP) ───────────────────────────
    public const string MetadataContextDebugProcedure = "Debug procedure…";
    public const string DebuggerTabTitleFormat = "Debug: {0}";
    // Launch panel.
    public const string DebuggerLaunchHeader = "Launch debug session";
    public const string DebuggerLaunchParametersHeader = "Input parameters";
    public const string DebuggerLaunchNoParameters = "This routine takes no input parameters.";
    public const string DebuggerLaunchIsolationLabel = "Transaction isolation:";
    public const string DebuggerIsolationReadCommitted = "Read Committed (rec_version)";
    public const string DebuggerIsolationSnapshot = "Snapshot";
    public const string DebuggerIsolationNote =
        "The debug session runs in its own transaction (NOWAIT). It cannot see the SQL editor's " +
        "uncommitted data and may conflict with it. Everything is rolled back when the session ends.";
    public const string DebuggerLaunchButton = "Start debugging";
    public const string DebuggerLaunchPreparing = "Preparing…";
    // Pre-flight report (§9.2 / §4.6).
    public const string DebuggerPreflightHeader = "Before you start";
    public const string DebuggerPreflightClean = "No issues detected.";
    public const string DebuggerPreflightAutonomousTx =
        "Contains IN AUTONOMOUS TRANSACTION — work committed there is permanent and survives the debug rollback.";
    public const string DebuggerPreflightGenerator =
        "Uses a generator/sequence (GEN_ID / NEXT VALUE FOR) — generator values are consumed permanently and are not restored on rollback.";
    public const string DebuggerPreflightUnsteppable =
        "The routine source could not be parsed into step points — debugging cannot start.";
    // Toolbar / commands.
    public const string DebuggerContinueTooltip = "Continue (F5)";
    public const string DebuggerStepIntoTooltip = "Step Into (F11)";
    public const string DebuggerStepOverTooltip = "Step Over (F10)";
    public const string DebuggerStepOutTooltip = "Step Out (Shift+F11)";
    public const string DebuggerRunToCursorTooltip = "Run To Cursor (Ctrl+F10)";
    public const string DebuggerStopTooltip = "Stop debugging (Shift+F5)";
    public const string DebuggerRestartTooltip = "Restart (Ctrl+Shift+F5)";
    public const string DebuggerToggleBreakpointTooltip = "Toggle breakpoint (F9)";
    // Status line.
    public const string DebuggerStatusReady = "Ready to launch.";
    public const string DebuggerStatusPausedFormat = "Paused at line {0} — {1}";
    public const string DebuggerStatusRunning = "Running…";
    public const string DebuggerStatusCompleted = "Completed — transaction rolled back.";
    public const string DebuggerStatusFaultedFormat = "Unhandled exception: {0}";
    public const string DebuggerStatusStopped = "Stopped — transaction rolled back.";
    public const string DebuggerStatusLaunchFailedFormat = "Could not start the debug session: {0}";
    public const string DebuggerStopReasonEntry = "entry";
    public const string DebuggerStopReasonStep = "step";
    public const string DebuggerStopReasonBreakpoint = "breakpoint";
    // Variables panel.
    public const string DebuggerVariablesHeader = "Variables";
    public const string DebuggerVariablesEmpty = "No variables in the current frame.";
    public const string DebuggerVariablesColumnName = "Name";
    public const string DebuggerVariablesColumnValue = "Value";
    public const string DebuggerVariablesColumnKind = "Kind";
    public const string DebuggerVariableNull = "<null>";
    public const string DebuggerVariableKindParameter = "param";
    public const string DebuggerVariableKindLocal = "local";
    // Call stack (single-frame in D4, but the header exists).
    public const string DebuggerCallStackHeader = "Call stack";
    // Errors.
    public const string DebuggerNoConnection = "Connect to a database before debugging.";
    public const string ProcedureDebugTooltip = "Debug procedure";
    public const string DebuggerSourceUnavailableFormat = "Could not load the source of {0}.";
    // Expression evaluation — Evaluate / Immediate / Executed SQL (D5, spec §9.5 / §10.3).
    public const string DebuggerImmediateHeader = "Immediate / Executed SQL";
    public const string DebuggerImmediateWatermark = "Evaluate an expression against the current frame…";
    public const string DebuggerImmediateAsStatement = "as statement";
    public const string DebuggerImmediateAsStatementTooltip =
        "Run the text as a PSQL statement against the live frame (may assign variables). Off: evaluate it as an expression.";
    public const string DebuggerImmediateEvaluateButton = "Evaluate";
    public const string DebuggerImmediateClearTooltip = "Clear";
    public const string DebuggerEvaluateSelectionTooltip = "Evaluate the selected expression (Shift+F9)";
    public const string DebuggerImmediateEmpty = "No evaluations yet. Evaluate an expression, or select one in the source and press Shift+F9.";
    public const string DebuggerEvalKindExpression = "expression";
    public const string DebuggerEvalKindStatement = "statement";
    public const string DebuggerEvalStatementOk = "(statement ran)";
    public const string DebuggerEvalErrorUnknown = "evaluation failed";
    // Watches panel (D5 seam b, §9.5).
    public const string DebuggerWatchesHeader = "Watches";
    public const string DebuggerWatchWatermark = "Watch an expression…";
    public const string DebuggerWatchAddButton = "Add";
    public const string DebuggerWatchAddTooltip = "Add a watch (re-evaluated after every step)";
    public const string DebuggerWatchRemoveTooltip = "Remove watch";
    public const string DebuggerWatchesEmpty = "No watches. Add an expression to re-evaluate after every step.";
    public const string DebuggerWatchNotEvaluated = "—";
    public const string DebuggerWatchSideEffectTooltip =
        "This watch is not a pure expression — it runs real SQL in the debug transaction each time it is re-evaluated, and may have side effects.";
}
