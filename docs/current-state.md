# EmberTern — current state

> **This is the ONE place that answers *"what is done, and what are we working on"*.**
> `CLAUDE.md` holds the rules and the architecture; `docs/history/` holds the narrative of how we
> got here. Neither is the place to look for status.
>
> ⛔ **Keep this file between 100 and 300 lines.** It is a status board, not a diary. When a stage
> closes, its row here becomes one line and its narrative goes to `docs/history/`. If you are about
> to paste a multi-paragraph "shipped" report here, you are recreating the defect that produced a
> 6 849-line `CLAUDE.md` twice — see `docs/history/30-claude-md-current-state-archive.md`.

**Last verified: 2026-08-15.**

---

## 0. ⏭ HANDOFF — read this first

> **Current milestone:** **Licensing system V1** — ✅ L1, L2, L3, L4a and **L4b accepted** (2026-08-15).
> ⭐ **EmberTern now licenses itself end to end**: verdict at startup, activation window, Settings ▸ Licence,
> About line, expiry banner, and every path that opens a database attachment gated through ONE seam, EN + PL.
> **Next task:** ⏭ **L5 — License Manager depth** (search, filters, group extend, re-issue, artifact preview,
> history view, encrypted backup + JSONL). ⛔ NOT started.
>
> ⚠ **L7 still owns the production key ceremony** — and until it runs, `TrustedKeys.Production` is empty and
> no real licence verifies as usable in any build. That is deliberate, and `Valid` / `Grace` are therefore
> proven by tests rather than by hand (§38.6).
>
> **Work lives on the branch `feat/licensing-system`**, cut from `master` at `2c3da45`.
> ⚠ **On this machine the remotes differ from the CLAUDE.md table and that is deliberate** (user, 2026-08-15):
> this clone is from the personal GitHub and has ONE remote, `origin` → `github.com/tamvel/EmberTern`. There is
> no `private` here. ⛔ Do not add one and do not rename `origin`. The company Gitea is synced from the work
> machine, by hand, later.
>
> Authority for every licensing decision: [`design/licensing-system.md`](design/licensing-system.md) — §0
> (ratified D1–D16), §32 (the L1–L7 plan), §34–§38 (as built).

⭐ The **audit follow-up** etap that this file used to hand off is **closed, merged to `master` and pushed
to both remotes** (`2c3da45`). Its two licensing-flavoured leftovers — NPOI's OSMF EULA and ImageSharp's
Split Licence — are now this etap's, see §3.

---

## 1. Entry state

| | |
|---|---|
| Branch | **`feat/licensing-system`** (cut from `master` at `2c3da45`), pushed to `origin` through L4b |
| HEAD | the **L4b closing commit** on `feat/licensing-system`, pushed to `origin` |
| Build | **0 warnings / 0 errors**, in **both `Release` and `Debug`**, in **both solutions** (`TreatWarningsAsErrors=true`) |
| Tests | EmberTern **9 081** · License Manager **102** — measured 2026-08-15, Debug and Release |
| Solutions | `EmberTern.slnx` (the product) **+** `EmberTern.LicenseManager.slnx` (the issuer). ⛔ They are separate on purpose: the private key must never be reachable from a solution that ships |
| Version | **0.5.0** (`Directory.Build.props` — the single source; 0.x is deliberate) |
| Remotes | `origin` (company Gitea) + `private` (GitHub) — **both** receive every accepted stage |

⚠⚠ **Build BOTH configurations before asking for a visual check.** `CLAUDE.md` runs the app from
`bin\Debug\`, and an etap built only in `Release` left the user verifying a binary that predated the
feature — reported as three UI defects that did not exist. Measured cost: one full review cycle.

⚠ **Test counts go stale every stage. Measure, do not quote.**

---

## 2. Closed stages

Everything below is delivered, user-accepted and merged. One line each; the pointer is where the
reasoning lives.

| Stage | Closed | Reference |
|---|---|---|
| V1 + V1.1 foundation, workspace persistence | 2026-05/06 | `history/01`–`05` |
| Table / View / Procedure / Function / Trigger / Package / Domain / Generator / Exception / Index editors | 2026-06/07 | `history/03`, `04`, `07`, `09` |
| Transactions & three connection lanes (Data / Metadata / Ddl) | 2026-07-14 | `history/05`, `13`, `15` |
| Data grids, filtering, aggregation, export framework | 2026-07 | `history/10`, `12` |
| Activity Monitor (Trace) · Session Manager · Performance Analysis | 2026-07 | `history/10`, `11` |
| Global Search · Script Executor · Smart SQL Parameters · Recompile Dependents | 2026-07-08 | `history/12` |
| Security Manager | 2026-07 | `history/09` |
| Editor language front-end (Etaps 0–6) + AST deepening (6.9) | 2026-07-15 | `design/editor-architecture.md`, `design/editor-ast-deepening.md`, `history/14` |
| Stage 7 Diagnostics · Unified Hover | 2026-07-16 | `design/editor-stage7-diagnostics.md` |
| Stage 8 M1 Structural Matching · Language Completion · Typing Ergonomics | 2026-07-16 | `design/editor-language-expansion.md`, `history/16`–`18` |
| Completion Matching (prefix-first IntelliSense) | 2026-07-17 | `history/17` |
| Stage Q Quick Fixes & Code Actions | 2026-07-25 | `design/editor-quick-fixes.md`, `history/20` |
| **Firebird Debugger** (P1/P2, D1–D13, D15, functions-as-root, Draft model) | 2026-07-25 | `design/firebird-debugger.md` + `-implementation-plan.md`, `history/19` |
| SQL Data Export (Copy as INSERT / UPDATE) | 2026-07-17 | `design/sql-data-export.md` |
| Data Import (I0–I12) | 2026-07-27 | `design/data-import.md`, `history/21` |
| Architecture hardening / product safety (DDL change safety, settings load health) | 2026-07-27 | `history/22` |
| Keyboard Manager / command system | 2026-07-28 | `design/keyboard-manager.md` |
| Hamburger navigation · About · Keyboard Shortcuts · third-party notices | 2026-07-29 | `design/hamburger-navigation.md` |
| Settings Center & SQL formatter casing (etaps 1–6) | 2026-08-01 | `design/settings-center.md` |
| Branding UX sprint | 2026-08-01 | `history/01` §"Branding UX sprint" |
| Colour language (4 systems, roles R‑1…R‑7) | 2026-08-03 | `design/color-language.md` |
| Avalonia 12.0.3 → 12.1.1 | 2026-08-05 | `design/avalonia-12.1.1-update.md` |
| Stabilization sprint (S‑1…S‑6) | 2026-08-05 | `history/24` |
| Grid consistency sprint | 2026-08-07 | `history/25` |
| Firebird grammar completeness sprint | 2026-08-08 | `history/26` |
| **Product Polish M0–M5** (tokens, base controls, colour, density, typography, final polish) | 2026-08-10 | `design/product-polish.md` §19 |
| Post-M5 UX package (points 1–6, incl. Database Properties) | 2026-08-10 | `history/27` |
| **Localization** — App + Core/Firebird (C0–C8) + full Polish translation | 2026-08-11 | `design/localization.md`, `history/28` |
| **Audit follow-up — test isolation** (global `Loc` state) | 2026-08-14 | commit `440c0ce` |
| **Audit follow-up — E: locked read-modify-write for settings** | 2026-08-14 | commit `972426e` |
| **Audit follow-up — Avalonia headless race: diagnosed, closed on our side** | 2026-08-14 | `avalonia-headless-session-race.md`, commit `b6f9e6b` |
| **Audit follow-up — Phase 4: debugger irreversible-effects warning** ✅ user-verified | 2026-08-14 | commits `1130e3d`, `1852611` |
| **Audit follow-up — Phase 5: charset guard** ✅ user-verified | 2026-08-15 | gotchas #372/#373, `tools/probes/CharsetProbe`, rule 12 in `CLAUDE.md` |
| **Audit follow-up — Phase 6: NuGet to latest stable** | 2026-08-15 | §3 below — 8 packages raised, 2 held for a stated reason |
| **Audit follow-up — Phase 7: `ARCHITECTURE.md` as-built** | 2026-08-15 | [`ARCHITECTURE.md`](../ARCHITECTURE.md) |

---

## 2a. The audit follow-up etap — as accepted

⭐ **Closed, merged to `master` (`2c3da45`) and pushed to both remotes.** The full narrative — test
isolation, E (settings read-modify-write), the Avalonia headless race, Phase 4's debugger
irreversible-effects warning and its UX fix — moved verbatim to
[`history/32-audit-followup-2026-08.md`](history/32-audit-followup-2026-08.md) on 2026-08-15, when this
file went over its 300-line budget. ⛔ Nothing was deleted.

## 3. Open work

### Licensing system V1 — ⭐ THE ACTIVE ETAP

Offline licensing (D1): a signed `EmberTern.etlic` artifact, ECDSA P-256, no backend and no mandatory
internet. ⛔ V2 (online activation) is a **planned next stage**, and ⛔ V1 carries no code only V2 would
use. Plan and acceptance criteria per stage: `design/licensing-system.md` §32.

| Stage | State |
|---|---|
| **L1** — `EmberTern.Licensing`: the ETL1 format and the verifier | ✅ accepted (`83d05a8`) — §34 |
| **L2** — `EmberTern.Licensing.Issuing`: keystore, issuer, key ceremony | ✅ accepted (`644f644`) — §35 |
| **L3** — License Manager: skeleton, SQLite register, customers, licences, issue, save | ✅ **accepted** — §36; ⚠ read §36.5 before any UI work here |
| **L4a** — mechanism: policy, location, store, service, text, clock, freshness, 4 gate guards | ⭐ **delivered, no UI** — §37 |
| **L4b** — surfaces: activation window, Settings ▸ Licence, About, banner, the connection SEAM, EN + PL | ✅ **accepted** — §38; ⚠ read §38.5 before touching a licence message |
| **L5** — Manager depth: search, filters, group extend, re-issue, preview, history, backup | ⭐ **in progress** — split into two sessions (L5.0–L5.2 read side, L5.3–L5.6 mutations + backup). **L5.0 delivered, awaiting acceptance**: schema v2, cross-customer query, history by subject, integrity check, atomic issuing batch — §39. ⛔ No UI yet, nothing committed |
| **L6** — e-mail: `ILicenseEmailSender`, SMTP + `.eml`, DPAPI settings, template, send audit | ⏳ not started |
| **L7** — hardening and closing: ⭐ **the real key ceremony**, public key shipped, docs | ⏳ not started |

⚠ **`TrustedKeys.Production` is empty and the REAL key ceremony has not been performed** — deliberately
**L7**, so no production private key is carried through five stages of development. A `Release` build
therefore refuses every licence today, and a test says so on purpose.

⛔ **Two licensing decisions inherited from the audit follow-up's Phase 6**, both test-only, both held
back for THIS etap to decide: **NPOI** stays 2.7.2 (2.8.0 is `OSMFEULA.txt` and demands
`<AcceptNPOIOSMFLicense>true</AcceptNPOIOSMFLicense>` — accepting terms on the owner's behalf), and
**SixLabors.ImageSharp** stays on the 2.x line NPOI supports (3.0+ moved to the Six Labors Split Licence).

### Audit follow-up — Phases 5, 6 and 7 ✅ CLOSED, merged, pushed

Charset guard · NuGet to latest stable · `ARCHITECTURE.md` as-built. Narrative moved out of this file on
2026-08-15: [`history/32-audit-followup-2026-08.md`](history/32-audit-followup-2026-08.md). The rules they
produced live in `CLAUDE.md` (architecture rule 12, the charset seam) and in `docs/gotchas.md` (#372/#373).

⛔ **Still open, deliberately out of Phase 5:** the UX of the read-side "cannot transliterate" message, and
whether `NONE` should stay in `CharsetCatalog.Supported` (lossy and machine-dependent — gotcha #373).

### Ratified but not started — each with a measured scope

| Item | Scope / why it waits | Reference |
|---|---|---|
| **Spacing stage** | 969 local `Spacing`/`Padding`/`Margin` values app-wide; `Padding` reads a role **zero** times. Ratified as its own stage; a guard already prevents growth. | `design/product-polish-m5-next-session.md` |
| **App-wide UX sprint** | Global control density (base controls sit on Fluent's 32 px) **+** monospace font consolidation — re-measured at **7 strings / 95 occurrences / 33 files**, so it decides `Cascadia Code` vs `Cascadia Mono` for every code surface at once. | `design/settings-center.md` §2.7, §7.1 |
| **`KindLabel` / `SymbolKind`** | ~8 Quick Info fact *values* that are our own words. A **contract** decision on `QuickInfoFact` (Core should hand up `SymbolKind` as data), not cleanup. ⛔ Do not declare kind keys in Core — App already owns that vocabulary. Cost today: a Polish reader sees *"Rodzaj: Table"* while the tree says *"Tabela"*. | `history/28` (C2) |
| **Find/Replace panel** | ⭐ **Ratified 2026-08-12: our own panel over AvaloniaEdit's search ENGINE** (this does not reverse `history/12`'s "no second engine" rule — that rule is about the engine, and the engine stays theirs). Forced by measurement: AvaloniaEdit 12.0.0 exposes **no localization seam** for `SearchPanel` — no `Localization` type, no message hook in its public API, and a `pl` satellite is impossible because the assembly is strong-named. 15 strings; the seams (`EditorSearch`, the command registry, `SqlEditorBehavior.Attach`) already exist as single places. ⛔ Three unknowns must be measured first — see §5. | `design/find-replace-panel.md` |
| **D14 — Step Back** | Debugger. Analysed, architecture ratified (snapshot + per-step savepoint + **undo-only**, no replay), deliberately **not** built. Returns only if real usage asks. | `design/firebird-debugger-implementation-plan.md` (D14) |
| **C3.4 — debugger root frame from AST header** | Would lift the "header must be byte-identical" gate on a draft-sourced session, resolve `TYPE OF`, and add the mandatory catalog-vs-draft fidelity case. Deferred pending real usage. | `design/firebird-debugger.md` |

### Localization residue

| Item | Note |
|---|---|
| ≈430 hardcoded strings | Not user-visible surfaces already migrated; the tail. `design/localization.md` §7 |
| 30 `(s)` hedges in the App catalog | Same class as plurals, solved by an English convention Polish does not have (`„8 wiersz(y)"` is wrong for 1 and 2). Own scope. |
| **#353 in Data Import** | The banner does not survive a language change — `SetStatus` stores rendered text and the module has no `RefreshLocalizedText`. Measured: **31 call sites, 18 with ready `UiStrings` text**, so reviving one path would leave 18 frozen statuses beside one live. A decision about `SetStatus`, not a migration leftover. |

### Deferred technical debt

| Item | Note |
|---|---|
| **`PreferencesService` holds a failed read for the whole session** | ⚠ **Found while implementing E (2026-08-14); deliberately left out of E's scope.** `PreferencesService` reads `_current = store.Load()` **once**, and `PreferencesStore.Load` turns any failure into validated DEFAULTS. If that one read fails transiently, the service serves default preferences for the rest of the session, and the next `Apply` persists them as if the user had chosen them. ⛔ `Update` does **not** catch this: by then the in-memory value is already wrong, so the write is entirely legitimate. Same shape as the defect E fixed, far narrower blast radius (the `Preferences` section only — never profiles, passwords or workspace). Needs `PreferencesService` to be able to tell "no preferences yet" from "could not read them". |
| **`SettingsPortability.ExportTo` can export defaults** | ⚠ Same 2026-08-14 finding, other direction. It is **read-only** (correctly classified out of E's Class A), but `_store.Load() ?? new ApplicationSettings()` means a failed read produces an **empty export file** while the user believes they took a backup. Not a `settings.dat` corruption, so not rule #11 — but it is a backup that silently isn't one. |
| ~~Charset silent data loss~~ | ⏭ **Promoted out of the backlog — it is now Phase 5, the next task.** The formerly unmeasured DDL/source path was measured and IS vulnerable. See §3 "Phase 5". |
| **Headless session init race** *(upstream — closed on our side)* | ⭐ **Root cause established 2026-08-14 and reproduced deterministically.** `EnsureIsolatedApplication` calls the process-wide `Dispatcher.ResetBeforeUnitTests()` on **every** `Dispatch`; a parallel thread constructing any Avalonia object claims `Dispatcher.UIThread` in that window and the session's `Compositor` then fails `VerifyAccess()`. Probe: **149/150 dispatches fail** with 4 noise threads, **0/150** without. Cost here: **1 test in ~1 run of 3–8**. ⭐ **It is NOT an EmberTern defect** and is identified by the STACK, not the test name. ⛔ **Five repairs measured and rejected** — no warm-up, no `Delay`, no retry, no global parallelism switch-off; do not attempt a sixth. Full evidence, the ready-to-file upstream report and the recognition signature: [`docs/avalonia-headless-session-race.md`](avalonia-headless-session-race.md); the "re-run once" rule is in `CLAUDE.md`. |
| Activity Monitor / Data Import width at 150 %/175 % DPI | Ratified as debt: both command bars are bare horizontal `StackPanel`s (~1130 DIP) with **no** `ScrollViewer`, so they clip rather than compress. Not a DPI defect — they do not fit at 100 % on 1366×768 either. |
| **B1** — `TableDetailTabView` private icons | PK/FK/Unique drawn with a raw `<Path>` over locally declared geometries on a **14**-unit grid, invisible to three mechanisms at once. Prepared and measured; appearance deliberately unresolved. |
| **Z‑3** — Table Data row height | A density question; cause must be found first (a taller row may be a deliberate readability decision). |
| Icon literal tail 10/11/13/15 | A question about roles, not a sweep. |
| `DataImportProbe` does not compile | **Pre-existing**, out of solution (so `dotnet build EmberTern.slnx` never compiled it); 20 checks of a closed module rotted silently. Data Import has a standing "return only for a real functional defect" directive. |
| `OfferRecompileDependentsAsync` is dead code | Was the intended consumer of `CompiledExistingObject`; reviving it changes what Save does. |
| `BindBareReference` ordering | A bare name in a query resolves to a **local** before a column; Firebird prefers the column. Worth its own measurement. |
| Grid column widths in settings export | Flushed only at window close, so an export can carry stale widths — same shape as the workspace defect fixed in Settings Center 5b, one layer further in. |
| `DatabaseApplyFailure.DatabaseInUse` | Lost its measured justification when Read Only left V1 scope; no longer provably reachable. |

---

## 4. Standing user directives

These outrank convenience and are not up for re-litigation. Full text in `CLAUDE.md`.

- **§0 / rule #11 — never lose information.** If EmberTern cannot prove it reproduces an object
  identically, it must not modify it automatically.
- **No autocommit, ever.** Auto-*begin* yes; auto-*commit* has no toggle and no setting.
- **One task at a time.** A cross-cutting problem found mid-stage goes to the backlog **with its
  measurement**, not into the current stage.
- **Root cause before symptom.** A report says *where* the user saw it, not *what* is broken.
- **Verify Firebird behaviour, never infer it** — use `Lab/EmberTern_Lab.fdb`.
- **Push to both remotes after every accepted stage.**
- **Measure before quoting a number.** Counts kept in prose have gone stale in this project
  repeatedly, and always in the direction nobody re-checks.

---

## 5. Where to read further

| You need | Read |
|---|---|
| Rules, architecture, gotchas | `CLAUDE.md` |
| Which document covers what | `CLAUDE.md` § "Documentation map" |
| Why a decision went the way it did | `docs/history/` (index: `docs/history/README.md`) |
| A familiar-looking bug | `docs/gotchas.md` |
| The pre-2026-08-11 status diary, verbatim | `docs/history/30-claude-md-current-state-archive.md` |
