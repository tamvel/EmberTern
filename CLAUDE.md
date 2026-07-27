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
| **`docs/design/editor-language-expansion.md`** | **FULLY DELIVERED design** for the code-writing experience that replaced Stage 8 M2 — both halves shipped + user-approved: **Language Completion** (construct completion by natural prefix, Tab + shown hint, grammar-armed, synchronous) + **Typing Ergonomics** (`begin…end` pairing on Enter, `()`/`[]`/`''` pairing, structural auto-indent; Enter stays normal), separate from IntelliSense. §3 documents the as-built ergonomics (incl. what is deliberately NOT done: paren alignment, `IndentLines`); §5 the arming gate; §9.1 the **one-responsibility-one-owner** rule (vocabulary *and* grammatical position). | When working on `Core.Sql.Language.Constructs`, `Core.Sql.Language.Ergonomics`, or the completion/ergonomics wiring. |
| **`docs/design/editor-quick-fixes.md`** | **DESIGN + as-built — Stage Q COMPLETE (Q0–Q5, 2026-07-25), user-confirmed.** The self-contained guide for the **Quick Fix stage**: the on-demand `QuickFixEngine` over the unchanged read-only diagnostics pipeline, the shared `CodeAction`/`TextEdit` currency, the ONE `TextEditApplier` (rename migrates onto it), the light-bulb + `Ctrl+.` surfaces, the v1 action set with what is excluded and why, and seams Q0–Q5 with the recipe for adding a fix. **Supersedes `editor-stage7-diagnostics.md` §12 and CLAUDE.md's older holding note** on two points (no `QuickFixes` on `Diagnostic`; hover stays read-only). | When working on Quick Fixes / code actions. |
| **`docs/design/firebird-debugger.md`** | **The debugger's behaviour authority (v2, decisions ratified 2026-07-17).** ⚠ *Its "nothing implemented" framing dated from the design phase and was corrected 2026-07-25 — the debugger is built (P1/P2, D1–D13, D15, functions-as-root, the Draft model); sections amended by delivery say so in place (§9.1, §9.3.1, §12.14).* Feasibility (Firebird has **no** debug API — verified), the Fidelity Law §F, the client-interpreter + `EXECUTE BLOCK` harness, harness declaration rules, frame savepoints, exception control flow, per-session connection + transaction, nested frames/call stack, local routines (no temporary metadata), cursor bridge, UI/UX, panels, reuse map, prerequisites P1/P2 + milestones D1–D14, Fidelity Boundaries, and a live-engine verification log. | When working on the debugger. |
| **`docs/design/firebird-debugger-implementation-plan.md`** | **The debugger's execution plan** — per-milestone briefs (P1, P2, D1–D14: cel/zakres/components/new types/deps/risks/DoD/verification), how to split sessions so each ends green + committable, the editor/transaction **danger zones**, and the **Developer Contract** (20 binding rules). The spec says *what*; this says *in what order and under what rules*. **D14 = ANALYZED + DEFERRED** (its STATUS block records the ratified snapshot+savepoint+undo-only architecture if ever revisited). | **Every debugger implementation session — read this + your milestone's brief first.** |
| **`docs/design/d15-debugger-experience-and-ide-polish.md`** | **DESIGN — D15 planning phase COMPLETE (2026-07-20); the next major stage, nothing implemented.** The self-contained implementation guide for **D15 — Debugger Experience & IDE Polish**: the **Presentation vs Feature** split, all seven milestones (D15.1 Editor Readability app-wide · D15.2 Toolbar + own SVG icon system + Error Bar · D15.3 Launch Experience · D15.4 Friendly Errors · D15.5 Inline Values · D15.6 Performance-integration · D15.7 Global UI Audit), per-milestone seams/DoD, ratified design decisions + rationale, priorities, dependencies, risks. A future session starts any milestone from here **without re-analysing**. | When working on any D15 milestone. |
| **`docs/design/data-import.md`** | **🔒 FROZEN architecture + live implementation status for the Data Import module (the CURRENT work).** One working surface with collapsible sections (deliberately NOT a wizard), one pipeline for every source, `ImportConfiguration` as the single representation of every user decision (so profiles are a foundation, not a future extension), the transaction model, §0's seven consequences, risks R1–R20, and the etap plan I0–I12. **Its „📍 STAN IMPLEMENTACJI" block at the top is the handover** — branch, last commit, test count, what exists, what is next, and the remaining-scope table for I6–I12. **§3.8 holds the OPEN UX findings from the I5 review (U1–U10) — read it before touching the surface's layout.** | **Every Data Import session — read the status block + §3.8 + your etap's row in §6 first.** |
| **`docs/design/data-import-i11-session-prompt.md`** | Session material, not architecture: the ready-to-paste opening prompt for the **I11** implementation session (named import profiles). Each etap's prompt replaces the previous one, so this file is the only live prompt; it carries the input state, the reuse inventory, the standing "no global UI work" rule and the DoD. **I11 is unlike every etap before it: it adds no capability, it CHECKS one.** §4.8 claimed named profiles are a consequence of the model rather than a feature to be built, and named the disproof — "if named profiles require changing even one model or rebuilding a UI section, §4.8 was violated along the way". So the session's unusual duty is that needing to touch the models or the pipeline is a **result to report**, not an obstacle to work around. | When starting etap I11. |
| **`docs/design/data-import-i0-findings.md`** | The Data Import **measurement archive** (etap I0): what the engine and the libraries actually do — batch throughput and row-error attribution, GDS error codes, the silent charset substitution, `.xlsx` reading traps. Evidence for the „(I0)" notes in the design doc. | On demand — when an I0-derived decision needs its proof. |
| **`docs/design/metadata-refresh-analysis.md`** | **The Metadata Explorer's measurement archive + the plan for its own stage.** Why the tree feels slow (the catalog is ~164 ms off the UI thread; the *projection* was quadratic), the flow of build/refresh, the 20 `RefreshAsync()` call sites, and the three-layer recommendation. **§7 is the as-built**: Layer 1 shipped 2026-07-27 (1 424 ms → 2 ms) together with the targeted in-place tree update; **Layers 2 and 3 + the unmeasured startup cost stay open** for the Metadata Explorer stage after Data Import. | Before touching the metadata tree, and at the start of the Metadata Explorer stage. |
| **`docs/gotchas.md`** | The **complete** gotcha catalog (259 entries, #1–#272), organized thematically. CLAUDE.md keeps only the ~20 most load-bearing ones inline; this is where the rest live. | On demand — search it when a bug "feels familiar". |
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

## Git remotes & push workflow (user directive, 2026-07-26)

The repository is mirrored to **two** remotes. Both must receive every accepted milestone.

| Remote | URL | Role |
|---|---|---|
| `origin` | `https://vm-srv-app-git-02.streamsoft.pl:3000/GGronski/EmberTern.git` | company Gitea (HTTPS, Git Credential Manager) |
| `private` | `git@github.com:tamvel/EmberTern.git` | the user's personal GitHub (SSH) |

**The rule: after every ACCEPTED etap / milestone, push to both.**

```bash
git push origin <branch>
git push private <branch>
```

**Branch hygiene (2026-07-26):** the repo carries only branches that are still needed. `feat/completion-matching`,
`feat/firebird-debugger`, `feat/save-and-close` and `feat/sql-data-export` were all provably merged into
`master` and were deleted locally and from **both** remotes. Live branches: **`master`** + the working branch
**`feat/data-import`**. ⚠ One residue: **`private`'s default branch (HEAD) still points at
`feat/completion-matching`**, so GitHub refuses to delete it — it stays until the user switches the default
to `master` in the GitHub repo settings (a repo-settings change, deliberately left to the user).

**⛔ Never change the remote configuration without the user's explicit decision** — not the URLs, not a
`pushurl`, not a rename. A dual-`pushurl` "one push reaches both" variant was considered and **rejected on
purpose**: when GitHub is unreachable (no network, VPN, expired key) it makes *every* push report failure,
including the company one, so a company push would depend on GitHub's availability. Two explicit pushes keep
failures isolated.

### ✅ SSH — resolved 2026-07-26: git uses the system OpenSSH (durable fix APPLIED)

The binding configuration from now on is:

```bash
git config --global core.sshCommand "C:/Windows/System32/OpenSSH/ssh.exe"
```

**Applied globally on the user's explicit decision (2026-07-26) and verified**: `git push private` and
`git ls-remote private` both authenticate with **no** `GIT_SSH_COMMAND` prefix. **`GIT_SSH_COMMAND` is no
longer needed and should not be added to commands** — a plain `git push private <branch>` is the workflow.

**Why it was needed** (keep — it explains the setting and warns against undoing it): there are **two
independent SSH agents** on this machine, and with `core.sshCommand` unset git talks to the wrong one.

- The key lives in the **Windows OpenSSH agent service** (`ssh-agent`, Automatic). In PowerShell/CMD,
  `ssh` and `ssh-add` resolve to `C:\Windows\System32\OpenSSH\*` and DO see it.
- **Git for Windows bundles its own ssh** (`C:\Program Files\Git\usr\bin\ssh.exe`), which reads
  `SSH_AUTH_SOCK` and therefore does **not** see the Windows agent.

Measured 2026-07-26 (before the fix): `ssh -T git@github.com` in PowerShell → *"Hi tamvel! You've
successfully authenticated"*, while git's bundled ssh on the same host → **`Permission denied (publickey)`**.
So **a green `ssh -T` proves nothing** about whether `git push private` will work — if pushes to `private`
ever start failing with `Permission denied (publickey)`, check `git config --global --get core.sshCommand`
FIRST (an unset/overwritten value re-opens exactly this trap).

**After a reboot** the agent forgets the key (it is passphrase-protected). Unlock it once per system session
**from PowerShell or CMD**, so it lands in the Windows agent:

```bash
ssh-add ~/.ssh/id_ed25519
```

Running `ssh-add` inside Git Bash targets the *other* agent and will not help git or the Windows tooling.
Check what is loaded with `ssh-add -l` (PowerShell).

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
  EmberTern.Office/          # the ONE place a NuGet dep on an Office format is allowed, in BOTH
                             # directions: DocumentFormat.OpenXml (the streaming XLSX writer XlsxExporter
                             # and, since I9, the streaming SAX reader XlsxImportProvider) plus, since I10,
                             # ExcelDataReader for legacy .xls (XlsImportProvider). Renamed from
                             # EmberTern.Export.Office in I9.
  EmberTern.App/             # WinExe, Avalonia 12.0.3, CommunityToolkit.Mvvm 8.4.2
    Program.cs, App.axaml(.cs), UiStrings.cs, app.manifest
    ViewModels/ Views/ Themes/ (Colors.axaml + ControlStyles.axaml — the ONLY theme sources)
    Behaviors/ Completion/ Controls/ Converters/ Diagnostics/ Export/ Security/ Sql/
    Assets/ (FirebirdSql.xshd + .Light.xshd, Branding/, Icons/ — SvgIcon geometries)
    (NuGet: Avalonia.AvaloniaEdit 12.0.0, Avalonia.Controls.DataGrid 12.0.0)
tests/
  EmberTern.Tests/           # xunit; ONE shared HeadlessUnitTestSession for the whole
                             # ConnectionExpandBindingProbe class — see gotchas #94 / #226
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
- **SQL Data Export — Copy as INSERT / UPDATE** — right-click a result row → runnable, provably-correct
  DML (de-aliased via the server's own provenance; UPDATE only on a catalog-verified complete PK, never a
  partial key → multi-row bug; `OVERRIDING SYSTEM VALUE` for `GENERATED ALWAYS`; InvariantCulture literals;
  refused-with-a-reason where proof is unavailable). One Core pipeline (`ResultOriginResolver` →
  `SqlStatementBuilder` → `SqlLiteralWriter`) behind a shared App `SqlCopyController`, live on the **SQL
  Editor** (E5) and **Table Data** (E6) grids; Procedure/View results stay `NotATable`. Design + as-built:
  [docs/design/sql-data-export.md](docs/design/sql-data-export.md). **Milestone COMPLETE + user-confirmed
  (2026-07-17)**, including the follow-up fix for environments that hand every cell back as a `string`
  (the writer parses each kind strictly under InvariantCulture/ISO, refusing anything ambiguous — §0).
- **Shared `MessageBanner`** (`App/Controls/MessageBanner.axaml`) — the IDE's ONE message surface: a calm
  severity-striped bar (Info/Success/Warning/Error) whose stripe, icon **and message text** all carry the
  severity colour, message wrapped + selectable, with Copy (default on) / Expand / Dismiss. Chrome comes from
  **exactly two variants** in `ControlStyles.axaml` — standalone (default) and `Classes="docked"`. Live on 23
  surfaces: debugger Error Bar + pre-flight, all 10 object editors, Execute Procedure/Function (+ its dialog),
  Table/View data errors, Performance panel, Export dialog, Security Manager. The SQL Editor **Messages log**
  stays a log but paints its problem rows through the **same** `BrushKeyFor`/`GeometryKeyFor` mapping.
  **Use it for any new error / warning / info message on a work surface — never a locally-styled coloured
  `TextBlock`, and never a local `Background`/`BorderBrush`/`BorderThickness` on the banner itself** (a local
  value outranks the shared style and re-opens per-host divergence). *(UX Polish Seam 4 + its QA round)*
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

- **🏁 DATA IMPORT — POST-I10 ERGONOMICS SEAM CLOSED AND USER-ACCEPTED (2026-07-27).
  Next: I11 named profiles · I12 close-out.** Three review findings from I10, scoped by the user as **closing
  I10, not a new etap**: the clipboard as a **live source**, a **Refresh** button, and the requirement that a
  re-read go through **the same chain** as the first read. Confirmed with no findings, and the architectural
  direction endorsed explicitly — *"the important thing is that no second refresh path appeared; everything goes
  through one chain with different reasons for running (Decision / Refresh)"*. `Ctrl+R` stays; **`F5` was
  deliberately NOT added** (user's call — `F5` is Import). Suite **5801 green** (+16), build 0/0, smoke clean. **The models, the pipeline, the converter, the validator, the mapping planner and the writer were not
  touched** — the third time that has held (I9, I10, this).
  **⭐⭐ The user's architectural condition — "I don't want two update paths" — was already met, and the whole
  seam was bringing three things onto the one path.** `Recalculate` → `RunChainAsync` is and was the single
  entry point; what bypassed it was the clipboard (fetched by a command *beside* the chain) and what could not
  reach it was a user asking for a re-read (there was no way to run it at all).
  **⭐ THE REASON IS AN ARGUMENT TO THE ONE CHAIN, NEVER A SECOND CHAIN.** `ImportChainTrigger.Decision` (an
  edit — recompute over the facts already established) vs `.Refresh` (the user asked — drop the cached facts and
  re-read them first). Exactly the discipline that made "Validate" a different **writer** passed to the one
  `ImportPipeline` rather than a second mode: there is nowhere for a refresh to drift from an edit.
  **⭐ Why "even Ctrl+V didn't recompute the mapping" was NOT a mapping defect (gotcha #271).** The clipboard's
  text arrived by **assigning a property**, and `SetProperty` compares — identical text meant no
  `PropertyChanged`, no `Changed`, no chain, so re-reading unchanged content recomputed **nothing**. The read is
  now the chain's **first link**, so what happens next follows from *having been asked*, not from the value
  differing. **General rule: any state whose only refresh trigger is "someone assigns it" cannot be refreshed to
  its current value.**
  **⭐ Refresh re-reads the two FACTS the surface caches** — the clipboard text and the table list (which was
  latched behind `_tablesLoaded` and read once per tab, so "the table blocking my `CREATE` has been dropped" was
  invisible until the tab was reopened). Everything downstream then re-runs unchanged: source → analysis →
  schema → mapping → readiness → preview.
  ⚠ **Found by a test that COUNTED clipboard reads (gotcha #272): `SourceDescriptor` cannot say "a file, but
  none chosen yet"** — `BuildSource` reports `Kind == Clipboard` for that state, so a gate on the source kind
  would have made **every freshly opened tab** read the clipboard and, whenever the content looked tabular,
  adopt it as the source **with the „File" radio still selected**. The gate asks the thing that actually carries
  the user's choice (`Source.UseFile`, i.e. the radio).
  **⭐ The automatic read adopts content only if it IS a table, and invents no heuristic for that:** the first
  question goes to `DelimiterDetector`, the module's one owner of "is this delimited text", so "tabular" means
  here what it means everywhere else; the second question exists only because that detector deliberately refuses
  to invent a separator for a single column, and a single column pasted out of Excel is still a table. **An
  explicit refresh does not consult it at all** — "re-read the clipboard" has one honest meaning, so it adopts
  whatever is there, **including nothing** (readiness then says "no source", which is true; keeping the previous
  text would show data the named source does not hold).
  ⚠ **Deliberate boundary, pinned as a test:** a refresh does **not** re-propose a new table's types while the
  source still describes the same fields — edited types are decisions, and overwriting a decision with a
  proposal is what rule #11 forbids outright; the existing rule re-infers when the fields or the culture move,
  i.e. when the ground under those decisions actually shifted.
  Also: `ClipboardReadRequested` moved from an **event** (wired after construction) to
  `DataImportEnvironment.ReadClipboardAsync`, because the **chain** reads the clipboard and must be able to ask
  before anything could be wired to the finished tab (the file picker stays an event — only a user command drives
  it); and `FileFacts` → **`SourceFacts`**, one facts line for both variants, carrying the clipboard's **read
  time** — the form of "is what I see current?" that a live source can answer, and what makes a refresh visibly
  acknowledge itself when the content is identical.
- **✅ DATA IMPORT I10 — CLIPBOARD + `.xls` DELIVERED (2026-07-27), awaits the user's visual confirmation in
  both palettes.** Branch `feat/data-import`, suite **5785 green**
  (+22), build 0/0, app launches clean, `DataImportRunProbe` **33/33 ALL PASS** on FB5 `WI-V5.0.3.1683`
  (section **I** added).
  **⭐⭐ The pillar held under a harder test than I9's, because this time a DEPENDENCY arrived.** I9 added a
  source using a library the project already had; I10 adds **`ExcelDataReader` 3.7.0 (MIT)** — the one NuGet
  decision left in the module — and it reaches **exactly one project** (`EmberTern.Office`). The pipeline,
  converter, validator, mapping planner and writer were **again not touched**; `IImportProvider` now has a
  third implementation and App learned exactly one thing: `ProviderFor` has one more branch.
  **⭐ The clipboard was already built — the etap added only proof.** The Plik/Schowek toggle, `ClipboardText`,
  `UseClipboardCommand`, the `ClipboardReadRequested` delegate and `MainWindow`'s `TopLevel` read all shipped
  in I5. There was nothing to write; there was something to *verify*, and that turned out to be the
  interesting part (below). ⚠ Worth carrying: **check whether a scope item is already standing before writing
  it** — the opening prompt said "only the clipboard read is missing", and that existed too.
  **⭐⭐ Delimiter detection is a step ABOVE the pipeline, and the first version of the probe case did not
  mirror that.** It passed `AutoDetectDelimiter = true` straight to `ImportPipeline` and got back **one
  column**. That is not a defect — it is §0.4 working as designed: auto-detection **proposes**, shows its
  basis, and the **resolved** value goes into the configuration, so the provider reads a declared delimiter
  and holds no opinion about detection. The probe has to walk the whole road the surface walks or it proves
  nothing about the surface. ⚠ **General rule: when a probe and the UI disagree on the same input, first ask
  whether the probe reproduced the entire path** — only then look for a defect in the product.
  **⭐ Excel's calendar got ONE owner because the second provider needed it in reverse.** `WorkbookCellReader`
  computed serial → date itself (I9). ExcelDataReader hands back an already-decoded `DateTime`, so honouring
  "do not treat date cells as dates" means converting **back** to a serial — and an inverse written beside a
  forward function it cannot see is exactly how two halves of one calendar drift apart. Hence
  `ExcelSerialDate` (`FromSerial` + `ToSerial`), used by both providers. Pinned: serial 15 is 1900-01-15 in
  both directions (`FromOADate(15)` would say 1900-01-14).
  **⭐ The library decides "is this a date" for us, so its verdict is RE-ASKED.** The danger here is the
  inverse of `.xlsx`'s: not a date missed but a date **invented**. `XlsCellReader` asks
  `SpreadsheetNumberFormats` — the one owner of that question, which **parses** a format code rather than
  searching it (gotcha #268) — so the real user's custom currency format `#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]`
  stays money here too, even though the library independently called it a date. It also closed a divergence
  nobody would have gone looking for: a "time only" cell came out of `.xlsx` as a `TimeSpan` and out of `.xls`
  as `1899-12-31 12:00`; both now yield a `TimeSpan`.
  ⚠ **R8 measured BEFORE the provider was written** (throwaway `XlsFormatProbe`, deleted on close). A 60 000-row
  × 5-column BIFF8 sheet: heap **flat at 26.7 MB at rows 15 000 / 30 000 / 45 000 / 60 000**. The *shape of the
  curve* is the proof, not the final number — a reader that materialises the sheet grows with the row count.
  The 19.6 MB retained is the shared-string table, proportional to **distinct** strings rather than rows — the
  same property I9 documented for `SharedStringTable`.
  ⚠ **Correction to I9:** `Wynagrodzenie.xlsx` was recorded as "the old format under the new name". Measured
  in I10: it is an OLE2 container (signature `d0cf11e0`) but **not a workbook** — the BIFF reader answers
  *"Neither stream 'Workbook' nor 'Book' was found"*. I10 therefore does **not** make that file readable, and
  the refusal message promises no such thing. `XlsxImportProvider`'s refusal did change, though: it used to
  advise "Save As", which was the only way out while BIFF was unreadable; it now leads with "rename it to
  `.xls`". A refusal that keeps recommending the long way round after a short one exists has quietly stopped
  being true.
- **🏁 DATA IMPORT I9 — XLSX CLOSED AND USER-ACCEPTED (2026-07-27).** Confirmed visually in both palettes;
  the user checked the sheet picker, the hiding of CSV/TXT-only settings, "treat date cells as dates" and a
  full XLSX run — no visual or functional findings.
  **Next after I9 was I10 (delivered — see above) · then I11 named profiles · I12 close-out. ⛔ User directive on closing I9: no
  further refactors and no architectural changes — the module is carried to the end on the frozen design.**
  Branch `feat/data-import`, suite
  **5763 green** (+46), build 0/0, `DataImportRunProbe` **25/25 ALL PASS** on FB5 `WI-V5.0.3.1683`.
  **⭐⭐ The result that matters is what did NOT change.** I9 was the first etap to add a new SOURCE, and
  therefore the first real test of the "one pipeline for every source" pillar (§1.4). **The pipeline,
  converter, validator, mapping planner and writer were not touched** — all workbook knowledge lives in
  `XlsxImportProvider` (project `EmberTern.Export.Office` → **`EmberTern.Office`**, decision D1). Probe
  section H makes it visible: the same journey as sections A–G, one new provider. It also closed a rule-#2
  debt — `IImportProvider` finally has a **second** production implementation (§4.3 called the single one
  transitional).
  **⭐ A defect in I0's own probe heuristic, found and NOT carried into production.** The probe asked
  `code.Contains('d')`. The custom format from the user's real file — `#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]`,
  which I0 itself labelled *"currency, NOT a date"* — answers **TRUE**, because `[Red]` contains a `d`. The
  probe never noticed because no cell used that style (it measured "numeric cells with a date format: **0**").
  In production that turns a money column into dates, silently — §0.1's worst class. `SpreadsheetNumberFormats`
  **parses** the format code (quoted literals, escapes, bracketed sections — `[Red]` rejected, `[h]` accepted
  as elapsed time) instead of searching it. ⚠ **The general lesson: a probe proves what it happened to
  execute.** Its PASS is not evidence the heuristic is right, only that the input never exercised it.
  **⭐ R20 closed with a CARRIER, not a new rule.** `ImportErrorKind.SourceErrorValue` had existed since I2
  (naming R20 in its own comment), with the UiString and the report mapping already wired — only the value a
  provider could use to say "this cell is an error" was missing. New `SourceErrorValue` (Core, deliberately
  source-neutral — `RawRecord` is the currency shared by every source) plus one branch in
  `ImportValueConverter` **before** the target-type branches. The order is the substance: the text branch
  returns `Ok(text)` unconditionally, so if the refusal depended on the column type, `"#N/A"` would land in a
  VARCHAR as data. Verified on the live engine (probe **H1b**).
  **⚠ I0's owed measurement gap (R3) is closed on REAL Excel output.** I0 honestly recorded that the user's
  file held *no* date cells, so date handling was designed against a generated sheet. The production provider
  was run over **eight real workbooks from the user's disk**: `Fantomy…` reproduced I0's measurements exactly
  (column "Nr technologii" = `double×4999 + string×1`), and **`Wyceny.xlsx` has genuine date cells** — column
  "Termin", ~450 dates across seven sheets, vintages 2021–2026, all read correctly. The round trip is proven
  end to end by probe **H4** (sheet `2026-04-03` == database `2026-04-03`).
  ⚠ Also found in passing: **a file named `.xlsx` need not be one** (`Wynagrodzenie.xlsx` is the old format
  under the new name; `SpreadsheetDocument.Open` says *"File contains corrupted data"*, which reads as "your
  file is damaged" when the truth is "this is BIFF and this library cannot read it"). The provider translates
  it into a sentence the user can act on. And Excel's pre-1900-03-01 epoch is **not** OLE's (serial 1 differs
  by a day, plus the phantom 1900-02-29): the correction is explicit and the phantom day stays a number rather
  than becoming a silently shifted date.
- **✅ METADATA REFRESH — LAYER 1 SHIPPED + THE TREE NOW SHOWS AN IMPORTED TABLE IMMEDIATELY (2026-07-27).**
  A short session between Data Import I8 and I9, scoped by the user to: fix the reported bug, apply **Layer 1**
  of [docs/design/metadata-refresh-analysis.md](docs/design/metadata-refresh-analysis.md), re-measure, update
  docs — and explicitly **NOT** start the Metadata Explorer infrastructure rebuild (Layer 2), which is its own
  stage after Data Import closes. Suite **5717 green** (+13), build 0/0, smoke clean.
  **⭐ Layer 1 is the whole lesson: the bulk guard already existed and was wired to ONE of the two mass
  mutations.** `SidebarFlatController.BeginUpdate/EndUpdate` — whose own comment names *"an O(n²) storm"* —
  was applied to the FILTER rebuild only; `MetadataNodeViewModel.SetLeaves` (`Clear()` + one `Add` per object)
  ran unguarded, so each `Add` re-spliced the owner's entire child block. Now called from `LoadGroupAsync`
  (covering every caller), `RefreshAsync` (13 re-projections → 1) and the connect-time prefetch; nesting-safe.
  **Measured (`tools/probes/MetadataPerfProbe`, 2 400-table schema): a full refresh with one category expanded
  1 424 ms → 2 ms; with two, 1 733 ms → 4 ms.**
  **⭐ The bug fix reversed the report's own recommendation, on the user's call:** the report advised deferring
  "the new table is not in the tree" to Layer 2; the user rejected that (ordinary UX bug, fix it now) while
  keeping the ban on a 21st `Metadata.RefreshAsync()`. Both hold at once because **the import knows the name of
  the table it created** — `DataImportEnvironment.TableCreated`/`TableDropped` report a FACT ("this table
  exists"), never a command ("refresh"), and `MetadataExplorerViewModel.ApplyObjectAddedInPlace`/
  `ApplyObjectRemovedInPlace` insert/remove **one leaf at its sorted position** (**1.3 ms, zero catalog round
  trips**, vs 13 queries ≈164 ms + a full re-projection). Three details make a targeted update correct and each
  is pinned: **idempotent** (a later refresh may already hold it); an **active filter** means the leaf enters the
  master list always and the displayed list only if it matches, with match count + zero-match visibility
  re-derived; an **unloaded** category has no leaves but does have an `(N)` label, so its count moves instead.
  The name index is **patched**, not invalidated (dropping it would cost 13 catalog reads to forget one name we
  were holding). ⚠ **Scope:** a narrow precedent beside the existing `ApplyTriggerActiveStateInPlace` — **not** a
  change protocol; the other 20 DDL paths were untouched.
  ⚠ **Startup did NOT improve, and that is measured, not assumed:** `RestoreExpandState` restores folder +
  connection expansion but **not** category expansion, so at connect every category is collapsed and the
  projection already had an early exit (the "0 expanded" row: 6 ms → 2 ms). Layer 1 fixes *refreshing*, not
  *starting*; the startup cost stays unresolved, instrument `EMBERTERN_PERF_DIAG=1`, suspects = the
  semantic-model rebuild after `NotifyMetadataReady` and workspace tab restore.
  ⚠ **Accepted trade-off:** `EndUpdate` re-projects everything, so `SidebarRow` objects are recreated and the
  list scrolls to top — already true of every refresh (it ends in `ApplyFilterAsync`, which does exactly this),
  but it broke `ConnectionExpandBindingProbe.AutoExpandOnConnect_ReflectedInFlatList`, which held a row
  **instance** across a connect; it re-resolves the row now (the probe is about mirroring, not identity).
  Removing the tree's "jumping" is Layer 2's job. **Still open for the Metadata Explorer stage:** Layer 2
  (first-class "what changed" across all DDL paths + incremental splicing + scroll/selection preserved),
  Layer 3 (reconcile the prefetch with `RefreshAsync`'s dead `LoadCountAsync` branch · `Domain` 79 ms
  `RDB$FIELDS` scan · `User` security-DB round trip), and the startup measurement. Gotchas **#266 / #267**;
  narrative: [docs/history/09-...](docs/history/09-object-editors-and-metadata-tree.md).
- **🔴 I8 REVIEW FOUND A CRASH — FIXED 2026-07-27, and its two lessons generalise well past this module
  (gotchas #264 / #265).** Symptom: new table → Validate passes → **Import closes the whole application**.
  Diagnosed in a minute from `%TEMP%\EmberTern-debug.log`, which the app's own `AppDomain.UnhandledException`
  hook fills with the full stack trace — **read that log first when a crash is reported, before re-reading any
  code.** Two independent defects, both mine, both pinned:
  **(1) Hiding a control does not retract the decision it carries.** "Empty the table before importing" is
  meaningless for a table about to be created and its checkbox is hidden in that variant — but `BuildBehavior`
  still copied the value, so a tick made on the existing-table variant (or, more likely, arriving inside the
  **restored "last used" configuration**, where the user never saw it) left `true` in the record and the run
  read a row count from a table that did not exist yet. Fixed in the ONE place that turns UI state into the
  record; the tick itself is deliberately not cleared, so switching back finds it where it was left.
  **(2) ⭐ `AsyncRelayCommand` rethrows a faulted command on the dispatcher, so an unhandled exception in a
  `[RelayCommand]` does not produce a bad report — it terminates the process.** The catch clauses were
  allow-lists of exception TYPES, and this VM reaches the world only through `DataImportEnvironment`'s
  delegates precisely so no Firebird type reaches a ViewModel (rule #1) — **that erasure cuts both ways: a
  component that talks to the world through delegates cannot enumerate the exceptions the world throws.** It
  could not even *name* `DdlExecutionException` without breaking its own layering. Now caught **by POSITION
  (this is the boundary), not by TYPE**, with `OperationCanceledException` kept separate and above (#253), at
  all 12 delegate seams, the other async commands, and the fire-and-forget recalculation chain (whose escapes
  became `UnobservedTaskException`). ⚠ This also hid a second, unreported crash: **any refused `CREATE TABLE`
  would have closed the app too**, since `DdlExecutionException` was on no list either.
  **Proof the guards work:** both fixes were temporarily reverted and the two new tests duly failed. The
  regression test throws a type nobody would put on an allow-list, from **every collaborator in turn**, so it
  fails the day anyone narrows a catch again.
- **✅ I8 — NEW TABLE: DELIVERED 2026-07-27, awaits the user's visual confirmation in both palettes.
  Next: I9 XLSX · I10 XLS · I11 named profiles · I12 close-out.** Branch `feat/data-import`, suite **5704
  green** (+97), build 0/0, app launches clean. **Live-verified:** `tools/probes/DataImportRunProbe` gained
  section **G** and runs **20/20 ALL PASS** against FB5 `WI-V5.0.3.1683`.
  **⭐ Four things worth carrying forward.**
  (1) ⭐⭐ **`ColumnTypeInferencer` owns no parser.** Every "could this value be a number / a date / a
  boolean?" is asked of **`ImportValueConverter`** — the same class that will convert the value during the
  real run, under the same culture. Not tidiness: an inferencer with its own idea of what an integer looks
  like would propose a type the converter then refuses, which is exactly R19's timebomb. Pinned by
  `EveryValueSeen_ConvertsIntoTheTypeThatWasProposedForIt`.
  (2) ⭐ **`ImportNewTable` is the ONE owner of "what does this column definition become".** The type text the
  preview validates against, the type text in the `CREATE TABLE`, and the type text the catalog reports
  afterwards are **the same string**, from one `DdlGenerator.FormatTypeOrDomain` call (§4.6 — no second
  generator, no second column model). Two producers would eventually disagree, and the disagreement would
  arrive as **rows rejected by a table the module itself designed**. Probe case **G4** checks it on the live
  engine: the catalog returned `VARCHAR(2), VARCHAR(8), NUMERIC(4,2), DATE` — exactly what was asked for.
  (3) ⭐⭐ **Projecting the new table as an `ImportTarget` (`ImportNewTable.Project`) is what gives "Validate"
  a table that does not exist — and that is its whole value.** The dry run answers "will these inferred types
  actually hold my file?" **at the one moment the answer is still free**: after the `CREATE` the table is
  committed and beyond a Rollback (§0.5 / gotcha #213). Mapping, readiness and the converted preview then
  work on a new table with **no special case at all** — from their side, a table that will exist and one that
  does are the same question. After a real `CREATE` the coordinator **re-reads the target from the CATALOG**:
  the projection is a prediction, the catalog is the fact.
  (4) ⭐ **A value with a leading zero is NOT a number — a §0.1 rule, not a parsing one.** `007` parses as 7
  without complaint, but the seven and the text are **different data**: a postal code, an index, an account
  number comes back different from what went in. That is rule #11 directly, so such a column becomes
  `VARCHAR`. A single leading zero (`0`, `0,5`) stays a number.
  ⚠ **Three deliberate boundaries:** nothing is ever inferred `NOT NULL` (the file in hand having no gaps
  says nothing about the next one, and the constraint outlives the import); **`SMALLINT` is never proposed**
  (right for this file, wrong for the next, and the difference from `INTEGER` is not worth a rejected row);
  and **`DOUBLE PRECISION` is not a candidate** (the only text it would accept and `NUMERIC` would not is
  scientific notation, which the converter refuses anyway — and choosing an approximate type for
  exact-looking values loses digits silently). Also new: **`IMP0028 NewTableAlreadyExists`** blocks a taken
  name *before* the run, because the `CREATE` is the first thing an import does and letting it through would
  mean a green strip followed instantly by a raw server error.
- **🏁 DATA IMPORT MVP IS COMPLETE — I0–I7.5 CLOSED AND USER-ACCEPTED (2026-07-26).**
- **⭐ I7.5 — DATA IMPORT HAS ITS OWN WORKING TRANSACTION (accepted). Ratified amendment to
  design §4.5, the module's "most important decision".** The deciding argument was not UX: while the import
  wrote into THE one user working transaction, its **Commit also persisted whatever the SQL Editor had left
  uncommitted**. A button must do exactly what it says (rule #11 / §0.5), so the possibility was removed
  rather than warned about. **Measured first** (`tools/probes/ImportTransactionIndependenceProbe`, FB5, 8/8):
  the driver refuses a second transaction on one `FbConnection`, but **two attachments give two fully
  independent transactions** — so "another independent transaction" means "another attachment", exactly as the
  debugger has done since D2. **Accepted cost:** a `SELECT` in the SQL Editor will NOT see imported rows before
  the import commits, and a same-row collision now fails immediately (SQLSTATE 40001 in ~28 ms under NOWAIT).
  **As-built, three things worth carrying.** (1) **`FirebirdSessionConnection` was extracted from
  `DebugSessionConnection` by COMPOSITION** — the debugger *holds* one instead of being one, so its public
  surface is byte-identical and its tests + `DebuggerFidelityProbe` stay an untouched regression proof of a
  closed subsystem. `ImportSessionConnection` joins on the same fundament. (2) **`ImportReadiness` stopped
  reporting the console's transaction at all** (IMP0021 retired, number never reused) — which dissolved a
  contradiction the design had carried since I2: §3.2 called an open working transaction BLOCKING while §4.5
  had the writer join one. (3) ⭐ **`PendingWorkRegistry` is the ONE owner of "does the application hold
  anything uncommitted".** The close/disconnect guards used to ask `_transactionService.IsActive` directly;
  with a second transaction that would have become a list of module names in the shell, and the module nobody
  remembers to add is the one that loses data. It is *not* an abstraction built for the future — the app
  already had this exact shape for editor work (`IUnsavedWorkSource`), and it ships with two real sources.
  **Deliberately NOT merged with editor work** (Save/Discard vs Commit/Rollback are different verbs), and the
  **debugger deliberately does not register** (spec §4.4 discards a debug run's writes by contract). ⚠ The
  **toolbar's** Commit stays the console's alone — making it settle the import would re-create the
  cross-module commit in the other direction. Live proof: `DataImportRunProbe` **13/13 ALL PASS**, case F —
  console leaves a row uncommitted, import commits, console rolls back, only the import survives.
  Commits `4bf6cf8` (A) · `019e7f8` (B) · `8766787` (C) · `bb28805` (D).

- **✅ I7 — CLOSED AND ACCEPTED (2026-07-26). The MVP surface:
  CSV/TXT → an existing table now imports end-to-end, with validation, a report, the transaction decision
  and a remembered configuration. Confirmed by the user in both palettes.** Branch `feat/data-import`, suite **5607 green** (+24), build 0/0, app launches clean.
  **Live-verified:** new `tools/probes/DataImportRunProbe` vs FB5 `WI-V5.0.3.1683` — **11/11 ALL PASS**
  (the report's numbers equal `SELECT COUNT(*)`; Rollback undoes the "empty first" DELETE together with the
  rows; the count behind that confirmation is read inside the user's own transaction; `Batched` really does
  commit every N and a later Rollback really cannot take those rows back; a dry run touches nothing).
  **⭐ Three things worth carrying forward.**
  (1) **The converted preview IS the real import, not an imitation of it.** §3.6 promises the grid shows
  "exactly what reaches the database", and that is only true because `ImportPipeline` fills it — same
  converter, validator, mapping and culture. Two additive Core pieces make that possible:
  **`BoundedImportProvider`** (a decorator bounding the READ, so a preview never reads a million rows to show
  a hundred) and **`PreviewImportWriter`** (a writer that KEEPS rows instead of sending them). Same discipline
  as "Validate is another argument, not another mode", one level up: **a different provider and a different
  writer, the same one import.** ⚠ A failed row shows its **RAW** values and that is not a half-measure — the
  pipeline stops a row at its first bad value, so such a row *has* no converted values, and the raw text is
  exactly what the user must fix.
  (2) **`CanValidate` is deliberately weaker than `CanRun`.** An open working transaction blocks the IMPORT
  but not the VALIDATION: a dry run writes nowhere, so refusing it would deny the one operation that helps
  most while the user is deciding what to do about that transaction. The rule lives in Core
  (`ImportReadinessReport.CanValidate`) — "what does this report permit" is that record's question, and a view
  deciding it would be a second opinion on readiness.
  (3) **`Batched`: `CommitEveryRows` is a FLOOR, not an exact multiple — measured live.** A commit can only
  land on a flush boundary (`BatchedCommitImportWriter` is a **decorator**, not a change to
  `FirebirdImportWriter`, so `Manual` and `AutoCommitOnSuccess` run byte-identical code), i.e. at the first
  batch boundary **at or past** N. With the I0-measured defaults (batch 500, commit 10 000) it lands exactly
  on 10 000; a commit interval *smaller* than the batch size yields one commit per batch. Shrinking the batch
  to match was rejected — I0 measured batch size as the thing that costs throughput while commit frequency is
  nearly free. Stated out loud because it is the number the report will be read against.
  Also new: **`DataImportEnvironment`** replaced five positional delegates with one named bundle (I7 adds six
  collaborators, and eleven positional arguments is where two get swapped silently), and
  **`FirebirdImportTargetPreparer`** (`COUNT(*)` + `DELETE FROM` on the **Data** lane inside the user's
  transaction — emptying a table is data, not schema, so it must roll back with the import).
  **I6 as-built — the Target section + the Mapping panel, and it *inserted into* the frame the I5 closing
  seam left rather than restructuring it.** New `ImportTargetSectionViewModel` (table picker fed from the
  **Metadata** lane, a facts line that **names** the BEFORE INSERT triggers rather than counting them — a
  count says something is there, the names say what will rewrite the values, R6 — and "empty the table
  first", off by default because it destroys data) and `ImportMappingPanelViewModel` + `…RowViewModel`
  (orientation **target → source**, because the target column is the side with requirements and the source
  field is the choice). The chain of §4.7 is now **source → target → mapping → readiness**, one cancellable
  sequence. ⚠ **Three things worth carrying forward.** (1) **A locked column is shown WITH its reason, never
  hidden** — a missing row is a question the user cannot even ask; the sentence comes from the ONE
  code→text table the readiness strip uses, so a column's note and the strip cannot drift. (2) **The
  sole-remaining-pair rule can reach an identity `GENERATED ALWAYS` column, and that is Core's ratified
  behaviour, not a bug**: `IsMappable` does not exclude identity and `Diagnose` then raises `IMP0007`
  ("an accent, not a fault — but never silent"), with the writer emitting `OVERRIDING SYSTEM VALUE` (I4). The
  UI lock governs a user reaching for that column *by hand*; when the planner paired it, the row shows
  unlocked and marked "assumed". **No Core change was made.** (3) **A defect caught by its own test:** the
  first cut cleared the record's `Mapping` whenever the target was null — so a restored profile lost its
  pairing merely because the target had not been read back yet. The grid clears; **the record never does**.
  **After I4 the whole ENGINE is built and live-verified** (`tools/probes/DataImportProbe` vs FB5
  `WI-V5.0.3.1683` — **20/20 ALL PASS**); from I5 on the work is the user interface only. Branch
  **`feat/data-import`** (pushed to **both** remotes), suite **5583 green**, build 0/0, app launches clean.
  **✅ I5 — CLOSED, user-confirmed after two visual reviews that produced U1–U12.** The reviews are the
  project's QA rule proving itself: build, green tests and a clean smoke had found **none** of it, because
  every finding is about **proportion and space**, not state. All twelve are settled and recorded in
  [data-import.md §3.8](docs/design/data-import.md); **ten shipped in one closing seam**, two stay
  deliberately open (**U4** global density → the app-wide UX sprint below; **U5** responsiveness →
  re-checked during I6, when Target and Mapping actually occupy space).
  **⭐ The layout was revised and carried into §3.1 in place — the single change that matters is that the
  STAR now belongs to the work, not the configuration.** Until the review the configuration `ScrollViewer`
  was the `*` row and the preview was nailed to 190 px — backwards, and the whole reason the preview was
  "practically invisible"; the work area now takes everything left (~500–540 px where it had none). With it:
  band A **deleted** (no other module carries a header; the one fact it was meant to add — connection + lane
  — moved to band H) · sections became **stacked one-row tiles, not side-by-side panels**, restoring the
  top-down reading order §1.2 names as one of the two things kept from the wizard · and each tile splits by
  **how often a decision changes**, so the file/table picker stays live at all times and only "Format
  options" folds — side-by-side had optimised the *rare* state (both expanded) at the cost of the *constant*
  one, and folding the picker away would have broken §1.2's promise that a repeat import is one `F5`.
  Also: readiness findings capped at 3 + "…and N more" with the chips always intact (U6) · a `GridSplitter`
  with double-click collapse and a **persisted** height (U2) · **U11** — the format options now collapse
  themselves once a source reads, unless the user opened them by hand · the ragged-row marker finally
  painted (U8 — it was computed and pinned by a ⭐ test but nothing rendered it, gotcha #233's shape) ·
  `Ctrl+O` (U10, only shortcuts that have something to drive) · **U12 — a shared settings-group idiom**:
  new `Border.settings-group` (a recessed card) + `TextBlock.group-header` in `ControlStyles.axaml`, because
  two captions floating between control rows read as captions, not as groups; the header outweighs
  `field-label` on purpose (a field caption names one *value*, a group header names a *subject*, and at equal
  weight they compete). It also removed the **dead** `Border.panel` style — zero consumers app-wide, and a
  near-identical sibling beside the new one is how the wrong one gets picked later.
  ⚠ **Two rules the seam established:**
  the panel height lives in `WorkspaceState.ImportPreviewPanelHeight` (global, like `ResultsPanelHeight`,
  because an import tab is deliberately transient) and **must never enter `ImportConfiguration`** — a layout
  preference is not an import decision (§4.8.2), and the reflection guard would otherwise ship a pixel height
  inside saved profiles; and **no module etap touches `Themes/ControlStyles.axaml`** (density = the separate
  sprint). Suite **5568 green** at that point, build 0/0. **The I6 opening prompt said "insert into the
  finished frame, don't rebuild it" — and that is what it did.** A new tool tab (toolbar, beside
  the Script Executor) that imports Clipboard / TXT / CSV / XLSX into an existing or a newly created table.
  **Read [docs/design/data-import.md](docs/design/data-import.md) — its „📍 STAN IMPLEMENTACJI" block is the
  handover, and the architecture is 🔒 FROZEN: from etap I1 on it is implementation only, and an
  implementation discovery that genuinely undermines the design means STOP THE ETAP AND REPORT, never a quiet
  redesign.** Shape worth knowing before touching it: **one working surface with collapsible sections, NOT a
  wizard** (the user runs the same import repeatedly; the gate is a readiness strip modelled on
  `DebugPreflight`, not Next buttons) · **one pipeline for every source** (a provider yields
  `SourceSchema` + `RawRecord`; the clipboard is not a second parser) · **`ImportConfiguration` is the single
  representation of every user decision** — surface state, pipeline input and profile payload are the same
  record, enforced by a reflection round-trip test that fails the suite when a new setting bypasses it ·
  **rows go to the Data lane as the ONE user working transaction, `CREATE TABLE` to the Ddl lane** (gotcha
  #213 — and the UI says out loud that Rollback will not remove that table). Etap I0 measured three
  corrections into the design; the one that generalises beyond this module is that **a character outside the
  CONNECTION charset is silently written as `?`** (see the spawned platform-wide audit below).
  **I2 as-built (Core only, still zero Avalonia / zero `FirebirdSql` / zero UI):** `ImportValueConverter`
  (strict — a value it cannot be *certain* of is a row error, never a guess) · `ImportRowValidator` +
  **`ImportCharsetGuard`** (NOT NULL / length / precision+scale / **connection-charset representability with
  `EncoderExceptionFallback`** — R1's mandatory guard) · `ImportMappingPlanner` (name-proof matching, the
  sole-remaining-pair rule, `Diagnose`, `Project`) · `ImportReadiness` (the §3.2 strip as a pure function).
  Two foundations came out of the one-owner rule: **`ImportTargetType`** — the single answer to "what type is
  this target column", reusing `SqlValueKind` *in the reverse direction* per §4.6, with an Unknown set kept
  **identical to the export side's** — and **`ImportDiagnostics`** (`IMP0001`–`IMP0027`, codes never messages).
  ⚠ **`ImportErrorKind` gained three client-side members** (`ValueOutOfRange`, `PrecisionWouldBeLost`,
  `UnsupportedTargetType`): additive, and forced by I2's own DoD — "zero silent conversions" cannot be met
  while rounding `1.555` into `NUMERIC(15,2)` has no way to be reported. **No "round it anyway" option was
  invented** (that is a design decision, and §0.1 defaults to refusal).
  **I3 as-built:** `ImportPipeline` — the ONE import, which knows neither what it is reading (a provider made
  the `RawRecord`s) nor whether it is writing (**"Validate" is `DryRunImportWriter` passed as an argument, not
  a second mode**, so a dry run cannot drift from the real thing — there is no second path to drift). It owns
  the **"batch index → source row number" window** (decision D9): a batched write reports failures by position
  in the batch, and the report must name the row the user can find in their file, so the translation happens
  once, here, and nothing downstream ever sees a batch index. Plus `DryRunImportWriter` (a product feature, not
  a test double) and the owed `DelimitedTextImportProvider` (CSV / TXT / **clipboard** — one provider, three
  origins; it resolves the NULL token, which is a property of reading a text field). ⚠ **`ImportOutcome` gained
  `Warnings`/`WarningsTruncated`** (init-only ⇒ additive): §0.2 requires every SHORTENED row in the report, and
  such a row is neither an error (it went in) nor silence (data was lost) — folding it into `Errors` would make
  `RowsFailed` count rows that actually succeeded. Two behaviours worth knowing: the tail flush runs on an
  **uncancelled** token so rows the writer already accepted stay attributable (§0.6, the gotcha-#253
  discipline), and the pipeline **refuses to start** rather than guessing when the mapping names a column the
  target does not have.
  **I4 as-built (the first import code that touches a database):** `FirebirdImportWriter` (`FbBatchCommand`;
  **`MultiError` maps 1:1 onto `ImportErrorPolicy`** so the policy is enforced by the server round trip rather
  than re-implemented client-side; `OVERRIDING SYSTEM VALUE` for a mapped ALWAYS identity; `CommandLock` per
  batch; Data lane, the user's working transaction, **auto-begin and never auto-commit**) ·
  `FirebirdImportTargetReader` (a thin adapter — columns come from the existing
  `FirebirdMetadataReader.ListColumnsAsync`, the codebase's one owner of that question; the only thing it adds
  is the BEFORE INSERT trigger list, decoded through the shared `DecodeTriggerHeader` because
  `RDB$TRIGGER_TYPE` is bit-encoded) · **`FirebirdImportErrorMapper` — the GDS-vector classifier.**
  ⚠ **Two live findings worth carrying forward.** (1) **A standalone `CREATE UNIQUE INDEX` violation leads
  with GDS `335544349`, NOT the `335544665` a PK/UNIQUE *constraint* reports** — I0 measured only the
  constraint form, so until the live run the import reported a duplicate key as a generic `ServerError`
  (gotcha #260; the I0 findings table is corrected in place). (2) **The client guards now fire first for
  NOT NULL, length and numeric range**, so those server branches are only reachable through a trigger — which
  is why the lab gained `IMP_SRV` and its `IMP_SRV_BI` trigger, the only way to exercise the
  `335544321` three-way split against a real engine.
  **I5 as-built (the first import code you can see):** `WorkspaceTabKind.DataImport` + a new **`Icon.Import`**
  (an arrow descending INTO a table grid — deliberately not `Icon.Download`, whose tray means "fetch a file to
  disk"; canonical `.svg` + geometry per the D15.2 icon rule) + a toolbar button beside the Script Executor,
  near-singleton, **added to the `SnapshotCurrentTabs` skip list** (omitting it would not merely fail to
  restore the tab — it would fall through and be captured as a `Ddl` tab, the exact bug the Debugger tab had).
  `DataImportTabViewModel` is the **single owner of `ImportConfiguration`**; `BuildConfiguration`/
  `ApplyConfiguration` is the only UI⇄record translation point (§4.8.6), and sections that do not exist yet
  are **passed through unchanged** so a newer build's profile is not quietly degraded by an older one. The
  readiness strip is a **pure projection** of Core's `ImportReadiness` that maps severity through the shared
  `MessageBanner.BrushKeyFor/GeometryKeyFor` table (§9.3) — no second brush map. ⚠ **Two things worth
  carrying:** detection order is **encoding → delimiter** (a delimiter is looked for in text something already
  decoded, so the reverse would be a guess resting on a guess), and the source preview's "ragged row" marker
  compares against the **majority** field count, not the schema's — the schema reports the WIDEST record so
  every column stays mappable, and marking against that inverts the signal (one row with an extra field would
  flag all the good ones). **Next: etap I6** (the Target section + the Mapping panel) — **gated on §3.8
  being settled first.**
- **📋 BACKLOG (do NOT start) — GLOBAL UI DENSITY / an EmberTern-wide UX sprint.** Raised by the user during
  the Data Import I5 review (2026-07-26) and, in the same breath, **deliberately scheduled for AFTER the
  Data Import module closes**. The finding: EmberTern's ordinary form controls are too tall app-wide —
  `TextBox`, `ComboBox`, `CheckBox`, `Button`, vertical spacing, form row heights — and it must **not** be
  patched per module. **Confirmed in code:** `Themes/ControlStyles.axaml` has **no implicit style at all**
  for `TextBox` / `ComboBox` / `CheckBox` / `RadioButton` / `NumericUpDown` / bare `Button`, so every one
  sits on FluentTheme's defaults (`MinHeight` 32 px); density was only ever applied **ad hoc, one control at
  a time** (`DataGridCell` FontSize 11 + tight padding, `DataGridRow` and `TabItem` `MinHeight="0"`). The
  precedent and the intent exist; the generalisation does not. **⭐ The user's ratified sequencing, which
  reverses my earlier "density first" recommendation — do not re-propose it:** one task at a time; finish
  Data Import to the accepted architecture first, then run a dedicated UX sprint that looks at **every**
  surface at once (SQL Editor, Debugger, Activity Monitor, Session Manager, Script Executor, Data Import and
  the rest) and designs the new global control style from that whole view — not extrapolated from one form.
  Mixing the two makes the scope unreadable and the progress unmeasurable. **⛔ Standing instruction (user,
  2026-07-26, on closing I5): from here on a module etap delivers the module — do NOT initiate global UI
  changes or style refactors.** Avalonia control rebuilds, density, styles and responsiveness all wait for
  the sprint. A **small remark about one specific screen** (the kind that produced `Border.settings-group`)
  may still be fixed in passing; anything that would touch the app as a whole may not. The working line:
  *a control's height* is the sprint, *two controls stacked instead of side by side* is the module.
- **⚠ Spun off from Data Import I0, NOT part of it — a platform-wide defect to audit.** Measured on live FB5:
  binding a string containing a character the **connection** charset cannot represent stores `?` with **no
  error at all**, even when the target column is UTF8 (the connection charset decides, not the column's), and
  `ConnectionProfile.Charset` defaults to `WIN1250`. Triage found it reaches `FirebirdDataEditor` (Table Data
  inline edit/insert) and `FirebirdQueryExecutor` (Smart SQL Parameters); the **statement-TEXT path
  (`FirebirdDdlExecutor`) is unmeasured and could silently corrupt user SOURCE CODE — measure it first**.
  Ratified: a **separate architectural audit**, ONE shared guard (natural home: `CharsetCatalog`, with
  `EncoderExceptionFallback`, never per-module fixes), plus an entry in `docs/gotchas.md`. Deferred by the
  user; escalates if the DDL path is confirmed. Evidence: `data-import-i0-findings.md` §2.8.
- **🏁 PREVIOUS STATE (2026-07-25) — THE FIREBIRD DEBUGGER IS CLOSED.** Everything planned for it is delivered
  and user-QA-confirmed: P1/P2, D1–D13, D15 (D15.6 dropped, D15.7 background), functions-as-root (standalone +
  packaged), the **Draft model** (Seams A + B), the **launch-config rebuild** (C3.1–C3.3b), and **Seam 6d**
  (a compiled object refreshes the other tabs showing it). **Two items stay deliberately deferred, each with a
  ratified brief and neither blocking anything: D14 (Step Back)** and **C3.4** (root frame layout from the AST
  header ⇒ the header gate comes off). Both return only if real usage asks. **The branch `feat/firebird-debugger`
  was merged to `master` at this point.** Next work is elsewhere in EmberTern.
- **⭐ The Draft model is DELIVERED and the launch config survives a signature
  change. Seams A + B + C3.1–C3.3b are COMPLETE and user-QA-confirmed on the live app; the ONE open item is
  C3.4, deliberately deferred pending real usage** (see the C3 entry below). **QA verdict on A+B:** the first
  edit ends the session · the user stays in the editor (`Editing`) · Restart runs the current editor code · the
  procedure in the database stays untouched · `Save` is still the only DDL write · stale code can no longer be
  executed by accident after an edit. **QA verdict on C3:** parameters, history, carry-over and triggers all
  behave as specified, and `Restored` vs `Assumed` read clearly.
  **Background.** After several days of real use the user reported the coherence gap: editing during a session
  was allowed, but the next step still executed the launched version — *I see code A, the debugger executes B*.
  Rule adopted (IBExpert's): **the first change to the text ends the session** — no save, no step, no restart
  needed. **Ratified split, do not re-litigate: `Save` is the ONLY operation that writes to the database;
  `Restart` starts a session from the CURRENT editor text without saving** (an earlier proposal to route a
  dirty Restart through Save→Compile→Launch was rejected by the user, correctly — the debugger never asks the
  server to run the compiled routine, so compiling to restart writes to the DB for no technical reason and
  kills experimenting on a routine without modifying it). **Seam boundaries (the user's own framing):** A =
  *no step can ever run stale code again* (it does NOT start a session on the new text); **B+C** = start the
  session **from the draft** — i.e. the backlogged Seam 6c is BACK, scoped to Restart only.
  **⭐ Seam A — DONE + user-QA-confirmed (commit `aa7f801`; build 0/0; 5230 green; smoke clean).** One rule in one place:
  `ApplySourceEdit` → `OnSourceChanged` → live session ⇒ `EndSessionForEditAsync` (teardown §4.4 + clear
  surfaces + `Phase = Editing`), `Busy` ⇒ condemn-and-end-in-the-operation's-tail, terminal (Completed/Faulted)
  ⇒ keep the retained frame inspectable (values at the fault while fixing it) but drop the position marker.
  Every stepping/eval command was already gated on `Phase == Paused && Session is not null`, so the whole
  toolbar disabled itself with **no per-button work**. **`DebuggerPhase.SaveFailed` → renamed `Editing`**
  (Contract #16): ONE state, two entry reasons (an edit ended the session / a save was refused), one meaning —
  *no session, and the tab keeps showing the source because the code is what matters*. `StopAsync` + the new
  path share `ClearSessionSurfaces()`. **Save is unchanged** (the edit-end arms `_resumeAfterSave` exactly as
  the save-triggered stop does, so Save from `Editing` still compiles → resumes); its *"saving ends the running
  session"* confirm is now near-unreachable but **kept** for the one remaining window (Ctrl+S while a step is on
  the wire) rather than deleted on a reachability argument. **⭐ RATIFIED on acceptance: ending the session is
  PERMANENT** — undoing the edit back to byte-identical text must never resurrect it; Restart may re-enable, but
  only as a deliberate new start (pinned by a test). **⚠ gotcha #253** came out of this and generalises: ending
  an async resource under an in-flight background op doesn't just race — the op throws on return and a generic
  `catch` reports it as a **fabricated** failure of the work (here: a red Firebird "fault" for something the
  user did on purpose); the fix is to let the op's own tail end it, with every `catch` asking "was this
  requested?" first.
  **⭐ Seam B — DONE + user-QA-confirmed (commit `7a410c7`; build 0/0; 5235 green; smoke clean).** The VM's duality is gone:
  **`_baseline`** = what the DB holds (only `IsSourceDirty` + Save read it), **`_editBuffer` + its parse = THE
  PROGRAM** — markers, breakpoint snapping, pre-flight, launch panel and `DebugLaunchSpec` all read it, so
  **Restart starts a session on the edited text with NO compile and NO write to the database.** Re-parse is
  **lazy, NOT debounced** (a `DispatcherTimer` re-introduces a path no headless test can reach, #251): an edit
  marks the program stale, and the few places that need a current parse ask (`EnsureProgramCurrent` — gutter
  click, launch, save); command gates never do. The two duplicated parse blocks collapsed into one
  `ReparseProgram` (+ `AdoptBaseline` as the only place the two texts are declared equal). **Restart ≡ Save
  without the compile:** `SaveAsync`'s tail became the shared `ResumeOnCurrentProgramAsync` (ensure current →
  is the panel still describing this routine? → rebuild inputs if not → re-run pre-flight → relaunch), used by
  both. **Breakpoints survive the loop** via two *provable* filters (never a guess, §0): an offset in the edit's
  **unchanged prefix** still starts the very statement it was set on (kept); one below it may have shifted
  (dropped); and after a re-parse an offset that no longer **starts** a step point is dropped. `AdoptSavedSource`'s
  blanket clear was deleted (Contract #20). **⚠ gotcha #254:** a "before" value must be REMEMBERED where the
  decision was made (`_panelSignature`), never re-derived at comparison time from state that now follows the
  user's input — the old `var configBefore = BuildLaunchSignature()` would, after a mid-flight re-parse, compare
  the new value **with itself** and pass a genuinely changed parameter list as "still valid".
  **⚠ INTERIM shipped in B, and only C3.4 removes it (don't relax it):** a draft runs only while its routine
  **HEADER** is byte-identical to the compiled one (proven without a parse — the common prefix reaches the
  baseline's body start), because the root **parameter list still comes from the catalog**. Body edits — the
  debugging loop — always qualify; a header edit blocks with a status naming Save.
  **⭐ Seam C was RE-ANALYSED against the code before any was written (2026-07-25) and the milestone's premise
  REVERSED — do not restore the old scope.** The App layer was already draft-sourced (panel, signature,
  pre-flight, step points, launch spec — all from the draft), a **trigger root reads nothing about itself from
  the catalog** (`CreateAsync` passes `routineName: null`; NEW/OLD types come from the target TABLE), and four
  of the original six 6c items had shipped in A+B. So the Draft model was **already delivered**; what was left
  split into (a) the engine work that lifts the header gate and (b) rebuilding the **launch configuration**
  after a signature change. The user redirected C3 to (b). Two corrections stand: the **`TYPE OF` regression is
  CREATED by the AST layout, not fixed by it** (the catalog resolves it fine today ⇒ resolving `TYPE OF` belongs
  *inside* C3.4, not "accepted" beside it), and **`RETURNS` does not belong in the launch signature** until a
  changed header can run (it is no user decision — it has no field).
  **⭐ C3 — the launch configuration survives a signature change. COMPLETE + user-QA-confirmed (C3.1 `46e5e67` ·
  C3.2 `1b97b0f` · C3.3a `178f503` · C3.3b `8acbb6f`).** THE RULE (ratified, do not re-litigate): *the debugger
  keeps everything it can PROVE is still correct, hands back everything it cannot, and never guesses* — the
  inverse of IBExpert, which restarts after a signature change without policing the configuration.
  **Proof = equality of `ExecuteParamKind`**; no narrowing analysis (whether a value *fits* is Firebird's job).
  **Matching:** parameters = **`ByName` → `SoleRemainingPair`** (the pair rule fires only when exactly ONE row
  is unmatched on each side; two or more ⇒ nothing carried); trigger NEW/OLD = **`ByName` only**, because a
  column's identity is its name in the catalog and grid position is just the order the body mentions it in.
  `ByName` **claims a pair even when the value fails the proof** (the rows ARE that parameter — leaving a
  retyped one unclaimed would let the pair rule hand its value to a row we can already tell apart). Composition
  lives at the **call site**, never a `bool` flag. **`LaunchValueCarryOver` does NOT implement the proof** — a
  value travels through the history's own `ToHistoryValue`/`ApplyHistoryValue`, so "does this still fit" has ONE
  answer in ONE place. **`ParameterValue.TypeText`** (additive; **schema version deliberately NOT bumped** — a
  bump trips the downgrade protection and older builds would refuse the whole file) makes the history obey the
  same proof; a legacy entry has no type ⇒ not auto-applied (self-heals after one run). **ONE marking
  convention** (`ValueOrigin` Entered/Restored/Assumed) for *every* automatic source — history and same-name
  carry-over → `Restored` (quiet italic, `SubtleForegroundBrush`), the pair rule → `Assumed` (upright,
  semi-bold, `AccentBrush`, the word "assumed"); accent **not** warning — a "worth a look", not a fault. Any
  edit resets the origin, so a marker never claims a value the user has since replaced. ⚠ **gotchas #256/#257**
  came out of this. **C3.4 — the ONLY open item, DEFERRED by user decision:** root frame layout from the AST
  header + `TYPE OF` resolved + `RETURNS` in the signature + §F boundaries in the pre-flight + the mandatory
  `DebuggerFidelityProbe` catalog-vs-draft case (Contract #12); only then does the header gate come off. The
  user will debug normally for a while first — practice decides whether running a changed signature without
  Save is worth the complexity. **§F boundaries for a draft-sourced session — verified in code, do not
  re-derive (all statically detectable from the draft's AST):** **recursion** (a self-call falls through
  `ResolveRoutine`'s local+package branches to `ResolveRoutineAsync`, which fetches the **compiled** source ⇒
  step-into descends into old code — and **step-over does not save you either**, the server runs the compiled
  routine), a **selectable procedure used in its own body**, and a **draft that would not compile** (runs
  partially — PSQL compile-time validation never happened). Narrative: [docs/history/19-...](docs/history/19-firebird-debugger.md)
  (last three sections).
- **Before that (2026-07-25) — two arcs closed; the debugger was idle.** Two arcs closed
  back to back: the **UX Polish Sprint** through **Seam 6b** (6a debugger status → app status bar; 6b
  `SaveAsync` false success), then the debugger's **Save workflow** was reworked after live QA — Save now
  compiles the draft and **resumes the session on the new code** with the settings already made, gated on the
  **launch signature** (object kind + ordered input params, or a trigger's header + referenced NEW/OLD
  columns) still describing the compiled routine; anything else means a new decision and lands on the launch
  panel. *(Seam 6c — the Draft-as-session-source model — was backlogged at that point; real usage asked for it
  days later, and it is **no longer backlog**: it came back as the A/B/C arc above, where A + B are DONE +
  user-QA-confirmed, C was re-analysed + re-scoped into the delivered C3, and only **C3.4** stays open by
  decision.)* Then **🏁 Stage Q (Quick Fixes & Code Actions) — COMPLETE, Q0–Q5, user-confirmed**
  (see the Quick Fix entry below and [docs/history/20-...](docs/history/20-stage-q-quick-fixes.md)).
  Remaining editor backlog: **Folding**, **Breadcrumbs**, and a future **RefactoringEngine** (a sibling
  producer feeding the same code-action menu — nothing was built for it in advance).
- **UX Polish Sprint (Debugger & SQL Editor) — Seams 0–5 DONE + committed + user-QA'd on the live lab; 6a/6b
  DONE. Historical detail below.** (the Seam 6 plan was **replaced** after that QA — read the
  "SEAM 6 REPLANNED" block below; the original Seam 6 = Quick Fix design doc is **deferred**). Goal: **quality,
  not features** — unify the IDE's look, improve readability, fix regressions. No UI rebuild; presentation-first
  where possible; keep the debugger's D1–D4 responsibility split (view/theme changes, never logic pushed into
  VMs). Each seam ends build 0/0 + tests green + committable; **visual changes await the user's QA** (done in
  parallel, not blocking). **Original plan:**
  `C:\Users\grzegorz.gronski\.claude\plans\indexed-launching-sphinx.md` (the approved Seam 0–6 breakdown — now
  **superseded for Seam 6 only**). Commit convention this sprint: **one commit per seam.**
  - **Ratified decisions (do not re-litigate):**
    - **Diagnostics (Item 6)** = a **minimal binder fix**, NOT an ET0003 redesign (see Seam 0 below).
    - **Shared error component = `MessageBanner`** (name chosen over `AlertBanner` — it will also carry
      Info/Warning/Success, not only errors). **Reach (Seam 4):** it becomes the standard on **every main
      work surface where the user executes or compiles code** — object editors (~11) + Execute Procedure —
      with Script Executor + Batch Results + Security Manager as natural follow-ons. **Migration must be
      MECHANICAL** — replace the existing error presentation only; **never change a module's business
      behavior/workflow.** The SQL Editor **Messages log stays a scrolling row-list** (out of scope).
    - **Edit during debugging (Item 5, Seam 5)** = editor is editable **at all times, incl. a live/paused
      session** (no edit-lock at breakpoints). *(Seam 5 as-built is recorded below; its save semantics are
      **superseded by the Seam 6 Draft model** — the buffer stops being a passive draft and becomes the
      session's own source, and Save compiles it in place instead of ending the tab's role.)*
    - **Quick Fix / Light Bulb (Item 7)** — **🏁 STAGE Q COMPLETE + user-confirmed (Q0–Q5, 2026-07-25).**
      Guide: [editor-quick-fixes.md](docs/design/editor-quick-fixes.md); narrative:
      [docs/history/20-...](docs/history/20-stage-q-quick-fixes.md). ⚠ **This holding note was WRONG on two
      points and the design doc supersedes it:** (1) `Diagnostic.QuickFixes` is
      **not** additive — `Diagnostic` is a `record struct` whose value equality the panel relies on to skip
      rebuilds, and a list member degrades that to reference equality ⇒ fixes are computed **on demand** from
      `(model, diagnostic)`, never stored; (2) the fix list does **not** go on the hover card — §15.1.1's
      ratified "plain hover = information, Ctrl = actionability" stands and `HoverInfo` keeps its read-only
      guarantee, so the light bulb + `Ctrl+.` (one shared menu) are the actionable surfaces. Still true: D3
      satisfies the wiring prerequisite, and the adorner is wired only in `SqlEditorBehavior.Attach` (never
      `AttachReadOnlyHighlighting` — a read-only surface must not offer mutating fixes).
      **As-built (commits `3a4a6cb` Q0 · `405290f` Q1 · `40e3b20` Q2 · `358e570` Q3 · `8803627` Q4 ·
      `9e20e2c` Q5 · plus the Q3 QA rounds `156bade`/`a641a43`/`ce70641`/`d0b0d12` and `73380b3`):**
      pure-Core `QuickFixEngine` computes fixes **on demand** from `(model, diagnostic)` — never stored on
      `Diagnostic`, whose record-struct value equality the panel needs — over the **unchanged** read-only
      diagnostics pipeline. Shared currency `CodeAction`/`TextEdit` (`Core.Sql.Language.CodeActions`);
      **`TextEditApplier` (App) is now the ONE owner of every document mutation** — drift check via
      `TextEdit.ExpectedOldText`, descending-order apply, one undo unit, one caret rule — and **safe local
      rename was migrated onto it** (`NavigationRename.Occurrences` gained the binder's per-occurrence text
      so the shared check compares against the MODEL, not the document with itself). v1 actions: qualify an
      ambiguous column (ET0005, one per candidate) + **"Did you mean …?"** for ET0001/2/3/4 when **exactly
      one** candidate is close (`NameSuggestion`, case-insensitive, budget by length, ties ⇒ silence), which
      **keeps the user's capitalisation** and **preserves the `:`/`@` sigil** (dropping it turns a variable
      into a COLUMN inside embedded DSQL). Deliberately refused: ET0006/ET0008 and "declare the missing
      variable" (it would have to invent a type — rule #11). **Four triggers, ONE menu and one selection
      model** — `Ctrl+.`, the light bulb, a single click, and the Diagnostics panel's "Quick Fix…" — all
      through `GetActionsAtCaret` → menu → `InvokeCodeAction`; the panel reaches it via an attached property
      published by the one attach seam (so a read-only DDL preview, never attached, offers nothing).
      **⚠ Three gotchas came out of the light-bulb investigation and are load-bearing beyond this stage:
      #250** (never look a BRUSH up without a `ThemeVariant` — `FindResource` returns UNSET and an
      `SvgIcon` then paints nothing while every observable state looks healthy), **#251** ("added" ≠
      "paints"; a timer-only trigger is untestable headlessly), **#252** (an editor popup is
      keyboard-complete on day one — preselection/↑↓/Enter/Esc + single-click-equals-Enter — handled on the
      TUNNEL, with focus staying in the editor).
  - **Seam 0 — Diagnostics regression → DONE (commit `140547b`).** Root cause: **not a revertible
    regression** — a long-standing gap. `DiagnosticsEngine` (ET0003) never changed; the `:name` path always
    flagged; an unresolved **bare** identifier was never recorded (`SemanticBinder.Psql.BindBareLocal`'s
    `switch (sym)` had no default), a deliberate guard against false-flagging unqualified columns, present
    since the SemanticModel was born (`632bd86`). Minimal fix (exact agreed scope): an unresolved **bare**
    identifier is now recorded as an unknown variable (ET0003) **only in unambiguous PSQL value positions** —
    `BindLeaf` flags `Assignment`/`Return` leaves, `BindControlHeader` flags `IF`/`WHILE`; `BindForSelect`/
    `BindBodyStatement` stay unflagged. Excluded so **no false positives**: query/DML ranges (columns),
    context variables (`ROW_COUNT`/`SQLCODE`/`GDSCODE`/`SQLSTATE`/`INSERTING`/`UPDATING`/`DELETING`/
    `RESETTING`/`USER` — a static `BareContextVariables` set), `NEXT VALUE FOR <seq>` (the `FOR`-preceded
    identifier), `EXCEPTION <name>` names and `LEAVE <label>` labels (those leaf kinds never flag). +11
    `DiagnosticsEngineTests` (both true-positive and each no-false-positive); 3 neighbouring fixtures
    (Diagnostics/Hover/Panel) tightened so their scaffolding vars resolve. **⚠ Side-effect to watch:** the
    binder now emits an extra unresolved-Variable `SymbolReference` for a bare bad name — any future test that
    counts references over PSQL with an undeclared bare identifier may see one more. Build 0/0; 5156 green.
  - **Seam 1 — Shortcut presentation → DONE (commit `d119057`).** New shared `TextBlock.shortcut-chip`
    (ControlStyles.axaml, on-accent). Execute button + debugger launch button both render label +
    shortcut-chip (launch button changed from a `Content` string to a label+chip StackPanel).
    `DebuggerLaunchButton` → `"Start debugging"` + new `DebuggerLaunchShortcut = "F5"`; every "Action (Key)"
    command tooltip normalized to paren-free "Action · Key" (Continue · F5, Step Into · F11, Format SQL ·
    Alt+F, Global Search · Ctrl+Shift+F, Edit selected field · F2, Evaluate … · Shift+F9, Run … · F5).
    Prose/instructional strings left as-is. Pure Presentation; build 0/0; 5156 green.
  - **Seam 2 — Current-line legibility → DONE (commit `6c299e3`).** Token-only: `DebugCurrentLineColor` wash
    alpha raised for VS/Rider-clear legibility, calm blue hue + strong accent bar kept — Dark `#285A8AC8` →
    `#4A5A8AC8` (α ≈16%→≈29%), Light `#1C0033B3` → `#380033B3` (α ≈11%→≈22%). (Note: **Dark** wash is line 55,
    **Light** is line 289 in `Colors.axaml` — verified, agents had them swapped.) Build 0/0; 5156 green.
  - **Seam 3 — Variables panel polish → DONE (commit `475a5c1`).** Ergonomics only (no panel-architecture
    change): value rows got a real right gutter (row padding `4,1` → `8,3,12,3`, inner grid margin dropped,
    group header `6,3` → `8,4`) so values no longer touch the edge/scrollbar. The panel has **no
    row-selection**; the only highlight is the semantic amber **changed-value** wash (D7 — "this value changed
    since the last step"), which the user chose to **keep** (deliberately distinct from selection/current-line)
    — only de-olived to a cleaner warm amber-gold, same low alpha: dark `#C8A000` → `#E0A830`, light `#E0B000`
    → `#D69E24`. Build 0/0; 5156 green.
  - **Seam 4 — shared `MessageBanner` → DONE (commit `1e25ce5`).** The IDE now has **ONE message surface**.
    **4a** — new `Controls/MessageBanner` (`UserControl` + `MessageSeverity` Info/Success/Warning/Error),
    generalized from the debugger Error Bar: `PanelBrush` chrome + a thin severity stripe + severity icon, the
    full message wrapped + selectable (capped `MaxExpandedHeight=190`, then scrolled), optional
    `ShowCopy`/`ShowExpand`/`ShowDismiss` (+ `DismissCommand`), `IsExpanded` **TwoWay**. Severity→brush-key
    **reuses the `DiagnosticRowViewModel` convention** (`BrushKeyFor`/`GeometryKeyFor` are public statics, so the
    mapping is testable); every binding inside the control is an **element binding onto itself** (`#Root.…`), so
    the host's DataContext flows through untouched; chrome overridable per host via `BorderThickness`/`Margin`;
    **Copy is self-contained** (clipboard from `TopLevel`). No `Header` property — nothing needed one (#233).
    **4b** — the debugger Error Bar + the launch Preflight rows migrated (no visual change). Preflight's severity
    split is now the item's own `DebugPreflightItem.BannerSeverity` (blocking → Error, else Warning) so **no
    severity logic stays in the view**. Deleted as redundant: `DebuggerTabView.OnCopyErrorClick` (the banner
    copies) and the VM's `ToggleErrorExpandedCommand` (expand is the banner's own gesture, two-way bound to
    `IsErrorExpanded`, which the VM still owns so a fresh fault re-expands). **4c** — mechanical adoption:
    the `ErrorMessage` status line on **all 10 object editors** (Domain/Exception/Function/Generator/Index/
    Package/Procedure/Table/Trigger/View), the **Execute Procedure/Function failure** in the results area
    (`ExecError`, `VerticalAlignment=Top` so it stays a bar over the hidden grid), the **Execute Procedure
    dialog** validation line, and the **Security Manager** banner; **Script Executor + Batch Results** dropped
    their local `DangerIconBrush` override for the shared `ErrorBrush` (`DangerIconBrush` is for
    destructive-action *icons*). **⚠ Latent bug found + fixed:** the Batch Results headline also set `Foreground`
    **locally**, which outranks any style setter — so its `Classes.error` "failed" tint could never have applied;
    removing the local value realizes the intent. **No module's behaviour/workflow changed.** Deliberately NOT
    migrated (documented scope): the SQL Editor **Messages log** (a scrolling row list), the **in-grid data-error
    empty states** (Table `DataError`/`EditStatusMessage`, View `DataError` — a grid empty-state, not a banner),
    and dialog **field-level** validation hints (`AddFieldDialog`), plus the Procedure/Function `ExecInfo` error
    note inside the compact metrics strip. New `UiStrings.MessageBanner{Copy,Expand,Collapse,Dismiss}Tooltip`
    replace the four `DebuggerError*Tooltip` constants. +2 tests (headless pin: every severity's brush resolves
    in **both** themes + its geometry resolves + the control constructs and re-derives both keys on a severity
    change; `BannerSeverity` follows `IsBlocking`); −1 (the superseded preflight-token pin). Build 0/0;
    **5157 green**; smoke clean.
  - **Seam 4 QA round → DONE (commit `4bd1a6a`).** The user's visual QA found the banner was not yet *one*
    component to the eye; all fixes ratified before implementing. **(1) Full banner unification:** the message
    **text now carries the severity brush** (like the stripe + icon) — an Error message reads as an error in
    full, which was the whole complaint (every non-migrated surface painted its error text red, the banner
    didn't). **Chrome moved out of the control's own XAML into exactly TWO shared variants** in
    `ControlStyles.axaml` — `controls|MessageBanner` (standalone, full border, the default) and
    `.docked` (`0,1,0,1`, a strip attached to a panel edge). **This is load-bearing:** a local value on the
    control **outranks a style setter**, which is exactly how six per-host variations crept in during Seam 4 —
    with chrome in the styles there is nowhere to put a seventh. `ShowCopy` now defaults **true** (not a
    per-host decision). Pinned by a headless test that hosts both variants in a real window and asserts the
    **applied** chrome. **(2) Group A** — 7 stragglers migrated: Procedure/Function `ExecInfo` error, Table
    `DataError` + `EditStatusMessage`, View `DataError`, Performance panel error, Export dialog error.
    **(3) Group B** — `DangerIconBrush` no longer paints **message text**: Trace Monitor error row + error
    detail, Script Executor + Batch Results `result-failed` → `ErrorBrush`. `DangerIconBrush` keeps its real
    job: **destructive-action icons** (trash / stop / rollback). **(4) SQL Editor Messages stays a LOG**
    (timestamp column + scrolling rows, **not** a stack of banners), but a **problem** entry speaks the same
    language — severity stripe + icon + colour — and the mapping is **not re-derived**: `QueryMessageViewModel`
    reads `MessageBanner.BrushKeyFor`/`GeometryKeyFor`, so log and banner cannot drift. Info rows keep the
    normal reading colour and earn no marker (a log is mostly Info; greying it all costs legibility for no
    signal) — that is what `MessageBrushKey` vs `SeverityBrushKey` encodes. **This also collapsed a genuine
    duplicate: two `MessageSeverity` enums (`Controls` + `ViewModels`) became one** (`Controls`, now with
    `Success`). **(5) Variables changed-value** left amber for the debugger's **current-line hue** (dark
    `#3D5A8AC8`, light `#330033B3` — same base as `DebugCurrentLineColor`): "this changed" is part of the
    debugger's visual language, not a separate accent; the two never share a control, and alpha sits just under
    the editor wash because `PanelBrush` makes the same value read stronger. **(6) `DebuggerIcon`** breakpoint
    dot 6 → **4.5** at the same centre (16, 15.5) and the same tip overlap — an accent on the pointer, not a
    second subject; canonical `Assets/Icons/Debugger/debugger.svg` updated with it. **Left out by decision:**
    dialog **field-level** validation hints (`AddFieldDialog` et al. — a hint beside a field, not a module
    message). Build 0/0; **5159 green** (+2). **⚠ Rule going forward: a `MessageBanner` host sets only
    `Severity`, `Message`, `IsVisible`, optional `Classes="docked"` and layout `Margin`/`Grid.*` — never
    `Background`/`BorderBrush`/`BorderThickness`/`ShowCopy`.**
  - **Build/Test/Smoke:** build 0/0 and full suite green after every seam (**5156** through Seam 3, **5157**
    after Seam 4, **5159** after the Seam 4 QA round). Smoke: the app launches clean. The visual results of
    Seams 0–4 (bare-var squiggles, shortcut chips, current-line wash, Variables spacing + the new blue
    changed-value wash, every message banner, the Messages-log problem rows, and the smaller debugger-icon dot,
    in **both** themes) **await the user's visual QA**.
  - **Seam 5 — edit code during debugging → DONE (5a `7401060` · 5b `846ecf2` · 5c `19aa236`).**
    **5a (editable editor + change tracking):** the debugger source editor is a **normal editor at every
    phase**, incl. a live/paused session — typing never ends a session, *saving* does. The hazard was that
    stepping rewrites the display on every frame change, so the **edit buffer is now separate from the
    frame-source display**: `_source` = what the DB holds (the baseline the running session was compiled
    from), `_editBuffer` = the root routine's editable text, `SourceText` = the buffer for the **root** frame
    / that frame's own source otherwise. **ONE helper `SourceForFrame(frame)` makes that choice and all four
    `SourceText` assignments go through it.** `IsReadOnly` survives with a narrower, **structural** meaning —
    while a callee/caller frame is selected the editor shows *another routine's* source, which this tab cannot
    save; `IsSourceEditable` says so and the VM **also rejects the edit**, and frame selection runs through the
    one `SetSelectedFrame` funnel that raises it. `IsSourceDirty` is a **diff**, not a flag (edit back → clean).
    Editor text flows both ways (`SyncEditorText` suppresses its own write). The three debug renderers already
    clamp to the document, so a shortened buffer can't make them throw.
    **5b (save + compile):** reuses the object editors' machinery — same Ddl-lane `FirebirdDdlExecutor`, same
    `ConfirmRequest` dialog, same `ISavableObjectEditor`. Flow: live session → warn plainly → `Stop` (rollback
    + close attachment, §4.4) → compile → on success adopt the text, **re-parse**, and land **ReadyToLaunch
    with the pre-flight re-run against the NEW code**; on failure the error goes to the shared `MessageBanner`
    Error Bar and the buffer is kept. Declining returns failure without touching session or buffer (Cancel is a
    real cancel). **Breakpoints are cleared on save** — their offsets belong to the old text and "keep the line
    number" would silently move them (§0). **⚠ A PACKAGE member tab can NEVER save** (`IsSavable` false): its
    source is *reconstructed* as a standalone `CREATE PROCEDURE/FUNCTION`, so compiling it would create a
    standalone routine instead of altering the package (rule #11) — the refusal is **in the VM**, not just the
    wiring. Toolbar Save + **Ctrl+S**.
    **5c (close guard):** `DebuggerTabViewModel` implements `IUnsavedWorkSource`; `WorkspaceTabViewModel`'s
    `UnsavedWork`/`SavableEditor` stop returning null for `Debugger` (`SavableEditor` is **conditional** —
    a package-member tab reports its work but is never offered Save). **Per-tab close promoted from
    Discard/Cancel to three-way Save / Discard / Cancel** for every tab with somewhere to save — benefits ALL
    editor tabs, matching disconnect + app close. **⚠ Hardening worth knowing:** the close proceeds only if
    `tab.UnsavedWork` is genuinely null afterwards, **not merely if `SaveAsync` reported success** — the
    adapters can return success without compiling anything (no `DdlExecutor` ⇒ `ExecuteCompileAsync`
    early-returns, `ErrorMessage` stays null), and this is the one place where a wrong "success" destroys code.
    *(The adapter contract itself is a separate fix — see the spawned task.)* Three existing close-guard tests
    moved `ConfirmationRequested` → `ChoiceRequested` (Confirmed → Discarded). Build 0/0; **5169 green** (+12
    across the three seams); smoke clean. **Live QA still owed:** the successful save+compile path needs a
    server (edit a paused routine → Save → warning → session ends → recompiles → ReadyToLaunch with the new
    code; a deliberate compile error surfaces in the Error Bar; closing a dirty debugger tab offers Save).
  - **⭐ SEAM 6 REPLANNED (2026-07-24) — the old "Seam 6 = Quick Fix design doc" is SUPERSEDED and DEFERRED.**
    Live QA of Seam 5 produced findings that change the debugger's architecture; the new Seam 6 plan below is
    **ratified — do not re-analyse it, do not re-litigate the decisions.** A new session starts at **Seam 6a**.
    - **⭐⭐ THE ARCHITECTURAL DECISION — "Draft is the session's source".** QA found that editing in the
      debugger did **not** affect the session: even Restart re-ran the OLD code. Verified in code —
      `LaunchAsync` builds `DebugLaunchSpec(_source, _body, _model, …)`, i.e. the DB baseline; the edit buffer
      never reaches it. **But the investigation showed this is a removable limitation, not a law:** the
      debugger **never asks the server to run the compiled routine**. It is a client-side interpreter — control
      flow from the AST, every statement executed as an anonymous `EXECUTE BLOCK` **harness that never names the
      routine**. For a ROOT frame the catalog is consulted for exactly ONE thing: **parameter / `RETURNS`
      types** (`FirebirdDebugMetadata.ReadProcedureParametersAsync`). Locals (R3 verbatim), sub-routines (R5),
      the body and every statement already come from the **parsed text**; a trigger's NEW/OLD types come from
      the target TABLE, not the trigger. **And that one dependency is already solved in this repo:**
      `FirebirdDebugMetadata.BuildLocalRoutineFrameVariablesAsync` (D9) builds a complete frame for a local
      `DECLARE PROCEDURE` — which has **no catalog row at all** — by reading param/`RETURNS` types from the AST
      header (`PsqlDeclarationExtractor.ExtractSignature`), resolving base types via `RDB$FIELDS` (domains exist
      independently of the routine). **A draft-sourced ROOT is that same proven path applied one level up.**
      This is why IBExpert can run edited code without saving — same reason, not a trick.
      **Fidelity Law holds** (Firebird still computes every statement's semantics), but it introduces a **NEW
      §F boundary class** to disclose: wherever the **server** resolves the routine BY NAME it gets the
      COMPILED version while the client interprets the draft — (1) **recursion stepped OVER**, (2) a
      **selectable procedure used inside its own body**, (3) a draft that **would not compile** runs partially
      (Firebird's PSQL compile-time validation never ran). (1)+(2) are statically detectable from the draft's
      own AST — the pre-flight already does exactly this kind of scan (§4.6).
      **⛔ REJECTED alternative — temporary DDL** (`ALTER PROCEDURE` in the debug transaction + `ROLLBACK`):
      gotcha #213 (a transaction cannot use an object whose DDL it has not committed) is precisely this
      pattern, "no temporary metadata" is a binding Developer-Contract rule, and it is **unnecessary** since
      the harness never references the object.
    - **Ratified model (IBExpert-like):** the first edit **automatically ends the live session**; the user
      **stays in the debugger tab**; **Restart / Launch build the session from the DRAFT**; the database is
      untouched until Save; the debugger never runs the old code after an edit.
    - **⭐ SAVE — THIS REVERSES THE SEAM-5b/hand-off DIRECTION.** The debugger is now a first-class
      **iterative** debugging surface: `Edit → Restart → Test → Edit → Restart → Test → Save`. **Save compiles
      the current draft, writes it to the DB, and KEEPS the user in the debugger tab**, refreshing session
      state to the new version. **No hand-off to the object editor** (the earlier plan to transfer the code to
      the Procedure editor is dropped). **Easy Mode stays the classic DDL editor's** — the debugger saves the
      **source text only**.
    - **Seam order (ratified):**
      **6a — Debugger status → the app Status Bar. ✅ DONE (`0587307`).** The Save button left no room for the message after
      `Break on exception`. Move `DebuggerTabViewModel.StatusText`/`IsFaulted` presentation out of the debugger
      toolbar into the bottom status bar, freeing the toolbar for `Break on exception` + future buttons.
      **NO second mechanism:** the debugger VM is unchanged — `MainWindowViewModel` already exposes
      `ActiveDebugger` + `IsDebuggerTabActive` (the gotcha-#25 notify chain) and Avalonia propagates the nested
      VM's `PropertyChanged`, so this is a **pure XAML binding** in the status bar's column 1 beside
      `QueryStatsText` (each `IsVisible`-gated), with `Classes.error` on `IsFaulted` reusing the shared
      `.error` style. Note the bottom status bar (`MainWindow.axaml`, `Grid.Row=2`) shows connection state
      (`MainWindowViewModel.StatusText`, owned by `UpdateStatusFromConnection`/`SetError`/`ClearError`) — do
      **not** write debugger status into that property; it is a second owner of one field.
      **6b — `SaveAsync` false success. ✅ DONE (`33d4e88`).** `SourceObjectDetailTabViewModel.ExecuteCompileAsync` has TWO silent
      exits (`if (DdlExecutor is null) return;` and `if (string.IsNullOrWhiteSpace(sql)) return;`) that leave
      `ErrorMessage` null, so the adapter reports **Success = true having compiled nothing**; the same shape
      exists in every other `ISavableObjectEditor`. Make them set `ErrorMessage` so the adapter tells the
      truth. Side effect to accept + state: Compile on empty source stops being a silent no-op. **Keep** the
      Seam-5c defensive check (`tab.UnsavedWork is not null` after a save) as belt and braces.
      **6c — the big one: rebuild the debugger on the Draft model. ✅ SUPERSEDED — it came back days later as
      the A/B/C arc at the top of "Current state": Seam A (`aa7f801`) + Seam B (`7a410c7`) are DONE +
      user-QA-confirmed (first edit ends the session; Restart runs the draft with NO compile and NO DB write;
      Save is the only DB write). Seam C was then re-analysed, re-scoped and delivered as **C3** (the launch
      configuration survives a signature change), leaving only **C3.4** (root frame layout from the AST header
      ⇒ the header interim disappears, `TYPE OF` resolved, pre-flight surfaces the draft §F boundaries, and the
      mandatory `DebuggerFidelityProbe` catalog-vs-draft case) — deferred by decision.**
      Two details of the original 6c sketch were **changed by the implementation**, do not restore them: the
      re-parse is **lazy, not debounced** (a `DispatcherTimer` re-introduces an untestable path, #251), and
      Save was already delivered separately (it compiles the draft and resumes in the tab).
      **6d — refresh every open tab showing the same object after a successful compile. ✅ DONE + user-QA-confirmed
      (commit `6afef20`).** Root cause (confirmed, not a cache): **nothing subscribed the notification.** The
      editors already raised `CompiledExistingObject` after compiling an existing object and **nobody listened**;
      `Metadata.RefreshAsync()` refreshes the object *tree*, not open tabs' loaded source. **NO stale cache** —
      the readers read live and the Metadata lane uses implicit per-command transactions, so committed DDL is
      visible. As built: the debugger raises the **same** event after its save (no debugger path); the one
      subscriber lives on `WorkspaceTabs.CollectionChanged` (ONE wiring point, not the ~39 add sites, so a future
      tab kind is covered without anyone remembering); sibling lookup keys on **(kind, name)** exactly like
      `CloseTabsForObject` and the open/focus dedup; the reload is each editor's own `RefreshAsync` via a new
      `WorkspaceTabViewModel.RefreshAsync()` that mirrors the `SavableEditor`/`UnsavedWork` per-kind dispatch.
      **Two ratified exclusions:** a tab with **unsaved work is never refreshed** (`RefreshAsync` reloads and
      clears dirty ⇒ refreshing a dirty sibling would destroy edits — rule #11), and a **debugger tab is never a
      refresh TARGET** (reloading resets the source its session was built from + tears down a live session — the
      Draft model's business). Decision (`TabsNeedingRefreshAfterCompile`) is separated from the doing so it is
      assertable without a DB. **⚠ Refactor forced by 6d:** a debugger tab hard-coded `ObjectKind = Procedure`
      (harmless while nothing read it) — `CreateDebugger` now takes the real kind, **required** so the compiler
      enumerated the call sites. ⚠ **`OfferRecompileDependentsAsync` remains DEAD CODE** — it was this event's
      intended consumer and has never been called; reviving it changes what Save does and was left alone
      deliberately. Gotcha #258.
    - Grounding facts kept from the Seam-5 plan: `ISavableObjectEditor.SaveAsync` pattern in
      `SourceObjectDetailTabViewModel.cs` (~`:489-544`); `MainWindowViewModel.RequestCloseTabAsync` is now
      three-way; the debugger's launch path is `OpenDebuggerForObject` / `OpenDebuggerForPackageMember`.
      **⚠ A debugger tab has no `MetadataObjectKind`** — 6c/6d may need it threaded through (additive).
      **⚠ kill any lingering `EmberTern.exe` before rebuilding** (locks output DLLs → MSB3021).
- **Stage X — Firebird Debugger. ⭐ STATUS (2026-07-25): P1 + P2 + D1–D13 DONE, D14 deferred, D15 complete
  (D15.6 dropped), functions-as-root complete, the Draft model delivered (A + B + C3), Seam 6d done — all
  user-QA-confirmed. **The debugger is CLOSED**; D14 and C3.4 stay deferred by decision — see the top of
  "Current state". The block below is the accumulated
  per-milestone record; read it for the "why", not for status.** Historical detail: **implementation STARTED; P1 + P2 + D1–D9 DONE. D9 (local procedures & functions — THE FLAGSHIP) COMPLETE + live-fidelity-verified (core 2026-07-18, seam c 2026-07-19): local routines are real, steppable debugger frames with real closure variables (the capability IBExpert cannot deliver); both local procedures and local functions are faithful step-into AND step-over. §6.3 closure version gate MEASURED (FB3 = closed scopes, FB5 = true closures; frame LexicalParent branches on version); seam (a) = local-routine step-into (AST + parser + binder + extractor R5; runtime ResolveRoutine + AST-header param types); seam (b) = closures — Part 1 closure capture for step-INTO (read+write an OUTER var, the write reaching the parent frame), Part 2 the transitive read/write-set fixpoint over the sub-routine call graph for step-OVER (a local call whose callee captures an outer var not named at the call site). All proven sim==real on the lab. D9 seam (c) — local-FUNCTION step-into (§6.4) — COMPLETE 2026-07-19 (c1 AST → c2 Core interpreter → c3 Firebird executor + live fidelity): a local function is a real steppable frame in all four value-consuming positions (v=f()/RETURN f()/IF f()/WHILE f()) via a Function Return Continuation, no new server path; live-proven sim==real for the four positions, six return types (INTEGER/BIGINT/NUMERIC/VARCHAR/BOOLEAN/NULL), shadowing (local shadows a same-named stored function), nesting, and closures (spec §15.11). 🏁 D9 FULLY COMPLETE — local procedures AND functions step faithfully, into and over. 🏁 D10 (Triggers) COMPLETE + user-confirmed 2026-07-19 (seam A pure-Core / seam B live-fidelity / seam C UI; NEW/OLD context, multi-action, embedded-subquery colon fix #248) — PLUS terminal debug states (Completed keeps state + END marker, Faulted stops on the raising line + red status). D10.5 (UX polish) DONE 2026-07-19: the debugger's internal harness-audit tab (formerly "Executed SQL" — a misleading name; it was never the user's SQL history) is renamed "Harness Log" and made a DEBUG-only diagnostic surface — it is built in code-behind under `#if DEBUG` (`DebuggerTabView.axaml.cs` → `BuildHarnessLogTab`, no longer in the XAML), so in RELEASE builds the tab does not exist at all (not hidden / not disabled — genuinely not compiled). No new setting/toggle (user rejected reusing the per-connection DDL Developer Mode — different domain). The audit log itself (`DebuggerTabViewModel.ExecutedSql`) is unchanged and still collected in every build — it also feeds the Immediate tab — so Immediate / Evaluate(Shift+F9) / Watches are untouched. A purpose description + empty-state explain the tab is a debugger-internals diagnostic, not user-SQL history. Build 0/0 in BOTH Debug and Release; 4929 tests green. 🏁 D11 (Packages) COMPLETE + user-confirmed 2026-07-20 (Seam 0 / A / B / C) — packaged procedures (public AND private) are real steppable debugger frames, reached both by stepping into them from a caller and by launching a member directly as the ROOT; one execution path, no parallel package executor, live-fidelity-proven. **Seam 0 (lab + blocking probes, commit f229b94):** extended the lab with `PKG_DBG` (public `PUB_RUN`/`PUB_ADD` + a PRIVATE `PRIV_DOUBLE`; a private and a public sibling call inside `PUB_RUN`) + standalone selectable `SP_DBG_PKG`; §8.2 probes measured live (§15.12) — a PRIVATE package routine is NOT callable from `EXECUTE BLOCK` (SQLSTATE 42000 "is private to package") ⇒ interpret it; a PUBLIC one IS (⇒ real step-over / source-fetch step-into); the whole body is verbatim-extractable from `RDB$PACKAGE_BODY_SOURCE` (parse the blob). **Seam A (pure Core, commit ead3a41):** `ExecuteProcedureStatement.PackageName` + parser reading a qualified `PKG.PROC` (binder still references the package at the first name token; `SqlParameterScanner` returns the qualified name); new `SqlParser.ParsePackageBodyMembers` turns a body blob (`BEGIN <members> END` of bare `PROCEDURE/FUNCTION` routines = the D9 sub-routine shape WITHOUT `DECLARE`) into member `SubroutineDeclaration`s (`ParseSubroutineDeclaration` generalized to both leading forms; `ParseScopedBlockBody` reused — no hand-rolled scanner); private-ness stays a metadata fact. **Seam B (Firebird + live fidelity, commit e07ad40):** `ResolveRoutine` resolves a package call (qualified `PKG.PROC`, or an unqualified SIBLING from within a package frame) through ONE path — the member is reconstructed as a standalone `CREATE PROCEDURE` (`"CREATE "` + its `RDB$PACKAGE_BODY_SOURCE` slice) so the **D8** path (scope-bound model+body, catalog params keyed by `RDB$PACKAGE_NAME` via a generalized `BuildFrameVariablesAsync`, arg seeding) applies to a PUBLIC member, while every package routine is declared as a harness sub-routine (**D9 R5**) so a PRIVATE sibling — not DSQL-callable (§15.12) — runs inside the harness like a local routine; a package member is a closed scope (`LexicalParent` null, no capture ⇒ the read/write fixpoint is a no-op) so `ExecuteStatement`/`EvaluateCondition`/`BindValues` are untouched. Public members maximally reuse D8, private maximally reuse D9 — **NO parallel package executor** (user directive). Live fidelity PROVEN sim==real (§15.13, `DebuggerFidelityProbe` +2): case 18 step-**Into** `SP_DBG_PKG → PUB_RUN → PRIV_DOUBLE (private, interpreted) + PUB_ADD`, depth 3, sim 16==real 16; case 19 step-**Into** `PUB_RUN` then step-**Over** its siblings — the private `PRIV_DOUBLE` runs via the R5 harness (depth 2, never a frame), sim 16==real 16; all 17 prior cases still pass. **Seam C (launch a member as the debug ROOT — C1 engine `fd50411`, C2 UI `0b6259d`):** the "Debug procedure…" entry point on a package member, launched as the root. **C1** — `SqlParser.ReconstructPackageMemberSource` is now the ONE owner of the `"CREATE "` + member-slice reconstruction (the seam-B step-into path was refactored to route through it); `FirebirdDdlReader.FetchPackageMemberSourceAsync` reads the raw `RDB$PACKAGE_BODY_SOURCE` blob and reconstructs the member's standalone source (the App/probe root source provider); `FirebirdDebugExecutor.CreateAsync` gained an optional `packageName` that builds a package ROOT frame exactly as seam B builds a stepped-into member (package-keyed catalog params + every package routine as a harness sub-routine R5 so a sibling resolves + package/members on the frame context; closed scope ⇒ Execute/EvaluateCondition/BindValues untouched); `DebugLaunchSpec.PackageName` (additive) threaded through the launcher; `DebuggerTabViewModel` gained a `packageName` arg; `MainWindowViewModel.OpenDebuggerForPackageMember` reuses the `OpenDebuggerForObject` launch shape (same §9.3 parameter panel + launcher). **Live fidelity PROVEN** (`DebuggerFidelityProbe` case 20): `PKG_DBG.PUB_RUN(5)` launched as ROOT steps into private `PRIV_DOUBLE` + public `PUB_ADD`, chain `PUB_RUN → PRIV_DOUBLE → PUB_ADD`, sim R 16==real 16; all 19 prior cases pass. **C2** — the Package editor → Members tab "Debug procedure…" context menu (visible only for PROCEDURE members via `PackageMemberItemNode.IsProcedure`; a function-as-root is out of scope, §F), reusing the sidebar's `MetadataContextDebugProcedure` label + mirroring the tab's double-click code-behind; `PackageDetailTabViewModel.DebugMemberRequested` → the C1 launch path. No sidebar member leaf (packages don't expand) and no toolbar button (which member? = a new workflow) — the Members tab is the unambiguous entry point. Build 0/0; package/debug tests green; smoke clean; user QA confirmed 2026-07-20. **§F boundary:** a package FUNCTION call as a step-into is not modelled on the call side (`CallExpression` carries no package qualifier) — step-over, faithful — add only when a real lab case needs it (gotcha #233).
  **D12 (Advanced breakpoints) — COMPLETE + user-confirmed (2026-07-20). 🏁 Break on exception, conditional
  breakpoints + hit counts, data breakpoints, and run-to-next-`SUSPEND` (+ result grid) all ship end-to-end,
  live-fidelity-proven. Seams 0 / A / B / C1 / C2 / D / E1 / E2 all DONE.**
  Scope (spec §9.8): break on exception, conditional breakpoints + hit counts, data breakpoints, run to next
  `SUSPEND` (+ result grid). Split into seams (each pure-Core-first, additive, one committable seam per session,
  ending build 0/0 + green tests + user QA): **Seam 0** — lab verified + the deterministic `SP_DBG_LOOP(N)`
  workhorse (one routine serves run-to-SUSPEND / hit-count / conditional / data-bp; `SP_DBG_LOOP(5)` →
  `(1,5),(2,15),(3,30),(4,50),(5,75)`), commit `211a629`. **Seam A — Break on Exception** (`a8b160a`): a raise
  can PAUSE at the raising statement (`Paused`/`StopReason.Exception`, frame intact) BEFORE routing, then the
  next resume routes it through the very same `ExceptionRouter` path — a pause, never a second handler. The
  inline raise-handling was extracted into one `RouteRaisedException`; a held raise is `_pendingRaise` (raising
  step + pre-routing stack snapshot); `SetNextStatement` abandons it. `StopReason.Exception` now pairs with
  `Faulted` (terminal, unchanged) OR `Paused` (a break — `DebugSession.IsPausedOnException`). New
  `BreakOnException` (default false). **Seam B — Conditional breakpoints + hit counts** (`f9beba7`): `Breakpoint`
  became a **stop-policy object** (`Offset`, `Condition`, `HitCount` policy, hit tally; `ShouldBreak(satisfied)`
  = the pure policy — false/NULL never counts, true increments + breaks iff the hit-count policy is met) +
  `HitCountPolicy` value object (`Always`/`Exactly`/`AtLeast`/`Multiple`, `IsMetAt`) + `HitCountKind`;
  `BreakpointSet` promoted `HashSet<int>` → map `offset → Breakpoint` with the whole prior API byte-compatible
  (+ `Get`/`GetOrAdd`; `All` deferred to Seam E). A condition is **just an expression through the ONE engine** —
  new `IDebugExecutor.EvaluateCondition(string, int, Frame)` reuses the SAME typed-`BOOLEAN` `EvaluateExpression`
  path as `IF`/`WHILE` (no second evaluator; `NULL` → not-true, else `Convert.ToBoolean`, never parsing boolean
  text — §F). `DebugSession.ShouldBreakAt` evaluates it in the correct frame; a condition that RAISES stops +
  surfaces (`BreakpointConditionError`), never silently skipped. **Seam C1 — Data breakpoints** (`5561e10`):
  the watched value as **another stop policy** — `DataBreakpoint` (owns `ShouldBreak(old,new)` = "changed?",
  `NULL`/`DBNull` equivalent) + `DataBreakpointSet` (collection AND the LOCAL detection: `Snapshot(frame)` →
  `FindChanged(before, frame)`, names resolved via the scope chain so a closure var is watchable). `RunStepping`
  snapshots before a step and diffs after **only when the innermost frame is unchanged** (a frame-identity gate
  mirroring the D7 change-highlight — a step-into/out can't false-positive; a cross-frame change on return is a
  documented boundary); new `StopReason.DataBreakpoint` + `DataBreakpointHit`. Purely client-side (no server
  round-trip → no Firebird change, no live-fidelity for C1). **Seam C2 — Run to next `SUSPEND`** (this
  session): a new **run mode** `StepKind.RunToSuspend` (runs full speed like Continue; `StepPlanner` returns
  false — the stop is the SUSPEND *event*, not a movement decision) pausing with the pre-existing
  `StopReason.Suspend` at the step point AFTER a `SUSPEND` emits a row — a selectable procedure's "give me the
  next row". Detection is a **row-count delta** (`_emittedRows` grows only on `ExecutionStatus.Suspended`), so
  no new signal on `ExecuteCurrent`/the outcome; public `DebugSession.RunToSuspend()` (mirrors
  `RunToCursor(int)`), `Step(kind)` rejects it. Precedence mirrors data-bp (checked right after it, wins over a
  line stop, same coincidence boundary); a breakpoint *before* a SUSPEND still stops. Result grid over
  `EmittedRows` is Seam E. All five Core seams are **pure Core + additive** (no
  App/UI wiring — that is Seam E; off by default → existing sessions byte-identical). **Seam D — Firebird +
  Live Fidelity (probe-only; no production code):** the most important seam — it proves every D12 mechanism
  behaves identically on the real engine AND, per the user's directive, stops at the same **logical moment** of
  execution (not just the same final result). Every mode is a client-side stop policy over the interpreter that
  already drives the REAL executor, so the values + the SUSPEND/condition/change/raise that triggers each stop
  are computed by Firebird; `DebuggerFidelityProbe` (+6 cases 21–26, new `SimulateStopsAsync` capturing the
  **sequence of stops**) proves sim==real live on FB5, grounded on `SP_DBG_LOOP(5)`'s real per-iteration
  `(IDX,ACC)`: **21** run-to-SUSPEND = 5 stops one-per-SUSPEND in order, each == real row; **22** run-to-SUSPEND
  over the D6 cursor; **23** conditional `IDX=3` stops exactly once (skips iters 1–2, condition evaluated on the
  engine); **24** hit-count `Exactly(4)` stops on the 4th arrival; **25** data-bp on `ACC` stops on every change
  `0→5→15→30→50→75`; **26** break-on-exception pauses AT the raise (before routing, frame intact) then routes
  through `WHEN` to `CAUGHT`==real, break-off identical (one path). All 20 prior D8–D11 cases still pass →
  `DebuggerFidelityProbe` **26/26 ALL PASS**. Build 0/0; debugger tests green (unchanged — Seam D added
  no production code). **Seam E1 — Breakpoints panel + Break-on-Exception toggle + data-bp gesture (`cab2f5b`):**
  the panel is a **pure VIEW of the Core `Breakpoint` / `DataBreakpoint` objects** — the VM keeps ONE
  `BreakpointSet` + one `DataBreakpointSet` as its own store, which the session SHARES (no mirroring), so a row's
  condition / hit-count / enable edit mutates the very object the engine consults. `BreakpointRowViewModel` /
  `DataBreakpointRowViewModel` are dumb projections; the hit-count kind→operand-enabled and condition editing
  route through Core (`HitCountPolicy.Of`). **Seam E2 — Run-to-`SUSPEND` command + Results grid (`9a96bb3`):**
  the toolbar Run-to-SUSPEND command drives `DebugSession.RunToSuspend()`, and a Results grid renders
  `EmittedRows` (reusing the shared grid). **Two manual-QA fixes:** (a) **gutter hit-test (`c8fc061`)** — the
  breakpoint margin was not hit-testable, so a gutter click never registered; fixed so toggling a breakpoint
  works. (b) **first-statement breakpoint (`863c89d` → refined + superseded by `01b66dd`)** — a breakpoint on the
  FIRST executed statement was always skipped. **Root cause:** `RunStepping` made the breakpoint stop-decision
  AFTER executing the current statement and advancing, so it structurally could not decide the statement a run
  command RESUMES from; launch always pauses at Entry on the first statement and the gutter only appears once a
  run is live, so the user's first breakpoint sits exactly on the statement the first Continue executed
  unchecked. **Fix — ONE pre-execute gate (`TryStopBeforeExecuting`), one semantics, no first-statement branch:**
  the stop decision now runs BEFORE executing the statement the IP points at, for EVERY statement in EVERY run
  mode (`Start` decides nothing — Entry is a pre-execution pause). A **resume-guard** lets a run command LEAVE the
  statement it is sitting on (no double-stop): it suppresses re-breaking the current statement when the pause was
  a delivered arrival OR the command is an explicit movement (Into/Over/Out step away → Step behaviour unchanged,
  per DoD); the sole un-guarded case is a RUN command resuming from Entry, so a breakpoint set at entry fires on
  the first resume like any later arrival. Routing a held raise is a different operation ("about to route", not
  "about to execute") and bypasses the gate. **Final D12 architecture (ratified):** `Breakpoint` is a **domain
  stop-policy object** (offset + `Condition` + `HitCountPolicy` + hit tally, `ShouldBreak` the pure policy), not
  a bare offset; `HitCountPolicy` is an immutable value object (`Always`/`Exactly`/`AtLeast`/`Multiple`);
  `DataBreakpoint` is another stop policy (change detection, NULL≡DBNull) with client-side snapshot→diff;
  Run-to-`SUSPEND` is a run mode whose stop is the SUSPEND *event*; Break-on-Exception is a pause-before-routing,
  never a second handler; a **condition is just an expression through the ONE D5 evaluation engine** (no second
  evaluator); **ONE breakpoint model is shared by the VM and `DebugSession`** (the panel edits the domain objects
  directly); there is **ONE decision point "before executing a statement"** for every run mode; and **Live
  Fidelity (`DebuggerFidelityProbe` 26/26 sim==real on FB5)** is the proof each mechanism matches Firebird's real
  values AND stop-moment. Build 0/0; full suite **4998 green**; user QA confirmed 2026-07-20 → **D12 formally
  closed.** D14 (Step-back) remains **optional — build only if real usage asks.**
  **D13 (Fast Forward — loop fast-forward) — COMPLETE + user-confirmed 2026-07-20 (Seam 0/A/B/C + docs-close
  Seam D). 🏁 Two commands ship end-to-end, live-fidelity-proven + manual-QA-confirmed: Continue Until Loop Exit
  + Next Iteration.** Scope
  (accepted, deliberately small): exactly **Continue Until Loop Exit** +
  **Next Iteration** — nothing else (Skip Current Iteration rejected = a control jump / new path; Continue Until
  RETURN deferred; Continue Until Exception / Variable-Changes / END subsumed by D12). **Hard constraint: no new
  execution path — Fast Forward only *controls* the existing `DebugSession`.** Both are pure stop policies on the
  **D12 `RunToSuspend` pattern** — new `StepKind.RunToLoopExit`/`RunToNextIteration`, `StepPlanner` returns
  false, the stop is a **loop-lifecycle event decided in `RunStepping`'s tail** (innermost loop captured once at
  the command; Loop Exit = the loop activation left the control stack, Next Iteration = its iteration counter
  incremented). **`IsInsideLoop`** gates the commands; `Step(kind)` rejects them; breakpoints inside the loop
  still win (pre-execute gate). **Seam 0 (`1049c71`):** 4 deterministic lab workhorses (`SP_DBG_LOOP_NESTED`/
  `_LEAVE`/`_BREAK`/`_EXIT`, `.fdb` rebuilt, live-verified) — and it revealed that the interpreter modelled **no
  `LEAVE`/`EXIT` control flow** (both fell to the server path → `LEAVE` faulted, `EXIT` was silently ignored).
  **Seam A (`3fd541e`):** the two run modes + a **user-ratified correctness patch** folding minimal
  `LEAVE`/`BREAK`/`EXIT` into `ExecuteCurrent` (EXIT → `Frame.ExitRoutine` terminates the frame; unlabeled
  `LEAVE`, and `BREAK` which the parser now maps to `PsqlLeafKind.Leave`, → `Frame.LeaveInnermostLoop`), plus a
  minimal `LoopActivation` base (only the iteration counter). **`LEAVE <label>` to an outer loop = §F boundary**
  (labels are not in the AST). Build 0/0; full suite **5013 green**; parser/AST/§0 after the `BREAK` change no
  regression. **Seam B (Live Fidelity — probe-only, NO production code):** extended `DebuggerFidelityProbe`
  **+8 cases (27–34)** over the 4 lab workhorses × both run modes, proving **sim == real on FB5** (values AND the
  logical stop-moment — statement, in-loop membership, rows-so-far, live vars): NESTED innermost-loop capture
  (Next Iteration advances inner `J`; Loop Exit lands in the outer body), `LEAVE`/`BREAK` exit to the post-loop
  statement (`BREAK ≡ LEAVE`), `EXIT` completes the session with 0 rows. **`DebuggerFidelityProbe` 34/34 ALL
  PASS** (all 26 prior D8–D12 cases still green); probe builds 0/0; no production code ⇒ test suite unchanged
  (5013 green). **Seam C (UI — thin presentation, no business logic):** two toolbar buttons (`↻ Next Iter`,
  `⤶ Loop Exit`) beside `⏭ SUSPEND`, bound to two new `[RelayCommand]`s (`RunToLoopExitAsync`/
  `RunToNextIterationAsync`) that delegate straight to `DebugSession.RunToLoopExit()`/`RunToNextIteration()` via
  the existing `RunStepAsync` background-step path; **gating uses the engine's own `IsInsideLoop`** (`CanFastForward`
  = `Phase==Paused && Session is { IsInsideLoop: true }`, added to the `Phase` `NotifyCanExecuteChangedFor` list
  so it re-evaluates on every step). No keyboard shortcut (mirrors Run-to-`SUSPEND`, toolbar-only). Build 0/0;
  compiled bindings validate the commands at compile time; manual QA confirmed (buttons enable only inside a
  loop; both fast-forward correctly on the live lab).
  **D15.2 (Toolbar Visual System & Error Bar) — STARTED; Seam A (icon system + toolbar) DONE 2026-07-21 (impl,
  awaits visual confirm).** Ratified icon-system decision: EmberTern does NOT build a parallel icon family —
  it extends the existing (Lucide-derived) `SvgIcon` system as the "EmberTern Icon System" (`.svg` canonical
  under `Assets/Icons/`, `IconGeometries.axaml` the runtime representation, reuse-before-create). The debugger
  toolbar was the last Unicode-glyph holdout (`▶ ⤵ ↷ ⤴ ■ ↻`); now on `SvgIcon`. Concept board approved (2
  refinements: Next Iteration = two-arrow cycle vs Restart moved to skip-to-start; Break on Exception =
  stop-octagon+`!`, not an energy bolt). 9 new geometries `Icon.StepInto/StepOver/StepOut/RunToCursor/
  RunToSuspend/NextIteration/LoopExit/Restart/BreakException` (`Assets/Icons/Debug/*.svg` + mirrored);
  Continue reuses `Icon.Play`, Stop reuses `Icon.Stop`. **Colour = hierarchy, not decoration:** Continue →
  `AccentIconBrush` (sole primary); steps + run-to + Restart → neutral; Next Iteration + Loop Exit → new
  shared `DebugLoopIconBrush` (teal, both dicts — NOT the PSQL-keyword violet, which would cross domains);
  Stop → `DangerIconBrush`; Break on Exception → `WarningIconBrush` (a mode, not destruction). Labels stay
  neutral text (only the icon carries the category → 6 neutral / 4 hues, calm). Removed 3 now-unused
  `Debugger*Content` glyph strings. Pure Presentation. Build 0/0; +1 headless pin (all 11 toolbar geometries +
  `DebugLoopIconBrush` both themes resolve); 5099 tests green; smoke clean. **Seam B (debugger identity icon,
  replacing `Icon.Bug`) — DONE + user-accepted 2026-07-22.** `Icon.Bug` replaced at all THREE
  debugger entry points — Procedure + Trigger editor toolbar "Debug…" buttons + the Debugger **tab** (which had
  been misusing the Continue `Icon.Play`) — with one unified identity mark. **First metaphor
  (playhead-on-a-branching-path) was shipped then REJECTED by the user** — not a quality problem, the metaphor
  itself didn't read as "debugger" (less legible than the bug). **Ratified replacement: a two-colour composite
  — a blue Play triangle (execution pointer, dominant) + a small red breakpoint dot nested into its lower-right,
  overlapping the tip so the two read as ONE "Start Debugging" glyph.** Two colours + a filled dot can't be a
  single stroked `SvgIcon`, so it is a dedicated composite control `Controls/DebuggerIcon.cs` + its ControlTheme
  in `IconGeometries.axaml`; both colours are **reused tokens** (`AccentIconBrush` + the gutter
  `DebugBreakpointBrush`), both dicts, same idiom (24×24, 2px stroke, round caps/joins). Tab branches on a new
  presentation-only `WorkspaceTabViewModel.IsDebuggerTab`; the orphan `Icon.Bug` geometry removed. Pure
  Presentation. Build 0/0; pin test extended (constructs `DebuggerIcon` + pins both brushes both themes);
  tests green; smoke clean. **User-accepted 2026-07-22** (a better mark may be revisited in the future Visual
  Polish sprint; `DebuggerIcon`'s ControlTheme is the one place to change).
  **Seam C (Error Bar) — DONE 2026-07-22 (impl, awaits visual confirm) → D15.2 COMPLETE.** The fault message
  moved out of the toolbar status line into its **own row** (root grid `Auto,Auto,*`; `Grid.Row=1`), shown only
  on a fault / Break-on-Exception pause (`ShowErrorBar`), collapsing to zero height otherwise so the toolbar
  never moves. Calm: `PanelBrush` + a thin 3px `ErrorBrush` left stripe + an error-toned icon (no loud fill).
  **Shows the FULL message by default** (user QA refinement — FB errors are short (2–6 lines), so
  default-collapse gave no benefit); sizes to content, capped ~8–10 lines (`MaxHeight=190`) + scrolled for the
  rare long one; **Expand/Collapse** is the opt-in one-line safety valve; font 12 + `LineHeight=18` for
  readability. **Copy** (clipboard, view code-behind), **Dismiss**. The status line is now a short fixed-height
  headline (`DebuggerStatusFaulted`);
  the full FB message lives only in the bar (the in-row `StatusText` bug fixed). Pure Presentation — the VM
  projects `ErrorDetail`/`ShowErrorBar` over the engine's `DebugError` + owns the expand/dismiss view-state,
  Core untouched. Build 0/0; +2 `DebuggerTabVmTests`; smoke clean. Guide: d15 doc §4.
  **D15.3 (Launch & Entry Experience) — STARTED; architecture re-reviewed + ratified 2026-07-22; seam order
  C→A→B→D→E.** Ratified decisions: **F5 = always "Go"** (launch panel → Start Debugging, session → Continue);
  **Enter launches only from the last parameter field or the Launch button** (not any field); **Transaction
  Isolation → an Advanced section (collapsed)**, description-before-level, default Read Committed; **no-decision
  fast path** — a non-trigger routine with no parameters and a clean pre-flight skips the launch panel entirely
  (Debug → Preparing → session; a pre-flight warning or any parameter/trigger context keeps the panel).
  **Seam C — DONE 2026-07-22 (impl, awaits visual confirm):** keyboard-first launch (F5 in the panel; Enter
  scoped via a tunnelled handler to the last `ParamsList` input / Launch button; multiline value boxes keep
  Enter=newline), post-launch focus → editor `TextArea` (once per launch/relaunch, gotcha #225), ready-to-launch
  focus → first field / Launch button, and the no-decision auto-launch (`ShouldAutoLaunch()` in `PrepareAsync`).
  Pure Presentation + one VM guard; engine untouched. Build 0/0; +3 `DebuggerTabVmTests` (+1 boundary pin:
  defaulted/optional params keep the panel — a default is still a decision); smoke clean. **Seam A (compact
  form) — DONE 2026-07-22 (impl, awaits visual confirm):** `ParamRowTemplate` rebuilt — name primary + type
  subordinate (smaller/greyed) on one line, value beside, NULL an **inline toggle** (no standing column),
  tighter rows + less whitespace; shared by proc/func + trigger NEW/OLD grids. Pure Presentation (XAML + 2
  `UiStrings`); no VM change. **Seam B (isolation→Advanced) — DONE 2026-07-22 (impl, awaits visual confirm):**
  isolation moved into an **Advanced disclosure** (flat chevron+"Advanced" toggle → VM view-state
  `IsAdvancedExpanded`, mirroring the bottom-panel collapse; no unstyled `Expander`), **collapsed by default**;
  the note now leads with what the option changes (plain language, `(rec_version)` jargon dropped), then the
  level selector; default Read Committed. Main flow = params (if any) → Start. View + `UiStrings` + one trivial
  VM toggle. **Launch-panel Visual Polish backlog (user, deferred to the future Visual Polish sprint):** cap
  panel max-width on wide monitors · refine History-vs-parameters hierarchy · reconsider Start-button placement ·
  gentler success cue for "No issues detected". **Seam D (Quick Relaunch) — COMPLETE via REUSE 2026-07-22
  (verified, no new production code):** already delivered by deliberate reuse — the launch form is the shared
  `ExecuteProcedureDialogViewModel`, whose ctor auto-applies the newest `ParameterHistoryStore` set (persisted
  per-routine, across tabs/restarts; each launch's `Accept()` records it), plus the existing `RestartCommand`
  (toolbar + `Ctrl+Shift+F5`) reusing last values and Seam C's F5 on the pre-filled panel. Named favorites stay
  DEFERRED. +2 `DebuggerTabVmTests` (pre-fill via the debugger path; Restart reuses last values); build 0/0.
  **Seam E (Members-tab Debug button) NOT started** (folded into the discoverability backlog below).** Guide:
  d15 doc §5. **D15.3 CLOSED by the user 2026-07-20.**
  **D15.4 (Expression UX + Friendly Errors) — COMPLETE 2026-07-23 (Seams A+B); Seam C DEFERRED.** Split into
  three seams. **Seam A — Expression Hints (P, `ea6957e`):** terse input placeholders each carry a concise
  valid-expression example + a subtle monospace examples line under the Immediate/Watches empty-states (new
  shared `UiStrings.DebuggerExpressionExamples`); pure `UiStrings` + two empty-states. **Seam B — Friendly Error
  Mapping (P+F, `a7d34cf`):** Core `DebugErrorClassifier.Classify(DebugError) → FriendlyErrorCategory
  { UserException, ConstraintViolation, SqlError, Unknown }`, keyed on **SQLSTATE/GDS codes only** (never
  message text, §F), unit-tested; App `DebugErrorPresenter` (`Raw` = best raw field; `Describe` = friendly
  one-liner, `Unknown → Raw`) — the ONE text composer that removed the duplicated `?? Message ?? ExceptionName`
  from all four surfaces (`DebugExecutedSqlRowViewModel`, `WatchRowViewModel`, `PausedReasonText`,
  `DescribeError`). Friendly text on the three expression surfaces with the **raw FB message kept as a tooltip**
  ("Friendly + raw available", ratified); Error Bar keeps the raw body (D15.2). **GDS codes MEASURED live on the
  FB5 lab** (throwaway probe, removed): user `EXCEPTION` → `ExceptionName`; `{335544879 NOT NULL, 335544347
  CHECK, 335544665 PK/UNIQUE}` → constraint; `335544569` (isc_dsql_error) → SqlError. **Key finding:**
  token-unknown (-104), table-unknown (-204), column-unknown (-206) ALL arrive as `335544569` (DebugError
  carries only the leading GDS code) ⇒ one honest `SqlError` bucket; the precise split is Seam C's job. **Seam C
  — Local Pre-validation — DEFERRED to backlog (prove-before-build spike, NO production code).** The mandatory
  empirical gate FAILED cleanly: `DiagnosticsEngine` (semantic) flags an unresolved variable **only for a
  `:name` reference inside a PSQL body**; a **bare** identifier (`v_counter * 2` — the typical Immediate input)
  is a column candidate, gated on metadata, so with `metadata:null` it is **silent** (measured: 0 diagnostics
  for every bare shape). Reuse-only local pre-validation therefore can't see the common case without an engine
  change (binder resolving bare expressions against ambient) — out of D15.4's reuse-only scope, and SQL
  synthesis / colon-injection / `BEGIN…END` wrapping were rejected. **Ratified:** no debugger-only validator, no
  silent-in-the-common-case component; Seam B already gives friendly server errors. Seam C stays backlog — a
  future **separate Core Feature** ("resolve bare expressions against ambient", full engine-change rigour) if
  real usage asks. Build 0/0; 5132 tests green (A+B); smoke clean. Guide: d15 doc §6.
  **D15.5 (Inline Values) — COMPLETE 2026-07-23 (Seams A+B; awaits final visual confirm of the combined
  effect). Pure Presentation — Core + engine untouched.** Greyed `name = value` annotations at line ends in the
  debugger source editor. **Seam A (`efbc89f`):** new `InlineValuesRenderer` (App/Completion) — a member of the
  `IBackgroundRenderer` family (mirrors `CurrentLineRenderer`): draws in the empty space PAST each line's text
  end (position from `GetRectsForSegment`, correct under word-wrap/folding) so it **never shifts the document
  text** and never touches formatter/layout; appended after the current-line renderer (paints on the wash),
  repaints on the existing `DebugMarkersChanged → TextView.Redraw()` path. VM: `InlineValues`
  (`IReadOnlyList<InlineValueAnnotation>`), recomputed once per pause in `RebuildInlineValues`;
  `ApplySelectedFrame` reordered so the roster refreshes before `SetCurrentMarker` recomputes + fires the
  repaint. Clean P/VM split (VM decides which/where, renderer only draws). **Seam B — visibility policy, SIMPLIFIED after
  QA to used-only:** final rule (§7.1) = **show ONLY the variables the current statement USES** — real current
  values, no prediction (paused BEFORE executing). Used set = tokenize the current statement span with the one
  `SqlLexer` (reuse, like `WatchSideEffectDetector` — no new analysis) matched to roster names (case-insensitive;
  string/keyword never matches). **QA arc:** B first shipped `used ∪ changed-not-used` (used primary, changed
  appended, commit `f004144`); seeing it live, the changed-not-used tail (e.g. `V_SUM = 10` at `v_text = p_text;`)
  read as noise, so the changed-not-used loop was removed → used-only. Fixes the note that changed anchoring read
  as if it referred to the line's instruction. **Boundary (§F):** a trigger context column's dotted name
  (`NEW.STATUS`) isn't a single token → not "used". Build 0/0; 5136 tests green (used shown + anchor,
  changed-not-used excluded, empty when not paused; + renderer pin); smoke clean. **D15 next: D15.6 (Debugger
  Performance — integration).** Guide: d15 doc §7.
  **D15.6 (Debugger Performance) — DROPPED 2026-07-23 (ratified; spike done, no product justification).** A
  prove-before-build spike (`tools/probes/DebugPerfBlockProbe`, since removed) tested **variant M-A** — the
  whole procedure body as ONE `EXECUTE BLOCK` fed to the existing Performance Analysis module (wired exactly as
  `MainWindowViewModel`), over selectable / non-selectable / full-scan scenarios on a 20k-row scratch table:
  M-A is **feasible and its DATA is trustworthy** (per-table reads + advisor findings match the baseline, plan
  is real) — **only execution TIME is tainted** (harness overhead). **Dropped anyway on product judgement:** an
  M-A Performance tab profiles the WHOLE procedure, which the user already gets from the Procedure editor's
  Performance tab; the extra plan richness + same-params convenience is too little to justify a second
  performance surface in the debugger. The debugger's only unique value is **per-statement** profiling of the
  currently-executing instruction — a separate, much larger feature (own architecture + spikes), out of scope
  now. Guide: d15 doc §8. **D15 is now effectively complete** (D15.1–D15.5 done, D15.6 dropped, D15.7 audit is
  background).
  **⭐ CURRENT WORK — Debugging standalone PSQL FUNCTIONS as a debug ROOT (the functional gap; NOT D15).**
  Distinct from D9 (which already makes a LOCAL `DECLARE FUNCTION` a faithful step-into/over frame *within* a
  routine) — this is a function as the **entry point**. Standalone-first (A/B/C1/C2 ratified); packaged
  functions as root = a later follow-up. **Seam A (Core function root frame) — DONE (`765272c`; pure Core,
  additive).** `DebugSession` gained optional `rootReturnType`; when non-null, `Start()` pushes the root as a
  **function frame** so a root `RETURN <expr>` is computed via the Expression Harness (`EvaluateReturn` — the
  same path D9 proved) and its value is kept on `FinalFrame` (no caller to deliver to). `Frame.IsFunctionFrame`
  widened **additively** to `ReturnContinuation is not null || ReturnType is not null` (local function still
  qualifies via its continuation; root function via its return type; only one usage — the RETURN gate — and
  `ApplyReturnContinuation` independently guards on the continuation, so a root's value stays on the frame with
  no delivery). Procedures/triggers/package-proc/anonymous-block roots pass null → not function frames →
  byte-identical. +2 `DebugEngineTests`. **Seam B (Firebird function-root layout + live fidelity) — DONE
  (`618dc13`).** "Resolve once, pass through" (ratified): the RETURNS **base type** is resolved ONCE in the
  Firebird layer during the SAME `RDB$FUNCTION_ARGUMENTS` read that builds the function's input params, then
  threaded — no re-derivation. `FirebirdDebugMetadata.BuildFunctionFrameVariablesAsync` +
  `ReadFunctionParametersAsync` (return arg = `RDB$FUNCTIONS.RDB$RETURN_ARGUMENT`; inputs base-typed via
  `RDB$FIELDS`/`FormatType` R2, domain kept for the declaration R3 — mirrors `ReadProcedureParametersAsync`; no
  outputs/SUSPEND; FB3+ standalone). `FirebirdDebugExecutor.CreateAsync` gained `isFunctionRoot` → builds the
  function layout, registers the closed-scope root context, exposes the resolved type on new `RootReturnType`
  (the launcher will pass it to `DebugSession(rootReturnType:)` — C1); a function is a closed scope so
  Execute/EvaluateCondition/BindValues untouched, and `EvaluateReturn` (D9) already computes RETURN via the
  Expression Harness typed as `frame.ReturnType`. **Live fidelity PROVEN** (`DebuggerFidelityProbe` +3, reusing
  lab `FN_ADD_TAX`/`FN_FULL_LABEL` — no lab change): `FN_ADD_TAX(100,20)` as ROOT → sim 120==real 120, depth 1;
  `FN_FULL_LABEL` else → `'ABC - Widget'`; if/null branch → `'Widget'`; all 34 prior cases pass → ALL PASS.
  Build 0/0; full suite 5138 green (two partitions #94/#226); smoke clean. **Seam C1 (App launch wiring) —
  DONE (`5aa75d0`).** `DebugLaunchSpec.IsFunction` (additive); `DebuggerTabViewModel.PrepareAsync` resolves
  `_isFunction` once from `ddl.ObjectKind == DdlObjectKind.Function` (excluding a package context — packaged
  functions as root are a later follow-up), `LaunchAsync` threads it, `BuildParameters` labels the panel
  "Function"; `FirebirdDebugSessionLauncher` passes `spec.IsFunction` → `CreateAsync(isFunctionRoot:)` and the
  executor's `RootReturnType` → `DebugSession(rootReturnType:)` — resolve-once-pass-through end to end. Every
  non-function root → `IsFunction=false` → `RootReturnType` null → byte-identical. +2 `DebuggerTabVmTests`.
  Build 0/0; 5140 green; smoke clean. **C2 UX RATIFIED (2026-07-23):** the return value is presented as a
  dedicated **"Return" group in the Variables panel** (function-only, top — mirrors the trigger "Context"
  group), "— (not returned yet)" while stepping → the real value at completion; status line stays simple (no
  return value in it). C2 split into **C2a (Return surface) + C2b (entry points)**. **Seam C2a — DONE
  (`b02dee0`).** `DebugVariableKind.Return` + `↩` glyph + reuse of the OUT accent brush (no new token); a
  synthetic Return row (`DebuggerTabViewModel.BuildRoster`, function-only) in the new top-most `_returnGroup`,
  skipped in the generic frame-resolve loop, showing the pending placeholder while stepping and
  `FinalFrame.ReturnValue` only at completion (`UpdateReturnRowValue`; a NULL return shows `<null>`, distinct
  from pending). Real state only. VM-test infra extended (FakeLauncher passes `rootReturnType` for a function
  spec; `FakeExecutor.EvaluateReturn` yields a scripted value) → +3 `DebuggerTabVmTests`. Build 0/0; 5142
  green; smoke clean. **Seam C2b (entry points) — DONE (`c132e18`) → the whole standalone-function-as-root
  feature is COMPLETE (A/B/C1/C2a/C2b), live-launchable, awaits full manual QA.** Reuses the ONE launch path
  (`OpenDebuggerForObject`), mirroring the procedure/trigger pattern — no new debugger logic: sidebar "Debug
  function…" on a function leaf (`MetadataNodeViewModel.IsFunctionLeaf` + `DebugFunctionCommand` →
  `MetadataExplorerViewModel.RequestDebugFunction`/`DebugFunctionRequested` →
  `MainWindowViewModel.OnDebugFunctionRequested`); function-editor toolbar Debug button (`DebuggerIcon`, gated
  `IsFunctionDetailTabActive`, `ActiveFunctionDetail.DebugFunctionCommand`); `FunctionDetailTabViewModel`
  gained `DebugRequested`/`CanDebugFunction`(`!IsNew`)/`DebugFunctionCommand`; `CreateFunctionDetail` wires
  `DebugRequested`. Source = the existing `FetchObjectDefinitionAsync`/`FetchDdlAsync` (a CREATE FUNCTION
  parses to `DdlStatement{Function, Body}` → C1 `_isFunction` → B function root → C2a Return group). Build 0/0;
  5142 green; smoke clean. **⚠ gotcha:** a lingering smoke-test `EmberTern.exe` locks the output DLLs and a
  rebuild then reports MSB3021/MSB3027 copy "errors" (not compile errors) — `taskkill /F /IM EmberTern.exe`
  before rebuilding. **Packaged functions as a debug root — STARTED (follow-up to the standalone feature),
  D1→D2; directive: extend existing paths with `packageName`, NO parallel standalone-vs-packaged code.**
  **Seam D1 (engine + live fidelity) — DONE (`ebaa036`).** After D1 the ONLY difference between a standalone
  and a packaged function root is the presence of `packageName`: `ReadFunctionParametersAsync`/
  `BuildFunctionFrameVariablesAsync` gained an optional `packageName` (same query/join/typing, only the
  `RDB$FUNCTION_ARGUMENTS`/`RDB$FUNCTIONS` filter `IS NULL`↔`= @pkg`); the `isFunctionRoot` branch is
  package-aware (checked BEFORE the procedure-package branch so a packaged function is framed as a function);
  and both the function-root and the D11 procedure-package roots now register through ONE new package-aware
  `RegisterRootAsync` (standalone → `Register`; package member → `RegisterPackageMember` sibling context/R5).
  `FetchPackageMemberSourceAsync` gained a `kind` param (default Procedure; reconstruction already kind-generic
  — a FUNCTION slice keeps its `RETURNS`). Lab `PKG_DBG` +PUBLIC `PUB_FN` (calls private `PRIV_DOUBLE`), `.fdb`
  rebuilt (#149). **Live fidelity PROVEN** (`DebuggerFidelityProbe` case 38, `SimulateFunctionRootAsync` gained
  an optional `packageName` — one simulate path): `PKG_DBG.PUB_FN(5)` as ROOT → sim 11==real 11, depth 2, chain
  `PUB_FN → PRIV_DOUBLE`; all 37 prior cases pass → ALL PASS. Build 0/0; 5142 green; smoke clean. **Seam D2
  (App entry point) — DONE (`96c184a`) → packaged-function-as-root is COMPLETE, awaits manual QA.** VM dropped
  the `_packageName is null` exclusion (a packaged function member's source is reconstructed as CREATE FUNCTION
  → `_isFunction` true → launcher threads packageName + IsFunction → D1 combined path); `DebugMemberRequested`
  now carries the whole `PackageMember` (name + kind); `RequestDebugMember`/`CanDebugSelectedMember`/tooltip
  widened to FUNCTION members; a "Debug function…" Members-tab context-menu item gated on new
  `PackageMemberItemNode.IsFunction` (the toolbar Debug button is data-driven → auto-enables);
  `OpenDebuggerForPackageMember`/`FetchPackageMemberSourceAsync` gained `isFunction` → reconstruct as CREATE
  FUNCTION. Dead "FunctionLater" tooltip removed; tooltips made kind-neutral. Tests updated (function member now
  RAISES + enables Debug; node `IsFunction`). Build 0/0; 5142 green; smoke clean. **⭐ The whole function-as-
  debug-root feature (standalone A/B/C1/C2 + packaged D1/D2) is now complete end to end — no known function
  debugging gap remains.**
  **⭐ Backlog captured during D15.2 Seam B QA (2026-07-22; user directive: record in the plan, do NOT
  implement yet):** (1) **Debugger discoverability** — a **Debug button on the Package "Members" tab toolbar**,
  disabled by default, enabled only when the selected member is a debuggable kind (procedure/trigger/function);
  today debugging a member is context-menu-only → undiscoverable. (2) **Debugging standalone/packaged PSQL
  FUNCTIONS as a debug ROOT** — a functional gap (the debugger launches procedures/triggers/packaged procedures
  as a root, not a function-as-entry-point — a §F boundary; distinct from D9's local-function step-into). Both
  in d15 doc §5.2/§5.3-E. See [[project-d15-debugger-experience-planned]].
  **Philosophy held — no new execution path (Fast Forward
  only *controls* the session), the new features are stop policies (the `RunToSuspend` template), `Simulator ==
  Real Firebird` (live-fidelity-proven). D13 formally CLOSED.**
  **D14 (Step Back) — ANALYZED + DEFERRED by user decision (2026-07-20). Not started.** Full
  architecture/feasibility analysis accepted; the user chose not to build it now — it stays optional, revisited
  only when real debugger usage asks. Ratified if ever revisited (do not re-derive): a **new engine capability
  (reversible state)**, **replay rejected**, only **full-client-state snapshot + one savepoint per step +
  undo-only** (`ROLLBACK TO`, never re-execute — matches spec §9.8.5); a **second savepoint layer**
  (`ET_DBG_STEP_{n}`) not eagerly released; **v1 scope** single step-back over leaf/DML/IF/assignment +
  stepped-over CALL, **loops/`FOR SELECT` out** (a live cursor cannot be rewound), exception routing out; **§4.6
  irreversibles disclosed, not hidden**; fidelity via a round-trip invariant. **Difficulty High; ~5 sessions.**
  Full write-up: the plan's D14 STATUS block + [docs/history/19-...](docs/history/19-firebird-debugger.md) (D14)
  + [[project-d14-step-back-deferred]].
  **⭐ CURRENT STAGE — D15 (Debugger Experience & IDE Polish) — IN PROGRESS (planning complete 2026-07-20;
  D15.1 Seam A implemented 2026-07-21, see below).** The debugger is now polished to professional-IDE level as
  a **full project**, designed + ratified over two review passes. **One organising principle: Presentation vs Feature** (P = view/theme/tokens
  only, obeys "no logic in VM/Core"; F = new data/Core surface, full rigour + live fidelity). Milestones:
  **D15.1** Editor Readability (P, **app-wide** — readability-first palette, variables neutral; rebuilt
  full-width calm-blue current-line + gutter bar) · **D15.2** Toolbar + Error Bar (P — a whole **SVG icon
  system** in EmberTern's *own* visual language, NOT a VS/Rider copy; debugger icon = an **execution-tracing/
  flow** metaphor, not a bug; fault message → its **own bar** with copy + expand, never shifts the toolbar) ·
  **D15.3** Launch Experience (P + tiny persist — compact form, type subordinate to name, isolation
  practical-description-first in Advanced, launch shortcut + post-launch focus, **Quick Relaunch yes / favorites
  deferred**) · **D15.4** Expression UX + Friendly Errors (P+F — placeholders/examples + friendly errors via the
  existing `EditorLanguageService` local pre-validation + `DebugErrorMapper`) · **D15.5** Inline Values (F —
  only current-line-used OR changed-since-last-step, never all; AvaloniaEdit renderer, no text shift) · **D15.6**
  Debugger Performance (F — **direction reversed**: no debug-time timing (harness overhead misleads); integrate
  with the existing Performance Analysis; any future debug metric labelled "debugger runtime") · **D15.7** Global
  UI Audit (analysis-only catalogue for a future visual-refresh stage). **Ratified priority order:** Script
  Executor **Step 0 Probe** → D15.1 → D15.2 → D15.3 → **Script Executor Rewrite (Steps 1–6)** → D15.4 → D15.5 →
  D15.6 (D15.7 in the background). Full self-contained guide (architecture / seams / decisions / rationale /
  priorities / deps / risks — start a milestone from it without re-analysing):
  [docs/design/d15-debugger-experience-and-ide-polish.md](docs/design/d15-debugger-experience-and-ide-polish.md).
  **D15.1 (Editor Readability) — STARTED; Seam A (syntax palette, app-wide) DONE 2026-07-21 (impl, awaits user
  visual confirmation).** Presentation-only readability palette: SQL keywords (query + DML-action + DDL) share
  ONE restrained blue (bold); types + built-in functions demoted to neutral foreground; comments/literals
  legible-but-quiet; `EditorLocalBrush` neutralized in both dictionaries so ordinary variables are neutral
  (objects + trigger context keep a restrained accent). **Refinement after user QA (2026-07-21):** to keep the
  SQL-vs-PSQL language hierarchy without a Christmas tree, the Core `FirebirdSyntax` `Statement` keyword
  category was **split** into `Statement` (SQL — blue) + a new `Psql` category (BEGIN/END/IF/WHILE/FOR/DECLARE/
  SUSPEND/EXECUTE/… — a second restrained **violet** accent, dark `#A88FD4` / light `#6C4C9E`, bold). The
  catalog partition is safe: `SqlKeywordCategory`/`CategoryOf`/`KeywordsInCategory` feed only `FirebirdSyntax`
  + the xshd drift-guard test (lexer `IsKeyword` + completion are category-agnostic). Files: `FirebirdSql.xshd`
  + `.Light.xshd` + `Themes/Colors.axaml` + `FirebirdSyntax.cs` + `FirebirdSyntaxTests.cs`. **Light-theme
  tuning (2026-07-21, bolder per user):** data types → strong teal `#0F766E`, comments → cool gray-green
  `#6E847A` (no more "olive"), PSQL light violet deepened to `#5D30A6`.
  **Built-in functions coloured (2026-07-21, user-requested — reverses the "functions neutral"
  recalibration):** QA found built-in functions (ROUND/COALESCE/CAST/TRIM/UPPER/LOWER/SUBSTRING/…) reading as
  plain text. Root cause was NOT a classifier/list gap — all are catalogued in `FirebirdSyntax.FunctionWords`
  (category `Function`) + the xshd `Function` block; the only cause was the recalibration painting the
  `Function` colour neutral. Reversed: `Function` now gets a soft yellow accent (VS Code-style, dark `#DCDCAA`
  / light `#795E26`), a fourth hue distinct from SQL blue / PSQL violet / type teal; operators stay neutral.
  Pure Presentation (only the two xshd `<Color name="Function">` values + comments) — the drift-guard pins
  block *membership* not hex → no test change; build 0/0, 52 syntax/highlight tests green. A user-defined
  function stays neutral by design (needs semantic resolution — separate feature).
  **Seam A2 — domain-as-type resolution (Feature) DONE 2026-07-21 (awaits visual confirm):** a domain used as
  a data type (`DECLARE VARIABLE`/param/`RETURNS`) was neutral because the binder emitted no reference for a
  type-position name. `SemanticBinder.Psql.BindDomainTypeReference` now emits a `SchemaObject` reference for the
  type identifier **only** when it resolves to a `Domain` via metadata (builtins are keywords, not identifiers;
  unknown name → nothing, no false diagnostic; scan bounded to the segment so it never reaches the body). The
  App paints a resolved domain like a SQL type via a new `EditorDataTypeBrush` (both dicts, mirrors the xshd
  `DataType` hex) mapped in `SemanticHighlighter` for `Domain` objects; hover/Ctrl+Click/diagnostics work for
  free through the model. +4 `SemanticModelTests`; semantic/highlight/nav/hover/diagnostics suites green; build
  0/0. A **distinct** domain accent (vs plain types) stays deferred. **Palette recalibration (2026-07-21, user
  clarified the goal):** the point is FEWER coloured categories (most identifiers stay plain text), not lower
  intensity — colours should be vivid + elegant (VS Code-like). So dark types → vivid `#4EC9B0` (+ mirrored
  `EditorDataTypeBrush`); light comments → elegant green `#2E8B57` (back to green, not gray/olive). SQL blue /
  PSQL violet already read as vivid; functions/operators/locals/columns stay neutral.
  **Seam A3 — DDL preview highlighting parity (bug fix) DONE 2026-07-21 (awaits visual confirm):** the object
  editors' DDL tab (+ sidebar DDL preview) coloured differently from the Editor tab — app-wide highlighting has
  TWO layers and only the lexical (xshd) one reached the DDL previews; the **semantic** layer
  (`SemanticHighlighter`, objects+domains) is installed only by `SqlEditorBehavior.Attach`, which the read-only
  DDL editors never called. New highlight-only `SqlEditorBehavior.AttachReadOnlyHighlighting(editor)` adds the
  semantic layer WITHOUT completion/squiggles/ergonomics (rebuilds model from text + metadata on change/load,
  resolves the VM from the visual tree, leak-free); wired into all **11** DDL previews (MainWindow + Table/
  Procedure/Function/Trigger/View/Package/Domain/Generator/Exception/Index). Additive; build 0/0; semantic/
  highlight/syntax + headless editor-attach probe green. Minor known gap: trigger DDL preview doesn't resolve
  NEW/OLD context vars (no context provider on the read-only path).
  **Seam B (current-line rebuild) DONE 2026-07-21 (impl, awaits visual confirm) → D15.1 COMPLETE.** Treated
  as the definitive review of current-line rendering. **Scope correction:** the current-line marker is
  **debugger-only** (`CurrentLineRenderer`, attached only in `DebuggerTabView`, NOT the shared
  `SqlEditorBehavior.Attach`; no editor uses `HighlightCurrentLine`) — it marks the debugger's **paused
  statement**, so Seam B's visual change is confined to the debugger source editor (the app-wide part of
  D15.1 was the palette). As-built: the old amber statement-span band → a calm **full-line-width blue wash**
  (`DebugCurrentLineColor` dark `#285A8AC8` ~16% / light `#1C0033B3` ~11%) **+ a ~2.5px accent bar** at the
  line's left edge (new `DebugCurrentLineBarBrush`, both dicts). Draws as the **backdrop**
  (`BackgroundRenderers.Insert(0,…)`) so squiggle/related renderers + selection read on top; low alpha never
  masks glyphs/syntax. Per-visual-line geometry via `GetRectsForSegment` (Y/Height reused, X → full viewport
  width) → correct under word-wrap / folding / variable heights. Repaint unchanged (`TextView.Redraw()`,
  gotcha #223). Pure Presentation; hex tunable. Build 0/0; +1 headless pin (backdrop ordering + non-throwing
  Draw); 5098 tests green; smoke clean. Next: **D15.2** (Toolbar + own SVG icon system + Error Bar). Guide:
  d15 doc §3.4/§4.
- **CTE column diagnostics — false ET0002 on CTE-projected columns FIXED (2026-07-21; Stage-7/binder Feature,
  pure Core).** QA found `cte_alias.col` flagged **Unknown column** even for a column the CTE projects (e.g.
  `po.rodzajsprznagl` off a `WITH RECURSIVE`). **Root cause (deterministic, not the init-ordering staleness —
  that stays unfixed, not reproducible):** `SemanticBinder.Query.BindDottedReference` resolved a CTE-alias's
  columns via `ResolveColumn(TargetName, …)` → `_metadata.GetColumns(cteName)` — the **catalog**, which never
  holds CTE columns — and `DiagnosticsEngine.QualifierResolvesTable` treated a CTE-backed
  `TableReferenceSymbol{Target:CteSymbol}` as a verifiable table. **Fix — the binder/diagnostics now understand
  the CTE's OWN projection** (user chose this over silencing ET0002): `CteSymbol` gained `OutputColumns` +
  `ColumnsComplete`; `BindCte` fills them from the explicit column list (authoritative) else a new
  `ExtractCteProjection` reading the body's **anchor SELECT** (leftmost of a set-op; Firebird's rule); it
  accepts **only** unambiguous item shapes (`col` / `t.col` / `<expr> AS name`) and marks `*`/`t.*`/unaliased-
  expression/empty as **incomplete — never a synthetic name (§0 / Paramount Law)**. `BindDottedReference` routes
  a CTE qualifier through new `ResolveCteColumn` (resolves against `OutputColumns`, gives the member a real
  `ColumnSymbol` → hover/Ctrl+Click work); `QualifierResolvesTable` flags a genuine typo only when
  `ColumnsComplete`, else stays silent (safe degradation). No probe, no Firebird. Build 0/0; **5097 tests green**
  (+9 `DiagnosticsEngineTests`: projected column silent, resolves-to-symbol, genuine-unknown-on-complete flagged,
  star silent, unaliased-expr silent, explicit-list resolves+flags, AS-alias resolves, recursive `UNION ALL`
  anchor). Reusable `ExtractCteProjection` could later serve derived-table columns (currently silent).
  **Script Executor Rewrite — Step 0 (Probe) DONE 2026-07-20; architecture stands, measurement-gated.** The
  `Sequenced`-mode plan is ratified ([docs/design/script-executor-transaction-review.md](docs/design/script-executor-transaction-review.md)
  §5/§6): fixes the KNOWN-BROKEN mixed DDL+DML defect (gotcha #213). **Step 0 (the Probe) — the blocking
  measurement gate — RAN against the live FB5 lab (twice, deterministic; no production code touched).** The one
  unmeasured load-bearing claim, the §2.2(b) self-block, is **real but SELECTIVE**: table-scanning DDL
  (`CREATE INDEX`) self-blocks on the script's own uncommitted DML (WAIT-exhausted lock timeout, SQLSTATE 40001,
  PROBE 1c), but the review's stated example (`ALTER TABLE … ADD COLUMN`/`DROP COLUMN`) does **not** —
  metadata-only on FB5 (~7 ms, PROBEs 1/1a/1d). So §2.2(b) was **restated, not withdrawn** (example corrected,
  scope narrowed, "decisive objection" framing dropped — the decisive objection is §2.2(a) Rollback-lies, which
  never rested on inference). #213 **re-confirmed** (PROBE 2); the commit-boundary fix **works** (PROBE 3);
  independent DDL **can share a segment** (PROBE 2a). **The architecture did NOT change** — the Sequenced design
  cannot self-block by construction (never two txns open at once); the review's §2.2(b)/§6/§7 were corrected in
  place. **Step 1 (Documentation Truth Pass) DONE 2026-07-21** — comment-only, no behaviour/UI/App change: the
  two remaining stale comments were corrected (`ConnectionRole` docstring falsely said Metadata *"carries … the
  metadata working transaction"* — it owns none; `MainWindowViewModel.cs:200` falsely cited *"co-location, gotcha
  #122"* for why the Script Executor is on the Data lane — the real reason is that it IS the user working
  transaction, #89); the other two review-named targets (`ConnectionProfile.cs`, the `FirebirdScriptExecutor`
  header) were already clean. Build 0/0; Script Executor + Dev Mode tests 101/101 green. **Step 3 (Sequenced
  core) DONE 2026-07-21** — Core-only, no Firebird/App/UI: `ScriptTransactionMode.Sequenced` (the "Deployment"
  mode — commit after each schema statement so #213 is fixed by design; NOT atomic, trade-off surfaced) + a pure
  `ScriptSegmentPlanner` (`EmberTern.Core.Scripting`) that splits a script into ordered `ScriptSegment`s over the
  **AST-based `SqlStatementClassifier`** (not the driver enum — the single-classifier convergence), each carrying
  a `SegmentTransactionPolicy` intent (`DataNoWait`/`SchemaWait`; the real Firebird TPB mapping is Step 4). v1 is
  conservative — each schema statement is its own committed segment (isql `SET AUTODDL ON`); grouping independent
  consecutive DDL (§5.1, PROBE 2a) is a documented deferred optimization. **Step 2 (Dev Mode text) folded in and
  found already truthful** (`UiStrings` already states the scope + SQL-Editor boundary). Build 0/0; +10
  `ScriptSegmentPlannerTests`; Script + Dev Mode suite 110/110 green. **Step 4 (Firebird layer) DONE 2026-07-21
  (seams A+B).** Seam A: pure `FirebirdScriptExecutor.ResolveSegmentTransactionOptions` (`SchemaWait` → the same
  Dev-Mode-aware WAIT policy Compile uses / `DataNoWait` → NOWAIT default; +5 unit tests). Seam B: `RunAsync`
  dispatches `Sequenced` to `RunSequencedAsync`, which runs the plan one committed segment at a time (per-segment
  begin with seam-A TPB → `RunOneAsync` → commit on success / roll back the OPEN segment on failure; one tx ever
  open, `TransactionLeftOpen` always false). **Core untouched; Manual/AutoCommit byte-unchanged.** The now-false
  "KNOWN BROKEN — a mixed migration cannot run" class docstring was corrected (single-tx modes still can't; use
  `Sequenced`). **Live-verified on FB5** — new throwaway `tools/probes/ScriptExecutorSequencedProbe` (scratch DB,
  lab untouched), **ALL PASS**: (A) mixed CREATE+INSERT+INDEX migration runs end-to-end + persists (#213 fixed by
  design), (B) same migration under AutoCommit still fails at the INSERT + rolls back (the #213 contrast), (C)
  mid-script failure keeps earlier segments committed + rolls back only the failing one. Build 0/0; Script + Dev
  Mode + Transaction suite 165 green (regression). **Step 5 (App layer — App/UX only) STARTED 2026-07-21, split
  into seams A/B/C; Seam A (third mode in the picker) DONE** — `Sequenced` ("deployment, commits in steps")
  added to the mode ComboBox with a per-mode description tooltip (the non-atomic trade-off stated where the mode
  is picked, §5.3); pure `ScriptExecutorTabViewModel.ResolveMode`/`ResolveModeDescription` + a mode-aware
  `BuildOutcomeStatus` (honest Sequenced summary + cancelled message); +11 `ScriptExecutorModeTests`;
  Manual/AutoCommit wording unchanged. **Seam B (up-front rejection of a mixed DDL+DML script in
  Manual/AutoCommit) DONE 2026-07-21** — pure `ScriptExecutorTabViewModel.ResolveMixedScriptBlock` stops the run
  BEFORE the first statement with a message that explains the single-transaction limitation and names
  `Sequenced` (`IsMixedMigration` classifies via the same AST `SqlStatementClassifier` the planner uses; engine
  untouched); +11 `ScriptExecutorMixedScriptTests`. **Seam C (results-grid segment presentation) assessed larger
  than one seam, split C1/C2/C3; C1 (a "Step" column showing each statement's committed Sequenced step) DONE
  2026-07-21** — pure `ScriptExecutorTabViewModel.BuildSegmentMap(statements, mode)` (statement index → 1-based
  step from the same planner the engine ran; empty/blank for single-transaction modes) + `ScriptResultRowViewModel.StepText`;
  +7 `ScriptExecutorSegmentPresentationTests`. **Seam C2 (per-step commit/rollback status) assessed larger
  than one seam, split C2a/C2b; C2a (pure reconstruction) DONE 2026-07-21** — `ScriptStepStatus`
  (Committed/RolledBack/NotRun) + pure `ScriptExecutorTabViewModel.BuildStepStatuses(segmentMap, results)`
  mirrors `RunSequencedAsync` exactly (captures a `Success` statement whose step still rolled back because a
  later statement in the same transaction failed); engine untouched; +7 `ScriptExecutorStepStatusTests`. **Seam
  C2b (presentation) split C2b-1/C2b-2; C2b-1 (colour the Step cell committed/rolled-back on executed rows) DONE
  2026-07-21** — post-run `ScriptExecutorTabViewModel.ApplyStepStatuses` stamps each row from `BuildStepStatuses`
  (unchanged); `ScriptResultRowViewModel` became observable (`StepStatus` + derived flags/tooltip); the grid's
  Step cell is coloured (committed = green / rolled back = amber, existing tokens) with a tooltip, so a `Success`
  statement whose step rolled back is visibly marked; +4 `ScriptExecutorStepStatusPresentationTests`. **Seam C2b-2
  (surface "not run" statements) DONE 2026-07-21** — a Sequenced stop-on-error / cancellation leaves later
  statements unexecuted (they get NO result row — rows arrive only via the progress callback); pure
  `ScriptExecutorTabViewModel.FindNotRunStatements(segmentMap, results)` reconstructs their indices (plan minus the
  covered indices; empty for single-transaction modes so nothing is synthesized there) and `AppendNotRunRows`
  appends a synthesized `ScriptResultRowViewModel` per index (new statement-based ctor: `IsNotRun`, Result = "Not
  run", `StepStatus = NotRun`, its would-be step number, source range preserved so double-click still navigates). A
  not-run row is neither success nor failure — shown muted/italic via a new `IsSucceeded` (`= !IsFailed &&
  !IsNotRun`, so "OK" stays green only for a real success) + a `result-notrun` style; the "Success" filter excludes
  it; `SuccessCount`/`FailedCount` untouched. App presentation only; Core + Firebird untouched; +7
  `ScriptExecutorNotRunTests`. **Seam C3 (a "N of M steps committed" status-line headline) DONE 2026-07-21** — the
  Sequenced status line now leads with committed steps of all planned steps (committed + rolled-back + not-run);
  pure `ScriptExecutorTabViewModel.BuildStepSummary(segmentMap, results)` counts committed steps by REUSING the
  unchanged `BuildStepStatuses` reconstruction (the count matches the grid — it only counts + formats), and
  `BuildOutcomeStatus` gained an optional `segmentMap` arg prepending the headline to both the deployment summary
  and the cancelled message (empty/absent for single-transaction modes + the existing 2-arg callers → no headline,
  byte-identical); App presentation only, Core + Firebird + the reconstruction untouched; +7
  `ScriptExecutorStepSummaryTests`. Build 0/0. **Step 5 seam C is COMPLETE.**
  **Step 6 (live verification against the lab) — DONE 2026-07-21 → SCRIPT EXECUTOR REWRITE (Steps 0–6) IS
  COMPLETE.** Full end-to-end verification passed: Technical Review + Live Verification (**12 scenarios, ALL
  PASS**) + UX Review + Code Review + Performance Review + Final Verdict. The one issue found was documentation
  only (FINDING 1 — a stale `ScriptSegmentPlanner` docstring about dependent DDL), corrected in commit
  `8faf200` (`docs(script-executor): Step 6 — correct ScriptSegmentPlanner docstring (dependent DDL)`). The
  `Sequenced` mode fixes the mixed-DDL+DML defect (#213) by design and is live-proven on the lab; nothing about
  the rewrite is open. Full record: review §6 "Results" + §7, and
  [docs/history/15-...](docs/history/15-ux-stabilization-sprint-and-console-refactor.md) (Step 0/1/3/4/6).
  D11 narrative + full D12 narrative + D13 (Seam 0/A/B/C + close) narrative:
  [docs/history/19-firebird-debugger.md](docs/history/19-firebird-debugger.md). Spec:
  [firebird-debugger.md](docs/design/firebird-debugger.md) (**v2, decisions ratified** — the target
  implementation spec). Execution plan: [firebird-debugger-implementation-plan.md](docs/design/firebird-debugger-implementation-plan.md)
  (milestone briefs, session split, danger zones, **Developer Contract**).
  **P1 (AST: exception handlers) — DONE.** `WHEN … DO` is now readable from the tree: a `WhenHandler`
  node per `WHEN` clause holding an **ordered `WhenCondition` list** (kind + optional operand) + a `Body`,
  hung off `BlockStatement.Handlers`. Parser producer (`SqlParser.Psql.cs`) peels the handler section
  (comma-split condition list, each recognised strictly by leading keyword — `ANY`/`EXCEPTION`/`GDSCODE`/
  `SQLCODE`/`SQLSTATE`); binder consumer (`SemanticBinder.Psql.cs`) binds each handler body against the
  enclosing scope and references every `EXCEPTION <name>` condition as a schema object. **Additive only** —
  `SqlFormatter` untouched (its PSQL layout is token-based), §0 round-trip byte-identical, an unrecognised /
  malformed `WHEN` still falls back to the lossless `PsqlLeafKind.Other` valve (never a handler, never
  swallowed). **Refined during P1 (decision 3, ratified by the user):** Firebird allows a comma-separated
  condition list per `WHEN`, so a single kind per node was insufficient — the model carries the whole list,
  and D1's router matches them in declaration order (spec + plan updated). Commit `590b220`.
  **P2 (server version gate, FB3+) — DONE.** `FirebirdConnectionService` refuses a pre-FB3 server on
  connect with a legible message (`FirebirdSql.Data.FirebirdClient` is Srp-only ⇒ FB2.5 is already
  unreachable; the gate ratifies that, decision 8 / spec §1.3). A `post-open precondition check` on
  `ConnectAsync` (right after the first attachment opens, before Metadata/Ddl — same server ⇒ gating the
  first covers all) and on `TestConnectionAsync`, closing cleanly (no half-open attachment). Pure predicate
  `IsSupportedServerVersion` reuses the app's one version parser (`FirebirdDdlReader.ParseServerMajor`) and
  **fails open on an unparseable version** (a live Srp connection is FB3+ by construction, so 0 ⇒ allow;
  reject only a positively-identified major 1–2). `MapErrorMessage` untouched (this is a precondition, not
  error interpretation). The message lives beside `MapErrorMessage` in the Firebird layer, not `UiStrings`
  — `EmberTern.Firebird` cannot reference `EmberTern.App` (layering); connection-failure messages already
  live there. **Live rejection is unverified** (no FB2.5 instance; the predicate is table-pinned and the
  FB5 lab connect path is behaviourally unchanged — FB5 ⇒ allowed). Build 0/0; **4652 tests green** (two
  partitions — 4625 + the 27-test `ConnectionExpandBindingProbe` alone — sidestepping the full-suite hang
  #94/#226); smoke clean. Follow-up (not urgent): the existing `serverMajor >= 3` catalog gates are now
  statically true.
  **D1 (debug engine core) — DONE (pure Core, no server; seams a + b both landed).** New namespace
  `EmberTern.Core.Sql.Debugging` (zero Avalonia, zero `FirebirdSql`): `DebugSession` (the interpreter/state
  machine), `Frame` (+ internal control-stack activations + the lexical scope chain), `FrameValues`,
  `StepPlanner` (pure stop-decision), `ExceptionRouter`, `BreakpointSet`, the enums
  (`DebugState`/`StepKind`/`StopReason`/`ExecutionStatus`), `DebugError`/`StatementOutcome`/
  `ConditionOutcome`/`IDebugCursor`/`DebugRoutine`, and `IDebugExecutor` — the **single server seam,
  contract only** (the precedented rule-#2 exception, like `ISqlMetadataProvider`).
  **Seam (a):** the interpreter walks block/`IF`/`WHILE`/`FOR`/leaf control flow and pushes/pops **nested
  frames** (step into `EXECUTE PROCEDURE` resolves a callee body via the executor; step over runs it on the
  server), with **Into/Over/Out/Continue/RunToCursor/SetNextStatement**. **Savepoint model from day one**
  (spec §4.5): `EnterFrameSavepoint` on every frame push (incl. root), `LeaveFrameSavepoint` on normal exit.
  **Seam (b) — exception routing + breakpoints (this session):** `ExceptionRouter.TryRoute` is the whole of
  exception control flow (spec §3.6) — on a raise it walks the innermost frame's active `BEGIN…END` blocks
  outward, matching `WHEN … DO` handlers **read from the AST** (`WhenHandler`/`WhenCondition` from P1, never
  re-parsed) in declaration order (handlers top-to-bottom, conditions left-to-right); **all five forms**
  (`ANY`/`EXCEPTION <name>`/`GDSCODE` numeric-or-symbolic/`SQLCODE` signed/`SQLSTATE` literal — the last
  three's operands read from `WhenCondition.Tokens`, where P1 left them). A **caught** exception repositions
  control to the handler body (abandoning inner activations, skipping the block's remaining statements,
  marking the block `HandlerActive` so it can't re-catch its own body → **re-raise** propagates out) and the
  catching frame is **NOT** rolled back (a `WHEN`-handling block's prior statements survive, §4.5). An
  **unhandled** frame closes its cursors, `RollbackFrameSavepoint`s (new `IDebugExecutor` method — the
  unhandled-exit counterpart), and pops; when **no frame** catches, every frame incl. the root is rolled
  back and the session `Faulted`s. `BreakpointSet` (offsets; `Add`/`Remove`/`Toggle`) hangs off
  `DebugSession.Breakpoints`; a run command stops at the next step point whose offset is set
  (`StopReason.Breakpoint`, always winning over `Step`). Re-raise needs no special interpreter state (the
  executor re-raises; the router routes it) — the router stays **pure control flow**, never interpreting
  Firebird semantics. Every step/route decision is a pure function of (AST, frames, breakpoints, command).
  Proven with a **scripted fake executor** — **39** `DebugEngineTests` (24 seam-a: step ordering, IF/WHILE/
  FOR, nested frames, savepoint order, scope chain, SUSPEND, RunToCursor, SetNext; +15 seam-b: matching per
  form, multi-condition `WHEN`, cross-frame propagation + rollback, re-raise + `HandlerActive` guard, cursor
  cleanup on both unhandled + handled unwind, four breakpoint cases).
  **D2 (harness + session connection + executor) — seam (a) DONE (Firebird; no harness yet).** New
  `DebugSessionConnection` (`EmberTern.Firebird`): a debug session's **own attachment + one transaction +
  frame savepoints** (spec §4.1/§4.2/§4.5) — **decision 5: a session is NOT a lane** (no
  `ConnectionRole.Debug`; two tabs = two sessions = two transactions, impossible on a per-profile lane
  singleton), and it **never** touches the Data lane (a debug rollback there would destroy the user's
  uncommitted work, rule #11). TPB **explicit** (#85) via pure `BuildDebugTransactionOptions(DebugIsolation)`
  — write + (read_committed rec_version | concurrency) + **NOWAIT** (a lock met on the user's Data tx ⇒
  step-level error at a known line, not a hang); isolation `ReadCommitted`/`Snapshot` user-selectable at
  launch (§12.4). Frame savepoints: `Set`/`Release`/`RollbackToSavepointAsync` (async counterparts of D1's
  `IDebugExecutor.Enter/Leave/RollbackFrameSavepoint`, bridged by seam c) — names (`ET_DBG_FRAME_{id}`)
  validated as bare identifiers; SQL verified through the driver (§15.3 [5]). Per-wire-op locking on the
  session's own single lock (#31/#98/#120/#236). `FirebirdConnectionService.CreateDebugSessionAsync` opens
  the attachment + registers the session; `DisconnectAsync`/`Dispose` tear all sessions down deterministically
  (attachments must not outlive the profile connection); each deregisters itself on dispose. Pinned by 13
  pure `DebugSessionConnectionTests` (TPB both isolations, the 3 savepoint statement forms, name validation);
  the **live** round-trip is **awaits user confirmation** (needs a server; driver capability already
  confirmed §15.3 [5]).
  **D2 seam (b) — harness builder + read/write-set analyzer + §3.4 R1–R5 DONE (pure Core, no server).**
  New `HarnessBuilder` (`EmberTern.Core.Sql.Debugging`): `Build(HarnessRequest) → HarnessResult` generates
  the anonymous `EXECUTE BLOCK` that is the **one** server mechanism (§3.2/§3.3) as a **pure function** — the
  fragment text, each variable's verbatim declaration + base type + value, sub-routine declarations and the
  read/write set are all **inputs** (seam c derives them from metadata + frame; tests supply them), which is
  what makes the non-negotiable §3.4 rules unit-testable without a server. Rules enforced: **R1** only reads
  with a non-null value are injected (a declared var is already `NULL`; `V=NULL` crashes a `NOT NULL`
  domain); **R2** params + `RETURNS` use the variable's **base type** (input; metadata-derived in seam c),
  never the domain; **R3** frame vars declared **verbatim**; **R4** inject only reads / return only writes;
  **R5** every in-scope sub-routine declaration carried verbatim, always (after the var declares). Statement
  vs Expression mode (conditions/watches → `ET_DBG_RESULT`); `ET_P_`/`ET_O_`/`ET_DBG_` prefixes avoid
  colliding with real names. New `ReadWriteSetAnalyzer.Analyze(statement, model)`: **consumes** the binder's
  resolved references (rule #1/#2 — never re-parse/re-resolve) — reads = referenced vars/params (safe
  over-inclusion), writes = leftmost l-value for an assignment / ∅ for an `IF`/`WHILE` condition / reads
  (superset) otherwise. **Two deliberate boundaries:** the transitive sub-routine call-graph fixpoint is
  **D9** (meanwhile R5 carries all sub-routine *declarations*, so nothing is lost); the §3.5 inject-all-in-
  scope fallback is the named primitive `InScopeLocals` (for a Watch on an arbitrary expr, D5), **not** an
  auto-branch (the binder never signals an unresolved *local*, so it'd be untestable dead code — #233).
  Pinned by 16 pure tests (11 `HarnessBuilderTests` covering R1–R5 + modes; 5 `ReadWriteSetAnalyzerTests`
  against the real `SemanticModel`). **Test lesson recorded:** the debugger builds the model from the
  **strict** `SqlParser.Parse(sql).Root` of a whole routine (`CREATE PROCEDURE` stays one `DdlStatement`
  with a bound body) — the editor's lenient `SemanticModel.Build(string)` splits a routine apart and binds
  the body without its declared vars. Build 0/0; tests green (user-verified — full-suite run was slow, so
  confirmed manually); smoke clean.
  **D2 seam (c) — executor + live fidelity — DONE + verified (2026-07-17). D2 IS COMPLETE.**
  `FirebirdDebugExecutor : IDebugExecutor` (`EmberTern.Firebird`) wires D1's interpreter to seam (a)'s
  `DebugSessionConnection` through seam (b)'s `HarnessBuilder`: each step/DML leaf → a Statement-mode harness,
  each `IF`/`WHILE` condition → an Expression-mode `BOOLEAN` harness, run in the debug tx; the server computes
  **all** semantics. `SUSPEND` is control flow — the output row is emitted **client-side** from the output
  params (no round-trip). Savepoints delegate to the session. **Sync-over-async** bridge is deadlock-safe
  (ConfigureAwait(false) throughout; per-wire-op command lock #98/#120/#236). **D2 boundaries (§F, explained
  stops):** `ResolveRoutine` → null (a call runs in place = step-over, 100% faithful §5.3; step-into is
  D8/D9); `OpenCursor` → Cursor Bridge (D6). New pure Core `PsqlDeclarationExtractor` (verbatim locals R3 +
  type spec, sub-routines R5 empty in D2 by construction). New `FirebirdDebugMetadata`: **R2 base-type
  derivation** from `RDB$FIELDS` via the existing `FirebirdDdlReader.FormatType` (derivation, not guessing) +
  frame variable templates (params from `RDB$PROCEDURE_PARAMETERS`, declared with their user domain R3 /
  base-typed injection R2; locals verbatim). New `DebugErrorMapper`: `FbException` → `DebugError` from
  SQLSTATE/GDS, never message-parsed (**grounded live** — user `EXCEPTION` carries `isc_except` 335544517 with
  its name on the message's first line; `NOT NULL` validation is SQLSTATE 42000 / GDS 335544879); pure
  `Build()` unit-tested; `SqlCode` + symbolic GDS name are documented D2 boundaries. Small **D1 extension**:
  `DebugSession` gained an optional `rootValues` ctor arg (a standalone routine's launch **input-parameter
  arguments** seed the root frame — the root has no caller to provide them; additive, existing tests pass
  null). **⚠ §3.5 fallback (gotcha #238):** a reused `SELECT … INTO` surfaces **no** local refs from the
  binder (the query binder records FROM/columns, not the `:`-refs in WHERE / the INTO targets), so its precise
  read/write set is empty and would drop the write-back — the executor falls back to `InScopeLocals` (§3.5
  "inject all in-scope", correct+chattier) when the model surfaces nothing; precise narrowing stays for every
  statement whose refs the binder does surface. **Lab zoo extended** (`Lab/setup.sql` + rebuilt `.fdb`): two
  D2 procs — `SP_DBG_SUMMARY` (assignment, **domain `NOT NULL` local**, IF/ELSE, SUSPEND) and `SP_DBG_GUARD`
  (`EXCEPTION` + `WHEN … DO`). **Live fidelity PROVEN (§15.4):** the real executor drove `DebugSession`
  step-by-step through `SP_DBG_SUMMARY`/`SP_DBG_GUARD`/`SP_ADD_ORDER` and the DB state + outputs **matched real
  execution** in all 7 cases (incl. the domain-`NOT NULL` local not crashing, exception routing via real
  `FbException`, DML + savepoint rollback, unhandled-exception root rollback). Nested calls / cursors / local
  routines / autonomous-tx grow the zoo per their own milestones (D6/D8/D9). +12 tests (6
  `PsqlDeclarationExtractor` + 5 `DebugErrorMapper` + 1 `ReadWriteSetAnalyzer` fallback pin). **Build 0/0;
  4732 tests green in one run; smoke clean.** History:
  [docs/history/19-...](docs/history/19-firebird-debugger.md).
  **D3 (editor-wiring consolidation) — DONE + user-confirmed (2026-07-17; behavior-preserving, manual QA
  passed on every surface in both themes).** The **two** hand-maintained copies of the SQL editor's intrinsic language block
  (completion / highlighting / navigation / squiggles / related-elements / language-completion /
  typing-ergonomics / search) are collapsed into **one attach path** — dissolving gotcha #219 *before* the
  debug tab (D4) becomes a third host. `MainWindow` no longer hand-wires that block in its ctor; it calls the
  **same** `SqlEditorBehavior.Attach(_editor, _currentVm)` the object editors use, once its VM arrives (first
  non-null `OnDataContextChanged` — the window's `DataContext` is set after construction, and the shared path
  needs a stable non-null VM: **"subscribe once the VM arrives"**, the spec §11.1 intent). Approach chosen
  over a null-safe shared-helper alternative because it **solves** the lifecycle rather than encapsulating it
  (user-ratified). **Deleted as now-dead** (Contract #20, only after the new path built + tested green — the
  user's "prove before delete" directive): `OnMainEditorMetadataChanged` / `OnMainEditorMetadataReady` /
  `WarmReferencedMetadataAsync` + the private `CreateMetadataSnapshot` / `EnsureColumnsAsync` /
  `EnsureRoutineParametersAsync` forwarders — every responsibility now owned by the shared `Attach` (metadata
  hooks bound to `vm.Metadata`; warm/snapshot/ensure read the VM's own methods). **Boundary: intrinsic block
  ONLY** (user-confirmed) — the per-host wiring (`DiagnosticsPanelHost.Track` = F8 + diagnostics panel,
  `AmbientModelRefresh`, `SqlSnippetDropTarget`) stays a caller responsibility, as it genuinely differs per
  host and was never the #219 risk. `SqlEditorBehavior` gained **no** new parameters — consolidation by
  *deleting* the second copy, not growing the shared one. Build 0/0; **4732 tests green in one run** (identical
  to the D2 baseline — behavior-preserving); smoke clean; the headless `ConnectionExpandBindingProbe` (drives
  `SqlEditorBehavior.Attach` + real key events) green. Gotcha #219 → **resolved by D3**; plan's "Dual wiring
  (until D3)" danger row retired. History: [docs/history/19-...](docs/history/19-firebird-debugger.md) (D3).
  **D4 (debugger tab MVP) — DONE + user-confirmed (2026-07-17; manual QA on the live lab passed — launch,
  stepping, breakpoints, variables all work; debugger felt stable). First real user value: launch a standalone
  procedure, set breakpoints, step, watch variables.**
  New `WorkspaceTabKind.Debugger` (+ `ActiveDebugger`/`IsDebuggerTabActive` on the notify chain, gotcha #25),
  opened from the sidebar procedure-leaf **"Debug procedure…"** (mirrors Execute; `Metadata.DebugProcedureRequested`
  → `MainWindowViewModel.OnDebugProcedureRequested`), hosted like `ScriptExecutorTabView` and torn down on tab
  close (rollback + close attachment, §4.4). The tab is a **thin presentation layer** over the proven engine:
  `DebuggerTabViewModel` parses the routine ONCE (strict whole-routine `SqlParser.Parse` → `SemanticModel`,
  gotcha #238) to derive the launch panel + step points, then drives D1's `DebugSession` through
  `IDebugSessionLauncher` (App seam — production `FirebirdDebugSessionLauncher` opens a `DebugSessionConnection`
  + wires D2's `FirebirdDebugExecutor`; a fake launcher over a scripted `IDebugExecutor` makes the VM
  server-lessly testable). **Launch panel (§9.2, inline not modal):** typed parameters reuse the Smart-Parameters
  infrastructure (`ExecuteProcedureDialogViewModel` — typed rows + history + validation + resolve, **no second
  editor**), an isolation selector (§4.2), and a **pre-flight** (`DebugPreflight`: `DiagnosticsEngine` unresolved
  names + the §4.6 data-safety boundaries — a lexical scan flags `IN AUTONOMOUS TRANSACTION` / generator use that
  survive the rollback — + the §F "no step points" refusal). **Stepping:** Into/Over/Out/Continue/Stop(rollback)/
  Restart + Run-To-Cursor, each engine call on a background thread (sync-over-async executor). **Renderers**
  (attached alongside D3's one `SqlEditorBehavior.Attach` seam on the source editor — **read-only as of D4;
  editable since Seam 5, and since the Draft model its text is what a session runs**, spec §11.1):
  `CurrentLineRenderer` (translucent-amber current-statement band) + `BreakpointMargin` (clickable red-dot gutter,
  breakpoints snap to an `IExecutableStatement` — §9.6); repaint via `TextView.Redraw()` (#223). **Keyboard is
  VS-standard + tab-scoped** — `F5`=Continue here (Execute in the SQL editor; the one deliberate contradiction,
  §9.7). Basic variables list from the current frame (the rich window is D7). **New theme tokens** `DebugCurrentLineBrush`
  / `DebugBreakpointBrush` (both dictionaries). **D4 boundaries (§F):** step-into resolves to nothing yet (a call
  runs on the server = step-over, 100% faithful §5.3); triggers/packages/local routines/cursors + Watches/Immediate
  are later milestones. Build 0/0; **4744 tests green in one run** (+12 `DebuggerTabVmTests`: prepare/params/
  preflight, launch-paused-at-entry, step/continue/complete, write-back, fault, stop-teardown, breakpoint snap +
  stop); smoke clean. (No headless view-attach probe yet — a follow-up; the live behaviour is user-confirmed.)
  History: [docs/history/19-...](docs/history/19-firebird-debugger.md) (D4). **D4 UX-review backlog (user, after
  first real use — deferred, NOT part of D4):** 8 items to fold into later milestones — first-class Debug entry
  points (toolbar/procedure-view button + shortcut, PPM as alternative); move transaction-isolation config to
  global Settings (show only params at launch); **current-line marker too aggressive in dark theme → a subtle
  blue wash (~10–15%) + a thin left bar, not the amber fill** (a `DebugCurrentLineBrush` re-tune); Variables must
  distinguish IN/OUT/local by icon/colour/grouping (⇒ D7); more distinct Into/Over/Out icons (VS/Rider-like);
  an "Edit Parameters…" affordance on a running session (not only at launch); grow the parameter-history feature
  (pin/recent/group/delete); a richer AST-derived paused status (e.g. "Paused — WHILE loop (line 14)"). Full list:
  [docs/history/19-...](docs/history/19-firebird-debugger.md) (§"D4 UX review"). **Directive: fix these as UX/theme
  in the view + tokens; do NOT push logic into the VMs/UI to paper over UX — keep the D1–D4 responsibility split.**
  See [[feedback-debugger-ux-polish-backlog]].
  **D5 (expression evaluation — Evaluate / Watches / Immediate) — seam (a) DONE (2026-07-18; impl, live
  evaluation awaits user confirmation). Seam (b) NOT started.** §9.5 decision 6: **one engine, three
  surfaces** — every surface is *literally the harness with a user-supplied fragment* (D5 risk #1: no second
  evaluator). **The one engine is Core:** new `EvaluationModels.cs` (`EvaluationKind` Expression|Statement,
  `EvaluationRequest`, `EvaluationResult` — carries the generated `Sql`, the §10.3/§F audit anchor) +
  **`IDebugExecutor.Evaluate(request, frame)`** (a new method on the one server seam; an arbitrary fragment
  has **no AST node**, so its read/write set is the §3.5 **`ReadWriteSetAnalyzer.InScopeLocals`** primitive —
  exactly what D2 carved out named for "a Watch on an arbitrary expression") + **`DebugSession.Evaluate(
  fragment, kind)`** (pure orchestration: requires Paused, delegates to the executor against `CurrentFrame`,
  applies a Statement's write-back to the live frame — the Immediate window operates *on the live frame*). The
  Firebird executor builds the harness with the machinery it already had; an arbitrary expression's result
  column is a wide `VARCHAR(8191) CHARACTER SET UTF8` (unknown type → server casts to text; a value that
  can't cast raises + is surfaced, never guessed, §F). **Deviation from the plan (documented, Contract):** no
  App `EvaluateController` — the real "one engine" is `DebugSession.Evaluate`; the App orchestration
  (`Task.Run` + audit append) is thin enough to live on the VM, exactly as stepping is orchestrated (a
  controller would be pure indirection; precedent: D3 chose "solve the lifecycle" over the plan's letter).
  **Two inline surfaces shipped:** the **Immediate window** (input + Enter = evaluate; an "as statement"
  checkbox → runs a PSQL statement against the live frame with write-back) and **Evaluate (Shift+F9** — the
  source selection / identifier-at-caret as an expression), both routing through the same engine, both landing
  in the **Executed SQL audit log** (§10.3 — newest-first, capped 200, the generated harness SQL kept on the
  row tooltip; a statement is always flagged `±` side-effect-capable). Evaluation runs on `Task.Run` with
  **Phase→Busy** for the duration → mutual exclusion with stepping via the existing state machine (the
  non-thread-safe `DebugSession` is never touched concurrently). New `DebugExecutedSqlRowViewModel`;
  `DebuggerTabViewModel` gained `ExecutedSql`/`ImmediateInput`/`ImmediateAsStatement`/`EvaluateImmediateCommand`/
  `EvaluateSelectionAsync`; `DebuggerTabView` gained the bottom Immediate/Executed-SQL panel + Shift+F9. All
  theme tokens; **no new colours; no UX polish** (the D4 UX backlog stays deferred). **Post-QA follow-ups
  (2026-07-18, view/discoverability only — no engine/architecture change):** the Immediate input is **no
  longer auto-cleared** after evaluation (kept for tweak-and-re-run) with an inline **Clear (✕)** button; and
  a **Debug button on the Procedure editor toolbar** (right of Run Procedure, new `Icon.Bug`) **reusing the
  one launch path** — `OnDebugProcedureRequested` extracted `OpenDebuggerForProcedure(routineName)`, the
  procedure VM raises a `DebugRequested` intent (mirrors `RunExecuteRequested`), `DebugProcedureCommand` gated
  on `!IsNew`. **Trigger/Package Debug buttons were requested but NOT added** — the debugger supports
  standalone procedures only (triggers = D10, packages later); a button there would be a dead entry point, so
  it ships *with* its enabling milestone. History: [docs/history/19-...](docs/history/19-firebird-debugger.md)
  (D5 seam a + follow-ups).
  **D5 seam (b) — Watches — DONE (2026-07-18; impl, live watch evaluation awaits user confirmation). D5 IS
  COMPLETE.** The third §9.5 surface: expressions **re-evaluated after every step** through the **same**
  `DebugSession.Evaluate` (Watches add **no** evaluation mechanism — risk #1). The tab VM calls
  `EvaluateWatchesAsync()` after each pause (step / launch / an Immediate that may have mutated the frame)
  **while `Phase==Busy`** (mutual exclusion with stepping via the state machine; each watch a wire op on
  `Task.Run`); not-paused → rows reset to `—`. New mutable `WatchRowViewModel` (value updates each pause).
  **Persistence per routine:** new Core `WatchStore` (section facade over the shared `settings.dat`, owns
  `UserSettings.DebugWatches`, additive — no schema bump), loaded in the VM ctor, saved on add/remove;
  `MainWindowViewModel` wires one on the same dir+protector as `ParameterHistoryStore`. **Side-effect flag:**
  new pure Core `WatchSideEffectDetector` **reuses the one `SqlLexer`** (no new parser) to flag a watch whose
  tokens contain a side-effecting keyword (`INSERT`/`UPDATE`/`DELETE`/`MERGE`/`EXECUTE`/`POST_EVENT`) — a
  bare-token match, so a keyword in a string/quoted-identifier never trips it; a conservative lexical warning
  cue (`±` + tooltip), not semantic analysis. UI: the right panel splits Variables (top) + Watches (bottom).
  **Deviation (documented, as seam a):** no standalone `WatchesPanelViewModel` — the collection + loop live on
  `DebuggerTabViewModel` (a separate panel VM would tightly couple to the session/eval/persistence for no
  gain); `WatchRowViewModel` is the row VM. **User backlog (recorded, NOT D5):** Immediate should pre-validate
  **syntax** locally via the existing `EditorLanguageService` (Lexer+Parser+Diagnostics) before the
  `EXECUTE BLOCK` — reuse the Language Service, syntax-only locally, semantics/execution stay the server's.
  Build 0/0; **4782 tests green in one run** (+6 `WatchStore`, +14 `WatchSideEffectDetector`, +6
  `DebuggerTabVmTests`); smoke clean. History: [docs/history/19-...](docs/history/19-firebird-debugger.md)
  (D5 seam b).
  **Debugger panel layout redesign — DONE (2026-07-18; UX only, no debugger logic change; live layout awaits
  user confirmation).** Done *before* D6+ adds panels (cheaper now). Analysis: future panels (Call Stack /
  Breakpoints / Output / result grid) are **width-hungry**, so — **right panel = Variables only** (primary
  inspection, full height, 300px); **bottom = a full-width, collapsible `TabControl`** (`bottom-tab` style,
  like the SQL editor) with **Immediate / Executed SQL / Watches** (a future panel = one `TabItem`). Full-width
  bottom (not under-editor-only) mirrors the SQL results panel + serves the width-hungry tabs; Variables get
  full height when the bottom is collapsed. **Collapse** = a chevron over the tab strip toggling the bottom
  grid **row height** (Auto ↔ pixel) in code-behind (`ApplyBottomPanel`, mirroring `MainWindow`); tab contents
  bind `IsVisible` to `!IsBottomPanelCollapsed` so Auto measures to the strip only. Immediate (REPL: input +
  latest result inline via new `LatestEvaluation`) vs Executed SQL (full audit) are non-redundant. New VM
  **presentation** members only: `IsBottomPanelCollapsed`/`ToggleBottomPanelCommand`, `LatestEvaluation`;
  `DebugSession`/`Evaluate`/`WatchStore`/`WatchSideEffectDetector` + Watches persistence/auto-re-eval untouched.
  Build 0/0; **4784 tests green** (+2 presentation); smoke clean. **Next milestone: D6 (Cursor Bridge). D6+ not
  started.**
  **Debugger tab UX follow-up — DONE (2026-07-18; live behaviour awaits user confirmation).** Two small IDE
  fixes, one commit, before D6: (1) **debugger tabs are session-transient** — `SnapshotCurrentTabs` skipped only
  the live-tool kinds, so a `Debugger` tab fell through and was persisted as a `Ddl` tab → an empty tab was
  "restored" on next launch; adding `WorkspaceTabKind.Debugger` to the skip-list means app-close captures
  nothing (restart restores nothing), and `ClearWorkspaceTabs` now also `DisposeAsync`-tears-down the debug
  session on **disconnect** (§4.4 rollback + close attachment) like the monitors. (2) **double-click the bottom
  panel's tab strip** toggles collapse via the **same** `ToggleBottomPanelCommand` (view `DoubleTapped`; toggles
  only on a `TabItem`-ancestor hit when expanded, any bar hit when collapsed, ignores the chevron button). Pinned
  by `DebuggerTab_IsTransient_NotCaptured`; build 0/0, 4785 green. History: [docs/history/19-...](docs/history/19-firebird-debugger.md).
  **D6 (Cursor Bridge) — DONE (2026-07-18; in-app stepping UX awaits user confirmation). `FOR SELECT` bodies
  step through a real incremental DSQL cursor.** Probes first (§F): FB3+FB5 cursor interleaving verified live
  (FB4 unavailable → unrecorded); `WHERE CURRENT OF` on a separately-opened DSQL cursor is unsupported
  cross-context (SQL -504) → a §F boundary, honest step error, not in DoD. **D6a** — additive AST: `ForSelectStatement.IntoTargets`
  (ordered folded INTO names) + `CursorName`, parsed order-independently (Contract #1 — don't token-scan the
  Firebird layer for structure). **D6b** — pure Core `CursorBridge` (`Build(source, loop) → CursorQueryPlan`,
  mirrors `HarnessBuilder`) + Firebird `CursorHandle : IDebugCursor` (real `FbDataReader` held open across
  steps, **per-wire-op** locking #236) + `FirebirdDebugExecutor.OpenCursor`. **§F correction caught live:** the
  first cut rewrote every binder-surfaced frame ref (bare + colon) → a `SELECT LINE_NO` column that shadows a
  `RETURNS (LINE_NO)` output param got rewritten to `?` → SQL -804; fix = rewrite **only** the colon/`@` form
  (unambiguous variable syntax; a bare name is a column), gotcha #239. Lab zoo +`SP_DBG_CURSOR`/`SP_DBG_NESTED`;
  **sim-vs-real fidelity proven** incl. a fully-stepped run + nested cursors (spec §15.5). Build 0/0, **4797
  green** (+11), smoke clean. History: [docs/history/19-...](docs/history/19-firebird-debugger.md).
  **Bottom-panel splitter double-click — root-cause fix (2026-07-18; live behaviour awaits user confirmation).**
  Three prior commits fiddled with the splitter gesture (a `_splitterGestureHeight` snapshot) and the panel
  still "glued" to the editor after collapse + re-expand. **Real cause** (found by comparing to the SQL editor):
  `ApplyBottomPanel` mutated only the bottom row, never the top — but Avalonia's `GridSplitter`
  (`PreviousAndNext`) converts the `*` top row to an **absolute pixel height** on a drag, so once dragged the
  grid has no star row to reclaim space. Fix = make `ApplyBottomPanel` the **single re-normalization point that
  sets both rows every toggle** (top → star, bottom → Auto/pixel), exactly like `ApplyResultsRowForActiveTab`;
  the `_splitterGestureHeight` workaround + its `PointerPressed` handler are **deleted** (full re-normalization
  absorbs the double-click micro-drag, as the SQL editor's maximize/restore does). Gotcha #240. Build 0/0, 4797
  green, smoke clean. **Root cause finally found (part 3, 2026-07-18):** parts 1+2 fixed the wrong place; the
  real bug was binding the **splitter's own `IsVisible` to `!IsBottomPanelCollapsed`** — the state its own
  double-click toggles (self-entangled). MainWindow's results splitter keeps visibility on an **independent**
  condition (`IsQueryTabActive`). Fix: debugger splitter is now **always visible** while the debug view shows +
  synchronous toggle (identical to `OnResultsSplitterDoubleTapped`); the `Dispatcher.Post` deferral removed.
  Gotcha #240 part 3. Awaits live confirmation.
  **D7 (Variables window, full) — DONE + user-confirmed (2026-07-18; grouping/filter/pin/edit/live-update all
  verified on the live lab).** The basic D4
  list becomes the rich window (spec §9.4). **Seam (a):** rows grouped **Pinned / Parameters / Locals** (kind
  glyph ⬤IN/◑OUT/○local coloured by a theme key string via `IconBrushConverter` — never a brush), declared
  types, distinct `<null>`, per-step **change-highlight** (reuses `FrameValues.Snapshot()`, new
  `DebugVariableChangedBrush` both dicts), **pin-to-top**, **type-to-filter**. Rows are mutable and updated
  **in place** so pins/expansion/selection survive a step; `Variables` stays the flat roster and
  `VariableGroups` is its grouped/filtered presentation over the same instances (one roster, two projections).
  Context (triggers → D10) + Cursors groups deliberately not shipped as empty groups (gotcha #233). **Seam
  (b):** **data tips** — a `DebugValue` section on the ordered aggregate `HoverInfo` (no `IHoverProvider`,
  rule #2); `HoverInfoEngine.GetHover` gained an **optional** value-lookup input (default null → SQL/object
  editors unaffected), threaded through `NavigationController`/`SqlEditorBehavior`, supplied by
  `DebuggerTabView` from the paused frame's roster; rendered first, reusing `QuickInfoView` chrome. **Inline
  edit** — double-click a value → text box (Enter commits via `frame.SetResolvedValue`, Esc cancels); a
  best-effort typed parse validates shape at edit time (red border on failure), the real domain CHECK
  surfaces on next injection (§3.4/§F); the box seeds from the full untruncated raw value (§0). **Lazy BLOBs**
  — a binary `byte[]` shows `[BLOB · N B]` and is not editable; long text truncates inline (256) but edits
  full; a dedicated value-viewer popup is a documented follow-up (no reusable viewer exists). Build 0/0,
  **4807 green** (+10), smoke clean. **Post-D7 UX bugfixes DONE (2026-07-18, awaits live confirm):** Variables
  kind icons now distinct SHAPE + hue per kind (▶ IN blue / ◆ OUT amber / ● local green — dedicated
  `DebugParamInBrush`/`DebugParamOutBrush`/`DebugLocalBrush` tokens, both dicts); the Pinned star is now **gold**
  (`DebugPinBrush`) with more spacing so it no longer blends into the kind glyph; and the splitter double-click
  root-cause fix above.
  **D8 (Call stack + nested stored routines) — seam (a) DONE (2026-07-18; pure Core, no server, no user-visible
  change yet).** D8's DoD needs a faithful Step Into of a stored procedure (pass its arguments, write its
  `RETURNING_VALUES` back), which the AST could not express — so seam (a) lands the **AST + Frame-model
  foundation** before the Firebird/UI seams. **AST deepening (Contract #1):** `ExecuteProcedureStatement` now
  produces `Arguments` (`IReadOnlyList<CallArgument>` — per-argument source spans, not tree children; a step-into
  slices + evaluates each in the CALLER frame to seed the callee's input params) + `ReturningTargets`
  (`IReadOnlyList<string>`, folded via the one `ForTargetName` reader). Parser producer `ReadProcedureCallParts`
  (paren-tolerant, top-level `RETURNING_VALUES` by text); **additive** — §0 tokens round-trip, formatter
  untouched, binder unchanged (it already binds the `:var` tokens via `BindPsqlExpression`). **Frame model —
  `LexicalParent` split from the call-stack `Parent` (the load-bearing correction):** D1 conflated the two, but
  a called **stored** routine has a caller (call stack / savepoints / write-back) yet is a **closed scope**
  (cannot see caller variables); left conflated, seam (b) would inject a caller's like-named variable into an
  unassigned callee local (§F bug). `TryResolveValue`/`SetResolvedValue` now walk `LexicalParent` (null for a
  stored callee — D8; the declaring frame for a local sub-routine — D9, exactly the spec §6 "lexical parent"
  language). `Frame`/`DebugRoutine` gained `OutputParameterNames` + `DebugRoutine.LexicalParent` (additive
  ctors). **Interpreter:** `ApplyReturningValues` on a callee's NORMAL return binds its outputs **positionally**
  into the caller's `RETURNING_VALUES` targets (spec §5; a no-op for root / no-returning / an unhandled unwind).
  A D1 test that used `execute procedure p` as a proxy for the scope-chain and (wrongly for a stored routine)
  asserted callee→caller resolution was split into an honest stored (closed-scope) test + a local (closure, D9
  mechanism) test — the fake executor gained an `asLocalClosure` mode. Build 0/0; **4813 green** (+6: 4
  `PsqlAstTests` arg/returning, +2 net `DebugEngineTests`); smoke clean. Gotcha #241 (LexicalParent vs Parent).
  **D8 seam (b) part 1 — `FirebirdDebugExecutor` multi-routine context (2026-07-18, behavior-preserving).** The
  executor held single-routine state (`_source`/`_model`/`_variableTemplates`/`_outputParameters`) — fine for
  the root, but a D8 call stack activates distinct routines. Refactored into a
  `Dictionary<BlockStatement, RoutineContext>` keyed by the routine's **body node**; every method reads
  `Ctx(frame)` and threads Source/Model/templates/outputs through the (now static) helpers. Root registers in
  `CreateAsync`; a callee will register in `ResolveRoutine` (part 2). Recursion correct for free (same body →
  one context; per-frame values on the `Frame`). **`ResolveRoutine` still returns null** → no callee frame
  pushed, single-routine path unchanged → 4813 green, live behaviour identical.
  **D8 seam (b) part 2 — `ResolveRoutine` + argument seeding + live fidelity DONE (2026-07-18). D8 DoD MET —
  Step Into a stored procedure works, proven simulated-vs-real on the lab.** `FirebirdDebugExecutor.ResolveRoutine`
  now resolves a standalone `EXECUTE PROCEDURE` to a real frame: (1) **fetches the callee source** on the debug
  session via a new internal `FirebirdDdlReader.BuildProcedureSourceAsync` seam (reuses the one CREATE-OR-ALTER
  reconstruction — Contract #17); (2) **parses** it (gotcha #238 strict whole-`CREATE PROCEDURE`) → model + body;
  (3) resolves frame templates (R2/R3) + the ordered **input** params (`DebugFrameLayout.InputParameters`);
  (4) **evaluates the call's arguments in the CALLER frame through the SAME harness** (Contract #4 — no second
  evaluator): a Statement-mode `EXECUTE BLOCK` assigning each argument to a synthetic `ET_ARG_i` **typed as the
  callee's i-th input-param base type** (R2) → seeds the callee input params positionally; (5) registers the
  callee context, returns a `DebugRoutine` (stored ⇒ `LexicalParent = null`, closed scope). The interpreter's
  `ApplyReturningValues` (seam a) binds the callee's outputs into the caller's `RETURNING_VALUES` on return.
  **Unresolvable call → step-over in place, 100% faithful §5.3** (non-EXECUTE-PROCEDURE, no name, package/qualified
  = D11, local sub-routine = D9, unreadable source). **§F boundary caught by probing not reasoning (gotcha #242):**
  `:name` is a SQL error as a PSQL assignment RHS (SQL -104, query-only syntax), so `RewriteColonRefsToBare`
  rewrites `:name`/`@name` → bare name **by span** over the tokens (mirrors CursorBridge #239; there → `?`, here →
  bare). **Lab fidelity (mandated §2.1 proof, `tools/probes/DebuggerFidelityProbe`, spec §15.6):** lab zoo +3-level
  chain `SP_DBG_LEAF`/`SP_DBG_MID`/`SP_DBG_ROOT`; the real executor Step-Into'd it — **depth 3 (`ROOT→MID→LEAF`),
  simulated `RESULT=112` == real `112`** (arg seeding + `RETURNING_VALUES` faithful across 3 frames), ALL PASS.
  Build 0/0; tests green (user-confirmed); smoke clean; no new unit tests (value = live fidelity, Contract #12).
  **D8 seam (c) part 1 — Call Stack panel (display-only) DONE (2026-07-18; awaits visual confirmation).** A new
  bottom `TabItem` (joining Immediate/Executed SQL/Watches) lists frames **innermost-first** from
  `DebugSession.CallStack`: routine name, position **line** (current statement for the innermost frame, **call
  site** for a caller — computed against **that frame's own source**), current-frame marker (▶), and the
  **simulated-frame indicator** (△ + tooltip) on any Step-Into frame (§5.3; root unmarked). Small Core enabler
  `Frame.Source`/`DebugRoutine.Source`/`DebugSession(rootSource)` — a frame carries its routine's text so its
  line resolves (and its source can later be shown). `DebugFrameRowViewModel` (immutable), rebuilt each pause by
  `RebuildCallStack` (reads the engine stack, never touches the session); cleared when not paused. **Display-only
  by design — frame-selection navigation (repoint editor+variables) needs the callee's own model roster surfaced
  to the VM (it lives in the executor's per-routine context, not the VM's root model), so it + Breadcrumbs
  (shared editor feature) + Peek Frame are seam (c) part 2.** Build 0/0; +1 `DebuggerTabVmTests` (126-test
  debugger subset green; full run hangs in this env — run manually); smoke clean.
  **D8 seam (c) part 2 — frame navigation DONE (2026-07-18; awaits visual confirmation). D8 IS COMPLETE.**
  The call stack is now navigable: selecting a frame (Call Stack row / breadcrumb / `Ctrl+Alt+Up/Down`)
  repoints the editor **source**, current-line **marker** and **Variables roster** to *that* frame (spec §5.2),
  and the editor auto-follows the innermost frame after a Step Into (the reported "editor stayed on the parent"
  gap). **Per-frame model surfaced (Contract #1 — no re-parse):** the callee's `SemanticModel` is threaded onto
  the frame exactly as `Source` already was — new `Frame.Model` / `DebugRoutine.Model` / `DebugSession(rootModel)`,
  filled by the launcher (`spec.Model`) for the root and by `ResolveRoutine` (the model it already built) for a
  callee; the Variables panel projects **that frame's own** roster, so a callee's locals show and the caller's
  don't. **The VM has ONE selection truth** (`ApplySelectedFrame`) that sets source + marker + variables + both
  selection controls together (`SelectedFrameRow` ⇄ `SelectedBreadcrumbIndex`, guarded), so a frame and
  everything mirroring it can't disagree; selection is **navigation only** — it never touches the session
  (`_selectedFrame` resets to the innermost on every pause). **Caller line corrected** (seam-c-part-1 bug): a
  caller's current line is the call site of its **child** (`stack[i-1].CallSite`, a statement in *this* frame's
  own source), not the frame's own call site (which is in its *parent's* source) — see gotcha #243. **Breadcrumbs
  = a genuinely shared, generic `EmberTern.App.Controls.BreadcrumbBar`** (segments + two-way `SelectedIndex`, no
  debugger knowledge; the debugger is its first consumer), mirroring the stack outermost→innermost. **Peek Frame**
  = double-click a call-stack row → a transient card previewing that frame's source (a debugger-local card, since
  the editor's Peek Definition is private to `NavigationController`). **Breakpoints stay root-routine-scoped**
  (`BreakpointOffsets`/`ToggleBreakpointAt`/`RunToCursorAsync` gated to `IsViewingRootSource`) — while the editor
  shows a callee/other-caller source the offsets are a different coordinate space, so none are surfaced;
  nested-routine breakpoints are D12. Stepping is unaffected. Build 0/0; **+5 `DebuggerTabVmTests`** (step-into
  switches source+roster, caller-frame repoint+marker, `Ctrl+Alt+Up/Down` walk, root-scoped breakpoints, Peek);
  full suite green (run manually — the full `dotnet test` hangs in this env, #94/#226); smoke clean. Gotchas
  #241/#242/#243. See [[feedback-debugger-ux-polish-backlog]].
  **D9 (Local procedures & functions — the flagship) — STARTED; §6.3 closure version gate MEASURED + RESOLVED
  (2026-07-18). No production code yet — this is the mandatory pre-implementation probe.** The plan makes
  §6.3 a hard blocker: §6.1 measured sub-routine closure semantics on **FB5.0 only**, and FB3 historically
  documented sub-routines as having *no* outer-variable access — if FB3 differs, the closure harness must
  branch on version. Measured with a new out-of-solution probe `tools/probes/Fb3ClosureProbe` (raw
  `EXECUTE BLOCK` against a throwaway scratch DB per instance — **no EmberTern interpreter**, so it measures
  the *engine*): **FB3.0.13 sub-routines are CLOSED scopes** (an outer var is rejected at compile, SQL -206
  "Column unknown") — FB3 is genuinely *simpler*, no closures; **FB5.0.3 sub-routines are true closures**
  (read + write, **by reference** — Q2=6, Q3=(5,99), Q4=77, confirming §6.1). **FB4 unverified** (not
  installed — only FB3.0+FB5.0 listening; recorded like P2's FB2.5 / D6's §15.5). **D9 consequence — no new
  abstraction:** the D8 `Frame.LexicalParent` split (gotcha #241) already models both worlds, so D9 only picks
  the lexical parent by server major (`FirebirdDdlReader.ParseServerMajor`, reused from P2) when it pushes a
  local-routine frame — **FB3 → `LexicalParent = null`** (closed, *forced correct*: an outer-referencing
  sub-routine can't even compile in the DB on FB3, so a closed frame is 100% faithful by construction; harness
  injects only the call arguments), **FB5 → `LexicalParent = declaring frame`** (outer reads/writes resolve up
  the chain; harness injects the read set R1–R4 + carries the decl verbatim R5 + reads writes back), **FB4 →
  conservative**, a documented §F boundary. Pure gate work — no production code touched ⇒ build 0/0 + tests
  unaffected. Spec §6.3 resolved, §15.7 log added, compatibility/open-items/roadmap rows updated; plan D9 risk
  resolved.
  **D9 seam (a) Part 1 — AST deepening + binder nested scope + extractor R5 carry — DONE (2026-07-18; pure
  Core, no runtime change).** Starting seam (a) exposed that spec §6.2a was too optimistic against the AST: a
  local sub-routine was **not modelled** — its `DECLARE PROCEDURE/FUNCTION` header was a lossless
  `PsqlLeafKind.Other` leaf and its body a bare sibling `BlockStatement` in the enclosing flat `Statements`, so
  the interpreter would step onto the unrunnable header + through the body as main flow, and `ResolveRoutine`
  would have to re-scan tokens (a **Contract #1** violation). So — exactly as D8 seam (a) deepened the AST
  before its Firebird seam — D9 seam (a) is split: **Part 1 = pure Core AST + binder + extractor; Part 2 =
  runtime.** New `SubroutineDeclaration : PsqlStatement` (+ `SubroutineKind`) carries `Kind`/`Name`/a real
  `Body` (null = forward declaration); its span/tokens cover the whole sub-routine (header + body) so `Body`
  nests (well-formedness). New `BlockStatement.LocalRoutines` (between `Declarations` and `Statements`); its
  `Children` now **merge** decls + local routines by source position (Firebird permits either order). Parser
  (`SqlParser.Psql.cs`): a shared `ParseDeclarationSection` consumes both, `ParseSubroutineDeclaration` ends the
  header at the first depth-0 `AS`/`;` (**not** the first `;` — a sub-routine's own `DECLARE VARIABLE …;` ends
  in one) and parses the body **block-scoped** (`ParseScopedBlockBody`, non-lenient — never swallows the
  enclosing `BEGIN`); the dead `ParsePsqlHeaderLeaf` + unused `IsDeclarationStart` deleted. Binder
  (`SemanticBinder.Psql.cs`): `BindLocalRoutine` gives each sub-routine the **first genuine nested scope** in a
  PSQL body (own params + `RETURNS` + locals in a child `RoutineBody` scope; outer vars resolve via the parent
  walk = the FB5 closure — the FB3 closed-scope distinction is Part 2's `LexicalParent`-by-version, the static
  editor stays permissive). Extractor: `RoutineDeclarations.SubRoutines` now filled verbatim (R5). **No runtime
  change — `ResolveRoutine` still returns null for a local call (step-over in place, §5.3).** Build 0/0; **4848
  tests green** (+7 `PsqlAstTests`, +2 `SemanticModelTests`, +1 `PsqlDeclarationExtractorTests`, +4 corpus
  shapes); smoke clean.
  **D9 seam (a) Part 2 — local-procedure step-into runtime + live fidelity DONE (2026-07-18). Step Into a
  local `DECLARE PROCEDURE` now works as a real debugger frame — proven simulated==real on the lab.**
  `FirebirdDebugExecutor.ResolveRoutine` now resolves a local sub-procedure **before** any server source fetch:
  a new `TryFindLocalProcedure` walks the frame's lexical chain (`f.Body.LocalRoutines`, then `LexicalParent`'s,
  … — spec §6) for a `SubroutineDeclaration { Kind: Procedure, Body: not null }` whose name matches; a match →
  `BuildLocalRoutineAsync` builds the callee frame from the **already-parsed** `Body` (no source fetch — the
  body is part of the enclosing routine's AST), seeds its input params through the **same** D8 argument-seeding
  harness (Contract #4 — no second path), and returns a `DebugRoutine` sharing the **enclosing** source + model
  (a local routine's spans + scope live in the enclosing routine, not a separate compilation). **`LexicalParent`
  by server major (§6.3 gate):** `ParseServerMajor` ⇒ FB5 → the declaring frame (true closure), FB3/FB4 → null
  (closed scope; FB4 conservative, a documented §F boundary). **The one new metadata path** —
  `FirebirdDebugMetadata.BuildLocalRoutineFrameVariablesAsync`: a local routine is **not** a catalog object
  (no `RDB$PROCEDURE_PARAMETERS` row), so its parameter + `RETURNS` types come from the **AST header** via new
  pure-Core `PsqlDeclarationExtractor.ExtractSignature` (params/RETURNS read from the sub-routine's own tokens,
  Contract #1 — never re-parse), each declared verbatim (R3) with its base type derived from `RDB$FIELDS`
  (R2, reusing `ResolveBaseTypeAsync` — a domain param resolves, a builtin is itself); body locals as usual.
  **R5 wired into the harness** (`RoutineContext.SubRoutines`, computed once from `PsqlDeclarationExtractor.Extract`
  and threaded into every `HarnessRequest`): a routine's local sub-routine declarations are carried verbatim so
  a statement that calls a local **function** (or procedure) binds to the local — a local `DECLARE FUNCTION` is
  exercised faithfully server-side as a step-over (it is called inside an expression, never an
  `EXECUTE PROCEDURE` step point, so it is not step-into'd). **Scope boundary (seam a part 2):** the local
  routines are **self-contained** — outer-variable **closure injection** (the closure harness) + the transitive
  read/write-set fixpoint over the sub-routine call graph are **seam (b)**; a self-contained local routine does
  not exercise the closure, so the `LexicalParent` choice is behaviourally inert here but set correctly for
  seam (b). **Lab zoo +`SP_DBG_LOCAL`** (`Lab/setup.sql` + rebuilt `.fdb`, #149): a local function `TRIPLE` +
  local procedure `ADD_TAX` (param seeding, `RETURNING_VALUES`, its own local `BONUS`). **Live fidelity PROVEN**
  (`tools/probes/DebuggerFidelityProbe` extended, not duplicated): the real executor Step-Into'd `ADD_TAX` —
  depth 2, frame chain `SP_DBG_LOCAL → ADD_TAX`, **simulated `TOTAL=115` == real `115`** (arg seeding +
  `RETURNING_VALUES` + the local function server-side), ALL PASS; D8's stored-chain cases unchanged. Build 0/0;
  **4852 tests green** (+4 `PsqlDeclarationExtractorTests` for `ExtractSignature`); smoke clean.
  **D9 seam (b) Part 1 — closure capture for stepped-INTO frames DONE + live-fidelity-verified (2026-07-18).
  Step Into a local routine whose body READS and WRITES an OUTER variable (an FB5 closure over the declaring
  frame) — proven sim==real on the lab.** Three coordinated changes, all reusing existing architecture (no new
  abstraction — the D8 `Frame.LexicalParent` chain already models closures): **(1) Core `Frame`** gained a
  declared-names set (params + body `DECLARE VARIABLE`s) so the scope-chain walk (`TryResolveValue`/
  `SetResolvedValue`) knows which frame **owns** a name from entry — a not-yet-assigned local resolves/writes in
  its declaring frame and correctly **shadows** a like-named outer variable (`Owns(name) = declares(name) ||
  Values.Contains(name)`; empty = old Values-only behaviour, so the fake-driven D1 tests are unaffected).
  **(2) Core `DebugSession`** routes every statement/cursor/Immediate write-back through the closure chain (new
  `ApplyWrites` → `frame.SetResolvedValue`), so a write to a captured outer variable lands in the **declaring**
  frame, not as a spurious callee local (previously `frame.Values.Apply` wrote only the callee frame — a §F bug
  for a closure). Identical to the old direct apply for a non-closure frame (no lexical parent). **(3) Firebird
  `FirebirdDebugExecutor.BindValues`** is closure-aware: beyond the frame's own templates it declares **every
  ancestor frame's variables up the lexical chain** (verbatim R3, current value resolved through the chain),
  so the harness for a sub-routine statement can declare + inject + write back a captured outer variable; an
  inner declaration shadows a like-named outer (first-seen wins). The read/write set for such a statement is
  already precise (`Analyze` surfaces the outer reference via the shared enclosing model) — **no fixpoint
  needed for step-INTO**. **Lab zoo +`SP_DBG_CLOSURE`** (a local `PROCEDURE BUMP` reading+writing outer `ACC`;
  FB5-only by construction — FB3 closed scopes can't compile it, §6.3); `DebuggerFidelityProbe` extended:
  Step-Into'd `BUMP` twice — depth 2, `SP_DBG_CLOSURE → BUMP`, **sim `TOTAL=25` == real `25`** (ACC 5→15→25,
  the closure write reaching the parent), ALL PASS. Build 0/0; **4853 tests green** (+1 `DebugEngineTests`
  pinning the interpreter's write-back routing); smoke clean. **⚠ Boundary — seam (b) Part 2 (NOT done):** a
  **step-OVER** of a local call **with direct arguments** (`EXECUTE PROCEDURE p(x) RETURNING_VALUES y`) whose
  callee mutates OTHER outer variables (not `x`/`y`) still drops those mutations — the transitive read/write-set
  **fixpoint over the sub-routine call graph** is Part 2. (A no-arg local call already over-includes correctly
  via the §3.5 `InScopeLocals` fallback + R5; step-INTO is fully correct.) History:
  [docs/history/19-...](docs/history/19-firebird-debugger.md) (D9 gate + seam a + seam b Part 1). See [[feedback-debugger-ux-polish-backlog]].
  **D9 seam (b) Part 2 — transitive read/write-set fixpoint DONE + live-fidelity-verified (2026-07-18). D9 IS
  COMPLETE.** Closes the last gap: a **step-OVER** of a local call (`EXECUTE PROCEDURE p(x) RETURNING_VALUES y`,
  or a local function call `z = f(x)` inside a leaf — always a step-over) whose callee captures an OUTER
  variable **not named at the call site** now injects that capture and reads its mutation back. New pure-Core
  `SubroutineCatalog` (name → `SubroutineDeclaration`, built by the executor from `BlockStatement.LocalRoutines`
  up the lexical chain) is an **optional** third argument to `ReadWriteSetAnalyzer.Analyze` (default null =
  today's direct-reference set, so D2–D8 are byte-identical): when a statement calls an in-scope local
  sub-routine, the analyzer folds in that callee's **transitively-referenced captured** variables — the callee's
  own body references (span-collected from `model.References`, which is inherently transitive for a **nested**
  sub-routine whose body lies within its parent's span) plus, recursively, every catalog sibling it calls (a
  **visited set** terminates mutual recursion) — then keeps only the ones **in scope at the call site**
  (`InScopeLocals`, so the callee's own params/locals drop out) and adds them to **both** reads and writes
  (over-inclusion is §F-safe: a returned-but-unchanged value writes itself back; an injected-but-unused value is
  harmless). **Reuses the binder for variable references** (Architecture rule #2 intact) — the only new signal
  is *call detection*, a conservative **name-membership** check of the statement's tokens against the
  AST-authoritative catalog (not a variable resolver; the AST models the call graph but the binder does not yet
  resolve local calls as symbols — the seam-a-part-1 note), which covers both an `EXECUTE PROCEDURE` proc call
  and an expression-embedded function call. The executor threads the catalog through `ResolveReadWrite` for the
  statement + condition harnesses (D5 `Evaluate` already uses the `InScopeLocals` superset — no change). **Lab
  zoo +`SP_DBG_CLOSURE_FN`** (a local function capturing `HIDDEN`) **+`SP_DBG_CLOSURE_OVER`** (a local procedure
  capturing `HIDDEN`, stepped OVER); `DebuggerFidelityProbe` extended (`SimulateAsync` gained a `StepKind`
  param): **sim `TOTAL=15` == real `15`** for both — the hidden capture injected + written back across the
  call, ALL PASS. Build 0/0; **4856 tests green** (+3 `ReadWriteSetAnalyzerTests`: function capture with the
  in-scope filter, transitivity across the call graph, null/empty-catalog = direct set); smoke clean.
  **D9 CORE — the flagship — is COMPLETE:** local procedures and functions are real, steppable debugger frames
  with real closure variables; a local **procedure** is faithful step-into *and* step-over, a local function is
  faithful step-over — the capability IBExpert cannot deliver. History:
  [docs/history/19-...](docs/history/19-firebird-debugger.md) (D9 gate + seam a + seam b Parts 1 & 2).
  See [[feedback-debugger-ux-polish-backlog]].
  **D9 seam (c) — local-FUNCTION step-into — DESIGNED, NOT IMPLEMENTED (2026-07-18); the immediate next task
  before D10.** Manual QA found the one asymmetry: Step Into works for a local *procedure* but a local
  *function* runs whole (effectively Step Over) — a complex local function's body can't be traced line by line.
  Not a §F/correctness bug, the last usability gap in the flagship. **Ratified design (closed, no code):** Step
  Into descends into a local function **only when the call is the ENTIRE operand of a value-consuming position**
  (`v = f(x)` / `RETURN f(x)` / `IF f(x)` / `WHILE f(x)` — **Variant A, one mechanism, all four**), so the
  client never evaluates a surrounding expression (a proper sub-expression like `f(x)+1` / `a AND f(x)` /
  `VALUES(f(x))` stays step-over — a permanent §F boundary, §6.4). **No new server path, no expression
  evaluator, no delivery/mini-harness:** reuse Statement + Expression Harness, `SeedInputParametersAsync`,
  `SetResolvedValue`/`ApplyReturningValues`, `Frame`/`LexicalParent`/closures; the return value is delivered
  **client-side** via `SetResolvedValue` (as procedures do for `RETURNING_VALUES`); `RETURN <expr>` computes via
  the **Expression Harness** (never the Statement Harness — `RETURN` is invalid in `EXECUTE BLOCK`); the
  mechanism is a **Function Return Continuation** (a generalisation of `ApplyReturningValues`). Small AST
  deepening only (`CallExpression` + additive lone-call props; Contract #1 — token-scan rejected). Sub-steps
  c1 (AST) → c2 (Core interpreter, fake executor) → c3 (Firebird executor + live fidelity, sim==real on the
  lab); optional c4 UI polish. Full design + handoff: [docs/history/19-...](docs/history/19-firebird-debugger.md)
  §"D9 seam (c)"; milestone brief + danger zones:
  [firebird-debugger-implementation-plan.md](docs/design/firebird-debugger-implementation-plan.md) (D9 §"seam (c)");
  spec §6.4 + boundary #13. **c1 — AST only (pure Core) — DONE (2026-07-19):** `CallExpression` +
  `PsqlLeafStatement.RhsCall`/`.AssignmentTarget` + `IfStatement`/`WhileStatement.ConditionCall`, set by
  **strict** parser producers (whole-operand lone call only; trailing op / second call / dotted target /
  sub-expression ⇒ null ⇒ step-over), reusing D8's `ReadCallArgumentList`/`MatchParenTok`; +
  `SubroutineSignature.ReturnType` (function `RETURNS` type spec, R2 input). Producer-only — no consumer yet
  (`ResolveFunction`/`EvaluateReturn` are c2/c3, staged per gotcha #233); §0 round-trip unchanged,
  binder/formatter untouched. Build 0/0; +19 `PsqlAstTests` +3 `PsqlDeclarationExtractorTests` +2 corpus
  (targeted green; full suite hangs #94/#226 → user-verified green); smoke clean.
  **c2 — Core interpreter (fake-executor-driven) — DONE (2026-07-19):** new internal `FunctionReturnContinuation`
  (`AssignTo`/`SetFrameReturn`/`BranchIf`/`DecideWhile`) with a `RecognizeStepInto` factory — **the single place**
  the interpreter decides step-into + which continuation consumes the return (not scattered across the
  IF/WHILE/leaf cases; the user's architectural request). `Frame` gained `ReturnType`/`ReturnValue`/
  `ReturnContinuation`/`IsFunctionFrame` + `SetReturnValue`/`TerminateForReturn`; `IDebugExecutor` gained
  `ResolveFunction` + `EvaluateReturn` (+ `ReturnOutcome`, `DebugRoutine.ReturnType`). `DebugSession` got two
  guarded `ExecuteCurrent` branches (step-into a resolved local function; a `RETURN <expr>` in a function frame
  → Expression-Harness `EvaluateReturn`) and ONE delivery switch `ApplyReturnContinuation` generalising
  `ApplyReturningValues`; a raised function unwinds without firing the continuation. `FirebirdDebugExecutor` got
  **c2 stubs** (`ResolveFunction`→null, `EvaluateReturn`→throw) so **live behaviour is byte-identical to D9
  core** until c3. Build 0/0; +11 `DebugEngineTests` (4 positions + deliver, nested `RETURN f()`, IF then/else,
  WHILE iteration, unresolved/step-over, raising ⇒ no continuation, plain `RETURN` ⇒ EvaluateReturn); targeted
  green (508); full suite hangs #94/#226 → user-verify; smoke clean.
  **c3 — Firebird executor + live fidelity — DONE (2026-07-19):** `FirebirdDebugExecutor.ResolveFunction` walks
  the lexical chain (generalised `TryFindLocalRoutine(name, frame, kind)`, shared with `ResolveRoutine`) for a
  local `DECLARE FUNCTION` — a **local shadows a same-named global** — builds its frame from the AST body,
  seeds args through the **shared** `SeedInputParametersAsync` (generalised to `(arguments, callTokens,
  callStart, …)`), and carries the `RETURNS` base type; `EvaluateReturn` computes the `RETURN` operand via the
  Expression Harness through a new `EvaluateExpression` **shared with `EvaluateCondition`** (one server path).
  `FirebirdDebugMetadata`: `DebugFrameLayout.ReturnType` (R2, via `ResolveBaseTypeAsync`). Lab zoo +4
  (`SP_DBG_FN_POS`/`_TYPES`/`_SHADOW`/`_CLOSURE`; `.fdb` rebuilt #149). **Live fidelity PROVEN (spec §15.11,
  `DebuggerFidelityProbe` cases 8–11 + re-pointed 4/6): all four positions (depth 3), six return types
  (INTEGER/BIGINT/NUMERIC/VARCHAR/BOOLEAN/NULL), shadowing, nesting, a closure — all sim == real.**
  **Live-verified: Firebird forbids nested sub-routines** (gotcha #244) ⇒ shadowing is local-vs-global.
  Build 0/0; 508 Core tests green (c3 is Firebird/metadata only, no Core change); smoke clean.
  **🏁 D9 IS COMPLETE — local procedures *and* functions step faithfully, into and over.**
  **D10 (Triggers) — STARTED; architecture review DONE + ratified, Seam A (pure Core) DONE (2026-07-19). Split
  into 3 committable seams (A Core → B Firebird+Live-Fidelity → C UI), mirroring D8/D9; user decisions: NEW/OLD
  in a `FOR SELECT` cursor is a §F boundary (clear refusal, not partial fidelity); lab to be extended with
  BEFORE DELETE + BEFORE INSERT OR UPDATE (full trigger matrix); "seed from a real row" deferred to Seam C2.**
  Two ratified architecture decisions shaping the milestone: (1) **there is no heavyweight `TriggerContextModel`
  — trigger context is *specialized state* mounted as an optional field on the existing per-routine context**:
  NEW/OLD are ordinary frame variables (synthetic names), values live on the frame, only the simulated event +
  timing are genuinely new; (2) **`ContextSubstitution` is entirely `SemanticModel`/`SymbolReference`-driven,
  never a text search** — confirmed feasible because the binder records the `NEW`/`OLD` `RecordAlias` reference
  **and** the following `Column` reference (span + text) *even in the debugger's metadata-less model* (the
  member does not resolve to a `ColumnSymbol`, but its reference span/text is all the engine needs). **Seam A —
  pure Core, no server, no UI, unwired (gotcha #233):** new `EmberTern.Core.Sql.Debugging.ContextSubstitution`
  (the ONE engine, designed to also serve the §3.6 handler error context — one mechanism, two consumers):
  `BuildColumns(model, scope)` assigns each distinct `NEW.col`/`OLD.col` a **stable, compact synthetic name**
  (`ET_CTX_i` — index-based, so it stays a valid ≤31-char identifier regardless of column-name length, FB3's
  limit; ContextSubstitution is the single owner of the naming convention), and
  `Substitute(model, source, region, context)` rewrites each `NEW.col`/`OLD.col` reference span to its synthetic
  and each `INSERTING`/`UPDATING`/`DELETING` predicate to `TRUE`/`FALSE` for the simulated event — reporting the
  context reads (inject) and writes (return; `NEW` only when a BEFORE trigger, over-inclusive but never missing
  a write; `OLD` never). New `TriggerContext` record (`TargetTable`/`Event`/`Timing`/`Columns`) with the §8.1
  availability rules as computed properties (`OldAvailable`/`NewAvailable`/`NewWritable`) — the value that Seam B
  mounts on `RoutineContext`. Pinned by 13 `ContextSubstitutionTests` built the debugger's way (**strict parse,
  NO metadata**): distinct/deduped columns, synthetic rewrite + read/write split, AFTER = no NEW write,
  predicate literals per event, **string-literal `'OLD.STATUS'` left byte-intact beside a real `OLD.STATUS`**
  (reference-driven proof), no-context verbatim, full availability matrix. Build 0/0; Seam A tests + 191
  neighbouring Core/semantic tests green; smoke clean.
  **D10 Seam B — Firebird executor + metadata + Live Fidelity — DONE + live-fidelity-verified (2026-07-19).**
  The trigger substitution is wired end-to-end and proven sim==real on the lab. **`RoutineContext` gained an
  optional `TriggerContext?`** (non-null only for a trigger root frame — a stepped-into stored/local callee has
  no NEW/OLD, so D8/D9 paths are untouched); a new `FirebirdDebugExecutor.CreateAsync` trigger overload merges
  the **NEW/OLD context columns** into the frame templates and registers the context on the root.
  `ExecuteStatement`/`EvaluateCondition` route every trigger-frame fragment/condition through
  `ContextSubstitution` (unioning the context reads/writes into the harness read/write set); `OpenCursor`
  **refuses a `FOR SELECT` that references NEW/OLD** with a clear message (the §F boundary, decision 2). One new
  metadata path — `FirebirdDebugMetadata.BuildTriggerContextVariablesAsync` — types each context column from the
  **trigger's target table** (`RDB$RELATION_FIELDS ⨝ RDB$FIELDS` via the existing `FormatType`, derivation not
  guessing). `DebugLaunchSpec`/`FirebirdDebugSessionLauncher` carry the `TriggerContext` through. **Two §F
  corrections found by probing, not reasoning:** (a) a context variable is declared with the column's **BASE
  type, never its domain** — a NEW/OLD field is a record field, not a domain-constrained local, so a
  user-supplied value that violates the column's `CHECK` (the very case a BEFORE trigger catches) must inject
  freely (gotcha #246; injecting `-5` into a `D_AMOUNT CHECK(VALUE>=0)` domain died on entry before the
  trigger's own logic); (b) inside an embedded DSQL statement a context reference is **colon-prefixed**
  (`:ET_CTX_i`) — a bare name there is read as a column — while a PSQL expression keeps it bare, chosen by
  `node is not PsqlStatement` (gotcha #247). **Lab extended** (`Lab/setup.sql` + rebuilt `.fdb`, #149) with an
  isolated `TRIG_LAB` table + a **BEFORE DELETE** (`TR_TRIG_BD`, OLD-only) and a **BEFORE INSERT OR UPDATE**
  (`TR_TRIG_BIU`, multi-action) trigger — isolated so they never clobber each other or the ORDERS triggers,
  closing the full trigger matrix. **Live fidelity PROVEN** (`DebuggerFidelityProbe` +5 cases, spec verification
  method — compare the body's *effects* since the triggering DML is not performed): BEFORE UPDATE exception
  (E_NEGATIVE_AMOUNT) sim==real, AFTER UPDATE audit side-effect (the `AUDIT_LOG` DETAILS row, read from the debug
  tx) sim==real, BEFORE DELETE (OLD-only) exception (E_ORDER_LOCKED) sim==real, and the multi-action BIU trigger
  producing `NEW.NOTE='INSERTED'` (INSERTING) / `='UPDATED'` (UPDATING) sim==real for both events — plus all 11
  D8/D9 cases still green (no regression). Build 0/0; 122 debugger Core/Firebird unit tests green; smoke clean.
  **D10 Seam C (UI) — DONE + user-confirmed (2026-07-19; commit `050b790`). 🏁 D10 COMPLETE — triggers debug
  end-to-end.** `TriggerHeaderReader` (Core) derives `(TargetTable, Timing, Events)` from the parsed trigger
  (refuses DB-level/DDL, §8.1); `TriggerContextEditorViewModel` is a **dumb VM** — availability read from Core
  `TriggerContext`, NEW/OLD grids show **only referenced columns** (typed via `EnsureColumnsAsync`), values mapped
  onto their synthetic frame variables; `DebuggerTabViewModel` trigger mode (prepare/launch), a Variables
  **Context** group (resolved via new `DebugVariableRowViewModel.ResolveName` = synthetic); "Debug trigger…" entry
  points (sidebar + trigger-editor toolbar). **QA-found + fixed — gotcha #248:** a `NEW`/`OLD` reference inside a
  **scalar subquery embedded in a PSQL assignment** was emitted bare → Firebird read it as a column (SQL -206);
  the colon-vs-bare decision is now **per-reference** from AST-derived colon-regions
  (`FirebirdDebugExecutor.ColonRegions`, each embedded `SubqueryExpression` span), not per-statement. Lab
  +`TRIG_SUBQ_LAB`/`TR_SUBQ_BU`; `DebuggerFidelityProbe` case 17 sim==real. "Seed from a real row" stays a
  future **C2**. **Terminal debug states (Completed / Faulted) — DONE + user-confirmed (2026-07-19; commit
  pending after this doc):** the session no longer clears on end. **Completed** keeps the last state visible with
  the closing **`END` marked** (execution finished — IBExpert-like), Variables/Context/Call-Stack showing final
  values; **Faulted** stops **on the raising statement** (marked), keeps Variables/Context/Call-Stack with the
  values **at the error**, and the status line goes **red+bold** (`IsFaulted`); in both, stepping is disabled and
  only **Restart/Stop** are active — **Stop** tears the session down + clears. Additive Core: `DebugSession`
  retains `FinalFrame`/`LastStatement` (Completed) and snapshots `FaultStatement`/`FaultFrame`/`FaultStack`
  **before** the exception unwind (which DB-rolls-back + pops but never touches client-side `frame.Values`) —
  `CurrentFrame`/`CallStack` still null after termination (existing contract unchanged). Running / Completed /
  Faulted / Stopped are now distinct debugger states.
  **Superseded note (D5 seam b already shipped): —
  Watches panel + per-routine persistence** (auto-re-evaluate after each step through the same
  `DebugSession.Evaluate`; flag a non-pure-expression watch; persist per routine). Order stays **risk-first**
  (P1 → P2 → D1 → D2 → D3 → D4 → D5 …). **Read the plan + your milestone's brief before writing any debugger code.**
- **Save-and-close / Save-and-disconnect — DONE + user-confirmed (2026-07-17).**
  The close/disconnect WorkGuard can now **compile every dirty metadata editor in one pass** instead
  of only listing-and-discarding them. It **reuses the group-recompilation pipeline** (one save
  mechanism, not a second): each editor's existing compile is wrapped by a thin
  `ISavableObjectEditor.SaveAsync` adapter (structured pass/fail; editors swallow errors into
  `ErrorMessage`, so the adapter reads `ErrorMessage is null`), and `RunBatchWithReportAsync` gained
  an optional `executeAsync` strategy delegate so `SaveDirtyEditorsAsync` drives those `SaveAsync`
  calls through the **same** batch-results dialog (recompile's SQL path is the unchanged default).
  **Continue-and-report** (user decision): all dirty editors are attempted; close proceeds only if
  all succeed, else it aborts and selects the first failed tab — DDL auto-commits per object, so a
  mid-batch failure never undoes the ones already saved. App close adds **Save and exit**; disconnect
  is **two-phase** (metadata Save/Discard/Cancel → the unchanged tx Commit/Rollback/Cancel).
  **Scope includes new objects.** Save order = tab order (a deliberate v1 simplification; dependency
  ordering is a possible future refinement, not required — continue-and-report + retry covers it).
  `_bulkSaveInProgress` suppresses the per-compile "recompile dependents?" offer mid-shutdown. New
  `ISavableObjectEditor` + `EditorSaveResult`; adapters on every object editor;
  `WorkspaceTabViewModel.SavableEditor`. Build 0/0, tests green (`DataLossGuardTests` +save cases).
  Full detail: [docs/history/08-...](docs/history/08-data-loss-sidebar-and-searchable-combo.md);
  gotcha #231 (decide from the loop's tally, not the batch dialog's `IProgress`-lagged counters).
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
- **Build**: 0 warnings / 0 errors (`TreatWarningsAsErrors=true`). **Tests**: **5801 as of 2026-07-27
  (`feat/data-import`, after the post-I10 ergonomics seam)** — green in two partitions (5761 + the 40-test
  `ConnectionExpandBindingProbe`), ~12s.
  `ConnectionExpandBindingProbe` uses **one shared `HeadlessUnitTestSession`** — what gotcha #94 always
  prescribed, and **mandatory**, because AvaloniaEdit's static `KeyBinding` lists make any real key sent into
  a `TextEditor` throw cross-thread from every session after the first (#226).
  **⚠ 2026-07-27, I9 session — the hang REPRODUCED, and the instrument named a suspect.** A plain
  `dotnet test` hung; a first `--blame-hang` run went clean; a later one caught it and reported the test
  running at the moment of the hang as **`ConnectionExpandBindingProbe.CompletionRow_HighlightsMatchedPrefix`**
  (vstest prints its own caveat that this may or may not be the cause). Two facts worth carrying: the hang is
  **after** the tests finish, not a failing test — the aborted run had already reported *5677 passed, 0
  failed, 6 s* before it stopped exiting; and it is in the headless-Avalonia probe class, consistent with
  #94/#226/#261. **Nothing was changed on this evidence** (it is one observation and the session's task was
  I9), but a future investigation now has a named starting point and a dump under
  `tests/EmberTern.Tests/TestResults/`. Practical workflow meanwhile: run the two partitions, always with
  `--blame-hang`. **📋 Ratified by the user on closing I9: this is its OWN infrastructure task and is
  explicitly NOT to be picked up inside Data Import** — record findings here, never detour a module etap
  into it.
  **⚠ The intermittent full-suite hang the user keeps hitting is NOT claimed fixed.** It did not reproduce
  during the 2026-07-26 investigation (5568 green in ~9s, repeatedly, and the probe class alone in 6s), so
  nothing was restructured on a hypothesis. What that investigation *did* find and fix is a real defect with
  a plausible mechanism: the shared session was held in a `static readonly` field and **never disposed**,
  and Avalonia's own contract says *"Disposing unit test session stops internal dispatcher loop"* — i.e. it
  left a thread spinning a dispatcher loop after the last test. Ownership moved to an `IClassFixture`
  (`HeadlessSessionFixture`), still ONE session (gotcha #261). **When it next hangs, do not re-run and hope
  — run the instrument**, which turns an infinite wait into a two-minute named failure:
  ```bash
  dotnet test EmberTern.slnx --blame-hang --blame-hang-timeout 120s
  ```
  Smoke: clean (app launches).
- **Script Executor — Dev Mode integration DONE (2026-07-16; impl, awaits user visual confirmation).**
  The Script Executor no longer ignores Developer Mode. An **all-DDL script under auto-commit** begins
  its transaction with the Dev Mode-aware DDL wait policy (`FirebirdDdlExecutor.BuildDdlTransactionOptions`
  — reused, not duplicated) instead of the working transaction's NOWAIT default, so deploying objects
  other sessions are using waits rather than failing instantly. **Deliberately NOT changed:** one lane,
  one transaction, no per-statement commits, no routing by statement kind (#215 stands) — this is one
  TPB flag chosen at BEGIN. **Both conditions are load-bearing** (`FirebirdScriptExecutor.UsesDeveloperModeWaitPolicy`,
  pure + unit-pinned): *all-DDL* because a transaction's wait policy is fixed at BEGIN and cannot vary
  per statement, so it is the only thing guaranteeing no DML ever waits; *auto-commit* because Manual
  leaves the tx OPEN and `BeginTransactionAsync` early-returns on an active tx, so the SQL Editor's next
  F5 would **join** it and silently get a WAIT console (gotcha #230). `TransactionService.BeginTransactionAsync`
  gained an optional `FbTransactionOptions`; the console never passes it and is unchanged. Full analysis:
  [docs/design/script-executor-transaction-review.md](docs/design/script-executor-transaction-review.md).
- **✅ `FirebirdScriptExecutor` mixed-DDL+DML defect (#213) — FIXED by the Script Executor Rewrite (Steps
  0–6 COMPLETE, live-verified 2026-07-21).** The `Sequenced` ("Deployment") mode runs a mixed migration one
  committed segment at a time (commit after each schema statement, like isql `SET AUTODDL ON`), so a
  create-then-populate script runs end-to-end — proven live (12 scenarios ALL PASS). The **single-transaction**
  modes (`Manual` / `AutoCommit`) still cannot run a mixed DDL+DML script — that is a Firebird truth, not a bug
  (a transaction cannot use an object it created but has not committed) — and the App now **rejects such a
  script up-front** in those modes with a message naming `Sequenced` (Step 5 seam B). The old *"Commit or roll
  back the active transaction…"* guard framing and the "all-or-nothing" docstring are gone. The Core
  classification lives in the AST-based `SqlStatementClassifier` (the segment planner uses it; not the driver
  enum). See `docs/history/15-...` and
  [docs/design/script-executor-transaction-review.md](docs/design/script-executor-transaction-review.md).
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
  delay and noise budget the unified hover needs — see `editor-stage7-diagnostics.md` §15). **P2c is DONE**
  (2026-07-17) — see the Completion Matching bullet. Formatter grammar-depth items now folded into Etap 6.9 as node
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
  the single App entry point. **App layer + QA sprint — DONE and USER-APPROVED (2026-07-16); LANGUAGE
  COMPLETION IS COMPLETE.** `LanguageExpansionController` (App/Completion, attached in BOTH seams — gotcha
  #219) has ONE decision point, `CurrentEdit()`, returning the very `ExpansionEdit` Tab applies — the hint
  renders *that object's* text, so preview and result **cannot** drift (casing was the first proof: `IF`
  previewed `if () then`, inserted `IF () THEN`). Every subscription only says *re-evaluate*; the sole state
  is `_dismissedAt` (Escape's caret offset — not derivable from (text, caret); without it Escape hid the card
  while Tab still expanded). Guards: focus (`TextArea.IsKeyboardFocusWithin` — **`TextEditor` is NOT
  focusable and `editor.Focus()` is a no-op**, gotcha #225), no selection (Tab = block indent), list closed,
  not dismissed. Passive `OverlayLayer` hint; **Tab** expands via a tunnelled KeyDown (gotcha #224).
  **Grammar arming** now returns a `[Flags]` set so a caret can be both Clause and StatementStart: a **blank
  line** *adds* StatementStart (fixes `where` ⏎ blank ⏎ `if`), `(` arms subquery `select`, and a bounded
  enclosing-statement look-back arms `INSERT … SELECT`; widening never removes a position. **ONE
  RESPONSIBILITY, ONE OWNER** — separation is by **vocabulary** *and* **grammatical position** (design §9.1,
  gotcha #228): `LanguageConstructCatalog.OwnedWords` + `KeywordPairCatalog.OwnedWords` (new
  `Core/Sql/Language/Ergonomics/` — **data only**, `begin`/`end` for Typing Ergonomics) are *derived* by
  `CompletionEngine.AddKeywords`, never hand-listed; **and** the identifier list no longer auto-pops where a
  construct is armed (it asks the same `Resolve`). Without that second half, typing `select` **inserted a
  procedure named `SELECT_PRACOWNIKOW`** — a *name* is not a keyword, and an open list owns Tab. Ctrl+Space
  still overrules the grammar. **The Etap-5 keyword live templates are DELETED** (`SnippetEngine`/
  `SnippetTemplate`/`SnippetCompletionData` + tests — design §11's unfinished clause; also removed the P7
  auto-trigger exception and the duplicate `ShowBaselineWindow`). **NOT touched:** the drag-drop
  `Core/Sql/Templates/*` + `SqlSnippetDropTarget` — a different, shipped feature that only shares the word.
  Coverage guarantee: `EveryConstruct_ArmsWhereItMayBegin` (16 constructs × 33 positions) — exclusive
  ownership makes an under-armed position a **dead zone**, so a catalog row arming nowhere fails the build.
  **Conscious open items (user decisions — revisit only on real usage):** `OVER (ORDER BY` doesn't arm;
  single-letter clause arms collide with aliases (`from ORDERS o` → `⇥ order by `); uniqueness is
  catalog-wide before gating (`wh` stays silent after FROM); **CASE stays an IntelliSense keyword** — the
  catalog is intentionally small, grown from real usage, not completeness.
- **Typing Ergonomics — DONE + user-approved (2026-07-16). THE LANGUAGE-EXPANSION DESIGN IS FULLY
  DELIVERED.** `begin` has its owner. (1) **`begin … end` pairing** (`KeywordPairing`) — trigger settled as
  **Enter** (pairing on the word's completion was rejected: it fires while typing `begin_date = …`, a Rule 0
  violation). Enter keeps its meaning — the caret lands where plain Enter+indent would, and the closer
  appears on the line *below*. Pairs only when an `end` is genuinely **missing** (CASE-aware, else Enter
  after an existing `begin` bolts on a second `end`) and only at a statement position (`ConstructContext`).
  (2) **Delimiter pairing** (`DelimiterPairing`) — `()`/`[]`/`''`, type-through (checked BEFORE pairing, as
  `'` is self-closing), smart backspace, suppressed inside literals/comments and before a word; literal
  openness by **quote parity** (`'it''` is open despite ending in a quote); line-comment span boundary =
  gotcha #229. (3) **Structural auto-indent** (`AutoIndent` + `SqlIndentationStrategy`) via AvaloniaEdit's
  own `IIndentationStrategy` seam — one level per unclosed block, +1 after `then`/`do`/`else`, `end` backs
  out. **ONE FORMATTING LANGUAGE (user directive):** the indent is `SqlFormatter.PsqlIndentUnit` — now
  **published by the formatter** because the editor's tab settings were a wrong guess — and a block's indent
  is **structural**, not the opener's typed column (the formatter puts a block under `then` at the `if`'s
  level, a single-statement body one deeper). Pinned by tests running the real formatter over generated
  blocks (`Format(x) == x`). **Deliberately simpler than the formatter:** parens are OUT of auto-indent (it
  aligns to columns, unknowable line-at-a-time) and `IndentLines` is **inherited untouched** from
  `DefaultIndentationStrategy` — re-indent-selection is a lightweight command, not a second formatter (both
  user decisions). CASE-aware block depth lives once in `BlockStructure`, shared by pairing + auto-indent.
  `'` pairing kept pending real usage (one-line removal if it annoys). Build 0/0, **4347 green**, smoke
  clean. **M4 Structural Selection remains future; M3 Snippet Engine would now start from scratch** (that
  engine is deleted).
- **Completion Matching Philosophy — prefix-first IntelliSense — COMPLETE (2026-07-17; impl + headless-probe
  proven, awaits the user's visual confirmation).** A separate **Completion** milestone (not Stage 8):
  interactive completion is a **prediction engine, not a search engine**. No prefix → all (Ctrl+Space);
  prefix with ≥1 StartsWith → **only** StartsWith; zero StartsWith → **no window** (never a Contains
  fallback); identical for every kind. The user re-reported the original symptom (`cont` → every
  `…CONTRACTOR…` object) as a **regression on 2026-07-17 — it wasn't one**: the foundation had shipped
  unused, so AvaloniaEdit's substring filter was still the only thing narrowing the list (gotcha #233 — a
  tested-but-uncalled component looks exactly like a regression, and the green suite is what hides it).
  **Now wired:** `SqlCompletionController` is a passive view — `IsFiltering = false` kills AvaloniaEdit's
  substring filter *and* its quality re-sort (**measured**, gotcha #232), every source (baseline, dot,
  on-demand column warm) routes through `ShowItems` → the one `CompletionMatcher`, and `RefreshOpenWindow`
  (off `Caret.PositionChanged`, so backspace/paste count too) re-filters the session's cached candidates.
  **⚠ The refresh MUST re-assign `ListBox.ItemsSource`, never just mutate `CompletionData`** — that is a
  plain `List<ICompletionData>` and broadcasts no change, so mutation updates the data and **nothing on
  screen** (the list froze on `ID_AKWIZYTOR` while every collection correctly read `ID_NAGL`). Turning
  `IsFiltering` off removes AvaloniaEdit's `SelectItemFiltering`, whose fresh-List assignment to
  `ItemsSource` *was* the refresh mechanism; `Populate` now mirrors that (gotcha #234). Pinned by a probe
  that types into the **open** window and asserts the **realized containers** — asserting `ItemsSource`
  reads our own input back and cannot fail (gotcha #235).
  **Responsibility split — one owner per *question*:** `CompletionEngine` answers "what is legal at this
  caret" (candidate set — a property of the *position*, fixed for the session); `CompletionMatcher` answers
  "which of those match what is typed" (a property of the *prefix*). So `CompletionEngine` deliberately did
  **NOT** get a `prefix` param, contrary to the original directive's letter: a prefix-filtering engine
  cannot widen on a **backspace** without a per-keystroke re-query — against a debounce-lagged model whose
  offsets no longer match the caret, or a synchronous whole-document parse (Etap 0 forbids it). **Deleted as
  now-redundant:** `ApplyInitialFilter` (#200) and `CloseIfNarrowedToNothing` (#227) — taking ownership of
  the filter removed both workarounds rather than adding to them — plus `BuildColumnDetail` and the warm
  path's second column-row builder. The planned `IsFiltering=false` fallback design was unnecessary and is
  not kept. Build 0/0, **4594 green**, smoke clean. As-built + the stale-list follow-up (incl. why six
  reproductions passed before the right instrument was applied):
  [docs/history/17-completion-matching-philosophy.md](docs/history/17-completion-matching-philosophy.md).
  **P2c — matched-fragment highlight — DONE (2026-07-17, user-requested "jak w IBExpert"), and it CLOSED
  ITSELF as a side effect.** P2c ("bold the typed fragment") was deferred for months as *"no clean
  AvaloniaEdit 12.0.0 path"* — true only while AvaloniaEdit owned filtering: rows were built once at open
  and the App never knew the prefix at row-build time. Taking the list over made `Populate` rebuild rows on
  every prefix change **with the prefix in hand**, so the blocker evaporated. Shipped as colour, not bold
  (the IBExpert cue the user asked for): `SqlCompletionData.BuildName` splits the name into two `Run`s —
  matched fragment in the new `CompletionMatchBrush` theme token (both dictionaries; deliberately NOT
  `ErrorBrush`, which it sits beside in the palette — this means "why this row is here", never "something is
  wrong"), unmatched tail inherits the row foreground so selection/theme still drive it. Empty prefix →
  plain text (no meaningless colour on a Ctrl+Space list). **The split renders `CompletionMatcher`'s ruling,
  it does not re-derive it** — `[0, prefix.Length)` follows from StartsWith; if the matcher ever grows a
  tier matching elsewhere it must report the span (§9.1's one-owner rule, one level down). Pinned by
  `CompletionRow_HighlightsMatchedPrefix` (split + the brush is the token + present in both dictionaries).
  Still open (ranking taste): whether common leading keywords deserve a boost over same-prefix objects.
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
backlog: ~~editor-wiring consolidation~~ (**DONE** — debugger milestone D3, 2026-07-17: the main SQL editor
now goes through the one `SqlEditorBehavior.Attach`; gotcha #219 resolved), **Quick Fixes**
(`editor-stage7-diagnostics.md` §12), Folding and Breadcrumbs.
**P2c is DONE** (2026-07-17): its "no clean AvaloniaEdit path" blocker was a *consequence* of AvaloniaEdit
owning the completion filter, and dissolved the moment the Completion Matching milestone took the list over
— a reminder that a long-deferred item is worth re-testing after the thing under it changes. Nothing is
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
- a new **component / control** → check `Controls/`, `Views/` and `ViewModels/` for an existing one to extend;
- a new **error / warning / info message** on a work surface → use `Controls/MessageBanner` (severity +
  message + optional Copy/Expand/Dismiss); never a locally-styled coloured `TextBlock`;
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

The **complete** catalog (255 entries, organized thematically) lives in
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
- **`SqlEditorBehavior.Attach` IS now "the one seam" for the editor-intrinsic block — RESOLVED by D3
  (2026-07-17).** It *used* to install only the OBJECT editors' capabilities while the main SQL Editor
  hand-wired its own in `MainWindow` — so a capability added to only one silently missed the other (how S3
  shipped with no squiggles in the SQL Editor). **D3 consolidated it:** `MainWindow` now calls the same
  `SqlEditorBehavior.Attach` at VM-arrival, so a new editor-intrinsic capability goes in **one** place.
  Per-host wiring (`DiagnosticsPanelHost.Track`, `AmbientModelRefresh`, `SqlSnippetDropTarget`) stays with the
  caller by design. *(#219 — resolved)*

**General**
- Reflect the actual API surface (get/set, public/protected) before assuming a member is
  settable or overridable — a member appearing in a metadata dump doesn't mean it has an
  accessible setter or is safely overridable. *(#199, applies broadly)*
- One headless UI test session per test **process** — share it, never `StartNew` per test. Not tidiness:
  AvaloniaEdit builds its caret/editing `KeyBinding`s as **static** lists owned by the thread of whichever
  session first constructs a `TextEditor`, so any real key sent into an editor from a later session throws
  *"the calling thread cannot access this object"* — no injection style avoids it. *(#94, #226)*
- **Reflect the real runtime contract of a UI member before guarding on it.** AvaloniaEdit's `TextEditor`
  is **not focusable** — `editor.Focus()` is a no-op returning `false` and `editor.IsFocused` is *always*
  false; keyboard focus lives on `editor.TextArea`. A guard written against the plausible-looking member
  compiles, tests green, and silently disables the feature forever. *(#225, an instance of #199)*

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
- **`docs/design/firebird-debugger.md`** — **Stage X — Firebird Debugger. DESIGN v2, decisions ratified
  2026-07-17; this is the target implementation spec. Nothing implemented.** Read before any debugger
  work. Key established facts (all measured against the live engine — §15 is the log): Firebird exposes
  **no debugging API at any version** (`RDB$DEBUG_INFO` is a BLR→source map, `MON$CALL_STACK` is
  read-only, `RDB$PROFILER` measures but cannot stop), so every Firebird debugger is a **client-side
  PSQL interpreter**. EmberTern's owns **control flow** (from the AST — incl. exception handlers) and
  delegates **all semantics** to the server via a generated anonymous `EXECUTE BLOCK` harness, so **no
  expression AST is needed** (the structural-depth boundary holds). Local routines need **no temporary
  packages** (IBExpert's workaround): stepping into one is just another frame. **The v1→v2 review
  falsified four claims** — a per-statement harness does **not** preserve Firebird's **call atomicity**
  (⇒ a SAVEPOINT per simulated frame), injecting frame state is **not** semantically neutral (a harness
  that assigns `NULL` into a `NOT NULL`-domain variable **fails on ordinary ERP code**), the **clock** is
  request-scoped (`CURRENT_TIMESTAMP` diverges while stepping), and **`WHEN … DO` was missing entirely**
  (⇒ **prerequisite P1**: the AST does not model handlers — they are a `PsqlLeafKind.Other` token bag).
  **⚠ `IN AUTONOMOUS TRANSACTION` work and generator increments survive the debug rollback** — "nothing
  is persisted" is false. Debugger scope is **FB3/FB4/FB5 only**; FB2.5 is already unreachable (the
  driver is Srp-only, FB2.5 is Legacy_Auth-only), so **P2**'s connect-time version gate ratifies reality
  rather than dropping support. The editor-wiring consolidation (gotcha #219) is **D3**, immediately
  before the first debugger UI — deliberately *after* the pure Core/Firebird milestones (D1/D2), which
  need no wiring.
- **`docs/design/firebird-debugger-implementation-plan.md`** — **the debugger's execution plan; read it
  (plus your milestone's brief) at the start of every debugger implementation session.** Milestone briefs
  for **P1** (AST exception handlers — blocks D1), **P2** (FB3+ version gate — app-wide, not
  debugger-scoped), and **D1–D14**, each with scope / components touched / new types / dependencies /
  risks / Definition of Done / how to verify (tests + Lab). Also: the **session split** (≈28 sessions,
  each ending build 0/0 + green tests + smoke + committable, with explicit seams inside the big
  milestones), the **danger zones** (dual editor wiring #219 until D3, one headless session #94/#226,
  `TextEditor` not focusable #225, `TextView.Redraw()` #223, dispatcher priority #221, the user's
  transaction is untouchable, per-wire-operation locking #236), and the **Developer Contract** — 20
  binding rules (never re-parse SQL, never duplicate `SemanticModel`, never re-implement Firebird
  semantics, the harness is the only server path, no alternative execution paths, no temporary metadata,
  §F outranks features, verify-don't-infer, one milestone per session ending green). **Order: P1 → P2 →
  D1 → D2 → D3 → D4 …** — risk first; the wiring consolidation sits at D3 because D1/D2 are pure and need
  no wiring.
- **`docs/gotchas.md`** — the complete gotcha catalog (259 entries, #1–#272), organized thematically.
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
