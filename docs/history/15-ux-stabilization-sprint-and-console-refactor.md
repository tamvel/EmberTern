# History — UX & Stabilization Sprint, the DDL attachment, and the SQL-Editor console refactor

*Sprint run 2026-07-14. Eleven user-reported UX/quality items, which turned into a
three-stage architectural correction of the transaction/attachment model. Nothing here
was planned as architecture work — it fell out of one bug report ("Compile says *Commit or
roll back the active transaction* while I have a SELECT open in the SQL Editor").*

---

## Part 1 — The eleven stabilization tasks

Each was root-caused before being fixed; several removed a mechanism rather than adding one.

1. **SQL Editor — "Copy cell" copied the wrong value.** The results grid is bound to
   `PagedResultRows` (a *sorted + filtered + paged* view), but `BuildCopyText` indexes the raw
   `CurrentResult.Rows`. `InvokeCopy` fed it **view** coordinates (`SelectedIndex`,
   `CurrentColumn?.DisplayIndex ?? 0`). Under any sort/filter/page-2 it read a different row, and
   because `CurrentColumn` is null on a fresh right-click it fell back to column 0 (the ID) — which
   is why `10619` came out as `1`. The *filter* menu beside it already resolved the true cell via
   `Column.Tag` + the clicked row object; copy was the odd one out. **Unified copy onto that same
   resolution** — one right-click cell-resolution path instead of two.

2. **Activity Monitor Start/Pause.** The Pause/Resume toggle's glyph + tooltip were static, so a
   paused session still showed a lit "Pause". Made the toggle reflect state (Pause↔Play).

3. **Activity Monitor DATETIME.** The trace inliner quoted the captured timestamp verbatim
   (`'1899-12-30T00:00:00'`). Firebird's trace emits the ISO `T`, but a Firebird datetime *literal*
   rejects it — so the "runnable SQL" the inliner exists to produce was never runnable for a
   timestamp. Normalized `T` → space at the point runnable SQL is produced; the raw captured value
   stays verbatim in the Parameters list (§0).

4. **Activity Monitor search focus ring.** A borderless TextBox inside a framed Border still had
   FluentTheme paint its own blue focus visual. Added a reusable `TextBox.frameless` class.

5. **Auto constraint names.** Each dialog produced a fixed `IDX_<table>` etc. with no uniqueness
   check. One shared `ConstraintNaming.MakeUnique(base, existing)` in Core now inserts the lowest
   free counter after the prefix (IBExpert style: `IDX_T → IDX1_T → IDX2_T`), fed by the table's
   existing index+constraint names. Four independent naming paths → one generator.

6. **Connection wizard.** A blank name is derived from the database filename
   (`Magazyn.fdb → Magazyn`); "Copy connection" now opens the clone in the edit dialog immediately.

7. **Context-menu consistency audit.** The Easy-Mode collection editors exposed add/remove/move only
   on the toolbar; right-clicking a row did nothing. Used `ActiveCollection()` as the authoritative
   map of every editable collection, then added context menus **bound to the same commands the
   toolbar uses** (no parallel logic) + a shared right-click-selects-row handler. Covered Procedure,
   Function, Trigger, New Table. *Remaining:* the View Easy-mode column editor.

8. **DDL vs the active SQL transaction.** → became Parts 2 and 3 below.

9. **Cancel Query needed several clicks.** The executor only *passed* the `CancellationToken` to the
   driver. A token is cooperative — it is observed at an await that yields — so it **cannot
   interrupt a statement Firebird is still computing** (a heavy join returns no rows for a long
   time, so nothing observes it). Cancel looked dead, and the extra clicks were no-ops on an
   already-cancelled CTS. **Fix:** register `FbCommand.Cancel()` (→ `fb_cancel_operation`) on the
   token, which aborts the statement *server-side*; plus an `IsCancelling` latch so the first click
   visibly registers.

10. **IntelliSense: procedure params + local variables.** Source mode already worked (pinned by test
    first). The gap was **Easy mode**: that editor holds only the *body*, while the parameters and
    DECLAREd variables live in the surrounding **grids** — a text-only model cannot see them.
    **Fix:** an **ambient symbols** seam — declarations that exist outside the text seed the model's
    root scope. Because they seed the *model* (not the completion list), every client benefits at
    once: completion, Quick Info, navigation, highlighting. A declaration in the text correctly
    shadows an ambient one.

11. **IntelliSense letter case.** `CaseMatcher` shaped the insert from the typed prefix — but right
    after `k.` that prefix is **empty**, so the catalog's UPPERCASE won and `ID_KONTRAHENT` landed in
    an all-lowercase query. **Fix:** decide from the user's *actual writing style in the document*.
    New `SqlCaseStyleDetector` reuses the existing lexer (so words in string literals, quoted
    identifiers and comments are never counted), counts identifiers first (keywords as fallback),
    ignores mixed-case words, and returns `Unknown` on a tie rather than guessing.

---

## Part 2 — The dedicated DDL attachment (and the correction of gotcha #122)

**The report:** run a SELECT in the SQL Editor, leave the transaction open, edit a trigger, press
Compile → *"Commit or roll back the active transaction before running DDL."*

**Why it happened.** DDL ran on `RequireOpenConnection()` = the **Data** connection — the same one
the SQL Editor holds its open SELECT transaction on. One `FbConnection` allows one transaction
(gotcha #89), so Compile could not begin its autonomous transaction. That co-location was
*deliberate*: the "Single-attachment DDL" fix (Krok 1) had concluded from gotcha #122 that Firebird
holds a routine "in use" at the **attachment** level, so Compile must run on the attachment that
executed it.

**The measurement that changed everything.** Before touching the transaction core, the premise was
tested against the real FB5 engine (Lab DB, via the user's own saved profile — the password never
left the store; the user's production databases were never touched):

| Case | Result |
|---|---|
| B alters a proc nobody touched | OK |
| A executed proc + committed, A still open; B alters (**NOWAIT**) | **FAIL** — *object is in use* |
| …same, but **WAIT**, with no prior NOWAIT attempt | **OK, ~10 ms** |
| A executed + committed, then closed | OK |
| A holds an **open unrelated SELECT tx**; B alters | **OK** |
| A holds an **uncommitted INSERT** firing a trigger; B alters that trigger | **OK** |

Two conclusions, both overturning the original workaround:

1. An unrelated open transaction on another attachment **never blocked DDL**. The block was purely a
   *same-connection* artifact, not a Firebird rule.
2. The cross-attachment *"object is in use"* is a **transient metadata-cache lock** that bites only
   **NOWAIT**. **WAIT was the real fix all along**; co-location was treating the symptom.
   → recorded as **gotcha #214**, which supersedes the conclusion of #122.

**What shipped.** A third attachment, `ConnectionRole.Ddl` — it carries Compile/structure DDL and
nothing else and **never holds a working transaction**, so DDL can always begin its own autonomous
transaction regardless of what the user left open anywhere. Its TPB is always **WAIT** with a bounded
timeout (Standard 3 s = absorb our own lane's ~10 ms cache release while still failing fast against
another session; Developer Mode 10 s = wait for another session). **Deleted:** the
*"Commit or roll back…"* guard (now reachable only in degraded mode, if the third attachment can't
open) and NOWAIT from the DDL path entirely.

Verified end-to-end through the production classes: **Compile succeeds with the SQL-Editor
transaction still open, and that transaction is left untouched**; and an `ALTER` of a routine *just
executed* on the data lane succeeds in 10 ms.

Also in this part: **compile/DDL error messages are now selectable and copyable**
(`SelectableTextBlock` across all 10 object editors).

---

## Part 3 — The SQL Editor becomes a classic console (and the transaction model collapses)

The Part-2 fix worked, but the user's review of the *whole* connection architecture surfaced
something bigger.

**What was actually there.** The SQL Editor was **already** routing: `SqlStatementClassifier` parsed
each F5 and sent DML to the data attachment and **DDL/DCL to the *metadata* attachment**, where it
silently auto-began a **second working transaction** with its own Commit/Rollback. Consequences:
"Commit" was ambiguous; a mixed script split across two transactions *and* two attachments, so
dependent statements couldn't see each other; and the app had to **log which lane it chose** on every
execute because the behaviour was otherwise unknowable. Classifying by the *first statement only*
compounded it (`SELECT …; DROP TABLE …;` routed as "Data").

**The user's decision:** the SQL Editor is a **classic Firebird SQL console** — one attachment, one
transaction, one Commit, one Rollback, NOWAIT, no hidden routing, no hidden commits. Intelligent
execution belongs in the Script Executor, which is a *deliberate* tool. → **gotcha #215.**

**The refactor (a deletion, not an addition).**

*Deleted*
- The SQL-Editor lane router and `_metadataExecutor`, and with them the **entire second working
  transaction** (nothing else ever *began* it — the readers only attach or use implicit transactions).
- The dual-lane commit model: `DecideCommitLanes`, `DecideRollbackLanes`, the per-lane
  Commit/Rollback command pairs, the second status chip, `IsMetadataTransaction*`,
  `MetadataLaneIndependent`, `ShowMetadataTransactionButtons`, the metadata unsaved-work warning.
- `BuildExecutedViaMessage` — the "which lane did this run on?" log line. It existed *because* the
  routing was invisible; with one attachment there is nothing to disclose.
- `TransactionService`'s degraded-mode fallback delegation (`ShouldDelegate`, `EffectiveRole`, the
  fallback chain) and its role/profile-selector parameters. Seven now-dead `UiStrings`.

*Renamed to tell the truth*
- **`StatementLane` → `SqlStatementCategory` (`Data` / `Schema` / `Ambiguous`).** It no longer picks
  a lane; it answers *"does this change the catalog?"* — a **refresh hint** only, changing no
  execution semantics. It now classifies the **whole script**, not just the first statement.
- `DecidePostTransactionRefresh` re-keyed from *which lane settled* to *what the transaction did*.

*New — one obvious responsibility each*
- **`TransactionService`** = **THE user transaction.** One. Data attachment. NOWAIT, now enforced
  internally so a stored legacy table-stability profile can't silently make the console WAIT.
- **`MetadataLane`** = the **read-only** catalog attachment: connection + lock, **owns no
  transaction**, implicit per-command reads. It replaces the zombie "metadata `TransactionService`" —
  an object that, once routing was gone, could never hold a transaction. Threaded through all 8
  readers.
- **DDL attachment** — object editors only (Compile / WAIT / Developer Mode).

**The behavioural change, accepted deliberately:** an object created in the SQL Editor appears in
the metadata tree **only after Commit** (uncommitted DDL is invisible to the read-only metadata
attachment). This is classic console semantics and the user explicitly signed off on it, preferring
it to hidden synchronization that would make uncommitted DDL appear.

**The wall we hit, and why it is not a bug.** Verifying the console against FB5 showed that
`CREATE TABLE T …; INSERT INTO T …;` **cannot run in one transaction** — the INSERT fails with
`Table unknown (-204)`. Firebird cannot both let a transaction use an object it created *and* keep
that object rollbackable; `isql`/IBExpert choose the former via `SET AUTODDL ON`. → **gotcha #213.**
So a mixed migration script in the console requires a manual Commit between the DDL and the
dependent DML. That is expected, and it is the correct division of labour: **the console never
surprises; the Script Executor is allowed to be smart.**

---

## Known-broken, deferred to its own sprint: the Script Executor

The same measurement exposed that **`FirebirdScriptExecutor` is broken for its primary use case**.
Its docstring claimed *"Firebird DDL is transactional, so a mixed DDL+DML migration is genuinely
all-or-nothing"* — assumed, never measured, and **false** (gotcha #213). Every mode runs the whole
script in ONE transaction, so a deployment script that creates and then populates anything fails at
the second statement. Nobody noticed because the premise was never tested.

It also carries the **last surviving instance of the bug class this sprint removed**: it shares the
one user transaction, so an unrelated open SQL-Editor transaction blocks it
(*"Commit or roll back the active transaction before running a script."*), and its DDL is stuck on
NOWAIT.

And there is a **duplication** worth collapsing: the Script Executor has its *own* classifier —
`FirebirdScriptParser.MapKind` maps the **driver's** `FbScript`/`SqlStatementType` into
`ScriptStatementKind` — while Core has the AST-based `SqlStatementClassifier`. Two classifiers, and
the Script Executor's is the weaker, driver-coupled one. (There are likewise two splitters:
`FbScript` vs our `SqlStatementSplitter`; note `FbScript` handles `SET TERM`, which our splitter does
not, so unifying splitters is *not* free.)

**By user decision, none of this was touched in this sprint** — the Script Executor is a dedicated
architectural task. The classification infrastructure in Core was deliberately **kept, not deleted**:
the routing was the bug, the classifier is the foundation of the future execution engine. A future
sprint should give it a real execution policy (commit-after-DDL / AUTODDL, DDL-aware WAIT, up-front
rejection of mixed scripts in single-transaction mode) driven by the AST classifier. The only change
made to `FirebirdScriptExecutor` here was a comment correcting the false claim so nobody builds on it.

---

## Script Executor Rewrite — Step 0 (the Probe), 2026-07-20

The rewrite's architecture review (`docs/design/script-executor-transaction-review.md`) chose a
**Sequenced** mode (one lane, one transaction at a time, commit boundaries between segments) over the
user's proposed concurrent lane split, and rejected the split on four grounds §2.2(a)–(d). One of
those, **§2.2(b) self-block**, was explicitly flagged as *reasoned from Firebird semantics, not
measured* — and the review made it a **blocking gate (Step 0)**: given #213/#214/#215 were all
falsified inferences, the claim had to be measured before the design was frozen. This session ran it.

The probe (`scratchpad/LaneProbe`, a standalone console on the managed driver — non-ASCII repo path
fine, #149; password from `ET_LAB_PWD`, never on disk) exercised the self-block, #213, the
commit-boundary fix, and the segment-sharing premise against the live FB5 lab. Two runs, identical.

**The decisive finding: the self-block is real but SELECTIVE — and the review's example was wrong.**

- `ALTER TABLE … ADD COLUMN` (the review's verbatim §2.2(b) example) does **not** self-block on the
  script's own uncommitted same-table `INSERT` — it succeeded in ~7 ms at both WAIT=10 s and WAIT=3 s.
  `DROP COLUMN` likewise. On FB5 these are metadata-only and proceed concurrently with open DML.
- `CREATE INDEX` **does** self-block — it must scan every row, so it waits on the lock our own
  still-open data transaction holds, exhausts the full 10 s WAIT, and fails with `isc_lock_timeout`
  (SQLSTATE 40001). "Populate a table, then index it" is an ordinary migration pattern, so this is a
  genuine hazard, not exotic.

So §2.2(b)'s *example* joined #213/#214/#215 as another falsified inference, while its *phenomenon*
was confirmed for a real DDL class. The right move was to **restate, not withdraw** it: correct the
example to `CREATE INDEX`, narrow the scope to table-scanning DDL, and drop the "decisive technical
objection" framing — because the genuinely decisive objection, §2.2(a) (a DDL auto-commit lane makes
manual Rollback roll back only the DML — a paramount-rule-#11 corruption), never rested on inference.

The rest of the probe validated the Sequenced design's engine premises: **#213 re-confirmed**
(`CREATE`+`INSERT` in one tx fails, -204); the **commit-boundary fix works** (`CREATE`, commit,
`INSERT` in a fresh tx on one lane succeeds); and **independent DDL can share a segment** (two
unrelated `CREATE TABLE`s commit together), so the planner need not commit between every DDL.

**Net: the architecture did not change.** The Sequenced conclusion stands and is now
measurement-backed; the Sequenced design *cannot* self-block by construction (it never holds two
transactions open at once — the self-block is purely a lane-split hazard). Only the review's §2.2(b),
§6 (Step 0 marked RUN with results), and §7 (evidence log) were corrected in place. No production
code was touched — Step 0 is validation only. The next actionable is Step 1 (the documentation truth
pass); the `Sequenced` build (Steps 3–6) proceeds only when the user schedules it. The lab `.fdb` was
churned by the probe's temporary tables and restored to pristine (`git checkout`) afterward.

## Script Executor Rewrite — Step 1 (Documentation Truth Pass), 2026-07-21

A small, safe cleanup step: bring the code comments into agreement with the Step 0 findings, with
**no** behaviour, execution-path, UI, or App change. The review (§6) named three stale-comment
targets; by the time this ran, **two were already clean** — `ConnectionProfile.cs` no longer claims
Dev Mode "OFF ⇒ NOWAIT" (it correctly describes a WAIT policy whose modes differ only in timeout),
and the `FirebirdScriptExecutor` header no longer cites the superseded gotcha #122 co-location
rationale. Two false statements remained and were corrected:

- **`ConnectionRole` enum docstring** (`FirebirdConnectionService.cs`) still advertised the Metadata
  attachment as carrying *"the metadata working transaction"* — directly contradicted by
  `MetadataLane`'s own docstring (*"It owns NO transaction"*). This was the exact place §1.2 said the
  Data/Metadata-routing and Developer-Mode mechanisms blur together. Rewritten to state that Metadata
  carries read-only catalog browsing on an **implicit per-command** transaction and owns none.
- **`MainWindowViewModel.cs:200`** cited *"co-location, gotcha #122"* as the reason the Script
  Executor is on the Data lane. #122 (DDL co-location) was superseded by #214 (the "object in use"
  lock is a transient NOWAIT-only cache pin, cleared by WAIT). The real reason the Script Executor
  lives on the Data lane is that it **IS** the user working transaction (long-lived, manual
  Commit/Rollback, one tx per connection — #89). Corrected.

The already-accurate *corrective* comments that describe #122/co-location as superseded
(`FirebirdConnectionService.ExecuteDdlAsync`, `FirebirdDdlExecutor`) were left untouched. The
vestigial `Data/MetadataTransactionProfile` fields (review §6 "Separately") are **dead state, not a
lying comment**, so they stay out of this pass. **Verification:** build 0/0; Script Executor +
Developer Mode tests green (101/101). The next actionable is the `Sequenced` build.

## Script Executor Rewrite — Step 3 (Sequenced core) + folded Step 2, 2026-07-21

The first *implementation* step of the rewrite — **Core only**, no Firebird execution path, no App,
no UI (those are Steps 4/5). The user folded the old Step 2 (Dev Mode text) into this work.

**Step 2 (Dev Mode text, review §4.2) — folded in, already truthful.** The review recommended stating
Developer Mode's *scope* (it covers object-editor Compile/Recompile and the Script Executor's all-DDL
path, not the SQL Editor). Reading `UiStrings` showed a prior milestone had already done exactly that:
`DeveloperModeDescription` already says *"applies when you compile an object in its editor, and when
the Script Executor runs a script that only creates or changes objects … The SQL Editor is not
affected"*, and `DeveloperModeBadgeTooltip` already ends *"Does not affect the SQL Editor."* So there
was nothing to change — the text is accurate for today's behaviour. (When the `Sequenced` build makes
*every* schema segment Dev-Mode-aware in Step 4, the scope sentence can broaden — a one-line edit left
for that step, so the text never over-claims ahead of the behaviour.)

**Step 3 (Sequenced core).** Two additions to `EmberTern.Core.Scripting`:
- `ScriptTransactionMode.Sequenced` — the third mode ("Deployment"). Its doc states the whole trade:
  run in order on one lane, commit after each schema statement so a later statement can use what an
  earlier one created (#213); **not atomic** — a committed segment stays applied if a later one fails
  (Firebird cannot both let a transaction use an object it created and keep it rollbackable). No
  execution path consumes it yet, and the App picker (a two-index map) never produces it, so adding the
  member is inert until Step 4/5 wire it.
- `ScriptSegmentPlanner` — a pure function `Plan(statements) → IReadOnlyList<ScriptSegment>`. Each
  statement is classified by the **AST-based `SqlStatementClassifier`** (Schema / Data / Ambiguous),
  never the driver's `ScriptStatementKind` — the single-classifier convergence the review (§7) asked
  for, and pinned by a test whose statements carry a `Kind` that *disagrees* with their text. A
  `ScriptSegment` is `(Statements, SegmentTransactionPolicy)`; the policy is an **intent**
  (`DataNoWait` / `SchemaWait`), mapped to a real Firebird TPB only in Step 4 (Core stays free of
  `FirebirdSql`).

**The rule (conservative v1):** a schema statement is its OWN committed segment; data statements group
into their own NOWAIT segments between schema statements. Every segment is homogeneous, exactly one
transaction is ever open, so the §2.2(b) lane-split self-block is impossible by construction and #213
is fixed by design. Review §5.1 *permits* grouping consecutive independent DDL into one segment (PROBE
2a proved that safe), but this planner does **not** — telling independent DDL apart from *dependent*
DDL (`CREATE TABLE T; CREATE INDEX … ON T;`, which #213 would break in one transaction) needs
object-dependency analysis that does not exist yet, and committing after each DDL is always correct
(exactly isql `SET AUTODDL ON`). The grouping is left as a documented future optimization, never a
correctness risk — faithful to "verify, don't infer."

**Verification:** build 0/0; +10 `ScriptSegmentPlannerTests` (empty, all-data, single-DDL,
create-then-insert boundary, data/schema/data, consecutive-DDL-not-grouped, DCL-as-schema, AST-not-Kind
classification, full-coverage-in-order); Script + Developer Mode suite green (110/110). The next
actionable is Step 4 (the Firebird layer runs the segments) — not started, gated on the user.

## Script Executor Rewrite — Step 4 (Firebird layer runs the plan), 2026-07-21

The Firebird layer now *executes* the Sequenced plan the Core planner prepares — **Firebird never
plans; the planner stays the sole planner**. Split into two committable seams.

**Seam A — per-segment TPB resolution (pure, no execution).** A tiny internal
`FirebirdScriptExecutor.ResolveSegmentTransactionOptions(SegmentTransactionPolicy, bool)` maps a
segment's planner-assigned policy to a Firebird TPB: `SchemaWait` → the SAME Developer-Mode-aware WAIT
policy object-editor Compile uses (`FirebirdDdlExecutor.BuildDdlTransactionOptions` — one definition, no
drift), so a deployment can outlast another session's transient hold; `DataNoWait` → `null` = the working
transaction's NOWAIT ReadCommitted default, so deployment DML never blocks on an ordinary row lock. Pure +
internal, unit-pinned in `DeveloperModeTests` (+5). Zero execution path, zero behaviour change to any
existing mode.

**Seam B — the Sequenced execution loop (live-verified).** `RunAsync` now dispatches — after the same
shared up-front checks (active-tx guard, disallowed-statement rejection, empty short-circuit) — to a new
`RunSequencedAsync`. It calls `ScriptSegmentPlanner.Plan(statements)` and runs each segment in its OWN
transaction on the data lane: begin with seam A's TPB, run the segment's statements through the existing
`RunOneAsync`, then **commit on success / roll back the OPEN segment on failure**. `stopOnError` stops the
whole run on the first failure; otherwise a data segment runs to its end and rolls back as a unit (exactly
as AutoCommit runs a whole script then rolls back — a schema segment is a singleton, so it never has a
"rest"). A running index reconstructs each statement's original position (segments are contiguous and in
order, so `ScriptSegment` needs no `StartIndex`). The command lock is held only around a segment's
statements — Begin/Commit/Rollback acquire it themselves, so holding it across them would deadlock (the
single-tx path releases before committing for the same reason); a mid-statement `OperationCanceledException`
is caught so the open segment is rolled back rather than leaked. Exactly one transaction is ever open →
the §2.2(b) self-block is impossible by construction; `TransactionLeftOpen` is always false (Sequenced is
never the "review then Commit" flow). **Core is untouched** — no `ScriptRunOutcome` change; Manual/AutoCommit
are byte-unchanged; and the now-false "KNOWN BROKEN — a mixed migration cannot run" class docstring was
corrected to "single-transaction modes still can't; use `Sequenced`."

**Live verification.** Seam B needs a real `FbConnection`, so — per the project's "verify, don't infer"
rule — a new throwaway probe `tools/probes/ScriptExecutorSequencedProbe` drives the REAL
`FirebirdScriptExecutor` in Sequenced mode against a scratch DB (`C:\Temp\…`, created + deleted; the lab
is never touched, per the probe rules). **ALL PASS on FB5 (`WI-V5.0.3.1683`):** (A) a mixed
`CREATE TABLE → INSERT → INSERT → CREATE INDEX → INSERT` migration runs end-to-end — 3 rows + the index
persist — proving #213 is fixed by design (and that the table-scanning `CREATE INDEX` after inserts, the
PROBE-1c self-block case for a lane split, runs fine here because segments are sequential); (B) the SAME
`CREATE + INSERT` under `AutoCommitOnSuccess` still fails the INSERT with a -204 and rolls the whole run
back (the table does not exist) — Sequenced is the fix, not a coincidence; (C) a Sequenced script whose
last statement is a duplicate-PK INSERT keeps the earlier table + row + index committed and rolls back
only the failing segment (one row remains). Build 0/0; Script + Developer Mode + Transaction suite 165
green (regression). The next actionable is Step 5 (App layer) — not started, gated on the user.

---

## Final architecture

| Attachment | Carries | Transaction |
|---|---|---|
| **Data** | SQL Editor F5 (queries **and** DDL), table-data edits, Execute Procedure, Script Executor | **THE user transaction** — auto-begin, never auto-commit, NOWAIT, one Commit / one Rollback |
| **Metadata** | catalog reads only (sidebar, DDL preview, completion, security, statistics) | none — implicit per-command |
| **DDL** | object-editor Compile / structure DDL only | autonomous, auto-committed, **WAIT**-bounded (Developer Mode) |

**Verification:** Build 0 warnings / 0 errors. Tests 3633 main + 23 headless-probe, all green
(3596 → 3633). Smoke clean. Every Firebird claim in this document was measured against the Lab DB on
the live FB5 engine, never assumed.
