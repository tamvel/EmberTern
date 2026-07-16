# Stage 7 — Diagnostics (design & vision)

> **Status: DESIGN, not yet started (2026-07-14).** This document captures the complete Stage 7
> vision so future sessions can implement it without reconstructing the reasoning. Stage 7 is
> **blocked on [Etap 6.9 — Structural AST Deepening](editor-ast-deepening.md)** and on the user
> formally closing the UX Polish Phase. Parent architecture: [`editor-architecture.md`](editor-architecture.md)
> §5.9 / §11.
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
   (IBackgroundRenderer /       (list, grouped/filtered)        (next/prev error,
    colorizing transformer)                                      jump-to-span)
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
- Be **cancellable and cheap** (§8) — it runs on the shared idle tick.

The engine does **not** apply fixes. Quick Fixes are explicitly post-Stage-7 (§12).

---

## 4. Diagnostic model

The `Diagnostic` type already exists in Core (`Diagnostic.cs`), currently:

```csharp
public readonly record struct Diagnostic(
    int Start, int Length, DiagnosticSeverity Severity, string Message, string Code)
{
    public int End => Start + Length;
}
```

Stage 7 keeps this shape. A `Category` (enum, §6) and, later, a `QuickFixes` collection (§12) are the
only additive extensions — and `QuickFixes` is **not** added until the post-Stage-7 Quick Fix stage,
so the read-only pipeline stays minimal.

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

Introduce a `DiagnosticCategory` enum. Initial set (all conservative, all expressible only on the
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

Categories are added incrementally per milestone (§11); the list above is the target, not a
big-bang. Every category must have a false-positive review before shipping (the conservatism rule).

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
  (`IBackgroundRenderer`) — attached in the single wiring seam `SqlEditorBehavior.Attach`, so **every
  SQL surface** (SQL Editor + object editors) gets diagnostics at once.
- Underlines the diagnostic span with the severity brush (Error → `ErrorBrush`, Warning → `WarningBrush`,
  Info → `SubtleForegroundBrush`; both themes, no hardcoded colours).
- Repaints on `ModelUpdated`; reads only the cached diagnostics (no work on the paint path).
- **Hover-shows-the-message is deferred out of S3** (user scope decision, 2026-07-16): S3 renders the
  squiggle only — no tooltip. The hover/message surface is a later milestone (it pairs naturally with
  the panel/nav UI). The squiggle already communicates location + severity; the message is available
  once the panel (S4) lands.

### 8.2 Diagnostics panel

- A list of the current diagnostics: severity icon, message, code, location; grouped/filterable by
  severity and category; click → navigate to the span (§8.3).
- Reuses existing list/grid styling + theme tokens (UI styling rules — no bespoke colours). Reuse the
  results-grid/panel skeleton rather than a new mechanism.
- Live-updates from the cached diagnostics on `ModelUpdated`.

### 8.3 Navigation

- Next/previous diagnostic (keyboard + panel), jump-to-span. Offset lookups are **inclusive at span
  end** (gotcha #198) — reuse the model's existing offset conventions.

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
| **S3** ✅ DONE (impl; awaits visual confirm) | Squiggle rendering + wire into `SqlEditorBehavior.Attach` (App) | S1, S2, S6 | `SquiggleRenderer` (`IBackgroundRenderer`), mirrors `SemanticHighlighter`; diagnostics computed on the same background pass as the model (in `EditorLanguageService`, cached + version-matched), repainted on `ModelUpdated`. Renderer only — zero analysis on the paint path. Includes the **Easy-mode ambient-refresh** follow-up (grid add/remove/rename → live model rebuild, §9). **Hover/tooltip NOT in S3** (deferred, see §8.1). **Visual QA awaits user confirmation** (QA rule). |
| **S4** | Diagnostics panel (App) | S3 | List + group/filter + jump-to-span; reuse panel/grid skeleton + theme tokens. |
| **S5** | Navigation (next/prev, keyboard) | S3 | Inclusive-at-end offset lookups. |

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
