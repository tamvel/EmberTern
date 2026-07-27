# 22 — Architecture Hardening / Product Safety sprint (2026-07-27)

**Scope, in the user's words:** *"To nie jest sprint funkcjonalny"* — no new capabilities, no large
refactorings, no architectural rebuild. Only: seal the places where a real risk or oversight exists.
Explicitly out: Safety Timeline, three-way merge, disposable debug database, explainable performance
workspace, product guardrails, keyboard manager, and the metadata-tree performance work (each already
scheduled elsewhere).

**Input:** `docs/audits/embertern-full-audit-2026-07-26.md`, an external full-repository audit by
GPT Terra. **The user's instruction was explicitly not to trust it** — every finding was to be
re-verified against the current code, and anything wrong or no longer applicable was to be said so
plainly. That instruction earned its keep: two findings were mis-rated, one was stale in a way that
made it *understate* the risk, and the highest-priority finding turned out to have a second, more
likely failure mode the audit had not seen.

**Result:** build 0 warnings / 0 errors, suite **5900 green** (from 5856), smoke clean, one new live
probe at 19/19 on FB5 `WI-V5.0.3.1683`.

---

## Verification pass — what was real and what was not

| Audit ID | Verdict | Note |
|---|---|---|
| **A-01** DDL overwrite | **CONFIRMED, and worse** | Plus a second path needing no concurrency at all (below). |
| **A-03** settings loss | **CONFIRMED, P0 by impact** | Reachable by resizing a grid column. |
| **A-04** document mutations | **Real as a documentation/contract defect only** | No dangerous path exists today. |
| **A-05** vulnerable dependency | **CONFIRMED, broader than reported** | The audit's own mitigating argument was stale. |
| **A-09** dead transaction profiles | **CONFIRMED as a UI that can lie** | Narrower than described, but genuinely wrong. |
| **A-02** debugger side effects | **REJECTED as a P0 defect** | A ratified, documented design decision — an unmade product decision, not a bug. |
| **A-06** import I7 | **Historical** | Module closed and merged 2026-07-27. |
| **A-07** import report contract | **Real, P2, deliberately not done** | Frozen module; reported for the user's decision. |
| **A-08** large ViewModels | **Declined** | Agreed with the user: file size is not a defect. |
| **A-13** stale docs | Confirmed in passing | CLAUDE.md claimed `net10.0`; the real target is `net9.0`. |

### A-02 — why the P0 rating was rejected

The audit called it P0: the debugger can leave `IN AUTONOMOUS TRANSACTION` work, generator values and
external effects behind after its rollback. Every fact is true. But `DebugPreflight` already **detects
and reports** all three, and not blocking is deliberate and documented — spec §4.6 is titled around
"disclosed, not hidden", and the class comment says the scan *"is a safety warning, not structural
analysis, and never suppresses a launch"*. Making it blocking by default is a change of product
policy, which is precisely open question #2 of the audit's own list. Rating it alongside "your
colleague's code is silently destroyed" conflates a deliberate disclosure with an unintended loss.
Left for a separate decision.

### A-04 — why the finding was right and the remedy was not

`TextEditApplier` documented itself as *"the one owner of every change EmberTern makes to a user
document … there is deliberately no second path that writes to a `TextDocument`"*. Thirteen files call
`Document.Replace`/`Insert` directly, so the claim was false. Reading all thirteen, none is dangerous:
each is the synchronous response to the keystroke or command that produced it, computed and applied in
the same turn, one undo unit, and the formatter additionally carries its own §0 lexeme-preservation
invariant.

So the defect was a **documentation lie plus a missing tripwire**, not a corruption path — and the
audit's proposed remedy (split the world into `UserTypingEdit` and `AssistedCodeEdit` types, forced
through one document adapter) would have been a large refactor of every editor surface for no
behavioural gain. What the class actually owns is the **drift window**: an assisted edit is computed
from a `SemanticModel` at one moment and applied at another, so its offsets are a claim about the past
that must be re-checked. Nothing else here has that property.

---

## A-05 — the dependency (done first; smallest, most certain)

`System.IO.Packaging 8.0.0`, two High advisories, reaching **EmberTern.App and EmberTern.Office** —
both shipped.

The audit softened this with *"the current Office module exports, and does not import, XLSX"*, so no
hostile input. **That was already false when the audit was read:** etap I9 added `XlsxImportProvider`,
so parsing workbooks the user was handed by someone else is now the module's job, and both CVEs are
denial of service *on hostile input*. The exposure is ordinary use, not hypothetical.

Fixed by direct overrides: `System.IO.Packaging` → 9.0.18 in `EmberTern.Office` (flowing to App through
the project reference), plus test-only `SixLabors.ImageSharp` → 2.1.11 and
`System.Security.Cryptography.Xml` → 8.0.4 so the scan is green solution-wide and a future CI gate has
no pre-existing failures to be waived past.

Two mechanical lessons, both in gotcha #278: promoting a transitive pin to a **direct** reference puts
it under `NU1902`, which `TreatWarningsAsErrors=true` turns into a **build failure** — desirable, and
the reason the version must be genuinely patched (ImageSharp 2.1.10 fixes only one of its two
advisories; 2.1.11 fixes both). And an XML comment cannot contain a double hyphen, so a `.csproj`
comment cannot quote the `dotnet list package` flags that find the problem.

Scan after: all five projects clean.

---

## A-01 — the DDL change-safety gate

### What the audit found, and the two things it missed

Confirmed: `ExecuteCompileAsync` builds SQL and calls `DdlExecutor.ExecuteAsync` with no comparison
against the database. There was no fingerprint, version or conflict concept anywhere in the repo.

**Missed #1 — the risk is specific to whole-object replacement.** Procedure/Function/Trigger/View/
Package emit `CREATE OR ALTER … AS <entire body>`; that is where a colleague's newer version disappears
without a trace. Domain/Exception/Index/Generator are **diff-based** (`BuildCompileSql` returns only
what the user changed, empty diff = no-op), and Table Detail emits incremental `ALTER TABLE` from a
pending-changes buffer — there a conflict mostly *fails at the server* instead of silently overwriting.
This scoped the gate to where user source code is at stake.

**Missed #2 — a second path that needs no concurrency at all.** The New-object template is literally
`CREATE OR ALTER PROCEDURE NEW_PROCEDURE`, and `BuildFullSource()` always emits `CREATE OR ALTER`. So:
New → type a name that already exists → Compile → **the existing object is silently overwritten**, with
no "already exists" error, because `CREATE OR ALTER` is overwrite by definition. One user and a typo.
Arguably likelier than the audit's two-session scenario.

### Mechanism, and why it is the only one available

Firebird's catalog has **no** change counter or modification timestamp for a routine — no
`RDB$UPDATE_TIME`, no row version. Re-reading the definition and comparing is therefore the only
mechanism the engine offers, not one option among several. That is worth stating because it forecloses
the "just use a version column" review question permanently.

- `ObjectChangeSafety` (Core, pure) — `Fingerprint` (SHA-256 of the definition) plus **two** decision
  functions, deliberately separate because the flows rest on different evidence:
  `EvaluateOverwrite(baseline, current)` and `EvaluateCreate(nameIsTaken)`.
- Verdicts: `Safe`, `ChangedInDatabase`, `AlreadyExists`, `Unverifiable`. **Unverifiable is not
  permission** — rule #11's "uncertainty ⇒ do nothing or ask".
- `ObjectChangeGate` (App) — holds the baseline for one tab, calls back into the caller's own reader,
  and owns the ONE verdict→message mapping so eight editors cannot describe a conflict eight ways.

**A hash rather than the text**, for a reason beyond size: it is structurally incapable of being
mistaken for content, so a later change cannot fall back to the baseline as though it were source code.
**Byte-exact, unnormalised**, because a whitespace-only change to a body *is* a change to the user's
code.

### The design correction found mid-implementation

The first plan used "the reconstruction returned nothing" as the existence test. Reading
`BuildProcedureDdlAsync` showed it never returns nothing for a missing routine — it synthesizes a
well-formed stub with a `/* Procedure source not available. */` body (**measured: 106 characters**). A
fingerprint-based existence test would have reported every non-existent object as existing and inverted
the New guard into a permanent refusal.

So existence came from a new `FirebirdMetadataReader.ExistsAsync`, built deliberately over the **same
`ListAsync` the object tree uses** — "does this name exist" carries the per-kind table choice, the
`SYSTEM_FLAG` predicate, the client-side system-name filter and the FB3+ packaged-routine exclusion, and
a second query would be a second definition of existence free to drift. A gate that disagrees with the
tree about what exists is worse than no gate. Cost: one name list per New compile, on a deliberate,
infrequent action. Gotcha #279.

The same correction removed a verdict: `RemovedFromDatabase` is unreachable (a dropped object simply
produces a different reconstruction and lands in `ChangedInDatabase`, which refuses for the right
reason), so it was deleted rather than shipped as a state nothing can produce — #233.

### Where it is wired

- Procedure/Function/Trigger — via `SourceObjectDetailTabViewModel`. Each subclass now implements
  `ReadDefinitionAsync`, and its load routes through `LoadDefinitionAsync`, which **drops the baseline
  before reading and captures it after** — loading and arming the gate are literally one act, so they
  cannot drift, and a failed reload leaves the gate disarmed rather than stale.
- View and Package carry their own gate (View is deliberately not on that base — no PSQL body, params or
  variables; Package composes header + body into one comparable artifact with a separator, so a change
  moving text across the boundary cannot produce an unchanged string).
- Debugger Save — see below.
- Checked **last** in compile, after every cheap refusal (it costs a round trip, and the earlier
  refusals have settled wording that tests pin), but **before** `ExecuteAsync`, which auto-commits.
- In the debugger, checked **before** the session-ending confirmation: a refusal must cost the user
  nothing, and refusing after tearing down a session would destroy a debugging session for a write that
  never happened.

### The debugger's baseline is not the Draft model's baseline

`DebuggerTabViewModel._baseline` already means "what the database holds" — but after a successful Save
it becomes **the text that was sent**, which is right for dirtiness and wrong for change safety: reading
the routine back yields the catalog reconstruction, not the user's typing. Fingerprinting the sent text
would refuse every *second* Save as a phantom conflict. So the gate re-reads through the same provider
that armed it (`CaptureChangeBaselineAsync`), and a failed re-read disarms rather than staling. The
object editors avoid this by accident — their Compile ends in `RefreshAsync()`, which re-baselines for
free. Gotcha #281.

### No force-overwrite in v1 — and why none was needed

The user asked for the possibility to exist without being built now. It does: the verdict and the
message are the only things a force path would need, both at the call site. And the escape hatch already
exists — **run the statement in the SQL Editor**, where the console makes it unmistakably the user's own
decision. Every refusal message says so.

### Live verification (`tools/probes/ChangeSafetyProbe`, 19/19 on FB5)

The gate rests on one claim that could have made it *worse* than the hazard, so it was measured rather
than assumed:

- **The reconstruction is deterministic** — two reads byte-identical, stable over 20 consecutive reads.
  Without this, every compile would report a false conflict.
- **A real change is detected** for a body change, for a **signature-only** change (byte-identical body,
  `INTEGER`→`BIGINT`; this is what fingerprinting the reconstruction rather than `RDB$PROCEDURE_SOURCE`
  buys), and for a DROP by another session.
- **Existence** behaves, including the trap: a dropped procedure yields 106 chars, not empty.
- **Cost: 2.1 ms** per overwrite check, **1.2 ms** per create check.

---

## A-03 — settings load health

### The confirmed path

`Load()` returned `null` for a missing file **and** for an undecryptable one. All eight facades do
`Load() ?? new ApplicationSettings()` then `Save()`. And `ExistingFileIsFromFuture` contained an
explicit `catch (Exception) { return false; }` commented *"Corrupt / undecryptable with a known scheme →
not a future file; allow the replace"*.

So on any machine where DPAPI cannot decrypt — a copied Windows profile, a restored account — the next
write replaced connection profiles, **passwords**, saved queries, workspace and watches with defaults.
The triggering writes are the ones nobody thinks of as writes: a grid column resized
(`GridProfileStore`), a procedure run recording parameters (`ParameterHistoryStore`), the app closing
(`WorkspaceStore`). **P0 by impact, not P1.**

The class comment asserted the opposite — *"Load degrades to null in that case and never overwrites the
unreadable file"* — literally true of `Load` and precisely the sentence that stops a reader from
checking `Save`.

### The model

`SettingsLoadStatus` = `Missing` / `Loaded` / `Unreadable` / `Corrupt` / `FutureVersion`, exactly the
distinction the user asked for; `SettingsLoadResult` carries it with `CanSave` and `NeedsAttention`.
`Load()` keeps its signature (all eight facades unchanged) and delegates to `LoadWithStatus()`.

**The safety property is on the write side**: `ExistingFileBlocksSave` refuses whenever the file on disk
was not fully understood. Classification is by **cause**, because the prognoses differ — an undecryptable
file is usually intact data belonging to another account, whereas a zero-length file (a killed mid-write)
holds nothing and stays safe to replace.

The old *"never strand the user forever on a genuinely broken file"* reasoning had the trade-off
backwards: being stranded is visible and recoverable; the overwrite it permitted was silent and final.
The answer to being stranded is `SaveOverUnreadableFile`, an explicit decision that **renames the old
file aside with a timestamp** rather than deleting it. Nothing calls it automatically, by design.

Secondary net: `AtomicWrite` now keeps the previous generation as `settings.dat.bak` — `File.Replace`
does this in the same atomic operation, for one filename and no extra I/O.

### And the user is told

Refusing quietly is only half the obligation: with saves refused, nothing the user does persists, and
silence makes safe behaviour indistinguishable from working. A **docked shared `MessageBanner`** on
MainWindow (new `Auto` row; body and status bar shifted from rows 1/2 to 2/3) carries the path and the
reason, `Warning` not `Error` — nothing is broken and nothing was lost, something is being *prevented*.
Dismissible for the session, and dismissal deliberately does not pretend anything was resolved.

Read once in the VM ctor, before anything in the session writes, so what the user is told reflects the
file they actually arrived with.

### The regression test has teeth — verified

`Save_RefusesToOverwrite_AnUndecryptableFile` was run against the restored pre-fix behaviour and
**failed**, along with one other; then the fix was restored. A regression test never seen to fail is a
hope, not a test.

---

## A-04 — the contract, corrected and pinned

The summary now states the real contract: **anything with a drift window goes through
`TextEditApplier`**, and the direct callers are named with the reason each has no drift window.

`DocumentMutationContractTests` pins the exact set of 13 files allowed to call
`Document.Replace`/`Insert`/`Remove` directly, each with a documented reason, plus two guards that keep
the list honest: no stale entries (an exemption outliving its file silently pre-approves a future file
reusing the name) and no exemption without a real reason. The failure message asks the reviewer the
right question — *which of the two kinds of edit is this?* — rather than just reporting a violation.

**Verified by adding a violating file**: the test failed and named the file and line. Then removed.

This is a tripwire, not a proof — a future caller who aliases the document to an unrelated name evades
the pattern, and the test says so rather than overclaiming.

---

## A-09 — a chip that could lie

`TransactionService.ResolveActiveProfile()` hard-returns `ReadCommitted` and documents why (a stored
legacy table-stability profile must never silently make the console WAIT). But the status-bar chips read
the **persisted** `DataTransactionProfile`/`MetadataTransactionProfile`.

Harmless by luck — both default to `ReadCommitted`. Not harmless in one reachable case: `Migrate_1_2`
copies a v1 file's single profile straight into `DataTransactionProfile`, so a user upgrading from v1
with "Table Stability" saw a chip claiming Table Stability while every transaction ran Read Committed.
A chip whose whole job is to say how the user's transactions behave must not be able to be wrong.

Fixed by reading the enforced value (new `TransactionService.EnforcedProfile`), which makes the agreement
structural rather than coincidental and stays true when the vestigial fields are eventually deleted.
Removed three dead `UiStrings` constants (`DialogField{,Data,Metadata}TransactionProfile`) that captioned
a connection-dialog field which does not exist.

---

## Closed out — the two deferrals are DECISIONS, not open questions

Both were reported at the end of the sprint for the user to decide, and both were **ratified on
2026-07-27 as deliberately not implemented**. The distinction matters for whoever reads this next: an
open question invites a future session to "just fix it", a ratified decision does not.

- **A-02 — Debugger Safety: not implemented, and the current behaviour IS the intended one.** In the
  user's framing: EmberTern *detects and clearly communicates* irreversible side effects but **does not
  block debugging**, and that is the product's philosophy rather than a gap in it. `DebugPreflight`
  already surfaces `IN AUTONOMOUS TRANSACTION`, `GEN_ID` and `NEXT VALUE FOR` (spec §4.6, "disclosed,
  not hidden"). Modes such as **Safe Simulation / Risk Mode** remain a **separate product decision for
  the future, explicitly NOT a fix** — which is the load-bearing half: they must never arrive as a
  bug-fix, or as a side effect of some other milestone.
- **A-07 — `ImportPipeline` `Complete`/`Abort` contract: not implemented.** The finding is real —
  `RunAsync` catches only `OperationCanceledException`, so another exception skips `CompleteAsync` and
  `TransactionLeftOpen` is never reported. But the App layer catches everything, reports it and drops a
  created table, so the user gets a message and a transaction they can roll back, never silent
  corruption. Data Import is closed and **its public contract stays closed**; this is recorded as a
  possible improvement to a **future version** of `ImportPipeline`, and on its own it does not justify
  re-opening a finished sprint.

## Also left open (pre-existing, scheduled elsewhere)
- **The charset audit, the app-wide UX sprint, the Metadata Explorer stage, the full-suite hang** — all
  pre-existing, all already scheduled elsewhere, none touched.
- **Test-only advisories** are now clean, but no CI SCA gate was added — that is S4 in the audit's own
  roadmap and needs CI that does not exist yet.
