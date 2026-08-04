# tools/probes — developer verification tools

**Nothing here ships.** These are throwaway-by-design programs that answer *"what does Firebird
actually do?"* against a live engine, so a design decision rests on a measurement instead of an
inference. They are development aids, in the same spirit as `Lab/EmberTern_Lab.fdb` — not tests, not
infrastructure, and not part of the product.

They exist because of a rule this project learned the hard way (CLAUDE.md, *"Verify Firebird
behaviour, never infer it"*): three long-standing architectural beliefs were falsified by ~30 lines of
probe against the lab DB, and two of them were silent data-corruption vectors. A probe is cheap; being
confidently wrong about the engine is not.

## The rules

- **Not in `EmberTern.slnx`.** Its `<Project>` entries are explicit — no globs — so `dotnet build
  EmberTern.slnx` and `dotnet test EmberTern.slnx` neither build nor run anything here. Keep it that
  way: a probe needs a live server and a password, and **the test suite must never need either**. The
  suite is hermetic and finishes in seconds; that is a property worth protecting.
- **The password is never written to disk and never passed through a session.** Every probe reads it
  from the `ET_LAB_PWD` environment variable, set for one shell only.
- **Never touch `Lab/EmberTern_Lab.fdb`.** A probe that needs DDL creates a throwaway scratch database
  at an ASCII path (`C:\Temp\…`) and deletes it. The lab is a long-lived asset; a probe is not.
  (ASCII because `isql` cannot reach the repo's non-ASCII path — gotcha #149. The managed driver can,
  which is why the probes use it.)
- **Reference the real code.** A probe that re-implements what it is verifying proves nothing. These
  reference `EmberTern.Core` and use the shipped driver, so they exercise the same path the app does.
- **Delete or archive when the question is answered.** A probe's value is the finding, and the finding
  belongs in the design doc, `docs/gotchas.md`, or a unit test — not in a program nobody runs again.
  Keeping a stale probe alive is how a `tools/` folder rots.

## Running one

```powershell
$env:ET_LAB_PWD = "<local dev SYSDBA password>"
dotnet run --project tools\probes\SqlDataExportProbe
```

Each probe prints a `PASS` / `FAIL` / `....` (informational) report and exits non-zero on failure.

## Current probes

| Probe | Question it answers | Status |
|---|---|---|
| `DebuggerFidelityProbe` | The §F fidelity gate for **Stage X / D8** (nested stored-routine step-into, `docs/design/firebird-debugger.md` §15.6): does the REAL `FirebirdDebugExecutor` driven Step Into through a 3-level chain (`SP_DBG_ROOT → SP_DBG_MID → SP_DBG_LEAF`) reproduce real execution — call depth 3, argument seeding down each level, `RETURNING_VALUES` write-back up, and simulated `RESULT` == real `RESULT`? | **Active** — passes on FB5. Extend it (not replace) for D9 local routines / D10 triggers / D11 packages, which also need simulated-vs-real proof. |
| `Fb3ClosureProbe` | The spec **§6.3 version gate for Stage X / D9** (local procedures & functions): are PSQL sub-routines (`DECLARE FUNCTION`/`PROCEDURE` in `EXECUTE BLOCK`) **lexical closures over the parent frame** — reading (Q2), seeing-mutated (Q3), and writing (Q4) an outer variable? Raw `EXECUTE BLOCK` against a throwaway scratch DB on each instance (no EmberTern interpreter — this measures the **engine**). | **Answered** — FB3.0.13 = **NOT closures** (outer var rejected, SQL -206); FB5.0.3 = **closures, read+write by ref** (confirms §6.1). FB4 unverified (not installed). Recorded in `docs/design/firebird-debugger.md` §15.7. Keep to re-run when an FB4 instance is available. |
| `ScriptExecutorSequencedProbe` | The live-engine gate for the **Script Executor Rewrite — Step 4 seam B** (the Sequenced execution loop): does the REAL `FirebirdScriptExecutor` in `ScriptTransactionMode.Sequenced`, against a throwaway scratch DB, (A) run a mixed CREATE+INSERT migration end-to-end (gotcha #213 fixed by design), (B) still fail the same migration under `AutoCommitOnSuccess` (#213 bites the old single-tx mode — the contrast), and (C) on a mid-script failure keep earlier segments committed while rolling back only the failing one? | **Active** — ALL PASS on FB5 (2026-07-21). Keep to re-verify the loop after Step 5 (App) or any executor change; retire once Sequenced is fully shipped + covered. |
| `DataImportProbe` | The live-engine gate for **Data Import / etap I4** ([data-import.md](../../docs/design/data-import.md) §6). Unlike every other probe here it exercises **our production code**, not the engine: `ImportPipeline` → `FirebirdImportWriter` → `FirebirdImportErrorMapper`. Does every failure class map to the right `ImportErrorKind` **from the GDS vector** (never from message text), and does the report name the right SOURCE row — including across a batch boundary? Does `FbBatchCommand` behave as I0 measured (1:1 error indexing, `MultiError` ↔ `ImportErrorPolicy`)? Does the writer emit `OVERRIDING SYSTEM VALUE`, find a multi-action BEFORE trigger, and never auto-commit? | **Active — 20/20 ALL PASS** on FB5 `WI-V5.0.3.1683` (2026-07-26). It found a code I0 had not: a standalone `CREATE UNIQUE INDEX` violation leads with GDS **335544349**, not the `335544665` a PK/UNIQUE *constraint* reports (gotcha #260). **Keep** — this is the regression proof for the Firebird layer; re-run after any change to the writer or the mapper. *(It replaced `DataImportWriteProbe`, the I0 raw-driver probe, which was deleted once production code covered its ground.)* |
| `DataImportRunProbe` | The live-engine gate for **Data Import / etap I7** — the RUN path, where I4's `DataImportProbe` covered the WRITE path. Does a CSV import land exactly the rows the report claims (report == `SELECT COUNT(*)`)? Does Rollback take the whole import back **including** the "empty the table first" `DELETE`, which §4.5/D5 deliberately puts in the same working transaction? Does `FirebirdImportTargetPreparer` count what the transaction that will do the deleting sees, uncommitted rows and all? Does `BatchedCommitImportWriter` really commit every N — and does a later Rollback therefore fail to take those rows back, as §0.5 promises the user out loud? | **Active — 25/25 ALL PASS** on FB5 `WI-V5.0.3.1683` (2026-07-27). Grew with the module: **G** (I8, a table that does not exist yet) and **H** (I9, the same journey from a workbook — including a real Excel date cell surviving the round trip into a `DATE` column, and an `#N/A` cell being refused by a **VARCHAR** target, which is R20's sharpest case). It corrected one expectation: `CommitEveryRows` is a **floor**, not an exact multiple — a commit can only land on a flush boundary, so it fires at the first batch boundary at or past N (documented on the writer). **Keep** — re-run after any change to the writer, the preparer, the providers or the transaction modes. |
| `ChangeSafetyProbe` | The live-engine gates for the **DDL change-safety gate** (audit A-01, `ObjectChangeSafety`). One claim could make the feature WORSE than the hazard it prevents, so it is measured, not assumed: **is the DDL reconstruction deterministic** — do two reads of an unchanged routine produce byte-identical text? (If not, every Compile reports a false conflict.) Then: is a real change by another session detected, for a body change AND for a **signature-only** change (byte-identical body, `INTEGER`→`BIGINT`) — the half that justifies fingerprinting the reconstruction rather than `RDB$PROCEDURE_SOURCE`? And does the existence primitive (`FirebirdMetadataReader.ExistsAsync`) behave, including the trap that motivated it: the reconstruction **synthesizes a stub** for a missing routine instead of returning nothing? | **Active — 19/19 ALL PASS** on FB5 `WI-V5.0.3.1683` (2026-07-27). Byte-identical over 20 consecutive reads; a dropped procedure still yields 106 chars (so a fingerprint-based existence test would have been silently wrong — gotcha #279); cost 2.1 ms per overwrite check, 1.2 ms per create check. **Keep** — re-run after any change to `FirebirdDdlReader`'s reconstruction, which is the artifact the whole gate compares. |
| `SqlDataExportProbe` | The live-engine gates for **SQL Data Export** (`docs/design/sql-data-export.md`): does every `SqlLiteralWriter` literal round-trip through a real engine (E1); what are Firebird's actual string/hex literal ceilings, and does `long.MinValue` have a literal form at all (E1); what does `GetSchemaTable()` expose, and which column carries the declared `FbDbType` (E2); how is `RDB$IDENTITY_TYPE` encoded, and does `OVERRIDING SYSTEM VALUE` behave as documented (E3); is the multi-row `VALUES` constructor supported? | **Active** — E1/E2 are complete in code and waiting on this run. Retire once E1–E3 close. |

## MetadataPerfProbe

Measurement tool for the **metadata mechanism analysis** (2026-07-27, before etap I9). Prices the two
independent costs behind "the tree is slow": the CATALOG (real `FirebirdMetadataReader` against a scratch
database built to production size) and the PROJECTION (real `SidebarFlatController`, no Avalonia needed).

Part B needs no server at all. Part A creates and reuses `C:\Temp\embertern_metaperf.fdb`; the lab database
is never touched. Findings and the recommendation:
[docs/design/metadata-refresh-analysis.md](../../docs/design/metadata-refresh-analysis.md).

## `VisualCandidateProbe` — renders visual CANDIDATES for a §0.5 judgement

Product Polish M3.5. Renders a proposed visual change **beside the current state**, in both themes, to PNG.

⭐⭐ It exists because `color-language.md` §0.5 requires an answer to *"will the user recognise the action
FASTER?"* before any colour changes, and *"don't know"* is a refusal. Without a tool that shows the candidate,
the only available answer about reception is a guess — which is exactly what §0.5 forbids. This turns the guess
into a picture. In M3.5 it did so three times: geometric variants → badge variants → badge proportions.

⚠ **Candidates are defined IN THE PROBE, not in the product**, so running it deploys nothing.
⭐ A separate `z6-SHIPPED-*` render uses the **real control and its ControlTheme** and pulls the geometry
**from the application's resources by key** — because *"the variant looks good"* and *"this is what shipped"*
are two different assertions, and M3.3b paid for conflating them.

⚠⚠ Inherited limit (`TabStripVisualProbe`, §19.23.9): **it lays out ONCE, so it cannot rule on convergence.**
It answers "how does this LOOK", never "does this SETTLE".
⚠ It must merge the same dictionaries as `App.axaml` — a missing one does not fail, it silently removes an
element from the image.

Run: `dotnet run --project tools/probes/VisualCandidateProbe` → `tools/probes/VisualCandidateProbe/out/*.png`
