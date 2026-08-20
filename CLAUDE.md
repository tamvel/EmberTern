# EmberTern — Claude Code Context

A modern desktop developer workbench for Firebird database developers, built with **.NET 9 + Avalonia
12.1.1**. Target users: ERP and backend devs who work daily with SQL, procedures, triggers, metadata
and transactions. Design philosophy: **less features, better experience; workflow quality over
feature count; transaction-aware by default**.

**Stack:** .NET 9 · Avalonia 12.1.1 (Fluent) · CommunityToolkit.Mvvm · AvaloniaEdit ·
`FirebirdSql.Data.FirebirdClient` (managed, Srp-only) · DocumentFormat.OpenXml + ExcelDataReader
(`EmberTern.Office` only) · xunit. Solution `EmberTern.slnx`, all projects `net9.0`, nullable enabled,
warnings as errors.

> **This file is the operating manual: rules, architecture, pointers. It is loaded automatically into
> every session, so it is kept short — target ~700 lines.**
> **Status is in [`docs/current-state.md`](docs/current-state.md). Narrative is in `docs/history/`.**
> ⛔ Do not append stage reports here — see § "Working style" → "The tripwire".

---

## Documentation map (read this first)

`CLAUDE.md` is the **operating manual**: rules, architecture, and pointers. It is loaded into every
session automatically, so it is kept short on purpose. **It is not the diary** — narrative lives in
`docs/history/`, status lives in `docs/current-state.md`, and detailed per-area specs live in
`docs/design/`.

⚠ This split was made by the **Documentation Cleanup Sprint (2026-07-11)** and restored by a **second
one (2026-08-11)** after the file regrew 472 → 6 849 lines. Nothing was ever deleted — every word
that used to live here still exists verbatim in `docs/history/`.

### Always

| Document | When |
|---|---|
| **`CLAUDE.md`** (this file) | Every session, automatically. Rules · architecture · live gotchas · pointers. |
| **`docs/current-state.md`** | To learn what is done / open / in progress. **The only status source.** |
| **`docs/gotchas.md`** | When a bug feels familiar. The complete catalog, organized thematically. |
| **`docs/history/README.md`** | Index into the narrative archive (31 files). |

### Editor & SQL/PSQL language front-end

| Document | When |
|---|---|
| `design/editor-architecture.md` | Before touching `EmberTern.Core.Sql.Language` or anything downstream. Kept current. |
| `design/editor-ast-deepening.md` | Parser / AST / binder work (Etap 6.9 — closed; node inventory + migration contract still apply). |
| `design/editor-stage7-diagnostics.md` | Diagnostics engine, squiggles, panel, navigation. ⚠ §12 is **superseded** by `editor-quick-fixes.md`. |
| `design/editor-quick-fixes.md` | Quick Fixes / code actions. Owns the `CodeAction`/`TextEdit` currency and the ONE `TextEditApplier`. |
| `design/editor-language-expansion.md` | `Core.Sql.Language.Constructs` / `.Ergonomics`, completion + typing ergonomics. §9.1 = one-responsibility-one-owner. |
| `design/etap1-tokenization-audit.md` | Historical tokenization audit. On demand. |

### Debugger

| Document | When |
|---|---|
| `design/firebird-debugger.md` | The behaviour authority. Fidelity Law §F, the `EXECUTE BLOCK` harness, frame savepoints, boundaries. |
| `design/firebird-debugger-implementation-plan.md` | **Every debugger session** — milestone briefs, danger zones, the 20-rule Developer Contract. |
| `design/d15-debugger-experience-and-ide-polish.md` | Any D15 milestone (D15.1–D15.5 delivered, D15.6 dropped). |

### UI, design system, terminology

| Document | When |
|---|---|
| `design/product-polish.md` | **Before any UI metric / token / density / typography work.** §0.1 principles outrank the catalog; §19 is the as-built. Large — read a subsection. |
| `design/color-language.md` | **Whenever you add an action or touch a colour.** §6 decision tree; ⛔ §0.5 is an overriding gate. |
| `design/terminology.md` | Before naming a UI action. A **norm**, enforced by `TerminologyTests`. |
| `design/keyboard-manager.md` | Before touching `App/Commands`, a shortcut, a gesture tooltip, or a context menu. |
| `design/hamburger-navigation.md` | Application menu, About, Keyboard Shortcuts, third-party notices. |
| `design/settings-center.md` | Before touching `Core/Settings`, the theme, formatter casing, or settings export. |
| `design/find-replace-panel.md` | 📋 Ratified-not-started: our own Find/Replace panel over AvaloniaEdit's search engine. Holds the measurements (why no cheaper seam exists) and the 3 unknowns to measure first. |
| `design/localization.md` | **Before any localization work.** Ratified D‑1/D‑2/D‑3; §2.1 records why the indexer binding is dead. |
| `design/avalonia-12.1.1-update.md` | Before changing an Avalonia package version. Records the two deliberate version mismatches. |
| `avalonia-headless-session-race.md` | When a full run loses exactly one headless test. The upstream race, its deterministic reproduction, and the five repairs already measured and rejected. |

### Modules

| Document | When |
|---|---|
| `design/data-import.md` | 🔒 Frozen architecture. Before touching import — §0 + the relevant section. |
| `design/data-import-i0-findings.md` | When an I0-derived decision needs its measured proof. |
| `design/sql-data-export.md` | Copy as INSERT / UPDATE. |
| `design/execution-modes-and-export-framework.md` | Execution modes + the shared export framework. |
| `design/script-executor-and-smart-parameters.md` | Script Executor + Smart SQL Parameters. |
| `design/script-executor-transaction-review.md` | Before changing Script Executor transaction handling. §5/§6 = the `Sequenced` design. |
| `design/metadata-refresh-analysis.md` | Before touching the metadata tree. §7 is the as-built; Layers 2/3 + startup stay open. |

### Audits & measurement archives

| Document | When |
|---|---|
| `audits/embertern-full-audit-2026-07-26.md` | ⚠ **Never alone** — read the verdicts in `history/22` alongside it; several findings did not survive verification. |
| `design/localization-readiness-audit.md` | The pre-stage inventory. ⚠ Its §6 ("restart is enough") is **superseded** by ratified D‑1. |
| `design/product-polish-m4-density-decision.md` · `-m4-typography-decision.md` · `-m5-focus-decision.md` · `-m5-severity-contrast-decision.md` · `-m5-dpi-checklist.md` | 🔒 Ratified decision records. Read for the method as much as the answer — each documents written premises that measurement refuted. |

### History

| Document | When |
|---|---|
| `docs/history/*.md` (31 files) | On demand, for the "why" behind a feature or bug. Index: `history/README.md`. |
| `history/30-claude-md-current-state-archive.md` | The pre-2026-08-11 status diary, verbatim (78 entries, 2026-07-12 → 2026-08-10). |
| `history/handovers/` | 🔒 Closed stage-handover and "next session" prompts (Product Polish M2a–M5, Localization App/Core). ⛔ Do not plan from them — several of their premises were refuted by measurement. |
| `memory/*.md` (outside the repo) | Claude's cross-session recall. `MEMORY.md` is the always-loaded index; individual files load on demand. |

### ⛔ The rule that keeps this working

**If you are about to append a multi-paragraph "shipped" narrative to `CLAUDE.md`, stop.** Put the
narrative in `docs/history/`, the rule in the relevant section of this file, and the status in one
line of `docs/current-state.md`. This file has had to be rescued from that exact habit twice.

## Build, test, run

```powershell
# from project root
dotnet build EmberTern.slnx
dotnet test  EmberTern.slnx
# run the app
src\EmberTern.App\bin\Debug\net9.0\EmberTern.exe
```

Solution file is `.slnx` (not `.sln`). App AssemblyName is `EmberTern`, so avares URIs use
`avares://EmberTern/...`. `Directory.Build.props` sets **`net9.0`**, `Nullable=enable` and
`TreatWarningsAsErrors=true` for every project, and is the **single source of product identity**
(version, release date, company, copyright — ⛔ never write a version number in code).
⚠ `TreatWarningsAsErrors` also escalates NuGet's `NU1902`/`NU1903`, so a **direct**
`PackageReference` with a known advisory **fails the build** — a de-facto SCA gate for direct
dependencies (transitive ones only surface in an explicit scan; gotcha #278).

⛔ **Never chain build and test** (`dotnet build && dotnet test`) — they deadlock and the run has to
be interrupted. Two separate calls.

⚠⚠ **Before asking the user to verify a UI change, build BOTH configurations.** The run command above
uses `bin\Debug\`, so an etap built only in `-c Release` leaves them looking at a binary that predates
the feature — measured: three UI defects reported that did not exist, one review cycle lost.

### Running the suite (read this before running tests)

⭐ **The suite runs as ONE command.** The old three-partition split is gone (2026-08-14): its real
purpose was to keep the global-`Loc` race apart, and that race is fixed at the source — headless tests
now get a clean subscriber list automatically (`IsolatesGlobalLanguageState` on `HeadlessCollection`).
⛔ Do not reintroduce partitions; they hid two defects for months.

Headless-Avalonia tests still share **one** `HeadlessUnitTestSession` per process (#94/#226/#286) — any
new headless test joins `HeadlessCollection`, never its own `IClassFixture`.

⚠ **The acceptance criterion is the TOTAL, not "0 failures".** Measured: a broken headless state
reported *"0 niepowodzeń, łącznie 7232"* while **128 tests silently never started**. A run is green
only when the total matches. Current expected total is in `docs/current-state.md`.

#### ⚠⚠ The ONE known false failure — recognise it before blaming your change

Roughly **1 full run in 3–8** loses **exactly one** test to an Avalonia infrastructure race — **not** a
product regression and **not** yours. ⭐ **The STACK identifies it**, never the test name: the victim is
whichever headless test dispatched first, so it is usually the *same* name run after run. Look for **one**
failed test, everything else green, and:

```text
System.InvalidOperationException : The calling thread cannot access this object because a different thread owns it.
   at Avalonia.Rendering.DefaultRenderLoop.Add(IRenderLoopTask i)          ← Dispatcher.VerifyAccess()
   at Avalonia.Headless.AvaloniaHeadlessPlatform.Initialize(...)
   at Avalonia.Headless.HeadlessUnitTestSession.EnsureIsolatedApplication()
```

**Re-run once.** A run that goes green with no change confirms it. ⛔ A failure WITHOUT that stack is a real
defect, however familiar the test name looks.

⛔ **Do not try to fix it again — five approaches were measured and rejected**, including the two that
look obvious (a warm-up dispatch, and serialising every Avalonia-touching test). Cause, evidence, the
rejected list and the ready-to-file upstream report: [`docs/avalonia-headless-session-race.md`](docs/avalonia-headless-session-race.md).

## Git remotes & push workflow (user directive)

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

⛔ **Never change the remote configuration without the user's explicit decision** — not the URLs, not
a `pushurl`, not a rename. A dual-`pushurl` "one push reaches both" variant was considered and
**rejected on purpose**: when GitHub is unreachable it makes *every* push report failure, including
the company one, so a company push would depend on GitHub's availability. Two explicit pushes keep
failures isolated.

### Branch hygiene

**Start from `master`.** As of 2026-08-11 both long-running feature branches (`feat/product-polish`,
`feat/localization`) are **merged into `master`**; they are kept on the remotes by earlier
instruction, but `master` is the superset. ⚠ Older notes saying "start from `feat/product-polish`"
are stale — that was a decision about *timing*, and its moment passed.

- Work cut from `master`: branch, merge back `--no-ff`, delete the branch.
- ⭐ **A short branch cut from a FEATURE branch merges back into THAT branch, never into `master`.**
  This is not style — the stabilization sprint measured it: merging such a branch to `master` would
  have carried **104 commits, only 8 of them the sprint's**, i.e. it would have merged the whole
  feature branch as a side effect. Separating it was measured *impossible*, not merely risky (12 of
  its files did not exist on `master`).
- Retire a branch with `git branch -d` (the safe variant that refuses unmerged work), and only after
  verifying it is merged.
- Push **after** acceptance, so a short technical branch can be retired locally with nothing to clean
  up remotely.

### ✅ SSH — git uses the system OpenSSH (durable fix, applied 2026-07-26)

```bash
git config --global core.sshCommand "C:/Windows/System32/OpenSSH/ssh.exe"
```

`GIT_SSH_COMMAND` is **no longer needed** — a plain `git push private <branch>` is the workflow.

**Why the setting exists** (do not undo it): there are **two independent SSH agents** on this machine.
The key lives in the **Windows OpenSSH agent service**, which PowerShell/CMD's `ssh` sees; **Git for
Windows bundles its own `ssh`**, which reads `SSH_AUTH_SOCK` and does **not** see that agent.

⚠⚠ **A green `ssh -T git@github.com` proves nothing about `git push private`** — measured: the same
host answered *"Hi tamvel!"* in PowerShell while git's bundled ssh gave `Permission denied
(publickey)`. If pushes to `private` start failing that way, check
`git config --global --get core.sshCommand` **first**.

**After a reboot** the agent forgets the passphrase-protected key. Unlock it once per system session
**from PowerShell or CMD** (not Git Bash — that targets the *other* agent):

```bash
ssh-add ~/.ssh/id_ed25519
```

## Laboratory Database

A single, persistent Firebird lab database for hand-verifying EmberTern behaviour against a real engine — **use it instead of guessing how Firebird behaves.**

> The Laboratory Database is a long-lived development asset. Future sprints should extend the existing EmberTern_Lab database with new object types and edge cases instead of creating temporary one-off databases.

- **Location**: `Lab/EmberTern_Lab.fdb` — committed to Git (intentionally, for now; if it grows significantly we revisit and may switch to a `setup.sql`-only model). Canonical recreate script: `Lab/setup.sql`.
- **Reference engine**: the local **Firebird 5.0** `DefaultInstance` on **localhost:3050** (FB3 is on 4050 and is **not** used for the lab — we keep ONE lab DB, not an FB3/FB5 matrix). `isql.exe` lives at `C:\Program Files\Firebird\Firebird_5_0\isql.exe`. SYSDBA password is the local dev password.
- **DB settings**: dialect 3, **default charset WIN1250** (matches the user's real environment). Identifiers in `setup.sql` are ASCII so the script runs under any client charset.
- **Purpose**: a development aid, not a test framework and not a compatibility matrix. It carries a small, representative object zoo: 8 domains, 5 tables, 3 views, 2 standalone procedures + 2 standalone functions, 3 triggers, 3 generators, 3 exceptions, 3 roles, 1 package (with body, containing 1 function + 1 procedure), plus a little sample data, plus the debugger zoo and — since the 2026-08-05 stabilization sprint — `SP_DOM_PARAMS` / `FN_DOM_ARG` (domain-typed parameters, arguments and RETURNS) and `SP_DBG_SELINTO` (a singleton `SELECT … INTO`, the state no fidelity case reproduced). Covers PK / FK / composite PK / unique / computed columns / identity (BY DEFAULT and ALWAYS) / domain-typed columns / SUSPEND / CASE / nested BEGIN-END / before-insert / before-update / after-update / COMMENT ON, etc.
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
Directory.Build.props        # net9.0, Nullable=enable, TreatWarningsAsErrors=true
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
  EmberTern.App/             # WinExe, Avalonia 12.1.1, CommunityToolkit.Mvvm 8.4.2
    Program.cs, App.axaml(.cs), UiStrings.cs, app.manifest
    ViewModels/ Views/ Themes/ (Colors.axaml + ControlStyles.axaml — the ONLY theme sources)
    Behaviors/ Completion/ Controls/ Converters/ Diagnostics/ Export/ Security/ Sql/
    Assets/ (FirebirdSql.xshd + .Light.xshd, Branding/, Icons/ — SvgIcon geometries)
    (NuGet: Avalonia.AvaloniaEdit 12.0.0 — ⚠ deliberately BEHIND the core, no 12.1 build exists;
     Avalonia.Controls.DataGrid 12.1.2 — ⚠ deliberately AHEAD, no 12.1.1 build exists. Both mismatches carry
     their reason at the `PackageReference`; see `docs/design/avalonia-12.1.1-update.md` + gotcha #321)
tests/
  EmberTern.Tests/           # xunit; ONE shared HeadlessUnitTestSession for the whole
                             # ConnectionExpandBindingProbe class — see gotchas #94 / #226
```


## What's built — feature inventory

A map of what exists, not how it got built (that's `docs/history/`). Everything below is shipped and
working. Where a module carries a **rule** that binds future work, the rule is stated here; the
reasoning lives in the referenced document.

- **Connections & sidebar** — DPAPI-encrypted profiles, folders, drag & drop reorder, a **flat-list
  (non-`TreeView`)** Object Explorer with lazy count-only load, type-to-filter, per-category context
  menus. *(history: 01, 08, 09)*
- **SQL Editor** — F5/Ctrl+Enter execute (Preview/Full, streaming, 1 M-row ceiling), manual
  transactions, Saved Queries, Execution Metrics, AST-based formatter, Find/Replace. *(01, 02, 10)*
- **Metadata browsing + DDL preview/export** — reconstructed DDL for any object; `.sql` export is
  UTF-8 **no BOM**. *(01, 12)*
- **Ten per-object editors** (Table, View, Procedure, Function, Trigger, Package, Domain, Generator,
  Exception, Index) — one shared editor contract, buffered structural edits for Table, Source⇄Easy
  mode for the routine editors, Revert/Discard with confirmation on every one. *(03, 04, 07, 09)*
- **DDL change safety** — a compile cannot overwrite a definition EmberTern cannot prove is the one
  the editor loaded, and a New-object compile cannot overwrite an existing object of the same name.
  One Core verdict (`ObjectChangeSafety`) behind one App gate (`ObjectChangeGate`). ⛔ **No
  force-overwrite** — the SQL Editor is the deliberate escape hatch. *(22)*
- **Transactions & three connection lanes** — **Data** carries everything the user runs by hand and
  holds **THE one user working transaction** (auto-begin, **never** auto-commit, NOWAIT);
  **Metadata** is read-only catalog browsing owning no transaction; **Ddl** carries object-editor
  Compile only — autonomous, auto-committed, **WAIT**-bounded, with the per-connection Developer Mode
  toggle. ⛔ The SQL Editor is a classic Firebird console: no routing by statement kind, no hidden
  second transaction. *(05, 13, 15)*
- **Settings & security** — every user setting in one whole-file DPAPI-encrypted `settings.dat`, with
  a versioned header, forward-compatible migration, and a `Save` that **refuses** over a file it
  cannot read. *(05, 22)*
- **Data grids** — shared filter panel, aggregation bar and Record-N-of-M across all five
  data-bearing grids; client-side for materialized grids, SQL push-down for server-paged ones.
  ⛔ **A new data grid gets the full context-menu set or fails `DataGridCopyMenuTests`** — one text
  builder (`App/ViewModels/GridCopyText.cs`), one clipboard writer (`App/Views/GridClipboard.cs`);
  ⚠ each host supplies its OWN row list ("all" means *the rows this grid is showing*). *(10, 12, 25)*
- **SQL Data Export — Copy as INSERT / UPDATE** — provably-correct DML de-aliased via the server's own
  provenance; UPDATE only on a catalog-verified **complete** PK; refuses with a reason where proof is
  unavailable. *(`design/sql-data-export.md`)*
- **Shared `MessageBanner`** — the IDE's **ONE** message surface (Info/Success/Warning/Error), live on
  23 surfaces. ⛔ Use it for any new message on a work surface — never a locally-styled coloured
  `TextBlock`, and never a local `Background`/`BorderBrush`/`BorderThickness` on the banner (a local
  value outranks the shared style and re-opens per-host divergence). A host sets only `Severity`,
  `Message`, `IsVisible`, optional `Classes="docked"` and layout margins.
- **Global Search** — metadata by name and by source content (server-side `CONTAINING`), 2-panel
  results with live DDL preview. *(12)*
- **Script Executor** — multi-statement scripts under a caller-controlled transaction; the
  **`Sequenced`** ("deployment") mode commits after each schema statement, which is what makes a mixed
  DDL+DML migration runnable. *(15, `design/script-executor-transaction-review.md`)*
- **Recompile Dependents · Smart SQL Parameters** — typed parameter collection for `:name`/`@name`
  placeholders, driven by the AST's `IRoutineInvocation`. ⛔ A call the parser cannot find is a call
  the parser does not model — do not add statement-kind branches to the walk. *(12, 23)*
- **Security Manager** — Users / Roles / Membership / object & column privileges, immediate-apply,
  contextual one-click grant/revoke. *(09)*
- **Database Activity Monitor (Trace)** — live grouped filterable view via the Services Trace API,
  with a bridge into Performance Analysis. *(11)*
- **Session Manager** — live `MON$` view of attachments and transactions, two health detectors,
  Disconnect. *(11)*
- **Database Properties** — read on the Metadata lane, written through the Services API. ⭐ Only
  **Sweep interval · Forced writes · Reserve space** are editable — exactly those measured writable
  online without exclusivity. ⛔ Opening the window and Apply send **nothing** unless changed (every
  field is nullable, `null` = don't touch) — the API has no rollback. *(27 §10–§11)*
- **Performance Analysis** — execution plan + measured per-table reads with a measured-first advisor
  (6 rules) producing confidence-scored findings and guidance — **never automatic fixes**. *(10)*
- **SQL/PSQL language front-end** — one shared Lexer → Parser → AST → Semantic Model in
  `EmberTern.Core.Sql.Language`; completion, signature help, snippets, navigation, semantic
  highlighting, Quick Info, diagnostics and Quick Fixes are all **clients** of that one model.
  *(`design/editor-architecture.md`)*
- **Firebird Debugger** — a client-side PSQL interpreter (Firebird has **no** debug API): control flow
  from the AST, every statement executed as an anonymous `EXECUTE BLOCK` harness that never names the
  routine, so all semantics come from the server. Procedures, functions, triggers, packages and
  **local** routines with real closures are steppable frames; breakpoints (conditional, hit-count,
  data), call stack, watches, Immediate, inline values. *(`design/firebird-debugger.md`, history 19)*
- **Data Import** — Clipboard / TXT / CSV / XLS / XLSX into an existing or new table. One working
  surface with collapsible sections (deliberately **NOT** a wizard), one pipeline for every source,
  `ImportConfiguration` as the single representation of every user decision, the module's own
  transaction on its own attachment. *(`design/data-import.md`, history 21)*
- **Application Menu (☰) · About · Keyboard Shortcuts · third-party notices** — the app's home for
  application-level functions. ⭐ **Version and identity have ONE source: the `PropertyGroup` in
  `Directory.Build.props`**; `AppInfo` reads it back off the assembly. ⛔ **Never write a version
  number in code** — two `AppInfoTests` guards enforce it. ⭐ The **OS icon every window carries comes
  from ONE `<Style Selector="Window">` setter**; ⛔ do not set `Icon` per window.
  `THIRD-PARTY-NOTICES.txt` is a licence obligation, not a courtesy. *(`design/hamburger-navigation.md`)*
- **Settings Center** — the one home for preferences: a **window** (never a workspace tab),
  apply-on-change with no OK/Cancel. ⭐ Every option a control offers is generated from Core's
  `PreferenceOptions` (a second list in XAML drifts silently); ⭐ ONE `PreferencesService` owns the live
  `Preferences` (the store persists the whole object, so two snapshot holders overwrite each other);
  ⭐ `App` is the ONE place a theme is applied. ⚠ Numeric fields commit on **blur or Enter**, never per
  keystroke, and out-of-range **clamps rather than resets**. *(`design/settings-center.md`)*
- **Settings export / import (`.etsettings`)** — EmberTern's own versioned, **always-encrypted**
  artifact (AES-256-GCM under a PBKDF2 passphrase key, cleartext header). An import **merges** into
  `settings.dat` non-destructively and keeps a `settings.dat.pre-import-<stamp>` **copy**. ⚠ Passwords
  are opt-in; `ClientLibraryPath`, `WindowBounds`, `ParameterHistory`, `DebugWatches` never travel, and
  a reflection guard fails the build when a new field has no recorded decision.
  *(`design/settings-center.md` §15–§16)*
- **Keyboard Manager / command system** — **ONE registry every UI surface reads from**
  (`EmberTern.App/Commands`): `CommandCatalog` is a single declarative table with a collision
  validator, `CommandRouter` resolves **Editor > Tree > Grid > Tab > Global**, `CommandTip` is the one
  place a gesture becomes text. ⛔ **No gesture is typed by hand anywhere** — shortcuts, tooltips,
  chips and all 32 context menus take it from the registry, and two tests enforce that.
  *(`design/keyboard-manager.md`)*
- **Localization** — `.resx` + `ResourceManager`, English as the neutral set, **Polish complete**.
  Language changes **live**, with no restart. ⛔ A localized member must never be a `const` (inlined)
  nor `static readonly` (frozen in the first language) — it is a **property**; in XAML the form is
  `{app:Loc Key}`, never `{x:Static}`. Core/Firebird hand up a `MessageKey` + arguments and App
  resolves the words. *(`design/localization.md`, history 28)*

## Current state

⭐ **Status lives in [`docs/current-state.md`](docs/current-state.md) — read that, not this file.**
It is the ONE place that answers *"what is done, what is open, what are we working on"*, and it is
kept between 100 and 300 lines on purpose.

At a glance, verified 2026-08-14: branch **`fix/audit-followup-2026-08`** (cut from `master`, **not
pushed**), HEAD `1852611`, build **0/0 in both Release and Debug**, tests **8 813**, last series **6/6
fully green**, version **0.5.0**. ⏭ **Milestone: audit follow-up, Phase 4 accepted. Next: Phase 5 —
charset guard, NOT started.** Read `docs/current-state.md` §0 + §3 first.

⚠⚠ **Do not restore a status diary here.** This section was **5 956 lines (83 % of CLAUDE.md)** until
2026-08-11, archived verbatim in
[`history/30-claude-md-current-state-archive.md`](docs/history/30-claude-md-current-state-archive.md).
It has regrown twice, always the same harmless-looking way — see § "The tripwire" for the mechanism and
the checklist.

## Editor Architecture — current direction

**Authority: [`docs/design/editor-architecture.md`](docs/design/editor-architecture.md) — read it
before touching anything under `EmberTern.Core.Sql.Language` or downstream of it.** Stage-by-stage
status is in `docs/current-state.md`; the narrative is in `docs/history/14`, `16`–`20`.

The editor runs on **one shared, error-tolerant language front-end** — Lexer → Parser → AST →
Semantic Model, all in `EmberTern.Core.Sql.Language`, pure and zero-Avalonia — and **every** feature
is a *client* of that one model: formatter, completion, signature help, snippets, navigation,
diagnostics, quick fixes, semantic highlighting, Quick Info. This replaced 7 independent ad-hoc SQL
scanners and 3 divergent keyword lists.

**The three properties that make it safe, and that new work must not break:**

1. ⭐ **§0 / rule #11 round-trip.** The AST retains the token stream, so any statement round-trips
   byte-for-byte regardless of parsing depth — an under-modelled construct (`RawStatement`) can never
   lose text. The formatter additionally enforces a **checked lexeme-preservation invariant**: if a
   formatted statement's lexeme sequence differs from its input, it keeps the statement verbatim.
   ⚠ The failure mode this creates is worth knowing: a formatter that *drops* a lexeme does not look
   like a formatting bug — the net reverts the statement and the feature merely appears to do nothing,
   while every lexeme-preservation test stays green. The assertion that can fail is *"the net did NOT
   fire"* (gotcha #359).
2. ⭐ **One owner per question.** `CompletionEngine` answers *"what is legal at this caret"*;
   `CompletionMatcher` answers *"which of those match what is typed"*. IntelliSense owns **names**,
   Language Completion owns **constructs**, Typing Ergonomics owns **mechanics** — and the split is by
   vocabulary **and** grammatical position, both derived from the owner's own catalog, never
   hard-coded (`design/editor-language-expansion.md` §9.1).
3. ⭐ **The parser is the single structural source.** Binder and formatter both *consume* the AST;
   neither re-walks tokens for structure. Ordinary expressions stay token fragments by design — that
   is the "structural depth" boundary, not debt.

**Attachment.** `SqlEditorBehavior.Attach` is the ONE seam for the editor-intrinsic block
(completion · highlighting · navigation · squiggles · related elements · language completion · typing
ergonomics · search). Read-only surfaces use `AttachReadOnlyHighlighting` / `AttachDdlPreview`
instead — ⛔ a read-only surface must never be given mutating quick fixes.

**Deliberately unbuilt** (consume the same AST, need no further foundation): **Folding** and
**Breadcrumbs**.

## Architecture rules — enforce against drift

From the master prompt — non-negotiable, still in force today (rule 10 corrected during the
2026-07-11 cleanup: it originally read "no workspace persistence in V1", which V1.1 shipped
long ago — the surviving, still-true boundary is kept below):

1. **Core has zero Avalonia dependencies.** ViewModels in App contain no Avalonia types (no `IImage`, `Color`, `Thickness`). Theme toggle lives in code-behind on purpose — single button, no value routing through VM.
2. **No interfaces without two concrete implementations.** Every service so far (`ConnectionService`, `QueryExecutor`, `TransactionService`, `ConnectionProfileStore`) is a direct class. No `IDbProvider` layer.
3. **No autocommit. Ever.** Auto-*begin* exists (matches IBExpert workflow); auto-*commit* doesn't. There's no toggle, no setting.
4. **Virtualized grid is mandatory.** Avalonia DataGrid handles this — don't replace it with a plain `ItemsControl`.
5. **No `Utils/` or `Helpers/` folders.** If something has no clear home, the structure is wrong.
6. **`UiStrings` is the ONE way code reaches a user-visible string** — never a literal in a view or a view model, never a `ResourceManager` read of your own. ⚠⚠ **AMENDED by the Localization stage (decision D‑2), and the amendment is only about the NOSNIK:** the "no `.resx`" half of this rule is **deliberately lifted** — the words now live in `Localization/Strings.resx` (English = the base/neutral set, satellites per language) and `UiStrings` members are thin **properties** reading them through `Loc`. ⛔ A localized member must never be a `const` (the compiler inlines it, so nothing is left to resolve) nor a `static readonly` (resolved once, then frozen in the first language). ⛔ In XAML the form is `{app:Loc Key}`, never `{x:Static app:UiStrings.Key}` — `x:Static` is not a binding and never re-evaluates, so with it the language could not change without a restart. Read [docs/design/localization.md](docs/design/localization.md) before touching any of this.
7. **No event bus / IMessenger** until 3+ components need to communicate. Currently events on services (`ActiveConnectionChanged`, `TransactionStateChanged`) wire VM directly — that's fine.
8. **Async only where the user waits**: query execution + connection. Not async everywhere.
9. **Dark + Light from day one.** Every new color → both dictionaries in `Themes/Colors.axaml`. Zero hardcoded colors in views — only `{DynamicResource}`.
10. **No plugin system, no debugger, no schema compare, no docking.** (Workspace persistence — the one item on this list that was originally "V1-only" — shipped in V1.1 and is now core; see "What's built" above. AI is separately addressed by the editor-architecture decision "kept AI-ready, nothing designed solely for AI".) The UI mockup shows aspirations; build only what's actually planned, not the whole vision at once.
11. **Never lose information / never corrupt user code or metadata (Critical / Data-Loss class — the project's #1 rule, above every feature).** Any feature that generates DDL or modifies user code or DB objects — formatter, recompile, refactor, Quick Fix, Rename, snippet expansion, future AI — MUST preserve every fragment it does not fully understand, **verbatim 1:1**. **If EmberTern is not 100% certain it can reproduce an object identically, it MUST NOT modify it automatically** (uncertainty ⇒ do nothing or ask). Correctness of generated code outranks aesthetics. Origin: a group procedure recompile once stripped input-parameter defaults and broke system mechanisms (gotcha #175) — that class of bug is unacceptable. In the editor front-end this is realized by an error-tolerant parser + `RawStatement` verbatim round-trip. See the "Editor Architecture — current direction" section above + [docs/design/editor-architecture.md](docs/design/editor-architecture.md) §0.

12. ⭐⭐ **EVERY new user-visible text goes through the localization mechanism, in EVERY supported language — no exceptions, and "it's just an error message" is not one.** A label, a button, a tooltip, a dialog, a status line, a validation message, a warning, an **exception the user will read** — if a human sees it, it is a `MessageKey`/`UiStrings` member with an entry in **`Strings.resx` AND `Strings.pl.resx`**, and it is resolved with `Loc` **at the moment of display**. ⛔ **Never hardcode a user-facing sentence**, and ⛔ never ship half a sentence from the catalog with a fragment concatenated in code — the whole sentence must come from the catalog, because word order is the translator's decision, not English's. Dynamic values (a name, a count, a character, a server message) travel as **arguments**, never baked into the text; a raw Firebird message stays the server's own words as an argument (D‑3). ⚠⚠ **The failure mode is silent and has bitten this project: it is not a missing entry, it is a PERFECT entry that nothing reads.** Phase 5 shipped correct Polish and English for the charset guard and still showed a Polish user a fully English paragraph, because the exception was *wrapped* on its way out and the display site read `ex.Message` — green build, 8 844 green tests, translated resource nobody resolved. So a new message is finished only when it has been **seen rendered in Polish**, or pinned by a test that resolves it through the path the UI actually uses (`ErrorText` for exceptions; see `CharsetGuardLocalizationTests`). Rule 6 says where the words live; this says that no user-visible text may live anywhere else. Read [docs/design/localization.md](docs/design/localization.md).

## UI styling rules — theme discipline (enforce on every new window / dialog / control)

The app has **one** central theming system. Every new window, dialog, UserControl, DataTemplate, and control MUST go through it — no exceptions. These rules exist because new UI kept introducing local colors and FluentTheme's `SystemAccentColor`-derived highlights (the brown/orange selection rectangles), which clash with the workbench palette.

**The central system — six files, each with ONE job; nothing else holds a colour or a metric:**
- [`Themes/Colors.axaml`](src/EmberTern.App/Themes/Colors.axaml) — the **single source of every color**. `ThemeDictionaries` with a `Dark` and a `Light` dictionary, each defining the same set of `Color` keys then `SolidColorBrush` keys over them. This is the token catalog.
- [`Themes/Tokens.axaml`](src/EmberTern.App/Themes/Tokens.axaml) *(M2a)* — the **non-colour catalog**: spacing, `Thickness`/`CornerRadius` roles, control heights, icon sizes, radii, border widths. **No `ThemeDictionaries`** — a metric does not depend on the theme.
- [`Themes/Typography.axaml`](src/EmberTern.App/Themes/Typography.axaml) *(M2a)* — the **12 typography roles** (size · weight · line-height) + `Font.Ui` / `Font.Code`.
- [`Themes/FluentBridge.axaml`](src/EmberTern.App/Themes/FluentBridge.axaml) *(M2b)* — ⭐ **the mapping layer that repins FluentTheme's own named resources onto our tokens**, so we keep the framework's behaviour without copying its templates. ⛔ **Mapping only — never a second token catalog** (rule 8 below).
- [`Themes/DataGridStyles.axaml`](src/EmberTern.App/Themes/DataGridStyles.axaml) *(2026-08-18)* — **the DataGrid standard**: row height, header height, cell padding, zebra striping, selection-over-zebra, hover, header chrome, cell focus ring. ⭐ **Split out of `ControlStyles.axaml` so it can be LINKED into `EmberTern.LicenseManager`** — that file cannot be (it binds to `EmberTern.App.Controls`, AvaloniaEdit and `avares://EmberTern/`), and one grid appearance for two applications beats a copy that drifts. ⛔ Nothing type-bound may be added here, and the EDITING styles stay in `ControlStyles.axaml`; `DataGridStylesSplitTests` fails the build otherwise. ⚠ Must be included AFTER `Avalonia.Controls.DataGrid/Themes/Fluent.xaml` in both applications.
- [`Themes/ControlStyles.axaml`](src/EmberTern.App/Themes/ControlStyles.axaml) — the **single home for shared/reusable styles** (`Button.icon`, `Button.primary`, `Button.flat`, `Button.caption`, `TabItem.bottom-tab`, `TabItem.sub-tab`, `TextBlock.field-label`, `DataGridRow`/`DataGridCell`/`DataGridColumnHeader`, `ListBoxItem`/`TreeViewItem` state overrides, etc.). Loaded app-wide via `Application.Styles`, so these styles apply inside dialog windows and UserControls too. **Also the home of every control METRIC setter** (rule 8).

⚠ [`Themes/ControlThemes.axaml`](src/EmberTern.App/Themes/ControlThemes.axaml) holds the **two** hand-written `ControlTemplate`s (`CheckBox`, `RadioButton`) — structure, not style. Adding a third requires the two measured conditions in `product-polish.md` §16.4.

**Hard rules:**
1. **No hardcoded colors. Anywhere.** No hex literals (`#RRGGBB`, `#AARRGGBB`), no named colors (`White`, `Black`, `Red`, …) on any `Background` / `Foreground` / `BorderBrush` / `Fill` / `Stroke` / `Color` / `Value` in views, code-behind, or styles. The only literal allowed is `Transparent` (it's "no fill", not a theme color — used for hit-target borders and reset states). If you need a color, it is a **theme token** in `Colors.axaml` or it does not exist.
2. **No local color definitions.** No `<SolidColorBrush>` / `<Color>` declared in a view's `.Resources`, no `new SolidColorBrush(...)` / `Color.Parse(...)` / `Brushes.X` / `Colors.X` in code-behind. Per-kind icon colors flow through `IconResourceKey` + `IconBrushConverter` (the VM holds the **key string**, never a brush — keeps "no Avalonia types in VMs" intact).
3. **Every color comes from both Light and Dark.** Add a new token → add it to **both** `ThemeDictionaries` (same key in `Dark` and `Light`). A token that exists in only one dictionary is a bug. Tokens that are intentionally theme-independent (e.g. `OnAccentColor` white text over the colored accent/chips, `CloseButtonHoverColor` the Windows caption red) are still defined in **both** dictionaries with the same value, with a comment saying why.
4. **Consume tokens via `{DynamicResource}`**, never `{StaticResource}`, for any brush a control paints with — `DynamicResource` re-resolves on theme toggle so the control recolors live. (`StaticResource` inside `Colors.axaml` to chain a `Color` into a `SolidColorBrush` is fine — that's a definition, not a consumption.)
5. **Reusable styles live in `ControlStyles.axaml`, not in views.** If a style (label, button variant, tab variant, grid styling) is or will be used in more than one place, it goes in the central file and views reference it by `Classes="..."`. A view's local `<X.Styles>` block is allowed **only** for genuinely view-specific, non-duplicated structure — and even then it must use theme tokens (e.g. row-height/padding sizing, opacity-only hover affordances, a `Classes.active-tab` background swap that reads `BackgroundBrush`). When in doubt, centralize.
6. **Never override FluentTheme state colors with hardcoded values.** FluentTheme paints selection/hover/focus from `SystemAccentColor` (brown/orange on a default Win11 install). The fix is already centralized: the `TreeViewItem*` / `DataGridCell*` / `ListBoxItem` resource-key overrides in `Colors.axaml` + the Style selectors in `ControlStyles.axaml`. New selection-driven controls inherit these automatically — do not re-solve it locally with a hex color.
7. **New windows/dialogs must set `Background="{DynamicResource BackgroundBrush}"` + `Foreground="{DynamicResource ForegroundBrush}"`** on the root so they theme correctly (FluentTheme's window default isn't our palette). Use `Classes="field-label"` for form captions, `Classes="h1"` for dialog headers, the `Button.primary` / `Button.flat` / `Button.icon` variants for buttons — don't restyle from scratch.
8. ⭐ **Restyling a base control: repin Fluent, don't rewrite it — and metrics, colours and aliases take THREE SEPARATE ROUTES.** Full pattern + the per-control procedure: [`product-polish.md` §16](docs/design/product-polish.md). In short: **metrics** (`MinHeight`, `Padding`, `FontSize`, `BorderThickness`) → a **style setter** in `ControlStyles.axaml` reading a token; **colours painted by template internals** (`PART_BorderElement` and friends) → **`FluentBridge.axaml`**; **a value the template holds as a LOCAL value on the element** (measured: `ExpanderMinHeight`, `ScrollBarThumbBackgroundColor`) → a **resource alias** `<StaticResource x:Key="…" ResourceKey="…" />`, because a setter cannot beat a local value. ⛔ **No local values in the Bridge** (`FluentBridge_ContainsNoLocalValues` fails the build otherwise), and ⛔ **no new `ControlTemplate`** unless §16.4's two conditions are both measured true.
9. ⭐ **Never write a number a token already names.** Spacing, control height, font size, radius, border width and icon size all have roles in `Tokens.axaml` / `Typography.axaml`; consume them with `{DynamicResource}` (architectural — §3.4 wants a future font/scale preference to swap the base tokens live). ⚠ `{DynamicResource}` **does not throw on a missing key** — the property silently keeps its default, so a typo is invisible at build time; that is what `DesignTokenApplicationTests` exists to catch.
10. ⭐⭐ **A control's size comes from its CONTEXT, never from its variant** (M2b §17.2). A chrome strip (`Border.chrome`), a `DataGridCell`, an `Expander` header and a dialog footer each declare what their children may be; the variant (`.primary`) carries **colour**. ⛔ **Never put `MinHeight`/`MinWidth` on the base `Button` style** — that exact setter silently grew the metadata tree's expander arrow from 20 to 100 px, because Avalonia clamps `Width` by `MinWidth`.
11. ⭐ **Write the rule POSITIVELY.** *"Everything is X unless…"* leaks: in M2b it leaked twice, the second time as a layout regression. State what a thing IS (a class, a container), never what it is not.

**Token cheat-sheet** (semantic name → use): `BackgroundBrush` (window/editor), `PanelBrush` (sidebar/header panels), `ChromeStrongBrush` (titlebar/column headers — chrome one step further from the document), `SurfaceRaisedBrush` (⭐ anything that FLOATS above its container: popups, menus, tooltips, dropdown lists, the selected tab — **not** the same job as chrome, and in Light the two are opposites), `BorderBrush` (structural separators, gridlines, the rest rail), ⭐ `ControlOutlineBrush` (**the visible
outline of an interactive control at rest** — a different role from `BorderBrush`, and that distinction is
what makes an unchecked `CheckBox` findable: sharing one token measured 1.35:1 in Light. Consumers: CheckBox,
RadioButton), `ForegroundBrush` (default text), `SubtleForegroundBrush` (hints/captions), `AccentBrush`/`AccentMutedBrush` (primary action, focus accent), `OnAccentBrush`/`OnAccentSubtleBrush` (text on accent/colored chips), `SelectionBrush`, `HoverOverlayBrush`, `FocusBorderBrush`, `ErrorBrush`/`WarningBrush`/`ConnectedBrush`, `TransactionActiveBrush`, `CommitButtonBrush`/`RollbackButtonBrush`, `RowAlternateBrush` (zebra), `DropTargetBrush`, `CloseButtonHoverBrush`, `DataLaneChipBrush`/`MetadataLaneChipBrush`, `IconColor_*` (per metadata kind, via `IconBrushConverter`). If none fit, add a new token (both dictionaries) — don't reach for a literal.

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
- [ ] **no literal metric a token already names** — spacing, height, `FontSize`, radius, border width, icon size come from `Tokens.axaml` / `Typography.axaml` (rule 9);
- [ ] **a restyled base control follows §16** — metrics via a style setter, colours via `FluentBridge`, no new `ControlTemplate` without §16.4;
- [ ] **judged in the complete set of states** — normal · hover · active/checked · disabled · focus;
- [ ] renders correctly in **Light** theme;
- [ ] renders correctly in **Dark** theme;
- [ ] existing styles / components reused (no reinvented label / button / grid / pagination);
- [ ] no duplicated styles (shared ones live in `ControlStyles.axaml`);
- [ ] no duplicated functionality (reused the existing behavior/VM pattern instead of a parallel one).


## Live gotchas — load-bearing subset

The **complete** catalog, organized thematically, lives in **[`docs/gotchas.md`](docs/gotchas.md)**.
Below are the ~20 that are load-bearing across almost *any* session — the rest are searchable there
by keyword the moment a bug "feels familiar". Each line is a one-sentence summary; follow the `#N`
reference for the full explanation and the failure it prevents.

⛔ **The entry count is deliberately NOT written down here.** Three separate counters used to carry it
and all three disagreed while every one of them was wrong. **Measure it** (last check 2026-08-18:
**376 entry lines, max #387**):

```bash
grep -cE "^[0-9]+\. \*\*" docs/gotchas.md
```

⚠ The count is *not* max−1: numbers **303 and 304 are each used twice**, in different thematic
sections, so a bare "#303" is ambiguous.

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
- **`x:DataType` on a `DataTemplate` is also the MATCHING type**, so a stale one produces no binding
  error — the template stops matching and the host silently renders the item's `ToString()`, i.e. a
  type name on screen where the content belonged. ⚠ Guard a template by asserting the **realized**
  output (`template.Match(item)` / the text the tree renders), never by what the XAML spells; and
  when a comment cites a test by name, **grep for it** — a named guard that does not exist reads as
  coverage while providing none. *(#370)*

**Editor language front-end (the current, active work)**
- The AST round-trips the source byte-for-byte via the retained token stream — this is
  independent of parsing depth, so `RawStatement`/an under-modeled node never risks data loss.
  Any text-reproducing consumer migrated onto the parser must be gated behind a permanent
  differential test proving byte-identity against the previous implementation. *(#191, #192)*
- No transitional class names (`V2`, `NewX`, `Temp`, `Parser2`, …) are left in the codebase once
  a migration completes — consolidate to the plain responsibility name the moment the old
  implementation is deleted. *(#195)*
- **THE CONSTANT RULE.** An AST-driven clause emitter that rebuilds its keyword from a **constant**
  (`Kw("select")`, `Kw("from")`, `Kw("with")`, a set operator) never renders the tokens that constant
  replaces — so a comment carried as those tokens' leading trivia is rendered by nobody, §0's net
  reverts the statement, and the formatter silently **does nothing**. Hand the comments back at the
  position they held (`CommentsIn` / `SplitCommentsAt` / `TakeLeadingComments`); ⛔ never hoist them to
  the top. ⚠ The net compares the lexeme **sequence**, so a recovered comment on the wrong side of a
  token is as fatal as a dropped one. *(#369; the mirror of THE TAIL RULE in `SqlFormatter`)*
- **A false positive and a missing feature can be the same bug** — so fix it at the **resolution** step,
  never at the reporting step. A selectable procedure's columns are its **output parameters**; the binder
  asked `GetColumns` (empty for a procedure), which surfaced as a false `ET0002` *and* as completion
  offering nothing after `alias.`. ⛔ Do not key such a decision on the AST node (`RoutineTableReference`
  misses paren-less `FROM MY_NOARG_PROC`) — key it on the **resolved catalog target**; and give every new
  lazily-warmed fact a `Knows…` readiness answer, or S-2's "everything underlined for a moment" returns.
  *(#371)*
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
  *"the calling thread cannot access this object"* — no injection style avoids it. **It is shared through an
  `ICollectionFixture` (`HeadlessCollection`), NOT `IClassFixture`** — the latter creates one per test *class*,
  so a second consumer silently gets a second session; join the collection instead. *(#94, #226, #286)*
- ⭐⭐ **Every headless test RETURNS its `Task`.** `Dispatch` returns one, and the expression-bodied `void`
  form compiles while discarding it — so xUnit never awaits, and **no assertion in the body can fail the
  test**. Five such tests shipped in the License Manager and one stage's UI claims rested on them. Prove a
  headless file is alive by injecting `Assert.Fail` into one body. *(#374)*
  ⚠⚠ **And `Dispatch(async () => …)` is the same defect one level deeper**: there is no `Func<Task>`
  overload, so an async lambda with no `return` binds to `Action` — i.e. `async void` — and everything
  after the first `await` detaches. Give it a return value so it binds to `Dispatch<T>(Func<Task<T>>, …)`.
  *(#391)*
- **A derived value that is typed by hand goes stale SILENTLY, and the guard against it must key on the
  value's SOURCE, not on the value.** A shortcut written into a string (`"Format SQL · Alt+F"`) survived the
  gesture being re-bound with a green build and green tests — a tooltip teaching a key that no longer existed.
  A *correctly composed* string contains the same text at run time, so only the declaration (`const` = literal
  by definition) distinguishes the two. Generalises to any copied derived fact. *(#284)*
- **Reflect the real runtime contract of a UI member before guarding on it.** AvaloniaEdit's `TextEditor`
  is **not focusable** — `editor.Focus()` is a no-op returning `false` and `editor.IsFocused` is *always*
  false; keyboard focus lives on `editor.TextArea`. A guard written against the plausible-looking member
  compiles, tests green, and silently disables the feature forever. *(#225, an instance of #199)*

## Known driver gotchas (Firebird + managed .NET driver)

- **`FirebirdSql.Data.FirebirdClient` 10.3.4 implements only Srp / Srp256.** No `Legacy_Auth` code path in the managed assembly. `FbConnectionStringBuilder.AuthPlugins` does **not exist** as a typed property; setting it via the dictionary indexer is silently ignored.
- **`FbServerType` is `Default` or `Embedded`.** `Default` is pure managed wire — `fbclient.dll` is **not loaded** on this code path, and the driver consults its `ClientLibrary` only in Embedded mode, which EmberTern never selects. ⚠⚠ **This line used to end "`ClientLibraryPath` only matters in Embedded mode (kept in the UI but harmless when unused)" — and that parenthesis was wrong, which is why the field is GONE (S-5, 2026-08-05).** It was not harmless: it offered the user a decision that could have no effect, and the user found it by pointing it at a completely invalid DLL and connecting successfully. ⛔ Do not re-add a client-library setting without the Embedded mode that would make it work; two guards in `ConnectionProfileStoreTests` say so, and the second keys on the **assignment** `ServerType = FbServerType.Embedded`, not on a mention.
- **Firebird 3 "Install incomplete... CREATE USER" error**: caused by SYSDBA living only in the legacy password file. Fix is **server-side**, not client-side: `CREATE USER SYSDBA PASSWORD '…' USING PLUGIN Srp;` against any database on the instance (security3.fdb is instance-wide). IBExpert works because it uses native fbclient with Legacy_Auth support; managed .NET driver can't. See `memory/feedback_firebird_multiversion.md`.
- **WIN1250 / WIN1252 / ISO8859_2**: register `CodePagesEncodingProvider.Instance` before any `OpenAsync` (done in `FirebirdConnectionService` static ctor). See `memory/feedback_firebird_codepages.md`.
- ⭐⭐ **The driver DESTROYS text the connection charset cannot hold — client-side, silently, before the server sees it. ONE seam guards it: `FirebirdCommandGuard`.** ⛔ **Never call `connection.CreateCommand()`, `new FbCommand` or `new FbBatchCommand` anywhere but that file, and never assign `CommandText` or `Parameters.AddWithValue` yourself** — use `CreateGuardedCommand(sql)` / `AddGuardedParameter(...)` / `CreateGuardedBatchCommand(...)`; three `CharsetGuardSeamTests` fail the build otherwise. The check is applied **uniformly** (constant ASCII catalog SQL costs ~1.8 µs) precisely so nobody has to judge which call sites are "risky". ⚠ The symptom is **not** `?`: 330 characters become a plausible different one (`£`→`L`, `¼`→`1`, `À`→`A`), so `R = 'Cena £100'` silently became `R = 'Cena L100'` in `RDB$PROCEDURE_SOURCE` — rule #11. ⭐ Reads are already safe (the server refuses to transliterate, loudly); this is a **write-side** problem only. ⛔ **Refuse, never repair.** ⚠ A representability question resolves through `CharsetCatalog.ResolveWireEncoding`, **never** `Resolve` — the latter answers `NONE` as UTF-8, which switches the guard off. Gotchas #372/#373; probe `tools/probes/CharsetProbe`.
- **Connection errors show the raw server message.** `MapErrorMessage` always returns `"Could not connect to {endpoint}: {ex.Message}"` — nothing else. Do not add hints or interpret error causes (wrong password, missing user, plugin mismatch, host down, …); the server message is authoritative and the user or admin can read it directly. Earlier builds tried to categorize errors and surface a `CREATE USER … USING PLUGIN Srp` hint for Legacy_Auth; that was removed because it misfired on unrelated failures (the driver concatenates the whole GDS error vector, so wrong-password / missing-user errors often carried `"plugin"`/`"Legacy_Auth"` text and got mis-hinted).
- **Connection-attempt debug log**: every Connect/Test appends a timestamped, password-masked connection string to `%TEMP%\EmberTern-debug.log` (`LogConnectionAttempt` in `FirebirdConnectionService`). Useful for triaging "EmberTern says X, IBExpert says Y" reports — but remember they take entirely different protocol paths.

## Working style — session protocol

**One milestone per session.** Don't pre-build future milestones — when the user says "M5 only",
that's the scope; questions about M6 go in memory pointers, not code. Start a new session for each
milestone; long sessions re-read the whole transcript every turn, which makes cost unpredictable and
risks running out of context mid-task.

**Every new session starts by reading `CLAUDE.md` + `docs/current-state.md`.** Do not ask the user to
re-explain context — if something needed is missing, that is a documentation gap to fix on the way
out, not a question to ask on the way in.

If the user asks for a change that contradicts a hard rule, **push back briefly and ask** before
implementing — the rules exist for a reason and the user has flagged *"remind me when I drift."*

### Standing directives

These are cross-cutting and ratified. Area-specific ones live in the relevant design doc.

- ⛔ **A package is NOT "fixed" on a green build + tests + smoke alone** *(user directive,
  2026-07-12)*. If a fix cannot be verified **visually in the running app**, report it as
  *"implementation done — awaits user confirmation"*, **never** "fixed". Trace flows to ground truth;
  don't guess.
- ⭐ **Verify Firebird behaviour, never infer it.** Three long-standing architectural beliefs were
  falsified by ~30 lines of probe against the lab (#213, #214, #215). If a design rests on *"Firebird
  does / doesn't allow X"*, measure it first.
- ⭐ **Measure before quoting a number.** Counts kept in prose have gone stale in this project
  repeatedly — and always in the direction nobody re-checks. Derive the test partition filter, don't
  transcribe it; re-count gotchas rather than copying a figure forward.
- ⭐ **One task at a time.** A cross-cutting problem found mid-module goes to the **backlog with its
  measurement**, not into the current stage. A module etap delivers the module — do **not** initiate
  global UI changes or style refactors on the way through.
- ⛔ **Nothing is added because it might be wanted later** — no update check, no telemetry, no
  experimental toggles, no options "for the future". The test is *"is the next step scheduled?"*, not
  *"would this be useful someday?"*
- ⭐ **Root cause before symptom.** A report says *where* the user saw it, not *what* is broken. Prefer
  a rule bounded by the **domain** over one bounded by the reports so far; when a fix would be one more
  entry on an exception list, stop and say so.
- ⛔ **Never chain `dotnet build && dotnet test`** — run them as separate calls; chained they deadlock
  and the user has to interrupt.

### Closing a milestone

1. **Narrative → `docs/history/`.** Extend the most relevant existing file, or create a new one named
   for its topic. This is where "what we tried, what worked, why" belongs.
2. **Status → `docs/current-state.md`.** One line in the closed-stages table, or one row in the open-work
   table. ⛔ Not a report. That file has a **100–300 line** budget.
3. **Rule → `CLAUDE.md`, in place.** Only if the milestone produced a rule that binds *future* work,
   and only as a sentence or a bullet inside the section it belongs to.
4. **Gotcha → `docs/gotchas.md`,** in the right thematic section. Promote it into `CLAUDE.md`'s short
   "Live gotchas" list **only** if it would bite almost any session, not just one working in that module.
5. **Verify:** build 0/0, tests green, app launches — before claiming "done".

### ⛔⛔ The tripwire

`CLAUDE.md` has had to be rescued from unbounded growth **twice** — 4 495 → 472 lines
(2026-07-11), then 6 849 → ~680 (2026-08-11), i.e. ~210 lines/day of appended narrative in between.
Both times the mechanism was identical and looked harmless: a finished stage got "just one more"
paragraph describing what shipped, inside a section whose name (*"Current state"*) invited it.

**So: `CLAUDE.md` has a budget of ~700 lines. Check it when you close a milestone:**

```bash
wc -l CLAUDE.md docs/current-state.md
```

If `CLAUDE.md` is over ~800 lines or `docs/current-state.md` over 300, something that belongs in
`docs/history/` has been written into it — move it out before committing. **A stage's narrative is
never current state, and current state is never a rule.**

## Pointers to deeper notes

For repository documents see § "Documentation map" above — this section covers only what lives
**outside** `docs/`.

**Claude's persistent memory** (`~/.claude/projects/C--Dane-C---r-d-a-EmberTern/memory/`) — `MEMORY.md`
is the always-loaded index; individual files load on demand. ⚠ **`CLAUDE.md` and
`docs/current-state.md` are authoritative over any conflicting memory note** — several memory files
predate the documentation splits and describe states that have since changed.

| Memory file | Holds |
|---|---|
| `project_embertern_editor_architecture.md` | Compact mirror of the editor rebuild's status and decisions. |
| `project_embertern_blueprint.md` · `project_embertern_scaffold.md` | Original V1 scope and the M1–M6 code-layout notes. Froze at V1/M6 — historical framing. |
| `feedback_never_lose_information.md` | The paramount rule (Architecture rule #11). |
| `feedback_staged_implementation_contract.md` | Each etap ships complete + tested + smoke-verified before the next starts. |
| `feedback-one-task-at-a-time.md` | A cross-cutting problem found mid-module goes to the backlog with its measurement. |
| `feedback-root-cause-before-symptom.md` | A report says *where* the user saw it, not *what* is broken. |
| `feedback-verify-external-analysis.md` | An external audit is an input to verify, not a task list. |
| `feedback-tempo-follows-uncertainty.md` | Small steps while a design is forming; one pass once it is accepted. |
| `feedback_first_principles_ux.md` | Halt and redesign from first principles when real usage shows the UX is wrong. |
| `feedback_no_speculative_repro.md` | Report what you *proved* vs what merely did not reproduce. |
| `feedback_naming_no_transitional.md` | No `V2`/`NewX`/`Temp` names survive a completed migration. |
| `feedback-build-and-test-separately.md` | ⛔ Never chain `dotnet build && dotnet test` — they deadlock. |
| `feedback_firebird_*.md` | Code-pages registration, FB3 SYSDBA/Srp auth, the transaction-lane audit trail. |
| `reference_lab_live_fidelity.md` | How to run live simulated-vs-real verification against the lab. |

**Master prompt / V1 blueprint:** `C:\Users\grzegorz.gronski\Desktop\embertern-claude-code-prompt.md`
**Target UI mockup:** `C:\Users\grzegorz.gronski\Desktop\UI koncepcja.png`

