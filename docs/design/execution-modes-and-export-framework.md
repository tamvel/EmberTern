# Execution Modes + Export Framework — Design (DESIGN ONLY, no code)

Status: **FROZEN — approved for implementation** (see the Status section at the end for the resolved decisions). Two independent-but-related modules:
- **Part A — Execution Modes**: how a query/statement is *run* (how many rows, for what purpose).
- **Part B — Export Framework**: one shared infrastructure for turning any grid's data into a file / clipboard / SQL script.

Grounded in the current code (verified this session): `FirebirdQueryExecutor` (5000-row cap, `Truncated`, `QueryResult` fully materialized, `CommandLock`, data-working-tx, `CommandTimeout=0`, cancellable); `CopyGridMode`/`BuildCopyText`/`CopyGridAsync` + `ClipboardWriteRequested`; `SqlFileWriter.Utf8NoBom`; `SaveFileRequest`/`SaveFileRequested`; the batch-results dialog; the `GridFilter`/`FirebirdGridSqlBuilder` filter model; the materialized-vs-server-paged grid split.

Not copying IBExpert 1:1 — it's inspiration only. The goals are the project's: **fewer features, better experience; infer intent from the action instead of making the user pick a mode; one shared infrastructure, no per-module duplication.**

---

## Part A — Execution Modes

### A.1 The real problem, restated
One pipeline + a fixed 5000-row cap means the user can never (a) time the *whole* query, (b) export the *whole* result, or (c) deliberately choose "just peek" vs "give me everything". Today `Truncated=true` is set but the UX doesn't even tell the user, let alone let them act on it.

### A.2 The intent model (small, behaviour-distinct — not label-distinct)
Only three intents differ in *behaviour*; everything else is an implementation detail or an action, not a mode the user should reason about.

| Intent | What it does | Fills grid? | Materializes rows? | Primary use | Etap |
|---|---|---|---|---|---|
| **Preview** (default) | Stream until `PreviewLimit`, then stop + dispose reader | yes (≤ limit) | ≤ limit | "let me look" — the 90% case | 1 |
| **Full** | Stream *all* rows, up to a hard safety ceiling | yes (incremental) | all | export source, whole-set inspection | 1 (basics) · 2 (smart guarding) |
| **Benchmark** | Stream + **discard** rows, count + time only (+ optional Performance capture) | no | none | "how long does the full query really take" without paying memory/UI | *later* |

`Streaming` from the prompt is **not** a fourth user mode — it's the *mechanism* that makes all of them responsive and cancellable (see A.4). `Benchmark` is genuinely distinct and high-value here because the Performance module already measures whole-set reads/amplification (`SELECT COUNT(*)` is not equivalent — different plan/projection — so Benchmark iterates the real reader and throws rows away). **Decision: Benchmark is deferred** — it's the natural next etap after the export chain lands, not part of the first pass. The `ExecutionIntent` enum still includes it from day one so nothing has to be re-plumbed when it arrives; only the UI affordance + the discard path are held.

```
// design sketch — Core
enum ExecutionIntent { Preview, Full, Benchmark }
record ExecutionRequest(
    string Sql,
    ExecutionIntent Intent,
    int PreviewLimit,                 // default from settings, e.g. 1000
    long FullSafetyCeiling,           // default e.g. 1_000_000, confirm past it
    IReadOnlyList<QueryParameter>? Parameters);
```

### A.3 Execute UX — DECIDED: Variant A + D (one button + Shift+F5 power path)
**Final decision:** ONE `Execute` button. `F5` = Preview; `Shift+F5` = Full (a quiet power-user path, surfaced only in the button tooltip — no second button, ever). The **primary, intended route to a full read is the post-Preview truncation bar** (A.6) — it walks the user through the process, which is more intuitive than any toolbar control. No twin Execute buttons (IBExpert), no split-button, no persistent mode dropdown. Benchmark, later, is a command + `Ctrl+Shift+F5` — never a toolbar button. (Variants B/C/E below are kept only as the record of what was weighed.)

The variants that were considered — shared premise: `Execute (F5)` always runs Preview, model is `Preview | Full`, difference is only *how a deliberate Full run is reached*:

**Variant A — Single button, no explicit Full control.**
One `Execute` (Preview). No Full button anywhere; Full is reached only through the truncated banner's `[Load all]` (A.6) and through Export (which pulls Full internally).
- **+** Absolute minimum chrome; most "fewer features, better experience"; impossible to accidentally launch a 3M-row run from a cold start; intent 100% action-driven.
- **−** No way to *ask for* Full on the very first run — always Preview first, then Load all (one extra click). For a query you *know* is huge and want fully now, that's a wasted preview pass (mitigated: preview is cheap + fast, and for results under the limit Preview == Full anyway).

**Variant B — Two asymmetric buttons (`Execute` primary + `Execute all` secondary).**
Primary Execute (Preview) + a visually *lighter* secondary (flat/icon, not a second primary) labelled "Execute all". Deliberately asymmetric so it never reads as IBExpert's twin buttons.
- **+** Full is discoverable + one-click from the start; clear keyboard partner (Shift+F5).
- **−** Still two controls in the run area; the asymmetry must be styled carefully or it drifts back toward the thing we're avoiding; a second thing to explain.

**Variant C — Split-button (`Execute ▾`).**
Primary click = Preview; the `▾` opens `Execute all` (and later `Benchmark`).
- **+** One visual slot; secondary intents tucked away; a pattern users know from VS / DataGrip run-configs.
- **−** The `▾` is easy to miss (discoverability); a two-part hit target is slightly fiddly; you weren't convinced — noted.

**Variant D — Single button + keyboard modifier (invisible power path).**
One visible `Execute`. F5 = Preview, **Shift+F5 = Full**, surfaced only in the tooltip (`F5 preview · Shift+F5 all rows`). The banner still offers Load all.
- **+** Zero extra chrome (identical toolbar to A); power users get Full from the start; casual users never need to know.
- **−** The Full path is hidden behind a modifier + tooltip — undiscoverable for someone who never reads tooltips (they fall back to Preview + banner, which is fine — the downside is "power feature is quiet", not "broken").

**Variant E — Adaptive Execute (no mode, ever).**
Philosophy framing of A: there is no "Full" concept in the UI at all. Execute runs Preview; if the result fits under the limit it *is* complete (no banner); if not, the banner appears and *is* the only place "all" exists.
- **+** Simplest possible mental model — the user never thinks about modes; the tool mentions "more data" only when there genuinely is more.
- **−** Same as A: no cold-start Full; Benchmark (later) has no natural home and needs its own affordance regardless.

**Recommendation: Variant A as the base, plus Variant D's Shift+F5 as a quiet power path** (A+D). Toolbar stays one button; the truncated banner (A.6) is the primary, highly-visible route to Full; Shift+F5 serves power users without adding chrome or a hidden *required* feature. Benchmark, when it lands, becomes a command/menu item + Ctrl+Shift+F5 — never a toolbar button.
*Fallback if you want a visible Full control:* Variant B (asymmetric secondary) — the safest "two controls" option, avoids the twin-button look. Variant C (split) only if the run area must stay one slot *and* a visible dropdown is acceptable.

**Rejected outright:** two *equal* Execute buttons (IBExpert's twins); a *persistent* "Execution Mode" dropdown you set-and-forget (invisible state → accidental huge runs; intent must be explicit at the moment of the expensive action).

### A.4 Executor changes — stream instead of buffer
Turn the one-shot buffered read into a streaming read. This single change enables Preview/Full/Benchmark, incremental grid fill, instant cancel, and export-without-double-buffering.

```
// design sketch — Firebird
IAsyncEnumerable<object?[]> StreamAsync(ExecutionRequest req, CancellationToken ct);
// yields rows as the reader produces them; caller decides to keep / count / stop.
// QueryResult stays the final aggregate for the materialized (Preview/Full-into-grid) case,
// built by the VM from the stream; Truncated means "Preview cap hit" (distinct from
// FullCeilingHit, a new flag meaning "Full stopped at the safety ceiling").
```

- **Preview**: consume `PreviewLimit` rows → stop → dispose. (Today's behaviour, but honest + actionable.)
- **Full**: consume all into the grid with incremental UI batching (marshal in ~100 ms chunks like the batch dialog) + a live row counter + Cancel. Stop + confirm at `FullSafetyCeiling`.
- **Benchmark**: consume + discard, counting; report count + `ExecutionTimer` total (+ hand the run to the per-host Performance panel so its advisor sees the *whole* set).
- Keeps: `CommandLock` for the whole read (FbConnection single-threaded), the data-working-tx attach, `CommandTimeout=0`, `OperationCanceledException` passthrough. `ExecutionTimer` already exists and now measures the *real* full duration for Full/Benchmark.

### A.5 Preview limit — hardcoded default now, settings-ready
**Decision: no user configuration yet.** `PreviewLimit` (and `FullSafetyCeiling`) stay hardcoded defaults baked into the app — do NOT add a `UserSettings` field in this pass. Structure them so moving to settings later (when we design EmberTern's whole configuration system) is a one-line change:
- Put both in a single `ExecutionDefaults` source-of-truth (a static holder, e.g. `ExecutionDefaults.PreviewLimit` / `.FullSafetyCeiling`) that every call site reads — never scatter a literal.
- `ExecutionRequest` already carries `PreviewLimit`/`FullSafetyCeiling` as *values* (not a global lookup), so the executor is already parameterised; the VM fills them from `ExecutionDefaults` today and from `UserSettings` later. The seam is the VM assignment, nothing deeper.
- Proposed default: keep **5000** for now (matches current behaviour — no surprise regression) rather than dropping to 1000; revisit the number when it becomes a setting.

### A.6 Truncated Preview — the UX (must be loud, not a "Truncated" flag)
When a Preview stops at the limit, the user must **immediately** understand they're looking at a *fragment*, and the next actions must be *right there*. `Truncated=true` as a quiet status word is not enough.

**The affordance: a persistent notification bar pinned across the top of the results grid** (not a status-bar word, not a tooltip). It uses the attention/warning token (`WarningBrush`), an icon, a plain-language sentence, and inline action buttons:

**Etap 1** ships the bar with `[Load all rows]` ONLY. **`[Export all…]` appears in Etap 3** (when the Export Framework exists) — no dead button ships. Etap-1 shape:
```
┌────────────────────────────────────────────────────────────────────────────┐
│ ⚠  Showing the first 5,000 rows — the full result is larger.                 │
│                                                    [ Load all rows ]      [–] │
└────────────────────────────────────────────────────────────────────────────┘
│  # │ ID_NAGL │ KONTRAHENT        │ NETTO   │ …                                │
│  1 │ 10036   │ …                                                              │
```
Etap 3 adds the second action → `[ Load all rows ]  [ Export all… ]`.
- **Message** — plain, not jargon: *"Showing the first 5,000 rows — the full result is larger."* Never the bare word "Truncated".
- **Actions inline**:
  - **`Load all rows`** (Etap 1) → runs Full into the same grid: incremental fill + live row counter + Cancel (reuses the batch-dialog streaming/cancel pattern for big sets). On completion the bar disappears, the record indicator becomes exact, and `ExecutionTimer` shows the real full duration. **Client-side view state is preserved** — any sort / filter / aggregation the user applied to the preview re-applies to the full set (Load all is "more data", not "reset").
  - **`Export all…`** (Etap 3) → opens the shared Export dialog (Part B) with **Scope pre-selected to `All rows`** — exports the complete set without first loading it into the grid (the export re-fetches/streams to file directly; no need to materialise millions of rows in the UI just to export them).
  - **`[–]` collapse** → shrinks the bar to a thin one-line strip (never fully dismissible while data is truncated — the state must stay visible); clicking the strip re-expands.

**Smart Load-all confirmation (A.6.1) — ETAP 2 (SHIPPED 2026-07-08).** `Load all rows` must not silently pull an unknown, possibly-huge set into memory — but a confirmation dialog on *every* click is noise. **Etap 1 guards Load-all with the hard `FullSafetyCeiling` only** (load runs, and if it reaches the ceiling it stops with a plain message — safe + complete, just not clever about size). **The smart *soft* threshold below is Etap 2 refinement**, not part of the first pass. Since a truncated preview does **not** know the true total up front (no count was run), the confirmation is **data-driven during the stream, not a guess before it**:
- Click `Load all` → loading **starts immediately** (responsive; most results finish quickly and never prompt).
- A **soft confirm threshold** sits *below* the hard `FullSafetyCeiling` (proposal: soft ≈ 250,000 rows, hard ceiling ≈ 1,000,000 — both in `ExecutionDefaults`, settings-ready). If the stream **crosses the soft threshold while rows are still arriving**, it *pauses* and asks: *"Loaded 250,000 rows so far and there's more — keep loading the whole result into memory? [Keep loading] [Stop here]"*. "Stop here" keeps what's loaded and shows the truncation bar again (now "first 250,000"). "Keep loading" continues to the hard ceiling.
- If the source **can** give a cheap up-front estimate (server-paged Table/View Data via `GetRowCountAsync`), the confirm can fire **before** starting instead of mid-stream — same threshold, nicer (the user isn't made to watch 250k rows load before being asked). Arbitrary SQL editor queries have no cheap estimate → they use the mid-stream prompt. One threshold, two trigger points depending on whether an estimate is available.
- The hard `FullSafetyCeiling` remains the absolute backstop with its own distinct bar (A.6, ceiling-hit message). Net: **no prompt for normal-sized results; one honest prompt only when the set is genuinely large.**
- **Record indicator** reinforces it: instead of `Record 1 of 5,000` it reads `Record 1 of 5,000+ (preview)` — the `+` and the word *preview* make "this is a fragment" unmissable even away from the bar.
- **Distinct, stronger message for the Full safety-ceiling case** (`FullCeilingHit`, from A.4): *"Stopped at 1,000,000 rows — this is a safety limit, not the end of the result. Narrow the query to see the rest."* No `[Load all]` here (there's nothing more to safely load); offers `[Export all…]` (streams past the ceiling straight to file, since export doesn't hold rows in the grid) and a note to refine the query.
- **When NOT truncated** (result fit under the limit, or Full completed): **no bar at all**, record indicator shows the exact count. The bar is a signal, not permanent chrome — it appears only when there's genuinely more data.

**Load-all fill behaviour (A.6.2) — DECIDED (Etap 1): counter, not live grid reshuffle.** The SQL results grid is paginated + **sortable + filterable**, so "incremental fill" is NOT true row-by-row streaming into the grid. During a Full / Load-all read the user sees the live **"Loading… N rows"** counter + the running timer + Cancel; the grid then shows the **complete, correctly-sorted, correctly-paginated** result on completion. This is deliberate — for a sortable/paginated grid, live incremental rendering would make the page count change, rows reshuffle as the sort re-applies, and filters recompute repeatedly during the load, which is more distracting than helpful. The counter is the "it's working, N read so far" feedback; a stable grid at the end is the calmer, more predictable UX. True streaming-append rendering is reserved for **simple, non-sortable lists / logs** (e.g. Activity Monitor), where new rows naturally append at the end — not for the classic SQL results grid.

This is deliberately the same "answer + next action at the top" pattern the Performance verdict bar and the Activity-Monitor error handling already use — one consistent language for "here's the situation, here's what to do about it".

---

## Part B — Export Framework

### B.1 Non-negotiable architecture principle (stated twice in the brief)
**One shared export infrastructure for the whole app.** No per-module exporter. A module *only* supplies data (columns, rows, current filter, selection, a name hint); *all* file/clipboard/format logic is central. SQL Editor, Table/View Data, Activity Monitor, Performance grids, and every future grid reuse the same components with a ~30-line adapter each.

### B.2 Layering (obeys the project rules: Core pure, Firebird isolates `Fb*`, App = UI)

```
EmberTern.Core.Export         (zero Avalonia, zero Fb)
  IExportDataSource           — the contract each module implements
  ExportColumn(Name, ClrType) — reuse QueryColumn shape
  ExportScope { CurrentView, AllRows, SelectedRows }
  ExportCapabilities          — which scopes supported + row-count estimate + name hint
  ExportRequest(Format, Scope, FormatOptions)
  IExporter                   — one per format; pure; STREAMING
    DelimitedTextExporter     (CSV + TXT)      — pure
    ClipboardTsvExporter                        — pure (Excel-paste convention)
    SqlScriptExporter family  (INSERT/UPDATE/DELETE/MERGE) — pure
  IExportSink                 — destination abstraction (TextWriter / Stream + finalize)

EmberTern.Export.Office        (NEW small project, allowed a NuGet dep)   [Etap 2]
  XlsxExporter                — streaming SpreadsheetML writer (needs a lib — see risks)

EmberTern.App
  ExportService               — orchestrates: resolve exporter, open sink, stream, progress, report
  ExportDialogViewModel + ExportDialog   — the ONE dialog every module opens
  adapters (per module):
    QueryResultExportSource   — materialized SQL result + a re-run delegate for AllRows
    ServerPagedExportSource   — Table/View Data: full re-fetch honouring current filter + order
    RowBufferExportSource     — Activity Monitor / Performance: project VM rows to object?[]
```

Why XLSX is a separate layer: Core must stay zero-dep. CSV/TXT/TSV/SQL-script exporters are just `TextWriter` writing → pure Core. XLSX needs a library → it lives in a layer that's allowed the dependency, and the framework resolves exporters by format, so adding it is "register one more `IExporter`".

**`ExportScope` is a general data-operation concept, not export-only.** `CurrentView / SelectedRows / AllRows` is the natural "what set of rows does this operation act on?" vocabulary for *any* future bulk data operation in EmberTern (bulk delete/update-from-grid, bulk copy, "generate script for these rows", send-to-…). Keep the enum + the `IExportDataSource.Capabilities` gating pattern reusable — a later feature should be able to reuse the same scope selector + capability model rather than reinventing it. (Not building those now; just don't box the concept into `Export`-only naming/coupling.)

### B.3 The data-source contract (what each module supplies)
```
// design sketch — Core
interface IExportDataSource {
    IReadOnlyList<ExportColumn> Columns { get; }
    ExportCapabilities Capabilities { get; }          // scopes + est. row count + default file name
    IAsyncEnumerable<object?[]> GetRowsAsync(ExportScope scope, CancellationToken ct);
}
```
- A **materialized** source (SQL result already fully in memory, `Truncated=false`) yields its rows synchronously-wrapped — instant, no DB hit.
- A source whose grid is a *Preview* (`Truncated=true`) or *server-paged* (Table Data holds one page) implements `AllRows` by **re-fetching** — the SQL source re-runs the query with `ExecutionIntent.Full`; the server-paged source calls `GetDataPreviewAsync` with no page limit + the **current filter (WHERE) + order**, so the export matches exactly what the user sees.
- Activity Monitor: `CurrentView` = the filtered `Rows`, `AllRows` = the whole `_all` ring buffer, `SelectedRows` = selection. No re-fetch (a live buffer can't re-read history) — capabilities advertise that.

Async throughout because AllRows may be large / a DB round-trip; the dialog streams it to the sink with progress + cancel, never building a second giant buffer.

### B.4 Formats — deliberately few
Only what's actually used, per the brief:

| Format | Layer | Notes |
|---|---|---|
| **Excel (.xlsx)** | Office (Etap 2) | the headline format (the "snapshot before a risky op" scenario) |
| **CSV** | Core | RFC-4180 quoting; configurable delimiter; **BOM by default** (Excel detects UTF-8) |
| **TXT** | Core | same engine as CSV, TAB default; or fixed-width (optional) |
| **Clipboard** | Core/App | TSV, Excel-paste convention (reuse the existing copy logic) |

One optional addition worth its weight: **JSON** — *only if* a real consumer appears (feeding an API/script). Not added on spec. **Rejected**: DBF, SYLK, DIF, LaTeX, RTF, XML, HTML — IBExpert's long tail, zero daily value, each a maintenance + test surface.

### B.5 CSV / TXT correctness (the safety strategy)
- **Quoting = RFC-4180**, not the clipboard's space-replacement. Quote a field iff it contains the delimiter, a `"`, CR, or LF; double internal quotes. This is what makes "separator inside data" safe — the file never corrupts. (The clipboard TSV path keeps space-replacement because Excel-paste doesn't honour quotes well — the two destinations legitimately differ.)
- **Delimiter**: `;` / `,` / `|` / TAB, user-selectable. Default `;` for pl-PL (Excel's regional list separator when the decimal mark is `,`).
- **Decimal/date**: format via `CultureInfo` with an option for Invariant vs Current — Excel-in-pl-PL wants `,` decimals; a machine consumer wants Invariant.
- ⚠️ **Encoding trap (sharp, non-obvious):** CSV-for-Excel wants a **UTF-8 BOM** (Excel needs it to read UTF-8 CSV; without it Polish chars mojibake). This is the **opposite** of `SqlFileWriter.Utf8NoBom` (the `.sql` no-BOM rule, gotcha #178). So a naive reuse of `SqlFileWriter` for CSV would break Excel import. **CSV default = UTF-8 *with* BOM (user-toggleable); `.sql` script export = no BOM.** Encoding is per-format, not global.

### B.6 Export re-run vs cached — the decision
Three options analysed:
1. **Export cached (grid) rows** — instant, but a Preview holds only N rows and a server-paged grid holds one page → **wrong for the snapshot scenario** (incomplete data).
2. **Export by re-running (Full)** — always complete, but costs a re-execute and can differ from what was shown if data changed; runs on the user's connection/tx so it sees their own uncommitted edits (usually the *desired* "current snapshot").
3. **Always ask the user** — a decision most users don't want every time.

**Decision — smart default, inferred from truncation state; explicit override available:**
- If the grid holds the **complete, untruncated** materialized result (`Truncated=false`) → export cached rows. Instant, genuinely complete, no pointless re-run.
- If the grid is a **Preview** (`Truncated=true`) or **server-paged** → export **re-fetches the full set** (SQL: re-run Full; Table Data: full fetch with the same filter+order), with the progress dialog.
- The dialog's **Scope** control makes this visible and overridable: `Visible rows (N)` / `All rows (~M)` / `Selected (K)`. "All rows" is preselected when it means completeness; the user can force "Visible rows only".
- **Honesty**: when the export re-ran, note it — "Exported 48,213 rows (re-read from the database just now)". Consistency caveat surfaced: the re-read is on the user's tx (their uncommitted edits included; another session's uncommitted work not).

### B.7 Export Script (INSERT/UPDATE/DELETE/MERGE) — architecture only
Just more `IExporter`s (`SqlScriptExporter` family). No generators built yet; the shape:
```
// design sketch — Core
record SqlScriptOptions(
    SqlStatementKind Kind,        // Insert | Update | Delete | Merge
    string TargetTable,
    IReadOnlyList<string> KeyColumns,   // WHERE key for Update/Delete, ON key for Merge
    IReadOnlyList<string> IncludeColumns,
    int BatchSize,                // e.g. blank line / COMMIT every N (optional)
    bool IncludeCommit);
```
- Needs a **target table** (+ key columns for UPDATE/DELETE/MERGE). An arbitrary SELECT/join has no inherent table → the user types the table name (prefilled for a Table Data grid) and picks key columns. Ambiguity is warned, never guessed.
- **Literal formatting reuses existing code** — `TraceSqlInliner` / `DdlGenerator` already turn a CLR value into a Firebird literal (string with `''` escape, NULL, invariant numeric, date/time literal, BLOB → placeholder/skip). Do **not** reinvent.
- INSERT is trivial (no keys). UPDATE/DELETE need keys. MERGE last — evaluate whether it earns its place (Firebird `UPDATE OR INSERT` / `MERGE` both exist; MERGE is the general form).

### B.8 UX — one entry point, one dialog, format-driven disclosure
- **One "Export…" affordance per grid**: an Export toolbar button (dedicated download/export `SvgIcon`) + an "Export…" item appended to the existing right-click Copy menu. Opens the shared `ExportDialog` with that grid's `IExportDataSource`.
- **One dialog** — Data formats *and* Script formats live in one grouped **Format** picker; picking a format reveals only that format's options (progressive disclosure). More modern than IBExpert's two separate windows; simpler than its tabbed 3-panel dialog.
- **Estimated row count, shown up front** — each Scope option displays the source's estimate when available (`All rows (~48,213)`), so the user knows *before* clicking Export whether they're exporting 500 rows or 5 million. From `ExportCapabilities.EstimatedRowCount` (nullable): **exact** for a complete materialized result and for AM buffer scopes (`(1,000)`); a **cheap estimate** for server-paged sources (`GetRowCountAsync`, capped → `(~50,000+)`); **unknown** for a truncated SQL preview whose full size wasn't counted → show `(all rows)` with no number rather than run a count query just to label the option. It's a UX hint (`~`, source-optional), never a promise of an exact figure.

```
┌ Export ───────────────────────────────────┐
│ Format:  [ Excel (.xlsx)            ▾ ]    │   grouped list:
│                                            │     Data:   Excel · CSV · Text · Clipboard
│ Scope:   ( ) Visible rows (1,000)          │     Script: INSERT · UPDATE · DELETE · MERGE
│          (•) All rows  (~48,213)           │
│          ( ) Selected  (3)                 │   (scopes gated by source capabilities)
│                                            │
│ ▸ Options            (collapsed by default)│   CSV/TXT → Delimiter [;▾] Encoding [UTF-8 (BOM)▾] ☑ Header
│                                            │   Script  → Table [____] Keys [▾] (only for U/D/MERGE)
│ Destination: (•) File …   ( ) Clipboard    │   (Clipboard hidden for xlsx; File default name from source hint)
├────────────────────────────────────────────┤
│                          [Cancel] [Export]  │
└────────────────────────────────────────────┘
```
- **File** goes through the existing `SaveFileRequested` channel (generalise its filter to the chosen format). **Clipboard** through `ClipboardWriteRequested`.
- **Progress**: a large/re-fetch export reuses the batch-results-dialog model (preparing → streaming row counter → Cancel → success/fail) so a 2M-row export is responsive and abortable. A small cached export just completes.

> **RESOLVED (Etap 3, 2026-07-08 — user-approved).** The mockup above listed "Clipboard" both as a **Format** *and* as a **Destination**, which is redundant. Resolution: **Clipboard is a Format, and the separate `Destination: File / Clipboard` row is removed.** The chosen Format determines the workflow — CSV / Text (and, later, Excel) prompt a Save-file dialog; **Clipboard** copies straight to the clipboard with no location prompt (a short success message, no separate destination control). This kills the ambiguous combinations (`Format=Clipboard + Destination=File`, `Format=CSV + Destination=Clipboard`) and matches "fewer features, better experience" + progressive disclosure. Etap-3 formats are **CSV · Text · Clipboard** only (Excel = Etap 4, SQL-Script = later etap → no dead options; presented as radio buttons since the list is short — the grouped dropdown returns when Excel/Script arrive). The clipboard path keeps the space-replacement TSV convention (B.5); CSV/Text use RFC-4180 with per-format encoding (CSV → UTF-8 **with** BOM by default; Text → UTF-8 no BOM).

### B.9 What existing code becomes
- `CopyGridMode` / `BuildCopyText` / `CopyGridAsync` → the **Clipboard exporter** of the framework (the copy right-click menu keeps working; internally it routes through `ClipboardTsvExporter`). No behaviour change for users.
- `SqlFileWriter` → the **`.sql` no-BOM** encoding used by the Script exporters (unchanged); CSV/TXT get their own per-format encoding (BOM default).
- `SaveFileRequest`/`SaveFileRequested` → generalised to the export dialog's file destination (multi-format filter).
- The batch-results dialog → the **export progress** UI (same preparing/streaming/counter/cancel shape).
- `GridFilter` + `FirebirdGridSqlBuilder.BuildWhere` → the **server-paged source's** re-fetch WHERE (so filtered Table Data exports exactly the filtered set).

---

## Etaps (build order — CONFIRMED with the user)

Order: **Preview → Full → Export Framework → XLSX → Activity Monitor.** Benchmark is a later natural etap (the `ExecutionIntent` enum keeps it from day one; only its affordance + discard path are held). Export Script is likewise a later etap after the data-export chain is solid.

- **Etap 1 — Streaming executor + Preview + Full (complete, self-contained).** This etap ships a *finished, usable* feature with no dead UI. It contains: the streaming executor (`IAsyncEnumerable`); `ExecutionIntent` (all three values defined); Preview (F5) honouring `ExecutionDefaults.PreviewLimit` (hardcoded 5000, settings-ready per A.5); the **truncated-Preview notification bar** (A.6) with a **working `[Load all rows]`** + the `N,NNN+ (preview)` record indicator; **Load all streams the full result into the same grid** (incremental fill + live progress counter + Cancel, reusing the batch-dialog streaming pattern), guarded by a **hard `FullSafetyCeiling`** (reaching it stops + shows a plain ceiling message); client-side view state (sort/filter/aggregation) preserved on load-all; `Shift+F5` = direct Full from a cold start. `[Export all…]` is **NOT** in the bar yet — it appears in Etap 3 when the Export Framework exists (no dead button). *The whole "preview → I want everything → here it is, cancellable, capped" loop works end-to-end at the close of Etap 1.*
- **✅ Etap 2 (SHIPPED 2026-07-08) — Full: smart guarding + large-result UX.** Developed Full's behaviour (no new capability): the **smart soft confirm threshold** (`ExecutionDefaults.FullSoftThreshold`=250k, mid-stream, asked once when crossed with more remaining — "Keep loading / Stop here"; "Stop here" keeps the partial as a truncated result so the notice bar reappears; A.6.1) below the hard `FullSafetyCeiling`; thousands-separated counts across the large-read strings (loading counter, notice, ceiling, completion, preview record indicator); the ceiling-hit message stays distinct. The up-front-estimate prompt path is for server-paged sources (a later etap's consumer) — the SQL editor uses the mid-stream prompt, exactly as A.6.1 specifies.
- **Etap 3 — Export Framework core + SQL results consumer (CSV / TXT / Clipboard).** Contracts (`IExportDataSource`/`IExporter`/`IExportSink`/scope/options) + pure exporters + `ExportService` + the single `ExportDialog` + `QueryResultExportSource` (smart cached-vs-re-run, B.6). Lights up the banner's `[Export all…]`. *Proves the whole framework with zero external deps.*
- **Etap 4 — XLSX exporter.** Decide the library (see risks), streaming writer, register it, wire into the dialog. *Headline format; isolated after the framework is proven because it carries the only real dependency risk.*
- **Etap 5 — Activity Monitor consumer (+ then other grids).** `RowBufferExportSource` for Activity Monitor scopes (Visible/filtered, All=buffer, Selected). Then `ServerPagedExportSource` (Table/View Data, filter+order-aware) and the Performance grids — each a small adapter. *AM is the named priority consumer; the rest follow the same 1-adapter pattern.*
- **Later — Benchmark** (Execute-discard-count-time + Performance whole-set capture; command/menu + Ctrl+Shift+F5) and **Export Script** (INSERT → UPDATE/DELETE → MERGE-if-it-earns-it).

---

## Risks / open architectural questions

1. **Memory on Full/AllRows** — millions of rows in the grid. Mitigate: streaming + `FullSafetyCeiling` + confirm; Benchmark for timing-without-materializing; export streams source→sink without a second buffer (server-paged export re-fetches page-by-page straight to the writer).
2. **XLSX library + streaming** — no lib today. Candidates: **ClosedXML** (MIT, high-level, easy — but builds more in memory) vs **OpenXML SDK** (`DocumentFormat.OpenXml`, SAX/streaming writer — lower-level, best for large exports) vs hand-rolled SpreadsheetML zip (too much). Decide + verify .NET/Avalonia compatibility at Etap-2 start. **XLSX hard limit = 1,048,576 rows/sheet** → detect, warn, optionally split sheets.
3. **Re-run export tx/consistency** — runs on the user's connection/tx (sees their uncommitted edits, not another session's; can differ from the shown grid if data changed). Be explicit ("re-read now"). Must hold `CommandLock` (single FbConnection); must not disturb the user's tx state / must not autocommit — decide whether the export re-read runs on the metadata lane / implicit read so it doesn't bump the working-tx statement counter.
4. **Long export UX** — must be cancellable + off-UI-thread; reuse the batch dialog's streaming+cancel+progress.
5. **CSV encoding trap (sharp)** — Excel CSV wants a **UTF-8 BOM**; `.sql` must **not** have one (gotcha #178). Encoding is **per-format**; do not reuse `SqlFileWriter` for CSV. Also pl-PL delimiter (`;`) + decimal (`,`) defaults.
6. **Clipboard size** — huge payloads choke the clipboard; cap/warn on very large clipboard exports (steer to file).
7. **Export Script keys for arbitrary SELECTs** — joined results have no single table/PK; user must specify table + keys for UPDATE/DELETE/MERGE; warn on ambiguity; INSERT needs neither.
8. **Scope capability matrix** — not every source supports every scope (AM can't re-fetch AllRows beyond its buffer; a non-multi-select grid has no SelectedRows). The dialog must gate scopes on `ExportCapabilities`, not assume.
9. **`CommandLock` held for the whole Full / Load-all read** (final-pass finding) — a multi-minute full load holds the data connection's `CommandLock` for its entire duration, blocking other *same-lane* commands (a second F5, a data-tab reload). **Acceptable**: it's an explicit user-initiated "load everything" with a visible progress+Cancel, and metadata/autocomplete/tree browsing run on the **separate metadata connection (lane #2 + its own lock)** so the app doesn't freeze — the user can still navigate while a big load streams. No mitigation needed beyond Cancel; noted so it isn't mistaken for a bug.
10. **Load-all preserves client-side view state** (final-pass finding) — a preview the user sorted/filtered/aggregated client-side must, after Load all, re-apply that same sort/filter/aggregation to the full set (see A.6). Load all is "more rows", not "reset the view". The materialized-grid filter/aggregation engine already operates on the in-memory set, so this is re-running the existing pipeline over the larger set, not new logic — but it must be wired, not forgotten.

---

## Status: FROZEN (ready for implementation) — re-frozen 2026-07-08 after the A.6↔Etaps reconciliation

All open questions resolved with the user:
- **Execute UX** → Variant **A + D**: one `Execute` button, `F5`=Preview, `Shift+F5`=Full (tooltip only), truncation bar is the primary path to Full. No twin buttons, no split-button, no mode dropdown. (A.3)
- **Preview limit** → hardcoded default (**5000**) via `ExecutionDefaults`, structured to move to `UserSettings` later in one line. No user config now. (A.5)
- **Full lands COMPLETE in Etap 1** (the reconciliation): the truncated bar ships a **working `[Load all rows]`** in Etap 1 — streaming full load into the grid, progress counter, Cancel, **hard `FullSafetyCeiling`**, `Shift+F5` cold-start. No dead button. `[Export all…]` is added in Etap 3 (when the framework exists). (A.6)
- **Etap 2 (SHIPPED 2026-07-08) refines Full, does not add it** → smart *soft* threshold (250k, mid-stream "Keep loading / Stop here"), thousands-separated large-read counts, distinct ceiling-hit messaging. The *capability* is Etap 1; the *intelligence* is Etap 2. (A.6.1)
- **Export Framework** → one shared infrastructure, per-module adapters, formats = Excel/CSV/TXT/Clipboard, Script later. Estimated row count shown up front from `ExportCapabilities`. (B)
- **Export dialog Format/Destination redundancy** → RESOLVED (Etap 3): "Clipboard" is a Format; the separate Destination radio is dropped (format determines file-vs-clipboard). See the RESOLVED note under B.8.
- **SQL-result "All rows" re-fetch** → a true **streaming** read (`FirebirdQueryExecutor.StreamAsync`, `IAsyncEnumerable`, the A.4 enabling change) on the **data lane** (B.6 "sees uncommitted edits"; resolves Risk #3 toward the B.6 Decision), so a large export streams source→sink→file with no second buffer and is NOT bounded by `FullSafetyCeiling` (A.6 "streams past the ceiling straight to file"). The cached/untruncated path yields the in-memory rows directly (no re-run, no second buffer).
- **Scope** (Visible/Selected/All) → confirmed, and kept reusable for future non-export data operations. (B.2)
- **Benchmark** → deferred to a later etap (enum reserved now).

**Etap order:** **✅ ① Preview + Full (complete)** → **✅ ② Full smart-guarding/large-result polish** → **🔨 ③ Export Framework (core + CSV/TXT/Clipboard, + `[Export all…]`)** ← IN PROGRESS → **④ XLSX** → **⑤ Activity Monitor**. Then, later: Benchmark, Export Script, remaining grid consumers.
