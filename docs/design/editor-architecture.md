# EmberTern — SQL/PSQL Editor Architecture & Modernization

> **STATUS: APPROVED (frozen 2026-07-09).** This is the binding design for the editor
> front-end. Per the staged-implementation contract: later work EXTENDS this design and never
> silently changes it — if implementation reveals a frozen decision must change, **STOP and
> consult the user before altering this doc or the code.**
>
> **This document describes the CURRENT architecture and design decisions only.** The
> etap-by-etap "as-built" narrative (what was tried, what broke, how each milestone actually
> shipped) lives in
> **[`docs/history/14-editor-language-frontend-history.md`](../history/14-editor-language-frontend-history.md)**
> — read it for the "why/how we got here" story. For exactly what's done, what's in flight, and
> what to do next, see **`CLAUDE.md`**'s "Current state" and "Editor Architecture — current
> direction" sections — this doc doesn't duplicate that (it would go stale the moment the next
> session ships something). This split happened during the 2026-07-11 Documentation Cleanup
> Sprint; nothing below was reworded, only the historical/status material was moved out.
>
> **One-paragraph status (detail in `CLAUDE.md`):** Etaps 0–6 are complete — one shared
> Lexer → Parser → AST → Semantic Model in `EmberTern.Core.Sql.Language`, with Completion,
> Signature Help, Snippets, Navigation (Ctrl+hover/Ctrl+Click, Peek Definition, safe local
> rename, find references), semantic highlighting, and Quick Info all built as *clients* of that
> one model. After Etap 6 the user reviewed the result against IBExpert, **endorsed the
> architecture**, and filed a UX Polish Phase backlog (P1–P9); most of it is done, with formatter
> polish (P8) and two small items (P5d, P2c) explicitly deferred. **Etap 7 (diagnostics, folding,
> breadcrumbs, bracket-matching) does not start until the user formally closes the UX Polish
> Phase.**

---

The SQL/PSQL editor is the single most-used surface in EmberTern and the most important
element of the whole application. Its quality shapes the product's reception more than most
other features combined. The goal is **not to catch up to IBExpert** — it is a modern
Firebird editor whose ergonomics, speed, and intelligence are comparable to Rider / Visual
Studio / DataGrip, while keeping the working comfort users know from IBExpert. **Quality,
stability, and safety of the architecture outrank speed of feature delivery.**

---

## 0. PARAMOUNT LAW — Never lose information / never corrupt user code or metadata

> This is the **first and highest** rule of the whole project, above every feature.
> Class: **Critical / Data-Loss.**

**EmberTern must NEVER damage the user's code or the database's metadata.** Origin: a group
procedure recompile once stripped input-parameter defaults and broke system mechanisms
(gotcha #175). That class of bug is absolutely unacceptable.

Derived, binding rules — every feature that **generates DDL or modifies code/objects**
(formatter, recompile, refactor, Quick Fix, Rename, future AI, snippet expansion, anything):

1. **If EmberTern is not 100% certain it can reproduce an object identically, it must NOT
   modify it automatically.** Uncertainty ⇒ do nothing (or ask), never guess.
2. **Never lose information.** Any fragment the parser/formatter does not fully understand
   is preserved **verbatim, 1:1** — never dropped, reordered, re-quoted, or "cleaned up."
3. **Correctness of generated code outranks aesthetics.** A slightly less pretty but
   provably-identical output always beats a prettier output that risks a semantic change.

In the editor front-end this law is realized by two mechanisms (§4.2): the parser is
**error-tolerant** (never throws, produces partial trees), and any span it doesn't
understand becomes a **`RawStatement` node that round-trips byte-for-byte**. The formatter
prints such nodes unchanged; all other features skip them.

---

## 1. Thesis (APPROVED)

Every symptom reported — the formatter's "endless list of exceptions", the IntelliSense
lag, the absence of smart completion / parameter info / semantic highlighting / real
navigation — has **one root cause**: the editor stack has **no shared language front-end**.
There is no lexer, no parser, no syntax tree (AST), and no semantic model. Every feature is
a separate ad-hoc character/token scan.

The fix is the model every serious IDE uses (Roslyn, TypeScript, Rider): **one
error-tolerant front-end — Lexer → Parser → AST → Semantic Model — and every feature
(formatter, completion, navigation, diagnostics, signature help, folding) is a *client* of
it.** A formatter that walks a tree does not need "if the previous token is `execute`"
rules — it knows it is inside an `ExecuteBlockStatement`.

This is a multi-year foundation, built incrementally so each etap ships user value. We do
**not** continue developing several independent parsers/scanners.

---

## 2. Current-state assessment (grounded in the code)

> **This is the ORIGINAL pre-rebuild assessment (2026-07-09) — the problems that
> motivated §1's thesis and the roadmap in §12. Kept verbatim for context on *why* this
> architecture exists; do not read §2.2's problem list as "still true today" — Etaps 0–6
> (see §15 / `CLAUDE.md`) fixed almost everything in it. §2.1 ("what's worth keeping")
> is still accurate; it describes what the new front-end was built on top of.**

### 2.1 What exists (and is worth keeping)
- **AvaloniaEdit `TextEditor`** as the editing surface — the right choice; keep it. Gives
  us the document model, virtualization, `IBackgroundRenderer` /
  `DocumentColorizingTransformer` hooks, the built-in `SearchPanel`, and the
  `CompletionWindow`. We build *on* it, not replace it.
- **`SqlScanHelpers`** (`Core/Sql`, ~300 lines) — a genuinely good shared low-level
  character scanner: trivia/quote/identifier/paren skipping + the CASE-aware `BEGIN…END`
  scanners (gotcha #129). **9 consumers ride it.** The seed of the real lexer.
- **Signature parsers** (`Procedure/Function/Trigger/View`) + `ProcedureBodyScanner` /
  `ProcedureBodySplitter` / `ProcedureBodyModel` / `PackageSourceScanner` — forward-scans on
  `SqlScanHelpers`. Correct and tested, but shallow DTOs, not a tree. They can later be
  re-expressed as AST queries (collapsing the parser zoo).
- **Snippet infrastructure** — `SqlSnippet` / `SqlSnippetBuilder` / `SqlTemplateRegistry`
  already model tab-stop placeholder offsets. **The Snippet Engine is half-built** and
  should be reused, not re-created.
- **`OccurrenceHighlighter`** (select an identifier → box occurrences) — keep.
- **`SqlEditorBehavior.Attach`** — the one-call installer that gives every editor (SQL
  editor, procedure/trigger/function/view bodies) the same capabilities. This wiring pattern
  is correct; it stays the integration point.

### 2.2 What is structurally wrong
1. **No AST anywhere.** Every feature scans text independently.
2. **7 independent SQL scanners + XSHD as an 8th lexer.** `SqlScanHelpers` unifies the
   parser family, but four outliers each re-implement literal/comment/identifier skipping:
   `SqlFormatter.Tokenize`, `SqlAliasResolver.Tokenize` (own `TokenKind` enum),
   `SqlStatementClassifier` (a byte-for-byte copy of `SqlScanHelpers.SkipTrivia`), and
   `TraceSqlInliner`.
3. **3 divergent keyword lists** — `SqlKeywords.All`, `SqlFormatter`'s hashsets, and the two
   XSHD files. They drift.
4. **The formatter is a flat-token pass (`SqlFormatter.cs`, ~1068 lines).** `IsPsql`/
   `FindBodyStart` heuristics + recursive descent *over the flat token list* (never a tree) +
   a post-emit string wrap pass keyed on `line.StartsWith("select ")`. Each new shape = a new
   special case. This is the architecture to eliminate.
5. **IntelliSense runs on the UI thread, per keystroke, over the whole document.**
   `SqlCompletionController.OnTextEntered` (fires on every char): reads `_editor.Text`
   (materializes the entire document) for `GetCurrentWord`; calls `TryShowDotCompletion` **on
   every identifier keystroke**, which runs `SqlAliasResolver.ParseAliases(wholeDocument)` — a
   **full re-tokenize of the entire document, synchronously, on the UI thread, every
   keystroke**; auto-triggers at 3 chars with no debounce. Only the dot→column DB fetch is
   async. This is the lag and the over-eagerness.
6. **No semantic model.** Highlighting is token-class only (XSHD regex). No notion of "this
   identifier is a table vs a column vs an alias vs a variable." Navigation (double-click /
   Ctrl+Click → open DDL) exists but is **invisible**: no hover affordance, no cursor change,
   no underline, no tooltip.
7. **No diagnostics, no signature/parameter help, no folding, no go-to-def/peek/find-refs/rename.**

### 2.3 The one success story to emulate
`SqlScanHelpers.TryScanBeginEndBlock` consolidated the repeated CASE/END miscount bug
(gotchas #117/#128/#129) into **one** implementation. The whole editor stack should be that:
one implementation of each concern, everyone else a client. The AST front-end is that
principle applied to the entire grammar.

---

## 3. Problem list (complaint → root cause → fix)

| # | Symptom | Root cause | Fixed by |
|---|---|---|---|
| P1 | Formatter mangles EXECUTE BLOCK, long INSERT/VALUES, FOR SELECT, BEGIN/END, IF, JOIN, WHERE AND/OR | flat-token formatter, no structure | AST + AST-driven formatter (Etap 3) |
| P2 | IntelliSense lags, pops too early | per-keystroke whole-document tokenize on UI thread, 3-char trigger, no debounce | async/debounced completion pipeline (Etap 0) |
| P3 | No smart completion (`if` → `if () then`, `execute` → block template) | no completion engine, snippets not wired to typing | Snippet/Completion engine (Etap 5) |
| P4 | No parameter info (INSERT/VALUES column hint, and beyond) | no signature-help model | Signature Help over AST + metadata (Etap 5+, see §8) |
| P5 | Only token highlighting; light theme low-contrast/muddy | no semantic model; 3 keyword sources; XSHD can't classify by resolution | palette unification (Etap 1) + semantic highlighting (Etap 6) |
| P6 | Clickable identifiers give no visual cue | no hover/cursor/underline affordance | Navigation Engine (Etap 6) |
| P7 | No folding / peek / find-refs / rename / diagnostics / breadcrumbs | no AST + semantic model | Etaps 6–7 |
| P8 | Duplication: 7 scanners, 3 keyword lists | no shared front-end | Lexer unification (Etap 1) |
| P9 | User must open an object's definition just to check a column's type/domain/nullability/etc. | no semantic model, no quick-documentation surface | Quick Info (§8A) over Semantic Model + metadata (Etaps 4–6) |

---

## 4. Target architecture

### 4.1 Component map (10 components, layered)

```
                    ┌─────────────────────── EmberTern.Core (pure, zero deps, offline-testable) ──────────────────────────┐
   editor text ───► │  Lexer  ─►  Parser  ─►  AST  ─►  Semantic Model                                                      │
                    │    │          │                      │                                                                │
                    │    │          │        ┌─────────────┼───────────────┬──────────────┬───────────────┬──────────┐    │
                    │    │          │     Formatter    Completion Engine  Navigation   Diagnostics   Signature Help  Snippet│
                    │    │          │        │              │             Engine        Engine          Engine       Engine │
                    └────┼──────────┼────────┼──────────────┼──────────────┼──────────────┼───────────────┼──────────┼──────┘
                         │          │        │              │              │              │               │          │
   ┌───────────────── EmberTern.App (Avalonia glue only — no grammar logic) ──────────────────────────────────────────────┐
   │  AvaloniaEdit TextEditor  •  colorizers (semantic paint)  •  CompletionWindow  •  hover/cursor  •  overlay adorners    │
   │  EditorLanguageService  — owns the debounced/async parse loop, caches the tree, fans results out to the glue above    │
   └────────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Everything grammar-related lives in `EmberTern.Core.Sql.Language`** (new namespace).
  Pure, zero Avalonia, unit-testable without a window or a DB — the layering rule the project
  already enforces. Reusable by the object editors, the trace SQL inliner, the performance
  predicate extractor, and the statement classifier.
- **The App layer holds only AvaloniaEdit glue**: painting semantic colors, showing the
  completion window, hover/cursor changes, adorners. It owns **`EditorLanguageService`** — the
  single async engine that re-parses on idle and hands the tree/diagnostics/etc. to the glue.

### 4.2 Non-negotiable design principles (APPROVED)

0. **The Paramount Law (§0) governs everything below.** Never lose information; never modify
   what we can't reproduce identically; correctness over aesthetics.
1. **The parser is ERROR-TOLERANT.** Editor text is almost always incomplete or invalid
   mid-typing. The parser MUST NEVER throw or return null — it produces a **partial tree with
   error nodes** and keeps going (Roslyn/TypeScript-style recovery). A strict parser is
   useless for an editor.
2. **Unknown-construct safety valve = `RawStatement` (round-trips verbatim).** Any span the
   parser doesn't understand becomes a raw node that emits **byte-for-byte unchanged**. This
   is the §0 law in code and bounds parser scope creep: EmberTern never loses or mangles SQL
   it can't parse.
3. **Parsing is async, debounced, cancellable, incremental.** Never on the keystroke. Lexing
   is O(n) and cheap; parsing runs on idle off the UI thread with a `CancellationToken`; the
   tree is cached and only the edited region re-parsed when feasible. The UI thread never
   blocks on parse.
4. **Immutable AST with absolute source spans on every node** (offset+length). Trivia
   (whitespace/comments) is attached to nodes so the formatter and features round-trip. Every
   feature maps a caret offset → the node at that offset in O(log n).
5. **Hand-written recursive-descent parser, NOT a generator (ANTLR / no external grammar
   dep).** Full control of error recovery and incrementalism; keeps Core zero-dependency;
   what every serious IDE does. (§13 R1.)
6. **One keyword source of truth.** A single `FirebirdSyntax` catalog (keywords by category,
   types, built-in functions) drives the lexer, completion, the formatter, and the
   highlighting palette. The XSHD files become derived, not hand-maintained parallel lists.
7. **The formatter is deterministic and idempotent.** Formatting the same code repeatedly
   always yields byte-identical output (`Format(Format(x)) == Format(x)`), and identical input
   always produces identical output. Pinned by tests.

### 4.3 Data flow (a keystroke)
1. User types → AvaloniaEdit updates the document (never blocked).
2. `EditorLanguageService` schedules a debounced (≈300 ms) background re-parse (cancels any
   in-flight one). Ctrl+Space / explicit triggers bypass the debounce and use the last cached
   tree immediately.
3. Background: Lexer → Parser → AST (+ error nodes) → Semantic Model (resolve identifiers
   against the connection's metadata cache).
4. Results marshalled to the UI thread: re-paint semantic colors, refresh diagnostics
   squiggles, update breadcrumbs/folding. Completion/hover/signature-help pull from the cached
   tree on demand.

---

## 5. Component specifications

Each: responsibility · key API (illustrative) · where it lives · what it replaces.

### 5.1 Lexer — `Core.Sql.Language.SqlLexer` (Etap 1)
- Turns text into an immutable token stream (kind, span, attached trivia). Firebird-aware:
  string literals with `''` escape, quoted `"…"` identifiers, `--` and `/* */` comments,
  `:name`/`?`/`@name` params, operators, numbers, dialect quirks. One `TokenKind` enum for the
  whole app. Backed by the single `FirebirdSyntax` keyword catalog.
- `IReadOnlyList<SqlToken> Tokenize(string text)` (+ an incremental variant later).
- **Replaces**: `SqlFormatter.Tokenize`, `SqlAliasResolver.Tokenize`,
  `SqlStatementClassifier`'s private scanner, `TraceSqlInliner`'s scanner; unifies the keyword
  lists. `SqlScanHelpers` primitives fold in.

### 5.2 Parser — `Core.Sql.Language.SqlParser` (Etap 2)
- Error-tolerant recursive descent producing the AST. Covers the grammar the editor needs
  (§5.4). Emits `Diagnostic`s for recovery points.
- `ParseResult Parse(IReadOnlyList<SqlToken> tokens)` → `{ SqlScript Root, IReadOnlyList<Diagnostic> }`.
- **Replaces (progressively)**: the ad-hoc structural logic in `SqlFormatter`,
  `SqlStatementClassifier`, `SqlParameterScanner`, and eventually the four signature parsers +
  body splitters (they become AST queries).

### 5.3 AST — `Core.Sql.Language.Ast` (Etap 2)
- Immutable node hierarchy with spans + trivia. Statement nodes (Select/Insert/Update/Delete/
  Merge/ExecuteBlock/ExecuteProcedure/Create*/Alter*/Drop*), PSQL nodes (Block/If/While/
  For(-Select)/Case/Declare/Assignment/Suspend/exception handling), expression nodes, clause
  nodes (From/Join/Where/GroupBy/Having/OrderBy/Cte), and the `RawStatement` safety-valve node.
- `SqlNode NodeAt(int offset)`, `IEnumerable<T> Descendants<T>()`, typed accessors.

### 5.4 Grammar coverage target
`SELECT` (+ CTE / `WITH` / `UNION`/`INTERSECT`/`EXCEPT` / subqueries / window if present),
`INSERT` (+ `VALUES` / `SELECT` / `UPDATE OR INSERT` / `MERGE`), `UPDATE`, `DELETE`,
`EXECUTE BLOCK`, `EXECUTE PROCEDURE`, `CREATE/ALTER/RECREATE PROCEDURE|FUNCTION|TRIGGER|VIEW|
PACKAGE|DOMAIN|EXCEPTION|GENERATOR|INDEX|TABLE`, and PSQL bodies (`BEGIN/END`, `IF/THEN/ELSE`,
`WHILE/DO`, `FOR … DO`, `FOR SELECT … INTO … DO`, `CASE`, `DECLARE`, `SUSPEND`, cursors,
`WHEN … DO` handlers). Anything else ⇒ `RawStatement` (verbatim, §0).

### 5.5 Semantic Model — `Core.Sql.Language.SemanticModel` (Etap 4)
- Binds the AST to meaning using the connection's metadata cache (tables/views/columns/
  procedures/functions/triggers/generators/domains/exceptions/packages — already available via
  the existing readers + `MainWindowViewModel.EnumerateLoadedObjects` / `EnsureColumnsAsync`)
  plus **local scope** (aliases, PSQL variables, params, `NEW`/`OLD`, cursors). Resolves each
  identifier to a `Symbol` (kind + defining metadata) or marks it unresolved.
- `Symbol? Resolve(IdentifierNode)`, `IReadOnlyList<ColumnSymbol> ColumnsInScope(int offset)`,
  `Scope ScopeAt(int offset)`.
- **Unlocks** semantic highlighting, context-aware completion, go-to-def, find-refs, rename,
  and "unknown column/table" diagnostics. **Fixes** the current alias resolver's
  subquery-blindness (gotcha #18) with real nested scopes.

### 5.6 Formatter — `Core.Sql.SqlFormatter` (Etap 3; as-built record in the history file)
- Walks the AST and pretty-prints per a **style profile**. No token special-cases. Handles
  every §5.4 construct by node type. `RawStatement` prints verbatim (§0).
- `string Format(SqlScript root, FormatOptions options)`.
- **One good default style first** (§6): IBExpert-inspired (familiar to migrating users; not a
  1:1 copy). **Deterministic + idempotent** (principle #7). The style-options **config panel is
  deferred** to the future application configurator; the options structure exists internally
  from day one but is not surfaced yet.
- **Acceptance bar**: pass *all* existing `SqlFormatterTests` + `PsqlFormatterTests` (they
  become the regression suite), plus new corpus/idempotency/round-trip tests, and beat the
  current output on the named pain cases (EXECUTE BLOCK, long INSERT/VALUES, FOR SELECT, IF,
  JOIN, WHERE AND/OR).
- **Replaces** `SqlFormatter.cs` entirely (kept until parity is proven, then retired).

### 5.7 Completion Engine — `Core.Sql.Language.CompletionEngine` (Etap 5)
- Given AST + Semantic Model + caret, returns a **context-ranked** completion list: after
  `FROM`/`JOIN` → tables/views; after `alias.` → that table's columns; inside a PSQL body →
  variables/params/`NEW`/`OLD`; after `EXECUTE PROCEDURE` → procedures; in an expression →
  functions + columns in scope; keyword position → relevant keywords only. Case-preserving
  insert (reuse `CaseMatcher`).
- `CompletionResult GetCompletions(SemanticModel model, int offset, CompletionTrigger trigger)`.
- **Replaces** the logic split across `SqlCompletionContext` + the controller +
  `SqlAliasResolver`. The App-side controller becomes thin glue.

### 5.8 Navigation Engine — `Core.Sql.Language.NavigationEngine` (Etap 6)
- `SymbolReference? ReferenceAt(int offset)` (what's under the cursor + is it navigable),
  `IReadOnlyList<Span> LocalReferences(Symbol, root)`, `Span? LocalDefinition(Symbol)`.
- Drives Ctrl+hover affordance (§10), go-to-definition (open DDL/detail — reuse
  `TryOpenDdlForWord`), peek, local find-refs / rename. Cross-DB find-refs reuses the existing
  Global Search (`FirebirdMetadataSearchReader` `CONTAINING`).

### 5.9 Diagnostics Engine — `Core.Sql.Language.DiagnosticsEngine` (Etap 7)
- Merges parser recovery diagnostics + semantic diagnostics (unknown table/column with a live
  connection, INSERT column/value **count mismatch**, unresolved variable, ambiguous column).
  Each `Diagnostic { Span, Severity, Message, Code, QuickFixes[] }`.
- Deliberately **conservative** (the project's "prefer silence over false positives" rule): only
  flag what's certain; unknown-with-no-connection ⇒ no diagnostic. Quick Fixes obey §0 (never a
  fix we can't apply safely).

### 5.10 Signature Help — `Core.Sql.Language.SignatureHelpEngine` (Etap 5+)
- `SignatureInfo? GetSignature(SemanticModel, int offset)`. Scope is broad (§8): INSERT/VALUES,
  INSERT … SELECT, UPDATE, EXECUTE PROCEDURE, CREATE PROCEDURE/FUNCTION parameter lists, and
  every place the user works with parameters.

### 5.11 Snippet Engine — `Core.Sql.Language.SnippetEngine` (Etap 5)
- Smart completion / live templates with tab-stops. **Reuses the existing `SqlSnippet` /
  `SqlSnippetBuilder` / `SqlTemplateRegistry`** (already track placeholder offsets). Templates
  for `if`→`if (·) then … end`, `declare`→`declare variable ·`, `execute`→EXECUTE BLOCK
  skeleton, `for select`, `create procedure/function/trigger/exception/domain/index`, etc.
  Tab-stop navigation in AvaloniaEdit via an overlay. Snippet expansion obeys §0.

### 5.12 Quick Info Engine — `Core.Sql.Language.QuickInfoEngine` (Etaps 4–6, §8A)
- Given AST + Semantic Model + caret (or a completion-list item), returns a structured
  **quick-documentation** model for the resolved symbol — the "check an object without
  opening its definition" surface (P9). Pure data; the App renders it as the Ctrl-hover
  tooltip (§9.4/§10) and as the completion-item detail pane.
- `QuickInfo? GetQuickInfo(SemanticModel model, int offset)` / `QuickInfo ForSymbol(Symbol)`.
- Content is per-kind (§8A) — column type/domain/nullability/default/description/PK-FK/
  identity-computed/owning-table; procedure/function/trigger/domain/exception/generator/view
  summaries. Sources: the metadata readers already feed the Semantic Model. Reuses the
  Signature Help / metadata plumbing (§5.10) rather than a parallel fetch path.

---

## 6. Formatter — default style (APPROVED)

- **One very good opinionated default first.** Config panel deferred to the future
  application configurator.
- **Default style IBExpert-inspired** — most users migrate from IBExpert, so the formatted
  code should look familiar. Not a 1:1 copy; keep the familiar shape (clause breaks, JOIN/ON
  layout, per-column lists, BEGIN/END placement).
- **Deterministic + idempotent** (principle #7) — non-negotiable; pinned by tests.
- **Never loses information** (§0) — `RawStatement` and any unrecognized construct round-trip
  verbatim; the formatter never "corrects" SQL it doesn't fully model.

---

## 7. IntelliSense redesign (P2) — concrete (Etap 0)

Move the whole pipeline off the keystroke:
1. **`EditorLanguageService`** debounces edits (≈300 ms idle) and re-parses/re-scans in the
   background with cancellation; caches results.
2. `OnTextEntered` does **no document-wide work**. It only decides *whether* to open the window
   (cheap: inspect the few chars before the caret, not the whole `Text`) and, if so, asks the
   completion path against **cached** state.
3. **No more `ParseAliases(wholeDocument)` per keystroke** — alias/scope info comes from cached
   state (Etap 0 uses the existing resolver, run debounced/off-thread; Etap 5 replaces it with
   the Semantic Model).
4. **Auto-popup is not aggressive.** Reasonable defaults now (idle-based, not eager at 3 chars
   mid-burst); a configurable **Auto-Popup delay (with a full-disable option)** is deferred to
   the application configurator. **Ctrl+Space ALWAYS works immediately** from the cached state.
5. Stop repeatedly materializing `_editor.Text`; work off the AvaloniaEdit `Document` + offsets.

This alone (Etap 0, before any AST) removes the lag and the over-eagerness.

---

## 8. Parameter Info / Signature Help (P4) — one of the strongest features (APPROVED scope)

Driven by the AST + Semantic Model + metadata. Scope is broad — **everywhere the user works
with parameters**, not just INSERT/VALUES:
- **INSERT / VALUES** — highlight the column matching the value under the caret; show `NAME
  TYPE NULL/NOT NULL [domain] [default]`. **Beyond IBExpert**: a live diagnostic when the INSERT
  column count ≠ VALUES count, and a type hint when a value literal mismatches the column type.
- **INSERT … SELECT** — map the SELECT projection position under the caret to the target column.
- **UPDATE** — for `SET col = …`, show the column being assigned + its type/nullability/domain.
- **EXECUTE PROCEDURE** — the procedure's parameter list with the active argument highlighted
  (IN/OUT, types, defaults).
- **CREATE PROCEDURE / CREATE FUNCTION** — assist while declaring parameters/returns.
- Generally: any call site or DML position where a value maps to a typed target.

---

## 8A. Quick Info / object documentation (Item 13, P9) — APPROVED requirement

> Added by the user 2026-07-09 and adopted as an approved requirement. **Not a patch of the
> Etap-0 IntelliSense responsiveness work** — this is a *semantic* feature and is
> deliberately deferred until the architecture is ready (Semantic Model + Quick-Info surface
> + Ctrl-hover). No provisional/half version is to be built before then.

**Goal (user's words, distilled):** the user must be able to check the key facts about an
object **without opening its definition** — a modern equivalent of Rider / Visual Studio
"quick documentation". This surfaces two ways:
- **Ctrl+hover** over an identifier → a tooltip with the object's quick info (§9.4/§10).
- **In the completion list** → when a name is selected (including a **fully-typed** name, so
  Ctrl+Space on a complete identifier shows its info rather than an empty list), the detail
  pane shows the same quick info.

**Content by object kind** (baseline — extend it if more is clearly useful; this is a
"modern quick documentation", not a fixed minimum):
- **Column** — data type · domain · `NULL` / `NOT NULL` · default value · description ·
  `PRIMARY KEY` / `FOREIGN KEY` (with the referenced table for FK) · Identity / Computed ·
  the owning table.
- **Procedure / Function** — name · input params (name/type/nullability/default) · returns /
  output params · deterministic (function) · description.
- **Trigger** — table · timing (BEFORE/AFTER) · events (INSERT/UPDATE/DELETE) · active ·
  position · description.
- **Domain** — base type · nullability · default · check · charset/collation · description.
- **Exception** — message · description.
- **Generator/Sequence** — current value · description.
- **View** — column list · description (and, on demand, the source).
- **Other kinds** (package, index, role) — the analogous key facts.

**Realized by** the Quick Info Engine (§5.12), the Semantic Model (§5.5, resolves the
identifier under the caret to a symbol + its metadata), and the Ctrl-hover surface (§9.4).
Data comes from the metadata readers already wired into the Semantic Model — no parallel
fetch path, no new "documentation store".

**Roadmap (revised 2026-07-10):** the Semantic Model foundation (resolve identifier → symbol +
metadata) landed in **Etap 4**, but the entire Quick-Info *feature* — the Quick Info Engine (§5.12),
the Ctrl-hover tooltip surface, and the completion-list detail pane — is delivered **wholly in
Etap 6**. It is explicitly **NOT part of Etaps 0–5** (user decision — see the history file's Etap 5 record): Etap 5 is code-writing
assistance only and adds no new symbol-info surface (the completion list keeps its existing `: TYPE`
display unchanged). §0 applies (read-only feature — it never modifies code, so it cannot lose
information).

---

## 9. Syntax highlighting + Semantic highlighting — UX analysis & proposed system (APPROVED to design)

The user asked for a UX-level analysis of the whole coloring system (not just a patch),
noting the dark theme is good but the **light theme has colors too similar — some elements
stop being distinguishable.** Below is the analysis + the proposed system. **Exact hex values
are tuned with visual verification during implementation (Etap 1 lexical layer, Etap 6
semantic layer)** — this section fixes the *system and principles*, which are what we freeze.

### 9.1 Why the current system has problems
- **Three sources of truth** for "what is colored" (`SqlKeywords`, formatter hashsets, two
  XSHD files) → drift and inconsistency.
- **XSHD is purely lexical** — it colors by token class (keyword/string/number), and
  **cannot** express semantic roles (table vs column vs variable). Real semantic highlighting
  is impossible with XSHD alone (gotcha #19); it needs a `DocumentColorizingTransformer` fed by
  the Semantic Model.
- **Light theme is derived from VS Code "Light+"**, whose palette runs several roles into
  similar mid-tones (blue keyword vs teal data-type vs purple DDL vs brown function vs near-black
  operator/identifier). At small font sizes on a near-white background these lose separation —
  exactly the reported "some elements stop being distinguishable."
- **The "Christmas-tree" risk**: coloring too many roles with strong hues makes code noisy and
  *reduces* comprehension.

### 9.2 Proposed system — "calm base, semantic accent" (two layers)

**Layer 1 — Lexical (from the Lexer; the base coat).** Restrained. Most text sits near the
foreground color; color is spent sparingly.
- Keywords: one calm keyword color (optionally a *subtle* 2-way DML/DDL split, not two fighting
  hues). Comments: muted. Strings: warm, clearly non-code. Numbers: distinct but low-chroma.
  Operators/punctuation: near-foreground (don't color them loudly).

**Layer 2 — Semantic (from the Semantic Model; the accent, Etap 6).** Identifiers colored by
**resolved role**, grouped into hue families so roles are distinguishable yet harmonious:
- **Navigable schema objects** (table, view, procedure, function, trigger, package, generator,
  exception, domain) → **reuse the existing per-kind `IconColor_*` palette** so an object's color
  in the editor **matches its icon color in the metadata tree.** This is a double win: visual
  consistency across the app, and it teaches the user that **"a colored object identifier =
  something you can navigate to."** (Directly answers the navigation-recognition requirement.)
- **Columns** → one calm, readable color (columns are frequent — must not shout).
- **Aliases / variables / parameters** → a distinct low-chroma "local scope" treatment (a
  separate hue, possibly italic) signalling "local, not a DB object."
- **Data types** → their own hue.

### 9.3 Contrast & accessibility rules (both themes)
- Target **WCAG AA-ish** legibility (≈ 4.5:1 for code text vs the editor background) in **both**
  dark and light.
- Adjacent roles must differ in **both hue AND lightness** — so they're separable at a glance and
  for color-vision-deficient users, and specifically so the light theme stops being muddy.
- Single source of truth: **one `EditorPalette` token set in `Colors.axaml`, both themes** —
  replaces the 3 keyword lists + 2 XSHD palettes conceptually. Theme toggle re-resolves it.

### 9.4 Navigation affordance (the key UX ask)
Not a permanent underline (that clutters). Layered cues, Rider/VS-style:
- **Permanent cue = the semantic color** — a navigable object identifier is already colored, so
  the user learns "colored object = has a definition."
- **Actionable cue = Ctrl held + hover** — the identifier under the cursor gets an **underline +
  the cursor becomes a hand + a tooltip** (kind + signature). The underline appears *only* under
  Ctrl, so no permanent clutter. Ctrl+Click navigates (§10).
- This directly satisfies "the user must instantly know an element leads to a definition" without
  the Christmas-tree effect.

### 9.5 Deliverables of this section (when we reach the etaps)
- Etap 1: unify the keyword catalog; regenerate/clean the **light-theme lexical palette** for
  contrast (an early, standalone light-theme improvement, before the semantic layer exists).
- Etap 6: the `DocumentColorizingTransformer` semantic layer + the `EditorPalette` tokens + the
  Ctrl+hover affordance; final hex tuning with visual verification in both themes.

---

## 10. Navigation UX (P6) — APPROVED model (Etap 6)

Modern model: **Ctrl+Hover** (underline + hand cursor + tooltip, §9.4) and **Ctrl+Click**
(go-to-definition; already wired to `TryOpenDdlForWord`, now made discoverable). **Double-click
stays as optional IBExpert compatibility.** Add **Peek Definition** (inline flyout with the
object's DDL/source) so the user doesn't leave the editor. Local **rename** (alias/variable/
parameter within the statement/body) is safe and feasible; **cross-DB rename is out of scope**
(dangerous — §0). **Find references**: local from the AST; cross-DB via Global Search.

---

## 11. Beyond IBExpert — modern-IDE features (recommendations)

Prioritized; not all at once. Feasibility noted honestly.
- **Error squiggles + Quick Fixes** (add missing alias, fix INSERT/VALUES count, wrap in EXECUTE
  BLOCK) — Diagnostics Engine (Etap 7). Every fix obeys §0.
- **Live Templates / Snippets with tab-stops** — reuse existing snippet infra (Etap 5).
- **Code Folding** — AvaloniaEdit `FoldingManager`; regions come free from the AST (BEGIN/END,
  statements, CTEs). (Etap 7.)
- **Breadcrumbs** — "PROCEDURE X ▸ FOR SELECT ▸ IF" from the AST path at the caret. (Etap 7.)
- **BEGIN/END + bracket matching & structure-aware auto-indent** — from the AST. Fixes daily PSQL
  indenting pain. (Etap 7.)
- **Format selection / format-on-paste** — trivial once the AST formatter exists (Etap 3+).
- **Peek definition** (§10) — Etap 6.
- **Scope-aware completion inside subqueries/CTEs** — fixes the current wholesale-subquery-skip.
- **Statement-aware Execute-under-caret** driven by the AST statement boundary.
- **Minimap** — ❌ **DROPPED** (user decision): AvaloniaEdit has no built-in minimap; high cost,
  low value in a DB tool.
- **AI** — ⏸ **NOT designed now** (user decision). The architecture is deliberately AI-ready
  (AST + Semantic Model are the ideal foundation), but we do **not** design any feature *solely*
  for AI, and we add no AI dependency. If AI is added later it becomes another *client* of the
  same front-end.

---

## 12. Roadmap (APPROVED order — each etap: complete + tested + smoke + polished before the next)

Rationale for the change from the draft: **the Lexer is too important a foundation to be a
"parser detail"** — it gets its own etap.

- **Etap 0 — IntelliSense responsiveness** (highest value / lowest risk, **NO AST yet**).
  Introduce `EditorLanguageService` (debounced/async/cancellable); stop per-keystroke
  whole-document tokenizing; move alias resolution off the keystroke; Ctrl+Space immediate;
  reasonable non-aggressive auto-popup defaults. **Delivers**: the lag is gone.
- **Etap 1 — Lexer. ✅ DONE (2026-07-09; as-built in the history file).** One Firebird-aware lexer + the single
  `FirebirdSyntax` keyword catalog; folded outliers O2/O3/O4 (O1 deferred → Etap 3, O5 → Etap 2
  per audit §4); **regenerated the light-theme lexical palette for contrast**. **Delivered**:
  P8 (dedup) + a light-theme improvement.
- **Etap 2 — Parser + AST. ✅ DONE (2026-07-10; as-built in the history file).** Error-tolerant recursive descent;
  grammar per §5.4 at the approved "statement skeleton" depth (history file); `RawStatement` safety valve.
  **Delivered**: a cached tree (`SqlScript`); `SqlStatementClassifier` + `SqlParameterScanner`
  re-expressed as AST queries; outlier O5 (`FirebirdDdlExecutor.SplitStatements`) migrated onto
  the parser's boundaries behind a §0 corpus-diff gate.
- **Etap 3 — Formatter (AST-based). ✅ DONE (2026-07-10; as-built in the history file).** Retired the old heuristic
  `SqlFormatter` → rewrote it AST-based **under the same name** (transitional `SqlFormatterV2`
  consolidated away; old impl deleted); IBExpert-inspired default;
  deterministic/idempotent; parity gate = all existing formatter tests (byte-for-byte) + new
  corpus/idempotency/§0-token-and-comment-preservation tests; outlier O1 (`SqlFormatter.Tokenize`)
  dead. **Delivered**: P1.
- **Etap 4 — Semantic Model.** Bind AST ↔ metadata + local scope; nested scopes. **Delivers**:
  the foundation for completion/navigation/diagnostics/semantic-color, and for Quick Info (§8A —
  resolve identifier → symbol + metadata).
- **Etap 5 — Completion Engine + Snippet Engine + Signature Help. ✅ DONE (2026-07-10; as-built in the history file).**
  Context/scope-aware completion (engine-driven, wired into the controller at M5); broad Parameter
  Info (§8, M6/M7); keyword live templates with Tab-stops (M8). **Code-writing assistance only** — NO
  Quick Info of any form (user decision 2026-07-10 — history file); the completion list keeps its existing
  `: TYPE` display but adds no new symbol-info surface. **Delivered**: P3 + P4. Milestone breakdown
  milestone breakdown & as-built record in the history file.
- **Etap 6 — Navigation + Semantic highlighting + Quick Info. ✅ DONE — M1–M5 (2026-07-10/11,
  the history file's Etap 6 records).** Ctrl+hover/underline/cursor/tooltip, go-to-def, peek, local find-refs/rename; the semantic
  color layer (§9); **and the whole Quick Info feature** — the Quick Info Engine (§5.12), the Ctrl-hover
  tooltip, AND the completion-list detail pane (§8A, moved here in full from Etap 5). Milestones: **M1
  Quick Info Engine (Core) ✅ · M2 Navigation Engine (Core) ✅ · M3 Semantic highlighting (Core
  classifier + App painter) ✅ · M4 Ctrl+hover/click go-to-def + tooltip (App `NavigationController` + `QuickInfoView`) ✅ · M5 Quick Info detail pane
  + Peek + local find-refs/rename (F2 rename / Alt+F12 peek) ✅.** **Delivers**: P5 (semantic) + P6 + P9 (all of it).
- **Etap 7 — Diagnostics + editor niceties.** Squiggles + Quick Fixes; folding, breadcrumbs,
  bracket/BEGIN-END matching, format-selection/on-paste. **Delivers**: P7.
- **Final cleanup etap (after Etap 7).** Purge any remaining transitional names (`V2`/`NewX`/`Temp`/…
  — §14 decision #15), retire any coexistence shims, and re-home classes to their final namespaces so
  the front-end reads as one coherent system. (Names consolidated as each component's migration
  completes are already done — this etap is the safety net for anything left mid-flight.)

Signature parsers / body scanners get migrated onto the AST opportunistically (not a dedicated
etap) to shrink the parser zoo.

---

## 13. Risk assessment

- **R0 — Data loss / corruption (Critical, §0).** *Mitigation*: error-tolerant parser +
  `RawStatement` verbatim round-trip + "don't modify what we can't reproduce identically" +
  deterministic/idempotent formatter + old-vs-new corpus diffing before any generator replaces
  another. The paramount rule; every etap is checked against it.
- **R1 — Parser scope creep (Firebird grammar is large).** *Mitigation*: hand-written recursive
  descent (full control) + `RawStatement` verbatim safety valve; build grammar incrementally,
  driven by what the formatter needs. Hand-written over ANTLR because generated parsers have poor
  error recovery, are awkward to make incremental, and add a runtime dependency Core forbids.
- **R2 — Formatter regressions.** *Mitigation*: the current formatter's large test suite becomes
  the AST formatter's acceptance gate; add idempotency + round-trip + a real-SQL corpus (the Lab
  DB + the user's ERP scripts); run old-vs-new diff before switching.
- **R3 — Performance on large bodies.** *Mitigation*: O(n) lexing, debounced background parse,
  cancellation, tree caching, incremental re-parse; the UI thread never parses.
- **R4 — Firebird dialect/version variance (2.5/3/4/5, dialect 1/3).** *Mitigation*: tolerant
  lexer/parser; verify against the Lab DB and the user's real FB5 ERP; the editor's grammar is
  largely version-stable.
- **R5 — Migration coexistence.** *Mitigation*: the AST front-end lands behind consumers one at a
  time; `SqlScanHelpers` + the signature parsers stay until fully absorbed; each etap independently
  shippable and reversible.
- **R6 — AvaloniaEdit integration limits.** Semantic paint needs a `DocumentColorizingTransformer`
  (XSHD can't classify — gotcha #19); Ctrl+hover needs pointer tracking + cursor override +
  adorner. *Mitigation*: prototype each glue point early; minimap dropped.
- **R7 — Effort/timeline.** Multi-etap program. *Mitigation*: ordering front-loads felt value
  (Etap 0 quick win; Etap 3 the headline). Quality over speed is an explicit user directive.

---

## 14. Decisions (APPROVED 2026-07-09)

1. **Central thesis** — one shared error-tolerant Lexer→Parser→AST→Semantic front-end in Core,
   every feature a client. ✅ **Approved.** No more independent parsers/scanners.
2. **Hand-written recursive-descent parser**, no ANTLR / no external generator. ✅ **Approved.**
3. **`RawStatement` verbatim safety valve.** ✅ **Approved** — and elevated: see §0.
4. **PARAMOUNT LAW (new, user-added)** — never lose information; never corrupt user code or
   metadata; don't modify what we can't reproduce identically; correctness > aesthetics. ✅
   **Adopted as the project's #1 rule (§0).**
5. **Roadmap order** — changed per the user: Etap 0 IntelliSense · Etap 1 **Lexer (own etap)** ·
   Etap 2 Parser+AST · Etap 3 Formatter · Etap 4 Semantic Model · Etap 5 Completion (+Snippets
   +Signature Help) · Etap 6 Navigation (+Semantic highlighting) · Etap 7 Diagnostics
   (+niceties). ✅ **Approved (§12).**
6. **Minimap** — ❌ **Dropped.**
7. **Formatter** — one very good IBExpert-inspired default first; config panel later (with the app
   configurator); **deterministic + idempotent**. ✅ **Approved (§6).**
8. **IntelliSense** — rebuilt; non-aggressive auto-popup; reasonable defaults now, configurable
   Auto-Popup delay / disable later; **Ctrl+Space always immediate.** ✅ **Approved (§7).**
9. **Parameter Info** — a flagship feature; broad scope: INSERT/VALUES, INSERT…SELECT, UPDATE,
   EXECUTE PROCEDURE, CREATE PROCEDURE, CREATE FUNCTION, and every parameter site. ✅ **Approved (§8).**
10. **Syntax + Semantic highlighting** — full UX redesign approved; "calm base, semantic accent",
    object colors reuse the tree's per-kind palette, WCAG-minded contrast in both themes, light-theme
    fix, no Christmas-tree, navigation-recognition via color + Ctrl-hover. ✅ **Approved (§9).**
11. **Navigation** — Ctrl+Hover + Ctrl+Click; double-click optional IBExpert compat. ✅ **Approved (§10).**
12. **AI** — not designed now; architecture kept AI-ready; nothing designed *solely* for AI. ✅ **Approved (§11).**
13. **Working discipline** — editor is the most important element; quality/stability/safety over
    speed; **if any frozen assumption needs changing during implementation, STOP and consult the
    user before proceeding.** ✅ **Approved.**
14. **Quick Info / object documentation (Item 13, added 2026-07-09)** — a modern "quick
    documentation" (Rider/VS-style): check an object's key facts without opening its definition,
    via Ctrl-hover and the completion detail pane. Broad object coverage (columns, procedures,
    functions, triggers, domains, exceptions, generators, views, …). ✅ **Approved as a
    requirement (§8A).** Explicitly a semantic-layer feature — deferred to Etaps 4–6; **no
    provisional version before the architecture is ready** (user directive). Extend the content
    if more is clearly useful.
15. **Naming — responsibility, not history (added 2026-07-10, user directive).** No transitional
    names left in the codebase: no `V2` / `NewX` / `Temp` / `Parser2`, etc. A transitional suffix is
    allowed ONLY while the old implementation still exists in parallel; the moment the old one is
    deleted and the new one is the sole implementation, the class takes the plain responsibility name
    (e.g. `SqlFormatter`) and the old file is removed. EmberTern is a prototype in active rebuild — no
    multi-year API-compat obligation, no parallel old/new implementations kept once a migration ends.
    Consolidate immediately for a completed component; keep the transitional name only for one still
    mid-migration. ✅ **Approved.** The editor rebuild ends with a **final cleanup etap** (§12) that
    purges any remaining transitional names. (Applied at the end of Etap 3: `SqlFormatterV2` →
    `SqlFormatter`.)

---

---

## 15. Current status & remaining work

*(Renumbered from the old §15–§29, which were per-etap "as-built" completion records — those
now live, verbatim, in
[`docs/history/14-editor-language-frontend-history.md`](../history/14-editor-language-frontend-history.md).
This section is the compact, kept-current summary of what's done and what's left; extend it in
place rather than appending new dated blocks — that's what the history file is for.)*

### 15.1 Etaps 0–6 — complete

| Etap | Delivered | History detail |
|---|---|---|
| 0 — IntelliSense responsiveness | Debounced/async/cancellable parsing; no more per-keystroke whole-document tokenizing; Ctrl+Space always immediate. | §16 of the history file |
| 1 — Lexer | `SqlLexer` (Firebird-aware, lossless) + the single `FirebirdSyntax` keyword catalog; folded 3 of 4 outlier scanners onto it; regenerated the light-theme lexical palette for contrast. | §18 |
| 2 — Parser + AST | Error-tolerant recursive descent at the "statement skeleton" depth (§5.4); `RawStatement` verbatim safety valve; the old DDL statement splitter migrated onto the parser's boundaries behind a byte-identity diff gate. | §19 |
| 3 — Formatter (AST-based) | Retired the old flat-token `SqlFormatter`; rewrote it as an AST-dispatching formatter under the same name; deterministic + idempotent; the old formatter's full test suite kept green byte-for-byte as the parity gate. | §20 |
| 4 — Semantic Model | `SemanticModel` binds the AST to meaning (scope tree + symbols + resolved references); the two-phase Query binder resolves a column qualifier against a FROM alias that appears later in the statement text. | §21 |
| 5 — Completion + Signature Help + Snippets | Context/scope-aware completion wired into the editor controller; broad parameter info (§8); keyword live templates with Tab-stops. | §23, §24 |
| 6 — Navigation + Semantic highlighting + Quick Info | Ctrl+hover/underline/cursor/tooltip, go-to-definition, Peek Definition, local find-references/rename; the semantic color layer (§9); the full Quick Info feature (engine, hover tooltip, completion detail pane). | §25, §26, §27 |

### 15.2 UX Polish Phase — opened after the Etap 6 review

After Etap 6 the user ran a practical review (EmberTern vs. IBExpert) and — while explicitly
endorsing the architecture — filed a UX polish backlog. **This is refinement, not new
architecture, and not Etap 7.**

**Done**: P1 (dot-completion resolves correctly at the end of a statement — the underlying fix,
`Scope`/`SemanticModel` offset lookups now inclusive at a span's end, is gotcha #198 and also
fixed several other end-of-text edge cases for free), P2 (completion list redesigned — per-kind
icon, rich column facts via a shared `Symbol`, lighter font), P3 (Ctrl+Space on a fully-typed,
resolved identifier now shows its Quick Info facts instead of an empty re-list), P4 (dragging the
completion list's own scrollbar no longer dismisses it — gotcha #199), P5 (semantic highlighting
is now consistent across all 12 statement kinds — the one gap was `CREATE TRIGGER`'s `FOR <table>`
target not being recorded as a schema-object reference), P6 (double-click in an INSERT/VALUES
list shows a popup naming which target column the clicked value maps to), P7 (PSQL live-template
snippets trigger correctly inside a bare `BEGIN…END` body and an ad-hoc `EXECUTE BLOCK`), P9 (a
conservative, contrast-computed theme pass fixed the two specifically-reported low-contrast
cases — the dark DML keyword color and the light built-in-function color).

**Deferred, not started (in priority order for whenever polish resumes):**
- **P8 — formatter polish.** The largest remaining item. `EXECUTE BLOCK` / `FOR SELECT` / `INTO`
  layout, and — the headline goal — real max-line-width wrapping for long `INSERT`/`VALUES`/
  `SELECT`/function-call lines (no more horizontal scrolling of hundreds of characters). Likely
  needs the parser deepened for INSERT/VALUES/SELECT-list clauses (deferred from Etap 3's
  "statement skeleton" depth on purpose — build grammar depth only when a concrete feature needs
  it). Its own large package.
  **§0 Krok 0 — Formatter Safety — DONE (2026-07-13).** The PSQL body emitter could silently DROP a
  token on malformed/incomplete input (a live §0 violation, found while scoping P8) — e.g. a
  stray/unmatched `END` hitting `SqlFormatter.EmitPsqlUnit`'s `if (IsWordTok(sig[i],"END")) return;`
  guard while called from a context with no enclosing `BEGIN` to consume it, whereupon the callers'
  anti-stall `if (i == before) i++;` advanced past the token without appending it. Fixed in two
  layers: **(a)** each stall guard now calls `EmitStrayToken` — emit the unplaced token verbatim and
  advance — so the emitter is lossless by construction (token-level analogue of `RawStatement`'s
  statement-level "reproduce 1:1" contract); **(b)** a **checked invariant** now wraps the formatter
  (`Format(SqlScript)`): after formatting each statement, its output is compared lexeme-for-lexeme to
  its input tokens and, on any mismatch, the statement is kept **verbatim** (leave the fragment
  unchanged); a script-level backstop returns the **input unchanged** if the whole result still
  differs by one lexeme (also covers the string-level long-line wrapping stage — refuse). A "lexeme"
  is a significant token (words case-insensitive since the formatter lowercases; everything else
  exact) plus every comment (trailing-trimmed); for well-formed input the sequences are always
  identical, so the guard never rejects valid code — it fires only on a genuine loss. Also fixed a
  related leading-comment drop (`FormatWithHeaderAndBody` lost a comment before `CREATE PROCEDURE`).
  §0 is now a guarantee, not a hope: the formatter either reproduces every lexeme or leaves the
  fragment/document unchanged, even for input it cannot model. Pinned by `SqlFormatterSafetyTests`
  (adversarial malformed corpus — lexeme-preservation + no-throw + idempotency), gotcha #212. Build
  0/0; full suite 3542 main + 23 probe green. The remaining P8 layout items (INSERT / UPDATE OR
  INSERT / EXECUTE BLOCK / FOR SELECT / long-line wrapping) are the cosmetic work now unblocked.

  **§F Shared list builder — DONE (2026-07-13).** One token-level mechanism replaces the per-kind
  comma-list emitters: `SplitTopLevelCommas` (nesting-aware, splits a token range at top-level commas)
  + `MatchParen` + `FormatBrokenList` (view: one item per line) / `FormatAdaptiveList` (inline or
  packed-to-width), with each item's CONTENT rendered by `Emit` — so spacing, lowercasing,
  function-call gluing, and nested parens are identical to every other emitted SQL, and there is no
  parallel item renderer. The break decision (usually width) stays with each caller. **Consolidation:**
  the CREATE VIEW column list (first consumer) was migrated onto it and its bespoke ~40-line character
  loop deleted; output stays byte-identical (all pinned view tests green). The token-level splitter is
  comma-safe inside quoted identifiers for free — the old string-level `SplitByTopLevelComma` needs an
  explicit quote-skip. INSERT / VALUES / UPDATE OR INSERT / EXECUTE BLOCK lists ride this builder in
  the following steps; the string-level long-line wrapping scanners (`SplitByTopLevelComma`,
  `FindInOpeningParen`, `FindMatchingClose`, `SkipString`, `SkipQuotedIdent`) become retirement
  candidates once the long-line-wrapping step moves to the token level. Pinned by
  `SqlFormatterListBuilderTests`. Build 0/0; full suite 3548 main + 23 probe green.

  **INSERT layout — DONE (2026-07-13).** `InsertStatement` formats IBExpert-standard (chosen by the
  user over an INTO-on-its-own-line variant — INSERT INTO stays one construct, like FOR SELECT /
  UPDATE OR INSERT / NEXT VALUE FOR): `insert into <target> (cols)` on one line, `values (…)` /
  `select …` / `default values` on its own line, `returning …` on its own, `;` glued. `FormatInsert`
  parses the skeleton by token scan (`FindInsertListOrSource` → target / column-list / source) and
  composes the shared `FormatAdaptiveList` for the two lists + `Emit` for the target and any
  INSERT…SELECT query — no bespoke list loop. The lists are **adaptive** (user directive): inline while
  they fit MaxLineWidth, else packed multiple-items-per-line aligned under the opening paren
  (readability-driven, not one-item-per-line). **Consolidation:** the adaptive-reflow packer
  `PackWithContinuation` gained a `startColumn` parameter and is now the ONE packing algorithm shared
  by the token-level list builder AND the string-level SELECT/IN wrapping — a single reflow, two entry
  points. Unrecognised INSERT shapes fall back to the generic emitter (the §0 net guarantees no loss).
  Pinned by `SqlFormatterInsertTests`. Build 0/0; full suite 3557 main + 23 probe green. **Open
  consolidation (flagged):** INSERT inside a PSQL body still routes through the PSQL emitter's
  per-statement `Emit`, not `FormatInsert` — unifying statement formatting across the top level and
  PSQL bodies is a larger change deferred to a later step.
- **P5d — a plain-hover info cue.** A dwell-delayed, info-only Quick Info tooltip on plain hover
  (no Ctrl held); the underline + hand-cursor affordance stays Ctrl-only per §9.4. Small and
  implementable, but it's a live-tuning UX addition (dwell delay, noise) the design defers to
  interactive judgment rather than shipping blind.
- **P2c — bold the typed fragment in each completion row.** Re-confirmed not cleanly doable on
  AvaloniaEdit 12.0.0: `CompletionList` exposes no per-item matched-range to a custom `Content`.
  Needs either a custom `CompletionListBox` item template or a controller-side re-render of every
  visible row on each filter keystroke (fragile). Do it only when it can be done without a hack.

### 15.3 Post-polish bug-fix sprint (2026-07-11) — status

A short stabilization sprint ran immediately after the UX Polish Phase review, fixing five
diagnosed issues in packages. **Etap 7 stays blocked until this sprint's items are fully closed.**

- **Packages 1–3 — done, verified** (completion-list initial filtering, a `VisualLinesInvalidException`
  crash on double-click, Semantic Model staleness after late metadata loads, a highlighting-delay
  fix, and a new-tab focus fix). Full detail + the resulting gotchas (#200, #201, #202) are in the
  history file.
- **Package 4 — edits complete, verification NOT confirmed.** A light-theme completion-popup
  background style and a dark-theme comment-contrast tweak were made, but the build/test/smoke
  verification pass was interrupted by a transient build-tool outage and was never re-run. **Next
  action: re-run build + test + smoke before treating this as done.**
- **Package 5 — DONE (2026-07-13, Quick Info richness).** `ColumnSpec` gained init-only rich fields
  (`DefaultValue`, `Description`, `IsPrimaryKey`, `IsForeignKey`, `ForeignKeyTable`, `IsComputed`,
  `IsIdentity`); `FirebirdMetadataReader.ColumnsSqlFor(serverMajor)` (replacing the old constant
  `ColumnsSql`) carries the PK/FK correlated subqueries + `RDB$DEFAULT_SOURCE` / `RDB$DESCRIPTION` /
  `RDB$COMPUTED_SOURCE`, with identity FB-version-gated (gotcha #146: `RDB$IDENTITY_TYPE` only on
  FB3+, a constant `0` on FB2.5). `ObjectMetadata` gained `ReturnType` (functions), `Trigger`
  (`TriggerDetail`: table/timing/events/position/active), and `Generator` (`GeneratorDetail`:
  start value + increment — **never** the dynamic current value). `QuickInfoEngine.ForSchemaObject`
  now renders column/PK/FK counts for tables, parameter in/out counts + return type for
  routines, and full trigger/generator header facts. Stage B/C wiring: a new proactive metadata-warm
  pipeline (`EditorLanguageService.BeginWarmReferencedMetadata` → `MainWindowViewModel.WarmReferencedAsync`
  → `LoadObjectDetailAsync`, cached in `_objectDetailCache`) fires after every model build and warms
  the rich detail for every object the current statement references — no user action (no "table.",
  no hover) required first — with `ModelFresh` now gating on a metadata GENERATION as well as text
  version so a background category load triggers a rebuild without a keystroke (see gotcha #211 for
  how this generalizes and supersedes the earlier per-character warm hacks). `SemanticBinder` also
  gained a flat catalog-reference pass (`BindGlobalCatalogReferences`) resolving `NEXT VALUE FOR
  <sequence>`, `GEN_ID(<sequence>, …)`, and bare function/procedure calls anywhere in a statement —
  these previously had no reference at all, so Quick Info/hover/colour never reached them. Covered by
  `ColumnMetadataFlowTests` (new), `MetadataReaderTests`, `QuickInfoEngineTests`, `SemanticModelTests`.
  Verified: `dotnet build` 0 warnings/errors, `dotnet test` 3449/3449 green.

### 15.4 Where the gotchas from this work live

Gotchas #189 through #202 (all introduced during the editor rebuild) are catalogued, with full
text, in **[`docs/gotchas.md`](../gotchas.md)** under "SQL lexing, parsing, formatting &
scanning" and "Never lose information / correctness-over-convenience". The ~6 most load-bearing
ones (the §0 round-trip guarantee, the end-of-span-inclusive lookup rule, the no-transitional-names
rule) are also in `CLAUDE.md`'s short "Live gotchas" list.
