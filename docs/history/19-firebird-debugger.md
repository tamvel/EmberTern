# Firebird Debugger — implementation history

The narrative "as-built" record of the **Stage X — Firebird Debugger** arc. The *what/why* authority is
[`docs/design/firebird-debugger.md`](../design/firebird-debugger.md) (spec v2); the *order/rules* are in
[`docs/design/firebird-debugger-implementation-plan.md`](../design/firebird-debugger-implementation-plan.md).
This file is the diary of how each milestone actually landed.

---

## P1 — AST: exception handlers (2026-07-17)

**Goal.** Make `WHEN … DO` readable from the AST. The interpreter (D1) owns exception control flow — like
`IF`/`WHILE`, it is client-owned — but the tree gave it nothing to read: handlers were an unstructured
`PsqlLeafKind.Other` token bag. P1 is a pure **parser-producer → binder-consumer** deepening, additive
only, following Etap 6.9's contract (formatter convergence deferred — build grammar depth only when a
feature needs it).

### The one spec refinement (decision 3, ratified by the user)

The brief prescribed a `WhenHandler` with a **single** `WhenHandlerKind` per node. Reading the Firebird
grammar showed that a single `WHEN` may list **several** conditions, comma-separated, sharing one `DO`
body:

```sql
WHEN GDSCODE grant_obj_notfound, GDSCODE grant_fld_notfound, EXCEPTION my_exc
DO BEGIN … END
```

A single kind cannot represent that (all conditions share one body, so they cannot be separate nodes).
Per Developer Contract #15 ("never silently change the frozen design — stop, report, get a decision"),
implementation halted and the question was raised. The user chose to model the real grammar: **a
`WhenHandler` holds one `WHEN` clause with an ordered list of conditions**, and D1's `ExceptionRouter`
matches them in declaration order (conditions within a clause left-to-right, clauses top-to-bottom). Spec
§3.6 + the decision log + the P1 brief were updated to record this before coding resumed. This is a model
refinement to match Firebird faithfully, not a debugger-architecture change.

### What was built

**AST (`Ast/PsqlNodes.cs`).**
- `WhenHandlerKind` — `Any` / `ExceptionName` / `GdsCode` / `SqlCode` / `SqlState`.
- `WhenCondition : SqlNode` — one condition (kind + its tokens); an `ExceptionName` condition surfaces the
  folded user-exception name (`ExceptionName`), the other kinds keep their operand only in `Tokens`.
- `WhenHandler : PsqlStatement` — one `WHEN … DO` clause: an ordered `Conditions` list + a `Body`
  (`SqlNode?`). `Children` = conditions then body (source order). Deliberately **not**
  `IExecutableStatement` — the clause is control-flow routing; its body statements are the step points.
- `BlockStatement.Handlers` (`IReadOnlyList<WhenHandler>`), added to `Children`. `Children` is built by a
  two-pointer **merge** of `Statements` + `Handlers` by source position (not a concatenation): in
  well-formed PSQL every statement precedes every handler, but a malformed trailing `WHEN` (a lossless
  `Other` leaf that lands in `Statements`) can interleave, and the well-formedness invariant
  (`StructuralAstDifferentialTests`) requires non-decreasing child order.

**Parser (`SqlParser.Psql.cs`).** `ParsePsqlBlockBody`'s three unit loops now route through
`ParseBodyUnit`, which sends a recognised `WHEN … DO` clause to `handlers` and everything else (statements,
and malformed/unrecognised `WHEN`s) to `statements`. `ParseWhenHandler`:
- Finds the clause's `DO` via `FindWhenDoIndex` — the first paren-depth-0 `DO` before the next depth-0
  `WHEN`/`END`/end-of-input (so a following clause's `DO` is never mis-attached).
- Parses the condition list (`ParseWhenConditions` → comma-split → `ParseOneWhenCondition`), recognising
  each condition **strictly by its leading keyword** (never guessed from text).
- Parses the body via `ParsePsqlUnit` (so a `BEGIN … END` body, a single leaf, or a reused DSQL statement
  all work).
- Falls back — exactly like `ParsePsqlIf` on a missing `THEN` — to a lossless `PsqlLeafStatement`
  (`Other`) when there is no `DO`, an empty condition list, or an unrecognised condition keyword. A `WHEN`
  at a body-unit position can only be an exception handler (a CASE/MERGE `WHEN` lives inside a leaf/DSQL
  statement the leaf collector consumes whole), so this is unambiguous.

**Binder (`SemanticBinder.Psql.cs`).** `BindBlock` now also iterates `block.Handlers`. `BindWhenHandler`
references each `EXCEPTION <name>` condition as a `SchemaObject` (resolved when metadata knows it, else a
plain unresolved occurrence — error-tolerant; the other condition kinds carry no schema reference) and
binds the handler body against the **enclosing** scope (Firebird PSQL has no block-local scopes — the one
`RoutineBody` scope is the whole body's, the simplification the rest of the binder already relies on). A
handler body that is itself a block recurses through `BindBlock`, so a nested handler section binds too.

### §0 / formatter byte-identity

`SqlFormatter` was **not touched**. Its PSQL layout is token-based (`EmitPsqlUnit` walks tokens; a
`WHEN` clause falls through to `CollectPsqlStatement`), and no existing test/corpus input contains a
`WHEN` handler, so no existing formatting changed. The only coupling — `BuildLeafIndex` — is keyed by a
collected statement's first-token start; a `WhenHandler` is not an indexed leaf type, so a handler clause
now takes the pure token-layout path. New handler shapes added to `SqlTestCorpus.StructuralConstructs` are
held to the formatter's idempotency + §0 token/comment-preservation invariants (guaranteed by the
formatter's per-statement lexeme net regardless), and to the B0 differential harness's byte-for-byte
round-trip + tree well-formedness. All green.

### Tests

- `PsqlAstTests` (+11): each handler form (ANY / EXCEPTION / GDSCODE / SQLCODE / SQLSTATE), the
  multi-condition clause (ordered conditions + the trailing `EXCEPTION` name), multiple clauses in
  declaration order, a block body, and three fall-back cases (no `DO`, empty condition list, unrecognised
  condition keyword) — each asserting the clause is **not** a handler and the round-trip stays byte-exact.
- `SemanticModelTests` (+4): the `EXCEPTION <name>` reference resolves against a fake catalog / stays an
  unresolved occurrence when unknown; a multi-condition `WHEN` references each `EXCEPTION` name (but not a
  `GDSCODE` operand); a handler body's local variable resolves to the routine's `DECLARE`.
- `SqlTestCorpus.StructuralConstructs` (+6 handler shapes) feeds the differential + formatter invariant
  suites.

**Test-writing note (not a numbered gotcha — too minor):** a malformed `WHEN` clause that falls back to
`ParsePsqlLeaf` gets its `PsqlLeafKind` from `ClassifyLeaf`, which returns `Assignment` whenever the leaf
contains a top-level `=` (e.g. `when do x = 2;`). The `Kind` of a fallback leaf is therefore incidental —
assert **"not a handler" + lossless round-trip**, never `Kind == Other`, for a malformed-WHEN test.

### Verification

Build 0 warnings / 0 errors. Tests run in two partitions to sidestep the documented full-suite hang
(#94/#226): **4612** green with `ConnectionExpandBindingProbe` excluded, then that class **alone** green
(27) — 4639 total, all green. Smoke: app launches cleanly. No live-engine (lab) work was needed — P1 is
pure AST/binder structure over the parser, and the multi-condition `WHEN` grammar is documented Firebird
syntax (stated as such in the session; the SYSDBA password for a live probe was not available, and
`isql` cannot reach the repo path anyway — #149).

Committed `590b220`.

---

## P2 — Server version gate (FB3+) (2026-07-17)

**Goal.** Refuse a pre-FB3 server on connect with a legible message (decision 8, spec §1.3). App-wide,
deliberately **outside** the debugger's own milestones. Free in the sense that it removes nothing that
works: `FirebirdSql.Data.FirebirdClient` 10.3.4 is **Srp-only**, and FB2.5 authenticates only via
Legacy_Auth, so FB2.5 is *already* unreachable — today it surfaces as a confusing auth failure; the gate
turns that into a clear refusal.

### What was built

`FirebirdConnectionService`, two additions (both `internal static`, so the test project can pin them
without a live server):

- `IsSupportedServerVersion(string? serverVersion)` — reuses the app's **one** version parser,
  `FirebirdDdlReader.ParseServerMajor` (do not add a second parsing site). It **fails open** on an
  unparseable version: `ParseServerMajor` returns `0` for a string it cannot read, and a
  *successfully-opened* connection is FB3+ by construction (the driver only speaks Srp, introduced in
  FB3), so `0` must not produce a false rejection. It rejects **only** a positively-identified pre-FB3
  major (1 or 2). Note: `ParseServerMajor` parses the **full** driver `ServerVersion`
  (`"WI-V5.0.0.1306 Firebird 5.0"`), not a bare `"5.0.3"` — the tests table-drive realistic strings.
- `UnsupportedServerMessage(string? serverVersion)` — the refusal text, naming the required version
  ("EmberTern requires Firebird 3.0 or later") and echoing the detected server verbatim.

The gate runs on both open paths:
- `ConnectAsync` — right after the **first** (Data) attachment opens, **before** the Metadata/Ddl lanes.
  All three attach to the same server, so gating the first covers all three, and we never open extra
  attachments to an unsupported server. On refusal the connection is closed cleanly (`CloseAndDisposeAsync`)
  before throwing `ConnectionFailedException` — no half-open attachment.
- `TestConnectionAsync` — the same check, so the "Test" button refuses a pre-FB3 server rather than
  reporting a bare success. The `await using` disposes the connection either way.

### Two decisions worth recording

1. **Precondition, not error interpretation.** The documented rule *"connection errors show the raw server
   message — never interpret"* stands untouched; `MapErrorMessage` is unchanged. The gate is a check on a
   fact we know for certain (`ServerVersion`) on an **already-open** connection — it runs before/independently
   of any server error.
2. **The message lives in the Firebird layer, not `UiStrings`.** The P2 brief listed `UiStrings (the
   message)`, but `EmberTern.Firebird` cannot reference `EmberTern.App` (layering: App → Firebird, never
   the reverse). Connection-failure messages already live in the Firebird layer (`MapErrorMessage`), so the
   refusal message goes beside it — consistent with the established pattern, zero behavioural/design impact.
   Flagged in the session, not a design change.

### Verification

- `FirebirdConnectionServiceTests` (+13): a `[Theory]` table-driving `IsSupportedServerVersion` over
  realistic `ServerVersion` strings (FB1.5/FB2.5 → rejected; FB3/4/5 → allowed; empty/null/garbage →
  fail-open), plus two message tests (names FB3.0, echoes the detected server / stays readable on null).
- **Live rejection is unverified** — there is no FB2.5 instance to point at, and (per Developer Contract
  #11/DoD) this is stated honestly rather than claimed. The predicate is table-pinned; the FB3/4/5 path is
  behaviourally unchanged (FB5 ⇒ allowed ⇒ the connect flow is exactly as before).
- Build 0/0. Tests: **4652** green in two partitions (4625 non-probe + 27 `ConnectionExpandBindingProbe`
  alone, #94/#226). Smoke: app launches cleanly.

**Follow-up (not urgent, per the brief):** the existing `serverMajor >= 3` catalog gates (e.g.
`StandalonePackageFilter`, the FB5 `RDB$` column gates) are now statically true and could be simplified in
a later cleanup.

---

## D1 — Debug engine core, seam (a) (2026-07-17)

**Goal.** The interpreter of PSQL **control flow** — frames, scopes, stepping — proven with **zero server
in the loop**. Control flow is the part the client owns (spec §3.1), therefore the part we can get wrong,
therefore where tests are cheapest and most valuable. D1 is a **two-session milestone**; this session is
the confirmed seam (a): frames + FrameValues + scopes + `StepPlanner` + the `DebugSession` state machine +
the savepoint-on-entry model + a full test suite. Seam (b) — `ExceptionRouter`, unhandled-exit rollback,
breakpoints — is the next session.

### The design: an explicit resumable control stack

The interpreter is a small VM. Each `Frame` holds a control stack of resumable **activations**
(`SequenceActivation` for a block / branch, `WhileActivation`, `ForActivation`) — the frame's
"instruction pointer". Two operations compose it:

- **Navigation** (`Frame.NextStepPoint`) — *pure, no server*: descends into nested blocks, pushes the loop
  activations, pops completed sequences, and returns the next `IExecutableStatement` step point (a leaf,
  an `IF`, or a loop header). A nested `BEGIN…END` is structural (not a step point); `IF`/`WHILE`/`FOR`
  headers and leaves are step points.
- **Execution** (`DebugSession.ExecuteCurrent`) — the *only* place the server (executor) is touched:
  evaluate an `IF`/`WHILE` condition and push the taken branch/body; open a `FOR` cursor and fetch a row,
  applying the `INTO` writes and pushing the body; run a leaf and apply its write-back. A `WHILE`/`FOR`
  header re-presents itself as the step point each iteration (the activation stays on the stack).

This split is what makes "every step decision is a pure function of (AST, frames, command)" literally true:
navigation is deterministic structure-walking, and the stop decision is `StepPlanner.ShouldStop`, a pure
function. The server only ever answers "what did this statement do / what is this condition" — it never
drives the walk.

### Stepping

`Step(Into/Over/Out/Continue)`, `RunToCursor(offset)`, `SetNextStatement(offset)`. Into and Over both stop
at the next step point after one executed step; they differ **only** in how a call is handled: **Into**
`EXECUTE PROCEDURE` resolves a callee body (`IDebugExecutor.ResolveRoutine`) and pushes a **new frame**;
**Over** runs the call on the server in place (spec §5.3 — step-over is real execution, step-into is
simulation). Out runs (Over-style) until the starting frame returns. Continue runs to completion (seam b
adds breakpoints). Run To Cursor runs until the target step point. Set Next repositions within the
current/enclosing active block (a documented D1 limit: it cannot jump into a branch/loop not yet entered).

### Frames, scopes, savepoints

A `Frame` carries its `Body`, `FrameValues` (the client-side truth, case-insensitive), a lexical `Parent`,
a `CallSite`, and a `SavepointName`. The **scope chain** (`TryResolveValue`/`SetResolvedValue` walking
`Parent`) is the mechanism the flagship D9 (local routines as closures, spec §6.1) builds on — provided and
tested now, wired to local routines later. The **savepoint model is present from day one** (spec §4.5, the
brief's hard requirement): `EnterFrameSavepoint` on every frame push (root included), `LeaveFrameSavepoint`
on normal exit. The unhandled-exit `ROLLBACK TO` needs the `ExceptionRouter`, so it is seam (b); seam (a)
stops a raised statement at `DebugState.Faulted` (no routing).

### The one seam: `IDebugExecutor`

Every server interaction goes through this one contract (spec §3.3) — execute statement, evaluate
condition, open cursor, resolve routine, enter/leave savepoint. It is the **single precedented exception**
to Architecture rule #2 (Core declares the contract it needs, exactly as `ISqlMetadataProvider` does);
D2/D6/D8 implement it, D1 drives it with a scripted fake. The interpreter never evaluates an expression,
coerces a type, or decides a boolean.

### Tests

`DebugEngineTests` (+24) against a scripted fake executor: leaf step ordering; IF true/false/no-else;
WHILE re-evaluating its header per iteration; FOR iterating rows, applying `INTO`, closing the cursor,
and the no-rows case; nested-block-is-structural; step Into pushing a frame (with savepoint order asserted)
/ callee completing + releasing + caller resuming / step Over executing in place / unresolvable call
falling back / step Out; Continue; Run To Cursor; Set Next forward/backward/unreachable; raised → Faulted;
SUSPEND rows; write-back into frame values; and the scope chain resolving + writing an outer variable
through a real nested frame (no reflection — Core does not expose internals, so the test drives the public
session API).

### Verification

Build 0/0. Tests: **4676** green in two partitions (4649 non-probe + 27 `ConnectionExpandBindingProbe`
alone, #94/#226). Smoke: app launches (the engine is pure Core, not yet wired to any UI). No lab needed
(no server). Seam (b) parked, recorded in CLAUDE.md's "Current state".

## D1 — Debug engine core, seam (b) (2026-07-17)

The second half of D1: **exception routing** (the client-owned other half of control flow, spec §3.6) and
**breakpoints**. Still pure Core (zero Avalonia, zero FirebirdSql), still driven only through
`IDebugExecutor`, still proven with a scripted fake — control flow is the part we own, so it is where
depth is cheapest. Purely additive to `EmberTern.Core.Sql.Language.Debugging`; nothing outside the
namespace changed.

### `ExceptionRouter` — matching + propagation + unwind

`ExceptionRouter.TryRoute(frames, error, executor)` is the whole of exception control flow. When a
statement or a control-flow condition raises (the executor reports a `DebugError`), the router:

1. **Searches the innermost frame** for a `WHEN … DO` handler, walking the frame's control stack from the
   innermost active `BEGIN…END` block outward. For each block it tries its handlers **top-to-bottom**, and
   each handler's conditions **left-to-right** (Firebird's declaration order, spec §3.6). Matching reads
   the AST (`WhenHandler`/`WhenCondition` from P1) — **never re-parses**. All five `WHEN` forms are
   matched: `ANY` (always), `EXCEPTION <name>` (the surfaced folded name), `GDSCODE` (numeric *or*
   symbolic), `SQLCODE` (signed number), `SQLSTATE` ('literal'). The operands of the last three P1
   deliberately left in the condition's tokens, so the router reads them from `WhenCondition.Tokens` — a
   leaf-value read, not structure the AST already owns.
2. **On a match**, it repositions that frame's control stack so the handler body is the next thing to run:
   abandon the inner activations (closing any abandoned `FOR SELECT` cursor via `Frame.PopForUnwind`),
   skip the catching block's remaining statements (`seq.Index = Items.Count`), mark the block
   `HandlerActive = true` so its own handler body cannot re-enter it, and `PushBranch(handler.Body)`. The
   catching frame is **not** rolled back — a `WHEN`-handling block's prior statements survive (§4.5,
   measured).
3. **On no match in a frame**, it closes that frame's open cursors, `RollbackFrameSavepoint`s it (§4.5 —
   the simulated frame's side effects are undone atomically, as a real call's would be), pops it, and
   continues in the caller. When **no frame** catches — the root included — every frame is rolled back and
   `TryRoute` returns false; the session `Faulted`s (and `CurrentStatement`/`CurrentError` reflect that).

**Re-raise** (`EXCEPTION;` in a handler) needs no special interpreter state: the executor re-raises it and
the router routes the resulting error like any other. The `HandlerActive` guard is what makes a handler not
catch its own body's exception — it propagates out to an enclosing block (or frame), exactly as Firebird
does. This keeps the router **pure control flow**: it never evaluates, coerces, or interprets Firebird
error semantics — the error's identity is what the driver already reported.

`IDebugExecutor` gained one method for this — `RollbackFrameSavepoint(name)` — the unhandled-exit
counterpart of the seam-(a) `EnterFrameSavepoint`/`LeaveFrameSavepoint`. The interface is a D1 deliverable
split across the two seams; this is the only contract change.

### Breakpoints

`BreakpointSet` — a mutable set of step-point **offsets** (`Add`/`Remove`/`Toggle`/`Contains`/`Clear`),
exposed as `DebugSession.Breakpoints`. The stepping loop stops at the next step point whose offset is set,
with `StopReason.Breakpoint`; a breakpoint always wins the stop reason over `Step`. The current step is
never re-stopped on resume (it is executed before the next breakpoint check), and a breakpoint inside a
callee stops while continuing (depth preserved). Conditional breakpoints / hit counts / break-on-exception
are D12 — they compose with this set, they are not modelled here.

### The loop

Seam (a)'s "raise → `Faulted`, no routing" was replaced by "raise → `ExceptionRouter.TryRoute`": caught ⇒
clear the error and fall through to the command's normal stop/continue decision (so Step stops at the
handler body, Continue runs through it); uncaught ⇒ `Faulted`. Then, before the movement stop decision, the
loop checks `Breakpoints`. Every decision remains a pure function of (AST, frames, breakpoints, command).

### Tests

`DebugEngineTests` (+15, now 39 total) against the scripted fake: `WHEN ANY` catching in the same block
(prior statements survive, frame not rolled back); `EXCEPTION <name>` matching and *not* matching (fault +
rollback); `GDSCODE` by number and by symbol; `SQLCODE` signed; `SQLSTATE` literal; a multi-condition
`WHEN`; propagation to the caller with the callee frame rolled back and the caller catching; re-raise
propagating out with the `HandlerActive` guard proven (inner handler does not re-catch its own body); a
`FOR SELECT` body raising both unhandled (cursor closed + frame rolled back) and handled (cursor closed on
unwind to the catching block, no frame rollback); and four breakpoint cases (stop, resume-past, removed,
inside-callee). One test-only gotcha surfaced and is documented in the test: the fake keys outcomes by a
node's `Start`, which is shared across frames' coordinate spaces (both `begin `-prefixed bodies put their
first statement at offset 6), so a cross-frame raise test must not run a root leaf at the callee's raising
offset.

### Verification

Build 0/0. Tests: **4691** green in **one** `dotnet test` run (~6 s). Smoke: app launches (the engine is
pure Core, not yet wired to any UI). No lab needed (no server — fidelity vs real execution is D2's
lab-mandatory proof). **D1 is COMPLETE.** Next: **D2** (harness + session connection + executor) — a
separate session, per the plan's order (P1 → P2 → D1 → D2 → D3 …).

## D2 — Harness + session connection + executor, seam (a) (2026-07-17)

D2's first seam: the **debug session connection** — a session's own attachment + transaction + frame
savepoints (spec §4.1/§4.2/§4.5). Seams (b) `HarnessBuilder`/`ReadWriteSetAnalyzer` and (c)
`FirebirdDebugExecutor` + the lab-mandatory fidelity proof are **not** in this seam (the full milestone did
not safely fit the session's remaining context, so it was split at the plan's designated seam boundary and
this checkpoint landed green).

### `DebugSessionConnection` (EmberTern.Firebird)

A session owns a dedicated `FbConnection` + one `FbTransaction` — **decision 5: a session is not a lane**
(the Data/Metadata/Ddl lanes are per-profile singletons, but two debug tabs are two sessions are two
transactions, impossible on one lane). It never touches the Data lane (a debug rollback there would destroy
the user's uncommitted work, rule #11). The TPB is **explicit** (never a bare `IsolationLevel`, #85):
`BuildDebugTransactionOptions(DebugIsolation)` → write + (read_committed rec_version | concurrency) +
**NOWAIT** — `NOWAIT` because the debug transaction *will* meet locks held by the user's Data transaction,
and a step-level error at a known line beats a silent hang (§4.2). Isolation (`ReadCommitted` / `Snapshot`)
is user-selectable at launch (§12.4) — a routine normally run under SNAPSHOT sees different data under READ
COMMITTED, so it is surfaced, never silently defaulted. Mirrors the established per-job pure-static TPB
builder pattern (`FirebirdDdlExecutor.BuildDdlTransactionOptions`), reuse over a parallel builder.

**Frame savepoints (§4.5)** are the point of this seam: `SetSavepointAsync` (frame entry),
`ReleaseSavepointAsync` (normal exit), `RollbackToSavepointAsync` (unhandled exit) — the async counterparts
of D1's `IDebugExecutor.Enter/Leave/RollbackFrameSavepoint`, which seam (c)'s executor will bridge. Names
come from `Frame.SavepointName` (`ET_DBG_FRAME_{id}`) and are validated as bare SQL identifiers before
concatenation, so no path can inject through them. `SAVEPOINT` / `RELEASE SAVEPOINT` / `ROLLBACK TO
SAVEPOINT` are verified working through `FirebirdClient` 10.3.4 (§15.3 probe [5]). Every wire operation is
serialized on the session's **own** command lock, captured once per acquire/release (#31/#98/#120/#236 — a
session connection never flips lanes, so it is a single lock; interleaving fine, concurrency not). `CommitAsync`
(the rare explicit `Commit debug transaction`, §4.4) / `RollbackAsync` (the default at session end) / an
idempotent `DisposeAsync` that rolls back then closes the attachment and deregisters.

### Lifecycle in `FirebirdConnectionService`

`CreateDebugSessionAsync(DebugIsolation)` opens a fresh attachment from the active profile, begins the
session transaction, registers it, and returns the `DebugSessionConnection`; a connection-limit refusal
surfaces as `ConnectionFailedException` (the thinking behind #89), never a broken app. Sessions are tracked
in `_debugSessions` **only** so `DisconnectAsync`/`Dispose` tear them down deterministically — their
attachments must not outlive the profile's connection (spec §4.1); each session deregisters itself on
dispose (`RemoveDebugSession`). `DisconnectAsync` tears sessions down first (async, proper rollback);
`Dispose` blocks best-effort (safe: `DisposeAsync` uses `ConfigureAwait(false)` throughout, so `GetResult`
cannot deadlock on a captured context). **No `ConnectionRole.Debug`** was added (decision 5) — a session
connection is not a lane, so it sidesteps the lane machinery (`GetCommandLock(role)`, the accessor hazard)
entirely.

### Tests

`DebugSessionConnectionTests` (+13, pure — mirrors `TransactionTpbTests`): the debug TPB for both
isolations (READ COMMITTED = write/read_committed/rec_version/nowait, never Wait/Concurrency; SNAPSHOT =
write/concurrency/nowait, never ReadCommitted/Wait); the three savepoint statement forms; and savepoint-name
validation (bare identifiers accepted, empty / leading-digit / spaces / injection rejected, and
`SavepointStatement` throws on a bad name). The **live** round-trip (open → begin → savepoint set/rollback-
to/release → commit/rollback → teardown) needs a real server and is **awaits user confirmation** per the QA
rule (the driver capability itself is already confirmed, §15.3 [5]).

### Verification

Build 0/0. Tests: **4704** green in one run (~8 s). Smoke: app launches. **D2 seam (a) DONE.** Parked for
the next session: **seam (b)** `HarnessBuilder` + `ReadWriteSetAnalyzer` + the §3.4 R1–R5 rules (pure Core,
unit-tested) and **seam (c)** `FirebirdDebugExecutor : IDebugExecutor` + the lab-mandatory simulated-vs-real
fidelity comparison (extend `Lab/setup.sql` with the debugging zoo first). Order within D2: (a) → (b) → (c),
per the plan.

## D2 — Harness + session connection + executor, seam (b) (2026-07-17)

D2's second seam: the **Evaluation Harness builder** and the **read/write-set analyzer** — the intellectual
core of D2, and the §3.4 rules R1–R5, all in **pure Core** (zero Avalonia, zero FirebirdSql). Seam (c)
(`FirebirdDebugExecutor` + the lab-mandatory fidelity proof) is deferred to the next session.

### `HarnessBuilder` — the one server mechanism, as a pure function

`HarnessBuilder.Build(HarnessRequest) → HarnessResult` generates the anonymous `EXECUTE BLOCK` that is the
**only** server mechanism (§3.2/§3.3) — every step, condition, watch and evaluation is the same builder with
a different fragment. It is a pure function: the fragment text, each variable's verbatim declaration + base
type + current value, the sub-routine declarations and the read/write set are all **inputs** (the Firebird
executor derives them from metadata + the frame in seam c; tests supply them directly). That decoupling is
exactly what makes the non-negotiable §3.4 rules unit-testable **without a live server** — the rules are
proven here; fidelity vs real execution is seam (c)'s lab proof. The rules, enforced in `Build`:

- **R1 — never assign an injected `NULL`.** Only reads with a non-null value become a parameter + an
  injection assignment; a declared variable is already `NULL`, and assigning `NULL` into a `NOT NULL`-domain
  variable is what crashes real ERP code (the whole reason v1 died on the first procedure).
- **R2 — parameters and `RETURNS` columns use the variable's BASE type**, never its domain (a domain-typed
  `RETURNS` re-validates on `SUSPEND` and fails on a legitimately-null write-back). Base types are an input
  (`HarnessVariable.BaseType`); their derivation from metadata is seam (c)'s job.
- **R3 — frame variables declared VERBATIM** (`HarnessVariable.Declaration`, copied from source — domain /
  `NOT NULL` / `CHECK` / default preserved), so the statement's own assignments keep domain semantics.
- **R4 — inject only the reads, return only the writes.**
- **R5 — every in-scope sub-routine declaration carried, verbatim, always** (dropping one lets a local
  `F()` silently resolve to a global `F()` — a §F violation). Emitted after the variable declarations
  (Firebird's required order).

Statement mode runs the fragment verbatim; Expression mode (conditions / watches) evaluates it into an
`ET_DBG_RESULT` column of the caller-supplied result type. `HarnessResult` carries the SQL, the ordered
parameter values (only the injected non-null reads — R1), the `RETURNS`→variable write-back map, and the
result-column name. Param/return names use distinctive `ET_P_`/`ET_O_`/`ET_DBG_` prefixes (not the spec
example's terse `P_`/`O_`) so they cannot collide with a real ERP variable name. A statement with no reads
and no writes is a plain executable block (no `RETURNS`, no `SUSPEND`).

### `ReadWriteSetAnalyzer` — the read/write set from the model

`Analyze(statement, model) → ReadWriteSet` computes the read/write-set-driven injection (§3.5) by
**consuming the binder's resolved references** (rule #1/#2 — never re-parse, never re-resolve). **Reads** =
the variable/parameter references in the statement's span (over-inclusion is safe: a variable appearing only
as an assignment target is harmless to inject, and R1 skips a null anyway). **Writes** = the variables it
may mutate: an assignment writes exactly its **leftmost l-value** (narrowed precisely); a control-flow
condition (`IF`/`WHILE`) writes nothing; any other statement's writes are the reads (a correct, chattier
superset — a single statement changes only variables it references).

Two deliberate boundaries, both documented in the type: the **transitive fixpoint over the sub-routine call
graph** belongs to **D9** (where local routines become frames) — meanwhile the sub-routine *declarations*
are always carried in full by the harness (R5), so nothing is silently lost; and the §3.5 **inject-all-in-
scope** fallback is exposed as the named primitive `InScopeLocals(model, offset)` for a caller that genuinely
cannot compute a precise set (a Watch on an arbitrary expression, D5) — **not** an auto-branch, because the
binder never emits an unresolved-*local* signal (an unrecognised bare identifier is not a frame variable —
it is a column/function/typo, correctly dropped), so an auto-fallback would be untestable dead code (the
gotcha-#233 lesson: don't ship untested branches).

### Tests

`HarnessBuilderTests` (+11): the injection shape; **R1** (null-valued and absent-valued reads are neither
parameter nor assignment); **R2** (base type on param + `RETURNS`, never the domain); **R3** (verbatim
declaration); **R4** (an unreferenced variable is declared but neither injected nor returned); **R5** (sub-
routine carried verbatim, after the variable declarations); expression mode (result column + no write-backs)
and its required result type; the plain-executable-block case; auto-terminator. `ReadWriteSetAnalyzerTests`
(+5) against the **real** `SemanticModel` (strict parse of a whole `CREATE PROCEDURE`, so the body binds
against the declared scope): assignment reads/writes, the leftmost-l-value target, a condition that reads but
writes nothing, a plain DML's superset writes, and `InScopeLocals`. **Note recorded (test lesson):** the
editor's `SemanticModel.Build(string)` uses the *lenient* newline-split parse, which breaks a routine apart
so its body binds without the declared variables — the debugger must build the model from the **strict**
`SqlParser.Parse(sql).Root` of the whole routine.

### Verification

Build 0/0. Tests green (user-verified; the full-suite run was slow so it was confirmed manually). Smoke: app
launches. **D2 seam (b) DONE.** Parked for the next session: **seam (c)** `FirebirdDebugExecutor :
IDebugExecutor` — wires D1's interpreter to seam (a)'s `DebugSessionConnection` through this harness, then
the **lab-mandatory** simulated-vs-real fidelity comparison (extend `Lab/setup.sql` with the debugging zoo
first). `HarnessBuilder`/`ReadWriteSetAnalyzer` are deliberately-parked pure infrastructure (mitigating
#233 — recorded here because nothing calls them until seam c). Order within D2: (a) → (b) → **(c)**.

## D2 seam (c) — FirebirdDebugExecutor + live fidelity (2026-07-17)

The seam that closes the debugger foundation: the real executor, driving `DebugSession` against a live
Firebird, with the mandated simulated-vs-real fidelity comparison (spec §2.1). Landed at natural
sub-seams, each green and committable.

**c.1 — `PsqlDeclarationExtractor` (pure Core).** Lifts a routine frame's local variable declarations
verbatim (R3 — domain / `NOT NULL` / `CHECK` / default preserved) and their type spec (the R2 base-type
resolver's input) from the parsed `BlockStatement` + source. Paren-aware type-spec slicing
(`NUMERIC(15,2)` whole, `DEFAULT`/`NOT NULL` excluded); `TYPE OF` kept verbatim for the resolver.
`SubRoutines` (R5) is empty in D2 **by construction** — a local `DECLARE PROCEDURE/FUNCTION` is not in
`BlockStatement.Declarations` (the parser's `IsDeclarationStart` excludes it; it is D9's flagship). 6
tests. Commit `21c7270`.

**c.2 — `FirebirdDebugExecutor` + `DebugErrorMapper` + `FirebirdDebugMetadata`.** The executor implements
`IDebugExecutor`: each step / DML leaf → a Statement-mode harness; each `IF`/`WHILE` condition →
an Expression-mode `BOOLEAN` harness; the read/write set narrows the payload; frame values injected
(R1 skips null/absent), write-backs applied. `SUSPEND` is control flow — the output row is emitted
client-side from the output params, no round-trip. Savepoints delegate to the session. The
sync-over-async bridge (`IDebugExecutor` is synchronous — D1's frozen contract) blocks on the async
session; deadlock-safe because everything is `ConfigureAwait(false)` and stepping runs off the UI thread
(D4). D2 boundaries (§F, explained stops): `ResolveRoutine` → null (a call runs in place = step-over,
100% faithful §5.3; step-into is D8/D9), `OpenCursor` → the Cursor Bridge (D6).

`FirebirdDebugMetadata` resolves the frame variable templates once at session start: **R2 base types**
come from `RDB$FIELDS` via the existing `FirebirdDdlReader.FormatType` ("derivation, not guessing"),
params from `RDB$PROCEDURE_PARAMETERS` (declared with their user domain, R3; base-typed injection, R2),
locals via the c.1 extractor. `TYPE OF` is a bounded D2 stop. `DebugErrorMapper` maps `FbException` →
`DebugError` from SQLSTATE/GDS, **grounded against the live engine** (a throwaway driver probe): a user
`EXCEPTION` carries `isc_except` (335544517) with its name on the message's **first line**; a `NOT NULL`
domain validation is SQLSTATE 42000 / GDS 335544879; the small vector entries (0, 1) are argument
separators, not GDS codes. The pure `Build()` decision is unit-tested (an `FbException` cannot be
constructed in a test); `SqlCode` (the legacy code the driver does not distinctly expose) and the
symbolic GDS name are documented D2 boundaries. 5 tests. Commit `d077a5f`.

**c.3 — lab zoo + live fidelity.** A small **D1 extension** was needed first: `DebugSession` had no way
to seed the root frame's input-parameter arguments (the root has no caller to provide them), so it gained
an optional `rootValues` ctor arg (additive; existing tests pass null). The lab was extended with two D2
procedures — `SP_DBG_SUMMARY` (assignment, a **domain `NOT NULL` local**, IF/ELSE, SUSPEND) and
`SP_DBG_GUARD` (`EXCEPTION` + `WHEN … DO`) — mirrored into `Lab/setup.sql` and the `.fdb` rebuilt at an
ASCII temp path then copied back (#149).

The fidelity harness (throwaway) drove the **real** `FirebirdDebugExecutor` through `DebugSession`
step-by-step and compared the DB state + outputs to **real execution** of the same routine. All seven
cases matched:

- `SP_DBG_SUMMARY(2,60)` → `(120,BIG)`, `(1,10)` → `(10,SMALL)` — the domain-`NOT NULL` local `V_TOTAL`,
  declared but unassigned at entry, **does not crash** (R1: never inject the null; R3: declared verbatim;
  R2: base-typed write-back) — the explicit DoD case.
- `SP_DBG_GUARD(10)` → `OK`, `(-5)` → `CAUGHT` — an `EXCEPTION` routed through the **real** `FbException`
  → `DebugErrorMapper` → `ExceptionRouter` → the `WHEN EXCEPTION` body.
- `SP_ADD_ORDER(1,…)` — `SELECT … INTO`, IF, DML `INSERT` (firing `TR_ORDERS_BI`), SUSPEND: inserts a
  matching order, and the session rollback undoes it (savepoint/tx).
- `SP_ADD_ORDER(999,…)` — unhandled `EXCEPTION E_CUSTOMER_NOT_FOUND`: `Faulted`, name resolved, root frame
  rolled back, no row.

**The finding that only the live comparison caught (gotcha #238).** The first `SP_ADD_ORDER` runs both
silently mis-behaved — the customer check no-oped. Diagnosis: a reused `SELECT … INTO` statement
(Etap 6.9 / B5) surfaces **no** local references from the binder (the query binder records its
`FROM`/columns, not the `:`-colon refs in the `WHERE` nor the `INTO` targets — a token-walked
`INSERT`/assignment/`IF` records theirs correctly), so `ReadWriteSetAnalyzer.Analyze` returns empty reads
AND empty writes — dropping the `INTO` write-back the statement exists to perform. Fixed in the consumer,
not with a second resolver: when the model surfaces nothing (empty/empty) the executor falls back to
§3.5's named `InScopeLocals` primitive (inject/return ALL in-scope locals — correct, chattier), never a
guess; precise narrowing stays in force for every statement whose refs the binder does surface. Pinned by
`ReadWriteSetAnalyzerTests.SelectInto_SurfacesNoLocalRefs_*` (a Core test that flips if the binder is ever
deepened to surface those refs). This is the §2.1 lesson in miniature: a green unit suite proved the
interpreter self-consistent; only the lab proved it **faithful**.

Build 0/0; **4732 tests green in one run**; smoke clean. D2 COMPLETE. Next: **D3** (editor-wiring
consolidation) — the first non-pure milestone, immediately before the first debugger UI.

## D3 — Editor-wiring consolidation (2026-07-17)

**The first non-pure debugger milestone, and the one with zero debugger code.** Spec §11.1, gotcha #219,
plan §2 (D3). Its job is to collapse the *two* hand-maintained copies of the SQL editor's language wiring
into **one attach path** — *before* the debug tab (D4) becomes a third host bringing four new renderers at
once, in the exact pattern that shipped S3 with no squiggles in the main editor.

### The two seams, and why they diverged

The **intrinsic block** — the capabilities identical on every SQL surface — is: `SqlCompletionController`
-> `SemanticHighlighter` -> `NavigationController` -> `SquiggleRenderer` -> `RelatedElementsRenderer` ->
`LanguageExpansionController` -> `TypingErgonomicsController` -> `EditorSearch`. It lived in two copies:

- **`SqlEditorBehavior.Attach`** — the object editors' installer (Procedure / Function / Trigger / View /
  Package detail + Script Executor). Runs at `OnAttachedToVisualTree`, where a **stable, non-null**
  `MainWindowViewModel` is reachable via `FindAncestorOfType<Window>().DataContext`. Uses the completion
  controller's built-in `subscribeMetadataChanged` / `subscribeMetadataReady` hooks.
- **`MainWindow` ctor** — the main SQL editor, hand-wired with null-safe `_currentVm?.…` callbacks, because
  the window's `DataContext` (its VM) is set *after* construction (`App.axaml.cs`:
  `new MainWindow { DataContext = … }`). It deliberately **bypassed** the controller's metadata hooks —
  they latched "subscribed" against a null VM and dropped the handler — and instead wired
  `Metadata.ObjectsChanged` / `MetadataReady` to the stable VM in `OnDataContextChanged`
  (`OnMainEditorMetadataChanged` / `OnMainEditorMetadataReady`), plus a private `WarmReferencedMetadataAsync`
  and private `CreateMetadataSnapshot` / `EnsureColumnsAsync` / `EnsureRoutineParametersAsync` forwarders.

The only real difference between the two copies was **timing** — the VM is the same type and stable once
known; the main editor just knows it late. So the null-safety, the bespoke metadata handlers, and the
metadata forwarders were *all* workarounds for attaching before the VM existed.

### The approach — "attach at VM-arrival" (user-ratified over a shared-helper alternative)

Rather than factor the block into a null-safe shared helper (which would preserve the timing but keep the
lifecycle problem encapsulated rather than solved), the main editor's wiring **moves from the ctor to the
first non-null `OnDataContextChanged`**, where it calls the *same* `SqlEditorBehavior.Attach(_editor,
_currentVm)` the object editors use — with a stable, non-null VM. This is precisely what the spec meant by
"subscribe once the VM arrives," and it dissolves the historical workaround instead of encapsulating it.

Feasibility was grounded before touching anything: `_completion` is referenced **nowhere** in `MainWindow`
except the wiring block and the two metadata handlers, so the blast radius is contained; and `DataContext`
is set via an object initializer after construction, so `OnDataContextChanged` reliably fires with the
non-null VM before `Show()` (the app already depends on that event for all its VM-event wiring).

**Consolidation boundary (user-confirmed): the intrinsic block only.** The genuinely per-host wiring stays
with the caller — `DiagnosticsPanelHost.Track` (per-window host + reveal), `AmbientModelRefresh`
(routine/trigger editors only), and `SqlSnippetDropTarget.Attach` (context varies) — because they truly
differ per host, were never the #219 duplication risk, and folding them into `Attach` would force
artificial per-host parameters for no proportional gain.

### The change

- `MainWindow` ctor: the ~65-line hand-wired language block is deleted; only `_editor.TextChanged +=
  OnEditorTextChanged` (needs no VM) stays. The DDL-preview editor + `diagnosticsPanel.Navigator` wiring
  (also VM-free) stay in the ctor untouched.
- `MainWindow.OnDataContextChanged`: a guarded (`_completionAttached`) one-time block calls
  `_completion = SqlEditorBehavior.Attach(_editor, _currentVm)` then `_diagnostics.Track(_editor,
  _completion)`. The `Metadata.ObjectsChanged` / `MetadataReady` subscribe+unsubscribe lines are removed.
- **Deleted as now-dead** (Developer Contract #20): `OnMainEditorMetadataChanged`, `OnMainEditorMetadataReady`,
  `WarmReferencedMetadataAsync`, and the private `CreateMetadataSnapshot` / `EnsureColumnsAsync` /
  `EnsureRoutineParametersAsync` forwarders — every one of their responsibilities is now owned by the shared
  `Attach` (which reads `vm.CreateMetadataSnapshot` / `vm.EnsureColumnsAsync` / `vm.EnsureRoutineParametersAsync`
  / `vm.WarmReferencedAsync` and subscribes the controller's own metadata hooks to `vm.Metadata`). Removal
  came **after** the new path built + tested green, per the user's explicit "prove before delete" directive.
- `SqlEditorBehavior` gained no new parameters — the consolidation is achieved by *deleting* the second copy,
  not growing the shared one (lighter than the plan implied; user agreed this is the better result). Its
  XML-doc + the stale "attach in BOTH seams" comments (here, in `TypingErgonomicsController`,
  `LanguageExpansionController`, and the `SquiggleRenderer`/`MainWindow` inline notes) were corrected to
  describe one path.

### Responsibility-transfer proof (why the deletion was safe)

| Removed MainWindow mechanism | Responsibility | New owner in the shared path |
|---|---|---|
| `OnMainEditorMetadataChanged` | rebuild model when a metadata category loads | controller's `subscribeMetadataChanged` hook -> `_language.NotifyMetadataChanged()` (wired via `vm.Metadata.ObjectsChanged`) |
| `OnMainEditorMetadataReady` | definitive rebuild + full warm + publish on prefetch complete | controller's `subscribeMetadataReady` hook -> `_language.RefreshModelWithMetadata()` (via `vm.Metadata.MetadataReady`) |
| `WarmReferencedMetadataAsync` | warm referenced objects' columns/detail | `Attach`'s `warmReferencedMetadata: (n,ct) => vm.WarmReferencedAsync(n,ct)` |
| `CreateMetadataSnapshot` | metadata snapshot for the model | `Attach`'s `metadataSnapshot: vm.CreateMetadataSnapshot` |
| `EnsureColumnsAsync` | dot-completion column warm | `Attach`'s `ensureColumnsAsync: t => vm.EnsureColumnsAsync(t)` |
| `EnsureRoutineParametersAsync` | signature-help param warm | `Attach`'s `ensureRoutineParamsAsync: t => vm.EnsureRoutineParametersAsync(t)` |
| `metadataGeneration: … ?? 0` | generation counter | `Attach`'s `metadataGeneration: () => vm.Metadata.ObjectsGeneration` |

Each responsibility has a live owner, and — the strongest evidence — **the object editors already run this
exact path in production**, exercised by the full suite (including the headless `ConnectionExpandBindingProbe`,
which drives `SqlEditorBehavior.Attach` and types real key events into the editor).

A behavioural equivalence worth noting: the old main-editor metadata subscription was tied to the stable VM
(always live); the new one is scoped to the *editor's visual-tree lifetime* (subscribe on attach, unsubscribe
on detach). For the main SQL editor — a permanent part of the window layout that never detaches — these are
equivalent; the lifetime scoping exists for the object editors' tabs, which do detach/reattach.

### Verification

Build 0/0; **4732 tests green in one run** (identical to the D2 baseline — no tests added or removed, this is
behaviour-preserving); smoke clean. Per the QA rule, the visual equivalence of every capability on every
surface (squiggles, hover, related-elements, completion, language-completion, diagnostics panel, F8
navigation — in the SQL Editor *and* the object editors, in *both* themes) **cannot be proven by tests** and
is reported as *awaits user confirmation* — a manual checklist is in the session summary. Gotcha #219 updated
to "resolved by D3"; the plan's "Dual wiring (until D3)" danger row retired.

**Next: D4 (debugger tab MVP)** — the first debugger UI, now attaching its renderers through the one seam.
