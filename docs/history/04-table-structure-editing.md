# History — Table Structure Editing (DDL generator, FK wizard, constraints, field dependencies)

> Archived from CLAUDE.md's former "Completed milestones" chronicle during the Documentation Cleanup Sprint (2026-07-11).
> Verbatim extract, lines 1343–1909 of the original file. Nothing altered except this header.

---

### Table structure editing — DdlGenerator + Pola edit toolbar + AddFieldDialog (shipped 2026-06-12)

Add a new table from a single dialog, queue field-level edits inline, compile them as a batch. Mirrors IBExpert's "pending DDL changes" workflow: nothing leaves the VM until ⚡ Compile fires.

**Core layer (zero Avalonia deps)** — [PendingDdlChange.cs](src/EmberTern.Core/Metadata/PendingDdlChange.cs), [FieldDefinition.cs](src/EmberTern.Core/Metadata/FieldDefinition.cs), [DdlGenerator.cs](src/EmberTern.Core/Metadata/DdlGenerator.cs):

- `PendingDdlChange { Kind, Description, Sql }` + `PendingDdlChangeKind { AddField, DropField, MoveField, Other }`.
- `FieldDefinition` carries the AddFieldDialog form state (name, NotNull, PK, Domain, BasicType, Size, Precision/Scale, BlobSubType, DefaultValue, CheckExpression, ComputedExpression, Description, AutoIncrement mode, GeneratorName, TriggerName).
- `DdlGenerator` is the pure emitter — static methods: `Quote(id)`, `BuildCreateTable(name)`, `BuildDropField(table, field)`, `BuildMoveField(table, field, pos)`, `BuildAddField(table, def)`, `BuildAutoIncTrigger(table, field, gen, trig?)`. ADD-FIELD output is a single string with multiple `;`-separated statements when autoincrement-by-generator is requested (`CREATE GENERATOR` + `CREATE TRIGGER` alongside the column ADD).

**Firebird layer** — [FirebirdDdlExecutor.cs](src/EmberTern.Firebird/FirebirdDdlExecutor.cs): new direct class with `ExecuteAsync(sql, ct)`. **Auto-begins the user's working transaction** when none is active (mirrors `FirebirdQueryExecutor`'s F5 path), so DDL participates in Commit / Rollback exactly like DML — the user can Add a field, see it appear, then Rollback to undo. Splits on TOP-LEVEL semicolons (`SplitStatements` tracks a BEGIN/END nesting counter so trigger bodies stay intact — see gotcha #55), holds `CommandLock` across the whole batch, attaches `cmd.Transaction = _transactionService?.ActiveTransaction` per statement. Wraps `FbException` in `DdlExecutionException`. One `NotifyStatementExecuted()` tick per `ExecuteAsync` call.

**New Table button** — main toolbar gained `⊞` between the connection-buttons and the connect/disconnect/reconnect/refresh group, gated on `MainWindowViewModel.CanCreateTable` (`_service.IsConnected`). Click opens [AddTableDialog](src/EmberTern.App/Views/AddTableDialog.axaml): a single-TextBox modal with Cancel / Create buttons. On OK, MainWindowViewModel.NewTableAsync calls `DdlGenerator.BuildCreateTable(name)` → `_ddlExecutor.ExecuteAsync(sql)` → `Metadata.RefreshAsync()` so the new table appears immediately in the sidebar. Errors surface in Messages.

**Pola sub-tab edit toolbar** — new `Border` above the FieldsGrid carrying ⚡ Compile, ＋ Add Field, − Drop Field, ↑ Move Up, ↓ Move Down. Bindings go through the TableDetail VM's new commands. `FieldsGrid.SelectedItem` is two-way-bound to `TableDetailTabViewModel.SelectedField`; the Up / Down / Drop CanExecute gates re-evaluate on selection change.

**Add / Drop are immediate; Move is pending.** Per the user's UX call: structural changes that the user expects to see reflected on the grid right away (Add, Drop) run through `FirebirdDdlExecutor.ExecuteAsync` directly, then reset `_loadTask = null` and re-run `EnsureLoadedAsync` so Fields / Constraints / Indexes / DDL all repaint. Both still run **in the user's working transaction** (DDL Executor auto-begins one if needed) so Rollback from the main toolbar undoes them like any DML. Move (and any future inline-grid edits) keep the pending-queue + Compile shape — a multi-step renumber is naturally a batch.

**TableDetailTabViewModel additions**:
- `ObservableCollection<PendingDdlChange> PendingChanges` + `HasPendingChanges` / `CanCompile` / `DdlWithPendingPreview` (live "current DDL + `-- Pending changes:` + queued statements" rendering — drives the DDL sub-tab via the existing `PushDdl` code-behind path).
- `AddPendingAddField(FieldDefinition)` — pure entry point, kept for tests (queueing-only without execution, easy to assert).
- `ExecuteAddFieldAsync(def)` / `ExecuteDropFieldAsync(name)` — execute immediately via `_ddlExecutor.ExecuteAsync`, then call `ReloadAfterStructuralChangeAsync` (resets `_loadTask`, re-runs `EnsureLoadedAsync`). Errors surface as `ErrorMessage`.
- `AddFieldCommand` opens the dialog through `AddFieldRequested`, then calls `ExecuteAddFieldAsync`. `DropFieldCommand` confirms, then calls `ExecuteDropFieldAsync`. `MoveFieldUpCommand` / `MoveFieldDownCommand` continue to queue `PendingDdlChange` entries.
- `CompileCommand` — iterates remaining `PendingChanges` (today: Move; future: inline-grid edits), awaits `_ddlExecutor.ExecuteAsync(change.Sql)` per entry, removes-on-success. On first failure: stops, sets `ErrorMessage`, leaves remaining statements queued so the user can fix + retry. Full success calls `ReloadAfterStructuralChangeAsync`.
- `AddFieldRequested` event — async `Func<Task<FieldDefinition?>>` that the view fulfils by fetching live domains/generators from `_metadataReader.ListAsync(...)` and opening the dialog.

**AddFieldDialog** ([ViewModels/AddFieldDialogViewModel.cs](src/EmberTern.App/ViewModels/AddFieldDialogViewModel.cs) + [Views/AddFieldDialog.axaml](src/EmberTern.App/Views/AddFieldDialog.axaml)) — 9-tab modal matching the spec: top always-visible name + Not Null + Primary Key. Tabs: Domain, Basic type (with conditional Size / Precision+Scale / BLOB subtype sub-controls based on selected type), Default, Check, Computed by, Autoincrement (4 RadioButtons + conditional generator-name fields), Description, DDL (read-only TextBox bound to `DdlPreview`). Every `[ObservableProperty]` source includes `NotifyPropertyChangedFor(DdlPreview)`, so the live preview re-evaluates on every keystroke — the "DDL" tab paints the exact statement that will be executed.

Autoincrement radios use four sibling `IsAutoincNone/Identity/Existing/New` properties (each TwoWay-bound to a `RadioButton.IsChecked`) — Avalonia's RadioButton doesn't bind cleanly to an enum, but the sibling-property pattern auto-syncs through `OnAutoIncrementModeChanged` raising PropertyChanged on the four properties at once.

**TableDetailTabView wiring** — `OnDataContextChanged` subscribes to `AddFieldRequested`; the handler walks up to the host Window via `FindAncestorOfType<Window>()`, reads `MainWindowViewModel.MetadataReader` for the live domain + generator lists, opens the dialog. `PushDdl` now reads `DdlWithPendingPreview` (instead of `DdlText`) and listens to both property changes, so the DDL sub-tab repaints when either the underlying DDL or the pending queue changes.

**Tests** — [DdlGeneratorTests.cs](tests/EmberTern.Tests/DdlGeneratorTests.cs) (+22): quoting, CREATE TABLE skeleton, drop/move emission, basic-type variants (INTEGER / VARCHAR + size / NUMERIC + precision/scale / BLOB + subtype / domain overrides type), NotNull + DEFAULT + CHECK + COMPUTED + PRIMARY KEY clauses, identity vs new-generator vs existing-generator autoincrement, auto-derivation of trigger / generator names, validation on empty inputs, `SplitStatements` shape (empty segments / whitespace / BEGIN-END nesting). [AddFieldDialogVmTests.cs](tests/EmberTern.Tests/AddFieldDialogVmTests.cs) (+18): validation, live DDL preview tracking each property, type-specific sub-controls, autoincrement radio exclusivity, BuildDefinition trim semantics, Accept/Cancel command flow. [PendingDdlVmTests.cs](tests/EmberTern.Tests/PendingDdlVmTests.cs) (+8): pending queue lifecycle, DDL-with-pending preview rendering, MoveUp/MoveDown emission + bounds, HasPendingChanges notification. **608 / 608 green** (557 → 608, +51).

**Gotcha — promote to architecture lore.**

55. **Splitting multi-statement DDL on `;` must track BEGIN/END nesting.** PSQL bodies (CREATE TRIGGER / CREATE PROCEDURE) carry internal semicolons that terminate intra-block statements — splitting naively chops a CREATE TRIGGER into pieces that no longer parse on their own. The fix: a single nesting counter that increments on word-boundary BEGIN and decrements on word-boundary END (case-insensitive). Each `;` only commits a statement when the counter is zero. Enough for the shapes EmberTern emits today (no nested triggers, no procedures); revisit if we ever generate nested BEGIN/END blocks. Tests pin both the "no nesting" simple case and the "trigger body with internal `;` is preserved as one statement" case via the autoincrement DDL.

### Table structure editing — Add/Drop immediate, Move pending; AddFieldDialog polish (shipped 2026-06-12)

Follow-ups to the previous milestone, shipped together:

- **Add Field and Drop Field run immediately** through `FirebirdDdlExecutor.ExecuteAsync` (which now auto-begins the user's working tx, mirroring the F5 executor); the Pola grid refreshes the moment the statement returns. Move Field stays pending → ⚡ Compile. Rationale per the user: structural changes the user expects to see "as soon as I press the button" should land immediately and be undone via the standard Rollback, not stay queued. `ExecuteAddFieldAsync` / `ExecuteDropFieldAsync` are public on `TableDetailTabViewModel` (entry points used by AddFieldCommand/DropFieldCommand); `AddPendingAddField` stays as the pure-API queue path for tests. `ReloadAfterStructuralChangeAsync` is the shared "reset `_loadTask` + re-run `EnsureLoadedAsync`" helper.
- **Pola edit toolbar moved into the main app toolbar.** Removed the duplicate `Border` above the Pola DataGrid. Added `IsFieldsSubTabActive` (on `TableDetailTabViewModel`) + `IsFieldsTabActive` (bridge on `MainWindowViewModel`) — the existing pattern from `IsDataTabActive`. Main toolbar shows ⚡ ＋ − ↑ ↓ bound through `ActiveTableDetail.*FieldCommand` when the Pola sub-tab is active. Commit / Rollback ✓ ✕ also surface on Pola (via `ShowTransactionButtons = IsQueryTabActive || IsDataTabActive || IsFieldsTabActive`) so the user can roll back an Add/Drop without changing context.
- **AddFieldDialog widened to 820 px** so all 8 tabs (Domain / Basic type / Default / Check / Computed by / Autoincrement / Description / DDL) fit in one line.
- **Domain ComboBox shows the underlying type.** New `DomainSpec(Name, Type)` record in Core; `FirebirdMetadataReader.ListDomainsAsync()` joins `RDB$FIELDS` to `FirebirdTableDetailReader.FormatFieldType` (via `InternalsVisibleTo`) and returns `(NAME, "VARCHAR(80)")` etc. ComboBox `ItemTemplate` is a two-column Grid — name on the left, type on the right in `SubtleForegroundBrush`. `FieldDefinition.Domain` stays a plain string; `SelectedDomain?.Name` is what feeds the generator.

### Table structure editing II — AI column, ALTER methods, CreateTableDialog, inline editing, edit-mode toggle (shipped 2026-06-12)

The big follow-up. Lands in several iterations within one session.

**FB5 caveat removed.** Identity columns work from FB3+ — the dialog radio label and `DdlGenerator`'s code comment no longer mention "Firebird 5".

**AI column in the Pola grid.** `FieldInfo.IsAutoIncrement` (new), detected by `FirebirdTableDetailReader.FieldsSql` via two paths: (a) FB3+ identity columns (`rf.RDB$IDENTITY_TYPE IS NOT NULL`), (b) legacy BEFORE INSERT triggers whose source contains `GEN_ID(` plus `NEW.<field>`. The grid renders `IsAutoIncrement` through the existing `BoolToCheckmarkConverter`. **FB2.5 trade-off**: `RDB$IDENTITY_TYPE` doesn't exist on 2.5; the Fields query errors there and `SafeLoadAsync` traps it — only the Pola tab shows an error, every other sub-tab still renders.

**DdlGenerator ALTER methods + TableSpec** ([TableSpec.cs](src/EmberTern.Core/Metadata/TableSpec.cs)): `BuildCreateTable(name, TableSpec)` emits `CREATE [GLOBAL TEMPORARY] TABLE` (Persistent / TempDeleteRows / TempPreserveRows kinds), per-field clauses, a named `CONSTRAINT "PK_T" PRIMARY KEY (...)`, optional `CREATE SEQUENCE` + `CREATE TRIGGER` (using `NEXT VALUE FOR`) for fields flagged `AutoIncrement = NewGenerator`, plus optional `COMMENT ON TABLE` + per-field `COMMENT ON COLUMN`. Single-quote literals escaped (`''`). New ALTER helpers: `BuildRenameField`, `BuildSetNotNull(bool)`, `BuildSetDefault(string?)`, `BuildAlterType`, `BuildCommentColumn`, `BuildCommentTable`.

**Inline editing on the Pola grid.** New `FieldRowViewModel` wraps a `FieldInfo` with editable copies (`Name`, `NotNull`, `DefaultValue`, `TypeText`, `DomainName`, `Description`); read-only forwards for everything else; `IsModified` flag drives a subtle `WarningBrush` row tint. `EditableFields: ObservableCollection<FieldRowViewModel>` mirrors `Fields` through a `CollectionChanged` hook. The grid binds to `EditableFields`; `RowEditEnding` in code-behind calls `EnqueueRowEdits(row)` which inspects edited vs. original and queues the matching ALTER. Rename and Type/Domain changes are **gated by `CanRenameField`** — when `DependedOnBy` contains anything referencing the field, the edit is reverted and `ErrorMessage` is set; FB rejects ALTER COLUMN TYPE / TO when other objects (views/triggers/checks) still reference the column. **Edit mode is opt-in for existing tables** — new `IsFieldEditMode` flag on `TableDetailTabViewModel` (default `false`); a toggle in the main toolbar (▦✎ icon, gated on Pola sub-tab) flips it. New Table tab is always-editable.

**New Table → workspace tab (not a modal).** Replaced the modal `CreateTableDialog` with `WorkspaceTabKind.NewTable` + `NewTableTabViewModel` + [`NewTableTabView`](src/EmberTern.App/Views/NewTableTabView.axaml) UserControl. Tab title "New Table" (or table name after the user types one); icon ▦ in the accent-blue table glyph (matches the sidebar). Top: name TextBox + kind ComboBox (Persistent / Temp : DELETE ROWS / Temp : PRESERVE ROWS). Fields tab: editable DataGrid with **PK | Name | Type | Domain | Size | Scale | Not Null | Default | Computed | Check | Charset | AI | Description**. Description tab: multiline COMMENT ON TABLE. Live DDL preview always-on at the bottom. The grid carries no inline toolbar — all five buttons (⚡ ＋ − ↑ ↓) live in the main toolbar gated on `IsNewTableTabActive`. ⚡ Compile fires `OnNewTableCompileRequested` on the owner, which executes the DDL, refreshes the metadata tree, and closes the tab. Cancel = close × on the tab strip.

**Toolbar reorganization.** New Table button moved AFTER the connection-state group (`▶ ⏹ ↺ ↻`); icon changed from `⊞` to `▦＋` (table glyph + plus) so it visually rhymes with the sidebar's Table category. Both ▦＋ and ▦✎ buttons are tinted by `Foreground="{DynamicResource IconColor_Table}"` so the table icon matches the per-kind colour scheme used in the metadata tree.

**ToggleButton.icon styles.** Avalonia's default `:checked` paints the SystemAccent (orange/brown on Windows). Overrode in [ControlStyles.axaml](src/EmberTern.App/Themes/ControlStyles.axaml) with `SelectionBrush` (background) + `AccentBrush` (1-px border) so toggled state matches the rest of the app's selection/accent visuals. `ToggleButton.icon` selector mirrors `Button.icon`'s defaults so unchecked state reads identically.

**Tests added.** `DdlGeneratorTests` (+22), `AddFieldDialogVmTests` (+18), `PendingDdlVmTests` (+8), `CreateTableDdlTests` (+10), `AlterFieldDdlTests` (+9), `FieldInfoAutoIncrementTests` (+3), `InlineFieldEditTests` (+10), `NewTableTabVmTests` (+9), `TableDetailEditModeTests` (+5). **658 / 658 green** (608 → 658, +50; net delta from session start was 557 → 658, +101).

### Gotchas — promote to architecture lore

56. **`DataGridTemplateColumn.CellEditingTemplate` with `ComboBox` is broken-by-focus in Avalonia 12.** When the cell enters edit mode the DataGrid swaps `CellTemplate` → `CellEditingTemplate`. If the editing template carries a `ComboBox`, the popup either (a) never opens (the chevron is the only entry point), or (b) opens then immediately closes as focus moves to the popup and the cell's pre-popup-open focus pass yanks it back. Trying to force `IsDropDownOpen = true` via `Dispatcher.Post` from `DataGrid.PreparingCellForEdit` makes the race worse — under repeated clicks the cell-edit cycle stops responding and the UI thread hangs. **Fix**: drop `CellEditingTemplate` entirely for `ComboBox`-driven cells. Put the `ComboBox` in `CellTemplate` always-visible, mark the column `IsReadOnly="True"` (so the DataGrid's cell-edit machinery never engages), and gate user interaction via `IsEnabled` bound to a row VM flag derived from your edit-mode toggle. `BorderThickness="0"` + `Background="Transparent"` makes the always-visible combo blend in like a label.

57. **`SelectedValueBinding` is WPF-only.** Avalonia 12 `ComboBox` has `SelectedValue` but no `SelectedValueBinding` property; setting it in XAML is a compile error. For a `ComboBox` whose source list is objects (e.g. `DomainSpec`) and whose bound model property is a string (e.g. `DomainName`), the cleanest pattern is a wrapper property on the row VM — `SelectedDomainSpec` getter looks up the matching item in `AvailableDomains` by name; setter writes `value?.Name` back into `DomainName`. The ComboBox binds `SelectedItem` to the wrapper. Notify the wrapper from the `OnDomainNameChanged` partial so external writes round-trip through the UI.

### Table editor — bugfix + UX polish session (shipped 2026-06-12)

Sesja #1 z planu czteroetapowego (sesje #2-4: menu kontekstowe Pola, kreator Foreign Key, panel zależności pola). Wszystkie zmiany ograniczone do istniejącej powierzchni edytora tabel — brak nowych okien dialogowych ani nowych warstw zapytań.

**1. Scentralizowany refresh po zmianach struktury** ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)). Wprowadzono publiczne `RefreshStructureAsync(CancellationToken)` które wykonuje:
- Snapshot przed reload: `ActiveSubTabIndex`, `SelectedField?.Name`, `SortColumn`/`SortDescending`, `CurrentPage`/`PageSize`, PK aktualnie zaznaczonego wiersza w Dane.
- `_loadTask = null; await EnsureLoadedAsync()` — pełen re-fetch fields/constraints/indexes/DDL/description/data preview.
- `PendingChanges.Clear()` — wszelkie pending DDL stają się nieaktualne wobec świeżej struktury.
- Restore po reload: po nazwie pola znajduje nowo zbudowane `FieldInfo` i ustawia `SelectedField` + lustrzane `SelectedFieldRow` (grid bindowany do `EditableFields`).
- Emituje `StructureRefreshed` event — hook dla widoku gdyby trzeba było odtworzyć stan UI-side.

Wszystkie operacje strukturalne — `ExecuteAddFieldAsync`, `ExecuteDropFieldAsync`, `ExecuteMoveAsync`, `CompileAsync` — przepuszczone przez tę jedną metodę. Wcześniej każda miała własne `_loadTask = null + await EnsureLoadedAsync()`; teraz to jedna ścieżka i jeden punkt dodawania future snapshot pól.

Szerokości kolumn zachowane "za darmo" bo gridy Pola/Constraints/Indexes deklarują kolumny statycznie w XAML (nigdy nie są niszczone), a `PopulateDataGrid` (Dane) ma `sameStructure` check pomijający `Columns.Clear()` gdy nazwy są niezmienne.

**2. Move Up/Down wykonywane od razu** (Fix #4). Wcześniej `MoveFieldUpCommand`/`MoveFieldDownCommand` dodawały tylko `PendingDdlChange` — user widział "compile-needed" stan bez wizualnego efektu. Teraz nowe `ExecuteMoveAsync(name, oneBasedPosition)` (publiczne) odpala `ALTER TABLE … POSITION` natychmiastowo w transakcji użytkownika (auto-begin via DdlExecutor) i wywołuje `RefreshStructureAsync` — symetrycznie do Add/Drop. Pole pozostaje zaznaczone (po nazwie). `AddMovePending` zostawione publicznie jako pure-API do testów + ewentualnego batch-move; testy `PendingDdlVmTests` (2 sztuki) przepisane na ten endpoint.

**3. Rollback wycofuje pending i refreshuje** (Fix #3). [MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs) `OnTransactionStateChanged` wykrywa przejście `Active → Idle` (commit lub rollback) i dla każdego otwartego TableDetail tab wywołuje fire-and-forget `RefreshAfterTransactionAsync` (alias na `RefreshStructureAsync` dla jasności call-site'u). Rzuca `PendingChanges.Clear()` + pełny re-fetch z bazy. Po rollback grid pokazuje stan rzeczywisty w DB; po commit działa idempotentnie.

**4. Tab po Create Table zostaje + przełącza na nową tabelę** (Fix #7). [MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs) `OnNewTableCompileRequested` po udanym CREATE TABLE + `Metadata.RefreshAsync()` zamyka tab NewTable i wywołuje `OnOpenDdlRequested(new MetadataObject(name, Table))`. Idzie przez tę samą ścieżkę co dwuklik na drzewie metadanych — auto-dedup + tworzenie TableDetail tab + EnsureLoadedAsync. User zostaje w kontekście tabeli którą właśnie zbudował.

**5. UPPERCASE dla identyfikatorów** (Fix #6). VM-side coercja przez `partial void OnXxxChanged` + flagę re-entrancy. Pokrycie:
- `NewTableTabViewModel.TableName` + `NewTableFieldRowViewModel.Name`
- `FieldRowViewModel.Name` (inline-edit Pola grid)
- `AddFieldDialogViewModel.FieldName`, `NewGeneratorName`, `TriggerName`

Setter sprawdza czy wartość jest już UPPERCASE; jeśli nie — re-assignuje uppercased pod flagą zapobiegającą rekurencji. Caret w TextBoxie po przejściu na UPPERCASE skacze na koniec stringu (znana cecha PropertyChanged → re-render) — UX akceptowalny. `OnNewTableCompileRequested` dodatkowo robi `trimmed.ToUpperInvariant()` jako belt-and-braces.

**6. Auto-szerokości kolumn** (Fix #5). Wszystkie kolumny w gridach edytora dostały `MinWidth="N"` z N ≈ 7px × długość nagłówka + padding. Wcześniej `DataGridLength.Auto` na grid-level miało pokrywać "max(header, content)", ale w Avalonia 12.0.3 podczas pierwszego measure-pass'u kolumna z pustą zawartością zwija się do minimum bez uwzględnienia szerokości tekstu nagłówka. `MinWidth` gwarantuje że nagłówek nie zostanie obcięty nawet przy pustej kolumnie. Pola/Indeksy grid: wszystkie kolumny dostały MinWidth. NewTableFieldsGrid: zamieniono fixed `Width="N"` na `MinWidth="N"`; Description ostatnia zachowała `Width="*"`.

**7. Cell focus rectangle nie obcinany + niebieski (nie pomarańczowy)** (Fix #1+#2). Dwa źródła problemu:
- FluentTheme paint'uje `DataGridCellFocusVisualPrimaryBrush`/`Secondary` przez SystemAccentColor (orange-brown na typowym Win11). Override w [Colors.axaml](src/EmberTern.App/Themes/Colors.axaml) — oba klucze ustawione na `FocusBorderBrush`. Plus `DataGridCellBackgroundBrushFocused/Selected/SelectedFocused/SelectedUnfocused` ustawione na `SelectionBrush` bo cell-level brush rysuje się NAD row-level `Rectangle#BackgroundRectangle` i pokazywał brąz pomimo overrideu row-level.
- Focus rectangle obcinany — FluentTheme template wkłada `Rectangle x:Name="FocusVisual"` z `Margin=0` co gubiło dolną krawędź. Nowy `Style Selector="DataGridCell /template/ Rectangle#FocusVisual"` w [ControlStyles.axaml](src/EmberTern.App/Themes/ControlStyles.axaml) ustawia `Margin="1"` + `StrokeThickness="1"` + `Stroke="{DynamicResource FocusBorderBrush}"`.

**Zmienione pliki** (10):
- [src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)
- [src/EmberTern.App/ViewModels/MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs)
- [src/EmberTern.App/ViewModels/NewTableTabViewModel.cs](src/EmberTern.App/ViewModels/NewTableTabViewModel.cs)
- [src/EmberTern.App/ViewModels/FieldRowViewModel.cs](src/EmberTern.App/ViewModels/FieldRowViewModel.cs)
- [src/EmberTern.App/ViewModels/AddFieldDialogViewModel.cs](src/EmberTern.App/ViewModels/AddFieldDialogViewModel.cs)
- [src/EmberTern.App/Views/TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml)
- [src/EmberTern.App/Views/NewTableTabView.axaml](src/EmberTern.App/Views/NewTableTabView.axaml)
- [src/EmberTern.App/Themes/Colors.axaml](src/EmberTern.App/Themes/Colors.axaml)
- [src/EmberTern.App/Themes/ControlStyles.axaml](src/EmberTern.App/Themes/ControlStyles.axaml)
- [tests/EmberTern.Tests/PendingDdlVmTests.cs](tests/EmberTern.Tests/PendingDdlVmTests.cs) (2 testy Move przepisane)

**Tests**: 658 / 658 zielone (bez zmiany liczby — 2 testy zmienione, 0 nowych). Build clean. Smoke launch: app startuje + zamyka się czysto.

**Gotchas — promote to architecture lore.**

58. **`DataGridLength.Auto` w Avalonia 12.0.3 nie zawsze respektuje header text width.** Dokumentacja mówi "max(SizeToCells, SizeToHeader)", ale dla kolumn z pustą lub krótką zawartością pierwszy measure-pass zwija column do minimum header'a — bez uwzględnienia jego pełnej szerokości tekstowej. Workaround: ustawić `MinWidth` per-kolumna obliczone jako ~7px × długość header'a + padding. **Reguła w projekcie**: każda kolumna DataGridowa otrzymuje explicit `MinWidth` chroniący header.

59. **FluentTheme DataGridCell ma DWIE warstwy podświetlenia, obie domyślnie biorą SystemAccent.** Pierwsza — focus rectangle (`Rectangle x:Name="FocusVisual"` w template, koloruje się przez `DataGridCellFocusVisualPrimaryBrush`/`Secondary`). Druga — cell-level background fill (`DataGridCellBackgroundBrushSelected/Focused/SelectedFocused/SelectedUnfocused`). Override tylko jednej zostawia brąz na drugiej. Spójny niebieski focus state wymaga override'u obu zestawów kluczy w obu theme dictionaries.

60. **Refresh-after-transaction hook na `Active→Idle`** wymaga zachowania `_previousTransactionState`. CommunityToolkit emitter `TransactionStateChanged` nie carry'uje "from/to" w EventArgs (`EventArgs.Empty`). Wzorzec: trzymać `_previousTransactionState` w polu, porównać z `_transactionService.State` na entry handler'a, aktualizować na końcu. Bez tego nie odróżnisz Active→Idle (commit/rollback completed) od Idle→Active (begin) od Active→Active (statement-count tick).

61. **Move Field "queue and compile" vs "execute immediately" — wybór sygnalizuje user'owi czy chodzi o pojedynczą zmianę czy batch.** Wcześniej Add/Drop było immediate ale Move było queue'd — niespójność która powodowała że user widział pole na miejscu, klikał ↑, nic się nie zmieniało wizualnie. Po decyzji "wszystko strukturalne idzie do bazy od razu, Rollback to escape hatch" wszystkie 3 mają identyczną semantykę. Jeśli kiedyś będzie potrzeba batch (drag-and-drop reorder z 5 polami) — `AddMovePending` zostało jako pure-API call, można zbudować nad nim batch-Compile workflow bez zmiany dotychczasowych ścieżek.

### Table editor — context menu + Edit Field dialog + FK stub (Sesja 2, shipped 2026-06-12)

Sesja #2 z planu czteroetapowego. Zakres: menu kontekstowe na Pola grid, edycja pola przez re-use'owany AddFieldDialog w trybie edit, stub kreatora Foreign Key, plus ekstrakcja wspólnej ścieżki DDL dla inline-edit i dialog-edit. Sesja #3 doda właściwy FK Wizard, sesja #4 panel zależności pola.

**1. Wspólna ścieżka generowania ALTER — `DdlGenerator.BuildAlterStatements`** ([DdlGenerator.cs](src/EmberTern.Core/Metadata/DdlGenerator.cs)). Nowy pure-Core helper:

```csharp
public static IReadOnlyList<PendingDdlChange> BuildAlterStatements(
    string tableName,
    FieldInfo original,
    AlterFieldTarget target,
    bool canRename)
```

Diff oryginalnego `FieldInfo` (loaded state) vs `AlterFieldTarget` (desired state) i emisja minimum-set ALTER statements w bezpiecznej kolejności (rename pierwszy, kolejne ALTERy referencują nową nazwę przez `effectiveName`). Pokrycie:
- Rename — gated by `canRename` (skipped silently gdy `false`; caller surfacuje feedback)
- Type/Domain — przez `TypeClause` (pre-formatted string przez `FormatTypeOrDomain` dla dialogu, raw user input dla inline); też gated by `canRename` (FB odrzuca ALTER TYPE przy zależnościach)
- NotNull — set/drop transition
- Default — set/drop (`null` i `""` traktowane równoważnie — oba oznaczają "no default")
- Description — `COMMENT ON COLUMN` (też `null`≡`""`)

`AlterFieldTarget` — prosta klasa transport-shape (Name / TypeClause / NotNull / DefaultValue / Description), wystarczająca dla obu ścieżek. Inline edit buduje ją z `FieldRowViewModel`; dialog edit konwertuje przez `DdlGenerator.FormatTypeOrDomain(FieldDefinition)` (już istniał, public static).

**2. Refactor inline `EnqueueRowEdits`** ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)). Ze 100+ linii property-by-property diff'u → ~30 linii wrappera nad `BuildAlterStatements`. Inline-specyficzne pozostało: UX wycofujący zmiany w gridzie + komunikat "rename blocked" gdy `canRename=false`. Zero powielania logiki diff między inline i dialog.

**3. EditField + CreateForeignKey commands** ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)):
- `EditFieldCommand` z `CanExecute = CanEditField` (executor wired + SelectedField not null). Wywołuje `EditFieldRequested` event (`Func<FieldInfo, bool, Task<FieldDefinition?>>`), passing `CanRenameField(originalName)` jako gate dla dialog UI.
- `ExecuteEditFieldAsync(FieldInfo original, FieldDefinition target)` — publiczna, owner-callable. Buduje `AlterFieldTarget` z `target`, woła `BuildAlterStatements`, **empty list → no-op** (per spec — user kliknął OK bez zmian, brak DDL, brak refresh, brak modyfikacji ErrorMessage). Inaczej — sekwencyjny `_ddlExecutor.ExecuteAsync` per statement, errors halt + set ErrorMessage, success → `RefreshStructureAsync`.
- `CreateForeignKeyCommand` z `CanExecute = CanCreateForeignKey` (executor present). Fire-and-forget `CreateForeignKeyRequested` (`Func<Task>`); owner-side dialog open. **Stub w Sesji 2 — Session 3 podmieni body dialogu, surface się nie zmieni.**

**4. `AddFieldDialogViewModel` edit mode** ([AddFieldDialogViewModel.cs](src/EmberTern.App/ViewModels/AddFieldDialogViewModel.cs)). Drugi konstruktor:

```csharp
AddFieldDialogViewModel(string tableName, IReadOnlyList<DomainSpec> domains,
                       IReadOnlyList<string> generators,
                       FieldInfo? originalField, bool canRename)
```

Gdy `originalField is not null` — `IsEditMode = true`, `SeedFromField(originalField)` populuje:
- FieldName / NotNull / PrimaryKey / DefaultValue / Description / ComputedExpression
- SelectedDomain (match po nazwie w `Domains` collection)
- SelectedBasicType z `FieldInfo.BaseTypeName` (computed, strips parens)
- Size dla CHAR/VARCHAR/CSTRING, Precision+Scale dla NUMERIC/DECIMAL

`CanRename` flag → driver dla `ShowRenameBlockedHint` + `IsEnabled` na FieldName TextBox. Dodatkowe gate'y view-side:
- `IsAddOnlyTabEnabled` (false w edit mode) → disable tabs Check / Computed / Autoinc
- `IsAddMode` (false w edit mode) → disable PrimaryKey checkbox
- `DialogTitle` zwraca `"Add Field"` lub `"Edit Field — {name}"` przez x:Static format binding

**Czemu jeden dialog, nie osobny EditFieldDialog?** Spec: "Nie twórz osobnego EditFieldDialog jeśli da się wykorzystać AddFieldDialog w trybie edit". Wystarczyła jedna ctor overload, jedna metoda Seed, kilka boolowych flag widoku. Test count rośnie po liniowo, nie kwadratowo (duplicated codebase).

**5. ForeignKeyDialog placeholder** ([ForeignKeyDialog.axaml](src/EmberTern.App/Views/ForeignKeyDialog.axaml)). Minimalny Window: header + body "Coming in Session 3" + Close. Static `ShowAsync(Window owner, string tableName)` zwraca Task — surface przygotowane pod Session 3 (gdzie zwracać będzie `ForeignKeySpec?`).

**6. FieldsGrid context menu + keybindings + double-click** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml) + .cs):
- `ContextMenu` przez `DataGrid.ContextMenu` z 4 itemami (Nowe pole / Edytuj pole / Usuń pole / sep / Utwórz klucz zewnętrzny). InputGesture na każdym pokazuje skrót.
- `DataGrid.KeyBindings`: `Insert` → AddField, `F2` → EditField, `Delete` → DropField. Scope grid-only — żeby F2 w SQL editor nie firowała EditField.
- `DoubleTapped` handler na DataGrid → walk do `DataGridRow`, sprawdź `DataContext is FieldRowViewModel`, fire `EditFieldCommand`. Avalonia DataGrid w edit mode (IsReadOnly=false) intercept'uje double-click pierwsza dla cell-edit, więc dwuklik-edit-pola fires tylko w trybie read-only (przy włączonym IsFieldEditMode trzeba użyć skrótu F2 lub menu).

**Wszystkie 4 wejścia (context menu / keybinding / double-click / toolbar) routują przez te same 4 commandy na VM-ie** — jedyne źródło prawdy dla "co robi New / Edit / Drop / FK".

**Zmienione pliki** (8):
- [src/EmberTern.Core/Metadata/DdlGenerator.cs](src/EmberTern.Core/Metadata/DdlGenerator.cs) — `AlterFieldTarget` + `BuildAlterStatements`
- [src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs) — refactor `EnqueueRowEdits`, dodanie `EditFieldCommand`/`ExecuteEditFieldAsync`/`CreateForeignKeyCommand` + events
- [src/EmberTern.App/ViewModels/AddFieldDialogViewModel.cs](src/EmberTern.App/ViewModels/AddFieldDialogViewModel.cs) — edit-mode ctor + SeedFromField + IsEditMode/CanRename/ShowRenameBlockedHint/IsAddOnlyTabEnabled/DialogTitle
- [src/EmberTern.App/Views/AddFieldDialog.axaml](src/EmberTern.App/Views/AddFieldDialog.axaml) — Title binding, hint visibility, tab IsEnabled gates, PrimaryKey IsEnabled
- [src/EmberTern.App/Views/TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml) — ContextMenu + KeyBindings + DoubleTapped
- [src/EmberTern.App/Views/TableDetailTabView.axaml.cs](src/EmberTern.App/Views/TableDetailTabView.axaml.cs) — `OnEditFieldRequested`, `OnCreateForeignKeyRequested`, `OnFieldsGridDoubleTapped`, shared `OpenAddFieldDialogAsync`
- [src/EmberTern.App/Views/ForeignKeyDialog.axaml(.cs)](src/EmberTern.App/Views/ForeignKeyDialog.axaml) (new) — placeholder
- [src/EmberTern.App/UiStrings.cs](src/EmberTern.App/UiStrings.cs) — `AddFieldDialogEditTitleFormat`, `AddFieldRenameBlockedHint`, `FieldsContextMenu*`, `ForeignKeyDialog*`

**Testy** (+27, 658 → 685): [BuildAlterStatementsTests.cs](tests/EmberTern.Tests/BuildAlterStatementsTests.cs) (13 — pure DDL diff: empty/rename/blocked/type/notnull/default/description/order-of-statements/null-clause/quoting), [AddFieldDialogEditModeTests.cs](tests/EmberTern.Tests/AddFieldDialogEditModeTests.cs) (9 — IsEditMode/seeding/domain-match/numeric-precision-scale/CanRename gate/DialogTitle/IsAddOnlyTabEnabled/BuildDefinition round-trip), [EditFieldCommandTests.cs](tests/EmberTern.Tests/EditFieldCommandTests.cs) (5 — CanExecute gates + no-op-on-empty-diff + event signature pins).

**Architektura — co świadomie zostało**:
- `EnqueueRowEdits` zachowane jako publiczne — view-side RowEditEnding nadal je wywołuje. Wewnętrznie thin wrapper nad `BuildAlterStatements`.
- `AddMovePending` (z sesji 1) zachowane jako pure-API + nadal testowane. Edit dialog **nie** używa pending-queue (executes immediately jak Add/Drop/Move).
- Dialog Computed/Check/Autoincrement tabs są **disabled**, nie ukryte. User widzi czego nie da się zmodyfikować przez ALTER (DROP+ADD wymagałby utraty danych — out of scope). Gdy ktoś kiedyś chce wspierać DROP+ADD, ścieżka jest dodaniem nowej branchy w `BuildAlterStatements` lub osobnego helpera.
- FK Wizard surface: command + event + placeholder dialog. Session 3 podmieni body dialogu na właściwe okno + zaimplementuje serializację → DDL. Surface się nie zmieni.

**Znane ograniczenia**:
1. Edit dialog nie wspiera zmian Computed/Check/Autoincrement/PrimaryKey. Te tabs/kontrolki są disabled. Modyfikacje wymagałyby DROP+ADD (z utratą danych) lub specjalnych ALTER ścieżek FB (FB nie ma `ALTER COLUMN COMPUTED BY`, etc.). Workaround: user może drop'nąć + dodać pole na nowo.
2. BLOB sub-type nie jest carrowane w `FieldInfo` (loader nie pobiera go z `RDB$FIELDS.RDB$FIELD_SUB_TYPE`). Edit mode dla BLOB pola domyśli się TEXT — zmiana sub-type i tak nie jest wspierana przez ALTER.
3. Double-click na pole otwiera Edit Field **tylko gdy grid jest read-only** (default). W trybie IsFieldEditMode=true Avalonia DataGrid intercept'uje double-click pierwsza dla cell-edit. Spec mówił "dwuklik na wierszu pola = Edytuj pole" — interpretacja: w trybie default-read-only. Power user może wymusić edit dialogiem przez F2.
4. Foreign Key command — placeholder dialog returnuje Task bez wartości. Session 3 dodaje ForeignKeySpec.

**Gotchas — promote to architecture lore.**

62. **CommunityToolkit MVVMTK0034 — backing fields are off-limits.** `[ObservableProperty] private string _foo;` generuje public `Foo` property; analyzer wymaga, żeby cały kod (włącznie z ctor'em + helperami w tej samej klasie) referował się przez `Foo`, nie przez `_foo`. Powód: gdyby ktoś write'ował do `_foo` bezpośrednio, PropertyChanged by nie firował i bindowane UI zostawałoby stale. Setup ctor / Seed-from-X / migrations → MUSI używać public property. Wyjątek: w `partial void OnFooChanged(...)` możesz czytać/pisać `Foo` jak normalnie (analyzer rozumie generator-emitted callback). **Rule**: jeśli widzisz `MVVMTK0034`, zamień `_xxx = ...` na `Xxx = ...` w całym setupowym kodzie.

63. **`x:Static` na internal class wymaga InternalsVisibleTo OR public class.** Pierwsze podejście do `AddFieldDialogEditTitleFormat` używało `{Binding DialogTitle}` które wewnętrznie format'owało przez `string.Format(CultureInfo, UiStrings.AddFieldDialogEditTitleFormat, name)` — działa bo VM ma direct access do `UiStrings`. Gdybym próbował `{x:Static app:UiStrings.AddFieldDialogEditTitleFormat}` w XAML — wymagałoby publicznego UiStrings (AXAML loader nie respects InternalsVisibleTo). Format-string trzymany w VM, raw string z dialog'a → bind przez computed property. **Rule**: gdy format-string ma argumenty, formatuj w VM przez computed property — XAML widzi tylko gotowy string przez Binding, nie potrzebuje x:Static do internal class.

64. **DataGrid context menu + keybindings + double-click MUSZĄ wywoływać te same commands.** Inaczej zachowanie się rozjeżdża między input modes. Wzorzec: zdefiniuj N commands na VM, połącz każdą z trzech input ścieżek do tego samego command'a. ContextMenu MenuItem → `Command="{Binding XxxCommand}"`. DataGrid.KeyBindings KeyBinding → `Command="{Binding XxxCommand}"`. DoubleTapped handler w code-behind → `vm.XxxCommand.Execute(null)`. Wszystkie 3 sprawdzają `CanExecute`. Jeśli kiedyś trzeba dodać 4-tą ścieżkę (np. drag-and-drop) — wystarczy podpiąć do command.

65. **Edit dialog "no-op when no change" wymaga diff'u, nie sygnału z dialogu.** Tempting: dodać `IsDirty` flag w dialog VM, gate'ować Accept na tym. Problem: użytkownik może wpisać znak i go zmazać — dialog uznałby się za dirty pomimo identycznego końcowego stanu. **Rule**: no-op semantykę implementuje pipeline DDL (BuildAlterStatements zwraca empty list dla zerowego diff'u), nie dialog. Owner woła `ExecuteEditFieldAsync` zawsze gdy user klika OK — pipeline decyduje czy cokolwiek emitować. Plus: testowalne czysto bez UI.

### Foreign Key Wizard (Sesja 3, shipped 2026-06-12)

Sesja #3 z planu czteroetapowego. Zastąpienie placeholder dialogu z Sesji 2 pełnym kreatorem FK. Architektura siedzi na powierzchniach z Sesji 1–2: `RefreshStructureAsync`, `CreateForeignKeyCommand`/`CreateForeignKeyRequested`, `DdlGenerator.BuildAddForeignKey` (nowy).

**1. Core model — `ForeignKeyAction` + `ForeignKeySpec`** ([ForeignKey.cs](src/EmberTern.Core/Metadata/ForeignKey.cs)). Plain init-only POCO + zamknięty enum.
- `ForeignKeyAction { NoAction, Cascade, SetNull }` — V1 spec. Komentarz w pliku opisuje miejsca do rozszerzenia (`SetDefault`, `Restrict`).
- `ForeignKeySpec { ConstraintName, LocalFields, ReferencedTable, ReferencedFields, OnUpdate, OnDelete }` — `IReadOnlyList<string>` dla pól (kolejność istotna: `LocalFields[i]` → `ReferencedFields[i]`).

**2. `DdlGenerator.BuildAddForeignKey(tableName, spec)`** ([DdlGenerator.cs](src/EmberTern.Core/Metadata/DdlGenerator.cs)). Pure-Core emit. Validation throws (5 osobnych `ArgumentException` per warunek). Shape:

```sql
ALTER TABLE "T" ADD CONSTRAINT "FK_..." FOREIGN KEY ("a", "b") REFERENCES "Y" ("c", "d")
[ ON UPDATE CASCADE | SET NULL ]
[ ON DELETE CASCADE | SET NULL ]
```

`NoAction` omits the clause entirely — matches FB's server-side default i konwencję reader-side `ForeignKeyRule` która suppress'uje RESTRICT przy display. `Cascade` / `SetNull` rendered literally. Order: `ON UPDATE` zawsze przed `ON DELETE`.

**3. `ForeignKeyDialogViewModel`** ([ForeignKeyDialogViewModel.cs](src/EmberTern.App/ViewModels/ForeignKeyDialogViewModel.cs)). Mirror'uje kontrakt `AddFieldDialogViewModel`:
- `[ObservableProperty]` na każdym polu form-state z `[NotifyPropertyChangedFor(nameof(DdlPreview))]` → live preview reaktywne na każdą zmianę
- `SourceFields` + `ReferencedFields` to `ObservableCollection<SelectableFieldViewModel>` (po jednym wrapperze per pole z `IsSelected` flag), bound do dwóch ListBox z checkboxami
- `LoadReferencedFieldsAsync` + `LoadReferencedPrimaryKeyAsync` to dwa callback'i dostarczane przez view — VM nie ma znajomości warstwy bazodanowej (test seam — testy podają synchronous fakes)
- `AvailableActions` to lista `NamedForeignKeyAction(Action, Label)` recordów — ComboBox renderuje Label, BuildSpec używa Action. Bez konwerterów wartości.

**Auto-mapowanie (3 stage)** w `RunAutoMappingAsync`:
- **Stage 1 (by name)**: dla każdego zaznaczonego source field, znajdź same-named field w target. Jeśli WSZYSTKIE zmatchują → propose them. Częściowe matchy fall through to Stage 2 (mieszanka name-matched + PK-matched mylniejsza niż brak propozycji).
- **Stage 2 (by PK)**: gdy Stage 1 fail, fetch PK target table'a. Jeśli PK column count == selected source count → propose PK columns in PK declaration order.
- **Stage 3 (no-op)**: user picks manually. UI hint w XAML mówi explicit: "Equal-named source fields are pre-selected automatically."

**Auto-derive nazwy constrainta**: `FK_{SOURCE}_{TARGET}` na zmianę `SelectedReferencedTable`. **Tylko gdy ConstraintName jest pusty LUB matches the last auto-derived value** — user override sticks across subsequent table changes (test pin'uje to: `UserOverridesName_SticksAcrossTableChange`).

**4. Replaced placeholder dialog** ([ForeignKeyDialog.axaml](src/EmberTern.App/Views/ForeignKeyDialog.axaml) + .cs). Layout: header / constraint name + source table label / dual ListBox (source + target with table picker) / 2× action ComboBox / DDL preview / validation row / Cancel+Create footer. Replaces stub body 1:1; static `ShowAsync(Window owner, ForeignKeyDialogViewModel viewModel) → Task<ForeignKeySpec?>` (nowa sygnatura — VM-injected, view-driven).

**5. Event signature change**: `CreateForeignKeyRequested` z `Func<Task>` → `Func<Task<ForeignKeySpec?>>`. Symmetria z `AddFieldRequested` (`Task<FieldDefinition?>`) i `EditFieldRequested` (`Task<FieldDefinition?>`). VM otrzymuje spec od view → woła `ExecuteCreateForeignKeyAsync` jeśli nie null.

**6. `ExecuteCreateForeignKeyAsync(spec)`** ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)):
- Buduje DDL przez `BuildAddForeignKey` (catch `ArgumentException` defensive — dialog's `IsValid` powinien już to złapać)
- `_ddlExecutor.ExecuteAsync(sql)` w transakcji użytkownika (auto-begin)
- Errors land w `ErrorMessage` z formatu `UiStrings.ForeignKeyExecuteFailedFormat`
- Success → `RefreshStructureAsync()` (refetch constraints/DDL z bazy + restore snapshot)
- **Post-create UX**: po refresh override'uje snapshot-restored sub-tab na `ConstraintsSubTabIndex (=1)` + ustawia `ConstraintsActiveSubTabIndex = ConstraintsForeignKeysIndex (=1)`. User ląduje na Ograniczenia → Foreign Keys i widzi nowy constraint w liście.

**7. Inner `ConstraintsActiveSubTabIndex` binding** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml)). Wewnętrzny `TabControl` w Ograniczenia teraz ma `SelectedIndex="{Binding ConstraintsActiveSubTabIndex, Mode=TwoWay}"`. Default = 0 (Primary Key). Post-FK flow ustawia na 1 (Foreign Keys). Konstanty: `ConstraintsSubTabIndex` + `ConstraintsForeignKeysIndex` na VM-ie (matching XAML order).

**8. View wiring** ([TableDetailTabView.axaml.cs](src/EmberTern.App/Views/TableDetailTabView.axaml.cs) `OnCreateForeignKeyRequested`):
- Snapshot source-table field names z `_currentVm.Fields`
- Best-effort `mainVm.MetadataReader.ListAsync(Table)` dla tablistę (failure → empty list)
- Dwie callback closure: `LoadFields(tableName)` + `LoadPrimaryKey(tableName)` — obie używają `mainVm.TableDetailReader.GetFieldsAsync(tableName)` (nowo wyeksponowanego). PK callback filtruje `FieldInfo.IsPrimaryKey == true` — jedno-zapytanie pokrywa oba uses. Tańsze niż osobne `GetConstraintsAsync` dla PK.
- Buduje VM, woła `ForeignKeyDialog.ShowAsync` → zwraca `ForeignKeySpec?` z powrotem do VM.

**`MainWindowViewModel.TableDetailReader`** (nowa internal property) — symmetria z istniejącym `MetadataReader`. Wyeksponowane bo callback'i FK wizarda potrzebują dostępu do `GetFieldsAsync` dla ref-table'a.

**Zmienione pliki** (9):
- [src/EmberTern.Core/Metadata/ForeignKey.cs](src/EmberTern.Core/Metadata/ForeignKey.cs) (new) — enum + spec
- [src/EmberTern.Core/Metadata/DdlGenerator.cs](src/EmberTern.Core/Metadata/DdlGenerator.cs) — `BuildAddForeignKey` + helpers
- [src/EmberTern.App/ViewModels/ForeignKeyDialogViewModel.cs](src/EmberTern.App/ViewModels/ForeignKeyDialogViewModel.cs) (new) — wizard VM + auto-mapping + auto-naming
- [src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs) — `ConstraintsSubTabIndex`/`ConstraintsForeignKeysIndex`/`ConstraintsActiveSubTabIndex`/`ExecuteCreateForeignKeyAsync`/event signature change
- [src/EmberTern.App/ViewModels/MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs) — `TableDetailReader` exposure
- [src/EmberTern.App/Views/ForeignKeyDialog.axaml(.cs)](src/EmberTern.App/Views/ForeignKeyDialog.axaml) — placeholder → real wizard
- [src/EmberTern.App/Views/TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml) — Constraints TabControl SelectedIndex binding
- [src/EmberTern.App/Views/TableDetailTabView.axaml.cs](src/EmberTern.App/Views/TableDetailTabView.axaml.cs) — `OnCreateForeignKeyRequested` rewrite
- [src/EmberTern.App/UiStrings.cs](src/EmberTern.App/UiStrings.cs) — FK wizard strings + action labels

**Testy** (+29, 685 → 714): [BuildAddForeignKeyTests.cs](tests/EmberTern.Tests/BuildAddForeignKeyTests.cs) (14 — single/multi-field, action variants, NoAction omits clause, identifier quoting, full validation coverage), [ForeignKeyDialogVmTests.cs](tests/EmberTern.Tests/ForeignKeyDialogVmTests.cs) (15 — default name derivation, user override sticks, 3-stage auto-mapping, multi-field PK auto-map preserves order, validation cases, DDL preview reactivity, full round-trip BuildSpec → BuildAddForeignKey, Accept/Cancel commands). Plus 1 updated test w `EditFieldCommandTests` (event signature change from Sesji 2).

**Architektura — co świadomie zostało**:
- Edit dialog (Session 2) i FK dialog (Session 3) używają **różnych dialog VMs** — różne form-shapes (FieldDefinition vs ForeignKeySpec). Wspólne: oba zwracają wynik przez `Result` property + fire `RequestClose` event. Brak unifikującej "DialogViewModelBase" — niepotrzebna abstrakcja na zapas.
- Auto-mapowanie wyłącznie *sugeruje* — nie blokuje. User zawsze może rozregulować propozycję ręcznie. Test `AutoMap_Stage3_NoOp` pin'uje że nie ma wymuszania ani na pustej, ani na partial-matched scenarii.
- Dialog **nie** loaduje ref-table fields ahead-of-time — tylko on-pick. Otwarcie wizarda nie blokuje UI na DB roundtrip listujący każdą tabelę. Tradeoff: pierwszy klik na tabelę docelową = jeden roundtrip (akceptowalne, 50ms typical).
- Nazwa constrainta auto-derive używa flagi `_lastAutoDerivedName` zamiast osobnej `IsCustom` bool. Niespodziewanie ekonomiczne — bo `OnConstraintNameChanged` rozpoznaje user override po fakcie (`value != _lastAutoDerivedName` po `_coercingName=false` cycle).

**Znane ograniczenia**:
1. **Brak highlight'u nowo utworzonego FK na liście.** DataGrid w FK sub-tabie nie ma `SelectedItem` binding'u. Dodanie selekcji wymagałoby (a) `SelectedForeignKey` na VM, (b) binding'u, (c) `OnPropertyChanged` po Refresh — niemała przebudowa. Spec dał discretion: "Jeżeli zaznaczenie wymagałoby dużej przebudowy — opisz ograniczenie". Pominięto. User widzi nowy FK w liście (sortowanie domyślne przez Firebird zazwyczaj alfabetyczne lub po dacie utworzenia).
2. **Brak inline drag-reorder pól.** Jeśli user wybierze [A, B, C] w source i auto-mapping da [X, Y, Z] w target, mapping = A→X, B→Y, C→Z (by ordered selection-position). Composite FK z innym mapping'iem wymaga ręcznej zmiany kolejności w XAML — nie jest wspierane. V1 spec tego nie wymagał.
3. **Brak walidacji compatibility typów.** Dialog nie sprawdza czy A:VARCHAR mapuje na X:INTEGER. FB sam odrzuci execution przy create — błąd wyląduje w `ErrorMessage`. Server-side validation tańsza i autorytatywna.
4. **`SetDefault` + `Restrict` nie wspierane** (spec V1). `ForeignKeyAction` enum + `RenderAction` + dialog's `AvailableActions` to 3 miejsca do dodania linii każdej, jeśli kiedyś trzeba.
5. **Cyclic FK (table referencing itself) jest legalny** — bieżąca tabela pokaże się w `AvailableTables` (lista wszystkich tabel w DB). User może wybrać self-ref. Test'em nie pinowane, ale powierzchnia działa.
6. **Brak loading indicatora** podczas fetch'a ref-fields. Async + szybkie zwykle — ale na powolnej sieci user może zauważyć ~100-500ms freeze przed pojawieniem się listy. V2 candidate: subtle `IsLoading` flag + spinner.

**Gotchas — promote to architecture lore.**

66. **Auto-derive form values musi mieć "user override sticks" semantykę przez `_lastAutoDerivedValue` tracking.** Pattern: VM auto-derive'uje wartość X (np. constraint name `FK_SRC_TGT`) na zmianę source-Y. Gdy user edytuje X ręcznie, kolejne zmiany Y NIE MOGĄ override'ować jego edycji. Implementacja:
    - `_lastAutoDerivedValue` field trackuje "what we last wrote automatically"
    - `_coercingX` re-entrancy flag chroni przed cyklem na auto-write
    - `OnXChanged(value)`: gdy `_coercingX==false`, user write → zresetuj `_lastAutoDerivedValue = ""` (signal "user pinned this")
    - Auto-derive logic: replace ONLY when `X` jest empty OR `X == _lastAutoDerivedValue`. To pierwsze pokrywa initial state, drugie subsequent auto-overrides.

    Ten wzorzec jest test'owalny: `UserOverridesName_SticksAcrossTableChange` pin'uje semantykę bez touch'owania UI.

67. **xUnit2031 — `Assert.Single` z lambdą zamiast `Where().Single()`.** Analyzer xUnit'a flag'uje `Assert.Single(coll.Where(p))` i wymaga `Assert.Single(coll, p)` (predicate overload). Działa identycznie ale generuje lepszy error message przy fail'u (mówi ile elementów spełniało predykat, nie tylko "expected 1 got N"). Razem z `xUnit2029` (`Assert.Empty(coll.Where(p))` → `Assert.DoesNotContain(coll, p)`) trzymaj w pamięci przy pisaniu testów na collection-predicate assertions — IDE pokaże errory ale to brak zwykłych warning'ów.

68. **Dialog VM-driven over dialog-loaded.** Wcześniejsze podejście (Session 2 AddFieldDialog): view-side `OpenAddFieldDialogAsync` fetch'ował domains+generators **przed** stworzeniem VM-a. Session 3 zmienia paradygmat: VM dostaje **callbacks** (Func<T1, Task<T2>>) i loaduje on-demand sam (gdy user picks target table). Plusy: (a) test'owalne synchronicznymi fake'ami, (b) tańsze open (nie blokuje na fetch'u tablistę całego DB), (c) re-fetch on user action darmowy (e.g. user changes target → fresh fields). **Rule**: dla future wizard'ów (Session 4 panel zależności, wszelkie kolejne) wybieraj callback'i over ahead-of-time fetch — szczególnie gdy dane są user-driven (target selection trigger'uje fetch).

### Field Dependencies Panel (Sesja 4, shipped 2026-06-12)

Sesja #4 — finalna z planu czteroetapowego. Dolny panel na Pola sub-tab pokazujący zależności wybranego pola. Bez nowych I/O — wykorzystuje już-załadowane `DependedOnBy` z `RefreshStructureAsync`.

**1. `FieldDependencyItem` wrapper VM** ([FieldDependencyItem.cs](src/EmberTern.App/ViewModels/FieldDependencyItem.cs)). Cienki view-side wrapper nad `DependencyInfo`:
- `ObjectName` / `ObjectType` — przekazywane verbatim
- `CanNavigate` — computed przez existing `TableDetailTabViewModel.MapObjectTypeToKind` (same mapping co tree-side Zależności). True dla Table/View/Trigger/Procedure/Function/Generator/Exception/Package/Index/User/Domain. False dla "Field" i fallback "Object (N)".
- `NavigateCommand` — `[RelayCommand(CanExecute = nameof(CanNavigate))]`. Wired do existing `_owner.RequestOpen(Info)` chain (which fires `OpenObjectRequested` → `MainWindowViewModel.OnOpenDdlRequested`). **View nie binduje command'u do żadnego gestu w Sesji 4** — wsparcie API jest, Session 5 (poza planem 4 sesji) wire'uje DoubleTapped na DataGrid.

**Dlaczego wrapper, nie rozszerzenie `DependencyInfo`?** `DependencyInfo` to Core POCO z `init`-only properties. Dodawanie commandów / wireup'u do `MainWindowViewModel` w Core warstwie złamałoby regułę "zero Avalonia in Core". Wrapper w App.ViewModels — naturalny rozdział.

**2. `FieldDependencies` collection na `TableDetailTabViewModel`**:
- `ObservableCollection<FieldDependencyItem> FieldDependencies` (publiczna)
- Computed flags: `HasFieldDependencies`, `HasFieldSelectionForDependencies`, `ShowFieldDependenciesEmpty`, `ShowFieldDependenciesNoSelection` — driving UI state (Empty vs NoSelection vs DataGrid).
- `RebuildFieldDependencies()` — filtruje `DependedOnBy` gdzie `FieldName == SelectedField.Name` (case-insensitive, bo Firebird przechowuje uppercase ale defensive against mixed-case input), dedup po `(ObjectType|ObjectName)`, wraps w `FieldDependencyItem(dep, this)`.
- **Rebuilds wired do dwóch sygnałów**:
  - `partial void OnSelectedFieldChanged` — user zmienia selekcję
  - `DependedOnBy.CollectionChanged` — refresh structure clears+repopulates collection, każdy Add fires rebuild

**Brak `_lastRebuildedField` cache / debounce.** Typowa tabela ma ≤20 deps; podczas `RefreshStructureAsync` collection Clear() + N×Add() daje N+1 rebuilds @ O(n) — total O(n²) ale n małe, koszt znikomy. Optymalizacja niepotrzebna.

**3. Pola sub-tab UI** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml)). Pola TabItem wrapnięte w `Grid RowDefinitions="*,4,180"`:
- Row 0: `FieldsGrid` (istniejący, niezmieniony co do contentu — tylko `Grid.Row="0"` dodane)
- Row 1: `GridSplitter Height="4" ResizeDirection="Rows"` — user resize'uje pionowo
- Row 2: `Border` z headerem ("Field dependencies") + `Grid RowDefinitions="Auto,*"`:
  - Trzy mutually-exclusive children w row 1:
    - `TextBlock` "Select a field…" (gdy `ShowFieldDependenciesNoSelection`)
    - `TextBlock` "This field has no dependencies." (gdy `ShowFieldDependenciesEmpty`)
    - `DataGrid` z kolumnami Type / Name (gdy `HasFieldDependencies`)

Default panel height = 180 px. Identyczna stylistyka grid'u jak Pola/Indeksy/Ograniczenia (compact rows, font 11, MinWidth per kolumna chronący nagłówki). Read-only — żadnych binding'ów edycyjnych.

**4. UiStrings** — 5 nowych const: `FieldDependenciesHeader`, `FieldDependenciesNoSelection`, `FieldDependenciesEmpty`, `FieldDependenciesColumnType`, `FieldDependenciesColumnName`.

**Zmienione pliki** (4):
- [src/EmberTern.App/ViewModels/FieldDependencyItem.cs](src/EmberTern.App/ViewModels/FieldDependencyItem.cs) (new) — wrapper VM + NavigateCommand
- [src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs) — FieldDependencies collection + rebuild + hooks
- [src/EmberTern.App/Views/TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml) — Pola sub-tab Grid + Splitter + bottom panel
- [src/EmberTern.App/UiStrings.cs](src/EmberTern.App/UiStrings.cs) — 5 string'ów

**Testy** ([FieldDependenciesPanelTests.cs](tests/EmberTern.Tests/FieldDependenciesPanelTests.cs), +11, 714 → 725):
- No-selection state flags
- Selection-with-no-matches → empty state
- Filter by field name + case-insensitivity
- Dedup by (ObjectType, ObjectName) — same trigger across multiple fields → one row
- Dedup key honors ObjectType (different kinds with same name don't collapse)
- CanNavigate true/false for known/unknown kinds
- CanNavigate gates NavigateCommand.CanExecute
- Selection change rebuilds list
- DependedOnBy.CollectionChanged triggers rebuild (simulates RefreshStructureAsync flow)
- FieldDependencyItem exposes ObjectName/ObjectType + holds raw Info reference

**Architektura — co świadomie zostało**:
- **No new I/O**. Sesja 4 jest czysto-VM + UI. `GetDependenciesAsync` istniejący Sesji wcześniej już populuje `DependedOnBy` — filter+dedup to czysta operacja w pamięci.
- **NavigateCommand wired ale nie bound** w XAML. API surface ready dla Session 5; UI gesture (DoubleTapped) doda się trywialnie. Pełne wzorce z `OnDependencyNodeDoubleTapped` na drzewie Zależności są do reuse.
- **Brak Insert/Update/Delete kolumn dla trigger'ów**. Spec gave discretion ("jeżeli relatywnie małym kosztem — dodaj"). Dodanie wymagałoby:
  - Rozszerzenia `DependencyInfo` o opcjonalne `OperationFlags` (bitfield)
  - Modyfikacji `DependedOnBySql` o LEFT JOIN `RDB$TRIGGERS` na `RDB$DEPENDENT_TYPE=2`, plus dodatkowy SELECT z `RDB$TRIGGER_TYPE`
  - Decoder bitfield client-side (logika już exists w `FirebirdDdlReader.DescribeTriggerType` — można re-use)
  - Re-bind paramters w UNION ALL branches (gotcha #47 from sesji wcześniejszych — distinct param names per branch)
  
  Estymata: ~50 lines reader + 1 DependencyInfo field + 3 columns w XAML + 5 tests. Możliwe ale `Priorytetem jest działający panel zależności` — zostawiam jako V2 candidate.

**Znane ograniczenia**:
1. **Brak Insert/Update/Delete dla triggerów** (j.w.). User widzi że trigger "TR_NAGL_AI" zależy od pola, ale nie wie czy odpala się na INSERT, UPDATE czy DELETE. W table-level zakładce Zależności też tego nie ma — wymagałoby spójnej rozbudowy obu miejsc.
2. **Index dependencies nieobecne**. `DependedOnBy` zbiera tylko `RDB$DEPENDENCIES` rows + indirect-via-domain Views. Indexes (`RDB$INDICES`) są separate source — nie pojawiają się w bieżącym filterze. Spec wymienił "Index" jako przykład — ale spec też mówił "wykorzystaj już załadowane dane". Indexes są w `Indexes` collection osobno; per-field index lookup wymagałby joinu po `RDB$INDEX_SEGMENTS.RDB$FIELD_NAME`. V2.
3. **Foreign Key dependencies (incoming)** — istnieją w `DependedOnBy` jako rows z `ObjectType = "Foreign Key"`? Sprawdzić: bieżący `MapObjectType` w readerze mapuje `RDB$DEPENDENT_TYPE = 7 (RDB$_CONSTRAINT)` → "Constraint" (nie "Foreign Key"). FK incoming w czystej formie zazwyczaj nie wpada w `RDB$DEPENDENCIES`. Better source: `RDB$REF_CONSTRAINTS` joined z `RDB$RELATION_CONSTRAINTS`. V2 — wymaga osobnego query albo zaszczepienia FK info z istniejącego `Constraints` collection (gdzie FK info już mamy z `GetConstraintsAsync`).
4. **Brak otwierania obiektów po dwukliku** — explicit spec. NavigateCommand exists, UI gesture w Session 5.
5. **Brak grupowania kolumn po Type** — flat DataGrid (sort by Type to fallback). IBExpert ma tree-shape po typie; ale w naszych prostych use-case'ach (1-10 wierszy max per field) flat grid + Type-sort jest czytelniejsze.

**Gotchas — promote to architecture lore.**

69. **Filtered-view ObservableCollection — wireup do dwóch źródeł sygnału**. Gdy chcesz pokazać filtered subset jakiejś collection (np. zależności pola = filtered DependedOnBy + selected field), musisz reagować na:
    - Zmianę kryterium filtra (tu: SelectedField → `partial void OnXxxChanged`)
    - Zmianę zbioru źródłowego (tu: DependedOnBy → CollectionChanged event)
    
    **Pomijanie któregokolwiek = stale data**. CollectionChanged ważniejszy bo Clear+Add sequence z `RefreshStructureAsync` może zostawić zfiltrowany panel z danymi sprzed refresh'u. Subscribe w ctorze, unsubscribe nie jest potrzebny dla tego VM (lifetime tabu).

70. **Wrapper VM dla view-side commands na Core POCO**. `DependencyInfo` (Core, init-only) nie może mieć `[RelayCommand]` — to App-only attribute. Wrapper VM (App.ViewModels) trzyma `DependencyInfo Info { get; }` jako referencję + dodaje view-side state (`CanNavigate`, `NavigateCommand`). Konstruktor bierze opcjonalny `TableDetailTabViewModel? owner` — null OK dla testów (tworzenie itemu w izolacji), production zawsze passes `this`. **Rule**: gdy Core model potrzebuje view-side behavior (command, computed property tied to App services), opakuj — nie rozszerzaj.

### UX Polish Sprint (Sesja 5, shipped 2026-06-13)

Sześć dopracowań po ręcznych testach. Bez nowych dużych funkcji — naprawy + drobne rozszerzenia istniejących mechanizmów.

**1. Fałszywe oznaczanie wierszy jako modified (root cause + fix).** **Przyczyna**: Type ComboBox na Pola grid bindował `SelectedItem="{Binding TypeText}"` gdzie `TypeText = original.Type` = `"VARCHAR(50)"`, ale `ItemsSource = BasicTypes` zawiera tylko bazowe typy (`"VARCHAR"`, `"INTEGER"`…). Avalonia ComboBox nie znajduje `"VARCHAR(50)"` w items → resetuje `SelectedItem` na `null` → TwoWay zapisuje `null` z powrotem do `TypeText` → `IsModified` (porównujące `TypeText` vs `Original.Type`) staje się `true` → brązowy tint mimo braku edycji. Analogicznie Domain ComboBox: `SelectedDomainSpec` getter zwraca `null` gdy `AvailableDomains` jeszcze nie załadowane (ładują się async PO zbudowaniu wierszy) → setter zerował `DomainName`. **Fix** ([FieldRowViewModel.cs](src/EmberTern.App/ViewModels/FieldRowViewModel.cs)):
- Nowy `SelectedTypeItem` wrapper: getter zwraca bazowy typ (strip parens) gdy jest w `BasicTypes`, inaczej null; setter ignoruje null/empty (load-time writeback) i no-opuje przy tym samym bazowym typie. `TypeText` zachowuje pełną formę → `IsModified` poprawne.
- Domain `SelectedDomainSpec` setter ignoruje `value is null` (nie ma "clear domain" entry, więc null to zawsze artefakt bindowania).
- `AvailableDomains.CollectionChanged` → re-raise `SelectedDomainSpec` żeby combo wybrało właściwą domenę gdy lista dojedzie async.
- `EnqueueRowEdits` ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)): typeClause pre-filter — ustawiany TYLKO przy realnej zmianie typu/domeny (porównanie base-to-base + domain-changed), inaczej domain-typed kolumny emitowały spurious ALTER (bo `original.Type` to rozwiązany typ, nie nazwa domeny). Po RefreshStructureAsync nowe `FieldRowViewModel` mają IsModified=false → brak stale brązu.

**2. Dwuklik + Enter w panelu zależności pola.** `FieldDependenciesGrid` (nowy x:Name) ma `DoubleTapped="OnFieldDependencyDoubleTapped"` (walk do `DataGridRow` → `FieldDependencyItem.NavigateCommand` jeśli `CanExecute`) + `KeyBinding Gesture="Enter"` → `NavigateSelectedDependencyCommand`. Nowy `[ObservableProperty] SelectedFieldDependency` + `NavigateSelectedDependencyCommand` na VM. Wszystko routuje przez istniejące `FieldDependencyItem.NavigateCommand` → `owner.RequestOpen(Info)` → `OpenObjectRequested` → `MainWindowViewModel.OnOpenDdlRequested` — ta sama ścieżka co drzewo metadanych i tree Zależności. Zero nowej ścieżki otwierania.

**3. Ikony typów w panelu zależności.** `FieldDependencyItem` dostał `Icon` + `IconResourceKey` przez `MetadataNodeViewModel.IconFor`/`ResourceKeyFor` + `MapObjectTypeToKind` (te same co drzewo). Type kolumna w panelu → `DataGridTemplateColumn` z glyph (przez `IconBrushConverter` + `RootControl.ActualThemeVariant` MultiBinding) + tekst typu. Theme-aware, recoloring live. Zero drugiego zestawu ikon.

**4. Kolumny Insert / Update dla zależności.** `DependedOnBySql` rozszerzony o 4. kolumnę `RDB$TRIGGER_TYPE` (LEFT JOIN `RDB$TRIGGERS` na `RDB$DEPENDENT_TYPE = 2`, NULL dla nie-triggerów). Reader dekoduje przez nowy `FirebirdTableDetailReader.DecodeTriggerOps(type)` → `(insert, update, delete)` używając FB packed-slot formula `((type+1) >> (2*slot+1)) & 3`. `DependencyInfo` dostał `bool? FiresOnInsert` / `FiresOnUpdate` (null dla nie-triggerów). `FieldDependencyItem.InsertMark`/`UpdateMark` → `"✓"` lub pusto. Dwie kolumny w panelu. **Delete pominięty** (spec: priorytet Insert/Update). **Procedury**: Firebird nie trzyma per-field operation semantyki dla procedur, więc tam Insert/Update zawsze puste — tylko triggery mają sensowną informację (zakodowaną w trigger type).

**5. Menu kontekstowe tabel w drzewie metadanych.** `MetadataNodeViewModel` dostał `IsTableGroup` (kategoria Tables) + `IsTableLeaf` (liść Table) flagi + komendy `NewTable` / `DeleteTable`. Menu w [MainWindow.axaml](src/EmberTern.App/Views/MainWindow.axaml): kategoria Tables → "New Table" (reuse `NewTableCommand`); liść tabeli → "Open" / "Design Table" (oba przez `OpenDdlCommand` → TableDetail tab, bez duplikacji logiki) / "Delete Table". Eventy `NewTableRequested` / `DeleteTableRequested` na `MetadataExplorerViewModel` → owner.

**6. Delete Table.** `OnDeleteTableRequested` w MainWindowViewModel: confirm dialog ("Are you sure you want to delete table X?") → `DdlGenerator.BuildDropTable(name)` (nowy, `DROP TABLE "X"`) → `_ddlExecutor.ExecuteAsync` → `CloseTabsForObject(kind, name)` (zamyka otwarte Ddl/TableDetail taby tej tabeli) → `Metadata.RefreshAsync()`. Błąd FB (w tym dependency error) surfacowany w Messages bez prób auto-usuwania zależności.

**Zmienione pliki** (9): [FieldRowViewModel.cs](src/EmberTern.App/ViewModels/FieldRowViewModel.cs), [TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs), [FieldDependencyItem.cs](src/EmberTern.App/ViewModels/FieldDependencyItem.cs), [MetadataNodeViewModel.cs](src/EmberTern.App/ViewModels/MetadataNodeViewModel.cs), [MetadataExplorerViewModel.cs](src/EmberTern.App/ViewModels/MetadataExplorerViewModel.cs), [MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs), [TableDetail.cs](src/EmberTern.Core/Metadata/TableDetail.cs) (DependencyInfo flags), [DdlGenerator.cs](src/EmberTern.Core/Metadata/DdlGenerator.cs) (BuildDropTable), [FirebirdTableDetailReader.cs](src/EmberTern.Firebird/FirebirdTableDetailReader.cs) (DependedOnBySql + DecodeTriggerOps), [TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml), [MainWindow.axaml](src/EmberTern.App/Views/MainWindow.axaml), [TableDetailTabView.axaml.cs](src/EmberTern.App/Views/TableDetailTabView.axaml.cs), [UiStrings.cs](src/EmberTern.App/UiStrings.cs).

**Testy** ([UxPolishSprintTests.cs](tests/EmberTern.Tests/UxPolishSprintTests.cs), +29, 725 → 754): modified-row detection (fresh-from-catalog / SelectedTypeItem display / null-writeback ignored / same-base no-op / real change / domain null-guard), DecodeTriggerOps (10 Theory rows + DB-level all-false), DependedOnBySql column pin, trigger marks, dependency navigation (NavigateCommand + NavigateSelected), BuildDropTable + empty-throws, table context-menu flags (group/leaf/view-leaf), New/Delete command dispatch.

**Ograniczenia**:
1. **Delete dla zależności pominięty** — tylko Insert/Update (spec). Trigger type dekoduje też delete (`DecodeTriggerOps` zwraca 3-krotkę), ale UI nie pokazuje. Dodanie kolumny Delete to ~1 linia VM + 1 kolumna XAML.
2. **Insert/Update tylko dla triggerów** — procedury/widoki/inne nie mają per-field operation semantyki w katalogu FB. Pokazują puste.
3. **Index dependencies wciąż nieobecne w panelu** (z Sesji 4) — `RDB$INDICES` to osobne źródło, nie wpada w `RDB$DEPENDENCIES`. V2.
4. **Open vs Design Table identyczne** — oba otwierają TableDetail (spec dopuścił). Gdy powstaną osobne detail-views dla różnych trybów, rozdzielą się.
5. **Delete Table nie kaskaduje** — czysty `DROP TABLE`. FK/zależności → błąd FB pokazany userowi (spec: nie auto-usuwać).

**Gotchas — promote to architecture lore.**

71. **Avalonia ComboBox z `SelectedItem` TwoWay + `ItemsSource` którego bound-value NIE zawiera = cicha korupcja bound property.** Gdy `SelectedItem="{Binding X}"` a `X` nie jest w `ItemsSource`, ComboBox resetuje SelectedItem na null i TwoWay zapisuje null z powrotem do X. Dla pól pochodnych (np. `IsModified` porównujące X vs original) to fałszywe "zmienione". Dwa scenariusze: (a) bound value w innej reprezentacji niż items (pełny typ "VARCHAR(50)" vs bazowe items), (b) ItemsSource ładowane async PO bindowaniu (puste w momencie ataczowania → każdy lookup zwraca null). **Fix**: wrapper property z getterem zwracającym null-bezpiecznie + setterem ignorującym null/empty writeback (`if (value is null) return;`). Trzymaj prawdziwą wartość w osobnym property, nie pozwól ComboBoxowi jej nadpisać.

72. **`MetadataObject` to klasa (reference type), nie struct/record-struct** — `MetadataObject? x` to nullable reference, NIE `Nullable<T>`. Brak `.Value`; dostęp przez `x!.Name`. Łatwa pomyłka w testach gdy piszesz `x.Value.Kind` z nawyku od struct-recordów.

73. **Rozszerzanie współdzielonego SQL o kolumnę jest bezpieczne gdy readery czytają po indeksie i nowa kolumna jest ostatnia.** `DependedOnBySql` używane przez table-level tree ORAZ field-panel. Dodanie 4. kolumny (`RDB$TRIGGER_TYPE`) nie zepsuło drzewa (czyta tylko 0-2, ignoruje 3). UNION ALL wymaga tej samej liczby kolumn w obu branchach → drugi branch dostał `CAST(NULL AS INTEGER)`. **Rule**: dokładając kolumnę do współdzielonego query trzymaj ją na końcu SELECT-listy i upewnij się że wszystkie branche UNION ją mają.

### Destructive-operation confirmation audit (shipped 2026-06-13)

Po zgłoszeniu, że usunięcie połączenia nie pytało o potwierdzenie (utrata configu + saved queries + workspace), pełny audyt operacji destrukcyjnych. Wszystkie używają jednego `ConfirmDialog` przez `RequestConfirmAsync` / `ConfirmRequest`.

**Pełna lista operacji + stan PRZED audytem:**

| # | Operacja | Lokalizacja | Confirm przed? | Ryzyko |
|---|---|---|---|---|
| 1 | **Delete connection** | `MainWindowViewModel.Delete` ← node Delete + toolbar DeleteSelected | ❌ **BRAK** | **HIGH** |
| 2 | Delete folder | `DeleteFolderAsync` | ✓ | Medium |
| 3 | Delete Table (DROP TABLE) | `OnDeleteTableRequested` | ✓ | HIGH |
| 4 | Drop Field (ALTER … DROP) | `DropFieldAsync` | ✓ | HIGH |
| 5 | Delete row (Dane) | `DeleteRowAsync` | ✓ | HIGH |
| 6 | Delete saved query | `DeleteSavedQueryAsync` | ✓ | Medium |
| 7 | Clear all queries | `ClearAllQueriesAsync` | ✓ | Medium |
| 8 | **Clear editor** | `ClearActiveEditor` | ❌ **BRAK** | Low/Medium |
| 9 | **Close New Table tab** | `CloseTab` (przez × / CloseActiveTab) | ❌ **BRAK** | Medium (niezapisany formularz) |
| — | Close DDL/TableDetail tab | `CloseTab` | ❌ (celowo) | None (reopenable z drzewa) |
| — | Disconnect z aktywną tx | `DisconnectAsync` | ✓ (istniejący rollback-confirm) | — |

**Poprawione (3):**
- **#1 Connection delete** ([MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs)): nowy `DeleteWithConfirmationAsync(profile)` z bogatym ostrzeżeniem ("Are you sure… '{0}'?" + bullet list: settings lost / linked saved queries removed / cannot be undone, `IsDestructive=true`). Raw `Delete(profile)` zachowany jako post-confirm executor + dla testów. `ConnectionNodeViewModel.Delete` command → async przez wrapper; `MetadataExplorerViewModel.DeleteSelected` routuje przez ten sam command. **To była zgłoszona regresja.**
- **#8 Clear editor**: `ClearActiveEditorAsync` pyta gdy `QueryText` niepusty (pusty → cicho, brak pointless prompt). `IsDestructive=true`.
- **#9 Close New Table tab**: nowy `RequestCloseTabAsync(tab)` — pyta TYLKO dla NewTable z `HasContent` (nazwa tabeli LUB ≠1 pole; świeży tab = pusta nazwa + seeded ID → bez promptu). User paths (`WorkspaceTabViewModel.Close` command + `CloseActiveTab`) idą przez wrapper; **programmatic `CloseTab`** (post-compile, delete-table cleanup) zostaje bez promptu. DDL/TableDetail zamykają się cicho (reopenable).

**Zweryfikowane (już miały spójny confirm)**: Delete folder, Delete Table, Drop Field, Delete row, Delete saved query, Clear all queries — wszystkie English, `IsDestructive=true`, nazwa obiektu w komunikacie, wzmianka o cofnięciu/rollbacku gdzie stosowne.

**Celowo bez potwierdzenia:**
- **Close DDL/TableDetail tab** — zero utraty danych, w pełni odtwarzalne z drzewa metadanych podwójnym klikiem.
- **Raw `Delete(profile)`** — to executor PO potwierdzeniu (wrapper już zapytał) + szew testowy.
- **`ConnectionListItemViewModel`** — martwy kod od Explorer Redesign, niepodłączony do UI; pominięty (nie ma żywej ścieżki).

**Zmienione pliki** (5): [MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs) (DeleteWithConfirmationAsync + ClearActiveEditorAsync + RequestCloseTabAsync + CloseActiveTabAsync), [ConnectionNodeViewModel.cs](src/EmberTern.App/ViewModels/ConnectionNodeViewModel.cs) (Delete → async wrapper), [WorkspaceTabViewModel.cs](src/EmberTern.App/ViewModels/WorkspaceTabViewModel.cs) (Close → RequestCloseTabAsync), [NewTableTabViewModel.cs](src/EmberTern.App/ViewModels/NewTableTabViewModel.cs) (`HasContent`), [UiStrings.cs](src/EmberTern.App/UiStrings.cs) (3 nowe zestawy confirm-stringów).

**Testy** ([DeleteConfirmationAuditTests.cs](tests/EmberTern.Tests/DeleteConfirmationAuditTests.cs), +8, 754 → 762): connection delete cancel-keeps / confirm-removes / message-is-destructive-and-named / raw-delete-unconfirmed; NewTable close with-content cancel-keeps / confirm-closes / untouched-no-prompt; HasContent tracking.

**Gotcha — promote to architecture lore.**

74. **Confirm-then-execute wrapper pattern dla destrukcyjnych komend.** Gdy operacja jest wywoływana zarówno z UI (musi pytać) jak i z testów / ścieżek programmatic (nie może pytać), rozdziel na dwie metody: publiczny `XxxWithConfirmationAsync` (RequestConfirmAsync → jeśli ok → wywołaj raw) i raw `Xxx` (bez promptu). UI commands routują przez wrapper; testy i programmatic cleanup wołają raw. `ConfirmationRequested` to **event** — testy podpinają `+=` (nie `=`), a domyślny brak handlera w `RequestConfirmAsync` zwraca `Task.FromResult(true)` (auto-proceed) żeby istniejące testy nie-confirm-aware nie blokowały się. **Rule**: nigdy nie wkładaj `RequestConfirmAsync` do raw-executora współdzielonego z programmatic cleanup — inaczej post-confirm/cleanup zapyta drugi raz albo zawiesi się czekając na nieistniejący dialog.

### CHECK-constraint duplicate-row bugfix (shipped 2026-06-13)

The Ograniczenia → Check sub-tab showed each CHECK constraint twice ("Check (2)", two rows with the same name, only one carrying the source). Root cause in [FirebirdTableDetailReader.cs](src/EmberTern.Firebird/FirebirdTableDetailReader.cs) `ConstraintsSql`: a Firebird CHECK constraint is backed by **multiple triggers** (BEFORE INSERT type 1, BEFORE UPDATE type 3, …), so `RDB$CHECK_CONSTRAINTS` holds one row per trigger. The old `LEFT JOIN RDB$CHECK_CONSTRAINTS chk` multiplied each CHECK into N grid rows; the `chk_src` join's `RDB$TRIGGER_TYPE = 1` filter only matched the INSERT-trigger row, leaving the others with a NULL source. Fix: replaced the two row-multiplying JOINs with a **correlated scalar subquery** (same pattern as `FIELDS` / `REF_FIELDS`) that returns one source per constraint (`ROWS 1` on the type-1 trigger) and NULL for non-CHECK constraints without affecting their row count. CHECK source stays at column index 6 — reader unchanged. 762 / 762 still green.

### Constraint Management Sprint V1 (shipped 2026-06-13)

Add + Drop for all four constraint kinds from the Ograniczenia sub-tab. No in-place edit (Firebird has no `ALTER CONSTRAINT`; future edit = Drop + Add over these same builders). Reuses the FK wizard (Session 3) for FK Add; PK/Unique share one field-picker dialog; Check gets a name + condition dialog. Grids stay read-only — management is via context menu + dialogs.

**DDL builders** ([DdlGenerator.cs](src/EmberTern.Core/Metadata/DdlGenerator.cs)) — `BuildAddPrimaryKey(table, name, fields)`, `BuildAddUnique(...)`, `BuildAddCheck(table, name, expr)`, `BuildDropConstraint(table, name)`. PK/Unique emit `ALTER TABLE … ADD CONSTRAINT … PRIMARY KEY/UNIQUE (...)`; Check normalizes its argument (bare `ID > 0` or full `CHECK (ID > 0)` both yield a valid `CHECK (...)` clause via `NormalizeCheckClause`); Drop is type-agnostic (`ALTER TABLE … DROP CONSTRAINT …`). All validate (throw `ArgumentException`) on empty table / name / fields / expression. Identifiers quoted via `Quote` (internal quotes doubled). All SQL generation stays in `DdlGenerator` — zero SQL string-building in VMs.

**Dialogs** — `ConstraintFieldDialog` (PK + Unique, parameterized by `ConstraintFieldKind`) + `CheckConstraintDialog`, both mirroring `ForeignKeyDialog`'s VM contract: `[ObservableProperty]` form state re-notifying a live `DdlPreview`, `IsValid()` + `ValidationMessage`, `Accept`/`Cancel` commands, `RequestClose` event, `Result`, static `ShowAsync(owner, vm)`. Field multi-select reuses `SelectableFieldViewModel`. Default names: `PK_<TABLE>` / `UNQ_<TABLE>` / `CHK_<TABLE>`. Result DTOs: `ConstraintFieldSpec(Name, Fields)`, `CheckConstraintSpec(Name, Expression)`.

**VM** ([TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)) — inner sub-tab index consts (PK=0/FK=1/Check=2/Unique=3); four independent `SelectedPrimaryKey`/`SelectedForeignKey`/`SelectedCheck`/`SelectedUnique` props (one per grid — a single shared property self-clobbers since an item selected in one grid isn't in another grid's filtered `ItemsSource`, so its TwoWay binding pushes null back); `ActiveConstraint` resolves the selection of whichever inner sub-tab is active (`ConstraintsActiveSubTabIndex`). Events `AddPrimaryKeyRequested` / `AddUniqueRequested` / `AddCheckRequested` (FK reuses existing `CreateForeignKeyRequested`). Commands `AddPrimaryKey` / `AddUnique` / `AddCheck` (gated on `CanManageConstraints = executor present`), `DropConstraint` (gated on `CanDropConstraint = executor + ActiveConstraint`). Execute methods `ExecuteAddPrimaryKeyAsync` / `…UniqueAsync` / `…CheckAsync` / `ExecuteDropConstraintAsync` are public (tests drive them; the Drop confirm lives in the command, like `DropFieldAsync`). Shared `ExecuteConstraintAddAsync` helper builds-or-bails (`SafeBuild` traps the builder's `ArgumentException` → `ErrorMessage`), runs through `TryExecuteConstraintSqlAsync` (catches `DdlExecutionException` / `InvalidOperationException`), then `RefreshStructureAsync`, then `SelectNewConstraint` (jump to the matching inner sub-tab + select the new row by name — reference-equal lookup works because `Filter()` re-filters the same `ConstraintInfo` instances). All DDL runs in the user's working transaction via `FirebirdDdlExecutor` (auto-begin → Rollback undoes). FK Add now also selects the new constraint.

**View** ([TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml) + .cs) — each of the four constraint sub-grids got `x:Name`, `SelectedItem` two-way bound to its matching VM property, a `DataGrid.ContextMenu` (Add + Drop for that kind; FK Add → `CreateForeignKeyCommand`, all Drops → `DropConstraintCommand`), and `PointerPressed="OnConstraintGridPointerPressed"` (right-click selects the row first — gotcha #16 — so context-menu Drop acts on the clicked row, not stale selection). Dialog-open handlers wired in `OnDataContextChanged`.

**Strings** ([UiStrings.cs](src/EmberTern.App/UiStrings.cs)) — constraint dialog chrome, PK/Unique/Check dialog titles + headers, validation, context-menu labels, drop-confirm. Zero inline strings in XAML.

**Tests** ([ConstraintManagementTests.cs](tests/EmberTern.Tests/ConstraintManagementTests.cs), +46): DDL builder shape + quoting + validation throws (PK/Unique/Check/Drop, incl. bare-vs-full CHECK clause); dialog VM default names, IsValid gates, BuildResult trimming, DdlPreview keyword/incomplete, Accept/Cancel/Result; VM `CanManageConstraints`/`CanDropConstraint` gating, `ActiveConstraint` per-sub-tab resolution, Add/Drop no-op on null executor / null spec / empty name, disconnected-executor → `ErrorMessage` set (no throw), Drop-command confirm false/true. **808 / 808 green** (762 → 808).

**Known limitations.**
1. **No in-place edit** (per spec). Future edit is Drop + Add over the existing builders.
2. **Successful-Add UX (refresh + select-new) is verified only at the wiring level.** Tests use a disconnected `FirebirdDdlExecutor`, which exercises the error branch; the full `RefreshStructureAsync` → `SelectNewConstraint` path needs a live Firebird (smoke deferred to the user, per session brief).
3. **Drop relies on the FB engine for dependency safety** — `DROP CONSTRAINT` is issued as-is; a Firebird rejection (e.g. an FK referencing this PK) surfaces as `ErrorMessage`, never auto-cascaded.
4. **CHECK expression is embedded verbatim** — EmberTern doesn't parse/validate the condition; the server is authoritative (invalid SQL → `ErrorMessage`).

**Gotcha — promote to architecture lore.**

75. **Binding N DataGrids' `SelectedItem` to ONE shared VM property self-clobbers when the grids hold disjoint filtered lists.** The four constraint sub-grids each show a different filtered slice of `Constraints`. A single `SelectedConstraint` bound to all four would break: selecting a row in the PK grid sets it to a PK item, then the FK grid's TwoWay `SelectedItem` binding can't find that item in its own `ItemsSource`, sets its `SelectedItem` to null, and pushes null back — clearing the selection. The fix is one selected-item property per grid plus a computed `ActiveConstraint` keyed off the active sub-tab index. **Rule**: never share one `SelectedItem` target across multiple items controls whose `ItemsSource`s don't all contain the same objects.

### Table-editor polish — three sessions (shipped 2026-06-13)

Three consecutive bugfix/polish passes on the table editor (Pola / Ograniczenia / Indeksy / Dane / Opis), driven by manual smoke against the FB 5 schema. Net: **762 → 864 tests** (+102), build clean, all green.

**Session 1 — UX bugfix sprint.**
- **CheckBox `Content="{Binding Name}"` strips `_`** (FK / constraint field pickers showed `ID_NAGL` as `IDNAGL`): FluentTheme's `ContentPresenter` treats a string content's `_` as an access-key mnemonic. Fix: an explicit `<TextBlock Text="{Binding Name}"/>` child (a control content isn't access-key-processed). **(gotcha #76)**
- **UI lock after reorder + commit**: `TransactionService.TransactionStateChanged` fires off the UI thread (gotcha #11); the off-thread `RefreshAfterTransactionAsync` mutated `ObservableCollection`s → broke the DataGrid binding layer. Fix: compute the state transition synchronously in `OnTransactionStateChanged`, then marshal the side-effects (AddMessage / refresh / OnPropertyChanged) onto `Dispatcher.UIThread`.
- **New Table Move Up/Down didn't re-render**: Avalonia DataGrid doesn't reliably repaint a `NotifyCollectionChangedAction.Move`; switched to RemoveAt + Insert.
- **Pola grid blank after tab switch**: Avalonia DataGrid can render empty after its `TabItem` is detached + reattached. Fix: nudge `ItemsSource` (null → same instance) on `AttachedToVisualTree` (posted at `DispatcherPriority.Loaded`).
- **Searchable combos (#1)**: `IsTextSearchEnabled="True"` on domain/type/table/generator combos; templated combos (DomainSpec) need `TextSearch.TextBinding="{ReflectionBinding Name}"` — a compiled `<Binding>` resolves against the host DataType, not the item. **(gotcha #77)**
- **Clearable domain (#5)**: a leading "(none)" sentinel (`UiStrings.DomainNoneOption`) in inline domain combos + a Clear button in AddFieldDialog; sentinel maps to `DomainName = null` in the `SelectedDomainSpec` setters (which still ignore the load-time null writeback).
- **Unique-fields-empty robustness (#9)**: `ResolveFieldNamesAsync` prefers the VM's loaded `Fields`, falls back to a fresh `GetFieldsAsync` when empty — used by both the constraint and FK field pickers.

**Session 2 — gap-fixes + Index Management V1 + description editing.**
- **Drop Foreign Key from Pola (#1-followup)**: context-menu entry routes through the SHARED `ConfirmAndDropConstraintAsync` (extracted from `DropConstraintCommand`); the FK constraint name is resolved from the selected field via `ResolveForeignKeyConstraintForField` (matches the field against `ForeignKeyConstraints[].Fields`). No second FK-drop implementation.
- **Unique/PK backing-index config**: `DdlGenerator.BuildAddPrimaryKey`/`BuildAddUnique` gained optional `indexName` + `descending` → `USING [ASC|DESC] INDEX "ix"` (FB 2.5+ on PK/UNIQUE/FK). Dialog exposes index name + descending. Firebird's `USING` clause requires the index name, so DESC-without-name defaults the index to the constraint name; no-name + ASC omits the clause. **(gotcha #78)**
- **Index Management V1** (mirror of Constraint Management): `DdlGenerator.BuildCreateIndex` (UNIQUE / DESCENDING / `COMPUTED BY (expr)` expression index) + `BuildDropIndex`; `IndexDialogViewModel`/`IndexSpec`/`IndexDialog`; VM `SelectedIndex` + `AddIndexCommand`/`DropIndexCommand` + `ExecuteAddIndexAsync`/`ExecuteDropIndexAsync`; Indeksy grid context menu + right-click-select. **Constraint-backed-index drop guard**: `IsConstraintBackedIndex` blocks dropping PK/FK indexes (`IndexType`) and UNIQUE-constraint backing indexes (matched against `ConstraintInfo.IndexName`) — a UNIQUE backing index has `IsUnique=true` but `IndexType=""`, so it can only be recognized via the constraint's `IndexName`. **(gotcha #79)**
- **Table description editing (Opis tab)**: editable `EditableDescription` (mirrors `Description` via `OnDescriptionChanged`) + Save/Clear → `DdlGenerator.BuildCommentTable` (empty → `IS NULL`) in the working tx → `RefreshStructureAsync`.
- **Field dependency model (#3/#4) first pass**: domain→type display + Computed/PK/Autoincrement gating in AddFieldDialog + the row VMs (see Session 3 for the completed model).

**Session 3 — final polish (this milestone).**
- **New Table horizontal scroll (#1)**: ROOT CAUSE — the last column's `Width="*"`. A star-width column makes the DataGrid lay columns out to FIT the viewport (the star absorbs remaining width), which **suppresses the horizontal scrollbar**; with 13 columns the ones past the viewport get cut off and unreachable. Fix: give every column a fixed/`MinWidth` (no star) so the grid's content width exceeds the viewport and its built-in horizontal scrollbar engages. The Pola grid never had a star column, which is why it scrolled. **(gotcha #80)** — this superseded the earlier (wrong) "shrink the DDL preview" guess, which addressed a non-existent *vertical* problem.
- **Completed Computed By model (#2)**: `DdlGenerator` is now authoritative — a computed column emits ONLY `COMPUTED BY (expr)` (no DEFAULT / NOT NULL / CHECK / PRIMARY KEY / IDENTITY / backing generator; all rejected by Firebird on a COMPUTED BY column). `BuildAddField` + `BuildCreateTable` guard on `isComputed` (incl. excluding computed fields from the PK list + `HasPkColumns`). AddFieldDialog already disables the conflicting tabs/checkboxes; the New Table grid (whose `DataGridTextColumn`s can't bind per-cell `IsEnabled`) **clears** the conflicting cells in `OnComputedExpressionChanged` (Domain/Size/Scale/Default/Check/NotNull/PK/Autoincrement). **(gotcha #81)**
- **Commit path for Indeksy / Opis / Ograniczenia (#3)**: ROOT CAUSE — `ShowTransactionButtons` only included `IsQueryTabActive || IsDataTabActive || IsFieldsTabActive`, so the Commit/Rollback toolbar buttons were HIDDEN on the Indeksy / Opis / Ograniczenia sub-tabs. Add/Drop index, Save/Clear description and Add/Drop constraint all open the working transaction → the user had no way to finalize it from those sub-tabs. Fix: `ShowTransactionButtons => IsQueryTabActive || IsTableDetailTabActive` (every TableDetail sub-tab). **(gotcha #82)**

**Final field dependency model (Firebird semantics).**
- **Computed By** is mutually exclusive with Type / Domain / Size / Scale / Default / Not Null / Check / Primary Key / Autoincrement (the type is derived from the expression). Only Name + Description coexist. Dialog: disables those tabs/checkboxes. Grid: clears those cells. DDL gen: emits only `COMPUTED BY`.
- **Domain** governs the type → the Basic type / Size / Scale editors are disabled while a domain is selected; the column's resolved type is shown (AddFieldDialog "→ VARCHAR(80)"; grid Type cell shows the domain type). Default / Not Null / Autoinc still allowed (a domain column can override them).
- **Autoincrement** supplies the value → Default is cleared + disabled while engaged.
- **Primary Key** implies NOT NULL → Not Null is forced true + disabled.
- **Description** is always independent.
Implemented consistently in AddFieldDialogViewModel (full tab/checkbox gating), NewTableFieldRowViewModel (combo gating + value-clearing), and FieldRowViewModel (inline Pola: Type disabled when domain-governed).

**Gotchas — promote to architecture lore.**

76. **A string `Content` on `CheckBox`/`Button` is access-key-processed — `_` becomes a mnemonic and is swallowed.** FluentTheme's `ContentPresenter` runs `RecognizesAccessKey` on string content, so `ID_NAGL` renders as `IDNAGL`. For data that may contain underscores (Firebird identifiers!), use an explicit `<TextBlock Text="{Binding Name}"/>` child instead of `Content="{Binding Name}"` — a control content isn't access-key-processed.

77. **`TextSearch.TextBinding` for a templated ComboBox must use `{ReflectionBinding}`, not element-syntax `<Binding>`.** With `x:DataType` on the host (compiled bindings), a `<TextSearch.TextBinding><Binding Path="Name"/></TextSearch.TextBinding>` compiles against the WINDOW's DataType (which has no `Name`) → `AVLN2000`. The TextBinding is evaluated per-ITEM, so it needs a runtime-resolved binding: `TextSearch.TextBinding="{ReflectionBinding Name}"`. Pair with `IsTextSearchEnabled="True"` to get type-ahead in a non-editable ComboBox.

78. **Firebird's `USING [ASC|DESC] INDEX <name>` clause names/orders a PK/UNIQUE/FK constraint's backing index — and the index name is mandatory in the clause.** To request a DESC backing index without an explicit name, default it to the constraint name (FB's own default). No name + ASC → omit the clause (FB auto-creates an ASC index named after the constraint).

79. **A UNIQUE-constraint backing index can't be distinguished from a user unique index by `IndexInfo` alone** (`IsUnique=true`, `IndexType=""` for both — `IndexType` only carries PRIMARY KEY / FOREIGN KEY). To block dropping a constraint-backing index from the Indeksy tab, match the index `Name` against every `ConstraintInfo.IndexName` (plus the PK/FK `IndexType` check). Firebird rejects `DROP INDEX` on a constraint index anyway — the guard just gives a clearer message + points the user to the Ograniczenia tab.

80. **A star-width (`Width="*"`) DataGrid column SUPPRESSES the horizontal scrollbar.** The star column absorbs remaining width, so the DataGrid lays its columns out to fit the viewport and never reports content wider than the viewport — fixed/MinWidth columns past the edge get cut off with no way to scroll to them. For a wide grid that must scroll horizontally, give EVERY column a fixed/`MinWidth` (no star); the content width then exceeds the viewport and the built-in horizontal scrollbar engages. **Rule**: never use a star column on a DataGrid that needs horizontal scrolling.

81. **Make the DDL generator authoritative for mutually-exclusive field options instead of trusting the UI to clean up.** A computed column must emit ONLY `COMPUTED BY (expr)` — guard the CHECK / PK / DEFAULT / NOT NULL / IDENTITY / generator emission on `isComputed` in BOTH `BuildAddField` and `BuildCreateTable` (and exclude computed fields from the PK column list + the trailing-comma `HasPkColumns` count). The UI still reacts (disable in dialogs, clear cells in grids where per-cell `IsEnabled` isn't bindable), but correctness lives in the generator so no UI gap can produce invalid DDL.

82. **A toolbar button gated on a sub-tab-specific flag is invisible on the other sub-tabs — check every sub-tab can reach the actions it needs.** `ShowTransactionButtons` was `IsQueryTabActive || IsDataTabActive || IsFieldsTabActive`, hiding Commit/Rollback on Indeksy / Opis / Ograniczenia — but those sub-tabs all open the working transaction (Add/Drop index, Save description, Add/Drop constraint). Gate on the broad `IsTableDetailTabActive` so the commit path exists from every sub-tab. **Rule**: when an action on sub-tab X opens a transaction, the Commit/Rollback affordance must be visible on sub-tab X.

### Table-editor polish — session 4 (final, shipped 2026-06-13)

Two more smoke-test findings, fixed properly (no DDL-only workarounds). **864 → 868 tests.**

- **New Table Computed By — UX layer, not just DDL (#1)**: setting Computed cleared the conflicting cells once, but the user could re-enter Size/Scale/Check/etc. afterwards because `DataGridTextColumn`/`DataGridCheckBoxColumn` can't bind a per-ROW `IsEnabled`. Fix: every editable New-Table cell is now a `DataGridTemplateColumn` with an always-visible editor (TextBox/CheckBox) whose `IsEnabled` binds to a per-row gate (`IsSizeEnabled`/`IsDefaultEnabled`/`IsCheckEnabled`/`IsPkEnabled`/`IsAiEnabled`/`IsNotNullEnabled`/`IsComputedEnabled`/…). Computed→disables+clears all conflicting cells; Domain→disables type/size/scale/charset/computed; PK→disables NotNull; Autoincrement→disables Default. The Add/Edit Field dialog already blocked via disabled tabs. **(gotcha #83)**
- **Compile gives no message (#2)**: the New Table validation row (and every dialog's) stayed collapsed because `_validationMessage` lacked `[NotifyPropertyChangedFor(nameof(HasValidationMessage))]` — `IsValid()` / the compile-error catch set `ValidationMessage` directly, so `HasValidationMessage` (→ the row's `IsVisible`) never re-evaluated → "click Compile, nothing happens". Added the attribute to all five VMs (NewTable + AddField/ForeignKey/ConstraintField/Index dialogs). **(gotcha #84)**

83. **Per-ROW cell enable/disable in an Avalonia DataGrid requires a `DataGridTemplateColumn` with an always-visible editor — `DataGridTextColumn`/`DataGridCheckBoxColumn` only support per-COLUMN `IsReadOnly`.** When a field-dependency model must disable individual cells based on the row's own state (e.g. Computed By disables Size/Check/PK on that row only), make each editable column a template column with a `TextBox`/`CheckBox`/`ComboBox` in `CellTemplate` (mark the column `IsReadOnly="True"` so the grid's own edit machinery stays out of the way) and bind the editor's `IsEnabled` to a per-row VM gate. Clearing the conflicting values on the trigger is still worth doing (so no stale value lingers) but is NOT sufficient on its own — the user can re-enter a plain text/checkbox cell unless it's actually disabled.

84. **A CommunityToolkit `[ObservableProperty]` whose value is set directly (not only via another notifying property) needs `[NotifyPropertyChangedFor(...)]` for any computed property that depends on it — including its own `HasX` visibility flag.** `_validationMessage` had no `[NotifyPropertyChangedFor(nameof(HasValidationMessage))]`, so `IsValid()` setting `ValidationMessage = "..."` changed the text but never re-raised `HasValidationMessage`; the bound `IsVisible` stayed false and the message never appeared ("click and nothing happens"). It "worked" only when an adjacent field that *did* notify `HasValidationMessage` happened to change. **Rule**: when a `HasX`/`IsX`-style getter derives from an `[ObservableProperty]` field, annotate the FIELD with `[NotifyPropertyChangedFor(nameof(HasX))]` — don't rely on a sibling property to trigger the re-evaluation.

