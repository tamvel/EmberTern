using EmberTern.App.Commands;
using EmberTern.App.Localization;

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
    // ── LOCALIZATION ─────────────────────────────────────────────────────────────────────────────────────
    //
    // ⭐ A localized member is a PROPERTY reading `Loc.Text(nameof(X))` — never a `const`, never a
    //    `static readonly` field. The member NAME is the resource key in `Localization/Strings.resx`, so
    //    there is one owner of a key and no second naming scheme to keep in step.
    //
    // ⚠⚠ THE SHAPE IS DICTATED BY LIVE SWITCHING (decision D‑1, ratified 2026-08-09) AND ALL THREE FORMS
    //    DIFFER IN A WAY THAT MATTERS:
    //      `const`          — inlined by the compiler; after the build there is no field left to resolve,
    //                         so no resource plumbing can ever reach it.
    //      `static readonly`— resolved ONCE at type initialization; correct for a restart-only design and
    //                         silently frozen in the first language for a live one.
    //      property         — resolved at the moment of the call. This one.
    //
    // ⚠ For XAML the equivalent rule is `{app:Loc Key}`, not `{x:Static app:UiStrings.Key}`: `x:Static` is
    //    not a binding and never re-evaluates. These members remain for C# consumers, which read them at
    //    call time and therefore follow the language for free.
    //
    // ⚠ A consumer that CAPTURES the text once (a tab header assigned on open, a grid column built in
    //    code-behind) still needs `Loc.LanguageChanged` to rebuild — a property cannot help something that
    //    never asks again.

    public static string AppTitle => Loc.Text(nameof(AppTitle));
    public static string ExecRowsReadFormat => Loc.Text(nameof(ExecRowsReadFormat));
    public static string ExecRowsReadSuffixFormat => Loc.Text(nameof(ExecRowsReadSuffixFormat));
    public static string ObjectKindLowerDomain => Loc.Text(nameof(ObjectKindLowerDomain));
    public static string ObjectKindLowerException => Loc.Text(nameof(ObjectKindLowerException));
    public static string ObjectKindLowerFunction => Loc.Text(nameof(ObjectKindLowerFunction));
    public static string ObjectKindLowerGenerator => Loc.Text(nameof(ObjectKindLowerGenerator));
    public static string ObjectKindLowerIndex => Loc.Text(nameof(ObjectKindLowerIndex));
    public static string ObjectKindLowerPackage => Loc.Text(nameof(ObjectKindLowerPackage));
    public static string ObjectKindLowerProcedure => Loc.Text(nameof(ObjectKindLowerProcedure));
    public static string ObjectKindLowerRole => Loc.Text(nameof(ObjectKindLowerRole));
    public static string ObjectKindLowerSystemTable => Loc.Text(nameof(ObjectKindLowerSystemTable));
    public static string ObjectKindLowerTable => Loc.Text(nameof(ObjectKindLowerTable));
    public static string ObjectKindLowerTrigger => Loc.Text(nameof(ObjectKindLowerTrigger));
    public static string ObjectKindLowerUser => Loc.Text(nameof(ObjectKindLowerUser));
    public static string ObjectKindLowerView => Loc.Text(nameof(ObjectKindLowerView));
    // ⭐ Lowercase PLURAL nouns, and they exist as their own entries rather than as
    // `ObjectKindLower* + "s"`: a plural morpheme is a fact about ENGLISH, so composing one in code
    // makes the result untranslatable (Polish "procedura" → "procedur", not "procedura"+"s").
    // ⛔ Only the four kinds `MetadataNodeViewModel.IsRecompilableGroup` admits — a key with no
    // producer is dead weight (#233), and the set is pinned by
    // `RecompileGroupTexts_CoverExactlyTheRecompilableKinds`.
    public static string ObjectKindPluralFunction => Loc.Text(nameof(ObjectKindPluralFunction));
    public static string ObjectKindPluralPackage => Loc.Text(nameof(ObjectKindPluralPackage));
    public static string ObjectKindPluralProcedure => Loc.Text(nameof(ObjectKindPluralProcedure));
    public static string ObjectKindPluralTrigger => Loc.Text(nameof(ObjectKindPluralTrigger));
    public static string ParameterHelperMemberFormat => Loc.Text(nameof(ParameterHelperMemberFormat));
    public static string PeekHeaderFormat => Loc.Text(nameof(PeekHeaderFormat));
    public static string SecurityRolesEmptyFormat => Loc.Text(nameof(SecurityRolesEmptyFormat));
    public static string SessionRowSelfSuffix => Loc.Text(nameof(SessionRowSelfSuffix));
    public static string StatusQueryPrefix => Loc.Text(nameof(StatusQueryPrefix));
    public static string TableDetailAddConstraintFormat => Loc.Text(nameof(TableDetailAddConstraintFormat));
    public static string TableDetailAddForeignKeyPrefix => Loc.Text(nameof(TableDetailAddForeignKeyPrefix));
    public static string TableDetailAddIndexPrefix => Loc.Text(nameof(TableDetailAddIndexPrefix));
    public static string TraceFormatMinutesFormat => Loc.Text(nameof(TraceFormatMinutesFormat));
    public static string TraceFormatMsFormat => Loc.Text(nameof(TraceFormatMsFormat));
    public static string TraceFormatSecondsFormat => Loc.Text(nameof(TraceFormatSecondsFormat));
    public static string TraceLensDurationSummaryFormat => Loc.Text(nameof(TraceLensDurationSummaryFormat));
    public static string TraceLensTransactionSummaryFormat => Loc.Text(nameof(TraceLensTransactionSummaryFormat));
    public static string TraceTimingFetchesFormat => Loc.Text(nameof(TraceTimingFetchesFormat));
    public static string TraceTimingMsFormat => Loc.Text(nameof(TraceTimingMsFormat));
    public static string TraceTimingReadsFormat => Loc.Text(nameof(TraceTimingReadsFormat));
    public static string TraceTimingRowsManyFormat => Loc.Text(nameof(TraceTimingRowsManyFormat));
    public static string TraceTimingRowsOneFormat => Loc.Text(nameof(TraceTimingRowsOneFormat));
    public static string TraceTimingWritesFormat => Loc.Text(nameof(TraceTimingWritesFormat));
    public static string ConnectionCopySuffix => Loc.Text(nameof(ConnectionCopySuffix));
    public static string ConnectionNoPath => Loc.Text(nameof(ConnectionNoPath));
    public static string DpapiWindowsOnly => Loc.Text(nameof(DpapiWindowsOnly));
    public static string DurationMsFormat => Loc.Text(nameof(DurationMsFormat));
    public static string ExecutionCaptureMonAttachment => Loc.Text(nameof(ExecutionCaptureMonAttachment));
    public static string ExecutionCaptureMonStatement => Loc.Text(nameof(ExecutionCaptureMonStatement));
    public static string ExecutionCapturePlanOnly => Loc.Text(nameof(ExecutionCapturePlanOnly));
    public static string ExecutionCaptureTrace => Loc.Text(nameof(ExecutionCaptureTrace));
    public static string ExecutionCopyCaptureLabel => Loc.Text(nameof(ExecutionCopyCaptureLabel));
    public static string ExecutionCopyPlanLabelFormat => Loc.Text(nameof(ExecutionCopyPlanLabelFormat));
    public static string ExecutionCopyTimingsLabel => Loc.Text(nameof(ExecutionCopyTimingsLabel));
    public static string ExecutionPlanDialectExplain => Loc.Text(nameof(ExecutionPlanDialectExplain));
    public static string ExecutionPlanDialectLegacy => Loc.Text(nameof(ExecutionPlanDialectLegacy));
    public static string ExecutionTimingsExecute => Loc.Text(nameof(ExecutionTimingsExecute));
    public static string ExecutionTimingsFetch => Loc.Text(nameof(ExecutionTimingsFetch));
    public static string ExecutionTimingsPrepare => Loc.Text(nameof(ExecutionTimingsPrepare));
    public static string ExportDelimitedOptionsRequired => Loc.Text(nameof(ExportDelimitedOptionsRequired));
    public static string ExportFormatUnsupportedFormat => Loc.Text(nameof(ExportFormatUnsupportedFormat));
    public static string ExportInsertTargetRequired => Loc.Text(nameof(ExportInsertTargetRequired));
    public static string ExportUpdateTargetRequired => Loc.Text(nameof(ExportUpdateTargetRequired));
    public static string FilePickerCsvTxt => Loc.Text(nameof(FilePickerCsvTxt));
    public static string FilePickerExcel => Loc.Text(nameof(FilePickerExcel));
    public static string FilePickerFirebirdDatabases => Loc.Text(nameof(FilePickerFirebirdDatabases));
    public static string FilePickerSelectDatabase => Loc.Text(nameof(FilePickerSelectDatabase));
    public static string FilePickerSqlScripts => Loc.Text(nameof(FilePickerSqlScripts));
    public static string FilterPlaceholder => Loc.Text(nameof(FilterPlaceholder));
    public static string FindingConfidenceHigh => Loc.Text(nameof(FindingConfidenceHigh));
    // ⭐ C7 (D‑7): the Performance findings' severity chip. It was four hardcoded literals in
    // `FindingViewModel` — invisible to `NoViewCarriesAHardcodedUserVisibleString`, which scans .axaml only
    // (#337), while its twin `DiagnosticRowViewModel` had read `UiStrings.DiagnosticSeverity*` all along.
    public static string FindingSeverityHigh => Loc.Text(nameof(FindingSeverityHigh));
    public static string FindingSeverityInfo => Loc.Text(nameof(FindingSeverityInfo));
    public static string FindingSeverityLow => Loc.Text(nameof(FindingSeverityLow));
    public static string FindingSeverityMedium => Loc.Text(nameof(FindingSeverityMedium));
    public static string FindingConfidenceLow => Loc.Text(nameof(FindingConfidenceLow));
    public static string FindingConfidenceMedium => Loc.Text(nameof(FindingConfidenceMedium));
    public static string ImportDateOrderIso => Loc.Text(nameof(ImportDateOrderIso));
    public static string ParameterHelperFunctionSuffix => Loc.Text(nameof(ParameterHelperFunctionSuffix));
    public static string ParameterHelperProcedureSuffix => Loc.Text(nameof(ParameterHelperProcedureSuffix));
    public static string PeekLoading => Loc.Text(nameof(PeekLoading));
    public static string PlaceholderFieldName => Loc.Text(nameof(PlaceholderFieldName));
    public static string PlaceholderTableName => Loc.Text(nameof(PlaceholderTableName));
    // ⛔⛔ `PlanInsightSubquery` was REMOVED in etap C7, and it must not come back. Its value ("Sub-query") was
    // FIREBIRD's word, matched with StartsWith against the engine's own plan text — so translating this entry
    // would have switched the sub-query summary off, silently, and invisibly in English (#356). The predicate
    // now has one owner in Core: `PlanNode.IsSubqueryRoot`.
    public static string QuickInfoMoreFormat => Loc.Text(nameof(QuickInfoMoreFormat));
    public static string ScriptDurationSecondsFormat => Loc.Text(nameof(ScriptDurationSecondsFormat));
    public static string SessionManagerWhatItMeans => Loc.Text(nameof(SessionManagerWhatItMeans));
    public static string SqlCopyKindFunction => Loc.Text(nameof(SqlCopyKindFunction));
    public static string SqlCopyKindNotATable => Loc.Text(nameof(SqlCopyKindNotATable));
    public static string SqlCopyKindProcedure => Loc.Text(nameof(SqlCopyKindProcedure));
    public static string SqlCopyKindSystemTable => Loc.Text(nameof(SqlCopyKindSystemTable));
    public static string SqlCopyKindView => Loc.Text(nameof(SqlCopyKindView));
    public static string StatusTracePrefix => Loc.Text(nameof(StatusTracePrefix));
    public static string TableAccessDelFormat => Loc.Text(nameof(TableAccessDelFormat));
    public static string TableAccessIdxFormat => Loc.Text(nameof(TableAccessIdxFormat));
    public static string TableAccessInsFormat => Loc.Text(nameof(TableAccessInsFormat));
    public static string TableAccessSeqFormat => Loc.Text(nameof(TableAccessSeqFormat));
    public static string TableAccessUpdFormat => Loc.Text(nameof(TableAccessUpdFormat));
    public static string TableColumnPickerFilterColumns => Loc.Text(nameof(TableColumnPickerFilterColumns));
    public static string TableColumnPickerFilterTables => Loc.Text(nameof(TableColumnPickerFilterTables));
    public static string TraceDetailFetchesFormat => Loc.Text(nameof(TraceDetailFetchesFormat));
    public static string TraceDetailPidPrefix => Loc.Text(nameof(TraceDetailPidPrefix));
    public static string TraceDetailReadsFormat => Loc.Text(nameof(TraceDetailReadsFormat));
    public static string TraceDetailTriggerEvent => Loc.Text(nameof(TraceDetailTriggerEvent));
    public static string TraceDetailWhatFired => Loc.Text(nameof(TraceDetailWhatFired));
    public static string TraceDetailWritesFormat => Loc.Text(nameof(TraceDetailWritesFormat));
    public static string TraceDroppedSuffixFormat => Loc.Text(nameof(TraceDroppedSuffixFormat));
    public static string TraceLensNoTransaction => Loc.Text(nameof(TraceLensNoTransaction));
    public static string TraceLensSystemEvents => Loc.Text(nameof(TraceLensSystemEvents));
    public static string TraceLensTransactionPrefix => Loc.Text(nameof(TraceLensTransactionPrefix));
    public static string TraceStateError => Loc.Text(nameof(TraceStateError));
    public static string TraceStatePaused => Loc.Text(nameof(TraceStatePaused));
    public static string TraceStateRecording => Loc.Text(nameof(TraceStateRecording));
    public static string TraceStateStarting => Loc.Text(nameof(TraceStateStarting));
    public static string TraceStateStopped => Loc.Text(nameof(TraceStateStopped));
    public static string TraceStateStopping => Loc.Text(nameof(TraceStateStopping));
    public static string ValueNone => Loc.Text(nameof(ValueNone));
    public static string VerdictGradeAcceptable => Loc.Text(nameof(VerdictGradeAcceptable));
    public static string VerdictGradeAnalyzed => Loc.Text(nameof(VerdictGradeAnalyzed));
    public static string VerdictGradeFast => Loc.Text(nameof(VerdictGradeFast));
    public static string VerdictGradeNeedsAttention => Loc.Text(nameof(VerdictGradeNeedsAttention));
    public static string VerdictGradeSlow => Loc.Text(nameof(VerdictGradeSlow));
    public static string VerdictRowsChangedManyFormat => Loc.Text(nameof(VerdictRowsChangedManyFormat));
    public static string VerdictRowsChangedOne => Loc.Text(nameof(VerdictRowsChangedOne));
    public static string VerdictRowsManyFormat => Loc.Text(nameof(VerdictRowsManyFormat));
    public static string VerdictRowsOne => Loc.Text(nameof(VerdictRowsOne));
    public static string VerdictRowsReadFormat => Loc.Text(nameof(VerdictRowsReadFormat));

    // ── Shared object-kind vocabulary ────────────────────────────────────────────────────────────────
    // ⭐ ONE owner per WORD, read by four independent mappers that used to carry their own copies:
    //   QuickInfoView.KindLabel · SqlCompletionData.DescribeKind · MetadataNodeViewModel.KindNounTitle ·
    //   NavigationController.KindLabel. They map FOUR DIFFERENT enums, so the mapping stays per-enum —
    //   what is shared is the vocabulary, not the switch. ⚠ Where two surfaces deliberately say different
    //   things for the same concept (a completion row says "Field"/"CTE", Quick Info says
    //   "Column"/"Common table expression") they read DIFFERENT keys — the terseness is a decision, and
    //   collapsing it would be a text change disguised as a cleanup.
    // ⛔ MainWindowViewModel's lowercase nouns ("table", "view") are NOT part of this family: they sit
    //   mid-sentence, and a language with grammatical case will not derive them from the title-cased word.
    public static string ObjectKindTable => Loc.Text(nameof(ObjectKindTable));
    public static string ObjectKindView => Loc.Text(nameof(ObjectKindView));
    public static string ObjectKindSystemTable => Loc.Text(nameof(ObjectKindSystemTable));
    public static string ObjectKindProcedure => Loc.Text(nameof(ObjectKindProcedure));
    public static string ObjectKindFunction => Loc.Text(nameof(ObjectKindFunction));
    public static string ObjectKindTrigger => Loc.Text(nameof(ObjectKindTrigger));
    public static string ObjectKindDomain => Loc.Text(nameof(ObjectKindDomain));
    public static string ObjectKindException => Loc.Text(nameof(ObjectKindException));
    public static string ObjectKindGenerator => Loc.Text(nameof(ObjectKindGenerator));
    public static string ObjectKindRole => Loc.Text(nameof(ObjectKindRole));
    public static string ObjectKindPackage => Loc.Text(nameof(ObjectKindPackage));
    public static string ObjectKindIndex => Loc.Text(nameof(ObjectKindIndex));
    public static string ObjectKindUser => Loc.Text(nameof(ObjectKindUser));
    public static string ObjectKindColumn => Loc.Text(nameof(ObjectKindColumn));
    public static string ObjectKindField => Loc.Text(nameof(ObjectKindField));
    public static string ObjectKindTableReference => Loc.Text(nameof(ObjectKindTableReference));
    public static string ObjectKindAlias => Loc.Text(nameof(ObjectKindAlias));
    public static string ObjectKindVariable => Loc.Text(nameof(ObjectKindVariable));
    public static string ObjectKindParameter => Loc.Text(nameof(ObjectKindParameter));
    public static string ObjectKindCte => Loc.Text(nameof(ObjectKindCte));
    public static string ObjectKindCteShort => Loc.Text(nameof(ObjectKindCteShort));
    public static string ObjectKindCursor => Loc.Text(nameof(ObjectKindCursor));
    public static string ObjectKindRecordAlias => Loc.Text(nameof(ObjectKindRecordAlias));
    public static string ObjectKindRecord => Loc.Text(nameof(ObjectKindRecord));
    public static string ObjectKindKeyword => Loc.Text(nameof(ObjectKindKeyword));
    public static string ObjectKindDefinition => Loc.Text(nameof(ObjectKindDefinition));
    public static string QuickInfoGroupColumns => Loc.Text(nameof(QuickInfoGroupColumns));
    public static string QuickInfoGroupParameters => Loc.Text(nameof(QuickInfoGroupParameters));
    public static string QuickInfoGroupReturns => Loc.Text(nameof(QuickInfoGroupReturns));
    public static string DebuggerContinueLabel => Loc.Text(nameof(DebuggerContinueLabel));
    public static string DebuggerStepIntoLabel => Loc.Text(nameof(DebuggerStepIntoLabel));
    public static string DebuggerStepOverLabel => Loc.Text(nameof(DebuggerStepOverLabel));
    public static string DebuggerStepOutLabel => Loc.Text(nameof(DebuggerStepOutLabel));
    public static string DebuggerRunToCursorLabel => Loc.Text(nameof(DebuggerRunToCursorLabel));
    public static string DebuggerRunToSuspendLabel => Loc.Text(nameof(DebuggerRunToSuspendLabel));
    public static string DebuggerNextIterationLabel => Loc.Text(nameof(DebuggerNextIterationLabel));
    public static string DebuggerLoopExitLabel => Loc.Text(nameof(DebuggerLoopExitLabel));
    public static string DebuggerStopLabel => Loc.Text(nameof(DebuggerStopLabel));
    public static string DebuggerRestartLabel => Loc.Text(nameof(DebuggerRestartLabel));
    public static string ConnectionCopy => Loc.Text(nameof(ConnectionCopy));
    public static string NewConnectionNamePlaceholder => Loc.Text(nameof(NewConnectionNamePlaceholder));
    public static string NewConnectionPathPlaceholder => Loc.Text(nameof(NewConnectionPathPlaceholder));
    public static string PerformanceLegendFullScanTooltip => Loc.Text(nameof(PerformanceLegendFullScanTooltip));
    public static string SessionManagerFilterAll => Loc.Text(nameof(SessionManagerFilterAll));
    public static string SessionManagerTxGapTooltip => Loc.Text(nameof(SessionManagerTxGapTooltip));
    public static string AppSubtitle => Loc.Text(nameof(AppSubtitle));

    // Shared MessageBanner (UX Polish Sprint / Seam 4) — the IDE's one message surface, so its
    // affordances are named once and read identically on every host (debugger, object editors,
    // Execute Procedure, Security Manager, …).
    public static string MessageBannerCopyTooltip => Loc.Text(nameof(MessageBannerCopyTooltip));
    public static string MessageBannerExpandTooltip => Loc.Text(nameof(MessageBannerExpandTooltip));
    public static string MessageBannerCollapseTooltip => Loc.Text(nameof(MessageBannerCollapseTooltip));
    public static string MessageBannerDismissTooltip => Loc.Text(nameof(MessageBannerDismissTooltip));

    public static string SidebarMetadataHeader => Loc.Text(nameof(SidebarMetadataHeader));
    public static string SidebarConnectionsHeader => Loc.Text(nameof(SidebarConnectionsHeader));
    // ── Pusty pasek boczny (M5 / M‑3 klasa A) ────────────────────────────────────────────────────
    // Pierwsze uruchomienie: zero profili ⇒ pod polem filtra nie ma NIC. Wariant W4, ratyfikowany na
    // renderze: najpierw KROK, potem miejsce akcji — i miejsce pokazane GLIFEM, nie tylko słowem, bo
    // przycisk „New Connection" jest w pasku tytułu wyłącznie ikoną `Icon.Plus`.
    //
    // ⚠⚠ POPRZEDNIA TREŚĆ TEJ PARY STAŁYCH BYŁA WADLIWA I NIE WOLNO JEJ PRZYWRACAĆ. `ConnectionsEmptyHint`
    //    brzmiało „Click “+ New Connection” to add one." i CYTOWAŁO ETYKIETĘ, KTÓREJ W PRODUKCIE NIE MA —
    //    przycisk nie ma podpisu, a jego tooltip brzmi „New Connection" (bez plusa w treści). Użytkownik
    //    dostawał polecenie znalezienia napisu, który nigdzie nie występuje: kształt gotchy #311, gdzie
    //    kłamiąca etykieta jest nieodróżnialna od awarii. Obie stałe były przy tym OSIEROCONE — nigdy nie
    //    wpięte — więc defekt nigdy się nie ujawnił, tylko czekał na kogoś, kto „wpnie gotowy tekst".
    // ⛔ Zmieniając te napisy, sprawdź w `MainWindow.axaml`, jak akcja NAPRAWDĘ nazywa się na ekranie.
    public static string SidebarPlaceholderEmpty => Loc.Text(nameof(SidebarPlaceholderEmpty));
    public static string SidebarTabMetadata => Loc.Text(nameof(SidebarTabMetadata));
    public static string SidebarTabConnections => Loc.Text(nameof(SidebarTabConnections));

    public static string MetadataGroupTables => Loc.Text(nameof(MetadataGroupTables));
    public static string MetadataGroupViews => Loc.Text(nameof(MetadataGroupViews));
    public static string MetadataGroupProcedures => Loc.Text(nameof(MetadataGroupProcedures));
    public static string MetadataGroupTriggers => Loc.Text(nameof(MetadataGroupTriggers));
    public static string MetadataGroupFunctions => Loc.Text(nameof(MetadataGroupFunctions));
    public static string MetadataGroupGenerators => Loc.Text(nameof(MetadataGroupGenerators));
    public static string MetadataGroupDomains => Loc.Text(nameof(MetadataGroupDomains));
    public static string MetadataGroupPackages => Loc.Text(nameof(MetadataGroupPackages));
    public static string MetadataGroupExceptions => Loc.Text(nameof(MetadataGroupExceptions));
    public static string MetadataGroupRoles => Loc.Text(nameof(MetadataGroupRoles));
    public static string MetadataGroupUsers => Loc.Text(nameof(MetadataGroupUsers));
    public static string MetadataGroupIndexes => Loc.Text(nameof(MetadataGroupIndexes));
    public static string MetadataGroupSystemTables => Loc.Text(nameof(MetadataGroupSystemTables));
    public static string MetadataNotConnectedHint => Loc.Text(nameof(MetadataNotConnectedHint));
    public static string MetadataFilterPlaceholder => Loc.Text(nameof(MetadataFilterPlaceholder));
    public static string MetadataRefreshTooltip => Loc.Text(nameof(MetadataRefreshTooltip));
    public static string MetadataContextOpenDdl => Loc.Text(nameof(MetadataContextOpenDdl));
    public static string MetadataContextCopyName => Loc.Text(nameof(MetadataContextCopyName));
    // Table context menu (metadata tree, Session 5 UX sprint)
    public static string MetadataContextNewTable => Loc.Text(nameof(MetadataContextNewTable));
    public static string MetadataContextOpenTable => Loc.Text(nameof(MetadataContextOpenTable));
    public static string MetadataContextDesignTable => Loc.Text(nameof(MetadataContextDesignTable));
    public static string MetadataContextDeleteTable => Loc.Text(nameof(MetadataContextDeleteTable));
    public static string MetadataDeleteTableConfirmTitle => Loc.Text(nameof(MetadataDeleteTableConfirmTitle));
    public static string MetadataDeleteTableConfirmFormat => Loc.Text(nameof(MetadataDeleteTableConfirmFormat));
    public static string MetadataDeleteTableConfirmYes => Loc.Text(nameof(MetadataDeleteTableConfirmYes));
    public static string MetadataDeleteTableExecutedFormat => Loc.Text(nameof(MetadataDeleteTableExecutedFormat));
    public static string MetadataDeleteTableFailedFormat => Loc.Text(nameof(MetadataDeleteTableFailedFormat));
    public static string MetadataNameCopiedFormat => Loc.Text(nameof(MetadataNameCopiedFormat));
    public static string MetadataLoadingPlaceholder => Loc.Text(nameof(MetadataLoadingPlaceholder));
    // Generic metadata-tree context menu (Metadata Tree & Context Menu sprint).
    public static string MetadataContextNewFormat => Loc.Text(nameof(MetadataContextNewFormat));
    public static string MetadataContextEdit => Loc.Text(nameof(MetadataContextEdit));
    public static string MetadataContextOpen => Loc.Text(nameof(MetadataContextOpen));
    public static string MetadataContextDelete => Loc.Text(nameof(MetadataContextDelete));
    public static string MetadataContextExecuteProcedure => Loc.Text(nameof(MetadataContextExecuteProcedure));
    public static string MetadataContextActivate => Loc.Text(nameof(MetadataContextActivate));
    public static string MetadataContextDeactivate => Loc.Text(nameof(MetadataContextDeactivate));
    // Trigger-group Activate/Deactivate submenus are scoped by these — Visible (current filter set)
    // or All. ("Selected" moved onto the selected trigger leaves — see the *SelectedFormat below.)
    public static string MetadataContextScopeVisible => Loc.Text(nameof(MetadataContextScopeVisible));
    public static string MetadataContextScopeAll => Loc.Text(nameof(MetadataContextScopeAll));
    // Shown directly on a selected trigger leaf's context menu when >1 trigger is multi-selected, so
    // the bulk op is reachable without scrolling back to the Triggers group header. {0} = count.
    public static string MetadataContextActivateSelectedFormat => Loc.Text(nameof(MetadataContextActivateSelectedFormat));
    public static string MetadataContextDeactivateSelectedFormat => Loc.Text(nameof(MetadataContextDeactivateSelectedFormat));
    public static string MetadataContextRecompileAllFormat => Loc.Text(nameof(MetadataContextRecompileAllFormat));
    public static string MetadataInactiveSuffix => Loc.Text(nameof(MetadataInactiveSuffix));
    // Generic delete (all deletable kinds) — {0}=kind noun, {1}=object name.
    public static string MetadataDeleteObjectConfirmTitle => Loc.Text(nameof(MetadataDeleteObjectConfirmTitle));
    public static string MetadataDeleteObjectConfirmFormat => Loc.Text(nameof(MetadataDeleteObjectConfirmFormat));
    public static string MetadataDeleteObjectConfirmYes => Loc.Text(nameof(MetadataDeleteObjectConfirmYes));
    public static string MetadataDeleteObjectExecutedFormat => Loc.Text(nameof(MetadataDeleteObjectExecutedFormat));
    public static string MetadataDeleteObjectFailedFormat => Loc.Text(nameof(MetadataDeleteObjectFailedFormat));
    // Connection (database) node — database-wide operations.
    public static string ConnectionContextRefresh => Loc.Text(nameof(ConnectionContextRefresh));
    public static string ConnectionContextRecomputeStats => Loc.Text(nameof(ConnectionContextRecomputeStats));
    public static string ConnectionContextRecompile => Loc.Text(nameof(ConnectionContextRecompile));
    // Bulk-operation execution + report.
    public static string BatchNothingToDo => Loc.Text(nameof(BatchNothingToDo));
    public static string BatchOpActivate => Loc.Text(nameof(BatchOpActivate));
    public static string BatchOpDeactivate => Loc.Text(nameof(BatchOpDeactivate));
    public static string BatchOpRecompile => Loc.Text(nameof(BatchOpRecompile));
    public static string BatchOpRecompileHeader => Loc.Text(nameof(BatchOpRecompileHeader));
    public static string BatchOpRecompileBody => Loc.Text(nameof(BatchOpRecompileBody));
    public static string BatchOpRecomputeStatistics => Loc.Text(nameof(BatchOpRecomputeStatistics));
    public static string BatchOpSave => Loc.Text(nameof(BatchOpSave));
    public static string BatchTitleActivateTriggers => Loc.Text(nameof(BatchTitleActivateTriggers));
    public static string BatchTitleDeactivateTriggers => Loc.Text(nameof(BatchTitleDeactivateTriggers));
    // "Selected" scope confirmation — {0} = number of selected triggers.
    public static string BatchConfirmActivateSelectedTitle => Loc.Text(nameof(BatchConfirmActivateSelectedTitle));
    public static string BatchConfirmActivateSelectedFormat => Loc.Text(nameof(BatchConfirmActivateSelectedFormat));
    public static string BatchConfirmDeactivateSelectedTitle => Loc.Text(nameof(BatchConfirmDeactivateSelectedTitle));
    public static string BatchConfirmDeactivateSelectedFormat => Loc.Text(nameof(BatchConfirmDeactivateSelectedFormat));
    public static string BatchTitleRecompileFormat => Loc.Text(nameof(BatchTitleRecompileFormat));
    public static string BatchTitleRecompileAll => Loc.Text(nameof(BatchTitleRecompileAll));
    public static string BatchTitleRecomputeStatistics => Loc.Text(nameof(BatchTitleRecomputeStatistics));
    // Save-and-close / Save-and-disconnect: compile every dirty object editor (shared batch dialog).
    public static string SaveDirtyEditorsBatchTitle => Loc.Text(nameof(SaveDirtyEditorsBatchTitle));
    public static string SaveDirtyEditorsUnknownError => Loc.Text(nameof(SaveDirtyEditorsUnknownError));
    public static string BatchResultsColumnObject => Loc.Text(nameof(BatchResultsColumnObject));
    public static string BatchResultsColumnOperation => Loc.Text(nameof(BatchResultsColumnOperation));
    public static string BatchResultsColumnResult => Loc.Text(nameof(BatchResultsColumnResult));
    public static string BatchResultsColumnError => Loc.Text(nameof(BatchResultsColumnError));
    public static string BatchResultOk => Loc.Text(nameof(BatchResultOk));
    public static string BatchResultFailed => Loc.Text(nameof(BatchResultFailed));
    // Live footer: Processed / Total, Success, Failed, Duration (hh:mm:ss).
    public static string BatchResultsLiveSummaryFormat => Loc.Text(nameof(BatchResultsLiveSummaryFormat));
    public static string BatchResultsFilterLabel => Loc.Text(nameof(BatchResultsFilterLabel));
    public static string BatchResultsFilterAll => Loc.Text(nameof(BatchResultsFilterAll));
    public static string BatchResultsFilterSuccess => Loc.Text(nameof(BatchResultsFilterSuccess));
    public static string BatchResultsFilterFailed => Loc.Text(nameof(BatchResultsFilterFailed));
    public static string BatchResultsCopyAll => Loc.Text(nameof(BatchResultsCopyAll));
    public static string BatchResultsCopyFailed => Loc.Text(nameof(BatchResultsCopyFailed));
    public static string BatchResultsCancel => Loc.Text(nameof(BatchResultsCancel));

    // ─── Script Executor ──────────────────────────────────────────────────────
    public static string ScriptExecutorTabTitle => Loc.Text(nameof(ScriptExecutorTabTitle));
    public static string ToolbarScriptExecutorTooltip => Loc.Text(nameof(ToolbarScriptExecutorTooltip));

    // ---- Data Import (etap I5: the tab, the frame, the readiness strip, Source & format) ----
    // Core returns codes only (rule #6); every sentence the module shows lives here.

    public static string DataImportTabTitle => Loc.Text(nameof(DataImportTabTitle));
    public static string ToolbarDataImportTooltip => Loc.Text(nameof(ToolbarDataImportTooltip));

    // Section titles — also the readiness strip's chip labels.
    public static string ImportSectionSource => Loc.Text(nameof(ImportSectionSource));
    public static string ImportSectionFormat => Loc.Text(nameof(ImportSectionFormat));
    public static string ImportSectionTarget => Loc.Text(nameof(ImportSectionTarget));
    public static string ImportSectionMapping => Loc.Text(nameof(ImportSectionMapping));
    public static string ImportSectionTransaction => Loc.Text(nameof(ImportSectionTransaction));

    // Source & format section.
    //
    // ⭐ The section splits by HOW OFTEN a decision changes, not by what it is about (U1/U5, ratified
    // 2026-07-26). The file (or clipboard) changes on every single run; the separator, encoding and date
    // format are set once and then survive for months. So the picker stays live at all times and only these
    // options fold away — otherwise the commonest action in the module (point at the next file) would cost
    // an expand and a collapse, and §1.2's promise that a repeat import is one F5 would be false.
    public static string ImportSourceHeader => Loc.Text(nameof(ImportSourceHeader));
    // Deliberately not "Format": that word also reads as "which format is this file", i.e. the source kind,
    // which is decided by the picker beside it. And not "Import parameters" either — the transaction mode and
    // the error policy are import parameters too, and they live in the command bar.
    public static string ImportFormatOptionsHeader => Loc.Text(nameof(ImportFormatOptionsHeader));
    public static string ImportFormatOptionsTooltip => Loc.Text(nameof(ImportFormatOptionsTooltip));
    public static string ImportSourceFile => Loc.Text(nameof(ImportSourceFile));
    public static string ImportSourceClipboard => Loc.Text(nameof(ImportSourceClipboard));
    public static string ImportSourceNoFile => Loc.Text(nameof(ImportSourceNoFile));
    public static string ImportSourceBrowseTooltip => Loc.Text(nameof(ImportSourceBrowseTooltip));
    // Choosing the clipboard READS it — that is the whole point of it being a live source, and a control with a
    // side effect should say so rather than let the user discover it.
    public static string ImportSourceUseClipboardTooltip => Loc.Text(nameof(ImportSourceUseClipboardTooltip));
    public static string ImportParsingHeader => Loc.Text(nameof(ImportParsingHeader));
    public static string ImportCultureHeader => Loc.Text(nameof(ImportCultureHeader));
    public static string ImportDelimiterLabel => Loc.Text(nameof(ImportDelimiterLabel));
    public static string ImportQuoteLabel => Loc.Text(nameof(ImportQuoteLabel));
    public static string ImportEncodingLabel => Loc.Text(nameof(ImportEncodingLabel));
    public static string ImportLineEndingLabel => Loc.Text(nameof(ImportLineEndingLabel));
    public static string ImportAutoDetectLabel => Loc.Text(nameof(ImportAutoDetectLabel));
    public static string ImportHasHeaderLabel => Loc.Text(nameof(ImportHasHeaderLabel));
    public static string ImportFirstDataRowLabel => Loc.Text(nameof(ImportFirstDataRowLabel));
    public static string ImportLastRowLabel => Loc.Text(nameof(ImportLastRowLabel));
    // Never "2147483647" — an implementation detail in the UI is what §8 point 7 criticises.
    public static string ImportLastRowPlaceholder => Loc.Text(nameof(ImportLastRowPlaceholder));
    public static string ImportTrimWhitespaceLabel => Loc.Text(nameof(ImportTrimWhitespaceLabel));
    // ── Spreadsheet sources (etap I9). Shown only when the provider declares sheets. ──
    public static string ImportSheetLabel => Loc.Text(nameof(ImportSheetLabel));
    public static string ImportDatesAsDatesLabel => Loc.Text(nameof(ImportDatesAsDatesLabel));

    public static string ImportNullTokenLabel => Loc.Text(nameof(ImportNullTokenLabel));
    public static string ImportNullTokenPlaceholder => Loc.Text(nameof(ImportNullTokenPlaceholder));
    public static string ImportDecimalSeparatorLabel => Loc.Text(nameof(ImportDecimalSeparatorLabel));
    public static string ImportThousandsSeparatorLabel => Loc.Text(nameof(ImportThousandsSeparatorLabel));
    public static string ImportDateOrderLabel => Loc.Text(nameof(ImportDateOrderLabel));
    public static string ImportDateSeparatorLabel => Loc.Text(nameof(ImportDateSeparatorLabel));
    public static string ImportTimeSeparatorLabel => Loc.Text(nameof(ImportTimeSeparatorLabel));
    public static string ImportDelimiterTab => Loc.Text(nameof(ImportDelimiterTab));
    public static string ImportSeparatorNone => Loc.Text(nameof(ImportSeparatorNone));
    public static string ImportSeparatorSpace => Loc.Text(nameof(ImportSeparatorSpace));
    public static string ImportLineEndingAuto => Loc.Text(nameof(ImportLineEndingAuto));
    public static string ImportChangeButton => Loc.Text(nameof(ImportChangeButton));

    // Detection evidence — an automatic decision that explains itself builds trust; a silent one does not.
    public static string ImportDelimiterEvidenceFormat => Loc.Text(nameof(ImportDelimiterEvidenceFormat));
    public static string ImportEncodingEvidenceBom => Loc.Text(nameof(ImportEncodingEvidenceBom));
    public static string ImportEncodingEvidenceAscii => Loc.Text(nameof(ImportEncodingEvidenceAscii));
    public static string ImportEncodingEvidenceHeuristic => Loc.Text(nameof(ImportEncodingEvidenceHeuristic));

    public static string ImportSummaryDelimiterFormat => Loc.Text(nameof(ImportSummaryDelimiterFormat));
    public static string ImportSummaryNoHeader => Loc.Text(nameof(ImportSummaryNoHeader));
    public static string ImportFileFactsFormat => Loc.Text(nameof(ImportFileFactsFormat));
    public static string ImportFileMissing => Loc.Text(nameof(ImportFileMissing));
    // The clipboard's facts carry the READ TIME, not a last-write time it does not have: for a live source that
    // is the question ("is what I see still what I copied?"), and it is also what makes a refresh visibly
    // acknowledge itself when the pasted content happens to be identical.
    public static string ImportClipboardFactsFormat => Loc.Text(nameof(ImportClipboardFactsFormat));
    public static string ImportClipboardEmpty => Loc.Text(nameof(ImportClipboardEmpty));
    // (There is deliberately no "format not supported yet" string any more. It refused .xls until etap I10 gave
    // that format a provider, and every source kind the surface can resolve now has one — a message for a state
    // that can no longer occur is worse than none, because the next reader of the code has to work out when it
    // fires before discovering it never does.)

    // Readiness strip.
    public static string ImportReadinessHeader => Loc.Text(nameof(ImportReadinessHeader));
    public static string ImportReadySummary => Loc.Text(nameof(ImportReadySummary));
    public static string ImportReadySummaryWithRowsFormat => Loc.Text(nameof(ImportReadySummaryWithRowsFormat));
    public static string ImportReadyBlocked => Loc.Text(nameof(ImportReadyBlocked));

    public static string ImportReadyNoSource => Loc.Text(nameof(ImportReadyNoSource));
    public static string ImportReadySourceMissingFormat => Loc.Text(nameof(ImportReadySourceMissingFormat));
    public static string ImportReadySourceUnreadable => Loc.Text(nameof(ImportReadySourceUnreadable));
    public static string ImportReadySourceHasNoFields => Loc.Text(nameof(ImportReadySourceHasNoFields));
    public static string ImportReadySourceOptionsMismatch => Loc.Text(nameof(ImportReadySourceOptionsMismatch));
    public static string ImportReadyNoTarget => Loc.Text(nameof(ImportReadyNoTarget));
    public static string ImportReadyTargetNotFoundFormat => Loc.Text(nameof(ImportReadyTargetNotFoundFormat));
    public static string ImportReadyNewTableHasNoColumns => Loc.Text(nameof(ImportReadyNewTableHasNoColumns));
    public static string ImportReadyNewTableWillBeCommittedFormat => Loc.Text(nameof(ImportReadyNewTableWillBeCommittedFormat));
    // Refused here rather than by the engine: the CREATE is the first thing the run does, so without this the
    // user would meet a raw server error immediately after being told everything was ready (§0).
    public static string ImportReadyNewTableAlreadyExistsFormat => Loc.Text(nameof(ImportReadyNewTableAlreadyExistsFormat));
    public static string ImportReadyBeforeInsertTriggersFormat => Loc.Text(nameof(ImportReadyBeforeInsertTriggersFormat));
    public static string ImportReadyTargetWillBeEmptiedFormat => Loc.Text(nameof(ImportReadyTargetWillBeEmptiedFormat));
    public static string ImportReadyNothingMapped => Loc.Text(nameof(ImportReadyNothingMapped));
    public static string ImportReadyRequiredColumnNotMappedFormat => Loc.Text(nameof(ImportReadyRequiredColumnNotMappedFormat));
    public static string ImportReadyUnsupportedColumnTypeFormat => Loc.Text(nameof(ImportReadyUnsupportedColumnTypeFormat));
    public static string ImportReadyColumnsNotMappedFormat => Loc.Text(nameof(ImportReadyColumnsNotMappedFormat));
    public static string ImportReadyFieldsUnusedFormat => Loc.Text(nameof(ImportReadyFieldsUnusedFormat));
    public static string ImportReadyAmbiguousNameFormat => Loc.Text(nameof(ImportReadyAmbiguousNameFormat));
    public static string ImportReadyMappingDroppedFormat => Loc.Text(nameof(ImportReadyMappingDroppedFormat));
    public static string ImportReadyColumnNotWritableFormat => Loc.Text(nameof(ImportReadyColumnNotWritableFormat));
    public static string ImportReadyIdentityOverrideFormat => Loc.Text(nameof(ImportReadyIdentityOverrideFormat));
    public static string ImportReadyPairingAssumedFormat => Loc.Text(nameof(ImportReadyPairingAssumedFormat));
    public static string ImportReadyNotConnected => Loc.Text(nameof(ImportReadyNotConnected));
    public static string ImportReadyUserTransactionOpen => Loc.Text(nameof(ImportReadyUserTransactionOpen));
    public static string ImportReadyBatchedNotAtomicFormat => Loc.Text(nameof(ImportReadyBatchedNotAtomicFormat));
    public static string ImportReadyTrimmingEnabled => Loc.Text(nameof(ImportReadyTrimmingEnabled));
    public static string ImportReadyLongTransactionFormat => Loc.Text(nameof(ImportReadyLongTransactionFormat));
    public static string ImportReadyNotRepresentableFormat => Loc.Text(nameof(ImportReadyNotRepresentableFormat));

    // The readiness strip's ceiling (U6). The chips carry §3.2's "every gap at once"; this only caps how
    // many findings are spelled out, so the strip cannot take the whole surface exactly when there is most
    // to fix.
    public static string ImportReadyMoreItemsFormat => Loc.Text(nameof(ImportReadyMoreItemsFormat));
    public static string ImportReadyShowFewer => Loc.Text(nameof(ImportReadyShowFewer));
    public static string ImportReadyExpandTooltip => Loc.Text(nameof(ImportReadyExpandTooltip));
    /// <summary>A chip is a status light AND a way in — so it says which, rather than leaving the user to
    /// guess whether it is a filter, a tab, a shortcut or an indicator (§3.2).</summary>
    public static string ImportReadyChipHintFormat => Loc.Text(nameof(ImportReadyChipHintFormat));
    public static string ImportReadyChipFormatHint => Loc.Text(nameof(ImportReadyChipFormatHint));

    // Band H, left half — where the rows land. The lane is a constant because it is one: rows always go to
    // the Data lane as the one user working transaction (§4.5).
    public static string ImportDestinationFormat => Loc.Text(nameof(ImportDestinationFormat));
    /// <summary>Band H once the command bar exists: where the rows land, on which lane, and what then happens
    /// to the transaction (§3.1).</summary>
    public static string ImportDestinationWithModeFormat => Loc.Text(nameof(ImportDestinationWithModeFormat));
    public static string ImportDestinationDataLane => Loc.Text(nameof(ImportDestinationDataLane));
    public static string ImportDestinationNotConnected => Loc.Text(nameof(ImportDestinationNotConnected));

    // The work area's empty state. Every area on this surface names the NEXT STEP rather than reporting an
    // absence (§9.4) — "no data" tells the user something they can already see.
    public static string ImportWorkAreaEmpty => Loc.Text(nameof(ImportWorkAreaEmpty));

    // ---- Data Import, etap I6: the Target tile (§3.4) and the Mapping panel (§3.5) ----

    public static string ImportTargetExistingTable => Loc.Text(nameof(ImportTargetExistingTable));
    public static string ImportTargetTableWatermark => Loc.Text(nameof(ImportTargetTableWatermark));
    public static string ImportTargetFilterWatermark => Loc.Text(nameof(ImportTargetFilterWatermark));
    public static string ImportTargetColumnsFormat => Loc.Text(nameof(ImportTargetColumnsFormat));
    public static string ImportTargetNoPrimaryKey => Loc.Text(nameof(ImportTargetNoPrimaryKey));
    public static string ImportTargetPrimaryKeyFormat => Loc.Text(nameof(ImportTargetPrimaryKeyFormat));
    // Triggers are NAMED, not counted: a count says something is there, the names say what will rewrite the
    // values on the way in (R6).
    public static string ImportTargetNoBeforeInsertTriggers => Loc.Text(nameof(ImportTargetNoBeforeInsertTriggers));
    public static string ImportTargetBeforeInsertTriggersFormat => Loc.Text(nameof(ImportTargetBeforeInsertTriggersFormat));
    public static string ImportTargetEmptyFirst => Loc.Text(nameof(ImportTargetEmptyFirst));
    public static string ImportTargetEmptyFirstTooltip => Loc.Text(nameof(ImportTargetEmptyFirstTooltip));

    // ---- Data Import, etap I8: a table that does not exist yet (§3.4) ----

    public static string ImportTargetNewTable => Loc.Text(nameof(ImportTargetNewTable));
    public static string ImportTargetNewTableWatermark => Loc.Text(nameof(ImportTargetNewTableWatermark));

    // ⚠ §0.5 / gotcha #213 — the module's most important honest sentence ("the CREATE is committed before the
    // first row, so a rollback cannot take the table with it") is NOT here any more. It lives in exactly one
    // place: Core's IMP0018, rendered by the readiness strip, which additionally names the table. There used to
    // be a second copy as a banner under the type grid, and saying one fact twice on one screen is how a warning
    // stops being read. If it ever needs to be louder, make the strip louder — do not add a second sentence.
    public static string ImportNewTableDropOnFailure => Loc.Text(nameof(ImportNewTableDropOnFailure));
    public static string ImportNewTableDropOnFailureTooltip => Loc.Text(nameof(ImportNewTableDropOnFailureTooltip));
    /// <summary>The DDL bottom tab — shown only in the „new table" variant, because in the other one there is
    /// no statement to generate and a permanently empty tab is a promise nothing keeps.</summary>
    public static string ImportDdlTab => Loc.Text(nameof(ImportDdlTab));

    /// <summary>Said inside the tab, once: it is regenerated from the grid above, so it can be read as current
    /// rather than as something that had to be refreshed.</summary>
    public static string ImportDdlLive => Loc.Text(nameof(ImportDdlLive));

    public static string ImportDdlEmpty => Loc.Text(nameof(ImportDdlEmpty));

    // The type grid.
    public static string ImportNewTableColumnName => Loc.Text(nameof(ImportNewTableColumnName));
    public static string ImportNewTableColumnType => Loc.Text(nameof(ImportNewTableColumnType));
    public static string ImportNewTableColumnSize => Loc.Text(nameof(ImportNewTableColumnSize));
    public static string ImportNewTableColumnScale => Loc.Text(nameof(ImportNewTableColumnScale));
    public static string ImportNewTableColumnNullable => Loc.Text(nameof(ImportNewTableColumnNullable));
    public static string ImportNewTableColumnBasis => Loc.Text(nameof(ImportNewTableColumnBasis));
    public static string ImportNewTableEmpty => Loc.Text(nameof(ImportNewTableEmpty));

    // ⭐ Always visible (§3.4): the types are worth exactly as much as the evidence behind them, and REK-7 makes
    // that evidence the WHOLE source rather than a sample.
    public static string ImportNewTableInferenceFormat => Loc.Text(nameof(ImportNewTableInferenceFormat));
    public static string ImportNewTableInferenceTruncatedFormat => Loc.Text(nameof(ImportNewTableInferenceTruncatedFormat));

    // The "Basis" cell — why this column has this type.
    public static string ImportNewTableBasisNoValues => Loc.Text(nameof(ImportNewTableBasisNoValues));
    public static string ImportNewTableBasisTextFormat => Loc.Text(nameof(ImportNewTableBasisTextFormat));
    public static string ImportNewTableBasisMatchedFormat => Loc.Text(nameof(ImportNewTableBasisMatchedFormat));
    // R19: a mixed column is the norm, not the exception — so it names the value that decided it and the row
    // the user can open their file at (§0.6).
    public static string ImportNewTableBasisMixedFormat => Loc.Text(nameof(ImportNewTableBasisMixedFormat));
    public static string ImportNewTableBasisRestored => Loc.Text(nameof(ImportNewTableBasisRestored));

    public static string ImportNewTableKindInteger => Loc.Text(nameof(ImportNewTableKindInteger));
    public static string ImportNewTableKindDecimal => Loc.Text(nameof(ImportNewTableKindDecimal));
    public static string ImportNewTableKindDate => Loc.Text(nameof(ImportNewTableKindDate));
    public static string ImportNewTableKindTimestamp => Loc.Text(nameof(ImportNewTableKindTimestamp));
    public static string ImportNewTableKindTime => Loc.Text(nameof(ImportNewTableKindTime));
    public static string ImportNewTableKindBoolean => Loc.Text(nameof(ImportNewTableKindBoolean));
    public static string ImportNewTableKindText => Loc.Text(nameof(ImportNewTableKindText));

    // Creating and dropping.
    public static string ImportCreatingTableFormat => Loc.Text(nameof(ImportCreatingTableFormat));
    public static string ImportCreatedTableFormat => Loc.Text(nameof(ImportCreatedTableFormat));
    public static string ImportCreateTableFailedFormat => Loc.Text(nameof(ImportCreateTableFailedFormat));
    /// <summary>⚠ Its own heading. The shared confirmation used to be titled "Empty the table before importing"
    /// for every question the module asked, so this one appeared under the name of a different action.</summary>
    public static string ImportConfirmDropTableTitle => Loc.Text(nameof(ImportConfirmDropTableTitle));
    public static string ImportConfirmDropTableConfirm => Loc.Text(nameof(ImportConfirmDropTableConfirm));
    public static string ImportConfirmDropTableFormat => Loc.Text(nameof(ImportConfirmDropTableFormat));
    public static string ImportDroppedTableFormat => Loc.Text(nameof(ImportDroppedTableFormat));
    public static string ImportDropTableFailedFormat => Loc.Text(nameof(ImportDropTableFailedFormat));
    // §0.5 / §0.6 — the report never leaves the created table unsaid, whether or not it was dropped.
    public static string ImportReportCreatedTableFormat => Loc.Text(nameof(ImportReportCreatedTableFormat));

    // Mapping panel.
    public static string ImportMappingHeadlineFormat => Loc.Text(nameof(ImportMappingHeadlineFormat));
    public static string ImportMappingMatchByPosition => Loc.Text(nameof(ImportMappingMatchByPosition));
    public static string ImportMappingMatchByPositionTooltip => Loc.Text(nameof(ImportMappingMatchByPositionTooltip));
    public static string ImportMappingClear => Loc.Text(nameof(ImportMappingClear));
    public static string ImportMappingOnlyUnmapped => Loc.Text(nameof(ImportMappingOnlyUnmapped));
    public static string ImportMappingDoNotImport => Loc.Text(nameof(ImportMappingDoNotImport));
    public static string ImportMappingFieldLabelFormat => Loc.Text(nameof(ImportMappingFieldLabelFormat));
    public static string ImportMappingUnusedFieldsFormat => Loc.Text(nameof(ImportMappingUnusedFieldsFormat));
    public static string ImportMappingColumnTarget => Loc.Text(nameof(ImportMappingColumnTarget));
    public static string ImportMappingColumnSource => Loc.Text(nameof(ImportMappingColumnSource));
    public static string ImportMappingColumnType => Loc.Text(nameof(ImportMappingColumnType));
    public static string ImportMappingColumnNote => Loc.Text(nameof(ImportMappingColumnNote));
    public static string ImportMappingEmpty => Loc.Text(nameof(ImportMappingEmpty));

    // Why a column's picker is disabled. A blocked control that does not say why is a UX defect (§9.1.3).
    public static string ImportMappingLockedComputed => Loc.Text(nameof(ImportMappingLockedComputed));
    public static string ImportMappingLockedUnsupportedFormat => Loc.Text(nameof(ImportMappingLockedUnsupportedFormat));
    public static string ImportMappingLockedIdentity => Loc.Text(nameof(ImportMappingLockedIdentity));
    public static string ImportMappingUnlockIdentity => Loc.Text(nameof(ImportMappingUnlockIdentity));

    // Mapping origin (§9.3 — the debugger's ValueOrigin vocabulary, reused rather than reinvented).
    public static string ImportMappingOriginMatched => Loc.Text(nameof(ImportMappingOriginMatched));
    public static string ImportMappingOriginAssumed => Loc.Text(nameof(ImportMappingOriginAssumed));

    // Bottom panel + surface status.
    public static string ImportSourcePreviewTab => Loc.Text(nameof(ImportSourcePreviewTab));
    public static string ImportSourcePreviewEmpty => Loc.Text(nameof(ImportSourcePreviewEmpty));
    public static string ImportSourcePreviewRaggedTooltip => Loc.Text(nameof(ImportSourcePreviewRaggedTooltip));
    public static string ImportRowNumberColumn => Loc.Text(nameof(ImportRowNumberColumn));
    /// <summary>Gutter marker for a record whose field count disagrees with the rest of the file (§3.6).</summary>
    public static string ImportRaggedMarker => Loc.Text(nameof(ImportRaggedMarker));
    public static string ImportSurfaceStatusNoSource => Loc.Text(nameof(ImportSurfaceStatusNoSource));
    public static string ImportSurfaceStatusFormat => Loc.Text(nameof(ImportSurfaceStatusFormat));
    public static string ImportSurfaceStatusMore => Loc.Text(nameof(ImportSurfaceStatusMore));
    public static string ImportBottomPanelToggleTooltip => Loc.Text(nameof(ImportBottomPanelToggleTooltip));

    // ---- Data Import, etap I7: the command bar (§3.1 band B), the run, and the report (§3.7) ----

    public static string ImportRun => Loc.Text(nameof(ImportRun));
    public static string ImportRunTooltip => CommandTip.For(
        CommandId.Go, Loc.Text(nameof(ImportRunTooltip)));
    public static string ImportValidate => Loc.Text(nameof(ImportValidate));
    public static string ImportValidateTooltip => CommandTip.For(
        CommandId.ImportValidate,
        Loc.Text(nameof(ImportValidateTooltip)));
    public static string ImportCancel => Loc.Text(nameof(ImportCancel));
    public static string ImportCancelTooltip => Loc.Text(nameof(ImportCancelTooltip));

    // Refresh names what it does to the WORLD, not to the screen: it re-reads every fact the surface holds. The
    // tooltip lists the cases because that is what makes the button discoverable — a bare "Refresh" leaves the
    // user guessing what exactly gets re-read. (Icon only, so there is deliberately no label constant: the
    // shared refresh mark already carries the meaning, and the command bar has no room to spare.)
    // ⚠ Ctrl+V stays literal here, and it is the one deliberate exception: it is not a catalog command (it
    // means "re-read the clipboard SOURCE", i.e. paste semantics that must yield to a focused text box), so
    // there is no descriptor to read it from. Ctrl+R comes from the catalog like every other gesture.
    // ⚠ TWO keys, not one, and the split is forced by where the gesture lands. CommandTip.For appends
    // " · <gesture>" to the label it is given, so folding the trailing note into the label would move the
    // gesture behind it and silently reword the tooltip. The note is therefore its own member.
    // ⚠ Its literal "Ctrl+V" is the standing, recorded exemption in UiStringsShortcutSourceTests — a raw key
    // in prose rather than a catalog gesture — and it stays exempt after moving into the resource file.
    public static string ImportRefreshTooltip => CommandTip.For(
        CommandId.ImportRefresh,
        Loc.Text(nameof(ImportRefreshTooltip)))
        + ImportRefreshTooltipClipboardNote;

    public static string ImportRefreshTooltipClipboardNote =>
        Loc.Text(nameof(ImportRefreshTooltipClipboardNote));
    public static string ImportRunCancelled => Loc.Text(nameof(ImportRunCancelled));

    public static string ImportTransactionLabel => Loc.Text(nameof(ImportTransactionLabel));
    public static string ImportTransactionManual => Loc.Text(nameof(ImportTransactionManual));
    public static string ImportTransactionAutoCommit => Loc.Text(nameof(ImportTransactionAutoCommit));
    public static string ImportTransactionBatched => Loc.Text(nameof(ImportTransactionBatched));
    public static string ImportTransactionManualDescription => Loc.Text(nameof(ImportTransactionManualDescription));
    public static string ImportTransactionAutoCommitDescription => Loc.Text(nameof(ImportTransactionAutoCommitDescription));
    public static string ImportTransactionBatchedDescriptionFormat => Loc.Text(nameof(ImportTransactionBatchedDescriptionFormat));

    public static string ImportErrorPolicyLabel => Loc.Text(nameof(ImportErrorPolicyLabel));
    public static string ImportErrorPolicyStop => Loc.Text(nameof(ImportErrorPolicyStop));
    public static string ImportErrorPolicySkip => Loc.Text(nameof(ImportErrorPolicySkip));

    public static string ImportProgressFormat => Loc.Text(nameof(ImportProgressFormat));

    public static string ImportConfirmEmptyFormat => Loc.Text(nameof(ImportConfirmEmptyFormat));
    public static string ImportConfirmEmptyCountFormat => Loc.Text(nameof(ImportConfirmEmptyCountFormat));

    // The converted preview (§3.6).
    public static string ImportMappingTitle => Loc.Text(nameof(ImportMappingTitle));

    /// <summary>Title of the work area's left half in the „new table" variant — the columns about to be
    /// created. It names a CONFIGURATION subject, which is why it lives beside Mapping rather than beside the
    /// preview: the work area is where the import is designed, the bottom panel is where results land.</summary>
    public static string ImportNewTableTypesTitle => Loc.Text(nameof(ImportNewTableTypesTitle));

    public static string ImportPreviewTitle => Loc.Text(nameof(ImportPreviewTitle));
    public static string ImportPreviewHeadlineFormat => Loc.Text(nameof(ImportPreviewHeadlineFormat));
    public static string ImportPreviewHeadlineProblemsFormat => Loc.Text(nameof(ImportPreviewHeadlineProblemsFormat));
    public static string ImportPreviewEmpty => Loc.Text(nameof(ImportPreviewEmpty));
    public static string ImportPreviewFailedTooltip => Loc.Text(nameof(ImportPreviewFailedTooltip));

    // The Errors / Report bottom tabs (§3.1 band G).
    public static string ImportErrorsTab => Loc.Text(nameof(ImportErrorsTab));
    public static string ImportErrorsTabCountFormat => Loc.Text(nameof(ImportErrorsTabCountFormat));
    public static string ImportErrorsEmpty => Loc.Text(nameof(ImportErrorsEmpty));
    public static string ImportReportTab => Loc.Text(nameof(ImportReportTab));
    public static string ImportReportEmpty => Loc.Text(nameof(ImportReportEmpty));
    public static string ImportReportExport => Loc.Text(nameof(ImportReportExport));
    public static string ImportReportCopy => Loc.Text(nameof(ImportReportCopy));
    public static string ImportReportColumnRow => Loc.Text(nameof(ImportReportColumnRow));
    public static string ImportReportColumnColumn => Loc.Text(nameof(ImportReportColumnColumn));
    public static string ImportReportColumnValue => Loc.Text(nameof(ImportReportColumnValue));
    public static string ImportReportColumnReason => Loc.Text(nameof(ImportReportColumnReason));
    public static string ImportReportRevealTooltip => Loc.Text(nameof(ImportReportRevealTooltip));

    public static string ImportReportImportedFormat => Loc.Text(nameof(ImportReportImportedFormat));
    public static string ImportReportCancelledFormat => Loc.Text(nameof(ImportReportCancelledFormat));
    public static string ImportReportValidatedFormat => Loc.Text(nameof(ImportReportValidatedFormat));
    public static string ImportReportValidatedCancelledFormat => Loc.Text(nameof(ImportReportValidatedCancelledFormat));
    /// <summary>§0.6: an open transaction is never described as a finished import.</summary>
    public static string ImportReportTransactionOpen => Loc.Text(nameof(ImportReportTransactionOpen));
    public static string ImportReportRowsCommittedFormat => Loc.Text(nameof(ImportReportRowsCommittedFormat));
    public static string ImportReportShortenedFormat => Loc.Text(nameof(ImportReportShortenedFormat));
    public static string ImportReportListTruncatedFormat => Loc.Text(nameof(ImportReportListTruncatedFormat));
    public static string ImportCommit => Loc.Text(nameof(ImportCommit));
    public static string ImportCommitTooltip => Loc.Text(nameof(ImportCommitTooltip));
    public static string ImportRollback => Loc.Text(nameof(ImportRollback));
    public static string ImportRollbackTooltip => Loc.Text(nameof(ImportRollbackTooltip));
    /// <summary>Toolbar marker: the import left a transaction open and the decision is pending. Amber, not
    /// red — a pending decision is not a failure, and after a clean import the red readiness line was being
    /// read as "the import did not work".</summary>
    public static string UnsavedImportRowsFormat => Loc.Text(nameof(UnsavedImportRowsFormat));
    public static string ImportTransactionOpenMarker => Loc.Text(nameof(ImportTransactionOpenMarker));
    public static string ImportTransactionOpenMarkerTooltip => Loc.Text(nameof(ImportTransactionOpenMarkerTooltip));
    public static string ImportCommitted => Loc.Text(nameof(ImportCommitted));
    public static string ImportRolledBack => Loc.Text(nameof(ImportRolledBack));
    public static string ImportRestoredLastConfiguration => Loc.Text(nameof(ImportRestoredLastConfiguration));
    public static string ImportForgetLastConfiguration => Loc.Text(nameof(ImportForgetLastConfiguration));

    // ---- Data Import: named profiles (etap I11) ----

    public static string ImportProfileLabel => Loc.Text(nameof(ImportProfileLabel));
    /// <summary>The standing first row. ⚠ Named for what it IS — no profile attached — and deliberately NOT
    /// „default configuration", which would promise defaults it does not restore. Restoring them is Reset.</summary>
    public static string ImportProfileNone => Loc.Text(nameof(ImportProfileNone));
    public static string ImportProfileDetached => Loc.Text(nameof(ImportProfileDetached));
    /// <summary>Says which profiles the selector holds. A restriction the user cannot see is indistinguishable
    /// from a profile that has gone missing (§4.8.3).</summary>
    public static string ImportProfileScopeFormat => Loc.Text(nameof(ImportProfileScopeFormat));
    /// <summary>Appended in the list to a profile that is not tied to a connection and is therefore offered
    /// everywhere.</summary>
    public static string ImportProfilePortableSuffix => Loc.Text(nameof(ImportProfilePortableSuffix));
    /// <summary>Appended to a profile written by a newer build. It stays in the list on purpose — hiding it
    /// would look exactly like a deletion.</summary>
    public static string ImportProfileUnreadableSuffix => Loc.Text(nameof(ImportProfileUnreadableSuffix));
    public static string ImportProfileUnreadableFormat => Loc.Text(nameof(ImportProfileUnreadableFormat));
    public static string ImportProfileLoadedFormat => Loc.Text(nameof(ImportProfileLoadedFormat));

    public static string ImportProfileSaveAs => Loc.Text(nameof(ImportProfileSaveAs));
    public static string ImportProfileSaveAsTooltip => Loc.Text(nameof(ImportProfileSaveAsTooltip));
    public static string ImportProfileSaveAsTitle => Loc.Text(nameof(ImportProfileSaveAsTitle));
    public static string ImportProfileNameLabel => Loc.Text(nameof(ImportProfileNameLabel));
    public static string ImportProfileSaveConfirm => Loc.Text(nameof(ImportProfileSaveConfirm));
    public static string ImportProfileSavedFormat => Loc.Text(nameof(ImportProfileSavedFormat));
    public static string ImportProfileOverwriteTitle => Loc.Text(nameof(ImportProfileOverwriteTitle));
    public static string ImportProfileOverwriteFormat => Loc.Text(nameof(ImportProfileOverwriteFormat));
    public static string ImportProfileOverwriteConfirm => Loc.Text(nameof(ImportProfileOverwriteConfirm));

    public static string ImportProfileRenameTooltip => Loc.Text(nameof(ImportProfileRenameTooltip));
    public static string ImportProfileRenameTitle => Loc.Text(nameof(ImportProfileRenameTitle));
    public static string ImportProfileRenameConfirm => Loc.Text(nameof(ImportProfileRenameConfirm));
    public static string ImportProfileRenamedFormat => Loc.Text(nameof(ImportProfileRenamedFormat));
    public static string ImportProfileNameTakenFormat => Loc.Text(nameof(ImportProfileNameTakenFormat));

    public static string ImportProfileDeleteTooltip => Loc.Text(nameof(ImportProfileDeleteTooltip));
    public static string ImportProfileDeleteTitle => Loc.Text(nameof(ImportProfileDeleteTitle));
    public static string ImportProfileDeleteFormat => Loc.Text(nameof(ImportProfileDeleteFormat));
    public static string ImportProfileDeleteConfirm => Loc.Text(nameof(ImportProfileDeleteConfirm));
    public static string ImportProfileDeletedFormat => Loc.Text(nameof(ImportProfileDeletedFormat));

    /// <summary>Start again: every decision back to its default, and no profile attached. The counterpart to
    /// „(no profile)", which only detaches.</summary>
    public static string ImportReset => Loc.Text(nameof(ImportReset));
    public static string ImportResetTooltip => Loc.Text(nameof(ImportResetTooltip));
    public static string ImportResetTitle => Loc.Text(nameof(ImportResetTitle));
    public static string ImportResetQuestion => Loc.Text(nameof(ImportResetQuestion));
    public static string ImportResetConfirm => Loc.Text(nameof(ImportResetConfirm));
    public static string ImportResetDone => Loc.Text(nameof(ImportResetDone));

    // ---- Data Import: ImportErrorKind → one sentence. The ONE table (rule #6). ----

    public static string ImportErrorNotAnInteger => Loc.Text(nameof(ImportErrorNotAnInteger));
    public static string ImportErrorNotANumber => Loc.Text(nameof(ImportErrorNotANumber));
    public static string ImportErrorNotADateTime => Loc.Text(nameof(ImportErrorNotADateTime));
    public static string ImportErrorNotABoolean => Loc.Text(nameof(ImportErrorNotABoolean));
    public static string ImportErrorValueTooLong => Loc.Text(nameof(ImportErrorValueTooLong));
    public static string ImportErrorValueTooLongMeasuredFormat => Loc.Text(nameof(ImportErrorValueTooLongMeasuredFormat));
    public static string ImportErrorValueOutOfRange => Loc.Text(nameof(ImportErrorValueOutOfRange));
    public static string ImportErrorPrecisionWouldBeLost => Loc.Text(nameof(ImportErrorPrecisionWouldBeLost));
    public static string ImportErrorUnsupportedTargetType => Loc.Text(nameof(ImportErrorUnsupportedTargetType));
    public static string ImportErrorNullNotAllowed => Loc.Text(nameof(ImportErrorNullNotAllowed));
    public static string ImportErrorNotRepresentable => Loc.Text(nameof(ImportErrorNotRepresentable));
    public static string ImportErrorSourceErrorValue => Loc.Text(nameof(ImportErrorSourceErrorValue));
    public static string ImportErrorServerNullViolation => Loc.Text(nameof(ImportErrorServerNullViolation));
    public static string ImportErrorServerUniqueViolation => Loc.Text(nameof(ImportErrorServerUniqueViolation));
    public static string ImportErrorServerCheckViolation => Loc.Text(nameof(ImportErrorServerCheckViolation));
    public static string ImportErrorServerForeignKeyViolation => Loc.Text(nameof(ImportErrorServerForeignKeyViolation));
    public static string ImportErrorServerStringTruncation => Loc.Text(nameof(ImportErrorServerStringTruncation));
    public static string ImportErrorServerNumericOverflow => Loc.Text(nameof(ImportErrorServerNumericOverflow));
    public static string ImportErrorServerTransliteration => Loc.Text(nameof(ImportErrorServerTransliteration));
    public static string ImportErrorServerError => Loc.Text(nameof(ImportErrorServerError));

    public static string ScriptRun => Loc.Text(nameof(ScriptRun));
    public static string ScriptRunTooltip => CommandTip.For(
        CommandId.Go, Loc.Text(nameof(ScriptRunTooltip)));
    public static string ScriptStopTooltip => Loc.Text(nameof(ScriptStopTooltip));
    public static string ScriptCommit => Loc.Text(nameof(ScriptCommit));
    public static string ScriptCommitTooltip => Loc.Text(nameof(ScriptCommitTooltip));
    public static string ScriptRollback => Loc.Text(nameof(ScriptRollback));
    public static string ScriptRollbackTooltip => Loc.Text(nameof(ScriptRollbackTooltip));
    public static string ScriptTransactionLabel => Loc.Text(nameof(ScriptTransactionLabel));
    public static string ScriptModeManual => Loc.Text(nameof(ScriptModeManual));
    public static string ScriptModeAutoCommit => Loc.Text(nameof(ScriptModeAutoCommit));
    public static string ScriptModeSequenced => Loc.Text(nameof(ScriptModeSequenced));
    // Per-mode descriptions — surfaced where the user picks the mode (the picker's tooltip), so the
    // Sequenced trade-off is stated at the point of choice (not buried). No transaction jargon.
    public static string ScriptModeManualDescription => Loc.Text(nameof(ScriptModeManualDescription));
    public static string ScriptModeAutoCommitDescription => Loc.Text(nameof(ScriptModeAutoCommitDescription));
    public static string ScriptModeSequencedDescription => Loc.Text(nameof(ScriptModeSequencedDescription));
    public static string ScriptStopOnError => Loc.Text(nameof(ScriptStopOnError));
    public static string ScriptOpenTooltip => Loc.Text(nameof(ScriptOpenTooltip));
    public static string ScriptSaveTooltip => Loc.Text(nameof(ScriptSaveTooltip));
    public static string ScriptStatusOpenedFormat => Loc.Text(nameof(ScriptStatusOpenedFormat));
    public static string ScriptStatusSavedFormat => Loc.Text(nameof(ScriptStatusSavedFormat));
    public static string ScriptStatusFileErrorFormat => Loc.Text(nameof(ScriptStatusFileErrorFormat));

    // ─── Recompile Dependents (Part 2) ────────────────────────────────────────
    public static string RecompileDependentsTitle => Loc.Text(nameof(RecompileDependentsTitle));
    public static string RecompileDependentsHeaderFormat => Loc.Text(nameof(RecompileDependentsHeaderFormat));
    public static string RecompileDependentsHint => Loc.Text(nameof(RecompileDependentsHint));
    public static string RecompileDependentsSelectAll => Loc.Text(nameof(RecompileDependentsSelectAll));
    public static string RecompileDependentsSelectNone => Loc.Text(nameof(RecompileDependentsSelectNone));
    public static string RecompileDependentsDontAskAgain => Loc.Text(nameof(RecompileDependentsDontAskAgain));
    public static string RecompileDependentsRecompile => Loc.Text(nameof(RecompileDependentsRecompile));
    public static string RecompileDependentsSkip => Loc.Text(nameof(RecompileDependentsSkip));
    public static string RecompileDependentsBatchTitleFormat => Loc.Text(nameof(RecompileDependentsBatchTitleFormat));

    // ─── Smart SQL Parameters (Part 3) ────────────────────────────────────────
    // Shown in the parameter dialog's Type column when the type can't be resolved from the
    // catalog — we show "Unknown", never a guessed type (a plain text input is used).
    public static string SmartParamUnknownType => Loc.Text(nameof(SmartParamUnknownType));
    public static string ScriptTransactionOpenMarker => Loc.Text(nameof(ScriptTransactionOpenMarker));
    // Result grid column headers.
    public static string ScriptColumnLine => Loc.Text(nameof(ScriptColumnLine));
    // Sequenced only: which committed step (segment/transaction) the statement ran in. Blank in the
    // single-transaction modes, where the whole script is one transaction.
    public static string ScriptColumnStep => Loc.Text(nameof(ScriptColumnStep));
    public static string ScriptColumnStepTooltip => Loc.Text(nameof(ScriptColumnStepTooltip));
    // Per-step outcome, shown by colouring the Step cell (Sequenced only). A step's outcome is distinct
    // from a statement's own result: a statement can have succeeded yet its step still rolled back.
    public static string ScriptStepCommittedTooltip => Loc.Text(nameof(ScriptStepCommittedTooltip));
    public static string ScriptStepRolledBackTooltip => Loc.Text(nameof(ScriptStepRolledBackTooltip));
    public static string ScriptColumnStatement => Loc.Text(nameof(ScriptColumnStatement));
    public static string ScriptColumnType => Loc.Text(nameof(ScriptColumnType));
    public static string ScriptColumnResult => Loc.Text(nameof(ScriptColumnResult));
    // Sequenced only: a statement a stop-on-error / cancellation left unexecuted. It never ran, so it
    // is neither a success nor a failure — surfaced as a muted "Not run" row so the grid shows exactly
    // what the deployment did NOT reach.
    public static string ScriptResultNotRun => Loc.Text(nameof(ScriptResultNotRun));
    public static string ScriptResultNotRunTooltip => Loc.Text(nameof(ScriptResultNotRunTooltip));
    public static string ScriptColumnRows => Loc.Text(nameof(ScriptColumnRows));
    public static string ScriptColumnDuration => Loc.Text(nameof(ScriptColumnDuration));
    public static string ScriptColumnError => Loc.Text(nameof(ScriptColumnError));
    // Status line.
    public static string ScriptStatusReady => Loc.Text(nameof(ScriptStatusReady));

    // ── Stany puste siatki wyników (M5 / M‑3, B6) ────────────────────────────────────────────────
    // ⭐ DWA, bo model niósł to rozróżnienie na długo przed M‑3: `HasResults` liczy się z `_allRows`
    //    (przed filtrem), a siatka wiąże się z `Rows` (po filtrze). Jeden komunikat mówiłby „uruchom
    //    skrypt" komuś, kto właśnie go uruchomił i tylko przełączył filtr na „Failed".
    // ⚠ Druga treść świadomie powtarza język, którym mówią już Session Manager i Trace Monitor
    //    („No sessions match the current filter." / „No events match the current filter.") — ta sama
    //    sytuacja ma brzmieć tak samo, niezależnie od ekranu.
    public static string ScriptResultsEmpty => Loc.Text(nameof(ScriptResultsEmpty));
    public static string ScriptResultsNoFilterMatch => Loc.Text(nameof(ScriptResultsNoFilterMatch));
    public static string ScriptStatusRunning => Loc.Text(nameof(ScriptStatusRunning));
    public static string ScriptStatusNothingToRun => Loc.Text(nameof(ScriptStatusNothingToRun));
    public static string ScriptStatusCancelled => Loc.Text(nameof(ScriptStatusCancelled));
    public static string ScriptStatusCommitted => Loc.Text(nameof(ScriptStatusCommitted));
    public static string ScriptStatusRolledBack => Loc.Text(nameof(ScriptStatusRolledBack));
    public static string ScriptStatusParseErrorFormat => Loc.Text(nameof(ScriptStatusParseErrorFormat));
    public static string ScriptStatusDisallowedFormat => Loc.Text(nameof(ScriptStatusDisallowedFormat));
    // Run gate: a transaction is already open and must be settled before a script runs.
    public static string ScriptBlockOwnTxOpen => Loc.Text(nameof(ScriptBlockOwnTxOpen));
    public static string ScriptBlockExternalTxOpen => Loc.Text(nameof(ScriptBlockExternalTxOpen));
    // Pre-flight: a mixed DDL+DML script cannot run in a single-transaction mode (Manual / Auto-commit)
    // because Firebird cannot use an object a statement created until it is committed (#213). Stop before
    // the first statement and point the user at Sequenced, which is built for exactly this.
    public static string ScriptStatusMixedNeedsSequenced => Loc.Text(nameof(ScriptStatusMixedNeedsSequenced));
    public static string ScriptStatusManualSummaryFormat => Loc.Text(nameof(ScriptStatusManualSummaryFormat));
    public static string ScriptStatusAutoSummaryFormat => Loc.Text(nameof(ScriptStatusAutoSummaryFormat));
    // Sequenced (deployment) — committed step-by-step, so the summary states the non-atomic reality
    // rather than a single Committed/Rolled-back verdict.
    public static string ScriptStatusSequencedSummaryFormat => Loc.Text(nameof(ScriptStatusSequencedSummaryFormat));
    // Sequenced headline: how many committed steps (transactions) of all the steps the run planned —
    // committed + rolled-back + not-run. Prepended to the deployment / cancelled summary (seam C3).
    public static string ScriptStatusSequencedStepsFormat => Loc.Text(nameof(ScriptStatusSequencedStepsFormat));
    public static string ScriptStatusSequencedCancelled => Loc.Text(nameof(ScriptStatusSequencedCancelled));
    public static string BatchResultsClose => Loc.Text(nameof(BatchResultsClose));
    // Preparation phase — the dialog opens here immediately so feedback is instant while
    // the object list + per-object SQL are still being built (Batch Operations UX sprint).
    public static string BatchPreparing => Loc.Text(nameof(BatchPreparing));
    public static string BatchPreparingBuildList => Loc.Text(nameof(BatchPreparingBuildList));
    // ⭐⭐ ONE WHOLE SENTENCE PER GROUP, never "Loading {0}…" with the noun as an argument.
    // The earlier pair (`BatchPreparingListFormat` / `BatchPreparingLoadFormat`) took the noun as {0},
    // which forced the producer to BUILD an English plural (`KindNoun(kind) + "s"`, plus two literal
    // "triggers"/"indexes"/"dependents"). That is the shape C7 removed with `PerformanceContext.OutputVerb`,
    // for the same reason: an inflecting language cannot slot a nominative noun into an arbitrary sentence
    // ("Ładowanie procedur…", genitive), and no translator can repair it from the catalog because the word
    // does not live in a key. The rule that picks the key is `RecompileGroupTexts`.
    public static string BatchPreparingListFunctions => Loc.Text(nameof(BatchPreparingListFunctions));
    public static string BatchPreparingListIndexes => Loc.Text(nameof(BatchPreparingListIndexes));
    public static string BatchPreparingListPackages => Loc.Text(nameof(BatchPreparingListPackages));
    public static string BatchPreparingListProcedures => Loc.Text(nameof(BatchPreparingListProcedures));
    public static string BatchPreparingListTriggers => Loc.Text(nameof(BatchPreparingListTriggers));
    // {0} = index of the object being fetched, {1} = total. e.g. "Loading procedures 143 / 1965".
    public static string BatchPreparingLoadDependentsFormat => Loc.Text(nameof(BatchPreparingLoadDependentsFormat));
    public static string BatchPreparingLoadFunctionsFormat => Loc.Text(nameof(BatchPreparingLoadFunctionsFormat));
    public static string BatchPreparingLoadPackagesFormat => Loc.Text(nameof(BatchPreparingLoadPackagesFormat));
    public static string BatchPreparingLoadProceduresFormat => Loc.Text(nameof(BatchPreparingLoadProceduresFormat));
    public static string BatchPreparingLoadTriggersFormat => Loc.Text(nameof(BatchPreparingLoadTriggersFormat));
    public static string TabCloseTooltip => Loc.Text(nameof(TabCloseTooltip));

    public static string ConnectionConnect => Loc.Text(nameof(ConnectionConnect));
    public static string ConnectionDisconnect => Loc.Text(nameof(ConnectionDisconnect));
    public static string ConnectionDelete => Loc.Text(nameof(ConnectionDelete));
    public static string ConnectionNew => Loc.Text(nameof(ConnectionNew));
    // Druga linia stanu pustego paska bocznego — stoi obok glifu `Icon.Plus` i nazywa akcję dokładnie tak,
    // jak nazywa ją jej własny tooltip (`ConnectionNewTooltip`). ⚠ Powód i historia poprzedniej treści:
    // przy `SidebarPlaceholderEmpty`.
    public static string ConnectionsEmptyHint => Loc.Text(nameof(ConnectionsEmptyHint));

    public static string WorkspaceTabUntitled => Loc.Text(nameof(WorkspaceTabUntitled));
    public static string WorkspaceEditorPlaceholder => Loc.Text(nameof(WorkspaceEditorPlaceholder));

    public static string TransactionBarInactive => Loc.Text(nameof(TransactionBarInactive));
    public static string TransactionBarActive => Loc.Text(nameof(TransactionBarActive));
    public static string TransactionBarError => Loc.Text(nameof(TransactionBarError));
    public static string TransactionCommit => Loc.Text(nameof(TransactionCommit));
    public static string TransactionRollback => Loc.Text(nameof(TransactionRollback));
    public static string TransactionStatementCountFormat => Loc.Text(nameof(TransactionStatementCountFormat));

    // ⭐ Chip transakcji w pasku statusu (§8.4.5) — GLOBALNA odpowiedź na „czy mam otwartą transakcję
    // i od jak dawna". Pasek nad wynikami edytora SQL niesie osobną, LOKALNĄ informację: liczbę
    // instrukcji. Dwa poziomy informacji, nie redundancja (decyzja użytkownika, 2026-08-02).
    public static string StatusBarTransactionChipFormat => Loc.Text(nameof(StatusBarTransactionChipFormat));
    // Stan przejściowy: transakcja jest otwarta, ale znacznik czasu jeszcze nie powstał (np. otwarta
    // przed podpięciem chipa). Lepszy niż chip pokazujący „0 s", który sugerowałby świeży start.
    public static string StatusBarTransactionChipBare => Loc.Text(nameof(StatusBarTransactionChipBare));

    // ⭐ Sekcja postępu (§8.4.6) — M3.1f. ⚠ Tekst jest ogólny („operation"), bo od M3b ta sekcja
    // obsługuje każdą długo trwającą operację, nie tylko zapytanie SQL. Skrótu klawiaturowego nie
    // podajemy: anulowanie nie ma gestu w `CommandCatalog`, a tooltip obiecujący nieistniejący
    // klawisz uczyłby nieprawdy (reguła z etapu Keyboard Manager, gotcha #284).
    public static string StatusBarCancelOperationTooltip => Loc.Text(nameof(StatusBarCancelOperationTooltip));

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
    public static string StatusProgressQueryRowsFormat => Loc.Text(nameof(StatusProgressQueryRowsFormat));
    public static string StatusProgressScriptFormat => Loc.Text(nameof(StatusProgressScriptFormat));
    public static string StatusProgressImportFormat => Loc.Text(nameof(StatusProgressImportFormat));

    // ⭐ Odczyt źródła przed importem (M3b.1c). ⚠ DWIE etykiety, nie jedna z „file" na sztywno: to samo ogniwo
    // obsługuje schowek, a napis „Loading file…" nad odczytem schowka byłby nieprawdą — a kłamiąca etykieta jest
    // nieodróżnialna od awarii (gotcha #311). Jeden warunek, dwa uczciwe zdania.
    // ⚠ Bez licznika: ten odcinek nie zna ani sumy, ani postępu (czyta próbkę schematu i ograniczony podgląd),
    // więc jakakolwiek liczba tutaj byłaby zmyślona.
    public static string StatusProgressImportReadingFile => Loc.Text(nameof(StatusProgressImportReadingFile));
    public static string StatusProgressImportReadingClipboard => Loc.Text(nameof(StatusProgressImportReadingClipboard));

    // ⭐ Ładowanie połączenia (M3b.2). Dwie etykiety na trzy fazy, i to jest zmierzone, nie oszczędne:
    // faza 2 (odtworzenie zakładek) jest SYNCHRONICZNA na wątku UI, a odmalowanie następuje PRZED nią —
    // napis ustawiony na jej początku pojawiłby się dopiero po jej zakończeniu, czyli gdy jest już
    // nieprawdziwy. Zamiast martwego UI zostaje etykieta fazy 1 (decyzja użytkownika 2026-08-04).
    // ⭐ Faza 3 jest jedyną fazą ze ZNANĄ sumą (13 kategorii), więc jedyną, która uczciwie pokazuje procent.
    public static string StatusProgressConnecting => Loc.Text(nameof(StatusProgressConnecting));
    public static string StatusProgressMetadataFormat => Loc.Text(nameof(StatusProgressMetadataFormat));

    // ⭐ Chipy Trace i Debuggera (§8.4.3 sekcja 3) — M3.1e. Etykieta niesie sam FAKT („gdzieś żyje
    // sesja"), a szczegół idzie do tooltipa, który czyta `StatusText` z VM-a odpowiedniej zakładki.
    // ⚠ Rzeczownik, nie czasownik: chip mówi, CO jest prawdą, a nie co się dzieje — „co się dzieje"
    // to rola railu (§8.4.1). Stąd „Debug"/„Trace", a nie „Debugging"/„Tracing".
    public static string StatusBarDebugChipLabel => Loc.Text(nameof(StatusBarDebugChipLabel));
    public static string StatusBarTraceChipLabel => Loc.Text(nameof(StatusBarTraceChipLabel));

    // Zgrubny czas trwania, czytelny kątem oka (§8.4.5). ⛔ Nie zwiększać precyzji — pasek statusu
    // nie jest stoperem; dokładny czas wykonania niesie ExecutionTimer w toolbarze edytora.
    public static string DurationSecondsFormat => Loc.Text(nameof(DurationSecondsFormat));
    public static string DurationMinutesFormat => Loc.Text(nameof(DurationMinutesFormat));
    public static string DurationHoursFormat => Loc.Text(nameof(DurationHoursFormat));
    public static string TransactionStartedMessage => Loc.Text(nameof(TransactionStartedMessage));
    public static string TransactionCommittedFormat => Loc.Text(nameof(TransactionCommittedFormat));
    public static string TransactionRolledBackFormat => Loc.Text(nameof(TransactionRolledBackFormat));
    // Lane-qualified transaction strings (C2 — Data / Metadata working transactions).
    public static string TransactionLaneData => Loc.Text(nameof(TransactionLaneData));
    public static string TransactionLaneStartedFormat => Loc.Text(nameof(TransactionLaneStartedFormat));
    public static string TransactionLaneCommittedFormat => Loc.Text(nameof(TransactionLaneCommittedFormat));
    public static string TransactionLaneRolledBackFormat => Loc.Text(nameof(TransactionLaneRolledBackFormat));
    public static string TransactionCommitDataTooltip => Loc.Text(nameof(TransactionCommitDataTooltip));
    public static string TransactionRollbackDataTooltip => Loc.Text(nameof(TransactionRollbackDataTooltip));
    // Unified single-pair tooltips — the app commits/rolls back whichever lane(s) are open.
    public static string TransactionCommitTooltip => CommandTip.For(CommandId.Commit, Loc.Text(nameof(TransactionCommitTooltip)));
    public static string TransactionRollbackTooltip => CommandTip.For(CommandId.Rollback, Loc.Text(nameof(TransactionRollbackTooltip)));
    // Execution-lane feedback: which profile the auto-router chose for a statement.
    // {0} = lane (Data/Metadata), {1} = profile label (e.g. "Read Committed").
    // Legacy binary disconnect-confirm strings — superseded by the DisconnectChoice*
    // set below (Commit / Roll back / Cancel). Kept only to avoid churn; not referenced.
    public static string DisconnectConfirmTitle => Loc.Text(nameof(DisconnectConfirmTitle));
    public static string DisconnectConfirmMessage => Loc.Text(nameof(DisconnectConfirmMessage));
    public static string DisconnectConfirmYes => Loc.Text(nameof(DisconnectConfirmYes));
    public static string DisconnectConfirmNo => Loc.Text(nameof(DisconnectConfirmNo));

    // ─── Data-loss WorkGuard ───────────────────────────────────────────────
    // Unsaved-work summary lines (one per affected tab / transaction lane).
    public static string UnsavedNewTableFormat => Loc.Text(nameof(UnsavedNewTableFormat));
    public static string UnsavedNewViewFormat => Loc.Text(nameof(UnsavedNewViewFormat));
    public static string UnsavedNewProcedureFormat => Loc.Text(nameof(UnsavedNewProcedureFormat));
    public static string UnsavedModifiedViewFormat => Loc.Text(nameof(UnsavedModifiedViewFormat));
    public static string UnsavedModifiedProcedureFormat => Loc.Text(nameof(UnsavedModifiedProcedureFormat));
    public static string UnsavedNewTriggerFormat => Loc.Text(nameof(UnsavedNewTriggerFormat));
    public static string UnsavedModifiedTriggerFormat => Loc.Text(nameof(UnsavedModifiedTriggerFormat));
    public static string UnsavedNewFunctionFormat => Loc.Text(nameof(UnsavedNewFunctionFormat));
    public static string UnsavedModifiedFunctionFormat => Loc.Text(nameof(UnsavedModifiedFunctionFormat));
    public static string UnsavedNewGeneratorFormat => Loc.Text(nameof(UnsavedNewGeneratorFormat));
    public static string UnsavedModifiedGeneratorFormat => Loc.Text(nameof(UnsavedModifiedGeneratorFormat));
    public static string UnsavedNewDomainFormat => Loc.Text(nameof(UnsavedNewDomainFormat));
    public static string UnsavedModifiedDomainFormat => Loc.Text(nameof(UnsavedModifiedDomainFormat));
    public static string UnsavedNewPackageFormat => Loc.Text(nameof(UnsavedNewPackageFormat));
    public static string UnsavedModifiedPackageFormat => Loc.Text(nameof(UnsavedModifiedPackageFormat));
    public static string UnsavedNewExceptionFormat => Loc.Text(nameof(UnsavedNewExceptionFormat));
    public static string UnsavedModifiedExceptionFormat => Loc.Text(nameof(UnsavedModifiedExceptionFormat));
    public static string UnsavedModifiedIndexFormat => Loc.Text(nameof(UnsavedModifiedIndexFormat));
    public static string UnsavedPendingStructureFormat => Loc.Text(nameof(UnsavedPendingStructureFormat));
    public static string UnsavedTransactionDataFormat => Loc.Text(nameof(UnsavedTransactionDataFormat));

    // Tab close (binary Discard / Cancel). {0} = the tab's unsaved-work label.
    public static string CloseTabUnsavedConfirmTitle => Loc.Text(nameof(CloseTabUnsavedConfirmTitle));
    public static string CloseTabUnsavedConfirmFormat => Loc.Text(nameof(CloseTabUnsavedConfirmFormat));
    public static string CloseTabUnsavedConfirmYes => Loc.Text(nameof(CloseTabUnsavedConfirmYes));
    // Seam 5c — per-tab close is Save / Discard / Cancel whenever the tab has somewhere to save,
    // matching the disconnect and app-close guards instead of forcing "discard or stay".
    public static string CloseTabUnsavedSave => Loc.Text(nameof(CloseTabUnsavedSave));

    // Disconnect with an active transaction (3-way choice; default Roll back).
    public static string DisconnectChoiceTitle => Loc.Text(nameof(DisconnectChoiceTitle));
    public static string DisconnectChoiceHeaderFormat => Loc.Text(nameof(DisconnectChoiceHeaderFormat));
    public static string DisconnectChoiceQuestion => Loc.Text(nameof(DisconnectChoiceQuestion));
    public static string DisconnectChoiceCommit => Loc.Text(nameof(DisconnectChoiceCommit));
    public static string DisconnectChoiceRollback => Loc.Text(nameof(DisconnectChoiceRollback));
    public static string DisconnectChoiceCancel => Loc.Text(nameof(DisconnectChoiceCancel));
    public static string DisconnectUnsavedDiscardNoteFormat => Loc.Text(nameof(DisconnectUnsavedDiscardNoteFormat));

    // Disconnect with uncompiled tab work but no transaction (binary).
    public static string DisconnectUnsavedTitle => Loc.Text(nameof(DisconnectUnsavedTitle));
    public static string DisconnectUnsavedIntro => Loc.Text(nameof(DisconnectUnsavedIntro));
    public static string DisconnectUnsavedYes => Loc.Text(nameof(DisconnectUnsavedYes));

    // Disconnect with unsaved metadata editors (Phase 1: Save / Discard / Cancel; default Save).
    public static string DisconnectSaveTitle => Loc.Text(nameof(DisconnectSaveTitle));
    public static string DisconnectSaveHeaderFormat => Loc.Text(nameof(DisconnectSaveHeaderFormat));
    public static string DisconnectSaveQuestion => Loc.Text(nameof(DisconnectSaveQuestion));
    public static string DisconnectSaveConfirm => Loc.Text(nameof(DisconnectSaveConfirm));
    public static string DisconnectSaveDiscard => Loc.Text(nameof(DisconnectSaveDiscard));

    // App close with unsaved work / active transactions (default Cancel; "Save and exit"
    // appears when there are unsaved editors to compile).
    public static string ExitUnsavedTitle => Loc.Text(nameof(ExitUnsavedTitle));
    public static string ExitUnsavedIntro => Loc.Text(nameof(ExitUnsavedIntro));
    public static string ExitUnsavedTransactionNote => Loc.Text(nameof(ExitUnsavedTransactionNote));
    public static string ExitUnsavedSave => Loc.Text(nameof(ExitUnsavedSave));
    public static string ExitUnsavedDiscard => Loc.Text(nameof(ExitUnsavedDiscard));
    public static string ExitUnsavedCancel => Loc.Text(nameof(ExitUnsavedCancel));

    public static string BottomTabMessages => Loc.Text(nameof(BottomTabMessages));
    public static string BottomTabResults => Loc.Text(nameof(BottomTabResults));
    public static string BottomTabOutput => Loc.Text(nameof(BottomTabOutput));
    public static string BottomTabDiagnostics => Loc.Text(nameof(BottomTabDiagnostics));

    // Diagnostics panel (Stage 7 / S4) — a view of the DiagnosticsEngine's findings for the SQL editor.
    public static string DiagnosticsEmptyHint => Loc.Text(nameof(DiagnosticsEmptyHint));
    public static string DiagnosticsLocationFormat => Loc.Text(nameof(DiagnosticsLocationFormat));
    public static string DiagnosticSeverityError => Loc.Text(nameof(DiagnosticSeverityError));
    public static string DiagnosticSeverityWarning => Loc.Text(nameof(DiagnosticSeverityWarning));
    public static string DiagnosticSeverityInfo => Loc.Text(nameof(DiagnosticSeverityInfo));

    // ⛔ `StatusBarReady` i `StatusBarConnectedTo` usunięte w M3.1b. Pasek statusu nie opisuje już
    // połączenia zdaniem — sekcja 1 pokazuje NAZWĘ połączenia i endpoint, a stan „połączony / nie"
    // niesie kropka. `StatusBarDisconnected` zostaje: jest etykietą w slocie nazwy, gdy połączenia
    // nie ma (§19.3).
    public static string StatusBarDisconnected => Loc.Text(nameof(StatusBarDisconnected));

    public static string ThemeToggleTooltip => Loc.Text(nameof(ThemeToggleTooltip));
    public static string SidebarToggleTooltip => Loc.Text(nameof(SidebarToggleTooltip));

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
    public static string ConnectionNewTooltip => Loc.Text(nameof(ConnectionNewTooltip));
    public static string ConnectionEditTooltip => Loc.Text(nameof(ConnectionEditTooltip));
    public static string ConnectionCopyTooltip => Loc.Text(nameof(ConnectionCopyTooltip));
    public static string ConnectionDeleteTooltip => Loc.Text(nameof(ConnectionDeleteTooltip));
    public static string ConnectionConnectTooltip => Loc.Text(nameof(ConnectionConnectTooltip));
    public static string ConnectionDisconnectTooltip => Loc.Text(nameof(ConnectionDisconnectTooltip));
    public static string ConnectionReconnectTooltip => Loc.Text(nameof(ConnectionReconnectTooltip));

    // ── Title bar — window caption buttons (M‑1, M3.2d) ─────────────────────────────────────────────
    // EmberTern draws its own caption buttons (the window is `ExtendClientAreaToDecorationsHint`), so
    // these three are ordinary application strings and not the OS's.
    public static string WindowMinimizeTooltip => Loc.Text(nameof(WindowMinimizeTooltip));
    public static string WindowMaxRestoreTooltip => Loc.Text(nameof(WindowMaxRestoreTooltip));
    public static string WindowCloseTooltip => Loc.Text(nameof(WindowCloseTooltip));

    // ── Application Menu (the hamburger) ────────────────────────────────────────────────────────────
    // A rarely-used ADMINISTRATIVE menu for application-level functions — never commands of the active
    // document, which stay on the toolbars, the shortcuts and the context menus. Design + the reasoning
    // for what is deliberately absent: docs/design/hamburger-navigation.md §3–§5.
    public static string AppMenuTooltip => Loc.Text(nameof(AppMenuTooltip));
    // ⭐ Live since Settings Center etap 3. It shipped as a DISABLED row with a "Not available yet" tooltip
    // while the window did not exist — the rule being that a row never ships ahead of what it opens — and the
    // etap that built the window is the etap that enabled the row and removed that tooltip string.
    public static string AppMenuSettings => Loc.Text(nameof(AppMenuSettings));
    public static string AppMenuKeyboardShortcuts => Loc.Text(nameof(AppMenuKeyboardShortcuts));
    public static string AppMenuAbout => Loc.Text(nameof(AppMenuAbout));
    public static string AppMenuExit => Loc.Text(nameof(AppMenuExit));

    // The About window. Deliberately a PRODUCT window, not a diagnostic one: the logo, the name, the version,
    // the author and the copyright — no runtime/OS block, no library names, and no liability or privacy
    // wording (a limitation of liability is a term of the future EmberTern licence and belongs there, in one
    // document). Design: docs/design/hamburger-navigation.md §8.
    // ⛔ There is deliberately NO version string here. The number is read from the assembly by AppInfo, whose
    // single source is <Version> in Directory.Build.props.
    public static string AboutTitle => Loc.Text(nameof(AboutTitle));
    public static string AboutVersionFormat => Loc.Text(nameof(AboutVersionFormat));
    // Released on its own line under the version. The date is assembly metadata fed by <ReleaseDate> in
    // Directory.Build.props — same single source as the version, so it is never a date typed into a view.
    public static string AboutReleasedFormat => Loc.Text(nameof(AboutReleasedFormat));
    // ⚠ The author line is LABELLED on purpose. Unlabelled it read as an unsigned line of text, and the name
    // already appears in the copyright below — the label is what turns a repetition into authorship.
    public static string AboutAuthorFormat => Loc.Text(nameof(AboutAuthorFormat));
    public static string AboutClose => Loc.Text(nameof(AboutClose));
    // A discreet footer button rather than a tab: a tab strip on a five-line window makes it look like a
    // configuration dialog. No licence requires these names on the About face itself — MIT is silent on
    // placement and IDPL 1.0 §3.6 scopes its "conspicuously" to the notices document — so the face stays bare
    // and the obligation is met by the document behind this button.
    public static string AboutThirdPartyNotices => Loc.Text(nameof(AboutThirdPartyNotices));
    public static string ThirdPartyNoticesTitle => Loc.Text(nameof(ThirdPartyNoticesTitle));
    public static string ThirdPartyNoticesUnavailable => Loc.Text(nameof(ThirdPartyNoticesUnavailable));

    // ── Keyboard Shortcuts window ───────────────────────────────────────────────────────────────────
    // A read-only VIEW of CommandCatalog: no name, gesture or scope is written here or in the window.
    public static string KeyboardShortcutsTitle => Loc.Text(nameof(KeyboardShortcutsTitle));
    public static string KeyboardShortcutsSearchPlaceholder => Loc.Text(nameof(KeyboardShortcutsSearchPlaceholder));
    public static string KeyboardShortcutsColumnCommand => Loc.Text(nameof(KeyboardShortcutsColumnCommand));
    public static string KeyboardShortcutsColumnShortcut => Loc.Text(nameof(KeyboardShortcutsColumnShortcut));
    public static string KeyboardShortcutsColumnScope => Loc.Text(nameof(KeyboardShortcutsColumnScope));
    public static string KeyboardShortcutsCountFormat => Loc.Text(nameof(KeyboardShortcutsCountFormat));
    public static string KeyboardShortcutsCountOne => Loc.Text(nameof(KeyboardShortcutsCountOne));
    public static string KeyboardShortcutsEmpty => Loc.Text(nameof(KeyboardShortcutsEmpty));
    // Restores Global → Tab → Tree → Grid → Editor → alphabetically. Shown only while a column sort is
    // overriding it, because an always-visible reset for a state you are not in is noise.
    public static string KeyboardShortcutsResetOrder => Loc.Text(nameof(KeyboardShortcutsResetOrder));
    public static string KeyboardShortcutsResetOrderTooltip => Loc.Text(nameof(KeyboardShortcutsResetOrderTooltip));

    // ── Settings Center ─────────────────────────────────────────────────────────────────────────────
    // The app's one home for user preferences: a category list, a search box, and pages that apply on change
    // with no OK/Cancel. Design: docs/design/settings-center.md §5–§6.
    // ⚠ Every OPTION KEY ("Dark", "en") lives in Core's PreferenceOptions, because it is persisted and
    // validated; only the words are here. The two are bound by a test — a key without a label ships a blank
    // row.
    public static string SettingsCenterTitle => Loc.Text(nameof(SettingsCenterTitle));
    public static string SettingsSearchPlaceholder => Loc.Text(nameof(SettingsSearchPlaceholder));
    public static string SettingsNoMatch => Loc.Text(nameof(SettingsNoMatch));
    public static string SettingsClose => Loc.Text(nameof(SettingsClose));
    // Apply-on-change is the ratified model (Q8), so the window has no OK/Cancel and nothing to confirm. The
    // hint says so once, quietly, rather than leaving the user hunting for a missing OK button.
    public static string SettingsAppliedImmediately => Loc.Text(nameof(SettingsAppliedImmediately));

    public static string SettingsCategoryGeneral => Loc.Text(nameof(SettingsCategoryGeneral));

    public static string SettingsThemeLabel => Loc.Text(nameof(SettingsThemeLabel));
    public static string SettingsThemeDescription => Loc.Text(nameof(SettingsThemeDescription));
    // Extra search terms: the words a user types when they do not know our label.
    public static string SettingsThemeKeywords => Loc.Text(nameof(SettingsThemeKeywords));
    public static string SettingsThemeDark => Loc.Text(nameof(SettingsThemeDark));
    public static string SettingsThemeLight => Loc.Text(nameof(SettingsThemeLight));

    public static string SettingsLanguageLabel => Loc.Text(nameof(SettingsLanguageLabel));
    // ⚠ It says "interface language" and nothing about availability, because the row is REAL: the value is
    // stored, validated and round-tripped from day one, and its list happens to have one entry. Presenting it
    // as unavailable would misrepresent what it does. Adding Polish is a row in Core's language catalog plus
    // the localization milestone (design §8) — no change to this window.
    public static string SettingsLanguageDescription => Loc.Text(nameof(SettingsLanguageDescription));
    public static string SettingsLanguageKeywords => Loc.Text(nameof(SettingsLanguageKeywords));
    public static string SettingsLanguageEnglish => Loc.Text(nameof(SettingsLanguageEnglish));
    // ⚠ A language's own name is written in THAT language in both catalogs ("Polski", never "Polish"):
    // the picker must be readable to someone who cannot read the language currently on screen.
    public static string SettingsLanguagePolish => Loc.Text(nameof(SettingsLanguagePolish));

    // ⚠ The description says "open tabs" and "saved queries stay" because the setting is narrower than its name
    // suggests, and the narrower half is the important one: a connection's saved queries live in the same stored
    // workspace and are the user's own content, so they come back either way.
    public static string SettingsRestoreWorkspaceLabel => Loc.Text(nameof(SettingsRestoreWorkspaceLabel));
    public static string SettingsRestoreWorkspaceDescription => Loc.Text(nameof(SettingsRestoreWorkspaceDescription));
    public static string SettingsRestoreWorkspaceKeywords => Loc.Text(nameof(SettingsRestoreWorkspaceKeywords));

    // ── Editor (etap 6) ─────────────────────────────────────────────────────────────────────────────
    // The default Source/Easy mode for newly opened object editors (§7.6) and the execution row limits
    // (§7.2). §7.2 calls this an "Editor / Execution" page; it is one page.
    public static string SettingsCategoryEditor => Loc.Text(nameof(SettingsCategoryEditor));

    // ⚠ One description for all four rows, and it states the two things a user needs to know: this is a
    // DEFAULT for newly opened editors, and switching a mode inside an editor no longer changes it. That second
    // sentence is the etap's actual fix — the four flags used to be rewritten silently by the last toggle.
    public static string SettingsEditorModeDescription => Loc.Text(nameof(SettingsEditorModeDescription));
    public static string SettingsEditorModeKeywords => Loc.Text(nameof(SettingsEditorModeKeywords));

    // Nagłówek karty, która grupuje te cztery wiersze (pakiet UX po M5, punkt 5). ⚠ Nazywa TEMAT, a nie
    // sumę pozycji — cztery flagi odpowiadają na jedno pytanie, i dopiero to czyni z nich jedną kartę.
    public static string SettingsEasyModeGroupLabel => Loc.Text(nameof(SettingsEasyModeGroupLabel));

    // ── Database Properties (pakiet UX po M5, punkt 6) ────────────────────────────────────────────────
    public static string DatabasePropertiesMenuItem => Loc.Text(nameof(DatabasePropertiesMenuItem));
    public static string DatabasePropertiesTitle => Loc.Text(nameof(DatabasePropertiesTitle));
    public static string DatabasePropertiesGroupIdentity => Loc.Text(nameof(DatabasePropertiesGroupIdentity));
    public static string DatabasePropertiesGroupStorage => Loc.Text(nameof(DatabasePropertiesGroupStorage));
    public static string DatabasePropertiesGroupConfiguration => Loc.Text(nameof(DatabasePropertiesGroupConfiguration));

    public static string DatabasePropertiesDatabase => Loc.Text(nameof(DatabasePropertiesDatabase));
    public static string DatabasePropertiesOwner => Loc.Text(nameof(DatabasePropertiesOwner));
    public static string DatabasePropertiesEngine => Loc.Text(nameof(DatabasePropertiesEngine));
    public static string DatabasePropertiesOds => Loc.Text(nameof(DatabasePropertiesOds));
    public static string DatabasePropertiesDialect => Loc.Text(nameof(DatabasePropertiesDialect));
    public static string DatabasePropertiesCharset => Loc.Text(nameof(DatabasePropertiesCharset));
    public static string DatabasePropertiesCreated => Loc.Text(nameof(DatabasePropertiesCreated));
    public static string DatabasePropertiesPageSize => Loc.Text(nameof(DatabasePropertiesPageSize));
    public static string DatabasePropertiesPages => Loc.Text(nameof(DatabasePropertiesPages));
    public static string DatabasePropertiesSize => Loc.Text(nameof(DatabasePropertiesSize));
    public static string DatabasePropertiesPageBuffers => Loc.Text(nameof(DatabasePropertiesPageBuffers));
    public static string DatabasePropertiesLinger => Loc.Text(nameof(DatabasePropertiesLinger));

    public static string DatabasePropertiesSweepInterval => Loc.Text(nameof(DatabasePropertiesSweepInterval));
    public static string DatabasePropertiesForcedWrites => Loc.Text(nameof(DatabasePropertiesForcedWrites));
    public static string DatabasePropertiesReserveSpace => Loc.Text(nameof(DatabasePropertiesReserveSpace));

    // ⚠ Mówi o CACHE DZIAŁAJĄCEJ INSTANCJI, a nie o wartości zapisanej w nagłówku — bo dokładnie to
    // raportuje MON$PAGE_BUFFERS (zmierzone). Bez tego zdania liczba wyglądałaby na ustawienie bazy.
    public static string DatabasePropertiesPageBuffersNote => Loc.Text(nameof(DatabasePropertiesPageBuffersNote));

    public static string DatabasePropertiesLingerNotSet => Loc.Text(nameof(DatabasePropertiesLingerNotSet));
    public static string DatabasePropertiesLingerSeconds => Loc.Text(nameof(DatabasePropertiesLingerSeconds));

    public static string DatabasePropertiesApply => Loc.Text(nameof(DatabasePropertiesApply));
    public static string DatabasePropertiesClose => Loc.Text(nameof(DatabasePropertiesClose));
    public static string DatabasePropertiesApplied => Loc.Text(nameof(DatabasePropertiesApplied));
    public static string DatabasePropertiesNothingToApply => Loc.Text(nameof(DatabasePropertiesNothingToApply));

    // ⚠ Wymienia to, co SIĘ UDAŁO — Apply nie jest atomowy, więc bez tej listy użytkownik nie wie, które
    // zmiany są już w bazie.
    public static string DatabasePropertiesPartial => Loc.Text(nameof(DatabasePropertiesPartial));

    public static string DatabasePropertiesNoPassword => Loc.Text(nameof(DatabasePropertiesNoPassword));

    // ⭐ Dwa jedyne wyjaśnienia, jakie wolno dodać — bo tylko te dwa przypadki są rozpoznawalne po
    // SQLSTATE/GDS. ⛔ Komunikat serwera jest ZAWSZE pokazywany obok; to jest lead, nie zamiennik.
    public static string DatabasePropertiesMissingPrivilege => Loc.Text(nameof(DatabasePropertiesMissingPrivilege));
    public static string DatabasePropertiesInUse => Loc.Text(nameof(DatabasePropertiesInUse));

    public static string SettingsProcedureEasyModeLabel => Loc.Text(nameof(SettingsProcedureEasyModeLabel));
    public static string SettingsViewEasyModeLabel => Loc.Text(nameof(SettingsViewEasyModeLabel));
    public static string SettingsTriggerEasyModeLabel => Loc.Text(nameof(SettingsTriggerEasyModeLabel));
    public static string SettingsFunctionEasyModeLabel => Loc.Text(nameof(SettingsFunctionEasyModeLabel));

    // ⚠ Both numeric descriptions name their range, because the field CLAMPS silently: a user who types 50000000
    // and gets 1000000 back has to be able to see why, and the alternative — a validation error on a settings
    // page that applies on change — would be a worse answer to the same problem.
    public static string SettingsPreviewRowLimitLabel => Loc.Text(nameof(SettingsPreviewRowLimitLabel));

    // ⚠ The key comes from the catalog, not from this string — CommandId.Go is what F5 actually runs, and a
    // hand-typed "(F5)" here would teach a stale shortcut the day it is re-bound, silently (gotcha #284). That
    // is also why this one member is `static readonly` while its neighbours are `const`: the guard keys on
    // const-ness, because a correctly composed string contains the same text at run time.
    public static string SettingsPreviewRowLimitDescription => CommandTip.Sentence(
        CommandId.Go,
        Loc.Text(nameof(SettingsPreviewRowLimitDescription)));
    public static string SettingsPreviewRowLimitKeywords => Loc.Text(nameof(SettingsPreviewRowLimitKeywords));

    // ⚠ It says the safety ceiling is separate and fixed, so the absence of a control for it reads as a decision
    // rather than an omission — ratified Q9: a configurable memory backstop is not a backstop.
    public static string SettingsFullLoadPromptLabel => Loc.Text(nameof(SettingsFullLoadPromptLabel));
    public static string SettingsFullLoadPromptDescription => Loc.Text(nameof(SettingsFullLoadPromptDescription));
    public static string SettingsFullLoadPromptKeywords => Loc.Text(nameof(SettingsFullLoadPromptKeywords));

    // ── Grid (etap 6) ───────────────────────────────────────────────────────────────────────────────
    public static string SettingsCategoryGrid => Loc.Text(nameof(SettingsCategoryGrid));

    // ⚠ Names the two grids explicitly. This is the page size of the SERVER-PAGED data grids, which is what
    // ratified Q9 admits; the SQL editor's results and the Procedure / Function exec grids page an
    // already-materialized result in memory and are not this setting's subject. A description saying just
    // "grids" would be a promise the code deliberately does not keep.
    public static string SettingsDataPageSizeLabel => Loc.Text(nameof(SettingsDataPageSizeLabel));
    public static string SettingsDataPageSizeDescription => Loc.Text(nameof(SettingsDataPageSizeDescription));
    public static string SettingsDataPageSizeKeywords => Loc.Text(nameof(SettingsDataPageSizeKeywords));

    public static string SettingsGridAutoFitLabel => Loc.Text(nameof(SettingsGridAutoFitLabel));
    public static string SettingsGridAutoFitDescription => Loc.Text(nameof(SettingsGridAutoFitDescription));
    public static string SettingsGridAutoFitKeywords => Loc.Text(nameof(SettingsGridAutoFitKeywords));

    // ── Tabs (M3.3b / product-polish §8.2) ──────────────────────────────────────────────────────────
    // ⭐ Zakładki dostały WŁASNĄ kategorię, a nie wiersze w General — decyzja użytkownika (2026-08-03):
    // pasek zakładek jest osobną powierzchnią aplikacji (§0.1), a General i tak już nosi motyw, język,
    // workspace i eksport. Kategoria jest też celem skoku dla przyszłej pozycji „Ustawienia zakładek…"
    // z menu kontekstowego zakładki (D9 / M3.3c).
    public static string SettingsCategoryTabs => Loc.Text(nameof(SettingsCategoryTabs));

    // ⚠ Opis mówi, co użytkownik ZOBACZY, a nie jak to jest zbudowane — i nazywa różnicę, która naprawdę
    // dzieli te tryby: czy zakładka może zniknąć z widoku. To jest ratyfikowana istota decyzji D5/D7.
    public static string SettingsTabStripModeLabel => Loc.Text(nameof(SettingsTabStripModeLabel));
    public static string SettingsTabStripModeDescription => Loc.Text(nameof(SettingsTabStripModeDescription));
    public static string SettingsTabStripModeKeywords => Loc.Text(nameof(SettingsTabStripModeKeywords));

    public static string SettingsTabStripModeMultiRow => Loc.Text(nameof(SettingsTabStripModeMultiRow));
    public static string SettingsTabStripModeSingleRow => Loc.Text(nameof(SettingsTabStripModeSingleRow));

    // ⚠ Mówi wprost, że dotyczy tylko trybu wielowierszowego — wiersz widoczny w trybie B, w którym nic
    // nie robi, byłby dokładnie tym „martwym zapisem wyglądającym na regułę", przed którym broni §18.R.
    public static string SettingsTabStripMaxRowsLabel => Loc.Text(nameof(SettingsTabStripMaxRowsLabel));
    public static string SettingsTabStripMaxRowsDescription => Loc.Text(nameof(SettingsTabStripMaxRowsDescription));
    public static string SettingsTabStripMaxRowsKeywords => Loc.Text(nameof(SettingsTabStripMaxRowsKeywords));

    // Przycisk przepełnienia w trybie pojedynczego wiersza. ⭐ Licznik pokazuje zakładki NIEWIDOCZNE,
    // nie wszystkie otwarte (§8.2 + decyzja użytkownika) — „ile mam poza ekranem" jest informacją, której
    // użytkownik potrzebuje w tym momencie; „ile mam otwartych" widać po samym pasku.
    public static string TabStripOverflowTooltip => Loc.Text(nameof(TabStripOverflowTooltip));
    public static string TabStripOverflowFilterWatermark => Loc.Text(nameof(TabStripOverflowFilterWatermark));

    // ── Menu kontekstowe zakładki (M3.3c / §8.3) ────────────────────────────────────────────────────
    // ⚠ Ikony przez `{app:MenuIcon}`, gesty przez `{app:CommandGesture}` — zero nowej chromy
    // (Keyboard Manager etap 5). Tu mieszkają wyłącznie słowa.
    public static string TabMenuClose => Loc.Text(nameof(TabMenuClose));
    public static string TabMenuCloseOthers => Loc.Text(nameof(TabMenuCloseOthers));
    public static string TabMenuCloseAll => Loc.Text(nameof(TabMenuCloseAll));
    public static string TabMenuCloseToTheRight => Loc.Text(nameof(TabMenuCloseToTheRight));
    public static string TabMenuCloseUnmodified => Loc.Text(nameof(TabMenuCloseUnmodified));
    public static string TabMenuRefresh => Loc.Text(nameof(TabMenuRefresh));
    public static string TabMenuCopyObjectName => Loc.Text(nameof(TabMenuCopyObjectName));
    public static string TabMenuRevealInExplorer => Loc.Text(nameof(TabMenuRevealInExplorer));
    public static string TabMenuSettings => Loc.Text(nameof(TabMenuSettings));

    // ⚠⚠ Bramka reguły #11 dla zamykania masowego — CZWARTE wejście do tej samej bramki, obok
    // zamknięcia zakładki, rozłączenia i zamknięcia aplikacji.
    // ⭐ Komunikat WYMIENIA zakładki z pracą ({0} = lista), bo „kilka zakładek ma niezapisane zmiany"
    // nie pozwala podjąć decyzji — a to jest moment, w którym użytkownik ją podejmuje.
    public static string TabsCloseUnsavedTitle => Loc.Text(nameof(TabsCloseUnsavedTitle));
    public static string TabsCloseUnsavedFormat => Loc.Text(nameof(TabsCloseUnsavedFormat));
    public static string TabsCloseUnsavedSave => Loc.Text(nameof(TabsCloseUnsavedSave));
    public static string TabsCloseUnsavedDiscard => Loc.Text(nameof(TabsCloseUnsavedDiscard));

    // ── Debugger (etap 6) ───────────────────────────────────────────────────────────────────────────
    public static string SettingsCategoryDebugger => Loc.Text(nameof(SettingsCategoryDebugger));

    // ⚠ Says the launch panel still offers it, because that is what makes this a DEFAULT rather than a
    // replacement — the recorded D4 wish was "show only params at launch", not "take the choice away".
    public static string SettingsDebuggerIsolationLabel => Loc.Text(nameof(SettingsDebuggerIsolationLabel));
    public static string SettingsDebuggerIsolationDescription => Loc.Text(nameof(SettingsDebuggerIsolationDescription));
    public static string SettingsDebuggerIsolationKeywords => Loc.Text(nameof(SettingsDebuggerIsolationKeywords));

    // ── SQL Formatter ───────────────────────────────────────────────────────────────────────────────
    // Exactly two rows, and that is ratified (§6.4 / §9.1): no line width, no indent size, no comma
    // placement. Both default to lower case, so a user who never opens this page sees the output EmberTern
    // has always produced.
    public static string SettingsCategoryFormatter => Loc.Text(nameof(SettingsCategoryFormatter));

    // ⚠ Says "Format SQL" rather than "the formatter", because that is the scope: the action on the
    // Ctrl+K / toolbar / context menu. SQL that EmberTern composes (Copy as INSERT, .sql export) and
    // generated DDL keep their own casing, by ratified Q1 — a description promising "everywhere" would be
    // a promise the code deliberately does not keep.
    public static string SettingsFormatterKeywordCaseLabel => Loc.Text(nameof(SettingsFormatterKeywordCaseLabel));
    public static string SettingsFormatterKeywordCaseDescription => Loc.Text(nameof(SettingsFormatterKeywordCaseDescription));
    public static string SettingsFormatterKeywordCaseKeywords => Loc.Text(nameof(SettingsFormatterKeywordCaseKeywords));

    public static string SettingsFormatterIdentifierCaseLabel => Loc.Text(nameof(SettingsFormatterIdentifierCaseLabel));
    // ⚠ Says quoted names are untouched because that is a correctness guarantee the user can rely on, not a
    // limitation: "MyTable" is a different object from MYTABLE in Firebird, so re-casing it would change
    // which object the statement names (§0 / architecture rule #11).
    public static string SettingsFormatterIdentifierCaseDescription => Loc.Text(nameof(SettingsFormatterIdentifierCaseDescription));
    public static string SettingsFormatterIdentifierCaseKeywords => Loc.Text(nameof(SettingsFormatterIdentifierCaseKeywords));

    public static string SettingsCaseLower => Loc.Text(nameof(SettingsCaseLower));
    public static string SettingsCaseUpper => Loc.Text(nameof(SettingsCaseUpper));

    // Shown in the docked MessageBanner when a change could not be written. Settings Center is the ONE place
    // where the store's silent refusal (audit A-03) must be spoken: every other writer in the app is
    // incidental, but a dialog whose entire purpose is "change this setting" cannot accept a change and
    // persist nothing without saying so. {0} = the store's diagnostic.
    public static string SettingsSaveRefusedFormat => Loc.Text(nameof(SettingsSaveRefusedFormat));

    // ── Settings export / import (etap 5b) ──────────────────────────────────────────────────────────
    // The user-facing half of EmberTern's own .etsettings format. Design §6.3; the format itself is §15.
    // ⚠ Failure messages are NOT duplicated here. SettingsImportReader / SettingsImportApplier produce them in
    // Core, on purpose (the same reason Firebird connection-failure text lives in the Firebird layer): a status
    // whose meaning is decided in Core should not have its explanation decided somewhere else. Surfaces switch
    // on the STATUS and show the message as-is (§15.8).
    public static string SettingsImportExportLabel => Loc.Text(nameof(SettingsImportExportLabel));
    public static string SettingsImportExportDescription => Loc.Text(nameof(SettingsImportExportDescription));
    public static string SettingsImportExportKeywords => Loc.Text(nameof(SettingsImportExportKeywords));

    public static string SettingsExportButton => Loc.Text(nameof(SettingsExportButton));
    public static string SettingsImportButton => Loc.Text(nameof(SettingsImportButton));
    public static string SettingsOpenFolderButton => Loc.Text(nameof(SettingsOpenFolderButton));
    public static string SettingsOpenFolderTooltip => Loc.Text(nameof(SettingsOpenFolderTooltip));

    // ── Export dialog ───────────────────────────────────────────────────────────────────────────────
    public static string SettingsExportTitle => Loc.Text(nameof(SettingsExportTitle));
    public static string SettingsExportIntro => Loc.Text(nameof(SettingsExportIntro));
    public static string SettingsExportSectionsHeader => Loc.Text(nameof(SettingsExportSectionsHeader));
    public static string SettingsExportPassphraseHeader => Loc.Text(nameof(SettingsExportPassphraseHeader));
    public static string SettingsExportRun => Loc.Text(nameof(SettingsExportRun));
    public static string SettingsExportCancel => Loc.Text(nameof(SettingsExportCancel));
    public static string SettingsExportFileFilter => Loc.Text(nameof(SettingsExportFileFilter));
    public static string SettingsExportSuggestedName => Loc.Text(nameof(SettingsExportSuggestedName));

    public static string SettingsSectionPreferences => Loc.Text(nameof(SettingsSectionPreferences));
    public static string SettingsSectionGridProfiles => Loc.Text(nameof(SettingsSectionGridProfiles));
    public static string SettingsSectionFolders => Loc.Text(nameof(SettingsSectionFolders));
    public static string SettingsSectionConnections => Loc.Text(nameof(SettingsSectionConnections));
    // ⚠ Ratified Q2: the label must state that the file will contain database credentials. It says it plainly —
    // the whole reason the checkbox exists is that the user should be making this decision knowingly.
    public static string SettingsSectionPasswords => Loc.Text(nameof(SettingsSectionPasswords));
    public static string SettingsSectionWorkspaces => Loc.Text(nameof(SettingsSectionWorkspaces));
    public static string SettingsSectionImportProfiles => Loc.Text(nameof(SettingsSectionImportProfiles));

    public static string SettingsExportPassphraseLabel => Loc.Text(nameof(SettingsExportPassphraseLabel));
    public static string SettingsExportPassphraseConfirmLabel => Loc.Text(nameof(SettingsExportPassphraseConfirmLabel));
    // ⚠ Stated where the passphrase is TYPED, not in a help page: a passphrase-derived key means a forgotten
    // passphrase makes the file permanently unreadable, with no reset and no back door (design §6.3.1). That is
    // a consequence of the ratified always-encrypted decision, and the only honest place to say it is here.
    public static string SettingsExportPassphraseWarning => Loc.Text(nameof(SettingsExportPassphraseWarning));
    public static string SettingsExportPassphraseMismatch => Loc.Text(nameof(SettingsExportPassphraseMismatch));
    public static string SettingsExportPassphraseMissing => Loc.Text(nameof(SettingsExportPassphraseMissing));
    public static string SettingsExportNothingSelected => Loc.Text(nameof(SettingsExportNothingSelected));
    // {0} = file name.
    public static string SettingsExportDoneFormat => Loc.Text(nameof(SettingsExportDoneFormat));
    // {0} = the failure message.
    public static string SettingsExportFailedFormat => Loc.Text(nameof(SettingsExportFailedFormat));

    // ── Import dialog ───────────────────────────────────────────────────────────────────────────────
    public static string SettingsImportTitle => Loc.Text(nameof(SettingsImportTitle));
    public static string SettingsImportPickFile => Loc.Text(nameof(SettingsImportPickFile));
    public static string SettingsImportIntro => Loc.Text(nameof(SettingsImportIntro));
    public static string SettingsImportPassphraseLabel => Loc.Text(nameof(SettingsImportPassphraseLabel));
    public static string SettingsImportOpen => Loc.Text(nameof(SettingsImportOpen));
    public static string SettingsImportRun => Loc.Text(nameof(SettingsImportRun));
    public static string SettingsImportCancel => Loc.Text(nameof(SettingsImportCancel));
    public static string SettingsImportContentsHeader => Loc.Text(nameof(SettingsImportContentsHeader));
    // Shown only once the file is open and every box has been unticked — the one state in which Import is dead
    // with nothing on screen saying why.
    public static string SettingsImportNothingSelected => Loc.Text(nameof(SettingsImportNothingSelected));
    // ⚠ Shown only when the file carries passwords AND the row is offered — an import overwrites the password
    // stored for the same connection, which is a thing to say before it happens rather than after.
    public static string SettingsImportPasswordsNote => Loc.Text(nameof(SettingsImportPasswordsNote));
    // ⭐ The honest disclosure of what an import can and cannot do to a RUNNING session, in the place the user
    // decides. Nothing is blocked (EmberTern discloses rather than forbids); it just has to be true.
    public static string SettingsImportLiveSessionNote => Loc.Text(nameof(SettingsImportLiveSessionNote));
    // {0} = the comma-separated sections taken. {1} = the preserved copy's file name.
    public static string SettingsImportDoneFormat => Loc.Text(nameof(SettingsImportDoneFormat));
    // Used when there was no settings.dat to preserve — a first run.
    public static string SettingsImportDoneNoBackupFormat => Loc.Text(nameof(SettingsImportDoneNoBackupFormat));

    // ── Canonical command names (CommandDescriptor.Title) ───────────────────────────────────────────
    // ONE host-independent name per command, for surfaces that LIST commands: the Keyboard Shortcuts window
    // today, a Command Palette later. Deliberately separate from the tooltip strings above and below, which
    // stay host-specific prose — that distinction is why adding Title did not reopen etap 4's decision.
    // ⛔ These belong here and not in CommandCatalog: the catalog owns the gesture, UiStrings owns the words.
    public static string CommandTitleGo => Loc.Text(nameof(CommandTitleGo));
    public static string CommandTitleExecuteQuery => Loc.Text(nameof(CommandTitleExecuteQuery));
    public static string CommandTitleExecuteQueryFull => Loc.Text(nameof(CommandTitleExecuteQueryFull));
    public static string CommandTitleFormatSql => Loc.Text(nameof(CommandTitleFormatSql));
    public static string CommandTitleCompile => Loc.Text(nameof(CommandTitleCompile));
    public static string CommandTitleImportValidate => Loc.Text(nameof(CommandTitleImportValidate));
    public static string CommandTitleImportRefresh => Loc.Text(nameof(CommandTitleImportRefresh));
    public static string CommandTitleImportBrowse => Loc.Text(nameof(CommandTitleImportBrowse));

    public static string CommandTitleDebuggerStepOver => Loc.Text(nameof(CommandTitleDebuggerStepOver));
    public static string CommandTitleDebuggerStepInto => Loc.Text(nameof(CommandTitleDebuggerStepInto));
    public static string CommandTitleDebuggerStepOut => Loc.Text(nameof(CommandTitleDebuggerStepOut));
    public static string CommandTitleDebuggerRunToCursor => Loc.Text(nameof(CommandTitleDebuggerRunToCursor));
    public static string CommandTitleDebuggerStop => Loc.Text(nameof(CommandTitleDebuggerStop));
    public static string CommandTitleDebuggerRestart => Loc.Text(nameof(CommandTitleDebuggerRestart));
    public static string CommandTitleDebuggerToggleBreakpoint => Loc.Text(nameof(CommandTitleDebuggerToggleBreakpoint));
    public static string CommandTitleDebuggerEvaluateSelection => Loc.Text(nameof(CommandTitleDebuggerEvaluateSelection));
    public static string CommandTitleDebuggerSaveSource => Loc.Text(nameof(CommandTitleDebuggerSaveSource));

    public static string CommandTitleEditorFind => Loc.Text(nameof(CommandTitleEditorFind));
    public static string CommandTitleEditorReplace => Loc.Text(nameof(CommandTitleEditorReplace));
    public static string CommandTitleEditorCompletion => Loc.Text(nameof(CommandTitleEditorCompletion));
    public static string CommandTitleEditorParameterHelper => Loc.Text(nameof(CommandTitleEditorParameterHelper));
    public static string CommandTitleEditorRename => Loc.Text(nameof(CommandTitleEditorRename));
    public static string CommandTitleEditorPeekDefinition => Loc.Text(nameof(CommandTitleEditorPeekDefinition));
    public static string CommandTitleEditorQuickFix => Loc.Text(nameof(CommandTitleEditorQuickFix));
    public static string CommandTitleEditorExpandConstruct => Loc.Text(nameof(CommandTitleEditorExpandConstruct));
    public static string CommandTitleEditorNextDiagnostic => Loc.Text(nameof(CommandTitleEditorNextDiagnostic));
    public static string CommandTitleEditorPreviousDiagnostic => Loc.Text(nameof(CommandTitleEditorPreviousDiagnostic));

    public static string CommandTitleNewObject => Loc.Text(nameof(CommandTitleNewObject));
    public static string CommandTitleDeleteObject => Loc.Text(nameof(CommandTitleDeleteObject));
    public static string CommandTitleRefreshMetadata => Loc.Text(nameof(CommandTitleRefreshMetadata));

    // Generic on purpose: these route through the app's ONE collection router, which serves fields, rows,
    // columns, parameters and variables. The per-collection nouns ("New field") belong to the toolbar and the
    // grid's own menu, which know which collection they are looking at; a catalogue does not.
    public static string CommandTitleCollectionAdd => Loc.Text(nameof(CommandTitleCollectionAdd));
    public static string CommandTitleCollectionEdit => Loc.Text(nameof(CommandTitleCollectionEdit));
    public static string CommandTitleCollectionRemove => Loc.Text(nameof(CommandTitleCollectionRemove));

    public static string CommandTitleGlobalSearch => Loc.Text(nameof(CommandTitleGlobalSearch));
    public static string CommandTitleFocusSidebarFilter => Loc.Text(nameof(CommandTitleFocusSidebarFilter));
    public static string CommandTitleCommit => Loc.Text(nameof(CommandTitleCommit));
    public static string CommandTitleRollback => Loc.Text(nameof(CommandTitleRollback));
    public static string CommandTitleCloseTab => Loc.Text(nameof(CommandTitleCloseTab));

    // ⛔ `StatusBarVersionFormat` usunięty w M3.1b (decyzja D3): nazwa aplikacji i numer wersji nie należą
    // do paska statusu, tylko do okna About. `AppInfo` pozostaje jedynym źródłem wersji.
    // No gesture is shown beside Exit: EmberTern does not own Alt+F4, and a gesture typed by hand is the
    // drift CommandTip exists to prevent (gotcha #284). It routes through the window's ordinary close, so
    // unsaved work and an open transaction still get their prompts.
    public static string AppMenuExitTooltip => Loc.Text(nameof(AppMenuExitTooltip));
    public static string SidebarExpandTooltip => Loc.Text(nameof(SidebarExpandTooltip));
    public static string ResultsPanelMaximizeTooltip => Loc.Text(nameof(ResultsPanelMaximizeTooltip));

    // Max length for a connection profile name. 60 chars comfortably holds
    // "ENV - Client - Database"-style names while keeping the titlebar chip and
    // sidebar rows from being pushed off-screen by an abusive name.
    //
    // ⚠⚠ A NUMBER, not a word — and the App localization stage nearly cost it. That sweep rewrote every
    // `{x:Static app:UiStrings.X}` into `{app:Loc X}`, and NewConnectionDialog's MaxLength went with the
    // strings: the binding then asked the CATALOG for a key that cannot exist, `Loc.Text` returned the key
    // name, and handing `MaxLength` a string failed conversion silently — so the limit was simply not
    // applied. ⛔ It is read with `{x:Static}` on purpose; `EveryLocBindingKey_ResolvesToSomething` fails
    // the build if it is ever asked of the catalog again.
    public const int ConnectionNameMaxLength = 60;

    public static string DialogNewConnectionTitle => Loc.Text(nameof(DialogNewConnectionTitle));
    public static string DialogEditConnectionTitle => Loc.Text(nameof(DialogEditConnectionTitle));
    public static string ConnectionEdit => Loc.Text(nameof(ConnectionEdit));
    public static string DialogSectionGeneral => Loc.Text(nameof(DialogSectionGeneral));
    public static string DialogFieldName => Loc.Text(nameof(DialogFieldName));
    public static string DialogFieldHost => Loc.Text(nameof(DialogFieldHost));
    public static string DialogFieldPort => Loc.Text(nameof(DialogFieldPort));
    public static string DialogFieldDatabasePath => Loc.Text(nameof(DialogFieldDatabasePath));
    public static string DialogFieldUsername => Loc.Text(nameof(DialogFieldUsername));
    public static string DialogFieldPassword => Loc.Text(nameof(DialogFieldPassword));
    public static string DialogFieldCharset => Loc.Text(nameof(DialogFieldCharset));
    public static string DialogFieldDialect => Loc.Text(nameof(DialogFieldDialect));
    // (Removed 2026-07-27, audit A-09: DialogFieldTransactionProfile + its Data/Metadata pair were three
    // captions for a connection-dialog field that no longer exists. The TPB profile is not user-configurable —
    // TransactionService.EnforcedProfile is a constant, deliberately — so a label offering to configure it
    // described a control nothing could honour. They were defined and never referenced.)
    public static string DialogTestConnection => Loc.Text(nameof(DialogTestConnection));
    public static string DialogSave => Loc.Text(nameof(DialogSave));
    public static string DialogCancel => Loc.Text(nameof(DialogCancel));
    public static string DialogBrowse => Loc.Text(nameof(DialogBrowse));

    // Developer Mode — the single user-facing switch that replaces the TPB profile
    // pickers. No transaction terminology is exposed (NOWAIT/WAIT/consistency are
    // implementation details).
    public static string DialogFieldDeveloperMode => Loc.Text(nameof(DialogFieldDeveloperMode));
    public static string DeveloperModeDescription => Loc.Text(nameof(DeveloperModeDescription));
    public static string DeveloperModeBadge => Loc.Text(nameof(DeveloperModeBadge));
    public static string DeveloperModeBadgeTooltip => Loc.Text(nameof(DeveloperModeBadgeTooltip));

    // Transaction profile labels (IBExpert terms — kept in English on purpose).
    public static string TransactionProfileReadCommitted => Loc.Text(nameof(TransactionProfileReadCommitted));
    public static string TransactionProfileSnapshot => Loc.Text(nameof(TransactionProfileSnapshot));
    public static string TransactionProfileReadOnlyTableStability => Loc.Text(nameof(TransactionProfileReadOnlyTableStability));
    public static string TransactionProfileReadWriteTableStability => Loc.Text(nameof(TransactionProfileReadWriteTableStability));
    // Per-profile one-line descriptions shown under the picker.
    public static string TransactionProfileReadCommittedDesc => Loc.Text(nameof(TransactionProfileReadCommittedDesc));
    public static string TransactionProfileSnapshotDesc => Loc.Text(nameof(TransactionProfileSnapshotDesc));
    public static string TransactionProfileReadOnlyTableStabilityDesc => Loc.Text(nameof(TransactionProfileReadOnlyTableStabilityDesc));
    public static string TransactionProfileReadWriteTableStabilityDesc => Loc.Text(nameof(TransactionProfileReadWriteTableStabilityDesc));
    // Title-bar transaction-profile block (C2): two stacked lines, each a static lane
    // label + the full profile name in a lane-colored badge. Vertical layout keeps the
    // block narrow while the full name stays readable without hovering.
    public static string TransactionProfileDataLabel => Loc.Text(nameof(TransactionProfileDataLabel));
    public static string TransactionProfileMetadataLabel => Loc.Text(nameof(TransactionProfileMetadataLabel));
    public static string TransactionProfileDataChipTooltipFormat => Loc.Text(nameof(TransactionProfileDataChipTooltipFormat));
    public static string TransactionProfileMetadataChipTooltipFormat => Loc.Text(nameof(TransactionProfileMetadataChipTooltipFormat));

    public static string TestInProgress => Loc.Text(nameof(TestInProgress));
    public static string TestSuccess => Loc.Text(nameof(TestSuccess));

    public static string ValidationNameRequired => Loc.Text(nameof(ValidationNameRequired));
    public static string ValidationDatabaseRequired => Loc.Text(nameof(ValidationDatabaseRequired));

    public static string ToolbarExecute => Loc.Text(nameof(ToolbarExecute));
    public static string ToolbarCancel => Loc.Text(nameof(ToolbarCancel));
    // The shortcut chip beside the Execute button — the gesture alone, no label.
    public static string ToolbarExecuteHint => CommandTip.Gesture(CommandId.Go);
    // Tooltip on the single Execute button — surfaces the Shift+F5 full-read power path (Variant A+D:
    // one button, no split-button, no second Execute button).
    // Two gestures in one tooltip, so it interpolates CommandTip.Gesture twice rather than using Sentence —
    // still the one formatter, still nothing typed by hand.
    public static string ToolbarExecuteTooltip => $"Execute  ·  {CommandTip.Gesture(CommandId.Go)} preview  ·  "
        + $"{CommandTip.Gesture(CommandId.ExecuteQueryFull)} all rows";
    public static string ToolbarClearEditor => Loc.Text(nameof(ToolbarClearEditor));
    public static string ToolbarClearEditorIcon => Loc.Text(nameof(ToolbarClearEditorIcon));
    public static string ToolbarClearEditorTooltip => Loc.Text(nameof(ToolbarClearEditorTooltip));
    public static string ToolbarCloseTab => Loc.Text(nameof(ToolbarCloseTab));
    public static string ToolbarCloseTabIcon => Loc.Text(nameof(ToolbarCloseTabIcon));
    public static string ToolbarCloseTabTooltip => CommandTip.For(CommandId.CloseTab, Loc.Text(nameof(ToolbarCloseTabTooltip)));
    public static string ToolbarNewQueryIcon => Loc.Text(nameof(ToolbarNewQueryIcon));
    public static string ToolbarNewQueryTooltip => Loc.Text(nameof(ToolbarNewQueryTooltip));
    public static string ToolbarToggleQueryPanelIcon => Loc.Text(nameof(ToolbarToggleQueryPanelIcon));
    public static string ToolbarToggleQueryPanelTooltip => Loc.Text(nameof(ToolbarToggleQueryPanelTooltip));
    public static string ToolbarFormatSqlIcon => Loc.Text(nameof(ToolbarFormatSqlIcon));
    // ⚠ This constant is why CommandTip exists: it said "Alt+F" for a whole etap after the gesture became
    // Ctrl+K, and nothing failed. It can no longer disagree with the catalog.
    public static string ToolbarFormatSqlTooltip => CommandTip.For(CommandId.FormatSql, Loc.Text(nameof(ToolbarFormatSqlTooltip)));
    public static string ToolbarRefreshDataIcon => Loc.Text(nameof(ToolbarRefreshDataIcon));
    public static string ToolbarRefreshDataTooltip => Loc.Text(nameof(ToolbarRefreshDataTooltip));

    public static string QueryPanelHeader => Loc.Text(nameof(QueryPanelHeader));
    public static string QueryPanelEmptyHint => Loc.Text(nameof(QueryPanelEmptyHint));
    public static string QueryDefaultNameFormat => Loc.Text(nameof(QueryDefaultNameFormat));
    public static string QueryDeleteTooltip => Loc.Text(nameof(QueryDeleteTooltip));
    public static string QueryClearAllTooltip => Loc.Text(nameof(QueryClearAllTooltip));
    public static string QueryDeleteConfirmTitle => Loc.Text(nameof(QueryDeleteConfirmTitle));
    public static string QueryDeleteConfirmFormat => Loc.Text(nameof(QueryDeleteConfirmFormat));
    public static string QueryDeleteConfirmYes => Loc.Text(nameof(QueryDeleteConfirmYes));
    public static string QueryClearAllConfirmTitle => Loc.Text(nameof(QueryClearAllConfirmTitle));
    public static string QueryClearAllConfirmMessage => Loc.Text(nameof(QueryClearAllConfirmMessage));
    public static string QueryClearAllConfirmYes => Loc.Text(nameof(QueryClearAllConfirmYes));
    public static string GridCopyCell => Loc.Text(nameof(GridCopyCell));
    public static string GridCopyRow => Loc.Text(nameof(GridCopyRow));
    public static string GridCopyRowWithHeaders => Loc.Text(nameof(GridCopyRowWithHeaders));
    public static string GridCopyAllWithHeaders => Loc.Text(nameof(GridCopyAllWithHeaders));
    public static string GridCopiedToClipboardFormat => Loc.Text(nameof(GridCopiedToClipboardFormat));
    public static string GridCopiedCellLabel => Loc.Text(nameof(GridCopiedCellLabel));
    public static string GridCopiedRowLabel => Loc.Text(nameof(GridCopiedRowLabel));
    public static string GridCopiedRowsFormat => Loc.Text(nameof(GridCopiedRowsFormat));

    // ── Copy as INSERT / UPDATE ───────────────────────────────────────────────
    // A disabled item here always carries a REASON (see SqlCopyReasonText): naming the actual obstacle
    // teaches the tool's model, and it is strictly more information than the alternative — generating
    // INSERT INTO TABLE_NAME (…) for the user to fix — could ever convey.
    public static string GridCopyAsInsert => Loc.Text(nameof(GridCopyAsInsert));
    public static string GridCopyAsUpdate => Loc.Text(nameof(GridCopyAsUpdate));
    public static string GridCopiedInsertLabel => Loc.Text(nameof(GridCopiedInsertLabel));
    public static string GridCopiedUpdateLabel => Loc.Text(nameof(GridCopiedUpdateLabel));
    public static string GridCopyNoRow => Loc.Text(nameof(GridCopyNoRow));

    // The reasons. Three kinds of claim, and the wording keeps them apart on purpose: what the QUERY
    // cannot be, what EMBERTERN cannot do yet, and what is merely not ready.
    public static string SqlCopyUnavailablePrefix => Loc.Text(nameof(SqlCopyUnavailablePrefix));
    public static string SqlCopyReasonSetOperation => Loc.Text(nameof(SqlCopyReasonSetOperation));
    public static string SqlCopyReasonMultipleTablesFormat => Loc.Text(nameof(SqlCopyReasonMultipleTablesFormat));
    public static string SqlCopyReasonJoinFormat => Loc.Text(nameof(SqlCopyReasonJoinFormat));
    public static string SqlCopyReasonAggregate => Loc.Text(nameof(SqlCopyReasonAggregate));
    public static string SqlCopyReasonNoSourceTable => Loc.Text(nameof(SqlCopyReasonNoSourceTable));
    public static string SqlCopyReasonDuplicateColumnFormat => Loc.Text(nameof(SqlCopyReasonDuplicateColumnFormat));
    public static string SqlCopyReasonUnknownObjectFormat => Loc.Text(nameof(SqlCopyReasonUnknownObjectFormat));
    public static string SqlCopyReasonNotATableFormat => Loc.Text(nameof(SqlCopyReasonNotATableFormat));
    public static string SqlCopyReasonViewFormat => Loc.Text(nameof(SqlCopyReasonViewFormat));
    // A CURRENT LIMITATION of EmberTern's analysis — never worded as a property of SQL.
    public static string SqlCopyReasonCte => Loc.Text(nameof(SqlCopyReasonCte));
    public static string SqlCopyReasonNotUnderstood => Loc.Text(nameof(SqlCopyReasonNotUnderstood));
    // TRANSIENT — the user's response is to wait, not to change anything.
    public static string SqlCopyReasonCatalogNotLoadedFormat => Loc.Text(nameof(SqlCopyReasonCatalogNotLoadedFormat));
    public static string SqlCopyReasonUnknownColumnFormat => Loc.Text(nameof(SqlCopyReasonUnknownColumnFormat));
    public static string SqlCopyReasonNoPrimaryKeyFormat => Loc.Text(nameof(SqlCopyReasonNoPrimaryKeyFormat));
    public static string SqlCopyReasonIncompletePkFormat => Loc.Text(nameof(SqlCopyReasonIncompletePkFormat));
    public static string SqlCopyReasonNoWritableColumnsFormat => Loc.Text(nameof(SqlCopyReasonNoWritableColumnsFormat));
    public static string SqlCopyReasonKeyValueIsNullFormat => Loc.Text(nameof(SqlCopyReasonKeyValueIsNullFormat));
    public static string SqlCopyReasonValueNotRenderableFormat => Loc.Text(nameof(SqlCopyReasonValueNotRenderableFormat));
    public static string SqlCopyReasonValueTooLargeFormat => Loc.Text(nameof(SqlCopyReasonValueTooLargeFormat));
    public static string SqlCopyReasonStatementTooLongFormat => Loc.Text(nameof(SqlCopyReasonStatementTooLongFormat));
    // Context-menu toggle for grid column layout — when checked, columns auto-size to
    // content and manual widths aren't remembered; when unchecked, manual widths persist.
    public static string GridAutoFitColumns => Loc.Text(nameof(GridAutoFitColumns));

    public static string ResultsEmptyHint => Loc.Text(nameof(ResultsEmptyHint));
    public static string MessagesEmptyHint => Loc.Text(nameof(MessagesEmptyHint));
    public static string ExecutingStatus => Loc.Text(nameof(ExecutingStatus));
    public static string CancellingStatus => Loc.Text(nameof(CancellingStatus));
    // Live execution-timer indicator (SQL Editor / Execute Procedure/Function / Script Executor).
    // One cohesive label — {0} = mm:ss.f elapsed.
    public static string ExecutionElapsedFormat => Loc.Text(nameof(ExecutionElapsedFormat));
    public static string NoConnectionMessage => Loc.Text(nameof(NoConnectionMessage));
    // Compile pre-condition refusals, shared by EVERY object editor's compile and the debugger's Save
    // (UX Polish Seam 6b). An ISavableObjectEditor adapter reads success as "no error after the attempt",
    // so a compile that cannot run must SAY so — otherwise the save-and-close WorkGuard is told the work
    // was written when nothing was, and discards it. NoConnectionMessage above covers the no-DDL-executor
    // case; this one covers "the buffer holds nothing to compile".
    public static string EditorNothingToCompile => Loc.Text(nameof(EditorNothingToCompile));
    // ─── Change-safety refusals (ObjectChangeGate) ──────────────────────────────────────────────
    // Every object editor compiles by REPLACING a whole object (CREATE OR ALTER … AS <entire body>), so a
    // compile can discard work the editor never saw. These three sentences are the whole user-facing
    // vocabulary of that gate; {0} = the object's name.
    //
    // Each one names the effect first, then the ONE next step. "Revert" and the SQL Editor are both
    // existing features, deliberately: the escape hatch for a deliberate overwrite already exists (run the
    // statement yourself, where the console makes it unmistakably your decision), which is why the gate
    // ships with no force-overwrite button of its own.
    public static string ObjectChangedInDatabaseFormat => Loc.Text(nameof(ObjectChangedInDatabaseFormat));
    public static string ObjectAlreadyExistsFormat => Loc.Text(nameof(ObjectAlreadyExistsFormat));
    public static string ObjectChangeUnverifiableFormat => Loc.Text(nameof(ObjectChangeUnverifiableFormat));
    // Fallback label when the object has no name yet — the message must still read as a sentence.
    public static string ObjectChangeUnnamedObject => Loc.Text(nameof(ObjectChangeUnnamedObject));
    // ─── Settings health (audit A-03) ───────────────────────────────────────────────────────────
    // Shown when settings.dat exists but this build cannot read it. Saving is refused for the whole session so
    // the unreadable file is never replaced, which means nothing the user does will persist — and that has to
    // be said out loud, with the path (so they can back it up or move it) and the reason (so they can tell a
    // wrong-machine DPAPI file, which is intact, from a damaged one).
    // {0} = full path to settings.dat, {1} = the load diagnostic.
    // ⭐ Localized (L1). Also the app's clearest illustration of the D‑3 boundary: {1} is a diagnostic
    // produced by CORE (ApplicationSettingsStore) and shown verbatim, so today one half of this sentence is
    // translatable and the other half is not. Stage L4 turns that argument into a resolved MessageKey.
    public static string SettingsUnreadableWarningFormat => Loc.Text(nameof(SettingsUnreadableWarningFormat));
    // The code-action light bulb (Stage Q / Q3) — a discreet affordance for the same menu Ctrl+. opens.
    public static string CodeActionsTooltip => CommandTip.For(
        CommandId.EditorQuickFix, Loc.Text(nameof(CodeActionsTooltip)));
    // Shown at the foot of the diagnostic hover when fixes exist there. Information only — the hover
    // never offers an action (§15.1.1); this just makes the shortcut discoverable.
    public static string CodeActionsHoverHint => CommandTip.For(
        CommandId.EditorQuickFix, Loc.Text(nameof(CodeActionsHoverHint)));
    // Diagnostics-panel row → the same menu (Stage Q / Q5).
    public static string CodeActionsMenuItem => Loc.Text(nameof(CodeActionsMenuItem));
    public static string QueryCancelledMessage => Loc.Text(nameof(QueryCancelledMessage));
    public static string AffectedRowsFormat => Loc.Text(nameof(AffectedRowsFormat));
    // Truncated-Preview notice bar — loud + actionable (A.6). {0} = rows loaded so far
    // (thousands-separated — these strings front large full reads).
    public static string ResultsTruncatedFormat => Loc.Text(nameof(ResultsTruncatedFormat));
    // Full hit the hard safety ceiling. {0} = ceiling row count.
    public static string ResultsCeilingFormat => Loc.Text(nameof(ResultsCeilingFormat));
    // Live counter shown in the status area while a Full / Load-all read streams. {0} = rows so far.
    public static string ResultsLoadingFormat => Loc.Text(nameof(ResultsLoadingFormat));
    public static string ToolbarLoadAllRows => Loc.Text(nameof(ToolbarLoadAllRows));
    // Smart soft-threshold prompt (Etap 2) — asked once mid-stream when a Full load crosses the soft
    // threshold and more rows remain. {0} = rows loaded so far.
    public static string LoadAllThresholdTitle => Loc.Text(nameof(LoadAllThresholdTitle));
    public static string LoadAllThresholdMessageFormat => Loc.Text(nameof(LoadAllThresholdMessageFormat));
    public static string LoadAllThresholdKeep => Loc.Text(nameof(LoadAllThresholdKeep));
    public static string LoadAllThresholdStop => Loc.Text(nameof(LoadAllThresholdStop));
    public static string RowsFetchedFormat => Loc.Text(nameof(RowsFetchedFormat));
    public static string MessagesCopyAll => Loc.Text(nameof(MessagesCopyAll));
    public static string MessagesClear => Loc.Text(nameof(MessagesClear));
    // {0} = current page, {1} = total pages, {2} = total rows in the result set.
    public static string ResultsPaginationHintFormat => Loc.Text(nameof(ResultsPaginationHintFormat));
    // Record position (IBExpert-style). {0} = 1-based absolute position of the
    // selected row in the full (sorted) result, {1} = total row count.
    public static string RecordPositionFormat => Loc.Text(nameof(RecordPositionFormat));
    // Shown when the grid has rows but none is selected. {0} = total row count.
    public static string RecordCountFormat => Loc.Text(nameof(RecordCountFormat));
    // Preview variants — the true total is unknown (only the first N were loaded), so "N+" + a
    // "(preview)" marker makes the fragment unmissable even away from the notice bar. Thousands-
    // separated (preview counts can be large, e.g. a 250,000-row soft-stop).
    public static string RecordPositionPreviewFormat => Loc.Text(nameof(RecordPositionPreviewFormat));
    public static string RecordCountPreviewFormat => Loc.Text(nameof(RecordCountPreviewFormat));

    // ── Grid filtering + aggregation (shared across all data grids) ──
    // Operator labels (filter condition rows).
    public static string FilterOpEquals => Loc.Text(nameof(FilterOpEquals));
    public static string FilterOpNotEquals => Loc.Text(nameof(FilterOpNotEquals));
    public static string FilterOpLessThan => Loc.Text(nameof(FilterOpLessThan));
    public static string FilterOpLessOrEqual => Loc.Text(nameof(FilterOpLessOrEqual));
    public static string FilterOpGreaterThan => Loc.Text(nameof(FilterOpGreaterThan));
    public static string FilterOpGreaterOrEqual => Loc.Text(nameof(FilterOpGreaterOrEqual));
    public static string FilterOpContains => Loc.Text(nameof(FilterOpContains));
    public static string FilterOpStartsWith => Loc.Text(nameof(FilterOpStartsWith));
    public static string FilterOpEndsWith => Loc.Text(nameof(FilterOpEndsWith));
    public static string FilterOpIsNull => Loc.Text(nameof(FilterOpIsNull));
    public static string FilterOpIsNotNull => Loc.Text(nameof(FilterOpIsNotNull));
    // Aggregate labels.
    public static string AggregateSum => Loc.Text(nameof(AggregateSum));
    public static string AggregateAvg => Loc.Text(nameof(AggregateAvg));
    public static string AggregateCount => Loc.Text(nameof(AggregateCount));
    public static string AggregateCountDistinct => Loc.Text(nameof(AggregateCountDistinct));
    public static string AggregateMin => Loc.Text(nameof(AggregateMin));
    public static string AggregateMax => Loc.Text(nameof(AggregateMax));
    // Filter panel chrome.
    public static string FilterToggleTooltip => Loc.Text(nameof(FilterToggleTooltip));
    public static string FilterPanelTitle => Loc.Text(nameof(FilterPanelTitle));
    public static string FilterAddCondition => Loc.Text(nameof(FilterAddCondition));
    public static string FilterApply => Loc.Text(nameof(FilterApply));
    public static string FilterClear => Loc.Text(nameof(FilterClear));
    public static string FilterMatchAll => Loc.Text(nameof(FilterMatchAll));
    public static string FilterMatchAny => Loc.Text(nameof(FilterMatchAny));
    public static string FilterEmptyHint => Loc.Text(nameof(FilterEmptyHint));
    public static string FilterRemoveConditionTooltip => Loc.Text(nameof(FilterRemoveConditionTooltip));
    // Filter-from-cell context menu.
    public static string FilterByValue => Loc.Text(nameof(FilterByValue));
    public static string FilterExcludeValue => Loc.Text(nameof(FilterExcludeValue));
    public static string FilterContainsValue => Loc.Text(nameof(FilterContainsValue));
    // Aggregation bar chrome.
    public static string AggregationToggleTooltip => Loc.Text(nameof(AggregationToggleTooltip));
    public static string AggregationBarTitle => Loc.Text(nameof(AggregationBarTitle));
    public static string AggregationAddLine => Loc.Text(nameof(AggregationAddLine));
    // Placeholder on the function picker — picking a function adds the aggregate chip.
    public static string AggregationFunctionPlaceholder => Loc.Text(nameof(AggregationFunctionPlaceholder));
    public static string AggregationEmptyHint => Loc.Text(nameof(AggregationEmptyHint));
    public static string AggregationRemoveLineTooltip => Loc.Text(nameof(AggregationRemoveLineTooltip));
    public static string AggregationRecomputeTooltip => Loc.Text(nameof(AggregationRecomputeTooltip));
    public static string AggregationNullResult => Loc.Text(nameof(AggregationNullResult));
    public static string AggregationErrorResult => Loc.Text(nameof(AggregationErrorResult));

    // Main tab names — English (the app is English-language; the earlier
    // "keep Polish" choice was reversed 2026-07-02).
    public static string TableDetailTabFields => Loc.Text(nameof(TableDetailTabFields));
    public static string TableDetailTabConstraints => Loc.Text(nameof(TableDetailTabConstraints));
    public static string TableDetailTabIndexes => Loc.Text(nameof(TableDetailTabIndexes));
    public static string TableDetailTabDependencies => Loc.Text(nameof(TableDetailTabDependencies));
    public static string TableDetailDependsOnHeader => Loc.Text(nameof(TableDetailDependsOnHeader));
    public static string TableDetailDependedOnByHeader => Loc.Text(nameof(TableDetailDependedOnByHeader));
    public static string TableDetailDependencyType => Loc.Text(nameof(TableDetailDependencyType));
    public static string TableDetailDependencyName => Loc.Text(nameof(TableDetailDependencyName));
    public static string TableDetailDependencyField => Loc.Text(nameof(TableDetailDependencyField));
    public static string TableDetailTabData => Loc.Text(nameof(TableDetailTabData));
    public static string TableDetailTabDescription => Loc.Text(nameof(TableDetailTabDescription));
    public static string TableDetailTabDdl => Loc.Text(nameof(TableDetailTabDdl));
    public static string TableDetailConstraintSubTabPrimaryKey => Loc.Text(nameof(TableDetailConstraintSubTabPrimaryKey));
    public static string TableDetailConstraintSubTabForeignKey => Loc.Text(nameof(TableDetailConstraintSubTabForeignKey));
    public static string TableDetailConstraintSubTabCheck => Loc.Text(nameof(TableDetailConstraintSubTabCheck));
    public static string TableDetailConstraintSubTabUnique => Loc.Text(nameof(TableDetailConstraintSubTabUnique));

    public static string TableDetailLoadingHint => Loc.Text(nameof(TableDetailLoadingHint));
    public static string TableDetailColumnPosition => Loc.Text(nameof(TableDetailColumnPosition));
    public static string TableDetailColumnName => Loc.Text(nameof(TableDetailColumnName));
    public static string TableDetailColumnType => Loc.Text(nameof(TableDetailColumnType));
    public static string TableDetailColumnSize => Loc.Text(nameof(TableDetailColumnSize));
    public static string TableDetailColumnScale => Loc.Text(nameof(TableDetailColumnScale));
    public static string TableDetailColumnNotNull => Loc.Text(nameof(TableDetailColumnNotNull));
    public static string TableDetailColumnDefault => Loc.Text(nameof(TableDetailColumnDefault));
    public static string TableDetailColumnDescription => Loc.Text(nameof(TableDetailColumnDescription));
    public static string TableDetailColumnPrimaryKey => Loc.Text(nameof(TableDetailColumnPrimaryKey));
    public static string TableDetailColumnForeignKey => Loc.Text(nameof(TableDetailColumnForeignKey));
    public static string TableDetailColumnUnique => Loc.Text(nameof(TableDetailColumnUnique));
    public static string TableDetailColumnDomain => Loc.Text(nameof(TableDetailColumnDomain));
    public static string TableDetailColumnForeignKeyTable => Loc.Text(nameof(TableDetailColumnForeignKeyTable));
    public static string TableDetailColumnComputed => Loc.Text(nameof(TableDetailColumnComputed));
    public static string TableDetailColumnCharset => Loc.Text(nameof(TableDetailColumnCharset));
    public static string TableDetailColumnAutoIncrement => Loc.Text(nameof(TableDetailColumnAutoIncrement));
    public static string TableDetailColumnAutoIncrementTooltip => Loc.Text(nameof(TableDetailColumnAutoIncrementTooltip));

    public static string TableDetailIndexType => Loc.Text(nameof(TableDetailIndexType));
    public static string TableDetailIndexFields => Loc.Text(nameof(TableDetailIndexFields));
    public static string TableDetailIndexExpression => Loc.Text(nameof(TableDetailIndexExpression));
    public static string TableDetailIndexUnique => Loc.Text(nameof(TableDetailIndexUnique));
    public static string TableDetailIndexDescending => Loc.Text(nameof(TableDetailIndexDescending));
    public static string TableDetailIndexPrimary => Loc.Text(nameof(TableDetailIndexPrimary));
    public static string TableDetailIndexActive => Loc.Text(nameof(TableDetailIndexActive));
    public static string TableDetailIndexStatistics => Loc.Text(nameof(TableDetailIndexStatistics));

    public static string TableDetailConstraintFields => Loc.Text(nameof(TableDetailConstraintFields));
    public static string TableDetailConstraintRefTable => Loc.Text(nameof(TableDetailConstraintRefTable));
    public static string TableDetailConstraintRefFields => Loc.Text(nameof(TableDetailConstraintRefFields));
    public static string TableDetailConstraintUpdateRule => Loc.Text(nameof(TableDetailConstraintUpdateRule));
    public static string TableDetailConstraintDeleteRule => Loc.Text(nameof(TableDetailConstraintDeleteRule));
    public static string TableDetailConstraintSource => Loc.Text(nameof(TableDetailConstraintSource));
    public static string TableDetailConstraintIndexName => Loc.Text(nameof(TableDetailConstraintIndexName));
    public static string TableDetailConstraintSort => Loc.Text(nameof(TableDetailConstraintSort));
    public static string TableDetailConstraintSortAscending => Loc.Text(nameof(TableDetailConstraintSortAscending));
    public static string TableDetailConstraintSortDescending => Loc.Text(nameof(TableDetailConstraintSortDescending));

    public static string TableDetailDataPagedHintFormat => Loc.Text(nameof(TableDetailDataPagedHintFormat));
    public static string TableDetailDataPreviewSortedByFormat => Loc.Text(nameof(TableDetailDataPreviewSortedByFormat));

    public static string TableDetailPaginationFirstIcon => Loc.Text(nameof(TableDetailPaginationFirstIcon));
    public static string TableDetailPaginationPreviousIcon => Loc.Text(nameof(TableDetailPaginationPreviousIcon));
    public static string TableDetailPaginationNextIcon => Loc.Text(nameof(TableDetailPaginationNextIcon));
    public static string TableDetailPaginationLastIcon => Loc.Text(nameof(TableDetailPaginationLastIcon));
    public static string TableDetailPaginationFirstTooltip => Loc.Text(nameof(TableDetailPaginationFirstTooltip));
    public static string TableDetailPaginationPreviousTooltip => Loc.Text(nameof(TableDetailPaginationPreviousTooltip));
    public static string TableDetailPaginationNextTooltip => Loc.Text(nameof(TableDetailPaginationNextTooltip));
    public static string TableDetailPaginationLastTooltip => Loc.Text(nameof(TableDetailPaginationLastTooltip));
    public static string TableDetailDataPreviewNullPlaceholder => Loc.Text(nameof(TableDetailDataPreviewNullPlaceholder));
    public static string TableDetailDataLoadingHint => Loc.Text(nameof(TableDetailDataLoadingHint));
    public static string TableDetailDescriptionEmpty => Loc.Text(nameof(TableDetailDescriptionEmpty));

    public static string DataEditAddRowIcon => Loc.Text(nameof(DataEditAddRowIcon));
    public static string DataEditAddRowTooltip => Loc.Text(nameof(DataEditAddRowTooltip));
    public static string DataEditDeleteRowIcon => Loc.Text(nameof(DataEditDeleteRowIcon));
    public static string DataEditDeleteRowTooltip => Loc.Text(nameof(DataEditDeleteRowTooltip));
    // Context-menu labels for the same two commands, in the surface's New / Edit / Delete vocabulary.
    public static string DataEditNewRow => Loc.Text(nameof(DataEditNewRow));
    public static string DataEditDeleteRow => Loc.Text(nameof(DataEditDeleteRow));
    public static string DataEditDeleteConfirmTitle => Loc.Text(nameof(DataEditDeleteConfirmTitle));
    public static string DataEditDeleteConfirmMessage => Loc.Text(nameof(DataEditDeleteConfirmMessage));
    public static string DataEditDeleteConfirmYes => Loc.Text(nameof(DataEditDeleteConfirmYes));
    public static string DataEditNoPrimaryKeyHint => Loc.Text(nameof(DataEditNoPrimaryKeyHint));
    public static string DataEditNotConnectedHint => Loc.Text(nameof(DataEditNotConnectedHint));
    // Cell context-menu: set the right-clicked cell to NULL. Enabled only for
    // nullable, non-computed columns; routes through the same UpdateCellAsync
    // path as a manual edit.
    public static string DataEditSetNull => Loc.Text(nameof(DataEditSetNull));

    public static string BlobEditorTitle => Loc.Text(nameof(BlobEditorTitle));
    public static string BlobEditorBinaryPlaceholder => Loc.Text(nameof(BlobEditorBinaryPlaceholder));
    public static string BlobEditorButtonIcon => Loc.Text(nameof(BlobEditorButtonIcon));
    public static string BlobEditorButtonTooltip => Loc.Text(nameof(BlobEditorButtonTooltip));
    public static string BlobEditorOk => Loc.Text(nameof(BlobEditorOk));

    public static string FolderNewTooltip => Loc.Text(nameof(FolderNewTooltip));
    public static string FolderNewIcon => Loc.Text(nameof(FolderNewIcon));
    public static string FolderNodeIcon => Loc.Text(nameof(FolderNodeIcon));
    public static string FolderDialogTitle => Loc.Text(nameof(FolderDialogTitle));
    public static string FolderDialogNameLabel => Loc.Text(nameof(FolderDialogNameLabel));
    public static string FolderDialogCreate => Loc.Text(nameof(FolderDialogCreate));
    public static string FolderContextRename => Loc.Text(nameof(FolderContextRename));
    public static string FolderContextDelete => Loc.Text(nameof(FolderContextDelete));
    public static string FolderDeleteConfirmTitle => Loc.Text(nameof(FolderDeleteConfirmTitle));
    public static string FolderDeleteConfirmFormat => Loc.Text(nameof(FolderDeleteConfirmFormat));
    public static string FolderDeleteConfirmYes => Loc.Text(nameof(FolderDeleteConfirmYes));
    public static string FolderDefaultName => Loc.Text(nameof(FolderDefaultName));

    // Connection deletion — HIGH risk (config + per-connection saved queries +
    // workspace state all gone, irreversible). Message phrased per the user's
    // spec; matches the English of the table-delete + saved-query confirms.
    public static string ConnectionDeleteConfirmTitle => Loc.Text(nameof(ConnectionDeleteConfirmTitle));
    public static string ConnectionDeleteConfirmFormat => Loc.Text(nameof(ConnectionDeleteConfirmFormat));
    public static string ConnectionDeleteConfirmYes => Loc.Text(nameof(ConnectionDeleteConfirmYes));

    // Clear-editor confirmation (only shown when the editor has text to lose).
    public static string ClearEditorConfirmTitle => Loc.Text(nameof(ClearEditorConfirmTitle));
    public static string ClearEditorConfirmMessage => Loc.Text(nameof(ClearEditorConfirmMessage));
    public static string ClearEditorConfirmYes => Loc.Text(nameof(ClearEditorConfirmYes));

    // Closing a New Table tab with unsaved form content.
    public static string NewTableCloseConfirmTitle => Loc.Text(nameof(NewTableCloseConfirmTitle));
    public static string NewTableCloseConfirmFormat => Loc.Text(nameof(NewTableCloseConfirmFormat));
    public static string NewTableCloseConfirmYes => Loc.Text(nameof(NewTableCloseConfirmYes));

    public static string ConnectionContextSort => Loc.Text(nameof(ConnectionContextSort));
    public static string ConnectionContextSortAscending => Loc.Text(nameof(ConnectionContextSortAscending));
    public static string ConnectionContextSortDescending => Loc.Text(nameof(ConnectionContextSortDescending));

    public static string FolderContextAddConnection => Loc.Text(nameof(FolderContextAddConnection));

    public static string QueryContextRename => Loc.Text(nameof(QueryContextRename));
    public static string QueryContextDelete => Loc.Text(nameof(QueryContextDelete));
    public static string QueryRenameIcon => Loc.Text(nameof(QueryRenameIcon));
    public static string QueryRenameTooltip => Loc.Text(nameof(QueryRenameTooltip));

    // ─── Table structure editing (New Table + Pola edit toolbar + AddFieldDialog) ───
    // Two glyphs: ▦ matches the metadata tree's Table icon (see MetadataNodeViewModel
    // IconFor) so the toolbar visually rhymes with the sidebar; ＋ signals "add".
    public static string ToolbarNewTableIcon => Loc.Text(nameof(ToolbarNewTableIcon));
    public static string ToolbarNewTableTooltip => Loc.Text(nameof(ToolbarNewTableTooltip));
    public static string ToolbarToggleFieldEditIcon => Loc.Text(nameof(ToolbarToggleFieldEditIcon));
    public static string ToolbarToggleFieldEditTooltip => Loc.Text(nameof(ToolbarToggleFieldEditTooltip));
    public static string NewTableDialogTitle => Loc.Text(nameof(NewTableDialogTitle));
    public static string NewTableDialogTableNameLabel => Loc.Text(nameof(NewTableDialogTableNameLabel));
    public static string NewTableDialogTableKindLabel => Loc.Text(nameof(NewTableDialogTableKindLabel));
    public static string NewTableKindPersistent => Loc.Text(nameof(NewTableKindPersistent));
    public static string NewTableKindTempDelete => Loc.Text(nameof(NewTableKindTempDelete));
    public static string NewTableKindTempPreserve => Loc.Text(nameof(NewTableKindTempPreserve));
    public static string NewTableTabFields => Loc.Text(nameof(NewTableTabFields));
    public static string NewTableTabDescription => Loc.Text(nameof(NewTableTabDescription));
    public static string NewTableDescriptionLabel => Loc.Text(nameof(NewTableDescriptionLabel));
    public static string NewTableDdlLabel => Loc.Text(nameof(NewTableDdlLabel));
    public static string NewTableDialogCompile => CommandTip.For(CommandId.Compile, Loc.Text(nameof(NewTableDialogCompile)));
    public static string NewTableNamePlaceholder => Loc.Text(nameof(NewTableNamePlaceholder));
    public static string NewTableAddRowTooltip => Loc.Text(nameof(NewTableAddRowTooltip));
    public static string NewTableDeleteRowTooltip => Loc.Text(nameof(NewTableDeleteRowTooltip));
    public static string NewTableMoveUpTooltip => Loc.Text(nameof(NewTableMoveUpTooltip));
    public static string NewTableMoveDownTooltip => Loc.Text(nameof(NewTableMoveDownTooltip));
    public static string NewTableValidationNameRequired => Loc.Text(nameof(NewTableValidationNameRequired));
    public static string NewTableValidationAtLeastOneField => Loc.Text(nameof(NewTableValidationAtLeastOneField));
    public static string NewTableFieldName => Loc.Text(nameof(NewTableFieldName));
    public static string NewTableFieldType => Loc.Text(nameof(NewTableFieldType));
    public static string NewTableFieldSize => Loc.Text(nameof(NewTableFieldSize));
    public static string NewTableFieldScale => Loc.Text(nameof(NewTableFieldScale));
    public static string NewTableFieldNotNull => Loc.Text(nameof(NewTableFieldNotNull));
    public static string NewTableFieldDefault => Loc.Text(nameof(NewTableFieldDefault));
    public static string NewTableFieldPk => Loc.Text(nameof(NewTableFieldPk));
    public static string NewTableFieldAi => Loc.Text(nameof(NewTableFieldAi));
    public static string NewTableFieldDescription => Loc.Text(nameof(NewTableFieldDescription));
    public static string NewTableFieldDomain => Loc.Text(nameof(NewTableFieldDomain));
    public static string NewTableFieldComputed => Loc.Text(nameof(NewTableFieldComputed));
    public static string NewTableFieldCheck => Loc.Text(nameof(NewTableFieldCheck));
    public static string NewTableFieldCharset => Loc.Text(nameof(NewTableFieldCharset));
    public static string NewTableTabDefaultTitle => Loc.Text(nameof(NewTableTabDefaultTitle));
    public static string NewTableExecutedFormat => Loc.Text(nameof(NewTableExecutedFormat));

    // ─── View Detail (View Detail V1) ───────────────────────────────────────
    public static string ViewDetailTabSql => Loc.Text(nameof(ViewDetailTabSql));
    public static string ViewDetailTabFields => Loc.Text(nameof(ViewDetailTabFields));
    public static string ViewDetailTabDependencies => Loc.Text(nameof(ViewDetailTabDependencies));
    public static string ViewDetailTabData => Loc.Text(nameof(ViewDetailTabData));
    public static string ViewDetailTabDescription => Loc.Text(nameof(ViewDetailTabDescription));
    public static string ViewDetailTabDdl => Loc.Text(nameof(ViewDetailTabDdl));
    public static string ViewDetailDescriptionEmpty => Loc.Text(nameof(ViewDetailDescriptionEmpty));
    public static string ViewDetailLoadingHint => Loc.Text(nameof(ViewDetailLoadingHint));
    public static string ToolbarNewViewTooltip => Loc.Text(nameof(ToolbarNewViewTooltip));
    public static string ViewCompileIcon => Loc.Text(nameof(ViewCompileIcon));
    public static string ViewCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(ViewCompileTooltip)));
    public static string ViewCompileFailedFormat => Loc.Text(nameof(ViewCompileFailedFormat));
    public static string NewViewTabDefaultTitle => Loc.Text(nameof(NewViewTabDefaultTitle));
    public static string NewViewExecutedFormat => Loc.Text(nameof(NewViewExecutedFormat));

    // ─── Package Detail ─────────────────────────────────────────────────────
    public static string PackageDetailTabPackage => Loc.Text(nameof(PackageDetailTabPackage));
    public static string PackageDetailTabBody => Loc.Text(nameof(PackageDetailTabBody));
    public static string PackageDetailTabMembers => Loc.Text(nameof(PackageDetailTabMembers));
    public static string PackageDetailTabDependencies => Loc.Text(nameof(PackageDetailTabDependencies));
    public static string PackageDetailTabDescription => Loc.Text(nameof(PackageDetailTabDescription));
    public static string PackageDetailTabDdl => Loc.Text(nameof(PackageDetailTabDdl));
    public static string PackageDetailDescriptionEmpty => Loc.Text(nameof(PackageDetailDescriptionEmpty));
    public static string PackageDetailMembersEmpty => Loc.Text(nameof(PackageDetailMembersEmpty));
    // Members Debug toolbar button (D15.3 Seam E) — an icon-only editor-toolbar action; the tooltip explains
    // availability (the button carries no text label, so there is no button-caption string).
    public static string PackageDebugMemberTooltipReady => Loc.Text(nameof(PackageDebugMemberTooltipReady));
    public static string PackageDebugMemberTooltipNotDebuggable => Loc.Text(nameof(PackageDebugMemberTooltipNotDebuggable));
    public static string PackageDebugMemberTooltipNoSelection => Loc.Text(nameof(PackageDebugMemberTooltipNoSelection));
    public static string PackageDetailLoadingHint => Loc.Text(nameof(PackageDetailLoadingHint));
    public static string PackageDetailDependsOnHeader => Loc.Text(nameof(PackageDetailDependsOnHeader));
    public static string PackageDetailDependedOnByHeader => Loc.Text(nameof(PackageDetailDependedOnByHeader));
    public static string ToolbarNewPackageTooltip => Loc.Text(nameof(ToolbarNewPackageTooltip));
    public static string PackageCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(PackageCompileTooltip)));
    public static string PackageCompileHeaderFailedFormat => Loc.Text(nameof(PackageCompileHeaderFailedFormat));
    public static string PackageCompileBodyFailedFormat => Loc.Text(nameof(PackageCompileBodyFailedFormat));
    public static string NewPackageTabDefaultTitle => Loc.Text(nameof(NewPackageTabDefaultTitle));
    public static string NewPackageExecutedFormat => Loc.Text(nameof(NewPackageExecutedFormat));
    public static string PackageDeleteConfirmTitle => Loc.Text(nameof(PackageDeleteConfirmTitle));
    public static string PackageDeleteConfirmFormat => Loc.Text(nameof(PackageDeleteConfirmFormat));
    public static string PackageDeleteConfirmYes => Loc.Text(nameof(PackageDeleteConfirmYes));

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
    // ⭐⭐ Czasownik usunięcia jest WŁASNOŚCIĄ KOLEKCJI, nie wspólnego formatu — słownik
    // (`docs/design/terminology.md` §1) rozcina ten router w poprzek: to samo polecenie obsługuje pole tabeli
    // (`ALTER TABLE … DROP` → „Drop"), wiersz danych (`DELETE FROM` → „Delete") i pozycję bufora edytora
    // (żadnego DDL → „Remove"). ⚠ Wcześniej wszystkie mówiły „Delete {0}", czyli o polu tabeli twierdziły
    // coś, czego produkt nie robi. Powód i mechanizm: `MainWindowViewModel.CollectionCommands`.
    public static string CollectionVerbDrop => Loc.Text(nameof(CollectionVerbDrop));
    public static string CollectionVerbDelete => Loc.Text(nameof(CollectionVerbDelete));
    public static string CollectionVerbRemove => Loc.Text(nameof(CollectionVerbRemove));
    public static string CollectionRemoveFormat => Loc.Text(nameof(CollectionRemoveFormat));
    public static string CollectionAddFormat => Loc.Text(nameof(CollectionAddFormat));
    public static string CollectionEditFormat => Loc.Text(nameof(CollectionEditFormat));

    public static string CollectionNounField => Loc.Text(nameof(CollectionNounField));
    public static string CollectionNounRow => Loc.Text(nameof(CollectionNounRow));
    public static string CollectionNounColumn => Loc.Text(nameof(CollectionNounColumn));
    public static string CollectionNounVariable => Loc.Text(nameof(CollectionNounVariable));
    // The fallback, and the honest name for the routed collections whose sub-tab decides what the items are
    // (a procedure's arguments / variables / cursors / subprograms all share one command pair).
    public static string CollectionNounItem => Loc.Text(nameof(CollectionNounItem));

    // Menu labels for the collections whose grids are edited in place — a generic noun, but the same verbs.
    // (These are LABELS. They used to be the tooltip constants above, reused as MenuItem headers, which is
    // how "Add item" ended up as a menu entry.)
    public static string CollectionMenuNew => Loc.Text(nameof(CollectionMenuNew));
    public static string CollectionMenuDelete => Loc.Text(nameof(CollectionMenuDelete));
    public static string CollectionMoveUpTooltip => Loc.Text(nameof(CollectionMoveUpTooltip));
    public static string CollectionMoveDownTooltip => Loc.Text(nameof(CollectionMoveDownTooltip));

    public static string ViewModeToggleTooltip => Loc.Text(nameof(ViewModeToggleTooltip));
    public static string ViewParseFailedNotice => Loc.Text(nameof(ViewParseFailedNotice));
    public static string ViewNameHeader => Loc.Text(nameof(ViewNameHeader));
    public static string ViewColumnsHeader => Loc.Text(nameof(ViewColumnsHeader));
    public static string ViewColumnAddTooltip => Loc.Text(nameof(ViewColumnAddTooltip));
    public static string ViewColumnDeleteTooltip => Loc.Text(nameof(ViewColumnDeleteTooltip));
    public static string ViewColumnMoveUpTooltip => Loc.Text(nameof(ViewColumnMoveUpTooltip));
    public static string ViewColumnMoveDownTooltip => Loc.Text(nameof(ViewColumnMoveDownTooltip));
    public static string ViewColumnName => Loc.Text(nameof(ViewColumnName));
    public static string ViewBodyHeader => Loc.Text(nameof(ViewBodyHeader));

    // ─── Generator Detail ──────────────────────────────────────────────────
    public static string GeneratorDetailTabGenerator => Loc.Text(nameof(GeneratorDetailTabGenerator));
    public static string GeneratorDetailTabDependencies => Loc.Text(nameof(GeneratorDetailTabDependencies));
    public static string GeneratorDetailTabDdl => Loc.Text(nameof(GeneratorDetailTabDdl));
    public static string GeneratorNameHeader => Loc.Text(nameof(GeneratorNameHeader));
    public static string GeneratorCurrentValueHeader => Loc.Text(nameof(GeneratorCurrentValueHeader));
    public static string GeneratorInitialValueHeader => Loc.Text(nameof(GeneratorInitialValueHeader));
    public static string GeneratorIncrementHeader => Loc.Text(nameof(GeneratorIncrementHeader));
    public static string GeneratorDescriptionHeader => Loc.Text(nameof(GeneratorDescriptionHeader));
    public static string GeneratorLoadingHint => Loc.Text(nameof(GeneratorLoadingHint));
    public static string GeneratorRefreshCurrentValueTooltip => Loc.Text(nameof(GeneratorRefreshCurrentValueTooltip));
    public static string ToolbarNewGeneratorTooltip => Loc.Text(nameof(ToolbarNewGeneratorTooltip));
    public static string GeneratorCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(GeneratorCompileTooltip)));
    public static string GeneratorCompileFailedFormat => Loc.Text(nameof(GeneratorCompileFailedFormat));
    public static string GeneratorDeleteTooltip => Loc.Text(nameof(GeneratorDeleteTooltip));
    public static string GeneratorDeleteConfirmTitle => Loc.Text(nameof(GeneratorDeleteConfirmTitle));
    public static string GeneratorDeleteConfirmFormat => Loc.Text(nameof(GeneratorDeleteConfirmFormat));
    public static string GeneratorDeleteConfirmYes => Loc.Text(nameof(GeneratorDeleteConfirmYes));
    public static string NewGeneratorTabDefaultTitle => Loc.Text(nameof(NewGeneratorTabDefaultTitle));
    public static string NewGeneratorExecutedFormat => Loc.Text(nameof(NewGeneratorExecutedFormat));

    // ─── Exception Detail ──────────────────────────────────────────────────
    public static string ExceptionDetailTabException => Loc.Text(nameof(ExceptionDetailTabException));
    public static string ExceptionDetailTabDescription => Loc.Text(nameof(ExceptionDetailTabDescription));
    public static string ExceptionDetailTabDependencies => Loc.Text(nameof(ExceptionDetailTabDependencies));
    public static string ExceptionDetailTabDdl => Loc.Text(nameof(ExceptionDetailTabDdl));
    public static string ExceptionNameHeader => Loc.Text(nameof(ExceptionNameHeader));
    public static string ExceptionMessageHeader => Loc.Text(nameof(ExceptionMessageHeader));
    public static string ExceptionDescriptionEditLabel => Loc.Text(nameof(ExceptionDescriptionEditLabel));
    public static string ExceptionLoadingHint => Loc.Text(nameof(ExceptionLoadingHint));
    public static string ToolbarNewExceptionTooltip => Loc.Text(nameof(ToolbarNewExceptionTooltip));
    public static string ExceptionCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(ExceptionCompileTooltip)));
    public static string ExceptionCompileFailedFormat => Loc.Text(nameof(ExceptionCompileFailedFormat));
    public static string ExceptionDeleteTooltip => Loc.Text(nameof(ExceptionDeleteTooltip));
    public static string ExceptionDeleteConfirmTitle => Loc.Text(nameof(ExceptionDeleteConfirmTitle));
    public static string ExceptionDeleteConfirmFormat => Loc.Text(nameof(ExceptionDeleteConfirmFormat));
    public static string ExceptionDeleteConfirmYes => Loc.Text(nameof(ExceptionDeleteConfirmYes));
    public static string NewExceptionTabDefaultTitle => Loc.Text(nameof(NewExceptionTabDefaultTitle));
    public static string NewExceptionExecutedFormat => Loc.Text(nameof(NewExceptionExecutedFormat));

    // ─── Index Detail ──────────────────────────────────────────────────────
    public static string IndexDetailTabIndex => Loc.Text(nameof(IndexDetailTabIndex));
    public static string IndexDetailTabDdl => Loc.Text(nameof(IndexDetailTabDdl));
    public static string IndexNameHeader => Loc.Text(nameof(IndexNameHeader));
    public static string IndexTableHeader => Loc.Text(nameof(IndexTableHeader));
    public static string IndexConstraintTypeHeader => Loc.Text(nameof(IndexConstraintTypeHeader));
    public static string IndexConstraintTypeNoneWatermark => Loc.Text(nameof(IndexConstraintTypeNoneWatermark));
    // Shown when the index backs a constraint, so it's immediately obvious WHY the
    // Active toggle and Drop action are disabled. {0} = PRIMARY KEY / UNIQUE / FOREIGN KEY.
    public static string IndexConstraintBackedNoteFormat => Loc.Text(nameof(IndexConstraintBackedNoteFormat));
    public static string IndexFieldsHeader => Loc.Text(nameof(IndexFieldsHeader));
    public static string IndexUniqueHeader => Loc.Text(nameof(IndexUniqueHeader));
    public static string IndexSortDirectionHeader => Loc.Text(nameof(IndexSortDirectionHeader));
    public static string IndexStatisticsHeader => Loc.Text(nameof(IndexStatisticsHeader));
    public static string IndexStatisticsNoneWatermark => Loc.Text(nameof(IndexStatisticsNoneWatermark));
    public static string IndexActiveHeader => Loc.Text(nameof(IndexActiveHeader));
    public static string IndexDescriptionHeader => Loc.Text(nameof(IndexDescriptionHeader));
    public static string IndexLoadingHint => Loc.Text(nameof(IndexLoadingHint));
    public static string IndexNotFoundFormat => Loc.Text(nameof(IndexNotFoundFormat));
    public static string IndexCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(IndexCompileTooltip)));
    public static string IndexCompileFailedFormat => Loc.Text(nameof(IndexCompileFailedFormat));
    public static string IndexRecomputeStatisticsTooltip => Loc.Text(nameof(IndexRecomputeStatisticsTooltip));
    public static string IndexDeleteTooltip => Loc.Text(nameof(IndexDeleteTooltip));
    public static string IndexDeleteConfirmTitle => Loc.Text(nameof(IndexDeleteConfirmTitle));
    public static string IndexDeleteConfirmFormat => Loc.Text(nameof(IndexDeleteConfirmFormat));
    public static string IndexDeleteConfirmYes => Loc.Text(nameof(IndexDeleteConfirmYes));

    // ─── Domain Detail ───────────────────────────────────────────────────────
    public static string DomainDetailTabDomain => Loc.Text(nameof(DomainDetailTabDomain));
    public static string DomainDetailTabDescription => Loc.Text(nameof(DomainDetailTabDescription));
    public static string DomainDetailTabUsedBy => Loc.Text(nameof(DomainDetailTabUsedBy));
    public static string DomainDetailTabDdl => Loc.Text(nameof(DomainDetailTabDdl));
    public static string DomainNameHeader => Loc.Text(nameof(DomainNameHeader));
    public static string DomainDataTypeHeader => Loc.Text(nameof(DomainDataTypeHeader));
    public static string DomainLengthHeader => Loc.Text(nameof(DomainLengthHeader));
    public static string DomainPrecisionHeader => Loc.Text(nameof(DomainPrecisionHeader));
    public static string DomainScaleHeader => Loc.Text(nameof(DomainScaleHeader));
    public static string DomainSubTypeHeader => Loc.Text(nameof(DomainSubTypeHeader));
    public static string DomainCharacterSetHeader => Loc.Text(nameof(DomainCharacterSetHeader));
    public static string DomainCollationHeader => Loc.Text(nameof(DomainCollationHeader));
    public static string DomainDefaultHeader => Loc.Text(nameof(DomainDefaultHeader));
    public static string DomainCheckHeader => Loc.Text(nameof(DomainCheckHeader));
    public static string DomainNotNullHeader => Loc.Text(nameof(DomainNotNullHeader));
    public static string DomainLoadingHint => Loc.Text(nameof(DomainLoadingHint));
    public static string ToolbarNewDomainTooltip => Loc.Text(nameof(ToolbarNewDomainTooltip));
    public static string DomainCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(DomainCompileTooltip)));
    public static string DomainCompileFailedFormat => Loc.Text(nameof(DomainCompileFailedFormat));
    public static string DomainRenamedFormat => Loc.Text(nameof(DomainRenamedFormat));
    public static string DomainDeleteTooltip => Loc.Text(nameof(DomainDeleteTooltip));
    public static string DomainDeleteConfirmTitle => Loc.Text(nameof(DomainDeleteConfirmTitle));
    public static string DomainDeleteConfirmFormat => Loc.Text(nameof(DomainDeleteConfirmFormat));
    public static string DomainDeleteConfirmYes => Loc.Text(nameof(DomainDeleteConfirmYes));
    public static string NewDomainTabDefaultTitle => Loc.Text(nameof(NewDomainTabDefaultTitle));
    public static string NewDomainExecutedFormat => Loc.Text(nameof(NewDomainExecutedFormat));

    // ─── Procedure Detail (Procedure Detail V1) ─────────────────────────────
    public static string ProcedureNameHeader => Loc.Text(nameof(ProcedureNameHeader));
    public static string ProcedureDetailTabEditor => Loc.Text(nameof(ProcedureDetailTabEditor));
    public static string ProcedureDetailTabDescription => Loc.Text(nameof(ProcedureDetailTabDescription));
    public static string ProcedureDetailTabDependencies => Loc.Text(nameof(ProcedureDetailTabDependencies));
    public static string ProcedureDetailTabDdl => Loc.Text(nameof(ProcedureDetailTabDdl));
    public static string ProcedureDetailParamInputFormat => Loc.Text(nameof(ProcedureDetailParamInputFormat));
    public static string ProcedureDetailParamOutputFormat => Loc.Text(nameof(ProcedureDetailParamOutputFormat));
    public static string ProcedureDetailLoadingHint => Loc.Text(nameof(ProcedureDetailLoadingHint));
    public static string ProcedureCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(ProcedureCompileTooltip)));
    public static string ProcedureCompileFailedFormat => Loc.Text(nameof(ProcedureCompileFailedFormat));
    public static string ToolbarNewProcedureTooltip => Loc.Text(nameof(ToolbarNewProcedureTooltip));
    public static string NewProcedureTabDefaultTitle => Loc.Text(nameof(NewProcedureTabDefaultTitle));
    public static string NewProcedureExecutedFormat => Loc.Text(nameof(NewProcedureExecutedFormat));

    // ─── Procedure Detail V1.1 (modes, locals, execute, comment) ────────────
    public static string ProcedureDetailTabResult => Loc.Text(nameof(ProcedureDetailTabResult));

    // Performance sub-tab in Procedure/Function Detail — hosts the per-tab PerformancePanelView.
    public static string DetailTabPerformance => Loc.Text(nameof(DetailTabPerformance));

    // Heading above the per-table breakdown in the expanded exec-info panel.
    public static string ExecutionSummaryHeader => Loc.Text(nameof(ExecutionSummaryHeader));

    // Clean styled line for a run that changed nothing (read-only) in the expanded exec-info panel
    // — reads/returned rows live in the collapsed header + the Performance tab, not here.
    public static string ExecutionSummaryNoChanges => Loc.Text(nameof(ExecutionSummaryNoChanges));
    public static string ProcedureDetailLocalsVariablesFormat => Loc.Text(nameof(ProcedureDetailLocalsVariablesFormat));
    public static string ProcedureDetailLocalsCursorsFormat => Loc.Text(nameof(ProcedureDetailLocalsCursorsFormat));
    public static string ProcedureDetailLocalsSubprogramsFormat => Loc.Text(nameof(ProcedureDetailLocalsSubprogramsFormat));
    public static string ProcedureDetailLocalsColumnDetail => Loc.Text(nameof(ProcedureDetailLocalsColumnDetail));
    public static string ProcedureParseFailedNotice => Loc.Text(nameof(ProcedureParseFailedNotice));
    public static string ProcedureModeSourceLabel => Loc.Text(nameof(ProcedureModeSourceLabel));
    public static string ProcedureModeEasyLabel => Loc.Text(nameof(ProcedureModeEasyLabel));
    public static string ProcedureModeToggleTooltip => Loc.Text(nameof(ProcedureModeToggleTooltip));
    public static string ProcedureExecuteTooltip => Loc.Text(nameof(ProcedureExecuteTooltip));
    public static string ProcedureCommentTooltip => Loc.Text(nameof(ProcedureCommentTooltip));
    public static string ProcedureUncommentTooltip => Loc.Text(nameof(ProcedureUncommentTooltip));
    public static string ProcedureParamAddTooltip => Loc.Text(nameof(ProcedureParamAddTooltip));
    public static string ProcedureParamDeleteTooltip => Loc.Text(nameof(ProcedureParamDeleteTooltip));
    public static string ProcedureParamMoveUpTooltip => Loc.Text(nameof(ProcedureParamMoveUpTooltip));
    public static string ProcedureParamMoveDownTooltip => Loc.Text(nameof(ProcedureParamMoveDownTooltip));
    public static string ProcedureExecRowsFormat => Loc.Text(nameof(ProcedureExecRowsFormat));
    public static string ProcedureExecCompleted => Loc.Text(nameof(ProcedureExecCompleted));
    // {0} = count, {1} = elapsed ms.
    public static string ProcedureExecInfoRowsFormat => Loc.Text(nameof(ProcedureExecInfoRowsFormat));
    public static string ProcedureExecInfoAffectedFormat => Loc.Text(nameof(ProcedureExecInfoAffectedFormat));
    public static string ProcedureExecInfoCompletedFormat => Loc.Text(nameof(ProcedureExecInfoCompletedFormat));
    public static string ProcedureExecutedViaDataProfile => Loc.Text(nameof(ProcedureExecutedViaDataProfile));
    public static string ProcedureExecEmptyHint => Loc.Text(nameof(ProcedureExecEmptyHint));

    // New-element templates (FB-valid PSQL) used when adding a cursor / subprogram.
    public static string ProcedureSnippetVariable => Loc.Text(nameof(ProcedureSnippetVariable));
    public static string ProcedureSnippetCursor => Loc.Text(nameof(ProcedureSnippetCursor));
    public static string ProcedureSnippetSubprogram => Loc.Text(nameof(ProcedureSnippetSubprogram));
    public static string ProcedureSnippetFunction => Loc.Text(nameof(ProcedureSnippetFunction));
    public static string ProcedureLocalsSourceEmptyHint => Loc.Text(nameof(ProcedureLocalsSourceEmptyHint));

    // ─── Function Detail ────────────────────────────────────────────────────
    // Reuses the Procedure strings for the shared surface (mode toggle, Variables/
    // Cursors/Subprograms headers, Comment/Uncomment, exec-info, snippets, Dependencies
    // / DDL tab labels). Only the function-specific differences live here.
    public static string FunctionDetailArgumentsFormat => Loc.Text(nameof(FunctionDetailArgumentsFormat));
    public static string FunctionDetailTabResult => Loc.Text(nameof(FunctionDetailTabResult));            // Easy-mode return-type metadata
    public static string FunctionDetailExecuteResultTab => Loc.Text(nameof(FunctionDetailExecuteResultTab)); // runtime execution output
    public static string FunctionDetailReturnTypeLabel => Loc.Text(nameof(FunctionDetailReturnTypeLabel));
    public static string FunctionDetailDeterministicLabel => Loc.Text(nameof(FunctionDetailDeterministicLabel));
    public static string FunctionDetailLoadingHint => Loc.Text(nameof(FunctionDetailLoadingHint));
    public static string FunctionCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(FunctionCompileTooltip)));
    public static string FunctionCompileFailedFormat => Loc.Text(nameof(FunctionCompileFailedFormat));
    public static string FunctionExecuteTooltip => Loc.Text(nameof(FunctionExecuteTooltip));
    public static string FunctionExecutedViaDataProfile => Loc.Text(nameof(FunctionExecutedViaDataProfile));
    public static string FunctionResultRequiredNotice => Loc.Text(nameof(FunctionResultRequiredNotice));
    public static string FunctionParseFailedNotice => Loc.Text(nameof(FunctionParseFailedNotice));
    public static string ToolbarNewFunctionTooltip => Loc.Text(nameof(ToolbarNewFunctionTooltip));
    public static string NewFunctionTabDefaultTitle => Loc.Text(nameof(NewFunctionTabDefaultTitle));
    public static string NewFunctionExecutedFormat => Loc.Text(nameof(NewFunctionExecutedFormat));
    public static string FunctionArgumentAddTooltip => Loc.Text(nameof(FunctionArgumentAddTooltip));
    public static string FunctionArgumentDeleteTooltip => Loc.Text(nameof(FunctionArgumentDeleteTooltip));
    public static string FunctionArgumentMoveUpTooltip => Loc.Text(nameof(FunctionArgumentMoveUpTooltip));
    public static string FunctionArgumentMoveDownTooltip => Loc.Text(nameof(FunctionArgumentMoveDownTooltip));

    // New-subprogram kind prompt (Procedure / Function).
    public static string SubprogramKindDialogTitle => Loc.Text(nameof(SubprogramKindDialogTitle));
    public static string SubprogramKindDialogPrompt => Loc.Text(nameof(SubprogramKindDialogPrompt));
    public static string SubprogramKindProcedure => Loc.Text(nameof(SubprogramKindProcedure));
    public static string SubprogramKindFunction => Loc.Text(nameof(SubprogramKindFunction));

    // Merged Domain/Column picker (Faza 4) — one cell replacing the separate Domain +
    // TYPE OF columns. Tab 1 = the domain list; tab 2 = a table→column picker (TYPE OF COLUMN).
    public static string FieldTypeSourceHeader => Loc.Text(nameof(FieldTypeSourceHeader));
    public static string FieldTypeSourceDomainTab => Loc.Text(nameof(FieldTypeSourceDomainTab));
    public static string FieldTypeSourceColumnTab => Loc.Text(nameof(FieldTypeSourceColumnTab));

    // Variable / parameter grid column headers not already present.
    public static string ProcedureFieldTypeOf => Loc.Text(nameof(ProcedureFieldTypeOf));
    public static string ProcedureFieldSubType => Loc.Text(nameof(ProcedureFieldSubType));
    public static string ProcedureFieldCharset => Loc.Text(nameof(ProcedureFieldCharset));
    public static string ProcedureFieldCollate => Loc.Text(nameof(ProcedureFieldCollate));
    public static string ProcedureFieldDescription => Loc.Text(nameof(ProcedureFieldDescription));
    public static string ProcedureCursorScroll => Loc.Text(nameof(ProcedureCursorScroll));

    // Local-element editor toolbars (Variables / Cursors / Subprograms — model-backed).
    public static string ProcedureLocalAddTooltip => Loc.Text(nameof(ProcedureLocalAddTooltip));
    public static string ProcedureLocalDeleteTooltip => Loc.Text(nameof(ProcedureLocalDeleteTooltip));
    public static string ProcedureLocalMoveUpTooltip => Loc.Text(nameof(ProcedureLocalMoveUpTooltip));
    public static string ProcedureLocalMoveDownTooltip => Loc.Text(nameof(ProcedureLocalMoveDownTooltip));

    // Execute Procedure parameter dialog
    // ⚠⚠ NEUTRAL ON PURPOSE (user decision, 2026-08-03). This dialog stopped being only about procedures long
    // ago: Smart SQL Parameters reuses it to collect values for ANY statement carrying `:name` placeholders, so a
    // plain INSERT or UPDATE OR INSERT opened a window headed "Execute Procedure". The user read that as the
    // Execute-Procedure feature misfiring — *"To nie jest wywołanie procedury"* — which is exactly what a
    // mislabelled surface causes: the behaviour was correct and only the label lied.
    // ⛔ Do not narrow it back to a procedure-specific wording; the reuse is the design (one parameter editor, not
    // two), and the dialog is reached from procedure execution AND from F5 on parameterised SQL.
    public static string ProcedureExecuteDialogTitle => Loc.Text(nameof(ProcedureExecuteDialogTitle));
    public static string ProcedureExecuteDialogColumnName => Loc.Text(nameof(ProcedureExecuteDialogColumnName));
    public static string ProcedureExecuteDialogColumnType => Loc.Text(nameof(ProcedureExecuteDialogColumnType));
    public static string ProcedureExecuteDialogColumnValue => Loc.Text(nameof(ProcedureExecuteDialogColumnValue));
    public static string ProcedureExecuteDialogColumnNull => Loc.Text(nameof(ProcedureExecuteDialogColumnNull));
    public static string ProcedureExecuteDialogRun => Loc.Text(nameof(ProcedureExecuteDialogRun));
    public static string ProcedureExecuteDialogCancel => Loc.Text(nameof(ProcedureExecuteDialogCancel));
    public static string ProcedureExecuteDialogHistoryLabel => Loc.Text(nameof(ProcedureExecuteDialogHistoryLabel));
    public static string ProcedureExecuteDialogHistoryEmpty => Loc.Text(nameof(ProcedureExecuteDialogHistoryEmpty));
    public static string ProcedureExecuteDialogTimeWatermark => Loc.Text(nameof(ProcedureExecuteDialogTimeWatermark));
    public static string ProcedureExecuteDialogTimeInvalid => Loc.Text(nameof(ProcedureExecuteDialogTimeInvalid));

    // ─── Trigger Detail ─────────────────────────────────────────────────────
    public static string TriggerNameHeader => Loc.Text(nameof(TriggerNameHeader));
    public static string TriggerTableHeader => Loc.Text(nameof(TriggerTableHeader));
    public static string TriggerTimingHeader => Loc.Text(nameof(TriggerTimingHeader));
    public static string TriggerEventsHeader => Loc.Text(nameof(TriggerEventsHeader));
    public static string TriggerEventInsert => Loc.Text(nameof(TriggerEventInsert));
    public static string TriggerEventUpdate => Loc.Text(nameof(TriggerEventUpdate));
    public static string TriggerEventDelete => Loc.Text(nameof(TriggerEventDelete));
    public static string TriggerPositionHeader => Loc.Text(nameof(TriggerPositionHeader));
    public static string TriggerActive => Loc.Text(nameof(TriggerActive));
    public static string TriggerDetailLoadingHint => Loc.Text(nameof(TriggerDetailLoadingHint));
    public static string TriggerCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(TriggerCompileTooltip)));
    public static string TriggerCompileFailedFormat => Loc.Text(nameof(TriggerCompileFailedFormat));
    public static string TriggerModeToggleTooltip => Loc.Text(nameof(TriggerModeToggleTooltip));
    public static string TriggerParseFailedNotice => Loc.Text(nameof(TriggerParseFailedNotice));
    public static string TriggerTableRequiredNotice => Loc.Text(nameof(TriggerTableRequiredNotice));
    public static string TriggerEventRequiredNotice => Loc.Text(nameof(TriggerEventRequiredNotice));
    public static string ToolbarNewTriggerTooltip => Loc.Text(nameof(ToolbarNewTriggerTooltip));
    public static string NewTriggerTabDefaultTitle => Loc.Text(nameof(NewTriggerTabDefaultTitle));
    public static string NewTriggerExecutedFormat => Loc.Text(nameof(NewTriggerExecutedFormat));

    /// <summary>
    /// The three facts a user must be told after compiling a renamed object, because Firebird has no rename
    /// for procedures / functions / triggers (measured on FB 5.0: <c>ALTER PROCEDURE … TO …</c> is
    /// <c>-104 Token unknown</c>). ⚠ The third sentence is the one that matters: an object they did not expect
    /// is now in their database, and nothing else will tell them.
    /// </summary>
    public static string ObjectRenameNotSupportedTitle => Loc.Text(nameof(ObjectRenameNotSupportedTitle));

    /// <summary>The acknowledge button of a dialog that reports rather than asks.</summary>
    public static string DialogOk => Loc.Text(nameof(DialogOk));

    public static string ObjectRenameNotSupportedFormat => Loc.Text(nameof(ObjectRenameNotSupportedFormat));

    public static string FieldEditCompileIcon => Loc.Text(nameof(FieldEditCompileIcon));
    public static string FieldEditCompileTooltip => CommandTip.For(
        CommandId.Compile, Loc.Text(nameof(FieldEditCompileTooltip)));
    public static string FieldEditDiscardTooltip => Loc.Text(nameof(FieldEditDiscardTooltip));
    // Confirmation before discarding the table designer's buffered structural changes —
    // an accidental click must not silently throw away uncompiled work.
    public static string FieldEditDiscardConfirmTitle => Loc.Text(nameof(FieldEditDiscardConfirmTitle));
    public static string FieldEditDiscardConfirmFormat => Loc.Text(nameof(FieldEditDiscardConfirmFormat));
    public static string FieldEditDiscardConfirmYes => Loc.Text(nameof(FieldEditDiscardConfirmYes));

    // ─── Revert (View / Procedure / Trigger source editors) ─────────────────
    // The source-editor analog of the table designer's "discard pending changes":
    // reload the object from the database, throwing away uncompiled edits. The
    // confirmation guards against an accidental click losing work. Every object
    // editor must expose this button — see the editor contract in CLAUDE.md.
    public static string RevertChangesTooltip => Loc.Text(nameof(RevertChangesTooltip));
    public static string RevertChangesConfirmTitle => Loc.Text(nameof(RevertChangesConfirmTitle));
    public static string RevertChangesConfirmFormat => Loc.Text(nameof(RevertChangesConfirmFormat));
    public static string RevertChangesConfirmYes => Loc.Text(nameof(RevertChangesConfirmYes));
    public static string FieldEditAddIcon => Loc.Text(nameof(FieldEditAddIcon));
    public static string FieldEditAddTooltip => Loc.Text(nameof(FieldEditAddTooltip));
    public static string FieldEditDropIcon => Loc.Text(nameof(FieldEditDropIcon));
    public static string FieldEditDropTooltip => Loc.Text(nameof(FieldEditDropTooltip));
    public static string FieldEditMoveUpIcon => Loc.Text(nameof(FieldEditMoveUpIcon));
    public static string FieldEditMoveUpTooltip => Loc.Text(nameof(FieldEditMoveUpTooltip));
    public static string FieldEditMoveDownIcon => Loc.Text(nameof(FieldEditMoveDownIcon));
    public static string FieldEditMoveDownTooltip => Loc.Text(nameof(FieldEditMoveDownTooltip));
    public static string FieldEditDropConfirmTitle => Loc.Text(nameof(FieldEditDropConfirmTitle));
    public static string FieldEditDropConfirmFormat => Loc.Text(nameof(FieldEditDropConfirmFormat));
    public static string FieldEditDropConfirmYes => Loc.Text(nameof(FieldEditDropConfirmYes));
    public static string FieldEditPendingHeader => Loc.Text(nameof(FieldEditPendingHeader));
    public static string FieldEditCompileFailedFormat => Loc.Text(nameof(FieldEditCompileFailedFormat));
    public static string FieldEditDescriptionAddFormat => Loc.Text(nameof(FieldEditDescriptionAddFormat));
    public static string FieldEditDescriptionDropFormat => Loc.Text(nameof(FieldEditDescriptionDropFormat));
    public static string FieldEditDescriptionMoveFormat => Loc.Text(nameof(FieldEditDescriptionMoveFormat));
    public static string FieldEditDescriptionRenameFormat => Loc.Text(nameof(FieldEditDescriptionRenameFormat));
    public static string FieldEditDescriptionSetNotNullFormat => Loc.Text(nameof(FieldEditDescriptionSetNotNullFormat));
    public static string FieldEditDescriptionDropNotNullFormat => Loc.Text(nameof(FieldEditDescriptionDropNotNullFormat));
    public static string FieldEditDescriptionSetDefaultFormat => Loc.Text(nameof(FieldEditDescriptionSetDefaultFormat));
    public static string FieldEditDescriptionDropDefaultFormat => Loc.Text(nameof(FieldEditDescriptionDropDefaultFormat));
    public static string FieldEditDescriptionAlterTypeFormat => Loc.Text(nameof(FieldEditDescriptionAlterTypeFormat));
    public static string FieldEditDescriptionCommentFormat => Loc.Text(nameof(FieldEditDescriptionCommentFormat));
    public static string FieldEditRenameBlockedFormat => Loc.Text(nameof(FieldEditRenameBlockedFormat));

    public static string AddFieldDialogTitle => Loc.Text(nameof(AddFieldDialogTitle));
    public static string AddFieldDialogEditTitleFormat => Loc.Text(nameof(AddFieldDialogEditTitleFormat));
    public static string AddFieldRenameBlockedHint => Loc.Text(nameof(AddFieldRenameBlockedHint));

    // Pola context menu + shortcuts
    public static string FieldsContextMenuAdd => Loc.Text(nameof(FieldsContextMenuAdd));
    public static string FieldsContextMenuEdit => Loc.Text(nameof(FieldsContextMenuEdit));
    public static string FieldsContextMenuDrop => Loc.Text(nameof(FieldsContextMenuDrop));
    public static string FieldsContextMenuCreateForeignKey => Loc.Text(nameof(FieldsContextMenuCreateForeignKey));
    public static string FieldEditEditIcon => Loc.Text(nameof(FieldEditEditIcon));
    // (FieldEditEditTooltip — "Edit selected field · F2" — was removed in the UX Consistency Pass. It had no
    // consumer: the toolbar's Edit button it was written for never existed, which is exactly the gap the pass
    // closed. The button now uses MainWindowViewModel.CollectionEditTooltip, which names the active
    // collection's noun and takes its gesture from the catalog.)
    public static string FieldEditForeignKeyIcon => Loc.Text(nameof(FieldEditForeignKeyIcon));
    public static string FieldEditForeignKeyTooltip => Loc.Text(nameof(FieldEditForeignKeyTooltip));

    // Field dependencies panel (Pola sub-tab, Session 4)
    public static string FieldDependenciesHeader => Loc.Text(nameof(FieldDependenciesHeader));
    public static string FieldDependenciesNoSelection => Loc.Text(nameof(FieldDependenciesNoSelection));
    public static string FieldDependenciesEmpty => Loc.Text(nameof(FieldDependenciesEmpty));
    public static string FieldDependenciesColumnType => Loc.Text(nameof(FieldDependenciesColumnType));
    public static string FieldDependenciesColumnName => Loc.Text(nameof(FieldDependenciesColumnName));
    public static string FieldDependenciesColumnInsert => Loc.Text(nameof(FieldDependenciesColumnInsert));
    public static string FieldDependenciesColumnUpdate => Loc.Text(nameof(FieldDependenciesColumnUpdate));

    // Foreign Key wizard (Session 3 full implementation)
    public static string ForeignKeyDialogTitle => Loc.Text(nameof(ForeignKeyDialogTitle));
    public static string ForeignKeyDialogHeader => Loc.Text(nameof(ForeignKeyDialogHeader));
    public static string ForeignKeyDialogClose => Loc.Text(nameof(ForeignKeyDialogClose));
    public static string ForeignKeyDialogCreate => Loc.Text(nameof(ForeignKeyDialogCreate));
    public static string ForeignKeyConstraintNameLabel => Loc.Text(nameof(ForeignKeyConstraintNameLabel));
    public static string ForeignKeySourceTableLabel => Loc.Text(nameof(ForeignKeySourceTableLabel));
    public static string ForeignKeySourceFieldsLabel => Loc.Text(nameof(ForeignKeySourceFieldsLabel));
    public static string ForeignKeyReferencedTableLabel => Loc.Text(nameof(ForeignKeyReferencedTableLabel));
    public static string ForeignKeyReferencedFieldsLabel => Loc.Text(nameof(ForeignKeyReferencedFieldsLabel));
    public static string ForeignKeyReferencedFieldsHint => Loc.Text(nameof(ForeignKeyReferencedFieldsHint));
    public static string ForeignKeyOnUpdateLabel => Loc.Text(nameof(ForeignKeyOnUpdateLabel));
    public static string ForeignKeyOnDeleteLabel => Loc.Text(nameof(ForeignKeyOnDeleteLabel));
    public static string ForeignKeyActionNoAction => Loc.Text(nameof(ForeignKeyActionNoAction));
    public static string ForeignKeyActionCascade => Loc.Text(nameof(ForeignKeyActionCascade));
    public static string ForeignKeyActionSetNull => Loc.Text(nameof(ForeignKeyActionSetNull));
    public static string ForeignKeyDdlPreviewLabel => Loc.Text(nameof(ForeignKeyDdlPreviewLabel));
    public static string ForeignKeyDdlPreviewIncomplete => Loc.Text(nameof(ForeignKeyDdlPreviewIncomplete));
    public static string ForeignKeyValidationConstraintNameRequired => Loc.Text(nameof(ForeignKeyValidationConstraintNameRequired));
    public static string ForeignKeyValidationReferencedTableRequired => Loc.Text(nameof(ForeignKeyValidationReferencedTableRequired));
    public static string ForeignKeyValidationLocalFieldsRequired => Loc.Text(nameof(ForeignKeyValidationLocalFieldsRequired));
    public static string ForeignKeyValidationReferencedFieldsRequired => Loc.Text(nameof(ForeignKeyValidationReferencedFieldsRequired));
    public static string ForeignKeyValidationFieldCountMismatch => Loc.Text(nameof(ForeignKeyValidationFieldCountMismatch));
    public static string ForeignKeyExecuteFailedFormat => Loc.Text(nameof(ForeignKeyExecuteFailedFormat));

    // ─── Constraint management (Constraint Management Sprint V1) ──────────
    // Shared dialog chrome
    public static string ConstraintNameLabel => Loc.Text(nameof(ConstraintNameLabel));
    public static string ConstraintFieldsLabel => Loc.Text(nameof(ConstraintFieldsLabel));
    public static string ConstraintDialogCreate => Loc.Text(nameof(ConstraintDialogCreate));
    public static string ConstraintDdlPreviewLabel => Loc.Text(nameof(ConstraintDdlPreviewLabel));
    public static string ConstraintDdlPreviewIncomplete => Loc.Text(nameof(ConstraintDdlPreviewIncomplete));
    public static string ConstraintValidationNameRequired => Loc.Text(nameof(ConstraintValidationNameRequired));
    public static string ConstraintValidationFieldsRequired => Loc.Text(nameof(ConstraintValidationFieldsRequired));
    public static string ConstraintExecuteFailedFormat => Loc.Text(nameof(ConstraintExecuteFailedFormat));
    // Primary Key / Unique field-picker dialog
    public static string PrimaryKeyDialogTitle => Loc.Text(nameof(PrimaryKeyDialogTitle));
    public static string PrimaryKeyDialogHeader => Loc.Text(nameof(PrimaryKeyDialogHeader));
    public static string UniqueDialogTitle => Loc.Text(nameof(UniqueDialogTitle));
    public static string UniqueDialogHeader => Loc.Text(nameof(UniqueDialogHeader));
    // Check dialog
    public static string CheckConstraintDialogTitle => Loc.Text(nameof(CheckConstraintDialogTitle));
    public static string CheckConstraintDialogHeader => Loc.Text(nameof(CheckConstraintDialogHeader));
    public static string CheckConstraintExpressionLabel => Loc.Text(nameof(CheckConstraintExpressionLabel));
    public static string CheckConstraintExpressionWatermark => Loc.Text(nameof(CheckConstraintExpressionWatermark));
    public static string CheckConstraintValidationExpressionRequired => Loc.Text(nameof(CheckConstraintValidationExpressionRequired));
    // Context-menu actions
    public static string ConstraintMenuAddPrimaryKey => Loc.Text(nameof(ConstraintMenuAddPrimaryKey));
    public static string ConstraintMenuDropPrimaryKey => Loc.Text(nameof(ConstraintMenuDropPrimaryKey));
    public static string ConstraintMenuAddForeignKey => Loc.Text(nameof(ConstraintMenuAddForeignKey));
    public static string ConstraintMenuDropForeignKey => Loc.Text(nameof(ConstraintMenuDropForeignKey));
    public static string ConstraintMenuAddCheck => Loc.Text(nameof(ConstraintMenuAddCheck));
    public static string ConstraintMenuDropCheck => Loc.Text(nameof(ConstraintMenuDropCheck));
    public static string ConstraintMenuAddUnique => Loc.Text(nameof(ConstraintMenuAddUnique));
    public static string ConstraintMenuDropUnique => Loc.Text(nameof(ConstraintMenuDropUnique));
    // Drop confirmation
    public static string ConstraintDropConfirmTitle => Loc.Text(nameof(ConstraintDropConfirmTitle));
    public static string ConstraintDropConfirmFormat => Loc.Text(nameof(ConstraintDropConfirmFormat));
    public static string ConstraintDropConfirmYes => Loc.Text(nameof(ConstraintDropConfirmYes));

    // Optional USING [ASC|DESC] INDEX clause for PK / UNIQUE (Constraint config).
    public static string ConstraintIndexNameLabel => Loc.Text(nameof(ConstraintIndexNameLabel));
    public static string ConstraintDescendingLabel => Loc.Text(nameof(ConstraintDescendingLabel));

    // Pola sub-tab: Drop Foreign Key context-menu entry (routes through the
    // shared Drop Constraint path; the FK constraint is resolved from the
    // selected field).
    public static string FieldsContextMenuDropForeignKey => Loc.Text(nameof(FieldsContextMenuDropForeignKey));

    // ─── Index Management V1 ──────────────────────────────────────────────
    public static string IndexDialogTitle => Loc.Text(nameof(IndexDialogTitle));
    public static string IndexDialogHeader => Loc.Text(nameof(IndexDialogHeader));
    public static string IndexDialogCreate => Loc.Text(nameof(IndexDialogCreate));
    public static string IndexNameLabel => Loc.Text(nameof(IndexNameLabel));
    public static string IndexFieldsLabel => Loc.Text(nameof(IndexFieldsLabel));
    public static string IndexUniqueLabel => Loc.Text(nameof(IndexUniqueLabel));
    public static string IndexDescendingLabel => Loc.Text(nameof(IndexDescendingLabel));
    public static string IndexComputedLabel => Loc.Text(nameof(IndexComputedLabel));
    public static string IndexDdlPreviewLabel => Loc.Text(nameof(IndexDdlPreviewLabel));
    public static string IndexDdlPreviewIncomplete => Loc.Text(nameof(IndexDdlPreviewIncomplete));
    public static string IndexValidationNameRequired => Loc.Text(nameof(IndexValidationNameRequired));
    public static string IndexValidationFieldsRequired => Loc.Text(nameof(IndexValidationFieldsRequired));
    public static string IndexMenuAdd => Loc.Text(nameof(IndexMenuAdd));
    public static string IndexMenuDrop => Loc.Text(nameof(IndexMenuDrop));
    public static string IndexDropConfirmTitle => Loc.Text(nameof(IndexDropConfirmTitle));
    public static string IndexDropConfirmFormat => Loc.Text(nameof(IndexDropConfirmFormat));
    public static string IndexDropConfirmYes => Loc.Text(nameof(IndexDropConfirmYes));
    public static string IndexExecuteFailedFormat => Loc.Text(nameof(IndexExecuteFailedFormat));
    // Shown when the user tries to drop an index that backs a PK / FK / UNIQUE
    // constraint — those are managed via the Ograniczenia tab.
    public static string IndexConstraintBackedFormat => Loc.Text(nameof(IndexConstraintBackedFormat));
    // SET STATISTICS INDEX — recompute index selectivity (single + all).
    public static string IndexMenuRecomputeStatistics => Loc.Text(nameof(IndexMenuRecomputeStatistics));
    public static string IndexMenuRecomputeAllStatistics => Loc.Text(nameof(IndexMenuRecomputeAllStatistics));
    public static string IndexStatsRecomputedOneFormat => Loc.Text(nameof(IndexStatsRecomputedOneFormat));
    public static string IndexStatsRecomputedAllFormat => Loc.Text(nameof(IndexStatsRecomputedAllFormat));
    public static string IndexStatsRecomputeFailedFormat => Loc.Text(nameof(IndexStatsRecomputeFailedFormat));

    // ─── Table description editing (Opis tab) ─────────────────────────────
    public static string TableDescriptionEditLabel => Loc.Text(nameof(TableDescriptionEditLabel));
    // Per-object description-tab mini-headers — each editor names its own object type
    // (the shared TableDescriptionEditLabel said "Table description" everywhere, which
    // was wrong for the View/Procedure/Function/Trigger/Package editors).
    public static string ViewDescriptionEditLabel => Loc.Text(nameof(ViewDescriptionEditLabel));
    public static string ProcedureDescriptionEditLabel => Loc.Text(nameof(ProcedureDescriptionEditLabel));
    public static string FunctionDescriptionEditLabel => Loc.Text(nameof(FunctionDescriptionEditLabel));
    public static string TriggerDescriptionEditLabel => Loc.Text(nameof(TriggerDescriptionEditLabel));
    public static string PackageDescriptionEditLabel => Loc.Text(nameof(PackageDescriptionEditLabel));
    public static string TableDescriptionSaveIcon => Loc.Text(nameof(TableDescriptionSaveIcon));
    public static string TableDescriptionSave => Loc.Text(nameof(TableDescriptionSave));
    public static string TableDescriptionClear => Loc.Text(nameof(TableDescriptionClear));
    public static string TableDescriptionSaveFailedFormat => Loc.Text(nameof(TableDescriptionSaveFailedFormat));

    public static string AddFieldFieldName => Loc.Text(nameof(AddFieldFieldName));
    public static string AddFieldNotNull => Loc.Text(nameof(AddFieldNotNull));
    public static string AddFieldPrimaryKey => Loc.Text(nameof(AddFieldPrimaryKey));
    public static string AddFieldTabDomain => Loc.Text(nameof(AddFieldTabDomain));
    public static string AddFieldTabBasicType => Loc.Text(nameof(AddFieldTabBasicType));
    public static string AddFieldTabDefault => Loc.Text(nameof(AddFieldTabDefault));
    public static string AddFieldTabCheck => Loc.Text(nameof(AddFieldTabCheck));
    public static string AddFieldTabComputed => Loc.Text(nameof(AddFieldTabComputed));
    public static string AddFieldTabAutoinc => Loc.Text(nameof(AddFieldTabAutoinc));
    public static string AddFieldTabDescription => Loc.Text(nameof(AddFieldTabDescription));
    public static string AddFieldTabDdl => Loc.Text(nameof(AddFieldTabDdl));
    public static string AddFieldDomainLabel => Loc.Text(nameof(AddFieldDomainLabel));
    public static string AddFieldDomainHint => Loc.Text(nameof(AddFieldDomainHint));
    public static string AddFieldClearDomain => Loc.Text(nameof(AddFieldClearDomain));
    // Sentinel label shown at the top of inline Domain combos so the user can
    // clear a previously-picked domain back to a basic type (#5). Real Firebird
    // domains can't be named with parentheses, so this never collides.
    public static string DomainNoneOption => Loc.Text(nameof(DomainNoneOption));
    public static string AddFieldBasicTypeLabel => Loc.Text(nameof(AddFieldBasicTypeLabel));
    public static string AddFieldSizeLabel => Loc.Text(nameof(AddFieldSizeLabel));
    public static string AddFieldPrecisionLabel => Loc.Text(nameof(AddFieldPrecisionLabel));
    public static string AddFieldScaleLabel => Loc.Text(nameof(AddFieldScaleLabel));
    public static string AddFieldBlobSubTypeLabel => Loc.Text(nameof(AddFieldBlobSubTypeLabel));
    public static string AddFieldDefaultLabel => Loc.Text(nameof(AddFieldDefaultLabel));
    public static string AddFieldCheckLabel => Loc.Text(nameof(AddFieldCheckLabel));
    public static string AddFieldComputedLabel => Loc.Text(nameof(AddFieldComputedLabel));
    public static string AddFieldAutoincNone => Loc.Text(nameof(AddFieldAutoincNone));
    public static string AddFieldAutoincIdentity => Loc.Text(nameof(AddFieldAutoincIdentity));
    public static string AddFieldAutoincExisting => Loc.Text(nameof(AddFieldAutoincExisting));
    public static string AddFieldAutoincNew => Loc.Text(nameof(AddFieldAutoincNew));
    public static string AddFieldGeneratorNameLabel => Loc.Text(nameof(AddFieldGeneratorNameLabel));
    public static string AddFieldTriggerNameLabel => Loc.Text(nameof(AddFieldTriggerNameLabel));
    public static string AddFieldDescriptionLabel => Loc.Text(nameof(AddFieldDescriptionLabel));
    public static string AddFieldDialogOk => Loc.Text(nameof(AddFieldDialogOk));
    public static string AddFieldValidationNameRequired => Loc.Text(nameof(AddFieldValidationNameRequired));

    // ─── Security Manager ──────────────────────────────────────────────────
    public static string SecurityManagerTabTitle => Loc.Text(nameof(SecurityManagerTabTitle));
    public static string SecurityManagerTabTitleFormat => Loc.Text(nameof(SecurityManagerTabTitleFormat));
    public static string SecurityTabUsers => Loc.Text(nameof(SecurityTabUsers));
    public static string SecurityTabRoles => Loc.Text(nameof(SecurityTabRoles));
    public static string SecurityTabMembership => Loc.Text(nameof(SecurityTabMembership));
    public static string SecurityTabPrivileges => Loc.Text(nameof(SecurityTabPrivileges));
    public static string SecurityGranteeUser => Loc.Text(nameof(SecurityGranteeUser));
    public static string SecurityGranteeRole => Loc.Text(nameof(SecurityGranteeRole));

    // Common toolbar
    public static string SecurityAdd => Loc.Text(nameof(SecurityAdd));
    public static string SecurityEdit => Loc.Text(nameof(SecurityEdit));
    public static string SecurityDelete => Loc.Text(nameof(SecurityDelete));
    public static string SecurityRefresh => Loc.Text(nameof(SecurityRefresh));
    public static string SecurityDeleteConfirm => Loc.Text(nameof(SecurityDeleteConfirm));

    // Users pane
    public static string SecurityUsersHeader => Loc.Text(nameof(SecurityUsersHeader));
    public static string SecurityUsersHint => Loc.Text(nameof(SecurityUsersHint));
    public static string SecurityColUserName => Loc.Text(nameof(SecurityColUserName));
    public static string SecurityColFirstName => Loc.Text(nameof(SecurityColFirstName));
    public static string SecurityColMiddleName => Loc.Text(nameof(SecurityColMiddleName));
    public static string SecurityColLastName => Loc.Text(nameof(SecurityColLastName));
    public static string SecurityColActive => Loc.Text(nameof(SecurityColActive));
    public static string SecurityColAdmin => Loc.Text(nameof(SecurityColAdmin));
    public static string SecurityColDescription => Loc.Text(nameof(SecurityColDescription));
    public static string SecurityColPlugin => Loc.Text(nameof(SecurityColPlugin));
    public static string SecurityAddUser => Loc.Text(nameof(SecurityAddUser));
    public static string SecurityEditUser => Loc.Text(nameof(SecurityEditUser));
    public static string SecurityDeleteUser => Loc.Text(nameof(SecurityDeleteUser));
    public static string SecurityDeleteUserTitle => Loc.Text(nameof(SecurityDeleteUserTitle));
    public static string SecurityDeleteUserMessage => Loc.Text(nameof(SecurityDeleteUserMessage));

    // Roles pane
    public static string SecurityRolesHeader => Loc.Text(nameof(SecurityRolesHeader));
    public static string SecurityColRoleName => Loc.Text(nameof(SecurityColRoleName));
    public static string SecurityColOwner => Loc.Text(nameof(SecurityColOwner));
    public static string SecurityAddRole => Loc.Text(nameof(SecurityAddRole));
    public static string SecurityDropRole => Loc.Text(nameof(SecurityDropRole));
    public static string SecurityDropRoleTitle => Loc.Text(nameof(SecurityDropRoleTitle));
    public static string SecurityDropRoleMessage => Loc.Text(nameof(SecurityDropRoleMessage));
    public static string SecurityRoleDescriptionLabel => Loc.Text(nameof(SecurityRoleDescriptionLabel));
    public static string SecuritySaveDescription => Loc.Text(nameof(SecuritySaveDescription));

    // Membership pane
    public static string SecurityMembershipHeader => Loc.Text(nameof(SecurityMembershipHeader));
    public static string SecurityGranteeLabel => Loc.Text(nameof(SecurityGranteeLabel));
    public static string SecurityColMember => Loc.Text(nameof(SecurityColMember));
    public static string SecurityColAdminOption => Loc.Text(nameof(SecurityColAdminOption));
    public static string SecurityMembershipHint => Loc.Text(nameof(SecurityMembershipHint));
    // Membership direction switch (feature A) + tri-state cell (feature B)
    public static string SecurityDirectionLabel => Loc.Text(nameof(SecurityDirectionLabel));
    public static string SecurityDirectionMemberOf => Loc.Text(nameof(SecurityDirectionMemberOf));
    public static string SecurityDirectionMembers => Loc.Text(nameof(SecurityDirectionMembers));
    public static string SecurityRolePickerLabel => Loc.Text(nameof(SecurityRolePickerLabel));
    public static string SecurityColMembership => Loc.Text(nameof(SecurityColMembership));
    public static string SecurityColMemberName => Loc.Text(nameof(SecurityColMemberName));
    public static string SecurityMembershipLegend => Loc.Text(nameof(SecurityMembershipLegend));

    // ── Stany puste Security Managera (M5 / M‑3, B2 + B3) ────────────────────────────────────────
    //
    // ⚠ „Brak ról" to stan ZWYCZAJNY, nie awaryjny: `RDB$ROLES` po odfiltrowaniu systemowych jest pusta
    //    na świeżo utworzonej bazie. ⭐ Treść wskazuje następny krok, bo przycisk „Add role" stoi tuż nad
    //    siatką i MA WIDOCZNĄ ETYKIETĘ — inaczej niż „+" w pasku tytułu, przez które stan pusty paska
    //    bocznego musiał pokazać glif.
    // ⛔ Nazwa akcji jest SKŁADANA ze stałej przycisku, nie przepisana: przepisany napis rozjeżdża się po
    //    cichu przy pierwszej zmianie etykiety (ta sama lekcja co `CommandTip`, gotcha #284).
    // ⛔⛔ NIE WOLNO tu użyć treści mówiącej o filtrze — zmierzone: `FilterText` istnieje wyłącznie
    //    w panelu uprawnień, a listy użytkowników i ról filtra NIE MAJĄ.
    // ⚠ A FORMAT key, not a sentence with a hole punched in it: the referenced label is itself localized,
    // and a translation must be free to place it elsewhere in the sentence.
    public static string SecurityRolesEmpty => string.Format(
        System.Globalization.CultureInfo.CurrentCulture, SecurityRolesEmptyFormat, SecurityAddRole);

    // ⭐ DWA komunikaty, bo selektor kierunku zadaje DWA różne pytania — i produkt już to wie: nagłówek
    //    kolumny przełącza się „Role name" ↔ „Member name" dokładnie z tego powodu. Jeden komunikat na oba
    //    kierunki byłby nieprawdziwy w jednym z nich.
    public static string SecurityMembershipEmptyMemberOf => Loc.Text(nameof(SecurityMembershipEmptyMemberOf));
    public static string SecurityMembershipEmptyMembers => Loc.Text(nameof(SecurityMembershipEmptyMembers));

    // Privileges pane
    public static string SecurityPrivilegesHeader => Loc.Text(nameof(SecurityPrivilegesHeader));
    public static string SecurityCategoryLabel => Loc.Text(nameof(SecurityCategoryLabel));
    public static string SecurityFilterWatermark => Loc.Text(nameof(SecurityFilterWatermark));
    public static string SecurityWithGrantOption => Loc.Text(nameof(SecurityWithGrantOption));
    public static string SecurityColObject => Loc.Text(nameof(SecurityColObject));
    public static string SecurityColAll => Loc.Text(nameof(SecurityColAll));
    public static string SecurityColSelect => Loc.Text(nameof(SecurityColSelect));
    public static string SecurityColInsert => Loc.Text(nameof(SecurityColInsert));
    public static string SecurityColUpdate => Loc.Text(nameof(SecurityColUpdate));
    public static string SecurityColDelete => Loc.Text(nameof(SecurityColDelete));
    public static string SecurityColReferences => Loc.Text(nameof(SecurityColReferences));
    public static string SecurityColExecute => Loc.Text(nameof(SecurityColExecute));
    public static string SecurityColUsage => Loc.Text(nameof(SecurityColUsage));
    // Per-column header trio (grant / grant + option / revoke this privilege for all visible rows).
    public static string SecurityColGrantTip => Loc.Text(nameof(SecurityColGrantTip));
    public static string SecurityColGrantOptionTip => Loc.Text(nameof(SecurityColGrantOptionTip));
    public static string SecurityColRevokeTip => Loc.Text(nameof(SecurityColRevokeTip));
    public static string SecurityColumnsHeader => Loc.Text(nameof(SecurityColumnsHeader));
    public static string SecurityColumnsForFormat => Loc.Text(nameof(SecurityColumnsForFormat));
    public static string SecurityColColumn => Loc.Text(nameof(SecurityColColumn));
    public static string SecurityColumnHint => Loc.Text(nameof(SecurityColumnHint));

    // Privileges — bulk operations (row / column / all visible) + tri-state legend.
    // The same three glyphs appear at every scope: ✓ grant, ✓+ grant with grant option, ✕ revoke.
    public static string SecurityGrantGlyph => Loc.Text(nameof(SecurityGrantGlyph));
    public static string SecurityGrantOptionGlyph => Loc.Text(nameof(SecurityGrantOptionGlyph));
    public static string SecurityRevokeGlyph => Loc.Text(nameof(SecurityRevokeGlyph));
    public static string SecurityPrivilegeLegend => Loc.Text(nameof(SecurityPrivilegeLegend));
    // All-visible toolbar
    public static string SecurityBulkAllLabel => Loc.Text(nameof(SecurityBulkAllLabel));
    public static string SecurityBulkGrantAll => Loc.Text(nameof(SecurityBulkGrantAll));
    public static string SecurityBulkGrantAllOption => Loc.Text(nameof(SecurityBulkGrantAllOption));
    public static string SecurityBulkRevokeAll => Loc.Text(nameof(SecurityBulkRevokeAll));
    public static string SecurityBulkGrantAllTip => Loc.Text(nameof(SecurityBulkGrantAllTip));
    public static string SecurityBulkGrantAllOptionTip => Loc.Text(nameof(SecurityBulkGrantAllOptionTip));
    public static string SecurityBulkRevokeAllTip => Loc.Text(nameof(SecurityBulkRevokeAllTip));
    // Row scope (hover trio + right-click menu)
    public static string SecurityRowGrantAll => Loc.Text(nameof(SecurityRowGrantAll));
    public static string SecurityRowGrantAllOption => Loc.Text(nameof(SecurityRowGrantAllOption));
    public static string SecurityRowRevokeAll => Loc.Text(nameof(SecurityRowRevokeAll));
    public static string SecurityRowGrantTip => Loc.Text(nameof(SecurityRowGrantTip));
    public static string SecurityRowGrantOptionTip => Loc.Text(nameof(SecurityRowGrantOptionTip));
    public static string SecurityRowRevokeTip => Loc.Text(nameof(SecurityRowRevokeTip));
    // Confirmation for the broadest destructive op
    public static string SecurityRevokeAllConfirmTitle => Loc.Text(nameof(SecurityRevokeAllConfirmTitle));
    public static string SecurityRevokeAllConfirmFormat => Loc.Text(nameof(SecurityRevokeAllConfirmFormat));
    public static string SecurityRevokeAllConfirmYes => Loc.Text(nameof(SecurityRevokeAllConfirmYes));

    // User dialog
    public static string SecurityUserDialogAddTitle => Loc.Text(nameof(SecurityUserDialogAddTitle));
    public static string SecurityUserDialogEditTitle => Loc.Text(nameof(SecurityUserDialogEditTitle));
    public static string SecurityUserNameLabel => Loc.Text(nameof(SecurityUserNameLabel));
    public static string SecurityPasswordLabel => Loc.Text(nameof(SecurityPasswordLabel));
    public static string SecurityConfirmPasswordLabel => Loc.Text(nameof(SecurityConfirmPasswordLabel));
    public static string SecurityPasswordEditHint => Loc.Text(nameof(SecurityPasswordEditHint));
    public static string SecurityActiveLabel => Loc.Text(nameof(SecurityActiveLabel));
    public static string SecurityAdministratorLabel => Loc.Text(nameof(SecurityAdministratorLabel));
    public static string SecurityDialogOk => Loc.Text(nameof(SecurityDialogOk));
    public static string SecurityDialogCancel => Loc.Text(nameof(SecurityDialogCancel));

    // Role dialog
    public static string SecurityRoleDialogTitle => Loc.Text(nameof(SecurityRoleDialogTitle));
    public static string SecurityRoleNameLabel => Loc.Text(nameof(SecurityRoleNameLabel));
    public static string SecurityRoleNameWatermark => Loc.Text(nameof(SecurityRoleNameWatermark));

    // Tree context menu
    public static string MetadataContextNewUser => Loc.Text(nameof(MetadataContextNewUser));
    public static string MetadataContextNewRole => Loc.Text(nameof(MetadataContextNewRole));
    public static string MetadataContextOpenSecurity => Loc.Text(nameof(MetadataContextOpenSecurity));
    public static string MetadataContextDeleteUser => Loc.Text(nameof(MetadataContextDeleteUser));
    public static string MetadataContextDropRole => Loc.Text(nameof(MetadataContextDropRole));

    // Toolbar (New User / New Role buttons)
    public static string ToolbarNewUserTooltip => Loc.Text(nameof(ToolbarNewUserTooltip));
    public static string ToolbarNewRoleTooltip => Loc.Text(nameof(ToolbarNewRoleTooltip));
    public static string ToolbarSecurityManagerTooltip => Loc.Text(nameof(ToolbarSecurityManagerTooltip));
    public static string ToolbarActivityMonitorTooltip => Loc.Text(nameof(ToolbarActivityMonitorTooltip));

    // ── Activity Monitor (Database Trace) ──
    public static string TraceMonitorTabTitle => Loc.Text(nameof(TraceMonitorTabTitle));
    public static string TraceStart => Loc.Text(nameof(TraceStart));
    public static string TraceStop => Loc.Text(nameof(TraceStop));
    public static string TracePauseResume => Loc.Text(nameof(TracePauseResume));
    public static string TracePause => Loc.Text(nameof(TracePause));
    public static string TraceResume => Loc.Text(nameof(TraceResume));
    public static string TraceClear => Loc.Text(nameof(TraceClear));
    public static string TraceGroupNone => Loc.Text(nameof(TraceGroupNone));
    public static string TraceGroupTransaction => Loc.Text(nameof(TraceGroupTransaction));
    public static string TraceGroupStatement => Loc.Text(nameof(TraceGroupStatement));
    public static string TraceHideSelf => Loc.Text(nameof(TraceHideSelf));
    public static string TraceFollowTail => Loc.Text(nameof(TraceFollowTail));
    public static string TraceShowOnlySelected => Loc.Text(nameof(TraceShowOnlySelected));
    public static string TraceFilterWatermark => Loc.Text(nameof(TraceFilterWatermark));
    public static string TraceColSeq => Loc.Text(nameof(TraceColSeq));
    public static string TraceColTime => Loc.Text(nameof(TraceColTime));
    public static string TraceColDelta => Loc.Text(nameof(TraceColDelta));
    public static string TraceColEvent => Loc.Text(nameof(TraceColEvent));
    public static string TraceColDuration => Loc.Text(nameof(TraceColDuration));
    public static string TraceColObject => Loc.Text(nameof(TraceColObject));
    public static string TraceColRows => Loc.Text(nameof(TraceColRows));
    public static string TraceColReads => Loc.Text(nameof(TraceColReads));
    public static string TraceColTx => Loc.Text(nameof(TraceColTx));
    // Quick filter chips (All / Errors / Slow)
    public static string TraceFilterAll => Loc.Text(nameof(TraceFilterAll));
    public static string TraceFilterErrors => Loc.Text(nameof(TraceFilterErrors));
    public static string TraceFilterSlow => Loc.Text(nameof(TraceFilterSlow));
    // Toolbar toggle tooltips
    public static string TraceHideSelfTip => Loc.Text(nameof(TraceHideSelfTip));
    public static string TraceFollowTailTip => Loc.Text(nameof(TraceFollowTailTip));
    public static string TraceShowOnlySelectedTip => Loc.Text(nameof(TraceShowOnlySelectedTip));
    public static string TraceIncludeFunctionsTip => Loc.Text(nameof(TraceIncludeFunctionsTip));
    public static string TraceDetailShowValuesTip => Loc.Text(nameof(TraceDetailShowValuesTip));
    public static string TraceDetailMaximizeTip => Loc.Text(nameof(TraceDetailMaximizeTip));
    public static string TraceJumpLatest => Loc.Text(nameof(TraceJumpLatest));
    // Event filter flyout (display-level; distinct from the source-level Include-Functions capture toggle)
    public static string TraceFilterEventsTip => Loc.Text(nameof(TraceFilterEventsTip));
    public static string TraceGridFilterTip => Loc.Text(nameof(TraceGridFilterTip));
    public static string TraceFilterSectionTypes => Loc.Text(nameof(TraceFilterSectionTypes));
    public static string TraceFilterSectionOperations => Loc.Text(nameof(TraceFilterSectionOperations));
    public static string TraceFilterStatements => Loc.Text(nameof(TraceFilterStatements));
    public static string TraceFilterProcedures => Loc.Text(nameof(TraceFilterProcedures));
    public static string TraceFilterTriggers => Loc.Text(nameof(TraceFilterTriggers));
    public static string TraceFilterFunctions => Loc.Text(nameof(TraceFilterFunctions));
    public static string TraceFilterOpSelect => Loc.Text(nameof(TraceFilterOpSelect));
    public static string TraceFilterOpInsert => Loc.Text(nameof(TraceFilterOpInsert));
    public static string TraceFilterOpUpdate => Loc.Text(nameof(TraceFilterOpUpdate));
    public static string TraceFilterOpDelete => Loc.Text(nameof(TraceFilterOpDelete));
    public static string TraceFilterOpExecute => Loc.Text(nameof(TraceFilterOpExecute));
    public static string TraceFilterOpDdl => Loc.Text(nameof(TraceFilterOpDdl));
    public static string TraceFilterReset => Loc.Text(nameof(TraceFilterReset));
    // Detail sections
    public static string TraceDetailParameters => Loc.Text(nameof(TraceDetailParameters));
    public static string TraceDetailTableAccess => Loc.Text(nameof(TraceDetailTableAccess));
    public static string TraceDetailTiming => Loc.Text(nameof(TraceDetailTiming));
    public static string TraceDetailSession => Loc.Text(nameof(TraceDetailSession));
    // ⚠ The captions of the Session facts under that header. They were English LITERALS in
    // TraceEventDetailViewModel until the PL QA round — beside a sibling row that already read UiStrings,
    // which is what makes the miss legible: the pattern was right there. ⭐ Single WORDS are why no audit
    // caught them; every sweep so far has looked for sentence-shaped literals.
    public static string TraceDetailUser => Loc.Text(nameof(TraceDetailUser));
    public static string TraceDetailRole => Loc.Text(nameof(TraceDetailRole));
    public static string TraceDetailHost => Loc.Text(nameof(TraceDetailHost));
    public static string TraceDetailProcess => Loc.Text(nameof(TraceDetailProcess));
    public static string TraceDetailAttachment => Loc.Text(nameof(TraceDetailAttachment));
    public static string TraceDetailTransaction => Loc.Text(nameof(TraceDetailTransaction));
    public static string TraceDetailCopySql => Loc.Text(nameof(TraceDetailCopySql));
    public static string TraceDetailOpenInEditor => Loc.Text(nameof(TraceDetailOpenInEditor));
    public static string TraceDetailNoSelection => Loc.Text(nameof(TraceDetailNoSelection));
    public static string TraceEmptyHint => Loc.Text(nameof(TraceEmptyHint));
    public static string TraceEmptyWaiting => Loc.Text(nameof(TraceEmptyWaiting));
    public static string TraceEmptyPaused => Loc.Text(nameof(TraceEmptyPaused));
    public static string TraceEmptyNoMatch => Loc.Text(nameof(TraceEmptyNoMatch));

    // Performance Analysis (Phase 1 — plan + timings)
    public static string PerformanceTabHeader => Loc.Text(nameof(PerformanceTabHeader));
    public static string PerformanceRefresh => Loc.Text(nameof(PerformanceRefresh));
    public static string PerformanceRefreshTooltip => Loc.Text(nameof(PerformanceRefreshTooltip));
    public static string PerformanceProfilingHint => Loc.Text(nameof(PerformanceProfilingHint));
    public static string PerformanceEmptyHint => Loc.Text(nameof(PerformanceEmptyHint));
    // Primary plain-language summary (interpolated in PerformanceInsight)
    public static string PerformanceGradeFast => Loc.Text(nameof(PerformanceGradeFast));
    public static string PerformanceGradeAcceptable => Loc.Text(nameof(PerformanceGradeAcceptable));
    public static string PerformanceGradeNeedsAttention => Loc.Text(nameof(PerformanceGradeNeedsAttention));
    public static string PerformanceGradeSlow => Loc.Text(nameof(PerformanceGradeSlow));
    public static string PerformanceGradeUnknown => Loc.Text(nameof(PerformanceGradeUnknown));
    public static string PerformanceLeadFullScanSingle => Loc.Text(nameof(PerformanceLeadFullScanSingle));
    public static string PerformanceLeadFullScanMultiple => Loc.Text(nameof(PerformanceLeadFullScanMultiple));
    public static string PerformanceLeadNoFullScan => Loc.Text(nameof(PerformanceLeadNoFullScan));
    // Measurement-derived lead (used instead of the plan heuristic once per-table reads exist,
    // so the summary always agrees with the Findings zone).
    public static string PerformanceMeasuredCostlyScanSingle => Loc.Text(nameof(PerformanceMeasuredCostlyScanSingle));
    public static string PerformanceMeasuredCostlyScanMultiple => Loc.Text(nameof(PerformanceMeasuredCostlyScanMultiple));
    public static string PerformanceMeasuredNoCostlyScan => Loc.Text(nameof(PerformanceMeasuredNoCostlyScan));
    public static string PerformanceMeasuredNoCostlyScanChanges => Loc.Text(nameof(PerformanceMeasuredNoCostlyScanChanges));
    public static string PerformanceNoiseSubqueriesSingle => Loc.Text(nameof(PerformanceNoiseSubqueriesSingle));
    public static string PerformanceNoiseSubqueriesMultiple => Loc.Text(nameof(PerformanceNoiseSubqueriesMultiple));
    public static string PerformanceForwardPointer => Loc.Text(nameof(PerformanceForwardPointer));
    // Advanced (execution plan)
    public static string PerformancePlanAdvancedHeader => Loc.Text(nameof(PerformancePlanAdvancedHeader));
    public static string PerformanceTimingLabel => Loc.Text(nameof(PerformanceTimingLabel));
    public static string PerformanceCaptureLabel => Loc.Text(nameof(PerformanceCaptureLabel));
    public static string PerformancePlanDialectLabel => Loc.Text(nameof(PerformancePlanDialectLabel));
    public static string PerformanceRawPlanLabel => Loc.Text(nameof(PerformanceRawPlanLabel));
    public static string PerformanceCopy => Loc.Text(nameof(PerformanceCopy));
    // Phase 2 — measured per-table reads (Findings + Table Access zones)
    public static string PerformanceReadsNotMeasured => Loc.Text(nameof(PerformanceReadsNotMeasured));
    public static string PerformanceFindingsHeader => Loc.Text(nameof(PerformanceFindingsHeader));
    public static string PerformanceFindingsNone => Loc.Text(nameof(PerformanceFindingsNone));
    public static string PerformanceFindingsFuture => Loc.Text(nameof(PerformanceFindingsFuture));
    public static string PerformanceAccessHeader => Loc.Text(nameof(PerformanceAccessHeader));
    public static string PerformanceAccessLegend => Loc.Text(nameof(PerformanceAccessLegend));

    // ── Session Manager (live sessions / transactions / health) ──
    public static string SessionManagerTabTitle => Loc.Text(nameof(SessionManagerTabTitle));
    public static string ToolbarSessionManagerTooltip => Loc.Text(nameof(ToolbarSessionManagerTooltip));

    // toolbar
    public static string SessionManagerRefreshTip => Loc.Text(nameof(SessionManagerRefreshTip));
    public static string SessionManagerAutoRefreshTip => Loc.Text(nameof(SessionManagerAutoRefreshTip));
    public static string SessionManagerDisconnectTip => Loc.Text(nameof(SessionManagerDisconnectTip));
    public static string SessionManagerCopyTip => Loc.Text(nameof(SessionManagerCopyTip));
    public static string SessionManagerHideSelfTip => Loc.Text(nameof(SessionManagerHideSelfTip));
    public static string SessionManagerFilterWatermark => Loc.Text(nameof(SessionManagerFilterWatermark));
    public static string SessionManagerMaximizeTip => Loc.Text(nameof(SessionManagerMaximizeTip));

    // health bar
    public static string SessionManagerCountSessions => Loc.Text(nameof(SessionManagerCountSessions));
    public static string SessionManagerCountTransactions => Loc.Text(nameof(SessionManagerCountTransactions));
    public static string SessionManagerCountLongTx => Loc.Text(nameof(SessionManagerCountLongTx));
    public static string SessionManagerCountGcRisk => Loc.Text(nameof(SessionManagerCountGcRisk));
    public static string SessionManagerCountOatLag => Loc.Text(nameof(SessionManagerCountOatLag));
    public static string SessionManagerPrivilegeBanner => Loc.Text(nameof(SessionManagerPrivilegeBanner));
    public static string SessionManagerGradeHealthy => Loc.Text(nameof(SessionManagerGradeHealthy));
    public static string SessionManagerGradeWatch => Loc.Text(nameof(SessionManagerGradeWatch));
    public static string SessionManagerGradeAtRisk => Loc.Text(nameof(SessionManagerGradeAtRisk));

    // sessions grid
    public static string SessionColHealth => Loc.Text(nameof(SessionColHealth));
    public static string SessionManagerHealthHealthy => Loc.Text(nameof(SessionManagerHealthHealthy));
    public static string SessionManagerHealthWarning => Loc.Text(nameof(SessionManagerHealthWarning));
    public static string SessionManagerHealthGcRisk => Loc.Text(nameof(SessionManagerHealthGcRisk));
    public static string SessionManagerHealthSelf => Loc.Text(nameof(SessionManagerHealthSelf));
    public static string SessionManagerHealthSystem => Loc.Text(nameof(SessionManagerHealthSystem));
    public static string SessionColId => Loc.Text(nameof(SessionColId));
    public static string SessionColUser => Loc.Text(nameof(SessionColUser));
    public static string SessionColApplication => Loc.Text(nameof(SessionColApplication));
    public static string SessionColHost => Loc.Text(nameof(SessionColHost));
    public static string SessionColState => Loc.Text(nameof(SessionColState));
    public static string SessionColTx => Loc.Text(nameof(SessionColTx));
    public static string SessionColOldestTx => Loc.Text(nameof(SessionColOldestTx));
    public static string SessionColLoad => Loc.Text(nameof(SessionColLoad));
    public static string SessionsEmpty => Loc.Text(nameof(SessionsEmpty));

    // transactions tab
    public static string SessionManagerTabTransactions => Loc.Text(nameof(SessionManagerTabTransactions));
    public static string SessionManagerTransactionsFilteredFormat => Loc.Text(nameof(SessionManagerTransactionsFilteredFormat));
    public static string TxColId => Loc.Text(nameof(TxColId));
    public static string TxColSession => Loc.Text(nameof(TxColSession));
    public static string TxColState => Loc.Text(nameof(TxColState));
    public static string TxColAge => Loc.Text(nameof(TxColAge));
    public static string TxColIsolation => Loc.Text(nameof(TxColIsolation));
    public static string TxColReadOnly => Loc.Text(nameof(TxColReadOnly));
    public static string TxColGcImpact => Loc.Text(nameof(TxColGcImpact));

    // session details tab (lightweight in M3)
    public static string SessionManagerTabDetails => Loc.Text(nameof(SessionManagerTabDetails));
    public static string SessionManagerDetailsNoSelection => Loc.Text(nameof(SessionManagerDetailsNoSelection));
    public static string SessionManagerDetailStatement => Loc.Text(nameof(SessionManagerDetailStatement));
    public static string SessionManagerDetailNoStatement => Loc.Text(nameof(SessionManagerDetailNoStatement));

    // warnings tab
    public static string SessionManagerTabWarnings => Loc.Text(nameof(SessionManagerTabWarnings));
    public static string SessionManagerNoWarnings => Loc.Text(nameof(SessionManagerNoWarnings));
    public static string SessionManagerWarningWhatToCheck => Loc.Text(nameof(SessionManagerWarningWhatToCheck));

    // session details (M4) — sections + plain-language diagnostics
    public static string SessionManagerGeneralHeader => Loc.Text(nameof(SessionManagerGeneralHeader));
    public static string SessionManagerActivityHeader => Loc.Text(nameof(SessionManagerActivityHeader));
    public static string SessionManagerRoleLabel => Loc.Text(nameof(SessionManagerRoleLabel));
    public static string SessionManagerConnectedLabel => Loc.Text(nameof(SessionManagerConnectedLabel));
    public static string SessionManagerActivitySeqReads => Loc.Text(nameof(SessionManagerActivitySeqReads));
    public static string SessionManagerActivityIdxReads => Loc.Text(nameof(SessionManagerActivityIdxReads));
    public static string SessionManagerActivityInserts => Loc.Text(nameof(SessionManagerActivityInserts));
    public static string SessionManagerActivityUpdates => Loc.Text(nameof(SessionManagerActivityUpdates));
    public static string SessionManagerActivityDeletes => Loc.Text(nameof(SessionManagerActivityDeletes));
    public static string SessionManagerWhyHeader => Loc.Text(nameof(SessionManagerWhyHeader));
    public static string SessionManagerWhyGc => Loc.Text(nameof(SessionManagerWhyGc));
    public static string SessionManagerWhyLongTx => Loc.Text(nameof(SessionManagerWhyLongTx));

    // integration bridges
    public static string SessionManagerOpenInEditor => Loc.Text(nameof(SessionManagerOpenInEditor));
    public static string SessionManagerOpenInEditorTip => Loc.Text(nameof(SessionManagerOpenInEditorTip));
    public static string SessionManagerAnalyze => Loc.Text(nameof(SessionManagerAnalyze));
    public static string SessionManagerAnalyzeTip => CommandTip.Sentence(
        CommandId.Go,
        Loc.Text(nameof(SessionManagerAnalyzeTip)));
    public static string SessionManagerCurrentStatementHeader => Loc.Text(nameof(SessionManagerCurrentStatementHeader));

    // transactions grid — always-on Health dot (mirrors the Sessions grid)
    public static string SessionManagerTxHealthGcBlocker => Loc.Text(nameof(SessionManagerTxHealthGcBlocker));
    public static string SessionManagerTxHealthLong => Loc.Text(nameof(SessionManagerTxHealthLong));
    public static string SessionManagerTxHealthNormal => Loc.Text(nameof(SessionManagerTxHealthNormal));

    // transaction-gap gauge (measured against the GC-danger budget — educate, don't alarm)
    public static string SessionManagerGapCaption => Loc.Text(nameof(SessionManagerGapCaption));
    public static string SessionManagerGapExplain => Loc.Text(nameof(SessionManagerGapExplain));
    public static string SessionManagerGapScaleMin => Loc.Text(nameof(SessionManagerGapScaleMin));
    public static string SessionManagerGapScaleMaxFormat => Loc.Text(nameof(SessionManagerGapScaleMaxFormat));
    public static string SessionManagerGapStatusHealthy => Loc.Text(nameof(SessionManagerGapStatusHealthy));
    public static string SessionManagerGapStatusWatch => Loc.Text(nameof(SessionManagerGapStatusWatch));
    public static string SessionManagerGapStatusCritical => Loc.Text(nameof(SessionManagerGapStatusCritical));

    // context menu
    public static string SessionManagerMenuDisconnect => Loc.Text(nameof(SessionManagerMenuDisconnect));
    public static string SessionManagerMenuCopy => Loc.Text(nameof(SessionManagerMenuCopy));

    // confirmations + status (VM)
    public static string SessionManagerDisconnectConfirmTitle => Loc.Text(nameof(SessionManagerDisconnectConfirmTitle));
    public static string SessionManagerDisconnectConfirmFormat => Loc.Text(nameof(SessionManagerDisconnectConfirmFormat));
    public static string SessionManagerDisconnectConfirmYes => Loc.Text(nameof(SessionManagerDisconnectConfirmYes));
    public static string SessionManagerDisconnectDone => Loc.Text(nameof(SessionManagerDisconnectDone));
    public static string SessionManagerCopyHeaders => Loc.Text(nameof(SessionManagerCopyHeaders));
    public static string SessionManagerLastRefreshFormat => Loc.Text(nameof(SessionManagerLastRefreshFormat));

    // Global Search (Etap 3 — Search Results)
    public static string ToolbarGlobalSearchTooltip => CommandTip.For(
        CommandId.GlobalSearch, Loc.Text(nameof(ToolbarGlobalSearchTooltip)));

    // Export DDL to .sql (portable object script — structure + comments, no grants).
    public static string ToolbarExportDdlTooltip => Loc.Text(nameof(ToolbarExportDdlTooltip));
    public static string ExportDdlDialogTitle => Loc.Text(nameof(ExportDdlDialogTitle));
    public static string ExportDdlFilterName => Loc.Text(nameof(ExportDdlFilterName));
    public static string ExportDdlSucceededFormat => Loc.Text(nameof(ExportDdlSucceededFormat));
    public static string ExportDdlFailedFormat => Loc.Text(nameof(ExportDdlFailedFormat));

    // Data export (Export Framework) — the shared Export dialog + its entry points on the SQL
    // results grid (banner "Export all…", toolbar icon, right-click "Export…").
    public static string ExportDialogTitle => Loc.Text(nameof(ExportDialogTitle));
    public static string ExportResultsMenuItem => Loc.Text(nameof(ExportResultsMenuItem));
    public static string ExportResultsTooltip => Loc.Text(nameof(ExportResultsTooltip));
    public static string ExportAllRowsButton => Loc.Text(nameof(ExportAllRowsButton));
    public static string ExportFormatLabel => Loc.Text(nameof(ExportFormatLabel));
    public static string ExportFormatExcel => Loc.Text(nameof(ExportFormatExcel));
    public static string ExportFormatCsv => Loc.Text(nameof(ExportFormatCsv));
    public static string ExportFormatText => Loc.Text(nameof(ExportFormatText));
    public static string ExportFormatClipboard => Loc.Text(nameof(ExportFormatClipboard));
    public static string ExportExcelFilterName => Loc.Text(nameof(ExportExcelFilterName));
    public static string ExportScopeLabel => Loc.Text(nameof(ExportScopeLabel));
    public static string ExportScopeCurrentView => Loc.Text(nameof(ExportScopeCurrentView));
    public static string ExportScopeAllRows => Loc.Text(nameof(ExportScopeAllRows));
    public static string ExportScopeSelected => Loc.Text(nameof(ExportScopeSelected));
    public static string ExportScopeCountFormat => Loc.Text(nameof(ExportScopeCountFormat));
    public static string ExportScopeCountApproxFormat => Loc.Text(nameof(ExportScopeCountApproxFormat));
    public static string ExportOptionsLabel => Loc.Text(nameof(ExportOptionsLabel));
    public static string ExportDelimiterLabel => Loc.Text(nameof(ExportDelimiterLabel));
    public static string ExportDelimiterSemicolon => Loc.Text(nameof(ExportDelimiterSemicolon));
    public static string ExportDelimiterComma => Loc.Text(nameof(ExportDelimiterComma));
    public static string ExportDelimiterPipe => Loc.Text(nameof(ExportDelimiterPipe));
    public static string ExportDelimiterTab => Loc.Text(nameof(ExportDelimiterTab));
    public static string ExportEncodingUtf8Bom => Loc.Text(nameof(ExportEncodingUtf8Bom));
    public static string ExportIncludeHeader => Loc.Text(nameof(ExportIncludeHeader));
    public static string ExportCultureInvariant => Loc.Text(nameof(ExportCultureInvariant));
    public static string ExportButton => Loc.Text(nameof(ExportButton));
    public static string ExportPreparing => Loc.Text(nameof(ExportPreparing));
    public static string ExportProgressFormat => Loc.Text(nameof(ExportProgressFormat));
    public static string ExportErrorFormat => Loc.Text(nameof(ExportErrorFormat));
    public static string ExportCsvFilterName => Loc.Text(nameof(ExportCsvFilterName));
    public static string ExportTextFilterName => Loc.Text(nameof(ExportTextFilterName));
    public static string ExportDefaultFileName => Loc.Text(nameof(ExportDefaultFileName));
    public static string ExportSavedFormat => Loc.Text(nameof(ExportSavedFormat));
    public static string ExportCopiedFormat => Loc.Text(nameof(ExportCopiedFormat));
    public static string GlobalSearchDialogTitle => Loc.Text(nameof(GlobalSearchDialogTitle));
    public static string GlobalSearchTermLabel => Loc.Text(nameof(GlobalSearchTermLabel));
    public static string GlobalSearchTermWatermark => Loc.Text(nameof(GlobalSearchTermWatermark));
    public static string GlobalSearchMatchNames => Loc.Text(nameof(GlobalSearchMatchNames));
    public static string GlobalSearchMatchSource => Loc.Text(nameof(GlobalSearchMatchSource));
    public static string GlobalSearchCaseSensitive => Loc.Text(nameof(GlobalSearchCaseSensitive));
    public static string GlobalSearchWholeWord => Loc.Text(nameof(GlobalSearchWholeWord));
    public static string GlobalSearchScopeHint => Loc.Text(nameof(GlobalSearchScopeHint));
    public static string GlobalSearchDialogFind => Loc.Text(nameof(GlobalSearchDialogFind));
    public static string GlobalSearchDialogCancel => Loc.Text(nameof(GlobalSearchDialogCancel));
    public static string GlobalSearchTabTitleFormat => Loc.Text(nameof(GlobalSearchTabTitleFormat));
    public static string GlobalSearchSearching => Loc.Text(nameof(GlobalSearchSearching));
    public static string GlobalSearchNoResults => Loc.Text(nameof(GlobalSearchNoResults));
    public static string GlobalSearchResultCount => Loc.Text(nameof(GlobalSearchResultCount));
    public static string GlobalSearchPreviewHint => Loc.Text(nameof(GlobalSearchPreviewHint));
    public static string GlobalSearchPreviewError => Loc.Text(nameof(GlobalSearchPreviewError));

    // Editor context menu + Find/Replace (Etap 1 — Global Search / Editor Find)
    public static string EditorMenuUndo => Loc.Text(nameof(EditorMenuUndo));
    public static string EditorMenuRedo => Loc.Text(nameof(EditorMenuRedo));
    public static string EditorMenuCut => Loc.Text(nameof(EditorMenuCut));
    public static string EditorMenuCopy => Loc.Text(nameof(EditorMenuCopy));
    public static string EditorMenuPaste => Loc.Text(nameof(EditorMenuPaste));
    public static string EditorMenuSelectAll => Loc.Text(nameof(EditorMenuSelectAll));
    public static string EditorMenuFind => Loc.Text(nameof(EditorMenuFind));
    public static string EditorMenuReplace => Loc.Text(nameof(EditorMenuReplace));
    public static string EditorMenuComment => Loc.Text(nameof(EditorMenuComment));
    public static string EditorMenuUncomment => Loc.Text(nameof(EditorMenuUncomment));
    public static string EditorMenuFormat => Loc.Text(nameof(EditorMenuFormat));

    // ─── Debugger (Stage X / D4 — Debugger tab MVP) ───────────────────────────
    public static string MetadataContextDebugProcedure => Loc.Text(nameof(MetadataContextDebugProcedure));
    public static string MetadataContextDebugTrigger => Loc.Text(nameof(MetadataContextDebugTrigger));
    public static string MetadataContextDebugFunction => Loc.Text(nameof(MetadataContextDebugFunction));
    public static string DebuggerTabTitleFormat => Loc.Text(nameof(DebuggerTabTitleFormat));
    // Launch panel.
    public static string DebuggerLaunchHeader => Loc.Text(nameof(DebuggerLaunchHeader));
    public static string DebuggerLaunchParametersHeader => Loc.Text(nameof(DebuggerLaunchParametersHeader));
    public static string DebuggerLaunchNoParameters => Loc.Text(nameof(DebuggerLaunchNoParameters));
    // Compact launch form (D15.3 Seam A) — the inline NULL toggle beside each value field.
    public static string DebuggerParamNullLabel => Loc.Text(nameof(DebuggerParamNullLabel));
    public static string DebuggerParamNullTooltip => Loc.Text(nameof(DebuggerParamNullTooltip));
    // The ONE marker for a value the app supplied rather than the user typing it here — used by every
    // automatic mechanism (parameter history, carry-over across a rebuilt panel, and whatever comes next), so
    // the user learns one convention instead of one per feature. The label says THAT it was filled in; the
    // tooltip says by which mechanism. It disappears the moment the value is edited.
    // Two words, not one word in two colours: Restored is the ordinary case and stays quiet, while Assumed is
    // the ONE inference the panel makes and has to be recognisable at a glance, without reading the tooltip.
    public static string LaunchValueRestoredMarker => Loc.Text(nameof(LaunchValueRestoredMarker));
    public static string LaunchValueAssumedMarker => Loc.Text(nameof(LaunchValueAssumedMarker));
    public static string LaunchValueRestoredTooltip => Loc.Text(nameof(LaunchValueRestoredTooltip));
    public static string LaunchValueAssumedTooltip => Loc.Text(nameof(LaunchValueAssumedTooltip));
    // Advanced section (D15.3 Seam B) — collapsed by default; transaction isolation lives here, out of the
    // main Launch flow (most users never change it). The note leads with WHAT the option changes, then names
    // the levels; the selector below shows the current level.
    public static string DebuggerAdvancedSection => Loc.Text(nameof(DebuggerAdvancedSection));
    public static string DebuggerLaunchIsolationLabel => Loc.Text(nameof(DebuggerLaunchIsolationLabel));
    public static string DebuggerIsolationReadCommitted => Loc.Text(nameof(DebuggerIsolationReadCommitted));
    public static string DebuggerIsolationSnapshot => Loc.Text(nameof(DebuggerIsolationSnapshot));
    public static string DebuggerIsolationNote => Loc.Text(nameof(DebuggerIsolationNote));
    // The shortcut surfaces Seam C's keyboard-first launch — the whole operation is reachable from the keyboard.
    // The label carries no parenthesised shortcut; the key is rendered in the shared shortcut-chip beside it.
    public static string DebuggerLaunchButton => Loc.Text(nameof(DebuggerLaunchButton));
    // The shortcut chip on the launch button — F5 means Start Debugging here, which is CommandId.Go on a
    // debugger tab (the one ratified contradiction with the SQL editor's Execute).
    public static string DebuggerLaunchShortcut => CommandTip.Gesture(CommandId.Go);
    public static string DebuggerLaunchPreparing => Loc.Text(nameof(DebuggerLaunchPreparing));
    // Pre-flight report (§9.2 / §4.6). D15.3 polish: the section is shown ONLY when it has something to say —
    // no header, and no "all clear" line when clean (a clean launch form stays maximally quiet). Each surfaced
    // item is a severity-striped row in the Error Bar visual language (warning = Alert Triangle / WarningBrush,
    // blocking = octagon / ErrorBrush), so there is no header/clean string here anymore.
    public static string DebuggerPreflightAutonomousTx => Loc.Text(nameof(DebuggerPreflightAutonomousTx));
    public static string DebuggerPreflightGenerator => Loc.Text(nameof(DebuggerPreflightGenerator));
    public static string DebuggerPreflightUnsteppable => Loc.Text(nameof(DebuggerPreflightUnsteppable));
    // Toolbar / commands. Every gesture below comes from CommandCatalog — the debugger's stepping keys are
    // declared there as CommandDispatch.Reserved (dispatched by DebuggerTabView, which owns the caret), and
    // being declared is exactly what lets them be shown here without being re-typed.
    // The button caption and the tooltip say the same word, so they read ONE entry — the tooltip composes
    // from the label. Retiring this member's own resource entry is the dedup, not a text change.
    public static string DebuggerContinueTooltip => CommandTip.For(CommandId.Go, DebuggerContinueLabel);
    public static string DebuggerStepIntoTooltip => CommandTip.For(CommandId.DebuggerStepInto, Loc.Text(nameof(DebuggerStepIntoTooltip)));
    public static string DebuggerStepOverTooltip => CommandTip.For(CommandId.DebuggerStepOver, Loc.Text(nameof(DebuggerStepOverTooltip)));
    public static string DebuggerStepOutTooltip => CommandTip.For(CommandId.DebuggerStepOut, Loc.Text(nameof(DebuggerStepOutTooltip)));
    public static string DebuggerRunToCursorTooltip => CommandTip.For(CommandId.DebuggerRunToCursor, Loc.Text(nameof(DebuggerRunToCursorTooltip)));
    public static string DebuggerRunToCursorMenu => Loc.Text(nameof(DebuggerRunToCursorMenu));
    public static string DebuggerStopTooltip => CommandTip.For(CommandId.DebuggerStop, Loc.Text(nameof(DebuggerStopTooltip)));
    public static string DebuggerRestartTooltip => CommandTip.For(CommandId.DebuggerRestart, DebuggerRestartLabel);
    public static string DebuggerToggleBreakpointTooltip => CommandTip.For(CommandId.DebuggerToggleBreakpoint, Loc.Text(nameof(DebuggerToggleBreakpointTooltip)));
    // Status line.
    public static string DebuggerStatusReady => Loc.Text(nameof(DebuggerStatusReady));
    public static string DebuggerStatusPausedFormat => Loc.Text(nameof(DebuggerStatusPausedFormat));
    public static string DebuggerStatusRunning => Loc.Text(nameof(DebuggerStatusRunning));
    public static string DebuggerStatusCompleted => Loc.Text(nameof(DebuggerStatusCompleted));
    // Short, fixed-height headline; the full Firebird message goes to the Error Bar (D15.2 Seam C).
    public static string DebuggerStatusFaulted => Loc.Text(nameof(DebuggerStatusFaulted));
    public static string DebuggerStatusStopped => Loc.Text(nameof(DebuggerStatusStopped));
    public static string DebuggerStatusLaunchFailedFormat => Loc.Text(nameof(DebuggerStatusLaunchFailedFormat));
    public static string DebuggerStopReasonEntry => Loc.Text(nameof(DebuggerStopReasonEntry));
    public static string DebuggerStopReasonStep => Loc.Text(nameof(DebuggerStopReasonStep));
    public static string DebuggerStopReasonBreakpoint => Loc.Text(nameof(DebuggerStopReasonBreakpoint));
    // Advanced-breakpoint stop reasons (D12, spec §9.8).
    public static string DebuggerStopReasonException => Loc.Text(nameof(DebuggerStopReasonException));
    public static string DebuggerStopReasonSuspend => Loc.Text(nameof(DebuggerStopReasonSuspend));
    public static string DebuggerStopReasonDataBreakpoint => Loc.Text(nameof(DebuggerStopReasonDataBreakpoint));
    public static string DebuggerStopReasonDataChangedFormat => Loc.Text(nameof(DebuggerStopReasonDataChangedFormat));
    public static string DebuggerStopReasonConditionErrorFormat => Loc.Text(nameof(DebuggerStopReasonConditionErrorFormat));
    // Error Bar (D15.2 Seam C) — its own thin row below the toolbar; shows on a fault / Break-on-Exception pause.
    public static string DebuggerErrorUnknown => Loc.Text(nameof(DebuggerErrorUnknown));
    // Friendly error text (D15.4 Seam B) — one short, categorised line per FriendlyErrorCategory, shown on the
    // three expression surfaces (Immediate result / Watch value / breakpoint-condition reason). The raw
    // Firebird message stays reachable (row tooltip, Executed SQL, Error Bar) — "friendly + raw available".
    public static string DebuggerFriendlyUserExceptionFormat => Loc.Text(nameof(DebuggerFriendlyUserExceptionFormat));
    public static string DebuggerFriendlyConstraint => Loc.Text(nameof(DebuggerFriendlyConstraint));
    public static string DebuggerFriendlySqlError => Loc.Text(nameof(DebuggerFriendlySqlError));
    public static string DebuggerFriendlyRawTooltip => Loc.Text(nameof(DebuggerFriendlyRawTooltip));
    // Save + compile from the debugger tab (UX Polish Seam 5b). Saving is a deliberate new work cycle: it
    // ends a live session (which was compiled from the old code) before recompiling the routine.
    public static string DebuggerSave => Loc.Text(nameof(DebuggerSave));
    public static string DebuggerSaveTooltip => CommandTip.For(
        CommandId.DebuggerSaveSource, Loc.Text(nameof(DebuggerSaveTooltip)));
    public static string DebuggerSaveUnavailable => Loc.Text(nameof(DebuggerSaveUnavailable));
    // (the empty-buffer refusal is the shared EditorNothingToCompile — one wording for every editor)
    public static string DebuggerSaveEndsSessionTitle => Loc.Text(nameof(DebuggerSaveEndsSessionTitle));
    public static string DebuggerSaveEndsSessionMessage => Loc.Text(nameof(DebuggerSaveEndsSessionMessage));
    public static string DebuggerSaveEndsSessionConfirm => Loc.Text(nameof(DebuggerSaveEndsSessionConfirm));
    public static string DebuggerSaveCompileFailedFormat => Loc.Text(nameof(DebuggerSaveCompileFailedFormat));
    public static string DebuggerStatusSaved => Loc.Text(nameof(DebuggerStatusSaved));
    // Save during a debugging cycle: the session is rebuilt on the compiled code with the settings the user
    // already made, so they land back where they were instead of re-entering the launch form.
    public static string DebuggerStatusSavedRestarting => Loc.Text(nameof(DebuggerStatusSavedRestarting));
    // The compile was refused: the tab stays on the source so the code can be fixed and saved again.
    // The server's own message is in the Error Bar — this is only the short status-line headline.
    public static string DebuggerStatusSaveFailed => Loc.Text(nameof(DebuggerStatusSaveFailed));
    // The first edit during a live session ends it: the session was built from the text that just changed, so
    // stepping on would run code the user can no longer see. Says what happened AND what to do next — the
    // toolbar going grey is the visual cue, this is the reason. (Until Restart can run the edited text without
    // saving, Save is the way back into a session — hence naming it here.)
    public static string DebuggerStatusEndedByEdit => CommandTip.Sentence(
        CommandId.DebuggerRestart,
        Loc.Text(nameof(DebuggerStatusEndedByEdit)));
    // The routine's HEADER changed, so the parameter list the engine reads from the catalog no longer describes
    // this text and a draft-sourced session cannot be started from it yet. Names the one way forward.
    public static string DebuggerStatusEndedByHeaderEdit => CommandTip.Sentence(
        CommandId.DebuggerSaveSource,
        Loc.Text(nameof(DebuggerStatusEndedByHeaderEdit)));
    public static string DebuggerUnsavedSourceFormat => Loc.Text(nameof(DebuggerUnsavedSourceFormat));
    // Variables panel.
    public static string DebuggerVariablesHeader => Loc.Text(nameof(DebuggerVariablesHeader));
    public static string DebuggerVariablesEmpty => Loc.Text(nameof(DebuggerVariablesEmpty));
    public static string DebuggerVariablesColumnName => Loc.Text(nameof(DebuggerVariablesColumnName));
    public static string DebuggerVariablesColumnValue => Loc.Text(nameof(DebuggerVariablesColumnValue));
    public static string DebuggerVariablesColumnKind => Loc.Text(nameof(DebuggerVariablesColumnKind));
    public static string DebuggerVariableNull => Loc.Text(nameof(DebuggerVariableNull));
    public static string DebuggerVariableKindParameter => Loc.Text(nameof(DebuggerVariableKindParameter));
    public static string DebuggerVariableKindLocal => Loc.Text(nameof(DebuggerVariableKindLocal));
    public static string DebuggerVariableKindIn => Loc.Text(nameof(DebuggerVariableKindIn));
    public static string DebuggerVariableKindOut => Loc.Text(nameof(DebuggerVariableKindOut));
    public static string DebuggerVariableKindContextNew => Loc.Text(nameof(DebuggerVariableKindContextNew));
    public static string DebuggerVariableKindContextOld => Loc.Text(nameof(DebuggerVariableKindContextOld));
    public static string DebuggerVariableKindReturn => Loc.Text(nameof(DebuggerVariableKindReturn));
    public static string DebuggerVariableGroupPinned => Loc.Text(nameof(DebuggerVariableGroupPinned));
    public static string DebuggerVariableGroupContext => Loc.Text(nameof(DebuggerVariableGroupContext));
    public static string DebuggerVariableGroupParameters => Loc.Text(nameof(DebuggerVariableGroupParameters));
    public static string DebuggerVariableGroupLocals => Loc.Text(nameof(DebuggerVariableGroupLocals));
    // D-function: the return-value row/group shown only when a function is the debug root. The row displays
    // "not returned yet" until RETURN runs (the session completes at RETURN), then the returned value.
    public static string DebuggerVariableGroupReturn => Loc.Text(nameof(DebuggerVariableGroupReturn));
    public static string DebuggerReturnRowName => Loc.Text(nameof(DebuggerReturnRowName));
    public static string DebuggerReturnPending => Loc.Text(nameof(DebuggerReturnPending));
    public static string DebuggerVariableFilterWatermark => Loc.Text(nameof(DebuggerVariableFilterWatermark));
    public static string DebuggerVariablePinTooltip => Loc.Text(nameof(DebuggerVariablePinTooltip));
    public static string DebuggerVariableEditTooltip => Loc.Text(nameof(DebuggerVariableEditTooltip));
    public static string DebuggerVariableBlobFormat => Loc.Text(nameof(DebuggerVariableBlobFormat));
    // Call stack (single-frame in D4, but the header exists).
    public static string DebuggerCallStackHeader => Loc.Text(nameof(DebuggerCallStackHeader));
    // Errors.
    public static string DebuggerNoConnection => Loc.Text(nameof(DebuggerNoConnection));
    public static string ProcedureDebugTooltip => Loc.Text(nameof(ProcedureDebugTooltip));
    public static string TriggerDebugTooltip => Loc.Text(nameof(TriggerDebugTooltip));
    public static string FunctionDebugTooltip => Loc.Text(nameof(FunctionDebugTooltip));
    public static string DebuggerSourceUnavailableFormat => Loc.Text(nameof(DebuggerSourceUnavailableFormat));
    // Trigger debugging (Stage X / D10) — the launch panel's NEW/OLD context editors + the out-of-scope refusal.
    public static string DebuggerTriggerOutOfScope => Loc.Text(nameof(DebuggerTriggerOutOfScope));
    public static string DebuggerTriggerActionLabel => Loc.Text(nameof(DebuggerTriggerActionLabel));
    public static string DebuggerTriggerActionInsert => Loc.Text(nameof(DebuggerTriggerActionInsert));
    public static string DebuggerTriggerActionUpdate => Loc.Text(nameof(DebuggerTriggerActionUpdate));
    public static string DebuggerTriggerActionDelete => Loc.Text(nameof(DebuggerTriggerActionDelete));
    public static string DebuggerTriggerNewHeader => Loc.Text(nameof(DebuggerTriggerNewHeader));
    public static string DebuggerTriggerOldHeader => Loc.Text(nameof(DebuggerTriggerOldHeader));
    public static string DebuggerTriggerNoColumns => Loc.Text(nameof(DebuggerTriggerNoColumns));
    // Bottom tabbed panel (D5 layout redesign) — extensible: Call Stack / Breakpoints / Output join later.
    public static string DebuggerBottomTabImmediate => Loc.Text(nameof(DebuggerBottomTabImmediate));
    public static string DebuggerBottomTabWatches => Loc.Text(nameof(DebuggerBottomTabWatches));
    public static string DebuggerBottomTabCallStack => Loc.Text(nameof(DebuggerBottomTabCallStack));
    public static string DebuggerBottomTabBreakpoints => Loc.Text(nameof(DebuggerBottomTabBreakpoints));
    public static string DebuggerBottomTabResults => Loc.Text(nameof(DebuggerBottomTabResults));
    // Run to next SUSPEND + its result grid (D12 Seam E2, spec §9.8). The button label is now an
    // SvgIcon + text (D15.2 Seam A); only the tooltip remains here.
    public static string DebuggerRunToSuspendTooltip => Loc.Text(nameof(DebuggerRunToSuspendTooltip));
    // Loop fast-forward (D13) — enabled only while paused inside a WHILE / FOR loop.
    public static string DebuggerRunToNextIterationTooltip => Loc.Text(nameof(DebuggerRunToNextIterationTooltip));
    public static string DebuggerRunToLoopExitTooltip => Loc.Text(nameof(DebuggerRunToLoopExitTooltip));
    public static string DebuggerResultsEmpty => Loc.Text(nameof(DebuggerResultsEmpty));
    // Breakpoints panel (D12 Seam E, spec §9.8) — a pure view of the Core Breakpoint / DataBreakpoint objects.
    public static string DebuggerBreakpointsEmpty => Loc.Text(nameof(DebuggerBreakpointsEmpty));
    public static string DebuggerBreakpointsLineHeader => Loc.Text(nameof(DebuggerBreakpointsLineHeader));
    public static string DebuggerBreakpointsDataHeader => Loc.Text(nameof(DebuggerBreakpointsDataHeader));
    public static string DebuggerBreakpointLineFormat => Loc.Text(nameof(DebuggerBreakpointLineFormat));
    public static string DebuggerBreakpointConditionWatermark => Loc.Text(nameof(DebuggerBreakpointConditionWatermark));
    public static string DebuggerBreakpointWhenLabel => Loc.Text(nameof(DebuggerBreakpointWhenLabel));
    public static string DebuggerBreakpointHitsLabel => Loc.Text(nameof(DebuggerBreakpointHitsLabel));
    public static string DebuggerBreakpointRemoveTooltip => Loc.Text(nameof(DebuggerBreakpointRemoveTooltip));
    public static string DebuggerBreakOnException => Loc.Text(nameof(DebuggerBreakOnException));
    public static string DebuggerBreakOnExceptionTooltip => Loc.Text(nameof(DebuggerBreakOnExceptionTooltip));
    public static string DebuggerDataBreakpointMenu => Loc.Text(nameof(DebuggerDataBreakpointMenu));
    // Hit-count kinds, in HitCountKind order (Always / Exactly / AtLeast / Multiple).
    public static string DebuggerHitCountAlways => Loc.Text(nameof(DebuggerHitCountAlways));
    public static string DebuggerHitCountExactly => Loc.Text(nameof(DebuggerHitCountExactly));
    public static string DebuggerHitCountAtLeast => Loc.Text(nameof(DebuggerHitCountAtLeast));
    public static string DebuggerHitCountMultiple => Loc.Text(nameof(DebuggerHitCountMultiple));
    // Harness Log (Sprint D10.5) — a DEBUG-only diagnostic surface for developing/diagnosing the debugger
    // itself. It is built in code-behind under #if DEBUG (DebuggerTabView.axaml.cs), so these strings are
    // referenced only in DEBUG builds; in RELEASE they are simply unused consts. It replaced the misnamed
    // "Executed SQL" tab (that name read as the user's SQL history, which it never was).
    public static string DebuggerBottomTabHarnessLog => Loc.Text(nameof(DebuggerBottomTabHarnessLog));
    public static string DebuggerHarnessLogDescription => Loc.Text(nameof(DebuggerHarnessLogDescription));
    public static string DebuggerHarnessLogEmpty => CommandTip.Sentence(
        CommandId.DebuggerEvaluateSelection,
        Loc.Text(nameof(DebuggerHarnessLogEmpty)));
    public static string DebuggerBottomPanelCollapseTooltip => Loc.Text(nameof(DebuggerBottomPanelCollapseTooltip));
    // Call Stack panel (D8, spec §5).
    public static string DebuggerCallStackEmpty => Loc.Text(nameof(DebuggerCallStackEmpty));
    public static string DebuggerCallStackLineFormat => Loc.Text(nameof(DebuggerCallStackLineFormat));
    public static string DebuggerCallStackSimulatedGlyph => Loc.Text(nameof(DebuggerCallStackSimulatedGlyph));
    public static string DebuggerCallStackSimulatedTooltip => Loc.Text(nameof(DebuggerCallStackSimulatedTooltip));
    public static string DebuggerCallStackPeekHeaderFormat => Loc.Text(nameof(DebuggerCallStackPeekHeaderFormat));
    // Expression evaluation — Evaluate / Immediate / Executed SQL (D5, spec §9.5 / §10.3).
    public static string DebuggerImmediateHeader => Loc.Text(nameof(DebuggerImmediateHeader));
    public static string DebuggerImmediateWatermark => Loc.Text(nameof(DebuggerImmediateWatermark));
    // A short line of valid-expression examples shown under the Immediate/Watches empty-state (D15.4 Seam A —
    // hints). Kept concise and separated by "·"; these are illustrative shapes, not references to real vars.
    public static string DebuggerExpressionExamples => Loc.Text(nameof(DebuggerExpressionExamples));
    public static string DebuggerImmediateAsStatement => Loc.Text(nameof(DebuggerImmediateAsStatement));
    public static string DebuggerImmediateAsStatementTooltip => Loc.Text(nameof(DebuggerImmediateAsStatementTooltip));
    public static string DebuggerImmediateEvaluateButton => Loc.Text(nameof(DebuggerImmediateEvaluateButton));
    public static string DebuggerImmediateClearTooltip => Loc.Text(nameof(DebuggerImmediateClearTooltip));
    public static string DebuggerEvaluateSelectionTooltip => CommandTip.For(
        CommandId.DebuggerEvaluateSelection, Loc.Text(nameof(DebuggerEvaluateSelectionTooltip)));
    public static string DebuggerImmediateEmpty => Loc.Text(nameof(DebuggerImmediateEmpty));
    public static string DebuggerEvalKindExpression => Loc.Text(nameof(DebuggerEvalKindExpression));
    public static string DebuggerEvalKindStatement => Loc.Text(nameof(DebuggerEvalKindStatement));
    public static string DebuggerEvalStatementOk => Loc.Text(nameof(DebuggerEvalStatementOk));
    public static string DebuggerEvalErrorUnknown => Loc.Text(nameof(DebuggerEvalErrorUnknown));
    // Watches panel (D5 seam b, §9.5).
    public static string DebuggerWatchesHeader => Loc.Text(nameof(DebuggerWatchesHeader));
    public static string DebuggerWatchWatermark => Loc.Text(nameof(DebuggerWatchWatermark));
    public static string DebuggerWatchAddButton => Loc.Text(nameof(DebuggerWatchAddButton));
    public static string DebuggerWatchAddTooltip => Loc.Text(nameof(DebuggerWatchAddTooltip));
    public static string DebuggerWatchRemoveTooltip => Loc.Text(nameof(DebuggerWatchRemoveTooltip));
    public static string DebuggerWatchesEmpty => Loc.Text(nameof(DebuggerWatchesEmpty));
    public static string DebuggerWatchNotEvaluated => Loc.Text(nameof(DebuggerWatchNotEvaluated));
    public static string DebuggerWatchSideEffectTooltip => Loc.Text(nameof(DebuggerWatchSideEffectTooltip));
}
