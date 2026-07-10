# EmberTern — SQL/PSQL Editor Architecture & Modernization

> **STATUS: APPROVED (frozen 2026-07-09).**
> The user reviewed the analysis and accepted the direction with the modifications recorded
> in §14. This is now the **binding design** for the editor front-end. Per the
> staged-implementation contract: later etaps EXTEND this design and never silently change
> it — if an etap reveals a frozen decision must change, **STOP and consult the user before
> altering the doc or the code.**
>
> **Progress: Etap 0 (IntelliSense responsiveness) COMPLETE** (2026-07-09, §16),
> **Etap 1 (Lexer + `FirebirdSyntax` catalog + light-palette fix) COMPLETE** (2026-07-09,
> §18), **Etap 2 (Parser + AST) COMPLETE** (2026-07-10, §19) — error-tolerant
> statement-level recursive descent at the user-chosen "statement skeleton" depth (every §5.4
> statement is its own typed node; interiors kept verbatim), the `RawStatement` safety valve,
> the 3 consumers re-expressed as AST queries, and outlier **O5**
> (`FirebirdDdlExecutor.SplitStatements`) migrated onto the parser's boundaries behind a §0
> byte-identity corpus-diff gate — and **Etap 3 (Formatter, AST-based) COMPLETE** (2026-07-10,
> §20) — the AST-driven `SqlFormatter` (statement dispatch from the parse tree; interior
> layout reusing the proven, test-pinned logic on the single lexer's tokens; `RawStatement`
> verbatim per §0; deterministic + idempotent), retiring the old heuristic formatter (O1 dead)
> behind the full existing test suite + new §0/idempotency gates, plus the one small
> formatter-driven AST refinement (`AnonymousBlockStatement`). **The next session begins Etap 4
> (Semantic Model).** Item 13 (Quick Info / object documentation) is an APPROVED requirement
> (§8A) scheduled for Etaps 4–6.

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

### 5.6 Formatter — `Core.Sql.SqlFormatter` (Etap 3; see §20 for the as-built)
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

**Roadmap:** delivered across **Etap 4** (Semantic Model — resolve identifier → symbol +
metadata), **Etap 5** (reuse the Signature/metadata plumbing for the completion detail
pane), and **Etap 6** (the Ctrl-hover tooltip surface). It is NOT part of Etaps 0–3. §0
applies (read-only feature — it never modifies code, so it cannot lose information).

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
- **Etap 1 — Lexer. ✅ DONE (2026-07-09, §18).** One Firebird-aware lexer + the single
  `FirebirdSyntax` keyword catalog; folded outliers O2/O3/O4 (O1 deferred → Etap 3, O5 → Etap 2
  per audit §4); **regenerated the light-theme lexical palette for contrast**. **Delivered**:
  P8 (dedup) + a light-theme improvement.
- **Etap 2 — Parser + AST. ✅ DONE (2026-07-10, §19).** Error-tolerant recursive descent;
  grammar per §5.4 at the approved "statement skeleton" depth (§19); `RawStatement` safety valve.
  **Delivered**: a cached tree (`SqlScript`); `SqlStatementClassifier` + `SqlParameterScanner`
  re-expressed as AST queries; outlier O5 (`FirebirdDdlExecutor.SplitStatements`) migrated onto
  the parser's boundaries behind a §0 corpus-diff gate.
- **Etap 3 — Formatter (AST-based). ✅ DONE (2026-07-10, §20).** Retired the old heuristic
  `SqlFormatter` → rewrote it AST-based **under the same name** (transitional `SqlFormatterV2`
  consolidated away; old impl deleted); IBExpert-inspired default;
  deterministic/idempotent; parity gate = all existing formatter tests (byte-for-byte) + new
  corpus/idempotency/§0-token-and-comment-preservation tests; outlier O1 (`SqlFormatter.Tokenize`)
  dead. **Delivered**: P1.
- **Etap 4 — Semantic Model.** Bind AST ↔ metadata + local scope; nested scopes. **Delivers**:
  the foundation for completion/navigation/diagnostics/semantic-color, and for Quick Info (§8A —
  resolve identifier → symbol + metadata).
- **Etap 5 — Completion Engine + Snippet Engine + Signature Help.** Context/scope-aware
  completion; smart completion + live templates; broad Parameter Info (§8); the completion-list
  **Quick Info detail pane** (§8A) reusing the signature/metadata plumbing. **Delivers**: P3 + P4
  (+ P9 completion-pane half).
- **Etap 6 — Navigation + Semantic highlighting.** Ctrl+hover/underline/cursor/tooltip (the tooltip
  carries the **Quick Info** model, §8A), go-to-def, peek, local find-refs/rename; the semantic
  color layer (§9) + `EditorPalette`. **Delivers**: P5 (semantic) + P6 + P9 (hover half).
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

## 15. Etap 0 — scope (COMPLETE — shipped 2026-07-09; see §16 for the record)

> **STATUS: DONE.** This section is the original ready-to-start scope, kept for the record;
> the as-built result is in §16.

Etap 0 was **App-layer only, no AST, no Core grammar changes, no behavior change to results** —
purely making IntelliSense responsive.

**Goal:** eliminate the typing lag and the over-eager popup; Ctrl+Space instant.

**Concrete work:**
1. Add **`EditorLanguageService`** (App, per-editor, created in `SqlEditorBehavior.Attach`) that
   owns a debounce timer (≈300 ms idle) + a `CancellationTokenSource` and runs the
   heavy work (currently `SqlAliasResolver.ParseAliases`) **off the keystroke, in the background,
   cancellable**, caching the latest result. Marshal back to the UI thread.
2. Rework **`SqlCompletionController.OnTextEntered`** so it does **no whole-document work**:
   - decide whether to open the window from the few chars before the caret (via the AvaloniaEdit
     `Document`, not `_editor.Text`);
   - pull alias/dot context from the **cached** `EditorLanguageService` result, not a fresh
     `ParseAliases(wholeDocument)`;
   - keep dot (`.`) and Ctrl+Space as immediate triggers; make identifier auto-trigger idle-based
     and non-aggressive (reasonable default; not eager at 3 chars mid-burst).
3. **Ctrl+Space** always shows completion immediately from cached state (re-scan just the current
   line if the cache is stale).
4. Reasonable non-aggressive defaults for auto-popup delay (a settings-backed value + full-disable
   is deferred to the app configurator — leave a clearly-named constant/field so it's trivial to
   wire later).
5. **No AST yet** — Etap 0 reuses `SqlAliasResolver` / `SqlCompletionContext` as-is, just moved
   behind the async service. Do NOT start the Lexer/Parser here.

**Acceptance:** typing in a large procedure body is smooth (no per-keystroke whole-document
tokenize); popup is not eager; Ctrl+Space instant; existing completion behavior otherwise
unchanged; build 0/0; smoke clean. **§0 holds trivially** (Etap 0 doesn't generate/modify code).

**Out of scope for Etap 0:** the Lexer, Parser, AST, formatter, semantic model, highlighting,
navigation — all later etaps.

---

## 16. Etap 0 — completion record (shipped 2026-07-09)

**Result:** build 0/0 · tests 2823 (main) + 10 (headless probe) green · 8 s smoke clean (no
`FATAL`, no early exit). App-layer only; no AST, no Core grammar; §0 holds trivially (nothing
generates/modifies code). The typing lag and over-eager popup are gone; Ctrl+Space is instant.

**Delivered (files):**
- **`EditorLanguageService`** (new, `App/Completion`) — per-editor; subscribes to `TextChanged`;
  debounces `ParseDebounce` = 300 ms; runs `SqlAliasResolver.ParseAliases` off-thread (`Task.Run`
  + `CancellationTokenSource`, superseded results dropped) and resumes on the UI thread
  (`ConfigureAwait(true)`); caches the alias map with a monotonic change-version.
  `EnsureFreshAliases()` = synchronous fallback used **only** on deliberate triggers.
- **`CaretContext`** (new, `App/Completion`) — bounded backward scan over the AvaloniaEdit
  `ITextSource` for the current word / dot context, returning **document-absolute** offsets.
  Removes whole-`_editor.Text` materialization from the keystroke path (§15.5). Mirrors the Core
  helpers exactly (reuses `SqlCompletionContext.IsIdentifierChar`); an **equivalence test**
  (`CaretContextTests`, via `StringTextSource`) pins that the Document scan == the Core string
  scan so the move can't drift.
- **`SqlCompletionController`** (reworked) — `OnTextEntered` does no document-wide work; a typed
  `.` and Ctrl+Space stay **immediate**; the identifier auto-popup is **idle-debounced**
  (`AutoPopupDelay` = 250 ms, a clearly-named settable property — the wire-in point for the future
  configurable delay/disable, §7.4); dot qualifiers resolve against the cached alias map + a
  `knownTablesProvider`; Ctrl+Space always yields a non-empty list from cache (never a dead empty
  list). `Detach` disposes the service + timer.
- **Call sites** — `SqlEditorBehavior.Attach` / `MainWindow` now pass
  `knownTablesProvider: EnumerateTableLikeNames` (was `dotTableResolver`); dead
  `MainWindowViewModel.ResolveDotTable` + `MainWindow.ResolveDotTable` removed.
- **Core (one small, approved reuse-refactor — NOT a rewrite, changes no §14 decision):** added
  the pure `SqlAliasResolver.ResolveTableForQualifier(IReadOnlyDictionary<string,string> aliases,
  qualifier, knownTables)` overload and had the existing `(sql,…)` overload **delegate** to it, so
  the cached path reuses the exact tested resolution instead of duplicating it in App. Existing
  tests stay green; +5 new tests.

**Consciously deferred (NOT Etap 0):** keyword-list unification → **Etap 1** (Lexer); Item 13
rich Quick Info → **Etaps 4–6** (§8A) — the *responsiveness* half of Item 13 (Ctrl+Space always
works, never a dead empty list) shipped now, the *documentation* half needs the Semantic Model.

**Gotcha (per keystroke, avoid whole-`Text`):** never read `_editor.Text` on `TextEntered` — it
materializes the whole document every keystroke. Use `CaretContext` over `_editor.Document`
(bounded backward scan, absolute offsets) and move any whole-document analysis into the
debounced `EditorLanguageService`.

---

## 17. Etap 1 — scope (COMPLETE — shipped 2026-07-09; see §18 for the record)

> **STATUS: DONE.** This section is the original ready-to-start scope, kept for the record;
> the as-built result is in §18.

> **Read first:** [etap1-tokenization-audit.md](etap1-tokenization-audit.md) — the code-grounded
> audit of every current tokenizer / scanner / keyword list / highlighting asset, the dependency
> map, and the REPLACE/KEEP/ADAPTER/DEFER disposition per component. Its §4 scanner-scope
> decision is **APPROVED (2026-07-09)**: Etap 1 folds outliers **O2/O3/O4** + creates the lexer +
> `FirebirdSyntax` + unifies the keyword catalog + fixes the light palette; **O1
> `SqlFormatter.Tokenize` is deferred to Etap 3** (dies with the AST formatter rewrite) and **O5
> `FirebirdDdlExecutor.SplitStatements` to Etap 2** (needs the parser's statement boundaries). Its
> §6 is the concrete execution order.

Start here without re-analyzing. Etap 1 introduces the **first piece of the real front-end: one
Firebird-aware Lexer + the single `FirebirdSyntax` keyword catalog** — and takes an early,
standalone **light-theme lexical-palette** win. **Still no parser/AST** (that is Etap 2).

**Goal:** kill the duplication (P8) — 4 outlier scanners + 3 keyword lists collapse to one lexer
+ one catalog — and fix the muddy light-theme lexical colors (§9.1/§9.5), all without changing
any feature's behavior yet.

**Concrete work (per §5.1, §4.2#6, §9.5):**
1. `Core.Sql.Language.SqlLexer` — text → immutable token stream (`SqlToken`: one `TokenKind` enum
   for the whole app, span + attached trivia). Firebird-aware: `''`-escaped strings, `"…"` quoted
   identifiers, `--` and `/* */` comments, `:name`/`?`/`@name` params, operators, numbers, dialect
   quirks. `IReadOnlyList<SqlToken> Tokenize(string)` (incremental variant later). Fold in the
   `SqlScanHelpers` primitives.
2. `Core.Sql.Language.FirebirdSyntax` — the single keyword catalog (keywords by category, types,
   built-in functions) that drives the lexer, completion, and the highlighting palette. **One
   source of truth** — `SqlKeywords.All`, `SqlFormatter`'s hashsets, and the two XSHD keyword
   lists become derived from it (or are replaced), not hand-maintained parallel lists.
3. Migrate the four outlier scanners onto the lexer **progressively** (`SqlFormatter.Tokenize`,
   `SqlAliasResolver.Tokenize`, `SqlStatementClassifier`'s private scanner, `TraceSqlInliner`) —
   proving the lexer covers their needs. Keep old code until each consumer is verified, then retire
   it (coexistence, R5). **§0:** any consumer that reproduces text (formatter, inliner) must remain
   byte-for-byte identical — old-vs-new corpus diff before switching.
4. **Light-theme lexical palette** — regenerate/clean the light-theme XSHD colors for contrast
   (adjacent roles differ in hue AND lightness; WCAG-AA-ish), an early light-theme improvement
   before the semantic layer (Etap 6) exists. Dark theme unchanged unless a role is clearly muddy.

**Acceptance:** the lexer tokenizes the Lab DB + real ERP SQL corpus correctly; each migrated
consumer keeps identical behavior (pinned by its existing tests + a corpus diff); the light theme
is visibly clearer with distinguishable roles; build 0/0; smoke clean. **§0 holds** (no code
generation/modification changes; the formatter/inliner outputs are diffed for byte-identity).

**Out of scope for Etap 1:** the Parser, AST, semantic model, formatter rewrite, completion
engine, navigation, Quick Info — all later etaps.

---

## 18. Etap 1 — completion record (shipped 2026-07-09)

**Result:** build 0/0 · tests 2902 (main) + 10 (headless probe) green (+79 new) · smoke clean
(app alive 9 s, exit 0, no `FATAL`). Core-layer only (one App asset touched: the light XSHD
palette). **§0 holds** — the two highest-risk text reproducers (O1 `SqlFormatter`, O5
`FirebirdDdlExecutor.SplitStatements`) were left untouched (deferred to Etaps 3/2 per audit §4);
the one text reproducer migrated (O4 `TraceSqlInliner`) reconstructs by copying source spans
between the `?` markers, so its passthrough is byte-identical by construction. The three
divergent keyword lists collapsed to one source of truth; the muddy light theme is de-clustered.

**Delivered (new — `EmberTern.Core.Sql.Language`):**
- **`FirebirdSyntax`** — the single keyword catalog. Four highlight-category arrays
  (Dml/Statement/DataType/Function) transcribed 1:1 from the XSHD blocks + the completion
  vocabulary; a static ctor unions them into a case-insensitive catalog, asserting the four
  highlight categories are disjoint. Drives the lexer (`IsKeyword`), completion
  (`CompletionKeywords`), and highlighting (`KeywordsInCategory`).
- **`SqlLexer`** + **`SqlToken`** / **`TokenKind`** (one enum for the whole app) / **`SqlTrivia`**
  — the Firebird-aware lexer: `''`/`""` escapes, `--` + `/* */` comments as attached leading
  trivia, `?`/`:name`/`@name` parameters, `$` identifiers, hex/exponent numbers, multi-char
  operators. **Lossless** (round-trip pinned): concatenating each token's leading-trivia text +
  `Text` reproduces the source byte-for-byte — the parser/formatter foundation. Keyword vs
  identifier is decided by the `FirebirdSyntax` catalog.

**Migrated (internals re-pointed onto the lexer; public APIs + behaviour unchanged):**
- **O2 `SqlAliasResolver`** — its private tokenizer now projects the lexer stream onto the
  resolver's Word/Comma/Dot/LParen/RParen/Other shape; the proven `ParseAliases` walk is
  untouched. (All 24 resolver tests green.)
- **O3 `SqlStatementClassifier`** — reads the leading word from the lexer; deleted its byte-copy
  of `SqlScanHelpers.SkipTrivia`. (All classifier tests green.)
- **O4 `TraceSqlInliner`** — §0-critical; rebuilds by copying source spans between the positional
  `?` tokens (named `:name`/`@name` never substituted). Byte-identical passthrough. (+3 defensive
  tests.)
- **K1 `SqlKeywords.All`** — now a thin ADAPTER over `FirebirdSyntax.CompletionKeywords` (the
  exact historical set preserved; completion behaviour unchanged).

**Light-theme lexical palette (§9.5):** the three "cool" roles that were near-identical
green/teal mid-tones (Comment/Number/DataType) are re-spaced across a green→teal→blue gradient
(differ in hue AND lightness), and the harsh pure-blue DML keyword is softened. Only the 8
`<Color>` values in `FirebirdSql.Light.xshd` changed — the keyword blocks are unchanged and are
now **pinned against `FirebirdSyntax`** by a drift-guard test (reads both XSHD files, asserts
each `<Keywords>` block equals the matching catalog category and that light == dark). Dark theme
unchanged.

**Deferred (per audit §4, unchanged):** O1 `SqlFormatter.Tokenize` + its keyword hashsets → Etap 3
(dies with the AST formatter); O5 `FirebirdDdlExecutor.SplitStatements` → Etap 2 (needs the
parser's real statement boundaries). `SqlScanHelpers` KEPT as-is (its many Core consumers ride
it); it folds into the lexer opportunistically later.

**Tests:** `SqlLexerTests` (round-trip corpus + span-contiguity + every lexical shape),
`FirebirdSyntaxTests` (catalog invariants + the XSHD drift guard), + the migrated consumers'
existing suites (all green, proving behaviour preservation).

---

## 19. Etap 2 — completion record (shipped 2026-07-10)

**Result:** build 0/0 · tests 3058 (main) + 10 (headless probe) green · 9 s smoke clean (app alive,
no `FATAL`). Core-layer only (plus the `FirebirdDdlExecutor` O5 delegator). **§0 holds** — the
round-trip is machine-checked, and O5 was migrated only after a differential corpus diff proved it
byte-for-byte identical to the legacy splitter.

**Approved depth (user decision 2026-07-10): "statement skeleton".** The user chose a complete,
stable AST *foundation* over a partial deep parser: every §5.4 statement is its own typed node from
the start, but a statement's interior is kept verbatim (in `SqlStatement.Tokens`) where deeper
analysis isn't needed yet. Later etaps deepen individual nodes (clauses, expressions, PSQL bodies)
without rebuilding the foundation — the natural growth path for Etap 3 (Formatter) / Etap 4
(Semantic Model) / onward. This best fits Never-Lose-Information and minimises regression risk.

**Delivered (new — `EmberTern.Core.Sql.Language.Ast` + `…Language`):**
- **`SqlNode`** (abstract base) — absolute span + ordered `Children` + `NodeAt(offset)` (deepest
  containing node) + `Descendants<T>()`. Immutable; holds no source string.
- **`SqlScript`** (root) — the ordered `Statements` + the complete lossless token stream + the
  original `Text`. **`ToSourceString()` reconstructs the input byte-for-byte** — the §0 round-trip
  invariant, guaranteed by the token stream and therefore **independent of grammar depth** (a
  `RawStatement` or a shallowly-modelled statement round-trips identically). This decoupling is the
  load-bearing design idea: the tree is a structural *overlay* on a lossless token stream.
- **`SqlStatement`** (abstract) + **`StatementKind`** + **17 concrete statement node types** — one
  per §5.4 kind: `SelectStatement`, `InsertStatement`, `UpdateStatement`, `UpdateOrInsertStatement`,
  `DeleteStatement`, `MergeStatement`, `ExecuteBlockStatement`, `ExecuteProcedureStatement`
  (→ `ProcedureName`), `ExecuteStatementStatement`, `DdlStatement` (→ `Verb`, `ObjectKind`,
  `ObjectName`, `IsPsqlDefinition`), `CommentStatement`, `SetStatement` (→ `Target`),
  `GrantStatement`, `RevokeStatement`, `DeclareStatement`, `EmptyStatement`, and the `RawStatement`
  safety valve. Each holds its significant `Tokens` (incl. a trailing `;` when consumed).
- **`SqlParser`** — error-tolerant recursive descent. Never throws, never returns null; every byte
  lands in exactly one statement, and an unrecognised statement becomes a `RawStatement` (not an
  error). It owns the one **statement-boundary authority**: the segmentation mirrors the
  long-standing PSQL-aware splitter exactly (plain → next top-level `;`; a `CREATE/ALTER/RECREATE`
  of a `PROCEDURE/TRIGGER/FUNCTION/PACKAGE` kept whole through the `END` closing its outermost
  `BEGIN`; CASE-aware; string/comment-safe because those are already opaque tokens/trivia).
- **`Diagnostic` / `DiagnosticSeverity` / `ParseResult`** — the recovery-diagnostics channel
  (infrastructure). Deliberately empty at statement-segmentation depth (there are no "recoverable
  errors" — unknown ⇒ `RawStatement`, the §0 valve); real diagnostics arrive with clause/PSQL
  parsing (later etaps) and user-facing squiggles are Etap 7.

**Consumers re-expressed as AST queries (the "proof the tree works"):**
- **`SqlStatementClassifier.Classify`** now parses and maps the first statement's node type to a
  lane (Data / Metadata / Ambiguous). Behaviour identical (its pinned tests pass unchanged).
- **`SqlParameterScanner`** — `IsExecuteBlock` / `TryExtractExecuteProcedureName` read the first
  statement node; `Scan` is now a filter over the lexer's `Parameter` tokens (byte-identical
  because `SqlScanHelpers.IsIdentifierChar` == the lexer's identifier-part set). `RewriteToDriverMarkers`
  is unchanged (rides `Scan`). Pinned tests pass unchanged.

**O5 migrated (audit §4/§5, §0-critical):** the DDL splitter now rides the parser. New Core
**`SqlStatementSplitter.Split(sql)`** = `SqlParser.Parse(sql).Root.Statements` sliced by span +
the exact legacy post-processing (trim, strip one trailing `;`, drop empties);
**`FirebirdDdlExecutor.SplitStatements`** is a one-line delegator and its ~200 lines of char
scanners are deleted. The switch was gated on **`SqlStatementSplitterDiffTests`** — a permanent
differential test that runs a ~45-case corpus (the long-standing pinned splitter cases + generated
DDL shapes + pathological/unterminated inputs) through both an **inlined copy of the legacy
algorithm** and the parser-backed splitter and asserts they are byte-identical.

**Tests:** `SqlParserTests` (round-trip §0 invariant + never-throws over a corpus, classification
into the taxonomy, multi-statement segmentation, PSQL-kept-whole, DDL/Execute/Set fact extraction,
`RawStatement`/`EmptyStatement`, `NodeAt`/`Descendants`), `SqlStatementSplitterDiffTests` (the §0
byte-identity gate), + the migrated consumers' existing suites (all green, proving behaviour
preservation).

**Deferred by design (the growth path, not gaps):** deeper per-node grammar — DML clauses
(WITH/SELECT-list/FROM/JOIN/WHERE/…), PSQL body blocks (BEGIN/END, IF/WHILE/FOR/CASE/DECLARE/…),
and expressions — is added in later etaps, driven by what the formatter (Etap 3) and Semantic Model
(Etap 4) need; statement nodes are leaves for now and gain `Children` then. `EXECUTE BLOCK` with a
DECLARE section before its `BEGIN` follows the legacy boundary (it is not a "PSQL definition" for
segmentation, so its pre-`BEGIN` `;` splits it) — a documented, never-triggered-in-O5 edge kept for
§0 byte-identity; a later etap that models EXECUTE BLOCK deeply will re-validate O5.

---

## 20. Etap 3 — completion record (shipped 2026-07-10)

**Result:** build 0/0 · tests 3213 (main) + 10 (headless probe) green · 9 s smoke clean (app alive,
no `FATAL`). Core-layer only (Core.Sql + Core.Sql.Language + the two test suites). **§0 holds** — the
existing formatter suite passes **byte-for-byte** (the old expected outputs = the old-vs-new parity
proof for every tested case), and new machine-checked tests prove no significant token or comment is
ever lost/added/reordered/mangled over a broad corpus.

**Approved approach (Variant A, user decision 2026-07-10):** AST *statement dispatch* + token-level
interior, reusing the proven test-pinned layout logic. The user explicitly chose NOT to deepen the
parser in Etap 3 (no clause/PSQL-body child nodes) — that stays the natural growth path for later
etaps. What the AST buys now: the statement-level decisions are 100% parse-tree-driven (the old
`IsPsql` / `FindBodyStart` heuristics and the separate keyword-classification tokenizer are gone).

**Delivered:**
- **`EmberTern.Core.Sql.SqlFormatter`** — the single AST-based formatter (the old heuristic
  implementation was deleted and this rewrite carries the plain name — no facade, no `V2`; it was
  landed transiently as `SqlFormatterV2` during the migration and consolidated once the old class was
  gone, per the naming policy in §14 decision #15). `Format(string)` /
  `Format(SqlScript)`. Walks `SqlScript.Statements` and dispatches by node kind:
  `RawStatement`/`EmptyStatement` → **verbatim** (source span + any leading comments — §0);
  `DdlStatement{IsPsqlDefinition}` + `ExecuteBlockStatement` → header verbatim through the body's
  top-level `AS`, body block-structured; `AnonymousBlockStatement` → PSQL body (the procedure-body
  editor's bare `BEGIN…END`); everything else (all DML + non-PSQL DDL + COMMENT/SET/GRANT/REVOKE/
  DECLARE/EXECUTE PROCEDURE/STATEMENT) → the clause-break SQL emitter (which still handles the
  CREATE VIEW header + long-line wrapping internally). Trailing comments (on EOF) are appended so
  nothing is lost. Deterministic + idempotent (indent from structure, breaks from clause keywords +
  `;`, never from input whitespace).
- **Interior reuse over the single lexer.** The proven emit + PSQL-block algorithms (clause/JOIN/ON/
  AND-OR breaks, view header, SELECT-column / IN-list wrapping, CASE-safe BEGIN/END structuring,
  blank-line preservation, `SELECT … INTO` split, gotcha-#152 package bodies) were ported onto a
  small flat "format token" stream produced from `SqlLexer` tokens — comments come from the lexer's
  **leading trivia** (never a separate comment tokenizer), whitespace is dropped, a `BlankBefore`
  flag carries author blank lines for PSQL. This is the **O1 death**: `SqlFormatter.Tokenize` and its
  keyword hashsets are gone; the formatter rides the one `SqlLexer`.
- **Stable public API.** `SqlFormatter.Format(string)` is unchanged, so the ~6 App call sites (editor
  Alt+F, EditorSearch, the object detail views, the trace SQL preview) and the regression suite need
  no edits. The old flat-token/heuristic algorithm is deleted; there is exactly one formatter class.
- **One small, formatter-driven AST refinement (sanctioned):** `AnonymousBlockStatement` +
  `StatementKind.AnonymousBlock`. The parser now classifies a bare `BEGIN…END` (and a `DECLARE`-led
  body that contains a `BEGIN`) as an anonymous block instead of a `RawStatement`, so formatting a
  procedure/function/trigger **body editor** (gotcha #114 — the stored body has no CREATE header)
  produces a structured block rather than a verbatim fallback. **Segmentation/spans are unchanged**,
  so the O5 splitter (which slices by span, not kind) is unaffected — pinned by the still-green
  `SqlStatementSplitterDiffTests`; `SqlStatementClassifier`/`SqlParameterScanner` have safe defaults
  for the new kind.

**Style policy vs lexical catalog.** The formatter keeps its own small *style* sets (which keyword
breaks a line / keeps a space before `(`) — that is layout policy, legitimately the formatter's, and
kept identical to the shipped style so output is byte-for-byte unchanged. The *lexical* "what is a
keyword" question is the single `FirebirdSyntax` catalog's job (via the lexer's token kinds). So O1's
K2 hashsets are not "moved into FirebirdSyntax"; the lexical part died (the lexer classifies), the
layout part stayed as style. Note that a couple of style words (`OPEN`/`CLOSE`) aren't FirebirdSyntax
keywords and lex as identifiers — the keep-space rule is independent of the lexer's classification, so
this is correct.

**One deliberate behavior change (§0-correct):** an **unrecognised fragment** (e.g. a bare
`a , b , c` comma-list — not a statement) is now a `RawStatement` and is emitted **verbatim**, where
the old flat-token formatter re-spaced it. This is the Paramount Law in action (never reshape SQL we
can't classify). Recognised statements still format fully. Exactly one existing test changed
(`SqlFormatterTests.NoStructuralKeywords_StaysOnOneLine` → `UnrecognisedFragment_IsPreservedVerbatim`).

**Tests:** existing `SqlFormatterTests` + `PsqlFormatterTests` kept green byte-for-byte (the parity
gate); new **`SqlFormatterInvariantsTests`** — a broad corpus (every statement kind + comments + incomplete +
erroneous + unusual + multi-statement) driving **idempotency** (`Format(Format(x)) == Format(x)`),
**§0 token-preservation** (identical normalised significant-token sequence in↔out), **§0
comment-preservation** (every comment survives in order), **never-throws**, plus statement-kind spot
assertions, the RawStatement/anonymous-block/trailing-comment cases, and the facade delegation.
`SqlStatementSplitterDiffTests` (the O5 §0 byte-identity gate) unchanged and green.

**Known limitations / deferred (the growth path, not gaps):** interior layout is still token-level
(no deeper clause/PSQL-body AST nodes yet) — so, e.g., `UPDATE … SET …` does not break `SET` onto its
own line, and long `INSERT … VALUES` isn't value-wrapped. Those are deeper-clause improvements for
later etaps (Etap 4+ deepen nodes "driven by what the formatter needs", §19). A configurable style
profile (`FormatOptions`) is deferred to the future application configurator (§6) — Etap 3 ships the
single opinionated default as constants. The pain cases named in §5.6 that ARE improved now: EXECUTE
BLOCK (header kept + body block-structured), bare/DECLARE-led PSQL bodies (structured, not verbatim),
IF/JOIN/WHERE-AND-OR (unchanged good behavior, now dispatched from the AST).

---

*End of frozen design. Etaps 0 (§16), 1 (§18), 2 (§19) and 3 (§20) complete; next session: implement
Etap 4 (Semantic Model) per §12 — bind the AST to the connection's metadata cache + local scope
(aliases/PSQL vars/params/NEW/OLD/cursors, nested scopes), the foundation for completion / navigation
/ diagnostics / semantic highlighting and for Quick Info (§8A).*
