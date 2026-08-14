# EmberTern — current state

> **This is the ONE place that answers *"what is done, and what are we working on"*.**
> `CLAUDE.md` holds the rules and the architecture; `docs/history/` holds the narrative of how we
> got here. Neither is the place to look for status.
>
> ⛔ **Keep this file between 100 and 300 lines.** It is a status board, not a diary. When a stage
> closes, its row here becomes one line and its narrative goes to `docs/history/`. If you are about
> to paste a multi-paragraph "shipped" report here, you are recreating the defect that produced a
> 6 849-line `CLAUDE.md` twice — see `docs/history/30-claude-md-current-state-archive.md`.

**Last verified: 2026-08-14.**

---

## 0. ⏭ HANDOFF — read this first

> **Current milestone:** Audit follow-up — **Phase 4 accepted** (user-verified in the running app).
> **Next task:** **Phase 5 — charset guard.** ⛔ Not started. Do not start it without the user asking.
>
> **Work lives on the branch `fix/audit-followup-2026-08`, NOT on `master`, and is NOT pushed.**
> Five commits, `440c0ce` → `1852611`. Pushing happens after the user accepts the whole etap
> (both remotes), which has not happened yet.

Remaining order, unchanged and untouched: **Phase 5 (charset guard)** → NuGet audit (stable versions
only) → `ARCHITECTURE.md` "as built" → final verification. ⛔ Licensing, Firebase, License Manager and
the installer are **out of scope** and belong to a later, separate etap.

---

## 1. Entry state

| | |
|---|---|
| Branch | **`fix/audit-followup-2026-08`** (cut from `master`) — clean working tree, **not pushed** |
| HEAD | `1852611` *fix(ui): etykieta "nie pokazuj ponownie" w calosci czytelna w kazdym jezyku* |
| Build | **0 warnings / 0 errors**, in **both `Release` and `Debug`** (`TreatWarningsAsErrors=true`) |
| Tests | **8 813**; last series **6 / 6 fully green** |
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

---

## 2a. The audit follow-up etap — as accepted

⚠ Delivered on `fix/audit-followup-2026-08`, **not yet merged and not pushed.**

**Test isolation (`440c0ce`).** The full suite failed 45 tests deterministically because
`Loc.LanguageChanged` is `static`: every view model any earlier test built stayed subscribed, and the
next test to swap the catalog broadcast into all of them. Fixed at the source —
`Loc.IsolateSubscribersForVerification()` plus `IsolatesGlobalLanguageState` on `HeadlessCollection`, so
each headless test gets a clean subscriber list **automatically**. ⛔ The old three-partition manual
split is gone and must not return; it hid this for months. `DiagnosticsPanelViewModel` stopped
subscribing to the static event (it leaked one live VM per editor tab) and became an ordinary child of
the app's single long-lived subscriber. Two source guards keep both rules armed.

**E — settings read-modify-write (`972426e`).** Measured data loss: a facade doing
`Load() ?? new ApplicationSettings()` → mutate → `Save()` turned a *transient* read failure into
DEFAULTS and wrote them. Against a concurrent publisher: **182 failed reads, 89 of which wrote
defaults, ending with 0 of 5 connection profiles surviving** — profiles and passwords, silently.
`ApplicationSettingsStore.Update()` now takes the cross-process lock, reads **under it**, mutates and
writes via `SaveCore`. `Missing` is the ONLY status that may produce a default aggregate and this is
the only place that may; `Unreadable` / `Corrupt` / `FutureVersion` end the operation untouched.
15 call sites migrated. ⛔ Not a retry — the lock's scope removes the window.

**Phase 4 — debugger irreversible-effects warning (`1130e3d`, `1852611`), ✅ user-verified in the app:**

- detection of `IN AUTONOMOUS TRANSACTION` / `GEN_ID` / `NEXT VALUE FOR` reuses the existing
  `DebugPreflight`; `Scan` gained `out bool irreversible`, so **one scan** answers both the launch
  panel's sentences and the running view's bar and they cannot disagree;
- a **one-time modal** before launching risky code, with **"Nie pokazuj tego ostrzeżenia ponownie"**;
  Cancel really stops the launch;
- a **dismissible bar** at the foot of the debug view (shared `MessageBanner`, `Classes="docked"`) —
  the launch panel disappears when a session starts, so that is where the warning was missing;
- `BuildPreflight` runs on every Launch **and** Restart, so re-arming is automatic; dismissing is per
  run;
- ⭐ the preference silences the **modal only, never the bar** — pinned by a test;
- ⛔ **no safe mode and no blocking of valid SQL**: suppressing a generator or an autonomous
  transaction would mean refusing to execute correct SQL, against the debugger's fidelity law (§F).

**UX fix (`1852611`).** The suppress checkbox was clipped (*"Nie pokazuj tego ostrzeż…"*). Measured:
the label needs **358 px in English and 435 px in Polish** against ~380 px of content width — so a row
of its own is necessary and **still not sufficient**, hence the label also wraps. ⛔ The shared dialog
width (420, also `TextPromptDialog`) and the font size were **not** touched, and the wording stays as
accepted — the layout absorbs longer localizations instead. `ConfirmDialogLayoutTests` measures the
property ("nothing is cut"), verified red in both broken shapes before being accepted green.

---

## 3. Open work

⏭ **Next task: Phase 5 — charset guard.** Not started.

### Phase 5 — charset guard (the immediate next task)

**Measured on live Firebird 5**, with `WIN1250` as EmberTern's **default** connection charset
(`CharsetCatalog.Default`, `ConnectionProfile.Charset`): a character outside the connection charset is
stored as `?` **with no error at all**, even into a `UTF8` column. Confirmed on **all three** paths:

| Path | WIN1250 | UTF8 |
|---|---|---|
| bound parameter | `AB??CD` | OK |
| SQL literal in statement text (the F5 path) | `AB??CD` | OK |
| **DDL text → `RDB$PROCEDURE_SOURCE`** | `AB??CD` | OK |

⭐ The third line is why this outranks the rest of the backlog: EmberTern can **silently corrupt the
user's own source code**, which is architecture rule #11 — the project's paramount rule.
⛔ **Design ONE shared mechanism** (natural home `CharsetCatalog`, direction `EncoderExceptionFallback`)
covering all three paths — **not three independent patches**. Regression tests must exercise the real
paths, not only a helper.

### Ratified but not started — each with a measured scope

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
