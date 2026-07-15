# Etap 6.9 — Structural AST Deepening (implementation guide)

> **Status: ETAP 6.9 COMPLETE (2026-07-15). Parser (B0–B5) + binder + FORMATTER are all AST consumers;
> Stage 7 follows.** Formatter convergence landed construct-by-construct (§13.2): a query is laid out by an
> AST-walking core with nested-query indentation + adaptive CASE; the token emitter is retained only as the
> interior/expression renderer and for the constructs the parser intentionally does not model (UPDATE
> SET/DELETE/MERGE clause layout, PACKAGE bodies), with one layout mechanism per construct. Verified: build
> 0/0, 4065 main + 23 probe green, smoke clean. The binder is a full AST consumer — its
> structural token walkers (`BindQuery`/`CollectTables`/`ParseTableList`/`ParseCteList`/`BindColumnReferences`
> FROM+`(SELECT` re-scan, and the PSQL `BindLeafReferences` `(SELECT` branch) are DELETED; only
> expression-level token work remains (column/local/param references + DML-target identification, which has
> no AST node). The formatter is still a token-stream (`FToken`) layout engine — converging it is the last
> item (see §13). Verified: build 0/0, 4008 main + 23 probe green, smoke clean. This document is the
> implementation guide for Etap 6.9, the foundational
> parser/AST deepening that must land **before Stage 7 (Diagnostics)** and before the future Debugger.
> As of B5 the parser is the **single structural source for all SQL/PSQL structure** (within the
> consciously-accepted structural-depth scope): the PSQL body tree is produced for all four PSQL surfaces
> **and the semantic binder consumes it** (B1); the **query model is fully recursive** — clauses +
> FROM/join + set operations (B2), WITH/CTE + derived tables + EXISTS/scalar subqueries (B3); **B3.1**
> attaches a real `QueryNode` to every query embedded in another statement (INSERT/MERGE source, CREATE
> VIEW body, UPDATE/DELETE/MERGE embedded subquery, PSQL FOR-SELECT cursor, DECLARE CURSOR query); **B4**
> models `CASE … END` (simple + searched, SELECT-expression + PSQL) as a `CaseExpression`; and **B5**
> promotes every embedded DSQL statement inside a PSQL body (SELECT/INSERT/UPDATE/DELETE/MERGE/EXECUTE) to
> the **reused** top-level statement node, so a DML query inside a routine body is the SAME node — with the
> same query structure — as at the top level (closing the last "query on tokens" residual). **No parallel
> AST representation remains; structure lives once in the tree.** The binder now consumes that tree
> (§13.1); the one remaining consumer that still re-derives structure from tokens is the FORMATTER (§13.2)
> — the last Etap-6.9 step. (As-built + verification: §10 B0…B5, §13 convergence; boundaries: §12.)
> Produced by a pre-Stage-7 architecture review, captured here in full.
>
> Companion document: [`editor-stage7-diagnostics.md`](editor-stage7-diagnostics.md) (the Stage 7
> vision that consumes this foundation). Parent architecture: [`editor-architecture.md`](editor-architecture.md).

---

## 0. Why this etap exists (the architectural review)

After Etaps 0–6 + the UX Polish Phase (incl. P8 formatter polish) shipped, a pre-Stage-7 review
asked whether the editor/parser is a solid enough foundation for Diagnostics, Folding, Breadcrumbs
and — especially — a future Debugger. It is **not yet**, and every gap traces to one root cause:

> **The AST is a *statement skeleton with token-bag annotations*, not a structural tree. The real
> knowledge of SQL structure lives — duplicated — inside three-to-four independent token walkers.**

Concretely, as of the review:

- `SqlParser` (~602 lines) is a **segmenter**: it splits top-level statements by
  paren/`BEGIN`/`CASE`/`END` token-depth counting and classifies by leading keyword. It parses
  **no** clause, expression, or PSQL-body structure. `SqlStatement.Children` is hardwired empty —
  statements are **leaves**.
- The only sub-statement nodes are `WithClause` / `CommonTableExpression`, and **even those are
  token bags** (`NameToken`, `ColumnTokens`, `BodyTokens` — raw `SqlToken` lists; `Children =>
  Empty`). A nested CTE, or a CTE body, is therefore **not modeled** — it is tokens.

Because the tree carries no structure, several subsystems each **re-derive the same structure by
hand**:

| Walker | What it re-derives from tokens | Scope |
|---|---|---|
| **`SqlFormatter`** (~1417 lines) | ~24 structural routines: clause breaks (`MatchStructuralPhrase`), lists (`SplitTopLevelCommas`, `MatchParen`, `FindColumnListEnd`), CTE layout, PSQL blocks (`EmitPsqlUnit`/`EmitForSelect`), 7 paren-depth counters | The whole layout engine |
| **`SemanticBinder`** (Query ~447 + Psql ~661 + Dml) | A *second informal recursive-descent*: `BindQuery`/`ParseCteList` descend into subqueries + CTE bodies (`SkipParens`, `CollectTables`) to build the `Scope` tree; the Psql walker finds `RoutineBody`/`Block` scopes + declares locals/cursors/NEW/OLD | The whole scope tree |
| **`SqlAliasResolver`** (older) | FROM/alias resolution, still used by `PredicateExtractor` (Performance) | Legacy 4th walker |

This is the divergence the project wants gone: *knowledge about SQL syntax spread across multiple
independent implementations*. The target — **Parser → AST → Semantic Model → all features** — is
broken at the first arrow: the AST does not hold the structure, so every feature re-parses.

### Symptoms this etap fixes (the review's five items)

1. **CASE formatting** — there is no `EmitCase`; `CASE/WHEN/THEN/ELSE/END` sit in the formatter's
   `OtherKeywords` set (lowercased, never line-broken), so CASE is emitted **entirely inline** and a
   300-char CASE stays 300 chars. As a SELECT item it is rendered atomically, so long-line wrapping
   cannot touch its interior. Genuinely unmodeled.
2. **Nested-query indentation** — there is **no subquery indentation model at all**. `Emit` has no
   indent parameter; a clause break appends a bare `\n` at **column 0**, and `MatchStructuralPhrase`
   ignores paren depth, so `SELECT`/`FROM`/`WHERE` **inside** a derived table / `EXISTS (…)` /
   scalar subquery breaks to column 0 exactly like a top-level clause. Inner queries are flattened.
   The 7 paren-depth counters are *boundary detectors*, never *indent counters*.
3. **Structural matching** (parens, BEGIN/END) — no bracket matching exists; BEGIN/END pairs are only
   discoverable via the binder's `Block` scopes today. (This item is largely App-side and does not
   require deepening — see §8.)
4. **AST completeness** — the core question; see the audit matrix in §6.
5. **Smart snippets** — already ~80% built (`SnippetEngine` + completion integration + Tab-stops);
   context-gating improves once real structural position is available. (See §8.)

### Decisions taken (2026-07-14, user)

- **Depth = "structural depth"**, not a full expression tree, not PSQL-body-only. Model all major
  *structural* constructs (clauses, subqueries, CASE, PSQL control-flow + executable statements);
  keep ordinary expressions (operators, arithmetic, operands) as opaque token fragments.
- **Foundation first.** Build this etap before implementing the remaining editor features, rather
  than knowingly creating another temporary token-walk. The only exception kept independent is
  pure-visual polish (e.g. colours), which has no architectural impact.
- **CASE / nested indentation wait for the AST node** — no interim formatter token-walker.
- **Incremental migration, not a big-bang rewrite** (see the Migration Contract, §5): the binder
  migrates first; new features consume the AST immediately; the formatter converges **one construct
  at a time** as each node is introduced.
- **Model the debugger's needs once, now** — every executable statement gets a stable node with a
  source span (§7).

---

## 1. Design principles (invariants for every milestone)

0. **§0 Paramount Law still governs.** Never lose information; never modify what we cannot reproduce
   identically; correctness over aesthetics. New nodes are **additive over the token stream** — each
   node stays backed by its token range, `RawStatement` remains the safety valve, and the formatter's
   lexeme-preservation invariant (per-statement + per-script) remains the net.
1. **One node per construct = the single structural source.** Once a node exists for a construct,
   **every new feature reads it**, and **no feature writes a new token walk** for that construct.
2. **Structural depth, not expression depth.** Inside `WHERE`/`ON`/projection/assignment, ordinary
   expressions stay opaque token fragments. Only *structurally significant* expressions become nodes:
   `CASE`, `EXISTS`, scalar subquery, subquery. This bounds the parser while single-sourcing SQL
   *structure*.
3. **Replace token-bags with real children.** The `WithClause`/CTE token-bag pattern was the right
   shape but the wrong depth; deepening promotes those bags (and adds new nodes) to real child nodes.
4. **Every executable statement is a `PsqlStatement` with a stable `Span`** — the debugger's step
   unit (§7). Model it from the first PSQL milestone, not retrofitted.
5. **The parser stays error-tolerant** (never throws, never returns null; unrecognised ⇒ `RawStatement`).
6. **Every milestone strictly reduces token-walk structural logic** (§5.4) — the etap's headline
   metric.

---

## 2. Target end-state

```
                         ┌── one recursive-descent SqlParser ──┐
   editor text ─► Lexer ─►  AST (statements + clauses + query   │
                         │  tree + PSQL body tree + structural  │
                         │  expression nodes)                   │
                         └──────────────┬──────────────────────┘
                                        │  (single structural source)
        ┌───────────────┬──────────────┼───────────────┬───────────────┬───────────────┐
   Semantic Model    Formatter     Diagnostics       Folding        Breadcrumbs       Debugger
   (binder reads     (consumes     (Stage 7)         (Stage 7)      (Stage 7)         (future)
    nodes, not        nodes per
    token walks)      construct)
```

SQL structure is represented **once**. The formatter, semantic model, diagnostics, folding,
breadcrumbs, navigation and the debugger are all pure *clients* of the tree.

---

## 3. Node inventory (structural depth)

### 3.1 Query layer — a reusable `QueryNode`

`QueryNode` = a SELECT with clauses, optional leading `WITH`, optional set-operations. Reused
everywhere a query can nest (CTE body, derived table, EXISTS, scalar subquery) — so **nesting,
including nested CTEs, falls out for free**.

- **Clauses:** `SelectClause` (projection items), `FromClause` (list of `FromItem`), `WhereClause`,
  `GroupByClause`, `HavingClause`, `OrderByClause`, `SetOperation` (UNION / INTERSECT / EXCEPT joining
  two `QueryNode`s).
- **`FromItem` variants:** `TableReference` (name + alias), `DerivedTable` (holds a `QueryNode`),
  `JoinedTable` (left / join-kind / right / ON-fragment).
- **Structural expression nodes:** `CaseExpression` (one node covers *simple* `CASE x WHEN` and
  *searched* `CASE WHEN`, and both the SELECT-expression and PSQL-statement forms), `ExistsExpression`
  (holds a `QueryNode`), `ScalarSubquery` (holds a `QueryNode`).
- **CTE promotion:** `CommonTableExpression.Body` and `WithClause.MainQuery` become real `QueryNode`s
  (replacing `BodyTokens` / `MainQueryTokens`).

> Ordinary expression interiors (predicates, arithmetic, function arguments) stay as opaque token
> fragments carried on the owning clause/node. That is the structural-depth boundary.

### 3.2 PSQL layer — debugger-critical

A base **`PsqlStatement : SqlNode`** carrying a stable `Span` is the **step unit** (§7).

- **Control flow:** `BlockStatement` (`BEGIN … END` + optional DECLARE section + `WhenHandler`s),
  `IfStatement` (condition fragment / then-branch / optional else-branch), `WhileStatement`
  (condition / body), `ForSelectStatement` and `ForExecuteStatement` (cursor `QueryNode` + INTO
  targets + body).
- **Executable leaves** (each a `PsqlStatement`): `AssignmentStatement`,
  `ExecuteProcedureStatement` / `CallStatement`, `ExecuteStatementStatement`, `SuspendStatement`,
  `ExitStatement` / `LeaveStatement`, `ExceptionRaiseStatement`, `PostEventStatement`, plus the DML
  statement nodes (INSERT / UPDATE / DELETE / MERGE / SELECT…INTO) reused verbatim.
- **Declarations:** `DeclareVariableStatement`, `DeclareCursorStatement`.
- **Definition bodies:** `CREATE PROCEDURE / FUNCTION / TRIGGER` and `EXECUTE BLOCK` gain a real
  `Body : BlockStatement` (today their body is one opaque `Tokens` blob).

---

## 4. Formatter convergence strategy

The formatter is the **highest-risk** consumer (it owns the §0 byte-identity invariant), so it is
migrated **construct-by-construct, never big-bang** (user directive):

- When a construct becomes an AST node, the formatter **starts consuming that node** and its
  construct-specific token-walk is **deleted** in the same milestone.
- Constructs without a node yet **keep their current token-walk** until their node lands.
- Each formatter migration is gated by the existing formatter test suite (byte-identity) **or** the
  intended new layout (for CASE / nesting, which deliberately change output).

**Token-walk logic scheduled for deletion, by milestone:**

| Milestone | Formatter routines retired |
|---|---|
| B1 | `EmitForSelect`, `EmitPsqlUnit` (+ its branch/collect helpers) |
| B2 | `MatchStructuralPhrase` flat-break engine, `FindColumnListEnd` |
| B3 | `MatchParen`, `SplitTopLevelCommas`, `StartsSubquery`, `FindTopLevelAs` |
| B4 | the CASE-as-`OtherKeyword` gap closed (CASE gets real layout) |
| B5 | `TryFormatExecuteBlockHeader` |

`SqlAliasResolver` (the legacy 4th walker) retires once `PredicateExtractor` reads the Semantic
Model instead — after B2/B3.

---

## 5. Migration contract

### 5.1 Per-construct milestone recipe (five steps)

1. **Parser** produces the new node(s), gated behind a **§0 differential test** (§5.2).
2. **Binder** migrates to consume the node in the **same** milestone → its token walk for that
   construct is deleted.
3. **New features** (debugger, folding, breadcrumbs, diagnostics) consume the node immediately —
   never a token walk.
4. **Formatter** migrates *that construct only* to consume the node (deleting that construct's
   emitter/scan). No big-bang rewrite.
5. **Tests** pin the node, the binder behavior, and formatter byte-parity (or the intended new
   layout, for CASE / nesting).

### 5.2 The §0 differential gate (the safety mechanism)

Every parser change must prove, before merge:
- **Token round-trip is byte-identical** — the node's backing tokens reproduce the source exactly.
- **Formatter output is unchanged** for constructs not deliberately being re-laid-out (the existing
  ~3,600-test suite + the lexeme-preservation invariant are the gate).
- **Binder behavior is unchanged or improved** — pinned by `SemanticModelTests`.

Anything the deepened parser cannot model becomes `RawStatement` (verbatim). Uncertainty ⇒ preserve.

### 5.3 Coexistence is allowed, permanence is not

During the etap the deepened parser and residual token-walks coexist (the transitional layer). This
is acceptable **only** because each milestone deletes its predecessor. No transitional class names
(`V2`/`NewX`/`Temp`) survive a completed milestone (§14 decision #15 of the parent doc).

### 5.4 The headline metric

**After every milestone, the total amount of token-walk structural logic must be strictly lower than
before it.** If a milestone would add net token-walk logic, it is mis-scoped.

---

## 6. Progress matrix — constructs × modeled? × consumers

Update this matrix as each milestone lands. `Token-bag` = a shallow annotation over raw tokens (not a
real child tree). `—` = not applicable / not started.

| Construct | Node today | Target node | Parser | Binder uses node | Formatter uses node | Diagnostics/Folding/Breadcrumbs use node | Debugger step-node | Milestone |
|---|---|---|---|---|---|---|---|---|
| Top-level statement boundaries | ✅ real | (unchanged) | ✅ | ✅ | ✅ | — | — | done (Etap 2) |
| CTE / `WITH` | `WithQuery` + `WithClause` | (unchanged) | ✅ (B3) | ⬜ | ✅ (B3) | ⬜ | — | B3 |
| Nested CTE | `WithQuery` (recursive) | (unchanged) | ✅ (B3) | ⬜ | ⬜ | ⬜ | — | B3 |
| Derived table `FROM (SELECT…)` | `DerivedTable` + `QueryNode` body | (unchanged) | ✅ (B3) | ⬜ | ⬜ | ⬜ | — | B3 |
| `EXISTS (…)` | `ExistsExpression` + `QueryNode` | (unchanged) | ✅ (B3) | ⬜ | ⬜ | ⬜ | — | B3 |
| Scalar subquery | `ScalarSubquery` + `QueryNode` | (unchanged) | ✅ (B3) | ⬜ | ⬜ | ⬜ | — | B3 |
| SELECT clauses (From/Where/…) | `*Clause` nodes | `*Clause` nodes | ✅ (B2) | ⬜ | ⬜ | ⬜ | — | B2 |
| FROM items + joins | `FromItem`/`JoinedTable` | (unchanged) | ✅ (B2) | ⬜ | ⬜ | ⬜ | — | B2 |
| Set-ops (UNION/…) | `SetOperationQuery` | `SetOperationQuery` | ✅ (B2) | ⬜ | ⬜ | ⬜ | — | B2 |
| `INSERT … SELECT` source | `InsertStatement.SourceQuery` | (unchanged) | ✅ (B3.1) | ⬜ | ⬜ | ⬜ | — | B3.1 |
| `UPDATE`/`DELETE`/`UOI` subqueries | `.Subqueries` | (unchanged) | ✅ (B3.1) | ⬜ | ⬜ | ⬜ | — | B3.1 |
| `MERGE USING (…)` source + subqueries | `MergeStatement.SourceQuery`/`.Subqueries` | (unchanged) | ✅ (B3.1) | ⬜ | ⬜ | ⬜ | — | B3.1 |
| `CREATE VIEW … AS` body | `DdlStatement.Query` | (unchanged) | ✅ (B3.1) | ⬜ | ⬜ | ⬜ | — | B3.1 |
| PSQL `FOR SELECT` cursor | `ForSelectStatement.Query` | (unchanged) | ✅ (B3.1) | ⬜ | ⬜ | ⬜ | ✅ | B3.1 |
| PSQL `DECLARE CURSOR` query | `DeclareCursorStatement.Query` | (unchanged) | ✅ (B3.1) | ⬜ | ⬜ | ⬜ | — | B3.1 |
| PSQL DML/`SELECT…INTO` body statement | reused DML/SELECT node | (unchanged) | ✅ (B5) | ✅ (neutral) | ⬜ | ⬜ | ✅ | B5 |
| CASE | `CaseExpression` (+`WhenClause`) | (unchanged) | ✅ (B4) | ⬜ | ⬜ | ⬜ | — | B4 |
| `BEGIN/END` block | `BlockStatement` | `BlockStatement` | ✅ (B1a/prep) | ✅ (B1b) | ⬜ | ⬜ | ✅ | B1 |
| `IF` | `IfStatement` | `IfStatement` | ✅ (B1a/prep) | ✅ (B1b) | ⬜ | ⬜ | ✅ | B1 |
| `WHILE` | `WhileStatement` | `WhileStatement` | ✅ (B1a/prep) | ✅ (B1b) | ⬜ | ⬜ | ✅ | B1 |
| `FOR SELECT` / `FOR EXECUTE` | `ForSelectStatement` | `ForSelectStatement` | ✅ (B1a/prep) | ✅ (B1b) | ⬜ | ⬜ | ✅ | B1 |
| Executable leaf statements | `PsqlLeafStatement` | `PsqlStatement` leaves | ✅ (B1a/prep) | ✅ (B1b) | ⬜ | ⬜ | ✅ | B1 |
| `DECLARE` var / cursor | `DeclareVariable/Cursor` | `DeclareVariable/Cursor` | ✅ (B1b-prep) | ✅ (B1b) | ⬜ | ⬜ | — | B1 |
| `EXECUTE BLOCK` body | `Body : BlockStatement` | `Body : BlockStatement` | ✅ (B1b-prep) | ✅ (B1b) | ⬜ | ⬜ | ✅ | B1 |
| Routine definition body | `Body : BlockStatement` | `Body : BlockStatement` | ✅ (B1b-prep) | ✅ (B1b) | ⬜ | ⬜ | ✅ | B1 |

> B1 progress note: the PSQL body tree is produced for **all four surfaces** the binder walks — anonymous
> block (B1a), plus `CREATE PROCEDURE/FUNCTION/TRIGGER` + `EXECUTE BLOCK` bodies and the `DECLARE` section
> (B1b-prep) — and the **binder now CONSUMES the tree (B1b): its structural token walker is deleted.**
> Binder ✅ across every PSQL row. **Remaining ⬜: the formatter still owns PSQL layout via tokens
> (`EmitForSelect`/`EmitPsqlUnit`) — converged per-construct in a later step; feature columns light up in
> Stage 7.**

Legend: ✅ done · ⬜ planned · ⚠️ partial (token-bag / scope-only) · ❌ not modeled.

---

## 7. Debugger considerations (design once, now)

A debugger needs three things this AST provides by construction:

1. **Breakpoint targets** — every executable statement is a `PsqlStatement` with a source `Span`;
   breakpoints attach to node spans, not re-scanned text.
2. **Step semantics** — the tree distinguishes step-over (skip a `BlockStatement`/`WhileStatement`
   body) from step-into (descend into a called routine or a `ForSelectStatement` body). Impossible
   with today's leaf-statement blob.
3. **Execution ↔ source mapping** — a stable node identity (structural index path from the routine
   root) maps an engine-reported position back to a node across edits.

**Design-in-now caveat:** Firebird reports PSQL positions **relative to the routine body**.
`PsqlStatement.Span` must stay absolute-in-editor **and** expose a body-relative offset, so the
debugger can translate engine positions. Cheap to carry from B1; painful to retrofit later. Model
both from the first PSQL milestone.

The debugger is **not built in this etap** — but modeling executable nodes once here prevents it
from ever becoming a fifth token walker.

---

## 8. What runs independently of this etap

- **Pure-visual polish — anytime:** occurrence-highlight palette bump (fill alpha `#38…`/`#2E…`
  ≈18–22% → ~`#55…`, both themes; `Colors.axaml` only) and parenthesis-match highlight (lexer tokens
  only — no AST). BEGIN/END matching is best done *after B1* (reads block nodes) but can ship earlier
  off the existing `Block` scopes if wanted sooner.
- **Snippets (#5) — after B1/B2** (context-gating uses real structural position): extend
  `SnippetEngine` / `SnippetCompletionData` (mirrored stops, `$0` final caret, choice stops, broader
  library) and surface the metadata-driven `ISqlTemplate` templates through completion. **No new
  mechanism** — both existing template systems already share `SqlSnippet` primitives.

---

## 9. Immediate hygiene (safe now, no dependency)

- **Fix the literal NUL byte** in `SemanticBinder.Query.cs` — `var key = table + "<NUL>" + column;`
  embeds a raw `\0` in the source (composite cache key) instead of the `"\0"` escape or a tuple key.
  It compiles, but git flags the file as binary and it breaks grep/diff tooling.
- **Leave documented for removal at the right milestone:** the dead alias path in
  `EditorLanguageService` (`Aliases`/`AliasesFresh`/`EnsureFreshAliases` + `_aliases`/`_aliasesVersion`
  + the `SqlAliasResolver.ParseAliases` calls — no consumer since Etap 5/M5) → remove once B2 confirms
  no consumer remains; the empty parser-diagnostics "merge" concept → simplified in Stage 7 (diagnostics
  are semantic-only).

---

## 10. Milestones B0–B5 (detail)

Each milestone: **complete + tested + smoke-verified before the next** (staged-implementation
contract). Prefer more, smaller steps within a milestone over one large change.

### B0 — Scaffolding + §0 differential gate — ✅ DONE (2026-07-14)
- **Goal:** introduce the node base types (`QueryNode`, `PsqlStatement` base, marker interfaces), the
  recursive-descent scaffolding, and the **differential-test harness** (parse → node tree → assert
  byte-identical token round-trip; assert formatter output unchanged). No consumer migration yet.
- **Dependencies:** none.
- **Architectural impact:** establishes the safety net every later milestone relies on.
- **Removes:** the dead alias path (below); also did the §9 NUL-byte hygiene fix here.
- **Testing:** the harness itself, plus a corpus (Lab DB scripts + real ERP SQL) proving round-trip.
- **As-built:** three new pure-Core abstractions — `Ast/QueryNode.cs` (abstract `SqlNode`),
  `Ast/PsqlStatement.cs` (abstract `SqlNode`, debugger step-unit base), `Ast/IExecutableStatement.cs`
  (debugger step marker exposing the span). `SqlParser` made `partial` (extension seam for
  `SqlParser.Psql.cs` / `SqlParser.Query.cs`); class doc records the seam. Differential harness =
  `tests/…/StructuralAstDifferentialTests.cs` (strict+lenient round-trip byte-identity, tree
  well-formedness — child spans nest, source order, no-throw traversal, over the whole corpus — plus
  extension-point contract facts), drawing from a new shared `tests/…/SqlTestCorpus.cs`
  (`Representative` = the ex-`SqlFormatterInvariantsTests` list, now referenced by it, + a new
  `StructuralConstructs` set: nested CTE, CASE, derived table, EXISTS, scalar subquery, FOR SELECT,
  nested IF/WHILE, set-op). **Cleanups landed:** the NUL byte in `SemanticBinder.Query.cs` (now
  `table + (char)0 + column`, pure-ASCII source); the dead alias path in `EditorLanguageService`
  (`Aliases`/`AliasesFresh`/`EnsureFreshAliases` + `_aliases`/`_aliasesVersion`/`EmptyAliases` + both
  `SqlAliasResolver.ParseAliases` calls — retired at Etap 5/M5, zero consumers) — so `SqlAliasResolver`
  is no longer referenced by the editor path (its only live consumer is now `PredicateExtractor`).
  **Verification:** build 0/0, 3841 main + 23 probe tests green, app smoke clean. **No formatter output
  changed, no semantic behaviour changed** (pure refactoring).

### B1 — PSQL body tree (debugger foundation) — ✅ DONE (B1a/B1b-prep 2026-07-14; B1b 2026-07-15)
- **Goal:** `BlockStatement` / `IfStatement` / `WhileStatement` / `ForSelectStatement`, executable
  leaves (`PsqlLeafStatement`), and declarations — each a `PsqlStatement` with an absolute (+ later
  body-relative) span.
- **Dependencies:** B0.
- **Migrates:** `SemanticBinder.Psql` → consumes nodes; delete its structural token walk. *(B1b DONE
  2026-07-15 — the structural walker is deleted; the binder is a pure AST consumer.)*
- **Formatter:** incrementally, per construct, retire `EmitForSelect` / `EmitPsqlUnit` (+ helpers) as
  each becomes a node consumer, behind the byte-identity gate — never a big-bang rewrite (user directive).
  *(Not started — the formatter still owns PSQL layout via tokens; that is fine as a transitional state.)*
- **Unlocks:** Debugger step-nodes, PSQL folding + breadcrumbs, BEGIN/END matching from nodes.
- **Testing:** `SemanticModelTests`, formatter byte-parity, node/span tests incl. body-relative offset.
- **B1a — as-built (parser producer, additive; DONE 2026-07-14).** Added the PSQL body node hierarchy
  (`Ast/PsqlNodes.cs`: `BlockStatement`, `IfStatement`, `WhileStatement`, `ForSelectStatement`,
  `PsqlLeafStatement` + `PsqlLeafKind`; `If/While/For/Leaf` implement `IExecutableStatement` = debugger
  step units) and the body sub-parser (`SqlParser.Psql.cs`, the `partial` seam from B0). It parses an
  **`AnonymousBlockStatement`** slice (the `BEGIN…END` shape the routine BODY editors hold, gotcha #114)
  into a `BlockStatement` tree, attached as the statement's `Body` child. **Additive only** — the token
  slice still round-trips (§0); the binder + formatter are UNCHANGED (they still token-walk — the
  transitional coexistence). Two invariants hold *by construction* regardless of grammar-recognition
  fidelity: node spans are computed from the exact consumed token range (children always nest + are in
  source order — pinned by the differential harness), and no token is dropped (round-trip is
  token-based); anything unrecognised → `PsqlLeafStatement` (PSQL-level §0 valve). The recognition
  **mirrors the formatter's `EmitPsqlUnit`** so structure matches established behaviour. **Dedup:** reuses
  the existing `Sub` token-range helper (no second copy). **Scope not yet done (next steps):** wire the
  same parser into `EXECUTE BLOCK` + `CREATE PROCEDURE/FUNCTION/TRIGGER` bodies (there the `DECLARE`
  section is inside the one statement — declaration nodes are added then, deliberately deferred out of
  B1a as they aren't exercised on the anonymous-block surface); then **B1b** migrates the binder.
  **Verification:** build 0/0, 3850 main + 23 probe green (+9 `PsqlAstTests`), smoke clean; no
  formatter/semantic behaviour changed.
- **B1b-prep — as-built (all PSQL surfaces + declarations; DONE 2026-07-14).** Reading the binder for
  B1b showed it walks **four** surfaces (`CREATE PROCEDURE/FUNCTION`, `CREATE TRIGGER`, `EXECUTE BLOCK`,
  anonymous block) plus a `DECLARE` section — so *retiring the walker completely* needs the AST to cover
  all of them first. This step does that, still additive: a shared `ParsePsqlBody` (declares + block) and
  `ParseRoutineBody` (skips the header to the top-level `AS`, then the body) now attach a `Body`
  `BlockStatement` to `DdlStatement` (PSQL proc/func/trigger — not PACKAGE, not DROP/non-PSQL) and
  `ExecuteBlockStatement`, alongside `AnonymousBlockStatement`. Re-introduced `DeclareVariableStatement` /
  `DeclareCursorStatement` + `BlockStatement.Declarations` (now genuinely exercised — declares live inside
  these single statements). Binder + formatter still unchanged (they token-walk — coexistence). §0 holds:
  round-trip + tree well-formedness auto-checked by the differential harness over the routine/EB corpus
  cases; no production consumer walks AST children so the `Body` overlays are behaviour-neutral.
  **Verification:** build 0/0, 3857 main + 23 probe green (+7 `PsqlAstTests`), smoke clean; no
  formatter/semantic behaviour changed. **B1b proper (the binder visitor migration that retires the
  structural walker) is the next step.**
- **B1b — as-built (binder is now an AST consumer; the structural walker is DELETED; DONE 2026-07-15).**
  `SemanticBinder.Psql` no longer re-derives the body's structure from tokens. New traversal:
  `BindBody` → `BindBlock` (declarations then statements) → `BindPsqlStatement` (a visitor switching on
  `BlockStatement`/`IfStatement`/`WhileStatement`/`ForSelectStatement`/`Declare*`/`PsqlLeafStatement`).
  A control-flow node's OWN tokens (its condition / cursor query + INTO — everything before its first
  child) are bound by `BindControlHeader`; its child statements are recursed into. `BindDeclaration`
  declares the variable/cursor symbol from the declaration node's own tokens (the node type already
  distinguishes them — no `CURSOR`-keyword scan). The four entry points (`BindRoutineDefinition`,
  `BindTriggerDefinition`, `BindExecuteBlock`, `BindAnonymousBlock`) parse only the HEADER (params /
  RETURNS / trigger table / NEW·OLD·predicates — the signature, not the body) from tokens, then call
  `BindBody(node.Body, …)`. **Deleted (the structural PSQL walker):** `BindRoutineBody`,
  `ScanDeclarations`, `FirstTopLevelBegin`, `FindTopLevelSemicolon`, `ContainsKeyword`,
  `SkipLocalSubprogram`, `MatchingEndExclusive` (~113 lines of BEGIN/END matching, declaration-boundary
  scanning, and local-subprogram skipping — every "find the block by counting tokens" routine). The old
  flat body scan `BindBodyReferences` is retained (renamed `BindLeafReferences`) and now runs **per node
  range**, not once over the whole body — it is the leaf-INTERIOR reference binder (subquery / SELECT…INTO
  / dotted / bare-local), which is ordinary/query-expression depth (B2/B3), not PSQL body structure. The
  reference set is identical because every body token belongs to exactly one node and the per-token
  binding is unchanged. **Behaviour delta (documented, negligible):** a local `DECLARE PROCEDURE/FUNCTION`
  subprogram body is now traversed against the enclosing routine scope (the tree models it as a
  header-leaf + nested block in `Statements`); the old walker skipped it wholesale. Effect is limited to a
  possible extra same-named-variable occurrence on the rare FB4+ local-subprogram surface (untested;
  proper sub-routine scoping is B5+). **Verification:** build 0/0, 3864 main + 23 probe green (+3
  `SemanticModelTests` pinning nested IF/WHILE, FOR SELECT…INTO, and a mixed DECLARE section), app smoke
  clean. Completion / highlighting / navigation / Quick Info consume the same model API — unchanged.

### B2 — Query clause tree — 🚧 parser-producer DONE (2026-07-15); binder/formatter convergence pending
- **Goal:** `SelectClause`/`FromClause`/`WhereClause`/`GroupByClause`/`HavingClause`/`OrderByClause`,
  `FromItem` (table / joined), `SetOperation`.
- **Dependencies:** B0 (independent of B1; can parallelize).
- **Migrates:** `SemanticBinder.Query` → walk nodes; delete `CollectTables`/`BindQuery` token
  recursion. *(Pending — deferred by user directive to build the full structural model first.)*
- **Formatter:** begin **nested-query indentation (#2)** for top-level clauses; retire
  `MatchStructuralPhrase` flat-break engine + `FindColumnListEnd`. *(Pending — same deferral.)*
- **Unlocks:** query folding/breadcrumbs, clause-aware diagnostics; enables `SqlAliasResolver`
  retirement once `PredicateExtractor` moves to the model.
- **Testing:** binder tests, formatter layout tests (new indentation), idempotency/round-trip.
- **B2 — as-built (parser producer, additive; DONE 2026-07-15).** New nodes in `Ast/QueryNodes.cs`: the
  concrete `SelectQuery` / `SetOperationQuery` (`QueryNode` subclasses), the `QueryClause` base +
  `SelectClause`/`FromClause`/`WhereClause`/`GroupByClause`/`HavingClause`/`OrderByClause`, and the
  `FromItem` base + `TableReference`/`DerivedTable`/`JoinedTable` (with `SetOperator` + `JoinKind` enums).
  New sub-parser `SqlParser.Query.cs` (the B0 `partial` seam): `TryParseSelectQuery` → `ParseQuery`
  (SELECT cores chained by set operators, left-associative, with a trailing ORDER BY that hangs on the
  whole query / set operation) → `ParseSelectCore` (depth-0 clause-boundary scan) → `ParseFromItems` /
  `ParseFromItem` (comma-separated entries; JOINs nest left-associatively; ON/USING captured as token
  fragments). Wired into `Classify` so a **plain (non-`WITH`) `SelectStatement`** exposes a `Query` child.
  **Additive only** — binder + formatter UNCHANGED (they still token-walk — the transitional coexistence,
  §5.3); the token slice still round-trips (§0). Invariants hold BY CONSTRUCTION: every node's span is
  `TokenSpan` of its exact consumed range (children nest + source-ordered — pinned by the differential
  harness), no token dropped, and any shape not cleanly recognised leaves `SelectStatement.Query` null
  (never lost). **Depth = structural:** clause interiors (projection items, predicates, ORDER BY terms, a
  join's ON) stay in the clause node's `Tokens`; nested subqueries (derived-table body, EXISTS/scalar
  subquery, CTE body) are NOT recursed into — that is B3. **Scope (this step):** plain top-level SELECT
  only. `WITH`-led queries keep the `WithClause` token bag (their main query becomes a `QueryNode` in B3,
  so no double representation now); INSERT…SELECT / CREATE VIEW…AS reuse the same parser in B3.
  **Dedup:** the token-range→span helper `PsqlSpan` was generalised to a shared `TokenSpan` in
  `SqlParser.cs` (one implementation for the PSQL and query sub-parsers); the query parser reuses the
  existing `Sub` / `MatchParenTok` / `Kw` / `At` helpers (no second copy). **Verification:** build 0/0,
  3896 main + 23 probe green (+14 `QueryAstTests`, +5 corpus shapes), app smoke clean; no
  formatter/semantic behaviour changed. **Next: B2 convergence (binder `SemanticBinder.Query` → node
  consumer, retiring `CollectTables`/`BindQuery` recursion; then formatter) OR B3 — on the user's
  go-ahead.**

### B3 — Subqueries + CTE-as-QueryNode (incl. nested CTE) — 🚧 parser-producer DONE (2026-07-15)
- **Goal:** `DerivedTable` / `ExistsExpression` / `ScalarSubquery` holding real `QueryNode`s; promote
  `CommonTableExpression.Body` + `WithClause.MainQuery` to `QueryNode`s → nested CTEs work.
- **Dependencies:** B2 (reuses `QueryNode`).
- **Migrates:** binder recursion → node traversal; fixes subquery-blindness (gotcha #18) properly.
  *(Pending — deferred by user directive to build the full structural model first.)*
- **Formatter:** **nested-query indentation (#2)** for subqueries/derived/EXISTS with a **capped**
  depth (avoid staircase); retire `MatchParen`/`SplitTopLevelCommas`/`StartsSubquery`/`FindTopLevelAs`.
  *(Pending — same deferral. B3 did do the one forced, byte-identical formatter accessor swap; see below.)*
- **Unlocks:** scope-accurate completion inside subqueries/CTEs.
- **Testing:** nested-CTE + derived-table corpus; capped-indent layout tests; idempotency.
- **B3 — as-built (recursive query model, additive; DONE 2026-07-15).** The query model is now fully
  recursive. **New nodes** (`Ast/QueryNodes.cs`): `WithQuery` (`WithClause` CTE declarations + main
  `QueryNode`), `RawQuery` (the query-level §0 valve, like `RawStatement`), and the
  `SubqueryExpression` base + `ExistsExpression` / `ScalarSubquery` (each owning a `QueryNode`). **Promoted**
  (`Ast/CteNodes.cs`): `CommonTableExpression.BodyTokens` → `Body` (a real `QueryNode`); `WithClause` lost
  `MainQueryTokens` (the main query now lives on `WithQuery.Query`) — **no parallel representation**.
  `QueryNode` gained a `Tokens` property on the base (every query node reproduces its exact source range,
  which the formatter relies on), and `SelectQuery`/`SetOperationQuery`'s own `Tokens` were pulled up to it
  (dedup). `SelectStatement.With` was **deleted** — a WITH-led statement's `Query` is now a `WithQuery`
  (one representation everywhere: top-level, CTE body, derived table, subquery). **Parser** (`SqlParser.Query.cs`):
  `ParseQueryRange(t, lo, hi)` is the single recursive entry (WITH → `ParseWithQuery`; SELECT →
  `ParseSetQuery`; unwraps a fully-parenthesised query; `RawQuery` valve — never null), reused by CTE
  bodies, derived tables, and `ParseEmbeddedSubqueries` (one scan that finds EXISTS / scalar / IN /
  quantified subqueries in a clause interior, descending through ordinary parens but never into a
  subquery — recursion covers that). Each non-FROM clause now carries its embedded subqueries as children;
  `DerivedTable` carries its inner `Query`; a `JoinedTable` carries subqueries found in its `ON`.
  `TryParseWithClause` was deleted from `SqlParser.cs` (WITH parsing consolidated into the query
  sub-parser). **Formatter:** ONE forced, byte-identical accessor swap — `FormatWithClause` now reads the
  promoted nodes (`cte.Body.Tokens`, `wq.Query.Tokens`) and the dispatcher matches
  `SelectStatement { Query: WithQuery }`; it emits the **exact same token ranges** the old token bags held,
  so output is unchanged (proven by the formatter invariants + idempotency + the per-statement lexeme net).
  This is a promotion-forced accessor update, **not** a layout migration — the only way to promote the WITH
  token-bag (which the milestone requires) without leaving a parallel representation. §0: additive overlay,
  round-trip + tree well-formedness auto-checked by the differential harness over the (extended) corpus.
  **Verification:** build 0/0, 3913 main + 23 probe green (+ new `QueryAstTests` for derived/EXISTS/scalar/
  nested-CTE + updated WITH parser tests + 3 corpus shapes), app smoke clean; **no formatter/semantic
  behaviour changed**. Remaining structural gaps documented in §12. **Next: B2/B3 binder + formatter
  convergence, or B4 (CASE) — on the user's go-ahead.**

### B3.1 — Embedded-statement queries — 🚧 parser-producer DONE (2026-07-15)
- **Goal:** attach a real `QueryNode` to every query that is a clause/part of ANOTHER statement, so the
  parser is the single structural source for *every* query, not only standalone `SELECT`/`WITH` and their
  nesting (closes §12 gap #1).
- **Dependencies:** B3 (reuses `ParseQueryRange` / `ParseEmbeddedSubqueries`).
- **Migrates:** nothing yet — additive/producer-only (binder + formatter still token-walk; convergence
  deferred by user directive, same as B2/B3).
- **B3.1 — as-built (additive; DONE 2026-07-15).** New sub-parser `SqlParser.Dml.cs` (the B0 `partial`
  seam) plus small PSQL/DDL wiring. Attachments:
  - **`InsertStatement.SourceQuery`** (`INSERT … SELECT/WITH`, capped before a top-level `RETURNING`) +
    **`.Subqueries`** (scalar subqueries in `VALUES`/`RETURNING`). The two never overlap: with a source
    query, only the `RETURNING` region is scanned for incidental subqueries.
  - **`UpdateStatement` / `UpdateOrInsertStatement` / `DeleteStatement` `.Subqueries`** — every embedded
    `EXISTS`/scalar/`IN`/quantified subquery in the `SET`/`WHERE`/`VALUES`/`RETURNING` expressions.
  - **`MergeStatement.SourceQuery`** (`USING ( <query> )`; null for a bare-table source) + **`.Subqueries`**
    (the `ON`/`WHEN` conditions and `UPDATE SET`/`INSERT VALUES` expressions). The incidental scan starts
    *past* the source parens, so the `USING` query is never also re-found as a scalar subquery (no double
    representation).
  - **`DdlStatement.Query`** — the `CREATE/CREATE OR ALTER/ALTER/RECREATE VIEW … AS <query>` body
    (mutually exclusive with the PSQL `Body`; `Children` returns whichever is present). Handles WITH-led and
    set-operation view bodies (they route through the one recursive `ParseQueryRange`).
  - **PSQL `ForSelectStatement.Query`** — the `FOR SELECT/WITH … [INTO …] [AS CURSOR c] DO` cursor query
    (null for `FOR EXECUTE STATEMENT`). Boundary scan stops at a depth-0 `INTO` or `AS CURSOR`, never a
    column-alias's own `AS` (only `AS` immediately followed by `CURSOR`). Children are cursor-then-body.
  - **PSQL `DeclareCursorStatement.Query`** — the `DECLARE … CURSOR FOR [(] <query> [)]` query (both
    parenthesised and bare forms).
  All spans are `TokenSpan` of the exact consumed range (children nest + source-ordered by construction);
  a shape not cleanly recognised leaves the slot null (never lost — §0). **No new representations:** each
  embedded query is modelled ONCE as a `QueryNode`; the statement's `Tokens` remain the lossless §0 backing
  every node carries, not a parallel structural model. Shared child-ordering moved to `Ast/AstChildren.cs`
  (one helper, no per-node duplication).
- **Robustness fix (B2, forced by B3.1):** `ParseSetQuery` no longer folds a **dangling set operator**
  (`… UNION ALL` with no following operand, produced by the lenient splitter mid-statement) into a
  degenerate empty `SelectQuery`. Such a `[0,0)` operand only *accidentally* passed tree well-formedness
  when the query started at offset 0; a set-op VIEW body (starting mid-statement) exposed it. The dangling
  operator's tokens now stay in the node's range (§0), no empty operand node is built.
- **Verification:** build 0/0, **3978 main + 23 probe green** (+`DmlQueryAstTests`, +14 corpus shapes),
  app smoke clean; **no formatter/semantic behaviour changed** (both still token-walk these statements —
  the additive overlay is behaviour-neutral).
- **Deliberately NOT in B3.1 (→ B5, §12):** a DML/`SELECT … INTO` statement that appears as a **PSQL body
  leaf** stays a `PsqlLeafStatement`. Modelling its query now would require a *second* way to represent a
  DML query (leaf-with-children vs `InsertStatement.SourceQuery`) — exactly the parallel representation the
  §0/naming discipline forbids. The correct fix is B5, where PSQL DML leaves become the reused DML statement
  nodes (§3.2) and inherit the SAME attachment logic.

### B4 — CASE node — 🚧 parser-producer DONE (2026-07-15)
- **Goal:** `CaseExpression` (+ `WhenClause`), simple and searched, in both SELECT-expression and PSQL
  forms.
- **Dependencies:** B2 (SELECT clause) for the expression-position case.
- **B4 — as-built (additive; DONE 2026-07-15).** New nodes `Ast/ExpressionNodes.cs`: `CaseExpression`
  (`IsSearched`, `Whens`, recursive `Children`) + `WhenClause`. The B3 clause-interior scan was generalised
  from `ParseEmbeddedSubqueries` to **`ParseEmbeddedExpressions`** — it now finds BOTH subqueries (B3) and
  CASE (B4) in an expression range, recursively: on `CASE` it matches the `END` (nested-CASE-depth aware,
  `MatchCaseEnd`), builds the node (`ParseCaseExpression` splits operand / WHEN…THEN arms / ELSE at
  paren+case depth 0), and each arm/operand/ELSE is itself scanned so a subquery or nested CASE inside a
  branch stays a real node. Because every clause and DML statement already runs that scan, CASE is modelled
  everywhere it structurally appears — SELECT projection/WHERE/…, UPDATE/DELETE/MERGE/INSERT expressions —
  and `PsqlLeafStatement` now carries the same embedded-expression children, so a CASE in a PSQL assignment
  / `RETURN` is modelled too. **Additive** — the formatter still treats CASE tokens inline
  (`OtherKeywords`); no layout change. A CASE whose END is unmatched is simply not turned into a node
  (tokens untouched, §0).
- **Formatter (DEFERRED):** structured WHEN/THEN/ELSE layout + adaptive wrap — closes the
  CASE-as-`OtherKeyword` gap. Part of the deferred formatter convergence (the P8 item that waited for this
  node), not done in B4.
- **Unlocks:** CASE diagnostics (unreachable ELSE, etc.); CASE layout (on formatter convergence).
- **Verification:** build 0/0, tests green (+`CaseAstTests`, +5 corpus shapes); no formatter/semantic
  behaviour changed.

### B5 — Routine/PSQL body statements = reused DSQL nodes — 🚧 parser-producer DONE (2026-07-15)
- **Goal:** an embedded DSQL statement inside a PSQL body (SELECT / INSERT / UPDATE / DELETE / MERGE /
  EXECUTE) is the **reused** top-level statement node (design §3.2), carrying its full B2/B3/B3.1 query
  structure — so a DML query inside a routine body is the SAME node, modelled the SAME way, as at the top
  level. This closes the last "query on tokens" residual (§12 #1). *(Routine/EXECUTE-BLOCK `Body :
  BlockStatement` was already produced in B1b-prep; B5 is the leaf-promotion + debugger-coverage piece.)*
- **Dependencies:** B1 (`BlockStatement`), B2/B3/B3.1 (the reused nodes' query structure), B4.
- **B5 — as-built (additive; DONE 2026-07-15).** The PSQL body statement / branch slots were widened from
  `PsqlStatement` to **`SqlNode`** (`BlockStatement.Statements`, `IfStatement.Then/Else`,
  `WhileStatement.Body`, `ForSelectStatement.Body`) so they can hold a reused DSQL `SqlStatement`.
  `ParsePsqlLeaf` now routes a leaf whose leading keyword is a DSQL verb through the top-level `Classify`
  (producing `InsertStatement`/`SelectStatement`/… with their query children); a PSQL-only leaf
  (assignment, SUSPEND, EXIT, LEAVE, POST_EVENT, EXCEPTION, RETURN, subprogram header) stays a
  `PsqlLeafStatement`. The reused DSQL statement nodes now implement **`IExecutableStatement`** (debugger
  step markers), completing executable-node coverage across every PSQL surface. `PsqlLeafKind` dropped its
  now-impossible DSQL members (Insert/Update/Delete/Merge/Select/Execute*), and `ClassifyLeaf` its dead
  cases. **Binder stays behaviour-neutral:** `BindPsqlStatement` accepts `SqlNode` and binds a reused DSQL
  node via the SAME `BindLeafReferences` over its tokens as the pre-B5 leaf scan — identical reference set
  (the node-based binding is the deferred convergence). **Formatter unaffected** (its PSQL emitter is
  token-based, does not read the body tree) — output byte-identical.
- **Formatter / binder node-consumption (DEFERRED):** part of convergence.
- **Unlocks:** debugger executable-node coverage across every PSQL surface; DML-in-body diagnostics on the
  same query nodes as top-level DML.
- **Verification:** build 0/0, **4008 main + 23 probe green** (+`PsqlAstTests` B5 case, updated leaf-kind
  test), app smoke clean; no formatter/semantic behaviour changed.

---

## 11. After Etap 6.9

Stage 7 (Diagnostics → Folding → Breadcrumbs) and, later, the Debugger, are built as pure clients of
this tree — see [`editor-stage7-diagnostics.md`](editor-stage7-diagnostics.md). Formatter convergence
continues opportunistically for any remaining constructs (UPDATE SET, MERGE) if a feature needs them.

---

## 12. Identified structural gaps (post-B3.1 audit — complete the AST before consumers migrate)

A careful audit of the query AST, recorded now because it is far cheaper to complete the AST **before**
the binder/formatter migrate onto it than to revisit the parser afterward. After **B3.1** the claim "the
parser is the single structural source of every query" is TRUE for **every query reachable from a top-level
statement or a PSQL control-flow node** — a standalone `SELECT`/`WITH` and its arbitrarily-nested
subqueries/CTEs (B2+B3), an `INSERT`/`MERGE` source, a `CREATE VIEW` body, an `UPDATE`/`DELETE`/`MERGE`
embedded subquery, and a PSQL `FOR SELECT` / `DECLARE CURSOR` query (B3.1). The gaps below are where a query
or a structurally-meaningful construct is still token-only. Ordered by impact.

1. ~~**Queries embedded as clauses of other statements are not `QueryNode`s yet.**~~ **CLOSED — B3.1
   (top-level embedded queries) + B5 (PSQL body DML)** (2026-07-15). `INSERT … SELECT`, `CREATE VIEW … AS`,
   `MERGE USING (…)`, `UPDATE`/`DELETE`/`MERGE` embedded subqueries, and the PSQL `FOR SELECT` cursor +
   `DECLARE CURSOR` query all hold real `QueryNode`s (B3.1); and a DML/`SELECT` statement inside a PSQL body
   is now the **reused** top-level statement node with its full query structure (B5), so there is exactly
   ONE representation of a DML query whether it sits at the top level or in a routine body. **The parser is
   the single structural source for every query in EmberTern.**
1a. **`EXECUTE STATEMENT '<sql>'` is never a `QueryNode` — and never will be (a conscious boundary, not
   debt).** Its SQL is a runtime string literal (often dynamic), not statically parseable text; there is no
   query to model. Listed only so the audit is exhaustive.
2. **Parenthesised set-operation operands are shallow.** `(SELECT a) UNION (SELECT b)` parses the operands
   as shallow `SelectClause`s (a leading `(` isn't unwrapped inside `ParseSetQuery`). Lossless (§0) but not
   deep. Cheap to fix by unwrapping a fully-parenthesised operand in `ParseSelectCore`.
3. **`DISTINCT` / `FIRST n` / `SKIP n` / `ALL` are not surfaced.** They live in `SelectClause.Tokens`; a
   consumer must scan tokens to answer "is this DISTINCT?" (matters for a "ORDER BY expr not in SELECT
   DISTINCT" diagnostic). Add boolean/limit facts to `SelectClause` when a feature needs them.
4. **Projection / GROUP BY / ORDER BY items are not split into per-item nodes.** By the agreed structural-
   depth boundary these are expressions (token fragments) — intentional, not debt. But the *output column
   alias* (`expr AS name`) is structural; a future "rename column" / "unused select item" feature will want
   a `SelectItem { Expr tokens, Alias }` node. Note as a boundary decision to revisit per-feature.
5. **`JOIN … USING (cols)` column list is unstructured** (kept as `OnTokens`); and `NATURAL LEFT/RIGHT`
   collapses to `JoinKind.Natural` (direction dropped). Minor; revisit if a join-aware feature needs it.
6. **Expression-level constructs stay tokens by design** — `CASE` is now a node (B4); window functions
   (`OVER`), `CAST`, aggregate `DISTINCT` remain fragments unless a feature requires them (structural-depth
   boundary).
7. **Consumer-side structural duplication (NOT a parser gap — the deferred convergence).** The AST now
   holds the full structure once, but two consumers still RE-DERIVE structure from tokens: the binder's
   query/DML walk (`SemanticBinder.Query` `BindQuery`/`ParseCteList`/`CollectTables`, `SemanticBinder.Dml`
   `BindDml`) and the entire formatter layout engine (clause breaks, list builders, PSQL block emitter,
   CASE-inline). The legacy `SqlAliasResolver` is a third (used only by `PredicateExtractor`, off the editor
   path). Retiring these is **binder convergence** then **formatter convergence** — each deletes a
   structural walker and reads the nodes instead. Until then the structure is represented once (in the tree)
   but derived thrice; that is the remaining work, tracked in the matrix (§6) "uses node" columns.

Impact on the downstream consumers: gap #1 (the one that materially affected **diagnostics** — clause-aware
rules on an INSERT's source or a view body — **breadcrumbs/folding**, and the **debugger** — a PSQL
`FOR SELECT` cursor as a stepable/inspectable node) is now **closed by B3.1** except for the PSQL DML-leaf
interior, which is a B5 item (leaves become reused DML nodes). Gaps #2–#5 are refinements; #6 is a
deliberate boundary; #1a is a permanent boundary. **The binder can now migrate onto a genuinely complete
query source in one pass** (B2/B3 clause + subquery tree, B3.1 embedded-statement queries), rather than
twice — which was the whole point of completing the AST before convergence.

---

## 13. Consumer convergence — status (2026-07-15)

The parser is the single structural source (B0–B5). Migrating the CONSUMERS off their own token walkers
onto that source is the convergence. Status:

### 13.1 Binder — ✅ DONE (2026-07-15)

`SemanticBinder` is a full AST consumer:

- **Query** (`SemanticBinder.Query.cs`) — `BindQueryNode`/`BindQueryInto`/`BindSelectInto` walk the
  `QueryNode` tree: FROM tables come from `FromClause` items (`TableReference`/`DerivedTable`/`JoinedTable`),
  CTEs from `WithClause`, embedded subqueries from each clause's `SubqueryExpression` children (each a
  correlated child scope). The clause interior is walked only for column references (expression-level).
- **DML** (`SemanticBinder.Dml.cs`) — the source query (`InsertStatement.SourceQuery` /
  `MergeStatement.SourceQuery`) and embedded subqueries come from the AST; only the DML TARGET (which has
  no AST node) and the SET/WHERE/VALUES column references are token-level.
- **PSQL** (`SemanticBinder.Psql.cs`) — already consumed the body tree (B1b); now its leaf/header binding
  drives subqueries from the AST too (a reused DSQL leaf's `Query`/`SourceQuery`/`Subqueries`, a
  `PsqlLeafStatement`'s embedded children, an `IF`/`WHILE` condition's `ConditionExpressions`, a
  `ForSelectStatement.Query`). Only the expression interior (`:param`/dotted/bare/INTO) is a token walk.

**Deleted structural token walkers:** `BindQuery` (token), `CollectTables`, `ParseTableList`, `ParseCteList`,
`BindColumnReferences` (the FROM-list + `(SELECT` re-scan), `RangeEndIfInside`, `BeginsSubquery`,
`BindNamedTable`/`BindDerivedTable`/`BindTargetAfter`/`ReadOptionalAlias` (token FROM-item logic),
`IsTableListTerminator`/`TableListTerminators`, and the PSQL `BindLeafReferences`/`FindBodySelectEnd`/
`BindOptionalInto`. What remains is expression-level (column/local/param references) + DML-target
identification (no AST node exists for it) — **not** structural query walkers. One producer refinement
landed with it: `IF`/`WHILE` now carry `ConditionExpressions` (condition subqueries/CASE), and a PSQL
singleton `SELECT … INTO` ends its `QueryNode` before `INTO` (top-level DSQL never has an INTO), so the
INTO targets bind as locals. Behaviour-equivalent: 4008 main + 23 probe green.

### 13.2 Formatter — ✅ DONE (2026-07-15)

`SqlFormatter` is now an **AST-walking layout engine** wherever the parser provides structure. The core is
`EmitQuery(QueryNode)`: it lays out each clause on its own line and recurses into nested queries (derived
table / EXISTS / scalar subquery / IN(SELECT), and a MERGE `USING (…)` source) as **expanded-paren blocks**
at one further indent level, so multi-level queries indent naturally instead of flattening to column 0.
`CaseExpression` is laid out **adaptively** (`EmitCaseChild`/`EmitCaseBlock`) — inline when it is simple
(≤1 WHEN) and fits the width, else a WHEN/THEN/ELSE block. The migration landed **construct by construct**
behind byte-identity / intended-layout gates (F1 nested-query indentation + projection item model, F2 CASE,
F3 WITH/CTE recursion, F4 INSERT…SELECT / CREATE VIEW body / MERGE source / UPDATE·DELETE embedded
subqueries, F5 PSQL body leaves + FOR SELECT cursor). Rendering strategy: everything is emitted
column-0-relative and composed by uniformly shifting a block right (`AppendBlock` / `IndentBlock`), so a
**flat query is byte-identical** to the pre-convergence output (all pre-existing exact tests unchanged)
while a **nested query gains real indentation**, and idempotency holds because layout is a pure function of
the tree. New baselines: `SqlFormatterNestedQueryTests`, `SqlFormatterEmbeddedQueryTests`,
`SqlFormatterPsqlAstTests`, plus a `StructuralConstructs` idempotency/§0 sweep in
`SqlFormatterInvariantsTests`.

**The token emitter (`Emit`) is retained — and this is the correct end state, not residual debt.** It is now
(a) the renderer for a clause/expression **interior** (the structural-depth boundary keeps ordinary
expressions as token fragments — a projection item, a WHERE predicate, an ON condition), into which it
**splices** the embedded structural child nodes (subqueries / CASE) it is handed by span (`StructuralSplices`
+ `EmitStructuralChild`); and (b) the layout for the constructs the parser deliberately does **not** model
structurally — UPDATE `SET` / DELETE / MERGE clause layout (no clause node — an intentional §12 boundary),
and PSQL **PACKAGE** bodies (no `Body` node). The PSQL block structurer (`EmitPsqlUnit`/`EmitForSelect`) is
**kept on purpose**: it is robust to the adversarial malformed-input corpus and handles PACKAGE bodies the
parser leaves unmodelled, and rewriting it to a second AST walker would create exactly the parallel
mechanism §5.3 forbids (there is no full AST for a PACKAGE body). Instead it **delegates each leaf's content
to the AST** (`FormatLeafStatement` → `FormatAstLeaf`, keyed by a leaf-span index) so a DML/SELECT leaf, a
FOR SELECT cursor, and an IF/WHILE/assignment CASE all lay out through the same AST-driven code as the top
level. `MatchStructuralPhrase` / `FindColumnListEnd` likewise survive as the interior/DML layout for the
non-modelled constructs — they are no longer reached for a plain SELECT (which goes through `EmitQuery`), so
there is **one layout mechanism per construct**, no AST + token walker for the same construct.

### 13.3 `SqlAliasResolver`

No longer referenced by the editor path (the binder does not use it). Its ONE remaining consumer is
`PredicateExtractor` (Performance Analysis), off the editor path. Retiring it is a separate
Performance-feature migration (`PredicateExtractor` → the semantic model), not part of the formatter
convergence — so it is intentionally left until that component is migrated.
