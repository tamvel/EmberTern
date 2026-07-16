# EmberTern — Language Completion & Typing Ergonomics (design)

**Status:** design **complete and agreed** (2026-07-16), pre-implementation. Replaces the Stage 8 **M2
Smart Snippets** direction, which is to be reverted (uncommitted) — see §11. No open design decisions
remain (§13 records the resolved ones); only an implementation-time detail on the `begin` opener trigger.

**Relationship to other docs:** consumes the language front-end (`docs/design/editor-architecture.md`)
and the Etap 6.9 AST (`docs/design/editor-ast-deepening.md`) for grammar-aware behaviour; the prefix-first
Completion work (`docs/history/17-completion-matching-philosophy.md`) is Tool C here.

---

## 1. Goal and principles

**Goal.** Let an experienced Firebird developer write SQL/PSQL in **the fewest keystrokes**, with an
interaction that is **immediate, predictable, and never surprising**. The editor removes repetitive
mechanics; it does not generate code, teach the language, or impose structure.

**Rule 0 — never generate code the user deletes.** Every expansion is the smallest fragment that still
saves keystrokes. Structure comes from indentation, delimiter pairing and formatting — not from generated
blocks. If a behaviour's first-reaction is *"now I delete half of this,"* it is wrong. This is the
acceptance test for every rule below.

**Obviousness principle — no EmberTern-specific behaviour to memorise.** Whenever a key does something
beyond inserting its own character, the editor must **already be showing exactly what will happen** before
the user commits. In practice: **Tab** is the only key that performs a special action, and it only acts
when a visible hint is on screen describing the result.

**Enter stays a normal editing key, everywhere.** Enter always inserts a newline (with ordinary
auto-indent — see §3.2). It never jumps out of a construct, never re-positions the caret by grammar, never
carries hidden meaning. Leaving a construct (e.g. the condition of an `if`) is done by normal navigation
for now; a *shown, Tab-based* ergonomic for it may come later (§10), never an Enter behaviour.

**Language Completion never depends on timing.** Tool A is a pure, synchronous function of (text, caret):
the hint appears/updates/vanishes *immediately* on the keystroke, deterministically, with no debounce, no
idle delay, and no dependence on whether the background `SemanticModel` has caught up (see §5/§8). This is
what makes it feel instantaneous and predictable. (IntelliSense — Tool C — remains idle-debounced; that is
the deliberate difference between predicting a *construct you're already typing* and offering a *list of
names*.)

**The editor removes repetitive mechanics — it does not author code.** Restating the intent: every tool
here exists to save keystrokes on things the developer would type anyway, never to produce structure they
must review or delete.

**Three tools, chosen by grammar, never by the user.** The developer never picks a mode; the editor knows
from the caret's grammatical position which tool is appropriate, and at most one is active at a time.

| Tool | Key | Helps with | Active where (grammar) |
|---|---|---|---|
| **C — IntelliSense** (prefix-first) | auto-list / Ctrl+Space | *names* (tables, views, procedures, functions, columns, variables, parameters, aliases) | reference positions |
| **A — Language Completion** | **Tab** (on a visible hint) | *constructs* the dev types daily (if / while / select / where / …) | positions where that construct may legally begin |
| **B — Typing ergonomics** | typing / Enter | mechanics: indentation, delimiter pairing | always on |

Tools A and C are **disjoint by grammatical position** — where you name things, constructs don't arm;
where a construct may begin, the identifier list doesn't auto-pop. Tool B is orthogonal to both.

---

## 2. Tool A — Language Completion (construct completion)

### 2.1 The trigger is a natural prefix of a real construct — there is no abbreviation to learn

The catalog is keyed on each construct's **real Firebird spelling** (`declare variable`, `group by`,
`execute procedure`, `if … then`). The **trigger is however far the developer naturally typed into that
spelling.** Tab finishes it:

```
decl▌  →  declare variable ▌
exec…  (see 2.3)
sele▌  →  select ▌
wher▌  →  where ▌
gro▌   →  group by ▌
if▌    →  if (▌) then
```

Tab always yields the same canonical form regardless of how much prefix was typed (`gro`+Tab and
`group`+Tab both → `group by `). There is **no second language of shortcuts** — Tab simply completes what
the developer already started.

### 2.2 Tab finishes; the hint shows the exact result

When the word/phrase before the caret is a prefix of exactly one armed construct (§2.3, §5), a subtle
OverlayLayer hint shows the **exact** text Tab will produce:

```
gro▌        ⇥ group by
```

Tab → replaces the typed prefix with the canonical expansion, caret at the single point the developer
types next (▌ in the catalog). Any other key → nothing special (the hint is the only "arming"; there is no
invisible state). This directly serves the obviousness principle.

### 2.3 Ambiguity → silent until unique *within the curated catalog*

A construct arms only when the typed text is a prefix of **exactly one** construct **in the catalog**.
Because the catalog is curated to the daily constructs (§2.5), common prefixes are unambiguous:

- `decl` → only `declare variable` is in the catalog (a `declare cursor` etc. is not) → **arms**.
- `exec` → both `execute procedure` and `execute block` are in the catalog → **ambiguous → silent**; the
  hint appears once the developer has typed enough to be unique (`execute p…` / `execute b…`).
- `for` → `for select` is in the catalog; if `for execute statement` is also added, `for` stays silent
  until `for s…` / `for e…`.

So "silent until unique" (which you liked) and "`decl` just works" (which you wanted) are both satisfied —
uniqueness is measured against the small curated set, not all of Firebird. The hint never shows a guess.

### 2.4 Casing

The expansion follows the document's existing case style (lowercase by default), via the existing
`SqlCaseStyleDetector` / `CaseMatcher` — identical to how identifier completion already inserts.

### 2.5 Construct catalog (starter set — pure data, tunable)

The catalog is declarative data (`{ spelling, expansion, caretOffset, armWhere }`); adding/removing a
construct is a one-line change, and it is the seam a future "user-defined constructs" feature would expose.
Starter set:

| Spelling | Expansion (▌ = caret) | Arms where |
|---|---|---|
| `if` | `if (▌) then` | PSQL statement position |
| `while` | `while (▌) do` | PSQL statement position |
| `for select` | `for select ▌` | PSQL statement position |
| `declare variable` | `declare variable ▌` | PSQL declare section / block top |
| `execute procedure` | `execute procedure ▌` | statement position |
| `execute block` | `execute block ▌` | statement position |
| `select` | `select ▌` | statement / subquery start |
| `insert into` | `insert into ▌` | statement position |
| `update` | `update ▌` | statement position |
| `delete from` | `delete from ▌` | statement position |
| `where` | `where ▌` | after a complete FROM / UPDATE / DELETE target |
| `group by` | `group by ▌` | query, after WHERE/FROM |
| `having` | `having ▌` | query, after GROUP BY |
| `order by` | `order by ▌` | query tail |
| `union` | `union ▌` | query tail |
| `when` | `when ▌ do` | block exception-handler position |

`begin … end` is **not** in this catalog — it is a structural delimiter pair handled by Typing Ergonomics
(§3.1), not a language construct. Datatypes and functions are also **not** here — they are *names*, handled
by IntelliSense (Tool C). Everything not in this small set is simply typed.

### 2.6 Caret landing and leaving a construct

The expansion places the caret at the primary edit point (inside `()`, after the clause keyword). Leaving
it (e.g. moving past `)` in `if (▌) then`) is **normal navigation** (End / →) for the first cut — a
stateless model with no post-expansion session. A *shown, Tab-based* "jump to the body" ergonomic is a
possible later addition (§10); it will never be an Enter behaviour.

---

## 3. Tool B — Typing ergonomics

### 3.1 `begin … end` is a structural delimiter pair (not Language Completion)

`begin`/`end` is a **delimiter pair**, conceptually identical to `()` / `''` / `[]` — an opener that always
needs its closer. That is a *different responsibility* from Language Completion (which finishes language
*constructs*), so it lives here, in Typing Ergonomics, alongside the other pairs (§3.3). Keeping the two
responsibilities separate is the cleaner architecture (your call, 2026-07-16).

Consequence: `begin` is **not** in the construct catalog, produces **no** Language-Completion hint, and the
old "Tab vs Enter for begin" question (former §13) is dissolved — it's now just "how does the pair behave,"
answered consistently with the other pairs. The pair, when opened, takes its natural PSQL form with the
caret inside:

```
begin
  ▌
end
```

**Recommended behaviour (treat it exactly like the other pairs):** when `begin` is completed **as a
keyword** (word boundary, and the grammar is at a statement position — never while typing an identifier
like a column that merely starts with those letters), the matching `end` is paired automatically, with the
caret on the indented body line. Deterministic on the keystroke — no timing, no Tab, no special Enter.
Backspace on the freshly-opened empty pair removes it (pair semantics). This is the analogue of `(` →
`()`, adapted to a multi-line keyword delimiter.

`if (…) then` / `while (…) do` never auto-insert `begin…end` (a `then`-body is often a single statement;
auto-inserting would violate Rule 0). If the developer wants a block there, they type `begin` and the pair
forms.

(The exact opener trigger — pair the moment `begin` is a complete keyword token vs. on the following
whitespace — is a small Typing-Ergonomics detail to settle during implementation; both are deterministic
and timing-free. It is no longer a Language-Completion decision.)

### 3.2 AST-aware auto-indent (part of "normal" Enter)

New lines inherit the correct structural indent (a level deeper after `begin` / `then` / `do` / `(`, back
out on `end` / `)`), computed from the token/AST model — better than IBExpert's naive indent. This is
ordinary modern-editor behaviour, not a special action: Enter still just makes a newline; only its leading
whitespace is smart. It never moves the caret by grammar.

### 3.3 Delimiter pairing

One pair family, one set of rules: `()`, `''`, `[]`, and the keyword pair `begin … end` (§3.1). Typing an
opener inserts its closer with the caret between; typing the closing char *types through* the auto-inserted
one; backspace on an empty pair removes both. Standard, expected, keeps the developer on the line. (The
`()` in `if (▌) then` comes from the catalog expansion, not from this — but the two are consistent.)

---

## 4. Tool C — IntelliSense (recap)

Identifier prediction only, prefix-first (`CompletionMatcher`, already built): StartsWith, exact floated to
top, no substring during typing, empty prefix → all (Ctrl+Space). Auto-pops only in **reference positions**.
Full spec + remaining wiring: `docs/history/17-completion-matching-philosophy.md`.

---

## 5. Grammar-driven arming

Arming (Tool A) and auto-pop (Tool C) are decided by **what the grammar allows at the caret**, using the
parser/AST — not by coarse "statement position" guesses:

```
select ▌ from …          → expression context → WHERE does NOT arm; identifier list may pop
select * from CUSTOMER
▌                        → post-FROM → WHERE / GROUP BY / HAVING / ORDER BY / UNION / JOIN arm
begin
  ▌                      → PSQL statement start → IF / WHILE / FOR / SELECT / … / DECLARE arm
```

Mechanism: a resolver computes the set of **construct-starts valid at the caret** (from the enclosing
query clause structure / PSQL body position / DECLARE section) and arms only catalog entries in that set.
Where the parse can't classify a fragment, it falls back to a coarse token-position rule (arm the common
statement/PSQL starters) — never worse than a naive editor.

**This computation is synchronous and timing-free** (per §1): arming depends only on the current text +
caret, resolved by a cheap local lex/parse of the enclosing statement — **not** on the debounced background
`SemanticModel`, which trails typing. Tool A must give the same answer whether or not the async model has
caught up. (Tool C — IntelliSense — is what needs the full model, because it needs metadata/symbols; Tool A
needs only grammatical position, which is cheap and local.) This is the EmberTern-over-IBExpert edge and a
payoff of the Etap 6.9 AST work.

---

## 6. Presentation

First version: the **OverlayLayer hint** (reusing `EditorPopups` / `ClampIntoOverlay`) — a small,
non-focusable, non-hit-testable card near the caret showing `⇥ <result>`. Updates per keystroke, vanishes
the instant the prefix stops matching, never stacks over the completion list.

**The mechanism is independent of the presentation.** True inline **ghost text** is a possible later
upgrade that replaces *only* the presentation layer (it needs a custom `VisualLineElementGenerator` /
overlay — AvaloniaEdit 12 has no first-class inline-suggestion API — so it's its own spike, deferred).

---

## 7. Explicit-control contract

- **Tab** performs a special action **only when a hint is visible**, and the hint shows exactly what it
  will do. No hint → Tab indents normally.
- **Enter** always = newline + auto-indent. No grammar jumps, ever.
- **Esc** dismisses the hint (Tab returns to indenting).
- When a completion list is open, it owns Tab (accept the item); a construct hint does not arm while the
  list is open. If no list is open and a hint is visible, Tab expands.

---

## 8. Architecture (thin, reuses the front-end)

- **Core (pure, testable):**
  - `LanguageConstruct` catalog — declarative `{ spelling, expansion, caretOffset, armContext }` data.
  - `ConstructCompletionResolver` — given the text + caret, returns `(expansionText, caretOffset)?` for the
    armed construct (prefix match against the catalog ∩ grammar-valid starts). **Synchronous and pure — it
    takes the text/caret, not the async `SemanticModel`** (§1/§5), so arming can never depend on timing.
  - The **synchronous** "valid construct-starts here" grammatical-context computation (a cheap local
    lex/parse of the enclosing statement), reused by the IntelliSense auto-pop gate so both tools read one
    context notion — but Tool A calls it inline, never awaiting a background rebuild.
- **App:**
  - A small controller (sibling of the completion controller, sharing its `Attach` seams and
    `CaretContext` word detection) that: resolves synchronously on each keystroke, shows/hides the
    OverlayLayer hint immediately (no timer), binds Tab to expand-when-armed (else normal Tab), applies
    casing via `CaseMatcher`.
  - `begin … end` pairing + auto-indent + delimiter pairing as an editor input/indentation strategy
    (Typing Ergonomics — §3), separate from the Language-Completion controller.
- **Reuse:** `CaretContext`, `EditorPopups`/`ClampIntoOverlay`, `SqlCaseStyleDetector`/`CaseMatcher`, the
  `SqlEditorBehavior.Attach` + MainWindow seams (gotcha #219 — attach in both), the AST context signal.
- **Not reused:** the entire M2 snippet engine (§11).

---

## 9. Coexistence & precedence

At any caret, at most one of {construct hint, completion list} is present, by grammar. Precedence if both
could apply: an open completion list wins Tab. Tool B (indent/pairing) is always available and never
conflicts (it acts on typing/Enter, not Tab).

---

## 10. Scope

**First cut:** Tool A (catalog + Tab + OverlayLayer hint + grammar arming), `begin` block creation, and
AST-aware auto-indent — these three are the "faster than IBExpert" experience. Tool C continues on its own
track.

**Later / deferred:** delimiter-pairing polish; a *shown, Tab-based* "leave the construct / jump to body"
ergonomic (never Enter); true ghost text (presentation upgrade); more catalog entries as gaps surface;
user-defined constructs (exposing the catalog data).

---

## 11. Disposition of M2

The M2 Smart Snippets implementation (uncommitted) is **reverted**: `SnippetLayout`, `EditorSnippetExpander`,
mirrored placeholders, final-caret, the enriched snippet completion row, and the `SnippetEngine` role in the
completion list all go. The only surviving idea — "the caret's landing offset" — is one integer per catalog
row here. `CompletionMatcher` (prefix-first) is **kept** — it's Tool C.

---

## 12. Testing

- **Core (pure, cheap):** catalog prefix resolution (unique/ambiguous/casing), grammar "valid starts here"
  for representative query/PSQL carets, expansion text + caret offset per construct.
- **App:** a headless probe (the `ConnectionExpandBindingProbe` pattern) that types a prefix and asserts the
  hint content + that Tab produces the exact expansion and caret; auto-indent + `begin` pairing over sample
  input.
- **QA rule:** the on-screen hint feel, Tab/Enter behaviour, and grammar arming in both themes await the
  user's interactive confirmation before "done."

---

## 13. Resolved decisions & the one remaining detail

**Resolved (2026-07-16):** `begin … end` is a **structural delimiter pair owned by Typing Ergonomics**, not
a Language-Completion construct. The former "Tab vs Enter for begin" question is therefore moot — begin is
not in the catalog and produces no Language-Completion hint. Language Completion finishes *constructs*;
Typing Ergonomics maintains *pairs*; the two responsibilities stay separate.

**Remaining detail (implementation-time, not blocking):** the exact `begin` opener trigger — pair the
moment `begin` is a complete keyword token vs. on the following whitespace. Both are deterministic and
timing-free; we'll pick whichever reads best when we build the pairing strategy. No further design decision
is needed to start.

The interaction model is complete. Next: finalize the doc's place in the docs map, revert M2, and begin
implementation with the pure Core catalog + synchronous resolver (the safe, testable foundation).
