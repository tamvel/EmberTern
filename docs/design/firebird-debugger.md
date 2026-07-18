# Stage X — Firebird Debugger

**Status: DESIGN v2 — decisions ratified 2026-07-17. This is the target implementation
specification. Nothing implemented yet.**

Reference engine: **Firebird 5.0.3** (`Lab/EmberTern_Lab.fdb`). Read this before touching anything
under a future `EmberTern.Core.Sql.Debugging`. It assumes
[editor-architecture.md](editor-architecture.md) and [editor-ast-deepening.md](editor-ast-deepening.md)
— the debugger is a **client of the AST**, not a new front-end.

> **v2 changelog.** v1 was reviewed against the live engine and **four of its claims were falsified**.
> v2 corrects them. The load-bearing changes: harness declaration rules (§3.4 — v1 would have crashed on
> the first real procedure), **request-atomicity reconstruction via frame savepoints** (§4.5 — v1 silently
> diverged), **exception handling is core control flow and was entirely absent** (§3.6, prerequisite P1),
> the clock and generators are **Fidelity Boundaries** (§12), the "fourth lane" is **replaced by a
> per-session connection** (§4.1), **Watch/Evaluate/Immediate restored as one mechanism** (§9.5), FB
> support is **FB3+ only** (§1.3), and the milestone order is **reversed to put risk first** (§13).
> §2 records *why* v1 was too optimistic — that failure mode is itself a design lesson.

---

## 0. Decision log

Ratified by the project owner on 2026-07-17, after an architecture review that verified each item
against the live engine. Recorded here because a specification trusted for years must show *what was
decided and why*, not just its conclusions.

| # | Decision | Rationale |
|---|---|---|
| 1 | **Harness never materializes "uninitialized" as an explicit `NULL` assignment.** Harness params/RETURNS use **base types**; frame variables are declared **verbatim**. | Changes Firebird semantics otherwise — and crashes on `NOT NULL` domains. §3.4 |
| 2 | **Preserve single-request semantics via a SAVEPOINT per simulated frame.** | Firebird's call atomicity is real and observable. §4.5 |
| 3 | **`WHEN … DO` is control flow, not a feature.** AST deepening is a **prerequisite** to D1. A `WhenHandler` models one `WHEN` clause with an **ordered list of conditions** (Firebird allows a comma-separated condition list per `WHEN`), matched in declaration order (refined 2026-07-17). | The client owns control flow; exceptions *are* control flow. §3.6 / P1 |
| 4 | **Do not emulate `CURRENT_TIMESTAMP` & co. Document them as Fidelity Boundaries** — generators included. | Emulation is incomplete by construction and would trade an honest boundary for a hidden one. §12 |
| 5 | **No "fourth lane". The debugger gets its own connection + transaction, owned by the *session*, not the profile.** | A lane is a per-profile singleton; sessions need independent transactions. §4.1 |
| 6 | **Evaluate / Watches / Immediate = one HarnessBuilder mechanism, three UI surfaces.** A pin does **not** replace a Watch. | A watch is an *expression*, not a variable. §9.5 |
| 7 | **Debugger supports Firebird 3, 4, 5 only.** FB2.5 is not a supported engine. | FB2.5 has no sub-routines/packages; and see §1.3 — it is already unreachable. |
| 8 | **Verify the server version on connect; reject below FB3 with a clear message.** No partial-compatibility mode. | Legible failure over a mysterious one. §1.3 / P2 |
| 9 | **Selectable-procedure step-into is a Fidelity Boundary for now.** | Must not block the first working debugger. §12.7 |

---

## 1. Feasibility analysis — the one fact everything follows from

> **Firebird has no debugging API. None. At any version.**
> No breakpoint, no pause-on-statement, no suspend-a-running-routine, no read-the-variables-of-a-stopped-
> frame, no step protocol. Not in the wire protocol, not in the services API, not in the catalog.

Verified against the live engine rather than assumed (project rule: *verify Firebird behaviour, never
infer it*). §15 is the full log. The relevant negatives:

| Facility | What it actually is | Can it debug? |
|---|---|---|
| `RDB$DEBUG_INFO` (`RDB$PROCEDURES` / `RDB$FUNCTIONS` / `RDB$TRIGGERS`) | A BLOB holding a **BLR-offset → source line/column map + variable names**, so the *engine* can say "at line 3, col 5". | **No.** A map, not a protocol. Static, read-only. |
| `MON$CALL_STACK` | A live, **read-only** view of the PSQL call stack of *currently running* statements (`MON$OBJECT_NAME`, `MON$CALLER_ID`, `MON$SOURCE_LINE`, `MON$SOURCE_COLUMN`). | **No.** Observation only, and only while running. A *stopped* routine is not an engine concept. |
| `RDB$PROFILER` (FB 5.0) | A real instrumented profiler (`START_SESSION`/`PAUSE`/`RESUME`/`FINISH`). Per-line/column stats. Verified working from a plain client. | **No.** Measures; cannot stop or inspect. Valuable as a *profiler* (§10.5). |
| Trace API (`FirebirdTraceService`) | Statement-level events. | **No.** Shows the *call*, not the internals. |
| Profiler *plugin* API (FB5) | Native server-side plugin with PSQL line/column callbacks. | **Rejected.** A profiler contract (cannot block or read variables), and it needs a native DLL deployed into the customer's server. EmberTern is a client tool. |

**Therefore: every Firebird debugger — IBExpert's included — is a client-side PSQL interpreter.** The
stored routine is *never executed as a unit*; what runs is a **reconstruction from source**. This is the
shape of the problem, not a limitation to engineer away.

### 1.1 What this means for the product

IBExpert's failure modes are what happens when you build an interpreter and **don't admit it**. Local
functions return `NULL` because its interpreter cannot call a sub-routine through DSQL, so it gives up —
*silently*. The user sees a wrong value, not an error. That is the anti-pattern this design exists to
avoid.

### 1.2 Version support — FB3 / FB4 / FB5 only (decision 7)

The debugger targets **Firebird 3, 4 and 5**. FB2.5 is out of scope: it has **no sub-routines and no
packages** (both FB3+), so the flagship capability is meaningless there.

### 1.3 The version gate is free — FB2.5 is already unreachable (decision 8)

**This decision drops nothing that works today.** `FirebirdSql.Data.FirebirdClient` 10.3.4 implements
**only Srp / Srp256** — there is no `Legacy_Auth` code path in the managed assembly (CLAUDE.md, *Known
driver gotchas*). **Srp was introduced in Firebird 3.0; FB2.5 authenticates only via Legacy_Auth.**
EmberTern therefore *cannot already* connect to an FB2.5 server; today that surfaces as a confusing
authentication failure.

So the explicit gate is not a removal of support — it **ratifies reality and makes the failure legible**.

> **Status — DONE (P2, 2026-07-17).** `FirebirdConnectionService.IsSupportedServerVersion` (reusing
> `FirebirdDdlReader.ParseServerMajor`) gates `ConnectAsync` (right after the first attachment opens,
> before the Metadata/Ddl lanes) and `TestConnectionAsync`, refusing a positively-identified pre-FB3
> server with a message that names the required version and closing cleanly. It **fails open** on an
> unparseable version string (a live Srp connection is FB3+ by construction). Live rejection is unverified
> (no FB2.5 instance); the predicate is table-pinned. See `docs/history/19-firebird-debugger.md`.

> **⚠ Scope note for review.** The gate is **app-wide**, not debugger-scoped, so it is milestone **P2**,
> deliberately *outside* the debugger's own milestones. It also touches a documented decision: *"Connection
> errors show the raw server message… do not add hints or interpret error causes."* A **precondition check
> on `ServerVersion` is not error interpretation** — it runs before/independently of any server error and
> states a fact EmberTern knows for certain. That rule stands untouched; the gate sits beside it.
> Implementation reuses the existing `FirebirdDdlReader.ParseServerMajor(connection.ServerVersion)` (already
> the app's one version-parsing site — used by five readers). Follow-up cleanup, not urgent: the existing
> `serverMajor >= 3` gates (e.g. `StandalonePackageFilter`) become statically true.

### 1.4 Compatibility matrix

| Capability | FB3 | FB4 | FB5 | Note |
|---|:--:|:--:|:--:|---|
| `EXECUTE BLOCK` (the harness) | ✔ | ✔ | ✔ | FB2.0+. |
| Sub-routines (`DECLARE PROCEDURE/FUNCTION`) | ✔ | ✔ | ✔ | FB3+. The reason FB2.5 is out. |
| Packages | ✔ | ✔ | ✔ | FB3+. |
| **Sub-routine closures over outer variables** | **⚠ VERIFY** | **⚠ VERIFY** | ✔ verified | **Blocks D9.** FB3 documented sub-routines as *unable* to access outer variables. If true, FB3 is *simpler* (no closures). §6.3 |
| Savepoints (`SAVEPOINT` / `ROLLBACK TO`) | ✔ | ✔ | ✔ verified | Frame atomicity (§4.5). |
| Multiple cursors + interleaved commands per attachment | ⚠ verify | ⚠ verify | ✔ verified | Cursor Bridge (§7). Driver-level; expected uniform. |
| `DECFLOAT`, `INT128`, `TIME ZONE` types | — | ⚠ | ⚠ | Round-trip fidelity risk (§12.6). |
| `RDB$ERROR(…)` in handlers | — | ✔ | ✔ | FB4+. §3.6 |
| `RDB$PROFILER` | — | — | ✔ | Not the debugger (§10.5). |

**Rule:** every ⚠ is an **explicit probe to run before its milestone**, never an inference. §15 carries the
recipes.

---

## 2. The Fidelity Law (§F) — the design's spine

The project's Paramount Law (Architecture rule #11): *never modify what you cannot reproduce identically;
uncertainty ⇒ do nothing or ask.* The debugger's analogue:

> **§F — The Fidelity Law.** The debugger must never present a simulated result it cannot produce
> faithfully. Where fidelity is impossible or uncertain, it **stops and says so** — it never guesses, never
> silently substitutes `NULL`, never quietly diverges.

Concretely:
- A construct the interpreter cannot execute faithfully ⇒ a **first-class, explained stop**, not a wrong value.
- Every boundary in §12 is **named, detected where possible, and surfaced**.
- The user can always see **exactly what SQL EmberTern sent** (§10.3). The run is a simulation;
  auditability is the trust anchor. Not optional.

### 2.1 Why v1 failed its own law — the lesson

v1 declared §F and then **violated it in four places**, because it reasoned from plausible engine
behaviour instead of measuring:

- it assumed a per-statement harness preserved call atomicity — **it does not** (§4.5);
- it assumed injecting frame state was semantically neutral — **it is not** (§3.4);
- it never asked whether the clock was request-scoped — **it is** (§12.5);
- it omitted exception handling entirely, the most common PSQL control flow there is.

Every one was found by a probe that took minutes. **The lesson is structural, not incidental: §F is not
satisfiable by intent. Each boundary must be measured, and a boundary nobody looked for is indistinguishable
from a boundary that does not exist.** This is why §15 exists, why every ⚠ in §1.4 blocks a milestone, and
why §13's definition-of-done requires comparing simulated results against real execution.

---

## 3. Architecture

### 3.1 The core split — and its one refinement

> **The client owns control flow. The server owns semantics.**

| Layer | Owner | Why |
|---|---|---|
| Control flow — blocks, `IF`, `WHILE`, `FOR`, **exception handlers**, stepping, frames, breakpoints | **Client** (interpreter over the AST) | The only part an engine-less debugger *can* own. |
| Semantics — expressions, coercion, collations, dialect, DML, function calls, `CASE`, `NULL` | **Server**, every step | EmberTern must **never** re-implement Firebird's type system. A re-implementation is a second, divergent engine. |

**The refinement v1 missed.** Some semantics are attached to the **request boundary**, not to a statement.
A per-statement harness dissolves that boundary and silently loses them. Three cases, all measured:

| Request-scoped semantic | Consequence | Resolution |
|---|---|---|
| **Call atomicity** — an unhandled exception out of a routine undoes *its* DML | Stepping into a routine that raises leaves state reality would have undone | **Reconstruct** — SAVEPOINT per frame (§4.5) |
| **Clock** — `CURRENT_TIMESTAMP` frozen per request | Advances between steps; a human steps in *seconds* | **Boundary** — §12.5 (decision 4) |
| **Coroutines** — `SUSPEND` in selectable procedures | Step-into needs generator frames | **Boundary** — §12.7 (decision 9) |

> **The refined rule: the client owns control flow; the server owns semantics; and every request-scoped
> semantic is either explicitly reconstructed or explicitly declared a boundary. Never left to chance.**

This split has a decisive consequence, and it is a gift:

> **The debugger needs no expression AST.** Expressions ship to the server **verbatim** as token spans.

EmberTern's AST is deliberately at *structural depth* — ordinary expressions stay token fragments
(`editor-ast-deepening.md` §12). This design **validates that boundary** rather than forcing a deepening.
§0's "preserve verbatim what you do not fully understand" is exactly the transport mechanism.

### 3.2 The Evaluation Harness — the central mechanism

Every server interaction is a generated, anonymous `EXECUTE BLOCK` that:

1. declares the variables the fragment needs,
2. **injects their current values** as real parameters (subject to §3.4),
3. carries **sub-routine declarations verbatim**,
4. runs the **one** statement / evaluates the **one** expression, verbatim,
5. **returns the result plus every variable the fragment may have mutated**.

Zero metadata is created. Ever. Verified end-to-end (§15 Q5):

```sql
EXECUTE BLOCK (P_A INTEGER = ?, P_B INTEGER = ?)      -- 2. current values injected (base types!)
RETURNS (EXPR_VALUE INTEGER, OUT_A INTEGER)           -- 5. result + write-back (base types!)
AS
DECLARE V_A INTEGER;                                   -- 1. frame variables, verbatim from source
DECLARE V_B INTEGER;
DECLARE FUNCTION LOCAL_F(X INTEGER) RETURNS INTEGER AS -- 3. sub-routine, verbatim
BEGIN
  V_A = V_A + 1;                                       --    (mutates a captured outer var)
  RETURN X * 10;
END
BEGIN
  V_A = P_A;                                           --    (skipped when the value is NULL — §3.4)
  V_B = P_B;
  EXPR_VALUE = LOCAL_F(V_B) + V_A;                     -- 4. the user's expression, verbatim
  OUT_A = V_A;                                         --    write-back of the mutated capture
  SUSPEND;
END
```

With `V_A=5, V_B=3` this returns **`EXPR_VALUE=36`, `OUT_A=6`**. Note *why* that is right: `LOCAL_F` ran
first (mutating `V_A` to 6), then `+ V_A` read **6** → 36. **EmberTern did not compute that ordering —
Firebird did.** The harness never re-implements evaluation order, so it cannot get it wrong. That property
is the whole argument for this design.

### 3.3 The harness is the *only* server mechanism

Every consumer — a step, a breakpoint condition, a Watch, the Immediate window, an evaluated selection —
is the same builder with a different fragment. **One mechanism, many surfaces** (decision 6). If a feature
needs the server, it goes through `HarnessBuilder`; there is no second path.

### 3.4 Harness declaration rules (decision 1) — **non-negotiable**

v1's naive injection **crashes on real code**. Verified (§15 F1/F2): a `NOT NULL`/`CHECK` domain variable
that is merely *declared* is fine in real execution, but the harness's `V = P_V` with `P_V = NULL` yields:

```
validation error for variable V, value "*** null ***"
```

The user's own screenshot shows the pattern this breaks: `declare variable v_ilosc_z_dostawy t_iloscstan null;`
— the `null` suffix exists **precisely because** the domain is `NOT NULL`. Domain-typed, uninitialized
variables are everywhere in real ERP PSQL. **v1 would have died on the first procedure.**

> **R1. Never assign an injected `NULL`.** A declared variable is already `NULL`; the assignment is
> redundant *and* semantically wrong — it converts "never assigned" into "assigned NULL", which is what
> the constraint rejects. Skip injection for `NULL` values entirely.
>
> **R2. Harness parameters and `RETURNS` columns use BASE TYPES — never domains, never `TYPE OF`.** A
> domain-typed `RETURNS` column would re-validate on `SUSPEND` and fail on write-back of a legitimately
> `NULL` variable.
>
> **R3. Frame variables are declared VERBATIM** (domain, `TYPE OF COLUMN`, `NOT NULL`, `CHECK`, default —
> all of it), copied from source. This preserves domain semantics *on the statement's own assignments*,
> which is what fidelity requires.
>
> **R4. Inject only what the statement reads; return only what it may write** (§3.5). Fewer assignments =
> fewer chances to trip R1, and it is also the performance fix.

R2 is a conscious, bounded trade: a *base-typed parameter* skips domain `CHECK` validation **on injection**
(a value that already passed the check when it was first assigned). The check still applies to every
assignment the user's own code performs (R3). Accepted (decision 1).

**Derivation, not guessing:** base types come from metadata (the domain's underlying type), reusing
`FirebirdDdlReader`/`ObjectMetadata` — never from string-munging the declaration.

### 3.5 State injection is read/write-set-driven (generalizes v1's closure rule)

v1 computed a read/write set **only for sub-routines**. That was a special case of a general need:

> **Every step injects only the variables the statement references, and returns only those it may write** —
> computed from the **SemanticModel** (the binder already resolves every reference), with a fixpoint over
> the sub-routine call graph for transitive calls.

This is one mechanism instead of a special case, and it solves three problems at once: R1 exposure, the
BLOB/perf problem (§12.6 — otherwise the *entire live frame* ships both ways every step; a 10 MB BLOB
variable = 20 MB per step), and closure capture (§6.2).

Fallbacks, in §F order: unresolved reference ⇒ inject **all** in-scope variables (correct, chattier); a
value that cannot round-trip (a `CURSOR`-typed variable) ⇒ **do not fast-path — interpret, or stop and
explain**. Never guess.

> **⚠ R5 — the read-set optimization must NEVER drop a sub-routine declaration.** **Always carry every
> sub-routine declaration in scope**, regardless of the read set. If local `F()` shadows a global `F()` and
> the declaration is omitted, the harness **silently calls the global one** — a wrong answer with no error.
> A §F violation of the worst kind. Sub-routine declarations are *scope*, not *data*.

### 3.6 Exception handling is control flow (decision 3)

v1 omitted `WHEN … DO` entirely — the most common PSQL control flow there is. It is **client-owned**, like
`IF`/`WHILE`, and the interpreter must implement:

- **Handler matching** — a `WHEN … DO` clause matches one of `ANY`, `EXCEPTION <name>`, `GDSCODE <x>`,
  `SQLCODE <x>`, `SQLSTATE '<x>'`. Firebird lets a **single `WHEN` list several conditions**, comma-
  separated, sharing one `DO` body (`WHEN GDSCODE a, GDSCODE b, EXCEPTION c DO …`); the interpreter tries
  each condition of each clause **in declaration order** (conditions within a clause left-to-right, clauses
  top-to-bottom). So the AST models **one `WhenHandler` per `WHEN` clause**, carrying an **ordered list of
  conditions** (each a kind + its optional operand) plus the body — never a single condition per node
  (decision 3, refined 2026-07-17). A `WHEN` whose shape the parser cannot recognise as `WHEN <conditions>
  DO <body>` falls back to the lossless `PsqlLeafKind.Other` valve, never a misleading structured node.
- **Propagation** — unwind frames until a handler matches; unwinding a frame triggers its savepoint
  rollback (§4.5).
- **Re-raise** — bare `EXCEPTION;` inside a handler.
- **Error context inside a handler** — `GDSCODE` / `SQLCODE` / `SQLSTATE` / `RDB$ERROR(…)` (FB4+) exist
  **only inside a real handler**. When the interpreter evaluates a fragment inside a *simulated* handler,
  the harness must **substitute those references with injected parameters** — the same
  substitute-by-resolved-span mechanism as trigger `NEW`/`OLD` (§8.1), never a text rewrite. One mechanism,
  two consumers.

Error → exception mapping comes from the driver's `FbException` (SQLSTATE / GDS codes), not from parsing
messages.

> **⚠ Prerequisite P1 — AST deepening.** The AST does **not** model handlers: `PsqlLeafKind.Other` is
> documented as *"a WHEN … DO handler leaf"*, i.e. handlers are an unstructured token bag. **The
> interpreter cannot read exception control flow from the tree as it stands.** A `WhenHandler` node
> (one per `WHEN` clause, holding an ordered `WhenCondition` list + a body) + `BlockStatement.Handlers`
> is required **before D1**, following Etap 6.9's contract (parser producer → binder consumer; formatter
> convergence later, only if a feature needs it).

### 3.7 Component map

```
                     ┌──────────────────────────────────────┐
                     │   SqlParser → AST  (+P1: handlers)   │  IExecutableStatement = step unit
                     │   SemanticBinder → SemanticModel     │  scopes, symbols, references
                     └───────────────┬──────────────────────┘
                                     │  (consumes — never re-parses)
   EmberTern.Core.Sql.Debugging      ▼
   ┌───────────────────────────────────────────────────────────────────┐
   │ DebugSession      — state machine: Run/Pause/Step/Stop            │
   │ Frame             — one activation: scope chain + values + savept │
   │ FrameValues       — variable store (client-side, the truth)       │
   │ StepPlanner       — Into/Over/Out/RunToCursor/SetNext             │
   │ ExceptionRouter   — handler matching + frame unwinding            │
   │ BreakpointSet     — spans + conditions + hit counts               │
   │ HarnessBuilder    — AST span + frame → EXECUTE BLOCK (§3.4/§3.5)  │
   │ IDebugExecutor    — the ONLY seam to the server (contract only)   │
   └───────────────────────────────┬───────────────────────────────────┘
                                   │
   EmberTern.Firebird              ▼
   ┌───────────────────────────────────────────────────────────────────┐
   │ FirebirdDebugExecutor : IDebugExecutor  — prepares + runs harness │
   │ DebugSessionConnection                  — per-session cn + tx     │
   │ CursorBridge                            — PSQL cursor ↔ DSQL      │
   └───────────────────────────────────────────────────────────────────┘
```

`EmberTern.Core.Sql.Debugging` is **pure**: zero Avalonia, zero `FirebirdSql`. The interpreter is headlessly
testable against a fake executor — which matters, because control flow is the part we own and therefore the
part we can get wrong.

**On Architecture rule #2** ("no interfaces without two concrete implementations"): `IDebugExecutor` is a
seam Core *requires* (Core cannot reference `EmberTern.Firebird`). Precedent: `ISqlMetadataProvider` in
`Core/Sql/Language/Semantics/` — same shape, same reason. A deliberate, precedented exception, flagged for
review rather than smuggled in.

### 3.8 No temporary metadata — ever

IBExpert generates temporary packages to debug local routines. EmberTern does not: §6 makes it unnecessary,
and mutating the user's database in order to observe it is a §0-adjacent hazard.

---

## 4. Transaction model

### 4.1 Per-session connection — not a lane (decision 5)

v1 proposed a fourth lane. **That was wrong.** Lanes (Data / Metadata / Ddl) are **per-profile singletons**;
a debug session needs its **own transaction**, and two debug tabs = two sessions = two transactions —
impossible on one lane.

> **A debug session owns a `DebugSessionConnection`: its own `FbConnection` + its own transaction, created
> on session start and disposed on session end. Lifetime is bound to the *session*, not the profile.**

This is also *simpler*: it sidesteps the lane machinery entirely — no new `ConnectionRole`, no new entry in
`GetCommandLock(role)`, and no exposure to the lane-resolving-accessor hazard (gotchas #98/#120), because a
session connection never flips lanes.

It **must not** use the Data lane, the first reason being disqualifying:

1. **Data loss (rule #11).** The Data lane holds *the one user working transaction*. A debug rollback would
   destroy the user's uncommitted work. Non-negotiable.
2. **Ambiguous commit.** The user's Commit would commit the debugger's half-finished side effects.
3. **A human-paced transaction.** Debug sessions live for minutes; on the Data lane that pins the user's
   transaction (GC pressure, blocking) — and EmberTern's own Session Manager long-running-transaction
   detector would fire on the app's own doing.

**Consequences to handle:** each session is another attachment (respect server connection limits; degrade
with a clear message rather than breaking — the thinking behind gotcha #89); the session connection must
register with `FirebirdConnectionService` lifecycle so disconnect/reconnect tears sessions down deterministically.

### 4.2 TPB

Explicit `FbTransactionOptions` — **never** a bare `IsolationLevel` (gotcha #85). Default:
**READ COMMITTED, REC_VERSION, NOWAIT**.

`NOWAIT` because the debug transaction *will* meet locks held by the user's Data transaction. A hung
debugger with no explanation is worse than a reported conflict: `NOWAIT` turns it into a **step-level error
at a known line** — exactly the information the user needs. (Contrast the Ddl lane, WAIT-bounded *because*
its job is a short autonomous compile, gotcha #214. Different job, different TPB, same evidence-first basis.)

**Isolation is user-selectable at launch** (§12.4): a routine normally called under SNAPSHOT sees different
data under READ COMMITTED. Defaulting silently would be a hidden boundary.

### 4.3 Isolation from the user's work

The debug transaction is a **different transaction** from the SQL Editor's, so uncommitted editor changes
are **invisible** to it, and it can **conflict** with them. Stated in the launch panel (§9.2), not discovered.

### 4.4 Ending a session

- **Default: rollback, no prompt.** That *is* the contract of a debug run.
- **Explicit `Commit debug transaction`** for the rare intentional case — visually distinct, confirmed.
  Respects rule #3 (*no autocommit ever*; auto-*begin* is fine) without silently discarding intended work.
  Reuse `CommitButtonBrush` / `RollbackButtonBrush`.

### 4.5 Frame savepoints — reconstructing call atomicity (decision 2)

**Measured (§15 A/C), and it falsified v1:**

| Situation | Firebird's actual behaviour |
|---|---|
| Exception **caught by `WHEN` in the same block** | prior statements of that block **survive** (`ROWS_SURVIVED=1`) |
| Exception **unhandled, propagating out of a called procedure** | that procedure's DML **is undone** (`SURVIVED=0`) — even though the caller catches and continues |

A real call is one request and the engine undoes it atomically. A **simulated** frame is N independent
top-level harnesses — so the engine undoes **nothing**, and the caller's handler runs against a database
state reality would never have produced. Silent, and it affects data.

> **The rule: SAVEPOINT on entry to every *simulated frame*; `ROLLBACK TO` that savepoint when the frame
> exits via an **unhandled** exception, then propagate to the caller.**
>
> **NOT per block.** A `WHEN`-handling block must **not** roll back (measured: prior statements survive).
> **NOT per statement.** That is D14's opt-in step-back, a different feature with a different cost.

Precise, cheap (one savepoint per *call*, not per step), verified through the driver (§15 probe [5]), and it
applies uniformly to the root frame — so an unhandled exception at the top leaves the same state reality
would. Nested frames ⇒ nested named savepoints keyed by frame id.

### 4.6 ⚠ What the rollback does **NOT** undo

Two measured cases where **"debugging is always safe, nothing is persisted" is FALSE**:

1. **`IN AUTONOMOUS TRANSACTION`** — verified (§15 Q8): the autonomous `INSERT` **survived** the outer
   `ROLLBACK`; the outer-transaction `INSERT` did not. Work committed there is **permanent**.
2. **Generators** — `GEN_ID()` / `NEXT VALUE FOR` are **outside transaction control** by design. The
   rollback does not restore them, and every **Restart** consumes more values.

Both are data-safety facts (rule #11 territory) and invisible unless surfaced. **The pre-flight scan (§9.2)
detects both in the routine (and, where resolvable, its callees) and warns before the session starts** —
naming the construct and its location. The user's call; not their surprise.

---

## 5. Nested debugging

### 5.1 The verdict on IBExpert's model

IBExpert opens a **new window per Step Into**, closing it on return. The good part is worth naming: it makes
**call depth tangible**. But it is a window-manager simulation of a data structure: it scales badly (5 deep =
5 windows), you cannot compare two frames' variables, you cannot jump to an *outer* frame without unwinding,
and closing the wrong window is destructive. Crucially it **cannot represent a local routine as a frame at
all** (§6) — the model's expressiveness is capped by its UI metaphor.

### 5.2 The proposal: one tab, a real call stack

> **One debugger tab. Frames are data, not windows.** Step Into pushes a frame and swaps the editor's
> document. Nothing opens; nothing closes.

- **Call Stack panel** — frames innermost-first. Selecting a frame shows *that* routine's source at *that*
  frame's line and repoints the Variables window. Standard modern-IDE semantics.
- **Current frame** gets the solid current-line marker; **caller frames** a subtler call-site marker.
- **Breadcrumbs** — `SP_ADD_ORDER › SP_VALIDATE › LOCAL_F`, clickable, mirroring the stack. This is the
  **Breadcrumbs backlog feature**, on the same AST — build it *as* the shared feature, not a debugger copy.
- **Keyboard**: `Ctrl+Alt+Up/Down` walks frames. Never mouse-only.
- **Peek Frame instead of split view** — reuse **Peek Definition** to peek the caller's line inline, then
  dismiss. Same muscle memory, zero permanent layout cost. (Additive later if real usage demands a split;
  the frame model already supports it. Do not build it up front.)
- Frame/breadcrumb jumps reuse `NavigationController.JumpTo` — no second navigation mechanism.

### 5.3 The asymmetry that must be surfaced

| Action | What happens | Fidelity |
|---|---|---|
| **Step Over** `EXECUTE PROCEDURE B(…)` | The harness sends the real call — **B's compiled BLR runs**. | 100% faithful, fast. |
| **Step Into** `EXECUTE PROCEDURE B(…)` | We fetch B's source, parse it, push a frame, and **interpret** it. | A simulation (+ §4.5 savepoint). |

> **Step-over is real execution. Step-into is simulation. They can differ.**

Inherent to every Firebird debugger. IBExpert never says it; under §F we must. A simulated frame carries a
quiet, permanent indicator — a fact, not a nag.

---

## 6. Local procedures and functions — the flagship

### 6.1 What was verified (§15 Q1–Q5, FB5)

| Question | Answer |
|---|---|
| Sub-**function** inside `EXECUTE BLOCK`, no metadata? | **Yes** — 42 |
| Selectable sub-**procedure** (`SUSPEND`) via `FOR SELECT`? | **Yes** — 10 |
| Sub-routine **reads** an outer variable? | **Yes** — 5 |
| Sees **mutated** outer values (by reference)? | **Yes** — 99 |
| Sub-routine **writes** an outer variable? | **Yes** — 77 |

> **Sub-routines in FB5 are true closures over the parent's frame — by reference, read *and* write.**

This contradicted the going-in assumption (FB3 documented *no* outer access) — which is exactly why it was
measured. Load-bearing: a sub-routine is **not** a closed, extractable unit.

### 6.2 The strategy — two halves, no temporary metadata

**(a) Step *into* a local routine ⇒ just interpret it.**

> A sub-routine body is PSQL. The interpreter interprets PSQL. **A local routine is not a special case — it
> is a frame whose lexical parent is the declaring frame.**

Nothing new is needed. The AST already parses `DECLARE PROCEDURE/FUNCTION` bodies and the binder already
traverses them against the enclosing scope (Etap 6.9 / B1b). The closure semantics of §6.1 fall out **for
free**: the interpreter's scope chain already models an inner scope whose parent is the declaring scope, so
outer reads/writes are scope-chain resolution against `FrameValues`.

IBExpert fails because its interpreter must *call* the routine through DSQL and cannot — hence temporary
packages, or `NULL`. **EmberTern never calls it. It interprets it.** Payoff: local routines are **real frames
in the call stack, with real variables, steppable line by line** — which IBExpert cannot do at all.

**(b) Step *over* a local call — or evaluate any expression containing one ⇒ the harness (§3.2).**

Carry the sub-routine declarations verbatim (**always** — R5), inject the captured read set, read mutated
captures back. Verified (Q5): 36 / 6, side effect and evaluation order both correct.

### 6.3 ⚠ Version gate — blocks D9

§6.1 was measured on **FB5.0.3 only**. FB3 documented sub-routines as **unable** to access outer variables;
if true, FB3 is *simpler* (no closures) and the closure harness is FB4+/FB5. **Measure Q2/Q3/Q4 on FB3 and
FB4 before implementing D9** (FB3 is installed locally on port 4050). Recorded as a gate, not an assumption.

---

## 7. Cursors — the sleeper problem

A PSQL cursor lives **inside one compiled PSQL request**; our statements run in **separate** `EXECUTE BLOCK`s.
So a cursor cannot survive between steps — and `FOR SELECT … DO` must hold its cursor open *across every
iteration while the user steps through the body*. `FOR SELECT` is in nearly every real procedure. Not an edge
case: the common case.

**Rejected:** materializing the result set client-side. It diverges on cursor-stability semantics, and an ERP
`FOR SELECT` over millions of rows would exhaust memory. §F.

**The Cursor Bridge:** map each PSQL cursor to a **real DSQL cursor on the session connection**, fetched
incrementally, in the real debug transaction. A `FOR SELECT`'s cursor query is *just a SELECT*; fetch one row
per iteration and assign the `INTO` targets into the frame client-side.

**Verified feasible at the driver level (§15 probes [1]–[4])** — this was the review's biggest open risk:
- a harness statement **runs fine on the same connection + transaction while a cursor is open**;
- the cursor is **still usable afterwards** (resumed and fetched the next row);
- **two cursors can be open simultaneously** (nested `FOR SELECT`).

> **⚠ Gotcha #31 must be read precisely — v1's reviewer initially misread it.** *"There is already an open
> DataReader…"* is a **concurrency** constraint (parallel commands from different threads), **not** a
> multiplexing one. Firebird supports multiple statement handles per attachment and the managed driver
> exposes them. **Interleaving is fine; concurrency is not.**
>
> **⚠ But the naive implementation violates EmberTern's locking convention.** #31's rule is that a command
> holds the `CommandLock` *"for the whole wire-touching operation"*. Holding it for a **cursor's lifetime**
> would deadlock every harness step inside the loop. **The session connection therefore takes the lock per
> *wire operation* (each `Read()`, each harness execute) — not per cursor lifetime.** This is a deliberate,
> reviewed narrowing of #31 for a connection that is single-threaded by construction (one debug session
> drives it), documented here so it is never mistaken for an oversight.

**Directly enabled by AST work already done:** `ForSelectStatement.Query` and `DeclareCursorStatement.Query`
are real `QueryNode`s with exact token spans (Etap 6.9 / B3.1) — the cursor's SQL is extractable verbatim.

**D6 as built (colon-only injection):** the pure `CursorBridge` builds the DSQL cursor SELECT from
`ForSelectStatement.Query`'s span, rewriting **only the `:name`/`@name` parameter form** to positional `?`
(the unambiguous variable-reference syntax; a bare name is a column — §15.5 / gotcha #239), and `IntoTargets`
(D6a) map the fetched columns onto the frame. `CursorHandle` (Firebird) holds the real `FbDataReader` open
across steps with **per-wire-op** locking. Nested `FOR SELECT` and fidelity are proven (§15.5).

**Open sub-problems (follow-ups, not in D6's DoD):** `FOR SELECT … AS CURSOR c` with `WHERE CURRENT OF c` —
positioned DML on a named DSQL cursor is **unsupported cross-context** (§15.5 [12], SQL -504); it surfaces as
an honest step error (the AST now carries `CursorName` to detect it). Explicit `DECLARE … CURSOR` +
`OPEN`/`FETCH`/`CLOSE` (same bridge, user-driven) is a later milestone. **The real constraint is not the
driver — it is §12.7** (a cursor query that calls a routine we are stepping into).

---

## 8. Triggers and packages

### 8.1 Triggers

No engine API ⇒ no attaching to a real firing trigger. So:

> **Debugging a trigger does not perform the triggering DML.** The user supplies `NEW`/`OLD`; we interpret
> the body. Same model as IBExpert, and honest about it.

`NEW`/`OLD` do not exist in an `EXECUTE BLOCK`, so the interpreter models them as **frame variables** and the
harness substitutes them. The semantic model already declares them — `RecordAliasSymbol` (NEW/OLD) and
`TriggerPredicateSymbol` (`INSERTING`/`UPDATING`/`DELETING`), built during the UX Polish sprint.

> **Substitution replaces resolved `SymbolReference` spans — never text search.**

A textual `NEW.X` rewrite would corrupt string literals, comments and quoted identifiers. The binder already
produced every reference with an exact span. **The same mechanism serves the handler error context** (§3.6) —
one substitution engine, two consumers. (This rewriting happens **only** inside a generated harness; EmberTern
never writes modified source back to the database.)

**Availability rules (drive the parameter editor, §9.3):**

| Trigger | `OLD` | `NEW` |
|---|---|---|
| BEFORE INSERT | unavailable | **editable** |
| AFTER INSERT | unavailable | read-only |
| BEFORE UPDATE | read-only | **editable** |
| AFTER UPDATE | read-only | read-only |
| BEFORE / AFTER DELETE | read-only | unavailable |

(`OLD` is always read-only in Firebird; `NEW` is writable only in BEFORE.) For a multi-action trigger
(`BEFORE INSERT OR UPDATE`), the user **picks the simulated action** — driving both context availability and
the `INSERTING`/`UPDATING`/`DELETING` values. **Database-level and DDL triggers: out of scope** (different
context entirely) — stated, not half-built.

### 8.2 Packages

Simpler than expected: **Firebird packages have no package-level variables** — only procedures and functions —
so there is no package state to model.

A **public** sibling call is real metadata: step-over calls it for real; step-into fetches its source from the
package body and pushes a frame. A **private** routine (in the body, absent from the header) is real compiled
metadata but **not callable from DSQL outside the package** — so the harness cannot call it. This lands on the
§6 answer: **interpret it.** Step-over of a private routine = interpret without UI updates.

> **⚠ Open probes for D11:** confirm a private package routine is not callable from an `EXECUTE BLOCK`, and
> that private-routine source is extractable from the package body (a body is one source blob — extracting an
> individual routine is real parsing work, not a lookup). The lab's `PKG_ORDERS` has only public routines
> (`RDB$PRIVATE_FLAG = 0`) — **extend `Lab/setup.sql`** with a private routine and verify.

---

## 9. UI / UX

Philosophy: **a modern IDE debugger, not a DB admin tool.** Consistent with the rest of EmberTern.

> **⚠ D4 UX-review backlog (user, 2026-07-17).** After the first real use of the D4 tab, the user filed an
> 8-item UX-polish backlog (first-class Debug entry points; move transaction config to global Settings; a
> subtler current-line marker — the amber fill is too aggressive in dark theme, wants a ~10–15% blue wash + a
> thin left bar; variable-kind distinction IN/OUT/local; more distinct step icons; edit-params on a running
> session; richer parameter history; AST-derived paused status). Full list + the binding directive
> (**fix UX in the view/theme tokens, never by pushing logic into the debugger VMs/UI — keep the D1–D4 split**)
> are in `docs/history/19-firebird-debugger.md` §"D4 UX review". Fold these into their natural milestones
> (variable kinds → D7; the rest → a UI-polish pass); do not pre-build them.

### 9.1 A separate tab — confirmed

**Debugging is a different activity from editing.** A debug tab is read-only source + runtime state; an editor
tab is authoring. Conflating them means every editing affordance grows a "but not while debugging" mode — the
complexity spiral EmberTern avoids.

Consequences: **no Easy Mode** (correct — it hides the body, and local routines live in the body); the source
shown is the **full, real** routine source.

### 9.2 Launch — a panel, not a modal

The tab opens on a **launch panel**. You re-run constantly (Restart); a modal you must re-summon each time is
IBExpert's tax. It carries:
- the **parameter editor** (§9.3);
- the **pre-flight report** — reuse `DiagnosticsEngine` for unresolved names, **plus the §4.6 warnings
  (autonomous transactions, generators)** and any §12 boundary detected in the routine. *Tell the user what
  this run cannot promise, before it starts.*
- the **transaction isolation selector** (§4.2) and the §4.3 note.

### 9.3 The parameter editor

**Reuse the existing Smart SQL Parameters infrastructure** — typed editors + **persisted parameter history**
(`settings.dat`). Do not build a second one.

- **Procedure / function / package routine** — input parameters, typed editors, **defaults and descriptions
  from metadata** (`ObjectMetadata`/`ColumnSpec` carry them since Package 5).
- **Trigger** — an **action selector** (only declared actions), then `NEW`/`OLD` grids enforcing §8.1
  (unavailable = absent, not greyed noise).
  - **"Seed from a real row…"** — pick an existing row to populate `OLD`/`NEW`, reusing the table data
    browser. Typing 40 columns by hand is the worst part of trigger debugging today.
- **Named parameter sets** — "last used" (auto) + saved presets per routine, in the existing history store.
  **Restart reuses the last values without re-prompting**, panel still editable.

### 9.4 The Variables window

One unified tree, **grouped and visually distinguished**:

```
▸ Parameters
    ⬤ IN   P_ID_POZ        INTEGER        4711
    ◑ OUT  O_RESULT        VARCHAR(20)    <null>
▸ Locals
    ○      V_ID_KARTOTEKA  INTEGER        1203        ← changed this step
    ○      V_ILOSC         NUMERIC(18,4)  12.5000
▸ Context            (triggers / handlers only)
    ◆ NEW.STATUS / ◆ OLD.STATUS / INSERTING = true / GDSCODE
▸ Cursors
```

Icons via the existing `IconResourceKey` + `IconBrushConverter` (VM holds the **key string**, never a brush —
rule #1 + theme rules). New colours are tokens in **both** dictionaries.

Beyond IBExpert:
- **Change highlighting** — a variable changed by the last step is highlighted. Cheap (we own the frame) and
  the highest value-per-line feature in any variable window.
- **Inline editing** — set a variable mid-session. *Trivial* here: the frame is client-side truth.
  (Validation: a value violating a domain `CHECK` will fail on the next injection — validate at edit time.)
- **Pin to top** — for variables. **Pins do not replace Watches** (§9.5).
- **Type shown**, from `VariableSymbol`/`ParameterSymbol`.
- **`<null>` rendered distinctly**, themed.
- **BLOBs/arrays lazily** — never eagerly; "…" → the existing value viewer.
- **Type-to-filter**, mirroring the sidebar.

**Data tips.** Hovering a variable shows its **current value**, via a `DebugValue` section on the existing
`HoverInfoEngine` — already an *ordered aggregate* (`HoverInfo` = Span/Diagnostics/Info) built for exactly this
extension, so the debugger cannot drift from Quick Info. **The best variable-inspection UX there is**: you
inspect where you are looking. Extended: **hovering a *selected expression* evaluates it** (§9.5).

Free: **Related Elements** already highlights the caret symbol's references while stopped (Stage 8 M1).

### 9.5 Expression evaluation — one mechanism, three surfaces (decision 6)

v1 deleted Watch and claimed pinning replaced it. **That was wrong: a watch is an *expression*, not a
variable** (`a + b`, `SELECT COUNT(*) FROM T WHERE ID = :v`). And v1 omitted Evaluate/Immediate entirely — the
cheapest high-value feature in the design, since it is *literally the harness with a user-supplied fragment*.

> **One engine (`HarnessBuilder` + the current frame), three surfaces:**
> - **Evaluate** — evaluate the selection / hovered expression, inline (`Shift+F9`).
> - **Watches** — expressions re-evaluated after every step, in a panel. Persisted per routine.
> - **Immediate window** — type any expression *or statement* against the live frame.

All three are the same call. This is also why **D5 lands early**: the Immediate window is the best possible
test instrument for D2's harness, and it delivers user value on day one.

Guards (§F): a watch/immediate fragment **runs real SQL in the debug transaction** — it can have side
effects. Watches are re-evaluated automatically, so **an automatic watch must be flagged when it is not a
pure expression**, and the Executed SQL panel (§10.3) records every evaluation like any other step.

### 9.6 The editor surface

- **Breakpoint gutter margin** — click to toggle; `F9`.
- **Current-line marker** + **call-site marker** for caller frames — new `IBackgroundRenderer`s, mirroring
  `SquiggleRenderer` / `RelatedElementsRenderer`.
- **Breakpoints land on `IExecutableStatement` nodes** — they snap to a real step unit; never a blank line or
  comment. This is Etap 6.9 B0's extension point being consumed as intended.
- **Set Next Statement** — move the instruction pointer (drag the marker / `Ctrl+Shift+F10`). **Trivial here**
  because control flow is client-side, and powerful. Guard: it cannot un-execute side effects already
  performed (§12 — offer D14's step-back instead when available).
- Read-only source. Editing is a different activity (§9.1).

### 9.7 Keyboard (VS-standard — do not invent)

`F5` Continue · `Shift+F5` Stop · `Ctrl+Shift+F5` Restart · `F9` Toggle breakpoint · `F10` Step Over ·
`F11` Step Into · `Shift+F11` Step Out · `Ctrl+F10` Run To Cursor · `Shift+F9` Evaluate ·
`Ctrl+Shift+F10` Set Next Statement · `Ctrl+Alt+Up/Down` Walk frames.

⚠ `F5` is EmberTern's **Execute** in the SQL editor. In the debug tab it must mean Continue (universal
debugger reflex). Tab-scoped binding; flagged as the one place the debugger contradicts an app-wide reflex.

### 9.8 Stepping modes

Given (all kept): Continue, Pause, Stop, Restart, Step Into/Over/Out, Run To Cursor.

⚠ **Pause needs an honest definition.** With client-side control flow there is nothing to interrupt *between*
steps — but a single step can be a long server statement, and that one is **not interruptible** except by
cancelling it. So: Pause = "stop before the next step", immediate while stepping; during a long statement it
**cancels the statement**. Define it; don't let it look broken.

**Additions**, in value order:

1. **Break on exception** — stop where an exception is raised, frame intact. We see every statement's outcome
   (§3.6), so it is cheap. High value.
2. **Conditional breakpoints + hit counts** — the condition is just another expression → §9.5's engine. Nearly
   free here; expensive in most debuggers.
3. **Run to next `SUSPEND`** — Firebird-specific: for a selectable procedure this is **"give me the next
   row."** IBExpert lacks it. (Where the rows *go*: a result grid on the debug tab, reusing the existing grid
   — v1 never said.)
4. **Data breakpoint** — break when a variable changes. We already diff the frame for change highlighting.
5. **Step back via savepoints** — *(D14, opt-in.)* One savepoint per **step** (vs §4.5's per **frame**), so
   stepping back = `ROLLBACK TO` + restore the client frame snapshot. Real reverse debugging of DB effects,
   which IBExpert cannot do. Honest limits: bounded history (memory), auto-disabled inside loops/fast-forward,
   and it **cannot** undo `IN AUTONOMOUS TRANSACTION` (§4.6), generator increments (§4.6),
   `EXECUTE STATEMENT ON EXTERNAL`, or side-effecting UDFs.

**Rejected: general time-travel/replay** — cannot undo external side effects. §9.8.5 is the sound subset.

---

## 10. Panels

| IBExpert panel | Verdict |
|---|---|
| **Watch** | **KEPT — restored in v2** (decision 6). v1 deleted it in favour of pinning; that conflated *variables* with *expressions*. Watches are expression watches, on §9.5's engine. |
| **Last Statement** | **Kept, reframed, promoted → "Executed SQL" (§10.3).** The audit log, and the trust anchor of a simulator. |
| **Breakpoints** | **Kept, low-profile.** Useful past ~5 breakpoints. Build by **reusing the Diagnostics panel *pattern*** — virtualizing `ListBox` + row VM + activate-to-navigate. A breakpoint list is structurally a list of spans you navigate to. Reuse the pattern, not the panel. |
| **Messages / SQL Messages** | **Removed as debugger-specific.** EmberTern already has Messages + Output. Two message panels is drift. |
| **Statistics / Plan** | **Removed.** Replaced by the existing **Performance Analysis** (§10.5). |

Final panel set: **Variables** (pins) · **Watches** · **Call Stack** · **Breakpoints** · **Executed SQL**
· **Immediate** · (+ a **result grid** when the debugged routine is selectable, §9.8.3).

### 10.3 Executed SQL — why a simulator must be auditable

The run is a reconstruction (§1), so the user must be able to answer *"what did EmberTern actually send?"* —
otherwise every surprising result is unfalsifiable. Shows each generated harness in order, with parameters
and results. **It is what makes §F checkable rather than a promise.** Reuse the existing **Output** panel
infrastructure.

### 10.5 Statistics → Performance Analysis (and, later, the FB5 profiler)

Debugging answers *"why is it wrong"*; profiling answers *"why is it slow."* EmberTern already has the second
(measured-first, 6-rule advisor).

**Future, separate feature — explicitly not this stage:** `RDB$PROFILER` is verified available on FB5 from a
plain client (§15 Q9), recording **per-line/column** stats — i.e. a **line-level heat map from a *real*
execution**, strictly better than IBExpert's Statistics/Plan, consuming the same AST/editor infrastructure.
It belongs to the Performance milestone. **Do not scope-creep the debugger into it.**

---

## 11. Reuse map

The rule: **extend › reuse › share › (last resort) create.**

| Existing | Used for | Notes |
|---|---|---|
| **AST** + `IExecutableStatement` | Step units, breakpoint placement | Built in Etap 6.9 **for this**. First consumer. |
| `ForSelectStatement.Query`, `DeclareCursorStatement.Query` | Cursor Bridge (§7) | B3.1 gives exact cursor SQL spans. |
| `BlockStatement` / `If` / `While`, sub-routine bodies | Control flow, local-routine frames | B1/B5. |
| **`WhenHandler` (P1 — to build)** | Exception control flow (§3.6) | **Prerequisite**, not existing. |
| **SemanticModel** — `Scope`, `Symbol`, `SymbolReference` | Variables window, read/write sets (§3.5), NEW/OLD + error-context substitution **by span** | `RecordAliasSymbol`, `TriggerPredicateSymbol`, `VariableSymbol`, `ParameterSymbol` exist. |
| **`DiagnosticsEngine`** | Pre-flight (§9.2) | As-is; the debugger adds no analysis. |
| **`HoverInfoEngine`** | **Data tips**, hover-evaluate (§9.4/§9.5) | Add a `DebugValue` section to the ordered aggregate. |
| **Related Elements** (Stage 8 M1) | Reference highlight while stopped | Free. |
| **`NavigationController.JumpTo`** / Peek | Call stack + breadcrumbs, Peek Frame | No second navigation mechanism. |
| **Smart SQL Parameters** + history | Launch panel (§9.3) | Typed editors + `settings.dat` persistence solved. |
| **Diagnostics panel pattern** | Breakpoints panel | Pattern, not the panel. |
| **Output / Messages** | Executed SQL, errors | No debugger-specific message panels. |
| **`FirebirdDdlReader`** (+ `ParseServerMajor`) | Frame source fetch; base types for §3.4/R2; version gate (P2) | Existing; already the one version-parsing site. |
| Grid / value viewer | BLOB/array inspection; selectable-routine rows (§9.8.3) | Existing. |
| `SquiggleRenderer` / `RelatedElementsRenderer` pattern | Current-line, call-site, breakpoint renderers | Same `IBackgroundRenderer` shape. |
| **Breadcrumbs** (backlog, unbuilt) | §5.2 | The debugger is its forcing function — build it *as* the shared feature. |
| Theme tokens | All new colours | **Both** dictionaries. No hardcoded colours. |

**Built new (irreducibly):** the interpreter (`DebugSession`/`Frame`/`StepPlanner`/`ExceptionRouter`),
`HarnessBuilder`, `IDebugExecutor` + `FirebirdDebugExecutor`, `DebugSessionConnection`, `CursorBridge`, and the
debug tab's panels/VMs.

### 11.1 Editor-wiring consolidation (gotcha #219) — prerequisite to the *UI*, not to the project

Editor capabilities are wired in **two** places: `SqlEditorBehavior.Attach` (object editors) and `MainWindow`
(the main SQL editor). That already shipped a real defect (S3: no squiggles in the main editor). The debug tab
is a **third** host bringing *several* new capabilities at once (breakpoint margin, current-line, call-site,
data tips) — four chances to silently miss a surface, in the exact pattern that produced S3.

It must also first solve the known lifecycle mismatch (MainWindow's editor exists *before* its VM; object
editors attach *after*; MainWindow bypasses `subscribeMetadataChanged` because it latched against a null VM) —
i.e. "subscribe once the VM arrives". A prerequisite, not a cleanup.

**v2 moves it from D0 to D3** — see §13.

---

## 12. Fidelity boundaries (§F)

Each is **named, detected where possible, and surfaced**. None is silently approximated.

1. **Step-into is simulation; step-over is real** (§5.3). Permanent, quiet frame indicator.
2. **`IN AUTONOMOUS TRANSACTION`.** Stepping *into* one is **not faithful** — our per-statement harness would
   run its statements in the debug transaction (the *wrong* one) and they would be rolled back. **Resolution:
   execute the whole autonomous block atomically as one step** (harness carries the construct verbatim;
   Firebird honours it) and explain why it cannot be entered. Plus §4.6's permanence warning.
3. **`EXECUTE STATEMENT '<sql>'`.** A runtime string: *executable* verbatim and faithfully, never
   *steppable* — there is no source until runtime. The AST already treats this as a permanent boundary
   (`editor-ast-deepening.md` §12). Physics, not debt.
4. **Isolation.** The debug transaction cannot see the SQL Editor's uncommitted data and may conflict with it
   (§4.3). A routine normally called under SNAPSHOT behaves differently under READ COMMITTED ⇒ **isolation is
   user-selectable at launch** (§4.2).
5. **The clock (decision 4).** **Measured:** `CURRENT_TIMESTAMP` is **frozen for a whole PSQL request**
   (`STABLE`) but **advances between statements** in one transaction (`.8480` → `.8840`). A real procedure is
   one request = one clock; a debug session is N statements = N clocks, and a human steps in **seconds**. A
   routine writing `CURRENT_TIMESTAMP` into rows in a loop gets identical stamps in production and **different
   ones** under the debugger. **Not emulated** — a rewrite could only pin *our* fragments, never a called
   routine's real BLR, trading an honest boundary for a hidden, inconsistent one. Applies to `CURRENT_TIME`,
   `'NOW'`. (`CURRENT_TRANSACTION`/`CURRENT_CONNECTION` are stable — one session connection, one transaction.)
6. **Generators (decision 4).** `GEN_ID`/`NEXT VALUE FOR` are **outside transaction control**: the rollback does
   not restore them, and every Restart consumes more. Detected by the pre-flight scan (§9.2).
7. **Selectable-procedure step-into (decision 9).** `FOR SELECT … FROM B(…)` where the user steps into `B`:
   `SUSPEND` makes `B` a **coroutine**, requiring generator frames — and the Cursor Bridge would have executed
   `B` for real. **Deferred: for now, stepping into a selectable procedure from a cursor query is refused with
   an explanation; step-over is fully faithful.** Revisit after the base architecture lands.
8. **Type round-trip (FB4+).** Every injected/returned value crosses .NET each step. `DECFLOAT(34)` and
   `INT128` have no native .NET equivalent (driver exposes `FbDecFloat`); `TIME ZONE` types and **arrays** are
   thin. **Rule: never convert — carry driver-native types end-to-end.** Arrays: boundary until proven.
9. **Performance.** Every step is a prepare+execute round trip (~ms locally). A 1M-iteration loop is not
   steppable in human time. Mitigations: §3.5's read/write set (the *big* one — otherwise the whole live frame
   ships both ways every step), prepared-statement caching keyed by (step node, frame shape), Run-To-Cursor /
   conditional breakpoints, and **Fast-forward (D13)**. **A debug run is orders of magnitude slower than real
   execution.** IBExpert has the identical constraint and doesn't say so.
10. **Domain `CHECK` on injection** (§3.4/R2). Base-typed harness parameters skip domain validation *on
    injection*; the user's own assignments still validate (R3). Accepted trade (decision 1).
11. **FB3/FB4 closure semantics unverified** (§6.3, §1.4) — a gate on D9, not an assumption.
12. **Unparseable source.** If a routine's source does not yield step points (the `RawStatement`/§0 valve),
    the debugger **refuses to start, with the reason** — it never debugs a partial understanding.

### 12.1 Fast-forward (D13) — the optimisation and its price

If a region has **no breakpoints** and nothing to observe, compile the *whole region* into one `EXECUTE BLOCK`
and run it server-side. Sound because it is the *same* harness trick already proven for one statement (§3.2),
just a bigger span. Turns a 1M-iteration loop from impossible into instant. **It also happens to restore
request-scoped fidelity within the fused region** (one request ⇒ one clock, native atomicity) — an argument for
it beyond speed.

Its price: error line mapping degrades to block-relative positions (`At block line: 3, col: 3` — observed,
§15), and `SUSPEND` inside a fused region needs care. **Opt-in, later, and it must degrade to per-statement
stepping the moment a breakpoint lands inside.** It is an optimisation over a *trusted* interpreter — never
earlier.

---

## 13. Milestones

Project contract: **one milestone per session**, each complete + tested + smoke-verified + polished before the
next. Foundation-first; never big-bang.

> **v2 reorders v1 — risk first.** v1 opened with D0 (editor-wiring consolidation): a broad refactor with
> **zero debugger value**, executed *before* the two things that can kill the design were validated. **D1 and
> D2 are pure (Core + Firebird, no UI) and need no wiring at all.** So: prove the engine, then refactor the
> wiring while it is being validated, then build UI on top.

| # | Milestone | Scope | Why here |
|---|---|---|---|
| **P1** | **AST — exception handlers** *(prerequisite)* | `WhenHandler` node + `BlockStatement.Handlers`; parser producer → binder consumer (Etap 6.9 contract). | **Blocks D1.** Handlers are currently an unstructured `PsqlLeafKind.Other` token bag — the interpreter has nothing to read (§3.6). |
| **P2** | **Server version gate (FB3+)** *(app-wide, small)* | Verify `ServerVersion` on connect; reject < FB3 with a clear message. Reuse `ParseServerMajor`. | Decision 8. **Outside the debugger's scope** — flagged in §1.3. Free: FB2.5 is already unreachable. |
| **D1** | **Debug engine core** *(pure Core, no server)* | `DebugSession`, `Frame`, `FrameValues`, scope chain, `StepPlanner`, **`ExceptionRouter` + the frame-savepoint model**, `BreakpointSet`, `IDebugExecutor` contract. Headless tests with a fake executor. | **Control flow is the part we own ⇒ the part we can get wrong.** Prove it with zero server in the loop. |
| **D2** | **Harness + session connection + executor** *(Firebird)* | `HarnessBuilder` incl. **§3.4 declaration rules** + **§3.5 read/write sets**; `FirebirdDebugExecutor`; `DebugSessionConnection` + TPB (§4.2) + **frame savepoints**; assignments, `IF`/`WHILE`, DML leaves. Verified against the lab. | The server contract. §15 already de-risked the mechanism. |
| **D3** | **Editor-wiring consolidation** | §11.1 — one seam; solve "subscribe once the VM arrives". | **Moved from v1's D0.** Now it lands immediately before the first UI, where it actually pays. |
| **D4** | **Debugger tab MVP** | Tab shell, launch panel + parameters (reuse), breakpoint gutter, current-line, Continue/Stop/Restart/Step, basic variables. **Standalone procedures only.** | First real user value. Everything after is depth. |
| **D5** | **Expression evaluation surface** | Evaluate + Watches + Immediate on **one** engine (§9.5). | **Early on purpose:** the best test instrument for D2's harness, and immediate user value. |
| **D6 ✅** | **Cursor Bridge** | `FOR SELECT` incremental stepping via a real DSQL cursor (§7); per-wire-op locking; colon-only injection (§15.5). **DONE** (nested cursors + fidelity proven; `WHERE CURRENT OF`/`DECLARE CURSOR` explicit cursors are follow-ups). | `FOR SELECT` is in nearly every real procedure — D4 isn't truly usable without it. Before the flashier work. |
| **D7** | **Variables window, full** | Grouping/icons, change highlight, inline edit + validation, pins, types, `<null>`, lazy BLOBs, filter, **data tips**. | The most important panel, once there is state worth showing. |
| **D8** | **Call stack + nested stored routines** | Frames, stack panel, **Breadcrumbs** (shared feature), Peek Frame, frame keyboard nav, simulated-frame indicator. | Nesting needs a working single frame first. |
| **D9** | **Local procedures & functions** 🏁 | Sub-routine frames (§6.2a) + closure harness (§6.2b) + read/write sets + **R5**. **Run §6.3's FB3/FB4 probes first.** | **The flagship.** Falls out of D1+D2+D8 — the design's central claim. |
| **D10** | **Triggers** | Action selector, NEW/OLD editor + availability rules, span-based substitution, seed-from-row. | Independent surface; needs D4+D7. |
| **D11** | **Packages** | Public + private routines (§8.2). **Extend the lab with a private routine first.** | Smallest remaining surface. |
| **D12** | **Advanced breakpoints** | Break on exception, conditional + hit counts, data breakpoints, **run to next `SUSPEND`** (+ its result grid). | Cheap *given* the engine; pure additions. |
| **D13** | **Fast-forward** *(optional)* | Block fusion (§12.1), prepared-statement caching. | An optimisation over a *trusted* interpreter. |
| **D14** | **Step back via savepoints** *(optional)* | Bounded reverse stepping (§9.8.5), honest limits. | Distinctive; strictly additive; last. |

**Definition of done** = the project's existing bar, plus one addition forced by §2.1:

> **Every milestone must be verified against `Lab/EmberTern_Lab.fdb` by comparing simulated results to REAL
> execution of the same routine.** A green unit test proves the interpreter self-consistent; only the lab
> proves it **faithful**. Extend `Lab/setup.sql` with the debugging zoo — nested calls, local routines
> (closures), cursors, exception handlers, an autonomous-transaction routine, a generator user, a private
> package routine, domain-typed `NOT NULL` variables — rather than creating throwaway databases.

---

## 14. Open items

**Probes that block a milestone (measure — never infer):**
- **§6.3 / §1.4** — sub-routine outer-variable capture on **FB3 / FB4** (probes Q2/Q3/Q4). **Blocks D9.**
- **§8.2** — private package routine callable from `EXECUTE BLOCK`? Source extractable from the body blob?
  **Blocks D11.**
- ~~**§7** — `WHERE CURRENT OF` on a named DSQL cursor.~~ **RESOLVED (§15.5 [12]):** unsupported cross-context
  (SQL -504) — a §F boundary, surfaced as an honest step error; not in D6's DoD.
- ~~**§1.4** — cursor interleaving verified on FB5; confirm on **FB3/FB4**.~~ **RESOLVED (§15.5 [11]):** FB3 +
  FB5 verified; FB4 unverified (no instance).
- **§12.8** — `DECFLOAT` / `INT128` / `TIME ZONE` / array round-trip fidelity through the driver. **Blocks
  FB4+ support of D2.**

**Design questions still open for review:**
1. **`F5` = Continue inside the debug tab** (§9.7), against the app-wide `F5` = Execute.
2. **`IDebugExecutor` vs Architecture rule #2** (§3.7) — the `ISqlMetadataProvider` precedent. Accept?
3. **Explicit `Commit debug transaction`** (§4.4) — consistent with rule #3, or a foot-gun to omit?
4. **Peek Frame instead of split view** (§5.2).
5. **Scope: DB-level and DDL triggers excluded** (§8.1).
6. **D13/D14 optional** — build only if real usage asks.
7. **Session count** — how many concurrent debug sessions to allow (§4.1 supports N; server attachment limits
   argue for a sane cap + a clear message).

---

## 15. Verification log — probes against the live engine

Engine: **Firebird 5.0.3**, `localhost:3050`, against a **copy** of `Lab/EmberTern_Lab.fdb` at an ASCII temp
path (gotcha #149 — `isql` cannot reach the repo's non-ASCII path; **the repo lab was never touched**).
Driver probes: `FirebirdSql.Data.FirebirdClient` 10.3.4 in a throwaway console app.

### 15.1 Feasibility (v1)

| # | Question | Result | Consequence |
|---|---|---|---|
| — | Debug-related catalog columns? | Only `RDB$DEBUG_INFO` on procedures/functions/triggers | BLR→source map. No protocol. §1 |
| — | Debug/trace/monitor system tables? | `MON$CALL_STACK` (+ `MON$*`); no debug tables | Observation only. §1 |
| — | `RDB$PROFILER` present? | Yes — `START/PAUSE/RESUME/FINISH/CANCEL_SESSION`, `FLUSH` | Profiler ≠ debugger. §10.5 |
| Q1 | Sub-**function** in `EXECUTE BLOCK`, no metadata? | **42** | **No temporary packages needed.** §6 |
| Q2 | Sub-routine **reads** outer variable? | **5** | Closures exist (contradicted prior). §6.1 |
| Q3 | Sees **mutated** outer value? | **5 → 99** | Capture is **by reference**. §6.1 |
| Q4 | Sub-routine **writes** outer variable? | **77** | Harness must write captures back. §6.2 |
| Q5 | **The crux** — inject values + verbatim sub-routine + expression + write-back | **`EXPR_VALUE=36`, `OUT_A=6`** | **The central mechanism works**, incl. side effect + evaluation order — computed by *Firebird*. §3.2 |
| Q7 | **Selectable** sub-procedure via `FOR SELECT` in `EXECUTE BLOCK`? | **10** | Selectable local procs reachable. §6 |
| Q8 | `IN AUTONOMOUS TRANSACTION` survives outer `ROLLBACK`? | **autonomous row survived; outer row did not** | **⚠ "Nothing is persisted" is false.** §4.6 |
| Q9 | `RDB$PROFILER.START_SESSION` from a plain client? | **session=1** | No plugin install needed. §10.5 |
| — | PSQL error position reporting | `At block line: 3, col: 3` | Block-relative ⇒ fused-region cost. §12.1 |

### 15.2 Architecture review (v2) — the four falsifications

| # | Question | Result | Consequence |
|---|---|---|---|
| **A** | Does a `BEGIN…WHEN…END` block have an **implicit savepoint** (are prior statements undone)? | **`ROWS_SURVIVED=1`, `HANDLER_RAN=yes`** → **NOT undone** | Savepoints go on **frames**, not blocks. §4.5 |
| **C** | Does an **unhandled** exception out of a called procedure undo **its** DML? | **`SURVIVED=0`, `CAUGHT=yes`** → **undone** | **Call atomicity is real.** v1 diverged silently ⇒ frame savepoints. §4.5 |
| **B** | Is `CURRENT_TIMESTAMP` stable across statements **inside one PSQL request**? | **`STABLE`** (T1 == T2 despite burning real time) | Clock is request-scoped. §12.5 |
| **B2** | …and across **separate statements in one transaction**? | **advances** (`.8480` → `.8840`) | **⇒ the debugger's clock diverges.** Boundary (decision 4). §12.5 |
| **F1** | Real execution: uninitialized `NOT NULL`+`CHECK` **domain** variable, never assigned | **OK** | Baseline. |
| **F2** | Harness injection of the same variable (`V = P_V` with `P_V = NULL`) | **`validation error for variable V, value "*** null ***"`** | **⚠ v1 would crash on the first real procedure.** ⇒ §3.4 rules R1–R4. |

### 15.3 Driver probes (`FirebirdClient` 10.3.4) — Cursor Bridge & savepoints

| # | Question | Result | Consequence |
|---|---|---|---|
| **[1]** | Hold a cursor (reader) open mid-iteration | **row 1 fetched, reader open** | Baseline. |
| **[2]** | Run a **harness statement** on the same cn+tx **while the cursor is open** | **SUCCESS → 2** | **Cursor Bridge is feasible.** Gotcha #31 is a *concurrency* limit, not multiplexing. §7 |
| **[3]** | Resume fetching the original cursor afterwards | **SUCCESS — next row (id=2)** | Interleaving is safe. §7 |
| **[4]** | Two cursors open simultaneously | **SUCCESS** | **Nested `FOR SELECT` is possible.** §7 |
| **[5]** | `SAVEPOINT` + `ROLLBACK TO SAVEPOINT` through the driver | **rows after rollback-to = 0** | **Frame atomicity is implementable.** §4.5 |

*(Incidental: `PKG_ORDERS`' routines are all `RDB$PRIVATE_FLAG = 0` — the lab has no private package routine,
hence §8.2's open probe.)*

### 15.4 D2 seam (c) — executor fidelity (simulated vs real, FB5 lab)

The mandated §2.1 proof: the real `FirebirdDebugExecutor` drove `DebugSession` through three lab procedures
step-by-step and the result was compared to **real execution** of the same routine. All identical.

| # | Question | Result | Consequence |
|---|---|---|---|
| **[6]** | `FbException` identity fields (user `EXCEPTION` / `NOT NULL` domain validation) | user exc → `isc_except` (335544517) present, **name on the message's first line**; validation → SQLSTATE 42000 / GDS 335544879 | The `DebugErrorMapper` mapping (§3.6). |
| **[7]** | `SP_DBG_SUMMARY` — assignment + **domain `NOT NULL` local** + IF/ELSE + SUSPEND | sim `(120,BIG)`/`(10,SMALL)` == real | R1/R2/R3 hold; a domain-`NOT NULL` uninitialized local **does not crash** (the DoD case). |
| **[8]** | `SP_DBG_GUARD` — `EXCEPTION` + `WHEN EXCEPTION … DO` | sim `OK`/`CAUGHT` == real | Exception routing through the **real** `FbException` → `DebugError` → `ExceptionRouter`. |
| **[9]** | `SP_ADD_ORDER(1,…)` — `SELECT … INTO`, IF, DML `INSERT` (+ trigger), SUSPEND | inserted order matches; **session rollback undoes it** | DML leaves + savepoint/tx rollback. |
| **[10]** | `SP_ADD_ORDER(999,…)` — unhandled `EXCEPTION E_CUSTOMER_NOT_FOUND` | `Faulted`, name resolved, **root frame rolled back**, no row | Unhandled-exit frame savepoint rollback (§4.5). |

**Finding (drove a design decision):** a reused `SELECT … INTO` statement surfaces **no** local references
from the binder (the query binder records its FROM/columns, not the `:`-colon refs in the `WHERE` nor the
`INTO` targets — a token-walked `INSERT`/assignment/`IF` surfaces its refs correctly). Its precise read/write
set is therefore empty, which would drop the `INTO` write-back. **Resolution:** the executor falls back to
§3.5's named "inject all in-scope" primitive (`InScopeLocals` — correct, chattier) when the model surfaces
nothing, never a wrong narrow set (gotcha #238). Precise narrowing stays in force for every statement whose
refs the binder does surface. (A future binder deepening that surfaces reused-`SELECT`/`INTO` refs would let
the fallback stop firing — pinned by `ReadWriteSetAnalyzerTests.SelectInto_SurfacesNoLocalRefs_*`.)

### 15.5 D6 — Cursor Bridge (driver probes + fidelity, FB3 @ 4050 + FB5 @ 3050)

Probes run before implementing D6 (managed driver, `FirebirdClient` 10.3.4). **FB4 unavailable** (only FB3.0 +
FB5.0 installed) — recorded unverified, same posture as P2's FB2.5.

| # | Question | Result | Consequence |
|---|---|---|---|
| **[11]** | Cursor interleaving on **FB3** (harness stmt while a cursor is open; resume; two cursors at once) | **all SUCCESS** — mirrors FB5 §15.3 [1]–[4] | **Cursor Bridge feasible on FB3 and FB5.** FB4 unverified (no instance). |
| **[12]** | `WHERE CURRENT OF <name>` on a separately-opened DSQL cursor | **fails** — SQL -504 "Cursor … not found in the current context"; `FbCommand.CursorName` not settable | Positioned DML on a bridged cursor is **not** supportable cross-context — a §F boundary (§7). Not in D6's DoD; a body `WHERE CURRENT OF` surfaces as an honest step-level error. |
| **[13]** | Does the binder surface local refs for a `FOR SELECT` query? | **bare** refs yes (`role=Variable/Parameter`, in-query); **colon `:name`** no (a single `Parameter` token, #238) | Point B: bare refs *are* surfaced — but see the finding below. |

**Finding (drove the design — §F "verify, don't infer").** The first cut rewrote **every** frame reference the
binder surfaced (bare + colon) to a `?` parameter. Live fidelity caught it: a routine that both
`RETURNS (LINE_NO …)` **and** does `SELECT LINE_NO …` has the binder resolve the SELECT-list **column**
`LINE_NO` to the output **parameter** (locals shadow columns in its resolution order), so the column was
rewritten to `?` → `SELECT ?, …` → **SQL -804 "Data type unknown"**. **Resolution:** `CursorBridge` rewrites
**only the colon/`@` parameter form** (`:name`/`@name` — a `Parameter` token, Firebird's *unambiguous*
variable-reference syntax in a query, and a native DSQL bind once extracted). A **bare** identifier in a query
is a **column** in DSQL and is left verbatim — matching Firebird's own disambiguation. A bare local ref that
Firebird would resolve as a variable is rare, ambiguous, and surfaces as an honest step-level "column unknown"
if it cannot bind, never a silent wrong result (§F: correctness over reach). Gotcha #239.

**Fidelity (simulated vs real, FB5 lab — the mandated §2.1 proof).** The real `FirebirdDebugExecutor` + Cursor
Bridge drove `DebugSession` through the two new lab cursor procs; outputs compared to real execution.

| # | Question | Result | Consequence |
|---|---|---|---|
| **[14]** | `SP_DBG_CURSOR(1000)` — single `FOR SELECT` over `ORDER_ITEMS`, **fully stepped** (Step Into per row) | sim `(1,20,20),(2,25.5,45.5)` == real; 10 steps, `Completed` | Per-step real-cursor fetch; `:P_ORDER` bound; INTO targets land in the frame; running-sum body correct. |
| **[15]** | `SP_DBG_CURSOR(1001)` | sim `(1,20,20)` == real | Second parameter value; single-row cursor. |
| **[16]** | `SP_DBG_NESTED` — nested `FOR SELECT` (outer `ORDERS`, inner `ORDER_ITEMS WHERE ORDER_ID = :V_OID`) | sim `(1000,2),(1001,1)` == real | **Two cursors open simultaneously**; inner cursor injects the outer frame's local; DoD met. |

*(The `20` vs `20.00` display difference is numeric scale only — the values are equal.)*

### 15.6 D8 seam (b) — nested stored-routine step-into fidelity (simulated vs real, FB5 lab)

The real `FirebirdDebugExecutor` (with D8's `ResolveRoutine`) drove `DebugSession` **Step Into** through a
3-level lab chain; outputs + call depth compared to real execution (`tools/probes/DebuggerFidelityProbe`, the
mandated §2.1 proof). Lab zoo extended with `SP_DBG_LEAF` / `SP_DBG_MID` / `SP_DBG_ROOT` (`Lab/setup.sql`,
`.fdb` rebuilt).

| # | Question | Result | Consequence |
|---|---|---|---|
| **[17]** | **Is `:P` valid as the RHS of a PSQL assignment?** (`x = :y;` in an `EXECUTE BLOCK` body) | **SQL -104, "token unknown" at the `:`** | The colon form is a query-only syntax; the argument-seeding harness must rewrite `:name`/`@name` → **bare** name (by span, like the Cursor Bridge). Gotcha #242. |
| **[18]** | `SP_DBG_MID(5)` — Step Into `SP_DBG_LEAF`, argument seeding + `RETURNING_VALUES` write-back | depth **2**, chain `SP_DBG_MID → SP_DBG_LEAF`; real `Q = 12` | Step-into resolves a stored callee to a real frame; the arg (`:P`) is evaluated in the caller and seeds the callee input param; the callee's output binds back into the caller's local. |
| **[19]** | `SP_DBG_ROOT(5)` — the **A→B→C** DoD chain (`ROOT → MID → LEAF`), `SUSPEND RESULT` | depth **3**, chain `SP_DBG_ROOT → SP_DBG_MID → SP_DBG_LEAF`; **simulated `RESULT = 112` == real `112`** | Nested frames, argument seeding and `RETURNING_VALUES` write-back are faithful across three levels (`LEAF(5)=6`, `MID=12`, `ROOT=112`). D8's DoD met. |

*(Every resolved callee is a **stored** routine → a closed scope, `LexicalParent = null` (gotcha #241); a
package/qualified callee — D11 — and a local sub-routine — D9 — still step over in place, 100% faithful §5.3.)*
