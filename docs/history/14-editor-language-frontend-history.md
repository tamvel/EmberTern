# History — Editor Language Front-End Rebuild: Etap-by-Etap As-Built Record

> Archived from `docs/design/editor-architecture.md` during the Documentation Cleanup
> Sprint (2026-07-11). Verbatim extract of the design doc's former §15–§29
> ("completion record" / "as-built" sections for each etap and polish package).
> The CURRENT architecture, component specs, and binding decisions live in
> `docs/design/editor-architecture.md` itself — this file is the narrative of
> HOW each etap was actually built, session by session, kept for full context.
> A concise, still-actionable summary of what was open at the time of this split
> (Package 4 verification pending, Package 5 deferred with a plan, the P8/P5d/P2c
> backlog) has been carried forward — in fresh, current wording — into the
> rewritten `editor-architecture.md`; this archive is the full verbatim record.

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

> **Read first:** [etap1-tokenization-audit.md](../design/etap1-tokenization-audit.md) — the code-grounded
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

## 21. Etap 4 — completion record (shipped 2026-07-10)

Fourth etap — the **Semantic Model**: binds the error-tolerant AST (Etap 2) to *meaning* — a tree
of lexical **scopes**, the **symbols** declared in them, and every identifier occurrence resolved
(or not) to a symbol. Core-layer only (new namespace `EmberTern.Core.Sql.Language.Semantics`); **no
App wiring yet** (that is Etap 5). Build 0/0, **3247 main + 10 probe tests green** (+35 new,
`SemanticModelTests`), 9 s smoke clean. Delivers the foundation §5.5 promised — completion /
navigation / diagnostics / semantic-color / Quick Info all query this one model.

**Public API (`SemanticModel`, offset-driven — the surface consumers use):** `Build(sql|SqlScript,
metadata?)`, `ScopeAt(offset)`, `ReferenceAt(offset)` (shortest containing span wins),
`ResolveAt(offset)`, `SymbolsInScope(offset)`, `ReferencesTo(symbol)` (the local find-references /
rename basis), plus `RootScope` / `AllSymbols` / `References`. Read-only ⇒ §0 holds by construction;
**error-tolerant** (never throws — a pathological statement contributes no symbols, the model still
binds the rest); **metadata-optional** (with `EmptyMetadataProvider` every local scope still binds
and references still record — only schema symbols stay unresolved).

**Model types (pure, LSP-ready):** the universal `Symbol` (rich-but-optional facts) + subclasses
`SchemaObjectSymbol` / `ColumnSymbol` / `TableReferenceSymbol` (the alias→table binding at the heart
of column resolution) / `VariableSymbol` / `ParameterSymbol` / `CteSymbol` / `CursorSymbol` /
`RecordAliasSymbol`; the flat `SymbolKind`; `Scope` (nested, resolves outward — shadowing/collisions
fall out of the tree) + `ScopeKind` (Script/Query/Dml/RoutineBody/Block); `SymbolReference` +
`ReferenceRole`; `TextSpan`. Metadata is abstracted behind **`ISqlMetadataProvider`** (synchronous +
pure — Core never touches the driver; the App implements it over its caches in Etap 5) with
rich-but-optional DTOs (`ObjectMetadata` / `ColumnMetadata` / `RoutineParameterMetadata`) +
`EmptyMetadataProvider`.

**The binder (`SemanticBinder`, internal, partial):** walks each statement's *token stream* (Etap 2
keeps interiors as tokens; when the parser deepens, the binder swaps to the deeper tree with **no
public-API change**). Split across `SemanticBinder.Query` / `.Dml` / `.Psql`. It builds the scope
tree, declares symbols, records references, and caches object/column symbols so repeated uses share
one symbol (so `ReferencesTo` groups them).

**Headline — the two-phase Query binder (the fix this session completed).** The interrupted single
left-to-right pass bound `k.nazwa` **before** it had seen the alias `k` (declared in the later
FROM), so the qualifier never resolved (`select k.nazwa from kontrahent k`). Fixed by splitting
`BindQuery` into **phase 1 `CollectTables`** — scan the query body, parse every top-level FROM/JOIN
table list into the scope (declaring a `TableReferenceSymbol` per entry + binding derived-table
subqueries), return the consumed token-index ranges, and do NOT descend into non-FROM subqueries —
then **phase 2 `BindColumnReferences`** — scan again with every table already in scope, skip the
phase-1 ranges, recurse into non-FROM subqueries as correlated child scopes, and resolve qualified +
bare column references. So the SELECT list, textually before FROM, resolves its columns. Bare
columns resolve only when exactly one in-scope table owns the name (else recorded
unresolved/ambiguous) — high-precision, metadata-gated. **DML was unified onto the same two-phase
walk** (pre-declare target(s) → `CollectTables` + `BindColumnReferences`), so INSERT … SELECT /
UPDATE / MERGE column refs also see every table; the old single-pass `BindQueryBody` is deleted.

**Also fixed — the nullable-token misuse.** `SqlToken` is a **record class** with a `Value` string
property, so the interrupted code's `alias!.Value` read that *property* (a string) where it meant
"the token itself" (`SqlToken?` is a nullable *reference*, not `Nullable<SqlToken>`). Corrected to
`alias!` / narrowed `aliasTok`. See gotcha #196.

**Coverage (`SemanticModelTests`, +35):** the two-phase fix (qualifier-before-FROM resolves with AND
without metadata; the reported bug is the first test), FROM/JOIN table refs + alias-vs-name +
schema-object target resolution, bare-column resolution (unique / ambiguous / no-metadata), CTEs,
correlated-subquery + derived-table child scopes, DML (UPDATE / DELETE / INSERT…SELECT targets),
PSQL (procedure params + variables + cursor + `:param` refs, trigger NEW/OLD → table columns,
EXECUTE BLOCK, EXECUTE PROCEDURE, CREATE VIEW), the public API (`ReferencesTo` groups an alias's
occurrences, `ScopeAt` depth + parent), error-tolerance (10 garbage/incomplete inputs never throw),
and metadata-optional operation. Pure Core — no window, no DB (a fluent fake `ISqlMetadataProvider`).

**Known limitations / deferred (documented, by design — the growth path, not gaps):**
- The binder walks the **token stream**, not a deep AST (Etap 2 "statement skeleton"). Consequences
  kept deliberately simple: bare-column resolution is best-effort (needs metadata + unambiguity); a
  `pkg.fn(...)`-style dotted call whose qualifier is not a known table/record alias is left
  unrecorded (no false column ref); nested `BEGIN…END` blocks add no separate scope (Firebird has no
  block-local variables); LATERAL/correlated derived tables aren't modelled (standard derived tables
  aren't correlated). All improve when the parser deepens — no public-shape change.
- **No App wiring** — `ISqlMetadataProvider` is implemented over the App's metadata caches in Etap 5,
  where completion / navigation / Quick Info consume the model; live end-to-end resolution against a
  real connection is verified there. Etap 4 is pinned by pure Core unit tests.
- Quick Info / semantic highlighting / go-to-def / find-refs / rename / diagnostics are the *clients*
  of this model (Etaps 5–7), not Etap 4.

---

## 22. Etap 5 — plan (ready-to-start milestone breakdown; NOT yet implemented)

> **STATUS: PLANNED (2026-07-10).** Etap 5 = **Completion Engine + Snippet Engine + Signature
> Help + the `ISqlMetadataProvider` App adapter + App integration**. It is materially larger than
> Etaps 0–4, so it is split into small, independent, individually-shippable milestones (**M1…M9**)
> so a session (or a different account) can stop after any milestone with the project **building,
> all tests green, smoke clean, and no half-implemented state**. Resumability outranks speed. This
> section is written to be executed cold — start at M1 (or M2, which is independent) without
> re-analysing. **If a milestone reveals a frozen decision must change, STOP and consult the user
> (decision #13).**

### 22.0 The binding constraint (do not violate)
Completion, Signature Help, and Snippets consume **only** the Lexer → Parser → AST → Semantic
Model. **No new heuristics, no re-tokenizing / re-scanning the raw text.** "Context detection"
(am I after `FROM`? in a dot qualifier? in a PSQL body?) is answered by reading the **already-produced
`SqlScript` token stream + AST nodes + `SemanticModel` scopes/symbols/references** — never a fresh
character scan. If a needed fact is not in the Semantic Model, do **not** reconstruct it with a side
mechanism: either it is derivable from the token stream/AST/model, or it is a scoped, explicitly-flagged
parser/binder deepening (its own micro-milestone), not a heuristic. The engines live in Core
(`EmberTern.Core.Sql.Language.*`), are pure and offline-unit-tested; the App layer is thin glue.

### 22.1 Load-bearing design decision — metadata **snapshot**, model built off-thread
`ISqlMetadataProvider` is documented as a **snapshot** (§5.5 / the interface docstring) and
`SemanticModel.Build` is pure/sync. The App's metadata lives in **UI-thread** structures
(`MainWindowViewModel.EnumerateLoadedObjects` over `Metadata.Connections`, the `_columnCache`
dictionary). Therefore: the debounced background parse **must not** read live App state off-thread.
The pattern (M1): on the UI-thread debounce tick, take a cheap **immutable snapshot** (object
names+kinds + currently-cached columns + cached routine params) that *implements*
`ISqlMetadataProvider`; then off-thread run `SqlParser.Parse(text)` + `SemanticModel.Build(script,
snapshot)`; marshal the model back and cache it. Columns/params are lazily loaded, so the snapshot
carries only what is already cached; a completion that needs an uncached table's columns warms the
App cache (`EnsureColumnsAsync`) and re-runs — the same warm-then-rebuild dance the controller does
today. This keeps `EditorLanguageService`'s Etap-0 threading contract (only pure work off-thread, on
captured immutable inputs).

### 22.2 Quick-Info scope — DECIDED (2026-07-10): entirely Etap 6
The frozen design (§12/§8A) had scheduled the completion-list Quick-Info detail pane in Etap 5; the
Etap-5 kickoff prompt listed Quick Info under "do not implement yet." **User decision: ALL Quick Info
moves in full to Etap 6.** That covers the Quick Info Engine (§5.12), Ctrl-hover, tooltips, the
completion-list detail pane, and **any new surface that presents information about a symbol**. Etap 5
is **code-writing assistance only** — Completion, Signature Help, Smart Snippets, Semantic-Model
integration. The completion list's existing `: TYPE` display (a column's type, shown today) may
**stay unchanged**, but Etap 5 adds **no** new Quick-Info functionality of any kind. **→ M9 is DROPPED
from Etap 5 and folded into Etap 6.**

### 22.3 What is explicitly NOT in Etap 5
Go-To-Definition, Find References, Rename, **all Quick Info** (engine + Ctrl-hover + tooltips +
completion detail pane + any new symbol-info surface — §22.2, moved wholly to Etap 6), Diagnostics,
semantic highlighting — all later etaps (6–7). The existing **object-driven drag-drop templates**
(`SqlSnippetDropTarget` + the `ISqlTemplate`/`SnippetContext` registry) are a shipped feature and stay
**untouched**; Etap 5's Snippet Engine adds **keyword-prefix live templates** as a *parallel* path,
reusing the `SqlSnippet`/`SqlSnippetBuilder`/`SqlPlaceholder` primitives only.

### 22.4 Milestone summary

| M | Goal | Layer | Risk | Size | Depends on |
|---|---|---|---|---|---|
| **M1 ✅** | `ISqlMetadataProvider` App adapter (snapshot) + build/cache `SemanticModel` in `EditorLanguageService` (no completion change) | App (+ tiny Core mapper) | Med (threading) | S–M | Etap 4 |
| **M2 ✅** | Core `CompletionEngine` skeleton + `CompletionItem`/`Kind`/`Trigger`/`Result` + keyword & schema-object & in-scope-symbol completion | Core | Low | M | Etap 4 |
| **M3 ✅** | Core `CompletionEngine` dot/qualifier → columns (alias/table/NEW/OLD) | Core | Low | S–M | M2 |
| **M4 ✅** | Core `CompletionEngine` positional context ranking (after FROM→tables, EXECUTE PROCEDURE→procs, expression→fns+cols; ranking-only, never hides) | Core | Med | M | M2, M3 |
| **M5 ✅** | Wire `CompletionEngine` into `SqlCompletionController` (replace ad-hoc keyword/object/alias logic); retire the alias-map path | App | Med | M | M1, M4 |
| **M6 ✅** | Core `SignatureHelpEngine` (`SignatureInfo`, active-param) — EXECUTE PROCEDURE, function call, INSERT/VALUES, INSERT…SELECT, UPDATE SET (§8) + routine-param cache/warming | Core (+ App cache) | Med | M–L | M1 |
| **M7 ✅** | App signature-help popup (AvaloniaEdit) wired via the controller | App | Med | M | M6 |
| **M8 ✅** | Core keyword live-template Snippet Engine (`if`/`declare`/`execute`/`for select`/`create …`/`begin`/`while`/`case`, PSQL-context-gated) reusing `SqlSnippet*` + App integration (completion-list items + Tab-between-stops) | Core + App | Med | M–L | M4, M5 |

Etap 5 ends at **M8** — there is no M9 (the former Quick-Info detail-pane milestone was dropped and
moved wholly to Etap 6, §22.2). Sizes: S ≈ half-session, M ≈ one session, L ≈ split-if-tight. M2–M4
and M6/M8's Core halves are pure and fully unit-testable offline (highest resumability). **M1 and M2
are independent** — either can go first; M1 is recommended first because it settles the
snapshot/threading decision (22.1) that shapes M5. Any M marked M/L can be split further if a session
can't finish it cleanly.

### 22.5 Per-milestone detail

**M1 — Metadata provider adapter + cached Semantic Model (no user-visible change).**
- *Core:* (optional) a pure `MetadataObjectKind → SymbolKind` mapper if convenient (both enums are
  Core). No engine yet.
- *App:* new `AppMetadataSnapshot : ISqlMetadataProvider` (immutable) built on the UI thread from
  `MainWindowViewModel` — `FindObject` over `EnumerateLoadedObjects()` (name+kind), `GetColumns` over
  the currently-cached `_columnCache` (via `TryGetCachedColumns`), `GetRoutineParameters` empty for
  now (M6 adds the cache). Add `MainWindowViewModel.CreateMetadataSnapshot()`. Extend
  `EditorLanguageService`: on the debounce tick capture the snapshot (UI thread) via an injected
  `Func<ISqlMetadataProvider>`, then off-thread `SqlParser.Parse` + `SemanticModel.Build(script,
  snapshot)`; cache `SemanticModel? Model` + a monotonic version; add `EnsureFreshModel()` sync
  fallback (mirrors `EnsureFreshAliases`). Thread `SqlEditorBehavior.Attach` → controller →
  service so the snapshot factory reaches the service. **Do not switch completion to the model yet.**
- *Risk:* threading (mitigated by the snapshot). *Done:* model builds + caches off a real edit; a
  headless probe (add to `ConnectionExpandBindingProbe`, gotcha #94) asserts the model is non-null and
  resolves a simple `select k.x from t k`; the alias-map path still drives completion unchanged; build
  0/0; smoke clean.

**M2 — CompletionEngine (Core) — baseline list.**
- *Core:* new `EmberTern.Core.Sql.Language.Completion`: `CompletionEngine.GetCompletions(SemanticModel
  model, int offset, CompletionTrigger trigger) → CompletionResult`; `CompletionItem` (InsertText,
  DisplayText, `CompletionItemKind`, sort priority, optional detail string); `CompletionItemKind`
  (Keyword + one per `SymbolKind`); `CompletionTrigger` (Explicit / Identifier / Dot). Baseline
  behavior: keywords from `FirebirdSyntax.CompletionKeywords` + schema objects from the model's
  provider/`AllSymbols` + in-scope symbols from `model.SymbolsInScope(offset)` (aliases, variables,
  parameters, CTEs, cursors, NEW/OLD). No positional ranking yet (M4). Case handling stays at
  insert-time (`CaseMatcher`, App).
- *App:* none. *Risk:* low. *Done:* `CompletionEngineTests` (Core, fake provider) — Ctrl+Space in a
  PSQL body lists its variables/params; in a query lists its aliases; keywords + loaded objects always
  present; empty/garbage never throws; build 0/0.

**M3 — CompletionEngine dot/qualifier → columns (Core).**
- *Core:* when the offset is in a dot context (qualifier token before a `.` in the AST token stream),
  resolve the qualifier via `model.ScopeAt(offset).Resolve(qualifier)` → `TableReferenceSymbol.Target`
  / `RecordAliasSymbol.TargetTable` → `provider.GetColumns(table)` → column items. Replaces the
  `SqlAliasResolver`-based dot logic *conceptually* (the App still warms uncached columns in M5).
- *Done:* `CompletionEngineTests` — `k.` → KONTRAHENT columns (alias), `KONTRAHENT.` → columns
  (table name), `NEW.`/`OLD.` in a trigger → the relation's columns; unknown qualifier → empty; build
  0/0.

**M4 — CompletionEngine positional context ranking (Core).**
- *Core:* determine the completion context from the containing statement's node kind + the previous
  significant token(s) in the AST token stream (NOT a text re-scan): after `FROM`/`JOIN` → tables/views
  first; after `EXECUTE PROCEDURE` → procedures; keyword position → only relevant keywords; expression
  position → functions + columns-in-scope. If a context genuinely needs a deeper AST node than the
  "statement skeleton" provides, flag it and do a **scoped** parser/binder deepening as a sub-step (not
  a heuristic scan). Ranking/filtering only — never *hides* correct items, just orders/sections them.
- *Done:* `CompletionEngineTests` per context; ambiguous/partial input degrades to the M2 baseline;
  build 0/0.

**M5 — Wire CompletionEngine into the App controller.**
- *App:* rework `SqlCompletionController` to call `CompletionEngine` against
  `EditorLanguageService.Model` (deliberate triggers use `EnsureFreshModel()`; auto path uses the
  cached model). Keep the AvaloniaEdit `CompletionWindow`, `AutoPopupDelay`, case-preserving insert,
  and the async column-warm-then-rebuild for dot completion (`EnsureColumnsAsync` → rebuild snapshot →
  re-run). Map `CompletionItem` → `SqlCompletionData`. Retire the controller's direct `SqlKeywords`/
  `EnumerateLoadedObjects`/`SqlAliasResolver` usage (now via the engine/model). `SqlEditorBehavior`
  unchanged as the wiring point.
- *Risk:* behavior parity + threading. *Done:* manual smoke (SQL editor + a procedure/trigger body):
  Ctrl+Space instant, dot completion works, PSQL vars/params/NEW/OLD complete; headless probe for the
  glue; build 0/0; no regression in the Etap-0 responsiveness. **This is the first user-visible Etap-5
  change.**

**M6 — SignatureHelpEngine (Core) + routine-param cache.**
- *Core:* new `EmberTern.Core.Sql.Language.Signatures`: `SignatureHelpEngine.GetSignature(SemanticModel,
  int offset) → SignatureInfo?` (`SignatureInfo` = label + ordered params {name,type,direction,
  nullable,default,description} + active-param index). Determine the active param by counting top-level
  commas within the call/DML parens in the AST token stream. Scope §8: EXECUTE PROCEDURE, a function
  call in an expression, INSERT column list ↔ VALUES (active column ↔ active value; **count-mismatch is
  a diagnostic for Etap 7, not here**), INSERT…SELECT projection position, UPDATE `SET col = …`,
  CREATE PROCEDURE/FUNCTION param declarations. Params come from `ISqlMetadataProvider.GetRoutineParameters`
  / `GetColumns`.
- *App:* add a routine-parameter cache to `MainWindowViewModel` mirroring `_columnCache`
  (`TryGetCachedRoutineParameters` sync + `EnsureRoutineParametersAsync` async via
  `_tableDetailReader.GetProcedureParametersAsync`), surfaced through the snapshot's
  `GetRoutineParameters`.
- *Done:* `SignatureHelpEngineTests` (Core, fake provider) per §8 site; build 0/0.

**M7 — Signature-help popup (App).**
- *App:* show the signature (AvaloniaEdit `OverloadInsightWindow` or a small custom adorner) on `(`/`,`
  typed and on demand, active param highlighted; warm routine params on a miss (like columns). Wired
  through the controller; `SqlEditorBehavior` unchanged.
- *Done:* manual smoke on EXECUTE PROCEDURE + a function call + INSERT/VALUES; build 0/0.

**M8 — Keyword live-template Snippet Engine (Core) + App integration.**
- *Core:* new `EmberTern.Core.Sql.Language.Snippets`: a keyword-prefix template set (`if`→`if (·) then
  begin · end`, `declare`→`declare variable ·`, `execute`→EXECUTE BLOCK skeleton, `for select`,
  `create procedure/function/trigger/exception/domain/index`, `begin…end`, `while`, `case`), each a
  small generator returning a `SqlSnippet` (reuse `SqlSnippet`/`SqlSnippetBuilder`/`SqlPlaceholder`).
  PSQL-only templates are gated by `model.ScopeAt(offset).Kind` (RoutineBody/Block). Pure, testable.
- *App:* surface snippet items in the completion list; on pick, insert `snippet.Text` and activate
  **Tab-between-stops** navigation (the current `SqlSnippetDropTarget` only selects the first stop —
  this needs real tab-stop nav via AvaloniaEdit `Snippet`/`SnippetReplaceableTextElement` or a small
  overlay). §0: expansion inserts, never rewrites surrounding code.
- *Done:* `SnippetEngineTests` (Core) for each template's text + placeholder offsets + PSQL gating;
  manual smoke for Tab navigation; build 0/0. Split Core-vs-App if a session can't finish both.

**M9 — DROPPED (2026-07-10).** The former "completion-list detail from resolved symbol facts"
milestone is Quick Info, which the user moved wholly to **Etap 6** (§22.2). Etap 5 ends at M8. The
completion list keeps its existing `: TYPE` display; no new symbol-info surface is added in Etap 5.

### 22.6 Test / smoke discipline (every milestone)
Build 0/0 (TWAE); Core engines fully unit-tested against a fake `ISqlMetadataProvider`; App glue via
the headless `ConnectionExpandBindingProbe` (run as a **separate** `dotnet test` partition — gotcha
#94) + manual smoke; the live end-to-end completion/signature/snippet against a real FB is the user's
manual pass (DB-path convention). §0 holds by construction (all three features are read-only except
snippet *insertion*, which only inserts text — it never rewrites existing code).

---

## 23. Etap 5 — completion record (M1–M4 shipped 2026-07-10)

Milestones M1–M4 of Etap 5. **Full solution build 0/0; 3273 main + 11 probe tests green; smoke
clean.** M5–M8 remain (§22). The app's completion behaviour is **unchanged** after M1–M4 (the model
is built + cached but not yet consumed; the CompletionEngine has no App consumer until M5) — so this
is a stable, zero-regression checkpoint.

**M1 — metadata snapshot + cached Semantic Model (App wiring; no user-visible change).**
- Core: `ISqlMetadataProvider` gained `AllObjects()` (enumeration — the completion engine's baseline
  object source; `EmptyMetadataProvider` returns empty) + a pure `MetadataSymbolMap.ToSymbolKind`
  (`MetadataObjectKind → SymbolKind`, both Core enums; User → Unknown so callers skip it).
- App: [`AppMetadataSnapshot`](../../src/EmberTern.App/Completion/AppMetadataSnapshot.cs) — an
  **immutable** `ISqlMetadataProvider` built on the UI thread from `EnumerateLoadedObjects()` +
  a shallow copy of the `_columnCache` (columns mapped lazily in `GetColumns`; params empty until
  M6). `MainWindowViewModel.CreateMetadataSnapshot()`. [`EditorLanguageService`](../../src/EmberTern.App/Completion/EditorLanguageService.cs)
  extended: on the idle tick it captures the snapshot (UI thread) via an injected
  `Func<ISqlMetadataProvider>`, then off-thread builds `SemanticModel.Build(text, snapshot)` (§22.1 —
  only pure work off-thread, on a captured immutable snapshot) alongside the existing alias parse,
  and caches `SemanticModel? Model` + `ModelFresh` + a sync `EnsureFreshModel()` (mirrors
  `EnsureFreshAliases`). Snapshot factory threaded `SqlEditorBehavior.Attach` / `MainWindow.axaml.cs`
  → controller → service. Headless probe `EditorLanguageService_BuildsAndCachesSemanticModel`.

**M2–M4 — `CompletionEngine` (Core, pure — `EmberTern.Core.Sql.Language.Completion`).**
`CompletionEngine.GetCompletions(SemanticModel model, int offset, CompletionTrigger trigger) →
CompletionResult` + `CompletionItem` (InsertText/DisplayText/`CompletionItemKind`/`SortPriority`/
`Detail`) + `CompletionItemKind` (Keyword + one per `SymbolKind`) + `CompletionTrigger`. Consumes
**only** the model — its metadata snapshot, `SymbolsInScope(offset)`, `ScopeAt(offset)`, and the AST
token stream — never a fresh text scan (§22.0). Case handling stays insert-time (App `CaseMatcher`).
- **M2 baseline:** keywords (`FirebirdSyntax.CompletionKeywords`) + schema objects
  (`model.Metadata.AllObjects()`, Unknown-kind e.g. users skipped) + in-scope symbols
  (`SymbolsInScope` — aliases/variables/parameters/CTEs/cursors/NEW-OLD). Deduped by (name, kind);
  ranked by kind priority (in-scope locals + columns above catalog objects above keywords).
- **M3 dot/qualifier → columns:** a "`qualifier . [prefix]|`" caret (detected from the token stream
  with adjacency, not a char scan) resolves the qualifier via `ScopeAt(offset).Resolve` →
  `TableReferenceSymbol.Target`/`.TargetName` / `RecordAliasSymbol.TargetTable` / a DDL object in
  scope / a catalog `FindObject` fallback → `GetColumns` → column items. `CompletionResult` carries
  `IsDotContext` + `DotTargetTable` so the App (M5) warms a resolved-but-uncached target and never
  falls back to keywords after a ".".
- **M4 positional ranking:** boosts the contextually-relevant kinds from the previous significant
  token + containing statement kind — after FROM/JOIN/INTO/UPDATE → tables/views/CTEs; after EXECUTE
  PROCEDURE (gated on the `ExecuteProcedure` statement kind, NOT `CREATE PROCEDURE`) → procedures;
  expression/value position (comma, `(`, operators, SELECT/WHERE/ON/AND/…) → columns/functions/
  in-scope locals. **Ranking only — never hides a correct item**; ambiguous input degrades to the M2
  baseline order.
- Tests: [`CompletionEngineTests`](../../tests/EmberTern.Tests/CompletionEngineTests.cs) (26) — a
  fake `ISqlMetadataProvider`, per-milestone: baseline (keywords always present, loaded objects,
  Unknown skipped, query aliases, PSQL params/vars, trigger NEW/OLD, dedup, garbage never throws),
  dot (alias/table/NEW/OLD columns + detail-type, partial prefix, unknown → empty dot, uncached →
  target-for-warming), ranking (FROM→tables, EXECUTE PROCEDURE→procs, CREATE PROCEDURE no-boost,
  expression→alias, offset-0 no-boost).

**Deferred to M5–M8 (§22, unchanged):** wire the engine into the App controller + retire the
alias-map path (M5, first user-visible change); Signature Help engine + popup + routine-param cache
(M6/M7); keyword live-template Snippet Engine + Tab-stop navigation (M8). All Quick Info is Etap 6.

---

## 24. Etap 5 — completion record (M5–M8 shipped 2026-07-10)

Milestones M5–M8 finish Etap 5. **Full solution build 0/0; 3321 main + 11 probe tests green; smoke
clean.** This is the first user-visible Etap-5 change: the editor's completion now runs off the
CompletionEngine + Semantic Model, signature help appears at call/DML sites, and keyword live
templates expand with Tab-stops. The Core engines (M6/M8 halves) are pure and offline unit-tested; the
live completion/signature/snippet UX against a real FB is the user's manual pass (DB-path convention).

**M5 — CompletionEngine wired into the controller (first user-visible change).** `SqlCompletionController`
is now thin glue over the engine + model: it decides *whether/when* to open the window (from the few
chars before the caret via `CaretContext` — never the whole `Text`) and positions the `CompletionWindow`
over the replaced segment; the item list comes entirely from `CompletionEngine.GetCompletions` against
`EditorLanguageService.Model`. The controller's direct `SqlKeywords` / `EnumerateLoadedObjects` /
`SqlAliasResolver` usage is **retired** — keyword/object/in-scope/dot resolution all live in the
engine/model. Deliberate triggers (typed `.`, Ctrl+Space) `EnsureFreshModel()`; the auto path uses the
cached model. Dot completion keeps the async column **warm-then-rebuild** (new
`EditorLanguageService.RefreshModelWithMetadata()` rebuilds the model against a fresh snapshot after the
App warms an uncached table). `SqlCompletionData` gained a pure `MapKind(CompletionItemKind →
SqlCompletionKind)` + a `FromItem` factory (+ new display kinds Alias/Variable/Parameter/Cte/Cursor/
Record); the `: TYPE` suffix stays columns-only (no new symbol-info surface, §22.2). **NEW/OLD in a
body-only (Easy-mode) trigger editor** — where the CREATE TRIGGER header isn't in the text so the model
can't bind them — still resolve via the retained `contextTableProvider` fallback (engine dot result has
no target → resolve NEW/OLD to the trigger's table → warm + show). Constructor slimmed to
`(editor, metadataSnapshot, ensureColumnsAsync?, contextTableProvider?, ensureRoutineParamsAsync?)`;
both call sites (`SqlEditorBehavior` + `MainWindow`) updated; dead `GetCompletionObjects`/`GetKnownTables`/
`GetCachedColumns` wrappers removed. Glue pinned by `SqlCompletionDataMapTests` (pure, non-flaky); live
completion is manual smoke.

**M6 — SignatureHelpEngine (Core) + routine-param cache.** New
`EmberTern.Core.Sql.Language.Signatures`: `SignatureHelpEngine.GetSignature(model, offset) →
SignatureInfo?` (label + ordered `SignatureParameter`s + active-param index + `SignatureKind`). Pure —
reads only the containing statement's AST token stream + the metadata snapshot. Sites (§8): the
innermost enclosing `(…)` wins — a routine call (EXECUTE PROCEDURE with parens / function call /
selectable proc, params from `GetRoutineParameters` inputs) or an INSERT column-list / VALUES paren
(columns from `GetColumns`); then non-paren statement sites — EXECUTE PROCEDURE without parens, UPDATE
`SET col = …` (assigned columns), and INSERT…SELECT projection (mapped to target columns). Active param =
top-level comma count in the relevant region. Count-mismatch diagnostics are Etap 7, not here; a CREATE
PROCEDURE/FUNCTION *declaration* site has no callee, so it produces no signature (documented — it's a
completion/type-list concern). App: a routine-parameter cache on `MainWindowViewModel`
(`TryGetCachedRoutineParameters` + `EnsureRoutineParametersAsync`, proc inputs+outputs via
`GetProcedureParametersAsync`, function args via `GetFunctionSignatureAsync`) surfaced through the
snapshot's `GetRoutineParameters`; cleared on disconnect with the column cache. Pinned by
`SignatureHelpEngineTests` (20, fake provider). **Known limits:** signatures are shown only for routines
the snapshot knows (user procs/functions) — built-in function signatures would need a static catalog
(future); function args carry only inputs.

**M7 — signature-help popup (App).** `SqlCompletionController` shows an AvaloniaEdit
`OverloadInsightWindow` (single-signature `IOverloadProvider`) on `(` / `,` typed and on
Ctrl+Shift+Space, dismissed on `)` that ends the call / Escape / when the completion list opens. Header
= `LABEL(p0 type, p1 type, …)` with the active parameter **bold**; content = the active param's
type/direction/nullability/default/description. On a metadata miss it warms the callee's routine params
(or the DML target's columns) via the injected delegates, then rebuilds the model and re-queries — the
same warm-then-show as columns. Live popup behaviour is manual smoke.

**M8 — keyword live-template Snippet Engine (Core) + App integration.** New
`EmberTern.Core.Sql.Language.Snippets`: `SnippetEngine.GetSnippets(model, offset)` returns the templates
valid at the caret — PSQL control-flow (`if`/`while`/`for select`/`begin`/`case`/`declare`) gated to a
`RoutineBody`/`Block` scope, top-level `execute` (EXECUTE BLOCK) + `create procedure/function/trigger/
exception/domain/index` elsewhere. Each `SnippetTemplate` builds a `SqlSnippet` (reusing
`SqlSnippet`/`SqlSnippetBuilder`/`SqlPlaceholder` — the shipped object-driven `SqlSnippetDropTarget` is
untouched, §22.3). App: `SnippetCompletionData` surfaces them in the completion list (kind label
"Snippet", filtered by the trigger keyword); on accept it removes the typed prefix and inserts an
AvaloniaEdit `Snippet` (Core placeholders → `SnippetReplaceableTextElement`s) so **Tab cycles the
stops** (§0: insertion only). Pinned by `SnippetEngineTests` (7 — template set, per-template placeholder
offsets, determinism, PSQL/top-level gating); Tab navigation is manual smoke.

**Deferred (Etap 6, unchanged):** all Quick Info (engine + Ctrl-hover + tooltips + completion detail
pane), Navigation (Ctrl+hover/click/peek/find-refs/rename), semantic highlighting. Etap 7: diagnostics +
folding/breadcrumbs/bracket-matching. Built-in-function signature catalog + CREATE-PROC/FUNC param assist
are open follow-ups noted in M6.

---

## 25. Etap 6 — completion record (M1–M3 shipped 2026-07-10)

Etap 6 — Navigation + Semantic highlighting + Quick Info — the first etap that shows the user the
full power of the new front-end. It splits into resumable milestones, Core-first (pure, fully
unit-tested) then App: **M1 Quick Info Engine · M2 Navigation Engine · M3 Semantic highlighting ·
M4 Ctrl+hover/click go-to-def + tooltip · M5 Quick Info detail pane + Peek + local find-refs/rename.**
This record covers **M1–M3** (all built this session). **Full solution build 0/0; 3365 main + 12 probe
tests green; smoke clean.** The binding constraint (§22.0) holds throughout: every engine consumes
**only** the Lexer → Parser → AST → Semantic Model — no name-based text search, no new heuristics, no
parallel fetch path. All three are read-only, so §0 (never lose information) holds by construction.

**M1 — Quick Info Engine (Core, pure).** New `EmberTern.Core.Sql.Language.QuickInfo`:
`QuickInfoEngine.GetQuickInfo(model, offset)` → `QuickInfo?` (resolves the identifier under the caret
to a `Symbol`, `null` when not on a resolved identifier) + `ForSymbol(symbol, metadata)` → `QuickInfo`
(never null for a real symbol). The `QuickInfo` model (`Kind` + `Header` + `Description` + ordered
`QuickInfoFact` label/value rows + `QuickInfoMember` column/parameter/return lines) is rich-but-optional
and App-renderable (the Ctrl-hover tooltip in M4, the completion detail pane in M5). Content per §8A:
**columns** (the headline "check a column without opening its table" — type/domain/nullability/default/
description/PK/FK→table/identity/computed/owning-table, all from the resolved `ColumnSymbol` the binder
already populates from `ColumnMetadata`); **tables/views** (kind + description + owner + column members
via `metadata.GetColumns`); **procedures/functions** (parameter/return members via
`metadata.GetRoutineParameters`, IN→Parameter / OUT→Returns groups); **FROM aliases** (`K → KONTRAHENT`
+ the target table's columns); **NEW/OLD** (→ the trigger table's columns); **CTE** (declared columns);
**PSQL variables/parameters/cursors** (kind + direction + type). Richer per-object facts for trigger
timing/domain-check/exception-message/generator-value are a documented provider extension (the
`ObjectMetadata` snapshot doesn't carry them yet) — baseline is honest and grows without an API change.
Pinned by `QuickInfoEngineTests` (18, fake provider).

**M2 — Navigation Engine (Core, pure).** New `EmberTern.Core.Sql.Language.Navigation`:
`NavigationEngine.TargetAt(model, offset)` → `NavigationTarget?` classifies the reference under the
caret into `SchemaObject` (name + kind → the App opens its DDL/detail) or `LocalDefinition` (an
in-editor declaration span the App jumps to). Real resolution, **not** a name search (the old
`TryOpenDdlForWord` path): a column → open its owning table; a FROM alias → open its target table (or,
for a CTE target, jump to the CTE declaration); a variable/parameter use → jump to its `DECLARE`; a
schema object → open it; NEW/OLD → the trigger's table; a derived table → its own declaration; an
unresolved/keyword offset → `null`. Also `LocalReferences(model, offset)` (every occurrence bound to
the same symbol — the local find-references / rename-highlight basis) and `LocalDefinition(model,
offset)`. Pinned by `NavigationEngineTests` (16). This is the engine M4 wires to Ctrl+hover/Ctrl+Click.

**M3 — Semantic highlighting (Core classifier + App painter).** The two-layer "calm base, semantic
accent" system (§9.2). **Core** `EmberTern.Core.Sql.Language.Highlighting`:
`SemanticHighlightClassifier.Classify(reference)` → `SemanticHighlight` (`Class` ∈ {None, SchemaObject,
Column, Local} + the object kind). Pure, keyed off the resolved `Symbol` subtype (unresolved → None, so
only the lexical XSHD layer shows — high-precision, never guesses). Pinned by
`SemanticHighlightClassifierTests` (10). **App** `SemanticHighlighter : DocumentColorizingTransformer`
(mirrors the shipped `OccurrenceHighlighter` glue pattern): walks the cached `SemanticModel`'s
references per visible line, classifies each, and paints a theme brush — **schema objects reuse the
metadata tree's per-kind `IconColor_*` palette** (editor colour == tree icon, teaching "coloured object
= navigable", §9.2), columns get a new calm `EditorColumnBrush`, locals (aliases/variables/parameters/
cursors/CTEs/NEW-OLD) a distinct low-chroma `EditorLocalBrush` — both added to **both** theme
dictionaries of `Colors.axaml` (dark + light per §9.3). An exact-span overlap (a table referenced by
its own name records both an object occurrence and an implicit table-reference occurrence) is resolved
by applying lowest-priority first so the object class wins (gotcha below). The lexical XSHD layer and
the semantic layer are disjoint in practice (the classifier only colours resolved *identifiers*, which
XSHD leaves at the default foreground; it never touches keywords/strings/numbers), so transformer order
doesn't matter. **Model sharing (low-risk wiring, no ctor refactor):** the highlighter reuses the
per-editor `EditorLanguageService`'s cached model via the existing `SqlCompletionController` — the
service gained a `ModelUpdated` event (raised on every model (re)build) and the controller forwards it +
exposes `Model`; the highlighter repaints on `ModelUpdated`. Wired in `SqlEditorBehavior.Attach` (every
SQL/PSQL surface) + the `MainWindow` SQL editor. Pinned by a headless probe
(`SemanticHighlighter_AttachesAndColorizesWithoutThrowing`) that colorizes a resolved query without
throwing and asserts the new + reused theme brush tokens resolve from the real App resources.

**Colour hex is a conservative default (§9.5) — final visual tuning in both themes is the user's
manual pass** (the whole point of the light-theme work is "does it look muddy" — inherently visual;
the code satisfies the principles: distinct hue+lightness, WCAG-minded, not a Christmas-tree). Italic
for locals (a Rider-style extra signal) was evaluated and left as an optional tuning to keep M3
colour-only + low-risk.

**Deferred to M4/M5 (unchanged):** the Ctrl-hover affordance (underline + hand cursor + Quick Info
tooltip) + Ctrl+Click go-to-definition driven by `NavigationEngine` (replacing the name-based
`TryOpenDdlForWord` for the primary path, kept as the fallback for body-only Easy-mode editors where
the model can't see the header); the Quick Info completion detail pane; Peek Definition; local
find-references + local rename. These are App-heavy AvaloniaEdit pointer/cursor/adorner glue that needs
real visual verification — a fresh session, not started late.

**Gotcha — promote to architecture lore.**

197. **A resolved identifier can carry MORE THAN ONE `SymbolReference` at the exact same span — apply
semantic highlighting lowest-priority-first so the stronger class wins.** A table referenced by its own
name (`FROM KONTRAHENT`, no alias) records both a schema-object occurrence (→ table colour) AND an
implicit table-reference occurrence (the implicit alias == the table name, → the "local" colour) at the
identical span. A `DocumentColorizingTransformer` applies `ChangeLinePart` in call order and the LAST
write wins for an overlapping range, so naively iterating `model.References` (object added before the
table-ref) would paint it local. Fix: collect the line's hits, sort by class priority
(Local < Column < SchemaObject), and apply ascending so the object class is painted last and wins.
Non-overlapping references are unaffected by order. General rule for any semantic paint over the model's
references: never assume one reference per offset — resolve overlap by a deliberate priority, not by
iteration order.

---

## 26. Etap 6 — completion record (M4 shipped 2026-07-10)

Etap 6 / M4 — the navigation UX: **Ctrl+hover** (underline + hand cursor + Quick Info tooltip) and
**Ctrl+Click** go-to-definition, wiring the M1/M2 engines into the editor. App-layer glue only (the
engines shipped in M1–M3). **Full solution build 0/0; 3365 main + 13 probe tests green; smoke clean.**
Read-only feature — §0 holds by construction (navigation never modifies code).

**`NavigationController` (App/Completion).** Attaches to a `TextEditor`; a thin glue over the pure
Core engines. Hover: on `TextView.PointerMoved` (and on the editor's `KeyDown`/`KeyUp` while focused, so
holding Ctrl over an already-hovered word lights it up) it maps the pointer to a document offset
(`TextEditor.GetPositionFromPoint` → `Document.GetOffset`), and **only when Ctrl is held** asks
`NavigationEngine.TargetAt(model, offset)`. A non-null target → underline the identifier + hand cursor +
Quick Info tooltip; anything else clears. The affordance is **semantic** (real resolution), so the
underline appears *exactly* where Ctrl+Click will navigate — not a name match. It reads the per-editor
**cached** `SemanticModel` (shared with the completion controller + semantic highlighter — one background
parse per editor); it never re-parses on the pointer path (`SemanticModel.ReferenceAt` is a cheap linear
scan, gated to Ctrl-held). Click: a tunneled `PointerPressed` (Ctrl+left) runs `NavigationEngine.TargetAt`
→ a **SchemaObject** opens via the VM (`TryOpenSchemaObject`), a **LocalDefinition** jumps the caret to
the declaration span in-editor (no DB), and a **null** target falls back to the name-based open
(`TryOpenDdlForWord`) so body-only Easy-mode editors (whose CREATE header isn't in the text, so the model
can't resolve) still navigate. Marks the click handled so no stray word-select fires. `Detach()` unhooks
everything and removes the renderer.

- **Underline** — an `IBackgroundRenderer` (`KnownLayer.Selection`) drawing a 1px `AccentBrush` line at
  the bottom of the active span via `BackgroundGeometryBuilder.GetRectsForSegment` (the accent = the
  universal "this is a link" cue, calm and consistent over any per-kind object colour).
- **Hand cursor** — `editor.TextArea.TextView.Cursor = Hand` while active, cleared to `null` (inherits
  the editor's I-beam) otherwise.
- **Tooltip** — a self-managed `Popup` (`PlacementMode.Pointer`, `VerticalOffset` 16 so it sits below the
  cursor, parented via `ISetLogicalParent`, `IsLightDismissEnabled=false`) whose content is
  **hit-test-invisible** so it never intercepts the pointer (which would fire `PointerExited` and flicker
  it shut). Rebuilt only when the hovered span changes (not every pointer move).

**`QuickInfoView` (App/Completion) — shared renderer.** `Build(QuickInfo, ThemeVariant, maxMembers=12)`
→ a themed `Border` (header + subtle kind tag + description + "label value" facts + grouped member lines,
members capped with an "… and N more" overflow). Every colour is a theme token (UI rules); the header is
coloured by resolved role via the new shared **`EditorSemanticColors.ObjectBrushKey`** (extracted from
`SemanticHighlighter`, which now delegates to it — one kind→`IconColor_*` mapping, no drift). Brushes are
resolved against the passed theme at build time — correct for the transient tooltip; the M5 detail pane
(persistent) will rebuild on theme change. **This is the reusable surface M5's completion detail pane
consumes.**

**VM.** `MainWindowViewModel.TryOpenSchemaObject(name, fallbackKind)` — opens a resolved schema object,
preferring the **authoritative kind from loaded metadata** (`TryResolveLoadedObject`, so a view's column
that the engine reports with a `Table` owner still opens as a View) and using the Navigation Engine's
mapped kind only as the fallback.

**Wiring.** `SqlEditorBehavior.Attach` (every SQL/PSQL surface) now calls `NavigationController.Attach`
and **owns Ctrl+Click** — the old name-based Ctrl+Click tunnel was removed; double-click stays name-based
(optional IBExpert compat, §10). The `MainWindow` SQL editor attaches it too (callbacks delegate to the
current VM null-safely), which also **adds Ctrl+Click navigation to the main SQL editor** (it previously
had only double-click). Both share the completion controller's cached model.

**Tests.** A headless probe (`NavigationController_AttachesAndDispatchesGoToDefinition`) proves attach
doesn't throw, the underline renderer is registered, `AccentBrush` resolves, and — the behavioural bit —
Ctrl+Click at a resolved table offset dispatches to the schema-object open callback with the mapped kind
(and not the name fallback), plus `Detach()` removes the renderer. The per-kind classification is
Core-tested by `NavigationEngineTests`; the pointer/cursor/tooltip UX and the final colour tuning are
**manual visual verification** (design §9.5 — inherently visual, a fresh-eyes pass in both themes).

**Known M4 caveats to verify visually (both themes):** the hand-cursor override (`TextView.Cursor`) vs
AvaloniaEdit's I-beam, the tooltip position/offset feel, and that Ctrl+hover reads as "obviously
clickable" without clutter. The `AccentBrush` underline + per-kind object colour is the conservative
default; adjust only if the visual pass finds it lacking.

**Deferred to M5 (unchanged):** the Quick Info **completion detail pane** (reuse `QuickInfoView`), **Peek
Definition** (inline DDL/source flyout), **local find-references** (`NavigationEngine.LocalReferences` →
highlight) and **local rename** (alias/variable/parameter/cursor within the statement/body — safe per §0;
cross-DB rename stays out of scope).

---

## 27. Etap 6 — completion record (M5 shipped 2026-07-11) — **Etap 6 COMPLETE**

Etap 6 / M5 — the four remaining Etap-6 features, all App-layer glue over the pure Core engines shipped in
M1–M4 (plus one small §0-safety helper added to the Core Navigation Engine). **Full solution build 0/0;
3373 main + 13 probe green; smoke clean (app alive 9 s, no fatal).** The binding constraint (§22.0) held:
every feature consumes **only** the Semantic Model / NavigationEngine / QuickInfoEngine / QuickInfoView —
no new renderers, no new models, no parallel presentation system (user directive). Read-only except the
rename, which is governed by §0 (never lose information / never corrupt user code — decision #4).

**Completion detail pane (design §8A).** `SqlCompletionData.Description` — the object AvaloniaEdit shows
beside the list for the selected item — is now a lazily-built **`QuickInfoView`** card (the same renderer
as the Ctrl-hover tooltip: one implementation, one source of truth) instead of a plain string. The
controller supplies a per-item `Func<object?>` detail factory; it's invoked only for the selected item, so
it's cheap and always matches the current theme. `SqlCompletionController.ResolveItemQuickInfo` builds the
`QuickInfo` via `QuickInfoEngine.ForSymbol`: for an in-scope **local** it uses the real symbol
(`SymbolsInScope` name match → rich alias arrow / variable type); every other kind synthesises a `Symbol`
from the item's name + kind (+ the `Detail` type for columns), and members (a table's columns, a routine's
params) come from the model's metadata snapshot when cached. Keywords get no card (fall back to the kind
label). Dot-completion columns (`ColumnSpec`) build a column card the same way. **Documented limitation:**
a schema object whose columns aren't cached shows header-only (no warm-on-selection — warming per
list-navigation keystroke would fire many DB reads; the user's already-used tables are cached).

**Safe local rename — F2 (design §10, §0).** The load-bearing safety lives in **pure Core**:
`NavigationEngine.GetLocalRename(model, offset) → NavigationRename?` returns a rename **only** for a
FROM/JOIN alias (or derived-table alias), a PSQL variable/parameter, a cursor, or a CTE — a schema object,
a column, a `NEW`/`OLD` record, or a table referenced by its own name yields `null`, so a rename **can
never touch a database object**. It carries every occurrence bound to that exact symbol (declaration + uses,
from the binder's precise resolution — a name that merely collides is never swept in). The App
(`NavigationController`) opens an inline rename box anchored at the caret (a `Popup` + `TextBox`, prefilled
from the identifier as written, Enter commits / Escape cancels / click-away cancels). Commit is guarded
three ways: the new name must be a plain identifier, must not be a Firebird keyword (`FirebirdSyntax.
IsKeyword`), and must not collide with another in-scope local; then — the §0 apply guard —
`TryApplyRename` verifies **every** occurrence still reads as the resolved identifier (folded like the
binder) before editing, and **aborts the whole rename** if the document has drifted from the model. The
replace runs last-to-first in one `BeginUpdate/EndUpdate` group (one undo step, offsets stay valid). No
DDL, no cross-DB, no partial edits.

**Local find references (design §10).** A caret-driven `IBackgroundRenderer` (`ReferenceHighlightRenderer`)
boxes every occurrence of the **local** symbol under the caret (alias / variable / parameter / cursor / CTE
/ NEW-OLD / derived table) via `NavigationEngine.LocalReferences` — schema objects and columns are
deliberately **not** highlighted (boxing every table/column occurrence would be noise), and a lone
occurrence isn't boxed (calm). It reuses the same subtle `OccurrenceHighlightBrush` fill as the
select-a-word occurrence boxes (fill-only, no outline, so it stays subtle and consistent), caches by
`(caret, model)` so scroll/repaint is cheap, and repaints on caret move (`Caret.PositionChanged`) and on
model rebuild (the semantic highlighter already forces a `TextView` redraw on `ModelUpdated`).

**Peek Definition — Alt+F12 (design §10).** Shows the definition inline **without opening a tab**, driven by
`NavigationEngine.TargetAt`: a **local** → its declaration line(s) from the document (no DB); a **schema
object** → its reconstructed DDL/source fetched read-only via a new VM callback
`MainWindowViewModel.FetchObjectDefinitionAsync` (reuses `FirebirdDdlReader.FetchDdlAsync`; best-effort →
null on failure, so a peek never crashes the editor). The flyout is a themed `Popup` card anchored at the
caret (a read-only monospace, scrollable `TextBox`), light-dismiss + Escape to close, with a generation
counter that drops a stale async DDL result if the peek was superseded/closed while fetching. A body-only
Easy-mode editor (whose CREATE header isn't in the text, so the model can't resolve) falls back to a
name-based fetch, mirroring the Ctrl+Click fallback.

**Wiring.** All four ride the existing integration points: the completion detail pane is inside
`SqlCompletionController`; find-refs/rename/peek extend the existing `NavigationController` (already attached
at both editor sites). `NavigationController.Attach` gained one optional `fetchDefinition` callback for
peek; `SqlEditorBehavior.Attach` (every SQL/PSQL surface) and the `MainWindow` SQL editor both pass it. No
new attach path, no new component surface.

**Tests.** Core: `NavigationEngineTests` +8 (`GetLocalRename` — alias/variable/parameter/CTE renameable;
schema object / column / NEW-OLD / keyword refused; garbage-input never throws). App: the
`NavigationController_AttachesAndDispatchesGoToDefinition` headless probe extended to prove the
reference-highlight renderer is registered, find-references highlights only locals, a **DB object rename is
refused**, and an **alias rename rewrites every occurrence atomically** (the §0-critical path, pinned
headlessly). The completion detail pane's rendering, the rename/peek popups' placement/feel, and the
find-refs colour are **manual visual verification** (design §9.5 — inherently visual, the editor review
pass in both themes).

**Known M5 caveats to verify in the review pass (both themes):** the completion detail card may nest inside
AvaloniaEdit's own tooltip chrome (a minor double-border — tunable); the rename/peek popup caret anchoring
(`PlacementRect` from the caret's visual position, centre fallback); Alt+F12 as the peek gesture (VS
convention) and F2 as rename; and that find-references reads as a calm "highlight usages" (not a Christmas
tree). None affect correctness.

---

## 28. Editor UX Polish Phase — plan (opened 2026-07-11 after the user's Etap-6 review)

After Etap 6 the user ran a practical review (EmberTern vs IBExpert) and — explicitly endorsing the
architecture — filed a **UX polish backlog**. This is NOT new architecture and NOT Etap 7: it refines
what's built to a modern-IDE feel. Work through it in priority order, one coherent package per session,
each complete + tested + smoke before the next (staged-implementation contract). Most items need the
user's **live visual iteration** (colours, spacing, popups); Core-side bugs are unit-pinned. **Do not
start Etap 7 until the user closes this phase.**

Backlog (grouped into packages; **P-nums are the review's numbering**):

- **P1 — dot completion after an alias (`n.`) at end of statement. ✅ FIXED (2026-07-11).** Root cause
  (found by diagnostic, not guessed): a **Core Semantic Model** bug — `Scope.ScopeAt` used half-open
  containment, so a caret at the exact END of a statement (`… where n.|`, the most common completion
  position) fell through the query scope to the Script scope, where the FROM alias isn't visible →
  `DotTargetTable = null` → no columns. Mid-statement worked; the trailing edge didn't (which is why it
  looked intermittent). Fix: `ScopeAt` is now inclusive at the end (`Start ≤ offset ≤ End`) with
  later-start-wins at a shared sibling boundary — a one-method change, no span changes. Pinned by
  `CompletionEngineTests.Dot_AtEndOfStatement_ResolvesAliasColumns` (SELECT + multi-line + UPDATE). This
  also improves baseline completion / signature help / snippets / `SymbolsInScope` at end-of-statement
  (all consume `ScopeAt`). Gotcha #198.

- **P2 — completion list redesign. ✅ DONE (2026-07-11)** except (c) deferred. The enabling architectural
  change (b) landed first: **`ColumnSpec` gained `Domain` + `NotNull`**, `FirebirdMetadataReader.ColumnsSql`
  now selects `RDB$FIELD_SOURCE` (normalized via `NormalizeDomain`) + the null flag (no PK/FK join — the
  completion read stays on the hot path), `AppMetadataSnapshot.GetColumns` maps them into `ColumnMetadata`
  (Domain + Nullable), and **`CompletionItem` gained an optional `Symbol`** carrier so the engine's dot path
  attaches a rich `ColumnSymbol` (type + domain + nullability + owning table) — the row AND the detail pane
  read the same one source (no second lookup, no duplicated model). UI: (b) the row shows a subtle
  `: TYPE : DOMAIN` (domain only when domain-typed); (d) a **per-kind `SvgIcon`** reusing the metadata-tree /
  semantic palette (`Icon.*` + `IconColor_*`/`EditorColumnBrush`/`EditorLocalBrush`), keywords get no icon
  (aligned blank slot); (e) the fixed 90-px text kind column is **replaced by the icon** and the row font
  dropped to 12 → lighter, VS/Rider-style; (a) the **detail pane is now genuinely additive** — a column's
  pane shows Table + Domain + Nullability on top of the row (and tables/procs still show their column/param
  members), so it's kept, not suppressed. Pinned by `CompletionEngineTests.Dot_Column_CarriesRichSymbolWithDomain`.
  The live look (icons/font/colours, both themes) is the user's visual pass. **(c) bold/highlight the typed
  fragment — DEFERRED** (not a hack, per directive): AvaloniaEdit's `CompletionList` doesn't expose per-item
  match highlighting for a custom `Content`, and reacting to the live filter text per row would need a
  per-keystroke re-render of every item (fragile) or a custom list template — its own small follow-up, not
  P2-blocking. Tracked below.

- **P2c — bold/highlight the typed fragment in each completion row** (split out of P2). Needs either a
  controller-side re-render of visible items on each filter keystroke (read the typed prefix from
  `[StartOffset, caret]`, rebuild each row's name with the matched run bolded) or a custom
  `CompletionListBox` item template fed the filter. Do it cleanly or not at all (§0-adjacent: no fragile
  hack). Low priority vs the rest.

- **P3 — ✅ DONE (2026-07-11).** Ctrl+Space on a fully-typed, resolved identifier now shows ITS facts —
  the same `QuickInfoView` card as the Ctrl-hover tooltip / completion detail pane (one source of truth) —
  instead of re-listing from scratch. A **second** Ctrl+Space (info already showing) escalates to the full
  completion list, so the user gets both (better than IBExpert, which only shows the facts). Root cause of
  the end-of-word case: `SemanticModel.ReferenceAt` used half-open `TextSpan.Contains`, so the caret at the
  exact end of `nrdokwew|` (the most common position) resolved to nothing → made **inclusive at the end**
  (`Start ≤ offset ≤ End`, shortest-span tie-break) — the same insight as gotcha #198 applied to reference
  lookup (also improves M4/M5 navigation at end-of-word). The popup is caret-anchored via a new shared
  `EditorPopups.PlaceAtCaret` (extracted from `NavigationController` — both now share ONE caret-placement
  helper), `IsLightDismissEnabled=false` + hit-test-invisible content (never steals editor focus), dismissed
  on type / Escape / caret move / window open. Pinned by `QuickInfoEngineTests.Column_ResolvesAtEndOfIdentifier`
  + `SemanticModelTests.ReferenceAt_IsInclusiveAtEndOfIdentifier`. The live popup placement is the user's
  visual pass.

- **P4 — ✅ DONE (2026-07-11).** Dragging the completion list's OWN scrollbar no longer dismisses it. Root
  cause (found by reflecting the AvaloniaEdit API, not guessing): the scrollbar-thumb drag opens the list as
  a separate popup window that deactivates the parent → AvaloniaEdit's `ParentWindow_Deactivated` →
  `CloseIfFocusLost` closed the list mid-scroll. `CloseOnFocusLost` is a `protected virtual` get-only
  property, so the fix is a tiny `NonFocusClosingCompletionWindow : CompletionWindow` overriding it to
  `false` — only the focus-lost close path is disabled; the list still closes on caret-move-away, Escape,
  item selection, and non-matching input (`CloseAutomatically`). Trade-off (accepted): clicking a non-editor
  control while the list is up leaves it open until the caret next moves. The scrollbar-drag confirmation is
  the user's visual pass (pointer gesture, untestable headlessly). Gotcha #199.

- **P5 — semantic highlighting consistency. ✅ DONE (2026-07-11).** (a) **Root cause found by reading the
  binders, not blind, then pinned headlessly across ALL 12 statement kinds** (`SemanticHighlightConsistencyTests`
  — SELECT/UPDATE/INSERT/UPDATE OR INSERT/DELETE/MERGE/EXECUTE PROCEDURE/EXECUTE BLOCK/CREATE
  PROCEDURE/FUNCTION/TRIGGER/VIEW): the test failed on **exactly one** kind — CREATE TRIGGER. The gap was
  in `SemanticBinder.Psql.BindTriggerDefinition`: it read the trigger's `FOR <table>` name (to bind NEW/OLD)
  but **never recorded a schema-object reference on that token**, so the trigger's target table was neither
  coloured nor Ctrl+Click-navigable, while the same table in FROM/UPDATE/INSERT/MERGE was (those all go
  through `BindNamedTable`, which does record it). Fix: record the schema-object reference on the FOR table
  when metadata resolves it (mirrors `BindNamedTable`'s precision — no unresolved reference). Now all 12
  kinds colour a table/object identically; nice bonus — the trigger's FOR table is now go-to-definition +
  Quick-Info navigable. (b) **Object colours reuse the tree palette** — verified structurally complete
  (`EditorSemanticColors` maps every schema-object kind). (c/d P5c dark keyword-vs-table + P5d plain-hover →
  see P9 / backlog below; the user's explicit P5 ask was *consistency*, which is done.)

- **P6 — INSERT/VALUES helper. ✅ DONE (2026-07-11).** Double-click a value in `values (…)` (or a name in the
  column list) → a popup listing the INSERT target columns, the one at the matching position **bold +
  `AccentBrush`** with a 1-based ordinal ("17. NAME : TYPE"), so a 30-column INSERT answers "which column is
  this value?" without counting. **Core: zero changes** — reuses `SignatureHelpEngine.GetSignature`, which
  already returns `SignatureKind.Insert` + target columns + the active value's index at a VALUES/column
  offset (pinned by `SignatureHelpEngineTests`). **App: consolidated into `NavigationController`** — the two
  duplicated `DoubleTapped` handlers (`SqlEditorBehavior` + `MainWindow`) were DELETED and replaced by ONE
  handler there: `sig = GetSignature(model, caret)`; if `IsInsertHelperSignature(sig)` (pure `internal
  static` predicate = `Kind: Insert, Parameters.Count > 0`, unit-pinned by `InsertHelperPredicateTests`) →
  show the helper popup (`EditorPopups.PlaceAtCaret`, hit-test-invisible, dismiss on caret-move/type/Escape/
  Detach — same pattern as the P3 Quick Info popup); ELSE → the unchanged name-based open (`TryOpenDdlForWord`).
  Non-INSERT positions (a table name, SELECT, a proc call) all fall through to the same name-based open, so the
  M4/M5 double-click behaviour is preserved. §0: read-only. The double-click gesture + popup render are manual
  smoke; the predicate + Core mapping are unit-pinned.
  <details><summary>original sprint plan</summary>
  - **Core: ZERO changes (reuse).** `SignatureHelpEngine.GetSignature(model, offset)` already returns, for
    an offset inside an INSERT column-list / VALUES paren, `SignatureInfo { Kind = SignatureKind.Insert,
    Label = target table, Parameters = ordered target columns (name+type+facts), ActiveParameter = the
    top-level comma index = the value's position }`. That IS the P6 data. The predicate is `sig is { Kind:
    SignatureKind.Insert, Parameters.Count: > 0 }`. (Nested parens / function calls in a value are handled —
    the engine uses innermost-enclosing-paren + top-level comma counting.)
  - **App: put it in `NavigationController` (single integration point — reuse, one source of truth).**
    `NavigationController.Attach` is already called at BOTH double-click sites (`SqlEditorBehavior.Attach`
    line ~40 + `MainWindow` line ~136) and already holds `Func<SemanticModel?> _model`, `OffsetAt(point)`,
    the shared `EditorPopups.PlaceAtCaret`, theme-brush helpers, and `_openByName` (the name-based open used
    by Ctrl+Click). So P6 belongs here, NOT in the two duplicated `DoubleTapped` handlers.
  - **Consolidate double-click (removes duplication).** Move the name-based double-click-open into
    `NavigationController` (subscribe to `editor.DoubleTapped` in `Attach`; add a `Func<int,bool>?` for the
    name-based open, or reuse `_openByName` with a word extracted via `SqlCompletionContext.GetWordAt`).
    One handler: `offset = OffsetAt(tap position)`; `sig = SignatureHelpEngine.GetSignature(model, offset)`;
    if `sig.Kind == Insert && Parameters.Count > 0` → show the P6 popup + `e.Handled = true`; ELSE → the
    existing name-based open (`TryOpenDdlForWord`) + `e.Handled` when it opened. Then DELETE the
    `editor.DoubleTapped += … TryOpenDdlForWord` closure in `SqlEditorBehavior.Attach` and the
    `OnSqlEditorDoubleTapped` handler in `MainWindow` (they move here). This is the clean path — avoids the
    `e.Handled`-ordering dance between two subscribers on the same event. **§0: read-only — P6 only reads +
    shows a popup; the name-based open is unchanged behavior.**
  - **Popup.** A small themed card: heading = `INSERT INTO <table>`, then the column list (one line each,
    `name : type`), the active column bold + `AccentBrush`. Reuse `EditorPopups.PlaceAtCaret`, a hit-test-
    invisible `Popup` (like the hover tooltip / P3 Quick Info), and theme tokens only. Consider a shared
    renderer if it's close to `QuickInfoView`'s member list — but a signature isn't a `QuickInfo`, so a tiny
    dedicated builder (StackPanel of TextBlocks) is fine; do NOT force it through `QuickInfoView`. Dismiss on
    caret move / Escape / type / window open (same pattern as the P3 popup).
  - **Tests.** The Core mapping is already pinned by `SignatureHelpEngineTests`; add a focused test that
    `GetSignature` at a VALUES offset yields `Kind == Insert` + the right `ActiveParameter` for the clicked
    position (if not already covered). The double-click gesture + popup are manual smoke (editor-gesture
    convention). Keep any P6 decision predicate pure/`internal static` so it's unit-testable without a window.
  - **Size / risk.** Medium: the consolidation touches the working M4/M5 double-click paths (regression-
    sensitive) + a new popup. Do it as ONE package: consolidate double-click → verify existing double-click-
    open still works → add the INSERT branch + popup → tests → build → smoke. **Start next session.**
  </details>

- **P2c — bold/highlight the typed fragment. ASSESSED (2026-07-11): not cleanly doable on AvaloniaEdit
  12.0.0 — DEFER (do not hack).** The `CompletionList`/`CompletionListBox` filters items and computes match
  quality internally and exposes NO per-item matched-range to `ICompletionData.Content` — so a clean bold
  needs either (a) a custom `CompletionListBox` item template fed the current filter text, or (b) a
  controller-side re-render of every visible item's `Content` on each filter keystroke (no clean AvaloniaEdit
  hook for "filter changed", and per-keystroke rebuild of all rows is the fragile path the design already
  flagged). Neither is a small clean change; both are their own follow-up. Lowest priority — do it only when
  it can be done without a hack (§0-adjacent directive).

- **P7 — ✅ DONE (2026-07-11).** (i) **Scope gating confirmed correct** — the binder creates a
  `RoutineBody` scope for BOTH a bare `BEGIN…END` (`BindAnonymousBlock`, the Easy-mode body editor) and an
  ad-hoc `EXECUTE BLOCK` (`BindExecuteBlock`), so PSQL control-flow snippets surface where needed (pinned:
  `SnippetEngineTests.InAnonymousBlockBody_OffersControlFlow` + `InsideExecuteBlockBody_OffersControlFlow`).
  (ii) The **2-char `if`** template was hidden by the ≥3-char identifier auto-trigger — fixed WITHOUT
  lowering the general threshold: new `SqlCompletionController.WordMayTriggerSnippet` lets the auto-popup
  fire when the word (≥2 chars) is a prefix of a snippet keyword valid at the caret (gated by applicability,
  so no new eagerness elsewhere); `OnAutoPopupTick` now lets ≥2-char words through the combined gate, and
  `ShowBaseline`'s gate is `ShouldAutoTrigger OR WordMayTriggerSnippet`. Pinned by
  `SnippetEngineTests.WordMayTriggerSnippet_*`. (iii) **Multi-word keyword** filtering (`for select` for a
  typed `for`) already works via AvaloniaEdit's StartsWith prefix match on `SnippetCompletionData.Text` —
  confirmed, no change. Live Tab-stop expansion is the user's visual smoke.

- **P8 — formatter polish** (large, its own package). `EXECUTE BLOCK` header on its own lines
  (`execute block` / `returns (…)` / `as`); `for select …` on one line (not `for` \n `select`); `INTO …` then
  `do begin` on new lines; **wrap long `INSERT`/`VALUES`/`SELECT`/function-call lines** — a real **max
  line-width** with intelligent wrapping is a headline goal (no horizontal scrolling of hundreds of chars).
  Build on the AST; aim to be **better** than IBExpert (which over-stretches), not a copy. Likely needs the
  parser deepened for INSERT/VALUES/SELECT-list clauses (deferred from Etap 3's "statement skeleton").

- **P9 — theme pass (dark + light equivalence). ✅ DONE conservatively (2026-07-11); final aesthetic = the
  user's live sign-off.** The two specifically-reported blur cases were fixed with **contrast-computed**
  (WCAG-ratio + HSL hue/lightness) values, not blind: **dark DML keyword** `#569CD6` → `#5A8AC8` (shifted to
  indigo + calmer saturation so it recedes as the base coat and stops blurring with the vivid table
  sky-blue — keyword↔table inter-contrast 1.47→1.78; objects keep the tree palette per §9.2, so the lever is
  the keyword, not the object); **light built-in function** `#7A5C1E` → `#8C6600` (a fully-saturated AA-safe
  gold — 4.71:1 vs bg — that reads clearly "coloured" instead of a muddy brown too close to near-black text).
  Both are lexical (XSHD) changes; the drift test only checks keyword *lists*, not hex, so it stays green.
  The remaining fine tuning (any further keyword/column/local hue separation) is deliberately left as the
  user's interactive visual pass — the design flags this repeatedly (§9.5) and Etap 1 already tuned the light
  lexical palette; a blind wholesale rewrite of a theme the user calls "good" would risk regressing it.

**Status (2026-07-11): P1–P9 done EXCEPT P8 (formatter) + two small backlog items.** Order delivered:
P1 → P2 → P3 → P7 + P4 → **P5 (consistency ✅) + P9 (theme, conservative ✅) → P6 (INSERT helper ✅)**.
**Remaining, NOT in the user's "close the phase" scope (P5+P9+P6):**
- **P8 — formatter polish** (EXECUTE BLOCK / FOR SELECT / INTO layout + max-line-width wrapping). Its own big
  package; likely needs the parser deepened for INSERT/VALUES/SELECT-list clauses. Deferred.
- **P5d — plain-hover cue without Ctrl** (a dwell-delayed, info-only Quick Info tooltip on plain hover;
  underline + hand-cursor stay Ctrl-only per §9.4). Implementable (add a tunable `HoverInfoDelay` +
  DispatcherTimer to `NavigationController`, schedule the tooltip on hover-dwell over a resolved reference)
  but deferred: it's a live-tuning UX addition the design defers to interactive judgment (dwell delay + noise
  need the user's eyes), and it isn't in the user's explicit P5 ask (consistency). Low risk, small.
- **P2c — bold typed fragment** in the completion list: **DEFERRED (not a hack, per directive).** Re-confirmed
  — AvaloniaEdit 12.0.0's `CompletionList` exposes no per-item matched-range to a custom `Content`; a clean
  bold needs a custom `CompletionListBox` item template or a per-keystroke re-render of every row (fragile).
  Its own clean follow-up when it can be done without a hack.

**The pure-aesthetic hex tuning (any further P9 hue separation) remains the user's live VISUAL pass** — the
colours shipped in P9 are contrast-computed conservative defaults; the final sign-off is interactive.

**Gotcha — promote to architecture lore.**

198. **`Scope.ScopeAt` (and any offset→scope/AST lookup driving completion) must be INCLUSIVE at the end of
a span.** A caret at the exact end of a statement/block/document is the single most common completion
position (`… where n.|`, `begin … |`), and half-open `[Start, End)` containment excludes it — so it resolves
to the enclosing (wrong) scope and loses the inner declarations (FROM aliases, PSQL variables), which reads
as "completion randomly stops working at the end of a line." Use `Start ≤ offset ≤ End`, and at a shared
boundary between two siblings prefer the later-starting one (the caret is at the start of the next
statement). This bug is invisible mid-statement, so verify offset→scope resolution specifically at
end-of-statement / end-of-text. **Corollary (P3, 2026-07-11): `SemanticModel.ReferenceAt` had the same
half-open bug** — a caret at the end of a fully-typed identifier (`nrdokwew|`) resolved to no reference, so
Quick-Info / go-to-def / P3 "show its facts" failed at the most common position. Made end-inclusive with a
shortest-span tie-break. Any offset→symbol/reference lookup driving an editor feature must be end-inclusive.

199. **AvaloniaEdit's `CompletionWindow.CloseOnFocusLost` is a `protected virtual` get-only property — to
stop the list dismissing when the user drags its OWN scrollbar, subclass and override it to `false`.** The
scrollbar-thumb drag opens the list as a separate popup window that deactivates the parent window;
AvaloniaEdit's `ParentWindow_Deactivated` → `CloseIfFocusLost` then closes the list mid-scroll (the reported
bug). There is no public setter, so the intended hook is a tiny `CompletionWindow` subclass overriding the
getter. This disables ONLY the focus-lost close path — `CloseAutomatically` (caret leaving `[Start,End]`),
Escape, item selection, and non-matching input still close it. Accepted trade-off: clicking a non-editor
control while the list is open leaves it open until the caret next moves. Reflect the real API surface
(get/set visibility) before assuming a property is settable — the member merely appearing in a dump doesn't
mean it has an accessible setter.

---

## §29 — Post-polish Bug Fix Sprint (final stabilization before Etap 7) — 2026-07-11

A stabilization sprint after the user's practical editor review. Worked in packages (build/test/smoke each).
Diagnose-first: every fix has a proven root cause, none is blind. **Etap 7 stays blocked until this sprint's
items are closed + verified.**

- **Package 1 — completion filtering + double-click crash (DONE, verified: build 0/0, 3401+13 green, smoke
  clean).**
  - **Task 1 (completion list unfiltered).** Root cause (confirmed against AvaloniaEdit 12.0.0 source):
    `CompletionWindow` filters the list ONLY on a subsequent `CaretPositionChanged`; a window opened with
    text already typed before the caret (`StartOffset < caret`, e.g. `n.nrdok|` + Ctrl+Space) shows the
    FULL list until the caret next moves — "looks like nothing was typed." Fix: `SqlCompletionController`
    `ApplyInitialFilter` calls `window.CompletionList.SelectItem(document.GetText(StartOffset, caret-Start))`
    right after `Show()`. Covers dot / baseline / column paths; no-op when nothing is typed. (gotcha #200)
  - **Task 2 (`VisualLinesInvalidException` on double-click).** Root cause: `EditorPopups.TryGetCaretRect`
    read `tv.VisualLines` OUTSIDE its try; `VisualLines` throws when `!VisualLinesValid` (a re-measure is
    pending — a double-click just changed the selection, or a just-activated tab hasn't laid out). Fix:
    guard `VisualLinesValid` + `EnsureVisualLines()` (catch the documented mid-Measure `InvalidOperationException`
    → fall back to Center placement) BEFORE touching `VisualLines`. (gotcha #201)

- **Package 2 — Semantic Model freshness / FROM view + FROM proc(…) (DONE, verified: build 0/0, 3403+13
  green, smoke clean).**
  - **Task 3.** Proven NOT a binder bug: two headless tests (`Select_FromView_ViewIsObject`,
    `Select_FromSelectableProc_ProcIsObject`) show the binder records the schema-object reference for a view
    / selectable proc in FROM *given the metadata*. Real cause = **model staleness**: the model is built
    once (on the debounced text-set) against whatever categories had prefetched THEN (categories load
    sequentially, Tables first), and is never rebuilt when Views/Procedures finish loading → they never
    resolve for highlight/Ctrl-nav/Quick-Info. Fix: `MetadataExplorerViewModel.ObjectsChanged` raised at the
    `LoadGroupAsync` choke point (covers prefetch/expand/refresh) → `EditorLanguageService.NotifyMetadataChanged()`
    coalesces (200 ms) → `RefreshModelWithMetadata()` → `ModelUpdated` → repaint + fresh nav model. The
    subscription is scoped to the editor's **visual-tree lifetime** (Attach/DetachFromVisualTree) because
    `SqlCompletionController.Detach()` is never called, so a raw subscription to the long-lived `Metadata`
    singleton would leak the editor. (gotcha #202)

- **Package 3 — highlighting delay + editor focus (DONE, verified: build 0/0, 3403+13 green, smoke clean).**
  - **Task 5 (semantic colours pop in ~300 ms after open/tab-switch).** Cause: model built lazily on the
    300 ms debounce after the text-set. Fix: `EditorLanguageService.OnTextChanged` builds the model
    IMMEDIATELY (synchronously) on the first text-set OR a **wholesale replace** (length delta > 20 —
    tab/saved-query switch, paste), so highlighting is ready at first paint; a single keystroke still
    debounces. The Package-2 metadata refresh also repaints once objects load.
  - **Task 6 (new SQL tab → focus editor).** `MainWindowViewModel.EditorFocusRequested` raised in
    `NewQuery()`; `MainWindow` focuses `_editor.TextArea` (posted at Background priority, after the empty
    query text is pushed) so the user types immediately without a click.

- **Package 4 — light-theme completion popup bg + comment contrast (EDITS COMPLETE, VERIFICATION BLOCKED by
  a transient build-tool outage — must re-run build/test/smoke next session before calling done).**
  - **Task 7.** No completion-popup styling existed → AvaloniaEdit's default left the popup the same colour
    as the editor (indistinguishable in Light). Added `ControlStyles.axaml` `aecc|CompletionList` style
    (`ElevatedPanelBrush` background + `BorderBrush` 1px border; included after AvaloniaEdit's theme so the
    Style setter wins). Structural fix; **final visual confirmation is the user's live pass** (§9.5) — if the
    `CompletionList` template doesn't honour `TemplateBinding Background` in 12.0.0 it may need to target the
    inner `PART_ListBox` instead.
  - **Task 8.** Dark comment `#6A9955` → `#7FB86B` (contrast on `#1E1E1E` ~5:1 → ~7.7:1; the user is on the
    Dark theme per the screenshots). Light comment `#3F6B1F` left (already high-contrast dark-on-light).
    Measured, not blind, but **visual-taste — the user's live pass decides the final value** (§9.5).

- **Package 5 — Quick Info richness (Task 4): DIAGNOSED, DEFERRED (not started — a DB-touching, FB-version-
  sensitive change that needs lab-DB verification; deferred per the "don't start a package you can't finish
  + verify" rule while the build tool was unavailable).**
  - Root cause CONFIRMED: `QuickInfoEngine` already consumes the full model richly (a column card can show
    type/table/domain/nullability/default/description/PK/FK→table/identity/computed; tables → column members;
    routines → params). The info is thin because the **snapshot is impoverished**: `AppMetadataSnapshot.GetColumns`
    maps only `Name/Type/Domain/Nullable`, and `ColumnSpec` + `FirebirdMetadataReader.ColumnsSql` don't carry
    PK/FK/default/description/computed/identity (kept lean for the completion hot path in the P2 milestone).
    `ColumnMetadata` already HAS all those fields — only the population is missing.
  - **Plan (next session):** enrich `ColumnSpec` with the missing fields; extend `ColumnsSql` with the PK/FK
    correlated subqueries + `RDB$DEFAULT_SOURCE`/`RDB$DESCRIPTION`/`RDB$COMPUTED_SOURCE`/`RDB$IDENTITY_TYPE`
    (**FB-version-gate identity — `RDB$IDENTITY_TYPE` is FB3+, gotcha #146**, exactly like `FirebirdTableDetailReader.FieldsSql`);
    map them in `AppMetadataSnapshot.GetColumns`. Verify against `Lab/EmberTern_Lab.fdb` (per the Lab rule).
    The completion `: TYPE : DOMAIN` row is unaffected; the detail pane + Ctrl-hover card gain the extra facts.
    Consider reusing `FirebirdTableDetailReader.GetFieldsAsync` (already returns full `FieldInfo`) rather than
    duplicating the SQL, OR a separate on-demand rich-column fetch so the completion column cache stays lean.

**New gotchas from this sprint (also mirrored into CLAUDE.md):**

200. **AvaloniaEdit's `CompletionWindow` does NOT apply an initial filter on `Show()` — it filters only on a
subsequent `CaretPositionChanged`.** A window opened with a prefix already typed before the caret
(`StartOffset < caret`) shows the entire list until the caret next moves. Always call
`window.CompletionList.SelectItem(document.GetText(StartOffset, caret - StartOffset))` right after `Show()`
to apply the initial filter (`SelectItem` is public, `IsFiltering` defaults true, and it templates itself if
the listbox isn't realized yet). Verified against the 12.0.0 source; do not assume the control self-filters.

201. **`TextView.VisualLines` THROWS `VisualLinesInvalidException` when `!VisualLinesValid` (a re-measure is
pending).** Never touch `VisualLines` (even `.Count`) without first checking `VisualLinesValid`. A
double-click (selection change) or a just-activated/not-yet-laid-out editor leaves the lines invalid, so any
caret-rect/popup-placement helper must guard `VisualLinesValid`, optionally `EnsureVisualLines()` (public,
but throws `InvalidOperationException` if called mid-Measure — catch it and fall back), and only then read
`VisualLines`. `GetVisualPosition` does not throw on invalid lines but returns garbage, so guard there too.

202. **An editor feature that must react to the App's metadata loading (prefetch/expand/refresh) needs a
metadata-changed signal + a rebuild — and the subscription must be scoped to the editor's visual-tree
lifetime, not a raw `+=` on a long-lived singleton.** The semantic model is built once on the text-set and
never refreshed on metadata growth, so objects whose category prefetches AFTER the editor opened (views,
selectable procedures in FROM) never resolve. Raise a coalesced `ObjectsChanged` at the single
`LoadGroupAsync` choke point and rebuild the model (`RefreshModelWithMetadata` → `ModelUpdated` → repaint +
fresh nav). Because `SqlCompletionController.Detach()` is never called, subscribe on
`AttachedToVisualTree` / unsubscribe on `DetachedFromVisualTree` (use `Control.IsLoaded` for the
"already attached at ctor" case — gotcha #95; the `GetVisualRoot()` extension didn't resolve on `TextEditor`)
so the subscription to `Metadata` can't leak the editor.

---

## 30. UX Polish — Final Completion sprint (shipped 2026-07-12)

Closed the three still-open review items from the Editor UX Polish phase. Entry cost after the
Documentation Cleanup Sprint was much lower — CLAUDE.md carried the architecture pointers, the editor
status, and the load-bearing gotchas; only a couple of targeted `docs/gotchas.md` lookups were needed
(no history files). Build 0/0, tests **3410 main + 14 probe** green, smoke clean.

**1. Light-theme completion popup blends into the editor (root cause + real fix — gotcha #203).**
The earlier fix styled `aecc|CompletionList` Background, which is a **visual no-op**: AvaloniaEdit's
`CompletionList` control template (verified against the 12.0.0 `CompletionList.xaml`) is *only* the inner
`CompletionListBox` — it never paints its own Background. The visible surface is the `CompletionListBox`
(a Fluent `ListBox` via `BasedOn`), which paints the Fluent default ListBox background — near-white in
Light, hence the blend. Fix: style **`aecc|CompletionListBox`** (Background `ElevatedPanelBrush` + border)
in `ControlStyles.axaml`; an app-level `Style` setter outranks the Fluent ListBox `ControlTheme` default.

**2. Double-click INSERT/VALUES helper shows no popup (root cause + fix — gotcha #204).**
The crash was already fixed (gotcha #201); the popup still never appeared because
`SignatureHelpEngine.GetSignature` builds the INSERT signature from `meta.GetColumns(table)`, and on a
fresh editor the target table's columns aren't cached → it returns `null` → `IsInsertHelperSignature` is
false. The double-click path never warmed the columns (unlike dot-completion / typed signature help). Fix:
new `SignatureHelpEngine.TryGetInsertTargetTable` (returns the target table even with no columns) +
`SqlCompletionController.WarmColumnsAndRebuildAsync`; `NavigationController.OnDoubleTapped` now warms the
columns, rebuilds the shared model, and retries the signature before falling back to name-based open. New
`SignatureHelpEngineTests` pin `TryGetInsertTargetTable` (incl. the no-columns-cached case, column-list vs
nested-function-arg, INSERT…SELECT, and non-insert positions).

**3. Semantic Model — views / selectable procedures in FROM don't resolve (real root cause was NOT the
binder — gotcha #205).** The binder resolves both a view and `proc(...)` in FROM perfectly *given the
metadata* — pinned by `SemanticHighlightConsistencyTests` and the new deterministic probe
`ConnectionExpandBindingProbe.EditorModel_RebuildsAgainstGrownMetadata` (empty snapshot → the FROM name is
only an unresolved `TableReferenceSymbol`; after `RefreshModelWithMetadata` against a grown snapshot it
resolves as a `SchemaObjectSymbol` View/Procedure). The real defect was in the LIVE rebuild trigger: the
main window's `ObjectsChanged` subscription went through the completion controller's attach-time hook whose
lambda guarded `if (_currentVm is not null)`, while `SubscribeMetadataChanged` latched
`_metadataSubscribed = true` regardless — so if `_currentVm` was null at attach the handler was never
attached and never retried. Since categories prefetch sequentially (Tables first, Views/Procedures **last**),
the model kept only the early categories and was never rebuilt when the late ones loaded → the exact
"tables work, views/procs don't" symptom. Fix: added public
`SqlCompletionController.NotifyMetadataChanged()` and wired the main SQL editor's metadata subscription to
the **stable VM** in `MainWindow.OnDataContextChanged` (guaranteed non-null, outlives the window), removing
the fragile attach-time hook for the main editor. Detail editors (via `SqlEditorBehavior`) already
subscribed directly to `vm.Metadata.ObjectsChanged` with no null-guard, so they were unaffected.

**Verification of prior fixes (item 4).** Full suite green (completion-after-alias, Ctrl+Space, Quick Info,
navigation, semantic highlighting, popup init-filter, VisualLines guards, no coloring delay are covered by
`CompletionEngineTests` / `NavigationEngineTests` / `QuickInfoEngineTests` / `SemanticHighlight*Tests` /
`SqlCompletionDataMapTests` + the headless probes) — no regressions.

**Deliberately NOT started:** P8 (formatter polish), Etap 7 (diagnostics/folding/breadcrumbs/bracket-match),
P5d, P2c, and Package 5 (Quick Info richness — `ColumnSpec`/`ColumnsSql` PK/FK/default/… fields) remain
deferred per the session scope.

---

## 31. UX Polish — QA Fix Sprint (2026-07-12, follow-up)

Practical re-review found the §30 "fixes" for the INSERT helper and the view/proc highlighting still didn't
work in the running app. New working rule adopted (user directive): a package is NOT "fixed" on green
build/tests/smoke alone — if it can't be verified visually, report "implementation done — awaits user
confirmation", not "fixed". This sprint traced each flow to ground truth with headless probes rather than
guessing. Build 0/0, tests **3410 main + 16 probe** green.

**1. INSERT/VALUES helper — real root cause found (gotcha #206).** Refactored `NavigationController`'s
double-click into a testable seam `TryHandleDoubleClick(offset)` (the `DoubleTapped` handler and the test
call the SAME code) + `InsertHelperIsOpen`. New probe `InsertHelper_DoubleClick_OpensPopup_CachedAndWarmed`
drives the REAL decision→popup path and proves the popup OPENS for both the cached-columns case and the
warm-then-retry case — so the flow is correct **given a good offset**. The live failure was the OFFSET:
`OnDoubleTapped` read `_editor.CaretOffset`, but on a real double-tap the caret isn't reliably placed at the
clicked value when the gesture fires, so `GetSignature` ran at a stale offset → no signature → no popup. Fix:
derive the offset from the pointer (`OffsetAt(e.GetPosition(_editor))`, the Ctrl-hover mapping) with a caret
fallback. (The event fires fine — the earlier crash proved the handler ran — so no `handledEventsToo`.) The
pointer-offset path itself is not unit-tested (needs a real gesture) → awaits user confirmation.

**2. View / selectable procedure highlighting — PROVEN not the highlighter, not the binder (gotchas
#207, #205).** The user asked to prove the cause before touching the binder, suspecting the highlighter.
Three probes settle it: (a) `EditorModel_RebuildsAgainstGrownMetadata` — the binder resolves view+proc in
FROM given metadata; (b) `SemanticHighlighter_BareFromView_PaintsObjectColour_NotLocal` — via the new
`SemanticHighlighter.PaintedBrushAt` seam, a resolved bare-name `FROM view` is painted `IconColor_View`
(and `FROM proc(...)` → `IconColor_Procedure`), i.e. the object class wins the overlap with the implicit
table-reference "local" brush; (c) AvaloniaEdit `TextView.Redraw()` = `ClearVisualLines()`+
`InvalidateMeasure()`, so the `ModelUpdated`→repaint genuinely re-runs the colorizer. Key insight: "Ctrl+Click
works" does NOT prove the model resolved — `NavigationController` has a name-based fallback (`_openByName`),
so the whole symptom set (no colour + no Ctrl-hover + no Quick Info, yet Ctrl+Click opens the object) is the
fingerprint of a model that did NOT resolve the object, i.e. the metadata wasn't in the snapshot at
model-build time. That is the §30 model-rebuild concern (gotcha #205), whose every link was re-verified this
sprint (`DataContextChanged`→`OnDataContextChanged`→`Metadata.ObjectsChanged`→`NotifyMetadataChanged`→200 ms
debounce→`RefreshModelWithMetadata`→fresh snapshot→`ModelUpdated`→repaint). Because the live failure could
not be reproduced headlessly despite every link checking out, this remains **awaits user confirmation** — if
it persists on a clean rebuild, the next step is runtime tracing of the snapshot object-count at model build.

**3. Package 5 (rich Quick Info) — consciously NOT started** (budget; must not be half-implemented).

---

## 32. QA Fix Sprint round 2 — runtime diagnostics + the real multi-statement root cause (2026-07-12)

User verified round 1 still failed live and asked for **runtime instrumentation** (no more hypotheses/unit
tests) to show the actual flow. Added `App/Diagnostics/EditorDiagnostics.cs` (ON by default, silence with
`EMBERTERN_EDITOR_DIAG=0`; writes to `%TEMP%\EmberTern-debug.log`) tracing: `[INSERT]` the whole
double-click path (pointer vs caret offset → `GetSignature` → helper-shown/warm/fallback → popup
IsOpen+placement+childBounds); `[HL]` on each model rebuild the parser statement count + every statement's
kind/span + every reference's Symbol→Classification→brushKey→brushResolved; `[META]` the full rebuild chain
with snapshot object-count; `[NAV]` whether Ctrl+Click used the model or the name fallback. Verified the
pipeline writes (startup `[INIT]` line).

**Real root cause of "only the first statement is analysed" (gotcha #208).** The user's editor held several
statements separated only by newlines, **no `;`**. `SqlParser.ScanPlain` ends a plain statement at the next
top-level `;`; with none it runs to EOF, so `Classify` typed the WHOLE document as one statement by its
first keyword (`EXECUTE PROCEDURE …`) → the binder resolved only that first statement's objects; everything
after read as plain text. This was NOT "the model stops after statement 1" — it's "the parser returned ONE
statement". Fix: a **lenient** parse for the READ-ONLY semantic model only (`SqlParser.Parse(text,
lenient:true)`, wired into `SemanticModel.Build(string)`) that also splits at top-level statement-start
keywords when `;` is absent, paren+BEGIN/CASE/END-depth aware, with continuation guards for `WITH … SELECT`
/ `INSERT … SELECT` / `… UNION SELECT` / `CREATE VIEW … AS SELECT` / `MERGE … WHEN … THEN …`. The strict
`;`-only `SqlParser.Parse` (executor boundary authority, gotcha #192) is untouched. Pinned by
`SemanticModelTests.MultipleStatements_WithoutSemicolons_AreEachAnalysed` (4 statements, later ones resolve)
+ `InsertSelect_WithoutSemicolon_StaysOneStatement` (no false split). Full suite 3412 + 16 green.

**INSERT helper.** Fixed a real bug the pointer-offset change had introduced: `WarmAndShowInsertHelperAsync`
guarded on `_editor.CaretOffset != offset`, but the offset is now the POINTER position (≠ caret), so the
warm-retry always aborted → no popup for uncached columns. Replaced with a generation counter
(`_insertWarmGen`). Increased the active-column contrast in the helper card: the active row now gets a solid
`AccentBrush` pill with `OnAccentBrush` text (bold accent text alone was too faint — user feedback).

**Round 3 — the `[INSERT]` log settled the popup-not-appearing question.** The trace of five real
double-clicks on VALUES showed the whole path is CORRECT: `DoubleTapped` arrives with the right pointer
offset, `GetSignature => Kind=Insert params=5`, `IsInsertHelperSignature=true → ShowInsertHelper`,
`IsOpen=True childBounds=0,0,173.6,114.4` (content laid out). So the popup opens with real content — the
problem is purely that it isn't SEEN. The two remaining possibilities are (a) it is dismissed a tick after
opening by the double-click's own caret settle (`OnCaretMoved` fires with a caret ≠ the just-captured
`_insertHelperCaret`), or (b) it renders off-screen. Fix for (a) applied: **disarm caret-move dismissal
until the gesture settles** — `_insertHelperCaret = int.MinValue` at open, then armed on a Background
dispatcher tick; `OnCaretMoved` ignores the disarmed state. Added `[INSERT] post-open:` logging that
reports STILL-OPEN (+ placementRect/childBounds/visible/opacity) vs ALREADY-CLOSED, plus `HideInsertHelper`
+ `OnCaretMoved dismiss` lines — so the next run pins (a) vs (b) definitively. If (b), switch the helper
(and the other `new Popup + SetParent` popups) to the editor's `OverlayLayer`.

**Round 4 — the popup was (b): rendered-but-invisible. Fixed via OverlayLayer (gotcha #209).** The next
log settled it: every double-click showed `ShowInsertHelper … IsOpen=True childBounds=0,0,205.6,358.4` and
`post-open: STILL OPEN … childVisible=True childOpacity=1`, staying open ~1–2 s until the NEXT double-click
moved the caret. So the popup was genuinely alive and "visible" by every property — it just never appeared
on the real desktop. That is a bare-`Popup`/`PopupRoot` hosting failure (the separate top-level renders
off-screen/behind/clipped). Fix: the INSERT helper is now hosted in the editor's **`OverlayLayer`**
(`OverlayLayer.GetOverlayLayer(_editor)`, a Canvas; card positioned via `Canvas.SetLeft/SetTop` in overlay
coords from `_editor.TranslatePoint(caretBottom, overlay)`, clamped inside the overlay — flips above the
caret when a wide INSERT's card would overflow the bottom). The `InsertHelper_DoubleClick_*` probe passes
against the overlay host (so `GetOverlayLayer` resolves headlessly). Also (user feedback): occurrence
highlighting recoloured — the faint 20 %-amber fill + BLUE `AccentBrush` outline read like a text
selection; now a stronger gold fill (`#4DE0A81E`) + a SOLID gold border (new
`OccurrenceHighlightBorderBrush`, 1.4 px) in both themes, clearly distinct from the blue selection.

**Round 5 — the helper auto-closed ~1 s after opening: context-driven lifetime (gotcha #210).** Once
visible, the OverlayLayer card vanished on its own. The `[INSERT]` log proved (a) it was NOT the model
rebuild (verified in AvaloniaEdit `Caret.cs`: `TextView_VisualLinesChanged` only calls
`Show()`+`InvalidateVisualColumn()`, never `RaisePositionChanged`), and (b) every dismissal was
`reason=caret-moved` with the caret drifting 1-3 chars AFTER the double-click (`armedAt=375 →
caretMoved=377`) — AvaloniaEdit settling the word-selection caret to the token boundary, still inside the
same VALUES argument. The user correctly identified that tying the helper to a raw `CaretOffset` is the
wrong model. Rewrote the lifetime to be CONTEXT-driven (VS/Rider/IBExpert style): `OnCaretMoved` →
`UpdateInsertHelperForCaret` re-queries `SignatureHelpEngine.GetSignature(model, caret)` and keeps the
helper open while it is still an INSERT signature for the SAME target `Label`, rebuilding the card to follow
the active argument; it closes only when the context changes (engine returns null/other kind, another
statement/table, Escape, detach). Removed all `_insertHelperCaret`/`armedAt` offset logic. Pinned by
extending `InsertHelper_DoubleClick_*` (caret jitter + argument change keep it open; leaving the INSERT
closes it). ALL temporary runtime diagnostics removed (`EditorDiagnostics.cs` deleted + every
`[INSERT]/[HL]/[META]/[NAV]/[INIT]` call stripped across `NavigationController`, `SemanticHighlighter`,
`EditorLanguageService`, `MainWindow`, `MetadataExplorerViewModel`) — code back to a clean state. Build
0/0, tests 3412 + 16 green, smoke clean.

---

## 33. Parameter Helper — unified across constructs (2026-07-12, final Parameter-Helper sprint)

User accepted the context-driven INSERT helper and asked to close the whole Parameter Helper topic:
extend the SAME mechanism to every construct where the user fills a parameter list, as ONE helper whose
source of truth stays `SignatureHelpEngine` (UI = presentation only), no separate per-construct helpers.

Extracted **`ParameterHelper`** (App/Completion) from the INSERT-only code: an OverlayLayer-hosted card,
generalised over all `SignatureKind`s — INSERT / UPDATE-OR-INSERT column↔value, EXECUTE PROCEDURE /
selectable-proc / function arguments — with a kind-appropriate heading, the active parameter a solid
accent pill, and an IN/OUT tag for routine parameters. It owns the context-driven lifetime (its own
caret subscription; re-queries the engine, stays open while the caret is still at the SAME site
kind+target, follows the active argument, closes on a real context change / Escape / detach) and the
warm-then-retry (routine params for a proc/function, columns for INSERT/UPDATE) via a single delegate.

Wiring: the `SqlCompletionController` OWNS the one `ParameterHelper` (it has the model + warm delegates).
Both triggers feed it — typing `(`/`,`/`)` + Ctrl+Shift+Space call it directly; the double-click in
`NavigationController` delegates via `TryShowParameterHelperAt`. The old M7 `OverloadInsightWindow`
signature popup (`ShowSignaturePopup`/`SingleSignatureProvider`/`BuildSignatureHeader/Content`) is
DELETED — one helper now, not two. Engine change: `SignatureHelpEngine` treats
`StatementKind.UpdateOrInsert` like `Insert` for the VALUES/column paren (priority #1). All the
INSERT-specific card/lifetime/warm code was removed from `NavigationController`; the obsolete
`InsertHelperPredicateTests` deleted; the double-click probe rewritten to drive `ParameterHelper.ShowAt`
directly across INSERT (cached + warm + lifetime) + UPDATE OR INSERT + EXECUTE PROCEDURE, plus two
`SignatureHelpEngineTests` pinning the UPDATE-OR-INSERT engine path. Build 0/0, tests 3408 + 16 green,
smoke clean.

---

## Stage 7 — Diagnostics

Design/vision: [`docs/design/editor-stage7-diagnostics.md`](../design/editor-stage7-diagnostics.md).
Stage 7 turns the deepened AST + Semantic Model (Etap 6.9) into user-visible diagnostics: a thin,
pure-Core analysis engine that emits conservative findings, rendered/panelled/navigated by thin App
clients on the *existing* cached-model / `ModelUpdated` cycle — no second parse loop.

### S1 + S2 + S6 — Core diagnostics engine (commit c3a269d)

`DiagnosticsEngine` (`Core.Sql.Language.DiagnosticsEngine`) is a pure-Core client of `SemanticModel`:
given a built model it returns a conservative, de-duplicated, deterministically-ordered
`IReadOnlyList<Diagnostic>` in one forward pass over `References` plus bounded AST checks (per-INSERT
count, SUSPEND context). Zero Avalonia, offline, unit-tested. It computes nothing structural — the
parser is the single structural source and the binder already resolved every occurrence, so diagnostics
are a *filter over existing data*. Codes `ET0001`–`ET0008`: UnknownObject, UnknownColumn,
UnresolvedVariable, UnresolvedParameter, AmbiguousColumn, InsertCountMismatch, UnknownCursor,
SuspendOutsideSelectable. Object/column categories are gated on a live metadata snapshot (with no
connection every object is unresolved by construction, so flagging would be pure noise). The binder was
extended once with general cursor-usage modelling (`ReferenceRole.Cursor`) so `UnknownCursor` is a pure
filter, not engine-side re-recognition. Tests: 41 `DiagnosticsEngineTests` + 3 `CursorUsageBindingTests`.

### S3 — Squiggle rendering (App) — DONE (impl); awaits user visual confirmation

The first *visual* Stage 7 milestone: connect the finished Core engine to the editor and draw
underlines, refreshing automatically. Scope was deliberately squiggles-only — no panel, no navigation,
**no hover tooltip** (the design doc had listed hover under S3; the user scoped it out — the message
surface pairs with the panel in a later milestone).

**Data flow (unchanged pipeline, one added step):**

```
text → Lexer → Parser → AST → SemanticModel → DiagnosticsEngine.Analyze → SquiggleRenderer
```

`EditorLanguageService` already built + cached the `SemanticModel` on a debounced, cancellable,
off-thread pass, raising `ModelUpdated`. Stage 7 adds exactly one step to that cycle: after the model is
built, run `DiagnosticsEngine.Analyze(model, ct)` and cache an `IReadOnlyList<Diagnostic>` alongside the
model. Done in all three model-build sites — inside the same `Task.Run` as the background reparse (so a
newer edit cancels the in-flight analysis via the existing `CancellationTokenSource`, and the token
propagates into the engine), and on the two synchronous rebuild paths (`EnsureFreshModel`,
`RefreshModelWithMetadata` — deliberate triggers + metadata warm/refresh). The cached list is always
version-matched to the cached model; a warm-driven rebuild (unknown object → resolved after its category
loads) re-analyses, so squiggles clear as metadata fills in. Exposed via
`SqlCompletionController.Diagnostics`.

`SquiggleRenderer` (`Completion/SquiggleRenderer.cs`) is an `IBackgroundRenderer` mirroring
`OccurrenceHighlighter`/`SearchMatchHighlighter`: attached once in `SqlEditorBehavior.Attach` (so every
SQL surface — SQL Editor + object editors — gets it at once), it repaints on `ModelUpdated`
(`InvalidateVisual`) and, on the paint path, does *zero* analysis — it reads the cached diagnostics and
draws a triangle-wave underline under each span using a severity theme brush (Error → `ErrorBrush`,
Warning → `WarningBrush`, Info → `SubtleForegroundBrush`; both light/dark, no hardcoded colours). The
paint loop is viewport-culled (bounds cost to on-screen diagnostics on a large script) and doc-clamped
(a paint that lands a hair ahead of the next rebuild never draws past the text — the staleness guard).
The renderer consumes only `Diagnostic.Start`/`Length`/`Severity`; it never interprets SQL or the AST.

Consistent with §9: no parallel analyses (diagnostics ride the single model pass; stale ones are
cancelled with their model), and only the current document is refreshed (each editor owns its own
service + renderer). Following the project convention that renderers are proven by *visual*
confirmation (neither `SemanticHighlighter` nor `OccurrenceHighlighter` has an App unit test — the
analysis they render is what carries the automated coverage), S3 adds no headless paint test; the
`DiagnosticsEngine` behaviour is already pinned by 44 Core tests. Build 0/0, 4114 main + 23 probe green,
smoke clean. **Visual QA (single/multiple/adjacent errors, fast typing + deletion, no flicker, squiggle
removal on fix, tab switch, connection change, large scripts) awaits the user** per the QA rule.

#### S3 QA finding + follow-up — Easy-mode ambient refresh

A manual QA pass reported that in an Easy-mode routine editor, local variables / input parameters appeared
as unresolved (squiggled). The user asked the right question first: does the model analyse the full
generated PSQL source or only the visible Easy-mode fragment?

**Investigation (empirical, not inferred).** The Easy-mode body editor holds only the BODY; the routine's
params + `DECLARE`d variables live in the surrounding grids and reach the model as *ambient symbols* seeded
into the root scope (gotcha #218 — the same seam completion/highlighting/navigation use). The binder's
`scope.Resolve` walks the body scope up to the root, so a seeded param/variable resolves. Feeding the same
body fragment three ways proved the mechanism is sound: **no ambient → 5 false positives; params + variables
→ 0; params only (matching the screenshot's `Variables (0)`) → only the genuinely-undeclared variables
flagged, params resolve.** So the engine, the binder, and the ambient mechanism are all correct — analysing
*fragment + ambient* is the right design (a synthesized full `CREATE PROCEDURE` source would need offset
translation back into the fragment, a second code path the ambient design exists to avoid). Decision (user):
keep fragment + ambient; do not investigate the body→grid split (no evidence of a parse bug — `Variables (0)`
can simply mean the object has no declared variables).

**The real bug (UX).** The investigation surfaced a genuine gap now made visible by squiggles: **editing the
Easy-mode grids did not rebuild the model.** The grids' `CollectionChanged` handlers only refreshed tab
headers + command state. So after adding `test` to the Variables grid, the `:test` squiggle lingered until
the user next edited the body text (which bumps the version and re-captures ambient). The user scoped fixing
this as *completing S3* (not S4): the model must rebuild when the ambient set changes.

**Fix.** `SourceObjectDetailTabViewModel` gained an `AmbientSymbolsChanged` event and a `TrackAmbient`
helper (a mirror of the existing `TrackDirty` — observe collection add/remove/reorder + each row's
`PropertyChanged`, but scoped to the `Name` property, the only one that affects symbol resolution). The base
tracks the shared Variables grid; `ProcedureDetailTabViewModel` additionally tracks Input/Output params and
`FunctionDetailTabViewModel` tracks Arguments (the collections that feed each `BuildAmbientSymbols`). A new
view-layer bridge `AmbientModelRefresh` (owned by each detail view) tracks the ambient-seeded editors'
controllers and, following the reused view onto its current VM, forwards `AmbientSymbolsChanged` to each
controller's new `SqlCompletionController.NotifyAmbientSymbolsChanged()` — which schedules the existing
debounced `RefreshModelWithMetadata` rebuild (that already re-captures ambient via the live delegate). So a
grid edit now rebuilds the model once (debounced — a name typed char-by-char coalesces), and **all**
SemanticModel consumers (diagnostics, completion, highlighting, Quick Info) refresh together, no body-text
edit required. Because the ambient delegate reads the grids live at each build, this also makes the initial
model robust to load order. Pinned by `EasyModeAmbientRefreshTests` (add/rename raises the event; a non-Name
row edit does not) + `EasyModeDiagnosticsTests` (ambient params/vars suppress the false positives). Build
0/0, 4122 main + 23 probe green, smoke clean. **Manual visual verification awaits the user; on confirmation
S3 is complete and committed.**

---

### S3 gap — the main SQL Editor never got squiggles (found while preparing S4; FIXED)

Preparing S4 surfaced a defect in shipped S3: **the single most-used SQL surface rendered no squiggles at
all.** S3 attached `SquiggleRenderer` in `SqlEditorBehavior.Attach` and its comment (and commit message)
claimed that covered "every SQL surface". It does not. `SqlEditorBehavior.Attach` is the shared installer
for the *object* editors; the **main SQL Editor is not one of its callers** — `MainWindow` hand-wires its
own `SqlCompletionController` and then attaches `SemanticHighlighter`, `NavigationController`,
`OccurrenceHighlighter` and `EditorSearch` one by one (its callbacks differ: null-safe `_currentVm?.…`
rather than a stable `vm`). So the renderer was simply never installed there. The *diagnostics* were being
computed for that editor all along — `EditorLanguageService` runs the engine on its background pass
regardless — only the paint was missing. This is also why the S3 manual QA pass, which ran in an Easy-mode
procedure editor, found the ambient-refresh bug but not this one.

**Root cause: duplicated wiring.** `MainWindow` is a parallel copy of `SqlEditorBehavior.Attach`, so every
new editor capability misses the main editor unless someone remembers both places. **Fix (user decision):
minimal.** Attach `SquiggleRenderer` in `MainWindow` next to the other renderers it already wires by hand,
and correct the false "every SQL surface" comment in `SqlEditorBehavior`. Consolidating the two wiring
paths is the real fix but is a refactor of its own and was explicitly kept out of Stage 7 — it gets a
dedicated milestone. Committed separately from S4 (`fix(diagnostics): render squiggles in the main SQL
Editor (S3 gap)`); build 0/0, 4122 main + 23 probe green. **With this, S3 is genuinely complete.**

> Lesson (gotcha #219): "wired in the one shared seam ⇒ every surface has it" is only true if every surface
> actually goes through that seam. `MainWindow`'s editor does not. Verify the call sites, don't trust the
> comment — and when adding an editor capability, add it in BOTH places until the duplication is retired.

### S4 — Diagnostics panel (App) — DONE (impl); awaits user visual confirmation

The panel is **only a view** of what the engine already produced. Scope was deliberately narrow (user
directive): list the findings, nothing else — no navigation (S5), no next/previous, no Quick Fix, no light
bulb, no hover, no code actions, no filtering, no grouping.

**Host — the SQL Editor's bottom tab strip only** (user decision). It joins Results / Messages / Output /
Performance as a fifth `bottom-tab`, gated on the existing `IsQueryTabActive`, so "the active document" is
unambiguous: the one `SqlEditor`. The object-detail editors were considered and deferred — each detail tab
hosts *four* editors (source / body / cursor / subprogram), so a panel there must first decide which one is
"the document" (focus-following), which is S5-shaped work and pure scope creep for S4. It is appended
**last** in the tab strip on purpose: `SelectedBottomTabIndex` is persisted in `WorkspaceState` and
`PerformanceBottomTabIndex = 3` is a hard-coded constant, so Results=0 / Messages=1 / Output=2 /
Performance=3 had to keep their positions.

**Data flow — one subscription, zero analysis.**
`DiagnosticsEngine → EditorLanguageService (cached, version-matched) → SqlCompletionController.Diagnostics
→ DiagnosticsPanelBinder → DiagnosticsPanelViewModel → DiagnosticsPanelView`. The binder subscribes to the
shared `SqlCompletionController.ModelUpdated` — the same signal the semantic highlighter and the squiggle
renderer ride — and simply *reads* the already-computed list. No second parse, no second model, no second
analysis, no new loop. Every refresh requirement falls out of that one subscription: a text edit and a
metadata-generation bump rebuild the model, and an Easy-mode ambient-symbol change routes through
`NotifyAmbientSymbolsChanged()` into the very same rebuild. Switching workspace tabs needs no work at all —
the whole bottom panel is already gated on `IsQueryTabActive`.

**Classes.** `DiagnosticRowViewModel` (App/ViewModels) — a read-only projection of one Core `Diagnostic`
following the established `SessionWarningViewModel` / `FindingViewModel` card shape: it keeps the source
record whole (so S5 can jump to its span without a second projection) and exposes display facets plus a
theme brush **key** (`SeverityBrushKey`), never a brush — resolved by `IconBrushConverter`, so the VM stays
free of Avalonia types. Its severity→brush mapping is deliberately **identical to the squiggle renderer's**
(Error→`ErrorBrush`, Warning→`WarningBrush`, Info→`SubtleForegroundBrush`), so a row and the underline it
describes always read as the same severity. `DiagnosticsPanelViewModel` (App/ViewModels) — an
`ObservableCollection` of rows plus `HasDiagnostics`/`ShowEmptyState` (the existing panel naming), whose
`Update` copies the rows verbatim, in engine order: it sorts nothing, filters nothing, analyses nothing.
`DiagnosticsPanelBinder` (App/Completion) — the view-layer bridge, sitting beside `AmbientModelRefresh` for
exactly the same reason: mapping a diagnostic's absolute offset to a line/column needs the AvaloniaEdit
document. `DiagnosticsPanelView` (App/Views) — a `ListBox` (virtualizing: a very large script's findings
must not be realized all at once — an `ItemsControl` in a `ScrollViewer`, as the Messages panel uses, would
not virtualize) with a severity icon · code · message · location row, and a readable "No diagnostics" state
instead of an empty table.

**One refinement worth naming:** `Update` no-ops when the findings *and* their locations are unchanged. A
keystroke rebuilds the model every debounce tick, but the diagnostics usually do not change, and rebuilding
the collection would churn the UI for nothing (and, from S5 on, drop the user's selection). `Diagnostic` is
a record struct, so this is a plain value comparison; the location is compared too, because an edit *above*
a diagnostic moves it to a new line without changing the engine's record.

**Naming note.** The VM property is `MainWindowViewModel.DiagnosticsPanel`, not the parallel-to-`Performance`
name `Diagnostics` — inside that class `Diagnostics` already resolves to the `EmberTern.App.Diagnostics`
namespace (`ScrollTrace` / `RefreshTrace`), so the obvious name is a compile error.

**Tests.** `DiagnosticsPanelVmTests` (14) pins what the panel promises: the empty state and its
`PropertyChanged` notifications (gotcha #179/#187 — a binding gated on a collection-derived value must be
told to re-query), engine order preserved for deliberately out-of-order input, replace-not-append, the
unchanged-input no-op, the same-diagnostic-new-line refresh, the severity→squiggle-brush mapping, and — end
to end — that running the **real** `DiagnosticsEngine` over a **real** `SemanticModel` and projecting it
yields exactly the engine's findings, count and order. Per the user's directive the renderer/binder stay
visually verified (no artificial UI tests). Build 0/0, **4136 main + 23 probe green**, smoke clean.
**Manual visual verification awaits the user; on confirmation S4 is complete and committed, and S5
(navigation) closes Stage 7.**

---

### S4 rollout — the Diagnostics panel on every object editor (scope change during manual QA)

Manual QA of S4 changed the scope. The panel worked, but the result read as inconsistent: the object
editors had shown **squiggles** since S3 yet offered no way to *browse* the findings. The user asked for
diagnostics to be available consistently across all SQL editing surfaces — explicitly **without** a second
diagnostics system: reuse `DiagnosticsPanelView` / `DiagnosticsPanelViewModel` / `DiagnosticsPanelBinder`
verbatim, and let only the hosting differ. A proposal was requested before any code.

**Two existing facts decided the design, and finding them first is what kept this small.**

1. **The hosting pattern already existed.** `PerformancePanelView` is *already* reused verbatim in the
   Procedure and Function detail tabs as a peer `bottom-tab`, its DataContext set to that tab's own panel
   VM — its comment reads *"Same view type as the SQL Editor's panel, but each host owns a separate context
   — no shared global state."* That is precisely "same view + VM, different host". So Diagnostics is hosted
   the same way, in **Procedure / Function / Trigger / View / Package** (all five already have a top-level
   `bottom-tab` strip). **Script Executor was deliberately deferred** (user decision): it is an SQL editing
   surface, but it has no tab strip, so it deserves its own UX decision rather than widening S4 at the end.
   The tab is appended **last** everywhere — `ActiveSubTabIndex` is persisted per tab and
   `PerformanceSubTabIndex = 5` / `SqlSubTabIndex` / `PackageSubTabIndex` / … are hard-coded constants.
2. **"Which editor?" was already answered.** Procedure/Function host FOUR SQL editors (source, body,
   cursor, subprogram) — the reason the object editors were excluded from S4 in the first place. But every
   one of these code-behinds already had a shipped `ActiveEditor` property (focus-following with a
   mode-aware fallback) driving Alt+F Format and selection replace. Users are trained on it.

**The trap that the proposal caught (and why the rule is NOT `ActiveEditor`).** `ActiveEditor`'s first
clause is `_focusedEditor is not null && _focusedEditor.IsEffectivelyVisible`. Selecting a **peer**
Diagnostics tab *hides the editor tab*, so that guard can never hold while the panel is on screen —
`ActiveEditor` would always collapse onto the mode's primary editor. The Cursors/Subprograms editors could
then never appear in the panel, and the user's explicit requirement ("changing focus between Source / Body
/ Cursor / Subprogram should refresh the panel") would be unobservable *by construction*. The two approved
decisions — peer tab + reuse `ActiveEditor` — silently conflicted. Raised before implementing; the user
chose the fix and asked for it to become an explicit design decision:

> **`LastFocusedSqlDocument` (Stage 7 design decision).** The Diagnostics panel reflects the SQL document
> the user was last *working in*, not the currently visible editor control: the last SQL editor to take
> focus, or — until one does, and after a mode switch — the mode's primary editor. Focus change retargets
> and republishes immediately (no text edit). A mode flip (Easy ⇄ Source) and a rebind to a different
> object both clear the sticky selection and fall back. **The panel never aggregates several editors**; a
> workspace-wide diagnostics list, if ever wanted, is a separate feature and must not change this panel's
> meaning. The visibility guard exists so Alt+F never formats a hidden editor — a read-only list has no
> such concern — and dropping it also makes the panel independent of how TabControl realizes hidden tab
> content.

**Implementation — one new wiring class, nothing else changed.** `DiagnosticsPanelHost` (App/Completion,
beside `AmbientModelRefresh`) owns the rule for a view: `Track(editor, controller)` attaches an ordinary
`DiagnosticsPanelBinder` gated through **the binder's existing lazy panel resolver** — a binder whose editor
is not the active document resolves to `null` and publishes nothing — so `Republish()` can simply ask every
binder to publish and exactly one will. `DiagnosticsPanelBinder`, `DiagnosticsPanelViewModel`,
`DiagnosticRowViewModel` and `DiagnosticsPanelView` are **byte-for-byte unchanged**; the panel VM is a
property on `SourceObjectDetailTabViewModel` (covering Procedure/Function/Trigger in one place),
`ViewDetailTabViewModel` and `PackageDetailTabViewModel`, mirroring `Performance`. Each view's duplicated
fallback expression was factored out of `ActiveEditor` into a `ModePrimaryEditor` property, which
`ActiveEditor` now calls — a small net reduction, not an addition. The host tracks focus separately from the
view's `_focusedEditor` on purpose: the two encode genuinely different rules (visibility-guarded for Format,
sticky for diagnostics), and that is documented at both sites.

**Tests.** The host is view-layer (it needs real `TextEditor` + controller instances), so per the standing
directive — renderers/binders are verified visually, no artificial UI tests, and one headless session per
process (#94) — it carries no unit test; the `LastFocusedSqlDocument` rule is on the manual QA checklist.
The 14 `DiagnosticsPanelVmTests` still pin everything the panel promises. Build 0/0, **4136 main + 23 probe
green**, smoke clean. **Awaits the user's manual visual verification; S5 (navigation) then closes Stage 7.**

---

### S5 — Navigation (App) — DONE (impl); awaits user visual confirmation · **CLOSES STAGE 7**

Scope, per the handoff (`editor-stage7-diagnostics.md` §8.3.1): next/previous diagnostic (keyboard + panel)
and jump-to-span. Nothing else — no Quick Fixes, no Unified Hover (§15), no code actions, no light bulb, no
filtering, no workspace diagnostics.

**The whole milestone rests on one decision the handoff already made: do not invent a second targeting
mechanism.** `DiagnosticsPanelHost` computed the one true target (`ActiveDocument` = the
`LastFocusedSqlDocument` rule, §8.2.1) but kept it private. Making it public was the first change, and
everything else followed from routing through it — the panel publishes through that rule and navigation
jumps through that rule, so a row and the jump it performs **cannot** disagree. That is a construction
property, not a behaviour anyone has to maintain.

**Navigation lives on the host, because the host is the class that knows the target.** It is a pure
consumer: it reads the panel's already-published rows (themselves the language service's cached,
version-matched findings) and never parses, rebuilds a `SemanticModel`, or re-runs the engine. Three methods
(`Navigate(forward)`, `ActivateRow(row)`, `JumpTo`) plus an `OnEditorKeyDown`.

**The SQL Editor was migrated off its bare `DiagnosticsPanelBinder` onto the same host.** S4 had left an
asymmetry: the object editors used `DiagnosticsPanelHost`, the SQL Editor used a binder directly (its single
editor needs no rule). Keeping that would have meant S5 either special-casing the SQL Editor or growing a
second targeting path — the exact thing the handoff forbade. With a host, its `ActiveDocument` simply
collapses onto its one editor: **behaviour-identical, one mechanism**. `MainWindow`'s VM-attach seed
(`_diagnosticsBinder?.Publish()`) became `_diagnostics.Republish()`, the host's equivalent.

**That migration also dissolved gotcha #219 for this milestone.** `F8`/`Shift+F8` is wired **once**, in
`DiagnosticsPanelHost.Track` — and because every SQL editing surface tracks its editors there (the main SQL
Editor now included), no surface can be missed. This is strictly better than "remember to add it in both
places": the capability lives in the seam that already had to be wired per surface. Script Executor has no
panel (S4 deferred it — no tab strip) and therefore no host and no `F8`: consistent by construction, not by
omission.

**The caret is the anchor.** `IndexAfter`/`IndexBefore` (on the panel VM) scan for the first/last diagnostic
starting strictly after/before `CaretOffset`, wrapping silently. Anchoring on the caret rather than a
remembered index makes navigation monotonic regardless of how the selection got where it is, and makes
"press F8 until you have seen them all" work — pinned by a test that walks the list and asserts `0,1,2,0`.

**The scan deliberately reuses the panel's OWN order** — which is the engine's order (`Finalize` sorts by
`Start`, `Length`, `Code`; the panel never re-sorts). Sorting again inside the navigator would have been a
*second* ordering, i.e. a second source of truth, and the panel and caret could then drift apart on any
engine change. Instead the dependency is explicit and **pinned by a test against the real engine**
(`EngineOrder_IsAscendingByStart_TheContractNavigationRelies_On`) — if the engine's order ever changed, that
test fails rather than navigation silently misbehaving.

**The two-target problem (object editors), solved with a per-surface `reveal` callback.** The panel there is
a *peer tab*, so the user is reading the list **instead of** the editor: moving the caret alone lands it
off-screen. The host takes an optional `Action<TextEditor> reveal` and calls it with **its** active document
— the view is told *which* editor to show and only decides *how*, so reveal never re-derives a target:

| Surface | reveal |
|---|---|
| SQL Editor | Nothing to switch — except a **results-maximized** layout, which collapses the editor row to zero height; restored through the existing `ToggleResultsMaximized()` rather than a second sizing path. |
| Procedure / Function | `ActiveSubTabIndex = EditorSubTabIndex`, **plus** `ActiveEasyCollectionIndex = Cursors/SubprogramsEasyIndex` for those two targets (they host SQL editors of their own). The body sits below the sub-tab strip and needs nothing. New named constants replaced the `0..4` literals that previously existed only inside `EasyCommands()`. |
| Trigger / View | `ActiveSubTabIndex = EditorSubTabIndex` / `SqlSubTabIndex`; both editors live on that tab (visibility follows the mode). |
| Package | The editor **is** the tab (`Body`/`PackageSubTabIndex`) — which also re-aligns Package's tab-based `ActiveEditor` fallback with the document just navigated to. |

**The one real trap — and it was a near-miss (gotcha #221).** The established "jump to an offset in another
tab" idiom (`PackageDetailTabView.OnNavigateToMember`) posts the whole caret+scroll+focus block at
`DispatcherPriority.Background`, and for a one-shot double-click that is right: the tab must lay out before
the editor can scroll. Copying it wholesale would have introduced a **hold-F8-does-nothing** bug — Avalonia
dispatches `Input` **above** `Background`, so a queued second `F8` would run *before* the first jump's
continuation, read the pre-jump caret, and re-select the same diagnostic. The split that works: caret +
selection **synchronously** (a document operation — it needs no layout, and it is the anchor the next
keypress reads), and post **only** `BringCaretToView()` + `Focus()`, which genuinely need a laid-out
`TextView`. The general lesson is in the gotcha: *when you defer UI work, ask what the next event will read.*

**The panel's gestures reach the host via a `Navigator` handle, not events.** `DiagnosticsPanelView` gets an
`internal DiagnosticsPanelHost? Navigator { get; set; }`, set by each hosting view in one line (the panel is
named and found exactly as `ProcPerformancePanel` already is). This keeps the dependency direction
Views → Completion (no `Completion` → `Views` reference was introduced), needs no subscribe/unsubscribe
lifecycle against a per-object VM that gets swapped, and cannot leak. Double-click resolves the row from the
**tapped element** rather than the selection, so a double-click on empty space is a no-op.

**Jump semantics mirror go-to-definition** (`NavigationController.JumpTo`): caret at `Start`, select the
span, `BringCaretToView`, focus — so a jump reads the same wherever it came from, and the occurrence
highlighter lights up the target exactly as it does for Ctrl+Click. Offsets are clamped the same way the
squiggle renderer and the panel binder clamp.

**Selection.** The host writes `panel.SelectedIndex` on every `F8`, so the panel follows the caret (and the
`ListBox` auto-scrolls to it). `Update` now drops the selection when the findings genuinely change (a stale
index must not point at an unrelated row) and keeps it when they do not — precisely what the S4 no-op guard
was built for, now with tests proving both halves.

**Tests** (+12, all headless VM-level): next/prev selection, wrap in both directions, clean document, single
diagnostic, repeated-navigation ordering, `SelectedRow` projection, selection kept across an unchanged
republish / dropped on a real change, plus two against the **real engine** (ascending-order contract;
navigation walks a real document in order). The caret/scroll/focus/tab routing is view-layer and stays on the
manual QA checklist, per the standing directive (no artificial UI tests; one headless session per process,
#94).

Build 0/0, **4148 main + 23 probe green**, smoke clean. **Awaits the user's manual visual verification —
with that, STAGE 7 IS COMPLETE.**

---

## Post-Stage-7 — Unified Hover Information

### The order decision — the retrospective's recommendation was reversed, with reason

The Stage 7 retrospective recommended consolidating the duplicated editor wiring (`MainWindow` vs
`SqlEditorBehavior.Attach`, gotcha #219) **before** the next editor feature. The user left the order to
judgement. Checking the recommendation against the code showed **its central claim was false for this
feature**, and it was reversed.

The claim was: *"both leading backlog items add per-editor surfaces — exactly the change the duplication
punishes."* True of **Quick Fixes** (a light bulb is a new adorner + gesture ⇒ a new `Attach` call ⇒ the
silent-omission risk that #219 describes). **False of Unified Hover**, which adds no new attach at all: it
modifies `NavigationController`, which *both* seams already attach. Its only wiring change is new
parameters on `Attach` — made **required**, so a missed seam is a **compile error**. (Both seams were in
fact caught by the compiler during implementation, which is the whole argument in one observation.) #219 is
about *silent* omission; a signature change is loud.

Two further findings sealed it. First, `NavigationController` is already this codebase's chosen
consolidation point — the double-click handler was deliberately moved *into* it from the two duplicated
wirings — so this feature **continues** that pattern rather than fighting it. Second, the consolidation is
not the mechanical merge the retrospective implied: the seams differ because of a genuine lifecycle
difference (MainWindow's editor is constructed *before* its VM exists; the object editors attach *after* the
VM is reachable), and MainWindow deliberately bypasses the controller's `subscribeMetadataChanged` hook
because it silently latched "subscribed" against a null VM and permanently dropped the handler — a bug
documented in a comment at the site. Consolidation must first solve "subscribe once the VM arrives".

Weighed against that: consolidation touches the *installation of every editor capability on every surface*
for zero user-visible value, and under the project's QA rule it cannot be signed off on green tests — it
needs a full manual re-verification of everything, everywhere. That is the largest QA bill in the backlog.
It is worth paying immediately before **Quick Fixes**, where it pays. Doing Hover first cost it one extra
parameter to thread. **Standing recommendation recorded in `editor-stage7-diagnostics.md` §15.4.**

### The interaction decision — plain hover = information, Ctrl = actionability

The user delegated the interaction choice, stating the goal as: *"when I see a squiggled span while writing
SQL, I want to understand why it is underlined without switching to the Diagnostics panel"* — explicitly
**not** a Ctrl-vs-plain question.

The deciding fact turned out to be technical rather than philosophical, and it is now gotcha #222. The old
tooltip was gated on `NavigationEngine.TargetAt` returning a **navigable target**. An unknown object is
*unresolved* — there is no target — so **Ctrl+hover displayed nothing precisely where `ET0001` fires**. It
could not have solved the stated problem without being re-gated anyway; and once re-gated, the modifier is
pure friction. Implementation confirmed the pattern is even stronger than §15 predicted: `ET0001`, `ET0002`,
`ET0003`, `ET0005` and `ET0007` **all** fire on references that by definition did not resolve, so
"both sections" is genuinely rare (`ET0006` is the main way to reach it — it spans a VALUES list, which can
contain resolved symbols). Two test fixtures had to be rewritten once this became clear: the first attempt
assumed `ET0006` spanned the whole statement, when the engine deliberately spans only the VALUES list.

It is also semantically wrong to hide the explanation behind Ctrl: Ctrl+hover *means* "this leads
somewhere", and the most common squiggle leads **nowhere**. So the frozen §9.4 split is **confirmed, not
amended**: plain hover = information, Ctrl = actionability. §9.4 never required Ctrl for information.

### As built

**Core — `Sql/Language/Hover/`.** `HoverInfo` is an ordered aggregate of optional sections
(`Span` / `Diagnostics` / `Info`) — deliberately **not** an `IHoverProvider` abstraction (rule #2: no
interfaces without two implementations); the real contract is **section order**, and diagnostics come first
because the error is why the user hovered. `HoverInfoEngine.GetHover(model, diagnostics, offset)` composes
by pure offset lookup. **The "no new analysis" constraint is enforced by the signature** — diagnostics are
an *input* (the cached, version-matched list), so the engine cannot re-run `DiagnosticsEngine` even by
mistake. That is pinned by a test that feeds it a finding the analyser would never produce.

`HoverInfo.Span` is the **narrowest** section's span, not the union: a wide `ET0006` overlapping a short
column reference must still re-query when the pointer leaves the column, because the content genuinely
differs there. It mirrors `ReferenceAt`'s narrowest-wins tie-break, and the App uses it to hold a card
stable without flicker. Diagnostic hit-testing is **inclusive at the span end** and mirrors `ReferenceAt`
exactly (gotcha #198) — one convention, so the two sections agree about what "here" means.

**App.** `NavigationController` now has two genuinely independent cues sharing only the pointer position:
`UpdateNavigationAffordance` (Ctrl → underline + hand cursor, unchanged semantics) and `UpdateHoverInfo`
(plain → the card). Ctrl+hover shows the card too — a superset — and pressing Ctrl deliberately does **not**
dismiss a card you are already reading.

**Gotcha #209 is closed for this surface, up front rather than after a bug report.** The `_tooltip` bare
`Popup` + `SetParent(editor)` — the exact pattern that rendered invisibly on the desktop for the Parameter
Helper — is **deleted**; the card is `OverlayLayer`-hosted. `ClampIntoOverlay` was extracted from
`ParameterHelper` (which held the only copy) into `EditorPopups` and is now shared: the same geometry
problem, one implementation, parameterised by the flip offset (a caret line for the helper, the pointer gap
for the hover).

**Reuse over duplication in the view.** `QuickInfoView` was split into `BuildContent` (sections, no chrome)
and `Card` (the shared chrome); `Build` is now `Card(BuildContent(...))` and is byte-identical for its
remaining caller, the standalone Ctrl+Space Quick Info popup. `HoverInfoView` composes those same pieces, so
the unified hover and the standalone Quick Info **cannot drift apart**. Severity colours are the same theme
tokens the squiggle renderer paints and the Diagnostics panel lists with, so an underline, a panel row and a
hover card always agree.

**Noise control** — the whole UX risk, since plain hover fires constantly where Ctrl+hover was
self-limiting: a **350 ms** dwell; the card stays put while the pointer is inside `HoverInfo.Span` (no
flicker as the pointer drifts across an identifier) and moving off it re-arms the dwell (so crossing text
never strobes cards along the path); it never opens while the completion list / Parameter Helper / Quick
Info popup is up (`SqlCompletionController.IsPopupOpen` — that controller already owned the "they shouldn't
stack" rule for all three, so arbitration stays in one place); it never steals focus and is never
hit-testable (a hit-testable card under the cursor fires `PointerExited` and flickers itself shut); and it
is dismissed by any click, any text edit, or the pointer leaving.

**Tests** (+11 `HoverInfoEngineTests`, headless Core): the headline unknown-object case *with the null
Quick Info explicitly asserted*; unresolved variable; symbol-only; nothing-to-say → null; null model; null
diagnostics; both-sections; inclusive-at-end hit-testing; narrowest applicable span; diagnostics-are-an-input;
engine order preserved. The pointer/dwell/placement UX is view-layer and stays on manual QA.

**P5d is delivered and closed** by this milestone, exactly as §15 recommended: one surface, one dwell, one
tuning pass.

Build 0/0, **4159 main + 23 probe green**, smoke clean. **Awaits the user's manual visual verification.**

---
