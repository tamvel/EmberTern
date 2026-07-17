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

## Status — COMPLETE (2026-07-17)

Both halves have landed. The user re-reported the original symptom on 2026-07-17 ("typing `cont` still
lists `XXX_PS_CONTRACTORMAP`, `GEN_XXX_PS_CONTRACTORMAP`, `MON$CONTEXT_VARIABLES`") as a **regression** —
it wasn't one. Nothing had regressed: the wiring below had simply never been done, so `CompletionMatcher`
sat unused and AvaloniaEdit's substring filter was still the only thing narrowing the list. Worth
remembering as a category: *a correct, unit-tested Core component that nothing calls looks exactly like a
regression from the outside* — the tests were green the whole time it was broken.

### DONE — `CompletionMatcher` (the filtering/ranking authority)

`src/EmberTern.Core/Sql/Language/Completion/CompletionMatcher.cs` — pure, zero-UI:
`Filter(items, prefix)` → empty prefix returns items unchanged (all, in rank); non-empty keeps only
`StartsWith` (case-insensitive, on `InsertText`), floats exact (case-insensitive equality) to the top,
preserves incoming order within each tier (stable), and returns **empty** when nothing starts with the
prefix. Never Contains. Pinned by `tests/EmberTern.Tests/CompletionMatcherTests.cs` (8 tests: the
`sta`/`zam`/`if`/`id` cases from the report, case-insensitivity, exact-floats-above-higher-ranked-
StartsWith, zero→empty, stable within-tier). Build 0/0; suite green. **Currently unused** (foundation
laid first, on purpose) — wiring it in is the remaining, entangled step below.

### DONE — engine/App wiring: the list is Core's, end to end (2026-07-17)

**Result.** Typing `cont` against the reported catalog now lists exactly `CONTRACT_LINES`, `CONTAINING`,
`CONTINUE`. Every merely-containing name is gone, the keywords the user asked to keep are kept, and a
zero-match prefix (`tractor`) opens no window at all.

**As-built, and where it diverges from the plan this section used to hold.** The plan was written before
two things changed, so it was followed in intent rather than to the letter:

- **The snippet fold (its steps 1–2) is moot.** The Etap-5 keyword live templates were deleted by the
  Language Completion milestone, so there is no `Snippet` item kind to add and no App-side snippet-merge
  loop to delete. That removed the entire reason the step was called "entangled" — what landed is much
  smaller than what was planned.
- **`CompletionEngine` did NOT get a `prefix` parameter, deliberately.** The directive was "the engine
  returns the FINAL list", but that cannot survive a **backspace**: to widen the list again the controller
  needs the *unfiltered* candidate set, so a prefix-filtering engine would have to be re-queried on every
  keystroke — either against the debounce-lagged model, whose token offsets no longer line up with the
  caret (wrong scope, wrong dot detection), or by forcing a synchronous whole-document parse per
  character, which is exactly the work Etap 0 forbids on the per-character path.

  So the split is by *question asked*, which is the same one-owner rule applied one level down:
  **`CompletionEngine` answers "what is legal at this caret"** — the candidate set, a property of the
  *position*, fixed for the session — and **`CompletionMatcher` answers "which of those match what is
  typed"** — a property of the *prefix*, which changes per keystroke. The controller holds the session's
  candidates (`_sessionCandidates`) and re-filters them through the matcher. The directive's actual
  content — one matcher owns all filtering, the UI invents no matching of its own, AvaloniaEdit's filter
  is off — holds fully: `CompletionMatcher.Filter` has exactly two call sites (open + refresh) and the App
  contains no match rule of its own.

**What landed**

1. **`CompletionEngine`** — no signature change. Its doc now states the contract explicitly (it returns
   the *unfiltered* candidate set; prefix narrowing is `CompletionMatcher`'s job) and *why* the two are
   separate. `PriorityFor` became `public` so the App's on-demand column warm ranks from that one table
   instead of a second copy.
2. **`SqlCompletionController` — a passive view.**
   - `OpenWindow` sets `CompletionList.IsFiltering = false`.
   - `ShowItems(start, end, candidates)` filters through the matcher, opens only if something matches, and
     caches `candidates` for the session.
   - `RefreshOpenWindow`, driven off `Caret.PositionChanged`, re-filters that cached set on every prefix
     change and closes the window when nothing matches. The caret is the right signal because it covers
     backspace/delete/paste/click where `TextEntered` covers only typing, and the document is already
     consistent with it (AvaloniaEdit's own `CompletionWindow` reads the prefix in that same event) — so
     no dispatcher post, no timing assumption.
   - **Deleted:** `ApplyInitialFilter` (gotcha #200's workaround — we pre-filter now, so there is no
     unfiltered moment left to patch up), `CloseIfNarrowedToNothing` (gotcha #227's workaround — the
     matcher returning empty *is* the close condition, so the empty-popup bug is now structurally
     impossible instead of swept up afterwards), and `BuildColumnDetail`.
   - **Unified the warm path:** `ShowColumns` used to build `SqlCompletionData` from `ColumnSpec` directly
     — a second column-row builder that filtered through nothing. It now converts to the same
     `CompletionItem` shape the engine produces (`ToColumnItem`) and routes through `ShowItems`, so a
     just-warmed column list filters, renders and detail-panes through one path.
3. **`IsFiltering=false` renders our list, in our order** (measured, not assumed): AvaloniaEdit binds the
   ListBox straight to `CompletionData` and its quality re-sort stops too — `CONTRACT_LINES` (a table,
   priority 3.0) renders above `CONTAINING`/`CONTINUE` (keywords, 1.0), where the quality sort would have
   floated the exactly-matching keywords first. The plan's fallback design was unnecessary and is not kept.
   **But turning the filter off also removed the only thing that ever refreshed the list — see the
   follow-up below, which is the half this milestone got wrong first time.**
4. **Tests** — `ConnectionExpandBindingProbe.Completion_PrefixFirst_ListsOnlyStartsWithMatches`: a
   headless probe over the real `SqlCompletionController` + `CompletionWindow`, reproducing the user's
   catalog verbatim — open at `cont` → only StartsWith rows; **type `i` into the open window** → narrows
   (the refresh path); **backspace** → widens again; `tractor` → no window; no prefix → everything in
   scope. **It has to be a probe, not a Core unit test.** The Core rule was already unit-tested and green
   for the entire time the bug was live; only an assertion against the real window can catch this class.
5. **Validation** — build 0/0, **4594 tests green**, smoke clean.

### Follow-up — the list went stale while typing (2026-07-17, same day)

The user confirmed prefix-first was correct, then reported the list **freezing on screen while typing**:
`select n.id_nagl` kept showing `ID_AKWIZYTOR`. A real regression, introduced by the wiring above, and the
diagnosis is worth keeping because two of this file's own claims were false.

**Root cause (gotcha #234).** `CompletionList.CompletionData` is a plain `List<ICompletionData>`, **not** an
`ObservableCollection`. `OnApplyTemplate` binds `ListBox.ItemsSource = _completionData`, and a `List<T>`
broadcasts nothing — so `RefreshOpenWindow`'s `Clear()`+`Add()` updated the data and **nothing on screen**.
AvaloniaEdit never mutates that list either: its `SelectItemFiltering` assigns a *fresh* `List` to
`ItemsSource`, and **that assignment is its refresh mechanism**. Switching `IsFiltering` off therefore
silently removed the only thing that ever re-assigned `ItemsSource`. The fix mirrors AvaloniaEdit: after
populating `CompletionData`, hand the ListBox a snapshot of it (`ItemsSource = new List<…>(data)`), keeping
the two content-identical because `SelectItemWithStart` indexes into `CompletionData` and applies that index
to the ListBox.

**Why the probe missed it — twice, in ways worth not repeating.**
- The `conti` step claimed to exercise `RefreshOpenWindow`. It did not: its `Type()` helper cleared the
  document, which **closes the window**, so every step was a fresh *open*. The refresh path — the entire
  point of the change — was never executed. It now types into the open window, and backspaces.
- The hook read `ListBox.ItemsSource`, which **was the very list we had just mutated** — the assertion read
  our own input back through the control's front door and could never fail. It was written explicitly to
  avoid that trap ("no fallback to the `CompletionData` we supplied"); object identity, not the property
  name, is what made it circular. `RenderedRowsForTest` now walks `ContainerFromIndex` after
  `UpdateLayout()` and reads what is actually realized (gotcha #235). **Verified by disabling the fix: the
  test fails with `Expected: ["CONTINUE"] / Actual: ["CONTRACT_LINES"]`** — the user's symptom exactly.

**How it was found.** Six reproductions passed (synthetic typing, the real `PerformTextInput` path, the dot
trigger, close-then-open, a genuinely async warm racing the burst) before instrumenting what the *control*
held rather than what we told it. The tell was `ListBox.ItemCount` frozen at its opening value while
`ItemsSource` read correctly — the control had never heard of the change. Reading AvaloniaEdit's actual
source settled it; guessing at its behaviour from memory had cost several wrong hypotheses first.

### Open ranking question to confirm during step 4/5

Keyword-at-bottom means typing a partial like `fr` ranks any object starting `fr` above the `FROM`
keyword (context ranking already boosts tables after FROM etc., but not the leading statement keyword).
The exact-float fix handles the *fully-typed* keyword. Decide during QA whether common leading keywords
deserve a small boost, or leave as-is (objects-over-keywords is defensible for a metadata-heavy user).
