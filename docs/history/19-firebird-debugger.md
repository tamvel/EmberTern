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
