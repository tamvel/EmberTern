# Stage 7 — Diagnostics (design & vision)

> **Status: IN PROGRESS — S1 · S2 · S6 · S3 · S4 are DONE; only S5 (navigation) remains** (updated
> 2026-07-16). Its blockers are gone: [Etap 6.9 — Structural AST Deepening](editor-ast-deepening.md) is
> COMPLETE and the UX Polish Phase is closed. The engine (`ET0001`–`ET0008`), the squiggles and the
> Diagnostics panel are shipped; **§11 is the authoritative milestone status**. Parent architecture:
> [`editor-architecture.md`](editor-architecture.md) §5.9 / §11.
>
> This document is both the original vision **and** the as-built record — where implementation refined a
> decision, the decision is rewritten here in place (see §8.2/§8.2.1) rather than left as an aspiration
> the code no longer matches. Post-Stage-7 follow-ups are §12 (Quick Fixes) and §15 (Unified Hover).
>
> Original scope of "Etap 7": **Diagnostics + editor niceties** (squiggles, folding, breadcrumbs,
> bracket/BEGIN-END matching, format-selection/on-paste). Folding, breadcrumbs and structural
> matching are documented as consumers of the deepened AST; **this document focuses on Diagnostics**,
> the core of the stage, and describes how the niceties relate to it.

---

## 1. Why Diagnostics comes *after* AST Deepening

Diagnostics is only as trustworthy as the structure it reasons over. Three concrete reasons it must
follow Etap 6.9:

1. **Category coverage depends on nodes.** Count-mismatch (INSERT columns ↔ VALUES), unreachable
   `ELSE` in a CASE, a `SUSPEND` outside a selectable context, an unresolved cursor — none of these
   are expressible while CASE, the query clauses, and the PSQL body are opaque token blobs. Building
   diagnostics on token walks would create *another* parallel structural implementation — exactly what
   Etap 6.9 exists to eliminate.
2. **"Silence over false positives" needs precise spans.** A conservative diagnostic engine must point
   at the *exact* offending node. Statement-skeleton spans are too coarse; the deepened AST gives a
   node per clause/expression/statement.
3. **Shared foundation with Folding / Breadcrumbs / Debugger.** All four features consume the same
   node tree. Landing the tree once (Etap 6.9), then building the four as clients, is the project's
   convergence goal (Parser → AST → Semantic Model → features). Doing diagnostics first would either
   duplicate structural logic or force a rewrite when the tree lands.

The pre-Stage-7 review also found that a large part of diagnostics is **already computed for free**:
the Semantic Model records every identifier occurrence as a `SymbolReference` with `IsResolved` and
`Role`. "Unknown table/column/variable" is therefore a *filter over existing data*, not new analysis
— once the deepened tree makes the remaining categories (counts, control-flow) expressible.

---

## 2. Overall architecture

```
   text ─► Lexer ─► Parser ─► AST ─► Semantic Model ─┐
                                                     │  (cached per editor, one parse per idle tick)
                                          DiagnosticsEngine.Analyze(SemanticModel)  ← pure Core
                                                     │
                                        IReadOnlyList<Diagnostic>  (cached, versioned)
                                                     │
        ┌────────────────────────────┬──────────────┴───────────────┐
   Squiggle renderer            Diagnostics panel               Navigation
   (IBackgroundRenderer)        (plain list, engine order;       (next/prev error,
                                 active document only — §8.2)     jump-to-span)  ← S5
```

- **`DiagnosticsEngine` is pure Core** (`Core.Sql.Language.DiagnosticsEngine`), zero Avalonia. It
  reads a `SemanticModel` and returns diagnostics. It **computes nothing structural** — it consumes
  the AST + Semantic Model. This keeps it offline-unit-testable and makes it a true *client* of the
  front-end.
- **Semantic-only, no channel merge.** The parser-recovery diagnostics channel (`ParseResult.Diagnostics`)
  is empty by design at this grammar depth (every byte lands in a statement; unrecognised ⇒
  `RawStatement`, which is the §0 valve, not an error). The original "merge parser + semantic
  channels" concept is therefore dropped — Stage 7 diagnostics are **semantic-only**. Keep the
  `Diagnostic`/`ParseResult` types as infrastructure.
- **The App layer is thin glue:** render squiggles, host the panel, wire navigation — all driven by
  the same cached model / `ModelUpdated` cycle that already powers highlighting, completion and
  navigation. **No new parse loop.**

---

## 3. Responsibilities of the Diagnostics engine

- Given a `SemanticModel`, produce a conservative, de-duplicated `IReadOnlyList<Diagnostic>`.
- **Be conservative** (the project's "prefer silence over false positives" rule): only flag what is
  *certain*. `unknown-object` requires a **live metadata connection**; with no metadata
  (`EmptyMetadataProvider`) emit **no** unresolved-object diagnostics (the model already distinguishes
  this). Local-scope diagnostics (unresolved variable, count-mismatch) do not need a connection.
- **Never mutate code** (§0 holds by construction — read-only analysis).
- **Deterministic** — same model ⇒ same diagnostics (stable ordering by span for the panel + tests).
- Be **cancellable and cheap** (§9 / §10) — it runs on the shared idle tick.

The engine does **not** apply fixes. Quick Fixes are explicitly post-Stage-7 (§12).

---

## 4. Diagnostic model

The `Diagnostic` type predates Stage 7 (`Diagnostic.cs`). Stage 7 kept its shape and made exactly one
additive extension — `Category` (§6), defaulted so the older parser-recovery channel is unaffected:

```csharp
public readonly record struct Diagnostic(
    int Start, int Length, DiagnosticSeverity Severity, string Message, string Code,
    DiagnosticCategory Category = DiagnosticCategory.None)   // ← added by S1
{
    public int End => Start + Length;
}
```

A `QuickFixes` collection (§12) is the only other planned extension, and it is **not** added until the
post-Stage-7 Quick Fix stage, so the read-only pipeline stays minimal. Being a **record struct** also
buys the panel free value equality — that is what lets it skip a rebuild when a keystroke leaves the
findings unchanged (§8.2).

- `Start`/`Length` — the **precise node span** (from the deepened AST), so the squiggle underlines
  exactly the offending construct.
- `Code` — a stable short code (`"ET0001"`, …) for filtering, test assertions and future Quick-Fix
  targeting.
- `Message` — human-readable, terse, non-judgmental.

---

## 5. Severity levels

`DiagnosticSeverity` already exists: `Info`, `Warning`, `Error`.

- **Error** — a definite problem at this span (e.g. INSERT column/value count mismatch; reference to
  a column that certainly does not exist on a resolved table).
- **Warning** — a likely problem that does not stop parsing/execution (e.g. an unresolved bare column
  when the table *is* resolved but the column is unknown; an assignment to an unknown variable).
- **Info** — a note, never a problem (reserved; used sparingly to avoid noise).

Rendering: Error = red squiggle, Warning = amber, Info = subtle. All colours come from existing theme
tokens (`ErrorBrush`/`WarningBrush`, both themes) — no hardcoded colours (UI styling rules).

---

## 6. Diagnostic categories

`DiagnosticCategory` (added by S1). The full set below is **shipped** — S1 + S2 + S6 completed the engine
before any UI, so `ET0001`–`ET0008` all exist today (all conservative, all expressible only on the
deepened AST + Semantic Model):

| Category | Severity | Requires connection | Source |
|---|---|---|---|
| `UnknownObject` (table/view/proc/function/…) | Warning | Yes | `SymbolReference.IsResolved == false`, role `SchemaObject` |
| `UnknownColumn` (table resolved, column not) | Warning | Yes | resolved `FromItem` + unresolved `Column` ref |
| `UnresolvedVariable` / `UnresolvedParameter` | Warning | No | unresolved local-scope ref (PSQL body) |
| `InsertCountMismatch` (columns ↔ VALUES) | Error | No | `InsertStatement` column list vs values list (reuse `SignatureHelpEngine` list split — **no new scan**) |
| `AmbiguousColumn` (bare col in ≥2 FROM tables) | Warning | Yes | Semantic Model column resolution |
| `UnknownCursor` | Warning | No | PSQL cursor scope |
| `SuspendOutsideSelectable` | Warning | No | PSQL body node context (needs B1/B5) |

Categories were added incrementally per milestone (§11) rather than big-bang, each with a
false-positive review before shipping (the conservatism rule). **The set is now complete** — extending it
further is a deliberate new decision, not a leftover.

> **Note (S1) — `UnresolvedParameter` / `ET0004` is a forward-looking implementation, not dead code.**
> The engine implements the `UnresolvedParameter` branch (code `ET0004`), but it is currently
> **inactive by design**: at a use site Firebird references a variable and a parameter identically
> (`:name` / bare `name`), and the binder maps an *unresolved* such reference to role `Variable`, never
> `Parameter` (an unresolved parameter role is not producible by the current `SemanticBinder`). An
> undeclared local in a routine body therefore always surfaces as `UnresolvedVariable` (`ET0003`).
> `ET0004` stays dormant **until the `SemanticModel` is deliberately extended** to distinguish the two
> at the reference site — a decision we make on its own merits, not by bending the binder just to
> light up the code path. Keeping the branch means S1 is complete against the category set and the
> future extension is additive.

> **Note (S2) — how `UnknownColumn` and `AmbiguousColumn` are told apart without a second pass.**
> The binder records BOTH as an unresolved `Column` reference: a qualified `alias.col` on a resolved
> table whose column is absent (→ `UnknownColumn` / `ET0002`), and a bare column matching a column on
> ≥2 in-scope tables (→ `AmbiguousColumn` / `ET0005`). The engine distinguishes them by the reference's
> **immediate predecessor**: the binder always emits a member's qualifier reference right before it, so
> a resolved `Qualifier`/`RecordAlias` predecessor ⇒ qualified (and if its table is unknown, silence —
> no cascade); no such predecessor ⇒ bare ⇒ ambiguous. This keeps the whole classification inside the
> single forward pass over `References`, no lookup index. `InsertCountMismatch` (`ET0006`, **Error**) is
> a bounded per-INSERT check reached by an AST traversal (so it also covers an INSERT reused inside a
> PSQL body, Etap 6.9 / B5); it reuses `SignatureHelpEngine`'s INSERT list reader
> (`InsertColumnAndValueCounts`) rather than a parallel scanner, counts **top-level** comma items only
> (a comma inside a function call never inflates the count), and stays silent unless there is an explicit
> column list AND a single, cleanly-parseable `VALUES` row (INSERT…SELECT, DEFAULT VALUES, a malformed
> list, or a multi-row `VALUES` all yield nothing).

> **Note (S6) — cursor usages are modelled by the binder, not the diagnostics engine.** `UnknownCursor`
> needed a signal the model didn't have: an *unresolved* cursor use. Rather than teach the engine to
> re-recognise cursor operations (a second source of SQL knowledge), the **binder** was extended to model
> cursor USAGES the same way it already models cursor DECLARATIONS — an `OPEN` / `FETCH` / `CLOSE` operand
> becomes an ordinary `SymbolReference` with `ReferenceRole.Cursor`, resolved to the declaration or
> unresolved. This is general semantic infrastructure every consumer reads (navigation / find-refs /
> rename / highlighting / quick info / diagnostics); the binder carries **zero** diagnostics-specific
> logic, and the engine stays a pure filter (`Role == Cursor && !IsResolved`). One conservatism guard: an
> unresolved cursor whose name IS declared **somewhere** in the script is not flagged — that is a
> scope/segmentation artifact (the lenient parser can split an EXECUTE BLOCK's `DECLARE` section from its
> `BEGIN…END`), not a genuine unknown cursor. `SuspendOutsideSelectable` is a pure AST-context walk that
> flags a `SUSPEND` leaf only inside a **trigger** or a **PSQL function** — the two contexts where it is
> categorically invalid; a procedure / EXECUTE BLOCK may be selectable, so those stay silent.
>
> Known pre-existing limitation (not introduced here, surfaced by this work): the lenient segmentation
> used by the semantic model can split an EXECUTE BLOCK that has a `DECLARE` section, degrading its
> semantics. The `UnknownCursor` guard neutralises the one false-positive this would otherwise cause; a
> proper fix (keep EXECUTE BLOCK whole in the lenient parser) is a separate parser task.

---

## 7. Pipeline (Parser → AST → Semantic Model → Diagnostics)

1. The per-editor `EditorLanguageService` already parses + builds the `SemanticModel` on a debounced
   (≈300 ms) idle tick, off the UI thread, cancellable, one parse per burst, caching the result and
   raising `ModelUpdated`.
2. Stage 7 adds **one step** to that existing cycle: after the model is built, run
   `DiagnosticsEngine.Analyze(model)` (same background pass) and cache
   `IReadOnlyList<Diagnostic>` with the model's version.
3. On `ModelUpdated`, the squiggle renderer, the panel and the navigation state all refresh from the
   cached diagnostics — the same fan-out pattern the semantic highlighter already uses.

No second parse, no second model, no second loop. Diagnostics ride the existing engine.

---

## 8. Editor interaction

### 8.1 Squiggles

- A dedicated renderer following the existing proven pattern — `SemanticHighlighter`
  (`DocumentColorizingTransformer`) and `OccurrenceHighlighter` / `SearchMatchHighlighter`
  (`IBackgroundRenderer`) — attached so **every SQL surface** (SQL Editor + object editors) gets
  diagnostics.
  > **There is no single wiring seam — this cost S3 a defect.** `SqlEditorBehavior.Attach` installs the
  > shared editor capabilities for the **object editors only**; the **main SQL Editor** hand-wires its own
  > `SqlCompletionController` + renderers in `MainWindow` (its callbacks are null-safe `_currentVm?.…`
  > rather than a stable `vm`). S3 attached the renderer in `SqlEditorBehavior.Attach` alone and assumed
  > that covered everything, so the most-used surface silently had no squiggles until it was caught while
  > preparing S4 (its diagnostics were computed all along — only the paint was missing). **Any new editor
  > capability must be attached in BOTH places** until the duplicated wiring is consolidated — a known
  > refactor deliberately kept out of Stage 7 and owed its own milestone.
- Underlines the diagnostic span with the severity brush (Error → `ErrorBrush`, Warning → `WarningBrush`,
  Info → `SubtleForegroundBrush`; both themes, no hardcoded colours).
- Repaints on `ModelUpdated`; reads only the cached diagnostics (no work on the paint path).
- **Hover-shows-the-message is deferred out of S3** (user scope decision, 2026-07-16): S3 renders the
  squiggle only — no tooltip. The hover/message surface is a later milestone (it pairs naturally with
  the panel/nav UI). The squiggle already communicates location + severity; the message is available
  once the panel (S4) lands.

### 8.2 Diagnostics panel

- A list of the current diagnostics: severity icon, message, code, location. **Grouping and filtering
  were dropped from S4** (user scope decision, 2026-07-16): the panel ships as a plain list first, and
  we only add controls over it if the complete workflow (with S5 navigation) shows a real need. Click →
  navigate to the span is **S5**, not S4.
- **The panel is only a view.** It analyses nothing, filters no semantics, invents no diagnostics, and
  applies no sorting of its own — it shows what `DiagnosticsEngine` produced, in the engine's order. The
  engine is the single source of truth.
- **Host: every SQL editing surface, one panel each.** In the SQL Editor it is a fifth `bottom-tab`
  (Results / Messages / Output / Performance / **Diagnostics**), gated on the existing `IsQueryTabActive`.
  In the object editors — **Procedure, Function, Trigger, View, Package** — it is a peer top-level
  `bottom-tab`, hosted exactly the way `PerformancePanelView` already is there: the same view type, one
  panel VM per host, no shared global state. **Script Executor is deliberately deferred** (user decision):
  it is an SQL editing surface, but its layout has no tab strip (toolbar / editor / splitter / bottom
  area), so it needs its own UX decision rather than being folded in at the end of S4.
  The tab is appended **last** everywhere: `SelectedBottomTabIndex` / `ActiveSubTabIndex` are persisted and
  `PerformanceBottomTabIndex = 3`, `Procedure/Function.PerformanceSubTabIndex = 5`, `ResultSubTabIndex`,
  `SqlSubTabIndex`, `PackageSubTabIndex`… are hard-coded, so the existing indices must not shift.
- **The editor layouts are NOT redesigned** (explicit user decision): no panel below the editor, no extra
  splitters. A peer tab was chosen over an editor-adjacent panel because the latter is layout surgery in
  five views, steals space from an already dense Easy mode (`Auto,240,4,*`) and stacks a second splitter.
  The accepted trade-off: reading the list hides the editor — exactly as Performance already behaves.

#### 8.2.1 DESIGN DECISION — the panel reflects the ACTIVE SQL document only

> Explicit decision (user, 2026-07-16), not an implementation detail.

A Procedure/Function detail tab hosts **four** SQL editors (source · body · cursor · subprogram); Trigger
and View host two; Package hosts two (header · body). The panel shows **one** of them — never a merge:

- **It never aggregates.** A finding in a non-active editor is not listed; its squiggle still flags it in
  place. If a workspace-wide diagnostics list is ever wanted, it is a **separate feature** and must not
  change the meaning of this panel.
- **The rule is `LastFocusedSqlDocument`**: the last SQL editor to take focus, or — until one does, and
  after a mode switch — the mode's primary editor (body in Easy mode, full source in Source mode; for
  Package, the tab-based `ActiveEditor`). Switching focus between Source / Body / Cursor / Subprogram
  retargets and republishes the panel immediately, with no text edit required.
- **Why not the views' existing `ActiveEditor` property**, whose first clause is
  `_focusedEditor is not null && _focusedEditor.IsEffectivelyVisible`? Selecting the peer Diagnostics tab
  **hides the editor tab**, so that guard always fails while the panel is on screen and `ActiveEditor`
  collapses to the mode's primary editor — the Cursors/Subprograms editors could then never appear in the
  panel at all, and "focus an editor → the panel refreshes" would be unobservable by construction. The
  guard exists so Alt+F never formats a hidden editor; a read-only list has no such concern. Tracking
  focus stickily also keeps the panel independent of how TabControl realizes hidden tab content.
- Implemented by `DiagnosticsPanelHost` (App/Completion) — pure wiring over the unchanged
  `DiagnosticsPanelBinder`: one binder per editor, gated through the binder's existing lazy panel
  resolver, so a non-active editor's binder resolves to `null` and publishes nothing.
- Reuses existing list/panel styling + theme tokens (UI styling rules — no bespoke colours): the central
  `ListBoxItem` state overrides, `Classes="subtle"`, and the `SeverityBrushKey` + `IconBrushConverter`
  pattern established by `SessionWarningViewModel` / `FindingViewModel`. A **virtualizing `ListBox`**, not
  an `ItemsControl` in a `ScrollViewer`, so a very large script's findings don't all realize.
- The row's severity → brush mapping is **identical to the squiggle renderer's**, so a row and the
  underline it describes always read as the same severity.
- Live-updates from the **cached** diagnostics on `ModelUpdated` — one subscription, zero analysis. Every
  refresh trigger (text edit, model rebuild, metadata bump, Easy-mode ambient-symbol change) already routes
  through that one signal.
- A clean document shows a readable "No diagnostics" state, never an empty table.

### 8.3 Navigation

> **S5 — not yet implemented.** The notes below include what S4's hosting decisions imply for it.

- Next/previous diagnostic (keyboard + panel), jump-to-span. Offset lookups are **inclusive at span
  end** (gotcha #198) — reuse the model's existing offset conventions.
- **A jump has TWO targets in an object editor, not one** (a consequence of §8.2/§8.2.1, recorded while
  the S4 implementation was fresh): the panel there is a **peer tab**, so activating a diagnostic must
  (a) move the caret in the **active SQL document** — which may be the cursor/subprogram editor, and may
  itself sit on a different Easy-mode *sub*-tab — and (b) switch the detail tab back to **Editor** so the
  caret is actually on screen. The SQL Editor has neither problem (its panel sits below a permanently
  visible editor). The active document is already known: it is the one the panel is reflecting
  (`DiagnosticsPanelHost`'s `LastFocusedSqlDocument`) — S5 must route the jump through the same rule
  rather than re-deriving a target, or the row and the jump can disagree.
- Focusing the target editor makes it the last-focused SQL document — which is consistent (you navigated
  there), but means a jump **can** retarget the panel. Intended; just don't let it fight the sticky rule.

#### 8.3.1 S5 — start here (handoff, 2026-07-16)

S5 is the **only** milestone left in Stage 7. Scope: next/previous diagnostic (keyboard + panel) and
jump-to-span. Nothing else — no Quick Fixes, no Unified Hover (§15), no code actions, no light bulb.

**It is a pure consumer.** It must not parse, touch the AST, rebuild the `SemanticModel`, or recompute
diagnostics — it navigates from the cached `Diagnostic` alone. That is already possible with no new
plumbing: `DiagnosticRowViewModel` deliberately keeps the whole source record (`Diagnostic`), so a row
carries its own `Start`/`Length`.

**First code change — expose the active document; do not invent a second targeting mechanism.**
`DiagnosticsPanelHost` already computes the one true target (`ActiveDocument` = the
`LastFocusedSqlDocument` rule, §8.2.1) but keeps it **private**. Navigation must jump into *that* editor,
or the row and the jump can disagree. Exposing it is the natural first step; everything else builds on it.

**Suggested order:** the SQL Editor first — it has a single editor, always visible, so it proves the jump
(caret + `ScrollTo` + focus) with none of the tab complications. Then the object editors, which add the
two-target problem above (caret in the active document **and** switch the detail tab back to Editor —
`EditorSubTabIndex = 0` on every detail VM; a cursor/subprogram target may also need its Easy-mode
*sub*-tab selected).

**Watch out for gotcha #219** if S5 adds any per-editor input handling: the main SQL Editor does **not**
go through `SqlEditorBehavior.Attach` — it hand-wires its own capabilities in `MainWindow`. A handler
added to only one of those two places silently misses a surface (exactly how S3 shipped with no squiggles
in the SQL Editor).

**Decided defaults (user delegated, 2026-07-16).** The **behaviour is the contract**; the *binding* is
not — it can be rebound later without touching the navigation architecture, and will be reviewed during
manual testing.

1. **`F8` = next diagnostic, `Shift+F8` = previous.** Visual Studio's Error-List convention, and free
   here. Rider's `F2`/`Shift+F2` is **not** available: `F2` is already rename
   (`NavigationController`) and Edit Field (Table Detail) — itself the VS convention. Also taken:
   `F5`/`Shift+F5` execute, `F3` Global Search, `Alt+F12` peek, `Alt+F` format, `Ctrl+Space` /
   `Ctrl+Shift+Space` completion / parameter helper. Active both on the SQL editing surface and while
   the panel has focus, always scoped to the **active document** (§8.2.1).
2. **Wrap around, silently** — last → first and first → last. Standard editor behaviour; a modal "no
   more diagnostics" prompt would be noise. A document with no diagnostics is simply a no-op.
3. **A panel row activates on double-click or `Enter`; single-click only selects.** So arrow-keying the
   list never yanks the caret around, which is how every error list behaves — and double-click is already
   this codebase's "open this" gesture (metadata tree → DDL, Trace → editor, the Parameter Helper).
4. **Activating a row moves focus into the editor** (you navigated there to edit it), and the panel
   **keeps its selection**. The S4 `Update` no-op guard (§8.2) already protects that selection across
   debounce ticks — that is what it was for.
5. **`F8`/`Shift+F8` also move the panel's selection** to the diagnostic they jump to, so the panel and
   the caret never disagree. Together with (1)'s active-document scoping, this is the "the panel and
   navigation always agree on the active SQL document" property S5 must preserve — it falls out of using
   the one `LastFocusedSqlDocument` target rather than being separately enforced.

---

## 9. Incremental refresh & cancellation

- **Incremental refresh** rides the existing model cycle: a keystroke bumps the text version; the
  debounce coalesces the burst; the background pass rebuilds the model **and** re-analyzes; the UI
  refreshes once. A metadata-generation bump (a category finishing load) also triggers a rebuild +
  re-analyze without a keystroke (the mechanism already exists for highlighting/completion).
- **Easy-mode ambient changes trigger the same rebuild (S3 follow-up).** An Easy-mode routine editor's
  body holds only the fragment; its parameters and `DECLARE`d variables live in the surrounding grids and
  reach the model as *ambient symbols*. Editing those grids (add / remove / reorder / **rename**) raises
  `SourceObjectDetailTabViewModel.AmbientSymbolsChanged`, which the view forwards to each ambient-seeded
  editor's `SqlCompletionController.NotifyAmbientSymbolsChanged()` → the same debounced
  `RefreshModelWithMetadata` rebuild (it re-captures the ambient symbols). Without this the model — and
  thus the squiggles — would go stale until the next body-text edit (e.g. a squiggle under `:test`
  lingering after the user added `test` to the Variables grid). Only the row **Name** is tracked (the only
  property affecting resolution); the debounce coalesces a name typed character-by-character into one
  rebuild.
- **Cancellation:** the analysis runs inside the same cancellable background pass; a newer edit
  cancels the in-flight one via the existing `CancellationTokenSource`. `DiagnosticsEngine.Analyze`
  accepts a `CancellationToken` and checks it between statements.
- **Staleness:** cached diagnostics carry the model version; the UI never renders diagnostics against
  a newer document than the one they were computed for.

---

## 10. Performance considerations

- **O(n) over references/nodes**, not O(n²): `UnknownObject`/`UnknownColumn`/`AmbiguousColumn` are a
  single pass over `SemanticModel.References` (already materialized); count-mismatch is a per-INSERT
  local check reusing the signature-help split.
- **No metadata fetch on the analysis path** — the engine reads the model's already-captured
  metadata snapshot; warming is the existing pipeline's job.
- **Paint path does zero analysis** — it reads cached results.
- **Large bodies:** the analysis is bounded by the same debounce/cancellation/tree-caching that keeps
  parsing off the keystroke; the UI thread never analyzes.

---

## 11. Planned milestones (Stage 7)

Prerequisite: **Etap 6.9 (at least B1 + B2)** landed. Order: cheapest/highest-certainty first, so the
diagnostics pipeline earns trust before anything mutates or before the panel/nav surface grows.

| # | Milestone | Depends on | Notes |
|---|---|---|---|
| **S1** ✅ DONE | `DiagnosticsEngine` (Core) — `UnknownObject`/`UnknownColumn`/`Unresolved*` from the model | Etap 6.9 B1/B2 | Pure Core; consumes the model; conservative + connection-gated. Add `DiagnosticCategory`. Codes `ET0001`–`ET0004`. |
| **S2** ✅ DONE | `InsertCountMismatch` + `AmbiguousColumn` (Core) | S1 | Reuse `SignatureHelpEngine` list split — no new scan. Codes `ET0005`–`ET0006`. |
| **S6** ✅ DONE | PSQL-specific categories (`UnknownCursor`, `SuspendOutsideSelectable`) | Etap 6.9 B1/B5 | Needs the PSQL body tree. Codes `ET0007`–`ET0008`. **Pulled ahead of S3–S5 (see note)** so the Core engine is complete before any UI. |
| **S3** ✅ **DONE + user-confirmed + committed** (c8266e3, + gap fix f397190) | Squiggle rendering (App) | S1, S2, S6 | `SquiggleRenderer` (`IBackgroundRenderer`), mirrors `SemanticHighlighter`; diagnostics computed on the same background pass as the model (in `EditorLanguageService`, cached + version-matched), repainted on `ModelUpdated`. Renderer only — zero analysis on the paint path. Includes the **Easy-mode ambient-refresh** follow-up (grid add/remove/rename → live model rebuild, §9). **Hover/tooltip NOT in S3** — deferred, and now folded into the post-Stage-7 Unified Hover (§15). **Attached in TWO places, not one** (§8.1): `SqlEditorBehavior.Attach` covers the object editors, `MainWindow` attaches its own — assuming a single seam is exactly what left the main SQL Editor with no squiggles until it was caught while preparing S4. |
| **S4** ✅ DONE (impl; awaits visual confirm) | Diagnostics panel (App) | S3 | **List only** (user scope decision — see §8.2): severity · code · message · location, in engine order, with a readable empty state. Hosted on **every** SQL editing surface: a fifth `bottom-tab` in the SQL Editor, and a peer top-level tab in the **Procedure / Function / Trigger / View / Package** editors (same view + VM, hosted like `PerformancePanelView`; Script Executor deferred). The panel reflects the **active SQL document only** — the `LastFocusedSqlDocument` rule (§8.2.1), never a merge. Fed by `DiagnosticsPanelBinder` from the **cached** diagnostics on the shared `ModelUpdated` cycle — no parse, no re-analysis. Group/filter and jump-to-span are NOT part of S4. |
| **S5** | Navigation (next/prev, keyboard) | S3, S4 | Inclusive-at-end offset lookups. Closes Stage 7. |

> **Execution-order note (user decision).** S6 was implemented **before** the App milestones S3–S5,
> a deliberate sequence change (not a roadmap change): S6 is the last Core milestone, its prerequisites
> (Etap 6.9 B1/B5) were already met, and it extends the engine without touching the App — so completing
> it first gives a **fully complete Core diagnostics engine** (`ET0001`–`ET0008`) that S3 can render
> against without later returning to add categories.

Folding, breadcrumbs and bracket/BEGIN-END matching are the remaining "niceties" of the original
Etap 7; they consume the same deepened tree and are tracked in
[`editor-ast-deepening.md`](editor-ast-deepening.md) §8 and the parent architecture doc.

---

## 12. Future Quick Fix integration (POST-STAGE-7 — explicitly out of scope here)

> Quick Fixes are **not** part of Stage 7 (user decision, 2026-07-14). Stage 7 establishes a
> **trusted, read-only diagnostics pipeline first**; Quick Fixes are a dedicated follow-up stage
> built on top of it once diagnostics are mature and stable.

When that stage comes:
- `Diagnostic` gains a `QuickFixes` collection (additive).
- Fixes (add missing alias, fix INSERT/VALUES count, wrap in EXECUTE BLOCK, declare missing variable)
  are generated from the **AST node** the diagnostic points at — never from a token re-scan.
- **Every fix obeys §0**: it must be a change EmberTern can apply *and reproduce identically*; if it
  cannot be applied safely, it is not offered (uncertainty ⇒ do nothing). This is why fixes wait for
  the deepened AST and a trusted diagnostics base — a fix that mutates user code on shaky structure is
  unacceptable (Architecture rule #11).

---

## 13. Relationship with Folding, Breadcrumbs and the Debugger

All four are **clients of the same deepened AST** (Etap 6.9) and share the same cached-model /
`ModelUpdated` cycle:

- **Folding** — fold regions come from node spans: statements + CTEs (query layer) and
  routine bodies / `BEGIN…END` / `IF` / `WHILE` / `FOR` (PSQL body tree). One structural source; no
  editor scanner. (Uses AvaloniaEdit `FoldingManager`.)
- **Breadcrumbs** — the ancestor chain of the node at the caret (`PROCEDURE X ▸ FOR SELECT ▸ IF`),
  now genuinely available because control-flow is modeled (B1) rather than the coarse scope-only path
  that existed before Etap 6.9.
- **Debugger** — every executable statement is a `PsqlStatement` with a stable, body-relative span
  (Etap 6.9 §7): breakpoints, stepping and execution↔source mapping map onto nodes directly.
- **Shared discipline:** none of these introduces a token walk. If a construct they need is missing
  from the AST, the fix is to deepen the parser (extend Etap 6.9), never to add an editor-side
  scanner.

---

## 14. Summary

Stage 7 Diagnostics is a **thin, pure-Core client** of the deepened AST + Semantic Model: it reads
structure it does not compute, emits conservative diagnostics on the existing background cycle, and
renders them through the existing renderer/panel/navigation patterns. It comes after Etap 6.9 because
its trustworthiness, its category coverage, and its shared foundation with Folding / Breadcrumbs / the
Debugger all depend on SQL structure being represented **once**, in the tree.

---

## 15. POST-STAGE-7 — Unified Hover Information (BACKLOG — do not implement during Stage 7)

> **Status: recorded, not scheduled** (user, 2026-07-16). Raised during S4 manual QA: the panel is useful,
> but for a single squiggled error it is unnecessary context switching. **Explicitly out of Stage 7's
> scope** — Stage 7 finishes at S5 (navigation). Like Quick Fixes (§12), this is a dedicated follow-up
> built on the trusted, read-only pipeline.

### 15.1 The idea

**One** hover surface, not two competing tooltips:

- hovering a **squiggled span** shows the diagnostic — **without requiring Ctrl**;
- hovering a **normal symbol** shows today's Quick Info, unchanged;
- when a span has **both**, they appear in **one** popup, diagnostics as an additional *section* — never a
  second popup.

```
Hover ─► Quick Info (semantic) ─┐
                                ├─► one unified popup
Hover ─► Diagnostics ───────────┘        (+ future sections)
```

**Hard constraint (user):** pure presentation. It consumes the existing `SemanticModel` and the existing
cached `DiagnosticsEngine` results and performs **no new parsing or semantic analysis**.

### 15.2 It is compatible with §9.4 — and it is where **P5d** should land

This does **not** amend the frozen §9.4 navigation-affordance decision, and that is worth stating plainly
because it looks like it might. §9.4 splits the cues: *permanent cue = the semantic colour*; *actionable
cue = Ctrl + hover* (underline + hand cursor + navigate). It never claimed information requires Ctrl.
[`editor-architecture.md`](editor-architecture.md) §15.2 already carries the deferred **P5d — a plain-hover
info cue**: *"a dwell-delayed, info-only Quick Info tooltip on plain hover (no Ctrl held); the underline +
hand-cursor affordance stays Ctrl-only per §9.4"*, deferred only because dwell/noise wants live tuning.

So the split stays exactly as approved: **plain hover = information, Ctrl = actionability.**

> **Recommendation: fold P5d into this feature; do not ship it separately first.** Both build the same
> surface — the plain-hover trigger, its dwell delay and its noise budget. Shipping P5d alone means
> building that surface, then immediately reopening it to add a section and re-tuning the dwell. One
> milestone, one tuning pass.

### 15.3 Architectural notes for whoever implements it

1. **The shapes don't match, and the common case is diagnostic-only.** `QuickInfoEngine.GetQuickInfo` is
   *symbol*-shaped — `model.ReferenceAt(offset)` → `Symbol` → `QuickInfo`, and it returns **null** when the
   offset isn't on a **resolved** identifier. Diagnostics are *span*-shaped and frequently sit where no
   symbol resolved — in fact **the most common unified case has no Quick Info at all**: hovering an unknown
   table (`ET0001`) means the reference did **not** resolve, so `GetQuickInfo` returns null exactly there.
   `ET0006` (InsertCountMismatch) spans a statement/list with no symbol under most of it. Consequence: the
   hover gate must change from *"is there a resolved symbol?"* to *"is there a resolved symbol **or** a
   diagnostic at this offset?"* — the trigger can no longer be driven by symbol resolution. Treat
   "both sections present" as the *rarer* path, not the design centre.
2. **Compose in Core, not the App — "presentation-layer" means "no new analysis", not "no model".** The
   composition is a pure offset lookup over two existing results; it belongs beside `QuickInfoEngine`
   (Core, zero Avalonia, headlessly unit-testable — the value of which S4's VM tests just demonstrated).
   Suggested shape, with the constraint enforced **by the signature**:
   ```csharp
   // Core. Diagnostics are an INPUT (the cached, version-matched list) — so the engine
   // *cannot* analyze, by construction, rather than by a rule someone must remember.
   HoverInfo? HoverInfoEngine.GetHover(SemanticModel model, IReadOnlyList<Diagnostic> diagnostics, int offset);
   ```
   The App renders `HoverInfo`'s sections, reusing `QuickInfoView.Build` for the semantic section.
3. **Don't build the provider interface yet (architecture rule #2 — no interfaces without two concrete
   implementations).** "Provider → provider → future providers" is a good *mental* model, but the actual
   composition is a handful of lines. Model it as an ordered aggregate of optional sections
   (`HoverInfo { QuickInfo? Info; IReadOnlyList<Diagnostic> Diagnostics; }`) and introduce `IHoverProvider`
   only if a third source genuinely lands and the set becomes open-ended. The real contract to decide is
   **section order**, not the provider type — recommend **diagnostics first**: the reason the user hovered a
   squiggle is the error; the semantic info is supporting context.
4. **Migrate the tooltip to `OverlayLayer` first (gotcha #209).** `NavigationController`'s `_tooltip` is
   still the bare `Popup` + `((ISetLogicalParent)…).SetParent(editor)` pattern — the exact pattern that was
   invisible on the desktop for the Parameter Helper and forced the OverlayLayer move. Under Ctrl-only
   hover it is rarely exercised; plain hover makes it the **primary** discovery surface. Do the migration up
   front, not after the bug report.
5. **Popup arbitration + noise.** Plain hover fires constantly, where Ctrl+hover was self-limiting. One
   rule needed: the hover never opens while the completion window or the Parameter Helper is open, and never
   steals focus (today's tooltip is already hit-test-invisible and focus-neutral — keep that). The dwell
   delay is the whole UX risk (P5d's stated reason for deferral, and P2's original "pops too early"
   complaint); budget a live-tuning pass.
6. **Reuse the existing guards.** Offset→span hit-testing is **inclusive at the span end** (gotcha #198).
   Diagnostics are version-matched to the model, so the hover applies the same clamp the squiggle renderer
   does on paint (a hover can land a hair ahead of the next rebuild).
7. **It is the natural home for Quick Fixes (§12).** When they land, the light bulb / fix list is a third
   section of this popup. Design the aggregate with room for it — but per (3), still don't abstract early.
8. **Scope boundary.** This must never become a reason to move diagnostics into the semantic model, change
   severity semantics, or add an analysis pass. If a hover wants something the model doesn't have, the fix
   is upstream (engine/binder), never a hover-side scan.
