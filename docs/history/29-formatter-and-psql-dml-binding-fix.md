# 29 — Three defects from one session: the formatter's dropped comment, DML binding in a PSQL body, and a card that outlived its tab

**Date:** 2026-08-10 · **Scope:** `EmberTern.Core` (formatter + semantic binder) + `EmberTern.App`
(Parameter Helper, §6) · **Build** 0/0 · **Suite** 8387 + 214 + 55 = **8656** green · **smoke** clean ·
`Lab/` untouched.

§1–§5 are one report; §6 is a separate report from later the same session.

The user reported one routine with two symptoms: *"the autoformatter cannot cope with this case, and one
`zasobtechcrp` has no colour"*. They turned out to be two independent defects, and both were **wider than
the report** — which is why the whole session was measurement-first.

```sql
CREATE OR ALTER PROCEDURE XXX_AKTCZASNARZ (ID_TECHNOLOGIA INTEGER) AS
  …
begin
  for select zc1.id_operacjatech, zc1.id_zasobtechcrp
	from operacjatech ot
inner join zasobtechcrp zc1 on (zc1.id_operacjatech = ot.id_operacjatech)
where ot.id_technologia = :ID_TECHNOLOGIA and zc1.id_zasobrodzaj = 3   --szukam narzedzi w operacjach
into :ID_OPERTECH, :ID_ZASOB
do begin
   select first 1 zcr1.czas, zcr1.czastpz, zcr1.czaszasobu
   from zasobtechcrp zcr1
   where zcr1.id_operacjatech = :ID_OPERTECH and zcr1.id_zasobrodzaj = 2   --szukam czasow z ganizda
   into :CZAS, :CZASTPZ, :CZASZASOB;

   update zasobtechcrp zc
   set zc.czas = :CZAS, zc.czastpz = :CZASTPZ--, zc.czaszasobu = :CZASZASOB
   where zc.id_zasobtechcrp = :ID_ZASOB; --aktualizauje czas narzedzia
end
    suspend;
end
```

---

## 1. The reported symptom was NOT the one I could see in the screenshot

My first reading of the screenshot said line 20 (`from zasobtechcrp zcr1`) was uncoloured. **The measurement
said otherwise**: with a metadata snapshot holding both tables, the model resolved `operacjatech` (join FROM),
`zasobtechcrp` (join) and `zasobtechcrp` (the nested SELECT's FROM) — and produced **no reference at all** for
the `zasobtechcrp` of `update zasobtechcrp zc`. Three occurrences, two references. Exactly *"one
`zasobtechcrp` has no colour"*, but a different one than I had picked out of the image.

⭐ Recorded because the correction cost nothing here and would have cost a wrong fix: **a screenshot tells you
a word looks wrong, never which word** — the reference dump is what identifies it.

## 2. Defect A — a DML statement in a PSQL body bound NO table

`SemanticBinder.Psql.BindBodyStatement` was a **second dispatch** over the same five statement kinds that
`SemanticBinder.Dml.BindDml` handles. It bound each statement's embedded subqueries, walked its expressions —
and **never declared the statement's TARGET**. Measured, with metadata present:

| statement | top level | inside a routine body |
|---|---|---|
| `update zasobtechcrp zc …` | resolves | **nothing** |
| `insert into zasobtechcrp …` | resolves | **nothing** |
| `delete from zasobtechcrp zc …` | resolves | **nothing** |
| `update or insert into zasobtechcrp …` | resolves | **nothing** |
| `merge into zasobtechcrp zc using …` | resolves | **nothing** |
| `select … from zasobtechcrp zc` | resolves | resolves |

So it was never about one word: **every DML statement inside a procedure, trigger or `EXECUTE BLOCK` — i.e.
most of an ERP codebase — resolved no table.** The reason only one word looked wrong is that in this routine
only one DML statement names a table that also appears elsewhere, coloured.

⭐⭐ **The invisible half is the worse half.** With the alias undeclared, `BindDottedReference` deliberately
records nothing for an unresolved qualifier — so the statement's entire `zc.czas` / `zc.czastpz` /
`zc.id_zasobtechcrp` set had **no reference either**: no colour, no Quick Info, no Ctrl+Click, no
find-references, and no unknown-column check. After the fix the same routine yields 3 new qualifier
references and 1 new table reference.

### The fix: delete the parallel path, do not complete it

The tempting move — add the missing `DeclareTargetAt`/`DeclareTargetAfter` calls to the five arms in
`BindBodyStatement` — was rejected: it would be a **second copy of the rule "where a DML statement's target
is"** (`UPDATE t` / `INSERT INTO t` / `DELETE FROM t` / `MERGE INTO t USING src`), and two copies drift with
only one of them tested. Instead the structural half of `BindDml` was extracted into
**`BindDmlTablesAndQueries`** — the one owner — and `BindBodyStatement`'s five arms collapsed into a single
arm that calls it. The five per-kind arms are gone.

⚠ Three boundaries kept deliberately:

- **The statement gets its own child scope** (`NewDmlScope(dsql, scope)`), not the body scope: two statements
  may reuse one alias for different tables, and a leaked alias would resolve the second statement's columns
  against the first statement's table — a *wrong* answer, worse than none. Pinned.
- **Its parent is the enclosing body scope**, which is what keeps `:variables` and parameters resolving up
  the chain. Pinned — rooting it at `_root` would have silently unbound every local a routine's DML uses.
- **The expression walk stays the PSQL one** (`BindPsqlExpression`, not the DSQL `BindExpressionReferences`):
  a bare name in a routine body has PSQL-specific resolution and its ET0003 conservatism is deliberate.
  **Only the structure is shared.**

⚠ **New visible behaviour, correct but new:** an unknown-column diagnostic (ET0002) can now appear inside a
routine's DML, where the whole statement used to be silent. That is the binding working; it is metadata-gated
as everywhere else.

### What the existing test suite said about it — the sharpest finding of the session

`SemanticHighlightConsistencyTests` **exists for this exact symptom**. Its doc comment reads *"a schema object
must be highlighted the SAME way regardless of the statement kind or position it is used in. The reported
symptom was «an object is coloured in FROM but not in UPDATE»"*, and it pins all five DML kinds.

**At the top level only.** Its routine-body rows pin a *query* (`FOR SELECT`, a scalar subquery). Two
independent axes — statement KIND × standing in a PSQL body — each varied while the other was held fixed, so
the crossing was never tested, and that is the cell the defect lived in for the suite's whole life. The word
*"position"* in that doc had silently come to mean *clause position*, never *nesting*.

Eight crossed cases were added there; planting the defect back fails **all 8 and none of the 14
pre-existing** ones — proof the old suite could not see it. → gotcha **#360**.

## 3. Defect B — the formatter dropped a comment, and §0 turned that into "does nothing"

The routine would not reformat **at all**. Bisecting the five comments isolated exactly one trigger: the
`--szukam narzedzi w operacjach` at the end of the `where` line **inside the `for select` header**. The
commented-out `--, zc.czaszasobu = …` glued into the `SET` list formatted fine, as did the trailing comment
after a `;` and the leading `/* … */`.

**Mechanism (measured, not deduced — my first hypothesis was refuted).** The natural guess was *"`EmitQuery`
drops comments"*; the measurement said no — a comment inside a clause was preserved all along.
`EmitSelectQuery` renders a query from its **clauses**, and `Flatten` materialises a comment from the
**leading trivia of the token it precedes**, so a clause renders every comment preceding one of *its* tokens.
A comment between the last clause and whatever **closes** the query precedes a token **no clause owns** — and
was rendered by nobody.

Reading the output **before** the safety net (the private `FormatStatement` is a pure function, so reflection
suffices) showed the comment simply absent, and three distinct owners of that gap:

| shape | where the lost comment lives | who renders the tail |
|---|---|---|
| `select … --c` `;` | trivia of `;`, **outside** the query node | the `;` re-attacher |
| `select … --c` `into :x;` (PSQL) | trivia of `into`, **inside** the node, covered by no clause | `FormatSelectLeaf` |
| `for select … --c` `into :x do` | trivia of `into`, **outside** the node | `EmitForSelect` |
| `create view v as select … --c` `;` | trivia of `;` | `FormatViewStatement` |

The fourth row was found **by the compiler**: replacing the one-purpose `WithSemicolon` with a shared tail
emitter broke its other call site, which turned out to have the same defect. A change of *shape* enumerates
its own use sites.

### The fix: one rule, two small helpers, four call sites

- **`EmitQueryTail`** — renders everything past the query node, putting a leading comment on its own line
  (a `;` glued after `--c` would be commented out — the very loss §0 would catch again). It replaced
  `WithSemicolon` **and** a hand-rolled duplicate of the same loop inside `FormatSelectLeaf`.
- **`StartOfCommentRunBefore`** — the run of comments immediately before a tail keyword belongs to *its*
  line. Used by `EmitForSelect` (for the `INTO`/`DO` boundary, whichever exists) and by `FormatSelectLeaf`.

⚠ **Visible consequence, worth knowing:** such a comment now lands on **its own line** above `into`, not at
the end of the `where` line where the user typed it. Deliberate — the `WHERE` may itself wrap onto several
lines, so "the end of the where" is not a stable place to append to, whereas a comment on its own line is
predictable and cannot swallow anything. Comments the emitter already handled (inside a clause, in a `SET`
list, after a `;`) are untouched, and both are pinned.

### Why nothing caught it, and what now does

⭐⭐ **§0's net converts this class of defect into silence.** Reverting a statement to verbatim preserves every
lexeme *perfectly*, so `SqlFormatterSafetyTests` and `SqlFormatterInvariantsTests` — whose entire subject is
lexeme preservation — were green for the defect's whole life. The assertion that can fail is **"the net did
NOT fire"**: feed a non-canonical input and require the output to have *changed*. That is what every case in
the new `SqlFormatterCommentPlacementTests` asserts, plus idempotency and a comment count taken **from the
input** rather than typed by hand.

⚠ Second reason it survived: the shared `SqlTestCorpus` held **almost no comments**, so no round-trip theory
had ever placed one in that position. Eight comment-bearing shapes were added to `StructuralConstructs`, which
puts them through the §0 round-trip, idempotency, casing and AST-differential machinery for good. → gotcha
**#359**.

## 4. Verification

- **Four plants, each caught by its own tests and nothing else:** the `EmitForSelect` boundary (3 tests), the
  `FormatSelectLeaf` INTO head (2), the shared tail emitter (2 — top-level select + view), and the binder's
  target declaration (6 in `SemanticModelTests`, 8 in `SemanticHighlightConsistencyTests`).
- **The user's routine** now formats, is idempotent, and every comment appears exactly once; all four table
  occurrences resolve.
- Suite green in the three documented partitions.

⚠⚠ **A process note worth carrying:** the first full run reported **13 failures**, none of them mine — my
partition filter was a **hand-copied list of 12 headless class names** while the code has **18**, so six
headless classes leaked into the main partition and hit the documented race over the global `Loc` catalog.
Deriving the filter from the source (`grep -l HeadlessCollection`) gave 0 failures. **A hand-maintained list
of names in prose goes stale exactly like a count** — the same shape CLAUDE.md already records for the test
totals. Derive it; do not transcribe it.

## 5. Files

| file | change |
|---|---|
| `Core/Sql/Language/Semantics/SemanticBinder.Dml.cs` | extracted `NewDmlScope` + `BindDmlTablesAndQueries` (the one owner) |
| `Core/Sql/Language/Semantics/SemanticBinder.Psql.cs` | five parallel DML arms → one arm delegating to it |
| `Core/Sql/SqlFormatter.cs` | `EmitQueryTail` + `StartOfCommentRunBefore`; `WithSemicolon` deleted; 4 call sites |
| `tests/…/SqlFormatterCommentPlacementTests.cs` | **new** — 8 cases |
| `tests/…/SemanticModelTests.cs` | +7 cases (binding + scope isolation + variables still resolving) |
| `tests/…/SemanticHighlightConsistencyTests.cs` | +8 crossed cases (kind × routine body) |
| `tests/…/SqlTestCorpus.cs` | +8 comment-bearing shapes |

---

# 6 — Follow-up the same day: the Parameter Helper card outlived its tab

**Report:** *"when I double-click a function this card appears, but it stays when I move to another tab; there
was a similar problem with QuickInfo and that fix was applied, but here it does not work."*

⚠ **The screenshot came with a second observation** — the card named
`XXX_FN_CZAS_BEZ_PRZEZBROJENIA` while the click was on `xxx_fn_czas_to_decimal`. I began treating that as a
second defect and measured it: clicking a function NAME resolves the **enclosing** call (its argument index
highlighted), and at one nesting level resolves **nothing** — because `SignatureHelpEngine`'s rule is *"the
innermost ENCLOSING paren wins"* and a name sits outside its own parens. ⛔ **The user ratified the existing
behaviour and told me to leave it**, so the engine change was reverted in full and `SignatureHelpEngine.cs` is
untouched. Recorded here only so nobody re-derives it as a bug.

## The mechanism, and a comment that read like a fixed bug

`ParameterHelper.Hide()` already carried this:

> *"The panel that HOLDS it, not the one the editor would resolve to now: after a tab switch the editor is
> detached, GetOverlayLayer answers null, Remove does nothing and the card is stranded in the window's
> overlay — where it survives every later tab change."*

That reads as a diagnosis with a fix attached. **Measured, every clause of it is false here:**

| premise in the comment | measured |
|---|---|
| the editor is detached on a tab switch | **no** — `DetachedFromVisualTree` fires **0×** |
| `GetOverlayLayer` answers null | **no** — still resolves |
| the card is *stranded* (nobody can remove it) | **no** — it is reachable; nothing ever asks |

MainWindow hosts workspace tabs as **co-existing views gated on `IsVisible`** (not a `TabControl` swapping
content), and **all tabs share ONE window overlay layer** — so a card parked while tab A was active is
physically over tab B afterwards. `Hide()` was never the broken half. **The card's lifetime is CARET-driven
(#210), and a tab switch moves no caret, so no rule ever fired.**

⭐ And the sibling card only *looked* immune: the hover card is dismissed by `PointerExited`, which fires
because you move the mouse away to click the tab strip. So *"the same fix is already there"* was true about the
removal and said nothing about the trigger — which is exactly why the user's *"there it was fixed, here it does
not work"* was right in a way the code comment actively hid.

## Three candidate signals, measured

| signal | result |
|---|---|
| `Visual.IsEffectivelyVisibleProperty` (subscribe to the invariant) | **does not exist** in Avalonia 12.1.1 |
| `DetachedFromVisualTree` | **0×** |
| `EffectiveViewportChanged` | **unstable** — 0× while probing, 1× in the first test (first delivery is async) |
| `LayoutUpdated` | **fires** |
| `LostFocus` | **fires** — raised *by* the visibility change (hiding an ancestor takes focus off the element) |

⚠ The unstable one is neither used nor claimed. Tuning it until it passed would have converted an unstable
measurement into an assertion; the code comment that first stated *"EffectiveViewportChanged does not fire"*
was corrected too.

## As built

**Two independent triggers, ONE decision, and the decision is the invariant:**

```csharp
private void DismissIfEditorLeftTheScreen()
{
    if (_card is not null && !_editor.IsEffectivelyVisible) Hide();
}
```

- Triggers: `_editor.LayoutUpdated` and `_editor.TextArea.LostFocus`, **subscribed only while a card is open**
  so `LayoutUpdated` — a firehose — costs nothing at rest.
- Neither trigger is trusted to *mean* "hidden": both ask. So a trigger going quiet in a future framework
  version degrades instead of breaking, and focus merely moving to another control **on the same visible tab**
  leaves the card alone — the change stays strictly about the reported defect.
- The same invariant guards the way **IN**: `ShowCard` refuses on an off-screen editor, because `ShowAt` has an
  async metadata-warm path that can resume after the switch, into the same shared overlay.

⛔ `LostFocus` was deliberately *not* wired as an unconditional hide (the tempting one-liner): that would be a
proxy for the invariant and would close the card on any focus change, an unrequested behaviour change.

## Verification

- Build 0/0; suite **8656** (8387 + 214 + 55) green in the three partitions; smoke clean; `Lab/` untouched.
- `ParameterHelperScreenWatchTests` pins the **premises**, not a policy: one shared overlay · no detach · the
  invariant flips · both triggers fire · and `IsEffectivelyVisibleProperty` still absent (that last one is the
  instruction to *replace* both triggers with one subscription the day it appears).
- Two plants, both caught: removing a trigger, and swapping the invariant for a proxy.
- ⛔ **The end-to-end behaviour is the user's QA** — `ParameterHelper` needs a real `TextEditor`, whose static
  keymap init throws in a headless session (#226, measured again here), so the stand-in is a plain control and
  every fact under test is Avalonia-level rather than editor-level.

New gotcha **#361**.
