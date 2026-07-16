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

## Known limitations / technical debt (for a future pass, not blocking)

- **Grammar arming is the simple 95% rule.** Deliberately misses: statements split by newline *without* a
  `;` (a statement construct won't arm after a value-completer), subquery-`select` right after `(`
  (`(` → None), and it does **not** suppress arming when the caret is inside a **string literal / comment**
  (the resolver matches raw chars; the gate classifies by the previous token but doesn't check whether the
  caret itself is inside a literal/trivia). All are safe under the shown-hint + explicit-Tab contract, and
  smarter grammar can be added later without changing the shape.
- **`doc.Text` per caret move is O(n).** Fine for query/procedure-sized documents; for a very large script
  it materialises the whole text on each caret move. If it ever matters, resolve against `doc.GetText(0,
  caret)` and/or find the previous token by a bounded left-scan — without adding caching/state.
- **No App-layer unit test of the live Tab/hint** (the tunnelled key interception + overlay). The Core
  (resolver, arming, expansion, casing) is fully covered; the App glue relies on those + the user's visual
  QA (QA rule). A headless CompletionWindow-style probe could pin it later if desired.
- **The catalog is intentionally small** (16 constructs). Growing it is one declarative row each; do it in
  response to real "I type this daily" gaps, not speculatively.

## What remains in Stage 8

- **Typing Ergonomics** (the next milestone): `begin…end` as a structural delimiter pair, `()`/`''`/`[]`
  pairing with type-through, and AST-aware auto-indent on Enter (Enter stays a normal editing key). Design
  §3 of the frozen doc. This is where `begin` gets handled — it is NOT a Language-Completion construct.
- **Deferred, separate track:** wiring the prefix-first `CompletionMatcher` (Tool C) into the completion
  engine/App as a passive view (the "Completion Matching Philosophy" work — `17-completion-matching-philosophy.md`).
- **Later / optional:** ghost-text presentation (replaces the hint layer only), a Tab-based "leave the
  construct" ergonomic, user-defined constructs (expose the catalog).
- **Not started:** M3 (Snippet Engine), M4 (Structural Selection).

## Next session should start with

1. The user's **visual QA feedback** on the App milestone (hint feel/placement/wording, Tab behaviour,
   arming positions, Light/Dark) — apply tweaks first.
2. Then **Typing Ergonomics** from this committed baseline.
