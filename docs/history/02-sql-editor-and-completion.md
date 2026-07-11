# History — SQL Editor UX, Autocomplete, Formatter/Highlighting v1

> Archived from CLAUDE.md's former "Completed milestones" chronicle during the Documentation Cleanup Sprint (2026-07-11).
> Verbatim extract, lines 404–649 of the original file. Nothing altered except this header.

---

### SQL Queries Panel (IBExpert-style, shipped 2026-05-30)

A vertical 200 px panel on the right of the SQL editor lists the saved queries for the active connection. Clicking a query loads its text into the editor; edits in the editor flow back to the active saved query without an explicit Save action. Panel toggles globally from a toolbar button.

**Persistence schema** ([WorkspaceState.cs](src/EmberTern.Core/Workspace/WorkspaceState.cs)):
- New `SavedQuery { Id, Name, SqlText }` — plain DTO, settable props for `System.Text.Json`.
- `ConnectionWorkspace` gained `List<SavedQuery> SavedQueries` + `string? ActiveSavedQueryId`. Empty list = legacy workspace (pre this milestone); VM bootstraps a single "Query 1" on first Connect in that case, seeded from the existing Query tab's SqlText so the migration is lossless.
- `WorkspaceState` gained `bool QueryPanelVisible = true` (global, not per-connection — layout preference is consistent across ERPs).

**VM model** ([MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs), [SavedQueryViewModel.cs](src/EmberTern.App/ViewModels/SavedQueryViewModel.cs)):
- New `SavedQueryViewModel` — `Id` immutable, `[ObservableProperty]` for `Name` + `SqlText`.
- `MainWindowViewModel.SavedQueries` is an `ObservableCollection<SavedQueryViewModel>` mirroring the active connection's saved queries.
- `SelectedSavedQuery` (TwoWay-bound to the panel's ListBox). Setter (`partial void OnSelectedSavedQueryChanged`) writes `value.SqlText → QueryText`.
- `QueryText` setter (`partial void OnQueryTextChanged`) writes `value → SelectedSavedQuery.SqlText`. This is the back-edge of the loop.
- `_suppressSavedQuerySync` guards the loop. Set during connection switches, programmatic SelectedSavedQuery changes, and bulk operations (Clear All); cleared before user-driven UI events take over. Without it, loading a saved query into the editor echoes back into the same saved query (no-op but wasteful), and connection switches corrupt cells on the way out.
- `IsQueryPanelVisible` (default `true`). Bound to the panel's `IsVisible`. Toggles via `ToggleQueryPanelCommand`.
- `ActiveWorkspaceProfileId` (replaces internal `_previousActiveProfileId` field) — now an `[ObservableProperty]` with `NotifyCanExecuteChangedFor` on `NewQueryCommand`, `DeleteSelectedQueryCommand`, `ClearAllQueriesCommand`. Notifying `HasActiveWorkspace` keeps button enabled-state in sync with connect/disconnect.
- Commands:
    - `NewQuery` — `CanExecute = HasActiveWorkspace`. Picks the next "Query N" by parsing existing names that match the `Query <int>` pattern (renamed queries are skipped so the next number is `max-of-still-numbered + 1`). New query starts with empty SqlText, becomes selected immediately.
    - `DeleteSelectedQuery` — `CanExecute = SelectedSavedQuery is not null`. Goes through the existing `ConfirmationRequested` event (reuses the modal `ConfirmDialog`). On confirm, removes the entry; if it was the active one, falls back to the neighbor (or re-bootstraps "Query 1" if the list would otherwise empty out — the panel must always have a target for the next keystroke).
    - `ClearAllQueries` — `CanExecute = HasActiveWorkspace && SavedQueries.Count > 0`. Confirms, clears, bootstraps fresh "Query 1".
    - `ToggleQueryPanel` — flips `IsQueryPanelVisible`.
- `LoadSavedQueriesFor(ws)` (called from `LoadWorkspaceFor`) hydrates `SavedQueries` from the dict entry, bootstraps from `QueryText` if empty, then selects either the persisted active query or the first one. Sync flag suppresses the QueryText echo through the whole load.
- `SnapshotCurrentTabs()` commits the live editor text into the active SavedQuery before reading the list — otherwise unsaved keystrokes disappear at the connection-switch boundary. The same snapshot also persists `ActiveSavedQueryId`.
- `ClearWorkspaceTabs()` clears `SavedQueries` and `SelectedSavedQuery` (under suppress) along with the tabs themselves.
- `CaptureWorkspace()` writes `IsQueryPanelVisible` into the saved `WorkspaceState`; `RestoreWorkspace()` reads it back.

**View** ([MainWindow.axaml](src/EmberTern.App/Views/MainWindow.axaml)):
- Editor area row is now a 2-column Grid (`*,Auto`). Left column hosts the SqlEditor/DdlEditor stack; right column is the 200 px saved-queries panel.
- Panel layout: header row with the panel title + three icon buttons (`+` new, `🗑` delete selected, `⌦` clear all) | empty-hint TextBlock when no queries | `ListBox` over `SavedQueries` with `SelectedItem` two-way bound to `SelectedSavedQuery`. Panel hidden via `IsVisible="{Binding IsQueryPanelVisible}"` — `Auto` column collapses cleanly to zero width.
- Editor toolbar fully rewritten to the spec order: `▶ Execute | ⏹ Cancel | ↺ Commit | ↻ Rollback | sep | + New query | ▤ Toggle panel | 🗑 Clear editor | ✕ Close tab`. Clear and Close are now icon buttons with tooltips matching the existing `Button.icon` style. Commit/Rollback moved here from the transaction bar so primary actions concentrate in one toolbar; transaction bar at the bottom is now status-only (status dot + text), no duplicate buttons.

**Strings** ([UiStrings.cs](src/EmberTern.App/UiStrings.cs)) — added `ToolbarClearEditorIcon = "🗑"`, `ToolbarCloseTabIcon = "✕"`, `ToolbarNewQueryIcon = "+"`, `ToolbarToggleQueryPanelIcon = "▤"` plus matching tooltips, and the panel-side strings: `QueryPanelHeader`, `QueryPanelEmptyHint`, `QueryDefaultNameFormat = "Query {0}"`, `QueryDeleteConfirm*`, `QueryClearAllConfirm*`.

**Tests** — 17 new in [SavedQueryVmTests.cs](tests/EmberTern.Tests/SavedQueryVmTests.cs) covering bootstrap, legacy migration, edit-write-back, selection-loads-text, "Query N" numbering (including the renamed-skip rule), delete-and-pick-neighbor, delete-last-rebootstraps, clear-all-rebootstraps, toggle-panel, stash-on-disconnect, restore-on-reconnect, cross-connection independence, capture/restore round-trip of `SavedQueries` + `QueryPanelVisible`. Plus 2 new in [WorkspaceStoreTests.cs](tests/EmberTern.Tests/WorkspaceStoreTests.cs): `SavedQueries` + `QueryPanelVisible` round-trip; legacy JSON without these fields loads with defaults. **162 / 162 green** (143 + 17 + 2). Smoke-verified app launches, exits cleanly.

**Gotcha promoted to architecture lore.**

14. **CommunityToolkit `[ObservableProperty]` + `partial void OnXxxChanged` is the right tool for paired bindings, but you MUST guard the loop with a `_suppress*` flag.** When `QueryText` writes back to `SelectedSavedQuery.SqlText` (via `OnQueryTextChanged`) and `SelectedSavedQuery` change writes to `QueryText` (via `OnSelectedSavedQueryChanged`), naive code re-enters infinitely. The guard pattern: `if (_suppressX) return;` at the top of each hook, and `_suppressX = true; try { ... } finally { _suppressX = false; }` around any line that would re-trigger the other hook. Skip the flag at your peril — diagnosing the infinite loop after the fact is harder than adding the flag prophylactically.

### Visual polish — selection, tabs, DataGrid, copy (shipped)

Five-part polish pass with no functional changes — addresses inconsistent selection colors, the cramped/Fluent-blue DataGrid, default-styled bottom tabs, and the missing "copy to clipboard" affordance for grid results.

**1. Unified selection color.** DataGrid's selected-row background was the FluentTheme/SystemAccent yellow-tan, clashing with the TreeView's already-overridden `SelectionBrush` (`#094771` dark / `#CCE4F7` light). Wired the same brush in via Style selector against the row template's `Rectangle#BackgroundRectangle` part — same approach as we used for TreeViewItem state colors, but applied via selector instead of resource keys. Three rules in [ControlStyles.axaml](src/EmberTern.App/Themes/ControlStyles.axaml): `DataGridRow:selected`, `:selected:pointerover`, and `:pointerover` (the last fed from `HoverOverlayBrush`).

**2. Compact DataGrid.** Default Fluent rows were 32 px tall with 14 px column padding — far too airy for a 5000-row result grid. Tightened to: `DataGridCell { FontSize=11, Padding=8,3, MinHeight=0 }`, `DataGridRow { MinHeight=0 }`. Visible row pitch drops roughly 40%, matching IBExpert density. Column headers (`DataGridColumnHeader`) now use `ElevatedPanelBrush` background with themed `ForegroundBrush`, `FontSize=11`, `FontWeight=SemiBold`, `Padding=8,4` — consistent with the rest of the chrome. Hover and pressed states fall through `HoverOverlayBrush` so the System-accent yellow never appears.

**3. Zebra stripes.** Avalonia 12.0.0's `DataGrid` doesn't expose `AlternatingRowBackground` as a regular/attached property in the version we pull (AVLN2000 at compile time when set as an attribute). Wired via `DataGridRow:nth-child(2n) /template/ Rectangle#BackgroundRectangle` style selector instead. New brush `RowAlternateBrush` defined in both theme dictionaries — `#0DFFFFFF` (~5% white overlay) on dark, `#0D000000` (~5% black overlay) on light. Subtle enough to not compete with hover/selection but clear enough to follow rows visually across wide tables.

**4. Bottom-panel tab styling.** Added `Classes="bottom-tab"` to the three `TabItem`s in the bottom panel (Results / Messages / Output). Style overrides in [ControlStyles.axaml](src/EmberTern.App/Themes/ControlStyles.axaml): `FontSize=11`, `Padding=8,4`, foreground via `SubtleForegroundBrush` (subtle when inactive). Active tab: `Border#PART_LayoutRoot` background → `BackgroundBrush`, foreground → `ForegroundBrush` (full-opacity reading). Hover: `HoverOverlayBrush`. The FluentTheme default uses a thin blue underline pipe (`PART_SelectedPipe`) on selected tabs which read as a navigation-style tab strip — explicitly hidden (`IsVisible=False`) so bottom tabs look like panel sections (fill instead of underline), consistent with VS Code / JetBrains Output/Problems/Terminal tabs.

**5. DataGrid context menu — copy.** Right-click on the result grid opens a 4-item menu: Copy cell / Copy row / Copy row with headers / Copy all with headers. The first three use the currently selected row (`DataGrid.SelectedIndex`) and current column (`DataGrid.CurrentColumn.DisplayIndex`); the last walks the whole result. Output is tab-separated values — pasting into Excel or IBExpert grids round-trips cleanly. Cells containing literal tabs / CR / LF have those characters replaced with spaces (IBExpert convention — no quoting / escaping, plain TSV).

   `CopyGridMode` enum (Cell / Row / RowWithHeaders / AllWithHeaders) lives in [ViewModels/CopyGridMode.cs](src/EmberTern.App/ViewModels/CopyGridMode.cs). `MainWindowViewModel.BuildCopyText(mode, rowIndex, colIndex)` is the pure formatter — returns `null` when there's no result set or the indices are out of range — and `CopyGridAsync(...)` is the side-effecting wrapper that calls `ClipboardWriteRequested` and posts a "Copied {label} to clipboard." Info message. The MenuItem `Click` handlers in [MainWindow.axaml.cs](src/EmberTern.App/Views/MainWindow.axaml.cs) (`OnCopyCellClick` / `OnCopyRowClick` / `OnCopyRowWithHeadersClick` / `OnCopyAllWithHeadersClick`) all funnel through one private `InvokeCopy(CopyGridMode)` that reads the DataGrid's selection/current-column and delegates to `CopyGridAsync`.

   Strings live in [UiStrings.cs](src/EmberTern.App/UiStrings.cs): `GridCopyCell`, `GridCopyRow`, `GridCopyRowWithHeaders`, `GridCopyAllWithHeaders`, `GridCopiedToClipboardFormat`, `GridCopiedCellLabel`, `GridCopiedRowLabel`, `GridCopiedRowsFormat`.

**Tests** — 11 new in [CopyGridTests.cs](tests/EmberTern.Tests/CopyGridTests.cs): cell value, null-cell → empty, row TSV without header, row-with-headers prepends header line, all-with-headers full dump, tab/newline escape, no-result-set → null, out-of-range index → null, `CopyGridAsync` invokes `ClipboardWriteRequested`, no-result → returns false / no clipboard invocation, success path logs an Info message. **173 / 173 green** (162 + 11).

**Gotcha promoted to architecture lore.**

15. **Avalonia 12.0.0 `DataGrid` doesn't expose `AlternatingRowBackground` as a settable XAML attribute.** Setting `AlternatingRowBackground="{DynamicResource X}"` fails the Avalonia compiler with `AVLN2000: Unable to resolve suitable regular or attached property AlternatingRowBackground on type Avalonia.Controls.DataGrid`. Newer Avalonia versions added the property — ours hasn't. Use a `:nth-child(2n)` style selector against the row template's `Rectangle#BackgroundRectangle` part instead. **Rule**: for DataGrid theming in this codebase, prefer Style selectors against template parts (`Rectangle#BackgroundRectangle`, `Grid#PART_ColumnHeaderRoot`, `Border#PART_LayoutRoot` for TabItem) over property setters on the DataGrid itself — the property surface lags behind FluentTheme's template surface.

### Visual polish — selection / right-click bugfixes (shipped)

Two follow-up bugs surfaced from the Visual polish milestone.

**ListBox selection color (Saved Queries panel).** The Saved Queries ListBox in the right-side panel was still rendering FluentTheme's SystemAccent yellow/brown for the selected item — only the DataGridRow / TreeViewItem were getting the unified `SelectionBrush`. Added three rules in [ControlStyles.axaml](src/EmberTern.App/Themes/ControlStyles.axaml) against `ListBoxItem` template's `ContentPresenter#PART_ContentPresenter`: `:selected`, `:selected:pointerover`, `:selected:focus` → `SelectionBrush`; `:pointerover` → `HoverOverlayBrush`. Same shape as the existing DataGridRow / TreeViewItem overrides — the FluentTheme ListBoxItem template names its content presenter `PART_ContentPresenter` (as opposed to `BackgroundRectangle` for DataGridRow or `PART_LayoutRoot` Border for TabItem). Applies to every ListBox in the app, not just Saved Queries.

**DataGrid right-click row selection.** Avalonia 12 DataGrid doesn't auto-select the row under the cursor on right-click — only left-click flips selection. Symptom: right-clicking the result grid opened the context menu, but the "Copy cell / row / row with headers" entries acted on whatever row was *previously* selected (or nothing). Fix: wire `_resultGrid.PointerPressed += OnResultGridPointerPressed` in [MainWindow.axaml.cs](src/EmberTern.App/Views/MainWindow.axaml.cs) — when `IsRightButtonPressed`, walk up `e.Source` via `FindAncestorOfType<DataGridRow>(includeSelf: true)` and set `_resultGrid.SelectedItem = row.DataContext`. Leaves `e.Handled = false` so the right-click still bubbles up to fire the ContextMenu. Order is correct because PointerPressed runs before the ContextMenu open path (which fires on PointerReleased / RightTapped) — so the selection is in place when the menu items execute.

Tests stay 173 / 173 green (no VM-level change). Smoke-verified.

**Gotcha promoted to architecture lore.**

16. **Avalonia DataGrid right-click does NOT change selection.** Other Avalonia ItemsControls (TreeView, ListBox) also don't right-click-select out of the box, but in practice the user-facing rule is: if you wire a ContextMenu to a selection-driven control, also wire a PointerPressed handler that selects the item under the cursor on right-button-down. Otherwise context-menu actions silently operate on stale selection. **Pattern**: `PointerPressed → if RightButtonPressed → FindAncestorOfType<RowType> → set SelectedItem → leave Handled=false`.

### SQL Editor — execute selected + autoformat (shipped 2026-05-30)

Two SQL-editor polish items: scope-by-selection for Execute, and a basic SQL autoformatter on Alt+F.

**Execute scoped to selection.** `ExecuteQueryAsync` previously read `QueryText` unconditionally. Now it calls `ResolveActiveSql()`, which returns the editor's current selection when non-whitespace and the full editor text otherwise. The selection comes from a `Func<string?>? SelectedQueryTextProvider` callback that the view installs in `OnDataContextChanged` — implemented by `GetSqlEditorSelection()` in the code-behind, which just returns `_editor.SelectedText` (null when nothing is selected). VM stays free of Avalonia types: it sees a plain `Func<string?>`.

Behaviour summary:
- Select a fragment in the SqlEditor → press F5 / Ctrl+Enter → only that fragment runs.
- Nothing selected → whole editor content runs (legacy behaviour, untouched).
- Selection of pure whitespace → falls through to whole-editor (so accidental tab/space drag-selects don't execute an empty statement).

**Autoformat — `EmberTern.Core.Sql.SqlFormatter` (pure Core, zero deps).** New static class with a single `Format(string) → string` entry point. Rules:
- Lowercase recognised SQL keywords (SELECT, FROM, WHERE, HAVING, GROUP BY, ORDER BY, JOIN forms, AND, OR, ON, AS, IN, IS, NOT, NULL, LIKE, BETWEEN, EXISTS, DISTINCT, ASC, DESC, UNION/ALL/INTERSECT/EXCEPT, INSERT/INTO/VALUES, UPDATE/SET, DELETE, CREATE/ALTER/DROP, table/view/index/procedure/trigger/function/generator/sequence, WITH, RETURNING, CASE/WHEN/THEN/ELSE/END, BEGIN/DECLARE/EXECUTE/BLOCK, TRUE/FALSE, PRIMARY/KEY/FOREIGN/REFERENCES, UNIQUE/CHECK/DEFAULT/CONSTRAINT, FETCH/FIRST/ROWS/ONLY/ROW, USING, PLAN). Identifiers and function names — including `COUNT`, `SUM`, etc. — keep their case (they're not SQL keywords).
- Break a new line before each top-level clause keyword (SELECT, FROM, WHERE, HAVING, GROUP BY, ORDER BY, any JOIN form including LEFT/RIGHT/INNER/OUTER/CROSS/FULL ± OUTER + JOIN). No leading newline when the result starts with a clause keyword (no `\nselect` at the start).
- AND / OR break with a 2-space indent (sub-conjunction inside WHERE / HAVING / JOIN-ON).
- String literals (`'...'`, including doubled `''` escapes), quoted identifiers (`"..."`), line comments (`-- ...`), block comments (`/* ... */`) are all treated as opaque tokens — no lowercase, no line break, contents preserved byte-for-byte.
- Whitespace and dotted qualifiers (`t.id`, `schema.t`) emit without spaces around `.`; function calls (`COUNT(*)`) get no space between identifier and `(`; commas emit `", "`; two-char operators (`<=`, `>=`, `<>`, `!=`, `||`, `::`) stay glued.

Implementation is a small two-pass tokeniser → emitter (no regex, no external lib). Tokens: Word / Number / String / QuotedIdent / LineComment / BlockComment / Punctuation / Whitespace / Newline. The emitter filters out whitespace tokens and re-emits with structural newlines. `MatchStructuralPhrase` greedily matches `LEFT OUTER JOIN`-style sequences (longest modifier chain + `JOIN`) and the two-word `GROUP BY` / `ORDER BY` pairs — bare `GROUP` without trailing `BY` is *not* a line break (defensive against pathological input). Idempotent: `Format(Format(x)) == Format(x)` for all tested inputs.

**Long-line wrapping (IBExpert style, 120-char threshold).** A `WrapLongLines` post-pass runs after `Emit` and rewrites any output line longer than `MaxLineWidth = 120`. Both wrap kinds pack multiple items per line up to the threshold, with continuation lines aligned under the first item — IBExpert convention. Shared `PackWithContinuation` helper handles the packing for both:
- **SELECT column list** — line starting with `select ` (optionally `select distinct ` / `select all `) packs columns up to 120 chars on the first line; continuation lines indent to `head.Length` spaces (7 for `select `, 16 for `select distinct `) so wrapped columns sit directly under the first column. Top-level commas only — commas inside `COALESCE(a, b, c)` or any nested call are skipped via `SplitByTopLevelComma`, which tracks paren depth and skips quoted runs (`'...'` with `''` escape, `"..."` for quoted idents).
- **IN (...) value list** — `" in ("` outside strings/quoted idents opens a value list; the matching `)` closes it. Values pack up to 120 chars per line; continuation lines indent to `parenOpen + 1` spaces so they line up one char past the opening `(`. The closing `)` stays inline with the last value (no `\n)`). Subquery IN clauses (`in (select ...`) are deliberately skipped — the emitter's structural break already handles them; comma-splitting a subquery body would be nonsense.
- **JOIN ON conditions** — no new code: the existing always-on `AND` / `OR` line break (`\n  and ...` / `\n  or ...`) already provides the spec's "wrap before AND with 2-space indent" behaviour. A regression-pinning test confirms `JOIN u ON a = b AND c = d` stays correctly split.

The wrap pass is idempotent by construction: re-tokenising the wrapped output collapses the continuation newlines and re-emits the same single line, which the post-pass then re-wraps identically. Tests cover both directions (`Format(sql)` == `Format(Format(sql))`).

**VM wiring** — new `FormatSqlCommand` (`[RelayCommand(CanExecute = nameof(CanFormatSql))]`, gated on `IsQueryTabActive`). When fired, reads selected-or-all SQL the same way as Execute, runs it through `SqlFormatter.Format`, and either calls back into the view's `ReplaceSelectedOrAllText` (which replaces the editor selection with the formatted text and re-selects it; or overwrites the full document when there's no selection) or, when no callback is registered (tests), writes `QueryText` directly. `_selectedWorkspaceTab` carries `NotifyPropertyChangedFor(CanFormatSql)` + `NotifyCanExecuteChangedFor(FormatSqlCommand)` so the button greys out instantly on DDL-tab switch.

**Same callback shape for both features.** One `Func<string?> SelectedQueryTextProvider` (read), one `Action<string> ReplaceSelectedOrAllText` (write). Execute uses only the reader; Format uses both. No two-way property binding for selection state — the callbacks are pull-on-demand at command-fire time, so there's no per-keystroke noise hitting the VM.

**UI** — Alt+F as a window-level `KeyBinding` next to F5 / Ctrl+Enter. New icon-toolbar button (`⎄` glyph, tooltip "Format SQL (Alt+F)") placed after the Commit/Rollback separator, immediately before the New-query button. Strings live in `UiStrings.ToolbarFormatSqlIcon` / `ToolbarFormatSqlTooltip` and bind via `x:Static` (no VM-string getter).

**Tests** — 37 `SqlFormatterTests` (26 baseline + 11 wrap: under/over-120 for SELECT cols + IN values; SELECT continuation aligned 7 spaces under `select`; DISTINCT pushes continuation to 16 spaces under `select distinct`; function-call commas don't split columns and each output line stays ≤120; IN continuation aligned to one char past `(`, close paren inline with last value; trailing AND clause sits on its own structural line; nested AND-broken IN respects the conjunction's leading 2-space indent; subquery-IN skipped; JOIN-ON 2-space indent; idempotency over wrapped output). Plus 8 `SqlEditorActionsVmTests` covering `ResolveActiveSql` (no provider / empty / whitespace / non-empty selection) and `FormatSql` (full text / selection routes through callback / no-op on empty / CanExecute on query tab). **218 / 218 green** (173 → 218, +45 new).

**Smoke-verified.** App launches, exits cleanly via the 5-second-uptime test.

**Gotcha note.** The Format command writes through `_editor.Document.Replace(start, len, text)` + `_editor.Select(start, text.Length)` when there's a selection (preserves the selection on the formatted output so the user can immediately re-format / refine), and assigns `_editor.Text = text` when not (which triggers the existing `TextChanged → VM.QueryText` push). Don't try to two-way-bind `TextEditor.Text` to mutate it — same flaky-binding gotcha from M3 / M6.

### SQL Autocomplete (shipped)

Two-step milestone: a keyword + schema-object completion popup, then dot-completion
that resolves `ALIAS.` / `TABLE.` to column names. Logic lives in `EmberTern.Core.Sql`
(testable without UI / DB); AvaloniaEdit's `CompletionWindow` does the popup.

**Step 1 — `Ctrl+Space` + 3-char auto-trigger** ([SqlKeywords.cs](src/EmberTern.Core/Sql/SqlKeywords.cs), [SqlCompletionContext.cs](src/EmberTern.Core/Sql/SqlCompletionContext.cs), [Completion/](src/EmberTern.App/Completion/)):
- `SqlKeywords.All` — deduped, alphabetised, single-token Firebird vocabulary
  (DML, DDL, PSQL, types, common functions). Multi-word phrases like
  `CHARACTER SET` are deliberately split — completion inserts one identifier at
  a time.
- `SqlCompletionContext.GetCurrentWord(text, caret)` returns the identifier ending
  at the caret (letters/digits/underscores; `$` and `.` end the run).
- `SqlCompletionContext.ShouldAutoTrigger(word)` is true when length ≥ 3 AND
  contains at least one letter or underscore (rejects pure numeric runs).
- `SqlCompletionData : ICompletionData` (AvaloniaEdit) carries `Text` + `Kind` +
  `Priority`. Schema objects sort above keywords on ties; columns sort highest.
- `SqlCompletionController` is wired per-`TextEditor`: subscribes to
  `TextArea.KeyDown` (Ctrl+Space force-open, Escape close-defensively) and
  `TextArea.TextEntered` (auto-trigger after each identifier char). One
  `CompletionWindow` at a time; closes itself on `Closed`, nullifies the field.
- `MainWindowViewModel.EnumerateLoadedObjects()` walks the active connection's
  metadata categories and yields the `MetadataObject` leaves the explorer has
  already eagerly loaded — cheap per keystroke; no DB round-trip.

**Step 2 — `ALIAS.` / `TABLE.` column completion** ([SqlAliasResolver.cs](src/EmberTern.Core/Sql/SqlAliasResolver.cs)):
- `SqlAliasResolver.ParseAliases(sql)` tokenises the editor text (skipping
  string literals, line + block comments, and `(...)` subqueries wholesale at
  both outer and inner level) and walks `FROM` / `JOIN` / `UPDATE` / `INTO` /
  `TABLE` starter keywords. Per starter, parses `TABLE [AS] alias` segments
  separated by commas, terminated by an alias-terminator (`ON`, `WHERE`,
  `GROUP`, `JOIN`, `LEFT`, `RIGHT`, `INNER`, …). Returns an
  `IReadOnlyDictionary<string,string>` (alias → table, case-insensitive).
  Unquoted identifiers are uppercased to match Firebird's catalog convention;
  quoted identifiers preserve literal case (and unwrap the surrounding `"`).
- `SqlAliasResolver.ResolveTableForQualifier(sql, qualifier, knownTables)` is
  the pure resolution entry point (testable, no UI, no DB): direct table-name
  match wins (qualifier might be `TABLE.` not an alias), then alias-map
  lookup, then the mapped table must itself exist in `knownTables` — no
  fabricated lookups against schemas we don't have evidence of.
- `SqlCompletionContext.GetDotContext(text, caret)` recognises
  `QUALIFIER.[prefix]` under the caret — qualifier uppercased, prefix
  preserved verbatim so the completion list can filter on it. Returns null
  when the caret isn't in a dot context.
- `FirebirdMetadataReader.ListColumnsAsync(tableName)` opens its own short-lived
  ReadCommitted tx (independent of the user's working tx, same pattern as
  `ListAsync`) and returns column names ordered by `RDB$FIELD_POSITION`.
- `MainWindowViewModel` caches columns per table in `_columnCache`
  (case-insensitive dict). `TryGetCachedColumns(table)` is the sync path used
  on every dot keystroke; `EnsureColumnsAsync(table)` populates the cache on
  first request and returns the list. Cache is cleared in
  `ApplyActiveConnectionChange` so switching connections never surfaces
  columns from a previous schema.
- The controller's dot path: when the user types `.`, it queries
  `_dotTableResolver` (a callback wired in the view to
  `MainWindowViewModel.ResolveDotTable`). Cache hit → popup immediately.
  Cache miss → fire `EnsureColumnsAsync` and re-show on completion **only if**
  the caret is still in the same dot context (user might have moved away while
  we awaited the round-trip).

**Architecture choices.**
- Five-callback shape on the controller (objects provider, dot resolver,
  cached columns provider, ensure-columns async, plus the existing selection
  shape from execute-selected/format). Controller asks for state; view
  supplies it. No AvaloniaEdit types reach the VM. No DB types reach the
  controller — `EnsureColumnsAsync` is just a `Func<string, Task<IReadOnlyList<string>>>`.
- Alias resolution is intentionally V1-simple: subqueries are skipped wholesale
  rather than tracked per-scope. `SELECT * FROM A a WHERE id IN (SELECT b.id FROM B b)`
  exposes `a → A` to the outer editor; typing `b.` inside the subquery
  doesn't currently resolve. Per-scope alias tables are a V2 candidate.
- The column cache is intentionally NOT persisted. It's connection-lifetime
  only — the catalog can change under the user (DDL via this very tool), and
  re-fetching on first dot per session is cheap.

**Tests** — 47 new across 3 files: [SqlKeywordsTests.cs](tests/EmberTern.Tests/SqlKeywordsTests.cs) (5), [SqlCompletionContextTests.cs](tests/EmberTern.Tests/SqlCompletionContextTests.cs) (14 + 8 Theory rows = 14 methods covering word boundaries, underscores/digits, auto-trigger gates, dot-context with empty / partial prefixes / uppercased qualifier / no-qualifier rejection / mid-word), [SqlAliasResolverTests.cs](tests/EmberTern.Tests/SqlAliasResolverTests.cs) (21 covering `FROM`, `JOIN`, `LEFT OUTER JOIN`, comma-lists, `UPDATE`, `AS`-keyword, quoted identifiers, terminator non-capture, string + comment skipping, subquery skipping, plus `ResolveTableForQualifier` direct-match, alias-map, unknown-alias bailout, mapped-to-unknown-table bailout, and the tie-break when an alias name shadows an existing table). **265 / 265 green** (218 → 265, +47 new). App launches and exits cleanly via the 5-second-uptime smoke test.

**Gotcha promoted to architecture lore.**

17. **AvaloniaEdit's `CompletionWindow.StartOffset` / `EndOffset` define the *segment that gets replaced* on selection.** Leaving `EndOffset` at default makes completion *insert* at the caret, leaving the prefix in place — selecting "SELECT" against editor text "SEL|" then produces "SELSELECT". Fix is to explicitly set both: for word completion, `StartOffset = word.Start, EndOffset = word.End`. For dot-completion, replace just the `[prefix]` after the dot (`StartOffset = dot.PrefixStart, EndOffset = dot.PrefixEnd`), NOT from the qualifier onward — otherwise selecting a column eats the `N.` part too. **Rule**: always specify both `StartOffset` and `EndOffset` on `CompletionWindow` when the user has typed any prefix at all.

18. **Outer-loop paren-skipping in any SQL scanner.** When scanning for top-level FROM/JOIN/UPDATE/…, a bare `for` loop will happily find them *inside* `(SELECT … FROM …)` subqueries and scoop aliases from a different scope into the outer map. The scan must track paren depth and `SkipParenBlock` on `(` even at the outer level. Same rule applies to any future "top-level X" scanner — top-level means depth-0 in `()`. Initial alias resolver was bitten by this; `Subquery_ScopedAliasesAreIgnored` regression-pins it.

### Formatter "lowercase everything" + light-theme syntax highlighting (shipped)

Two-part follow-up after the autocomplete milestone.

**Formatter lowercases all Word tokens, not just keywords.** [SqlFormatter.cs](src/EmberTern.Core/Sql/SqlFormatter.cs)'s `MaybeLowercase` no longer gates on `IsKeyword` — every `TokenKind.Word` is `ToLowerInvariant`'d. Other token kinds pass through verbatim: `TokenKind.String` (`'...'`), `TokenKind.QuotedIdent` (`"..."`), `TokenKind.LineComment`, `TokenKind.BlockComment`, `TokenKind.Number`, `TokenKind.Punctuation`. Matches IBExpert's "lowercase all" preset, which the user prefers for daily ERP work. Cleanup: `IsKeyword` is now only consulted by `NeedsSpaceBefore` (decides whether to glue an identifier against a following `(` for function-call shape) — gluing must NOT happen for keywords like `IN`/`ON`, so the function lives on. Tests updated: `NonKeywordIdentifiers_PreserveCase` → `_AreLowercased`, `Having_IsClauseBreak` switches `COUNT(*)` → `count(*)`, `FunctionCall_NoSpaceBeforeOpeningParen` switches `COUNT(*)` → `count(*)`, `SelectColumnList_Wrap_RespectsCommasInsideFunctionCalls` switches `COALESCE(...)` → `coalesce(...)`. New regression: `LowercaseAll_AppliesToAliasesAndFunctionsAndDottedNames` (aliases, dotted qualifiers `N.ID`, function name `COUNT`, all lowercased; mid-statement `'Mixed CASE Stays'` string literal preserved).

**Light-theme syntax highlighting** ([Assets/FirebirdSql.Light.xshd](src/EmberTern.App/Assets/FirebirdSql.Light.xshd)). A second XSHD with a VS Code Light+ palette: `#008000` comment, `#A31515` string, `#098658` number, `#0000FF` bold keyword, `#267F99` data type, `#795E26` function, `#333333` operator. Keyword/datatype/function lists are mirrored 1:1 from the dark variant so toggling theme only changes colors, never which tokens get highlighted. Registered in [App.axaml.cs](src/EmberTern.App/App.axaml.cs) under `App.FirebirdSyntaxLightName = "Firebird SQL Light"`; the existing `FirebirdSyntaxName = "Firebird SQL"` stays as the dark default. Both registered eagerly at `Initialize`. `MainWindow.axaml.cs` picks the right one in `ApplySyntaxHighlightingForCurrentTheme()` based on `Window.ActualThemeVariant`, called from ctor (initial paint) and from a new `ActualThemeVariantChanged` handler (theme toggle). Applies to both `SqlEditor` and `DdlEditor`. The existing `OnThemeToggleClick` is unchanged — it flips `RequestedThemeVariant`; Avalonia's resolved-variant pipeline then fires `ActualThemeVariantChanged` on the window, which re-runs `ApplySyntax…` automatically.

**Gotcha promoted to architecture lore.**

19. **AvaloniaEdit XSHD palettes can't bind to DynamicResource — swap definitions on theme change.** A `Color name="Keyword" foreground="#569CD6"` value in the XSHD is parsed once at registration into a `HighlightingColor`. Trying to make it react to theme change by retroactively recoloring would require walking every line's `RichTextModel`. Cleaner path: register **two** named definitions (`"Firebird SQL"` + `"Firebird SQL Light"`), and on `ActualThemeVariantChanged` reassign `editor.SyntaxHighlighting` to the matching one. Same shape as the `IconBrushConverter` solution for the metadata tree icons — re-evaluate the resource selection on `ActualThemeVariant` change.

### Light-theme color polish (shipped)

Two follow-up tweaks to the previous milestone.

**Editor selection brush picked up from theme.** `AvaloniaEdit.Editing.TextArea.SelectionBrush` is also a static property — FluentTheme's default selection color (light brown / accent yellow) leaks through unless overridden. Wired through the same `ApplyEditorThemeColors` helper that swaps syntax highlighting: pulls `"SelectionBrush"` from the app resource dictionary using `TryGetResource(key, themeVariant, out _)` (so it resolves against the *target* theme), and assigns to both `SqlEditor.TextArea.SelectionBrush` and `DdlEditor.TextArea.SelectionBrush`. Selection foreground is left at the editor default — text on dark `#094771` (light: `#CCE4F7`) reads fine without an override. The helper was renamed from `ApplySyntaxHighlightingForCurrentTheme` to `ApplyEditorThemeColors` to reflect the broader scope.

**Light accent → blue.** `AccentColor` in the Light dictionary of [Colors.axaml](src/EmberTern.App/Themes/Colors.axaml) flipped from purple `#6F3DC7` / `#9B6DDB` to the same blue `#2D6BBF` / `#1A4F8F` the Dark dictionary already uses. Drives the Execute button + focus rings + transaction-active marker — everything that bound `AccentBrush`/`AccentMutedBrush` reads identically across themes now. The earlier purple was a holdover from the original Light palette and clashed with the rest of the light chrome.

**Tests**: 266 / 266 still green (purely visual tweaks; the editor selection brush flow goes through code that isn't exercised by xUnit). Smoke: app starts and exits cleanly via the 5-second-uptime test.

**Gotcha promoted to architecture lore.**

20. **AvaloniaEdit's `TextArea.SelectionBrush` is a regular `IBrush` property — same DynamicResource limitation as the XSHD palettes.** Setting it via `<TextArea.SelectionBrush>{DynamicResource SelectionBrush}</TextArea.SelectionBrush>` in XAML would work in theory but the editor exposes its `TextArea` only through code, and we don't have a XAML reference to it. Set programmatically from the same theme-change handler that already updates syntax highlighting, using `Application.Current.Resources.TryGetResource(key, themeVariant, out _)` so the lookup hits the dictionary for the variant we're transitioning *to*, not the one we're leaving.

### Double-click-in-editor → open DDL (shipped)

Match the IBExpert/DataGrip reflex: double-clicking on an identifier in the SQL editor that names a loaded metadata object opens (or focuses) its DDL tab — the same code path as double-clicking the leaf in the metadata tree. Non-matching words fall through to AvaloniaEdit's default (select-the-word).

**Core helper** ([SqlCompletionContext.cs](src/EmberTern.Core/Sql/SqlCompletionContext.cs)): new `GetWordAt(string text, int offset) → CurrentWord` — walks identifier chars in both directions from the offset. Distinct from the existing `GetCurrentWord` which only walks backward (designed for "word ending at caret" for completion). Stops at the same non-identifier set: whitespace, dots, `$`, parens, commas. Returns an empty `CurrentWord` when the offset doesn't touch any identifier char on either side.

**VM** ([MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs)):
- `internal static MetadataObject? ResolveByName(IEnumerable<MetadataObject>, string?)` — pure case-insensitive first-match lookup. First-match wins maps cleanly to `ConnectionNodeViewModel.CategoryOrder` (Tables before Triggers etc.) when the input comes from `EnumerateLoadedObjects()` — so if a table and a trigger share a name, the table wins, which is the expected default for "show me what this thing is".
- `internal MetadataObject? TryResolveLoadedObject(string?)` — thin wrapper that feeds `EnumerateLoadedObjects()` into `ResolveByName`.
- `public bool TryOpenDdlForWord(string?)` — combines the lookup with the existing `OnOpenDdlRequested(MetadataObject)` open-or-focus path. Returns true when a match was found (caller marks the editor event handled).

**View** ([MainWindow.axaml.cs](src/EmberTern.App/Views/MainWindow.axaml.cs)): `_editor.DoubleTapped += OnSqlEditorDoubleTapped`. Handler reads `_editor.Text` + `_editor.CaretOffset`, extracts the word via `SqlCompletionContext.GetWordAt`, calls `_currentVm.TryOpenDdlForWord(word.Text)`. On a successful match, sets `e.Handled = true` so the SQL editor doesn't keep a stale word-selection behind after focus moves to the DDL tab. On no match, leaves the event unhandled — AvaloniaEdit's default kicks in and the word stays selected (useful for "copy this identifier").

**Design choices.**
- `GetWordAt` lives in Core, not in the view — pure, testable, no AvaloniaEdit dependency. The view only uses it as a one-liner.
- No custom rendering, no hover underline, no Ctrl-click — spec called it explicitly. Just the double-click handler.
- `EnumerateLoadedObjects()` already walks the metadata tree in category order; reusing it for the lookup means there's nothing new to invalidate when categories load/unload — same source of truth as the autocomplete popup.
- The lookup is O(n) over loaded objects rather than the HashSet hinted at in the spec — n is bounded by what the user has expanded (≤ 2000-ish leaves for the FK ERP schema) and a double-click is a once-per-user-action event. A cached Dictionary would need invalidation on every connect/disconnect/category-refresh, and the savings (sub-millisecond → microsecond) wouldn't be felt. If the cost ever shows up in practice we can add it without changing the public surface.

**Tests**. +8 in [SqlCompletionContextTests.cs](tests/EmberTern.Tests/SqlCompletionContextTests.cs) covering `GetWordAt`: empty text, caret mid-word, caret at start/end of identifier, caret in spaces (empty result), stops at `.`, stops at `$` (RDB$RELATIONS case), underscores + digits included. +7 in new [OpenDdlOnDoubleClickTests.cs](tests/EmberTern.Tests/OpenDdlOnDoubleClickTests.cs) covering `ResolveByName`: null/empty name returns null, no-match returns null, exact match, case-insensitivity (lower / mixed / upper), first-wins on duplicate (table beats trigger when sharing a name), empty input list, match across kinds when earlier categories don't contain the name. **281 / 281 green** (266 → 281, +15). Smoke-verified: app launches, runs 8s without crashing.

