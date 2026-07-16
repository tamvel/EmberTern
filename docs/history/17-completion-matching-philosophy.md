# Completion Matching Philosophy — prefix-first IntelliSense

A dedicated **Completion** milestone (not part of Stage 8's Smart-Editing charter), inserted at the
user's request after M2 because it's a foundational architectural improvement to how the completion
list matches — and it should land before more editor features build on the current behaviour.

## The problem (user report, 2026-07-16)

Interactive completion behaved like a **text-search engine**, not a **prediction engine**. Typing
`sta` surfaced `NR_STATUS` / `OLD_STATUS` / `DATASTATUS` alongside `STATUS*`; typing `if` surfaced
`IIF` / `NULLIF` / `NOTIFICATIONS`; typing `id` surfaced almost everything. This makes the list longer
*and* harder to scan.

**Root cause (verified).** Our Core `CompletionEngine.GetCompletions` returns the **full** in-scope
candidate set (all columns/objects/locals/keywords), *unfiltered by the typed prefix* — it only
kind-ranks. All live narrowing is done by **AvaloniaEdit's `CompletionList`** (`GetMatchQuality`, a
*private* method, no override hook), whose quality scale accepts exact → StartsWith → CamelCase(≤2) →
**substring/Contains** (quality ≥ 1 ⇒ shown). The substring tier is the noise. So today the list is
effectively a substring search owned by the UI toolkit.

## The decision (user, agreed)

Interactive completion becomes a **prediction engine**:
- **No prefix typed** (Ctrl+Space on whitespace) → the full in-scope list.
- **A prefix with ≥1 StartsWith match** → show **only** StartsWith matches.
- **Zero StartsWith matches** → **close the list** (no popup). **Never** fall back to Contains during
  normal typing — substring lookup is Global Search, a separate workflow.
- Applies **consistently to every kind**: tables, views, procedures, functions, columns, variables,
  parameters, aliases, keywords, **and** snippets.

**Architecture directive (user):** the **`CompletionEngine` becomes the single authority** — given the
caret context, scope, and typed prefix it returns the **final** candidate list. A pure Core
**`CompletionMatcher`** owns all filtering + ranking. The **UI becomes a passive view** that never
filters. **AvaloniaEdit's substring filtering is completely disabled.**

Ranking within the result (verify/​improve as part of this work): **Exact → StartsWith**; CamelCase/
abbreviation is a *future* tier; Contains is never in interactive completion. Within a tier keep the
engine's existing kind-priority + name order (columns/locals > tables/views/procs > functions > other
objects > snippets > keywords), but an **exact match floats to the very top regardless of kind** (so
typing a full keyword like `select` beats a table `SELECT_LOG`).

## Status

### DONE — `CompletionMatcher` (the filtering/ranking authority)

`src/EmberTern.Core/Sql/Language/Completion/CompletionMatcher.cs` — pure, zero-UI:
`Filter(items, prefix)` → empty prefix returns items unchanged (all, in rank); non-empty keeps only
`StartsWith` (case-insensitive, on `InsertText`), floats exact (case-insensitive equality) to the top,
preserves incoming order within each tier (stable), and returns **empty** when nothing starts with the
prefix. Never Contains. Pinned by `tests/EmberTern.Tests/CompletionMatcherTests.cs` (8 tests: the
`sta`/`zam`/`if`/`id` cases from the report, case-insensitivity, exact-floats-above-higher-ranked-
StartsWith, zero→empty, stable within-tier). Build 0/0; suite green. **Currently unused** (foundation
laid first, on purpose) — wiring it in is the remaining, entangled step below.

### REMAINING — engine fold + App passive-view (must land atomically)

The engine change and the App change are entangled: fold snippets into the engine output and the App
must stop adding them separately AND map the new `Snippet` item kind, or the list double-lists snippets
/ renders them as keywords. Do all of it in one change, then verify.

1. **`CompletionModel.cs`** — add `CompletionItemKind.Snippet`; add an optional `SnippetTemplate? Snippet = null`
   payload to the `CompletionItem` record (add `using EmberTern.Core.Sql.Language.Snippets;`). No cycle
   (Snippets → Ast/Semantics/Templates, not Completion).
2. **`CompletionEngine.cs`**
   - `GetCompletions(model, offset, trigger, string prefix = "")` — new `prefix` param.
   - `AddSnippets(model, offset, items, seen)` — fold `SnippetEngine.GetSnippets(model, offset)` in as
     `CompletionItem`s (`Kind = Snippet`, `InsertText = DisplayText = t.Keyword`, `Detail = t.DisplayText`
     (the shape), `Snippet = t`, priority via `PriorityFor(Snippet)`); dedupe under the `Snippet` kind.
   - `PriorityFor(CompletionItemKind.Snippet) => 1.5` (between keyword 1.0 and objects 2.0 — preserves
     the current App-side snippet priority).
   - Baseline: after `Sort`, `return new CompletionResult(CompletionMatcher.Filter(items, prefix))`.
   - Dot path: `CompletionMatcher.Filter(columnItems, prefix)` before returning (so `k.sta` narrows).
3. **`SqlCompletionController.cs`** — make it a passive view:
   - `CreateData(CompletionItem)` helper: `Kind == Snippet ? new SnippetCompletionData(item.Snippet!)
     : SqlCompletionData.FromItem(item, () => BuildItemDetail(item))`. Route `ShowBaselineWindow` and
     `ShowItems` through it; **delete** the separate snippet-merge loop + the `snippets` parameter.
   - Pass the prefix into every `GetCompletions` call: baseline `word.Text`; dot the partial between
     `seg.PrefixStart..seg.PrefixEnd`.
   - `ShowColumns` (the ColumnSpec warm fallback): filter through the **same** matcher — convert to
     temp `CompletionItem`s, `CompletionMatcher.Filter(_, prefix)`, rebuild in that order — so no
     App-side filtering logic exists anywhere. Pass the prefix down from `WarmAndShowAsync`.
   - `OpenWindow`: set `window.CompletionList.IsFiltering = false` (kill AvaloniaEdit's substring filter).
   - **Own the visible list.** `FinishWindow`: subscribe `_editor.Document.TextChanged` (or
     `Document.Changed`) for the window's lifetime (unsubscribe on `window.Closed`); replace
     `ApplyInitialFilter`'s `SelectItem`-based narrowing with selecting index 0.
   - `RefreshOpenWindow()` (driven by the doc-changed subscription): recompute dot-vs-baseline + prefix
     from the caret against the **cached** model (no sync refresh on the hot path); `Items.Count == 0`
     (or a dot-context flip that yields nothing) → `_window.Close()`; else set `StartOffset`/`EndOffset`
     to the current word/prefix segment, `CompletionData.Clear()` + add `CreateData(item)` for each,
     and select index 0.
   - The `WordMayTriggerSnippet` auto-trigger gate (deciding *whether to open* for a 2-char snippet
     prefix like `if`) stays — it's about opening, not filtering; it can keep using
     `SnippetEngine.GetSnippets`.
   - **⚠ AvaloniaEdit `IsFiltering=false` rendering must be confirmed:** verify the ListBox shows
     `CompletionData` in OUR order (not re-sorted by AvaloniaEdit quality) and reflects Clear+Add
     mutations. If `IsFiltering=false` doesn't render our order, the fallback is IsFiltering=true with a
     per-keystroke full repopulate of the exact prefix set (AvaloniaEdit then can't admit a substring),
     accepting that it re-sorts by StartsWith quality — but that loses our exact-float, so prefer
     getting IsFiltering=false right. **This is why the step needs interactive visual QA.**
4. **Tests** — engine: prefix filtering + snippet-as-item + dot-prefix (Core, cheap). A **headless
   `CompletionWindow` probe** (the `ConnectionExpandBindingProbe` pattern) asserting that after typing
   `sta` the *visible* list is exactly the prefix set in the right order — the objective pin for "Core
   owns filtering, UI shows exactly that."
5. **Validation** — build 0/0, full suite + probe, smoke; then the user's interactive visual QA
   (prefix-first + no-match-closes + Ctrl+Space-shows-all + ranking feel), per the QA rule.

### Open ranking question to confirm during step 4/5

Keyword-at-bottom means typing a partial like `fr` ranks any object starting `fr` above the `FROM`
keyword (context ranking already boosts tables after FROM etc., but not the leading statement keyword).
The exact-float fix handles the *fully-typed* keyword. Decide during QA whether common leading keywords
deserve a small boost, or leave as-is (objects-over-keywords is defensible for a metadata-heavy user).
