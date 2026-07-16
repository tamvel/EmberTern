# EmberTern — Claude Code Context

A modern desktop developer workbench for Firebird database developers, built with **.NET 9 + Avalonia 12**. Target users: ERP and backend devs who work daily with SQL, procedures, triggers, metadata, and transactions. Design philosophy: **less features, better experience; workflow quality over feature count; transaction-aware by default**.

Master prompt / V1 blueprint: `C:\Users\grzegorz.gronski\Desktop\embertern-claude-code-prompt.md`
Target UI mockup: `C:\Users\grzegorz.gronski\Desktop\UI koncepcja.png`

---

## Documentation map (read this first)

CLAUDE.md was, for a long time, both the project's operating manual *and* its diary — every
milestone got appended in full, and the file grew to the point where simply starting a new
session against it consumed roughly half the available context budget before any work began.
On 2026-07-11 it was split by a **Documentation Cleanup Sprint** into a small set of documents,
each with one job. Nothing was deleted — every word that used to live here still exists,
verbatim, in the archive below.

| Document | Job | Loaded automatically? |
|---|---|---|
| **`CLAUDE.md`** (this file) | Rules, current architecture pointers, a short "what's built" inventory, current state, live gotchas that matter to almost every session. | Yes — every session. Keep it short on purpose. |
| **`docs/design/editor-architecture.md`** | The SQL/PSQL editor's current architecture: components, public API, binding decisions, roadmap. Kept current — extend it, don't let it re-accumulate history. | Only when working on the editor. |
| **`docs/design/editor-ast-deepening.md`** | **Active implementation guide** for **Etap 6.9 — Structural AST Deepening** (the next foundational work: node inventory, migration contract, milestones B0–B5, debugger considerations, formatter convergence, progress matrix). | When working on the parser/AST/binder deepening. |
| **`docs/design/editor-stage7-diagnostics.md`** | **Active design/vision** for **Stage 7 (Diagnostics)** — engine, model, categories, pipeline, squiggles/panel/nav, milestones, post-Stage-7 Quick Fixes. Consumes Etap 6.9. | When working on Stage 7. |
| **`docs/design/editor-language-expansion.md`** | **FROZEN design** for the code-writing experience that replaced Stage 8 M2: **Language Completion** (construct completion by natural prefix, Tab + shown hint, grammar-armed, synchronous) + **Typing Ergonomics** (`begin…end` pairing, auto-indent; Enter stays normal), separate from IntelliSense. | When working on `Core.Sql.Language.Constructs` or the completion/ergonomics wiring. |
| **`docs/gotchas.md`** | The **complete** gotcha catalog (~190 entries, #1–#202), organized thematically. CLAUDE.md keeps only the ~20 most load-bearing ones inline; this is where the rest live. | On demand — search it when a bug "feels familiar". |
| **`docs/history/`** | The full narrative archive — every milestone, session, and investigation, split into ~15 thematic files with an index (`docs/history/README.md`). This is the "diary" that CLAUDE.md used to be. | On demand — read a file when you need the backstory on a specific feature or bug. |
| **`docs/design/*.md`** (other files) | Frozen feature-specific design docs (Script Executor, Execution Modes + Export Framework, the Etap-1 tokenization audit) — mostly already implemented; kept as reference. | On demand. |
| **`memory/*.md`** (Claude's persistent memory, outside the repo) | Cross-session recall — rules, gotchas, and project facts Claude chose to remember. `memory/MEMORY.md` is the always-loaded index; the individual files load only when relevant. | Index only, every session; files on demand. |

**Rule of thumb for future work**: if you're about to append a multi-paragraph "shipped"
narrative to CLAUDE.md the way the old file did, stop — put the narrative in a new
`docs/history/` file (or extend the most relevant existing one) and add at most a **one-line**
pointer here (or nothing at all, if the "Current state" section below already covers it). If
you're about to add a gotcha, put its full text in `docs/gotchas.md`; only promote it into
CLAUDE.md's short list if it's the kind of thing that would bite almost *any* future session,
not just one working in a specific module.

---

## Build, test, run

```powershell
# from project root
dotnet build EmberTern.slnx
dotnet test  EmberTern.slnx
# run the app
src\EmberTern.App\bin\Debug\net9.0\EmberTern.exe
```

Solution file is `.slnx` (not `.sln`) — .NET 10 default. App AssemblyName is `EmberTern`, so avares URIs use `avares://EmberTern/...`. `Directory.Build.props` sets `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true` for every project.

## Laboratory Database

A single, persistent Firebird lab database for hand-verifying EmberTern behaviour against a real engine — **use it instead of guessing how Firebird behaves.**

> The Laboratory Database is a long-lived development asset. Future sprints should extend the existing EmberTern_Lab database with new object types and edge cases instead of creating temporary one-off databases.

- **Location**: `Lab/EmberTern_Lab.fdb` — committed to Git (intentionally, for now; if it grows significantly we revisit and may switch to a `setup.sql`-only model). Canonical recreate script: `Lab/setup.sql`.
- **Reference engine**: the local **Firebird 5.0** `DefaultInstance` on **localhost:3050** (FB3 is on 4050 and is **not** used for the lab — we keep ONE lab DB, not an FB3/FB5 matrix). `isql.exe` lives at `C:\Program Files\Firebird\Firebird_5_0\isql.exe`. SYSDBA password is the local dev password.
- **DB settings**: dialect 3, **default charset WIN1250** (matches the user's real environment). Identifiers in `setup.sql` are ASCII so the script runs under any client charset.
- **Purpose**: a development aid, not a test framework and not a compatibility matrix. It carries a small, representative object zoo: 8 domains, 5 tables, 3 views, 2 standalone procedures + 2 standalone functions, 3 triggers, 3 generators, 3 exceptions, 3 roles, 1 package (with body, containing 1 function + 1 procedure), plus a little sample data. Covers PK / FK / composite PK / unique / computed columns / identity (BY DEFAULT and ALWAYS) / domain-typed columns / SUSPEND / CASE / nested BEGIN-END / before-insert / before-update / after-update / COMMENT ON, etc.
- **Use it from EmberTern**: add a connection profile → host `localhost`, port `3050`, database `C:\Dane\C#\Źródła\EmberTern\Lab\EmberTern_Lab.fdb`, SYSDBA, charset WIN1250, dialect 3.

**The rule (enforce on yourself):** before implementing or fixing anything touching Firebird **metadata, DDL, dependencies, or SQL semantics**, prefer verifying the actual engine behaviour against `Lab/EmberTern_Lab.fdb` (via EmberTern or via `isql` at an ASCII path — see the gotcha below) rather than assuming how Firebird behaves. Several past milestones were corrected only after checking a live DB (e.g. gotchas #46, #147, #148).

**Extending the lab**: edit `Lab/setup.sql` (keep object identifiers ASCII), then recreate the `.fdb` (below) and re-run the verification, OR apply the change live (EmberTern / `isql` at an ASCII path) and mirror it back into `setup.sql` so the script stays canonical. Keep it simple — this is a dev asset, not infrastructure.

**Recreate `Lab/EmberTern_Lab.fdb`** (Windows; the build-in-temp-then-copy method — forced by the path gotcha #149):
```powershell
# 1. copy the canonical script to an ASCII path (isql can't reach the repo path)
copy "Lab\setup.sql" "C:\Temp\setup.sql"
# 2. build at an ASCII temp path: CREATE DATABASE then INPUT the script
#    (put both lines in a small build.sql and run: isql -b -i C:\Temp\build.sql)
#      CREATE DATABASE 'localhost/3050:C:\Temp\embertern_lab_build.fdb'
#        USER 'SYSDBA' PASSWORD '<pwd>' DEFAULT CHARACTER SET WIN1250;
#      INPUT 'C:\Temp\setup.sql';
#      COMMIT;
& "C:\Program Files\Firebird\Firebird_5_0\isql.exe" -b -i C:\Temp\build.sql
# 3. copy the finished file into the repo (filesystem copy is Unicode-safe)
copy "C:\Temp\embertern_lab_build.fdb" "Lab\EmberTern_Lab.fdb"
```

**Gotcha — promote to architecture lore.**

149. **`isql.exe` cannot connect to a database whose path contains non-ASCII characters (the repo path has `Źródła`); EmberTern's managed driver can.** Passing the path on the `isql` command line (or via `CONNECT`/`CREATE DATABASE`/`INPUT` of a non-ASCII path) mangles it at the shell/ANSI encoding boundary — the FB5 server receives garbage (`Źródła` → `ťRãDłA`) and reports "path not found" (SQLSTATE 08001 / I/O error on CreateFile). The `#` in `C#` is fine; only the non-ASCII letters trigger it. **But the path is fully usable everywhere else**: the Windows filesystem, .NET file APIs (`Directory.Exists`/`File.Copy`), and — crucially — **`FirebirdSql.Data.FirebirdClient` (what EmberTern uses)** all handle it correctly, because the managed driver sends the database path as Unicode over the wire rather than through a shell. Consequences: (a) the lab `.fdb` is **built at an ASCII temp path with `isql`, then copied into `Lab/`** (a plain file copy — the file is location-independent); (b) any `isql` script that `INPUT`s `setup.sql` must reference an **ASCII copy** of it, not the repo copy; (c) EmberTern connects to `Lab\EmberTern_Lab.fdb` directly with no issue — verified end-to-end (create + attach + DDL + metadata read all succeed via the managed driver at the non-ASCII path). Rule: never use `isql` against a non-ASCII database path on Windows — build at an ASCII path and copy, or drive the managed driver.

## Project layout

Top-level shape (refreshed 2026-07-11 against the real tree — the per-file annotations below
predate most of the modules in "What's built" above and are kept only as illustrative examples
of each project's role, not a complete file listing):

```
EmberTern.slnx
Directory.Build.props        # net10.0, Nullable=enable, TreatWarningsAsErrors=true
src/
  EmberTern.Core/            # zero Avalonia dependencies, zero FirebirdSql dependency
    Connections/ Diagnostics/ Export/ Metadata/ Performance/ Query/ Scripting/
    Search/ Security/ Settings/ Sql/ (incl. Sql/Language/ — the Lexer/Parser/AST/
    Semantics/Completion/Navigation/Highlighting/Signatures/Snippets front-end)
    Trace/ Transactions/ Workspace/
  EmberTern.Firebird/        # all `Fb*` driver types live here; readers return Core DTOs
    (NuGet: FirebirdSql.Data.FirebirdClient 10.3.4; InternalsVisibleTo: EmberTern.Tests)
    e.g. FirebirdConnectionService.cs / FirebirdQueryExecutor.cs / TransactionService.cs /
    FirebirdMetadataReader.cs / FirebirdDdlReader.cs / FirebirdCatalogReader.cs /
    FirebirdTraceService.cs / FirebirdSessionReader.cs / FirebirdScriptExecutor.cs
  EmberTern.Export.Office/   # the ONE place a NuGet dep is allowed for export (XLSX only);
                             # DocumentFormat.OpenXml, streaming writer
  EmberTern.App/             # WinExe, Avalonia 12.0.3, CommunityToolkit.Mvvm 8.4.2
    Program.cs, App.axaml(.cs), UiStrings.cs, app.manifest
    ViewModels/ Views/ Themes/ (Colors.axaml + ControlStyles.axaml — the ONLY theme sources)
    Behaviors/ Completion/ Controls/ Converters/ Diagnostics/ Export/ Security/ Sql/
    Assets/ (FirebirdSql.xshd + .Light.xshd, Branding/, Icons/ — SvgIcon geometries)
    (NuGet: Avalonia.AvaloniaEdit 12.0.0, Avalonia.Controls.DataGrid 12.0.0)
tests/
  EmberTern.Tests/           # xunit; run the headless-probe test class as its own
                             # `dotnet test` partition — see "Live gotchas" (#94)
```


## What's built — feature inventory

A map of what exists, not a narrative of how it got built (that's `docs/history/`). Every item
below is shipped and working unless marked otherwise. Reference is `docs/history/<file>` unless
noted.

- **Connections & sidebar** — encrypted (DPAPI) connection profiles, folders, drag & drop
  reorder, a flat-list (non-`TreeView`) Object Explorer with lazy count-only load, type-to-filter,
  and per-category context menus (New/Edit/Delete/Execute/Activate-Deactivate/Recompile).
  *(history: 01, 08, 09)*
- **SQL Editor** — F5/Ctrl+Enter execute (Preview/Full modes with a streaming executor and a
  1M-row safety ceiling), manual transactions (no autocommit, ever), Saved Queries panel,
  Execution Metrics (per-table read/write counts, a live elapsed timer), and the new AST-based
  formatter. Find/Replace via AvaloniaEdit's built-in `SearchPanel`. *(history: 01, 02, 10)*
- **Metadata browsing + DDL preview/export** — read the reconstructed DDL for any object;
  `Icon.Save` exports a complete, portable `.sql` script (structure + `COMMENT ON`, UTF-8 no-BOM).
  *(history: 01, 12)*
- **Per-object detail editors** (Table, View, Procedure, Function, Trigger, Package, Domain,
  Generator, Exception, Index) — each a dedicated multi-tab surface following one shared editor
  contract (see "Architecture rules" below): buffered structural edits for Table (edit → Compile
  → autonomous auto-commit), Source⇄Easy mode for Procedure/Function/Trigger/View, a Revert/
  Discard button with confirmation on every editor, native per-kind workspace persistence.
  *(history: 03, 04, 07, 09)*
- **Transactions & connection lanes** — THREE `FbConnection`s per profile, one responsibility each
  (rewritten 2026-07-14, history 15): **Data** carries everything the user runs by hand (SQL Editor
  F5 — queries *and* DDL — table-data edits, Execute Procedure, Script Executor) and holds **THE one
  user working transaction** (auto-begin, never auto-commit, NOWAIT, one Commit / one Rollback);
  **Metadata** is **read-only** catalog browsing with implicit per-command transactions and owns no
  transaction (`MetadataLane`); **Ddl** carries object-editor Compile only — autonomous,
  auto-committed, **WAIT**-bounded, with the per-connection **Developer Mode** toggle setting how
  long it waits for an object another *session* holds. The SQL Editor is a classic Firebird console:
  no routing by statement kind, no hidden second transaction. *(history: 05, 13, 15)*
- **Settings & security** — every user setting (connections, folders, workspace, grid layouts,
  parameter history) lives in one whole-file DPAPI-encrypted `settings.dat`, with a versioned
  container header and forward-compatible schema migration. *(history: 05)*
- **Data grids** — shared filter panel + aggregation bar + Record-N-of-M indicator across all 5
  data-bearing grids (SQL Results, Procedure/Function Results, Table Data, View Data); client-side
  for materialized grids, SQL push-down for server-paged ones. Export to CSV/TXT/Clipboard/XLSX
  via one shared `EmberTern.Core.Export` framework. *(history: 10, 12)*
- **Global Search** — search metadata by name and by source/field/message content
  (server-side `CONTAINING`), 2-panel results with a live DDL preview. *(history: 12)*
- **Script Executor, Recompile Dependents, Smart SQL Parameters** — run a multi-statement `.sql`
  script under one caller-controlled transaction (via the driver's `FbScript` parser); after
  compiling an existing object, optionally recompile its direct dependents; F5 on a statement with
  `:name`/`@name` placeholders opens a typed parameter-collection dialog before binding and
  running it. *(history: 12)*
- **Security Manager** — Users / Roles / Membership / object & column privileges, immediate-apply,
  contextual one-click grant/revoke (cell / row / column / all-visible). *(history: 09)*
- **Database Activity Monitor (Trace)** — a live, grouped, filterable view of everything the
  connected database is doing (via the Firebird Services Trace API), with per-table read bars and
  an "open the traced SQL in the editor" bridge into Performance Analysis. *(history: 11)*
- **Session Manager** — live `MON$`-based view of attachments and transactions, with two health
  detectors (long-running transaction, garbage-collection risk) and a Disconnect action.
  *(history: 11)*
- **Performance Analysis** — profile a query's execution plan + measured per-table reads, with a
  measured-first advisor (6 rules) producing confidence-scored findings, investigation guidance,
  and improvement recommendations — never automatic fixes. *(history: 10)*
- **SQL/PSQL Editor language front-end** — the current, actively-developed rebuild: one shared
  Lexer → Parser → AST → Semantic Model in `EmberTern.Core.Sql.Language`, with completion,
  signature help, snippets, navigation (Ctrl+hover/Ctrl+Click, Peek Definition, safe local
  rename, find references), semantic highlighting, and Quick Info all built as *clients* of that
  one model. See **`docs/design/editor-architecture.md`** for the current architecture and
  **"Editor Architecture — current direction"** below for status.

## Current state

- **Active branch: `feat/editor-language-frontend`** — holds the editor-language-front-end rebuild
  (Etaps 0–6 + UX Polish incl. P8), the 2026-07-14 **UX & Stabilization Sprint** (transaction/
  attachment model rewrite), **and** a 2026-07-14 **UX Polish follow-up sprint** (below). Not yet
  merged to `master`.
- **UX Polish follow-up sprint (2026-07-14) — DONE** (4 tasks + 2 review fixes, separate commits):
  **(1) trigger context variables** — NEW/OLD/INSERTING/UPDATING/DELETING get a distinct semantic
  highlight (new `SemanticHighlightClass.ContextVariable`, higher-chroma amber `#E5C07B`). Done
  through the semantic model, not an editor exception: the binder declares the predicates
  (`TriggerPredicateSymbol`) into the trigger's routine-body scope, so they resolve ONLY inside a
  trigger — none are reserved words. **Review fix:** the trigger DETAIL editor edits a body-only
  `begin…end` (no CREATE TRIGGER header), so `TriggerDetailTabViewModel.BuildAmbientSymbols` now seeds
  NEW/OLD (bound to the table) + the predicates as **ambient symbols** — the same seam the routine
  editors use for out-of-text params/vars (gotcha #218). **(2) occurrence highlight** retinted from
  warm gold (→ muddy brown on dark) to a subtle accent-blue wash (theme token `OccurrenceHighlight*`).
  **(3) formatter WITH/CTE** — **now AST-modelled**: the parser builds a `WithClause` +
  `CommonTableExpression` nodes (`Ast/CteNodes.cs`; `SelectStatement.With`), and the formatter
  *consumes* the AST (no token-level CTE parsing in the formatter). Set operators (UNION [ALL]/
  INTERSECT/EXCEPT) break onto their own line via the one `MatchStructuralPhrase` mechanism. A CTE
  query is one statement — no blank line before the main SELECT. **(4) Easy-mode DDL casing** — new
  `DdlGenerator.PresentIdentifier` folds a picked domain to UPPERCASE + bare in generated DDL (regular
  ASCII identifiers only — §0-safe; special/case-sensitive names preserved verbatim + quoted), kept
  distinct from `SqlFormatter` (which preserves its own casing on existing source).
- **Build**: 0 warnings / 0 errors (`TreatWarningsAsErrors=true`). **Tests**: 3666 main + 23
  headless-probe (run the `ConnectionExpandBindingProbe` headless class as its own `dotnet test`
  partition — it intermittently hangs alongside the rest of the suite; both partitions pass
  independently). Smoke: clean (app launches). *(Trigger-highlight + occurrence colours are proven by
  headless model/classifier tests + chosen per the documented palette; the on-screen appearance awaits
  the user's visual confirmation per the QA rule.)*
- **⚠ `FirebirdScriptExecutor` is KNOWN-BROKEN for its primary use case** — it runs the whole script
  in ONE transaction and its docstring claimed mixed DDL+DML migration is "all-or-nothing", which is
  **false** (gotcha #213: a Firebird transaction cannot use an object it created but has not
  committed). A deployment script that creates and then populates anything fails at the second
  statement. It also still carries the last *"Commit or roll back the active transaction…"* guard,
  and duplicates the classifier (its `FirebirdScriptParser.MapKind` uses the **driver's** statement
  enum, while Core has the AST-based `SqlStatementClassifier`). **Deferred to a dedicated sprint by
  user decision** — the Core classification infrastructure was deliberately KEPT, not deleted, as the
  foundation of that future execution engine. See `docs/history/15-...`.
- **Verify Firebird behaviour, never infer it.** Three long-standing architectural beliefs were
  falsified by ~30 lines of probe against the Lab DB this sprint (#213, #214, #215). If a design
  rests on "Firebird does/doesn't allow X", measure it first.
- **QA rule (2026-07-12, user directive):** a package is NOT "fixed" on green build/tests/smoke
  alone. If a fix can't be verified **visually in the running app**, report it as "implementation
  done — awaits user confirmation", never "fixed". Trace flows to ground truth, don't guess.
- **Parameter Helper — DONE + UNIFIED (gotchas #206–#210).** One `ParameterHelper` (App/Completion,
  OverlayLayer-hosted, source of truth = `SignatureHelpEngine`) shows the parameter list of whatever
  call/DML site the caret is at — **INSERT / UPDATE OR INSERT / EXECUTE PROCEDURE / function** — with
  the active parameter a solid accent pill and IN/OUT for routine params. Both triggers feed it: a
  **double-click** on a value (`NavigationController` → `SqlCompletionController.TryShowParameterHelperAt`)
  and **typing** an argument list (`(`/`,`/`)` / Ctrl+Shift+Space) — the old M7 `OverloadInsightWindow`
  is gone. Lifetime is **context-driven**, not offset-driven (#210): on each caret move it re-queries
  the engine and stays open while still the same site (kind+target), following the active argument,
  closing only on a real context change / Escape / detach. The journey (all fixed): wrong offset (caret
  vs pointer, #206), columns not warmed (#204), bare `Popup`/`PopupRoot` invisible on the desktop →
  OverlayLayer (#209). Engine: `SignatureHelpEngine` now treats `StatementKind.UpdateOrInsert` like
  `Insert`. All temporary `EditorDiagnostics` instrumentation removed (code clean). **The hover tooltip has
  since been migrated to OverlayLayer too** (the Unified Hover milestone — done up front, before plain hover
  made it the primary discovery surface, rather than after a bug report); `ClampIntoOverlay` now lives in
  `EditorPopups`, shared by both cards. The remaining custom popups (Ctrl+Space Quick Info, Peek, rename)
  still use the bare-Popup pattern — migrate to OverlayLayer if they show the same invisibility symptom.
- **Multi-statement root cause FOUND & FIXED (gotcha #208):** the user's real problem was that several
  statements in one editor **separated only by newlines (no `;`)** collapsed into ONE parser statement
  (`ScanPlain` ends only at a top-level `;`), so only the first was analysed (coloured/nav/Quick Info).
  Fix: a **lenient** parse for the READ-ONLY semantic model only — `SqlParser.Parse(text, lenient:true)`
  wired into `SemanticModel.Build(string)` — that also splits at top-level statement-start keywords with
  continuation guards (`WITH…SELECT`, `INSERT…SELECT`, `…UNION SELECT`, `CREATE VIEW…AS`, `MERGE…WHEN`).
  The strict `;`-only `Parse` (executor boundary authority, gotcha #192) is untouched. Pinned by
  `SemanticModelTests.MultipleStatements_WithoutSemicolons_*`. **User-confirmed fixed live** (all objects
  across every statement now colour/navigate).
- **UX Polish — QA Fix Sprint (2026-07-12).** (1) **Light-theme popup blend — fixed & verifiable:**
  style `aecc|CompletionListBox` (the earlier `aecc|CompletionList` Background was a no-op — that
  control's template never paints its Background). (2) **Double-click INSERT/VALUES helper —
  root-caused & fixed (awaits visual confirm):** the decision→popup flow is proven correct by
  `InsertHelper_DoubleClick_OpensPopup_*`; the live miss was the OFFSET — `OnDoubleTapped` used
  `_editor.CaretOffset` (not reliably on the clicked value when the gesture fires) instead of the
  POINTER offset (now `OffsetAt(e.GetPosition(...))`, gotcha #206). Also added warm-then-retry so
  the helper works when the target columns aren't cached. (3) **View / selectable-proc in FROM not
  coloured — PROVEN not the binder and not the highlighter (gotcha #207):** three probes show the
  binder resolves them given metadata, the highlighter paints the object colour when resolved
  (`SemanticHighlighter.PaintedBrushAt`), and `TextView.Redraw()` genuinely re-runs the colorizer.
  "Ctrl+Click works" is misleading — it has a name-based fallback, so the symptom set (no colour +
  no hover + no Quick Info, yet Ctrl+Click opens it) means the MODEL didn't resolve the object =
  metadata-not-in-snapshot-at-build-time (gotcha #205). Every link of the rebuild chain
  (`DataContextChanged`→`OnDataContextChanged`→`ObjectsChanged`→`NotifyMetadataChanged`→debounce→
  `RefreshModelWithMetadata`→repaint) is re-verified, but the live failure could NOT be reproduced
  headlessly → **awaits user confirmation**; if it persists on a clean rebuild, add runtime tracing
  of the snapshot object-count at model build.
- **Functional development is otherwise PAUSED.** Per explicit instruction: **do not start Etap 7
  or any new feature** until the user says so. (P8 formatter polish is now COMPLETE + architecturally
  closed — see the P8 bullet below.)
- **Package 5 (Quick Info richness) — DONE (2026-07-13).** `ColumnSpec` carries PK/FK/default/
  description/computed/identity; `ObjectMetadata` carries a function's return type + trigger/generator
  header facts; a new proactive warm pipeline (`EditorLanguageService.BeginWarmReferencedMetadata`)
  fills them for every object the current statement references, without requiring a "table." or a
  hover first. Full detail: `docs/design/editor-architecture.md` §15.2/§15.3, gotcha #211 (this
  also generalizes/supersedes the earlier per-character warm-then-retry hacks — there is now one
  metadata cache + one generic warm pipeline). Build 0/0, tests 3449/3449 green.
- **P8 DONE — Formatter Polish + architecturally closed (2026-07-13 → 2026-07-14).** Scope + order
  agreed with the user, all shipped:
  **Krok 0 Formatter Safety (§0) → F shared list builder → INSERT layout → UPDATE OR INSERT layout →
  long-line wrapping → EXECUTE BLOCK → FOR SELECT**, each its own commit with full build + tests +
  round-trip/idempotency. Standing directives for P8: never add a formatter workaround/special-case
  where a small parser/AST deepening is cleaner ("build grammar depth only when a concrete feature
  needs it"); after each step, remove any now-redundant historical workaround rather than leaving
  compatibility layers; report + justify architectural changes per step.
  - **Krok 0 (Formatter Safety) — DONE.** The formatter can no longer lose a token on malformed/
    incomplete input (§0 guarantee). Two layers: each PSQL emitter anti-stall guard now emits the
    unplaced token verbatim (`EmitStrayToken`) instead of silently skipping; and a checked invariant
    wraps `SqlFormatter.Format` — per statement, if the output's lexeme sequence ≠ the input tokens'
    it keeps the statement verbatim, and a script-level backstop returns the input unchanged if the
    whole result still differs. Also fixed a leading-comment drop before `CREATE PROCEDURE`. Detail:
    `docs/design/editor-architecture.md` §15.2, gotcha #212. Pinned by `SqlFormatterSafetyTests`.
    Build 0/0, 3542 main + 23 probe green.
  - **Krok F (shared list builder) — DONE.** ONE token-level mechanism (`SplitTopLevelCommas` +
    `MatchParen` + `FormatBrokenList`/`FormatAdaptiveList`, item content rendered by `Emit`) now lays
    out every "( item, item, … )" comma list. The CREATE VIEW column list — first consumer — was
    migrated onto it and its **bespoke ~40-line character loop was deleted** (net simplification).
    Byte-identical view output; the token-level splitter is comma-safe inside quoted identifiers for
    free. Pinned by `SqlFormatterListBuilderTests`. Build 0/0, 3548 main + 23 probe green.
  - **Krok INSERT (layout) — DONE.** `InsertStatement` now formats as IBExpert-standard: `insert into
    <target> (cols)` on one line, `values (…)` / `select …` / `default values` on its own line,
    `returning …` on its own, `;` glued. Column & value lists ride the shared **adaptive** builder —
    inline while they fit 120 chars, else packed multiple-per-line aligned under the opening paren
    (readability-driven, NOT one-per-line, per user directive). INSERT…SELECT reuses `Emit`.
    **Simplification:** the adaptive-reflow packer `PackWithContinuation` was generalized with a
    `startColumn` param and is now the ONE packing algorithm shared by the token-level list builder AND
    the string-level SELECT/IN wrapping. Pinned by `SqlFormatterInsertTests`. Build 0/0, 3557 main + 23
    probe green.
  - **Krok UPDATE OR INSERT (layout) — DONE.** `FormatInsert` generalized to `FormatInsertFamily(
    List<FToken>, headerLen)` handling BOTH `InsertStatement` (headerLen 2) and `UpdateOrInsertStatement`
    (headerLen 4) — they differ only by the leading verb and the `matching (…)` clause (its own line,
    via the shared adaptive builder). One formatter, two statement kinds. Pinned by the UPDATE OR INSERT
    cases in `SqlFormatterInsertTests`. Build 0/0, 3561 main + 23 probe green.
  - **PSQL leaf-statement unification — DONE (user-requested).** The PSQL body emitter no longer has its
    own INSERT/UPDATE/SELECT formatting: `AddPsqlEmit` now delegates each leaf statement to a shared
    `FormatLeafStatement`, which routes INSERT/UPDATE OR INSERT to the same `FormatInsertFamily` used at
    the top level (SELECT…INTO keeps its PSQL-specific INTO-on-own-line split; everything else → generic
    `Emit`). So an INSERT/UOI inside a procedure, trigger, or EXECUTE BLOCK now lays out identically to
    one at the top level — the divergence the user noticed is gone. The PSQL emitter owns only block
    STRUCTURE (BEGIN/END, IF/WHILE/FOR indentation); statements are formatted once. Pinned by the
    "inside body" cases in `SqlFormatterInsertTests`. Build 0/0, 3564 main + 23 probe green.
  - **Krok long-line wrapping — DONE.** There is now exactly ONE long-line wrapping mechanism, at the
    TOKEN level inside `Emit`: a SELECT column list (`EmitSelectColumnList`) and an `IN ( … )` value list
    (`EmitInList`) are laid out by the shared adaptive builders (`FormatAdaptiveBareList` /
    `FormatAdaptiveList`) using precise column positions from the StringBuilder. **The entire string-level
    post-pass is deleted** — `WrapLongLines`, `WrapLine`, `TryWrapSelectColumns`, `TryWrapInList`,
    `SplitByTopLevelComma`, `FindInOpeningParen`, `FindMatchingClose`, `SkipString`, `SkipQuotedIdent`,
    `LooksLikeSubquery` are all gone (~110 lines). Bonus: wrapping is now consistent inside PSQL bodies
    too (the old post-pass never wrapped indented SELECT lines). Byte-compatible with the old wrapping
    (all pinned SELECT/IN wrapping tests green). Pinned by `SqlFormatterWrappingTests`. Build 0/0, 3568
    main + 23 probe green.
  - **Krok EXECUTE BLOCK (header) — DONE.** `ExecuteBlockStatement` now formats its header instead of
    keeping it verbatim: `execute block (params)` (adaptive list) / `returns (cols)` on its own line
    (adaptive list) / `as` on its own line, all lowercased, then the block-structured body — because
    EXECUTE BLOCK is a *runnable* statement, not persistent DDL (a CREATE definition header stays
    verbatim by design). `FormatExecuteBlock` + `TryFormatExecuteBlockHeader` reuse the shared adaptive
    builder + `Emit` (item content) + `FormatPsqlBody`; any header shape not fully recognised falls back
    to the verbatim-header path (never guess, §0). Pinned by
    `SqlFormatterExecuteBlockAndForSelectTests`. Build 0/0, 3585 main + 23 probe green.
  - **Krok FOR SELECT — DONE.** The PSQL `FOR <select|execute statement> INTO <vars> DO <stmt>` loop was
    previously mangled (`for` split from `select`, `into …` glued onto the `where` line). `EmitForSelect`
    treats **FOR SELECT as one Firebird construct** (user directive — like INSERT INTO): `for` prefixes
    the cursor query's first line (NOT split onto its own line, query NOT extra-indented); the query is
    the shared `Emit` (so its SELECT/FROM/WHERE breaks + long-line wrapping match plain DML); then
    `into <vars>` and `do` each on their own line at the loop indent; body via `EmitPsqlBranch`. INTO and
    DO are found at paren depth 0 (a subquery in FROM never leaks out); malformed input (no top-level DO)
    falls back to the generic statement path (§0). WHILE stays on its own single-line path. Pinned by
    `SqlFormatterExecuteBlockAndForSelectTests`. Build 0/0, 3585 main + 23 probe green.
  - **Call-argument-list wrapping (UX follow-up) — DONE.** A call's argument list now rides the SAME
    shared adaptive builder as INSERT/VALUES/MATCHING/SELECT/IN. New `EmitCallArgList` in `Emit` fires on
    any `name ( … )` where `name` is an identifier/quoted-ident that is not a style keyword (the glue rule
    `NeedsSpaceBefore` already uses to detect a call) — so **EXECUTE PROCEDURE, function/procedure calls,
    and every other call** wrap adaptively under the `(` instead of sitting on one giant line; short lists
    stay byte-identical. No per-construct formatter (explicit user directive — EXECUTE PROCEDURE just
    routes through `Emit` like everything else). A subquery argument is left to the clause break. Pinned
    by `SqlFormatterCallArgumentTests`. Two documented edge limits (both idempotent + lossless): a
    single-item list can't pack (a lone very-long arg won't wrap), and a call nested as a list item wraps
    aligned from its own column-0 render, not its placed column. Build 0/0, 3596 main + 23 probe green.
  - **Final architecture close-out — P8 IS ARCHITECTURALLY CLOSED.** Audited on the user's request:
    (a) **no historical workarounds left** — the string-level wrap scanners are deleted (survive only in
    one explanatory comment), the CREATE VIEW char-loop is gone, all per-character/warm-then-retry hacks
    superseded; (b) **no parallel implementations** — ONE list builder (`SplitTopLevelCommas` + `MatchParen`
    + `FormatBrokenList`/`FormatAdaptiveList`/`FormatAdaptiveBareList`), ONE packing algorithm
    (`PackWithContinuation`), ONE item renderer (`RenderListItems`→`Emit`), ONE long-line wrapping
    mechanism (token-level), and statements formatted in ONE place (top-level == PSQL body via
    `FormatLeafStatement`); (c) every private method is live (no dead code), no transitional names
    (`V2`/`New*`/`Temp`); (d) the residual verbatim paths (CREATE definition headers, UPDATE SET
    per-assignment, MERGE, CASE/expression interior) are **intentional scope boundaries** — grammar depth
    not yet built because no feature needs it — not debt. §0 is a checked invariant (per-statement +
    per-script lexeme preservation), so the formatter either reproduces every lexeme or leaves the
    fragment/document unchanged.
- **What's next — DECIDED (pre-Stage-7 architecture review, 2026-07-14).** A review before Stage 7
  found the AST is a *statement skeleton with token-bag annotations*, and SQL structure is duplicated
  across 3–4 token walkers (formatter ~24 routines, the binder's Query+Psql walks, the legacy
  `SqlAliasResolver`). Decision (user): **build a foundational parser/AST deepening — [Etap 6.9 —
  Structural AST Deepening](docs/design/editor-ast-deepening.md) — BEFORE Stage 7**, at "structural
  depth" (model clauses, subqueries, CTE/nested-CTE, CASE, PSQL control-flow + executable statements;
  keep ordinary expressions as token fragments), foundation-first, migrating the binder first and the
  formatter **one construct at a time** (never a big-bang rewrite; every milestone must strictly
  reduce token-walk logic). This is also the foundation for the future Debugger (every executable
  statement gets a stable node + span). Milestones B0–B5 + progress matrix are in that doc. **Stage 7
  (Diagnostics) follows** and is fully specced in [editor-stage7-diagnostics.md](docs/design/editor-stage7-diagnostics.md)
  (semantic-only engine, `Diagnostic` model, categories, squiggles/panel/nav, incremental refresh,
  Quick Fixes explicitly post-Stage-7). **Etap 6.9 / B0 — DONE (2026-07-14):** pure-refactoring
  scaffolding — new base abstractions `QueryNode` / `PsqlStatement` / `IExecutableStatement`,
  `SqlParser` made `partial` (B1/B2 seam), a §0 differential-test harness (round-trip byte-identity +
  tree well-formedness over a shared corpus), the NUL-byte fix in `SemanticBinder.Query.cs`, and the
  dead alias path removed from `EditorLanguageService`; build 0/0, 3841 main + 23 probe green, smoke
  clean, no formatter/semantic behaviour changed. **Etap 6.9 / B1a — DONE (2026-07-14):** the PSQL body
  node hierarchy (`Ast/PsqlNodes.cs`: `BlockStatement`/`IfStatement`/`WhileStatement`/`ForSelectStatement`/
  `PsqlLeafStatement`, control-flow + leaves implement `IExecutableStatement` = debugger step units) + a
  body sub-parser (`SqlParser.Psql.cs`) that parses an `AnonymousBlockStatement` (the body-only editor
  shape) into a `Body` tree — **additive only** (binder + formatter unchanged; token slice still
  round-trips; spans nest + no token dropped by construction; mirrors the formatter's `EmitPsqlUnit`);
  build 0/0, 3850 main + 23 probe green (+9 `PsqlAstTests`), smoke clean. **Etap 6.9 / B1b-prep — DONE
  (2026-07-14):** reading the binder for B1b showed it walks FOUR PSQL surfaces (CREATE PROC/FUNC, CREATE
  TRIGGER, EXECUTE BLOCK, anon block) + a DECLARE section, so retiring the walker COMPLETELY needs the AST
  to cover them all first. Added (still additive) `ParseRoutineBody` (skip header to top-level `AS`, parse
  declares+block) attaching a `Body` `BlockStatement` to `DdlStatement` (PSQL proc/func/trigger) +
  `ExecuteBlockStatement`, and `DeclareVariable/CursorStatement` nodes + `BlockStatement.Declarations`
  (now exercised); binder + formatter still token-walk (coexistence); build 0/0, 3857 main + 23 probe
  green (+7 `PsqlAstTests`), smoke clean. **Etap 6.9 / B1b — DONE (2026-07-15):** `SemanticBinder.Psql`
  is now a pure **AST consumer** — a visitor (`BindBody`→`BindBlock`→`BindPsqlStatement`, with
  `BindControlHeader`/`BindDeclaration`) traverses the parser's `BlockStatement` body tree, and the
  **complete structural PSQL token walker is DELETED** (`BindRoutineBody`, `ScanDeclarations`,
  `FirstTopLevelBegin`, `FindTopLevelSemicolon`, `ContainsKeyword`, `SkipLocalSubprogram`,
  `MatchingEndExclusive` — ~113 lines of BEGIN/END matching + boundary/subprogram scanning gone). The
  entry points bind only the HEADER (signature) from tokens; the old flat body scan is retained as the
  leaf-INTERIOR reference binder (`BindLeafReferences`, ordinary/query-expression depth = B2/B3) and now
  runs per node-range — identical reference set (every body token is in exactly one node). Behaviour
  delta (documented, negligible): a local `DECLARE PROCEDURE/FUNCTION` body is now traversed against the
  enclosing scope (the old walker skipped it) — rare FB4+ surface, proper sub-routine scoping is B5+.
  Build 0/0, 3864 main + 23 probe green (+3 `SemanticModelTests`), smoke clean; completion/highlighting/
  navigation/Quick Info consume the same model API, unchanged. **Etap 6.9 / B2 — parser-producer DONE
  (2026-07-15):** the **query clause tree** is now produced. New `Ast/QueryNodes.cs` (`SelectQuery` /
  `SetOperationQuery` : `QueryNode`; the `QueryClause` base + `SelectClause`/`FromClause`/`WhereClause`/
  `GroupByClause`/`HavingClause`/`OrderByClause`; the `FromItem` base + `TableReference`/`DerivedTable`/
  `JoinedTable`; `SetOperator`+`JoinKind` enums) + new sub-parser `SqlParser.Query.cs` (`TryParseSelectQuery`
  → clause-boundary scan + comma/JOIN-structured FROM list + left-assoc set operations with a trailing
  ORDER BY on the whole). Wired into `Classify` so a plain (non-`WITH`) `SelectStatement` exposes a `Query`
  child. **Additive only** — binder + formatter UNCHANGED (still token-walk; transitional coexistence),
  token slice still round-trips (§0); shapes not cleanly recognised leave `Query` null (never lost). Depth
  = structural: clause/join interiors stay token fragments; nested subqueries (derived body, EXISTS/scalar,
  CTE body) NOT recursed — that's B3; `WITH`-led queries keep the `WithClause` token bag (main query →
  `QueryNode` in B3, so no double representation). **Dedup:** `PsqlSpan`→shared `TokenSpan` in `SqlParser.cs`
  (one token-range→span helper for both sub-parsers); reuses existing `Sub`/`MatchParenTok`/`Kw`/`At`.
  Build 0/0, 3896 main + 23 probe green (+14 `QueryAstTests`, +5 corpus shapes), smoke clean. **Etap 6.9 /
  B3 — parser-producer DONE (2026-07-15):** the **query model is now fully recursive**. New nodes
  (`Ast/QueryNodes.cs`): `WithQuery` (WithClause CTE-decls + main `QueryNode`), `RawQuery` (query-level §0
  valve), `SubqueryExpression` base + `ExistsExpression`/`ScalarSubquery` (each owning a `QueryNode`).
  Promoted (`Ast/CteNodes.cs`): `CommonTableExpression.BodyTokens`→`Body` (real `QueryNode`); `WithClause`
  dropped `MainQueryTokens` (main now on `WithQuery.Query`) — **no parallel representation**. `QueryNode`
  base gained `Tokens` (pulled up from `SelectQuery`/`SetOperationQuery` — dedup). `SelectStatement.With`
  **deleted** — a WITH-led statement's `Query` is a `WithQuery` (one representation everywhere). Parser:
  `ParseQueryRange` is the single recursive entry (reused by CTE bodies, derived tables, `ParseEmbeddedSubqueries`
  which finds EXISTS/scalar/IN subqueries in clause interiors, descending ordinary parens but never into a
  subquery); clauses/derived-tables/JOIN-ON now carry their subquery children; `TryParseWithClause` deleted
  (WITH parsing consolidated into `SqlParser.Query.cs`). **Formatter:** ONE forced byte-identical accessor
  swap — `FormatWithClause` reads `cte.Body.Tokens`/`wq.Query.Tokens`, dispatcher matches
  `SelectStatement { Query: WithQuery }`; emits the exact same token ranges → output unchanged (proven by
  formatter invariants + idempotency + the per-statement lexeme net). Not a layout migration — the only way
  to promote the WITH token-bag without a parallel representation. Build 0/0, 3913 main + 23 probe green,
  smoke clean. **Etap 6.9 / B3.1 — parser-producer DONE (2026-07-15):** the last "query as a token blob"
  gap is closed — the parser is now the single structural source for **every query reachable from a
  top-level statement or a PSQL control-flow node**. New sub-parser `SqlParser.Dml.cs` attaches a real
  `QueryNode` to: `InsertStatement.SourceQuery` (INSERT…SELECT/WITH) + `.Subqueries` (VALUES/RETURNING
  scalar subqueries); `UpdateStatement`/`UpdateOrInsertStatement`/`DeleteStatement.Subqueries` (embedded
  EXISTS/scalar/IN); `MergeStatement.SourceQuery` (USING (…)) + `.Subqueries` (ON/WHEN/SET/VALUES);
  `DdlStatement.Query` (CREATE/ALTER/RECREATE VIEW…AS body, incl. WITH-led + set-op bodies, mutually
  exclusive with the PSQL `Body`); PSQL `ForSelectStatement.Query` (FOR SELECT/WITH cursor — boundary stops
  at depth-0 INTO / AS CURSOR, never a column-alias AS; null for FOR EXECUTE STATEMENT); PSQL
  `DeclareCursorStatement.Query` (DECLARE…CURSOR FOR (…)). **Additive/producer-only** — binder + formatter
  still token-walk these (convergence deferred, same as B2/B3); every embedded query is modelled ONCE as a
  `QueryNode` (no parallel representation — the statement `Tokens` are the §0 backing, not a second model);
  shared child-ordering in new `Ast/AstChildren.cs`. Also a **B2 robustness fix** (forced by a set-op VIEW
  body): `ParseSetQuery` no longer folds a dangling `… UNION ALL` (lenient-split mid-statement) into a
  degenerate `[0,0)` operand. Build 0/0, **3978 main + 23 probe green** (+`DmlQueryAstTests`, +14 corpus
  shapes), smoke clean; no formatter/semantic behaviour changed. **ONE documented residual (→ B5, §12):** a
  DML/`SELECT…INTO` statement appearing as a PSQL body LEAF stays a `PsqlLeafStatement` (its query token-only)
  — modelling it now would create a parallel DML-query representation; the fix is B5 (leaves → reused DML
  nodes). `EXECUTE STATEMENT '<sql>'` is never a `QueryNode` (runtime string — a permanent boundary, not
  debt).
- **Etap 6.9 / B4 (CASE AST) — parser-producer DONE (2026-07-15):** `CASE … END` (simple + searched, in a
  SELECT expression and in PSQL) is now a `CaseExpression` (+ `WhenClause`) node — `Ast/ExpressionNodes.cs`.
  The B3 clause-interior scan was generalised from `ParseEmbeddedSubqueries` to `ParseEmbeddedExpressions`
  (finds subqueries AND CASE, recursively — a subquery/nested CASE inside a WHEN/THEN/ELSE stays a real
  node); `PsqlLeafStatement` carries the same embedded-expression children so a CASE in an assignment/RETURN
  is modelled too. Additive — the formatter still emits CASE inline (layout is deferred convergence).
- **Etap 6.9 / B5 (Routine/PSQL body = reused DSQL nodes) — parser-producer DONE (2026-07-15):** an embedded
  DSQL statement inside a PSQL body (SELECT/INSERT/UPDATE/DELETE/MERGE/EXECUTE) is now the **reused**
  top-level statement node (with its B2/B3/B3.1 query structure), NOT a `PsqlLeafStatement` — so a DML query
  in a routine body is the SAME node, modelled the SAME way, as at the top level (closes the §12 #1
  residual). Body statement/branch slots widened `PsqlStatement`→`SqlNode`; PSQL-only leaves (assignment,
  SUSPEND, EXIT, LEAVE, POST_EVENT, EXCEPTION, RETURN, subprogram header) stay `PsqlLeafStatement`; the
  reused nodes now implement `IExecutableStatement` (debugger step coverage across every PSQL surface).
  `PsqlLeafKind` dropped its DSQL members. **Binder behaviour-neutral** (a reused node is bound via the same
  `BindLeafReferences` over its tokens as the old leaf scan); **formatter unaffected** (token-based PSQL
  emitter). Build 0/0, **4008 main + 23 probe green**, smoke clean; no formatter/semantic behaviour changed.
  **PARSER STAGE COMPLETE (B0–B5): the parser is the single structural source for all SQL/PSQL structure.
  No parallel AST representation remains.**
- **Etap 6.9 / BINDER CONVERGENCE — DONE (2026-07-15):** `SemanticBinder` is now a full AST consumer. The
  query binder (`SemanticBinder.Query`) reads the `QueryNode` tree (FROM items, WITH/CTE, embedded
  subqueries, clauses); the DML binder (`SemanticBinder.Dml`) reads the DML nodes' source query + subqueries;
  the PSQL binder drives its leaf/header subqueries from the AST (reused-node `Query`, leaf children,
  `IF`/`WHILE` `ConditionExpressions`, `ForSelectStatement.Query`). **Structural token walkers DELETED:**
  `BindQuery` (token), `CollectTables`, `ParseTableList`, `ParseCteList`, `BindColumnReferences`
  (FROM+`(SELECT` re-scan), `BindNamedTable`/`BindDerivedTable`/`BindTargetAfter`/`ReadOptionalAlias`,
  `IsTableListTerminator`/`TableListTerminators`, `BeginsSubquery`, and the PSQL `BindLeafReferences`/
  `FindBodySelectEnd`/`BindOptionalInto`. Only expression-level token work remains (column/local/param refs +
  DML-target identification, which has no AST node) + two producer refinements (`IF`/`WHILE`
  `ConditionExpressions`; PSQL `SELECT … INTO` ends its `QueryNode` before `INTO`). Behaviour-equivalent:
  build 0/0, **4008 main + 23 probe green**, smoke clean.
- **Etap 6.9 / FORMATTER CONVERGENCE — DONE (2026-07-15) → ETAP 6.9 IS CLOSED.** `SqlFormatter` is now an
  AST-walking layout engine wherever the parser provides structure. Landed construct-by-construct (never
  big-bang): **F1** nested-query indentation (derived table / EXISTS / scalar subquery / IN(SELECT) as
  expanded-paren blocks) + the projection item model (a CASE/subquery item owns its layout without forcing
  neighbours one-per-line); **F2** adaptive CASE (`CaseExpression` → inline when ≤1 WHEN and it fits, else a
  WHEN/THEN/ELSE block); **F3** WITH/CTE bodies recurse through `EmitQuery`; **F4** INSERT…SELECT source,
  CREATE VIEW body, MERGE `USING (…)` source, UPDATE/DELETE embedded subqueries; **F5** PSQL body leaves +
  FOR SELECT cursors delegate to the AST (a leaf-span index bridges the token block-structurer to
  `FormatAstLeaf`). Core mechanism: everything renders **column-0-relative** and composes by uniformly
  shifting a block right (`AppendBlock`/`IndentBlock`), so a **flat query stays byte-identical** (all
  pre-existing exact tests unchanged) while a nested query gains real indentation; layout is a pure function
  of the tree ⇒ idempotent; the §0 lexeme net is unchanged. New tests: `SqlFormatterNestedQueryTests`,
  `SqlFormatterEmbeddedQueryTests`, `SqlFormatterPsqlAstTests` + a `StructuralConstructs` §0/idempotency
  sweep. **The token emitter is retained by design, not as debt:** it is the clause/expression INTERIOR
  renderer (structural-depth boundary — ordinary expressions stay token fragments), the layout for the
  constructs the parser intentionally does NOT model (UPDATE SET / DELETE / MERGE clause layout — a §12
  boundary; PACKAGE bodies — no `Body` node), and the robust PSQL block structurer (malformed-input safe).
  **One layout mechanism per construct — no parallel AST + token walker for the same construct.** The
  reported formatting problems are fixed: **CASE** lays out (adaptive), **WITH** and **multi-level nested
  queries** indent naturally. **Three follow-up fixes closed reported gaps** (all pinned by tests): (a)
  subqueries in function-call args / CASE arms / any derived table now nest at exactly +1 (not the
  enclosing paren's column) — the shared list builders thread structural children, and `EmitFromClause`
  goes structural for ANY derived table; (b) a **bare `IF`/`WHILE`/`FOR` fragment** (no enclosing
  BEGIN…END — a selection lifted from a body) is recognised as an anonymous PSQL body (`Classify`) so it
  formats instead of falling to a verbatim `RawStatement`; (c) a PSQL **`SELECT…INTO` leaf's leading
  comment is no longer duplicated** (the AST leaf renderer re-materialised the comment the block
  structurer already emitted → the duplicate tripped the §0 net and reverted the WHOLE routine to verbatim
  — the "the whole procedure didn't format" symptom). Build 0/0, **4070 main + 23 probe green**, smoke
  clean; user-confirmed on a real procedure. **ETAP 6.9 IS COMPLETE — parser + binder + formatter all
  consume one AST model.** **Stage 7 (Diagnostics) has since begun — see the Stage 7 bullet below.**
  `SqlAliasResolver` is off the editor path (only `PredicateExtractor`/Performance uses it).
  Still deferred: **P5d** a plain-hover info cue — now **folded into the post-Stage-7 "Unified Hover
  Information" backlog item** (do NOT ship P5d separately: it builds the same plain-hover surface, dwell
  delay and noise budget the unified hover needs — see `editor-stage7-diagnostics.md` §15); **P2c** bold
  the typed completion-fragment (no clean AvaloniaEdit 12.0.0 path yet). Formatter grammar-depth items now folded into Etap 6.9 as node
  consumers: **CASE** (was inline/verbatim), **nested-query indentation** (no indent model today),
  and eventually **UPDATE SET** / **MERGE … WHEN** if a feature needs them; CREATE-definition headers
  stay verbatim by design. Immediate hygiene noted for Etap 6.9: a literal NUL byte in
  `SemanticBinder.Query.cs` (composite cache key written as a raw `\0`), and the dead alias path in
  `EditorLanguageService` (no consumer since Etap 5/M5 — remove once validated).
- **Stage 7 (Diagnostics) — COMPLETE** (S5 impl done 2026-07-16; awaits the user's visual confirmation per
  the QA rule). Design/vision + as-built: [editor-stage7-diagnostics.md](docs/design/editor-stage7-diagnostics.md).
  **Core engine (S1+S2+S6) — DONE (commit c3a269d):** `DiagnosticsEngine` is a pure-Core client of
  `SemanticModel` — conservative, deterministic, de-duplicated diagnostics `ET0001`–`ET0008`
  (UnknownObject/UnknownColumn/UnresolvedVariable/UnresolvedParameter/AmbiguousColumn/InsertCountMismatch/
  UnknownCursor/SuspendOutsideSelectable) in one forward pass over `References` + bounded AST checks; zero
  Avalonia, "prefer silence over false positives" throughout (object/column categories gated on live
  metadata). **S3 (Squiggle rendering) — DONE (impl); awaits user visual confirmation.** New App renderer
  `SquiggleRenderer` (`IBackgroundRenderer`, `Completion/SquiggleRenderer.cs`) draws a wavy underline under
  each diagnostic span (Error→`ErrorBrush`, Warning→`WarningBrush`, Info→`SubtleForegroundBrush`; both
  themes, no hardcoded colours), mirroring `SemanticHighlighter`/`OccurrenceHighlighter`. Wired once in
  `SqlEditorBehavior.Attach` → every SQL surface. Diagnostics are computed on the **existing** model
  background pass: `EditorLanguageService` now runs `DiagnosticsEngine.Analyze` inside the same cancellable
  `Task.Run` that builds the model (and on the two synchronous rebuild paths), caches an `IReadOnlyList<Diagnostic>`
  version-matched to the model, and exposes it via `SqlCompletionController.Diagnostics`. No second parse
  loop, no parallel analyses (a newer edit cancels the in-flight one via the existing CTS); the paint path
  reads the cached list only (viewport-culled + doc-clamped, so large scripts + post-edit staleness are safe).
  **Hover/tooltip is NOT part of S3** (user scope decision — squiggles only; the message surface is a later
  milestone). **S3 follow-up — Easy-mode ambient refresh (DONE, impl):** an Easy-mode routine editor's body
  holds only the fragment; its params/`DECLARE`d variables live in the grids and reach the model as *ambient
  symbols* (gotcha #218). A manual-QA pass found the model did NOT rebuild when those grids changed, so
  squiggles (and completion/highlighting) went stale until the next body-text edit. Fixed: the routine VMs
  raise `SourceObjectDetailTabViewModel.AmbientSymbolsChanged` on a grid add/remove/reorder or row **rename**
  (base tracks Variables; Procedure adds Input/Output params, Function adds Arguments — via a `TrackAmbient`
  mirror of `TrackDirty`, scoped to the `Name` property); the detail views bridge it (`AmbientModelRefresh`)
  to each ambient-seeded editor's new `SqlCompletionController.NotifyAmbientSymbolsChanged()` → the existing
  debounced `RefreshModelWithMetadata` rebuild (re-captures ambient). **Root-cause note (investigation):** the
  binder/engine/ambient mechanism are all correct — fed complete ambient symbols the model has zero false
  positives; the only gap was this staleness. Analysis stays on the *visible fragment + ambient*, NOT a
  synthesized full CREATE source (avoids offset translation; consistent with every other model consumer).
  **S3 is COMPLETE + committed (c8266e3), plus a defect fix found while preparing S4 (f397190): the main
  SQL Editor rendered NO squiggles.** S3 attached the renderer in `SqlEditorBehavior.Attach` believing that
  seam covered "every SQL surface" — it does not: that installer serves the **object editors**, while
  `MainWindow` hand-wires the main editor itself (null-safe `_currentVm?.…` callbacks). Its diagnostics were
  computed all along; only the paint was missing. Fixed minimally (attach the renderer in `MainWindow`
  too + correct the false comments); consolidating the duplicated wiring is the real fix and is owed **its
  own refactoring milestone**, deliberately kept out of Stage 7 (user decision). **⚠ Until then, a new
  editor capability must be attached in BOTH places** (gotcha #219).
  **S4 (Diagnostics panel) — DONE (impl); awaits user visual confirmation.** Scope was deliberately narrow
  (user directive): **list only** — no navigation/next-prev (S5), no Quick Fix, light bulb, hover, code
  actions, filtering or grouping. Hosted on **every** SQL editing surface (scope widened during manual QA —
  the object editors had squiggles since S3 but no way to browse them): a fifth `bottom-tab` in the SQL
  Editor (Results/Messages/Output/Performance/**Diagnostics**, gated on `IsQueryTabActive`), and a **peer
  top-level tab in the Procedure / Function / Trigger / View / Package editors**, hosted exactly the way
  `PerformancePanelView` already is there (same view + VM type, one panel VM per host, no shared state).
  **Script Executor deliberately deferred** (no tab strip → its own UX decision). The tab is appended **last**
  everywhere because `SelectedBottomTabIndex`/`ActiveSubTabIndex` are persisted and `PerformanceBottomTabIndex
  = 3` / `PerformanceSubTabIndex = 5` / `SqlSubTabIndex` / `PackageSubTabIndex` are hard-coded. Editor layouts
  were **not** redesigned (no panel-below-editor, no extra splitters — user decision).
  **DESIGN DECISION — the panel reflects the ACTIVE SQL document only, never a merge** (`LastFocusedSqlDocument`,
  design §8.2.1): the last SQL editor to take focus, else the mode's primary (body in Easy / full source in
  Source). Focus change retargets + republishes with no text edit; a mode flip or object rebind clears the
  sticky and falls back. It deliberately does **not** reuse the views' `ActiveEditor` — its
  `IsEffectivelyVisible` guard can never hold while a peer Diagnostics tab is on screen, so Cursors/Subprograms
  findings could never reach the panel (gotcha #220). A workspace-wide list, if ever wanted, is a SEPARATE
  feature and must not change this panel's meaning. New: `DiagnosticsPanelHost` (App/Completion — pure wiring
  over the UNCHANGED binder: one binder per editor, gated via the binder's existing lazy panel resolver),
  `DiagnosticRowViewModel` + `DiagnosticsPanelViewModel` (App/ViewModels),
  `DiagnosticsPanelBinder` (App/Completion — the view-layer bridge, beside `AmbientModelRefresh`, because
  offset→line/column needs the AvaloniaEdit document), `DiagnosticsPanelView` (App/Views — a *virtualizing*
  `ListBox`, not the Messages panel's `ItemsControl`-in-`ScrollViewer`, so a huge script's findings don't all
  realize). The panel is **only a view**: it analyses nothing, sorts nothing, filters nothing and shows the
  engine's findings in the engine's order. It rides the existing `ModelUpdated` cycle and reads the
  **cached** version-matched list — no parse, no model rebuild, no second analysis — so every refresh
  trigger (text edit, model rebuild, metadata bump, Easy-mode ambient change) is satisfied by that ONE
  subscription. Severity→brush mapping is identical to the squiggle renderer's, so a row and its underline
  always agree. VM property is `DiagnosticsPanel`, not `Diagnostics` — that name already resolves to the
  `EmberTern.App.Diagnostics` namespace inside `MainWindowViewModel`; it lives on `SourceObjectDetailTabViewModel`
  (covering Procedure/Function/Trigger at once), `ViewDetailTabViewModel` and `PackageDetailTabViewModel`,
  mirroring `Performance`. Build 0/0, **4136 main + 23 probe green** (+14 `DiagnosticsPanelVmTests`), smoke
  clean. **S4 is user-confirmed + committed (1d078c6).**
  **S5 (Navigation) — DONE (impl); awaits user visual confirmation. CLOSES STAGE 7.** `F8` / `Shift+F8` =
  next / previous (wrapping, silently; a clean document is a no-op), panel row activation on **double-click
  or Enter** (single-click only selects), caret + span selection + scroll + focus, and the object editors'
  two-target routing. A **pure consumer**: it navigates the panel's already-published rows — no parse, no
  model rebuild, no re-analysis. **The one architectural decision: everything routes through the ONE target**
  — `DiagnosticsPanelHost.ActiveDocument` (the `LastFocusedSqlDocument` rule) was made public, and navigation
  lives on the host because that is the class that knows it; so a row and its jump cannot disagree *by
  construction*. Navigation also scans the panel's **own** order (= the engine's, `Finalize` sorts by Start/
  Length/Code) rather than sorting again — reusing the one order is what makes "panel and navigation always
  agree" structural instead of coincidental; **pinned by a test against the real engine**. **The SQL Editor
  was migrated off its bare `DiagnosticsPanelBinder` onto the same host** — behaviour-identical (one editor ⇒
  the rule collapses onto it), but it removes the second targeting path AND hands that editor `F8` for free:
  `F8` is wired **once**, in `Track`, so no surface can be missed (**gotcha #219 dissolved by construction**,
  not by remembering two places). Script Executor has no panel ⇒ no host ⇒ no `F8` (consistent, S4's
  deferral). Object-editor reveal is a per-surface `Action<TextEditor>` handed the host's active document (it
  never re-derives a target): Procedure/Function → Editor tab **+** `Cursors/SubprogramsEasyIndex` when the
  target is those editors; Trigger/View → Editor/SQL tab; Package → the editor IS the tab; SQL Editor →
  nothing, except un-maximizing a results-maximized layout via the existing `ToggleResultsMaximized()`.
  Jump semantics mirror go-to-definition (`NavigationController.JumpTo`). **Near-miss worth knowing (gotcha
  #221):** the established "post the whole caret+scroll+focus block at `DispatcherPriority.Background`" idiom
  (Package member nav) would have made a held `F8` re-select the same diagnostic forever — `Input` outranks
  `Background`, so the next keypress reads the pre-jump caret; caret+selection are therefore set
  **synchronously** and only scroll+focus are posted. Build 0/0, **4148 main + 23 probe green** (+12
  `DiagnosticsPanelVmTests`), smoke clean.
- **Unified Hover Information — DONE (2026-07-16; impl, awaits user visual confirmation).** The first
  post-Stage-7 milestone. ONE hover surface instead of independent Quick Info / diagnostics tooltips:
  **plain hover** (no Ctrl, 350 ms dwell) shows the diagnostic on a squiggled span, today's Quick Info on a
  symbol, and both as *sections* of a single card when a span has both (**diagnostics first**). Pure
  presentation over the existing `SemanticModel` + the **cached** `DiagnosticsEngine` list — and
  "no new analysis" is **enforced by the signature**: `HoverInfoEngine.GetHover(model, diagnostics, offset)`
  takes the diagnostics as an INPUT, so it *cannot* re-analyse. **Absorbs + closes P5d.** New: Core
  `Sql/Language/Hover/` (`HoverInfo` = ordered aggregate `Span`/`Diagnostics`/`Info`, **no `IHoverProvider`**
  — rule #2; `HoverInfoEngine`), App `HoverInfoView` (composes the EXISTING `QuickInfoView.BuildContent` +
  shared `QuickInfoView.Card` chrome, so the unified hover and the standalone Ctrl+Space Quick Info cannot
  drift apart). **INTERACTION DECISION (user delegated) — plain hover = information, Ctrl = actionability;
  this CONFIRMS the frozen §9.4, it does not amend it.** The deciding fact is technical: the old tooltip was
  gated on `NavigationEngine.TargetAt` returning a *navigable target*, so Ctrl+hover showed **nothing exactly
  where `ET0001` fires** (an unknown object is unresolved ⇒ no target). Ctrl also *means* "this leads
  somewhere" — and an unknown object leads nowhere, so overloading it would make the affordance lie.
  `NavigationController` now has two independent cues (`UpdateNavigationAffordance` = Ctrl → underline +
  hand cursor; `UpdateHoverInfo` = plain → the card) sharing only the pointer position; Ctrl+hover shows the
  card too (superset) and does NOT dismiss one you are reading. **Gotcha #209 closed:** `_tooltip`'s bare
  `Popup` is **deleted** — the card is `OverlayLayer`-hosted, and `ClampIntoOverlay` was extracted from
  `ParameterHelper` into `EditorPopups` (one implementation, two consumers). Noise control: dwell + card
  stays put while the pointer is inside `HoverInfo.Span` (narrowest section, so no flicker) + never opens
  while the completion list / Parameter Helper / Quick Info is up (`SqlCompletionController.IsPopupOpen` —
  that controller already owned the "don't stack" rule) + never steals focus / never hit-testable +
  dismissed by any click / text edit / pointer exit. **Gotcha #219 did NOT bite** — this adds no new
  `Attach`; the new `Attach` params are **required**, so a missed seam is a compile error (both were).
  Build 0/0, **4159 main + 23 probe green** (+11 `HoverInfoEngineTests`), smoke clean.
- **Stage 8 (Smart Editing & Structural Assistance) — STARTED.** New milestone: *the editor helps you
  write code but never writes it for you without your explicit decision* (modern-IDE, not IBExpert).
  Charter: **M1 Structural Matching**, M2 Smart Snippets, M3 Snippet Engine, M4 Structural Selection
  (future) — one at a time. Design/as-built:
  [docs/history/16-stage8-smart-editing.md](docs/history/16-stage8-smart-editing.md).
  **M1 — Structural Matching — DONE + visually confirmed + finalized (2026-07-16). CLOSED.** The editor's two
  fragmented "related-elements" highlighters (the former text-based occurrence highlighter, and
  `NavigationController`'s semantic caret-symbol reference boxer) are unified into **one Related Elements
  Highlighting pipeline**: a Core `RelatedElementMatcher` (`Sql/Language/Matching/`, pure/testable) runs
  interchangeable **producers** — selection occurrences, caret-symbol references, caret-adjacent bracket
  pairs `()`/`[]`/`{}` (via the one `SqlLexer`, so brackets in strings/comments never match), and
  caret-adjacent `BEGIN/END` (via the AST `BlockStatement.Descendants` — covers proc/func/trigger/EXECUTE
  BLOCK/anonymous bodies + IF/WHILE/FOR bodies; a `CASE…END` is not a block so its END isn't matched) — and
  the App `RelatedElementsRenderer` (one `IBackgroundRenderer`) paints them. A **future CASE/END or LOOP is
  one more producer, never another renderer.** Matching reacts to the **caret** (adjacent to the token),
  not only a selection. New high-contrast, theme-tuned tokens `RelatedElementHighlight*` (fill + border,
  both dictionaries; the user delegated colour, requiring only high contrast in both themes and palette
  consistency — burnt-orange Light / bright-amber Dark, now visually confirmed). Attached in **BOTH** wiring seams (gotcha #219);
  the DDL-preview editor gets the model-less overload (text producers only).
  **Finalization cleanup (post-confirmation):** the dormant rollback path was removed — `OccurrenceHighlighter.cs`
  deleted and the obsolete `OccurrenceHighlightBorder*` tokens dropped; the one still-live consumer of the old
  fill token, `SearchMatchHighlighter` (Global-Search preview), was migrated onto a correctly-named
  `SearchMatch*` token so no "Occurrence" name survives as drift.
  (`NavigationController`'s nested reference renderer could NOT stay dormant — unused private members fail
  `TreatWarningsAsErrors` — so it was removed in place; git is its revert path.)
  **Post-M1 QA fix (gotcha #223):** bracket matching didn't activate on the FIRST call right after connect
  (worked after clicking another call and returning). The matcher is a pure function of (text, caret,
  model) — proven correct for the exact input; the fault was the App repaint: a plain `InvalidateVisual()`
  could run before the text view's visual lines were rebuilt (`Draw` saw `VisualLines.Count == 0`), and the
  diff guard made the miss permanent. Fixed: repaint with `TextView.Redraw()` (as `SemanticHighlighter`
  does) + skip the guard only on empty→empty so a missed paint self-heals. Build 0/0, **4187 main + 24
  probe green** (+28 `RelatedElementMatchingTests`, +1 headless renderer pin), smoke clean.
  **M1 finalization (post visual-confirmation) — DONE + CLOSED:** the dormant rollback path was removed —
  `OccurrenceHighlighter.cs` deleted, obsolete `OccurrenceHighlightBorder*` tokens dropped, and the one
  live consumer of the old fill token (`SearchMatchHighlighter`, Global-Search preview) migrated onto a
  correctly-named `SearchMatch*` token so no "Occurrence" name survives as drift. Committed `5e51989`.
  **M2 — Smart Snippets — BUILT then REVERTED (2026-07-16); SUPERSEDED.** A VS/Rider-style interactive
  snippet session was implemented (mirrored placeholders, final caret, indentation-aware expansion) but the
  user tried it and rejected the whole direction — *"now I delete half of this."* Full-block skeletons +
  placeholder sessions are the wrong UX for experienced Firebird devs. The code-writing experience was
  **redesigned from first principles** (uncommitted M2 reverted) into two independent subsystems — see the
  next bullet. `CompletionMatcher` (prefix-first) was kept. History + rationale:
  `docs/history/16-stage8-smart-editing.md`.
- **Language Completion & Typing Ergonomics — DESIGN FROZEN + Core foundation started (2026-07-16).** The
  redesign of the code-writing experience, goal = **fewest keystrokes, immediate & predictable, never
  generate code the user deletes (Rule 0)**. Frozen design:
  **[docs/design/editor-language-expansion.md](docs/design/editor-language-expansion.md)**. Three
  independent tools, chosen by grammar: **IntelliSense** (names, prefix-first, idle-debounced — Tool C =
  `CompletionMatcher`); **Language Completion** (finishes daily Firebird *constructs* the developer already
  started typing — `if`→`if (▌) then`, `gro`→`group by ` — via **Tab + a shown OverlayLayer hint**, matched
  by **natural prefix** (no invented abbreviations), **silent-until-unique** within a curated catalog,
  **synchronous / never timing-dependent**); **Typing Ergonomics** (`begin…end` as a structural delimiter
  pair, `()`/`''`/`[]` pairing, AST-aware auto-indent — **Enter stays a normal editing key everywhere**).
  Key principle: *anything special Tab does is always shown on screen first — no EmberTern-specific
  behaviour to memorise.* **DONE:** `CompletionMatcher` (+8 tests) and the **Language Completion Core
  foundation** — `Core.Sql.Language.Constructs` (`LanguageConstruct`/`LanguageConstructCatalog`/
  `LanguageConstructResolver`): the declarative catalog (each row a `ConstructCategory`) + a pure
  synchronous prefix resolver (multi-word aware, unique-within-catalog) + the **grammar-aware arming gate**
  (`ConstructContext` — a simple deterministic previous-significant-token rule: statement boundaries arm
  `Statement` constructs, value-completers arm `Clause` constructs, else none; one cheap synchronous lex,
  no AST/model, no timing). `LanguageConstructResolver.Resolve(text, caret)` = prefix match ∩ grammar is
  the single App entry point. +44 Core tests. **App hint controller — DONE (impl 2026-07-16); awaits user
  visual confirmation.** `LanguageExpansionController` (App/Completion, attached in BOTH seams — gotcha
  #219): a thin, **stateless** consumer — the armed construct is re-derived from (text, caret) via
  `Resolve` on every caret move and on Tab, nothing remembered. A passive `OverlayLayer` hint (`⇥ <expansion>`,
  reusing `EditorPopups`/`ClampIntoOverlay`) shows exactly what Tab inserts — below the caret line (never
  covers it), viewport-clamped, non-focus/non-hit-test, theme-tokened, font-scaled, hidden the instant
  nothing is armed and never while the completion list is up. **Tab** expands (tunnelled KeyDown preempts
  AvaloniaEdit's indent; falls through to indent when nothing armed); casing via the pure Core
  `ConstructExpansion.For` (Core owns the decision). +4 expansion tests. Build 0/0, **4243 main + 24 probe
  green**, smoke clean. **NEXT:** Typing Ergonomics (`begin…end` pairing + AST-aware auto-indent). **M3
  Snippet Engine / M4 Structural Selection remain future.**
- **Completion Matching Philosophy — prefix-first IntelliSense — IN PROGRESS (foundation done 2026-07-16).**
  A separate **Completion** milestone (not Stage 8), inserted at the user's request: interactive completion
  must be a **prediction engine, not a search engine**. Root cause of today's noise (`sta`→`NR_STATUS`/
  `DATASTATUS`, `if`→`IIF`/`NULLIF`): the Core engine returns the FULL in-scope set unfiltered, and all
  live narrowing is AvaloniaEdit's `CompletionList.GetMatchQuality` (private, no hook) which admits
  **substring** matches. Agreed behaviour: no prefix→all (Ctrl+Space); prefix with ≥1 StartsWith→**only**
  StartsWith; zero StartsWith→**close** (never Contains); consistent across every kind incl. keywords +
  snippets. **Architecture (user directive):** `CompletionEngine` is the single authority returning the
  FINAL list; a pure Core **`CompletionMatcher`** owns all filtering/ranking; the **UI is a passive view**;
  AvaloniaEdit's substring filter is **disabled**. **DONE:** `CompletionMatcher` (Core, pure,
  `Filter(items, prefix)` — StartsWith-only, exact floated to top, empty→all, zero-match→empty) +
  `CompletionMatcherTests` (8). Build 0/0, suite green. **It is currently UNUSED** (foundation-first). The
  **remaining step is entangled and must land atomically** (fold snippets into the engine + add a `prefix`
  param + rewrite the App as a passive view: `IsFiltering=false`, re-query per keystroke, close on empty,
  remap the new `Snippet` item kind) and needs **interactive visual QA** of AvaloniaEdit's `IsFiltering=false`
  rendering/ordering — so it was deliberately deferred to a fresh focused session per the user's
  "stop at a clean boundary" guidance. **The exact remaining steps are written up in
  `docs/history/17-completion-matching-philosophy.md` — a fresh session should execute that.**
- **⚠ MILESTONE-ORDER DECISION (2026-07-16) — the Stage 7 retrospective's "consolidate the editor wiring
  first" recommendation was REVERSED, with reason.** It rested on "both backlog items add per-editor
  surfaces — exactly what the duplication punishes"; that is true of **Quick Fixes** (a light bulb = a new
  adorner + gesture = a new `Attach` = the silent-omission risk) but **false of Unified Hover** (no new
  attach; required params ⇒ compile-time enforcement; and `NavigationController` is already the chosen
  consolidation point — the double-click handler was moved *into* it from the two seams). Consolidation is
  also not a mechanical merge: the seams differ by a real lifecycle (MainWindow's editor exists *before* its
  VM; object editors attach *after*), and MainWindow deliberately bypasses `subscribeMetadataChanged`
  because it latched "subscribed" against a null VM and dropped the handler — so consolidation must first
  solve "subscribe once the VM arrives". It touches every capability's installation on every surface for
  zero user-visible value, which under the QA rule means a full manual re-verification everywhere.
  **Standing recommendation: the wiring consolidation is the milestone immediately BEFORE Quick Fixes**,
  where it actually pays. Full reasoning: `docs/design/editor-stage7-diagnostics.md` §15.4.
- **R2 (2026-06-18 Transaction Architecture Audit) — CLOSED 2026-07-14, and its premise was wrong.**
  R2 ("procedure lock after Execute → Rollback → Compile") was left OPEN pending a live `MON$` dump,
  and the "Single-attachment DDL" fix that followed concluded DDL must be **co-located** on the
  attachment that executed the object. Measurement (gotcha #214) showed that conclusion was inferred
  from a **NOWAIT** failure: the cross-attachment lock is transient and a **WAIT** transaction clears
  it in ~10 ms. DDL now runs on its own dedicated attachment with a WAIT-bounded TPB, the
  *"Commit or roll back the active transaction before running DDL"* guard is deleted, and the
  scenario is verified working end-to-end on FB5. See `docs/history/15-...`.

## Editor Architecture — current direction

**Full architecture, component specs, and binding decisions: `docs/design/editor-architecture.md`
(kept current — read it before touching anything under `EmberTern.Core.Sql.Language`).** Status
summary only, here:

The SQL/PSQL editor is being rebuilt on **one shared, error-tolerant language front-end** —
Lexer → Parser → AST → Semantic Model, all in `EmberTern.Core.Sql.Language`, pure and
zero-Avalonia — with every feature (formatter, completion, navigation, diagnostics, signature
help, snippets, semantic highlighting, Quick Info) built as a *client* of that one model. This
replaced 7 independent ad-hoc SQL scanners + 3 divergent keyword lists. Governed throughout by
the project's **§0 Paramount Law**: never lose information, never modify code EmberTern can't
reproduce identically, correctness over aesthetics (see "Architecture rules" rule #11 below).

**Etaps 0–6 are COMPLETE**: IntelliSense responsiveness → Lexer + `FirebirdSyntax` keyword
catalog → Parser + AST ("statement skeleton" depth, `RawStatement` verbatim safety valve) →
AST-based Formatter → Semantic Model (scope tree + symbol resolution) → Completion + Signature
Help + Snippets → Navigation + Semantic highlighting + Quick Info (Ctrl+hover, Ctrl+Click,
Peek Definition, safe local rename, find references).

After Etap 6, the user ran a practical review (vs. IBExpert), **endorsed the architecture**, and
filed a **UX Polish Phase** backlog (P1–P9). **P1–P9 are done, including P8 (formatter polish +
max-line wrapping), which is now COMPLETE and architecturally closed** (§F shared list builder →
INSERT / UPDATE OR INSERT → long-line wrapping → EXECUTE BLOCK → FOR SELECT, one mechanism each);
only **P5d (a plain-hover info cue) and P2c (bold the typed completion fragment) remain consciously
deferred** — see "Current state" above for exactly where things stand.

A **pre-Stage-7 architecture review (2026-07-14)** then established the next foundation: because the
AST is a *statement skeleton with token-bag annotations* and SQL structure is duplicated across 3–4
token walkers, a foundational **Etap 6.9 — Structural AST Deepening** is inserted **before Stage 7**,
so the parser/AST becomes the single structural source for the formatter, semantic model, diagnostics,
folding, breadcrumbs and the future Debugger. Two design docs are the implementation guides:
- **[docs/design/editor-ast-deepening.md](docs/design/editor-ast-deepening.md)** — Etap 6.9 (design
  principles, node inventory, migration contract, milestones B0–B5, debugger considerations, formatter
  convergence, and a progress matrix). **Read before touching the parser/AST/binder for this work.**
- **[docs/design/editor-stage7-diagnostics.md](docs/design/editor-stage7-diagnostics.md)** — the full
  Stage 7 (Diagnostics) vision, which consumes Etap 6.9.

**Etap 6.9 parser stage is COMPLETE — B0–B5 parser producers are all DONE** (B0 = scaffolding + §0
differential harness + NUL/alias cleanups; B1 = the PSQL body tree, produced for all four surfaces **and
consumed by the semantic binder — its structural token walker is deleted**; B2 = the query clause tree
(clauses + FROM/join + set operations); B3 = the **fully recursive query model** — WITH/CTE, derived tables,
EXISTS/scalar subqueries all hold real `QueryNode`s; B3.1 = **queries embedded in OTHER statements**
(INSERT/MERGE sources, CREATE VIEW bodies, UPDATE/DELETE/MERGE embedded subqueries, PSQL FOR-SELECT /
DECLARE-CURSOR cursors); B4 = **CASE** (`CaseExpression`/`WhenClause`, simple + searched, SELECT-expression
+ PSQL); B5 = **PSQL body statements are reused top-level DSQL nodes** (a SELECT/INSERT/… inside a routine
body is the SAME node, with the SAME query structure, as at the top level). **The parser is now the single
structural source for all SQL/PSQL structure** (within the structural-depth scope: ordinary expressions stay
token fragments; `EXECUTE STATEMENT '<sql>'` runtime strings and a `PACKAGE` body are conscious boundaries).
**No parallel AST representation remains.** **BINDER CONVERGENCE — DONE (2026-07-15):** `SemanticBinder`
is now a full AST consumer — the query binder reads the `QueryNode` tree (FROM items, WITH/CTE, embedded
subqueries, clauses), the DML binder reads the DML nodes' source query + subqueries, and the PSQL binder
drives its leaf/header subqueries from the AST. Its **structural token walkers are DELETED** (`BindQuery`
token version, `CollectTables`, `ParseTableList`, `ParseCteList`, `BindColumnReferences`'s FROM+`(SELECT`
re-scan, `BindNamedTable`/`BindDerivedTable`/`BindTargetAfter`, `IsTableListTerminator`, and the PSQL
`BindLeafReferences`/`FindBodySelectEnd`/`BindOptionalInto`); only expression-level token work remains
(column/local/param references + DML-target identification, which has no AST node). Two small producer
refinements landed with it: `IF`/`WHILE` carry `ConditionExpressions` (condition subqueries/CASE), and a
PSQL singleton `SELECT … INTO` ends its `QueryNode` before `INTO`. Behaviour-equivalent: build 0/0, **4008
main + 23 probe green**, smoke clean. **FORMATTER CONVERGENCE — DONE (2026-07-15) ⇒ ETAP 6.9 CLOSED:** the
formatter is now an AST-walking layout engine wherever the parser provides structure — `EmitQuery` lays out
a query's clauses and recurses into nested queries as expanded-paren blocks (natural multi-level
indentation), `CaseExpression` lays out adaptively (inline when simple, else a WHEN/THEN/ELSE block), and
WITH/CTE, INSERT…SELECT, CREATE VIEW bodies, MERGE `USING (…)`, UPDATE/DELETE subqueries, and PSQL FOR-SELECT
cursors + leaf statements all drive their layout from the AST. A flat query is byte-identical (all
pre-existing exact tests unchanged), layout is idempotent, §0 is unchanged. The token emitter (`Emit`,
`MatchStructuralPhrase`, the PSQL block structurer) is **retained by design** as the interior/expression
renderer + the layout for constructs the parser intentionally does not model (UPDATE SET/DELETE/MERGE clause
layout, PACKAGE bodies) — one layout mechanism per construct, no parallel AST + token walker. The reported
issues (CASE, WITH, multi-level indentation) are fixed. See `editor-ast-deepening.md` §13.2. The legacy
`SqlAliasResolver` is off the editor path (only `PredicateExtractor`/Performance uses it); retiring it is a
separate Performance migration. **Stage 7 (Diagnostics) is COMPLETE** (S1–S6; engine → squiggles → panel
→ navigation), and the first post-Stage-7 milestone — **Unified Hover Information** (§15, absorbed P5d) —
**has shipped** (see "Current state" above). **Folding and Breadcrumbs** were part of the original "Etap 7
niceties" and are still **unbuilt**; they consume the same AST and need no further foundation. Remaining
backlog: **editor-wiring consolidation** (gotcha #219 — recommended as the milestone immediately BEFORE
Quick Fixes, per §15.4), **Quick Fixes** (`editor-stage7-diagnostics.md` §12), Folding, Breadcrumbs, and
**P2c** (bold the typed completion fragment — still no clean AvaloniaEdit 12.0.0 path). Nothing is
scheduled: next steps are the user's call.

## Architecture rules — enforce against drift

From the master prompt — non-negotiable, still in force today (rule 10 corrected during the
2026-07-11 cleanup: it originally read "no workspace persistence in V1", which V1.1 shipped
long ago — the surviving, still-true boundary is kept below):

1. **Core has zero Avalonia dependencies.** ViewModels in App contain no Avalonia types (no `IImage`, `Color`, `Thickness`). Theme toggle lives in code-behind on purpose — single button, no value routing through VM.
2. **No interfaces without two concrete implementations.** Every service so far (`ConnectionService`, `QueryExecutor`, `TransactionService`, `ConnectionProfileStore`) is a direct class. No `IDbProvider` layer.
3. **No autocommit. Ever.** Auto-*begin* exists (matches IBExpert workflow); auto-*commit* doesn't. There's no toggle, no setting.
4. **Virtualized grid is mandatory.** Avalonia DataGrid handles this — don't replace it with a plain `ItemsControl`.
5. **No `Utils/` or `Helpers/` folders.** If something has no clear home, the structure is wrong.
6. **No `AppResources.resx`.** Use `UiStrings` (static const class). Add new strings there, in both spots if light/dark variants are needed.
7. **No event bus / IMessenger** until 3+ components need to communicate. Currently events on services (`ActiveConnectionChanged`, `TransactionStateChanged`) wire VM directly — that's fine.
8. **Async only where the user waits**: query execution + connection. Not async everywhere.
9. **Dark + Light from day one.** Every new color → both dictionaries in `Themes/Colors.axaml`. Zero hardcoded colors in views — only `{DynamicResource}`.
10. **No plugin system, no debugger, no schema compare, no docking.** (Workspace persistence — the one item on this list that was originally "V1-only" — shipped in V1.1 and is now core; see "What's built" above. AI is separately addressed by the editor-architecture decision "kept AI-ready, nothing designed solely for AI".) The UI mockup shows aspirations; build only what's actually planned, not the whole vision at once.
11. **Never lose information / never corrupt user code or metadata (Critical / Data-Loss class — the project's #1 rule, above every feature).** Any feature that generates DDL or modifies user code or DB objects — formatter, recompile, refactor, Quick Fix, Rename, snippet expansion, future AI — MUST preserve every fragment it does not fully understand, **verbatim 1:1**. **If EmberTern is not 100% certain it can reproduce an object identically, it MUST NOT modify it automatically** (uncertainty ⇒ do nothing or ask). Correctness of generated code outranks aesthetics. Origin: a group procedure recompile once stripped input-parameter defaults and broke system mechanisms (gotcha #175) — that class of bug is unacceptable. In the editor front-end this is realized by an error-tolerant parser + `RawStatement` verbatim round-trip. See the "Editor Architecture — current direction" section above + [docs/design/editor-architecture.md](docs/design/editor-architecture.md) §0.

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


## Live gotchas — load-bearing subset

The **complete** catalog (~190 entries, organized thematically) lives in
**[`docs/gotchas.md`](docs/gotchas.md)**. Below are the ~20 that are load-bearing across almost
*any* future session — the rest are searchable there by keyword the moment a bug "feels
familiar". Each line is a one-sentence summary; follow the `#N` reference into `docs/gotchas.md`
for the full explanation, code, and the failure it prevents.

**Firebird transactions & connections**
- Never start a Firebird transaction from a bare `IsolationLevel` — always build explicit
  `FbTransactionOptions` (the driver's default silently picks `WAIT`, the opposite of what you
  usually want). *(#85)*
- One `FbConnection` allows exactly one transaction at a time — concurrent commands on the same
  connection must be serialized (`CommandLock`), and any reader must attach to the caller's
  active working transaction rather than opening its own. *(#89, #31, #22-revised)*
- A lane-resolving lock accessor (`MetaLock()`/`DataLock()`/`LaneLock()`) must be captured into a
  **local variable once** per acquire/release pair — never re-invoked at `Release()`, or a
  mid-call lane flip leaks one semaphore and over-releases another (survives reconnect; only a
  restart clears it). *(#98, #120)*
- **DDL ⇒ WAIT with a bounded lock timeout, wherever it runs.** The cross-attachment
  `object … is in use` is a TRANSIENT metadata-cache lock that bites only **NOWAIT**; a WAIT
  transaction clears it in ~10 ms. This **supersedes the old "DDL must be co-located on the
  attachment that executed the object"** conclusion (#122), which was inferred from a NOWAIT failure
  and forced Compile onto the data connection — which in turn forced the *"Commit or roll back the
  active transaction before running DDL"* guard. Both are gone: DDL now runs on its own dedicated
  attachment, independent of every user transaction. Never conclude "Firebird forbids X across
  attachments" from a NOWAIT failure without re-testing with WAIT. *(#214, supersedes #122;
  `docs/history/15-...`)*
- **A Firebird transaction cannot use an object it created but has not committed** —
  `CREATE TABLE T …; INSERT INTO T …;` in one transaction fails the INSERT with `Table unknown`
  (-204). Firebird cannot both let a transaction use an object it created *and* keep it rollbackable;
  `isql`/IBExpert choose the former via `SET AUTODDL ON`. So in EmberTern's console the user must
  Commit between DDL and dependent DML — that is correct, expected behaviour. Corollary: uncommitted
  DDL is invisible to other attachments, so an object created in the SQL Editor appears in the
  metadata tree **only after Commit**. *(#213, `docs/history/15-...`)*
- **A statement classifier may decide whether to REFRESH the UI; it must never decide WHERE/HOW a
  statement executes in an interactive console.** The SQL Editor used to auto-route DDL onto a second
  attachment with a hidden second transaction — making Commit ambiguous and splitting mixed scripts
  across two transactions. The classifier is kept (it is reusable infrastructure and the foundation
  of the future Script Executor engine); only the routing was removed. *(#215,
  `docs/history/15-...`)*
- After a transaction settles, refresh ONLY the object actually touched — never blanket-refresh
  every open tab (each refresh reruns several implicit-tx catalog reads, which on a DB with an
  `ON TRANSACTION_COMMIT` trigger multiplies into a real storm). *(#119)*

**Firebird catalog & DDL generation**
- Firebird catalog columns vary by version (e.g. `RDB$IDENTITY_TYPE` is FB3+) — version-gate the
  query (`ParseServerMajor`) instead of issuing a doomed SELECT and catching the exception.
  *(#146)*
- A `.sql` script written for `isql`/IBExpert must be UTF-8 **without** a BOM —
  `Encoding.UTF8` in .NET emits one and breaks the first statement's parse. *(#178)*
- Object names typed by the user are coerced to UPPERCASE on input (Firebird folds unquoted
  identifiers anyway) — apply this consistently to every new name-entry field. *(#141)*
- In PSQL, distinguish `CASE…END` from `BEGIN…END` by statement scope (to the next top-level
  `;`), never by a naive `BEGIN+1/END-1` counter — a `CASE`'s `END` has no matching `BEGIN` and
  will corrupt any hand-rolled block scanner. Route through `SqlScanHelpers`'s shared CASE-aware
  scanner. *(#117, #128, #129)*

**Avalonia UI & data binding**
- `x:DataType` on a `<Style Selector="...">` does **not** scope the selector at runtime — it's a
  compile-time binding hint only. A container style shared by multiple VM types needs ONE style
  with `ReflectionBinding` setters, never one typed style per VM type (the latter silently
  clobbers with `UnsetValue`). *(#38)*
- Avalonia's `DataGrid`/`TreeView`/`ListBox` don't select the row under the cursor on
  right-click — wire `PointerPressed` to select-then-let-the-context-menu-open, or context-menu
  actions act on stale selection. *(#16, #99)*
- A `TreeView` with a nested `VirtualizingStackPanel` cannot do stable random-access scrolling on
  a large expanded subtree — the sidebar was migrated to a flat, single-VSP `ListBox` for this
  reason. Filtering must rebuild the bound collection, never just flip `IsVisible` on hidden
  items (the panel still measures every hidden row). *(#154, #157, FlatTree migration in
  `docs/history/09-...`)*
- `Avalonia.Controls.TreeDataGrid` is a **commercially licensed** Avalonia "Accelerate" control —
  verify licensing before depending on it, version compatibility is not enough. *(#158)*
- A button/command gated on a computed or collection-derived value (`Count`, `Any()`, a
  `CanExecute`) needs an explicit `NotifyPropertyChangedFor`/`OnPropertyChanged` on **every**
  mutation path — correctly computing the value isn't enough if nothing tells the binding to
  re-query it (symptom: "the feature works but the button stays disabled"). *(#179, #187)*

**Editor language front-end (the current, active work)**
- The AST round-trips the source byte-for-byte via the retained token stream — this is
  independent of parsing depth, so `RawStatement`/an under-modeled node never risks data loss.
  Any text-reproducing consumer migrated onto the parser must be gated behind a permanent
  differential test proving byte-identity against the previous implementation. *(#191, #192)*
- No transitional class names (`V2`, `NewX`, `Temp`, `Parser2`, …) are left in the codebase once
  a migration completes — consolidate to the plain responsibility name the moment the old
  implementation is deleted. *(#195)*
- Any offset→scope/reference lookup driving an editor feature (completion, Quick Info, go-to-def)
  must be **inclusive at the end of a span** — the caret sitting at the exact end of a
  statement/identifier is the single most common position, and a half-open range silently
  resolves to the wrong (enclosing) scope there. *(#198)*
- Every object editor (Table/View/Procedure/Trigger/Function/Package/Domain/Generator/Exception/
  Index Detail) ships a Revert/Discard action beside its primary Compile/Save action, and it must
  **confirm** before discarding — an accidental click must never lose uncompiled work. *(#143)*
- **`SqlEditorBehavior.Attach` is NOT "the one seam" — it installs the OBJECT editors' capabilities; the
  main SQL Editor hand-wires its own in `MainWindow`.** A capability added only to that installer silently
  misses the app's most-used editor (this is exactly how S3 shipped with no squiggles in the SQL Editor).
  Add it in **both** places and verify by grepping the call sites, not by trusting the comment. *(#219)*

**General**
- Reflect the actual API surface (get/set, public/protected) before assuming a member is
  settable or overridable — a member appearing in a metadata dump doesn't mean it has an
  accessible setter or is safely overridable. *(#199, applies broadly)*
- One headless UI test session (`ConnectionExpandBindingProbe`) per test process — share it,
  don't spin up a second `HeadlessUnitTestSession`, and run it as its own `dotnet test` partition
  when the full suite hangs. *(#94)*

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

**After each milestone** (this replaced the old "append to CLAUDE.md's Completed milestones
section" instruction after the 2026-07-11 Documentation Cleanup Sprint — see "Documentation map"
above; do not revert to the old habit, it's exactly what made CLAUDE.md too expensive to load):
1. Write the milestone's full narrative into the most relevant existing `docs/history/*.md` file
   (or create a new one, named for its topic, if it doesn't fit any existing file) — this is
   where the "what we tried, what worked, why" detail belongs.
2. If the milestone changed anything in "What's built", "Current state", the "Architecture rules",
   or the "Editor Architecture — current direction" section, update those sections in CLAUDE.md
   **in place** — a sentence or a bullet, not a new appended block. CLAUDE.md describes the
   present, not the path that got here.
3. If the milestone taught a genuinely new lesson, add it to `docs/gotchas.md` in the right
   thematic section; promote it into CLAUDE.md's short "Live gotchas" list only if it's the kind
   of thing that would bite almost any future session, not just one working in that module.
4. Confirm `dotnet test` is green and the app launches before claiming "done".

## Working conventions

### Session management
- **One milestone per session.** Start a new Claude Code session for each milestone (M1, M2, …, V1.1 task N). Don't try to land two milestones in the same chat.
- **End every session by updating the docs** *before* closing the session — the milestone's full narrative goes in `docs/history/*.md` (new or extended file); CLAUDE.md's own "What's built" / "Current state" / rules sections get updated **in place** (short, present-tense); genuinely load-bearing gotchas get added to `docs/gotchas.md` (and, if cross-cutting, to CLAUDE.md's short list). CLAUDE.md is the handoff document, not the chat transcript, and it must stay short — that's the entire point of the 2026-07-11 Documentation Cleanup Sprint (see "Documentation map" above).
- **Every new session starts by reading CLAUDE.md.** Do not ask the user to re-explain context — the answer is in this file. If something needed is missing from here, that's a documentation gap to fix on the way out, not a question to ask on the way in.

**Why these rules exist:** Claude Code's context window grows with every message in a session. Long sessions burn more tokens per turn (re-reading the whole transcript), risk hitting context limits mid-task, and make cost unpredictable. One milestone per session keeps the working set tight, costs predictable, and the handoff explicit — CLAUDE.md carries the state, not chat history.


## Pointers to deeper notes

- **`docs/design/editor-architecture.md`** — the current, kept-up-to-date architecture of the
  SQL/PSQL editor language front-end. Read before touching `EmberTern.Core.Sql.Language` or
  anything downstream of it.
- **`docs/design/editor-ast-deepening.md`** — **Etap 6.9 — Structural AST Deepening** implementation
  guide (design principles, node inventory, migration contract, milestones B0–B5, debugger
  considerations, formatter convergence, progress matrix). The next foundational work, ahead of
  Stage 7. Read before deepening the parser/AST/binder.
- **`docs/design/editor-stage7-diagnostics.md`** — the full **Stage 7 (Diagnostics)** design/vision
  (engine, `Diagnostic` model, severities, categories, pipeline, squiggles/panel/navigation,
  incremental refresh, cancellation, performance, milestones, and post-Stage-7 Quick Fixes). Consumes
  Etap 6.9; explains why Diagnostics comes after AST Deepening.
- **`docs/gotchas.md`** — the complete gotcha catalog (~190 entries), organized thematically.
  Search it whenever a bug looks familiar.
- **`docs/history/README.md`** — index into the full project narrative archive (every milestone,
  session, and investigation, ~15 thematic files). Read a file when you need the "why" behind a
  specific feature or fix; nothing here is loaded automatically.
- **`docs/design/*.md`** (other files) — frozen, feature-specific design docs for already-shipped
  work: `script-executor-and-smart-parameters.md`, `execution-modes-and-export-framework.md`,
  `etap1-tokenization-audit.md`.
- **`memory/project_embertern_blueprint.md`** — the original V1 scope + hard-rule framing (V1
  shipped 2026-05-28). Mostly superseded by the "Architecture rules" section above and
  `docs/history/00-v1-definition-of-done-and-backlog.md`; kept for the historical framing.
- **`memory/project_embertern_scaffold.md`** — deep M1–M6 (V1) code-layout notes at a finer
  grain than `docs/history/01-v1-foundation-and-workspace.md` covers (exact gotcha mechanics,
  file-by-file layout as it stood at V1). Explicitly froze at M6; everything since is in
  `docs/history/`.
- **`memory/project_embertern_editor_architecture.md`** — a compact, actively-maintained memory
  mirror of the editor rebuild's status; kept in sync with `docs/design/editor-architecture.md`.
- **`memory/feedback_firebird_codepages.md`** — WIN1250/WIN1252/ISO8859_2 `CodePagesEncodingProvider`
  registration gotcha.
- **`memory/feedback_firebird_multiversion.md`** — FB3 SYSDBA "Install incomplete" auth fix +
  managed-driver auth-plugin caveats.
- **`memory/feedback_firebird_transactions.md`** — the full transaction-lane audit trail (C1/C2,
  the 2026-06-18 Transaction Architecture Audit, R1/R2/R3). Corrected during this cleanup sprint
  to reflect that R3 was ultimately resolved by *reverting to* a buffered/staged Compile model
  (not by keeping apply-immediately, as an earlier note here had it) — see "Current state" above
  for R2's still-unconfirmed status.
- **`memory/feedback_staged_implementation_contract.md`** — each etap of a staged rollout ships
  complete + tested + smoke-verified + polished before the next starts; never silently change a
  frozen design mid-flight.
- **`memory/feedback_never_lose_information.md`** — the paramount #1 project rule (Architecture
  rule #11 above): never corrupt user code or metadata; don't modify what can't be reproduced
  identically.
- **`memory/feedback_naming_no_transitional.md`** — no `V2`/`NewX`/`Temp` names left in the
  codebase once a migration completes.
- **`memory/reference_embertern_prompt.md`** — where the original master prompt + UI mockup live
  on disk.
