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
| — | ~~**Script Executor — Step 0 (Probe)**~~ **✅ DONE** | measure | — | — | **1** |
| **D15.1** | Editor Readability (syntax + current-line) — **Seam A DONE** | P | ~2 | — | **2** |
| **D15.2** | Toolbar Visual System + Error Bar | P | ~2 | icon system before use | **3** |
| **D15.3** | Launch & Entry Experience | P (+persist) | ~2 | — | **4** |
| — | ~~**Script Executor Rewrite (Steps 1–6)**~~ **✅ COMPLETE (0–6, live-verified 2026-07-21)** | correctness | — | — | **5** |
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
  columns were already paint-opted-out. Presentation-only; `FirebirdSyntaxTests` pin only keyword-block
  membership per category (not hex) → green; build 0/0. Hex is a conservative start, tunable against the
  running app. App-wide QA (main editor / object editors / debugger, both themes) is the user-side step.
  **Refinement after user QA (2026-07-21):** collapsing *all* keywords to one blue erased the SQL-vs-PSQL
  language hierarchy (SELECT/FROM and BEGIN/END/WHILE/DECLARE looked identical). Per the user, SQL and PSQL
  must read as two groups **without** returning to the Christmas tree. Resolved by **splitting the keyword
  catalog** (not duplicating a keyword list in the xshd — that would defeat the single-source guard): the
  Core `FirebirdSyntax` `Statement` category was split into `Statement` (DML-action + DDL + transaction +
  constraints, **blue** — SQL) and a new `Psql` category (BEGIN/END/IF/WHILE/FOR/DO/EXIT/SUSPEND/LEAVE/
  DECLARE/VARIABLE/CURSOR/EXECUTE/BLOCK/STATEMENT/RETURN/RETURNS) painted a **second restrained accent —
  violet** (dark `#A88FD4` / light `#6C4C9E`, bold). `SqlKeywordCategory`/`CategoryOf`/`KeywordsInCategory`
  are consumed only by `FirebirdSyntax` + the xshd drift-guard test (the lexer's `IsKeyword` and completion
  are category-agnostic), so the re-partition changes only colour grouping — build 0/0; syntax 18/18 +
  lexer/completion/semantic 229/229 green.
  **Palette tuning after light-theme QA (2026-07-21):** (a) the light PSQL violet was too pale → deepened,
  same hue (`#6C4C9E` → `#5D30A6`); (b) demoting data types all the way to neutral went too far — types
  (INTEGER/VARCHAR/TIMESTAMP/NUMERIC/… incl. domain-typed columns) matter in declarations, so they regain a
  **discreet muted-teal** accent (dark `#5FA894` / light `#2C7A70`); built-in functions + operators + ordinary
  identifiers stay neutral. Still a controlled palette (blue SQL / violet PSQL / soft-teal types + quiet
  literals), not a Christmas tree. Pure xshd colour values (no catalog/test change).
  **Bolder pass after further light QA (2026-07-21):** the light tuning was too timid — data types → a strong
  elegant teal `#0F766E` (immediately recognizable, distinct from the blue keywords); comments → a cool
  gray-green `#6E847A` (modern-IDE feel, no more yellow "olive").
  **Built-in functions coloured after QA (2026-07-21, user-requested — reverses the "functions neutral"
  recalibration):** QA found built-in functions (ROUND/COALESCE/CAST/TRIM/UPPER/LOWER/SUBSTRING/
  CURRENT_TIMESTAMP/…) reading as plain text. **Root cause was NOT a classifier/list gap** — every such
  function is already catalogued in `FirebirdSyntax.FunctionWords` (category `Function`) and in the xshd
  `<Keywords color="Function">` block; the only cause was the D15.1 recalibration painting the `Function`
  colour neutral (`#D4D4D4` dark / `#1F1F1F` light). The user reversed that: `Function` now gets a soft
  elegant **yellow** accent (VS Code-style — dark `#DCDCAA` / light `#795E26`), a clear fourth hue distinct
  from SQL blue / PSQL violet / type teal; **operators** stay neutral. Pure Presentation (only the two
  `<Color name="Function">` values + header comments) — `FirebirdSyntaxTests` pins keyword-block *membership*
  per category, never the hex, so no test change; build 0/0, 52 syntax/highlight tests green. A **user-defined**
  function (e.g. `dziel(…)`) stays neutral by design — colouring those needs semantic resolution, a separate
  feature. (The Firebird 5 built-in list could later be extended with more functions — optional follow-up,
  not needed by any reported case since every example was already catalogued.)
- **B (current-line rebuild) — DONE (impl 2026-07-21; awaits user visual confirmation).** Treated as the
  *definitive review* of current-line rendering, not just a recolour. **Scope correction (important):** the
  current-line marker is **debugger-only** — `CurrentLineRenderer` is attached solely in `DebuggerTabView`,
  NOT in the shared `SqlEditorBehavior.Attach`, and no editor uses AvaloniaEdit's `HighlightCurrentLine`. So
  "current line" = the debugger's **paused statement**; Seam B's visual change is confined to the debugger's
  source editor (the app-wide part of D15.1 was the palette, Seam A). As-built: the old amber statement-span
  band (`#55…`, "too aggressive" dark / "too weak" light) is replaced by a calm **full-line-width blue wash**
  (`DebugCurrentLineColor` ~16% dark `#285A8AC8` / ~11% light `#1C0033B3`) **plus a crisp ~2.5px accent bar**
  at the line's left edge (new token `DebugCurrentLineBarBrush`, both dictionaries). The renderer now draws
  as the **backdrop** — `Attach` does `BackgroundRenderers.Insert(0, …)` so the squiggle / related-element
  renderers (added earlier) and the text selection read ON TOP; the low alpha never masks glyphs or syntax
  colour. Per-visual-line geometry from `BackgroundGeometryBuilder.GetRectsForSegment` (Y/Height reused, X
  spanned to `textView.Bounds.Width`) keeps it correct under **word wrap** (one band per visual line),
  **folding** (a hidden line has no geometry ⇒ not painted) and **variable line heights**. Repaint unchanged:
  `TextView.Redraw()` on `DebugMarkersChanged` (gotcha #223, never `InvalidateVisual()`). Files:
  `CurrentLineRenderer.cs` + `Themes/Colors.axaml` (both dicts). Pure Presentation; hex is tunable against
  the running app. Build 0/0; +1 headless pin (`CurrentLineRenderer_Attach_IsBackdropBelowOtherRenderers` —
  backdrop ordering + a non-throwing full-line Draw); **5098 tests green**; smoke clean. **D15.1 COMPLETE**
  (Seam A + A2 + A3 + functions + B); next milestone is **D15.2**.

### 3.6 Seam A2 — domain-as-type resolution *(Feature)* — DONE (2026-07-21; awaits user visual confirmation)
A follow-up found by QA: a builtin type (`VARCHAR`) was coloured but a **domain used as a type** (`T_STRING500`)
stayed neutral, because the binder emitted **no reference** for a type-position name — the type was only a
string on the symbol. That is a semantic-model gap, not a colour value, so it is a small **Feature** seam (it
changes the model's reference set → feeds hover / Ctrl+Click / diagnostics). As-built:
- **Binder (`SemanticBinder.Psql`):** a new `BindDomainTypeReference` emits a `SchemaObject` reference for the
  type-position identifier in `DECLARE VARIABLE`, procedure/function **parameters**, `RETURNS (…)` columns, and
  a scalar `RETURNS <domain>` — **only** when it resolves to a `Domain` via `ResolveObject`/metadata (builtins
  are catalogued keywords, never identifiers; an unknown name emits nothing → no false reference, no false
  diagnostic — §0 / "prefer silence"). The scan is bounded to the declaration/param segment so it can never
  reach the routine body.
- **Colour (App):** a resolved domain reference paints like a **SQL type** — new theme brush
  `EditorDataTypeBrush` (both dictionaries, mirroring the xshd `DataType` hex) mapped in `SemanticHighlighter`
  for `SchemaObject` of kind `Domain`; every other object keeps its tree-icon palette.
- **Free participation:** because a domain now resolves to a real reference, hover, Ctrl+Click (go to the
  domain) and diagnostics work through the existing model — no extra wiring.
- Build 0/0; +4 `SemanticModelTests` (declare / parameter+returns / builtin-no-ref / unknown-no-ref) + the
  semantic/highlight/diagnostics/nav/hover suites green.

**Still deferred (user, "w dalszej perspektywie"):** giving domains their **own distinct accent** (visually
different from plain SQL types) — a pure presentation change on top of the now-existing resolution; scope it
when wanted.

### 3.7 Seam A3 — DDL preview highlighting parity (bug fix)  — DONE (2026-07-21; awaits user visual confirmation)
A regression found by QA: the object editors' **DDL tab** (and the sidebar DDL preview) coloured differently
from the Editor tab. Root cause — **app-wide highlighting has two layers, and only one reached the DDL
preview.** Every DDL editor already got the lexical **XSHD** layer (each view's `ApplyEditorTheme` applies the
theme-matched `FirebirdSql[.Light].xshd` to `_ddlEditor`), but the **semantic** layer (`SemanticHighlighter`,
which colours schema objects + domains) is installed only by `SqlEditorBehavior.Attach`, and the read-only DDL
editors never called it — so objects/domains stayed the default foreground while the Editor tab coloured them
(the D15.1 domain teal made the gap newly obvious). Fix: a new **highlight-only** attach
`SqlEditorBehavior.AttachReadOnlyHighlighting(editor)` adds the `SemanticHighlighter` layer **without** the
interactive machinery (completion / squiggles / ergonomics) a read-only preview must not have — it rebuilds the
model from the editor text + the window's metadata snapshot on text-change and on metadata load, resolves the
`MainWindowViewModel` from the visual tree (so each call site is one line, no VM plumbing), and is leak-free
(metadata subscription released on detach). Wired into **all 11 DDL previews** (MainWindow sidebar + Table /
Procedure / Function / Trigger / View / Package / Domain / Generator / Exception / Index editors). Now the DDL
surface colours objects/domains like the Editor tab, closing the app-wide gap. Additive (only adds a colour
layer to read-only editors; if metadata isn't warmed it shows nothing extra — no behaviour change). Build 0/0;
semantic/highlight/syntax suites + the headless editor-attach probe green. **Minor known gap:** a trigger DDL
preview does not resolve `NEW`/`OLD` context vars (no trigger-table context provider on the read-only path) —
those stay neutral in the preview; deferred, not in scope.
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
- **A (icon system + toolbar) — DONE (impl 2026-07-21; awaits user visual confirmation).** The icon
  **system** decision was ratified with the user: EmberTern does **not** build a parallel icon family — it
  extends the existing (Lucide-derived) `SvgIcon` system as the "EmberTern Icon System", `.svg` = canonical
  source, `IconGeometries.axaml` = the runtime representation, reuse-before-create. The debugger toolbar was
  the **last Unicode-glyph holdout** in the app; it now renders on `SvgIcon`. **Icon set** (concept board
  approved, incl. two refinements: Next Iteration = a two-arrow cycle so it no longer collides with Restart,
  which moved to a **skip-to-start** metaphor; Break on Exception = a **stop-octagon + `!`**, not the earlier
  energy-bolt): 9 new geometries `Icon.StepInto/StepOver/StepOut/RunToCursor/RunToSuspend/NextIteration/
  LoopExit/Restart/BreakException` authored under `Assets/Icons/Debug/*.svg` + mirrored in
  `IconGeometries.axaml`; Continue reuses `Icon.Play`, Stop reuses `Icon.Stop`. **Colour = hierarchy, not
  decoration** (ratified mapping, existing tokens + ONE new): Continue → `AccentIconBrush` (the single
  primary); Into/Over/Out + Run-to-Cursor + Run-to-SUSPEND + Restart → neutral (inherit `NeutralIconBrush`);
  Next Iteration + Loop Exit → a new shared **`DebugLoopIconBrush` (teal, both dicts)** marking the
  "loop-operations" category — **teal, not the D15.1 PSQL-keyword violet** (that would cross the syntax domain
  into the toolbar); Stop → `DangerIconBrush`; Break on Exception → `WarningIconBrush` (a behaviour mode, not
  a destructive action). Labels stay neutral text so only the icon carries the category → 6 neutral / 4
  meaning-bearing hues, calm after D15.1. The three now-unused `Debugger*Content` glyph strings were removed;
  the results-empty hint reworded off the old glyph. Build 0/0; +1 headless pin (all 11 toolbar geometries +
  `DebugLoopIconBrush` in both themes resolve at runtime); 5099 tests green; smoke clean.
- **B (app/debugger metaphor icon) — DONE + user-accepted 2026-07-22.** `Icon.Bug`
  replaced at all three debugger entry points (Procedure + Trigger editor toolbar "Debug…" buttons + the
  Debugger tab, which had been misusing the Continue `Icon.Play`) with a single unified debugger identity mark.
  **First metaphor (playhead-on-a-branching-path) was authored, shipped, and REJECTED by the user** — not a
  quality/SVG problem but the metaphor itself failed to read as "debugger / execution tracing" (less legible
  than the old bug). **Ratified replacement (user-directed): a two-colour composite — a blue Play triangle (the
  execution pointer, dominant ~80–85%) + a small red breakpoint dot (~20–25% dia) nested into its lower-right,
  overlapping the tip so the two read as ONE "Start Debugging" glyph.** Two colours + a filled dot cannot be a
  single stroked `SvgIcon`, so it is a dedicated composite control `Controls/DebuggerIcon.cs` with its
  ControlTheme in `Themes/IconGeometries.axaml`; both colours are **reused theme tokens** (`AccentIconBrush` +
  the very `DebugBreakpointBrush` the gutter breakpoint uses), both dicts, same idiom (24×24, 2px stroke, round
  caps/joins). The tab template branches on a new presentation-only `WorkspaceTabViewModel.IsDebuggerTab`. Pure
  Presentation. Build 0/0; pin test extended (constructs `DebuggerIcon` + pins both brushes in both themes);
  tests green; smoke clean. **NOTE: the geometry redesign for the debugger identity stays an OPEN topic for the
  future Visual Polish sprint** — the current composite is the accepted interim; if Visual Polish designs a
  better mark, `DebuggerIcon`'s ControlTheme is the single place to change.
- **C (error bar) — DONE (impl 2026-07-22; a UX refinement applied after first QA — see below). D15.2
  COMPLETE.** The fault message moved out of the toolbar status line into its **own row** (`Grid.Row=1`, root
  grid now `Auto,Auto,*`) shown only on a fault OR a Break-on-Exception pause (`ShowErrorBar`); hidden it
  collapses to zero height so the toolbar never moves. Calm, not dominating: `PanelBrush` background + a thin
  3px `ErrorBrush` left stripe + an error-toned icon carry the signal (no loud fill). **Shows the FULL message
  by default** (user QA refinement — FB errors are short (2–6 lines) and are exactly what the user wants to
  read, so default-collapse gave no benefit): the message sizes to its content, capped at ~8–10 lines
  (`MaxHeight=190`) then scrolled so a rare long one never dominates the editor. **Copy** (clipboard, in the
  view code-behind — Avalonia concern), **Expand/Collapse** is the opt-in one-line **safety valve** for a very
  long message (`ChevronsDown`/`ChevronsUp`), **Dismiss** (`Icon.X`). Font 12 + `LineHeight=18` for easy
  reading (the bar keeps its thin, no-fill character). The status line is now a **short, fixed-height
  headline** (`DebuggerStatusFaulted`), the full Firebird message lives only in the bar — the "current in-row
  `StatusText`" bug fixed. Pure Presentation: the VM projects `ErrorDetail`/`ShowErrorBar` (over the engine's
  `DebugError`) + owns the expand/dismiss view-state; the engine is untouched. Build 0/0; +2
  `DebuggerTabVmTests`; smoke clean.

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
- **Start Debugging — keyboard (ratified 2026-07-22):** **F5 = Start Debugging** in the launch panel (F5 is
  always "Go"; the debug view's F5 = Continue is a different phase → no conflict). **Enter launches ONLY from
  the last parameter field or the Launch button** (user refinement — Enter from any field was rejected); every
  other field keeps its natural Enter (a multiline value box → newline). **Focus management:** after launch,
  focus lands on the editor `TextArea` so `F10/F11/…` work immediately without a click; on the launch panel,
  focus lands on the first parameter field (or the Launch button when there are none).
- **No-decision fast path (ratified 2026-07-22, changed from the earlier "focus Launch" answer):** if the user
  has **nothing to decide** before launching — a non-trigger routine with **no input parameters** and a
  **clean pre-flight** — the launch panel is **not shown at all**: Debug → Preparing → session. A pre-flight
  note (a §4.6 data-safety warning) or any parameter/trigger context keeps the panel. Isolation is never a
  required decision (defaults to Read Committed, lives in Advanced). Principle: *don't show a step the user has
  no decision in.* A future "Debug with options…" path could force the panel, but must not slow the default.
- **Quick Relaunch (YES) — favorites (DEFERRED):** one gesture/shortcut to re-launch with the last parameters.
  **Named favorite configurations are deferred** — parameter history already covers most cases (ratified §9).
- **Debugger discoverability — Debug button on the Package "Members" tab toolbar (BACKLOG, added 2026-07-22
  from Seam B QA; NOT yet implemented).** Today the only way to debug a package member is the Members-tab
  **context menu** — a new user has virtually no chance of discovering it. Add a **Debug** button to the Members
  tab toolbar, **disabled by default**, that enables only when the selected member is a debuggable kind
  (procedure / trigger / function) and stays disabled for the rest. Reuses the existing member-launch path
  (`PackageDetailTabViewModel.DebugMemberRequested`); pure App/UX. *(Blocked in part by the function-debugging
  gap below — a function member can only enable this once the debugger supports functions.)*
- **Debugging standalone/stored PSQL FUNCTIONS as a debug ROOT — FUNCTIONAL GAP (BACKLOG, added 2026-07-22
  from Seam B QA; NOT a D15 item — a debugger feature, recorded here for the plan).** The debugger today
  launches **procedures, triggers, and packaged procedures** as a root; a **standalone (or packaged) function
  launched as its own debug session is NOT supported** (a §F boundary — cf. "a function-as-root is out of
  scope" / a package FUNCTION call is not modelled on the call side, gotcha #233). Note this is distinct from
  D9, which already makes a **local** `DECLARE FUNCTION` a faithful step-into/over frame *within* a routine —
  the gap is a function as the **entry point**. Needs its own debugger milestone (a function root frame +
  return-value surface), sequenced with the other debugger work, before the Members-tab Debug button can light
  up for function members.

### 5.3 Seams
**Ratified order (2026-07-22): C → A → B → D → E** (keyboard/flow first — the biggest daily-workflow win at
the smallest scope).
- **C (keyboard-first launch + focus + no-decision fast path) — DONE 2026-07-22 (impl, awaits visual
  confirm).** F5 = Start Debugging in the launch panel; Enter launches only from the last parameter field or
  the Launch button (a tunnelled handler on the panel; multiline value boxes keep Enter = newline); after
  launch focus → editor `TextArea` (gotcha #225), on ready-to-launch focus → first parameter field / Launch
  button. **No-decision fast path:** a non-trigger routine with no parameters and a clean pre-flight
  auto-launches (skips the panel) via `ShouldAutoLaunch()` in `PrepareAsync`. Pure Presentation + one VM
  guard; engine untouched. Build 0/0; +3 `DebuggerTabVmTests` (auto-launch, with-params keeps panel, pre-flight
  note keeps panel); smoke clean.
- **A (compact form + type styling + NULL affordance) — DONE 2026-07-22 (impl, awaits visual confirm).**
  `ParamRowTemplate` rebuilt: **name PRIMARY + type SUBORDINATE** (smaller/greyed `SubtleForegroundBrush`) on
  one line, value beside, **NULL an inline toggle** at the value's right edge (no standing 40px column);
  tighter rows (`MinHeight` 30→26, `Margin` 0,3→0,2) and less panel whitespace (`Margin` 20,16→16,14,
  `Spacing` 14→10). Shared by the proc/func launch grid AND the trigger NEW/OLD grids → consistent
  everywhere. Pure Presentation (XAML + 2 `UiStrings` for the NULL toggle label/tooltip); no VM change.
  Build 0/0; smoke clean.
- **B (isolation → Advanced + plain-language copy) — DONE 2026-07-22 (impl, awaits visual confirm).** Isolation
  moved into an **Advanced disclosure** (a flat chevron+"Advanced" button toggling a VM view-state flag
  `IsAdvancedExpanded`, mirroring the bottom-panel collapse — no unstyled FluentTheme `Expander`), **collapsed
  by default**. Inside: the note now leads with **what the option changes** (Read Committed vs Snapshot in plain
  language, plus the own-transaction/rolled-back caveat), then the level selector; the level labels dropped the
  `(rec_version)` jargon. Default stays Read Committed. The main Launch flow is now just parameters (if any) →
  Start. View + `UiStrings` rewrite + one trivial VM toggle (precedent: `IsBottomPanelCollapsed`). Build 0/0; +1
  `DebuggerTabVmTests` (collapsed-by-default); smoke clean.

  **Launch-panel Visual Polish backlog (user, 2026-07-22 — candidates for the future Visual Polish sprint, NOT
  this milestone):** (1) cap the launch panel's max width (it stretches too far on wide monitors); (2) refine
  the History-section hierarchy relative to the parameters; (3) reconsider the final placement of the Start
  Debugging button; (4) give the "No issues detected" pre-flight line a gentler success cue (a success icon or
  colour).
- **D (Quick Relaunch) — COMPLETE via REUSE 2026-07-22 (verified, no new production code).** A state review
  found Quick Relaunch was **already delivered** by deliberate reuse — not a separate feature to build:
  the debugger's launch form is the shared `ExecuteProcedureDialogViewModel`, whose ctor **auto-selects the
  newest history set (`History[0]`, `Record` inserts at front) and applies it to the fields**, backed by the
  persistent per-routine `ParameterHistoryStore` (`settings.dat`); each launch's `Accept()` records the set
  (closing the loop, across tabs + app restarts). In-session re-run is the existing **`RestartCommand`**
  (toolbar button + **`Ctrl+Shift+F5`** in the debug-view key handler), which re-uses the last values with no
  re-prompt; and **Seam C's F5** launches the pre-filled panel in one keypress. **Named favorites stay
  DEFERRED.** Scope reduced from "implement" to "verify + pin": +2 `DebuggerTabVmTests`
  (`Prepare_PreFillsLaunchForm_WithNewestHistorySet`, `Restart_ReusesLastParameterValues`) drive the
  **debugger's own path** and prove both behaviours. No production change; build 0/0; smoke clean.
- **E (Members-tab Debug button):** toolbar button + a `CanExecute` gated on the selected member's kind
  (procedure now; function only once the "function as debug root" gap is closed).

### 5.4 DoD
Launch is compact and keyboard-drivable end-to-end; type never dominates name; isolation is out of the way with
a plain-language description; Quick Relaunch works; both themes. **Risk:** low; the only persistence is
Quick-Relaunch's "last values", which history already stores.

---

## 6. D15.4 — Expression Surfaces UX & Friendly Errors *(P + F)*

**Goal.** The Immediate / Watches / Breakpoint-condition inputs tell the user what to type, and errors are
friendly, not raw SQL.

**STARTED 2026-07-23; split ratified into three seams (A → B → C), Seam A DONE.** The plan's original two
seams were split for cleaner risk profiles + smaller verifiable units: **A** Expression Hints (P) · **B**
Friendly Error Mapping (Core-first, pure) · **C** Local Pre-validation (Core reuse, advisory only). **Seam B
presentation decision (ratified 2026-07-23): "Friendly + raw available"** — the user sees a friendly,
categorised message by default; the full Firebird message stays always reachable (Executed SQL audit, Error
Bar expand, Details) so no diagnostic information or auditability is lost (§F / §0).

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
- **A (P) — Expression Hints — DONE 2026-07-23 (impl, awaits user visual confirm).** Pure Presentation
  (`UiStrings` + two empty-states in `DebuggerTabView.axaml`; no VM/Core change). The terse input placeholders
  now each carry one concise valid-expression example (`DebuggerImmediateWatermark` → `…, e.g. v_counter * 2`;
  `DebuggerWatchWatermark` → `…, e.g. v_status = 'OK'`; `DebuggerBreakpointConditionWatermark` style aligned),
  and a subtle monospace examples line (new shared `DebuggerExpressionExamples` = `v_counter * 2 · v_status =
  'OK' · char_length(v_text)`) sits under the Immediate and Watches empty-states. Build 0/0; 5114 tests green;
  smoke clean. Commit `ea6957e`.
- **B (F, Core-first) — Friendly Error Mapping — DONE 2026-07-23 (impl, awaits user visual confirm). Commit
  `a7d34cf`.** **Core:** `DebugErrorClassifier.Classify(DebugError) → FriendlyErrorCategory { UserException,
  ConstraintViolation, SqlError, Unknown }`, keyed on **SQLSTATE/GDS codes only** (never message text, §F),
  unit-tested without a server. **App:** `DebugErrorPresenter` with `Raw` (best raw field — Error Bar / tooltip)
  and `Describe` (friendly one-liner; `Unknown → Raw`), the single text composer consumed by all four surfaces
  that used to duplicate `?? Message ?? ExceptionName`; `DebuggerTabViewModel.DescribeError` now delegates to
  `Raw` (duplication removed). Friendly text lands on the **three expression surfaces** (Immediate result / Watch
  value / breakpoint-condition reason) with the **raw FB message kept as a tooltip** ("friendly + raw available",
  ratified); the Error Bar (fault) keeps the full raw message (D15.2, the raw surface). No engine change → no
  live-fidelity. **GDS codes MEASURED live on the FB5 lab** (throwaway D15.4 probe, removed after use — the
  leading at-or-above-ISC number the driver surfaces, = what `DebugErrorMapper` stores on `DebugError.GdsCode`):
  user `EXCEPTION` → `ExceptionName` (isc_except 335544517); **NOT NULL** var validation `335544879`, **CHECK**
  `335544347` (isc_not_valid), **PK/UNIQUE** `335544665` → ConstraintViolation; **`335544569`** (isc_dsql_error)
  → SqlError. **Key finding:** token-unknown (-104), table-unknown (-204) and column-unknown (-206) **all** arrive
  as `335544569` (SQLSTATE 42000) — `DebugError` carries only the leading GDS code, not the SQLCODE/sub-code — so
  they are ONE honest `SqlError` bucket, and the precise split (unclosed paren / unknown variable / unknown
  function) is exactly Seam C's job (local pre-validation, richer context before the send). Build 0/0; 5132 tests
  green (+18); smoke clean.
- **C (F, reuse) — Local Pre-validation.** Advisory-only syntax/unknown-name check of a fragment via the
  existing Language Service (Lexer/Parser/`DiagnosticsEngine`) BEFORE the `EXECUTE BLOCK`, with the paused
  frame's in-scope variables seeded as ambient symbols (else a real local reads as "unknown"); NEVER blocks a
  fragment the server would accept (§F — the server owns semantics). Live-verify a valid expression still
  evaluates identically.

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

**Ratified order** (with my endorsement — the sequence is sound; no change recommended). **Progress note
(2026-07-21):** items 1 and 5 (the whole Script Executor track) are **COMPLETE**; D15.1 is **in progress**
(Seam A done). The rewrite ran ahead of its slotted position 5 as a self-contained block.

1. ~~**Script Executor — Step 0 (Probe).**~~ **✅ DONE** — measurement, not implementation; it gated the
   §2.2b self-block decision (never trust an unmeasured Firebird inference: #213/#214/#215 were all falsified).
2. **D15.1 — Editor Readability.** Highest daily value, lowest risk, and **app-wide** — so it should land
   before other editor-surface work builds on the palette. **(In progress — Seam A done.)**
3. **D15.2 — Toolbar + Error Bar.** High visibility on the freshly-shipped debugger; pure P.
4. **D15.3 — Launch Experience.** Daily workflow; repeated launches; pure P + tiny persistence.
5. ~~**Script Executor Rewrite (Steps 1–6).**~~ **✅ COMPLETE (Steps 0–6, live-verified 2026-07-21).** The
   self-contained correctness-debt block; the mixed-DDL+DML defect (#213) is fixed by `Sequenced` mode.
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

**STATUS: COMPLETE (Steps 0–6, live-verified 2026-07-21 — 12 scenarios ALL PASS).** This section is retained
as the historical planning summary; the track is closed. Full record + verdict in
[script-executor-transaction-review.md](script-executor-transaction-review.md) (§6/§7).

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
- **Immediate action:** ~~run Step 0 and record results before starting the rewrite~~ — **done; the whole
  rewrite (Steps 0–6) is complete and live-verified.**

---

*End of D15 design. D15.1 Seam A is implemented (2026-07-21); the rest is unimplemented. A future session
starts a milestone from its § + seam list.*
