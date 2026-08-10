# EmberTern — Claude Code Context

A modern desktop developer workbench for Firebird database developers, built with **.NET 9 + Avalonia 12**. Target users: ERP and backend devs who work daily with SQL, procedures, triggers, metadata, and transactions. Design philosophy: **less features, better experience; workflow quality over feature count; transaction-aware by default**.

Master prompt / V1 blueprint: `C:\Users\grzegorz.gronski\Desktop\embertern-claude-code-prompt.md`
Target UI mockup: `C:\Users\grzegorz.gronski\Desktop\UI koncepcja.png`

---

## Documentation map (read this first)

CLAUDE.md was, for a long time, both the project's operating manual *and* its diary — every
milestone got appended in full, and the file grew to the point where simply starting a new
session against it consumed roughly half the available context budget before any work began.
On 2026-07-11 it was split by a **Documentation Cleanup Sprint** into a small set of documents,
each with one job. Nothing was deleted — every word that used to live here still exists,
verbatim, in the archive below.

| Document | Job | Loaded automatically? |
|---|---|---|
| **`CLAUDE.md`** (this file) | Rules, current architecture pointers, a short "what's built" inventory, current state, live gotchas that matter to almost every session. | Yes — every session. Keep it short on purpose. |
| **`docs/design/editor-architecture.md`** | The SQL/PSQL editor's current architecture: components, public API, binding decisions, roadmap. Kept current — extend it, don't let it re-accumulate history. | Only when working on the editor. |
| **`docs/design/editor-ast-deepening.md`** | **Active implementation guide** for **Etap 6.9 — Structural AST Deepening** (the next foundational work: node inventory, migration contract, milestones B0–B5, debugger considerations, formatter convergence, progress matrix). | When working on the parser/AST/binder deepening. |
| **`docs/design/editor-stage7-diagnostics.md`** | **Active design/vision** for **Stage 7 (Diagnostics)** — engine, model, categories, pipeline, squiggles/panel/nav, milestones, post-Stage-7 Quick Fixes. Consumes Etap 6.9. | When working on Stage 7. |
| **`docs/design/editor-language-expansion.md`** | **FULLY DELIVERED design** for the code-writing experience that replaced Stage 8 M2 — both halves shipped + user-approved: **Language Completion** (construct completion by natural prefix, Tab + shown hint, grammar-armed, synchronous) + **Typing Ergonomics** (`begin…end` pairing on Enter, `()`/`[]`/`''` pairing, structural auto-indent; Enter stays normal), separate from IntelliSense. §3 documents the as-built ergonomics (incl. what is deliberately NOT done: paren alignment, `IndentLines`); §5 the arming gate; §9.1 the **one-responsibility-one-owner** rule (vocabulary *and* grammatical position). | When working on `Core.Sql.Language.Constructs`, `Core.Sql.Language.Ergonomics`, or the completion/ergonomics wiring. |
| **`docs/design/editor-quick-fixes.md`** | **DESIGN + as-built — Stage Q COMPLETE (Q0–Q5, 2026-07-25), user-confirmed.** The self-contained guide for the **Quick Fix stage**: the on-demand `QuickFixEngine` over the unchanged read-only diagnostics pipeline, the shared `CodeAction`/`TextEdit` currency, the ONE `TextEditApplier` (rename migrates onto it), the light-bulb + `Ctrl+.` surfaces, the v1 action set with what is excluded and why, and seams Q0–Q5 with the recipe for adding a fix. **Supersedes `editor-stage7-diagnostics.md` §12 and CLAUDE.md's older holding note** on two points (no `QuickFixes` on `Diagnostic`; hover stays read-only). | When working on Quick Fixes / code actions. |
| **`docs/design/firebird-debugger.md`** | **The debugger's behaviour authority (v2, decisions ratified 2026-07-17).** ⚠ *Its "nothing implemented" framing dated from the design phase and was corrected 2026-07-25 — the debugger is built (P1/P2, D1–D13, D15, functions-as-root, the Draft model); sections amended by delivery say so in place (§9.1, §9.3.1, §12.14).* Feasibility (Firebird has **no** debug API — verified), the Fidelity Law §F, the client-interpreter + `EXECUTE BLOCK` harness, harness declaration rules, frame savepoints, exception control flow, per-session connection + transaction, nested frames/call stack, local routines (no temporary metadata), cursor bridge, UI/UX, panels, reuse map, prerequisites P1/P2 + milestones D1–D14, Fidelity Boundaries, and a live-engine verification log. | When working on the debugger. |
| **`docs/design/firebird-debugger-implementation-plan.md`** | **The debugger's execution plan** — per-milestone briefs (P1, P2, D1–D14: cel/zakres/components/new types/deps/risks/DoD/verification), how to split sessions so each ends green + committable, the editor/transaction **danger zones**, and the **Developer Contract** (20 binding rules). The spec says *what*; this says *in what order and under what rules*. **D14 = ANALYZED + DEFERRED** (its STATUS block records the ratified snapshot+savepoint+undo-only architecture if ever revisited). | **Every debugger implementation session — read this + your milestone's brief first.** |
| **`docs/design/d15-debugger-experience-and-ide-polish.md`** | **DESIGN — D15 planning phase COMPLETE (2026-07-20); the next major stage, nothing implemented.** The self-contained implementation guide for **D15 — Debugger Experience & IDE Polish**: the **Presentation vs Feature** split, all seven milestones (D15.1 Editor Readability app-wide · D15.2 Toolbar + own SVG icon system + Error Bar · D15.3 Launch Experience · D15.4 Friendly Errors · D15.5 Inline Values · D15.6 Performance-integration · D15.7 Global UI Audit), per-milestone seams/DoD, ratified design decisions + rationale, priorities, dependencies, risks. A future session starts any milestone from here **without re-analysing**. | When working on any D15 milestone. |
| **`docs/design/data-import.md`** | **🔒 FROZEN ARCHITECTURE of the Data Import module — CLOSED and merged (2026-07-27).** How the module is built and why, in the present tense: one working surface with collapsible sections (deliberately NOT a wizard), one pipeline for every source, `ImportConfiguration` as the single representation of every user decision (so profiles are a foundation, not a future extension), the transaction model, §0's seven consequences, risks R1–R20, and what stays deliberately open with a reason for each. The etap-by-etap narrative lives in `docs/history/21-data-import.md`. | Before touching the import module — §0 and the relevant architecture section. |
| **`docs/history/21-data-import.md`** | The Data Import module’s **narrative** — etap by etap, why each decision went the way it did, the I12 close-out (1 M-row measurement, the UI audit, the last defect) and the closing table of what stays OPEN with a reason for each. Written in I12, when ~520 lines of it moved out of CLAUDE.md. | On demand — when you need the backstory on an import behaviour. |
| **`docs/design/data-import-i0-findings.md`** | The Data Import **measurement archive** (etap I0): what the engine and the libraries actually do — batch throughput and row-error attribution, GDS error codes, the silent charset substitution, `.xlsx` reading traps. Evidence for the „(I0)" notes in the design doc. | On demand — when an I0-derived decision needs its proof. |
| **`docs/design/metadata-refresh-analysis.md`** | **The Metadata Explorer's measurement archive + the plan for its own stage.** Why the tree feels slow (the catalog is ~164 ms off the UI thread; the *projection* was quadratic), the flow of build/refresh, the 20 `RefreshAsync()` call sites, and the three-layer recommendation. **§7 is the as-built**: Layer 1 shipped 2026-07-27 (1 424 ms → 2 ms) together with the targeted in-place tree update; **Layers 2 and 3 + the unmeasured startup cost stay open** for the Metadata Explorer stage after Data Import. | Before touching the metadata tree, and at the start of the Metadata Explorer stage. |
| **`docs/audits/embertern-full-audit-2026-07-26.md`** | An external full-repository audit (GPT Terra). **Read the verdicts in `docs/history/22-...` alongside it, never it alone** — the 2026-07-27 hardening sprint verified every finding against the code and several did not survive: A-02's P0 rating was rejected (a ratified design decision), A-04 was real only as a documentation defect, A-08 was declined, A-06 is historical — while A-05's mitigation and A-01's scope were both *understated*. | On demand, with the history file. |
| **`docs/design/keyboard-manager.md`** | **🔒 THE COMMAND SYSTEM'S ARCHITECTURE + AS-BUILT — sprint CLOSED and merged (2026-07-28).** The `CommandDescriptor`/`CommandCatalog`/`CommandRouter` design and *why the obvious alternatives do not work here* (§7), the user's **ratified shortcut map**, the as-built per etap (§11 registry · §12 shortcuts · §14 tooltips · §15 context menus · §16 consistency pass), the **collision report vs Windows/IDE conventions** (§13 — accepted costs, not oversights), and the original command/shortcut/menu **audit** (§1–§6) with the measured facts that constrain the design. | **Before touching `EmberTern.App/Commands`, any shortcut, a tooltip that names a key, or a context menu** — §7 and the relevant as-built section. |
| **`docs/design/settings-center.md`** | **🔒 SPRINT CLOSED — all six etaps delivered, user-accepted and merged to `master`. Design closed + ratified, ⭐ etap 2 (Core foundation, §12), ⭐ etap 3 (the Settings Center window + the complete General page, §13), ⭐ etap 4 (the formatter's two casing settings, §14), ⭐ etap 5a (the export FORMAT — Core only, §15), ⭐ etap 5b (the export/import UI + the non-destructive write into `settings.dat`, §16) and ⭐ etap 6 (the approved §7 settings — ratified Q9, §17) all DELIVERED.** ⚠ **§17 is the newest as-built** — the first non-string preferences + `PreferenceRange`, the blur-or-Enter numeric commit path, the Easy-mode migration out of `WorkspaceState`, and §17.5's measured correction (a `TextBox` does NOT claim Enter). ⚠⚠ **§2.7 and §7.1 were CORRECTED in etap 6 — the monospace font item left the sprint entirely** (7 strings / 95 occurrences / 33 files, not 4 / 10); do not re-add it here. ⚠ **§16.1 is the one to read before touching an import path** — the stale-snapshot trap and the measured list of in-memory holders; **§16.3** records the ratified live-session behaviour (⛔ the workspace-capture suppression must not become a setting). ⚠ **§15.1 records the one deviation from the etap brief — `aes256-passphrase` is deliberately NOT registered in `ResolveProtector`; read it before "fixing" that.** ⚠ **§14.1 corrects §2.2 on two measured points — read it before touching the formatter.** The self-contained guide for **Settings Center & formatter casing**: the full settings audit (what is persisted, what is a live UI control, what is a hard-coded constant in waiting), the ⭐ **measured facts** — the theme is *never saved* not "reset on restart" · the formatter has **no casing decision point** and cannot tell a keyword from an identifier · **localization is NOT built** (1 815 `const`s, so the ratified Language row is deliberately storage-only) · the export/import seam was reserved by name in `EncryptionSchemes` · ⚠ **`settings.dat` already carries the magic `EMBERTERN-SETTINGS`** (§6.3.1b — measured in etap 2, which is why the export gets its own, Q13) — the `UserSettings.Preferences` architecture, EmberTern's own **versioned encrypted export format** (magic · `ExportFormatVersion` · `SchemaVersion` · `AppVersion`, one job each), the **13 ratified decisions (§9)** + the standing "no features for the future" directive (§9.1), and the etap plan 2 → 3 → 4 → 5a → 5b → 6 (§10, all delivered). | **Before touching `Core/Settings`, the theme, `SqlFormatter` casing, or settings export** — §9 first, then §2, then §14.1 (formatter) / §15 (export). |
| **`docs/design/product-polish.md`** | **⭐ THE ACTIVE STAGE — Product Polish. M0–M3 COMPLETE and user-accepted; the §13.3 gate PASSED; M4 decisions (density + typography) 🔒 ratified; screen migration in progress — M4.1 ✅ accepted, ⏳ M4.2 awaiting visual QA.** ⭐⭐ **§19.40 is M4.2's as-built and its headline is worth knowing before planning any migration etap: the counted debt in the ten object editors was ALREADY zero, so the etap's product is a MEASUREMENT — including B1, an editor that draws its own icons with a raw `<Path>` and was therefore invisible to three mechanisms at once (gotcha #337).** §19.37–§19.39 are M4's decision blocks + M4.1. ⭐⭐ **§19 is M3's as-built — and §19.20 is its closing summary for the colour language: what shipped, rules R15–R17, traps 18–21, the four architectural decisions, and what stays open.** §17 (M2b closing summary) and §18 (M2c) are **historical** — read a specific subsection, never the whole thing. The stage's one document: the measured audit (§1 — 4 Release Blockers, 10 High, 7 Medium, 3 Low, 7 UX Debt), the user's ratified decisions **D1–D12** (§2), the three catalog rules (§3 — ⭐ *a token names a ROLE, never a value*), the full token catalog (§4–§10: spacing · heights 24/22/28 · 12 typography roles · surfaces · colour semantics · tab strip · Status Bar 2.0 · motion · WCAG AA targets), the guard test (§11) and the complete plan M2a→M5 with dependencies, DoD and risks (§13). ⭐ **§0.1 Persistent UI · §0.1.1 tokens are a means not the end · §0.1.2 Application Chrome is ONE surface** are principles that outrank the catalog. ⛔ **§13.3 is a quality gate that blocks M4 on visual judgement, not on green tests.** | **Before any Product Polish work.** |
| **`docs/design/color-language.md`** | **⭐⭐ THE COLOUR LANGUAGE — a PRODUCT document. 🔒 Accepted 2026-08-02, ROLLED OUT IN FULL and visually accepted 2026-08-03; zero open questions.** From now on it is a **reference, not a plan** — it outlives Product Polish and governs every new feature. Four independent systems (rodzaj · akcja · tożsamość modułu · hierarchia przycisku), seven action roles R‑1…R‑7, **named exceptions** (an exception with a written reason is the target state, not debt), and **§6**, the decision tree for colouring a NEW action. ⛔⛔ **§0.5 is an overriding gate — before changing ANY colour: "will the user recognise the action FASTER?"; "no"/"don't know" ⇒ stop and propose.** ⚠ Supersedes `product-polish.md` §7.5 entirely. ⚠ §0.4 (R14 tempo) is closed and historical — superseded by R15. | **Whenever you add an action or touch a colour** (§6 + §0.5). |
| **`docs/design/product-polish-m5-next-session.md`** | ⭐⭐ **THE STARTUP PROMPT for the next Product Polish session — paste it and go.** State after **M4 closed in full**, the M4 summary (what shipped · ⭐ the four written premises measurement refuted · the three stages that turned out to be *acceptance of deferred decisions* rather than sweeps), and — the reason to read it first — ⛔ **the next stage is a USER DECISION, not an obvious next item**: three candidates with measured scope (M5 Final Polish · the ratified spacing stage, 969 local values with `Padding` reading a role **zero** times · the app-wide UX sprint incl. the monospace font), plus the parked items each with its reason and the mandatory closing order. | **First, at the start of every Product Polish session.** |
| **`docs/design/product-polish-m4-migration-next-session.md`** | 🔒 **HISTORICAL (since 2026-08-09)** — was the startup prompt for the M4 screen migration, all of it now closed. ⛔ Do not plan from it: **four of its premises were refuted by measurement** (incl. its claim that `GridSplitter` was the one parked item M4.4 would meet — there is not one in any of the 25 windows). ⭐ Its main prediction was right, though, and is worth the read: M4.4 turned out to be the acceptance of orphaned §13.3 deferrals, in exactly the three files it named. | Historical only. |
| **`docs/design/product-polish-m4-typography-decision.md`** | 🔒 **The M4 typography decision — measurement, variants, ratified answer (A‑2 · B‑1 · C · D · K5 struck). Accepted 2026-08-08; a RECORD, not a plan.** ⭐⭐ Its finding is the one worth carrying: `Text.SectionHeader` (11 SemiBold) stood above `field-label` (12) in **all 19** canonical uses, so the header was smaller than the text it names — and **five views had independently refused the role**, 19 against 17 in an identical context. ⚠ §6 is the recommendation as it stood BEFORE the decision; the user overruled one item (K4 stays 13) and one was withdrawn after building it (the counter window). | On demand — before touching a text role or the collision register. |
| **`docs/design/product-polish-m4-next-session.md`** | 🔒 **HISTORICAL** — was the startup prompt for ENTERING M4. Its §5 records the ratified direction decisions D‑M4‑1…3, all now executed. ⛔ Do not plan from it; the live prompt is the migration one above. | Historical only. |
| **`docs/design/product-polish-m4-density-decision.md`** | 🔒 **The M4 density decision — measurement, variants, recommendation, ratified answer (A‑3 · B‑1 · C‑1 · D). Accepted 2026-08-08; a RECORD, not a plan.** ⭐⭐ Read it for the method as much as the answer: **three written premises did not survive measurement from the code** — „`Size.Icon` — 64 literals" described 164 of 355 icon declarations (191 declare nothing and take 16 from a `ControlTheme`), „K15 — 112 occurrences in 17 files" was really 44 in 13 of which 41 are ONE role, and Z‑3's „40 px" **does not exist in `src/` at all**. ⚠ §6 is the recommendation as it stood BEFORE the decision — kept as reasoning, never as instructions. | On demand — before touching an icon size, a grid row height, or the collision register. |
| **`docs/design/product-polish-m3-next-session.md`** | 🔒 **HISTORICAL (since 2026-08-04)** — was the M3 startup prompt. Describes closed work (M3 · M3b · the §13.3 gate · M3.5). ⛔ Do not plan from it; the live prompt is the M4 one above. Kept as the record of "why", not "what next". | Historical only. |
| **`docs/design/product-polish-m3-handover.md`** | ⭐⭐ **The self-contained entry point into M3**, read right after the prompt above. State · scope M3.1–M3.4 + M3b · rules **R1–R17** · collision register K1–K11 · the per-iteration procedure · **21 traps** · the iteration plan §10. | At the start of every M3 session, in full. |
| **`docs/design/product-polish-m2c-handover.md`** | **🔒 CLOSED — historical**, like the M2a/M2b ones. Was the entry point into M2c (the de-localization sweep). Its durable lessons live on in `product-polish.md` §18 and in the M3 handover’s rules and traps. ⛔ Do not plan from it. | Historical only. |
| **`docs/design/product-polish-m2a-handover.md`** | **🔒 CLOSED** — the M2a entry document, kept as the record of entering that etap. ⚠ Its §6 describes M2b in one line written *before* M2b existed; do not plan from it. | Historical only. |
| **`docs/design/localization.md`** | **🔒 LOCALIZATION / APP — STAGE CLOSED (2026-08-09). ⛔ Nothing translated yet; Core/Firebird (~280 user-visible) deliberately OUT of scope and is the NEXT stage.** Ratified **D‑1 (language changes LIVE, no restart — reversed mid-stage from "restart required")**, **D‑2 (`.resx`/`ResourceManager`; architecture rule #6's "no resx" half deliberately lifted)**, **D‑3 (Core/Firebird hand up `MessageKey` + arguments; raw *server* messages may stay raw, our own wrappers may not)**. ⭐⭐ §2.1 is the one to read before touching the binding: the standard single-object **indexer** design was built and MEASURED DEAD — neither `"Item[]"` nor `string.Empty` reaches an indexer binding in Avalonia 12.1.1, and it renders correctly on first load, so the failure hides. Hence one notifying object **per key**. ⭐ §5 records the as-built (2 030 catalog entries · 2 026 members migrated · 1 242 XAML sites · **0 of 2 033 values changed**, proven against a COMPILER-produced snapshot, not a re-parse) and §5.1 the two findings it produced. §7 lists what is still open (≈430 hardcoded strings, the `LanguageChanged` wiring, the Polish translation). | **Before any localization work.** |
| **`docs/design/localization-app-stage-handover.md`** | ⭐⭐ **THE STARTUP DOCUMENT for the Core/Firebird stage — read it first.** Entry state (branch/HEAD/remotes/build/test/smoke), what the App stage closed, what was deliberately left, the exact D‑3 recipe for migrating a Core producer, the binding rules, and the open user decisions. ⚠ Its §A partition filter now names **13** classes — it grows with every migrated module. | **First, at the start of every localization session.** |
| **`docs/history/28-localization-core-stage.md`** | ⭐ **The Core/Firebird stage's narrative, etap by etap (C0 audit, C1 `SessionHealthAnalyzer`, …).** Read it for the C0 result — the inherited *"≈280 messages"* was **~170–190**, because three of ten inventory rows described something other than what they claimed — and for the four measured constraints that shape every later module (`Diagnostic`'s value equality · sentence concatenation vs plural · `ex.Message` as a catch-all channel · the guard that scanned only one assembly). | When migrating a Core/Firebird producer. |
| **`docs/design/localization-readiness-audit.md`** | The pre-stage audit that scoped it: what was hardcoded and where, measured four ways. ⚠ Its §6 recommends "restart is enough" — **superseded by the ratified D‑1**; read it for the inventory, not for the decision. | On demand. |
| **`docs/gotchas.md`** | The **complete** gotcha catalog (**357 entries, #1–#368** — re-measured 2026-08-10 after Localization C8 with `grep -cE "^[0-9]+\. \*\*" docs/gotchas.md`; ⚠ the count is *not* max−1, because **numbers 303 and 304 are each used TWICE**, in different thematic sections, so a bare "#303" is ambiguous — 357 entry LINES over 355 distinct numbers; ⛔ re-measure with that command rather than copying this figure forward, it has disagreed with itself before), organized thematically. ⭐⭐ **#366–#368 came out of Localization C8, and #368 is the one to read before trusting ANY localization test while one language ships** — the dual form guarantees `ex.Message` renders exactly what the key resolves to in English, so a test asserting *"the surface equals `Loc.Format(...)`"* passes identically for a consumer that resolves the key and one that prints the fallback; measured by reverting the consumer and watching all nine tests stay **green**, and only a SWAPPED CATALOG separates them. **#367** is its guard-side companion: **a guard whose scope is a list of ASSEMBLIES fails asymmetrically** — the two guards that go red are the ones whose failure was visible anyway, while the one that simply *stops looking* (a declared key with no English entry → raw identifier on screen) stays green, and the plausible fix for the two red ones is to exempt them, which leaves the silent one blind in a state that looks resolved. **#366** is the tooling half: `sed -i` in Git Bash rewrote CRLF→LF across a whole file and `git diff --stat` hid it, because the repo normalises line endings — and `sed` cannot verify the repair either, since its output stream drops CR too. ⭐⭐ **#359–#360 came out of a user bug report on one routine, and #359 is the one to read before trusting a green formatter suite** — a formatter that DROPS a lexeme does not present as a formatting bug: §0's net reverts the statement to verbatim, so the feature merely appears to do nothing, and **every lexeme-preservation test stays green** (reverting preserves lexemes perfectly), which is why one `--` comment could freeze a 30-line procedure unnoticed; the assertion that can fail is *"the net did NOT fire"*. ⚠ Its companion cause: the shared corpus had **almost no comments**, so 2 000 corpus-driven assertions said nothing about a shape absent from the corpus. **#360** is the binder half: **a consistency suite that varies ONE dimension while holding another fixed reads as exhaustive and is not** — the suite written for *"coloured in FROM but not in UPDATE"* pinned all five DML kinds at the top level and pinned the routine body with a QUERY, so the crossing (kind × nesting) was never tested, and there sat a second dispatch that bound subqueries and **never declared the target** ⇒ DML in a procedure resolved no table at all. ⭐⭐ **#358 came out of Localization C5 and is the one to read before embedding ANY reference type in a value type** — a record's synthesized equality compares a COLLECTION member by reference, so `LocalizableMessage` (key + `IReadOnlyList<object?>`) was *not* value-equal, harmlessly for four etaps because nothing compared it, and dangerously the moment it was to live inside `Diagnostic`, whose value equality the diagnostics panel uses to skip rebuilding its collection and keep the user's selection — the panel would have churned on every debounce tick with a green build. ⭐ The transferable half is WHERE to fix it: give the CARRIER structural equality (one change, at the type that owns the property, serving every future embedder) rather than contorting the consumer with a fixed-arity field, which is an arity ceiling — a defect scheduled for later. ⚠ And structural equality has a precondition worth guarding: every element must itself be value-equatable, or a `char[]` silently restores reference comparison one layer down. ⭐⭐ **#357 came out of Localization C4b and is the one to read before writing a guard against a hazard you have not reproduced** — the dual-form pattern's equality guard has an unstated precondition (both halves must format each argument the same way), and the mechanism written down for how it breaks was **false**: a bare `{0}` does NOT group an `int` under `pl-PL`, so planting the numeric argument left every guard green. ⭐ The real lever is a **`{0:N0}` specifier in the resource value** — which is where #354's `48 102` actually came from — so the hazard lives with the TRANSLATOR, not the machine. ⚠⚠ Its transferable half is about the test rather than the number: `TheEnglishAndLocalizedForms_AgreeOnAnyCulture` looked rigorous (four cultures × every message) and measured nothing of what it promised; only a plant revealed it, and the replacement had to measure the MECHANISM (serve a hostile `{0:N0}` template and require the argument verbatim) instead of restating the rule. Same shape as **#342**, where a plant that did not fire disproved the premise scoping a whole iteration. ⭐ **#351 came out of the post-M5 package's point 5 and is the one to read before "fixing" a shared style that looks wrong at ONE site** — `Border.settings-group` states in its own comment that it is a recessed surface *inside PanelBrush chrome*; three of its four consumers really do provide that chrome and the fourth provided none, so the card painted itself onto an identically-coloured host and read as *"the style is too weak"*. ⚠ Strengthening the style would have been applied at the only site where the style is innocent and would have broken the three that were correct. ⭐ The rule: **a style that describes its own figure/ground pair is making a claim about something it does not own**, so check the host's container before touching the style — #340's split of custody, applied to appearance instead of to decisions. ⭐⭐ **#349–#350 came out of the post-M5 UX package's point 4, and #350 is the one to read before cleaning up after ANY rejected experiment** — an untracked file that is deleted has **no safety net in git** (`git checkout` restores to HEAD, i.e. the state before the whole unmerged stage), so a cleanup removed an already-ACCEPTED mechanism and recovery was impossible: empty `git log --all`, no dangling blob, assembly rebuilt. ⭐ The rule: **commit an accepted stage before experimenting on top of it** — an uncommitted approval is exactly the window in which a cleanup cannot tell what you approved from what you rejected; and a cleanup instruction inherits the premise it was written under, so whoever holds the dependency must say so BEFORE executing. **#349** is its design-side companion: **a token's ROLE decides which contrast threshold applies**, so re-using an icon colour (floor 3:1) as 11 px TEXT (floor 4,5:1) silently moves the goalposts while the render still looks fine — #345 one step further out, because measuring the element is necessary but you must also ask which RULE it is now subject to. ⭐⭐ **#347 came out of the post-M5 UX package and is the one to read before trusting that a style variant's colour reaches its content** — a variant that overrides `Foreground` on the CONTROL and on its explicit `TextBlock`/`SvgIcon` children still loses to the template's setter on the `ContentPresenter`, and it loses **only for plain-STRING content**, so the same variant looked correct on Execute and painted Save nearly black on hover (**2,04:1** in Light); ⭐ the mechanism was broken in **both** themes and Dark passed at 5,62:1 purely because its `ForegroundColor` is near-white, so "it looks fine in Dark" was a coincidence that would have turned one variant defect into an endless per-theme patch. ⚠ Its second half is about the guard: the disabled state has the same shape but is **deliberately dimmed**, so the obvious "every state clears 4,5:1" test turns a correct product red and invites undoing a ratified decision — #322 committed inside the guard written to prevent it. **#348** is its companion from the same package: **a missing REGISTRATION fails as silently as a missing resource dictionary, one layer further out** — a probe rendered the "after" column with no colour because the XSHD definitions are registered by `App` and the probe runs `ProbeApp`, producing a perfectly plausible image that answers a different question. ⭐⭐ **#346 came out of M5/M‑3 and is the one to read before acting on ANY inventory of missing UI** — such an inventory answers *„is there a message here?"* and never *„can this state occur?"*, and the two produce different worklists: thirteen apparent gaps collapsed to four, two of them because the state CANNOT HAPPEN (a users grid over `SEC$USERS`, which always holds SYSDBA; a view's column list, when a view always projects a column), so building them would have shipped UI nobody can see. ⭐⭐ Its sharpest half: **a collection's NAME is not evidence of what its grid shows** — „No privileges in this category." sounded obviously right and was false, because that grid enumerates OBJECTS with privileges as cells; three proposed texts in a row were refuted the same way. ⚠ The failure mode is not a wrong number but a grammatical, plausible sentence describing something other than what the user is looking at. ⭐ Practical order, the reverse of the intuitive one: **establish WHEN the state occurs, only then decide what it says.** ⚠⚠ And a ready-made constant is not a ready-made answer: an orphaned `*Empty*` string quoted a button label that does not exist on screen, and had survived the product's whole life precisely BECAUSE nothing used it. ⭐⭐ **#344–#345 came out of M5/§10 and #344 is the one to read before leaning on ANY norm cited in our own docs** — a threshold row read *"≥ 3:1 — WCAG AA Large"*, and the **number was a defensible in-house choice while the attribution was false** (WCAG means 24 px or 18,7 px bold; the largest role here is 23 px, so nothing qualified). ⚠ It nearly decided a product change: one candidate fix satisfied the document *as written* and no external standard, and would have re-weighted every message in the app on a citation that did not hold. ⭐ The asymmetry is the lesson — **a number invites scrutiny, a norm label invites deference**, so the label is exactly the part that rots unnoticed, and it rots toward *more* confidence. #345 is its companion at one layer earlier: **measuring a token instead of the thing that paints** produced a reported failure for a pairing the product cannot render (a `ShowSeverityMarker` gate makes half the severity × surface grid unreachable) — and it surfaced only when the guard had to name a production property instead of a transcribed map. ⭐⭐ **#343 came out of M4.4 and is the one to read before replacing N local values with one mechanism** — a hand-set value can be doing TWO jobs, and a mechanism that takes over the property silently discards the one it never knew about: `GrowingDialogBehavior` assigned `MaxHeight` unconditionally, so attaching it would have raised a dialog’s deliberate 720 to 1008 on an ordinary 1080-tall screen, because the literal meant both „never exceed the screen” (which the mechanism does better) and „do not grow past a comfortable size on a huge monitor” (which it does not do at all); the fix is `Math.Min(declared, screen)` and it is provably a no-op for the existing consumers. ⚠⚠ Its general half: **before generalizing N local answers, ask what each was FOR** — „they are all guessed constants” is a hypothesis about their origin, not a measurement of their purpose. ⭐ Its companion, same iteration: a ceiling WITHOUT a scroll surface does not bound content, it CLIPS it, so „attach the behavior” is not a portable one-liner. ⭐⭐ **#342 came out of M4.3c and is the one to read before hoisting a style into the shared sheet — or before trusting a rule about style priority** — Avalonia resolves two competing STYLES by selector specificity, **not** by document order (measured: a base `Button` style setting `Padding` and placed AFTER `Button.seg` did not override it), so „moving a style changes its priority" is true only against a **local value on the element**; conflating the two produced a rigorous-sounding premise that scoped a whole iteration and was false. ⭐ Its transferable half is how it surfaced: **the plant did NOT fail the test, and that silence was the finding** — a plant usually proves a guard works; here it disproved the reasoning. ⭐⭐ **#340–#341 came out of M4.3, and #340 is the one to read before declaring ANY register closed** — a decision deferred in a CODE COMMENT is not an entry in the register, so closing the register „in full" leaves it standing, and it then reads to the next author as a settled state: the §13.3 gate decided **none** of the ~19 deferrals sitting in M4.3's five files (nor ~43 more across `src/`), because the deferral lives in the SOURCE while the register lives in a DOCUMENT and only the document ever gets closed; ⚠ the orphans are not scattered randomly — they cluster exactly where the migration counters still show a remainder, so *„why does this file still have local values?"* and *„which decisions were never taken?"* are the same question. #341 is its companion: one glyph can carry two roles (`Icon.X` is both an inline ✕ **and** the toolbar's close-tab ACTION, which correctly declares no size at all), so a rule grouped by the geometry's NAME is grouped by the wrong thing — an error committed **inside the guard written to prevent it**. ⭐⭐ **#338–#339 came out of M4.2b and both are about the gap between „the code is right" and „the feature works"** — a subclass of a templated control does NOT inherit its `ControlTheme` (Avalonia resolves it by CONCRETE type), so it renders an EMPTY surface while `ItemCount` and `DataTemplates` both look healthy; and „the rule is correct" + „the wiring exists" still leaves the question nobody asked — whether the event REACHES the handler, and whether there is still selection for it to act on (one keypress passed, two in a row did not). ⭐⭐ **#337 came out of M4.2 and is the one to read before trusting any "this file is clean"** — a counter keyed to the NAME OF A CONTROL cannot see the same thing built another way, so its zero means *"not built like that here"*, never *"clean"*: one editor drew PK/FK/Unique with a raw `<Path>` over locally declared geometries and thereby hid from the size counter, the `ControlTheme` default AND the centring audit at once; the failure mode is the friendly one — not a wrong number but an **absent row**, and an absent row reads as compliance. ⭐⭐ **#336 came out of M4.1's QA round and is the one to read before writing any guard that COMPUTES** — a guard must compute with the engine the product renders with; the headless test platform ignores an arc's bulge, so a geometry guard ran there was RED with precise numbers naming six icons that render perfectly, and acting on it would have shipped six new defects. ⭐⭐ **#335 came out of M4.1 and is the one to read before any value-keyed cleanup** — neither the VALUE nor the GEOMETRY NAME identifies a control, so a sweep keyed to either reports progress while the thing a user looks at stays inconsistent (one pagination bar carried two sizes across five screens through two prior sweeps); its practical half is *group the same control across screens and check it agrees with itself*, its second half was paid for by committing the error inside the entry itself (one geometry name serving two controls produced a false claim, retracted before any code changed), and its sibling finding is that `Spacing`/`Padding`/`Margin` had no counter at all, so files reporting zero debt held 985 local values app-wide. ⭐⭐ **#334 came out of M4's typography block and is the one to read before migrating exceptions onto a role** — when several independent authors refuse the same role, that is a MEASUREMENT of the role, not several oversights: the tell is the DISTRIBUTION (19 headers at one size against 17 at another, in an identical context), and a register that records only the deviating side makes a symmetrical disagreement look like one-sided drift. ⭐⭐ **#332–#333 came out of M4's density decision and both are about a value that lives OUTSIDE the place that claims to govern it** — a `ControlTheme` default is what 191 of 355 icons actually render at while the catalog's role describes two of them, and an unmeasured property is not "clean" but unmeasured (seven icon sizes, green build throughout); #333 is its mirror in the test suite — a guard that TRANSCRIBES a premise breaks when that premise moves onto a role, and reports something its own name does not describe. ⭐⭐ **#329–#331 came out of the language-sprint QA round (2026-08-08) and all three are about a UI element that speaks for a value it cannot fully express** — an editor showing only part of a type OVERWRITES the rest on commit; a display rule written against the CLR type cannot express the DECLARED type (and the tempting repair, "hide midnight", trades a visible defect for an invisible one); and a seed that deliberately shows less than the value holds turns a mere focus change into a write. ⭐⭐ **#322–#323 came out of the 2026-08-07 grid consistency sprint, and #322 is the one worth reading whatever you are working on** — a safety rule stated about a CLASS of things ("a data grid") can be false for every actual member of that class, and the test that pins it will look rigorous while protecting nothing, because a guard asserting a POLICY inherits every unchecked premise of that policy; #323 is a source guard whose fallback answered "yes" for exactly the thing it was written to catch. ⭐ **#321 came out of the Avalonia 12.1.1 update sprint** — a `>=` dependency range makes an untested combination look supported, and restore/build/tests are all silent about it. CLAUDE.md keeps only the ~20 most load-bearing ones inline; this is where the rest live. ⭐ **#316–#320 came out of the 2026-08-05 stabilization sprint** — a catalog read that resolves a domain destroys it on the next compile (and byte-identity passes while the catalog is wrong); an empty result meaning both "absent" and "not loaded yet"; the three measured `DataGrid` facts about Enter; a setting for a mode the product never selects; and a reported correlation whose variable was wrong. ⭐ **#313–#315 came out of the §13.3 gate and M3.5** — a variant's chrome cancellation losing to Fluent's `:disabled`; the two hard limits on a 24-unit icon box; and why a guard that reads a token instead of the painting element is green while the product is broken. | On demand — search it when a bug "feels familiar". |
| **`docs/history/`** | The full narrative archive — every milestone, session, and investigation, split into ~27 thematic files with an index (`docs/history/README.md`). This is the "diary" that CLAUDE.md used to be. ⭐ **`27-post-m5-ux-package.md` is the newest and is the one in progress** — the six post-M5 UX reports; read it for the phase-0 result (three of six described something other than what they looked like, and each time that changed the SHAPE of the work), and for #347/#348. ⭐ `24-stabilization-sprint.md` remains the sharpest on method: two of six reports were not what they described, plus the three shared causes and the fix that changed the debugger as a side effect. | On demand — read a file when you need the backstory on a specific feature or bug. |
| **`docs/design/*.md`** (other files) | Frozen feature-specific design docs (Script Executor, Execution Modes + Export Framework, the Etap-1 tokenization audit) — mostly already implemented; kept as reference. | On demand. |
| **`memory/*.md`** (Claude's persistent memory, outside the repo) | Cross-session recall — rules, gotchas, and project facts Claude chose to remember. `memory/MEMORY.md` is the always-loaded index; the individual files load only when relevant. | Index only, every session; files on demand. |

**Rule of thumb for future work**: if you're about to append a multi-paragraph "shipped"
narrative to CLAUDE.md the way the old file did, stop — put the narrative in a new
`docs/history/` file (or extend the most relevant existing one) and add at most a **one-line**
pointer here (or nothing at all, if the "Current state" section below already covers it). If
you're about to add a gotcha, put its full text in `docs/gotchas.md`; only promote it into
CLAUDE.md's short list if it's the kind of thing that would bite almost *any* future session,
not just one working in a specific module.

---

## Build, test, run

```powershell
# from project root
dotnet build EmberTern.slnx
dotnet test  EmberTern.slnx
# run the app
src\EmberTern.App\bin\Debug\net9.0\EmberTern.exe
```

Solution file is `.slnx` (not `.sln`) — .NET 10 default. App AssemblyName is `EmberTern`, so avares URIs use `avares://EmberTern/...`. `Directory.Build.props` sets **`net9.0`** (corrected 2026-07-27 — this line claimed `net10.0`; the real target framework is and has been `net9.0`), `Nullable=enable`, `TreatWarningsAsErrors=true` for every project. ⚠ `TreatWarningsAsErrors` also escalates NuGet's `NU1902`/`NU1903`, so a **direct** `PackageReference` with a known advisory **fails the build** — a de-facto SCA gate for direct dependencies (transitive ones are only reported by an explicit scan; gotcha #278).

## Git remotes & push workflow (user directive, 2026-07-26)

The repository is mirrored to **two** remotes. Both must receive every accepted milestone.

| Remote | URL | Role |
|---|---|---|
| `origin` | `https://vm-srv-app-git-02.streamsoft.pl:3000/GGronski/EmberTern.git` | company Gitea (HTTPS, Git Credential Manager) |
| `private` | `git@github.com:tamvel/EmberTern.git` | the user's personal GitHub (SSH) |

**The rule: after every ACCEPTED etap / milestone, push to both.**

```bash
git push origin <branch>
git push private <branch>
```

**Branch hygiene — ⚠⚠ CORRECTED 2026-08-05: the repo is NOT at `master` only any more, and that is deliberate.**
There are **two branches** (`master` + **`feat/product-polish`**), and the working assumption for a new session
is therefore: **start from `feat/product-polish`, not from `master`.**

⭐⭐ **SUPERSEDED 2026-08-10 — `feat/product-polish` IS NOW MERGED TO `master`, on the user's explicit
instruction closing the post-M5 UX package.** The 2026-08-05 decision below was *„nie chcę jej TERAZ scalać …
czekają nas jeszcze większe prace"* — a decision about timing, and the user judged that the moment had come
once Product Polish M0–M5 plus the whole UX package were accepted. Merged `--no-ff` so the arc stays readable.
⚠ **The branch is deliberately KEPT on both remotes** (explicitly instructed — not deleted like the retired
technical branches below), so the working assumption for a new session is unchanged: **start from
`feat/product-polish`.** ⛔ The general rule below still stands — do not merge it again without an explicit
instruction; this was one, for one closed stage.

⭐ *(HISTORICAL, kept because it explains the branch's whole shape:)* `feat/product-polish` is the ACTIVE
PRODUCT BRANCH and was deliberately NOT merged to `master` — the user's ratified decision (2026-08-05):
*„to jest aktywna gałąź produktu i właśnie na niej będziemy kontynuować
M4 oraz kolejne sprinty Product Polish. Nie chcę jej teraz scalać z `master`, ponieważ czekają nas jeszcze
większe prace w tym obszarze."* ⛔ Do not merge it to `master` without an explicit instruction.

⚠⚠ **AND THIS IS THE TRAP THE STABILIZATION SPRINT WALKED INTO, worth reading before the next cleanup:** a fix
branch cut FROM a long-running feature branch **cannot be merged "alongside" it.** The sprint branched off
`feat/product-polish`, so merging it to `master` would have carried **104 commits, only 8 of them the sprint's**
— i.e. it would have merged Product Polish as a side effect, the one thing the same instruction forbade. ⭐ And
separating it was measured impossible, not merely risky: 12 of its files do not exist on `master`, and the S-3
fix hangs on a style with **0 occurrences on `master`** plus a token from a `Tokens.axaml` that `master` does
not have. **The sprint's work is technically inseparable from Product Polish, because it fixed things Product
Polish introduced.** So it was merged into `feat/product-polish` (`45ff01f`, `--no-ff`) and the technical branch
was retired everywhere.

**The general rule this yields: a short branch cut from a feature branch merges back into THAT branch, never
into `master`.** For work cut from `master`, the old assumption still holds: *branch, merge back `--no-ff`,
delete the branch.*

Retired so far, each verified merged first (`git branch -d`, the safe variant that refuses unmerged work):
`feat/stabilization-sprint` (2026-08-05, into `feat/product-polish`) · `chore/avalonia-12.1.1` (2026-08-05,
into `feat/product-polish`). ⭐ The second one never reached either remote — it was not pushed before the
user's acceptance, which is the rule working as intended: *push after ACCEPTED*, so a short technical branch
can be retired locally with nothing to clean up remotely.

The 2026-08-01 sweep (historical, and the reason the paragraph above had to be corrected rather than deleted —
it describes a state that was true then) merged the last outstanding branch — **`feat/branding-ux`** (the branding UX sprint plus the
two ET0003 diagnostics bugfixes) — into `master` as `93d640f`, `--no-ff` so the arc stays readable, then
deleted **locally and from BOTH remotes**: `feat/branding-ux`, `feat/data-import`, `feat/hamburger-navigation`,
`feat/keyboard-manager`, `feat/settings-center`. Each was verified merged first (`git branch -d`, the safe
variant, which refuses unmerged work). Earlier retirements, same rule: `feat/completion-matching`,
`feat/firebird-debugger`, `feat/save-and-close`, `feat/sql-data-export`.

✅ **RESOLVED — verified 2026-08-05, and this paragraph used to say the opposite.** The residue was: `private`'s
default branch (HEAD) pointed at `feat/completion-matching`, so GitHub refused to delete it
(`refusing to delete the current branch`) even though it was provably merged, and switching the default was a
repo-settings change deliberately left to the user. Measured on closing the Avalonia sprint:
`git ls-remote --symref private HEAD` → **`ref: refs/heads/master`**, and `feat/completion-matching` exists on
**neither** remote. So the default was switched and the branch is gone.

⭐ **Both remotes now hold exactly two branches — `master` and `feat/product-polish` — and nothing else.**
⚠ Worth the line it costs: this was found by *verifying the state while closing an unrelated sprint*, not by
anyone revisiting the note. A "still open, user's to clear" item goes stale in the direction nobody checks,
because its own wording tells the next reader not to look.

**⛔ Never change the remote configuration without the user's explicit decision** — not the URLs, not a
`pushurl`, not a rename. A dual-`pushurl` "one push reaches both" variant was considered and **rejected on
purpose**: when GitHub is unreachable (no network, VPN, expired key) it makes *every* push report failure,
including the company one, so a company push would depend on GitHub's availability. Two explicit pushes keep
failures isolated.

### ✅ SSH — resolved 2026-07-26: git uses the system OpenSSH (durable fix APPLIED)

The binding configuration from now on is:

```bash
git config --global core.sshCommand "C:/Windows/System32/OpenSSH/ssh.exe"
```

**Applied globally on the user's explicit decision (2026-07-26) and verified**: `git push private` and
`git ls-remote private` both authenticate with **no** `GIT_SSH_COMMAND` prefix. **`GIT_SSH_COMMAND` is no
longer needed and should not be added to commands** — a plain `git push private <branch>` is the workflow.

**Why it was needed** (keep — it explains the setting and warns against undoing it): there are **two
independent SSH agents** on this machine, and with `core.sshCommand` unset git talks to the wrong one.

- The key lives in the **Windows OpenSSH agent service** (`ssh-agent`, Automatic). In PowerShell/CMD,
  `ssh` and `ssh-add` resolve to `C:\Windows\System32\OpenSSH\*` and DO see it.
- **Git for Windows bundles its own ssh** (`C:\Program Files\Git\usr\bin\ssh.exe`), which reads
  `SSH_AUTH_SOCK` and therefore does **not** see the Windows agent.

Measured 2026-07-26 (before the fix): `ssh -T git@github.com` in PowerShell → *"Hi tamvel! You've
successfully authenticated"*, while git's bundled ssh on the same host → **`Permission denied (publickey)`**.
So **a green `ssh -T` proves nothing** about whether `git push private` will work — if pushes to `private`
ever start failing with `Permission denied (publickey)`, check `git config --global --get core.sshCommand`
FIRST (an unset/overwritten value re-opens exactly this trap).

**After a reboot** the agent forgets the key (it is passphrase-protected). Unlock it once per system session
**from PowerShell or CMD**, so it lands in the Windows agent:

```bash
ssh-add ~/.ssh/id_ed25519
```

Running `ssh-add` inside Git Bash targets the *other* agent and will not help git or the Windows tooling.
Check what is loaded with `ssh-add -l` (PowerShell).

## Laboratory Database

A single, persistent Firebird lab database for hand-verifying EmberTern behaviour against a real engine — **use it instead of guessing how Firebird behaves.**

> The Laboratory Database is a long-lived development asset. Future sprints should extend the existing EmberTern_Lab database with new object types and edge cases instead of creating temporary one-off databases.

- **Location**: `Lab/EmberTern_Lab.fdb` — committed to Git (intentionally, for now; if it grows significantly we revisit and may switch to a `setup.sql`-only model). Canonical recreate script: `Lab/setup.sql`.
- **Reference engine**: the local **Firebird 5.0** `DefaultInstance` on **localhost:3050** (FB3 is on 4050 and is **not** used for the lab — we keep ONE lab DB, not an FB3/FB5 matrix). `isql.exe` lives at `C:\Program Files\Firebird\Firebird_5_0\isql.exe`. SYSDBA password is the local dev password.
- **DB settings**: dialect 3, **default charset WIN1250** (matches the user's real environment). Identifiers in `setup.sql` are ASCII so the script runs under any client charset.
- **Purpose**: a development aid, not a test framework and not a compatibility matrix. It carries a small, representative object zoo: 8 domains, 5 tables, 3 views, 2 standalone procedures + 2 standalone functions, 3 triggers, 3 generators, 3 exceptions, 3 roles, 1 package (with body, containing 1 function + 1 procedure), plus a little sample data, plus the debugger zoo and — since the 2026-08-05 stabilization sprint — `SP_DOM_PARAMS` / `FN_DOM_ARG` (domain-typed parameters, arguments and RETURNS) and `SP_DBG_SELINTO` (a singleton `SELECT … INTO`, the state no fidelity case reproduced). Covers PK / FK / composite PK / unique / computed columns / identity (BY DEFAULT and ALWAYS) / domain-typed columns / SUSPEND / CASE / nested BEGIN-END / before-insert / before-update / after-update / COMMENT ON, etc.
- **Use it from EmberTern**: add a connection profile → host `localhost`, port `3050`, database `C:\Dane\C#\Źródła\EmberTern\Lab\EmberTern_Lab.fdb`, SYSDBA, charset WIN1250, dialect 3.

**The rule (enforce on yourself):** before implementing or fixing anything touching Firebird **metadata, DDL, dependencies, or SQL semantics**, prefer verifying the actual engine behaviour against `Lab/EmberTern_Lab.fdb` (via EmberTern or via `isql` at an ASCII path — see the gotcha below) rather than assuming how Firebird behaves. Several past milestones were corrected only after checking a live DB (e.g. gotchas #46, #147, #148).

**Extending the lab**: edit `Lab/setup.sql` (keep object identifiers ASCII), then recreate the `.fdb` (below) and re-run the verification, OR apply the change live (EmberTern / `isql` at an ASCII path) and mirror it back into `setup.sql` so the script stays canonical. Keep it simple — this is a dev asset, not infrastructure.

**Recreate `Lab/EmberTern_Lab.fdb`** (Windows; the build-in-temp-then-copy method — forced by the path gotcha #149):
```powershell
# 1. copy the canonical script to an ASCII path (isql can't reach the repo path)
copy "Lab\setup.sql" "C:\Temp\setup.sql"
# 2. build at an ASCII temp path: CREATE DATABASE then INPUT the script
#    (put both lines in a small build.sql and run: isql -b -i C:\Temp\build.sql)
#      CREATE DATABASE 'localhost/3050:C:\Temp\embertern_lab_build.fdb'
#        USER 'SYSDBA' PASSWORD '<pwd>' DEFAULT CHARACTER SET WIN1250;
#      INPUT 'C:\Temp\setup.sql';
#      COMMIT;
& "C:\Program Files\Firebird\Firebird_5_0\isql.exe" -b -i C:\Temp\build.sql
# 3. copy the finished file into the repo (filesystem copy is Unicode-safe)
copy "C:\Temp\embertern_lab_build.fdb" "Lab\EmberTern_Lab.fdb"
```

**Gotcha — promote to architecture lore.**

149. **`isql.exe` cannot connect to a database whose path contains non-ASCII characters (the repo path has `Źródła`); EmberTern's managed driver can.** Passing the path on the `isql` command line (or via `CONNECT`/`CREATE DATABASE`/`INPUT` of a non-ASCII path) mangles it at the shell/ANSI encoding boundary — the FB5 server receives garbage (`Źródła` → `ťRãDłA`) and reports "path not found" (SQLSTATE 08001 / I/O error on CreateFile). The `#` in `C#` is fine; only the non-ASCII letters trigger it. **But the path is fully usable everywhere else**: the Windows filesystem, .NET file APIs (`Directory.Exists`/`File.Copy`), and — crucially — **`FirebirdSql.Data.FirebirdClient` (what EmberTern uses)** all handle it correctly, because the managed driver sends the database path as Unicode over the wire rather than through a shell. Consequences: (a) the lab `.fdb` is **built at an ASCII temp path with `isql`, then copied into `Lab/`** (a plain file copy — the file is location-independent); (b) any `isql` script that `INPUT`s `setup.sql` must reference an **ASCII copy** of it, not the repo copy; (c) EmberTern connects to `Lab\EmberTern_Lab.fdb` directly with no issue — verified end-to-end (create + attach + DDL + metadata read all succeed via the managed driver at the non-ASCII path). Rule: never use `isql` against a non-ASCII database path on Windows — build at an ASCII path and copy, or drive the managed driver.

## Project layout

Top-level shape (refreshed 2026-07-11 against the real tree — the per-file annotations below
predate most of the modules in "What's built" above and are kept only as illustrative examples
of each project's role, not a complete file listing):

```
EmberTern.slnx
Directory.Build.props        # net9.0, Nullable=enable, TreatWarningsAsErrors=true
src/
  EmberTern.Core/            # zero Avalonia dependencies, zero FirebirdSql dependency
    Connections/ Diagnostics/ Export/ Metadata/ Performance/ Query/ Scripting/
    Search/ Security/ Settings/ Sql/ (incl. Sql/Language/ — the Lexer/Parser/AST/
    Semantics/Completion/Navigation/Highlighting/Signatures/Snippets front-end)
    Trace/ Transactions/ Workspace/
  EmberTern.Firebird/        # all `Fb*` driver types live here; readers return Core DTOs
    (NuGet: FirebirdSql.Data.FirebirdClient 10.3.4; InternalsVisibleTo: EmberTern.Tests)
    e.g. FirebirdConnectionService.cs / FirebirdQueryExecutor.cs / TransactionService.cs /
    FirebirdMetadataReader.cs / FirebirdDdlReader.cs / FirebirdCatalogReader.cs /
    FirebirdTraceService.cs / FirebirdSessionReader.cs / FirebirdScriptExecutor.cs
  EmberTern.Office/          # the ONE place a NuGet dep on an Office format is allowed, in BOTH
                             # directions: DocumentFormat.OpenXml (the streaming XLSX writer XlsxExporter
                             # and, since I9, the streaming SAX reader XlsxImportProvider) plus, since I10,
                             # ExcelDataReader for legacy .xls (XlsImportProvider). Renamed from
                             # EmberTern.Export.Office in I9.
  EmberTern.App/             # WinExe, Avalonia 12.1.1, CommunityToolkit.Mvvm 8.4.2
    Program.cs, App.axaml(.cs), UiStrings.cs, app.manifest
    ViewModels/ Views/ Themes/ (Colors.axaml + ControlStyles.axaml — the ONLY theme sources)
    Behaviors/ Completion/ Controls/ Converters/ Diagnostics/ Export/ Security/ Sql/
    Assets/ (FirebirdSql.xshd + .Light.xshd, Branding/, Icons/ — SvgIcon geometries)
    (NuGet: Avalonia.AvaloniaEdit 12.0.0 — ⚠ deliberately BEHIND the core, no 12.1 build exists;
     Avalonia.Controls.DataGrid 12.1.2 — ⚠ deliberately AHEAD, no 12.1.1 build exists. Both mismatches carry
     their reason at the `PackageReference`; see `docs/design/avalonia-12.1.1-update.md` + gotcha #321)
tests/
  EmberTern.Tests/           # xunit; ONE shared HeadlessUnitTestSession for the whole
                             # ConnectionExpandBindingProbe class — see gotchas #94 / #226
```


## What's built — feature inventory

A map of what exists, not a narrative of how it got built (that's `docs/history/`). Every item
below is shipped and working unless marked otherwise. Reference is `docs/history/<file>` unless
noted.

- **Connections & sidebar** — encrypted (DPAPI) connection profiles, folders, drag & drop
  reorder, a flat-list (non-`TreeView`) Object Explorer with lazy count-only load, type-to-filter,
  and per-category context menus (New/Edit/Delete/Execute/Activate-Deactivate/Recompile).
  *(history: 01, 08, 09)*
- **SQL Editor** — F5/Ctrl+Enter execute (Preview/Full modes with a streaming executor and a
  1M-row safety ceiling), manual transactions (no autocommit, ever), Saved Queries panel,
  Execution Metrics (per-table read/write counts, a live elapsed timer), and the new AST-based
  formatter. Find/Replace via AvaloniaEdit's built-in `SearchPanel`. *(history: 01, 02, 10)*
- **Metadata browsing + DDL preview/export** — read the reconstructed DDL for any object;
  `Icon.Save` exports a complete, portable `.sql` script (structure + `COMMENT ON`, UTF-8 no-BOM).
  *(history: 01, 12)*
- **Per-object detail editors** (Table, View, Procedure, Function, Trigger, Package, Domain,
  Generator, Exception, Index) — each a dedicated multi-tab surface following one shared editor
  contract (see "Architecture rules" below): buffered structural edits for Table (edit → Compile
  → autonomous auto-commit), Source⇄Easy mode for Procedure/Function/Trigger/View, a Revert/
  Discard button with confirmation on every editor, native per-kind workspace persistence.
  *(history: 03, 04, 07, 09)*
- **DDL change safety** — a compile cannot overwrite an object definition EmberTern cannot prove is the one
  the editor loaded, and a New-object compile cannot overwrite an existing object whose name it reuses. One
  pure-Core verdict (`ObjectChangeSafety`) behind one App gate (`ObjectChangeGate`), on the five
  whole-object-replacement editors + the debugger's Save. Refuses rather than guesses; no force-overwrite
  (the SQL Editor is the deliberate escape hatch). *(history: 22)*
- **Transactions & connection lanes** — THREE `FbConnection`s per profile, one responsibility each
  (rewritten 2026-07-14, history 15): **Data** carries everything the user runs by hand (SQL Editor
  F5 — queries *and* DDL — table-data edits, Execute Procedure, Script Executor) and holds **THE one
  user working transaction** (auto-begin, never auto-commit, NOWAIT, one Commit / one Rollback);
  **Metadata** is **read-only** catalog browsing with implicit per-command transactions and owns no
  transaction (`MetadataLane`); **Ddl** carries object-editor Compile only — autonomous,
  auto-committed, **WAIT**-bounded, with the per-connection **Developer Mode** toggle setting how
  long it waits for an object another *session* holds. The SQL Editor is a classic Firebird console:
  no routing by statement kind, no hidden second transaction. *(history: 05, 13, 15)*
- **Settings & security** — every user setting (connections, folders, workspace, grid layouts,
  parameter history) lives in one whole-file DPAPI-encrypted `settings.dat`, with a versioned
  container header and forward-compatible schema migration. *(history: 05)*
- **Data grids** — shared filter panel + aggregation bar + Record-N-of-M indicator across all 5
  data-bearing grids (SQL Results, Procedure/Function Results, Table Data, View Data); client-side
  for materialized grids, SQL push-down for server-paged ones. Export to CSV/TXT/Clipboard/XLSX
  via one shared `EmberTern.Core.Export` framework. ⭐ **All five also offer the same context-menu set**
  (Copy cell / row / row with headers / all with headers · Filter by / Exclude / Contains · Export), through
  the ONE text builder `App/ViewModels/GridCopyText.cs` and the ONE clipboard writer
  `App/Views/GridClipboard.cs` — **only module-specific operations differ** (Table Data's edit group; Copy as
  INSERT/UPDATE where a table backs the rows). ⛔ A new data grid gets that set or fails
  `DataGridCopyMenuTests`; ⚠ each host supplies its OWN row list, because "all" means *the rows this grid is
  showing* (Table Data must pass `EditableRows`). *(history: 10, 12, 25)*
- **SQL Data Export — Copy as INSERT / UPDATE** — right-click a result row → runnable, provably-correct
  DML (de-aliased via the server's own provenance; UPDATE only on a catalog-verified complete PK, never a
  partial key → multi-row bug; `OVERRIDING SYSTEM VALUE` for `GENERATED ALWAYS`; InvariantCulture literals;
  refused-with-a-reason where proof is unavailable). One Core pipeline (`ResultOriginResolver` →
  `SqlStatementBuilder` → `SqlLiteralWriter`) behind a shared App `SqlCopyController`, live on the **SQL
  Editor** (E5) and **Table Data** (E6) grids; Procedure/View results stay `NotATable`. Design + as-built:
  [docs/design/sql-data-export.md](docs/design/sql-data-export.md). **Milestone COMPLETE + user-confirmed
  (2026-07-17)**, including the follow-up fix for environments that hand every cell back as a `string`
  (the writer parses each kind strictly under InvariantCulture/ISO, refusing anything ambiguous — §0).
- **Shared `MessageBanner`** (`App/Controls/MessageBanner.axaml`) — the IDE's ONE message surface: a calm
  severity-striped bar (Info/Success/Warning/Error) whose stripe, icon **and message text** all carry the
  severity colour, message wrapped + selectable, with Copy (default on) / Expand / Dismiss. Chrome comes from
  **exactly two variants** in `ControlStyles.axaml` — standalone (default) and `Classes="docked"`. Live on 23
  surfaces: debugger Error Bar + pre-flight, all 10 object editors, Execute Procedure/Function (+ its dialog),
  Table/View data errors, Performance panel, Export dialog, Security Manager. The SQL Editor **Messages log**
  stays a log but paints its problem rows through the **same** `BrushKeyFor`/`GeometryKeyFor` mapping.
  **Use it for any new error / warning / info message on a work surface — never a locally-styled coloured
  `TextBlock`, and never a local `Background`/`BorderBrush`/`BorderThickness` on the banner itself** (a local
  value outranks the shared style and re-opens per-host divergence). *(UX Polish Seam 4 + its QA round)*
- **Global Search** — search metadata by name and by source/field/message content
  (server-side `CONTAINING`), 2-panel results with a live DDL preview. *(history: 12)*
- **Script Executor, Recompile Dependents, Smart SQL Parameters** — run a multi-statement `.sql`
  script under one caller-controlled transaction (via the driver's `FbScript` parser); after
  compiling an existing object, optionally recompile its direct dependents; F5 on a statement with
  `:name`/`@name` placeholders opens a typed parameter-collection dialog before binding and
  running it. *(history: 12)*
- **Security Manager** — Users / Roles / Membership / object & column privileges, immediate-apply,
  contextual one-click grant/revoke (cell / row / column / all-visible). *(history: 09)*
- **Database Activity Monitor (Trace)** — a live, grouped, filterable view of everything the
  connected database is doing (via the Firebird Services Trace API), with per-table read bars and
  an "open the traced SQL in the editor" bridge into Performance Analysis. *(history: 11)*
- **Session Manager** — live `MON$`-based view of attachments and transactions, with two health
  detectors (long-running transaction, garbage-collection risk) and a Disconnect action.
  *(history: 11)*
- **Performance Analysis** — profile a query's execution plan + measured per-table reads, with a
  measured-first advisor (6 rules) producing confidence-scored findings, investigation guidance,
  and improvement recommendations — never automatic fixes. *(history: 10)*
- **SQL/PSQL Editor language front-end** — the current, actively-developed rebuild: one shared
  Lexer → Parser → AST → Semantic Model in `EmberTern.Core.Sql.Language`, with completion,
  signature help, snippets, navigation (Ctrl+hover/Ctrl+Click, Peek Definition, safe local
  rename, find references), semantic highlighting, and Quick Info all built as *clients* of that
  one model. See **`docs/design/editor-architecture.md`** for the current architecture and
  **"Editor Architecture — current direction"** below for status.
- **Application Menu (☰) + About + Keyboard Shortcuts + third-party notices** — the hamburger is the **first
  button of the titlebar action zone** and opens EmberTern's one home for *application-level* functions:
  `Settings…` (live since Settings Center etap 3), `Keyboard Shortcuts…`, `About EmberTern…`,
  `Exit`. Deliberately a **rarely used administrative menu** — daily work stays on the toolbar, the shortcuts and
  the context menus, and no existing tool was moved or mirrored into it. It is a plain `ContextMenu` opened from
  code, so it inherits the app's one menu appearance with **no new style**.
  **About** is a *product* window (logo, name, version, `Created by`, copyright) — no runtime/OS/library block by
  decision; a footer button opens the **third-party notices**. ⭐ **Since 2026-08-01 it is the ONLY place a logo
  appears in the running application** — the titlebar mark was removed on purpose (chrome belongs to the
  document; the identity belongs here), and the **OS icon every window carries comes from ONE
  `<Style Selector="Window">` setter** in `ControlStyles.axaml`, never from a per-window assignment. ⛔ Do not
  set `Icon` on an individual window (a local value outranks the setter) and do not re-add a mark to the
  titlebar. Asset map + how to replace the artwork:
  [src/EmberTern.App/Assets/Branding/BRANDING.md](src/EmberTern.App/Assets/Branding/BRANDING.md). **Keyboard Shortcuts** is a read-only projection of
  `CommandCatalog` (search · Command/Shortcut/Scope · canonical order `Global → Tab → Tree → Grid → Editor →
  alphabetical`, restorable after any column sort · live count), which is why there is no second list of
  shortcuts to maintain.
  **⭐ Version and identity have ONE source: the `PropertyGroup` in `Directory.Build.props`.** `AppInfo` reads
  `<Version>`, `<ReleaseDate>`, `<Product>`, `<Company>`, `<Copyright>` back off the assembly, so a release is one
  line in one file — and both the About window *and* the status-bar chip read it. **⛔ Never write a version
  number in code**; two `AppInfoTests` guards enforce it (the current value nowhere under `src/`; no
  version-shaped literal where one could be displayed). Current version **0.5.0**, and 0.x is deliberate: 1.0
  arrives with the finished product and its licensing system.
  **⭐ `THIRD-PARTY-NOTICES.txt` is required, not a courtesy** — MIT obliges its notice "in all copies", and
  `FirebirdSql.Data.FirebirdClient` is **IDPL 1.0**, whose §3.6 wants a source-availability notice with an
  executable distribution. One file at the repo root, embedded *and* copied beside the exe; every licence text is
  a **copy of an artefact**, never a recitation. `ThirdPartyNoticesTests` fails when a shipping `PackageReference`
  is not named in it. Design + licence findings + as-built:
  [docs/design/hamburger-navigation.md](docs/design/hamburger-navigation.md).
- **Settings Center** — the app's one home for user preferences, opened from the hamburger's `Settings…`: a
  **window** (never a workspace tab — a tab would need threading into five per-kind families), two panes
  (search + category list · the selected category's page), **apply-on-change with no OK/Cancel**, and a docked
  shared `MessageBanner` when the store refuses to write. The General page carries **Theme** (persisted and
  read at startup — the titlebar toggle writes the same preference) and **Language** (real storage over Core's
  one-row catalog; the localization *mechanism* is its own milestone). ⭐ **Every option a control offers is
  generated from Core's `PreferenceOptions`** — a second list in XAML drifts silently, in the direction where
  the user picks an option that reverts on next load and nothing fails. ⭐ **One `PreferencesService` owns the
  live `Preferences` for the whole app**, because the store persists the whole object: two snapshot holders
  would overwrite each other's fields. ⭐ **`App` is the ONE place a theme is applied** — every writer only
  writes the preference. The **SQL Formatter** page carries **Keyword case** and **Identifier case** (both `lower`/`UPPER`, both
  defaulting to lower so shipped output is byte-identical). ⭐ **`SqlFormatter` now takes a `FormatterStyle`
  parameter with a default — never an ambient read — and has exactly ONE casing decision point** where it
  previously had ~30; the keyword/identifier split reads the verdict `SqlLexer` already recorded, so there is
  no second keyword list *and* no second keyword decision. ⚠ **Quoted identifiers, literals and comments are
  never re-cased** (a quoted name's case is part of the object's identity — §0 / rule #11), and the setting
  governs the **Format SQL action**: generated DML (Copy as INSERT, `.sql` export) and generated DDL keep the
  shipped style, per ratified Q1.
  **Three more pages since etap 6** — **Editor** (default Source/Easy mode for procedures / views / triggers /
  functions · **Preview row limit** · **Ask before loading more than**), **Grid** (**Data page size** for the two
  server-paged data grids · **Auto-fit columns by default**), **Debugger** (**default transaction isolation** —
  the launch panel's per-launch selector still overrides it). ⭐ **A row declares its `SettingValueKind`**
  (`Option` | `Toggle` | `Number`), which is what picks its control — separate from `SettingKind`
  (`Preference` | `Action`), because *value or command* and *what shape of value* are two questions.
  ⭐ **A numeric preference is bounded by a Core `PreferenceRange`** that the store and the field both read, and
  out-of-range **clamps rather than resets** (a stored `50 000 000` means "as many as possible"), with the field
  echoing the settled number back. ⚠ **Numeric fields commit on BLUR or ENTER, never per keystroke** — every save
  fully rewrites `settings.dat` and rolls its single `.bak`. ⚠⚠ **A numeric control carries its ROW as its
  `DataContext`**; on the page's DataContext it types correctly and persists nothing. ⭐ **The four Source/Easy
  flags moved out of `WorkspaceState` and the editor write-back is deleted** — they were rewritten by whatever
  editor was last toggled, so a procedure opening in Easy mode because of a *different* procedure looked like a
  bug; toggling mode in an editor is now per-tab only, and a restored tab's own mode still wins.
  ⚠ **"Restore open tabs" gates RESTORE, never CAPTURE, and only the TABS** — the stored workspaces are still
  loaded (they are what capture writes back, so skipping the load would erase other connections' work) and
  **saved queries always come back**. Design + as-built:
  [docs/design/settings-center.md](docs/design/settings-center.md) §13 + §14 + §17.
- **Settings export / import (`.etsettings`)** — EmberTern's own **versioned, always-encrypted** artifact, so a
  configuration is portable where DPAPI deliberately is not. **Live end to end since etap 5b**: the General page's
  *Import / export settings* row opens two modals (section checkboxes · passphrase with confirmation · a content
  preview of what the file declares · per-section accept), plus *Open settings folder*. An import **merges into
  `settings.dat`** — non-destructively: unselected sections are untouched, connections merge **by `Id`** (a
  re-import updates, never duplicates), and the previous file is kept as **`settings.dat.pre-import-<stamp>`**.
  ⚠ **The pre-import file is a COPY, not a rename** — an import *merges*, so the preserved file is also the merge
  base; moving it would make every unselected section come back as a default (gotcha #293). ⚠⚠ **An import that
  declines passwords must not blank the ones already stored** — the export omits a password by writing it *empty*,
  so a wholesale copy of the incoming profile would erase a working credential as a side effect of importing a host
  name; `ClientLibraryPath` is likewise kept locally (gotcha #292). ⭐ **One `SettingsPortability` owns both
  directions AND what the running app must be re-read afterwards** — an import writes the file several in-memory
  holders were loaded from, and the damage is the *next* write, not the stale read (§16.1). ⭐ **An imported theme
  repaints through the app's existing single apply point** (`PreferencesService.Reload` raises `Changed`; `App`
  still assigns the variant) — no second apply path. ⚠ **`Workspaces` is the one section that cannot take effect
  live**: the window captures the workspace on close, so importing it arms a one-shot suppression of that capture
  and the dialog says the section applies on the next start. Nothing is blocked — theme, formatter, folders and
  connections apply immediately, a connected profile changes on its next connect, and the dialog states all three
  (§16.3). ⚠⚠ **That same "captured only at close" fact is why an EXPORT cannot read the workspace off
  `settings.dat`** — it is the ONE section not written when the user changes it, so the store holds the *previous*
  session's tabs. The export takes the **live** capture through `SettingsPortability.CaptureLiveWorkspace`, built
  by `MainWindow.CaptureLiveWorkspaceState()` — ⭐ the ONE builder, shared with the close save — and taking it
  writes nothing (§16.6). A
  cleartext header (`EMBERTERN-SETTINGS-EXPORT` · format version · app version · scheme · KDF + iterations +
  salt) over an **AES-256-GCM** payload under a PBKDF2-SHA256 passphrase key. ⭐ **The header is cleartext
  because that is what makes versioning possible at all** — an opaque file could not tell *"older export,
  migrating"* from *"wrong passphrase"* from *"corrupt"*; the **section list stays inside the payload**, since
  a cleartext *"contains: Passwords"* advertises what is worth attacking. ⭐ **GCM, not CBC**: a wrong
  passphrase fails as *authentication*, so it is distinguishable from a damaged file — the same distinction
  `SettingsLoadStatus` draws between `Corrupt` and `Unreadable`, for the same reason. ⭐ **The magic is the
  export's OWN, not `settings.dat`'s** (ratified Q13), so the first header read alone determines the file's
  type and *"never ask for a credential that cannot possibly work"* holds — a `settings.dat` offered to import
  is rejected **at the magic**, never with a passphrase prompt. ⭐ **`SettingsImportReader` is TWO PHASES** —
  `Inspect` (identity → format version → scheme, **no passphrase**) then `Open(inspection, passphrase)` — so
  the passphrase dialog *cannot* become import's entry point. ⚠ **Three version numbers, one job each**:
  `ExportFormatVersion` is the migration contract, `SchemaVersion` is the settings shape (the **existing**
  `MigrateToCurrentVersion` ladder, called not copied), and **`AppVersion` is diagnostics and is never branched
  on** — Core takes it as an input precisely so it cannot be. ⚠ Passwords are **opt-in**; `ClientLibraryPath`,
  `WindowBounds`, `ParameterHistory` and `DebugWatches` **never travel**, and a reflection guard per persisted
  type fails the build when a new field has no recorded decision. Design + as-built:
  [docs/design/settings-center.md](docs/design/settings-center.md) §6.3 + §15 (the format) + §16 (the surface).
- **Keyboard Manager / command system** — **ONE registry every UI surface reads from.**
  `EmberTern.App/Commands`: `CommandCatalog` is a single declarative table of `CommandDescriptor`s built once
  at type-init (id · scope · dispatch · gesture(s) · tab kinds), plus a collision validator; `CommandRouter`
  (window, **Bubble** phase) resolves a keystroke **Editor > Tree > Grid > Tab > Global** and declines when
  nothing is live; `CommandTip` is the one place a gesture becomes text. The registry holds **descriptions,
  never `ICommand`s** — the instance is resolved at invoke time by `MainWindowViewModel.ResolveCommand`
  (Global), `WorkspaceTabViewModel.ResolveCommand` (Tab — the fourth member of the per-kind family beside
  `UnsavedWork`/`SavableEditor`/`RefreshAsync`) and `MetadataExplorerViewModel.ResolveCommand` (Tree).
  **Shortcuts, tooltips, shortcut-chips and all 32 context menus take their gesture from it** — no gesture is
  typed by hand anywhere, and two tests enforce that. Context menus are one shared style set (icons left via
  `{app:MenuIcon}`, gestures right via `{app:CommandGesture}`). *(design + as-built:
  [docs/design/keyboard-manager.md](docs/design/keyboard-manager.md))*

## Current state

- **🔢🌍 LOCALIZATION / CORE — C8 (`EmberTern.Office` ×2) — ODEBRANE i ZACOMMITOWANE 2026-08-10 (`45829a5`).**
  Gałąź `feat/localization`; ⛔ **NIE pushowana, NIE scalona** — `origin/feat/localization` stoi wciąż na
  `8f861ce` (etap App), więc **osiem odebranych etapów Core czeka lokalnie na wypchnięcie**. Build 0/0; suite
  **8 730** (8 389 + 286 + 55); smoke czysty; `Lab/` nietknięty. Narracja:
  **[docs/history/28-…](docs/history/28-localization-core-stage.md)** (sekcja C8). Nowe gotchy **#366–#368**.
  🔒 Ratyfikowane po audycie: **D‑1/D‑2** klucze i wyjątek w `Office` · **D‑3 (a)** live switching poza C8 ·
  **D‑4** brakujący pin `.xlsx` dodany.
  **Kontrakt:** `ImportSourceMessages` (2 klucze) · `ImportSourceException` (forma dualna: `Localized` +
  angielski `Message`) · `InnerException` **techniczny, niepokazywany nikomu** ·
  `DataImportTabViewModel.Describe(ex)` — test TYPU przed catch-allem, **bez resolvera** · `SetStatus`
  **nietknięty**.
  ⭐⭐ **GRANICA JEST ODWROTNA NIŻ W C3 I TO POMIAR, NIE STYL.** Tam słowa serwera jadą jako **argument**, bo są
  autorytatywne. Tu `DocumentFormat.OpenXml` odpowiada `File contains corrupted data` na plik, który **NIE jest
  uszkodzony** — jest tylko starszym formatem pod nową nazwą, a `ExcelDataReader` mówi `Invalid file signature`.
  Pierwszy komunikat jest nie tyle nieprzydatny, co **FAŁSZYWY**. ⭐ Reguła, która się z tego uogólnia, jest
  węższa niż „opakuj obcy komunikat": **obcy komunikat jedzie jako dana, gdy jest autorytatywny, i jest
  tłumiony, gdy jest nieprawdziwy.** ⚠ `InnerException` jest **przypięty jako OBECNY** — usunięcie go
  uniemożliwiłoby postawienie tej samej diagnozy drugi raz.
  ⭐⭐ **ZNALEZISKO 1 — strażnik katalogu nie widział assembly `Office`, i zawodził ASYMETRYCZNIE.**
  `DeclaredCoreMessageKeys()` skanowało Core + Firebird (to samo, co C0 §4 znalazł dla Firebirda — **po raz
  drugi**). Podsadzenie: `EveryCoreShapedEntry_IsDeclaredByCore` 🔴 · `EveryLocalizedMember_…` 🔴 ·
  **`EveryCoreMessageKey_HasAnEnglishEntry` 🟢** — czyli czerwone są te, których awaria i tak jest widoczna
  (sierocy wpis w zasobie), a **cichy** (klucz bez wpisu rozwiązuje się do siebie ⇒ surowy identyfikator na
  ekranie) po prostu przestaje patrzeć. Podsadzenie odwrotne zapala **sześć** testów, w tym ten trzeci.
  ⛔⛔ **Pułapka dla następnego autora: gdyby C8 zadeklarowało klucze bez ruszania strażnika, dwa czerwone testy
  zażądałyby uwagi — a „oczywistą" naprawą jest DOPISANIE ICH DO WYJĄTKÓW, nie rozszerzenie skanu.** To zamyka
  objaw i zostawia cichego strażnika ślepym, w stanie wyglądającym na rozwiązany.
  ⭐⭐ **ZNALEZISKO 2 — mój test powierzchniowy był ZIELONY Z DWÓCH POWODÓW i nie przypinał żadnego.**
  Cofnięcie konsumenta do `SetStatus(ex.Message, …)` **nie zapaliło go** (9/9 zielonych): angielski jest jedynym
  wysyłanym językiem, więc `Loc.Format(localized)` i `ex.Message` renderują te same znaki — **z gwarancji formy
  dualnej**. Test nie odróżniał *„App rozwiązał klucz"* od *„App wypisał fallback"*. Kształt **#357** i C6-owego
  `SwitchingLanguage_…`. ⭐ Jedynym instrumentem, który je rozdziela, jest **PODMIANA KATALOGU**
  (`Loc.UseCatalogForVerification`, cofana w `finally`); po przepisaniu to samo podsadzenie zapala dokładnie ten
  jeden test. ⚠ Skutek: przynależność klasy do `HeadlessCollection` przestała być ostrożnościowa i stała się
  **nośna**.
  ⚠⚠ **UZASADNIENIE, KTÓRE SAM ZAPISAŁEM W TEŚCIE, BYŁO FAŁSZYWE.** Twierdziłem, że zamiana kluczy między
  providerami jest niewidoczna dla sprawdzenia formy dualnej; zmierzone — zapala **oba** testy, bo literał EN
  zostaje u producenta, a rozwiązany wpis się przesuwa. Forma dualna widzi to **tylko dlatego, że te dwa zdania
  różnią się po angielsku** — własność dzisiejszego brzmienia, nie mechanizmu. ⭐ Test per provider wnosi więc
  **poprawną DIAGNOZĘ** (`expected Import.Source.NotReadableXls`), a nie widoczność; komentarz poprawiony
  w miejscu.
  ⭐ **`Describe` wpięte we wszystkie 13 gołych renderów `ex.Message`, nie w 5 osiągalnych** — lista miejsc
  byłaby **analizą osiągalności zaszytą w kodzie**, która się zdezaktualizuje, a jej awaria jest cicha
  (angielskie zdanie w polskim oknie, #337). Dyskryminatorem jest **TYP** wyjątku, więc dla każdego innego
  wynikiem jest dowodliwie ten sam łańcuch co wcześniej. Dwa `string.Format(…, ex.Message)` na ścieżkach DDL
  Firebirda świadomie nietknięte.
  ⚠ **Dowód zerowej zmiany treści NIE mógł oprzeć się na nietkniętych testach** (jak C4a/C6) — pinów
  praktycznie nie było: dwa `Assert.Contains` na jednym z dwóch zdań, **zero na drugim**. Mechanicznie:
  `git diff` obu providerów pokazuje literały jako **linie KONTEKSTU**, a strażnik formy dualnej wiąże literał
  z wpisem w zasobie, czytając zdanie z **prawdziwie rzuconego** wyjątku.
  ⚠ **Pięć podsadzeń, każde zapala swojego strażnika**; ⭐ dwa z nich **zmieniły pracę** (asymetria strażnika ·
  fałszywie zielony test). Dryf słowa w zasobie zapala **dokładnie jeden** test, a istniejący pin `.xls`
  **przechodzi** — potwierdza, że nigdy nie mógł tego złapać.
  ⛔ **#353 świadomie zostawione jako DŁUG poza zakresem:** baner nie przeżywa zmiany języka, bo `SetStatus`
  zapisuje gotowy tekst, a moduł nie ma `RefreshLocalizedText`. Zmierzony koszt: **31 wywołań, 18 z gotowym
  tekstem `UiStrings`**, więc ożywienie ścieżki wyjątku zostawiłoby osiemnaście zamrożonych statusów obok
  jednego ruchomego (R7). To decyzja o `SetStatus`, nie konsekwencja migracji dwóch zdań.
  ⚠ **Sprostowania inwentarza:** ⭐ *„Office ×2" PRZETRWAŁO pomiar* — dokładnie 2 literały user-visible w 6
  plikach produkcyjnych, **pierwszy wiersz inwentarza C0, który się zgadza**; natomiast C0 §3 *„renders it in
  eight places"* → zmierzone **15** miejsc czyta `ex.Message`, z czego **5 osiągalnych** z Office i **1
  dominujące** (`Open` zawodzi na pierwszym wywołaniu providera, więc łańcuch nie dochodzi dalej).
  ⚠⚠ **Defekt narzędziowy zapłacony po drodze (#366): `sed -i` w Git Bash przepisało CRLF → LF w CAŁYM pliku
  2 581-liniowym, a `git diff --stat` pokazało tylko zamierzone 39/13**, bo repo normalizuje końce linii —
  **git zamaskował przepisanie całego pliku**. Ta sama rodzina co uszkodzenie Pythonem w etapie App.
  ⛔ `sed` nie nadaje się też do WERYFIKACJI, bo jego strumień wyjściowy również gubi CR; sprawdzaj
  `tr -d '\r'` po obu stronach + `grep -c $'\r$' f` == `wc -l < f`.
  ⚠ **Filtr partycji headless ma 22 nazwy** — ⛔⛔ **wyprowadzaj go, nie przepisuj:**
  `grep -rln HeadlessCollection tests/EmberTern.Tests/*.cs`.
  ⚠⚠ **LUKA W DOKUMENTACJI, ZAPISANA A NIE WYPEŁNIONA: C7 (`Performance`, `d620cc8`) NIE MA sekcji
  w `docs/history/28-…` ANI wpisu w CLAUDE.md.** Jego kontrakt opisuje wyłącznie komunikat commita. ⛔ C8 tego
  nie zmyśla — jeżeli narracja C7 ma powstać, jest to osobne zadanie.
  ⏭ **Ratyfikowana kolejność C0 jest WYCZERPANA** (`Office` był ostatnią pozycją przed „decyzją o Performance",
  a Performance zamknęło C7). Następny krok wymaga decyzji użytkownika. ⏸ Otwarte tematy etapu, każdy z powodem:
  **`KindLabel`/`SymbolKind`** (decyzja kontraktowa, odłożona) · **30 hedge'ów `(s)` w katalogu App** ·
  **#353 w Data Imporcie** · **≈430 zaszytych stringów** i **polskie tłumaczenie** z §7 `localization.md`.

- **🔢🌍 LOCALIZATION / CORE — C6 (`ExecutionSummary` / `ExecutionActivity` + MECHANIZM LICZBY MNOGIEJ) —
  ODEBRANE; ZACOMMITOWANE w `96cc758` (C1–C6 razem).** ⚠ *Zapis historyczny: ten wpis mówił „nadal NIE
  scalona, nic nie commitowane … SIEDEM etapów bez commita (#350)" i było to prawdą, gdy powstawał. C1–C6
  poszły jako `96cc758`, C7 jako `d620cc8`, C8 jako `45829a5`; ⛔ gałąź nadal NIE jest pushowana ani scalona —
  bieżący stan trzyma wpis C8 na górze tej sekcji.* Gałąź `feat/localization`. Build 0/0;
  suite **8 709** (8 387 + 267 + 55) — ⚠ liczba z chwili C6, nieaktualna; smoke czysty; `Lab/` nietknięty.
  Narracja:
  **[docs/history/28-…](docs/history/28-localization-core-stage.md)** (sekcja C6); architektura mechanizmu:
  **[localization.md](docs/design/localization.md) §4.3**. Nowe gotchy **#362–#365**.
  🔒 **Ratyfikowany wariant W‑B** + R1(a) reguła w `.resx` kultury · R2(a) nazwy opisują GRAMATYKĘ ·
  R3(a) liczba zawsze jako argument {0} · R4(a) **bez `Count` w `LocalizableMessage`** · R5(b) kolorowane
  `Run`-y · R6(a) zakres tylko `Query.Exec.*` · R7 status i panele reagują, log Messages nie.
  ⭐⭐ **ZNALEZISKO GŁÓWNE: to były TRZY problemy, a mechanizmu wymagał tylko JEDEN.** Szyk zdania
  (`"{n} {row|rows} {inserted}"`) i sklejanie fragmentów (`Join(" · ")`) rozwiązuje **zwykła reguła D‑3
  w rozdzielczości ZDANIA** — jedno całe zdanie, jeden klucz, dane jako argumenty; tłumacz przejmuje
  kolejność. Mechanizmu wymagała wyłącznie **KATEGORIA liczby**, więc cały mechanizm to jeden plik
  (`PluralRules`) i jedno rozgałęzienie w `Loc.Format`.
  ⭐⭐ **O tym, czy zdanie potrzebuje form liczby, decyduje JĘZYK, nie producent** — `Loc.Format` sonduje
  warianty **w katalogu renderowanej kultury**, więc angielski trzyma wpis płaski tam, gdzie polski deklaruje
  trzy. ⛔ Flaga „to jest komunikat liczbowy" na `LocalizableMessage` byłaby twierdzeniem Core o gramatyce,
  której Core nie zna — i zamroziłaby angielski podział dwuelementowy w kontrakcie (**#363**).
  ⛔ **Zestaw reguł nazywa GRAMATYKĘ, nigdy języka** (`one-other`, `one-few-many`) — kilka języków dzieli
  jeden kształt, więc nazwa językowa jest fałszywa przy drugim konsumencie. ⛔ Reguła jako WYRAŻENIE
  parsowane z zasobu **odrzucona**: mini-język na ścieżce, która nie ma prawa rzucić, został już raz opłacony
  (`TreeDiagnostics`). ⚠ Granica powiedziana wprost: „nowy język bez kodu" obowiązuje dla gramatyki już
  zamodelowanej; genuinnie nowy algorytm liczby mnogiej **JEST kodem**.
  ⭐⭐ **ODZIEDZICZONY LICZNIK „5 PRZYPADKÓW" NIE PRZETRWAŁ POMIARU — trzeci taki inwentarz w tym etapie,
  i pierwszy błędny w stronę ZANIŻENIA:** zmierzone **7** rozgałęzień w kodzie (4 w modułach otwartych,
  3 w zamkniętym Performance) **+ 30 hedge'ów `(s)` w katalogu App** — ta sama klasa problemu, rozwiązana
  angielską konwencją, której polski nie ma (`„8 wiersz(y)"` jest błędne dla 1 i 2). ⛔ C6 ich NIE migruje
  (R6), ale mechanizm zaprojektowany na 5 przypadków byłby niedowymiarowany o rząd wielkości. ⭐ Siódmy
  przypadek to inna gramatyka — `"has"/"have"` to zgodność CZASOWNIKA — i obsługuje go ten sam kształt,
  bo mechanizm wybiera WARIANT ZDANIA, a nie odmienia rzeczownik.
  ⭐⭐ **Dowodem zerowej zmiany treści są NIETKNIĘTE testy:** `ExecutionSummaryTests` (14) i
  `ExecutionActivityTests` (8) przypinają angielskie brzmienie dosłownie i wołają przeciążenia BEZ resolvera,
  renderowane przez nowe `ExecutionEnglish` (forma dualna). Osobny strażnik żąda, żeby KATALOG odtworzył te
  same zdania dla każdego kształtu × każdej liczby — dwa niezależne zbiory danych przez jeden wspólny
  składacz, więc rozjechać je może wyłącznie różnica SŁÓW (i to samo łapie dopisane przez tłumacza `{0:N0}`,
  czyli zagrożenie #357, bez testu, który musi je powtarzać).
  ⚠ **Jedna wartość angielska zmieniona świadomie i zgłoszona PRZED akceptacją kontraktu:** fallback
  sterownika nie miał liczby pojedynczej i renderował **`"1 rows affected"`** (osiągalne). Nadanie kluczowi
  rodziny jest tym, co czyni go przetłumaczalnym; korekta angielskiego jest konsekwencją.
  ⛔⛔ **`TableChange.Verb` NIE JEST JUŻ BINDOWANE W XAML i nie wolno tego cofnąć** — karta rysowała `Count`
  i `Verb` jako dwa sąsiadujące, różnie pokolorowane bindingi, czyli miała **angielski szyk zapisany
  w UKŁADZIE**; polski stawia liczbę w środku. Karta renderuje teraz jedno zlokalizowane zdanie rozcięte
  wokół liczby (`Loc.FormatParts`, trzy `Run`-y), więc akcent na liczbie przeżywa tłumaczenie (**#362**).
  ⭐ Skutek uboczny: trzy `DataTemplate` kluczowane po podtypach rekordu Core → **jeden** nad wierszem App,
  i ratchet odstępów **ZAŻĄDAŁ obniżenia** swoich baseline'ów — mechanizm działający zgodnie z zamysłem.
  ⚠ **Live switching to #353 po raz TRZECI** (`RaiseAllPropertiesChanged` dociera do ZAKŁADKI, nie do VM
  treści, a `ExecInfo`/`QueryStatsText` to wartości zapisane). Zamiast odkrywać to po raz czwarty:
  `EveryViewModelThatCanRefreshItsText_IsForwardedFromTheTab` **uzbraja się sam z TYPÓW** deklarujących
  `RefreshLocalizedText`. ⛔ Log Messages świadomie NIE jest przepisywany — to timestampowany zapis tego, co
  powiedziano wtedy (ta sama decyzja co „Query N").
  ⚠⚠ **Test, który przechodził z DWÓCH powodów, złapany przed zaraportowaniem:**
  `SwitchingLanguage_RebuildsTheActivityCards` był zielony niezależnie od tego, czy wiersz rozwiązuje tekst
  przy odczycie, czy jest przebudowywany — oba są zaimplementowane, więc nie przypinał żadnego. Rozbity na
  dwie nazwane asercje, każda z własnym podsadzeniem.
  ⭐ **Dziesięć podsadzeń, każde zapala swojego strażnika** (m.in. zdjęta reguła nastolatków → 5 przypadków;
  dryf wartości w zasobie; brakująca kategoria rodziny; wpis płaski obok rodziny; usunięte przekazanie do VM
  dziecka; argument niebędący liczbą → 9 testów, w tym **NIETKNIĘTY** `ExecutionSummaryTests`; zestaw reguł
  nazwany językiem; spacja między `Run`-ami w dostarczonym XAML; cache w wierszu; brak przebudowy).
  ⚠⚠ **Dwa defekty PROCESOWE zapłacone przy podsadzeniach, oba warte zapamiętania:** `git checkout` **nie
  przywraca pliku nieśledzonego** (dokładnie #350 — rewert podsadzenia w `PluralRules.cs` zawiódł), a rewert
  z PUSTYM łańcuchem zastępującym **wstawia linię na pozycji 0** (wylądowała nad blokiem `using`). Dalsze
  podsadzenia rewertowane odwrotną łatką, pliki nieśledzone skopiowane do scratchpada (**#365**).
  ⚠ **Wzrost +53 trafił W CAŁOŚCI do partycji ZGRUPOWANEJ** (214 → 267); partycja główna stoi na 8 387.
  Filtr headless ma **20** nazw — ⛔⛔ **wyprowadzaj go, nie przepisuj**:
  `grep -rln HeadlessCollection tests/EmberTern.Tests/*.cs`.
  ⚠ **Zmierzone przy okazji, zapisane i NIENAPRAWIONE:** reguła §B.7 („każdy test dotykający `Loc` należy do
  `HeadlessCollection`") nigdy nie była egzekwowalna — **~40 klas testowych czyta `UiStrings` spoza kolekcji**,
  w tym `ProcedureDetailTests` od etapu App. To, co faktycznie obowiązuje i czego C6 przestrzegało: **test,
  który PODMIENIA katalog, dołącza do kolekcji i cofa podmianę w `finally`**.
  ⏭ **Następny: `Office` ×2** (`ex.Message` jako klasa A pod postacią `throw`). ⛔ Nie otwierać przy okazji
  SessionHealth, QuickInfo ani Performance; 30 hedge'ów `(s)` w App to osobny zakres.

- **🃏🔧 KARTA PARAMETER HELPER PRZEŻYWAŁA ZMIANĘ ZAKŁADKI — NAPRAWIONE 2026-08-10, ⏸ czeka na QA
  użytkownika.** Zgłoszenie: *„jak klikam dwa razy na funkcję, okienko zostaje, gdy przechodzę na inną
  zakładkę; przy quickinfo była poprawka, ale tutaj nie działa"*. Build 0/0; suite **8656** (8387 + 214 + 55);
  smoke czysty. As-built: **[docs/history/29-…](docs/history/29-formatter-and-psql-dml-binding-fix.md) §6**.
  Nowa gotcha **#361**.
  ⭐⭐ **`Hide()` NIGDY nie było zepsutą połową — brakowało WYZWALACZA, a komentarz w `Hide()` czytał się jak
  naprawiony błąd i ukrywał to.** Twierdził: *„po zmianie zakładki edytor jest odczepiony, `GetOverlayLayer`
  zwraca null, karta jest osierocona"*. Zmierzone — **każda klauzula fałszywa**: MainWindow trzyma zakładki
  jako **współistniejące widoki bramkowane `IsVisible`**, więc `DetachedFromVisualTree` fires **0×**,
  `GetOverlayLayer` nadal rozwiązuje, a **wszystkie zakładki dzielą JEDNĄ warstwę overlay okna** — dlatego
  karta jest fizycznie nad nową zakładką. Cyklem życia karty rządzi **karetka** (#210), a zmiana zakładki nie
  rusza karetki, więc żadna reguła nigdy nie odpaliła.
  ⭐ **Bliźniacza karta hovera tylko WYGLĄDAŁA na odporną:** zdejmuje ją `PointerExited`, które odpala, bo
  myszka odjeżdża do paska zakładek — gest, którego karta karetkowa nie ma. Czyli *„tam była poprawka"* było
  prawdą o USUWANIU i nic nie mówiło o wyzwalaczu.
  ⚠⚠ **Trzy oczywiste sygnały padły po kolei:** `Visual` **nie ma `IsEffectivelyVisibleProperty`** w Avalonii
  12.1.1 · `DetachedFromVisualTree` 0× · `EffectiveViewportChanged` **niemierzalne stabilnie** (0× w sondzie,
  1× w teście — pierwsza dostawa jest asynchroniczna), więc **ani nie użyte, ani nie twierdzone**, zamiast
  dostrojone pod zielone. Zostają **`LayoutUpdated`** i **`LostFocus`** — to drugie podnoszone **przez samą
  zmianę widoczności** (ukrycie przodka zabiera fokus), więc nie zależy od tego, czy pasek zakładek jest
  fokusowalny.
  ⭐ **Kształt poprawki wart skopiowania: DWA niezależne wyzwalacze, JEDNA decyzja, a decyzją jest
  NIEZMIENNICZOŚĆ** (`karta otwarta && !editor.IsEffectivelyVisible`), nie proxy — więc wyciszenie jednego
  wyzwalacza degraduje, a nie psuje, a przejście fokusu na inną kontrolkę **tej samej widocznej zakładki**
  karty nie zamyka. ⛔ Bezwarunkowe „hide on LostFocus" (kuszący jednolinijkowiec) świadomie odrzucone.
  ⚠ Ta sama niezmienniczość bramkuje drogę **DO ŚRODKA** — `ShowAt` ma asynchroniczne dogrzanie metadanych,
  które może wrócić już po przełączeniu, do tej samej wspólnej warstwy.
  ⛔⛔ **NIE ruszaliśmy `SignatureHelpEngine` — decyzja użytkownika.** Zmierzone i **wycofane w całości**:
  kliknięcie NAZWY funkcji rozwiązuje wywołanie **otaczające** (a przy jednym poziomie zagnieżdżenia — nic),
  bo regułą silnika jest *„wygrywa najgłębszy OTACZAJĄCY nawias"*, a nazwa leży poza własnymi nawiasami.
  Użytkownik ratyfikował istniejące zachowanie. ⛔ Nie wyprowadzać tego ponownie jako defektu.
  ⛔ **QA end-to-end należy do użytkownika** — `ParameterHelper` wymaga prawdziwego `TextEditor`, którego
  statyczna inicjalizacja keymapy rzuca w sesji headless (#226, zmierzone ponownie), więc testy przypinają
  **PRZESŁANKI** (jedna warstwa · brak detach · niezmienniczość się przełącza · oba wyzwalacze odpalają ·
  `IsEffectivelyVisibleProperty` nadal nieobecne), nie zachowanie.

- **🐞🔧 DWA DEFEKTY Z JEDNEGO ZGŁOSZENIA — NAPRAWIONE 2026-08-10, ⏸ czeka na potwierdzenie użytkownika
  w działającej aplikacji.** Zgłoszenie: *„autoformater sobie nie radzi"* + *„jedna `zasobtechcrp` nie ma
  koloru"* na jednej procedurze. **Oba okazały się szersze niż zgłoszenie.** Build 0/0; suite **8649**
  (8387 + 207 + 55, +107); smoke czysty; `Lab/` nietknięty. Narracja:
  **[docs/history/29-formatter-and-psql-dml-binding-fix.md](docs/history/29-formatter-and-psql-dml-binding-fix.md)**.
  Nowe gotchy **#359–#360**.
  ⭐⭐ **DEFEKT A — w ciele PSQL ŻADEN cel DML nie był wiązany.** `BindBodyStatement` był **drugą, równoległą
  ścieżką** nad tymi samymi pięcioma rodzajami instrukcji: wiązał podzapytania i **nigdy nie deklarował CELU**.
  Zmierzone: na top-level wiąże się `update`/`insert`/`delete`/`update or insert`/`merge`, w ciele procedury
  **ani jeden** — czyli **większość kodu w bazie ERP**. Jedno słowo wyglądało źle tylko dlatego, że w tej
  procedurze jedynie ta instrukcja nazywa tabelę występującą też gdzie indziej.
  ⭐ **Gorsza połowa jest niewidoczna:** bez zadeklarowanego aliasu `BindDottedReference` świadomie milczy na
  nierozwiązanym kwalifikatorze, więc CAŁA instrukcja traciła referencje `alias.kolumna` — brak koloru, Quick
  Info, Ctrl+Click, find-references **i** kontroli nieznanej kolumny. ⚠ Skutek: ET0002 może się teraz pojawić
  w DML wewnątrz procedury, gdzie wcześniej było cicho — to wiązanie działające poprawnie.
  ⭐ **Naprawa likwiduje równoległą ścieżkę, nie dopisuje brakującej połowy:** jeden właściciel
  `BindDmlTablesAndQueries`, pięć ramion w binderze PSQL → jedno. ⛔ Nie dodawać tam ramion per rodzaj —
  pozycja celu (`UPDATE t` / `INSERT INTO t` / `DELETE FROM t` / `MERGE INTO t USING src`) to jedna reguła.
  ⚠ Instrukcja dostaje **własny scope potomny** (alias nie wycieka do sąsiada), którego **rodzicem jest scope
  ciała** (dzięki temu `:zmienne` nadal się rozwiązują) — jedno i drugie przypięte.
  ⭐⭐ **NAJMOCNIEJSZE ZNALEZISKO (#360): suita, która to miała pilnować, ISTNIEJE DOKŁADNIE DLA TEGO OBJAWU**
  (*„obiekt kolorowany w FROM, ale nie w UPDATE"*) i pilnuje wszystkich pięciu rodzajów DML — **tylko na
  top-level**; sąsiednie wiersze dla ciała procedury pilnują **ZAPYTANIA**. Dwie niezależne osie (rodzaj ×
  zagnieżdżenie), każda zmieniana przy ustalonej drugiej ⇒ skrzyżowania nigdy nie było, i tam mieszkał defekt.
  Słowo *„position"* w jej dokumentacji po cichu znaczyło *pozycję klauzuli*, nigdy *zagnieżdżenie*.
  Podsadzenie: **8 nowych skrzyżowanych przypadków czerwonych, 14 istniejących zielonych.**
  ⭐⭐ **DEFEKT B — formater gubił komentarz, a §0 zamieniało to w „nic nie robi" (#359).** `EmitSelectQuery`
  renderuje zapytanie z **KLAUZUL**, a komentarz materializuje się z **triwii tokenu, który poprzedza** — więc
  komentarz między ostatnią klauzulą a tym, co zapytanie **ZAMYKA** (`;` / `INTO` / `DO`), nie należał do
  nikogo. Siatka §0 wykrywała stratę i zostawiała instrukcję dosłowną — poprawnie, i to właśnie ukrywało
  defekt: **jeden `--` przed `into` zamrażał 30-linijkową procedurę**, a wyglądało to na bezczynność.
  ⚠⚠ **Każdy test zachowania leksemów był zielony przez całe życie defektu**, bo cofnięcie do dosłownej
  zachowuje leksemy idealnie. Asercją, która potrafi zapalić, jest **„siatka NIE zadziałała"** (wejście
  niekanoniczne ⇒ wyjście MUSI się zmienić) — ten sam kształt co
  `UpperKeywords_ActuallyReCase_AndDoNotTripTheSafetyNet`.
  ⚠ Drugi powód przeżycia: wspólny `SqlTestCorpus` **nie miał prawie żadnych komentarzy** — dopisano 8
  kształtów, więc teoria round-trip/idempotencji/casingu pilnuje ich już na stałe.
  ⚠ **Pierwsza hipoteza („`EmitQuery` gubi komentarze") została OBALONA pomiarem** — komentarz wewnątrz
  klauzuli zawsze przechodził; trzeba było odczytać wyjście **przed** siatką (prywatne `FormatStatement` jest
  czystą funkcją właśnie po to).
  ⭐ **Czwarte miejsce z tym samym defektem znalazł KOMPILATOR** (ciało `CREATE VIEW`), gdy jednozadaniowe
  `WithSemicolon` zostało zastąpione wspólnym `EmitQueryTail` — zmiana KSZTAŁTU wylicza swoje miejsca użycia.
  ⚠ **Widoczna zmiana zachowania:** taki komentarz trafia teraz na **własną linię** nad `into`, nie na koniec
  linii `where` (klauzula `WHERE` sama się łamie, więc „koniec where" nie jest stabilnym miejscem). Komentarze,
  które działały (w klauzuli, w liście `SET`, po `;`), są nietknięte — przypięte w obie strony.
  ⚠⚠ **Nota procesowa: pierwszy pełny przebieg dał 13 niepowodzeń i ŻADNE nie było moje** — ręcznie przepisany
  filtr partycji miał **12** nazw klas headless, a kod ma **18**, więc sześć klas wyciekło do partycji głównej
  i trafiło w udokumentowany wyścig o globalny katalog `Loc`. ⭐ **Filtr wyprowadzaj z kodu, nie przepisuj:**
  `grep -rln HeadlessCollection tests/EmberTern.Tests/*.cs`.

- **🌍🚧 LOCALIZATION / CORE + FIREBIRD — ETAP W TOKU. C0 (audyt), C1 (`SessionHealthAnalyzer`),
  C2 (`QuickInfoEngine`), C3 (`FirebirdConnectionService`), **C4a** (`ApplicationSettingsStore`) i **C4b**
  (`Settings/Export`, ⭐ tym samym CAŁE C4) i **C5** (`DiagnosticsEngine`) — ODEBRANE (2026-08-10).** Gałąź
  `feat/localization`; ⛔ **nadal NIE scalona do `master`.** Build 0/0; suite **8 542** (8 280 + 207 + 55);
  smoke czysty; `Lab/` nietknięty. ⏭ **Następny: C6 — `ExecutionSummary` / `ExecutionActivity`**, i ⛔⛔ **znów
  od PROPOZYCJI KSZTAŁTU KONTRAKTU, nie od kodu**: te zdania są **SKLEJANE** (`"{0} {1} {2}"` z liczbą,
  `"row"/"rows"` i czasownikiem), więc migracja klucz-za-klucz jest niemożliwa i to ten moduł **wymusza
  mechanizm liczby mnogiej** (licznik przypadków: **5** — C5 nie dodało żadnego). Narracja:
  **[docs/history/28-localization-core-stage.md](docs/history/28-localization-core-stage.md)**. Nowe gotchy
  **#353–#358**.
  ⭐⭐ **C5 — pierwszy moduł, którego KSZTAŁT KONTRAKTU był ratyfikowany PRZED napisaniem linii kodu**, i to
  była właściwa kolejność: warunkiem migracji było nadanie `LocalizableMessage` **równości strukturalnej**.
  Równość generowana rekordu porównywała `IReadOnlyList<object?>` **po referencji**, więc dwa identyczne
  komunikaty były nierówne — nieszkodliwie przez cztery etapy, bo **nikt ich nie porównywał**, i groźnie
  w chwili osadzenia w `Diagnostic` (`readonly record struct`, którego równość `DiagnosticsPanelViewModel.Update`
  używa, żeby NIE przebudowywać kolekcji i NIE gubić zaznaczenia). Panel churnowałby przy **każdym ticku
  debounce**, przy zielonym buildzie. Gotcha **#358**.
  ⭐ **Naprawione u NOŚNIKA, nie u konsumenta** — wariant „nośnik o stałej arności w `Diagnostic`" odrzucony
  (sufit arności to defekt zaplanowany na później). ⭐⭐ Bezpieczne, bo **zmierzone**: grep dał **zero**
  konsumentów równości tego typu, więc zmiana mogła tylko zrównać więcej rzeczy, nigdy mniej.
  ⚠ Przesłanka nowej równości jest **pilnowana**: argument musi być wartościowo porównywalny (`string`, liczba);
  podsadzenie `char[]` zapala strażnika.
  ⭐⭐ **PODSADZENIE A jest najmocniejszym wynikiem etapu: przywrócenie równości referencyjnej zapala 7 testów,
  w tym DWA ISTNIEJĄCE** (`Update_WithUnchangedDiagnostics_DoesNotRebuildTheCollection`, `…_KeepsTheSelection`)
  — czyli ochrona nie jest czymś, co C5 wymyśliło, tylko czymś, co C5 **zachowało**.
  ⭐ **Bez bliźniaka angielskiego, i to POMIAR:** `DiagnosticsEngineTests` asertuje `Category`, `Code`,
  `Severity`, `Start`, `Length` i **ani razu treści** ⇒ nie było czego przypinać. Kształt **C2** (zmienia się
  typ), nie C4a. Dowód zerowej zmiany: **diff pusty, 9 == 9**, mechanicznie.
  ⛔ **`ET0008` = DWA klucze** (rzeczownik „trigger"/„function" nie jedzie jako argument — reguła C3), i to
  właśnie dlatego klucz **mieszka w strukturze**, a nie jest wyprowadzany z `Category`: jedna kategoria daje dwa
  zdania, więc mapa nie jest 1:1. ⭐ Rozstrzyga to projekt: `Category` mówi *jakiego RODZAJU* to problem (na tym
  rozgałęzia się `QuickFixEngine`), klucz mówi *które ZDANIE*.
  ⛔ **`ET0004` jest NIEOSIĄGALNE — zmierzone znalezisko, nie luka:** żadna ścieżka bindera nie tworzy
  nierozwiązanej referencji o roli `Parameter` (wszystkie oddają `Variable`). ⭐ Potwierdzone niezależnie:
  **w całym suite nie ma testu ET0004** — tak wygląda nieosiągalna kategoria z zewnątrz i nikt tego nie zauważył.
  Nazwany wyjątek z **przypiętą przesłanką** (sześć kształtów PSQL).
  ⭐⭐ **Live switching W3 i pułapka, dla której istnieje:** panel ma **jedną** subskrypcję i odświeża `Message`
  istniejących wierszy. ⛔ Oczywista naprawa — przebudowa i republikacja — jest **POŻERANA przez `Unchanged()`**,
  bo po zmianie języka znaleziska są te same; optymalizacja chroniąca zaznaczenie zjadłaby odświeżenie.
  Podsadzenie tej wersji zapala test z komunikatem *„the row was never told its text changed"*.
  ⭐ **Hover bez mechanizmu — NIEOSIĄGALNE (zmierzone, #346):** kartę zdejmuje `PointerExited` na `TextView`
  **oraz** dowolne kliknięcie, a dotarcie do wiersza Language wymaga obu.
  ⚠⚠ **Inwentarz z mojej propozycji pominął DWÓCH konsumentów** (`DebugPreflight`, `FirebirdGrammarCorpusTests`)
  — oba znalazł **kompilator**, bo zmiana TYPU wylicza swoje miejsca użycia. ⭐ To ukryta zaleta zastąpienia nad
  formą dualną: dodanie właściwości obok zostawiłoby oba cicho po angielsku na zawsze.
  ⭐⭐ **C4b — 20 kluczy, i najważniejsza linia jest o tym, czego NIE dodano: dwie odmowy store'a są
  PRZEWLECZONE, nie powtórzone.** `CanSave(out,out)` i `LastSaveMessage` — jedno i drugie dodane w C4a
  dokładnie na to — niosą odmowę przez applier, więc dialog importu pokazuje **zdanie store'a z klucza
  store'a**. Drugi klucz o tej samej treści byłby dwiema odpowiedziami na jedno pytanie i rozjechałby się przy
  pierwszym przeredagowaniu.
  ⭐ **Rodzina `Damaged*` to CZTERY CAŁE ZDANIA**, nie prefiks + fragment (prefiks przyklejony do fragmentu nie
  przetłumaczy się na język fleksyjny). ⭐ Angielska połowa **nadal jest SKŁADANA** tym samym prefiksem — celowo:
  dzięki temu strażnik równości dowodzi, że wpis zasobu **odtwarza konkatenację**, a nie że ktoś poprawnie
  przepisał zdanie. Przepisanie byłoby jedynym sposobem zmienić treść niewidocznie.
  ⛔ **Strona EKSPORTU jest poza zakresem i to POMIAR, nie pominięcie:** `SettingsExporter` ma dwa komunikaty,
  oba `ArgumentException`, a `CanExport` bramkuje oba warunki ⇒ nieosiągalne z UI; opakowanie błędu jest już
  zlokalizowane w App, a treść wewnętrzna należy do platformy.
  ⚠ **Jeden klucz jest dziś NIEOSIĄGALNY** (`NoMigrationStep` — ladder wymaga `Oldest < Current`, oba = 1) i ma
  **nazwany wyjątek z PRZYPIĘTĄ PRZESŁANKĄ**: `TheOnlyUnreachableKey_IsStillUnreachable` pilnuje równości wersji,
  więc dzień, w którym powstanie format v2, zapala ten test i każe dopisać scenariusz (#322 — pilnuj przesłanki,
  nie polityki).
  ⭐ **Live switching NIE dostał haka — i to jest ustalenie, nie brak.** Zmierzone: preferencja języka ma
  **jednego pisarza** (wiersz Language w Settings Center), a dialog importu jest `ShowDialog` **nad tym właśnie
  oknem**, więc język nie może się zmienić, gdy dialog jest otwarty; a gdy sam import niesie inny język,
  `SettingsPortability.Apply` przełącza go **przed zwróceniem wyniku**, czyli komunikat składa się już po
  przełączeniu (ta sama „poprawność przez kolejność" co baner Settings Center w C4a). ⛔ Zapisane w miejscu:
  gdyby dialog przestał być modalny, rozumowanie wygasa.
  ⚠⚠ **ZNALEZISKO #357 — strażnik równości wzorca dualnego ma NIEUŚWIADOMIONĄ PRZESŁANKĘ, a wersja mechanizmu,
  którą zapisałem najpierw, była BŁĘDNA.** Twierdziłem (i zaraportowałem jako pomiar), że `CurrentCulture`
  pogrupuje argument `2000000000` i strażnik będzie czerwony na maszynie polskiej. ⭐ **Podsadzenie argumentu
  liczbowego nie zapaliło NICZEGO**: `{0}` z `int` **nie** stawia separatorów grup — kultura rządzi separatorem
  dziesiętnym i minusem, nie grupowaniem liczby w formacie `G`. Prawdziwą dźwignią (i źródłem `48 102` z #354)
  jest **`{0:N0}` w wartości zasobu**. ⚠ To przenosi zagrożenie z MASZYNY na TŁUMACZA: angielska połowa jest
  literałem u producenta, zlokalizowana jest wpisem, który ktoś będzie edytować. Stąd: liczba będąca **ECHEM
  pola technicznego** jedzie jako **string invariantny** (specyfikator staje się bezczynny); ⛔ liczba, którą
  czytelnik **liczy**, nadal idzie za jego kulturą — #354 stoi.
  ⭐⭐ **I test musiał się zmienić razem z historią:** `TheEnglishAndLocalizedForms_AgreeOnAnyCulture` wyglądał
  rygorystycznie (4 kultury × wszystkie komunikaty) i **nie mierzył tego, co obiecywał**; został z uczciwą
  etykietą „szeroki test niezmienności", a przypadek liczbowy pilnuje
  `EveryArgument_IsAlreadyFormatted_SoNoFormatSpecifierCanChangeIt`, który podaje **wrogi szablon `{0:N0}`** pod
  `pl-PL` i wymaga, żeby argument przeszedł dosłownie. Po ponownym podsadzeniu: **dokładnie jeden test czerwony,
  dziewięć zielonych.**
  ⭐ **Dowód zerowej zmiany treści jest znów NIETKNIĘTYMI TESTAMI** (`SettingsExportFormatTests`,
  `SettingsImportApplyTests` — w tym `Assert.Contains("Refusing to overwrite", result.Message)`, czyli
  przewleczona odmowa store'a).
  ⚠ **Siedem podsadzeń, każde zapala swojego i tylko swojego strażnika** (numeryczny argument · dryf słowa
  w zasobie · zdjęty prefiks `Damaged` · dwa zdania na jednym kluczu · `CurrentFormatVersion = 2` · przemianowany
  klucz bez wpisu · dialog pokazujący nierozwiązany angielski).
  ⚠ **Znany flake zapalił się w 3 z 6 przebiegów partycji głównej** (`SettingsLoadHealthTests.ConcurrentSaves_…`,
  `Assert.Empty`), przy 3/3 zielonych solo i trzech przebiegach z pełnym 8 280. ⛔ Nie ogłaszany naprawionym ani
  powiązanym: ślad C4b to 7 plików i **nie ma wśród nich `ApplicationSettingsStore`, `AtomicWrite` ani blokady
  międzyprocesowej** — czyli dokładnie tego, co ten test ćwiczy. ⚠⚠ Częstotliwość wygląda wyżej niż wcześniej
  notowana; jeśli się utrzyma, zasługuje na własne zadanie, nie na kolejny przypis.
  ⭐ **Jedna korekta prawdy poza migracją:** doc `LocalizableMessage` twierdził *„seam nie ma jeszcze PRODUCENTA
  w Core … ⛔ nie zaczynaj migrować"* — prawda, gdy powstał, i fałsz od C1. Zostawiony mówiłby następnemu
  autorowi coś odwrotnego niż zrobiły cztery odebrane etapy, i to na typie, którego właśnie by używał (#284
  w kształcie komentarza przy samym seamie).
  ⭐⭐ **C3 — wzorcowy D‑3, cała granica w jednej linii: nasze zdanie to KLUCZ, zdanie serwera to ARGUMENT.**
  `Firebird.Connection.Failed` = *„Could not connect to {0}: {1}"*, gdzie **`{1}` to dosłownie to, co
  powiedział Firebird**. ⭐ `ConnectionFailedException` niesie **oba** kształty: `Localized` dla trzech
  powierzchni App, `Message` po angielsku dla logów i nieprzemigrowanych ścieżek — dzięki czemu nieznana
  ścieżka degraduje się do **dokładnie dzisiejszego zachowania**, nigdy do gorszego. ⚠ Duplikacja jest
  **pilnowana**, nie tolerowana (strażnik wymaga zgodności obu po angielsku) — i ten sam strażnik jest
  **dowodem zerowej zmiany treści, mocniejszym niż diff tekstowy**, bo porównuje z NIETKNIĘTYM producentem.
  ⛔⛔ **`Legacy_Auth` nadal rozpoznawany po tekście SERWERA** — przypięte **pod zmianą języka**, bo objaw
  przepięcia na nasz zlokalizowany tekst jest w angielskim **niewidoczny**. Podsadzenie kompiluje się
  z **0 błędów** i zapala trzy testy.
  ⭐ **Rozszerzenie strażnika o assembly Firebird, zrobione prewencyjnie w C1, zadziałało bez żadnej zmiany**
  — C3 jest pierwszym producentem w tym assembly i podsadzenie przemianowanego klucza zapala wszystkie trzy
  strażniki katalogu.
  ⭐⭐ **C2 — migracja okazała się PODZIAŁEM, nie sweepem:** fakt Quick Info to para `etykieta : wartość`
  o **dwóch różnych właścicielach** — etykieta (18 kluczy) jest nasza, **wartość zostaje dosłowna**, bo to
  słownictwo Firebirda (`NOT NULL`, `PRIMARY KEY`, `BEFORE INSERT`), a karta, która by je przetłumaczyła,
  **nie zgadzałaby się z DDL-em, który opisuje**. Zero zmiany treści: 18 starych literałów == 18 wartości
  w katalogu, **diff pusty**.
  ⭐⭐ **Znalezisko C2 (#355): zmiana typu `string` → `MessageKey` NIE zepsuła miejsca, które go SKLEJAŁO.**
  `new Run(fact.Label + "  ")` kompilowało się dalej (konkatenacja bierze `ToString()`) i wypisałoby
  `QuickInfo.Fact.Nullability` na karcie hover. ⚠ **Żaden strażnik kluczowy tego nie widzi** — wszystkie
  rozwiązują klucz same, więc testują katalog, nie ekran; łapie to dopiero strażnik czytający
  **zrealizowaną kontrolkę**. Podsadzenie: build **0 błędów**, 4 z 5 testów zielone, ten jeden czerwony.
  ⭐ **C2 nie potrzebowało instalacji live switchingu i to jest ZMIERZONE**: karta Quick Info jest budowana
  od nowa przy każdym najechaniu i każdym wyborze wiersza uzupełniania (inaczej niż karty SessionHealth,
  które żyją w kolekcji).
  ⛔⛔ **OTWARTE PYTANIE Z C2 — wartości faktów będące NASZYMI słowami** (`KindLabel` ×3, `"Variable"`,
  `"Active"/"Inactive"`, `"Identity"/"Computed"`, `"Input parameter"`). ⭐ `QuickInfoEngine.KindLabel` to
  **PIĄTA kopia słownictwa, które etap App już skonsolidował** (`QuickInfoView.KindLabel` → `ObjectKind*`,
  już zlokalizowane). ⛔ **Nie deklarować kluczy rodzajów w Core** — to byłaby druga kopia słów, które App
  już ma; właściwy kształt to oddać `SymbolKind` jako daną (reguła enum-vs-`MessageKey`). ⚠ Zmienia kontrakt
  `QuickInfoFact`, więc **decyzja użytkownika**. ⚠ Koszt bieżący: polski czytelnik zobaczy *„Rodzaj: Table"*,
  **podczas gdy drzewo metadanych nazywa to samo „Tabela"**.
  ⭐⭐ **WYNIK C0, wart więcej niż sam etap: odziedziczone „≈280 komunikatów" NIE PRZETRWAŁO POMIARU — trzy
  z dziesięciu pozycji inwentarza opisywały coś innego, niż twierdziły.** `CharsetCatalog` „8 opisów
  zestawów znaków" → **0** (to NAZWY charsetów Firebirda, klasa D) · Data Import „~20 błędów wierszy" → **0**
  (moduł **był już zbudowany poprawnie**: `ImportRowError` niesie enum, a komentarz mówi wprost *„as a code —
  **never a message**"*) · `FirebirdDiagnostics` „24" → **0** klasy A (klasa deklaruje o sobie *„No UI"*,
  wszyscy konsumenci piszą do `%TEMP%`). ⭐ Realny zakres: **~170–190**, i za każdym razem korekta zmieniała
  KSZTAŁT pracy, nie tylko liczbę.
  ⭐ **Drugi, ratyfikowany mechanizm na ten sam problem JUŻ istniał** (`ExportUnavailableReason`,
  `ImportDiagnosticCode` — enum kodu → `UiStrings`). 🔒 Reguła: **enum**, gdy zbiór jest zamknięty i App może
  chcieć się rozgałęzić; **`MessageKey` + argumenty**, gdy komunikat niesie dane dynamiczne. ⛔ Istniejących
  poprawnych enumów NIE migrujemy.
  🔒 **Kolejność ratyfikowana:** `SessionHealthAnalyzer` ✅ → `QuickInfoEngine` → `FirebirdConnectionService` →
  `Settings/Export` → `DiagnosticsEngine` → `ExecutionSummary`/`ExecutionActivity` → `Office ×2` → dopiero
  potem decyzja o **Performance** (moduł ZAMKNIĘTY, nieotwierany).
  **C1 as-built:** `SessionHealthFinding` → `LocalizableMessage` (16 kluczy w `SessionHealthMessages`), dane
  jako argumenty. ⭐⭐ **Zero zmiany treści DOWIEDZIONE** porównaniem z `git show HEAD`: jedyne różnice to pięć
  niemigrowanych nagłówków werdyktu i trzy przekształcenia interpolacji w placeholder, gdzie **tekst dosłowny
  wokół nich jest niezmieniony co do znaku**.
  ⛔⛔ **`SessionHealthVerdict.Headline` ŚWIADOMIE zostaje `string`iem — decyzja użytkownika: NIE projektować
  mechanizmu liczby mnogiej dla dwóch komunikatów.** Zbieramy przypadki z kolejnych modułów
  (`ExecutionSummary`, QuickInfo „1 column"), potem **jeden wspólny** mechanizm. ⚠ Zmierzone ograniczenie:
  strażnik `NoCode_BranchesOnAParticularLanguage` skanuje `App/Localization/**`, więc tablica reguł
  per-język **go zapali**. Powód zapisany w kodzie, przy typie, razem z zakazem „dokończenia".
  🔒 **`48,102` → `48 102` jest ZACHOWANIEM OCZEKIWANYM, nie regresją** (ratyfikowane): `Loc.Format` formatuje
  pod `CurrentCulture` z założenia. ⛔ Nie cofać do grupowania invariantnego. Każdy kolejny migrowany
  producent dziedziczy tę zmianę — **#354**.
  ⭐ **Defekt WCZEŚNIEJSZY ujawniony przez migrację (#353): łańcuch zmiany języka kończył się na
  `WorkspaceTabViewModel`** — treść każdego rodzaju wisi na osobnym VM, którego bindingi są na DZIECKU, więc
  `GradeText` i `GapStatusText` były zamrożone w języku startowym **już przed tym etapem**. ⚠ Naprawa rośnie
  o linię na moduł.
  ⚠ **Katalog ma teraz DWÓCH właścicieli, więc strażnik jest PODZIELONY, nie osłabiony** (klucz Core zawiera
  kropki, więc nie może mieć składowej `UiStrings`). Dyskryminatorem jest **refleksja**, nie konwencja kropki;
  obie partycje strzeżone w OBU kierunkach + nowy `EveryCoreShapedEntry_IsDeclaredByCore`. ⭐ Podsadzenie
  zapaliło **trzy** strażniki — źle napisany klucz Core wpada do partycji App i tam też jest łapany, więc
  **między połówkami nie ma szczeliny**. Skan kluczy objął też assembly **Firebird** (luka z audytu).
  ⚠ **Filtr partycji headless ma 13. nazwę** (`SessionHealthLocalizationTests`) — rośnie z każdym modułem,
  a objaw pominięcia jest **utajony** (wyścig o globalny katalog `Loc`), nie czerwony test.
  ⭐⭐ **C4a — wzorzec DUALNY z C3, ale tu z innego, zmierzonego powodu:** komunikaty odmowy zapisu to
  user-visible połowa powierzchni **reguły #11** (odmowa nadpisania pliku, który może trzymać jedyną kopię
  wszystkich połączeń i haseł), a ich DOKŁADNE brzmienie przypina ~20 istniejących testów. Przepisanie typu
  zamieniłoby strażnik dowodzący, że *użytkownik dostaje właściwe zdanie*, na taki, który dowodzi, że
  *wybrano klucz*. ⭐⭐ Skutek: **dowodem zerowej zmiany treści są NIETKNIĘTE testy** — najmocniejszy w całym
  etapie.
  ⛔ **`SettingsLoadResult` świadomie NIE dostał komunikatu** — to `readonly record struct`, więc
  `LocalizableMessage` zdegradowałby jego równość wartościową do porównania referencji listy argumentów
  (dokładnie pułapka zmierzona w C0 §3.1). App czyta `ConnectionProfileStore.SettingsMessage`.
  ⭐ **Live switching: jedna powierzchnia była JUŻ poprawna — przez KOLEJNOŚĆ.** Baner odmowy w Settings
  Center składa się PO tym, jak `Apply()` podniosło `Changed`, czyli po przełączeniu języka; baner
  settings-health w `MainWindow` był zamrożony (#353) i dostał jeden składacz wołany z haka języka.
  ⚠ *(Zapis historyczny: po C4a zakres C4 był zrealizowany w POŁOWIE — `Settings/Export` był nie zaczęty.
  ⭐ Ujawnił to samo-uzbrajający się strażnik: plik kluczy dla eksportu bez wpisów i producentów zapalił
  `EveryCoreMessageKey_HasAnEnglishEntry`, więc został usunięty zamiast zostawiony jako półprodukt — i to jest
  reguła, nie epizod: **nie twórz pliku kluczy, dopóki nie migrujesz producentów w tym samym kroku**. Druga
  połowa weszła jako **C4b**, opisane wyżej.)*
  ⛔ **Licznik przypadków liczby mnogiej: 5** (2 nagłówki werdyktu · `"1 column"/"N columns"` w QuickInfo ·
  `ExecutionSummary`). Mechanizm projektujemy dopiero na pełnym zestawie.
  ⏸ **TEMAT ODŁOŻONY decyzją użytkownika (2026-08-10): `KindLabel` / `SymbolKind`** — ~8 wartości faktów
  QuickInfo będących NASZYMI słowami. ⛔ **Nie otwierać przy okazji innego etapu**: to decyzja
  **kontraktowa** (`QuickInfoFact`), nie sprzątanie. ⭐ Kierunek jest znany — Core przestaje produkować SŁOWO
  i oddaje `SymbolKind` jako daną; ⛔ **nie** deklarować kluczy rodzajów w Core (byłaby to druga kopia
  słownictwa `ObjectKind*`, które App już ma).

- **🔒🌍 LOCALIZATION / APP — ETAP ZAMKNIĘTY I ODEBRANY (2026-08-09).** Gałąź
  `feat/localization`; ⛔ **NIE scalona do `master`.** Build 0/0; suite **8 499** (8 280 + 164 + 55); smoke
  czysty; `Lab/` nietknięty. Dokument etapu: **[docs/design/localization.md](docs/design/localization.md)**;
  ⭐⭐ punkt startowy następnej sesji:
  **[docs/design/localization-app-stage-handover.md](docs/design/localization-app-stage-handover.md)**.
  🔒 **Ratyfikowane:** **D‑1** język zmienia się **NA ŻYWO**, bez restartu · **D‑2** `.resx`/`ResourceManager`,
  angielski jako zestaw neutralny (⛔ połowa reguły #6 zakazująca `.resx` **świadomie uchylona**, reszta stoi) ·
  **D‑3** Core/Firebird oddają `MessageKey` + argumenty, słowa rozwiązuje App.
  **Liczby:** katalog **2 186** wpisów · `UiStrings` **2 186 property, ZERO pól** · **1 263** miejsc
  `{app:Loc}` · ⭐ **0 zaszytych user-visible w XAML i w App C#** · ⭐ **0 zmienionych wartości angielskich**.
  ⭐⭐ **Zerowa zmiana wartości jest DOWIEDZIONA, nie deklarowana** — zrzut wszystkich wartości **tak, jak
  wyliczył je KOMPILATOR**, katalog wygenerowany z tego zrzutu, po migracji porównanie każdej składowej
  z powrotem. Parsowanie źródła dałoby dowód okrężny. ⚠ Pierwszy przebieg dał **22 rozjazdy i wszystkie były
  błędami NARZĘDZIA**: Python w trybie tekstowym przepisał `
`→`
` w 11 stringach, `unicode_escape`
  zamienił 4 półpauzy w mojibake, 4 klucze wypadły — **żadnego nie było widać w diffie źródła**.
  ⭐⭐ **ZNALEZISKO ARCHITEKTONICZNE: standardowy wzorzec z INDEKSEREM jest w Avalonii 12.1.1 MARTWY.** Jeden
  obiekt z `this[key]` zbudowano i **zmierzono na prawdziwym `TextBlock`**: wartość początkowa binduje się
  poprawnie, a po zmianie języka kontrolka pokazuje **stary tekst**; ani `"Item[]"`, ani `string.Empty` nie
  docierają do bindingu po indekserze. ⛔ **Nie upraszczać z powrotem** — wersja z indekserem renderuje się
  poprawnie przy pierwszym załadowaniu, więc awaria się chowa. Stąd **jeden mały obiekt powiadamiający na
  KLUCZ** (`LocalizedString.Value`).
  ⭐ **Trzy formy składowej, tylko jedna dopuszczalna:** `const` (inline'owana — nie ma czego rozwiązywać) ·
  `static readonly` (raz, potem **zamarza w pierwszym języku**) · **property** ✅. W XAML analogicznie:
  `{x:Static}` **nie jest bindingiem**, obowiązuje `{app:Loc}`. ⚠ Koszt zamiany powiedziany wprost: **utrata
  sprawdzania klucza przez kompilator**, rekompensowana strażnikiem.
  ⭐ **Powierzchnie „capture-once" rozwiązano DWOMA sposobami, i binding jest lepszy od subskrypcji:** kolumny
  `DataGrid` budowane w kodzie **bindują** `HeaderProperty` (`LocalizedColumn.Header`), wiersz IntelliSense
  **przestał cache'ować** opis rodzaju; `Loc.LanguageChanged` obsługuje tylko to, czego binding nie dosięga
  (tekst, który VM wylicza raz i publikuje — `MainWindowViewModel` + wszystkie zakładki).
  ⛔⛔ **188 wartości z wieloma kluczami ŚWIADOMIE NIE SCALONO — decyzja użytkownika.** `"Delete"` ma 12
  właścicieli, `"Cancel"` 11. To w większości **różne pojęcia dzielące angielskie słowo**, a język fleksyjny
  odmieni je różnie; scalenie byłoby **defektem lokalizacyjnym udającym sprzątanie**. ⭐ Zasada:
  **w lokalizacji kontekst jest ważniejszy niż mechaniczna deduplikacja.**
  ⚠⚠ **DODANIE JĘZYKA MA TRZY KROKI, NIE DWA — i to koryguje mój własny wcześniejszy zapis.** Dokument
  twierdził „wiersz w katalogu + plik `.resx`". Zmierzone przez faktyczne dodanie `pl`: **36 testów padło,
  wszystkie z jednej przyczyny** — wiersz `SettingLanguage` w `SettingsCatalog` ma mapę `klucz → etykieta`
  indeksowaną wprost, więc język bez ETYKIETY rzuca `KeyNotFoundException` przy budowaniu strony Settings.
  ⭐ Złapał to **istniejący** strażnik `EveryEnumeratedOptionHasALabel` — mechanizm zadziałał, nieprawdziwy
  był opis.
  ⚠⚠ **UTAJONY WYŚCIG O GLOBALNY STAN `Loc`:** sonda liveness podmienia katalog procesowo, a xunit
  zrównolegla kolekcje — uruchomienie obu filtrów naraz dało `AboutAuthorFormat` renderujące tekst pustego
  paska bocznego. W udokumentowanych partycjach nie zachodziło. Naprawa: `LocalizationMechanismTests`
  w `HeadlessCollection`. ⚠ **Filtr partycji headless ma przez to dwie nazwy więcej.**
  ⏸ **Świadomie poza zakresem:** Core/Firebird (≈280 user-visible; seam D‑3 zbudowany i przetestowany
  end-to-end, **zero producentów przeniesionych**) · polskie tłumaczenie · QA wzrokowe na żywym oknie
  (⚠ niewykonalne przy jednym języku — żywy i zamrożony binding renderują ten sam tekst).

- **⚙🔒 PAKIET UX PO M5 · PUNKT 5 — SETTINGS UX — ZAMKNIĘTY I ODEBRANY PO QA WIZUALNYM W OBU MOTYWACH
  (2026-08-10).** Build 0/0; suite **8430** (8243 + 132 + 55); smoke czysty; `Lab/` nietknięty. As-built:
  **[docs/history/27-post-m5-ux-package.md](docs/history/27-post-m5-ux-package.md) §9**. Nowa gotcha **#351**.
  Render: `dotnet run --project tools/probes/VisualCandidateProbe -- settings after`.
  🔒 Ratyfikowane: **T‑1** (nawigacja `ChromeStrongBrush` › treść `PanelBrush` › karta `BackgroundBrush`) ·
  **sześć ikon, wszystkie istniejące** (`Icon.PanelLeft` dla Tabs, `Icon.Crosshair` dla Debuggera — ⛔ **nie**
  kompozyt `DebuggerIcon`, bo byłby drugim mechanizmem rysowania ikony kategorii) · **`Size.Row.Tree` (24)**
  bez roli `Size.Row.Nav` · wskaźnik aktywnej kategorii · **cztery pozycje Easy-mode w jednej karcie**.
  ⛔ **Zero nowych tokenów** — kolorów, odstępów i ról wysokości.
  ⭐⭐ **ZNALEZISKO GŁÓWNE: to był defekt HOSTA, nie stylu karty.** `Border.settings-group` opisuje siebie jako
  *„recessed BackgroundBrush **inside the PanelBrush chrome** that hosts it"*; trzej pozostali konsumenci (oba
  dialogi ustawień, Data Import) rzeczywiście dają mu ten kontener — **Settings Center nie dawał żadnego**, więc
  karta i tło okna miały **identyczną barwę** (`#1E1E1E` na `#1E1E1E`; `#FCFCFD` na `#FCFCFD`) i całą separację
  17 kart niosła kreska 1 px. ⛔ Kusząca naprawa — wzmocnić styl — trafiłaby w jedyne miejsce, gdzie styl jest
  niewinny, i **zepsułaby trzy poprawne** (R7 w drugą stronę). Gotcha **#351**.
  ⭐ **B4 zmieściło się w istniejącej architekturze**: cztery flagi zostają czterema wierszami katalogu, zmienia
  się tylko pojemnik; jedyny dodatek to `ShowEasyModeGroup` w kształcie istniejącego `ShowTabStripMaxRows`.
  ⛔ Zero zmian w `SettingsCatalog`. Skutek uboczny: strona Editor **mieści się w całości** (6 kart → 3).
  ⭐ **Wskaźnik nie zmienia geometrii Z KONSTRUKCJI** — pasek `Size.TabIndicator` stoi zawsze, zaznaczenie
  zmienia wyłącznie barwę (wzorzec ratyfikowany w M5 / L‑1). ⛔ Wariant `BorderThickness` na `ContentPresenter`
  odrzucony: ramka wchodzi w desired size, więc rozpychałaby wiersz przy każdym kliknięciu (§13.3).
  ⭐⭐ **DWIE RZECZY ZŁAPAŁ POMIAR PIKSELI, NIE OKO:** wskaźnik **w ogóle nie działał** (`Background="Transparent"`
  lokalnie w szablonie bije setter stylu — **#342**; zmierzone `#094771` zamiast `#2D6BBF`), a render wyglądał
  wiarygodnie — brak 2 px paska wygląda po prostu jak brak paska. ⚠ Odwrotnie: **zgłosiłem defekt, którego nie
  ma** („panel treści nie bierze `PanelBrush`") i wycofałem go przed zmianą kodu — punkt pomiarowy trafił
  w stopkę.
  ⚠⚠ **Trzy istniejące strażniki padły na POPRAWNYM produkcie.** Dwa przepisywały przesłankę „jeden wiersz
  katalogu = jedna karta" (**#333**) — przeformułowane na **tożsamość zamiast licznika** (każdy wiersz musi mieć
  swoją etykietę na ekranie + kart nie może być więcej niż wierszy), ⛔ bez osłabiania i bez cofania grupowania.
  Trzeci (`TheCatalogTableContainsNoStringLiterals`) **miał rację** — klucze ikon dostały nazwy jako `const`
  obok istniejących id, a ⭐ nowy `EveryCategoryIcon_ResolvesToARealGeometry` pilnuje, żeby literówka w kluczu
  **nie usuwała ikony po cichu** (`IconGeometryConverter` zwraca `null` — #348 w kształcie ikony).
  ⏸ **Otwarte:** `Pad.Dialog` dostał pierwszego konsumenta (⚠ CLAUDE.md notuje tę rolę jako „zero konsumentów"
  przy odłożonym pytaniu o padding NAGŁÓWKÓW dialogów — inne miejsce, ale zapisane) · ⛔ dwunastu innych
  powierzchni nie ruszano.
  ⏭ **Następny: punkt 6 (Database Properties), od KROKU 0 — wyłącznie sonda pomiarowa na żywym FB5**, bez
  projektowania UX i bez implementacji, dopóki zachowanie API i bazy nie zostanie potwierdzone.

- **🗄🔒 PAKIET UX PO M5 · PUNKT 6 — DATABASE PROPERTIES — ZAMKNIĘTY I ODEBRANY (2026-08-10). ⭐ TYM SAMYM
  CAŁY PAKIET UX PO M5 JEST ZAMKNIĘTY (punkty 1–6).** Build 0/0; suite **8467** (8280 + 132 + 55); smoke
  czysty; `Lab/` nietknięty. As-built: **[docs/history/27-post-m5-ux-package.md](docs/history/27-post-m5-ux-package.md) §11**
  (krok 0 / pomiar: §10). Nowa gotcha **#352**. Render: `VisualCandidateProbe -- dbprops`.
  **Co jest w produkcie:** `Properties…` w menu kontekstowym połączenia → okno z właściwościami bazy.
  🔒 **Edytowalne wyłącznie: Sweep interval · Forced writes · Reserve space** — czyli dokładnie te, które
  pomiar wykazał jako zapisywalne ONLINE, bez wyłączności. Reszta informacyjna.
  ⭐⭐ **JEDNA OBIETNICA, NA KTÓREJ STOI BEZPIECZEŃSTWO: otwarcie okna i Apply nie wysyła NICZEGO.** To okno
  pisze do współdzielonej bazy produkcyjnej przez API **bez wycofania**, poza wszystkimi lane'ami i poza
  transakcją użytkownika. Realizacja: każda składowa `DatabaseConfigurationChange` jest nullowalna, `null` =
  *nie ruszaj* ⇒ obietnica prawdziwa **z konstrukcji**. Potwierdzona **na żywym FB5**: wysłano
  `sweep=4321 forced=<nic>`, a `ForcedWrites` po zapisie pozostało `True`.
  ⚠⚠ **Apply NIE jest atomowy — to fakt o API, nie wybór**: każde ustawienie to osobne wywołanie Services,
  więc wynik jest LISTĄ per ustawienie, a komunikat wymienia to, co **się udało**.
  ⛔ **Poza V1, każde z powodem:** **SQL dialect** (zmierzony jako zapisywalny ONLINE — read-only jest tu
  **decyzją produktową**, bo zmienia SQL samego EmberTerna) · **Page buffers** (`MON$` raportuje cache
  DZIAŁAJĄCEJ instancji, więc pole zasiane tą wartością przypięłoby bazie liczbę dziedziczoną z serwera) ·
  **Read Only** (wymaga wyłączności; usunięty w całości przy domykaniu — §11.6) · osiem kolumn FB4/FB5.
  ⭐ **Bez bramkowania wersją, i to pomiar:** cały zestaw kolumn zweryfikowany na **FB3.0.13 (ODS 12.0)**
  i **FB5.0.3 (ODS 13.1)**. ⭐ Odczyt na lane **Metadata**, zapis przez **Services API** (własne połączenie
  poza lane'ami). ⛔ `FirebirdTraceService.BuildServiceConnectionString` **nie nadaje się wprost** — buduje
  string *bez bazy*, a operacje konfiguracyjne wymagają `Database`.
  ⭐ **VM bierze czytnik i writer jako DELEGATY** — dzięki temu każda reguła tej funkcji jest testowalna
  **bez serwera**. ⚠ Menu jest **wyszarzane, nie ukrywane** (reguła z M3.4b część 2), na istniejącym
  `CanDisconnect` — zero nowej maszynerii.
  ⭐⭐ **Trzy poprawki UX z domknięcia** (§11.7): odstępy w Configuration (przyczyną była STRUKTURA — pole
  i przełączniki w jednej siatce — nie brak marginesu) · **komunikat uwierzytelniania naprowadzający na SRP**
  (⚠⚠ **odwrócenie zapisanej decyzji**: podpowiedź do `Legacy_Auth` raz usunięto, bo **orzekała** o przyczynie;
  nowa **nie orzeka nic**, więc jest prawdziwa dla każdej przyczyny — gotcha **#352**; ⚠ rozpoznanie po
  TEKŚCIE, bo ten błąd nie ma SQLSTATE ani GDS) · nawigacja Settings na **`PanelBrush`**, dokładnie tym
  zasobie co `SidebarPanel` (⚠ skutek: nawigacja i treść mają ten sam ton, granicę niesie kreska) ·
  **niełamliwe separatory liczb** w opisach Settings (`ProseNumbers` — reguła o KSZTAŁCIE: spacja z cyframi
  po obu stronach; wpięta w JEDNYM miejscu, więc obejmuje też teksty przyszłe; ⚠ wyszukiwanie czyta tekst
  surowy).
  ⭐ **Strażniki zakresu, nie błędów:** writer nigdy nie woła trzech setterów wyłączonych z V1 (⚠ wszystkie
  trzy **działają technicznie**, więc ich brak wygląda na przeoczenie) · czytnik nie nazywa żadnej kolumny
  FB4/FB5 (asercja NEGATYWNA — lista dozwolonych byłaby przepisaniem katalogu FB3, #333).
  ⏸ **Zapisane, nierozstrzygnięte:** `DatabaseApplyFailure.DatabaseInUse` straciło zmierzone uzasadnienie
  (było nim Read Only) i **nie jest już dowodliwie osiągalne** — zostawione z jawnym zastrzeżeniem.
  ⚠ Sonda pomiarowa `tools/probes/DatabasePropertiesProbe` **zostaje w repo** (decyzja użytkownika) — opis
  i uzasadnienie w `tools/probes/README.md`.

- **🔬 PAKIET UX PO M5 · PUNKT 6 · KROK 0 — SONDA POMIAROWA ZAKOŃCZONA (2026-08-10).** ⛔ **Zero kodu
  produkcyjnego** — bez dialogu, VM, menu i writerów; produktem kroku jest POMIAR. Narzędzie:
  `tools/probes/DatabasePropertiesProbe` (⭐ **sonda diagnostyczna, świadomie poza solucją, ZOSTAJE w repo**
  decyzją użytkownika — jej wartość odtworzeniowa jest realna, bo każde ustalenie jest własnością WERSJI
  silnika i sterownika). Pełny zapis: **[docs/history/27-post-m5-ux-package.md](docs/history/27-post-m5-ux-package.md) §10**;
  opis sondy: `tools/probes/README.md`. FB **5.0.3**, ODS **13.1**, sterownik **10.3.4**, baza scratch;
  ⚠ `Lab/` nietknięty (sonda z definicji zmienia nagłówek bazy).
  ⭐⭐ **TRZY USTALENIA OBALIŁY TO, CO SUGEROWAŁA LEKTURA KODU I BINARKI.** (1) **`ENGINE_VERSION` NIE jest
  zamiennikiem `FbConnection.ServerVersion`** — kontekst daje `5.0.3`, sterownik pełny banner **z nazwą
  maszyny serwera**; moja własna rekomendacja „reuse before create, nie dodawaj zapytania" **wycofana**.
  (2) **`SetAccessModeAsync` bierze `bool`, nie enum** — a refleksja STATYCZNA nie odpowiedziała w ogóle
  (7 typów z całego assembly; graf zależności wymaga prawdziwego hosta), więc pomiar musiał być
  URUCHOMIONY. Znalezienie nazwy metody w binarce to dowód o SYMBOLU, nigdy o zachowaniu — **#321**
  w tym samym kształcie. (3) **Błędne hasło do Services API zgłasza się jako `Not supported plugin
  'Legacy_Auth'`**, a nie jako błąd poświadczeń.
  ⭐⭐ **NAJWAŻNIEJSZE DLA PROJEKTU: `Page buffers` ODCZYT I ZAPIS NIE DOTYCZĄ TEJ SAMEJ RZECZY.**
  `MON$PAGE_BUFFERS` raportuje **cache DZIAŁAJĄCEJ instancji**, nie zapisany nagłówek; zmiana obowiązuje
  dopiero po **PEŁNYM ZWOLNIENIU** bazy, nie przy następnym attachmencie (rozdzielone osobnym scenariuszem
  z trzymanym attachmentem — zwykła sekwencja odczyt→zapis→odczyt tego nie rozróżnia). ⚠⚠ Konsekwencja:
  pole zasiane z `MON$` pokazuje **wartość dziedziczoną z serwera** (51200), a Apply bez żadnej edycji
  **przypiąłby ją do bazy na stałe**. ⭐ `SetPageBuffersAsync(0)` = „dziedzicz" i jest odwracalne, ale
  „dziedziczone" i „przypięte 51200" są przez `MON$` **nierozróżnialne**.
  ⭐ **`Read Only` wymaga WYŁĄCZNOŚCI — zmierzone**: SQLSTATE `40001` przy jednym otwartym attachmencie,
  sukces po zamknięciu wszystkich. EmberTern trzyma 2–3 attachmenty na profil.
  ⭐ **`SQL dialect` działa ONLINE** (3→1→3 przy otwartym attachmencie) — pozostawienie go do odczytu jest
  decyzją PRODUKTOWĄ (wpływa na SQL samego EmberTerna), nie ograniczeniem technicznym.
  ⭐ Bramka uprawnień to **`USE_GFIX_UTILITY`** (SQLSTATE `28000`) z cytowalnym komunikatem ⇒ własny
  pre-check zbędny. ⛔ `Database` w connection stringu Services jest **wymagane**, a
  `FirebirdTraceService.BuildServiceConnectionString` buduje string **bez bazy**.
  ⚠ Pułapki odczytu: `RDB$LINGER` czyta się **NULL, nie 0**; `MON$OWNER` wraca **dopełniony spacjami**.
  ⚠ Niezmierzone i zapisane jako takie: serwer zdalny (mierzone na `localhost`), FB3/FB4, `RDB$LINGER`
  jako wartość zapisywalna.
  ⏭ **Następny krok: propozycja zakresu, wyłącznie na podstawie pomiarów — bez implementacji.**

- **🧰🔒 PAKIET UX PO M5 — ZAMKNIĘTY W CAŁOŚCI (2026-08-10). Wszystkie sześć punktów odebranych po QA
  użytkownika.** Gałąź `feat/product-polish`; ⛔ **nadal NIE
  scalona do `master`.** Sześć zgłoszeń ze zwykłego używania: 1 ikona zakładki Security · 2 `Button.primary`
  w stanach · 3 Live DDL na pięciu powierzchniach · 4 Performance / Execution plan · 5 Settings UX ·
  **6 Database Properties** (osobny mini-etap; ⭐ Faza 0 zmierzyła już jego wykonalność na żywym FB5 — patrz
  historia §1). Narracja + as-built:
  **[docs/history/27-post-m5-ux-package.md](docs/history/27-post-m5-ux-package.md)**. Nowe gotchy **#347–#348**.
  Build 0/0; suite **8401** (8218 + 128 + 55); smoke czysty; render QA:
  `dotnet run --project tools/probes/VisualCandidateProbe -- qa123`.
  ⭐⭐ **WYNIK FAZY 0: trzy z sześciu zgłoszeń opisywały coś innego, niż się wydawało — i za każdym razem
  zmieniło to KSZTAŁT pracy, nie jej rozmiar.** Ikona Security **nie była defektem renderowania**, tylko
  ratyfikowaną decyzją (R‑6 / K‑final) wykonaną w połowie — przycisk przeniesiono na `AccentBrush`, zakładki
  nie, przez co była **jedyną z sześciu** zakładek narzędziowych na kolorze RODZAJU (5:1). Plan wykonania
  **nie potrzebuje parsera — parser już istnieje** (`PlanNode` niesie `Method`/`TableName`/`IndexName`/
  `Detail`), a `PlanNodeViewModel.DisplayText` zwraca `Node.RawText`, czyli **wyrzuca całą klasyfikację**;
  ⛔ dlatego highlighting SQL byłby złym narzędziem — plan Firebirda nie jest SQL-em. Live DDL to nie jedna
  powierzchnia, tylko **pięć bajtowo identycznych kopii**.
  ⭐⭐ **#347 — `Button.primary`: wariant nadpisywał `Foreground` na PRZYCISKU i na jawnych dzieciach, ale
  nigdy na `ContentPresenter`**, gdzie Fluent stawia `ButtonForeground{PointerOver,Pressed,Disabled}` —
  a presenter z treścią będącą ZWYKŁYM STRINGIEM rysuje tekst sam. Zmierzone: Light **2,04:1 → 8,33:1**;
  22 przyciski z `Content=` zagrożone, 18 z dziećmi odporne. ⭐ **Mechanizm był zepsuty w OBU motywach** —
  Dark przechodził przy 5,62:1 wyłącznie dlatego, że `ForegroundColor` jest tam prawie biały.
  ⛔ **Nie naprawiać w `FluentBridge`** (obsługuje wszystkie zwykłe przyciski, gdzie ciemny tekst jest
  poprawny).
  ⚠⚠ **Pierwsza wersja strażnika zaświeciła na CZERWONO na poprawnym produkcie**: obejmowała progiem 4,5:1
  także stan NIEAKTYWNY, który jest **świadomie przygaszony** (2,43:1 / 4,02:1, decyzja z 2026-08-03), więc
  „naprawa" oznaczałaby cofnięcie tamtej decyzji dla zadowolenia progu. **#322 popełnione wewnątrz strażnika
  pisanego przeciwko temu błędowi**; zakres zawężony do stanów akcyjnych, stan nieaktywny pilnuje asercja
  WIĄZANIA.
  ⭐ **Live DDL nie dostał pięciu kopii:** `AttachReadOnlyHighlighting` wpina tylko warstwę semantyczną,
  a paletę XSHD + `SelectionBrush` + reakcję na motyw przepisano ręcznie do **dwunastu** widoków — piąta
  powierzchnia byłaby kopią 13–17. Nowe `SqlEditorBehavior.AttachDdlPreview` = jedno wywołanie
  (semantyka + leksyka + motyw + push tekstu przez nowy `IDdlPreviewSource`).
  ⚠⚠ **Granica zapisana, nie ukryta: w czterech DIALOGACH warstwa semantyczna jest bezczynna** — jej
  metadane płyną z `Window.DataContext as MainWindowViewModel`, a tam DataContextem jest VM dialogu; dialogi
  dostają warstwę leksykalną, New Table (żyje w `MainWindow`) obie. ⛔ Dwunastu istniejących powierzchni
  **nie migrowano** — follow-up, nie zmiana przy okazji.
  ⚠ **Znalezisko z samego renderu (#348):** pierwsza wersja pokazała „PO" **bez koloru**, bo definicje XSHD
  rejestruje `App`, a sonda uruchamia `ProbeApp` — brak rejestracji nie zawodzi, po cichu zabiera kolor,
  a obrazek wygląda wiarygodnie.
  ⭐⭐ **PUNKT 4 (Performance / Execution plan) — ZAMKNIĘTY: 4a layout + 4b kolorowanie nazw.** Suite **8429**
  (8242 + 132 + 55). ⛔⛔ **NAJWAŻNIEJSZE DLA NASTĘPNEJ SESJI: warianty W1/W2 (różnicowanie metod dostępu,
  wycofywanie czasowników `Table`/`Index`, trzeci poziom neutralny) zostały ZBUDOWANE, WYRENDEROWANE
  I ODRZUCONE — nie projektować ich ponownie** (historia §6.5). Powód jest zmierzony, nie estetyczny:
  katalog ma **dokładnie DWA** neutralne poziomy tekstu, a dzieli je w Dark **1,78:1** (Light 2,88:1), więc
  deklarowane rozróżnienie nie było wiarygodnie widoczne; trzeci poziom wymagałby nowego tokenu.
  ⭐⭐ **Znalezisko 4b: parser planu JUŻ ISTNIAŁ, a widok wyrzucał jego wynik** — `PlanNode` niesie
  `Method`/`TableName`/`IndexName`/`Detail` dla każdego węzła, a `DisplayText` zwracał `Node.RawText`.
  Kolorowanie nie potrzebowało nowej gramatyki, tylko żeby widok przestał spłaszczać. ⛔ Highlighting SQL
  byłby złym narzędziem — plan Firebirda nie jest SQL-em.
  ⚠⚠ **Podział bierze się z ROZCIĘCIA tekstu surowego, nigdy ze składania z pól** — inaczej klasa
  odtwarzałaby wypowiedź silnika i każdy rozjazd po cichu pokazałby plan, którego serwer nie wydrukował.
  Granica z faktu, że **`Detail` jest SUFIKSEM `RawText`**, więc nie ma tu drugiej kopii listy słów parsera.
  ⚠⚠ **Korekta `IconColor_Index` → `#4F812C` i `IconColor_Procedure` → `#CE4800` (Light) NIE jest osobną
  decyzją estetyczną, tylko WARUNKIEM poprawności kolorowania**: te role zaprojektowano dla IKON (próg 3:1),
  a plan maluje nimi TEKST 11 px (próg 4,5:1) — zmierzone 4,00:1 i 3,70:1. ⛔ Cofnięcie ich przy zachowanym
  kolorowaniu wraca pod próg §10 i zapala strażnika.
  ⚠ **Copy przy „Raw plan" NIE kopiuje samego planu**, tylko ładunek „expert drawer" (timings + capture +
  plan) — zachowanie zastane, świadomie nieruszone, przypięte testem.
  ⛔⛔ **LEKCJA PROCESOWA, warta więcej niż sam punkt (historia §6a): sprzątanie po odrzuconym eksperymencie
  skasowało ZAAKCEPTOWANY 4b, a git nie miał czego przywrócić — pliki nigdy nie były commitowane.**
  `git checkout` cofa do HEAD, czyli do stanu SPRZED całego punktu; plik nieśledzony i usunięty nie ma
  w gicie żadnej siatki bezpieczeństwa. ⭐ **Commituj odebrany etap, zanim zaczniesz na nim eksperymentować.**
  ⏸ **Otwarte w pakiecie:** punkty 5–6; ujednolicenie dwunastu istniejących podglądów DDL; rozjazd rozmiaru
  podglądu 13 (zakładka) / 11 (dialog) — świadomy, opisany rodziną, zmiana byłaby decyzją typograficzną.
  ⚠ **`Lab/EmberTern_Lab.fdb` bywa modyfikowany przez SMOKE TESTY** (Firebird dotyka nagłówka przy
  podłączeniu) — sprawdzaj `git status Lab/` przed commitem po smoke; to nie jest zmiana w kodzie.

- **🏁🔒 M5 · PRODUCT POLISH — ETAP ZAMKNIĘTY W CAŁOŚCI I ODEBRANY (2026-08-10). ⭐ WRAZ Z NIM ZAMKNIĘTY
  JEST CAŁY PLAN §13 — po M5 nie ma kolejnej pozycji.** Build 0/0; suite **8392** (8214 + 123 + 55); smoke
  czysty. Podsumowanie: `product-polish.md` **§19.51**; startowy dokument:
  **[product-polish-m5-next-session.md](docs/design/product-polish-m5-next-session.md)**.
  ⛔ **`feat/product-polish` NIE jest scalona do `master`** — ratyfikowana decyzja użytkownika (2026-08-05)
  nadal obowiązuje.
  **Sześć pozycji:** kontrast **§10** (§19.45) · focus **L‑1** (§19.46) · empty states **M‑3** (§19.47) ·
  animacje **§9** (§19.48) · **DPI** (§19.49) · terminologia **M‑4** (§19.50). „Oba motywy" to kryterium
  przekrojowe każdego QA, nie osobna pozycja.
  ⭐⭐ **WYNIK METODOLOGICZNY CAŁEGO M5: w CZTERECH z sześciu pozycji pomiar obalił zapis, na którym pozycja
  stała — i za każdym razem zmieniło to kształt pracy, nie tylko liczbę.** §10 cytowało normę, której nie
  spełnia (żadna rola typograficzna nie kwalifikuje się jako „duży tekst" WCAG) · L‑1 opisywał objaw, a
  produkt miał **dwie konwencje focusu naraz** · §9 „zero naruszeń" pochodziło z licznika na źródłach, gdy
  framework wnosi **16 przejść** · M‑4 „Drop wyłącznie w Data Import" opisywało **2 z 26** wystąpień, i to
  na tej przesłance zapadła decyzja projektowa, zanim pomiar ją obalił. ⭐ Wspólny wniosek: **zapis
  w dokumencie starzeje się ciszej niż kod, a najgroźniej wtedy, gdy brzmi konkretnie** — norma z nazwą,
  liczba wystąpień, „zero". ⛔ Mierz przed planowaniem, nie cytuj.
  ⭐ **Dwie pozycje (§9 i DPI) zamknięto BEZ zmiany produktu i to jest wynik, nie brak** — ich produktem był
  pomiar i korekta zapisu. ⚠ Odwrotnie **M‑4 wymusiło zmianę KODU** (`CollectionCommands.RemoveVerb`), choć
  wyglądało na etap czysto tekstowy: wspólny router kolekcji opisywał **jedną etykietą** trzy różne
  operacje — `ALTER TABLE … DROP` na polu, `DELETE FROM` na wierszu i wyjęcie pozycji z bufora.
  ⛔ **Świadomie odłożone po M5 (żadne NIE jest niedokończonym M5)** — pełna tabela z powodami w §19.51.3:
  szerokość Activity Monitora i Data Importu przy 150 %/175 % (dług konstrukcyjny) · **etap odstępów**
  (969 wartości lokalnych) · **app-wide UX sprint** (gęstość + czcionka monospace) · **B1** · **Z‑3** ·
  ogon literałów ikon 10/11/13/15 · nazwane wyjątki terminologii · skale DPI > 175 % · `ToolTip` w §9.
  ⏭ **Następny temat wymaga decyzji użytkownika.**

- **🔤🔒 M5 · M‑4 TERMINOLOGIA — ZAMKNIĘTE I ODEBRANE PO QA (2026-08-10).** As-built: `product-polish.md`
  **§19.50**; norma: **[terminology.md](docs/design/terminology.md)**; strażnik `TerminologyTests` (**R‑8**),
  zweryfikowany podsadzeniem.
  ⛔⛔ **INWENTARZ BYŁ BŁĘDNY I DECYZJA ZAPADŁA NA FAŁSZYWEJ PRZESŁANCE.** Raportowałem, że `Drop` występuje
  **wyłącznie w Data Import**; zmierzone — **26 etykiet w sześciu modułach**, z czego import to **2**
  (niecałe 8 %). Przyczyna: podgląd ucięty na `head -45`, czyli **uogólnienie z tego, co się wydrukowało**.
  ⭐ Zgłoszone użytkownikowi **przed jakąkolwiek zmianą tekstu**; decyzja podjęta od nowa.
  🔒 **Ratyfikowany wariant B: `Drop` = operacja DDL generująca `DROP`, `Delete` = pozostałe usuwanie,
  `Remove` = element edytowanej kolekcji.** Uzasadnienie użytkownika: *„EmberTern jest narzędziem dla
  developerów baz danych, więc chcę zachować informację o tym, jaka operacja DDL zostanie wykonana"*.
  ⭐ Reguła **istniała utajona** i była łamana w połowie przypadków — M‑4 ją **dokończyło**: indeks miał
  **cztery etykiety w dwóch słowach**, a generator kłócił się z własnym potwierdzeniem.
  ⭐⭐ **To wymusiło zmianę kodu:** czasownik usunięcia stał się własnością **kolekcji**
  (`CollectionCommands.RemoveVerb`), bo jedna etykieta nie może być poprawna dla pola tabeli (`DROP`),
  wiersza danych (`DELETE FROM`) i pozycji bufora naraz.
  ⭐ **Strażnik M‑3 złapał kaskadę** — zmiana tooltipa wymusiła synchronizację podpowiedzi w pustym pasku
  bocznym. ⚠ **Dwa istniejące testy przepisywały starą treść** i padły (#333). ⚠ **Pułapka polskiego
  cudzysłowu po raz TRZECI** (M4.3, M4.4, tutaj).
  ⏸ **Nazwane wyjątki:** debuggerowe „Save" (kompiluje, ale „Save" opisuje akcję użytkownika —
  `terminology.md` §2.1) · `FolderDialogCreate` · tytuły okien i zakładek · proza z „run".

- **🖥🔒 M5 · DPI 100–175 % — ZAMKNIĘTE PO QA UŻYTKOWNIKA (2026-08-10). ⭐ ZERO ZMIAN PRODUKCYJNYCH; R‑6
  SPŁACONE.** As-built: `product-polish.md` **§19.49**; checklista + wynik:
  **[product-polish-m5-dpi-checklist.md](docs/design/product-polish-m5-dpi-checklist.md)**; pomiar:
  `VisualCandidateProbe -- fit` → `out/m5-dpi-fit.txt`.
  ⭐ **100 % i 125 % czyste; przystanki 1–7, 9 i 10 przeszły bez uwag** — czyli **żadna zmiana metryki
  z M4 ani M5 nie okazała się defektem DPI**, a to był właściwy przedmiot R‑6. ⚠ Checklista była celowana:
  dziesięć przystanków, każdy przypięty do iteracji, która go ruszył, a wszystkie liczby **policzone
  z tokenów** — stąd priorytet: **125 % jest arytmetycznie najgorsze** (15 z 28 tokenów na ułamku piksela),
  150 % ma 5, **200 % jest czyste dla wszystkiego**.
  ⛔⛔ **ZNALEZISKO PRZY 150 % / 175 %: Activity Monitor i Data Import nie mieszczą się w szerokości,
  bez możliwości przewinięcia.** 🔒 Ratyfikowane jako **DŁUG TECHNICZNY poza zakresem M5** — layout nie
  jest zmieniany.
  ⭐⭐ **To ograniczenie KONSTRUKCYJNE, nie defekt skalowania.** Pasek poleceń obu widoków to **goły poziomy
  `StackPanel`** żądający **~1130 DIP** (18 i 20 dzieci), **bez `ScrollViewera`** — taki panel mierzy dzieci
  przy nieskończoności i **nie kompresuje się, tylko przycina**. ⭐ Kontrolą jest Script Executor: żąda
  *więcej* (1554 DIP) i problemu nie ma, bo jego nadmiar siedzi w siatce, która **umie się przewijać**.
  ⛔ **Dowód, że to nie DPI:** na **1366×768 przy 100 %** dla treści zakładki zostaje **1082 DIP** (ekran −
  pasek boczny 280 − splitter 4), czyli oba widoki nie mieszczą się **bez żadnego skalowania**; skalowanie
  nie zmienia liczby DIP-ów, których żąda kontrolka, tylko liczbę, którą ma ekran. ⭐ Przewidywanie z pomiaru
  **odtwarza obserwację co do skali** (mieści się przy 100/125, nie mieści przy 150/175) — to dowód, że
  diagnoza trafia w mechanizm, a nie w objaw.
  ⚠ **To nie regresja M4/M5, tylko dług, który M4 ZMNIEJSZYŁ** — mechanizm opisało już **§19.33** (M3b.1d),
  licząc wtedy 520 px samych podłóg combo; blok gęstości M4 te podłogi zdjął (~154 px odzyskane).
  ⚠ **Wysokość to osobny objaw i tylko Data Import** (żąda 739 DIP przy 688 dostępnych na 150 % i 590 na
  175 %). ⚠ **Hipoteza dla znikającego paska statusu przy 175 % świadomie NIEDOWIEDZIONA** — `MainWindow`
  nie daje się zbudować w sesji headless.
  ⛔ **NIE implementować przewijania, `WrapPanela` ani redukcji toolbaru przy okazji innego etapu** — każdy
  z tych kierunków to osobna decyzja UX (⚠ `WrapPanel` narusza §13.3 Zero Layout Shift). ⭐ Pomiar w §19.49
  jest kompletny: **przy powrocie do tematu nie trzeba go powtarzać**.
  ⚠ **175 % to obserwacja, nie cel projektowy**; skale >175 % nietestowane (Windows ich nie udostępnia na
  konfiguracji użytkownika).

- **🎬🔒 M5 · §9 RUCH I ANIMACJA — ZAMKNIĘTE (2026-08-10). ⭐ ZERO ZMIAN PRODUKCYJNYCH — iteracja
  dostarczyła POMIAR, KOREKTĘ DOKUMENTU i JEDNEGO STRAŻNIKA.** Build 0/0; suite **8386** (8208 + 123 + 55,
  +1); smoke czysty. As-built: `product-polish.md` **§19.48**; korekta: **§9 + nowe §9.1**; pomiar:
  `VisualCandidateProbe -- motion` → `out/m5-motion.txt`.
  ⭐⭐ **ZNALEZISKO: „§9 nie ma ani jednego naruszenia" opisywało coś innego, niż wyglądało.** Ten zapis
  pochodził z licznika na źródłach (zero `Transitions`/`Animation`/`Storyboard` w `EmberTern.App`) — czyli
  odpowiadał wyłącznie na pytanie **„czego MY nie napisaliśmy"**, podczas gdy §9 zakazuje przejść na
  właściwościach układu **niezależnie od tego, kto je wniósł**, a sam §9 pisał *„Fluent wnosi własne
  przejścia"* bez żadnej konsekwencji. **#337 jeszcze raz, o poziom wyżej.**
  **Pomiar** z elementu, który maluje: **350 elementów na motyw**, zrealizowanych w prawdziwym oknie,
  z wnętrzem szablonów i **otwartymi popupami** (lista `ComboBox`a i podmenu mają własny `PopupRoot`).
  **16 przejść, wszystkie z Fluenta, wynik IDENTYCZNY w obu motywach.**
  ⭐ **Zero przejść na właściwości UKŁADU** — jedyna reguła §9 z zapisanym uzasadnieniem jest spełniona.
  ⛔ Naruszone są za to: **sufit 120 ms** (`ProgressBar` **197 ms**) i **krzywa** (wszystkie 16 na
  `LinearEasing` zamiast `CubicEaseOut`).
  ⭐⭐ **`RenderTransform` na przyciskach (7) jest BEZCZYNNY i zmierzenie tego zmieniło jego status:**
  macierz jest **jednostkowa w spoczynku i po wymuszeniu `:pressed`**, również na **gołym `Button`** —
  czyli to nie nasze warianty kasują efekt, tylko **Fluent 12.1.1 nie ustawia transformacji wciśnięcia**.
  🔒 **DECYZJA UŻYTKOWNIKA (W‑A): korygujemy DOKUMENT, nie produkt.** ⛔ Nie nadpisujemy i nie przejmujemy
  przejść Fluenta — ani czasu, ani krzywej, ani `ProgressBar`. ⭐ Powód rachunkowy: alternatywa to przejście
  z **0 własnych przejść na 16** i utrzymywanie ich na zawsze, żeby zmienić zanikanie separatora paska
  przewijania ze 100 ms liniowo na 100 ms kubicznie — zmiana dla użytkownika niewidoczna.
  **§9 dostało kolumnę „Zakres"**: zakaz właściwości układu **bezwzględny**, a czas / krzywa / lista
  dozwolonych właściwości dotyczą **ruchu, który EmberTern deklaruje sam**. Nowe **§9.1** zapisuje baseline
  Fluenta jako **nazwany wyjątek z liczbami**; ⚠ wyjątek dla `RenderTransform` obejmuje **wyłącznie
  zmierzone zachowanie** — gdy przyszły Fluent zacznie tę wartość zmieniać, decyzję trzeba podjąć od nowa.
  ⭐ **Strażnik `EmberTernDeclaresNoMotionOfItsOwn`** (w `DesignTokenComplianceTests`): `EmberTern.App` ma
  zero własnych `Transitions`/`Animation`/`Storyboard`. ⛔ **Celowo NIE próbuje kontrolować szablonów
  Fluenta** — taki test wymagałby uruchomienia aplikacji i zmieniałby kolor przy każdej aktualizacji
  frameworka (#333). ⭐ Zamienia stan **przypadkowy** („nikt nie napisał") w **egzekwowany**, żeby pierwsze
  `<Transitions>` na `Width` było świadomą decyzją. Zweryfikowany podsadzeniem właśnie takiego przejścia.
  ⚠⚠ **GRANICA POMIARU, zapisana świadomie:** **nie zmierzono `ToolTip`** (wymaga prawdziwego najechania)
  ani przejść włączanych przez `ControlTheme` w stanie niewymuszonym przez sondę. „Zero na właściwościach
  układu" jest **mocne, ale nie wyczerpujące**; ⛔ niczego w tych obszarach nie naprawiano.
  ⚠ **Trzy rzeczy w samym pomiarze dawały fałszywą odpowiedź** — `ITransition.Property` i `Popup.Host`
  **nie są publiczne** w Avalonii 12.1.1, `TransformOperations.ToString()` zwraca **samą nazwę typu**
  (pierwsza wersja „pokazała" identyczny spoczynek i wciśnięcie, porównując dwie takie same etykiety),
  a otwarcie popupu **mutuje drzewo w trakcie leniwej enumeracji**.
  ⚠ **Jeden przebieg partycji głównej zaświecił raz na czerwono i NIE jest ogłaszany naprawionym ani
  powiązanym** — trzy kolejne przebiegi po nim dały 8208 zielonych, nazwy nie udało się przechwycić.

- **📭🔒 M5 · M‑3 STANY PUSTE — ZAMKNIĘTE I ODEBRANE PO QA WIZUALNYM UŻYTKOWNIKA W OBU MOTYWACH
  (2026-08-10).** Trzecia iteracja M5. Build 0/0; suite **8385** (8207 + 123 + 55, +14); smoke czysty;
  **wszystkie trzy podsadzenia złapane, każde przez swój i tylko swój test**. As-built:
  `product-polish.md` **§19.47**. Nowa gotcha **#346**. Materiał: `VisualCandidateProbe -- empty`.
  ⭐⭐ **ZNALEZISKO GŁÓWNE: audyt wskazał realną lukę, ale zbiór do zrobienia był ~3× mniejszy — i zawęziło
  go PYTANIE, KTÓREGO INWENTARZ NIE ZADAŁ.** Audyt §1.1 mówił „empty state w **3 z 48** widoków"; zmierzone
  — **12 widoków + 5 ViewModeli** już je miało, a i ta liczba jest zaniżona, bo licznik idzie po NAZWIE
  stałej (`*Empty*`) i nie widzi Global Searcha, który ma pełny stan pusty pod nazwą `GlobalSearchNoResults`
  (**#337 trzeci raz**). ⭐ Prawdziwe zawężenie dało jednak inne pytanie: nie *„czy jest komunikat"*, tylko
  ***„czy ten stan JEST OSIĄGALNY"*** — z 13 zgłoszonych luk zostały **4**.
  **Co weszło:** ⭐ **pusty pasek boczny** (wariant W4 z sześciu, ratyfikowany na renderze) — najpierw KROK,
  potem miejsce akcji pokazane **glifem `Icon.Plus`**, bo przycisk „New Connection" nie ma na ekranie
  podpisu; ⛔ wariant z własnym przyciskiem w panelu **odrzucony** (druga afordancja tej samej akcji) ·
  **Roles** (baza bez ról to stan zwyczajny) · **Membership — DWA komunikaty kluczowane KIERUNKIEM**
  (produkt wiedział o tym rozróżnieniu przed M‑3: `RowHeader` przełącza „Role name" ↔ „Member name") ·
  **Script Executor — DWA stany** (`HasResults` liczy się z `_allRows` przed filtrem, siatka wiąże `Rows`
  po filtrze).
  ⚠ **Odstępstwo od zatwierdzonego renderu, zaakceptowane:** glif **12** i podpis **11**, nie 14 i 10 —
  komentarz roli `Size.Icon.Sm` mówi wprost, że „ikona 14 px obok tekstu 11 px optycznie go przerasta".
  ⚠ Bramka paska to **`RootNodes.Count == 0`**, nie „lista wierszy pusta": `SidebarRows` jest projekcją
  FILTROWANĄ, więc bramka na niej pokazywałaby podpowiedź komuś, kto tylko wpisał filtr bez trafień.
  ⛔⛔ **WPIĘCIE GOTOWEGO TEKSTU BYWA WPIĘCIEM DEFEKTU.** Z 8 osieroconych stałych `*Empty*` dwie opisywały
  stan wciąż osiągalny — i **były wadliwe**: `ConnectionsEmptyHint` cytowało etykietę *„+ New Connection"*,
  **której w produkcie nie ma** (przycisk to sam glif). Defekt przeżył całe życie produktu **dlatego, że
  stała była osierocona**. Pilnuje tego dziś osobny strażnik.
  ⛔ **WYCOFANE — stany NIEOSIĄGALNE, nie przeoczenia:** **Users** (`SEC$USERS` zawsze ma SYSDBA, a błąd
  odczytu ląduje w banerze) · **View → Fields** (widok zawsze ma ≥1 kolumnę) · **klasa D** (zaznaczenie jest
  null praktycznie tylko wtedy, gdy lista jest pusta — czyli klasa C).
  ⛔ **WYCOFANE — treść nieprawdziwa:** **Privileges**. Ta siatka **wypisuje OBIEKTY** kategorii,
  z uprawnieniami jako komórkami, więc grantee bez ani jednego uprawnienia do 200 tabel widzi **200 wierszy**;
  „No privileges in this category." było zdaniem o czymś, czego użytkownik nie ogląda.
  ⏸ **ODŁOŻONE decyzją:** **Session → Transactions** (licznik stoi w pasku podsumowania) · **Table → Indeksy**
  (jedyna afordancja to menu kontekstowe — ⛔ nie budujemy stanu pustego wokół prawego przycisku myszy).
  ⛔⛔ **NAZWANY WYJĄTEK — 17 drzew „Zależności" ZOSTAJE bez stanu pustego** (parytet z IBExpertem: każda
  z 10 kategorii jest wierszem również pusta, więc obiekt bez zależności pokazuje `… (0)` ×10 — pustka jest
  ogłoszona, ekran nie jest pusty). ⭐ Wyjątek jest **pilnowany TESTEM, nie zapisem w dokumencie** — to
  **#340 przełożone na strażnika** — i test pilnuje **PRZESŁANKI**, nie polityki (#322).
  ⚠ **Trzy razy pomiar obalił treść, którą sam zaproponowałem**, zawsze z tego samego powodu: **nazwa
  kolekcji sugerowała, co siatka pokazuje, a kod mówił co innego**. ⭐ Wniosek na dalsze etapy: przy stanie
  pustym pytaj najpierw, **KIEDY on zachodzi**, a nie jak go nazwać.

- **⌨🔒 M5 · L‑1 FOCUS — ZAMKNIĘTE I ODEBRANE PO QA UŻYTKOWNIKA (2026-08-10).** QA potwierdziło: nawigacja
  Tabem pokazuje widoczny focus, biała ramka na przycisku akcji działa, brak problemu wizualnego przy
  przejściu focusu. ⛔ Nie wracać. Druga
  iteracja M5. Build 0/0; suite **8371** (8193 + 123 + 55, +20); smoke czysty; **każdy z pięciu
  wariantów zweryfikowany podsadzeniem**. As-built: `product-polish.md` **§19.46**; decyzja + pomiar:
  **[product-polish-m5-focus-decision.md](docs/design/product-polish-m5-focus-decision.md)**.
  🔒 **Ratyfikowany wariant 1** (użytkownik, 2026-08-10): **jedna konwencja `:focus-visible`** dla
  wszystkich wariantów przycisku + uzupełnienie obu braków.
  ⭐⭐ **ZNALEZISKO GŁÓWNE: L‑1 OPISYWAŁ OBJAW, NIE DEFEKT.** Audyt mówił *„`primary`/`caption` nie mają
  `:focus`"*; pomiar headless dał trzy ustalenia: (1) ⛔ **`:focus` zapala się TAKŻE OD MYSZY** — więc
  `Button.icon`/`.flat` trzymały obwódkę **po kliknięciu**, podczas gdy `CheckBox`/`RadioButton` używały
  `:focus-visible` **od początku**, czyli produkt miał **dwie konwencje focusu naraz, w jednym oknie
  dialogowym**; (2) ⛔ naiwna poprawka dla `primary` dałaby pierścień **niewidoczny** (`FocusBorderBrush`
  na akcencie = **1,26:1 / 1,17:1** przy progu 3:1); (3) ⛔ naiwna poprawka dla `caption` byłaby **martwa**
  (`BorderThickness=0` to świadomy reset, więc setter `BorderBrush` nie ma czego malować).
  ⭐ Czyli **obie „brakujące" pozycje zawiodłyby przy dosłownym wykonaniu zalecenia audytu — i to
  z dwóch różnych powodów.**
  **Co weszło:** `icon`/`flat` — bez zmiany wyglądu, zmienił się WYZWALACZ · `primary` — ramka na
  **`OnAccentBrush`** (barwa własnego tekstu), **5,29:1** · `caption` — **tło** `FocusBorderBrush`
  + glif `OnAccentBrush` (3,27/3,76 i 4,21/4,53) · `ToggleButton.icon`. ⛔ **Żadnej nowej barwy.**
  ⚠⚠ **ODSTĘPSTWO OD ZATWIERDZONEGO RENDERU: grubość pierścienia `primary` to 1, nie 2.** Render
  pokazywał 2 px i tak został odebrany, ale grubość ramki wchodzi w desired size, a `ContentPresenter`
  Fluenta jest wcinany o `BorderThickness` — więc 1 → 2 rozpycha przycisk **przy każdym Tabie**, czyli
  łamie **§13.3 Zero Layout Shift** dokładnie tam, gdzie zmiana działa. ⭐ Odstępstwo **nic nie kosztuje**:
  ramka grubości 1 **już tam była**, tylko niosła `AccentBrush` (barwę własnego tła), więc była
  niewidoczna z konstrukcji — zmiana samej barwy **odsłania pierścień, który istniał**, przy zerowej
  zmianie geometrii i tym samym kontraście.
  ⚠ **`caption` wymaga DWÓCH setterów** — samo tło zostawia glif w `ForegroundBrush` = **2,84:1 w Dark**,
  pod progiem; wariant „tylko tło" odrzucony pomiarem **zanim trafił do materiału decyzyjnego**.
  ⚠ **`ToggleButton.icon` (22 wystąpienia) objęty poza dosłownym zakresem decyzji**, bo pozostawienie go
  na `:focus` odtwarzałoby usuwaną niespójność w tym samym pasku. ⚠⚠ Uczciwie: weszło najpierw
  **przypadkiem** (`sed` trafił w podciąg), zostało **rozstrzygnięte świadomie po fakcie** i objęte
  strażnikiem — nie przepuszczone.
  ⭐ **Strażnik ma DWIE asercje, bo dwa braki zawiodły inaczej:** focus musi **cokolwiek zmienić** i to
  coś musi **trzymać 3:1**. Sama druga przepuściłaby martwy styl `caption`, sama pierwsza — niewidoczny
  pierścień `primary`. Drugi test pilnuje, że wskazania **NIE MA** po fokusie wskaźnikiem.
  ⏸ **Otwarte:** QA wizualne (w tym część, której render pokazać nie może — obwódka **nie** po myszy,
  **tak** po Tabie) · ⚠ `TextBox` i `DataGrid` mają własne ścieżki focusu, **poza** zakresem L‑1.

- **🎨 M5 · §10 KONTRAST SEVERITY — ZAIMPLEMENTOWANE 2026-08-10, ⏸ CZEKA NA QA WIZUALNE UŻYTKOWNIKA
  w obu motywach.** Pierwsza iteracja M5. Build 0/0; suite **8351** (8193 + 103 + 55, +6); smoke czysty;
  trzy nowe strażniki **zweryfikowane podsadzeniem**. As-built: `product-polish.md` **§19.45**; materiał
  decyzyjny + pomiar: **[product-polish-m5-severity-contrast-decision.md](docs/design/product-polish-m5-severity-contrast-decision.md)**.
  🔒 **Ratyfikowany wariant B** (użytkownik, 2026-08-10): trzy wartości w `Colors.axaml`, każda policzona
  **przy progu** 4,5:1 na `PanelBrush` z zachowaniem odcienia — Light `WarningColor` `#C77800` → **`#A16100`**
  (3,12 → 4,52) · Light `SuccessIconColor` `#2E8B4F` → **`#2A7E48`** (3,88 → 4,57) · Dark `ErrorColor`
  `#F44747` → **`#F55252`** (4,26 → 4,53). ⚠ **Widać wyłącznie Light/Warning** (bursztyn → bursztynowo-brąz);
  Dark/Error jest wizualnie nierozróżnialny i użytkownik świadomie odrzucił wariant „zostawmy, bo nie widać".
  ⭐⭐ **ZNALEZISKO GŁÓWNE: to NIE był defekt `MessageBanner`, choć tak go nazwałem w inwentaryzacji.**
  Zmierzony zasięg: `ErrorBrush` **30** konsumentów, `WarningBrush` **36**, `SuccessIconBrush` **25**, a każdy
  maluje mały tekst w ~8–13 miejscach poza banerem — poprawka lokalna byłaby **R7** w czystej postaci.
  ⭐ Druga połowa poszła w drugą stronę: **pasek i ikona przechodzą 3:1 we WSZYSTKICH ośmiu kombinacjach**,
  więc sygnał severity był poprawny od początku, a wadliwy był wyłącznie TEKST — i to przesądziło o odrzuceniu
  wariantu „tekst neutralny" (rozwiązywałby problem, którego nie ma, odwracając ratyfikowaną decyzję Seam 4).
  ⚠⚠ **§10 CYTOWAŁO NORMĘ, KTÓREJ NIE SPEŁNIA, I O MAŁO NIE UZASADNIŁO DECYZJI.** Wiersz *„Tekst duży
  (≥ 14 px lub ≥ 12 px SemiBold) → ≥ 3:1"* był opisany jako **„WCAG AA Large"**; WCAG wymaga **24 px** albo
  **18,7 px bold**, a najwyższa rola EmberTerna to `Text.Display` = **23 px** — **żadna się nie kwalifikuje**.
  Jeden z wariantów („SemiBold zamiast barwy") spełniałby §10 *jak napisane*, nie spełniając WCAG.
  🔒 Ratyfikowane: próg 3:1 **zostaje jako wymóg własny EmberTerna**, jawnie nie jako WCAG (§10 + nowe §10.1).
  ⚠ **`ActionRunColor` — rzecz spoza wariantów:** w Light miał wartość celowo identyczną z `SuccessIconColor`,
  więc zmiana rozjeżdżała parę. 🔒 Rozstrzygnięte pomiarem — **wszystkie cztery** wystąpienia `ActionRunBrush`
  to `SvgIcon` (próg 3:1, spełniony 3,88:1), więc **zostaje**. ⭐ Rozejście jest **projektem działającym zgodnie
  z zamysłem**: decyzja W4 rozdzieliła te tokeny właśnie po to; komentarze w OBU słownikach zapisują rozejście
  z powodem, więc żaden nie stał się nieprawdziwy.
  ⭐ **Strażnik pilnuje DWÓCH progów, bo to dwa pytania** — tekst 12 px (4,5:1) osobno od paska/ikony (3:1),
  na banerze i w logu Messages, w obu motywach. **Odczyt z ELEMENTU, KTÓRY MALUJE** (prawdziwy `MessageBanner`),
  a log bierze mapowanie z produkcyjnego `QueryMessageViewModel.MessageBrushKey`, nie z przepisanej tablicy.
  ⭐⭐ **Podsadzenie dało wynik mocniejszy niż „test świeci":** po cofnięciu `#C77800` **oba testy tekstu padły,
  a test sygnału został ZIELONY** — dowód, że dwa progi są naprawdę rozdzielone, a nie zlane. Liczby
  z podsadzenia (3,12 · 3,35) są **identyczne** z pomiarem statycznym sprzed wdrożenia.
  ⚠⚠ **Sprostowanie mojej własnej tabeli, wykryte dopiero przy pisaniu strażnika:** twierdziłem, że w logu
  Messages Success w Light ma 4,16:1 pod progiem — nieprawda, `ShowSeverityMarker` obejmuje **tylko Warning
  i Error**, więc Success czyta tam `ForegroundBrush` i ta para **nigdy się nie renderuje**. Liczyłem kontrast
  tokenu zamiast tego, co element maluje; decyzji nie zmienia (Success wymagał korekty z powodu banera).
  ⏸ **Otwarte:** QA wizualne · brak **`SuccessBrush`** (jedyne severity odsyłające do tokenu IKONOWEGO —
  asymetria zapisana, tokenu ⛔ NIE tworzono) · **P‑2** `AccentBrush` na railu 2,89:1 w Dark (poza zakresem,
  `color-language.md` §9.2).

- **🏁🔒 M4 · PRODUCT POLISH — ETAP M4 ZAMKNIĘTY W CAŁOŚCI I ODEBRANY (2026-08-09).** Oba bloki decyzyjne
  (gęstość · typografia) + pięć etapów migracji (M4.1 · M4.2 · M4.2b · M4.3b+c · M4.4). Rejestr kolizji
  **K1–K15 zamknięty**, zero otwartych pytań projektowych M4. ⏭ **Następny krok wymaga decyzji użytkownika —
  patrz `docs/design/product-polish-m5-next-session.md`.**
  ⭐⭐ **Wynik metodologiczny całego M4, wart więcej niż liczby: CZTERY zapisane przesłanki nie przeżyły
  zderzenia z kodem** — „`Size.Icon` = 64 literały" opisywało 164 z 355 deklaracji (#332) · „K15 = 112
  wystąpień w 17 plikach" to naprawdę 44 w 13, z czego 41 to JEDNA rola · §13.2 odrzucało `SidebarFlatController`
  sprzężeniem, którego ten kontroler nie ma (§19.41.2) · a `GridSplitter` miał być „jedyną odłożoną pozycją,
  którą M4.4 napotka" i nie ma go w oknach ani razu. ⭐ Do tego **trzy etapy z rzędu** (M4.2, M4.3, M4.4)
  okazały się nie sweepem literałów, tylko **odbiorem decyzji, których nikt nie podjął** (#340).
  ⚠ Praktyczny wniosek na następny sprint: **liczba w prozie starzeje się cicho** — mierz przed planowaniem.

- **🪟🔒 M4.4 · DIALOGI I OKNA + `GrowingDialogBehavior` (M‑5) — ZAMKNIĘTY I ODEBRANY PO QA WIZUALNYM
  (2026-08-09). Ostatni etap migracji M4.**
  Build 0/0; suite **8345** (8193 + 97 + 55, +11); smoke czysty. As-built: `product-polish.md` **§19.44**.
  Nowa gotcha **#343**. Decyzje użytkownika: punkt 1 TAK · `ApplyCeiling` = `min` · trzy dialogi wpięte ·
  `RecompileDependents` wykluczony · nagłówki NIE TERAZ.
  ⭐⭐ **MIGRACJA BYŁA PRAKTYCZNIE ZROBIONA — trzeci etap z rzędu o tym kształcie.** W zakresie 25 okien:
  `FontSize` **3**, `CornerRadius` **0**, literały ikon **0**. Trzy pozostałe `FontSize` siedziały
  w **tych samych trzech plikach**, co trzy żywe odesłania „Rozstrzyga §13.3" — sierocy backlog bramy
  (#340), dokładnie jak przewidział dokument startowy. Wszystkie trzy zeszły na `Text.Application`;
  grupa „TextBlock 13 px" przestała istnieć w dialogach. `FontSize` app-wide **31/12 → 28/9**.
  ⚠⚠ **Trzeci przypadek nie był tym samym co dwa pierwsze i POMIAR ODWRÓCIŁ MOJĄ DIAGNOZĘ.** Wszystkie
  trzy niosły identyczny odziedziczony komentarz („treść komunikatu"), ale w `ForeignKeyDialog` to nazwa
  obiektu bazy pisana **monospace** — nasuwało to `Text.Code` (niesie 13, zero zmiany wyglądu) i tak
  zamierzałem zrobić. ⭐ Rodzina to odrzuciła: **wszystkie 25 konsumentów `Text.Code` to EDYTORY**, a wśród
  ~48 elementów monospace spoza edytora ten był **jedynym z literałem**; bliźniaczy `AddFieldDialog` (nazwa
  typu bazy, monospace) czyta `Text.Application`. Reguła *„`Text.Code` opisuje edytor, monospace poza
  edytorem bierze rolę tekstu"* **już w produkcie była** — M4.4 ją dokończyło (kształt Q2 z M4.3).
  ⭐⭐ **M‑5: KRYTERIUM RZECZYWISTE, NIE GRUPOWE — i grupa 16 okazała się złym predyktorem.** Wpięte trzy
  z czterech kandydatów: `NewConnectionDialog` (jedyny z prawdziwym wzrostem — komunikat testu połączenia
  rośnie **poza** `ScrollViewerem`), `ExportDialog` (CSV odsłania opcje, baner błędu, podmiana paneli) oraz
  `ExecuteProcedureDialog` — ⭐ ten ostatni **nie rośnie**, ale jego świadomy limit **720 stoi POWYŻEJ
  obszaru roboczego 696** na 1366×768. ⛔ `RecompileDependentsDialog` **wykluczony pomiarem**: zero wiązań
  `IsVisible`, lista gotowa przed otwarciem; jego 640 nietknięte.
  ⛔⛔ **ZNALEZISKO, KTÓRE ZMIENIŁO KSZTAŁT PUNKTU: `ApplyCeiling` NADPISYWAŁ `MaxHeight`.** Podpięcie
  podniosłoby świadome 720 → **1008** na zwykłym ekranie 1080 (a 640 → 1008), bo ręczny limit wykonuje **dwie
  prace** — „nie przekraczaj ekranu" *i* „nie rośnij ponad wygodny rozmiar na dużym monitorze" — a mechanizm
  znał tylko pierwszą. 🔒 Ratyfikowane: sufit to **`Math.Min(zadeklarowany, ekran)`**, i ⭐ jest to
  **dowodliwie NO-OP dla obu dotychczasowych konsumentów** (żaden nie deklaruje limitu; nieustawiony
  `MaxHeight` to `PositiveInfinity`). Gotcha **#343**.
  ⭐ **`ExportDialog` dostał najpierw STRUKTURĘ, potem sufit** — nie miał `ScrollViewera` w ogóle, a sufit bez
  przewijania **przycina** treść zamiast ją udostępnić (mówi to o sobie dokumentacja mechanizmu). Jeden
  `ScrollViewer` obejmuje **`Panel`, czyli oba stany naraz**, więc trzeci stan odziedziczy przewijanie.
  ⭐ **11 nowych strażników, wszystkie zweryfikowane podsadzeniem**, w tym `EverySizeToContentDialog_…` —
  tablica 16 okien → decyzja + powód, egzekwowana w OBIE strony (nowe okno bez decyzji zapala test; wpis
  niezgodny z kodem też). ⭐ To **#340 przełożone na strażnika**: odesłanie żyjące w źródle nie może już
  wypaść między etapami, bo tablica jest w teście, a nie w dokumencie.
  ⏸ **Otwarte:** ⛔ **nagłówki dialogu `20,16` ×15 vs `20,14` ×5** — 🔒 decyzja użytkownika: **na etap
  odstępów po M4.4** (⚠ zmierzone: **stopka jest w pełni spójna 19/19**, rozjazd jest wyłącznie w nagłówku,
  a rola `Pad.Dialog` = `20,16` ma **zero konsumentów**) · **B1** · **Z‑3** · migracja odstępów · ogon ikon
  10/11/13/15 · `FontFamily`.
  ⚠⚠ **Czwarta przesłanka M4 obalona pomiarem:** dokument startowy pisał, że `GridSplitter` to *„jedyna
  z odłożonych pozycji, którą M4.4 może naturalnie napotkać"*. Zmierzone: wszystkie **15** deklaracji stoi
  w **widokach zakładek**, a **w 25 oknach nie ma ani jednego**.

- **🔘✅ M4.3c · `Button.seg` — ZAMKNIĘTY I ODEBRANY (2026-08-09).**
  Build 0/0; suite **8334** (8182 + 97 + 55, +2); smoke czysty. As-built: `product-polish.md` **§19.43**.
  Nowa gotcha **#342**. ⭐ Dziewięć segmentów w dwóch ekranach (Session 3, Trace 6) → **jeden styl**
  w `ControlStyles.axaml`; dwie kopie lokalne usunięte.
  ⭐⭐ **PRZESŁANKA, DLA KTÓREJ TA ITERACJA BYŁA OSOBNA, OKAZAŁA SIĘ NIEPRAWDZIWA — i ujawniło to
  PODSADZENIE, KTÓRE NIE ZAPALIŁO TESTU.** M4.3b odłożyło `Button.seg` z uzasadnieniem *„konsolidacja zmienia
  priorytet stylu — mechanizm §19.2, przez który M3.4a odmówiło"*. Zmierzone: bazowy `<Style Selector="Button">`
  z `Padding` 99,99 postawiony **PO** bloku `.seg` **nie nadpisał** 8,3. ⭐ **Avalonia rozstrzyga między stylami
  SPECYFICZNOŚCIĄ SELEKTORA, nie kolejnością** — selektor z klasą bije goły selektor typu niezależnie od
  pozycji. ⚠⚠ §19.2 dotyczyło czego innego: styl przegrał z **WARTOŚCIĄ LOKALNĄ na elemencie**, a ta bije każdy
  setter niezależnie od tego, gdzie styl mieszka. ⭐ Warunkiem bezpieczeństwa przeniesienia jest więc pytanie
  **o ELEMENTY** („czy niosą wartości lokalne?" — nie niosą), a nie o pozycję bloku. ⛔ Nie wynika z tego, że
  odmowa M3.4a była błędna — inne elementy, nieprzemierzone (**#342**).
  ⭐ **Rozjazd, który to usunęło:** kopie różniły się **jedną wartością** — poziomym odstępem (Session 8,
  Trace 10) — przy komentarzu Session deklarującym *„same segmented language as the Activity Monitor toolbar"*.
  #335 ze świadkiem: **autor zapisał intencję, kod jej nie dotrzymywał.** 🔒 Wybrano **8** (R18 — przy równej
  czytelności wygrywa gęstszy); ⏸ **widoczny skutek: sześć segmentów Trace zwęża się o 2 px z każdej strony.**
  ⚠ Wartość jest LITERAŁEM celowo: `Pad.Cell` (8,3) i `Pad.MenuItem` (10,3) niosą te liczby, ale opisują
  komórkę siatki i wiersz menu — rola niepasująca do elementu jest gorsza od wartości lokalnej z powodem (R12);
  ⛔ nie utworzono `Pad.Segment` (jeden konsument = legalizacja wartości, jak odrzucone `Radius.Card` w B2).
  ⭐ Wysokość segmentu nie była pytaniem: `Border.chrome Button` nadaje `Size.ControlToolbar` (22) — **rozstrzyga
  KONTENER** (reguła #10). **Testy:** behawioralny (mierzy ZREALIZOWANY przycisk — kasowanie geometrii bazowej,
  identyczność obu segmentów, 8,3, `MinHeight` z kontenera, stan aktywny, oraz **że kontrolka zajęła miejsce
  w układzie**) + źródłowy (kopia lokalna wygrałaby przez bliskość w drzewie, a test behawioralny by jej nie
  zobaczył, bo buduje segment bez widoku). Oba dołączyły do **istniejącej** klasy headless, żeby nie powiększać
  kruchej listy nazw w filtrze partycji.
  ⚠ **Drugi raz w tej iteracji poprawił mnie pomiar:** mój komentarz **podniósł licznik wartości lokalnych
  o 1**, bo cytował składnię atrybutu, a `Measure` nie pomija komentarzy (kwirk znany od M2c iteracji 2).
  Naprawa: przeredagować komentarz, **nie** podnosić sufit. ⚠ Spadek `CornerRadius` 9 → 7 i `Padding` 38 → 37
  jest częściowo **PRZENIESIENIEM, nie migracją** — wartości wyszły do `Themes/`, którego licznik nie skanuje;
  zapisane przy obu wpisach bazowych.

- **🖥✅ M4.3 (M4.3b) · DEBUGGER · TRACE · SESSION · SECURITY · PERFORMANCE — ZAMKNIĘTY I ODEBRANY PO QA
  UŻYTKOWNIKA (2026-08-09).** ⭐ Cały M4.3 (M4.3b + M4.3c) wszedł JEDNYM commitem `2ebb341`, zgodnie
  z decyzją użytkownika.
  Build 0/0; suite **8332** (8182 + 95 + 55, +3); smoke czysty. As-built: `product-polish.md` **§19.42**.
  Nowe gotchy **#340–#341**. Decyzje **Q1–Q4 ratyfikowane na renderze** (`VisualCandidateProbe -- m43`).
  ⭐⭐ **ZNALEZISKO GŁÓWNE: to nie był sweep literałów, tylko ODBIÓR ZALEGŁYCH DECYZJI.** W pięciu plikach
  etapu stało **19 komentarzy „rozstrzyga §13.3"**, pokrywających praktycznie każdą pozostałą tam wartość
  lokalną — a brama §13.3a nie podjęła **ani jednej** (Z‑1…Z‑6 dotyczą czego innego), żadna nie dostała
  numeru K, po czym blok typografii ogłosił rejestr kolizji „zamknięty w całości". ⭐ §19.40.5 opisało ten
  mechanizm przy **B2**, ale jako pojedynczą pozycję, która „wypadła między etapami"; zmierzone — **B2 było
  pierwszym z ~19 w zakresie i ~43 w całym `src/`**. Przyczyna to **rozdzielenie kustodii**: odesłanie żyje
  w ŹRÓDLE, rejestr w DOKUMENCIE, a zamykany bywa wyłącznie dokument (**#340**).
  ⭐ **Q1 — siedem promieni 4 → `Radius.Surface` (3)** + chip Session → `Radius.Chip` (4 → 4, bez zmiany).
  ⚠ Jedna liczba, **dwa różne argumenty**: trzy KARTY dziedziczą B2 (rola nazywa „Kartę" we własnym
  komentarzu), a cztery RAMKI KONTROLEK rozstrzygnął argument mocniejszy — `ControlCornerRadius` Fluenta = 3
  i jest świadomie **NIENADPISANY** w `FluentBridge`, więc **każda prawdziwa obramowana kontrolka renderuje
  się przy 3**, a te cztery ją tylko udawały. ⛔ Zostaje 9: koła, kapsuły i dwa resety — arytmetyka, nie role.
  ⭐ **Q2 — pięć inline ✕ → `Size.Icon.Sm`.** Rozjazd (12 vs 11) siedział **wewnątrz jednego pliku**, więc nie
  opisywała go żadna reguła per ekran (#335 o poziom niżej). ⭐ `MainWindow` **już** używał tej roli dla dwóch
  takich ✕, więc M4.3 dokończyło regułę, a nie wprowadziło nową. ⚠ Koszt przyjęty świadomie: cztery ikony
  rosną 11 → 12, pod prąd R18; wariant „rola na 11" cofałby odebraną decyzję M4.2.
  ⭐⭐ **Q3 — grupa „TextBlock 13 px" (§18.0.5/3) opisywała pod jedną nazwą DWIE różne rzeczy.** TEKST bez roli
  migruje (puste stany Session + Trace 13 → `Text.Application`; podpisy 9 → `Text.Caption`), a **osiem „Bold
  13" w Security Managerze zostaje** — są bindowane do `PrivilegeStateGlyphConverter` zwracającego `✓`/`✓+`
  w przycisku **20×18**, czyli to GLIFY strojone do kontenera (reguła #10), a nie tekst. ⭐ **Zmieniony został
  POWÓD, nie wartość**: komentarz twierdził „a to jest tekst" i to on był nieprawdziwy (R12 w drugą stronę).
  ⚠ Oba puste stany są konstrukcyjnie identyczne — jedna decyzja na dwa ekrany, nie dwie.
  ⭐ **Q4 — pole filtra Trace `Height="26"` → `Size.Control` (24).** Rozstrzygnął pomiar, nie render: `TextBox`
  i `ComboBox` biorą `MinHeight = Size.Control` = 24, więc pole stało **2 px wyżej niż każdy prawdziwy sąsiad**
  i to ono wyznaczało wysokość paska. ⭐ Argumentem za obiema zmianami naraz było zdanie z istniejącego
  komentarza — *„reads clearly as a text input"*: autor zapisał intencję, kod jej nie dotrzymywał.
  **Liczby:** `FontSize` 36 → **31** app-wide (w zakresie 22 → 17, Session zdjęty w całości) · `CornerRadius`
  17 → **9** · literały ikon 19 → **14** (Debugger zdjęty w całości).
  ⚠⚠ **SPROSTOWANIE:** zapis „sufit rośnie 20 → 25" (§19.40.4 i ta sekcja) **nigdy nie zgadzał się z kodem** —
  dodano +5 za `TableDetailTabView`, nie odejmując −6 zdjętych w tej samej iteracji z bliźniaków. Rzeczywisty
  sufit po M4.2 to **19**; tablica bazowa sumowała 19, więc strażnik był zielony i rozjeżdżała się wyłącznie
  **proza**.
  ⭐ **Trzy nowe strażniki, wszystkie RELACYJNE i wszystkie zweryfikowane podsadzeniem** —
  `BothEmptyStates_ShareOneTextRole` · `TheTraceFilterField_TakesTheHeightOfTheControlsBesideIt` (pilnuje
  PRZESŁANKI: czyta rolę z `ControlStyles.axaml`, skąd bierze ją prawdziwy `TextBox`, #322) ·
  `EveryInlineClearIcon_SharesOneSizeRole`.
  ⚠⚠ **DWA Z TRZECH BYŁY W PIERWSZEJ WERSJI BŁĘDNE i wykrył to dopiero PRZEBIEG:** strażnik ✕ grupował **po
  NAZWIE GEOMETRII** i padł na `MainWindow`, gdzie trzeci `Icon.X` to przycisk „Zamknij zakładkę" — samodzielna
  AKCJA, **poprawnie bez deklaracji**, biorąca 16 z `ControlTheme` (**#341**: §19.39.2a popełnione *wewnątrz
  strażnika napisanego, żeby temu zapobiec*); a strażnik wysokości łapał ogon **`MinHeight="0"`** i porównywał
  rolę z zerem. ⚠ I raz jeszcze pułapka §19.31: przebieg raportował „2 niepowodzenia" przy buildzie z **4
  błędami** (polski cudzysłów otwierający + ASCII zamykający w komunikacie asercji) — złapane wyłącznie
  dlatego, że `Liczba błędów` czyta się PRZED listą niepowodzeń.
  ⏸ **Otwarte po M4.3:** ⛔ **`Button.seg` → osobne M4.3c z QA behawioralnym** (decyzja użytkownika) —
  zmierzone: styl zadeklarowany **dwa razy lokalnie**, kopie różnią się `Padding` **8,3 vs 10,3**, przy
  komentarzu autora *„same segmented language as the Activity Monitor toolbar"*; ⚠⚠ konsolidacja **zmienia
  priorytet stylu** (mechanizm regresji §19.2, z powodu którego M3.4a odmówiło takiego przeniesienia) ·
  **B1** · **Z‑3** · migracja odstępów · ogon ikon 10/11/13/15 · `FontFamily` · `GridSplitter` · **M4.4**.
  ⏸ **Świadomie bez testu behawioralnego:** iteracja zmienia wyłącznie METRYKI i ROLE na
  `Border`/`TextBlock`/`SvgIcon` — zero zdarzeń, klawiatury, zaznaczenia i szablonowania, więc przesłanka
  reguły z M4.2b nie zachodzi. ⚠ Przy `Button.seg` zachodzi — i stąd M4.3c ma go mieć.

- **🌳🔒 M4.2b · DRZEWA „ZALEŻNOŚCI" — ZAMKNIĘTY, ODEBRANY PO QA WIZUALNYM UŻYTKOWNIKA (2026-08-08).**
  ⏭ **NASTĘPNY ETAP: M4.3** (Debugger · Trace · Session Manager · Security Manager · Performance) —
  ⛔ **rozpoczynany w NOWEJ sesji**; punkt startowy niżej.
  Build 0/0 w Debug i Release; suite **8329** (8179 + 95 + 55); smoke czysty. As-built: `product-polish.md`
  **§19.41**. Nowe gotchy **#338–#339**.
  ⭐ **17 drzew (nie 18) → jedna kontrolka** `Controls/DependencyTreeView`, na **tym samym
  `SidebarFlatController`**, którym działa drzewo połączenia. Znikło 17 kopii szablonu i 9 bajtowo
  identycznych kopii `OnDependencyNodeDoubleTapped`; model i role tokenów były wspólne już wcześniej —
  duplikacja siedziała **wyłącznie w warstwie widoku**. `MemberGroups`, `PlanRoots` i `Groups` świadomie poza
  zakresem.
  ⛔⛔ **§13.2 ODRZUCAŁO TĘ DROGĘ PRZESŁANKĄ, KTÓRA JEST NIEPRAWDZIWA** — pisało, że kontroler *„jest
  sprzężony z połączeniem, metadanymi, filtrowaniem, licznikami"*; zmierzone: jego konstruktor bierze
  **wyłącznie delegaty**, a całe to sprzężenie żyje w `MetadataExplorerViewModel`. ⭐ Zapis mówił prawdę
  o SĄSIEDZTWIE klasy, nie o klasie — czwarta przesłanka M4, która nie przeżyła zderzenia z kodem.
  ⚠ Wykrył to **użytkownik z działającej aplikacji**, nie ja z dokumentu.
  ⭐ **Wysokość wiersza `Size.Row.Tree` (24)** — ten sam rodzaj elementu co wiersz Metadata Explorera;
  reguła **zawężona do tej kontrolki**, bo globalny `TreeViewItem` obsługuje też Performance i Global Search.
  Nowa rola **`Margin.PanelCaption`** (`Space.Md` + `Space.Xs`) dla 16 nagłówków — bez nowej wartości;
  ⚠ świadomie NIE `Pad.Tab`, mimo że pionowo pasuje: jego rola opisuje zakładkę (R12).
  ⭐ **JEDNA kanoniczna kolejność kategorii** (`MetadataCategoryOrder.All`), z której każde drzewo filtruje
  swoje — bo dwie tablice, które dziś się zgadzają, jutro się rozjadą. Odniesieniem jest drzewo połączenia,
  więc **nie zmieniła się w nim ani jedna pozycja**. ⛔ **„UDF" usunięte w całości** (kategoria historyczna,
  zawsze pusta, produkt wspiera FB5); ⭐ skutek uboczny lepszy od poprawki: **każda kategoria ma teraz `Kind`**,
  więc lista jest wyłącznie zawężeniem kanonicznej — zero pozycji wstawianych lokalnie.
  ⭐ **Nawigacja ←/→ wspólna dla OBU drzew** — reguła w `SidebarFlatController.Navigate`, wpięcie
  w `Behaviors/SidebarKeyboardNavigation`. ⭐ Kontroler decyduje, widok wykonuje, dzięki czemu **7 testów
  reguły jest CZYSTYCH** i nie powiększa listy klas headless.
  ⛔⛔ **TRZY DEFEKTY PRZESZŁY PRZEZ ZIELONY SUITE I ZOSTAŁY ZŁAPANE DOPIERO PRZEZ QA UŻYTKOWNIKA** — to jest
  główny wynik tego etapu: (1) **pusty ekran**, bo podklasa `TreeView` nie dostaje `ControlTheme` (Avalonia
  szuka go po typie KONKRETNYM) — naprawa `StyleKeyOverride`, **#338**; (2) **←/→ nie działało**, bo `ListBox`
  obsługuje strzałki w class handlerze i handler instancyjny nie jest wołany — naprawa **tunel**, #224
  o rodzinę kontrolek dalej; (3) **zaznaczenie ginęło po rozwinięciu**, bo odwzorowywanie wierszy przez
  `Clear()` kasuje `SelectedItem` — ⚠ pojedynczy klawisz przechodził, ujawniły to dopiero DWA po sobie
  (**#339**). ⭐ Lekarstwem są **testy behawioralne**, których etap nie miał na starcie
  (`DependencyTreeRenderTests` — realizacja wierszy, ujawnienie dzieci, przebarwienie ikon, **realny klawisz
  przez pełny pipeline**); asercją jest **zrealizowany kontener**, nie `ItemCount` (w chwili awarii `ItemCount`
  = 1 przy 0 wierszy).
  ⚠⚠ **Trzy razy pomiar wykazał błąd w MOICH WŁASNYCH testach** — fikstura przechodząca z zepsutą
  implementacją (pusty węzeł stał ostatni), strażnik liczący ZAKOMENTOWANE wpięcie, test wysyłający klawisz
  **donikąd** (`list.Focus()` nie ustawia fokusu w sesji headless — fokusować trzeba KONTENER WIERSZA).
  Wszystkie trzy wykryło podsadzenie albo pomiar, nigdy czytanie kodu.
  ⏸ **Otwarte po M4.2b:** **B1** z M4.2 (prywatne ikony PK/FK/Unique w `TableDetailTabView`, rysowane surowym
  `<Path>` na siatce 14 — przygotowane, wygląd nierozstrzygnięty) · **Z‑3** · migracja odstępów · ogon
  literałów ikon 10/11/13/15.

- **🧱🔒 M4.2 · EDYTORY OBIEKTÓW — ZAMKNIĘTY, ODEBRANY PO QA WIZUALNYM UŻYTKOWNIKA W OBU MOTYWACH
  (2026-08-08).** Build 0/0 w Debug i Release; suite **8313** (8167 + 91 + 55, +2); smoke czysty. As-built:
  `product-polish.md` **§19.40**. Nowa gotcha **#337**.
  🔒 **B2 ROZSTRZYGNIĘTE NA RENDERZE: karta aktywności bierze `Radius.Surface` (4 → 3), wariant `Radius.Card`
  = 4 ODRZUCONY.** ⭐ Uzasadnienie jest merytoryczne, nie estetyczne: komentarz roli w `Tokens.axaml` zaczyna
  się od słowa **„Karta"**, więc rola opisywała ten element od zawsze, a element jej odmawiał. ⭐⭐ To **R12
  przeczytane w drugą stronę** — reguła chroni wartość lokalną, która ma powód, ale nie legalizuje takiej,
  której powodem było *„czeka na decyzję"*. ⚠ Różnica wobec **K10** (gdzie nowa rola `Radius.Tab` = 4 była
  właśnie właściwa) jest rzeczowa: tam sporna była **NAZWA przy zgodnej liczbie**, tu **LICZBA przy zgodnej
  nazwie**; §3.3 pozwala dwóm rolom dzielić wartość, nie pozwala dwóm wartościom dzielić roli.
  ⭐⭐ **ZNALEZISKO GŁÓWNE: M4.2 w rozumieniu „policzone literały → role" BYŁO JUŻ WYKONANE.** Pomiar
  (regeksy strażników odtworzone 1:1) dał **zero** dla `FontSize`, `CornerRadius`, literałów ikon **i kolorów
  zaszytych** w ośmiu z dziesięciu edytorów; w bliźniakach zostały wyłącznie pozycje **ratyfikowane, żeby
  zostać** (dwa `TextEditor` 12 px w wierszu siatki + znak rodzaju 9 px — decyzje KONTENERA z §18.0.5/3,
  wyłączone wprost w §19.38.7). Robotę wykonały **M2c iteracja 5** i oba bloki decyzyjne M4. ⭐ Potwierdza się
  §18.5.2: wyjątki biorą się z tego, **ile RÓŻNYCH decyzji projektowych zapadło w pliku**, a nie z jego
  wielkości ani wieku — edytory obiektów stoją na jednym wzorcu i nie mają nic własnego.
  ⭐ **Co weszło:** sześć ikon karty aktywności w bliźniakach (literał `12`) → **`Size.Icon.Sm`**, którego
  własny opis brzmi *„ikona inline w tekście 11 px"*, a każda z nich stoi obok tekstu `Text.Compact.Size` (11).
  Wartość 12 → 12, **wygląd bez zmiany**; bliźniaki znikają z sufitu ikon (3 + 3 → 0). ⚠ To część ogona
  sparkowanego w §19.37.7, ale **tylko dla 12 rola już istnieje i pasuje opisem** — tu nie było pytania, tylko
  odpowiedź; ogon 10/11/13/15 zostaje sparkowany.
  ⛔⛔ **B1 — `TableDetailTabView` MA WŁASNY, PRYWATNY SYSTEM IKON.** Trzy `StreamGeometry` deklarowane
  lokalnie (PK / FK / Unique), rysowane **surowym `<Path Fill=…>`** pięć razy, na **siatce 14 jednostek**
  zamiast kanonicznych 24. ⭐⭐ Ten sam obchód ukrył plik przed **trzema mechanizmami naraz**: domyślnym
  rozmiarem z `ControlTheme` (A‑3), audytem wyśrodkowania z rundy QA M4.1 (czyta `IconGeometries.axaml`)
  i licznikiem literałów, który wymagał `<controls:SvgIcon` — więc **plik raportował 0 przy pięciu literałach**.
  ⚠ Zmierzone: **to NIE duplikat** — katalog nie ma ikony klucza głównego, obcego ani unikalności, więc
  przeniesienie do systemu = przerysowanie na siatkę 24 = **zmiana wyglądu**. 🔒 Decyzja użytkownika:
  **przygotować jako osobny przypadek, wyglądu NIE rozstrzygać w M4.2** — zrobione dokładnie tyle, dług jest
  teraz widoczny i zmierzony.
  ⭐ **Strażnik ROZSZERZONY, nie dobudowany obok:** `MeasureIconSizeLiterals` dostał `|Path` w istniejącym
  regeksie; **bez ani jednego wyjątku**, bo w całym `src/` jest 9 elementów `<Path>` — 5 to te ikony, 4 to
  wnętrza `ControlTemplate` i żaden nie deklaruje literału. Druga połowa jest **strukturalna**: geometria poza
  `IconGeometries.axaml` musi nieść **zapisany powód** (wzorzec `DatePresentationTests`), plus strażnik
  nieaktualnych wyjątków (#333). ⚠⚠ **Sufit rośnie 20 → 25 i to KOREKTA POMIARU, nie regresja** — ⛔ nie wolno
  <!-- ⚠ SPROSTOWANIE (M4.3, 2026-08-09): liczba „25" jest BŁĘDNA i nigdy nie zgadzała się z kodem — do 20
       z M4.1 dodano +5 za `TableDetailTabView`, nie odejmując −6 zdjętych w tej samej iteracji z bliźniaków.
       Rzeczywisty sufit po M4.2 to **19** (tyle sumowała tablica bazowa, więc strażnik był zielony —
       rozjeżdżała się wyłącznie proza). Po M4.3 jest **14**. Zdanie zostaje jako zapis tego, co napisano. -->
  „naprawiać" tego obniżeniem wpisu. **Wszystkie trzy gałęzie zweryfikowane podsadzeniem** (podsadzenie `5 → 4`
  jest jedynym dowodem, że regeks naprawdę liczy pięć `<Path>`, a nie że wpisałem pasującą liczbę).
  ⚠⚠ **B2 WYPADŁ MIĘDZY ETAPAMI i to jest lekcja o rejestrze, nie o promieniu:** §18.4.5 oddał decyzję bramie
  §13.3, **a §13.3a nigdy jej nie podjęła** (Z‑1…Z‑6 tego nie dotyczą) i pozycja **nie dostała numeru K**, więc
  przeżyła zamknięcie rejestru kolizji „w całości". ⭐ **Odesłanie do etapu NIE JEST wpisem do rejestru** —
  widać to dopiero, gdy ktoś idzie po plikach, a nie po rejestrze. Materiał: `VisualCandidateProbe -- radius`
  (oba motywy, 1:1 **i ×4**, karta w otoczeniu `Radius.Surface`/`Radius.Chip`).
  ⭐ **Dwie hipotezy o wspólnym wzorcu OBALONE pomiarem** (wynik negatywny wart zapisania): `MinHeight` 60 vs 80
  to dwa różne pola, a `EditableDescription` (2 × 80 przy 8 bez deklaracji) to **reguła #10 działająca
  poprawnie** — 8 bierze rozmiar od kontenera, 2 stoją w wierszu formularza. Pułapka 17.
  ⛔ **Poza zakresem, decyzja użytkownika:** `GridSplitter Height="4"` ×5 (element chromy dzielony z M4.3/M4.4,
  rozstrzyga się raz, na komplecie — R7) · migracja odstępów · M4.2b · Z‑3.

- **🖼 M4.1 · ZAMKNIĘTY, ODEBRANY PO QA WIZUALNYM UŻYTKOWNIKA (2026-08-08).** Build 0/0; suite **8311**
  (8165 + 91 + 55, +7 — sześć przypadków teorii strażnika odstępów + strażnik wyśrodkowania ikon); smoke czysty. As-built:
  `product-polish.md` **§19.39**. Nowa gotcha **#335**.
  ⭐⭐ **SUFIT LITERAŁÓW ROZMIARU IKONY 95 → 20, I NIE MA JUŻ ANI JEDNEGO LITERAŁU `14` ANI `16`** — zostaje
  wyłącznie ogon 10/11/12/13/15, czyli pytanie o ROLE, sparkowane osobno w §19.37.7.
  ⭐ **Znalezisko, które ukształtowało iterację: pasek paginacji nie zgadzał się sam ze sobą.** Populacja `14`
  to **75 z 95 literałów**, a jej druga połowa — samotna ikona w `Button.icon` — była sporna, bo **identyczny
  kształt renderował się 16 w 135 miejscach i 14 w 34**. Zejście do konkretnej KONTROLKI pokazało rozjazd, nie
  rolę bez nazwy: ten sam pasek paginacji (te same stringi `TableDetailPagination*Tooltip`) miał **14**
  w wynikach SQL i Table Data, a **16** w edytorach funkcji / procedury / widoku; tak samo trio
  filtr/agregacja/eksport i chevron zwijania panelu. ⚠ Obie populacje żyją **w tych samych plikach**, więc nie
  opisuje tego reguła ani per plik, ani per powierzchnia.
  ⭐⭐ **Reguła jednak ISTNIEJE — łamały ją trzy grupy, a nie „wszystko", i dowodzi tego `Icon.RefreshCw`**,
  jedyna ikona stojąca po OBU stronach linii i za każdym razem trafiająca dobrze: **16** jako przycisk paska
  narzędzi, **14** gdy odświeża SIATKĘ (Table Data).
  🔒 **Decyzja użytkownika: „dokończyć regułę, którą produkt już ma" — wszystkie trzy grupy na `Size.Icon`.**
  Kryterium A‑3 to drabina *stoi w SERII vs stoi SAMOTNIE*, a nie *czy jest klikalne*: przycisk paginacji stoi
  w serii czterech. ⭐ Wariant „wszędzie 16" odrzucony, bo rósłby powierzchnie już odebrane — wybrany
  **wyłącznie ZMNIEJSZA**. Wykonanie: 75 ikon literał → rola (**wygląd bez zmiany**) + **18 ikon 16 → 14**
  (paginacja w edytorach funkcji/procedury/widoku, chevrony panelu w Session i Trace, filtr+eksport w Trace) —
  ⏸ **to te 18 jest przedmiotem QA**, reszta jest wizualnie zerowa z konstrukcji. Reguła zapisana w komentarzu
  `Size.Icon` w `Tokens.axaml`, razem z dowodem i odrzuconym wariantem.
  ⚠ **Zakres świadomie wyszedł poza trzy ekrany M4.1** — rozjazd jest z natury app-wide (jedna kontrolka
  w pięciu ekranach), więc migracja per ekran zostawiłaby pasek niezgodny ze sobą do M4.3 (R7). M4.2/M4.3
  zastaną mniej literałów, niż zakładał ich sufit.
  ⛔⛔ **DRUGIE ZNALEZISKO — `Spacing`/`Padding`/`Margin` NIE BYŁY NIGDY LICZONE, i to nadal NIE jest migracja.**
  Zmierzone app-wide: **985 wartości lokalnych w 150 wpisach plikowych**, przy odczycie roli **zero razy** dla
  `Padding` i `Margin` — mimo że katalog ma dla nich siedem stopni skali od M2a. 🔒 Decyzja użytkownika:
  **sam strażnik, zero migracji, odstępy jako osobny etap po M4.4**; ⛔ *„nie zmieniaj teraz żadnych wartości
  odstępów tylko po to, żeby zadowolić nowego strażnika"* — i ani jedna nie została zmieniona. Trzy własności
  **dołączyły do ISTNIEJĄCEGO mechanizmu** (`GuardedProperties`), baseline **per plik, nie sumą** (suma
  przepuściłaby +5 w jednym widoku przy −5 w innym), policzony **tym samym `Measure`, który egzekwuje regułę**.
  Zweryfikowany podsadzeniem naruszenia. ⭐ Dla przyszłego etapu: **214 z 320 `Spacing` pokrywa się 1:1** ze
  `Space.Sm`/`Space.Xs`/`Space.Md` i będzie mechaniczne — treścią tamtego etapu jest ogon (5, 2, 1, 10, 3 px).
  ⭐ **RUNDA QA (2026-08-08): 18 ikon 16 → 14 odebrane bez uwag; jedno nowe zgłoszenie — strzałki Undo/Redo
  „wyglądają na nierówno ustawione".** Zgłoszenie **trafne**, i przyczyną jest **GEOMETRIA, nie
  pozycjonowanie**: przycisk, kontener, padding i rozmiar są identyczne jak u sąsiadów, ale szablon `SvgIcon`
  to `Viewbox` nad **stałym** `Canvas 24×24`, więc położenie ścieżki przenosi się 1:1 na render i **nic go nie
  normalizuje**. Zmierzone: środek tuszu **14,75 zamiast 12**, czyli **1,83 px za nisko przy 16 px** — strzałka
  jest wyśrodkowana, ściąga ją **łuk powrotu**. ⭐⭐ Audyt **wszystkich 86 geometrii** zamienił wrażenie
  w rozkład: **73 mieszczą się w 0,25 jednostki od środka, 83 w 1,0**, a Undo/Redo były **dwoma z trzech**
  największych odchyleń w aplikacji. Poprawka: **czyste przesunięcie o 2,5** (nie 2,75 — zostawia współrzędne
  na siatce 0,5, a reszta 0,17 px leży w paśmie rodziny), kanoniczne `.svg` zaktualizowane, decyzja podjęta
  **na renderze**. ⛔ `Icon.StepOver` (+1,62) **świadomie nietknięty** — wyjątek z powodem: „przeskok" nie ma
  podłogi ani sufitu jak rodzeństwo, to pasek debuggera (M4.3), a tę trójkę trzeba oceniać razem.
  ⛔ Oś pozioma nie jest pilnowana: `Icon.Play` +1,5 to **poprawna korekta optyczna** trójkąta (#288).
  ⛔⛔ **I strażnik, który to pilnuje, w pierwszej wersji MIERZYŁ INNYM SILNIKIEM NIŻ RENDER** — liczył
  `GetRenderBounds` w sesji headless i zgłosił **sześć fałszywych naruszeń**, wszystkie z łukiem: platforma
  headless **ignoruje wybrzuszenie łuku**. `Icon.Search` to u niej środek 16,00, a w Skii 12,00. Naprawione
  pomiarem **wprost SkiaSharpem**, przez co test nie potrzebuje platformy Avalonii i **zostaje w partycji
  głównej**, nie powiększając listy klas headless. Obie gałęzie zweryfikowane podsadzeniem. Nowa gotcha
  **#336**; as-built **§19.39.7**.
  ⚠⚠ **Dwa moje własne zapisy z tej iteracji były NIEPRAWDZIWE i zostały wycofane przed zmianą kodu**
  (§19.39.2a): „pasek paginacji ma TRZY rozmiary, w tym w debuggerze" — **debugger nie ma paska paginacji**,
  a `Icon.ChevronRight` pełni dwie funkcje (paginacja i chevron ujawnienia); oraz wynikające z tego oskarżenie,
  że **blok gęstości pogłębił rozjazd** — sweep A‑3 nie zmienił tam ani jednej wartości. ⭐ Grupowałem po NAZWIE
  GEOMETRII, czyli popełniłem dokładnie ten błąd, który w tej samej iteracji opisywałem jako #335; wykryło to
  dopiero wypisanie HOSTA (tooltipa i komendy) obok każdej ikony — pytanie *„czym ta rzecz jest dla
  użytkownika"*, a nie *„jak nazywa się jej ścieżka"*.

- **🔤🔒 M4 · BLOK TYPOGRAFII — ZAMKNIĘTY, ODEBRANY PO QA WIZUALNYM UŻYTKOWNIKA (2026-08-08).**
  ⭐⭐ **TYM SAMYM DECYZJE PROJEKTOWE M4 SĄ KOMPLETNE** — zostaje migracja ekranów **M4.1–M4.4** i odłożone
  **Z‑3**. Punkt startowy następnej sesji:
  **[docs/design/product-polish-m4-migration-next-session.md](docs/design/product-polish-m4-migration-next-session.md)**.
  ⚠ **ZAPIS HISTORYCZNY — ten wskaźnik był aktualny 2026-08-08.** Migracja M4.1–M4.4 jest zamknięta, a tamten
  dokument jest historyczny; żywy punkt startowy to `product-polish-m5-next-session.md`.
  Zgodnie z **D‑M4‑1** to druga i ostatnia pozycja rejestru; po niej **rejestr kolizji K1–K15 jest ZAMKNIĘTY
  W CAŁOŚCI** i nie zostaje ani jedna otwarta kolizja. Build 0/0; suite **8304** (8158 + 91 + 55); smoke
  czysty; trzy nowe strażniki **zweryfikowane podsadzeniem**. Materiał + pomiar:
  **[docs/design/product-polish-m4-typography-decision.md](docs/design/product-polish-m4-typography-decision.md)**;
  as-built: `product-polish.md` **§19.38**. Nowa gotcha **#334**.
  ⭐⭐ **ZNALEZISKO GŁÓWNE — nagłówek sekcji był MNIEJSZY od tekstu, który nazywa.** `Text.SectionHeader`
  niosła 11 SemiBold, a jej kanoniczny konsument stoi w **każdym z 19 użyć** nad `field-label`, który niesie
  12 — więc obietnica roli („mocniejszy od podpisu pola") była prawdziwa wyłącznie o WADZE. ⭐ Dowodem był
  **ROZKŁAD, nie pojedynczy ekran**: pięć widoków niezależnie odmówiło tej roli, a populacje wyszły **19
  nagłówków przy 11 i 17 przy 12, w identycznym kontekście**. Rola idzie na **12 SemiBold** (interlinia za
  rozmiarem, 15 → 17), 17 wyjątków znika. Kształt `Size.Row.Tree` z M3.4a.
  ⭐ **`Text.Toolbar` wycofana — duplikowała `Text.Compact` ROLĄ, nie tylko liczbą** (tamta mówi wprost
  „chroma — panele, zakładki, **paski**"). Zero konsumentów, przy trzech paskach na 11 i jednym na 12;
  pasmo importu zeszło 12 → 11. ⚠ Trzy podpisy w tym pasmie zachowały `field-label` (barwa + margines)
  i dostały **jawny** rozmiar chromy — 164 użyć tej klasy blok nie rusza.
  ⚠⚠ **K9 wskazywał ZŁY ELEMENT:** `.bottom-tab`/`.sub-tab` były na roli od M2c/M3, a trzynastka siedziała
  na **bazowym** `TabItem` obsługującym 10 zakładek dialogowych — „zakładka" okazała się nośnikiem **trzech**
  różnych rzeczy. ⛔ **K5 SKREŚLONE — nie miało przedmiotu** (rozwiązane w chwili zapisu, jak wycofane K12).
  🔒 **K4 ZOSTAJE 13 — ratyfikowana decyzja użytkownika, nie dług:** *„to pojedynczy element pełniący rolę
  nagłówka planu i nie widzę potrzeby sztucznego sprowadzania go do 12 tylko po to, żeby zniknął literał"*.
  ⭐ To **R12** wypowiedziane wprost; render pokazał, że zejście do 12 jest możliwe — i właśnie dlatego
  odmowa jest decyzją, a nie ograniczeniem. ⛔ Nie „dokańczać" tego bloku.
  ⭐ **D w całości:** lokalne `MinHeight="26"` usunięte (nagłówek `Expandera` bierze `Size.Control` z mostu) ·
  chip transakcji na **`Space.Xs`** (4), nie `Space.Sm` (6) — bo to „elementy wewnątrz jednego wiersza",
  a przy równej czytelności wygrywa gęstszy (**R18**); zbiega się z paddingiem badge'a DEV MODE · nowa rola
  **`Radius.Tab`** = 4, bo sporna była NAZWA, nie liczba (§3.3 pozwala dwóm rolom dzielić wartość).
  ⚠ Kolumny **×3** w renderze były częścią pytania: przy 1:1 wszystkie trzy pary wyglądają identycznie,
  a *„nie widać różnicy"* i *„render nie pokazuje różnicy"* wyglądają tak samo.
  ⏸ **Wycofane po zbudowaniu i wymaga OSOBNEJ decyzji:** poszerzenie okna licznika `FontSize` o `Completion/`,
  `Sql/` i `Themes/`. ⭐ Zmierzone: **29 deklaracji leży poza jego zasięgiem** (karty hover, Quick Info,
  Parameter Helper — powierzchnie oglądane przy pisaniu każdego zapytania). Ten sam `Measure` obsługuje jednak
  **`FontFamily`**, czyli temat czcionki monospace ratyfikowany jako backlog sprintu UX — to nowa informacja
  wobec rekomendacji, więc nie wsuwam jej przy okazji. Pomiar w komentarzu `DesignTokenComplianceTests`.
  ⚠ **Jeden czerwony test w pierwszym przebiegu:** `SettingsLoadHealthTests.ConcurrentSaves_…` — znany
  jednorazowy flake ze sprintu stabilizacyjnego; **3 przebiegi solo zielone i partycja zielona przy
  powtórzeniu**. Nie ma związku z typografią i nie jest ogłaszany naprawionym.

- **📐🔒 M4 · GRUPA GĘSTOŚCI — ZAMKNIĘTA, ODEBRANA PO QA UŻYTKOWNIKA W OBU MOTYWACH (2026-08-08).**
  QA objęło siedem powierzchni: pasek narzędzi · Metadata Explorer · menu kontekstowe · zakładki · siatki
  edytowalne · pasek importu · Security Manager. **Zero nieprawidłowości i zero regresji wizualnych.**
  ⚠ Poza zakresem tego QA (i nadal otwarte): **150 % DPI** — R‑6 jest częściowo nieweryfikowalne headlessowo
  i sprawdza się okiem, a ta iteracja ruszyła metryki. Zgodnie z **D‑M4‑1** etap otworzył **rejestr kolizji**, nie migracja ekranów,
  a zgodnie z **D‑M4‑2** cała gęstość poszła jako **jedno pytanie**. Build 0/0; suite **8301** (8155 + 91 + 55);
  smoke czysty; cztery nowe strażniki **zweryfikowane podsadzeniem**. Materiał decyzyjny + pełny pomiar:
  **[docs/design/product-polish-m4-density-decision.md](docs/design/product-polish-m4-density-decision.md)**;
  as-built: `product-polish.md` **§19.37**. Nowe gotchy **#332–#333**.
  ⭐⭐ **NOWA REGUŁA OBOWIĄZUJĄCA W CAŁYM M4 — R18 (ratyfikowana z decyzjami):** *„jeżeli dwa warianty są
  równie czytelne, wybieramy ten gęstszy; EmberTern jest narzędziem dla deweloperów baz danych, więc
  priorytetem jest ilość informacji widocznych na ekranie bez pogarszania czytelności"*. ⚠ To reguła
  ROZSTRZYGAJĄCA REMIS — warunek „bez pogarszania czytelności" jest pierwszy, a jego kryterium jest ekran
  (R16), nie arytmetyka.
  ⭐⭐ **TRZY ZAPISY NIE PRZETRWAŁY POMIARU, I KAŻDY ZMIENIAŁ KSZTAŁT PYTANIA.** (1) „`Size.Icon` — 64
  literały" opisywało 164 z **355 deklaracji ikon**: **191 nie deklaruje nic** i bierze **16** z
  `ControlTheme`, a rola miała **dwóch** konsumentów — komentarz obiecywał „toolbar, zakładka, drzewo, menu"
  i 14, zmierzone **16 / 14 / 15 / 14** (gotcha #332). (2) „K15 — 112 wystąpień w 17 plikach, zmiana
  rozjechałaby drzewo z resztą aplikacji (R7)" — naprawdę **44 ikony w 13 plikach, 41 z nich to WIERSZ
  DRZEWA**, więc ⭐ **R7 przemawiał ZA zmianą całej roli naraz, nie przeciw niej**. (3) ⛔ **Z‑3: liczby
  40 px NIE MA w `src/`** — `data-edit` ma stałe `Height="32"` od commita sprzed bramy, a siatka wyników nie
  ma własnego stylu wiersza; **Z‑3 zostaje otwarte do pomiaru na żywej aplikacji** i świadomie NIE weszło do
  tej iteracji.
  **Co weszło:** ⭐ **A‑3** — chroma ma dwa nazwane poziomy: `Size.Icon.Toolbar` (16; przejęła nazwę po
  `Size.Icon.Lg`, która miała **zero** konsumentów, więc migracja jest wizualnie zerowa) czyta ją domyślna
  `ControlTheme` ikon i 16 literałów paska narzędzi/okna, a `Size.Icon` (14) obsługuje **wiersz** — zakładka,
  drzewo, menu (`MenuMarkup.cs` czyta rolę, nie literał). ⭐⭐ To **nie nowa zasada, tylko drabina z M2b
  przeniesiona o poziom niżej**: *„pole stoi w SERII, przycisk stoi SAMOTNIE i jest CELEM MYSZY"*
  (`Size.Control` 24 / `Size.ControlToolbar` 22 → `Size.Icon` 14 / `Size.Icon.Toolbar` 16). ⭐ **B‑1** — 41 par
  `15 + Spacing 5` → `Size.Icon` + `Space.Xs`; **wysokość wiersza drzewa się nie zmienia** (`Size.Row.Tree` to
  `MinHeight` 24), zmiana jest wyłącznie pozioma. ⭐⭐ **C‑1** — nowa rola **`Size.Row.GridEdit` = 30**
  zastąpiła 34/32/30: **wszystkie** siatki definicji mają `Padding="6 2"` (pion 4), edytor ma `Size.Control`
  24, więc minimum to **28** — trzy liczby na jedno wymaganie, żadna z niego nie wynikała. ⭐ **D** — podłogi
  `Transaction` (170) i `Errors` (180) zdjęte, `Profile` 170 → **140**; zmierzone naturalne szerokości
  110 / 90 / 137, a `ComboBox` mierzy się do **najszerszej pozycji**, nie do zaznaczonej, więc lista
  o zamkniętym zbiorze jest stabilna sama z siebie. **~154 px wróciło przyciskom.**
  ⚠ **Świadome wyłączenia z powodem:** Security Manager (28 / `.checkbox-grid` 34) — jego komórką
  interaktywną jest `CheckBox`, nie pole `Size.Control`, więc przesłanka roli jest tam inna (#322) · trzy
  ikony 15 px, które nie są wierszem drzewa · pole trafienia chevronu 20×20 (cel myszy, nie rozmiar rysunku).
  ⚠⚠ **DWA ISTNIEJĄCE STRAŻNIKI ZAWIODŁY NA TEJ ZMIANIE I ŻADEN DLATEGO, ŻE PRODUKT JEST ZŁY** — oba
  pilnowały przesłanki „wiersz musi unieść edytor 24", **czytając ją jako LITERAŁ**, więc migracja na rolę
  odebrała im zdolność czytania; jeden miał wysokość **przepisaną do stałej `= 32`**. ⭐ Naprawione przez
  **rozwiązywanie roli**, nie przez osłabienie asercji, i przy okazji jeden z nich objął **wszystkie** siatki
  edytowalne zamiast jednej (gotcha #333). ⚠ Pierwsza wersja nowego strażnika drzewa **skanowała cały plik**
  i zgłaszała poprawne ikony o roli `Size.Icon.Sm`; zawężona do trzech szablonów po `DataType`.
  ⏸ **Otwarte po tej iteracji:** sweep **95 pozostałych literałów** rozmiaru ikony (**M4.3** — sufit
  `IconSizeLiteralBaseline` pilnuje, żeby nie przybyło, więc „ile zostało" jest liczbą) · ogon 10/11/13 px
  (pytanie o ROLE) · **Z‑3** · Security Manager · reszta rejestru **K1–K11**, w którym po zamknięciu K15
  ⭐ **nie ma już ani jednego pytania o gęstość** — zostały pytania typograficzne, promień i odstęp chipa.
  ⛔ Migracja ekranów dopiero po rejestrze (**D‑M4‑1**).

- **🔤🔒 SPRINT ZGODNOŚCI Z GRAMATYKĄ FIREBIRDA + POLISH (P1…P6 + runda QA) — ZAMKNIĘTY, ODEBRANY PO QA
  UŻYTKOWNIKA (2026-08-08).** Sześć zgłoszeń z normalnego użycia, świadomie **przed M4**, plus cztery poprawki
  z QA. Build 0/0 w Debug i Release; suite **8291** (8145 + 91 + 55); smoke Debug + Release czysty;
  `DebuggerFidelityProbe` **40/40 ALL PASS** i `ChangeSafetyProbe` ALL PASS na żywym FB5. Narracja:
  **[docs/history/26-firebird-language-completeness-sprint.md](docs/history/26-firebird-language-completeness-sprint.md)**.
  Nowe gotchy **#324–#331**.
  ⚠ Skok 7378 → 8263 to **nie** ~900 ręcznie napisanych asercji: 80 pozycji korpusu weszło do
  `SqlTestCorpus.All`, które zasila teorie formatera (§0 round-trip, idempotencja, casing) i harness różnicowy
  AST — ~11 przebiegów na pozycję. ⚠ `GridDateEditorTests` dołącza do partycji headless **zbiorczej** — a jej
  brak w filtrze partycji był defektem samego dokumentu, naprawionym przy zamykaniu (patrz „Tests").
  ⭐⭐ **RUNDA QA (2026-08-08) — cztery poprawki, wszystkie o TEJ SAMEJ granicy: o formacie i o wyborze
  kontrolki decydował dotąd typ CLR, a nie typ kolumny Firebirda.** Nowe gotchy **#329–#331**.
  🐞 **`CalendarDatePicker` obsługiwał też kolumny TIMESTAMP, i defekt był groźniejszy niż zgłoszenie:** widoczna
  połowa to „nie da się edytować czasu", prawdziwa — `SelectedDate` sprawiał, że **zatwierdzenie wybranej daty
  zapisywało północ na miejsce godziny, którą wiersz już miał** (reguła #11). ⭐ Zamiennik wybrany z **pomiaru
  frameworka**: Avalonia 12.1.1 nie ma kontrolki łączącej datę z czasem (`CalendarDatePicker`/`DatePicker`/
  `TimePicker`/`Calendar`), więc TIMESTAMP edytuje się **jako tekst**. ⭐ Zatwierdzenie **parsuje na typowany
  `DateTime`**, bo Firebird czyta literał po SEPARATORZE (`07/08/2026` = 8 lipca), a siatka pokazuje pisownię
  kultury czytelnika; kolejność: forma silnika → kultura czytelnika → invariant → tekst dosłownie.
  ⛔⛔ **Prezentacja w siatce danych idzie z typu METADANYCH Firebirda, nigdy z typu CLR** (dyrektywa
  użytkownika): `DATE` → sama data · `TIME` → sam czas · `TIMESTAMP` → data i czas · `… WITH TIME ZONE` → pełna
  wartość ze strefą. ⚠ Kusząca naprawa „ukryj czas, gdy północ" jest **gorsza** — ukrywa prawdziwe `00:00:00`
  na TIMESTAMP. Nowe `DateTimeDisplay.CellForType(value, firebirdType)`; **martwy `Cell(object?)`**, który
  rozstrzygał dokładnie tą heurystyką i nie miał konsumenta, **usunięty**. ⚠ Siatka wyników SQL świadomie poza
  zakresem: `QueryColumn` niesie `ClrType`, nie typ Firebirda.
  ⭐ **W debuggerze formą czytelną jest forma SILNIKA** (`yyyy-MM-dd [HH:mm:ss]`) — Variables/Context, Watche,
  wartości inline i zasiew pola edycji; ⛔ liczby zostają invariantne (konwencja literałów harnessu).
  ⚠⚠ **Edytor siatki pracuje do SEKUNDY (decyzja użytkownika), a to samo w sobie tworzyło drugi defekt:**
  `DataGrid` zatwierdza komórkę dlatego, że wyszedł z niej fokus, więc przejście Tabem zapisywałoby zaokrągloną
  wartość na miejsce ułamkowej. Stąd **„nic nie wpisano ⇒ nic nie zapisano"** i **jeden właściciel zasiewu**
  (`EditorSeedText`, czytany przez szablon i przez to sprawdzenie). ⚠ Debugger zachowuje pełną precyzję —
  tam wartość się OGLĄDA, a nie przepisuje.
  ⭐⭐ **TRZY Z CZTERECH PUNKTÓW PO POMIARZE OKAZAŁY SIĘ CZYMŚ INNYM, NIŻ OPISYWAŁO ZGŁOSZENIE — a w P5
  pomiar ODWRÓCIŁ DIAGNOZĘ:** siatki danych nigdy nie były przywiązane do `InvariantCulture`, tylko wiernie
  odtwarzały **nadpisanie krótkiej daty w Windows samego zgłaszającego** (`pl-PL` z `yyyy-MM-dd`; kultura bez
  nadpisania daje `d.MM.yyyy`, prawdziwy invariant `08/07/2026` — trzy rozróżnialne wyniki, więc pomiar jest
  rozstrzygający). **Na wskazanej powierzchni nie było defektu, i powiedzenie tego wprost jest wynikiem**
  (gotcha #328). Zaszyte było to, czego zgłoszenie nie wymieniało: data wydania w About pod
  `InvariantCulture`'s `d MMMM yyyy`, czyli **angielska nazwa miesiąca na każdej maszynie**, oraz etykieta
  historii parametrów.
  ⭐⭐ **P2 — audyt gramatyki. Reguła, która się ZBIEGA, bo jest ograniczona JĘZYKIEM, a nie zgłoszeniami
  (gotcha #324).** Cztery poprzednie poprawki dodały po jednym predykacie POZYCYJNYM (`NEXT VALUE FOR`, potem
  `GEN_ID`, potem `EXTRACT`) — czyli listę wyjątków, której kompletność wyznaczają zgłoszenia, które ją
  zbudowały. Nowy **korpus `SqlTestCorpus.LanguageReference`** (80 konstrukcji, przejście rozdział po rozdziale
  przez Language Reference) zmierzył stan wyjściowy: **26 z 80 pozycji produkowało fałszywe znaleziska albo
  błędne wiązania**, a zgłoszone konstrukcje były podzbiorem. Rozwiązanie to **dwa uzupełniające się
  mechanizmy**: nowa klasa Core **`FirebirdGrammar`** (wiedza POZYCYJNA — gniazda słów w built-inach, ramka
  okna, pozycja typu `CAST`, tablica fraz; przejęła oba stare predykaty, jeden właściciel) i
  **`FirebirdSyntax.IsNonReservedWord`** (SŁOWNIK, którego jedyną dozwoloną konsekwencją jest MILCZENIE —
  nigdy nie tłumi wiązania). ⚠ **Słownik sam był za tępy i dowiódł tego ISTNIEJĄCY strażnik**
  (`TheSameWord_OutsideExtract_IsStillFlagged`: *„the word is not exempt — only the position is"*), więc
  regułą jest **SĄSIEDZTWO SŁÓW** — `YEAR` samotny między `=` a `;` to operand, `USING SHA256` / `AT LOCAL` /
  `UNBOUNDED PRECEDING` to frazy. ⚠ Trzecie rozróżnienie, też merytoryczne: **zmienna kontekstowa
  (`ROW_COUNT`, `SQLCODE`, `USER`, `INSERTING`) JEST kompletnym wyrażeniem**, więc milczy bezwarunkowo;
  sklejenie zbiorów czyni jeden z tych przypadków błędnym.
  ⭐ **Druga połowa defektu nigdy nie trafiła do zgłoszenia (gotcha #325): spacer ZAPYTANIOWY nie miał żadnej
  bramki gramatycznej** — jego objaw jest cichszy (ciche BŁĘDNE wiązanie słowa Firebirda z kolumną o tej
  nazwie: zły kolor, złe Quick Info, złe find-references; ET0005 dopiero przy dwóch tabelach). ⛔ Tam **nie
  wolno** użyć reguły słownikowej — `SELECT MONTH FROM SALES` musi dalej wiązać kolumnę — więc dostał wyłącznie
  regułę pozycyjną.
  🐞 **Znalezisko strukturalne spoza zgłoszenia (gotcha #326):** `ParsePsqlUnit` rozgałęział się po PIERWSZYM
  tokenie, więc instrukcja z PREFIKSEM (etykieta pętli `retry: while …`, `IN AUTONOMOUS TRANSACTION DO`)
  spadała do liścia kończącego się na **pierwszym średniku** → liść zawierał `=` → klasyfikacja jako
  przypisanie → etykieta zgłaszana jako nieznana zmienna, **a ciało pętli w ogóle niezamodelowane**. Kształt
  #301 o konstrukcję dalej. ⚠⚠ **Naprawa samego parsera zostawia squiggle** — po niej etykieta jest w tokenach
  węzła, a `BindControlHeader` chodzi po nich z włączonym zgłaszaniem; dlatego
  `FirebirdGrammar.StatementPrefixLength` jest **wspólne**: parser go używa, żeby DOJŚĆ do instrukcji, binder —
  żeby nie zaczynać nagłówka od etykiety.
  ⭐ **P6 — ograniczenie debuggera zniesione, i to bez nowego mechanizmu (gotcha #327).** Odmowa D10 („a FOR
  SELECT cursor that references NEW/OLD is not supported") stała na **prawdziwej przesłance i niewynikającym
  wniosku**: to prawda, że syntetyczne `ET_CTX_i` nie istnieją w osobno otwartym kursorze DSQL — ale kursor
  ich nigdy nie potrzebował, bo **`NEW.ID` w zapytaniu kursora jest WARTOŚCIĄ, a ramka już ją ma**. Referencja
  jest więc przepisywana na pozycyjne `?` i wiązana z ramki, dokładnie jak `:zmienna` od D6.
  `ContextSubstitution.ReferencesIn` wyciągnięte i upublicznione (**jedna** definicja parowania
  `RecordAlias`+`Column`, dwa konsumenty). ⚠ **Wiązanie przy OPEN to jest to, co robi sam Firebird** —
  skompilowany wyzwalacz oblicza parametry kursora raz, więc przypisanie `NEW.col` w trakcie pętli nie zmienia
  wierszy otwartego kursora; odczyt przy każdym FETCH byłby wyborem NIEwiernym. Lab +`TRIG_CURSOR_LAB` /
  `TR_CURSOR_BU`, probe **przypadek 40** sim `'L=2/3'` == real `'L=2/3'` + asercja, że przypadek jest
  **rozróżniający** (`L=0/0` to wynik kursora z `NULL`-owym `NEW.ID` — wyglądałby sensownie i przeszedłby samo
  porównanie). ⛔ Martwy `QueryReferencesContext` usunięty.
  ⭐ **P3 — edytor daty: przyczyna ZMIERZONA, nie zgadnięta.** `CalendarDatePicker` z Fluenta ma własne
  `MinHeight` **32**, jego zawartość prosi o **24**, a wiersz `data-edit` (stałe `Height="32"`, #322) po
  paddingu `6 2` zostawia **28**. Poprawką jest **ROLA** (`Size.Control`, ta sama co `TextBox` obok), nie
  dobrana liczba. Druga połowa, pozioma: lokalne `MinWidth = 120` usunięte — siatka danych **pamięta
  szerokości kolumn**, więc zwężona kolumna wraca po restarcie i edytor nie mieści się w komórce; **rozmiar
  nadaje pojemnik** (decyzja 2 z M2b).
  ⭐ **P5 — jeden właściciel prezentacji daty:** `EmberTern.Core.Formatting.DateTimeDisplay`. Nie „na
  przyszłość" — użytkownik zapowiedział wybór formatu w Settings Center, a preferencja potrzebuje jednego
  miejsca, w którym zadziała. ⚠ Jeden świadomy wyjątek: **`LogTime` zostaje 24-godzinny i stałej szerokości**
  (kolumny logu czyta się w dół i porównuje). ⛔ Strona maszynowa nietknięta i **przypilnowana**:
  `DatePresentationTests` trzyma listę plików wolno formatujących invariantnie **wraz z powodem** — wartością
  listy nie są nazwy, tylko to, że dopisanie się do niej zmusza autora do zadeklarowania strony granicy.
  Strażnik zweryfikowany podsadzeniem naruszenia.
  ⏸ **Otwarte, każde z powodem:** wybór formatu daty w Settings Center (zapowiedziany, nie zbudowany) ·
  etykieta pętli zapisana bez spacji (`retry:while` → lekser daje jeden token `Parameter` `:WHILE`; rozdzielenie
  = przeniesienie gramatyki do tokenizacji za pisownię, której nikt nie używa) · `BindBareReference`
  rozwiązujące gołą nazwę do LOKALNEJ przed kolumną (otwarte od sprintu stabilizacyjnego).
  ⚠ (Zapis historyczny: „M4 wymaga osobnego pozwolenia" — pozwolenie padło 2026-08-08 i M4 trwa.) ⛔ M4 startuje z `product-polish-m4-next-session.md`.

- **🔲🔒 SPRINT SPÓJNOŚCI GRIDÓW DANYCH — ZAMKNIĘTY, ODEBRANY PO QA UŻYTKOWNIKA (2026-08-07).** Mały sprint
  domykający stan produktu **przed M4**, świadomie nie będący M4. Build 0/0; suite **7378** (7250 + 73 + 55,
  +18); smoke czysty. Narracja:
  **[docs/history/25-grid-consistency-sprint.md](docs/history/25-grid-consistency-sprint.md)**. Nowe gotchy
  **#322–#323**.
  ⭐ **Co jest w produkcie:** wysokość edytora w komórce Table Data zgodna z pozostałymi edytowalnymi siatkami,
  oraz **komplet operacji kopiowania (Copy cell / row / row with headers / all with headers) we wszystkich
  pięciu gridach danych** — od pozycji kopiowania w dół menu wszystkich pięciu są identyczne co do pozycji i
  kolejności, a różnią się wyłącznie operacjami specyficznymi dla modułu (grupa edycyjna Table Data na górze;
  Copy as INSERT/UPDATE tylko tam, gdzie za wierszami stoi jedna tabela).
  ⭐⭐ **Najtrwalszy wynik jest metodologiczny i dotyczy strażników — gotcha #322: reguła bezpieczeństwa
  wypowiedziana o KLASIE rzeczy („siatka danych") może być fałszywa o każdym jej rzeczywistym elemencie, a test,
  który jej pilnuje, wygląda rygorystycznie i nie chroni przed niczym.** `EditableGridKind` wstrzymywał rolę
  wysokości Table Data z uzasadnieniem brzmiącym jak pomiar (*„24 px urosłoby każdy wiersz, bo te siatki nie
  mają ComboBoxa"*) — prawdziwym o siatce danych w ogóle i **nigdy niesprawdzonym na tej jednej, której
  dotyczyło**: `TableDetailTabView` przypina jej wierszowi **stałą** `Height="32"` (nie `MinHeight`), więc
  wiersz nie ma jak urosnąć. ⚠ Wzmacnia to fakt, że **istniejący test pilnował złego zachowania i nazywał
  poprawkę „the tempting simplification"**. ⭐ Lekarstwo: **pilnować PRZESŁANKI, nie POLITYKI** — nowy strażnik
  czyta wysokość wiersza z widoku, padding komórki z tego samego widoku i `Size.Control` z `Tokens.axaml`, i
  wymaga, by relacja między nimi się utrzymała. `EditableGridKind` usunięty; jedno `Attach(grid)`, jedna reguła.
  ⭐ **Jeden formater dla wszystkich gridów** — `App/ViewModels/GridCopyText.cs` (czysty, statyczny);
  `MainWindowViewModel.BuildCopyText` tylko deleguje, a **wszystkie 12 istniejących `CopyGridTests` przeszło bez
  edycji ani jednego oczekiwanego ciągu** — to jest dowód bajtowej niezmienności wyjścia gridu SQL.
  ⚠ **Trzy ratyfikowane decyzje (2026-08-07), do nierewidowania:** (a) **„Copy all" w Table Data czyta
  `EditableRows`, nie `DataResult.Rows`** — wiersz dodany/usunięty w sesji istnieje tylko w mirrorze, więc
  kopia wyniku emitowałaby wiersze już niewidoczne i pomijała dodane, cicho i z poprawnie wyglądającym tekstem;
  (b) **pojedyncza KOMÓRKA kopiuje się dosłownie** — spłaszczanie tabulatorów/nowych linii do spacji zostaje
  tylko przy kopiowaniu WIERSZY, gdzie służy strukturze TSV; (c) **brak danych ⇒ schowek nietknięty** —
  `Views/GridClipboard.cs` jest jedynym miejscem zapisu i odmawia, bo przepuszczenie pustego ciągu zniszczyłoby
  zawartość schowka użytkownika.
  ⚠ **Cel to prawoklikniętą KOMÓRKA, nigdy zaznaczenie gridu** (kształt #16/#99 o poziom wyżej).
  ⛔ **Czego sprint NIE ruszył:** wysokości WIERSZY żadnej siatki — rozjazd 34/32/30/22 i **Z‑3** to pytania o
  **gęstość**, przypisane do M4/§13.3; naprawiona wyłącznie wysokość EDYTORA w komórce.
  ⚠ (Zapis historyczny: „M4 wymaga osobnego pozwolenia" — pozwolenie padło 2026-08-08 i M4 trwa.) ⛔ M4 startuje z
  `product-polish-m4-next-session.md`.

- **⬆🔒 AVALONIA 12.0.3 → 12.1.1 — SPRINT ZAMKNIĘTY, ODEBRANY PO QA UŻYTKOWNIKA I SCALONY (2026-08-05).**
  Osobny sprint techniczny **przed M4**, świadomie nie mieszany z Product Polish. QA wzrokowe: **brak regresji
  w aplikacji i w edytorze**. Scalony `--no-ff` do **`feat/product-polish`** (`42a6b98`) i wypchnięty na **oba**
  remote'y; **`master` nietknięty**. Gałąź `chore/avalonia-12.1.1` wycofana lokalnie — ⭐ na remote'ach jej
  **nigdy nie było**, bo nie pushowałem przed odbiorem, więc nie było czego usuwać (sprawdzone
  `git ls-remote`). Weryfikacja **po scaleniu**: build 0/0 Debug + Release, **7232 + 73 + 55 = 7360**, smoke
  Release z zerem `FATAL`. Siedem commitów sprintu zostaje osobno, żeby `git bisect` trafiał w pojedynczą
  zmianę wersji.
  Jeden dokument: **[docs/design/avalonia-12.1.1-update.md](docs/design/avalonia-12.1.1-update.md)** —
  ratyfikowane decyzje D‑1…D‑5 (§0), ryzyka **R1–R8** (§3), wpływ na nasze komponenty (§4), checklista QA
  (§6), dziennik wykonania (§7), wynik QA (§8), znaleziska do decyzji (§9).
  **Wersje: core `Avalonia`/`Desktop`/`Themes.Fluent`/`Fonts.Inter` + `Avalonia.Headless` = 12.1.1 · ⚠ dwa
  celowe rozjazdy, każdy z powodem zapisanym przy `PackageReference`:** `Avalonia.AvaloniaEdit` **12.0.0**
  (buildu 12.1 nie ma) i `Avalonia.Controls.DataGrid` **12.1.2** (DataGrida 12.1.1 nie ma — pakiet ma własne
  repo i własny cykl).
  **QA maszynowe:** build **0/0 w Debug i Release** · suite **7360** zielona **dziewięć razy** (3 partycje
  × 3 przebiegi, każdy `--blame-hang`) · `MetadataTreeVirtualizationProbe` 4/4 (93–823 ms przy limitach 5 s)
  · `SharedContextMenuFeasibilityProbe` 2/2 · żywe FB5: `DebuggerFidelityProbe` **39/39**,
  `ChangeSafetyProbe` ALL PASS, `DataImportRunProbe` **33 sprawdzenia** ALL PASS · smoke Debug + Release, **0
  wpisów `FATAL`**.
  ⭐⭐ **Najmocniejszy wynik: 18/18 renderów obu sond wizualnych jest BAJTOWO IDENTYCZNYCH** z zapisanymi na
  12.0.3 (SHA‑256 plik po pliku, oba motywy) — zero ruchu w geometrii, układzie i rozwiązywaniu pędzli.
  ⚠ Identyczność zweryfikowana **zanim** została uznana za wynik (`Avalonia.Base.dll` w `bin/Release` sondy
  raportuje 12.1.1.0), bo „identyczne rendery" i „sonda się nie przebudowała" wyglądają tak samo.
  ⚠⚠ **ZAKRES TEGO WYNIKU:** `RenderTargetBitmap` idzie ścieżką natychmiastowej rasteryzacji Skia, **nie przez
  kompozytor GPU** — czyli tam, gdzie *nie* żyją zmienione w 12.1.0 domyślne (`dirty-rect clipping`,
  `stencil buffers`). Dowodzi, że nie ruszyła się geometria; **nie dowodzi, że żywe okno maluje się tak samo.**
  ⛔⛔ **RYZYKO R1 JEST NIETKNIĘTE PRZEZ CAŁE QA MASZYNOWE I TO ONO DECYDUJE O ODBIORZE: AvaloniaEdit nie ma
  buildu pod 12.1**, jego zakres `>= 12.0.0` spełnia core 12.1.1 **bez ostrzeżenia, bez `NU1605`, bez
  informacji o downgrade** — a 7360 asercji przeszłoby również wtedy, gdyby układ tekstu w edytorze przesunął
  się o wiersz (testy asertują właściwości i pędzle, nie piksele). Na `TextView`/`GetRectsForSegment` wisi
  sześć `IBackgroundRenderer`ów, hit-testowany `BreakpointMargin`, `InlineValuesRenderer` (rysuje **za** końcem
  linii), cztery karty `OverlayLayer`, `SqlIndentationStrategy` i 11 preview DDL. **Punkt 1 checklisty
  wzrokowej = edytor SQL, priorytet bezwzględny.** Nowa gotcha **#321** generalizuje to poza ten pakiet:
  *brak błędu restore jest dowodem o metadanych, nigdy o zgodności.*
  ⚠ **Kryterium wycofania (ratyfikowane D‑5): cokolwiek z R1 → cofnij do 12.0.5, nie do 12.0.3** — 12.0.5 daje
  `TextRunCache`, Unicode v17, `ScrollViewer` NaN fix i headless `TestContext` fix przy **zerowej** zmianie
  domyślnych renderowania, zerowym ruchu w compiled bindings i zerowej zmianie hit-testingu.
  ⚠ **Czego QA NIE dowiodło, powiedziane wprost:** brak zawieszenia suite w 9 przebiegach po zmianie (i w 9
  przed) **nie jest** dowodem, że 12.1.1's `Fix headless session hang when cleanup throws` (#21781) naprawiło
  nasz wieloletni objaw (#94/#226/#261) — od 2026‑08‑04 mamy **dwie** niezależne kandydatki na tę przyczynę
  (`AutoScrollToSelectedItem` i teraz #21781), a objaw jest rzadki z definicji. Obserwować, notować w obie
  strony, nie przypisywać.
  ⏸ **Dwa znaleziska do decyzji, żadne nie blokuje odbioru (§9):** (a) 🐞 **`DataImportProbe` NIE KOMPILUJE
  SIĘ — i jest to defekt WCZEŚNIEJSZY, dowiedziony a nie założony**: identyczny błąd (`TransactionService` →
  `ImportSessionConnection`, skutek I7.5) na commicie `8d5c510` zbudowanym w osobnym `git worktree`, a sonda
  nie referencuje **ani jednego** pakietu Avalonii. ⭐ Jest **poza solucją**, więc `dotnet build EmberTern.slnx`
  nigdy jej nie kompilował — 20 sprawdzeń modułu zamkniętego jako „user-accepted" zgniło cicho przy zielonym
  buildzie; ⛔ nie naprawione, bo Data Import ma stojącą dyrektywę „wracać tylko po rzeczywisty defekt
  funkcjonalny", a sprint ma zostać przypisywalny. (b) ⚠ **numery gotchy 303 i 304 są użyte po dwa razy**, więc
  odwołania „gotchas #303/#304" są niejednoznaczne; przy okazji sprostowano **dwa** liczniki wpisów w CLAUDE.md,
  które nie zgadzały się ze sobą i **oba** były błędne — zmierzone **308 wpisów, #1–#321** (#284 o warstwę wyżej).
  ⚠ (Zapis historyczny: „M4 wymaga osobnego pozwolenia" — pozwolenie padło 2026-08-08 i M4 trwa.) ⛔ M4 startuje z
  `product-polish-m4-next-session.md`; ten sprint niczego w Product Polish nie zmienił.

- **🔧🔒 SPRINT STABILIZACYJNY (S-1 … S-6) — CLOSED, USER-QA'D AND ACCEPTED 2026-08-05.** All six confirmed in
  the running app (S-1a/S-1b · S-2 no flicker and correct after a metadata refresh · S-3 · S-4 · S-5 · S-6),
  **no regressions found**. Branch `feat/stabilization-sprint` (off `feat/product-polish`), pushed to both
  remotes. ⏸ **M4 Product Polish still needs its own explicit go-ahead — and the user deferred it to the next
  session, to be started from `product-polish-m4-next-session.md`.**
  ⭐ The user's own closing note, worth keeping because it is a directive about method, not praise: *„kilka
  zgłoszeń okazało się prowadzić do głębszych przyczyn niż same objawy — dzięki temu udało się naprawić
  fundamenty, a nie tylko zamaskować problemy."* ⛔ So on the next report of this kind: **find the cause before
  fixing the symptom, even when the symptom points somewhere plausible** — twice in this sprint the plausible
  place was the wrong one.
  Narrative: **[docs/history/24-stabilization-sprint.md](docs/history/24-stabilization-sprint.md)**.
  Six defects from ordinary use, closed in etaps E0–E6, one commit each; build 0/0, suite **7360**
  (7232 + 74 + 54), smoke clean. New gotchas **#316–#320**.
  ⭐⭐ **THE SPRINT'S DURABLE RESULT IS METHODOLOGICAL, AND IT IS THE §13.3 GATE'S LESSON FROM THE OTHER SIDE:
  TWO of the six reports were not what they described.** The gate taught that *an impression from a screenshot
  is a hypothesis*; this sprint adds that **a precisely reproducible report can be a real CORRELATION with the
  wrong VARIABLE**. Operationally: *a report says WHERE the user saw it, not WHAT is broken* — and in both
  cases reading the code would have **confirmed** the wrong hypothesis, because it sends you where the symptom
  points. Only measurement separated them.
  ⭐ **THREE SHARED CAUSES, so it was not six independent fixes** (the user asked for exactly this assessment):
  **S-1a + S-3** — the set of editable definition grids was IMPLICIT (*whoever calls `FieldGridColumns.Build`*,
  which is where the height-role class lived), so the three grids that declare columns in XAML silently missed
  it · **S-2** — both halves are one fact: the snapshot could not say "not yet", AND nothing invalidated the
  caches on refresh; ⚠ the second **cannot** be fixed without the first, or every refresh becomes the same
  false-positive storm · **S-1b + S-6** — the same SHAPE, not the same code: a layer discarding information it
  had, fixed at the PRODUCER both times.
  🐞 **S-1b was the serious one: a rule #11 data-loss path, worse than reported.** "Changing a parameter's
  domain does not save" was really *the domain died on READ* — `RDB$FIELD_SOURCE` was never selected, so
  opening a procedure to edit its BODY and pressing Compile rewrote every domain-typed parameter as its base
  type, destroying the domain link in the database. Gotcha #175's shape, one object kind further along.
  ⭐ Demonstrated live, not deduced: with the pre-fix decision planted, the probe reports
  `the CATALOG still records D_CODE after the recompile — RDB$3`. ⚠⚠ **And byte-identity of the reconstruction
  PASSED under that plant** — both reads were wrong the same way, so a round-trip assertion is necessary and
  insufficient; the catalog is what must be asked. ⭐ Nullability follows the TYPE source (measured: a domain's
  own `NOT NULL` lives on the domain's flag, an explicit one on the parameter's), so a `COALESCE` in SQL made
  that decision unrepresentable. ⚠ The debugger needs the OPPOSITE answer (base type, R2) and still gets it —
  the two readers share only the "is this a user domain" predicate; `DebuggerFidelityProbe` 39/39.
  ⚠ **This changes the visible DDL text** for every routine with domain-typed parameters (preview, `.sql`
  export, Source mode). In QA it looks like a behaviour change; it is a return to the truth.
  ⭐ **S-6 had no colon bug at all.** Measured: `:a` is ONE `Parameter` token and resolves through the same
  `scope.Resolve` as a bare name, with Quick Info answering on every offset including the colon. What had no
  binding was a colon-form reference **inside a query clause** (the query binder's walk had no
  `TokenKind.Parameter` branch) plus the `INTO` targets of a singleton `SELECT` (the `SelectQuery` NODE span
  swallows them while no CLAUSE covers them → skip the clauses, not the node). The colon form is simply where
  an embedded `SELECT` puts a local.
  ⚠⚠ **AND THAT FIX CHANGED THE DEBUGGER AS A SIDE EFFECT — the most transferable warning here.** Its
  read/write set falls back to "inject every in-scope local" **precisely when the analyzer returns nothing**
  (#238), so restoring the references NARROWED the injection. ⭐⭐ Its own 38-case fidelity probe said nothing
  about it, because **not one of the 22 routines it drives contains a singleton `SELECT … INTO`** — *a
  measurement can reproduce a MECHANISM without reproducing the STATE.* The state was added to the lab
  (`SP_DBG_SELINTO`) and the probe grew case 39, with an assertion that the case is **discriminating**.
  ⭐ **S-2: `ISqlMetadataProvider.KnowsColumns`** (default `true`, so a provider must opt IN to admitting
  ignorance — a default of `false` would silence real ET0002 everywhere) + parameters loaded BEFORE the body in
  the routine editors + `SchemaInvalidated` raised BEFORE the reload. ⚠ CTE is exempt: its columns come from
  its own projection in the text.
  ⭐ **S-1a/S-3: `Behaviors/EditableGridBehavior`** carries the Enter gesture and the height role; nine explicit
  `Attach` calls, no automatic path to forget. Measured framework facts: `DataGrid` claims Enter itself, a
  **TUNNEL** handler is required (at bubble it is already handled), there is **no public "am I editing"** so
  the gate is FOCUS, and `DataGridCell` has no public `Column` (locate the cell by `SelectedItem` +
  `DisplayIndex`). ⚠ The data grid gets Enter but NOT the height role (M2b step 7's measured row growth).
  ⚠⚠ The guard **cannot** key on `IsReadOnly="False"` — Table Detail's fields grid binds it, so a scan by that
  attribute misses the very grid that was reported (#285).
  ⛔ **S-4: the import module has no progress bar of its own** — this REVERSES §19.33's "the status bar
  complements, never replaces", on the user's call after living with two bars on screen. The progress TEXT and
  the elapsed timer stay. ⛔ **S-5: `ClientLibraryPath` is removed** — see the driver-gotcha section; it could
  never have an effect, and the "Advanced" expander went with it because that field was its only content.
  ⏸ **Left open, each with a reason** (full list in the history file): the `BindBareReference` ordering (a bare
  name in a query resolves to a LOCAL before a column; Firebird prefers the column — worth its own
  measurement) · the row-HEIGHT divergence across definition grids (34/32/30/22 — a **density** question, so
  M4/§13.3 by the user's decision; only the in-cell EDITOR height was fixed) · ET0001 during a partial catalog
  load (same shape as #317 if it ever surfaces) · the import command bar's three 170/180 px combos (density).
  ⚠ **Two one-off reds, not reproduced and claimed neither fixed nor unrelated:**
  `DataImportNewTableTests.ANewTable_NeverCarriesEmptyTheTableFirst…` (once, during E3) and
  `SettingsLoadHealthTests.ConcurrentSaves_NeverLeaveSettingsUnreadable` (once, during E4 — a `Parallel.For`
  test). Each passed alone and in two subsequent full runs; no mechanism links them to those etaps.

- **🎨 PRODUCT POLISH — ACTIVE STAGE. Branch `feat/product-polish`. M3 · M3b · ⛔ the §13.3 GATE · M3.5 are all
  CLOSED. ⏸ NEXT IS M4, and it needs the user's explicit go-ahead — do not start it.**
  ✅ **⛔ BRAMA §13.3 PRZESZŁA (2026-08-04) — zapis: `product-polish.md` §13.3a.** Reviewed on the user's own
  16 screenshots (8 states × 2 themes, maximized, PNG, `SZKOLENIE_SQL` 2218 tabel / 1075 procedur). Verdict per
  question: the **status bar, tab strip and metadata tree read as one designed frame**; the **toolbar did not**,
  and the reason turned out to be **optical glyph size, not colour**. Six findings, three taken into **M3.5**
  (Z‑1/Z‑2/Z‑6), three deferred with reasons (Z‑3/Z‑4/Z‑5), one folded into `color-language.md` P‑1.
  ⭐⭐ **THE GATE'S MOST DURABLE RESULT IS METHODOLOGICAL: of seven suspicions SIX fell to measurement, and two
  of those were errors of the measurement itself** — a pixel scan missed the 2 px active-tab indicator by one
  row (it *is* there: `#2D6BBF` at y=111–113), and screen capture lost the status bar entirely (the user
  confirmed it renders correctly). ⚠ **An impression from looking at a screenshot is a HYPOTHESIS, not a
  finding** — in this gate it was wrong more often than right.
  ⛔⛔ **„TĘCZA IKON" WYCOFANA I ZAMKNIĘTA — nie wolno jej „naprawić" drugi raz** (§13.3a.3). The gate reported
  nine differently-coloured create icons beside six uniformly blue tools as incoherent; **the user rejected it
  and was right**: those colours are S1 (kind identity) and match the metadata tree, so the user learns a kind
  once and recognises it everywhere. ⭐ That was **trap 17 committed by the gate itself** — seeing a system and
  following the observation to its logical conclusion instead of asking whether it WORKS.
  ✅🔒 **M3.5 DONE, USER-QA-CONFIRMED AND CLOSED (2026-08-04) — three defects from the gate**
  (`product-polish.md` §19.36). Build 0/0; suite **7317** (7196 + 67 + 54, +7); smoke clean.
  **All four new guards verified by planting the violation.** Pushed to both remotes (`cb76c0b`).
  **QA verdict (both themes):** a disabled `Button.icon` no longer pretends to be active · unchecked
  `CheckBox`es are finally visible **without dominating** · the create icons are markedly more legible.
  ⭐ On the architecture the user was explicit: **`CreateIcon` is a better answer than maintaining nine
  separate `*Plus` variants**, and the accent badge *„dobrze odcina się od glifu, nie konkuruje z kolorem
  rodzaju obiektu i czyta się bardzo naturalnie jako akcja «utwórz»"*. ⛔ Do not revisit either decision.
  **Z‑1 — a disabled `Button.icon` no longer paints a chip.** Mechanism measured, not inferred: Fluent's
  `:disabled` paints `/template/ ContentPresenter` from `ButtonBackgroundDisabled` (Bridge → `PanelColor`),
  and `Button.icon`'s transparent setters sit **on the control**, so they lose; `Opacity 0.4` merely *dimmed*
  the chip (0,4 × `#252526` + 0,6 × `#2D2D2D` = `#2A2A2A`, the measured value). Visible on four chrome
  surfaces; **169 `Classes="icon"` in 15 files** is the structural reach. ⛔ **Deliberately NOT fixed in the
  Bridge** — those keys serve `Button.flat`/`Button.primary`, which are *supposed* to look like buttons when
  disabled. ⭐⭐ **Second half, and it is why this was a reception defect:** in the results strip the four
  *disabled* buttons were the only bordered elements while the three *enabled* ones were bare, and in Table
  Data the same chip meant **both** „unavailable" and „engaged" (`ToggleButton.icon:checked`) — fixing one
  restored the other for free. ⚠ Corrects an earlier diagnosis: `docs/history/23` blamed `Opacity` alone.
  **Z‑2 — the interactive-control outline gets its own role.** New token **`ControlOutlineBrush`**
  (`#6A6A70` / `#90939A`), consumers `CheckBox` + `RadioButton`. On `BorderBrush` an unchecked box measured
  **1,60:1 (Dark) / 1,35:1 (Light)**, i.e. the control you must click was invisible in its **default** state.
  ⭐ Value computed **at the threshold** (~3,1:1), the road gotcha **#308** ratified; the
  `SubtleForegroundBrush` variant (6,31:1) was rendered and **rejected** as heavier than its own label.
  ⛔ Never alias it to `BorderBrush` or `SubtleForegroundColor` (K1).
  **Z‑6 — `CreateIcon`: the glyph is the KIND, the badge is the ACTION.** All nine `*Plus` geometries carried
  the identical plus segment in the whole lower-right quadrant while the glyph was squeezed to **11–12 of 24
  units** where its own counterpart in the tree has **18** — ~62 % linear, ~40 % area, in an **identical box**
  (gotcha #288 inverted: the ink box started dictating optical size). Replaced by a composite on the proven
  `DebuggerIcon` pattern: full-size plain glyph (`IconColor_*`, **by reference**) + a solid `AccentBrush`
  disc Ø10 inset 0,5 with an `OnAccentBrush` plus. ⭐⭐ **Nine hand-maintained copies are GONE** — the toolbar
  and the tree now share one geometry per kind, so improving a glyph reaches both; this was nine unfixed
  instances of the very defect `DebuggerIcon` documents for itself. ⚠ **`AccentBrush` here does NOT re-open
  P‑2**: a solid 10-unit disc with a white plus works by area and internal contrast, not by a 2 px difference
  against the surface.
  ⚠⚠ **THE ROUTE MATTERS AS MUCH AS THE RESULT — two dead ends closed by measurement, so nobody re-walks
  them.** (1) *Plus inside the glyph* (pure geometry, `Icon.FolderPlus`'s own model) won decisively on the
  glyph and **lost the badge**: 5 of 9 worked, and in `View`/`Trigger`/`Generator` the plus merged with the
  outline. ⚠ My design error there is worth keeping: I measured clearance against **path centrelines, not the
  stroked outline** — with a 2-unit stroke a „1-unit gap" is touching outlines. (2) ⭐⭐ *Full glyph + big
  corner plus is ARITHMETICALLY IMPOSSIBLE* in 24 units: the maximum non-overlapping split is glyph ~13 +
  plus 6, i.e. **+18 %** over the old 11. **So today's 11+7 was near the optimum for „no overlap"** — very
  likely the wall the user's earlier attempt hit. (3) The badge route is **structurally** required, not a
  taste call: `SvgIcon` is one `Path`, one `Stroke`, **one `StrokeThickness=2` for the whole geometry**, and a
  badge is by definition *smaller and denser* — from there you only get „smaller but equally thick", which at
  16 px is a blob.
  ⭐ **New tool: `tools/probes/VisualCandidateProbe`** — renders candidates beside the current state, both
  themes. It exists because §0.5 demands an answer about *reception* and „don't know" is a refusal; without it
  the only answer is a guess. ⚠ Candidates live **in the probe, not the product**, and a separate
  `z6-SHIPPED-*` render uses the **real control + resources by key**, because „the variant looks good" and
  „this is what shipped" are different assertions.
  ⏸⏸ **NEXT IS M4 — direction RATIFIED 2026-08-04, start needs a separate explicit go-ahead** (full text +
  reasoning: [product-polish-m4-next-session.md](docs/design/product-polish-m4-next-session.md) §5):
  **D‑M4‑1** M4 opens with the **collision register K1–K15**, not with screen migration — *„najpierw zamknąć
  decyzje projektowe o charakterze globalnym"*, so no surface is touched twice (**R7 applied to ORDER**, and
  §13.0.1's Z‑6: migrating onto an unaccepted frame is migrating to a fix) · **D‑M4‑2** **`Size.Icon` (64
  literals) and K15 (112 occurrences / 17 files) are ONE design question about visual density** — ⛔ never
  settled separately, because two independent iterations would change density twice without ever looking at
  the whole (R17, exactly as K12–K14 and K15 went to the gate as single questions) · **D‑M4‑3** none of the
  deferred topics comes **before** M4. ⚠⚠ **That is not „do not touch"** — it forbids *pulling them forward*,
  not meeting them: *„jeżeli podczas M4 okaże się, że któryś z nich naturalnie wiąże się z wykonywaną pracą,
  wtedy podejmiemy decyzję w kontekście konkretnej zmiany, a nie z góry"* — so stop and ask **in the context
  of that change**, rather than deciding up front or avoiding the topic artificially.
  ⏸ **Open from the gate, each with a home:** **Z‑3** (Table Data row 40 px vs the catalog's 22 and the
  sibling grid's 27) — ⛔ **find the CAUSE first; the user ratified that a taller row may be a deliberate
  readability decision and then it stays** · **Z‑4** (Settings window clips a row mid-description) · **Z‑5**
  (the Execute dialog's date editor looks disabled and breaks the row rhythm) · ⏸⏸ **Settings Center as a UX
  surface** (§13.3a.5 — icons per category, a distinct nav-pane surface, a more „product" left nav; ⭐ measured
  delta: **the nav pane and content have IDENTICAL backgrounds today**, so the largest part is giving the pane
  `PanelBrush`). ⛔ None of these is M4 and none is a defect blocking it.
  ⚠ **Execute dialog semantics confirmed intended by the user:** a ticked NULL passes `NULL` and the Value
  field is ignored. Not a functional defect; whether the field should *look* excluded belongs to the later
  dialog polish.
  **Historical pointer for M3/M3b detail:**
  [docs/design/product-polish-m3-next-session.md](docs/design/product-polish-m3-next-session.md) and the
  handover it points at — ⚠ both now describe CLOSED work; read them for the why, not for what to do next.
  🔒 **M3.3 (TAB STRIP) IS CLOSED AND ACCEPTED (2026-08-03)** — `product-polish.md` **§19.25** is its closing
  summary. Three sub-etaps: **M3.3a** paid off the strip's technical debt (12 → 5 local values), **M3.3b**
  delivered **two modes + two preferences + a Tabs category** in Settings Center, **M3.3c** added the
  **context menu** and took the rule-#11 gate from three entries to four. ⛔ Do not return to the tab strip
  without a real functional defect.
  ⚠⚠ **THREE ITEMS ADDED TO M3.4's CHECKLIST BY THE USER (2026-08-03), before implementation** — full
  record in the handover **§3.7a**, and they are a checklist to walk *while* working on the Metadata
  Explorer, **not** a new feature. (a) 🐞 **A rare tree hang**: expanding a **large** category makes the
  tree **scroll down on its own**, then the app freezes and closes — seen **2–3 times in the whole life of
  EmberTern**, so it predates Product Polish. ⭐⭐ **A measured mechanism candidate already exists, found by
  reading during the M3.3 close-out:** `SidebarFlatController.OnExpandedChanged` inserts children **one at a
  time** (`Rows.Insert` in a loop) and the bulk guard **does not cover that path — it SKIPS it**
  (`if (_suspendDepth > 0) return;`). So an expand *from code* runs under the guard (what Layer 1 fixed),
  while an expand **by click** on an already-loaded category does **N individual inserts** into an
  `ObservableCollection` bound to a virtualizing `ListBox`. ⚠⚠ **Scale, stated precisely because the first
  draft of this note overstated it:** this is **not** Layer 1's Θ(N²) — it is **Θ(N) notifications** plus
  **Θ(N × tail)** element shifts in the backing `List<T>`, i.e. far cheaper than the fixed defect but far
  dearer than the guarded single `Rebuild`. ⭐ **Nobody has measured this path** — Layer 1 measured
  *refresh*, not *click-expand*. ⛔ Measure before "fixing": the cost may turn out negligible and the real
  cause lie elsewhere (scroll anchoring, or the `Dispatcher.Post` in `OnIsExpandedChanged`). (b) ⚠ The user asks whether this **shares a mechanism with the long-standing flaky
  test** — and explicitly says *not* to assume it does. **For:** the class is `ConnectionExpandBindingProbe`
  and its `AutoExpandOnConnect_ReflectedInFlatList` exercises exactly that path. **Against:** it was measured
  (Keyboard Manager etap 5) that the full-suite hang reports the last headless test **positionally**, with
  teardown as the suspect. ⭐ Two different observations; the decisive experiment is cheap — if the mechanism
  is the incremental splice, forcing a large-category expand in a headless test should reproduce the hang
  **deterministically**, which would turn a "flaky test" into a **regression test for a real defect**.
  Record the outcome either way. (c) ⚠ A **short performance review** of category expansion — ⛔ nothing
  forced; finding nothing is a valid result, and this is *not* `metadata-refresh-analysis.md`'s Layer 2/3.
  ⭐⭐ **Four findings from M3.3 that outlive it:** (1) **moving a rule changes its PRIORITY** — the same style
  in `Border.Styles` and in the global sheet behaves differently against a local value, so *"I moved the style
  unchanged"* is a sentence you may not say without measuring; (2) **a tool that computes ONCE cannot rule on
  CONVERGENCE** — the visual probe renders one layout pass, so a feedback-loop defect is outside its reach by
  construction, not by mistake; (3) **a stage plan goes stale exactly as silently as a string or a comment** —
  check in the code that a sub-etap's subject still exists before starting it; (4) **a test on a property's
  VALUE is not a test that the screen works** — the binding re-queries only on `PropertyChanged`, so the
  notification must be an assertion, verified by planting the violation (R16).
  Build 0/0; suite **7245** (7134 + 57 + 54); smoke clean.
  ✅ **M3.4a DONE (2026-08-04) — the Metadata Explorer tree row; the CATALOG followed the PRODUCT**
  (`product-polish.md` §19.26). `Size.Row.Tree` **20 → 24** (ratified decision **DB**), `MinHeight` moved
  onto the role — **the token gained its first consumer, having had zero** — the two chevron glyphs onto
  `Size.Icon.Sm`, and the remaining local values got a reason in place. Zero visual change, +2 guards.
  ⭐⭐ **The iteration's most important result is NEGATIVE, and that is the point: the measurement REFUTED
  the hang hypothesis this file recorded above.** New probe case **B4** (`MetadataPerfProbe`, out of
  solution) drives the real `SidebarFlatController`: a click-expand of **2 400 leaves with a 6 000-row
  tail costs 2,3 ms** (collapse 2,7 ms; 5 000 + 6 000 → 4,8 / 7,4 ms). The predicted **shape** held —
  Θ(N) notifications, Θ(N × tail) shifts, visible in the tail column — but the constant is small enough to
  fit in one frame, against **916,9 ms** for the defect Layer 1 fixed on the same 2 400 leaves. ⛔ **No
  guard was added there**: 2 ms does not justify changing a working mechanism, and §3.7a(c) explicitly
  allows "found nothing" as a result.
  ⚠⚠ **THE MEASUREMENT'S SCOPE, STATED BECAUSE WITHOUT IT THE NUMBER MISLEADS: the probe measures the
  MODEL, not the PANEL.** Those 2 400 `CollectionChanged` notifications reach a **virtualizing `ListBox`**
  in the real app and that half is **unmeasured** — while the reported symptom (*the tree scrolls down on
  its own*) is a **panel** behaviour, not a collection one. So the measurement **moved the boundary of
  ignorance; it did not close the question**. ⭐ Consequently the hypothesis in (b) **weakens but does not
  fall**, and the two observations stay **unjoined**; the decisive headless experiment is now its own step
  (**15b**, user's call — do not mix it with catalog housekeeping). ⭐ The live-app instrument already
  exists and needed no work: `App/Diagnostics/ScrollTrace.cs` (`EMBERTERN_SCROLL_DIAG=1`) distinguishes
  *VSP re-estimating the extent* from *we rebuilt the tree*.
  ⛔⛔ **A tempting move was refused and is worth knowing: "move the style into `ControlStyles.axaml` so it
  can be tested".** The sidebar row style lives in a local `<ListBox.Styles>` block, so only `MainWindow`
  sees it — and a headless test constructing `MainWindow` hangs the suite. Moving it is **exactly what
  re-created the §19.2 regression in M3.3a** (*moving a rule changes its PRIORITY*) and the narrowing is
  deliberate, so the Saved-Queries list is untouched. ⭐ **We do not move the product to fit the tool** —
  both guards read the SOURCE instead, and both were verified by planting the violation.
  ⚠ **K15 joins the collision register** — the node icon (15 vs `Size.Icon` 14) **and** the icon↔label gap
  (`Spacing` 5 vs `Space.Xs` 4), as **ONE question about tree density** for §13.3, exactly as K12–K14 went
  as one question about tab-strip density. ⛔ Not fixed here: those two literals have **112 occurrences
  across 17 files**, so changing them in the tree alone would patch one screen (R7) *and* drift the tree
  away from the rest of the app; the sweep belongs with `Size.Icon`'s 64 literals in **M4.3**.
  ⛔⛔ **STANDING USER DIRECTIVE FOR ALL OF M3.4 (2026-08-04): scroll stability is an acceptance criterion
  alongside correctness and performance, and it outranks a few milliseconds.** Judge every larger Metadata
  Explorer change by it too, and **if you meet a mechanism that could cause a reentrant layout, a
  notification loop, or a fight over the `ScrollViewer`'s position — stop and show the user BEFORE
  implementing.** ⭐ This is §19.23.9 generalised: that defect was a feedback loop, and a tool that computes
  one layout pass could not have caught it *by construction* — and the tree has thousands of rows,
  virtualization and scroll anchoring, i.e. exactly those conditions.
  ✅ **STEP 15b DONE (2026-08-04) — the headless experiment; the hypothesis is refuted a SECOND time, now in
  the layer M3.4a could not reach** (`product-polish.md` §19.27, `metadata-refresh-analysis.md` §9). New
  `MetadataTreeVirtualizationProbe` wires the **real `SidebarFlatController`** into a **real `ListBox` with a
  `VirtualizingStackPanel`** in a 600 px window. Four scenarios on 2 400 leaves + 3 000 siblings: **no hang
  (43–57 ms each, layout included) and the scroll position never moved by itself** — 0→0, 1500→1500 (first
  realized row 50→50) and, in the sharpest case, a **full re-projection at offset 40 000 px leaving both the
  offset and the first realized row unchanged**.
  ⭐⭐ **The experiment's main product is that it SEPARATED TWO VARIABLES that had always occurred together:**
  **A** = constructing `MainWindow` in a headless test (the measured hang-prone shape — `BrandingPresentationTests`
  hung until it stopped doing it, 476 ms on a bare `new Window()`), **B** = the incremental splice into a
  virtualizing list. ⚠⚠ `ConnectionExpandBindingProbe` — the class the user runs alone because it hangs —
  **builds `MainWindow` in several tests**, so both variables sit inside it and neither can be read off it.
  ⛔ That is why the experiment **had to be its own class**; adding it there would have re-glued exactly what
  it separates. **Result: B is exonerated in isolation; A stays the only standing suspect, unproven.**
  ⭐ **So the answer to "does the old tree bug share a mechanism with the flaky test" is NO — and that is a
  result, not the absence of one.** ⛔ The suite hang stays its own infrastructure task; the two observations
  stay unjoined.
  ⭐ **Side finding that confirms M3.4a from the other side: a bulk guard on that path would buy nothing** —
  the incremental splice and a single re-projection are the same order (tens of ms, high run-to-run variance),
  because the panel re-realizes its containers either way.
  ⚠ **A discrepancy recorded and deliberately NOT resolved:** `metadata-refresh-analysis.md` §7 describes the
  Layer-1 trade-off as *"the list scrolls to top"*; the measurement does not reproduce that. Candidate
  explanations (filter re-application, selection, focus, or §7 being a conclusion rather than a measurement)
  are listed in §9.3 — ⛔ no document was "corrected" on a guess, and it belongs to Layer 2, whose subject it is.
  ⚠ **What the experiment does NOT prove:** the row template is simplified and the nodes synthetic, so it
  shows the **mechanism** is stable, not that the **product's** tree is. Uniform row height — the property the
  extent and anchoring depend on — is reproduced faithfully.
  ⭐ **The four tests stay** and become the machine check behind the user's standing request: every larger
  Metadata Explorer change now has a guard that a large expand finishes in bounded time and does not move the
  scroll position. ⚠ Their 5 s bounds are **deliberately generous** — a hang shows up as seconds, and a bound
  tightened to the measured ~50 ms would be a test that fails for reasons unrelated to its subject (R16 applied
  to test construction).
  ✅ **M3.4b PART 1 DONE (2026-08-04) — the sidebar's context menus stop being multiplied by
  virtualization** (`product-polish.md` §19.28). ⭐⭐ **The finding came from the INVENTORY, not the plan**:
  the `MetadataNodeViewModel` row template carried an **inline `ContextMenu` with 22 items**, and that
  template is applied to **every realized row** of the virtualized sidebar. Per the standing request I
  stopped and showed it before implementing.
  **Measured** (`SharedContextMenuFeasibilityProbe`, 5 000 rows, 40 scroll jumps): virtualization does **not**
  fully recycle containers — the template is built **1 640 times per scroll**, so the menu was created and
  discarded 1 640 times. Per-row **1237–2619 ms** vs shared **324–504 ms** → **~74 % of scroll time**, and
  live `MenuItem`s **440 → 22**. ⭐ The *variance* is the second datum: the per-row variant swings 2.1×,
  which is the allocation-pressure signature — and that shows up as **stutter**, not as uniform slowness.
  ⭐ **Feasibility answered before any change: a shared `ContextMenu` needs no binding workaround.** One
  instance attaches to all rows and, on open, adopts the **DataContext of the row it was opened on**
  (`OBJ_3` → `OBJ_7` → `OBJ_1`), so ordinary `{Binding}` resolves correctly and follows. ⚠ The carrier is
  **DataContext inheritance, not `PlacementTarget`** (which read `null` under a programmatic `Open`) — ⛔ do
  not build on `PlacementTarget` here without your own measurement.
  **As built:** three `<ContextMenu>` blocks moved into `<ListBox.Resources>` with `x:Key`, referenced as
  `ContextMenu="{StaticResource …}"`. **No code-behind, no behaviour, no change to any item's bindings.**
  ⭐⭐ **The compiler caught something that is an IMPROVEMENT, not an obstacle:** inside a `DataTemplate` the
  context type came from its `DataType` — implicitly and for free — so in resources ~30 `AVLN2000` errors
  appeared. The answer is **`x:DataType` on each menu**, which makes the binding contract **explicit and
  compile-checked** where it used to be positional. ⚠⚠ With reflection bindings the same defect would have
  been **silence**: an empty menu on right-click and a green build. ⛔ Do not remove `x:DataType`.
  ⚠⚠ **Three guards, each verified by planting the violation — and one of them is the ONLY net.**
  `EverySharedMenuReference_HasItsResource`: a planted bad key **passed the build**, because an unresolved
  `StaticResource` throws only **when a row is realized** — i.e. after connecting and expanding a category.
  ⭐ **Smoke cannot catch it**: an empty sidebar realizes no metadata row at all, so the app starts, looks
  right, and fails later in the user's hands.
  ⏸ **Verification scope, stated plainly:** the folder and connection menus are machine-verified (the
  existing probe realizes those rows); **`SidebarMetadataMenu` and the `IsVisible` re-evaluation are the
  user's QA** — the latter by explicit decision (*"no point building more measuring infrastructure for
  something verifiable in the running app"*).
  ⚠ **The same "inline menu in a row template" shape exists in two more places and was deliberately left
  alone**: Saved Queries and the tab strip — **neither is virtualized nor reaches thousands of rows**, so the
  multiplier that decided here does not exist there (trap 17).
  ✅ **M3.4b PART 2 DONE (2026-08-04) — the review of all 32 context menus, and it found NOTHING to fix**
  (`product-polish.md` §19.30). ⚠ **The entry measurement was wrong and is corrected: 6 items lack an icon,
  not 14** — eight carry one through the ELEMENT syntax `<MenuItem.Icon>`, which a scan for the `Icon=`
  attribute cannot see (#285 again: a measurement by carrier does not answer the question about the role).
  ⭐⭐ **Two apparent inconsistencies both turned out to be rules working correctly.** `DeleteCommand`
  appears in four menus and only one shows `F8` — because `ResolveCommand` resolves `DeleteObject` **only**
  for a metadata leaf, so the gesture genuinely does not work on a folder or a connection and showing it
  would teach something false. `Connect`/`Disconnect` have no `CanExecute` — they are gated by `IsVisible`,
  and ⭐ **hiding and disabling are two correct tools for two different situations**: hide when the item
  makes no sense in that state at all, disable when the operation exists but is momentarily unavailable
  (a vanishing item destroys muscle memory — which is why M3.3c chose `CanExecute`).
  ⚠⚠ **Method note worth keeping: an automated cross-check BY NAME cannot answer the gesture question.**
  Menus bind ViewModel commands (`AddFieldCommand`) while the catalog holds ids (`CollectionAdd`); the
  mapping lives in `ResolveCommand`, not in names. Across 154 items the names coincided **once**, by
  accident. ⛔ Do not build a guard on that association — it would give false comfort.
  ⭐ **M3.4 IS CLOSED IN FULL** (M3.4a §19.26 · step 15b §19.27 · M3.4b part 1 §19.28 · part 2 §19.30).
  ✅ **M3b.1 DONE (2026-08-04) — import and the Script Executor now report to the status-bar progress
  section** (`product-polish.md` §19.31). ⭐⭐ **The entry measurement refuted the stage inventory on three
  points, and one of them halved the scope.** (1) There are **FIVE** `IProgress` paths, not three and not
  four — the missing one is the **Script Executor** (`IProgress<ScriptStatementResult>`), which is also the
  **only path in the app with an exact total** (`_lastStatements.Count`), so it became the first live
  consumer of the percentage path §19.7.2 warned was untested. (2) ⛔⛔ **Export and batch run MODALLY**
  (`ShowDialog(owner)`), so the section's whole value — §19.7.3's *"the operation survives switching tabs"* —
  cannot exist there, and `HasCancel` would render a button **that cannot be clicked** behind a blocked
  window; they are out of scope **permanently**, and their own `ProgressBar`s stay (the status bar
  *complements*, never replaces). (3) ⚠ The **"16 ViewModels" figure is not a list of things to wire** —
  14 are `IsLoading` for "loading this tab's content" and **each already has its own in-place carrier**
  (11 `*LoadingHint` constants); trap 13's question answers itself, because the owner of that fact is *what
  you are looking at*. `PerformancePanelViewModel` was declined separately: `CancellationToken.None`, i.e.
  an operation with no cancel.
  ⭐ **Two operations genuinely CAN run at once** — import owns its own transaction since I7.5 — so the
  arbitration `StatusProgressViewModel`'s comment deferred to M3b had a real subject. **Ratified: one
  operation at a time, on a priority ladder** (connect/metadata › query **and** script › import), with the
  label always naming its operation. ⚠ Query and script are **one rung on purpose**: they contend for the
  Data lane (`RunAsync` refuses over an open transaction), so they cannot meaningfully overlap — ⛔ a rule
  for an unreachable case would be an inert branch posing as a design decision.
  ⭐⭐ **The architecture is one sentence: ONE writer of the section.** Every source now only says
  *"recompute"*; `UpdateProgressSection` alone calls `Begin`/`Report`/`End`. The tab VMs deliberately got
  **no reference** to `StatusProgressViewModel` — two writers would be two owners of one state, and the
  arbitration would have nowhere to live. The aggregation seam needed **widening, not building**, and the
  name followed the responsibility: `WireRailSource` → `WireActivitySource` (`RaiseActivityChanged` has been
  named for "activity" since M3.1e precisely because it feeds consumers with different roles; progress is
  the **third**). ⭐ That one subscription set is also what guarantees **no source outlives its tab** —
  closing a tab mid-import and disconnecting (`Reset`, which carries no `OldItems`) both go the same way.
  ⚠ **`RailBrushKey` was NOT touched** — rail colour semantics are M3b.3, after every source is wired
  (the user's call: *if the current colours turn out to be enough, there is no need to complicate them*).
  ⚠⚠ **THE ITERATION'S MOST IMPORTANT RESULT IS A LESSON ABOUT TESTS, AND IT GENERALISES: the first
  version of the guard PASSED with the violation planted.** Its scenario ended the script *before* starting
  the query, so the section passed through "nothing running" — and `End()` resets the mode **by itself**.
  The test was green for a reason its own name did not describe, and **only planting the violation revealed
  it**; without that step the iteration would have closed with a pin that pinned nothing. ⭐ The correct
  shape is an **owner handover with no gap** (script running, user hits F5) — reachable exactly because the
  ladder puts the query above the script. ⚠ Two more measured notes: the **first plant was too broad**
  (removing `Begin` took `IsRunning` with it, so 7 of 13 tests fell and nothing was isolated — a plant must
  lie in **one** dimension), and one plant **failed to compile** while the tests ran against the **stale
  binary** and showed the *previous* plant's red — ⭐ check `0 errors` **before** reading the failure list.
  ⚠ The label is short **from a measurement, not for taste**: the status bar is
  `ColumnDefinitions="Auto,*,Auto,Auto"`, so section 4 grows at the star column's expense and **pushes the
  state chips left** — which is why §8.4.6 fixed the bar itself at 120 px. ⛔ Do not add the operation's
  detail to it; the detail belongs to the surface running the operation (§19.5.1/§19.7.1's ownership split).
  ✅ **M3b.1 A+B+C DONE (2026-08-04) — selecting a large `.xlsx` for import no longer blocks the UI:
  17 768 ms → 1 ms** (`product-polish.md` §19.32). ⚠⚠ **My first measurement answered a different question and
  the user caught it.** I priced the *provider* (`ListSheetsAsync`, `ReadSchemaAsync`, the shared-string table)
  and the numbers were right, but they did not explain the symptom; the user's reply was exact — *"the problem
  is at the boundary between OpenFileDialog closing and the first preview, not in reading the XLSX"*.
  ⭐⭐ **Cost explains why something is SLOW; it does not explain why the UI is BLOCKED** — the second
  measurement was about the thread, not milliseconds, and that is the one that named the mechanism.
  **The mechanism:** `Recalculate` starts the chain synchronously (`PendingRecalculation =
  RunGuardedChainAsync(...)`) and an `async` method runs inline until its first *incomplete* await — while
  `FileImportSource.OpenStreamAsync`/`OpenTextAsync` return `Task.FromResult(...)`, so every provider await
  continues **inline regardless of `ConfigureAwait`**. So the whole read ran **inside the `Source.FilePath =
  path` setter**, and a Dispatcher job posted at **`Render`** priority beforehand did not run **once** in
  17 768 ms. ⭐ That is what "the window looks frozen and repaints oddly" is: a window that stopped pumping.
  **A —** `RowsFromDimension` fetched one attribute through `worksheetPart.Worksheet`, the **DOM accessor**,
  which materializes the entire sheet *before* checking whether the element exists: **8 546 ms vs 15 ms** via
  `OpenXmlReader` for the same value, paid **twice** per file selection. ⭐ Not an optimization — the class's own
  doc lists *"SAX not DOM (1)"* as the first of I0's seven binding REK-6 guidelines, and this one place had
  quietly broken it. ⚠ The stop at `<sheetData>` is part of the fix: without it a workbook with **no**
  dimension would be walked row by row, trading one expensive mechanism for another (13 ms measured).
  **B —** three provider calls moved off the Dispatcher; ⛔ **everything touching a ViewModel or an observable
  collection stayed on it**. ⭐ Not a new pattern — `InferNewTableColumnsAsync` and
  `RefreshConvertedPreviewAsync` **already** did "read off-thread, publish on-thread"; `ReadSourceAsync` was
  the one expensive link left out, and its `await foreach` had the collection *pinning the read to the UI
  thread*. ⚠ Encoding/delimiter detection was deliberately **not** moved (bounded 64 KB, 1–3 ms) — moving work
  whose cost was never measured as a problem is exactly the "artificial `Task.Run`" the user forbade.
  **C —** new `IsRecalculating` (⚠ **not** `IsBusy`, which covers only `ReadSourceAsync` while the chain has
  two more flags — a bar bound to it would blink mid-operation) drives *"Loading file…"* / *"Reading
  clipboard…"* through M3b.1's one writer. ⚠⚠ Clearing it is **conditional on still being the current chain**:
  a superseded chain finishes *after* its successor starts, so an unconditional `false` would darken the bar
  for an operation that just began.
  ⭐⭐ **The most durable finding is in the frozen design doc: `data-import.md` §4.7 has said "the schema read
  and the preview go on a background thread" since v2 — and the implementation never did it.** So B was not a
  design change; it was the code finally catching up. ⚠ A sentence in a design document goes stale exactly as
  silently as a comment or a string (#284): that one was *true as intent* and *false as description* for the
  module's whole life, with a green build and green tests, because nothing checked it.
  ⚠⚠ **`XlsxImportProvider` had NO unit tests at all** (live probes only) — the fix brought its first four.
  ⭐ One guard reads the **source**, and that is justified rather than lazy: DOM and SAX return the **same
  value**, so no assertion about the result can tell 15 ms from 8 546 ms; only the mechanism differs. Verified
  by planting it — the source guard failed and all three behavioural tests stayed green. ⚠ **The suite does
  not prove the work left the UI thread**; that is the probe's standing job
  (`tools/probes/ImportFileOpenProbe`, which posts a `Render`-priority job and reports a verdict against a
  frame budget). ⚠ `.xls` (`ExcelDataReader`) is **unmeasured** and left alone — no large `.xls` to hand.
  ✅ **M3b.1d DONE (2026-08-04) — the import progress lives in ONE place, and the import command bar stops
  clipping** (`product-polish.md` §19.33). QA on A+B+C found the run's progress in two places at once and the
  top bar not fitting. ⚠ **A correction of fact changed what had to be removed: "Loading file…" was never in
  the top bar** — the elements there (`ProgressText`/`ProgressBar`/`Timer`) are gated on `IsRunning`, so the
  duplication was of the RUN, and one half of it (the toolbar bar) had been there since etap I5. Without that
  correction the fix would have deleted the wrong element.
  ⭐⭐ **The clipping mechanism is worth knowing generally: band B is a `DockPanel` with `LastChildFill`, so
  right-docked children take their size FIRST and the buttons are the last child — a horizontal `StackPanel`,
  which does not compress, it CLIPS.** Measured from the XAML: that panel already carries **520 px of combo
  minimums** (170+170+180) plus 7 buttons, 3 dividers, 3 labels and ~18 gaps, and a run took another ~400 px
  from it. ⚠ Band B's own comment said the timer is docked right *"so a running import never shifts the
  buttons"* — true and insufficient: they do not shift, they **disappear**. ⛔ Do not dock anything else there
  without counting what is left for the last child.
  **The user's call:** only the elapsed time stays on top; the bar and all statistics move to the **bottom
  panel** — which matches the module's ratified split (*top = where the import is DESIGNED, bottom = where
  RESULTS land*). ⭐ The elapsed time stays for a reason: the status bar does not carry it, and the SQL Editor
  and Script Executor both keep theirs in the toolbar, so removing it would break a family, not simplify.
  ⭐ Placed as an **overlay on the bottom panel's tab strip** (the chevron's own pattern), never its own row:
  a row would push the tabs down exactly when the run starts — §13.3 spread over time, the very defect this
  iteration removes — and an overlay costs zero pixels at rest. It is also visible whichever tab is selected,
  which a placement inside the Report tab would not be (and the Report is still empty during a run).
  ⚠ Recorded and deliberately unsolved: on a very narrow window the overlay could cover the last tab — QA,
  not a number tuned blind. ⚠ The command bar was NOT slimmed down: three 170/180 px combos are a **density**
  question, so they belong to the §13.3 gate and the UX sprint, not to a patch here (R7).
  ✅ **M3b.2 DONE (2026-08-04) — connecting to a database now reports in the status bar**
  (`product-polish.md` §19.34). ⭐⭐ **The start had a funnel; the END did not, and that decided the design.**
  Connect has two entry points, both through `MainWindowViewModel.ConnectAsync` — but `MetadataReady`, the
  obvious end signal, **does not fire** on three paths: a failed connect (no `ActiveConnectionChanged`, so no
  prefetch), a disconnect mid-load, and a prefetch throwing anything `LoadGroupAsync` does not catch (the
  `NotifyMetadataReady()` call sits *after* the loop). Each would have left the bar lit forever — §19.7.4's
  hazard at its worst, because the symptom would be permanent and unrelated to anything the user is doing.
  ⭐ So **each phase clears itself in its own `finally`**, which removes the "did the event fire" question
  entirely; `MetadataReady` is untouched and the status bar does not use it. ⚠ Verified and NOT a defect:
  `OnIsConnectedChanged` sets `IsExpanded = true`, which calls `LoadCategoriesAsync` synchronously, and then
  calls it again — no double prefetch, because `_categoriesBuilt = true` lands *before* the first await.
  ⚠⚠ **Phase 2 (restoring the tabs) got NO label, because one would be dead UI** — measured: the repaint
  happens *before* it, so a label set at its start only appears once it ends, i.e. when it is already false.
  ⭐ Ratified instead: keep phase 1's label up, so the window freezes showing *"Connecting to database…"*
  rather than going dark — and nothing flickers, because nothing repaints while it blocks.
  ⚠ Consequently `IsConnecting` is **not** cleared in its own method's `finally`: that would leave a gap over
  phase 2 and the bar would go off and on inside one operation. It lives until phase 3 takes over, and three
  paths guarantee every ending clears it (catch · `ApplyActiveConnectionChange(null)` · the prefetch's
  `finally`). ⭐ Phase 3 needed **no new `try/finally`** — the existing one (for `EndSidebarBulkUpdate`) was
  widened, so the safety mechanism is the one already proven there. It is also the **second** path in the app
  with an honest percentage (13 categories; the first was the Script Executor).
  ⭐ **A test caught a missing notification for the second time this stage:** 4 of 9 guards were red not
  because of the ladder but because `IsConnecting` had no change hook — the section only recomputed where I
  called `UpdateProgressSection()` by hand. The fix is also the better design (`OnIsConnectingChanged`, exactly
  like `OnIsExecutingChanged`) and deleted three scattered calls. §19.23.10 again: **a value being correct is
  not the screen updating.**
  ⚠ Manual metadata refresh is deliberately **not** wired (the user narrowed the scope) — pinned by a guard,
  because `RefreshAsync` does the same work with its own `try/finally`, so wiring it "while we are here" would
  be one line nobody would notice. ⛔ No cancel for connecting: there is no command for it, and inventing one
  would be adding a feature under cover of wiring progress.
  ✅ **M3b.3 CLOSED AS ANALYSED + DEFERRED (2026-08-04) — zero code changes, and that is the result**
  (`product-polish.md` §19.35). With every source wired the progress section reports **five** activities while
  `RailBrushKey` knows **three**, so the status bar could say *"Importing data… 110 200 rows"* while the rail
  showed **rest**. The user reconfirmed the direction — the rail distinguishes activity types by colour — but
  ⛔ **the set cannot be built today, and that is measured, not estimated:** the rail is **2 px**, severity owns
  hue **0°** and **~36°**, and **every** existing identity colour sits in the **149–215°** band
  (`ConnectedColor` 154° · `DebugLoopIconColor` 174° · `IconColor_Query` 200° · `AccentIconColor` 209°), so
  every pair collides (9–35° apart). Five distinguishable hues would need colours the product has never used.
  ⭐⭐ **The lesson is bigger than colour: a limitation of the TOOL is not an argument for shrinking the
  REQUIREMENT.** I recommended cutting the number of categories to fit the current palette; the user rejected
  that — *"I would not give up distinguishing activities just because the palette turned out too poor"* — and
  was right. The correct order is the reverse: the requirement stands and the insufficient tool becomes its own
  topic. ⭐ Stopping was also the correct execution of `color-language.md` **§0.5**: with three of five hues
  needing to be invented, the honest answer to *"will the user recognise it faster?"* is **"don't know"**.
  ⏸ **New topic P‑1 in `color-language.md` §9.2** (the palette's own question), carrying two defects found on
  the way, deliberately **not** fixed one at a time: **P‑2** `AccentBrush` on the rail is **2,89:1 in Dark**,
  below §10's 3:1 (⚠ `AccentColor` is shared, so it is not a local correction), and **P‑3** ⛔⛔ **the debugger
  has TWO colours for ONE fact in the same status bar** — the chip paints `AccentIconBrush`, the rail paints
  `DebugCurrentLineBarBrush`, i.e. the **editor's** current-line token; neither is a debugger identity colour
  and `AccentIconBrush` is already slated for retirement (DC).
  ⚠ Correction to §19.4.4 in passing: its note *"trace reads weak in Light"* is **not about contrast** — trace
  has the **best** contrast of the set (8,03:1 / 6,58:1).
  ⭐ **M3b IS NOW CLOSED IN FULL** (§19.31 · §19.32 · §19.33 · §19.34 · §19.35).
  ⏸⏸ **Next: ⛔ the §13.3 GATE** — the four persistent surfaces reviewed *together*, on a real database, in
  both themes, for **visual reception, not document compliance**. It blocks M4.
  ⭐ **A ready startup prompt for the next session:**
  [docs/design/product-polish-m3-next-session.md](docs/design/product-polish-m3-next-session.md).
- **🔬 THE OLD TREE DEFECT IS NOW REPRODUCIBLE BY THE USER, AND AN INSTRUMENT IS SHIPPED FOR IT
  (2026-08-04) — `EMBERTERN_TREE_DIAG`.** Full record: `metadata-refresh-analysis.md` **§10**.
  Reported scenario: expand several large categories (~tens of thousands of rows) → the list **starts
  scrolling down on its own** → cannot be stopped → any click hangs and closes the process.
  ⚠⚠ **The observation that shapes everything: from the EXE the process DIES, under Visual Studio it
  scrolls to a point, STOPS, and the app carries on.** A "debugger present / absent" difference points at
  an **exception**, not at cost — under a debugger an exception in a Dispatcher callback can be caught,
  without one it ends the process. ⭐ That is why the exception channel is a first-class part of the
  instrument, not an extra.
  **What the log answers** (five questions, set by the user): (1) does the offset change, **who** changes
  it and **from where** — `Offset`/`Extent` watched by two routes plus a **stack trace** on movement;
  (2) loop-forming events — `ScrollChanged`, `SelectionChanged`, **`RequestBringIntoView`** (tunnel +
  bubble), `EffectiveViewportChanged`; (3) rebuilds — row `CollectionChanged` plus the three existing
  rebuild points; (4) a cyclic Dispatcher callback — **scope nesting depth**, post counters by name, a
  500 ms heartbeat and **call-stack depth**; (5) exceptions — **`FirstChanceException`** plus unhandled
  and unobserved-task.
  ⭐ **Design decisions worth keeping:** own flag and **own file** (`%TEMP%\EmberTern-tree-diag-<pid>-<stamp>.log`)
  because a storm writes tens of thousands of lines and would drown the shared debug log · **`AutoFlush`
  on**, a real observer effect accepted on purpose because **the last lines before the process dies are
  the whole point** · **stack captures are budgeted** (first 25, then ≤1 per 250 ms, always during a
  detected storm) so the log does not drown in its own noise · ⛔ **not one diagnostic line inside a
  ViewModel or `SidebarFlatController`** — everything is subscribed from outside in one code-behind
  method, because the instrument must *observe* the mechanism, not join it (the three existing
  `ScrollTrace.Rebuild` calls were **re-routed**, not duplicated).
  ⭐⭐ **The instrument SELF-TESTS its exception channel** — it throws and catches a benign exception at
  startup so the log proves the hook is live. ⚠⚠ Without that, **an absence of `EXC` lines is
  undecidable**: it would mean either "nothing was thrown" or "the hook is dead", which are opposite
  conclusions leading to opposite searches (a negative measurement is the dangerous kind, #285).
  ⛔⛔ **THE INSTRUMENT'S FIRST RUN KILLED THE APP, AND THAT IS THE ENTRY WORTH READING (fixed 2026-08-04).**
  `TreeDiagnostics.Scroll` built its line with `string.Format` using the alignment `{4,+8:0.0}` —
  **alignment takes only an integer, so `+` is a syntax error** — and the `FormatException` travelled up
  through the ScrollViewer's `PropertyChanged` handler into Avalonia and **ended the process on the first
  category expand**. The build was green and would have stayed green: a composite format string is a
  mini-language parsed **at run time**. The user diagnosed it from the stack in `EmberTern-debug.log`
  (*"Failure to parse near offset 77. Expected an ASCII digit"* — offset 77 is that `+`).
  ⭐⭐ **The worst part is what it destroyed: a tool meant to catch someone else's defect BECAME the defect**,
  and the user's log described only the instrumentation. So the fix is not one character:
  **(1)** every line is now built from **separately formatted pieces** (`ToString`) — ⛔ **not one composite
  format string is left in the class**, because concatenation has nothing to parse and therefore nothing to
  throw; **(2)** every public entry goes through **one `Safe` gate** that swallows `Exception` and drops the
  entry (the user's requirement stated plainly: *a failed log write must skip the entry, never stop the
  app*), with dropped entries **counted and reported at exit** — a silent loss would be worse than no
  instrument; **(3)** a **`[ThreadStatic]` reentrancy guard**, because a throw inside logging reaches the
  `FirstChanceException` hook, which logs, which throws again.
  ⭐ **And the guard written against the CAUSE found a second instance on its first run** —
  `$"{DateTime.Now:yyyy-MM-dd…}"` in the header, the same family. `TreeDiagnosticsFormattingTests` feeds the
  pure formatters hostile input (`NaN`, infinities, `int.MinValue`, `null`, and text containing `{`, `}`,
  `{4,+8:0.0}`) and scans the source so the class cannot go back to a run-time-parsed format.
  ⚠ **The general lesson, wider than this file: a tool whose only job is to not crash the app must not use
  a mini-language evaluated in production.**
  🐞🔧 **CAUSE FOUND AND FIXED FROM THE USER'S LIVE LOG (2026-08-04) — `AutoScrollToSelectedItem`.**
  Full record: `metadata-refresh-analysis.md` **§11**. **The loop, identical in all 93 stack captures:**
  `SelectingItemsControl.AutoScrollToSelectedItemIfNecessary` (posted to the Dispatcher) →
  `ItemsControl.ScrollIntoView(index)` → `VirtualizingStackPanel.ScrollIntoView` →
  `RequestBringIntoView` → `ScrollContentPresenter.BringDescendantIntoView` → **Offset +24 px**.
  **Timeline:** t=122 502 the user clicks a row (`SelectionChanged`) · t=123 422 a category expands, the
  list reaches **13 217 rows** · t=123 499 onward the offset walks **26 → 50 → 74 → … → 2 210, always
  exactly +24,0 px, every ~98 ms, without end**.
  ⭐⭐ **Why one row and why never-ending:** the selected row sits thousands of positions outside the
  realized window (~39 of 13 217 visible), and `VirtualizingStackPanel.ScrollIntoView` **cannot jump** to
  an unrealized index — it knows the geometry of realized elements only. So it scrolls one row, realizes
  the next, raises `RequestBringIntoView` again, and **crawls toward the target one row per dispatcher
  cycle** (~9 minutes to reach row 6 000).
  ⭐⭐ **Why it cannot be stopped — measured, not deduced: the `heartbeat` DIES the moment the loop starts
  and never returns.** It runs at `DispatcherPriority.Background`; the loop floods the queue and starves
  that priority, so clicks and wheel events queue behind work that never yields.
  ⭐ **What the log RULED OUT, which mattered as much:** **zero exceptions** in the whole run (the only
  `EXC` line is the instrument's self-test — the silence was decidable *only* because of it) · **no
  reentrancy** (`ChevronClick` never exceeds `depth=1`) · **no selection loop** (three `SelectionChanged`
  in 133 s, none inside the loop) · expansion is **linear**, not quadratic (218 leaves → 220 entries) ·
  **our own `ScrollIntoView` appears in no stack of the loop**. ⭐ So the scrolling is a **CONSEQUENCE** of
  continuous `BringIntoView`, not its cause — the user's reading of the log, confirmed.
  ⚠⚠ **WHY BOTH EARLIER MEASUREMENTS MISSED IT, and the lesson is bigger than this defect: neither had
  anything SELECTED**, so `AutoScrollToSelectedItem` had nothing to chase. The variable that decides the
  whole phenomenon was absent from both experiments — neither was wrong, both were blind to that
  condition. ⭐ **A synthetic measurement reproduces the MECHANISM but not the STATE.** Before accepting
  that one rules a hypothesis out, list the states in which the defect occurs for the user and check which
  of them the experiment actually reproduces.
  **The fix is one property — `AutoScrollToSelectedItem="False"` on `SidebarList` only.** ⭐ It is a fix of
  the cause, not a workaround: it removes the mechanism the log names, and this list already has its **own,
  deliberate** "show me this object" (`OnRevealSidebarRow` → explicit `ScrollIntoView`), so a second
  automatic mechanism doing the same job is redundant here. ⛔ **Deliberately no conditions** ("scroll only
  if the target is near the viewport"), no custom scrolling algorithm, nothing else changed — the user's
  ratified call. ⛔ Guarded by `SidebarList_DisablesAvaloniaAutoScrollToSelectedItem`, because the property
  looks exactly like something that will one day be "tidied up": it defaults to `true`, removing it breaks
  **no other test**, changes no pixel, and the defect only returns for a user with a very large database.
  ✅ **CONFIRMED ON A LIVE RUN AFTER THE FIX, and the evidence is stronger than "it did not recur":**
  `AutoScrollToSelectedItemIfNecessary` in stacks **93 → 0**, `dOffset=+24.0` **93 → 0**, and the
  `heartbeat` **alive to the end** instead of dying — on a **bigger** tree (15 980 rows vs 13 217) with
  **six times more selections** (19 vs 3), i.e. the triggering condition occurred *more* often than in the
  run that produced the defect. ⚠ `RequestBringIntoView` still fires 84 times and that is correct — normal
  scroll-to-item on user interaction; what disappeared is the **automatic mechanism**, not the event.
  ⏸ **One open item for QA: keyboard navigation** (arrows, PageUp/PageDown, Home/End) keeping the selected
  row in view. ⛔ If it does not, the answer is a fix **for keyboard navigation**, never the return of the
  global auto-scroll (ratified 2026-08-04).
  ⏸⏸ **HYPOTHESIS TO OBSERVE, recorded at the user's request and deliberately NOT raised to a finding —
  there is no hard proof:** this may also have been the cause of the long-standing **flaky
  `ConnectionExpandBindingProbe` hang** (#94/#226/#261). The probe drives tree operations inside a real
  `MainWindow`; if a row happened to be selected it could have entered the same `AutoScrollToSelectedItem`
  loop, and a **starved Dispatcher looks exactly like** the previously-measured "teardown / dispatcher-loop
  shutdown" suspicion — a hung run rather than a failed assertion. ⭐ **The decisive criterion needs no new
  infrastructure: if that probe stops hanging from 2026-08-04 on, it is strong evidence the two problems
  shared one cause.** ⛔ Not to be declared resolved on a few green runs — the hang was rare by definition;
  observe over time and **record the outcome either way**. ⚠ Until then nothing changes: the probe still
  runs in its own partition and the user's 2026-08-01 instruction stands. Full record:
  `metadata-refresh-analysis.md` **§12**.
  ⏸ **DATA POINT, recorded because the note above says to record the outcome either way (2026-08-05, Avalonia
  12.1.1 sprint): the probe did NOT hang once in ~25 runs** across the sprint (nine baseline, nine post-change,
  plus the partition verifications). ⚠⚠ **This is NOT evidence for the hypothesis and must not be read as
  such** — three reasons: the observation window is days, not the months over which the hang was rare; the
  probe now shares its partition with `BrandingPresentationTests`, so it is not the same experiment any more;
  and Avalonia 12.1.1 itself brings **`Fix headless session hang when cleanup throws` (#21781)**, which is a
  *third* candidate cause. ⭐ So the sprint **added a candidate rather than eliminating one**, and the
  hypothesis is now less decidable than before, not more. ⛔ Do not close it on this.
  🔧 **`EMBERTERN_TREE_DIAG` STAYS as a hidden developer tool** (user's decision) — it is what found this
  cause after two years of the symptom, and it costs nothing when the flag is unset: no file, no
  subscriptions. ⛔ Do not remove it and do not surface it in the UI; reach for it whenever a
  scroll/selection/Dispatcher-shaped defect appears anywhere in the app, not only in the tree.
- **📋 OBSERVATION PARKED, NOT TO BE ACTED ON (user, 2026-08-04):** startup is still noticeably slower with
  a large number of open tabs. ⚠ This is the **known cost of the deliberate deterministic load** — chosen so
  diagnostics always has the full metadata context and never flags valid symbols as errors. ⛔ Do not touch
  it; recorded for the future only.
  ✅ **M3.3c DONE (2026-08-03) — the tab context menu; the tab strip is complete** (`product-polish.md`
  §19.24). Nine items, **zero new chrome** (the Keyboard Manager's `ContextMenu`/`MenuItem` styles +
  `{app:MenuIcon}` already exist).
  ⭐⭐ **The rule-#11 gate went from three entries to four by gaining a SCOPE, and that was the only change it
  needed.** `CollectUnsavedWork` / `HasSavableDirtyEditors` / `SaveDirtyEditorsAsync` iterated over *all*
  tabs, because the three existing entries always concern the whole set — but *"close tabs to the right"*
  concerns a **subset**, and without a scope the fourth entry would have to either **bypass the gate** or
  **ask about work in tabs it does not close** (the first is data loss, the second is a lie). ⭐ `scope ==
  null` means "all", so the three existing entries are untouched and their 26 tests passed unchanged.
  ⛔ Do not build a second "save many tabs" path. ⚠ The gate is **aggregating, not N prompts in a row** — a
  question asked eight times is not a gate, it is an obstacle to click through — and a failed save closes
  **nothing** (a partial close after a failed save is the worst outcome).
  ⚠ **Every command takes the tab as a PARAMETER, never the selection**: a context menu opens over a tab that
  need not be active, so reading `SelectedWorkspaceTab` would close someone else's document — gotcha #16/#99
  one level up.
  ⭐ **Every item has its own `CanExecute`** (user's request before implementation): *close to the right* is
  disabled on the last tab, *close unmodified* when every tab is dirty, *refresh* for a dirty tab **or a kind
  that does not refresh** — hence `WorkspaceTabViewModel.CanRefresh`, the **fifth member of the per-kind
  family**, because `RefreshAsync`'s `_ => Task.CompletedTask` arm makes the call *safe* but the menu item
  *dead*, and a clickable item that does nothing teaches that the command is broken. ⚠⚠ Gating depends on the
  COLLECTION's composition and `[RelayCommand]` knows nothing about it, so it is recomputed in the one
  existing `OnWorkspaceTabsChanged` hook — pinned on `CanExecuteChanged`, not on the value (§19.23.10's
  lesson again).
  ⭐ **"Show in Metadata Explorer" selects AND scrolls** — a selection off screen is indistinguishable from no
  reaction. ⚠⚠ **Expanding the category must be AWAITED, not merely requested**: setting `IsExpanded` fires
  `LoadGroupAsync` fire-and-forget, so looking for the leaf straight after would hit a category with no
  children — the item would do nothing the first time and work the second, the worst kind of defect. ⚠ Select
  synchronously, `ScrollIntoView` posted at `Background` (gotcha #221's shape).
  ✅ **M3.3b DONE (2026-08-03) — two tab-strip modes + two preferences** (`product-polish.md` §19.23).
  ⭐⭐ **Two modes, ONE mechanism**: one `ItemsControl`, one tab template, and the mode is *only* the
  `ScrollViewer`'s scroll directions — a `WrapPanel` wraps exactly when it is given a **finite** width, so
  horizontal-`Disabled` gives multi-row (+ `MaxHeight` = `Size.Row.Tab` × rows, so **only the strip scrolls**)
  and horizontal-`Auto` gives an infinite width and therefore a single row, forever. ⛔ Do not split it into
  two `ItemsControl`s: the ~60-line tab template would be duplicated and could then drift between modes.
  ⚠ `MaxHeight` is computed in code-behind because it is a **product of a role and a preference** and
  `{DynamicResource}` does not multiply — ⛔ no third catalog layer of ready-made heights (§19.1.4 settled the
  same question for `GridLength`).
  ⭐⭐ **The overflow counter counts the tabs you CANNOT see, not the tabs you have** (ratified) — the first is
  the only number nothing else on screen tells you. It is therefore measured from the **real layout**, not from
  the collection; a half-clipped tab counts as hidden. ⚠ Recorded risk: the strip does not virtualise, so the
  count is complete — if it ever starts to, the counter goes **silently low**. Reuse before create: the
  filtered list is the existing `SearchableComboBox`, the count rides its `SelectionBoxText`, zero new chrome.
  ⚠ Preferences are additive — **`CurrentSchemaVersion` stays 2** — and travel in the `.etsettings` export for
  free. `TabStripMaxRows` **survives a round trip through single-row mode** (pinned): resetting a limit that
  momentarily does not apply would look like tidiness and read as lost settings. Its minimum is **1, not 2**,
  because one row of a *multi*-row strip still hides nothing behind a menu, which `SingleRow` does not.
  ⭐ Settings Center got its own **Tabs** category (user's call — the tab strip is a separate surface, and it
  gives M3.3c's *"Tab settings…"* a precise destination).
  ⚠ **Two Settings Center guards fired on the first run and both were right** — a missing page-visibility
  property (selecting "Tabs" would have left the right pane blank) and a preference with no row and no
  recorded reason. That mechanism was not ceremony.
  ⚠ **Third time the visual probe showed a state that did not exist**: it loaded six resource dictionaries and
  `SearchableComboBox.axaml` was not among them, so the overflow control had no `ControlTheme`, no template,
  and rendered as **nothing** — a plausible-looking image with the subject missing. ⭐ **The probe must load
  the same dictionaries as `App.axaml`**; a missing one does not fail, it silently removes an element.
  ⭐⭐ **ACCEPTANCE ROUND ON M3.3b — three reports, and TWO OF THEM HAD ONE CAUSE** (§19.23.8). *"The scrollbar
  covers the tabs"* (single-row) and *"the scrollbar practically disappears"* (multi-row) are the same fact
  seen twice: Avalonia's `ScrollViewer` keeps its bar as a **thin line lying ON the content**, expanded only
  under the pointer. `AllowAutoHide = false` removes both properties at once — constant thickness, so space
  *can* be reserved, and visible without hovering. ⚠ **Reserving that space had to be code**: Fluent's
  `ScrollViewer` template spans the `ScrollContentPresenter` across the whole grid, so the bars always overlay
  and there is no "reserve space" property; the reservation is the `ScrollViewer`'s **`Padding`**, which the
  template passes to the presenter. ⭐⭐ **The thickness is MEASURED off the bar itself, never typed** — our
  themes declare no scrollbar width, and writing `12` would either be a dead literal or, worse, a reach for
  `Space.Lg`, which happens to be 12 too (**trap 6: a number does not determine a role**). Reservation is
  conditional (R13). ⛔ **The thumb colour was NOT touched even though the report named colour**:
  `ScrollBarThumbColor` is an application token, so raising it for one strip is patching a single screen (R7)
  and raising it globally leaves the etap — and it proved unnecessary, because the problem was the control's
  **state**, not its colour. ⭐ Wheel scrolling is single-row only (multi-row's built-in vertical wheel is
  already what one expects), handled on the **whole strip** and on the **tunnel**, stepping by a **quarter of
  the viewport** — tab widths vary by design (D6/§8.1), so "one tab" is not a unit.
  ⛔⛔ **AND THE FIRST FIX FOR THE OVERLAP WAS WRONG — the round that matters most** (§19.23.9). Reserving the
  bar's space with the `ScrollViewer`'s `Padding` is a **feedback loop**: padding changes the viewport, the
  viewport changes the bar's visibility, the bar changes the padding. In the **probe**, which lays out **once**,
  it rendered correctly and convincingly; in the app, which lays out in a loop, it never settled. ⭐⭐ **That is
  the FIFTH time this strip's probe showed a state that did not exist — and the first where the probe could not
  have been right in principle**: the other three were bugs *in* the probe, this one is outside its reach by
  construction. ⚠ **A tool that computes once cannot rule on convergence** — written on the probe, in place.
  ⭐ **The fix is structural**: the horizontal bar is no longer the `ScrollViewer`'s (it runs `Hidden`) but a
  **sibling of the tabs in its own grid row** — siblings cannot overlap, by construction rather than by a
  tuned number. ⭐⭐ And that same structure is why the loop cannot return: the bar's visibility depends on the
  **horizontal** span while its presence changes only the **vertical** dimension — orthogonal quantities.
  ⛔ Never gate that bar's visibility on anything it affects itself.
  ⏸ **The overflow button/counter is DEFERRED by the user, not abandoned** — the `SearchableComboBox` version
  was visually wrong (mispositioned popup, rows rendered by `ToString()` because `DisplayMemberPath` does not
  feed the popup list — it needs an `ItemTemplate`), but the deeper error was the one the user named: it mixed
  the strip's LAYOUT with an extra element. `TabStripOverflow*` strings stay for its return.
  ⭐ **Third round: "Maximum rows" now HIDES in single-row layout** (§19.23.10) — the user overruled this
  etap's own decision to keep it visible, and the rule is better: *the interface does not show settings that do
  nothing in the current mode.* My argument confused **hiding a row** with **discarding a value**; the number
  is still kept. ⚠ The comment stating the opposite was corrected in the same step (trap 21 in its worst form —
  it justified behaviour that no longer existed). ⭐ The condition is an **AND of two independent reasons**
  (mode ∧ search filter) and lives on the page: writing the mode's answer into the row's `IsVisible` would let
  a search resurrect a row that does not apply. ⚠⚠ **Asserting the property's VALUE was not enough** — read
  directly it is correct even when nothing announces it, while the binding re-queries only on
  `PropertyChanged`; the notification is therefore an assertion, **verified by planting the violation**.
  ✅ **M3.3a DONE (2026-08-03) — RE-SCOPED BY THE USER BEFORE IT STARTED, and that is its first lesson**
  (`product-polish.md` §19.22). The plan row read *"geometry, `Size.Row.Tab`, indicator"* — **all three were
  already delivered by M3.1a**, so the user refused to do the etap for the etap's sake: *"jeżeli M3.1a
  faktycznie dostarczyło geometrię M3.3a, to nie cofajmy się do planu tylko dlatego, że plan jest
  nieaktualny."* ⭐⭐ **A stage plan goes stale exactly as silently as a string or a comment** (#284, traps
  20/21) — check in the CODE that a sub-etap's subject still exists before starting it.
  The iteration instead closed the strip's **technical debt**: 12 → 5 local values (4 onto roles, 2 deleted
  as genuinely redundant, 3 moved into styles), and the last M‑1 literal.
  ⚠⚠ **Moving a style is a change to its PRIORITY, and this one re-created the §19.2 regression.** The tab
  Border carried a local `Background="{DynamicResource PanelBrush}"`; the relocated
  `Border.active-tab { Background = BackgroundBrush }` **lost to it**, so the active-tab background swap
  would have silently stopped working — build, suite and smoke all green, exactly like the indicator in
  §19.2. The new test failed with `#ff252526` vs `#ff1e1e1e` and named it. ⭐ Fix is §19.2's own recipe —
  **both states as setters** — plus a component-class anchor: `Border.workspace-tab` (rest) and
  `Border.workspace-tab.active-tab` (active). ⚠ **The anchor is load-bearing**: without it the *resting*
  rule would paint every `Border` in the application. **A state class says WHICH state; a component class
  says OF WHAT.**
  ⚠ **K9/K10 were never about this strip** — both sit on `TabItem` (bottom-panel + editor sub-tabs); the
  document tab strip has no `TabItem`, no 13 px label and no `CornerRadius`. New **K12–K14** (the two
  paddings + the close-button margin) go to §13.3 **as one question**, because all three change how many
  tabs fit in a row — a density decision, not a catalog one.
  ⭐ Three catalog findings recorded, not acted on: **`Pad.Tab` has 0 consumers**, and **`Size.Icon` (14) /
  `Size.Icon.Lg` (16) have 0** while the literals appear **64 ×** and **15 ×** app-wide — an app-wide sweep
  for §13.3/M4.3. The tab strip is now `Size.Icon`'s first consumer (the role's own comment names it).
  ✅ **M3.2d DONE (2026-08-03) — M‑1, pure housekeeping, zero visual change and zero test-count change**
  (`product-polish.md` §19.21): the 13 English `ToolTip.Tip` literals are **13 → 3** (7 connection-toolbar
  + 3 window-caption buttons migrated to `UiStrings`; 1 belongs to M3.3, 2 to M4.3). ⭐ They got their **own
  `*Tooltip` constants rather than reusing the existing label strings** — a label and a tooltip answer
  different questions, and the *reverse* of that reuse is already recorded as a defect (Keyboard Manager
  audit finding **D6**: seven menu items whose `Header` read a tooltip constant, which is how "Add item"
  became a menu entry). ⚠ **None carries a gesture, by rule**: the commands are `Tree`-scoped (F3/F4/F8), and
  a toolbar tooltip promising a key that only works in the tree teaches something false
  (`keyboard-manager.md` §14). ⚠ `{x:Static}` is **compile-checked**, so a typo is a build error — the exact
  opposite failure mode from `{DynamicResource}`, which silently keeps the inherited value.
  ⭐ **Side finding, measured and deliberately NOT fixed** (scope was literals in XAML, not orphans in
  `UiStrings`): six constants have **no consumer anywhere** — `ConnectionConnect`, `ConnectionDisconnect`,
  `ConnectionDelete`, `ConnectionNew`, `ConnectionsEmptyHint` and **`TabCloseTooltip`**. The last one matters
  for planning: the string for M3.3's remaining literal **already exists**, so that item is probably one
  substitution rather than new work.
  ⚠ **An ACCEPTANCE FIX ROUND ran first (2026-08-03)** — 14 defects from ordinary
  use, grouped into 6 causes: [docs/history/23-acceptance-fix-round.md](docs/history/23-acceptance-fix-round.md).
  ⭐⭐ **One of them was a data-loss bug: two instances of EmberTern could publish an EMPTY `settings.dat`**
  and the empty file then loaded as `Missing`, so the next write made defaults permanent (gotcha #304 —
  `AtomicWrite` shared one temp filename across processes). ⭐ **Four reports were NOT where they pointed:**
  the result grid's column order was `GridLayoutBehavior` replaying a saved order, not `PopulateResultGrid`;
  the disabled hammer's problem was `Opacity` letting the toolbar through, not a colour (`AccentColor` is
  identical in both themes); the "tooltip" that only a restart removed is an `OverlayLayer` card, not a
  tooltip — the first fix for it was measured **inert** and deleted; and the parameter-type dialog was fed a
  **selectable procedure called from `FROM`**, a shape the lookup did not recognise at all (gotcha #307).
  ⭐ **`Alt+F` is Format SQL again**, with `Ctrl+K` as its alternate — the one ratified `Alt+letter`
  (`keyboard-manager.md` §18).
  ⚠⚠ **TWO of the six needed further rounds after the user's acceptance passes, and the lesson is the SCOPE OF THE
  QUESTION, not the analysis.** (a) The picker filter fields — round one moved the border only, and the measurement
  then showed the selector **was** applying while the value still sat at **2.55:1**, under §10's 3:1 floor;
  ⭐ *"almost visible" is indistinguishable from "invisible"*, and the fix is a recessed **fill** plus a border over
  the threshold, pinned at the threshold in both themes (gotcha #308). (b) Parameter types took **four** rounds and
  ended in an architectural change — see below.
  ⚠⚠ **AND ROUND FOUR'S LESSON IS THE BIGGEST: an architecture is not verified by the tests of the thing it
  replaced.** The user rejected round three with four findings, of which **only two were code defects** — and the
  inventory they demanded (*which consumers must work for a routine call, which for any parameterised SQL, which
  must not fire at all*) settled it in minutes: **"does this SQL carry named placeholders?"** is scoped to **any SQL**
  and gates the parameter dialog, while **`IRoutineInvocation`** is scoped to **typing only**. So the new model
  *could not* have widened the dialog — provable from `git show HEAD:`, and the gate is unchanged since `54b630c`.
  ⭐ What the screenshots really showed was a **mislabelled dialog**: the parameter editor is reused for any
  parameterised statement, so a plain `INSERT … VALUES (:a, :b)` opened a window headed *"Execute Procedure"*.
  Title and header are now neutral (**"Execute"**, the user's choice). ⭐⭐ **A lying label is indistinguishable from
  a malfunction** (gotcha #311).
  **⭐⭐ THE ROUTINE-INVOCATION MODEL (2026-08-03) — the round's most durable result, and it came from the user
  refusing a third patch.** Parameter typing had been fixed twice by adding a STATEMENT SHAPE to a consumer
  (`EXECUTE PROCEDURE`, then `SELECT … FROM P(…)`), and each fix left the next syntax dead — `FOR SELECT … INTO`,
  `INSERT … SELECT`, and a long tail after them. The user named the cause: *the AST should be able to answer "a
  procedure is invoked here with an argument list"*. Delivered: **`IRoutineInvocation`** on the AST (routine ·
  package · argument spans), implemented by `ExecuteProcedureStatement` and a new **`RoutineTableReference`**
  (⚠ a **subclass** of `TableReference`, so the binder/highlighting/navigation are untouched); the parser **stopped
  discarding the argument list** — `ParsePrimaryFromItem` read the name then jumped to the alias, so `rap(:a,:b) r`
  carried the single token `rap`, neither arguments **nor alias**, which is *why* consumers were re-scanning text;
  `MERGE … USING <name>(args)` modelled through the same `ParsePrimaryFromItem`; and the consumer now asks
  `DescendantNodesAndSelf().OfType<IRoutineInvocation>()`, with ~130 lines of token walking and
  `TryExtractExecuteProcedureName` **deleted**. Typing resolves **per placeholder** (one statement can invoke
  several routines), so a binding carries its routine plus the slot and a name standing in two routines claims
  nothing. ⭐ **The proof it is architecture and not a patch: the pinning theory has rows for FOR SELECT,
  INSERT…SELECT, UPDATE OR INSERT, a CTE body, MERGE USING, a cursor declaration, a derived table and an EXISTS
  subquery — and none of them has a line of code behind it.** ⛔ **Do not add a statement-kind branch to that walk;
  a call it cannot find is a call the parser does not model** (gotcha #309, Contract #1).
  **⭐ Two drag-and-drop templates, on that same foundation:** `INSERT INTO … SELECT` for tables (new — both column
  lists from ONE `Insertable` call, so they cannot drift), and `FOR SELECT … INTO` for selectable procedures, which
  **already existed and was unreachable from the SQL Editor** because the snippet context was fixed at attach time.
  New `SnippetInsertionContextResolver` decides it from the **drop offset** by asking the parser whether it is
  inside a `BlockStatement` — so scaffolds appear inside a routine being written in the console and stay hidden at
  the top of an empty one (⛔ never by counting `BEGIN`/`END`: wrong for `CASE … END`, #117/#128/#129).
  ⭐⭐ **`EveryGeneratedInvocation_IsRecognisedByTheModel` is what makes generation and parsing ONE feature** — every
  template that emits a call is fed back through the walk the parameter dialog uses (gotcha #310).
  ⚠ **And EVERY built-in scaffold is now offered in every editor**, ratified on the user's general argument (the SQL
  Editor is where `EXECUTE BLOCK` / `CREATE PROCEDURE` / `CREATE TRIGGER` get written). Two narrower answers were
  built and removed: widening only the reported template (an exception, not a rule), and deriving the context from
  the drop offset — which fails for the case that matters, since a scaffold is what you reach for **to start** a
  body. Pinned once over the whole catalog by `NoBuiltInTemplate_IsHiddenByTheInsertionContext`.
  **⭐⭐ A TYPE HAS TWO PROVABLE ORIGINS, AND THAT IS THE SECOND AST FACT (round four).** On the user's directive
  (*"nie seria if-ów dla kolejnych instrukcji, tylko wykorzystanie modelu AST jako jednego źródła wiedzy"*):
  **`IColumnValueTarget`** — table + **(column, value-span) PAIRS** — implemented by `InsertStatement`,
  `UpdateOrInsertStatement` (positional `(cols)`↔`VALUES`, ONE producer since the shape is identical) and
  `UpdateStatement` (`SET col = expr`, paired by adjacency). ⭐ Pairs rather than two parallel lists is what lets one
  interface serve shapes that pair differently while keeping that difference out of the consumer. One
  **`ParameterTypeSource`** (`RoutineParameter(owner, slot)` | `TableColumn(owner, column)`) is the uniform answer,
  so the VM switches on the **kind of source**, never on a kind of statement — a third origin would be a new fact
  plus one arm. ⚠ Refusals, each with a reason: no column list · a column/value length mismatch · `WHERE col = :p`
  (a predicate is a token fragment at structural depth) · a value that is not the whole placeholder (gotcha #312).
  ✅ Pushed to both remotes (`85c8747`). ⏸ Awaits the user's visual QA (both themes).
  **Closed and not returning:** M3.1 (Status Bar 2.0, six iterations) · **H‑3** (stable titlebar
  layout) · **H‑5** (Commit/Rollback on their own tokens) · **§7.5** (superseded by the colour
  language) · 🔒 **the colour language itself — designed, ratified, rolled out across the whole product
  and visually accepted** (K1–K7 + a closing sweep + one optical fix; `product-polish.md`
  §19.15–§19.20). Measured on close: **230 `SvgIcon` in views, 81 coloured, not one action button
  outside the language**, zero open questions.
  **⭐⭐ THE STAGE'S HARDEST LESSON IS ABOUT METHOD, NOT PIXELS.** Of four moves in M3.2a **one
  survived**; M3.2b was **withdrawn in full**. Every rejected change *worked* and removed a measured
  defect — the user's diagnosis names the mechanism: *"analiza jest bardzo dobra, pomiary są bardzo
  dobre, ale później próbujesz doprowadzić regułę do logicznej konsekwencji, zamiast jeszcze raz
  spojrzeć na gotową aplikację."* ⛔ **Trap 17: a rule DESCRIBES what is already good; it is not a
  mandate to change everything that does not fit it. An element that breaks the rule is often an
  exception that WORKS.**
  **⭐⭐ SEVENTEEN RATIFIED RULES (R1–R17) — handover §5 holds them all; six are load-bearing
  everywhere:** **R5** colour may express an action's priority, SIZE MAY NOT · **R8** the acceptance
  criterion is *"does this look like a polished commercial application?"* · **R13** never reserve space
  for an element that cannot appear in that context · ⭐ **R15** *iteration size follows UNCERTAINTY,
  not caution* — small steps while a design is forming, one pass once it is accepted; keeping
  micro-iterations after the uncertainty is gone is its own failure mode · ⭐⭐ **R16** *a measurement is
  a DIAGNOSTIC tool; the acceptance criterion is the screen*, with the hard corollary that **a test
  which goes green on a bad screen is worse than no test** — such a test is NARROWED to what a machine
  can judge, never "strengthened" · ⭐ **R17** *conformance to the document ≠ coherence of the product*;
  a whole-surface review is its own step, never the sum of per-iteration acceptances.
  **⭐ [docs/design/color-language.md](docs/design/color-language.md) IS NOW A REFERENCE DOCUMENT, NOT A
  PLAN — read it whenever you add an action or touch a colour.** Colour in an IDE answers **four**
  disjoint questions, which is why they never contradict: **S1 rodzaj** (`IconColor_*`) · **S2 akcja**
  (roles R‑1…R‑7) · **S3 tożsamość modułu** (state only, never a button) · **S4 hierarchia przycisku**
  (`primary` + `OnAccentBrush` = contrast, *not* semantics). ⛔⛔ **§0.5 is an overriding gate that
  outranks the role table: before changing ANY colour, answer "will the user recognise the action
  FASTER?" — "no" or "don't know" means STOP and come back with a proposal.** *"It now matches the
  language"* is **not** an answer to that question — it was M3.2b's only justification. ⭐ And if a rule
  turns out to make the UX worse, the **rule in the document gets fixed, not the implementation
  defended**.
  **⚠ Four traps this stage paid for, all wider than Product Polish** (handover §9; gotchas
  **#303/#304**): ⭐⭐ **a text control's BOX is not its INK** — line height includes descent, which
  descender-free text leaves empty, so box-centring still reads as misaligned against an element whose
  ink *is* its box; correct it with a `RenderTransform` (never a margin), and note that
  `UseLayoutRounding="False"` helps only an element that IS its own ink · ⭐ **a measurement by CARRIER
  cannot tell a role from a state** (the icon inventory counted by token, so state glyphs landed in the
  actions table and three planned rows evaporated) · ⭐ **read what a prior measurement was MEASURING
  before using it as an answer** — the more emphatic the old comment, the stronger the pull to treat it
  as closed · ⚠ **a stale comment teaches the wrong thing exactly like a stale string**
  (`Colors.axaml`'s "Warning=delete" legend outlived the change it described and generated the whole
  drift K2 had to undo — gotcha #284's shape in prose).
  **⭐ Four architectural decisions from the colour rollout that outlive it:** a role gets its **own
  value, never an alias** (an alias re-couples what was just uncoupled, silently), and **a value meant
  to diverge is a measurement with a date, not an assertion** · an optical correction goes through
  `RenderTransform` · when moving a role onto its own token, **give the token per-theme values BEFORE
  repointing consumers** (that is what made K7 visually neutral instead of a contrast regression) ·
  `UseLayoutRounding="False"` is for an element that is its own ink.
  **⏸ Still open, each with a home:** **DC** (retiring `AccentIconBrush`/`InfoIconBrush` → M4.3/M5;
  ⚠ both still have consumers and are **not** orphans) · the **K1–K11 collision register** (§18.R → the
  §13.3 gate) · **V‑1** (the SQL comment colour, ratified to stay) · **R‑6 (DPI)** — ⚠ partly
  **unverifiable headlessly**, check 150% by eye.
  ⛔ **§13.3 IS A QUALITY GATE THAT BLOCKS M4** — after M3 the four persistent surfaces are reviewed
  *together, on a real database, in both themes*, for **visual reception, not document compliance**.
  ⭐ R17 raised its weight: the colour language's closing sweep found two residues that became decidable
  only when the whole strip was looked at at once.
  **⭐ THREE PRINCIPLES THAT OUTRANK THE CATALOG:** **§0.1 Persistent UI** — Status Bar, Toolbar, tab
  strip, Metadata Explorer, DataGrid, base controls and context menus beat screens opened once a day;
  governs *order and effort, not scope*. **§0.1.1 tokens are a means, not the end** — ⛔ never report a
  stage done on green tests alone. **§0.1.2 Application Chrome is ONE surface, not four components.**
  **⭐ RATIFIED (D1–D12), do not re-litigate:** control heights **24 / 22 / 28** · **Cascadia Mono** ·
  app name + version **removed** from the status bar · progress infrastructure (M3.1) split from wiring
  operations (M3b) · **two tab-strip modes**, multi-row default, row limit 1–10 default 3, only the
  strip scrolls · ⛔ **no `MaxWidth`/ellipsis on tabs** — measured refutation: `XXX_GG_WYSTCECHKART_AU99`
  and `…_BU99` differ at **character 20**, so truncation renders them identically · Metadata Explorer
  unchanged (a decision, not debt) · Dependencies trees migrate for **consistency, not performance**,
  onto a shared `TreeListView` — **never onto `SidebarFlatController`**.
  ⚠ Tab-strip preferences are **additive — `CurrentSchemaVersion` STAYS 2**, and `TabStripMaxRows` is a
  numeric preference, so the settings-center §17.4/§17.4a pattern applies (`PreferenceRange`,
  blur-or-Enter commit, digits-only on the tunnel, and ⚠⚠ **the control carries its ROW as
  `DataContext`**).
  **Earlier sub-stages (M0–M2c) — one line each; detail lives in `product-polish.md`:** **M2a** built
  the catalog (`Tokens.axaml`, `Typography.axaml`) with zero visual change; **M2b** switched the base
  controls onto it over 21 iterations and produced the **`FluentBridge`** pattern plus four
  architectural decisions (⛔ the Bridge is mapping only · **the container decides size, the element
  accepts it** · **a rule must be written POSITIVELY** · **height comes from context, the variant
  carries colour**); **M2c** removed what the catalog blocked — `FontSize` 605 → 43, `CornerRadius`
  37 → 19, **62 deliberate exceptions each with its reason written beside it** (R12), appearance
  unchanged. ⚠ Two views still carry a local `MinHeight="26"` for Fluent's chunky `Expander`
  (`ProcedureDetailTabView`, `FunctionDetailTabView`) — **removing it is NOT neutral** (the Bridge maps
  `ExpanderMinHeight` to 24), so it stays with a reason as register entry **K7**.
- **🐞 ET0003 NA NAZWIE GENERATORA W `GEN_ID(…)` — FIXED 2026-08-01 (minimal bugfix, awaits the user's
  confirmation in the running app).** `v = GEN_ID(gen_bomitem, 1);` w ciele PSQL zgłaszało **ET0003
  UnresolvedVariable** na istniejącym generatorze. Build 0/0; suite **7057** green (6989 + 14 + 54, up 17).
  ⭐ **Znowu nie diagnostyka — binder, i znowu CZĘŚCIOWA KOPIA jednej wiedzy** (gotcha **#302**).
  Firebird dopuszcza gołą nazwę obiektu w wyrażeniu w **dokładnie dwóch** miejscach: operand
  `NEXT VALUE FOR` i **pierwszy** argument `GEN_ID(…)`. `BindGlobalCatalogReferences` znał **oba**;
  `BindPsqlExpression` miał ręcznie dopisany jednolinijkowiec pokrywający tylko *„poprzedni token to `FOR`"*.
  Więc w ciele PSQL argument `GEN_ID` trafiał do `BindBareLocal` jako kandydat na zmienną lokalną.
  ⭐⭐ **Kolejność dobiła sprawę: globalny skan katalogu biegnie OSTATNI i pomija offset, który inny binder
  już zreferencjonował** — więc błędna referencja `Variable` **wygrywała pozycję**, a poprawny binder był
  pomijany. Objaw jest odwrotnością „nikt tego nie zbindował". ⚠ W `SELECT` działało od zawsze (nie ma tam
  walkera PSQL), co maskowało defekt — istniejące testy `GenId_ResolvesSequence` pokrywały wyłącznie zapytania.
  ⭐ **Poprawka: jeden wspólny `IsGeneratorNamePosition(t, k)`**, czytany przez skan, który nazwę
  **rozwiązuje**, i przez walker PSQL, który ma ją **zostawić nieprzypisaną**. ⛔ Zero `if (Function ==
  "GEN_ID")` w `DiagnosticsEngine` — silnik diagnostyk **nietknięty**.
  ⭐ **Druga połowa: nieznany generator to teraz ET0001, nie cisza.** Skan zapisywał referencję tylko gdy
  nazwa **rozwiązała się** do `Sequence`; literówka znikała bez śladu. Teraz referencja `SchemaObject`
  powstaje zawsze — związana z sekwencją albo **świadomie nierozwiązana**. ⚠ **To nie łamie reguły „prefer
  silence"**: cisza jest słuszna tam, gdzie goła nazwa jest naprawdę wieloznaczna (kolumna / zmienna /
  etykieta), ale w pozycji, którą gramatyka przypina do jednego rodzaju obiektu, nieznana nazwa jest
  *dowodliwie* nieznanym obiektem. ET0001 pozostaje bramkowane metadanymi — bez połączenia cisza.
  ⚠ **Zakres zmierzony, nie wydedukowany (FB5, 2026-08-01):** `GEN_ID(GEN_ORDER_ID, 0)` → `999`, natomiast
  `MAKE_DBKEY(ORDERS, 0)` **odrzucane przez sam silnik** (`-206 Column unknown`) — jego pierwszy argument to
  zwykłe wyrażenie, a `RDB$GET_CONTEXT`/`RDB$SET_CONTEXT` biorą literały tekstowe. Żadna inna funkcja
  wbudowana nie stawia nazwy obiektu tam, gdzie czytana byłaby kolumna lub zmienna. ⚠ **Precyzja:** tylko
  **pierwszy** argument `GEN_ID`; drugi to zwykłe wyrażenie i niezadeklarowana nazwa tam nadal daje ET0003.

- **🐞 ET0003 ON `EXECUTE BLOCK` — FIXED 2026-08-01 (minimal bugfix, awaits the user's confirmation in the
  running app).** A variable DECLAREd in an `EXECUTE BLOCK` was reported **ET0003 UnresolvedVariable**
  wherever the body used it; the identical code in a procedure was silent. Build 0/0; suite **7040** green
  (6972 + 14 + 54, up 13). ⭐ **It was NOT a diagnostics bug and not a binder bug — it was STATEMENT
  SEGMENTATION**, one predicate answering the wrong question (gotcha **#301**). `SqlParser.Parse` chose the
  PSQL whole-body scan via `IsPsqlDefinitionStart` — *"is this a `CREATE/ALTER/RECREATE` of a
  `PROCEDURE/TRIGGER/FUNCTION/PACKAGE`?"* — but what segmentation needs to know is *"does this statement have
  a PSQL body, so its inner semicolons do not end it?"*, and the two questions agree on everything **except
  `EXECUTE BLOCK`**, which defines nothing yet has exactly that shape. So the block fell to the plain `;` scan
  and was **cut in two at the end of its first `DECLARE`**: its `BEGIN … END` became a separate
  `AnonymousBlockStatement` with its own `RoutineBody` scope, which could not see the declarations.
  ⚠ **Invisible without a DECLARE section** — the body's `BEGIN` raises the depth before any top-level `;`
  appears, so the plain scan was right by accident, which is why the defect survived this long.
  ⚠ **It only *looked* colon-specific**: a `:v` always records a Variable reference, while a bare name in a
  DML position is a column candidate and stays silent (the bare assignment target *was* flagged too).
  ⭐ **Fix: a second predicate for the second question** — `HasPsqlBodyShape` = `IsPsqlDefinitionStart` ||
  `IsExecuteBlockStart`, used **only** at the segmentation dispatch; `IsPsqlDefinitionStart` keeps its exact
  meaning for `ClassifyDdl` (`DdlStatement.IsPsqlDefinition`). ⛔ No `if (ExecuteBlock)` anywhere downstream,
  no ET0003 exemption, no `:`-token special case — the diagnostics engine, the binder and `ScanPsql` are all
  **untouched**, and the block now takes the very path a procedure takes.
  ⚠ **The strict scan was fixed too, deliberately, and that is a small behaviour improvement beyond the
  editor**: the same split reached the executor boundary (gotcha #192), so an `EXECUTE BLOCK` with a `DECLARE`
  section was being sent as two broken statements. Pinned that it still yields at its `END` and does not
  swallow the next statement. ⚠ `UnknownCursor_DeclaredButMisSplit_IsNotFlagged` — a conservatism guard
  written *around* this split — still passes, and is worth reading as the shape of a workaround for a bug in
  a lower layer.

- **🎨 BRANDING UX SPRINT — DELIVERED 2026-08-01, awaits the user's visual QA. Committed `3e1df1a`, merged
  to `master` in `93d640f`.** A small,
  closed sprint on the visual identity only: **no logic, no fonts, no spacing, no colours, no layout rebuild**
  — and explicitly **not** the start of the backlogged app-wide UX sprint. Build 0/0; suite **7027** green
  (6959 + 54 probe + 14, run in the three groups described under "Tests"); smoke clean.
  Narrative: [docs/history/01-...](docs/history/01-v1-foundation-and-workspace.md) (§"Branding UX sprint");
  asset map + swap procedure: **[src/EmberTern.App/Assets/Branding/BRANDING.md](src/EmberTern.App/Assets/Branding/BRANDING.md)**.
  **⭐ THE LOGO IS OUT OF THE TITLEBAR AND NOW APPEARS IN EXACTLY ONE PLACE: About.** The user's reasoning,
  and it is the modern desktop idiom (ChatGPT, Claude, VS Code) — a working surface's chrome is for the
  document, not the product's identity, and the 26px mark plus its divider spent ~40px of the window's most
  contested horizontal space telling the user which application they had launched.
  **⚠ The removal's hard half was the LEFTOVERS, not the `<Image>`.** Three, each of which reads as a
  rendering bug rather than as stale markup: (a) ⭐ **a container whose children are all collapsed is STILL
  MEASURED**, so `IsVisible` on the children alone would have left the brand `StackPanel`'s own
  `Margin="8,0,2,0"` as an empty inset at the window's left edge — the block is therefore gated as a whole on
  `HasActiveConnection` (safe: its only other content is the DEV MODE badge, and `IsDeveloperModeActive` reads
  `ActiveProfile?.DeveloperMode == true`, which cannot be true without a profile); (b) the divider that
  separated the mark from the connection name — deleted, since with nothing to its left it is a rule against
  the window edge; (c) the action zone's **leading** divider, the same problem one level out — gated on the
  same condition as the block it separates. ⛔ The titlebar comment says all of this in place, because
  "add the logo back" is a plausible-sounding regression.
  **⭐⭐ THE ICON AUDIT'S REAL FINDING WAS THE PATH THAT DID NOT EXIST: 25 of 26 windows had NO icon at all** —
  Settings Center, Keyboard Shortcuts, Third-party notices, the BLOB editor and every dialog rendered a blank
  slot in their title bar and in Alt+Tab. Only `MainWindow` set one, in its constructor. ⭐ **Fixed with ONE
  `<Style Selector="Window">` setter in `ControlStyles.axaml`, and that is structural rather than tidy:
  Avalonia has no application-level window icon, but `Window.Icon` IS a styled property**, so one setter
  reaches all 26 windows *and every window added later* — which a per-window assignment cannot, being a rule
  someone must remember 26 times where the 27th window is the one that forgets. ⚠ **The `MainWindow` ctor
  assignment was DELETED in the same change, not kept as belt-and-braces**: a local value outranks a style
  setter, so it would have made the main window the one window whose icon came from a second source.
  ⚠ **A compiling style setter proves nothing** — it compiles whether or not `Icon` is styled and whether or
  not the converter reads an avares URI — so it is pinned by a test against a **bare `new Window()`**, which
  is also the stronger assertion (an icon reaching a window with no XAML and no code-behind can only have come
  from the app-level style). ⚠ **Nothing about the exe icon changed**: `<ApplicationIcon>` is a build-time
  embed for Explorer and the pre-launch taskbar, a different job from the runtime one.
  ⚠ **`logo.png` stopped shipping** — 833 KB of source artwork with no avares reference anywhere, the largest
  embedded resource in the assembly, now excluded from `AvaloniaResource` exactly as the icon `.svg` sources
  already are. It stays in the repo as the master.
  ⚠ **About needed no work** — it has shown the 128px mark since the Hamburger sprint (2026-07-29); only a
  comment that had just gone stale ("the same asset the titlebar uses") was corrected.
  **⭐⭐ THE ARTWORK ITSELF WAS REPLACED (the sprint's actual point), IN TWO ROUNDS — AND IT ENDED WITH TWO
  SEPARATE MASTERS, WHICH IS A DECISION, NOT DRIFT.** Round 1 replaced all three files from one master (a
  forged-steel database cylinder with an ember wing). ⚠ **Then the user saw it in the Windows taskbar and
  rejected it there** — so round 2 replaced the **OS icon only**, from a *different* source, and left the
  About mark on the round-1 artwork.
  ⭐ **The lesson generalises past branding: an icon and a presentation mark are judged at different sizes
  (16–32px in dense chrome vs 128px on a quiet window), so "one source feeds both" is a convenience, not a
  requirement — and when it stops holding, the honest answer is two masters, not a compromise rendition that
  serves neither.** ⚠ Round 1's note here claimed the two "change together because both read one source";
  that is now false **on purpose**, and `BRANDING.md` opens with the correction because *"update the logo"*
  is otherwise ambiguous and would silently change a surface nobody asked about.
  ⭐ The masters live in `Assets/Branding/Masters/`, excluded from `AvaloniaResource` **as a folder** —
  a per-file rule is one more thing to remember, and its failure is silent (the 1.5 MB icon source would
  simply have shipped).
  **⚠⚠ CUTTING AN OPAQUE BACKGROUND — THE STEP THAT MUST NOT BE SIMPLIFIED, AND THE REASON IS ARITHMETIC.**
  The icon source arrives 24bpp on `rgb(14,15,19)`, and **the artwork itself contains pure black** — Chebyshev
  distance **19**. So a global "remove pixels near the background colour" punches holes through the cylinder,
  and a **flood fill from the border with tolerance ≥ 19 walks through those black pixels into the interior
  and eats a wedge out of the middle of the logo** (observed at 28, visible only once composited over
  magenta). **The tolerance must sit above the background's own noise (±4) and strictly below the distance to
  the artwork's darkest pixel; 12 shipped.** ⚠ The feather is likewise restricted to the **1px rim** touching
  the cut — feathering globally re-opens the same trap one level subtler. ⚠ **Verify a cut-out over white and
  magenta, never over the dark ground it came from**: a leak, a hole or a dark halo is invisible against
  near-black.
  ⚠ **The icon is cropped tight with NO padding** (the About mark keeps its 5% pad): an icon is drawn small in
  dense chrome, and the empty margin that flatters a 128px slot just makes a 16px icon look shrunken.
  Verified rather than assumed: both shipped assets found by payload search inside `EmberTern.dll` (and both
  masters correctly *absent*), the new mark embedded in the built `.exe`, and the **live** window returning a
  non-zero `ICON_BIG` from `WM_GETICON` — the proof the headless test cannot give.
  **⚠⚠ TWO GDI+ TRAPS MAKE A CORRECT `.ico` LOOK CATASTROPHICALLY BROKEN — gotcha #299, and the lesson is
  general.** `Icon.ToBitmap()` returns **colour noise** for a PNG-compressed entry (it decodes the frame as a
  DIB), and `new Icon(path, Size(256,256))` **returns the 64px frame** (GDI+ never selects PNG-compressed 256
  entries). ⭐ **What settled it in one step was running the same inspection against the PREVIOUSLY SHIPPED
  icon, which reproduced both symptoms identically** — the known-good artefact is what distinguishes *"my file
  is broken"* from *"my tool is lying"*, the same shape as #214, where a NOWAIT failure was mistaken for a
  Firebird prohibition. Real verification walks the `ICONDIRENTRY` table and decodes each payload with
  `Image.FromStream` (declared size == decoded size · PNG signature · `Format32bppArgb`); neither API above
  can express that. Neither trap affects the app — Avalonia decodes with Skia, the shell decodes itself.
  **⛔ NOT done, by scope:** no per-window icon overrides.
  ⚠ **Stated, not tested: the titlebar's "nothing left over" property is visual QA.** The test that would have
  covered it constructed a `MainWindow` and **hung** — see the new datum under "Tests"; it was cut rather than
  fought, on the standing instruction that the suite hang is its own infrastructure task.

- **⚙ SETTINGS CENTER & SQL FORMATTER CASING — ACTIVE SPRINT. Etap 1 (audit + design) CLOSED AND RATIFIED
  2026-07-29; ⭐ ETAP 2 (Core foundation) and ⭐ ETAP 3 (the window + the complete General page) both
  DELIVERED AND USER-ACCEPTED 2026-07-29; ⭐ ETAP 4 (the formatter's two casing settings) DELIVERED AND
  USER-ACCEPTED 2026-07-30; ⭐ ETAP 5a (the export FORMAT — Core only) DELIVERED AND USER-ACCEPTED 2026-07-30;
  ⭐ ETAP 5b (the export/import UI + the write into `settings.dat`) DELIVERED 2026-07-30 and, after a QA round
  that produced **three findings — one functional (§16.6) and two UX (§16.7) — DELIVERED AND USER-ACCEPTED
  2026-07-31**; ⭐ **ETAP 6 (the approved §7 settings — ratified Q9) DELIVERED AND USER-ACCEPTED 2026-08-01.**
  **🔒 THE SPRINT IS CLOSED — all six etaps delivered and accepted, merged to `master` (`--no-ff`, so the
  sprint's arc stays readable) and pushed to both remotes.** A closing compliance audit checked every point of
  §6, §7, §9 and §11 against the code: each is built, deliberately deferred with its measurement, or ratified
  out with a reason, and **no global user preference is left outside `Preferences`** (verified in code —
  `WorkspaceState` now holds only gesture-set layout, per-tab state and content). Build 0/0; suite **7026**
  green (partitions 6959 + 67, up 38); smoke clean.
  **The sprint's one document: [docs/design/settings-center.md](docs/design/settings-center.md)** — read
  §9 (the 13 ratified decisions), §2 (the measured facts) and the as-built sections **§12 (etap 2) + §13
  (etap 3) + §14 (etap 4) + §15 (etap 5a) + §16 (etap 5b) + §17 (etap 6)** before writing any code.
  **⭐ ETAP 6 — the six approved §7 settings. See "What's built" for what it IS; the notes here are the WHY.**
  Three new categories (Editor · Grid · Debugger), ten new rows, `Preferences` 4 → 14 properties, and
  **`CurrentSchemaVersion` still 2** (additive).
  **⚠ TWO §7 ITEMS DID NOT END WHERE THE PLAN LEFT THEM, both on the user's call, and both are AMENDMENTS to
  ratified Q9 rather than drift.** **(a) 7.4 (grid auto-fit) was BUILT** although Q9 listed it as *"trim
  first"*. **(b) ⭐ 7.1 (monospace font) LEFT THE SPRINT ENTIRELY** — Q9 had it as *"prerequisite only"* and §10
  said to *"fold it into whichever etap has room"*, both resting on §2.7's count of *"four divergent strings
  across ten files"*. **Re-measured before committing to it: 7 distinct strings, 95 occurrences, 33 files** —
  so it is neither small nor independent, it would decide `Cascadia Code` vs `Cascadia Mono` for every code
  surface at once, and it sits squarely inside the standing *"a module etap delivers the module — do NOT
  initiate global UI changes or style refactors"* instruction. It moved to the backlogged app-wide UX sprint
  **with its measurement**. ⚠ **§2.7, §7.1, §10 and Q9 were all amended in place** — the point is not left
  unexecuted anywhere.
  **⭐ THE FIRST NON-STRING PREFERENCES, and why that is not a breach of §5.2.3 (§17.1).** Ten of the fourteen
  properties are now `bool`/`int`. ⛔ **Do not "fix" them into strings for consistency**: §5.2.3 is about
  **enums specifically** — an unknown enum name throws → `Corrupt` → `Save` refuses → *the whole settings file
  is lost* — and that reason does not transfer to a `bool` (the set of legal JSON booleans never grows) or an
  `int`. ⚠ What a number **does** need is bounds: new **`PreferenceRange`**, the numeric sibling of
  `PreferenceOptionSet`, because §5.2.1/2 makes normalization *silent and total*, and a hand-edited or
  imported `0` row limit would make the SQL editor return nothing. ⭐ **Out of range CLAMPS, never resets** — a
  stored `50 000 000` means *"as many as possible"*, and answering it with the shipped `5 000` would be data
  loss with extra steps (the `"dark"` → `"Dark"` reasoning, §12.2) — **and the field echoes the clamped value
  back**, because a box still showing `50000000` over a stored `1 000 000` would be lying and a validation
  error has nowhere to live under apply-on-change. ⭐ `FullSafetyCeiling` is the **ceiling of the two ranges
  that are configurable**, so *soft < ceiling* holds by construction, not by comment; it stays
  non-configurable itself (Q9 — a configurable memory backstop is not a backstop).
  **⭐ §7.6 — THE MIGRATION OUT OF `WorkspaceState`, AND THE WRITE-BACK IS DELETED (ratified, §17.2).** The four
  `*EasyMode` flags were written by whatever editor the user last toggled, so opening a procedure in Easy mode
  because of something done to a *different* procedure looked like a bug. Four `PropertyChanged` handlers and
  four settable properties are **gone**; the four are now read-only and read the live `PreferencesService` when
  a tab is built. ⚠ A restored tab's own `WorkspaceTab.EasyMode` still wins — that half was never the problem.
  ⛔ **Do not re-add a global flag to `WorkspaceState`**; the removed block carries a comment saying so, because
  *"remember the last-used mode"* is a plausible-sounding regression.
  **⚠⚠ §7.5 — "GATE RESTORE, NEVER CAPTURE" HAD TWO WAYS TO DESTROY DATA, and both are the obvious
  implementation (§17.3).** (a) **Not LOADING the stored workspaces** would have silently erased every *other*
  connection's tabs **and saved queries** at the next close — the dictionary `RestoreWorkspace` fills is the
  very thing `CaptureWorkspace` writes back. (b) **"Do not restore the workspace"** would have discarded
  **saved queries**, which live inside the same stored `ConnectionWorkspace`; *"start me clean"* is about a
  stale tab strip, not about throwing away named SQL. So the dictionary loads either way and only the *tabs'
  materialisation* is suppressed. ⭐ **The suppression is a set of profile ids that EMPTIES as it is read**, not
  a standing flag: the setting says *on startup*, so a reconnect later in the same session must restore the
  tabs **this** session built. All three pinned by name.
  **⭐ §5.5.1's NUMERIC COMMIT PATH — the debt §16.8 recorded, now paid (§17.4).** `EditText` follows every
  keystroke and commits **nothing**; the view calls `Commit()` on **blur** and **Enter**, which parses, clamps
  and echoes back. ⚠ The reason is not performance: every `Save` fully reads + decrypts + rewrites
  `settings.dat` and rolls the **single** generation of `settings.dat.bak`, so per-keystroke saving destroys
  the one hand-recovery net exactly while someone edits settings. ⚠⚠ **A numeric control must carry its ROW as
  its `DataContext`** (the shipped `DataImportTabView` idiom) — all three hooks identify what to act on from it,
  and on the page's DataContext the field types correctly, shows the number and **calls nothing**.
  **⭐ QA FOLLOW-UP (2026-08-01) — the fields take DIGITS ONLY, and two designs were measured and rejected on
  the way (§17.4a).** One predicate, `NumericSettingViewModel.AcceptsText`, enforced by the view at the input
  boundary; it judges a **partial** entry (empty and a lone `-` are legal steps) and deliberately does **not**
  check the range, since typing `1` toward `1000` would fail a minimum of `10`. ⚠⚠ **Rejected: vetoing inside
  the `EditText` setter** — measured to fail twice: Avalonia's two-way binding **ignores a `PropertyChanged`
  raised while it pushes target → source** (the box kept showing the refused text), and it makes **paste
  strictly worse** (`Commit` finds the model already correct, notifies nothing, junk stays forever). ⭐ **The
  general shape: the property a control writes to cannot also be the property that refuses the write.**
  ⚠⚠ **Rejected: capping length at the RANGE's width** — it silently broke §17.1's promise, since typing
  `50000000` into a million-max field means "as many as possible" and must clamp, not be refused at the 8th
  keystroke; the cap is **`int`'s** width and `Commit` parses as **`long`** before clamping. ⚠ **The gate is a
  TUNNEL handler and here that IS justified** — a `TextBox` genuinely consumes `TextInput`, and class handlers
  run **before** instance handlers, so a bubbling attribute fires after the character is already in. Hence the
  same field has **Enter on the bubble and TextInput on the tunnel** (gotcha #298). ⚠ **Paste stays unblocked
  by decision** (no `TextInput`; undone at blur/Enter), pinned by a test so it is on record.
  **⚠⚠ A MEASURED CORRECTION TO AN IN-FLIGHT ASSUMPTION — a `TextBox` does NOT claim Enter (§17.5).** Mid-etap
  the Enter path was diagnosed as *"TextBox handles Enter in its own class handler, so a bubbling `KeyDown=`
  never fires"* and a **tunnelled window-level handler** was added on that basis. **Probed on the headless
  session before closing: false** — with `AcceptsReturn=false` the bubbling handler runs with `Handled=false`
  and the key reaches the window still unhandled. The real cause was the `DataContext` above. ⭐ **The tunnel
  was removed**; Enter is an ordinary bubbling handler, symmetric with the blur one. This is §14.3's shape
  again — *a false premise with a working conclusion* — and it matters for §15.7's reason: **an inert guard
  reads to the next author as a real hazard.** Gotcha #224's tunnel is for a key an editing control genuinely
  claims; this was not one.
  **⚠ FOUR as-built decisions later work must not undo (§17.6):** (a) ⭐ **`SettingValueKind`
  (`Option`|`Toggle`|`Number`) is separate from `SettingKind`** — exactly the distinction §16.2(f) predicted;
  one answers *value or command*, the other *what shape*, and reusing `Options == null` would conflate them.
  ⚠ No `Text` member — a free-text preference has no consumer (#233) · (b) **three mapping methods
  (`ValueOf`/`FlagOf`/`NumberOf`), not one returning `object`** — each throws for an id it does not know, so a
  new row fails loudly instead of silently reading the wrong kind · (c) ⭐ **the debugger isolation is a SEED at
  construction, deliberately NOT a live provider** (the opposite of the formatter's style, §14.2e): the launch
  panel's selector is the user's per-launch choice, and re-reading later would move one they may already have
  touched — whereas **grid auto-fit IS a provider**, because grids get built while the preference moves ·
  (d) ⭐ **`MainWindowViewModel.Request(...)` is the ONE place an `ExecutionRequest` gets the limits** —
  `ExecutionDefaults`' own comment predicted this and was right; one builder is what makes a *fifth* execution
  surface inherit the limits instead of quietly shipping on the defaults.
  **⚠ TWO MEASURED CORRECTIONS TO THE SPEC, both recorded rather than acted on:** **§7.7 has FIVE grid page-size
  `200`s, not two** — the other three are the **client-side** result grids (SQL editor, Procedure exec, Function
  exec), which page an already-materialized result and are **out of scope by ratified Q9**, stated in the row's
  own label; ⚠ one consequence had to be fixed in place — `MainWindowViewModel.ResultPageSize`'s comment claimed
  *"same page size as the Table Data View (DataPreviewRowLimit)"*, true while both were literal `200`s and false
  the moment one started following a preference. **§2.7's font count was off by 3× in files** (above).
  ⚠ **Deliberately NOT built in etap 6 (§17.8):** §7.1 · §7.8 timing constants · §7.9 suppressible confirmations
  · §7.10 import defaults · page size for the three client-side grids · `FullSafetyCeiling` · *Restore defaults*
  (still §13.4's reasoning) · a `CommandId` or `Ctrl+,` (§5.6, unchanged through six etaps) · **any change to
  etap 2–5b contracts** — `CurrentSchemaVersion` still 2, the export format, the one theme apply point and
  `SqlFormatter`/`FormatterStyle` all verified untouched, and `aes256-passphrase` still unregistered in
  `ResolveProtector` (§15.1).
  ⚠ **The new preferences travel in the export for free** — they are properties of `Preferences`, which the
  export's `Preferences` section already carries whole; no format version moves and the reflection guard had
  nothing new to record.
  **⭐ ETAP 5b — the etap that connected the feature to the user. See "What's built" for what it IS; the notes
  here are the WHY.** Build 0/0; suite **6988** green (partitions 6924 + 64, up 28); smoke clean.
  ⚠ **The headless partition held THREE classes at that point** (`ConnectionExpandBindingProbe` ·
  `SettingsCenterViewTests` · `ContextMenuPresentationTests`), all in `HeadlessCollection`. ⚠ Historical: the
  third was later folded into the probe and **no longer exists** — the live list is under "Tests" below.
  **⚠⚠ THE QA DEFECT AND ITS ONE-SENTENCE CAUSE (2026-07-31, §16.6) — the generalisation outlives this module.**
  An exported workspace did not survive a restart. ⭐ **Cause: `SettingsPortability.ExportTo` read the whole
  configuration off `settings.dat`, which is correct for every section EXCEPT `Workspace` — the one section the
  app does not write when the user changes it** (it has exactly ONE writer, `MainWindow.OnWindowClosing`). So a
  mid-session export carried the *previous* session's tabs, and then imported and restored perfectly faithfully.
  ⭐ **The rule worth keeping: *"read the persisted state" equals "read the current state" only for state that is
  persisted eagerly* — one deferred section breaks it silently, because the operation still succeeds end to end.**
  ⚠ **Four suspects were cleared by tracing before anything was changed** — the reader, the merge, the write, and
  `SuppressWorkspaceCaptureOnClose` were all correct; the file simply described the wrong session.
  **Fix: `SettingsPortability.CaptureLiveWorkspace` (a `Func<WorkspaceState>?`, mirroring `AfterImport`), supplied
  by the new `MainWindow.CaptureLiveWorkspaceState()` — ⭐ extracted from the close path so there is ONE builder of
  "the workspace right now", never two.** ⚠ It had to be the **View's**: sidebar width, results-panel height and
  the import panel live on controls, so `CaptureWorkspace()` alone would have exported default layout. ⚠ Still
  **read-only** — the capture lands in the in-memory copy handed to the exporter, never in `settings.dat` (ratified
  §15.9/4; persisting it was the easy wrong fix). ⚠ The hook is wired **outside** MainWindow's run-once restore
  guard, because an unset hook fails *silently* by falling back to the stale read. ⚠ **Stated, not fixed: grid
  column WIDTHS have the same shape** (flushed only at close), which is a `GridProfiles` staleness this sprint
  deliberately does not reach into. Verified by planting the violation — the test fails pre-fix with the exact QA
  symptom.
  **⚠ TWO UX FINDINGS FROM THE SAME QA ROUND, AND BOTH PRODUCED A REUSABLE PIECE (§16.7).**
  **(a) A blocked gate must speak the app's severity language.** *"The two passphrases are not the same"* was a
  plain `SubtleForegroundBrush` line — which is exactly what `MessageSeverity.Info` looks like — so a real input
  error read as guidance. New **`App/ViewModels/DialogGateHint.cs`**: text + severity, with the colour and icon
  read from **`MessageBanner`'s shared map** (the `ImportReadinessItemViewModel` precedent, whose own comment says
  *"a greyed-out button with no reason is a UX defect"*). ⛔ Never paint a gate hint with a local `ErrorBrush`.
  ⚠ **Two severities on purpose:** `Error` = what is there is wrong, `Warning` = a required step is outstanding —
  blanket red would make a freshly-opened dialog red before the user has done anything. ⚠ **Not a second
  `MessageBanner`**: each dialog already has one for the *outcome*, and a bar that appears and disappears would
  resize a `SizeToContent` window, i.e. cause finding (b). ⚠⚠ **A latent defect surfaced while wiring it —
  gotcha #296: five of seven section flags notified `CanExport` but not the reason, so unticking everything
  disabled Export and the *"select at least one thing"* line could never appear.** Silent, green build, and
  invisible to any test that *reads* the property rather than listening to `PropertyChanged`.
  **(b) A `SizeToContent` dialog grows DOWNWARDS from where it already is** (gotcha #295) — `CenterOwner` centres
  once, at the opening size — so the import dialog's *Import* button went under the bottom edge once the section
  list appeared. New **`App/Behaviors/GrowingDialogBehavior.cs`**: a **ceiling** (`MaxHeight` from the screen's
  working area) plus a **nudge** back on screen, and it only works because the dialog bodies are now in a
  `ScrollViewer` with the footers **outside** it — a cap without one clips instead of scrolling. ⚠ Deliberately
  **not** re-centred (moving a dialog the user is reading is worse than the defect); rejected too: "open it
  higher" and "make it resizable". ⚠ **The units are the trap** — `Position`/`WorkingArea` are physical pixels,
  `MaxHeight`/`ClientSize`/`FrameSize` are DIPs — so the arithmetic is a pure static, unit-tested including a
  working area that does not start at the origin. ⚠ **Sixteen dialogs use `SizeToContent`; only these two were
  touched** — wider adoption belongs to the backlogged app-wide UX sprint, not to a settings etap.
  **⭐⭐ THE TRAP THE BRIEF NAMED WAS REAL, AND THE FIX REUSED AN EXISTING SEAM RATHER THAN ADDING ONE (§16.1).**
  An import writes `settings.dat` directly, so every in-memory holder loaded from it is stale — and ⭐ **the damage
  is the NEXT WRITE, not the stale read**: `PreferencesStore.Save` persists a *whole* `Preferences`, so the next
  preference the user touched would carry the pre-import copy of every other field back to disk, silently, with a
  green build. The holders were **measured, not assumed**: `PreferencesService._current` and
  `MainWindowViewModel._folderState` are live snapshots (both reloaded); connections have none but the tree must be
  rebuilt (and it reads `_folderState`, so the order is not arbitrary); `GridProfileStore.Get` reads per call (an
  imported layout lands when that grid is next built — stated, not hidden); `ParameterHistory`/`DebugWatches`
  cannot be exported at all. ⭐ **`PreferencesService.Reload()` applies nothing** — it re-reads and raises
  `Changed`, so an imported theme repaints through the app's ONE apply point (§13.2) and the import adds no second
  one. ⚠ **The test is written to fail before the fix and was verified by planting the violation**: it does not stop
  at *"the service sees the imported theme"* — it then changes a **different** preference and requires the imported
  one to survive that write, which is the only half that catches a missing reload.
  **⚠⚠ TWO NEW GOTCHAS, both wider than this module.** **#292 — when a format omits a field by EMPTYING it rather
  than leaving it out, a merge that copies the incoming record wholesale DELETES the local value, and the deletion
  looks like the feature working.** The export omits a password by writing `Password = ""`, so the obvious
  merge-by-`Id` would erase a working credential as a side effect of importing a host name; nothing fails, and the
  box is simply empty days later. ⭐ **The division of labour to keep: `SettingsExporter.BuildContent` decides
  whether a field TRAVELS (reflection-guarded), `MergeConnections` decides what happens to the LOCAL value — two
  questions, two places, both needed for the same field.** **#293 — a "preserve the old file first" step must be a
  COPY when the operation MERGES and a rename only when it overwrites**; §6.3.4 inherited *"rename aside"* from
  `SaveOverUnreadableFile`, which writes over empty ground, whereas an import's preserved file **is also its merge
  base** — moving it makes every unselected section come back as a default while the operation reports success.
  Its second half is why `ApplicationSettingsStore.CanSave` exists: copying before a write that is then **refused**
  would leave a rescue file for an operation that never happened, and the only alternatives were a delete branch on
  a rule #11 surface or clutter — so the order is refuse → copy → merge → write. **A guard you have to undo is a
  guard in the wrong place.**
  **⚠ THE LIVE-SESSION DECISION WAS MINE AND IS RECORDED IN §16.3: apply immediately, disclose honestly, block
  nothing — except that the ONE section which cannot survive the session gets its overwrite suppressed.** Rejected:
  *"import requires no active connection"* (a cost with no safety benefit — `ReloadConnections` already runs
  mid-session on every folder edit) and *"import applies after restart"*. ⭐ **`Workspaces` is different in kind, and
  applying it "immediately" would not have saved it**: `MainWindow` captures the live workspace on close, so a
  session that imported workspaces and then exited would write its own tabs straight over them — **the import would
  silently undo itself**. Importing that section therefore arms a one-shot `SuppressWorkspaceCaptureOnClose`.
  ⚠ **That is NOT §7.5's rule being broken** — *"gate restore, never capture"* is about a persistent preference, and
  its reasoning has no purchase on a session-scoped suppression following an explicit instruction to replace the
  stored workspace; ⛔ **do not turn it into a setting.** Also rejected: `RestoreWorkspace` into the live session,
  which would rebuild tabs under an open connection and transaction and discard unsaved editor work.
  **⚠ FOUR as-built decisions etap 6 must not undo (§16.2).** (a) ⭐ **`SettingKind` (`Preference` | `Action`) —
  the shape of a row that is a command**: leaving it out of the catalog would cost **search** (§5.4's promise is
  that "export" finds it, and search reads `SettingsCatalog.Settings`), while reusing `Options == null` as the
  marker would conflate *"not a preference"* with *"a preference that is not enumerated"* — exactly what etap 6's
  first numeric setting is. Etap 4's `EveryPreference_IsRenderedOrRecordedAsHidden` was **taught the distinction
  rather than exempted from it**, and it caught the new row on the first run · (b) ⭐ **`SettingsImportSelection` is
  its own type, not `SettingsExportOptions` reused** — the flags line up but the **defaults are opposites** (the
  export's defaults *are* §6.3.4's ratified classification; a selection has no defensible default because it depends
  on the file), and a flag for a section the file lacks means *"I would have taken it"*, not an error · (c) **an
  import that could not make its recovery copy REFUSES** — the copy is what makes the operation undoable by hand ·
  (d) **failure text is Core's, shown as-is** — both dialogs switch on the status and add no wording to `UiStrings`
  (§15.8).
  ⚠ **The dialogs are modals with a primary button, and that does NOT contradict Q8** — apply-on-change governs the
  preference *pages*; an export is a command producing one file with an outcome to report (precedent: the data
  `ExportDialog`). ⭐ **The passphrase lives INSIDE the import dialog rather than in a modal of its own**, which is
  what keeps §6.3.3's corollary readable: pick file → phase one runs immediately → the passphrase group appears only
  if `CanBeOpened`. The export's passphrase is **confirmed**, and the confirmation gates the button — a typo makes
  the file permanently unreadable and the error is undetectable afterwards, so this is the only moment it can be
  caught.
  ⚠ **Deliberately NOT built in etap 5b (§16.5):** export profiles / schedules / `.json` interchange (§9.1) · a
  `CommandId` or `Ctrl+,` (§5.6) · *Restore defaults* (§13.4) · a separate passphrase dialog (it is where the check
  order would have been inverted) · **any change to etap 5a's format**, to `Preferences`/`PreferenceOptions`/
  `PreferencesStore`/`CurrentSchemaVersion`, to the one theme apply point, or to `SqlFormatter`/`FormatterStyle` —
  all verified untouched, and `aes256-passphrase` stays unregistered in `ResolveProtector` (§15.1).
  **⭐ ETAP 5a — the format is settled and provable before any dialog exists, which is exactly what Q11's split
  was for. See "What's built" for what it IS; the notes here are the WHY.** Build 0/0; suite **6960** green
  (partitions 6901 + 59, up 176); smoke clean.
  ⭐ **RATIFIED BY THE USER ON ACCEPTING IT (§15.9) — two of these stop being implementation judgements:**
  (1) **F4's shape is the etap's most important decision**, because *"import uses exactly the same migration
  path as `settings.dat`, which eliminates the risk of two independent migration mechanisms that would
  eventually drift apart"* — so ⛔ **the payload being an `ApplicationSettings` is a ratified constraint: the
  export must never grow a migration ladder of its own**; (2) **the §15.1 deviation is ratified**, in the
  user's cleaner words — *`ResolveProtector` protects `settings.dat` **at rest**, whereas the export is a
  separate format requiring a credential from the user; registering `aes256-passphrase` there merely to return
  a misleading DPAPI message would be worse than an explicit, documented `null`* ⭐ (the general rule: **an
  at-rest scheme and a credential-bearing scheme are different kinds of thing, and a seam built for the first
  does not extend to the second**); (3) the two-phase API is ratified as the realisation of the
  already-ratified *"never ask for a credential that cannot possibly work"*, and binds 5b's wiring; (4) the
  deep copy is ratified — an export must under no circumstances modify the live settings.
  **⚠⚠ ONE DEVIATION FROM THE BRIEF, and it is a correction to a stale instruction rather than a shortcut
  (§15.1): `aes256-passphrase` is deliberately NOT registered in `ApplicationSettingsStore.ResolveProtector`.**
  Both the brief and `EncryptionSchemes`' reserved comment said to register it there — written before the export
  had an envelope of its own. `ResolveProtector` answers *"which protector decrypts **this settings.dat**
  payload"* and **has no passphrase**, so it could only return a protector that cannot decrypt: an export
  dropped in place of `settings.dat` would then be refused as *"could not be decrypted — written by a different
  Windows account"*, which is untrue, and §6.3.1b explicitly praises the current honest refusal. The protector
  an import needs is built **per file** from that file's own header. ⭐ **The general shape: the reserved note
  generalised from an AT-REST scheme (whose key the store can obtain itself) to one that needs a credential
  from the user, and those are not the same kind of thing** — so the instruction now says it holds for
  `aes256-machinekey` and not for this, and `ResolveProtector` carries an explicit commented arm instead of a
  fall-through. Pinned by a test.
  **⭐ F4 IS RESOLVED, AND THE ANSWER IMPROVED THE FORMAT (§15.2).** `MigrateToCurrentVersion` became
  `internal static` (it never used instance state), and — the load-bearing part — **the payload's settings
  travel as an `ApplicationSettings`**, so the import calls *the very method `LoadWithStatus` calls* and a
  future `Migrate_2_3` applies to imported files for free. ⛔ Never re-implement that ladder. ⭐ **`JsonOptions`
  had to become `internal` too, and that is a one-owner point, not a convenience**: `ApplicationSettings` holds
  three enums whose stable-name form comes from that options object, so an export with its own options would
  write them as **numbers** — two representations of one aggregate, free to drift, invisible until a file
  crossed between them.
  **⭐ THE `AppVersion` SEAM (§15.3a): Core takes it as a REQUIRED INPUT PARAMETER**, because Core cannot see
  `AppInfo`. ⭐ That is not a workaround for layering — it is the right shape for a field nothing may branch on:
  Core does not derive the version and therefore *cannot* condition on it, which makes §6.3.2's ⛔ *"diagnostics
  only"* structural rather than a matter of discipline. ⛔ **No literal version fallback in Core.**
  **⭐ THE READER IS TWO PHASES, and that is what makes §6.3.3's corollary structural (§15.3b).** `Inspect`
  runs checks 1–4 and takes **no passphrase**; `Open` takes an *inspection*, never a path — so etap 5b cannot
  wire the passphrase dialog as import's entry point. ⭐ **The assertion that proves the ordering is real and is
  worth reusing:** every phase-one failure is handed to `Open` with three different passphrases and the status
  must be **unchanged**. If no passphrase can alter the outcome, asking for one would have been asking for
  nothing.
  **⚠ FOUR as-built decisions later work must not undo (§15.4):** (a) ⭐ **the format-version ladder REFUSES on
  a missing step where `MigrateToCurrentVersion` stamps and continues** — same pattern, opposite correct answer:
  for `settings.dat` a missing step means a mislabelled current file, for an *import* it means a shape we cannot
  read, and claiming it is current would import whatever deserialized and drop the rest (**a partial import is
  worse than none**) · (b) **`OldestSupportedFormatVersion` exists so "too old" is decided from the HEADER** —
  whether a step exists is a fact about the version, not the payload, so it belongs *before* the passphrase
  prompt · (c) **the ladder operates on a `JsonObject`**, because a format migration is exactly the case where a
  field may have been renamed and the old shape may not deserialize into the current type at all · (d) **the
  exporter deep-copies through the store's own serializer and never mutates the live settings** — the guarded
  failure is stripping a password out of the export *and* out of the running app.
  ⚠ **The section names are STRINGS, not an enum** — they are persisted, so §5.2.3's ratified rule applies:
  an unknown enum name throws → the whole file is lost, whereas an unknown *string* is simply a section this
  build does not act on.
  ⚠ **Deliberately NOT built in etap 5a (§15.7):** every part of the UI · **nothing applies an import to
  `settings.dat` yet** (that is a rule #11 decision belonging with the surface that asks which sections to take)
  · Q2's *"refuse an unencrypted export with credentials"* clause (unreachable under Q3 — an unreachable guard
  reads as a real safety net) · any change to `settings.dat`'s own format.
  ⚠ **One test of mine was flaky and the lesson generalises — gotcha #291.** Asserting a **short** string is
  absent from an encrypted blob is flaky by construction: `"Lab"` is three Base64 characters and turns up in
  ciphertext by chance. Choose a needle the encoding **cannot** produce (the password fixture contains a `-`).
  **⭐ ETAP 4 gave `SqlFormatter` its first two user-owned decisions — see "What's built"; the notes here
  are the WHY.** Build 0/0; suite **6784** green (partitions 6725 + 59, up 762); smoke clean.
  ⭐ **The load-bearing result: the default output did not move.** All **459** existing formatter assertions
  pass **with no expected string edited** — they were deliberately NOT parameterised, because their whole
  value is being the unchanged byte-for-byte record of the shipped layout. ⚠ **Keep that property:** a future
  formatter change must not "update" those expectations to make itself pass.
  ⭐ **The user's own framing on accepting it, kept because two points are general rules (§14.4a):** *if the
  lexer already decides `Keyword` vs `Identifier`, that verdict should be the ONLY source of truth* — stated
  explicitly as **better than what the design originally described**, which is why §14.1(b) is now the
  ratified shape rather than a happy accident; and **`FormatterStyle` stays a pure Core model with no
  persistence mixed in** — preferences store only the keys, the mapping lives at the App ↔ Core boundary.
  **⚠⚠ TWO MEASURED CORRECTIONS TO §2.2 — do not re-derive them (§14.1).** (1) **§2.2(a) undercounted the
  casing sites threefold: ~30, not ~9.** It counted `ToLowerInvariant()` calls on token text but missed the
  **25 lowercase keyword literal sites the emitters SYNTHESIZE** (`"select"`, `"in"`, `"begin"`, `"end"`,
  `"union"`, `"from "`, …) — equally keyword-casing decisions, and left alone `Keywords: Upper` would emit
  `SELECT … in (1, 2, 3)`. The architecture was unaffected; only the definition of done was. ⚠ No §0 test
  would have caught it, because mixed-case output preserves every lexeme perfectly. (2) ⭐ **The
  keyword/identifier split needed no `IsKeyword` call at all** — `SqlLexer` already *is*
  `IsKeyword(word) ? Keyword : Identifier` and `MapToken` was discarding that verdict, so the split reads
  the token's own kind. Stronger than §6.4/4 asked: there is no second keyword **decision**, not merely no
  second list. ⛔ **Do not "improve" it by calling `IsKeyword` in the formatter.**
  **⚠ FOUR as-built decisions later etaps must not undo (§14.2):** (a) ⭐ **`FKind.Word` STAYS fused** —
  ~40 sites key on it for *spacing*, where "is this a word" is the right question; the classification is a
  second orthogonal field (`FWord`) read in ONE place, not a split of the layout kind · (b) **the style
  travels through ~40 emitter signatures and the churn is the point** — casing inside `Flatten` would leave
  `FToken.Text` styled while `Start`/`End` point at source (a permanent trap), and an instance-based engine
  would re-indent 2 000 lines of a §0 file; threading is **compiler-enforced** so nothing silently keeps
  the default · (c) ⚠ **SCOPE: the settings govern the Format SQL ACTION, not every `Format` call** — ten
  calls at seven locations follow the preference; `SqlCopyController` + Core's two `.sql` exporters (which *compose* DML) and
  `TraceEventDetailViewModel` (read-only display) keep the default, on ratified **Q1's own reasoning**. It
  is the reading that stops Copy-as-INSERT going upper while Export-to-.sql stays lower. ⭐ **RATIFIED BY THE
  USER on accepting etap 4 — do not re-litigate:** the preferences affect *the deliberate formatting of the
  user's code*; generators, exporters and data-presenting views keep their deterministic format, and **if it
  is ever wanted wider that is a single argument passed to those places, not an architecture change** · (d) **a provider, never a captured style**, and
  non-nullable with a real default (apply-on-change moves the value while tabs are open).
  **⚠ The §0 comment correction needed more than a one-line edit (§14.3).** *"Words are lowercased on
  output → compare case-insensitively"* had a **false premise and a true conclusion** — the shape that
  licenses a wrong simplification. ⛔ **An exact word compare would be silent and total:** `SELECT` vs
  `select` reads as a lost lexeme, the safety net fires, and **every re-cased statement reverts to
  verbatim** while every §0 assertion still passes. `UpperKeywords_ActuallyReCase_AndDoNotTripTheSafetyNet`
  asserts the output *changed* — the only assertion that can catch it.
  **⚠ One trap the UI paid for (§14.2f):** both formatter rows render the same two option labels, and a
  `RadioButton` group is keyed by name — **a shared `GroupName` would make choosing UPPER for keywords
  silently uncheck the identifier row**, so the two settings could never differ. Invisible to any
  view-model test; the headless case is what catches it.
  ⚠ **Deliberately NOT built in etap 4 (§14.5):** any third formatter option (§9.1) · *Restore defaults*
  (still §13.4's reasoning) · **a context-aware keyword/identifier split** — the classification is lexical,
  so `t.type` renders `t.TYPE` under `Keywords: Upper`; **semantically inert** (Firebird folds unquoted
  identifiers) and fixable by a local dot rule, but §6.4/4 ratifies the split as `IsKeyword`'s and an
  unratified heuristic on a §0 surface in this etap would have been the wrong place to be clever.
  **⭐ ETAP 3 shipped Settings Center and closed the theme gap end to end** — see "What's built" for what it
  *is*; the notes here are the WHY. Build 0/0; suite **6022** green (partitions 5964 + 58); smoke clean.
  ⭐ **The user's own framing on accepting it, worth keeping because it is the general rule:** with etap 2's
  whole-object `Save`, **two independent snapshots would sooner or later silently overwrite settings** — and
  it was better solved now than discovered after a few more Settings Center pages; likewise one apply point
  beats several places setting `RequestedThemeVariant`; and `App.axaml`'s `Dark` is a **startup technical
  detail, not a user default**.
  **⚠ FOUR as-built decisions etap 4+ must not undo (§13):** (a) ⭐ **ONE `PreferencesService` owns the live
  `Preferences` for the whole app** — a direct consequence of etap 2's ratified API (`Save` takes the WHOLE
  object, so a page commits a *settled* value): two snapshot holders **overwrite each other's fields**, and
  the concrete case is the titlebar toggle writing `Theme` while an open Settings Center still holds the
  pre-toggle copy. Silent, unlogged, green build · (b) ⭐ **`App` is the ONE place a theme is applied** — the
  startup read, the toggle and the Settings radio all only *write* the preference, and `App` subscribes to
  `PreferencesService.Changed`; two apply sites are two answers to *"what does Light mean"* · (c)
  ⚠ **`App.axaml`'s `RequestedThemeVariant="Dark"` STAYS** and is now commented: it is the bootstrap value
  between XAML load and the startup read, and deleting it leaves `ThemeVariant.Default`, which follows the
  **OS** theme (§2.1's trap) · (d) **the UI generates every option from `PreferenceOptions`** and each
  `SettingDescriptor` carries the option set *plus* its `UiStrings` labels, so §5.2.2's binding test is one
  local assertion (`EveryEnumeratedOptionHasALabel`) that also catches a label left behind by a removed
  option.
  **⚠ `PreferencesService.Apply` adopts the value EVEN WHEN THE SAVE IS REFUSED** — a refusal means *this
  file cannot be written* (audit A-03), not *this choice is invalid*; the surface that asked for the change
  is what must say it did not persist, which is what its `bool` return is for. Settings Center is the ONE
  place that silence is wrong; the titlebar toggle stays silent because MainWindow already carries the
  startup settings-health banner.
  **⚠ Deliberately NOT built in etap 3, each with a reason (§13.4):** *"Restore defaults"* per page (both
  General settings are one click from their default — it belongs with the first page that has enough rows),
  the blur-or-Enter commit path (§5.5.1 has no subject yet — both settings are discrete; the first numeric
  setting in etap 6 brings its own trigger), a second category (a category ships **with** its page), and
  any `CommandId` / `Ctrl+,` (§5.6, unchanged).
  **⭐ Etap 2 shipped the Core foundation and NOTHING else** — `Preferences` (4 scalars) ·
  `PreferenceOptions` (the ONE options table, holding the one-row **language catalog**) · `PreferencesStore`
  (8th facade) · `UserSettings.Preferences` (+11 lines, the whole schema change) · 32 tests. Additive,
  **`CurrentSchemaVersion` still 2**, zero App/Avalonia code touched.
  **⚠ FOUR as-built decisions etap 3+ must not undo (§12.1):** (a) **ONE options table**, not one class per
  preference — the `CommandCatalog` precedent; forty preferences must not become forty micro-classes ·
  (b) an option set is **one object whose ctor rejects a default outside its own values**, because §5.2.1/4's
  pin compares model against validator and both would read the same bad catalog — the symptom would be a
  preference that silently resets itself · (c) `Preferences` initializers **read the catalog's `Default`
  rather than repeating a literal**, so the default exists once · (d) ⭐ **`Validate` returns
  `source with { … }`, never a fresh instance** — a fresh instance silently resets any property someone
  forgets to list, turning *"I added a preference"* into *"that preference never persists"*, which is a
  data-loss shape; with `with`, an unlisted property passes through. Its cost (a forgotten *enumerated*
  property goes unvalidated quietly) is closed by `EveryPreference_IsAccountedForInValidation` — a declared
  table + a test, so **adding a property to `Preferences` fails the build until the author records whether
  it is normalized and against what**. Both guards were **verified by planting a violation**, including the
  ratified pin failing on §5.2.1/4's own `Language = "pl"` example.
  **⚠ Two contract details, so they are not re-derived:** normalization runs in **BOTH directions** across
  the file boundary (writing is also a crossing; `Validate` is idempotent so they cannot fight) · a
  *recognised* value is corrected to the catalog's spelling (`"dark"` → `"Dark"`), **not** reset — "silent
  and total" means never refusing, not preferring the default. And **F3 is settled as API shape**:
  `PreferencesStore` has **no per-property setters**, so etap 3 cannot stream keystrokes into a file whose
  every save costs ~7 file ops + 2 DPAPI round-trips and rolls the single-generation `.bak`.
  **⚠⚠ MEASURED CORRECTION THAT LANDED WITH ETAP 2 AND BELONGS TO ETAP 5a (§6.3.1b) — `settings.dat`
  ALREADY carries the magic `EMBERTERN-SETTINGS`.** `SettingsFileContainer.Magic` has held that exact
  literal, with a cleartext version + scheme header read before decryption, since the container shipped —
  so §6.3.1a's *"settings.dat does not get a magic"* is **false** and §2.4's *"no new crypto plumbing"* is
  **understated** (the envelope pattern is implemented and `public`). The consequence is a **collision, not
  a convenience**: Q10's magic exists to answer *"is this even our file?"*, and if both formats begin with
  the same bytes, step 1 of §6.3.3's ordered checks **cannot tell them apart** — a user who picks
  `settings.dat` in the import dialog would be **asked for a passphrase** and told *"wrong passphrase"*
  about a file that never had one, which is the exact outcome the check order exists to prevent.
  ⭐ **RESOLVED THE SAME DAY — RATIFIED DECISION Q13 (user, on accepting etap 2), binding on etap 5a and not
  to be re-litigated: the export gets its OWN unambiguous magic, `EMBERTERN-SETTINGS-EXPORT`, independent of
  `settings.dat`'s**, so the first header read alone determines the file's type and *"never ask for a
  credential that cannot possibly work"* holds. Every other Q10 property is unchanged (self-documenting
  first line, **never versioned**, read from the stream, byte-compared) and **`settings.dat`'s shipped
  format is untouched** — `SettingsFileContainer.Magic` stays exactly as it is. Identity is **per format**,
  not per product.
  **⭐ RATIFIED, do not re-litigate:** all scalar preferences live in a new **`UserSettings.Preferences`**
  (additive — **`CurrentSchemaVersion` stays 2**, because a bump trips downgrade protection and older
  builds then refuse the whole file), stored as **strings never enums** — ⚠ **and the reason is NOT rule #1**,
  which is refutable (three Core enums are already persisted in that same file via `JsonStringEnumConverter`);
  the durable reason is that an **unknown enum name THROWS** → `Corrupt` → `Save` refuses → **the whole
  settings file is lost**, whereas a string normalizes. ⭐ **Yields the rule: adding a VALUE to a persisted
  enum is NOT an additive change, even though adding a property is** (§5.2.3) · ⭐ **one source of truth for
  an enumerated preference's legal values** — Core declares the option set, the validator consumes it and the
  **UI generates its items from it**; two lists drift silently in the dangerous direction (the user picks an
  option that reverts on next load, and nothing fails), §5.2.2 · ⭐ **apply-on-change means on CHANGE, not per
  keystroke** — discrete controls commit immediately, **text/numeric commit on blur or Enter**, because every
  `Save` does a full read+decrypt+deserialize before rewriting and rolls the single-generation
  `settings.dat.bak`, so per-keystroke saving destroys the pre-edit backup while editing (§5.5.1) · ⭐ **`Preferences`
  is a SELF-SUFFICIENT CONTRACT and `PreferencesStore` only validates** (§5.2.1, binding): every property is
  valid from its own initializer so `new Preferences()` is always usable and *is* "restore defaults"; the
  store owns **validation + normalization of what it read from the file** (unknown value → the model's
  default) and supplies **no defaults of its own**; no property may be "nullable meaning unset" (that hands
  the default decision to every reader), normalization is **silent and total** (never refuse to load), and
  it must **not** rewrite the file on load (a writing `Load` is audit A-03's shape). The pinning test is
  `Validate(new Preferences())` == `new Preferences()` — it fails the day a property's initializer would be
  rejected by the validator, drift that is invisible in either half alone. **`Language` is validated from
  day one despite having no consumer** — it is the property most likely to be left until "it matters", and
  §8 is far enough off that a bad value would be entrenched by then · Settings
  Center is a **window** (not a `WorkspaceTabKind` — a tab would need threading into five per-kind
  families for no gain) · **apply-on-change, no OK/Cancel**, and it MUST surface `Save`'s refusal (§2.5)
  because a dialog that accepts a change and persists nothing is the worst place for that silence ·
  **no `CommandId` and NOT `Ctrl+,`** (an unratified gesture would have to pass the collision validator
  and appear in Keyboard Shortcuts) · the formatter gets **exactly two** settings, Keywords + Identifiers
  casing, both defaulting to `Lower` so shipped output is byte-identical.
  **⛔ STANDING DIRECTIVE (user, on accepting the design): nothing is added because it might be wanted
  later** — no update check, no telemetry, no experimental toggles, **no formatter options beyond those
  two**. The test is *"is the next step scheduled?"*, not *"would this be useful someday?"* **Language is
  the sole exception** because Polish is planned. Also ratified OUT, as decisions rather than gaps:
  configurable editor **timing constants** (tuned values — a user setting debounce to 0 experiences the
  editor as broken and reports it as our bug), **suppressible confirmations** (a "don't ask again"
  checkbox exists only to disarm rule #11), and **import batch defaults** (measured I0 optima, already
  per-profile, module closed).
  **⚠ FOUR MEASURED FACTS THAT REVERSE THE OBVIOUS ASSUMPTION — do not re-derive them.** (1) **The theme
  is never saved, not "reset on restart"**: `App.axaml:4` hard-codes `RequestedThemeVariant="Dark"` and
  `MainWindow.axaml.cs:1564` flips it in memory only — a *missing feature*, so removing that hard-coded
  `Dark` without first supplying a stored value yields `ThemeVariant.Default`, which follows the **OS**
  theme and reads exactly like a regression. (2) **`SqlFormatter` has no casing decision point at all** —
  casing is applied at ~9 sites and `FKind.Word` (`:360`) fuses keywords + identifiers + parameters, so
  the two requested settings are *not separable* until that classification exists (via the existing
  `FirebirdSyntax.IsKeyword`, never a second keyword list); the §0 lexeme net at `:2011` stays correct but
  its comment *"Words are lowercased on output"* goes false and must be fixed in the same etap, or someone
  "simplifies" it to a case-sensitive compare and disarms §0 for every keyword. (3) **Localization is NOT
  built** — 1 815 `public const string` (a `const` is *inlined*, so there is no field to reassign), 42
  XAML files on `x:Static`, zero `.resx`, and architecture rule #6 pointed *away* from it; the Language row
  therefore ships as **live storage over a one-row catalog and no mechanism**, and Polish is its own
  milestone whose scope includes finding a new discriminator for the `const`-keyed guard behind gotcha
  #284. (4) **`UserSettings` holds four lists and not one scalar**, which is why every existing scalar
  preference ended up in `WorkspaceState` beside window bounds.
  **⭐ The export format is EmberTern's OWN versioned artifact, and the four version-ish fields each have
  exactly ONE job** (the user's framing, and the reason the design holds): **`Magic`** (`EMBERTERN-SETTINGS`,
  literal first bytes) = identity, **never versioned** — versioning it would make an old file report *"not
  an EmberTern file"* instead of *"older export, migrating"*; **`ExportFormatVersion`** = the migration
  contract; **`SchemaVersion`** = the `ApplicationSettings` shape (the existing ladder); **`AppVersion`** =
  **diagnostics only, never branched on** (gotcha #289's shape), written from `AppInfo` never a literal.
  **Every export is encrypted** (AES-256-**GCM**, so a wrong passphrase fails as *authentication* and is
  distinguishable from a damaged file), cleartext header over encrypted payload — **which is what makes
  versioning possible at all**, and the section list stays inside the payload so a cleartext
  *"contains: Passwords"* never advertises what is worth attacking. ⚠ **The check order is itself the
  design**: magic → format version → scheme → **only then the passphrase** → GCM auth, so we *never ask
  for a credential that cannot possibly work*; the passphrase dialog must therefore not be import's entry
  point. ⚠ Do **not** collapse `ExportFormatVersion` into `SchemaVersion` (it would force a schema bump
  every time a section is added), and do **not** implement Q2's original *"refuse an unencrypted export
  containing credentials"* — always-encrypted makes it **unreachable**, and an unreachable guard reads as
  a real safety net to whoever comes next.
- **☰ HAMBURGER NAVIGATION / APPLICATION MENU — CLOSED, USER-ACCEPTED AND MERGED TO `master` (2026-07-29).**
  All five etaps accepted; build 0/0; suite **5971** green (partitions 5917 + 54); smoke clean. Merged `--no-ff`
  so the sprint stays one readable arc, pushed to **both** remotes.
  **What it IS is in "What's built" above** (Application Menu · About · Keyboard Shortcuts · notices). The notes
  below are the WHY — decisions and traps, not history. **The sprint's one document:
  [docs/design/hamburger-navigation.md](docs/design/hamburger-navigation.md)** — read it before touching the
  menu, the About window, the notices or `CommandDescriptor.Title`.
  **⭐ RATIFIED, do not re-litigate:** version **`0.5.0`** and **0.x is deliberate** — 1.0 arrives with the
  finished product and its licensing system, possibly preceded by a Beta suffix · About is a **product** window,
  **no environment/diagnostic block**, no library names on its face · **no liability, warranty or privacy wording
  anywhere at this stage** (liability is a term of the future EmberTern licence and belongs there, in one
  document) · `CommandDescriptor.Title` is the ONE canonical command name for surfaces that *list* commands, text
  from `UiStrings`, **no literals in `CommandCatalog`**; a `Description` field was **declined** until it has a
  consumer (#233) · Keyboard Shortcuts' canonical order is restorable after any column sort.
  ⚠ **No `CommandId` was added for the menu's rows, and that is a decision:** none shows a shortcut, so by
  `CommandId`'s own admission rule — *"a command earns an id only when a shared surface must speak about it"* —
  they do not qualify yet. They earn ids when a Command Palette needs them; four `Command="{Binding …}"`
  substitutions, not a rebuild. ⚠ **A row never ships ahead of what it opens** — each arrived *with* its window,
  because a row that opens nothing is indistinguishable from a defect in QA.
  **⭐ LICENCE FINDINGS (verified from artefacts — nuspec + licence files vs the DLLs in `bin`; re-verify the same
  way, do not trust this summary alone):** everything shipped is **MIT except `FirebirdSql.Data.FirebirdClient`
  10.3.4, which is IDPL 1.0** (MPL-1.1-derived file-level copyleft; §3.6 wants a source-availability notice with
  an executable distribution — EmberTern's own code stays a "Larger Work"). **Icons are Lucide, and its LICENSE
  carries TWO notices — ISC *and* MIT for the portions inherited from Feather**; the obligation follows the
  geometries in `IconGeometries.axaml`, not the excluded `.svg` files. **Inter** is the one genuine ambiguity (the
  package declares MIT and ships no OFL text while the typeface is upstream SIL OFL 1.1, and it *is* rendered) —
  listed under the package's declared licence with the credit recorded. Native Skia/HarfBuzz upstream notices are
  **flagged in the file's own Notes, not resolved**; `AvaloniaUI.DiagnosticsSupport` is excluded (Debug-only, and
  its package declares no licence at all).
  ⛔ **`Icon.Menu` IS CLOSED — user-accepted after three QA rounds; do not touch its geometry**, not to tidy the
  fractional coordinates, not to match a neighbour's ink box, not to adopt the upstream Lucide file. The two
  generalisations that DO apply to future icons are in
  [`Assets/Icons/ICONS.md`](src/EmberTern.App/Assets/Icons/ICONS.md) and as gotchas **#287/#288**: **repeated
  parallel strokes are spaced by a multiple of 1.5** in the 24-unit grid, and **the ink box DIAGNOSES optical
  size, it never dictates it**.
  **⚠ Four traps this sprint paid for, all now gotchas — read them before writing a similar guard or grid:**
  **#287/#288** the icon lessons above · **#289** a guard keyed to a value's *current* contents cannot catch a
  copy of an *earlier* one (the status bar showed a stale `0.1.0` while About read the assembly), and a
  plausible-looking regex can pass while matching nothing · **#290** a `DataGridTextColumn` gets no sort path from
  a **compiled** binding, so headers sort **nothing** without an explicit `SortMemberPath`.
  ⚠ **Still open, deliberately:** the monospace font-family string is duplicated across the app — centralising
  it is typography, which the backlogged app-wide UX sprint owns. ⚠⚠ **This line used to say "three copies
  (`HoverInfoView`, `LanguageExpansionController`, `ThirdPartyNoticesWindow`)" and was badly wrong: measured
  2026-08-01, it is 7 distinct strings / 95 occurrences / 33 files** — see the UX-sprint backlog entry above
  and settings-center.md §2.7. The estimate was low because one string dominates and the divergence is a long
  tail, which is exactly how a survey that samples the sites it has reason to look at goes wrong.
- **⌨ KEYBOARD MANAGER & CONTEXT MENU UX — CLOSED, USER-ACCEPTED AND MERGED TO `master` (2026-07-28).**
  Etaps 1–5 + a UX Consistency Pass, every one visually QA'd and accepted. Build 0/0; suite **5952 green**
  (full run in one pass, and in the two documented partitions 5903 + 49); smoke clean. Merged `--no-ff` so the
  sprint's history stays one readable arc, and pushed to **both** remotes.
  **The command system is now part of the app's architecture — see the "Keyboard Manager / command system"
  entry in "What's built" for what it IS; the notes below are the WHY, kept because several are decisions
  rather than history.**
  **⭐ UX CONSISTENCY PASS — one surface, one vocabulary.** The user's visual QA found Table Detail → Fields
  saying **"Add item"/"Remove item"** on the toolbar and **"New/Edit/Delete field"** in the menu, no **Edit**
  on the toolbar, and no **Move Up/Down** in the menu. One cause, not three bugs: the toolbar is the *shared*
  collection router (so generic labels) and the menu is per-grid (so specific ones). Fixed **at the router** —
  `ActiveCollection()` now returns a named `CollectionCommands` record carrying `Edit` **and the collection's
  own noun** (field/row/column/variable/item), so the toolbar tooltips are computed as
  *"New field · F3"* / *"Edit field · F2"* / *"Delete field · F8"* and cannot disagree with the menu.
  **⭐ The proof that Edit had been intended and dropped:** `UiStrings.FieldEditEditTooltip` — *"Edit selected
  field · F2"* — existed with **no consumer anywhere**. The string for the missing button was in the file.
  **⭐ THE LAST HAND-TYPED GESTURES IN THE APP ARE GONE.** The fields grid's `Insert`/`F2`/`Delete` were three
  local `DataGrid.KeyBindings` + three literal `InputGesture` attributes — the only entries in either guard's
  allowlist. Now catalog commands at Grid scope: `CollectionAdd` (F3, **Insert** alternate), **`CollectionEdit`
  (F2, new)**, `CollectionRemove` (F8, **Delete** alternate). Muscle memory keeps working; menus display the
  ratified keys. **Measured first:** Avalonia's `DataGrid` claims none of the three, so nothing relied on a
  local binding winning a race. **Both allowlists are now empty** — the finished state, not an oversight.
  ⚠ `Delete` at Grid scope coexists with the editor's own `Delete` (#282); Editor outranks Grid, so the caret
  decides — the case scopes exist for.
  **⭐ A machine found the icon drift the eye would have missed.**
  `TheSameMenuOperationAlwaysCarriesTheSameIcon` groups all **63 distinct menu operations** by their
  `UiStrings` label and requires one icon each — it caught **"Debug procedure"/"Debug function"** carrying the
  debugger's composite mark in the tree and a plain `Icon.Crosshair` in the Package Members menu. Also
  surfaced two toolbar-only operations in the menu of the same grid: Table Data **New/Delete row**, Session
  Manager **Open in SQL Editor / Analyze in Performance**. Deliberately NOT equalised (different sets on
  purpose): Trace start/pause/stop, Security bulk-vs-row scopes, grid refresh/pagination strips, trigger-group
  scope qualifiers. The rule applied is the user's — *the same surface offers the same basic operations
  whichever way you reach them*, not *every menu holds the same items*.
  ⚠ Seven menu items used **tooltip** constants (`CollectionAddTooltip`) as their `Header` — audit finding D6,
  which is how "Add item" became a menu entry. They now use label constants; the tooltip constants are gone.
  **⭐ Etap 5 — context menus. A CUSTOM CONTROL PROVED UNNECESSARY, and that was measured.** FluentTheme's
  **context-menu** `MenuItem` template already provides `PART_IconPresenter` (icons left),
  `PART_InputGestureText` (gestures right), the submenu chevron and the check mark — **and the icon column
  keeps its width when empty** (header presenter at x=28 either way), so labels already align. So "one shared
  menu control for the whole app" ships as **one shared style set** in `ControlStyles.axaml`, which is the
  *stronger* guarantee: a style needs no opt-in, while a control would have to be adopted by 32 menus and a
  33rd could forget. Rows **27px → 22px** (FontSize 14→12, symmetric padding — Fluent's was `11,4,11,7`),
  hover/selection off `SystemAccentColor` onto the app palette, subordinate gesture column, readable disabled
  rows, real separators. **130 of 133 menu items carry an icon, 21 carry a catalog gesture**; the 3 without
  are trigger-scope qualifiers whose parent carries the mark. Two markup extensions in `MenuMarkup.cs`:
  `Icon="{app:MenuIcon Icon.Trash}"` (existing geometries + the one `SvgIcon`; `Brush=DangerIconBrush` the
  destructive exception, bound dynamically) and `InputGesture="{app:CommandGesture Compile}"` (from the
  catalog). Three new icons only — `Icon.Redo` (Undo mirrored), `Icon.Cut`, `Icon.Paste`.
  **⚠ The rule the style creates, and it is Seam 4's `MessageBanner` lesson again:** a menu host sets
  `Header`/`Command`/`Icon`/`InputGesture`/`IsVisible` — **never** `Background`/`Padding`/`FontSize`/
  `Foreground`. A local value outranks a style setter, which is how the banner grew six per-host variants.
  **⚠ Two measurement traps recorded as gotchas #285/#286.** (1) Avalonia templates a **menu-BAR** `MenuItem`
  differently from a **context-menu** one; the first probe measured the bar item, reported *no icon or gesture
  part*, and would have justified building a control the framework did not need. A negative measurement is the
  dangerous kind. `MenuItem.InputGesture` is also **display-only** (measured — safe on every item, no
  double-fire). (2) `IClassFixture` creates one fixture **per class**, so a second headless test class
  silently produced a **second** `HeadlessUnitTestSession` (banned by #94/#226) and hung the suite —
  now an `ICollectionFixture` (`HeadlessCollection`), with the rule written on the fixture: join the
  collection, never add your own class fixture.
  **⚠ NEW, LOAD-BEARING DATUM FOR THE FULL-SUITE HANG (still out of scope, but it reframes four earlier
  observations):** with a different headless class running last, the hang reported **that** class's last
  test — at 5901 of 5902 completed, the identical shape. So the name tracks the **POSITION (the last headless
  test in a long run), not the test**: the four consistent sightings of
  `CompletionRow_HighlightsMatchedPrefix` were an artefact of ordering. The suspect is session teardown /
  dispatcher-loop shutdown. Start there, not at that assertion.
  **⭐ Etap 4 — a keyboard gesture is now written down in exactly ONE place.** `Commands/CommandTip.cs` is the
  ONE composer (`For` / `Gesture` / `Sentence` / `Format`); ~25 `UiStrings` members became `static readonly`
  and compose their gesture from the catalog. **The etap justified itself before it started:** etap 3 re-bound
  Format SQL to `Ctrl+K` and `ToolbarFormatSqlTooltip` went on reading *"Format SQL · Alt+F"* for a whole
  etap with a green build — a hand-typed gesture does not duplicate the catalog, it **goes stale silently**.
  ⚠ **The label text deliberately stayed in `UiStrings`** (rule #6) and is passed in: one `CommandId` serves
  **eleven** differently-worded Compile tooltips, so a single text field on the descriptor could not have
  served them. The catalog owns the gesture, `UiStrings` owns the words.
  ⚠ **`CommandTip.Format` is deliberately NOT `KeyGesture.ToString()`** — that spells the raw enum name, so
  `Ctrl+.` would reach the user as *"Ctrl+OemPeriod"*.
  ⚠ **`const` → `static readonly` was the cheap migration**: `x:Static` resolves both identically, so ~25
  strings centralised **without touching any of the 15 consuming XAML files**. Verified first that none is
  used in a `const` expression (the one thing that would break it).
  ⚠ **A gesture is shown ONLY where it works.** Tooltips carry gestures for `Global`/`Tab`-scoped commands
  only; the focus-scoped ones (`F3`/`F4`/`F8`) are NOT shown on toolbar buttons, because a tooltip promising
  `F3` on a button outside the tree/grid scope teaches something false — they belong in etap 5's context
  menus. The collection `+`/`−` buttons are the concrete case: same commands, deliberately no gesture shown.
  ⚠ **The rule is enforced, not remembered — and the guard keys on the DECLARATION, not the value**
  (`UiStringsShortcutSourceTests`): a correctly composed string also contains `" · F7"` at run time, so only
  `const`-ness distinguishes a literal from a computed one. **Verified by planting a violation** and watching
  it fail by name. Three exemptions, each with a reason + a test that fails when an exemption goes stale
  (`Esc`, Data Import's `Ctrl+V`, the fields grid's local `F2` — none is a catalog command). Gotcha **#284**.
  **⭐ Etap 3 shipped the whole ratified map** (`F3` New · `F4` Refresh · `F6` Commit · `Shift+F6` Rollback ·
  `F7` Compile · `F8` Delete · `Ctrl+K` Format · `Ctrl+W` Close tab) and **retired `Alt+F` with no
  exception**, pinned by `NoCommandUsesAltPlusALetter`. Two scopes joined: **`Tree`** (needed one small
  addition — `MetadataExplorerViewModel.SelectedNode`, fed by the sidebar's existing selection handler like
  `SelectedConnection`, deliberately NOT observable) and **`Grid`**, which needed **no per-grid knowledge at
  all** because `F3`/`F8` route through the app's *existing* unified collection router, whose
  `ActiveCollection()` already answers "which collection" and self-gates.
  **⚠ Nothing destructive became a one-keystroke action — verified, not assumed:** `F8` on a leaf routes to
  the node's own `DeleteCommand`, which raises the **existing confirm dialog**; `F6`/`Shift+F6` bind the very
  commands the toolbar buttons bind, with the same `CanCommitAll`/`CanRollbackAll`; `Ctrl+W` uses the
  confirming close.
  **⚠ A design trap worth not re-walking:** `CollectionAdd`/`Remove` were first placed at *Global* scope
  (their commands live on `MainWindowViewModel` and self-gate). Subtly wrong — with a table leaf selected in
  the tree and a Procedure editor open, `F3` fell through Tree→Global and would add a **parameter row to the
  background tab**. `Grid` scope removes the fall-through because the scope is simply not live in the tree.
  **⚠ The ONE gesture that could not be centralised, and why it is structural:** Procedure/Function Easy mode
  formats the **cursor/subprogram** grid-row editors *in place*, an action identified by a specific
  `TextEditor` **instance** — the router resolves commands, not controls. Those two handlers survive, rebound
  to `Ctrl+K` and **narrowed to fire only for those two editors**, so everything else falls through to the
  catalog. Deleting a working behaviour was not a refactor's call. Trigger/View/Package handlers were deleted
  outright.
  **⚠ Collisions with Windows/IDE standards are REPORTED in the design doc §13, not silently resolved** (the
  user's standing instruction). The two worth knowing: **`Shift+F6` is Build in Visual Studio** and here rolls
  back the working transaction — the one pairing whose two meanings are not equally recoverable; and **`F3` is
  Find Next almost everywhere**, mitigated only because AvaloniaEdit binds no `F3` and the gesture is live
  solely in the tree and grids. `F8` is a *match* with VS (Next Error) precisely because of the scope split.
  **⚠ Flagged, NOT touched (§13.2):** `CommitAllAsync`'s comment claims *"the TOOLBAR's Commit stays
  deliberately narrower"* while the toolbar binds `CommitAllCommand`, with a narrower unused `CommitCommand`
  beside it. `F6` binds what the button binds — the correct rule either way — but the discrepancy is
  transaction-settlement territory and belongs to whoever owns rule #11. **The sprint's one document — audit, ratified decisions, architecture,
  as-built, etap order: [docs/design/keyboard-manager.md](docs/design/keyboard-manager.md).** Read it before
  touching anything under `EmberTern.App/Commands`.
  **Goal (the user's own framing): not "a few more shortcuts" but ONE source of truth for commands** — the
  same registry feeding shortcuts, tooltips, context menus and, later, a Command Palette and a shortcut
  editor, with nothing duplicated in XAML or `UiStrings`.
  **⭐ RATIFIED SHORTCUT MAP — decided by the user, do not re-litigate:** `F3` New · `F4` Refresh ·
  `F5` Execute (**Continue in the debugger stays the one accepted contradiction**) · `F6` Commit ·
  `Shift+F6` Rollback · `F7` Compile · `F8` **Delete in trees/lists, Next Diagnostic in the editor** (a
  scope split, both kept) · `Ctrl+K` Format SQL. **No `Alt+letter` gestures at all** — `Alt+F` is retired in
  etap 3 with no exception — and F-keys are reserved for the most frequent operations. Windows/IDE standards
  stay (`Ctrl+S`/`F`/`H`/`Z`/`Y`/`X`/`C`/`V`/`A`, `Ctrl+Enter`, `Ctrl+Space`, `Ctrl+.`, `Ctrl+Shift+F`,
  `Escape`). `Alt+F12` (Peek) is Alt+**function key**, outside the rule, and stays.
  **⭐ ARCHITECTURE (ratified):** `CommandCatalog` holds **`CommandDescriptor`s, never `ICommand`s** — a
  gesture→command map is not expressible here, because "Go" belongs to the selected tab and the Explorer's
  15 commands belong to a `MetadataNodeViewModel` built **per tree node**. One literal table, built once at
  type-init (the `LanguageConstructCatalog` pattern); `CommandRouter` (view layer, **Bubble** phase) does the
  focus probe and resolves **Editor > Tab > Global** — `CommandScope`'s numeric values *are* that order.
  `WorkspaceTabViewModel.ResolveCommand(CommandId)` joined `UnsavedWork`/`SavableEditor`/`RefreshAsync` as
  the **fourth member of the existing per-kind family**, not a new mechanism beside it.
  **⚠ The line to hold: no `KeyGesture` ever reaches a view model.** VMs answer questions about a
  `CommandId`; gestures live in the catalog and the view layer.
  **⚠ `CommandDispatch.Reserved` is load-bearing, not bookkeeping.** The editor's typing mechanics
  (#224/#228) and the debugger's stepping keys stay **locally dispatched** — several are view actions needing
  the source editor's caret — but are **declared**, so the collision validator sees them and no global
  gesture can quietly steal one. A live Reserved command stops resolution *without* handling the key.
  **⭐ Etap 2 fixed the audit's confirmed defect C1 — and it was a real data-safety issue.**
  `MainWindowViewModel.GoCommand` had no `CanExecute` and ended "anything else → Execute Query", while
  `ResolveActiveSql()` falls back to the SQL editor's `QueryText` — so **F5 on a Table editor, Security
  Manager, Trace, Sessions or any object editor ran the editor's text inside the user's working
  transaction**. F5's reach is now a declaration (four tab kinds), so the other 16 cannot see it. Deleted
  with it: the whole `Window.KeyBindings` block, the Tunnel-phase `OnWindowKeyDown` + its `IsInsideEditor`
  focus probe, `GoCommand`/`GoAsync`, `RequestGoAsync`, and the duplicate F5 owners in the debugger, Script
  Executor and Data Import views. **Side effects, both improvements:** Script Executor's F5 works anywhere in
  its tab (it used to need focus in the script editor, and otherwise executed the SQL editor's query), and F5
  in a non-actionable debugger phase now leaves the key alone instead of silently doing nothing.
  **⚠ Two measured facts that overturned inherited assumptions — do not re-derive them.** (1) **AvaloniaEdit
  12.0.0 claims NO function key**: `SearchInputHandler` registers Find/FindNext/FindPrevious as
  `CommandBindings` with **no `KeyGesture`**, so the AvalonEdit lore that `F3`/`Shift+F3` are find-next/prev
  does not hold here and `F1`–`F12` are free in the editor — now a permanent guard, because an upgrade that
  began binding one would break a global shortcut with the build green. It *does* claim `Delete`, `Back`,
  `Return`, the arrows and `Shift+Alt`+arrows (box select). (2) **Every `TextEditor` shipped TWO
  `SearchPanel`s**: the editor creates its own, and `EditorSearch.Install` called `SearchPanel.Install` on
  top, so `Ctrl+F` and the context menu's Find drove different instances. Fixed by using
  `TextEditor.SearchPanel`; pinned by asserting the **handler count**, since a non-null check passed either
  way. Gotchas **#282 / #283**.
  **⚠ The audit's A-10 is ONE table row**, not a design — no scope list, no resolver, no `CommandId` shape.
  The richer "sketch" was written during the previous sprint's triage. Its diagnosis is right; there was
  nothing to copy.
  ⚠ The **`MenuItem`/`ContextMenu` style gap is now closed** — that file had no menu selectors at all, which
  is why menus looked untouched next to the rest of the app.
  ⚠ Still relevant: the **app-wide UX sprint** (density) is backlogged and owns *control heights* — "smaller
  typography + icons on the left in the context menu" is this sprint's, a global control-height change is not.
- **🛡 ARCHITECTURE HARDENING / PRODUCT SAFETY SPRINT — CLOSED AND USER-ACCEPTED (2026-07-27).** Build 0/0,
  suite **5900 green** (from 5856), smoke clean, `tools/probes/ChangeSafetyProbe` 19/19 on FB5. Committed as
  `340a634` and pushed to **both** remotes. Narrative +
  audit-by-audit verdicts: **[docs/history/22-architecture-hardening-sprint.md](docs/history/22-architecture-hardening-sprint.md)**.
  Driven by an external audit (`docs/audits/embertern-full-audit-2026-07-26.md`) that the user explicitly
  said **not to trust**; every finding was re-verified against the code first. Five things landed:
  - **⭐ DDL CHANGE SAFETY — the big one.** A compile can no longer overwrite an object definition EmberTern
    cannot prove is the one the editor loaded. `ObjectChangeSafety` (Core, pure) renders the verdict —
    `Safe` / `ChangedInDatabase` / `AlreadyExists` / `Unverifiable`, where **`Unverifiable` is not
    permission** (rule #11) — and `ObjectChangeGate` (App) holds one baseline per tab, calls back into the
    caller's own reader, and owns the ONE verdict→message mapping. Wired into the **five whole-object-
    replacement** surfaces (Procedure/Function/Trigger/View/Package) **+ the debugger's Save**; checked LAST
    in compile (it costs a round trip) but BEFORE `ExecuteAsync` (which auto-commits), and in the debugger
    BEFORE the session-ending confirm (a refusal must cost nothing). **No force-overwrite in v1, by
    decision** — the escape hatch already exists (run it in the SQL Editor, where it is unmistakably the
    user's call) and every refusal message says so.
    **⚠ Scope is a decision, not an oversight:** Domain/Exception/Index/Generator are **diff-based** (only
    what the user edited is emitted) and their New flow is `CREATE …`, not `CREATE OR ALTER`, so the server
    itself rejects a duplicate; Table Detail emits incremental `ALTER TABLE`. Those are out.
    **⚠ The audit missed the likelier failure: New-object templates are `CREATE OR ALTER`**, so typing an
    existing name silently overwrote that object — one user and a typo, no concurrency needed. Hence
    `FirebirdMetadataReader.ExistsAsync`, built over **the same `ListAsync` the tree uses** (a second
    existence query would be a second definition of existence, free to drift).
    **⚠ Three facts that cost design time — do not re-derive them.** (1) Firebird has **no** change counter
    or timestamp for a routine, so re-read-and-compare is the only available mechanism. (2) The DDL
    reconstruction **synthesizes a stub** for a missing routine (106 chars, measured) ⇒ it can never answer
    "does this exist" — but it *is* the right thing to fingerprint, because it carries parameters as well as
    body and so catches a **signature-only** change. (3) The debugger's Draft `_baseline` becomes the text
    SENT after a save, so change safety must re-READ to re-baseline or every second Save phantom-conflicts.
    Gotchas **#279 / #281**.
  - **⭐ SETTINGS LOAD HEALTH — a confirmed P0 by impact.** `Load()` returned `null` for a missing file AND
    an undecryptable one; all 8 facades do `Load() ?? new()` then `Save()`; and the save guard explicitly
    allowed replacing a file it could not decrypt. So on a copied Windows profile a **grid-column resize
    destroyed the connection profiles and passwords.** Now `SettingsLoadStatus`
    (`Missing`/`Loaded`/`Unreadable`/`Corrupt`/`FutureVersion`) + **`Save` refuses** over anything not fully
    understood, classified by *cause* (an undecryptable file is usually intact data belonging to another
    account). Escape hatch `SaveOverUnreadableFile` renames the old file aside, timestamped; nothing calls it
    automatically. `AtomicWrite` also keeps `settings.dat.bak`. **The App SAYS so** — a docked shared
    `MessageBanner` on MainWindow (new `Auto` row; body/status bar moved to rows 2/3), `Warning` not `Error`,
    dismissible — because refusing quietly makes safe behaviour indistinguishable from working. The
    regression test was **run against the pre-fix code and seen to fail**. Gotcha **#280**.
  - **Document-mutation contract corrected + pinned.** `TextEditApplier`'s "one owner of every change" claim
    was false (13 files call `Document.Replace`/`Insert` directly) but none is dangerous — each is
    synchronous with the keystroke that produced it. The real contract is **anything with a DRIFT WINDOW**
    (computed from the model, applied later) goes through it; `DocumentMutationContractTests` now pins the
    allowed set with a reason per file + guards against stale/unexplained entries. Verified by planting a
    violation. **The audit's proposed `UserTypingEdit`/`AssistedCodeEdit` split was declined** — a large
    refactor for no behavioural gain.
  - **Dependencies patched.** `System.IO.Packaging` 8.0.0 → **9.0.18** (2× High DoS on hostile input) in
    `EmberTern.Office`, flowing to App. ⚠ The audit's mitigation *"we export, we don't import XLSX"* was
    **stale** — since I9 the module READS untrusted `.xlsx`, so the exposure is ordinary use. Test-only
    `SixLabors.ImageSharp` → **2.1.11** (2.1.10 fixes only one of its two advisories) and
    `System.Security.Cryptography.Xml` → 8.0.4. All five projects now scan clean.
  - **A status chip that could lie.** The transaction-profile chips read the *persisted* profile while
    `TransactionService` hard-enforces `ReadCommitted`; a v1-migrated file could make the UI claim "Table
    Stability" for Read Committed transactions. They now read the new `TransactionService.EnforcedProfile`;
    3 dead `UiStrings` constants removed.
  **⛔ NOT DOING — RATIFIED BY THE USER ON CLOSING THE SPRINT (2026-07-27). These are DECISIONS, not open
  questions; do not re-open them from the audit text, and do not offer them as "while we're here" fixes.**
  - **A-02 (Debugger Safety) — NOT IMPLEMENTED, and the current behaviour is the intended one.** The user's
    words: EmberTern *detects and clearly communicates* irreversible side effects but **does not block
    debugging** — that is the product's philosophy, not a gap in it. `DebugPreflight` already surfaces
    `IN AUTONOMOUS TRANSACTION` / `GEN_ID` / `NEXT VALUE FOR` (spec §4.6, "disclosed, not hidden"). Modes such
    as **Safe Simulation / Risk Mode** are a **separate product decision for the future — explicitly NOT a
    fix**, so they must never arrive as a bug-fix or a side effect of another milestone.
  - **A-07 (`ImportPipeline` skips `CompleteAsync` on a non-cancellation exception) — NOT IMPLEMENTED.** Real
    but P2: the App layer catches everything, reports it and drops a created table, so the user gets a message
    and a rollbackable transaction, never silent corruption. Data Import is **closed and its public contract
    stays closed** — recorded as a possible improvement to a FUTURE version of `ImportPipeline`, and on its own
    it does not justify re-opening a finished sprint.
  - **A-08** (split the large ViewModels) declined — file size is not a defect. **A-06** historical.
- **🏁 DATA IMPORT — CLOSED, USER-ACCEPTED AND MERGED TO `master` (2026-07-27).** Suite **5856 green**
  (5815 + the 41-test `ConnectionExpandBindingProbe`), build 0/0, smoke clean. **Full narrative:
  [docs/history/21-data-import.md](docs/history/21-data-import.md)** — the etap-by-etap record lives there,
  moved out of CLAUDE.md and out of the design doc at close-out; architecture (🔒 frozen):
  [docs/design/data-import.md](docs/design/data-import.md) — architecture only, in the present tense.
  **What it is:** a tool tab (toolbar, beside the Script Executor) that imports Clipboard / TXT / CSV / XLS /
  XLSX into an existing or a newly created table. One working surface with collapsible sections, **deliberately
  NOT a wizard** (the same import runs repeatedly; the gate is a readiness strip, not Next buttons) · **one
  pipeline for every source** (a provider yields `SourceSchema` + `RawRecord`) · **`ImportConfiguration` is the
  single representation of every user decision**, so surface state, pipeline input and profile payload are the
  same record — enforced by a reflection round-trip test that fails the suite when a new setting bypasses it ·
  rows go to the module's **own transaction on its own attachment** (I7.5), `CREATE TABLE` to the **Ddl** lane
  (gotcha #213 — and the UI says out loud that Rollback will not remove that table) · named profiles, a
  converted preview that IS the real pipeline, and a report whose numbers equal `SELECT COUNT(*)`.
  **Live verification that exists and must keep passing:** `tools/probes/DataImportProbe` (20/20) and
  `tools/probes/DataImportRunProbe` (33/33) on FB5 `WI-V5.0.3.1683`.
  **⭐ Three results worth carrying out of the module.** (1) **The „one pipeline for every source" pillar held
  three times** — I9 (XLSX), I10 (XLS + clipboard) and the post-I10 seam touched neither the pipeline, the
  converter, the validator, the mapping planner nor the writer; I10 was the harder test because it arrived with
  a **dependency**, and that dependency reached exactly one project. (2) **I11 was the design's own audit and
  the account balanced** — named profiles required **no model change at all**, which is what §4.8 had staked
  itself on. (3) **A million rows go through in 14.0 s (71 437 rows/s) with a FLAT managed heap** (~1 MB across
  the run, peak working set 66.6 MB on a 36.6 MB file) — the shape is the proof, not the number; the I0
  defaults (batch 500 / commit 10 000) **hold**, and `Batched` costs 40% throughput, which is the price of the
  *mode*, not of the number.
  ⚠ **Open after the module, each with its reason, in the history file's closing table** — the app-wide UX
  sprint (U4 density, **U5 responsiveness**, + the remaining I11 review wishes), profile `.json` exchange, a
  „modified" marker on the selected profile, the platform-wide charset audit, and the full-suite hang
  (#94/#226/#261). None is a Data Import defect; each is somebody else's task.
  **⭐⭐ THE SURFACE IS SPLIT BY RESPONSIBILITY: the top half is where the import is DESIGNED, the bottom panel
  is where RESULTS land** (user's call, 2026-07-27, and it is the decision that finally settled U5). „Preview
  after conversion" is a *result* — what the pipeline produces — so it is a **bottom tab** beside Source
  preview / Errors / Report, needed as it is in both target variants. The work area now belongs entirely to
  configuration: **Existing table → Mapping at full width; New table → `Table types | Mapping`** with a
  splitter, so the user sets the proportion. The type grid moved out of the `Auto` Target tile into that left
  half, which is what made the tile thin again.
  **⭐ The generated `CREATE TABLE` is the FIFTH BOTTOM TAB (`DDL`), shown only in the „new table" variant** —
  the same configuration/results principle carried to its end: the statement is an *artefact* of the
  configuration, so it belongs with Source preview / Preview after conversion / Errors / Report. It is **live**
  (rendered from `CreateTableSql`, computed through the same `ImportNewTable.BuildCreateSql` the run calls), so
  there is no button, no command and no „is it showing" state to keep in sync. Last on the strip on purpose —
  a hidden tab still holds its index and the run reveals Report by index.
  **⭐ It renders through the SAME read-only SQL surface as the other eleven DDL previews** — an
  `AvaloniaEdit.TextEditor` + `SqlEditorBehavior.AttachReadOnlyHighlighting` (shared XSHD lexical layer + the
  semantic accent layer from the one language front-end), **never a second renderer**. A plain text block had
  made this the only place in the application showing colourless SQL. Read-only in the strict sense the seam
  guarantees (no completion, no squiggles, no ergonomics); no line numbers; text **pushed** from the VM, since
  a two-way `TextEditor.Text` binding is flaky — the same workaround every DDL preview uses.
  ⚠ **Two worse answers came first and neither should be retried.** Disclosed *inline* in the types column, it
  complicated that column permanently for something read rarely. Opened in the **SQL Editor**, it was technically
  clean but a **UX regression the user rejected**: the DDL is part of configuring *this* import, so reaching it
  must not switch modules or move the active tab. `OpenSqlInEditor`, `ShowCreateTableDdlCommand` and the
  `OpenSqlAsSavedQuery` generalization were all removed with it.
  ⚠ **Two earlier attempts failed and are worth knowing before re-opening this.** A `MinHeight` floor on the
  work row plus a clamp on the bottom panel was built and **reverted** — honouring the floor made the bottom
  panel give way, so the middle grew and the panel stopped being useful. The lesson the user drew, and it is
  the load-bearing one: **it was never a height problem, it was configuration and results interleaved and
  fighting over the same pixels.** Do not answer a space complaint here by adding a floor or another vertical
  section; ask first what is a *decision* and what is an *outcome*. Gotcha #274 carries the postscript.
  **⛔ STANDING DIRECTIVE: do NOT return to Data Import cosmetics.** Come back only for a real functional
  defect.
- **✅ METADATA REFRESH — LAYER 1 SHIPPED + THE TREE NOW SHOWS AN IMPORTED TABLE IMMEDIATELY (2026-07-27).**
  A short session between Data Import I8 and I9, scoped by the user to: fix the reported bug, apply **Layer 1**
  of [docs/design/metadata-refresh-analysis.md](docs/design/metadata-refresh-analysis.md), re-measure, update
  docs — and explicitly **NOT** start the Metadata Explorer infrastructure rebuild (Layer 2), which is its own
  stage after Data Import closes. Suite **5717 green** (+13), build 0/0, smoke clean.
  **⭐ Layer 1 is the whole lesson: the bulk guard already existed and was wired to ONE of the two mass
  mutations.** `SidebarFlatController.BeginUpdate/EndUpdate` — whose own comment names *"an O(n²) storm"* —
  was applied to the FILTER rebuild only; `MetadataNodeViewModel.SetLeaves` (`Clear()` + one `Add` per object)
  ran unguarded, so each `Add` re-spliced the owner's entire child block. Now called from `LoadGroupAsync`
  (covering every caller), `RefreshAsync` (13 re-projections → 1) and the connect-time prefetch; nesting-safe.
  **Measured (`tools/probes/MetadataPerfProbe`, 2 400-table schema): a full refresh with one category expanded
  1 424 ms → 2 ms; with two, 1 733 ms → 4 ms.**
  **⭐ The bug fix reversed the report's own recommendation, on the user's call:** the report advised deferring
  "the new table is not in the tree" to Layer 2; the user rejected that (ordinary UX bug, fix it now) while
  keeping the ban on a 21st `Metadata.RefreshAsync()`. Both hold at once because **the import knows the name of
  the table it created** — `DataImportEnvironment.TableCreated`/`TableDropped` report a FACT ("this table
  exists"), never a command ("refresh"), and `MetadataExplorerViewModel.ApplyObjectAddedInPlace`/
  `ApplyObjectRemovedInPlace` insert/remove **one leaf at its sorted position** (**1.3 ms, zero catalog round
  trips**, vs 13 queries ≈164 ms + a full re-projection). Three details make a targeted update correct and each
  is pinned: **idempotent** (a later refresh may already hold it); an **active filter** means the leaf enters the
  master list always and the displayed list only if it matches, with match count + zero-match visibility
  re-derived; an **unloaded** category has no leaves but does have an `(N)` label, so its count moves instead.
  The name index is **patched**, not invalidated (dropping it would cost 13 catalog reads to forget one name we
  were holding). ⚠ **Scope:** a narrow precedent beside the existing `ApplyTriggerActiveStateInPlace` — **not** a
  change protocol; the other 20 DDL paths were untouched.
  ⚠ **Startup did NOT improve, and that is measured, not assumed:** `RestoreExpandState` restores folder +
  connection expansion but **not** category expansion, so at connect every category is collapsed and the
  projection already had an early exit (the "0 expanded" row: 6 ms → 2 ms). Layer 1 fixes *refreshing*, not
  *starting*; the startup cost stays unresolved, instrument `EMBERTERN_PERF_DIAG=1`, suspects = the
  semantic-model rebuild after `NotifyMetadataReady` and workspace tab restore.
  ⚠ **Accepted trade-off:** `EndUpdate` re-projects everything, so `SidebarRow` objects are recreated and the
  list scrolls to top — already true of every refresh (it ends in `ApplyFilterAsync`, which does exactly this),
  but it broke `ConnectionExpandBindingProbe.AutoExpandOnConnect_ReflectedInFlatList`, which held a row
  **instance** across a connect; it re-resolves the row now (the probe is about mirroring, not identity).
  Removing the tree's "jumping" is Layer 2's job. **Still open for the Metadata Explorer stage:** Layer 2
  (first-class "what changed" across all DDL paths + incremental splicing + scroll/selection preserved),
  Layer 3 (reconcile the prefetch with `RefreshAsync`'s dead `LoadCountAsync` branch · `Domain` 79 ms
  `RDB$FIELDS` scan · `User` security-DB round trip), and the startup measurement. Gotchas **#266 / #267**;
  narrative: [docs/history/09-...](docs/history/09-object-editors-and-metadata-tree.md).
- **📋 BACKLOG (do NOT start) — GLOBAL UI DENSITY / an EmberTern-wide UX sprint.** Raised by the user during
  the Data Import I5 review (2026-07-26) and, in the same breath, **deliberately scheduled for AFTER the
  Data Import module closes**. The finding: EmberTern's ordinary form controls are too tall app-wide —
  `TextBox`, `ComboBox`, `CheckBox`, `Button`, vertical spacing, form row heights — and it must **not** be
  patched per module. **Confirmed in code:** `Themes/ControlStyles.axaml` has **no implicit style at all**
  for `TextBox` / `ComboBox` / `CheckBox` / `RadioButton` / `NumericUpDown` / bare `Button`, so every one
  sits on FluentTheme's defaults (`MinHeight` 32 px); density was only ever applied **ad hoc, one control at
  a time** (`DataGridCell` FontSize 11 + tight padding, `DataGridRow` and `TabItem` `MinHeight="0"`). The
  precedent and the intent exist; the generalisation does not. **⭐ The user's ratified sequencing, which
  reverses my earlier "density first" recommendation — do not re-propose it:** one task at a time; finish
  Data Import to the accepted architecture first, then run a dedicated UX sprint that looks at **every**
  surface at once (SQL Editor, Debugger, Activity Monitor, Session Manager, Script Executor, Data Import and
  the rest) and designs the new global control style from that whole view — not extrapolated from one form.
  Mixing the two makes the scope unreadable and the progress unmeasurable. **⛔ Standing instruction (user,
  2026-07-26, on closing I5): from here on a module etap delivers the module — do NOT initiate global UI
  changes or style refactors.** Avalonia control rebuilds, density, styles and responsiveness all wait for
  the sprint. A **small remark about one specific screen** (the kind that produced `Border.settings-group`)
  may still be fixed in passing; anything that would touch the app as a whole may not. The working line:
  *a control's height* is the sprint, *two controls stacked instead of side by side* is the module.
  **⭐ THE SPRINT INHERITED THE MONOSPACE-FONT ITEM FROM SETTINGS CENTER (user, 2026-08-01) — with its
  measurement, which is the part that makes it actionable.** Settings Center §7.1 had planned the
  *consolidation* as a small prerequisite inside that sprint (ratified Q9: *"prerequisite only"*). Re-measured
  before etap 6 committed to it: **7 distinct monospace `FontFamily` strings, 95 occurrences, 33 files** — not
  the *"four across ten"* the audit had recorded. So it is neither small nor independent, **collapsing them
  decides `Cascadia Code` (ligatures) vs `Cascadia Mono` (none) for the editor, the debugger, the hover cards
  and eleven DDL previews at once**, and that is a typography decision for the sprint that sees every surface
  together. ⚠ Both halves — the consolidation *and* the font family/size setting — are this sprint's now;
  Settings Center ships **no** font setting, so nothing exists that would configure some surfaces and not
  others. Detail: [docs/design/settings-center.md](docs/design/settings-center.md) §2.7 + §7.1.
- **⚠ Spun off from Data Import I0, NOT part of it — a platform-wide defect to audit.** Measured on live FB5:
  binding a string containing a character the **connection** charset cannot represent stores `?` with **no
  error at all**, even when the target column is UTF8 (the connection charset decides, not the column's), and
  `ConnectionProfile.Charset` defaults to `WIN1250`. Triage found it reaches `FirebirdDataEditor` (Table Data
  inline edit/insert) and `FirebirdQueryExecutor` (Smart SQL Parameters); the **statement-TEXT path
  (`FirebirdDdlExecutor`) is unmeasured and could silently corrupt user SOURCE CODE — measure it first**.
  Ratified: a **separate architectural audit**, ONE shared guard (natural home: `CharsetCatalog`, with
  `EncoderExceptionFallback`, never per-module fixes), plus an entry in `docs/gotchas.md`. Deferred by the
  user; escalates if the DDL path is confirmed. Evidence: `data-import-i0-findings.md` §2.8.
- **🏁 PREVIOUS STATE (2026-07-25) — THE FIREBIRD DEBUGGER IS CLOSED.** Everything planned for it is delivered
  and user-QA-confirmed: P1/P2, D1–D13, D15 (D15.6 dropped, D15.7 background), functions-as-root (standalone +
  packaged), the **Draft model** (Seams A + B), the **launch-config rebuild** (C3.1–C3.3b), and **Seam 6d**
  (a compiled object refreshes the other tabs showing it). **Two items stay deliberately deferred, each with a
  ratified brief and neither blocking anything: D14 (Step Back)** and **C3.4** (root frame layout from the AST
  header ⇒ the header gate comes off). Both return only if real usage asks. **The branch `feat/firebird-debugger`
  was merged to `master` at this point.** Next work is elsewhere in EmberTern.
- **⭐ The Draft model is DELIVERED and the launch config survives a signature
  change. Seams A + B + C3.1–C3.3b are COMPLETE and user-QA-confirmed on the live app; the ONE open item is
  C3.4, deliberately deferred pending real usage** (see the C3 entry below). **QA verdict on A+B:** the first
  edit ends the session · the user stays in the editor (`Editing`) · Restart runs the current editor code · the
  procedure in the database stays untouched · `Save` is still the only DDL write · stale code can no longer be
  executed by accident after an edit. **QA verdict on C3:** parameters, history, carry-over and triggers all
  behave as specified, and `Restored` vs `Assumed` read clearly.
  **Background.** After several days of real use the user reported the coherence gap: editing during a session
  was allowed, but the next step still executed the launched version — *I see code A, the debugger executes B*.
  Rule adopted (IBExpert's): **the first change to the text ends the session** — no save, no step, no restart
  needed. **Ratified split, do not re-litigate: `Save` is the ONLY operation that writes to the database;
  `Restart` starts a session from the CURRENT editor text without saving** (an earlier proposal to route a
  dirty Restart through Save→Compile→Launch was rejected by the user, correctly — the debugger never asks the
  server to run the compiled routine, so compiling to restart writes to the DB for no technical reason and
  kills experimenting on a routine without modifying it). **Seam boundaries (the user's own framing):** A =
  *no step can ever run stale code again* (it does NOT start a session on the new text); **B+C** = start the
  session **from the draft** — i.e. the backlogged Seam 6c is BACK, scoped to Restart only.
  **⭐ Seam A — DONE + user-QA-confirmed (commit `aa7f801`; build 0/0; 5230 green; smoke clean).** One rule in one place:
  `ApplySourceEdit` → `OnSourceChanged` → live session ⇒ `EndSessionForEditAsync` (teardown §4.4 + clear
  surfaces + `Phase = Editing`), `Busy` ⇒ condemn-and-end-in-the-operation's-tail, terminal (Completed/Faulted)
  ⇒ keep the retained frame inspectable (values at the fault while fixing it) but drop the position marker.
  Every stepping/eval command was already gated on `Phase == Paused && Session is not null`, so the whole
  toolbar disabled itself with **no per-button work**. **`DebuggerPhase.SaveFailed` → renamed `Editing`**
  (Contract #16): ONE state, two entry reasons (an edit ended the session / a save was refused), one meaning —
  *no session, and the tab keeps showing the source because the code is what matters*. `StopAsync` + the new
  path share `ClearSessionSurfaces()`. **Save is unchanged** (the edit-end arms `_resumeAfterSave` exactly as
  the save-triggered stop does, so Save from `Editing` still compiles → resumes); its *"saving ends the running
  session"* confirm is now near-unreachable but **kept** for the one remaining window (Ctrl+S while a step is on
  the wire) rather than deleted on a reachability argument. **⭐ RATIFIED on acceptance: ending the session is
  PERMANENT** — undoing the edit back to byte-identical text must never resurrect it; Restart may re-enable, but
  only as a deliberate new start (pinned by a test). **⚠ gotcha #253** came out of this and generalises: ending
  an async resource under an in-flight background op doesn't just race — the op throws on return and a generic
  `catch` reports it as a **fabricated** failure of the work (here: a red Firebird "fault" for something the
  user did on purpose); the fix is to let the op's own tail end it, with every `catch` asking "was this
  requested?" first.
  **⭐ Seam B — DONE + user-QA-confirmed (commit `7a410c7`; build 0/0; 5235 green; smoke clean).** The VM's duality is gone:
  **`_baseline`** = what the DB holds (only `IsSourceDirty` + Save read it), **`_editBuffer` + its parse = THE
  PROGRAM** — markers, breakpoint snapping, pre-flight, launch panel and `DebugLaunchSpec` all read it, so
  **Restart starts a session on the edited text with NO compile and NO write to the database.** Re-parse is
  **lazy, NOT debounced** (a `DispatcherTimer` re-introduces a path no headless test can reach, #251): an edit
  marks the program stale, and the few places that need a current parse ask (`EnsureProgramCurrent` — gutter
  click, launch, save); command gates never do. The two duplicated parse blocks collapsed into one
  `ReparseProgram` (+ `AdoptBaseline` as the only place the two texts are declared equal). **Restart ≡ Save
  without the compile:** `SaveAsync`'s tail became the shared `ResumeOnCurrentProgramAsync` (ensure current →
  is the panel still describing this routine? → rebuild inputs if not → re-run pre-flight → relaunch), used by
  both. **Breakpoints survive the loop** via two *provable* filters (never a guess, §0): an offset in the edit's
  **unchanged prefix** still starts the very statement it was set on (kept); one below it may have shifted
  (dropped); and after a re-parse an offset that no longer **starts** a step point is dropped. `AdoptSavedSource`'s
  blanket clear was deleted (Contract #20). **⚠ gotcha #254:** a "before" value must be REMEMBERED where the
  decision was made (`_panelSignature`), never re-derived at comparison time from state that now follows the
  user's input — the old `var configBefore = BuildLaunchSignature()` would, after a mid-flight re-parse, compare
  the new value **with itself** and pass a genuinely changed parameter list as "still valid".
  **⚠ INTERIM shipped in B, and only C3.4 removes it (don't relax it):** a draft runs only while its routine
  **HEADER** is byte-identical to the compiled one (proven without a parse — the common prefix reaches the
  baseline's body start), because the root **parameter list still comes from the catalog**. Body edits — the
  debugging loop — always qualify; a header edit blocks with a status naming Save.
  **⭐ Seam C was RE-ANALYSED against the code before any was written (2026-07-25) and the milestone's premise
  REVERSED — do not restore the old scope.** The App layer was already draft-sourced (panel, signature,
  pre-flight, step points, launch spec — all from the draft), a **trigger root reads nothing about itself from
  the catalog** (`CreateAsync` passes `routineName: null`; NEW/OLD types come from the target TABLE), and four
  of the original six 6c items had shipped in A+B. So the Draft model was **already delivered**; what was left
  split into (a) the engine work that lifts the header gate and (b) rebuilding the **launch configuration**
  after a signature change. The user redirected C3 to (b). Two corrections stand: the **`TYPE OF` regression is
  CREATED by the AST layout, not fixed by it** (the catalog resolves it fine today ⇒ resolving `TYPE OF` belongs
  *inside* C3.4, not "accepted" beside it), and **`RETURNS` does not belong in the launch signature** until a
  changed header can run (it is no user decision — it has no field).
  **⭐ C3 — the launch configuration survives a signature change. COMPLETE + user-QA-confirmed (C3.1 `46e5e67` ·
  C3.2 `1b97b0f` · C3.3a `178f503` · C3.3b `8acbb6f`).** THE RULE (ratified, do not re-litigate): *the debugger
  keeps everything it can PROVE is still correct, hands back everything it cannot, and never guesses* — the
  inverse of IBExpert, which restarts after a signature change without policing the configuration.
  **Proof = equality of `ExecuteParamKind`**; no narrowing analysis (whether a value *fits* is Firebird's job).
  **Matching:** parameters = **`ByName` → `SoleRemainingPair`** (the pair rule fires only when exactly ONE row
  is unmatched on each side; two or more ⇒ nothing carried); trigger NEW/OLD = **`ByName` only**, because a
  column's identity is its name in the catalog and grid position is just the order the body mentions it in.
  `ByName` **claims a pair even when the value fails the proof** (the rows ARE that parameter — leaving a
  retyped one unclaimed would let the pair rule hand its value to a row we can already tell apart). Composition
  lives at the **call site**, never a `bool` flag. **`LaunchValueCarryOver` does NOT implement the proof** — a
  value travels through the history's own `ToHistoryValue`/`ApplyHistoryValue`, so "does this still fit" has ONE
  answer in ONE place. **`ParameterValue.TypeText`** (additive; **schema version deliberately NOT bumped** — a
  bump trips the downgrade protection and older builds would refuse the whole file) makes the history obey the
  same proof; a legacy entry has no type ⇒ not auto-applied (self-heals after one run). **ONE marking
  convention** (`ValueOrigin` Entered/Restored/Assumed) for *every* automatic source — history and same-name
  carry-over → `Restored` (quiet italic, `SubtleForegroundBrush`), the pair rule → `Assumed` (upright,
  semi-bold, `AccentBrush`, the word "assumed"); accent **not** warning — a "worth a look", not a fault. Any
  edit resets the origin, so a marker never claims a value the user has since replaced. ⚠ **gotchas #256/#257**
  came out of this. **C3.4 — the ONLY open item, DEFERRED by user decision:** root frame layout from the AST
  header + `TYPE OF` resolved + `RETURNS` in the signature + §F boundaries in the pre-flight + the mandatory
  `DebuggerFidelityProbe` catalog-vs-draft case (Contract #12); only then does the header gate come off. The
  user will debug normally for a while first — practice decides whether running a changed signature without
  Save is worth the complexity. **§F boundaries for a draft-sourced session — verified in code, do not
  re-derive (all statically detectable from the draft's AST):** **recursion** (a self-call falls through
  `ResolveRoutine`'s local+package branches to `ResolveRoutineAsync`, which fetches the **compiled** source ⇒
  step-into descends into old code — and **step-over does not save you either**, the server runs the compiled
  routine), a **selectable procedure used in its own body**, and a **draft that would not compile** (runs
  partially — PSQL compile-time validation never happened). Narrative: [docs/history/19-...](docs/history/19-firebird-debugger.md)
  (last three sections).
- **Before that (2026-07-25) — two arcs closed; the debugger was idle.** Two arcs closed
  back to back: the **UX Polish Sprint** through **Seam 6b** (6a debugger status → app status bar; 6b
  `SaveAsync` false success), then the debugger's **Save workflow** was reworked after live QA — Save now
  compiles the draft and **resumes the session on the new code** with the settings already made, gated on the
  **launch signature** (object kind + ordered input params, or a trigger's header + referenced NEW/OLD
  columns) still describing the compiled routine; anything else means a new decision and lands on the launch
  panel. *(Seam 6c — the Draft-as-session-source model — was backlogged at that point; real usage asked for it
  days later, and it is **no longer backlog**: it came back as the A/B/C arc above, where A + B are DONE +
  user-QA-confirmed, C was re-analysed + re-scoped into the delivered C3, and only **C3.4** stays open by
  decision.)* Then **🏁 Stage Q (Quick Fixes & Code Actions) — COMPLETE, Q0–Q5, user-confirmed**
  (see the Quick Fix entry below and [docs/history/20-...](docs/history/20-stage-q-quick-fixes.md)).
  Remaining editor backlog: **Folding**, **Breadcrumbs**, and a future **RefactoringEngine** (a sibling
  producer feeding the same code-action menu — nothing was built for it in advance).
- **UX Polish Sprint (Debugger & SQL Editor) — Seams 0–5 DONE + committed + user-QA'd on the live lab; 6a/6b
  DONE. Historical detail below.** (the Seam 6 plan was **replaced** after that QA — read the
  "SEAM 6 REPLANNED" block below; the original Seam 6 = Quick Fix design doc is **deferred**). Goal: **quality,
  not features** — unify the IDE's look, improve readability, fix regressions. No UI rebuild; presentation-first
  where possible; keep the debugger's D1–D4 responsibility split (view/theme changes, never logic pushed into
  VMs). Each seam ends build 0/0 + tests green + committable; **visual changes await the user's QA** (done in
  parallel, not blocking). **Original plan:**
  `C:\Users\grzegorz.gronski\.claude\plans\indexed-launching-sphinx.md` (the approved Seam 0–6 breakdown — now
  **superseded for Seam 6 only**). Commit convention this sprint: **one commit per seam.**
  - **Ratified decisions (do not re-litigate):**
    - **Diagnostics (Item 6)** = a **minimal binder fix**, NOT an ET0003 redesign (see Seam 0 below).
    - **Shared error component = `MessageBanner`** (name chosen over `AlertBanner` — it will also carry
      Info/Warning/Success, not only errors). **Reach (Seam 4):** it becomes the standard on **every main
      work surface where the user executes or compiles code** — object editors (~11) + Execute Procedure —
      with Script Executor + Batch Results + Security Manager as natural follow-ons. **Migration must be
      MECHANICAL** — replace the existing error presentation only; **never change a module's business
      behavior/workflow.** The SQL Editor **Messages log stays a scrolling row-list** (out of scope).
    - **Edit during debugging (Item 5, Seam 5)** = editor is editable **at all times, incl. a live/paused
      session** (no edit-lock at breakpoints). *(Seam 5 as-built is recorded below; its save semantics are
      **superseded by the Seam 6 Draft model** — the buffer stops being a passive draft and becomes the
      session's own source, and Save compiles it in place instead of ending the tab's role.)*
    - **Quick Fix / Light Bulb (Item 7)** — **🏁 STAGE Q COMPLETE + user-confirmed (Q0–Q5, 2026-07-25).**
      Guide: [editor-quick-fixes.md](docs/design/editor-quick-fixes.md); narrative:
      [docs/history/20-...](docs/history/20-stage-q-quick-fixes.md). ⚠ **This holding note was WRONG on two
      points and the design doc supersedes it:** (1) `Diagnostic.QuickFixes` is
      **not** additive — `Diagnostic` is a `record struct` whose value equality the panel relies on to skip
      rebuilds, and a list member degrades that to reference equality ⇒ fixes are computed **on demand** from
      `(model, diagnostic)`, never stored; (2) the fix list does **not** go on the hover card — §15.1.1's
      ratified "plain hover = information, Ctrl = actionability" stands and `HoverInfo` keeps its read-only
      guarantee, so the light bulb + `Ctrl+.` (one shared menu) are the actionable surfaces. Still true: D3
      satisfies the wiring prerequisite, and the adorner is wired only in `SqlEditorBehavior.Attach` (never
      `AttachReadOnlyHighlighting` — a read-only surface must not offer mutating fixes).
      **As-built (commits `3a4a6cb` Q0 · `405290f` Q1 · `40e3b20` Q2 · `358e570` Q3 · `8803627` Q4 ·
      `9e20e2c` Q5 · plus the Q3 QA rounds `156bade`/`a641a43`/`ce70641`/`d0b0d12` and `73380b3`):**
      pure-Core `QuickFixEngine` computes fixes **on demand** from `(model, diagnostic)` — never stored on
      `Diagnostic`, whose record-struct value equality the panel needs — over the **unchanged** read-only
      diagnostics pipeline. Shared currency `CodeAction`/`TextEdit` (`Core.Sql.Language.CodeActions`);
      **`TextEditApplier` (App) is now the ONE owner of every document mutation** — drift check via
      `TextEdit.ExpectedOldText`, descending-order apply, one undo unit, one caret rule — and **safe local
      rename was migrated onto it** (`NavigationRename.Occurrences` gained the binder's per-occurrence text
      so the shared check compares against the MODEL, not the document with itself). v1 actions: qualify an
      ambiguous column (ET0005, one per candidate) + **"Did you mean …?"** for ET0001/2/3/4 when **exactly
      one** candidate is close (`NameSuggestion`, case-insensitive, budget by length, ties ⇒ silence), which
      **keeps the user's capitalisation** and **preserves the `:`/`@` sigil** (dropping it turns a variable
      into a COLUMN inside embedded DSQL). Deliberately refused: ET0006/ET0008 and "declare the missing
      variable" (it would have to invent a type — rule #11). **Four triggers, ONE menu and one selection
      model** — `Ctrl+.`, the light bulb, a single click, and the Diagnostics panel's "Quick Fix…" — all
      through `GetActionsAtCaret` → menu → `InvokeCodeAction`; the panel reaches it via an attached property
      published by the one attach seam (so a read-only DDL preview, never attached, offers nothing).
      **⚠ Three gotchas came out of the light-bulb investigation and are load-bearing beyond this stage:
      #250** (never look a BRUSH up without a `ThemeVariant` — `FindResource` returns UNSET and an
      `SvgIcon` then paints nothing while every observable state looks healthy), **#251** ("added" ≠
      "paints"; a timer-only trigger is untestable headlessly), **#252** (an editor popup is
      keyboard-complete on day one — preselection/↑↓/Enter/Esc + single-click-equals-Enter — handled on the
      TUNNEL, with focus staying in the editor).
  - **Seam 0 — Diagnostics regression → DONE (commit `140547b`).** Root cause: **not a revertible
    regression** — a long-standing gap. `DiagnosticsEngine` (ET0003) never changed; the `:name` path always
    flagged; an unresolved **bare** identifier was never recorded (`SemanticBinder.Psql.BindBareLocal`'s
    `switch (sym)` had no default), a deliberate guard against false-flagging unqualified columns, present
    since the SemanticModel was born (`632bd86`). Minimal fix (exact agreed scope): an unresolved **bare**
    identifier is now recorded as an unknown variable (ET0003) **only in unambiguous PSQL value positions** —
    `BindLeaf` flags `Assignment`/`Return` leaves, `BindControlHeader` flags `IF`/`WHILE`; `BindForSelect`/
    `BindBodyStatement` stay unflagged. Excluded so **no false positives**: query/DML ranges (columns),
    context variables (`ROW_COUNT`/`SQLCODE`/`GDSCODE`/`SQLSTATE`/`INSERTING`/`UPDATING`/`DELETING`/
    `RESETTING`/`USER` — a static `BareContextVariables` set), `NEXT VALUE FOR <seq>` (the `FOR`-preceded
    identifier), `EXCEPTION <name>` names and `LEAVE <label>` labels (those leaf kinds never flag). +11
    `DiagnosticsEngineTests` (both true-positive and each no-false-positive); 3 neighbouring fixtures
    (Diagnostics/Hover/Panel) tightened so their scaffolding vars resolve. **⚠ Side-effect to watch:** the
    binder now emits an extra unresolved-Variable `SymbolReference` for a bare bad name — any future test that
    counts references over PSQL with an undeclared bare identifier may see one more. Build 0/0; 5156 green.
  - **Seam 1 — Shortcut presentation → DONE (commit `d119057`).** New shared `TextBlock.shortcut-chip`
    (ControlStyles.axaml, on-accent). Execute button + debugger launch button both render label +
    shortcut-chip (launch button changed from a `Content` string to a label+chip StackPanel).
    `DebuggerLaunchButton` → `"Start debugging"` + new `DebuggerLaunchShortcut = "F5"`; every "Action (Key)"
    command tooltip normalized to paren-free "Action · Key" (Continue · F5, Step Into · F11, Format SQL ·
    Alt+F, Global Search · Ctrl+Shift+F, Edit selected field · F2, Evaluate … · Shift+F9, Run … · F5).
    Prose/instructional strings left as-is. Pure Presentation; build 0/0; 5156 green.
  - **Seam 2 — Current-line legibility → DONE (commit `6c299e3`).** Token-only: `DebugCurrentLineColor` wash
    alpha raised for VS/Rider-clear legibility, calm blue hue + strong accent bar kept — Dark `#285A8AC8` →
    `#4A5A8AC8` (α ≈16%→≈29%), Light `#1C0033B3` → `#380033B3` (α ≈11%→≈22%). (Note: **Dark** wash is line 55,
    **Light** is line 289 in `Colors.axaml` — verified, agents had them swapped.) Build 0/0; 5156 green.
  - **Seam 3 — Variables panel polish → DONE (commit `475a5c1`).** Ergonomics only (no panel-architecture
    change): value rows got a real right gutter (row padding `4,1` → `8,3,12,3`, inner grid margin dropped,
    group header `6,3` → `8,4`) so values no longer touch the edge/scrollbar. The panel has **no
    row-selection**; the only highlight is the semantic amber **changed-value** wash (D7 — "this value changed
    since the last step"), which the user chose to **keep** (deliberately distinct from selection/current-line)
    — only de-olived to a cleaner warm amber-gold, same low alpha: dark `#C8A000` → `#E0A830`, light `#E0B000`
    → `#D69E24`. Build 0/0; 5156 green.
  - **Seam 4 — shared `MessageBanner` → DONE (commit `1e25ce5`).** The IDE now has **ONE message surface**.
    **4a** — new `Controls/MessageBanner` (`UserControl` + `MessageSeverity` Info/Success/Warning/Error),
    generalized from the debugger Error Bar: `PanelBrush` chrome + a thin severity stripe + severity icon, the
    full message wrapped + selectable (capped `MaxExpandedHeight=190`, then scrolled), optional
    `ShowCopy`/`ShowExpand`/`ShowDismiss` (+ `DismissCommand`), `IsExpanded` **TwoWay**. Severity→brush-key
    **reuses the `DiagnosticRowViewModel` convention** (`BrushKeyFor`/`GeometryKeyFor` are public statics, so the
    mapping is testable); every binding inside the control is an **element binding onto itself** (`#Root.…`), so
    the host's DataContext flows through untouched; chrome overridable per host via `BorderThickness`/`Margin`;
    **Copy is self-contained** (clipboard from `TopLevel`). No `Header` property — nothing needed one (#233).
    **4b** — the debugger Error Bar + the launch Preflight rows migrated (no visual change). Preflight's severity
    split is now the item's own `DebugPreflightItem.BannerSeverity` (blocking → Error, else Warning) so **no
    severity logic stays in the view**. Deleted as redundant: `DebuggerTabView.OnCopyErrorClick` (the banner
    copies) and the VM's `ToggleErrorExpandedCommand` (expand is the banner's own gesture, two-way bound to
    `IsErrorExpanded`, which the VM still owns so a fresh fault re-expands). **4c** — mechanical adoption:
    the `ErrorMessage` status line on **all 10 object editors** (Domain/Exception/Function/Generator/Index/
    Package/Procedure/Table/Trigger/View), the **Execute Procedure/Function failure** in the results area
    (`ExecError`, `VerticalAlignment=Top` so it stays a bar over the hidden grid), the **Execute Procedure
    dialog** validation line, and the **Security Manager** banner; **Script Executor + Batch Results** dropped
    their local `DangerIconBrush` override for the shared `ErrorBrush` (`DangerIconBrush` is for
    destructive-action *icons*). **⚠ Latent bug found + fixed:** the Batch Results headline also set `Foreground`
    **locally**, which outranks any style setter — so its `Classes.error` "failed" tint could never have applied;
    removing the local value realizes the intent. **No module's behaviour/workflow changed.** Deliberately NOT
    migrated (documented scope): the SQL Editor **Messages log** (a scrolling row list), the **in-grid data-error
    empty states** (Table `DataError`/`EditStatusMessage`, View `DataError` — a grid empty-state, not a banner),
    and dialog **field-level** validation hints (`AddFieldDialog`), plus the Procedure/Function `ExecInfo` error
    note inside the compact metrics strip. New `UiStrings.MessageBanner{Copy,Expand,Collapse,Dismiss}Tooltip`
    replace the four `DebuggerError*Tooltip` constants. +2 tests (headless pin: every severity's brush resolves
    in **both** themes + its geometry resolves + the control constructs and re-derives both keys on a severity
    change; `BannerSeverity` follows `IsBlocking`); −1 (the superseded preflight-token pin). Build 0/0;
    **5157 green**; smoke clean.
  - **Seam 4 QA round → DONE (commit `4bd1a6a`).** The user's visual QA found the banner was not yet *one*
    component to the eye; all fixes ratified before implementing. **(1) Full banner unification:** the message
    **text now carries the severity brush** (like the stripe + icon) — an Error message reads as an error in
    full, which was the whole complaint (every non-migrated surface painted its error text red, the banner
    didn't). **Chrome moved out of the control's own XAML into exactly TWO shared variants** in
    `ControlStyles.axaml` — `controls|MessageBanner` (standalone, full border, the default) and
    `.docked` (`0,1,0,1`, a strip attached to a panel edge). **This is load-bearing:** a local value on the
    control **outranks a style setter**, which is exactly how six per-host variations crept in during Seam 4 —
    with chrome in the styles there is nowhere to put a seventh. `ShowCopy` now defaults **true** (not a
    per-host decision). Pinned by a headless test that hosts both variants in a real window and asserts the
    **applied** chrome. **(2) Group A** — 7 stragglers migrated: Procedure/Function `ExecInfo` error, Table
    `DataError` + `EditStatusMessage`, View `DataError`, Performance panel error, Export dialog error.
    **(3) Group B** — `DangerIconBrush` no longer paints **message text**: Trace Monitor error row + error
    detail, Script Executor + Batch Results `result-failed` → `ErrorBrush`. `DangerIconBrush` keeps its real
    job: **destructive-action icons** (trash / stop / rollback). **(4) SQL Editor Messages stays a LOG**
    (timestamp column + scrolling rows, **not** a stack of banners), but a **problem** entry speaks the same
    language — severity stripe + icon + colour — and the mapping is **not re-derived**: `QueryMessageViewModel`
    reads `MessageBanner.BrushKeyFor`/`GeometryKeyFor`, so log and banner cannot drift. Info rows keep the
    normal reading colour and earn no marker (a log is mostly Info; greying it all costs legibility for no
    signal) — that is what `MessageBrushKey` vs `SeverityBrushKey` encodes. **This also collapsed a genuine
    duplicate: two `MessageSeverity` enums (`Controls` + `ViewModels`) became one** (`Controls`, now with
    `Success`). **(5) Variables changed-value** left amber for the debugger's **current-line hue** (dark
    `#3D5A8AC8`, light `#330033B3` — same base as `DebugCurrentLineColor`): "this changed" is part of the
    debugger's visual language, not a separate accent; the two never share a control, and alpha sits just under
    the editor wash because `PanelBrush` makes the same value read stronger. **(6) `DebuggerIcon`** breakpoint
    dot 6 → **4.5** at the same centre (16, 15.5) and the same tip overlap — an accent on the pointer, not a
    second subject; canonical `Assets/Icons/Debugger/debugger.svg` updated with it. **Left out by decision:**
    dialog **field-level** validation hints (`AddFieldDialog` et al. — a hint beside a field, not a module
    message). Build 0/0; **5159 green** (+2). **⚠ Rule going forward: a `MessageBanner` host sets only
    `Severity`, `Message`, `IsVisible`, optional `Classes="docked"` and layout `Margin`/`Grid.*` — never
    `Background`/`BorderBrush`/`BorderThickness`/`ShowCopy`.**
  - **Build/Test/Smoke:** build 0/0 and full suite green after every seam (**5156** through Seam 3, **5157**
    after Seam 4, **5159** after the Seam 4 QA round). Smoke: the app launches clean. The visual results of
    Seams 0–4 (bare-var squiggles, shortcut chips, current-line wash, Variables spacing + the new blue
    changed-value wash, every message banner, the Messages-log problem rows, and the smaller debugger-icon dot,
    in **both** themes) **await the user's visual QA**.
  - **Seam 5 — edit code during debugging → DONE (5a `7401060` · 5b `846ecf2` · 5c `19aa236`).**
    **5a (editable editor + change tracking):** the debugger source editor is a **normal editor at every
    phase**, incl. a live/paused session — typing never ends a session, *saving* does. The hazard was that
    stepping rewrites the display on every frame change, so the **edit buffer is now separate from the
    frame-source display**: `_source` = what the DB holds (the baseline the running session was compiled
    from), `_editBuffer` = the root routine's editable text, `SourceText` = the buffer for the **root** frame
    / that frame's own source otherwise. **ONE helper `SourceForFrame(frame)` makes that choice and all four
    `SourceText` assignments go through it.** `IsReadOnly` survives with a narrower, **structural** meaning —
    while a callee/caller frame is selected the editor shows *another routine's* source, which this tab cannot
    save; `IsSourceEditable` says so and the VM **also rejects the edit**, and frame selection runs through the
    one `SetSelectedFrame` funnel that raises it. `IsSourceDirty` is a **diff**, not a flag (edit back → clean).
    Editor text flows both ways (`SyncEditorText` suppresses its own write). The three debug renderers already
    clamp to the document, so a shortened buffer can't make them throw.
    **5b (save + compile):** reuses the object editors' machinery — same Ddl-lane `FirebirdDdlExecutor`, same
    `ConfirmRequest` dialog, same `ISavableObjectEditor`. Flow: live session → warn plainly → `Stop` (rollback
    + close attachment, §4.4) → compile → on success adopt the text, **re-parse**, and land **ReadyToLaunch
    with the pre-flight re-run against the NEW code**; on failure the error goes to the shared `MessageBanner`
    Error Bar and the buffer is kept. Declining returns failure without touching session or buffer (Cancel is a
    real cancel). **Breakpoints are cleared on save** — their offsets belong to the old text and "keep the line
    number" would silently move them (§0). **⚠ A PACKAGE member tab can NEVER save** (`IsSavable` false): its
    source is *reconstructed* as a standalone `CREATE PROCEDURE/FUNCTION`, so compiling it would create a
    standalone routine instead of altering the package (rule #11) — the refusal is **in the VM**, not just the
    wiring. Toolbar Save + **Ctrl+S**.
    **5c (close guard):** `DebuggerTabViewModel` implements `IUnsavedWorkSource`; `WorkspaceTabViewModel`'s
    `UnsavedWork`/`SavableEditor` stop returning null for `Debugger` (`SavableEditor` is **conditional** —
    a package-member tab reports its work but is never offered Save). **Per-tab close promoted from
    Discard/Cancel to three-way Save / Discard / Cancel** for every tab with somewhere to save — benefits ALL
    editor tabs, matching disconnect + app close. **⚠ Hardening worth knowing:** the close proceeds only if
    `tab.UnsavedWork` is genuinely null afterwards, **not merely if `SaveAsync` reported success** — the
    adapters can return success without compiling anything (no `DdlExecutor` ⇒ `ExecuteCompileAsync`
    early-returns, `ErrorMessage` stays null), and this is the one place where a wrong "success" destroys code.
    *(The adapter contract itself is a separate fix — see the spawned task.)* Three existing close-guard tests
    moved `ConfirmationRequested` → `ChoiceRequested` (Confirmed → Discarded). Build 0/0; **5169 green** (+12
    across the three seams); smoke clean. **Live QA still owed:** the successful save+compile path needs a
    server (edit a paused routine → Save → warning → session ends → recompiles → ReadyToLaunch with the new
    code; a deliberate compile error surfaces in the Error Bar; closing a dirty debugger tab offers Save).
  - **⭐ SEAM 6 REPLANNED (2026-07-24) — the old "Seam 6 = Quick Fix design doc" is SUPERSEDED and DEFERRED.**
    Live QA of Seam 5 produced findings that change the debugger's architecture; the new Seam 6 plan below is
    **ratified — do not re-analyse it, do not re-litigate the decisions.** A new session starts at **Seam 6a**.
    - **⭐⭐ THE ARCHITECTURAL DECISION — "Draft is the session's source".** QA found that editing in the
      debugger did **not** affect the session: even Restart re-ran the OLD code. Verified in code —
      `LaunchAsync` builds `DebugLaunchSpec(_source, _body, _model, …)`, i.e. the DB baseline; the edit buffer
      never reaches it. **But the investigation showed this is a removable limitation, not a law:** the
      debugger **never asks the server to run the compiled routine**. It is a client-side interpreter — control
      flow from the AST, every statement executed as an anonymous `EXECUTE BLOCK` **harness that never names the
      routine**. For a ROOT frame the catalog is consulted for exactly ONE thing: **parameter / `RETURNS`
      types** (`FirebirdDebugMetadata.ReadProcedureParametersAsync`). Locals (R3 verbatim), sub-routines (R5),
      the body and every statement already come from the **parsed text**; a trigger's NEW/OLD types come from
      the target TABLE, not the trigger. **And that one dependency is already solved in this repo:**
      `FirebirdDebugMetadata.BuildLocalRoutineFrameVariablesAsync` (D9) builds a complete frame for a local
      `DECLARE PROCEDURE` — which has **no catalog row at all** — by reading param/`RETURNS` types from the AST
      header (`PsqlDeclarationExtractor.ExtractSignature`), resolving base types via `RDB$FIELDS` (domains exist
      independently of the routine). **A draft-sourced ROOT is that same proven path applied one level up.**
      This is why IBExpert can run edited code without saving — same reason, not a trick.
      **Fidelity Law holds** (Firebird still computes every statement's semantics), but it introduces a **NEW
      §F boundary class** to disclose: wherever the **server** resolves the routine BY NAME it gets the
      COMPILED version while the client interprets the draft — (1) **recursion stepped OVER**, (2) a
      **selectable procedure used inside its own body**, (3) a draft that **would not compile** runs partially
      (Firebird's PSQL compile-time validation never ran). (1)+(2) are statically detectable from the draft's
      own AST — the pre-flight already does exactly this kind of scan (§4.6).
      **⛔ REJECTED alternative — temporary DDL** (`ALTER PROCEDURE` in the debug transaction + `ROLLBACK`):
      gotcha #213 (a transaction cannot use an object whose DDL it has not committed) is precisely this
      pattern, "no temporary metadata" is a binding Developer-Contract rule, and it is **unnecessary** since
      the harness never references the object.
    - **Ratified model (IBExpert-like):** the first edit **automatically ends the live session**; the user
      **stays in the debugger tab**; **Restart / Launch build the session from the DRAFT**; the database is
      untouched until Save; the debugger never runs the old code after an edit.
    - **⭐ SAVE — THIS REVERSES THE SEAM-5b/hand-off DIRECTION.** The debugger is now a first-class
      **iterative** debugging surface: `Edit → Restart → Test → Edit → Restart → Test → Save`. **Save compiles
      the current draft, writes it to the DB, and KEEPS the user in the debugger tab**, refreshing session
      state to the new version. **No hand-off to the object editor** (the earlier plan to transfer the code to
      the Procedure editor is dropped). **Easy Mode stays the classic DDL editor's** — the debugger saves the
      **source text only**.
    - **Seam order (ratified):**
      **6a — Debugger status → the app Status Bar. ✅ DONE (`0587307`).** The Save button left no room for the message after
      `Break on exception`. Move `DebuggerTabViewModel.StatusText`/`IsFaulted` presentation out of the debugger
      toolbar into the bottom status bar, freeing the toolbar for `Break on exception` + future buttons.
      **NO second mechanism:** the debugger VM is unchanged — `MainWindowViewModel` already exposes
      `ActiveDebugger` + `IsDebuggerTabActive` (the gotcha-#25 notify chain) and Avalonia propagates the nested
      VM's `PropertyChanged`, so this is a **pure XAML binding** in the status bar's column 1 beside
      `QueryStatsText` (each `IsVisible`-gated), with `Classes.error` on `IsFaulted` reusing the shared
      `.error` style. Note the bottom status bar (`MainWindow.axaml`, `Grid.Row=2`) shows connection state
      (`MainWindowViewModel.StatusText`, owned by `UpdateStatusFromConnection`/`SetError`/`ClearError`) — do
      **not** write debugger status into that property; it is a second owner of one field.
      **6b — `SaveAsync` false success. ✅ DONE (`33d4e88`).** `SourceObjectDetailTabViewModel.ExecuteCompileAsync` has TWO silent
      exits (`if (DdlExecutor is null) return;` and `if (string.IsNullOrWhiteSpace(sql)) return;`) that leave
      `ErrorMessage` null, so the adapter reports **Success = true having compiled nothing**; the same shape
      exists in every other `ISavableObjectEditor`. Make them set `ErrorMessage` so the adapter tells the
      truth. Side effect to accept + state: Compile on empty source stops being a silent no-op. **Keep** the
      Seam-5c defensive check (`tab.UnsavedWork is not null` after a save) as belt and braces.
      **6c — the big one: rebuild the debugger on the Draft model. ✅ SUPERSEDED — it came back days later as
      the A/B/C arc at the top of "Current state": Seam A (`aa7f801`) + Seam B (`7a410c7`) are DONE +
      user-QA-confirmed (first edit ends the session; Restart runs the draft with NO compile and NO DB write;
      Save is the only DB write). Seam C was then re-analysed, re-scoped and delivered as **C3** (the launch
      configuration survives a signature change), leaving only **C3.4** (root frame layout from the AST header
      ⇒ the header interim disappears, `TYPE OF` resolved, pre-flight surfaces the draft §F boundaries, and the
      mandatory `DebuggerFidelityProbe` catalog-vs-draft case) — deferred by decision.**
      Two details of the original 6c sketch were **changed by the implementation**, do not restore them: the
      re-parse is **lazy, not debounced** (a `DispatcherTimer` re-introduces an untestable path, #251), and
      Save was already delivered separately (it compiles the draft and resumes in the tab).
      **6d — refresh every open tab showing the same object after a successful compile. ✅ DONE + user-QA-confirmed
      (commit `6afef20`).** Root cause (confirmed, not a cache): **nothing subscribed the notification.** The
      editors already raised `CompiledExistingObject` after compiling an existing object and **nobody listened**;
      `Metadata.RefreshAsync()` refreshes the object *tree*, not open tabs' loaded source. **NO stale cache** —
      the readers read live and the Metadata lane uses implicit per-command transactions, so committed DDL is
      visible. As built: the debugger raises the **same** event after its save (no debugger path); the one
      subscriber lives on `WorkspaceTabs.CollectionChanged` (ONE wiring point, not the ~39 add sites, so a future
      tab kind is covered without anyone remembering); sibling lookup keys on **(kind, name)** exactly like
      `CloseTabsForObject` and the open/focus dedup; the reload is each editor's own `RefreshAsync` via a new
      `WorkspaceTabViewModel.RefreshAsync()` that mirrors the `SavableEditor`/`UnsavedWork` per-kind dispatch.
      **Two ratified exclusions:** a tab with **unsaved work is never refreshed** (`RefreshAsync` reloads and
      clears dirty ⇒ refreshing a dirty sibling would destroy edits — rule #11), and a **debugger tab is never a
      refresh TARGET** (reloading resets the source its session was built from + tears down a live session — the
      Draft model's business). Decision (`TabsNeedingRefreshAfterCompile`) is separated from the doing so it is
      assertable without a DB. **⚠ Refactor forced by 6d:** a debugger tab hard-coded `ObjectKind = Procedure`
      (harmless while nothing read it) — `CreateDebugger` now takes the real kind, **required** so the compiler
      enumerated the call sites. ⚠ **`OfferRecompileDependentsAsync` remains DEAD CODE** — it was this event's
      intended consumer and has never been called; reviving it changes what Save does and was left alone
      deliberately. Gotcha #258.
    - Grounding facts kept from the Seam-5 plan: `ISavableObjectEditor.SaveAsync` pattern in
      `SourceObjectDetailTabViewModel.cs` (~`:489-544`); `MainWindowViewModel.RequestCloseTabAsync` is now
      three-way; the debugger's launch path is `OpenDebuggerForObject` / `OpenDebuggerForPackageMember`.
      **⚠ A debugger tab has no `MetadataObjectKind`** — 6c/6d may need it threaded through (additive).
      **⚠ kill any lingering `EmberTern.exe` before rebuilding** (locks output DLLs → MSB3021).
- **Stage X — Firebird Debugger. ⭐ STATUS (2026-07-25): P1 + P2 + D1–D13 DONE, D14 deferred, D15 complete
  (D15.6 dropped), functions-as-root complete, the Draft model delivered (A + B + C3), Seam 6d done — all
  user-QA-confirmed. **The debugger is CLOSED**; D14 and C3.4 stay deferred by decision — see the top of
  "Current state". The block below is the accumulated
  per-milestone record; read it for the "why", not for status.** Historical detail: **implementation STARTED; P1 + P2 + D1–D9 DONE. D9 (local procedures & functions — THE FLAGSHIP) COMPLETE + live-fidelity-verified (core 2026-07-18, seam c 2026-07-19): local routines are real, steppable debugger frames with real closure variables (the capability IBExpert cannot deliver); both local procedures and local functions are faithful step-into AND step-over. §6.3 closure version gate MEASURED (FB3 = closed scopes, FB5 = true closures; frame LexicalParent branches on version); seam (a) = local-routine step-into (AST + parser + binder + extractor R5; runtime ResolveRoutine + AST-header param types); seam (b) = closures — Part 1 closure capture for step-INTO (read+write an OUTER var, the write reaching the parent frame), Part 2 the transitive read/write-set fixpoint over the sub-routine call graph for step-OVER (a local call whose callee captures an outer var not named at the call site). All proven sim==real on the lab. D9 seam (c) — local-FUNCTION step-into (§6.4) — COMPLETE 2026-07-19 (c1 AST → c2 Core interpreter → c3 Firebird executor + live fidelity): a local function is a real steppable frame in all four value-consuming positions (v=f()/RETURN f()/IF f()/WHILE f()) via a Function Return Continuation, no new server path; live-proven sim==real for the four positions, six return types (INTEGER/BIGINT/NUMERIC/VARCHAR/BOOLEAN/NULL), shadowing (local shadows a same-named stored function), nesting, and closures (spec §15.11). 🏁 D9 FULLY COMPLETE — local procedures AND functions step faithfully, into and over. 🏁 D10 (Triggers) COMPLETE + user-confirmed 2026-07-19 (seam A pure-Core / seam B live-fidelity / seam C UI; NEW/OLD context, multi-action, embedded-subquery colon fix #248) — PLUS terminal debug states (Completed keeps state + END marker, Faulted stops on the raising line + red status). D10.5 (UX polish) DONE 2026-07-19: the debugger's internal harness-audit tab (formerly "Executed SQL" — a misleading name; it was never the user's SQL history) is renamed "Harness Log" and made a DEBUG-only diagnostic surface — it is built in code-behind under `#if DEBUG` (`DebuggerTabView.axaml.cs` → `BuildHarnessLogTab`, no longer in the XAML), so in RELEASE builds the tab does not exist at all (not hidden / not disabled — genuinely not compiled). No new setting/toggle (user rejected reusing the per-connection DDL Developer Mode — different domain). The audit log itself (`DebuggerTabViewModel.ExecutedSql`) is unchanged and still collected in every build — it also feeds the Immediate tab — so Immediate / Evaluate(Shift+F9) / Watches are untouched. A purpose description + empty-state explain the tab is a debugger-internals diagnostic, not user-SQL history. Build 0/0 in BOTH Debug and Release; 4929 tests green. 🏁 D11 (Packages) COMPLETE + user-confirmed 2026-07-20 (Seam 0 / A / B / C) — packaged procedures (public AND private) are real steppable debugger frames, reached both by stepping into them from a caller and by launching a member directly as the ROOT; one execution path, no parallel package executor, live-fidelity-proven. **Seam 0 (lab + blocking probes, commit f229b94):** extended the lab with `PKG_DBG` (public `PUB_RUN`/`PUB_ADD` + a PRIVATE `PRIV_DOUBLE`; a private and a public sibling call inside `PUB_RUN`) + standalone selectable `SP_DBG_PKG`; §8.2 probes measured live (§15.12) — a PRIVATE package routine is NOT callable from `EXECUTE BLOCK` (SQLSTATE 42000 "is private to package") ⇒ interpret it; a PUBLIC one IS (⇒ real step-over / source-fetch step-into); the whole body is verbatim-extractable from `RDB$PACKAGE_BODY_SOURCE` (parse the blob). **Seam A (pure Core, commit ead3a41):** `ExecuteProcedureStatement.PackageName` + parser reading a qualified `PKG.PROC` (binder still references the package at the first name token; `SqlParameterScanner` returns the qualified name); new `SqlParser.ParsePackageBodyMembers` turns a body blob (`BEGIN <members> END` of bare `PROCEDURE/FUNCTION` routines = the D9 sub-routine shape WITHOUT `DECLARE`) into member `SubroutineDeclaration`s (`ParseSubroutineDeclaration` generalized to both leading forms; `ParseScopedBlockBody` reused — no hand-rolled scanner); private-ness stays a metadata fact. **Seam B (Firebird + live fidelity, commit e07ad40):** `ResolveRoutine` resolves a package call (qualified `PKG.PROC`, or an unqualified SIBLING from within a package frame) through ONE path — the member is reconstructed as a standalone `CREATE PROCEDURE` (`"CREATE "` + its `RDB$PACKAGE_BODY_SOURCE` slice) so the **D8** path (scope-bound model+body, catalog params keyed by `RDB$PACKAGE_NAME` via a generalized `BuildFrameVariablesAsync`, arg seeding) applies to a PUBLIC member, while every package routine is declared as a harness sub-routine (**D9 R5**) so a PRIVATE sibling — not DSQL-callable (§15.12) — runs inside the harness like a local routine; a package member is a closed scope (`LexicalParent` null, no capture ⇒ the read/write fixpoint is a no-op) so `ExecuteStatement`/`EvaluateCondition`/`BindValues` are untouched. Public members maximally reuse D8, private maximally reuse D9 — **NO parallel package executor** (user directive). Live fidelity PROVEN sim==real (§15.13, `DebuggerFidelityProbe` +2): case 18 step-**Into** `SP_DBG_PKG → PUB_RUN → PRIV_DOUBLE (private, interpreted) + PUB_ADD`, depth 3, sim 16==real 16; case 19 step-**Into** `PUB_RUN` then step-**Over** its siblings — the private `PRIV_DOUBLE` runs via the R5 harness (depth 2, never a frame), sim 16==real 16; all 17 prior cases still pass. **Seam C (launch a member as the debug ROOT — C1 engine `fd50411`, C2 UI `0b6259d`):** the "Debug procedure…" entry point on a package member, launched as the root. **C1** — `SqlParser.ReconstructPackageMemberSource` is now the ONE owner of the `"CREATE "` + member-slice reconstruction (the seam-B step-into path was refactored to route through it); `FirebirdDdlReader.FetchPackageMemberSourceAsync` reads the raw `RDB$PACKAGE_BODY_SOURCE` blob and reconstructs the member's standalone source (the App/probe root source provider); `FirebirdDebugExecutor.CreateAsync` gained an optional `packageName` that builds a package ROOT frame exactly as seam B builds a stepped-into member (package-keyed catalog params + every package routine as a harness sub-routine R5 so a sibling resolves + package/members on the frame context; closed scope ⇒ Execute/EvaluateCondition/BindValues untouched); `DebugLaunchSpec.PackageName` (additive) threaded through the launcher; `DebuggerTabViewModel` gained a `packageName` arg; `MainWindowViewModel.OpenDebuggerForPackageMember` reuses the `OpenDebuggerForObject` launch shape (same §9.3 parameter panel + launcher). **Live fidelity PROVEN** (`DebuggerFidelityProbe` case 20): `PKG_DBG.PUB_RUN(5)` launched as ROOT steps into private `PRIV_DOUBLE` + public `PUB_ADD`, chain `PUB_RUN → PRIV_DOUBLE → PUB_ADD`, sim R 16==real 16; all 19 prior cases pass. **C2** — the Package editor → Members tab "Debug procedure…" context menu (visible only for PROCEDURE members via `PackageMemberItemNode.IsProcedure`; a function-as-root is out of scope, §F), reusing the sidebar's `MetadataContextDebugProcedure` label + mirroring the tab's double-click code-behind; `PackageDetailTabViewModel.DebugMemberRequested` → the C1 launch path. No sidebar member leaf (packages don't expand) and no toolbar button (which member? = a new workflow) — the Members tab is the unambiguous entry point. Build 0/0; package/debug tests green; smoke clean; user QA confirmed 2026-07-20. **§F boundary:** a package FUNCTION call as a step-into is not modelled on the call side (`CallExpression` carries no package qualifier) — step-over, faithful — add only when a real lab case needs it (gotcha #233).
  **D12 (Advanced breakpoints) — COMPLETE + user-confirmed (2026-07-20). 🏁 Break on exception, conditional
  breakpoints + hit counts, data breakpoints, and run-to-next-`SUSPEND` (+ result grid) all ship end-to-end,
  live-fidelity-proven. Seams 0 / A / B / C1 / C2 / D / E1 / E2 all DONE.**
  Scope (spec §9.8): break on exception, conditional breakpoints + hit counts, data breakpoints, run to next
  `SUSPEND` (+ result grid). Split into seams (each pure-Core-first, additive, one committable seam per session,
  ending build 0/0 + green tests + user QA): **Seam 0** — lab verified + the deterministic `SP_DBG_LOOP(N)`
  workhorse (one routine serves run-to-SUSPEND / hit-count / conditional / data-bp; `SP_DBG_LOOP(5)` →
  `(1,5),(2,15),(3,30),(4,50),(5,75)`), commit `211a629`. **Seam A — Break on Exception** (`a8b160a`): a raise
  can PAUSE at the raising statement (`Paused`/`StopReason.Exception`, frame intact) BEFORE routing, then the
  next resume routes it through the very same `ExceptionRouter` path — a pause, never a second handler. The
  inline raise-handling was extracted into one `RouteRaisedException`; a held raise is `_pendingRaise` (raising
  step + pre-routing stack snapshot); `SetNextStatement` abandons it. `StopReason.Exception` now pairs with
  `Faulted` (terminal, unchanged) OR `Paused` (a break — `DebugSession.IsPausedOnException`). New
  `BreakOnException` (default false). **Seam B — Conditional breakpoints + hit counts** (`f9beba7`): `Breakpoint`
  became a **stop-policy object** (`Offset`, `Condition`, `HitCount` policy, hit tally; `ShouldBreak(satisfied)`
  = the pure policy — false/NULL never counts, true increments + breaks iff the hit-count policy is met) +
  `HitCountPolicy` value object (`Always`/`Exactly`/`AtLeast`/`Multiple`, `IsMetAt`) + `HitCountKind`;
  `BreakpointSet` promoted `HashSet<int>` → map `offset → Breakpoint` with the whole prior API byte-compatible
  (+ `Get`/`GetOrAdd`; `All` deferred to Seam E). A condition is **just an expression through the ONE engine** —
  new `IDebugExecutor.EvaluateCondition(string, int, Frame)` reuses the SAME typed-`BOOLEAN` `EvaluateExpression`
  path as `IF`/`WHILE` (no second evaluator; `NULL` → not-true, else `Convert.ToBoolean`, never parsing boolean
  text — §F). `DebugSession.ShouldBreakAt` evaluates it in the correct frame; a condition that RAISES stops +
  surfaces (`BreakpointConditionError`), never silently skipped. **Seam C1 — Data breakpoints** (`5561e10`):
  the watched value as **another stop policy** — `DataBreakpoint` (owns `ShouldBreak(old,new)` = "changed?",
  `NULL`/`DBNull` equivalent) + `DataBreakpointSet` (collection AND the LOCAL detection: `Snapshot(frame)` →
  `FindChanged(before, frame)`, names resolved via the scope chain so a closure var is watchable). `RunStepping`
  snapshots before a step and diffs after **only when the innermost frame is unchanged** (a frame-identity gate
  mirroring the D7 change-highlight — a step-into/out can't false-positive; a cross-frame change on return is a
  documented boundary); new `StopReason.DataBreakpoint` + `DataBreakpointHit`. Purely client-side (no server
  round-trip → no Firebird change, no live-fidelity for C1). **Seam C2 — Run to next `SUSPEND`** (this
  session): a new **run mode** `StepKind.RunToSuspend` (runs full speed like Continue; `StepPlanner` returns
  false — the stop is the SUSPEND *event*, not a movement decision) pausing with the pre-existing
  `StopReason.Suspend` at the step point AFTER a `SUSPEND` emits a row — a selectable procedure's "give me the
  next row". Detection is a **row-count delta** (`_emittedRows` grows only on `ExecutionStatus.Suspended`), so
  no new signal on `ExecuteCurrent`/the outcome; public `DebugSession.RunToSuspend()` (mirrors
  `RunToCursor(int)`), `Step(kind)` rejects it. Precedence mirrors data-bp (checked right after it, wins over a
  line stop, same coincidence boundary); a breakpoint *before* a SUSPEND still stops. Result grid over
  `EmittedRows` is Seam E. All five Core seams are **pure Core + additive** (no
  App/UI wiring — that is Seam E; off by default → existing sessions byte-identical). **Seam D — Firebird +
  Live Fidelity (probe-only; no production code):** the most important seam — it proves every D12 mechanism
  behaves identically on the real engine AND, per the user's directive, stops at the same **logical moment** of
  execution (not just the same final result). Every mode is a client-side stop policy over the interpreter that
  already drives the REAL executor, so the values + the SUSPEND/condition/change/raise that triggers each stop
  are computed by Firebird; `DebuggerFidelityProbe` (+6 cases 21–26, new `SimulateStopsAsync` capturing the
  **sequence of stops**) proves sim==real live on FB5, grounded on `SP_DBG_LOOP(5)`'s real per-iteration
  `(IDX,ACC)`: **21** run-to-SUSPEND = 5 stops one-per-SUSPEND in order, each == real row; **22** run-to-SUSPEND
  over the D6 cursor; **23** conditional `IDX=3` stops exactly once (skips iters 1–2, condition evaluated on the
  engine); **24** hit-count `Exactly(4)` stops on the 4th arrival; **25** data-bp on `ACC` stops on every change
  `0→5→15→30→50→75`; **26** break-on-exception pauses AT the raise (before routing, frame intact) then routes
  through `WHEN` to `CAUGHT`==real, break-off identical (one path). All 20 prior D8–D11 cases still pass →
  `DebuggerFidelityProbe` **26/26 ALL PASS**. Build 0/0; debugger tests green (unchanged — Seam D added
  no production code). **Seam E1 — Breakpoints panel + Break-on-Exception toggle + data-bp gesture (`cab2f5b`):**
  the panel is a **pure VIEW of the Core `Breakpoint` / `DataBreakpoint` objects** — the VM keeps ONE
  `BreakpointSet` + one `DataBreakpointSet` as its own store, which the session SHARES (no mirroring), so a row's
  condition / hit-count / enable edit mutates the very object the engine consults. `BreakpointRowViewModel` /
  `DataBreakpointRowViewModel` are dumb projections; the hit-count kind→operand-enabled and condition editing
  route through Core (`HitCountPolicy.Of`). **Seam E2 — Run-to-`SUSPEND` command + Results grid (`9a96bb3`):**
  the toolbar Run-to-SUSPEND command drives `DebugSession.RunToSuspend()`, and a Results grid renders
  `EmittedRows` (reusing the shared grid). **Two manual-QA fixes:** (a) **gutter hit-test (`c8fc061`)** — the
  breakpoint margin was not hit-testable, so a gutter click never registered; fixed so toggling a breakpoint
  works. (b) **first-statement breakpoint (`863c89d` → refined + superseded by `01b66dd`)** — a breakpoint on the
  FIRST executed statement was always skipped. **Root cause:** `RunStepping` made the breakpoint stop-decision
  AFTER executing the current statement and advancing, so it structurally could not decide the statement a run
  command RESUMES from; launch always pauses at Entry on the first statement and the gutter only appears once a
  run is live, so the user's first breakpoint sits exactly on the statement the first Continue executed
  unchecked. **Fix — ONE pre-execute gate (`TryStopBeforeExecuting`), one semantics, no first-statement branch:**
  the stop decision now runs BEFORE executing the statement the IP points at, for EVERY statement in EVERY run
  mode (`Start` decides nothing — Entry is a pre-execution pause). A **resume-guard** lets a run command LEAVE the
  statement it is sitting on (no double-stop): it suppresses re-breaking the current statement when the pause was
  a delivered arrival OR the command is an explicit movement (Into/Over/Out step away → Step behaviour unchanged,
  per DoD); the sole un-guarded case is a RUN command resuming from Entry, so a breakpoint set at entry fires on
  the first resume like any later arrival. Routing a held raise is a different operation ("about to route", not
  "about to execute") and bypasses the gate. **Final D12 architecture (ratified):** `Breakpoint` is a **domain
  stop-policy object** (offset + `Condition` + `HitCountPolicy` + hit tally, `ShouldBreak` the pure policy), not
  a bare offset; `HitCountPolicy` is an immutable value object (`Always`/`Exactly`/`AtLeast`/`Multiple`);
  `DataBreakpoint` is another stop policy (change detection, NULL≡DBNull) with client-side snapshot→diff;
  Run-to-`SUSPEND` is a run mode whose stop is the SUSPEND *event*; Break-on-Exception is a pause-before-routing,
  never a second handler; a **condition is just an expression through the ONE D5 evaluation engine** (no second
  evaluator); **ONE breakpoint model is shared by the VM and `DebugSession`** (the panel edits the domain objects
  directly); there is **ONE decision point "before executing a statement"** for every run mode; and **Live
  Fidelity (`DebuggerFidelityProbe` 26/26 sim==real on FB5)** is the proof each mechanism matches Firebird's real
  values AND stop-moment. Build 0/0; full suite **4998 green**; user QA confirmed 2026-07-20 → **D12 formally
  closed.** D14 (Step-back) remains **optional — build only if real usage asks.**
  **D13 (Fast Forward — loop fast-forward) — COMPLETE + user-confirmed 2026-07-20 (Seam 0/A/B/C + docs-close
  Seam D). 🏁 Two commands ship end-to-end, live-fidelity-proven + manual-QA-confirmed: Continue Until Loop Exit
  + Next Iteration.** Scope
  (accepted, deliberately small): exactly **Continue Until Loop Exit** +
  **Next Iteration** — nothing else (Skip Current Iteration rejected = a control jump / new path; Continue Until
  RETURN deferred; Continue Until Exception / Variable-Changes / END subsumed by D12). **Hard constraint: no new
  execution path — Fast Forward only *controls* the existing `DebugSession`.** Both are pure stop policies on the
  **D12 `RunToSuspend` pattern** — new `StepKind.RunToLoopExit`/`RunToNextIteration`, `StepPlanner` returns
  false, the stop is a **loop-lifecycle event decided in `RunStepping`'s tail** (innermost loop captured once at
  the command; Loop Exit = the loop activation left the control stack, Next Iteration = its iteration counter
  incremented). **`IsInsideLoop`** gates the commands; `Step(kind)` rejects them; breakpoints inside the loop
  still win (pre-execute gate). **Seam 0 (`1049c71`):** 4 deterministic lab workhorses (`SP_DBG_LOOP_NESTED`/
  `_LEAVE`/`_BREAK`/`_EXIT`, `.fdb` rebuilt, live-verified) — and it revealed that the interpreter modelled **no
  `LEAVE`/`EXIT` control flow** (both fell to the server path → `LEAVE` faulted, `EXIT` was silently ignored).
  **Seam A (`3fd541e`):** the two run modes + a **user-ratified correctness patch** folding minimal
  `LEAVE`/`BREAK`/`EXIT` into `ExecuteCurrent` (EXIT → `Frame.ExitRoutine` terminates the frame; unlabeled
  `LEAVE`, and `BREAK` which the parser now maps to `PsqlLeafKind.Leave`, → `Frame.LeaveInnermostLoop`), plus a
  minimal `LoopActivation` base (only the iteration counter). **`LEAVE <label>` to an outer loop = §F boundary**
  (labels are not in the AST). Build 0/0; full suite **5013 green**; parser/AST/§0 after the `BREAK` change no
  regression. **Seam B (Live Fidelity — probe-only, NO production code):** extended `DebuggerFidelityProbe`
  **+8 cases (27–34)** over the 4 lab workhorses × both run modes, proving **sim == real on FB5** (values AND the
  logical stop-moment — statement, in-loop membership, rows-so-far, live vars): NESTED innermost-loop capture
  (Next Iteration advances inner `J`; Loop Exit lands in the outer body), `LEAVE`/`BREAK` exit to the post-loop
  statement (`BREAK ≡ LEAVE`), `EXIT` completes the session with 0 rows. **`DebuggerFidelityProbe` 34/34 ALL
  PASS** (all 26 prior D8–D12 cases still green); probe builds 0/0; no production code ⇒ test suite unchanged
  (5013 green). **Seam C (UI — thin presentation, no business logic):** two toolbar buttons (`↻ Next Iter`,
  `⤶ Loop Exit`) beside `⏭ SUSPEND`, bound to two new `[RelayCommand]`s (`RunToLoopExitAsync`/
  `RunToNextIterationAsync`) that delegate straight to `DebugSession.RunToLoopExit()`/`RunToNextIteration()` via
  the existing `RunStepAsync` background-step path; **gating uses the engine's own `IsInsideLoop`** (`CanFastForward`
  = `Phase==Paused && Session is { IsInsideLoop: true }`, added to the `Phase` `NotifyCanExecuteChangedFor` list
  so it re-evaluates on every step). No keyboard shortcut (mirrors Run-to-`SUSPEND`, toolbar-only). Build 0/0;
  compiled bindings validate the commands at compile time; manual QA confirmed (buttons enable only inside a
  loop; both fast-forward correctly on the live lab).
  **D15.2 (Toolbar Visual System & Error Bar) — STARTED; Seam A (icon system + toolbar) DONE 2026-07-21 (impl,
  awaits visual confirm).** Ratified icon-system decision: EmberTern does NOT build a parallel icon family —
  it extends the existing (Lucide-derived) `SvgIcon` system as the "EmberTern Icon System" (`.svg` canonical
  under `Assets/Icons/`, `IconGeometries.axaml` the runtime representation, reuse-before-create). The debugger
  toolbar was the last Unicode-glyph holdout (`▶ ⤵ ↷ ⤴ ■ ↻`); now on `SvgIcon`. Concept board approved (2
  refinements: Next Iteration = two-arrow cycle vs Restart moved to skip-to-start; Break on Exception =
  stop-octagon+`!`, not an energy bolt). 9 new geometries `Icon.StepInto/StepOver/StepOut/RunToCursor/
  RunToSuspend/NextIteration/LoopExit/Restart/BreakException` (`Assets/Icons/Debug/*.svg` + mirrored);
  Continue reuses `Icon.Play`, Stop reuses `Icon.Stop`. **Colour = hierarchy, not decoration:** Continue →
  `AccentIconBrush` (sole primary); steps + run-to + Restart → neutral; Next Iteration + Loop Exit → new
  shared `DebugLoopIconBrush` (teal, both dicts — NOT the PSQL-keyword violet, which would cross domains);
  Stop → `DangerIconBrush`; Break on Exception → `WarningIconBrush` (a mode, not destruction). Labels stay
  neutral text (only the icon carries the category → 6 neutral / 4 hues, calm). Removed 3 now-unused
  `Debugger*Content` glyph strings. Pure Presentation. Build 0/0; +1 headless pin (all 11 toolbar geometries +
  `DebugLoopIconBrush` both themes resolve); 5099 tests green; smoke clean. **Seam B (debugger identity icon,
  replacing `Icon.Bug`) — DONE + user-accepted 2026-07-22.** `Icon.Bug` replaced at all THREE
  debugger entry points — Procedure + Trigger editor toolbar "Debug…" buttons + the Debugger **tab** (which had
  been misusing the Continue `Icon.Play`) — with one unified identity mark. **First metaphor
  (playhead-on-a-branching-path) was shipped then REJECTED by the user** — not a quality problem, the metaphor
  itself didn't read as "debugger" (less legible than the bug). **Ratified replacement: a two-colour composite
  — a blue Play triangle (execution pointer, dominant) + a small red breakpoint dot nested into its lower-right,
  overlapping the tip so the two read as ONE "Start Debugging" glyph.** Two colours + a filled dot can't be a
  single stroked `SvgIcon`, so it is a dedicated composite control `Controls/DebuggerIcon.cs` + its ControlTheme
  in `IconGeometries.axaml`; both colours are **reused tokens** (`AccentIconBrush` + the gutter
  `DebugBreakpointBrush`), both dicts, same idiom (24×24, 2px stroke, round caps/joins). Tab branches on a new
  presentation-only `WorkspaceTabViewModel.IsDebuggerTab`; the orphan `Icon.Bug` geometry removed. Pure
  Presentation. Build 0/0; pin test extended (constructs `DebuggerIcon` + pins both brushes both themes);
  tests green; smoke clean. **User-accepted 2026-07-22** (a better mark may be revisited in the future Visual
  Polish sprint; `DebuggerIcon`'s ControlTheme is the one place to change).
  **Seam C (Error Bar) — DONE 2026-07-22 (impl, awaits visual confirm) → D15.2 COMPLETE.** The fault message
  moved out of the toolbar status line into its **own row** (root grid `Auto,Auto,*`; `Grid.Row=1`), shown only
  on a fault / Break-on-Exception pause (`ShowErrorBar`), collapsing to zero height otherwise so the toolbar
  never moves. Calm: `PanelBrush` + a thin 3px `ErrorBrush` left stripe + an error-toned icon (no loud fill).
  **Shows the FULL message by default** (user QA refinement — FB errors are short (2–6 lines), so
  default-collapse gave no benefit); sizes to content, capped ~8–10 lines (`MaxHeight=190`) + scrolled for the
  rare long one; **Expand/Collapse** is the opt-in one-line safety valve; font 12 + `LineHeight=18` for
  readability. **Copy** (clipboard, view code-behind), **Dismiss**. The status line is now a short fixed-height
  headline (`DebuggerStatusFaulted`);
  the full FB message lives only in the bar (the in-row `StatusText` bug fixed). Pure Presentation — the VM
  projects `ErrorDetail`/`ShowErrorBar` over the engine's `DebugError` + owns the expand/dismiss view-state,
  Core untouched. Build 0/0; +2 `DebuggerTabVmTests`; smoke clean. Guide: d15 doc §4.
  **D15.3 (Launch & Entry Experience) — STARTED; architecture re-reviewed + ratified 2026-07-22; seam order
  C→A→B→D→E.** Ratified decisions: **F5 = always "Go"** (launch panel → Start Debugging, session → Continue);
  **Enter launches only from the last parameter field or the Launch button** (not any field); **Transaction
  Isolation → an Advanced section (collapsed)**, description-before-level, default Read Committed; **no-decision
  fast path** — a non-trigger routine with no parameters and a clean pre-flight skips the launch panel entirely
  (Debug → Preparing → session; a pre-flight warning or any parameter/trigger context keeps the panel).
  **Seam C — DONE 2026-07-22 (impl, awaits visual confirm):** keyboard-first launch (F5 in the panel; Enter
  scoped via a tunnelled handler to the last `ParamsList` input / Launch button; multiline value boxes keep
  Enter=newline), post-launch focus → editor `TextArea` (once per launch/relaunch, gotcha #225), ready-to-launch
  focus → first field / Launch button, and the no-decision auto-launch (`ShouldAutoLaunch()` in `PrepareAsync`).
  Pure Presentation + one VM guard; engine untouched. Build 0/0; +3 `DebuggerTabVmTests` (+1 boundary pin:
  defaulted/optional params keep the panel — a default is still a decision); smoke clean. **Seam A (compact
  form) — DONE 2026-07-22 (impl, awaits visual confirm):** `ParamRowTemplate` rebuilt — name primary + type
  subordinate (smaller/greyed) on one line, value beside, NULL an **inline toggle** (no standing column),
  tighter rows + less whitespace; shared by proc/func + trigger NEW/OLD grids. Pure Presentation (XAML + 2
  `UiStrings`); no VM change. **Seam B (isolation→Advanced) — DONE 2026-07-22 (impl, awaits visual confirm):**
  isolation moved into an **Advanced disclosure** (flat chevron+"Advanced" toggle → VM view-state
  `IsAdvancedExpanded`, mirroring the bottom-panel collapse; no unstyled `Expander`), **collapsed by default**;
  the note now leads with what the option changes (plain language, `(rec_version)` jargon dropped), then the
  level selector; default Read Committed. Main flow = params (if any) → Start. View + `UiStrings` + one trivial
  VM toggle. **Launch-panel Visual Polish backlog (user, deferred to the future Visual Polish sprint):** cap
  panel max-width on wide monitors · refine History-vs-parameters hierarchy · reconsider Start-button placement ·
  gentler success cue for "No issues detected". **Seam D (Quick Relaunch) — COMPLETE via REUSE 2026-07-22
  (verified, no new production code):** already delivered by deliberate reuse — the launch form is the shared
  `ExecuteProcedureDialogViewModel`, whose ctor auto-applies the newest `ParameterHistoryStore` set (persisted
  per-routine, across tabs/restarts; each launch's `Accept()` records it), plus the existing `RestartCommand`
  (toolbar + `Ctrl+Shift+F5`) reusing last values and Seam C's F5 on the pre-filled panel. Named favorites stay
  DEFERRED. +2 `DebuggerTabVmTests` (pre-fill via the debugger path; Restart reuses last values); build 0/0.
  **Seam E (Members-tab Debug button) NOT started** (folded into the discoverability backlog below).** Guide:
  d15 doc §5. **D15.3 CLOSED by the user 2026-07-20.**
  **D15.4 (Expression UX + Friendly Errors) — COMPLETE 2026-07-23 (Seams A+B); Seam C DEFERRED.** Split into
  three seams. **Seam A — Expression Hints (P, `ea6957e`):** terse input placeholders each carry a concise
  valid-expression example + a subtle monospace examples line under the Immediate/Watches empty-states (new
  shared `UiStrings.DebuggerExpressionExamples`); pure `UiStrings` + two empty-states. **Seam B — Friendly Error
  Mapping (P+F, `a7d34cf`):** Core `DebugErrorClassifier.Classify(DebugError) → FriendlyErrorCategory
  { UserException, ConstraintViolation, SqlError, Unknown }`, keyed on **SQLSTATE/GDS codes only** (never
  message text, §F), unit-tested; App `DebugErrorPresenter` (`Raw` = best raw field; `Describe` = friendly
  one-liner, `Unknown → Raw`) — the ONE text composer that removed the duplicated `?? Message ?? ExceptionName`
  from all four surfaces (`DebugExecutedSqlRowViewModel`, `WatchRowViewModel`, `PausedReasonText`,
  `DescribeError`). Friendly text on the three expression surfaces with the **raw FB message kept as a tooltip**
  ("Friendly + raw available", ratified); Error Bar keeps the raw body (D15.2). **GDS codes MEASURED live on the
  FB5 lab** (throwaway probe, removed): user `EXCEPTION` → `ExceptionName`; `{335544879 NOT NULL, 335544347
  CHECK, 335544665 PK/UNIQUE}` → constraint; `335544569` (isc_dsql_error) → SqlError. **Key finding:**
  token-unknown (-104), table-unknown (-204), column-unknown (-206) ALL arrive as `335544569` (DebugError
  carries only the leading GDS code) ⇒ one honest `SqlError` bucket; the precise split is Seam C's job. **Seam C
  — Local Pre-validation — DEFERRED to backlog (prove-before-build spike, NO production code).** The mandatory
  empirical gate FAILED cleanly: `DiagnosticsEngine` (semantic) flags an unresolved variable **only for a
  `:name` reference inside a PSQL body**; a **bare** identifier (`v_counter * 2` — the typical Immediate input)
  is a column candidate, gated on metadata, so with `metadata:null` it is **silent** (measured: 0 diagnostics
  for every bare shape). Reuse-only local pre-validation therefore can't see the common case without an engine
  change (binder resolving bare expressions against ambient) — out of D15.4's reuse-only scope, and SQL
  synthesis / colon-injection / `BEGIN…END` wrapping were rejected. **Ratified:** no debugger-only validator, no
  silent-in-the-common-case component; Seam B already gives friendly server errors. Seam C stays backlog — a
  future **separate Core Feature** ("resolve bare expressions against ambient", full engine-change rigour) if
  real usage asks. Build 0/0; 5132 tests green (A+B); smoke clean. Guide: d15 doc §6.
  **D15.5 (Inline Values) — COMPLETE 2026-07-23 (Seams A+B; awaits final visual confirm of the combined
  effect). Pure Presentation — Core + engine untouched.** Greyed `name = value` annotations at line ends in the
  debugger source editor. **Seam A (`efbc89f`):** new `InlineValuesRenderer` (App/Completion) — a member of the
  `IBackgroundRenderer` family (mirrors `CurrentLineRenderer`): draws in the empty space PAST each line's text
  end (position from `GetRectsForSegment`, correct under word-wrap/folding) so it **never shifts the document
  text** and never touches formatter/layout; appended after the current-line renderer (paints on the wash),
  repaints on the existing `DebugMarkersChanged → TextView.Redraw()` path. VM: `InlineValues`
  (`IReadOnlyList<InlineValueAnnotation>`), recomputed once per pause in `RebuildInlineValues`;
  `ApplySelectedFrame` reordered so the roster refreshes before `SetCurrentMarker` recomputes + fires the
  repaint. Clean P/VM split (VM decides which/where, renderer only draws). **Seam B — visibility policy, SIMPLIFIED after
  QA to used-only:** final rule (§7.1) = **show ONLY the variables the current statement USES** — real current
  values, no prediction (paused BEFORE executing). Used set = tokenize the current statement span with the one
  `SqlLexer` (reuse, like `WatchSideEffectDetector` — no new analysis) matched to roster names (case-insensitive;
  string/keyword never matches). **QA arc:** B first shipped `used ∪ changed-not-used` (used primary, changed
  appended, commit `f004144`); seeing it live, the changed-not-used tail (e.g. `V_SUM = 10` at `v_text = p_text;`)
  read as noise, so the changed-not-used loop was removed → used-only. Fixes the note that changed anchoring read
  as if it referred to the line's instruction. **Boundary (§F):** a trigger context column's dotted name
  (`NEW.STATUS`) isn't a single token → not "used". Build 0/0; 5136 tests green (used shown + anchor,
  changed-not-used excluded, empty when not paused; + renderer pin); smoke clean. **D15 next: D15.6 (Debugger
  Performance — integration).** Guide: d15 doc §7.
  **D15.6 (Debugger Performance) — DROPPED 2026-07-23 (ratified; spike done, no product justification).** A
  prove-before-build spike (`tools/probes/DebugPerfBlockProbe`, since removed) tested **variant M-A** — the
  whole procedure body as ONE `EXECUTE BLOCK` fed to the existing Performance Analysis module (wired exactly as
  `MainWindowViewModel`), over selectable / non-selectable / full-scan scenarios on a 20k-row scratch table:
  M-A is **feasible and its DATA is trustworthy** (per-table reads + advisor findings match the baseline, plan
  is real) — **only execution TIME is tainted** (harness overhead). **Dropped anyway on product judgement:** an
  M-A Performance tab profiles the WHOLE procedure, which the user already gets from the Procedure editor's
  Performance tab; the extra plan richness + same-params convenience is too little to justify a second
  performance surface in the debugger. The debugger's only unique value is **per-statement** profiling of the
  currently-executing instruction — a separate, much larger feature (own architecture + spikes), out of scope
  now. Guide: d15 doc §8. **D15 is now effectively complete** (D15.1–D15.5 done, D15.6 dropped, D15.7 audit is
  background).
  **⭐ CURRENT WORK — Debugging standalone PSQL FUNCTIONS as a debug ROOT (the functional gap; NOT D15).**
  Distinct from D9 (which already makes a LOCAL `DECLARE FUNCTION` a faithful step-into/over frame *within* a
  routine) — this is a function as the **entry point**. Standalone-first (A/B/C1/C2 ratified); packaged
  functions as root = a later follow-up. **Seam A (Core function root frame) — DONE (`765272c`; pure Core,
  additive).** `DebugSession` gained optional `rootReturnType`; when non-null, `Start()` pushes the root as a
  **function frame** so a root `RETURN <expr>` is computed via the Expression Harness (`EvaluateReturn` — the
  same path D9 proved) and its value is kept on `FinalFrame` (no caller to deliver to). `Frame.IsFunctionFrame`
  widened **additively** to `ReturnContinuation is not null || ReturnType is not null` (local function still
  qualifies via its continuation; root function via its return type; only one usage — the RETURN gate — and
  `ApplyReturnContinuation` independently guards on the continuation, so a root's value stays on the frame with
  no delivery). Procedures/triggers/package-proc/anonymous-block roots pass null → not function frames →
  byte-identical. +2 `DebugEngineTests`. **Seam B (Firebird function-root layout + live fidelity) — DONE
  (`618dc13`).** "Resolve once, pass through" (ratified): the RETURNS **base type** is resolved ONCE in the
  Firebird layer during the SAME `RDB$FUNCTION_ARGUMENTS` read that builds the function's input params, then
  threaded — no re-derivation. `FirebirdDebugMetadata.BuildFunctionFrameVariablesAsync` +
  `ReadFunctionParametersAsync` (return arg = `RDB$FUNCTIONS.RDB$RETURN_ARGUMENT`; inputs base-typed via
  `RDB$FIELDS`/`FormatType` R2, domain kept for the declaration R3 — mirrors `ReadProcedureParametersAsync`; no
  outputs/SUSPEND; FB3+ standalone). `FirebirdDebugExecutor.CreateAsync` gained `isFunctionRoot` → builds the
  function layout, registers the closed-scope root context, exposes the resolved type on new `RootReturnType`
  (the launcher will pass it to `DebugSession(rootReturnType:)` — C1); a function is a closed scope so
  Execute/EvaluateCondition/BindValues untouched, and `EvaluateReturn` (D9) already computes RETURN via the
  Expression Harness typed as `frame.ReturnType`. **Live fidelity PROVEN** (`DebuggerFidelityProbe` +3, reusing
  lab `FN_ADD_TAX`/`FN_FULL_LABEL` — no lab change): `FN_ADD_TAX(100,20)` as ROOT → sim 120==real 120, depth 1;
  `FN_FULL_LABEL` else → `'ABC - Widget'`; if/null branch → `'Widget'`; all 34 prior cases pass → ALL PASS.
  Build 0/0; full suite 5138 green (two partitions #94/#226); smoke clean. **Seam C1 (App launch wiring) —
  DONE (`5aa75d0`).** `DebugLaunchSpec.IsFunction` (additive); `DebuggerTabViewModel.PrepareAsync` resolves
  `_isFunction` once from `ddl.ObjectKind == DdlObjectKind.Function` (excluding a package context — packaged
  functions as root are a later follow-up), `LaunchAsync` threads it, `BuildParameters` labels the panel
  "Function"; `FirebirdDebugSessionLauncher` passes `spec.IsFunction` → `CreateAsync(isFunctionRoot:)` and the
  executor's `RootReturnType` → `DebugSession(rootReturnType:)` — resolve-once-pass-through end to end. Every
  non-function root → `IsFunction=false` → `RootReturnType` null → byte-identical. +2 `DebuggerTabVmTests`.
  Build 0/0; 5140 green; smoke clean. **C2 UX RATIFIED (2026-07-23):** the return value is presented as a
  dedicated **"Return" group in the Variables panel** (function-only, top — mirrors the trigger "Context"
  group), "— (not returned yet)" while stepping → the real value at completion; status line stays simple (no
  return value in it). C2 split into **C2a (Return surface) + C2b (entry points)**. **Seam C2a — DONE
  (`b02dee0`).** `DebugVariableKind.Return` + `↩` glyph + reuse of the OUT accent brush (no new token); a
  synthetic Return row (`DebuggerTabViewModel.BuildRoster`, function-only) in the new top-most `_returnGroup`,
  skipped in the generic frame-resolve loop, showing the pending placeholder while stepping and
  `FinalFrame.ReturnValue` only at completion (`UpdateReturnRowValue`; a NULL return shows `<null>`, distinct
  from pending). Real state only. VM-test infra extended (FakeLauncher passes `rootReturnType` for a function
  spec; `FakeExecutor.EvaluateReturn` yields a scripted value) → +3 `DebuggerTabVmTests`. Build 0/0; 5142
  green; smoke clean. **Seam C2b (entry points) — DONE (`c132e18`) → the whole standalone-function-as-root
  feature is COMPLETE (A/B/C1/C2a/C2b), live-launchable, awaits full manual QA.** Reuses the ONE launch path
  (`OpenDebuggerForObject`), mirroring the procedure/trigger pattern — no new debugger logic: sidebar "Debug
  function…" on a function leaf (`MetadataNodeViewModel.IsFunctionLeaf` + `DebugFunctionCommand` →
  `MetadataExplorerViewModel.RequestDebugFunction`/`DebugFunctionRequested` →
  `MainWindowViewModel.OnDebugFunctionRequested`); function-editor toolbar Debug button (`DebuggerIcon`, gated
  `IsFunctionDetailTabActive`, `ActiveFunctionDetail.DebugFunctionCommand`); `FunctionDetailTabViewModel`
  gained `DebugRequested`/`CanDebugFunction`(`!IsNew`)/`DebugFunctionCommand`; `CreateFunctionDetail` wires
  `DebugRequested`. Source = the existing `FetchObjectDefinitionAsync`/`FetchDdlAsync` (a CREATE FUNCTION
  parses to `DdlStatement{Function, Body}` → C1 `_isFunction` → B function root → C2a Return group). Build 0/0;
  5142 green; smoke clean. **⚠ gotcha:** a lingering smoke-test `EmberTern.exe` locks the output DLLs and a
  rebuild then reports MSB3021/MSB3027 copy "errors" (not compile errors) — `taskkill /F /IM EmberTern.exe`
  before rebuilding. **Packaged functions as a debug root — STARTED (follow-up to the standalone feature),
  D1→D2; directive: extend existing paths with `packageName`, NO parallel standalone-vs-packaged code.**
  **Seam D1 (engine + live fidelity) — DONE (`ebaa036`).** After D1 the ONLY difference between a standalone
  and a packaged function root is the presence of `packageName`: `ReadFunctionParametersAsync`/
  `BuildFunctionFrameVariablesAsync` gained an optional `packageName` (same query/join/typing, only the
  `RDB$FUNCTION_ARGUMENTS`/`RDB$FUNCTIONS` filter `IS NULL`↔`= @pkg`); the `isFunctionRoot` branch is
  package-aware (checked BEFORE the procedure-package branch so a packaged function is framed as a function);
  and both the function-root and the D11 procedure-package roots now register through ONE new package-aware
  `RegisterRootAsync` (standalone → `Register`; package member → `RegisterPackageMember` sibling context/R5).
  `FetchPackageMemberSourceAsync` gained a `kind` param (default Procedure; reconstruction already kind-generic
  — a FUNCTION slice keeps its `RETURNS`). Lab `PKG_DBG` +PUBLIC `PUB_FN` (calls private `PRIV_DOUBLE`), `.fdb`
  rebuilt (#149). **Live fidelity PROVEN** (`DebuggerFidelityProbe` case 38, `SimulateFunctionRootAsync` gained
  an optional `packageName` — one simulate path): `PKG_DBG.PUB_FN(5)` as ROOT → sim 11==real 11, depth 2, chain
  `PUB_FN → PRIV_DOUBLE`; all 37 prior cases pass → ALL PASS. Build 0/0; 5142 green; smoke clean. **Seam D2
  (App entry point) — DONE (`96c184a`) → packaged-function-as-root is COMPLETE, awaits manual QA.** VM dropped
  the `_packageName is null` exclusion (a packaged function member's source is reconstructed as CREATE FUNCTION
  → `_isFunction` true → launcher threads packageName + IsFunction → D1 combined path); `DebugMemberRequested`
  now carries the whole `PackageMember` (name + kind); `RequestDebugMember`/`CanDebugSelectedMember`/tooltip
  widened to FUNCTION members; a "Debug function…" Members-tab context-menu item gated on new
  `PackageMemberItemNode.IsFunction` (the toolbar Debug button is data-driven → auto-enables);
  `OpenDebuggerForPackageMember`/`FetchPackageMemberSourceAsync` gained `isFunction` → reconstruct as CREATE
  FUNCTION. Dead "FunctionLater" tooltip removed; tooltips made kind-neutral. Tests updated (function member now
  RAISES + enables Debug; node `IsFunction`). Build 0/0; 5142 green; smoke clean. **⭐ The whole function-as-
  debug-root feature (standalone A/B/C1/C2 + packaged D1/D2) is now complete end to end — no known function
  debugging gap remains.**
  **⭐ Backlog captured during D15.2 Seam B QA (2026-07-22; user directive: record in the plan, do NOT
  implement yet):** (1) **Debugger discoverability** — a **Debug button on the Package "Members" tab toolbar**,
  disabled by default, enabled only when the selected member is a debuggable kind (procedure/trigger/function);
  today debugging a member is context-menu-only → undiscoverable. (2) **Debugging standalone/packaged PSQL
  FUNCTIONS as a debug ROOT** — a functional gap (the debugger launches procedures/triggers/packaged procedures
  as a root, not a function-as-entry-point — a §F boundary; distinct from D9's local-function step-into). Both
  in d15 doc §5.2/§5.3-E. See [[project-d15-debugger-experience-planned]].
  **Philosophy held — no new execution path (Fast Forward
  only *controls* the session), the new features are stop policies (the `RunToSuspend` template), `Simulator ==
  Real Firebird` (live-fidelity-proven). D13 formally CLOSED.**
  **D14 (Step Back) — ANALYZED + DEFERRED by user decision (2026-07-20). Not started.** Full
  architecture/feasibility analysis accepted; the user chose not to build it now — it stays optional, revisited
  only when real debugger usage asks. Ratified if ever revisited (do not re-derive): a **new engine capability
  (reversible state)**, **replay rejected**, only **full-client-state snapshot + one savepoint per step +
  undo-only** (`ROLLBACK TO`, never re-execute — matches spec §9.8.5); a **second savepoint layer**
  (`ET_DBG_STEP_{n}`) not eagerly released; **v1 scope** single step-back over leaf/DML/IF/assignment +
  stepped-over CALL, **loops/`FOR SELECT` out** (a live cursor cannot be rewound), exception routing out; **§4.6
  irreversibles disclosed, not hidden**; fidelity via a round-trip invariant. **Difficulty High; ~5 sessions.**
  Full write-up: the plan's D14 STATUS block + [docs/history/19-...](docs/history/19-firebird-debugger.md) (D14)
  + [[project-d14-step-back-deferred]].
  **⭐ CURRENT STAGE — D15 (Debugger Experience & IDE Polish) — IN PROGRESS (planning complete 2026-07-20;
  D15.1 Seam A implemented 2026-07-21, see below).** The debugger is now polished to professional-IDE level as
  a **full project**, designed + ratified over two review passes. **One organising principle: Presentation vs Feature** (P = view/theme/tokens
  only, obeys "no logic in VM/Core"; F = new data/Core surface, full rigour + live fidelity). Milestones:
  **D15.1** Editor Readability (P, **app-wide** — readability-first palette, variables neutral; rebuilt
  full-width calm-blue current-line + gutter bar) · **D15.2** Toolbar + Error Bar (P — a whole **SVG icon
  system** in EmberTern's *own* visual language, NOT a VS/Rider copy; debugger icon = an **execution-tracing/
  flow** metaphor, not a bug; fault message → its **own bar** with copy + expand, never shifts the toolbar) ·
  **D15.3** Launch Experience (P + tiny persist — compact form, type subordinate to name, isolation
  practical-description-first in Advanced, launch shortcut + post-launch focus, **Quick Relaunch yes / favorites
  deferred**) · **D15.4** Expression UX + Friendly Errors (P+F — placeholders/examples + friendly errors via the
  existing `EditorLanguageService` local pre-validation + `DebugErrorMapper`) · **D15.5** Inline Values (F —
  only current-line-used OR changed-since-last-step, never all; AvaloniaEdit renderer, no text shift) · **D15.6**
  Debugger Performance (F — **direction reversed**: no debug-time timing (harness overhead misleads); integrate
  with the existing Performance Analysis; any future debug metric labelled "debugger runtime") · **D15.7** Global
  UI Audit (analysis-only catalogue for a future visual-refresh stage). **Ratified priority order:** Script
  Executor **Step 0 Probe** → D15.1 → D15.2 → D15.3 → **Script Executor Rewrite (Steps 1–6)** → D15.4 → D15.5 →
  D15.6 (D15.7 in the background). Full self-contained guide (architecture / seams / decisions / rationale /
  priorities / deps / risks — start a milestone from it without re-analysing):
  [docs/design/d15-debugger-experience-and-ide-polish.md](docs/design/d15-debugger-experience-and-ide-polish.md).
  **D15.1 (Editor Readability) — STARTED; Seam A (syntax palette, app-wide) DONE 2026-07-21 (impl, awaits user
  visual confirmation).** Presentation-only readability palette: SQL keywords (query + DML-action + DDL) share
  ONE restrained blue (bold); types + built-in functions demoted to neutral foreground; comments/literals
  legible-but-quiet; `EditorLocalBrush` neutralized in both dictionaries so ordinary variables are neutral
  (objects + trigger context keep a restrained accent). **Refinement after user QA (2026-07-21):** to keep the
  SQL-vs-PSQL language hierarchy without a Christmas tree, the Core `FirebirdSyntax` `Statement` keyword
  category was **split** into `Statement` (SQL — blue) + a new `Psql` category (BEGIN/END/IF/WHILE/FOR/DECLARE/
  SUSPEND/EXECUTE/… — a second restrained **violet** accent, dark `#A88FD4` / light `#6C4C9E`, bold). The
  catalog partition is safe: `SqlKeywordCategory`/`CategoryOf`/`KeywordsInCategory` feed only `FirebirdSyntax`
  + the xshd drift-guard test (lexer `IsKeyword` + completion are category-agnostic). Files: `FirebirdSql.xshd`
  + `.Light.xshd` + `Themes/Colors.axaml` + `FirebirdSyntax.cs` + `FirebirdSyntaxTests.cs`. **Light-theme
  tuning (2026-07-21, bolder per user):** data types → strong teal `#0F766E`, comments → cool gray-green
  `#6E847A` (no more "olive"), PSQL light violet deepened to `#5D30A6`.
  **Built-in functions coloured (2026-07-21, user-requested — reverses the "functions neutral"
  recalibration):** QA found built-in functions (ROUND/COALESCE/CAST/TRIM/UPPER/LOWER/SUBSTRING/…) reading as
  plain text. Root cause was NOT a classifier/list gap — all are catalogued in `FirebirdSyntax.FunctionWords`
  (category `Function`) + the xshd `Function` block; the only cause was the recalibration painting the
  `Function` colour neutral. Reversed: `Function` now gets a soft yellow accent (VS Code-style, dark `#DCDCAA`
  / light `#795E26`), a fourth hue distinct from SQL blue / PSQL violet / type teal; operators stay neutral.
  Pure Presentation (only the two xshd `<Color name="Function">` values + comments) — the drift-guard pins
  block *membership* not hex → no test change; build 0/0, 52 syntax/highlight tests green. A user-defined
  function stays neutral by design (needs semantic resolution — separate feature).
  **Seam A2 — domain-as-type resolution (Feature) DONE 2026-07-21 (awaits visual confirm):** a domain used as
  a data type (`DECLARE VARIABLE`/param/`RETURNS`) was neutral because the binder emitted no reference for a
  type-position name. `SemanticBinder.Psql.BindDomainTypeReference` now emits a `SchemaObject` reference for the
  type identifier **only** when it resolves to a `Domain` via metadata (builtins are keywords, not identifiers;
  unknown name → nothing, no false diagnostic; scan bounded to the segment so it never reaches the body). The
  App paints a resolved domain like a SQL type via a new `EditorDataTypeBrush` (both dicts, mirrors the xshd
  `DataType` hex) mapped in `SemanticHighlighter` for `Domain` objects; hover/Ctrl+Click/diagnostics work for
  free through the model. +4 `SemanticModelTests`; semantic/highlight/nav/hover/diagnostics suites green; build
  0/0. A **distinct** domain accent (vs plain types) stays deferred. **Palette recalibration (2026-07-21, user
  clarified the goal):** the point is FEWER coloured categories (most identifiers stay plain text), not lower
  intensity — colours should be vivid + elegant (VS Code-like). So dark types → vivid `#4EC9B0` (+ mirrored
  `EditorDataTypeBrush`); light comments → elegant green `#2E8B57` (back to green, not gray/olive). SQL blue /
  PSQL violet already read as vivid; functions/operators/locals/columns stay neutral.
  **Seam A3 — DDL preview highlighting parity (bug fix) DONE 2026-07-21 (awaits visual confirm):** the object
  editors' DDL tab (+ sidebar DDL preview) coloured differently from the Editor tab — app-wide highlighting has
  TWO layers and only the lexical (xshd) one reached the DDL previews; the **semantic** layer
  (`SemanticHighlighter`, objects+domains) is installed only by `SqlEditorBehavior.Attach`, which the read-only
  DDL editors never called. New highlight-only `SqlEditorBehavior.AttachReadOnlyHighlighting(editor)` adds the
  semantic layer WITHOUT completion/squiggles/ergonomics (rebuilds model from text + metadata on change/load,
  resolves the VM from the visual tree, leak-free); wired into all **11** DDL previews (MainWindow + Table/
  Procedure/Function/Trigger/View/Package/Domain/Generator/Exception/Index). Additive; build 0/0; semantic/
  highlight/syntax + headless editor-attach probe green. Minor known gap: trigger DDL preview doesn't resolve
  NEW/OLD context vars (no context provider on the read-only path).
  **Seam B (current-line rebuild) DONE 2026-07-21 (impl, awaits visual confirm) → D15.1 COMPLETE.** Treated
  as the definitive review of current-line rendering. **Scope correction:** the current-line marker is
  **debugger-only** (`CurrentLineRenderer`, attached only in `DebuggerTabView`, NOT the shared
  `SqlEditorBehavior.Attach`; no editor uses `HighlightCurrentLine`) — it marks the debugger's **paused
  statement**, so Seam B's visual change is confined to the debugger source editor (the app-wide part of
  D15.1 was the palette). As-built: the old amber statement-span band → a calm **full-line-width blue wash**
  (`DebugCurrentLineColor` dark `#285A8AC8` ~16% / light `#1C0033B3` ~11%) **+ a ~2.5px accent bar** at the
  line's left edge (new `DebugCurrentLineBarBrush`, both dicts). Draws as the **backdrop**
  (`BackgroundRenderers.Insert(0,…)`) so squiggle/related renderers + selection read on top; low alpha never
  masks glyphs/syntax. Per-visual-line geometry via `GetRectsForSegment` (Y/Height reused, X → full viewport
  width) → correct under word-wrap / folding / variable heights. Repaint unchanged (`TextView.Redraw()`,
  gotcha #223). Pure Presentation; hex tunable. Build 0/0; +1 headless pin (backdrop ordering + non-throwing
  Draw); 5098 tests green; smoke clean. Next: **D15.2** (Toolbar + own SVG icon system + Error Bar). Guide:
  d15 doc §3.4/§4.
- **CTE column diagnostics — false ET0002 on CTE-projected columns FIXED (2026-07-21; Stage-7/binder Feature,
  pure Core).** QA found `cte_alias.col` flagged **Unknown column** even for a column the CTE projects (e.g.
  `po.rodzajsprznagl` off a `WITH RECURSIVE`). **Root cause (deterministic, not the init-ordering staleness —
  that stays unfixed, not reproducible):** `SemanticBinder.Query.BindDottedReference` resolved a CTE-alias's
  columns via `ResolveColumn(TargetName, …)` → `_metadata.GetColumns(cteName)` — the **catalog**, which never
  holds CTE columns — and `DiagnosticsEngine.QualifierResolvesTable` treated a CTE-backed
  `TableReferenceSymbol{Target:CteSymbol}` as a verifiable table. **Fix — the binder/diagnostics now understand
  the CTE's OWN projection** (user chose this over silencing ET0002): `CteSymbol` gained `OutputColumns` +
  `ColumnsComplete`; `BindCte` fills them from the explicit column list (authoritative) else a new
  `ExtractCteProjection` reading the body's **anchor SELECT** (leftmost of a set-op; Firebird's rule); it
  accepts **only** unambiguous item shapes (`col` / `t.col` / `<expr> AS name`) and marks `*`/`t.*`/unaliased-
  expression/empty as **incomplete — never a synthetic name (§0 / Paramount Law)**. `BindDottedReference` routes
  a CTE qualifier through new `ResolveCteColumn` (resolves against `OutputColumns`, gives the member a real
  `ColumnSymbol` → hover/Ctrl+Click work); `QualifierResolvesTable` flags a genuine typo only when
  `ColumnsComplete`, else stays silent (safe degradation). No probe, no Firebird. Build 0/0; **5097 tests green**
  (+9 `DiagnosticsEngineTests`: projected column silent, resolves-to-symbol, genuine-unknown-on-complete flagged,
  star silent, unaliased-expr silent, explicit-list resolves+flags, AS-alias resolves, recursive `UNION ALL`
  anchor). Reusable `ExtractCteProjection` could later serve derived-table columns (currently silent).
  **Script Executor Rewrite — Step 0 (Probe) DONE 2026-07-20; architecture stands, measurement-gated.** The
  `Sequenced`-mode plan is ratified ([docs/design/script-executor-transaction-review.md](docs/design/script-executor-transaction-review.md)
  §5/§6): fixes the KNOWN-BROKEN mixed DDL+DML defect (gotcha #213). **Step 0 (the Probe) — the blocking
  measurement gate — RAN against the live FB5 lab (twice, deterministic; no production code touched).** The one
  unmeasured load-bearing claim, the §2.2(b) self-block, is **real but SELECTIVE**: table-scanning DDL
  (`CREATE INDEX`) self-blocks on the script's own uncommitted DML (WAIT-exhausted lock timeout, SQLSTATE 40001,
  PROBE 1c), but the review's stated example (`ALTER TABLE … ADD COLUMN`/`DROP COLUMN`) does **not** —
  metadata-only on FB5 (~7 ms, PROBEs 1/1a/1d). So §2.2(b) was **restated, not withdrawn** (example corrected,
  scope narrowed, "decisive objection" framing dropped — the decisive objection is §2.2(a) Rollback-lies, which
  never rested on inference). #213 **re-confirmed** (PROBE 2); the commit-boundary fix **works** (PROBE 3);
  independent DDL **can share a segment** (PROBE 2a). **The architecture did NOT change** — the Sequenced design
  cannot self-block by construction (never two txns open at once); the review's §2.2(b)/§6/§7 were corrected in
  place. **Step 1 (Documentation Truth Pass) DONE 2026-07-21** — comment-only, no behaviour/UI/App change: the
  two remaining stale comments were corrected (`ConnectionRole` docstring falsely said Metadata *"carries … the
  metadata working transaction"* — it owns none; `MainWindowViewModel.cs:200` falsely cited *"co-location, gotcha
  #122"* for why the Script Executor is on the Data lane — the real reason is that it IS the user working
  transaction, #89); the other two review-named targets (`ConnectionProfile.cs`, the `FirebirdScriptExecutor`
  header) were already clean. Build 0/0; Script Executor + Dev Mode tests 101/101 green. **Step 3 (Sequenced
  core) DONE 2026-07-21** — Core-only, no Firebird/App/UI: `ScriptTransactionMode.Sequenced` (the "Deployment"
  mode — commit after each schema statement so #213 is fixed by design; NOT atomic, trade-off surfaced) + a pure
  `ScriptSegmentPlanner` (`EmberTern.Core.Scripting`) that splits a script into ordered `ScriptSegment`s over the
  **AST-based `SqlStatementClassifier`** (not the driver enum — the single-classifier convergence), each carrying
  a `SegmentTransactionPolicy` intent (`DataNoWait`/`SchemaWait`; the real Firebird TPB mapping is Step 4). v1 is
  conservative — each schema statement is its own committed segment (isql `SET AUTODDL ON`); grouping independent
  consecutive DDL (§5.1, PROBE 2a) is a documented deferred optimization. **Step 2 (Dev Mode text) folded in and
  found already truthful** (`UiStrings` already states the scope + SQL-Editor boundary). Build 0/0; +10
  `ScriptSegmentPlannerTests`; Script + Dev Mode suite 110/110 green. **Step 4 (Firebird layer) DONE 2026-07-21
  (seams A+B).** Seam A: pure `FirebirdScriptExecutor.ResolveSegmentTransactionOptions` (`SchemaWait` → the same
  Dev-Mode-aware WAIT policy Compile uses / `DataNoWait` → NOWAIT default; +5 unit tests). Seam B: `RunAsync`
  dispatches `Sequenced` to `RunSequencedAsync`, which runs the plan one committed segment at a time (per-segment
  begin with seam-A TPB → `RunOneAsync` → commit on success / roll back the OPEN segment on failure; one tx ever
  open, `TransactionLeftOpen` always false). **Core untouched; Manual/AutoCommit byte-unchanged.** The now-false
  "KNOWN BROKEN — a mixed migration cannot run" class docstring was corrected (single-tx modes still can't; use
  `Sequenced`). **Live-verified on FB5** — new throwaway `tools/probes/ScriptExecutorSequencedProbe` (scratch DB,
  lab untouched), **ALL PASS**: (A) mixed CREATE+INSERT+INDEX migration runs end-to-end + persists (#213 fixed by
  design), (B) same migration under AutoCommit still fails at the INSERT + rolls back (the #213 contrast), (C)
  mid-script failure keeps earlier segments committed + rolls back only the failing one. Build 0/0; Script + Dev
  Mode + Transaction suite 165 green (regression). **Step 5 (App layer — App/UX only) STARTED 2026-07-21, split
  into seams A/B/C; Seam A (third mode in the picker) DONE** — `Sequenced` ("deployment, commits in steps")
  added to the mode ComboBox with a per-mode description tooltip (the non-atomic trade-off stated where the mode
  is picked, §5.3); pure `ScriptExecutorTabViewModel.ResolveMode`/`ResolveModeDescription` + a mode-aware
  `BuildOutcomeStatus` (honest Sequenced summary + cancelled message); +11 `ScriptExecutorModeTests`;
  Manual/AutoCommit wording unchanged. **Seam B (up-front rejection of a mixed DDL+DML script in
  Manual/AutoCommit) DONE 2026-07-21** — pure `ScriptExecutorTabViewModel.ResolveMixedScriptBlock` stops the run
  BEFORE the first statement with a message that explains the single-transaction limitation and names
  `Sequenced` (`IsMixedMigration` classifies via the same AST `SqlStatementClassifier` the planner uses; engine
  untouched); +11 `ScriptExecutorMixedScriptTests`. **Seam C (results-grid segment presentation) assessed larger
  than one seam, split C1/C2/C3; C1 (a "Step" column showing each statement's committed Sequenced step) DONE
  2026-07-21** — pure `ScriptExecutorTabViewModel.BuildSegmentMap(statements, mode)` (statement index → 1-based
  step from the same planner the engine ran; empty/blank for single-transaction modes) + `ScriptResultRowViewModel.StepText`;
  +7 `ScriptExecutorSegmentPresentationTests`. **Seam C2 (per-step commit/rollback status) assessed larger
  than one seam, split C2a/C2b; C2a (pure reconstruction) DONE 2026-07-21** — `ScriptStepStatus`
  (Committed/RolledBack/NotRun) + pure `ScriptExecutorTabViewModel.BuildStepStatuses(segmentMap, results)`
  mirrors `RunSequencedAsync` exactly (captures a `Success` statement whose step still rolled back because a
  later statement in the same transaction failed); engine untouched; +7 `ScriptExecutorStepStatusTests`. **Seam
  C2b (presentation) split C2b-1/C2b-2; C2b-1 (colour the Step cell committed/rolled-back on executed rows) DONE
  2026-07-21** — post-run `ScriptExecutorTabViewModel.ApplyStepStatuses` stamps each row from `BuildStepStatuses`
  (unchanged); `ScriptResultRowViewModel` became observable (`StepStatus` + derived flags/tooltip); the grid's
  Step cell is coloured (committed = green / rolled back = amber, existing tokens) with a tooltip, so a `Success`
  statement whose step rolled back is visibly marked; +4 `ScriptExecutorStepStatusPresentationTests`. **Seam C2b-2
  (surface "not run" statements) DONE 2026-07-21** — a Sequenced stop-on-error / cancellation leaves later
  statements unexecuted (they get NO result row — rows arrive only via the progress callback); pure
  `ScriptExecutorTabViewModel.FindNotRunStatements(segmentMap, results)` reconstructs their indices (plan minus the
  covered indices; empty for single-transaction modes so nothing is synthesized there) and `AppendNotRunRows`
  appends a synthesized `ScriptResultRowViewModel` per index (new statement-based ctor: `IsNotRun`, Result = "Not
  run", `StepStatus = NotRun`, its would-be step number, source range preserved so double-click still navigates). A
  not-run row is neither success nor failure — shown muted/italic via a new `IsSucceeded` (`= !IsFailed &&
  !IsNotRun`, so "OK" stays green only for a real success) + a `result-notrun` style; the "Success" filter excludes
  it; `SuccessCount`/`FailedCount` untouched. App presentation only; Core + Firebird untouched; +7
  `ScriptExecutorNotRunTests`. **Seam C3 (a "N of M steps committed" status-line headline) DONE 2026-07-21** — the
  Sequenced status line now leads with committed steps of all planned steps (committed + rolled-back + not-run);
  pure `ScriptExecutorTabViewModel.BuildStepSummary(segmentMap, results)` counts committed steps by REUSING the
  unchanged `BuildStepStatuses` reconstruction (the count matches the grid — it only counts + formats), and
  `BuildOutcomeStatus` gained an optional `segmentMap` arg prepending the headline to both the deployment summary
  and the cancelled message (empty/absent for single-transaction modes + the existing 2-arg callers → no headline,
  byte-identical); App presentation only, Core + Firebird + the reconstruction untouched; +7
  `ScriptExecutorStepSummaryTests`. Build 0/0. **Step 5 seam C is COMPLETE.**
  **Step 6 (live verification against the lab) — DONE 2026-07-21 → SCRIPT EXECUTOR REWRITE (Steps 0–6) IS
  COMPLETE.** Full end-to-end verification passed: Technical Review + Live Verification (**12 scenarios, ALL
  PASS**) + UX Review + Code Review + Performance Review + Final Verdict. The one issue found was documentation
  only (FINDING 1 — a stale `ScriptSegmentPlanner` docstring about dependent DDL), corrected in commit
  `8faf200` (`docs(script-executor): Step 6 — correct ScriptSegmentPlanner docstring (dependent DDL)`). The
  `Sequenced` mode fixes the mixed-DDL+DML defect (#213) by design and is live-proven on the lab; nothing about
  the rewrite is open. Full record: review §6 "Results" + §7, and
  [docs/history/15-...](docs/history/15-ux-stabilization-sprint-and-console-refactor.md) (Step 0/1/3/4/6).
  D11 narrative + full D12 narrative + D13 (Seam 0/A/B/C + close) narrative:
  [docs/history/19-firebird-debugger.md](docs/history/19-firebird-debugger.md). Spec:
  [firebird-debugger.md](docs/design/firebird-debugger.md) (**v2, decisions ratified** — the target
  implementation spec). Execution plan: [firebird-debugger-implementation-plan.md](docs/design/firebird-debugger-implementation-plan.md)
  (milestone briefs, session split, danger zones, **Developer Contract**).
  **P1 (AST: exception handlers) — DONE.** `WHEN … DO` is now readable from the tree: a `WhenHandler`
  node per `WHEN` clause holding an **ordered `WhenCondition` list** (kind + optional operand) + a `Body`,
  hung off `BlockStatement.Handlers`. Parser producer (`SqlParser.Psql.cs`) peels the handler section
  (comma-split condition list, each recognised strictly by leading keyword — `ANY`/`EXCEPTION`/`GDSCODE`/
  `SQLCODE`/`SQLSTATE`); binder consumer (`SemanticBinder.Psql.cs`) binds each handler body against the
  enclosing scope and references every `EXCEPTION <name>` condition as a schema object. **Additive only** —
  `SqlFormatter` untouched (its PSQL layout is token-based), §0 round-trip byte-identical, an unrecognised /
  malformed `WHEN` still falls back to the lossless `PsqlLeafKind.Other` valve (never a handler, never
  swallowed). **Refined during P1 (decision 3, ratified by the user):** Firebird allows a comma-separated
  condition list per `WHEN`, so a single kind per node was insufficient — the model carries the whole list,
  and D1's router matches them in declaration order (spec + plan updated). Commit `590b220`.
  **P2 (server version gate, FB3+) — DONE.** `FirebirdConnectionService` refuses a pre-FB3 server on
  connect with a legible message (`FirebirdSql.Data.FirebirdClient` is Srp-only ⇒ FB2.5 is already
  unreachable; the gate ratifies that, decision 8 / spec §1.3). A `post-open precondition check` on
  `ConnectAsync` (right after the first attachment opens, before Metadata/Ddl — same server ⇒ gating the
  first covers all) and on `TestConnectionAsync`, closing cleanly (no half-open attachment). Pure predicate
  `IsSupportedServerVersion` reuses the app's one version parser (`FirebirdDdlReader.ParseServerMajor`) and
  **fails open on an unparseable version** (a live Srp connection is FB3+ by construction, so 0 ⇒ allow;
  reject only a positively-identified major 1–2). `MapErrorMessage` untouched (this is a precondition, not
  error interpretation). The message lives beside `MapErrorMessage` in the Firebird layer, not `UiStrings`
  — `EmberTern.Firebird` cannot reference `EmberTern.App` (layering); connection-failure messages already
  live there. **Live rejection is unverified** (no FB2.5 instance; the predicate is table-pinned and the
  FB5 lab connect path is behaviourally unchanged — FB5 ⇒ allowed). Build 0/0; **4652 tests green** (two
  partitions — 4625 + the 27-test `ConnectionExpandBindingProbe` alone — sidestepping the full-suite hang
  #94/#226); smoke clean. Follow-up (not urgent): the existing `serverMajor >= 3` catalog gates are now
  statically true.
  **D1 (debug engine core) — DONE (pure Core, no server; seams a + b both landed).** New namespace
  `EmberTern.Core.Sql.Debugging` (zero Avalonia, zero `FirebirdSql`): `DebugSession` (the interpreter/state
  machine), `Frame` (+ internal control-stack activations + the lexical scope chain), `FrameValues`,
  `StepPlanner` (pure stop-decision), `ExceptionRouter`, `BreakpointSet`, the enums
  (`DebugState`/`StepKind`/`StopReason`/`ExecutionStatus`), `DebugError`/`StatementOutcome`/
  `ConditionOutcome`/`IDebugCursor`/`DebugRoutine`, and `IDebugExecutor` — the **single server seam,
  contract only** (the precedented rule-#2 exception, like `ISqlMetadataProvider`).
  **Seam (a):** the interpreter walks block/`IF`/`WHILE`/`FOR`/leaf control flow and pushes/pops **nested
  frames** (step into `EXECUTE PROCEDURE` resolves a callee body via the executor; step over runs it on the
  server), with **Into/Over/Out/Continue/RunToCursor/SetNextStatement**. **Savepoint model from day one**
  (spec §4.5): `EnterFrameSavepoint` on every frame push (incl. root), `LeaveFrameSavepoint` on normal exit.
  **Seam (b) — exception routing + breakpoints (this session):** `ExceptionRouter.TryRoute` is the whole of
  exception control flow (spec §3.6) — on a raise it walks the innermost frame's active `BEGIN…END` blocks
  outward, matching `WHEN … DO` handlers **read from the AST** (`WhenHandler`/`WhenCondition` from P1, never
  re-parsed) in declaration order (handlers top-to-bottom, conditions left-to-right); **all five forms**
  (`ANY`/`EXCEPTION <name>`/`GDSCODE` numeric-or-symbolic/`SQLCODE` signed/`SQLSTATE` literal — the last
  three's operands read from `WhenCondition.Tokens`, where P1 left them). A **caught** exception repositions
  control to the handler body (abandoning inner activations, skipping the block's remaining statements,
  marking the block `HandlerActive` so it can't re-catch its own body → **re-raise** propagates out) and the
  catching frame is **NOT** rolled back (a `WHEN`-handling block's prior statements survive, §4.5). An
  **unhandled** frame closes its cursors, `RollbackFrameSavepoint`s (new `IDebugExecutor` method — the
  unhandled-exit counterpart), and pops; when **no frame** catches, every frame incl. the root is rolled
  back and the session `Faulted`s. `BreakpointSet` (offsets; `Add`/`Remove`/`Toggle`) hangs off
  `DebugSession.Breakpoints`; a run command stops at the next step point whose offset is set
  (`StopReason.Breakpoint`, always winning over `Step`). Re-raise needs no special interpreter state (the
  executor re-raises; the router routes it) — the router stays **pure control flow**, never interpreting
  Firebird semantics. Every step/route decision is a pure function of (AST, frames, breakpoints, command).
  Proven with a **scripted fake executor** — **39** `DebugEngineTests` (24 seam-a: step ordering, IF/WHILE/
  FOR, nested frames, savepoint order, scope chain, SUSPEND, RunToCursor, SetNext; +15 seam-b: matching per
  form, multi-condition `WHEN`, cross-frame propagation + rollback, re-raise + `HandlerActive` guard, cursor
  cleanup on both unhandled + handled unwind, four breakpoint cases).
  **D2 (harness + session connection + executor) — seam (a) DONE (Firebird; no harness yet).** New
  `DebugSessionConnection` (`EmberTern.Firebird`): a debug session's **own attachment + one transaction +
  frame savepoints** (spec §4.1/§4.2/§4.5) — **decision 5: a session is NOT a lane** (no
  `ConnectionRole.Debug`; two tabs = two sessions = two transactions, impossible on a per-profile lane
  singleton), and it **never** touches the Data lane (a debug rollback there would destroy the user's
  uncommitted work, rule #11). TPB **explicit** (#85) via pure `BuildDebugTransactionOptions(DebugIsolation)`
  — write + (read_committed rec_version | concurrency) + **NOWAIT** (a lock met on the user's Data tx ⇒
  step-level error at a known line, not a hang); isolation `ReadCommitted`/`Snapshot` user-selectable at
  launch (§12.4). Frame savepoints: `Set`/`Release`/`RollbackToSavepointAsync` (async counterparts of D1's
  `IDebugExecutor.Enter/Leave/RollbackFrameSavepoint`, bridged by seam c) — names (`ET_DBG_FRAME_{id}`)
  validated as bare identifiers; SQL verified through the driver (§15.3 [5]). Per-wire-op locking on the
  session's own single lock (#31/#98/#120/#236). `FirebirdConnectionService.CreateDebugSessionAsync` opens
  the attachment + registers the session; `DisconnectAsync`/`Dispose` tear all sessions down deterministically
  (attachments must not outlive the profile connection); each deregisters itself on dispose. Pinned by 13
  pure `DebugSessionConnectionTests` (TPB both isolations, the 3 savepoint statement forms, name validation);
  the **live** round-trip is **awaits user confirmation** (needs a server; driver capability already
  confirmed §15.3 [5]).
  **D2 seam (b) — harness builder + read/write-set analyzer + §3.4 R1–R5 DONE (pure Core, no server).**
  New `HarnessBuilder` (`EmberTern.Core.Sql.Debugging`): `Build(HarnessRequest) → HarnessResult` generates
  the anonymous `EXECUTE BLOCK` that is the **one** server mechanism (§3.2/§3.3) as a **pure function** — the
  fragment text, each variable's verbatim declaration + base type + value, sub-routine declarations and the
  read/write set are all **inputs** (seam c derives them from metadata + frame; tests supply them), which is
  what makes the non-negotiable §3.4 rules unit-testable without a server. Rules enforced: **R1** only reads
  with a non-null value are injected (a declared var is already `NULL`; `V=NULL` crashes a `NOT NULL`
  domain); **R2** params + `RETURNS` use the variable's **base type** (input; metadata-derived in seam c),
  never the domain; **R3** frame vars declared **verbatim**; **R4** inject only reads / return only writes;
  **R5** every in-scope sub-routine declaration carried verbatim, always (after the var declares). Statement
  vs Expression mode (conditions/watches → `ET_DBG_RESULT`); `ET_P_`/`ET_O_`/`ET_DBG_` prefixes avoid
  colliding with real names. New `ReadWriteSetAnalyzer.Analyze(statement, model)`: **consumes** the binder's
  resolved references (rule #1/#2 — never re-parse/re-resolve) — reads = referenced vars/params (safe
  over-inclusion), writes = leftmost l-value for an assignment / ∅ for an `IF`/`WHILE` condition / reads
  (superset) otherwise. **Two deliberate boundaries:** the transitive sub-routine call-graph fixpoint is
  **D9** (meanwhile R5 carries all sub-routine *declarations*, so nothing is lost); the §3.5 inject-all-in-
  scope fallback is the named primitive `InScopeLocals` (for a Watch on an arbitrary expr, D5), **not** an
  auto-branch (the binder never signals an unresolved *local*, so it'd be untestable dead code — #233).
  Pinned by 16 pure tests (11 `HarnessBuilderTests` covering R1–R5 + modes; 5 `ReadWriteSetAnalyzerTests`
  against the real `SemanticModel`). **Test lesson recorded:** the debugger builds the model from the
  **strict** `SqlParser.Parse(sql).Root` of a whole routine (`CREATE PROCEDURE` stays one `DdlStatement`
  with a bound body) — the editor's lenient `SemanticModel.Build(string)` splits a routine apart and binds
  the body without its declared vars. Build 0/0; tests green (user-verified — full-suite run was slow, so
  confirmed manually); smoke clean.
  **D2 seam (c) — executor + live fidelity — DONE + verified (2026-07-17). D2 IS COMPLETE.**
  `FirebirdDebugExecutor : IDebugExecutor` (`EmberTern.Firebird`) wires D1's interpreter to seam (a)'s
  `DebugSessionConnection` through seam (b)'s `HarnessBuilder`: each step/DML leaf → a Statement-mode harness,
  each `IF`/`WHILE` condition → an Expression-mode `BOOLEAN` harness, run in the debug tx; the server computes
  **all** semantics. `SUSPEND` is control flow — the output row is emitted **client-side** from the output
  params (no round-trip). Savepoints delegate to the session. **Sync-over-async** bridge is deadlock-safe
  (ConfigureAwait(false) throughout; per-wire-op command lock #98/#120/#236). **D2 boundaries (§F, explained
  stops):** `ResolveRoutine` → null (a call runs in place = step-over, 100% faithful §5.3; step-into is
  D8/D9); `OpenCursor` → Cursor Bridge (D6). New pure Core `PsqlDeclarationExtractor` (verbatim locals R3 +
  type spec, sub-routines R5 empty in D2 by construction). New `FirebirdDebugMetadata`: **R2 base-type
  derivation** from `RDB$FIELDS` via the existing `FirebirdDdlReader.FormatType` (derivation, not guessing) +
  frame variable templates (params from `RDB$PROCEDURE_PARAMETERS`, declared with their user domain R3 /
  base-typed injection R2; locals verbatim). New `DebugErrorMapper`: `FbException` → `DebugError` from
  SQLSTATE/GDS, never message-parsed (**grounded live** — user `EXCEPTION` carries `isc_except` 335544517 with
  its name on the message's first line; `NOT NULL` validation is SQLSTATE 42000 / GDS 335544879); pure
  `Build()` unit-tested; `SqlCode` + symbolic GDS name are documented D2 boundaries. Small **D1 extension**:
  `DebugSession` gained an optional `rootValues` ctor arg (a standalone routine's launch **input-parameter
  arguments** seed the root frame — the root has no caller to provide them; additive, existing tests pass
  null). **⚠ §3.5 fallback (gotcha #238):** a reused `SELECT … INTO` surfaces **no** local refs from the
  binder (the query binder records FROM/columns, not the `:`-refs in WHERE / the INTO targets), so its precise
  read/write set is empty and would drop the write-back — the executor falls back to `InScopeLocals` (§3.5
  "inject all in-scope", correct+chattier) when the model surfaces nothing; precise narrowing stays for every
  statement whose refs the binder does surface. **Lab zoo extended** (`Lab/setup.sql` + rebuilt `.fdb`): two
  D2 procs — `SP_DBG_SUMMARY` (assignment, **domain `NOT NULL` local**, IF/ELSE, SUSPEND) and `SP_DBG_GUARD`
  (`EXCEPTION` + `WHEN … DO`). **Live fidelity PROVEN (§15.4):** the real executor drove `DebugSession`
  step-by-step through `SP_DBG_SUMMARY`/`SP_DBG_GUARD`/`SP_ADD_ORDER` and the DB state + outputs **matched real
  execution** in all 7 cases (incl. the domain-`NOT NULL` local not crashing, exception routing via real
  `FbException`, DML + savepoint rollback, unhandled-exception root rollback). Nested calls / cursors / local
  routines / autonomous-tx grow the zoo per their own milestones (D6/D8/D9). +12 tests (6
  `PsqlDeclarationExtractor` + 5 `DebugErrorMapper` + 1 `ReadWriteSetAnalyzer` fallback pin). **Build 0/0;
  4732 tests green in one run; smoke clean.** History:
  [docs/history/19-...](docs/history/19-firebird-debugger.md).
  **D3 (editor-wiring consolidation) — DONE + user-confirmed (2026-07-17; behavior-preserving, manual QA
  passed on every surface in both themes).** The **two** hand-maintained copies of the SQL editor's intrinsic language block
  (completion / highlighting / navigation / squiggles / related-elements / language-completion /
  typing-ergonomics / search) are collapsed into **one attach path** — dissolving gotcha #219 *before* the
  debug tab (D4) becomes a third host. `MainWindow` no longer hand-wires that block in its ctor; it calls the
  **same** `SqlEditorBehavior.Attach(_editor, _currentVm)` the object editors use, once its VM arrives (first
  non-null `OnDataContextChanged` — the window's `DataContext` is set after construction, and the shared path
  needs a stable non-null VM: **"subscribe once the VM arrives"**, the spec §11.1 intent). Approach chosen
  over a null-safe shared-helper alternative because it **solves** the lifecycle rather than encapsulating it
  (user-ratified). **Deleted as now-dead** (Contract #20, only after the new path built + tested green — the
  user's "prove before delete" directive): `OnMainEditorMetadataChanged` / `OnMainEditorMetadataReady` /
  `WarmReferencedMetadataAsync` + the private `CreateMetadataSnapshot` / `EnsureColumnsAsync` /
  `EnsureRoutineParametersAsync` forwarders — every responsibility now owned by the shared `Attach` (metadata
  hooks bound to `vm.Metadata`; warm/snapshot/ensure read the VM's own methods). **Boundary: intrinsic block
  ONLY** (user-confirmed) — the per-host wiring (`DiagnosticsPanelHost.Track` = F8 + diagnostics panel,
  `AmbientModelRefresh`, `SqlSnippetDropTarget`) stays a caller responsibility, as it genuinely differs per
  host and was never the #219 risk. `SqlEditorBehavior` gained **no** new parameters — consolidation by
  *deleting* the second copy, not growing the shared one. Build 0/0; **4732 tests green in one run** (identical
  to the D2 baseline — behavior-preserving); smoke clean; the headless `ConnectionExpandBindingProbe` (drives
  `SqlEditorBehavior.Attach` + real key events) green. Gotcha #219 → **resolved by D3**; plan's "Dual wiring
  (until D3)" danger row retired. History: [docs/history/19-...](docs/history/19-firebird-debugger.md) (D3).
  **D4 (debugger tab MVP) — DONE + user-confirmed (2026-07-17; manual QA on the live lab passed — launch,
  stepping, breakpoints, variables all work; debugger felt stable). First real user value: launch a standalone
  procedure, set breakpoints, step, watch variables.**
  New `WorkspaceTabKind.Debugger` (+ `ActiveDebugger`/`IsDebuggerTabActive` on the notify chain, gotcha #25),
  opened from the sidebar procedure-leaf **"Debug procedure…"** (mirrors Execute; `Metadata.DebugProcedureRequested`
  → `MainWindowViewModel.OnDebugProcedureRequested`), hosted like `ScriptExecutorTabView` and torn down on tab
  close (rollback + close attachment, §4.4). The tab is a **thin presentation layer** over the proven engine:
  `DebuggerTabViewModel` parses the routine ONCE (strict whole-routine `SqlParser.Parse` → `SemanticModel`,
  gotcha #238) to derive the launch panel + step points, then drives D1's `DebugSession` through
  `IDebugSessionLauncher` (App seam — production `FirebirdDebugSessionLauncher` opens a `DebugSessionConnection`
  + wires D2's `FirebirdDebugExecutor`; a fake launcher over a scripted `IDebugExecutor` makes the VM
  server-lessly testable). **Launch panel (§9.2, inline not modal):** typed parameters reuse the Smart-Parameters
  infrastructure (`ExecuteProcedureDialogViewModel` — typed rows + history + validation + resolve, **no second
  editor**), an isolation selector (§4.2), and a **pre-flight** (`DebugPreflight`: `DiagnosticsEngine` unresolved
  names + the §4.6 data-safety boundaries — a lexical scan flags `IN AUTONOMOUS TRANSACTION` / generator use that
  survive the rollback — + the §F "no step points" refusal). **Stepping:** Into/Over/Out/Continue/Stop(rollback)/
  Restart + Run-To-Cursor, each engine call on a background thread (sync-over-async executor). **Renderers**
  (attached alongside D3's one `SqlEditorBehavior.Attach` seam on the source editor — **read-only as of D4;
  editable since Seam 5, and since the Draft model its text is what a session runs**, spec §11.1):
  `CurrentLineRenderer` (translucent-amber current-statement band) + `BreakpointMargin` (clickable red-dot gutter,
  breakpoints snap to an `IExecutableStatement` — §9.6); repaint via `TextView.Redraw()` (#223). **Keyboard is
  VS-standard + tab-scoped** — `F5`=Continue here (Execute in the SQL editor; the one deliberate contradiction,
  §9.7). Basic variables list from the current frame (the rich window is D7). **New theme tokens** `DebugCurrentLineBrush`
  / `DebugBreakpointBrush` (both dictionaries). **D4 boundaries (§F):** step-into resolves to nothing yet (a call
  runs on the server = step-over, 100% faithful §5.3); triggers/packages/local routines/cursors + Watches/Immediate
  are later milestones. Build 0/0; **4744 tests green in one run** (+12 `DebuggerTabVmTests`: prepare/params/
  preflight, launch-paused-at-entry, step/continue/complete, write-back, fault, stop-teardown, breakpoint snap +
  stop); smoke clean. (No headless view-attach probe yet — a follow-up; the live behaviour is user-confirmed.)
  History: [docs/history/19-...](docs/history/19-firebird-debugger.md) (D4). **D4 UX-review backlog (user, after
  first real use — deferred, NOT part of D4):** 8 items to fold into later milestones — first-class Debug entry
  points (toolbar/procedure-view button + shortcut, PPM as alternative); move transaction-isolation config to
  global Settings (show only params at launch); **current-line marker too aggressive in dark theme → a subtle
  blue wash (~10–15%) + a thin left bar, not the amber fill** (a `DebugCurrentLineBrush` re-tune); Variables must
  distinguish IN/OUT/local by icon/colour/grouping (⇒ D7); more distinct Into/Over/Out icons (VS/Rider-like);
  an "Edit Parameters…" affordance on a running session (not only at launch); grow the parameter-history feature
  (pin/recent/group/delete); a richer AST-derived paused status (e.g. "Paused — WHILE loop (line 14)"). Full list:
  [docs/history/19-...](docs/history/19-firebird-debugger.md) (§"D4 UX review"). **Directive: fix these as UX/theme
  in the view + tokens; do NOT push logic into the VMs/UI to paper over UX — keep the D1–D4 responsibility split.**
  See [[feedback-debugger-ux-polish-backlog]].
  **D5 (expression evaluation — Evaluate / Watches / Immediate) — seam (a) DONE (2026-07-18; impl, live
  evaluation awaits user confirmation). Seam (b) NOT started.** §9.5 decision 6: **one engine, three
  surfaces** — every surface is *literally the harness with a user-supplied fragment* (D5 risk #1: no second
  evaluator). **The one engine is Core:** new `EvaluationModels.cs` (`EvaluationKind` Expression|Statement,
  `EvaluationRequest`, `EvaluationResult` — carries the generated `Sql`, the §10.3/§F audit anchor) +
  **`IDebugExecutor.Evaluate(request, frame)`** (a new method on the one server seam; an arbitrary fragment
  has **no AST node**, so its read/write set is the §3.5 **`ReadWriteSetAnalyzer.InScopeLocals`** primitive —
  exactly what D2 carved out named for "a Watch on an arbitrary expression") + **`DebugSession.Evaluate(
  fragment, kind)`** (pure orchestration: requires Paused, delegates to the executor against `CurrentFrame`,
  applies a Statement's write-back to the live frame — the Immediate window operates *on the live frame*). The
  Firebird executor builds the harness with the machinery it already had; an arbitrary expression's result
  column is a wide `VARCHAR(8191) CHARACTER SET UTF8` (unknown type → server casts to text; a value that
  can't cast raises + is surfaced, never guessed, §F). **Deviation from the plan (documented, Contract):** no
  App `EvaluateController` — the real "one engine" is `DebugSession.Evaluate`; the App orchestration
  (`Task.Run` + audit append) is thin enough to live on the VM, exactly as stepping is orchestrated (a
  controller would be pure indirection; precedent: D3 chose "solve the lifecycle" over the plan's letter).
  **Two inline surfaces shipped:** the **Immediate window** (input + Enter = evaluate; an "as statement"
  checkbox → runs a PSQL statement against the live frame with write-back) and **Evaluate (Shift+F9** — the
  source selection / identifier-at-caret as an expression), both routing through the same engine, both landing
  in the **Executed SQL audit log** (§10.3 — newest-first, capped 200, the generated harness SQL kept on the
  row tooltip; a statement is always flagged `±` side-effect-capable). Evaluation runs on `Task.Run` with
  **Phase→Busy** for the duration → mutual exclusion with stepping via the existing state machine (the
  non-thread-safe `DebugSession` is never touched concurrently). New `DebugExecutedSqlRowViewModel`;
  `DebuggerTabViewModel` gained `ExecutedSql`/`ImmediateInput`/`ImmediateAsStatement`/`EvaluateImmediateCommand`/
  `EvaluateSelectionAsync`; `DebuggerTabView` gained the bottom Immediate/Executed-SQL panel + Shift+F9. All
  theme tokens; **no new colours; no UX polish** (the D4 UX backlog stays deferred). **Post-QA follow-ups
  (2026-07-18, view/discoverability only — no engine/architecture change):** the Immediate input is **no
  longer auto-cleared** after evaluation (kept for tweak-and-re-run) with an inline **Clear (✕)** button; and
  a **Debug button on the Procedure editor toolbar** (right of Run Procedure, new `Icon.Bug`) **reusing the
  one launch path** — `OnDebugProcedureRequested` extracted `OpenDebuggerForProcedure(routineName)`, the
  procedure VM raises a `DebugRequested` intent (mirrors `RunExecuteRequested`), `DebugProcedureCommand` gated
  on `!IsNew`. **Trigger/Package Debug buttons were requested but NOT added** — the debugger supports
  standalone procedures only (triggers = D10, packages later); a button there would be a dead entry point, so
  it ships *with* its enabling milestone. History: [docs/history/19-...](docs/history/19-firebird-debugger.md)
  (D5 seam a + follow-ups).
  **D5 seam (b) — Watches — DONE (2026-07-18; impl, live watch evaluation awaits user confirmation). D5 IS
  COMPLETE.** The third §9.5 surface: expressions **re-evaluated after every step** through the **same**
  `DebugSession.Evaluate` (Watches add **no** evaluation mechanism — risk #1). The tab VM calls
  `EvaluateWatchesAsync()` after each pause (step / launch / an Immediate that may have mutated the frame)
  **while `Phase==Busy`** (mutual exclusion with stepping via the state machine; each watch a wire op on
  `Task.Run`); not-paused → rows reset to `—`. New mutable `WatchRowViewModel` (value updates each pause).
  **Persistence per routine:** new Core `WatchStore` (section facade over the shared `settings.dat`, owns
  `UserSettings.DebugWatches`, additive — no schema bump), loaded in the VM ctor, saved on add/remove;
  `MainWindowViewModel` wires one on the same dir+protector as `ParameterHistoryStore`. **Side-effect flag:**
  new pure Core `WatchSideEffectDetector` **reuses the one `SqlLexer`** (no new parser) to flag a watch whose
  tokens contain a side-effecting keyword (`INSERT`/`UPDATE`/`DELETE`/`MERGE`/`EXECUTE`/`POST_EVENT`) — a
  bare-token match, so a keyword in a string/quoted-identifier never trips it; a conservative lexical warning
  cue (`±` + tooltip), not semantic analysis. UI: the right panel splits Variables (top) + Watches (bottom).
  **Deviation (documented, as seam a):** no standalone `WatchesPanelViewModel` — the collection + loop live on
  `DebuggerTabViewModel` (a separate panel VM would tightly couple to the session/eval/persistence for no
  gain); `WatchRowViewModel` is the row VM. **User backlog (recorded, NOT D5):** Immediate should pre-validate
  **syntax** locally via the existing `EditorLanguageService` (Lexer+Parser+Diagnostics) before the
  `EXECUTE BLOCK` — reuse the Language Service, syntax-only locally, semantics/execution stay the server's.
  Build 0/0; **4782 tests green in one run** (+6 `WatchStore`, +14 `WatchSideEffectDetector`, +6
  `DebuggerTabVmTests`); smoke clean. History: [docs/history/19-...](docs/history/19-firebird-debugger.md)
  (D5 seam b).
  **Debugger panel layout redesign — DONE (2026-07-18; UX only, no debugger logic change; live layout awaits
  user confirmation).** Done *before* D6+ adds panels (cheaper now). Analysis: future panels (Call Stack /
  Breakpoints / Output / result grid) are **width-hungry**, so — **right panel = Variables only** (primary
  inspection, full height, 300px); **bottom = a full-width, collapsible `TabControl`** (`bottom-tab` style,
  like the SQL editor) with **Immediate / Executed SQL / Watches** (a future panel = one `TabItem`). Full-width
  bottom (not under-editor-only) mirrors the SQL results panel + serves the width-hungry tabs; Variables get
  full height when the bottom is collapsed. **Collapse** = a chevron over the tab strip toggling the bottom
  grid **row height** (Auto ↔ pixel) in code-behind (`ApplyBottomPanel`, mirroring `MainWindow`); tab contents
  bind `IsVisible` to `!IsBottomPanelCollapsed` so Auto measures to the strip only. Immediate (REPL: input +
  latest result inline via new `LatestEvaluation`) vs Executed SQL (full audit) are non-redundant. New VM
  **presentation** members only: `IsBottomPanelCollapsed`/`ToggleBottomPanelCommand`, `LatestEvaluation`;
  `DebugSession`/`Evaluate`/`WatchStore`/`WatchSideEffectDetector` + Watches persistence/auto-re-eval untouched.
  Build 0/0; **4784 tests green** (+2 presentation); smoke clean. **Next milestone: D6 (Cursor Bridge). D6+ not
  started.**
  **Debugger tab UX follow-up — DONE (2026-07-18; live behaviour awaits user confirmation).** Two small IDE
  fixes, one commit, before D6: (1) **debugger tabs are session-transient** — `SnapshotCurrentTabs` skipped only
  the live-tool kinds, so a `Debugger` tab fell through and was persisted as a `Ddl` tab → an empty tab was
  "restored" on next launch; adding `WorkspaceTabKind.Debugger` to the skip-list means app-close captures
  nothing (restart restores nothing), and `ClearWorkspaceTabs` now also `DisposeAsync`-tears-down the debug
  session on **disconnect** (§4.4 rollback + close attachment) like the monitors. (2) **double-click the bottom
  panel's tab strip** toggles collapse via the **same** `ToggleBottomPanelCommand` (view `DoubleTapped`; toggles
  only on a `TabItem`-ancestor hit when expanded, any bar hit when collapsed, ignores the chevron button). Pinned
  by `DebuggerTab_IsTransient_NotCaptured`; build 0/0, 4785 green. History: [docs/history/19-...](docs/history/19-firebird-debugger.md).
  **D6 (Cursor Bridge) — DONE (2026-07-18; in-app stepping UX awaits user confirmation). `FOR SELECT` bodies
  step through a real incremental DSQL cursor.** Probes first (§F): FB3+FB5 cursor interleaving verified live
  (FB4 unavailable → unrecorded); `WHERE CURRENT OF` on a separately-opened DSQL cursor is unsupported
  cross-context (SQL -504) → a §F boundary, honest step error, not in DoD. **D6a** — additive AST: `ForSelectStatement.IntoTargets`
  (ordered folded INTO names) + `CursorName`, parsed order-independently (Contract #1 — don't token-scan the
  Firebird layer for structure). **D6b** — pure Core `CursorBridge` (`Build(source, loop) → CursorQueryPlan`,
  mirrors `HarnessBuilder`) + Firebird `CursorHandle : IDebugCursor` (real `FbDataReader` held open across
  steps, **per-wire-op** locking #236) + `FirebirdDebugExecutor.OpenCursor`. **§F correction caught live:** the
  first cut rewrote every binder-surfaced frame ref (bare + colon) → a `SELECT LINE_NO` column that shadows a
  `RETURNS (LINE_NO)` output param got rewritten to `?` → SQL -804; fix = rewrite **only** the colon/`@` form
  (unambiguous variable syntax; a bare name is a column), gotcha #239. Lab zoo +`SP_DBG_CURSOR`/`SP_DBG_NESTED`;
  **sim-vs-real fidelity proven** incl. a fully-stepped run + nested cursors (spec §15.5). Build 0/0, **4797
  green** (+11), smoke clean. History: [docs/history/19-...](docs/history/19-firebird-debugger.md).
  **Bottom-panel splitter double-click — root-cause fix (2026-07-18; live behaviour awaits user confirmation).**
  Three prior commits fiddled with the splitter gesture (a `_splitterGestureHeight` snapshot) and the panel
  still "glued" to the editor after collapse + re-expand. **Real cause** (found by comparing to the SQL editor):
  `ApplyBottomPanel` mutated only the bottom row, never the top — but Avalonia's `GridSplitter`
  (`PreviousAndNext`) converts the `*` top row to an **absolute pixel height** on a drag, so once dragged the
  grid has no star row to reclaim space. Fix = make `ApplyBottomPanel` the **single re-normalization point that
  sets both rows every toggle** (top → star, bottom → Auto/pixel), exactly like `ApplyResultsRowForActiveTab`;
  the `_splitterGestureHeight` workaround + its `PointerPressed` handler are **deleted** (full re-normalization
  absorbs the double-click micro-drag, as the SQL editor's maximize/restore does). Gotcha #240. Build 0/0, 4797
  green, smoke clean. **Root cause finally found (part 3, 2026-07-18):** parts 1+2 fixed the wrong place; the
  real bug was binding the **splitter's own `IsVisible` to `!IsBottomPanelCollapsed`** — the state its own
  double-click toggles (self-entangled). MainWindow's results splitter keeps visibility on an **independent**
  condition (`IsQueryTabActive`). Fix: debugger splitter is now **always visible** while the debug view shows +
  synchronous toggle (identical to `OnResultsSplitterDoubleTapped`); the `Dispatcher.Post` deferral removed.
  Gotcha #240 part 3. Awaits live confirmation.
  **D7 (Variables window, full) — DONE + user-confirmed (2026-07-18; grouping/filter/pin/edit/live-update all
  verified on the live lab).** The basic D4
  list becomes the rich window (spec §9.4). **Seam (a):** rows grouped **Pinned / Parameters / Locals** (kind
  glyph ⬤IN/◑OUT/○local coloured by a theme key string via `IconBrushConverter` — never a brush), declared
  types, distinct `<null>`, per-step **change-highlight** (reuses `FrameValues.Snapshot()`, new
  `DebugVariableChangedBrush` both dicts), **pin-to-top**, **type-to-filter**. Rows are mutable and updated
  **in place** so pins/expansion/selection survive a step; `Variables` stays the flat roster and
  `VariableGroups` is its grouped/filtered presentation over the same instances (one roster, two projections).
  Context (triggers → D10) + Cursors groups deliberately not shipped as empty groups (gotcha #233). **Seam
  (b):** **data tips** — a `DebugValue` section on the ordered aggregate `HoverInfo` (no `IHoverProvider`,
  rule #2); `HoverInfoEngine.GetHover` gained an **optional** value-lookup input (default null → SQL/object
  editors unaffected), threaded through `NavigationController`/`SqlEditorBehavior`, supplied by
  `DebuggerTabView` from the paused frame's roster; rendered first, reusing `QuickInfoView` chrome. **Inline
  edit** — double-click a value → text box (Enter commits via `frame.SetResolvedValue`, Esc cancels); a
  best-effort typed parse validates shape at edit time (red border on failure), the real domain CHECK
  surfaces on next injection (§3.4/§F); the box seeds from the full untruncated raw value (§0). **Lazy BLOBs**
  — a binary `byte[]` shows `[BLOB · N B]` and is not editable; long text truncates inline (256) but edits
  full; a dedicated value-viewer popup is a documented follow-up (no reusable viewer exists). Build 0/0,
  **4807 green** (+10), smoke clean. **Post-D7 UX bugfixes DONE (2026-07-18, awaits live confirm):** Variables
  kind icons now distinct SHAPE + hue per kind (▶ IN blue / ◆ OUT amber / ● local green — dedicated
  `DebugParamInBrush`/`DebugParamOutBrush`/`DebugLocalBrush` tokens, both dicts); the Pinned star is now **gold**
  (`DebugPinBrush`) with more spacing so it no longer blends into the kind glyph; and the splitter double-click
  root-cause fix above.
  **D8 (Call stack + nested stored routines) — seam (a) DONE (2026-07-18; pure Core, no server, no user-visible
  change yet).** D8's DoD needs a faithful Step Into of a stored procedure (pass its arguments, write its
  `RETURNING_VALUES` back), which the AST could not express — so seam (a) lands the **AST + Frame-model
  foundation** before the Firebird/UI seams. **AST deepening (Contract #1):** `ExecuteProcedureStatement` now
  produces `Arguments` (`IReadOnlyList<CallArgument>` — per-argument source spans, not tree children; a step-into
  slices + evaluates each in the CALLER frame to seed the callee's input params) + `ReturningTargets`
  (`IReadOnlyList<string>`, folded via the one `ForTargetName` reader). Parser producer `ReadProcedureCallParts`
  (paren-tolerant, top-level `RETURNING_VALUES` by text); **additive** — §0 tokens round-trip, formatter
  untouched, binder unchanged (it already binds the `:var` tokens via `BindPsqlExpression`). **Frame model —
  `LexicalParent` split from the call-stack `Parent` (the load-bearing correction):** D1 conflated the two, but
  a called **stored** routine has a caller (call stack / savepoints / write-back) yet is a **closed scope**
  (cannot see caller variables); left conflated, seam (b) would inject a caller's like-named variable into an
  unassigned callee local (§F bug). `TryResolveValue`/`SetResolvedValue` now walk `LexicalParent` (null for a
  stored callee — D8; the declaring frame for a local sub-routine — D9, exactly the spec §6 "lexical parent"
  language). `Frame`/`DebugRoutine` gained `OutputParameterNames` + `DebugRoutine.LexicalParent` (additive
  ctors). **Interpreter:** `ApplyReturningValues` on a callee's NORMAL return binds its outputs **positionally**
  into the caller's `RETURNING_VALUES` targets (spec §5; a no-op for root / no-returning / an unhandled unwind).
  A D1 test that used `execute procedure p` as a proxy for the scope-chain and (wrongly for a stored routine)
  asserted callee→caller resolution was split into an honest stored (closed-scope) test + a local (closure, D9
  mechanism) test — the fake executor gained an `asLocalClosure` mode. Build 0/0; **4813 green** (+6: 4
  `PsqlAstTests` arg/returning, +2 net `DebugEngineTests`); smoke clean. Gotcha #241 (LexicalParent vs Parent).
  **D8 seam (b) part 1 — `FirebirdDebugExecutor` multi-routine context (2026-07-18, behavior-preserving).** The
  executor held single-routine state (`_source`/`_model`/`_variableTemplates`/`_outputParameters`) — fine for
  the root, but a D8 call stack activates distinct routines. Refactored into a
  `Dictionary<BlockStatement, RoutineContext>` keyed by the routine's **body node**; every method reads
  `Ctx(frame)` and threads Source/Model/templates/outputs through the (now static) helpers. Root registers in
  `CreateAsync`; a callee will register in `ResolveRoutine` (part 2). Recursion correct for free (same body →
  one context; per-frame values on the `Frame`). **`ResolveRoutine` still returns null** → no callee frame
  pushed, single-routine path unchanged → 4813 green, live behaviour identical.
  **D8 seam (b) part 2 — `ResolveRoutine` + argument seeding + live fidelity DONE (2026-07-18). D8 DoD MET —
  Step Into a stored procedure works, proven simulated-vs-real on the lab.** `FirebirdDebugExecutor.ResolveRoutine`
  now resolves a standalone `EXECUTE PROCEDURE` to a real frame: (1) **fetches the callee source** on the debug
  session via a new internal `FirebirdDdlReader.BuildProcedureSourceAsync` seam (reuses the one CREATE-OR-ALTER
  reconstruction — Contract #17); (2) **parses** it (gotcha #238 strict whole-`CREATE PROCEDURE`) → model + body;
  (3) resolves frame templates (R2/R3) + the ordered **input** params (`DebugFrameLayout.InputParameters`);
  (4) **evaluates the call's arguments in the CALLER frame through the SAME harness** (Contract #4 — no second
  evaluator): a Statement-mode `EXECUTE BLOCK` assigning each argument to a synthetic `ET_ARG_i` **typed as the
  callee's i-th input-param base type** (R2) → seeds the callee input params positionally; (5) registers the
  callee context, returns a `DebugRoutine` (stored ⇒ `LexicalParent = null`, closed scope). The interpreter's
  `ApplyReturningValues` (seam a) binds the callee's outputs into the caller's `RETURNING_VALUES` on return.
  **Unresolvable call → step-over in place, 100% faithful §5.3** (non-EXECUTE-PROCEDURE, no name, package/qualified
  = D11, local sub-routine = D9, unreadable source). **§F boundary caught by probing not reasoning (gotcha #242):**
  `:name` is a SQL error as a PSQL assignment RHS (SQL -104, query-only syntax), so `RewriteColonRefsToBare`
  rewrites `:name`/`@name` → bare name **by span** over the tokens (mirrors CursorBridge #239; there → `?`, here →
  bare). **Lab fidelity (mandated §2.1 proof, `tools/probes/DebuggerFidelityProbe`, spec §15.6):** lab zoo +3-level
  chain `SP_DBG_LEAF`/`SP_DBG_MID`/`SP_DBG_ROOT`; the real executor Step-Into'd it — **depth 3 (`ROOT→MID→LEAF`),
  simulated `RESULT=112` == real `112`** (arg seeding + `RETURNING_VALUES` faithful across 3 frames), ALL PASS.
  Build 0/0; tests green (user-confirmed); smoke clean; no new unit tests (value = live fidelity, Contract #12).
  **D8 seam (c) part 1 — Call Stack panel (display-only) DONE (2026-07-18; awaits visual confirmation).** A new
  bottom `TabItem` (joining Immediate/Executed SQL/Watches) lists frames **innermost-first** from
  `DebugSession.CallStack`: routine name, position **line** (current statement for the innermost frame, **call
  site** for a caller — computed against **that frame's own source**), current-frame marker (▶), and the
  **simulated-frame indicator** (△ + tooltip) on any Step-Into frame (§5.3; root unmarked). Small Core enabler
  `Frame.Source`/`DebugRoutine.Source`/`DebugSession(rootSource)` — a frame carries its routine's text so its
  line resolves (and its source can later be shown). `DebugFrameRowViewModel` (immutable), rebuilt each pause by
  `RebuildCallStack` (reads the engine stack, never touches the session); cleared when not paused. **Display-only
  by design — frame-selection navigation (repoint editor+variables) needs the callee's own model roster surfaced
  to the VM (it lives in the executor's per-routine context, not the VM's root model), so it + Breadcrumbs
  (shared editor feature) + Peek Frame are seam (c) part 2.** Build 0/0; +1 `DebuggerTabVmTests` (126-test
  debugger subset green; full run hangs in this env — run manually); smoke clean.
  **D8 seam (c) part 2 — frame navigation DONE (2026-07-18; awaits visual confirmation). D8 IS COMPLETE.**
  The call stack is now navigable: selecting a frame (Call Stack row / breadcrumb / `Ctrl+Alt+Up/Down`)
  repoints the editor **source**, current-line **marker** and **Variables roster** to *that* frame (spec §5.2),
  and the editor auto-follows the innermost frame after a Step Into (the reported "editor stayed on the parent"
  gap). **Per-frame model surfaced (Contract #1 — no re-parse):** the callee's `SemanticModel` is threaded onto
  the frame exactly as `Source` already was — new `Frame.Model` / `DebugRoutine.Model` / `DebugSession(rootModel)`,
  filled by the launcher (`spec.Model`) for the root and by `ResolveRoutine` (the model it already built) for a
  callee; the Variables panel projects **that frame's own** roster, so a callee's locals show and the caller's
  don't. **The VM has ONE selection truth** (`ApplySelectedFrame`) that sets source + marker + variables + both
  selection controls together (`SelectedFrameRow` ⇄ `SelectedBreadcrumbIndex`, guarded), so a frame and
  everything mirroring it can't disagree; selection is **navigation only** — it never touches the session
  (`_selectedFrame` resets to the innermost on every pause). **Caller line corrected** (seam-c-part-1 bug): a
  caller's current line is the call site of its **child** (`stack[i-1].CallSite`, a statement in *this* frame's
  own source), not the frame's own call site (which is in its *parent's* source) — see gotcha #243. **Breadcrumbs
  = a genuinely shared, generic `EmberTern.App.Controls.BreadcrumbBar`** (segments + two-way `SelectedIndex`, no
  debugger knowledge; the debugger is its first consumer), mirroring the stack outermost→innermost. **Peek Frame**
  = double-click a call-stack row → a transient card previewing that frame's source (a debugger-local card, since
  the editor's Peek Definition is private to `NavigationController`). **Breakpoints stay root-routine-scoped**
  (`BreakpointOffsets`/`ToggleBreakpointAt`/`RunToCursorAsync` gated to `IsViewingRootSource`) — while the editor
  shows a callee/other-caller source the offsets are a different coordinate space, so none are surfaced;
  nested-routine breakpoints are D12. Stepping is unaffected. Build 0/0; **+5 `DebuggerTabVmTests`** (step-into
  switches source+roster, caller-frame repoint+marker, `Ctrl+Alt+Up/Down` walk, root-scoped breakpoints, Peek);
  full suite green (run manually — the full `dotnet test` hangs in this env, #94/#226); smoke clean. Gotchas
  #241/#242/#243. See [[feedback-debugger-ux-polish-backlog]].
  **D9 (Local procedures & functions — the flagship) — STARTED; §6.3 closure version gate MEASURED + RESOLVED
  (2026-07-18). No production code yet — this is the mandatory pre-implementation probe.** The plan makes
  §6.3 a hard blocker: §6.1 measured sub-routine closure semantics on **FB5.0 only**, and FB3 historically
  documented sub-routines as having *no* outer-variable access — if FB3 differs, the closure harness must
  branch on version. Measured with a new out-of-solution probe `tools/probes/Fb3ClosureProbe` (raw
  `EXECUTE BLOCK` against a throwaway scratch DB per instance — **no EmberTern interpreter**, so it measures
  the *engine*): **FB3.0.13 sub-routines are CLOSED scopes** (an outer var is rejected at compile, SQL -206
  "Column unknown") — FB3 is genuinely *simpler*, no closures; **FB5.0.3 sub-routines are true closures**
  (read + write, **by reference** — Q2=6, Q3=(5,99), Q4=77, confirming §6.1). **FB4 unverified** (not
  installed — only FB3.0+FB5.0 listening; recorded like P2's FB2.5 / D6's §15.5). **D9 consequence — no new
  abstraction:** the D8 `Frame.LexicalParent` split (gotcha #241) already models both worlds, so D9 only picks
  the lexical parent by server major (`FirebirdDdlReader.ParseServerMajor`, reused from P2) when it pushes a
  local-routine frame — **FB3 → `LexicalParent = null`** (closed, *forced correct*: an outer-referencing
  sub-routine can't even compile in the DB on FB3, so a closed frame is 100% faithful by construction; harness
  injects only the call arguments), **FB5 → `LexicalParent = declaring frame`** (outer reads/writes resolve up
  the chain; harness injects the read set R1–R4 + carries the decl verbatim R5 + reads writes back), **FB4 →
  conservative**, a documented §F boundary. Pure gate work — no production code touched ⇒ build 0/0 + tests
  unaffected. Spec §6.3 resolved, §15.7 log added, compatibility/open-items/roadmap rows updated; plan D9 risk
  resolved.
  **D9 seam (a) Part 1 — AST deepening + binder nested scope + extractor R5 carry — DONE (2026-07-18; pure
  Core, no runtime change).** Starting seam (a) exposed that spec §6.2a was too optimistic against the AST: a
  local sub-routine was **not modelled** — its `DECLARE PROCEDURE/FUNCTION` header was a lossless
  `PsqlLeafKind.Other` leaf and its body a bare sibling `BlockStatement` in the enclosing flat `Statements`, so
  the interpreter would step onto the unrunnable header + through the body as main flow, and `ResolveRoutine`
  would have to re-scan tokens (a **Contract #1** violation). So — exactly as D8 seam (a) deepened the AST
  before its Firebird seam — D9 seam (a) is split: **Part 1 = pure Core AST + binder + extractor; Part 2 =
  runtime.** New `SubroutineDeclaration : PsqlStatement` (+ `SubroutineKind`) carries `Kind`/`Name`/a real
  `Body` (null = forward declaration); its span/tokens cover the whole sub-routine (header + body) so `Body`
  nests (well-formedness). New `BlockStatement.LocalRoutines` (between `Declarations` and `Statements`); its
  `Children` now **merge** decls + local routines by source position (Firebird permits either order). Parser
  (`SqlParser.Psql.cs`): a shared `ParseDeclarationSection` consumes both, `ParseSubroutineDeclaration` ends the
  header at the first depth-0 `AS`/`;` (**not** the first `;` — a sub-routine's own `DECLARE VARIABLE …;` ends
  in one) and parses the body **block-scoped** (`ParseScopedBlockBody`, non-lenient — never swallows the
  enclosing `BEGIN`); the dead `ParsePsqlHeaderLeaf` + unused `IsDeclarationStart` deleted. Binder
  (`SemanticBinder.Psql.cs`): `BindLocalRoutine` gives each sub-routine the **first genuine nested scope** in a
  PSQL body (own params + `RETURNS` + locals in a child `RoutineBody` scope; outer vars resolve via the parent
  walk = the FB5 closure — the FB3 closed-scope distinction is Part 2's `LexicalParent`-by-version, the static
  editor stays permissive). Extractor: `RoutineDeclarations.SubRoutines` now filled verbatim (R5). **No runtime
  change — `ResolveRoutine` still returns null for a local call (step-over in place, §5.3).** Build 0/0; **4848
  tests green** (+7 `PsqlAstTests`, +2 `SemanticModelTests`, +1 `PsqlDeclarationExtractorTests`, +4 corpus
  shapes); smoke clean.
  **D9 seam (a) Part 2 — local-procedure step-into runtime + live fidelity DONE (2026-07-18). Step Into a
  local `DECLARE PROCEDURE` now works as a real debugger frame — proven simulated==real on the lab.**
  `FirebirdDebugExecutor.ResolveRoutine` now resolves a local sub-procedure **before** any server source fetch:
  a new `TryFindLocalProcedure` walks the frame's lexical chain (`f.Body.LocalRoutines`, then `LexicalParent`'s,
  … — spec §6) for a `SubroutineDeclaration { Kind: Procedure, Body: not null }` whose name matches; a match →
  `BuildLocalRoutineAsync` builds the callee frame from the **already-parsed** `Body` (no source fetch — the
  body is part of the enclosing routine's AST), seeds its input params through the **same** D8 argument-seeding
  harness (Contract #4 — no second path), and returns a `DebugRoutine` sharing the **enclosing** source + model
  (a local routine's spans + scope live in the enclosing routine, not a separate compilation). **`LexicalParent`
  by server major (§6.3 gate):** `ParseServerMajor` ⇒ FB5 → the declaring frame (true closure), FB3/FB4 → null
  (closed scope; FB4 conservative, a documented §F boundary). **The one new metadata path** —
  `FirebirdDebugMetadata.BuildLocalRoutineFrameVariablesAsync`: a local routine is **not** a catalog object
  (no `RDB$PROCEDURE_PARAMETERS` row), so its parameter + `RETURNS` types come from the **AST header** via new
  pure-Core `PsqlDeclarationExtractor.ExtractSignature` (params/RETURNS read from the sub-routine's own tokens,
  Contract #1 — never re-parse), each declared verbatim (R3) with its base type derived from `RDB$FIELDS`
  (R2, reusing `ResolveBaseTypeAsync` — a domain param resolves, a builtin is itself); body locals as usual.
  **R5 wired into the harness** (`RoutineContext.SubRoutines`, computed once from `PsqlDeclarationExtractor.Extract`
  and threaded into every `HarnessRequest`): a routine's local sub-routine declarations are carried verbatim so
  a statement that calls a local **function** (or procedure) binds to the local — a local `DECLARE FUNCTION` is
  exercised faithfully server-side as a step-over (it is called inside an expression, never an
  `EXECUTE PROCEDURE` step point, so it is not step-into'd). **Scope boundary (seam a part 2):** the local
  routines are **self-contained** — outer-variable **closure injection** (the closure harness) + the transitive
  read/write-set fixpoint over the sub-routine call graph are **seam (b)**; a self-contained local routine does
  not exercise the closure, so the `LexicalParent` choice is behaviourally inert here but set correctly for
  seam (b). **Lab zoo +`SP_DBG_LOCAL`** (`Lab/setup.sql` + rebuilt `.fdb`, #149): a local function `TRIPLE` +
  local procedure `ADD_TAX` (param seeding, `RETURNING_VALUES`, its own local `BONUS`). **Live fidelity PROVEN**
  (`tools/probes/DebuggerFidelityProbe` extended, not duplicated): the real executor Step-Into'd `ADD_TAX` —
  depth 2, frame chain `SP_DBG_LOCAL → ADD_TAX`, **simulated `TOTAL=115` == real `115`** (arg seeding +
  `RETURNING_VALUES` + the local function server-side), ALL PASS; D8's stored-chain cases unchanged. Build 0/0;
  **4852 tests green** (+4 `PsqlDeclarationExtractorTests` for `ExtractSignature`); smoke clean.
  **D9 seam (b) Part 1 — closure capture for stepped-INTO frames DONE + live-fidelity-verified (2026-07-18).
  Step Into a local routine whose body READS and WRITES an OUTER variable (an FB5 closure over the declaring
  frame) — proven sim==real on the lab.** Three coordinated changes, all reusing existing architecture (no new
  abstraction — the D8 `Frame.LexicalParent` chain already models closures): **(1) Core `Frame`** gained a
  declared-names set (params + body `DECLARE VARIABLE`s) so the scope-chain walk (`TryResolveValue`/
  `SetResolvedValue`) knows which frame **owns** a name from entry — a not-yet-assigned local resolves/writes in
  its declaring frame and correctly **shadows** a like-named outer variable (`Owns(name) = declares(name) ||
  Values.Contains(name)`; empty = old Values-only behaviour, so the fake-driven D1 tests are unaffected).
  **(2) Core `DebugSession`** routes every statement/cursor/Immediate write-back through the closure chain (new
  `ApplyWrites` → `frame.SetResolvedValue`), so a write to a captured outer variable lands in the **declaring**
  frame, not as a spurious callee local (previously `frame.Values.Apply` wrote only the callee frame — a §F bug
  for a closure). Identical to the old direct apply for a non-closure frame (no lexical parent). **(3) Firebird
  `FirebirdDebugExecutor.BindValues`** is closure-aware: beyond the frame's own templates it declares **every
  ancestor frame's variables up the lexical chain** (verbatim R3, current value resolved through the chain),
  so the harness for a sub-routine statement can declare + inject + write back a captured outer variable; an
  inner declaration shadows a like-named outer (first-seen wins). The read/write set for such a statement is
  already precise (`Analyze` surfaces the outer reference via the shared enclosing model) — **no fixpoint
  needed for step-INTO**. **Lab zoo +`SP_DBG_CLOSURE`** (a local `PROCEDURE BUMP` reading+writing outer `ACC`;
  FB5-only by construction — FB3 closed scopes can't compile it, §6.3); `DebuggerFidelityProbe` extended:
  Step-Into'd `BUMP` twice — depth 2, `SP_DBG_CLOSURE → BUMP`, **sim `TOTAL=25` == real `25`** (ACC 5→15→25,
  the closure write reaching the parent), ALL PASS. Build 0/0; **4853 tests green** (+1 `DebugEngineTests`
  pinning the interpreter's write-back routing); smoke clean. **⚠ Boundary — seam (b) Part 2 (NOT done):** a
  **step-OVER** of a local call **with direct arguments** (`EXECUTE PROCEDURE p(x) RETURNING_VALUES y`) whose
  callee mutates OTHER outer variables (not `x`/`y`) still drops those mutations — the transitive read/write-set
  **fixpoint over the sub-routine call graph** is Part 2. (A no-arg local call already over-includes correctly
  via the §3.5 `InScopeLocals` fallback + R5; step-INTO is fully correct.) History:
  [docs/history/19-...](docs/history/19-firebird-debugger.md) (D9 gate + seam a + seam b Part 1). See [[feedback-debugger-ux-polish-backlog]].
  **D9 seam (b) Part 2 — transitive read/write-set fixpoint DONE + live-fidelity-verified (2026-07-18). D9 IS
  COMPLETE.** Closes the last gap: a **step-OVER** of a local call (`EXECUTE PROCEDURE p(x) RETURNING_VALUES y`,
  or a local function call `z = f(x)` inside a leaf — always a step-over) whose callee captures an OUTER
  variable **not named at the call site** now injects that capture and reads its mutation back. New pure-Core
  `SubroutineCatalog` (name → `SubroutineDeclaration`, built by the executor from `BlockStatement.LocalRoutines`
  up the lexical chain) is an **optional** third argument to `ReadWriteSetAnalyzer.Analyze` (default null =
  today's direct-reference set, so D2–D8 are byte-identical): when a statement calls an in-scope local
  sub-routine, the analyzer folds in that callee's **transitively-referenced captured** variables — the callee's
  own body references (span-collected from `model.References`, which is inherently transitive for a **nested**
  sub-routine whose body lies within its parent's span) plus, recursively, every catalog sibling it calls (a
  **visited set** terminates mutual recursion) — then keeps only the ones **in scope at the call site**
  (`InScopeLocals`, so the callee's own params/locals drop out) and adds them to **both** reads and writes
  (over-inclusion is §F-safe: a returned-but-unchanged value writes itself back; an injected-but-unused value is
  harmless). **Reuses the binder for variable references** (Architecture rule #2 intact) — the only new signal
  is *call detection*, a conservative **name-membership** check of the statement's tokens against the
  AST-authoritative catalog (not a variable resolver; the AST models the call graph but the binder does not yet
  resolve local calls as symbols — the seam-a-part-1 note), which covers both an `EXECUTE PROCEDURE` proc call
  and an expression-embedded function call. The executor threads the catalog through `ResolveReadWrite` for the
  statement + condition harnesses (D5 `Evaluate` already uses the `InScopeLocals` superset — no change). **Lab
  zoo +`SP_DBG_CLOSURE_FN`** (a local function capturing `HIDDEN`) **+`SP_DBG_CLOSURE_OVER`** (a local procedure
  capturing `HIDDEN`, stepped OVER); `DebuggerFidelityProbe` extended (`SimulateAsync` gained a `StepKind`
  param): **sim `TOTAL=15` == real `15`** for both — the hidden capture injected + written back across the
  call, ALL PASS. Build 0/0; **4856 tests green** (+3 `ReadWriteSetAnalyzerTests`: function capture with the
  in-scope filter, transitivity across the call graph, null/empty-catalog = direct set); smoke clean.
  **D9 CORE — the flagship — is COMPLETE:** local procedures and functions are real, steppable debugger frames
  with real closure variables; a local **procedure** is faithful step-into *and* step-over, a local function is
  faithful step-over — the capability IBExpert cannot deliver. History:
  [docs/history/19-...](docs/history/19-firebird-debugger.md) (D9 gate + seam a + seam b Parts 1 & 2).
  See [[feedback-debugger-ux-polish-backlog]].
  **D9 seam (c) — local-FUNCTION step-into — DESIGNED, NOT IMPLEMENTED (2026-07-18); the immediate next task
  before D10.** Manual QA found the one asymmetry: Step Into works for a local *procedure* but a local
  *function* runs whole (effectively Step Over) — a complex local function's body can't be traced line by line.
  Not a §F/correctness bug, the last usability gap in the flagship. **Ratified design (closed, no code):** Step
  Into descends into a local function **only when the call is the ENTIRE operand of a value-consuming position**
  (`v = f(x)` / `RETURN f(x)` / `IF f(x)` / `WHILE f(x)` — **Variant A, one mechanism, all four**), so the
  client never evaluates a surrounding expression (a proper sub-expression like `f(x)+1` / `a AND f(x)` /
  `VALUES(f(x))` stays step-over — a permanent §F boundary, §6.4). **No new server path, no expression
  evaluator, no delivery/mini-harness:** reuse Statement + Expression Harness, `SeedInputParametersAsync`,
  `SetResolvedValue`/`ApplyReturningValues`, `Frame`/`LexicalParent`/closures; the return value is delivered
  **client-side** via `SetResolvedValue` (as procedures do for `RETURNING_VALUES`); `RETURN <expr>` computes via
  the **Expression Harness** (never the Statement Harness — `RETURN` is invalid in `EXECUTE BLOCK`); the
  mechanism is a **Function Return Continuation** (a generalisation of `ApplyReturningValues`). Small AST
  deepening only (`CallExpression` + additive lone-call props; Contract #1 — token-scan rejected). Sub-steps
  c1 (AST) → c2 (Core interpreter, fake executor) → c3 (Firebird executor + live fidelity, sim==real on the
  lab); optional c4 UI polish. Full design + handoff: [docs/history/19-...](docs/history/19-firebird-debugger.md)
  §"D9 seam (c)"; milestone brief + danger zones:
  [firebird-debugger-implementation-plan.md](docs/design/firebird-debugger-implementation-plan.md) (D9 §"seam (c)");
  spec §6.4 + boundary #13. **c1 — AST only (pure Core) — DONE (2026-07-19):** `CallExpression` +
  `PsqlLeafStatement.RhsCall`/`.AssignmentTarget` + `IfStatement`/`WhileStatement.ConditionCall`, set by
  **strict** parser producers (whole-operand lone call only; trailing op / second call / dotted target /
  sub-expression ⇒ null ⇒ step-over), reusing D8's `ReadCallArgumentList`/`MatchParenTok`; +
  `SubroutineSignature.ReturnType` (function `RETURNS` type spec, R2 input). Producer-only — no consumer yet
  (`ResolveFunction`/`EvaluateReturn` are c2/c3, staged per gotcha #233); §0 round-trip unchanged,
  binder/formatter untouched. Build 0/0; +19 `PsqlAstTests` +3 `PsqlDeclarationExtractorTests` +2 corpus
  (targeted green; full suite hangs #94/#226 → user-verified green); smoke clean.
  **c2 — Core interpreter (fake-executor-driven) — DONE (2026-07-19):** new internal `FunctionReturnContinuation`
  (`AssignTo`/`SetFrameReturn`/`BranchIf`/`DecideWhile`) with a `RecognizeStepInto` factory — **the single place**
  the interpreter decides step-into + which continuation consumes the return (not scattered across the
  IF/WHILE/leaf cases; the user's architectural request). `Frame` gained `ReturnType`/`ReturnValue`/
  `ReturnContinuation`/`IsFunctionFrame` + `SetReturnValue`/`TerminateForReturn`; `IDebugExecutor` gained
  `ResolveFunction` + `EvaluateReturn` (+ `ReturnOutcome`, `DebugRoutine.ReturnType`). `DebugSession` got two
  guarded `ExecuteCurrent` branches (step-into a resolved local function; a `RETURN <expr>` in a function frame
  → Expression-Harness `EvaluateReturn`) and ONE delivery switch `ApplyReturnContinuation` generalising
  `ApplyReturningValues`; a raised function unwinds without firing the continuation. `FirebirdDebugExecutor` got
  **c2 stubs** (`ResolveFunction`→null, `EvaluateReturn`→throw) so **live behaviour is byte-identical to D9
  core** until c3. Build 0/0; +11 `DebugEngineTests` (4 positions + deliver, nested `RETURN f()`, IF then/else,
  WHILE iteration, unresolved/step-over, raising ⇒ no continuation, plain `RETURN` ⇒ EvaluateReturn); targeted
  green (508); full suite hangs #94/#226 → user-verify; smoke clean.
  **c3 — Firebird executor + live fidelity — DONE (2026-07-19):** `FirebirdDebugExecutor.ResolveFunction` walks
  the lexical chain (generalised `TryFindLocalRoutine(name, frame, kind)`, shared with `ResolveRoutine`) for a
  local `DECLARE FUNCTION` — a **local shadows a same-named global** — builds its frame from the AST body,
  seeds args through the **shared** `SeedInputParametersAsync` (generalised to `(arguments, callTokens,
  callStart, …)`), and carries the `RETURNS` base type; `EvaluateReturn` computes the `RETURN` operand via the
  Expression Harness through a new `EvaluateExpression` **shared with `EvaluateCondition`** (one server path).
  `FirebirdDebugMetadata`: `DebugFrameLayout.ReturnType` (R2, via `ResolveBaseTypeAsync`). Lab zoo +4
  (`SP_DBG_FN_POS`/`_TYPES`/`_SHADOW`/`_CLOSURE`; `.fdb` rebuilt #149). **Live fidelity PROVEN (spec §15.11,
  `DebuggerFidelityProbe` cases 8–11 + re-pointed 4/6): all four positions (depth 3), six return types
  (INTEGER/BIGINT/NUMERIC/VARCHAR/BOOLEAN/NULL), shadowing, nesting, a closure — all sim == real.**
  **Live-verified: Firebird forbids nested sub-routines** (gotcha #244) ⇒ shadowing is local-vs-global.
  Build 0/0; 508 Core tests green (c3 is Firebird/metadata only, no Core change); smoke clean.
  **🏁 D9 IS COMPLETE — local procedures *and* functions step faithfully, into and over.**
  **D10 (Triggers) — STARTED; architecture review DONE + ratified, Seam A (pure Core) DONE (2026-07-19). Split
  into 3 committable seams (A Core → B Firebird+Live-Fidelity → C UI), mirroring D8/D9; user decisions: NEW/OLD
  in a `FOR SELECT` cursor is a §F boundary (clear refusal, not partial fidelity); lab to be extended with
  BEFORE DELETE + BEFORE INSERT OR UPDATE (full trigger matrix); "seed from a real row" deferred to Seam C2.**
  Two ratified architecture decisions shaping the milestone: (1) **there is no heavyweight `TriggerContextModel`
  — trigger context is *specialized state* mounted as an optional field on the existing per-routine context**:
  NEW/OLD are ordinary frame variables (synthetic names), values live on the frame, only the simulated event +
  timing are genuinely new; (2) **`ContextSubstitution` is entirely `SemanticModel`/`SymbolReference`-driven,
  never a text search** — confirmed feasible because the binder records the `NEW`/`OLD` `RecordAlias` reference
  **and** the following `Column` reference (span + text) *even in the debugger's metadata-less model* (the
  member does not resolve to a `ColumnSymbol`, but its reference span/text is all the engine needs). **Seam A —
  pure Core, no server, no UI, unwired (gotcha #233):** new `EmberTern.Core.Sql.Debugging.ContextSubstitution`
  (the ONE engine, designed to also serve the §3.6 handler error context — one mechanism, two consumers):
  `BuildColumns(model, scope)` assigns each distinct `NEW.col`/`OLD.col` a **stable, compact synthetic name**
  (`ET_CTX_i` — index-based, so it stays a valid ≤31-char identifier regardless of column-name length, FB3's
  limit; ContextSubstitution is the single owner of the naming convention), and
  `Substitute(model, source, region, context)` rewrites each `NEW.col`/`OLD.col` reference span to its synthetic
  and each `INSERTING`/`UPDATING`/`DELETING` predicate to `TRUE`/`FALSE` for the simulated event — reporting the
  context reads (inject) and writes (return; `NEW` only when a BEFORE trigger, over-inclusive but never missing
  a write; `OLD` never). New `TriggerContext` record (`TargetTable`/`Event`/`Timing`/`Columns`) with the §8.1
  availability rules as computed properties (`OldAvailable`/`NewAvailable`/`NewWritable`) — the value that Seam B
  mounts on `RoutineContext`. Pinned by 13 `ContextSubstitutionTests` built the debugger's way (**strict parse,
  NO metadata**): distinct/deduped columns, synthetic rewrite + read/write split, AFTER = no NEW write,
  predicate literals per event, **string-literal `'OLD.STATUS'` left byte-intact beside a real `OLD.STATUS`**
  (reference-driven proof), no-context verbatim, full availability matrix. Build 0/0; Seam A tests + 191
  neighbouring Core/semantic tests green; smoke clean.
  **D10 Seam B — Firebird executor + metadata + Live Fidelity — DONE + live-fidelity-verified (2026-07-19).**
  The trigger substitution is wired end-to-end and proven sim==real on the lab. **`RoutineContext` gained an
  optional `TriggerContext?`** (non-null only for a trigger root frame — a stepped-into stored/local callee has
  no NEW/OLD, so D8/D9 paths are untouched); a new `FirebirdDebugExecutor.CreateAsync` trigger overload merges
  the **NEW/OLD context columns** into the frame templates and registers the context on the root.
  `ExecuteStatement`/`EvaluateCondition` route every trigger-frame fragment/condition through
  `ContextSubstitution` (unioning the context reads/writes into the harness read/write set); `OpenCursor`
  **refuses a `FOR SELECT` that references NEW/OLD** with a clear message (the §F boundary, decision 2). One new
  metadata path — `FirebirdDebugMetadata.BuildTriggerContextVariablesAsync` — types each context column from the
  **trigger's target table** (`RDB$RELATION_FIELDS ⨝ RDB$FIELDS` via the existing `FormatType`, derivation not
  guessing). `DebugLaunchSpec`/`FirebirdDebugSessionLauncher` carry the `TriggerContext` through. **Two §F
  corrections found by probing, not reasoning:** (a) a context variable is declared with the column's **BASE
  type, never its domain** — a NEW/OLD field is a record field, not a domain-constrained local, so a
  user-supplied value that violates the column's `CHECK` (the very case a BEFORE trigger catches) must inject
  freely (gotcha #246; injecting `-5` into a `D_AMOUNT CHECK(VALUE>=0)` domain died on entry before the
  trigger's own logic); (b) inside an embedded DSQL statement a context reference is **colon-prefixed**
  (`:ET_CTX_i`) — a bare name there is read as a column — while a PSQL expression keeps it bare, chosen by
  `node is not PsqlStatement` (gotcha #247). **Lab extended** (`Lab/setup.sql` + rebuilt `.fdb`, #149) with an
  isolated `TRIG_LAB` table + a **BEFORE DELETE** (`TR_TRIG_BD`, OLD-only) and a **BEFORE INSERT OR UPDATE**
  (`TR_TRIG_BIU`, multi-action) trigger — isolated so they never clobber each other or the ORDERS triggers,
  closing the full trigger matrix. **Live fidelity PROVEN** (`DebuggerFidelityProbe` +5 cases, spec verification
  method — compare the body's *effects* since the triggering DML is not performed): BEFORE UPDATE exception
  (E_NEGATIVE_AMOUNT) sim==real, AFTER UPDATE audit side-effect (the `AUDIT_LOG` DETAILS row, read from the debug
  tx) sim==real, BEFORE DELETE (OLD-only) exception (E_ORDER_LOCKED) sim==real, and the multi-action BIU trigger
  producing `NEW.NOTE='INSERTED'` (INSERTING) / `='UPDATED'` (UPDATING) sim==real for both events — plus all 11
  D8/D9 cases still green (no regression). Build 0/0; 122 debugger Core/Firebird unit tests green; smoke clean.
  **D10 Seam C (UI) — DONE + user-confirmed (2026-07-19; commit `050b790`). 🏁 D10 COMPLETE — triggers debug
  end-to-end.** `TriggerHeaderReader` (Core) derives `(TargetTable, Timing, Events)` from the parsed trigger
  (refuses DB-level/DDL, §8.1); `TriggerContextEditorViewModel` is a **dumb VM** — availability read from Core
  `TriggerContext`, NEW/OLD grids show **only referenced columns** (typed via `EnsureColumnsAsync`), values mapped
  onto their synthetic frame variables; `DebuggerTabViewModel` trigger mode (prepare/launch), a Variables
  **Context** group (resolved via new `DebugVariableRowViewModel.ResolveName` = synthetic); "Debug trigger…" entry
  points (sidebar + trigger-editor toolbar). **QA-found + fixed — gotcha #248:** a `NEW`/`OLD` reference inside a
  **scalar subquery embedded in a PSQL assignment** was emitted bare → Firebird read it as a column (SQL -206);
  the colon-vs-bare decision is now **per-reference** from AST-derived colon-regions
  (`FirebirdDebugExecutor.ColonRegions`, each embedded `SubqueryExpression` span), not per-statement. Lab
  +`TRIG_SUBQ_LAB`/`TR_SUBQ_BU`; `DebuggerFidelityProbe` case 17 sim==real. "Seed from a real row" stays a
  future **C2**. **Terminal debug states (Completed / Faulted) — DONE + user-confirmed (2026-07-19; commit
  pending after this doc):** the session no longer clears on end. **Completed** keeps the last state visible with
  the closing **`END` marked** (execution finished — IBExpert-like), Variables/Context/Call-Stack showing final
  values; **Faulted** stops **on the raising statement** (marked), keeps Variables/Context/Call-Stack with the
  values **at the error**, and the status line goes **red+bold** (`IsFaulted`); in both, stepping is disabled and
  only **Restart/Stop** are active — **Stop** tears the session down + clears. Additive Core: `DebugSession`
  retains `FinalFrame`/`LastStatement` (Completed) and snapshots `FaultStatement`/`FaultFrame`/`FaultStack`
  **before** the exception unwind (which DB-rolls-back + pops but never touches client-side `frame.Values`) —
  `CurrentFrame`/`CallStack` still null after termination (existing contract unchanged). Running / Completed /
  Faulted / Stopped are now distinct debugger states.
  **Superseded note (D5 seam b already shipped): —
  Watches panel + per-routine persistence** (auto-re-evaluate after each step through the same
  `DebugSession.Evaluate`; flag a non-pure-expression watch; persist per routine). Order stays **risk-first**
  (P1 → P2 → D1 → D2 → D3 → D4 → D5 …). **Read the plan + your milestone's brief before writing any debugger code.**
- **Save-and-close / Save-and-disconnect — DONE + user-confirmed (2026-07-17).**
  The close/disconnect WorkGuard can now **compile every dirty metadata editor in one pass** instead
  of only listing-and-discarding them. It **reuses the group-recompilation pipeline** (one save
  mechanism, not a second): each editor's existing compile is wrapped by a thin
  `ISavableObjectEditor.SaveAsync` adapter (structured pass/fail; editors swallow errors into
  `ErrorMessage`, so the adapter reads `ErrorMessage is null`), and `RunBatchWithReportAsync` gained
  an optional `executeAsync` strategy delegate so `SaveDirtyEditorsAsync` drives those `SaveAsync`
  calls through the **same** batch-results dialog (recompile's SQL path is the unchanged default).
  **Continue-and-report** (user decision): all dirty editors are attempted; close proceeds only if
  all succeed, else it aborts and selects the first failed tab — DDL auto-commits per object, so a
  mid-batch failure never undoes the ones already saved. App close adds **Save and exit**; disconnect
  is **two-phase** (metadata Save/Discard/Cancel → the unchanged tx Commit/Rollback/Cancel).
  **Scope includes new objects.** Save order = tab order (a deliberate v1 simplification; dependency
  ordering is a possible future refinement, not required — continue-and-report + retry covers it).
  `_bulkSaveInProgress` suppresses the per-compile "recompile dependents?" offer mid-shutdown. New
  `ISavableObjectEditor` + `EditorSaveResult`; adapters on every object editor;
  `WorkspaceTabViewModel.SavableEditor`. Build 0/0, tests green (`DataLossGuardTests` +save cases).
  Full detail: [docs/history/08-...](docs/history/08-data-loss-sidebar-and-searchable-combo.md);
  gotcha #231 (decide from the loop's tally, not the batch dialog's `IProgress`-lagged counters).
- **⚠ Corrected 2026-07-27:** an entry here used to claim `feat/editor-language-frontend` was an active,
  unmerged branch holding the editor rebuild. It has not existed for a long time — that work (Etaps 0–6 +
  UX Polish incl. P8, the 2026-07-14 UX & Stabilization Sprint, and its follow-up sprint below) is **on
  `master`**. See "Git remotes & push workflow" for the live branch list.
- **UX Polish follow-up sprint (2026-07-14) — DONE** (4 tasks + 2 review fixes, separate commits):
  **(1) trigger context variables** — NEW/OLD/INSERTING/UPDATING/DELETING get a distinct semantic
  highlight (new `SemanticHighlightClass.ContextVariable`, higher-chroma amber `#E5C07B`). Done
  through the semantic model, not an editor exception: the binder declares the predicates
  (`TriggerPredicateSymbol`) into the trigger's routine-body scope, so they resolve ONLY inside a
  trigger — none are reserved words. **Review fix:** the trigger DETAIL editor edits a body-only
  `begin…end` (no CREATE TRIGGER header), so `TriggerDetailTabViewModel.BuildAmbientSymbols` now seeds
  NEW/OLD (bound to the table) + the predicates as **ambient symbols** — the same seam the routine
  editors use for out-of-text params/vars (gotcha #218). **(2) occurrence highlight** retinted from
  warm gold (→ muddy brown on dark) to a subtle accent-blue wash (theme token `OccurrenceHighlight*`).
  **(3) formatter WITH/CTE** — **now AST-modelled**: the parser builds a `WithClause` +
  `CommonTableExpression` nodes (`Ast/CteNodes.cs`; `SelectStatement.With`), and the formatter
  *consumes* the AST (no token-level CTE parsing in the formatter). Set operators (UNION [ALL]/
  INTERSECT/EXCEPT) break onto their own line via the one `MatchStructuralPhrase` mechanism. A CTE
  query is one statement — no blank line before the main SELECT. **(4) Easy-mode DDL casing** — new
  `DdlGenerator.PresentIdentifier` folds a picked domain to UPPERCASE + bare in generated DDL (regular
  ASCII identifiers only — §0-safe; special/case-sensitive names preserved verbatim + quoted), kept
  distinct from `SqlFormatter` (which preserves its own casing on existing source).
- **Build**: 0 warnings / 0 errors (`TreatWarningsAsErrors=true`). **Tests**: **8345, MEASURED 2026-08-09**
  (Product Polish through **M4.4** + M4's density decision + typography block + the stabilization sprint
  S-1…S-6 + the grid consistency sprint + the Firebird language completeness sprint incl. its QA round).
  Green in the three documented partitions (**8193 + 97 + 55**).
  ⚠⚠ **NIEAKTUALNE OD 2026-08-10 — po ZAMKNIĘCIU CAŁEGO M5 suma to 8392
  (8214 + 123 + 55).** Zdanie wyżej zostaje jako zapis stanu po M4; ⛔ nie cytuj go jako bieżącego.
  ⚠⚠ **I TO TEŻ JEST JUŻ NIEAKTUALNE — po ZAMKNIĘCIU CAŁEGO pakietu UX po M5 suma to 8467
  (8280 + 132 + 55), zmierzone 2026-08-10.** ⭐ Trzeci warstwowy dopisek do jednej linii w ciągu dwóch dni jest sam w sobie
  pomiarem: **licznik trzymany w prozie starzeje się co etap**. Zmierz przed cytowaniem; nie kopiuj żadnej
  z tych liczb w przód.
  ⏭ **Żywa liczba jest w pierwszym wpisie „Current state" (etap Localization) — po C8: 8 730
  (8 389 + 286 + 55), a filtr partycji headless ma 22 nazwy.** ⛔ Nie cytuj liczb z tego akapitu; to zapis
  historyczny pięciu kolejnych etapów, zostawiony jako dowód, że licznik w prozie starzeje się co etap.
  ⛔⛔ **I filtru też nie przepisuj — WYPROWADZAJ go:**
  `grep -rln HeadlessCollection tests/EmberTern.Tests/*.cs`.
  ⭐ **M‑3 (+14), §9 (+1) i M‑4 (+6) trafiły w całości do partycji GŁÓWNEJ** (8193 → 8214), bo żaden z tych
  testów nie konstruuje kontrolek Avalonii — czytają ViewModele i ŹRÓDŁA. ⭐⭐ Krucha ręczna lista nazw
  w filtrze partycji headless **nie urosła przez CAŁY M5 ani razu**, przez sześć iteracji z rzędu.
  ⭐ Wcześniej: §10 i L‑1 dołożyły **26**
  testów i **wszystkie** wylądowały w partycji ZGRUPOWANEJ (97 → 123), bo obie dopisały się do
  **istniejącej** klasy `DesignTokenApplicationTests` — krucha ręczna lista nazw w filtrze partycji
  headless **nie urosła ani o jedną pozycję**. ⭐ +6 to trzy teorie kontrastu × dwa motywy,
  wszystkie dołączone do **istniejącej** klasy `DesignTokenApplicationTests`, więc krucha ręczna lista nazw
  w filtrze partycji headless **nie urosła** — a rosnąca jest wyłącznie partycja ZGRUPOWANA (97 → 103).
  ⚠ M4.4: +11, wszystkie w partycji GŁÓWNEJ — 8 przypadków arytmetyki sufitu (`CeilingFor`, czysta statyka,
  wzorzec `ClampOnScreen`), 2 strażniki strukturalne M‑5 (sufit wymaga `ScrollViewera`; tablica 16 okien
  `SizeToContent` → decyzja + powód, egzekwowana w obie strony) i 1 relacyjny na bliźniaczych dialogach
  komunikatu. ⭐ Żaden nie zakłada nowej klasy headless — arytmetyka jest czysta, a strażniki czytają ŹRÓDŁO,
  więc krucha ręczna lista nazw w filtrze partycji nie urosła.
  ⚠ M4.2b: +16 netto — 7 CZYSTYCH testów reguły ←/→ (`SidebarKeyboardNavigationTests`, bez sesji headless,
  bo reguła żyje w kontrolerze), 4 behawioralne w partycji headless (`DependencyTreeRenderTests`, w tym
  REALNY klawisz przez pełny pipeline), 6 strażników źródłowych, minus usunięty test kategorii „UDF".
  ⚠ M4.2's +2 are both main-partition and both source-reading: the structural guard requiring an icon geometry
  declared outside `IconGeometries.axaml` to carry a written reason, plus its stale-exemption sibling. ⭐ The
  size counter itself did **not** gain a test — it gained `|Path` inside the regex it already had, which is
  why the ceiling moved 20 → 25 without a second mechanism appearing beside the first.
  ⚠⚠ **This line was internally inconsistent until 2026-08-08 — it declared 8304 while its own partitions
  summed to 8301** (the density block's numbers, left behind when the typography block moved the total). ⭐ The
  warning below is therefore not hypothetical: **re-measure BOTH numbers, and check that they add up.**
  ⚠ M4.1's +6 is the spacing ratchet: `Spacing`/`Padding`/`Margin` joined the existing `GuardedProperties`
  theory (3 properties × 2 tests), so no new mechanism was built beside the old one.
  ⚠ M4's +10 is all main-partition and all source-reading: four new guards over icon-size roles, the tree-row
  pair and the editable-grid row height. ⭐ Two of them are a **ceiling with a number** (now 20 literal icon
  sizes in 8 files, down from 95), so „how much of the sweep is left" stops being an opinion — the same
  mechanism the `FontSize` baseline uses.
  ⚠ The language sprint's jump (7378 → 8263) is mostly **not** hand-written assertions: 80 Language-Reference
  corpus entries joined `SqlTestCorpus.All`, which feeds the formatter theories (§0 round-trip, idempotency,
  casing) and the AST differential harness — ~11 runs per entry. Its QA round added the remaining 28
  (`DebuggerValueFormatTests` + the grid date-editor and cell-rendering guards).
  ⚠ The grid consistency sprint's +18 is all main-partition (`GridCopyTextTests` 14 + `DataGridCopyMenuTests` 3
  + one premise guard in `EditableGridSeamTests`): every one of them reads source or calls a pure static, and
  the behavioural half it replaced was a swap inside `EditableGridEnterTests`, which is why the headless
  partitions did not move.
  ⚠ The stabilization sprint's +43 split both ways on the same criterion: the source-reading guards
  (`EditableGridSeamTests`, `MetadataCacheInvalidationTests`) are main-partition, while the behavioural Enter
  tests construct a real `DataGrid` and therefore became the **eighth** headless class (see the list below —
  the filter grew by one name, deliberately, because there was nothing existing for it to join).
  ⚠ M3.5's +7 splits across BOTH partitions and the split follows one criterion: `CreateIconContractTests` (3)
  reads **source files** so it is main-partition; the four new `DesignTokenApplicationTests` cases construct
  Avalonia controls, so they went into a class **already inside** the headless filter — deliberately, to avoid
  growing that fragile list of names by one more entry.
  ⚠ This line said **7228 (7118 + 56 + 54)**, then **7271 (7154 + 63 + 54)**, then **7310 (7193 + 63 + 54)**,
  then **7317 (7196 + 67 + 54)** — i.e. **it has now drifted five times**. Re-measure; do not copy it forward.
  ⚠⚠ **A count kept in prose goes stale silently — this very line has been wrong twice.** Once because a
  partition filter named a class that no longer existed (so the total read one too high, `product-polish.md`
  §18.1.6), and once because the sub-stage's own numbers moved under it. **Re-measure before quoting it.**
  **⚠⚠ THIS LINE SAID 7088 / "54 + 34" AND THE ARITHMETIC WAS NEVER MEASURED — corrected 2026-08-02 (M2c
  iteration 1, `product-polish.md` §18.1.6), verified on a clean `HEAD`.** The cause is the next paragraph's
  own class list: it named **`ContextMenuPresentationTests`**, a class that **no longer exists** — the Keyboard
  Manager sprint's context-menu tests were folded into `ConnectionExpandBindingProbe` (which is where
  `TheSameMenuOperationAlwaysCarriesTheSameIcon` lives today), and the name survived in the filter. Excluding a
  name that matches nothing is harmless *as a filter*, which is exactly why nobody noticed the total was one
  too high. ⭐ The general shape is gotcha #284 one layer out: **a number kept in prose stays green while the
  thing it counts moves.**
  ⚠ Etap 6's +34 is mostly `SettingsConsumerWiringTests` — the etap's centre of gravity, because a stored value
  and a mapping are two lines each and what actually fails is **a consumer left on the shipped constant**.
  ⚠ Etap 5a's +176 is
  mostly one 126-case theory: the export round trip runs for **every combination of sections**, which is what
  the DoD asked for on a rule-#11 surface. ⚠ Etap 4's +762 is mostly theory rows:
  the shared SQL corpus is re-run under three non-default formatter styles, so a corpus addition now costs
  four times its own count. ⚠ The headless partition holds **ten** classes — measured, not listed from memory
  (`ConnectionExpandBindingProbe` + `SettingsCenterViewTests` + `BrandingPresentationTests` +
  `DesignTokenApplicationTests` + `TabStripPresentationTests` + `MetadataTreeVirtualizationProbe` +
  `SharedContextMenuFeasibilityProbe` + `EditableGridEnterTests` + `GridDateEditorTests` +
  `DependencyTreeRenderTests`), all in
  `HeadlessCollection` — a new headless test **joins that collection**, never adds its own `IClassFixture`
  (#94/#226/#286).
  ⛔⛔ **THE „ten classes" ABOVE AND THE HAND-TYPED FILTER BELOW ARE BOTH STALE — measured 2026-08-10 there are
  EIGHTEEN, and transcribing them is what makes this rot. DERIVE the list, never copy it:**
  ```bash
  grep -rln HeadlessCollection tests/EmberTern.Tests/*.cs | xargs -n1 basename | sed 's/\.cs$//'
  ```
  ⚠ Cost of ignoring this, paid on 2026-08-10: a run with the 12-name filter below reported **13 failures, none
  of them the session's change** — six headless classes ran inside the main partition and hit the documented
  latent race over the global `Loc` catalog. With the derived filter: 0. ⭐ Same shape as the stale COUNT this
  paragraph already warns about, one layer out — and note the direction it fails in: a *missing* exclusion is
  invisible (the class simply runs in the wrong partition), while a *surplus* one matches nothing and is
  harmless, so the list only ever rots toward false red. The lines below are kept as the record of what was
  transcribed, not as the filter to use.
  The partition filter is those class names excluded / included:
  `--filter "FullyQualifiedName!~ConnectionExpandBindingProbe&FullyQualifiedName!~SettingsCenterViewTests&FullyQualifiedName!~BrandingPresentationTests&FullyQualifiedName!~DesignTokenApplicationTests&FullyQualifiedName!~TabStripPresentationTests&FullyQualifiedName!~MetadataTreeVirtualizationProbe&FullyQualifiedName!~SharedContextMenuFeasibilityProbe&FullyQualifiedName!~EditableGridEnterTests&FullyQualifiedName!~GridDateEditorTests&FullyQualifiedName!~DependencyTreeRenderTests"`
  and its inverse with `|` — ⚠ **except that `BrandingPresentationTests` now belongs to the ISOLATED partition,
  so the grouped inverse must NOT include it** (grouped = the other eight; isolated =
  `ConnectionExpandBindingProbe|BrandingPresentationTests`).
  ⚠⚠ **`GridDateEditorTests` was MISSING from this filter until 2026-08-08, and that is the mirror image of the
  `ContextMenuPresentationTests` defect two paragraphs up.** There, an excluded name matched nothing — harmless
  as a filter, which is why nobody noticed. Here, a headless class was simply **absent from the exclusion**, so
  it ran inside the main partition, quietly creating a headless session there. ⭐ Both are the same failure:
  **the split is enforced by a hand-maintained list of names, and a list is exactly as stale-prone as a
  count.** Check the list against the classes that carry `[Collection(HeadlessCollection.Name)]`, not against
  memory.
  ⛔⛔ **AND THE ACCEPTANCE CRITERION IS THE TOTAL, NOT „0 FAILURES" — measured 2026-08-05.** With
  `--blame-hang`, a broken headless state reported **`Powodzenie!` — 0 niepowodzeń, łącznie 7232** while **128
  tests silently never started**; the same state without the flag gave 94 failures, and on a retry it hung.
  So a run is green only when it reports **`łącznie: 8304`** (or the partition's own 8158 / 91 / 55). A summary
  line saying „0 niepowodzeń" is satisfiable by a run in which a whole partition failed to load.
  ⚠⚠ **The filter is a LIST OF NAMES and goes stale silently** — an excluded name
  that matches nothing is harmless *as a filter*, which is exactly why nobody notices (§18.1.6). The
  criterion for adding a class: **does it construct Avalonia controls?**
  **⚠⚠ A THIRD, FINER SPLIT — USER DIRECTIVE, 2026-08-01: do NOT run `ConnectionExpandBindingProbe` together
  with the other headless classes; it hangs often enough that it is not worth it.** Run it **alone** and the
  rest together. ⭐ **CORRECTED 2026-08-05 (Avalonia 12.1.1 sprint): the isolated partition now holds TWO
  classes — `ConnectionExpandBindingProbe` + `BrandingPresentationTests` — so the split is 7250 + 73 + 55 (7232 at the time; the main partition has since grown).**
  `BrandingPresentationTests` moved there because it failed ~1 in 3 in the grouped run with *"The calling
  thread cannot access this object because a different thread owns it"* and is **green 6/6 alone**; it is the
  only headless test that opens a real platform `Window` (`Show()`), which is why it is the one that needed
  isolating. ⚠ Weakening its assertion was rejected — dropping `Show()` makes `Icon` read null, i.e. the test
  would pass while proving nothing. Detail + the two rejected attempts: the class doc and
  `docs/design/avalonia-12.1.1-update.md` §11. Both were clean that way on the same
  commit where a combined run had to be interrupted twice. ⚠ That "the other four" used to read four and then
  seven — the number moves with the class list above, so read the list, not this sentence.
  **⭐ A NEW DATUM ON THE CAUSE, and it is a better suspect than any assertion: a headless test that constructs
  a `MainWindow` is the hang-prone shape.** The first draft of `BrandingPresentationTests` built one to check
  the titlebar and hung; rewritten around a bare `new Window()` the same class runs in **476 ms**. Constructing
  `MainWindow` is exactly what the probe does. ⚠ The consequence for test design: **assert app-wide
  presentation against the cheapest control that can carry it** — the bare window is also the *stronger*
  assertion, since an icon reaching a window with no XAML and no code-behind can only have come from the
  application-level style. Still its own infrastructure task; **do not detour a sprint into it.**
  **⭐⭐ 2026-07-28, Keyboard Manager etap 5 — THE FOUR "SAME TEST" OBSERVATIONS BELOW WERE AN ARTEFACT OF
  ORDERING. READ THIS BEFORE TRUSTING THEM.** Etap 5 briefly had a SECOND headless test class, and running the
  partition in which *it* ran last moved the reported hang to **that class's** last test
  (`ContextMenuPresentationTests.TheSharedStyle_…`, at 5901 of 5902 completed) — the identical shape.
  So **the reported name tracks the POSITION — the last headless test in a long run — not the test.** Four
  consistent sightings of `CompletionRow_HighlightsMatchedPrefix` corroborated each other only because that
  test happened to be last in the only class that owned a session; they are not four independent witnesses.
  **The suspect is session teardown / dispatcher-loop shutdown**, and the investigation should start there
  rather than at that assertion. (Etap 5 also fixed a *different*, self-inflicted hang: the second class used
  `IClassFixture`, which creates one fixture PER CLASS and so produced a second `HeadlessUnitTestSession` —
  banned by #94/#226. The fixture is now an `ICollectionFixture`; gotcha #286.) Still its own infrastructure
  task; do not detour a sprint etap into it.
  **⚠ 2026-07-28, Keyboard Manager etap 2 — the hang REPRODUCED A THIRD TIME, and named the SAME test for
  the third time** *(see the reframing above — the name was positional)*. A single full-suite run hung;
  `--blame-hang` reported **5868 of 5869 tests
  `Completed="True"`** and the one that was not as
  **`ConnectionExpandBindingProbe.CompletionRow_HighlightsMatchedPrefix`** — again. **Three independent
  observations across three sprints now agree on the name**, and the count says it hangs on the *last* test
  of the run, which fits "after the work is done, not a failing assertion". Both partitions were green on the
  same commit immediately before and after. Not caused by this sprint (nothing here touches the completion
  probe or the headless session — the two tests it *did* add to that class both pass). Dump + sequence file
  under `tests/EmberTern.Tests/TestResults/`. **Still its own infrastructure task; do not detour a sprint
  etap into it.**
  **⚠ 2026-07-27, hardening sprint — the hang REPRODUCED A SECOND TIME, and named the SAME test.** Four
  consecutive full runs this session finished green in one pass; the fifth hung, and `--blame-hang` reported
  the only test not `Completed="True"` as **`ConnectionExpandBindingProbe.CompletionRow_HighlightsMatchedPrefix`**
  — the identical name the I9 session's instrument produced. That is now **two independent observations
  agreeing**, which is materially stronger evidence than either alone and the right starting point for whoever
  takes the infrastructure task. It is **not** caused by this sprint: nothing here touched the completion
  probe, the headless session, or the editor; the same commit ran green four times before it and green in both
  partitions after it. Dump + sequence file under `tests/EmberTern.Tests/TestResults/`.
  `ConnectionExpandBindingProbe` uses **one shared `HeadlessUnitTestSession`** — what gotcha #94 always
  prescribed, and **mandatory**, because AvaloniaEdit's static `KeyBinding` lists make any real key sent into
  a `TextEditor` throw cross-thread from every session after the first (#226).
  **⚠ 2026-07-27, I9 session — the hang REPRODUCED, and the instrument named a suspect.** A plain
  `dotnet test` hung; a first `--blame-hang` run went clean; a later one caught it and reported the test
  running at the moment of the hang as **`ConnectionExpandBindingProbe.CompletionRow_HighlightsMatchedPrefix`**
  (vstest prints its own caveat that this may or may not be the cause). Two facts worth carrying: the hang is
  **after** the tests finish, not a failing test — the aborted run had already reported *5677 passed, 0
  failed, 6 s* before it stopped exiting; and it is in the headless-Avalonia probe class, consistent with
  #94/#226/#261. **Nothing was changed on this evidence** (it is one observation and the session's task was
  I9), but a future investigation now has a named starting point and a dump under
  `tests/EmberTern.Tests/TestResults/`. Practical workflow meanwhile: run the two partitions, always with
  `--blame-hang`. **📋 Ratified by the user on closing I9: this is its OWN infrastructure task and is
  explicitly NOT to be picked up inside Data Import** — record findings here, never detour a module etap
  into it.
  **⚠ The intermittent full-suite hang the user keeps hitting is NOT claimed fixed.** It did not reproduce
  during the 2026-07-26 investigation (5568 green in ~9s, repeatedly, and the probe class alone in 6s), so
  nothing was restructured on a hypothesis. What that investigation *did* find and fix is a real defect with
  a plausible mechanism: the shared session was held in a `static readonly` field and **never disposed**,
  and Avalonia's own contract says *"Disposing unit test session stops internal dispatcher loop"* — i.e. it
  left a thread spinning a dispatcher loop after the last test. Ownership moved to an `IClassFixture`
  (`HeadlessSessionFixture`), still ONE session (gotcha #261). **When it next hangs, do not re-run and hope
  — run the instrument**, which turns an infinite wait into a two-minute named failure:
  ```bash
  dotnet test EmberTern.slnx --blame-hang --blame-hang-timeout 120s
  ```
  Smoke: clean (app launches).
- **Script Executor — Dev Mode integration DONE (2026-07-16; impl, awaits user visual confirmation).**
  The Script Executor no longer ignores Developer Mode. An **all-DDL script under auto-commit** begins
  its transaction with the Dev Mode-aware DDL wait policy (`FirebirdDdlExecutor.BuildDdlTransactionOptions`
  — reused, not duplicated) instead of the working transaction's NOWAIT default, so deploying objects
  other sessions are using waits rather than failing instantly. **Deliberately NOT changed:** one lane,
  one transaction, no per-statement commits, no routing by statement kind (#215 stands) — this is one
  TPB flag chosen at BEGIN. **Both conditions are load-bearing** (`FirebirdScriptExecutor.UsesDeveloperModeWaitPolicy`,
  pure + unit-pinned): *all-DDL* because a transaction's wait policy is fixed at BEGIN and cannot vary
  per statement, so it is the only thing guaranteeing no DML ever waits; *auto-commit* because Manual
  leaves the tx OPEN and `BeginTransactionAsync` early-returns on an active tx, so the SQL Editor's next
  F5 would **join** it and silently get a WAIT console (gotcha #230). `TransactionService.BeginTransactionAsync`
  gained an optional `FbTransactionOptions`; the console never passes it and is unchanged. Full analysis:
  [docs/design/script-executor-transaction-review.md](docs/design/script-executor-transaction-review.md).
- **✅ `FirebirdScriptExecutor` mixed-DDL+DML defect (#213) — FIXED by the Script Executor Rewrite (Steps
  0–6 COMPLETE, live-verified 2026-07-21).** The `Sequenced` ("Deployment") mode runs a mixed migration one
  committed segment at a time (commit after each schema statement, like isql `SET AUTODDL ON`), so a
  create-then-populate script runs end-to-end — proven live (12 scenarios ALL PASS). The **single-transaction**
  modes (`Manual` / `AutoCommit`) still cannot run a mixed DDL+DML script — that is a Firebird truth, not a bug
  (a transaction cannot use an object it created but has not committed) — and the App now **rejects such a
  script up-front** in those modes with a message naming `Sequenced` (Step 5 seam B). The old *"Commit or roll
  back the active transaction…"* guard framing and the "all-or-nothing" docstring are gone. The Core
  classification lives in the AST-based `SqlStatementClassifier` (the segment planner uses it; not the driver
  enum). See `docs/history/15-...` and
  [docs/design/script-executor-transaction-review.md](docs/design/script-executor-transaction-review.md).
- **Verify Firebird behaviour, never infer it.** Three long-standing architectural beliefs were
  falsified by ~30 lines of probe against the Lab DB this sprint (#213, #214, #215). If a design
  rests on "Firebird does/doesn't allow X", measure it first.
- **QA rule (2026-07-12, user directive):** a package is NOT "fixed" on green build/tests/smoke
  alone. If a fix can't be verified **visually in the running app**, report it as "implementation
  done — awaits user confirmation", never "fixed". Trace flows to ground truth, don't guess.
- **Parameter Helper — DONE + UNIFIED (gotchas #206–#210).** One `ParameterHelper` (App/Completion,
  OverlayLayer-hosted, source of truth = `SignatureHelpEngine`) shows the parameter list of whatever
  call/DML site the caret is at — **INSERT / UPDATE OR INSERT / EXECUTE PROCEDURE / function** — with
  the active parameter a solid accent pill and IN/OUT for routine params. Both triggers feed it: a
  **double-click** on a value (`NavigationController` → `SqlCompletionController.TryShowParameterHelperAt`)
  and **typing** an argument list (`(`/`,`/`)` / Ctrl+Shift+Space) — the old M7 `OverloadInsightWindow`
  is gone. Lifetime is **context-driven**, not offset-driven (#210): on each caret move it re-queries
  the engine and stays open while still the same site (kind+target), following the active argument,
  closing only on a real context change / Escape / detach. The journey (all fixed): wrong offset (caret
  vs pointer, #206), columns not warmed (#204), bare `Popup`/`PopupRoot` invisible on the desktop →
  OverlayLayer (#209). Engine: `SignatureHelpEngine` now treats `StatementKind.UpdateOrInsert` like
  `Insert`. All temporary `EditorDiagnostics` instrumentation removed (code clean). **The hover tooltip has
  since been migrated to OverlayLayer too** (the Unified Hover milestone — done up front, before plain hover
  made it the primary discovery surface, rather than after a bug report); `ClampIntoOverlay` now lives in
  `EditorPopups`, shared by both cards. The remaining custom popups (Ctrl+Space Quick Info, Peek, rename)
  still use the bare-Popup pattern — migrate to OverlayLayer if they show the same invisibility symptom.
- **Multi-statement root cause FOUND & FIXED (gotcha #208):** the user's real problem was that several
  statements in one editor **separated only by newlines (no `;`)** collapsed into ONE parser statement
  (`ScanPlain` ends only at a top-level `;`), so only the first was analysed (coloured/nav/Quick Info).
  Fix: a **lenient** parse for the READ-ONLY semantic model only — `SqlParser.Parse(text, lenient:true)`
  wired into `SemanticModel.Build(string)` — that also splits at top-level statement-start keywords with
  continuation guards (`WITH…SELECT`, `INSERT…SELECT`, `…UNION SELECT`, `CREATE VIEW…AS`, `MERGE…WHEN`).
  The strict `;`-only `Parse` (executor boundary authority, gotcha #192) is untouched. Pinned by
  `SemanticModelTests.MultipleStatements_WithoutSemicolons_*`. **User-confirmed fixed live** (all objects
  across every statement now colour/navigate).
- **UX Polish — QA Fix Sprint (2026-07-12).** (1) **Light-theme popup blend — fixed & verifiable:**
  style `aecc|CompletionListBox` (the earlier `aecc|CompletionList` Background was a no-op — that
  control's template never paints its Background). (2) **Double-click INSERT/VALUES helper —
  root-caused & fixed (awaits visual confirm):** the decision→popup flow is proven correct by
  `InsertHelper_DoubleClick_OpensPopup_*`; the live miss was the OFFSET — `OnDoubleTapped` used
  `_editor.CaretOffset` (not reliably on the clicked value when the gesture fires) instead of the
  POINTER offset (now `OffsetAt(e.GetPosition(...))`, gotcha #206). Also added warm-then-retry so
  the helper works when the target columns aren't cached. (3) **View / selectable-proc in FROM not
  coloured — PROVEN not the binder and not the highlighter (gotcha #207):** three probes show the
  binder resolves them given metadata, the highlighter paints the object colour when resolved
  (`SemanticHighlighter.PaintedBrushAt`), and `TextView.Redraw()` genuinely re-runs the colorizer.
  "Ctrl+Click works" is misleading — it has a name-based fallback, so the symptom set (no colour +
  no hover + no Quick Info, yet Ctrl+Click opens it) means the MODEL didn't resolve the object =
  metadata-not-in-snapshot-at-build-time (gotcha #205). Every link of the rebuild chain
  (`DataContextChanged`→`OnDataContextChanged`→`ObjectsChanged`→`NotifyMetadataChanged`→debounce→
  `RefreshModelWithMetadata`→repaint) is re-verified, but the live failure could NOT be reproduced
  headlessly → **awaits user confirmation**; if it persists on a clean rebuild, add runtime tracing
  of the snapshot object-count at model build.
- **Functional development is otherwise PAUSED.** Per explicit instruction: **do not start Etap 7
  or any new feature** until the user says so. (P8 formatter polish is now COMPLETE + architecturally
  closed — see the P8 bullet below.)
- **Package 5 (Quick Info richness) — DONE (2026-07-13).** `ColumnSpec` carries PK/FK/default/
  description/computed/identity; `ObjectMetadata` carries a function's return type + trigger/generator
  header facts; a new proactive warm pipeline (`EditorLanguageService.BeginWarmReferencedMetadata`)
  fills them for every object the current statement references, without requiring a "table." or a
  hover first. Full detail: `docs/design/editor-architecture.md` §15.2/§15.3, gotcha #211 (this
  also generalizes/supersedes the earlier per-character warm-then-retry hacks — there is now one
  metadata cache + one generic warm pipeline). Build 0/0, tests 3449/3449 green.
- **P8 DONE — Formatter Polish + architecturally closed (2026-07-13 → 2026-07-14).** Scope + order
  agreed with the user, all shipped:
  **Krok 0 Formatter Safety (§0) → F shared list builder → INSERT layout → UPDATE OR INSERT layout →
  long-line wrapping → EXECUTE BLOCK → FOR SELECT**, each its own commit with full build + tests +
  round-trip/idempotency. Standing directives for P8: never add a formatter workaround/special-case
  where a small parser/AST deepening is cleaner ("build grammar depth only when a concrete feature
  needs it"); after each step, remove any now-redundant historical workaround rather than leaving
  compatibility layers; report + justify architectural changes per step.
  - **Krok 0 (Formatter Safety) — DONE.** The formatter can no longer lose a token on malformed/
    incomplete input (§0 guarantee). Two layers: each PSQL emitter anti-stall guard now emits the
    unplaced token verbatim (`EmitStrayToken`) instead of silently skipping; and a checked invariant
    wraps `SqlFormatter.Format` — per statement, if the output's lexeme sequence ≠ the input tokens'
    it keeps the statement verbatim, and a script-level backstop returns the input unchanged if the
    whole result still differs. Also fixed a leading-comment drop before `CREATE PROCEDURE`. Detail:
    `docs/design/editor-architecture.md` §15.2, gotcha #212. Pinned by `SqlFormatterSafetyTests`.
    Build 0/0, 3542 main + 23 probe green.
  - **Krok F (shared list builder) — DONE.** ONE token-level mechanism (`SplitTopLevelCommas` +
    `MatchParen` + `FormatBrokenList`/`FormatAdaptiveList`, item content rendered by `Emit`) now lays
    out every "( item, item, … )" comma list. The CREATE VIEW column list — first consumer — was
    migrated onto it and its **bespoke ~40-line character loop was deleted** (net simplification).
    Byte-identical view output; the token-level splitter is comma-safe inside quoted identifiers for
    free. Pinned by `SqlFormatterListBuilderTests`. Build 0/0, 3548 main + 23 probe green.
  - **Krok INSERT (layout) — DONE.** `InsertStatement` now formats as IBExpert-standard: `insert into
    <target> (cols)` on one line, `values (…)` / `select …` / `default values` on its own line,
    `returning …` on its own, `;` glued. Column & value lists ride the shared **adaptive** builder —
    inline while they fit 120 chars, else packed multiple-per-line aligned under the opening paren
    (readability-driven, NOT one-per-line, per user directive). INSERT…SELECT reuses `Emit`.
    **Simplification:** the adaptive-reflow packer `PackWithContinuation` was generalized with a
    `startColumn` param and is now the ONE packing algorithm shared by the token-level list builder AND
    the string-level SELECT/IN wrapping. Pinned by `SqlFormatterInsertTests`. Build 0/0, 3557 main + 23
    probe green.
  - **Krok UPDATE OR INSERT (layout) — DONE.** `FormatInsert` generalized to `FormatInsertFamily(
    List<FToken>, headerLen)` handling BOTH `InsertStatement` (headerLen 2) and `UpdateOrInsertStatement`
    (headerLen 4) — they differ only by the leading verb and the `matching (…)` clause (its own line,
    via the shared adaptive builder). One formatter, two statement kinds. Pinned by the UPDATE OR INSERT
    cases in `SqlFormatterInsertTests`. Build 0/0, 3561 main + 23 probe green.
  - **PSQL leaf-statement unification — DONE (user-requested).** The PSQL body emitter no longer has its
    own INSERT/UPDATE/SELECT formatting: `AddPsqlEmit` now delegates each leaf statement to a shared
    `FormatLeafStatement`, which routes INSERT/UPDATE OR INSERT to the same `FormatInsertFamily` used at
    the top level (SELECT…INTO keeps its PSQL-specific INTO-on-own-line split; everything else → generic
    `Emit`). So an INSERT/UOI inside a procedure, trigger, or EXECUTE BLOCK now lays out identically to
    one at the top level — the divergence the user noticed is gone. The PSQL emitter owns only block
    STRUCTURE (BEGIN/END, IF/WHILE/FOR indentation); statements are formatted once. Pinned by the
    "inside body" cases in `SqlFormatterInsertTests`. Build 0/0, 3564 main + 23 probe green.
  - **Krok long-line wrapping — DONE.** There is now exactly ONE long-line wrapping mechanism, at the
    TOKEN level inside `Emit`: a SELECT column list (`EmitSelectColumnList`) and an `IN ( … )` value list
    (`EmitInList`) are laid out by the shared adaptive builders (`FormatAdaptiveBareList` /
    `FormatAdaptiveList`) using precise column positions from the StringBuilder. **The entire string-level
    post-pass is deleted** — `WrapLongLines`, `WrapLine`, `TryWrapSelectColumns`, `TryWrapInList`,
    `SplitByTopLevelComma`, `FindInOpeningParen`, `FindMatchingClose`, `SkipString`, `SkipQuotedIdent`,
    `LooksLikeSubquery` are all gone (~110 lines). Bonus: wrapping is now consistent inside PSQL bodies
    too (the old post-pass never wrapped indented SELECT lines). Byte-compatible with the old wrapping
    (all pinned SELECT/IN wrapping tests green). Pinned by `SqlFormatterWrappingTests`. Build 0/0, 3568
    main + 23 probe green.
  - **Krok EXECUTE BLOCK (header) — DONE.** `ExecuteBlockStatement` now formats its header instead of
    keeping it verbatim: `execute block (params)` (adaptive list) / `returns (cols)` on its own line
    (adaptive list) / `as` on its own line, all lowercased, then the block-structured body — because
    EXECUTE BLOCK is a *runnable* statement, not persistent DDL (a CREATE definition header stays
    verbatim by design). `FormatExecuteBlock` + `TryFormatExecuteBlockHeader` reuse the shared adaptive
    builder + `Emit` (item content) + `FormatPsqlBody`; any header shape not fully recognised falls back
    to the verbatim-header path (never guess, §0). Pinned by
    `SqlFormatterExecuteBlockAndForSelectTests`. Build 0/0, 3585 main + 23 probe green.
  - **Krok FOR SELECT — DONE.** The PSQL `FOR <select|execute statement> INTO <vars> DO <stmt>` loop was
    previously mangled (`for` split from `select`, `into …` glued onto the `where` line). `EmitForSelect`
    treats **FOR SELECT as one Firebird construct** (user directive — like INSERT INTO): `for` prefixes
    the cursor query's first line (NOT split onto its own line, query NOT extra-indented); the query is
    the shared `Emit` (so its SELECT/FROM/WHERE breaks + long-line wrapping match plain DML); then
    `into <vars>` and `do` each on their own line at the loop indent; body via `EmitPsqlBranch`. INTO and
    DO are found at paren depth 0 (a subquery in FROM never leaks out); malformed input (no top-level DO)
    falls back to the generic statement path (§0). WHILE stays on its own single-line path. Pinned by
    `SqlFormatterExecuteBlockAndForSelectTests`. Build 0/0, 3585 main + 23 probe green.
  - **Call-argument-list wrapping (UX follow-up) — DONE.** A call's argument list now rides the SAME
    shared adaptive builder as INSERT/VALUES/MATCHING/SELECT/IN. New `EmitCallArgList` in `Emit` fires on
    any `name ( … )` where `name` is an identifier/quoted-ident that is not a style keyword (the glue rule
    `NeedsSpaceBefore` already uses to detect a call) — so **EXECUTE PROCEDURE, function/procedure calls,
    and every other call** wrap adaptively under the `(` instead of sitting on one giant line; short lists
    stay byte-identical. No per-construct formatter (explicit user directive — EXECUTE PROCEDURE just
    routes through `Emit` like everything else). A subquery argument is left to the clause break. Pinned
    by `SqlFormatterCallArgumentTests`. Two documented edge limits (both idempotent + lossless): a
    single-item list can't pack (a lone very-long arg won't wrap), and a call nested as a list item wraps
    aligned from its own column-0 render, not its placed column. Build 0/0, 3596 main + 23 probe green.
  - **Final architecture close-out — P8 IS ARCHITECTURALLY CLOSED.** Audited on the user's request:
    (a) **no historical workarounds left** — the string-level wrap scanners are deleted (survive only in
    one explanatory comment), the CREATE VIEW char-loop is gone, all per-character/warm-then-retry hacks
    superseded; (b) **no parallel implementations** — ONE list builder (`SplitTopLevelCommas` + `MatchParen`
    + `FormatBrokenList`/`FormatAdaptiveList`/`FormatAdaptiveBareList`), ONE packing algorithm
    (`PackWithContinuation`), ONE item renderer (`RenderListItems`→`Emit`), ONE long-line wrapping
    mechanism (token-level), and statements formatted in ONE place (top-level == PSQL body via
    `FormatLeafStatement`); (c) every private method is live (no dead code), no transitional names
    (`V2`/`New*`/`Temp`); (d) the residual verbatim paths (CREATE definition headers, UPDATE SET
    per-assignment, MERGE, CASE/expression interior) are **intentional scope boundaries** — grammar depth
    not yet built because no feature needs it — not debt. §0 is a checked invariant (per-statement +
    per-script lexeme preservation), so the formatter either reproduces every lexeme or leaves the
    fragment/document unchanged.
- **What's next — DECIDED (pre-Stage-7 architecture review, 2026-07-14).** A review before Stage 7
  found the AST is a *statement skeleton with token-bag annotations*, and SQL structure is duplicated
  across 3–4 token walkers (formatter ~24 routines, the binder's Query+Psql walks, the legacy
  `SqlAliasResolver`). Decision (user): **build a foundational parser/AST deepening — [Etap 6.9 —
  Structural AST Deepening](docs/design/editor-ast-deepening.md) — BEFORE Stage 7**, at "structural
  depth" (model clauses, subqueries, CTE/nested-CTE, CASE, PSQL control-flow + executable statements;
  keep ordinary expressions as token fragments), foundation-first, migrating the binder first and the
  formatter **one construct at a time** (never a big-bang rewrite; every milestone must strictly
  reduce token-walk logic). This is also the foundation for the future Debugger (every executable
  statement gets a stable node + span). Milestones B0–B5 + progress matrix are in that doc. **Stage 7
  (Diagnostics) follows** and is fully specced in [editor-stage7-diagnostics.md](docs/design/editor-stage7-diagnostics.md)
  (semantic-only engine, `Diagnostic` model, categories, squiggles/panel/nav, incremental refresh,
  Quick Fixes explicitly post-Stage-7). **Etap 6.9 / B0 — DONE (2026-07-14):** pure-refactoring
  scaffolding — new base abstractions `QueryNode` / `PsqlStatement` / `IExecutableStatement`,
  `SqlParser` made `partial` (B1/B2 seam), a §0 differential-test harness (round-trip byte-identity +
  tree well-formedness over a shared corpus), the NUL-byte fix in `SemanticBinder.Query.cs`, and the
  dead alias path removed from `EditorLanguageService`; build 0/0, 3841 main + 23 probe green, smoke
  clean, no formatter/semantic behaviour changed. **Etap 6.9 / B1a — DONE (2026-07-14):** the PSQL body
  node hierarchy (`Ast/PsqlNodes.cs`: `BlockStatement`/`IfStatement`/`WhileStatement`/`ForSelectStatement`/
  `PsqlLeafStatement`, control-flow + leaves implement `IExecutableStatement` = debugger step units) + a
  body sub-parser (`SqlParser.Psql.cs`) that parses an `AnonymousBlockStatement` (the body-only editor
  shape) into a `Body` tree — **additive only** (binder + formatter unchanged; token slice still
  round-trips; spans nest + no token dropped by construction; mirrors the formatter's `EmitPsqlUnit`);
  build 0/0, 3850 main + 23 probe green (+9 `PsqlAstTests`), smoke clean. **Etap 6.9 / B1b-prep — DONE
  (2026-07-14):** reading the binder for B1b showed it walks FOUR PSQL surfaces (CREATE PROC/FUNC, CREATE
  TRIGGER, EXECUTE BLOCK, anon block) + a DECLARE section, so retiring the walker COMPLETELY needs the AST
  to cover them all first. Added (still additive) `ParseRoutineBody` (skip header to top-level `AS`, parse
  declares+block) attaching a `Body` `BlockStatement` to `DdlStatement` (PSQL proc/func/trigger) +
  `ExecuteBlockStatement`, and `DeclareVariable/CursorStatement` nodes + `BlockStatement.Declarations`
  (now exercised); binder + formatter still token-walk (coexistence); build 0/0, 3857 main + 23 probe
  green (+7 `PsqlAstTests`), smoke clean. **Etap 6.9 / B1b — DONE (2026-07-15):** `SemanticBinder.Psql`
  is now a pure **AST consumer** — a visitor (`BindBody`→`BindBlock`→`BindPsqlStatement`, with
  `BindControlHeader`/`BindDeclaration`) traverses the parser's `BlockStatement` body tree, and the
  **complete structural PSQL token walker is DELETED** (`BindRoutineBody`, `ScanDeclarations`,
  `FirstTopLevelBegin`, `FindTopLevelSemicolon`, `ContainsKeyword`, `SkipLocalSubprogram`,
  `MatchingEndExclusive` — ~113 lines of BEGIN/END matching + boundary/subprogram scanning gone). The
  entry points bind only the HEADER (signature) from tokens; the old flat body scan is retained as the
  leaf-INTERIOR reference binder (`BindLeafReferences`, ordinary/query-expression depth = B2/B3) and now
  runs per node-range — identical reference set (every body token is in exactly one node). Behaviour
  delta (documented, negligible): a local `DECLARE PROCEDURE/FUNCTION` body is now traversed against the
  enclosing scope (the old walker skipped it) — rare FB4+ surface, proper sub-routine scoping is B5+.
  Build 0/0, 3864 main + 23 probe green (+3 `SemanticModelTests`), smoke clean; completion/highlighting/
  navigation/Quick Info consume the same model API, unchanged. **Etap 6.9 / B2 — parser-producer DONE
  (2026-07-15):** the **query clause tree** is now produced. New `Ast/QueryNodes.cs` (`SelectQuery` /
  `SetOperationQuery` : `QueryNode`; the `QueryClause` base + `SelectClause`/`FromClause`/`WhereClause`/
  `GroupByClause`/`HavingClause`/`OrderByClause`; the `FromItem` base + `TableReference`/`DerivedTable`/
  `JoinedTable`; `SetOperator`+`JoinKind` enums) + new sub-parser `SqlParser.Query.cs` (`TryParseSelectQuery`
  → clause-boundary scan + comma/JOIN-structured FROM list + left-assoc set operations with a trailing
  ORDER BY on the whole). Wired into `Classify` so a plain (non-`WITH`) `SelectStatement` exposes a `Query`
  child. **Additive only** — binder + formatter UNCHANGED (still token-walk; transitional coexistence),
  token slice still round-trips (§0); shapes not cleanly recognised leave `Query` null (never lost). Depth
  = structural: clause/join interiors stay token fragments; nested subqueries (derived body, EXISTS/scalar,
  CTE body) NOT recursed — that's B3; `WITH`-led queries keep the `WithClause` token bag (main query →
  `QueryNode` in B3, so no double representation). **Dedup:** `PsqlSpan`→shared `TokenSpan` in `SqlParser.cs`
  (one token-range→span helper for both sub-parsers); reuses existing `Sub`/`MatchParenTok`/`Kw`/`At`.
  Build 0/0, 3896 main + 23 probe green (+14 `QueryAstTests`, +5 corpus shapes), smoke clean. **Etap 6.9 /
  B3 — parser-producer DONE (2026-07-15):** the **query model is now fully recursive**. New nodes
  (`Ast/QueryNodes.cs`): `WithQuery` (WithClause CTE-decls + main `QueryNode`), `RawQuery` (query-level §0
  valve), `SubqueryExpression` base + `ExistsExpression`/`ScalarSubquery` (each owning a `QueryNode`).
  Promoted (`Ast/CteNodes.cs`): `CommonTableExpression.BodyTokens`→`Body` (real `QueryNode`); `WithClause`
  dropped `MainQueryTokens` (main now on `WithQuery.Query`) — **no parallel representation**. `QueryNode`
  base gained `Tokens` (pulled up from `SelectQuery`/`SetOperationQuery` — dedup). `SelectStatement.With`
  **deleted** — a WITH-led statement's `Query` is a `WithQuery` (one representation everywhere). Parser:
  `ParseQueryRange` is the single recursive entry (reused by CTE bodies, derived tables, `ParseEmbeddedSubqueries`
  which finds EXISTS/scalar/IN subqueries in clause interiors, descending ordinary parens but never into a
  subquery); clauses/derived-tables/JOIN-ON now carry their subquery children; `TryParseWithClause` deleted
  (WITH parsing consolidated into `SqlParser.Query.cs`). **Formatter:** ONE forced byte-identical accessor
  swap — `FormatWithClause` reads `cte.Body.Tokens`/`wq.Query.Tokens`, dispatcher matches
  `SelectStatement { Query: WithQuery }`; emits the exact same token ranges → output unchanged (proven by
  formatter invariants + idempotency + the per-statement lexeme net). Not a layout migration — the only way
  to promote the WITH token-bag without a parallel representation. Build 0/0, 3913 main + 23 probe green,
  smoke clean. **Etap 6.9 / B3.1 — parser-producer DONE (2026-07-15):** the last "query as a token blob"
  gap is closed — the parser is now the single structural source for **every query reachable from a
  top-level statement or a PSQL control-flow node**. New sub-parser `SqlParser.Dml.cs` attaches a real
  `QueryNode` to: `InsertStatement.SourceQuery` (INSERT…SELECT/WITH) + `.Subqueries` (VALUES/RETURNING
  scalar subqueries); `UpdateStatement`/`UpdateOrInsertStatement`/`DeleteStatement.Subqueries` (embedded
  EXISTS/scalar/IN); `MergeStatement.SourceQuery` (USING (…)) + `.Subqueries` (ON/WHEN/SET/VALUES);
  `DdlStatement.Query` (CREATE/ALTER/RECREATE VIEW…AS body, incl. WITH-led + set-op bodies, mutually
  exclusive with the PSQL `Body`); PSQL `ForSelectStatement.Query` (FOR SELECT/WITH cursor — boundary stops
  at depth-0 INTO / AS CURSOR, never a column-alias AS; null for FOR EXECUTE STATEMENT); PSQL
  `DeclareCursorStatement.Query` (DECLARE…CURSOR FOR (…)). **Additive/producer-only** — binder + formatter
  still token-walk these (convergence deferred, same as B2/B3); every embedded query is modelled ONCE as a
  `QueryNode` (no parallel representation — the statement `Tokens` are the §0 backing, not a second model);
  shared child-ordering in new `Ast/AstChildren.cs`. Also a **B2 robustness fix** (forced by a set-op VIEW
  body): `ParseSetQuery` no longer folds a dangling `… UNION ALL` (lenient-split mid-statement) into a
  degenerate `[0,0)` operand. Build 0/0, **3978 main + 23 probe green** (+`DmlQueryAstTests`, +14 corpus
  shapes), smoke clean; no formatter/semantic behaviour changed. **ONE documented residual (→ B5, §12):** a
  DML/`SELECT…INTO` statement appearing as a PSQL body LEAF stays a `PsqlLeafStatement` (its query token-only)
  — modelling it now would create a parallel DML-query representation; the fix is B5 (leaves → reused DML
  nodes). `EXECUTE STATEMENT '<sql>'` is never a `QueryNode` (runtime string — a permanent boundary, not
  debt).
- **Etap 6.9 / B4 (CASE AST) — parser-producer DONE (2026-07-15):** `CASE … END` (simple + searched, in a
  SELECT expression and in PSQL) is now a `CaseExpression` (+ `WhenClause`) node — `Ast/ExpressionNodes.cs`.
  The B3 clause-interior scan was generalised from `ParseEmbeddedSubqueries` to `ParseEmbeddedExpressions`
  (finds subqueries AND CASE, recursively — a subquery/nested CASE inside a WHEN/THEN/ELSE stays a real
  node); `PsqlLeafStatement` carries the same embedded-expression children so a CASE in an assignment/RETURN
  is modelled too. Additive — the formatter still emits CASE inline (layout is deferred convergence).
- **Etap 6.9 / B5 (Routine/PSQL body = reused DSQL nodes) — parser-producer DONE (2026-07-15):** an embedded
  DSQL statement inside a PSQL body (SELECT/INSERT/UPDATE/DELETE/MERGE/EXECUTE) is now the **reused**
  top-level statement node (with its B2/B3/B3.1 query structure), NOT a `PsqlLeafStatement` — so a DML query
  in a routine body is the SAME node, modelled the SAME way, as at the top level (closes the §12 #1
  residual). Body statement/branch slots widened `PsqlStatement`→`SqlNode`; PSQL-only leaves (assignment,
  SUSPEND, EXIT, LEAVE, POST_EVENT, EXCEPTION, RETURN, subprogram header) stay `PsqlLeafStatement`; the
  reused nodes now implement `IExecutableStatement` (debugger step coverage across every PSQL surface).
  `PsqlLeafKind` dropped its DSQL members. **Binder behaviour-neutral** (a reused node is bound via the same
  `BindLeafReferences` over its tokens as the old leaf scan); **formatter unaffected** (token-based PSQL
  emitter). Build 0/0, **4008 main + 23 probe green**, smoke clean; no formatter/semantic behaviour changed.
  **PARSER STAGE COMPLETE (B0–B5): the parser is the single structural source for all SQL/PSQL structure.
  No parallel AST representation remains.**
- **Etap 6.9 / BINDER CONVERGENCE — DONE (2026-07-15):** `SemanticBinder` is now a full AST consumer. The
  query binder (`SemanticBinder.Query`) reads the `QueryNode` tree (FROM items, WITH/CTE, embedded
  subqueries, clauses); the DML binder (`SemanticBinder.Dml`) reads the DML nodes' source query + subqueries;
  the PSQL binder drives its leaf/header subqueries from the AST (reused-node `Query`, leaf children,
  `IF`/`WHILE` `ConditionExpressions`, `ForSelectStatement.Query`). **Structural token walkers DELETED:**
  `BindQuery` (token), `CollectTables`, `ParseTableList`, `ParseCteList`, `BindColumnReferences`
  (FROM+`(SELECT` re-scan), `BindNamedTable`/`BindDerivedTable`/`BindTargetAfter`/`ReadOptionalAlias`,
  `IsTableListTerminator`/`TableListTerminators`, `BeginsSubquery`, and the PSQL `BindLeafReferences`/
  `FindBodySelectEnd`/`BindOptionalInto`. Only expression-level token work remains (column/local/param refs +
  DML-target identification, which has no AST node) + two producer refinements (`IF`/`WHILE`
  `ConditionExpressions`; PSQL `SELECT … INTO` ends its `QueryNode` before `INTO`). Behaviour-equivalent:
  build 0/0, **4008 main + 23 probe green**, smoke clean.
- **Etap 6.9 / FORMATTER CONVERGENCE — DONE (2026-07-15) → ETAP 6.9 IS CLOSED.** `SqlFormatter` is now an
  AST-walking layout engine wherever the parser provides structure. Landed construct-by-construct (never
  big-bang): **F1** nested-query indentation (derived table / EXISTS / scalar subquery / IN(SELECT) as
  expanded-paren blocks) + the projection item model (a CASE/subquery item owns its layout without forcing
  neighbours one-per-line); **F2** adaptive CASE (`CaseExpression` → inline when ≤1 WHEN and it fits, else a
  WHEN/THEN/ELSE block); **F3** WITH/CTE bodies recurse through `EmitQuery`; **F4** INSERT…SELECT source,
  CREATE VIEW body, MERGE `USING (…)` source, UPDATE/DELETE embedded subqueries; **F5** PSQL body leaves +
  FOR SELECT cursors delegate to the AST (a leaf-span index bridges the token block-structurer to
  `FormatAstLeaf`). Core mechanism: everything renders **column-0-relative** and composes by uniformly
  shifting a block right (`AppendBlock`/`IndentBlock`), so a **flat query stays byte-identical** (all
  pre-existing exact tests unchanged) while a nested query gains real indentation; layout is a pure function
  of the tree ⇒ idempotent; the §0 lexeme net is unchanged. New tests: `SqlFormatterNestedQueryTests`,
  `SqlFormatterEmbeddedQueryTests`, `SqlFormatterPsqlAstTests` + a `StructuralConstructs` §0/idempotency
  sweep. **The token emitter is retained by design, not as debt:** it is the clause/expression INTERIOR
  renderer (structural-depth boundary — ordinary expressions stay token fragments), the layout for the
  constructs the parser intentionally does NOT model (UPDATE SET / DELETE / MERGE clause layout — a §12
  boundary; PACKAGE bodies — no `Body` node), and the robust PSQL block structurer (malformed-input safe).
  **One layout mechanism per construct — no parallel AST + token walker for the same construct.** The
  reported formatting problems are fixed: **CASE** lays out (adaptive), **WITH** and **multi-level nested
  queries** indent naturally. **Three follow-up fixes closed reported gaps** (all pinned by tests): (a)
  subqueries in function-call args / CASE arms / any derived table now nest at exactly +1 (not the
  enclosing paren's column) — the shared list builders thread structural children, and `EmitFromClause`
  goes structural for ANY derived table; (b) a **bare `IF`/`WHILE`/`FOR` fragment** (no enclosing
  BEGIN…END — a selection lifted from a body) is recognised as an anonymous PSQL body (`Classify`) so it
  formats instead of falling to a verbatim `RawStatement`; (c) a PSQL **`SELECT…INTO` leaf's leading
  comment is no longer duplicated** (the AST leaf renderer re-materialised the comment the block
  structurer already emitted → the duplicate tripped the §0 net and reverted the WHOLE routine to verbatim
  — the "the whole procedure didn't format" symptom). Build 0/0, **4070 main + 23 probe green**, smoke
  clean; user-confirmed on a real procedure. **ETAP 6.9 IS COMPLETE — parser + binder + formatter all
  consume one AST model.** **Stage 7 (Diagnostics) has since begun — see the Stage 7 bullet below.**
  `SqlAliasResolver` is off the editor path (only `PredicateExtractor`/Performance uses it).
  Still deferred: **P5d** a plain-hover info cue — now **folded into the post-Stage-7 "Unified Hover
  Information" backlog item** (do NOT ship P5d separately: it builds the same plain-hover surface, dwell
  delay and noise budget the unified hover needs — see `editor-stage7-diagnostics.md` §15). **P2c is DONE**
  (2026-07-17) — see the Completion Matching bullet. Formatter grammar-depth items now folded into Etap 6.9 as node
  consumers: **CASE** (was inline/verbatim), **nested-query indentation** (no indent model today),
  and eventually **UPDATE SET** / **MERGE … WHEN** if a feature needs them; CREATE-definition headers
  stay verbatim by design. Immediate hygiene noted for Etap 6.9: a literal NUL byte in
  `SemanticBinder.Query.cs` (composite cache key written as a raw `\0`), and the dead alias path in
  `EditorLanguageService` (no consumer since Etap 5/M5 — remove once validated).
- **Stage 7 (Diagnostics) — COMPLETE** (S5 impl done 2026-07-16; awaits the user's visual confirmation per
  the QA rule). Design/vision + as-built: [editor-stage7-diagnostics.md](docs/design/editor-stage7-diagnostics.md).
  **Core engine (S1+S2+S6) — DONE (commit c3a269d):** `DiagnosticsEngine` is a pure-Core client of
  `SemanticModel` — conservative, deterministic, de-duplicated diagnostics `ET0001`–`ET0008`
  (UnknownObject/UnknownColumn/UnresolvedVariable/UnresolvedParameter/AmbiguousColumn/InsertCountMismatch/
  UnknownCursor/SuspendOutsideSelectable) in one forward pass over `References` + bounded AST checks; zero
  Avalonia, "prefer silence over false positives" throughout (object/column categories gated on live
  metadata). **S3 (Squiggle rendering) — DONE (impl); awaits user visual confirmation.** New App renderer
  `SquiggleRenderer` (`IBackgroundRenderer`, `Completion/SquiggleRenderer.cs`) draws a wavy underline under
  each diagnostic span (Error→`ErrorBrush`, Warning→`WarningBrush`, Info→`SubtleForegroundBrush`; both
  themes, no hardcoded colours), mirroring `SemanticHighlighter`/`OccurrenceHighlighter`. Wired once in
  `SqlEditorBehavior.Attach` → every SQL surface. Diagnostics are computed on the **existing** model
  background pass: `EditorLanguageService` now runs `DiagnosticsEngine.Analyze` inside the same cancellable
  `Task.Run` that builds the model (and on the two synchronous rebuild paths), caches an `IReadOnlyList<Diagnostic>`
  version-matched to the model, and exposes it via `SqlCompletionController.Diagnostics`. No second parse
  loop, no parallel analyses (a newer edit cancels the in-flight one via the existing CTS); the paint path
  reads the cached list only (viewport-culled + doc-clamped, so large scripts + post-edit staleness are safe).
  **Hover/tooltip is NOT part of S3** (user scope decision — squiggles only; the message surface is a later
  milestone). **S3 follow-up — Easy-mode ambient refresh (DONE, impl):** an Easy-mode routine editor's body
  holds only the fragment; its params/`DECLARE`d variables live in the grids and reach the model as *ambient
  symbols* (gotcha #218). A manual-QA pass found the model did NOT rebuild when those grids changed, so
  squiggles (and completion/highlighting) went stale until the next body-text edit. Fixed: the routine VMs
  raise `SourceObjectDetailTabViewModel.AmbientSymbolsChanged` on a grid add/remove/reorder or row **rename**
  (base tracks Variables; Procedure adds Input/Output params, Function adds Arguments — via a `TrackAmbient`
  mirror of `TrackDirty`, scoped to the `Name` property); the detail views bridge it (`AmbientModelRefresh`)
  to each ambient-seeded editor's new `SqlCompletionController.NotifyAmbientSymbolsChanged()` → the existing
  debounced `RefreshModelWithMetadata` rebuild (re-captures ambient). **Root-cause note (investigation):** the
  binder/engine/ambient mechanism are all correct — fed complete ambient symbols the model has zero false
  positives; the only gap was this staleness. Analysis stays on the *visible fragment + ambient*, NOT a
  synthesized full CREATE source (avoids offset translation; consistent with every other model consumer).
  **S3 is COMPLETE + committed (c8266e3), plus a defect fix found while preparing S4 (f397190): the main
  SQL Editor rendered NO squiggles.** S3 attached the renderer in `SqlEditorBehavior.Attach` believing that
  seam covered "every SQL surface" — it does not: that installer serves the **object editors**, while
  `MainWindow` hand-wires the main editor itself (null-safe `_currentVm?.…` callbacks). Its diagnostics were
  computed all along; only the paint was missing. Fixed minimally (attach the renderer in `MainWindow`
  too + correct the false comments); consolidating the duplicated wiring is the real fix and is owed **its
  own refactoring milestone**, deliberately kept out of Stage 7 (user decision). **⚠ Until then, a new
  editor capability must be attached in BOTH places** (gotcha #219).
  **S4 (Diagnostics panel) — DONE (impl); awaits user visual confirmation.** Scope was deliberately narrow
  (user directive): **list only** — no navigation/next-prev (S5), no Quick Fix, light bulb, hover, code
  actions, filtering or grouping. Hosted on **every** SQL editing surface (scope widened during manual QA —
  the object editors had squiggles since S3 but no way to browse them): a fifth `bottom-tab` in the SQL
  Editor (Results/Messages/Output/Performance/**Diagnostics**, gated on `IsQueryTabActive`), and a **peer
  top-level tab in the Procedure / Function / Trigger / View / Package editors**, hosted exactly the way
  `PerformancePanelView` already is there (same view + VM type, one panel VM per host, no shared state).
  **Script Executor deliberately deferred** (no tab strip → its own UX decision). The tab is appended **last**
  everywhere because `SelectedBottomTabIndex`/`ActiveSubTabIndex` are persisted and `PerformanceBottomTabIndex
  = 3` / `PerformanceSubTabIndex = 5` / `SqlSubTabIndex` / `PackageSubTabIndex` are hard-coded. Editor layouts
  were **not** redesigned (no panel-below-editor, no extra splitters — user decision).
  **DESIGN DECISION — the panel reflects the ACTIVE SQL document only, never a merge** (`LastFocusedSqlDocument`,
  design §8.2.1): the last SQL editor to take focus, else the mode's primary (body in Easy / full source in
  Source). Focus change retargets + republishes with no text edit; a mode flip or object rebind clears the
  sticky and falls back. It deliberately does **not** reuse the views' `ActiveEditor` — its
  `IsEffectivelyVisible` guard can never hold while a peer Diagnostics tab is on screen, so Cursors/Subprograms
  findings could never reach the panel (gotcha #220). A workspace-wide list, if ever wanted, is a SEPARATE
  feature and must not change this panel's meaning. New: `DiagnosticsPanelHost` (App/Completion — pure wiring
  over the UNCHANGED binder: one binder per editor, gated via the binder's existing lazy panel resolver),
  `DiagnosticRowViewModel` + `DiagnosticsPanelViewModel` (App/ViewModels),
  `DiagnosticsPanelBinder` (App/Completion — the view-layer bridge, beside `AmbientModelRefresh`, because
  offset→line/column needs the AvaloniaEdit document), `DiagnosticsPanelView` (App/Views — a *virtualizing*
  `ListBox`, not the Messages panel's `ItemsControl`-in-`ScrollViewer`, so a huge script's findings don't all
  realize). The panel is **only a view**: it analyses nothing, sorts nothing, filters nothing and shows the
  engine's findings in the engine's order. It rides the existing `ModelUpdated` cycle and reads the
  **cached** version-matched list — no parse, no model rebuild, no second analysis — so every refresh
  trigger (text edit, model rebuild, metadata bump, Easy-mode ambient change) is satisfied by that ONE
  subscription. Severity→brush mapping is identical to the squiggle renderer's, so a row and its underline
  always agree. VM property is `DiagnosticsPanel`, not `Diagnostics` — that name already resolves to the
  `EmberTern.App.Diagnostics` namespace inside `MainWindowViewModel`; it lives on `SourceObjectDetailTabViewModel`
  (covering Procedure/Function/Trigger at once), `ViewDetailTabViewModel` and `PackageDetailTabViewModel`,
  mirroring `Performance`. Build 0/0, **4136 main + 23 probe green** (+14 `DiagnosticsPanelVmTests`), smoke
  clean. **S4 is user-confirmed + committed (1d078c6).**
  **S5 (Navigation) — DONE (impl); awaits user visual confirmation. CLOSES STAGE 7.** `F8` / `Shift+F8` =
  next / previous (wrapping, silently; a clean document is a no-op), panel row activation on **double-click
  or Enter** (single-click only selects), caret + span selection + scroll + focus, and the object editors'
  two-target routing. A **pure consumer**: it navigates the panel's already-published rows — no parse, no
  model rebuild, no re-analysis. **The one architectural decision: everything routes through the ONE target**
  — `DiagnosticsPanelHost.ActiveDocument` (the `LastFocusedSqlDocument` rule) was made public, and navigation
  lives on the host because that is the class that knows it; so a row and its jump cannot disagree *by
  construction*. Navigation also scans the panel's **own** order (= the engine's, `Finalize` sorts by Start/
  Length/Code) rather than sorting again — reusing the one order is what makes "panel and navigation always
  agree" structural instead of coincidental; **pinned by a test against the real engine**. **The SQL Editor
  was migrated off its bare `DiagnosticsPanelBinder` onto the same host** — behaviour-identical (one editor ⇒
  the rule collapses onto it), but it removes the second targeting path AND hands that editor `F8` for free:
  `F8` is wired **once**, in `Track`, so no surface can be missed (**gotcha #219 dissolved by construction**,
  not by remembering two places). Script Executor has no panel ⇒ no host ⇒ no `F8` (consistent, S4's
  deferral). Object-editor reveal is a per-surface `Action<TextEditor>` handed the host's active document (it
  never re-derives a target): Procedure/Function → Editor tab **+** `Cursors/SubprogramsEasyIndex` when the
  target is those editors; Trigger/View → Editor/SQL tab; Package → the editor IS the tab; SQL Editor →
  nothing, except un-maximizing a results-maximized layout via the existing `ToggleResultsMaximized()`.
  Jump semantics mirror go-to-definition (`NavigationController.JumpTo`). **Near-miss worth knowing (gotcha
  #221):** the established "post the whole caret+scroll+focus block at `DispatcherPriority.Background`" idiom
  (Package member nav) would have made a held `F8` re-select the same diagnostic forever — `Input` outranks
  `Background`, so the next keypress reads the pre-jump caret; caret+selection are therefore set
  **synchronously** and only scroll+focus are posted. Build 0/0, **4148 main + 23 probe green** (+12
  `DiagnosticsPanelVmTests`), smoke clean.
- **Unified Hover Information — DONE (2026-07-16; impl, awaits user visual confirmation).** The first
  post-Stage-7 milestone. ONE hover surface instead of independent Quick Info / diagnostics tooltips:
  **plain hover** (no Ctrl, 350 ms dwell) shows the diagnostic on a squiggled span, today's Quick Info on a
  symbol, and both as *sections* of a single card when a span has both (**diagnostics first**). Pure
  presentation over the existing `SemanticModel` + the **cached** `DiagnosticsEngine` list — and
  "no new analysis" is **enforced by the signature**: `HoverInfoEngine.GetHover(model, diagnostics, offset)`
  takes the diagnostics as an INPUT, so it *cannot* re-analyse. **Absorbs + closes P5d.** New: Core
  `Sql/Language/Hover/` (`HoverInfo` = ordered aggregate `Span`/`Diagnostics`/`Info`, **no `IHoverProvider`**
  — rule #2; `HoverInfoEngine`), App `HoverInfoView` (composes the EXISTING `QuickInfoView.BuildContent` +
  shared `QuickInfoView.Card` chrome, so the unified hover and the standalone Ctrl+Space Quick Info cannot
  drift apart). **INTERACTION DECISION (user delegated) — plain hover = information, Ctrl = actionability;
  this CONFIRMS the frozen §9.4, it does not amend it.** The deciding fact is technical: the old tooltip was
  gated on `NavigationEngine.TargetAt` returning a *navigable target*, so Ctrl+hover showed **nothing exactly
  where `ET0001` fires** (an unknown object is unresolved ⇒ no target). Ctrl also *means* "this leads
  somewhere" — and an unknown object leads nowhere, so overloading it would make the affordance lie.
  `NavigationController` now has two independent cues (`UpdateNavigationAffordance` = Ctrl → underline +
  hand cursor; `UpdateHoverInfo` = plain → the card) sharing only the pointer position; Ctrl+hover shows the
  card too (superset) and does NOT dismiss one you are reading. **Gotcha #209 closed:** `_tooltip`'s bare
  `Popup` is **deleted** — the card is `OverlayLayer`-hosted, and `ClampIntoOverlay` was extracted from
  `ParameterHelper` into `EditorPopups` (one implementation, two consumers). Noise control: dwell + card
  stays put while the pointer is inside `HoverInfo.Span` (narrowest section, so no flicker) + never opens
  while the completion list / Parameter Helper / Quick Info is up (`SqlCompletionController.IsPopupOpen` —
  that controller already owned the "don't stack" rule) + never steals focus / never hit-testable +
  dismissed by any click / text edit / pointer exit. **Gotcha #219 did NOT bite** — this adds no new
  `Attach`; the new `Attach` params are **required**, so a missed seam is a compile error (both were).
  Build 0/0, **4159 main + 23 probe green** (+11 `HoverInfoEngineTests`), smoke clean.
- **Stage 8 (Smart Editing & Structural Assistance) — STARTED.** New milestone: *the editor helps you
  write code but never writes it for you without your explicit decision* (modern-IDE, not IBExpert).
  Charter: **M1 Structural Matching**, M2 Smart Snippets, M3 Snippet Engine, M4 Structural Selection
  (future) — one at a time. Design/as-built:
  [docs/history/16-stage8-smart-editing.md](docs/history/16-stage8-smart-editing.md).
  **M1 — Structural Matching — DONE + visually confirmed + finalized (2026-07-16). CLOSED.** The editor's two
  fragmented "related-elements" highlighters (the former text-based occurrence highlighter, and
  `NavigationController`'s semantic caret-symbol reference boxer) are unified into **one Related Elements
  Highlighting pipeline**: a Core `RelatedElementMatcher` (`Sql/Language/Matching/`, pure/testable) runs
  interchangeable **producers** — selection occurrences, caret-symbol references, caret-adjacent bracket
  pairs `()`/`[]`/`{}` (via the one `SqlLexer`, so brackets in strings/comments never match), and
  caret-adjacent `BEGIN/END` (via the AST `BlockStatement.Descendants` — covers proc/func/trigger/EXECUTE
  BLOCK/anonymous bodies + IF/WHILE/FOR bodies; a `CASE…END` is not a block so its END isn't matched) — and
  the App `RelatedElementsRenderer` (one `IBackgroundRenderer`) paints them. A **future CASE/END or LOOP is
  one more producer, never another renderer.** Matching reacts to the **caret** (adjacent to the token),
  not only a selection. New high-contrast, theme-tuned tokens `RelatedElementHighlight*` (fill + border,
  both dictionaries; the user delegated colour, requiring only high contrast in both themes and palette
  consistency — burnt-orange Light / bright-amber Dark, now visually confirmed). Attached in **BOTH** wiring seams (gotcha #219);
  the DDL-preview editor gets the model-less overload (text producers only).
  **Finalization cleanup (post-confirmation):** the dormant rollback path was removed — `OccurrenceHighlighter.cs`
  deleted and the obsolete `OccurrenceHighlightBorder*` tokens dropped; the one still-live consumer of the old
  fill token, `SearchMatchHighlighter` (Global-Search preview), was migrated onto a correctly-named
  `SearchMatch*` token so no "Occurrence" name survives as drift.
  (`NavigationController`'s nested reference renderer could NOT stay dormant — unused private members fail
  `TreatWarningsAsErrors` — so it was removed in place; git is its revert path.)
  **Post-M1 QA fix (gotcha #223):** bracket matching didn't activate on the FIRST call right after connect
  (worked after clicking another call and returning). The matcher is a pure function of (text, caret,
  model) — proven correct for the exact input; the fault was the App repaint: a plain `InvalidateVisual()`
  could run before the text view's visual lines were rebuilt (`Draw` saw `VisualLines.Count == 0`), and the
  diff guard made the miss permanent. Fixed: repaint with `TextView.Redraw()` (as `SemanticHighlighter`
  does) + skip the guard only on empty→empty so a missed paint self-heals. Build 0/0, **4187 main + 24
  probe green** (+28 `RelatedElementMatchingTests`, +1 headless renderer pin), smoke clean.
  **M1 finalization (post visual-confirmation) — DONE + CLOSED:** the dormant rollback path was removed —
  `OccurrenceHighlighter.cs` deleted, obsolete `OccurrenceHighlightBorder*` tokens dropped, and the one
  live consumer of the old fill token (`SearchMatchHighlighter`, Global-Search preview) migrated onto a
  correctly-named `SearchMatch*` token so no "Occurrence" name survives as drift. Committed `5e51989`.
  **M2 — Smart Snippets — BUILT then REVERTED (2026-07-16); SUPERSEDED.** A VS/Rider-style interactive
  snippet session was implemented (mirrored placeholders, final caret, indentation-aware expansion) but the
  user tried it and rejected the whole direction — *"now I delete half of this."* Full-block skeletons +
  placeholder sessions are the wrong UX for experienced Firebird devs. The code-writing experience was
  **redesigned from first principles** (uncommitted M2 reverted) into two independent subsystems — see the
  next bullet. `CompletionMatcher` (prefix-first) was kept. History + rationale:
  `docs/history/16-stage8-smart-editing.md`.
- **Language Completion & Typing Ergonomics — DESIGN FROZEN + Core foundation started (2026-07-16).** The
  redesign of the code-writing experience, goal = **fewest keystrokes, immediate & predictable, never
  generate code the user deletes (Rule 0)**. Frozen design:
  **[docs/design/editor-language-expansion.md](docs/design/editor-language-expansion.md)**. Three
  independent tools, chosen by grammar: **IntelliSense** (names, prefix-first, idle-debounced — Tool C =
  `CompletionMatcher`); **Language Completion** (finishes daily Firebird *constructs* the developer already
  started typing — `if`→`if (▌) then`, `gro`→`group by ` — via **Tab + a shown OverlayLayer hint**, matched
  by **natural prefix** (no invented abbreviations), **silent-until-unique** within a curated catalog,
  **synchronous / never timing-dependent**); **Typing Ergonomics** (`begin…end` as a structural delimiter
  pair, `()`/`''`/`[]` pairing, AST-aware auto-indent — **Enter stays a normal editing key everywhere**).
  Key principle: *anything special Tab does is always shown on screen first — no EmberTern-specific
  behaviour to memorise.* **DONE:** `CompletionMatcher` (+8 tests) and the **Language Completion Core
  foundation** — `Core.Sql.Language.Constructs` (`LanguageConstruct`/`LanguageConstructCatalog`/
  `LanguageConstructResolver`): the declarative catalog (each row a `ConstructCategory`) + a pure
  synchronous prefix resolver (multi-word aware, unique-within-catalog) + the **grammar-aware arming gate**
  (`ConstructContext` — a simple deterministic previous-significant-token rule: statement boundaries arm
  `Statement` constructs, value-completers arm `Clause` constructs, else none; one cheap synchronous lex,
  no AST/model, no timing). `LanguageConstructResolver.Resolve(text, caret)` = prefix match ∩ grammar is
  the single App entry point. **App layer + QA sprint — DONE and USER-APPROVED (2026-07-16); LANGUAGE
  COMPLETION IS COMPLETE.** `LanguageExpansionController` (App/Completion, attached in BOTH seams — gotcha
  #219) has ONE decision point, `CurrentEdit()`, returning the very `ExpansionEdit` Tab applies — the hint
  renders *that object's* text, so preview and result **cannot** drift (casing was the first proof: `IF`
  previewed `if () then`, inserted `IF () THEN`). Every subscription only says *re-evaluate*; the sole state
  is `_dismissedAt` (Escape's caret offset — not derivable from (text, caret); without it Escape hid the card
  while Tab still expanded). Guards: focus (`TextArea.IsKeyboardFocusWithin` — **`TextEditor` is NOT
  focusable and `editor.Focus()` is a no-op**, gotcha #225), no selection (Tab = block indent), list closed,
  not dismissed. Passive `OverlayLayer` hint; **Tab** expands via a tunnelled KeyDown (gotcha #224).
  **Grammar arming** now returns a `[Flags]` set so a caret can be both Clause and StatementStart: a **blank
  line** *adds* StatementStart (fixes `where` ⏎ blank ⏎ `if`), `(` arms subquery `select`, and a bounded
  enclosing-statement look-back arms `INSERT … SELECT`; widening never removes a position. **ONE
  RESPONSIBILITY, ONE OWNER** — separation is by **vocabulary** *and* **grammatical position** (design §9.1,
  gotcha #228): `LanguageConstructCatalog.OwnedWords` + `KeywordPairCatalog.OwnedWords` (new
  `Core/Sql/Language/Ergonomics/` — **data only**, `begin`/`end` for Typing Ergonomics) are *derived* by
  `CompletionEngine.AddKeywords`, never hand-listed; **and** the identifier list no longer auto-pops where a
  construct is armed (it asks the same `Resolve`). Without that second half, typing `select` **inserted a
  procedure named `SELECT_PRACOWNIKOW`** — a *name* is not a keyword, and an open list owns Tab. Ctrl+Space
  still overrules the grammar. **The Etap-5 keyword live templates are DELETED** (`SnippetEngine`/
  `SnippetTemplate`/`SnippetCompletionData` + tests — design §11's unfinished clause; also removed the P7
  auto-trigger exception and the duplicate `ShowBaselineWindow`). **NOT touched:** the drag-drop
  `Core/Sql/Templates/*` + `SqlSnippetDropTarget` — a different, shipped feature that only shares the word.
  Coverage guarantee: `EveryConstruct_ArmsWhereItMayBegin` (16 constructs × 33 positions) — exclusive
  ownership makes an under-armed position a **dead zone**, so a catalog row arming nowhere fails the build.
  **Conscious open items (user decisions — revisit only on real usage):** `OVER (ORDER BY` doesn't arm;
  single-letter clause arms collide with aliases (`from ORDERS o` → `⇥ order by `); uniqueness is
  catalog-wide before gating (`wh` stays silent after FROM); **CASE stays an IntelliSense keyword** — the
  catalog is intentionally small, grown from real usage, not completeness.
- **Typing Ergonomics — DONE + user-approved (2026-07-16). THE LANGUAGE-EXPANSION DESIGN IS FULLY
  DELIVERED.** `begin` has its owner. (1) **`begin … end` pairing** (`KeywordPairing`) — trigger settled as
  **Enter** (pairing on the word's completion was rejected: it fires while typing `begin_date = …`, a Rule 0
  violation). Enter keeps its meaning — the caret lands where plain Enter+indent would, and the closer
  appears on the line *below*. Pairs only when an `end` is genuinely **missing** (CASE-aware, else Enter
  after an existing `begin` bolts on a second `end`) and only at a statement position (`ConstructContext`).
  (2) **Delimiter pairing** (`DelimiterPairing`) — `()`/`[]`/`''`, type-through (checked BEFORE pairing, as
  `'` is self-closing), smart backspace, suppressed inside literals/comments and before a word; literal
  openness by **quote parity** (`'it''` is open despite ending in a quote); line-comment span boundary =
  gotcha #229. (3) **Structural auto-indent** (`AutoIndent` + `SqlIndentationStrategy`) via AvaloniaEdit's
  own `IIndentationStrategy` seam — one level per unclosed block, +1 after `then`/`do`/`else`, `end` backs
  out. **ONE FORMATTING LANGUAGE (user directive):** the indent is `SqlFormatter.PsqlIndentUnit` — now
  **published by the formatter** because the editor's tab settings were a wrong guess — and a block's indent
  is **structural**, not the opener's typed column (the formatter puts a block under `then` at the `if`'s
  level, a single-statement body one deeper). Pinned by tests running the real formatter over generated
  blocks (`Format(x) == x`). **Deliberately simpler than the formatter:** parens are OUT of auto-indent (it
  aligns to columns, unknowable line-at-a-time) and `IndentLines` is **inherited untouched** from
  `DefaultIndentationStrategy` — re-indent-selection is a lightweight command, not a second formatter (both
  user decisions). CASE-aware block depth lives once in `BlockStructure`, shared by pairing + auto-indent.
  `'` pairing kept pending real usage (one-line removal if it annoys). Build 0/0, **4347 green**, smoke
  clean. **M4 Structural Selection remains future; M3 Snippet Engine would now start from scratch** (that
  engine is deleted).
- **Completion Matching Philosophy — prefix-first IntelliSense — COMPLETE (2026-07-17; impl + headless-probe
  proven, awaits the user's visual confirmation).** A separate **Completion** milestone (not Stage 8):
  interactive completion is a **prediction engine, not a search engine**. No prefix → all (Ctrl+Space);
  prefix with ≥1 StartsWith → **only** StartsWith; zero StartsWith → **no window** (never a Contains
  fallback); identical for every kind. The user re-reported the original symptom (`cont` → every
  `…CONTRACTOR…` object) as a **regression on 2026-07-17 — it wasn't one**: the foundation had shipped
  unused, so AvaloniaEdit's substring filter was still the only thing narrowing the list (gotcha #233 — a
  tested-but-uncalled component looks exactly like a regression, and the green suite is what hides it).
  **Now wired:** `SqlCompletionController` is a passive view — `IsFiltering = false` kills AvaloniaEdit's
  substring filter *and* its quality re-sort (**measured**, gotcha #232), every source (baseline, dot,
  on-demand column warm) routes through `ShowItems` → the one `CompletionMatcher`, and `RefreshOpenWindow`
  (off `Caret.PositionChanged`, so backspace/paste count too) re-filters the session's cached candidates.
  **⚠ The refresh MUST re-assign `ListBox.ItemsSource`, never just mutate `CompletionData`** — that is a
  plain `List<ICompletionData>` and broadcasts no change, so mutation updates the data and **nothing on
  screen** (the list froze on `ID_AKWIZYTOR` while every collection correctly read `ID_NAGL`). Turning
  `IsFiltering` off removes AvaloniaEdit's `SelectItemFiltering`, whose fresh-List assignment to
  `ItemsSource` *was* the refresh mechanism; `Populate` now mirrors that (gotcha #234). Pinned by a probe
  that types into the **open** window and asserts the **realized containers** — asserting `ItemsSource`
  reads our own input back and cannot fail (gotcha #235).
  **Responsibility split — one owner per *question*:** `CompletionEngine` answers "what is legal at this
  caret" (candidate set — a property of the *position*, fixed for the session); `CompletionMatcher` answers
  "which of those match what is typed" (a property of the *prefix*). So `CompletionEngine` deliberately did
  **NOT** get a `prefix` param, contrary to the original directive's letter: a prefix-filtering engine
  cannot widen on a **backspace** without a per-keystroke re-query — against a debounce-lagged model whose
  offsets no longer match the caret, or a synchronous whole-document parse (Etap 0 forbids it). **Deleted as
  now-redundant:** `ApplyInitialFilter` (#200) and `CloseIfNarrowedToNothing` (#227) — taking ownership of
  the filter removed both workarounds rather than adding to them — plus `BuildColumnDetail` and the warm
  path's second column-row builder. The planned `IsFiltering=false` fallback design was unnecessary and is
  not kept. Build 0/0, **4594 green**, smoke clean. As-built + the stale-list follow-up (incl. why six
  reproductions passed before the right instrument was applied):
  [docs/history/17-completion-matching-philosophy.md](docs/history/17-completion-matching-philosophy.md).
  **P2c — matched-fragment highlight — DONE (2026-07-17, user-requested "jak w IBExpert"), and it CLOSED
  ITSELF as a side effect.** P2c ("bold the typed fragment") was deferred for months as *"no clean
  AvaloniaEdit 12.0.0 path"* — true only while AvaloniaEdit owned filtering: rows were built once at open
  and the App never knew the prefix at row-build time. Taking the list over made `Populate` rebuild rows on
  every prefix change **with the prefix in hand**, so the blocker evaporated. Shipped as colour, not bold
  (the IBExpert cue the user asked for): `SqlCompletionData.BuildName` splits the name into two `Run`s —
  matched fragment in the new `CompletionMatchBrush` theme token (both dictionaries; deliberately NOT
  `ErrorBrush`, which it sits beside in the palette — this means "why this row is here", never "something is
  wrong"), unmatched tail inherits the row foreground so selection/theme still drive it. Empty prefix →
  plain text (no meaningless colour on a Ctrl+Space list). **The split renders `CompletionMatcher`'s ruling,
  it does not re-derive it** — `[0, prefix.Length)` follows from StartsWith; if the matcher ever grows a
  tier matching elsewhere it must report the span (§9.1's one-owner rule, one level down). Pinned by
  `CompletionRow_HighlightsMatchedPrefix` (split + the brush is the token + present in both dictionaries).
  Still open (ranking taste): whether common leading keywords deserve a boost over same-prefix objects.
- **⚠ MILESTONE-ORDER DECISION (2026-07-16) — the Stage 7 retrospective's "consolidate the editor wiring
  first" recommendation was REVERSED, with reason.** It rested on "both backlog items add per-editor
  surfaces — exactly what the duplication punishes"; that is true of **Quick Fixes** (a light bulb = a new
  adorner + gesture = a new `Attach` = the silent-omission risk) but **false of Unified Hover** (no new
  attach; required params ⇒ compile-time enforcement; and `NavigationController` is already the chosen
  consolidation point — the double-click handler was moved *into* it from the two seams). Consolidation is
  also not a mechanical merge: the seams differ by a real lifecycle (MainWindow's editor exists *before* its
  VM; object editors attach *after*), and MainWindow deliberately bypasses `subscribeMetadataChanged`
  because it latched "subscribed" against a null VM and dropped the handler — so consolidation must first
  solve "subscribe once the VM arrives". It touches every capability's installation on every surface for
  zero user-visible value, which under the QA rule means a full manual re-verification everywhere.
  **Standing recommendation: the wiring consolidation is the milestone immediately BEFORE Quick Fixes**,
  where it actually pays. Full reasoning: `docs/design/editor-stage7-diagnostics.md` §15.4.
- **R2 (2026-06-18 Transaction Architecture Audit) — CLOSED 2026-07-14, and its premise was wrong.**
  R2 ("procedure lock after Execute → Rollback → Compile") was left OPEN pending a live `MON$` dump,
  and the "Single-attachment DDL" fix that followed concluded DDL must be **co-located** on the
  attachment that executed the object. Measurement (gotcha #214) showed that conclusion was inferred
  from a **NOWAIT** failure: the cross-attachment lock is transient and a **WAIT** transaction clears
  it in ~10 ms. DDL now runs on its own dedicated attachment with a WAIT-bounded TPB, the
  *"Commit or roll back the active transaction before running DDL"* guard is deleted, and the
  scenario is verified working end-to-end on FB5. See `docs/history/15-...`.

## Editor Architecture — current direction

**Full architecture, component specs, and binding decisions: `docs/design/editor-architecture.md`
(kept current — read it before touching anything under `EmberTern.Core.Sql.Language`).** Status
summary only, here:

The SQL/PSQL editor is being rebuilt on **one shared, error-tolerant language front-end** —
Lexer → Parser → AST → Semantic Model, all in `EmberTern.Core.Sql.Language`, pure and
zero-Avalonia — with every feature (formatter, completion, navigation, diagnostics, signature
help, snippets, semantic highlighting, Quick Info) built as a *client* of that one model. This
replaced 7 independent ad-hoc SQL scanners + 3 divergent keyword lists. Governed throughout by
the project's **§0 Paramount Law**: never lose information, never modify code EmberTern can't
reproduce identically, correctness over aesthetics (see "Architecture rules" rule #11 below).

**Etaps 0–6 are COMPLETE**: IntelliSense responsiveness → Lexer + `FirebirdSyntax` keyword
catalog → Parser + AST ("statement skeleton" depth, `RawStatement` verbatim safety valve) →
AST-based Formatter → Semantic Model (scope tree + symbol resolution) → Completion + Signature
Help + Snippets → Navigation + Semantic highlighting + Quick Info (Ctrl+hover, Ctrl+Click,
Peek Definition, safe local rename, find references).

After Etap 6, the user ran a practical review (vs. IBExpert), **endorsed the architecture**, and
filed a **UX Polish Phase** backlog (P1–P9). **P1–P9 are done, including P8 (formatter polish +
max-line wrapping), which is now COMPLETE and architecturally closed** (§F shared list builder →
INSERT / UPDATE OR INSERT → long-line wrapping → EXECUTE BLOCK → FOR SELECT, one mechanism each);
only **P5d (a plain-hover info cue) and P2c (bold the typed completion fragment) remain consciously
deferred** — see "Current state" above for exactly where things stand.

A **pre-Stage-7 architecture review (2026-07-14)** then established the next foundation: because the
AST is a *statement skeleton with token-bag annotations* and SQL structure is duplicated across 3–4
token walkers, a foundational **Etap 6.9 — Structural AST Deepening** is inserted **before Stage 7**,
so the parser/AST becomes the single structural source for the formatter, semantic model, diagnostics,
folding, breadcrumbs and the future Debugger. Two design docs are the implementation guides:
- **[docs/design/editor-ast-deepening.md](docs/design/editor-ast-deepening.md)** — Etap 6.9 (design
  principles, node inventory, migration contract, milestones B0–B5, debugger considerations, formatter
  convergence, and a progress matrix). **Read before touching the parser/AST/binder for this work.**
- **[docs/design/editor-stage7-diagnostics.md](docs/design/editor-stage7-diagnostics.md)** — the full
  Stage 7 (Diagnostics) vision, which consumes Etap 6.9.

**Etap 6.9 parser stage is COMPLETE — B0–B5 parser producers are all DONE** (B0 = scaffolding + §0
differential harness + NUL/alias cleanups; B1 = the PSQL body tree, produced for all four surfaces **and
consumed by the semantic binder — its structural token walker is deleted**; B2 = the query clause tree
(clauses + FROM/join + set operations); B3 = the **fully recursive query model** — WITH/CTE, derived tables,
EXISTS/scalar subqueries all hold real `QueryNode`s; B3.1 = **queries embedded in OTHER statements**
(INSERT/MERGE sources, CREATE VIEW bodies, UPDATE/DELETE/MERGE embedded subqueries, PSQL FOR-SELECT /
DECLARE-CURSOR cursors); B4 = **CASE** (`CaseExpression`/`WhenClause`, simple + searched, SELECT-expression
+ PSQL); B5 = **PSQL body statements are reused top-level DSQL nodes** (a SELECT/INSERT/… inside a routine
body is the SAME node, with the SAME query structure, as at the top level). **The parser is now the single
structural source for all SQL/PSQL structure** (within the structural-depth scope: ordinary expressions stay
token fragments; `EXECUTE STATEMENT '<sql>'` runtime strings and a `PACKAGE` body are conscious boundaries).
**No parallel AST representation remains.** **BINDER CONVERGENCE — DONE (2026-07-15):** `SemanticBinder`
is now a full AST consumer — the query binder reads the `QueryNode` tree (FROM items, WITH/CTE, embedded
subqueries, clauses), the DML binder reads the DML nodes' source query + subqueries, and the PSQL binder
drives its leaf/header subqueries from the AST. Its **structural token walkers are DELETED** (`BindQuery`
token version, `CollectTables`, `ParseTableList`, `ParseCteList`, `BindColumnReferences`'s FROM+`(SELECT`
re-scan, `BindNamedTable`/`BindDerivedTable`/`BindTargetAfter`, `IsTableListTerminator`, and the PSQL
`BindLeafReferences`/`FindBodySelectEnd`/`BindOptionalInto`); only expression-level token work remains
(column/local/param references + DML-target identification, which has no AST node). Two small producer
refinements landed with it: `IF`/`WHILE` carry `ConditionExpressions` (condition subqueries/CASE), and a
PSQL singleton `SELECT … INTO` ends its `QueryNode` before `INTO`. Behaviour-equivalent: build 0/0, **4008
main + 23 probe green**, smoke clean. **FORMATTER CONVERGENCE — DONE (2026-07-15) ⇒ ETAP 6.9 CLOSED:** the
formatter is now an AST-walking layout engine wherever the parser provides structure — `EmitQuery` lays out
a query's clauses and recurses into nested queries as expanded-paren blocks (natural multi-level
indentation), `CaseExpression` lays out adaptively (inline when simple, else a WHEN/THEN/ELSE block), and
WITH/CTE, INSERT…SELECT, CREATE VIEW bodies, MERGE `USING (…)`, UPDATE/DELETE subqueries, and PSQL FOR-SELECT
cursors + leaf statements all drive their layout from the AST. A flat query is byte-identical (all
pre-existing exact tests unchanged), layout is idempotent, §0 is unchanged. The token emitter (`Emit`,
`MatchStructuralPhrase`, the PSQL block structurer) is **retained by design** as the interior/expression
renderer + the layout for constructs the parser intentionally does not model (UPDATE SET/DELETE/MERGE clause
layout, PACKAGE bodies) — one layout mechanism per construct, no parallel AST + token walker. The reported
issues (CASE, WITH, multi-level indentation) are fixed. See `editor-ast-deepening.md` §13.2. The legacy
`SqlAliasResolver` is off the editor path (only `PredicateExtractor`/Performance uses it); retiring it is a
separate Performance migration. **Stage 7 (Diagnostics) is COMPLETE** (S1–S6; engine → squiggles → panel
→ navigation), and the first post-Stage-7 milestone — **Unified Hover Information** (§15, absorbed P5d) —
**has shipped** (see "Current state" above). **Folding and Breadcrumbs** were part of the original "Etap 7
niceties" and are still **unbuilt**; they consume the same AST and need no further foundation. Remaining
backlog: ~~editor-wiring consolidation~~ (**DONE** — debugger milestone D3, 2026-07-17: the main SQL editor
now goes through the one `SqlEditorBehavior.Attach`; gotcha #219 resolved), **Quick Fixes**
(`editor-stage7-diagnostics.md` §12), Folding and Breadcrumbs.
**P2c is DONE** (2026-07-17): its "no clean AvaloniaEdit path" blocker was a *consequence* of AvaloniaEdit
owning the completion filter, and dissolved the moment the Completion Matching milestone took the list over
— a reminder that a long-deferred item is worth re-testing after the thing under it changes. Nothing is
scheduled: next steps are the user's call.

## Architecture rules — enforce against drift

From the master prompt — non-negotiable, still in force today (rule 10 corrected during the
2026-07-11 cleanup: it originally read "no workspace persistence in V1", which V1.1 shipped
long ago — the surviving, still-true boundary is kept below):

1. **Core has zero Avalonia dependencies.** ViewModels in App contain no Avalonia types (no `IImage`, `Color`, `Thickness`). Theme toggle lives in code-behind on purpose — single button, no value routing through VM.
2. **No interfaces without two concrete implementations.** Every service so far (`ConnectionService`, `QueryExecutor`, `TransactionService`, `ConnectionProfileStore`) is a direct class. No `IDbProvider` layer.
3. **No autocommit. Ever.** Auto-*begin* exists (matches IBExpert workflow); auto-*commit* doesn't. There's no toggle, no setting.
4. **Virtualized grid is mandatory.** Avalonia DataGrid handles this — don't replace it with a plain `ItemsControl`.
5. **No `Utils/` or `Helpers/` folders.** If something has no clear home, the structure is wrong.
6. **`UiStrings` is the ONE way code reaches a user-visible string** — never a literal in a view or a view model, never a `ResourceManager` read of your own. ⚠⚠ **AMENDED by the Localization stage (decision D‑2), and the amendment is only about the NOSNIK:** the "no `.resx`" half of this rule is **deliberately lifted** — the words now live in `Localization/Strings.resx` (English = the base/neutral set, satellites per language) and `UiStrings` members are thin **properties** reading them through `Loc`. ⛔ A localized member must never be a `const` (the compiler inlines it, so nothing is left to resolve) nor a `static readonly` (resolved once, then frozen in the first language). ⛔ In XAML the form is `{app:Loc Key}`, never `{x:Static app:UiStrings.Key}` — `x:Static` is not a binding and never re-evaluates, so with it the language could not change without a restart. Read [docs/design/localization.md](docs/design/localization.md) before touching any of this.
7. **No event bus / IMessenger** until 3+ components need to communicate. Currently events on services (`ActiveConnectionChanged`, `TransactionStateChanged`) wire VM directly — that's fine.
8. **Async only where the user waits**: query execution + connection. Not async everywhere.
9. **Dark + Light from day one.** Every new color → both dictionaries in `Themes/Colors.axaml`. Zero hardcoded colors in views — only `{DynamicResource}`.
10. **No plugin system, no debugger, no schema compare, no docking.** (Workspace persistence — the one item on this list that was originally "V1-only" — shipped in V1.1 and is now core; see "What's built" above. AI is separately addressed by the editor-architecture decision "kept AI-ready, nothing designed solely for AI".) The UI mockup shows aspirations; build only what's actually planned, not the whole vision at once.
11. **Never lose information / never corrupt user code or metadata (Critical / Data-Loss class — the project's #1 rule, above every feature).** Any feature that generates DDL or modifies user code or DB objects — formatter, recompile, refactor, Quick Fix, Rename, snippet expansion, future AI — MUST preserve every fragment it does not fully understand, **verbatim 1:1**. **If EmberTern is not 100% certain it can reproduce an object identically, it MUST NOT modify it automatically** (uncertainty ⇒ do nothing or ask). Correctness of generated code outranks aesthetics. Origin: a group procedure recompile once stripped input-parameter defaults and broke system mechanisms (gotcha #175) — that class of bug is unacceptable. In the editor front-end this is realized by an error-tolerant parser + `RawStatement` verbatim round-trip. See the "Editor Architecture — current direction" section above + [docs/design/editor-architecture.md](docs/design/editor-architecture.md) §0.

## UI styling rules — theme discipline (enforce on every new window / dialog / control)

The app has **one** central theming system. Every new window, dialog, UserControl, DataTemplate, and control MUST go through it — no exceptions. These rules exist because new UI kept introducing local colors and FluentTheme's `SystemAccentColor`-derived highlights (the brown/orange selection rectangles), which clash with the workbench palette.

**The central system — five files, each with ONE job; nothing else holds a colour or a metric:**
- [`Themes/Colors.axaml`](src/EmberTern.App/Themes/Colors.axaml) — the **single source of every color**. `ThemeDictionaries` with a `Dark` and a `Light` dictionary, each defining the same set of `Color` keys then `SolidColorBrush` keys over them. This is the token catalog.
- [`Themes/Tokens.axaml`](src/EmberTern.App/Themes/Tokens.axaml) *(M2a)* — the **non-colour catalog**: spacing, `Thickness`/`CornerRadius` roles, control heights, icon sizes, radii, border widths. **No `ThemeDictionaries`** — a metric does not depend on the theme.
- [`Themes/Typography.axaml`](src/EmberTern.App/Themes/Typography.axaml) *(M2a)* — the **12 typography roles** (size · weight · line-height) + `Font.Ui` / `Font.Code`.
- [`Themes/FluentBridge.axaml`](src/EmberTern.App/Themes/FluentBridge.axaml) *(M2b)* — ⭐ **the mapping layer that repins FluentTheme's own named resources onto our tokens**, so we keep the framework's behaviour without copying its templates. ⛔ **Mapping only — never a second token catalog** (rule 8 below).
- [`Themes/ControlStyles.axaml`](src/EmberTern.App/Themes/ControlStyles.axaml) — the **single home for shared/reusable styles** (`Button.icon`, `Button.primary`, `Button.flat`, `Button.caption`, `TabItem.bottom-tab`, `TabItem.sub-tab`, `TextBlock.field-label`, `DataGridRow`/`DataGridCell`/`DataGridColumnHeader`, `ListBoxItem`/`TreeViewItem` state overrides, etc.). Loaded app-wide via `Application.Styles`, so these styles apply inside dialog windows and UserControls too. **Also the home of every control METRIC setter** (rule 8).

⚠ [`Themes/ControlThemes.axaml`](src/EmberTern.App/Themes/ControlThemes.axaml) holds the **two** hand-written `ControlTemplate`s (`CheckBox`, `RadioButton`) — structure, not style. Adding a third requires the two measured conditions in `product-polish.md` §16.4.

**Hard rules:**
1. **No hardcoded colors. Anywhere.** No hex literals (`#RRGGBB`, `#AARRGGBB`), no named colors (`White`, `Black`, `Red`, …) on any `Background` / `Foreground` / `BorderBrush` / `Fill` / `Stroke` / `Color` / `Value` in views, code-behind, or styles. The only literal allowed is `Transparent` (it's "no fill", not a theme color — used for hit-target borders and reset states). If you need a color, it is a **theme token** in `Colors.axaml` or it does not exist.
2. **No local color definitions.** No `<SolidColorBrush>` / `<Color>` declared in a view's `.Resources`, no `new SolidColorBrush(...)` / `Color.Parse(...)` / `Brushes.X` / `Colors.X` in code-behind. Per-kind icon colors flow through `IconResourceKey` + `IconBrushConverter` (the VM holds the **key string**, never a brush — keeps "no Avalonia types in VMs" intact).
3. **Every color comes from both Light and Dark.** Add a new token → add it to **both** `ThemeDictionaries` (same key in `Dark` and `Light`). A token that exists in only one dictionary is a bug. Tokens that are intentionally theme-independent (e.g. `OnAccentColor` white text over the colored accent/chips, `CloseButtonHoverColor` the Windows caption red) are still defined in **both** dictionaries with the same value, with a comment saying why.
4. **Consume tokens via `{DynamicResource}`**, never `{StaticResource}`, for any brush a control paints with — `DynamicResource` re-resolves on theme toggle so the control recolors live. (`StaticResource` inside `Colors.axaml` to chain a `Color` into a `SolidColorBrush` is fine — that's a definition, not a consumption.)
5. **Reusable styles live in `ControlStyles.axaml`, not in views.** If a style (label, button variant, tab variant, grid styling) is or will be used in more than one place, it goes in the central file and views reference it by `Classes="..."`. A view's local `<X.Styles>` block is allowed **only** for genuinely view-specific, non-duplicated structure — and even then it must use theme tokens (e.g. row-height/padding sizing, opacity-only hover affordances, a `Classes.active-tab` background swap that reads `BackgroundBrush`). When in doubt, centralize.
6. **Never override FluentTheme state colors with hardcoded values.** FluentTheme paints selection/hover/focus from `SystemAccentColor` (brown/orange on a default Win11 install). The fix is already centralized: the `TreeViewItem*` / `DataGridCell*` / `ListBoxItem` resource-key overrides in `Colors.axaml` + the Style selectors in `ControlStyles.axaml`. New selection-driven controls inherit these automatically — do not re-solve it locally with a hex color.
7. **New windows/dialogs must set `Background="{DynamicResource BackgroundBrush}"` + `Foreground="{DynamicResource ForegroundBrush}"`** on the root so they theme correctly (FluentTheme's window default isn't our palette). Use `Classes="field-label"` for form captions, `Classes="h1"` for dialog headers, the `Button.primary` / `Button.flat` / `Button.icon` variants for buttons — don't restyle from scratch.
8. ⭐ **Restyling a base control: repin Fluent, don't rewrite it — and metrics, colours and aliases take THREE SEPARATE ROUTES.** Full pattern + the per-control procedure: [`product-polish.md` §16](docs/design/product-polish.md). In short: **metrics** (`MinHeight`, `Padding`, `FontSize`, `BorderThickness`) → a **style setter** in `ControlStyles.axaml` reading a token; **colours painted by template internals** (`PART_BorderElement` and friends) → **`FluentBridge.axaml`**; **a value the template holds as a LOCAL value on the element** (measured: `ExpanderMinHeight`, `ScrollBarThumbBackgroundColor`) → a **resource alias** `<StaticResource x:Key="…" ResourceKey="…" />`, because a setter cannot beat a local value. ⛔ **No local values in the Bridge** (`FluentBridge_ContainsNoLocalValues` fails the build otherwise), and ⛔ **no new `ControlTemplate`** unless §16.4's two conditions are both measured true.
9. ⭐ **Never write a number a token already names.** Spacing, control height, font size, radius, border width and icon size all have roles in `Tokens.axaml` / `Typography.axaml`; consume them with `{DynamicResource}` (architectural — §3.4 wants a future font/scale preference to swap the base tokens live). ⚠ `{DynamicResource}` **does not throw on a missing key** — the property silently keeps its default, so a typo is invisible at build time; that is what `DesignTokenApplicationTests` exists to catch.
10. ⭐⭐ **A control's size comes from its CONTEXT, never from its variant** (M2b §17.2). A chrome strip (`Border.chrome`), a `DataGridCell`, an `Expander` header and a dialog footer each declare what their children may be; the variant (`.primary`) carries **colour**. ⛔ **Never put `MinHeight`/`MinWidth` on the base `Button` style** — that exact setter silently grew the metadata tree's expander arrow from 20 to 100 px, because Avalonia clamps `Width` by `MinWidth`.
11. ⭐ **Write the rule POSITIVELY.** *"Everything is X unless…"* leaks: in M2b it leaked twice, the second time as a layout regression. State what a thing IS (a class, a container), never what it is not.

**Token cheat-sheet** (semantic name → use): `BackgroundBrush` (window/editor), `PanelBrush` (sidebar/header panels), `ChromeStrongBrush` (titlebar/column headers — chrome one step further from the document), `SurfaceRaisedBrush` (⭐ anything that FLOATS above its container: popups, menus, tooltips, dropdown lists, the selected tab — **not** the same job as chrome, and in Light the two are opposites), `BorderBrush` (structural separators, gridlines, the rest rail), ⭐ `ControlOutlineBrush` (**the visible
outline of an interactive control at rest** — a different role from `BorderBrush`, and that distinction is
what makes an unchecked `CheckBox` findable: sharing one token measured 1.35:1 in Light. Consumers: CheckBox,
RadioButton), `ForegroundBrush` (default text), `SubtleForegroundBrush` (hints/captions), `AccentBrush`/`AccentMutedBrush` (primary action, focus accent), `OnAccentBrush`/`OnAccentSubtleBrush` (text on accent/colored chips), `SelectionBrush`, `HoverOverlayBrush`, `FocusBorderBrush`, `ErrorBrush`/`WarningBrush`/`ConnectedBrush`, `TransactionActiveBrush`, `CommitButtonBrush`/`RollbackButtonBrush`, `RowAlternateBrush` (zebra), `DropTargetBrush`, `CloseButtonHoverBrush`, `DataLaneChipBrush`/`MetadataLaneChipBrush`, `IconColor_*` (per metadata kind, via `IconBrushConverter`). If none fit, add a new token (both dictionaries) — don't reach for a literal.

### Reuse before create

Before adding any of the following, **search the project first** and prefer extending/sharing over a parallel implementation:
- a new **style** → check `Themes/ControlStyles.axaml` for an existing class (`Button.icon`, `field-label`, `bottom-tab`, …) before writing one;
- a new **component / control** → check `Controls/`, `Views/` and `ViewModels/` for an existing one to extend;
- a new **error / warning / info message** on a work surface → use `Controls/MessageBanner` (severity +
  message + optional Copy/Expand/Dismiss); never a locally-styled coloured `TextBlock`;
- a new **dialog layout** → reuse the dialog skeleton (`Background`/`Foreground` tokens + `h1` header + `field-label` captions + `Button.primary`/`flat` footer) used by the existing dialogs;
- a new **DataGrid behavior** → reuse `Behaviors/GridLayoutBehavior.cs` (column order/width/auto-fit), the `RowIndexComparer` (object?[] sort), and the dynamic-column build pattern in `TableDetailTabView` / `MainWindow.PopulateResultGrid`;
- a new **pagination mechanism** → reuse the page-state shape already in `TableDetailTabViewModel` (CurrentPage / PageSize / First/Prev/Next/Last + `HasNextPage`/`HasPreviousPage` + hint string) and the shared `TableDetailPagination*Icon/Tooltip` strings — do **not** stand up a second paging system;
- a new **toolbar** → extend the existing titlebar / editor toolbar, gating new buttons on the relevant `IsXxxTabActive` flag;
- a new **theme resource** → check `Themes/Colors.axaml` for an existing token (see the cheat-sheet above) before adding one.

Preferred order: **extend an existing component › reuse an existing style/behavior › share logic across views › (last resort) create new**. Avoid parallel implementations of the same capability — they drift and double the maintenance surface.

### UI Review Checklist

Before considering any UI task done, verify:
- [ ] no hardcoded colors (`#RRGGBB` / named) — only theme tokens;
- [ ] no local color definitions (no `<SolidColorBrush>`/`<Color>` in a view, no `new SolidColorBrush(...)` / `Brushes.X` in code-behind);
- [ ] colors consumed via `{DynamicResource}`;
- [ ] **no literal metric a token already names** — spacing, height, `FontSize`, radius, border width, icon size come from `Tokens.axaml` / `Typography.axaml` (rule 9);
- [ ] **a restyled base control follows §16** — metrics via a style setter, colours via `FluentBridge`, no new `ControlTemplate` without §16.4;
- [ ] **judged in the complete set of states** — normal · hover · active/checked · disabled · focus;
- [ ] renders correctly in **Light** theme;
- [ ] renders correctly in **Dark** theme;
- [ ] existing styles / components reused (no reinvented label / button / grid / pagination);
- [ ] no duplicated styles (shared ones live in `ControlStyles.axaml`);
- [ ] no duplicated functionality (reused the existing behavior/VM pattern instead of a parallel one).


## Live gotchas — load-bearing subset

The **complete** catalog (organized thematically; ⚠ its entry count is kept in ONE place — the Documentation
map's row — never repeated here) lives in
**[`docs/gotchas.md`](docs/gotchas.md)**. Below are the ~20 that are load-bearing across almost
*any* future session — the rest are searchable there by keyword the moment a bug "feels
familiar". Each line is a one-sentence summary; follow the `#N` reference into `docs/gotchas.md`
for the full explanation, code, and the failure it prevents.

**Firebird transactions & connections**
- Never start a Firebird transaction from a bare `IsolationLevel` — always build explicit
  `FbTransactionOptions` (the driver's default silently picks `WAIT`, the opposite of what you
  usually want). *(#85)*
- One `FbConnection` allows exactly one transaction at a time — concurrent commands on the same
  connection must be serialized (`CommandLock`), and any reader must attach to the caller's
  active working transaction rather than opening its own. *(#89, #31, #22-revised)*
- A lane-resolving lock accessor (`MetaLock()`/`DataLock()`/`LaneLock()`) must be captured into a
  **local variable once** per acquire/release pair — never re-invoked at `Release()`, or a
  mid-call lane flip leaks one semaphore and over-releases another (survives reconnect; only a
  restart clears it). *(#98, #120)*
- **DDL ⇒ WAIT with a bounded lock timeout, wherever it runs.** The cross-attachment
  `object … is in use` is a TRANSIENT metadata-cache lock that bites only **NOWAIT**; a WAIT
  transaction clears it in ~10 ms. This **supersedes the old "DDL must be co-located on the
  attachment that executed the object"** conclusion (#122), which was inferred from a NOWAIT failure
  and forced Compile onto the data connection — which in turn forced the *"Commit or roll back the
  active transaction before running DDL"* guard. Both are gone: DDL now runs on its own dedicated
  attachment, independent of every user transaction. Never conclude "Firebird forbids X across
  attachments" from a NOWAIT failure without re-testing with WAIT. *(#214, supersedes #122;
  `docs/history/15-...`)*
- **A Firebird transaction cannot use an object it created but has not committed** —
  `CREATE TABLE T …; INSERT INTO T …;` in one transaction fails the INSERT with `Table unknown`
  (-204). Firebird cannot both let a transaction use an object it created *and* keep it rollbackable;
  `isql`/IBExpert choose the former via `SET AUTODDL ON`. So in EmberTern's console the user must
  Commit between DDL and dependent DML — that is correct, expected behaviour. Corollary: uncommitted
  DDL is invisible to other attachments, so an object created in the SQL Editor appears in the
  metadata tree **only after Commit**. *(#213, `docs/history/15-...`)*
- **A statement classifier may decide whether to REFRESH the UI; it must never decide WHERE/HOW a
  statement executes in an interactive console.** The SQL Editor used to auto-route DDL onto a second
  attachment with a hidden second transaction — making Commit ambiguous and splitting mixed scripts
  across two transactions. The classifier is kept (it is reusable infrastructure and the foundation
  of the future Script Executor engine); only the routing was removed. *(#215,
  `docs/history/15-...`)*
- After a transaction settles, refresh ONLY the object actually touched — never blanket-refresh
  every open tab (each refresh reruns several implicit-tx catalog reads, which on a DB with an
  `ON TRANSACTION_COMMIT` trigger multiplies into a real storm). *(#119)*

**Firebird catalog & DDL generation**
- Firebird catalog columns vary by version (e.g. `RDB$IDENTITY_TYPE` is FB3+) — version-gate the
  query (`ParseServerMajor`) instead of issuing a doomed SELECT and catching the exception.
  *(#146)*
- A `.sql` script written for `isql`/IBExpert must be UTF-8 **without** a BOM —
  `Encoding.UTF8` in .NET emits one and breaks the first statement's parse. *(#178)*
- Object names typed by the user are coerced to UPPERCASE on input (Firebird folds unquoted
  identifiers anyway) — apply this consistently to every new name-entry field. *(#141)*
- In PSQL, distinguish `CASE…END` from `BEGIN…END` by statement scope (to the next top-level
  `;`), never by a naive `BEGIN+1/END-1` counter — a `CASE`'s `END` has no matching `BEGIN` and
  will corrupt any hand-rolled block scanner. Route through `SqlScanHelpers`'s shared CASE-aware
  scanner. *(#117, #128, #129)*

**Avalonia UI & data binding**
- `x:DataType` on a `<Style Selector="...">` does **not** scope the selector at runtime — it's a
  compile-time binding hint only. A container style shared by multiple VM types needs ONE style
  with `ReflectionBinding` setters, never one typed style per VM type (the latter silently
  clobbers with `UnsetValue`). *(#38)*
- Avalonia's `DataGrid`/`TreeView`/`ListBox` don't select the row under the cursor on
  right-click — wire `PointerPressed` to select-then-let-the-context-menu-open, or context-menu
  actions act on stale selection. *(#16, #99)*
- A `TreeView` with a nested `VirtualizingStackPanel` cannot do stable random-access scrolling on
  a large expanded subtree — the sidebar was migrated to a flat, single-VSP `ListBox` for this
  reason. Filtering must rebuild the bound collection, never just flip `IsVisible` on hidden
  items (the panel still measures every hidden row). *(#154, #157, FlatTree migration in
  `docs/history/09-...`)*
- `Avalonia.Controls.TreeDataGrid` is a **commercially licensed** Avalonia "Accelerate" control —
  verify licensing before depending on it, version compatibility is not enough. *(#158)*
- A button/command gated on a computed or collection-derived value (`Count`, `Any()`, a
  `CanExecute`) needs an explicit `NotifyPropertyChangedFor`/`OnPropertyChanged` on **every**
  mutation path — correctly computing the value isn't enough if nothing tells the binding to
  re-query it (symptom: "the feature works but the button stays disabled"). *(#179, #187)*

**Editor language front-end (the current, active work)**
- The AST round-trips the source byte-for-byte via the retained token stream — this is
  independent of parsing depth, so `RawStatement`/an under-modeled node never risks data loss.
  Any text-reproducing consumer migrated onto the parser must be gated behind a permanent
  differential test proving byte-identity against the previous implementation. *(#191, #192)*
- No transitional class names (`V2`, `NewX`, `Temp`, `Parser2`, …) are left in the codebase once
  a migration completes — consolidate to the plain responsibility name the moment the old
  implementation is deleted. *(#195)*
- Any offset→scope/reference lookup driving an editor feature (completion, Quick Info, go-to-def)
  must be **inclusive at the end of a span** — the caret sitting at the exact end of a
  statement/identifier is the single most common position, and a half-open range silently
  resolves to the wrong (enclosing) scope there. *(#198)*
- Every object editor (Table/View/Procedure/Trigger/Function/Package/Domain/Generator/Exception/
  Index Detail) ships a Revert/Discard action beside its primary Compile/Save action, and it must
  **confirm** before discarding — an accidental click must never lose uncompiled work. *(#143)*
- **`SqlEditorBehavior.Attach` IS now "the one seam" for the editor-intrinsic block — RESOLVED by D3
  (2026-07-17).** It *used* to install only the OBJECT editors' capabilities while the main SQL Editor
  hand-wired its own in `MainWindow` — so a capability added to only one silently missed the other (how S3
  shipped with no squiggles in the SQL Editor). **D3 consolidated it:** `MainWindow` now calls the same
  `SqlEditorBehavior.Attach` at VM-arrival, so a new editor-intrinsic capability goes in **one** place.
  Per-host wiring (`DiagnosticsPanelHost.Track`, `AmbientModelRefresh`, `SqlSnippetDropTarget`) stays with the
  caller by design. *(#219 — resolved)*

**General**
- Reflect the actual API surface (get/set, public/protected) before assuming a member is
  settable or overridable — a member appearing in a metadata dump doesn't mean it has an
  accessible setter or is safely overridable. *(#199, applies broadly)*
- One headless UI test session per test **process** — share it, never `StartNew` per test. Not tidiness:
  AvaloniaEdit builds its caret/editing `KeyBinding`s as **static** lists owned by the thread of whichever
  session first constructs a `TextEditor`, so any real key sent into an editor from a later session throws
  *"the calling thread cannot access this object"* — no injection style avoids it. **It is shared through an
  `ICollectionFixture` (`HeadlessCollection`), NOT `IClassFixture`** — the latter creates one per test *class*,
  so a second consumer silently gets a second session; join the collection instead. *(#94, #226, #286)*
- **A derived value that is typed by hand goes stale SILENTLY, and the guard against it must key on the
  value's SOURCE, not on the value.** A shortcut written into a string (`"Format SQL · Alt+F"`) survived the
  gesture being re-bound with a green build and green tests — a tooltip teaching a key that no longer existed.
  A *correctly composed* string contains the same text at run time, so only the declaration (`const` = literal
  by definition) distinguishes the two. Generalises to any copied derived fact. *(#284)*
- **Reflect the real runtime contract of a UI member before guarding on it.** AvaloniaEdit's `TextEditor`
  is **not focusable** — `editor.Focus()` is a no-op returning `false` and `editor.IsFocused` is *always*
  false; keyboard focus lives on `editor.TextArea`. A guard written against the plausible-looking member
  compiles, tests green, and silently disables the feature forever. *(#225, an instance of #199)*

## Known driver gotchas (Firebird + managed .NET driver)

- **`FirebirdSql.Data.FirebirdClient` 10.3.4 implements only Srp / Srp256.** No `Legacy_Auth` code path in the managed assembly. `FbConnectionStringBuilder.AuthPlugins` does **not exist** as a typed property; setting it via the dictionary indexer is silently ignored.
- **`FbServerType` is `Default` or `Embedded`.** `Default` is pure managed wire — `fbclient.dll` is **not loaded** on this code path, and the driver consults its `ClientLibrary` only in Embedded mode, which EmberTern never selects. ⚠⚠ **This line used to end "`ClientLibraryPath` only matters in Embedded mode (kept in the UI but harmless when unused)" — and that parenthesis was wrong, which is why the field is GONE (S-5, 2026-08-05).** It was not harmless: it offered the user a decision that could have no effect, and the user found it by pointing it at a completely invalid DLL and connecting successfully. ⛔ Do not re-add a client-library setting without the Embedded mode that would make it work; two guards in `ConnectionProfileStoreTests` say so, and the second keys on the **assignment** `ServerType = FbServerType.Embedded`, not on a mention.
- **Firebird 3 "Install incomplete... CREATE USER" error**: caused by SYSDBA living only in the legacy password file. Fix is **server-side**, not client-side: `CREATE USER SYSDBA PASSWORD '…' USING PLUGIN Srp;` against any database on the instance (security3.fdb is instance-wide). IBExpert works because it uses native fbclient with Legacy_Auth support; managed .NET driver can't. See `memory/feedback_firebird_multiversion.md`.
- **WIN1250 / WIN1252 / ISO8859_2**: register `CodePagesEncodingProvider.Instance` before any `OpenAsync` (done in `FirebirdConnectionService` static ctor). See `memory/feedback_firebird_codepages.md`.
- **Connection errors show the raw server message.** `MapErrorMessage` always returns `"Could not connect to {endpoint}: {ex.Message}"` — nothing else. Do not add hints or interpret error causes (wrong password, missing user, plugin mismatch, host down, …); the server message is authoritative and the user or admin can read it directly. Earlier builds tried to categorize errors and surface a `CREATE USER … USING PLUGIN Srp` hint for Legacy_Auth; that was removed because it misfired on unrelated failures (the driver concatenates the whole GDS error vector, so wrong-password / missing-user errors often carried `"plugin"`/`"Legacy_Auth"` text and got mis-hinted).
- **Connection-attempt debug log**: every Connect/Test appends a timestamped, password-masked connection string to `%TEMP%\EmberTern-debug.log` (`LogConnectionAttempt` in `FirebirdConnectionService`). Useful for triaging "EmberTern says X, IBExpert says Y" reports — but remember they take entirely different protocol paths.

## Working style — session protocol

The master prompt is delivered milestone-by-milestone. Don't pre-build future milestones. When the user says "M5 only", that's the scope — questions / hypotheticals about M6 go in memory pointers, not code.

If the user asks for a change that contradicts a hard rule, push back briefly and ask before implementing — the rules exist for a reason and the user has flagged "remind me when I drift."

**After each milestone** (this replaced the old "append to CLAUDE.md's Completed milestones
section" instruction after the 2026-07-11 Documentation Cleanup Sprint — see "Documentation map"
above; do not revert to the old habit, it's exactly what made CLAUDE.md too expensive to load):
1. Write the milestone's full narrative into the most relevant existing `docs/history/*.md` file
   (or create a new one, named for its topic, if it doesn't fit any existing file) — this is
   where the "what we tried, what worked, why" detail belongs.
2. If the milestone changed anything in "What's built", "Current state", the "Architecture rules",
   or the "Editor Architecture — current direction" section, update those sections in CLAUDE.md
   **in place** — a sentence or a bullet, not a new appended block. CLAUDE.md describes the
   present, not the path that got here.
3. If the milestone taught a genuinely new lesson, add it to `docs/gotchas.md` in the right
   thematic section; promote it into CLAUDE.md's short "Live gotchas" list only if it's the kind
   of thing that would bite almost any future session, not just one working in that module.
4. Confirm `dotnet test` is green and the app launches before claiming "done".

## Working conventions

### Session management
- **One milestone per session.** Start a new Claude Code session for each milestone (M1, M2, …, V1.1 task N). Don't try to land two milestones in the same chat.
- **End every session by updating the docs** *before* closing the session — the milestone's full narrative goes in `docs/history/*.md` (new or extended file); CLAUDE.md's own "What's built" / "Current state" / rules sections get updated **in place** (short, present-tense); genuinely load-bearing gotchas get added to `docs/gotchas.md` (and, if cross-cutting, to CLAUDE.md's short list). CLAUDE.md is the handoff document, not the chat transcript, and it must stay short — that's the entire point of the 2026-07-11 Documentation Cleanup Sprint (see "Documentation map" above).
- **Every new session starts by reading CLAUDE.md.** Do not ask the user to re-explain context — the answer is in this file. If something needed is missing from here, that's a documentation gap to fix on the way out, not a question to ask on the way in.

**Why these rules exist:** Claude Code's context window grows with every message in a session. Long sessions burn more tokens per turn (re-reading the whole transcript), risk hitting context limits mid-task, and make cost unpredictable. One milestone per session keeps the working set tight, costs predictable, and the handoff explicit — CLAUDE.md carries the state, not chat history.


## Pointers to deeper notes

- **`docs/design/editor-architecture.md`** — the current, kept-up-to-date architecture of the
  SQL/PSQL editor language front-end. Read before touching `EmberTern.Core.Sql.Language` or
  anything downstream of it.
- **`docs/design/editor-ast-deepening.md`** — **Etap 6.9 — Structural AST Deepening** implementation
  guide (design principles, node inventory, migration contract, milestones B0–B5, debugger
  considerations, formatter convergence, progress matrix). The next foundational work, ahead of
  Stage 7. Read before deepening the parser/AST/binder.
- **`docs/design/editor-stage7-diagnostics.md`** — the full **Stage 7 (Diagnostics)** design/vision
  (engine, `Diagnostic` model, severities, categories, pipeline, squiggles/panel/navigation,
  incremental refresh, cancellation, performance, milestones, and post-Stage-7 Quick Fixes). Consumes
  Etap 6.9; explains why Diagnostics comes after AST Deepening.
- **`docs/design/firebird-debugger.md`** — **Stage X — Firebird Debugger. DESIGN v2, decisions ratified
  2026-07-17; this is the target implementation spec. Nothing implemented.** Read before any debugger
  work. Key established facts (all measured against the live engine — §15 is the log): Firebird exposes
  **no debugging API at any version** (`RDB$DEBUG_INFO` is a BLR→source map, `MON$CALL_STACK` is
  read-only, `RDB$PROFILER` measures but cannot stop), so every Firebird debugger is a **client-side
  PSQL interpreter**. EmberTern's owns **control flow** (from the AST — incl. exception handlers) and
  delegates **all semantics** to the server via a generated anonymous `EXECUTE BLOCK` harness, so **no
  expression AST is needed** (the structural-depth boundary holds). Local routines need **no temporary
  packages** (IBExpert's workaround): stepping into one is just another frame. **The v1→v2 review
  falsified four claims** — a per-statement harness does **not** preserve Firebird's **call atomicity**
  (⇒ a SAVEPOINT per simulated frame), injecting frame state is **not** semantically neutral (a harness
  that assigns `NULL` into a `NOT NULL`-domain variable **fails on ordinary ERP code**), the **clock** is
  request-scoped (`CURRENT_TIMESTAMP` diverges while stepping), and **`WHEN … DO` was missing entirely**
  (⇒ **prerequisite P1**: the AST does not model handlers — they are a `PsqlLeafKind.Other` token bag).
  **⚠ `IN AUTONOMOUS TRANSACTION` work and generator increments survive the debug rollback** — "nothing
  is persisted" is false. Debugger scope is **FB3/FB4/FB5 only**; FB2.5 is already unreachable (the
  driver is Srp-only, FB2.5 is Legacy_Auth-only), so **P2**'s connect-time version gate ratifies reality
  rather than dropping support. The editor-wiring consolidation (gotcha #219) is **D3**, immediately
  before the first debugger UI — deliberately *after* the pure Core/Firebird milestones (D1/D2), which
  need no wiring.
- **`docs/design/firebird-debugger-implementation-plan.md`** — **the debugger's execution plan; read it
  (plus your milestone's brief) at the start of every debugger implementation session.** Milestone briefs
  for **P1** (AST exception handlers — blocks D1), **P2** (FB3+ version gate — app-wide, not
  debugger-scoped), and **D1–D14**, each with scope / components touched / new types / dependencies /
  risks / Definition of Done / how to verify (tests + Lab). Also: the **session split** (≈28 sessions,
  each ending build 0/0 + green tests + smoke + committable, with explicit seams inside the big
  milestones), the **danger zones** (dual editor wiring #219 until D3, one headless session #94/#226,
  `TextEditor` not focusable #225, `TextView.Redraw()` #223, dispatcher priority #221, the user's
  transaction is untouchable, per-wire-operation locking #236), and the **Developer Contract** — 20
  binding rules (never re-parse SQL, never duplicate `SemanticModel`, never re-implement Firebird
  semantics, the harness is the only server path, no alternative execution paths, no temporary metadata,
  §F outranks features, verify-don't-infer, one milestone per session ending green). **Order: P1 → P2 →
  D1 → D2 → D3 → D4 …** — risk first; the wiring consolidation sits at D3 because D1/D2 are pure and need
  no wiring.
- **`docs/gotchas.md`** — the complete gotcha catalog. Search it whenever a bug looks familiar.
  ⚠⚠ **The entry count is deliberately NOT repeated here.** This line and the one above "Live gotchas" used to
  carry their own numbers, and all three disagreed while every one of them was wrong — a prose counter drifts
  the moment the file it counts grows, and three counters drift three ways (#284's shape, one layer out).
  **The count lives in exactly one place: the Documentation map's `docs/gotchas.md` row.** Re-measure before
  quoting it (`grep -oE "^[0-9]+\. \*\*"` → unique numbers), and note the duplicate-number caveat recorded there.
- **`docs/history/README.md`** — index into the full project narrative archive (every milestone,
  session, and investigation, ~20 thematic files). Read a file when you need the "why" behind a
  specific feature or fix; nothing here is loaded automatically.
- **`docs/design/*.md`** (other files) — frozen, feature-specific design docs for already-shipped
  work: `script-executor-and-smart-parameters.md`, `execution-modes-and-export-framework.md`,
  `etap1-tokenization-audit.md`.
- **`memory/project_embertern_blueprint.md`** — the original V1 scope + hard-rule framing (V1
  shipped 2026-05-28). Mostly superseded by the "Architecture rules" section above and
  `docs/history/00-v1-definition-of-done-and-backlog.md`; kept for the historical framing.
- **`memory/project_embertern_scaffold.md`** — deep M1–M6 (V1) code-layout notes at a finer
  grain than `docs/history/01-v1-foundation-and-workspace.md` covers (exact gotcha mechanics,
  file-by-file layout as it stood at V1). Explicitly froze at M6; everything since is in
  `docs/history/`.
- **`memory/project_embertern_editor_architecture.md`** — a compact, actively-maintained memory
  mirror of the editor rebuild's status; kept in sync with `docs/design/editor-architecture.md`.
- **`memory/feedback_firebird_codepages.md`** — WIN1250/WIN1252/ISO8859_2 `CodePagesEncodingProvider`
  registration gotcha.
- **`memory/feedback_firebird_multiversion.md`** — FB3 SYSDBA "Install incomplete" auth fix +
  managed-driver auth-plugin caveats.
- **`memory/feedback_firebird_transactions.md`** — the full transaction-lane audit trail (C1/C2,
  the 2026-06-18 Transaction Architecture Audit, R1/R2/R3). Corrected during this cleanup sprint
  to reflect that R3 was ultimately resolved by *reverting to* a buffered/staged Compile model
  (not by keeping apply-immediately, as an earlier note here had it) — see "Current state" above
  for R2's still-unconfirmed status.
- **`memory/feedback_staged_implementation_contract.md`** — each etap of a staged rollout ships
  complete + tested + smoke-verified + polished before the next starts; never silently change a
  frozen design mid-flight.
- **`memory/feedback_never_lose_information.md`** — the paramount #1 project rule (Architecture
  rule #11 above): never corrupt user code or metadata; don't modify what can't be reproduced
  identically.
- **`memory/feedback_naming_no_transitional.md`** — no `V2`/`NewX`/`Temp` names left in the
  codebase once a migration completes.
- **`memory/reference_embertern_prompt.md`** — where the original master prompt + UI mockup live
  on disk.
