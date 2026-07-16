# Language Completion & Typing Ergonomics — as-built

The redesign of the code-writing experience that **replaced Stage 8 M2 (Smart Snippets)** after the user
tried M2 and rejected VS/Rider-style interactive snippet sessions ("now I delete half of this"). Design is
frozen in **[docs/design/editor-language-expansion.md](../design/editor-language-expansion.md)** — read it
first; this file is the "what we actually built, in what order, and why" companion. Provenance of the
pivot: `16-stage8-smart-editing.md` (M2 built-then-reverted).

The three tools, chosen by grammar, never by the user: **IntelliSense** (names, prefix-first, idle-debounced
— `CompletionMatcher`, Tool C); **Language Completion** (finishes daily Firebird *constructs* the developer
already started typing — Tab + a shown hint, synchronous/timing-free); **Typing Ergonomics** (`begin…end`
pairing + auto-indent — Enter stays a normal editing key). Governing rules: **Rule 0** (never generate code
the user deletes) and **obviousness** (anything special Tab does is shown on screen first).

## Milestones (all committed)

1. **Revert M2 + freeze design** — commit `2f89239`. Uncommitted M2 (`SnippetLayout`,
   `EditorSnippetExpander`, mirrored placeholders, final-caret, enriched row) reverted to the Etap-5
   snippet baseline. `CompletionMatcher` (prefix-first, Tool C) kept. Design doc frozen.
2. **Core foundation** — commit `d0c7e61`. New pure namespace `EmberTern.Core.Sql.Language.Constructs`:
   `LanguageConstruct` + `LanguageConstructCatalog` (declarative rows: spelling + minimal expansion +
   caret offset + category) and `LanguageConstructResolver.Match(text, caret)` — natural-prefix,
   case-insensitive, multi-word aware, silent-until-unique **within the curated catalog**. `begin…end` is
   deliberately absent (delimiter pair → Typing Ergonomics). +19 tests.
3. **Grammar-aware arming** — commit `6d0ddf8`. `ConstructCategory` per row (Statement | Clause) +
   `ConstructContext.Classify(text, pos)` — a deliberately simple, deterministic **previous-significant-token**
   rule (statement boundaries → Statement; value-completers → Clause; else None), one cheap synchronous lex,
   no AST/model, no timing. `LanguageConstructResolver.Resolve(text, caret)` = prefix match ∩ grammar is the
   single App entry point. +25 tests.
4. **App layer** — commit `c937723` (this session's final milestone; **awaits the user's visual QA**).
   `ConstructExpansion.For` (pure Core) turns a match into the exact `ExpansionEdit`, including the one
   decision the App must not own — casing (match what was typed). `LanguageExpansionController` (App): a
   thin, **stateless** consumer — the armed construct is re-derived from (text, caret) on every caret move
   and on Tab, nothing remembered. Tab expands (tunnelled KeyDown preempts indent; falls through otherwise);
   a passive `OverlayLayer` hint (`⇥ <expansion>`) shows exactly what Tab inserts. Attached in **both** seams
   (`SqlEditorBehavior.Attach` + `MainWindow`, gotcha #219). +4 tests.

5. **QA sprint — hint fidelity, one owner per keystroke, snippet removal** (this session; **user-approved,
   Language Completion considered COMPLETE**). Four rounds of the user's interactive QA against the running
   app, each finding something the Core tests structurally could not. Detail below.

## QA sprint (2026-07-16) — what QA found that tests could not

**Round 1 — the hint could lie, and Tab could misfire.** Four defects, all in the App layer:
- **Casing:** the hint rendered the catalog's lowercase `Expansion` while Tab inserted the *case-matched*
  text — `IF` previewed as `if () then` and inserted `IF () THEN`, breaking the obviousness principle
  (design §2.2: the hint shows the exact result). Fixed structurally, not by patching the string: one method
  (`CurrentEdit()`) now returns the very `ExpansionEdit` Tab applies and the hint renders *that object's*
  text, so preview and result are the same value and **cannot** drift.
- **Escape:** dismissed the card but Tab still expanded — a hidden special action, exactly what §7 forbids.
  The controller was stateless by construction, which is why Escape had nowhere to record itself. Added the
  one field a pure function genuinely cannot derive: `_dismissedAt` (the caret offset of the dismissal,
  retired on the next caret move). The controller still remembers nothing about *what* is armed. The user's
  framing on the trade: *"the stateless principle is there to avoid duplicated logic, not to violate the UX
  contract."*
- **Selection:** Tab expanded instead of block-indenting when text was selected — it could eat selected code.
- **Focus:** the hint could float over another control after the editor lost focus.

The last three became guards inside the same single decision point, so no trigger can disagree with the Tab
handler.

**The headless probe caught a bug in the fix.** Written to *prove* the focus guard rather than believe it, it
failed on its first assertion: `TextArea.IsKeyboardFocusWithin` was `False`. Ground truth (gotcha #225):
AvaloniaEdit's **`TextEditor` is not focusable at all** — `editor.Focus()` is a no-op returning `false`;
focus lives on the `TextArea`, which is what a real click focuses. The guard was right and the *test* was
wrong — but had the guard read `editor.IsFocused`, Language Completion would have been **silently dead on a
green build**. Getting the probe to run also forced gotcha #226: the class was starting **24 headless
sessions** (one per test), and AvaloniaEdit's static `KeyBinding` lists made any real key into an editor
throw cross-thread from every session after the first. Fixed with one shared session — what #94 always meant
— which also cut the partition from 16s to 5s.

**Round 2 — grammar arming was too dependent on the previous token.** The user reported `where` ⏎ blank ⏎
`if` arming nothing. A classifier dump proved the diagnosis exactly: `prefixMatch=if` in every failing case —
**the matcher was right; the gate refused it**. `where` is a non-boundary keyword → `None`; and had the WHERE
been completed (`= 1`), the previous token would be `1` → `Clause` → the Statement construct `if` is refused
either way. Fix: `ConstructPosition` became a `[Flags]` **set** so a caret can be both, and a **blank line
adds `StatementStart` without removing `Clause`** — widening never removes a position, so nothing regressed
(all existing tests unmoved). `(` also arms statement constructs (subqueries, CTE bodies, derived tables).

**Verification, then removal — the user's ordering.** Because exclusive ownership turns an under-armed
position into a *dead zone*, the user required proof before IntelliSense stopped covering these words:
*"there must never be a place where neither system offers them."* A table of all 16 constructs × 33 real
positions found two further dead zones. `INSERT … SELECT` was closed with a bounded look-back to the
enclosing statement's first two tokens (the previous-token rule structurally cannot see it) — design §5's
sanctioned "cheap local lex of the enclosing statement", gated on `Clause` so a `VALUES(…)` slot stays
silent. `OVER (ORDER BY` is left as a **conscious documented exception**: closing it needs `(` → `Clause`,
arming clause constructs after *every* paren. The table is now a permanent test: a future catalog row that
arms nowhere fails the build.

**Round 3 — the separation was only half implemented, and the missing half was the dangerous one.** A
screenshot showed `select` inserting a procedure called `SELECT_PRACOWNIKOW`. Vocabulary filtering could
never have stopped it — a *procedure name* is not a keyword. The identifier list auto-popped on the prefix,
and an open list owns Tab (gotcha #228). The frozen design §1 had already said *"where a construct may begin,
the identifier list doesn't auto-pop"* — that sentence was simply never implemented. The auto-pop path now
asks the **same** `Resolve(text, caret)` the hint asks; Ctrl+Space still overrules the grammar.

**Ownership is declared by its owner, derived by its consumer** (user directive: *"don't hard-code this
exclusion list… think in terms of ownership"*). `LanguageConstructCatalog.OwnedWords` derives the trigger
words from the catalog's own rows; `KeywordPairCatalog` (new, **data only**) declares `begin`/`end` for
Typing Ergonomics. `CompletionEngine.AddKeywords` reads both. Adding a construct or a pair retires its word
automatically — the two cannot drift. Tests assert over the owners' declarations, not word lists, so they
hold for rows that do not exist yet.

**Round 4 — an empty completion window, and the last of M2.** Removing `BEGIN` exposed gotcha #227: the
emptiness guard tested the *unfiltered* candidate set, and the filter that empties it ran afterwards. Fixed
against AvaloniaEdit's own `CurrentList`. Deliberately **not** fixed with `CompletionMatcher`, which would
have smuggled prefix-first matching in ahead of its own milestone.

Then the user spotted the real remainder: the completion list still offered `if (…) then begin … end`,
`begin … end`, `case when … then … else … end`. These were the **Etap-5 keyword live templates** — the
baseline M2 was reverted *to*, not a leftover of M2 itself. Design §11 had already ruled that "the
`SnippetEngine` role in the completion list" goes; the revert had simply stopped short. Removed:
`SnippetEngine` + `SnippetTemplate`, `SnippetCompletionData`, the controller wiring, `SnippetEngineTests`.
**Kept** (only shares the word): the object-driven drag-drop templates — `Core/Sql/Templates/*`,
`SqlSnippetDropTarget` — which `SnippetEngine`'s own docstring called a separate path. Two simplifications
fell out: the P7 auto-trigger exception (which existed so a 2-char `if` snippet could pop the list — the
exact competition being removed) is gone, and `ShowBaselineWindow` became byte-identical to `ShowItems`, so
one window-populating path remains instead of two.

**Left open by explicit user decision:** `OVER (ORDER BY` (above); the single-letter clause arms (`g`/`h`/`o`
collide with aliases — `from ORDERS o` arms `⇥ order by `), kept pending real usage rather than
pre-optimised; grammar-first uniqueness (`wh` after FROM stays silent), same reason; and **CASE stays an
IntelliSense keyword** — *"I'd rather grow the catalog based on real usage than theoretical completeness."*

## Architecture decisions (App milestone)

- **Stateless by construction.** The controller holds only the presentation card; every *decision* comes
  from `Resolve(text, caret)`, re-evaluated per caret move and per Tab. No cached "armed" state, no timers,
  no async — the Core is cheap enough to run on every keystroke (user directive).
- **Tab interception via a TUNNEL KeyDown handler** (gotcha #224) — a bubbling `+=` loses the race to
  AvaloniaEdit's built-in Tab-indent. Only consumes Tab when a construct is armed; bails when the completion
  list is open (the list keeps Tab for accept).
- **Casing is a Core decision** (`ConstructExpansion.For` via `CaseMatcher`/`SqlCaseStyleDetector`), so the
  App applies a ready-made edit and never re-implements the rule.
- **Hint is pure presentation** — `OverlayLayer`-hosted (reusing `EditorPopups`/`ClampIntoOverlay`), below
  the caret line (never covers the caret), viewport-clamped, non-focus/non-hit-test, theme-tokened,
  font-scaled; hidden the instant nothing is armed and never while the completion list is up.
- **`Resolve` takes `doc.Text`** each call. Accepted per "the Core is cheap"; see debt below for the
  large-document caveat.

## Architecture decisions (QA sprint)

- **The hint cannot lie *by construction*, not by discipline.** `CurrentEdit()` returns the same
  `ExpansionEdit` object Tab applies and the hint renders its `InsertText`. Preview and result are one value,
  so no future per-site decision (casing today, anything else later) can drift between them.
- **One decision point.** Focus, selection, the completion list, the Escape dismissal and the resolver call
  all live inside `CurrentEdit()`. Every subscription (caret move, selection change, focus in/out) only says
  *re-evaluate*; none decides anything. A trigger therefore cannot disagree with the Tab handler.
- **Exactly one piece of interaction state.** `_dismissedAt` exists because "the user said not here" is not a
  function of (text, caret) — no pure resolver can know it. Everything about *what* is armed stays derived.
- **Ownership is declared by the owner and derived by the consumer.** `LanguageConstructCatalog.OwnedWords`
  and `KeywordPairCatalog.OwnedWords` are computed from their own catalogs; `CompletionEngine.AddKeywords`
  reads them. No hand-kept exclusion list exists to drift. Tests assert over the declarations, so they cover
  rows that do not exist yet. User framing: *"think in terms of ownership… whenever a Firebird language
  construct eventually becomes owned by Language Completion, IntelliSense should automatically stop offering
  it."*
- **Separation is by grammatical position, not just vocabulary** (gotcha #228). Both halves of design §1 are
  now implemented: constructs don't arm where you name things, **and** the identifier list doesn't auto-pop
  where a construct may begin. Both halves consult the *same* `Resolve`.
- **`KeywordPairCatalog` is data with no behaviour** — a deliberate, minimal exception to "don't pre-build
  the next milestone". `begin` needed an owner *now* (to leave IntelliSense), the pairing behaviour is
  Typing Ergonomics' job, and putting `begin` in the construct catalog instead would have contradicted the
  frozen §13 decision. Typing Ergonomics consumes this catalog rather than redeclaring it.
- **Widen, don't narrow, when the gate is uncertain.** Once a word has one owner, over-arming costs a hint
  you ignore; under-arming costs a dead zone. Hence `ConstructPosition` as a flag set, and a blank line
  *adding* `StatementStart` rather than replacing `Clause`.

## Known limitations / technical debt (for a future pass, not blocking)

- **Grammar arming is the simple 95% rule** — but the bar rose once Language Completion became the
  *exclusive* owner of these words, because an under-armed position is now a dead zone rather than a
  harmless miss (the completion list no longer covers it). Verified across 16 constructs × 33 positions
  (`ConstructArmingTests.EveryConstruct_ArmsWhereItMayBegin`), 32 of 33 correct. Fixed since the App
  milestone: statements split by a **blank line** without a `;`, subquery-`select` after `(`, and
  `INSERT … SELECT`. Still deliberately missing:
  - **`OVER (ORDER BY …`** — a conscious exception. `(` arms Statement constructs, not Clause ones; closing
    it needs `(` → Clause, which would arm `group by`/`having`/`order by` after *every* paren
    (`count(o…`, `values (o…`) to serve a construct typed inside window functions only. Costs 4 keystrokes
    there, and is the one place where neither system offers the word.
  - **Statements split by a newline with no blank line and no `;`** (`where` ⏎ `if`) — genuinely ambiguous
    text; the blank line is the intentional signal.
  - **Arming inside a string literal / comment is not suppressed** — the resolver matches raw chars and the
    gate classifies by the previous token, neither checks whether the caret itself sits in a literal/trivia.
    Safe under the shown-hint + explicit-Tab contract.
- **The single-letter clause arms collide with aliases.** Only five letters arm on one character: `s`/`f`
  (Statement) and `g`/`h`/`o` (Clause). The clause three collide with single-letter aliases — `from ORDERS o`
  arms `⇥ order by ` while you type the alias, and `join B on` flashes the hint on `o` and drops it on `n`.
  Known and **kept pending real usage** (user decision): the fix, if wanted, is a two-character minimum for
  `g`/`h`/`o`, which kills the alias collisions and the `on` flash in one move.
- **Uniqueness is measured across the whole catalog BEFORE grammar gating**, so `wh` after a FROM stays
  silent even though `where` is the only clause construct starting with "wh". Grammar-first uniqueness would
  arm it, at the cost of a single `u` arming `⇥ union ` while typing an alias. Kept simple by user decision:
  *"I'd rather evaluate the actual typing experience first than optimize this prematurely."*
- **`doc.Text` per caret move is O(n).** Fine for query/procedure-sized documents; for a very large script
  it materialises the whole text on each caret move. If it ever matters, resolve against `doc.GetText(0,
  caret)` and/or find the previous token by a bounded left-scan — without adding caching/state.
- ~~**No App-layer unit test of the live Tab/hint**~~ — **closed by the QA sprint.**
  `ConnectionExpandBindingProbe.LanguageCompletion_HintNeverLies_AndYieldsTabWhenNotArmed` drives the real
  `SqlEditorBehavior.Attach` seam and pins the six live rules: TextArea really holds focus (#225), the hint
  shows exactly what Tab inserts (casing included), Tab inserts precisely that, Escape → Tab indents again,
  a selection → Tab block-indents, and focus loss removes the hint. It raises keys directly at the
  `TextArea` rather than via headless input injection — see #226 for why that is not optional.
- **The auto-pop suppression is not unit-tested** — it rides a `DispatcherTimer`, which headless cannot
  advance reliably. The decision it makes is the same pure `Resolve` the hint uses (fully covered); only the
  timer path is unpinned, and it was verified by the user's interactive QA.
- **The catalog is intentionally small** (16 constructs) — and that is now a *stated policy*, not an
  accident of scope. The user declined to add CASE when it came up: *"I don't think we should try to migrate
  every SQL keyword into Language Completion… I'd rather grow the catalog based on real usage than
  theoretical completeness."* Growing it is one declarative row (and ownership then follows automatically);
  do it in response to real "I type this daily" gaps.

## What remains in Stage 8

- **Typing Ergonomics** (the next milestone): `begin…end` as a structural delimiter pair, `()`/`''`/`[]`
  pairing with type-through, and AST-aware auto-indent on Enter (Enter stays a normal editing key). Design
  §3 of the frozen doc. This is where `begin` gets handled — it is NOT a Language-Completion construct.
  **It already has a foothold:** `Core/Sql/Language/Ergonomics/KeywordPairCatalog` declares `begin`/`end`
  (data only, no behaviour) so ownership holds today; the milestone should consume that catalog, not
  redeclare the pair. Until it lands, `begin` is deliberately **ownerless** in the UI — no hint, no pairing,
  and not offered by IntelliSense. The user confirmed this is the milestone boundary showing through, not a
  regression: *"this is simply the next milestone waiting to be implemented, not a reason to move `begin`
  back into IntelliSense."*
- **Deferred, separate track:** wiring the prefix-first `CompletionMatcher` (Tool C) into the completion
  engine/App as a passive view (the "Completion Matching Philosophy" work — `17-completion-matching-philosophy.md`).
  Note: the QA sprint deliberately did **not** use `CompletionMatcher` to fix the empty-popup bug (#227),
  because doing so would have imposed prefix-first matching ahead of that milestone, which must land
  atomically.
- **Later / optional:** ghost-text presentation (replaces the hint layer only), a Tab-based "leave the
  construct" ergonomic, user-defined constructs (expose the catalog).
- **M3 (Snippet Engine)** — still listed in the Stage 8 charter, but note the QA sprint **deleted** the
  Etap-5 `SnippetEngine` (design §11). M3, if it ever happens, starts from scratch rather than from that
  code; git holds it. Given the user rejected template insertion outright (M2, and again here), the bar for
  M3 existing at all is high. **M4 (Structural Selection)** not started.

## Next session should start with

**Typing Ergonomics**, from this committed baseline. Language Completion is **user-approved and complete** —
*"So for now I'd consider Language Completion complete."* The open items above (`OVER (ORDER BY`, the
single-letter alias collisions, grammar-first uniqueness) are conscious decisions to revisit only if real
usage complains, not unfinished work.
