# 23 — Acceptance fix round before M3.2d (2026-08-03)

Fourteen defects the user collected during ordinary work with EmberTern. Not Product Polish, not features —
real bugs, closed in one pass before M3.2d started. The instruction was explicit: **group by common cause, not
by report order, and close the list in as few logical iterations as possible.**

Analysis found **6 causes** behind the 14 reports. Three of them are shapes this codebase already knows by
name, which is the most useful thing that came out of the round.

Build 0/0 throughout. Suite **7195** green in the three documented partitions (**7085 + 56 + 54**, up from
7138). Two instances launched side by side and closed; `settings.dat` intact, no orphaned temp files.

⚠ **Two of the six needed a SECOND round after the user's acceptance pass** — the parameter types (I2) and the
picker filter fields (I5's fourth item). Both first-round fixes were correct and addressed real defects; neither
changed what the user saw. Each write-up below keeps that arc, because the failure was in the **scope of the
question asked**, not in the analysis, and that is the reusable part.

---

## What the grouping actually bought

| Iteration | Reports | One cause |
|---|---|---|
| I1 | 7 | `settings.dat`'s atomic write was atomic only **within one process** |
| I2 | 4 | the type lookup knew only ONE of Firebird's two procedure-call shapes |
| I3 | 3, 2.4 | Core.Sql.Language: the grammar pins a meaning, the consumer does not ask |
| I4 | 2.1, 2.2 | the result grid treated as a grid the user arranges |
| I5 | 1.1, 1.2, 1.3, 5 | M2b/M2c residue: a value used where its premise does not hold |
| I6 | 6, 2.3 | a popup's lifetime not tied to its owner · one gesture in the catalog |

⭐ Four of the fourteen were **not** where the report pointed, and in each case reading the code (not
reasoning about it) moved the diagnosis:

- **"Parameter types stopped working"** → the statement was a selectable procedure called from `FROM`, a shape
  the lookup did not recognise at all. The count gate I fixed first was a real defect, just not this one.

- **"Columns are out of SELECT order"** → `PopulateResultGrid` builds them in exactly `result.Columns` order and
  always did. The reorder came from `GridLayoutBehavior` replaying a saved order.
- **"The disabled hammer has the wrong colour in Light"** → `AccentColor` and `OnAccentColor` are **identical in
  both themes**, so colour could not be the cause. `Opacity` was, because it lets the toolbar background through.
- **"A tooltip hangs forever"** → the first fix (close `ToolTip.IsOpen` on detach) was measured **inert**:
  Avalonia already does that. The real hang is an `OverlayLayer` card nobody removes.

---

## I1 — two instances, one settings file

**The mechanism is arithmetic, not chance.** `AtomicWrite` used the fixed temp path `settings.dat.tmp`,
identical in every process. `File.WriteAllText` truncates before it writes, so that shared file is momentarily
zero-length; a second instance reaching its `File.Replace` inside that window publishes an **empty**
`settings.dat`. An empty file loads as `Missing` — correctly, that is what a killed-mid-write leaves behind — so
every facade answers it with `Load() ?? new()`, the save guard lets a blank file through, and the next write
makes defaults permanent while `File.Replace` rolls the single `.bak` generation away.

Fix: per-write temp name (`<pid>.<guid>`), a named `Local\` mutex spanning the whole read-judge-write, and I/O
failure reported through `LastSaveDiagnostic` instead of an exception escaping into `MainWindow`'s Closing
handler.

⭐⭐ **The lesson that outlives the bug: a race is the wrong instrument for pinning a race.** The concurrent-save
stress test (4 threads × 15 writes) **passes against the broken code** — the window is microseconds wide. What
fails pre-fix is the deterministic statement of the property: plant a file at the shared temp path and require
the save to leave it untouched. Both tests are kept; only one is the guard, and the test says which.

⚠ **Stated boundary, not fixed:** last-writer-wins at the *section* level survives. Instance A holds live
in-memory copies (`PreferencesService`, `_folderState` — settings-center.md §16.1) and `Save` persists the whole
aggregate. That is coherent and visible for two deliberately-run instances. Losing everything to defaults was not.

Ratified with the user: **do not block a second instance.** Two instances are a real developer scenario (two
databases side by side, a smoke test beside a working app).

---

## I2 — the type lookup did not know how a selectable procedure is called

⚠⚠ **This iteration took two rounds, and the second one is the lesson.** The first fix was correct and changed
nothing the user could see; they re-reported the defect with a screenshot, and the screenshot contained the
answer:

```sql
select nazwisko_imie, … from xxx_sel_rap_czasupracy(:p_dataod, :p_datado, :p_id_jedkadr)
```

⭐⭐ **That is not `EXECUTE PROCEDURE`.** In Firebird a **selectable** procedure is invoked from a `FROM` clause,
so the statement parses to a `SelectStatement` — and the lookup recognised `ExecuteProcedureStatement` only,
bailing out before the catalog was ever consulted. Selectable procedures are exactly the reports an ERP
developer parameterises by hand, i.e. most real uses of this dialog.

**Round one** had found and fixed a genuine, different defect in the same method: it required
`catalog.Count == names.Count` and fell back to `Unknown` for *all* placeholders otherwise, which breaks on a
call omitting parameters that have DEFAULTS, a repeated placeholder, or `RETURNING_VALUES :r` (measured: 3
names, 2 inputs). ⚠ And it is **not** a regression — the gate has been there since the feature's own etap
(`54b630c`), and the only later change to that path (`404b7a6`) made the count *more* correct.

⭐ **The methodological failure was the scope of the question, not the analysis.** I asked *"why does the count
gate fail?"*, measured three ways it does, fixed them, and never asked *"is this statement even the shape the
code recognises?"* — the one question the screenshot answers immediately. **A correct fix to a real defect can
leave the reported symptom untouched, and only the user's screen says which happened.**

Both are now answered by one question in one place: `TryExtractRoutineCall` knows the two shapes a routine call
takes, and `MapNamesToArgumentSlots` consumes *that* call rather than re-deriving it, so the catalog lookup and
the slot mapping cannot describe different invocations. Firebird binds both shapes **positionally**, so "slot
*i* is input parameter *i*" is the language's rule, not an inference. `TryExtractExecuteProcedureName` was
**deleted** rather than left beside it — keeping both would preserve a way to reproduce exactly this bug.

⚠ **And the FROM shape needed one measured correction of its own:** a `TableReference`'s own `Tokens` hold
**only the name** (`rap`), because the parser models a FROM entry's name and alias and an argument list is not a
node. The first implementation looked for `(` inside the entry, found nothing, and detected no calls at all —
looking entirely correct.

### Round three — the user stopped the patching, and was right

⭐⭐ **The third report is the most valuable thing in this file.** `SELECT … FROM proc(…)` now worked, and the
user immediately found `FOR SELECT … INTO` and `INSERT INTO … SELECT` broken, then said what the problem actually
was: *"To wygląda tak, jakby parser rozpoznawał kolejne tekstowe wzorce zamiast korzystać z informacji, które już
powinien mieć AST … AST powinno umieć odpowiedzieć na pytanie: «W tym miejscu wykonywane jest wywołanie procedury
z listą argumentów»."*

That is exactly what was wrong. Two rounds had added **statement-shape branches** to a consumer, and each round
left the next syntax silently dead. The list that would have followed is long: `FOR SELECT`, `INSERT … SELECT`,
CTE bodies, `MERGE … USING`, cursor declarations, and a call in any subquery of any of them.

**What was built instead:**

1. ⭐ **`IRoutineInvocation` on the AST** — `RoutineName` / `PackageName` / `Arguments`, implemented by
   `ExecuteProcedureStatement` and by a new `RoutineTableReference`.
2. ⭐ **The parser stopped dropping the argument list.** `ParsePrimaryFromItem` read the name and went straight to
   the alias, so `rap(:a, :b) r` produced a `TableReference` carrying the single token `rap` — neither the
   arguments **nor the alias**. That is why consumers were re-scanning text: the structure was not there to read.
   ⚠ `RoutineTableReference` is a **subclass** of `TableReference`, so every existing consumer (the binder
   resolving a selectable procedure in FROM, highlighting, navigation) keeps working untouched.
3. ⭐ **`MERGE … USING <name>(args)` is modelled too** — that branch previously noted "bare table source" and moved
   on, making it the last place a routine could be invoked invisibly. It now parses through the same
   `ParsePrimaryFromItem`, so it yields the same node kinds and introduces no second notion of "a source".
4. ⭐⭐ **The consumer lost all knowledge of statements.** `SqlParameterScanner` asks
   `DescendantNodesAndSelf().OfType<IRoutineInvocation>()`; ~130 lines of token walking, join flattening and
   clause-shape logic were **deleted**, and `TryExtractExecuteProcedureName` with them.
5. ⭐ **Typing is resolved per PLACEHOLDER, not per statement.** One statement can invoke several routines (two
   selectable procedures joined), so a binding carries its own routine name plus the slot; a name standing in two
   different routines is ambiguous and claims nothing.

**The measure of whether this was architecture or another patch:** the theory that pins it has rows for
`FOR SELECT … INTO`, `INSERT … SELECT`, `UPDATE OR INSERT`, a CTE body, `MERGE … USING`, a cursor declaration, a
derived table and an `EXISTS` subquery — and **not one of them has a line of code behind it.** They pass because
the parser hangs each embedded query off the statement that owns it and the walk reaches them all.

⛔ The standing rule now written at the walk: **do not add a statement-kind branch here.** If a call is not found,
the parser is not modelling it, and that is where the fix belongs (Contract #1).

### Round four — the inventory, and a type has TWO provable origins

The user rejected round three too, and the objection was sharper than "another syntax is missing": *"zabrakło
jednego kroku weryfikacji: czy wszystkie miejsca korzystające z tej architektury nadal zachowują się poprawnie …
które z nich mają działać dla wywołania procedury, które dla dowolnego SQL z parametrami, a które nie powinny
uruchamiać się w ogóle."*

**The inventory they asked for turned out to be two questions with one consumer each:**

| Question | Scope | Consumer | Touched in this round? |
|---|---|---|---|
| "Does this SQL carry named placeholders?" (`RewriteToDriverMarkers`) | **any SQL** | the parameter-dialog gate in `ExecuteQueryAsync` | **no** — byte-identical to before the session |
| "Is this placeholder provably a routine argument?" (`IRoutineInvocation`) | **typing only** | `BuildSmartParamSpecsAsync` | yes |

⭐ So `IRoutineInvocation` **could not** have widened the dialog's reach — it is consulted only for types. Verified
against `git show HEAD:` rather than argued: the gate is `names.Count > 0`, unchanged, and has been since the
Smart-SQL-Parameters etap (`54b630c`).

**What the screenshots actually showed was a MISLABELLED dialog.** Smart SQL Parameters reuses the Execute
Procedure editor to collect values for any parameterised statement, so a plain `INSERT … VALUES (:a, :b)` opened a
window titled *"Execute Procedure"*. The behaviour was correct; only the label lied — and a lying label is
indistinguishable from a malfunction, which is why the user read it as one. ⭐ Title and header are now neutral
(**"Execute"**, user's choice), and the reuse is documented at the string so nobody narrows it back.

**And the real remaining defect was that a type has two provable origins, not one.** The user's directive:
*"Jeżeli AST potrafi jednoznacznie ustalić, z jaką kolumną jest związany placeholder, to chcę, żeby typ był
rozpoznawany również dla DML … nie seria if-ów dla kolejnych instrukcji, tylko wykorzystanie modelu AST jako
jednego źródła wiedzy."* Delivered as a second AST fact beside the first:

- ⭐ **`IColumnValueTarget`** — table + **(column, value-span) pairs**. Implemented by `InsertStatement`,
  `UpdateOrInsertStatement` (paired positionally from `(cols)` against `VALUES (…)` — one producer, because the
  shape is identical, the same reason `SqlFormatter` lays both out through one `FormatInsertFamily`) and
  `UpdateStatement` (`SET col = expr`, paired by adjacency). ⚠ Modelling **pairs** rather than two parallel lists
  is what lets one interface serve shapes that pair differently, and keeps that difference away from the consumer.
- ⭐ **`ParameterTypeSource`** — the single answer a consumer gets: `RoutineParameter(owner, slot)` or
  `TableColumn(owner, column)`. `ResolveTypeSources` walks for both facts; the VM switches on the **kind of
  source** to pick a catalog, never on a kind of statement. A third origin would arrive as a new AST fact plus one
  arm, not as a branch anywhere.

⚠ **What it refuses, each with a reason** (rule #11): an insert with no column list (matching values to columns
would need the catalog's order — a lookup, not a fact about the text) · a column/value length mismatch (a statement
Firebird rejects anyway; pairing the prefix would type values whose column is undecided) · `WHERE col = :p` (a
predicate is a token fragment at structural depth) · a value that is not the whole placeholder (`:a + 1`).

**Two drag-and-drop corrections in the same round.** `FOR SELECT … INTO` was *still* missing from the SQL Editor,
because round three's "clever" answer — deriving the insertion context from whether the drop offset sits inside a
`BEGIN … END` — fails for the case that matters: a scaffold is what you reach for **to start** a body, so there is
no block yet. ⭐ The user settled it with a general argument — *the SQL Editor is where `EXECUTE BLOCK`,
`CREATE PROCEDURE` and `CREATE TRIGGER` get written* — so **every** built-in scaffold is now offered in every
editor, the offset resolver was **deleted**, and the rule is pinned once over the whole catalog
(`NoBuiltInTemplate_IsHiddenByTheInsertionContext`) instead of per template.

⭐⭐ **The methodological lesson of rounds three and four together: an architecture is not verified by the tests of
the thing it replaced.** Round three's model was right and its theory was broad, but nobody asked what *else*
consumed the old behaviour, or whether the surfaces built on it still read correctly to a user. The inventory the
user demanded took ten minutes and found that one of the four reported defects was a label, one was a second
missing fact, and two were mine.

---

## I3 — the grammar pins a meaning; two consumers did not ask

**`EXTRACT(YEAR FROM …)` reported ET0003** on `YEAR`. Same shape as gotcha #302 (`GEN_ID`). The date/time parts
are deliberately **not** keywords — they are not reserved in Firebird, so a column may be called `MONTH` — hence
they lex as identifiers and the PSQL walker read one as a local. The fix is **positional**, a sibling of
`IsGeneratorNamePosition`, composed with it at the one caller that has the shared job. ⚠ Composed at the caller,
not merged: a generator name must be *resolved* by the catalog scan, a date part resolves to nothing.
19 tests fail pre-fix, 0 after.

**The formatter did not break the `INTO` list.** It was the one comma list P8's convergence missed — every other
(SELECT, VALUES, INSERT, MATCHING, IN, call arguments) rides the shared adaptive builder. One `FormatIntoClause`
now serves all three call sites. ⭐ All 1208 existing formatter assertions pass **with no expectation edited**,
because a short list still joins with `", "`.

---

## I4 — whose order is it?

`GridId="QueryResults"` is one profile for every query, and `GridLayoutBehavior` remembered order for any grid
with a profile. ⭐ The rule that fixed it is read from the grid: **order is remembered only where the user can set
it** (`CanUserReorderColumns`), applied to both restore and save. Five grids corrected at once; three had never
shown the symptom because a table's own column order does not change between loads.

**Resize started a sort** because the header handler fired on `PointerPressed` alone, and a resize drag begins
with a press inside the header. Sorting is now press **and** release: same header, pointer travel under a
threshold, and the press ignored when it lands on a `Thumb`. Two independent guards, deliberately — the gripper
is matched by type rather than template-part name, and if that ever stops holding the travel check still catches
the drag.

---

## I5 — three M2b/M2c residues, one shape

Detail in `product-polish.md` §21. The shape: **a value correct in one context, used where its premise does not
hold** — `Pad.Control`'s zero vertical (premise: `Size.Control` owns the height), `field-editor`'s reach (premise:
we construct the editor), `Opacity 0.5` (premise: fading is theme-neutral).

⭐ Two of the three first required **removing local values** so a style could reach the element: twelve `SvgIcon`
in `MainWindow` carried `Foreground` locally, and a local value beats a setter. Third time this mechanism has
cost a separate report (`MessageBanner`'s six chrome variants; `FieldGridColumns`' 12 px editor; now the disabled
icon). Measured 12 → 24 px, and the guard fails pre-fix with exactly those numbers.

⚠⚠ **The fourth item (the picker filter fields) also took two rounds, and for the opposite reason to I2's.**
Round one strengthened the **border** only; the user re-reported it on **both** themes. The measurement then said
two things at once: the selector *was* applying (so "it does not work" was misleading), and it was still not
enough — in Light the field's fill is `#FCFCFD` on a `#FFFFFF` raised surface, a **three-unit** difference, so the
shape of the field rested on one pixel of line, and that line measured **2.55:1** against the surface, under the
3:1 floor §10 sets for a non-text boundary.

⭐ **The fix is a recessed FILL plus a border that clears the threshold** — the fill is what gives a field its
shape; the border only closes it. Measured after: Light 1.18 fill / **3.07** border, Dark 1.30 / **3.31**. Pinned
by `AFilterFieldOnARaisedSurface_StandsOutAtRest` in both themes, asserting the **threshold** rather than a
colour, so it survives a change of shade and fails exactly when the field starts blending again. ⚠ `:pointerover`
and `:focus` had to be re-declared: an app-level style beats the `ControlTheme`, so the resting border would
otherwise have covered the hover cue **and** the focus ring — fixing visibility by removing affordance.

⭐ **The general lesson, and it is the twin of I2's:** *"almost visible" is indistinguishable from "invisible" to
the person using it.* A change that moves a value in the right direction but stops short of a threshold reads to
the user as no change at all — which is why the threshold, not the direction, is what gets pinned.

⚠ **A measurement corrected the test, twice.** The editor-height guard first caught an unrealized `TextBox` from
a `ComboBox` template (height 0, control padding) and failed against a *correct* application; two guesses at what
it was were both wrong, and printing the ancestor chain settled it in one run.

---

## I6 — a stranded card, and Alt+F

**The tooltip that only a restart removed was not a tooltip.** Every `OverlayLayer` card closed itself with
`OverlayLayer.GetOverlayLayer(_editor)?.Children.Remove(card)` — which answers *"which overlay would this editor
use NOW"*. After a tab switch the editor is detached, the answer is `null`, `Remove` does nothing, and clearing
the field drops the last reference to a card still parented in the **window's** overlay, which outlives every
tab. ⭐ The rule was already written in the same file, on `HideBulb`, with its reason — and missing from the other
four sites. #302's shape one layer up: one piece of knowledge, partially copied.

⚠⚠ **The first fix was deleted rather than kept.** It closed `ToolTip.IsOpen` on the owner's detach; measured
afterwards it changed nothing, because Avalonia's tooltip service already does that. An inert guard reads to the
next author as a real safety net (§15.7), and a fix whose mechanism you cannot demonstrate is a false record of
what was wrong.

**`Alt+F` is back for Format SQL** (user decision — `Ctrl+K` needs two hands for an action used constantly), with
`Ctrl+K` retained as the alternate. One line in `CommandCatalog`; every tooltip, chip and the Keyboard Shortcuts
window recomposed themselves. Details and the narrowed guard: `keyboard-manager.md` §18.

⭐ **Two guards fired on the change, which is the system working.** `RatifiedGesture_IsTheDeclaredOne` declares
the ratified map, and `MigratedTooltips_CarryTheCatalogsGesture` — written because a tooltip once read
`"Format SQL · Alt+F"` after the command moved to `Ctrl+K` — **failed again, in the opposite direction, without
anyone touching a string.** That is exactly the property `CommandTip` was built to buy.

⭐ And the two local `OnEditorKeyDown` handlers (the one place the router cannot reach, because the target is a
specific `TextEditor` instance) now read the gesture from `CommandCatalog.For(CommandId.FormatSql)`. Had they kept
their literal `Key.K`, Alt+F would have worked everywhere **except** those two editors — gotcha #284's drift, in a
key handler instead of a string.

---

## Two drag-and-drop templates, sharing the model's knowledge

Requested with round three, and deliberately built on the same foundation so the code generator and the language
model are not two independent implementations.

- ⭐ **`INSERT INTO … SELECT` for tables** — genuinely new (`TableInsertFromSelectTemplate`). Both column lists come
  from ONE call to `Insertable`, which is the point: written by hand the two lists have to be kept in
  correspondence, and that is where the mistake happens. Source table defaults to the same table (the archive/twin
  shape), never a guessed one.
- ⚠⚠ **`FOR SELECT … INTO` for selectable procedures already existed** — and was **unreachable from the SQL
  Editor**. The insertion context was fixed when the drop target was attached: body editors were `PsqlBody`, the
  SQL Editor was always `PlainSql`, and the PSQL scaffolds are `PsqlOnly`. That is right about the body editors and
  wrong about the SQL Editor, because writing `CREATE OR ALTER PROCEDURE … BEGIN … END` there is ordinary work.
  ⭐ New `SnippetInsertionContextResolver` decides from the **drop offset** by asking the parser whether it lies
  inside a `BlockStatement` — so the scaffolds appear inside a body being written in the console and stay hidden at
  the top of an empty one. ⛔ Not by counting `BEGIN`/`END` keywords: that scan is wrong for `CASE … END`
  (gotchas #117/#128/#129) and is the token-level guessing the parser exists to replace.

⭐⭐ **The contract that makes them one feature, not two:** `EveryGeneratedInvocation_IsRecognisedByTheModel` feeds
each template that emits a call back through the very walk the Smart-Parameters dialog uses, and requires the same
routine, the same argument count, and each generated `:param` bound to the slot it was generated into. Without it a
template could emit a shape the model does not model — precisely the defect chased through three rounds — and
nothing would fail until a user dropped it in and pressed F5.

## New gotchas

**#303** positional vs lexical for a grammar-pinned name · **#304** a shared temp filename makes an "atomic"
write cross-process-unsafe, plus *a race is the wrong instrument for pinning a race* · **#305** remove a popup
from the panel that HOLDS it, and the inert-fix lesson · **#306** persist a layout decision only where the user
can make it.

## Left open, deliberately

- **`RETURNING_VALUES` placeholders are still rewritten to `@name` and bound as inputs.** Out of scope here (it
  changes what is executed, and nobody reported an execution failure), but it is a real latent defect in the same
  feature — worth its own look.
- The two-instance **section-level** last-writer-wins (I1's stated boundary).
- Local `DataGridRow Height` in eight views — ratified to stay.
