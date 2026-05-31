# EmberTern — Claude Code Context

A modern desktop developer workbench for Firebird database developers, built with **.NET 10 + Avalonia 12**. Target users: ERP and backend devs who work daily with SQL, procedures, triggers, metadata, and transactions. Design philosophy: **less features, better experience; workflow quality over feature count; transaction-aware by default**.

Master prompt / V1 blueprint: `C:\Users\grzegorz.gronski\Desktop\embertern-claude-code-prompt.md`
Target UI mockup: `C:\Users\grzegorz.gronski\Desktop\UI koncepcja.png`

## Build, test, run

```powershell
# from project root
dotnet build EmberTern.slnx
dotnet test  EmberTern.slnx
# run the app
src\EmberTern.App\bin\Debug\net10.0\EmberTern.exe
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
3. **Interface-typed compiled bindings in `Style.Setter` don't push back TwoWay in Avalonia 12.0.3.** Tried `Style Selector="TreeViewItem" x:DataType="vm:IExpandableNode"` with `Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}"` — source→target worked, target→source silently failed. Category chevrons rendered but clicking them never flipped the VM. Fix: two concrete-type Styles (`x:DataType="vm:ConnectionNodeViewModel"` + `x:DataType="vm:MetadataNodeViewModel"`), each with its own `IsExpanded` setter. The interface was deleted. **Rule**: for TwoWay setters in Style, stick to concrete `x:DataType`. If a Style needs to apply to multiple VM types, write one Style per type rather than reaching for a common interface.
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

## Current state

- **Build**: clean (zero warnings, `TreatWarningsAsErrors=true` enforced).
- **Tests**: 319 / 319 passing.
- **App**: builds, launches, exits cleanly. SQL editor: autocomplete (Ctrl+Space, 3+ char auto-trigger, `ALIAS./TABLE.` column completion), formatter lowercases keywords + identifiers, syntax highlighting swaps on theme toggle, selection brush matches the rest of the app, light-theme accent blue, double-click on a loaded metadata object name opens its DDL tab. **Tables open as a 6-sub-tab TableDetailTab (Pola / Ograniczenia / Indeksy / Dane / Opis / DDL)** — 1-based field position, zero scale blanked, font 11, tight 22 px row pitch, Dane shows first 200 rows in a dynamic grid, Opis surfaces the table's RDB$DESCRIPTION. TableDetail tabs survive restart; toolbar buttons + Saved Queries panel hide on non-Query tabs.
- **Branch state**: working on master. **V1 + Explorer Redesign + Metadata-categories-expansion + V1.1 Workspace Persistence + Per-Connection Workspace + SQL Editor UX + SQL Queries Panel + Visual polish + SQL Editor execute-selected + autoformat + SQL Autocomplete + formatter-lowercase-all + light-theme highlighting + light-color-polish + double-click-open-DDL + Table Detail tab + Table Detail polish + per-tab toolbar + TableDetail persistence + 6 sub-tabs shipped.** Next V1.1 candidate from the list below.

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
1. **Refresh DDL button** on DDL tabs — DDL is fetched once at open; the user has no way to re-fetch after they edit the object elsewhere (e.g. in another tool).
2. **Procedure / function param signature in tab header** — currently `Procedure: SP_BALANCE` is just the name. IBExpert shows `(IN, OUT)` shape; would help disambiguate overloads.
3. **DDL for FB 2.5 functions** — currently we just emit a one-line comment. Reconstructing the `DECLARE EXTERNAL FUNCTION` from `RDB$FUNCTION_ARGUMENTS` is mechanical and would close that gap.
4. **DDL syntax: domains, character sets, COMPUTED BY columns** — table reconstruction handles `COMPUTED BY` and references domains, but doesn't emit `CREATE DOMAIN` for the user-defined ones a table depends on. A "show dependencies" toggle would be a natural extension.
5. **Tab right-click menu** — Close, Close Others, Copy DDL to Clipboard.
6. **M7 hardening** — test against FB 2.5 / 3.0 / 4.0 (only FB5 has been used so far). Verify WIN1250 round-trip in DDL text. Verify large tables (50+ columns) render correctly in the column loop.
7. **Trigger types 8192+** — DB-level / DDL triggers currently render as `/* trigger type 8192 */`. Decoding is non-trivial but feasible.
8. **Smart tab limit** — no cap on open DDL tabs right now; ten+ tabs and the strip overflows into a horizontal scrollbar. UX-wise a most-recently-used eviction policy at ~10 tabs would be cleaner.
9. **Editor: keyboard close (Ctrl+W)** — DDL tabs only close via the × button.

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

## Known driver gotchas (Firebird + managed .NET driver)

- **`FirebirdSql.Data.FirebirdClient` 10.3.4 implements only Srp / Srp256.** No `Legacy_Auth` code path in the managed assembly. `FbConnectionStringBuilder.AuthPlugins` does **not exist** as a typed property; setting it via the dictionary indexer is silently ignored.
- **`FbServerType` is `Default` or `Embedded`.** `Default` is pure managed wire — `fbclient.dll` is **not loaded** on this code path. `ClientLibraryPath` only matters in Embedded mode (kept in the UI but harmless when unused).
- **Firebird 3 "Install incomplete... CREATE USER" error**: caused by SYSDBA living only in the legacy password file. Fix is **server-side**, not client-side: `CREATE USER SYSDBA PASSWORD '…' USING PLUGIN Srp;` against any database on the instance (security3.fdb is instance-wide). IBExpert works because it uses native fbclient with Legacy_Auth support; managed .NET driver can't. See `memory/feedback_firebird_multiversion.md`.
- **WIN1250 / WIN1252 / ISO8859_2**: register `CodePagesEncodingProvider.Instance` before any `OpenAsync` (done in `FirebirdConnectionService` static ctor). See `memory/feedback_firebird_codepages.md`.
- **`FbException.Errors`** doesn't support `[i]` indexing in 10.3.4 — iterate with `foreach` and `break`. `FbError` exposes `LineNumber` but not `ColumnNumber`.
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
