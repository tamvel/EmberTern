# 31 — THE CONSTANT RULE, a card that printed its own type name, and a procedure that had no columns

**Date:** 2026-08-12 · **Scope:** `EmberTern.Core` (`SqlFormatter`, `SemanticBinder`,
`CompletionEngine`, `DiagnosticsEngine`) + `EmberTern.App` (`ProcedureDetailTabView` /
`FunctionDetailTabView`, `AppMetadataSnapshot`) · **Build** 0/0 ·
**Suite** 8426 + 318 + 54 + 1 = **8799** green (was 8768; +31 new) · `Lab/` untouched.

Four reports across one session. Three were defects with a single root cause each; the fourth turned
out not to be a defect at all but a **missing capability in a third-party control**, and is recorded as
such rather than half-fixed. ⭐ **Every one of the three defects was wider than its report** — the same
pattern as #29, and the reason each fix started with a measurement rather than an edit.

---

## §1 The formatter, again — and the report was again narrower than the bug

> *"Autoformat does not work for this procedure; if I remove the header comment it works. You already
> fixed me a similar case, but clearly there are still unhandled scenarios."*

The user is right about the shape and right about the precedent. [#29 §1](29-formatter-and-psql-dml-binding-fix.md)
closed **THE TAIL RULE**: a comment sitting between the last clause and whatever *closes* a query
precedes a token **no clause owns**, so nobody rendered it. This one is its exact mirror.

### The measurement

The routine's body opens `begin` + a 20-line block comment + `select … into …`, and the projection
carries scalar subqueries. Formatting returned the input **byte-for-byte**. Reaching past the §0 net
(reflection onto the private `FormatStatement`) showed why:

```
  select
         select m.id_meldunek, m.ilosc,
```

`select` **twice** — an *added* lexeme, not a lost one (in = 165 lexemes, out = 166).

`EmitProjection` does:

```csharp
var f = Flatten(sc.Tokens);
int h = 1;                                   // ← assumes f[0] is the SELECT keyword
var header = new StringBuilder(Kw("select", style));
var items = SplitTopLevelCommas(f, h, f.Count);
```

`Flatten` materialises the clause's comments as `FToken`s **ahead of** the keyword. So with a leading
comment present, `f[0]` is the comment, `f[1]` is the real `select` — which fell into the **column
list** — while the header emitted its own constant `"select"`. The comment, meanwhile, was skipped by
`SplitTopLevelCommas(f, 1, …)` and rendered by nobody.

### The rule, and why it is a rule and not a patch

The generalisation is not "leading comments" — it is **constants**:

> An AST-driven clause emitter that rebuilds its keyword from a **constant** never renders the tokens
> that constant replaces, so every comment carried as those tokens' leading trivia is rendered by
> **nobody**.

That predicts more than one site, so it was measured before it was fixed. A 13-shape corpus over the
four emitters that build from constants — `EmitProjection` (`Kw("select")`), `EmitFromClause`
(`Kw("from")`), `FormatWithClause` (`Kw("with")` / the CTE name / `"as ("` / `")"` / `","`) and
`EmitSetOperation` (the operator) — came back **9 broken of 13**. The report named one.

### ⭐ The finding that cost a second iteration: the net forbids REORDERING, not just loss

The first pass recovered every comment and **five shapes were still broken**, with the lexeme *counts
equal*. `LexemesEqual` compares the **sequence**. Two examples:

| Source | First-pass output | Verdict |
|---|---|---|
| `with /* c */ q as (…)` | `/* c */` above `with` | comment jumped **ahead** of `with` |
| `with q as (…) /* c */, r as (…)` | `/* c */` above the body's `)` | comment jumped **ahead** of `)` |

So a recovered comment on the wrong **side** of a token is exactly as fatal as a dropped one: the net
fires, the statement reverts, the formatter still appears to do nothing. That is what forced the
second primitive:

- `CommentsOn(token)` / `CommentsIn(tokens, lo, hi)` — the comments of a replaced token run;
- **`SplitCommentsAt(tokens, lo, hi)`** — what stood **before** the run's first token and what stood
  **after** it, because a run like `")" ","` or `"union" "all"` has *two* constant positions;
- `TakeLeadingComments(f, ref i)` — the same job for a caller already on the flattened stream, which
  also yields the index of the real keyword. `TryFormatExecuteBlockHeader` had this peel inline and
  now shares it, so there is one implementation, not two.

Consequence in layout: where a constant leaves no line for a comment to sit above, the **construct
takes its own line** rather than the comment jumping the token — `with` + a comment before the CTE
name now breaks the name onto its own line. ⛔ Hoisting to the top of the statement was considered and
rejected: a comment stays with the construct it annotated, and hoisting is precisely what the net
caught.

### Result

21 of 21 shapes clean, the user's routine formats, output idempotent, the header comment still one
block still above its `select`. `SqlFormatterCommentConstantRuleTests` pins it — **17 of its 18 cases
are red with the fix reverted** (the 18th, a comment *inside* a CTE column list, always worked because
that list renders from tokens). The 1774 pre-existing formatter tests stayed green throughout.

New gotcha **#369**.

---

## §2 The Execution Summary card that printed its own type name

> *(screenshot)* **Podsumowanie wykonania** → `EmberTern.App.ViewModels.ExecActivityLineViewModel`

The user guessed localization. It was not: the string is a `ToString()`.

The localization stage's **C6** replaced the bound items with an App row view model
(`ExecActivityLineViewModel`) — correctly, and for a good reason recorded in that type's own docstring:
the old template drew `Count` and `Verb` as two adjacent bindings, which pins **English word order into
the layout**. But the outer `DataTemplate` kept declaring Core's `TableActivityLine`:

```xml
<DataTemplate x:DataType="core:TableActivityLine">
```

On a `DataTemplate`, `x:DataType` is **also the matching type**. Both types expose `Table` and
`Changes`, so the markup looked right and no binding error was raised — the template simply stopped
matching, the `ItemsControl` fell back to the default presenter, and the presenter called
`ToString()`. One line in each of the two views.

### ⚠ Why it shipped, which is the more useful half

Both views carried this comment:

> *"`ExecActivityCardTests` reads the realized text back."*

**No such class existed.** A guard cited by name in a comment is an assertion about the repository, and
nothing checks it — so it reads as coverage while providing none, and it is *worse* than no comment,
because it answers the question "is this covered?" wrongly. The class now exists, and it asserts the
**realized output** (`template.Match(row)`, plus the text the rendered tree actually carries) rather
than what the XAML spells: a type mismatch is only one of the ways a card breaks. It is red on the old
markup, in both views.

⚠ A trap inside the guard itself, worth keeping: the change sentence is built from three `<Run>`
inlines, and **a `TextBlock` carrying `Inlines` reports a null `Text`** — reading only `Text` would
have made the guard blind to the exact line it exists to check.

New gotcha **#370**.

---

## §3 The Find/Replace panel is not localizable — recorded, not half-fixed

> *"A localization gap: the search inside a procedure's metadata is 100 % English despite Polish being
> on."*

True, and it is **not** a migration leftover. EmberTern deliberately uses AvaloniaEdit's own
`SearchPanel` rather than a second find/replace engine (a decision from `history/12`), and AvaloniaEdit
**12.0.0 exposes no localization seam**. Measured, because every plausible seam had to be eliminated
before proposing work:

| Candidate | Measured result |
|---|---|
| A `Localization` class with virtual strings (older AvaloniaEdit had one) | **Absent.** No such type anywhere in the assembly. |
| The public `SearchPanel` API | 6 properties (search/replace pattern, the three option flags, replace mode), 10 methods, 1 event. **No string, no message hook.** |
| A `pl` satellite assembly for `AvaloniaEdit.SR.resources` | **Impossible.** The strings live in an internal `SR` over that resource (a `zh-Hans` satellite ships, proving the mechanism), but AvaloniaEdit is **strong-named** (`PublicKeyToken=c8d484a7012f9a8b`) and a satellite must carry the main assembly's key. |
| A style setter over the template | **Partly viable.** On the realized tree the two placeholders and *all five* tooltips read priority `Template` — the lowest — so a style beats them. But the placeholders are named parts (`PART_searchTextBox` / `PART_replaceTextBox`) while the option toggles are **unnamed**, reachable only positionally. |
| The status line — the actually reported string | `PART_MessageContent`, written from the panel's **own code**. A local value; no style can beat it. |

So the reported string is the one a style cannot reach, and the reachable ones are reachable either
safely (2) or positionally (5). A style-only pass would deliver a **half-localized** panel whose
riskiest half is the part that could silently *mislabel* a toggle after an AvaloniaEdit upgrade — worse
than English.

⛔ Nothing was implemented here. Closing it was a real decision with options of very different cost, so
it was put to the user with the measurement attached. **Ratified the same day: our own Find/Replace UI
over AvaloniaEdit's search ENGINE** — which does not reverse `history/12`'s *"no second find/replace
engine"* rule, because that rule is about the **engine** (which stays theirs) and never claimed the
**presentation**. Rejected: a `ControlTheme` over their template (needs `product-polish.md` §16.4's gate
*and* takes on maintenance of a third-party template) · a style-only partial pass (its riskiest half is
the one that could silently **mislabel** a toggle) · accepting the gap. The stage is scoped in
[`design/find-replace-panel.md`](../design/find-replace-panel.md), which carries the perishable
measurements and **three unknowns that must be measured before any wiring is written** — chief among
them whether `SearchPanel` needs to be instantiated at all, since `SearchStrategyFactory` is public.

---

## §4 A selectable procedure that had no columns

> *(screenshot)* `ET0002 Nieznana kolumna „czas_1szt"` — *"diagnostics says unknown column and it exists,
> and the whole procedure compiles"*

It does exist. Firebird lets a procedure stand where a table stands, and the routine's `RETURNS` list is
then the alias's column set:

```sql
select y.id_meldunek, y.czas_1szt, …
from xxx_nk_cechy_meldunku_wylicz(:p_id_meldunek) y
into …
```

`SemanticBinder.ResolveColumn` asked `ISqlMetadataProvider.GetColumns(name)`, which for a **procedure**
is legitimately empty — so **every** `y.column` came back unresolved, and the qualifier *did* resolve
(to the procedure), which is exactly the state `DiagnosticsEngine` reads as "table known, column
absent". Measured: three columns, three ET0002s. The user's screenshot showed one tooltip; all of them
were underlined.

### ⭐⭐ The measurement that decided where the fix goes

The probe also asked what completion offered after `y.`:

```
completions after 'y.' = 0
```

**Zero.** So the report's visible half (a false squiggle) and a silent missing feature were *the same
bug seen from two sides* — and Quick Info, navigation and find-references were equally blind. A patch in
the diagnostics engine would have made the squiggle test pass and left three features broken. The defect
is in **resolution**, so the fix went there: `FromSourceColumns`, which answers *"which columns does this
FROM source contribute, and do we know them yet"* for the four call sites that had each been asking
independently (`ResolveColumn`, completion's dot path, completion's implicit-single-table path, the
diagnostics engine's readiness check).

### The two traps inside the fix

**(a) The tempting signal was the wrong one.** The AST already models `proc(args)` in FROM as a
`RoutineTableReference`, so keying on the node looked obvious — and would have been wrong. Firebird
admits `FROM MY_NOARG_PROC` with no parentheses for a no-argument selectable procedure, which parses as
a plain `TableReference` and is indistinguishable from a table *in the text*; that node's own docstring
says so. **The catalog knows; the text does not.** So the decision keys on the **resolved target
symbol** — which also avoids re-deriving a kind the binder has already committed to (in Firebird a table
and a procedure may share a name).

**(b) Readiness is a second question, and forgetting it would have re-created S-2.** Routine parameters
are warmed lazily *exactly* like columns, so an empty parameter list is the same undecidable signal that
`KnowsColumns` exists for. Without a `KnowsRoutineParameters` companion, every `alias.column` over a
selectable procedure would have been squiggled until the warm pass finished — *"everything is underlined
for a moment, then the errors disappear"* reproduced verbatim, one object kind further along. ⭐ The App
could already answer honestly: `_routineParameters` is a dictionary, so a missing key and a
present-but-empty entry were always distinguishable — the information was being discarded one layer too
early, which is the same sentence `AppMetadataSnapshot` already carries about columns.

### What keeps it a fix and not a mute button

`SelectableProcedureColumnsTests` (9 cases, **6 red before the fix**) pins the positives *and* four
negatives: a genuine typo still fires · an **INPUT** parameter is **not** a column of the result
(offering `P_ID_MELDUNEK` as a column of `y` would be a wrong answer, not merely a noisy one) · an
unwarmed parameter list is silent · and the plain-table path is untouched. Plus the paren-less shape from
trap (a).

New gotcha **#371**.

---

## Verification

- Build **0/0** (`TreatWarningsAsErrors=true`), whole solution.
- Suite **8799** green across the three partitions (8426 + 318 + 54 + 1). ⚠ Checked against the
  **total**, not against "0 failures": 8768 + 31 new = 8799 exactly, so nothing silently failed to
  start (the trap `CLAUDE.md` warns about, where a broken headless state reports 0 failures while 128
  tests never ran). The headless class list was **derived**, never transcribed.
- All three new guards were verified **red on the reverted code** — 17/18 for the formatter, 2/4 for the
  cards, 6/9 for the selectable-procedure columns — because a guard nobody has seen fail is a guard nobody
  has tested.
- ⛔ **§1, §2 and §4 await the user's confirmation in the running app.** Per the standing directive, a green
  build plus green tests is not "fixed": the formatter is proven on the reported routine's text, the card
  on the realized visual tree, and the column resolution on the model + completion list — but none of the
  three has been seen on screen by a human.
