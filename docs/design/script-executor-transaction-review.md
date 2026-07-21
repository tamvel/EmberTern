# Script Executor — Transaction & Connection Architecture Review

*Architecture review, 2026-07-16.*

> **Status.** §2.4 axis 1 (**wait policy**) **SHIPPED** 2026-07-16 as the *Script Executor Dev Mode
> integration* milestone, scoped by user decision to an **all-DDL script under auto-commit** — see
> CLAUDE.md's "Current state" and `FirebirdScriptExecutor.UsesDeveloperModeWaitPolicy`. Both
> conditions are load-bearing; the auto-commit one is now gotcha #230.
>
> §2.4 axis 2 (**transaction boundaries** — `Sequenced` mode, §5) is **NOT built**: the user
> explicitly kept the existing transaction model. So **gotcha #213 stands** — a mixed DDL+DML
> migration still cannot run. §5 remains a proposal, retained as the analysis of what fixing that
> would require.
>
> **Step 0 (§6) — RUN 2026-07-20 against the live FB5 lab. The design is now measurement-gated and
> stands.** The one load-bearing unmeasured claim, the §2.2(b) self-block, was measured: it is **real
> but selective** — table-scanning DDL (`CREATE INDEX`) self-blocks on the script's own uncommitted
> DML (WAIT-exhausted lock timeout, SQLSTATE 40001), but the review's stated example (`ALTER TABLE …
> ADD COLUMN`) does **not** (it is metadata-only on FB5). §2.2(b) is therefore **restated, not
> withdrawn**; the lane-split rejection is unchanged (decisive on §2.2(a) alone). #213 re-confirmed
> (PROBE 2); the Sequenced commit-boundary fix works (PROBE 3); independent DDL can share a segment
> (PROBE 2a). Full report: see §6 "Results" and §7.
>
> **Step 1 (doc truth pass) — DONE 2026-07-21** (comment-only; §6). The stale comments that caused
> the Data/Metadata-vs-Dev-Mode drift are fixed.
>
> **Step 3 (Sequenced Core) — DONE 2026-07-21** (§6). `ScriptTransactionMode.Sequenced` + the pure
> `ScriptSegmentPlanner` (segments over the AST classifier, per-segment TPB intent) land Core-only;
> Step 2 (Dev Mode text) was folded in and found already truthful.
>
> **Step 4 (Firebird layer) — STARTED 2026-07-21** (§6, split into seams A + B). **Seam A (per-segment
> TPB resolution) — DONE**: pure `ResolveSegmentTransactionOptions`, unit-pinned, no execution path.
> **The next actionable is Step 4 seam B (the Sequenced execution loop) — NOT started, gated on the
> user scheduling it.**

Scope: (1) should the Script Executor keep one transaction, or reintroduce automatic
metadata/data separation under Auto Commit; (2) are three connections still justified;
(3) what should Developer Mode's description say.

---

## 0. Verdict up front

| Question | Answer |
|---|---|
| Should the Script Executor reuse Developer Mode's **wait policy** for the DDL it runs? | **Yes.** It ignores it today, and that is a real gap. |
| Should it route DDL onto a **separate concurrent lane/transaction** while DML runs on another? | **No.** |
| Should it therefore keep a **single transaction**? | **Also no** — for a reason unrelated to Dev Mode (gotcha #213). |
| Is Developer Mode obsolete? | **No.** It is alive, correctly scoped, and correctly named. |
| Are three connections still architecturally justified? | **Yes.** Each owns a distinct, concurrent, mutually incompatible transaction lifetime. |
| Could we return to two? | **No** — every merge reintroduces something we deliberately deleted. |
| Is the Dev Mode description wrong? | **Incomplete, not wrong.** It never states its scope. |

**The proposal's goal is right; its mechanism is wrong; and the correct answer is neither of the two
options as posed.** "Reuse the Dev Mode infrastructure" is sound when read as *reuse the Dev
Mode-aware `WAIT` + bounded-timeout TPB for DDL the Script Executor runs* — that is real, existing,
well-scoped infrastructure, and extending it to deployment scripts is exactly right. What cannot be
reused is a *Dev Mode metadata transaction*: Dev Mode has never been a transaction or a lane, only a
**wait policy** (§1.2).

That distinction splits the question in two (§2.4). **Wait policy** → Dev Mode, answer *yes*.
**Transaction boundaries** → gotcha #213, answer *several, sequenced* — and Dev Mode has no opinion
on it. The two are coupled only in implementation: a transaction's wait policy is fixed at `BEGIN`,
so **a single transaction cannot honour Dev Mode for DDL without also making DML wait.** That, not
the lane question, is why "keep one transaction" fails.

Underneath both sits a defect that must be fixed regardless: **the Script Executor cannot currently
run a migration script at all** (§2.1).

---

## 1. What the code says about the brief's premises

The proposal is coherent, and one of its premises is simply true: **Developer Mode exists and its
architecture matters.** Two others need correcting — not because Dev Mode decayed, but because two
mechanisms that shipped weeks apart have merged in the retelling (§1.2). Getting them apart is what
makes the narrow question answerable.

### 1.1 "Auto Commit executes independent tasks one after another"

It does not. `ScriptTransactionMode` has exactly two members
([ScriptModels.cs:62](../../src/EmberTern.Core/Scripting/ScriptModels.cs#L62)):

```csharp
Manual,               // run everything, leave the tx OPEN, user commits/rolls back
AutoCommitOnSuccess,  // run everything, THEN commit if nothing failed, else roll back
```

`AutoCommitOnSuccess` is **one transaction for the whole script, committed once at the very end**
([FirebirdScriptExecutor.cs:132](../../src/EmberTern.Firebird/FirebirdScriptExecutor.cs#L132)).
The UI label is honest — *"Auto-commit on success"* — but there is **no per-task commit and no task
concept at all**. So the proposal's premise ("because Auto Commit executes independent tasks one
after another, each could pick its own lane") describes a mode that does not exist. Per-statement
commit boundaries would have to be **built**, not reused.

### 1.2 "The existing Dev Mode metadata transaction"

**Developer Mode is alive, is not obsolete, and is not in question.** But it has never been a
*transaction* or a *lane*. It is a **TPB policy** — a wait rule applied to the DDL path. That
distinction is the whole key to this review, and it is worth getting exactly right, because two
separate mechanisms have merged in the retelling:

| | **Data/Metadata routing** | **Developer Mode** |
|---|---|---|
| Commit | `7377968` *"automatyczny routing Data/Metadata (jeden F5)"* | `60b456a` *(Krok 2)* |
| What it did | SQL Editor sent DDL to a second attachment with its own hidden transaction | DDL runs `WAIT` + lock timeout instead of `NOWAIT` |
| Gated by Dev Mode? | **No — unconditional** | — |
| Scope | SQL Editor F5 | **Compile / structure DDL only** |
| Status | **Removed** (`3ce2a9e`) | **Alive, untouched** |

Both touched "metadata" and "transactions" in the same weeks, which is exactly why they blur
together. But they were never the same mechanism, and **Developer Mode never applied to the SQL
Editor at all** — from the day it was born. History 13, the milestone that created it:

> *Replaced the per-lane TPB profile pickers with a single `ConnectionProfile.DeveloperMode` …
> **Only the DDL path is affected*** — [and, in the same milestone's out-of-scope list] — *__Raw F5
> DDL in the SQL Editor stays NOWAIT (Developer Mode covers only the Compile/structure path).__*

So there was no Dev Mode behaviour in the SQL Editor to remove; what was removed from the editor was
the **routing**, a different and unconditional mechanism. This matters for the narrow question,
because it means **"one transaction vs. several" was never a Dev Mode question** — Dev Mode has only
ever answered *"how long should DDL wait for a lock?"*, never *"which transaction should DDL run
in?"*. Those are two orthogonal axes (§2.4).

The separate claim — a *metadata working transaction* to route to — is the part that genuinely no
longer exists. `MetadataLane` owns **no** transaction
([MetadataLane.cs:6](../../src/EmberTern.Firebird/MetadataLane.cs#L6)): *"That zombie abstraction is
gone."* That was the routing's transaction, not Dev Mode's.

### 1.3 What Developer Mode is today — and it is intact

([FirebirdDdlExecutor.cs:129](../../src/EmberTern.Firebird/FirebirdDdlExecutor.cs#L129))

```csharp
| FbTransactionBehavior.Wait,          // ← Wait in BOTH modes
WaitTimeout = TimeSpan.FromSeconds(developerMode ? 10 : 3);
```

**Its semantics are preserved, not diluted.** The 3 s in Standard mode is *not* "Standard now waits
for other sessions" — it exists solely to absorb the **~10 ms** transient metadata-cache release from
**our own** other lane (gotcha #214), a side effect of moving DDL to attachment #3. Read at the
intended granularity, the switch means exactly what it always meant, as `DeveloperModeTests`' own
docstring states:

> *Standard fails fast against another session, Developer waits longer.*

Exactly one place in `src/` reads the flag behaviourally — `FirebirdDdlExecutor.cs:115`. Everything
else is the field, the checkbox, the badge, and the strings. That is not evidence of decay; it is a
narrow, well-scoped policy lever with one consumer, which is what it was designed to be.

> **Finding — stale comment, `ConnectionProfile.cs:19`.** It still claims *"OFF (default): DDL runs
> NOWAIT — fail-fast on an in-use object"*. The *intent* is right (Standard = fail fast vs another
> session) but the *mechanism* is now false — Standard is a 3 s WAIT, not NOWAIT. Same for the
> `ConnectionRole` enum docstring
> ([FirebirdConnectionService.cs:15](../../src/EmberTern.Firebird/FirebirdConnectionService.cs#L15)),
> which still advertises Metadata *"carries … the metadata working transaction"* — contradicted by
> `MetadataLane`'s own docstring. Internal comments only, but they are where the two mechanisms blur.

---

## 2. The Script Executor question

### 2.1 The real bug the proposal is circling

The proposal *feels* right because it would accidentally fix a genuine, currently-shipping defect.
The executor's own docstring flags it
([FirebirdScriptExecutor.cs:23](../../src/EmberTern.Firebird/FirebirdScriptExecutor.cs#L23)):

> **KNOWN BROKEN** … a transaction CANNOT use an object it created but has not committed —
> `CREATE TABLE T …; INSERT INTO T …;` fails with `Table unknown (-204)` (gotcha #213).

**The Script Executor cannot run a migration script — the exact thing it exists for.** Every mode
runs the whole script in one transaction, so the second statement of any create-then-populate script
fails. The user's proposal would fix this as a side effect: if DDL went to an auto-committing lane,
the table would be committed before the INSERT.

That is the real insight in the brief, and it should be acted on. But the lane split is the wrong
mechanism for it.

### 2.2 Why the lane split is the wrong mechanism

**(a) It makes Rollback lie.** The DDL lane auto-commits. Split a script across it and manual
Rollback rolls back only the DML — schema changes stay applied. A deployment tool whose Rollback
leaves half the migration in place is worse than one that cannot run the migration at all: the
first silently corrupts, the second visibly fails. This directly violates the paramount rule
(#11, never lose/corrupt) more sharply than today's honest failure does.

**(b) It self-blocks — for table-scanning DDL.** Under the split, a script like:

```sql
INSERT INTO CUSTOMERS ...;      -- data → Data lane, transaction stays OPEN for the whole run
CREATE INDEX IX_CUSTOMERS ...;  -- metadata → DDL lane, autonomous, WAIT-bounded
```

has the DDL lane waiting on a lock held by **our own** still-open data transaction — which will not
settle until the script finishes. The script blocks on itself, resolved only by the 3 s/10 s timeout,
and then reports a lock error that names no other session (SQLSTATE 40001, `isc_lock_timeout`). The
irony: the split is proposed to *reduce* lock pain during deployment, but it introduces a lock
conflict **with yourself**. "Populate a table, then index it" is a completely ordinary migration
pattern, so this hazard is not exotic.

> ✅ **MEASURED (Step 0, 2026-07-20) — real but SELECTIVE; example corrected.** The self-block was
> reasoned, not measured — and, given #213/#214/#215 were all falsified inferences, the probe (§6)
> ran before freezing. Finding: **the mechanism is real for table-scanning DDL** — `CREATE INDEX`
> against a table with our own uncommitted `INSERT` waits out the full 10 s WAIT and fails with
> `isc_lock_timeout` (PROBE 1c). **But the review's original example was wrong**: `ALTER TABLE … ADD
> COLUMN` (PROBE 1) and `DROP COLUMN` (PROBE 1d) are metadata-only on FB5 and do **not** block
> (~7 ms). So (b) is **not** the decisive objection it was billed as — (a) is decisive on its own —
> but it is a confirmed hazard for a real class of DDL, so it stays as a corrected objection rather
> than being withdrawn. Note: the Sequenced design (§5) can never exhibit this, by construction — it
> never holds two transactions open at once.

**(c) It re-creates what was deliberately removed.** The brief says the editor's two-transaction
model caused "locking and consistency problems" and that removing it was intentional. Every one of
those problems returns here: two concurrent transactions with different snapshots, an ambiguous
Commit, and a script split across transaction boundaries the user cannot see. Gotcha #215's letter
is scoped to *"an interactive console"* — but its *reason* ("making Commit ambiguous and splitting
mixed scripts across two transactions") applies with **more** force to a deployment tool, where a
half-applied script is the worst possible outcome.

**(d) The classifier would be deciding the wrong thing.** #215's rule is precise and worth keeping
verbatim: a classifier *"may decide whether to REFRESH the UI; it must never decide WHERE/HOW a
statement executes."* The proposal has it decide **where**. The design in §5 has it decide **when to
commit** — a scheduling decision on one lane, which is a legitimate and much weaker use.

### 2.3 What actually serves the goal

The stated goal is *"reduce metadata locking when another user is connected during deployments."*
Two honest observations:

1. **A lane choice cannot reduce contention with another session.** If another session holds an
   object, your DDL waits or fails — on any attachment. Lanes only change who *else* you contend
   with (and, per (b), add yourself).
2. **What actually helps is exactly two things**, neither of which needs a second lane:
   - **Committing DDL promptly** → shorter lock hold → smaller window for others to collide. This is
     what a commit boundary gives you.
   - **WAIT with a bounded timeout** → survive a transient hold instead of failing instantly.

> **Finding — the goal is currently unserved, and Developer Mode is exactly the thing that should
> serve it.** The Script Executor runs on `TransactionService`
> ([FirebirdScriptExecutor.cs:96](../../src/EmberTern.Firebird/FirebirdScriptExecutor.cs#L96)),
> whose TPB is hard-coded:
> ```csharp
> private static TransactionProfile ResolveActiveProfile() => TransactionProfile.ReadCommitted; // → NOWAIT
> ```
> So **every DDL statement in a deployment script runs NOWAIT and ignores Developer Mode entirely.**
> Against an object another session is using, it fails *instantly* — today, in the tool whose whole
> job is deployment. Developer Mode's stated purpose is *"lets you modify objects that are in use by
> active sessions"*; a deployment script is the single most likely place to need that, and it is the
> one place the switch does not reach.

### 2.4 The two axes — and the direct answer to the narrow question

The question *"should the Script Executor reuse Dev Mode infrastructure when Auto Commit is on, or
keep a single transaction?"* reads as one question but is **two orthogonal ones**, and they have
different answers. Separating them is what §1.2 buys us:

| Axis | The question | Governed by | Answer for the Script Executor |
|---|---|---|---|
| **Wait policy** | *How long should DDL wait for a lock another session holds?* | **Developer Mode** | **YES — reuse it.** Today: NOWAIT, ignores the switch. |
| **Transaction boundaries** | *One transaction, or several?* | **gotcha #213** — nothing to do with Dev Mode | **Several, sequenced** — but not for any Dev Mode reason. |

**On axis 1 the answer is yes, and this is the real content of the proposal.** "Reuse the existing
Dev Mode infrastructure" — read as *reuse the Dev Mode-aware WAIT + bounded-timeout TPB
(`BuildDdlTransactionOptions`) for DDL the Script Executor runs* — is **correct, small, and directly
serves the deployment goal.** It needs no new concept, no lane routing, and no new setting; it
extends an existing, well-scoped policy to the one path that most needs it. The instinct behind the
brief is sound.

**On axis 2, Dev Mode has no opinion** — and this is where the proposal's mechanism goes wrong. The
reason to break the script into several transactions is #213 (a transaction cannot use an object it
created), which would be just as true with Developer Mode off. Dev Mode cannot answer it because it
was never that kind of thing.

**The two axes are not independent in implementation — and that is the crux.** A per-statement-kind
TPB is *meaningless* inside a single transaction: a transaction's wait policy is fixed at `BEGIN`.
So axis 1 **cannot** be delivered without axis 2:

- **One transaction** ⇒ one TPB for the whole script. Give it WAIT and the **DML waits too** — a
  deployment now blocks on ordinary row locks, which is worse than what we have. Give it NOWAIT and
  DDL keeps failing instantly. **There is no correct single choice.**
- **Concurrent lanes** (the brief's mechanism) ⇒ per-lane TPB, but at the cost of §2.2(a)–(d).
- **Sequenced segments** (§5) ⇒ **per-segment TPB**: DDL segments WAIT-bounded and Dev Mode-aware,
  DML segments NOWAIT. This is the only shape that delivers axis 1 *and* fixes #213 *and* never has
  two transactions open at once.

So the proposal's **goal is right and its mechanism is wrong**, and the fix is narrower than either
"split the lanes" or "keep one transaction": **sequence the transactions on one lane, and let each
segment carry the TPB its statement kind deserves — which is where Dev Mode plugs in.**

---

## 3. Connection architecture

### 3.1 Why three, and who owns each

| # | Role | Owner | Transaction lifetime | Why it cannot share |
|---|---|---|---|---|
| 1 | `Data` | `TransactionService` → SQL Editor F5, table-data edits, Execute Procedure, Script Executor | **THE** user working transaction: long-lived, NOWAIT, auto-begin, never auto-commit | Holds an open tx for minutes/hours |
| 2 | `Metadata` | `MetadataLane` → sidebar, DDL preview, completion warm, security, Session Manager | **None.** Implicit per-command tx | Must never block or be blocked |
| 3 | `Ddl` | `FirebirdDdlExecutor` → object-editor Compile, Recompile dependents, admin batch | Autonomous, auto-committed, **WAIT** + bounded timeout | Must be independent of #1 |

The forcing constraint is gotcha #89: **one `FbConnection` allows one transaction at a time.** These
three lifetimes **overlap in wall-clock time** and demand contradictory TPBs (long NOWAIT vs none vs
short WAIT). Three overlapping transactions ⇒ three attachments. This is forced by the driver, not
accumulated by accident.

### 3.2 Testing the brief's premise

> *"an additional connection was introduced to work around transaction conflicts … that happened
> before we simplified the SQL Editor transaction model."*

**Chronologically correct** — `7586a25` (*dedicated DDL attachment*) does precede `3ce2a9e` (*a classic
Firebird console — delete routing + the hidden transaction*). Both are in the same 2026-07-14 sprint.

**But the inference does not follow.** The DDL lane was not introduced to work around the *editor's
routing*. It was introduced to fix a scenario the simplification did not touch
([history 15, Part 2](../history/15-ux-stabilization-sprint-and-console-refactor.md#L78)):

> *run a SELECT in the SQL Editor, leave the transaction open, edit a trigger, press Compile → [fails]*

That scenario is **unchanged today**: the user still holds an open working transaction on lane #1,
and Compile still must not depend on them settling it. The lane's justification never rested on
routing, so removing routing did not invalidate it.

### 3.3 Every merge is a regression

- **Ddl → Data:** reintroduces the deleted *"Commit or roll back the active transaction before
  running DDL"* guard (#89). This is the exact regression the DDL lane was built to remove, and it
  is still live in the code as the *degraded-mode* branch
  ([FirebirdDdlExecutor.cs:86](../../src/EmberTern.Firebird/FirebirdDdlExecutor.cs#L86)) — i.e. the
  two-connection world is already implemented, and is explicitly labelled the degraded one.
- **Ddl → Metadata:** breaks the metadata lane's core invariant. A WAIT-bounded DDL tx (up to 10 s)
  would hold the metadata command lock, freezing sidebar + completion behind a blocked Compile — and
  metadata reads would be forced to *join* the DDL transaction (#89), entangling catalog reads with
  uncommitted DDL.
- **Metadata → Data:** this *is* degraded mode (`MetadataIsIndependent == false`), already
  implemented as a **fallback**, and it entangles catalog reads with the user's working transaction.

**Conclusion: three is justified. Do not merge.**

### 3.4 The honest counterpoint

Three attachments per profile is a real cost, and the degraded-mode fallback (`MetadataIsIndependent`
/ `DdlIsIndependent`) is threaded through 8+ readers with repeated *"can flip mid-call"* comments
(gotchas #98/#120). **That** is where the accidental complexity actually lives — not in the lane
count. If simplification is wanted, the question worth asking is whether degraded mode earns its
branching, not whether lane #3 earns its socket. Out of scope here; noted for a future pass.

---

## 4. Developer Mode UX

### 4.1 The description's flaw is scope — as the brief said

Current
([UiStrings.cs:317](../../src/EmberTern.App/UiStrings.cs#L317)):

> *"Lets you modify procedures, functions, triggers and other objects that are in use by active
> sessions. DDL operations may wait for the object to be released instead of returning an error
> immediately."*

**Two retractions from the first draft of this review, both material:**

- I called this *"wrong about Firebird"* on the grounds that Standard also waits (3 s), so *"instead
  of returning an error immediately"* is false. **Withdrawn.** The 3 s exists only to absorb our own
  lane's ~10 ms cache release (§1.3); against *another session* — which is what the sentence is
  about — Standard does fail effectively immediately. The description is a fair simplification, not
  a falsehood.
- I questioned whether the switch still *"earns a name like Developer Mode"*, calling it an
  over-named dial. **Withdrawn.** That reading came from comparing 3 s to 10 s as if they were two
  points on one scale. They are not: 3 s means *"don't wait for other sessions"* and 10 s means
  *"do"*. The semantic distinction the name promises is intact and is exactly what the switch
  delivers.

**What remains — and it is what the brief identified — is scope.** The description says what Dev Mode
does but never says *where*, so nothing tells the user it covers Compile and not the SQL Editor. One
correction to the brief's framing, though: **the SQL Editor does not "ignore" Dev Mode; Dev Mode was
never in scope for it** (§1.2). The editor runs DDL in the user's NOWAIT working transaction because
it is a console — a consequence of the console decision, not an exception carved out of Dev Mode.
Describing it as "ignored" implies a rule with a hole in it; it is a rule with a stated boundary.

### 4.2 Recommended text

The change is **additive** — keep the existing sentences, which are good, and state the boundary:

```
Developer Mode
Lets you modify procedures, functions, triggers and other objects that are in use by active
sessions: Compile waits for the object to be released instead of returning an error immediately.
Applies to Compile and Recompile in the object editors. The SQL Editor is not affected — it runs
every statement in your working transaction, which never waits.
```

Badge tooltip:

```
Developer Mode is on — Compile waits for objects other sessions are using instead of failing immediately.
```

If §5 ships, the scope line becomes the payoff rather than a caveat:

```
Applies to Compile and Recompile in the object editors, and to DDL run by the Script Executor.
```

### 4.3 The real Dev Mode gap is coverage, not naming

The setting is well-named and well-scoped. Its actual weakness is that its **stated purpose and its
reach have drifted apart**: it promises *"lets you modify objects that are in use by active
sessions"*, and the place a developer most often meets an in-use object held by someone else is a
**deployment against a live database** — the one path it does not cover (§2.3).

So the recommendation is not to rename or delete it, but to **extend it to the Script Executor's DDL**
— which is precisely what the brief asked for, and is the correct half of the proposal (§2.4, axis 1).
That is also why this section cannot be decided independently of §5: extending Dev Mode to the Script
Executor is only *implementable* if the script's DDL has its own transaction segment to carry the TPB.

---

## 5. Proposed design — sequencing, not lane-splitting

**One lane. One transaction at a time. Commit boundaries between segments.**

This is what `isql`/IBExpert do with `SET AUTODDL ON`, and it is the only shape Firebird actually
permits for a mixed migration (#213 is physics: Firebird cannot both let a transaction use an object
it created *and* keep that object rollbackable).

### 5.1 Behaviour

A new **third** mode, sitting alongside the existing two (which are unchanged):

| Mode | Transactions | Semantics |
|---|---|---|
| `Manual` *(default, unchanged)* | one, left open | review, then Commit/Rollback. **All-or-nothing.** |
| `AutoCommitOnSuccess` *(unchanged)* | one, committed at end | all-or-nothing, hands-free |
| **`Sequenced`** *(new — "Deployment")* | **many, sequential, never concurrent** | commit boundary after each DDL segment |

In `Sequenced`:
- Statements run in order on the **Data lane only**. Never two transactions at once.
- The classifier (`SqlStatementClassifier`) decides **when to close a segment** — not where anything
  runs. `Schema` statement → run it, commit, start the next segment. Consecutive DDL may share one
  segment; the boundary is required only between a `Schema` statement and a later statement that
  might depend on it.
- **DDL segments use a WAIT-bounded TPB honouring Developer Mode** (3 s / 10 s); **DML segments stay
  NOWAIT ReadCommitted.** This is the §2.3 fix and the §4.3(c) reconnection, and it composes only
  because segments are sequential — a per-segment TPB is meaningless if lanes overlap.
- On failure: stop (or continue, per `StopOnError`), roll back the **current** segment, and report
  **exactly which segments already committed**. This is the honest cost, surfaced rather than hidden.

### 5.2 Why this is safe where the split is not

| Hazard | Lane split | Sequenced |
|---|---|---|
| Rollback lies | ✗ DDL committed, DML rolled back | ✓ Rollback scopes to the open segment; committed segments are *reported* |
| Self-block (§2.2b) | ✗ DDL waits on our own open data tx | ✓ impossible — one tx at a time |
| Commit ambiguity | ✗ two lanes, which does Commit settle? | ✓ one lane, one meaning |
| Classifier authority | ✗ decides **where** (violates #215) | ✓ decides **when to commit** |
| Fixes #213 | ✓ (accidentally) | ✓ (by design) |
| Deployment DDL can wait out a lock | ✗ (and self-blocks) | ✓ per-segment WAIT TPB |

### 5.3 Honest trade-offs of the recommendation

- **Atomicity is lost in `Sequenced` — unavoidably.** Firebird cannot do a transactional mixed
  migration. Today's tool claims all-or-nothing and *fails*; the new mode gives up the claim and
  *works*. This is the whole trade, and it must be **explicit in the UI**, not a footnote — the mode
  should say so where the user picks it. `Manual` remains for anyone who wants true all-or-nothing
  (for a DDL-only or DML-only script, where Firebird can honour it).
- **`Manual` + a mixed script is still broken** and should be **rejected up front** with a clear
  message pointing at `Sequenced`, rather than failing on statement 2 with `Table unknown`.
  (`ScriptValidation` already does up-front rejection; this is one more rule in an existing seam.)
- **Cost**: a real mode, real tests, real UI text. Not a small change — but far smaller than a lane
  split, and it deletes a KNOWN-BROKEN docstring instead of building on top of it.

---

## 6. Implementation plan

**Step 0 is DONE (2026-07-20). Steps 1–6 are not started.**

**Step 0 — MEASURE — RUN 2026-07-20 (blocking gate; cleared).**
The probe (`scratchpad/LaneProbe/`, standalone, not in `EmberTern.slnx`; managed driver, non-ASCII
repo path fine — gotcha #149) reads the password from `ET_LAB_PWD` so no secret hits disk. It was run
twice against `Lab/EmberTern_Lab.fdb` on the live FB5 (`WI-V5.0.3.1683`), identical outcomes; the lab
`.fdb` was restored to pristine afterward.

```powershell
$env:ET_LAB_PWD = "<local dev SYSDBA password>"
dotnet run --project "<scratchpad>\LaneProbe"
```

**Results:**

| Probe | Scenario | Result |
|---|---|---|
| **1**  | DDL `ADD COLUMN` on lane #3 vs our uncommitted `INSERT` on lane #1 (WAIT 10 s) | **SUCCEEDED ~7 ms — no self-block** |
| **1a** | Same, WAIT 3 s | **SUCCEEDED ~6 ms — no self-block** |
| **1c** | Table-scanning DDL `CREATE INDEX` vs our uncommitted `INSERT` (WAIT 10 s) | **FAILED ~10 011 ms — SELF-BLOCK** (SQLSTATE 40001, `isc_lock_timeout`) |
| **1d** | Format-changing DDL `DROP COLUMN` vs our uncommitted `INSERT` (WAIT 10 s) | **SUCCEEDED ~7 ms — no self-block** |
| **1b** | DDL vs an open **read-only** tx on lane #1 | SUCCEEDED ~6 ms — an open read never blocks DDL |
| **2**  | `CREATE TABLE T; INSERT INTO T;` in ONE tx | INSERT **FAILED** (SQLSTATE 42000 / -204) — **#213 confirmed** |
| **2a** | Two independent `CREATE TABLE`s in ONE tx | Both **committed** — independent DDL can share a segment |
| **3**  | `CREATE` then `INSERT` split by a commit boundary, one lane | **Both succeeded** — the §5 fix works |

**Outcome — §2.2(b) is RESTATED, not withdrawn; the architecture is unchanged and now
measurement-gated.** The self-block is **real but selective**: it occurs for table-scanning DDL
(`CREATE INDEX` — PROBE 1c, a genuine WAIT-exhausted lock timeout) but **not** for the metadata-only
op the review used as its example (`ADD COLUMN`/`DROP COLUMN` — PROBEs 1/1a/1d). So §2.2(b)'s example
was falsified while its phenomenon was confirmed. Because §2.2(a) (Rollback lies) is decisive on its
own, the lane-split rejection stands regardless, and the **Sequenced design (§5) — which by
construction never holds two transactions open at once — cannot self-block at all.** PROBE 2/2a/3
validate the three engine facts the Sequenced planner rests on. The gate is cleared: the design is
safe to freeze. Full write-up in the Step 0 report kept with this session's scratchpad; §2.2(b) and
§7 updated in place.

**Step 1 — Documentation truth pass — DONE (2026-07-21).** *(Comment-only; no behaviour change.)*
Fixed the stale comments that caused this drift. Two of the three originally-named targets had
**already** been corrected by the time this ran — `ConnectionProfile.cs:19` (the NOWAIT claim; now
correctly describes Dev Mode as a WAIT policy) and the `FirebirdScriptExecutor` header (no longer
cites the superseded gotcha #122). The remaining two false statements were corrected in place:
- **`ConnectionRole` docstring** (`FirebirdConnectionService.cs`) — claimed Metadata *"carries …
  the metadata working transaction"*, contradicted by `MetadataLane` (which owns **no** transaction).
  Now: Metadata carries read-only catalog browsing on an implicit per-command transaction, owns none.
- **`MainWindowViewModel.cs:200`** — cited *"co-location, gotcha #122"* as the reason the Script
  Executor runs on the Data lane. That rationale was superseded by #214; the real reason is that the
  Script Executor **IS** the user working transaction (one tx per connection, #89). Corrected.

The corrective comments that already describe #122/co-location as *superseded* (`FirebirdConnectionService`
`ExecuteDdlAsync`, `FirebirdDdlExecutor`) are accurate and were left unchanged. Build 0/0; Script
Executor + Developer Mode tests green (101).

**Step 2 — Dev Mode text (§4.2) — FOLDED INTO STEP 3, and already in place (2026-07-21).** On review
the user's decision (c) was effectively already shipped: `UiStrings.DeveloperModeDescription` already
states the scope (*"applies when you compile an object in its editor, and when the Script Executor
runs a script that only creates or changes objects"*) **and** the SQL-Editor boundary, and
`DeveloperModeBadgeTooltip` already ends *"Does not affect the SQL Editor."* The text is truthful for
today's behaviour (the all-DDL auto-commit path already reuses the Dev Mode WAIT policy), so **no edit
was needed.** When the `Sequenced` build lands (Step 4) and *every* schema segment becomes
Dev-Mode-aware, the scope sentence can broaden from *"a script that only creates or changes objects"*
to *"the schema statements of any deployment script"* — a one-line text change to make with Step 4, not
now.

**Step 3 — `Sequenced` mode, Core — DONE (2026-07-21).** `ScriptTransactionMode.Sequenced` added; new
pure `ScriptSegmentPlanner` (`EmberTern.Core.Scripting`) splits a parsed script into ordered
`ScriptSegment`s over the AST-based `SqlStatementClassifier` (**not** the driver's statement enum — the
single-classifier convergence the review asks for), each carrying a `SegmentTransactionPolicy` intent
(`DataNoWait` / `SchemaWait`). Conservative v1 rule: **each schema statement is its own committed
segment** (isql `SET AUTODDL ON`), data statements group into their own NOWAIT segments — every segment
homogeneous, one transaction ever open, so the §2.2(b) self-block is impossible by construction and
#213 is fixed by design. Grouping independent consecutive DDL (permitted by §5.1, proven safe by PROBE
2a) is a **documented deferred optimization** — it needs object-dependency analysis to stay safe for
dependent DDL, and committing after each DDL is always correct. Pure Core only — no Firebird execution
path, no App, no UI (Steps 4/5). Build 0/0; +10 `ScriptSegmentPlannerTests`; Script + Dev Mode suite
green (110). **Next actionable is Step 4** (Firebird layer runs the segments) — gated on the user
scheduling it.

**Step 4 — Firebird layer.** `FirebirdScriptExecutor` runs the prepared plan (the planner stays the
sole planner; Firebird only executes). Split into two seams:
- **Seam A — per-segment TPB resolution — DONE (2026-07-21).** Pure internal
  `FirebirdScriptExecutor.ResolveSegmentTransactionOptions(SegmentTransactionPolicy, bool)` maps a
  planner-assigned policy to a Firebird TPB: `SchemaWait` → the SAME Dev-Mode-aware WAIT policy
  Compile uses (`FirebirdDdlExecutor.BuildDdlTransactionOptions`, one definition, no drift);
  `DataNoWait` → null (the working transaction's NOWAIT ReadCommitted default). Unit-pinned in
  `DeveloperModeTests` (+5); no execution path, no behaviour change to any existing mode. Build 0/0.
- **Seam B — Sequenced execution loop — NOT started.** `RunAsync` gains a Sequenced branch: call
  `ScriptSegmentPlanner.Plan`, then per segment begin a transaction with seam A's TPB, run its
  statements via the existing `RunOneAsync`, commit on success / roll back the OPEN segment on failure
  (stop or continue per `StopOnError`). Manual/AutoCommit paths unchanged. Committed segments stay
  applied — the per-statement results already record what succeeded, and the App reconstructs segment
  boundaries + the "which segments committed" summary in Step 5. **Requires live verification** against
  the lab with a real mixed migration (clean + failing).

**Step 5 — App layer.** Third mode in the picker with the atomicity trade-off stated in the UI;
up-front rejection of mixed scripts in `Manual`; results grid shows segment boundaries.

**Step 6 — Verify.** Build 0/0 + full suite + **live verification against the lab DB** with a real
mixed migration script. Per the QA rule: not "fixed" until visually confirmed in the running app.

**Separately (not part of this milestone) — cleanup.** `ConnectionProfile.Data/MetadataTransactionProfile`
are **fully vestigial**: never read (`ResolveActiveProfile()` hard-returns `ReadCommitted`), the dialog
pickers are gone, and the `MainWindowViewModel` profile badges/tooltips are **unbound in every view**.
They survive only in persistence, migration, and tests. `TransactionService.cs:106` already says they
are *"slated for removal in their own pass"*. Worth doing — but it is dead state, **not** a lying
control, so it is low-severity and does not belong in this milestone.

---

## 7. Evidence log

**Proven by reading the code** (file:line cited inline):
`ScriptTransactionMode` has no per-task mode · `AutoCommitOnSuccess` commits once at the end ·
`MetadataLane` owns no transaction · Script Executor runs NOWAIT and ignores Dev Mode ·
`ResolveActiveProfile()` hard-returns `ReadCommitted` · profile pickers absent from the dialog,
badges unbound · `7586a25` precedes `3ce2a9e`.

**Proven about Developer Mode specifically** (this is what the §1.2 correction rests on):
`grep -rn DeveloperMode src/` returns **exactly one behavioural consumer** —
`FirebirdDdlExecutor.cs:115`; every other hit is the field, the checkbox, the badge, or a string ·
`DeveloperModeTests`' docstring states the intended semantics verbatim (*"Standard fails fast against
another session, Developer waits longer"*) and asserts `NoWait` is absent in **both** modes ·
history 13 records Dev Mode as **DDL-only from birth** (commit `60b456a`: *"Only the DDL path is
affected"*), with *"Raw F5 DDL in the SQL Editor stays NOWAIT"* listed as **deliberately out of
scope** — so Dev Mode never applied to the SQL Editor and therefore was never removed from it · the
Data/Metadata routing is a **separate**, unconditional mechanism (`7377968`), removed by `3ce2a9e`.

**Retracted from this review's first draft** (both were overreach, corrected on challenge):
"the Dev Mode description is wrong about Firebird" — it is a fair simplification (§4.1) ·
"the switch no longer earns the name Developer Mode" — its 3 s/10 s split is a real semantic
distinction, not a diluted dial (§1.3).

**Measured previously, recorded, trusted:** #213 (CREATE+INSERT in one tx → -204) · #214
(cross-attachment "object in use" is transient; WAIT clears in ~10 ms).

**Measured in Step 0 (2026-07-20, live FB5 `WI-V5.0.3.1683`, two runs, deterministic):**
- **§2.2(b) self-block is real but SELECTIVE.** Table-scanning DDL (`CREATE INDEX`) against a table
  with our own uncommitted `INSERT` waits out the full 10 s WAIT and fails `isc_lock_timeout`
  (SQLSTATE 40001) — PROBE 1c. **But `ALTER TABLE … ADD COLUMN` (PROBE 1) and `DROP COLUMN`
  (PROBE 1d) do NOT block** (~7 ms, metadata-only on FB5) — so the review's original *example* was a
  falsified inference, joining #213/#214/#215; the *phenomenon* stands for a real DDL class.
- **#213 re-confirmed on FB5** (PROBE 2): `CREATE TABLE T; INSERT INTO T;` in one tx → INSERT fails
  (SQLSTATE 42000 / -204).
- **Sequenced commit-boundary fix works** (PROBE 3): `CREATE`, commit, then `INSERT` in a new tx on
  one lane → succeeds.
- **Independent DDL can share a segment** (PROBE 2a): two unrelated `CREATE TABLE`s commit in one tx
  → the §5.1 planner need not commit between every DDL.
- **An open read-only tx never blocks cross-lane DDL** (PROBE 1b).

**Net:** the review's *conclusion* (Sequenced, not lane-split; three attachments) is unchanged and
now measurement-backed; only §2.2(b)'s example and "decisive objection" framing were corrected. The
decisive objection to the lane split is §2.2(a) (Rollback lies), which never rested on inference.
