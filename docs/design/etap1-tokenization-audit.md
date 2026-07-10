# Etap 1 — Firebird tokenization audit (starting point for the Lexer etap)

> **STATUS: EXECUTED — Etap 1 shipped 2026-07-09** per this audit's §4 decision + §6 order.
> `SqlLexer` + `FirebirdSyntax` created; O2/O3/O4 folded onto the lexer; K1 (`SqlKeywords`)
> derived; light XSHD palette fixed + keyword blocks pinned to the catalog. O1 (→ Etap 3) and
> O5 (→ Etap 2) deferred as decided. As-built record: [editor-architecture.md](editor-architecture.md)
> §18. This document is retained as the ground-truth audit that drove the work.
>
> Produced 2026-07-09 as the ground-truth starting point for **Etap 1 (Lexer + single
> `FirebirdSyntax` keyword catalog + light-theme lexical-palette fix)** of the editor rebuild.
> Companion to [editor-architecture.md](editor-architecture.md) (frozen design; Etap 1 scope in
> its §17).
> Every entry is grounded in the code (file:line), not memory. Governed by the **§0
> Paramount Law** (never lose information; text-reproducing consumers must round-trip
> byte-for-byte).

This document answers three questions for the whole tokenization surface:
1. **Inventory** — every place that lexes / scans / tokenizes SQL, holds a keyword list, or
   drives syntax highlighting.
2. **Dependency map** — who produces, who consumes (the edges).
3. **Disposition** — REPLACE (by the new lexer) · KEEP (unchanged; rides the lexer) ·
   ADAPTER (public API kept, internals re-pointed) · DEFER (migrate in a later etap) —
   with the §0 risk for each.

---

## 1. Inventory (grounded in code)

### 1.1 The shared low-level scanner — THE SEED (keep)
- **`SqlScanHelpers`** — [src/EmberTern.Core/Sql/SqlScanHelpers.cs](../../src/EmberTern.Core/Sql/SqlScanHelpers.cs).
  Public primitives: `IsIdentifierChar`, `SkipTrivia`, `TrySkipQuoted`, `ReadWord`,
  `ReadIdentifier`, `TryKeyword`, `ReadParenBlock`, `ReadUntilSemicolon`,
  `SplitTopLevelCommas`, `ContainsWord`, `FindOuterBeginEndContent`, `SkipToEndOfBlock`
  (the CASE-aware BEGIN/END scanner, gotcha #129). This is the ONE good shared scanner and
  the acknowledged seed of the real lexer (design §2.1). **Core consumers (all ride it):**
  `SqlParameterScanner`, `PredicateExtractor`, `ProcedureBodyModel`/`ProcedureBodySplitter`,
  `ProcedureBodyScanner`, `PackageSourceScanner`, `ProcedureSignatureParser`,
  `FunctionSignatureParser`, `TriggerSignatureParser`, `ViewSignatureParser`,
  `TraceStatementFingerprinter`, `TraceSqlOperationClassifier`.

### 1.2 Outlier scanners — each re-implements literal/comment/identifier skipping (the P8 duplication)
| # | Scanner | File:line | Own tokenizer? | Consumers |
|---|---|---|---|---|
| O1 | `SqlFormatter.Tokenize` + keyword hashsets | [SqlFormatter.cs:37–78](../../src/EmberTern.Core/Sql/SqlFormatter.cs) | yes (flat-token pass; `TopLevelSingle`/`JoinModifiers`/`Conjunctions`/`OtherKeywords` + `MultiCharOps`) | `SqlFormatter.Format` → `EditorSearch` (Ctrl+F menu Format), `ProcedureDetailTabView`/`FunctionDetailTabView` (Alt+F) |
| O2 | `SqlAliasResolver.Tokenize` (+ own `TokenKind` enum) | [SqlAliasResolver.cs:235–331](../../src/EmberTern.Core/Sql/SqlAliasResolver.cs) | yes | `SqlCompletionController` (via `ParseAliases`, Etap 0 cached in `EditorLanguageService`), `PredicateExtractor.Extract` |
| O3 | `SqlStatementClassifier` private scanner | [SqlStatementClassifier.cs:99–146](../../src/EmberTern.Core/Sql/SqlStatementClassifier.cs) (`ReadKeyword`/`SkipTrivia`/`IsIdentifierChar`) | yes (byte-copy of `SqlScanHelpers.SkipTrivia`) | `MainWindowViewModel` lane routing ([MainWindowViewModel.cs:5100](../../src/EmberTern.App/ViewModels/MainWindowViewModel.cs)) |
| O4 | `TraceSqlInliner` private scanner | [TraceSqlInliner.cs:41–106](../../src/EmberTern.Core/Trace/TraceSqlInliner.cs) | yes | `TraceEventDetailViewModel`, `TraceMonitorTabViewModel` (Show values) |
| O5 | `FirebirdDdlExecutor.SplitStatements` | [FirebirdDdlExecutor.cs:151](../../src/EmberTern.Firebird/FirebirdDdlExecutor.cs) | yes (standalone, PSQL/BEGIN-END/CASE/package-aware — gotchas #55/#128/#140/#152) | `FirebirdDdlExecutor.ExecuteAsync` (**every** Compile/Apply path) |

> O1–O4 are the "four outlier scanners" named in design §5.1. **O5 is a 5th, not named
> there** — it lives in `EmberTern.Firebird`, not Core, and is §0-critical (it splits the DDL
> string actually sent to the server). It must be handled deliberately (see §3/§5).

### 1.3 Editor caret/word helpers (App + Core; not full tokenizers)
- **`SqlCompletionContext`** — [src/EmberTern.Core/Sql/SqlCompletionContext.cs](../../src/EmberTern.Core/Sql/SqlCompletionContext.cs): `GetCurrentWord` / `GetDotContext` / `GetWordAt` / `ShouldAutoTrigger` / `IsIdentifierChar`. Consumers: `SqlCompletionController`, `CaretContext`, `SqlEditorBehavior` (double-click), `MainWindow` (double-click), and `SqlAliasResolver` (reuses `IsIdentifierChar`).
- **`CaretContext`** — [src/EmberTern.App/Completion/CaretContext.cs](../../src/EmberTern.App/Completion/CaretContext.cs) (Etap 0): bounded `ITextSource` backward scan; mirrors `SqlCompletionContext`. App glue.
- **`CaseMatcher`** — [src/EmberTern.Core/Sql/CaseMatcher.cs](../../src/EmberTern.Core/Sql/CaseMatcher.cs): case-preserving insert. Consumer: `SqlCompletionData`. (Text transform, not tokenization.)

### 1.4 Signature parsers + body model (Core; shallow DTO parsers over `SqlScanHelpers`)
- `ProcedureSignatureParser` (its `ParseSegment` is reused by `FunctionSignatureParser` + `ProcedureBodyModel`), `FunctionSignatureParser`, `TriggerSignatureParser`, `ViewSignatureParser`.
- `ProcedureBodyScanner` (`CommentBody`/`UncommentBody`/`FindOuterBodyContent`) — consumers: Procedure/Function/Trigger/Package detail views (Comment/Uncomment).
- `ProcedureBodyModel` + `ProcedureBodySplitter` (`Split`/`ParseCursorName`/`CursorIsScroll`/`RewriteCursorHeader`/`ParseSubprogram`/`RewriteSubprogramName`; inverse `DdlGenerator.BuildProcedureBody`) — consumers: Procedure/Function/Trigger detail VMs + `ProcedureLocalRowViewModels`.
- `PackageSourceScanner.FindMemberOffset` — consumer: `PackageDetailTabViewModel`.

### 1.5 Performance predicate layer (Core)
- `PredicateExtractor` (uses `SqlScanHelpers` + `SqlAliasResolver.ParseAliases`), `SargabilityClassifier`, `QueryPredicate`. Consumer: `PerformanceContextBuilder` → the advisor rules (R1–R6).

### 1.6 Trace tokenization (Core)
- `TraceStatementFingerprinter` (rides `SqlScanHelpers`) — consumers: `TraceEventGrouper`, `TraceMonitorTabViewModel`, `TraceEventRowViewModel`.
- `TraceSqlOperationClassifier` (rides `SqlScanHelpers`) — consumers: `TraceEventRowViewModel`, `TraceMonitorTabViewModel`.
- `TraceSqlInliner` — see O4 (own scanner).

### 1.7 Keyword lists (the "3 divergent sources" — P5/P8)
| # | List | File | Drives |
|---|---|---|---|
| K1 | `SqlKeywords.All` (one flat list, ~150 tokens) | [SqlKeywords.cs:15](../../src/EmberTern.Core/Sql/SqlKeywords.cs) | completion vocabulary (`SqlCompletionController`) |
| K2 | `SqlFormatter` hashsets: `TopLevelSingle`, `JoinModifiers`, `Conjunctions`, `OtherKeywords` (+ `MultiCharOps`) | [SqlFormatter.cs:37–78](../../src/EmberTern.Core/Sql/SqlFormatter.cs) | formatting decisions |
| K3a | Dark XSHD `<Keywords>` blocks: Function / DataType / StatementKeyword / DmlKeyword | [FirebirdSql.xshd:39–…](../../src/EmberTern.App/Assets/FirebirdSql.xshd) | lexical highlighting (dark) |
| K3b | Light XSHD `<Keywords>` blocks (mirror of K3a) | [FirebirdSql.Light.xshd:37–…](../../src/EmberTern.App/Assets/FirebirdSql.Light.xshd) | lexical highlighting (light) |

> **Grammar-role keyword sets** (NOT the highlight/completion catalog — they belong to the
> parsers): `SqlAliasResolver.AliasTerminators`/`TableListStarters`,
> `PredicateExtractor.RegionTerminators`/`LhsStopWords`, `TriggerSignatureParser` terminators,
> and the inline keyword checks in `SqlStatementClassifier`. These stay with their parsers;
> `FirebirdSyntax` (Etap 1) unifies K1–K3 (the catalog), not these role sets.

### 1.8 Syntax highlighting assets + wiring (App)
- **Assets:** [FirebirdSql.xshd](../../src/EmberTern.App/Assets/FirebirdSql.xshd) (dark) + [FirebirdSql.Light.xshd](../../src/EmberTern.App/Assets/FirebirdSql.Light.xshd) (light). 8 colours each (`Comment`/`String`/`Number`/`DmlKeyword`/`StatementKeyword`/`DataType`/`Function`/`Operator`) + 4 keyword blocks (K3).
- **Registration:** [App.axaml.cs:67–84](../../src/EmberTern.App/App.axaml.cs) — `RegisterFirebirdSyntax` registers both by name (`FirebirdSyntaxName` / `FirebirdSyntaxLightName`).
- **Per-editor apply (identical pattern, 11 sites):** `MainWindow.ApplyEditorThemeColors` ([MainWindow.axaml.cs:1499](../../src/EmberTern.App/Views/MainWindow.axaml.cs)) + `ApplyEditorTheme` in `DomainDetailTabView`, `ExceptionDetailTabView`, `FunctionDetailTabView`, `GeneratorDetailTabView`, `GlobalSearchTabView`, `IndexDetailTabView`, `PackageDetailTabView`, `ProcedureDetailTabView`, `TriggerDetailTabView`, `ViewDetailTabView`. Each picks light/dark by `ActualThemeVariant` and sets `editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(name)` (gotcha #19).
- **Text-range highlighters (NOT tokenization — keep as-is):** `OccurrenceHighlighter`, `SearchMatchHighlighter` ([src/EmberTern.App/Completion](../../src/EmberTern.App/Completion)) — `IBackgroundRenderer`s that box a selected identifier / search hits.

---

## 2. Dependency map

```
                         ┌──────────────────────── Core.Sql ────────────────────────┐
                         │                                                            │
  SqlScanHelpers (KEEP → lexer seed) ──rides──┬─ SqlParameterScanner ── MainWindowVM (Smart Params)
        ▲                                      ├─ PredicateExtractor ─┐
        │                                      │        ▲             ├─ PerformanceContextBuilder ─ advisor R1–R6
        │                                      │  SargabilityClassifier┘
        │                                      ├─ Procedure/Function/Trigger/ViewSignatureParser ─┐
        │                                      ├─ ProcedureBodyModel/Splitter, ProcedureBodyScanner ├─ Procedure/Function/Trigger/View/Package Detail VMs+Views
        │                                      ├─ PackageSourceScanner ───────────────────────────┘
        │                                      ├─ TraceStatementFingerprinter ─┐
        │                                      └─ TraceSqlOperationClassifier ──┼─ Trace VMs (Monitor/Detail/Row) + TraceEventGrouper
                                                                                 
  OUTLIER SCANNERS (own tokenizers):                                             
   O1 SqlFormatter.Tokenize + K2 ── SqlFormatter.Format ── EditorSearch, Procedure/Function Detail (Alt+F)
   O2 SqlAliasResolver.Tokenize ── ParseAliases ──┬─ EditorLanguageService (Etap 0 cache) ─ SqlCompletionController
                                                   └─ PredicateExtractor
   O3 SqlStatementClassifier scanner ── Classify ── MainWindowVM (lane routing / F5)
   O4 TraceSqlInliner scanner ── Inline ── Trace Detail/Monitor VMs (Show values)
   O5 FirebirdDdlExecutor.SplitStatements ── ExecuteAsync ── ALL Compile/Apply paths      [EmberTern.Firebird]

  KEYWORD CATALOG (drift):  K1 SqlKeywords.All ─ completion   K2 SqlFormatter sets ─ formatting   K3a/K3b XSHD ─ highlighting
                         └── all three hand-maintained separately → unify under FirebirdSyntax (new) ──┘

  EDITOR WORD/CARET:  SqlCompletionContext ──┬─ SqlCompletionController / CaretContext / SqlEditorBehavior / MainWindow(double-click)
                       CaseMatcher ── SqlCompletionData                                    (App/Completion)

  HIGHLIGHTING:  FirebirdSql.xshd + .Light.xshd ── App.RegisterFirebirdSyntax ── 11× ApplyEditorTheme(GetDefinition)
                 OccurrenceHighlighter / SearchMatchHighlighter  (IBackgroundRenderer — text-range, not tokenization)
```

---

## 3. Disposition — REPLACE / KEEP / ADAPTER / DEFER

| Component | Etap 1 disposition | Rationale / §0 note | Eventually |
|---|---|---|---|
| **`SqlLexer` (new, `Core.Sql.Language`)** | **CREATE** | one Firebird-aware tokenizer; `SqlToken`+one `TokenKind` | the app's only lexer |
| **`FirebirdSyntax` (new, `Core.Sql.Language`)** | **CREATE** | one keyword catalog (K1+K2+K3 source of truth) | drives lexer/completion/formatter/XSHD |
| `SqlScanHelpers` | **KEEP** (fold its primitives *into* the lexer; may re-home under `Language`) | many Core consumers ride it — must not break | absorbed into `SqlLexer` |
| K1 `SqlKeywords.All` | **ADAPTER** — derive from `FirebirdSyntax`; keep the `All` API | `SqlCompletionController` reads it | may retire when Completion Engine reads `FirebirdSyntax` (Etap 5) |
| O2 `SqlAliasResolver.Tokenize` | **REPLACE** internals with the lexer; **ADAPTER** keeps `ParseAliases`/`ResolveTableForQualifier` public API | Etap-0 cache + `PredicateExtractor` depend on the API, not the tokenizer | resolver superseded by Semantic Model (Etap 4) |
| O3 `SqlStatementClassifier` scanner | **REPLACE** internals with the lexer (keep `Classify` API) | only reads the leading keyword — low risk | re-expressed as AST query (Etap 2) |
| O4 `TraceSqlInliner` scanner | **REPLACE** internals with the lexer — **§0 corpus-diff first** | reproduces SQL + inlines values; passthrough must stay byte-identical | client of the lexer |
| O1 `SqlFormatter.Tokenize` + K2 | **DEFER to Etap 3** (see §4 open decision) | folding it now = a partial formatter rewrite before the AST formatter → R2 regression risk | REPLACED wholesale by the AST formatter (Etap 3) |
| O5 `FirebirdDdlExecutor.SplitStatements` | **DEFER to Etap 2** (KEEP now) | §0-CRITICAL (splits DDL sent to server), in `Firebird` not Core, heavily gotcha-pinned; needs real statement boundaries from the parser | migrate to parser statement boundaries + corpus diff |
| Signature parsers, `ProcedureBody*`, `PackageSourceScanner`, `PredicateExtractor`, `SargabilityClassifier`, `SqlParameterScanner`, `TraceStatementFingerprinter`, `TraceSqlOperationClassifier` | **KEEP** | they ride `SqlScanHelpers` → benefit transparently once its primitives are the lexer's; no call-site change | re-expressed as AST queries opportunistically (Etap 2+) |
| `SqlCompletionContext`, `CaretContext`, `CaseMatcher` | **KEEP** | editor word/caret + insert casing; not full tokenizers | subsumed by Completion Engine (Etap 5) / stay |
| K3a dark XSHD | **KEEP colours**; keyword blocks **ADAPTER** (generate/verify from `FirebirdSyntax`) | dark theme is good (design §9.1); only kill the drift | lexical base under the Etap-6 semantic colorizer |
| K3b light XSHD | **REPLACE palette** (contrast fix, §9.5) + keyword blocks derive | light theme is muddy (design §9.1) — the early standalone win | same |
| App registration + 11× `ApplyEditorTheme` | **KEEP** | works; identical glue | dedup candidate (NOT Etap 1); Etap 6 adds the semantic colorizer layer |
| `OccurrenceHighlighter`, `SearchMatchHighlighter` | **KEEP** | text-range renderers, unrelated to tokenization | stay |

---

## 4. Etap 1 scanner-scope decision — APPROVED 2026-07-09

Design **§5.1 + §12 Etap 1** say "fold in the **four** outlier scanners" (O1–O4). But **§12
Etap 3** is where `SqlFormatter` is rewritten AST-based, and **R2** flags formatter
regressions as a top risk. Folding **O1 (`SqlFormatter.Tokenize`)** into the lexer in Etap 1
means re-pointing the formatter's tokenizer *before* its Etap-3 rewrite — a partial,
regression-prone formatter change with no AST payoff yet.

**Decision (user-approved 2026-07-09) — binding for Etap 1:**
- **Etap 1 folds O2 (`SqlAliasResolver`), O3 (`SqlStatementClassifier`), O4 (`TraceSqlInliner`)**
  onto the new lexer, plus creates **`SqlLexer` + `FirebirdSyntax`**, unifies the keyword
  catalog (K1/K2/K3), and fixes the **light-theme lexical palette**.
- **O1 (`SqlFormatter.Tokenize`) → deferred to Etap 3.** The formatter is rewritten AST-based
  there and O1 dies with it; investing in re-tokenizing a to-be-replaced formatter is wasted
  work and a needless R2/§0 risk. No partial formatter change before Etap 3.
- **O5 (`FirebirdDdlExecutor.SplitStatements`) → deferred to Etap 2**, where the parser knows
  the real statement boundaries; migration gated by an old-vs-new corpus diff (§0-critical).

Net: Etap 1 removes 3 of the 4 named outliers now while keeping the two highest-risk
text-reproducers (O1, O5) untouched until their proper etap.

---

## 5. §0 (Paramount Law) constraints for this migration

Any lexer swap under a **text-reproducing** consumer must be proven byte-for-byte identical
(old-vs-new corpus diff on the Lab DB + the user's ERP scripts) **before** the switch:
- **O5 `SplitStatements`** — a wrong split corrupts the DDL sent to the server (highest risk;
  hence DEFER to Etap 2 with the parser's real boundaries).
- **O1 `SqlFormatter.Format`** — reproduces user SQL (hence DEFER to Etap 3).
- **O4 `TraceSqlInliner.Inline`** — the non-inlined passthrough must be verbatim; placeholder
  substitution must be exact.
- **Signature parsers / `ProcedureBodySplitter` / `DdlGenerator.BuildProcedureBody`** — Easy⇄Source
  round-trip must not lose or reorder anything (gotchas #114/#152). These are KEEP in Etap 1
  (they ride `SqlScanHelpers`), so they're unaffected — but the `SqlScanHelpers`→lexer
  primitive fold must preserve their exact scan behaviour (unit tests + round-trip pins guard this).

---

## 6. Etap 1 execution order + acceptance (cross-ref editor-architecture §17)

1. `FirebirdSyntax` catalog (K1+K2+K3 unified) — keywords by category, types, built-in functions.
2. `SqlLexer` + `SqlToken`/`TokenKind`, folding `SqlScanHelpers` primitives in; `SqlScanHelpers`
   consumers keep working (KEEP) — verify with their existing tests.
3. Migrate O2/O3/O4 onto the lexer (each behind its stable public API), pinned by that
   consumer's tests + (O4) a byte-identity corpus diff. **Defer O1 + O5** (§4/§5).
4. `SqlKeywords.All` → derived from `FirebirdSyntax` (ADAPTER).
5. Light-theme XSHD palette regenerated for contrast (§9.5); K3 keyword blocks derived from
   `FirebirdSyntax`; dark palette unchanged unless a role is clearly muddy.

**Acceptance:** lexer tokenizes the Lab DB + real ERP SQL corpus correctly; each migrated
consumer keeps identical behaviour (its tests + corpus diff); the light theme is visibly
clearer with distinguishable roles; build 0/0; smoke clean. **§0 holds** — the deferred O1/O5
keep the two highest-risk text reproducers untouched until their proper etap.

**Out of scope for Etap 1:** Parser, AST, Semantic Model, formatter rewrite, completion engine,
navigation, Quick Info — all later etaps.

---

*End of audit. This is the Etap 1 starting point; the §4 scope decision is APPROVED — begin from §6.*
