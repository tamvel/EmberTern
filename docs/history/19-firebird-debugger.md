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
