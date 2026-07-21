# D15 — Debugger Experience & IDE Polish

**Status: DESIGN — planning phase COMPLETE, ratified by the user 2026-07-20. Nothing implemented.**
This document is the self-contained implementation guide: a future session can start any D15 milestone from
here **without re-analysing the topic**. It records the architecture, roadmap, per-milestone seams, design
decisions + rationale, priorities, dependencies, and risks. The Script Executor Rewrite is a **parallel track**
governed by its own doc ([script-executor-transaction-review.md](script-executor-transaction-review.md) §5/§6);
D15 only references it (§14).

Read the debugger spec/plan first if touching the engine: [firebird-debugger.md](firebird-debugger.md),
[firebird-debugger-implementation-plan.md](firebird-debugger-implementation-plan.md). D15 sits **on top** of the
finished debugger (Stage X, complete through D13; D14 deferred).

---

## 0. What D15 is — and is not

D15 raises the debugger and its surrounding workflow to the polish of a **professional IDE** — but with
**EmberTern's own visual language**, not a copy of Visual Studio or Rider (inspiration yes, imitation no,
decision §10-D3). It is a **coherent project**, not a bag of UI tweaks: every milestone has a stated purpose,
seams, and a Definition of Done.

**Non-goals (explicit):**
- **No global UI redesign** — D15.7 only *catalogues* oversized controls/fonts/spacing for a later stage.
- **No new execution path in the debugger** — the standing directive holds: fix UX in the **view + theme
  tokens**, never push logic into the debugger VMs/Core ([[feedback-debugger-ux-polish-backlog]]).
- **No debug-time performance profiler** — reversed at review (§8): harness overhead makes debug-time timing
  misleading as "procedure performance".

---

## 1. Architecture — the two classes

The single most important structural decision (ratified, user called it "bardzo trafiony"): every D15 milestone
is one of two classes.

| Class | Meaning | Rule | Live-fidelity? |
|---|---|---|---|
| **P — Presentation** | View / theme / tokens only. Zero change to the debug engine or Core. | Obeys the directive verbatim: no logic in VMs/Core; theme tokens in **both** dictionaries; `{DynamicResource}`. | No — nothing about Firebird conformance changes. |
| **F — Feature** | New data/Core surface (friendly-error data, inline-value policy, any engine telemetry). | Treated with full debugger rigour: additive, off-by-default where it touches the engine, and **live-verified** if it changes what the engine computes. | Yes, where it touches engine behaviour. |

**Class per milestone:** D15.1 **P** · D15.2 **P** · D15.3 **P** (+ tiny persistence) · D15.4 **P+F** ·
D15.5 **F** · D15.6 **F** (analysis/integration, no engine timing) · D15.7 analysis-only.

**Cross-cutting scope fact (do not miss):** **D15.1 is app-wide, not debugger-local.** The debugger's source
editor goes through the **same** `SqlEditorBehavior.Attach` seam (D3, gotcha #219 resolved) and the **same**
`FirebirdSql.xshd` + `FirebirdSql.Light.xshd` + semantic highlighter as the main SQL editor and every object
editor. Re-theming syntax **changes code colouring across the whole application**. This is a *ratified,
conscious* decision (wider QA, wider benefit) — plan D15.1 as an app-wide change with QA on the main editor,
object editors, and the debugger, in **both** themes.

---

## 2. Roadmap at a glance

| # | Milestone | Class | Cost (sessions) | Depends on | Order |
|---|---|---|---|---|---|
| — | **Script Executor — Step 0 (Probe)** | measure | ~0.5 (probe run + record) | — | **1** |
| **D15.1** | Editor Readability (syntax + current-line) | P | ~2 | — | **2** |
| **D15.2** | Toolbar Visual System + Error Bar | P | ~2 | icon system before use | **3** |
| **D15.3** | Launch & Entry Experience | P (+persist) | ~2 | — | **4** |
| — | **Script Executor Rewrite (Steps 1–6)** | correctness | ~4–5 | Probe results | **5** |
| **D15.4** | Expression UX + Friendly Errors | P+F | ~2 | — | **6** |
| **D15.5** | Inline Values | F | ~2 | D15.1 (renderer/token knowledge) | **7** |
| **D15.6** | Debugger Performance (integration analysis) | F | ~1–2 | Performance Analysis module | **8** |
| **D15.7** | Global UI Audit | analysis | ~1 (or in-the-background) | — | parallel |

**D15 total ≈ 11–13 implementation sessions** (+ the Script Executor track ≈ 5). Sequencing rationale in §11.

---

## 3. D15.1 — Editor Readability *(Presentation; app-wide)*

**Goal.** Kill the "colourful Christmas tree". Colour must *guide the eye*, not fill the background. Readability
first.

### 3.1 Current state (measured)
- `src/EmberTern.App/Assets/FirebirdSql.xshd` (dark) + `FirebirdSql.Light.xshd` paint **six hues at once**:
  DML keyword blue (`#5A8AC8` bold), DDL/DML-action purple (`#C586C0` bold), data types teal (`#4EC9B0`),
  built-in functions yellow (`#DCDCAA`), numbers green, strings salmon, comments green. The semantic highlighter
  layers object/column/parameter colours on top.
- Identifiers/variables have **no xshd rule** → they render in the default foreground (light on dark reads as
  "coloured" against the background).
- **Current-line:** `src/EmberTern.App/Completion/CurrentLineRenderer.cs` fills **only the statement span** with
  a translucent band (`DebugCurrentLineBrush`, tokens in both dictionaries) in layer `KnownLayer.Selection` —
  hence "looks like the default AvaloniaEdit selection" and reads weak.

### 3.2 Design — what draws the eye (ratified preference)
Reduce to ~3 tiers instead of 6. **Should attract attention:** **structural keywords**, **comments**,
**literals** (strings/numbers). **Should stay neutral (default text colour):** **variables and ordinary
identifiers**. Types/functions: neutral or a very restrained accent (decide during the seam; bias to neutral).
Principle: **colour = the exception that leads the eye**, never the field. Tune in **both** themes (separate
palettes already exist).

> Concretely, this means demoting the current 6-hue palette: collapse DML/DDL/action keywords toward one
> restrained "structural keyword" treatment; keep comments and literals legible-but-quiet; drop the function
> yellow and type teal to neutral or a single faint accent; leave identifiers on the foreground brush. Exact
> hex values are a seam decision, chosen against the running app in both themes — this doc fixes the *hierarchy*,
> not the swatches.

### 3.3 Design — current-line, fully rebuilt
Replace "span-in-Selection-layer" with an IDE-grade marker inspired by (not copied from) VS / Rider / VS Code:
- **Full line width** band (not just the statement span).
- **Calm blue** wash (~10–15% alpha), tuned in both themes (closes the old D4 backlog note: dark was "too
  aggressive", now "too weak" — hit **both**).
- **Subtle vertical bar in the gutter** (~2px, accent) marking the current execution line.
- Keep the renderer a pure client of the paused span (no analysis), repaint via `TextView.Redraw()` (gotcha
  #223 — never `InvalidateVisual()`).

### 3.4 Seams
- **A (syntax palette) — DONE (impl 2026-07-21; awaits user visual confirmation).** `FirebirdSql.xshd` +
  `.Light.xshd` collapsed to the 3-tier hierarchy: DML + DML-action + DDL keywords share ONE restrained blue
  (dark `#5A8AC8` / light `#0033B3`, bold); data types + built-in functions demoted to the neutral default
  foreground (dark `#D4D4D4` / light `#1F1F1F`); comments + literals unchanged (legible-but-quiet). Semantic
  tokens retuned **with** the xshd (not separately — Risk register): `EditorLocalBrush` → the default
  foreground in **both** dictionaries, so ordinary variables/locals are neutral; navigable objects
  (`IconColor_*`) and trigger-context variables (`EditorContextVariableBrush`) keep their restrained accent;
  columns were already paint-opted-out. Presentation-only (no VM/Core change); `FirebirdSyntaxTests` pin only
  keyword-block membership per category (not hex) → 18/18 green; build 0/0. Hex is a conservative start,
  tunable against the running app. App-wide QA (main editor / object editors / debugger, both themes) is the
  user-side confirmation step.
- **B (current-line):** rebuild `CurrentLineRenderer` for full-width + calm-blue + gutter bar; new/retuned
  tokens in both dictionaries; both themes. **NOT started.**

### 3.5 DoD
Code reads calmly in both themes on all editor surfaces; variables neutral; current line unmistakable but not
loud; no hardcoded colours; the UI Review Checklist (CLAUDE.md) passes. **Risk:** app-wide blast radius → QA
every editor surface; a palette that pleases dark can fail light (and vice-versa) — verify both explicitly.

---

## 4. D15.2 — Toolbar Visual System & Error Bar *(Presentation)*

**Goal.** A toolbar that reads like a modern IDE **in EmberTern's own visual language**, and error messages
that never shove the toolbar.

### 4.1 Current state (measured)
- `src/EmberTern.App/Views/DebuggerTabView.axaml` toolbar: every button is `Classes="flat"` with **Unicode
  glyphs** in `Content` (`▶ Continue`, `⤵ Into`, `↷ Over`, `⤴ Out`, `⇥ To Cursor`, SUSPEND, Next Iter, Loop
  Exit, `■ Stop`, `↻ Restart`) + a Break-on-exception `CheckBox`. Uniform grey, **no visual hierarchy**.
- The Faulted message renders **inside the same `DockPanel` as the buttons** (`StatusText`, `Classes.fault`,
  `TextTrimming=CharacterEllipsis`) → it shifts the toolbar and truncates.

### 4.2 Design — the icon SYSTEM (design the whole family, not single icons)
Ratified: **do not copy VS/Rider; build EmberTern's own language.** Define the system first, then draw every
icon inside it. Reuse the existing `SvgIcon` control + `Icon.*` geometry resources (do **not** hand-pick
per-icon Unicode).

**System tokens (fix these before drawing anything):**
- **One stylistic family:** consistent **stroke weight**, consistent **geometry** (cap/join style, corner
  radius), one **optical grid** (e.g. a single px grid for all debugger icons), single-hue default.
- **Hierarchy through restraint:** **Continue = the primary action** (a discreet accent), **Into/Over/Out = a
  coherent equal-weight directional trio** (distinct shapes, not three near-identical arrows — the D4 backlog
  item), **Stop = a muted destructive accent**, **Break-on-exception = a visually separated toggle**. Colour
  only on load-bearing actions; everything else neutral (or D15.1 turns the toolbar into a Christmas tree too).

**Action → icon concept (a coherent set):** Continue = play/▷ (accent); Step Into = arrow curving *into* a
line; Step Over = arc hopping *over* a node; Step Out = arrow curving *out*; Run to Cursor = arrow to a
caret/target line; Run to SUSPEND = arrow to a pause-bar/row marker; Next Iteration / Loop Exit = loop-arrow
variants (re-enter vs leave); Stop = filled square (muted); Restart = circular arrow; Break-on-exception =
pause-on-burst toggle; breakpoint gutter dot = filled circle.

### 4.3 Design — the app / debugger icon metaphor (ratified: not a classic bug)
The debugger is not only for bug-hunting — it is for **analysing behaviour, learning code, observing
execution**. Replace the "bug" metaphor with one about **execution flow / tracing / observation**. Candidates:
1. **Playhead-on-a-flow-path (recommended primary)** — a small filled marker travelling along a branching
   path line; reads as "you are here in the program's execution", i.e. tracing/observing flow.
2. **Step-trail / footprints along a path** — stepping through execution.
3. **Magnifier over a flow line** — analysis of execution (risks re-implying "search for bugs").

Recommendation: pursue #1 as the primary, keep #2 as fallback; final art is a seam decision, but the *metaphor*
(execution tracing, not a bug) is fixed here.

### 4.4 Design — the Error Bar (own row, never shifts the toolbar)
A **separate thin bar below the toolbar** (its own `Grid.Row`), shown **only** on error
(`IsFaulted` / `IsPausedOnException`). One line, `TextTrimming`. Adds **copy** and **expand-to-full-message**
(FB messages are often multi-line). Dismissable. The toolbar row keeps a fixed height regardless of error
state (the current in-row `StatusText` is the bug being fixed).

### 4.5 Seams
- **A (icon system + toolbar):** define the SVG system tokens; author the `Icon.*` set; rebuild the toolbar
  buttons onto SvgIcon with the Continue/step/Stop hierarchy; new theme accent tokens if needed (both dicts).
- **B (app/debugger metaphor icon):** design + wire the new debugger tab/entry-point icon.
- **C (error bar):** extract the fault message into its own collapsible row with copy + expand.

### 4.6 DoD
Toolbar has clear primary/secondary/toggle hierarchy in EmberTern's own style; icons share one geometry;
debugger icon is a flow/trace metaphor; an error shows in its own bar without moving any button; copy + expand
work; both themes; no hardcoded colours. **Risk:** SVG authoring is fiddly; "own language" is subjective →
converge with the user on the icon-set sketch before mass-authoring.

---

## 5. D15.3 — Launch & Entry Experience *(Presentation + tiny persistence)*

**Goal.** A compact, keyboard-first launch, and fast repeat-runs.

### 5.1 Current state (measured)
- Launch panel (`DebuggerTabView.axaml`): `StackPanel Spacing=14, MaxWidth=720`; `ParamRowTemplate` grid
  `180,130,40,*`, `MinHeight=30`, `Margin=0,3` → large rows, "ERP-like", empty space.
- Parameter **history already exists** (`Parameters.History` / `SelectedHistory`, reusing Smart-Parameters).
- Isolation: `ComboBox Width=280` + a long technical note (`DebuggerIsolationNote`), eats vertical space.
- **No launch shortcut, no Enter-to-launch** (the launch `StackPanel` has no `KeyBindings`).

### 5.2 Design
- **Compact form:** smaller rows (reduce `MinHeight`/`Spacing`), controls closer to editor scale, less empty
  space. Name + type on one line, value beside.
- **Type subordinate to name (ratified):** the **name is primary**; the **type is smaller, greyed, less
  dominant** (`SubtleForegroundBrush`, smaller font) — it must not compete with the name.
- **NULL affordance:** today a dedicated `40px` `IsNull` checkbox column. Keep the capability but make it a
  **compact inline toggle/clear** in the value field, not a standing column (reclaims width, less noise).
- **Transaction Isolation — practical description FIRST, technical name later (ratified):** collapse into an
  **Advanced** section (closed by default — most users never change it) or a compact top-right control. Rewrite
  the note so it says **what the option changes** before naming the level, e.g. *"You see data other sessions
  commit while you debug (Read Committed)"* / *"A consistent snapshot from the moment you start, unchanged to
  the end (Snapshot)"*. Default stays Read Committed.
- **Start Debugging — keyboard shortcut (ratified):** add a launch shortcut (recommend **F5** in the launch
  context — consistent with "F5 = Continue/go" in the debug view; alternative `Ctrl+Enter`) + **Enter-to-launch**
  when focus is in a parameter field. **Focus management (added):** after launch, focus lands on the editor
  `TextArea` so `F10/F11/…` work immediately without a click.
- **Quick Relaunch (YES) — favorites (DEFERRED):** one gesture/shortcut to re-launch with the last parameters.
  **Named favorite configurations are deferred** — parameter history already covers most cases (ratified §9).

### 5.3 Seams
- **A (compact form + type styling + NULL affordance):** view-only re-layout.
- **B (isolation → Advanced + plain-language copy):** view + `UiStrings` rewrite.
- **C (launch shortcut + Enter-to-launch + post-launch focus):** view/keybinding + focus.
- **D (Quick Relaunch):** command reusing the existing param-history/last-values; minimal persistence, no new
  favorites store.

### 5.4 DoD
Launch is compact and keyboard-drivable end-to-end; type never dominates name; isolation is out of the way with
a plain-language description; Quick Relaunch works; both themes. **Risk:** low; the only persistence is
Quick-Relaunch's "last values", which history already stores.

---

## 6. D15.4 — Expression Surfaces UX & Friendly Errors *(P + F)*

**Goal.** The Immediate / Watches / Breakpoint-condition inputs tell the user what to type, and errors are
friendly, not raw SQL.

### 6.1 Current state (measured)
- Placeholders are terse (`DebuggerImmediateWatermark`, `DebuggerWatchWatermark`,
  `DebuggerBreakpointConditionWatermark`).
- Errors are raw: Immediate `LatestEvaluation.ResultText` in `ErrorBrush`; Watch `ValueText` in `ErrorBrush`.

### 6.2 Design
- **P — hints:** richer placeholders + short **examples of valid expressions** (e.g. `v_counter * 2`,
  `v_status = 'OK'`, `char_length(v_text)`), possibly under the panel empty-state.
- **F — friendly errors:** translate to a category — *unknown variable*, *syntax error*, *unclosed
  parenthesis*, *unsupported function* — with a suggested fix where possible. **Reuse the existing
  `EditorLanguageService` (Lexer + Parser + `DiagnosticsEngine`) for LOCAL syntax/unknown-name pre-validation
  BEFORE sending the `EXECUTE BLOCK`** — this is exactly the D5 seam (b) backlog item ("Immediate should
  pre-validate syntax locally via the existing Language Service; semantics/execution stay the server's"). For
  errors that reach the server, map `FbException` (SQLSTATE/GDS) to a friendly message reusing
  `DebugErrorMapper` — never parse the raw message text.

### 6.3 Seams
- **A (P):** placeholders + examples (pure view/UiStrings).
- **B (F):** local pre-validation via Language Service + friendly mapping; live-verify that a valid expression
  still evaluates identically (no behaviour change to the harness).

### 6.4 DoD
The user knows what to type; a bad expression yields a friendly, categorised message (with a fix hint when
possible) rather than raw SQL; valid expressions behave exactly as before. **Risk:** the local validator must
be advisory only — never block a fragment the server would accept (§F: the server owns semantics).

---

## 7. D15.5 — Inline Values *(Feature)*

**Goal.** Show current variable values next to the code, **without cluttering the editor**.

### 7.1 Design (ratified visibility rule)
Show **only**: (a) **variables used on the current line**, **or** (b) **variables changed since the previous
step**. Never all variables. Render as **greyed end-of-line annotations** (`nazwa = wartość`), for the current
frame's assigned values only, on visible lines.

### 7.2 AvaloniaEdit mechanism
Two viable approaches — decide in the seam:
- A `VisualLineElementGenerator` emitting trailing end-of-line elements, **or**
- An `IBackgroundRenderer` drawing annotation text in the trailing whitespace / right margin (consistent with
  the existing `CurrentLineRenderer` / `SquiggleRenderer` / `RelatedElementsRenderer` family).
Hard rule: **must not shift the source text**. Data comes from the same paused-frame roster the Variables panel
already projects (`Frame.Values` + model roster) — **zero new analysis**; the "which variables changed" set is
the same signal the Variables change-highlight already uses (`FrameValues.Snapshot()` diff).

### 7.3 Seams
- **A:** the renderer/generator + paused-state integration (draw current-line-used values).
- **B:** the "changed since last step" set + visibility policy + tuning + both themes.

### 7.4 DoD
Inline values appear only for current-line-used or just-changed variables, never shift text, greyed and
unobtrusive, both themes. Depends on **D15.1** (shared renderer/token knowledge). **Risk:** over-showing →
clutter; keep the visibility rule strict.

---

## 8. D15.6 — Debugger Performance *(Feature — direction CHANGED at review)*

**Reversed decision (ratified):** **do NOT measure debugger step time.** Because every step runs as a separate
`EXECUTE BLOCK` harness (separate round-trips + harness overhead), debug-time timing is **not representative of
real procedure performance** — it would mislead. Real profiling is the job of the existing **Performance
Analysis** module (`docs/history/10`).

**Design.** Analyse **integration with the existing Performance Analysis** rather than building a debug-time
profiler:
- Provide a path from the debugged routine to the existing Performance Analysis (profile the real procedure
  once via the established module — real plan + per-table reads), reusing `PerformancePanelView`/VM (already
  hosted as a peer tab in the object editors, so the pattern is proven).
- **If** debug-time metrics are ever added later, they **must be labelled unmistakably as "debugger runtime"
  (with harness overhead), NOT real procedure performance** — a permanent labelling requirement.

**Seam.** Analysis + a reuse-of-Performance-Analysis entry point; no engine timing. **DoD:** the user can reach
a real, honest performance profile of the debugged routine through the existing module; no misleading debug-time
number is presented as "procedure performance". **Cost ~1–2 sessions** (reduced from the earlier 3 — the
timing-capture engine work is dropped). **Risk:** scope creep back toward debug-time timing — resist.

---

## 9. D15.7 — Global UI Audit *(analysis only)*

Per the ratified decision, **no global redesign in D15** — only **catalogue** oversized controls / fonts /
spacing (launch form, dialogs, toolbars, grids) into a document feeding a future "EmberTern Visual Refresh"
stage. Naturally coupled to D15.1 (colour is also global). Best run **in the background** — collect
observations during every D15 seam. **Deliverable:** an inventory doc, not code.

---

## 10. Cross-cutting design decisions (ratified) + rationale

- **D1 — Presentation vs Feature split is the organising principle.** Rationale: keeps most of D15 fast and
  regression-free (P), while subjecting the three real features (friendly errors, inline values, any
  telemetry) to full debugger rigour (F).
- **D2 — Readability first; colour is the exception.** Attract the eye with structural keywords / comments /
  literals; keep variables + ordinary identifiers neutral. Rationale: 6 simultaneous hues defeat reading.
- **D3 — EmberTern's own visual language.** Inspiration from VS/Rider/VS Code, not imitation. Rationale: a
  distinct product identity; copying produces an incoherent pastiche.
- **D4 — Debugger metaphor = execution tracing, not a bug.** Rationale: the debugger is for analysis /
  learning / observation as much as bug-hunting.
- **D5 — No debug-time performance profiler.** Rationale: harness overhead makes debug-time timing misleading;
  integrate with the real Performance Analysis instead; any future debug metric must be labelled "debugger
  runtime".
- **D6 — Favorites deferred; Quick Relaunch only.** Rationale: parameter history already covers most repeat
  cases; add favorites only if real usage asks.
- **D7 — D15.1 is app-wide and that is intentional.** Rationale: one highlighting pipeline serves every editor;
  fixing it fixes everywhere (wider QA is the accepted cost).
- **D8 — The "don't push logic into VM/Core" directive stands for all P seams.** Rationale: the D1–D13
  responsibility split must survive the polish pass.

---

## 11. Priorities & sequencing

**Ratified order** (with my endorsement — the sequence is sound; no change recommended):

1. **Script Executor — Step 0 (Probe).** It is **measurement, not implementation**, it is already written and
   builds, and it gates a real correctness-debt decision (§2.2b self-block is *reasoned, not measured* — and
   the project's history says never trust an unmeasured Firebird inference: #213/#214/#215 were all falsified).
   Cheap, unblocking, zero implementation risk → first.
2. **D15.1 — Editor Readability.** Highest daily value, lowest risk, and **app-wide** — so it should land
   before other editor-surface work builds on the palette.
3. **D15.2 — Toolbar + Error Bar.** High visibility on the freshly-shipped debugger; pure P.
4. **D15.3 — Launch Experience.** Daily workflow; repeated launches; pure P + tiny persistence.
5. **Script Executor Rewrite (Steps 1–6).** A self-contained correctness-debt block, slotted after the quick
   visible wins so the polish momentum isn't lost but the debt gets paid before the heavier D15 features.
6. **D15.4 — Friendly Errors.** First of the feature-bearing D15 seams.
7. **D15.5 — Inline Values.** After D15.1 (shares renderer/token knowledge).
8. **D15.6 — Performance (integration).** Lightest now that debug-time timing is dropped; last.

D15.7 (Global UI Audit) runs **in the background** throughout.

**Why not reorder:** the only defensible alternative — pulling the Script Executor Rewrite fully ahead of D15
— was rejected because the debugger just shipped and the visible readability/toolbar wins keep the momentum and
are app-wide foundations; the rewrite is self-contained and loses nothing by following them, as long as **Step
0 (the probe) runs first** (it does, at position 1).

---

## 12. Dependencies

- **D15.5 → D15.1** (renderer/token knowledge; inline values reuse the current-line renderer family).
- **D15.2 seam A → its own icon-system tokens** (design the system before authoring/using icons).
- **Script Executor Rewrite (Steps 1–6) → Step 0 probe results** (blocking; freezes the `Sequenced` design).
- **D15.4 (F half) → `EditorLanguageService` + `DebugErrorMapper`** (both exist; reuse, don't rebuild).
- **D15.6 → Performance Analysis module** (exists; integrate).
- All others are independent.

---

## 13. Risks register

| Risk | Milestone | Mitigation |
|---|---|---|
| App-wide colour change looks good in one theme, bad in the other | D15.1 | Verify **both** themes on all editor surfaces; the D4 backlog already proved this bites. |
| Palette re-inflates into a Christmas tree via semantic tokens | D15.1 | Retune `Themes/Colors.axaml` semantic-highlight tokens together with the xshd, not separately. |
| "Own visual language" is subjective; mass-authoring before agreement | D15.2 | Converge on an icon-set sketch with the user before authoring the full family. |
| Local expression pre-validation blocks a fragment the server would accept | D15.4 | Advisory only; the server owns semantics (§F); never hard-block. |
| Inline values clutter the editor | D15.5 | Strict visibility rule (current-line-used OR changed-since-last-step); never all. |
| Debugger Performance drifts back to misleading debug-time timing | D15.6 | Decision D5: no debug-time profiler; any future debug metric labelled "debugger runtime". |
| Script Executor §2.2b self-block is an unmeasured inference | Script Executor | Step 0 probe first; do not freeze the design until measured. |
| A P seam accidentally pushes logic into a VM/Core | all P | Directive D8; keep the D1–D13 split. |

---

## 14. Script Executor Rewrite — parallel track (pointer)

Fully analysed and ratified in [script-executor-transaction-review.md](script-executor-transaction-review.md).
Summary for planning:
- **Why KNOWN-BROKEN:** the whole script runs in **one transaction**, and Firebird cannot use an object it
  created but has not committed (#213) → any create-then-populate migration fails on statement 2. It also
  ignores Developer Mode (runs NOWAIT).
- **Root cause:** transactions & commit boundaries — **not** the parser or classifier (both are correct and
  kept).
- **Target model:** `Sequenced` mode (§5) — one lane, one transaction at a time, commit boundary after each DDL
  segment (like `SET AUTODDL ON`); DDL segments WAIT-bounded + Dev-Mode-aware, DML segments NOWAIT. Atomicity
  is consciously traded and stated in the UI; `Manual` stays for true all-or-nothing.
- **Plan (§6):** **Step 0 — Probe (blocking, measurement only)** → Step 1 doc-truth pass → Step 2 Dev-Mode text
  → Step 3 `Sequenced` Core (segment planner over the classifier) → Step 4 Firebird (per-segment TPB + commit +
  committed-segments reporting) → Step 5 App (third mode, up-front rejection of mixed `Manual`, segment
  boundaries in the grid) → Step 6 verify live on the lab.
- **Immediate action:** run **Step 0** and record results in the review doc's §7 evidence log before starting
  the rewrite.

---

*End of D15 design. Nothing here is implemented. A future session starts a milestone from its § + seam list.*
