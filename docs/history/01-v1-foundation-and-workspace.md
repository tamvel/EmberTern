# History — V1 Foundation, Explorer Redesign, Workspace Persistence (M1–M6, V1.1)

> Archived from CLAUDE.md's former "Completed milestones" chronicle during the Documentation Cleanup Sprint (2026-07-11).
> Verbatim extract, lines 89–403 of the original file. Nothing altered except this header.

---

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

#### ⚠ Superseded in part — Branding UX sprint, 2026-08-01

Three statements above are no longer true, and the current state is documented in
[`src/EmberTern.App/Assets/Branding/BRANDING.md`](../../src/EmberTern.App/Assets/Branding/BRANDING.md),
which is the authority from here on:

- **`EmberTern_logo.png` is no longer rendered in the top bar.** The titlebar mark was removed; the asset's
  one remaining consumer is the About window, at 128px.
- **The window icon is no longer assigned in `MainWindow`'s constructor.** It comes from a single
  `<Style Selector="Window">` setter in `Themes/ControlStyles.axaml`, which reaches every window — the other
  25 had no icon at all until then.
- **`logo.png` no longer ships.** It is excluded from `AvaloniaResource` (the same treatment the icon `.svg`
  sources get) and stays in the repository purely as the master artwork.
- **All three files are different artwork now** — the logo was replaced on 2026-08-01 (below). The
  1536×1024 recorded above was already wrong for the old master (it was 976×973).
- **`logo.png` moved to `Assets/Branding/Masters/`, and it is no longer the only master.** The OS icon is
  rendered from a *second* source, `Masters/icon-source.png` — the two shipped assets are deliberately
  different artwork (below).

---

### Branding UX sprint (2026-08-01)

A small, closed sprint on the application's visual identity — no logic touched, no fonts, spacing, colours or
layout changed beyond the removal itself.

**1. The titlebar logo is gone.** The user's reasoning, and it is the modern desktop idiom (ChatGPT, Claude,
VS Code): a working surface's chrome is for the document, not for the product's identity, and the 26px mark
plus its divider were spending ~40px of the window's most contested horizontal space telling the user which
application they had launched.

⭐ **The interesting half was making sure nothing was left behind.** Deleting the `<Image>` is trivial; the
mistakes that read as rendering bugs are the leftovers. Three of them existed here:

- The brand `StackPanel`'s own `Margin="8,0,2,0"` — a container whose children are all collapsed **is still
  measured**, so `IsVisible` on the children alone would have left a 10px inset at the window's left edge
  with nothing in it. Fixed by gating the container itself on `HasActiveConnection` (safe: the block's only
  other content is the DEV MODE badge, and `IsDeveloperModeActive` reads
  `ActiveProfile?.DeveloperMode == true` — it cannot be true without an active profile).
- The divider that separated the mark from the connection name, which with nothing to its left becomes a rule
  against the window edge. Deleted.
- The action zone's **leading** divider, which had the same problem one level out: with no connection there is
  now nothing to its left either. Gated on the same condition as the block it separates.

**2. The icon mechanism, audited end to end.** The audit found the real defect was not in the paths that
existed but in the ones that did not:

| Surface | Before | After |
|---|---|---|
| `EmberTern.exe` (Explorer, pre-launch taskbar) | `<ApplicationIcon>` ✓ | unchanged |
| Main window / taskbar / Alt+Tab | assigned in `MainWindow`'s ctor | from the shared style |
| **The other 25 windows** | **no icon at all** — blank slot in the title bar and in Alt+Tab | from the shared style |

⭐ **The fix is one `<Style Selector="Window">` setter in `ControlStyles.axaml`, and the choice is
structural rather than tidy.** Avalonia has no application-level window icon, but `Window.Icon` *is* a styled
property — so one setter reaches all 26 windows **and every window added later**, which is precisely what a
per-window assignment cannot do: that is a rule someone has to remember 26 times, and the 27th window is the
one that forgets. The `MainWindow` ctor assignment was **deleted with the same change**, not left as a
belt-and-braces duplicate — a local value outranks a style setter, so keeping it would have made the main
window the one window whose icon came from somewhere else.

⚠ **A compiling style setter proves nothing here**, which is why it is pinned by a test: the setter compiles
whether or not `Icon` is a styled property and whether or not the converter can read an avares URI. The
assertion is deliberately made against a **bare `new Window()`** — an icon reaching a window with no XAML and
no code-behind can only have come from the application-level style, which is exactly the property that must
hold when a future window is added.

⚠ **The test was written twice.** The first version also constructed a `MainWindow` to assert the titlebar
carried no mark, and it **hung** — the same shape as `ConnectionExpandBindingProbe`, the notoriously
hang-prone class, which does the same thing. Rewritten as the cheapest possible headless test (one bare
window, 476 ms) on the standing instruction that the suite hang is its own infrastructure task and no sprint
detours into it. The titlebar's "nothing left over" property is therefore **visual QA, not a test** — stated
rather than quietly dropped.

**3. `logo.png` stopped shipping.** 833 KB of source artwork with no avares reference anywhere — the single
largest embedded resource in the assembly, carried for nothing. Excluded from `AvaloniaResource` exactly the
way the icon `.svg` sources already are, so it stays the repository's master and leaves the binary.

**4. About was already correct.** The window has shown the 128px mark since the Hamburger Navigation sprint
(2026-07-29), so the sprint's third task needed no work — only the removal of a comment that had just gone
stale ("the same asset the titlebar uses"). ⭐ The outcome is the one the user asked for and is worth stating
positively: the logo now appears in **exactly one** place in the running application, and it is the place
where it is the subject rather than decoration.

**5. The artwork itself was replaced** — the sprint's actual point, delivered after the user pointed out that
the first pass had built the infrastructure but not performed the swap. The new mark (a forged-steel database
cylinder with an ember wing) replaced all three files, every shipped size regenerated **from the new master**
by the pipeline documented in `BRANDING.md`: `EmberTern.ico` at 16/24/32/48/64/256 and `EmberTern_logo.png`
at 256.

⚠ The new master is **673×673 and already tightly cropped** — the content bounding box came back as the whole
canvas, so the mark bled to all four edges. The pipeline's 5% pad is what stops it touching the slot border.

**6. Then the taskbar rejected it, and the sprint ended with TWO masters.** The user saw round 5's icon in the
Windows taskbar, did not like it there, and asked for a different graphic **for the `.ico` only** — the About
mark to stay exactly as it was.

⭐ **That turns a convenience into a decision worth recording.** Round 5's note above said the two shipped
assets "change together, because both are rendered from one source". That is now false *on purpose*: an OS
icon is judged at 16–32px inside dense chrome, an About mark at 128px on a quiet window, and a rendition that
carries one does not automatically carry the other. **When "one source feeds both" stops holding, two masters
is the honest answer** — a single compromise rendition would have served neither. `BRANDING.md` now opens
with that, because *"update the logo"* is otherwise an ambiguous instruction that would silently change a
surface nobody asked about. The masters moved to `Assets/Branding/Masters/`, excluded from `AvaloniaResource`
**as a folder** rather than by filename: the folder rule cannot be forgotten, and the forgotten-file failure
is silent (the 1.5 MB icon source would simply have shipped).

**⚠⚠ Cutting the opaque background was the one genuinely delicate step, and the constraint is arithmetic.**
The new icon source is 24bpp — no alpha at all — on a uniform `rgb(14,15,19)` ground, and **the artwork itself
contains pure black**, i.e. Chebyshev distance **19** from the background. Two consequences, both observed
rather than reasoned about:

- A **global colour threshold** ("remove pixels near the background colour") punches holes straight through
  the cylinder, because parts of the cylinder are *darker* than the background.
- A **flood fill from the border is correct, but only below that distance.** At tolerance 28 the fill walked
  through the artwork's own black pixels into the interior and ate a wedge out of the middle of the logo.

⭐ So the rule is: **tolerance strictly above the background's own noise (±4 here) and strictly below the
distance to the artwork's darkest pixel.** 12 shipped. The feather is restricted to the **1px rim** touching
the cut for the same reason — feathering by colour distance globally would make the artwork's near-black
interior semi-transparent, the same trap one level subtler.

⚠ **The wedge was invisible in every check made against the source's own dark background** and obvious the
moment the cut-out was composited over magenta. Verify a cut over white and magenta, never over the ground it
came from.

⚠ The icon is cropped **tight, with no padding** (the About mark keeps its 5% pad, since round 5) — the user
asked for it explicitly and the reasoning holds: an icon is drawn small in dense chrome, and the empty margin
that flatters a 128px presentation slot just makes a 16px icon look shrunken.

**⚠⚠ Two GDI+ traps made a correct `.ico` look catastrophically broken, and both cost verification time.**
`System.Drawing.Icon.ToBitmap()` returns **colour noise** for a PNG-compressed entry (it decodes the frame as
a DIB, so PNG bytes are read as pixels), and `new Icon(path, new Size(256,256))` **hands back the 64px
frame** (GDI+ does not select PNG-compressed 256px entries at all). A magnified strip built the obvious way
looks like static at every size.

⭐ **What settled it in one step: running the same inspection against the previously shipped icon, which
reproduced both symptoms identically.** The check that distinguishes "my file is broken" from "my tool is
lying" is the known-good file, not reasoning about the format — the same shape as gotcha #214, where a
NOWAIT failure was mistaken for a Firebird prohibition. The real verification walks the `ICONDIRENTRY` table
and decodes each payload with `Image.FromStream`: all six frames report declared size == decoded size, a PNG
signature, and `Format32bppArgb`. Neither of the two APIs can express that assertion. Recorded in
`BRANDING.md` so the next person to regenerate the icon does not re-derive it.

Verified end to end rather than assumed: the new bytes are embedded in `EmberTern.dll` (both shipped assets
found by payload search; `logo.png` correctly absent), the freshly built `EmberTern.exe` carries the new
mark, and the live process's main window returns a non-zero `ICON_BIG` handle from `WM_GETICON` — which is
what proves the style-driven icon reaches a real OS window, something the headless test cannot show.

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

