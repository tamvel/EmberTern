# Design Review — Script Executor · Recompile Dependents · Smart SQL Parameters

**Branch:** `feat/script-executor-and-smart-parameters`
**Status:** DESIGN ONLY — no implementation. Awaiting approval.
**Author:** analysis pass, 2026-07-06.

This document is grounded in the actual codebase plus a reflection probe of the shipped
`FirebirdSql.Data.FirebirdClient` 10.3.4 driver (the same technique the Activity-Monitor
milestone used). Every "the driver can/can't" statement below was verified against the
binary, not recalled.

---

## 0. Verified facts (probe results — these drive the whole design)

| Fact | Verified how | Consequence |
|---|---|---|
| `FbScript(text).Parse()` splits a full ISQL script — handles `SET TERM ^ ;`, keeps a PSQL body whole, splits DML on `;`, respects `'a;b'` literals, consumes the `SET TERM` directives themselves | ran it on a mixed SET-TERM script → 4 clean statements | **Part 1 splitting = reuse `FbScript`, NOT the custom `SplitStatements`** |
| Each `FbStatement` carries `.Text` + `.StatementType` (full `SqlStatementType` enum: `CreateProcedure`, `Update`, `CommentOn`, `ExecuteBlock`, `ExecuteProcedure`, `Commit`, `SetGenerator`, `Grant`, …) | probe | drives the results-grid **Operation** column + transaction-control detection |
| The custom `FirebirdDdlExecutor.SplitStatements` has **zero `SET TERM` support** | code read | it's fine for internal single-object Compile, **wrong for user migration scripts** |
| No existing execution path runs **DDL + DML in one caller-controlled transaction without autocommit** — `ExecuteAutonomousBatchAsync` = per-statement autocommit; `ExecuteDdlAsync` = one tx but auto-commit; `FirebirdQueryExecutor` = data-lane working tx, refuses DDL | code read | **Part 1 needs a new `FirebirdScriptExecutor`** |
| EmberTern **never** recompiles dependents — Compile = one `CREATE OR ALTER`; `DependedOnBy` is display-only | code read | **Part 2 is a new opt-in offer, not a suppression** (the user's premise is inverted) |
| Driver's `NamedParametersParser.Parse(sql)` recognizes **only `@name`** (returned `[]` for `:name`) and leaked `@no` from a `-- comment` | ran it | **do NOT use the driver parser for extraction; use the project's own `SqlScanHelpers`** |
| The server describes input params on `Prepare()`, but the type descriptor lives on the **internal** `StatementBase.Parameters` — **no public `FbCommand` accessor**. `FbCommandBuilder.DeriveParameters` is StoredProcedure-catalog-only | probe | **Part 3 types = catalog (for procedures) + Text fallback (arbitrary SQL)**, not driver describe |
| `ExecuteProcedureDialogViewModel` + `ExecuteProcedureParamRowViewModel` + `ParameterHistoryStore` already provide typed controls + per-object value history | code read | **Part 3 reuses these wholesale** |

---

## PART 1 — Script Executor

### 1.1 Naming — recommendation: **Script Executor**

`Migration Executor` narrows it to schema migrations; the capability (run N mixed
DDL/DML statements as one reviewable, transaction-controlled operation) is general. IBExpert
users already know "Script Executive". Recommend **Script Executor** (broad, familiar).
Toolbar tooltip can add "(migrations & multi-object DDL)".

### 1.2 UI host — recommendation: **new Workspace Tab, reusing the SQL editor CONTROL (not the SQL Editor tab)**

Three options weighed:

| Option | Verdict |
|---|---|
| **A. New `WorkspaceTabKind.ScriptExecutor`** | ✅ **Recommended** |
| B. Reuse the existing SQL Editor tab | ❌ collides with its per-connection Saved-Queries workflow + F5 auto-lane classification; a script has different execution semantics (one manual tx, no lane routing) |
| C. Modal dialog | ❌ long-lived, stateful, needs a big editor + big results grid + its own toolbar — that's a workspace tab, not a modal |

It is a first-class module exactly like Security Manager / Trace Monitor / Session Manager,
so the wiring is the proven `CreateSecurityManager` pattern: `WorkspaceTabKind.ScriptExecutor`
+ `WorkspaceTabViewModel.CreateScriptExecutor(...)` + a Monitoring/tools toolbar button
(`Icon.Play`-family, gated on `IsConnected`) + `Is/ActiveScriptExecutor` notify chain +
persist-skip. **Not a singleton** — allow one script tab per script the user opens (like the
Global Search "tab per phrase"); V1 can start with a single reopenable tab and relax later.

**Do NOT copy IBExpert's 3-pane layout** (left object tree + Skrypt/Instrukcje/Opcje tabs).
Proposed layout — one `Grid RowDefinitions="Auto,*,4,240"`:

1. **Toolbar** (row 0): `▶ Run · ⏹ Stop · │ · [tx-mode dropdown] · ☑ Stop on error · │ · ✓ Commit · ✕ Rollback · │ · Open .sql · Save .sql`. Commit/Rollback are prominent and only enabled after a Run left the transaction open (Variant B).
2. **Editor** (row 1): reuse `AvaloniaEdit` + `SqlEditorBehavior.Attach` (Firebird highlighting, autocomplete, Ctrl-click nav, occurrence highlight) + `EditorSearch.Install` (Find/Replace). This is the same control the SQL Editor uses — no new editor.
3. **`GridSplitter`** (row 2) — the same resizable/collapsible/double-click-maximize pattern as the SQL editor results panel.
4. **Results** (row 3): an embedded results grid (see §1.4) + a live status line (N run · M failed · duration) + a transaction-state chip.

### 1.3 Transaction model — recommendation: **Variant B default, Variant C as the setting; never silent autocommit**

This is the load-bearing decision and it is squarely EmberTern's house style ("transaction-aware
by default, no autocommit — ever", hard rule #3). The user is right: safety > convenience.

- **Default = Variant B.** `Run` → begin ONE transaction → execute every statement into it →
  **leave the transaction OPEN** → show the full per-statement results → the user reads them and
  clicks **Commit** or **Rollback**. If a statement failed (with Stop-on-error on), the tx is
  already positioned for Rollback.
- **Variant C = a tx-mode dropdown** exposing:
  - **Manual (review then commit)** — Variant B, the default.
  - **Auto-commit on success** — begin, run all, and if zero failures COMMIT automatically; on any
    failure ROLLBACK and show results (Variant A, but rollback-on-error, never a half-applied script).
  - *(explicitly NOT offered: per-statement autocommit — that's the IBExpert behaviour the user dislikes.)*
- **Explicit transaction-control statements inside the script** (`COMMIT` / `ROLLBACK` / `SET
  TRANSACTION`, detected via `SqlStatementType`) are **rejected before Run in Manual mode** with a
  clear message ("The script controls its own transaction; remove COMMIT/ROLLBACK or switch to
  Auto-commit mode") — because we own the transaction. In Auto mode they may be honoured or still
  rejected (recommend: still reject in V1 for predictability).

**Firebird specifics that make Variant B correct and safe:**
- Firebird DDL is **transactional** — `CREATE`/`ALTER`/`DROP` participate in a transaction and roll
  back cleanly. So a mixed DDL+DML migration genuinely is all-or-nothing under one tx. This is a real
  advantage over engines where DDL auto-commits.
- **Co-location (gotcha #122):** the script tx MUST run on the MAIN (data) connection — the same
  attachment as F5/Execute — or a later `ALTER` of an object created earlier self-blocks. So the
  Script Executor transaction effectively *is* the data lane's single transaction.
- **One tx per connection (gotcha #89):** the script tx is the connection's sole tx. ⇒ **Run is gated
  on "no other working transaction active"**, and while the script tx is open, the SQL Editor's F5 is
  blocked until Commit/Rollback. This must be surfaced (the title-bar tx chip already exists; the
  Script Executor status chip mirrors it). This is acceptable — it mirrors any manual transaction.
- **TPB:** use the Developer-Mode-aware DDL options (`FirebirdDdlExecutor.BuildDdlTransactionOptions`)
  so a migration honours WAIT + lock timeout in Dev Mode.

### 1.4 Results — recommendation: **reuse the batch results *presentation*, NOT its execution pipeline**

The existing `BatchResultsViewModel` / `BatchResultRowViewModel` / `BatchResultsDialog` give
counters + All/Success/Failed filter + Copy-All/Copy-Failed. **Reuse the row model + filter/copy/
counter logic**, but:

- **Embed it in the tab** (not the modal `BatchResultsDialog` — that stays for the tree recompile ops).
- **Richer columns:** `#` · Statement (elided, monospace) · Type (from `SqlStatementType`) · Result
  (OK/FAILED) · Rows (RecordsAffected for DML) · Duration · Error. Matches the user's example table
  plus rows/duration/type.
- **Statement → editor navigation:** clicking a result row selects/scrolls to that statement in the
  editor (each `ScriptStatement` carries its source char offset). This is the IBExpert "click the
  error, jump to the line" affordance and a real win.
- **Do NOT reuse the execution pipeline** (`RunBatchWithReportAsync` → `ExecuteAutonomousBatchAsync`):
  its per-statement autonomous autocommit is the *opposite* of Variant B. The Script Executor uses the
  new `FirebirdScriptExecutor` (§1.5).

Counters (success/error/duration) and Copy (cell/row/all/failed, TSV) come for free from the reused
row/filter/copy shape.

### 1.5 Statement splitting + execution — architecture

**Splitting/classification lives in the Firebird layer (wraps the driver `FbScript`), returns Core DTOs:**

```
EmberTern.Core.Scripting            (pure, zero deps)
  record ScriptStatement(string Text, ScriptStatementKind Kind, int SourceOffset, int SourceLength)
  enum  ScriptStatementKind { Ddl, Dml, Select, ExecuteProcedure, ExecuteBlock, TransactionControl, SetTerm, Other }
  record ScriptStatementResult(int Index, bool Success, int? RecordsAffected, int? RowCount, TimeSpan Elapsed, string? Error)
  record ScriptRunOutcome(IReadOnlyList<ScriptStatementResult> Results, bool TransactionLeftOpen, bool AnyFailed)

EmberTern.Firebird
  FirebirdScriptParser
    IReadOnlyList<ScriptStatement> Parse(string script)   // wraps FbScript(script).Parse(); maps SqlStatementType → ScriptStatementKind
  FirebirdScriptExecutor
    Task<ScriptRunOutcome> RunAsync(IReadOnlyList<ScriptStatement>, ScriptTransactionMode, IProgress<ScriptStatementResult>?, CancellationToken)
    Task CommitAsync() / RollbackAsync()                  // finalize the left-open tx
```

- `FirebirdScriptParser` keeps the driver types (`FbScript`, `SqlStatementType`) internal and returns
  only Core DTOs — the one-way layering rule holds. **Why the driver splitter and not the custom one:**
  it handles `SET TERM`, PSQL bodies, `EXECUTE BLOCK`, literals, comments, and classifies — verified.
  The custom `SplitStatements` handles none of `SET TERM`.
- `FirebirdScriptExecutor.RunAsync` begins ONE transaction on the MAIN connection (co-located), runs
  each `ScriptStatement.Text` via `FbCommand` in that tx (holding `CommandLock`, per gotcha #31),
  records a per-statement result, and — in Manual mode — **does not commit**. It streams each result
  via `IProgress` so the grid fills live (same pattern as the batch dialog's preparing→executing flow).
- Result-set statements (`Select`): to avoid materializing a huge grid, V1 reports **row count**
  (execute + count) or a capped preview; full materialization is a V2 refinement.
- Stop-on-error: on the first failure, stop the loop (tx left open for Rollback). Continue-on-error is
  a toggle (runs all, then the user decides) — but note a failed DDL mid-transaction may cascade
  failures; Stop-on-error is the safer default.

### 1.6 Part 1 — phased plan

- **Etap 1 (Core + Firebird, headless):** `ScriptStatement`/`ScriptStatementKind`/`ScriptStatementResult`,
  `FirebirdScriptParser` (map `SqlStatementType` → kind, carry source offsets), `FirebirdScriptExecutor`
  (one manual tx, run-all, no autocommit, per-statement result, progress, cancellation, Commit/Rollback).
  Tests: split fixtures (SET TERM, PSQL block, EXECUTE BLOCK, literals-with-`;`, comment-with-`;`, mixed
  DDL/DML), classification, transaction-control detection. `FbScript` splitting is the driver's — our
  tests pin the mapping + the executor's tx behaviour (the live run is manual smoke).
- **Etap 2 (App shell):** `WorkspaceTabKind.ScriptExecutor` + `CreateScriptExecutor` + toolbar button +
  `ScriptExecutorTabViewModel` + `ScriptExecutorTabView` (editor + embedded results + tx bar). Reuse
  `SqlEditorBehavior`, `EditorSearch`, the batch row/filter/copy shape.
- **Etap 3 (transaction UX):** Variant B default + mode dropdown (Variant C) + Stop-on-error +
  Commit/Rollback bar + statement→editor navigation + gate on no-other-working-tx + Dev-Mode TPB +
  status chip.
- **Etap 4 (polish):** Open/Save `.sql` (reuse `SqlFileWriter` UTF-8-no-BOM, gotcha #178), large
  result-set handling, cancellation, optional per-tab script persistence.

---

## PART 2 — Recompile dependents (opt-in offer)

### 2.1 Reality check — EmberTern does NOT auto-recompile dependents

The user's premise ("EmberTern prawdopodobnie rekompiluje zależności automatycznie") is **inverted**.
Verified: every Compile path runs a single `CREATE OR ALTER` of the edited object and a metadata
refresh; `DependedOnBy` is used only to render the Dependencies tree. Firebird's `CREATE OR ALTER`
recompiles the object itself and marks dependents' BLR stale (they recompile on next use) — EmberTern
adds nothing on top. So the design is **not "add a prompt to suppress auto-recompile"** but **"add an
explicit, opt-in way to recompile dependents when the user wants it."** This matches the user's real
workflow perfectly: *"I fixed PROC_A, PROC_B isn't ready, I'll fix it next"* → the correct default is
**do nothing**.

### 2.2 Recommendation: on-demand action + a quiet post-compile offer (default = do nothing)

- **Never** recompile dependents automatically.
- After a successful Compile, **if** the object has recompilable dependents, show a **subtle,
  non-blocking prompt** (not a modal that interrupts): *"PROC_A affects N dependent objects. Recompile
  them?"* → opens a **checklist dialog**; or the user ignores it. A "don't ask again this session"
  checkbox prevents nagging. Because the default is do-nothing, this respects the workflow.
- Also expose **"Recompile dependents…"** as an explicit action (editor toolbar overflow / tree context
  menu) so it's available on demand regardless of the prompt.

### 2.3 Reuse — almost entirely existing infrastructure

- **Detection:** `FirebirdTableDetailReader.Get{Procedure,Function,Trigger,Package,View}DependenciesAsync`
  → the `DependedOnBy` list (already used for the Dependencies tree). Filter to **recompilable kinds**
  (Procedure/Function/Trigger/Package/View — a table/domain/generator can't be "recompiled").
- **Checklist dialog:** a small new dialog (checkbox list of dependents, all checked by default,
  Recompile / Cancel). `ChoiceDialog` is N-button, not a checklist, so this needs its own tiny VM +
  view — but it's trivial.
- **Execution:** on confirm, fetch each selected dependent's source and run `CREATE OR ALTER` — this is
  exactly `BuildRecompileStepsAsync` (already exists) fed into `RunBatchWithReportAsync` → the existing
  `BatchResultsDialog` (live counters, filter, copy). So the whole execution + results path is reused.

### 2.4 Part 2 — considerations / phased plan

- **Depth:** V1 recompiles the **direct** `DependedOnBy` set (flat). Transitive recompilation
  (PROC_B invalidates PROC_C) is V2. Each recompile is a `CREATE OR ALTER` of the dependent's own
  stored source, which Firebird re-resolves — so flat, unordered recompilation is correct for the common
  case; topological ordering is a V2 refinement.
- **Cross-attachment "in use" (gotcha #122):** the existing recompile pipeline runs on a transient
  autonomous-batch connection; dependents aren't usually "in use" by the current attachment, and Dev-Mode
  WAIT mitigates. No new risk beyond what "Recompile all" already carries.
- **Etap 1 (single milestone):** dependent detection + recompilable-kind filter + checklist dialog +
  wire to the existing batch pipeline + the quiet post-compile offer with "don't ask again". Reuse
  `BuildRecompileStepsAsync` / `RunBatchWithReportAsync` / `BatchResultsDialog`.

---

## PART 3 — Smart SQL Parameters

### 3.1 The user's hypothesis, corrected against the driver

The hypothesis (SQL → Firebird parses → if valid, params are available → dialog) is *the right shape*,
but the probe shows the driver won't hand us what we need cleanly:

- `NamedParametersParser.Parse` recognizes **only `@name`** (the user's `:name` → empty) and **leaked a
  `@no` out of a `-- comment`**. Inadequate and `:`-blind.
- Input **types** are computed by `Prepare()` but live on the **internal** `StatementBase.Parameters`
  descriptor — **no public `FbCommand` accessor**. `FbCommandBuilder.DeriveParameters` is StoredProcedure-
  catalog-only.

So the correct EmberTern design does **not** lean on the driver for extraction, and gets types from the
**catalog** (for the shapes that matter) with a Text fallback.

### 3.2 Extraction — recommendation: a Core `SqlParameterScanner` built on `SqlScanHelpers` (NOT regex, NOT the driver)

The user explicitly rejected regex and demanded correctness (`:tekst` in a literal, comments, EXECUTE
BLOCK). The project **already has** the right tool: `EmberTern.Core.Sql.SqlScanHelpers` — the
literal/comment/quoted-identifier-aware lexer that underpins every PSQL feature (gotcha #129). Build on it:

```
EmberTern.Core.Sql
  SqlParameterScanner
    record SqlParameter(string Name, int Offset, int Length, char Marker /* ':' or '@' */)
    IReadOnlyList<SqlParameter> Scan(string sql)          // ordered, de-duplicated by name
    string RewriteToDriverMarkers(string sql, out names)  // ':name'/'@name' → '@name' at real positions only
```

Rules (all handled by `SqlScanHelpers`, none by regex):
- Skip string literals, `--` and `/* */` comments, quoted identifiers.
- `:name` and `@name` at host-variable positions are parameters; **`::` is the cast operator** (skip the
  pair); a `:` not followed by an identifier char is not a parameter.
- **EXECUTE BLOCK is the sharp edge the user flagged:** inside an `EXECUTE BLOCK … AS BEGIN … END`, `:var`
  are **block locals, not input params** (the block's inputs are the positional `?` in its header). So the
  scanner must NOT harvest `:name` from a PSQL body. Detect statement kind first (`SqlStatementType` /
  the existing `SqlStatementClassifier`): scan for `:name`/`@name` only in **non-PSQL** statements
  (SELECT / INSERT / UPDATE / DELETE / MERGE / EXECUTE PROCEDURE). For EXECUTE BLOCK, V1 either relies on
  the driver's positional `?` describe or is out of scope — flag clearly (see §3.6).

The driver takes `@name`, so before execution `RewriteToDriverMarkers` converts `:name`→`@name` at the
scanned offsets only, then we bind via the existing `FirebirdQueryExecutor.ExecuteAsync(sql, parameters)`.

### 3.3 Types — recommendation: catalog for procedures, Text fallback for arbitrary SQL

- **Primary (fully typed, matches the screenshot):** for `EXECUTE PROCEDURE name(:a,:b,…)` and selectable
  `SELECT … FROM name(:a,…)`, resolve the object's **catalog** IN-parameter types
  (`FirebirdTableDetailReader.GetProcedureParametersAsync` / the function signature reader) and map by
  positional order → the existing typed controls (Numeric/Date/Timestamp/Boolean/BLOB, `ExecuteParamKind`).
  This is exactly the user's screenshot scenario and needs **zero fragile reflection**.
- **Fallback (arbitrary SQL, e.g. `… WHERE id = :id`):** present **Text** inputs; Firebird coerces on
  bind/execute. Robust and simple.
- **Optional stretch (documented, NOT V1):** reflect the internal `StatementBase.Parameters` descriptor
  after `Prepare()` to type arbitrary-SQL params. Fragile / driver-version-coupled → against the project's
  "reflect only to verify APIs, never at runtime" ethos. Only if a real need appears.
- **Validation step (matches the hypothesis):** optionally `PrepareAsync` the (rewritten) statement first
  — a syntax error surfaces *before* the dialog, and prepare confirms the parameter count. Recommended as
  a cheap correctness gate.

### 3.4 Reuse — the parameter dialog + history already exist

- **Dialog:** `ExecuteProcedureDialogViewModel` + `ExecuteProcedureParamRowViewModel` take
  `(Name, TypeText)` rows and already give per-type controls, NULL-by-default, typed `Resolve()`,
  validation. Feed it the scanned names (+ catalog TypeText where resolved, else `"VARCHAR"`/unknown →
  Text). **Directly reusable.**
- **History:** `ParameterHistoryStore` persists per-`(ConnectionId, ObjectKind, ObjectName)` value sets in
  the encrypted `settings.dat`, with auto-load-newest. For **ad-hoc SQL** (no object name), key history by a
  **hash of the normalized SQL text** (kind = `"AdHocSql"`, name = the hash) — so re-running the same query
  recalls its last values. The user's "should parameters be remembered / value history" questions are
  answered by infrastructure that already exists.

### 3.5 Where it lives — SQL Editor F5 (and Script Executor)

- Integrate into `RunExecuteAsync` / `ExecuteWithMetricsAsync`: after resolving the active SQL, run
  `SqlParameterScanner.Scan`; if it finds unbound params → (optional Prepare-validate) → open the reused
  dialog → on OK, execute with the bound values (rewriting `:`→`@`). If no params, execute as today. Zero
  change to the no-parameter path.
- The Script Executor (Part 1) can reuse the same scanner if a script statement carries params (rare in
  migrations; nice for consistency).

### 3.6 Part 3 — risks + phased plan

- **`:` vs `@` rewrite correctness** — mitigated by `SqlScanHelpers` (proven); pin `::cast`, literal `':x'`,
  comment `-- :x`, `x.y`, dotted qualifiers with tests.
- **EXECUTE BLOCK `:var` locals** — the one genuinely hard case. V1: scan only non-PSQL statements; for
  EXECUTE BLOCK, params are positional `?` in the header → defer or rely on driver describe. Document the
  limitation (don't silently harvest body locals).
- **Types for arbitrary SQL** — Text fallback (robust); catalog typing only where the shape resolves.
- **Etap 1 (Core):** `SqlParameterScanner` on `SqlScanHelpers` (`:name`+`@name`, cast/literal/comment/
  quoted-ident aware, dedup, ordered) + `RewriteToDriverMarkers` + tests (all the edge cases above).
- **Etap 2 (App integration):** SQL Editor F5 intercept → reuse `ExecuteProcedureDialog` → bind + execute;
  ad-hoc history keyed by SQL hash. Optional Prepare-validate gate.
- **Etap 3 (typed controls):** catalog type resolution for `EXECUTE PROCEDURE` / selectable-proc / function
  calls (reuse the existing readers); EXECUTE BLOCK handling/exclusion + clear messaging.

---

## Cross-cutting: sequencing & independence

The three features are **independent** and can ship in any order on this branch. Recommended order by
**value ÷ risk**:

1. **Part 3 (Smart Parameters)** — smallest, highest daily value, ~80% reuse (dialog + history + lexer),
   lowest risk. Great first win.
2. **Part 1 (Script Executor)** — the headline; the only genuinely new subsystem (script executor + tx
   control + new tab). Largest.
3. **Part 2 (Recompile dependents)** — small, self-contained, almost entirely reuse; do last or interleave.

Shared new Core surface: `SqlParameterScanner` (Part 3) could also feed Part 1. `FbScript`-based
`FirebirdScriptParser` (Part 1) is not needed by the others.

## Architecture-rule compliance (checked against CLAUDE.md hard rules)

- **Core zero-Avalonia / Firebird isolates `Fb*`:** `Script*` DTOs + `SqlParameterScanner` are pure Core;
  `FirebirdScriptParser`/`FirebirdScriptExecutor` hold all driver types and return Core DTOs. ✅
- **No interface without two impls:** no new interfaces proposed (direct classes, matching the codebase). ✅
- **No autocommit ever (hard rule #3):** Variant B is the default; even Variant C never does per-statement
  autocommit. ✅ — this feature *reinforces* the rule.
- **Reuse before create (UI rules):** results presentation, parameter dialog, history store, batch pipeline
  (Part 2), editor control, editor Find/Replace all reused. New surface is minimal and justified. ✅
- **Theme discipline:** the new tab + checklist dialog use only theme tokens + shared styles. ✅

## Open decisions for the user to confirm before implementation

1. **Naming:** Script Executor (recommended) vs Migration Executor.
2. **Transaction default:** Variant B (recommended) confirmed as default, with Variant C as a mode dropdown?
3. **Result sets in scripts:** report row count only (recommended V1) vs preview grid?
4. **Recompile dependents surface:** quiet post-compile offer + on-demand action (recommended), default
   do-nothing — confirmed?
5. **Smart Parameters marker:** support both `:name` and `@name` (recommended) — confirmed? And EXECUTE
   BLOCK deferred in V1?
6. **Build order:** Part 3 → Part 1 → Part 2 (recommended) vs Part 1 first (headline first)?
