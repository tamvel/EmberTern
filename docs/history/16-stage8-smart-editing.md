# Stage 8 — Smart Editing & Structural Assistance

The milestone after the editor rebuild (Etaps 0–6.9), Stage 7 (Diagnostics) and Unified Hover. Stage 8
adds *editing ergonomics* on top of the existing language front-end — it introduces no new language
capability, only comfort. Guiding principle (user's charter): **the editor helps you write code but
never writes it for you without your explicit decision** — modern-IDE behaviour (VS / Rider / VS Code),
not IBExpert.

Charter milestones: **M1 Structural Matching**, M2 Smart Snippets, M3 Snippet Engine, M4 Structural
Selection (future). Executed one at a time per the session protocol.

---

## M1 — Structural Matching (Related Elements Highlighting) — DONE (impl); awaits user visual confirmation

**Goal.** Make structural relationships instantly visible: matching **brackets** and matching
**BEGIN/END**, reacting to the **caret** position (caret adjacent to the token — modern IDE), not only to
a selection. Generic, not special-cased per construct; sourced from the language front-end (AST + the one
`SqlLexer`), never from ad-hoc text scanning.

**The unification (the heart of M1).** The editor had *two* fragmented "related-elements" highlighters,
both already drawing with `OccurrenceHighlightBrush`:
1. `OccurrenceHighlighter` (App) — text-based, selection-driven, boxes the selected word's occurrences;
2. a semantic, caret-driven reference boxer nested inside `NavigationController`
   (`ReferenceHighlightRenderer` + `ComputeLocalReferenceSpans`), boxing the local symbol under the caret.

Per the user's directive ("evolve the occurrence infra into a general Related Elements Highlighting
system; alias occurrences, brackets and BEGIN/END become different producers feeding one renderer"), both
were folded into **one pipeline**:

- **Core** (`EmberTern.Core.Sql.Language.Matching`, pure/testable): `MatchContext` (text, caret, selection,
  cached `SemanticModel?`), `IRelatedElementProducer`, and `RelatedElementMatcher` (runs the producers,
  merges + de-dupes spans). Producers:
  - `SelectionOccurrenceProducer` — the old text-based occurrences (needs no model → works in the
    model-less DDL preview);
  - `CaretSymbolReferenceProducer` — the semantic caret-symbol references (extracted verbatim from
    `NavigationController`; `NavigationEngine.LocalReferences` gated to script-local symbol kinds; `>= 2`
    draw gate preserved);
  - `BracketMatchProducer` — caret-adjacent `()` / `[]` / `{}`, generic over a pair table. Tokenizes the
    **current** text with the single `SqlLexer` (not a new scanner, and always in sync — no stale flash),
    gated by a cheap caret-adjacency char peek so it only lexes when the caret is actually next to a
    bracket; a bracket inside a string/comment/quoted-ident is not a bracket token, so it is correctly
    never matched; per-family depth scan for nesting;
  - `BlockMatchProducer` — caret-adjacent `BEGIN`/`END` from the **AST**
    (`model.Syntax.Descendants<BlockStatement>()`). One node type covers procedure / function / trigger /
    EXECUTE BLOCK / anonymous bodies AND `IF` / `WHILE` / `FOR` bodies (their body *is* a `BlockStatement`).
    A block's own delimiters = the FIRST `begin` and LAST `end` token in its slice (robust to nesting).
    A `CASE … END` is not a `BlockStatement`, so its `END` is correctly not matched (a future CASE/END
    producer owns it). Validates the text at each span still reads begin/end before drawing (stale-AST
    guard).
- **App** (`RelatedElementsRenderer`, one `IBackgroundRenderer`): maps editor state → `MatchContext`, asks
  the matcher, paints boxed highlights. Recomputes on caret move / selection change / `ModelUpdated`;
  viewport-culled + doc-length-clamped on paint; no analysis on the paint path. Two Attach overloads —
  one taking the completion controller (model + `ModelUpdated`), one taking `() => null` for the
  model-less DDL preview (only the text-based producers contribute there).

**Genericity.** A future structural pair (CASE/END, LOOP, …) is **one more producer**, never another
renderer — exactly the charter's "treat future structural pairs as just another producer."

**Colour.** The old occurrence highlight was "too subtle." The user delegated the colour choice, asking
only for high contrast in **both** themes and palette consistency (not IBExpert red). New unified theme
tokens `RelatedElementHighlightBrush` (fill) + `RelatedElementHighlightBorderBrush` (border) in both
dictionaries: a translucent fill + a strong ~1.5px border (the border is what makes the partner element
pop). Starting hues — burnt-orange on the near-white Light editor, bright amber on the dark editor —
**pending on-screen confirmation** (the hex/hue may be tuned; the token names + treatment are settled).

**Wiring (gotcha #219).** `RelatedElementsRenderer` is attached in **both** seams —
`SqlEditorBehavior.Attach` (object editors) and the `MainWindow` hand-wiring (main SQL editor + the DDL
preview) — replacing the `OccurrenceHighlighter.Attach` calls. `NavigationController` lost its nested
`ReferenceHighlightRenderer` + the per-caret `InvalidateVisual` (the unified renderer owns caret repaint
now); it keeps the Ctrl-hover underline, navigation, rename, peek. Its `ReferencesForTest` seam now
delegates to `CaretSymbolReferenceProducer.Compute`, so the headless probe is behaviour-identical.

**Deprecation, not deletion (user request).** During M1 impl `OccurrenceHighlighter.cs` was kept
**dormant** (unattached, banner comment) and the old `OccurrenceHighlight*` tokens kept in place, both as a
one-line revert path if the new renderer misbehaved. (The `NavigationController` nested renderer could NOT
be left dormant — an unused private class/field fails the build under `TreatWarningsAsErrors` — so it was
removed in place; git is its revert path.)

**M1 finalization (2026-07-16, after user visual confirmation) — cleanup DONE.** Manual QA confirmed the
first-call repaint fix and correct bracket / BEGIN-END / occurrence behaviour, so the dormant rollback path
was removed: `OccurrenceHighlighter.cs` deleted; the obsolete `OccurrenceHighlightBorder*` tokens (fill +
border, both themes) removed. The one still-live consumer of the old fill token — `SearchMatchHighlighter`
(Global-Search preview) — was migrated onto a correctly-named `SearchMatchColor` / `SearchMatchBrush` token
(same values, both themes), so no "Occurrence*" token name survives as drift. A stale orphaned doc-comment
above `NavigationController.UnderlineRenderer` (describing the removed reference boxer) and the two
"Supersedes OccurrenceHighlighter" wiring comments were tidied; the `<see cref="OccurrenceHighlighter"/>` in
`SquiggleRenderer`'s doc (which would have become a `CS1574` build error once the type was gone) now points
at `RelatedElementsRenderer`. Build 0/0, 4187 main + 24 probe green, smoke clean. **M1 is CLOSED.**

**Verification.** Build 0/0. New Core tests `RelatedElementMatchingTests` (27: bracket nesting, brackets
in strings/comments/quoted-idents not matched, caret adjacency incl. empty `()`, unmatched, `[]`/`{}`,
different-family non-crossing; BEGIN/END on begin/end/inside-keyword, nested innermost, IF-body,
EXECUTE BLOCK, CASE-END-not-matched, no-model; selection occurrences + word boundaries; matcher dedupe).
Full suite green: 4186 main + 23 probe. Smoke: app launches cleanly with the theme changes. On-screen
appearance + caret-adjacent behaviour in both themes **awaits the user's visual confirmation** (QA rule).

**Post-M1 QA fix — first-call repaint (gotcha #223).** Manual QA found bracket matching did not activate
on the FIRST call the caret landed on right after connect; clicking a different call worked, and returning
to the first then worked. The matcher is a pure function of (text, caret, model) — proven to return the
pair for the exact `execute procedure name(args)` input (Core pin) and via a real caret move on a real
editor (headless pin) — so the fault was purely the App repaint: a plain `TextView.InvalidateVisual()` on
the first caret-driven repaint could run before the text view's visual lines were rebuilt (window still
settling post-connect), so `Draw` saw `VisualLines.Count == 0` and painted nothing; and the "skip if spans
unchanged" guard then made the miss permanent at that caret position. Fixed: repaint with
`TextView.Redraw()` (rebuilds visual lines + renders — the mechanism `SemanticHighlighter` already uses),
and the guard now skips only the empty→empty case so a missed paint self-heals. Build 0/0; suite 4187 main
+ 24 probe green (+2 pins). Still awaits the user's visual re-confirmation (a render-timing fix).

**Files.** New: `src/EmberTern.Core/Sql/Language/Matching/RelatedElementMatcher.cs`,
`.../Matching/RelatedElementProducers.cs`, `src/EmberTern.App/Completion/RelatedElementsRenderer.cs`,
`tests/EmberTern.Tests/RelatedElementMatchingTests.cs`. Edited: `SqlEditorBehavior.cs`,
`MainWindow.axaml.cs`, `NavigationController.cs`, `Themes/Colors.axaml`, `ConnectionExpandBindingProbe.cs`.
Deprecated (dormant): `OccurrenceHighlighter.cs`.
