# EmberTern — Claude Code Context

A modern desktop developer workbench for Firebird database developers, built with **.NET 9 + Avalonia 12**. Target users: ERP and backend devs who work daily with SQL, procedures, triggers, metadata, and transactions. Design philosophy: **less features, better experience; workflow quality over feature count; transaction-aware by default**.

Master prompt / V1 blueprint: `C:\Users\grzegorz.gronski\Desktop\embertern-claude-code-prompt.md`
Target UI mockup: `C:\Users\grzegorz.gronski\Desktop\UI koncepcja.png`

## Build, test, run

```powershell
# from project root
dotnet build EmberTern.slnx
dotnet test  EmberTern.slnx
# run the app
src\EmberTern.App\bin\Debug\net9.0\EmberTern.exe
```

Solution file is `.slnx` (not `.sln`) — .NET 10 default. App AssemblyName is `EmberTern`, so avares URIs use `avares://EmberTern/...`. `Directory.Build.props` sets `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true` for every project.

## Project layout

```
EmberTern.slnx
Directory.Build.props
src/
  EmberTern.Core/          # zero Avalonia dependencies — domain models only
    Connections/           # ConnectionProfile, ConnectionProfileStore (JSON), CharsetCatalog
    Query/                 # QueryResult, QueryColumn
    Metadata/              # MetadataObject record + MetadataObjectKind enum
    Workspace/             # WorkspaceState + WorkspaceTab + WindowBounds + WorkspaceStore (JSON)
  EmberTern.Firebird/      # Direct services, no interfaces yet
    FirebirdConnectionService.cs   # one active FbConnection, Connect/Disconnect/Test
    FirebirdQueryExecutor.cs       # ExecuteAsync, 5000-row cap, auto-begins tx
    TransactionService.cs          # one active FbTransaction, Begin/Commit/Rollback
    FirebirdMetadataReader.cs      # ListAsync(kind) over RDB$*, own short-lived tx
    (NuGet: FirebirdSql.Data.FirebirdClient 10.3.4)
    (InternalsVisibleTo: EmberTern.Tests — exposes SqlFor + IsSystemName)
  EmberTern.App/           # WinExe, Avalonia 12.0.3, CommunityToolkit.Mvvm 8.4.2
    Program.cs, App.axaml(.cs), UiStrings.cs, app.manifest
    ViewModels/  MainWindowViewModel, ConnectionNodeViewModel,
                 ConnectionListItemViewModel (unused since Explorer Redesign),
                 NewConnectionDialogViewModel, ConfirmDialogViewModel,
                 QueryMessageViewModel, MetadataExplorerViewModel,
                 MetadataNodeViewModel, ViewModelBase
    Views/       MainWindow, NewConnectionDialog, ConfirmDialog
    Themes/      Colors.axaml (Dark/Light dictionaries), ControlStyles.axaml
    Assets/      FirebirdSql.xshd (syntax highlighting)
                 Branding/ — logo.png (source), EmberTern_logo.png (header), EmberTern.ico (taskbar/window/exe)
    (NuGet: Avalonia.AvaloniaEdit 12.0.0, Avalonia.Controls.DataGrid 12.0.0)
tests/
  EmberTern.Tests/         # xunit — 106 tests passing; references EmberTern.App since Explorer Redesign
```

## Completed milestones (V1 COMPLETE + Explorer Redesign + V1.1 Workspace Persistence + Per-Connection Workspace shipped)

### M1 — Scaffold (shipped)
Multi-project solution, 3-zone main window (sidebar | workspace | bottom panel + status bar), dark/light theme toggle via `Application.RequestedThemeVariant`. All colors via `{DynamicResource}` — Dark + Light dictionaries in `Themes/Colors.axaml`. **`Application.Resources` quirk**: `ResourceInclude` cannot be a direct child — wrap in `ResourceDictionary.MergedDictionaries` or AVLN3000 fires.

### M2 — Connections (shipped)
`ConnectionProfile` (Name, Host, Port, DatabasePath, Username, Password, Charset, Dialect, **ClientLibraryPath**). JSON store at `%APPDATA%\EmberTern\connections.json`, **no encryption** (per spec hard rule). `FirebirdConnectionService` holds one active FbConnection at a time, fires `ActiveConnectionChanged`. Error mapping wraps everything in `ConnectionFailedException` with human-readable text — no raw "SocketException 10061" leaks.

Sidebar shows the connection list with Connect / Disconnect buttons inline. Right-click → Edit / Delete (Edit reopens the dialog pre-filled, `Upsert` preserves the Id so it updates in place). New Connection dialog has Name / Host / Port / Database (with file picker) / Username / Password (masked) / Charset combo. Advanced expander (collapsed) holds Dialect + ClientLibraryPath (with file picker for `fbclient.dll`). Test Connection button shows green / red inline result.

**Critical gotcha — Windows code pages**: `FirebirdConnectionService` registers `CodePagesEncodingProvider.Instance` in its static constructor. Without this, WIN1250/WIN1252/ISO8859_2 connections fail at `OpenAsync` with the misleading "Invalid character set specified" error. On .NET 10, the type is in the BCL — no `System.Text.Encoding.CodePages` NuGet package needed (NU1510 if added). See `memory/feedback_firebird_codepages.md`.

### M3 — SQL editor + query execution (shipped)
AvaloniaEdit integrated as the SQL editor with line numbers and Firebird syntax highlighting from `Assets/FirebirdSql.xshd` (loaded once in `App.Initialize`). Execute toolbar above the editor: **▶ Execute (F5)** primary button when idle, **■ Cancel** when running. `Window.KeyBindings`: both **F5** and **Ctrl+Enter** invoke `ExecuteQueryCommand`.

`FirebirdQueryExecutor` is a direct class. `ExecuteAsync(sql, CancellationToken)` returns `QueryResult { Columns, Rows, Elapsed, Truncated, RecordsAffected }`. Row cap **5000** — when hit, sets `Truncated = true` and breaks the read loop. Handles both result sets (SELECT) and non-result statements (DDL/DML, returns `RecordsAffected`). `OperationCanceledException` rethrown unwrapped so VM can distinguish cancel from error.

Bottom panel is a real `TabControl`: **Results** (Avalonia DataGrid with built-in row virtualization, columns built programmatically per query, `Binding "[i]"` against `object?[]` rows), **Messages** (timestamped log of `QueryMessageViewModel` — Info / Warning / Error, error rows in red), **Output** (placeholder). Auto-switches to Results on SELECT success, Messages on error or DML. Status bar middle slot: "Executing query…" → "50 rows in 125 ms" or "3 rows affected in 12 ms" or "Query cancelled."

Editor ↔ VM sync: code-behind subscribes to `TextEditor.TextChanged` and pushes `_editor.Text` into `MainWindowViewModel.QueryText`. Don't try to two-way-bind `TextEditor.Text` — it's flaky in compiled bindings. Dynamic DataGrid columns rebuilt on `CurrentResultVersionTag` change (VM bumps a Guid string after each successful execute).

### M4 — Manual transactions (shipped)
`TransactionService` is a direct class. Holds one `FbTransaction` at a time. Default `IsolationLevel.ReadCommitted`. State enum: `Idle` / `Active` / `Error` (Error only on commit failure — query failures keep the tx alive per Firebird semantics). Fires `TransactionStateChanged`. Subscribes to `ConnectionService.ActiveConnectionChanged` so connection-drop auto-clears the tx (the handle is dead anyway).

**Auto-begin on every execute**: `FirebirdQueryExecutor.ExecuteAsync` calls `BeginTransactionAsync()` first if no tx is active. Applies to **all** statements (SELECT, DML, DDL alike). Mirrors IBExpert-with-autocommit-disabled — the standard ERP dev workflow. Commit and Rollback remain **manual**, always explicit — no autocommit toggle anywhere, no "Begin Transaction" button.

Transaction bar: empty right-side when Idle; **Rollback + Commit** buttons when not Idle (Commit enabled only when Active). Dot color: grey (Idle), amber `TransactionActiveBrush` (Active), red `ErrorBrush` (Error). Status text: "No transaction" / "Active Transaction · 3 statement(s)" / "Transaction Error". Active tab title gets a `●` suffix when `HasExecutedInTransaction`. Messages log gets "Transaction started." on every Idle→Active transition.

**Disconnect with active tx**: VM raises a `ConfirmationRequested` event (`Func<ConfirmRequest, Task<bool>>`); `MainWindow` opens a reusable `ConfirmDialog` modal with "Disconnecting will roll back the active transaction. Disconnect anyway?" — on Yes, rollback then disconnect; on No, no-op. The same dialog is available for any future confirmation needs.

### M5 — Metadata sidebar (shipped)
`FirebirdMetadataReader` (direct class, no interface) in `EmberTern.Firebird` exposes `ListAsync(MetadataObjectKind, CancellationToken)` against `RDB$RELATIONS` (split tables/views on `RDB$VIEW_BLR IS NULL/NOT NULL`), `RDB$PROCEDURES`, `RDB$TRIGGERS`, `RDB$FUNCTIONS`, `RDB$GENERATORS`. System filter is `COALESCE(RDB$SYSTEM_FLAG, 0) = 0` **plus** a name-prefix safety net (`RDB$` / `MON$` / `SEC$` rejected client-side). SQL is FB 2.5/3.0/5.0-compatible. Each read opens its **own** short-lived `IsolationLevel.ReadCommitted` transaction on the same `FbConnection` — independent from the user's working transaction so metadata browsing never bumps the statement counter or touches the active-tx state. `MetadataReadException` wraps `FbException`. `SqlFor` and `IsSystemName` are `internal` with `InternalsVisibleTo("EmberTern.Tests")` so the predicate and SQL shape are unit-testable without a live FB.

`MetadataObject` + `MetadataObjectKind` enum live in `EmberTern.Core/Metadata` (zero Avalonia deps).

**Sidebar layout**: two-tab `TabControl` (Metadata + Connections). Default selected tab is Connections (1) at startup; on `ActiveConnectionChanged` with an active profile, auto-switches to Metadata (0). The Connection Manager content moved verbatim into the Connections tab; nothing was removed.

`MetadataExplorerViewModel` owns six fixed group nodes (Tables / Views / Procedures / Triggers / Functions / Generators in that order), each a `MetadataNodeViewModel`. **Lazy load**: `MetadataNodeViewModel.IsExpanded` is two-way-bound to `TreeViewItem.IsExpanded` via a `Style Selector="TreeViewItem" x:DataType="vm:MetadataNodeViewModel"` (needs `x:DataType` or compiled bindings fail to resolve `IsExpanded` against the parent VM). `partial void OnIsExpandedChanged` fires `_owner.LoadGroupAsync(this)` once per group, sets `Children`, populates `Count` → label becomes `Tables (12)`. `IsLoading` toggles a "…" subtle marker next to the group name during the fetch. Refresh button (top-right of the filter row) clears all children, marks groups unloaded, and re-fetches every group that was expanded (so the user doesn't have to collapse/re-expand).

**Placeholder-child trick for lazy expand**: Avalonia's `TreeViewItem` hides the expand chevron when `HasItems` is false. With genuinely empty `Children`, group nodes had no chevron and were not expandable. Fix: each group is seeded with a `MetadataNodeViewModel.CreatePlaceholder(...)` child (`IsPlaceholder = true`, label "Loading…", no icon). `LoadGroupAsync` clears Children (removing the placeholder) before populating real items. `ResetGroupToPlaceholder` restores it on disconnect/refresh-without-reload. Placeholders are skipped in `ApplyFilter` and excluded from context-menu / double-click via the `IsActionable` property (`!IsGroup && !IsPlaceholder`). Without this trick the tree looks populated but is dead — keep it.

**Filter**: case-insensitive substring match on leaf names. When the filter is active, groups with no matching children hide; groups with matches auto-expand. Unloaded groups stay visible during filter so the user can expand to load+filter — there is no eager full-tree load (the 500+-table rule).

**Icons** are single-letter chips inside an 18×18 `AccentMutedBrush` rounded square: T / V / P / R (tRigger, since T is taken) / F / G.

**Context menu** on leaves: `View DDL` + `Copy Name`. Both commands live on the node VM, talk to `MetadataExplorerViewModel` via `OpenDdlRequested` / `CopyNameRequested` events, which `MainWindowViewModel` handles. Group nodes hide both menu items (`IsVisible="{Binding !IsGroup}"`). **Double-click** on a leaf is wired in code-behind (`OnMetadataNodeDoubleTapped` on the `StackPanel` inside the `TreeDataTemplate`) — invokes `OpenDdlCommand` so behaviour stays in sync with the context menu.

**DDL action wiring**: `OnOpenDdlRequested` fetches DDL via `FirebirdDdlReader.FetchDdlAsync(obj)` and opens (or focuses an existing) `WorkspaceTabViewModel` keyed on `(Kind, Name)`. See M6 below.

**Copy-name to clipboard**: VM exposes `ClipboardWriteRequested` event (`Func<string, Task>`) — Core/VM hold no Avalonia types per hard rule #1. `MainWindow.axaml.cs` handles it via `Clipboard.SetTextAsync` (which lives on the `ClipboardExtensions` static class in `Avalonia.Input.Platform` — need `using Avalonia.Input.Platform;` for the extension method to be visible). Adds a confirmation Info message `"Copied "{name}" to clipboard."`.

**Connection lost while browsing**: `MetadataExplorerViewModel` subscribes to `ActiveConnectionChanged`; on disconnect clears all loaded children, marks groups unloaded, collapses them, and resets the filter — the "Connect to a database to browse its objects." hint reappears.

### M6 — DDL preview (shipped — closes V1)
`FirebirdDdlReader` (direct class, sibling of `FirebirdMetadataReader`) exposes `FetchDdlAsync(MetadataObject, CancellationToken)`. Same isolation pattern: short-lived `ReadCommitted` tx so DDL browsing never touches the user's working transaction. Returns the reconstructed DDL as a `string`. `MetadataReadException` wraps `FbException`. Static helpers `Quote`, `FormatType`, `DescribeTriggerType`, `ParseServerMajor`, `SqlForTableColumns` are `internal` and unit-tested without a live FB (35 new tests).

Per-kind reconstruction:
- **Tables** — `RDB$RELATION_FIELDS` JOIN `RDB$FIELDS` for columns (with character set + collation join). Builds `CREATE TABLE` body, then separate `ALTER TABLE ADD CONSTRAINT` statements for PK / UNIQUE / FK (joining `RDB$RELATION_CONSTRAINTS` ↔ `RDB$REF_CONSTRAINTS` ↔ `RDB$INDEX_SEGMENTS`), then `CREATE INDEX` for standalone indexes (`NOT EXISTS` against `RDB$RELATION_CONSTRAINTS` filters out the implicit ones). FK rule columns (`UPDATE_RULE` / `DELETE_RULE`) are emitted only when non-`RESTRICT`.
- **Views** — `RDB$RELATIONS.RDB$VIEW_SOURCE` (BLOB SUB_TYPE TEXT) for the source; column list from `RDB$RELATION_FIELDS` ordered by `RDB$FIELD_POSITION`.
- **Procedures** — `RDB$PROCEDURE_SOURCE` (FB 3+). Param signature reconstructed from `RDB$PROCEDURE_PARAMETERS` (paramType 0=in, 1=out) regardless of FB version. When `RDB$PROCEDURE_SOURCE` is null and server major ≤ 2 the body shows `/* Procedure source unavailable on Firebird 2.5 (only compiled BLR is stored). */`.
- **Triggers** — `RDB$TRIGGER_SOURCE` (exists in 2.5+). `DescribeTriggerType` maps the bit-encoded `RDB$TRIGGER_TYPE` (1=BEFORE INS, 2=AFTER INS, …, 17/18=INS+UPD, 25/26=INS+DEL, 27/28=UPD+DEL, 113/114=INS+UPD+DEL). DB-level / DDL triggers (codes ≥ 8192) fall back to a `/* trigger type N */` comment. Honours `RDB$TRIGGER_SEQUENCE` (`POSITION n`) and `RDB$TRIGGER_INACTIVE`.
- **Functions** — `RDB$FUNCTION_SOURCE` (FB 3+). On FB ≤ 2 emits a single-line comment that the object is a UDF declaration with no catalog source.
- **Generators** — `CREATE SEQUENCE {name};` plus a `/* current value: N */` comment from `GEN_ID(name, 0) FROM RDB$DATABASE` (best-effort; any `FbException` degrades silently to the bare `CREATE`).

**FB version detection** — `FirebirdDdlReader.ParseServerMajor(connection.ServerVersion)` extracts the major from the `WI-V3.0.7.33374 Firebird 3.0` / `WI-V5.0.0.1306 Firebird 5.0` pattern (regex `V(\d+)\.`, fallback `Firebird (\d+)\.`). Used to gate procedure / function source fetching ahead of time — avoids the "try the SQL, parse FbException" anti-pattern called out in the M6 plan.

**Identifier quoting** — `Quote(name)` only quotes when needed: lowercase letters, leading digit, special chars, or empty. SHOUTY_SNAKE_CASE Firebird names stay unquoted, matching `isql -x` output. Internal quotes are doubled.

**Multi-tab workspace** — `WorkspaceTabViewModel` (in `App/ViewModels`) has two kinds: `Query` (one fixed instance, not closable, owns the existing SQL editor) and `Ddl` (one per opened object, closable). `MainWindowViewModel.WorkspaceTabs` is an `ObservableCollection<WorkspaceTabViewModel>` with `_queryTab` always at index 0. `SelectedWorkspaceTab` drives `IsQueryTabActive` / `IsDdlTabActive`, which gate the execute toolbar, transaction bar, and which editor is visible. The transaction's `●` marker moved from the global `ActiveTabTitleDisplay` to per-tab `ShowActiveTransactionMarker` (only the query tab toggles it).

**Tab strip rendering** — replaced the single-slot `StackPanel` with an `ItemsControl` over `WorkspaceTabs`. Each item: a flat button (`ActivateCommand`) carrying `DisplayTitle`, plus a close `×` button (`CloseCommand`) visible only when `IsClosable`. Active tab styled via `Border.active-tab` class bound to `IsSelected`. Tabs scroll horizontally when they overflow.

**Editor swap** — two `AvaloniaEdit.TextEditor` instances in the same `Grid` cell: `SqlEditor` (editable, visible when query tab) and `DdlEditor` (`IsReadOnly=True`, visible when DDL tab). DDL text is pushed into `DdlEditor.Text` from code-behind on `MainWindowViewModel.SelectedWorkspaceTab` / `ActiveDdlText` change — two-way binding `TextEditor.Text` is flaky (same gotcha as the SQL editor). Both editors share the registered Firebird syntax highlighting.

**Open / focus semantics** — `OnOpenDdlRequested` first scans `WorkspaceTabs` for a DDL tab with matching `(Kind, Name)`; if found it activates that tab, otherwise it awaits `FetchDdlAsync`, appends a new tab, and activates it. Errors (`MetadataReadException`, `InvalidOperationException`) post to the Messages tab and switch the bottom panel to Messages, matching how query errors are surfaced.

**Tab close semantics** — closing the active DDL tab falls back to the previous tab (or the query tab if it was the only DDL tab). The query tab is never closable.

**Grid row sizing** — workspace rows are now `36, Auto, *, Auto` (previously `36, 36, *, 40`) so the execute toolbar and transaction bar collapse to zero height when a DDL tab is active. No empty 40px strip below DDL content.

### M6 post-ship fixes (smoke against real FB 5)
Surfaced when the user opened DDL against the production FK ERP schema (2356 tables, Polish data):

1. **`RDB$COMPUTED_SOURCE` is on `RDB$FIELDS`, not `RDB$RELATION_FIELDS`.** Initial `SqlForTableColumns` had `rf.RDB$COMPUTED_SOURCE` — the alias `rf` is `RDB$RELATION_FIELDS`, which doesn't expose that column on **any** FB version. Fix: `f.RDB$COMPUTED_SOURCE` (the domain table, which is where Firebird actually stores computed-column sources via an anonymous domain). The user initially diagnosed this as an FB-2.5-only column requiring `ParseServerMajor` gating — pushed back, no version gate needed, the alias was simply wrong. Regression-pinned by a dedicated test.

2. **Source BLOBs are stored in a mix of UTF-8 *and* the connection charset, in the same database.** `RDB$PROCEDURE_SOURCE` / `RDB$TRIGGER_SOURCE` / `RDB$VIEW_SOURCE` / `RDB$FUNCTION_SOURCE` are `BLOB SUB_TYPE TEXT CHARACTER SET NONE` — the bytes are whatever the *writing* tool emitted at the time. In the user's DB, FB-3-era PSQL was UTF-8, older IBExpert writes were WIN1250. The driver's `GetString` decodes via the *connection* charset (WIN1250 for this user), which mangles both kinds in opposite directions (`kaĹĽda` for UTF-8 bytes, `KA▯DEJ` for WIN1250 bytes after a naive UTF-8-only fix). Final fix: read raw bytes via `DbDataReader.GetBytes`, then `DecodeBytes(buf, len, fallback)` does strict UTF-8 first (`UTF8Encoding(throwOnInvalidBytes: true)`) and falls back to the connection's encoding on `DecoderFallbackException`. UTF-8 is a strong discriminator — single-byte WIN1250 text containing Polish chars has invalid UTF-8 continuation bytes, so the fallback triggers reliably. The fallback encoding is sourced from `CharsetCatalog.Resolve(connectionService.ActiveProfile.Charset)`.

3. **`CharsetCatalog.Resolve(fbCharset)`** (new, in `EmberTern.Core/Connections/CharsetCatalog.cs`) maps Firebird charset names (`WIN1250`, `WIN1252`, `ISO8859_2`, `UNICODE_FSS`, `NONE`, …) to .NET `Encoding`. Self-registers `CodePagesEncodingProvider` in its static ctor so callers don't have to remember (covers the case where `Resolve` runs before any `FbConnection` is opened — e.g. early in a test process).

4. **Metadata-tree full-row hit target.** Previously `DoubleTapped` lived on the horizontal `StackPanel` inside the `TreeDataTemplate`, which sized to content — only the icon+label rectangle was clickable, even though the row highlight visually spanned the full sidebar width. Fix: wrap the row content in a `Border` with `HorizontalAlignment="Stretch"` + `Background="Transparent"` (transparent background is needed for the Border to receive hit events). Moved `DoubleTapped` and the `ContextMenu` from the StackPanel to that Border. Avalonia's FluentTheme `TreeViewItem` already stretches its header presenter, so the Border now spans the full row width. Both double-click and right-click work anywhere on the row.

### Post-V1 polish — Branding + Visual (shipped)

**Branding assets** in `src/EmberTern.App/Assets/Branding/`:
- `logo.png` — source artwork (RGBA, 1536×1024).
- `EmberTern_logo.png` — 256×256 square; rendered at 40×40 in the top-bar via `<Image Source="avares://EmberTern/Assets/Branding/EmberTern_logo.png" ... />` (replaces the prior 22×22 placeholder `Border`).
- `EmberTern.ico` — multi-size 16/24/32/48/64/256 (all 32-bit RGBA). Drives both the embedded .exe icon (`<ApplicationIcon>Assets\Branding\EmberTern.ico</ApplicationIcon>` in `EmberTern.App.csproj` — feeds Explorer / pre-launch taskbar) and the running-window/taskbar icon (`Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://EmberTern/Assets/Branding/EmberTern.ico")))` in the `MainWindow` ctor; needs `using Avalonia.Platform;`).

`EmberTern_logo.png` and `EmberTern.ico` are regenerated from `logo.png` by an in-session PowerShell + System.Drawing pipeline (no Python on this machine):
1. Find alpha bounding box via inline-compiled C# `AlphaBounds.Find(Bitmap)` (`LockBits` + byte-array scan — a PowerShell pixel loop is too slow on 1.5M pixels).
2. Content rule: `alpha ≥ 32 AND NOT (R≥240 ∧ G≥240 ∧ B≥240)`. The `alpha ≥ 32` cutoff is critical — the source has ~2.8k pixels with alpha 1–31 (faint glow/halo) that bloat the bbox to nearly the full canvas. The near-white check is defence-in-depth for source PNGs that aren't actually transparent (white backdrop under `alpha=255`).
3. Pad bbox by 5% of its larger dimension, center on a transparent square canvas.
4. `HighQualityBicubic` resize to each target size, with `HighQuality` smoothing / pixel-offset / compositing.
5. ICO assembled by hand: 6-byte `ICONDIR` header + N×16-byte `ICONDIRENTRY` + concatenated PNG payloads. Width/height bytes = 0 for the 256px entry (per the .ico spec).

**csproj gotcha** — `<AvaloniaResource Include="Assets\**" />` already covers the new `Branding` subfolder. **Do not add a second more-specific include** (`Assets/Branding/**`) — with `TreatWarningsAsErrors=true` the duplicate item fails the build (NETSDK1022).

**Theme palette refresh** — `Themes/Colors.axaml`:

Moved from a violet-tinted palette to VS Code-style neutrals. Dark dropped the blue-purple cast (`#1A1B26` / `#22232F` → `#1E1E1E` / `#252526`) and brightened the accent (`#8B7AB8` violet → `#C084FC`, with `#7C3AED` muted). Light gave up pure-white panels (`#FFFFFF` → `#E8E8E8`) and switched to a deeper purple accent (`#6F3DC7`) for stronger contrast on light. Selection in dark is now VS Code's `#094771` blue (was muted purple `#3D3F58`).

**Focus border added** — new `FocusBorderColor` (`#007FD4` dark / `#0078D4` light) + `FocusBorderBrush` in both theme dictionaries. Drives the keyboard accessibility outline on flat / icon buttons.

**Control hover / focus / selection states** — `Themes/ControlStyles.axaml`:
- `Button.primary:pointerover` → `AccentMutedBrush`; `Button.primary:pressed` → `AccentMutedBrush` with `Opacity=0.85`. Execute button now has real feedback.
- `Button.flat:focus` / `Button.icon:focus` → `FocusBorderBrush` outline. Keyboard navigation now shows where focus is.
- `TreeViewItem`: tighter `Padding="2,1"`. **Selection/hover colors now flow through themed resource keys** (`TreeViewItemBackgroundSelected`, `TreeViewItemBackgroundPointerOver`, etc.) defined in `Colors.axaml` — see "Explorer Redesign" below for the resource-key approach and why the older `Border#PART_LayoutRoot` selector approach was abandoned.

Tests still 93 / 93 green.

### Explorer Redesign — IBExpert-style connection tree (shipped)

Replaced the two-tab sidebar (Metadata + Connections) with a single tree where each saved connection is a root node and the six metadata categories (Tables/Views/Procedures/Triggers/Functions/Generators) are its children. Multiple connections coexist in the tree; `FirebirdConnectionService` still allows only one active at a time (V2 lifts that), but each `ConnectionNodeViewModel` independently tracks its own `IsConnected` from `ActiveConnectionChanged` events. UX matches IBExpert: connect auto-expands the node, disconnects collapse + drop children + hide the chevron entirely (`rozwinięte = połączone`).

**New / changed VMs.**
- `ConnectionNodeViewModel` (new) — owns one profile. Exposes `Profile`, `IsConnected`, `IsExpanded`, `DisplayName` (`"Name (Host:Port)"`), `StatusIndicator` (`●` connected / `○` not), `Children` (the 6 category nodes). Commands: `Connect`, `Disconnect`, `Reconnect`, `Edit`, `Copy`, `Delete` — all delegate to the `MainWindowViewModel` owner. Subscribes to `FirebirdConnectionService.ActiveConnectionChanged`; auto-expands on connect (via `OnIsConnectedChanged` → `IsExpanded = true`); on disconnect clears Children entirely so the FluentTheme TreeViewItem hides its chevron (Avalonia hides expanders when `HasItems = false`). `Detach()` unsubscribes from the service event — called by `MainWindowViewModel.ReloadConnections` before clearing the collection, otherwise the service retains dead VMs in its event invocation list.
- `MetadataExplorerViewModel` — rewritten. Holds `ObservableCollection<ConnectionNodeViewModel> Connections` + `SelectedConnection`. Selected-action commands (`EditSelected`, `CopySelected`, `DeleteSelected`, `ConnectSelected`, `DisconnectSelected`, `ReconnectSelected`) forward to the selected node and have state-aware `CanExecute`. `OnSelectedConnectionChanged(old, new)` resubscribes a `PropertyChanged` listener so flipping `IsConnected` on the selected node invalidates `Connect/Disconnect/Reconnect-Selected` `CanExecute`. `LoadGroupAsync` still lives here (called by `MetadataNodeViewModel.OnIsExpandedChanged`). `RefreshAsync` rewritten to iterate connected nodes' categories. Removed: `Groups`, `IsConnected`, `ShowNotConnectedHint`, `ShowTree`, `OnConnectionChanged`, `UpdateConnectedState`, `BuildGroups` plus the matching UiString getters.
- `MainWindowViewModel` — dropped `Connections` (`ObservableCollection<ConnectionListItemViewModel>`), `SelectedSidebarTabIndex`, `HasConnections`, `ShowEmptyHint`, and 8 orphaned UiString getters. `ReloadConnections()` now `Detach`-clears `Metadata.Connections` and rebuilds with one `ConnectionNodeViewModel` per profile. Added `Copy(profile)` — clones with new `Guid` and `" (Copy)"` suffix, saves via store, reloads. **Critical thread-safety fix**: `FirebirdConnectionService.ActiveConnectionChanged` fires on the async-continuation thread, not the UI thread. The handler in `ConnectionNodeViewModel` (`OnActiveConnectionChanged`) wraps the work in `Dispatcher.UIThread.Post(UpdateConnectedState)` — touching `IsConnected` from a non-UI thread blows up compiled bindings. `ConnectAsync` itself stays free of dispatcher noise; only the event sink needs marshalling.
- `MetadataNodeViewModel` — `OnIsExpandedChanged` now flashes `IsLoading = true` on a cached re-expand and posts the clear at `DispatcherPriority.Background`, so the "…" indicator stays visible until layout/render of all child rows completes. Eager-loaded categories still hit this path: instant visual feedback even when data's cached.

**XAML rewrite.** Whole sidebar Border replaced — `DockPanel` with toolbar (`+ ✎ ⧉ ✕ │ ⏵ ⏹ ↺ ↻`) + filter `TextBox` + `TreeView`. Code-behind `OnSidebarTreeSelectionChanged` only writes to `Metadata.SelectedConnection` when the selected item is a `ConnectionNodeViewModel` — picking a category/leaf leaves the previous selection, which is what the toolbar commands want to act on. Double-click on a connection node invokes `ConnectCommand` when disconnected (`OnConnectionNodeDoubleTapped`). The Metadata-node double-click (open DDL) is unchanged.

**Eager load + virtualization.** After `LoadCategoriesAsync` builds the 6 category nodes, it sequentially awaits `Metadata.LoadGroupAsync` on each so counts (`Tables (2356)`, `Views (215)`, …) show immediately after connect — matches IBExpert workflow. **Sequential is mandatory**: `FbConnection` services one command at a time, `Task.WhenAll` throws. `VirtualizingStackPanel` is now the `ItemsPanel` on both `TreeView` and nested `TreeViewItem.ItemsPanel` style — expanding `Tables` against the user's 2356-row schema went from a ~1s freeze to instant.

**Theme overrides.** Selection/hover backgrounds piped through FluentTheme themed resource keys (`TreeViewItemBackgroundSelected`, `TreeViewItemBackgroundPointerOver`, `TreeViewItemBackgroundSelectedPointerOver`, etc.) defined in both `Dark` and `Light` dictionaries of `Colors.axaml`. FluentTheme's `TreeViewItem.xaml` template does `Background="{DynamicResource TreeViewItemBackgroundSelected}"` etc. — overriding the resource key wins, no Style.Setter required. Border-brush keys forced to `Transparent` to suppress the colored selection ring.

**Testing.** `EmberTern.Tests` now references `EmberTern.App` (first time the test project pulls App in). New test files: `ConnectionNodeViewModelTests` (6 tests) and `MetadataExplorerViewModelTests` (7 tests). `MainWindowViewModel` is constructed against an isolated temp `ConnectionProfileStore` so no `%APPDATA%` I/O leaks. **106 / 106 green** (93 pre-redesign + 13 new). Smoke-verified end-to-end against the user's production FB 5 schema (2356 tables, Polish data) — connect, eager-load counts, expand categories, view DDL, double-click connect, reconnect, all green.

**Gotchas — promote to architecture lore.**

1. **Avalonia color hex is ARGB, not RGBA.** `#FFFFFF12` parses as `A=FF, R=FF, G=FF, B=12` — an opaque pale-yellow brush. We wrote that for `HoverOverlayColor` meaning "7% white overlay", and it was always opaque yellow. Correct form is `#12FFFFFF` (alpha-first). Spent three iterations chasing this through Style selectors before realizing the hex itself was wrong. **Rule**: any `#XXXXXXXX` literal in `Colors.axaml` is ARGB; if you mean N% opacity, write `N×255/100` in the leading byte. Three-pair `#RRGGBB` is implicitly `A=FF`.
2. **FluentTheme `TreeViewItem` state colors go through DynamicResource keys, not hard-coded brushes.** Trying to override via `<Style Selector="TreeViewItem:selected /template/ Border#PART_LayoutRoot">` is fragile (depends on part name) and incomplete (FluentTheme uses several keys across states). The canonical override is to define the `TreeViewItem*` resource keys in our `ThemeDictionaries` — FluentTheme picks them up via the resource lookup chain. Same pattern applies to most FluentTheme controls. Source for the key list: `https://raw.githubusercontent.com/AvaloniaUI/Avalonia/release/12.0.3/src/Avalonia.Themes.Fluent/Controls/TreeViewItem.xaml`.
3. **Interface-typed compiled bindings in `Style.Setter` don't push back TwoWay in Avalonia 12.0.3.** Tried `Style Selector="TreeViewItem" x:DataType="vm:IExpandableNode"` with `Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}"` — source→target worked, target→source silently failed. **⚠️ CORRECTED — see gotcha #38.** The "fix" recorded here (one concrete-typed Style per VM type) was WRONG and was the root cause of the later auto-expand-on-connect failure: `x:DataType` does not scope a Style selector at runtime, so all the per-type Styles match every `TreeViewItem` and the last one clobbers the rest via a failed `DataContext` cast. The actual fix is a SINGLE `<Style Selector="TreeViewItem">` with `ReflectionBinding` setters (resolve by name, no cast). Do not reach for "one Style per type."
4. **`TreeView.ItemTemplate` + `TreeView.DataTemplates` mix breaks nested template resolution.** Putting the root template (`ConnectionNodeViewModel`) in `ItemTemplate` and the child template (`MetadataNodeViewModel`) in `DataTemplates` caused nested category `ItemsSource="{Binding Children}"` to silently not kick in — categories rendered, but had no chevrons. Fix: put **all** `TreeDataTemplates` in `TreeView.DataTemplates`, leave `ItemTemplate` unset. Avalonia then resolves the template by `DataType` at every nesting level. **Rule**: TreeView with mixed node types → never set `ItemTemplate`, always use `DataTemplates`.
5. **`FirebirdConnectionService.ActiveConnectionChanged` fires on the async-continuation thread.** Any handler that touches UI-bound state must marshal with `Dispatcher.UIThread.Post`. Only `ConnectionNodeViewModel.OnActiveConnectionChanged` needs this — the service's `ConnectAsync` / `DisconnectAsync` are normal awaitables and don't need wrapping.
6. **`TreeViewItem` placeholder seed pattern (from M5) still applies but the trigger is different now.** ConnectionNodeViewModel constructor leaves `Children` empty. `UpdateConnectedState` seeds the placeholder only on transition-to-connected and clears children entirely on transition-to-disconnected. Disconnected = no chevron, can't expand. This relies on Avalonia hiding the expander when `HasItems = false`.

**V2 candidates (surfaced by this redesign, not committed).**
- **`FirebirdConnectionService` multi-connection.** UI is already shaped for it — every `ConnectionNodeViewModel` has its own `IsConnected` driven by `ActiveConnectionChanged`. The service still holds a single `FbConnection`. V2 should hold a `Dictionary<Guid, FbConnection>` keyed by profile Id and change the event signature to carry which profile changed.
- **Reparent `MetadataNodeViewModel._owner` from `MetadataExplorerViewModel` to `ConnectionNodeViewModel`.** Today works because there's only one active connection, so `LoadGroupAsync` correctly fetches against the active one regardless of which subtree the group belongs to. Multi-connection breaks that assumption — each group has to load against its own connection's `FbConnection` instance.
- **`ConnectionListItemViewModel` removal.** Unused since step 4; left in the project per the milestone instruction ("może zostać"). Safe to delete once tests no longer reference any of its API surface (they don't currently).

### Custom Titlebar (shipped)

Replaced the OS chrome + 48 px app header (logo, title, theme toggle on its own row) with a single 36 px integrated titlebar à la VS Code / JetBrains. Recovered ~48 px of vertical space and removed the duplicate logo/title that previously appeared in both the OS title bar and the app header.

**Window properties** (`MainWindow.axaml`):
- `ExtendClientAreaToDecorationsHint="True"` — extend the client area over the OS chrome.
- `ExtendClientAreaTitleBarHeightHint="-1"` — let our content drive the titlebar height.
- `WindowDecorations="BorderOnly"` — keep the resize border, drop OS-drawn chrome.

**Single 36 px bar** (`Grid.Row="0"`, `Background="ElevatedPanelBrush"`) — from left to right:
1. Logo (28×28) + "EmberTern" SemiBold 13 + 1 px separator + `ActiveConnectionName` (12 px subtle, hidden when no active connection).
2. Connection toolbar moved out of the sidebar: `+ ✎ ⧉ ✕ │ ▶ ⏹ ↺ ↻` (bound to `Metadata.*SelectedCommand` etc.).
3. `*` drag spacer.
4. Theme toggle `◐`.
5. Min `—` / Max-restore `◻`↔`❐` / Close `✕` using new `Button.caption` (46×36, flat) and `Button.caption.close:pointerover` (red `#E81123`, white glyph).

Sidebar's own toolbar row is gone; sidebar now is just filter + tree.

**Drag** — `PointerPressed="OnTitleBarPointerPressed"` on the titlebar Border calls `BeginMoveDrag(e)` when left button is down. Buttons consume their own clicks first, so they aren't accidentally turned into drag handles.

**Window controls** — three code-behind handlers: `OnMinimizeClick` → `WindowState.Minimized`, `OnMaxRestoreClick` toggles `Maximized` / `Normal`, `OnCloseClick` → `Close()`. A window-level `PropertyChanged` listener swaps the max-restore glyph between `◻` and `❐` on `WindowStateProperty` change.

**VM additions** (`MainWindowViewModel`):
- `ActiveConnectionName` → `_service.ActiveProfile?.Name ?? string.Empty`.
- `HasActiveConnection` → `_service.ActiveProfile is not null` (drives separator + name visibility).
- Both notified from `OnActiveConnectionChanged` alongside `IsConnected`.

**Gotcha — promote to architecture lore.**

7. **Avalonia 12 renamed `SystemDecorations` → `WindowDecorations` and removed `ExtendClientAreaChromeHints`.** First-pass code copied the Avalonia 11 idiom (`SystemDecorations="BorderOnly"` + `ExtendClientAreaChromeHints="NoChrome"`) and hit `AVLN2000: Unable to resolve suitable regular or attached property ExtendClientAreaChromeHints` plus `AVLN5001: 'Window.SystemDecorations' is obsolete`. In Avalonia 12 the chrome-hints concept is folded into `WindowDecorations` (enum: `None` / `BorderOnly` / `Full`) — just set that. **Rule**: when porting client-area-extension snippets, check `Avalonia.Controls.xml` for the current property surface; the v11 trio (`ExtendClientAreaToDecorationsHint` + `ExtendClientAreaChromeHints` + `SystemDecorations`) collapsed to a duo (`ExtendClientAreaToDecorationsHint` + `WindowDecorations`).

### Metadata tree — extra categories + dense icons (shipped)

**Step 1 — seven new metadata kinds.** `MetadataObjectKind` grew from 6 → 13: added `Domain`, `Package`, `Exception`, `Role`, `User`, `Index`, `SystemTable`. Each has a `SqlFor` query against the matching `RDB$`/`SEC$` catalog table (or its bypass for `SystemTable` — see below), a label in `UiStrings` + `MetadataNodeViewModel.LabelFor`, an entry in `ConnectionNodeViewModel.CategoryOrder` (appended in the listed order, after the original six). `FirebirdDdlReader` handles all new kinds: `Exception` builds real `CREATE OR ALTER EXCEPTION … 'message';` from `RDB$EXCEPTIONS.RDB$MESSAGE`; `Role` emits `CREATE ROLE …;`; `SystemTable` reuses `BuildTableDdlAsync` (the column SQL is identical against `RDB$RELATION_FIELDS`); `Domain` / `Package` / `User` / `Index` ship a placeholder comment (`/* DDL for X "name" is not reconstructed in this build. */`) — placeholder over throw was the explicit spec.

**Per-kind data sources.** `Domain` → `RDB$FIELDS` (the `RDB$` client-side filter strips anonymous backing-domains, leaving only user domains); `Package` → `RDB$PACKAGES` (FB 3+, surfaces empty / error on FB 2.5 — acceptable); `Exception` → `RDB$EXCEPTIONS`; `Role` → `RDB$ROLES`; `User` → `SEC$USERS` (FB 3+, needs admin or own-row privileges; failure surfaces as `MetadataReadException`); `Index` → `RDB$INDICES`; `SystemTable` → `RDB$RELATIONS WHERE RDB$SYSTEM_FLAG = 1 AND RDB$VIEW_BLR IS NULL` (the inverse of `Table`).

**SystemTable filter bypass.** Added `internal static bool BypassSystemNameFilter(MetadataObjectKind kind)` to `FirebirdMetadataReader`. Only `SystemTable` returns true. `ListAsync` consults it before applying `IsSystemName`, so the `RDB$`/`MON$`/`SEC$` prefixed rows that the SQL deliberately returns aren't filtered back out client-side. All other kinds still get the prefix safety net.

**Step 2 — denser tree + unicode icons.** Replaced single-letter chip icons (`T V P R F G`) with kind-specific unicode glyphs: `▦` Table, `◫` View, `⚙` Procedure, `⚡` Trigger, `ƒ` Function, `№` Generator, `◇` Domain, `⊞` Package, `⚠` Exception, `♜` Role, `☻` User, `⌘` Index, `⛁` SystemTable. Dropped the chip background entirely — glyphs render in `AccentBrush` directly (DataGrip / IBExpert style), with a fixed `Width="14" TextAlignment="Center"` so columns of leaves line up regardless of glyph metrics. Leaf label `FontSize` 12 → 11, group label same. Connection-node label `FontSize` 12 → 11 (still SemiBold), status dot 14 → 12. `StackPanel.Spacing` 6 → 5 both rows. `TreeViewItem` style: padding `2,1` → `2,0` and added `MinHeight="0"` so FluentTheme's implicit min doesn't keep rows tall — visible row pitch dropped ~30%, matches IBExpert density on the FK ERP schema (2356 tables in one scroll-friendly tree).

**Tests.** Build clean (zero warnings, TWAE on). 124 / 124 green (106 → 124, +18 new): `SqlFor_FiltersSystemFlag` extended to all kinds-with-`RDB$SYSTEM_FLAG`; new `SqlFor_SystemTable_InvertsSystemFlagFilter`, `SqlFor_User_QueriesSecUsers`, `SqlFor_NewKinds_UseCorrectSystemTable`, `BypassSystemNameFilter_OnlySystemTableBypasses`; `BuildRoleDdl_EmitsCreateRoleStatement`, `BuildPlaceholderDdl_EmitsCommentBlock`. Existing `LoadCategoriesAsync_PopulatesSixCategoryGroups` renamed and now asserts the full 13-kind ordering.

**Gotcha note.** When changing `TreeViewItem` density via global style, both `Padding` AND `MinHeight="0"` are needed — FluentTheme's default `MinHeight` enforces ~26px row regardless of content padding. Without `MinHeight=0` the padding change is invisible.

### Per-kind icon colors + titlebar polish + light sidebar contrast (shipped)

**Per-kind icon colors (theme-aware).** Replaced the single-accent glyph color with one brush per `MetadataObjectKind` — 13 `IconColor_*` `SolidColorBrush` keys defined in **both** Dark and Light theme dictionaries in `Themes/Colors.axaml`. Dark uses Material 300/200 tones (bright on dark — e.g. `IconColor_Table = #4FC3F7`); Light uses Material 700/800 (darker + more saturated for legibility on near-white — e.g. `IconColor_Table = #0277BD`). `MetadataNodeViewModel` exposes `IconResourceKey` (a string like `"IconColor_Table"`) instead of a hex literal, keeping the "VM holds no Avalonia types" rule.

**Live theme switching for keyed brushes — `IconBrushConverter`.** Avalonia's `DynamicResource` markup extension takes a *literal* key, so we can't write `Foreground="{DynamicResource {Binding IconResourceKey}}"`. Solution: a tiny `IMultiValueConverter` (`src/EmberTern.App/IconBrushConverter.cs`) wired through a `MultiBinding` with two sources — the node's `IconResourceKey` and `RootWindow.ActualThemeVariant`. The converter calls `Application.Current.Resources.TryGetResource(key, theme)` and returns the resolved `IBrush`. Theme toggle changes `ActualThemeVariant` → the MultiBinding re-fires → converter re-resolves against the new theme dictionary → icons re-color live without a tree rebuild. **Rule for keyed-brush bindings in Avalonia 12: if the resource key is dynamic, bind it through a converter + `ActualThemeVariant` MultiBinding — `DynamicResource` won't help.**

**Custom titlebar — double-click toggles Maximize/Restore.** Added `DoubleTapped="OnTitleBarDoubleTapped"` to the titlebar `Border`. Handler in `MainWindow.axaml.cs` toggles `WindowState` between `Maximized` and `Normal`. To prevent double-clicking the toolbar `+ ✎ ⧉ ✕ ▶ ⏹ ↺ ↻` icons or window-control buttons from accidentally maximizing, the handler walks the original source's ancestor chain (`Visual.FindAncestorOfType<Button>(includeSelf: true)`, from `Avalonia.VisualTree`) and bails when any ancestor is a `Button` — cleaner than peppering `e.Handled = true` across every button's click handler.

**Light theme sidebar contrast.** `PanelColor` `#E8E8E8 → #E0E0E0` (sidebar), `ElevatedPanelColor` `#E5E5E5 → #D6D6D6` (titlebar), `BorderColor` `#C8C8C8 → #BDBDBD`. Establishes a clear three-step hierarchy in light mode: editor/results stay near-white (`BackgroundColor #F3F3F3`), sidebar one step darker, titlebar darker still. Border now sits between panel and elevated-panel values so the seams read clearly without a sharp line.

**Dark accent → blue (Execute button).** `AccentColor` `#C084FC → #2D6BBF`, `AccentMutedColor` `#7C3AED → #1A4F8F` in the Dark dictionary only. Light keeps the original purple (`#6F3DC7` / `#9B6DDB`). Side effect: every `AccentBrush` consumer turns blue in dark — focus rings, transaction marker, etc. — consistent with the primary-action color.

### V1.1 — Workspace Persistence (shipped)

Goal: after restart, the user lands where they left off — same window geometry, same open tabs (Query + DDL), same SQL text, same active tab. Connection selection in the tree is restored; auto-reconnect is explicitly out of scope (user logs back in manually).

**File**: `%AppData%\EmberTern\workspace.json`. Same dir as `connections.json`. JSON pretty-printed, enums serialized as strings (`JsonStringEnumConverter`) so future kinds/states don't silently corrupt old files.

**Persisted**:
- Window bounds (X/Y/Width/Height) + WindowState ("Normal" / "Maximized" / "Minimized" / "FullScreen") — stored as string so Core stays free of any Avalonia enum dependency.
- All workspace tabs in order. Query tab → `SqlText`. DDL tabs → `ObjectName`, `ObjectKind`, `ConnectionProfileId`, plus **cached `DdlText`** (so re-opened DDL tabs render immediately, no reconnect required — explicit choice per user, accepts the ~few-KB-per-tab size cost).
- `ActiveTabIndex` into the workspace tabs list.
- `LastActiveConnectionId` — used only to pre-select the matching node in the sidebar, never to auto-connect.

**Not persisted**: query results, transaction state, expanded tree nodes, scroll position, sidebar filter text.

**New Core types** (`src/EmberTern.Core/Workspace/`):
- `WorkspaceState.cs` — `WorkspaceState`, `WorkspaceTab` (with its own `WorkspaceTabKind { Query, Ddl }`), `WindowBounds`. Plain mutable classes with default-able properties so `System.Text.Json` round-trips them without a custom converter.
- `WorkspaceStore.cs` — mirror of `ConnectionProfileStore`. Same default dir resolution (`%AppData%\EmberTern`). `Load()` returns `null` on missing file, empty file, corrupt JSON (catches `JsonException`, `IOException`, `UnauthorizedAccessException`) — never throws on startup. `Save()` writes the file (intentionally not exception-shielded; failures bubble for the view to handle).

**Two parallel `WorkspaceTabKind` enums.** `EmberTern.App.ViewModels.WorkspaceTabKind` (VM) and `EmberTern.Core.Workspace.WorkspaceTabKind` (persistence DTO). Identical values, kept separate so the persistence schema can evolve independently of the VM. `MainWindowViewModel` explicit-qualifies `Core.Workspace.WorkspaceTabKind` at the boundaries (Capture/Restore); tests use `using` aliases (`CoreTabKind`, `VmTabKind`) to disambiguate. If you ever feel tempted to collapse them, remember: persistence schemas need their own versioning lifecycle.

**VM changes** (`MainWindowViewModel`):
- `CaptureWorkspace()` → snapshots `WorkspaceTabs` (Query → SqlText from `QueryText`; DDL → name/kind/profile-id/ddl-text), `SelectedWorkspaceTab` index, and `_service.ActiveProfile?.Id`. Returns a `WorkspaceState` with `WindowBounds == null` — the view fills that in.
- `RestoreWorkspace(WorkspaceState)` → removes all closable (DDL) tabs, walks `state.Tabs`: Query → set `QueryText`, DDL → reconstruct `MetadataObject` and call `WorkspaceTabViewModel.CreateDdl(..., ddlText, profileId)`. Then clamps `ActiveTabIndex` into range and `SelectTab`s it. Finally walks `Metadata.Connections` and sets `SelectedConnection` to the node matching `LastActiveConnectionId` (no Connect call).
- `WorkspaceTabViewModel.CreateDdl` gained a `string? connectionProfileId` parameter; the existing call site in `OnOpenDdlRequested` passes `_service.ActiveProfile?.Id`. The VM stores it on the tab so Capture can read it back. Init-only property, never UI-visible today (V2 candidate: show source-connection chip in the tab header).

**View wiring** (`MainWindow.axaml.cs`):
- Fields: `_workspaceStore = new WorkspaceStore()`, `_pendingRestore` (loaded once in ctor), `_lastNormalBounds` (snapshot used at save time), two flags `_vmRestored` + `_boundsRestored` so each consumer fires exactly once.
- `_pendingRestore = _workspaceStore.Load()` in ctor (cheap; null-safe).
- **VM restore runs in `OnDataContextChanged`**, before the existing `_editor.Text = vm.QueryText` push. Order is intentional: SetQueryText → push to editor; `SelectTab(...)` inside Restore fires `OnVmPropertyChanged(SelectedWorkspaceTab)` which feeds the DDL editor.
- **Bounds restore runs in `OnWindowOpened`** so `Screens.All` is available for the sanity check. `AreBoundsSane(b)` enforces `Width >= MinWidth (900)`, `Height >= MinHeight (600)`, non-NaN X/Y, and that the proposed `PixelRect` intersects at least one screen's `WorkingArea`. Fall-through `catch` returns `true` if screens enumeration throws — better to trust saved bounds than discard them on platform quirks.
- `_lastNormalBounds` snapshotted in two places: `OnWindowPropertyChanged` for `ClientSize/Width/Height` and `OnWindowPositionChanged` for position (Position is NOT an `AvaloniaProperty` in Avalonia 12 — it fires its own `PositionChanged` event with `PixelPointEventArgs`). Snapshot only when `WindowState == Normal`, so maximizing doesn't smear the saved restore-rect.
- `OnWindowClosing` → `_currentVm.CaptureWorkspace()`, fills `WindowBounds` from `_lastNormalBounds` + current `WindowState.ToString()`, calls `_workspaceStore.Save(state)`. I/O exceptions swallowed — never block app shutdown on a transient disk hiccup.

**Tests added** (`tests/EmberTern.Tests/`):
- `WorkspaceStoreTests.cs` — 5 tests. Load null on missing/empty/corrupt; full round-trip of state with bounds + 3 tabs + active index + connection id; enums-as-strings serialization check.
- `WorkspacePersistenceVmTests.cs` — 9 tests. Capture emits Query/DDL with right fields; Capture reflects active tab index; Restore recreates DDL tabs and selects active; Restore clamps out-of-range active index; Restore drops existing DDL tabs (keeps Query); Restore skips malformed DDL entries (null name or kind); Restore pre-selects the matching connection node; Capture→Restore round-trip across two harness instances.

**Smoke-verified.** Launched twice end-to-end:
1. Fresh launch (no file) → exit 0 → workspace.json written with defaults (1280×800 at default position, single empty Query tab).
2. Seeded launch (custom bounds 1100×700 at 200,150 + restored SQL text) → exit 0 → re-saved file matches seeded state byte-for-byte. No spurious console output, no exceptions in process.

**Gotchas — promote to architecture lore.**

8. **`Window.Position` is NOT an `AvaloniaProperty`.** Subscribing to `PropertyChanged` and matching on `PositionProperty` is a compile error (`CS0103: name 'PositionProperty' does not exist`). Avalonia 12 exposes Position via the dedicated `PositionChanged` event with `PixelPointEventArgs`. ClientSize, Width, Height still come through `PropertyChanged` — only Position is the odd one out. **Rule**: when reaching for `WindowState`/`Position`/sizing changes, use `Window.PropertyChanged` for the sizing props and `Window.PositionChanged` for moves.

9. **`Screens` may be empty before window is on a platform.** Accessing `this.Screens.All` from the constructor returns null/empty on some configurations. Sanity-checking bounds against screens must happen in `Opened` (or later) — that's why bounds restore moved out of the ctor. The check itself wraps in `try/catch` and returns `true` on failure so a platform quirk doesn't strand the user with default-sized windows forever.

10. **Maximizing a window mid-session would otherwise destroy the Restore-rect on save.** Avalonia 12 doesn't expose `RestoreBounds`. The fix is to snapshot `_lastNormalBounds` from Position/ClientSize changes whenever `WindowState == Normal`, and persist that snapshot regardless of the closing-state. Persist the current `WindowState` separately so the next launch can re-maximize if needed. **Rule**: any session-persisting window-state code must track the Normal-bounds independently of the current bounds.

### Per-Connection Workspace (shipped)

Goal: tabs belong to a `ConnectionProfile`, not to the session globally. Connect → that profile's tabs reappear with their SQL text and DDL cache intact; disconnect → tabs hide (state preserved in memory); reconnect → tabs come back. Per-connection scope makes the "switch between ERPs all day" workflow viable — A's working SQL doesn't bleed into B.

**Schema change** ([WorkspaceState.cs](src/EmberTern.Core/Workspace/WorkspaceState.cs)):
- Removed the flat `Tabs` + `ActiveTabIndex` on `WorkspaceState`.
- Added `Dictionary<string, ConnectionWorkspace> Workspaces` keyed by `ConnectionProfile.Id`.
- New `ConnectionWorkspace { List<WorkspaceTab> Tabs, int ActiveTabIndex }`. Per-connection ActiveTabIndex is preserved across restarts (we deliberately extended past the spec's `Dictionary<string, List<WorkspaceTab>>` so the user lands on the same tab they left, per-profile).
- `WindowBounds` + `LastActiveConnectionId` stay on `WorkspaceState`. `LastActiveConnectionId` still only pre-selects the tree node — no auto-reconnect.

**VM model** ([MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs)):
- Removed the singleton `_queryTab` field. There is no Query tab at startup. The whole `WorkspaceTabs` ObservableCollection is empty until a connection becomes active.
- Added `_workspacesByConnection: Dictionary<string, ConnectionWorkspace>` (runtime + persistence pivot) and `_previousActiveProfileId: string?`.
- New `ApplyActiveConnectionChange(string? newProfileId)` is the single entry point for connection-driven workspace swaps: stash `WorkspaceTabs` into `_workspacesByConnection[_previousActiveProfileId]`, clear, then if there's a new profile load (or create fresh) its `ConnectionWorkspace`. Made `internal` so tests can drive it without a live `FbConnection` — production calls it from `OnActiveConnectionChanged` (which reads `_service.ActiveProfile?.Id` and `Dispatcher.UIThread.Post`s the work, because `FbConnection` async continuations don't land on UI thread and `ObservableCollection` mutations require it).
- `SnapshotCurrentTabs()` builds a `ConnectionWorkspace` from live `WorkspaceTabs` + `QueryText` + `SelectedWorkspaceTab` index. `LoadWorkspaceFor(profileId)` does the inverse, defensively prepending a Query tab if a corrupt dict entry has none (the user must never end up with a connected workspace that has no editor).
- `CaptureWorkspace()` mirrors live tabs into the dict for `_previousActiveProfileId` before serializing. `RestoreWorkspace(state)` populates the dict but **does not** mutate `WorkspaceTabs` — there's no active connection at startup; first Connect call brings the tabs out.
- `Delete(profile)` now also `Remove`s the dict entry so deleted profiles don't keep dead tab state forever.
- **Disconnect clears the entire bottom panel + status stats**, not just tabs. `ClearResultsAndMessages()` runs from the `newProfileId is null` branch of `ApplyActiveConnectionChange`: nulls `CurrentResult` + bumps `CurrentResultVersionTag` (the code-behind's `PopulateResultGrid` reacts by dropping DataGrid columns and `ItemsSource`), `Messages.Clear()` with `HasMessages`/`ShowMessagesEmptyHint` notifications, and resets `QueryStatsText` so the status bar's "N rows in X ms" disappears. Output tab is a placeholder — nothing to clear there. Rationale: results, log entries, and stats all belong to the connection that produced them; surfacing them post-disconnect (or after a future reconnect) would be misleading. Reconnect to the same profile restores tabs/SQL via the dict, but the result grid and message log start blank — the user re-executes if they want them.

**View** ([MainWindow.axaml.cs](src/EmberTern.App/Views/MainWindow.axaml.cs)):
- `OnVmPropertyChanged` now reacts to `QueryText` changes too (previously only DDL editor was repushed). When a connection switch flips `QueryText` to the new profile's SQL, the `SqlEditor` follows. The `if (_editor.Text != text)` guard breaks the `editor TextChanged → VM.QueryText → editor.Text` feedback loop.

**Project plumbing**:
- Added `<InternalsVisibleTo Include="EmberTern.Tests" />` to `EmberTern.App.csproj` so the test suite can call `ApplyActiveConnectionChange` and read `WorkspacesByConnection` directly. The Firebird project already had this; App didn't.

**Tests** — store + VM tests rewritten end-to-end:
- [WorkspaceStoreTests.cs](tests/EmberTern.Tests/WorkspaceStoreTests.cs) — 5 tests, round-trip on the new dict shape (two profiles, mixed tabs, per-connection ActiveTabIndex).
- [WorkspacePersistenceVmTests.cs](tests/EmberTern.Tests/WorkspacePersistenceVmTests.cs) — 13 tests, covering: ctor → no tabs visible; Connect → fresh Query tab when profile is new; Disconnect → stash + clear; Reconnect → restore stashed tabs; switch A→B → stash A, load fresh B; switch back to A → A restored, B still in dict; Capture while connected mirrors live tabs; Capture after disconnect picks up stashed tabs; Restore loads dict without populating tabs; LastActiveConnectionId pre-selects tree node; Delete drops dict entry; full cross-instance round-trip (Capture in one VM → Restore in another → Connect → tabs appear); pathological dict entry with no Query tab still presents one.

**Smoke**: app launches with empty workspace tabs (no active connection → nothing to show), exits cleanly, `workspace.json` written with `"Workspaces": {}` + saved window bounds. Schema validated end-to-end.

**Gotchas — promote to architecture lore.**

11. **`ObservableCollection` mutations from non-UI threads break compiled bindings.** `FirebirdConnectionService.ActiveConnectionChanged` fires on whichever thread the async work completed on (`ConfigureAwait(false)` everywhere). Touching `WorkspaceTabs.Add/Clear` from that thread crashes the binding layer for `ItemsControl` consumers. **Rule**: any service-event handler in a VM that mutates ObservableCollection (or properties bound to ItemsControl) must `Dispatcher.UIThread.Post(...)` the work. Scalar OnPropertyChanged is fine (binding layer handles it), but collection-change events go through directly.

12. **Test seams: prefer parameterizing the worker over mocking the service.** Driving `OnActiveConnectionChanged` end-to-end requires a real `FbConnection.OpenAsync`, which tests can't have. Solution: factor the connection-switch work into `ApplyActiveConnectionChange(string? newProfileId)` (internal). Production reads `_service.ActiveProfile?.Id` and passes it in; tests pass arbitrary string ids. No mock, no extra interface, no `IConnectionService` abstraction. Matches the codebase's "no interfaces without two implementations" rule.

### SQL Editor UX (shipped)

Three polish steps on the workspace tab strip and editor toolbar.

**Step 1 — tab wrap + per-kind icons + tighter type.** Tab strip was a horizontal `ScrollViewer` over a `StackPanel`; ten+ open DDL tabs forced horizontal scrolling. Replaced with a bare `ItemsControl` whose `ItemsPanel` is a `WrapPanel` — overflow flows onto a second (third, …) row à la IBExpert. Parent `Grid.Row="0"` height `36` (pinned) → `Auto` so the strip grows with the wrap. Per-tab font 12 → 11; activate-button padding `14,8 → 8,4`; close-button padding `6,4 → 4,2`. Each tab now carries a colored unicode glyph in front of the title via the same `IconBrushConverter` pipeline the metadata tree uses — DDL tabs reuse `MetadataNodeViewModel.IconFor(kind)` + `ResourceKeyFor(kind)` (promoted `private static → internal static`), so the icon on a `Procedure` tab is identical to the one in the sidebar. The Query tab gets its own `≣` glyph + `IconColor_Query` brush (neutral `#B0BEC5` on dark, slate `#455A64` on light) defined in both theme dictionaries of `Colors.axaml`.

**Step 2 — anchored tab renamed.** `UiStrings.WorkspaceTabUntitled = "Query 1"` → `"SQL Editor"`. Single-string change; the tab stays anchored (`IsClosable = false` on `WorkspaceTabViewModel.CreateQuery`) so it can't be closed by the user.

**Step 3 — Clear / Close-tab buttons on the editor toolbar.** The editor toolbar's outer Border previously hid entirely when a DDL tab was active (`IsVisible="{Binding IsQueryTabActive}"`); replaced that with per-button visibility so the toolbar stays present and just shows different controls. Added `ShowExecuteButton`/`ShowCancelButton` computed properties on `MainWindowViewModel` (`IsQueryTabActive && !IsExecuting` / `&& IsExecuting`) with `NotifyPropertyChangedFor` from both `_selectedWorkspaceTab` and `_isExecuting`. New buttons:
- **Clear** (`⌫ Clear`) — `ClearActiveEditorCommand` with `CanExecute = nameof(CanClearActiveEditor)` where `CanClearActiveEditor = IsQueryTabActive`. Wipes `QueryText` to empty. Greyed out on DDL tabs (read-only).
- **Close tab** (`✕ Close tab`) — `CloseActiveTabCommand` with `CanExecute = nameof(CanCloseActiveTab)` where `CanCloseActiveTab = SelectedWorkspaceTab is { IsClosable: true }`. Routes through the existing `CloseTab(tab)` so behaviour matches clicking the `×` on the tab itself. Greyed out on the anchored SQL Editor tab.

Both new buttons need `NotifyPropertyChangedFor`/`NotifyCanExecuteChangedFor` on `_selectedWorkspaceTab` so the IsEnabled state flips immediately on tab switch.

**Drive-by**: fixed `ConnectionNodeViewModelTests.IsConnected_TrueAutoExpands` — `OnIsConnectedChanged(true)` delegated to `LoadCategoriesAsync`, which bails on `_owner is null` (unit-test scenarios) before reaching `IsExpanded = true`. Added a synchronous `IsExpanded = true` before kicking off `LoadCategoriesAsync` so the connected→expanded invariant holds regardless of owner presence. No UI change — the post-load Dispatcher re-flip in `LoadCategoriesAsync` was already a no-op when the value didn't change, and CommunityToolkit short-circuits same-value sets.

**Gotcha promoted to architecture lore.**

13. **`x:Static` on internal static classes works from XAML in the same assembly.** `UiStrings` is `internal static`; binding `Text="{x:Static app:UiStrings.ToolbarClearEditor}"` compiles and resolves fine because both XAML and `UiStrings` live in `EmberTern.App`. **Rule**: when you need a string constant on a button or tooltip, `x:Static` to `UiStrings` is the right tool — don't add a property to the VM just to surface a constant. VM-string getters (`ExecuteLabel => UiStrings.ToolbarExecute`) made sense pre-step-3 when those toolbar buttons existed on the VM contract for symmetry with other VM-driven labels; for static labels that never change at runtime, `x:Static` cuts the indirection.

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

### Transaction TPB hardening — Milestone 1: C1 + diagnostics (shipped 2026-06-13)

First of a two-part transaction-handling fix. Driven by a real-world bug report: with an open transaction in EmberTern, the user could not perform some operations (notably DDL) in IBExpert on the same database. A full audit (see `memory/feedback_firebird_transactions.md`) found two distinct causes — this milestone fixes the smaller one (TPB flags) and adds the diagnostics needed to verify the larger one before fixing it. **868 → 892 tests** (+24). No connection-architecture change yet (that is Milestone 2 / C2).

**Audit findings (verified against the actual driver binary, not from memory).**
1. The single transaction-creation site — [`TransactionService.BeginTransactionAsync`](src/EmberTern.Firebird/TransactionService.cs) — used `connection.BeginTransactionAsync(IsolationLevel.ReadCommitted)`. The managed `FirebirdSql.Data.FirebirdClient` 10.3.4 maps that to a TPB ending in **`isc_tpb_wait`**, not `isc_tpb_nowait` — opposite of IBExpert's default "Data transaction" profile.
2. **`FirebirdClient` 10.3.4 genuinely forbids two concurrent transactions on one `FbConnection`.** Confirmed by scanning the shipped assembly: the literal guard string `"A transaction is currently active. Parallel transactions are not supported."` is present, and the connection exposes a single `HasActiveTransaction` slot. The Firebird *engine* supports many transactions per attachment (that's how IBExpert does it — one attachment, multiple native transaction handles), but the managed `FbConnection` wrapper does not expose it. ⇒ "a separate short metadata transaction on the same connection" is **impossible** while a working tx is open; true decoupling needs a **second connection** (Milestone 2).
3. The real cause of the IBExpert blocking is **not** `wait` — it is that the long-lived read-write working transaction is shared by all the read-only metadata readers ([`FirebirdMetadataReader`](src/EmberTern.Firebird/FirebirdMetadataReader.cs), [`FirebirdDdlReader`](src/EmberTern.Firebird/FirebirdDdlReader.cs), [`FirebirdTableDetailReader`](src/EmberTern.Firebird/FirebirdTableDetailReader.cs)) via `cmd.Transaction = _transactionService?.ActiveTransaction`. When a working tx is active, browsing the schema runs *inside* it and pins object metadata → blocks DDL from other connections.

**C1 — explicit TPB.** [`TransactionService.BuildWorkingTransactionOptions()`](src/EmberTern.Firebird/TransactionService.cs) now returns an `FbTransactionOptions` with `FbTransactionBehavior.Write | ReadCommitted | RecVersion | NoWait` — i.e. `isc_tpb_write + isc_tpb_read_committed + isc_tpb_rec_version + isc_tpb_nowait`, matching IBExpert. `BeginTransactionAsync` uses it instead of `IsolationLevel.ReadCommitted`. Effect: EmberTern fails fast on a lock conflict instead of hanging indefinitely. **This does NOT by itself stop the IBExpert blocking** (that is finding #3 → C2).

**Diagnostics infrastructure** (no UI, per scope). New [`FirebirdDiagnostics`](src/EmberTern.Firebird/FirebirdDiagnostics.cs):
- `GetCurrentTransactionIdAsync` (`SELECT CURRENT_TRANSACTION`), `DescribeCurrentTransactionAsync` (one-liner: id + isolation + lock-resolution + read-only, from `MON$TRANSACTIONS WHERE MON$TRANSACTION_ID = CURRENT_TRANSACTION`), `GetTransactionsAsync` (`MON$TRANSACTIONS`), `GetAttachmentsAsync` (`MON$ATTACHMENTS`), `BuildReportAsync` (full text dump).
- Decoders (internal, unit-tested): `DecodeIsolationMode` (0=consistency, 1=concurrency, 2=read_committed rec_version, 3=no_rec_version, 4=read_consistency), `DecodeLockTimeout` (-1=wait infinite, 0=no wait, N=wait Ns), transaction/attachment state.
- Same access pattern as the other readers: holds `CommandLock`, attaches to the working tx so `CURRENT_TRANSACTION` resolves to it; never opens its own tx.
- **Begin-time log hook**: when env var `EMBERTERN_TX_DIAG` is set, `BeginTransactionAsync` appends the real server-side TPB (`TX-BEGIN tx=N isolation=[...] lock=[no wait] readOnly=False`) to `%TEMP%\EmberTern-debug.log`. Zero cost when unset. This is the one-switch way to confirm nowait took effect against a live server.

**Manual verification protocol (requires the user's live FB5 + IBExpert — not runnable here).**
1. Set `EMBERTERN_TX_DIAG=1`, launch, connect, press F5 on any SELECT → the working tx opens. Check `%TEMP%\EmberTern-debug.log` → expect `lock=[no wait]`, `isolation=[read committed (rec_version)]`. Confirms C1.
2. Leave the tx open (no Commit). Open a table in TableDetail / view another object's DDL → those reads attach to the open working tx.
3. In IBExpert, attempt `ALTER TABLE` / `DROP` on a touched object.
**Predicted result: still blocked.** `nowait` changes how EmberTern reacts to a conflict, not its lock/metadata footprint — the open working tx still pins every object the readers touched. This is the empirical confirmation that C2 (Milestone 2) is required.

**C2 pre-implementation analysis (next session — do NOT build yet).**

Reader routing in the two-connection model:

| Path | Connection | Reason |
|---|---|---|
| `FirebirdQueryExecutor` (F5), `FirebirdDdlExecutor`, `FirebirdDataEditor` | **#1 working** | user SQL/DDL/DML — must see own uncommitted changes |
| `FirebirdMetadataReader` (`ListAsync` / `ListColumnsAsync` / `ListDomainsAsync`) | **#2 metadata** | pure browse, read-only, never needs uncommitted state |
| `FirebirdDdlReader.FetchDdlAsync` (standalone DDL tab) | **#2 metadata** | browse (edge: a just-altered object's DDL lags until commit) |
| `FirebirdTableDetailReader` — initial browse load (`EnsureLoadedAsync`) | **#2 metadata** | browse |
| `FirebirdTableDetailReader` — `RefreshStructureAsync` after a structural edit | **#1 working** | must show the user's uncommitted ALTER (the "Add field, see it, Rollback to undo" UX) |
| `FirebirdTableDetailReader.GetDataPreviewAsync` in the Dane *edit* context | **#1 working** | must show the user's uncommitted DML |
| `FirebirdDiagnostics` (current-tx) | **#1 working** (attach) | reports the working tx itself |

Key design point — **`FirebirdTableDetailReader` and `FirebirdDdlReader` need *contextual* routing**, not a fixed connection: browse → #2, post-edit refresh → #1. `FirebirdMetadataReader` is unconditionally #2. The routing rule is by **intent** (browse vs. show-my-uncommitted-change), not by tx-active state — otherwise an unrelated browse while a working tx is open would still pin objects on #1.

Classes to modify in C2:
- `FirebirdConnectionService` — own a second read-only `FbConnection` (metadata) + its own `SemaphoreSlim` (separate `CommandLock`); open/close it in lockstep with the primary, same profile/credentials; add `RequireOpenMetadataConnection()`.
- The three readers — take which connection + lock to use (per-call or two instances). Honor the no-interface rule (pass the `FbConnection` + lock, or add a small internal selector).
- `MainWindowViewModel` — wire browse readers to #2, executors/editor to #1.
- `TableDetailTabViewModel` — distinguish browse-load from post-edit-refresh and pick the connection accordingly; `RefreshStructureAsync` must stay on #1.

Impact on `RefreshStructureAsync` + uncommitted visibility: `RefreshStructureAsync` MUST read on #1 so a just-applied (uncommitted) ALTER is visible; the plain initial load reads on #2. Consequence to accept: the metadata **tree** (always #2) will not reflect an uncommitted DDL change until Commit — correct/expected (the tree shows committed schema). Cost of the second connection: one extra `MON$ATTACHMENTS` entry + a second physical attachment (`Pooling=false`), same credentials — negligible for a dev tool, and lighter than the alternative (which doesn't exist on this driver).

**Gotcha promoted to architecture lore.**

85. **`IsolationLevel.ReadCommitted` on `FirebirdSql.Data.FirebirdClient` maps to a TPB ending in `isc_tpb_WAIT`.** The ADO.NET `IsolationLevel` enum can't express the wait/nowait or rec_version axes, and the driver's default for ReadCommitted is `wait` — so EmberTern blocked indefinitely on lock conflicts. To match IBExpert (and to fail fast), build an explicit `FbTransactionOptions { TransactionBehavior = Write | ReadCommitted | RecVersion | NoWait }` and pass it to `BeginTransactionAsync(options)`. **Rule**: never start a Firebird transaction from a bare `IsolationLevel` — always go through explicit `FbTransactionOptions` so wait/nowait and rec_version are deliberate. The single transaction-creation site is `TransactionService.BeginTransactionAsync`; any new one must do the same. Confirm the live TPB via `MON$TRANSACTIONS.MON$LOCK_TIMEOUT` (0 = nowait) — `EMBERTERN_TX_DIAG=1` logs it on every begin.

### Transaction profiles (IBExpert-style) — shipped 2026-06-13

Extension of C1 (not a return to C2). Per-connection choice of transaction profile, mirroring IBExpert's presets, so Firebird admins can deliberately switch the working transaction's TPB. Default stays the safe `Read Committed`. **No transaction-architecture change** — still one working transaction, still C1's explicit-TPB path; only the flag set is now profile-driven. **892 → 904 tests** (+12).

**Profiles → TPB mapping** ([`TransactionService.BuildTransactionOptions(TransactionProfile)`](src/EmberTern.Firebird/TransactionService.cs) — pure, unit-pinned):
| Profile | `FbTransactionBehavior` | TPB |
|---|---|---|
| **Read Committed** (default) | `Write \| ReadCommitted \| RecVersion \| NoWait` | `isc_tpb_write + read_committed + rec_version + nowait` |
| **Snapshot** | `Write \| Concurrency \| NoWait` | `isc_tpb_write + concurrency + nowait` |
| **Read Only Table Stability** | `Read \| Consistency` | `isc_tpb_read + consistency` |
| **Read Write Table Stability** | `Write \| Consistency` | `isc_tpb_write + consistency` |

Access-mode note: the spec listed only the isolation flags for Read Committed / Snapshot; both are data transactions so they carry explicit `Write` (Read Committed unchanged from C1). The two Table Stability profiles carry the exact `read`/`write` + `consistency` the user specified — deliberately **no nowait** (server-default wait), since they are conscious admin profiles meant to lock.

**Model & persistence.** New [`TransactionProfile`](src/EmberTern.Core/Connections/TransactionProfile.cs) enum in Core (`ReadCommitted = 0` so legacy files default safely). [`ConnectionProfile.TransactionProfile`](src/EmberTern.Core/Connections/ConnectionProfile.cs) (default `ReadCommitted`). [`ConnectionProfileStore`](src/EmberTern.Core/Connections/ConnectionProfileStore.cs) gained a `JsonStringEnumConverter` so it persists as the readable name; old `connections.json` without the field loads as `ReadCommitted`.

**Resolution & lifecycle.** `TransactionService.ResolveActiveProfile()` reads `_connectionService.ActiveProfile?.TransactionProfile` **at begin time**. So changing the profile affects only the NEXT transaction — an active transaction keeps its parameters until Commit/Rollback (the user's rule). No in-flight reparametrization, no autocommit (rule #3 intact).

**UI.** New-connection dialog ([NewConnectionDialog.axaml](src/EmberTern.App/Views/NewConnectionDialog.axaml)) got a "Transaction profile" ComboBox after Charset (`SelectedItem` bound to a `TransactionProfileOption` wrapper — Avalonia has no `SelectedValueBinding`, gotcha #57). Below it: a subtle per-profile description, swapped for a prominent `WarningBrush` SemiBold line for the two Consistency profiles ("locks whole tables and can block other users") — **warns, does not block** (conscious admin feature). Title bar shows an accent chip `TX: <profile>` ([MainWindowViewModel.ActiveTransactionProfileText](src/EmberTern.App/ViewModels/MainWindowViewModel.cs), notified on `ActiveConnectionChanged`) so the active profile is always visible. UI labels/descriptions live in [`TransactionProfileCatalog`](src/EmberTern.App/ViewModels/TransactionProfileCatalog.cs) + `UiStrings` (Core enum stays UI-free).

**Tests.** [TransactionTpbTests](tests/EmberTern.Tests/TransactionTpbTests.cs) rewritten to pin all four mappings (incl. negative assertions — Snapshot is not Consistency, Table Stability is not NoWait). New [TransactionProfileCatalogTests](tests/EmberTern.Tests/TransactionProfileCatalogTests.cs) (order, warning flags, label, fallback). [ConnectionProfileStoreTests](tests/EmberTern.Tests/ConnectionProfileStoreTests.cs): default = ReadCommitted, round-trip persists as string name, legacy JSON without the field loads as ReadCommitted.

### Security — connection-password encryption at rest (DPAPI) + audit (shipped 2026-06-13)

First of the user-settings-security workstream. Full audit (report kept out of the repo; lives in this session) found exactly one hard secret persisted in cleartext: `ConnectionProfile.Password` in `%APPDATA%\EmberTern\connections.json`. `workspace.json` (SQL/DDL text) and `folders.json` hold no credentials; the `%TEMP%\EmberTern-debug.log` connection-string line already masks the password. Decision (user-approved): encrypt **only `Password`** with **DPAPI CurrentUser** — no custom crypto, no Base64/XOR, no app-embedded key. **904 → 909 tests** (+5). No UI change.

**Reusable seam (Core, zero deps)** — [SecretProtector.cs](src/EmberTern.Core/Security/SecretProtector.cs): a concrete sealed class holding a `Func<string,string>` protect/unprotect pair (not an interface — rule #2 honoured; the two behaviours are delegates). `SecretProtector.Identity` is the no-op (stored == plaintext), the safe default when no protector is injected (tests, non-Windows). This is the **foundation** the planned ApplicationSettings store and config export/import will share — each takes the same `SecretProtector` rather than re-deriving crypto.

**DPAPI implementation (App)** — [Security/DpapiSecretProtector.cs](src/EmberTern.App/Security/DpapiSecretProtector.cs): `ProtectedData.Protect/Unprotect` with `DataProtectionScope.CurrentUser` + app-specific (non-secret) entropy `"EmberTern.v1.secret"`, Base64 in/out. Empty plaintext → empty stored (no blob for a blank password). `Create()` wraps it into a `SecretProtector`. Platform-guarded with `OperatingSystem.IsWindows()` (satisfies CA1416 under `TreatWarningsAsErrors`; throws `PlatformNotSupportedException` off-Windows). NuGet `System.Security.Cryptography.ProtectedData` 9.0.0 added to App (not in the BCL on .NET Core+).

**Store rewrite** — [ConnectionProfileStore.cs](src/EmberTern.Core/Connections/ConnectionProfileStore.cs): on-disk shape changed from a bare `List<ConnectionProfile>` array to a **versioned container** `ConnectionsFile { SchemaVersion, Connections[] }` (`CurrentSchemaVersion = 1`). A private `ConnectionProfileDto` (separate from the runtime model, same split as the workspace DTOs) carries BOTH the legacy plaintext `Password` (read-only migration path, never written) and the v1 `ProtectedPassword`. Constructor overloads: `()` / `(SecretProtector)` / `(string dir)` / `(string dir, SecretProtector?)` — null protector ⇒ `Identity`. `ToDto` sets `Password = null` (`JsonIgnoreCondition.WhenWritingNull` keeps it out of the file) and `ProtectedPassword = protector.Protect(p.Password)`. `FromDto` decrypts via `UnprotectSafe` (catches any exception → empty password, so a DPAPI blob copied from another account/machine degrades gracefully instead of crashing the load).

**Auto-migration** — `LoadAll` detects a legacy root JSON **array** (via `JsonDocument` root `ValueKind`) → maps the plaintext passwords → immediately re-saves as the encrypted v1 container. A deliberate one-time write triggered by a read; secures existing installs on first launch with no user action. The re-save's `IOException`/`UnauthorizedAccessException` are swallowed (profiles still returned; next launch retries). Corrupt JSON still throws (unchanged strict read contract).

**Atomic write** — `AtomicWrite`: write to `connections.json.tmp` in the same dir, then `File.Replace` (or `File.Move` when the target doesn't exist yet). No torn file if the process dies mid-write.

**Production wiring** — [App.axaml.cs](src/EmberTern.App/App.axaml.cs) and the parameterless [MainWindowViewModel](src/EmberTern.App/ViewModels/MainWindowViewModel.cs) ctor construct `new ConnectionProfileStore(DpapiSecretProtector.Create())`. Tests use the `(dir)` overload ⇒ Identity, so they stay platform-agnostic.

**Verified end-to-end against the user's real `connections.json`**: 4 connections migrated to `SchemaVersion: 1`, each with a 308-char DPAPI Base64 `ProtectedPassword`, no plaintext `Password` key remaining; app launched and decrypted them cleanly.

**Explicitly deferred (architecture noted, not built — per scope):** ApplicationSettings store, grid/column-width profiles, layout persistence, and config **export/import**. Export/import is the one with a real crypto consequence: DPAPI CurrentUser ciphertext is **not portable** across machines/accounts (intended — a synced/backed-up `connections.json` yields no passwords elsewhere). When export/import lands, the portable path should re-protect secrets with a **user-supplied passphrase** (PBKDF2 → AES, standard primitives), separate from the at-rest DPAPI path. Backup note: backing up `%APPDATA%\EmberTern` only restores working passwords on the **same Windows account + machine**.

**Tests** ([ConnectionProfileStoreTests.cs](tests/EmberTern.Tests/ConnectionProfileStoreTests.cs), +5): password encrypted-at-rest + decrypted-on-load (asserts no plaintext / no legacy key in the file, via a reversible `"ENC:"`-prefix fake protector); legacy plaintext array migrated to encrypted v1 container; `Unprotect` failure degrades to empty password; empty password round-trips; `DpapiSecretProtector` real round-trip (guarded to Windows).

**Gotcha promoted to architecture lore.**

86. **`System.Security.Cryptography.ProtectedData` (DPAPI) needs the NuGet package AND an `OperatingSystem.IsWindows()` guard under `TreatWarningsAsErrors`.** It's not in the BCL on .NET Core+ (add the Microsoft-owned package). The API is `[SupportedOSPlatform("windows")]`, so calling it from a plain `net9.0` (not `net9.0-windows`) assembly raises CA1416 — an error here. Wrap each call site behind `if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(...)`; the flow analyser accepts the negated guard-then-throw and suppresses CA1416 without changing the TFM. Keep platform-crypto out of Core (zero-deps rule): the DPAPI impl lives in App and is injected into the Core store as a `SecretProtector` delegate pair.

### Unified encrypted settings store — ApplicationSettingsStore (shipped 2026-06-13)

Consolidated the three separate settings files (`connections.json`, `workspace.json`,
`folders.json`) into **one whole-file DPAPI-encrypted file**, `%AppData%\EmberTern\settings.dat`,
and laid the foundation for `GridProfiles` / `AppearanceSettings` / config Import/Export.
**909 → 919 tests** (+10). Build clean.

**Aggregate model** ([src/EmberTern.Core/Settings/](src/EmberTern.Core/Settings/)):
- `ApplicationSettings` — `SchemaVersion`, `List<ConnectionProfile> Connections`, `FolderState Folders`, `WorkspaceState Workspace`, `UserSettings UserSettings`. The whole object is serialized to JSON and run through a `SecretProtector` at the I/O boundary, so `ConnectionProfile.Password` is **plaintext inside the encrypted blob** — per-field `ProtectedPassword` is gone (double-DPAPI was pointless).
- `UserSettings` — its own section holding `List<GridProfile> GridProfiles` + `AppearanceSettings Appearance` (separate from connection/session data, per the user's request).
- `GridProfile` — shipped with the **full future field set up front** to avoid a second schema migration when grid-layout memory lands: `GridId`, `Dictionary<string,double> ColumnWidths`, `List<string> ColumnOrder`, `bool AutoFitColumns`. No consumer wires it yet.
- `AppearanceSettings` — `string? ThemeVariant`, `string? AccentColor` (stub; no consumer yet).

**Store** ([ApplicationSettingsStore.cs](src/EmberTern.Core/Settings/ApplicationSettingsStore.cs)) — the single real persistence. Ctors mirror the old stores (`()`, `(SecretProtector)`, `(string dir)`, `(string dir, SecretProtector?)` → Identity default). `Load()` → `ApplicationSettings?`: reads `settings.dat`, `Unprotect`s, deserializes; returns **null** (forgiving, no overwrite) on missing/empty/corrupt/undecryptable. When `settings.dat` is missing it runs **one-time migration** from any legacy files present — `connections.json` (both v0 plaintext array and v1 `ProtectedPassword` container, recovered via the protector), `folders.json`, `workspace.json` — writes the unified file, then **deletes the 3 legacy files**. `Save()` serializes → `Protect` → atomic write (`.tmp` + `File.Replace`). `JsonStringEnumConverter` so enums persist as names.

**Facade strategy (low blast radius).** `ConnectionProfileStore` / `FolderStore` / `WorkspaceStore` keep their exact public APIs but are now **section accessors** over a shared `ApplicationSettingsStore`. Each write is read-modify-write on its own section so it never clobbers the others. The VM, the View, and all black-box VM tests are unchanged; only the three *format-specific* store test files were rewritten (their on-disk format changed by design) + a new `ApplicationSettingsStoreTests`. `ConnectionProfileStore.Protector` is now `public` (crosses Core→App; holds no secret).

**Protector threading (critical).** Every facade over the same dir MUST use the same protector or an Identity-protector save would write the shared file unencrypted. Production: `App.axaml.cs` builds `DpapiSecretProtector.Create()` → `ConnectionProfileStore`; the VM threads `store.Protector` into the `FolderStore` it creates; the **View derives its `WorkspaceStore` from the VM's store dir + protector in `OnDataContextChanged`** (was a `new()` field initializer — see gotcha #88). Tests pass no protector → Identity (plaintext) consistently.

**Gotchas — promote to architecture lore.**

87. **Whole-file DPAPI encryption changes the failure mode from "lose the password" to "lose everything".** With per-field encryption a bad decrypt degraded only that field; with the whole file encrypted, an undecryptable `settings.dat` (e.g. copied to another Windows account/machine) yields **no settings at all**. `ApplicationSettingsStore.Load` returns null and **never overwrites** the unreadable file (it may decrypt on the right machine). This is the intended stronger "nothing leaks cross-machine" posture, but it means the store degrades to a fresh/empty state rather than a partial one — design every consumer's null-handling accordingly.

88. **A View that owns persistence must derive its store location from the VM, never hardcode the default dir — or headless tests hit the real `%AppData%`.** `MainWindow` had `private readonly WorkspaceStore _workspaceStore = new(DpapiSecretProtector.Create());` (default dir) and `_pendingRestore = _workspaceStore.Load()` in the ctor. `ConnectionExpandBindingProbe` builds the **real** `MainWindow`, so that field initializer + ctor `Load()` ran against the user's live `%AppData%\EmberTern` — and with the new unified store it triggered destructive **legacy-file migration of real data** during `dotnet test`. Fix: make `_workspaceStore` nullable and build it in `OnDataContextChanged` from `_currentVm.Store` (its dir + `Protector`), so the View's workspace section lands in the same file the VM uses — temp dir in tests, real dir in production. **Rule**: any store a View constructs for itself must be sourced from the attached VM's store location, not a hardcoded default; verify with a before/after mtime check on the real file during a test run.

### C2 — second metadata connection + Data/Metadata transaction-profile split (shipped 2026-06-14)

The IBExpert-style payoff: separate the connection's transaction handling into two independent **lanes** so a metadata-only aggressive profile (e.g. Read Write Table Stability for fixing procedures/triggers/tables) never leaks into everyday data work. Single milestone — C2 (the second physical connection) and the profile split shipped together because the split is meaningless without two transaction contexts, and two contexts require two attachments (the managed driver forbids two transactions on one `FbConnection`). **919 → 924 tests.**

**Two lanes.** [FirebirdConnectionService](src/EmberTern.Firebird/FirebirdConnectionService.cs) now holds two `FbConnection`s to the same database (same profile/credentials, `Pooling=false`) and two `SemaphoreSlim` command locks. New `ConnectionRole { Data, Metadata }` enum; `RequireOpenConnection(role)` / `GetCommandLock(role)` / `MetadataIsIndependent`. The metadata attachment opens **best-effort** in `ConnectAsync` (after the data one); if it fails (e.g. server connection limit) it's logged and the Metadata role transparently **aliases the Data role** (degraded mode) so metadata work still functions on the single connection.

**Profiles.** [ConnectionProfile](src/EmberTern.Core/Connections/ConnectionProfile.cs) replaced the single `TransactionProfile` with `DataTransactionProfile` + `MetadataTransactionProfile` (both default ReadCommitted) + a read-only `LegacyTransactionProfile` JSON shim (`[JsonPropertyName("TransactionProfile")]`, omitted-when-null). [ApplicationSettingsStore](src/EmberTern.Core/Settings/ApplicationSettingsStore.cs) bumped `CurrentSchemaVersion` 1→2 and migrates **variant A** (old single value → Data; Metadata → ReadCommitted) on both load paths: `MigrateTransactionProfiles` for the unified `settings.dat`, and `FromLegacyDto` for the pre-unified `connections.json`. The `TransactionProfile` enum + `TransactionProfileCatalog` are unchanged.

**TransactionService is lane-parameterized.** [TransactionService](src/EmberTern.Firebird/TransactionService.cs) gained a `ConnectionRole` + a `Func<ConnectionProfile?, TransactionProfile>` profile selector + an optional degraded `fallback`. The convenience ctor `TransactionService(cs)` is the Data lane reading `DataTransactionProfile`. Two instances live in the VM: data (`_transactionService`) and metadata (`_metadataTransactionService`, selector → `MetadataTransactionProfile`, `fallback: _transactionService`). It exposes `RequireOpenConnection()` + `CommandLock` resolved by its own `EffectiveRole` (with degraded aliasing), so readers/executors that hold a TransactionService get connection+lock+tx from one consistent source. In degraded mode the metadata service forwards its whole lifecycle (Begin/Commit/Rollback/State/ActiveTransaction/NotifyStatementExecuted) to the data service so there's only ever one transaction on the shared connection. `BuildTransactionOptions(profile)` (the TPB mapping) is unchanged — gotcha #85 still applies.

**Lane routing** (each reader/executor uses the lane of its injected TransactionService; falls back to the data connection when none — tests):
| Path | Lane |
|---|---|
| `FirebirdQueryExecutor` `_executor` (F5) | Data |
| `FirebirdQueryExecutor` `_metadataExecutor` (Shift+F5) | Metadata |
| `FirebirdMetadataReader`, `FirebirdDdlReader`, `FirebirdDdlExecutor` | Metadata |
| `FirebirdTableDetailReader` — structure (fields/indexes/constraints/description/dependencies) | Metadata |
| `FirebirdTableDetailReader.GetDataPreviewAsync` / `GetRowCountAsync` (Dane) | Data |
| `FirebirdDataEditor` (Dane inline edit) | Data |
| `FirebirdDiagnostics` | follows its txService's lane |

This is the real fix for the original bug (open EmberTern tx blocked IBExpert DDL): browsing now runs on the metadata attachment via an implicit per-command read tx, so it never pins objects in the data working transaction.

**SQL Editor — variant C1.** `F5` = Data lane; `Shift+F5` ("Execute on Metadata", `▶▶` toolbar button) = Metadata lane. This is how a hand-written `ALTER PROCEDURE` / `ALTER TRIGGER` gets the Metadata profile (those objects have no structure-editor path). Every execute logs an explicit Info message — "Executed via Data profile (Read Committed)." / "Executed via Metadata profile (Read Write Table Stability)." — so the user never guesses which lane/profile ran a statement (explicit user requirement).

**Two transaction lifecycles in the UI.** Data and Metadata each have their own working transaction, so the UI shows both: dual title-bar chips (`Data: …` / `Meta: …`), two Commit/Rollback pairs on the editor toolbar (data ✓/✕ gated `ShowDataTransactionButtons`; metadata ✓/✕ — table-tinted, separated — gated `ShowMetadataTransactionButtons`, only when `MetadataLaneIndependent`), and a two-group bottom transaction bar. `OnTransactionStateChanged` is subscribed to both services and recomputes both lanes (metadata treated as inert in degraded mode to avoid double-counting). Disconnect rolls back both lanes. Structure-editor DDL auto-begins the metadata working tx; the user commits/rolls it back via the metadata buttons (reachable on every TableDetail sub-tab + the New Table tab + the Query tab once a Shift+F5 metadata tx is open).

**Dialog.** [NewConnectionDialog](src/EmberTern.App/Views/NewConnectionDialog.axaml) now has two profile ComboBoxes (Data + Metadata), each with its own per-profile description and the prominent `WarningBrush` consistency warning for the two Table Stability profiles.

**Gotchas — promote to architecture lore.**

89. **The managed FirebirdClient forbids two transactions on one `FbConnection`, but the engine supports many per attachment — so "two transaction profiles at once" REQUIRES two physical connections.** The profile split could not be done on a single connection without silently tearing down/recreating the user's working tx on every context switch. C2 (a second `FbConnection` to the same DB) is the enabling mechanism; the split is impossible before it. Open the second attachment best-effort and alias it to the first when it can't open, so a server connection limit degrades instead of breaking.

90. **Give each reader/executor its connection + lock + transaction from ONE source (its `TransactionService` lane), never mix sources.** A reader that takes `connection` from `connectionService.RequireOpenConnection()` (data) but `tx` from a metadata service would attach a metadata transaction to a command on the data connection — a cross-lane bug. `TransactionService` exposes `RequireOpenConnection()` + `CommandLock` resolved by its own role (with degraded aliasing), so a reader that uses only its txService for all three can't desync — and degraded-mode fallback happens automatically. `FirebirdTableDetailReader` is the one reader that legitimately spans both lanes (structure → metadata, data preview → data): it holds two TransactionService references and each method picks the matching one.

91. **Degraded-mode forwarding belongs in `TransactionService`, not at every call site.** When the metadata attachment is unavailable, the metadata `TransactionService` (which carries a `fallback` to the data service) forwards its entire public surface to the data service via a single `ShouldDelegate` guard. Readers, executors, the VM's commit/rollback, and the transaction bar all keep calling the metadata service as if it were independent; the forwarding makes it transparently share the data lane's single transaction. The VM only checks `MetadataIsIndependent` for one thing: whether to render the metadata lane as a *separate* UI group (hidden in degraded mode so it doesn't duplicate the data lane).

### C2 follow-up — automatic lane routing + compact lane chips (shipped 2026-06-14)

UX polish over C2. Two findings from manual testing: (1) making the user choose F5=Data / Shift+F5=Metadata was confusing — most users never learn when to use which; (2) the `Data: Read Committed` / `Meta: Read Committed` title-bar chips were too wide. **924 → 960 tests** (+36). Build clean.

**1. Single Execute, automatic lane.** Shift+F5 and the `▶▶` "Execute on Metadata" toolbar button are **gone**. F5 / Ctrl+Enter is the only Execute; the lane is chosen automatically from the SQL. No manual override exists by design (explicit user decision — an escape hatch would just re-introduce the "which key?" confusion). Hand-written `ALTER PROCEDURE` / `CREATE TRIGGER` etc. route to Metadata automatically because the classifier recognizes them — which is exactly why Shift+F5 became redundant.

- New pure classifier [SqlStatementClassifier.cs](src/EmberTern.Core/Sql/SqlStatementClassifier.cs) in `EmberTern.Core.Sql` (sibling of `SqlFormatter` / `SqlKeywords`): `enum StatementLane { Data, Metadata, Ambiguous }` + `Classify(sql)`. Strips leading whitespace + line/block comments, reads the leading keyword(s):
  - **Data**: SELECT, WITH (CTE), INSERT, UPDATE, DELETE, MERGE, EXECUTE (PROCEDURE/BLOCK).
  - **Metadata**: CREATE / ALTER / DROP / RECREATE / COMMENT / DECLARE (EXTERNAL FUNCTION) / GRANT / REVOKE, and SET GENERATOR / SET STATISTICS.
  - **Ambiguous**: SET TERM / SET TRANSACTION, unrecognized leading token, empty/comment-only.
- `MainWindowViewModel.RunExecuteAsync()` (no param now) calls `Classify(ResolveActiveSql())`; **Ambiguous → Data** (read_committed + nowait, never blocks). Removed `ExecuteQueryOnMetadataCommand` + its keybinding + button + the `ToolbarExecuteMetadata*` strings. `_metadataExecutor` stays — the router uses it for the Metadata branch.
- The existing "Executed via {Data|Metadata} profile ({label})." Messages-log line is now the **sole** transparency mechanism (the user no longer picks the lane), so it's load-bearing — kept and fed the router's verdict.
- **EXECUTE BLOCK → Data, by principle not fallback.** Firebird PSQL cannot contain DDL inside a block; an EXECUTE BLOCK is a data/result-set construct. The one residual gap — dynamic DDL via `EXECUTE STATEMENT 'CREATE …'` built from a variable — is statically undecidable and vanishingly rare; it runs harmlessly on the Data lane. A scanner for it was assessed and rejected as disproportionate.
- **Known limitation (documented, not worked around):** classification is by the *first* statement — the executor already sends one command per Execute, so a multi-statement script under one F5 is a degenerate case routed by its leading statement.

**2. Compact, color-distinct lane block.** The title-bar transaction-profile indicator is **two stacked lines**, each a static lane label (`Data:` / `Meta:`) plus the full profile name in a **lane-colored badge** — blue badge for Data, purple for Metadata (`DataLaneChipBrush` / `MetadataLaneChipBrush`, white badge text). Vertical stacking keeps the block narrow while the full name stays readable without hovering (readability over max compression — short codes `D: RC` were tried first and rejected as too cryptic; full-line foreground-colored text was the intermediate step). FontSize 10 so the block fits inside the **hard-fixed 36 px** title-bar row (`RowDefinitions="36,*,28"` — content can't expand it). VM exposes the badge text only (`DataProfileName` / `MetadataProfileName`); the `Data:`/`Meta:` prefix is static (`UiStrings.TransactionProfile*Label`). Full lane name also in each line's tooltip (`DataTransactionProfileTooltip` / `MetadataTransactionProfileTooltip`).

**Brand-row geometry (alignment pass).** The profile block is a **2×2 Grid** (`RowDefinitions="Auto,Auto" ColumnDefinitions="Auto,Auto"` + `RowSpacing`/`ColumnSpacing`), not nested StackPanels: the Auto label column sizes to the wider of `Data:`/`Meta:`, so both badges left-align in column 1, and the whole block is `VerticalAlignment="Center"` as a unit. Gaps come from `RowSpacing`/`ColumnSpacing` — **no per-element margin nudges** (the earlier hand-tuned offsets are gone). The brand logo slot is **square 26×26 `Stretch="Uniform"`** (source is 256×256) so there's no horizontal slop and its centre lines up with the `EmberTern` text — the prior `43×28` slot left the logo looking off-centre. Every brand-row item (`logo / title / separator / connection name / profile block`) is `VerticalAlignment="Center"` inside the centered row, so they share one horizontal axis (VS / DataGrip style). Verified by screenshot of the connected header.

**Click-to-change-profile from the chip: deliberately deferred** (user's call). The architecture is ready for it — `TransactionService.ResolveActiveProfile()` reads the profile at begin time, so a live switch would apply to the next transaction with no C2/lifecycle change; a future flyout + `SetDataProfile`/`SetMetadataProfile` command (mutate `ActiveProfile` → `Upsert` → notify chips) is a low-medium-cost add. Open product question for then: persist the change vs. session-only.

**Tests** — [SqlStatementClassifierTests.cs](tests/EmberTern.Tests/SqlStatementClassifierTests.cs) (data/metadata/ambiguous theories, comment-skipping, first-statement-wins). The chip change is purely presentational (no VM-test change).

### settings.dat file container — future-proofing (shipped 2026-06-14)

Architectural milestone (no data-model or user-facing change) hardening `settings.dat` for multi-year evolution: encryption-algorithm changes, config export/import, version migrations, and downgrade protection. Driven by a prior architecture audit that graded the unified store **B — small corrections, do them while the install base is ~1 user**. **958 → 970 tests** (+12). Build clean, smoke-verified, real `settings.dat` migrated in place.

**The shape change.** `settings.dat` was a bare protector blob (`Base64(DPAPI(JSON))` in prod, raw JSON with Identity). It is now a tiny **plaintext container header + the same payload**:

```
EMBERTERN-SETTINGS<TAB><containerVersion><TAB><encryptionScheme>\n
<payload exactly as the protector produced it>
```

The header is deliberately **outside** the encryption so a load can read it without the key. Payload bytes are unchanged — the migration just prepends 27 bytes (`EMBERTERN-SETTINGS\t1\tdpapi\n`).

**KROK 1 — container** ([SettingsFileContainer.cs](src/EmberTern.Core/Settings/SettingsFileContainer.cs), zero-deps Core). `Magic = "EMBERTERN-SETTINGS"`, `CurrentContainerVersion = 1`. `Wrap(version, scheme, payload)` builds it; `TryParse(content, out header, out payload)` splits on the **first** `\n` (the header terminator we write) and returns false for a legacy headerless file — in which case the whole content is the payload. Tolerates a stray `\r` and extra trailing tab-fields (reserved for forward-compatible header growth). Robust against the two legacy shapes: a single-line Base64 DPAPI blob (no tab → not magic → legacy) and raw indented JSON (first line `{` → legacy).

**KROK 2 — explicit encryption scheme** ([EncryptionSchemes.cs](src/EmberTern.Core/Security/EncryptionSchemes.cs)). String constants `None`/`Dpapi` (append-only — never rename/reuse). `SecretProtector` gained a `Scheme` property (new 3-arg ctor; the old 2-arg ctor defaults to `None`, so existing call sites and the test `FakeProtector` compile unchanged). `Identity` → `None`; `DpapiSecretProtector.Create()` → `Dpapi`. On load, `ApplicationSettingsStore.ResolveProtector(scheme)` maps the header's scheme to a protector — today: the injected protector when the scheme matches, `Identity` for `none` (so a plaintext/dev file is always readable), **null for anything else**. That null is the seam: a future `"aes256-passphrase"` (export) or `"aes256-machinekey"` simply registers another branch. No algorithms implemented — only the dispatch.

**KROK 3 — downgrade protection (3 axes).** `Load` degrades to `null` **and never overwrites** when the on-disk file is from a newer build, recording a human-readable reason in `LastLoadDiagnostic`:
- container version > `CurrentContainerVersion` (newer envelope),
- encryption scheme unknown to `ResolveProtector` (newer algorithm),
- decrypted `SchemaVersion` > `CurrentSchemaVersion` (newer data shape).

Crucially, `null` from `Load` wasn't enough — the section facades do `Load() ?? new ApplicationSettings()` then `Save()`, which would clobber the future file. So `Save` now calls `ExistingFileIsFromFuture()` first and **refuses to write** (records `LastSaveDiagnostic`) when the existing file is detectably newer (same three axes). Corrupt / undecryptable-but-known-scheme files are NOT treated as future — they remain replaceable, matching prior behaviour (never strand the user on a genuinely broken file).

**KROK 4 — migration ladder.** Replaced the single ad-hoc `MigrateTransactionProfiles` with `MigrateToCurrentVersion`: a defensive idempotent shim-consume (`Migrate_1_2`, the v1→v2 transaction-profile split — unchanged behaviour) **plus** a stepwise `while (SchemaVersion < Current) switch(SchemaVersion)` ladder. A future contributor adds `case 2: Migrate_2_3(s); break;` without reading any earlier step; the `default` branch stamps current to avoid an infinite loop on an unknown gap. Files from the future are already rejected in `Load`, so the ladder always runs on `SchemaVersion <= Current`.

**Backward compatibility — full, auto.** A legacy headerless `settings.dat` is read unchanged (TryParse → legacy → decrypt with the injected protector's scheme) and **re-wrapped with the container on the next Save**. Verified end-to-end against the user's real 82 KB DPAPI `settings.dat`: launch → read legacy → graceful close → file now starts `EMBERTERN-SETTINGS\t1\tdpapi` with the DPAPI payload byte-identical (+27 bytes); relaunch → reads the new container, length stable (idempotent, no double-wrap).

**Public API unchanged.** `ConnectionProfileStore` / `FolderStore` / `WorkspaceStore` facades untouched. `ApplicationSettingsStore` only **gained** `LastLoadDiagnostic` / `LastSaveDiagnostic` (public get) — additive. No export/import, no AES (deliberately out of scope — only the foundation).

**Tests** — [SettingsContainerTests.cs](tests/EmberTern.Tests/SettingsContainerTests.cs) (+12): container wrap/parse round-trip, legacy-blob + legacy-JSON detection, extra-trailing-field tolerance; store writes header w/ magic+version+scheme; legacy headerless read → re-wrap on save; future container version / unknown scheme / future data SchemaVersion → `Load` null + diagnostic; `Save` refuses future-container + future-schema files (file byte-unchanged + diagnostic); ladder stamps `CurrentSchemaVersion` on a v1 file. Existing `ApplicationSettingsStoreTests` / `ConnectionProfileStoreTests` on-disk assertions updated from `StartsWith("ENC:")` to header-aware (`StartsWith(Magic)` + `Contains("ENC:")`).

**Gotcha promoted to architecture lore.**

92. **Whole-file `Load` returning null is NOT enough to protect a file from overwrite — the facade write path clobbers it.** `ConnectionProfileStore`/`FolderStore`/`WorkspaceStore` all do `_settings.Load() ?? new ApplicationSettings()` then `Save()`. So a `Load` that degrades a future/unreadable file to null is immediately followed by a `Save` of fresh defaults that *overwrites the very file you were protecting*. Real downgrade protection requires the guard in **`Save`** too: read the existing file, and if it's detectably from a newer build (container version, unknown encryption scheme, or future data SchemaVersion), refuse to write and leave it intact. **Rule**: any "don't overwrite a newer file" requirement must be enforced at the write boundary, not just the read boundary — a read-only guard is defeated by the next read-modify-write cycle.

### Grid layout profiles V1 — column order / width / auto-fit persistence (shipped 2026-06-14)

Lit up the previously-unused `UserSettings.GridProfiles` so a configured grid layout survives restarts. One shared mechanism across all supported grids — zero per-view layout logic. **970 → 980 tests** (+10). Build clean, smoke-verified.

**Scope (8 grids, by priority).** Query results (`QueryResults`), Dane (`TableDetail.Data`), Pola (`TableDetail.Fields`), Indeksy (`TableDetail.Indexes`), Ograniczenia ×4 (`TableDetail.Constraints.{PrimaryKey,ForeignKey,Check,Unique}`). **Deliberately excluded** (V2): `FieldDependencies` (auxiliary 4-col panel, low value) and `NewTable.Fields` (ephemeral tab — vanishes on Compile/Cancel). Mechanism is generic — adding either is one XAML attribute.

**What's remembered per grid:** column **order** (always), column **widths** (only when AutoFit off), and the **AutoFitColumns** flag. Not sorting/filters/grouping/selection (V1 scope).

**Identification.** Opt-in per grid via the attached property `behaviors:GridLayoutBehavior.GridId="..."` in XAML — a registration, not duplicated logic. Columns keyed by their **Header string** (stable `UiStrings` constants for static grids; DB column names for the two dynamic grids). Matches the existing `GridProfile` model 1:1 (`Dictionary<string,double> ColumnWidths`, `List<string> ColumnOrder`, `bool AutoFitColumns`).

**Shared layers (no duplication):**
- **Core** — [GridProfileStore.cs](src/EmberTern.Core/Settings/GridProfileStore.cs): section facade over `ApplicationSettingsStore` (mirrors `WorkspaceStore`/`FolderStore`), read-modify-write on `UserSettings.GridProfiles`. [GridLayoutOrdering.cs](src/EmberTern.Core/Settings/GridLayoutOrdering.cs): pure `OrderedNames(current, saved)` — saved order first (present-only, de-duped), new columns appended, removed columns skipped. Unit-tested.
- **App** — [Behaviors/GridLayoutBehavior.cs](src/EmberTern.App/Behaviors/GridLayoutBehavior.cs): one attached behavior (like `FocusBehavior`). On attach: loads the profile, applies order (`DisplayIndex`) + widths (`DataGridLength`), and **programmatically appends** a checkable "Auto-fit columns" item to the grid's `ContextMenu` (creates one if absent). Saves on `ColumnReordered`, on `DetachedFromVisualTree` (tab close), and via static `FlushAll()` from `MainWindow.OnWindowClosing` (the reliable moment to capture final `ActualWidth`). Re-applies on `Columns.CollectionChanged` (dynamic grids rebuild columns in code-behind) via a coalesced Background-priority post, guarded by `_applying`.

**AutoFit semantics.** Default **true** (= current `ColumnWidth="Auto"` behavior — zero regression; only order is persisted). True → columns reset to `DataGridLength.Auto`, manual widths NOT saved, order saved. False → saved pixel widths applied, manual widths captured from `ActualWidth`, order saved. Toggle in the grid's context menu (no new permanent buttons, per the UX ask). Turning AutoFit off seeds widths from what's on screen so the first manual layout is captured even before a resize.

**Store wiring.** `GridLayoutBehavior.Store` is a static set once by `MainWindow.OnDataContextChanged` from the VM's settings dir + protector (same pattern as `WorkspaceStore` — so tests/headless never touch the real `%AppData%`, gotcha #88). Null Store → behavior no-ops.

**Tests** — [GridProfileStoreTests.cs](tests/EmberTern.Tests/GridProfileStoreTests.cs) (+5: get-missing→null, round-trip across instances, upsert-by-GridId, multiple-profiles-independent, preserves other sections) + [GridLayoutOrderingTests.cs](tests/EmberTern.Tests/GridLayoutOrderingTests.cs) (+5: no-saved-order identity, exact reorder, new-columns-appended, removed-skipped, duplicate-collapse). The behavior glue (order persist+restore through a real attached `DataGrid`) was proven by a headless probe run in isolation, then **removed** to keep the suite deterministic (see gotcha #94); it's covered going forward by manual smoke, consistent with the "behaviors aren't unit-tested" precedent (`FocusBehavior`/`IconBrushConverter`).

**Gotchas — promote to architecture lore.**

94. **Avalonia headless allows ONE `HeadlessUnitTestSession` per test process — a second `StartNew` test class collides with the first.** `ConnectionExpandBindingProbe` already owns the process's headless session; adding a second headless probe class (each calling `HeadlessUnitTestSession.StartNew`) made BOTH fail when the full suite ran, though each passed in isolation. **Rule**: if you need a headless integration probe, add it to the existing headless probe class (share the one session), don't spin up a second session-owning class. For new App-side glue, prefer deterministic Core unit tests + an isolated manual probe run over a permanent second headless session.

95. **`DataGrid.ColumnReordered` + `DataGridColumn.DisplayIndex`/`Width`/`ActualWidth`/`Header` and `MenuItem.ToggleType`/`IsChecked` are all stable in Avalonia 12.0.0 — but `Visual.GetVisualRoot()` resolved awkwardly on `DataGrid` even with `using Avalonia.VisualTree;`; use `Control.IsLoaded` for an "already attached?" check instead.** Set `DisplayIndex` sequentially in increasing target order to reorder columns programmatically. Apply pixel widths as `new DataGridLength(px)` and reset to content sizing with `DataGridLength.Auto`.

### UI ergonomics — theme consolidation + resizable/collapsible layout (shipped 2026-06-14)

Three-part UX pass. **980 → 992 tests** (+12). Build clean, smoke-verified Light + Dark.

**Part 1 — theme/style consolidation** ([Colors.axaml](src/EmberTern.App/Themes/Colors.axaml), [ControlStyles.axaml](src/EmberTern.App/Themes/ControlStyles.axaml)). Audited every view + code-behind for color drift. Findings + fixes: 6 hardcoded literals tokenized — `Button.primary` white text + `Button.caption.close` hover red/white + the title-bar lane-chip white text + the Execute-button "F5" hint `#80FFFFFF` → new tokens `OnAccentColor`/`OnAccentSubtleColor`/`CloseButtonHoverColor` (defined identically in **both** Dark + Light because they sit on theme-independent fills). The `TextBlock.field-label` style was duplicated verbatim in 7 dialogs → centralized in `ControlStyles.axaml`, removed from each. The CLAUDE.md UI section gained two subsections: **Reuse before create** (search for an existing style/component/dialog/DataGrid-behavior/pagination/toolbar/token before making a new one) and the **UI Review Checklist** (8 gates: no hardcoded/local colors, DynamicResource, Light, Dark, reuse, no style/functionality duplication).

**Part 2 — resizable + collapsible sidebar** (VS Code / DataGrip style). The fixed 280px sidebar column + 1px separator became a named-grid column (`Width=280, MinWidth=220, MaxWidth=600`) + a native `GridSplitter` (no custom control). A `☰` titlebar button and a double-click on the splitter both **toggle full collapse**. Collapsed = the column is hard-clamped to **exactly 0px** (`MinWidth=0; MaxWidth=0; Width=0` — see gotcha #96), the panel + splitter hide, and a 12px left-edge **grab rail** (a `Border`, shown only while collapsed) appears flush at x=0; pressing it re-expands to the **last** width (not the default). Width + collapsed state persist in `WorkspaceState.SidebarWidth` / `SidebarCollapsed` (read in `MainWindow.ApplyLayoutFromPendingRestore`, written in `OnWindowClosing` — same pattern as `WindowBounds`).

**Part 3 — SQL editor results panel** (reuse-first). (a) **Resizable height** via a native `GridSplitter` between editor and results; `WorkspaceState.ResultsPanelHeight` persists it; the row collapses to 0 on non-Query tabs. (b) **3-state column sort** (asc → desc → none) on the Results grid: Avalonia's `DataGridColumnEventArgs` can't be cancelled and `DataGridColumnHeader.OwningColumn` is internal, so the grid stays `CanUserSortColumns=False` and a tunneled `PointerPressed` maps the clicked header (by arrow-stripped text) to a column index → `MainWindowViewModel.CycleResultSort`. The VM sorts the materialized result client-side with the new shared `EmberTern.Core.Query.RowIndexComparer` (extracted from `TableDetailTabView`'s private nested copy — both now share it) and paints a ▲/▼ header glyph. (c) **Client-side pagination** mirroring `TableDetailTabViewModel`'s page-state shape (`ResultPage` / `ResultPageSize`=200 / First-Prev-Next-Last + `HasResult*Page` + hint), reusing the same `TableDetailPagination*Icon/Tooltip` strings — no parallel paging system; the SQL result is already materialized (≤5000 rows) so paging slices in memory.

**Gotchas — promote to architecture lore.**

96. **Setting only `Width=0` on a `Grid` `ColumnDefinition` does NOT reliably collapse it to 0 when a `GridSplitter` is adjacent — the column's `MinWidth`/`MaxWidth` still permit a non-zero width and the splitter's layout pass re-reserves the prior pixel width (leaving an empty gap, panel hidden but space reserved).** To force a column to exactly 0px, clamp all three: `MinWidth=0; MaxWidth=0; Width=GridLength(0)`. Restore `MinWidth`/`MaxWidth` on expand. This is correct sizing via the column's own constraints, not a visibility hack.

97. **A thin (≈12px) clickable strip must NOT be a `Button` driven by `Click`.** Two failure modes: (1) `Button.Click` needs press AND release on the same control — a 1-2px pointer drift on a narrow target drops the release outside and cancels the click ("works on the Nth try"); (2) switching that `Button` to a bubbling `PointerPressed` handler fails entirely because `Button` marks `PointerPressed` handled in its own class handler before instance handlers run. Use a `Border` (doesn't swallow pointer input) with a `PointerPressed` handler — fires immediately on press, reliable every time. Reserve `Button`+`Click` for normal-sized targets.

### Connection management + metadata-refresh + context-menu fixes (shipped 2026-06-14 → 06-18)

A run of bugfix/UX milestones on connection editing, metadata refresh, the TableDetail context menus, and transaction handling. **Tests 992 → 1031.** Build clean, smoke-verified each step.

**Connection editing.**
- Transaction-profile pickers (Data + Metadata) moved into the existing **Advanced** `Expander` of [NewConnectionDialog.axaml](src/EmberTern.App/Views/NewConnectionDialog.axaml) (reused the dialog's expander; bindings unchanged, no value/migration change).
- **Editing the active connection now updates the live session.** Root cause: `FirebirdConnectionService._activeProfile` was a captured reference from connect-time; `Upsert` + `ReloadConnections` built a NEW profile object, so the title-bar TX chips and `TransactionService.ResolveActiveProfile()` (read at begin time) kept stale values until reconnect/restart. Fix: `FirebirdConnectionService.UpdateActiveProfile(profile)` swaps `_activeProfile` when the Id matches + raises a NEW `ActiveProfileUpdated` event (distinct from `ActiveConnectionChanged` so it does NOT run the heavy workspace-stash/reload). VM `ApplyEditedProfile` = `Upsert` + `UpdateActiveProfile` + `ReloadConnections`; `OnActiveProfileUpdated` repaints status bar + profile chips. Pure helper `ShouldReplaceActiveProfile(active, incoming)` (Id-match) is unit-tested.

**Metadata refresh corruption (lock leak).** Symptom: after Refresh, category counts vanished, categories wouldn't expand, reconnect didn't help, only restart did. Two causes: (1) `RefreshAsync` reset every group to a placeholder but only reloaded EXPANDED ones — eager-loaded-but-collapsed categories lost their `(N)` counts; now it reloads every previously-LOADED group (`group.IsLoaded || group.IsExpanded`). (2) **`LaneLock()` was evaluated twice** (acquire + release) in `FirebirdMetadataReader` / `FirebirdDiagnostics`; if `MetadataIsIndependent` flips between (e.g. the metadata attachment breaks mid-call) the reader releases a DIFFERENT `SemaphoreSlim` than it acquired, permanently leaking the metadata `_metadataCommandLock`. That semaphore lives on the long-lived connection service → survives reconnect, only a process restart clears it. Fix: capture the lock ONCE (`var commandLock = LaneLock();`) — matches `FirebirdDdlReader`.

**TableDetail context menus.**
- **Indeksy → Recompute statistics / Recompute all statistics.** `DdlGenerator.BuildSetIndexStatistics(name)` → `SET STATISTICS INDEX "ix"` (Firebird has no per-index `ANALYZE`; that's Oracle). "All" iterates the already-loaded `Indexes` and continues past per-index failures, reporting `N of M`.
- **Dane → Set NULL** (cell context menu). Enabled only for nullable, non-PK, non-computed columns (`IsColumnNullable`). Routes through the EXISTING `UpdateCellAsync(row, col, null)` — same change-tracking/UPDATE as a manual edit, no separate save path. The right-clicked cell is resolved via the public `DataGrid.CellPointerPressed` event (args carry `Row` + `Column`) — a grid-level `PointerPressed` + internal `DataGridCell.OwningColumn` reflection did NOT resolve the cell on the editable grid (item stayed greyed); the dedicated event is raised by the cell itself and is reliable.

**Index statistics show `-1`.** Firebird stores `-1` in `RDB$INDICES.RDB$STATISTICS` as the "selectivity not computed" sentinel (freshly created index / empty table), NOT NULL. The reader passed it straight to the grid → "-1.000000" everywhere. Fix: `FirebirdTableDetailReader.NormalizeStatistics` maps any negative selectivity to null → blank cell.

**SET STATISTICS auto-commits (no manual Commit).** Recompute used the metadata working tx (`_ddlExecutor.ExecuteAsync`) → left an active tx the user had to Commit. Fix: `FirebirdConnectionService.ExecuteAdminBatchAsync` runs each statement in its OWN short, auto-committed transaction on a **transient connection** (same profile, `Pooling=false`) — fully independent of the C2 working-tx lanes (a separate attachment, because the managed `FbConnection` allows only one tx at a time). Per-statement result (null = ok / error message) so the batch continues past failures. `FirebirdDdlExecutor.ExecuteAutonomousBatchAsync` is the passthrough; `RecomputeStatisticsForAsync` uses it. Matches IBExpert: completes immediately, nothing pending.

**Post-commit refresh routed by lane.** A data-edit + Commit triggered a full `RefreshStructureAsync` (8 metadata round-trips incl. the heavy dependencies query) on every TableDetail tab → seconds-long freeze, and tearing down the Fields model transiently surfaced "Table has no primary key". A DML data-edit can't change schema, so this was pure waste. Fix: `MainWindowViewModel.DecidePostTransactionRefresh(dataSettled, metadataSettled)` — **metadata** commit/rollback → full `RefreshAfterTransactionAsync` (DDL may have changed schema); **data** commit/rollback → lightweight `RefreshDataAfterTransactionAsync` = `ReloadDataPreviewAsync` (data preview only; `EnsureLoadedAsync` is idempotent so structure/Fields/PK stay intact; essential on rollback to revert optimistic writes). Metadata wins when both coalesce.

**Single unified Commit / Rollback pair.** The 4 buttons (Commit/Rollback × Data/Metadata) confused users — the app already auto-routes the lane, so the user shouldn't choose. Replaced with ONE `CommitAllCommand` / `RollbackAllCommand`: Commit settles every open lane, Rollback reverts every active/error lane (both when both are open). Pure decisions `DecideCommitLanes` / `DecideRollbackLanes` (metadata only counts when independent — degraded mode delegates to data, so acting on it again is a redundant no-op). C2 transaction architecture unchanged — this is a UI/command-layer change only.

**Primary-key detection — authoritative source is the constraint, not the per-field flag.** Reported bug: a table with a real PK showed "only INSERT available" while IBExpert allowed UPDATE. Root cause: `HasPrimaryKey` derived from `FieldInfo.IsPrimaryKey`, set by `FieldsSql`'s `PK_FLAG` correlated subquery whose `s.RDB$FIELD_NAME = rf.RDB$FIELD_NAME` CHAR comparison can return 0 for a table that genuinely has a PK → every field `IsPrimaryKey=false` → empty `PrimaryKeyColumns`. Fix: `RefreshPrimaryKeyColumns` now derives PK from the **PRIMARY KEY entry in the `Constraints` collection** (loaded via `ConstraintsSql` → `RDB$RELATION_CONSTRAINTS` → `RDB$INDEX_SEGMENTS`, no per-field CHAR comparison — the same reliable path that fills the Ograniczenia tab), falling back to the field flag only when constraints aren't loaded yet. Pure helper `PrimaryKeyColumnsFromConstraints`; `RefreshPrimaryKeyColumns` is re-run after the Constraints load step. Repro test pins it (PK constraint present + all field flags false → `HasPrimaryKey` true).

**Transaction-profile model (analysis, no change).** Confirmed the dual Data/Meta profiles + auto-lane-routing (`SqlStatementClassifier`, F5 only) are the intended C2 design and consistent — the app already auto-selects the profile by operation type. Only fixed a stale `Shift+F5` code comment.

**Gotchas — promote to architecture lore.**

98. **`LaneLock()` / any lane-resolving accessor must be captured ONCE per acquire/release pair.** If the lock object is recomputed at release time and the lane (`MetadataIsIndependent`) changed in between, you release a different `SemaphoreSlim` than you acquired — permanently leaking the held one. Because these semaphores live on the long-lived `FirebirdConnectionService`, a leak survives reconnect and only a process restart clears it. `var commandLock = LaneLock();` then use that variable for both `WaitAsync` and `Release`.

99. **Resolve a right-clicked DataGrid cell via the public `DataGrid.CellPointerPressed` event, NOT grid-level `PointerPressed` + `DataGridCell.OwningColumn` reflection.** `DataGridCell.OwningColumn` is internal AND a grid-level pointer handler doesn't reliably resolve the cell on an EDITABLE DataGrid (the cell consumes/owns the gesture differently than on a read-only grid). The dedicated `CellPointerPressed` event is raised by the `DataGridCell` itself with public `Row` + `Column` on the args (`DataGridCellPointerPressedEventArgs`) — reliable, no reflection, fires before the ContextMenu opens.

100. **Firebird `RDB$INDICES.RDB$STATISTICS` uses `-1` (not NULL) as the "no statistics computed" sentinel** (new index / empty table / `SET STATISTICS` on zero rows). Selectivity is otherwise in [0, 1], so map any negative to null/blank rather than rendering a meaningless "-1.000000".

101. **For admin maintenance (e.g. `SET STATISTICS INDEX`) that must auto-commit, use a transient dedicated connection, not the working-tx lanes.** The managed `FbConnection` allows only one transaction at a time, so you can't open a short auto-committed admin tx on a connection that may hold a working tx. Open a transient `FbConnection` from the active profile, run each statement in its own begin→exec→commit, close it. Fully independent of the C2 Data/Metadata working transactions; nothing left pending for the user to Commit (IBExpert behaviour). This is the ONE sanctioned auto-commit path — it's admin maintenance, not user DML/DDL, so it doesn't violate the "no autocommit" rule for the user's work.

102. **A pure-DML (data-lane) commit must NOT trigger a structure refresh.** Data edits can't change the schema (DDL goes through the metadata lane), so re-fetching fields/constraints/indexes/dependencies after a data Commit is pure waste — and tearing down the Fields model transiently flips `HasPrimaryKey` false ("only INSERT available"). Route post-transaction refresh by lane: metadata-settled → full structure reload; data-settled → data-preview reload only (`ReloadDataPreviewAsync`, which keeps Fields/PK intact and reverts optimistic writes on rollback).

103. **Derive the editable-grid primary key from the PRIMARY KEY CONSTRAINT, not from a per-field flag.** `FieldsSql`'s per-field `PK_FLAG` correlated subquery (with an `s.RDB$FIELD_NAME = rf.RDB$FIELD_NAME` CHAR comparison) can miss the PK on some tables, wrongly forcing "only INSERT available". The PRIMARY KEY entry in the constraints load (`RDB$RELATION_CONSTRAINTS` → `RDB$INDEX_SEGMENTS`, no per-field comparison) is authoritative — use it for `PrimaryKeyColumns`, fall back to the field flag only before constraints are loaded.

### Sprint UI Premium — SVG icon system + titlebar polish (shipped 2026-06-18)

Full migration off Unicode/emoji glyphs to a central themeable **SVG icon system** (Lucide source set), plus FluentTheme accent unification and titlebar/active-tab readability polish. The whole app now reads as one consistent product — no mixed glyphs anywhere. **1031 → 1033 tests** (+2 headless probes). Build 0/0, smoke-verified, both themes.

**Icon system architecture** ([Controls/SvgIcon.cs](src/EmberTern.App/Controls/SvgIcon.cs), [Themes/IconGeometries.axaml](src/EmberTern.App/Themes/IconGeometries.axaml), [IconGeometryConverter.cs](src/EmberTern.App/IconGeometryConverter.cs)):
- `SvgIcon` — a `TemplatedControl` with a `Geometry? Data` property; its ControlTheme strokes the path with `Foreground` inside a fixed 24×24 `Viewbox` (uniform scale, consistent stroke). Default 16; per-use overrides (14 tabs, 12 tab-close, 16 caption).
- `IconGeometries.axaml` — every icon as `<StreamGeometry x:Key="Icon.<Name>">` (Lucide path data, 24-viewbox). The matching `.svg` files under `Assets/Icons/<category>/` are **source-of-truth only** (`<AvaloniaResource Remove="Assets\Icons\**\*.svg" />` + `*.md` — repo docs, zero app size; runtime ships only the geometry strings). Catalog: [Assets/Icons/ICONS.md](src/EmberTern.App/Assets/Icons/ICONS.md).
- Two converters, both keyed off VM string properties (VM holds **no** Avalonia types): `IconGeometryConverter` (key → `Geometry`, theme-invariant, single-value) for SHAPE; the existing `IconBrushConverter` (key + `ActualThemeVariant` → `IBrush`) for COLOR. Per-kind metadata color comes from the 13 `IconColor_*` tokens already in both theme dictionaries.
- `MetadataNodeViewModel.GeometryKeyFor(kind) => $"Icon.{kind}"` — 1:1 with `MetadataObjectKind`; a headless probe asserts every kind + chrome key resolves to a real `Geometry` (catches a missing/typo'd key, which renders BLANK at runtime — smoke wouldn't catch it).

**Object/metadata icons (13 kinds + chrome)** — Lucide: Table=`table`, View=`eye`, Procedure=`square-terminal`, Trigger=`zap`, Function=`square-function`, Generator=`hash`(authored), Domain=`diamond`(authored), Package=`package`, Exception=`triangle-alert`, Role=`shield`, User=`user`, Index=`key-round`, SystemTable=`database`; plus Query tab=`file-code`, Connection node=`server` (color = `StatusBrushKey`: green connected / subtle disconnected — replaced the `●/○` glyph), Folder=`folder`. Migrated in EVERY site: metadata tree (group+leaf), workspace tab strip, dependency tree (Used by / Depends on), field-dependencies panel — one source of truth, no second icon set. `IconGeometryKey` added to `MetadataNodeViewModel` / `WorkspaceTabViewModel` / `FieldDependencyItem` / `DependencyGroupNode`+`DependencyLeafNode`; `StatusBrushKey` added to `ConnectionNodeViewModel`.

**Action icons** — Etap-1 toolbar set (Play/Stop/Check/Undo/Plus/Pencil/Trash/ListX/Save/X/Copy/Eraser + window caption + nav chevrons/panel toggles) plus Etap-2 close-out of the Table Detail toolbars: Compile=`hammer` (accent **primary** CTA button, no text label), New Table=`table-plus` (composed), field-edit toggle=`pencil-ruler`, Connect=`plug-zap`, Format SQL=`braces`, field/data Drop=`minus`, Move=`arrow-up`/`arrow-down`, refresh=`refresh-cw`, pagination=`chevron-*`. **No `Content="…Icon"` glyph remains in any view.**

**FluentTheme accent unification** — the amber/gold leakage on CheckBox / RadioButton / ComboBox+ListBox selection / ToggleSwitch was FluentTheme's platform `SystemAccentColor` (orange on Win11), which the per-control overrides (TreeViewItem*/DataGridCell*) didn't catch. Fixed by overriding `SystemAccentColor` + its 6-step ramp to the EmberTern accent blue — see gotcha #104.

**Semantic icon tokens** (Etap 1, both themes, [Colors.axaml](src/EmberTern.App/Themes/Colors.axaml)): `AccentIconBrush` / `SuccessIconBrush` / `DangerIconBrush` / `WarningIconBrush` / `InfoIconBrush` / `NeutralIconBrush` (subtle, no rainbow) + `IconHoverBrush`. `Button.icon`/`ToggleButton.icon`/`Button.flat`/`Button.primary` got rounded hover chip + `:disabled` opacity. **Etap 2 added ZERO new color tokens** (reused the 13 `IconColor_*` + `ConnectedBrush`/`SubtleForegroundBrush` for connection status).

**Titlebar + active-tab readability polish:**
- Active **DB name**: was subtle/grey 12px → now `Bold` 14px full-contrast `ForegroundBrush` + a 2px `ConnectedBrush` underline (Rider/DataGrip-style; vertical StackPanel with a stretched 2px Border so the underline spans exactly the name width). No badge/glow/border/colored text.
- Active **tab**: 2px `AccentBrush` top line (height-reserved 2px Grid row → no reflow active/inactive) + `SemiBold` full-contrast title (selected only; inactive untouched).
- **Data/Meta transaction chips: untouched** (explicit user constraint — operational info from C2 must stay exactly as readable).
- Window **caption icons** (minimize/maximize/restore/close): 10→16px geometry to match the adjacent theme-toggle/toolbar; `Button.caption` 46×36 + hover/close-hover unchanged (click area identical).

**Process note:** this sprint ran across several review rounds. The user is highly design-sensitive ("premium DataGrip/Rider", no rainbow, no AI-looking icons) and reviews each step visually before sign-off. Screenshots of the running app can't be captured from this environment (the IDE window holds foreground), so visual specifics are confirmed by the user; correctness is pinned by build + tests + the two headless probes.

### Gotchas — promote to architecture lore

104. **Override FluentTheme's `SystemAccentColor` at the TOP LEVEL of a merged dictionary (theme-invariant), NOT inside `ThemeDictionaries`.** Theme-scoped placement does NOT resolve where FluentTheme reads the accent — a headless `TryFindResource("SystemAccentColor", variant)` returned `found=False`, and the amber leak persisted. Moved to root-level keys in `Colors.axaml` (our accent is the same blue in both themes anyway) → resolves, and recolors every Fluent control that derives from the accent (CheckBox checked fill, RadioButton, ComboBox/ListBox item selection, ToggleSwitch, Slider) in one place. Provide the full ramp: `SystemAccentColor` + `SystemAccentColorLight1/2/3` + `SystemAccentColorDark1/2/3`. The per-control `TreeViewItem*`/`DataGridCell*` overrides remain for templates that DON'T read SystemAccent. **Rule:** the canonical Avalonia accent override is one top-level `SystemAccentColor` block; never scope it per-variant, and verify with a headless resource-resolution probe (a blank/amber control is the failure mode, not a crash).

105. **Themeable SVG icons: VM holds a string key, the view resolves shape + color via two converters.** The pattern that keeps "zero Avalonia types in the VM" while rendering real vector icons: (1) define geometries as `<StreamGeometry x:Key="Icon.X">` in one central dictionary; (2) the VM exposes a geometry-key string (`IconGeometryKey`, e.g. `$"Icon.{kind}"`) and a color-key string (`IconResourceKey`); (3) the view binds `SvgIcon.Data` through `IconGeometryConverter` (key→Geometry, theme-invariant) and `SvgIcon.Foreground` through a MultiBinding to `IconBrushConverter` (key + `ActualThemeVariant` → brush, re-evals on theme toggle). Keep the `.svg` source files repo-only (`AvaloniaResource Remove`), name geometry keys 1:1 with an enum so a `GeometryKeyFor` is a one-liner, and pin "every key resolves" with a headless probe — a missing key renders an invisible icon, not an error. Reuse-before-create: a new object type that already has an `IconColor_*` token needs zero new color tokens.

### Stabilization Sprint — refresh storm, inline Size, tab history, results maximize, name limits (shipped 2026-06-15)

Five-task hardening pass before further feature work. **1033 → 1044 tests** (+11). Build clean (0/0, TWAE on), smoke-verified.

**1. DB-file picker filter** ([NewConnectionDialog.axaml.cs](src/EmberTern.App/Views/NewConnectionDialog.axaml.cs)) — added `*.ib` (InterBase) to the database file filter (was `*.fdb`/`*.gdb` only) plus explicit upper-case glob variants (`*.FDB`/`*.GDB`/`*.IB`) since Avalonia `FilePickerFileType.Patterns` is case-sensitive on Linux/macOS.

**2. SQL-editor results panel maximize/restore** — `OnResultsSplitterDoubleTapped` ([MainWindow.axaml.cs](src/EmberTern.App/Views/MainWindow.axaml.cs)) now toggles a tri-state: Normal (editor `*` + results at saved height) ⇄ **Results maximized** (editor row collapsed to 0, results row `*`). Restoring returns to the previous (possibly dragged) height. New tab-strip button overlaid top-right of the Results/Messages/Output `TabControl` (`OnToggleResultsMaximizeClick`) does the same — its icon swaps `Icon.ChevronsUp` (maximize) / `Icon.ChevronsDown` (restore) bound to the new `MainWindowViewModel.IsResultsMaximized`. The code-behind owns the `GridLength` sizing (captured `_editorRow` = `WorkspaceGrid.RowDefinitions[0]`); the VM holds only the bound display flag. `ApplyResultsRowForActiveTab` honours the maximized state on tab switch.

**3. Tab activation history** ([MainWindowViewModel](src/EmberTern.App/ViewModels/MainWindowViewModel.cs)) — `SelectTab` records a most-recently-activated-last `List<WorkspaceTabViewModel> _tabActivationHistory` (move-to-end). `CloseTab` prunes the closing tab then returns to `PreviousActiveTab()` (newest still-open entry) instead of the arbitrary index-neighbour — so closing a procedure opened from a table's dependencies lands back on the table. Falls back to the index-neighbour only when history is empty; history cleared in `ClearWorkspaceTabs`.

**4. CRITICAL — refresh storm.** Four confirmed defects fixed (root-cause report below):
- **Event-subscription leak** ([FieldRowViewModel](src/EmberTern.App/ViewModels/FieldRowViewModel.cs)): each row VM subscribed to `owner.PropertyChanged` + `owner.AvailableDomains.CollectionChanged` and never unsubscribed. New `Detach()` unhooks both; `RebuildEditableFields()` detaches outgoing rows before clearing.
- **O(N²) EditableFields rebuild** ([TableDetailTabViewModel](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs)): `OnFieldsCollectionChanged` rebuilt the whole collection on EVERY `Fields` mutation (Clear + N×Add = N+1 rebuilds per load). New `_bulkFieldsLoading` guard + `ReplaceFields()` → exactly one rebuild per load.
- **Unnecessary data-preview reload on metadata-only change**: `LoadAsync` split into `LoadStructureCoreAsync` (fields/constraints/indexes/deps/DDL/description/domains) + `LoadDataPreviewCoreAsync` (`SELECT *`). `RefreshStructureAsync` now reloads the data preview ONLY when the column SET changed (add/drop/rename) — a type/length/precision/NOT-NULL/default/constraint/index edit keeps the same columns and SKIPS the `SELECT *`. This is the storm fix: on a table with a `MOD`/computed column (or an ORDER BY over a non-indexed column), the `SELECT *` evaluated the function per row — re-running it on every commit was the "thousands of Function MOD" trace.
- **Duplicate refresh after commit**: `RefreshStructureAsync` coalesces concurrent calls via `_refreshInFlight` (Compile's refresh racing the post-commit refresh now join one task).
- **Diagnostics**: new [RefreshTrace](src/EmberTern.App/Diagnostics/RefreshTrace.cs) (gated by `EMBERTERN_REFRESH_DIAG`, writes to the shared `%TEMP%\EmberTern-debug.log`) instruments Commit/Rollback, the post-transaction refresh decision, RefreshStructure (begin / coalesced / column-set-changed-or-skip / end), LoadStructure, LoadDataPreview, and RefreshTree.

   **Inline Size/Scale editing** (the grid/dialog consistency ask): `FieldRowViewModel.Size`/`Scale` are now editable, **parsed from the user-facing `Type` string** (`VARCHAR(50)`→50, `NUMERIC(15,2)`→15,2) — NOT from `FieldInfo.Size`, which is the raw byte length (`RDB$FIELD_LENGTH`) and would generate wrong DDL. New `EffectiveTypeText` reassembles the full type through the SAME `DdlGenerator.FormatTypeOrDomain` pipeline the Edit-Field dialog uses, so the grid and dialog emit identical `ALTER COLUMN … TYPE …`. `IsModified` and `EnqueueRowEdits` compare `EffectiveTypeText` (catches size/precision edits the old base-only comparison missed). Pola grid Size/Scale columns are now editable (gated by the existing edit-mode toggle).

**5. Connection-name limits** — `UiStrings.ConnectionNameMaxLength = 60` (holds `ENV - Client - Database` names without dominating the titlebar). Enforced via `TextBox.MaxLength` in [NewConnectionDialog.axaml](src/EmberTern.App/Views/NewConnectionDialog.axaml) + a truncate-on-build backstop in `NewConnectionDialogViewModel.TryBuildProfile` (covers paste/restore/import). Titlebar active-connection `TextBlock` and sidebar tree `DisplayName` got `MaxWidth`/`TextTrimming=CharacterEllipsis` + full-name `ToolTip` so a long name can no longer push the connection toolbar / window buttons off-screen.

**Refresh-storm root-cause report — every refresh path, before vs. after.**

| Trigger | BEFORE | AFTER |
|---|---|---|
| Edit a column (inline or dialog) → Compile/Execute the ALTER | `RefreshStructureAsync` → full `LoadAsync` incl. `SELECT *` data preview | `RefreshStructureAsync` (coalesced) → structure-only reload; data preview reloaded ONLY if column set changed (a type/size edit keeps the column → **no `SELECT *`**) |
| …then user Commits (metadata lane) | `OnTransactionStateChanged` → `RefreshAfterTransactionAsync` → ANOTHER full `LoadAsync` (incl. `SELECT *`) for EVERY open TableDetail tab | same hook, but each refresh is structure-only + data-skip-when-unchanged, and coalesces with any in-flight Compile refresh |
| Each `Fields` repopulation during a load | `OnFieldsCollectionChanged` fired N+1×, rebuilding the ENTIRE `EditableFields` each time → O(N²) row-VM allocs, every one leaking an owner-event subscription | `ReplaceFields` bulk-updates under `_bulkFieldsLoading`; ONE `RebuildEditableFields` that Detaches outgoing rows first |
| Data-lane commit (DML) | already data-only (`RefreshDataAfterTransactionAsync`) — unchanged | unchanged |

The `MOD` itself originates in the user's schema (a computed column / CHECK / trigger using `MOD`) and was evaluated by the `SELECT *` data preview; the storm was EmberTern re-issuing that `SELECT *` on every structure refresh × every open tab, amplified by the leaking O(N²) row rebuild. Eliminating the redundant `SELECT *` (column-set-unchanged ⇒ skip) + the leak + the duplicate refresh removes the cascade. Performance impact: a column type/size commit on a wide table with computed columns goes from N full loads (each a `SELECT *` full-table scan) to N coalesced structure-only reloads (six light `RDB$` catalog queries, no data scan); row-VM churn per load drops from O(N²)+leak to O(N) with clean detach. If the later Firebird trace shows a residual loop, the `EMBERTERN_REFRESH_DIAG` log now pins the exact path.

**Files changed (14).** Core: none. Firebird: none. App — [NewConnectionDialog.axaml](src/EmberTern.App/Views/NewConnectionDialog.axaml)(.cs), [MainWindow.axaml](src/EmberTern.App/Views/MainWindow.axaml)(.cs), [TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml), [TableDetailTabViewModel.cs](src/EmberTern.App/ViewModels/TableDetailTabViewModel.cs), [FieldRowViewModel.cs](src/EmberTern.App/ViewModels/FieldRowViewModel.cs), [MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs), [MetadataExplorerViewModel.cs](src/EmberTern.App/ViewModels/MetadataExplorerViewModel.cs), [NewConnectionDialogViewModel.cs](src/EmberTern.App/ViewModels/NewConnectionDialogViewModel.cs), [UiStrings.cs](src/EmberTern.App/UiStrings.cs), [Themes/IconGeometries.axaml](src/EmberTern.App/Themes/IconGeometries.axaml), new [Diagnostics/RefreshTrace.cs](src/EmberTern.App/Diagnostics/RefreshTrace.cs). Tests: new [SprintStabilizationTests.cs](tests/EmberTern.Tests/SprintStabilizationTests.cs) (11).

**Gotchas — promote to architecture lore.**

106. **`FieldInfo.Size` is the raw byte length (`RDB$FIELD_LENGTH`), not the user-facing length/precision.** For `NUMERIC(15,2)` it's the storage width (8), not the precision (15); for a multi-byte-charset `VARCHAR(80)` it's the byte count. Never feed it into DDL as the size/precision (the Edit-Field dialog's `Precision = f.Size` seeding is a latent bug). Parse the displayed `Type` string (`VARCHAR(50)` / `NUMERIC(15,2)`) for the user-facing args, and reassemble via the shared `DdlGenerator.FormatTypeOrDomain` so grid and dialog emit identical DDL.

107. **A refresh after a metadata change should reload the data preview ONLY when the column SET changed.** A `SELECT *` data-preview reload is the single most expensive step in a TableDetail refresh (full-table computed-column evaluation / sort). A type/length/NOT-NULL/default/constraint/index edit keeps the same columns — the existing `DataResult` is still valid, so skip the reload. Compare the pre/post column-name list; reload only on add/drop/rename. This is what kills the "thousands of `Function MOD`" post-commit storm.

108. **Any VM that subscribes to a longer-lived owner's events MUST expose a `Detach()` and be detached before it's discarded — especially when a collection of such VMs is rebuilt on every load.** `EditableFields` (one `FieldRowViewModel` per column) is torn down and rebuilt on every structure load; each row hooked `owner.PropertyChanged` + `owner.AvailableDomains.CollectionChanged`. Without `Detach()`, every reload left a generation of dead row VMs still wired to the owner, accumulating across refreshes (and firing on every subsequent `AvailableDomains` mutation). Rule: rebuild helpers detach outgoing items before `Clear()`.

### Stabilization Sprint — follow-up (trace-confirmed root cause + 2 fixes, shipped 2026-06-15)

The user supplied the Firebird trace. Analysis settled the refresh-storm question definitively and produced two more fixes + a file-filter correction. **1044 → 1045 tests.** Build clean, smoke OK.

**Trace verdict — the "storm" is the user's OWN `ON TRANSACTION_COMMIT` audit trigger, NOT an EmberTern metadata-reload loop.** The 480 KB / 579 `BIN_AND`+`MOD` `EXECUTE_FUNCTION` calls all run inside the database trigger `XXX_WS_TRANS_ON_COMMIT` → procedure `GET_NAGL_WERDYSP`, which fires once per *committed transaction* on that DB. The trace shows **zero** EmberTern `RDB$` metadata-reload statements and only **2** EmberTern transactions — so the prior refresh-loop fixes hold; there is no metadata reload loop. EmberTern's only lever is committing fewer transactions.

**Fix A — post-commit refresh is the one extra transaction EmberTern can drop.** A field-edit-and-commit committed TWO transactions: `TRA_95390` (the ALTER working tx) AND `TRA_95413` (the post-commit `RefreshAfterTransactionAsync` reads). On this DB *every* commit re-fires the audit trigger, so the post-commit refresh's commit is the heavy `GET_NAGL_WERDYSP` fire. Fix ([MainWindowViewModel](src/EmberTern.App/ViewModels/MainWindowViewModel.cs)): **a COMMIT never triggers a post-settle refresh** — the UI already shows the committed state (the structure editor calls `RefreshStructureAsync` when it *applies* the ALTER, before the user commits; data edits paint optimistically). `DecidePostTransactionRefresh(dataSettled, metadataSettled, wasRollback)` returns `None` on commit; **ROLLBACK** still refreshes (Structure for metadata, DataOnly for data) to revert the in-memory/optimistic state. A `_lastTransactionSettleWasRollback` flag set in `CommitLaneAsync`/`RollbackLaneAsync` drives it. This is global across all editors — the post-commit refresh was the ONLY auto-refresh-on-transaction path (DDL tabs for views/procs/triggers/etc. never auto-refreshed on commit; the metadata tree refreshes only on explicit New/Delete/Recompute). Trade-off: a raw-SQL DDL/DML committed via the SQL editor won't auto-refresh an open table tab (it never refreshed the tree either) — manual refresh; acceptable vs. the audit-trigger cost.

**Fix B — file filter missed `.fb`.** The real DB is `SZKOLENIE.FB` (`.fb`, not `.fdb`); the earlier `.fdb`/`.gdb`/`.ib` filter hid it. Patterns now `*.fdb *.fb *.gdb *.ib` (+ upper-case for Linux/macOS) ([NewConnectionDialog.axaml.cs](src/EmberTern.App/Views/NewConnectionDialog.axaml.cs)).

**Fix C — brown "modified row" tint.** The Pola "pending edit" row used full `WarningBrush` (brown/gold) and `OnFieldRowPropertyChanged` was a no-op so it could go stale. Now: a dedicated subtle `RowModifiedBrush` token (~20% accent overlay, both themes), a `.pending:selected` variant so selection colour wins on a selected modified row, and `OnFieldRowPropertyChanged` walks the realized `DataGridRow` containers to re-apply/clear the class **live** the moment `IsModified` flips (revert or post-Compile rebuild) — not only on the next `LoadingRow`.

**Tests** — `PostCommitRefreshTests` split into commit-never-refreshes + rollback-routes-by-lane (new 3-arg signature).

**Gotcha — promote to architecture lore.**

109. **On a database with an `ON TRANSACTION_COMMIT` (or START/ROLLBACK) trigger, every transaction a tool commits re-fires that trigger — so "huge activity after commit" can be the user's own DB infrastructure, not the tool.** Before chasing a refresh loop, separate the tool's attachment from others in the trace (the `EXE:pid` line) and COUNT the tool's distinct transactions + statements. Here: 2 EmberTern transactions, 0 RDB$ reloads, 579 functions all inside the user's `XXX_WS_TRANS_ON_COMMIT` audit trigger. The only tool-side lever is committing fewer transactions — hence "a COMMIT never refreshes" (the UI is already current; only ROLLBACK must re-read). Verify with `EMBERTERN_REFRESH_DIAG=1`: a clean commit now logs `Commit (no post-commit refresh)` and NO `RefreshStructure begin`.

**Verification vs. IBExpert (trace-compared, sprint complete).** The user captured the same field-edit-then-commit in IBExpert. IBExpert's post-edit refresh runs ~8 metadata queries (RDB$RELATION_FIELDS+RDB$FIELDS, RDB$RELATIONS format, RDB$DEPENDENCIES, fields again, PK/FK/UNIQUE constraints) EACH in its OWN short `CONSISTENCY|WAIT|READ_WRITE` transaction → `XXX_WS_TRANS_ON_COMMIT` fires ~8×, **0 ms every time** because the gate `RDB$GET_CONTEXT('USER_TRANSACTION','TABLE_ID_NAGL_ORDERSTATUS'/'…ORDERPAYMENT')` is unset (those context vars are set only by the ERP's NAGL business operations, never by a metadata edit). The trigger is cheap unless that context is present. **EmberTern now runs the equivalent metadata reads INSIDE the working tx at apply-time (every structural edit calls `RefreshStructureAsync` — 12 call sites) and commits ONE transaction → the trigger fires once.** So EmberTern fires the on-commit trigger FEWER times than IBExpert (1 vs ~8) and, like IBExpert, never with the context set during a metadata edit — it cannot hit the expensive path more often than IBExpert. The earlier 579-function storm ran entirely on the post-commit refresh transaction (`TRA_95413`); removing that transaction (commit ⇒ no refresh) removes the storm regardless of which gated object produced the functions. Final checks PASS: (1) no secondary transaction after a user commit (post-commit code is UI-only); (2) one commit ⇒ one trigger fire; (3) no stale metadata — all 12 structural-edit paths refresh at apply-time within the working tx, so the tab reflects the change before the user commits; (4) behaviour comparable-to/leaner-than IBExpert. One dual-lane nuance: if BOTH a data and a metadata working tx are open, "Commit All" commits 2 → 2 fires; a pure metadata edit leaves the data lane idle, so the typical field-edit commits exactly 1.

### System Tables — read-only TableDetail via capability-based factory (shipped 2026-06-15)

System tables now open in the full **TableDetail** workbench (Pola / Ograniczenia / Indeksy / Zależności / Dane / Opis / DDL) instead of a plain DDL-only tab — but fully **read-only**. Read-only is NOT a flag; it falls out of the existing capability model (service presence stays the single source of truth — no `IsReadOnly`). **1045 → 1061 tests** (+16). Build clean, all green.

**Routing + single factory** ([MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs)). The hard-coded `obj.Kind == Table` branch in `OnOpenDdlRequested` became `OpensAsTableDetail(kind) => kind is Table or SystemTable` — table-shaped kinds only; Views/Procedures/Triggers/Functions/Packages stay DDL tabs (or get their own detail view later). The single construction point `internal TableDetailTabViewModel CreateTableDetail(MetadataObject obj)` makes the ONE capability decision keyed on kind: a writable `Table` gets the data editor + DDL executor; a `SystemTable` gets **neither**, so the existing gates (`CanEditData` / `CanAddField` / `CanManageConstraints` / `CanManageIndexes` / `CanCompile` / `CanEditDescription` / `CanAddRow`, all derived from those services) turn every edit affordance off. **Both** the direct-open path (`OnOpenDdlRequested`) and the workspace-restore path (`LoadWorkspaceFor`) now go through this factory, so a restored system-table tab can't silently regain edit capability — parity is structural, not enforced by duplicate code. Event wiring (`OpenObjectRequested` / `ConfirmationRequested`) lives in the factory; both call sites stopped wiring it manually. `ObjectKind` round-trips through persistence (`SnapshotCurrentTabs`) and dedup keys on `(Kind, Name)`, so `SystemTable` needs no special-casing there.

**Editing affordances HIDDEN, not greyed** (the explicit UX call — a system table is a different object category, not a permission-denied normal table). Two new capability-reading bridge props on `MainWindowViewModel`: `ShowFieldEditTools => IsFieldsTabActive && (ActiveTableDetail?.CanAddField ?? false)` and `ShowDataEditTools => IsDataTabActive && (ActiveTableDetail?.CanEditData ?? false)`, wired into the `_selectedWorkspaceTab` notify chain + re-fired from `OnTableDetailPropertyChanged` on inner sub-tab change. [MainWindow.axaml](src/EmberTern.App/Views/MainWindow.axaml): the Pola edit cluster (⚡ Compile / edit-toggle / ＋ / − / ↑ / ↓ + separator) re-bound to `ShowFieldEditTools`; Dane Add/Delete-row (+ separator) to `ShowDataEditTools`; **Refresh + pagination stay on `IsDataTabActive`** (read paths, must remain). [TableDetailTabView.axaml](src/EmberTern.App/Views/TableDetailTabView.axaml): the fields, 4 constraint sub-grid, index, and Set-NULL context menus + the Opis Save/Clear controls gate on the matching capability (`CanAddField` / `CanManageConstraints` / `CanManageIndexes` / `CanEditData` / `CanEditDescription`) — item-level `IsVisible` guarantees the hide (codebase precedent), plus a `ContextMenu`-level `IsVisible` as best-effort empty-popup suppression. **No read-only hint/message added.**

**Reuse only — zero new readers/services/viewmodels/views.** `FirebirdTableDetailReader` / `FirebirdDdlReader` / `FirebirdMetadataReader` / `TableDetailTabViewModel` / `EmberTern.Core` all unchanged — system tables already query the same `RDB$*` catalogs (`FieldsSql` etc. take `@tableName`; `"RDB$RELATIONS"` is a valid quoted identifier), and `FirebirdDdlReader` already reconstructs `SystemTable` DDL via `BuildTableDdlAsync`.

**Tests** ([SystemTableReadOnlyTests.cs](tests/EmberTern.Tests/SystemTableReadOnlyTests.cs), +16): `OpensAsTableDetail` predicate over 10 kinds; factory → SystemTable read-only / Table editable; direct-open of a system table → a TableDetail tab (not DDL) with `ObjectKind=SystemTable`, read-only, de-dups on second open; workspace-restore of a persisted SystemTable tab → identical read-only capabilities, restored Table stays editable.

**Gotcha — promote to architecture lore.**

110. **Read-only object categories should reuse a writable detail view via capability omission, not a new flag or a parallel view.** A system table is the same data shape as a user table (same `RDB$*` catalogs, same TableDetail sub-tabs) differing only in *capability*. The clean model: one factory makes the single per-kind decision (writable → wire the data editor + DDL executor; read-only → omit both), and the pre-existing `CanEdit*` gates — already `=> _service is not null` — do the rest. This is strictly more expressive than an `IsReadOnly` boolean (data-editing and structure-editing are independent axes) and avoids a second source of truth. Route both the open path and the restore path through that one factory so they can't diverge. Hide edit affordances (don't grey them) by gating visibility on the same capability properties.

**Follow-up — Dane grid truly read-only (shipped 2026-06-15).** After the initial milestone, Boolean cells could still be toggled and the BLOB button still opened editable on a system table. Root cause: `DataGrid.IsReadOnly` (correctly bound to `IsDataReadOnly`) gates only the grid's *standard* edit flow — it does NOT reach interactive controls placed inside a `CellTemplate`, and the Boolean column (gotcha #56) deliberately puts a live `CheckBox` in the always-visible `CellTemplate` with a `Click` handler. Fix (capability-driven, still no new flag — [TableDetailTabView.axaml.cs](src/EmberTern.App/Views/TableDetailTabView.axaml.cs)): `BuildBooleanCellTemplate` branches on `_currentVm.CanEditData` — `false` renders a read-only `✓`/blank glyph (same approach as the Pola/Indeksy/Ograniczenia grids via `BoolToCheckmarkConverter`), `true` keeps the interactive `CheckBox`; BLOB viewing is preserved but never editable via new `internal static ResolveBlobReadOnly(cellValue, canEditData) => cellValue is byte[] || !canEditData` (`OpenBlobEditorAsync` opens `BlobEditorWindow` read-only on system tables, the existing `if (readOnly) return;` blocks write-back). Text/Date already covered by `DataGrid.IsReadOnly`. **1061 → 1069 tests** (+8: read-only cell-write no-op, `ResolveBlobReadOnly` text/binary matrix).

111. **`DataGrid.IsReadOnly` does NOT disable controls living inside a `CellTemplate`.** It governs only the grid's own edit-mode machinery (double-click / F2 → swap to `CellEditingTemplate`), which covers plain text/date columns. Any *always-visible interactive control* injected into a `CellTemplate` (a `CheckBox` for booleans, a `…` Button for BLOBs — the gotcha #56 pattern) stays live regardless of `IsReadOnly`, so it must be gated on the capability itself (`CanEditData`) at template-build time — render a non-interactive read-only display (e.g. the `✓` glyph) when editing is disabled. **Rule:** when a column hosts a live control in its `CellTemplate`, gating the grid's `IsReadOnly` is not enough — branch the template on the edit capability.

### View Detail V1 — dedicated View surface (shipped 2026-06-18)

Views now open in a dedicated **View Detail** experience instead of a plain DDL tab: six tabs — **SQL** (editable `CREATE OR ALTER VIEW` source + Compile) · **Fields** (read-only) · **Dependencies** · **Data** (read-only paged preview) · **Description** (read-only) · **DDL** (read-only). **1069 → 1093 tests** (+24). Build clean, smoke-verified.

**Architecture decision — a focused `ViewDetailTabViewModel`, NOT `TableDetail` reuse, NOT a generic `SourceObject` abstraction.** A view is a different object category from a table (editable SELECT source; no constraints/indexes/inline data editing), so the System-Tables capability-omission reuse does NOT extend here — that worked because a system table *is* a table. Reuse happens at the **reader / static-helper level**, not via inheritance: the same `FirebirdTableDetailReader.GetFieldsAsync` / `GetDependenciesAsync` / `GetDataPreviewAsync` / `GetRowCountAsync` / `GetDescriptionAsync` (all relation-generic — `RDB$RELATIONS`/`RDB$RELATION_FIELDS`/`RDB$DEPENDENCIES` include views), `FirebirdDdlReader.FetchDdlAsync`, `TableDetailTabViewModel.BuildDependencyTree` + `MapObjectTypeToKind` (both `internal static`, called directly), the dependency-tree templates, the paged data-preview pattern, and the dynamic-column `PopulateDataGrid` shape. The routing/factory stayed explicit (`OpensAsTableDetail`/`OpensAsViewDetail` + `CreateTableDetail`/`CreateViewDetail`) — a registry/`IDetailTabViewModel` would collapse ~10 lines while hiding each factory's divergent capability decision; evaluated and rejected per "no abstraction on speculation".

**Editable source (the one new reader capability).** [FirebirdDdlReader.FetchViewSourceAsync](src/EmberTern.Firebird/FirebirdDdlReader.cs) returns the source rebuilt as `CREATE OR ALTER VIEW` (so re-Compile alters in place); `BuildViewDdlAsync` gained an `orAlter` bool so the editable-source path and the read-only `FetchDdlAsync` DDL path (`CREATE VIEW`) share one code path. Same lane/lock + tx-attach as every other read.

**Compile / Save.** [ViewDetailTabViewModel](src/EmberTern.App/ViewModels/ViewDetailTabViewModel.cs) `CompileCommand` runs the SQL-tab source through `FirebirdDdlExecutor.ExecuteAsync` in the user's working (metadata) transaction — Rollback undoes, Commit persists, consistent with all other DDL. On success an existing view **fully refreshes itself** (`RefreshAsync` discards the cached `_loadTask` and re-reads source/fields/dependencies/data/DDL/description — requirement #2). The Compile button lives in the main toolbar gated on `IsViewDetailTabActive`; `ShowTransactionButtons` + `IsClosableTabActive` extended for `ViewDetail`.

**New View.** Toolbar button next to New Table (`Icon.View` glyph, `IconColor_View`, gated `CanCreateView = IsConnected`) opens a View Detail tab in `IsNew` mode seeded with a simple `CREATE VIEW` template — no visual designer, the user edits SQL directly. Compile in new mode raises `ViewCreated(parsedName?)`; the owner (`OnViewCreated`) refreshes the metadata tree, closes the New View tab, and reopens the real view when the name parses (`TryParseViewName`, pure/tested, falls back to tree-only when the shape doesn't match).

**Native persistence** (no DDL-only fallback — requirement #1). New `WorkspaceTabKind.ViewDetail` in both the VM and the Core persistence enum; `SnapshotCurrentTabs` persists real views as `ViewDetail` (skips transient `IsNew` tabs), `LoadWorkspaceFor` restores them via `CreateViewDetail` (lazy-load on first activation, same anti-race pattern as TableDetail). Dedup keys on `(Kind, Name)` across Ddl/TableDetail/ViewDetail.

**Self-contained view** ([ViewDetailTabView.axaml](src/EmberTern.App/Views/ViewDetailTabView.axaml)(.cs)) hosts its own editable SQL `TextEditor` + read-only DDL `TextEditor` + read-only data `DataGrid` + dual dependency `TreeView`s — MainWindow only adds the sibling + visibility binding. Pagination is self-contained in the Data tab (no inner-sub-tab tracking pushed into `MainWindowViewModel`).

**Out of scope (held):** version history, version comparison, TODO integration, plan analyzer, permissions tab, CSV/Excel/TXT export, source control, history-tracking objects — none added; no IBExpert-style helper objects installed in user databases.

**Tests** ([ViewDetailTests.cs](tests/EmberTern.Tests/ViewDetailTests.cs), +24): `OpensAsViewDetail` predicate; factory wires Compile but no data editor; direct-open → ViewDetail tab (not DDL) + dedup; native ViewDetail restore (not DDL) + cached DDL seed; Capture persists as ViewDetail; no-executor → can't compile / Compile no-op; empty-source no-op; IsNew LoadAsync no-op; `TryParseViewName` Theory.

**Gotcha — promote to architecture lore.**

112. **A view is a different object category from a table — reuse readers + static helpers, not the table detail VM.** The System-Tables milestone reused `TableDetailTabViewModel` because a system table *is* a table (identical shape, read-only). A view differs structurally (editable SELECT source; no constraints/indexes/inline data editing; a different tab set), so it gets its own focused `ViewDetailTabViewModel`. The reusable layer is the **relation-generic Firebird readers** (they query `RDB$RELATIONS`/`RDB$RELATION_FIELDS`/`RDB$DEPENDENCIES`, which include views) plus the **`internal static` helpers** (`BuildDependencyTree`, `MapObjectTypeToKind`) called cross-VM. Resist a shared base class / generic `SourceObject` abstraction until a third concrete consumer (procedure/trigger source editor) actually exists. An editable AvaloniaEdit hosted inside a UserControl follows the main `SqlEditor` pattern — push VM→editor under a `_suppress*` flag, write editor→VM on `TextChanged`; never two-way-bind `TextEditor.Text`.

**Follow-up fixes (shipped 2026-06-18).** Three live-testing issues, all reuse-only: **(1) SQL formatting** — `ViewDetailTabViewModel.FormatSqlCommand` reuses the shared `EmberTern.Core.Sql.SqlFormatter` with the same selection-or-all callback shape (`SelectedTextProvider`/`ReplaceSelectedOrAllText`) as the SQL Editor, wired in the view code-behind; Alt+F (editor `KeyDown`) + a `Icon.Braces` toolbar button gated on `IsViewDetailTabActive`. No View-specific formatter. **(2) Editable description** — Firebird supports `COMMENT ON VIEW` (no limitation), so the Description tab is now editable via the same workflow as table descriptions: `EditableDescription` + Save/Clear commands → `DdlGenerator.BuildCommentView` (new; `BuildCommentTable`/`BuildCommentView` now share a private `BuildRelationComment("TABLE"|"VIEW", …)` helper) executed in the working metadata transaction → `RefreshAsync`; Commit/Rollback finalize. **(3) New View button stuck disabled** — root cause: the connection-change notify block in `ApplyActiveConnectionChange` re-notified only `NewTableCommand`, never `NewViewCommand`, so the command kept its construction-time `CanExecute=false`; fix adds `OnPropertyChanged(nameof(CanCreateView)) + NewViewCommand.NotifyCanExecuteChanged()` alongside the table pair. **1093 → 1106 tests** (+13: format wholesale/selection/empty, `CanEditDescription` gating + `Description`→`EditableDescription` mirror, `BuildCommentView` cases + escaping + empty-name throw + `BuildCommentTable` unchanged, `NewViewCommand` re-notifies on connection change).

**Final polish (shipped 2026-06-18).** Two items, reuse-only. **(1) View DDL formatting quality** — the shared `SqlFormatter` (NOT a view-specific formatter) gained two rules: `OR ALTER` no longer breaks as a boolean `OR` (the `create\n  or alter` bug — guarded in `MatchStructuralPhrase`), and a `CREATE [OR ALTER] VIEW <name> [(cols)] AS` header rule (`TryEmitViewHeader`) that emits `view name (` + each column on its own 4-space-indented line + `)` glued to the last column + `as` on its own line, IBExpert-style. Column-alias `AS` and boolean `OR` are unaffected; idempotent. **(2) New View icon** — new composed `Icon.ViewPlus` (eye base + the same bottom-right `+` overlay strokes as `Icon.TablePlus`) so New View reads as a create action consistent with New Table. **1106 → 1112 tests** (+6 formatter: `OR ALTER` one-line, single/multi-column view header, no-column-list AS-on-own-line, idempotency, boolean-OR-still-breaks regression guard).

Before/after for `CREATE OR ALTER VIEW XXX_DOMEK_VIEW (ID_DOMEK, KONTRAHENT, NAZWA, AKTYWNY) AS SELECT … FROM XXX_DOMEK X` — before: `create\n  or alter view xxx_domek_view(id_domek, kontrahent, nazwa, aktywny) as\nselect …`; after: `create or alter view xxx_domek_view (\n    id_domek,\n    kontrahent,\n    nazwa,\n    aktywny)\nas\nselect …\nfrom xxx_domek x`.

### Procedure Detail V1 — dedicated 4-tab surface + Input/Output params (shipped 2026-06-18)

Stored procedures open in a dedicated **Procedure Detail** experience: four top tabs — **Editor** (editable `CREATE OR ALTER PROCEDURE` source + Compile) · **Description** (editable `COMMENT ON PROCEDURE`) · **Dependencies** · **DDL** (read-only) — with a **resizable bottom panel under the Editor source** holding read-only **[Input] [Output]** parameter grids (IBExpert-style; future-extensible to Variables/Cursors/Subprograms as more bottom sub-tabs). **1112 → 1151 tests** (+39). Build clean (0/0), 8s smoke OK.

**Architecture decision — a focused `ProcedureDetailTabViewModel`, NOT a ViewDetail/TableDetail reuse, NOT a generic abstraction.** A procedure is **not a relation**: it lives in `RDB$PROCEDURES` (not `RDB$RELATIONS`) and its dependency rows are `RDB$*_TYPE = 5` (Procedure), not `0` (Relation). View Detail could reuse `FirebirdTableDetailReader` for description/dependencies/fields/data *because a view IS a relation* — that reuse does **not** carry over to procedures (gotcha #113). So Procedure Detail gets its own VM (4 tabs, no Fields/Data — structurally *simpler* than View) and its own **procedure-scoped catalog reads**, while reusing the editable-source/compile pattern, the shared `SqlFormatter`, the description-editing workflow, the dependency tree builder + templates, and the routing/factory/persistence/toolbar wiring.

**Reuse (directly):** editable-source Compile in the working (metadata) tx via `FirebirdDdlExecutor`; `EmberTern.Core.Sql.SqlFormatter` (Alt+F + toolbar, same `SelectedTextProvider`/`ReplaceSelectedOrAllText` callback shape); read-only DDL via `FirebirdDdlReader.FetchDdlAsync` (already routes `Procedure` → `BuildProcedureDdlAsync`); `TableDetailTabViewModel.BuildDependencyTree` + `DependencyInfo` + `MapObjectTypeToKind` + the dual dependency-tree templates + `OpenObjectRequested`; `DdlGenerator.BuildRelationComment("PROCEDURE", …)` (the helper is generic — `COMMENT ON PROCEDURE` is valid FB); the AvaloniaEdit ↔ VM `_suppress*` sync + theme/selection swap; `GridLayoutBehavior` on the param grids; the tab factory / icon / persistence patterns.

**New, procedure-specific:**
- [FirebirdDdlReader.FetchProcedureSourceAsync](src/EmberTern.Firebird/FirebirdDdlReader.cs) — public, returns the editable `CREATE OR ALTER PROCEDURE …` via the existing `BuildProcedureDdlAsync` (procedures always rebuild as CREATE OR ALTER, so source == DDL in V1; the DDL tab is kept anyway for consistency with Table/View — per the user's explicit call).
- [FirebirdTableDetailReader](src/EmberTern.Firebird/FirebirdTableDetailReader.cs): `GetProcedureDescriptionAsync` (`RDB$PROCEDURES.RDB$DESCRIPTION`), `GetProcedureParametersAsync(name, paramType)` (structured `ProcedureParameterInfo` rows — `RDB$PROCEDURE_PARAMETERS ⋈ RDB$FIELDS`, reusing `FormatFieldType`/`StripDefaultPrefix`; 0=input, 1=output), `GetProcedureDependenciesAsync` (`ProcedureDependsOnSql`/`ProcedureDependedOnBySql` — `RDB$DEPENDENCIES` filtered to `RDB$*_TYPE = 5`, projected through `MapObjectType`). All `internal const` SQL pinned by tests; all use the metadata-lane access (`MetaConnection`/`MetaLock`/`MetaTx`).
- [ProcedureParameterInfo](src/EmberTern.Core/Metadata/ProcedureParameterInfo.cs) (Core) + `DdlGenerator.BuildCommentProcedure`.
- [ProcedureDetailTabViewModel](src/EmberTern.App/ViewModels/ProcedureDetailTabViewModel.cs) + [ProcedureDetailTabView](src/EmberTern.App/Views/ProcedureDetailTabView.axaml)(.cs): `IsNew`, `EnsureLoadedAsync`/`RefreshAsync` (Compile fully refreshes an existing procedure), `TryParseProcedureName`, `ProcedureCreated` event; `InputTabHeader`/`OutputTabHeader` (`Input (n)` / `Output (n)`, tracked off the collections); the bottom param panel is a `GridSplitter`-resized `TabControl`.
- [MainWindowViewModel](src/EmberTern.App/ViewModels/MainWindowViewModel.cs): `OpensAsProcedureDetail`, `CreateProcedureDetail`, the `OnOpenDdlRequested` branch + dedup-kind, `SelectTab` lazy-load branch, `Snapshot`/`LoadWorkspaceFor` branches (native `CoreTabKind.ProcedureDetail` persistence — restores the full surface, not DDL-only; transient `IsNew` tabs skipped), `NewProcedure`/`OnProcedureCreated`, `IsProcedureDetailTabActive`/`ActiveProcedureDetail` + notify chain, `ShowTransactionButtons`/`IsClosableTabActive` extension, `CanCreateProcedure` re-notify on connect.
- [MainWindow.axaml](src/EmberTern.App/Views/MainWindow.axaml): **New Procedure** toolbar button (`Icon.ProcedurePlus`, composed from the terminal-square + the shared TablePlus/ViewPlus `+` overlay) next to New Table/New View; Compile (`Icon.Hammer`, primary) + Format (`Icon.Braces`) buttons gated on `IsProcedureDetailTabActive`; sibling `<views:ProcedureDetailTabView>`.

**Out of scope (held, per spec):** Variables / Cursors / Subprograms (require PSQL **source parsing** — not in the catalog; recommended future home is a sidebar/secondary panel inside the Editor or more bottom sub-tabs, two-phase: catalog params already done, source-scanner later); procedure **execution** (future: reuse the Data-tab grid + `FirebirdQueryExecutor`, a catalog-param dialog + per-proc history in `UserSettings`, **Data lane** via `SqlStatementClassifier` which already routes `EXECUTE PROCEDURE`/`SELECT` to Data); debugger; plan analyzer; version history; permissions; compare.

**Gotcha — promote to architecture lore.**

113. **A stored procedure is NOT a relation — the View/Table detail readers do not apply to it.** View Detail reused `FirebirdTableDetailReader.GetDescriptionAsync` / `GetDependenciesAsync` because a view lives in `RDB$RELATIONS` and its dependency rows are `RDB$*_TYPE = 0` (Relation) — identical to a table. A procedure lives in `RDB$PROCEDURES` and its dependency rows are type **5** (Procedure): `GetDescriptionAsync` (reads `RDB$RELATIONS`) returns nothing, and `DependsOnSql`/`DependedOnBySql` (hard-filtered to type 0 + table-specific FK/field branches) return nothing. Procedure metadata therefore needs **procedure-scoped catalog queries** (`RDB$PROCEDURES` for description, `RDB$DEPENDENCIES WHERE RDB$DEPENDENT_TYPE/RDB$DEPENDED_ON_TYPE = 5`, `RDB$PROCEDURE_PARAMETERS` for params). Reuse stays at the *tree-builder / type-mapping / FormatFieldType* level, not the query level. **Rule:** before reusing a relation-shaped reader for a new object kind, confirm the object actually lives in `RDB$RELATIONS` and check its dependency type code — if not (procedures=5, functions=15, triggers=2, generators=14, exceptions=7…), it needs its own catalog SQL.

### Procedure Detail V1.1 — Source/Easy modes, editable params, body scanner, execute, comment (shipped 2026-06-18)

Closes the V1 workflow gap vs. IBExpert. The Procedure Detail tab grew tabs **Editor · Description · Dependencies · DDL · Result**, and the Editor tab gained a **Source ⇄ Easy mode** toggle. **1151 → 1186 tests** (+35). Build clean (0/0), 8s smoke OK.

**Key enabler:** `RDB$PROCEDURE_SOURCE` is the **body alone** (the text after `AS` — DECLARE…BEGIN…END, no header). So Easy mode needs **zero PSQL parsing to LOAD**: params from `RDB$PROCEDURE_PARAMETERS`, body from a new [`FirebirdDdlReader.FetchProcedureBodyAsync`](src/EmberTern.Firebird/FirebirdDdlReader.cs). Parsing is needed only for the **Source→Easy round-trip** (decision A), and that's a bounded header parser, not a full PSQL grammar.

**1. Source ⇄ Easy mode (canonical model {name, inputs, outputs, body}).** Source mode = the full editable `CREATE OR ALTER PROCEDURE` text (V1 behaviour). Easy mode = metadata panels **above** a body-only editor (per the user's IBExpert-style layout; grids are NOT below the editor). Switching to Easy parses `SourceText` via [`ProcedureSignatureParser`](src/EmberTern.Core/Sql/ProcedureSignatureParser.cs) → params + body; on parse failure it **keeps the last-good model** and shows a non-blocking notice (`ProcedureParseFailedNotice`). Switching to Source regenerates the text via [`DdlGenerator.BuildCreateOrAlterProcedure`](src/EmberTern.Core/Metadata/DdlGenerator.cs) (deterministic inverse). Compile reassembles the active mode and runs in the working (metadata) tx. New Procedure stays Source-only (`CanUseEasyMode = !IsNew` — no catalog to seed Easy).

**2 & 3. Editable Input / Output parameter grids** (Add / Delete / Move Up / Move Down) — the [`NewTableTabViewModel`](src/EmberTern.App/ViewModels/NewTableTabViewModel.cs) grid pattern via new [`ProcedureParamRowViewModel`](src/EmberTern.App/ViewModels/ProcedureParamRowViewModel.cs) (Name uppercased; `TypeText` free text so every FB param type form round-trips). Firebird has no incremental `ALTER PROCEDURE ADD PARAMETER`, so param edits are inseparable from the canonical model — Compile rebuilds the whole signature + body.

**4 & 5 & cursors. Read-only Variables / Cursors / Subprograms** — one [`ProcedureBodyScanner`](src/EmberTern.Core/Sql/ProcedureBodyScanner.cs) over the body lists top-level `DECLARE [VARIABLE]`, `DECLARE … CURSOR`, and FB3 `DECLARE PROCEDURE|FUNCTION` (subprogram bodies skipped via BEGIN/END nesting). Cursors fell out of the same scanner at low cost (per the user's conditional approval). Rescans live as the body editor changes. Displayed in sub-tabs alongside the param grids; editing stays in the body editor / Source mode.

**6. Execute Procedure (Data lane, parameterized).** Toolbar ▶ → [`ExecuteProcedureDialog`](src/EmberTern.App/Views/ExecuteProcedureDialog.axaml) collects input values (per-row NULL + type-aware conversion, [`ExecuteProcedureDialogViewModel.ConvertByType`](src/EmberTern.App/ViewModels/ExecuteProcedureDialogViewModel.cs)) → bound (never literal-embedded) via a new [`FirebirdQueryExecutor.ExecuteAsync(sql, IReadOnlyList<QueryParameter>, ct)`](src/EmberTern.Firebird/FirebirdQueryExecutor.cs) overload + [`QueryParameter`](src/EmberTern.Core/Query/QueryParameter.cs). Selectable (`SELECT * FROM P(…)`) when the proc has outputs + a `SUSPEND` in the body, else `EXECUTE PROCEDURE P(…)`. Results show in a **self-contained Result tab** (dynamic-column grid), not the global Results panel. Data lane via the owner's `_executor` (the classifier already routes EXECUTE/SELECT to Data); auto-begins the data working tx, Commit/Rollback as usual. No debugger / history / execute-all (out of scope).

**7. Comment / Uncomment** — toolbar buttons → VM commands raise events; the view performs the line op (`-- ` prefix / strip) on the **active** editor (Source or body) via the AvaloniaEdit `Document` API. Pure editor operation.

**Toolbar (gated on `IsProcedureDetailTabActive`):** mode toggle (`Icon.PencilRuler`, ToggleButton) · ⚡ Compile · ▶ Execute · {} Format · Comment · Uncomment. Shared scan primitives live in [`SqlScanHelpers`](src/EmberTern.Core/Sql/SqlScanHelpers.cs) (trivia/quote/identifier/paren scanning), used by both procedure scanners.

**Gotchas — promote to architecture lore.**

114. **`RDB$PROCEDURE_SOURCE` is the procedure BODY only (no header).** It's exactly the text after `AS` — DECLARE section + BEGIN…END. So a structured "easy" editor for a procedure needs **no parsing to load**: take params from `RDB$PROCEDURE_PARAMETERS` and the body verbatim from `RDB$PROCEDURE_SOURCE`. Parsing is required ONLY to turn a user-edited *full* `CREATE OR ALTER PROCEDURE` text back into parts (the Source→Easy direction), and that's a bounded header parser (name + two param lists + locate the body-`AS`), never a full PSQL grammar. The reverse (parts→text) is a deterministic generator. Keep the round-trip's risky direction (text→parts) non-destructive: on parse failure, keep the last-good model and warn — don't discard the user's edits.

115. **Firebird has no incremental parameter ALTER — editing a procedure's params means recompiling the whole `CREATE OR ALTER PROCEDURE`.** There is no `ALTER PROCEDURE ADD/DROP PARAMETER`. So an "edit params in a grid" feature can't be a standalone operation; it must feed a canonical model {name, inputs, outputs, body} that Compile reassembles into the full statement (signature + body) and runs. Param grids, the body editor, and Compile are one unit.

116. **`DdlGenerator` now has TWO quoting conventions — pick deliberately.** The long-standing `Quote` ALWAYS quotes (`"T"`), which table/constraint DDL + tests rely on. The procedure source generator uses a new `QuoteLight` (quote only when needed) so a reassembled procedure reads like the catalog DDL (unquoted `SHOUTY_CASE`, matching `FirebirdDdlReader.Quote`) instead of `"ALL" "QUOTED"`. When adding a generator whose output the user reads as editable source, prefer light quoting; when it's a one-shot DDL statement, the always-quote `Quote` is fine. (The body scanner's subprogram skipping uses BEGIN/END nesting, which a bare `CASE…END` inside a subprogram can miscount — documented as a read-only-listing limitation, not a data risk.)

### Procedure Detail V1.2 — UX/UI polish (shipped 2026-06-18)

Seven live-testing corrections; no architecture change. **1186 → 1193 tests** (+7). Build clean (0/0), 8s smoke OK.

1. **Comment / Uncomment → block `/* */`** (was line `--`). [`ProcedureDetailTabView`](src/EmberTern.App/Views/ProcedureDetailTabView.axaml.cs) `BlockComment` wraps the selection in `/*\n…\n*/` and unwraps a selection whose trimmed content is already a `/* … */` block; acts on the active editor (Source or body). Needs a selection.
2. **Metadata grids usability** — all five (Input/Output/Variables/Cursors/Subprograms): row height 22 → **30** (Not-Null checkboxes now fit; matches the editable New Table grid's pitch), `ColumnWidth="Auto"` autosize (dropped the star-width Type/Detail columns — gotcha #80), `CanUserResizeColumns` + `CanUserReorderColumns` = true. Param grids keep `CanUserSortColumns="False"` (row order = signature order).
3. **Toolbar visual hierarchy** — Execute icon **green** (`SuccessIconBrush`), Comment **blue** (`InfoIconBrush`), Uncomment **red** (`DangerIconBrush`). Identifiable without the tooltip.
4. **Result paging** — the Result grid gained First/Prev/Next/Last + page hint, **client-side** over the materialized result (`ExecResultPageSize=200`, mirrors the SQL editor's paging shape). A procedure may have side effects, so paging **never re-executes** it — it slices the already-fetched rows; no server round-trip per page. Reuses the Table/View Data chevron icons + `TableDetailPagination*Tooltip` + `ResultsPaginationHintFormat`.
5. **Persistent execution-info panel** — a bottom bar inside Procedure Detail (NOT in the Result tab), visible after any Execute regardless of active tab: `Executed in N ms · M row(s) returned/affected` / `… completed` / error (red). So `EXECUTE PROCEDURE` (no result set) still gives feedback. The Result tab is only auto-selected when there are rows.
6. **Formatter (item 6 — analysis-first, no formatter-logic change).** **Root cause:** the shared `SqlFormatter` is a single-statement SELECT/DML formatter — it breaks a newline before clause keywords (SELECT/FROM/WHERE/JOIN/AND/OR) and has **zero awareness of `;` statement boundaries or `BEGIN/END` blocks**, so on PSQL it collapses multiple statements onto one line and destroys block structure (the observed regression). **Correction shipped (no rule tweak):** Format is **removed from the Procedure surface** (toolbar button + Alt+F gone); `SqlFormatter` is untouched and still used by the SQL Editor / View source where it's appropriate. **Proposed future path:** a dedicated PSQL-aware formatter (statement/block-structured) — its own milestone; not built here.
7. **Easy Mode persistence** — new global `WorkspaceState.ProcedureEasyMode` (like `QueryPanelVisible`). `MainWindowViewModel.ProcedureEasyModePreference` is applied to each newly opened existing procedure (New stays Source) and updated when the user toggles, persisted via Capture/Restore — so the last-used mode follows the user across procedures, new procedures, and restarts.

**Note (gotcha #114 leverage):** because `RDB$PROCEDURE_SOURCE` is the body alone, applying the Easy-mode preference at tab-creation is safe — `OnEasyModeChanged` guards on an empty (not-yet-loaded) `SourceText` (no parse, no spurious notice); the lazy `LoadAsync` then populates the structured model.

### Procedure Detail V1.3 — corrections + editor parity (6 of 7 shipped; item 4 analyzed only — 2026-06-18)

Six approved items implemented; the 7th (unify all DDL to auto-commit) is **architecture-analyzed and HELD for approval** (no code). **1193 → 1214 tests** (+21). Build clean (0/0), 8s smoke OK.

1. **Comment Body / Uncomment Body** (replaces the V1.2 selection comment). New pure `ProcedureBodyScanner.FindOuterBodyContent` / `CommentBody` / `UncommentBody` find the outermost `BEGIN…END` (BEGIN/END nesting, string/comment aware) and wrap/unwrap it in `/* */`. Idempotent (already-wrapped → no-op), inner comments untouched, works in both Source and Easy modes (the helper finds the body's BEGIN…END in either). VM commands `CommentBody`/`UncommentBody` raise events; the view applies the transform to the active editor.
2. **SQL-Editor parity in the procedure editors** — new shared [`SqlEditorBehavior.Attach(editor, mainVm)`](src/EmberTern.App/Completion/SqlEditorBehavior.cs) wires the **existing** `SqlCompletionController` (autocomplete + dot-completion) + double-click + **Ctrl+Click** open-object navigation, delegating to the VM's existing `EnumerateLoadedObjects`/`ResolveDotTable`/`TryGetCachedColumns`/`EnsureColumnsAsync`/`TryOpenDdlForWord`. No second implementation. Attached to `ProcSqlEditor` + `ProcBodyEditor` on `OnAttachedToVisualTree` (when the owning `MainWindowViewModel` is reachable). (Ctrl+Click is new shared infra — the SQL Editor only had double-click.)
3. **Typed Execute-Procedure dialog** — `ExecuteProcedureParamRowViewModel` gained an `ExecuteParamKind` (Text/Numeric/Date/Time/Timestamp/Boolean/BlobText/BlobBinary, classified from the type text — BLOB text/binary split reads the `SUB_TYPE n` already in the type string, **no reader change**) + typed value holders. The dialog renders per-kind controls (TextBox / multi-line TextBox / `NumericUpDown` / `CalendarDatePicker` / `TimePicker` / `CheckBox`) via one row template with per-kind `IsVisible`. `Resolve()` returns the typed CLR value (bound, never a literal). `ConvertByType` kept for the text path + tests.
4. **(HELD — analysis only)** unify "all DDL/metadata auto-commits, transactions only for data". Analysis recorded below; **not implemented** pending approval of the migration plan.
5 & 6. **Subprograms / Cursors split views** — `ProcedureLocal` gained `Source` (the scanner now captures each declaration's full text); the Cursors + Subprograms tabs are now IBExpert-style **list (left) + read-only source editor (right)** (`SelectedCursor`/`SelectedSubprogram` → the view pushes `.Source` into a read-only AvaloniaEdit). Variables stays a grid.
7. **Insert helpers** — Insert Variable / Cursor / Subprogram buttons (in the Variables/Cursors/Subprograms panels) drop an FB-valid template at the caret in the active editor (`InsertSnippetRequested` callback → `Document.Insert`). Editor stays the source of truth.

### Procedure Detail V1.4 — Phase A: metadata DDL auto-commit (shipped 2026-06-18)

First step of the approved unified model (DDL auto-commits, transactions only for data). **Phase A only** — the metadata working-tx infrastructure + Commit/Rollback UI stay in place for now (pending live validation on the real DB); they simply won't activate from a Compile/Apply.

- New [`FirebirdConnectionService.ExecuteAutonomousAsync(statements, ct)`](src/EmberTern.Firebird/FirebirdConnectionService.cs) runs the WHOLE apply in ONE autonomous transaction on a transient connection (begin → all statements → commit; rollback on any failure) — so a multi-statement apply (e.g. ADD FIELD + CREATE GENERATOR + CREATE TRIGGER) stays **atomic** and auto-commits, independent of the working-tx lanes.
- [`FirebirdDdlExecutor.ExecuteAsync`](src/EmberTern.Firebird/FirebirdDdlExecutor.cs) now routes through it (was: auto-begin + attach the metadata working tx). One change → every Compile/Apply call site auto-commits: TableDetail structure edits (Pola/Ograniczenia/Indeksy), Opis/View/Procedure descriptions, New Table CREATE, View/Procedure Compile. The `transactionService` ctor param is retained (call-site stability, possible Phase B/D revisit) but unused.
- Metadata `_metadataExecutor` (SQL-Editor F5 DDL) is **unchanged** (still the working tx) — that's Phase B.
- Tests unchanged (1214 green — the change is runtime DB behavior, only verifiable live). **Validate on the real DB** (table create, field mods, indexes, FKs, views, procedures) that nothing needs cross-apply atomic grouping before Phases B–D remove the old model.

### PSQL-aware shared formatter (shipped 2026-06-18)

Item 1 of the V1.4 feedback: **one** `SqlFormatter`, now with a PSQL mode — Format is re-enabled everywhere (SQL Editor, View Detail, Procedure Detail; ready for future Trigger/Function/Package). **1214 → 1235 tests** (+21). Build clean, smoke OK.

- **Dispatch:** `Format()` calls `IsPsql(tokens)` (a top-level `BEGIN`, or a leading `CREATE/RECREATE/ALTER {PROCEDURE|TRIGGER|FUNCTION|PACKAGE}` / `EXECUTE BLOCK`) → `FormatPsql`; else the existing DML clause formatter (unchanged).
- **PSQL mode reuses the DML `Emit`** for every leaf statement (identical spacing / lowercasing / `:var` gluing / SELECT clause breaks) and adds only block structure via a **recursive unit emitter** (`EmitPsqlUnit`): `BEGIN` pushes indent, `END` pops; `IF…THEN`/`ELSE`/`WHILE…DO`/`FOR…DO` headers + block-or-single branches; `DECLARE` section one-per-line; local `DECLARE PROCEDURE/FUNCTION … BEGIN…END` recurses. A `CREATE … AS` header is kept **verbatim** (already well-formed from `DdlGenerator`); only the body after the top-level `AS` is structured.
- **CASE…END is safe (the headline requirement):** a statement is collected up to its top-level `;`, and a `CASE…END` has no `;`, so it's consumed WHOLE inside the statement and handed to `Emit` as inline text — the BEGIN/END block loop **never sees a CASE's END**. (gotcha #117)
- **`SELECT … INTO :vars`** puts the INTO clause on its own line (only when the statement starts with SELECT and the INTO is top-level → `INSERT INTO` unaffected).
- **Comments keep position + can't comment out code:** `Emit` now forces a newline after a line comment (`-- …`) so trailing tokens on the source line aren't swallowed; block comments stay inline. PSQL emits standalone comments on their own line at the current indent.
- **Idempotent:** indentation derived purely from BEGIN/END/IF/WHILE/FOR structure, statement breaks purely from `;` — never from existing whitespace. Pinned by `Format(Format(x)) == Format(x)` over IF/ELSE, WHILE, CASE-in-block, SELECT-INTO, EXECUTE STATEMENT, local-subprogram shapes.
- **PSQL control keywords** (`IF/WHILE/FOR/DO/SUSPEND/INTO/…`) added to the keyword set so they're not glued to a following `(` like a function call (`if (…)` keeps its space). DML mode is unaffected (these were already lowercased; the keyword flag only governs paren-gluing, which never fires for these in plain SQL).
- Tests: [PsqlFormatterTests.cs](tests/EmberTern.Tests/PsqlFormatterTests.cs) (21) — all your required cases (IF/ELSE, WHILE, FOR SELECT, EXECUTE STATEMENT, local procedures, nested BEGIN/END, CASE…END, comments, full-source header preservation, DML-mode-unaffected, idempotency theory).

**Gotcha — promote to architecture lore.**

117. **In PSQL, distinguish `CASE…END` from `BEGIN…END` by statement scope, not by counting `END`.** A naive BEGIN/END counter is corrupted by `CASE…END` (and `END IF`-less Firebird control flow). The robust rule: a statement runs to its top-level `;`; a `CASE…END` expression has no `;`, so it is contained entirely within one statement. Format by recursing on *units* (statement | BEGIN-block | IF/WHILE/FOR), where a block only consumes `END`s seen at unit-start position — a CASE's `END` is mid-statement and never reaches the block loop. This also makes the formatter idempotent (structure comes from BEGIN/END/`;`, never from whitespace) and is the same insight behind the body scanner's subprogram skipping.

**Item-4 architecture analysis (the held migration plan).** *Current:* all DDL → `FirebirdDdlExecutor.ExecuteAsync` → the **metadata working tx** (manual Commit). *Consumers:* TableDetail structure edits (Pola/Ograniczenia/Indeksy — rely on Rollback-to-undo), Opis/View/Procedure descriptions, New Table CREATE, View/Procedure Compile, SQL-Editor DDL (F5 → metadata lane). *Desired:* every DDL auto-commits; transactions only for data. *What breaks:* Rollback-of-structural-edit + atomic multi-statement structural edits (⚠️ ADD FIELD-with-generator emits CREATE GENERATOR + CREATE TRIGGER + ALTER — must stay one autonomous tx, not per-statement) + the metadata Commit/Rollback UI. *Can the metadata tx be removed?* The **working transaction yes** (DDL → autonomous auto-commit on a transient connection — the proven `ExecuteAdminBatchAsync` path; reads use the implicit per-command read tx); the metadata **connection stays** (C2 read isolation from the data working tx). *Migration (phased, no screen breakage):* **A** `FirebirdDdlExecutor.ExecuteAsync` → one autonomous tx per apply (atomic multi-statement); **B** SQL-Editor metadata DDL → same; **C** remove metadata Commit/Rollback buttons + bar, narrow `CommitAll`/`RollbackAll` + `ShowTransactionButtons` to data; **D** shrink the metadata `TransactionService` to connection+lock only. Data lane (Dane edit, Procedure Execute, SQL DML/SELECT) unchanged. Risk: medium; each phase independently shippable.

### V1.4 follow-up — formatter blank lines + Execute-dialog polish (partial, uncommitted, 2026-06-16)

Two of the eight pre-freeze follow-up items done; the rest (structured Easy mode) still pending. **1235 → 1239 tests** (+4). Build clean (0/0), 8s smoke OK.

- **Item 1 — PSQL formatter preserves logical blank lines.** `FormatPsql` now carries a parallel `List<bool> blank` flag (true when ≥2 source newlines precede a significant token); `EmitPsqlUnit`/`EmitPsqlBranch` call `MaybeBlankLine` before each unit + comment. Author blank lines separating declaration groups / loops / IF / SUSPEND survive; runs of 2+ blanks collapse to ONE; never a leading blank, never doubled — so idempotency holds (`Format(Format(x)) == Format(x)`). Pinned by `PreservesLogicalBlankLines` + `CollapsesMultipleBlankLinesToOne` (23 PSQL tests total).
- **Item 6 — Execute Procedure dialog polish.** `SizeToContent="Height"` + `MaxHeight=700` (auto-sizes to the param list); `TimePicker UseSeconds="True"` (TIMESTAMP/TIME to the second); per-kind defaults seeded in the row ctor (DATE/TIMESTAMP=now, TIME=now, NUMERIC=0, BOOLEAN=false, text=empty); **NULL checkbox checked by default for every param** (IBExpert-style); **in-memory per-procedure param history** (`ExecuteProcedureHistory`, process-lifetime, no persistence) restored on open / saved on Accept via `ApplyHistoryValue`. Dialog VM ctor now takes the procedure name; the view passes `_currentVm.ProcedureName`. Pinned by `ExecuteDialog_DefaultsAndNullChecked` + `ExecuteDialog_History_RoundTripsDuringSession` (two existing `Resolve_*` tests updated to set `IsNull = false` now that NULL defaults to checked).
- **Item 8 — metadata auto-commit: Phase A only, unchanged.** No B–D; awaiting live-DB validation.
- Items 2/3/4/5/7 (structured Easy mode) — see the dedicated milestone below (now done).

### Structured Easy Mode — Variables/Cursors/Subprograms as model elements (shipped 2026-06-16, uncommitted)

Items 2/3/4/5/7 implemented as one unit. Easy mode is no longer a text-derived projection: the procedure body is a canonical model `{inputs, outputs, variables, cursors, subprograms, executableBody}`; the DECLARE section is regenerated from the model; the body editor holds ONLY the executable `BEGIN…END`. **1239 → 1253 tests** (+14). Build clean (0/0), 8s smoke OK.

**Core (round-trip engine, fully unit-pinned).** New [ProcedureBodyModel.cs](src/EmberTern.Core/Sql/ProcedureBodyModel.cs): `ProcedureVariable {Name, TypeText, NotNull, Default}` (TypeText free-text → every Firebird form round-trips verbatim — VARCHAR(n), NUMERIC(p,s), domain, `TYPE OF COLUMN`, `CHARACTER SET`/`COLLATE` all live inline, no information loss), `ProcedureCursor {Name, Declaration}` + `ProcedureSubprogram {Name, Kind, Declaration}` (full `DECLARE …;` text, verbatim), and `ProcedureBodyModel {Variables, Cursors, Subprograms, ExecutableBody}`. `ProcedureBodySplitter.Split(body)` walks the top-level DECLARE section (reusing `SqlScanHelpers` + `ProcedureSignatureParser.ParseSegment` for variables) and keeps the `BEGIN…END` as `ExecutableBody`; `DdlGenerator.BuildProcedureBody(model)` is the deterministic inverse (variables → canonical `DECLARE VARIABLE`; cursors/subprograms verbatim; then the executable body). `ParseCursorName` / `ParseSubprogram` derive a row's display name/kind from its edited declaration. Round-trip is idempotent (`Split∘Build∘Split == Split`) — pinned by [ProcedureBodyModelTests.cs](tests/EmberTern.Tests/ProcedureBodyModelTests.cs) (10).

**App (editable model elements).** New [ProcedureLocalRowViewModels.cs](src/EmberTern.App/ViewModels/ProcedureLocalRowViewModels.cs): `ProcedureVariableRowViewModel` reuses the field infrastructure — same `DomainSpec` list + "(none)" sentinel as the New Table / Pola grids (no second type system), TypeText canonical; `ProcedureCursorRowViewModel` / `ProcedureSubprogramRowViewModel` hold an editable `Declaration` with the name/kind auto-derived on edit. [ProcedureDetailTabViewModel](src/EmberTern.App/ViewModels/ProcedureDetailTabViewModel.cs) now owns `ObservableCollection`s of these row VMs with Add/Delete/Move + selection commands (generic `DeleteRow`/`CanUp`/`CanDown`/`MoveRow<T>` helpers), `ExecutableBody` (the Easy body editor) replacing the old full-body `BodyText`, `SyncEasyModelFromBody` (Source→Easy split) + `BuildBodyModel`/`BuildFullSource` (Easy→Source regenerate). **All text-insertion-at-caret (`Insert*` snippet commands) removed** — locals are added as model rows, not typed into the editor. `MainWindowViewModel.CreateProcedureDetail` best-effort-loads domains into the grid's combo.

**View** ([ProcedureDetailTabView](src/EmberTern.App/Views/ProcedureDetailTabView.axaml)). Variables → editable `DataGrid` (Name / Type / Domain combo / Not Null / Default) + Add/Delete/Move toolbar. Cursors + Subprograms → IBExpert split: list (left) + **editable** SQL editor (right) + Add/Delete/Move toolbar. The cursor/subprogram editors are full editors — syntax highlighting + autocomplete + Ctrl+Click + double-click navigation via `SqlEditorBehavior.Attach`, plus Alt+F format (in-place for the local editors, via the VM command for body/source). A `_focusedEditor` tracks which editable editor the toolbar Format/Comment act on.

**Round-trip tests** ([ProcedureEasyModeRoundTripTests.cs](tests/EmberTern.Tests/ProcedureEasyModeRoundTripTests.cs), 4): Source→Easy populates the structured model; Source→Easy→Source→Easy preserves every part; Easy→Source→Easy preserves every part (incl. derived cursor/subprogram names); Compile-from-Easy reassembles from the model. A full-model snapshot (params + variables + cursors + subprograms + executable body) is compared for exact equality across the round-trip.

**Limitation (documented, by design).** Variable Size/Scale/Sub Type/Charset/Collate/`TYPE OF` are authored **inline in the free-text Type column** (the natural Firebird declaration form), not as separate structured cells — this is deliberate: free-text TypeText is the canonical value, guaranteeing zero information loss on round-trip (a fixed Size/Scale/charset column set cannot represent `TYPE OF COLUMN`/`COLLATE` and would silently drop them). The Domain combo reuses the field-definition domain infrastructure. Type case may normalise (semantics preserved; idempotent). *(Superseded by the round-2 milestone below — full structured columns now ship.)*

### Structured Easy Mode round 2 — full field grids, cursor/subprogram workflow, highlighting (shipped 2026-06-16, uncommitted)

Second feedback pass (10 items). **1253 → 1267 tests** (+14). Build clean (0/0), 8s smoke OK.

- **#1 Full field-definition grids + dropdowns.** New shared [`ProcedureFieldRowBase`](src/EmberTern.App/ViewModels/ProcedureFieldRowBase.cs) backs Input / Output param + Variable rows with the **full 12-column field model** — Name / **Type (dropdown)** / TYPE OF / **Domain (dropdown)** / Size / Scale / Sub Type / Charset / Not Null / Collate / Default / Description — reusing `FieldDefinition` + `DdlGenerator.FormatTypeOrDomain` + `DomainSpec` (no second type system). Columns built once in the view code-behind (`BuildFieldColumns`), shared by all three grids; Type/Domain are real `ComboBox` cells. **Round-trip safety**: structured editors *compose* the canonical `TypeText`, which stays exactly as loaded until a structured field is edited — so `TYPE OF COLUMN`, `CHARACTER SET`, `COLLATE`, and any exotic form survive load→save with zero loss (parsed for display, preserved verbatim).
- **#2 Compile lock RCA + diagnostics (no architecture change, per instruction).** RCA: a procedure Execute (`SELECT * FROM proc(…)`) runs on the **Data lane** and auto-begins the data working tx, which holds the *selectable procedure* "in use" until it ends (no autocommit, by design). Firebird's metadata-update in-use check is **cross-attachment** — the autonomous metadata Compile (separate attachment, `Pooling=false`) still fails with "object … is in use" while ANY transaction holds the procedure. The unified Rollback disposes the data tx; if Compile still fails after rollback, another transaction holds it. Added env-gated (`EMBERTERN_TX_DIAG`) MON$TRANSACTIONS ⋈ MON$ATTACHMENTS dump in [`FirebirdConnectionService.ExecuteAutonomousAsync`](src/EmberTern.Firebird/FirebirdConnectionService.cs)'s in-use catch so the exact holding attachment+transaction is captured live for the next validation pass.
- **#3 Cursors.** Name editable in the left list + **Scroll checkbox**; editing name/Scroll rewrites the declaration header (`ProcedureBodySplitter.RewriteCursorHeader` / `CursorIsScroll`) keeping the SELECT body; Scroll survives Easy→Source→Easy.
- **#4 Subprograms.** Name editable in the left list; **Add asks Procedure / Function** ([`SubprogramKindDialog`](src/EmberTern.App/Views/SubprogramKindDialog.axaml)) seeding the matching template; kind survives round-trip (`RewriteSubprogramName`).
- **#5 Order preservation.** Add / Move Up / Move Down / Delete regenerate the DECLARE section in the same order — pinned by round-trip tests across variables/cursors/subprograms ([ProcedureEasyModeStructuredTests.cs](tests/EmberTern.Tests/ProcedureEasyModeStructuredTests.cs)).
- **#6 BEGIN/END highlighting.** `END` was in the XSHD `Function` block (gold), `BEGIN` in `StatementKeyword` (purple). Moved `END` to the statement-keyword class in both [FirebirdSql.xshd](src/EmberTern.App/Assets/FirebirdSql.xshd) + [.Light.xshd](src/EmberTern.App/Assets/FirebirdSql.Light.xshd) → both render the same purple.
- **#7 Occurrence highlighting.** New [`OccurrenceHighlighter`](src/EmberTern.App/Completion/OccurrenceHighlighter.cs) (AvaloniaEdit `IBackgroundRenderer`) boxes every occurrence of the selected identifier; attached via `SqlEditorBehavior.Attach` (procedure editors) + the main SQL/DDL editors. New theme token `OccurrenceHighlightBrush` (both themes).
- **#8 Sub-tab contrast.** `TabItem.sub-tab`: inactive subtle, **selected = full contrast + SemiBold + accent underline**, larger font/padding, clearer hover.
- **#9 Cursor/Subprogram editors** keep the IBExpert list-left/editor-right split with editable names + autocomplete + Ctrl+Click + double-click + Alt+F format (via `SqlEditorBehavior`).

**Known limitations.** (a) Editing a structured cell on an exotic type (e.g. Size on a `TYPE OF` column) normalises it — an explicit user edit, not a silent loss. (b) #2 is RCA + diagnostics only (no behavior change) — the live MON$ capture is needed before deciding any fix. (c) Updated screenshots can't be produced from this environment; visual items (#6/#7/#8) await the user's validation pass.

**Gotcha — promote to architecture lore.**

118. **Firebird's metadata-update "object in use" check is cross-attachment — an autonomous DDL transaction on a separate connection does NOT isolate it from a working transaction that holds the object.** A selectable-procedure `SELECT * FROM proc(…)` holds the procedure "in use" for its transaction's lifetime; `ALTER PROCEDURE` (even on a fresh `Pooling=false` attachment) fails until that transaction ends. "Isolated metadata compile" only avoids lock contention on the working tx's *data*, not the engine's object-in-use guard. To diagnose which attachment/transaction pins an object, dump `MON$TRANSACTIONS ⋈ MON$ATTACHMENTS` from a transient connection at the moment of failure (gated by `EMBERTERN_TX_DIAG`).

### Refresh-storm fix #2 — scoped post-transaction refresh + Domain/Type sync (shipped 2026-06-17, uncommitted)

Trace-confirmed root cause of the post-commit "transaction cascade" (the user supplied EmberTern + IBExpert traces). **1267 → 1274 tests** (+7). Build clean (0/0), 8s smoke OK.

**Root cause (definitive, from the trace).** After a transaction settle, `MainWindowViewModel.OnTransactionStateChanged` ran a **blanket `foreach (tab in WorkspaceTabs) RefreshAfterTransactionAsync()`** — a FULL `RefreshStructureAsync` (fields + constraints + indexes + 4 dependency queries + data preview, ~7 heavy `RDB$` queries each) over **EVERY open TableDetail tab**, even tabs completely unrelated to the operation. In the trace, executing/editing a *procedure* triggered a structure refresh of two unrelated open table tabs (`XXX_GG_EMBERTRACE`, `NAGL`) → TRA_99122…99136+, each query an implicit auto-committed transaction, each commit re-firing the user's `ON TRANSACTION_COMMIT` audit trigger (`XXX_WS_TRANS_ON_COMMIT`). IBExpert refreshes ONLY the object it changed (~8 bounded transactions) and never touches unrelated open tabs. NOT infinite recursion — a bounded **fan-out** (N tabs × M queries) amplified by the audit trigger; the implicit metadata-read commits don't re-fire `TransactionStateChanged` (driver-level, not the working-tx `TransactionService`), so it doesn't truly recurse, but the volume reads as a storm.

Why the blanket structure refresh is obsolete: since Phase A, structure DDL (Add/Drop/Move field, constraints, indexes, Compile) runs in an **autonomous** transaction and **self-refreshes at apply-time**. The metadata working tx no longer carries revertible DDL, so "rollback reverts ALTERs → refresh every tab" is dead weight.

**Fix** ([MainWindowViewModel.cs](src/EmberTern.App/ViewModels/MainWindowViewModel.cs)). Replaced the blanket loop with a **pure, scoped selector** `SelectRefreshTargets(refresh, tabs, activeTab)`:
- **Structure** (metadata rollback, e.g. a raw F5 `ALTER`) → refresh ONLY the **active** TableDetail tab (the object the user is on). If the active tab isn't a TableDetail (e.g. a procedure), refresh **nothing** — exactly the user's scenario.
- **DataOnly** (data rollback) → reload the preview ONLY for tabs with **pending data edits** (`TableDetailTabViewModel.HasPendingDataEdits`, set on cell/row edit, cleared on reload/commit) — revert optimistic writes on the edited tab(s) only.
- **None** (commit) → nothing (unchanged).
Plus a re-entrancy guard `_postTxRefreshInFlight` (gotcha #119) so two coalesced settle events can't start overlapping refresh batches; per-VM `RefreshStructureAsync` coalescing (`_refreshInFlight`) still applies underneath.

**Domain → Type field-sync bug** ([ProcedureFieldRowBase.cs](src/EmberTern.App/ViewModels/ProcedureFieldRowBase.cs)). Selecting a Domain blanked the Type column (the old code nulled `BaseType`). Fixed: selecting a domain now **mirrors the domain's resolved type into the Type/Size/Scale cells for display** (`SyncTypeDisplayFromDomain`, guarded by `_syncingType` so it doesn't read as a user "pick a base type" that would clear the domain) — the canonical `TypeText` still becomes the domain NAME (round-trip-safe). A domain-typed variable loaded before the domain list arrives resolves its Type cell once `AvailableDomains` fires `CollectionChanged`. Picking a plain base type over a domain still clears the domain.

**Tests** ([RefreshStormFixTests.cs](tests/EmberTern.Tests/RefreshStormFixTests.cs), +7): data-rollback refreshes only edited tabs (not all); metadata-rollback refreshes only the active tab; settle with a non-TableDetail active tab refreshes nothing (the storm scenario); None selects nothing; Domain selection fills the Type cell + keeps the domain canonical; base-type-over-domain clears the domain; domain-typed variable resolves its Type cell when domains arrive after load.

**Gotcha — promote to architecture lore.**

119. **A transaction settle must NEVER blanket-refresh every open TableDetail tab — scope it to the object that was actually touched.** A full `RefreshStructureAsync` is ~7 heavy `RDB$` queries, EACH an implicit auto-committed transaction. On a database with an `ON TRANSACTION_COMMIT` (or START/ROLLBACK) trigger, every one of those commits re-fires the trigger — so refreshing N unrelated tabs after one settle produces an N×7 transaction storm that re-runs the user's audit trigger N×7 times. Since structure DDL is autonomous + self-refreshing (Phase A), the only legitimate post-settle refreshes are: the **active** tab on a metadata rollback (raw F5 DDL), and **tabs with pending data edits** on a data rollback (revert optimistic writes). Route through a pure `SelectRefreshTargets` selector + a re-entrancy guard; never `foreach (WorkspaceTabs) Refresh…`. This is the deeper, permanent form of gotchas #102/#108/#109 — "refresh only what changed, like IBExpert."

## Current state

- **Build**: clean (zero warnings, `TreatWarningsAsErrors=true` enforced).
- **Tests**: 1274 / 1274 passing.
- **Icons**: central themeable **SVG system** (Lucide) — `SvgIcon` control + `IconGeometries.axaml` (`Icon.*` `StreamGeometry` keys) + `IconGeometryConverter` (shape) / `IconBrushConverter` (color); VMs hold string keys only. ZERO Unicode/emoji glyph icons remain in any view (metadata tree, tabs, Table Detail, dependency views, toolbars, window caption). `.svg` sources are repo-only (build-excluded). Add a new icon: drop the Lucide `.svg` in `Assets/Icons/<cat>/`, add its `<StreamGeometry x:Key="Icon.Name">`, reuse before create — see [ICONS.md](src/EmberTern.App/Assets/Icons/ICONS.md).
- **UI / theming**: every color flows through the two-file central system ([Colors.axaml](src/EmberTern.App/Themes/Colors.axaml) tokens + [ControlStyles.axaml](src/EmberTern.App/Themes/ControlStyles.axaml) shared styles) — zero hardcoded/local colors in views or code-behind. FluentTheme's `SystemAccentColor` is overridden to the EmberTern blue (top-level, both themes) so checkboxes/radios/combobox selection no longer leak the Win11 amber. Sidebar is resizable (220–600px) + fully collapsible (0px column + left-edge grab rail), and the SQL editor's results panel is height-resizable with 3-state column sort + client-side pagination; sidebar width/collapse + results height persist in `WorkspaceState`. New-UI rules ("Reuse before create" + "UI Review Checklist") live in the UI styling section above.
- **Security / settings**: all user settings live in **one whole-file DPAPI-encrypted file** `%AppData%\EmberTern\settings.dat` (`ApplicationSettings` aggregate: Connections + Folders + Workspace + UserSettings), now wrapped in a **plaintext container header** `EMBERTERN-SETTINGS<TAB>containerVersion<TAB>encryptionScheme\n` ([SettingsFileContainer](src/EmberTern.Core/Settings/SettingsFileContainer.cs)) so a load can identify the file, pick the right protector (`EncryptionSchemes` — `dpapi` today, future AES/passphrase registered in `ResolveProtector`), and reject a newer container/scheme/SchemaVersion without decrypting. `ConnectionProfileStore` / `FolderStore` / `WorkspaceStore` are section facades over a shared `ApplicationSettingsStore` (read-modify-write per section). **Downgrade protection** on three axes guards both `Load` (degrade to null + `LastLoadDiagnostic`) and `Save` (refuse to overwrite a future file + `LastSaveDiagnostic`). **Migration ladder** `MigrateToCurrentVersion` (`Migrate_1_2` + stepwise `switch` — add `Migrate_2_3` etc.) replaces ad-hoc shim migration. Legacy headerless `settings.dat` is read unchanged and re-wrapped with the container on first save (verified on the real file). First launch still migrates legacy `connections.json` / `workspace.json` / `folders.json` (v0 or v1) into `settings.dat` and deletes them. `UserSettings.GridProfiles` is now wired to live grids (column order/width/auto-fit — see the Grid layout profiles milestone); `AppearanceSettings` is still a persisted stub. DPAPI ciphertext is per-user/per-machine → not portable; cross-machine export will register an `aes256-passphrase` `SecretProtector` (seam ready — `ResolveProtector` returns null for unknown schemes today).
- **App**: builds + launches cleanly (8-second-uptime smoke). Table editor: Pola (inline edit + Add/Drop/Move field + Drop FK; PK detected from the PRIMARY KEY constraint, not the per-field flag), Ograniczenia (Add/Drop PK/FK/Check/Unique, PK/Unique with optional USING index config), Indeksy (Add/Drop index + constraint-backed-index guard + **Recompute statistics / all** via an autonomous auto-committed admin tx; `-1` sentinel rendered blank), Dane (paged inline edit + **Set NULL** cell context menu), Opis (editable + Save/Clear), DDL. **Views** open in a dedicated View Detail surface (SQL editable source + Compile · Fields · Dependencies · Data · Description · DDL); New View seeds a `CREATE VIEW` template (SQL-only, no designer). **Procedures** open in a dedicated Procedure Detail surface — **Source ⇄ Easy mode** Editor (Easy = editable Input/Output param grids + read-only Variables/Cursors/Subprograms above a body-only editor; Source = full `CREATE OR ALTER PROCEDURE` text) · Description · Dependencies · DDL · Result (with First/Prev/Next/Last paging) + a persistent execution-info bar (time/rows/affected/error, even for no-result-set procs); toolbar has Compile · **Execute** (Data-lane, parameterized, green) · **Format** (the shared PSQL-aware `SqlFormatter`) · Comment Body (blue) / Uncomment Body (red, block `/* */` around the outer BEGIN…END). Last-used Source/Easy mode persists across procedures + restarts. New Procedure seeds a template (SQL-only, no designer). Computed By has a complete dependency model enforced in the UI (New Table grid cells DISABLE per-row, not just the DDL); domain selection governs/shows the type. New Table grid scrolls horizontally to all columns. Validation/compile messages always surface. Post-data-commit refresh is data-only (no structure reload → no freeze, no spurious "no PK").
- **Transactions (C2 + auto-routing shipped)**: **two independent lanes per connection** — Data (connection #1: data SQL, preview/edit) and Metadata (connection #2: DDL from the structure editor + metadata browsing). Each lane has its own **transaction profile** (`DataTransactionProfile` / `MetadataTransactionProfile`, both default Read Committed), its own working transaction, and its own Commit/Rollback. Profiles map to explicit TPB; default `write + read_committed + rec_version + nowait` (IBExpert-matching), read at begin time. Browsing runs on the metadata attachment so it no longer pins objects in the data working tx (the original IBExpert-blocking bug). **SQL Editor: one Execute (F5 / Ctrl+Enter), lane chosen automatically** by `SqlStatementClassifier` (data DML/reads → Data, DDL/DCL → Metadata, ambiguous → Data); no Shift+F5 / manual override. Every execute logs "Executed via X profile (Y)". **One unified Commit / Rollback pair** (`CommitAllCommand` / `RollbackAllCommand`) — the user never picks a lane; Commit settles every open lane, Rollback reverts every active/error lane (both when both are open). Compact title-bar profile block — two stacked lines, static `Data:`/`Meta:` label + full profile name in a lane-colored badge (blue / purple), inside the hard-fixed 36 px title-bar row. Metadata attachment opens best-effort; degrades to aliasing the data lane if the second attach fails. `EMBERTERN_TX_DIAG=1` logs real server-side TPB (per lane) to `%TEMP%\EmberTern-debug.log` on every begin.
- **Branch state**: committed on branch `feat/stabilization-sprint` (off `master`), pushed. **Uncommitted in working tree: Procedure Detail V1.3 + V1.4 Phase A + PSQL-aware shared formatter** (Format re-enabled in SQL Editor / View Detail / Procedure Detail; one `SqlFormatter` with a PSQL block mode; CASE…END kept inline; idempotent — gotcha #117). Next approved-and-pending build: **structured Easy-mode locals** (items 5/6/7/8 — Variables/Cursors/Subprograms as editable model elements regenerating the DECLARE section, full table-field type model + dropdown reused from `FieldRowViewModel`, IBExpert list+editor layout for cursors/subprograms, with items 2 [unify SQL Editor onto `SqlEditorBehavior`] + 3 [typed-dialog defaults/seconds/NULL-default/auto-size/in-memory history] folded in). Metadata model: **Phase A done**; Phases B–D held for your live validation. **Earlier uncommitted: Procedure Detail V1.3 + V1.4 Phase A** (metadata DDL Compile/Apply now auto-commits in one autonomous transaction via `FirebirdConnectionService.ExecuteAutonomousAsync`; metadata Commit/Rollback infra kept but inert — validate on the real DB before Phases B–D). Pending (designs to approve before coding): **PSQL-aware shared formatter** (item 1 — design-first) and the **structured Easy-mode locals model** (items 5/6/7/8 — Variables/Cursors/Subprograms as editable model elements that regenerate the DECLARE section, full table-field type model + dropdown reused from `FieldRowViewModel`, IBExpert list+editor layout for cursors/subprograms). Items 2 (unify completion onto `SqlEditorBehavior`) + 3 (typed-dialog defaults/seconds/NULL-default/auto-size/in-memory history) fold into the structured-locals pass. **Earlier uncommitted: Procedure Detail V1.3** (6 of 7 items implemented — block Comment Body/Uncomment Body; SQL-Editor parity in the procedure editors via shared `SqlEditorBehavior` reusing `SqlCompletionController` + double-click/**Ctrl+Click** navigation; typed Execute dialog with per-type controls; Cursors/Subprograms split list+source views with `ProcedureLocal.Source` capture; Insert Variable/Cursor/Subprogram helpers; item 7 of the original list). **Item 4 (unify all DDL→auto-commit, transactions only for data) is architecture-analyzed and HELD for approval — no code; migration plan recorded in the V1.3 milestone above.**). Prior shipped: **Procedure Detail V1.2** (UX polish — block `/* */` comment; metadata grids taller + autosize/resize/reorder; Execute green / Comment blue / Uncomment red; Result-tab client-side paging that never re-runs the proc; persistent execution-info bar for no-result-set procs; **Format removed from procedures** because the generic `SqlFormatter` is SELECT/DML-only and damages PSQL — a PSQL-aware formatter is the future path, `SqlFormatter` untouched; Easy/Source mode persisted via `WorkspaceState.ProcedureEasyMode`). Prior: **Procedure Detail V1.1** (Source⇄Easy mode toggle; Easy mode = editable Input/Output param grids + read-only Variables/Cursors/Subprograms panels ABOVE a body-only editor; canonical model {name,inputs,outputs,body} with a bounded `ProcedureSignatureParser` for Source→Easy round-trip + deterministic `DdlGenerator.BuildCreateOrAlterProcedure` for Easy→Source; `RDB$PROCEDURE_SOURCE` is the body alone so loading needs no parsing — gotcha #114; param editing reassembles the whole proc since FB has no incremental ALTER param — gotcha #115; `ProcedureBodyScanner` lists locals; Execute Procedure on the Data lane with bound `QueryParameter`s + a self-contained Result tab + a param-collection dialog; Comment/Uncomment via the editor Document API; `QuoteLight` in DdlGenerator — gotcha #116). Prior: **Procedure Detail V1** (procedures open in a dedicated 4-tab surface — editable `CREATE OR ALTER PROCEDURE` source + Compile, read-only Description[editable]/Dependencies/DDL, plus read-only Input/Output parameter grids in a resizable bottom panel under the editor — via a focused `ProcedureDetailTabViewModel`, NOT a ViewDetail/TableDetail reuse and NOT a generic abstraction; a procedure is not a relation, so it needs procedure-scoped catalog SQL — `RDB$PROCEDURES`, `RDB$PROCEDURE_PARAMETERS`, `RDB$DEPENDENCIES` type 5 — reuse stays at the tree-builder/FormatFieldType level; native ProcedureDetail persistence; New Procedure toolbar action with template; gotcha #113). Prior: **View Detail V1** (views open in a dedicated 6-tab surface — editable `CREATE OR ALTER VIEW` SQL + Compile, read-only Fields/Dependencies/Data/Description/DDL — via a focused `ViewDetailTabViewModel`, NOT a TableDetail reuse and NOT a generic abstraction; reuse is at the relation-generic reader + `internal static` helper level; native ViewDetail persistence; New View toolbar action with SQL template; gotcha #112). Prior: **System Tables — read-only TableDetail** (a system table opens in the full TableDetail workbench, read-only, via the single capability-based `CreateTableDetail` factory + `OpensAsTableDetail` routing predicate shared by direct-open and workspace-restore; edit affordances hidden via `ShowFieldEditTools`/`ShowDataEditTools` + capability-gated context menus; no `IsReadOnly` flag, no new readers/services/viewmodels/views). Follow-up: Dane grid made truly read-only — Boolean cells render a `✓`/blank glyph instead of a live `CheckBox` and BLOB opens view-only when `CanEditData` is false (`DataGrid.IsReadOnly` doesn't reach `CellTemplate` controls — gotcha #111). Prior: **Stabilization Sprint + trace follow-up**. Refresh storm RESOLVED: the trace proved the post-commit volume was the user's own `ON TRANSACTION_COMMIT` audit trigger (`XXX_WS_TRANS_ON_COMMIT` → `GET_NAGL_WERDYSP`, 579 `BIN_AND`/`MOD`), not an EmberTern metadata loop (0 RDB$ reloads, 2 txns). EmberTern now **never refreshes after a COMMIT** (only after ROLLBACK), so it commits one fewer transaction and stops re-firing that trigger; combined with the earlier event-leak/O(N²)-rebuild/duplicate-refresh fixes + skip-`SELECT *`-when-columns-unchanged + `EMBERTERN_REFRESH_DIAG` tracing. Also: file filter now includes `.fb` (real ext was `SZKOLENIE.FB`); the Pola "modified row" tint is a subtle accent (was brown `WarningBrush`) and clears live; inline Size/Scale editing; tab activation history; results-panel maximize/restore; connection-name 60-char limit + ellipsis. Prior: **Sprint UI Premium — SVG icon system + titlebar polish** (full Lucide SVG migration of all object/metadata + action icons across every view, FluentTheme `SystemAccentColor` unification, active-DB-name + active-tab readability polish, 16px window-caption icons). Prior: **Connection management + metadata-refresh + context-menu fixes** (active-profile live update on edit, metadata-refresh lock-leak fix, Indeksy Recompute statistics + `-1` sentinel, Dane Set NULL, unified Commit/Rollback pair, PK-from-constraint detection, data-only post-commit refresh). Prior: **UI ergonomics — theme consolidation + resizable/collapsible sidebar + SQL results panel**. Prior: **Grid layout profiles V1** (column order / width / auto-fit persistence via one shared `GridLayoutBehavior` + `GridProfileStore` over `UserSettings.GridProfiles`; 8 supported grids; smoke-verified). Deferred (architecture ready): click-to-change profile from the chip (flyout + `SetData/MetadataProfile`, profile read at begin time so a live switch is safe); grid profiles for `FieldDependencies` / `NewTable.Fields` (one XAML attribute each); grid sort/filter/grouping/selection memory (V2). Next planned workstreams (not started): config **export/import** — mostly unblocked by the settings container (add an `aes256-passphrase` `SecretProtector`, register it in `ResolveProtector`, write that scheme into the header for the exported file); wire `AppearanceSettings` to a real consumer.

## Milestone final state — Procedure Detail + Structured Easy Mode (2026-06-17)

This milestone is **committed** at the end of this session (Procedure Detail V1.1–V1.4, structured Easy Mode rounds 1+2, the PSQL-aware shared formatter, the metadata refresh-storm fix, and related UI work). 1274 / 1274 tests green; build clean (0/0). Two issues remain **open and explicitly deferred to a future session** (recorded below so they are not lost and so the already-solved refresh problem is not revisited).

### Done in this milestone
- **Procedure Detail** surface (Editor with Source⇄Easy mode, Description, Dependencies, DDL, Result + persistent exec-info bar; Execute on the Data lane; Compile; Comment/Uncomment Body).
- **Structured Easy Mode** — Variables / Cursors / Subprograms are first-class editable model elements; the DECLARE section is regenerated from the model (no insert-at-caret); full Source↔Easy↔Source round-trip with order preservation; full field-definition grids (Type/Domain dropdowns + TYPE OF / Size / Scale / Sub Type / Charset / Collate / Not Null / Default / Description); cursor Scroll + editable list names; subprogram Procedure/Function prompt + editable list names.
- **PSQL-aware shared `SqlFormatter`** (one formatter; block-structured PSQL mode; CASE…END kept inline; idempotent — gotcha #117) — Format re-enabled in SQL Editor / View Detail / Procedure Detail.
- **Metadata refresh-storm fix** — object-scoped post-transaction refresh (gotcha #119; details below).
- **UI** — BEGIN/END highlighting parity, occurrence highlighting, sub-tab contrast, Domain→Type field-cell sync.
- **Metadata auto-commit: Phase A only** (Compile/Apply auto-commits in one autonomous transaction). Phases B–D remain held pending live validation — and see open Issue #2.

### Refresh storm — root cause + fix + the rule (do not violate)
- **Root cause:** after a transaction settle, `MainWindowViewModel.OnTransactionStateChanged` ran a **blanket refresh of ALL open TableDetail tabs** (`foreach (WorkspaceTabs) RefreshAfterTransactionAsync()`), each tab triggering ~7 heavy `RDB$` structure queries. On a DB with an `ON TRANSACTION_COMMIT` trigger, every implicit-tx commit re-fired the trigger → a transaction storm over tabs unrelated to the action.
- **Fix:** **object-scoped refresh only** — a pure `SelectRefreshTargets` selector. Metadata rollback → only the **active** TableDetail tab; data rollback → only tabs with **pending data edits**; commit → nothing. Plus a `_postTxRefreshInFlight` re-entrancy guard.
- **gotcha #119 remains load-bearing and must not be violated.** **Never iterate all open workspace tabs and refresh metadata after a transaction settle. Refresh only the object affected by the originating action.**

## Known Open Issues (deferred to a future session)

### Issue #1 — Procedure lock after Execute → Rollback → Compile
- **Status:** OPEN — needs investigation in a future session.
- The **refresh storm (#119) is FIXED** — metadata refresh of unrelated tabs no longer occurs, and the trace spam is gone.
- **However**, the sequence Execute Procedure → Rollback → Compile can **still** produce an `"object … is in use"` lock.
- **Important:** the original diagnosis was incomplete. The refresh storm and the procedure lock are **two separate issues**. The refresh storm is solved; the remaining lock is a distinct problem still to be root-caused. **Do NOT revisit the already-solved refresh problem when investigating this** — start fresh on the lock itself (likely a transaction still holding the procedure "in use" at Compile time; `EMBERTERN_TX_DIAG=1` dumps MON$TRANSACTIONS ⋈ MON$ATTACHMENTS on an in-use DDL failure — gotcha #118).

### Issue #2 — Table Detail Compile-workflow regression after metadata auto-commit (Phase A)
- **Status:** OPEN — must be reviewed before the metadata workflow can be considered complete.
- **Symptom:** when **adding a field** to a table, the expected Compile workflow no longer behaves correctly — the **Compile button becomes inactive and/or the change is applied automatically** (rather than staged for an explicit Compile).
- **Cause:** a regression introduced during the metadata **auto-commit (Phase A)** work (structural edits now run autonomously/auto-commit; the staged-Compile expectation on the Table Detail Pola surface was not reconciled with that).
- **Action for next session:** review the Table Detail field-edit → Compile flow against the Phase A autonomous-commit model and decide the intended behaviour (staged Compile vs. immediate apply) before continuing the metadata auto-commit migration (Phases B–D).

## V1 — definition of done (all met)

1. Add a Firebird connection ✓ (M2)
2. Connect to a database ✓ (M2)
3. Write SQL in editor ✓ (M3)
4. Execute and see results ✓ (M3)
5. Manual transaction with visible status ✓ (M4)
6. Commit / Rollback ✓ (M4)
7. Browse metadata tree ✓ (M5)
8. Double-click object → see DDL ✓ (M6)

## V1.1 candidates (post-V1 polish, not committed)

Surfaced by what was actually built; ordered roughly by user-visible value:
1. **Refresh button on TableDetail / DDL tabs** — content is fetched once at open (lazy-loaded the first time the tab activates). After an external schema change there's no way to re-fetch without closing and reopening. Resetting `_loadTask = null` and re-firing `EnsureLoadedAsync` is mechanically straightforward.
2. **TableDetail persistence schema upgrade** — TableDetail tabs serialize as `CoreTabKind.Ddl` today (Fields/Indexes/Constraints discarded; only `DdlText` survives). A native `TableDetail` kind in the persistence DTO would keep the per-tab cache hot across restarts, at the cost of versioning the schema.
3. **Procedure / function param signature in tab header** — currently `Procedure: SP_BALANCE` is just the name. IBExpert shows `(IN, OUT)` shape; would help disambiguate overloads.
4. **DDL for FB 2.5 functions** — currently we just emit a one-line comment. Reconstructing the `DECLARE EXTERNAL FUNCTION` from `RDB$FUNCTION_ARGUMENTS` is mechanical and would close that gap.
5. **DDL syntax: domains, character sets, COMPUTED BY columns** — table reconstruction handles `COMPUTED BY` and references domains, but doesn't emit `CREATE DOMAIN` for the user-defined ones a table depends on. A "show dependencies" toggle would be a natural extension.
6. **Tab right-click menu** — Close, Close Others, Copy DDL to Clipboard.
7. **M7 hardening** — test against FB 2.5 / 3.0 / 4.0 (only FB5 has been used so far). Verify WIN1250 round-trip in DDL text. Verify large tables (50+ columns) render correctly in the column loop. Verify the new constraints query (with the `RDB$TRIGGERS chk_src` join) against FB 2.5 — should work but unverified.
8. **Trigger types 8192+** — DB-level / DDL triggers currently render as `/* trigger type 8192 */`. Decoding is non-trivial but feasible.
9. **Smart tab limit** — no cap on open DDL/TableDetail tabs right now; ten+ tabs and the strip wraps. A most-recently-used eviction policy at ~10 tabs would be cleaner.
10. **Editor: keyboard close (Ctrl+W)** — DDL/TableDetail tabs only close via the × button.
11. **Constraints/Indexes sub-tabs counts in the tab strip** — Pola shows N fields immediately but Ograniczenia/Indeksy/Dane need a click to learn their size. A `(N)` badge per sub-tab header would surface that.
12. **Drag a connection to the empty root area to un-folder it.** Today `ExecuteDrop` only moves a folder member back to root when it's dropped *onto a root sibling* (Before/After). Dropping onto blank space below the tree resolves to a null target and cancels. A "no row under pointer → treat as root append" branch in `ResolveDropTarget` would let users drag a connection straight out of a folder without needing a root sibling to aim at.
13. **Multi-select drag.** Drag grabs exactly one row. Selecting several connections and dragging them into a folder as a batch would speed up bulk reorganization of a large connection list.
14. **Insertion-line drop indicator.** Drop feedback is a full-row background tint (`IsDropTarget` → `DropTargetBrush`). A thin line between rows for Before/After (vs. the row tint reserved for Into) would make the exact landing position clearer, IBExpert-style. Deferred deliberately — the spec said "keep it simple, no animated insertion line."
15. **Headless UI test harness, expanded.** `ConnectionExpandBindingProbe` proved its worth (caught the `x:DataType` style clobber that VM-level tests can't see). Worth growing into a small suite that pins the other tree bindings (drop-target highlight, selection brush, folder rename TextBox focus) — the kind of regressions that only surface against the real compiled XAML.

## Architecture rules — enforce against drift

From the master prompt — these are non-negotiable for V1:

1. **Core has zero Avalonia dependencies.** ViewModels in App contain no Avalonia types (no `IImage`, `Color`, `Thickness`). Theme toggle lives in code-behind on purpose — single button, no value routing through VM.
2. **No interfaces without two concrete implementations.** Every service so far (`ConnectionService`, `QueryExecutor`, `TransactionService`, `ConnectionProfileStore`) is a direct class. No `IDbProvider` layer.
3. **No autocommit. Ever.** Auto-*begin* exists (matches IBExpert workflow); auto-*commit* doesn't. There's no toggle, no setting.
4. **Virtualized grid is mandatory.** Avalonia DataGrid handles this — don't replace it with a plain `ItemsControl`.
5. **No `Utils/` or `Helpers/` folders.** If something has no clear home, the structure is wrong.
6. **No `AppResources.resx`.** Use `UiStrings` (static const class). Add new strings there, in both spots if light/dark variants are needed.
7. **No event bus / IMessenger** until 3+ components need to communicate. Currently events on services (`ActiveConnectionChanged`, `TransactionStateChanged`) wire VM directly — that's fine.
8. **Async only where the user waits**: query execution + connection. Not async everywhere.
9. **Dark + Light from day one.** Every new color → both dictionaries in `Themes/Colors.axaml`. Zero hardcoded colors in views — only `{DynamicResource}`.
10. **No workspace persistence in V1, no plugin system, no AI, no debugger, no schema compare, no docking.** Mockup shows aspirations; build only what's in the milestone plan.

## UI styling rules — theme discipline (enforce on every new window / dialog / control)

The app has **one** central theming system. Every new window, dialog, UserControl, DataTemplate, and control MUST go through it — no exceptions. These rules exist because new UI kept introducing local colors and FluentTheme's `SystemAccentColor`-derived highlights (the brown/orange selection rectangles), which clash with the workbench palette.

**The central system — two files, nothing else holds colors:**
- [`Themes/Colors.axaml`](src/EmberTern.App/Themes/Colors.axaml) — the **single source of every color**. `ThemeDictionaries` with a `Dark` and a `Light` dictionary, each defining the same set of `Color` keys then `SolidColorBrush` keys over them. This is the token catalog.
- [`Themes/ControlStyles.axaml`](src/EmberTern.App/Themes/ControlStyles.axaml) — the **single home for shared/reusable styles** (`Button.icon`, `Button.primary`, `Button.flat`, `Button.caption`, `TabItem.bottom-tab`, `TabItem.sub-tab`, `TextBlock.field-label`, `DataGridRow`/`DataGridCell`/`DataGridColumnHeader`, `ListBoxItem`/`TreeViewItem` state overrides, etc.). Loaded app-wide via `Application.Styles`, so these styles apply inside dialog windows and UserControls too.

**Hard rules:**
1. **No hardcoded colors. Anywhere.** No hex literals (`#RRGGBB`, `#AARRGGBB`), no named colors (`White`, `Black`, `Red`, …) on any `Background` / `Foreground` / `BorderBrush` / `Fill` / `Stroke` / `Color` / `Value` in views, code-behind, or styles. The only literal allowed is `Transparent` (it's "no fill", not a theme color — used for hit-target borders and reset states). If you need a color, it is a **theme token** in `Colors.axaml` or it does not exist.
2. **No local color definitions.** No `<SolidColorBrush>` / `<Color>` declared in a view's `.Resources`, no `new SolidColorBrush(...)` / `Color.Parse(...)` / `Brushes.X` / `Colors.X` in code-behind. Per-kind icon colors flow through `IconResourceKey` + `IconBrushConverter` (the VM holds the **key string**, never a brush — keeps "no Avalonia types in VMs" intact).
3. **Every color comes from both Light and Dark.** Add a new token → add it to **both** `ThemeDictionaries` (same key in `Dark` and `Light`). A token that exists in only one dictionary is a bug. Tokens that are intentionally theme-independent (e.g. `OnAccentColor` white text over the colored accent/chips, `CloseButtonHoverColor` the Windows caption red) are still defined in **both** dictionaries with the same value, with a comment saying why.
4. **Consume tokens via `{DynamicResource}`**, never `{StaticResource}`, for any brush a control paints with — `DynamicResource` re-resolves on theme toggle so the control recolors live. (`StaticResource` inside `Colors.axaml` to chain a `Color` into a `SolidColorBrush` is fine — that's a definition, not a consumption.)
5. **Reusable styles live in `ControlStyles.axaml`, not in views.** If a style (label, button variant, tab variant, grid styling) is or will be used in more than one place, it goes in the central file and views reference it by `Classes="..."`. A view's local `<X.Styles>` block is allowed **only** for genuinely view-specific, non-duplicated structure — and even then it must use theme tokens (e.g. row-height/padding sizing, opacity-only hover affordances, a `Classes.active-tab` background swap that reads `BackgroundBrush`). When in doubt, centralize.
6. **Never override FluentTheme state colors with hardcoded values.** FluentTheme paints selection/hover/focus from `SystemAccentColor` (brown/orange on a default Win11 install). The fix is already centralized: the `TreeViewItem*` / `DataGridCell*` / `ListBoxItem` resource-key overrides in `Colors.axaml` + the Style selectors in `ControlStyles.axaml`. New selection-driven controls inherit these automatically — do not re-solve it locally with a hex color.
7. **New windows/dialogs must set `Background="{DynamicResource BackgroundBrush}"` + `Foreground="{DynamicResource ForegroundBrush}"`** on the root so they theme correctly (FluentTheme's window default isn't our palette). Use `Classes="field-label"` for form captions, `Classes="h1"` for dialog headers, the `Button.primary` / `Button.flat` / `Button.icon` variants for buttons — don't restyle from scratch.

**Token cheat-sheet** (semantic name → use): `BackgroundBrush` (window/editor), `PanelBrush` (sidebar/header panels), `ElevatedPanelBrush` (titlebar/column headers), `BorderBrush`, `ForegroundBrush` (default text), `SubtleForegroundBrush` (hints/captions), `AccentBrush`/`AccentMutedBrush` (primary action, focus accent), `OnAccentBrush`/`OnAccentSubtleBrush` (text on accent/colored chips), `SelectionBrush`, `HoverOverlayBrush`, `FocusBorderBrush`, `ErrorBrush`/`WarningBrush`/`ConnectedBrush`, `TransactionActiveBrush`, `CommitButtonBrush`/`RollbackButtonBrush`, `RowAlternateBrush` (zebra), `DropTargetBrush`, `CloseButtonHoverBrush`, `DataLaneChipBrush`/`MetadataLaneChipBrush`, `IconColor_*` (per metadata kind, via `IconBrushConverter`). If none fit, add a new token (both dictionaries) — don't reach for a literal.

### Reuse before create

Before adding any of the following, **search the project first** and prefer extending/sharing over a parallel implementation:
- a new **style** → check `Themes/ControlStyles.axaml` for an existing class (`Button.icon`, `field-label`, `bottom-tab`, …) before writing one;
- a new **component / control** → check `Views/` and `ViewModels/` for an existing one to extend;
- a new **dialog layout** → reuse the dialog skeleton (`Background`/`Foreground` tokens + `h1` header + `field-label` captions + `Button.primary`/`flat` footer) used by the existing dialogs;
- a new **DataGrid behavior** → reuse `Behaviors/GridLayoutBehavior.cs` (column order/width/auto-fit), the `RowIndexComparer` (object?[] sort), and the dynamic-column build pattern in `TableDetailTabView` / `MainWindow.PopulateResultGrid`;
- a new **pagination mechanism** → reuse the page-state shape already in `TableDetailTabViewModel` (CurrentPage / PageSize / First/Prev/Next/Last + `HasNextPage`/`HasPreviousPage` + hint string) and the shared `TableDetailPagination*Icon/Tooltip` strings — do **not** stand up a second paging system;
- a new **toolbar** → extend the existing titlebar / editor toolbar, gating new buttons on the relevant `IsXxxTabActive` flag;
- a new **theme resource** → check `Themes/Colors.axaml` for an existing token (see the cheat-sheet above) before adding one.

Preferred order: **extend an existing component › reuse an existing style/behavior › share logic across views › (last resort) create new**. Avoid parallel implementations of the same capability — they drift and double the maintenance surface.

### UI Review Checklist

Before considering any UI task done, verify:
- [ ] no hardcoded colors (`#RRGGBB` / named) — only theme tokens;
- [ ] no local color definitions (no `<SolidColorBrush>`/`<Color>` in a view, no `new SolidColorBrush(...)` / `Brushes.X` in code-behind);
- [ ] colors consumed via `{DynamicResource}`;
- [ ] renders correctly in **Light** theme;
- [ ] renders correctly in **Dark** theme;
- [ ] existing styles / components reused (no reinvented label / button / grid / pagination);
- [ ] no duplicated styles (shared ones live in `ControlStyles.axaml`);
- [ ] no duplicated functionality (reused the existing behavior/VM pattern instead of a parallel one).

## Known driver gotchas (Firebird + managed .NET driver)

- **`FirebirdSql.Data.FirebirdClient` 10.3.4 implements only Srp / Srp256.** No `Legacy_Auth` code path in the managed assembly. `FbConnectionStringBuilder.AuthPlugins` does **not exist** as a typed property; setting it via the dictionary indexer is silently ignored.
- **`FbServerType` is `Default` or `Embedded`.** `Default` is pure managed wire — `fbclient.dll` is **not loaded** on this code path. `ClientLibraryPath` only matters in Embedded mode (kept in the UI but harmless when unused).
- **Firebird 3 "Install incomplete... CREATE USER" error**: caused by SYSDBA living only in the legacy password file. Fix is **server-side**, not client-side: `CREATE USER SYSDBA PASSWORD '…' USING PLUGIN Srp;` against any database on the instance (security3.fdb is instance-wide). IBExpert works because it uses native fbclient with Legacy_Auth support; managed .NET driver can't. See `memory/feedback_firebird_multiversion.md`.
- **WIN1250 / WIN1252 / ISO8859_2**: register `CodePagesEncodingProvider.Instance` before any `OpenAsync` (done in `FirebirdConnectionService` static ctor). See `memory/feedback_firebird_codepages.md`.
- **Connection errors show the raw server message.** `MapErrorMessage` always returns `"Could not connect to {endpoint}: {ex.Message}"` — nothing else. Do not add hints or interpret error causes (wrong password, missing user, plugin mismatch, host down, …); the server message is authoritative and the user or admin can read it directly. Earlier builds tried to categorize errors and surface a `CREATE USER … USING PLUGIN Srp` hint for Legacy_Auth; that was removed because it misfired on unrelated failures (the driver concatenates the whole GDS error vector, so wrong-password / missing-user errors often carried `"plugin"`/`"Legacy_Auth"` text and got mis-hinted).
- **Connection-attempt debug log**: every Connect/Test appends a timestamped, password-masked connection string to `%TEMP%\EmberTern-debug.log` (`LogConnectionAttempt` in `FirebirdConnectionService`). Useful for triaging "EmberTern says X, IBExpert says Y" reports — but remember they take entirely different protocol paths.

## Working style — session protocol

The master prompt is delivered milestone-by-milestone. Don't pre-build future milestones. When the user says "M5 only", that's the scope — questions / hypotheticals about M6 go in memory pointers, not code.

If the user asks for a change that contradicts a hard rule, push back briefly and ask before implementing — the rules exist for a reason and the user has flagged "remind me when I drift."

After each milestone:
1. Update this file's **Completed milestones** section.
2. Update `memory/project_embertern_scaffold.md` with the per-milestone details (this file stays high-level).
3. Confirm `dotnet test` is green and the app launches before claiming "done".

## Working conventions

### Session management
- **One milestone per session.** Start a new Claude Code session for each milestone (M1, M2, …, V1.1 task N). Don't try to land two milestones in the same chat.
- **End every session by updating CLAUDE.md** with completed work and current state *before* closing the session — this file is the handoff document, not the chat transcript. Includes the "Completed milestones" entry, "Current state" (build/test status, smoke-verify state), and any new gotchas worth promoting from memory notes into here.
- **Every new session starts by reading CLAUDE.md.** Do not ask the user to re-explain context — the answer is in this file. If something needed is missing from here, that's a documentation gap to fix on the way out, not a question to ask on the way in.

**Why these rules exist:** Claude Code's context window grows with every message in a session. Long sessions burn more tokens per turn (re-reading the whole transcript), risk hitting context limits mid-task, and make cost unpredictable. One milestone per session keeps the working set tight, costs predictable, and the handoff explicit — CLAUDE.md carries the state, not chat history.

## Pointers to deeper notes

- `memory/project_embertern_blueprint.md` — V1 scope, hard rules, milestone plan (master prompt distilled).
- `memory/project_embertern_scaffold.md` — every layout decision, every gotcha, every "why we did it this way" for M1–M5. Read this before making architectural changes.
- `memory/reference_embertern_prompt.md` — pointers to the master prompt + UI mockup on disk.
- `memory/feedback_firebird_codepages.md` — WIN1250 fix details.
- `memory/feedback_firebird_multiversion.md` — FB3 SYSDBA fix + driver caveats.
