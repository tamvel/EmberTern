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
navigation — in the SQL Editor *and* the object editors, in *both* themes) cannot be proven by tests; it was
verified by a **manual QA pass — user-confirmed 2026-07-17**. Gotcha #219 updated to "resolved by D3"; the
plan's "Dual wiring (until D3)" danger row retired.

**Next: D4 (debugger tab MVP)** — the first debugger UI, now attaching its renderers through the one seam.

---

## D4 — Debugger tab MVP (2026-07-17)

The first debugger UI: launch a **standalone procedure**, set breakpoints, step, watch variables. Built as
a **thin presentation layer** over the already-proven engine (D1 interpreter + D2 executor/harness/session),
per Developer Contract #1–#5 — the tab never evaluates an expression, coerces a type, or re-implements
Firebird semantics.

### What shipped

- **Tab infrastructure.** `WorkspaceTabKind.Debugger`; `ActiveDebugger`/`IsDebuggerTabActive` on the
  `_selectedWorkspaceTab` notify chain (gotcha #25); `WorkspaceTabViewModel.CreateDebugger`; hosted in
  `MainWindow.axaml` exactly like `ScriptExecutorTabView` (a per-kind view gated on `IsDebuggerTabActive`).
  Opened from the sidebar procedure-leaf **"Debug procedure…"** context item — `MetadataNodeViewModel.DebugProcedure`
  → `MetadataExplorerViewModel.DebugProcedureRequested` → `MainWindowViewModel.OnDebugProcedureRequested`
  (mirrors the Execute-procedure chain). Not a singleton (two tabs = two sessions). Torn down on tab close
  (`CloseTab` → `DebuggerTabViewModel.DisposeAsync` → rollback + close the attachment, §4.4).
- **`DebuggerTabViewModel`** (App). Parses the routine **once** — the strict whole-routine
  `SqlParser.Parse(source).Root` → `SemanticModel.Build(...)` → the `DdlStatement.Body` (gotcha #238: a
  `CREATE PROCEDURE` stays one `DdlStatement` whose body binds with its declares in scope) — to derive the
  launch panel and the step-point set. Built **without** a metadata provider, so `DiagnosticsEngine`'s
  object/column categories stay silent (conservative — the routine already compiled). Then drives D1's
  `DebugSession` through the launcher seam. Every engine call blocks on a wire op (the sync-over-async
  executor), so stepping runs on `Task.Run` and the awaiting continuation updates observable state.
- **`IDebugSessionLauncher`** (App seam). The one place App touches the Firebird debug backend, so the VM is
  server-lessly unit-testable. Production `FirebirdDebugSessionLauncher` opens a `DebugSessionConnection`
  (`FirebirdConnectionService.CreateDebugSessionAsync`), builds `FirebirdDebugExecutor.CreateAsync`, constructs
  the `DebugSession` over the body + root parameter values, and `Start()`s it (paused at entry). A test fake
  builds the session over a scripted `IDebugExecutor` with a no-op teardown.
- **Launch panel** (`§9.2` — inline, not a modal, because you re-run constantly). Typed parameters **reuse**
  the Smart-Parameters infrastructure (`ExecuteProcedureDialogViewModel` — typed per-kind rows + persisted
  history + validation + `Resolve()`), so there is no second parameter editor; its `AcceptCommand` is the
  resolve/validate/record path, and input-parameter arguments seed the root frame (§9.3). An isolation selector
  (Read Committed / Snapshot, §4.2). A **pre-flight** (`DebugPreflight`): `DiagnosticsEngine` unresolved-name
  warnings + a conservative **lexical** scan for the §4.6 data-safety boundaries that survive the debug rollback
  (`IN AUTONOMOUS TRANSACTION`, `GEN_ID` / `NEXT VALUE FOR`), + the §F "no step points ⇒ cannot start" blocking
  refusal. The §4.6 warnings ship **with** the MVP, as the plan requires.
- **Editor surface.** The read-only source editor attaches D3's **one** `SqlEditorBehavior.Attach` seam (intrinsic
  highlighting/hover over the source), then the debugger renderers alongside it (spec §11.1): `CurrentLineRenderer`
  (an `IBackgroundRenderer` painting a translucent-amber band over the paused step point) and `BreakpointMargin`
  (a clickable `AbstractMargin` red-dot gutter). Breakpoints **snap to an `IExecutableStatement`** (§9.6) — the
  margin/keyboard report the clicked offset, the VM maps it to the nearest step point. Repaint via
  `TextView.Redraw()`, never `InvalidateVisual()` (gotcha #223).
- **Stepping + keyboard.** Continue / Step Into / Over / Out / Run-To-Cursor / Stop(rollback) / Restart. Keyboard
  is VS-standard and **tab-scoped** (`F5`=Continue, `F10`=Over, `F11`=Into, `Shift+F11`=Out, `Shift+F5`=Stop,
  `Ctrl+Shift+F5`=Restart, `F9`=toggle breakpoint, `Ctrl+F10`=Run-To-Cursor) — tunnelled on the editor so the
  read-only control never swallows them. `F5`=Continue is the one deliberate contradiction with the app-wide
  Execute (spec §9.7).
- **Variables.** A basic list from the current frame — the declared symbols (params then locals) as the roster,
  the client-side frame as the live values, `<null>` rendered distinctly. The rich window (grouping, change
  highlight, inline edit, data tips) is D7.
- **Theme.** New tokens `DebugCurrentLineColor`/`Brush` + `DebugBreakpointColor`/`Brush` in **both** dictionaries.

### Boundaries kept (§F)

Step-into resolves to nothing in D4 (`FirebirdDebugExecutor.ResolveRoutine` → null), so a call runs on the
server = **step-over**, 100% faithful (§5.3); stepping into a stored/local routine is D8/D9. Triggers, packages,
cursors, and the Watches/Immediate/Evaluate surfaces are their own later milestones.

### Verification

- **VM unit tests** (`DebuggerTabVmTests`, +12) against a fake launcher over a scripted `IDebugExecutor` — no
  server: preparation derives the input parameters + readies launch; the pre-flight flags autonomous-tx +
  generator use; an unsteppable/missing source blocks; launch pauses at entry with the variable roster; Step
  Over advances the current statement then completes; Continue runs to completion; a write-back updates a
  variable; an unhandled raise faults; Stop tears the run down and clears; a breakpoint snaps to a step-point
  start and stops Continue at the marked statement.
- Build 0/0; **4744 tests green in one run**; smoke clean (app launches).
- **User-confirmed on the live lab (2026-07-17):** a manual pass launched `XXX_ZESTAWIENIE` (a `WHILE` loop with
  a `SELECT … INTO` and `SUSPEND`), stepped through it, hit breakpoints, and watched variables update — the
  debugger worked and felt stable. Follow-ups still open: an automated **simulated-vs-real lab comparison** (§13
  DoD) and a headless view-attach probe in `ConnectionExpandBindingProbe`.

### D4 UX review — backlog for later milestones (user, after first real use)

The user reviewed D4 in real use and confirmed it works well; the notes below are **explicitly not a D4 change**
— they are recorded here to be folded into later milestones (UI polish + wherever the debugger grows). The
standing directive: **address them as UX/theme in the view + theme tokens; never patch UX by pushing logic into
the ViewModels/UI layer — keep the D1–D4 responsibility split (Core interpreter · Firebird executor · thin VM ·
view).**

1. **First-class entry points.** The debugger is one of EmberTern's most important features but today launches
   only from the sidebar context menu ("Debug procedure…"). Add first-category affordances: a Debug button in the
   procedure view, a bug-icon toolbar button, and a keyboard shortcut — keep PPM as an alternative, not the
   primary path.
2. **Transaction config belongs in global Settings.** The per-launch isolation selector is technically right but
   reads as an advanced knob most users never change (IBExpert doesn't surface it every run). Once global app
   settings exist, move Debugger transaction options there (isolation, wait/no-wait, read-only, …) and show only
   the routine's **parameters** at launch. Fine as-is for now.
3. **Current-line marker is too aggressive (esp. dark theme).** The amber fill dominates the syntax colouring.
   Re-style to a subtle blue wash (~10–15% opacity) + a thin blue left bar (optionally a margin arrow) — highlight
   the statement without masking the syntax highlight. This is a `DebugCurrentLineBrush` re-tune (both dicts) +
   possibly a left-edge draw in `CurrentLineRenderer`; no VM change.
4. **Variables must show kind.** IN params, OUT params, and locals look identical (name + type only). Distinguish
   them by icon / icon colour / grouping / sections (e.g. `→ IN`, `← OUT`, `◇ local`). This is the D7 Variables
   window's job — the VM already knows the kind (`ParameterSymbol.Direction` / `VariableSymbol`), so it's an
   icon-key + template concern, not new logic.
5. **Step Into / Over / Out icons too similar.** Adopt a more distinctive, VS/JetBrains-like icon set with clearer
   colour differentiation so the controls are recognisable at a glance.
6. **Edit parameters on a running session.** The inline launch-panel model is less convenient than a dialog once
   running — there's no easy way to change parameters mid-session. Preferred model: first run shows the params;
   while debugging, an "Edit Parameters…" affordance re-opens them → Restart. (Keeps the panel's "no re-prompt on
   Restart" while restoring easy editing.)
7. **Grow parameter history.** The history mechanism is liked; future additions: pin favourites, recent, group by
   date, delete entries.
8. **Richer paused status.** "Paused at line 14 — step" is thin; the AST knows the statement kind, so show e.g.
   "Paused — SELECT INTO (line 14)" / "Paused — WHILE loop (line 14)" / "Paused — FOR SELECT (line 27)". Low
   priority, pure presentation off the current step node.

**Overall (user):** the debugger architecture looks very good; D4 proved the engine + UI integration work and that
D3's single `SqlEditorBehavior.Attach` seam paid off. The main area to refine is UX — a natural product stage, not
an architecture or implementation flaw.

**Next: D5 (Evaluate / Watches / Immediate — one HarnessBuilder mechanism, three surfaces).**

## D5 — Expression evaluation surface, seam (a): Evaluate + Immediate (2026-07-18)

**Cel (§9.5, decision 6):** expression evaluation as **one engine, three surfaces** (Evaluate / Watches /
Immediate). This seam ships **Evaluate + Immediate + the Executed SQL audit**; **Watches + per-routine
persistence is seam (b)** (the plan splits D5 into two sessions — this is session one, stopped at the
architectural seam with the repo green).

### The one engine (Core) — no second evaluator (D5 risk #1)
Every surface is *literally the harness with a user-supplied fragment* (§3.2/§3.3), so nothing new evaluates
anything:
- **`EvaluationModels.cs`** (`EmberTern.Core.Sql.Debugging`): `EvaluationKind` (Expression | Statement),
  `EvaluationRequest` (fragment + kind + `ScopeOffset`), `EvaluationResult` (the generated `Sql` — the
  §10.3/§F audit anchor — plus `Value` / `Error` / `Writes`, with `HasError`/`HadWriteBack`).
- **`IDebugExecutor.Evaluate(request, frame)`** — a **new method on the one server seam**. An arbitrary
  fragment has **no AST node**, so its injected read/write set is the §3.5 **`ReadWriteSetAnalyzer.InScopeLocals`**
  primitive — which is *exactly* why D2 carved that out named ("a Watch on an arbitrary expression the model
  did not bind — D5"). The fake scripts it; the Firebird executor implements it with the machinery it already
  had.
- **`DebugSession.Evaluate(fragment, kind)`** — the pure orchestration face the VM talks to. Requires
  **Paused** (a live frame exists only while paused), delegates to the executor against `CurrentFrame`, and
  for **Statement** mode applies the write-back to the live frame (the Immediate window operates *on the live
  frame*, §9.5). It never evaluates/coerces/interprets anything itself.
- **`FirebirdDebugExecutor.Evaluate`** — builds a `HarnessRequest` (Expression → result column, Statement →
  verbatim + write-back), reusing `BindValues`/`RunHarnessAsync`. An arbitrary expression has **no known
  type** (unlike an `IF`/`WHILE` condition, which is `BOOLEAN`), so the result column is a wide
  `VARCHAR(8191) CHARACTER SET UTF8` — the server casts the value to text and we surface it as text (typed,
  per-kind inspection of a *declared* variable is the Variables window, D7). A value that cannot cast (a
  binary BLOB) raises and is surfaced as the error, never guessed (§F).

### Deviation from the plan (Developer Contract): no `EvaluateController`
The plan named an App-side `EvaluateController`. **The real "one engine" is Core's `DebugSession.Evaluate`;**
the App orchestration (background-thread run + append to the audit log) is thin enough to live on the VM —
exactly as *stepping* is orchestrated today (`Task.Run` + `RefreshFromSession`). A separate controller would
be pure indirection over `Task.Run` + a collection append. So evaluation is a few methods on
`DebuggerTabViewModel`, and seam (b)'s Watches refresh loop will call the same `DebugSession.Evaluate` from
`RefreshFromSession`. (Precedent: D3 chose "solve the lifecycle" over the plan's letter, documented.)

### App — the two inline surfaces + the audit log
- **`DebugExecutedSqlRowViewModel`** — one Executed-SQL entry (spec §10.3): fragment, kind label, result
  text (value / statement note / error), the **generated harness SQL kept visible** (the row's tooltip), a
  timestamp, and an `IsError` / `HasSideEffect` flag. A **statement is always flagged `±`** (it ran real SQL
  in the debug transaction — side-effect-capable by nature, §9.5); an expression never is. The precise
  "which variables changed" is the Variables panel's job (it reflects the applied write-back), not the audit
  flag's.
- **`DebuggerTabViewModel`** — added `ExecutedSql` (newest-first, capped at 200), `ImmediateInput` +
  `ImmediateAsStatement`, `EvaluateImmediateCommand` (gated on Paused + non-empty input), and the shared
  `EvaluateFragmentAsync(fragment, kind)` used by both Immediate and Evaluate(Shift+F9). Evaluation runs on
  `Task.Run` with **Phase → Busy for the duration**, which gives mutual exclusion with stepping *via the
  existing state machine* (a step can't start while Busy; evaluation requires Paused) — so the non-thread-safe
  `DebugSession` is never touched concurrently. A clean evaluation clears the input (REPL-style); a **server
  raise keeps it** so the user can edit and retry. The audit log is cleared on Launch (fresh session) and
  Stop.
- **`DebuggerTabView`** — a bottom panel (below a horizontal splitter): the Immediate input (Enter =
  evaluate) + an "as statement" checkbox + an Evaluate button, and the Executed SQL list (fragment + result,
  error rows in `ErrorBrush`, `±` in `WarningBrush`, harness SQL on the row tooltip). **Shift+F9** in the
  source editor evaluates the selection (or the identifier under the caret) as an expression through the same
  engine (spec §9.7). All theme tokens; no new colours; no UX polish (the D4 UX backlog stays deferred).

### Tests (+11)
- `DebugEngineTests` (+5): expression returns a value and does not mutate the frame; statement applies
  write-back to the live frame; expression mode never applies write-back (even if the executor returned
  writes); evaluate-when-not-paused throws; empty fragment throws. The fake grew a scriptable `Evaluate`.
- `DebuggerTabVmTests` (+6): Immediate expression appends the result + clears input; Immediate statement
  flags the side effect + updates live variables + passes Statement mode; a server error shows an error row +
  **keeps** the input; Evaluate(selection) routes through the same engine; the command is disabled when not
  paused; Stop clears the audit log. The fake grew a scriptable `Evaluate`.

**Build 0/0; 4755 tests green in one run; smoke clean.** **Live evaluation awaits user confirmation** (needs
a server, per the QA rule) — the §9.5 verification is: evaluate an expression calling a stored function and
compare to `SELECT <expr> FROM RDB$DATABASE`. Manual QA checklist prepared.

### Follow-ups after manual QA (2026-07-18, same session)

Two small UX/discoverability changes the user asked for after trying seam (a) — **no engine/architecture
change** (the D1–D5 responsibility split is untouched):

1. **Immediate input is no longer auto-cleared after a successful evaluation.** The typical workflow is
   experimenting with the *same* expression and tweaking it, so clearing forced re-typing. Now the input is
   **kept**, and a small inline **Clear (✕)** button (inside the text box, right-aligned, visible only when
   there is text) clears it on demand (`ClearImmediateCommand`, gated on `HasImmediateInput`). Pure
   view/VM-presentation: `EvaluateFragmentAsync` dropped its "did it succeed → clear" return; the row/audit
   logic is unchanged. (+1 test: `ClearImmediate_EmptiesTheInput`; the former "clears input" assertion became
   "keeps input".)
2. **Debugger entry point on the Procedure editor toolbar** — a bug-icon button immediately right of the
   existing Run Procedure button, **reusing the one launch path**. `MainWindowViewModel.OnDebugProcedureRequested`
   was refactored to extract `OpenDebuggerForProcedure(routineName)` (the sidebar handler now calls it), and
   the procedure detail VM raises a new `DebugRequested` intent (mirroring `RunExecuteRequested`) wired by the
   host to `OpenDebuggerForProcedure(detail.ProcedureName)`. `DebugProcedureCommand` is gated on
   `!IsNew` (an uncompiled New-procedure tab can't be debugged — mirrors `CanExecuteProcedure`); both inputs
   are fixed at construction, so no change-notification is needed. New `Icon.Bug` (Lucide) + `ProcedureDebugTooltip`.
   **No new debug logic** — it is only an additional entry point onto the existing `DebuggerTabViewModel` launch.

**Deferred (flagged to the user): Debug buttons on the Trigger / Package editors.** The user also asked for
these, but the debugger's current scope is **standalone procedures only** (D4/D5). Triggers (NEW/OLD context,
no input params) are **D10**; package members need package-qualified resolution (a later milestone);
`OnDebugProcedureRequested`/`FirebirdDebugMetadata` handle only `MetadataObjectKind.Procedure`. Adding a Debug
button there now would be a **dead entry point** (a broken promise, against the QA/honesty rules), so it was
**not** added — it belongs with the milestone that makes those debuggable. Functions likewise (not yet
supported). Recommendation recorded: add each editor's Debug entry point *with* its enabling milestone.

Build 0/0; **4756 tests green in one run**; smoke clean.

### Backlog (user, 2026-07-18 — NOT part of D5; do not implement now)

**Immediate should pre-validate syntax locally before hitting the server.** Today an invalid Immediate
expression is sent to Firebird and the user sees a server SQL error. The user would prefer Immediate to run
the entered text through the **existing `EditorLanguageService` (Lexer + Parser + Diagnostics)** *before*
issuing the `EXECUTE BLOCK`, so **syntax** errors are caught locally (no round-trip). Constraints: **reuse the
existing Language Service** — do **not** build a separate debugger parser/validator; **syntax** errors are
caught locally, **semantic + execution** errors stay the server's responsibility (the harness). A future UX
improvement, explicitly not D5. Recorded here per the user's request.

## D5 — Expression evaluation surface, seam (b): Watches (2026-07-18). **D5 IS COMPLETE.**

**Cel:** the third surface of §9.5's *one engine, three surfaces* — **Watches**: expressions re-evaluated
after every step, persisted per routine, with the non-pure ones flagged.

### One engine — Watches add no evaluation mechanism (D5 risk #1)
Every watch is evaluated through the **same** `DebugSession.Evaluate(expression, Expression)` built in seam
(a). The tab VM re-evaluates all watches after each pause; there is **no** separate watch evaluator.
- **Auto re-evaluation.** After every pause-producing engine op — a step (`RunStepAsync`), launch/entry
  (`LaunchAsync`), and an Immediate run that may have mutated the frame (`EvaluateFragmentAsync`) — the VM
  calls `EvaluateWatchesAsync()` **while `Phase == Busy`**, so the non-thread-safe `DebugSession` is never
  touched concurrently (the same mutual-exclusion-via-state-machine rule as seam a). Each watch is a wire op,
  run on a background thread (`Task.Run`), then the row values are applied on the UI thread. When the session
  is not paused (completed/faulted) the rows reset to the `—` placeholder — there is no live frame.
- **`WatchRowViewModel`** (App) — unlike the other read-only row VMs it is **mutable** (`ObservableObject`):
  its `ValueText`/`IsError`/`Evaluated` update each pause; `Expression` and the side-effect flag are fixed.

### Persistence per routine
New Core `WatchStore` (`EmberTern.Core.Settings`) — a section facade over the shared `settings.dat`
(mirroring `ParameterHistoryStore`), owning `UserSettings.DebugWatches` (one `DebugWatchEntry` per
`(ConnectionId, ObjectName)`; additive property, **no schema bump** — an old file simply has none). The VM
**loads** watches in its ctor (they show, unevaluated, before launch) and **saves** the whole list on every
add/remove. `MainWindowViewModel` builds one `WatchStore` on the same directory+protector as
`ParameterHistoryStore` and passes it to the debugger tab. Stop keeps the (persisted) rows and only resets
their live values.

### Side-effect flagging (§9.5 guard)
New pure Core `WatchSideEffectDetector.HasSideEffect(fragment)` — an auto-re-evaluated watch runs real SQL in
the debug transaction, so a watch that is **not a pure expression** is flagged. It **reuses the one
`SqlLexer`** (Developer Contract — no new parser) and looks for a side-effecting keyword among the fragment's
**tokens** (`INSERT`/`UPDATE`/`DELETE`/`MERGE`/`EXECUTE`/`POST_EVENT`); a keyword only matches as a bare
token, so a string literal (`'please UPDATE'`) or a quoted identifier (`"UPDATE"`) never trips it. It is a
deliberately conservative **lexical warning cue**, not semantic analysis (a UDF with hidden side effects is
inherently the server's domain). The flagged rows show a `±` marker with an explanatory tooltip. (This is the
minimal honest flag; the user's separate backlog item — richer pre-validation via `EditorLanguageService` —
stays deferred.)

### UI
The right panel splits into **Variables** (top) + **Watches** (bottom, own splitter): a watch input (Enter =
Add) + Add button, and the list — each row `± | expression / value | ✕`, value in `SubtleForegroundBrush`
(or `ErrorBrush` on a raise), the `✕` removing via the tab VM's `RemoveWatchCommand` (ancestor binding). All
theme tokens; no new colours.

### Deviation from the plan (documented, same rationale as seam a)
No standalone `WatchesPanelViewModel` — the Watches collection + input + add/remove + the re-evaluation loop
live on `DebuggerTabViewModel`, consistent with `Variables`/`ExecutedSql` and the seam-a `EvaluateController`
decision (a separate panel VM would need the session + the evaluation path + persistence — tight coupling to
the tab VM for no gain). `WatchRowViewModel` is the per-row VM.

### Tests (+26)
- `WatchStoreTests` (+6): round-trip across instances (in order), replace, empty removes, per-routine, blank
  key disables.
- `WatchSideEffectDetectorTests` (+14): pure expressions (incl. a scalar subquery, an equality, a keyword in
  a string) not flagged; DML/EXECUTE/POST_EVENT/MERGE flagged; case-insensitive; blank.
- `DebuggerTabVmTests` (+6): add-when-paused evaluates immediately + clears input; re-evaluates after each
  step; side-effect flag; remove; Stop resets values but keeps rows; watches persist per routine across VM
  instances.

**Build 0/0; 4782 tests green in one run; smoke clean. Live evaluation of watches awaits user confirmation**
(needs a server; the shared engine's live fidelity is the same as seam a's). Manual QA checklist prepared.

**D5 IS COMPLETE** — Evaluate + Immediate + Watches, all on the one `HarnessBuilder`/`DebugSession.Evaluate`
engine (decision 6).

## D5 — Debugger panel layout redesign (2026-07-18; UX only, no debugger logic change)

After manual QA the user asked for a **layout redesign before D6+ adds more panels** (cheaper now than later).
**Presentation only** — `DebugSession` / `Evaluate` / `WatchStore` / `WatchSideEffectDetector` and all Watches
functionality (persistence, auto-re-evaluation) are untouched; only the view + two presentation VM properties
changed.

**Analysis / decision (endorsed the user's proposal with one refinement).** Future debugger panels — Call
Stack, Breakpoints, Output, the selectable-procedure result grid — are **width-hungry** (tables / logs), not
height-hungry. So:
- **Right panel = Variables only** (the primary inspection surface, full editor height — widened to 300).
- **Bottom panel = a full-width, collapsible `TabControl`** (`bottom-tab` style, exactly like the SQL editor)
  with **Immediate / Executed SQL / Watches**; a new tab (Call Stack / Breakpoints / Output) is one `TabItem`.
  **Refinement over "bottom under the editor only":** the bottom spans the **full width** (under editor +
  Variables) — it mirrors the SQL results panel the user referenced (same collapse intuition) and serves the
  width-hungry future tabs; Variables get the full height whenever the bottom is collapsed (the common
  focused-stepping state). The one conscious trade-off (Variables always-visible on the right, Watches as one
  bottom tab) follows the user's stated priority; docking is a separate future concern, not precluded.
- **Collapse** mirrors the SQL results panel: a chevron overlays the tab strip (`ChevronsDown` collapse /
  `ChevronsUp` expand, bound to `IsBottomPanelCollapsed`); the view toggles the bottom grid **row height**
  between the remembered (draggable) pixel height and **Auto** in code-behind (`ApplyBottomPanel`, mirroring
  `MainWindow.ApplyResultsRowForActiveTab`). Each tab's content binds `IsVisible` to `!IsBottomPanelCollapsed`,
  so an Auto row measures to just the tab strip — collapsed → editor + Variables reclaim the height.

**Immediate vs Executed SQL split (non-redundant).** The Immediate tab is a self-contained REPL: the input +
the **latest** result inline (new presentation prop `LatestEvaluation` = newest audit row); the Executed SQL
tab is the **full audit history**. New VM presentation members only: `IsBottomPanelCollapsed` +
`ToggleBottomPanelCommand`, `LatestEvaluation` (+ `HasLatestEvaluation`). Watches moved verbatim into its tab
(same input + list + `±` flag + `✕` remove).

Build 0/0; **4784 tests green** (+2 presentation: toggle-collapse, latest-evaluation-tracks-and-clears); smoke
clean. The live layout (collapse, tab switching, ancestor `RemoveWatchCommand` binding) **awaits user
confirmation** (the debugger tab renders only against a live DB).

**Next milestone: D6 (Cursor Bridge). D6+ not started.**

## Debugger tab UX follow-up — transient tab + double-click-to-collapse (2026-07-18)

Two small IDE-behaviour fixes surfaced during manual QA, landed as one commit *before* starting D6 (cheaper
now, before D6 adds panels/state). No debugger logic changed.

1. **Debugger tabs are session-transient — never persisted.** With a debugger tab open, closing the app then
   relaunching "restored" an empty tab. Root cause: `MainWindowViewModel.SnapshotCurrentTabs` skipped only the
   live-tool kinds (SecurityManager/TraceMonitor/SessionManager/GlobalSearch/ScriptExecutor); a `Debugger` tab
   fell through to the final `else` and was captured as a `CoreTabKind.Ddl` tab (routine name + empty DDL),
   which re-opened as an empty tab on the next launch. Fix: add `WorkspaceTabKind.Debugger` to that skip-list —
   a debug session is transient (rolled back on close), not a document. Consequences, all now correct: **app
   close** → not captured → **restart** restores nothing; **disconnect** already clears every visible tab, and
   `ClearWorkspaceTabs` now also tears the debug session down (`DisposeAsync` = §4.4 rollback + close the
   session's attachment) the same way it disposes the monitors — so a debug tab bound to the disconnected DB is
   closed *and* its attachment released. No new architecture — the tab was already `IsClosable`, already torn
   down on explicit tab-close (`RequestCloseTabAsync`); this only stops it leaking into persisted state and
   adds the disconnect teardown. Pinned by `WorkspacePersistenceVmTests.DebuggerTab_IsTransient_NotCaptured`.
2. **Double-click the bottom panel's tab strip to collapse/expand.** A second, more natural affordance beside
   the chevron button, reusing the **same** `ToggleBottomPanelCommand` (no second mechanism). Handled on the
   bottom `Border`'s `DoubleTapped` in `DebuggerTabView`: when expanded, only a double-tap whose source has a
   `TabItem` ancestor toggles (so double-clicking panel *content* — rows, inputs — is left alone; the selected
   content lives in the TabControl's ContentPresenter, not under a `TabItem`); when collapsed only the strip is
   visible, so any double-tap on the bar expands it. A double-tap that lands on the chevron `Button` is ignored
   (the button owns its own click). Presentation-only; view + `DoubleTapped` wiring only.

Build 0/0; **4785 tests green** (+1); smoke clean. Live behaviour (transient tab across restart/disconnect,
double-click collapse) awaits user confirmation (the debugger tab renders only against a live DB).

## D6 — Cursor Bridge (2026-07-18)

`FOR SELECT` bodies now step through a **real, incremental DSQL cursor** instead of the D2 hard stop. Landed
in two seams after the spec-mandated probes.

**Probes first (§F "verify, don't infer", spec §15.5).** Three probes ran before any code:
- **Binder for `FOR SELECT`** (point B, empirical): bare local refs in the query *are* surfaced
  (`role=Variable/Parameter`); colon `:name` refs are **not** (a single `Parameter` token, #238). Initially
  read as "existing architecture suffices."
- **Cursor interleaving on FB3** (managed driver): harness stmt while a cursor is open, resume, two cursors —
  **all succeed**, mirroring FB5 §15.3. Cursor Bridge feasible on FB3 + FB5. **FB4 unavailable** (only FB3.0 +
  FB5.0 installed) → unverified, recorded honestly (P2's FB2.5 posture).
- **`WHERE CURRENT OF`** on a separately-opened DSQL cursor: **fails**, SQL -504 "cursor not found in the
  current context"; `FbCommand.CursorName` not settable. → a §F boundary, not in D6's DoD.

**D6a — AST deepening (commit `5f7d222`).** `ForSelectStatement` gained `IntoTargets` (ordered, folded INTO
variable names) + `CursorName` (`AS CURSOR c`), parsed order-independently at paren depth 0. The interpreter
maps a fetched row's columns onto `IntoTargets` positionally; `CursorName` lets a `WHERE CURRENT OF` be
detected. Additive overlay — tokens round-trip (§0), binder + formatter untouched. Per Developer Contract #1
(don't token-scan the Firebird layer for structure that belongs in the AST). +6 `PsqlAstTests`.

**D6b — the bridge.** Pure Core `CursorBridge` (mirrors `HarnessBuilder`: `Build(source, loop) →
CursorQueryPlan` — the DSQL SELECT with frame refs rewritten to positional `?`, the ordered parameter names,
the INTO targets) + Firebird `CursorHandle : IDebugCursor` (holds the real `FbDataReader` open across steps,
**per-wire-op** command locking #236 — the lock is taken per fetch/close, never for the cursor's lifetime, so
harness steps inside the loop don't deadlock) + `FirebirdDebugExecutor.OpenCursor` glue (binds the plan's
parameter names from the frame, opens the reader; `FOR EXECUTE STATEMENT` → a clear §F refusal). +5
`CursorBridgeTests` (pure).

**The design correction (§F caught it live).** The first cut rewrote *every* frame ref the binder surfaced —
bare **and** colon. Live fidelity broke it: `SP_DBG_CURSOR` both `RETURNS (LINE_NO …)` and does
`SELECT LINE_NO …`, and the binder resolves the SELECT-list **column** `LINE_NO` to the output **parameter**
(locals shadow columns), so the column was rewritten to `?` → `SELECT ?, …` → **SQL -804 "Data type
unknown"**. This was invisible to the pure unit tests (valid-looking SQL) — only the sim-vs-real run exposed
it. **Fix:** rewrite **only the colon/`@` form** (`:name`/`@name` — Firebird's unambiguous variable syntax in
a query, a native DSQL bind once extracted); a bare name is a **column** and is left verbatim. This also
dropped the `SemanticModel` dependency from `CursorBridge` entirely. Gotcha #239.

**Lab zoo + fidelity (spec §15.5, the §2.1 proof).** `Lab/setup.sql` gained `SP_DBG_CURSOR` (single
`FOR SELECT` over `ORDER_ITEMS`, colon-param WHERE, INTO targets, running-sum body, SUSPEND per row) and
`SP_DBG_NESTED` (nested `FOR SELECT`, two simultaneous cursors, inner WHERE injects the outer frame's local).
The real executor drove `DebugSession` through them; outputs matched real execution exactly — including a
**fully stepped** run of `SP_DBG_CURSOR(1000)` (10 steps, per-step cursor fetch) and the nested case.

Build 0/0; **4797 tests green** (+11: 6 D6a AST, 5 CursorBridge); smoke clean; live fidelity proven. The
in-app stepping UX (breakpoints inside a loop body, Variables reflecting INTO targets live) awaits user
confirmation (renders only against a live DB). **Next: D7 (Variables window, full).**

---

## Bottom-panel splitter double-click — root-cause fix (2026-07-18)

Three prior commits (`1b77c55`, `c5bf882`, `282fd4d`) tried to fix "double-click the bottom-panel
GridSplitter to collapse, re-expand restores the height" by fiddling with the splitter gesture itself
(a `_splitterGestureHeight` snapshot captured on `PointerPressed` with `handledEventsToo`, restored before
the toggle). It still misbehaved: after a real drag, collapse + re-expand left the panel "glued" to the
editor.

**Root cause (found by comparing to the SQL editor, not the splitter).** `ApplyBottomPanel` mutated **only**
the bottom row (Row 2 of `DebugLayout`), never the top row (Row 0). Avalonia's `GridSplitter`
(`ResizeBehavior=PreviousAndNext`) between a `*` (star) top row and an absolute-pixel bottom row **converts
the star row to an absolute pixel height** on a genuine drag. Once Row 0 is stuck absolute, the grid has **no
star row** to reclaim vacated space — so collapse leaves a gap and re-expand can't re-establish the
editor↔panel relationship. The SQL editor never has this bug because `ApplyResultsRowForActiveTab` /
`ApplyResultsMaximized` are a **single re-normalization point that always sets both rows** (editor row → `*`,
results row → pixel/0) on every layout apply; the splitter's transient mutations never survive to the next
normalize.

**Fix (unify, delete the workaround).** `ApplyBottomPanel` is now the debugger's single re-normalization
point: it sets **Row 0 back to star** in both branches and Row 2 to Auto (collapsed) / remembered pixel
height (expanded) — structurally identical to `ApplyResultsRowForActiveTab`. With full re-normalization on
every toggle, the double-click's spurious micro-drag is absorbed exactly as the SQL editor's maximize/restore
absorbs it (collapse captures the post-drag height, imperceptibly close to the original), so the
`_splitterGestureHeight` field, `OnBottomSplitterPointerPressed`, and the `handledEventsToo` registration are
**deleted** — one collapse/expand logic, no per-gesture exception.

**Part 2 — the double-click still didn't hide the panel (part 1 was necessary but not sufficient).** On live
QA the *chevron button* collapsed/expanded perfectly, but *double-clicking the splitter* did nothing useful.
Both call the same `ToggleBottomPanelCommand` → `ApplyBottomPanel`, so the difference had to be the context:
the button fires when nothing is mid-gesture; the double-click fires **while the GridSplitter is still handling
its own pointer gesture**, and the collapse **hides that very splitter** (`IsVisible="{Binding
!IsBottomPanelCollapsed}"`). Toggling synchronously mid-gesture leaves the collapse half-applied. This also
explains the user's own SQL-editor comparison: that splitter's double-click does maximize/restore, which never
hides the splitter, so it works. **Fix:** defer the toggle — `Dispatcher.UIThread.Post(...)` it in
`OnBottomSplitterDoubleTapped` — so it runs after the gesture completes, making the double-click behave exactly
like the button. Gotcha #240 (both parts). Build 0/0; 4797 green; smoke clean; live behaviour awaits user
confirmation.

---

## D7 — Variables window, full (2026-07-18)

The basic D4 list becomes the rich window of spec §9.4. Split into two seams; both build green.

**Seam (a) — grouping / kinds / change-highlight / pins / filter.** `DebugVariableRowViewModel` was rewritten
as a *mutable* row updated **in place** across steps, so pins, group expansion and selection survive a step.
`Variables` stays the flat roster; new `VariableGroups` is its grouped + filtered presentation over the *same*
row instances (one roster, two projections — no duplicated logic). New `DebugVariableGroupViewModel` (Header /
Rows / IsExpanded). Groups shipped: **Pinned / Parameters / Locals**, reusing persistent group instances so
IsExpanded survives the per-step rebuild; empty groups are hidden. `Context` (triggers, D10) and `Cursors`
(needs cursor surfacing) are deliberately **not** shipped as empty groups (gotcha #233). Each row carries a
kind glyph (⬤ IN / ◑ OUT / ○ local) coloured by a theme **key string** via `IconBrushConverter` (never a
brush — rule #1); the declared type; a distinct `<null>` (subtle + italic); and per-step change-highlight
(new `DebugVariableChangedBrush`, both dictionaries) computed by reusing `FrameValues.Snapshot()` with the
baseline reset on frame-identity change. Type-to-filter mirrors the sidebar. `TogglePin` moves a row to the
top Pinned group (session-scoped; not a Watch — §9.5).

**Seam (b) — inline edit + data tips + lazy BLOBs.**
- **Data tips** (§9.4): a `DebugValue` section was added to the ordered aggregate `HoverInfo` (no
  `IHoverProvider` — extended, per rule #2). `HoverInfoEngine.GetHover` gained an *optional*
  `Func<string, DebugHoverValue?>? debugValueLookup` (default null → the SQL/object editors are unaffected);
  it is an **input**, so the no-analysis guarantee still holds by signature. Threaded through
  `NavigationController.Attach` and `SqlEditorBehavior.Attach` (both optional, default null);
  `DebuggerTabView` supplies a lookup that reads the paused frame's value from the VM's roster (the same rows
  the panel shows — one truth), gated to the paused state. The card renders the data tip **first** (in a
  paused debugger the value is the reason you hovered), reusing the existing `QuickInfoView` chrome. A
  colon/at-sigil is stripped when a reference is unresolved.
- **Inline edit** (§9.4 "trivial — the frame is client-side truth"): double-click a value → a text box
  (Enter commits, Esc cancels). Setting is `frame.SetResolvedValue`; the only real work is
  `TryParseEditedValue`, a best-effort typed parse (InvariantCulture, prefers the value's CLR type, else the
  type name). A parse failure keeps the box open with a red border — **shape validated at edit time; the real
  domain CHECK still surfaces on the next injection** (§3.4/§F — never a guessed value). The edit box is
  seeded from the value's **full untruncated** raw string, never the possibly-truncated display text (§0).
- **Lazy BLOBs**: a binary BLOB (`byte[]`) renders as a `[BLOB · N B]` placeholder and is **not** text-editable
  (`IsEditable`); a long text value is truncated inline (256 chars) while staying fully editable. A dedicated
  "…→ value viewer" popup is a documented follow-up — no reusable viewer exists to wire yet.

Build 0/0; **4807 tests green** (+4 seam a: grouping / change / pins / filter; +3 seam b hover; +3 seam b
inline edit). Smoke clean. Live behaviour (data tips, inline edit, change-highlight against a real session)
**awaits user confirmation**. **D7 DONE. Next: D8 (Call stack + nested stored routines).**

### D7 UX backlog (user, 2026-07-18 — deferred, NOT to implement ad hoc)

After confirming D7 works (grouping, filtering, pinning, inline edit, live value updates while stepping all
behaved as expected), the user filed three UX items for later sessions:

1. **GridSplitter double-click — still not identical to the main SQL Editor.** Two fixes shipped this session
   (re-normalize both rows — gotcha #240 part 1; `Dispatcher.Post` the collapse off the splitter gesture —
   part 2) and it is still not right. **Directive: stop iterating on the current implementation.** The user
   believes the project already contains the correct mechanism (the SQL Editor's results-panel
   collapse/maximize) and wants a **dedicated session to reuse that exact mechanism** rather than more local
   patches. Left open.
2. **Variables icons too similar** — every row reads as a dot. Distinguish **Parameters / Returns(OUT) /
   Locals / Cursor variables** (cursors land in a later milestone) with distinct **icons or clearly different
   colours**, not near-identical glyphs.
3. **Pinned star blends into the kind glyph** — consider a different colour (e.g. yellow), more spacing from
   the kind glyph, or a **pin icon** instead of the star.

Per the standing directive ([[feedback-debugger-ux-polish-backlog]]): fix these as UX/theme in the view +
tokens; never push logic into the VMs to paper over UX.

### Post-D7 bugfixes (2026-07-18) — splitter (real root cause), Variables icons, pin

Three UX bugfixes before D8 (user-reported after D7 manual QA).

**1. GridSplitter double-click — the ACTUAL root cause (parts 1+2 were wrong places).** Earlier this session
re-normalized both rows (part 1) and deferred the toggle off the gesture (part 2); neither fixed it. The user
directed: stop iterating, find the SQL Editor's mechanism, reuse the exact pattern, refactor if needed. The
structural divergence, found by that comparison: the debugger bound the **splitter's own `IsVisible` to
`!IsBottomPanelCollapsed`** — the very state the splitter's double-click toggles, so the control was entangled
with its own action. MainWindow's results splitter keeps its visibility on an **independent** condition
(`IsQueryTabActive`), never on what `ToggleResultsMaximized` does. Fix: the debugger splitter is now **always
visible** while the debug view shows (it already lives inside a host gated on that), and the double-click
handler is synchronous — structurally identical to `OnResultsSplitterDoubleTapped`. The `Dispatcher.Post`
deferral from part 2 was removed. Gotcha #240 (part 3 = the real fix). Awaits live confirmation.

**2. Variables kind icons — distinct shape + colour per kind.** IN and OUT previously shared the accent colour
and near-identical dot glyphs, so every row read the same. Now each kind has a distinct SHAPE — ▶ IN / ◆ OUT /
● local (all full-size black shapes → equal optical mass; the initial small ▸ read lighter and was bumped to
▶) — AND a distinct hue via dedicated theme tokens (both dictionaries): `DebugParamInBrush` (blue),
`DebugParamOutBrush` (amber), `DebugLocalBrush` (green). The row VM's `KindGlyph`/`KindBrushKey` map per kind;
the colour is still a theme **key string** resolved through `IconBrushConverter` (never a brush — rule #1).
Cursor variables get their own glyph/token when a later milestone surfaces them.

**3. Pinned star.** The pinned ★ shared the accent colour with the kind glyph beside it, so they blended. Now
the pinned star is **gold** (`DebugPinBrush`, both dictionaries), the unpinned ☆ is faint (subtle + 0.6 opacity),
both a touch larger, with a 4px gap added between the pin button and the kind glyph.

Build 0/0; **4807 green** (no test changes — no test asserts glyph/colour strings); smoke clean. UX awaits the
user's visual confirmation.

## D8 — Call stack + nested stored routines, seam (a): AST deepening + Frame model (2026-07-18, pure Core)

D8's DoD ("A→B→C stack navigable; simulated vs real") requires a **faithful** Step Into of a stored
procedure — pass its arguments, write its `RETURNING_VALUES` back — which the AST could not express. Analysis
(presented to the user before coding, per the "stop and recommend if AST/binder deepening is needed" rule)
found two structural gaps; the user ratified **full D8, starting from a pure-Core seam (a)**. Seam (a) lands
the AST + Frame-model foundation with **zero server** in the loop and no production behaviour change — the
executor's `ResolveRoutine` still returns null (seam b activates it), so no callee frame is pushed in prod yet.

### 1. AST — `ExecuteProcedureStatement` deepened (Contract #1: structure belongs in the AST, not a token scan)

The node carried only `ProcedureName`. It now also produces (parser producer, additive overlay — §0 tokens
still round-trip; formatter untouched, token-based):
- **`Arguments`** — `IReadOnlyList<CallArgument>`, each a **source span** of one positional argument (not a
  tree child — it carries only a span, like `ForSelectStatement.IntoTargets`). A step-into slices the span and
  evaluates the argument expression **in the caller frame** to seed the callee's input parameters (seam b). The
  argument's ordinary-expression interior stays in the tokens (structural-depth boundary — no subquery/CASE
  recursion into arguments in D8, a documented boundary).
- **`ReturningTargets`** — `IReadOnlyList<string>`, the `RETURNING_VALUES` targets folded to the resolution
  convention (reusing the one `ForTargetName` INTO-target reader), so they key straight into a frame.

Parser: `ReadProcedureCallParts` (SqlParser.cs) — skips the (possibly dotted) name, finds the top-level
`RETURNING_VALUES` (an identifier, matched by text — mirrors the binder), splits the argument and returning
sections at paren-depth-0 commas, tolerating the optional surrounding parens Firebird allows in either section.
The binder is **unchanged**: it already binds the `:var` argument/returning tokens via `BindPsqlExpression`
(the sanctioned expression-interior token walk), so the read/write sets already see them — no new binding.

### 2. Frame model — `LexicalParent` split from the call-stack `Parent`; `OutputParameterNames`

The load-bearing correction: D1's `Frame.Parent` conflated **two roles** — the call-stack parent (caller) and
the lexical/scope-chain parent (closure). For a **stored** routine these differ: the callee has a caller (call
stack, savepoint nesting, `RETURNING_VALUES` write-back, the future caller-line marker) but is a **closed
scope** — it cannot see the caller's variables. D1 never exercised this (only the root frame existed); D8 is
the first milestone that pushes a second frame, so it is where the two first diverge. Left conflated, in seam
(b) an unassigned callee local `X` would resolve up to a caller `X` and inject the wrong value — a §F bug.

- New `Frame.LexicalParent` (distinct from `Parent`); `TryResolveValue` / `SetResolvedValue` now walk
  `LexicalParent`. A stored callee gets `LexicalParent = null` (closed scope, D8); a **local** sub-routine will
  get its declaring frame (spec §6 closure — D9). This is exactly the spec §6 "lexical parent" language, now
  first-class.
- New `Frame.OutputParameterNames` (declaration order) + `DebugRoutine.OutputParameterNames` /
  `DebugRoutine.LexicalParent` — additive ctor params (defaults preserve every existing caller).

### 3. Interpreter — `RETURNING_VALUES` write-back on normal return

`AdvanceToNextStepPoint` now calls `ApplyReturningValues` before releasing a completing frame's savepoint: on a
callee's **normal** exit, its output parameters are bound **positionally** into the caller's `RETURNING_VALUES`
targets (spec §5 — a real `EXECUTE PROCEDURE` binds outputs into the caller; a simulated frame reconstructs
that client-side from the callee's own values). A no-op for the root / a call with no `RETURNING_VALUES` / an
unhandled unwind (the `ExceptionRouter` rolls those back — a faulted call returns nothing); zips to the shorter
list on a malformed pair, never throws.

### A D1 test that encoded the wrong assumption (corrected, not deleted)

`ScopeChain_InnerFrameResolvesAndWritesOuterVariable` used `execute procedure p` as a **proxy** for the
scope-chain mechanism and asserted the callee resolves the caller's variable — true only for a *local*
sub-routine (D9), **false** for a stored one (D8). Split into two honest tests:
`StoredCallee_IsAClosedScope_DoesNotSeeCallerVariables` (D8 default) and
`LocalCallee_IsAClosure_ResolvesAndWritesOuterVariable` (the D9 mechanism, driven by the fake executor's new
`asLocalClosure` mode that sets `LexicalParent = caller`). Plus `StepInto_ReturningValues_...` (the write-back)
and 4 `PsqlAstTests` (paren/bare arguments, paren/bare + folded returning targets, the no-arg regression).

Build 0/0; **4813 green** (in one run); smoke clean. **Nothing user-visible yet** — seam (a) is the
foundation; seam (b) (Firebird `ResolveRoutine` + lab fidelity) and seam (c) (Call Stack / breadcrumbs / frame
nav UI) follow. Gotcha #241 (the LexicalParent-vs-Parent distinction).

### D8 seam (b) part 1 — `FirebirdDebugExecutor` multi-routine context (2026-07-18, behavior-preserving)

`FirebirdDebugExecutor` held **single-routine** state (`_source` / `_model` / `_variableTemplates` /
`_outputParameters`) — fine while only the root frame ever ran, but a D8 call stack activates **distinct
routines**, each with its own source / model / variable templates / outputs. Refactored the four fields into a
`Dictionary<BlockStatement, RoutineContext>` keyed by the routine's **body node** (the stable per-frame key):
every method now reads `Ctx(frame)` (via `frame.Body`) and threads its `Source` / `Model` / `VariableTemplates`
/ `OutputParameters` through the (now `static`) helpers `ResolveReadWrite` / `BindValues` / `SnapshotOutputs` /
`Slice` / `ConditionExpression` / `AllTemplateNames`. The root registers its context in `CreateAsync`; a
stepped-into stored routine will register its own in `ResolveRoutine` (part 2). **Recursion is correct for
free**: the same body on two frames shares one context (same declarations + types; the per-frame *values* live
on the `Frame`).

**Behavior-preserving**: `ResolveRoutine` still returns null, so no callee frame is pushed and the single
routine's path is byte-for-byte the same lookup as before — the whole suite stays green (4813) and the live
single-routine behaviour is unchanged. This is the necessary foundation for part 2 (`ResolveRoutine` — fetch +
parse + metadata the callee, evaluate its arguments through the harness, register its context) + the mandatory
**lab fidelity** verification (nested procedures, simulated vs real), which the §F rule requires be proven
against real execution in a focused session.

Build 0/0; **4813 green**; smoke clean.

### D8 seam (b) part 2 — `ResolveRoutine` + argument seeding + live fidelity (2026-07-18). **D8 DoD MET.**

The capability that makes the call stack real: **Step Into a stored procedure**. `FirebirdDebugExecutor.ResolveRoutine`
now, for a standalone `EXECUTE PROCEDURE`:
1. **fetches the callee's source** on the DEBUG session (its own attachment + tx) — a new internal
   `FirebirdDdlReader.BuildProcedureSourceAsync(connection, tx, …)` seam reusing the exact CREATE-OR-ALTER
   reconstruction the metadata readers use (Contract #17: no second DDL builder), holding the session command
   lock across the multi-command build (#98/#120/#236);
2. **parses it** the same way the launch path does (gotcha #238: the strict whole-`CREATE PROCEDURE` parse, so
   the body's declares are in scope) → `SemanticModel` + `BlockStatement` body;
3. **resolves its frame variable templates** (R2/R3) via `FirebirdDebugMetadata` — now also returning the
   ordered **input** parameters (`DebugFrameLayout.InputParameters`);
4. **evaluates the call's arguments in the CALLER frame** through the SAME harness as a step (Contract #4 — no
   second evaluator): a Statement-mode `EXECUTE BLOCK` declaring the caller's variables (injecting its
   in-scope reads, §3.5) and assigning each argument expression to a synthetic `ET_ARG_i` **typed as the
   callee's i-th input param base type** (R2), so the server computes each argument with full fidelity and
   returns it typed → the values seed the callee frame's input parameters positionally;
5. **registers the callee's context** (multi-routine map from part 1) and returns a `DebugRoutine` (a stored
   routine ⇒ `LexicalParent = null`, a closed scope — gotcha #241).

The interpreter (D8 seam a) then pushes the callee frame; on its normal return `ApplyReturningValues` binds
its outputs into the caller's `RETURNING_VALUES` targets. **Every unresolvable call runs in place = step-over,
100% faithful (§5.3):** a non-`EXECUTE PROCEDURE` step, a call with no readable name, a package/qualified name
(D11), a local sub-routine (D9), or a callee whose source/metadata can't be read (`FbException` → null).

**§F live boundary caught by probing, not reasoning (gotcha #242):** the argument-seeding harness first tried
`ET_ARG_0 = :P;` — and a quick live test showed `:name` is a **SQL error** as a PSQL assignment RHS (SQL -104;
the colon form is query-only). Fix: `RewriteColonRefsToBare` rewrites each `:name`/`@name` (a `Parameter`
token) to its bare name **by span** over the statement's tokens (a colon inside a string literal is a `String`
token, untouched) — mirroring the Cursor Bridge's span rewrite (gotcha #239; there → `?`, here → bare name).

**Lab fidelity — the mandated §2.1 proof (`tools/probes/DebuggerFidelityProbe`, spec §15.6).** Extended the lab
zoo with a 3-level chain — `SP_DBG_LEAF` (`Q = P + 1`, executable), `SP_DBG_MID` (calls LEAF, `Q = T*2`),
`SP_DBG_ROOT` (calls MID, `RESULT = T + 100`, selectable). The real executor drove `DebugSession` Step Into
through it: **depth reached 3 (`SP_DBG_ROOT → SP_DBG_MID → SP_DBG_LEAF`), and the simulated `RESULT = 112`
equalled real execution's `112`** — arg seeding + `RETURNING_VALUES` write-back faithful across three frames.
**ALL PASS.**

Build 0/0; tests green (user-confirmed — the full run is slow in this env); smoke clean. No new unit tests
(the value is the live fidelity proof, which a unit test structurally cannot give — Contract #12). **D8 seams
(a) + (b) COMPLETE.** Remaining for D8: seam (c) — the Call Stack panel / Breadcrumbs / frame-nav UI over this
now-real call stack.

### D8 seam (c) part 1 — Call Stack panel (display-only) (2026-07-18; awaits visual confirmation)

The first UI over the now-real call stack: a **Call Stack** panel (a new bottom `TabItem`, joining
Immediate / Executed SQL / Watches). It lists frames **innermost-first** (`DebugSession.CallStack`), each row
showing the routine name, its position **line** (the current statement for the innermost frame, the **call
site** for a caller — computed against **that frame's own source**, which is why the small Core enabler
`Frame.Source` / `DebugRoutine.Source` / `DebugSession(rootSource)` was added: a frame carries its routine's
text so its line resolves and, later, its source can be shown), the current-frame marker (▶), and the
**simulated-frame indicator** (△ + tooltip) on any frame reached by Step Into — interpreted, so it can differ
from real execution (§5.3; the root is not marked). `DebugFrameRowViewModel` (immutable row), rebuilt each
pause by `RebuildCallStack` (presentation only — it reads the engine's stack, never touches the session);
cleared when not paused.

**Deliberately display-only (documented seam-c boundary).** Full frame-selection **navigation** (selecting a
caller frame repoints the editor source **and** the Variables panel) needs the callee's **own semantic-model
roster** (its params/locals with kinds + declared types) surfaced to the VM — the roster lives in the
executor's per-routine context (D8 seam b part 1), not in the VM's single root model — so it is real
infrastructure, not a mechanical add. That, plus **Breadcrumbs** (a *shared* editor feature, not a
debugger-local copy) and **Peek Frame** (reuse Peek Definition), are **seam (c) part 2**. This part gives the
primary value now — you SEE the A→B→C stack with the simulation indicator — as a clean, green increment.

Build 0/0; +1 `DebuggerTabVmTests` (`CallStack_ShowsRootFrame_WhilePaused_ClearedOnStop`; the debugger subset
of 126 tests green — the full run hangs in this env, run manually); smoke clean. **Awaits the user's visual
confirmation** (UI, per the QA rule).

### D8 seam (c) part 2 — frame navigation (2026-07-18; awaits visual confirmation). **D8 IS COMPLETE.**

The display-only panel becomes navigable. First real use of the Call Stack surfaced two gaps that share one
cause: after a Step Into (1) the editor **stayed on the parent's source** instead of following the callee, and
(2) the Variables panel kept showing the **root routine's roster** — so a callee's own locals were missing and
the caller's like-named variables lingered. Both are "the UI is pinned to the root, not the current frame."

**Per-frame model, surfaced without a re-parse (Developer Contract #1).** The roster the Variables panel
projects is a property of a *routine* — its declared parameters (with IN/OUT direction) and locals, with types
— which needs the routine's whole `SemanticModel` (parameters come from the `CREATE` header, not the body). The
root's model lives on the VM; a callee's was built by `FirebirdDebugExecutor.ResolveRoutine` and lived only in
its private per-routine context. Rather than re-parse the callee source in the VM (Contract #1 forbids it, and
it duplicates work), the model is **threaded onto the frame exactly as `Source` already is** — a new
`Frame.Model` / `DebugRoutine.Model` / `DebugSession(rootModel)`, filled by the launcher (`spec.Model`) for the
root and by `ResolveRoutine` (the very model it already built) for a callee. The interpreter never reads it;
it is carried for the UI, precedent set by `Frame.Source`. The Variables panel now builds its roster from
`frame.Model` (falling back to the VM's root model when a frame carries none — the fake-driven tests), so it
shows **that frame's** parameters + locals.

**One selection truth.** The VM gained a `_selectedFrame` (the inspected frame, reset to the innermost on every
pause) and a single `ApplySelectedFrame(frame, computeChanges)` that sets **everything together** — `SourceText`
(→ the frame's source), the current-line marker (→ the frame's position in *its own* source), the Variables
roster + values, and both selection controls (`SelectedFrameRow` for the Call Stack list, `SelectedBreadcrumbIndex`
for the breadcrumb bar) under a `_syncingFrameSelection` guard. So a frame and everything mirroring it cannot
disagree, and the three ways to pick a frame — a Call Stack row, a breadcrumb, `Ctrl+Alt+Up/Down` — all route
through the one `SelectFrame`. Selection is **navigation, never execution**: it reads `DebugSession.CallStack`
and never touches the session, and the change-highlight step-baseline is only updated on the innermost-frame
step path (`computeChanges: true`), so browsing a caller never disturbs it.

**Caller-line bug fixed (seam c part 1).** A caller's "current line" is the call it made into its child —
`stack[i-1].CallSite`, a statement in **this** frame's source — not `frame.CallSite`, whose offset is in the
*parent's* source. Part 1 used the latter (measured against this frame's text), a latent mismatch that only
looked right when two routines shared layout. See gotcha #243: an offset only means something against the exact
source it came from, so switching the shown document must switch the marker offset to that document's space in
the same step.

**Breadcrumbs are a genuinely shared control**, not a debugger-local copy (the plan's directive). New
`EmberTern.App.Controls.BreadcrumbBar` (`.axaml` + code-behind) is generic — an `ItemsSource` of arbitrary
items (each rendered by `ToString`) + a two-way `SelectedIndex`, "›"-separated clickable segments, theme tokens
only, zero debugger knowledge — with the debugger as its first consumer (fed the call stack outermost→innermost,
so it reads left-to-right as the call chain). Any editor surface can reuse it.

**Peek Frame** is a transient card (double-click a call-stack row) previewing that frame's routine source
without changing the pinned frame — a debugger-local card, because the editor's Peek Definition is private to
`NavigationController` (reusing the *pattern*, not the private impl).

**Breakpoints stay root-routine-scoped.** They belong to the launched routine; while the editor shows a
different frame's source (a stepped-into callee, or a selected caller other than the root) the breakpoint
offsets are in a different coordinate space, so `BreakpointOffsets` surfaces none and `ToggleBreakpointAt` /
`RunToCursorAsync` no-op (gated on `IsViewingRootSource`). Stepping (Into/Over/Out/Continue) is unaffected;
nested-routine breakpoints are D12. To manage breakpoints you view the root frame — which frame selection makes
one click away.

**DoD met:** an A→B stack is navigable; selecting a frame repoints editor + variables; breadcrumbs mirror the
stack; frame savepoints (unchanged from D1) are correct on unwind. Build 0/0; **+5 `DebuggerTabVmTests`**
(`StepInto_PushesCalleeFrame_SwitchesSourceAndRoster`, `SelectingCallerFrame_RepointsSourceRosterAndMarker`,
`MoveFrameSelection_WalksTheStack_BothDirections`, `Breakpoints_AreRootScoped_HiddenWhileViewingACallee`,
`StepInto_PeekFrame_ReturnsCalleeSource`) driven by a fake executor whose `ResolveRoutine` returns a real parsed
callee; full suite green (run manually — the full `dotnet test` hangs in this env, #94/#226); smoke clean.
**Awaits the user's visual confirmation** (UI, per the QA rule). Gotchas #241/#242/#243. **Next: D9 — local
procedures & functions (the flagship).**

---

## D9 — Local Procedures & Functions (the flagship) — the §6.3 closure version gate (2026-07-18)

D9's first, mandatory unit is the spec **§6.3 version gate**: §6.1 measured sub-routine closure semantics on
**FB5.0 only**, and FB3 historically documented sub-routines as having *no* access to outer variables. The
plan makes this a hard blocker — *"Run §6.3's FB3/FB4 probes first"* — because if FB3 differs, the D9 closure
harness must branch on server version. Verify, don't infer (Developer Contract).

**The probe** (`tools/probes/Fb3ClosureProbe`, raw `EXECUTE BLOCK` against a throwaway scratch DB on each
instance — deliberately **no EmberTern interpreter**, so it measures the *engine*, not the shipped code):
three anonymous blocks, each declaring an outer variable and a sub-routine that reads it (Q2), sees it mutated
between calls (Q3), or writes it (Q4). FB3 on port 4050, FB5 on 3050. FB4 is **not installed** in this
environment (only FB3.0 + FB5.0 listening), recorded unverified — the same honest posture as P2's FB2.5 and
D6's §15.5 [11].

**Result (decisive):**

| | FB 3.0.13 | FB 5.0.3 |
|---|---|---|
| Q2 sub-fn *reads* outer | **REJECTED** — SQL -206 "Column unknown `OUTER_V`" at compile | **COMPILED** — `RESULT = 6` |
| Q3 sees outer *mutated* (byref) | **REJECTED** (-206) | **COMPILED** — `R1 = 5, R2 = 99` |
| Q4 sub-proc *writes* outer | **REJECTED** (-206) | **COMPILED** — `RESULT = 77` |

**FB3 sub-routines are CLOSED scopes; FB5 sub-routines are true closures (read + write, by reference).**
Exactly what §6.3 anticipated ("if true, FB3 is *simpler* — no closures").

**Why this is elegant, not a complication.** The D8 `Frame.LexicalParent` split (gotcha #241) already models
both worlds — D8 gave stored callees `LexicalParent = null` (closed) and *reserved* `LexicalParent = declaring
frame` for D9 local routines. The gate simply tells D9 which to pick, by server major:

- **FB3 → `LexicalParent = null`.** Not just "simpler" — *forced correct*: a sub-routine that references an
  outer variable **cannot compile in the database on FB3** (-206), so no stored FB3 routine can contain one; a
  closed frame reproduces the engine with 100% fidelity by construction. The step-over closure harness injects
  **only the call arguments** — there are no captures.
- **FB5 → `LexicalParent = declaring frame`.** Outer reads/writes resolve up the scope chain against
  `FrameValues`; the harness injects the captured read set (R1–R4), carries the sub-routine declaration
  verbatim (R5), and reads mutated captures back. The carried declaration references the parent's variables,
  which the harness declares at the enclosing level — FB5's by-reference capture makes it work with no extra
  machinery.
- **FB4 → unverified**, a documented §F boundary; re-run `Fb3ClosureProbe` against an FB4 instance when one
  exists.

The branch is a single predicate on `FirebirdDdlReader.ParseServerMajor` (already reused from P2's connect
gate) at frame construction — **no new abstraction**, matching the plan's "New: little or nothing."

**State.** Pure gate work: a new out-of-solution probe + design-doc updates (spec §6.3 resolved, §15.7 log
added, compatibility matrix + open-items + roadmap rows updated; plan D9 risk resolved). **No production code
touched** ⇒ build 0/0 and the test suite are unaffected. Committed as the D9 gate. **Next session: D9 seam a**
— `FirebirdDebugExecutor.ResolveRoutine` resolves a local `DECLARE PROCEDURE/FUNCTION` call to a real frame
(interpret §6.2a), picking `LexicalParent` by version; then seam b — the closure harness + transitive
read/write-set fixpoint over the sub-routine call graph + R5. See the handoff at the end of this session.

## D9 — Local Procedures & Functions (the flagship) — seam (a) Part 1: AST deepening + binder + extractor (2026-07-18)

The plan's D9 seam (a) is *"`ResolveRoutine` resolves a local `DECLARE PROCEDURE/FUNCTION` to a real
interpreted frame"* — but the moment we started it, the spec §6.2a assumption ("just interpret the local
routine's body") proved too optimistic against the **existing AST**: a local sub-routine was **not modelled
at all**. Its header (`DECLARE PROCEDURE p (…) … AS`) was a lossless `PsqlLeafKind.Other` leaf and its body a
bare sibling `BlockStatement` — both dropped into the enclosing body's flat `Statements`. Two consequences,
each fatal to a faithful step-into: the interpreter would step **onto the unrunnable header** and **through
the sub-routine's body as if it were the enclosing routine's main flow**; and `ResolveRoutine` would have to
**re-scan tokens** to recover the callee's structure — a direct **Contract #1** violation ("the parser owns
structure; the executor never re-derives it"). So — exactly as D8 seam (a) deepened the AST for `ExecuteProcedure`
arguments/`RETURNING_VALUES` before its Firebird seam — D9 seam (a) is split: **Part 1 (this) = pure Core AST
+ binder + extractor foundation; Part 2 = the runtime** (`ResolveRoutine`, local frames, `FirebirdDebugMetadata`,
`LexicalParent`-by-version, lab fidelity). Part 1 changes **no runtime behaviour** — `ResolveRoutine` still
returns `null` for a local call (⇒ step-over in place, 100% faithful §5.3), so the live debugger is byte-identical.

**AST (Contract #1 — additive overlay, §0 unchanged).** New `SubroutineDeclaration : PsqlStatement` (+
`SubroutineKind` procedure/function) in `Ast/PsqlNodes.cs`: a named unit carrying `Kind`, `Name`, and a real
`Body` (a `BlockStatement`, or `null` for a forward declaration). Its span (and `Tokens`) cover the **whole**
sub-routine — header *and* body — like every other compound node, so the `Body` child nests (the
well-formedness invariant); the header is the token run before the body's `BEGIN`, from which the binder/
extractor read the signature verbatim (R2/R3), never re-modelled as tree fields. `BlockStatement` gained a
new `LocalRoutines` list (between `Declarations` and `Statements`); its `Children` now **merge** declarations
+ local routines **by source position** (Firebird permits `DECLARE VARIABLE` and `DECLARE PROCEDURE` in either
order), so children stay in non-decreasing source order.

**Parser producer (`SqlParser.Psql.cs`).** The pre-`BEGIN` declaration loop was factored into `ParseDeclarationSection`,
which now consumes **both** `DECLARE VARIABLE/CURSOR` (→ `Declarations`) and `DECLARE PROCEDURE/FUNCTION` (→
`LocalRoutines`, via the new `ParseSubroutineDeclaration`). The **load-bearing subtlety**: a sub-routine's
header ends at its first depth-0 `AS` (⇒ a body follows) or depth-0 `;` (⇒ a forward declaration) — **not** at
the first `;`, because the sub-routine's own `declare variable tmp integer;` ends in a `;` too; scanning to
that would truncate the header and lose the body. The body is parsed by the new `ParseScopedBlockBody` —
declarations + one `BEGIN … END`, **block-scoped (non-lenient)**, so it stops at its own matching `END` and
never swallows the enclosing routine's main `BEGIN` (the way the `isTopLevel` path folds trailing tokens
would). `ParsePsqlUnit` now routes a stray `DECLARE PROCEDURE/FUNCTION` at a statement position (mid-edit /
malformed — Firebird declares these only pre-`BEGIN`) through the **same** `ParseSubroutineDeclaration`, so the
now-dead `ParsePsqlHeaderLeaf` and the now-unused `IsDeclarationStart` were **deleted** (replaced by
`IsLocalRoutineStart`) — no dead code.

**Binder (`SemanticBinder.Psql.cs`) — the first genuine nested scope in a PSQL body.** `BindBlock` now binds
`block.LocalRoutines` via a new `BindLocalRoutine`: a **child `RoutineBody` scope** per sub-routine, its
header params + `RETURNS` outputs + local variables declared into it (reusing the same `BindParamList` /
`BindBody` the top-level routine uses), the body bound against that child. Two things fall out of the scope
tree for free: the sub-routine's own symbols resolve inside it, and — because `Resolve` walks to the parent —
an outer variable also resolves (the **FB5 closure**). On FB3 a sub-routine is a **closed scope**, but that is
a *runtime* distinction the debugger honours in Part 2 via `LexicalParent`-by-version; the static editor model
has no server version and stays permissive here (consistent with the diagnostics engine's "prefer silence over
false positives"). The sub-routine **name** is not yet a callable symbol — resolving a local call is Part 2.

**Extractor (`PsqlDeclarationExtractor`).** `RoutineDeclarations.SubRoutines` — modelled in D2 but always
empty — is now **filled**: each `block.LocalRoutines` entry sliced verbatim (header + body) for **R5** (the
harness re-declares each 1:1 so a call in the frame binds to the local, never a like-named global — a §F
violation if dropped). The per-sub-routine base-type derivation + frame layout the callee frames need stay the
Firebird layer's job (Part 2).

**Tests / verification.** +7 `PsqlAstTests` (local procedure/function shape, own-local-variable header-not-
truncated, forward declaration, interleaved decls+routines in source order, byte round-trip), +2
`SemanticModelTests` (nested scope with non-leaking params/locals; a body reference resolving to the
sub-routine's own param), +1 `PsqlDeclarationExtractorTests` (verbatim R5 carry) + the stale "InD2" test
renamed, +4 `SqlTestCorpus` shapes feeding the §0 differential harness (round-trip + well-formedness). Build
0/0; **4848 tests green** (4821 + the 27-test `ConnectionExpandBindingProbe` in its own partition, #94/#226);
smoke clean. One commit. **Next: D9 seam (a) Part 2** — the runtime: `FirebirdDebugExecutor.ResolveRoutine`
resolves a local call to an interpreted frame (`Frame.LexicalParent` by server major — FB3 `null` / FB5
declaring frame), `FirebirdDebugMetadata` derives the callee's param base types (R2), and lab fidelity proves
simulated == real for a stepped local call. Then **seam (b)** — the closure harness + transitive read/write-set
fixpoint over the sub-routine call graph.

## D9 — Local Procedures & Functions (the flagship) — seam (a) Part 2: local-procedure step-into runtime + live fidelity (2026-07-18)

**DoD met: Step Into a local `DECLARE PROCEDURE` works as a real debugger frame, proven simulated == real on
the lab.** Part 1 laid the pure-Core foundation (AST `SubroutineDeclaration` + `BlockStatement.LocalRoutines`,
binder nested scope, extractor R5 carry) but left `ResolveRoutine` returning `null` for a local call (staged,
gotcha #233). Part 2 is the runtime that turns a local `EXECUTE PROCEDURE` into a real frame.

### 1. `ResolveRoutine` — resolve a LOCAL sub-procedure before any server fetch

`FirebirdDebugExecutor.ResolveRoutine` now tries a **local** sub-procedure *first* (before the D8 stored-source
fetch and before the D11 dotted-name step-over):

- **`TryFindLocalProcedure(name, frame)`** walks the **lexical scope chain** (`frame`, then `frame.LexicalParent`,
  …) exactly as name resolution does (spec §6), checking each frame's `Body.LocalRoutines` for a
  `SubroutineDeclaration { Kind: Procedure, Body: not null }` whose name matches (case-insensitive). It returns
  the declaration **and the declaring frame** (the callee's lexical parent). A local **function** is never here
  (it is called inside an expression, not as an `EXECUTE PROCEDURE` step point); a forward declaration (null
  body) is skipped (not runnable).
- **`BuildLocalRoutineAsync`** builds the callee frame from the **already-parsed** `routine.Body` — **no source
  fetch**, because a local routine's body is part of the enclosing routine's AST. It reuses the D8
  argument-seeding harness (`SeedInputParametersAsync` — Contract #4, no second evaluator) to evaluate the
  call's arguments in the caller frame and seed the callee's input params, then returns a `DebugRoutine` sharing
  the **enclosing** `Source` + `Model` (a local routine's spans and its scope live in the enclosing routine, not
  a separate compilation unit) — distinct from a D8 **stored** callee, which gets its own fetched source/model.
- **`LexicalParent` by server major (the §6.3 gate):** `FirebirdDdlReader.ParseServerMajor` ⇒ **FB5** → the
  declaring frame (a true closure), **FB3/FB4** → `null` (a closed scope; FB4 conservative, a documented §F
  boundary — closure semantics unverified there). This reuses the D8 `Frame.LexicalParent`/`Parent` split
  (gotcha #241) — **no new abstraction**, exactly as the plan's D9 brief predicted.

### 2. The one new metadata path — a local routine is not a catalog object

A local `DECLARE PROCEDURE` has **no `RDB$PROCEDURE_PARAMETERS` row**, so its parameter and `RETURNS` types
cannot be read from the catalog. New `FirebirdDebugMetadata.BuildLocalRoutineFrameVariablesAsync` derives them
from the **AST header** instead, via a new pure-Core primitive:

- **`PsqlDeclarationExtractor.ExtractSignature(SubroutineDeclaration, source)`** reads the sub-routine's input
  params + `RETURNS (…)` output params from **its own header tokens** (before the body's `BEGIN`), returning
  `SubroutineParam(Name, TypeSpec)` lists (Contract #1 — consume the AST, never re-parse). A local **function**'s
  single `RETURNS <type>` yields no output parameter (its value returns via `RETURN`). The type-spec scanner
  (`TypeSpecBetween`) was factored out of the existing `TypeSpecOf` so both share one paren-aware "type up to
  `NOT`/`CHECK`/`DEFAULT`/`COLLATE`/`=`/terminator" reader.
- Each param is then declared **verbatim** with its written type (R3 — a domain keeps its semantics) and its
  **base type** derived from `RDB$FIELDS` (R2, reusing `ResolveBaseTypeAsync` — a domain resolves, a builtin is
  itself), exactly as a stored routine's params are, only sourced from the AST rather than the catalog. Body
  locals extract as usual.

### 3. R5 wired into the harness — a local function runs faithfully server-side

`RoutineContext` gained a `SubRoutines` field (R5): the routine's in-scope local sub-routine declarations,
computed once from `PsqlDeclarationExtractor.Extract` in `Register` and threaded into **every** `HarnessRequest`
(step, condition, evaluate, argument-seed). So a statement that calls a local `TRIPLE()`/`ADD_TAX()` has those
declarations in its `EXECUTE BLOCK` and binds to the **local**, never a like-named global (a §F violation). This
is what lets a local **function** be exercised faithfully as a **step-over** (the server runs it). Empty for a
routine with no sub-routines (all D2–D8 routines) — no harness change there, no regression.

### 4. Scope boundary (seam a part 2)

The local routines here are **self-contained** — no outer-variable references. Outer-variable **closure
injection** (the closure harness) and the **transitive read/write-set fixpoint** over the sub-routine call graph
are **seam (b)**, deliberately not started. A self-contained routine does not exercise the closure, so on FB5 the
`LexicalParent = declaring frame` is set correctly (forward-looking) but behaviourally inert; an FB5 local
routine that *did* reference an outer variable would surface a harness error (the outer var isn't in the callee's
own frame templates) rather than a silent wrong value — the honest §F stop until seam (b) wires injection.

### 5. Lab + live fidelity (§F — mandatory)

`Lab/setup.sql` gained **`SP_DBG_LOCAL(BASE)`**: a local `DECLARE FUNCTION TRIPLE` + a local `DECLARE PROCEDURE
ADD_TAX` (input param `AMOUNT`, output `WITH_TAX`, its own local `BONUS`), the main body doing
`ACC = TRIPLE(BASE); EXECUTE PROCEDURE ADD_TAX(:ACC) RETURNING_VALUES :TOTAL; SUSPEND;`. Rebuilt the `.fdb` at an
ASCII path and copied in (gotcha #149). `DebuggerFidelityProbe` was **extended** (not duplicated): the real
`FirebirdDebugExecutor` Step-Into'd `SP_DBG_LOCAL(5)` → **depth 2**, frame chain `SP_DBG_LOCAL → ADD_TAX`,
**simulated `TOTAL = 115` == real `115`** (TRIPLE(5)=15 server-side; ADD_TAX(15): BONUS=100 → WITH_TAX=115 →
TOTAL). D8's stored-chain cases unchanged. **ALL PASS.**

### Result

Build 0/0; **4852 tests green** (+4 `PsqlDeclarationExtractorTests` for `ExtractSignature`); smoke clean; live
fidelity proven. One commit. **Next: D9 seam (b)** — the closure harness (inject captured outer variables into a
local routine frame) + the transitive read/write-set fixpoint over the sub-routine call graph, so a local
routine that reads/writes an *outer* variable (an FB5 closure) steps faithfully.

## D9 — Local Procedures & Functions (the flagship) — seam (b) Part 1: closure capture for stepped-INTO frames (2026-07-18)

**Step Into a local routine whose body READS and WRITES an OUTER variable — an FB5 closure over the declaring
frame (§6.1/§6.2b) — now works, proven simulated == real on the lab.** Seam (a) made a local `DECLARE
PROCEDURE` a real frame but the zoo was *self-contained* (no outer-variable references). Seam (b) closes the
closure: a sub-routine statement that touches an outer variable now injects it, and its mutation reaches the
parent frame.

The key realisation: **the D1 scope-chain mechanism (`Frame.LexicalParent` walk in `TryResolveValue`/
`SetResolvedValue`) already existed and was tested** (`LocalCallee_IsAClosure_ResolvesAndWritesOuterVariable`).
What was missing was (a) the interpreter *using* it when applying a statement's write-back, (b) the harness
*declaring + injecting* the captured outer variables, and (c) correct *ownership* of a name during the walk.
Three focused changes, **no new abstraction** (Contract: if D9 needs a new abstraction, something earlier was
built wrong — it did not).

### 1. Core `Frame` — declared-names for correct ownership (shadowing)

`TryResolveValue`/`SetResolvedValue` previously decided "which frame owns this name" by `Values.Contains(name)`
— true only once a variable is *assigned*. That mis-handles two cases: a not-yet-assigned outer variable
written from inside a sub-routine (would leak to the wrong frame), and an inner local **shadowing** a like-named
outer (an unassigned inner local would resolve to the outer). Fix: `Frame` now records its **declared names**
(its parameters + its body's `DECLARE VARIABLE`s) at construction, and the walk uses
`Owns(name) = _declaredNames.Contains(name) || Values.Contains(name)`. A declared local is owned from frame
entry, so it stops the walk (shadowing) and receives its own writes. Empty declared-names (the fake-driven D1
tests, whose bodies have no `DECLARE`s) fall back to the old Values-only behaviour — so every existing test is
unaffected.

### 2. Core `DebugSession` — route write-backs up the closure chain

The interpreter applied a statement's write-back with `frame.Values.Apply(outcome.Writes)` — writing only the
*callee* frame. For a closure statement writing a captured outer variable, that created a spurious callee local
and left the parent stale (a §F bug). New private `ApplyWrites(frame, writes)` routes each write through
`frame.SetResolvedValue`, which walks the lexical chain to the **owning** frame. Applied at all three write-back
sites (statement outcome, `FOR SELECT` `INTO`, the Immediate window). For a non-closure frame (no lexical
parent) `SetResolvedValue` writes locally — behaviourally identical to the old direct apply, so nothing else
changes.

### 3. Firebird `FirebirdDebugExecutor.BindValues` — declare the captured outer variables

The harness for a sub-routine statement previously declared only the frame's **own** templates. A closure
statement references outer variables that aren't there → the harness fragment references an undeclared variable
→ SQL error. `BindValues` now, beyond the frame's own templates, walks `frame.LexicalParent` and declares
**every ancestor frame's variables** (verbatim R3, current value resolved through the chain), so the harness can
declare + inject the captured reads (R1) and return the captured writes. An inner declaration **shadows** a
like-named outer (first-seen wins, this frame first). For a non-closure frame the chain loop does nothing, so
D2–D8 harnesses are byte-identical. **No fixpoint is needed for step-INTO**: the statement's own references are
precise (`ReadWriteSetAnalyzer.Analyze` surfaces the outer reference via the shared enclosing model), so exactly
the touched outer variables are injected/written.

### Lab + live fidelity (§F)

`Lab/setup.sql` +`SP_DBG_CLOSURE(SEED)`: a local `PROCEDURE BUMP` with **no** parameters that does
`ACC = ACC + 10`, where `ACC` is the enclosing routine's variable — a read+write closure capture. Called twice;
`TOTAL = ACC`. FB5-only by construction (FB3 sub-routines are closed scopes and can't compile an outer reference,
§6.3), which is exactly why the lab is FB5. Rebuilt the `.fdb` (#149). `DebuggerFidelityProbe` **extended**:
the real executor Step-Into'd `BUMP` twice → depth 2, chain `SP_DBG_CLOSURE → BUMP`, **simulated `TOTAL = 25` ==
real `25`** (ACC 5 → 15 → 25 — each closure write reaching the parent frame). D8 + seam-a cases unchanged. ALL
PASS.

### Boundary — seam (b) Part 2 (NOT done)

A **step-OVER** of a local call **with direct arguments** — `EXECUTE PROCEDURE p(x) RETURNING_VALUES y` — whose
callee `p` also mutates OTHER outer variables (not `x`/`y`) still drops those mutations: `Analyze` returns the
precise `{x, y}` set (non-empty, so the `InScopeLocals` fallback does not fire), and the callee's *hidden*
captured writes are neither injected nor returned. Closing that is the **transitive read/write-set fixpoint over
the sub-routine call graph** (spec §3.5), which is D9 seam (b) Part 2. Two things already keep the common cases
correct meanwhile: a **no-argument** local call has no direct refs → the `InScopeLocals` fallback injects/returns
all in-scope locals (correct, chattier), and **R5** carries every sub-routine declaration verbatim (seam a Part
2) so the call binds to the local. And **step-INTO is fully correct** — the flagship capability (a local routine
as a real, steppable frame with real closure variables) works.

### Result

Build 0/0; **4853 tests green** (+1 `DebugEngineTests` pinning the interpreter's closure write-back routing);
smoke clean; live fidelity proven. One commit. **Next: D9 seam (b) Part 2** — the transitive read/write-set
fixpoint over the sub-routine call graph.

## D9 — Local Procedures & Functions (the flagship) — seam (b) Part 2: the transitive read/write-set fixpoint (2026-07-18). 🏁 D9 COMPLETE

**Closes the last gap: a step-OVER of a local call whose callee captures an OUTER variable NOT named at the call
site now injects that capture and reads its mutation back — proven simulated == real on the lab.** Seam (b)
Part 1 handled step-*into* (the callee's own statements directly reference the outer var, so `Analyze` surfaces
it). Part 2 handles step-*over*: the call `EXECUTE PROCEDURE p(x) RETURNING_VALUES y` (or a local **function**
call `z = f(x)` inside a leaf — always a step-over) hides the callee's captures behind the call site, so the
statement's direct read/write set (`{x, y}`) drops them. The fix is the spec §3.5 **fixpoint over the
sub-routine call graph**.

### The mechanism (reusing existing architecture, no parallel path)

New pure-Core **`SubroutineCatalog`** (`Sql/Language/Debugging/SubroutineCatalog.cs`): the authoritative name →
`SubroutineDeclaration` map of the in-scope local sub-routines, built by the executor from
`BlockStatement.LocalRoutines` up the lexical (closure) chain. It is *scope*, not a resolver.

`ReadWriteSetAnalyzer.Analyze` gained an **optional** third argument `SubroutineCatalog? subroutines = null` —
so every existing caller (and D2–D8) is byte-identical (the direct-reference set). When a catalog is supplied
and the statement **calls** an in-scope local sub-routine, `FoldTransitiveCaptures`:

1. **Detects the call** — `CalledSubroutines` scans the statement's tokens for an identifier whose folded name
   is a catalog key. A conservative **name-membership** check (over-detection only adds a callee's captures =
   safe), covering both an `EXECUTE PROCEDURE` proc call and an expression-embedded function call. This is *not*
   a variable resolver — variable references still come only from the binder (Architecture rule #2); the AST
   models the call graph but the binder does not yet resolve local calls as symbols (the seam-a-part-1 note), so
   this token-membership check is the pragmatic call-site detector at the structural-depth boundary.
2. **Collects the callee's transitively-referenced variables** — `CollectTransitiveReferencedVars` unions every
   Variable/Parameter reference in the callee body's span (from `model.References` — **reuses the binder**), which
   is *inherently transitive for a nested sub-routine* (its body lies within the parent's span), and recurses
   into every catalog sibling the callee calls. A **visited set** terminates mutual recursion.
3. **Keeps only the captures visible at the call site** — intersect with `InScopeLocals(model, call.Start)`, so
   the callee's own params/locals (out of scope here) drop out and only the outer captures remain.
4. **Adds them to both reads and writes.** Over-inclusion is §F-safe: a returned-but-unchanged variable writes
   its own value back; an injected-but-unused value is harmless (R1 skips a null anyway).

The executor threads the catalog through `ResolveReadWrite` (built once per statement via
`BuildSubroutineCatalog(frame)`), so both the statement harness (`ExecuteStatement`) and the condition harness
(`EvaluateCondition`) get the fixpoint. D5 `Evaluate` already uses the `InScopeLocals` superset, so it needs no
change. **BindValues already declares the frame's + ancestors' variables (seam b Part 1)** — so the fixpoint
only widens what is *injected/returned*, never what is *declared*; the captured variable is already in the
harness, it just now gets its value.

### Why this is precise, not "inject everything"

The old empty-set fallback (`InScopeLocals` as both reads+writes) already made a **no-argument** local call
correct, but a call **with** direct arguments has a non-empty precise set and skipped the fallback — dropping the
hidden captures. The fixpoint targets exactly the callee's captures (e.g. a 10 MB BLOB variable the call does not
touch is *not* shipped both ways every step — the §3.5 perf motivation), rather than widening to all in-scope
locals.

### Lab + live fidelity (§F)

`Lab/setup.sql` +**`SP_DBG_CLOSURE_FN`** (a local `FUNCTION BUMP_HIDDEN` that reads+writes the outer `HIDDEN`;
the call `TOTAL = BUMP_HIDDEN(10)` names only the literal 10) and +**`SP_DBG_CLOSURE_OVER`** (a local
`PROCEDURE ACCUMULATE` that reads+writes `HIDDEN`; stepped OVER). Rebuilt the `.fdb` (#149).
`DebuggerFidelityProbe` extended — `SimulateAsync` gained a `StepKind` parameter so a routine can be driven with
Step Over:
- **Case 6** (`SP_DBG_CLOSURE_FN`, function via natural step-over): depth 1, **sim `TOTAL = 15` == real `15`**.
- **Case 7** (`SP_DBG_CLOSURE_OVER`, procedure, explicit `StepKind.Over`): depth 1, **sim `TOTAL = 15` == real
  `15`**.

Without the fixpoint both would simulate `NULL`/stale (HIDDEN injected as NULL, its mutation dropped). ALL PASS;
D8 + seam-a + seam-b-Part-1 cases unchanged.

### Result — 🏁 the flagship is complete

Build 0/0; **4856 tests green** (+3 `ReadWriteSetAnalyzerTests`: a called function's captured outer var folded in
while its own param is filtered out; transitivity across the call graph; null/empty catalog = the direct set);
smoke clean; live fidelity proven. One commit. **D9 CORE is COMPLETE — local procedures and functions are real,
steppable debugger frames with real closure variables, the capability IBExpert cannot deliver.** A local
**procedure** is faithful both step-into and step-over; a local **function** is faithful step-over. The one
remaining asymmetry — step *into* a local function's body — is closed by **seam (c)** below (designed here,
implemented next).

## D9 seam (c) — Step Into a local FUNCTION — DESIGN (2026-07-18; NOT implemented — full handoff for a new session)

During manual QA of D9 the user found an asymmetry: **Step Into works for a local procedure but a local
function runs whole and returns (effectively Step Over)** — you cannot trace a complex local function's body
line by line. After an architectural analysis (recorded below) we ratified a small closing seam. **Nothing is
implemented; this section is the complete design so the next session codes without re-analysing.**

### Why functions differ (root cause)

A **procedure** call is a *statement* (`EXECUTE PROCEDURE p(...)`) — a discrete step point the interpreter owns.
A **function** call is an *element of a server-evaluated expression* (`v = f(x)`, `if (f(x))`, …). The whole
expression is computed by the server in one harness (§F / Contract #3: the client owns control flow, the server
owns **all** semantics). Stepping into a function *in the general case* would force the client to decompose and
drive expression evaluation — becoming an expression evaluator, exactly what the architecture forbids. **The
general case is a permanent §F boundary, NOT a gap to fix.**

### The Step Into principle (ratified, final)

> **Step Into descends into a local function only when the function call is the ENTIRE operand of a
> value-consuming position, so the client never has to evaluate an expression around it.**

Covered (Variant A — all four positions):

| Position | Consumer of the return value `r` | Client evaluates an expression? |
|---|---|---|
| `v = LocalFunction(args)` (assignment; Firebird PSQL uses `=`, not `:=`) | the assignment target `v` | no |
| `RETURN LocalFunction(args)` | the enclosing function frame's return value | no |
| `IF LocalFunction(args) THEN` | the IF branch decision (`r == true`) | no |
| `WHILE LocalFunction(args) DO` | the WHILE loop decision (re-evaluated per iteration) | no |

The function's **arguments** may be arbitrary (server-evaluated during seeding, incl. a nested `g(...)` which is
step-over). **Excluded** (require expression decomposition → step-over, no exceptions): `f(x)+1`, `f(x)=5`,
`f(g(x))` [f is stepped-in, g is a step-over argument — this IS covered; the *excluded* form is f being a
sub-operand], `a AND f(x)`, `INSERT … VALUES(f(x))`, and any proper-sub-expression position.

### Ratified architectural decisions (final)

1. Small closing seam before D10. **No §F violation. No expression evaluator. No new SERVER path.**
2. Reuse only: **Statement Harness**, **Expression Harness**, **`SetResolvedValue` / `ApplyReturningValues`**,
   `Frame`, `LexicalParent`, closures, argument seeding.
3. **No mini-harness for delivery.** Delivery of `r` is **client-side** via `SetResolvedValue` — the same
   primitive procedures use for `RETURNING_VALUES`. A delivery harness was rejected for TWO reasons: (a) it
   would be a second delivery mechanism (procedures deliver client-side ⇒ duplication), and (b) declaring the
   target with its domain (R3) would **re-validate the domain mid-flight**, contradicting R2 (which deliberately
   base-types harness slots to avoid exactly that). Coercion faithfulness: the function's **own** return
   coercion (operand → its `RETURNS` type) IS faithful — done by the server inside the Expression Harness whose
   result column is typed as the function's `RETURNS` base type (R2). The **outer** assignment coercion
   (function type → `v`'s type) is raw client-side, **identical to procedure `RETURNING_VALUES`**, and
   self-heals at `v`'s next injection (declared with its type ⇒ the server coerces). The only residual is a
   cosmetic display difference if `v` is emitted by `SUSPEND` before any use — the same, accepted boundary as
   procedures.
4. `RETURN <expr>` inside a stepped function is evaluated by the **existing Expression Harness** (result column
   typed as the function's `RETURNS` base type). Never route a `RETURN` leaf through the Statement Harness — it
   is invalid inside `EXECUTE BLOCK` (would error).
5. **Function Return Continuation** — a generalized, client-side mechanism: "the caller statement is paused
   pending the callee function's return value, then consumes it per the call position." `ApplyReturningValues`
   becomes the procedure special-case of this idea (both fire at the callee frame's normal return in
   `AdvanceToNextStepPoint`).
6. Small AST deepening (Contract #1 — structure in the tree, never a token-scan in the Firebird layer).

### Impact — AST

New node **`CallExpression`** (`Ast/ExpressionNodes.cs`): a lone call — `Name : string` (folded, like
`ExecuteProcedureStatement.ProcedureName`) + `Arguments : IReadOnlyList<CallArgument>` (reuse D8's
`CallArgument` span record). Its span is the whole `name(args)`.

Additive properties on existing nodes, set by the parser **only** when the operand is *exactly* a lone call
(strict recognition — under-recognise → step-over is always safe):
- `PsqlLeafStatement.RhsCall : CallExpression?` — the lone-call RHS of an `Assignment` leaf **or** the lone-call
  operand of a `Return` leaf.
- `PsqlLeafStatement.AssignmentTarget : string?` — the folded bare-identifier target of an `Assignment` whose
  RHS is a lone call (null for a `Return` leaf and when the target is dotted/`NEW.col` → not recognised, a D10
  concern). Precedent for a folded name in the AST: `ProcedureName` / `ReturningTargets`.
- `IfStatement.ConditionCall : CallExpression?` and `WhileStatement.ConditionCall : CallExpression?` — non-null
  when the **entire** condition (inside the header parens) is exactly a lone call.

Parser producers recognise these shapes strictly (a trailing operator / second call / dotted target ⇒ leave the
property null ⇒ step-over). The parser does **not** decide local-function-vs-not (no catalog) — it models "a
lone call `name(args)`"; the **debugger** resolves whether `name` is an in-scope local function. Additive
overlay; §0 round-trip unchanged; `SqlFormatter` untouched (token-based).

Extend `PsqlDeclarationExtractor.ExtractSignature` → `SubroutineSignature.ReturnType : string?` — a local
**function**'s single `RETURNS <typespec>` (the tokens between `RETURNS` and `AS`, no parens); null for a
procedure. Feeds R2 base-type derivation for the Expression Harness result column.

### Impact — interpreter (`DebugSession.ExecuteCurrent` / `AdvanceToNextStepPoint`)

- **Step-into recognition (4 positions).** When `kind == StepKind.Into` and the relevant `CallExpression` is
  present and `_executor.ResolveFunction(call, frame)` returns a routine: push a **function frame** (its
  `LexicalParent` per server major — FB5 declaring frame / FB3+FB4 null; seeded input params; carrying its
  `ReturnType`) with a **Function Return Continuation** derived from the position. Do **not** advance the caller
  sequence / decide the branch yet — the continuation does that on the callee's return. If `ResolveFunction`
  returns null (not a local function, e.g. a stored/builtin) → run the statement normally (Statement/Expression
  Harness — the function runs server-side, step-over). For `StepKind.Over/Out/Continue` the `CallExpression` is
  ignored (always run normally).
- **`RETURN` inside a function frame.** A `Return` leaf in a function frame is NOT run through the Statement
  Harness. Instead: if its `RhsCall` is a step-into-able local function (and `StepKind.Into`) → step into it
  with a `SetFrameReturn` continuation; otherwise → `_executor.EvaluateReturn(returnLeaf, frame)` (Expression
  Harness typed as `frame.ReturnType`) → set `frame.ReturnValue` and terminate the frame (clear its control
  stack so it completes).
- **Delivery on normal return.** `AdvanceToNextStepPoint`, when a frame completes, generalises the existing
  `ApplyReturningValues` step: a **procedure** callee delivers output params → `RETURNING_VALUES` targets (as
  today); a **function** callee runs its `ReturnContinuation` against the caller frame + `frame.ReturnValue`:
  - `AssignTo(target)` → `caller.SetResolvedValue(target, r)`; consume the assignment leaf.
  - `SetFrameReturn` → `caller.ReturnValue = r`; terminate the caller (its own `RETURN` completes → its
    continuation fires — recursion handled naturally).
  - `BranchIf(ifNode)` → advance past the IF; `caller.PushBranch(r == true ? then : else)`.
  - `DecideWhile(whileNode)` → `r == true` ? `caller.PushBranch(body)` : pop the `WhileActivation`.
  A raised function (exception) unwinds via `ExceptionRouter` (savepoint rollback) and the continuation does
  **not** fire — identical to procedures.
- A function frame that completes with no `RETURN` on its path (Firebird runtime error "function returned no
  value") → surface a `DebugError`/step error; do not silently deliver null. (§F boundary — documented.)

### Impact — DebugSession (state/API)

Same class as the interpreter. New: `PushFrame` gains an optional continuation + return type. No public API
change for the tab VM (Step Into is the same command). `CallStack`, breadcrumbs, frame navigation all work for a
function frame unchanged (it is a `Frame`, marked simulated △ like any stepped-in local routine).

### Impact — HarnessBuilder

**NONE.** The `RETURN` operand and the seeding reuse Statement/Expression modes exactly as they are.

### Impact — runtime (`FirebirdDebugExecutor` / `FirebirdDebugMetadata`)

- New `IDebugExecutor.ResolveFunction(CallExpression call, Frame frame) : DebugRoutine?` — mirrors
  `ResolveRoutine`: `BuildSubroutineCatalog(frame)` → an in-scope local **function** named `call.Name` with a
  body → build the frame (reuse the `BuildLocalRoutineAsync` path; **no server source fetch** — the body is in
  the enclosing AST), seed input params from `call.Arguments` (generalise `SeedInputParametersAsync` to take
  `IReadOnlyList<CallArgument>` + the enclosing tokens/source), `LexicalParent` per server major, carry
  `ReturnType` (R2, from `SubroutineSignature.ReturnType` via `FirebirdDebugMetadata`). Null → step-over.
- New `IDebugExecutor.EvaluateReturn(IExecutableStatement returnStatement, Frame frame) : (value/error)` —
  refactor `EvaluateCondition` into a private typed-expression evaluator (`EvaluateExpression(fragment, frame,
  resultType, reads)`), with two public entry points: `EvaluateCondition` (BOOLEAN → `ConditionOutcome`) and
  `EvaluateReturn` (`frame.ReturnType` → value). Same Expression Harness; reuses `ResolveReadWrite` for the
  operand's reads (incl. the seam-b fixpoint if the operand itself calls a local routine).
- `FirebirdDebugMetadata`: derive the function's `RETURNS` base type (R2) from `SubroutineSignature.ReturnType`
  (reuse `ResolveBaseTypeAsync`).
- **No `DebugSessionConnection` change. No new SERVER round-trip type.**

### Impact — UI

Minimal / none required for the mechanism (Step Into descends automatically in the recognised positions; the
function frame shows in the Call Stack / breadcrumbs / Variables like any local-routine frame). **Optional
polish (defer, D4-backlog style — view/status only, no VM logic):** (a) a subtle "will step into f()" cue in the
richer paused-status when the current step point is a step-into-able function call, to make the boundary
predictable; (b) show a function frame's `ReturnValue` as a synthetic `⟵ RETURN` row in Variables once set.

### New classes / structures — responsibilities

| Type | Where | Responsibility |
|---|---|---|
| `CallExpression` | Core `Ast/ExpressionNodes.cs` | A lone call operand (name + arg spans) recognised in a value-consuming position. |
| `PsqlLeafStatement.RhsCall` / `.AssignmentTarget` | Core AST (additive props) | The lone-call RHS/RETURN-operand + the assignment target. |
| `IfStatement.ConditionCall` / `WhileStatement.ConditionCall` | Core AST (additive props) | The lone-call whole-condition. |
| `SubroutineSignature.ReturnType` | Core `PsqlDeclarationExtractor` | A local function's single `RETURNS` type spec (R2 input). |
| `FunctionReturnContinuation` (variants `AssignTo` / `SetFrameReturn` / `BranchIf` / `DecideWhile`) | Core `Sql/Language/Debugging` | Encapsulates "resume the caller statement with the callee function's return value." |
| `Frame.ReturnValue` / `.ReturnType` / `.ReturnContinuation` | Core `Frame` | A function frame's computed return, its `RETURNS` base type (for the Expression Harness), and how its result is delivered. |
| `IDebugExecutor.ResolveFunction` | Core seam + Firebird impl | Resolve a lone local-function call → a function frame (or null → step-over). |
| `IDebugExecutor.EvaluateReturn` | Core seam + Firebird impl | Evaluate a `RETURN` operand via the Expression Harness typed as the function's `RETURNS` type. |

### Implementation plan — small, safe, committable sub-steps

- **c1 — AST only (pure Core, no runtime change). ✅ DONE (2026-07-19).** `CallExpression`
  (`Ast/ExpressionNodes.cs`: folded `Name` + reused D8 `CallArgument` spans; **not a tree child** — an
  additive overlay referenced by typed props, so `Descendants`/round-trip/formatter are untouched). The
  additive props + strict parser producers (`SqlParser.Psql.cs`): `PsqlLeafStatement.RhsCall` +
  `.AssignmentTarget` (an assignment whose **whole** RHS is a lone call, with a **single bare** target only —
  `eq == lo+1`; `NEW.col` is left null, a D10 concern) and `RhsCall` for a `RETURN` operand;
  `IfStatement.ConditionCall` / `WhileStatement.ConditionCall` (the whole header condition — a single
  enclosing paren pair stripped first, then the remainder must be exactly a lone call). Shared helpers
  `TryReadLoneCall` (name → `(` → matching `)` is the **last** token, nothing trailing) / `TryReadConditionCall`
  / `ReadLeafCall` / `FindTopLevelAssign`, **reusing** the D8 `ReadCallArgumentList` + `MatchParenTok` (rule
  #1 — no second scanner). `PsqlDeclarationExtractor.ExtractSignature` gained
  `SubroutineSignature.ReturnType` (a local function's single `RETURNS` type spec, captured up to the header's
  depth-0 `AS` via a new `FindHeaderAs` so the `AS` is not absorbed — the R2 base-type input for c3's
  Expression Harness result column; null for a procedure). **Producer-only, deliberately staged:**
  `CallExpression` has no consumer yet (`IDebugExecutor.ResolveFunction` / `EvaluateReturn` are c2/c3), so the
  tests assert the **AST is produced correctly** at this layer; the "is it actually called" surface assertion
  arrives with the consumer in c2/c3 (gotcha #233 — a tested-but-uncalled component; recorded as the c1→c2
  boundary). Additive overlay — §0 round-trip byte-identical, binder + `SqlFormatter` untouched. Tests: +19
  `PsqlAstTests` (each of the 4 positions recognised; no-arg `f()`; nested-arg `f(g(x))` = one step-over
  argument; quoted target+callee folding; **negatives** for a trailing operator, a plain expression, two
  calls, a dotted target, a `RETURN` with a trailing op, a compound/comparison IF/WHILE condition, a
  non-assignment leaf; a lone-call round-trip), +3 `PsqlDeclarationExtractorTests` (function `ReturnType`:
  scalar / parametrised / domain / null-for-procedure), +2 `SqlTestCorpus` shapes (round-trip +
  well-formedness). Build 0/0; targeted tests green (402); full suite hangs in this env (#94/#226) —
  user-verified green; smoke clean. *(Mirrors seam-a Part 1.)*
- **c2 — Core interpreter (fake-executor-driven). ✅ DONE (2026-07-19).** New internal
  `FunctionReturnContinuation` (`Sql/Language/Debugging/FunctionReturnContinuation.cs`) — four variants
  (`AssignTo(target)` / `SetFrameReturn` / `BranchIf(ifNode)` / `DecideWhile(whileNode)`) plus a
  `RecognizeStepInto(step)` factory returning `FunctionStepInto?` (the call + its continuation). **Per the
  user's architectural request, `RecognizeStepInto` is the single concentration point:** the interpreter's
  "is this a step-into-able local-function position, and which continuation consumes the return" decision lives
  in ONE place, not scattered across the IF/WHILE/leaf branches of the step loop. `Frame` gained
  `ReturnType`/`ReturnValue`/`ReturnContinuation` + `IsFunctionFrame` (⟺ has a continuation) + `SetReturnValue`
  / `TerminateForReturn` (close cursors + clear the control stack — a RETURN exits regardless of block
  nesting). `IDebugExecutor` gained `ResolveFunction(CallExpression, Frame)` + `EvaluateReturn(returnStmt,
  Frame)` (+ the `ReturnOutcome` record, + `DebugRoutine.ReturnType`). `DebugSession.ExecuteCurrent` got two
  guarded branches **before** the node switch: (1) `kind==Into && RecognizeStepInto(step) is {} into &&
  ResolveFunction(into.Call, frame) is {} fn` → push a function frame carrying `into.Continuation` (the
  caller's control flow is **not** advanced/branched now — the continuation owns that on return, so it fires
  exactly once); (2) a `RETURN <expr>` in a function frame → `EvaluateReturn` (Expression Harness) → record
  the value + `TerminateForReturn`. `AdvanceToNextStepPoint` got `ApplyReturnContinuation(completed)` — the ONE
  delivery switch generalising `ApplyReturningValues`: `AssignTo` writes the value + consumes the assignment
  leaf; `SetFrameReturn` sets the caller's own return value + terminates it (its continuation fires next —
  recursion for `RETURN f()`); `BranchIf`/`DecideWhile` resume the caller's branch/loop with the returned
  boolean. A raised function unwinds via the `ExceptionRouter` and its continuation never fires (identical to a
  procedure). **`FirebirdDebugExecutor` got c2 stubs** — `ResolveFunction` → null (⇒ every local-function call
  still steps over), `EvaluateReturn` → `NotSupportedException` (unreachable while no function frame exists) —
  so **live behaviour is byte-identical to D9 core** until c3. All types internal (no new public API; the tab
  VM's Step Into command is unchanged). +11 `DebugEngineTests` (each of the 4 positions + deliver + savepoints,
  nested `RETURN f()` propagation, IF then/else, WHILE iteration count, unresolved ⇒ step-over, Step Over
  ignores the call, unresolved IF condition ⇒ server `EvaluateCondition`, a raising function ⇒ no continuation
  + frame rollback, plain `RETURN <expr>` ⇒ `EvaluateReturn` not the Statement Harness). Build 0/0; targeted
  green (508 across the debugger + parser classes); full suite hangs in this env (#94/#226) — user-verified;
  smoke clean. *(Mirrors D1 / seam-a Core work.)*
- **c3 — Firebird executor + live fidelity. ✅ DONE (2026-07-19).** `FirebirdDebugExecutor.ResolveFunction`
  resolves a lone local-function call: a generalised `TryFindLocalRoutine(name, frame, SubroutineKind)` (the D9
  seam-a `TryFindLocalProcedure` widened, now shared by `ResolveRoutine` for procedures and `ResolveFunction`
  for functions) walks the lexical chain **nearest-first** (so a local shadows a same-named global); a match →
  `BuildLocalFunctionAsync` (mirrors `BuildLocalRoutineAsync`) builds the frame from the **already-parsed AST
  body** (no server source fetch), seeds input params through the **shared** `SeedInputParametersAsync`
  (generalised from `(ExecuteProcedureStatement exec, …)` to `(IReadOnlyList<CallArgument> arguments,
  IReadOnlyList<SqlToken> callTokens, int callStart, …)`; the procedure callers pass `exec.Arguments/.Tokens/
  .Start`, the function passes `call.Arguments`, the caller body's tokens, `call.Start`), sets `LexicalParent`
  by the §6.3 server-major gate, and carries the `RETURNS` base type. `EvaluateReturn` computes the `RETURN`
  operand (`ReturnOperandExpression` — the tokens after `RETURN`, before `;`) via the Expression Harness typed
  as `frame.ReturnType`, through a new private `EvaluateExpression` **shared with `EvaluateCondition`** (one
  server path — no second evaluator). `FirebirdDebugMetadata`: `DebugFrameLayout` gained `ReturnType`, derived
  in `BuildLocalRoutineFrameVariablesAsync` from `SubroutineSignature.ReturnType` via the existing
  `ResolveBaseTypeAsync` (R2; null for a procedure). **Lab zoo +4** (`Lab/setup.sql`, `.fdb` rebuilt at an ASCII
  temp path #149): `SP_DBG_FN_POS` (four positions), `SP_DBG_FN_TYPES` (six return types), `SP_DBG_FN_SHADOW`
  (a local shadows the stored `FN_ADD_TAX`), `SP_DBG_FN_CLOSURE` (a function closing over an outer var). **Live
  fidelity (spec §15.11):** `DebuggerFidelityProbe` cases 8–11 (+ re-pointed 4 & 6) — all four positions (depth
  3), all six return types (INTEGER/BIGINT/NUMERIC/VARCHAR/BOOLEAN/NULL), shadowing (depth 2 → local chosen),
  nesting, a closure — **all sim == real**. **Live-verified boundary:** Firebird rejects a **nested sub-routine**
  (`SQLSTATE 0A000 "nested sub function"` — gotcha #244), so the first shadow design (a sub-function inside a
  sub-procedure) was invalid; lexical-level shadowing is not expressible in Firebird — the realistic case is
  local-vs-global (`SP_DBG_FN_SHADOW`). Build 0/0; 508 Core tests green (no regression — c3 is Firebird/metadata
  only); smoke clean. *(Mirrors seam-a Part 2 / seam-b live fidelity.)*
- **c4 — optional UI polish** (the two view/status items above). Deferred; not required to close the seam.

### Danger zones for the implementer

- A `RETURN` leaf inside a function frame must go through `EvaluateReturn` (Expression Harness), **never** the
  Statement Harness — `RETURN` is invalid inside `EXECUTE BLOCK`.
- Do **not** advance the caller sequence / decide the branch when pushing the function frame — the continuation
  owns that on return (otherwise the branch/assignment fires twice or in the wrong order).
- The continuation fires **only** on normal return; an exception unwinds via `ExceptionRouter` and must not run
  it.
- **No delivery harness** — deliver client-side (`SetResolvedValue`); a harness would duplicate the procedure
  path and violate R2 (mid-flight domain re-validation).
- Parser recognition must be **strict**: under-recognise (step-over) rather than over-recognise. A recognised
  shape the interpreter cannot deliver cleanly is a bug.
- Firebird PSQL assignment is `=`, not `:=`.

**🏁 c1–c3 LANDED (2026-07-19): D9 is fully closed — local procedures *and* functions step faithfully, both
into and over (spec §15.8–§15.11). Next: D10 (Triggers).**

---

## D10 — Triggers

Debug a trigger body with user-supplied `NEW`/`OLD` (spec §8.1). No engine API attaches to a real firing
trigger, so — like IBExpert, and honest about it — **debugging a trigger does not perform the triggering DML**:
the user supplies `NEW`/`OLD`, we interpret the body. `NEW`/`OLD` do not exist inside an `EXECUTE BLOCK`, so the
interpreter models them as **frame variables** and the harness **substitutes** them.

### Architecture review (ratified before any code)

The plan (2 sessions) was reviewed against the post-D8/D9 codebase and found **cheaper than budgeted**: the
mechanism is *one pure-Core engine + one new metadata path + a launch/UI extension*, all resting on shipped,
live-verified seams. Ratified decisions:

- **Split into 3 committable seams** (A pure Core → B Firebird + Live Fidelity → C UI), mirroring the D8/D9
  rhythm, instead of the plan's monolithic "2 sessions". Each seam ends build 0/0 + green + smoke + docs +
  committable.
- **User decisions:** (2) `NEW`/`OLD` inside a `FOR SELECT` cursor (or a stepped-into callee) is a **§F
  boundary** — D10 shows a clear refusal rather than partial fidelity; (3) extend the lab with a **BEFORE
  DELETE** and a **BEFORE INSERT OR UPDATE** trigger to close the full trigger matrix; (4) **"seed from a real
  row"** deferred to Seam **C2** so C1 stays small.
- **No heavyweight `TriggerContextModel` (user's architectural request).** The "trigger context" decomposes into
  already-existing state: the context columns are ordinary `HarnessVariable`s (they go into
  `RoutineContext.VariableTemplates`), their values live on the `Frame`, and the synthetic names are a naming
  *convention*, not stored state. What genuinely remains is a *small* value — the simulated event + timing —
  from which the §8.1 availability rules derive. So it lands as an **optional field on the existing
  `RoutineContext`** (Seam B), carried by a small pure-Core `TriggerContext` record, **not** a parallel model.
- **`ContextSubstitution` is entirely `SemanticModel`/`SymbolReference`-driven, never a text search (user's
  second architectural request).** Confirmed feasible by reading the binder: `AddReference` records a reference
  **even when the symbol is null**, so in the debugger's **metadata-less** model (`SemanticModel.Build(
  SqlParser.Parse(source).Root)` with no provider) `NEW.STATUS` still yields two references — `NEW` (role
  `RecordAlias`, resolved) and `STATUS` (role `Column`, span + `Text="STATUS"` present though the column symbol
  does not resolve). The engine anchors every rewrite on those reference spans; the column name comes from the
  reference's own text. A `'NEW.'` inside a string literal/comment/quoted identifier has no such reference and
  is therefore untouched — the substring-corruption risk the danger-zone warns about is structurally excluded.

### Seam A — pure Core (2026-07-19)

Pure Core, no server, no UI, **unwired** (staged per gotcha #233 — the engine ships tested-but-uncalled; Seam B
wires it):

- `EmberTern.Core.Sql.Debugging.ContextSubstitution` — the **one** substitution engine (designed to also serve
  the §3.6 handler error context `GDSCODE`/`SQLSTATE`/`RDB$ERROR` — one mechanism, two consumers):
  - `BuildColumns(model, scope)` scans the body's references for each distinct `NEW.col`/`OLD.col`
    (a `RecordAlias` reference immediately followed by a `Column` reference — how the binder records a dotted
    ref) and assigns each a **stable, compact synthetic name** `ET_CTX_i`. Index-based on purpose: it stays a
    valid ≤31-char identifier regardless of column-name length (FB3's identifier limit — `ET_CTX_NEW_<col>`
    could overflow). This class is the **single owner** of the naming convention; Seam B's metadata (base type)
    and the write-back both consume the names it hands out. Assigned once over the whole body so the same
    `NEW.col` is the same frame variable in every statement, in the frame, and in the Variables window.
  - `Substitute(model, source, region, context)` rewrites each `NEW.col`/`OLD.col` reference span to its
    synthetic and each `INSERTING`/`UPDATING`/`DELETING` predicate to `TRUE`/`FALSE` for the simulated
    `TriggerContext.Event`, returning the rewritten fragment + the context **reads** (inject) and **writes**
    (return for write-back). Reads = every context occurrence (over-inclusive is safe — R1 skips a null value).
    Writes = `NEW` columns only when `TriggerContext.NewWritable` (a BEFORE trigger); over-inclusive there (a
    merely-read `NEW.col` written back returns its own value, harmlessly) so it can never *miss* a real write —
    the alternative (missing a write) would leave the frame/Variables stale, a §F divergence. `OLD` is never
    written back. Edits are applied by a non-overlapping span splice (reference spans don't overlap), mirroring
    the executor's existing `RewriteColonRefsToBare`/`CursorBridge` span rewrites — a generalisation of a proven
    pattern, not a new class of mechanism.
- `TriggerContext` record (`TargetTable`/`Event`/`Timing`/`Columns`) + `TriggerEvent`/`TriggerTiming`/
  `TriggerRecord` enums + `ContextColumn` record. The §8.1 availability table is expressed as computed
  properties: `OldAvailable` (UPDATE/DELETE), `NewAvailable` (INSERT/UPDATE), `NewWritable` (BEFORE ∧
  NewAvailable). This is the value Seam B mounts on `RoutineContext`.

**Tests — built the debugger's way (strict whole-routine parse, NO metadata):** 13 `ContextSubstitutionTests`
covering distinct + deduplicated columns, synthetic rewrite with the read/write split, AFTER trigger ⇒ no NEW
write, predicate literals flipping with the simulated event, a **string literal `'…OLD.STATUS'` left
byte-for-byte intact beside a real rewritten `OLD.STATUS`** (the reference-driven proof), a no-context statement
returned verbatim, and the full §8.1 availability matrix (6 cases). The metadata-less build is the load-bearing
case — it proves the `Column` reference is present and usable even when it does not resolve to a symbol.

Build 0/0; Seam A (13) + 191 neighbouring Core/semantic tests green (full suite hangs in this env, #94/#226 —
ran filtered); smoke clean.

### Seam B — Firebird executor + metadata + Live Fidelity (2026-07-19)

The pure-Core substitution from Seam A is wired to the live executor and proven sim==real on the lab.

**Wiring (behaviour-preserving for non-triggers):**
- `FirebirdDebugExecutor.RoutineContext` gained an optional `TriggerContext? Trigger`, **non-null only for a
  trigger's root frame**. A stepped-into stored/local callee has no NEW/OLD in scope, so its context stays null
  and the D8/D9 step-into paths are byte-for-byte untouched.
- A new `CreateAsync` overload takes the `TriggerContext`: it skips the (pointless) `RDB$PROCEDURE_PARAMETERS`
  query (a trigger is not a procedure), builds the **NEW/OLD context column templates** and merges them into the
  frame variable templates, then registers the trigger context on the root.
- `ExecuteStatement` and `EvaluateCondition` route every trigger-frame fragment / condition through
  `ContextSubstitution.Substitute` (over the node span / the condition's paren-group span), replacing the
  verbatim `Slice`, and **union** the context reads/writes into the local read/write set. `ConditionExpression`
  was split into a reusable `ConditionBounds` so the condition region can be substituted, not only sliced.
- `OpenCursor` **refuses** a `FOR SELECT` whose query references NEW/OLD with a clear message — the §F boundary
  (decision 2): a cursor is a separately-opened DSQL statement where the harness's synthetic context variables
  do not exist, so a partially-faithful cursor is never opened.
- `DebugLaunchSpec` + `FirebirdDebugSessionLauncher` carry the `TriggerContext` through to `CreateAsync`.

**One new metadata path** — `FirebirdDebugMetadata.BuildTriggerContextVariablesAsync`: types each context
column from the **trigger's target table** (`RDB$RELATION_FIELDS ⨝ RDB$FIELDS` via the existing
`FirebirdDdlReader.FormatType` — derivation, not guessing), producing one `HarnessVariable` per `ContextColumn`.

**Two §F corrections found by probing, not reasoning** (Contract: verify-don't-infer):
- **Base type, never the domain (gotcha #246).** First cut declared a context variable with its column domain
  (mirroring `ReadProcedureParametersAsync`'s R3). The probe seeded `NEW.TOTAL_AMOUNT = -5` for `TR_ORDERS_BU`
  (which raises `E_NEGATIVE_AMOUNT` on a negative amount); the harness died on entry with *"validation error for
  variable ET_CTX_0, value -5.00"* — the `D_AMOUNT CHECK (VALUE >= 0)` domain re-validated the injected value
  before the trigger's own logic ran. A NEW/OLD field is a **record field**, not a domain-constrained local: in a
  real trigger the value can violate the column CHECK (that is exactly what a BEFORE trigger is there to catch;
  the constraint is enforced at write time, after the trigger, which the debugger never performs). Fixed by
  declaring context variables with the **base type** (R2) — `BuildTriggerContextVariablesAsync` now reads only
  the base type.
- **Colon prefix inside DSQL (gotcha #247).** `TR_ORDERS_AU`'s `INSERT INTO AUDIT_LOG … VALUES (…, NEW.ORDER_ID,
  …)` failed with *"Column unknown ET_CTX_2"*: inside an embedded DSQL statement Firebird reads a **bare**
  identifier as a **column**, so a PSQL variable there must be `:ET_CTX_2`. This flips the direction of the other
  colon rewrites (#239 `:v`→`?`, #242 `:v`→bare). `ContextSubstitution.Substitute` gained a `colonReferences`
  flag; the executor sets it with `node is not PsqlStatement` (DSQL statements are `SqlStatement`, PSQL leaves are
  `PsqlStatement`). Reads/writes stay the bare synthetic (the harness declares + injects it bare); only the
  fragment reference is qualified.

**Lab extended** (`Lab/setup.sql` + rebuilt `.fdb`, #149): an isolated `TRIG_LAB` table + `TR_TRIG_BD` (BEFORE
DELETE, OLD-only) and `TR_TRIG_BIU` (BEFORE INSERT OR UPDATE, multi-action, writes NOTE not STATUS). Isolating
them on their own table means they never clobber each other or the ORDERS triggers, giving clean independent
fidelity checks and closing the full trigger matrix.

**Live fidelity PROVEN** (`DebuggerFidelityProbe` +5 cases; the spec's method — compare the body's *effects*,
since the triggering DML is not performed): (12) `TR_ORDERS_BU` raises `E_NEGATIVE_AMOUNT` on `NEW.TOTAL_AMOUNT
= -5` — sim faults == a real UPDATE faults with the same exception; a non-negative amount completes. (13)
`TR_ORDERS_AU` writes an `AUDIT_LOG` row on a STATUS change — the sim's DETAILS (read from the debug tx before
rollback) == the real UPDATE's DETAILS ("Status changed from ACT to DONE"). (14) `TR_TRIG_BD` (OLD-only) raises
`E_ORDER_LOCKED` on `OLD.STATUS='LOCKED'` — sim == a real DELETE of a locked row; a non-locked row completes.
(15/16) `TR_TRIG_BIU` produces `NEW.NOTE='INSERTED'` for the INSERTING event and `='UPDATED'` for UPDATING —
sim == the persisted value from a real INSERT / UPDATE, proving predicate substitution + the writable-NEW
write-back for a multi-action trigger. All 11 D8/D9 cases stayed green (the executor changes are regression-free).

Build 0/0; 122 debugger Core/Firebird unit tests green (full suite hangs #94/#226 — ran filtered); smoke clean.
**Next: Seam C — the UI (`TriggerContextEditor` with the action selector + NEW/OLD grids honouring the §8.1
availability rules, trigger-mode `DebuggerTabViewModel`, the Variables Context group, and the sidebar / trigger-
editor "Debug trigger…" entry points). "Seed from a real row" is deferred to C2.**

## D10 Seam C — trigger launch UI + terminal debug states (2026-07-19, user-confirmed)

**Seam C (Triggers UI) — commit `050b790`.** The trigger becomes debuggable end-to-end. New Core
`TriggerHeaderReader` derives `(TargetTable, Timing, Events)` from the parsed `CREATE TRIGGER` (matching header
words by TEXT, since Firebird lexes BEFORE/AFTER/ACTIVE as identifiers) and returns null for a DB-level / DDL
trigger (out of scope, §8.1). `TriggerContextEditorViewModel` is a **dumb VM**: it holds no availability rules —
it builds a Core `TriggerContext` for the picked action and reads `NewAvailable`/`OldAvailable` from it; the
NEW/OLD grids show **only the columns the body references** (`ContextSubstitution.BuildColumns`, typed via
`EnsureColumnsAsync`) and reuse the Smart-Parameters editor; `CollectRootValues` maps each entered value onto its
synthetic frame variable (`ET_CTX_i`). `DebuggerTabViewModel` gained trigger mode (prepare → header/columns/
editor; launch → synthetic rootValues + `TriggerContext` into `DebugLaunchSpec`), a Variables **Context** group
(rows resolved through a new `DebugVariableRowViewModel.ResolveName` = synthetic; filtered by availability), and
entry points across `MetadataExplorer`/`MetadataNode`/`MainWindow`/`TriggerDetail` (sidebar + editor toolbar
"Debug trigger…"). Manual entry only; "seed from a real row" is a future C2.

**QA bug — gotcha #248 (per-reference colon).** A real ERP trigger stopped with `SQL -206, Column unknown
ET_CTX_3` on `podstwylcen = coalesce((select … where k.id_kartoteka = new.id_kartoteka), 0)`. Root cause: #247's
colon-vs-bare decision was **per statement** (`node is not PsqlStatement`), but a PSQL assignment can embed a DSQL
subquery — the `NEW.id_kartoteka` inside the subquery must be `:ET_CTX_3` while the l-value stays bare. Fix:
`ContextSubstitution.Substitute` takes `colonRegions` and decides **per reference**; the executor derives the
regions from the AST (whole node if it is a DSQL statement, else each embedded `SubqueryExpression` span —
`FirebirdDebugExecutor.ColonRegions`), never a token scan. Lab +`TRIG_SUBQ_LAB`/`TR_SUBQ_BU`; `DebuggerFidelityProbe`
case 17 proves sim==real (`NEW.NOTE='CNT=2'`), all prior cases still green.

**Terminal debug states — Completed / Faulted (follow-up commit).** At end-of-run the debugger no longer clears
the session (which made it "vanish"). **Completed** keeps the terminal snapshot visible with the block's closing
`END` marked (execution finished there — IBExpert-like), Variables / Context / Call Stack showing FINAL values.
**Faulted** stops **on the raising statement** (marked), keeps Variables / Context / Call Stack with the values
**at the error**, and the status line renders **red + bold** (`IsFaulted`). In both, stepping is disabled and
only Restart / Stop are active; **Stop** tears the session down and clears. Additive Core: `DebugSession` retains
`FinalFrame`/`LastStatement` (Completed) and snapshots `FaultStatement`/`FaultFrame`/`FaultStack` **before**
`ExceptionRouter.TryRoute` unwinds — the unwind DB-rolls-back + pops each frame but never touches its client-side
`frame.Values`, so the snapshot preserves the state at the raise (§4.5). `CurrentFrame`/`CallStack` still go null
after termination (existing contract unchanged). The debugger now has four distinct states: Running / Completed /
Faulted / Stopped. Architecturally the whole D10 + terminal-state work is additive, contract-preserving, unit-
tested, and live-fidelity-verified.

---

## Sprint D10.5 — UX polish: the harness-audit tab is now DEBUG-only ("Harness Log")

A short cleanup sprint between D10 and D11 (no new debugger capability). The debugger's bottom-panel
**"Executed SQL"** tab exposed the generated `EXECUTE BLOCK` harnesses (the §10.3/§F audit log) directly in the
production UI. Two problems: (1) the name read as "the user's SQL history", which it never was — it is a view of
how the debugger evaluates expressions/statements on the server; (2) that internal mechanism should not be part
of the interface a normal EmberTern user sees at all. It is a diagnostic surface for *developing / diagnosing the
debugger itself*.

**Decision (user, mid-sprint).** No new setting, no toggle, no reuse of the existing per-connection
`ConnectionProfile.DeveloperMode` — that flag has a specific, narrow domain meaning (metadata/DDL WAIT policy for
one connection) and must not be overloaded with debugger diagnostics; two different concepts. Instead the tab is
made a **compile-time DEBUG-only** surface: present in DEBUG builds, *absent* (not hidden, not disabled — simply
not compiled) in RELEASE.

**Implementation (additive, one commit's worth).**
- The tab's markup was **removed from `DebuggerTabView.axaml`** entirely and is now **built in code-behind under
  `#if DEBUG`** (`DebuggerTabView.axaml.cs` → `InsertHarnessLogTab` / `BuildHarnessLogTab` / `BuildHarnessRow`),
  inserted into the named `BottomTabs` `TabControl` at its historical position (right after Immediate). In RELEASE
  the `#if DEBUG` block (and its DEBUG-only `using`s) is not compiled, so the tab does not exist. Chosen over a
  csproj `Configuration`-conditioned separate `.axaml` (fragile XAML-globbing surgery, and a RELEASE-only path I
  can't runtime-verify here) and over runtime `IsVisible` hiding (the user explicitly wanted it *not compiled*,
  not merely hidden). The code-behind row builder faithfully mirrors the former XAML row (time | fragment | ±
  side-effect, then result/error text, harness SQL on the row tooltip) and consumes theme brushes as live
  `DynamicResource`s via `GetResourceObservable` (project rule: brushes via DynamicResource, never a snapshot).
- **Renamed** "Executed SQL" → **"Harness Log"** (`UiStrings.DebuggerBottomTabHarnessLog`); the dead
  `DebuggerBottomTabExecutedSql` const was removed.
- **Task 3 — self-explanation:** a persistent purpose description (`DebuggerHarnessLogDescription`) sits at the top
  of the tab and is also the header tooltip, plus a dedicated empty-state (`DebuggerHarnessLogEmpty`). Both state
  plainly that this is a diagnostic view of the debugger's generated `EXECUTE BLOCK`s, *not* a history of the
  user's SQL.
- **Nothing else changed.** The audit log itself (`DebuggerTabViewModel.ExecutedSql` / `HasExecutedSql` /
  `LatestEvaluation` / `AddExecutedSql`) is untouched and still collected in **every** build — it also feeds the
  Immediate tab's inline latest-result — so `HarnessBuilder`, `DebugSession.Evaluate`, Immediate, Evaluate
  (Shift+F9), Watches and the harness-SQL tooltips are all behaviour-identical. The `DebuggerTabVmTests` that
  assert on `ExecutedSql`/`HasExecutedSql` pass unchanged.

**Verification.** Build **0/0 in BOTH Debug and Release** (the RELEASE build proves the harness UI compiles out
cleanly with no unused-symbol warnings under `TreatWarningsAsErrors`), **4929 tests green** in one run. Live visual
confirmation (open a debug session in a DEBUG build → the "Harness Log" tab shows with its description; a RELEASE
build has no such tab) is the user's to make, per the QA rule.
