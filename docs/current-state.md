# EmberTern — current state

> **This is the ONE place that answers *"what is done, and what are we working on"*.**
> `CLAUDE.md` holds the rules and the architecture; `docs/history/` holds the narrative of how we
> got here. Neither is the place to look for status.
>
> ⛔ **Keep this file between 100 and 300 lines.** It is a status board, not a diary. When a stage
> closes, its row here becomes one line and its narrative goes to `docs/history/`. If you are about
> to paste a multi-paragraph "shipped" report here, you are recreating the defect that produced a
> 6 849-line `CLAUDE.md` twice — see `docs/history/30-claude-md-current-state-archive.md`.

**Last verified: 2026-08-22** (the licensing module is CLOSED and merged to `master`).

---

## 0. ⏭ HANDOFF — read this first

> ⭐⭐ **THE LICENSING MODULE IS CLOSED (2026-08-22).** L1–L10 delivered, accepted and merged to
> `master`. The production key `R1` exists and ships its public half in `TrustedKeys.Production`; a real
> licence has been seen **`Valid` in a `Release` build**; a licence reaches a customer by e-mail, one at a
> time or in a throttled batch; and customers and licences can be removed administratively.
>
> ## ⏭ **NO STAGE IS IN PROGRESS. The next topic is a user decision.**
>
> ⛔ **Do not start anything from the licensing module.** It is finished, and §61 of its design document
> lists the six things left open **on purpose** — each one ratified, none of them a defect.
> ⛔ Do not close any of them "while we're here".
>
> ⭐ **Where the licensing knowledge now lives, so no next session has to read the whole history:**
>
> | You need | Read |
> |---|---|
> | The current state of the module — removal semantics, the final schema, what is open | `design/licensing-system.md` **§61** |
> | Why the bulk send is shaped the way it is | `design/licensing-system.md` **§60** (ratified specification) |
> | The ratified product decisions D1–D16 | `design/licensing-system.md` **§0** |
> | What L10 discovered, and the defects a real operator found | `history/35` |
> | The key ceremony, and how to repeat it for a rotation | `design/licensing-key-ceremony-runbook.md`; register in **§35.4** |
>
> ⚠ **Two suites live here** — `dotnet test EmberTern.slnx` (the product) and
> `dotnet test EmberTern.LicenseManager.slnx` (the issuer). The License Manager suite runs **serially**
> (`DisableTestParallelization`) because `Loc` is global static state — §57.9.
>
> ⚠ **Two RED tests in `EmberTern.Tests` are PRE-EXISTING and are NOT licensing defects** — §49.9:
> `CharsetGuardSeamTests.TheExcludedProjectsGenuinelyCannotReachTheFirebirdDriver` (matches the word
> `Firebird` in a COMMENT in the License Manager csproj — gotchas #396 / #412) and
> `DatePresentationTests.NoUserFacingSurface_FormatsADateInvariantly` (`RestoreWorkflow.cs`,
> `StorageViewModel.cs`). Both are named in `docs/gotchas.md`; ⛔ neither was introduced by L9 or L10.
>
> ⚠ **Findings from this module a next session should not rediscover** — all in `docs/gotchas.md`:
> **#394** an option's identity must not contain a label · **#396** every text-scanning guard reads
> `CodeOf(file)` · **#401** a template bound to a non-notifying item's property renders once and freezes ·
> **#403** `Loc` is global static state, so the suite runs SERIALLY · **#410** a function whose result
> nobody receives does not exist · **#412** a guard that reads the comment quoting the value it replaced ·
> **#414** `SmtpClient.Timeout` does not bound `SendMailAsync` · **#415** `open(path, "w")` truncates on
> OPEN · **#416** a programmatic restore keeps the backup's mtime, so the incremental build skips it ·
> **#417** `Progress<T>` delivers asynchronously · **#418** comparing by `ToString()` on a type that does
> not override it is a vacuous assertion.
>
> ### ⛔ Standing constraints the module leaves behind
>
> - ⛔ **A language is applied in ONE place** — `ApplicationLanguageService`.
>   `TheLanguage_IsAppliedInExactlyOnePlace` says so.
> - ⛔ **`ApplicationLanguages` and `MessageLanguages` are INDEPENDENT catalogs** and must never be
>   merged: the interface language is a fact about the OPERATOR, the message language about the CUSTOMER.
>   Defaults differ on purpose — **English** for the interface (D‑3), **Polish** for the message (D‑9).
> - ⛔ **Nothing is localized that is a technical contract**: persisted values, audit actions AND audit
>   notes, file names, ISO dates, branding. `design/terminology.md` §4.4 is the list.
> - ⛔ **`BrandEmberBrush` is identity, never a signal** — it may not paint a control state or a severity,
>   and a guard fails the build on a second consumer (`design/color-language.md` §1.3).
> - ⛔ **Never a version literal in either application** — `AppInfo` and `ManagerInfo` both read the build,
>   from the one `Directory.Build.props`.

---

## 1. Entry state

**Verified 2026-08-22 at the licensing module's closure, by running the commands rather than by recall.**

| | |
|---|---|
| Branch | ⭐ **`master`** — `feat/licensing-system` was merged back with `--no-ff` at the module's closure and is **kept**, locally and on both remotes, as the historical reference for L1–L10 |
| HEAD | the merge commit *Merge branch 'feat/licensing-system'*, over *docs(licensing): close License Manager module*. ⛔ A commit cannot name its own hash; `git log -1` gives the SHA |
| Sync | ⭐ **`master` == `origin/master` == `private/master`**, pushed to both at the closure |
| Working tree | ✅ **CLEAN** |
| Build | **0 warnings / 0 errors** — License Manager **Debug and Release**. ⚠ Measured before the closing documentation commit; ⛔ that commit changes no code |
| Tests | ⭐ **License Manager: 929 / 929**, measured 2026-08-22 at the closure. ⚠ **Measure, do not quote** — this row has gone stale at every stage of this module |
| Solutions | `EmberTern.slnx` (the product) **+** `EmberTern.LicenseManager.slnx` (the issuer). ⛔ Separate on purpose: the private key must never be reachable from a solution that ships |
| Version | **0.5.0** (`Directory.Build.props` — the single source; 0.x is deliberate) |
| Remotes | ⭐ **TWO, and both are kept on the same SHA**: `origin` → the company Gitea, `private` → the personal GitHub. The flow is **commit → push `origin` → push `private`** |

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
| ⭐⭐ **LICENSING SYSTEM V1 — THE WHOLE MODULE, CLOSED.** Offline licensing end to end: the signed `.etlic` artifact and its verifier, the License Manager (register, customers, licences, issuing, re-issuing, batch renewal, encrypted backup and restore), the product's activation surfaces, e-mail delivery one at a time **and** as a throttled batch, administrative removal of licences and customers, the production key `R1`, and a fully bilingual (EN + PL, live-switching) interface. ✅ user-verified against the user's own register | 2026-08-22 | `design/licensing-system.md` — **§61** is the as-built state, **§60** the ratified bulk-send specification, **§0** the ratified decisions; per stage §34–§59. Narrative: `history/33`, `history/34`, `history/35` |

---

## 2a. The audit follow-up etap — as accepted

⭐ **Closed, merged to `master` (`2c3da45`) and pushed to both remotes.** The full narrative — test
isolation, E (settings read-modify-write), the Avalonia headless race, Phase 4's debugger
irreversible-effects warning and its UX fix — moved verbatim to
[`history/32-audit-followup-2026-08.md`](history/32-audit-followup-2026-08.md) on 2026-08-15, when this
file went over its 300-line budget. ⛔ Nothing was deleted.

## 3. Open work

### Licensing system V1 — ✅ CLOSED, merged to `master`

⛔ **Nothing here is open.** The module shipped L1–L10 and its whole state lives in
`design/licensing-system.md` **§61**; the six items it left open are ratified and listed there
(§61.6) — the clock-rollback warning surface, key portability at the first real migration, the
unmeasured company mailbox, window size/position, `CompletedWithErrors`, and V2.

⭐ **V2 — online activation** remains a **planned next stage**, not a hypothesis, and ⛔ V1 deliberately
carries no code that only V2 would use (§3). It starts when the user decides it does.

⛔ **Two dependency decisions inherited from the audit follow-up's Phase 6**, both test-only, both held
back for this etap and both resolved as *stay*: **NPOI** stays 2.7.2 (2.8.0 is `OSMFEULA.txt` and demands
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
| **`Icon.Name` nie istnieje — kolumny i zmienne lokalne w completion są bez ikony** | ⚠ **Znalezione 2026-08-16 przez nowy `IconGeometriesSplitTests`, nie spowodowane przez ten etap.** `SqlCompletionData.cs:283,286` prosi o `"Icon.Name"` dla `SqlCompletionKind.Column` i dla lokalnych; jedyne wystąpienie tego klucza w `IconGeometries.axaml` jest **wewnątrz komentarza** pokazującego, jak dodać geometrię. Zmierzone przez odpytanie żywego systemu zasobów, nie przez czytanie pliku. ⛔ Nie naprawione tutaj: wybór glifu dla kolumny to decyzja projektowa do przeglądu użytkownika. Strażnik trzyma to jako **jedyny** wpis `KnownMissing`, z komentarzem, że drugi wpis oznacza błędną regułę, a nie kolejny wyjątek. |
| **`Calendar*` is not repinned in `FluentBridge`** | ⚠ Measured 2026-08-16: the bridge carries **zero** `Calendar*` keys, so every `CalendarDatePicker` popup in the product shows Fluent's own `SystemAccentColor` — the brown/orange the palette fights everywhere else. Affects EmberTern (`DebuggerTabView`, `ExecuteProcedureDialog`) and, since the L5.1 QA pass, the License Manager. ⛔ Not a License Manager defect and not fixable there: it is work in the product's bridge, for both applications at once. Ratified by the user as its own design-system item. |
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
