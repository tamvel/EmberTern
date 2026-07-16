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

## Final architecture

| Attachment | Carries | Transaction |
|---|---|---|
| **Data** | SQL Editor F5 (queries **and** DDL), table-data edits, Execute Procedure, Script Executor | **THE user transaction** — auto-begin, never auto-commit, NOWAIT, one Commit / one Rollback |
| **Metadata** | catalog reads only (sidebar, DDL preview, completion, security, statistics) | none — implicit per-command |
| **DDL** | object-editor Compile / structure DDL only | autonomous, auto-committed, **WAIT**-bounded (Developer Mode) |

**Verification:** Build 0 warnings / 0 errors. Tests 3633 main + 23 headless-probe, all green
(3596 → 3633). Smoke clean. Every Firebird claim in this document was measured against the Lab DB on
the live FB5 engine, never assumed.
