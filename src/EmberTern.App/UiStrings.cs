using EmberTern.App.Commands;

namespace EmberTern.App;

/// <summary>
/// Every user-visible string in the application (architecture rule #6 — no resx).
///
/// <para>⚠ <b>A shortcut is never typed into a string here.</b> A handful of members are
/// <c>static readonly</c> rather than <c>const</c> because they name a keyboard gesture, and the gesture
/// comes from <see cref="CommandCatalog"/> through <see cref="CommandTip"/> — the text lives here, the key
/// lives there, and re-binding a shortcut updates every surface that mentions it. Etap 3 proved why: it
/// moved Format SQL to <c>Ctrl+K</c> and the hand-written tooltip went on teaching <c>Alt+F</c> forever,
/// with a green build. <c>UiStringsShortcutSourceTests</c> fails if a literal gesture reappears.</para>
/// </summary>
internal static class UiStrings
{
    public const string AppTitle = "EmberTern";
    public const string AppSubtitle = "Firebird Developer Workbench";

    // Shared MessageBanner (UX Polish Sprint / Seam 4) — the IDE's one message surface, so its
    // affordances are named once and read identically on every host (debugger, object editors,
    // Execute Procedure, Security Manager, …).
    public const string MessageBannerCopyTooltip = "Copy message";
    public const string MessageBannerExpandTooltip = "Show full message";
    public const string MessageBannerCollapseTooltip = "Collapse message";
    public const string MessageBannerDismissTooltip = "Dismiss";

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

    // ---- Data Import (etap I5: the tab, the frame, the readiness strip, Source & format) ----
    // Core returns codes only (rule #6); every sentence the module shows lives here.

    public const string DataImportTabTitle = "Data Import";
    public const string ToolbarDataImportTooltip = "Import data (clipboard, TXT, CSV, XLSX) into a table";

    // Section titles — also the readiness strip's chip labels.
    public const string ImportSectionSource = "Source";
    public const string ImportSectionFormat = "Format";
    public const string ImportSectionTarget = "Target";
    public const string ImportSectionMapping = "Mapping";
    public const string ImportSectionTransaction = "Transaction";

    // Source & format section.
    //
    // ⭐ The section splits by HOW OFTEN a decision changes, not by what it is about (U1/U5, ratified
    // 2026-07-26). The file (or clipboard) changes on every single run; the separator, encoding and date
    // format are set once and then survive for months. So the picker stays live at all times and only these
    // options fold away — otherwise the commonest action in the module (point at the next file) would cost
    // an expand and a collapse, and §1.2's promise that a repeat import is one F5 would be false.
    public const string ImportSourceHeader = "SOURCE";
    // Deliberately not "Format": that word also reads as "which format is this file", i.e. the source kind,
    // which is decided by the picker beside it. And not "Import parameters" either — the transaction mode and
    // the error policy are import parameters too, and they live in the command bar.
    public const string ImportFormatOptionsHeader = "Format options";
    public const string ImportFormatOptionsTooltip = "Separator, encoding, dates and number formats";
    public const string ImportSourceFile = "File";
    public const string ImportSourceClipboard = "Clipboard";
    public const string ImportSourceNoFile = "no file chosen";
    public const string ImportSourceBrowseTooltip = "Choose a file…";
    // Choosing the clipboard READS it — that is the whole point of it being a live source, and a control with a
    // side effect should say so rather than let the user discover it.
    public const string ImportSourceUseClipboardTooltip =
        "Use the clipboard as the source, and read it now · Ctrl+V re-reads it";
    public const string ImportParsingHeader = "Parsing";
    public const string ImportCultureHeader = "Data culture";
    public const string ImportDelimiterLabel = "Column separator";
    public const string ImportQuoteLabel = "Text qualifier";
    public const string ImportEncodingLabel = "Encoding";
    public const string ImportLineEndingLabel = "Line ending";
    public const string ImportAutoDetectLabel = "detect automatically";
    public const string ImportHasHeaderLabel = "First record holds column names";
    public const string ImportFirstDataRowLabel = "First data row";
    public const string ImportLastRowLabel = "Last row";
    // Never "2147483647" — an implementation detail in the UI is what §8 point 7 criticises.
    public const string ImportLastRowPlaceholder = "(to the end)";
    public const string ImportTrimWhitespaceLabel = "Trim whitespace around values";
    // ── Spreadsheet sources (etap I9). Shown only when the provider declares sheets. ──
    public const string ImportSheetLabel = "Sheet";
    public const string ImportDatesAsDatesLabel = "Treat date cells as dates (otherwise: Excel serial number)";

    public const string ImportNullTokenLabel = "NULL value";
    public const string ImportNullTokenPlaceholder = "(empty field)";
    public const string ImportDecimalSeparatorLabel = "Decimal separator";
    public const string ImportThousandsSeparatorLabel = "Thousands separator";
    public const string ImportDateOrderLabel = "Date format";
    public const string ImportDateSeparatorLabel = "Date separator";
    public const string ImportTimeSeparatorLabel = "Time separator";
    public const string ImportDelimiterTab = "Tab";
    public const string ImportSeparatorNone = "(none)";
    public const string ImportSeparatorSpace = "space";
    public const string ImportLineEndingAuto = "auto";
    public const string ImportChangeButton = "Change";

    // Detection evidence — an automatic decision that explains itself builds trust; a silent one does not.
    public const string ImportDelimiterEvidenceFormat = "{0}/{1} records have the same field count ({2} fields)";
    public const string ImportEncodingEvidenceBom = "byte-order mark";
    public const string ImportEncodingEvidenceAscii = "pure ASCII — the file does not distinguish encodings";
    public const string ImportEncodingEvidenceHeuristic = "no BOM → heuristic over the file's bytes";

    public const string ImportSummaryDelimiterFormat = "\"{0}\"";
    public const string ImportSummaryNoHeader = "no header";
    public const string ImportFileFactsFormat = "{0:N1} KB · {1:g}";
    public const string ImportFileMissing = "file not found";
    // The clipboard's facts carry the READ TIME, not a last-write time it does not have: for a live source that
    // is the question ("is what I see still what I copied?"), and it is also what makes a refresh visibly
    // acknowledge itself when the pasted content happens to be identical.
    public const string ImportClipboardFactsFormat = "clipboard: {0} lines · {1:N1} KB · read {2:T}";
    public const string ImportClipboardEmpty = "clipboard is empty";
    // (There is deliberately no "format not supported yet" string any more. It refused .xls until etap I10 gave
    // that format a provider, and every source kind the surface can resolve now has one — a message for a state
    // that can no longer occur is worse than none, because the next reader of the code has to work out when it
    // fires before discovering it never does.)

    // Readiness strip.
    public const string ImportReadinessHeader = "Ready:";
    public const string ImportReadySummary = "Ready to import";
    public const string ImportReadySummaryWithRowsFormat = "Ready to import — {0:N0} rows previewed";
    public const string ImportReadyBlocked = "Not ready yet";

    public const string ImportReadyNoSource = "No source chosen — pick a file or paste from the clipboard.";
    public const string ImportReadySourceMissingFormat = "The file is gone: {0}";
    public const string ImportReadySourceUnreadable = "The source could not be read.";
    public const string ImportReadySourceHasNoFields = "The source has no fields yet.";
    public const string ImportReadySourceOptionsMismatch = "The format settings do not match this kind of source.";
    public const string ImportReadyNoTarget = "No target table chosen.";
    public const string ImportReadyTargetNotFoundFormat = "Table {0} is not in this database.";
    public const string ImportReadyNewTableHasNoColumns = "The new table has no columns defined.";
    public const string ImportReadyNewTableWillBeCommittedFormat =
        "Table {0} will be created and COMMITTED before any row is written — a rollback will not remove it.";
    // Refused here rather than by the engine: the CREATE is the first thing the run does, so without this the
    // user would meet a raw server error immediately after being told everything was ready (§0).
    public const string ImportReadyNewTableAlreadyExistsFormat =
        "A table named {0} already exists — choose another name, or import into the existing table.";
    public const string ImportReadyBeforeInsertTriggersFormat =
        "{0} BEFORE INSERT trigger(s) on the target can overwrite imported values.";
    public const string ImportReadyTargetWillBeEmptiedFormat = "{0} will be emptied before the import.";
    public const string ImportReadyNothingMapped = "No column is mapped — there is nothing to import.";
    public const string ImportReadyRequiredColumnNotMappedFormat =
        "Column {0} is NOT NULL with no default and is unmapped — every row would fail.";
    public const string ImportReadyUnsupportedColumnTypeFormat = "Column {0} has a type this build cannot write.";
    public const string ImportReadyColumnsNotMappedFormat = "{0} target column(s) not mapped.";
    public const string ImportReadyFieldsUnusedFormat = "{0} source field(s) unused.";
    public const string ImportReadyAmbiguousNameFormat = "Two source fields match column {0} — pick one.";
    public const string ImportReadyMappingDroppedFormat = "Field {0} no longer exists; its mapping was dropped.";
    public const string ImportReadyColumnNotWritableFormat = "Column {0} is computed and can never be written.";
    public const string ImportReadyIdentityOverrideFormat =
        "Column {0} is GENERATED ALWAYS — the INSERT will carry OVERRIDING SYSTEM VALUE.";
    public const string ImportReadyPairingAssumedFormat = "Column {0} was paired by position — worth a look.";
    public const string ImportReadyNotConnected = "Not connected.";
    public const string ImportReadyUserTransactionOpen =
        "A working transaction is open — commit or roll it back before importing.";
    public const string ImportReadyBatchedNotAtomicFormat =
        "Batched mode is NOT atomic: it commits every {0:N0} rows, and a committed batch stays applied.";
    public const string ImportReadyTrimmingEnabled =
        "Value trimming is on — over-long values will be SHORTENED, and every one is reported.";
    public const string ImportReadyLongTransactionFormat =
        "About {0:N0} rows in one transaction — it will stay open for a while. Consider Batched mode.";
    public const string ImportReadyNotRepresentableFormat =
        "{0} sampled value(s) carry characters this connection's charset cannot store — connect in UTF8 to keep them.";

    // The readiness strip's ceiling (U6). The chips carry §3.2's "every gap at once"; this only caps how
    // many findings are spelled out, so the strip cannot take the whole surface exactly when there is most
    // to fix.
    public const string ImportReadyMoreItemsFormat = "… and {0} more problem(s)";
    public const string ImportReadyShowFewer = "Show fewer";
    public const string ImportReadyExpandTooltip = "Show every finding";
    /// <summary>A chip is a status light AND a way in — so it says which, rather than leaving the user to
    /// guess whether it is a filter, a tab, a shortcut or an indicator (§3.2).</summary>
    public const string ImportReadyChipHintFormat = "Go to {0}";
    public const string ImportReadyChipFormatHint = "Show or hide the format options";

    // Band H, left half — where the rows land. The lane is a constant because it is one: rows always go to
    // the Data lane as the one user working transaction (§4.5).
    public const string ImportDestinationFormat = "{0} · {1} lane";
    /// <summary>Band H once the command bar exists: where the rows land, on which lane, and what then happens
    /// to the transaction (§3.1).</summary>
    public const string ImportDestinationWithModeFormat = "{0} · {1} lane · transaction: {2}";
    public const string ImportDestinationDataLane = "Data";
    public const string ImportDestinationNotConnected = "Not connected";

    // The work area's empty state. Every area on this surface names the NEXT STEP rather than reporting an
    // absence (§9.4) — "no data" tells the user something they can already see.
    public const string ImportWorkAreaEmpty =
        "Choose a target table to map its columns. The mapping grid and the converted preview appear here.";

    // ---- Data Import, etap I6: the Target tile (§3.4) and the Mapping panel (§3.5) ----

    public const string ImportTargetExistingTable = "Existing table";
    public const string ImportTargetTableWatermark = "Choose a table…";
    public const string ImportTargetFilterWatermark = "Type to filter…";
    public const string ImportTargetColumnsFormat = "{0} columns";
    public const string ImportTargetNoPrimaryKey = "primary key: none";
    public const string ImportTargetPrimaryKeyFormat = "primary key: {0}";
    // Triggers are NAMED, not counted: a count says something is there, the names say what will rewrite the
    // values on the way in (R6).
    public const string ImportTargetNoBeforeInsertTriggers = "BEFORE INSERT triggers: none";
    public const string ImportTargetBeforeInsertTriggersFormat = "BEFORE INSERT triggers: {0}";
    public const string ImportTargetEmptyFirst = "Empty the table before importing";
    public const string ImportTargetEmptyFirstTooltip =
        "DELETE FROM in the SAME transaction as the rows — a rollback takes the deletion with it.";

    // ---- Data Import, etap I8: a table that does not exist yet (§3.4) ----

    public const string ImportTargetNewTable = "New table";
    public const string ImportTargetNewTableWatermark = "Name for the new table…";

    // ⚠ §0.5 / gotcha #213 — the module's most important honest sentence ("the CREATE is committed before the
    // first row, so a rollback cannot take the table with it") is NOT here any more. It lives in exactly one
    // place: Core's IMP0018, rendered by the readiness strip, which additionally names the table. There used to
    // be a second copy as a banner under the type grid, and saying one fact twice on one screen is how a warning
    // stops being read. If it ever needs to be louder, make the strip louder — do not add a second sentence.
    public const string ImportNewTableDropOnFailure = "Drop the table if the import fails";
    public const string ImportNewTableDropOnFailureTooltip =
        "On failure: roll back the imported rows, then DROP the table on the DDL connection. You are asked first.";
    /// <summary>The DDL bottom tab — shown only in the „new table" variant, because in the other one there is
    /// no statement to generate and a permanently empty tab is a promise nothing keeps.</summary>
    public const string ImportDdlTab = "DDL";

    /// <summary>Said inside the tab, once: it is regenerated from the grid above, so it can be read as current
    /// rather than as something that had to be refreshed.</summary>
    public const string ImportDdlLive =
        "Generated from the types above — this is the statement the import will run.";

    public const string ImportDdlEmpty = "Name the new table and choose a source — the CREATE TABLE appears here.";

    // The type grid.
    public const string ImportNewTableColumnName = "Column";
    public const string ImportNewTableColumnType = "Type";
    public const string ImportNewTableColumnSize = "Size";
    public const string ImportNewTableColumnScale = "Scale";
    public const string ImportNewTableColumnNullable = "NULL";
    public const string ImportNewTableColumnBasis = "Basis";
    public const string ImportNewTableEmpty =
        "Name the new table and choose a source — its columns are proposed from the file, and you can correct every type before anything is created.";

    // ⭐ Always visible (§3.4): the types are worth exactly as much as the evidence behind them, and REK-7 makes
    // that evidence the WHOLE source rather than a sample.
    public const string ImportNewTableInferenceFormat = "Types inferred from {0:N0} rows analysed — editable:";
    public const string ImportNewTableInferenceTruncatedFormat =
        "Types inferred from the first {0:N0} rows (safety limit reached) — editable:";

    // The "Basis" cell — why this column has this type.
    public const string ImportNewTableBasisNoValues = "no values — text";
    public const string ImportNewTableBasisTextFormat = "text, {0:N0} values, longest {1}";
    public const string ImportNewTableBasisMatchedFormat = "{0:N0} values, all {1}";
    // R19: a mixed column is the norm, not the exception — so it names the value that decided it and the row
    // the user can open their file at (§0.6).
    public const string ImportNewTableBasisMixedFormat =
        "mixed — {0} until row {1} “{2}”; text, longest {3}";
    public const string ImportNewTableBasisRestored = "from the restored configuration";

    public const string ImportNewTableKindInteger = "whole numbers";
    public const string ImportNewTableKindDecimal = "decimals";
    public const string ImportNewTableKindDate = "dates";
    public const string ImportNewTableKindTimestamp = "dates with time";
    public const string ImportNewTableKindTime = "times";
    public const string ImportNewTableKindBoolean = "true/false";
    public const string ImportNewTableKindText = "text";

    // Creating and dropping.
    public const string ImportCreatingTableFormat = "Creating table {0}…";
    public const string ImportCreatedTableFormat = "Table {0} created and committed.";
    public const string ImportCreateTableFailedFormat = "Table {0} could not be created: {1}";
    /// <summary>⚠ Its own heading. The shared confirmation used to be titled "Empty the table before importing"
    /// for every question the module asked, so this one appeared under the name of a different action.</summary>
    public const string ImportConfirmDropTableTitle = "Drop the created table";
    public const string ImportConfirmDropTableConfirm = "Drop table";
    public const string ImportConfirmDropTableFormat =
        "The import into {0} did not succeed.\n\nRoll back the imported rows and DROP the table?\n\n" +
        "The table was committed when it was created, so this is the only way to remove it.";
    public const string ImportDroppedTableFormat = "Table {0} dropped.";
    public const string ImportDropTableFailedFormat = "Table {0} could not be dropped: {1}";
    // §0.5 / §0.6 — the report never leaves the created table unsaid, whether or not it was dropped.
    public const string ImportReportCreatedTableFormat = "created table {0} (a rollback does not remove it)";

    // Mapping panel.
    public const string ImportMappingHeadlineFormat = "Mapped {0} of {1} columns.";
    public const string ImportMappingMatchByPosition = "Match by position";
    public const string ImportMappingMatchByPositionTooltip =
        "Pair column 1 with field 1, and so on — for a source whose names say nothing";
    public const string ImportMappingClear = "Clear";
    public const string ImportMappingOnlyUnmapped = "Only unmapped";
    public const string ImportMappingDoNotImport = "— do not import —";
    public const string ImportMappingFieldLabelFormat = "{0}  {1}";
    public const string ImportMappingUnusedFieldsFormat = "Source fields nobody uses: {0}";
    public const string ImportMappingColumnTarget = "Target column";
    public const string ImportMappingColumnSource = "Source field";
    public const string ImportMappingColumnType = "Target type";
    public const string ImportMappingColumnNote = "Note";
    public const string ImportMappingEmpty =
        "Choose a target table above — its columns will appear here, already matched by name where the names agree.";

    // Why a column's picker is disabled. A blocked control that does not say why is a UX defect (§9.1.3).
    public const string ImportMappingLockedComputed = "COMPUTED BY — Firebird rejects an INSERT naming it.";
    public const string ImportMappingLockedUnsupportedFormat = "Type {0} is not supported by the import.";
    public const string ImportMappingLockedIdentity =
        "Identity GENERATED ALWAYS — tick to override it; the INSERT then carries OVERRIDING SYSTEM VALUE.";
    public const string ImportMappingUnlockIdentity = "override";

    // Mapping origin (§9.3 — the debugger's ValueOrigin vocabulary, reused rather than reinvented).
    public const string ImportMappingOriginMatched = "matched";
    public const string ImportMappingOriginAssumed = "assumed";

    // Bottom panel + surface status.
    public const string ImportSourcePreviewTab = "Source preview";
    public const string ImportSourcePreviewEmpty = "Choose a file or paste from the clipboard.";
    public const string ImportSourcePreviewRaggedTooltip =
        "This record has a different number of fields than the header — usually a wrong column separator.";
    public const string ImportRowNumberColumn = "#";
    /// <summary>Gutter marker for a record whose field count disagrees with the rest of the file (§3.6).</summary>
    public const string ImportRaggedMarker = "⚠";
    public const string ImportSurfaceStatusNoSource = "No source yet.";
    public const string ImportSurfaceStatusFormat = "{0} fields · {1:N0} rows previewed{2}";
    public const string ImportSurfaceStatusMore = "+";
    public const string ImportBottomPanelToggleTooltip = "Collapse / expand the bottom panel";

    // ---- Data Import, etap I7: the command bar (§3.1 band B), the run, and the report (§3.7) ----

    public const string ImportRun = "Import";
    public static readonly string ImportRunTooltip = CommandTip.For(
        CommandId.Go, "Read the source and write the rows into the target table");
    public const string ImportValidate = "Validate";
    public static readonly string ImportValidateTooltip = CommandTip.For(
        CommandId.ImportValidate,
        "Run everything except the write — same pipeline, same conversion, same checks");
    public const string ImportCancel = "Cancel";
    public const string ImportCancelTooltip = "Stop after the current batch · Esc";

    // Refresh names what it does to the WORLD, not to the screen: it re-reads every fact the surface holds. The
    // tooltip lists the cases because that is what makes the button discoverable — a bare "Refresh" leaves the
    // user guessing what exactly gets re-read. (Icon only, so there is deliberately no label constant: the
    // shared refresh mark already carries the meaning, and the command bar has no room to spare.)
    // ⚠ Ctrl+V stays literal here, and it is the one deliberate exception: it is not a catalog command (it
    // means "re-read the clipboard SOURCE", i.e. paste semantics that must yield to a focused text box), so
    // there is no descriptor to read it from. Ctrl+R comes from the catalog like every other gesture.
    public static readonly string ImportRefreshTooltip = CommandTip.For(
        CommandId.ImportRefresh,
        "Read the source, the table list and the target again, then recompute everything: mapping, readiness and "
        + "the preview. Use it when the file has changed on disk, the clipboard now holds something else, or a "
        + "table has been added or dropped")
        + " (Ctrl+V re-reads the clipboard)";
    public const string ImportRunCancelled = "Cancelled. Rows already written stay in the open transaction.";

    public const string ImportTransactionLabel = "Transaction";
    public const string ImportTransactionManual = "Manual";
    public const string ImportTransactionAutoCommit = "Commit on success";
    public const string ImportTransactionBatched = "Batched";
    public const string ImportTransactionManualDescription =
        "The transaction stays open. You commit or roll back after reading the report.";
    public const string ImportTransactionAutoCommitDescription =
        "Commits automatically when every row went in and nothing was cancelled; otherwise it stays open for you.";
    public const string ImportTransactionBatchedDescriptionFormat =
        "Commits every {0:N0} rows — NOT atomic: a later failure cannot roll back what was already committed.";

    public const string ImportErrorPolicyLabel = "Errors";
    public const string ImportErrorPolicyStop = "Stop at the first";
    public const string ImportErrorPolicySkip = "Skip the row and continue";

    public const string ImportProgressFormat = "{0:N0} read · {1:N0} written · {2:N0} failed";

    public const string ImportConfirmEmptyFormat =
        "Empty table {0} before importing? The DELETE runs in the same transaction, so Rollback takes it back too.";
    public const string ImportConfirmEmptyCountFormat =
        "This deletes {0:N0} row(s) from {1} before importing. The DELETE runs in the same transaction, so Rollback takes it back too.";

    // The converted preview (§3.6).
    public const string ImportMappingTitle = "Mapping";

    /// <summary>Title of the work area's left half in the „new table" variant — the columns about to be
    /// created. It names a CONFIGURATION subject, which is why it lives beside Mapping rather than beside the
    /// preview: the work area is where the import is designed, the bottom panel is where results land.</summary>
    public const string ImportNewTableTypesTitle = "Table types";

    public const string ImportPreviewTitle = "Preview after conversion";
    public const string ImportPreviewHeadlineFormat = "{0:N0} row(s) after conversion — this is what reaches the database.";
    public const string ImportPreviewHeadlineProblemsFormat =
        "{0:N0} row(s) after conversion — {1:N0} would be rejected. Failed rows show their RAW values.";
    public const string ImportPreviewEmpty =
        "Choose a source and a target table, and map at least one column — the converted rows appear here.";
    public const string ImportPreviewFailedTooltip = "This row would be rejected; the values shown are the raw ones.";

    // The Errors / Report bottom tabs (§3.1 band G).
    public const string ImportErrorsTab = "Errors";
    public const string ImportErrorsTabCountFormat = "Errors ({0})";
    public const string ImportErrorsEmpty = "Nothing in the previewed rows would be rejected.";
    public const string ImportReportTab = "Report";
    public const string ImportReportEmpty = "Run an import or a validation — what happened appears here.";
    public const string ImportReportExport = "Export report…";
    public const string ImportReportCopy = "Copy";
    public const string ImportReportColumnRow = "Row";
    public const string ImportReportColumnColumn = "Column";
    public const string ImportReportColumnValue = "Value";
    public const string ImportReportColumnReason = "Reason";
    public const string ImportReportRevealTooltip = "Double-click to show this row in the converted preview";

    public const string ImportReportImportedFormat =
        "Imported {0:N0} of {1:N0} row(s). {2:N0} rejected. Time {3}.";
    public const string ImportReportCancelledFormat =
        "Cancelled — {0:N0} of {1:N0} row(s) written. {2:N0} rejected. Time {3}.";
    public const string ImportReportValidatedFormat =
        "Validated {0:N0} of {1:N0} row(s). {2:N0} would be rejected. Nothing was written. Time {3}.";
    public const string ImportReportValidatedCancelledFormat =
        "Validation cancelled — {0:N0} of {1:N0} row(s) checked. {2:N0} would be rejected. Time {3}.";
    /// <summary>§0.6: an open transaction is never described as a finished import.</summary>
    public const string ImportReportTransactionOpen = "Transaction OPEN — commit or roll back.";
    public const string ImportReportRowsCommittedFormat =
        "{0:N0} row(s) already committed — Rollback cannot take those back.";
    public const string ImportReportShortenedFormat = "{0:N0} value(s) were shortened to fit.";
    public const string ImportReportListTruncatedFormat = "The list stops at {0:N0} entries; the counts are exact.";
    public const string ImportCommit = "Commit";
    public const string ImportCommitTooltip = "Commit the rows this import wrote";
    public const string ImportRollback = "Rollback";
    public const string ImportRollbackTooltip = "Roll back everything this import wrote";
    /// <summary>Toolbar marker: the import left a transaction open and the decision is pending. Amber, not
    /// red — a pending decision is not a failure, and after a clean import the red readiness line was being
    /// read as "the import did not work".</summary>
    public const string UnsavedImportRowsFormat = "Data Import: {0:N0} row(s) written but not committed";
    public const string ImportTransactionOpenMarker = "● transaction open";
    public const string ImportTransactionOpenMarkerTooltip =
        "This import's rows are written but not persisted. Commit keeps them, Rollback discards them.";
    public const string ImportCommitted = "Committed.";
    public const string ImportRolledBack = "Rolled back.";
    public const string ImportRestoredLastConfiguration = "restored the last configuration";
    public const string ImportForgetLastConfiguration = "Clear";

    // ---- Data Import: named profiles (etap I11) ----

    public const string ImportProfileLabel = "Profile";
    /// <summary>The standing first row. ⚠ Named for what it IS — no profile attached — and deliberately NOT
    /// „default configuration", which would promise defaults it does not restore. Restoring them is Reset.</summary>
    public const string ImportProfileNone = "(no profile)";
    public const string ImportProfileDetached =
        "Working without a profile. The decisions on the surface are unchanged — use Reset to clear them.";
    /// <summary>Says which profiles the selector holds. A restriction the user cannot see is indistinguishable
    /// from a profile that has gone missing (§4.8.3).</summary>
    public const string ImportProfileScopeFormat =
        "Profiles saved on {0}, plus any that are not tied to a connection. A profile saved on another " +
        "connection is not offered — it names a table this database may not have.";
    /// <summary>Appended in the list to a profile that is not tied to a connection and is therefore offered
    /// everywhere.</summary>
    public const string ImportProfilePortableSuffix = "  · any connection";
    /// <summary>Appended to a profile written by a newer build. It stays in the list on purpose — hiding it
    /// would look exactly like a deletion.</summary>
    public const string ImportProfileUnreadableSuffix = "  · newer version";
    public const string ImportProfileUnreadableFormat =
        "Profile {0} was saved by a newer version of EmberTern and was not loaded. Applying only the parts " +
        "this build understands would silently change decisions you did not take.";
    public const string ImportProfileLoadedFormat = "Loaded profile {0}.";

    public const string ImportProfileSaveAs = "Save as…";
    public const string ImportProfileSaveAsTooltip = "Save the current decisions as a named profile";
    public const string ImportProfileSaveAsTitle = "Save the import profile";
    public const string ImportProfileNameLabel = "Profile name";
    public const string ImportProfileSaveConfirm = "Save";
    public const string ImportProfileSavedFormat = "Saved profile {0}.";
    public const string ImportProfileOverwriteTitle = "Overwrite the profile";
    public const string ImportProfileOverwriteFormat =
        "A profile named {0} already exists. Overwrite it with the decisions currently on the surface?";
    public const string ImportProfileOverwriteConfirm = "Overwrite";

    public const string ImportProfileRenameTooltip = "Rename the selected profile";
    public const string ImportProfileRenameTitle = "Rename the profile";
    public const string ImportProfileRenameConfirm = "Rename";
    public const string ImportProfileRenamedFormat = "Renamed to {0}.";
    public const string ImportProfileNameTakenFormat = "A profile named {0} already exists.";

    public const string ImportProfileDeleteTooltip = "Delete the selected profile";
    public const string ImportProfileDeleteTitle = "Delete the profile";
    public const string ImportProfileDeleteFormat =
        "Delete profile {0}? Only the saved decisions are removed — nothing on the surface changes and no " +
        "data is touched.";
    public const string ImportProfileDeleteConfirm = "Delete";
    public const string ImportProfileDeletedFormat = "Deleted profile {0}.";

    /// <summary>Start again: every decision back to its default, and no profile attached. The counterpart to
    /// „(no profile)", which only detaches.</summary>
    public const string ImportReset = "Reset";
    public const string ImportResetTooltip =
        "Start a new configuration — clears every decision, restores the defaults and detaches from the profile";
    public const string ImportResetTitle = "Start a new configuration";
    public const string ImportResetQuestion =
        "Clear every decision on this surface and start again? Saved profiles are not affected, and no data " +
        "is touched.";
    public const string ImportResetConfirm = "Start again";
    public const string ImportResetDone = "New configuration — defaults restored, no profile attached.";

    // ---- Data Import: ImportErrorKind → one sentence. The ONE table (rule #6). ----

    public const string ImportErrorNotAnInteger = "Not a whole number.";
    public const string ImportErrorNotANumber = "Not a number under the declared decimal separator.";
    public const string ImportErrorNotADateTime = "Not a date/time under the declared field order.";
    public const string ImportErrorNotABoolean = "Neither a true nor a false token.";
    public const string ImportErrorValueTooLong = "Longer than the target column.";
    public const string ImportErrorValueTooLongMeasuredFormat = "Too long: {0} characters, limit {1}.";
    public const string ImportErrorValueOutOfRange = "Outside the target column's range.";
    public const string ImportErrorPrecisionWouldBeLost = "Writing it would drop decimal places or a time part.";
    public const string ImportErrorUnsupportedTargetType = "The target column's type cannot be imported.";
    public const string ImportErrorNullNotAllowed = "The column is NOT NULL and has no default.";
    public const string ImportErrorNotRepresentable =
        "A character the connection charset cannot represent — it would be stored as '?'.";
    public const string ImportErrorSourceErrorValue = "The source cell holds an error value.";
    public const string ImportErrorServerNullViolation = "The server rejected a NULL.";
    public const string ImportErrorServerUniqueViolation = "Unique-key violation.";
    public const string ImportErrorServerCheckViolation = "CHECK constraint violated.";
    public const string ImportErrorServerForeignKeyViolation = "Foreign key: the referenced row does not exist.";
    public const string ImportErrorServerStringTruncation = "The server refused the value as too long.";
    public const string ImportErrorServerNumericOverflow = "Numeric overflow on the server.";
    public const string ImportErrorServerTransliteration = "The server could not transliterate the value.";
    public const string ImportErrorServerError = "The server refused the row.";

    public const string ScriptRun = "Run";
    public static readonly string ScriptRunTooltip = CommandTip.For(
        CommandId.Go, "Run the whole script in one transaction");
    public const string ScriptStopTooltip = "Stop after the current statement";
    public const string ScriptCommit = "Commit";
    public const string ScriptCommitTooltip = "Commit the open script transaction";
    public const string ScriptRollback = "Rollback";
    public const string ScriptRollbackTooltip = "Roll back the open script transaction";
    public const string ScriptTransactionLabel = "Transaction:";
    public const string ScriptModeManual = "Manual (review, then commit)";
    public const string ScriptModeAutoCommit = "Auto-commit on success";
    public const string ScriptModeSequenced = "Sequenced (deployment, commits in steps)";
    // Per-mode descriptions — surfaced where the user picks the mode (the picker's tooltip), so the
    // Sequenced trade-off is stated at the point of choice (not buried). No transaction jargon.
    public const string ScriptModeManualDescription =
        "Runs the whole script as one transaction and leaves it open so you can review the results, then Commit or Roll back. All-or-nothing.";
    public const string ScriptModeAutoCommitDescription =
        "Runs the whole script as one transaction and commits it automatically if nothing failed, otherwise rolls the whole script back. All-or-nothing.";
    public const string ScriptModeSequencedDescription =
        "For deployments: runs the script in steps, committing after each schema change so a later statement can use an object an earlier one created. NOT all-or-nothing — steps that already committed stay applied if a later step fails.";
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
    // Sequenced only: which committed step (segment/transaction) the statement ran in. Blank in the
    // single-transaction modes, where the whole script is one transaction.
    public const string ScriptColumnStep = "Step";
    public const string ScriptColumnStepTooltip =
        "In Sequenced mode, the committed step (transaction) this statement ran in. Each step commits before the next begins.";
    // Per-step outcome, shown by colouring the Step cell (Sequenced only). A step's outcome is distinct
    // from a statement's own result: a statement can have succeeded yet its step still rolled back.
    public const string ScriptStepCommittedTooltip =
        "This step committed — its changes are permanent.";
    public const string ScriptStepRolledBackTooltip =
        "This step rolled back — its changes were undone because a statement in this step failed (or the run was cancelled). Steps committed earlier stay applied.";
    public const string ScriptColumnStatement = "Statement";
    public const string ScriptColumnType = "Type";
    public const string ScriptColumnResult = "Result";
    // Sequenced only: a statement a stop-on-error / cancellation left unexecuted. It never ran, so it
    // is neither a success nor a failure — surfaced as a muted "Not run" row so the grid shows exactly
    // what the deployment did NOT reach.
    public const string ScriptResultNotRun = "Not run";
    public const string ScriptResultNotRunTooltip =
        "This statement was never reached — the run stopped (an earlier step failed) or was cancelled before it. It had no effect.";
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
    // Pre-flight: a mixed DDL+DML script cannot run in a single-transaction mode (Manual / Auto-commit)
    // because Firebird cannot use an object a statement created until it is committed (#213). Stop before
    // the first statement and point the user at Sequenced, which is built for exactly this.
    public const string ScriptStatusMixedNeedsSequenced =
        "This script mixes schema changes (CREATE / ALTER / …) with data statements (INSERT / UPDATE / …). In Manual and Auto-commit the whole script runs as a single transaction, and Firebird cannot use an object a statement just created until that change is committed — so a later statement would fail. Choose the “Sequenced (deployment)” transaction mode: it commits each schema change before the statements that depend on it.";
    public const string ScriptStatusManualSummaryFormat =
        "{0} succeeded, {1} failed in {2}. Transaction open — Commit or Rollback.";
    public const string ScriptStatusAutoSummaryFormat = "{0} {1} succeeded, {2} failed in {3}.";
    // Sequenced (deployment) — committed step-by-step, so the summary states the non-atomic reality
    // rather than a single Committed/Rolled-back verdict.
    public const string ScriptStatusSequencedSummaryFormat =
        "Deployment: {0} succeeded, {1} failed in {2}. Committed steps stay applied — this mode is not all-or-nothing.";
    // Sequenced headline: how many committed steps (transactions) of all the steps the run planned —
    // committed + rolled-back + not-run. Prepended to the deployment / cancelled summary (seam C3).
    public const string ScriptStatusSequencedStepsFormat = "{0} of {1} steps committed.";
    public const string ScriptStatusSequencedCancelled =
        "Deployment cancelled. Steps that already committed stay applied; the step in progress was rolled back.";
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

    // ⭐ Chip transakcji w pasku statusu (§8.4.5) — GLOBALNA odpowiedź na „czy mam otwartą transakcję
    // i od jak dawna". Pasek nad wynikami edytora SQL niesie osobną, LOKALNĄ informację: liczbę
    // instrukcji. Dwa poziomy informacji, nie redundancja (decyzja użytkownika, 2026-08-02).
    public const string StatusBarTransactionChipFormat = "Transaction · {0}";
    // Stan przejściowy: transakcja jest otwarta, ale znacznik czasu jeszcze nie powstał (np. otwarta
    // przed podpięciem chipa). Lepszy niż chip pokazujący „0 s", który sugerowałby świeży start.
    public const string StatusBarTransactionChipBare = "Transaction";

    // ⭐ Sekcja postępu (§8.4.6) — M3.1f. ⚠ Tekst jest ogólny („operation"), bo od M3b ta sekcja
    // obsługuje każdą długo trwającą operację, nie tylko zapytanie SQL. Skrótu klawiaturowego nie
    // podajemy: anulowanie nie ma gestu w `CommandCatalog`, a tooltip obiecujący nieistniejący
    // klawisz uczyłby nieprawdy (reguła z etapu Keyboard Manager, gotcha #284).
    public const string StatusBarCancelOperationTooltip = "Cancel the running operation";

    // ⭐⭐ ETYKIETY SEKCJI POSTĘPU — M3b.1. Wymóg użytkownika: „etykieta zawsze jednoznacznie określa,
    // co jest wykonywane", bo od tej iteracji sekcja ma TRZY źródła i sam napis „Loading… 12 345 rows"
    // nie mówi, czy to zapytanie, skrypt, czy import. Stąd nazwa operacji w każdym formacie.
    //
    // ⚠⚠ KAŻDA JEST KRÓTKA I OGRANICZONA, I TO NIE JEST ESTETYKA — POMIAR. Pasek statusu ma
    // `ColumnDefinitions="Auto,*,Auto,Auto"` (MainWindow.axaml:2095), więc sekcja 4 rośnie kosztem
    // kolumny gwiazdkowej i PRZESUWA chipy stanu w lewo. §8.4.6 nadało samemu paskowi stałe 120 px
    // dokładnie z tego powodu; etykieta takiego ograniczenia nie ma, więc ogranicza ją treść.
    // ⛔ Nie dopisywać tu szczegółu operacji (np. „N read · M written · K failed"). Szczegół należy do
    // powierzchni, która operację prowadzi — to ten sam podział własności, który §19.5.1 i §19.7.1 już
    // ratyfikowały: pasek statusu niesie FAKT globalny, właściciel operacji niesie SZCZEGÓŁ lokalny.
    public const string StatusProgressQueryRowsFormat = "Executing query… {0:N0} rows";
    public const string StatusProgressScriptFormat = "Running script… {0:N0} / {1:N0}";
    public const string StatusProgressImportFormat = "Importing data… {0:N0} rows";

    // ⭐ Odczyt źródła przed importem (M3b.1c). ⚠ DWIE etykiety, nie jedna z „file" na sztywno: to samo ogniwo
    // obsługuje schowek, a napis „Loading file…" nad odczytem schowka byłby nieprawdą — a kłamiąca etykieta jest
    // nieodróżnialna od awarii (gotcha #311). Jeden warunek, dwa uczciwe zdania.
    // ⚠ Bez licznika: ten odcinek nie zna ani sumy, ani postępu (czyta próbkę schematu i ograniczony podgląd),
    // więc jakakolwiek liczba tutaj byłaby zmyślona.
    public const string StatusProgressImportReadingFile = "Loading file…";
    public const string StatusProgressImportReadingClipboard = "Reading clipboard…";

    // ⭐ Ładowanie połączenia (M3b.2). Dwie etykiety na trzy fazy, i to jest zmierzone, nie oszczędne:
    // faza 2 (odtworzenie zakładek) jest SYNCHRONICZNA na wątku UI, a odmalowanie następuje PRZED nią —
    // napis ustawiony na jej początku pojawiłby się dopiero po jej zakończeniu, czyli gdy jest już
    // nieprawdziwy. Zamiast martwego UI zostaje etykieta fazy 1 (decyzja użytkownika 2026-08-04).
    // ⭐ Faza 3 jest jedyną fazą ze ZNANĄ sumą (13 kategorii), więc jedyną, która uczciwie pokazuje procent.
    public const string StatusProgressConnecting = "Connecting to database…";
    public const string StatusProgressMetadataFormat = "Loading metadata… {0:N0} / {1:N0}";

    // ⭐ Chipy Trace i Debuggera (§8.4.3 sekcja 3) — M3.1e. Etykieta niesie sam FAKT („gdzieś żyje
    // sesja"), a szczegół idzie do tooltipa, który czyta `StatusText` z VM-a odpowiedniej zakładki.
    // ⚠ Rzeczownik, nie czasownik: chip mówi, CO jest prawdą, a nie co się dzieje — „co się dzieje"
    // to rola railu (§8.4.1). Stąd „Debug"/„Trace", a nie „Debugging"/„Tracing".
    public const string StatusBarDebugChipLabel = "Debug";
    public const string StatusBarTraceChipLabel = "Trace";

    // Zgrubny czas trwania, czytelny kątem oka (§8.4.5). ⛔ Nie zwiększać precyzji — pasek statusu
    // nie jest stoperem; dokładny czas wykonania niesie ExecutionTimer w toolbarze edytora.
    public const string DurationSecondsFormat = "{0} s";
    public const string DurationMinutesFormat = "{0} min";
    public const string DurationHoursFormat = "{0} h {1} min";
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
    public static readonly string TransactionCommitTooltip = CommandTip.For(CommandId.Commit, "Commit");
    public static readonly string TransactionRollbackTooltip = CommandTip.For(CommandId.Rollback, "Roll back");
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
    // Seam 5c — per-tab close is Save / Discard / Cancel whenever the tab has somewhere to save,
    // matching the disconnect and app-close guards instead of forcing "discard or stay".
    public const string CloseTabUnsavedSave = "Save and close";

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

    // ⛔ `StatusBarReady` i `StatusBarConnectedTo` usunięte w M3.1b. Pasek statusu nie opisuje już
    // połączenia zdaniem — sekcja 1 pokazuje NAZWĘ połączenia i endpoint, a stan „połączony / nie"
    // niesie kropka. `StatusBarDisconnected` zostaje: jest etykietą w slocie nazwy, gdy połączenia
    // nie ma (§19.3).
    public const string StatusBarDisconnected = "Disconnected";

    public const string ThemeToggleTooltip = "Toggle dark / light theme";
    public const string SidebarToggleTooltip = "Show / hide the connections panel";

    // ── Title bar — connection toolbar (M‑1, M3.2d) ─────────────────────────────────────────────────
    // The seven buttons between the sidebar toggle and the separator. They carried English literals in
    // MainWindow.axaml since the sidebar was built; the text is unchanged here, only its home.
    // ⚠ Deliberately their OWN `*Tooltip` members rather than a reuse of `ConnectionConnect` /
    // `ConnectionDisconnect` / `ConnectionNew` above: those are (orphaned) LABEL strings, and the UX
    // Consistency Pass recorded the reverse mistake as audit finding D6 — seven menu items whose Header
    // read a tooltip constant, which is how "Add item" became a menu entry. A label and a tooltip answer
    // different questions and are free to diverge the moment either is reworded.
    // ⚠ No gesture is spelled here, and that is the ratified rule, not an omission: these commands are
    // Tree-scoped (F3 / F4 / F8 reach them only while the tree has focus), so a toolbar tooltip promising
    // a key would teach something false outside the tree — keyboard-manager.md §14.
    public const string ConnectionNewTooltip = "New Connection";
    public const string ConnectionEditTooltip = "Edit Connection";
    public const string ConnectionCopyTooltip = "Copy Connection";
    public const string ConnectionDeleteTooltip = "Delete Connection";
    public const string ConnectionConnectTooltip = "Connect";
    public const string ConnectionDisconnectTooltip = "Disconnect";
    public const string ConnectionReconnectTooltip = "Reconnect";

    // ── Title bar — window caption buttons (M‑1, M3.2d) ─────────────────────────────────────────────
    // EmberTern draws its own caption buttons (the window is `ExtendClientAreaToDecorationsHint`), so
    // these three are ordinary application strings and not the OS's.
    public const string WindowMinimizeTooltip = "Minimize";
    public const string WindowMaxRestoreTooltip = "Maximize / Restore";
    public const string WindowCloseTooltip = "Close";

    // ── Application Menu (the hamburger) ────────────────────────────────────────────────────────────
    // A rarely-used ADMINISTRATIVE menu for application-level functions — never commands of the active
    // document, which stay on the toolbars, the shortcuts and the context menus. Design + the reasoning
    // for what is deliberately absent: docs/design/hamburger-navigation.md §3–§5.
    public const string AppMenuTooltip = "Application menu";
    // ⭐ Live since Settings Center etap 3. It shipped as a DISABLED row with a "Not available yet" tooltip
    // while the window did not exist — the rule being that a row never ships ahead of what it opens — and the
    // etap that built the window is the etap that enabled the row and removed that tooltip string.
    public const string AppMenuSettings = "Settings…";
    public const string AppMenuKeyboardShortcuts = "Keyboard Shortcuts…";
    public const string AppMenuAbout = "About EmberTern…";
    public const string AppMenuExit = "Exit";

    // The About window. Deliberately a PRODUCT window, not a diagnostic one: the logo, the name, the version,
    // the author and the copyright — no runtime/OS block, no library names, and no liability or privacy
    // wording (a limitation of liability is a term of the future EmberTern licence and belongs there, in one
    // document). Design: docs/design/hamburger-navigation.md §8.
    // ⛔ There is deliberately NO version string here. The number is read from the assembly by AppInfo, whose
    // single source is <Version> in Directory.Build.props.
    public const string AboutTitle = "About EmberTern";
    public const string AboutVersionFormat = "Version {0}";
    // Released on its own line under the version. The date is assembly metadata fed by <ReleaseDate> in
    // Directory.Build.props — same single source as the version, so it is never a date typed into a view.
    public const string AboutReleasedFormat = "Released {0}";
    // ⚠ The author line is LABELLED on purpose. Unlabelled it read as an unsigned line of text, and the name
    // already appears in the copyright below — the label is what turns a repetition into authorship.
    public const string AboutAuthorFormat = "Created by {0}";
    public const string AboutClose = "Close";
    // A discreet footer button rather than a tab: a tab strip on a five-line window makes it look like a
    // configuration dialog. No licence requires these names on the About face itself — MIT is silent on
    // placement and IDPL 1.0 §3.6 scopes its "conspicuously" to the notices document — so the face stays bare
    // and the obligation is met by the document behind this button.
    public const string AboutThirdPartyNotices = "Third-party notices";
    public const string ThirdPartyNoticesTitle = "Third-party notices";
    public const string ThirdPartyNoticesUnavailable =
        "The third-party notices file could not be read from this build.";

    // ── Keyboard Shortcuts window ───────────────────────────────────────────────────────────────────
    // A read-only VIEW of CommandCatalog: no name, gesture or scope is written here or in the window.
    public const string KeyboardShortcutsTitle = "Keyboard Shortcuts";
    public const string KeyboardShortcutsSearchPlaceholder = "Search commands and shortcuts…";
    public const string KeyboardShortcutsColumnCommand = "Command";
    public const string KeyboardShortcutsColumnShortcut = "Shortcut";
    public const string KeyboardShortcutsColumnScope = "Scope";
    public const string KeyboardShortcutsCountFormat = "{0} commands";
    public const string KeyboardShortcutsCountOne = "1 command";
    public const string KeyboardShortcutsEmpty = "No command matches this search.";
    // Restores Global → Tab → Tree → Grid → Editor → alphabetically. Shown only while a column sort is
    // overriding it, because an always-visible reset for a state you are not in is noise.
    public const string KeyboardShortcutsResetOrder = "Reset order";
    public const string KeyboardShortcutsResetOrderTooltip =
        "Return to the default order: Global, Tab, Tree, Grid, Editor, then alphabetical";

    // ── Settings Center ─────────────────────────────────────────────────────────────────────────────
    // The app's one home for user preferences: a category list, a search box, and pages that apply on change
    // with no OK/Cancel. Design: docs/design/settings-center.md §5–§6.
    // ⚠ Every OPTION KEY ("Dark", "en") lives in Core's PreferenceOptions, because it is persisted and
    // validated; only the words are here. The two are bound by a test — a key without a label ships a blank
    // row.
    public const string SettingsCenterTitle = "Settings";
    public const string SettingsSearchPlaceholder = "Search settings…";
    public const string SettingsNoMatch = "No setting matches this search.";
    public const string SettingsClose = "Close";
    // Apply-on-change is the ratified model (Q8), so the window has no OK/Cancel and nothing to confirm. The
    // hint says so once, quietly, rather than leaving the user hunting for a missing OK button.
    public const string SettingsAppliedImmediately = "Changes apply immediately.";

    public const string SettingsCategoryGeneral = "General";

    public const string SettingsThemeLabel = "Theme";
    public const string SettingsThemeDescription =
        "Colour scheme for the whole application. The titlebar button switches the same setting.";
    // Extra search terms: the words a user types when they do not know our label.
    public const string SettingsThemeKeywords = "colour color appearance dark light contrast";
    public const string SettingsThemeDark = "Dark";
    public const string SettingsThemeLight = "Light";

    public const string SettingsLanguageLabel = "Language";
    // ⚠ It says "interface language" and nothing about availability, because the row is REAL: the value is
    // stored, validated and round-tripped from day one, and its list happens to have one entry. Presenting it
    // as unavailable would misrepresent what it does. Adding Polish is a row in Core's language catalog plus
    // the localization milestone (design §8) — no change to this window.
    public const string SettingsLanguageDescription = "Language of the EmberTern interface.";
    public const string SettingsLanguageKeywords = "locale translation localization interface";
    public const string SettingsLanguageEnglish = "English";

    // ⚠ The description says "open tabs" and "saved queries stay" because the setting is narrower than its name
    // suggests, and the narrower half is the important one: a connection's saved queries live in the same stored
    // workspace and are the user's own content, so they come back either way.
    public const string SettingsRestoreWorkspaceLabel = "Restore open tabs on startup";
    public const string SettingsRestoreWorkspaceDescription =
        "Reopen the tabs from your last session when you connect. Saved queries are always restored.";
    public const string SettingsRestoreWorkspaceKeywords =
        "workspace session tabs restore startup launch reopen clean start";

    // ── Editor (etap 6) ─────────────────────────────────────────────────────────────────────────────
    // The default Source/Easy mode for newly opened object editors (§7.6) and the execution row limits
    // (§7.2). §7.2 calls this an "Editor / Execution" page; it is one page.
    public const string SettingsCategoryEditor = "Editor";

    // ⚠ One description for all four rows, and it states the two things a user needs to know: this is a
    // DEFAULT for newly opened editors, and switching a mode inside an editor no longer changes it. That second
    // sentence is the etap's actual fix — the four flags used to be rewritten silently by the last toggle.
    public const string SettingsEditorModeDescription =
        "Mode a newly opened editor starts in. Switching mode inside an editor affects that tab only; a "
        + "restored tab keeps the mode it was saved in.";
    public const string SettingsEditorModeKeywords =
        "editor mode easy source default open procedure view trigger function structured";

    public const string SettingsProcedureEasyModeLabel = "Open procedures in Easy mode";
    public const string SettingsViewEasyModeLabel = "Open views in Easy mode";
    public const string SettingsTriggerEasyModeLabel = "Open triggers in Easy mode";
    public const string SettingsFunctionEasyModeLabel = "Open functions in Easy mode";

    // ⚠ Both numeric descriptions name their range, because the field CLAMPS silently: a user who types 50000000
    // and gets 1000000 back has to be able to see why, and the alternative — a validation error on a settings
    // page that applies on change — would be a worse answer to the same problem.
    public const string SettingsPreviewRowLimitLabel = "Preview row limit";

    // ⚠ The key comes from the catalog, not from this string — CommandId.Go is what F5 actually runs, and a
    // hand-typed "(F5)" here would teach a stale shortcut the day it is re-bound, silently (gotcha #284). That
    // is also why this one member is `static readonly` while its neighbours are `const`: the guard keys on
    // const-ness, because a correctly composed string contains the same text at run time.
    public static readonly string SettingsPreviewRowLimitDescription = CommandTip.Sentence(
        CommandId.Go,
        "Rows a Preview execution ({0}) stops at. Full load is unaffected. Between 1 and 1 000 000.");
    public const string SettingsPreviewRowLimitKeywords =
        "execution execute preview rows limit f5 query results fetch cap";

    // ⚠ It says the safety ceiling is separate and fixed, so the absence of a control for it reads as a decision
    // rather than an omission — ratified Q9: a configurable memory backstop is not a backstop.
    public const string SettingsFullLoadPromptLabel = "Ask before loading more than";
    public const string SettingsFullLoadPromptDescription =
        "Rows at which a Full load stops to ask whether to keep going. The hard 1 000 000-row memory limit is "
        + "separate and not configurable.";
    public const string SettingsFullLoadPromptKeywords =
        "execution execute full load threshold prompt rows ask keep loading memory";

    // ── Grid (etap 6) ───────────────────────────────────────────────────────────────────────────────
    public const string SettingsCategoryGrid = "Grid";

    // ⚠ Names the two grids explicitly. This is the page size of the SERVER-PAGED data grids, which is what
    // ratified Q9 admits; the SQL editor's results and the Procedure / Function exec grids page an
    // already-materialized result in memory and are not this setting's subject. A description saying just
    // "grids" would be a promise the code deliberately does not keep.
    public const string SettingsDataPageSizeLabel = "Data page size";
    public const string SettingsDataPageSizeDescription =
        "Rows per page in the Table Data and View Data grids. Each grid's own page-size box still overrides it. "
        + "Between 1 and 1 000.";
    public const string SettingsDataPageSizeKeywords =
        "grid data page size rows pagination table view records per page";

    public const string SettingsGridAutoFitLabel = "Auto-fit columns by default";
    public const string SettingsGridAutoFitDescription =
        "Size columns to their content in a grid whose layout you have not adjusted yet. A grid you have "
        + "resized keeps its own layout.";
    public const string SettingsGridAutoFitKeywords =
        "grid columns auto fit width size layout default resize";

    // ── Tabs (M3.3b / product-polish §8.2) ──────────────────────────────────────────────────────────
    // ⭐ Zakładki dostały WŁASNĄ kategorię, a nie wiersze w General — decyzja użytkownika (2026-08-03):
    // pasek zakładek jest osobną powierzchnią aplikacji (§0.1), a General i tak już nosi motyw, język,
    // workspace i eksport. Kategoria jest też celem skoku dla przyszłej pozycji „Ustawienia zakładek…"
    // z menu kontekstowego zakładki (D9 / M3.3c).
    public const string SettingsCategoryTabs = "Tabs";

    // ⚠ Opis mówi, co użytkownik ZOBACZY, a nie jak to jest zbudowane — i nazywa różnicę, która naprawdę
    // dzieli te tryby: czy zakładka może zniknąć z widoku. To jest ratyfikowana istota decyzji D5/D7.
    public const string SettingsTabStripModeLabel = "Tab strip layout";
    public const string SettingsTabStripModeDescription =
        "Multiple rows keeps every open tab visible — the strip grows and then scrolls. A single row scrolls "
        + "sideways instead and moves the rest into a searchable list.";
    public const string SettingsTabStripModeKeywords =
        "tabs tab strip rows layout multi row single row overflow scroll workspace documents";

    public const string SettingsTabStripModeMultiRow = "Multiple rows";
    public const string SettingsTabStripModeSingleRow = "Single row";

    // ⚠ Mówi wprost, że dotyczy tylko trybu wielowierszowego — wiersz widoczny w trybie B, w którym nic
    // nie robi, byłby dokładnie tym „martwym zapisem wyglądającym na regułę", przed którym broni §18.R.
    public const string SettingsTabStripMaxRowsLabel = "Maximum rows";
    public const string SettingsTabStripMaxRowsDescription =
        "How tall the tab strip may grow before it starts scrolling. Multiple-rows layout only. Between 1 "
        + "and 10.";
    public const string SettingsTabStripMaxRowsKeywords =
        "tabs tab strip rows maximum height limit scroll workspace";

    // Przycisk przepełnienia w trybie pojedynczego wiersza. ⭐ Licznik pokazuje zakładki NIEWIDOCZNE,
    // nie wszystkie otwarte (§8.2 + decyzja użytkownika) — „ile mam poza ekranem" jest informacją, której
    // użytkownik potrzebuje w tym momencie; „ile mam otwartych" widać po samym pasku.
    public const string TabStripOverflowTooltip = "Tabs that do not fit — click to search all open tabs";
    public const string TabStripOverflowFilterWatermark = "Filter tabs…";

    // ── Menu kontekstowe zakładki (M3.3c / §8.3) ────────────────────────────────────────────────────
    // ⚠ Ikony przez `{app:MenuIcon}`, gesty przez `{app:CommandGesture}` — zero nowej chromy
    // (Keyboard Manager etap 5). Tu mieszkają wyłącznie słowa.
    public const string TabMenuClose = "Close";
    public const string TabMenuCloseOthers = "Close others";
    public const string TabMenuCloseAll = "Close all";
    public const string TabMenuCloseToTheRight = "Close tabs to the right";
    public const string TabMenuCloseUnmodified = "Close unmodified";
    public const string TabMenuRefresh = "Refresh";
    public const string TabMenuCopyObjectName = "Copy object name";
    public const string TabMenuRevealInExplorer = "Show in Metadata Explorer";
    public const string TabMenuSettings = "Tab settings…";

    // ⚠⚠ Bramka reguły #11 dla zamykania masowego — CZWARTE wejście do tej samej bramki, obok
    // zamknięcia zakładki, rozłączenia i zamknięcia aplikacji.
    // ⭐ Komunikat WYMIENIA zakładki z pracą ({0} = lista), bo „kilka zakładek ma niezapisane zmiany"
    // nie pozwala podjąć decyzji — a to jest moment, w którym użytkownik ją podejmuje.
    public const string TabsCloseUnsavedTitle = "Unsaved changes";
    public const string TabsCloseUnsavedFormat =
        "These tabs have uncompiled changes:\n\n{0}\n\nSave them before closing?";
    public const string TabsCloseUnsavedSave = "Save and close";
    public const string TabsCloseUnsavedDiscard = "Discard and close";

    // ── Debugger (etap 6) ───────────────────────────────────────────────────────────────────────────
    public const string SettingsCategoryDebugger = "Debugger";

    // ⚠ Says the launch panel still offers it, because that is what makes this a DEFAULT rather than a
    // replacement — the recorded D4 wish was "show only params at launch", not "take the choice away".
    public const string SettingsDebuggerIsolationLabel = "Default transaction isolation";
    public const string SettingsDebuggerIsolationDescription =
        "Isolation a debug session starts with. The launch panel's Advanced section can still change it for a "
        + "single run.";
    public const string SettingsDebuggerIsolationKeywords =
        "debugger debug transaction isolation snapshot read committed default launch";

    // ── SQL Formatter ───────────────────────────────────────────────────────────────────────────────
    // Exactly two rows, and that is ratified (§6.4 / §9.1): no line width, no indent size, no comma
    // placement. Both default to lower case, so a user who never opens this page sees the output EmberTern
    // has always produced.
    public const string SettingsCategoryFormatter = "SQL Formatter";

    // ⚠ Says "Format SQL" rather than "the formatter", because that is the scope: the action on the
    // Ctrl+K / toolbar / context menu. SQL that EmberTern composes (Copy as INSERT, .sql export) and
    // generated DDL keep their own casing, by ratified Q1 — a description promising "everywhere" would be
    // a promise the code deliberately does not keep.
    public const string SettingsFormatterKeywordCaseLabel = "Keyword case";
    public const string SettingsFormatterKeywordCaseDescription =
        "How Format SQL cases keywords, data types and built-in functions — select or SELECT.";
    public const string SettingsFormatterKeywordCaseKeywords =
        "formatter format sql case casing uppercase lowercase keyword reserved word";

    public const string SettingsFormatterIdentifierCaseLabel = "Identifier case";
    // ⚠ Says quoted names are untouched because that is a correctness guarantee the user can rely on, not a
    // limitation: "MyTable" is a different object from MYTABLE in Firebird, so re-casing it would change
    // which object the statement names (§0 / architecture rule #11).
    public const string SettingsFormatterIdentifierCaseDescription =
        "How Format SQL cases table, column and variable names. Quoted names like \"MixedCase\" are never "
        + "changed — their case is part of the object's identity.";
    public const string SettingsFormatterIdentifierCaseKeywords =
        "formatter format sql case casing uppercase lowercase identifier name table column variable";

    public const string SettingsCaseLower = "lower case";
    public const string SettingsCaseUpper = "UPPER CASE";

    // Shown in the docked MessageBanner when a change could not be written. Settings Center is the ONE place
    // where the store's silent refusal (audit A-03) must be spoken: every other writer in the app is
    // incidental, but a dialog whose entire purpose is "change this setting" cannot accept a change and
    // persist nothing without saying so. {0} = the store's diagnostic.
    public const string SettingsSaveRefusedFormat =
        "This change applies for the current session only — it could not be saved. {0}";

    // ── Settings export / import (etap 5b) ──────────────────────────────────────────────────────────
    // The user-facing half of EmberTern's own .etsettings format. Design §6.3; the format itself is §15.
    // ⚠ Failure messages are NOT duplicated here. SettingsImportReader / SettingsImportApplier produce them in
    // Core, on purpose (the same reason Firebird connection-failure text lives in the Firebird layer): a status
    // whose meaning is decided in Core should not have its explanation decided somewhere else. Surfaces switch
    // on the STATUS and show the message as-is (§15.8).
    public const string SettingsImportExportLabel = "Import / export settings";
    public const string SettingsImportExportDescription =
        "Copy your settings to another machine, or keep a backup. The file is always encrypted with a "
        + "passphrase you choose.";
    public const string SettingsImportExportKeywords =
        "export import backup restore transfer move copy migrate passphrase encrypt file etsettings folder";

    public const string SettingsExportButton = "Export…";
    public const string SettingsImportButton = "Import…";
    public const string SettingsOpenFolderButton = "Open settings folder";
    public const string SettingsOpenFolderTooltip =
        "Opens the folder holding settings.dat and its backup copies.";

    // ── Export dialog ───────────────────────────────────────────────────────────────────────────────
    public const string SettingsExportTitle = "Export settings";
    public const string SettingsExportIntro = "Choose what to include, then set a passphrase for the file.";
    public const string SettingsExportSectionsHeader = "Include";
    public const string SettingsExportPassphraseHeader = "Passphrase";
    public const string SettingsExportRun = "Export…";
    public const string SettingsExportCancel = "Cancel";
    public const string SettingsExportFileFilter = "EmberTern settings export";
    public const string SettingsExportSuggestedName = "embertern-settings";

    public const string SettingsSectionPreferences = "Preferences (theme, language, formatter)";
    public const string SettingsSectionGridProfiles = "Grid column layouts";
    public const string SettingsSectionFolders = "Connection folders";
    public const string SettingsSectionConnections = "Connection profiles";
    // ⚠ Ratified Q2: the label must state that the file will contain database credentials. It says it plainly —
    // the whole reason the checkbox exists is that the user should be making this decision knowingly.
    public const string SettingsSectionPasswords = "Connection passwords — the file will contain database credentials";
    public const string SettingsSectionWorkspaces = "Open tabs, SQL text and saved queries";
    public const string SettingsSectionImportProfiles = "Data Import configurations";

    public const string SettingsExportPassphraseLabel = "Passphrase";
    public const string SettingsExportPassphraseConfirmLabel = "Repeat passphrase";
    // ⚠ Stated where the passphrase is TYPED, not in a help page: a passphrase-derived key means a forgotten
    // passphrase makes the file permanently unreadable, with no reset and no back door (design §6.3.1). That is
    // a consequence of the ratified always-encrypted decision, and the only honest place to say it is here.
    public const string SettingsExportPassphraseWarning =
        "There is no way to recover this passphrase. Without it the file cannot be read again — by anyone, "
        + "including us.";
    public const string SettingsExportPassphraseMismatch = "The two passphrases are not the same.";
    public const string SettingsExportPassphraseMissing = "Enter a passphrase — every export is encrypted.";
    public const string SettingsExportNothingSelected = "Select at least one thing to include.";
    // {0} = file name.
    public const string SettingsExportDoneFormat = "Exported to {0}.";
    // {0} = the failure message.
    public const string SettingsExportFailedFormat = "The export could not be written: {0}";

    // ── Import dialog ───────────────────────────────────────────────────────────────────────────────
    public const string SettingsImportTitle = "Import settings";
    public const string SettingsImportPickFile = "Choose file…";
    public const string SettingsImportIntro =
        "Choose an exported settings file. Its contents are shown once it has been opened.";
    public const string SettingsImportPassphraseLabel = "Passphrase";
    public const string SettingsImportOpen = "Open";
    public const string SettingsImportRun = "Import selected";
    public const string SettingsImportCancel = "Close";
    public const string SettingsImportContentsHeader = "Take from this file";
    // Shown only once the file is open and every box has been unticked — the one state in which Import is dead
    // with nothing on screen saying why.
    public const string SettingsImportNothingSelected = "Select at least one thing to import.";
    // ⚠ Shown only when the file carries passwords AND the row is offered — an import overwrites the password
    // stored for the same connection, which is a thing to say before it happens rather than after.
    public const string SettingsImportPasswordsNote =
        "Taking passwords replaces the password stored for each matching connection.";
    // ⭐ The honest disclosure of what an import can and cannot do to a RUNNING session, in the place the user
    // decides. Nothing is blocked (EmberTern discloses rather than forbids); it just has to be true.
    public const string SettingsImportLiveSessionNote =
        "Theme, formatter, folders and connections apply immediately. A profile you are connected to keeps its "
        + "current settings until you reconnect. Open tabs and saved queries apply the next time EmberTern "
        + "starts.";
    // {0} = the comma-separated sections taken. {1} = the preserved copy's file name.
    public const string SettingsImportDoneFormat = "Imported: {0}. Your previous settings were kept as {1}.";
    // Used when there was no settings.dat to preserve — a first run.
    public const string SettingsImportDoneNoBackupFormat = "Imported: {0}.";

    // ── Canonical command names (CommandDescriptor.Title) ───────────────────────────────────────────
    // ONE host-independent name per command, for surfaces that LIST commands: the Keyboard Shortcuts window
    // today, a Command Palette later. Deliberately separate from the tooltip strings above and below, which
    // stay host-specific prose — that distinction is why adding Title did not reopen etap 4's decision.
    // ⛔ These belong here and not in CommandCatalog: the catalog owns the gesture, UiStrings owns the words.
    public const string CommandTitleGo = "Execute (active tab)";
    public const string CommandTitleExecuteQuery = "Execute query";
    public const string CommandTitleExecuteQueryFull = "Execute query, all rows";
    public const string CommandTitleFormatSql = "Format SQL";
    public const string CommandTitleCompile = "Compile";
    public const string CommandTitleImportValidate = "Validate import";
    public const string CommandTitleImportRefresh = "Re-read import source";
    public const string CommandTitleImportBrowse = "Choose import file";

    public const string CommandTitleDebuggerStepOver = "Step over";
    public const string CommandTitleDebuggerStepInto = "Step into";
    public const string CommandTitleDebuggerStepOut = "Step out";
    public const string CommandTitleDebuggerRunToCursor = "Run to cursor";
    public const string CommandTitleDebuggerStop = "Stop debugging";
    public const string CommandTitleDebuggerRestart = "Restart debugging";
    public const string CommandTitleDebuggerToggleBreakpoint = "Toggle breakpoint";
    public const string CommandTitleDebuggerEvaluateSelection = "Evaluate selection";
    public const string CommandTitleDebuggerSaveSource = "Save debugged source";

    public const string CommandTitleEditorFind = "Find";
    public const string CommandTitleEditorReplace = "Replace";
    public const string CommandTitleEditorCompletion = "Show completion list";
    public const string CommandTitleEditorParameterHelper = "Show parameter help";
    public const string CommandTitleEditorRename = "Rename";
    public const string CommandTitleEditorPeekDefinition = "Peek definition";
    public const string CommandTitleEditorQuickFix = "Quick fix";
    public const string CommandTitleEditorExpandConstruct = "Expand construct";
    public const string CommandTitleEditorNextDiagnostic = "Next diagnostic";
    public const string CommandTitleEditorPreviousDiagnostic = "Previous diagnostic";

    public const string CommandTitleNewObject = "New object";
    public const string CommandTitleDeleteObject = "Delete object";
    public const string CommandTitleRefreshMetadata = "Refresh metadata";

    // Generic on purpose: these route through the app's ONE collection router, which serves fields, rows,
    // columns, parameters and variables. The per-collection nouns ("New field") belong to the toolbar and the
    // grid's own menu, which know which collection they are looking at; a catalogue does not.
    public const string CommandTitleCollectionAdd = "New item in list";
    public const string CommandTitleCollectionEdit = "Edit selected item";
    public const string CommandTitleCollectionRemove = "Delete selected item";

    public const string CommandTitleGlobalSearch = "Global search";
    public const string CommandTitleFocusSidebarFilter = "Focus object filter";
    public const string CommandTitleCommit = "Commit transaction";
    public const string CommandTitleRollback = "Roll back transaction";
    public const string CommandTitleCloseTab = "Close tab";

    // ⛔ `StatusBarVersionFormat` usunięty w M3.1b (decyzja D3): nazwa aplikacji i numer wersji nie należą
    // do paska statusu, tylko do okna About. `AppInfo` pozostaje jedynym źródłem wersji.
    // No gesture is shown beside Exit: EmberTern does not own Alt+F4, and a gesture typed by hand is the
    // drift CommandTip exists to prevent (gotcha #284). It routes through the window's ordinary close, so
    // unsaved work and an open transaction still get their prompts.
    public const string AppMenuExitTooltip = "Close EmberTern";
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
    // (Removed 2026-07-27, audit A-09: DialogFieldTransactionProfile + its Data/Metadata pair were three
    // captions for a connection-dialog field that no longer exists. The TPB profile is not user-configurable —
    // TransactionService.EnforcedProfile is a constant, deliberately — so a label offering to configure it
    // described a control nothing could honour. They were defined and never referenced.)
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
    // The shortcut chip beside the Execute button — the gesture alone, no label.
    public static readonly string ToolbarExecuteHint = CommandTip.Gesture(CommandId.Go);
    // Tooltip on the single Execute button — surfaces the Shift+F5 full-read power path (Variant A+D:
    // one button, no split-button, no second Execute button).
    // Two gestures in one tooltip, so it interpolates CommandTip.Gesture twice rather than using Sentence —
    // still the one formatter, still nothing typed by hand.
    public static readonly string ToolbarExecuteTooltip =
        $"Execute  ·  {CommandTip.Gesture(CommandId.Go)} preview  ·  "
        + $"{CommandTip.Gesture(CommandId.ExecuteQueryFull)} all rows";
    public const string ToolbarClearEditor = "Clear";
    public const string ToolbarClearEditorIcon = "🗑";
    public const string ToolbarClearEditorTooltip = "Clear editor content";
    public const string ToolbarCloseTab = "Close tab";
    public const string ToolbarCloseTabIcon = "✕";
    public static readonly string ToolbarCloseTabTooltip = CommandTip.For(CommandId.CloseTab, "Close active tab");
    public const string ToolbarNewQueryIcon = "+";
    public const string ToolbarNewQueryTooltip = "New saved query";
    public const string ToolbarToggleQueryPanelIcon = "▤";
    public const string ToolbarToggleQueryPanelTooltip = "Show / hide saved queries panel";
    public const string ToolbarFormatSqlIcon = "⎄";
    // ⚠ This constant is why CommandTip exists: it said "Alt+F" for a whole etap after the gesture became
    // Ctrl+K, and nothing failed. It can no longer disagree with the catalog.
    public static readonly string ToolbarFormatSqlTooltip = CommandTip.For(CommandId.FormatSql, "Format SQL");
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
    // Compile pre-condition refusals, shared by EVERY object editor's compile and the debugger's Save
    // (UX Polish Seam 6b). An ISavableObjectEditor adapter reads success as "no error after the attempt",
    // so a compile that cannot run must SAY so — otherwise the save-and-close WorkGuard is told the work
    // was written when nothing was, and discards it. NoConnectionMessage above covers the no-DDL-executor
    // case; this one covers "the buffer holds nothing to compile".
    public const string EditorNothingToCompile = "There is nothing to compile.";
    // ─── Change-safety refusals (ObjectChangeGate) ──────────────────────────────────────────────
    // Every object editor compiles by REPLACING a whole object (CREATE OR ALTER … AS <entire body>), so a
    // compile can discard work the editor never saw. These three sentences are the whole user-facing
    // vocabulary of that gate; {0} = the object's name.
    //
    // Each one names the effect first, then the ONE next step. "Revert" and the SQL Editor are both
    // existing features, deliberately: the escape hatch for a deliberate overwrite already exists (run the
    // statement yourself, where the console makes it unmistakably your decision), which is why the gate
    // ships with no force-overwrite button of its own.
    public const string ObjectChangedInDatabaseFormat =
        "{0} was changed in the database after this tab opened it, so compiling now would discard that newer " +
        "version. Nothing was written. Use Revert to load the current definition, then re-apply your changes " +
        "— or run your statement in the SQL Editor if you mean to overwrite it.";
    public const string ObjectAlreadyExistsFormat =
        "{0} already exists. This editor creates objects with CREATE OR ALTER, which would overwrite it " +
        "rather than fail. Nothing was written. Choose a different name, or close this tab and open the " +
        "existing object to edit it.";
    public const string ObjectChangeUnverifiableFormat =
        "EmberTern could not read the current state of {0}, so it cannot confirm that compiling would not " +
        "overwrite newer work. Nothing was written. Check the connection and try again.";
    // Fallback label when the object has no name yet — the message must still read as a sentence.
    public const string ObjectChangeUnnamedObject = "This object";
    // ─── Settings health (audit A-03) ───────────────────────────────────────────────────────────
    // Shown when settings.dat exists but this build cannot read it. Saving is refused for the whole session so
    // the unreadable file is never replaced, which means nothing the user does will persist — and that has to
    // be said out loud, with the path (so they can back it up or move it) and the reason (so they can tell a
    // wrong-machine DPAPI file, which is intact, from a damaged one).
    // {0} = full path to settings.dat, {1} = the load diagnostic.
    public const string SettingsUnreadableWarningFormat =
        "Your settings file could not be read, so EmberTern will not save settings this session — connections, " +
        "saved queries, workspace and grid layouts will not persist. Nothing has been lost and the existing " +
        "file has been left untouched: it is most often readable on the Windows account or machine that wrote " +
        "it. File: {0} — {1}";
    // The code-action light bulb (Stage Q / Q3) — a discreet affordance for the same menu Ctrl+. opens.
    public static readonly string CodeActionsTooltip = CommandTip.For(
        CommandId.EditorQuickFix, "Show code actions");
    // Shown at the foot of the diagnostic hover when fixes exist there. Information only — the hover
    // never offers an action (§15.1.1); this just makes the shortcut discoverable.
    public static readonly string CodeActionsHoverHint = CommandTip.For(
        CommandId.EditorQuickFix, "Quick Fix available");
    // Diagnostics-panel row → the same menu (Stage Q / Q5).
    public const string CodeActionsMenuItem = "Quick Fix…";
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
    // Context-menu labels for the same two commands, in the surface's New / Edit / Delete vocabulary.
    public const string DataEditNewRow = "New row";
    public const string DataEditDeleteRow = "Delete row";
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
    public static readonly string NewTableDialogCompile = CommandTip.For(CommandId.Compile, "Compile");
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
    public static readonly string ViewCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile view (CREATE OR ALTER VIEW)");
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
    // Members Debug toolbar button (D15.3 Seam E) — an icon-only editor-toolbar action; the tooltip explains
    // availability (the button carries no text label, so there is no button-caption string).
    public const string PackageDebugMemberTooltipReady = "Debug the selected member";
    public const string PackageDebugMemberTooltipNotDebuggable = "The selected member cannot be debugged.";
    public const string PackageDebugMemberTooltipNoSelection = "Select a procedure or function in the list to debug it.";
    public const string PackageDetailLoadingHint = "Loading package…";
    public const string PackageDetailDependsOnHeader = "Depends on";
    public const string PackageDetailDependedOnByHeader = "Used by";
    public const string ToolbarNewPackageTooltip = "New Package";
    public static readonly string PackageCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile package (header then body)");
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
    // The collection surface names its operations ONE way, whether the user reaches them from the toolbar or
    // from the context menu. The verbs are New / Edit / Delete / Move — the nomenclature the fields menu
    // already used and the toolbar did not ("Add item" / "Remove item", for the very same commands).
    //
    // ⚠ The {0} is the ACTIVE collection's own noun (below), supplied by MainWindowViewModel — which is why
    // the toolbar tooltips are computed properties rather than constants: the same button is "New field" on a
    // table's fields and "New parameter" on a procedure's arguments.
    public const string CollectionNewFormat = "New {0}";
    public const string CollectionEditFormat = "Edit {0}";
    public const string CollectionDeleteFormat = "Delete {0}";

    public const string CollectionNounField = "field";
    public const string CollectionNounRow = "row";
    public const string CollectionNounColumn = "column";
    public const string CollectionNounVariable = "variable";
    // The fallback, and the honest name for the routed collections whose sub-tab decides what the items are
    // (a procedure's arguments / variables / cursors / subprograms all share one command pair).
    public const string CollectionNounItem = "item";

    // Menu labels for the collections whose grids are edited in place — a generic noun, but the same verbs.
    // (These are LABELS. They used to be the tooltip constants above, reused as MenuItem headers, which is
    // how "Add item" ended up as a menu entry.)
    public const string CollectionMenuNew = "New item";
    public const string CollectionMenuDelete = "Delete item";
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
    public static readonly string GeneratorCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile generator (CREATE / ALTER SEQUENCE)");
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
    public static readonly string ExceptionCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile exception (CREATE / ALTER EXCEPTION)");
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
    public static readonly string IndexCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile index changes (ALTER INDEX / COMMENT ON INDEX)");
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
    public static readonly string DomainCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile domain (CREATE / ALTER DOMAIN)");
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
    public static readonly string ProcedureCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile procedure (CREATE OR ALTER PROCEDURE)");
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
    public static readonly string FunctionCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile function (CREATE OR ALTER FUNCTION)");
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
    // ⚠⚠ NEUTRAL ON PURPOSE (user decision, 2026-08-03). This dialog stopped being only about procedures long
    // ago: Smart SQL Parameters reuses it to collect values for ANY statement carrying `:name` placeholders, so a
    // plain INSERT or UPDATE OR INSERT opened a window headed "Execute Procedure". The user read that as the
    // Execute-Procedure feature misfiring — *"To nie jest wywołanie procedury"* — which is exactly what a
    // mislabelled surface causes: the behaviour was correct and only the label lied.
    // ⛔ Do not narrow it back to a procedure-specific wording; the reuse is the design (one parameter editor, not
    // two), and the dialog is reached from procedure execution AND from F5 on parameterised SQL.
    public const string ProcedureExecuteDialogTitle = "Execute";
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
    public static readonly string TriggerCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile trigger (CREATE OR ALTER TRIGGER)");
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
    public static readonly string FieldEditCompileTooltip = CommandTip.For(
        CommandId.Compile, "Compile pending changes (apply DDL + auto-commit)");
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
    // (FieldEditEditTooltip — "Edit selected field · F2" — was removed in the UX Consistency Pass. It had no
    // consumer: the toolbar's Edit button it was written for never existed, which is exactly the gap the pass
    // closed. The button now uses MainWindowViewModel.CollectionEditTooltip, which names the active
    // collection's noun and takes its gesture from the catalog.)
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
    public static readonly string SessionManagerAnalyzeTip = CommandTip.Sentence(
        CommandId.Go,
        "Open in the SQL Editor and reveal the Performance tab — run it ({0}) to analyze "
        + "(it is not run automatically)");
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
    public static readonly string ToolbarGlobalSearchTooltip = CommandTip.For(
        CommandId.GlobalSearch, "Global Search");

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
    public const string MetadataContextDebugTrigger = "Debug trigger…";
    public const string MetadataContextDebugFunction = "Debug function…";
    public const string DebuggerTabTitleFormat = "Debug: {0}";
    // Launch panel.
    public const string DebuggerLaunchHeader = "Launch debug session";
    public const string DebuggerLaunchParametersHeader = "Input parameters";
    public const string DebuggerLaunchNoParameters = "This routine takes no input parameters.";
    // Compact launch form (D15.3 Seam A) — the inline NULL toggle beside each value field.
    public const string DebuggerParamNullLabel = "null";
    public const string DebuggerParamNullTooltip = "Set this parameter to NULL";
    // The ONE marker for a value the app supplied rather than the user typing it here — used by every
    // automatic mechanism (parameter history, carry-over across a rebuilt panel, and whatever comes next), so
    // the user learns one convention instead of one per feature. The label says THAT it was filled in; the
    // tooltip says by which mechanism. It disappears the moment the value is edited.
    // Two words, not one word in two colours: Restored is the ordinary case and stays quiet, while Assumed is
    // the ONE inference the panel makes and has to be recognisable at a glance, without reading the tooltip.
    public const string LaunchValueRestoredMarker = "auto";
    public const string LaunchValueAssumedMarker = "assumed";
    public const string LaunchValueRestoredTooltip =
        "Filled in automatically — this value was kept because it provably still fits this parameter.";
    public const string LaunchValueAssumedTooltip =
        "Filled in automatically, on an assumption: after matching by name this was the only parameter left on "
        + "each side with a matching type, so the value was carried over. Check it before running.";
    // Advanced section (D15.3 Seam B) — collapsed by default; transaction isolation lives here, out of the
    // main Launch flow (most users never change it). The note leads with WHAT the option changes, then names
    // the levels; the selector below shows the current level.
    public const string DebuggerAdvancedSection = "Advanced options";
    public const string DebuggerLaunchIsolationLabel = "Transaction isolation";
    public const string DebuggerIsolationReadCommitted = "Read Committed";
    public const string DebuggerIsolationSnapshot = "Snapshot";
    public const string DebuggerIsolationNote =
        "Controls which committed changes from other sessions you see while stepping. Read Committed shows " +
        "changes other sessions commit during the run; Snapshot gives a consistent view from the moment you " +
        "start, unchanged to the end. Either way the debug session runs in its own transaction and is rolled " +
        "back when it ends.";
    // The shortcut surfaces Seam C's keyboard-first launch — the whole operation is reachable from the keyboard.
    // The label carries no parenthesised shortcut; the key is rendered in the shared shortcut-chip beside it.
    public const string DebuggerLaunchButton = "Start debugging";
    // The shortcut chip on the launch button — F5 means Start Debugging here, which is CommandId.Go on a
    // debugger tab (the one ratified contradiction with the SQL editor's Execute).
    public static readonly string DebuggerLaunchShortcut = CommandTip.Gesture(CommandId.Go);
    public const string DebuggerLaunchPreparing = "Preparing…";
    // Pre-flight report (§9.2 / §4.6). D15.3 polish: the section is shown ONLY when it has something to say —
    // no header, and no "all clear" line when clean (a clean launch form stays maximally quiet). Each surfaced
    // item is a severity-striped row in the Error Bar visual language (warning = Alert Triangle / WarningBrush,
    // blocking = octagon / ErrorBrush), so there is no header/clean string here anymore.
    public const string DebuggerPreflightAutonomousTx =
        "Contains IN AUTONOMOUS TRANSACTION — work committed there is permanent and survives the debug rollback.";
    public const string DebuggerPreflightGenerator =
        "Uses a generator/sequence (GEN_ID / NEXT VALUE FOR) — generator values are consumed permanently and are not restored on rollback.";
    public const string DebuggerPreflightUnsteppable =
        "The routine source could not be parsed into step points — debugging cannot start.";
    // Toolbar / commands. Every gesture below comes from CommandCatalog — the debugger's stepping keys are
    // declared there as CommandDispatch.Reserved (dispatched by DebuggerTabView, which owns the caret), and
    // being declared is exactly what lets them be shown here without being re-typed.
    public static readonly string DebuggerContinueTooltip = CommandTip.For(CommandId.Go, "Continue");
    public static readonly string DebuggerStepIntoTooltip =
        CommandTip.For(CommandId.DebuggerStepInto, "Step Into");
    public static readonly string DebuggerStepOverTooltip =
        CommandTip.For(CommandId.DebuggerStepOver, "Step Over");
    public static readonly string DebuggerStepOutTooltip =
        CommandTip.For(CommandId.DebuggerStepOut, "Step Out");
    public static readonly string DebuggerRunToCursorTooltip =
        CommandTip.For(CommandId.DebuggerRunToCursor, "Run To Cursor");
    public const string DebuggerRunToCursorMenu = "Run to Cursor";
    public static readonly string DebuggerStopTooltip =
        CommandTip.For(CommandId.DebuggerStop, "Stop debugging");
    public static readonly string DebuggerRestartTooltip =
        CommandTip.For(CommandId.DebuggerRestart, "Restart");
    public static readonly string DebuggerToggleBreakpointTooltip =
        CommandTip.For(CommandId.DebuggerToggleBreakpoint, "Toggle breakpoint");
    // Status line.
    public const string DebuggerStatusReady = "Ready to launch.";
    public const string DebuggerStatusPausedFormat = "Paused at line {0} — {1}";
    public const string DebuggerStatusRunning = "Running…";
    public const string DebuggerStatusCompleted = "Completed — transaction rolled back.";
    // Short, fixed-height headline; the full Firebird message goes to the Error Bar (D15.2 Seam C).
    public const string DebuggerStatusFaulted = "Unhandled exception — transaction rolled back.";
    public const string DebuggerStatusStopped = "Stopped — transaction rolled back.";
    public const string DebuggerStatusLaunchFailedFormat = "Could not start the debug session: {0}";
    public const string DebuggerStopReasonEntry = "entry";
    public const string DebuggerStopReasonStep = "step";
    public const string DebuggerStopReasonBreakpoint = "breakpoint";
    // Advanced-breakpoint stop reasons (D12, spec §9.8).
    public const string DebuggerStopReasonException = "exception";
    public const string DebuggerStopReasonSuspend = "suspended";
    public const string DebuggerStopReasonDataBreakpoint = "data change";
    public const string DebuggerStopReasonDataChangedFormat = "data breakpoint — {0} changed";
    public const string DebuggerStopReasonConditionErrorFormat = "breakpoint condition error — {0}";
    // Error Bar (D15.2 Seam C) — its own thin row below the toolbar; shows on a fault / Break-on-Exception pause.
    public const string DebuggerErrorUnknown = "Unknown error";
    // Friendly error text (D15.4 Seam B) — one short, categorised line per FriendlyErrorCategory, shown on the
    // three expression surfaces (Immediate result / Watch value / breakpoint-condition reason). The raw
    // Firebird message stays reachable (row tooltip, Executed SQL, Error Bar) — "friendly + raw available".
    public const string DebuggerFriendlyUserExceptionFormat = "Exception raised: {0}";
    public const string DebuggerFriendlyConstraint =
        "A database constraint was violated (NOT NULL, CHECK, or unique key).";
    public const string DebuggerFriendlySqlError =
        "SQL error — check the expression's syntax and that all names exist.";
    public const string DebuggerFriendlyRawTooltip = "Full Firebird message";
    // Save + compile from the debugger tab (UX Polish Seam 5b). Saving is a deliberate new work cycle: it
    // ends a live session (which was compiled from the old code) before recompiling the routine.
    public const string DebuggerSave = "Save";
    public static readonly string DebuggerSaveTooltip = CommandTip.For(
        CommandId.DebuggerSaveSource, "Save and compile the routine");
    public const string DebuggerSaveUnavailable = "This debugger tab cannot save (no connection).";
    // (the empty-buffer refusal is the shared EditorNothingToCompile — one wording for every editor)
    public const string DebuggerSaveEndsSessionTitle = "Save ends the debug session";
    public const string DebuggerSaveEndsSessionMessage =
        "Saving recompiles {0}, so the running debug session no longer matches the code.\n\n"
        + "The session will be stopped and its transaction rolled back before compiling. "
        + "You can start debugging again straight away with the new code.";
    public const string DebuggerSaveEndsSessionConfirm = "Stop session and save";
    public const string DebuggerSaveCompileFailedFormat = "Compile failed: {0}";
    public const string DebuggerStatusSaved = "Saved and compiled.";
    // Save during a debugging cycle: the session is rebuilt on the compiled code with the settings the user
    // already made, so they land back where they were instead of re-entering the launch form.
    public const string DebuggerStatusSavedRestarting = "Saved — restarting the session…";
    // The compile was refused: the tab stays on the source so the code can be fixed and saved again.
    // The server's own message is in the Error Bar — this is only the short status-line headline.
    public const string DebuggerStatusSaveFailed = "Save failed — fix the code and save again.";
    // The first edit during a live session ends it: the session was built from the text that just changed, so
    // stepping on would run code the user can no longer see. Says what happened AND what to do next — the
    // toolbar going grey is the visual cue, this is the reason. (Until Restart can run the edited text without
    // saving, Save is the way back into a session — hence naming it here.)
    public static readonly string DebuggerStatusEndedByEdit = CommandTip.Sentence(
        CommandId.DebuggerRestart,
        "Session ended — the code changed. Restart ({0}) runs the current code without saving.");
    // The routine's HEADER changed, so the parameter list the engine reads from the catalog no longer describes
    // this text and a draft-sourced session cannot be started from it yet. Names the one way forward.
    public static readonly string DebuggerStatusEndedByHeaderEdit = CommandTip.Sentence(
        CommandId.DebuggerSaveSource,
        "Session ended — the routine header changed. Save ({0}) to compile and debug the new signature.");
    public const string DebuggerUnsavedSourceFormat = "{0} — modified source (not compiled)";
    // Variables panel.
    public const string DebuggerVariablesHeader = "Variables";
    public const string DebuggerVariablesEmpty = "No variables in the current frame.";
    public const string DebuggerVariablesColumnName = "Name";
    public const string DebuggerVariablesColumnValue = "Value";
    public const string DebuggerVariablesColumnKind = "Kind";
    public const string DebuggerVariableNull = "<null>";
    public const string DebuggerVariableKindParameter = "param";
    public const string DebuggerVariableKindLocal = "local";
    public const string DebuggerVariableKindIn = "IN";
    public const string DebuggerVariableKindOut = "OUT";
    public const string DebuggerVariableKindContextNew = "NEW record";
    public const string DebuggerVariableKindContextOld = "OLD record";
    public const string DebuggerVariableKindReturn = "return";
    public const string DebuggerVariableGroupPinned = "Pinned";
    public const string DebuggerVariableGroupContext = "Context";
    public const string DebuggerVariableGroupParameters = "Parameters";
    public const string DebuggerVariableGroupLocals = "Locals";
    // D-function: the return-value row/group shown only when a function is the debug root. The row displays
    // "not returned yet" until RETURN runs (the session completes at RETURN), then the returned value.
    public const string DebuggerVariableGroupReturn = "Return";
    public const string DebuggerReturnRowName = "«return»";
    public const string DebuggerReturnPending = "— (not returned yet)";
    public const string DebuggerVariableFilterWatermark = "Filter variables…";
    public const string DebuggerVariablePinTooltip = "Pin to top / unpin";
    public const string DebuggerVariableEditTooltip = "Double-click to edit (Enter to apply, Esc to cancel)";
    public const string DebuggerVariableBlobFormat = "[BLOB · {0} B]";
    // Call stack (single-frame in D4, but the header exists).
    public const string DebuggerCallStackHeader = "Call stack";
    // Errors.
    public const string DebuggerNoConnection = "Connect to a database before debugging.";
    public const string ProcedureDebugTooltip = "Debug procedure";
    public const string TriggerDebugTooltip = "Debug trigger";
    public const string FunctionDebugTooltip = "Debug function";
    public const string DebuggerSourceUnavailableFormat = "Could not load the source of {0}.";
    // Trigger debugging (Stage X / D10) — the launch panel's NEW/OLD context editors + the out-of-scope refusal.
    public const string DebuggerTriggerOutOfScope =
        "Only relation triggers (BEFORE/AFTER INSERT/UPDATE/DELETE) can be debugged — database-level and DDL triggers are out of scope.";
    public const string DebuggerTriggerActionLabel = "Fires for";
    public const string DebuggerTriggerActionInsert = "INSERT";
    public const string DebuggerTriggerActionUpdate = "UPDATE";
    public const string DebuggerTriggerActionDelete = "DELETE";
    public const string DebuggerTriggerNewHeader = "NEW values";
    public const string DebuggerTriggerOldHeader = "OLD values";
    public const string DebuggerTriggerNoColumns = "This trigger references no columns of this record.";
    // Bottom tabbed panel (D5 layout redesign) — extensible: Call Stack / Breakpoints / Output join later.
    public const string DebuggerBottomTabImmediate = "Immediate";
    public const string DebuggerBottomTabWatches = "Watches";
    public const string DebuggerBottomTabCallStack = "Call Stack";
    public const string DebuggerBottomTabBreakpoints = "Breakpoints";
    public const string DebuggerBottomTabResults = "Results";
    // Run to next SUSPEND + its result grid (D12 Seam E2, spec §9.8). The button label is now an
    // SvgIcon + text (D15.2 Seam A); only the tooltip remains here.
    public const string DebuggerRunToSuspendTooltip =
        "Run to next SUSPEND — produce the next result row of a selectable procedure (rows collect in the Results tab).";
    // Loop fast-forward (D13) — enabled only while paused inside a WHILE / FOR loop.
    public const string DebuggerRunToNextIterationTooltip =
        "Next Iteration — finish the current loop iteration and pause at the start of the next (or after the loop if it exits). Available inside a loop.";
    public const string DebuggerRunToLoopExitTooltip =
        "Continue Until Loop Exit — run the rest of the current loop and pause just after it (any exit: condition, LEAVE/BREAK, EXIT). Available inside a loop.";
    public const string DebuggerResultsEmpty =
        "No rows yet. Use “Suspend” to run a selectable procedure to its next SUSPEND; each emitted row is collected here.";
    // Breakpoints panel (D12 Seam E, spec §9.8) — a pure view of the Core Breakpoint / DataBreakpoint objects.
    public const string DebuggerBreakpointsEmpty =
        "No breakpoints. Click the editor gutter to add a line breakpoint; right-click a variable → " +
        "\"Break when changes\" for a data breakpoint.";
    public const string DebuggerBreakpointsLineHeader = "Line breakpoints";
    public const string DebuggerBreakpointsDataHeader = "Data breakpoints (break on change)";
    public const string DebuggerBreakpointLineFormat = "Line {0}";
    public const string DebuggerBreakpointConditionWatermark = "condition, e.g. IDX = 3";
    public const string DebuggerBreakpointWhenLabel = "when";
    public const string DebuggerBreakpointHitsLabel = "hits";
    public const string DebuggerBreakpointRemoveTooltip = "Remove breakpoint";
    public const string DebuggerBreakOnException = "Break on exception";
    public const string DebuggerBreakOnExceptionTooltip =
        "Pause at the raising statement before the exception is routed to a WHEN … DO handler (spec §9.8.1).";
    public const string DebuggerDataBreakpointMenu = "Break when changes";
    // Hit-count kinds, in HitCountKind order (Always / Exactly / AtLeast / Multiple).
    public const string DebuggerHitCountAlways = "always";
    public const string DebuggerHitCountExactly = "= N";
    public const string DebuggerHitCountAtLeast = "≥ N";
    public const string DebuggerHitCountMultiple = "every N";
    // Harness Log (Sprint D10.5) — a DEBUG-only diagnostic surface for developing/diagnosing the debugger
    // itself. It is built in code-behind under #if DEBUG (DebuggerTabView.axaml.cs), so these strings are
    // referenced only in DEBUG builds; in RELEASE they are simply unused consts. It replaced the misnamed
    // "Executed SQL" tab (that name read as the user's SQL history, which it never was).
    public const string DebuggerBottomTabHarnessLog = "Harness Log";
    public const string DebuggerHarnessLogDescription =
        "Diagnostic tool (debug builds only). Shows the EXECUTE BLOCK harnesses the debugger generates " +
        "internally to evaluate expressions and statements on the server — this is how the debugger works " +
        "under the hood, not a history of your SQL.";
    public static readonly string DebuggerHarnessLogEmpty = CommandTip.Sentence(
        CommandId.DebuggerEvaluateSelection,
        "No harnesses generated yet. Evaluate an expression ({0}) or run an Immediate statement while "
        + "the session is paused, and the generated harness SQL will appear here.");
    public const string DebuggerBottomPanelCollapseTooltip = "Collapse / expand the panel";
    // Call Stack panel (D8, spec §5).
    public const string DebuggerCallStackEmpty = "No call stack — not paused.";
    public const string DebuggerCallStackLineFormat = "line {0}";
    public const string DebuggerCallStackSimulatedGlyph = "△";
    public const string DebuggerCallStackSimulatedTooltip =
        "Simulated frame — reached by Step Into (interpreted), which can differ from real execution.";
    public const string DebuggerCallStackPeekHeaderFormat = "{0} — line {1}";
    // Expression evaluation — Evaluate / Immediate / Executed SQL (D5, spec §9.5 / §10.3).
    public const string DebuggerImmediateHeader = "Immediate / Executed SQL";
    public const string DebuggerImmediateWatermark = "Evaluate an expression, e.g. v_counter * 2";
    // A short line of valid-expression examples shown under the Immediate/Watches empty-state (D15.4 Seam A —
    // hints). Kept concise and separated by "·"; these are illustrative shapes, not references to real vars.
    public const string DebuggerExpressionExamples =
        "Examples: v_counter * 2 · v_status = 'OK' · char_length(v_text)";
    public const string DebuggerImmediateAsStatement = "as statement";
    public const string DebuggerImmediateAsStatementTooltip =
        "Run the text as a PSQL statement against the live frame (may assign variables). Off: evaluate it as an expression.";
    public const string DebuggerImmediateEvaluateButton = "Evaluate";
    public const string DebuggerImmediateClearTooltip = "Clear";
    public static readonly string DebuggerEvaluateSelectionTooltip = CommandTip.For(
        CommandId.DebuggerEvaluateSelection, "Evaluate the selected expression");
    public const string DebuggerImmediateEmpty = "No evaluations yet. Evaluate an expression, or select one in the source and press Shift+F9.";
    public const string DebuggerEvalKindExpression = "expression";
    public const string DebuggerEvalKindStatement = "statement";
    public const string DebuggerEvalStatementOk = "(statement ran)";
    public const string DebuggerEvalErrorUnknown = "evaluation failed";
    // Watches panel (D5 seam b, §9.5).
    public const string DebuggerWatchesHeader = "Watches";
    public const string DebuggerWatchWatermark = "Watch an expression, e.g. v_status = 'OK'";
    public const string DebuggerWatchAddButton = "Add";
    public const string DebuggerWatchAddTooltip = "Add a watch (re-evaluated after every step)";
    public const string DebuggerWatchRemoveTooltip = "Remove watch";
    public const string DebuggerWatchesEmpty = "No watches. Add an expression to re-evaluate after every step.";
    public const string DebuggerWatchNotEvaluated = "—";
    public const string DebuggerWatchSideEffectTooltip =
        "This watch is not a pure expression — it runs real SQL in the debug transaction each time it is re-evaluated, and may have side effects.";
}
