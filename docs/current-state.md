# EmberTern — current state

> **This is the ONE place that answers *"what is done, and what are we working on"*.**
> `CLAUDE.md` holds the rules and the architecture; `docs/history/` holds the narrative of how we
> got here. Neither is the place to look for status.
>
> ⛔ **Keep this file between 100 and 300 lines.** It is a status board, not a diary. When a stage
> closes, its row here becomes one line and its narrative goes to `docs/history/`. If you are about
> to paste a multi-paragraph "shipped" report here, you are recreating the defect that produced a
> 6 849-line `CLAUDE.md` twice — see `docs/history/30-claude-md-current-state-archive.md`.

**Last verified: 2026-08-21** (L8.5 accepted).

---

## 0. ⏭ HANDOFF — read this first

> ⭐⭐ **L8 IS THE ACTIVE STAGE, AND IT RUNS BEFORE L7 — 🔒 the user's decision (2026-08-20).** L8 localizes
> the License Manager's interface (English + Polski, chosen under ☰ → Settings → General, live) — sequencing,
> not priority: it touches many surfaces and is a UX change, so it closes before L7's security and
> production finalisation. ⛔ **L7 is NOT STARTED**, and ⛔ do not prepare the key ceremony.
>
> ## ⏭ START HERE: **L8.6 — visual QA in EN/PL × Light/Dark.** NOT STARTED. **Brief: §58.13.**
>
> ⭐⭐ **L8.6 is where a TERM MAY STILL BE CORRECTED.** `terminology.md` §4 was ratified as a BASE and the
> user reserved that right explicitly — so this stage judges the Polish in the real window, not in a
> catalog. §58.13 names what to look at first: **length** (Polish runs longer nearly everywhere, and the
> long explanations plus the fixed-width grid columns are where that shows), `Main.Held` → *Wstrzymane*,
> `Storage` → *Dane*, the `Row.Standing*` sentences under the column named *Termin*, and the `Filter.*`
> participles in a narrow picker.
>
> ✅ **L8.5 — the Polish interface.** §58. `Strings.pl.resx` with **392 entries** — the whole catalog, nothing
> left to fall back. **Plural families 7 → 12**, two arms in English and **three in Polish** (`one-few-many`,
> ⛔ never `.other` — measured in the product's own catalog). ⭐⭐ **The Application-language picker is REAL:
> D‑8 is discharged, not bypassed**, and the single apply path survived becoming two callers by moving into
> `ApplicationLanguageService`. ⭐ The dictionary was INHERITED — all thirteen §4.1 terms verified present in
> `EmberTern.App`'s Polish catalog before a word was written. Four checks, one per claim (§58.11); ⛔ no
> injection campaign, by the user's standing rule on proportionate verification.
>
> ✅ **L8.4 — ACCEPTED** (QA 2026-08-21). §57. **85 new keys, 12 new catalogs** (3 → 15), `Strings.resx`
> 294 → **379** with **0 pre-existing entries changed**; literals in `ViewModels` 92 → 11, in `Views` 8 → 0.
> **737/737**, twice in a row. ⭐⭐ **Both §53.6 obligations discharged** — `Capitalise(status)` is gone and
> the selected artifact survives a language change — **plus the twin the brief did not name**:
> `isCurrent ? "current" : "superseded"` printed persisted values too. ⭐⭐ **The finding: most of L8.4 was
> REFRESH, not translation** — a lookup makes the C# live and does nothing for the screen, and a `ComboBox`
> bound to `{Binding Label}` was **measured** not to re-read (hence `LocalizedCaption`). ⚠ Injections
> **4 of 12, 4 reds** — the campaign was stopped by the user as disproportionate; the other eight guards are
> green but unproven-on-demand. ⚠ `DisableTestParallelization` is on: `Loc` is global static state, so
> concurrent classes raced (~65 s → ~120 s).
>
> ✅ **L8.3** §56 · **L8.2** §55 · **L8.1** §54 · **L8.0/prep** §53 — the mechanism and the migrations, each
> closed and each proved mechanically. Findings worth not rediscovering: **#394** (an option's identity must
> not contain a label), **#395** (a BOM made `System.Text.Json` throw into a forgiving `catch`), **#396**
> (every text-scanning guard reads `CodeOf(file)`), **#399** (XML normalises the line ending before the
> attribute value), and L8.2's lesson that safety came from making the old shape UNCOMPILABLE.
>
> ⭐⭐ **THE RULE THAT GOVERNED L8.1 → L8.4, now discharged: NOT ONE USER-VISIBLE WORD CHANGED.** Proved
> mechanically each time, never by review — L8.4's instruments are in §57.2.
>
> ### ⛔ Standing constraints for the rest of L8
>
> - ✅ **The Application-language picker is REAL since L8.5 and D‑8 is DISCHARGED** — it stores to `ui.json`
>   and applies immediately, through the ONE seam (`ApplicationLanguageService`). ⛔ Never apply a language
>   anywhere else: two callers is how a stored preference and a rendered window start disagreeing.
> - ⛔ **`ApplicationLanguages` and `MessageLanguages` are INDEPENDENT catalogs** and must never be merged.
>   The interface language is a fact about the OPERATOR; the message language a fact about the CUSTOMER.
>   Defaults differ on purpose: **English** for the interface (D‑3), **Polish** for the message (D‑9).
> - ⛔ **Nothing is localized that is a technical contract**: persisted values, audit actions AND audit
>   notes, file names, ISO dates, branding. `design/terminology.md` §4.4 is the list.
> - ⛔ **Nothing in `EmberTern.App` or the product** — no product file touched, so its suite is not run here.
>
> ---
>
> ### Earlier milestones — closed, and their detail lives in the design doc
>
> ⭐ **L6 CLOSED (2026-08-19)** — a licence reaches a customer by e-mail, proved end to end against a real
> Gmail account in both languages. §48–§52; **§52.2 holds the four properties L6 must not lose.**
> ⚠⚠ **The company mailbox is still UNMEASURED** — proved on Gmail with an app password (§48.1); a tenant
> refusing basic auth is a NEW CLASS behind `ILicenseEmailSender`, ⛔ not a defect. ⏭ **Bulk sending is its
> OWN stage** (§14.1). **L1–L4b**: the offline loop end to end (§34–§38). **L5 CLOSED 2026-08-18**
> (§39–§47).
>
> ⚠ **Two RED tests in `EmberTern.Tests` are PRE-EXISTING and are not L8's** (nor L6's) — §49.9.
> `CharsetGuardSeamTests.TheExcludedProjectsGenuinelyCannotReachTheFirebirdDriver` (matches a COMMENT in
> the License Manager csproj — see gotcha #396) and
> `DatePresentationTests.NoUserFacingSurface_FormatsADateInvariantly` (`RestoreWorkflow.cs:352`).
>
> **Standing facts a next session must not rediscover:** §42.4, §46.11, §47.6, and now §48.1 (the Gmail /
> company distinction), §49.4 (`WithCulture="false"`) and §49.9 (the two reds).
>

> ⚠ **L7 still owns the production key ceremony** — until it runs, `TrustedKeys.Production` is empty and
> no real licence verifies as usable in any build. Deliberate; `Valid` / `Grace` are proven by tests
> rather than by hand (§38.6).
>
> **Work lives on the branch `feat/licensing-system`**, cut from `master` at `2c3da45`.
> ⭐⭐ **The remotes are back to the CLAUDE.md table, and BOTH are kept on the same SHA** (user's decision,
> 2026-08-21, on returning to the work laptop): `origin` → the company Gitea, `private` → the personal
> GitHub. The flow is **commit → push `origin` → push `private`**.
> ⚠ **This reverses the 2026-08-15 note**, which said this clone had ONE remote pointing at the personal
> GitHub — that was true of the OTHER machine, where L4b…L8.1 were written and pushed to GitHub only. Those
> eleven commits were fast-forwarded onto the company Gitea on 2026-08-21; ⛔ nothing was reset or forced.
>
> Authority for every licensing decision: [`design/licensing-system.md`](design/licensing-system.md) — §0
> (ratified D1–D16), §32 (the L1–L7 plan), §34–§52 (as built; §52 closes L6).

---

## 1. Entry state

**Verified 2026-08-20, by running the commands rather than by recall.**

| | |
|---|---|
| Branch | **`feat/licensing-system`** (cut from `master` at `2c3da45`), pushed to `origin` **through L6** |
| HEAD | *feat(licensing): L8.5 polska wersja jezykowa* — L8.5 in one code commit + one documentation commit. Beneath it: *docs(licensing): checkpoint po L8.4* and *feat(licensing): L8.4 localize C# presentation text* — L8.3 in ONE commit (views + resx + guard). ⛔ A commit cannot name its own hash, so this row names it by SUBJECT; `git log -1` gives the SHA. Beneath it: **`5ac1add`** *checkpoint po L8.2*, **`2d30fcf`** *L8.2*, **`f72b7b0`** *L8.1*, **`94ff665`** *L6 closed* |
| Sync | ⭐ **HEAD == `origin/feat/licensing-system` == `private/feat/licensing-system`** — pushed to BOTH on 2026-08-21; see the Remotes row |
| Working tree | ✅ **CLEAN** |
| Build | **0 warnings / 0 errors** — License Manager **Debug** and **Release**. ⛔ EmberTern not rebuilt: L8 has touched no product file |
| Tests | ⭐ **License Manager: 766 / 766** (0 failed, 0 skipped — 737 after L8.4, 709 after L8.3, 705 after L8.2, 685 after L8.1, 632 after L8.0/prep), and **two consecutive stable runs**. ⚠ The suite now runs **serially** (`DisableTestParallelization`) because `Loc` is global static state — §57.9; ~65 s → ~120 s. ⛔ **The EmberTern suite was NOT run**: L8 has touched no file of the product. ⭐ One product test WAS run punctually because it scans License Manager source: `DatePresentationTests` **9 / 10** — its one red carries **two** offenders, both byte-identical at `b012a0e` and absent from L8.4's diff (§57.8 corrects §49.9, which named only one) |
| Solutions | `EmberTern.slnx` (the product) **+** `EmberTern.LicenseManager.slnx` (the issuer). ⛔ Separate on purpose: the private key must never be reachable from a solution that ships |
| Version | **0.5.0** (`Directory.Build.props` — the single source; 0.x is deliberate) |
| Remotes | ⭐ **TWO, and both are kept on the same SHA** (user's decision, 2026-08-21): `origin` → the company Gitea, `private` → the personal GitHub. ⚠ This REVERSES the one-remote note L8.0–L8.1 carried — that described the *other* machine. See §0 |

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
| **Licensing L5.4** — bulk selection + batch renewal, licences list as EmberTern's grid ✅ user-verified | 2026-08-18 | `design/licensing-system.md` §46; gotchas #381–#383 |
| **Licensing L5 — CLOSED.** L5.5: encrypted verified backup, two restore modes, JSONL escape hatch, Storage window ✅ user-verified | 2026-08-18 | `design/licensing-system.md` §47; gotchas #384–#387 |
| **Licensing L8.0 / prep** — option identity taken back from labels (7 records), independent interface/message language catalogs, ratified EN → PL terminology. ⛔ Not one user-visible word changed | 2026-08-20 | `design/licensing-system.md` §53; `design/terminology.md` §4; gotcha #394 |
| **Licensing L8.5** — the Polish interface: `Strings.pl.resx` complete (392 entries), plural families with three arms, the Application-language picker made real (D‑8 discharged), and the inherited dictionary verified against the product | 2026-08-21 | `design/licensing-system.md` §58; `design/terminology.md` §4 |
| **Licensing L8.1–L8.4** — the localization mechanism (`Loc`, `{lm:Loc}`, `PluralRules`, `ui.json`), `StatusMessage` as key + arguments, 147 XAML values and 85 C# keys migrated, and the refresh path for every surface whose words are BUILT. ⛔ Not one user-visible word changed, proved mechanically each time | 2026-08-21 | `design/licensing-system.md` §54–§57; gotchas #395/#396/#399 |
| **Licensing L6 — CLOSED.** E-mail delivery end to end: **L6.1** SMTP settings + own-entropy DPAPI `smtp.dat` with four load states · **L6.1a** hamburger, Settings Center, PL/EN message language, template resolver, Customer/Licences split · **L6.2** `LicenseMessage` + a pure composer, the attachment byte-identical to `SaveArtifact` · **L6.3** `ILicenseEmailSender` with the SMTP and `.eml` senders, the Send licence window, `licence.sent` / `licence.send-failed`, and **Send test email…** · **L6.3a** the message the customer actually reads. ✅ **Proved against a real Gmail account**, PL and EN | 2026-08-19 | `design/licensing-system.md` §48–§52 |

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
| **L5** — Manager depth: search, filters, group extend, re-issue, preview, history, backup | ✅ **CLOSED 2026-08-18**, all six sub-stages accepted and pushed. **L5.0** data layer — schema v2, cross-customer query, history by subject, integrity check, atomic issuing batch (§39). **L5.1 + two QA rounds** — Licences view, search, three filters, own AppBar and title bar, the licence re-parenting defect, spacing and uniform control heights (§40–§43). **L5.2** — issuing history and artifact preview, current marked from the register's POINTER never from the ordering (§44). **L5.3** — re-issue with an operator-chosen reason, validated against a measured diff of the SIGNED payload (§45). **L5.4** — bulk selection and batch renewal, and the licences list rebuilt as EmberTern's own grid, LINKED rather than reproduced (§46). **L5.5** — encrypted verified backup with its own passphrase, two explicit restore modes with the previous register always preserved, the five-type JSONL escape hatch, and the Storage window (§47). ⚠ Read §47.6 before quoting L5.5's verification: it is deliberately narrower than earlier stages' |
| **L6** — e-mail | ✅ **CLOSED 2026-08-19**, all five sub-stages accepted. Delivery end to end: the SMTP settings and their own-entropy DPAPI secret with four load states (§48); the Settings Center, message language and Customer/Licences split (§49); a pure composer whose attachment is byte-identical to `SaveArtifact` (§50); `ILicenseEmailSender` with an SMTP and an `.eml` sender, the Send licence window whose preview IS the message, `licence.sent` / `licence.send-failed`, and **Send test email…** (§51); and the message the customer reads (§51.9). ⭐ **Proved against a real Gmail account** — §32's exit criterion satisfied. ⚠ The COMPANY mailbox is still unmeasured (§48.1) — a NEW CLASS behind the sender contract if it refuses basic auth, not a rebuild. ⛔ Bulk sending was deliberately NOT built; it is its own stage below. Closure: §52 |
| ⭐ **L8** — **localization of the License Manager's interface (EN + PL)** — 🔒 runs BEFORE L7, by the user's decision | 🚧 **IN PROGRESS. ⏭ Next: L8.6, NOT STARTED** (§58.13) — visual QA in EN/PL × Light/Dark, and the one stage where a ratified TERM may still be corrected. **L8.5 ✅ ACCEPTED 2026-08-21** (§58): `Strings.pl.resx` 392 entries, plural families 7 → 12 (three arms in Polish), `StatusMessage` learned to resolve a counted key, the Application-language picker is real and D‑8 is discharged, and the inherited dictionary was VERIFIED against the product rather than trusted. **L8.4 ✅** (§57) 85 C# keys, both §53.6 obligations, and the finding that most of it was REFRESH rather than translation. **L8.3 ✅** (§56) 147 XAML values. **L8.2 ✅** (§55) `StatusMessage` as key + arguments. **L8.1 ✅** (§54) the mechanism. **L8.0/prep ✅** (§53) option identity (#394). ⛔ **L8.1–L8.4 changed no user-visible word and it was proved mechanically every time; L8.5 is the stage that changed them all, on purpose.** Authority: §53 |
| **L7** — hardening and closing: ⭐ **the real key ceremony**, public key shipped, docs | ⏳ not started — ⛔ **and deliberately after L8** |

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
| **Bulk sending of licences** | ⭐ Ratified as its OWN stage by the user at L6's closure — deliberately left out of L6. §14.1 already holds the design and it is the part that must not be softened: the FULL recipient list on screen, ONE explicit confirmation, then a per-message report. ⛔ No silent bulk send. ⚠ The pieces exist — `LicenceDelivery` records one line per attempt, and the composer is pure — so this is a surface plus a policy, not new plumbing. | `design/licensing-system.md` §14.1 |
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
