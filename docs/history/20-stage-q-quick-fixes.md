# Stage Q — Quick Fixes & Code Actions (2026-07-25)

> **COMPLETE — Q0–Q5 shipped and user-confirmed.** Design + as-built contract:
> [docs/design/editor-quick-fixes.md](../design/editor-quick-fixes.md). This file is the narrative:
> what was decided, what went wrong, and what the failures taught.

The first stage in which EmberTern **modifies the user's code on its own initiative**. Everything before
it either read code or changed it under a fully-specified instruction. That single fact set the tone:
Architecture rule #11 outranked every feature goal, and "offer nothing" was always an acceptable answer.

---

## 1. The seams, and why in that order

| Seam | What shipped |
|---|---|
| **Q0** | The design doc. Three findings from reading the shipped code overruled the earlier §12 sketch (below). |
| **Q1** | Core: `TextEdit`, `CodeAction`, `QuickFixEngine` + one producer (ET0005 qualify-ambiguous-column). No UI. |
| **Q2** | App: `TextEditApplier` — one owner of every document mutation — plus `Ctrl+.`. Rename migrated onto it. |
| **Q3** | The light bulb. Six rounds of QA; the hardest part of the whole stage (§3). |
| **Q4** | The "Did you mean …?" family (ET0001/2/3/4) + `NameSuggestion`. |
| **Q5** | The Diagnostics panel as a third trigger, plus the keyboard/mouse interaction model. |

Q4 was deliberately sequenced **after** Q3: if adding four fixes had touched anything outside one Core
file, the Q1 design was wrong and that was where it would show, on real examples, while correcting it was
still cheap. It touched `QuickFixEngine` and one new pure `NameSuggestion` — no UI, no applier, no
diagnostics engine. The claim held.

---

## 2. Three decisions that came from reading the code, not from planning

**`Diagnostic` must NOT carry the fixes.** The §12 sketch said it would ("additive"). It is not: `Diagnostic`
is a `readonly record struct` whose VALUE equality the Diagnostics panel depends on to skip a rebuild when
a keystroke leaves the findings unchanged. A list member degrades that to reference equality, so identical
findings would compare unequal and the panel would rebuild on every keystroke. Fixes are computed **on
demand** instead — which also states the one-source-of-truth rule more strongly: the engine is handed a
finding and can only answer how to repair it.

**The §0-safe apply idiom already existed.** `NavigationController.TryApplyRename` had it: verify the text
at every target span still reads what the model said, apply last-to-first, wrap in one undo unit. Rather
than reinvent it, Q2 generalized it into `TextEditApplier` and migrated rename onto it — so there is now
exactly ONE thing that writes to a user document on EmberTern's behalf. To make the shared drift check
*mean* something for rename, `NavigationRename.Occurrences` gained the text the binder saw at each
occurrence; without that the applier would have compared the document with itself.

**Hover stays read-only.** CLAUDE.md's holding note suggested putting the fix list in the hover card. That
contradicted §15.1.1 ("plain hover = information, Ctrl = actionability") and `HoverInfo`'s own documented
"read-only, so §0 holds by construction". The card instead gained a one-line hint naming the shortcut.

### Naming, evaluated rather than assumed

The user proposed renaming everything to `CodeAction`/`CodeActionEngine`. The mechanism splits in two, and
the halves differ in generality: the **currency** is genuinely general (it already describes rename, an
existing consumer, so calling it `QuickFix` would be wrong *today*) while the **producer** is not — its
entry point requires a `Diagnostic`, whereas refactorings are caret-driven. So: `CodeAction`/`TextEdit` for
the currency, `QuickFixEngine` for the producer, and a future `RefactoringEngine` is a sibling rather than
a rename. This mirrors Roslyn's `CodeFixProvider`/`CodeRefactoringProvider` over a shared `CodeAction`.

`CodeAction` was also deliberately **not** made an abstraction over "some action": every action that
exists, and every one on the v1 list, is a set of text edits, so a discriminator no caller could branch on
would be dead code. The cost of waiting was measured instead — one optional member plus ONE branch at the
App's activation point — which is why activation is funnelled through a single method.

---

## 3. The light bulb: six rounds, and what each one actually taught

Q3 shipped, and the user reported the bulb simply never appeared — while `Ctrl+.` worked at the same
caret. It took six rounds, and every wrong turn was instructive.

1. **"The diagnostics must not be firing."** A tracing probe over realistic queries proved the Core half
   sound: ET0005 fired and the engine returned two actions for every shape. The fault was above it.
2. **`VisualLines` accessed outside the render pass.** Real defect: the placement idiom was lifted from
   `InlineValuesRenderer`, an `IBackgroundRenderer` whose `Draw` only runs when visual lines are valid by
   construction. The bulb positions from a timer tick, where that guarantee does not transfer — and this
   codebase already had the rule written down (`EditorPopups`: *"never access VisualLines while it's
   invalid"*). Fixed, but not the cause.
3. **A re-entrancy the fix itself introduced.** `UpdateBulb`'s hide/show pair could be re-entered, because
   mutating the overlay changes layout and can raise `VisualLinesChanged` synchronously. Both passes added
   a control, only the last was remembered, the first was stranded forever. The test caught it.
4. **The show path depended on a timer no test could reach.** After the model settles, moving the caret
   raises neither `VisualLinesChanged` nor `ModelUpdated` — the only trigger left was a 450 ms dwell, and
   headless runs no `DispatcherTimer` at all (measured: a control timer ticked 0 times). The single link
   that mattered was the single link nothing could pin. The dwell was **removed** rather than trusted;
   caret movement now evaluates immediately, which is safe because the bulb is anchored to a span and does
   nothing when that span is unchanged. The failing path became testable in the same move.
5. **Live instrumentation, because reasoning had run out.** `BulbTrace` logged every decision to
   `%TEMP%\EmberTern-debug.log`. The user's log showed the bulb added, sized 16.8×16.8, correctly
   positioned, `IsEffectivelyVisible=true` — every state healthy.
6. **The cause.** One line, added after noticing the instrumentation never logged the *brush*:
   `BULB BRUSH findResource=NULL themeAware=True theme=Dark`. `Control.FindResource(key)` supplies no
   theme variant, so every brush in `ThemeDictionaries` came back UNSET and `as IBrush` made it null.
   `SvgIcon` **strokes** its geometry with `Foreground` — so the control painted nothing while having a
   size, a position and a parent. Geometries are not theme-scoped, so `Data` resolved through the same
   broken call and hid the problem: `dataNull=False` was true and useless.

Then two UX passes on top: the bulb moved from the line end (measurably correct, practically useless —
nobody looks at the right margin) to the flagged symbol, got its own `CodeActionBrush`, and finally became
a **filled** icon, because at 14px a stroked outline reads as an empty ring.

**The through-line:** every test asserted the bulb was *added, measured and positioned* — none asserted it
had anything to paint **with**. See gotchas #250 and #251.

---

## 4. The v1 action set, and what was refused

Shipped: qualify an ambiguous column (one action per candidate table — the user picks the meaning,
EmberTern never guesses which table), and "Did you mean …?" for the four unknown-name categories when
**exactly one** candidate is close.

Refused, with reasons recorded so they are not re-litigated: **ET0006** (repairing an INSERT count mismatch
needs to know which column or value was meant — unknowable), **ET0008**, and the obvious-looking "declare
the missing variable", which would have to **invent a type**. That last one is precisely what rule #11
forbids; it becomes possible only if a producer can *derive* the type with certainty.

Two defects the Q4 tests caught before they shipped:

- **The `:` sigil.** A variable reference's span includes it, so the naive replacement dropped it — and
  inside an embedded DSQL statement `:v` is a VARIABLE while `v` is a COLUMN. The fix would have silently
  changed what the code means.
- **ET0001's real shape.** It is emitted for an `EXECUTE PROCEDURE` of an unknown routine, not for an
  unknown table in `FROM`. The first test shapes assumed otherwise and were vacuously empty.

And one UX correction: a fix now keeps **the user's capitalisation** rather than importing the catalog's.
Firebird folds unquoted identifiers, so the two spell the same name — taking the catalog's would be a
gratuitous restyling of their code, not part of the repair.

---

## 5. One menu, four doors

The interaction model settled in Q5: `Ctrl+.` → ↑/↓ → Enter, `Ctrl+.` → single click, the bulb → single
click, and the Diagnostics panel's "Quick Fix…". All four run `GetActionsAtCaret` → the menu →
`InvokeCodeAction`; none owns a way to obtain or perform an action.

Two things made that real rather than nominal. **Focus stays in the editor** (an overlay-hosted ListBox
does not reliably take keyboard focus, and a menu whose arrows depend on that is a menu that needs a
mouse), with the keys handled on the **tunnel** because AvaloniaEdit's TextArea consumes Escape and the
arrows at the source. And a **single click** applies, because the ListBox has already moved the selection
onto the pressed item — so "click" and "Enter on the selection" are literally the same operation rather
than two behaviours to keep in step. See gotcha #252.

The panel reaches that flow through an attached property published by the one attach seam: the panel is
hosted by eleven views, none of which build the `NavigationController`. That also gets the read-only case
right for free — a DDL preview is never attached, so the property is null and the item does nothing.
