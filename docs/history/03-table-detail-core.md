# History — Table Detail tab (Pola/Indeksy/Dane/Zależności core)

> Archived from CLAUDE.md's former "Completed milestones" chronicle during the Documentation Cleanup Sprint (2026-07-11).
> Verbatim extract, lines 650–1342 of the original file. Nothing altered except this header.

---

### Table Detail tab (Pola / Indeksy / DDL sub-tabs, shipped)

Double-clicking a Table in the metadata tree no longer opens a DDL-only tab — it opens a **TableDetailTab** with three sub-tabs: **Pola** (Fields), **Indeksy** (Indexes), **DDL**. Other object kinds (View / Procedure / Trigger / Function / Generator / etc.) keep the existing DDL-only behavior unchanged.

**Core models** ([TableDetail.cs](src/EmberTern.Core/Metadata/TableDetail.cs)): two plain classes with init-only props — `FieldInfo` (Position, Name, Type, Size, Scale, NotNull, DefaultValue, ComputedSource, Description) and `IndexInfo` (Name, Fields, IsUnique, IsDescending, IsPrimary). Zero Avalonia dependencies, no interfaces.

**Firebird reader** ([FirebirdTableDetailReader.cs](src/EmberTern.Firebird/FirebirdTableDetailReader.cs)) — direct class, no interface. Two async methods:
- `GetFieldsAsync(tableName)` — joins `RDB$RELATION_FIELDS` ↔ `RDB$FIELDS` on `RDB$FIELD_SOURCE`. Returns position, name, formatted type, length, abs(scale), null flag, stripped default, computed source, description. Uses its own short-lived `ReadCommitted` tx (matching the M5 pattern) so browsing never bumps the user's working-tx counter.
- `GetIndexesAsync(tableName)` — queries `RDB$INDICES` with correlated subqueries against `RDB$INDEX_SEGMENTS` (`LIST(TRIM(...), ',')`) and `RDB$RELATION_CONSTRAINTS` to pull column list + constraint type. `IsPrimary = constraintType == "PRIMARY KEY"`; `IsDescending = RDB$INDEX_TYPE == 1`; `IsUnique = RDB$UNIQUE_FLAG == 1`. FB 2.5+ compatible (`LIST` aggregate exists from 2.5; the `ORDER BY` inside the correlated subquery is a no-op on 2.5 but harmless).

`FormatFieldType(type, length, scale, precision, subType)` is `internal static` so tests cover the integer-→-string mapping without a live FB. Special cases: types 7/8/16 with negative scale render as `NUMERIC(p,s)` (or `DECIMAL(p,s)` when `RDB$FIELD_SUB_TYPE == 2`). CHAR/VARCHAR/CSTRING include length. Unknown → `TYPE_<n>`. `StripDefaultPrefix` removes the `DEFAULT ` keyword that `RDB$DEFAULT_SOURCE` carries. Names from `RDB$` tables are trimmed (catalog strings are space-padded). Both queries take `@tableName` (consistent with `FirebirdMetadataReader.ListColumnsAsync`), and both wrap raw `FbException` in `MetadataReadException`.

**VM** ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)) — partial CTM observable, two ObservableCollections (`Fields`, `Indexes`), `[ObservableProperty]` for `ActiveSubTabIndex`, `DdlText`, `IsLoading`, `ErrorMessage`. `LoadAsync()` calls the reader for fields + indexes and the existing `FirebirdDdlReader.FetchDdlAsync` for the DDL sub-tab. Catches `MetadataReadException` → sets `ErrorMessage`; outer `OnOpenDdlRequested` surfaces it as a Messages-tab error so the UX matches a DDL fetch failure.

**WorkspaceTabViewModel** gained a `TableDetail` enum value and a `CreateTableDetail(owner, obj, detail, profileId)` factory. The tab carries a nullable `TableDetailTabViewModel TableDetail` reference. Tab strip rendering is unchanged — same `▦` Table icon, same close button.

**MainWindowViewModel** routes through the new path in `OnOpenDdlRequested`: when `obj.Kind == Table`, creates a TableDetail tab and kicks off `LoadAsync` after `SelectTab`. All other kinds use the existing `_ddlReader.FetchDdlAsync` + `CreateDdl` path. Added `IsTableDetailTabActive` / `ActiveTableDetail` computed properties; the `_selectedWorkspaceTab` `[ObservableProperty]` notifications were extended to fire them on tab switch.

**Dedup** — the existing "focus existing tab for same (kind,name)" loop now matches both `Ddl` and `TableDetail` kinds, so double-clicking the same table twice focuses the existing TableDetail tab (and after restore from disk, a persisted-as-Ddl tab still de-dups against the same name).

**Persistence** — `SnapshotCurrentTabs` writes TableDetail tabs as `CoreTabKind.Ddl`. Live `DdlText` is pulled from `tab.TableDetail.DdlText` so the persisted DDL is the post-load value (the tab-level `DdlText` was captured at create-time when `detail.DdlText` was still empty). On restore the tab comes back as DDL-only — to re-open the detail view the user double-clicks the table again. Persistence schema evolution for TableDetail tabs is a V2 candidate.

**View** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml) + .cs) — UserControl with `x:DataType="TableDetailTabViewModel"`. Inside: a `TabControl` with 3 `TabItem`s reusing the existing `Classes="bottom-tab"` style for visual consistency with the Results/Messages strip.

- **Pola**: read-only `DataGrid` with #(Position) / Nazwa / Typ / Rozmiar / Skala / Not Null (checkbox) / Default / Opis. Same DataGrid styling as the results grid (font 11, zebra stripes, hover/selection brushes via global Style selectors).
- **Indeksy**: read-only `DataGrid` with Nazwa / Pola / Unikalny (checkbox) / Malejący (checkbox) / PK (checkbox).
- **DDL**: read-only `AvaloniaEdit.TextEditor`. Same syntax-highlighting + selection-brush swap on `ActualThemeVariantChanged` as the main DDL editor. Text pushed from code-behind via `PropertyChanged` listener on `TableDetailTabViewModel.DdlText` — two-way `TextEditor.Text` binding is flaky (same gotcha from M3/M6, applies to this third editor too).

**MainWindow.axaml integration** — added `xmlns:views="using:EmberTern.App.Views"` and a third element inside the editor `Grid.Column="0"` stack, sibling to `SqlEditor` and `DdlEditor`:

```xaml
<views:TableDetailTabView DataContext="{Binding ActiveTableDetail}"
                          IsVisible="{Binding DataContext.IsTableDetailTabActive, ElementName=RootWindow}" />
```

The view is hidden unless a TableDetail tab is active; with no active TableDetail tab the DataContext is null and inner bindings silently no-op.

**Gotcha promoted to architecture lore.**

21. **When a child element sets its own `DataContext` via Binding, sibling bindings on the same element resolve against the NEW DataContext — not the parent's.** First-pass markup `<views:TableDetailTabView DataContext="{Binding ActiveTableDetail}" IsVisible="{Binding IsTableDetailTabActive}" />` fails compile with `AVLN2000: Unable to resolve property … IsTableDetailTabActive on TableDetailTabViewModel` because IsVisible's binding source is the just-set DataContext (`TableDetailTabViewModel`), not the parent `MainWindowViewModel`. Fix: source IsVisible through the window's DataContext explicitly with `ElementName`: `IsVisible="{Binding DataContext.IsTableDetailTabActive, ElementName=RootWindow}"`. **Rule**: when you set `DataContext` on an element via Binding, every other binding on that same element must source explicitly (ElementName, RelativeSource) or evaluate against the new context — there is no parent-DataContext fallthrough.

**Tests** — new [FirebirdTableDetailReaderTests.cs](tests/EmberTern.Tests/FirebirdTableDetailReaderTests.cs) covering type-integer→string mapping (basic types, CHAR/VARCHAR with length, NUMERIC/DECIMAL with negative scale, INTEGER with zero scale, unknown→TYPE_n), `IsPrimaryConstraint` case-insensitivity, `StripDefaultPrefix` behavior on `DEFAULT 0` / `default 42` / null / empty, and SQL-shape regression pins for both `FieldsSql` and `IndexesSql`. **309 / 309 green** (281 → 309, +28 new). Smoke-verified: app launches, runs 5 seconds without crashing.

### Table Detail polish + per-tab toolbar visibility (shipped)

Four follow-ups to the Table Detail tab milestone:

**1. Field position is 1-based; zero scale renders empty.** Firebird stores `RDB$FIELD_POSITION` as 0-based but users expect SQL/IBExpert's 1-based column numbering. Added a computed `DisplayPosition => Position + 1` on `FieldInfo` and re-pointed the "#" column binding at it. Plain INTEGER columns have `Scale == 0`, which rendered as a noisy "0" in the Skala column; new [ZeroToEmptyConverter.cs](src/EmberTern.App/Converters/ZeroToEmptyConverter.cs) (IValueConverter, singleton `Instance`) returns `""` for zero/null int/long/short/double/decimal and the value's string form otherwise. Bound the Skala column through it. New folder `Converters/` follows the no-`Utils/` rule — named for what it holds.

**2. DataGrid font 11 in TableDetailTabView.** Both Fields and Indexes grids now match the results grid's `FontSize="11"` instead of inheriting the FluentTheme default (~13). Higher row density for the typical ERP schema where tables have 20-50 columns.

**3. Saved Queries panel hides on non-Query tabs.** Previously the panel showed/hid only via the user's `IsQueryPanelVisible` toggle preference. Added a computed `ShowQueryPanel = IsQueryPanelVisible && IsQueryTabActive` and bound the panel's `IsVisible` to it. The user's preference is preserved across tab switches — toggling back to the Query tab restores the previous shown/hidden state. `IsQueryPanelVisible` and `_selectedWorkspaceTab` both `NotifyPropertyChangedFor(ShowQueryPanel)` so the panel reacts to either source of change.

**4. Toolbar buttons gate by tab kind, not by `IsEnabled`.** Replaced the "always-visible-but-disabled" pattern with explicit `IsVisible` bindings — hidden is cleaner than greyed-out noise:

| Button | IsVisible binding |
|---|---|
| ▶ Execute / ⏹ Cancel | unchanged (`ShowExecuteButton`/`ShowCancelButton` already gate on `IsQueryTabActive`) |
| ✓ Commit / ✕ Rollback | unchanged (`IsQueryTabActive`) |
| Separator | `IsQueryTabActive` (was always visible) |
| + New saved query | `IsQueryTabActive` |
| ▤ Toggle Saved Queries | `IsQueryTabActive` |
| 🗑 Clear editor | `IsQueryTabActive` |
| ⎄ Format SQL | `IsQueryTabActive` |
| ✕ Close tab | **`IsClosableTabActive`** (new — covers DDL + TableDetail; anchored Query tab can't be closed so the button hides on it) |

New computed property: `IsClosableTabActive => SelectedWorkspaceTab is { Kind: WorkspaceTabKind.Ddl or WorkspaceTabKind.TableDetail }`. Added to the `_selectedWorkspaceTab` `NotifyPropertyChangedFor` chain alongside `ShowQueryPanel`.

Transaction bar at the bottom unchanged — still gates on `IsQueryTabActive` (visible only when the Query tab is selected; matches the existing convention that transaction state belongs to executing SQL).

**Tests**: 309 / 309 still green (no VM-level test change; the new converters and visibility flags are visual). Build clean (zero warnings, TWAE on). Smoke-verified: app launches and runs 5 seconds without crashing.

### TableDetail persistence + tighter row pitch (shipped)

Two follow-ups on top of the Table Detail polish milestone:

**1. TableDetail tabs survive a restart as TableDetail (not as DDL-only).** Previously `SnapshotCurrentTabs` wrote every non-Query tab as `CoreTabKind.Ddl`, so a restored TableDetail tab degraded to a DDL-only tab and the user had to re-double-click the tree to get the 3-sub-tab view back. Now:

- Added `WorkspaceTabKind.TableDetail` to the **persistence** enum in [WorkspaceState.cs](src/EmberTern.Core/Workspace/WorkspaceState.cs) (independent of the VM-side `WorkspaceTabKind` enum, per the V1.1 milestone rule that persistence schemas evolve on their own).
- `SnapshotCurrentTabs` now branches three ways: Query → `CoreTabKind.Query`; TableDetail → `CoreTabKind.TableDetail` (with live `td.DdlText` captured); else → `CoreTabKind.Ddl`. Fields/Indexes intentionally aren't serialized — re-fetched after Connect.
- `LoadWorkspaceFor` gained a third branch: when it sees `CoreTabKind.TableDetail`, it instantiates `new TableDetailTabViewModel(name, _tableDetailReader, _ddlReader)`, seeds `DdlText` from the cached value (so the DDL sub-tab paints instantly), wraps it via `CreateTableDetail`, and fires `_ = detail.LoadAsync()` — fire-and-forget; the existing `ConfigureAwait(true)` chain in `LoadAsync` posts ObservableCollection mutations back to the UI thread.
- Dedup is unchanged — the existing `tab.Kind is WorkspaceTabKind.Ddl or WorkspaceTabKind.TableDetail` match in `OnOpenDdlRequested` already covers both kinds. Legacy saved files (with old `"Kind": "Ddl"` for tables) restore as DDL tabs; user re-opens to get the new view — same forward-compat story `JsonStringEnumConverter` gives us for free.

**2. Tighter DataGrid row pitch in TableDetailTabView.** Added scoped styles inside `<UserControl.Styles>`:

```xaml
<Style Selector="DataGridRow">  <Setter Property="Height"  Value="22"/>  </Style>
<Style Selector="DataGridCell"> <Setter Property="Padding" Value="6 2"/> </Style>
```

UserControl scope means these don't bleed into the SQL Editor result grid (which keeps its existing styling). Font stays at 11. Row height drops from FluentTheme's ~28 to 22; the Fields tab now shows ~50% more columns above the fold on the typical FK-ERP schema.

**Tests**: 309 / 309 still green (persistence wiring is exercised by `WorkspacePersistenceVmTests` round-trips but no test pinned the "TableDetail → Ddl" downgrade behavior, so nothing needed adjusting). Build clean (zero warnings, TWAE on). Smoke-verified.

### TableDetail sub-tabs grown to six (shipped)

Tab order is now: **Pola | Ograniczenia | Indeksy | Dane | Opis | DDL** — three new tabs slotted around the existing ones.

**1. ConstraintInfo model** ([TableDetail.cs](src/EmberTern.Core/Metadata/TableDetail.cs)) — plain class with Name / Kind ("PRIMARY KEY" / "FOREIGN KEY" / "CHECK" / "UNIQUE") / Fields / RefTable / RefFields / CheckSource. All strings default to empty for null-safe binding into the grid.

**2. Three new reader methods** on [FirebirdTableDetailReader.cs](src/EmberTern.Firebird/FirebirdTableDetailReader.cs):

- `GetConstraintsAsync(tableName)` — joins `RDB$RELATION_CONSTRAINTS` LEFT JOIN `RDB$REF_CONSTRAINTS` LEFT JOIN `RDB$CHECK_CONSTRAINTS` LEFT JOIN `RDB$TRIGGERS` (filtered to type=1). FK gets its referenced fields via a sub-LIST aggregate on `RDB$INDEX_SEGMENTS` keyed by `RDB$CONST_NAME_UQ`. **Spec deviation**: the spec query referenced `chk.RDB$TRIGGER_SOURCE` — that column doesn't exist on `RDB$CHECK_CONSTRAINTS` (only the trigger-name pointer does). Added a join to `RDB$TRIGGERS chk_src ON chk_src.RDB$TRIGGER_NAME = chk.RDB$TRIGGER_NAME AND chk_src.RDB$TRIGGER_TYPE = 1`. Each CHECK constraint creates 6 hidden triggers sharing the same source body; filtering to type 1 (BEFORE INSERT) picks one canonical row.
- `GetDescriptionAsync(tableName)` — `SELECT RDB$DESCRIPTION FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @tableName`. Returns trimmed string or empty when the BLOB is null.
- `GetDataPreviewAsync(tableName, limit)` — `SELECT FIRST {limit} * FROM "{table}"` with the identifier quoted (internal `"` doubled). Returns a `QueryResult` shaped exactly like `FirebirdQueryExecutor`'s output. Own short-lived ReadCommitted tx — doesn't touch the user's working-tx counter (intentional: this is metadata browsing, not user query execution).

Mapping helpers `BuildConstraintInfo` / `NormalizeDescription` / `NormalizeCheckSource` are `internal static` for unit testing without a live FB.

**3. VM additions** ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)):
- `ObservableCollection<ConstraintInfo> Constraints`
- `Description` (string) + `DescriptionLoaded` flag → `HasDescription` / `ShowDescriptionEmpty` computed
- `DataResult` (QueryResult?) + `DataError` (string) + `DataResultVersionTag` (bump-on-fetch GUID so the view rebuilds DataGrid columns) → `HasDataResult` / `ShowDataError` computed
- `DataPreviewRowLimit = 200` const + `DataPreviewHint` formatted string

`LoadAsync` extended to fetch constraints + description + data preview after the existing fields/indexes/DDL chain. **Stays sequential** — `FbConnection` services one command at a time, so `Task.WhenAll` would throw "Connection in use" (this contradicts the spec's "Task.WhenAll is fine" — kept sequential per the existing architecture lore from earlier milestones; commented inline). The data-preview fetch is wrapped in its own `try/catch MetadataReadException` so a SELECT permission failure surfaces as `DataError` on the Dane tab without stranding the other tabs.

**4. View** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml)) — three new TabItems in spec order:
- **Ograniczenia** — DataGrid with Name / Typ / Pola / Tabela ref. / Pola ref. / Warunek. Same compact row style as the other grids (row 22, cell padding 6 2, font 11).
- **Dane** — `Grid RowDefinitions="Auto,*"`: TextBlock hint ("Pokazuję pierwsze 200 wierszy") on row 0; DataGrid (`x:Name="DataPreviewGrid"`) on row 1 visible when `HasDataResult`; error TextBlock on the same row visible when `ShowDataError`. Columns are built imperatively in code-behind from `QueryResult.Columns` (same pattern as `MainWindow.PopulateResultGrid` — `Binding "[i]"` against `object?[]` rows). Listens to `DataResultVersionTag` change to rebuild.
- **Opis** — `Grid` with two children, mutually exclusive via `IsVisible`: subtle centered "Brak opisu." when empty; read-only `TextBox` with `TextWrapping="Wrap"` + `AcceptsReturn="True"` for selectable text when populated. Plain text only — no AvaloniaEdit needed.

**Tests** — appended to [FirebirdTableDetailReaderTests.cs](tests/EmberTern.Tests/FirebirdTableDetailReaderTests.cs):
- `ConstraintsSql_QueriesAllConstraintCatalogTables` — regression-pins the catalog tables joined
- `BuildConstraintInfo_*` — 5 tests covering PK / FK / CHECK / UNIQUE / all-nulls mapping
- `NormalizeDescription_TrimsAndDefaultsToEmpty` — Theory with 4 cases

**319 / 319 green** (309 → 319, +10 new). Build clean (zero warnings, TWAE on). Smoke-verified: app launches, runs 5 seconds without crashing.

### TableDetail late-session fixes (shipped)

After the 6-sub-tabs landed, four real-world bugs surfaced (and were fixed) while smoke-testing against the user's FB 5 schema. Test count stayed at 319 throughout — all fixes were code/architecture changes, no new behavior to pin.

**1. CheckBox columns rendered as orange/tan rectangles at 22 px row pitch.** FluentTheme's `CheckBox` template has a `~20 px` inner Border element that doesn't scale cleanly to 14×14 — squeezing it via style setters produced a malformed rounded rectangle, no glyph. Replaced the three `DataGridCheckBoxColumn`s (Not Null, Unique, Descending, PK) with `DataGridTextColumn` + a new [BoolToCheckmarkConverter](src/EmberTern.App/Converters/BoolToCheckmarkConverter.cs) returning `✓` for true and `""` for false. Cleaner read for read-only display, inherits row selection/hover colors naturally, matches IBExpert/DataGrip convention. The `DataGridCell CheckBox` style block in `UserControl.Styles` was removed (no longer needed).

**2. Bottom panel (Results/Messages/Output) visible on non-SQL tabs.** Results and Messages only make sense for SQL execution; on a DDL or TableDetail tab they're noise. The workspace grid row defs flipped from `"*,1,280"` to `"*,Auto"`, with the 1px separator + 280px panel wrapped in an inner `Grid RowDefinitions="1,280" IsVisible="{Binding IsQueryTabActive}"`. When the inner Grid hides, the outer Auto row collapses to 0 and the editor area (`*`) expands to fill the full height. Same `IsQueryTabActive` property as the toolbar/Saved-Queries gates.

**3. LoadAsync was monolithic — one failing query wiped the rest.** The original `LoadAsync` had a single outer `try/catch` wrapping the five sequential reads (fields/constraints/indexes/DDL/description). A failure in step 2 (e.g. a Constraints SQL error on a particular FB version) skipped every later step too. And the error message was reported to Messages — which is now hidden on TableDetail tabs (see fix #2). User saw "empty data" with no feedback. Refactored: each step runs through a `SafeLoadAsync` helper that traps `MetadataReadException` independently. First error wins for the tab-level `ErrorMessage`; subsequent steps run regardless. Added an `ErrorMessage` TextBlock inside the TableDetail view (next to the loading hint) so errors are visible even with Messages hidden.

**4. Multiple restored TableDetail tabs raced against the single FbConnection — only the first loaded.** `LoadWorkspaceFor` was fire-and-forgetting `_ = detail.LoadAsync()` for every restored TableDetail tab. With N tabs sharing one connection, the first tab's `BeginTransactionAsync` held it; subsequent tabs threw "Parallel transactions" and the catch swallowed them. Fix: **lazy-load on first activation**. New `EnsureLoadedAsync` entry point on `TableDetailTabViewModel`:

```csharp
private Task? _loadTask;
public Task EnsureLoadedAsync(CancellationToken ct = default)
    => _loadTask ??= LoadAsync(ct);
```

Idempotent — first caller starts the load and stashes the task; subsequent callers join the *same* task. `SelectTab` kicks off `EnsureLoadedAsync` when the newly-activated tab is a TableDetail. The restored-active tab loads automatically (via `SelectTab(WorkspaceTabs[activeIndex])` at the end of `LoadWorkspaceFor`); non-active tabs load when the user clicks them. `OnOpenDdlRequested` (new-tab path) awaits the same task so its post-load `ErrorMessage` check still works. Reset semantics are free: on disconnect/reconnect, `LoadWorkspaceFor` builds *new* VM instances with `_loadTask = null`, so the next activation loads fresh against the new connection.

**5. Opening a TableDetail tab during an active user transaction flooded Messages with "Parallel transactions are not supported".** `FirebirdTableDetailReader` opened its own `ReadCommittedTransactionAsync` for every query. When the user already had a working tx active, this raised on every call. Adopted the **borrow-or-begin** pattern already proven in `FirebirdDdlReader`: new constructor accepts an optional `TransactionService`; new private helper:

```csharp
private async Task<(FbTransaction tx, bool ownsTx)> BorrowOrBeginAsync(FbConnection conn, CancellationToken ct)
{
    var borrowed = _transactionService?.ActiveTransaction;
    if (borrowed is not null) return (borrowed, false);
    return ((FbTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false), true);
}
```

All five methods (Fields/Indexes/Constraints/Description/DataPreview) use the helper and gate `CommitAsync` / `RollbackAsync` / `DisposeAsync` on `ownsTx`. When the user has a working tx, reads piggyback on it; when there's none, the reader owns and tears down its own short-lived tx as before. `MainWindowViewModel` passes `_transactionService` when constructing the reader.

The spec for fix #5 actually said "remove `BeginTransactionAsync` entirely" and "SELECT works without an explicit tx". Pushed back on that — the managed driver rejects commands without an attached transaction on a connection that needs one, and the borrow-or-begin pattern is already standardized in `FirebirdDdlReader`. Consistency over the suggested shortcut.

**Gotchas promoted to architecture lore.**

22. **Borrow-or-begin transaction pattern for any reader on a shared `FbConnection`.** Firebird/managed driver disallows parallel transactions on one connection. Any reader that opens its own `BeginTransactionAsync` will throw on every call once a user transaction goes active. The fix is to optionally accept a `TransactionService`, check `ActiveTransaction`, and reuse it when present — only start your own when there's no active user tx, and only commit/dispose what you own. `FirebirdDdlReader` and now `FirebirdTableDetailReader` both follow this; `FirebirdMetadataReader.ListAsync` / `ListColumnsAsync` still don't — they're called only during sidebar load (no user-tx pressure today), but should adopt the same pattern if that changes.

23. **`EnsureLoadedAsync` + `_loadTask` for idempotent lazy load.** When a VM may have its `LoadAsync` triggered by multiple call sites (e.g. tab activation fire-and-forget + an `OnOpenDdlRequested` that awaits the result), they MUST join the same task. The two-line idiom: `private Task? _loadTask; public Task EnsureLoadedAsync(CancellationToken ct = default) => _loadTask ??= LoadAsync(ct);`. First caller starts the load and assigns; later callers get the running/completed task back. Single-thread UI context means no race on `??=` assignment. Reset is free: throw away the VM instance (e.g. on reconnect) and the new one starts fresh.

24. **Per-step error isolation in any multi-step `LoadAsync`.** When `LoadAsync` runs N sequential queries against the same connection, a single outer `try/catch` is wrong: a failure in step 2 silently kills steps 3-N too. Wrap each step in its own try/catch (`SafeLoadAsync` helper in `TableDetailTabViewModel`) that traps the expected exception type, accumulates the first error into a tab-level `ErrorMessage`, and lets the rest proceed. Compound with **surface errors in the view itself** when an out-of-band error channel (e.g. the Messages tab) might be hidden on the current tab kind — otherwise the user sees blank data with no explanation.

25. **Toolbar + bottom panel + Saved Queries panel visibility ALL gate on a small set of computed VM properties.** `IsQueryTabActive` (true when `SelectedWorkspaceTab.Kind == Query`) is the master switch for SQL-execution chrome — Execute / Cancel / Commit / Rollback / Format / New / Toggle panel / Clear, plus the transaction bar, the Saved Queries panel (combined as `ShowQueryPanel = IsQueryPanelVisible && IsQueryTabActive`), and the bottom Results/Messages/Output panel (via the inner-wrapper `IsVisible`). `IsClosableTabActive` (DDL or TableDetail) gates the Close-tab button. All of these notify off `_selectedWorkspaceTab`'s `NotifyPropertyChangedFor` chain — adding a new tab-kind-aware control means adding the property to that chain, not a new event or subscription.

### Connection-wide TransactionGate (shipped)

Bug surfaced after disconnect → reconnect: opening a TableDetail tab while another was loading (or pressing F5 in the SQL Editor mid-load) flooded Messages with "A transaction is currently active. Parallel transactions are not supported." even though the status bar showed Idle.

**Root cause.** The borrow-or-begin pattern (gotcha #22) only checks `_transactionService?.ActiveTransaction` — but a *reader-owned* tx is invisible to `TransactionService`. So:

- TableDetail load owns its own tx (TransactionService Idle, status bar shows "No transaction").
- User clicks another restored TableDetail tab → its `SelectTab` fires `EnsureLoadedAsync` fire-and-forget → its `BorrowOrBeginAsync` sees TransactionService Idle → calls `connection.BeginTransactionAsync(...)` → **second concurrent tx on the same `FbConnection`** → `FbException` "Parallel transactions are not supported" on every subsequent reader call until the first load finishes.
- Same race against the F5 executor path (`TransactionService.BeginTransactionAsync` calls `connection.BeginTransactionAsync` without checking for in-flight reader-owned txs).

Pre-reconnect this was rare because `OnOpenDdlRequested` `await`s the load before yielding control. Post-reconnect, `LoadWorkspaceFor` kicks off the active tab's load fire-and-forget at `SelectTab(WorkspaceTabs[activeIndex])`, leaving the user free to click around while the background tx is alive.

**Fix.** A connection-wide `SemaphoreSlim TransactionGate` in [FirebirdConnectionService.cs](src/EmberTern.Firebird/FirebirdConnectionService.cs). Guards every `Begin` against concurrent `Begin`s — borrow path is unchanged (no gate needed, multiple borrowers freely share the user's tx).

- **Owned reader tx** ([FirebirdTableDetailReader.cs](src/EmberTern.Firebird/FirebirdTableDetailReader.cs) + [FirebirdDdlReader.cs](src/EmberTern.Firebird/FirebirdDdlReader.cs)): `BorrowOrBeginAsync` acquires the gate before `Begin`, releases on the owned-tx commit/dispose path (in each method's `finally`). Borrowed path: no gate.
- **User tx** (`TransactionService.BeginTransactionAsync`): acquires the gate around the `Begin`, releases immediately after (no need to hold for the user-tx duration — subsequent readers will see `ActiveTransaction != null` and Borrow instead of Begin).

**Re-check on acquire**: after `WaitAsync`, re-read `_transactionService?.ActiveTransaction` — between queuing and acquiring, the user may have begun a tx via F5. If so, release the gate and borrow.

**Why not just keep the borrow-or-begin pattern unchanged?** Per the bug owner's explicit ask. The gate composes with the pattern — it doesn't replace it. Without the gate, borrow-or-begin only handles user-vs-reader races; with the gate, reader-vs-reader and reader-vs-executor races close too.

**Gotchas — promote to architecture lore.**

26. **Borrow-or-begin alone is not enough — concurrent owned-tx Begins still collide.** The original gotcha #22 covers user-tx-vs-reader-tx, but two readers racing each other (or a reader racing the F5 executor) still hit "Parallel transactions are not supported" because neither one knows the other has an owned tx in flight (`TransactionService.ActiveTransaction` stays null for reader-owned txs by design — we don't want metadata browsing to bump the user's working-tx state). The fix is a `SemaphoreSlim` on `FirebirdConnectionService` that gates every `BeginTransactionAsync` (whether owned by a reader or by `TransactionService`). Held by the owner across the tx's lifetime; borrowers don't acquire it. **Rule**: any future `BeginTransactionAsync` caller on the shared `FbConnection` MUST go through `_connectionService.TransactionGate` — borrow-or-begin without the gate is a latent race.

27. **Fire-and-forget background work from `SelectTab` is a liability after reconnect.** `LoadWorkspaceFor` kicks off `EnsureLoadedAsync` on the active TableDetail tab via `_ = detail.EnsureLoadedAsync()`. The user sees the tab is selected, doesn't realize a load is running in the background, and clicks something else. Anything that needs the connection (another TableDetail, F5, even a DDL view) hits the race. The TransactionGate (gotcha #26) is the *correct* fix for this — fire-and-forget itself is fine as a UX pattern, but the underlying resource access must be serialization-safe. Don't reach for "await before yielding to UI" — UI thread can't await without freezing. Lock the resource, not the call site.

### SQL autocomplete polish — case-preserving + rich display (shipped)

Two follow-ups on the autocomplete experience.

**1. Case-preserving completion** ([CaseMatcher.cs](src/EmberTern.Core/Sql/CaseMatcher.cs)). New pure helper in `EmberTern.Core.Sql`: `CaseMatcher.Match(typedPrefix, candidate)` — all-lowercase prefix → lowercase candidate, all-uppercase → uppercase, mixed/empty/digits-or-underscores-only → catalog form verbatim. Applied in `SqlCompletionData.Complete`: read the typed text via `textArea.Document.GetText(completionSegment)` and feed it through `CaseMatcher.Match(typed, Text)` before `Document.Replace`. The completion list still stores names in the original RDB$ casing — transformation happens only at insertion time, so the CompletionList's prefix filter doesn't get confused. Picking `NAGL_TABLE_NAME` after typing "nagl" now inserts `nagl_table_name` (matches IBExpert / DataGrip with the lowercase-everywhere preset).

**2. Richer two-column display** ([SqlCompletionData.cs](src/EmberTern.App/Completion/SqlCompletionData.cs)). `Content` is now a 2-column `Grid` (`90,*`) instead of a bare string:
- **Columns**: left column `"Field"` (Opacity 0.6), right column `"NAME : TYPE"` (e.g. `ID_NAGL : INTEGER`, `NAZWA : VARCHAR(50)`).
- **Schema objects / keywords**: left column the kind label (`Table`, `View`, `Procedure`, `Trigger`, `Function`, …, `Keyword`), right column the name verbatim.

Layout matches the IBExpert dropdown in the user's reference screenshots. The 90px fixed-width left column means all entries align cleanly. Opacity instead of a themed brush keeps both light + dark legible without extra resource keys; entry text color flows through FluentTheme's `CompletionList` default (no hardcoded colors).

**Column type pipeline.** Required getting the formatted SQL type ("INTEGER", "VARCHAR(50)", "NUMERIC(15,2)") down to the completion data:

- New `ColumnSpec(string Name, string Type)` record in [Core/Metadata/ColumnSpec.cs](src/EmberTern.Core/Metadata/ColumnSpec.cs).
- `FirebirdMetadataReader.ListColumnsAsync` returns `IReadOnlyList<ColumnSpec>` (was `IReadOnlyList<string>`). New `ColumnsSql` constant joins `RDB$RELATION_FIELDS` ↔ `RDB$FIELDS` (same shape as TableDetail) and reuses `FirebirdTableDetailReader.FormatFieldType` (internal access within the `EmberTern.Firebird` assembly — no public-surface change needed). Same short-lived ReadCommitted tx pattern as before.
- `MainWindowViewModel._columnCache` retyped to `Dictionary<string, IReadOnlyList<ColumnSpec>>`; `TryGetCachedColumns` / `EnsureColumnsAsync` signatures follow.
- `SqlCompletionController._cachedColumnsProvider` / `_ensureColumnsAsync` callbacks retyped accordingly; `ShowWindowWithColumns` passes `col.Name` + `col.Type` into the new `SqlCompletionData` ctor param `columnType`.
- `MainWindow.axaml.cs` callback bodies updated; the rest of the wiring is unchanged.

The inserted `Text` is still just the name — the rich Content is display-only.

**Tests** — 11 new in [CaseMatcherTests.cs](tests/EmberTern.Tests/CaseMatcherTests.cs) covering lower / upper / mixed / empty / null prefixes, digits+underscores only (no signal → verbatim), single-letter prefix, empty candidate. **330 / 330 green** (319 → 330, +11 new). Build clean (zero warnings, TWAE on). Smoke-verified: app launches and runs 8 seconds without crashing.

**Gotchas — promote to architecture lore.**

28. **AvaloniaEdit `ICompletionData.Content` accepts any `Control`, not just strings.** The API name suggests Content should be text. It's actually placed inside a `ContentPresenter`, so a full `Grid` (or any other Avalonia control) renders fine. This is the cleanest path to multi-column IBExpert-style completion entries — no custom template, no custom item container, no per-language theming. **Rule**: when an Avalonia (or AvaloniaEdit) API takes `object Content`, look at the host control — if it ends up in a `ContentPresenter`, you can pass a built-up control tree directly.

29. **Case-matching at insertion time, not at display/sort time.** Tempting to lowercase the entire completion list on display when the user's prefix is lowercase. Don't — the prefix can change between keystrokes, and CompletionList's prefix-filter would re-run incorrectly if `Content`/display text and `Text` (insertion) drift apart. Keep names in catalog form for both the dropdown and filtering; transform only the inserted text in `ICompletionData.Complete`, reading the typed prefix via `textArea.Document.GetText(completionSegment)` (the segment passed in matches `StartOffset..EndOffset` the controller set on the `CompletionWindow`). The pure transformation lives in `EmberTern.Core.Sql.CaseMatcher` so it's unit-testable without AvaloniaEdit.

### SQL highlighter — richer per-category palette (shipped)

Split the single bold "Keyword" color into separate categories matching VS Code Dark+/Light+:

- **DML keywords** (SELECT/FROM/WHERE/JOIN/AND/OR/NULL/AS/UNION/...) — blue (dark `#569CD6`, light `#0000FF`), bold.
- **DML-action + DDL keywords** (INSERT/UPDATE/DELETE/MERGE/INTO/VALUES/SET/CREATE/ALTER/DROP/TABLE/VIEW/PROCEDURE/BEGIN/IF/DO/FOR/EXIT/SUSPEND/...) — purple (dark `#C586C0`, light `#AF00DB`), bold.
- **Data types** — teal (`#4EC9B0` / `#267F99`).
- **Built-in functions** (CAST/COALESCE/IIF/CASE/WHEN/THEN/ELSE/END/COUNT/SUM/MIN/MAX/EXTRACT/ROUND/UPPER/LOWER/TRIM/CURRENT_*/OVER/PARTITION/ROW_NUMBER/...) — yellow/gold (`#DCDCAA` / `#795E26`).

Numbers, strings, comments unchanged. Operators dropped to subtle grey (`#D4D4D4` / `#666666`).

Both [FirebirdSql.xshd](src/EmberTern.App/Assets/FirebirdSql.xshd) and [FirebirdSql.Light.xshd](src/EmberTern.App/Assets/FirebirdSql.Light.xshd) updated 1:1 — same Keywords blocks, same ordering, only colors differ. Theme-switch path (`ApplyEditorThemeColors` in `MainWindow.axaml.cs`) unchanged. No C# logic touched.

**Ordering decision.** Functions block declared FIRST so CASE/WHEN/THEN/ELSE/END resolve as functions (also valid PSQL control-flow tokens, but reading them as expression keywords matches IBExpert/DataGrip and is more useful for daily query work). LEFT/RIGHT stay in DML keywords (blue) since `LEFT JOIN` is far more common than the `LEFT(str, n)` function in this codebase's daily use.

**Tests**: 330 / 330 still green (visual only — no test changes). Build clean (zero warnings, TWAE on).

**Gotcha promoted to architecture lore.**

30. **XSHD `<Keywords>` blocks are precedence-ordered — first declared wins.** When the same word appears in multiple Keywords blocks (e.g. CASE in both Function and DDL lists), the *first* block to match wins. AvaloniaEdit doesn't merge; it short-circuits on first hit. **Rule**: when splitting a single "Keyword" category into per-color categories (DML/DDL/Function/...), put the higher-priority category first in the XSHD file. Tidiest to keep each word in exactly one block, but later-block duplicates simply don't fire — no error.

### Architecture correction — readers never open their own transaction (shipped)

Significant architecture correction over the prior three milestones (borrow-or-begin in [#22](), TransactionGate in [#26](), fire-and-forget-needs-gate in [#27]()). The whole borrow-or-begin pattern + connection-wide semaphore was overengineered around a flawed premise — that every command needs an explicit transaction. The Firebird managed driver (`FirebirdSql.Data.FirebirdClient` 10.3.4) creates an **implicit, auto-committed read transaction per command** when `FbCommand.Transaction == null` and the connection has no user-level pending tx. That's exactly the right semantics for read-only metadata browsing.

**The new rule.** Readers (`FirebirdMetadataReader`, `FirebirdDdlReader`, `FirebirdTableDetailReader`) set:

```csharp
cmd.Transaction = _transactionService?.ActiveTransaction;
```

— and nothing else. No `BeginTransactionAsync`, no `CommitAsync`, no `RollbackAsync`, no `DisposeAsync`, no gate. When the user has a working tx active, `cmd.Transaction` is non-null and we attach. When they don't, it's null and the driver runs the SELECT in an implicit per-command tx.

**What got deleted.**
- [FirebirdTableDetailReader.cs](src/EmberTern.Firebird/FirebirdTableDetailReader.cs) — `BorrowOrBeginAsync` + `ReleaseOwnedGate` helpers and the matching `try/commit/finally/Dispose+Release` shell from all five methods (Fields/Indexes/Constraints/Description/DataPreview).
- [FirebirdDdlReader.cs](src/EmberTern.Firebird/FirebirdDdlReader.cs) — the 30-line `borrowed/ownsTransaction/gate.Wait/re-check/Begin/Release` block at the top of `FetchDdlAsync`. Inner method signatures changed from `FbTransaction tx` → `FbTransaction? tx` so they accept a null tx and forward it to `cmd.Transaction`.
- [FirebirdMetadataReader.cs](src/EmberTern.Firebird/FirebirdMetadataReader.cs) — was the worst case: it opened its own tx unconditionally, never even reaching for borrow. Now takes an optional `TransactionService` ctor param (matching the other readers) and uses the same attach-or-null pattern in `ListAsync` + `ListColumnsAsync`. Construction site in [MainWindowViewModel.cs:61](src/EmberTern.App/ViewModels/MainWindowViewModel.cs) updated to pass `_transactionService`.
- [FirebirdConnectionService.cs](src/EmberTern.Firebird/FirebirdConnectionService.cs) — `_transactionGate` field + `TransactionGate` property gone.
- [TransactionService.cs](src/EmberTern.Firebird/TransactionService.cs) — `TransactionGate.WaitAsync/Release` removed from `BeginTransactionAsync`. User transactions are inherently sequential (the user clicks Begin manually) and there is now no other Begin caller competing on the connection.

**The only legitimate Begin callers on the shared `FbConnection` now are**:
1. `TransactionService.BeginTransactionAsync` — user-initiated working tx.
2. `FirebirdQueryExecutor.ExecuteAsync` — but only by way of `TransactionService.BeginTransactionAsync` (the auto-begin path); it doesn't call `connection.BeginTransactionAsync` directly.

**Eliminates the entire class of "Parallel transactions are not supported" errors** that motivated the prior three milestones — readers no longer compete for transaction slots because they don't ask for one. Two TableDetail tabs lazy-loading after reconnect is fine. F5 mid-load is fine. The user pressing Begin Transaction while a DDL view is fetching is fine.

**Tests** — 330 / 330 still green. The test suite has no live-FB readers and the readers still throw `MetadataReadException` on `FbException`, so xUnit assertions on shape and helpers all pass unchanged. The shape change (no own tx) is verifiable only at runtime against a real Firebird; smoke against the user's FB 5 schema should now show: open TableDetail during active tx → no flood; reconnect + lazy-load multiple tabs → no race; F5 mid-load → no parallel-tx error.

**Gotchas promoted to architecture lore.** These supersede the now-wrong #22, #26, #27.

22 (revised). **Readers must never open their own transaction on the shared `FbConnection`.** Set `cmd.Transaction = _transactionService?.ActiveTransaction` and nothing else. When the user has a working tx, we attach to it. When they don't, the managed driver runs the SELECT in an implicit auto-committed read tx per command. The "Parallel transactions are not supported" error happens specifically when one Begin is in flight while a second Begin starts on the same connection — eliminate the second Begin and the error class disappears.

26 (revised). **`SemaphoreSlim`/gate on `FbConnection` is a code smell.** If you're reaching for a connection-wide lock around `BeginTransactionAsync` to suppress "Parallel transactions are not supported", you're working around an upstream design issue — almost certainly a path that's opening a tx it shouldn't. Look for the unnecessary Begin first. The driver's command-level serialization plus implicit per-command txs handles read-only access without any explicit synchronization.

27 (revised). **Fire-and-forget background work from `SelectTab` is fine** *as long as command execution is serialized at the connection level* — see gotcha #31. Without reader-owned transactions, the only shared resource is the `FbConnection` itself, which the managed driver does not protect against concurrent commands. Serialize commands explicitly (we use `FirebirdConnectionService.CommandLock`) and lazy-load / fire-and-forget paths are safe.

### CommandLock — serialize all FbConnection commands (shipped)

Follow-up to the "readers never open their own transaction" milestone. After that shipped, smoke against the user's FB 5 schema exposed a hang: metadata tree's eager-load completes Tables (showing count 2356) and then `Views.ListAsync` hangs at "Loading…" indefinitely, with subsequent categories never starting.

**Diagnosis.** The hang isn't from `Task.WhenAll` (eager-load is sequential via `foreach await` in `ConnectionNodeViewModel.LoadCategoriesAsync`). It's from independent fire-and-forget paths that all hit the same `FbConnection`:

- `MetadataNodeViewModel.OnIsExpandedChanged` user-click → `_ = _owner.LoadGroupAsync(this)`
- SQL editor autocomplete `.` keystroke → `_ = EnsureColumnsAsync(tableName)`
- DDL fetch on double-click → `OnOpenDdlRequested` → `await _ddlReader.FetchDdlAsync(obj)`
- TableDetail `EnsureLoadedAsync` lazy-load on tab activate, also fire-and-forget from `LoadWorkspaceFor`
- `FirebirdQueryExecutor.ExecuteAsync` on F5

Any two of these running concurrently against the single `FbConnection` either throws ("There is already an open DataReader") or hangs (driver waiting for the previous command's reader/implicit-tx to release the connection). The managed `FirebirdSql.Data.FirebirdClient` driver does NOT internally serialize concurrent commands — that's the application's job.

**Fix.** A connection-wide `SemaphoreSlim CommandLock` on [FirebirdConnectionService.cs](src/EmberTern.Firebird/FirebirdConnectionService.cs). Every reader / executor / `TransactionService` lifecycle operation wraps its command body in:

```csharp
await _connectionService.CommandLock.WaitAsync(ct).ConfigureAwait(false);
try { /* cmd.ExecuteReaderAsync, read rows, etc. */ }
finally { _connectionService.CommandLock.Release(); }
```

**Naming choice — `CommandLock`, not `TransactionGate`.** Different semantics, different scope. The removed `TransactionGate` (gotcha #26) gated only `BeginTransactionAsync` calls; the new `CommandLock` gates **command execution end-to-end**. The TransactionGate would not have prevented the hang (the readers no longer call `BeginTransactionAsync` after the prior milestone; the hang is purely from concurrent `cmd.ExecuteReaderAsync` calls).

**Where it's held**:
- `FirebirdMetadataReader.ListAsync` + `ListColumnsAsync` — around the whole command body.
- `FirebirdDdlReader.FetchDdlAsync` — across the entire DDL build (some kinds issue 3+ commands serially — table builder reads RDB$RELATION_FIELDS, then constraints, then indexes — all must hold the lock).
- `FirebirdTableDetailReader.GetFieldsAsync` / `GetIndexesAsync` / `GetConstraintsAsync` / `GetDescriptionAsync` / `GetDataPreviewAsync` — per-method.
- `FirebirdQueryExecutor.ExecuteAsync` — around the command body. The auto-begin path (when no user tx is active) calls `_transactionService.BeginTransactionAsync()` BEFORE acquiring the executor's own lock — that Begin acquires/releases the lock itself, no deadlock.
- `TransactionService.BeginTransactionAsync` + `CommitAsync` + `RollbackAsync` — all three send wire messages to the server.

**Lock-not-held detail** for `FirebirdQueryExecutor.ExecuteAsync` + `TransactionService.BeginTransactionAsync` — `RequireOpenConnection()` throws `InvalidOperationException` BEFORE the WaitAsync (test pins this). In the executor that exception must be caught and rewrapped as `QueryExecutionException`, so the try/finally is structured with a `bool lockHeld` flag to avoid releasing a never-acquired lock in the finally. In the TransactionService that exception is the test contract (test expects raw `InvalidOperationException`), so RequireOpenConnection sits outside the try — no flag needed.

**Tests**: 330 / 330 still green. The lock is a no-op on a single-threaded test (xUnit always takes it first try). Build clean.

**Gotcha promoted to architecture lore.**

31. **`FbConnection` is single-threaded — application MUST serialize commands.** The managed `FirebirdSql.Data.FirebirdClient` driver does NOT internally serialize concurrent `ExecuteReaderAsync` calls on the same connection. Two fire-and-forget paths firing simultaneously will either throw "There is already an open DataReader associated with this Connection which must be closed first" or hang waiting on the previous command's implicit-tx commit. The fix is a connection-wide `SemaphoreSlim` (we expose `FirebirdConnectionService.CommandLock`) acquired around every command body. Every reader, the query executor, and `TransactionService.BeginTransactionAsync/CommitAsync/RollbackAsync` must hold it for their whole wire-touching operation. **This is independent of transactions** — even with the "readers attach to user tx or run with implicit per-command tx" pattern from the prior milestone, you still need the lock because the driver doesn't queue concurrent commands. **Rule**: any new code that does `await cmd.ExecuteReaderAsync` / `ExecuteScalarAsync` / `ExecuteNonQueryAsync` / `connection.BeginTransactionAsync` / `tx.CommitAsync` / `tx.RollbackAsync` on the shared connection MUST acquire `_connectionService.CommandLock` first.

### Saved-query rename + Connection folders (shipped)

Two UX features that ship together. **Tests: 351 / 351 green** (330 → 351, +21).

**1. Inline rename for saved queries.** Double-click the name in the right-side Saved Queries panel → it swaps for a focused, all-selected TextBox. Enter / LostFocus commits, Escape cancels. Blank input is rejected (keeps old name). Trim is applied.

- [SavedQueryViewModel.cs](src/EmberTern.App/ViewModels/SavedQueryViewModel.cs) gained `IsRenaming` + `IsNotRenaming` (computed) + `EditingName`, and `BeginRename` / `CommitRename` / `CancelRename` commands. Commit writes `EditingName.Trim()` back into `Name`; persistence is automatic via the existing close-time `CaptureWorkspace` path — no live save, same mechanism as SqlText edits.
- ListBox item template now wraps a TextBlock + TextBox in a `Panel`; visibility flips off `IsRenaming` / `IsNotRenaming`. TextBox carries `behaviors:FocusBehavior.FocusOnVisible="True"` for auto-focus.
- Code-behind: `OnSavedQueryNameDoubleTapped` → BeginRename; `OnRenameTextBoxLostFocus` → CommitRename (guarded by `sq.IsRenaming` so double-fire from Enter+focus-shift is a no-op); `OnRenameTextBoxKeyDown` → Enter commits, Escape cancels.

**2. Connection folders in the sidebar.** Each saved profile can live in a named folder (depth 1 — folders don't nest). Folder button (`📁`) in the titlebar toolbar opens a small NewFolderDialog. Right-click on a folder gives Zmień nazwę / Usuń katalog. Right-click on a connection gains a Sortuj węzły → Rosnąco (A→Z) / Malejąco (Z→A) submenu. Sort affects siblings — folder members or root-level mixed (folders + connections together).

**Core layer** (zero Avalonia deps):
- [FolderEntry.cs](src/EmberTern.Core/Connections/FolderEntry.cs) — `Id` (GUID), `Name`, `SortOrder`.
- [FolderStore.cs](src/EmberTern.Core/Connections/FolderStore.cs) — JSON store at `%AppData%\EmberTern\folders.json`. Mirror of `ConnectionProfileStore` / `WorkspaceStore`: graceful Load returns empty state on missing / corrupt / unreadable file. `FolderState` carries `List<FolderEntry> Folders`, `Dictionary<string,string> ConnectionFolderMap` (connectionId → folderId; absent = root), and `Dictionary<string,int> ConnectionSortOrders` (per-connection sort key).

**Why a separate file and not extending `connections.json`?** Keeping `connections.json` as `List<ConnectionProfile>` preserves forward compat with older builds and avoids a JSON schema migration. Folder layout is metadata *about* connections, not part of the connection itself.

**App layer**:
- [FolderNodeViewModel.cs](src/EmberTern.App/ViewModels/FolderNodeViewModel.cs) — same inline-rename surface as `SavedQueryViewModel` (`IsRenaming` / `EditingName` / Begin/Commit/Cancel). Holds `ObservableCollection<ConnectionNodeViewModel> Connections`. Commit calls `_owner.PersistFolderState()` directly (no debounce, no batching — folder edits are rare).
- [ConnectionNodeViewModel.cs](src/EmberTern.App/ViewModels/ConnectionNodeViewModel.cs) gained `SortAscendingCommand` / `SortDescendingCommand` that delegate to `_owner.SortSiblingsOf(this, ascending)`.
- [MetadataExplorerViewModel.cs](src/EmberTern.App/ViewModels/MetadataExplorerViewModel.cs) — kept `Connections` (flat list of all `ConnectionNodeViewModel` instances; iterated by `ApplyFilter` / `RefreshAsync` / `EnumerateLoadedObjects`) and added a parallel `RootNodes : ObservableCollection<object>` for the tree's `ItemsSource`. Mixed types (folder VMs + root-level connection VMs) handled by `TreeView.DataTemplates` keyed on `DataType`.
- [MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs) — added `FolderStore` + `FolderState` fields. Rewrote `ReloadConnections()` to (a) detach old nodes, (b) drop stale folder-map / sort entries for deleted profiles, (c) build folder VMs in `SortOrder` order, (d) slot each connection into its folder or the root list, (e) within each folder sort by SortOrder then Name, (f) at the root sort folders + root connections together by SortOrder then Name. New public methods: `CreateFolder(name)`, `DeleteFolderAsync(FolderNodeViewModel)` (confirm via existing `ConfirmDialog`), `PersistFolderState()`, `SortSiblingsOf(node, ascending)`.

**Sort algorithm.** When the pivot is a folder member → sort only that folder's connections by Name (case-insensitive, current culture), write back the index into `ConnectionSortOrders`. When the pivot is a root connection → sort `Folders + root connections` together by Name, write back into `FolderEntry.SortOrder` (for folders) or `ConnectionSortOrders` (for connections). Descending = reverse after sort. Always followed by `PersistFolderState()` + `ReloadConnections()` (the second rebuilds the tree in the new order).

**XAML wiring**:
- New `xmlns:behaviors="using:EmberTern.App.Behaviors"`.
- TreeView's `ItemsSource` flipped from `Connections` → `RootNodes`.
- New `TreeDataTemplate DataType="vm:FolderNodeViewModel"` with `ItemsSource="{Binding Connections}"`. Folder name is a TextBlock when `IsNotRenaming`, a TextBox when `IsRenaming` — same Panel-overlay trick as Saved Queries. Context menu: Zmień nazwę / Usuń katalog.
- Connection template gained a Separator + `Sortuj węzły` submenu with two children bound to `SortAscendingCommand` / `SortDescendingCommand`.
- New `Style Selector="TreeViewItem" x:DataType="vm:FolderNodeViewModel"` for TwoWay `IsExpanded` (folders default to expanded; user can collapse).
- Titlebar toolbar gained a folder button (`📁`) right after the existing `+` connection button, calling `OnNewFolderClick`.

**Dialog**: [NewFolderDialog](src/EmberTern.App/Views/NewFolderDialog.axaml) is a tiny window — TextBox + OK + Cancel. `ShowDialog<string?>` returns the trimmed name or null. Auto-focus + select-all on Opened so the user just types.

**Behaviors**: new [FocusBehavior.cs](src/EmberTern.App/Behaviors/FocusBehavior.cs) attached property `FocusOnVisible`. When the property is true on a Control, subscribes to `Visual.IsVisibleProperty` changes; when it flips to visible, dispatches Focus + (for TextBox) SelectAll at Background priority so layout settles first. Used by both Feature 1 (saved-query rename) and Feature 2 (folder rename) — the rename TextBox is always in the visual tree and only `IsVisible`-toggled by the binding, so a one-shot Loaded / AttachedToVisualTree event won't fire on each rename.

**Tests** ([FolderStoreTests.cs](tests/EmberTern.Tests/FolderStoreTests.cs) +5, [FolderVmTests.cs](tests/EmberTern.Tests/FolderVmTests.cs) +11, [SavedQueryVmTests.cs](tests/EmberTern.Tests/SavedQueryVmTests.cs) +5): FolderStore Load on missing / empty / corrupt → empty state; round-trip preserves folders + connection map + sort orders; FolderEntry default Id is unique. VM tests: CreateFolder persists + appears in RootNodes; blank name → default; reload places mapped connections into folders + others at root; DeleteFolder moves children back to root; rename persists; blank rename keeps name; sort asc / desc at root mixes folders + connections by name; sort inside folder only affects that folder; sort persists across fresh VM instance; deleting a connection drops its stale folder mapping. SavedQuery rename: Begin seeds EditingName + flips flag; Commit applies trimmed name; blank input keeps original; Cancel reverts without mutating; renamed name survives CaptureWorkspace.

**Auto-confirm pattern in tests.** `_main.ConfirmationRequested += _ => Task.FromResult(true);` short-circuits the modal dialog so `DeleteFolderAsync` can be tested without UI. Same shape as the existing delete-saved-query path.

**Gotchas — promote to architecture lore.**

32. **Avalonia 12 `IsVisible`-toggled controls don't get a fresh `AttachedToVisualTree` event each time.** When a TextBox lives in a Panel and visibility flips via `IsVisible="{Binding IsRenaming}"`, the control stays in the visual tree the whole time. `Loaded` / `AttachedToVisualTree` fire only once. To focus on every show, you need to subscribe to `Visual.IsVisibleProperty` changes — what `Behaviors/FocusBehavior.cs` does. **Rule**: for any "focus when this becomes visible" pattern, prefer the attached behavior (`FocusBehavior.FocusOnVisible`) over wiring `Loaded` events — `Loaded` fires once per control lifetime, not once per show.

33. **Mixed-type TreeView (folders + connections at the same level) requires `TreeView.DataTemplates`, not `TreeView.ItemTemplate`.** Same gotcha as Explorer Redesign gotcha #4 — setting `ItemTemplate` to one of the two templates breaks template lookup for the other type at every nesting level. Both templates go in `DataTemplates`, keyed on `DataType`. The root-level `ObservableCollection<object>` happily holds either type; the tree picks the right template per item.

34. **`ObservableCollection<object>` for mixed-type tree roots is the cleanest Avalonia idiom.** Tried briefly with a common interface (same approach that bit gotcha #3 for `TreeViewItem.IsExpanded` setters), but compiled bindings on the parent collection don't need to know the item type — Avalonia resolves templates by DataType at runtime. `object` keeps the collection type-agnostic; concrete `x:DataType` on each TreeDataTemplate handles the per-item compile-time binding contract.

### Folders + saved queries follow-up fixes (shipped)

Three follow-ups on the prior milestone. **Tests: 357 / 357 green** (+6).

**1. Saved-query rename — full-row hit area + right-click + hover affordance.** The double-click handler moved from the bare `TextBlock` to a wrapper `Border HorizontalAlignment="Stretch" Background="Transparent"` so the whole row receives the gesture. Right-click anywhere on the row now opens a `ContextMenu` with "Zmień nazwę" / "Usuń". A hover-visible `✎` icon button sits in the right column of a `Grid ColumnDefinitions="*,Auto"`; styled via `Button.row-hover-icon { Opacity 0 }` + `ListBoxItem:pointerover Button.row-hover-icon { Opacity 1 }` (scoped under `ListBox.Styles`). Both context menu items and the hover button delegate to commands on `SavedQueryViewModel`.

[SavedQueryViewModel.cs](src/EmberTern.App/ViewModels/SavedQueryViewModel.cs) gained an optional `MainWindowViewModel? _owner` ctor parameter and a new `Delete` `[RelayCommand]` that calls `_owner?.DeleteSavedQueryAsync(this) ?? Task.CompletedTask` — same shape as `ConnectionNodeViewModel.Delete`. `DeleteSavedQueryAsync(sq)` is the public entry on the main VM; `DeleteSelectedQueryAsync` now just thin-wraps it.

**2. Add connection to folder.**
- Folder right-click gained "Dodaj połączenie" at the top of the menu. Bound to `FolderNodeViewModel.AddConnectionCommand` which calls `_owner.RequestAddConnectionAsync(Id)`. MainWindowViewModel exposes `event Func<string?, Task>? AddConnectionRequested` + `RequestAddConnectionAsync(folderId)`; the view subscribes in `OnDataContextChanged`, opens `NewConnectionDialog`, and on confirm calls `PlaceConnectionInFolder(profile.Id, folderId)`.
- Titlebar `+` button now detects folder context from the tree: if `SidebarTree.SelectedItem` is a `FolderNodeViewModel` → that folder; if it's a `ConnectionNodeViewModel` whose id is in `ConnectionFolderMap` → the parent folder; otherwise root (legacy behaviour). `DetectFolderContext(vm)` lives in the code-behind.
- New helper `MainWindowViewModel.PlaceConnectionInFolder(profileId, folderId)` mutates `ConnectionFolderMap` (or removes the entry when `folderId is null`), persists, and reloads. Isolated for testability — doesn't require the dialog.

**3. Sort scope (no fix needed — already correct).** The prior milestone's `SortSiblingsOf` already scopes correctly: folder member pivot → only that folder's `ConnectionSortOrders` are updated; root pivot → only `Folders.SortOrder` + root connections' `ConnectionSortOrders`. Added two regression tests pinning this: `SortInsideFolder_LeavesRootOrderUntouched` (root order + root sort-orders + folder.SortOrder all unchanged after a folder sort) and `SortAtRoot_LeavesFolderMembersUntouched` (folder members get no `ConnectionSortOrders` entry from a root sort).

**Tests added (+6).** 2 in [SavedQueryVmTests.cs](tests/EmberTern.Tests/SavedQueryVmTests.cs) — DeleteCommand on a VM with owner routes through `DeleteSavedQueryAsync` and removes; DeleteCommand on a bare VM (owner null) is a no-op (Task.CompletedTask). 4 in [FolderVmTests.cs](tests/EmberTern.Tests/FolderVmTests.cs) — `PlaceConnectionInFolder` maps + persists + reloads tree; `PlaceConnectionInFolder(null)` moves back to root; plus the two sort-scope regression tests above.

**Gotchas — promote to architecture lore.**

35. **Avalonia ListBox hover-visible row actions: `Button.cls { Opacity 0 }` + `ListBoxItem:pointerover Button.cls { Opacity 1 }`.** The hover state cascades from `ListBoxItem` down to descendants, so a Style scoped under `ListBox.Styles` with the two-selector pair makes any tagged button fade in only on the hovered row. No code-behind required, no per-item PointerEntered subscriptions. **Rule**: when adding a row-scoped affordance to an Avalonia ListBox/TreeView, prefer this pattern over `PointerEntered/PointerExited` handlers.

### Tree drag-and-drop (shipped)

Drag-and-drop on the sidebar tree: drag a connection into a folder, reorder connections within a container, reorder folders at root. Pointer-event-driven (not the `DragDrop` API — unreliable for `TreeView` in Avalonia 12: virtualized item containers, drop events fire while the cursor is over children, etc.).

**Core/VM**:
- New [DropPosition.cs](src/EmberTern.App/ViewModels/DropPosition.cs) enum (`Before` / `After` / `Into`).
- [ConnectionNodeViewModel.cs](src/EmberTern.App/ViewModels/ConnectionNodeViewModel.cs) + [FolderNodeViewModel.cs](src/EmberTern.App/ViewModels/FolderNodeViewModel.cs) each gained `[ObservableProperty] IsDragging` + `IsDropTarget`. View handlers drive both — IsDragging follows the grabbed row, IsDropTarget highlights the row under the pointer.
- [MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs) gained `ExecuteDrop(source, target, position)`. Goes through `_folderState` + `PersistFolderState` + `ReloadConnections` — same persistence pattern as `SortSiblingsOf`. Handles: Connection → Folder, Into (membership change + place at end); Connection → Connection, Before/After (may also change folder); Folder → Folder, Before/After (root only); Folder → root Connection, Before/After. Rejects source==target, folder onto folder-member context, into-same-folder.
- Internal `ReorderForDrop` builds the post-move sibling list, sorts, inserts source, then renumbers `FolderEntry.SortOrder` / `ConnectionSortOrders` contiguously 0..N.

**Theme** ([Colors.axaml](src/EmberTern.App/Themes/Colors.axaml)): `DropTargetColor`/`Brush` in both Dark + Light dictionaries — ARGB `#552D6BBF` (~33% accent-blue overlay; alpha-first per gotcha #1).

**View** ([MainWindow.axaml](src/EmberTern.App/Views/MainWindow.axaml) + [MainWindow.axaml.cs](src/EmberTern.App/Views/MainWindow.axaml.cs)):
- `Border.row-target` + `Border.row-target.drop-target` Style selectors inside `TreeView.Styles`. Folder + Connection templates carry `Classes="row-target"` + `Classes.drop-target="{Binding IsDropTarget}"`. The inline `Background="Transparent"` was removed — `Style.Setter` wouldn't beat the LocalValue.
- Tunneled `PointerPressed` (so we see clicks before TreeView's selection handling) + bubbling `PointerMoved`/`PointerReleased`/`PointerCaptureLost` on `SidebarTree`. 8-px threshold to enter drag mode; cursor switches to `DragMove`.
- Hit-test via `TreeView.InputHitTest(point)` + `FindAncestorOfType<TreeViewItem>(includeSelf: true)` → DataContext = row VM.
- Position resolution: folder target → `Into`; connection target → top half = `Before`, bottom half = `After` (computed by translating the `TreeViewItem.Bounds` to tree coordinates via `TranslatePoint`).
- Mid-flight connect/disconnect connection rows are not grabbable (`IsBusyConnection` filter).

**Tests** ([FolderVmTests.cs](tests/EmberTern.Tests/FolderVmTests.cs)): +10 covering connection-into-folder mapping + persistence, into-same-folder no-op, before/after at root, before/after across folder boundary (root → folder and folder → root), folder reorder, folder onto folder-member rejection, source==target no-op, cross-instance persistence round-trip. **369 / 369 green** (359 → 369).

### Tree expand-state persistence + drag-collapse fix (shipped)

Two related features shipped together: after drag/drop (or any `ReloadConnections` path — rename, sort, create/delete folder), the tree no longer collapses; and folder/connection expand state survives app restarts.

Root cause for the drag-collapse: `ExecuteDrop` and `SortSiblingsOf` both call `ReloadConnections()`, which clears `RootNodes` and rebuilds all `FolderNodeViewModel` / `ConnectionNodeViewModel` instances with default `IsExpanded` values. TreeView (correctly) followed the VM defaults and collapsed everything.

**Persistence schema** ([FolderStore.cs](src/EmberTern.Core/Connections/FolderStore.cs)):
- `FolderState.ExpandedNodeIds : HashSet<string>` — presence == expanded, absence == collapsed. Keys are `FolderEntry.Id` or `ConnectionProfile.Id`.
- `FolderState.ExpandStateInitialized : bool` — gates the one-time legacy migration. Absent in older folders.json (deserializes to default `false`).

**Default-state asymmetry.** Folders default to `IsExpanded=true`, connections to `false`. A naive presence-based set would re-expand any folder the user collapses (next launch would see the absent id as "default folder → expanded"). Two reconciliation mechanisms:

1. **`CreateFolder` seeds the new folder's id into the set** so default-expanded survives a restart. User collapses → removed from set → stays collapsed.
2. **Legacy migration in `MaybeMigrateExpandState`** (called from `RestoreExpandState`): if `ExpandStateInitialized` is false, seed the set with every existing `FolderEntry.Id` and flip the flag. From that point on the set is fully authoritative.

**VM wiring**:
- `ConnectionNodeViewModel.OnIsExpandedChanged` and `FolderNodeViewModel.OnIsExpandedChanged` (new partial method in folder VM) call `_owner.OnNodeExpansionChanged(id, value)`.
- `MainWindowViewModel.OnNodeExpansionChanged` mutates `_folderState.ExpandedNodeIds` and persists — guarded by `_suppressExpandSave` so the writes that `RestoreExpandState` itself fires don't echo back as redundant saves.
- `MainWindowViewModel.CaptureExpandState()` syncs the VM tree's current state into the set (testability; the on-change hook keeps them in sync during normal use).
- `MainWindowViewModel.RestoreExpandState()` applies the set verbatim to every node in `Metadata.RootNodes` (and to folder children). Called at the end of every `ReloadConnections`.

**Drag-collapse fix is automatic.** Once the persistence loop is wired, `ReloadConnections` rebuilds + restores in one pass; the user-visible state is preserved across the rebuild. No explicit Capture-before-drag/Restore-after-drag handshake needed.

**Auto-expand on connect** existed pre-Explorer-Redesign but regressed once expand-state persistence shipped (see the dedicated fix below).

**Tests** ([FolderVmTests.cs](tests/EmberTern.Tests/FolderVmTests.cs)): +10 covering new-folder-defaults-to-expanded-and-in-set, folder-collapse-removes-from-set-and-persists, connection-expand-adds-to-set-and-persists, reload-restores-folder-collapse, reload-restores-connection-expand-inside-folder, drag-reload-preserves-expand-state, CaptureExpandState mirrors VM state into set, RestoreExpandState applies set verbatim, RestoreExpandState suppresses saves (on-disk set unchanged by reload), legacy-migration seeds existing folders into set.

**Gotcha promoted to architecture lore.**

36. **`ReloadConnections` is the rebuild-everything boundary — anything in the tree that's user-driven and not derivable from disk state will be lost across it.** The tree is fully rebuilt: `Metadata.Connections` cleared, `Metadata.RootNodes` cleared, new VM instances allocated for every node. Anything held only in VM state (`IsExpanded`, selection, scroll position) goes back to constructor defaults. For state we want to preserve: either thread it through `_folderState` + persist on change + restore at the end of `ReloadConnections` (the pattern used by `ExpandedNodeIds` and `LastActiveConnectionId`), or hold it on the owner (`MainWindowViewModel`) where it doesn't share the tree's lifetime. **Rule**: when adding any new VM-side state on a tree node, decide explicitly which side of the rebuild boundary it lives on — defaults-restored is fine for transient flags (`IsDragging`, `IsDropTarget`), but persistence is mandatory for anything the user spent effort on.

### Auto-expand-on-connect regression fix (shipped)

Expand-state persistence (above) shipped a `RestoreExpandState` that applied the saved set **verbatim** to both folders and connections (`node.IsExpanded = ExpandedNodeIds.Contains(id)`). That, combined with the connect-time expand being set *before* the categories loaded, meant a freshly-connected node could render collapsed. Two-part fix:

**1. `RestoreExpandState` is now asymmetric by node kind** ([MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs)):
- **Connections** → only-set-*true*, never false. Their default is `IsExpanded=false`, so absence from the set already means collapsed — no need to force false. Critically, never forcing false means a just-connected node (auto-expanded, possibly not yet in the set when a concurrent `ReloadConnections` runs) can't be clobbered back to collapsed by a restore pass.
- **Folders** → verbatim (presence ⇒ true, absence ⇒ false). Their default is `IsExpanded=true`, so the *only* way to persist a user collapse is to force false on absence.

**Tests** ([FolderVmTests.cs](tests/EmberTern.Tests/FolderVmTests.cs)): +2 — `RestoreExpandState_DoesNotCollapseExpandedConnectionMissingFromSet` (the regression pin: expanded connection absent from set stays expanded) and `RestoreExpandState_CollapsesFolderMissingFromSet` (folder absent from set is force-collapsed). **381 / 381 green** (379 → 381).

**Gotcha promoted to architecture lore.**

37. **`RestoreExpandState` must treat folders and connections asymmetrically because their `IsExpanded` defaults differ.** Folders default expanded (true), connections default collapsed (false). A single verbatim `IsExpanded = set.Contains(id)` rule looks symmetric but is wrong for connections: it force-collapses any connection not in the set, which clobbers a freshly-connected node that auto-expanded but isn't persisted yet. The correct split: connections only-set-true (absence already == their collapsed default), folders verbatim (force-false needed to persist a collapse against their expanded default). **Rule**: any "apply a persisted boolean to rebuilt VMs" pass must account for each VM type's *default* value of that boolean — only force the non-default direction.

### Auto-expand-on-connect — real root cause: TreeViewItem container-style binding clobber (shipped)

The auto-expand-on-connect bug survived several speculative "timing" fixes (Dispatcher posts, false→true toggles, moving the expand after category load). Those were all wrong — the bug was never timing. Proven with a **headless Avalonia probe** ([ConnectionExpandBindingProbe.cs](tests/EmberTern.Tests/ConnectionExpandBindingProbe.cs)) that builds the real `MainWindow` (real compiled bindings) and inspects the actual container:

```
VM IsExpanded after set      = True    ← VM state changed (hyp "VM not changing" FALSE)
container exists after set   = True    ← container exists  (hyp "container missing" FALSE)
container.IsExpanded after set = False ← binding did NOT propagate  ← THE BUG
```

**Root cause.** `MainWindow.axaml` had three `<Style Selector="TreeViewItem" x:DataType="vm:...">` styles — one each for Connection / Folder / Metadata, each with an `IsExpanded` TwoWay setter. **`x:DataType` does NOT filter a style selector at runtime — it's only a compile-time binding hint.** So all three styles matched *every* `TreeViewItem`, and the last one in document order (the `MetadataNodeViewModel`-typed style) won. For a connection (or folder) container, that style's compiled binding failed to cast the `DataContext` to `MetadataNodeViewModel`, the `IsExpanded` setter resolved to `UnsetValue`, and it **clobbered** the correct Connection-typed setter — so `TreeViewItem.IsExpanded` never received the VM's `true`. Categories expanded only because the metadata style is last and metadata nodes match its cast; folders "looked fine" only because they default to expanded.

**Fix** ([MainWindow.axaml](src/EmberTern.App/Views/MainWindow.axaml)). Replaced the three typed styles with a **single** `<Style Selector="TreeViewItem">` whose setters use `ReflectionBinding` (resolves the property by name at runtime against whatever VM is the `DataContext` — all three node types expose `IsExpanded`; `IsVisible` exists only on `MetadataNodeViewModel`, so its setter carries `FallbackValue=True`). Re-running the probe after the fix: `container.IsExpanded after set = True`. A second probe drives the full connect path (`IsConnected = true`) and confirms the container expands — with **no** Dispatcher posts or toggles in the VM. Those workarounds were removed: `OnIsConnectedChanged` now does a plain synchronous `IsExpanded = true`, and `LoadCategoriesAsync` ends with a plain idempotent `IsExpanded = true` re-assert. The `_suppressExpansionPersist` flag and `ExpandAfterCategoriesLoaded` toggle method are gone.

**Tests** — 2 headless probes in `ConnectionExpandBindingProbe` (binding propagation + end-to-end connect). Required adding `Avalonia.Headless` 12.0.3 to the test project. **383 / 383 green**.

**Gotchas — promoted to architecture lore; gotcha #3 corrected.**

38. **`x:DataType` on a `<Style>` does NOT scope the selector at runtime — it's a compile-time binding-typing hint only.** A `<Style Selector="TreeViewItem" x:DataType="vm:Foo">` matches *every* `TreeViewItem`, not just those whose `DataContext` is `Foo`. Multiple such styles for different VM types therefore all apply to every container, and the last one in document order wins; its compiled binding fails the `DataContext` cast for the non-matching types and clobbers the property with `UnsetValue`. **Rule**: for a container style (e.g. `TreeViewItem`) that must bind a property shared across several `DataContext` types, use ONE style with a `ReflectionBinding` (resolves by name, no cast), not N compiled-binding styles. This *supersedes the original gotcha #3*, whose "use N concrete-typed styles, one per VM type" advice was the actual cause of the auto-expand failure.

39. **When a UI binding "doesn't work", prove which of the three layers is broken before touching timing.** The three layers: (1) VM property actually changing, (2) the target control's binding receiving the change, (3) the control/container existing at all. A headless Avalonia probe (`HeadlessUnitTestSession.StartNew` + build the real window + `Dispatcher.UIThread.RunJobs()` + inspect the live control) isolates which layer fails in seconds, without a live DB or manual clicking. Reach for it instead of stacking `Dispatcher.Post` / property-toggle workarounds — those only ever paper over layer-1/2 confusion and leave the real defect in place.

40. **Pointer-event drag & drop, not the `DragDrop` API, for `TreeView` in Avalonia 12.** The built-in `DragDrop`/`DataObject` API is unreliable on a virtualized `TreeView`: item containers come and go under `VirtualizingStackPanel`, `DragOver`/`Drop` fire while the cursor is over child rows rather than the intended row, and there's no clean drop-position (before/after/into) signal. The working approach (sidebar tree): tunneled `PointerPressed` to record the drag candidate before `TreeView`'s own selection runs, an 8-px movement threshold before entering drag mode, `TreeView.InputHitTest(point)` + `FindAncestorOfType<TreeViewItem>(includeSelf:true)` to resolve the row VM under the pointer each `PointerMoved`, top/bottom-half of the target's translated `Bounds` for Before/After (folders → Into), and `PointerReleased`/`PointerCaptureLost` to commit or cancel. Visual feedback rides VM flags (`IsDragging`/`IsDropTarget`) bound to a Style class, not adorners. **Rule**: reach for pointer events whenever a drag needs per-row position resolution inside a virtualized items control.

### TableDetail Pola — PK / FK / Unique columns + sorting + autosize (shipped)

Fields tab in the TableDetail tab grew from 8 → 14 columns and gained sorting + per-column autosize.

**New columns** ([TableDetail.cs](src/EmberTern.Core/Metadata/TableDetail.cs)): `IsPrimaryKey` / `IsForeignKey` / `IsUnique` (bool) + `Domain` / `Charset` / `ForeignKeyTable` (string?) on `FieldInfo`. Plus computed `BaseTypeName` that strips the parens off `Type` (`"VARCHAR(255)"` → `"VARCHAR"`) so the Type column shows just the base name and Size/Scale live in their own columns. Original `Type` property kept for the underlying full string + tests.

**Reader** ([FirebirdTableDetailReader.cs](src/EmberTern.Firebird/FirebirdTableDetailReader.cs)): `FieldsSql` grew correlated subqueries against `RDB$INDEX_SEGMENTS ⋈ RDB$RELATION_CONSTRAINTS` for PK/FK/UNIQUE counts (one query per kind, filtered by `RDB$CONSTRAINT_TYPE`), a `LEFT JOIN RDB$CHARACTER_SETS` on `cs.RDB$CHARACTER_SET_ID = ft.RDB$CHARACTER_SET_ID`, and a `ROWS 1` subquery resolving FK → referenced table name via `RDB$REF_CONSTRAINTS ⋈ RDB$RELATION_CONSTRAINTS rc_uq`. `NormalizeDomain` helper strips `RDB$xxx` anonymous backing domains (Firebird creates one per inline column definition); only user-defined domain names pass through.

**View** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml)): three `DataGridTemplateColumn`s for PK/FK/UNQ render `Path` shapes (`StreamGeometry` keys `PkIconGeometry` / `FkIconGeometry` / `UnqIconGeometry` defined in `UserControl.Resources`) sized 14×14, `Stretch="Uniform"`, `Fill` from `AccentBrush` / `WarningBrush` / `SubtleForegroundBrush`, gated by `IsVisible` on the corresponding bool. Computed source shown as italic subtle text in its own template column. Domain / FK Tabela / Charset are plain text columns. New `WarningColor`/`WarningBrush` defined in both theme dictionaries of [Colors.axaml](src/EmberTern.App/Themes/Colors.axaml) (`#E8A020` dark, `#C77800` light).

**Header text**: started with "PK" / "FK" / "UNQ" abbreviations — got cut off at default 40 px column width. Iterated through "Primary key" / "Foreign key" / "Unique" with explicit widths, then dropped explicit widths entirely (see autosize below). Headers stored in [UiStrings.cs](src/EmberTern.App/UiStrings.cs).

**Sorting + autosize on all DataGrid columns** in all three TableDetail sub-grids (Fields, Constraints, Indexes):
- `CanUserSortColumns="True"` + `CanUserReorderColumns="True"` on every grid (was `"False"` on Constraints/Indexes).
- Every `DataGridTextColumn` carries an explicit `SortMemberPath="<PropertyName>"` matching its `Binding` path — **mandatory under compiled bindings** (see gotcha #42). Without it, only template columns (which need `SortMemberPath` anyway) sorted; text columns silently no-op'd on header click.
- Explicit `Width="N"` removed from all PK/FK/UNQ/Domain/FK Tabela/Computed/Charset columns. Grid-level `ColumnWidth="Auto"` does the rest — every column sizes to `max(header, all visible cells)`. `MinWidth="60"` kept on the three icon columns so a small filter result can't collapse them below readability.

**Tried double-click-on-gripper auto-fit, gave up.** Avalonia 12.0.3 `DataGrid` has no native equivalent of WPF/Excel's "double-click the column-resize gripper to fit content". Attempted a code-behind handler in `TableDetailTabView`: bubbled `DoubleTappedEvent` on the UserControl → walk to ancestor `Thumb` named `PART_RightHeaderGripper` → reflect on `DataGridColumnHeader.OwningColumn` (internal in 12.0.0) → set `column.Width = DataGridLength.Auto`. Build clean, event fired (verified), but the visual auto-fit didn't happen — the column either re-measured to the same width or the Width assignment was ignored. Removed the code; deferred to a future Avalonia upgrade that exposes column-header double-click natively.

**ConvertBack regression fix** ([ZeroToEmptyConverter.cs](src/EmberTern.App/Converters/ZeroToEmptyConverter.cs) + [BoolToCheckmarkConverter.cs](src/EmberTern.App/Converters/BoolToCheckmarkConverter.cs)): both converters' `ConvertBack` threw `NotSupportedException` (WPF idiom for one-way converters). Opening a restored TableDetail tab tripped Visual Studio's "Break on user-unhandled exception" prompt — Avalonia's binding pipeline catches the throw internally (so the app keeps running outside the debugger) but VS still pauses on the first chance. Changed both to `return BindingOperations.DoNothing` (canonical Avalonia pattern). No more debugger break, behaviour identical at runtime.

**Tests**: 407 / 407 green (no new tests this round — XAML and converter changes). Smoke-verified end-to-end against the FK ERP schema: 22-column ADRES table opens, PK icon on ID_ADRES, FK icons on ID_PRACOWNIK / ID_WOJEWODZTWO / ID_KRAJ with FK Tabela populated, Domain shows T_ID / T_MIEJSCOWOS / T_KODPOCZ etc., Charset shows WIN1250 on VARCHARs. Header click sorts every column with arrow indicator. Column drag-resize works on every column.

**Gotchas — promoted to architecture lore.**

41. **One-way converters: return `BindingOperations.DoNothing` from `ConvertBack`, never `throw new NotSupportedException()`.** WPF's idiom is to throw; Avalonia's pipeline catches it cleanly but Visual Studio's "Break when this exception type is not handled by user code" — on by default for `NotSupportedException` — surfaces it as a debugger prompt. Worse, `DataGridTextColumn` wires its `Binding` as TwoWay even when the grid is `IsReadOnly="True"` (the same `IBinding` instance drives both the display `TextBlock` and the editor `TextBox`); refresh / column reorder / sort paths invoke `ConvertBack` defensively. `BindingOperations.DoNothing` is the explicit "no write" signal the binding layer expects — no exception, no break.

42. **DataGrid sorting with compiled bindings requires explicit `SortMemberPath` on EVERY column.** With `x:DataType` on the host (compiled bindings), `DataGridTextColumn.Binding` is compiled to a delegate and its path string is opaque to the DataGrid's sort engine. `CanUserSortColumns="True"` enables header-click sorting, but without `SortMemberPath` the column silently no-ops on click — no arrow, no reorder. Add `SortMemberPath="<PropertyName>"` to every column, duplicating the `Binding` path on `DataGridTextColumn`s and providing it standalone on `DataGridTemplateColumn`s. **Rule**: when the host UserControl has `x:DataType`, treat `SortMemberPath` as mandatory boilerplate on every sortable column, not an optional override.

43. **Avalonia 12.0.3 `DataGrid` has no double-click-on-gripper auto-fit, and `DataGridColumnHeader.OwningColumn` is `internal`.** WPF/IBExpert/Excel users expect "double-click the column separator to auto-fit"; Avalonia 12 doesn't ship it. Implementing it from scratch is harder than expected: catching the bubbled `DoubleTappedEvent` and walking to an ancestor `Thumb` named `PART_RightHeaderGripper` is doable, but there's no public API to map the header back to its column (`OwningColumn` is internal — reflection works to read it, but setting `column.Width = DataGridLength.Auto` afterward didn't actually re-fit in our tests; the column either kept its width or measured to the same value). Defer until a future Avalonia release exposes either column-header `DoubleTapped` or a public `OwningColumn` + reliable Auto re-measure path.

### TableDetail Ograniczenia — sub-tabs per constraint kind (shipped)

The Ograniczenia tab is no longer a flat list. It contains a nested `TabControl` with 4 sub-tabs — **Primary Key / Foreign Keys / Check / Unique** — each with its own DataGrid and a column set tailored to the constraint kind.

**Model** ([TableDetail.cs](src/EmberTern.Core/Metadata/TableDetail.cs)). Renamed `ConstraintInfo.Kind` → `ConstraintType` and `CheckSource` → `CheckClause` (spec asks). Added `IndexName` (backing index for PK / UNIQUE / FK), `UpdateRule` + `DeleteRule` (FK rules from `RDB$REF_CONSTRAINTS`), `IsDescending` (sort direction of the backing index from `RDB$INDICES.RDB$INDEX_TYPE`), and computed `ForeignKeyRule` that combines UPDATE/DELETE rules into a single string with RESTRICT suppressed (kept for back-compat; not used by the current view).

**Reader** ([FirebirdTableDetailReader.cs](src/EmberTern.Firebird/FirebirdTableDetailReader.cs)). `ConstraintsSql` now selects `rc.RDB$INDEX_NAME`, `fk.RDB$UPDATE_RULE`, `fk.RDB$DELETE_RULE`, and `idx.RDB$INDEX_TYPE` via `LEFT JOIN RDB$INDICES idx ON idx.RDB$INDEX_NAME = rc.RDB$INDEX_NAME`. `BuildConstraintInfo` gained optional `indexName` / `updateRule` / `deleteRule` / `indexDirection` params; `indexDirection == 1` ⇒ `IsDescending = true`.

**ViewModel** ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)). Added 4 plain get-only filter properties — `PrimaryKeyConstraints` / `ForeignKeyConstraints` / `CheckConstraints` / `UniqueConstraints` — each a case-insensitive LINQ filter over `Constraints` by `ConstraintType`. Matching `Count` / `HasX` / `XTabHeader` computed properties (e.g. `"Primary Key (1)"`). `Constraints.CollectionChanged` fires `OnPropertyChanged` for all 12 derived properties so the sub-tabs and counters refresh whenever the underlying collection mutates.

**View** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml)). Flat constraints DataGrid replaced with a nested `TabControl` (4 sub-tabs). Columns:
- **Primary Key**: Name, Fields, Index name, Sort
- **Foreign Keys**: Name, Fields, Ref. table, Ref. fields, Update rule, Delete rule, Sort
- **Check**: Name, Source (`Width="*"`)
- **Unique**: Name, Fields, Index name, Sort

Sub-tab headers are bound to the VM's `*TabHeader` (e.g. `"Foreign Keys (3)"`). `Classes.empty="{Binding !HasX...}"` greys the header label out via the new `TabItem.sub-tab.empty` style (uses `SubtleForegroundBrush`) when the constraint kind has no rows.

**Styles** ([ControlStyles.axaml](src/EmberTern.App/Themes/ControlStyles.axaml)). New `TabItem.sub-tab` class (font 10, padding 6,3) + `.empty` greying + hover/selected/PART_SelectedPipe rules mirroring `TabItem.bottom-tab`. All colors via `DynamicResource` — zero hardcoded.

**Sort column display** is driven by `BoolToSortOrderConverter` ([Converters/BoolToSortOrderConverter.cs](src/EmberTern.App/Converters/BoolToSortOrderConverter.cs)) — bool → `UiStrings.TableDetailConstraintSort{Ascending|Descending}`. Same `BindingOperations.DoNothing` `ConvertBack` shape as the other one-way converters.

**Tests** ([TableDetailConstraintFilterTests.cs](tests/EmberTern.Tests/TableDetailConstraintFilterTests.cs) +9, [FirebirdTableDetailReaderTests.cs](tests/EmberTern.Tests/FirebirdTableDetailReaderTests.cs) +12): per-kind filter behavior, empty list, case-insensitive ConstraintType match, header formatting, PropertyChanged on add/clear, `BuildConstraintInfo` mapping for new fields, `ForeignKeyRule` formatting + RESTRICT suppression, `IndexDirection → IsDescending` mapping, SQL regression-pins (`RDB$INDICES` join + `idx.RDB$INDEX_TYPE` selected; FK rule columns; index name).

### TableDetail Indeksy — type icon, expression, active, statistics (shipped)

Indeksy tab grown from 5 → 9 columns, matching IBExpert / DataGrip information density.

**Model** ([TableDetail.cs](src/EmberTern.Core/Metadata/TableDetail.cs)). `IndexInfo` gained `IsActive` (default `true`), `Statistics` (`double?`), `Expression` (`string?`), and `IndexType` (`"PRIMARY KEY"` / `"FOREIGN KEY"` / `""`). `IsPrimary` and new `IsForeignKeyIndex` are now computed from `IndexType` — single source of truth; removed the old `IsPrimary` init-prop duplication.

**Reader**. `IndexesSql` SELECTs `RDB$INDEX_INACTIVE`, `RDB$STATISTICS`, `RDB$EXPRESSION_SOURCE`. The correlated `RDB$RELATION_CONSTRAINTS` subquery is narrowed to `'PRIMARY KEY', 'FOREIGN KEY'` (UNIQUE backing indexes are surfaced via `IsUnique` and don't need to clutter `IndexType`). New internal `NormalizeIndexType` maps raw catalog values to the 3-value enum — case-insensitive, trimmed; UNIQUE/CHECK/null pass through to `""`.

**View** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml)). DataGrid columns in spec order/width:
- Type icon (30) — `Panel` with two layered Paths reusing `PkIconGeometry` / `FkIconGeometry` from `UserControl.Resources` (gated by `IsPrimary` / `IsForeignKeyIndex`, fills via `AccentBrush` / `WarningBrush`)
- Name (200), Fields (150), Expression (120)
- Unique (60, ✓), Descending (70, ✓), PK (40, ✓), Active (60, ✓)
- Statistics (80) — `DataGridTemplateColumn` with `TextBlock TextAlignment="Right" StringFormat={}{0:F6}` for right-aligned numeric display

**UI English-ization** ([UiStrings.cs](src/EmberTern.App/UiStrings.cs)). All TableDetail column header values translated from Polish to English: "Name" / "Type" / "Size" / "Scale" / "Description" / "Domain" / "FK Table" / "Fields" / "Unique" / "Descending" / "Ref. table" / "Ref. fields" / "Source" / "Index name" / "Sort" / "Ascending" / "Update rule" / "Delete rule". Hint strings also English ("Loading table details…", "Showing first {0} rows", "Loading data…", "No description.") and new Indeksy column strings ("Expression", "Active", "Statistics"). Sub-tab values English: "Primary Key", "Foreign Keys", "Check", "Unique". **Kept Polish** (per "Nazwy zakładek… nie ruszaj ich"): the 6 main TableDetail tab names — `"Pola"`, `"Ograniczenia"`, `"Indeksy"`, `"Dane"`, `"Opis"`, `"DDL"`.

No inline strings in XAML — every header goes through `x:Static app:UiStrings.*`.

**Tests** ([FirebirdTableDetailReaderTests.cs](tests/EmberTern.Tests/FirebirdTableDetailReaderTests.cs) +14): `IndexesSql_IncludesNewColumns`; `NormalizeIndexType` Theory (PK/FK case-insensitive + trimmed; UNIQUE/CHECK pass to `""`); IndexInfo defaults sensible (`IsActive=true`, nullable stats/expression null); `IsPrimary`/`IsForeignKeyIndex` derive from `IndexType`; full init round-trip.

### Gotchas — promote to architecture lore

44. **`ObservableCollection.CollectionChanged` is the right hook for "refresh derived properties" in a CommunityToolkit VM.** The MVVM toolkit's `[ObservableProperty]` covers scalar properties; collection-derived computed properties (counts, filters, headers) need their own `PropertyChanged` raise. Pattern: subscribe `Constraints.CollectionChanged += OnConstraintsCollectionChanged` in the ctor, raise `OnPropertyChanged(nameof(X))` for each derived property in the handler. Don't try to use `[NotifyPropertyChangedFor]` against an ObservableCollection — it only listens to the wrapped backing field, not the collection's mutation events.

45. **Avalonia `DataGridTextColumn` doesn't support per-column `HorizontalAlignment` / `TextAlignment` directly.** The fixed-text-column shape is opinionated about left-alignment. For right-aligned numeric data (e.g. statistics), use a `DataGridTemplateColumn` with an inner `TextBlock TextAlignment="Right"`. Same applies to italic / colored cell text — once you need any non-default text style, switch to a template column.

### TableDetail Zależności — IBExpert-style tree, FK-sourced Related Tables (shipped)

Long, iterative milestone with several intermediate states. The final shape:

**Tab layout** — new **Zależności** sub-tab between Indeksy and Dane in `TableDetailTabView`. Left panel **"Used by"** (objects depending on this table), right panel **"Depends on"** (objects this table uses), split by a `GridSplitter`. Each panel is a `TreeView` rooted in 11 fixed IBExpert-order categories: **Domains, Tables, Views, Procedures, Functions, Packages, Triggers, Exceptions, UDFs, Generators, Indexes**. Empty categories show `"Tables (0)"` with no expand chevron (Avalonia hides it when `HasItems = false`). Group + leaf templates reuse the metadata-tree icon pipeline (`MetadataNodeViewModel.IconFor` / `ResourceKeyFor` + `IconBrushConverter`) so colors and unicode glyphs match the sidebar 1:1, and re-evaluate on theme toggle. Leaves with a non-null `FieldName` show it as 10pt `SubtleForegroundBrush` secondary text; dedup at the VM level collapses repeated `ObjectName`s within a category.

**Double-click on a leaf** routes through `TableDetailTabViewModel.RequestOpen(DependencyLeafNode)` → `OpenObjectRequested` event → `MainWindowViewModel.OnOpenDdlRequested(MetadataObject)`. Tables open as TableDetail tabs; other kinds open as DDL tabs. Same dedup, same error routing as the metadata-tree double-click. Domain leaves are openable (added to `MapObjectTypeToKind`). Non-openable inputs (`Field`, `Object (N)`, empty name) silently no-op.

**Related Tables = FK only, not RDB$DEPENDENCIES.** This is the load-bearing correction: RDB$DEPENDENCIES does NOT record foreign-key relationships. Firebird stores FKs exclusively in `RDB$REF_CONSTRAINTS` joined to `RDB$RELATION_CONSTRAINTS`. The earlier attempts that pulled Tables from RDB$DEPENDENCIES (direct branch + indirect-via-domain branch) were producing accidental table-relationship rows — tables that happened to share a domain CHECK or computed-column expression with the current table — and missed the entire FK graph. For a header table like NAGL with 134 incoming FKs and 36 outgoing FKs in the user's schema, the old approach returned 6 / 9.

The final SQL surface ([FirebirdTableDetailReader.cs](src/EmberTern.Firebird/FirebirdTableDetailReader.cs)):

- **`FkOutgoingSql`** — `T → other tables`. `RDB$REF_CONSTRAINTS rc JOIN RDB$RELATION_CONSTRAINTS fk ON fk.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME JOIN RDB$RELATION_CONSTRAINTS pk ON pk.RDB$CONSTRAINT_NAME = rc.RDB$CONST_NAME_UQ WHERE TRIM(fk.RDB$RELATION_NAME) = @tableName`. Returns target-table names, lands in **Depends on → Tables**.
- **`FkIncomingSql`** — `other tables → T`. Same shape with `WHERE TRIM(pk.RDB$RELATION_NAME) = @tableName`. Returns referencing-table names, lands in **Used by → Tables**.
- **`DependsOnSql`** (4-branch UNION ALL) — domains from `RDB$RELATION_FIELDS.RDB$FIELD_SOURCE` (type 9), direct deps via RDB$DEPENDENCIES, package deps surfaced as type 18, indirect-via-domain. Every RDB$DEPENDENCIES branch carries `d.RDB$DEPENDED_ON_TYPE <> 0` so Relation-typed rows never produce Tables here.
- **`DependedOnBySql`** (2-branch UNION ALL) — direct dependents (excluding type 0 = Relation), and indirect-via-domain restricted to Views only via an `INNER JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = f.RDB$RELATION_NAME AND r.RDB$VIEW_BLR IS NOT NULL` filter with the projected type hardcoded to `CAST(1 AS INTEGER)`.
- All four queries hold the existing `CommandLock` and attach to the user's working transaction via `_transactionService?.ActiveTransaction`.

**Other categories untouched** — Procedures, Triggers, Views, Domains, Packages, Functions, Exceptions, Generators all still flow through RDB$DEPENDENCIES with the new `<> 0` and CHECK_/RDB$ filters. The CHECK_/RDB$ exclusion is scoped to `RDB$DEPENDENT_TYPE = 2` (Trigger) only — user-defined procedures named `CHECK_<something>` (the user's CHECK_ZAKSIEGWREJVAT example) pass through, while system-named CHECK constraint triggers are still filtered out.

**Distinct parameter names per branch** — `DependsOnSql` uses `@tableName / @t2 / @t3 / @t4`, `DependedOnBySql` uses `@tableName / @t2`. FK queries use `@tableName`. The C# adds each separately. Empirically the FirebirdSql.Data multi-reference name binding silently dropped bindings on UNION ALL branches past the first INNER JOIN, leaving indirect-via-domain rows behind. Distinct names sidestep that path entirely — each branch gets its own positional parameter.

**Persistence** — TableDetail tabs carrying a Zależności sub-tab persist via `CoreTabKind.TableDetail`; `LoadAsync` populates `DependsOn` / `DependedOnBy` collections AND rebuilds the matching tree groups via `BuildDependencyTree`. Lazy-loads on first activation via `EnsureLoadedAsync` (the existing `_loadTask` idempotency pattern).

**Tests** — `FirebirdTableDetailReaderTests` got SQL pins for the FK queries (`FkOutgoingSql_QueriesRefConstraintsOnly` + `DoesNotContain("RDB$DEPENDENCIES")` to prevent regression), `<> 0` filters in both directions, the VIEW_BLR-gated indirect branch, and updated direction-specific WHERE-clause assertions. `MapObjectType(9)` now maps to `"Domain"` instead of `"Field"` (test updated). New [DependencyTreeTests.cs](tests/EmberTern.Tests/DependencyTreeTests.cs) (16 cases) covers `BuildDependencyTree` always returning 11 categories in IBExpert order, populated/empty split, group + leaves sharing kind icon/key, UDFs staying empty without an icon, unknown types dropped, `MapObjectTypeToKind` known + non-openable inputs, `RequestOpen` event-firing semantics including Domain. **492 / 492 green**.

**Gotchas — promote to architecture lore.**

46. **`RDB$DEPENDENCIES` is not the FK catalog.** It records compile-time PSQL-style dependencies: view source bodies, trigger bodies, procedure bodies, computed-column expressions, default-value expressions, domain CHECK constraints. Foreign keys are stored exclusively in `RDB$REF_CONSTRAINTS` joined to `RDB$RELATION_CONSTRAINTS`. Any "Related Tables" view that pulls from `RDB$DEPENDENCIES` will surface accidental shared-domain / shared-expression links while missing every actual FK relationship. The right shape: source Tables ONLY from `RDB$REF_CONSTRAINTS` joins, source everything else (Views, Procedures, Triggers, Domains, Packages, Functions, Exceptions, Generators) from `RDB$DEPENDENCIES`. To prevent the two paths from polluting each other, filter `RDB$DEPENDED_ON_TYPE <> 0` (Relation) out of the RDB$DEPENDENCIES branches. If you need indirect-via-domain Views, gate that branch with `INNER JOIN RDB$RELATIONS r ON ... AND r.RDB$VIEW_BLR IS NOT NULL` and project type 1.

47. **FirebirdSql.Data 10.x multi-reference named parameters are unreliable inside complex UNION ALL queries.** The docs say "the same @name can be used multiple times" — and for simple 2-branch unions it does work. But once a branch has an INNER JOIN further down the UNION, the driver empirically drops the parameter binding on that branch and the WHERE silently returns no rows (no exception, just zero results). Cost: a "Used by → Tables" panel that showed 6 instead of 134. The fix is mechanical: use distinct `@t2`, `@t3`, `@t4` names per branch and `AddWithValue` each one. Each branch gets its own positional parameter; the driver never has to do the name-to-multiple-positions resolution that's broken. **Rule**: any time the SQL has more than one `@name` reference, give each occurrence a distinct name. If tests need an `Assert.Contains("= @tableName", sql)` pin to stay green, keep the first occurrence as `@tableName` and rename the rest.

48. **`CHECK_<n>` and `RDB$<n>` system-name filters must be scoped by type.** Firebird's system-generated CHECK-constraint triggers are named `CHECK_<n>` and have `RDB$DEPENDENT_TYPE = 2` (Trigger). A blanket `TRIM(name) NOT STARTING WITH 'CHECK_'` filter also excludes user procedures / views / tables whose name happens to start with `CHECK_` (the user's `CHECK_ZAKSIEGWREJVAT` procedure was a real case). Scope the prefix exclusion to triggers only: `NOT (RDB$DEPENDENT_TYPE = 2 AND TRIM(name) STARTING WITH 'CHECK_')`. Same for `RDB$<n>` system triggers — scope to type 2, leave non-trigger objects starting with `RDB$` unmolested.

### TableDetail Dane — inline data editing (shipped)

Inline edit on the Dane sub-tab. Auto-begin via the existing `TransactionService` on the first mutation; user controls Commit / Rollback through the same toolbar buttons the Query tab uses. No autocommit, no new transaction flow.

**Editor** ([FirebirdDataEditor.cs](src/EmberTern.Firebird/FirebirdDataEditor.cs)) — direct class, no interface. Takes `FirebirdConnectionService` + `TransactionService`. Three async methods: `UpdateCellAsync` / `InsertRowAsync` / `DeleteRowAsync`. Each one:
- Calls `EnsureTransactionAsync` — if `_transactionService.IsActive` is false, awaits `BeginTransactionAsync()` (mirrors the F5 executor's auto-begin path). Never opens a raw `connection.BeginTransactionAsync` of its own.
- Acquires `_connectionService.CommandLock` around the command body (per gotcha #31).
- Sets `cmd.Transaction = _transactionService.ActiveTransaction`.
- Wraps `FbException` as `DataEditException` with the server's raw message.
- Calls `_transactionService.NotifyStatementExecuted()` after release so the transaction-bar counter ticks.

Internal static SQL builders (`BuildUpdateSql` / `BuildInsertSql` / `BuildDeleteSql`) emit `"NAME"`-quoted identifiers with doubled internal quotes; parameter names are positional (`@newValue`, `@pk0..N`, `@v0..N`) so the driver never has to dedupe multi-references (per gotcha #47).

**VM** ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)) — new ctor accepts `FirebirdDataEditor?`. Backward-compat ctors still work (existing tests / construction sites with `null` editor get a read-only data tab). New surface:
- `EditableRows: ObservableCollection<object?[]>` — writable mirror of `DataResult.Rows`. Re-populated by `partial void OnDataResultChanged` after each preview fetch. DataGrid binds to this so Add/Delete mutate the visible row list without re-allocating a QueryResult.
- `IsEditingData`, `SelectedRow`, `EditStatusMessage`, `HasPrimaryKey`, `PrimaryKeyColumns`, `ColumnIndex`, `CanEnterEditMode`, `EditModeHint`.
- Commands: `ToggleEditModeCommand` (gated on `CanEnterEditMode` = editor present), `AddRowCommand` (gated on `IsEditingData && editor`), `DeleteRowCommand` (gated on `IsEditingData && editor && SelectedRow && HasPrimaryKey && _pkSnapshots.ContainsKey(SelectedRow)`).
- `RefreshPrimaryKeyColumns()` rebuilds `PrimaryKeyColumns` from `Fields.Where(f => f.IsPrimaryKey)`. Called at the end of the Fields load step in `LoadAsync`.
- `_pkSnapshots: Dictionary<object?[], object?[]>` (keyed by row reference via `ReferenceEqualityComparer.Instance`) holds the original PK values per loaded row so UPDATE/DELETE can identify the row even after the user edits a PK cell. Captured during `RebuildEditableRows`.
- `_newRows: HashSet<object?[]>` tracks rows added in-grid via `AddRowCommand` that haven't been INSERTed yet.
- `UpdateCellAsync(row, columnIndex, newValue)` — view's per-cell commit entry point. For an existing row: looks up PK snapshot, calls `_dataEditor.UpdateCellAsync`, on success updates the cell + refreshes the snapshot if the PK column itself was edited. For a new row (in `_newRows`): just stores the cell in memory — INSERT is deferred to `CommitNewRowAsync`.
- `CommitNewRowAsync(row)` — view's row-commit entry point. Builds a `(column, value)` list omitting nulls (NULL columns aren't sent in INSERT — Firebird uses column defaults), calls `_dataEditor.InsertRowAsync`, on success removes from `_newRows` and captures a PK snapshot so subsequent cell edits flip to UPDATE. Empty rows (all nulls) are silently dropped from the grid.
- `IsNewRow(row)` — view-facing predicate.
- `ConfirmationRequested: Func<ConfirmRequest, Task<bool>>?` event for the delete confirmation; `MainWindowViewModel` wires its own `RequestConfirmAsync` into it so the existing `ConfirmDialog` modal handles both DDL-load failures and row-delete confirmations.

**View** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml) + .axaml.cs):
- Dane sub-tab grid now has 4 rows: edit toolbar / status / hint / grid (was 2).
- Toolbar: ✎ Toggle (always visible inside the Dane sub-tab), `+` New row + `−` Delete (visible only when `IsEditingData`), `EditModeHint` text (shown when no PK — "Table has no primary key — only INSERT is available.").
- Status row: `EditStatusMessage` in error styling, surfaces UPDATE/INSERT/DELETE failures (Messages tab is hidden on TableDetail tabs).
- DataGrid: `ItemsSource="{Binding EditableRows}"`, `SelectedItem="{Binding SelectedRow, Mode=TwoWay}"`, `IsReadOnly="{Binding !IsEditingData}"`.
- Code-behind: each column built imperatively now also carries a `CellEditingTemplate` (TextBox over the cell value). `CellEditEnding` + `RowEditEnding` events wired in the ctor. CellEditEnding reads the TextBox text, empty → NULL, calls `vm.UpdateCellAsync`. RowEditEnding checks `vm.IsNewRow(row)` and only fires `vm.CommitNewRowAsync` for not-yet-INSERTed rows. Flipping `IsEditingData` triggers a full column rebuild (forces `_dataPreviewColumnNames.Clear()` so `PopulateDataGrid` regenerates with the editing templates active).

**MainWindowViewModel wiring**: new field `_dataEditor = new FirebirdDataEditor(_service, _transactionService)`. Both TableDetail construction sites (`OnOpenDdlRequested` for new tabs, `LoadWorkspaceFor` for restored tabs) pass `_dataEditor` to the VM and `+= RequestConfirmAsync` to the new `ConfirmationRequested` event.

**Strings** ([UiStrings.cs](src/EmberTern.App/UiStrings.cs)) — `DataEditToggleIcon/Tooltip`, `DataEditAddRowIcon/Tooltip`, `DataEditDeleteRowIcon/Tooltip`, `DataEditDeleteConfirm{Title,Message,Yes}`, `DataEditNoPrimaryKeyHint`, `DataEditNotConnectedHint`. Zero inline strings in XAML.

**Tests** ([FirebirdDataEditorTests.cs](tests/EmberTern.Tests/FirebirdDataEditorTests.cs) +7, [TableDetailDataEditTests.cs](tests/EmberTern.Tests/TableDetailDataEditTests.cs) +12): SQL builder shape (single PK / composite PK / internal-quote escaping); VM defaults (no editor → can't enter edit mode); `RefreshPrimaryKeyColumns` derives from Fields; no-PK hint shown; `DataResult` assignment populates `EditableRows` + `ColumnIndex` + clears them on reassignment / null; `CanAddRow` gates correctly; `UpdateCellAsync` no-ops when editor null; `BuildKeyValuePairs` pairs by index; `IsNewRow` false for existing rows. **536 / 536 green** (517 → 536, +19). Build clean (zero warnings, TWAE on). App launches and exits cleanly via the 8-second-uptime smoke.

**Gotchas — promote to architecture lore.**

49. **`ReferenceEqualityComparer.Instance` for `Dictionary<object?[], ...>` keyed by row identity.** Default `EqualityComparer<object?[]>` falls back to `ReferenceEquals` anyway for arrays without a custom equality, but `ReferenceEqualityComparer.Instance` makes the intent explicit and is mandatory for `HashSet<object?[]>` where the default would block on null elements during hash. Use this whenever a tracking collection needs to identify rows by reference (row mutations don't invalidate the key).

50. **DataGrid `CellEditEnding` + `RowEditEnding` are the two-stage commit gates.** CellEditEnding fires per-cell (Tab/Enter out of the TextBox); RowEditEnding fires when the row "confirms" (Enter on the row, or focus moves to a different row). For Avalonia 12.0.3 DataGrid editing of `object?[]` rows: rely on `CellEditEnding`'s `EditingElement` (the `TextBox` from the `CellEditingTemplate`) to read the new text, then map column → index via `_dataPreviewGrid.Columns.IndexOf(e.Column)`. For row-level commit (INSERT path on a freshly added row), use `RowEditEnding` and check a VM-side `IsNewRow` predicate so existing rows (already UPDATEd cell-by-cell) don't fire a duplicate operation.

### TableDetail Dane — UX polish: always-editable, optimistic writes, PK refresh sync (shipped)

Post-ship feedback round on the initial Dane inline-edit. Five fixes; whole edit-mode-toggle layer was removed.

**1. No more ✎ toggle — DataGrid is always editable when an editor is wired.** `IsEditingData`, `ToggleEditModeCommand`, `CanEnterEditMode` deleted. The new surface: `CanEditData` (`_dataEditor is not null`) + `IsDataReadOnly` (inverse, bound to `DataGrid.IsReadOnly`). Edit starts on F2 / Enter / second-click natively. `+ −` buttons unconditionally visible inside the Dane sub-tab. Dropped now-unused `DataEditToggleIcon` / `DataEditToggleTooltip` strings.

**2. Optimistic local cell write — the cell paints the new value immediately.** Avalonia's DataGrid rebuilds `CellTemplate` synchronously after `CellEditEnding` returns. Our `FuncDataTemplate<object?[]>` lambda reads `row[columnIndex]` during the rebuild — so the new value must already be in place when the rebuild fires (i.e. before the `await` in `UpdateCellAsync`). [`TableDetailTabViewModel.UpdateCellAsync`](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs) now: (1) captures `oldValue`; (2) writes `row[columnIndex] = newValue` synchronously; (3) `await`s the DB UPDATE; (4) on failure, reverts via `ReplaceRowInGrid(row)` which clones the array, migrates `_pkSnapshots` / `_newRows` / `SelectedRow` to the clone, and re-inserts into `EditableRows` so the DataGrid recycles the row container and re-renders.

**3. AddRow auto-begins the working transaction.** `AddRowCommand` is `async` and `await`s a new public `FirebirdDataEditor.EnsureTransactionAsync` (calls `TransactionService.BeginTransactionAsync` only when no tx is active). The existing `TransactionStateChanged` → `MainWindowViewModel.OnTransactionStateChanged` flow propagates `IsTransactionActive` / `HasExecutedInTransaction` / `TransactionBarText` notifications — toolbar Commit/Rollback enable correctly on first row add.

**4. False "no primary key" after refresh — two-part fix.** `RebuildEditableRows` defensively calls `RefreshPrimaryKeyColumns()` when `Fields.Count > 0` so PK is always derived from the current Fields snapshot. `ReloadDataPreviewAsync` (used by both Refresh and ApplyColumnSort paths) now awaits `EnsureLoadedAsync` at its start — idempotent (cached task) when LoadAsync already finished, but covers the race where Refresh is clicked before the initial lazy load completes.

**5. View XAML adjustments.** Removed `IsEditingData` / `ToggleEditModeCommand` plumbing. `IsReadOnly="{Binding IsDataReadOnly}"`. The toolbar row inside `TableDetailTabView` keeps only the edit-hint text — `+ −` move to the main toolbar in the next polish round.

**Tests**: 536 → 538 (+2 — `RebuildEditableRows_RefreshesPrimaryKeyFromFields`, `IsDataReadOnly_NoEditor_IsTrue`). Build clean, zero warnings.

### TableDetail Dane — pagination, toolbar move, smart cell editors (shipped)

Big polish round: row height bumped, +/- relocated to the main toolbar with pagination next to them, and per-column-type editors for DATE/TIMESTAMP/BOOLEAN/BLOB.

**1. Row height 32 px.** Style selector `<Style Selector="DataGrid.data-edit DataGridRow"><Setter Property="Height" Value="32"/></Style>` declared after the global `DataGridRow Height=22`. The Dane DataGrid carries `Classes="data-edit"`; Pola/Indeksy/Ograniczenia stay at 22.

**2. Pagination — VM + reader + paged SQL.**

- [`FirebirdTableDetailReader.cs`](src/EmberTern.Firebird/FirebirdTableDetailReader.cs): `GetDataPreviewAsync` signature is now `(tableName, page, pageSize, [orderBy], ct)`. SQL switched from `SELECT FIRST {limit} *` to `SELECT * FROM "T" [ORDER BY ...] ROWS m TO n` (FB 2.5+ syntax; embedded literals so FB 2.5 parameter-binding quirks don't bite). New `GetRowCountAsync(tableName, cap, ct)` runs `SELECT COUNT(*) FROM (SELECT FIRST {cap} 1 AS X FROM "T") sub` — the inner FIRST cap keeps the engine from sequential-scanning a 50M-row table. New internal helpers `ComputeRowRange` + `BuildRowCountSql` + `BuildDataPreviewSql(tableName, startRow, endRow, orderBy)` pinned by tests.
- [`TableDetailTabViewModel.cs`](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs): new state `CurrentPage` (1-based), `PageSize` (default 200, clamped 1..1000), `LastKnownRowCount` (set after `GoToLastPageAsync`), `HasPreviousPage`, `HasNextPage`, and 4 commands `GoToFirstPageCommand` / `GoToPreviousPageCommand` / `GoToNextPageCommand` / `GoToLastPageCommand`. `HasNextPage` uses authoritative COUNT-probe (`LastKnownRowCount`) when set; otherwise falls back to "current page is full" heuristic. Sort changes reset `LastKnownRowCount = null` and `CurrentPage = 1`. `DataPreviewHint` reformatted to `"Page {0} · Showing {1} rows"` (plus existing sort suffix).
- New constants: `MaxPageSize = 1000`, `RowCountCap = 50000`.

**3. `+ −` and pagination buttons in the main toolbar.** Removed from [`TableDetailTabView.axaml`](src/EmberTern.App/Views/TableDetailTabView.axaml) (only the edit-hint text remains in that row). Added in [`MainWindow.axaml`](src/EmberTern.App/Views/MainWindow.axaml) after the Refresh button, all gated on `IsDataTabActive`: separator → `+ −` (bound through `ActiveTableDetail.AddRowCommand` / `DeleteRowCommand`) → separator → `⏮ ◀ ▶ ⏭` pagination (bound through `ActiveTableDetail.GoTo*PageCommand`).

**4. Smart cell editors.**

- New [`Converters/DateTimeToDateTimeOffsetConverter.cs`](src/EmberTern.App/Converters/DateTimeToDateTimeOffsetConverter.cs) — singleton `IValueConverter` going `DateTime` ↔ `DateTimeOffset?`. Unspecified-kind DateTimes surface as Local (Firebird's managed driver returns Unspecified; treating as UTC shifts the displayed wall-clock).
- New [`Views/BlobEditorWindow.axaml(.cs)`](src/EmberTern.App/Views/BlobEditorWindow.axaml) — modal 600×400 Window with monospace multiline TextBox + OK/Cancel. Static `ShowAsync(owner, currentValue, readOnly)` returns `string?` (null on Cancel). For SUB_TYPE 1 text BLOBs the dialog is editable; for binary BLOBs the caller passes a `"Binary BLOB (N bytes)"` placeholder + `readOnly=true`.
- [`TableDetailTabView.axaml.cs`](src/EmberTern.App/Views/TableDetailTabView.axaml.cs): new `CellEditorKind` enum (Text / Date / Boolean / Blob). `DetermineEditorKind` resolves from the matching `FieldInfo`: `BaseTypeName` starts with `DATE` or `TIMESTAMP` → Date; `BaseTypeName == "BOOLEAN"` → Boolean; `BaseTypeName == "SMALLINT" && Domain == "T_BOOLEANN"` → Boolean (legacy ERP convention); `BaseTypeName == "BLOB"` → Blob; else Text. Per-kind template builders: `BuildTextCellTemplate` / `BuildTextEditingTemplate` (existing TextBox flow), `BuildDateEditingTemplate` (CalendarDatePicker, `MinWidth = 120` after UX feedback iteration 160→120), `BuildBooleanCellTemplate` (CheckBox in CellTemplate; Click handler fires `UpdateCellAsync` directly with `bool` or `short 0/1` depending on underlying type), `BuildBlobCellTemplate` (`…` Button opens BlobEditorWindow). Boolean and BLOB columns are `IsReadOnly = true` so the standard cell-edit flow stays out of their way; their CellTemplate handles the commit. `OnCellEditEnding` dispatches on the resolved kind via the parallel `_dataPreviewEditorKinds` list.

**Tests** (538 → 557, +19): pagination cycle, SQL/RowCount shape (`ComputeRowRange` Theory, `BuildDataPreviewSql_*` updated to ROWS form, `BuildRowCountSql_*`), `DateTimeToDateTimeOffsetConverterTests` (6 tests covering Convert / ConvertBack / Unspecified-as-Local / DoNothing fallback). Build clean (zero warnings, `TreatWarningsAsErrors=true`). App smoke-launched and exits cleanly.

**Gotchas — promote to architecture lore.**

51. **`Avalonia 12.0.3 CalendarDatePicker.SelectedDate` is `DateTime?`, NOT `DateTimeOffset?`.** Specs / older Avalonia samples assume `DateTimeOffset?`. In 12.0.3 the property is `DateTime?`; assigning a `DateTimeOffset?` is a `CS0029` compile error. The `DateTimeToDateTimeOffsetConverter` we ship is forward-compat for newer Avalonia versions or third-party pickers that do use `DateTimeOffset`; the view code path assigns `DateTime?` directly. **Rule**: check the API surface against the running Avalonia version before threading a converter for type bridging — the type may already match.

52. **`TextBox` scroll-bar visibility goes through attached `ScrollViewer.*` properties, not direct ones.** `<TextBox HorizontalScrollBarVisibility="Auto" .../>` fails with `AVLN2000` in 12.0.3 — the property doesn't exist on TextBox itself. Correct: `<TextBox ScrollViewer.HorizontalScrollBarVisibility="Auto" ScrollViewer.VerticalScrollBarVisibility="Auto" .../>` — these are the standard Avalonia ScrollViewer attached properties. **Rule**: when a scroll affordance is needed on a TextBox, reach for the attached `ScrollViewer.*` setters; don't expect them as direct properties.

53. **Avalonia DataGrid CellTemplate rebuild on `CellEditEnding` lets us paint optimistic local writes without `INotifyPropertyChanged` wrapping the row.** The DataGrid tears down the editing element and re-applies `CellTemplate` synchronously after `CellEditEnding` returns. With a `FuncDataTemplate<object?[]>` reading `row[columnIndex]`, mutating that cell *before the first await* in the async commit handler means the post-edit rebuild paints the new value immediately. For failure rollback (the new value didn't survive the DB UPDATE), revert + force a row swap via `EditableRows[idx] = cloneOfRow` to trigger an ItemsControl replace, then migrate any per-row tracking dictionaries (`_pkSnapshots`, `_newRows`) to the clone. **Rule**: prefer optimistic local mutation + swap-on-failure over wrapping `object?[]` rows in an observable shape — works with the existing data model and keeps the DataGrid path identical to the read-only case.

54. **Firebird `ROWS m TO n` pagination uses literal integers, not parameters, on FB 2.5.** Newer FB versions accept `ROWS @offset TO @end` parameter binding; FB 2.5 does not. `BuildDataPreviewSql` embeds the page bounds as literals via `StringBuilder.AppendFormat(InvariantCulture, ...)` — safe with integers, and avoids the brittle cross-version parameter-binding behavior of the `ROWS` clause. **Rule**: for pagination SQL targeting "anything from FB 2.5 onward", embed the row range as literals.

