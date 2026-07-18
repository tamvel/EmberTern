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
| `SqlDataExportProbe` | The live-engine gates for **SQL Data Export** (`docs/design/sql-data-export.md`): does every `SqlLiteralWriter` literal round-trip through a real engine (E1); what are Firebird's actual string/hex literal ceilings, and does `long.MinValue` have a literal form at all (E1); what does `GetSchemaTable()` expose, and which column carries the declared `FbDbType` (E2); how is `RDB$IDENTITY_TYPE` encoded, and does `OVERRIDING SYSTEM VALUE` behave as documented (E3); is the multi-row `VALUES` constructor supported? | **Active** — E1/E2 are complete in code and waiting on this run. Retire once E1–E3 close. |
