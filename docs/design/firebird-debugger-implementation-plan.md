# Stage X — Firebird Debugger: Implementation Plan

**Companion to [firebird-debugger.md](firebird-debugger.md) (DESIGN v2 — the specification).**
Nothing implemented. This document is the **execution plan**: milestone briefs, session split, danger
zones, and the Developer Contract.

| Document | Its job |
|---|---|
| `firebird-debugger.md` | **WHAT and WHY.** Feasibility, §F Fidelity Law, architecture, boundaries, decisions. The authority on behaviour. |
| **this file** | **IN WHAT ORDER, and UNDER WHAT RULES.** Per-milestone briefs, how to split sessions, what not to break. |

> **How to start a debugger session.** Read CLAUDE.md → this file's §1 + the milestone brief → the spec
> sections the brief cites → **§4 Developer Contract**. Do not read the whole spec every session; the
> briefs cite what each milestone needs.

---

## 1. Session protocol

The project rule is **one milestone per session**. Some milestones need more than one; those are split
below at an explicit seam, and **every seam is a committable state**.

**Every session ends with, without exception:**
1. `dotnet build EmberTern.slnx` → **0 warnings / 0 errors** (`TreatWarningsAsErrors=true`).
2. `dotnet test EmberTern.slnx` → **all green**, in ONE run.
   ⚠ **Never chain build+test in one command** — they deadlock and hang. Separate tool calls.
3. **Smoke**: the app launches (`src\EmberTern.App\bin\Debug\net9.0\EmberTern.exe`).
4. **Lab verification** where the milestone touches engine behaviour (§3.4).
5. Docs updated *before* closing: narrative → a `docs/history/` file; CLAUDE.md "Current state" → **in
   place, one bullet**; genuinely new lessons → `docs/gotchas.md`.
6. A clean, committable tree. **Never leave a milestone half-landed.**

**Branch:** one feature branch for the debugger arc (e.g. `feat/firebird-debugger`), one commit per
milestone (or per seam). Do not merge to `master` until an arc completes.

**If a session runs out of context mid-milestone:** stop at the nearest seam, land it green, and record
the parked state in CLAUDE.md's "Current state". Gotcha #233 is the reason this matters: *a correct,
tested component that nothing calls is indistinguishable from a regression*, and CLAUDE.md is where the
next session finds out it was parked.

---

## 2. Milestone briefs

Legend: **Dep** = depends on · **New** = new types · **Mod** = existing components modified.

---

### P1 — AST: exception handlers *(prerequisite — blocks D1)*

- **Cel.** Make `WHEN … DO` readable from the tree. The interpreter owns exception control flow (spec
  §3.6) and today has nothing to read: handlers are an unstructured `PsqlLeafKind.Other` token bag.
- **Zakres.** Parser producer + binder consumer, per Etap 6.9's contract. **Additive only.** Formatter
  convergence is **out of scope** (spec: build grammar depth only when a feature needs it — the
  formatter's current handler layout is not broken).
- **New.** `Ast/PsqlNodes.cs`: `WhenHandler : PsqlStatement` — **one `WHEN … DO` clause** carrying an
  **ordered `IReadOnlyList<WhenCondition>`** + `Body`; `WhenCondition` (kind + optional operand — the
  exception name, gds/sql code, sqlstate literal); `WhenHandlerKind` enum (`Any` / `ExceptionName` /
  `GdsCode` / `SqlCode` / `SqlState`); `BlockStatement.Handlers` (+ `Children`). **Refined 2026-07-17
  (decision 3):** Firebird permits a comma-separated condition list per `WHEN`, so a single kind per node
  is insufficient — a `WhenHandler` holds the whole list, and D1's router matches them in declaration
  order.
- **Mod.** `SqlParser.Psql.cs` (parse the handler section of a block + the condition list),
  `Ast/PsqlNodes.cs`, `SemanticBinder.Psql.cs` (bind handler bodies + the exception-name reference of each
  `ExceptionName` condition).
- **Dep.** None (Etap 6.9 is closed).
- **Ryzyka.**
  - **§0 round-trip.** Handlers must stay in the lossless token stream; an unrecognised handler shape
    **must** fall back to the existing `PsqlLeafKind.Other` valve, never be swallowed.
  - `WhenHandlerKind` must not be guessed from text — parse the grammar (recognise by leading keyword).
  - Do **not** touch `SqlFormatter`. Byte-identical output is a hard requirement here.
- **DoD.** Nodes produced for all handler forms (incl. multi-condition `WHEN`); binder resolves handler
  bodies against the enclosing scope and references each `EXCEPTION <name>` condition; formatter output
  **byte-identical**; §0 differential harness (B0) green; unrecognised shapes still land in `Other`.
- **Weryfikacja.** Unit tests beside `PsqlAstTests` (one per handler form + multi-condition + a malformed
  shape → `Other` + the binder's exception-name reference); the B0 differential corpus extended with
  handler shapes; `SqlFormatterSafetyTests` unchanged and green.
- **Sesje: 1.**

---

### P2 — Server version gate (FB3+) *(app-wide — not debugger-scoped)*

- **Cel.** Reject a pre-FB3 server on connect with a clear message (decision 8). Spec §1.3.
- **Zakres.** Deliberately tiny and **outside** the debugger. **Free**: the driver is Srp-only and FB2.5
  is Legacy_Auth-only, so FB2.5 is *already* unreachable — this only makes the failure legible.
- **Mod.** `FirebirdConnectionService` (post-open precondition check), `UiStrings` (the message).
  Reuse `FirebirdDdlReader.ParseServerMajor(connection.ServerVersion)` — the app's **one** version-parsing
  site; do not add a second.
- **Dep.** None.
- **Ryzyka.**
  - ⚠ **Do not break the documented rule** *"connection errors show the raw server message — never
    interpret"*. This is a **precondition check**, not error interpretation: it runs on a successfully
    opened connection and states a fact we know. Keep `MapErrorMessage` untouched.
  - Must run on **every** lane's open path, and must not leave a half-open attachment.
- **DoD.** Connecting to FB3/4/5 unaffected; a sub-FB3 server is refused with a specific message naming
  the required version; the connection is closed cleanly.
- **Weryfikacja.** Unit test on the version predicate (a pure function over a version string — table-drive
  it: `"2.5.9"`, `"3.0.0"`, `"5.0.3"`, malformed). **Live rejection cannot be lab-tested** (no FB2.5
  instance) — pin the predicate, and state honestly in the session summary that the live path is
  unverified. *(Follow-up, not urgent: existing `serverMajor >= 3` gates become statically true.)*
- **Sesje: 1** (small — may share a session with P1 only if both land green separately).

---

### D1 — Debug engine core *(pure Core, no server)*

- **Cel.** The interpreter: frames, scopes, stepping, exception routing, breakpoints — **proven with zero
  server in the loop.** Control flow is the part we own, therefore the part we can get wrong.
- **Zakres.** Spec §3.1, §3.6, §3.7, §4.5, §5. **No UI, no Firebird.**
- **New.** `EmberTern.Core.Sql.Debugging`: `DebugSession` (state machine), `Frame`, `FrameValues`,
  `StepPlanner` (Into/Over/Out/RunToCursor/SetNext), `ExceptionRouter` (handler matching + unwinding),
  `BreakpointSet`, `IDebugExecutor` (contract only), `DebugState`/`StepKind`/`StopReason` enums.
- **Mod.** Nothing existing. **Purely additive** — this is why D1 is first.
- **Dep.** P1.
- **Ryzyka.**
  - **Rule #2** ("no interfaces without two implementations"): `IDebugExecutor` is the **one precedented
    exception** (`ISqlMetadataProvider` sets it — Core declares the contract it needs). **Do not add a
    second interface** on this argument without review.
  - The **frame savepoint model** (spec §4.5) must be in the engine from day one — savepoint on frame
    entry, rollback-to on unhandled exit, **never per block, never per statement**. Retrofitting it is
    how v1's silent divergence happened.
  - Core purity: **zero Avalonia, zero `FirebirdSql`**. If you need a driver type, the design is wrong.
- **DoD.** Interpreter executes block/`IF`/`WHILE`/`FOR`/leaf control flow and full exception routing
  against a **fake executor**; every step decision is a pure function of (AST, frame, breakpoints);
  savepoint enter/rollback events are emitted at frame boundaries.
- **Weryfikacja.** Headless Core unit tests (`DebugEngineTests`) with a scripted fake `IDebugExecutor`:
  step ordering, nested frames, handler matching per form, propagation + unwind, re-raise, breakpoint
  hits, Run-To-Cursor, Set Next Statement. **No lab needed** (no server). This is the milestone where
  test depth is cheapest — spend it here.
- **Sesje: 2.** Seam: *(a)* frames + scopes + stepping; *(b)* `ExceptionRouter` + savepoint events +
  breakpoints.

---

### D2 — Harness + session connection + executor *(Firebird)*

- **Cel.** The server contract: generate the `EXECUTE BLOCK`, run it on a per-session connection, in the
  debug transaction, with frame savepoints.
- **Zakres.** Spec §3.2, **§3.4 (R1–R5)**, §3.5, §4.1, §4.2, §4.5.
- **New.** Core: `HarnessBuilder` (+ `HarnessRequest`/`HarnessResult`), `ReadWriteSetAnalyzer`.
  Firebird: `FirebirdDebugExecutor : IDebugExecutor`, `DebugSessionConnection` (own `FbConnection` + tx +
  savepoint API).
- **Mod.** `FirebirdConnectionService` (session-connection lifecycle: create/dispose, tear down on
  disconnect). **Do NOT add a `ConnectionRole.Debug`** — decision 5: a session connection is not a lane.
- **Dep.** D1.
- **Ryzyka — the highest-risk milestone in the arc.**
  - **§3.4 R1–R4 are non-negotiable.** Skip `NULL` injection; base types for params/`RETURNS`; frame
    variables verbatim. v1's naive injection **crashes on ordinary ERP code** (`validation error for
    variable V, value "*** null ***"`). Base types come from **metadata**, never from string-munging a
    declaration.
  - **R5**: *always* carry every in-scope sub-routine declaration, regardless of the read set — otherwise
    a local `F()` silently resolves to a global `F()`. Worst-class §F violation.
  - **Never** `BeginTransactionAsync` on `TransactionService` — its early-return is a **join** (#230); the
    debug session owns its own transaction.
  - **Never** a bare `IsolationLevel` — explicit `FbTransactionOptions` (#85).
  - **Type round-trip (FB4+)**: carry driver-native types end-to-end; never convert (`DECFLOAT`/`INT128`).
    **⚠ Probe blocks FB4+ support** (spec §14).
- **DoD.** Assignments, `IF`/`WHILE` conditions and DML leaves execute against the lab through the
  harness; read/write sets narrow the payload; frame savepoints commit/rollback correctly; a
  domain-`NOT NULL`-typed uninitialized variable **does not crash**.
- **Weryfikacja.** **Lab-mandatory.** Extend `Lab/setup.sql` with the debugging zoo *(nested calls, local
  routines/closures, cursors, exception handlers, an autonomous-transaction routine, a generator user,
  domain-typed `NOT NULL` variables)*. Then: **simulate a routine step-by-step and compare the resulting
  DB state + outputs to REAL execution of the same routine.** That comparison is the only proof of
  fidelity (spec §2.1). Plus unit tests on `HarnessBuilder` text generation (pure function).
- **Sesje: 3.** Seams: *(a)* `DebugSessionConnection` + TPB + savepoints; *(b)* `HarnessBuilder` + §3.4
  rules (pure, unit-tested); *(c)* `FirebirdDebugExecutor` + lab fidelity comparison.

---

### D3 — Editor-wiring consolidation

- **Cel.** One wiring seam before the first debugger UI. Spec §11.1, gotcha #219.
- **Zakres.** Consolidate `SqlEditorBehavior.Attach` (object editors) and `MainWindow`'s hand-wiring.
  **Zero user-visible change.**
- **Mod.** `SqlEditorBehavior`, `MainWindow(.axaml.cs)`, and every capability's attach site.
- **Dep.** None technically — **placed here on purpose**: after the risky core is proven, before the debug
  tab becomes a third host with four new capabilities at once.
- **Ryzyka — deceptively hard; this is not a mechanical merge.**
  - The two seams differ by a **real lifecycle**: MainWindow's editor exists *before* its VM; object
    editors attach *after*. MainWindow deliberately bypasses `subscribeMetadataChanged` because it latched
    "subscribed" against a null VM and dropped the handler. **Consolidation must first solve "subscribe
    once the VM arrives."**
  - Touches every capability on every surface for zero visible value ⇒ under the QA rule this needs **full
    manual re-verification everywhere** (squiggles, hover, related elements, completion, diagnostics
    panel, F8 navigation — in *both* the SQL Editor and the object editors, in *both* themes).
- **DoD.** One attach path; every existing capability verified live on every surface; no behaviour change.
- **Weryfikacja.** Existing suite green + **a manual pass over every editor surface** (report honestly as
  "awaits user confirmation" per the QA rule — this cannot be proven by tests alone).
- **Sesje: 1–2.** Seam: *(a)* solve the VM-arrival subscription; *(b)* migrate the capabilities.

---

### D4 — Debugger tab MVP

- **Cel.** First real user value: launch a **standalone procedure**, set breakpoints, step, see variables.
- **Zakres.** Spec §9.1, §9.2, §9.3, §9.6, §9.7. Standalone procedures only — no triggers, packages,
  local routines, cursors.
- **New.** App: `DebuggerTabViewModel`, `DebuggerTabView`, `DebugLaunchPanelView(Model)`,
  `BreakpointMargin`, `CurrentLineRenderer` (`IBackgroundRenderer`), `DebugCommands`.
- **Mod.** `WorkspaceTabViewModel` (new tab kind), `MainWindowViewModel` (open-debugger command + the
  `IsXxxTabActive` chain — gotcha #25), sidebar context menus (Debug action), `Themes/Colors.axaml`
  (+ **both** dictionaries), `UiStrings`.
- **Dep.** D1, D2, D3.
- **Ryzyka.**
  - Reuse the **Smart SQL Parameters** infrastructure (`SmartParametersRequest`,
    `ExecuteProcedureDialogViewModel`, `ProcedureParamRowViewModel`, `QueryParameter`) — **do not build a
    second typed parameter editor**.
  - `F5` = Continue is tab-scoped and contradicts the app-wide `F5` = Execute (spec §14 open item).
  - Renderers mirror `SquiggleRenderer`/`RelatedElementsRenderer`; repaint via `TextView.Redraw()`, **not**
    `InvalidateVisual()` (gotcha #223).
  - **Pre-flight warnings** (§4.6 autonomous tx + generators) must ship **with** the MVP, not later —
    they are a data-safety promise, not polish.
- **DoD.** Launch → breakpoint → step → stop works on a lab procedure; rollback on session end; pre-flight
  warnings shown; both themes; keyboard works.
- **Weryfikacja.** VM unit tests (`DebuggerTabVmTests`); a headless probe **only** inside the existing
  `ConnectionExpandBindingProbe` class (gotchas #94/#226 — one session per process, shared); **manual lab
  run** → "awaits user confirmation".
- **Sesje: 2–3.** Seams: *(a)* tab shell + launch panel + parameters; *(b)* breakpoints + current line +
  step commands; *(c)* basic variables list.

---

### D5 — Expression evaluation surface

- **Cel.** Evaluate + Watches + Immediate — **one engine, three surfaces** (decision 6). Spec §9.5.
- **Zakres.** Evaluate selection/hover (`Shift+F9`); Watches panel (persisted per routine); Immediate
  window.
- **New.** App: `WatchesPanelViewModel`/`View`, `ImmediateWindowViewModel`/`View`, `EvaluateController`.
  Core: reuse `HarnessBuilder` — **no new engine**.
- **Mod.** `HoverInfoEngine` consumer side (hover-evaluate); settings store (watch persistence).
- **Dep.** D2, D4.
- **Ryzyka.**
  - **One mechanism.** If a surface needs "just a small evaluator", the design is being violated.
  - A watch runs **real SQL in the debug transaction** and can have side effects; auto-re-evaluated
    watches must be **flagged when not a pure expression**, and every evaluation lands in Executed SQL.
- **DoD.** All three surfaces route through `HarnessBuilder`; watches survive restart; side-effect flagging
  present.
- **Weryfikacja.** Unit tests on the shared path; lab: evaluate an expression calling a stored function and
  compare to `SELECT <expr> FROM RDB$DATABASE`.
- **Sesje: 2.** Seam: *(a)* Evaluate + Immediate; *(b)* Watches + persistence. **Placed early on purpose:
  the Immediate window is the best test instrument for D2's harness.**
- **STATUS — D5 COMPLETE (2026-07-18; live evaluation awaits user confirmation).**
  The one engine is **Core**: `EvaluationModels` + `IDebugExecutor.Evaluate` + `DebugSession.Evaluate`
  (arbitrary fragment → §3.5 `InScopeLocals` inject; Firebird executor reuses the D2 harness). **Deviation
  (documented):** no App `EvaluateController`/`WatchesPanelViewModel` — the real "one engine" is
  `DebugSession.Evaluate`; the App orchestration (evaluate + the watch re-eval loop) is thin enough to live on
  `DebuggerTabViewModel` (as stepping is), so separate controller/panel VMs would be pure indirection.
  - **Seam (a) — Evaluate + Immediate + Executed SQL audit** (§10.3 — every evaluation lands there, harness
    SQL visible). Immediate window (expression / "as statement" → live-frame write-back), Evaluate (Shift+F9).
    Post-QA: input kept (not auto-cleared) + inline Clear (✕); procedure-editor Debug toolbar button (reuses
    the one launch path).
  - **Seam (b) — Watches.** Re-evaluated after every pause through the **same** `DebugSession.Evaluate` (no
    second evaluator); persisted per routine (`WatchStore` over `settings.dat`); non-pure watches flagged via
    `WatchSideEffectDetector` (reuses the one `SqlLexer`, no new parser). `WatchRowViewModel` (mutable row).
  Build 0/0; **4782 tests green**; smoke clean. **Next milestone: D6 (Cursor Bridge).**
  **Backlog (user, recorded — NOT D5):** Immediate should pre-validate **syntax** locally via the existing
  `EditorLanguageService` before the `EXECUTE BLOCK` (reuse the Language Service; syntax-only locally;
  semantics/execution stay the server's).

---

### D6 — Cursor Bridge

- **Cel.** Step through `FOR SELECT` / `DECLARE CURSOR` bodies with a **real** incremental cursor. Spec §7.
- **Zakres.** PSQL cursor ↔ DSQL cursor on the session connection, in the debug transaction.
- **New.** `CursorBridge` (Firebird), `CursorHandle`.
- **Mod.** `DebugSessionConnection` (**per-wire-operation locking** — see risks), `StepPlanner` (loop
  iteration), Variables window (Cursors group).
- **Dep.** D2, D4.
- **Ryzyka.**
  - ⚠ **Locking.** Gotcha #236: interleaving is fine, **concurrency is not**. Holding `CommandLock` for a
    cursor's *lifetime* deadlocks every harness step inside the loop → take it **per wire operation**
    (each `Read()`, each execute). Capture a lock object **once** per acquire/release pair (#98/#120).
  - Cursor SQL comes from `ForSelectStatement.Query` / `DeclareCursorStatement.Query` **spans** (B3.1) —
    never re-derived, never re-parsed.
  - **Never materialize** the result set (memory + semantics). §F.
  - **⚠ Probes block this milestone:** `WHERE CURRENT OF` on a named DSQL cursor; cursor interleaving on
    **FB3/FB4** (verified on FB5 only).
- **DoD.** Stepping a `FOR SELECT` body iterates a real cursor; nested loops work (two cursors); `INTO`
  targets land in the frame.
- **Weryfikacja.** **Lab-mandatory**: a routine looping over a lab table, simulated vs real execution,
  identical results. Nested-loop case included.
- **Sesje: 2.**
- **STATUS — D6 COMPLETE (2026-07-18; in-app stepping UX awaits user confirmation).** Probes first: FB3+FB5
  interleaving verified live (FB4 no instance); `WHERE CURRENT OF` unsupported cross-context (SQL -504) → a §F
  boundary, honest step error, out of DoD (spec §15.5). **D6a** (added, not in the original brief): additive
  AST `ForSelectStatement.IntoTargets` + `CursorName` (Contract #1 — structure belongs in the AST, not a
  token-scan in the Firebird layer). **D6b**: pure Core `CursorBridge` (`Build(source, loop) →
  CursorQueryPlan`, mirrors `HarnessBuilder`) + Firebird `CursorHandle : IDebugCursor` (real `FbDataReader`
  held open, **per-wire-op** locking #236) + `OpenCursor` glue. **§F correction (live -804):** rewrite **only**
  the `:name`/`@name` form (a bare name is a column — a `SELECT LINE_NO` shadowing `RETURNS (LINE_NO)` broke
  otherwise; gotcha #239). Lab zoo +`SP_DBG_CURSOR`/`SP_DBG_NESTED`; sim-vs-real proven incl. a fully-stepped
  run + nested cursors. Build 0/0; **4797 green** (+11); smoke clean. **`DECLARE CURSOR` explicit
  OPEN/FETCH/CLOSE + `WHERE CURRENT OF` support are follow-ups, not D6.**

---

### D7 — Variables window, full

- **Cel.** The most important panel. Spec §9.4.
- **Zakres.** Grouping/icons, change highlight, inline edit + validation, pins, types, `<null>`, lazy
  BLOBs, filter, **data tips**.
- **New.** `VariableRowViewModel`, `VariablesPanelViewModel`/`View`.
- **Mod.** **`HoverInfoEngine`** — add a `DebugValue` section to the ordered aggregate (`HoverInfo`);
  `HoverInfoView`; `Themes/Colors.axaml` (both dictionaries).
- **Dep.** D4.
- **Ryzyka.**
  - `HoverInfo` is an **ordered aggregate with no `IHoverProvider`** by deliberate design — extend it, do
    **not** introduce a provider abstraction (spec §9.4; the "one responsibility, one owner" rule).
  - VM holds an **icon key string**, never a brush (rule #1 + theme rules).
  - Inline edit must validate against the declared domain, or the next injection fails (§3.4).
  - BLOBs **lazily** — reuse the existing value viewer.
- **DoD.** All of the above live in both themes; data tips show current values; no hardcoded colours.
- **Weryfikacja.** VM unit tests; manual pass in both themes → "awaits user confirmation".
- **Sesje: 2.**

---

### D8 — Call stack + nested stored routines

- **Cel.** Frames as data, not windows. Spec §5.
- **Zakres.** Call Stack panel, **Breadcrumbs** (the shared backlog feature), Peek Frame, frame keyboard
  nav, simulated-frame indicator, step-into a stored routine.
- **New.** `CallStackPanelViewModel`/`View`, `FrameRowViewModel`, `BreadcrumbsView` (**shared**, not
  debugger-local).
- **Mod.** `NavigationController` (reuse `JumpTo` + Peek), `FirebirdDdlReader` consumer (fetch callee
  source).
- **Dep.** D1, D4.
- **Ryzyka.**
  - Breadcrumbs is a **backlog feature for the whole editor** — build it as the shared feature; a
    debugger-local copy is drift.
  - ⚠ **Do not post caret+selection at `DispatcherPriority.Background`** — gotcha #221: `Input` outranks it,
    so a held key reads the pre-jump caret. Caret+selection **synchronously**; only scroll+focus posted.
  - Focus lives on `editor.TextArea`; `editor.Focus()` is a **no-op** (#225).
  - Step-into = simulation, step-over = real (§5.3) — the indicator is required, not optional.
- **DoD.** A→B→C stack navigable; selecting a frame repoints editor + variables; breadcrumbs mirror the
  stack; frame savepoints correct on unwind.
- **Weryfikacja.** Lab: nested procedures; simulated vs real. VM tests for stack/selection.
- **Sesje: 2–3.**
- **STATUS — seam (a) DONE (2026-07-18; pure Core, no server, no user-visible change yet).** The faithful
  Step Into the DoD needs (pass arguments, write `RETURNING_VALUES` back) required an **AST deepening**, so the
  analysis stopped for a decision before coding (per Contract #1/#15) and the user ratified full D8 starting
  from a pure-Core foundation seam. Landed: **AST** — `ExecuteProcedureStatement.Arguments` (per-arg source
  spans) + `ReturningTargets` (folded), parser producer `ReadProcedureCallParts` (additive; §0 tokens
  round-trip; formatter + binder unchanged). **Frame model** — `LexicalParent` **split** from the call-stack
  `Parent` (gotcha #241): a stored callee is a **closed scope** (`LexicalParent = null`), a local sub-routine's
  (D9) is its declaring frame; `TryResolveValue`/`SetResolvedValue` walk `LexicalParent`. `OutputParameterNames`
  on `Frame`/`DebugRoutine`; `DebugRoutine.LexicalParent`. **Interpreter** — `ApplyReturningValues` on a callee's
  normal return binds its outputs positionally into the caller's `RETURNING_VALUES` targets (§5). A D1 test that
  used a stored-proc call as a scope-chain proxy was split into an honest stored (closed) + local (closure, D9
  mechanism) pair. Build 0/0; **4813 green** (+6); smoke clean. `ResolveRoutine` still returns null in prod →
  **no callee frame is pushed yet** (gotcha #233: staged, not a regression — recorded here + in CLAUDE.md).
- **STATUS — seam (b) DONE (2026-07-18).** Firebird `ResolveRoutine` (multi-routine executor context:
  fetch/parse/metadata the callee, seed args via the harness) + **lab fidelity** proven (`ROOT→MID→LEAF`,
  simulated == real). See CLAUDE.md for detail.
- **STATUS — seam (c) DONE (2026-07-18; awaits visual confirmation). D8 IS COMPLETE.** Part 1: Call Stack panel
  (display-only). Part 2: **frame navigation** — the call stack / breadcrumbs / `Ctrl+Alt+Up/Down` repoint the
  editor source + current-line marker + Variables to the selected frame (spec §5.2), and the editor auto-follows
  the innermost frame after Step Into. Per-frame roster surfaced by threading the callee `SemanticModel` onto
  `Frame.Model` (Contract #1 — no re-parse; exactly as `Source` is threaded). One selection truth
  (`ApplySelectedFrame`); navigation never touches the session. **Breadcrumbs = a generic shared
  `EmberTern.App.Controls.BreadcrumbBar`** (the debugger is its first consumer). Peek Frame = a transient
  double-click card. Breakpoints stay root-routine-scoped (a callee/other-caller view surfaces none — D12).
  Caller-line bug fixed (child's `CallSite`, in this frame's own source — gotcha #243). +5 `DebuggerTabVmTests`;
  build 0/0; full suite green (run manually — full `dotnet test` hangs, #94/#226); smoke clean. **DoD met: A→B
  stack navigable; selecting a frame repoints editor + variables; breadcrumbs mirror the stack.** Next: **D9**
  (local procedures & functions — the flagship). Gotchas #241/#242/#243.

---

### D9 — Local procedures & functions 🏁 *(the flagship)*

- **Cel.** Local routines as **real frames** — the capability IBExpert cannot deliver. Spec §6.
- **Zakres.** Sub-routine frames (interpret — §6.2a) + closure harness (§6.2b) + read/write sets + **R5**.
- **New.** Little or nothing — **this is the design's central claim**: a local routine is a frame whose
  lexical parent is the declaring frame, so it falls out of D1+D2+D8.
- **Mod.** `Frame` scope chain (lexical parent), `ReadWriteSetAnalyzer` (transitive fixpoint over the
  sub-routine call graph).
- **Dep.** D1, D2, D8.
- **Ryzyka.**
  - ✅ **Gate MEASURED (2026-07-18, `Fb3ClosureProbe`, spec §15.7).** FB3.0.13 sub-routines are **CLOSED
    scopes** (outer var rejected, SQL -206); FB5.0.3 are **true closures** (read+write by ref, confirms
    §6.1). FB4 unverified (not installed). **⇒ the frame's `LexicalParent` branches on server major**
    (`ParseServerMajor`): FB3 → `null` (like a stored callee); FB5 → declaring frame. FB4 conservative,
    a documented §F boundary. No new abstraction — the D8 `LexicalParent` split (gotcha #241) already models
    both. Do **not** assume FB5 semantics on FB3.
  - **R5** again: never drop a sub-routine declaration from a harness.
  - If new abstractions are needed here, **something earlier was built wrong** — stop and reconsider rather
    than special-casing.
- **DoD.** Step into a local function/procedure; it appears as a frame with its own variables; outer
  reads/writes propagate; step-over evaluates via the closure harness with write-back.
- **Weryfikacja.** **Lab-mandatory**: a routine with local function + local procedure (incl. one mutating
  an outer variable) — simulated vs real, identical. Plus the FB3/FB4 probe log recorded in the spec §15.
- **Sesje: 2.**
- **STATUS — §6.3 gate MEASURED + seam (a) DONE (2026-07-18).** Gate (spec §15.7): FB3 sub-routines CLOSED,
  FB5 true closures ⇒ `LexicalParent` branches on server major. **Seam (a) Part 1** (pure Core): AST
  `SubroutineDeclaration` + `BlockStatement.LocalRoutines`, parser producer, binder nested scope, extractor R5
  carry — `ResolveRoutine` still null (staged). **Seam (a) Part 2** (runtime + live fidelity):
  `ResolveRoutine` resolves a **local `DECLARE PROCEDURE`** to a real frame — `TryFindLocalProcedure` walks the
  lexical chain, `BuildLocalRoutineAsync` reuses the already-parsed `Body` (no source fetch) + the D8
  argument-seeding harness + `RETURNING_VALUES` write-back; `LexicalParent` by server major (FB5 declaring
  frame / FB3+FB4 null). New metadata path `FirebirdDebugMetadata.BuildLocalRoutineFrameVariablesAsync` — a
  local routine has no `RDB$PROCEDURE_PARAMETERS` row, so param/`RETURNS` types come from the AST header (new
  pure-Core `PsqlDeclarationExtractor.ExtractSignature`, R3 verbatim + R2 base-type derivation). **R5** wired
  into the harness (`RoutineContext.SubRoutines`) so a local **function** is exercised server-side (step-over —
  it is expression-embedded, never an `EXECUTE PROCEDURE` step point). Lab zoo +`SP_DBG_LOCAL`; `DebuggerFidelityProbe`
  extended — **sim `TOTAL=115` == real** (Step Into `ADD_TAX`, depth 2). Build 0/0; **4852 green**; smoke clean.
  **Seam (b) Part 1 — closure capture for stepped-INTO frames (2026-07-18):** Step Into a local routine whose
  body reads+writes an OUTER variable (an FB5 closure). Core `Frame` gained declared-names (own scope for correct
  shadowing); `DebugSession` routes write-backs up the closure chain (`SetResolvedValue`); Firebird `BindValues`
  declares captured ancestor variables in the harness. No fixpoint needed (step-into refs are precise via the
  shared model). Lab +`SP_DBG_CLOSURE`; probe **sim `TOTAL=25` == real** (BUMP: ACC 5→15→25, the closure write
  reaching the parent). Build 0/0; **4853 green**; smoke clean. **Seam (b) Part 2 — the transitive read/write-set
  fixpoint over the sub-routine call graph — DONE (2026-07-18).** New pure-Core `SubroutineCatalog` + an optional
  arg to `ReadWriteSetAnalyzer.Analyze`: a statement that calls an in-scope local sub-routine folds in that
  callee's transitively-captured variables (span-collected from `model.References` — reuses the binder, rule #2;
  call detection is a conservative name-membership check against the AST catalog), keeping only those in scope at
  the call site, added to both reads+writes (over-inclusion §F-safe). Lab +`SP_DBG_CLOSURE_FN`/`SP_DBG_CLOSURE_OVER`;
  probe **sim `TOTAL=15` == real** for a local function and a local procedure stepped OVER with a hidden capture.
  Build 0/0; **4856 green**; smoke clean. **🏁 D9 CORE IS COMPLETE — local routines are real, steppable frames
  with real closure variables (procedure step-into + step-over faithful).**
- **STATUS — seam (c) DESIGNED, NOT IMPLEMENTED (2026-07-18). The immediate next task, before D10.** Manual QA
  found the one asymmetry: **Step Into works for a local *procedure* but a local *function* runs whole and
  returns (effectively Step Over)** — a complex local function's body can't be traced line by line. Not a
  correctness bug (§F is intact) but the last usability gap in the flagship. Full design + handoff:
  [docs/history/19-...](../history/19-firebird-debugger.md) §"D9 seam (c)". Brief below.

#### D9 seam (c) — Step Into a local FUNCTION 🏁 *(closes the flagship, before D10)*

- **Cel.** Let Step Into descend into a local **function**'s body — but only where it needs **no** client-side
  expression evaluation, so the responsibility split (client = control flow, server = semantics) is untouched.
- **Root cause.** A procedure call is a *statement* (a step point the interpreter owns); a function call is an
  *element of a server-evaluated expression*. Stepping into a function in the general case would force the
  client to become an expression evaluator (§F / Contract #3 forbids it). **The general case is a permanent §F
  boundary, not a gap.**
- **Ratified principle (final).** *Step Into descends into a local function only when the call is the ENTIRE
  operand of a value-consuming position, so the client never evaluates an expression around it.* **Variant A —
  ONE mechanism covering all four positions, not split into stages:**
  `v = f(args)` · `RETURN f(args)` · `IF f(args) THEN` · `WHILE f(args) DO`. **Excluded** (require expression
  decomposition ⇒ step-over): `f(x)+1`, `f(x)=5`, `f` as a sub-operand, `a AND f(x)`, `INSERT … VALUES(f(x))`,
  any proper-sub-expression position. Boundary is architectural, not syntactic. The function's **arguments** may
  be arbitrary (server-evaluated during seeding).
- **Zakres / decisions (all ratified — closed).**
  1. Small closing seam. **No §F violation, no expression evaluator, no new SERVER path.**
  2. Reuse ONLY: **Statement Harness**, **Expression Harness**, **`SeedInputParametersAsync`**,
     **`SetResolvedValue` / `ApplyReturningValues`**, `Frame`, `LexicalParent`, closures.
  3. **No mini-harness / delivery harness.** The return value `r` is delivered **client-side** via
     `SetResolvedValue` — the same primitive procedures use for `RETURNING_VALUES`. (A delivery harness would be
     a second delivery path *and* re-validate the target's domain mid-flight, contradicting R2.)
  4. `RETURN <expr>` in a stepped function uses the **existing Expression Harness** (result column typed as the
     function's `RETURNS` base type, R2). **Never** route a `RETURN` leaf through the Statement Harness — it is
     invalid inside `EXECUTE BLOCK`.
  5. **Function Return Continuation** — a generalisation of `ApplyReturningValues`: "the caller statement is
     paused pending the callee function's return, then consumes it per the call position." Procedures become the
     `RETURNING_VALUES` special-case; both fire at the callee frame's normal return in `AdvanceToNextStepPoint`.
  6. Small AST deepening (**Contract #1** — structure in the tree; token-scan rejected).
- **New.**
  - Core AST: `CallExpression` (`Name` + `Arguments : IReadOnlyList<CallArgument>` — reuse D8's record) in
    `Ast/ExpressionNodes.cs`; additive props set **only** on strict lone-call recognition:
    `PsqlLeafStatement.RhsCall` / `.AssignmentTarget`, `IfStatement.ConditionCall`, `WhileStatement.ConditionCall`.
  - Core `PsqlDeclarationExtractor`: `SubroutineSignature.ReturnType` (a local function's `RETURNS` type spec; R2).
  - Core `Sql/Language/Debugging`: `FunctionReturnContinuation` (variants `AssignTo` / `SetFrameReturn` /
    `BranchIf` / `DecideWhile`); `Frame.ReturnValue` / `.ReturnType` / `.ReturnContinuation`.
  - Seam + Firebird impl: `IDebugExecutor.ResolveFunction(CallExpression, Frame) : DebugRoutine?` (mirrors
    `ResolveRoutine`; null ⇒ step-over) and `IDebugExecutor.EvaluateReturn(returnStatement, Frame)` (refactor
    `EvaluateCondition` into a private typed-expression evaluator with two public entry points).
- **Mod.** `DebugSession.ExecuteCurrent` / `AdvanceToNextStepPoint` (step-into recognition + generalised
  delivery); `SeedInputParametersAsync` generalised to take `IReadOnlyList<CallArgument>`;
  `FirebirdDebugMetadata` (function `RETURNS` base-type derivation). **`HarnessBuilder` — NONE.**
  **`DebugSessionConnection` — NONE. No new server round-trip type.**
- **Dep.** D8 (`CallArgument`, arg seeding, `LexicalParent`), D9 seam (a)/(b).
- **Ryzyka / danger zones.**
  - A `RETURN` leaf in a function frame **must** go through `EvaluateReturn` (Expression Harness), never the
    Statement Harness (`RETURN` is invalid inside `EXECUTE BLOCK`).
  - Do **not** advance the caller sequence / decide the branch when pushing the function frame — the
    continuation owns that on the callee's normal return (else it fires twice / out of order).
  - The continuation fires **only** on normal return; an exception unwinds via `ExceptionRouter` (savepoint
    rollback) and must not run it — identical to procedures.
  - Parser recognition must be **strict**: under-recognise (step-over) rather than over-recognise. A recognised
    shape the interpreter cannot deliver cleanly is a bug.
  - Firebird PSQL assignment is `=`, **not** `:=`.
  - A function frame that completes with no `RETURN` on its path → surface a `DebugError` (Firebird's "function
    returned no value"); never silently deliver null.
- **DoD.** Step Into a local function in each of the 4 positions; it appears as a real frame (Call Stack,
  breadcrumbs, Variables, simulated △); its body steps line by line; `RETURN` computes via the Expression
  Harness; the value is delivered to the caller position; a raising function unwinds without firing the
  continuation. All other shapes step over, 100% faithful.
- **Weryfikacja.** **Lab-mandatory (§2.1):** a local function with a multi-statement body (incl. a closure
  capture) exercised in each of the 4 positions — `DebuggerFidelityProbe` extended, **sim == real**. Rebuild
  `Lab/EmberTern_Lab.fdb` at an ASCII temp path then copy (#149).
- **Sesje: 1** (sub-steps c1 AST → c2 Core interpreter (fake executor) → c3 Firebird executor + live fidelity;
  optional c4 UI polish). Each ends build 0/0 + green tests + smoke + committable. Full sub-step detail:
  history §"D9 seam (c)" → "Implementation plan".
- **STATUS.**
  - **c1 — AST only (pure Core) — DONE (2026-07-19).** New `CallExpression` (`Ast/ExpressionNodes.cs`:
    folded `Name` + reused D8 `CallArgument` spans; not a tree child — an additive overlay referenced by
    typed props). Additive props set by strict parser producers (`SqlParser.Psql.cs`): a leaf's `RhsCall` +
    `AssignmentTarget` (assignment whose whole RHS is a lone call, single bare target only — `NEW.col` left
    for D10) and `RhsCall` for a `RETURN` operand; `IfStatement.ConditionCall` / `WhileStatement.ConditionCall`
    (the whole header condition, one enclosing paren pair stripped first). Recognition is **strict** — a
    trailing operator / second call / dotted callee / proper sub-expression leaves the prop null ⇒ step-over
    (shared helpers `TryReadLoneCall` / `TryReadConditionCall` / `ReadLeafCall`, reusing the D8
    `ReadCallArgumentList` + `MatchParenTok`). `PsqlDeclarationExtractor.ExtractSignature` gained
    `SubroutineSignature.ReturnType` (a local function's single `RETURNS` type spec, stopping before the
    header `AS` — the R2 input for c3's Expression Harness result column). **Producer-only** — `CallExpression`
    is not walked by any consumer yet (`ResolveFunction`/`EvaluateReturn` are c2/c3), the deliberate staged
    boundary (gotcha #233 — a tested-but-uncalled component; the consumer + surface assertion arrive in c2/c3).
    Additive; §0 round-trip unchanged; binder/formatter untouched. +19 `PsqlAstTests` (4 positions + no-arg +
    nested-arg + quoted + every excluded shape + round-trip), +3 `PsqlDeclarationExtractorTests` (function
    `ReturnType`: scalar / parametrised / domain / null-for-procedure), +2 corpus shapes. Build 0/0; tests
    green (targeted 402 green; full suite hangs in this env #94/#226 — user-verified green); smoke clean.
  - **c2 — Core interpreter (fake-executor-driven) — DONE (2026-07-19).** New internal
    `FunctionReturnContinuation` (variants `AssignTo`/`SetFrameReturn`/`BranchIf`/`DecideWhile`) with its
    `RecognizeStepInto` factory — **the single concentration point** the user asked for: the interpreter's
    step-into decision (is this a lone-call value-consuming position, and which continuation consumes the
    return) lives in ONE place, not scattered across the IF/WHILE/leaf cases. `Frame` gained
    `ReturnType`/`ReturnValue`/`ReturnContinuation`/`IsFunctionFrame` + `SetReturnValue`/`TerminateForReturn`.
    `IDebugExecutor` gained `ResolveFunction` + `EvaluateReturn` (+ `ReturnOutcome`, + `DebugRoutine.ReturnType`).
    `DebugSession.ExecuteCurrent` got two guarded branches — (1) step-into a resolved local function (push a
    function frame carrying the continuation; caller control flow untouched until return), (2) a `RETURN <expr>`
    in a function frame → `EvaluateReturn` (Expression Harness) → terminate the frame; and
    `AdvanceToNextStepPoint` got `ApplyReturnContinuation` — the ONE delivery switch generalising
    `ApplyReturningValues` (AssignTo writes+consumes the leaf; SetFrameReturn propagates to the caller's return;
    BranchIf/DecideWhile resume the caller's branch/loop with the returned boolean). A raised function unwinds
    via the `ExceptionRouter` and the continuation never fires. **`FirebirdDebugExecutor` got c2 stubs**
    (`ResolveFunction` → null, `EvaluateReturn` → throw) so **live behaviour is byte-identical to D9 core**
    until c3. +11 `DebugEngineTests` (each of the 4 positions + deliver, nested `RETURN f()`, IF then/else,
    WHILE iteration, unresolved ⇒ step-over, Step Over ignores the call, unresolved IF condition ⇒ server
    EvaluateCondition, a raising function ⇒ no continuation, plain `RETURN <expr>` ⇒ EvaluateReturn not
    Statement Harness). Build 0/0; targeted green (508); full suite hangs in this env (#94/#226) — user-verified.
    Smoke clean.
  - **c3 — Firebird executor + live fidelity — DONE (2026-07-19).** `FirebirdDebugExecutor.ResolveFunction`
    walks the lexical chain (generalised `TryFindLocalRoutine(name, frame, kind)`) for a local `DECLARE
    FUNCTION`, builds its frame from the already-parsed AST body via `BuildLocalFunctionAsync` (no source
    fetch), seeds args through the **shared** `SeedInputParametersAsync` (generalised to `(arguments, callTokens,
    callStart, …)` — Contract #4), sets `LexicalParent` by the §6.3 gate, and carries the `RETURNS` base type.
    `EvaluateReturn` computes the `RETURN` operand via the Expression Harness typed as `frame.ReturnType`,
    sharing a new private `EvaluateExpression` with `EvaluateCondition` (one server path). `FirebirdDebugMetadata`:
    `DebugFrameLayout.ReturnType` derives the function's `RETURNS` base type (R2, reusing `ResolveBaseTypeAsync`).
    **Live fidelity (spec §15.11, `DebuggerFidelityProbe` cases 8–11 + re-pointed 4/6):** four positions (`=`/
    `RETURN`/`IF`/`WHILE`, depth 3), six return types (INTEGER/BIGINT/NUMERIC/VARCHAR/BOOLEAN/NULL), shadowing
    (a local shadows the stored `FN_ADD_TAX`), and a closure — **all sim == real**. Lab zoo +`SP_DBG_FN_POS`/
    `_TYPES`/`_SHADOW`/`_CLOSURE`; `.fdb` rebuilt (#149). **Live-verified constraint: Firebird forbids nested
    sub-routines** (gotcha #244) — lexical-level shadowing is not expressible, so the shadow test is local-vs-global.
    Build 0/0; 508 Core tests green (no regression — c3 is Firebird/metadata only); smoke clean.
  - **c4 — optional UI polish** (step-into cue / synthetic RETURN row) — not required to close the seam; deferred.
  - **🏁 D9 IS COMPLETE — local procedures *and* functions step faithfully, into and over. NEXT: D10 (Triggers).**

---

### D10 — Triggers

- **Cel.** Debug a trigger body with user-supplied `NEW`/`OLD`. Spec §8.1.
- **Zakres.** Action selector, NEW/OLD editor + availability rules, span-based substitution, seed-from-row.
- **New.** `TriggerContextEditorViewModel`/`View`, `ContextSubstitution` (shared with §3.6's handler
  context).
- **Mod.** `HarnessBuilder` (substitution), launch panel.
- **Dep.** D4, D7.
- **Ryzyka.**
  - ⚠ **Substitute by resolved `SymbolReference` span — never text search.** A textual `NEW.X` rewrite
    corrupts string literals, comments and quoted identifiers. The binder already produced the spans
    (`RecordAliasSymbol`, `TriggerPredicateSymbol`).
  - **One substitution engine**, shared with the handler error context (`GDSCODE`/`SQLSTATE`/`RDB$ERROR`).
  - Availability table (spec §8.1) is engine truth: `OLD` always read-only; `NEW` writable only in BEFORE.
  - DB-level/DDL triggers **out of scope** — refuse clearly.
- **DoD.** All trigger kinds launch with correct context availability; multi-action triggers drive the
  predicates; seed-from-row works.
- **Weryfikacja.** Lab: the 3 existing triggers; simulated vs real (compare the body's *effects*, since the
  DML itself is not performed).
- **Sesje: 2.**

---

### D11 — Packages

- **Cel.** Debug packaged routines, public and private. Spec §8.2.
- **Zakres.** Public sibling calls (real/step-into); private routines (interpret — not callable from DSQL).
- **Mod.** Frame source fetch; possibly `PackageSourceScanner` (**existing** — check it before writing a
  package-body parser).
- **Dep.** D8, D9.
- **Ryzyka.**
  - ⚠ **Probes block this:** is a private package routine callable from an `EXECUTE BLOCK`? Is
    private-routine source extractable from the body blob? The lab has **no private routine**
    (`RDB$PRIVATE_FLAG = 0` on both) → **extend `Lab/setup.sql` first**.
  - A package body is **one source blob** — extracting an individual routine is real parsing, not a
    lookup. Reuse `PackageSourceScanner`/the AST; do not hand-roll a scanner.
- **DoD.** Step into public and private package routines; both appear as frames.
- **Weryfikacja.** Lab (extended with a private routine); simulated vs real.
- **Sesje: 1–2.**

---

### D12 — Advanced breakpoints — ✅ COMPLETE + user-confirmed (2026-07-20)

- **Status.** DONE. All seams shipped (0 / A / B / C1 / C2 / D / E1 / E2 + QA fixes), live-fidelity-proven
  (`DebuggerFidelityProbe` 26/26 sim==real on FB5). Final architecture: `Breakpoint` is a domain stop-policy
  object; `HitCountPolicy`; `DataBreakpoint`; Run-to-`SUSPEND` run mode; Break-on-Exception is a pause-before-
  routing; a condition is an expression through the ONE D5 evaluation engine; ONE breakpoint model shared by the
  VM and `DebugSession`; ONE decision point "before executing a statement" (`TryStopBeforeExecuting` — the
  first-statement-skipped QA fix). Narrative: `docs/history/19-firebird-debugger.md` (D12 section).
- **Cel.** Cheap, high-value additions *given* the engine. Spec §9.8.
- **Zakres.** Break on exception; conditional breakpoints + hit counts; data breakpoints; **run to next
  `SUSPEND`** (+ its result grid).
- **Mod.** `BreakpointSet`, `StepPlanner`, `ExceptionRouter`, Breakpoints panel.
- **Dep.** D1, D5 (conditions use the evaluation engine), D12's grid reuses the existing grid.
- **Ryzyka.** A breakpoint condition is **just an expression** → §9.5's engine. No second evaluator.
  Conditions can have side effects — same flagging rule as watches.
- **DoD.** All four modes work; conditions evaluate in the correct frame.
- **Weryfikacja.** Core unit tests (fake executor) + lab for `SUSPEND`.
- **Sesje: 2.**

---

### D13 — Fast-forward *(optional)*

- **Cel.** Make loops survivable. Spec §12.1.
- **Zakres.** Block fusion for breakpoint-free regions; prepared-statement caching.
- **Dep.** Everything. **Never earlier** — an optimisation over a *trusted* interpreter.
- **Ryzyka.** Error line mapping degrades to block-relative (`At block line: 3, col: 3`); `SUSPEND` inside a
  fused region needs care. **Must degrade to per-statement stepping the instant a breakpoint lands
  inside.** Opt-in.
- **DoD.** A large loop completes in reasonable time; fidelity unchanged; breakpoint inside → automatic
  de-fusion.
- **Weryfikacja.** Lab: a loop routine, fused vs stepped vs real — identical results.
- **Sesje: 2.** **Build only if real usage asks.**

---

### D14 — Step back via savepoints *(optional)*

- **Cel.** Bounded reverse stepping. Spec §9.8.5.
- **Zakres.** One savepoint per **step** (vs §4.5's per **frame**) + client frame snapshots; bounded
  history.
- **Dep.** Everything.
- **Ryzyka.** Memory (bound the history; auto-disable in loops/fast-forward). **Cannot** undo
  `IN AUTONOMOUS TRANSACTION`, generator increments, `EXECUTE STATEMENT ON EXTERNAL`, or side-effecting
  UDFs — the UI must say so, not imply otherwise.
- **DoD.** Step back restores DB state + frame within the bound; limits surfaced.
- **Weryfikacja.** Lab: step forward/back over DML, compare state.
- **Sesje: 2.** **Build only if real usage asks.**

---

## 3. Danger zones — do not break these

### 3.1 The editor architecture

| Zone | Rule |
|---|---|
| ~~**Dual wiring** (until D3)~~ **RESOLVED by D3** | *Was:* `SqlEditorBehavior.Attach` served object editors; `MainWindow` hand-wired the main SQL editor, so a capability added to one silently missed the other (how S3 shipped with no squiggles). **D3 consolidated the intrinsic block into one `SqlEditorBehavior.Attach` seam** — `MainWindow` now calls it too, at VM-arrival. New editor-intrinsic capabilities go in **one** place. Per-host wiring (`DiagnosticsPanelHost.Track`, `AmbientModelRefresh`, `SqlSnippetDropTarget`) remains a caller responsibility by design. *(#219 — resolved; `docs/history/19`)* |
| **Headless tests** | **ONE `HeadlessUnitTestSession` per process.** Add probes to the existing `ConnectionExpandBindingProbe` class; never `StartNew`. AvaloniaEdit's `KeyBinding` lists are **static** and owned by the first session's thread — any later session throws cross-thread. *(#94/#226)* |
| **Focus** | `TextEditor` is **not focusable**; `editor.Focus()` is a no-op returning `false`; `IsFocused` is always false. Focus lives on `editor.TextArea`. *(#225)* |
| **Repaint** | Use `TextView.Redraw()`, not `InvalidateVisual()` — the latter can run before visual lines exist and a diff-guard makes the miss permanent. *(#223)* |
| **Dispatcher** | Do **not** post caret+selection at `Background`; `Input` outranks it. Caret+selection synchronously; scroll+focus posted. *(#221)* |
| **Semantic model** | The binder is the **one** name resolver. Never add a second resolver, never re-scan tokens for something the model already knows. Ambient symbols are the seam for out-of-text params/vars *(#218)*. |
| **AST §0** | The token stream is lossless and the AST is a structural overlay. Anything unrecognised → the existing valve (`PsqlLeafStatement` / `RawStatement`). P1 must keep the formatter **byte-identical**. |
| **Formatter** | Do **not** touch `SqlFormatter` for the debugger. Grammar depth is built only when a feature needs it; the debugger needs *reading*, not layout. |
| **VM naming** | `Diagnostics` already resolves to the `EmberTern.App.Diagnostics` **namespace** inside `MainWindowViewModel` — hence `DiagnosticsPanel`. Expect the same class of collision for debugger VMs. |
| **Tab-active chain** | New tab-kind chrome gates on the `IsXxxTabActive` computed properties and their `NotifyPropertyChangedFor` chain — not a new event *(#25)*. A command gated on a computed value needs explicit notification on **every** mutation path *(#179/#187)*. |
| **Theme** | Every colour is a token in **both** dictionaries; consume via `{DynamicResource}`; VMs hold **key strings**, never brushes. Zero hardcoded colours. |

### 3.2 Transactions & connections

| Zone | Rule |
|---|---|
| **The user's transaction** | The debugger **never** touches the Data lane. `TransactionService.BeginTransactionAsync` early-returns on an active tx — it is a **join**, not a create *(#230)*. A debug rollback on the Data lane would destroy the user's uncommitted work (**rule #11**). |
| **TPB** | Never a bare `IsolationLevel`; always explicit `FbTransactionOptions` *(#85)*. |
| **Locking** | Interleaving is fine; **concurrency is not** *(#236)*. Per-wire-operation locking on the session connection. Capture the lock object **once** per acquire/release pair *(#98/#120)*. |
| **No routing by statement kind** | A classifier may decide whether to refresh the UI; it must **never** decide where/how a statement executes *(#215)*. |
| **Lanes** | Do **not** add `ConnectionRole.Debug`. A session connection is not a lane (decision 5). |

### 3.3 Fidelity (§F)

Uncertainty ⇒ **stop and explain**. Never guess, never silently `NULL`, never "approximate for now".
Every boundary in spec §12 is detected and surfaced where possible. **A boundary nobody looked for is
indistinguishable from a boundary that does not exist** (spec §2.1) — so when in doubt, **probe the
engine**, don't reason about it.

---

## 4. Developer Contract

**Binding for every debugger implementation session. If a task seems to require breaking one of these,
stop and raise it — do not work around it.**

**Architecture**
1. **Never re-parse SQL.** Consume `SqlParser`/the AST. If you are scanning tokens for structure, the AST
   is missing a node — deepen it (P1-style, per Etap 6.9's contract), don't scan.
2. **Never duplicate `SemanticModel` logic.** Scopes, symbols and references have one owner. No second
   resolver, no name-based fallback.
3. **Never re-implement Firebird semantics client-side.** No expression evaluator, no type system, no
   collation logic. **The server owns semantics — always.**
4. **The harness is the only server path.** Every step, condition, watch, evaluation goes through
   `HarnessBuilder`. No second execution route, no "quick direct query".
5. **No alternative execution paths.** One interpreter, one executor, one cursor mechanism. If two code
   paths can produce a value, they will diverge.
6. **No temporary metadata. Ever.** Not packages, not tables, not GTTs. If a design needs it, the design
   is wrong (spec §3.8).
7. **Core stays pure** — `EmberTern.Core.Sql.Debugging`: zero Avalonia, zero `FirebirdSql`.
8. **Rule #2 holds.** `IDebugExecutor` is the **one** precedented interface exception
   (`ISqlMetadataProvider`'s pattern). Do not add another on that argument without review.

**Fidelity**
9. **§F outranks features.** Uncertain ⇒ stop and explain. Never a guessed value, never a silent `NULL` —
   that is precisely IBExpert's failure.
10. **Never modify the user's routine.** Rewriting happens **only** inside a generated harness, **only** by
    resolved `SymbolReference` span, and **never** written back to the database (**rule #11**).
11. **Verify Firebird behaviour; never infer it.** Every ⚠ in spec §1.4/§14 **blocks its milestone**. Probe
    against the lab (a copy at an ASCII path — `isql` cannot reach the repo path, #149) and **record the
    result in spec §15**.
12. **Fidelity is proven against real execution**, not against the interpreter's self-consistency. Every
    milestone touching engine behaviour compares simulated vs real on the lab.

**Process**
13. **One milestone per session**; end green (build 0/0 → tests → smoke), committable, docs updated.
    **Never chain build+test in one command** — they deadlock.
14. **Do not pre-build future milestones.** Scope is the brief. Questions about later milestones go in the
    docs, not the code.
15. **Never silently change the frozen design.** If implementation reveals the spec is wrong — as the v1
    review did four times — **stop, report, get a decision, update the spec.** That is the process working,
    not a failure.
16. **No transitional names.** No `V2`, `NewX`, `Temp`, `Parser2`. Consolidate to the responsibility name
    the moment the old implementation dies.
17. **Reuse before create.** Check `ControlStyles.axaml`, existing VMs/views, existing behaviors, existing
    tokens. Parallel implementations drift and double the surface.
18. **Not "fixed" on green alone.** If it can't be verified visually in the running app, report
    "implementation done — awaits user confirmation". Trace to ground truth; don't guess.
19. **Assert at the surface.** A unit test proves a rule exists; only a test at the surface proves it is in
    force. If a component's whole value is being *called*, assert that it is called *(#233)*.
20. **Delete the workaround you replace.** When a milestone makes an earlier hack redundant, remove it —
    don't leave a compatibility layer.

---

## 5. Quick reference

| Need | Where |
|---|---|
| Behaviour authority | `firebird-debugger.md` (spec v2) |
| Why a boundary exists | spec §12 + §15 (verification log) |
| Decisions + rationale | spec §0 |
| What blocks a milestone | spec §14 + this file's briefs |
| Editor rules | this file §3.1 + `editor-architecture.md` |
| Gotchas | `docs/gotchas.md` (#219 wiring, #236 locking, #237 exception atomicity, #94/#226 headless) |
| Lab | `Lab/setup.sql` — **extend it**, never create throwaway DBs |
