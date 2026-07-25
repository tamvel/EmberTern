# Quick Fixes & Code Actions — design (Stage Q)

> **STATUS: DESIGN ONLY — Q0 complete (2026-07-25). No production code exists.**
> This document is the self-contained implementation guide for the Quick Fix stage. A future session
> starts any seam from here **without re-analysing**. Decisions recorded as "ratified" were agreed with
> the user during Q0 and must not be re-litigated.
>
> Prerequisite stages, all shipped: **Stage 7 (Diagnostics)** — the trusted read-only pipeline this
> builds on ([editor-stage7-diagnostics.md](editor-stage7-diagnostics.md)); **Unified Hover** (§15 there);
> **D3 editor-wiring consolidation** — `SqlEditorBehavior.Attach` is the ONE seam, so gotcha #219 cannot
> bite a new adorner.

---

## 1. What this stage is, and what it is not

Quick Fixes are the first stage in which EmberTern **modifies the user's code on its own initiative**.
Everything before it either read code (diagnostics, hover, navigation, completion) or changed it only
under an explicit, fully-specified instruction (the formatter, safe local rename).

That single fact sets the tone for the whole design. **Architecture rule #11 (never lose information,
never corrupt user code) outranks every feature goal here.** A fix that is convenient but occasionally
wrong is worse than no fix at all, exactly as a diagnostic that is occasionally a false positive is
worse than silence.

**Ratified scope principles:**

- **One source of truth.** Diagnostics detect problems. Quick Fixes only *propose repairs* for a problem
  the diagnostics already found. The fix engine cannot report a problem, so the two can never disagree.
- **Easy to extend.** Adding a new fix must touch exactly one Core file plus its tests — never the UI,
  never the applier, never the diagnostics engine.
- **The light bulb is an extension of diagnostics, not a parallel mechanism.** It renders findings that
  already exist in the cached diagnostics list; it never scans, parses, or analyses anything.

---

## 2. Three findings that shaped this design

These came out of reading the shipped code during Q0. Two of them **overrule the earlier sketch** in
`editor-stage7-diagnostics.md` §12 and in CLAUDE.md's holding note. Both older texts should be read as
superseded by this document.

### 2.1 `Diagnostic` must NOT gain a `QuickFixes` collection

§12 planned exactly that ("additive"). It is not additive in effect.

`Diagnostic` is a `readonly record struct`
([Diagnostic.cs](../../src/EmberTern.Core/Sql/Language/Diagnostic.cs)), and the Diagnostics panel
**depends on its value equality** to skip a rebuild when a keystroke leaves the findings unchanged
(design §8.2 — the reason it is a record struct at all). A record struct's synthesised equality compares
members with `EqualityComparer<T>.Default`, which for a list member is **reference** equality. Two
diagnostics with identical content but separately-built fix lists would therefore compare **unequal**,
and the panel would rebuild on every keystroke.

It is also wasteful in the other direction: fixes would be computed for every diagnostic on every
background pass, while the user asks for them a handful of times per session.

**Decision: fixes are computed on demand from `(model, diagnostic)` and never stored on the diagnostic.**
This also expresses the one-source-of-truth rule more strongly than a stored collection would — the
engine is handed the finding and can only answer "how would I repair this".

### 2.2 The §0-safe apply idiom already exists — generalize it, don't invent one

[`NavigationController.TryApplyRename`](../../src/EmberTern.App/Completion/NavigationController.cs)
already solves "mutate the user's document without corrupting it":

1. **drift check** — verify the document text at every target span still reads what the model said it
   did; any mismatch aborts the whole operation before a single character is touched;
2. **apply in descending offset order**, so earlier edits cannot invalidate later offsets;
3. wrap in `document.BeginUpdate()` / `EndUpdate()`, so the whole change is **one undo unit**.

Quick Fixes need precisely this. **Decision (ratified): one owner for all document mutations —
`TextEditApplier` — and `TryApplyRename` is migrated onto it in the same stage.** Rename's drift check
is name-specific (it compares a folded identifier against the symbol's current name); the shared check
is content-based and therefore *stronger* and construct-agnostic.

### 2.3 The hover card stays read-only; actions live on the light bulb and `Ctrl+.`

CLAUDE.md's holding note suggested plugging fixes into "the hover card (primary)". That conflicts with
two things already ratified and shipped:

- **§15.1.1 — "plain hover = information, Ctrl = actionability."**
- `HoverInfo`'s own contract: *"Read-only, so §0 (never lose information) holds by construction."*

**Decision (ratified 2026-07-25): the hover card remains informational.** It keeps explaining the
diagnostic; it may show a one-line affordance hint naming the shortcut, but it never carries a clickable
mutation. `HoverInfo` therefore gains **no** new section and keeps its read-only guarantee.

---

## 3. Naming — `CodeAction` for the currency, `QuickFixEngine` for the producer

Considered and **rejected**: renaming everything to `CodeAction` / `CodeActionEngine`.
Considered and **rejected**: naming everything `QuickFix`.

The mechanism splits cleanly in two, and the two halves have different generality:

- **The currency is genuinely general.** "A titled set of text edits" describes a quick fix, a
  refactoring, a generate action — *and rename*, which is being migrated onto this applier in this very
  stage. Naming it `QuickFix` would be **wrong today**, for an existing consumer. It is `CodeAction`.
- **The producer is not general.** `GetFixes(model, diagnostic)` **requires a diagnostic**. Refactorings
  (Introduce Variable, Surround With) are driven by the caret or the selection — a different query, not
  a different name for the same one. Calling the class `CodeActionEngine` would over-promise, and the
  next contributor would hang a caret-driven overload off it, giving one class two responsibilities.

This mirrors Roslyn, whose split the editor front-end already resembles: `CodeFixProvider` and
`CodeRefactoringProvider` are separate producers that both emit a shared `CodeAction`.

A future refactoring stage adds a **sibling** producer (`RefactoringEngine`) feeding the same menu with
the same `CodeAction` type. No rename, no churn, no abstraction built in advance.

> The file is named `editor-quick-fixes.md` because Quick Fixes are the stage; the mechanism inside it is
> code actions.

---

## 4. Architecture and data flow

The Stage 7 pipeline is **unchanged**. Quick Fixes are a pure layer *above* it that runs only when the
user asks.

```
edit ─► EditorLanguageService background pass (existing debounce, existing cancellation)
          └─► SemanticModel + Diagnostics  (cached, version-matched)      ← UNCHANGED, read-only
                       │
        user asks: Ctrl+.  or  clicks the light bulb
                       │
                       ▼
   QuickFixEngine.GetFixes(model, diagnostic) ─► IReadOnlyList<CodeAction>     [Core, pure]
                       │
             user picks one from the menu
                       ▼
   TextEditApplier.TryApply(document, action.Edits) ─► drift check → one undo unit   [App]
                       │
                       ▼
   document changed ─► the existing debounce rebuilds model + diagnostics ─► the squiggle disappears
```

Three properties follow from the shape and are worth stating explicitly:

- **No new analysis pass, ever.** The engine consumes the cached model and a finding the diagnostics
  engine already produced.
- **No feedback loop.** Applying a fix changes the document; the document change re-runs the *existing*
  pipeline. The fix layer never tells the diagnostics layer anything.
- **Nothing is offered where nothing is wrong.** With no diagnostic there is no query, so the light bulb
  simply does not appear.

### Layering

| Piece | Project | Notes |
|---|---|---|
| `TextEdit`, `CodeAction` | `EmberTern.Core.Sql.Language.CodeActions` | Pure data, zero Avalonia |
| `QuickFixEngine` | same namespace | Pure static; the diagnostic-driven producer |
| `TextEditApplier` | `EmberTern.App.Completion` | Needs `TextDocument`, so it is App by necessity |
| Light bulb adorner + menu | `NavigationController` | Wired **only** in `SqlEditorBehavior.Attach` |

---

## 5. API

```csharp
// ── Core ─────────────────────────────────────────────────────────────────────────────────────
namespace EmberTern.Core.Sql.Language.CodeActions;

/// One replacement. ExpectedOldText is what the producer believed was there — the applier verifies it
/// before touching anything, which makes staleness impossible to get wrong (§6.2).
public readonly record struct TextEdit(int Start, int Length, string NewText, string ExpectedOldText);

/// A titled, atomic set of edits. The shared currency of quick fixes, rename, and future refactorings.
public sealed record CodeAction(string Title, IReadOnlyList<TextEdit> Edits);

/// The diagnostic-driven producer. Pure: same (model, diagnostic) ⇒ same actions, in a stable order.
/// Returns EMPTY whenever a repair cannot be named exactly — silence is always a valid answer.
public static class QuickFixEngine
{
    public static IReadOnlyList<CodeAction> GetFixes(SemanticModel model, Diagnostic diagnostic);
}
```

```csharp
// ── App ──────────────────────────────────────────────────────────────────────────────────────
namespace EmberTern.App.Completion;

/// THE one owner of every mutation EmberTern makes to a user document (§2.2). All-or-nothing:
/// verifies every edit's ExpectedOldText, then applies in descending offset order inside one
/// BeginUpdate/EndUpdate, so Ctrl+Z reverts the whole action. False ⇒ nothing was changed.
public static class TextEditApplier
{
    public static bool TryApply(TextDocument document, IReadOnlyList<TextEdit> edits);
}
```

### Extensibility — no interface

`IQuickFixProvider` is deliberately **not** introduced. Architecture rule #2 forbids an interface below
two concrete implementations, and the precedent is M1 Structural Matching, whose rule was *"a future
CASE/END or LOOP is one more **producer**, never another renderer."*

`GetFixes` dispatches on `Diagnostic.Category` to private static producer methods. **Adding a fix =
one producer method + one dispatch entry + its tests.** No UI change, no applier change, no diagnostics
change. Seam Q4 exists specifically to prove this claim on four real fixes.

---

## 6. Safety — how §0 / rule #11 is enforced

### 6.1 A fix is only offered when it can be named exactly

Every edit's span comes from an **AST node** the diagnostic already points at — never from a token
re-scan, never from a text search. If a producer cannot state the exact span *and* the exact replacement,
it returns nothing. This is the diagnostics engine's "prefer silence over false positives" rule applied
to mutation, where the cost of being wrong is far higher.

### 6.2 Drift is impossible to get wrong

`ExpectedOldText` makes the staleness check **content-based**, which is stronger and simpler than
tracking document versions: the applier re-reads each span and compares. The user may type between the
menu opening and their click; if anything moved, the apply is refused whole. There is no partial state.

### 6.3 One undo unit

`BeginUpdate`/`EndUpdate` means a multi-edit action (e.g. qualifying several occurrences) is one
Ctrl+Z. A half-undone fix would leave code in a state neither the user nor EmberTern authored.

### 6.4 Never on a read-only surface

The light bulb is wired **only** in `SqlEditorBehavior.Attach` — **never** in
`AttachReadOnlyHighlighting`, which serves the 11 DDL preview editors. A read-only surface must not offer
a mutating action. Since D3 there is one attach seam, so this is a single decision rather than a rule
someone has to remember in two places (gotcha #219 is dissolved, not merely avoided).

---

## 7. UI surfaces

**Ratified: the light bulb and `Ctrl+.` open exactly the same menu**, built from the same
`GetFixes` call. Two triggers, one surface, one code path — the M1 discipline again.

| Surface | Role |
|---|---|
| **Light bulb** (adorner at the diagnostic's line) | Discoverability — "something can be done here" |
| **`Ctrl+.`** | The keyboard path for users who never look for a bulb |
| **Hover card** | Information only (§2.3). Explains the diagnostic; may name the shortcut; never mutates |
| **Diagnostics panel row** | Optional third trigger (Q5), same menu |

The menu itself is presentation over `IReadOnlyList<CodeAction>`: title per row, Enter/click applies.
It has no knowledge of diagnostics, categories, or SQL.

---

## 8. The v1 action set

Only repairs that can be stated exactly ship in v1.

| Code | Category | Action | Why it is exact |
|---|---|---|---|
| ET0005 | AmbiguousColumn | *Qualify as `alias.col`* (one action per candidate) | The candidate tables are already resolved in the model; the edit is a single insertion at a known span |
| ET0001 | UnknownObject | *Did you mean `X`?* | Offered **only** when exactly one candidate is close enough; a pure span replacement |
| ET0002 | UnknownColumn | *Did you mean `X`?* | Candidates are the resolved table's own columns |
| ET0003/4 | UnresolvedVariable / UnresolvedParameter | *Did you mean `X`?* | Candidates are the in-scope symbols the binder already resolved |

**Deliberately excluded from v1, with reasons** (each may return later if a producer can make it exact):

- **ET0006 InsertCountMismatch** — repairing it requires knowing *which* column or value the user meant
  to add or drop. Unknowable; guessing would edit code on a hunch.
- **ET0008 SuspendOutsideSelectable** — the repair is a `RETURNS (…)` clause whose columns we cannot know.
- **"Declare the missing variable"** (the obvious-looking fix for ET0003) — **we do not know its type.**
  Inserting `DECLARE VARIABLE X <guess>;` is precisely the kind of plausible-but-invented code rule #11
  forbids. It becomes possible only if a future producer can *derive* the type from the assignment's
  right-hand side, and only for the shapes where that derivation is certain.

The "did you mean" family needs a string-distance measure. There is none in the codebase
(`CompletionMatcher` is prefix-only, by design), so Q4 adds a small pure-Core one — with a conservative
threshold and the **exactly-one-candidate** rule, so an ambiguous typo produces silence rather than a
menu of guesses.

---

## 9. Seams

Each seam ends build 0/0, tests green, smoke clean, and is committable on its own.

| Seam | Scope | Ends with |
|---|---|---|
| **Q0** | This document | Design ratified; no production code |
| **Q1** | Core: `TextEdit`, `CodeAction`, `QuickFixEngine` + **one** producer (ET0005 qualify). No UI | Unit-tested engine; feature invisible to the user |
| **Q2** | App: `TextEditApplier` (+ migrate `TryApplyRename` onto it) and `Ctrl+.` with a minimal menu | The feature **works end to end** |
| **Q3** | Light bulb adorner in `NavigationController`, `Attach` only; same menu as `Ctrl+.` | Discoverability |
| **Q4** | The "did you mean" producer family (ET0001/2/3/4) + the distance helper | **Proof of extensibility** |
| **Q5** | Diagnostics panel row → same menu (optional) | Third trigger |

**Why this order.** Risk first: Q1 and Q2 carry the rule-#11 risk and are settled before any pixels.
Q2 delivers a usable feature before the discoverability layer, so the mechanism is proven by use rather
than by a bulb that might have nothing behind it. **Q4 is deliberately after Q3**: if adding four fixes
turns out to touch anything outside one Core file, the Q1 design was wrong and this is where we find
out, on real examples, while the cost of correcting it is still small.

The rename migration sits in Q2 rather than in its own seam because the applier and its first two
consumers should be reviewed as one change — that is the point where "one owner of all mutations"
either holds or does not.

---

## 10. Recipe — how to add a Quick Fix later

1. Confirm the diagnostic exists and its `Category` identifies the case. **If it does not, extend the
   diagnostics engine first** — a fix must never detect its own problem (§1).
2. Write a producer: `(SemanticModel, Diagnostic) → IReadOnlyList<CodeAction>`. Read spans from the AST
   node, never from text. Return empty whenever the repair cannot be named exactly.
3. Register it in `GetFixes`'s dispatch on `DiagnosticCategory`.
4. Test the producer: the offered edit for the positive case, **and silence for every ambiguous case**.
   The silence tests are the important ones.
5. Nothing else changes. If the UI, the applier, or the diagnostics engine needed a change, the design
   has been violated — stop and reconsider rather than working around it.

---

## 11. Open items

- **Multi-file / multi-statement actions** — out of scope. Every v1 action edits one document.
- **Action ordering in the menu** — engine order for now (stable, deterministic). If real usage shows a
  preferred fix should lead, ordering becomes a producer concern, not a UI one.
- **Refactorings** (Introduce Variable, Surround With, Extract) — a future sibling producer feeding the
  same menu with the same `CodeAction` type (§3). Nothing in this stage is built for them in advance.
- **"Fix all occurrences"** — deliberately not designed. It multiplies the blast radius of a wrong fix,
  and is worth revisiting only once single fixes have been trusted in real use.
