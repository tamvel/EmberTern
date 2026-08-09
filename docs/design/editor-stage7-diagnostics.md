# Stage 7 — Diagnostics (design & vision)

> **Status: STAGE 7 IS COMPLETE — S1 · S2 · S6 · S3 · S4 · S5 all DONE** (updated 2026-07-16). The engine
> (`ET0001`–`ET0008`), the squiggles, the Diagnostics panel and navigation are all shipped; **§11 is the
> authoritative milestone status**. Its blockers were [Etap 6.9 — Structural AST
> Deepening](editor-ast-deepening.md) (COMPLETE) and the UX Polish Phase (closed). Parent architecture:
> [`editor-architecture.md`](editor-architecture.md) §5.9 / §11.
>
> Diagnostics is now a closed, read-only pipeline. The two follow-ups built ON it — Quick Fixes (§12) and
> Unified Hover (§15) — are **post-Stage-7** and deliberately unscheduled.
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
  ⚠⚠ **CORRECTED 2026-08-05 (stabilization sprint S-2): "a live connection" is NOT the same question as "this
  particular input is loaded", and treating them as one broke this very rule for years.** Columns are warmed
  LAZILY, so with a live connection the snapshot typically knows every object and NONE of their columns — and
  the engine read an empty column set as "this table has no such column", squiggling practically every
  qualified column in a freshly-opened document until the warm pass finished. The provider contract said as
  much in its own words ("unknown **or has no columns loaded yet**"), i.e. the two facts were
  indistinguishable BY CONTRACT rather than by oversight. `ISqlMetadataProvider.KnowsColumns` now draws that
  line and `UnknownColumn` waits for it. ⭐ The general form, worth applying to any future category: **the gate
  is not "is there metadata" but "is the specific fact this diagnostic depends on KNOWN"** — and an empty
  result is only an answer if the provider can say it was asked. See gotcha #317.
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

### 8.3 Navigation — AS BUILT (S5)

**Everything routes through the ONE target.** `DiagnosticsPanelHost.ActiveDocument` (the
`LastFocusedSqlDocument` rule, §8.2.1) was made public and is now the single answer to "which document?" for
both the panel's contents and every jump — so a row and the jump it performs cannot disagree. Navigation
lives **on the host** because that is the class that already knows the target; it is a **pure consumer**,
reading the panel's already-published rows (themselves the language service's cached, version-matched
findings) and never parsing, rebuilding a `SemanticModel`, or re-running the engine.

- **`F8` / `Shift+F8` = next / previous**, wired **once**, in `DiagnosticsPanelHost.Track`. Because every SQL
  editing surface tracks its editors there — **including the main SQL Editor, which now takes a host too**
  (see below) — this sidesteps gotcha #219 by construction rather than by remembering two places. The editor
  receiving the key IS the active document (it has focus), so the binding is always scoped correctly.
- **The caret is the anchor**, not a remembered index: `IndexAfter`/`IndexBefore` on the panel VM scan for the
  first/last diagnostic starting strictly after/before `CaretOffset`. Navigation is therefore monotonic
  regardless of how the selection got where it is, and repeated `F8` walks each finding once, then **wraps
  silently** (last → first, first → last). A clean document is a **no-op** — never a prompt.
- **The scan uses the panel's OWN order** — which is the engine's order (`Finalize` sorts by `Start`, `Length`,
  `Code`; the panel never re-sorts). Sorting again in the navigator would have been a *second* ordering, i.e.
  a second source of truth; reusing the one order is what makes "the panel and navigation always agree" true
  by construction. That dependency is pinned by a test against the real engine.
- **The panel's selection follows** every `F8` (the host writes `SelectedIndex`), and the list auto-scrolls to
  it. A row activates on **double-click or `Enter`**; single-click only selects, so arrow-keying the list
  never yanks the caret. Activation **moves focus into the editor** and leaves the selection alone — the S4
  `Update` no-op guard (§8.2) is what preserves it across debounce ticks, exactly as intended.
- **The jump itself mirrors go-to-definition** (`NavigationController.JumpTo`): caret at `Start`, select the
  span, `BringCaretToView`, focus — so a jump reads the same wherever it came from. Offsets are clamped the
  same way the squiggle renderer and the panel binder clamp (a jump can land a hair ahead of the next
  rebuild).
- **The SQL Editor was migrated from a bare `DiagnosticsPanelBinder` onto the host.** Its single editor makes
  the rule collapse onto itself, so this changes no behaviour — it exists so there is exactly **one**
  targeting mechanism, per the standing "no parallel implementation" rule. It also gives that editor `F8` for
  free.

#### 8.3.1 The two-target problem in the object editors — AS BUILT

**A jump has TWO targets there, not one** (a consequence of §8.2/§8.2.1): the panel is a **peer tab**, so the
user is reading the list *instead of* the editor. Moving the caret alone would land it off-screen. This is
handled by the host's optional `reveal` callback, supplied per surface — the host still decides *which*
editor, the view only knows *how to show* it:

| Surface | `reveal` |
|---|---|
| **SQL Editor** | Editor is always visible beside its panel — nothing to switch. Only exception: a **results-maximized** layout collapses the editor's row to zero height, so reveal restores the split via the existing `ToggleResultsMaximized()` (no second sizing path). |
| **Procedure / Function** | `ActiveSubTabIndex = EditorSubTabIndex`, **plus** `ActiveEasyCollectionIndex = Cursors/SubprogramsEasyIndex` when the target is the cursor or subprogram editor — those Easy sub-tabs host SQL editors of their own. The body sits *below* the sub-tab strip and needs nothing. |
| **Trigger / View** | `ActiveSubTabIndex = EditorSubTabIndex` / `SqlSubTabIndex`. Both editors live directly on that tab (visibility follows the mode) — no sub-tab. |
| **Package** | The editor **is** the tab: `ActiveSubTabIndex = Body/PackageSubTabIndex`. This also re-aligns Package's tab-based `ActiveEditor` fallback with the document just navigated to. |

- **Reveal never re-derives the target** — it is handed the host's active document. Re-deriving it is exactly
  how the row and the jump would drift apart.
- Focusing the target makes it the last-focused SQL document, so a jump **can** retarget the panel. Intended
  and consistent (you navigated there); it does not fight the sticky rule because the `GotFocus` handler
  no-ops when the editor is already the sticky one.
- **Caret + selection are set SYNCHRONOUSLY; only scrolling + focus are posted** (`DispatcherPriority.Background`).
  This is a deliberate divergence from the Package member-navigation idiom, which posts the whole block: the
  caret is the anchor the *next* `F8` reads, and `Background` is dispatched **after** queued `Input`, so
  posting it would make a held `F8` re-select the same diagnostic forever (gotcha #221).
- **Script Executor** has no Diagnostics panel (S4 deferred it — no tab strip), so it has no host and
  therefore no `F8`. Consistent by construction, not by omission.

#### 8.3.2 The UX contract (user delegated, 2026-07-16) — SHIPPED

**The behaviour is the contract; the *binding* is not.** `F8` can be rebound later without touching the
navigation architecture — the key is read in exactly two handlers (`DiagnosticsPanelHost.OnEditorKeyDown`
and `DiagnosticsPanelView.OnListKeyDown`), and nothing else knows about it.

1. **`F8` = next diagnostic, `Shift+F8` = previous.** Visual Studio's Error-List convention, and free here.
   Rider's `F2`/`Shift+F2` is **not** available: `F2` is already rename (`NavigationController`) and Edit
   Field (Table Detail) — itself the VS convention. Also taken: `F5`/`Shift+F5` execute, `F3` Global Search,
   `Alt+F12` peek, `Alt+F` format, `Ctrl+Space` / `Ctrl+Shift+Space` completion / parameter helper. Active
   both on the SQL editing surface and while the panel has focus, always scoped to the **active document**
   (§8.2.1).
2. **Wrap around, silently** — last → first and first → last. A document with no diagnostics is a no-op.
3. **A panel row activates on double-click or `Enter`; single-click only selects.** Double-click is already
   this codebase's "open this" gesture (metadata tree → DDL, Trace → editor, the Parameter Helper). It
   activates the row **under the pointer**, not the selection, so a double-click on empty space below the
   list does nothing instead of yanking the caret to a stale row.
4. **Activating a row moves focus into the editor** (you navigated there to edit it), and the panel **keeps
   its selection** — protected by the S4 `Update` no-op guard, which is what it was for.
5. **`F8`/`Shift+F8` also move the panel's selection.** Combined with (1)'s active-document scoping, this is
   the "panel and navigation always agree" property — and it *falls out of* using the one
   `LastFocusedSqlDocument` target and the one engine order, rather than being separately enforced.

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
| **S5** ✅ DONE (impl; awaits visual confirm) | Navigation (next/prev, keyboard) | S3, S4 | **Closes Stage 7.** `F8` / `Shift+F8` (wrapping, silent), panel row activation on double-click / `Enter`, caret + span selection + scroll + focus, and the object editors' two-target routing (caret **and** tab). A **pure consumer**: it navigates the panel's already-published rows — no parse, no model rebuild, no re-analysis. Everything routes through the one `DiagnosticsPanelHost.ActiveDocument` (§8.2.1, now public) and the one engine order, so the panel and the caret agree by construction. The SQL Editor was migrated off its bare `DiagnosticsPanelBinder` onto the same host — behaviour-identical, but it collapses the two targeting mechanisms into one and hands that editor `F8` for free (gotcha #219 handled by construction). See §8.3 for the as-built record. |

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

## 15. Unified Hover Information — SHIPPED (post-Stage-7, 2026-07-16)

> **Status: DONE (impl); awaits user visual confirmation.** Raised during S4 manual QA: the panel is
> useful, but for a single squiggled error it is unnecessary context switching. Chosen as the first
> post-Stage-7 milestone **ahead of the editor-wiring consolidation** — see §15.4 for why that order was
> reversed. **Absorbs P5d.**

### 15.1 The idea — as built

**One** hover surface, not two competing tooltips:

- hovering a **squiggled span** shows the diagnostic — **without requiring Ctrl**;
- hovering a **normal symbol** shows today's Quick Info, unchanged;
- when a span has **both**, they appear in **one** popup, diagnostics as the *first* section — never a
  second popup.

```
Hover ─► Quick Info (semantic) ─┐
                                ├─► one unified popup
Hover ─► Diagnostics ───────────┘        (+ future sections)
```

**Hard constraint (user):** pure presentation. It consumes the existing `SemanticModel` and the existing
cached `DiagnosticsEngine` results and performs **no new parsing or semantic analysis** — enforced by
`HoverInfoEngine.GetHover`'s signature, which takes the diagnostics as an **input**.

#### 15.1.1 DESIGN DECISION — plain hover = information, Ctrl = actionability

> The user delegated the interaction choice (2026-07-16), stating the goal as: *"when I see a squiggled
> span while writing SQL, I want to understand why it is underlined without switching to the Diagnostics
> panel."*

**Plain hover (dwell-delayed) shows information; Ctrl keeps meaning actionability.** This **confirms** the
frozen §9.4 split rather than amending it — §9.4 assigns the *permanent* cue to the semantic colour and the
*actionable* cue to Ctrl; it never claimed information requires Ctrl.

Three reasons, in order of decisiveness:

1. **Ctrl+hover physically cannot show it.** The old tooltip was gated on `NavigationEngine.TargetAt`
   returning a **navigable target**. An unknown object is *unresolved* — there is no target — so Ctrl+hover
   displayed nothing **precisely where `ET0001` fires**. It could not solve the stated problem without being
   re-gated anyway, and once re-gated the modifier is pure friction.
2. **It would make the actionability cue lie.** Ctrl+hover means *"this leads somewhere"* (underline + hand
   cursor). The most common squiggle leads **nowhere**. Overloading Ctrl to also explain errors would
   attach a navigation affordance to things that cannot be navigated.
3. **Information should not need a modifier you must already know to press.** The squiggle is the cue that
   something is wrong; requiring a hidden gesture to read *why* is the context switch this feature exists to
   remove. Plain hover is also what VS / Rider / VS Code do — zero learning cost.

The two cues are now genuinely independent in `NavigationController`: `UpdateNavigationAffordance`
(Ctrl → underline + hand cursor) and `UpdateHoverInfo` (plain → the card) share only the pointer position.
Ctrl+hover shows the card too — it is a superset, and pressing Ctrl deliberately does **not** dismiss a card
you are already reading.

### 15.1.2 Noise control — as built

Plain hover fires constantly where Ctrl+hover was self-limiting, so the noise budget is the whole UX risk:

- **Dwell: 350 ms** before anything appears (`NavigationController.HoverDwell`). Long enough not to flash
  while the pointer crosses text on its way somewhere; short enough to read as an answer.
- **Stability:** the card stays put while the pointer remains inside `HoverInfo.Span` (the narrowest section's
  span), so it does not flicker as the pointer drifts across one identifier. Moving off it drops the card and
  re-arms the dwell, so crossing text never strobes cards along the path.
- **Arbitration:** the card never opens while the completion list, the Parameter Helper or the Quick Info
  popup is up (`SqlCompletionController.IsPopupOpen` — the controller already owned the "they shouldn't
  stack" rule for all three, so it stays in one place).
- **Never steals focus, never intercepts the pointer** (`IsHitTestVisible = false`, `Focusable = false`) — a
  hit-testable card under the cursor fires `PointerExited` on the editor and flickers itself shut.
- **Dismissal:** any click, any text edit, or the pointer leaving the editor.

### 15.2 §9.4 is confirmed, not amended — and **P5d is absorbed**

§9.4 splits the cues: *permanent cue = the semantic colour*; *actionable cue = Ctrl + hover* (underline +
hand cursor + navigate). It never claimed information requires Ctrl. So the split stays exactly as approved:
**plain hover = information, Ctrl = actionability** (§15.1.1 for why that is the only workable reading).

**P5d — "a dwell-delayed, info-only Quick Info tooltip on plain hover"** — is **delivered by this feature**
and is closed. Shipping it separately would have built the same surface (the plain-hover trigger, its dwell
delay, its noise budget) and then immediately reopened it to add a section and re-tune the dwell. One
milestone, one tuning pass — as recommended. See [editor-architecture.md](editor-architecture.md) §15.2.

### 15.3 Architectural record — as built

1. **The gate is "a resolved symbol OR a diagnostic", never symbol resolution.** `QuickInfoEngine` is
   *symbol*-shaped and returns **null** unless the offset is on a **resolved** identifier; diagnostics are
   *span*-shaped and mostly sit where nothing resolved. **This is not an edge case — it is the headline
   case:** hovering an unknown table (`ET0001`) means the reference did *not* resolve, so `GetQuickInfo`
   returns null exactly there. Implementation confirmed this is even stronger than predicted: `ET0001`,
   `ET0002`, `ET0003`, `ET0005`, `ET0007` **all** fire on unresolved references by construction, so
   "both sections" is genuinely rare — `ET0006` is the main way to reach it (it spans the VALUES list, which
   can contain resolved symbols). Pinned by
   `HoverInfoEngineTests.UnknownObject_HoverExplainsTheSquiggle_EvenWithNoQuickInfo`.
2. **Composed in Core** (`Core.Sql.Language.Hover`), not the App — "presentation-layer" means "no new
   analysis", not "no model". The composition is a pure offset lookup over two existing results, so it sits
   beside `QuickInfoEngine`: zero Avalonia, headlessly unit-testable. **The constraint is enforced by the
   signature** — diagnostics are an *input*, so the engine cannot analyse even by mistake:
   ```csharp
   HoverInfo? HoverInfoEngine.GetHover(SemanticModel model, IReadOnlyList<Diagnostic> diagnostics, int offset);
   ```
   Pinned by `Diagnostics_AreAnInput_NeverRecomputed` (feeding it a list the analyser would never produce).
3. **No provider interface** (architecture rule #2 — no interfaces without two concrete implementations).
   `HoverInfo` is an ordered aggregate of optional sections (`Span`, `Diagnostics`, `Info`). The real
   contract is **section order: diagnostics first** — the reason the user hovered a squiggle is the error;
   the semantic info is supporting context. `IHoverProvider` arrives only if a third *open-ended* source
   lands.
4. **The tooltip was migrated to `OverlayLayer` as step one** (gotcha #209). The old `_tooltip` was the bare
   `Popup` + `SetParent(editor)` pattern that rendered invisibly on the desktop for the Parameter Helper.
   Ctrl-only hover rarely exercised it; plain hover makes it the **primary** discovery surface, so the
   migration went in up front rather than after the bug report. Placement reuses
   `EditorPopups.ClampIntoOverlay` — extracted from `ParameterHelper` (which had the only copy) and now
   shared: same geometry problem, one implementation.
5. **Arbitration + noise** — see §15.1.2. `SqlCompletionController.IsPopupOpen` is the single arbitration
   handle (that controller already owned the "they shouldn't stack" rule for its three popups). The dwell is
   **350 ms** and is the one number expected to want live tuning.
6. **Existing guards reused.** Offset→span hit-testing is **inclusive at the span end** and mirrors
   `SemanticModel.ReferenceAt` exactly (gotcha #198) — one convention, so the two sections agree about what
   "here" means. Diagnostics are version-matched to the model, so a hover that lands a hair ahead of the next
   rebuild degrades exactly like the squiggle renderer's paint-path clamp.
7. **`HoverInfo.Span` is the narrowest section's span**, not the union: a wide `ET0006` overlapping a short
   column reference must still re-query when the pointer leaves the column, because the content genuinely
   differs there. It mirrors `ReferenceAt`'s narrowest-wins tie-break, and it is what the App uses to keep a
   card stable without flicker.
8. **It is the natural home for Quick Fixes (§12).** When they land, the light bulb / fix list is a third
   section of this card — but per (3), still don't abstract early.
9. **Scope boundary.** This must never become a reason to move diagnostics into the semantic model, change
   severity semantics, or add an analysis pass. If a hover wants something the model doesn't have, the fix
   is upstream (engine/binder), never a hover-side scan.

### 15.4 DECISION — why this shipped BEFORE the editor-wiring consolidation

> The Stage 7 retrospective recommended consolidating the duplicated editor wiring (`MainWindow` vs
> `SqlEditorBehavior.Attach`, gotcha #219) **before** the next editor feature. The user left the order to
> judgement; on inspection **that recommendation was wrong for this feature** and was reversed.

The retrospective's argument was: *"both backlog items add per-editor surfaces — exactly the change the
duplication punishes."* That is true of **Quick Fixes** (a light bulb is a new adorner + gesture ⇒ a new
`Attach` call ⇒ the silent-omission risk). It is **false of Unified Hover**:

- Unified Hover **adds no new attach**. It modifies `NavigationController`, which *both* seams already
  attach. The only wiring change is new parameters on `Attach` — and they are **required**, so a missed seam
  is a **compile error**, not a silent gap. Gotcha #219 is about *silent* omission; a signature change is
  loud. (Both seams were in fact caught by the compiler during implementation.)
- `NavigationController` is already the codebase's chosen consolidation point — the double-click handler was
  deliberately moved *into* it from the two duplicated wirings. This feature continues that pattern, so it
  **reduces** per-seam surface rather than adding to it.
- The consolidation is **not** a mechanical merge: the two wirings differ because of a real lifecycle
  difference (MainWindow's editor exists *before* its VM; the object editors attach *after*), and MainWindow
  deliberately bypasses the controller's `subscribeMetadataChanged` hook because it silently latched
  "subscribed" against a null VM and dropped the handler. Consolidation must first solve "subscribe once the
  VM arrives" — a design problem worth its own milestone, not a rename.
- Consolidation touches the *installation of every editor capability on every surface* for **zero
  user-visible value**, and under the project's QA rule it cannot be signed off on green tests — it needs a
  full manual re-verification everywhere. Spending that bill is justified when it *pays*: immediately before
  **Quick Fixes**.

**Standing recommendation: the wiring consolidation is now the milestone immediately before Quick Fixes.**
Doing Unified Hover first cost it one extra parameter to thread.
