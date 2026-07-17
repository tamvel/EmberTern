# SQL Data Export — Design

**Status: COMPLETE (E1–E6) — user-confirmed 2026-07-17.** Works consistently across the SQL Editor and
Table Data grids through one Core pipeline, including the follow-up fix for environments that deliver cell
values as strings.
Milestone: *Stage — SQL Data Export*. Scope: extend result-grid copying into a complete,
format-extensible data-export capability, starting with **Copy as INSERT** and **Copy as UPDATE**.

> **As-built (2026-07-17).** E1–E5 shipped and were verified on a real Firebird engine; **E6 (this
> session)** extended the feature to the **Table Data grid** as a pure adapter — no new format, safety
> rule, or UI concept. The Copy-as-INSERT/UPDATE context menu now works consistently across the SQL
> Editor grid **and** the Table Data grid through **one** Core implementation. See §9.6 for the E6
> as-built shape; §11 for the milestone status.

> **Read §1 first.** Every design decision below rests on measured Firebird/driver behaviour, not
> on inference. Three beliefs that a reasonable design would have assumed turned out to be **false**,
> and two of them are silent data-corruption vectors. Per CLAUDE.md's standing rule — *verify Firebird
> behaviour, never infer it* — this design was written after the probes, not before.

---

## §1 Verification log — measured against a live engine

All probes run against **Firebird 5.0** (`WI-V5.0.3.1683`) via `FirebirdSql.Data.FirebirdClient` 10.3.4
— `Lab/EmberTern_Lab.fdb` for read-only shapes, and throwaway scratch DBs at ASCII paths (gotcha #149)
for anything needing new DDL. The lab DB was **not** modified.

### 1.1 The server *does* report per-column provenance — this is the foundation

`FbDataReader.GetSchemaTable()` returns `BaseTableName` + `BaseColumnName` per output column, sourced
from the server's own XSQLDA. It is **alias-transparent**:

| Query | `ColumnName` | `BaseTableName` | `BaseColumnName` |
|---|---|---|---|
| `select c.CUSTOMER_ID as CID from CUSTOMERS c` | `CID` | `CUSTOMERS` | `CUSTOMER_ID` |
| `select CUSTOMER_ID * 2 as DOUBLED from CUSTOMERS` | `DOUBLED` | *(empty)* | `MULTIPLY` |
| `select count(*) as CNT from CUSTOMERS` | `CNT` | *(empty)* | `COUNT` |
| `select 1 as LITERAL from RDB$DATABASE` | `LITERAL` | *(empty)* | `CONSTANT` |

**Consequences.**
- The request says *"using … visible columns"*. That is subtly wrong and must not be implemented
  literally: a visible column name may be an **alias**. `select NAME as CUSTOMER_NAME from CUSTOMERS`
  must generate `INSERT INTO CUSTOMERS (NAME)`, never `(CUSTOMER_NAME)`. **Generation uses
  `BaseColumnName`; the grid header is display only.**
- **An empty `BaseTableName` is the reliable "this is a derived expression" signal** — *not*
  `IsExpression`, which is `False` for `CUSTOMER_ID * 2` (see 1.3). `BaseColumnName` for a derived
  column is an operator name (`MULTIPLY`, `COUNT`, `CONSTANT`) and is **garbage — never emit it.**

### 1.2 `IsKey` and `IsUnique` are per-column *participation* flags — trusting them corrupts data

This is the single most important finding. Both flags mean *"this column takes part in some key"*,
**not** *"these columns identify a row"*. Both report `True` on an **incomplete** key:

| Shape | Reported | Reality |
|---|---|---|
| `select ORDER_ID, QTY from ORDER_ITEMS` — PK is `(ORDER_ID, LINE_NO)` | `ORDER_ID` **IsKey=True** | `ORDER_ID` alone is **not** unique |
| `select A, D from T` — `UNIQUE(A, B)` | `A` **IsUnique=True** | `A` alone is **not** unique |

Proven concretely, not argued — two rows `(A=1,B=10)`, `(A=1,B=20)`:

```
UPDATE T ... WHERE A = 1  would hit 2 row(s)   <-- A was reported Uniq=True
```

**A WHERE clause built from `IsKey`/`IsUnique` is a multi-row-update bug.** This is exactly the failure
the request names (*"I do not want UPDATE generation that can accidentally modify multiple rows"*), and
it is the default outcome of the obvious implementation.

Further traps in the same family:

- **A UNIQUE column permits multiple NULLs.** Two rows inserted with `C = NULL` into `UNIQUE(C)`;
  `WHERE C IS NULL` → **2 rows**. A nullable unique key is not an identifier when its value is NULL.
- **`WHERE x = NULL` matches nothing** (0 rows, vs 2 for `IS NULL`). NULL needs `IS NULL`.
- **"All visible columns" cannot guarantee one row** — two identical rows,
  `WHERE A=1 and B=2 and TXT is null` → **2 rows**.

### 1.3 Shapes that *masquerade* as a clean single-table result

| Shape | What the driver reports | Why it's dangerous |
|---|---|---|
| `select CUSTOMER_ID, NAME from CUSTOMERS`<br>`union all select PRODUCT_ID, NAME from PRODUCTS` | `CUSTOMERS` / `CUSTOMER_ID` / **IsKey=True** | **Only leg 1 is reported.** A `PRODUCT_ID` value would be written into a *real, wrong* `CUSTOMERS` row. |
| `select a.CUSTOMER_ID, b.CUSTOMER_ID from CUSTOMERS a join CUSTOMERS b …` | both `CUSTOMERS`, both IsKey=True | Self-join — one base table name, **two different row instances**. |
| `select CUSTOMER_ID, CUSTOMER_ID as AGAIN from CUSTOMERS` | both → `CUSTOMER_ID` | Would emit `INSERT … (CUSTOMER_ID, CUSTOMER_ID)` — **invalid SQL**. |
| `select * from SP_CUSTOMER_ORDERS(1)` | `BaseTableName = SP_CUSTOMER_ORDERS` | A **procedure**, not a table. |
| `select * from V_ORDER_DETAILS` | `BaseTableName = V_ORDER_DETAILS` | A 4-table join **view** — indistinguishable from a real table by schema alone. |
| `select CUSTOMER_ID, count(*) from ORDERS group by CUSTOMER_ID` | `ORDERS` (IsKey=False) | An **aggregate** row, not a table row. |

**No amount of schema metadata detects the UNION or the self-join case** — the server reports a clean,
key-complete single-table result. This is why §4 requires a second, independent signal.

Correctly handled by the driver (no veto needed): a **derived table** (`select … from (select … from
CUSTOMERS) x`) reports `CUSTOMERS`/IsKey=True — genuinely a single-table result; and a **LEFT JOIN**
null-extended side reports IsKey=False.

### 1.4 Writability rules

| Attempt | Result |
|---|---|
| `insert into R (A, B, CALC) …` — CALC is `COMPUTED BY` | **FAIL** — *attempted update of read-only column R.CALC* |
| `update R set CALC = 9` | **FAIL** — same |
| `insert into R (ID_ALWAYS, A) values (5, 1)` — `GENERATED ALWAYS AS IDENTITY` | **FAIL** — *OVERRIDING clause should be used …* |
| `insert into R (ID_ALWAYS, A) overriding system value values (5, 1)` | **OK** |
| `insert into R (ID_DEFAULT, A) values (5, 1)` — `GENERATED BY DEFAULT` | **OK** |

- **Computed columns must be excluded** from both INSERT and UPDATE. (`IsExpression=True` marks them
  — the one thing that flag is good for.)
- **`GENERATED ALWAYS` identity needs `OVERRIDING SYSTEM VALUE`.** This is not hypothetical: the lab's
  `PRODUCTS.PRODUCT_ID` is `GENERATED ALWAYS`, so the naive `Copy as INSERT` from a `PRODUCTS` result
  **fails on the user's own lab database**. We emit `OVERRIDING SYSTEM VALUE` rather than dropping the
  column — preserving the actual key is the point of copying a row (rule #11: never lose information).

### 1.5 Values, types, and literals

CLR types the driver actually returns, and what a *correct* Firebird literal looks like:

| Firebird type | CLR type | Literal | Notes |
|---|---|---|---|
| SMALLINT/INTEGER/BIGINT | `Int16/Int32/Int64` | `123` | |
| NUMERIC/DECIMAL | `Decimal` | `123456789.1234` | exact |
| FLOAT / DOUBLE PRECISION | `Single` / `Double` | `3.14` / `2.718281828459045` | default `ToString(Invariant)` **is** round-trip-exact on .NET Core 3.0+ (verified `reparse-equal=True`) |
| CHAR/VARCHAR | `String` | `'It''s'` | `'` → `''` |
| DATE | `DateTime` | `'2024-03-15'` | |
| TIME | **`TimeSpan`** | `'13:45:59.1234'` | *not* DateTime |
| TIMESTAMP | `DateTime` | `'2024-03-15 13:45:59.1234'` | |
| BOOLEAN | `Boolean` | `true` / `false` | unquoted |
| BLOB SUB_TYPE TEXT | **`String`** | `'text'` | arrives decoded |
| BLOB SUB_TYPE 0 (binary) | **`byte[]`** | `x'DEADBEEF00FF'` | verified round-trip |
| NULL (any) | `DBNull` | `NULL` | |

**Four proven data-loss / invalid-SQL vectors in naive rendering:**

1. **Culture.** The user's machine is **pl-PL**. `123456789.1234m.ToString(CurrentCulture)` →
   `123456789,1234` → `Dynamic SQL Error`. **This is what
   `ExportValueFormatter.Format(value, CultureInfo.CurrentCulture)` does today** — the existing
   clipboard exporter's formatter is unusable for SQL generation as-is. **Literals are always
   InvariantCulture.**
2. **Fractional seconds.** `DateTime.ToString()` → `2024-03-15 13:45:59` — **silently drops `.1234`**.
   Requires an explicit `yyyy-MM-dd HH:mm:ss.ffff`.
3. **The ISO `T` separator.** `ToString("o")` → `2024-03-15T13:45:59.1234000` → Firebird rejects it
   (*Invalid time zone region: T13:45:59.1234*). `ToString(InvariantCulture)` is *also* wrong here —
   it yields US-format `03/15/2024 13:45:59`. Only an explicit format string is correct.
   (This is the same trap `TraceSqlInliner.NormalizeTemporal` already documents.)
4. **BLOBs.** `ExportValueFormatter` renders `byte[]` → the string `(BLOB)`. In an INSERT that becomes
   `'(BLOB)'` — **silent data corruption**. Binary blobs must be `x'…'` hex literals.

**DATE and TIMESTAMP are the same CLR type** (`DateTime`) — they are **indistinguishable by CLR type**.
Rendering a DATE via the TIMESTAMP format yields a misleading `'2024-03-15 00:00:00'`; rendering a
TIMESTAMP via the DATE format **loses the time**. ⇒ The literal writer must be driven by the **declared
Firebird type**, not `Type`. This is why §9 introduces `SqlValueKind`.

Corrected assumption (worth recording — the probe overturned it): **BLOBs *are* comparable in Firebird.**
`where BL = x'AABB'` → 1 row; `where BT = 'tb'` → 1 row. An earlier draft of this design excluded BLOBs
from WHERE clauses on the assumption they were incomparable. They need not be excluded for *correctness*
(§6.4 still excludes them from the all-columns fallback, but for a different, honest reason).

### 1.6 `RDB$DB_KEY` — works, and is still the wrong tool here

- Round-trips: `select RDB$DB_KEY` → 8 bytes; `where RDB$DB_KEY = x'8000000001000000'` **matched**.
- Stable across two separate transactions in the probe.
- Driver quirk: comes back named `DB_KEY`, `GetFieldType` says **`String`** while the value is **`byte[]`**.
- Provenance is reported normally (`BaseTableName = CUSTOMERS`, `BaseColumnName = DB_KEY`).

See §6.3 for why this nevertheless does not belong in generated clipboard SQL.

### 1.7 `GetSchemaTable()` is expensive — it must not touch the hot path

| Operation | Cost |
|---|---|
| execute + read all rows | **1.55 ms** |
| execute + read all rows **+ `GetSchemaTable()`** | **8.66 ms** |
| `SchemaOnly` prepare + `GetSchemaTable()` | **6.61 ms** |

`GetSchemaTable()` adds **~7 ms — 5.6× the cost of the entire query.** Capturing provenance on every F5
to serve an occasional menu action would be a silent, across-the-board regression of the SQL Editor
(and of its Execution Metrics timer). ⇒ **Provenance is captured lazily, on demand** (§9.4).

---

## §2 Current state — what exists, and the drift already present

**The architecture the request asks for already exists.** `EmberTern.Core.Export` is exactly the
proposed diagram:

```
IExportDataSource ──> ExportService ──> IExporter  { Xlsx | Csv | Text | Clipboard }
   (per-grid adapter)                   (one per format, streaming, pure)
```

`ExportFormat`'s own docstring even reserves the slot: *"The SQL-Script family is added in a later etap."*
⇒ **Do not build a new `DataExportService`.** That would be a parallel implementation of a shipped
framework (violating *Reuse before create*, and gotcha #195 on naming). The work is to **extend** it.

**What is genuinely missing:**

| Need | Today |
|---|---|
| Column provenance | `ExportColumn(Name, ClrType)` / `QueryColumn(Name, ClrType)` — **no provenance at all**; `GetSchemaTable` is called **nowhere** in the codebase |
| Declared Firebird type | absent — only CLR `Type` (insufficient, §1.5) |
| SQL-correct value rendering | `ExportValueFormatter` uses **CurrentCulture** and renders blobs as `(BLOB)` — wrong for SQL on both counts (§1.5) |
| Per-**format** gating | `ExportCapabilities` gates *scopes* only; nothing lets a format say *"I can't run on this source, because …"* |
| Selected rows / columns | see below |

**Two drifts to name now:**

1. **There are already two parallel clipboard implementations.** `MainWindowViewModel.BuildCopyText`
   (the grid context menu: Copy Cell / Row / Row+Headers / All+Headers, ~40 lines of hand-rolled TSV)
   and `ClipboardTextExporter` (the Export dialog's Clipboard format). The context menu — exactly where
   *Copy as INSERT* belongs — is the **non-framework** path. Adding new formats there would deepen the
   drift; routing them through the framework leaves the menu half-and-half. This is a real ordering
   decision → §11 / §12 Q4.

2. **"Selected rows/columns" does not exist.** The request's contract is *"grids provide metadata,
   selected rows, selected columns"*. In fact: `ResultGrid` declares no `SelectionMode`; every copy
   action resolves its target from the **right-clicked cell** (`_resultCellCtx`), not from a selection;
   and `QueryResultExportSource` documents *"SelectedRows is intentionally omitted — the SQL results grid
   is single-select"*. There is **no** column-selection concept anywhere. ⇒ M1 targets the right-clicked
   row (§11); multi-select is separable work (§12 Q3).

**Reusable as-is:** `SqlFormatter.Format` (§8) · `DdlGenerator.PresentIdentifier` (identifier
presentation — UPPERCASE+bare for regular names, verbatim+quoted for special/case-sensitive ones, §0-safe)
· `IExportSink` / `ExportService` / `IExporter` / `ExportScope` · `ISqlMetadataProvider.GetColumns`
(already warmed by the Package-5 pipeline) · `ColumnSpec` (`IsPrimaryKey`/`IsComputed`/`IsIdentity`/`NotNull`).

**Prior art, not reusable:** `TraceSqlInliner` inlines values into traced SQL and its
Numeric/Text/Temporal/Boolean/NonInlinable split plus its `T`-separator fix are conceptually right —
but it maps *trace strings keyed by type-name*, not typed `object?` values. It is a sibling of the new
literal writer, not a base for it. Convergence is a **boundary, not debt** (§13).

---

## §3 The safety spine

Architecture rule #11 (the project's #1 rule) governs this milestone directly:

> *If EmberTern is not 100% certain it can reproduce an object identically, it MUST NOT modify it
> automatically (uncertainty ⇒ do nothing or ask).*

Restated for generated DML, and binding on every decision below:

> **EmberTern emits an INSERT or UPDATE only when it can *prove* — from the server's own provenance,
> corroborated by the statement's shape and the catalog — that the statement targets exactly the row the
> user is looking at. Where proof is unavailable, EmberTern generates nothing and says why.**

And, inherited from the Language Completion milestone (Rule 0): **never generate code the user has to
delete.** A generated statement is either correct and runnable, or it is not offered.

Note the asymmetry that makes this urgent: a *malformed* statement fails loudly and harmlessly. A
statement built from `IsKey` on a partial composite PK (§1.2), or from a UNION's first leg (§1.3),
**succeeds** — against the wrong rows. Generated DML is the one place in EmberTern where being wrong is
silent.

---

## §4 Provenance — three independent signals

No single source is sufficient (§1.3). The design requires **unanimous agreement of three**:

| Signal | Source | Answers | Blind to |
|---|---|---|---|
| **A — Provenance** | `GetSchemaTable()`: `BaseTableName`/`BaseColumnName`/`IsExpression` | *"Which base table/column is each output column?"* | UNION legs 2+, self-joins, table-vs-view-vs-procedure |
| **B — Shape** | the executed statement's **AST** (`SqlParser` / `SemanticModel`) | *"Is this a shape where A can be trusted?"* | nothing relevant — but only exists where there **is** a statement |
| **C — Catalog** | `ISqlMetadataProvider` / `ColumnSpec` | *"Is the object a real table? What is its **complete** PK? Which columns are computed / identity / NOT NULL?"* | the query |

**Signal B is not a nicety — it is required for safety.** It is the *only* thing that catches the UNION
case (§1.3), where A reports a clean, key-complete single-table result and would happily generate an
UPDATE against wrong rows. EmberTern already produces exactly what B needs: B2/B3 of Etap 6.9 give
`SelectQuery.From` and `SetOperationQuery` as first-class nodes. This is that foundation paying off.

**B vetoes (any ⇒ no generation):** a set operation (`SetOperationQuery`) · more than one `FromItem`,
or a `JoinedTable` · the same base table appearing twice (self-join) · `GroupByClause`/aggregate ·
**and — critically — any statement the parser could not confidently model** (`RawStatement`, or a lenient
parse). *Uncertainty ⇒ do nothing*, so an unmodelled shape vetoes rather than defaults to permit.

**C vetoes:** the base object is not a `Table` (a procedure, or a non-updatable view — §12 Q2).

**A vetoes:** any projected column has an empty `BaseTableName` **and** is required for the operation ·
two projected columns share a `BaseColumn` (the duplicate-column trap, §1.3).

**Where there is no statement**, B is supplied differently rather than skipped — that is the point of
putting provenance behind the source abstraction (§9.3):

| Grid | Signal A | Signal B |
|---|---|---|
| SQL Editor | `GetSchemaTable()` (lazy) | AST of the executed statement |
| Table Data | *authoritative* — the grid **is** a table | `DirectTable` — trivially satisfied |
| Procedure results | — | `NotATable` — a permanent, honest veto |

Table Data is *strictly safer* than the SQL Editor: it needs no inference at all.

---

## §5 UX Decision 1 — the multi-table INSERT placeholder

> *IBExpert generates `INSERT INTO TABLE_NAME (COL1, COL2) VALUES (…)` … I'm not convinced this is
> actually useful. Is this feature genuinely valuable? Or should we simply disable it?*

### Recommendation: **do not generate the placeholder. Disable — but never silently.**

The instinct is right, and the reason is stronger than "it's untidy":

**1. The table name is the *least* of what's wrong.** The placeholder framing assumes the only unknown
is the target table. It isn't — for a multi-table result, the **column list is wrong too**:

```sql
select o.ORDER_ID, c.NAME as CUSTOMER_NAME, p.NAME as PRODUCT_NAME, oi.QTY
from ORDERS o join CUSTOMERS c … join PRODUCTS p … join ORDER_ITEMS oi …
```

IBExpert's shape yields `INSERT INTO TABLE_NAME (ORDER_ID, CUSTOMER_NAME, PRODUCT_NAME, QTY)`, in which:
`CUSTOMER_NAME`/`PRODUCT_NAME` **are aliases that exist on no table**; the two `NAME` columns collide
into a duplicate if de-aliased (§1.3); and the four columns belong to **four different tables**, so no
single INSERT can ever be right. The user must fix the table, fix the columns, drop most of them, and
add the missing NOT NULL ones. **At that point they have hand-written the INSERT** — the paste saved
nothing and cost a review.

**2. It fails the project's own rules.** It generates code EmberTern knows to be wrong (rule #11) and
that the user must delete before use (Rule 0). Only the *number of literals* is preserved — and those
are the one part already available via Copy Row.

**3. Its worst case is silent.** `TABLE_NAME` is a legal Firebird identifier. Pasted-and-run unedited it
normally fails loudly — but in a schema that happens to contain a table named `TABLE_NAME` (staging and
scratch tables really are named things like this), it **succeeds**, against the wrong table.

**4. The honest reading of the workflow.** There *is* a real one behind it — *"I have a result and want
to load it into a table I know about."* But that workflow needs a **target table + a column mapping**,
which is a mapping dialog (§13 — deliberately not in scope), not a placeholder. A placeholder is a
mapping dialog with every field left blank and no validation.

### Instead: disable **with a specific, structured reason**

Not a grey menu item — a grey menu item **that says why**, naming the actual obstacle:

> *Copy as INSERT — unavailable: the result combines 4 tables (ORDERS, CUSTOMERS, PRODUCTS, ORDER_ITEMS).*
> *Copy as INSERT — unavailable: the result is a UNION; EmberTern cannot tell which table each row came from.*
> *Copy as INSERT — unavailable: SP_CUSTOMER_ORDERS is a procedure, not a table.*
> *Copy as INSERT — unavailable: V_ORDER_DETAILS is a view over 4 tables and is not updatable.*

This teaches the tool's model instead of leaving the user to guess, and it is strictly more information
than a placeholder conveys. Core returns a **structured reason** (an enum + data); App maps it to
`UiStrings` (rule #1: Core has no UI strings; rule #6: no `.resx`).

**Single-table INSERT stays fully supported and is the 90% case** — and, per §1.1, it is *more* correct
than IBExpert's, because it de-aliases through `BaseColumnName`.

---

## §6 UX Decision 2 — the UPDATE WHERE clause

> *This part needs careful design. I do not want UPDATE generation that can accidentally modify
> multiple rows.*

§1.2 proves this is the default outcome of the obvious implementation. The rule:

> **The WHERE clause is built from a key EmberTern has *verified complete* against the catalog, or the
> UPDATE is not offered at all.**

Verified complete means, for every column of the chosen key: (a) it is **present in the projection**;
(b) it maps to a real `BaseColumn` of the one base table; (c) its **actual value in this row is not
NULL**; and (d) the key is a **whole** constraint — never a subset (§1.2).

### 6.1 Primary key — the default ✅

The only default. All PK columns are known from `ColumnSpec.IsPrimaryKey` over the **table's full column
list** (not the result's), which is what makes completeness checkable — the exact check the driver's
`IsKey` fails to make. PK columns are NOT NULL by definition, so (c) is automatic — assert it anyway.

If the PK is **incomplete in the projection** (`select ORDER_ID, QTY from ORDER_ITEMS`), that is a
**veto**, not a fallback to `WHERE ORDER_ID = 1`. This single rule is what §1.2 is about.

### 6.2 Unique key — offered, with two extra conditions ✅ (conditional)

A complete UNIQUE constraint is a legitimate row identifier, and is the answer for the real
"PK not selected" case. Two conditions beyond completeness, both from measurement:

- **every column NOT NULL** (declared) — because a UNIQUE column permits **multiple NULLs** (§1.2), so a
  nullable unique key is not an identifier; and
- **the row's actual values non-NULL** — declared-NOT-NULL is necessary but a row still gets checked.

⚠ **Gap to close in E2:** `ColumnSpec` has **no unique-constraint information**, and `FieldInfo.IsUnique`
is per-column (same trap as `IsKey` — it cannot express *which* constraint or whether it is complete).
Unique-key support therefore requires reading **constraint → ordered column set** from the catalog
(`RDB$RELATION_CONSTRAINTS` + `RDB$INDEX_SEGMENTS`). That is a real, contained piece of work.
**If we want to cut scope, this is the honest cut** (§12 Q1) — PK-only covers the overwhelming majority.

### 6.3 `RDB$DB_KEY` — **no** ❌

It works (§1.6). It is still wrong *for this feature*, and the reason is a lifetime mismatch:

- **It isn't in the result.** Users don't `select RDB$DB_KEY`. We'd have to silently re-run their query
  with an injected column — changing what they asked for to serve a menu item.
- **It is physical, not logical.** It is meaningless in any other database, and does not survive
  backup/restore. Generated SQL goes to the clipboard → a migration script, a ticket, a colleague, a
  different environment. A `WHERE RDB$DB_KEY = x'8000000001000000'` in a migration script is a hazard.
- **It is unreadable.** A human reviewing a diff cannot tell what row it hits — and generated DML gets
  reviewed precisely because it is generated.
- **Recycling.** A DB_KEY can be reused after delete + garbage collection. Stability across two
  transactions (§1.6) is not a guarantee across time.

**The distinction worth keeping:** DB_KEY is right for a tool holding a **live cursor in one transaction**
— which is why it belongs in the conversation about **in-place Table Data editing**, a different feature
with a transaction-scoped lifetime. *Copy as text* has an unbounded lifetime. Same identifier, different
lifetime, different answer. Recorded here so the question isn't re-litigated from scratch later.

### 6.4 All visible columns — **not a WHERE strategy** ❌ (as a default), ⚠ (as an opt-in)

**It cannot satisfy the requirement** — proven: two identical rows,
`WHERE A=1 and B=2 and TXT is null` → **2 rows** (§1.2). Offering it as a *fallback* would mean the
feature silently degrades from "guaranteed one row" to "who knows", precisely when the user has least
information — the exact outcome the request forbids.

It also needs NULL→`IS NULL` rewriting (§1.2), and while BLOBs *are* comparable (§1.5 — corrected), a
WHERE over blob equality is a full-blob comparison per row: it is a performance trap and,
for `SUB_TYPE TEXT` under a different collation, a correctness one.

**If offered at all**, it must be an **explicit, per-invocation opt-in**, labelled with what it is —
*"match on all columns (may update more than one row)"* — never a silent fallback. **Recommendation:
leave it out of M1** and see whether anyone asks. (§12 Q1)

### 6.5 User choice — yes, but as a *refinement over verified options* ✅

Correct instinct, with the ordering inverted from how it's usually built: the default must be **safe and
silent**, and choice is for the cases where more than one *verified* key exists.

- **Default:** PK. No prompt, no dialog — the 90% case must stay one click.
- **Choice:** when >1 verified-complete key exists (PK + a complete NOT NULL unique key), the user may
  pick. The picker offers **only keys EmberTern has already proven safe** — it is a choice *among proofs*,
  never a way to opt into an unproven one.
- **Never:** a free-text WHERE, or a column-checkbox list that permits a non-unique combination. That
  moves the proof obligation onto the user while keeping EmberTern's name on the generated SQL.

### 6.6 The resulting decision table

| Situation | Copy as UPDATE |
|---|---|
| Single table, complete PK projected | ✅ `WHERE` = PK |
| Single table, PK not projected, complete NOT NULL unique key projected, values non-NULL | ✅ `WHERE` = that key *(needs 6.2's catalog work)* |
| Single table, PK partially projected (`ORDER_ID` of `ORDER_ITEMS`) | ❌ *"…needs the complete primary key; LINE_NO is not in the result."* |
| Single table, no key projected | ❌ *"…needs a key column; none of CUSTOMERS' key columns are in the result."* |
| Table has no PK and no unique constraint at all | ❌ *"CUSTOMERS has no primary key, so a single row cannot be identified."* |
| Unique key projected but this row's value is NULL | ❌ *"EMAIL is NULL in this row and cannot identify it."* |
| Join / self-join / UNION / aggregate / procedure / non-updatable view | ❌ (§4 vetoes) |
| Every SET column is computed / nothing left to set | ❌ *"…no updatable columns in the result."* |

Same in every case: **disabled, with the specific reason** (§5).

---

## §7 Value → SQL literal

The genuinely new correctness core, and the one piece worth over-testing. Rules, all from §1.5:

1. **`DBNull`/null → `NULL`** (bare). In a WHERE: `IS NULL`, never `= NULL`.
2. **Always `InvariantCulture`** — never `CurrentCulture` (pl-PL ⇒ invalid SQL, proven).
3. **Never `ToString()` for temporals** — explicit formats: DATE `yyyy-MM-dd`, TIME
   `hh\:mm\:ss\.ffff` (from a **`TimeSpan`**), TIMESTAMP `yyyy-MM-dd HH:mm:ss.ffff`. Space separator,
   never ISO `T`. Fractional seconds always (dropping them is silent data loss).
4. **Driven by `SqlValueKind`, not CLR `Type`** — DATE and TIMESTAMP are both `DateTime` (§1.5).
5. **Strings:** `'` → `''`. Unicode passes through unescaped (verified round-trip: *Zażółć gęślą jaźń
   日本語*). No other escaping — Firebird has no backslash escapes in standard literals; `\` is literal
   (verified: `'It''s a "test" \ n'` round-trips intact).
6. **Binary BLOB → `x'DEADBEEF'`** hex — **never** the `(BLOB)` placeholder (silent corruption).
   Text BLOB → arrives as `String`, quote as text.
7. **Boolean → `true`/`false`** unquoted.
8. **Identifiers via `DdlGenerator.PresentIdentifier`** — reuse, don't re-solve quoting.

**Boundaries to decide (§12 Q5):** a very large binary blob becomes an enormous hex literal (a 10 MB blob
→ a 20 MB `x'…'` on the clipboard). Firebird also has literal-length limits. Proposal: a size threshold
above which the *statement* is not generated and the reason is reported — never a truncated literal, which
would be silent corruption. `ARRAY` columns and any unmapped kind → refuse the statement, same rule.

Every literal rule gets a **live round-trip test against the lab** (generate → execute → read back →
compare), not just a string-equality unit test. A unit test asserting `'2024-03-15T13:45:59'` would have
passed while the engine rejected it.

---

## §8 Formatting — a conflict in the request, and a recommendation

> *Reuse existing EmberTern SQL formatting wherever possible… Don't introduce another formatting style.*

**These two instructions conflict with the example SQL in the same request**, and the conflict should be
resolved before implementation rather than discovered in review. Measured, by running the real formatter:

```
IN : INSERT INTO CUSTOMERS (CUSTOMER_ID, NAME, CITY) VALUES (1, 'John', 'London');
OUT: insert into customers (customer_id, name, city)
     values (1, 'John', 'London');
```

`SqlFormatter` **lowercases keywords *and* unquoted identifiers**, keeps `(cols)` on the header line, and
lays lists out **adaptively** (inline under 120 chars; packed multi-per-line beyond — explicitly *not*
one-per-line, per a previous directive of yours). Quoted identifiers stay verbatim (`"MixedCase"`);
literals are untouched (`'O''Brien'` survives exactly).

The request's example is the **IBExpert** style: UPPERCASE, paren on its own line, one item per line —
i.e. a **different formatting style**, which the same request forbids introducing.

### Recommendation: **generate canonical SQL → `SqlFormatter.Format`. Treat the examples as content, not layout.**

Output would be `insert into customers (customer_id, name, city)` / `values (1, 'John', 'London');`.

Why:
- It is the literal reading of *"don't introduce another formatting style"* and of the **one formatting
  language** directive already established for Typing Ergonomics (where `SqlFormatter.PsqlIndentUnit` was
  *published* rather than guessed — same principle, same reason).
- **§0 makes it free of risk**: the formatter's checked lexeme-preservation invariant means it either
  reproduces every token or returns our input unchanged. It cannot corrupt generated SQL.
- Future formatter work (e.g. a casing option) improves export automatically, with no second style to
  keep in sync.
- Lowercase is **semantically identical** in Firebird — unquoted identifiers fold to uppercase. Nothing
  is lost but the visual.

**If you want UPPERCASE output** — which is a legitimate preference for DML pasted into migration scripts
— then that is a **`SqlFormatter` style option** and its own milestone, applying everywhere. It must not
be an export-local casing switch; that is precisely how a second style is born. (§12 Q6)

**Measure in E3:** formatting is *per statement*, so an N-row copy is N parses. Fine for a clipboard copy
of a few rows; **not** obviously fine for a future 100k-row INSERT file export. The fallback (emit
canonical, unformatted) is trivially available, so this is a known boundary to measure, not to
pre-optimize.

---

## §9 Architecture

### 9.1 Shape — extend the existing framework

```
        Result Grid  (SQL Editor | Table Data | Procedure Results | future)
              │  supplies FACTS only: columns · rows · origin
              ▼
        IExportDataSource                       ← existing contract, + Origin
              │
              ▼
        ExportService                           ← existing orchestrator, unchanged shape
              │  resolves format → IExporter, opens sink, streams
    ┌─────────┼──────────┬───────────┬────────────┬──────────────┐
    ▼         ▼          ▼           ▼            ▼              ▼
  Xlsx       Csv       Text      Clipboard   InsertScript   UpdateScript      (+ future)
                                                  │              │
                                                  └──────┬───────┘
                                                         ▼
                                          SqlStatementBuilder  (shared)
                                                         │
                                    ┌────────────────────┼────────────────────┐
                                    ▼                    ▼                    ▼
                          SqlLiteralWriter    DdlGenerator.PresentIdentifier   SqlFormatter
                            (§7, new)            (reused)                       (reused, §8)
```

The grids supply facts; **everything else is Core** — exactly the requested split.

### 9.2 New Core types (all in `EmberTern.Core.Export.Sql`, pure, zero Avalonia, zero FirebirdSql)

| Type | Responsibility |
|---|---|
| `SqlValueKind` | Core-owned enum: `Integer/Decimal/Float/Text/Date/Time/Timestamp/Boolean/BinaryBlob/TextBlob/Unknown`. **Required** because CLR `Type` cannot distinguish DATE/TIMESTAMP (§1.5). Firebird maps `FbDbType` → this (keeps rule #1: Core has no FirebirdSql dependency). |
| `SqlLiteralWriter` | `(object? value, SqlValueKind kind) → string`. Pure, InvariantCulture, §7's rules. **The one place** a value becomes SQL. |
| `ColumnOrigin` | `BaseTable` · `BaseColumn` · `IsComputed` · `SqlValueKind`. Signal A per column. |
| `ResultOrigin` | Signal A + B as **facts**, supplied by the source: per-column origins + an `OriginShape`. |
| `OriginShape` | `DirectTable(name)` \| `Statement(shape facts from the AST)` \| `NotATable(reason)`. How a source declares B without knowing about ASTs. |
| `ResultOriginResolver` | **The verdict.** facts (A+B) + catalog (C) → `TargetResolution`. Where §4's vetoes and §6's key verification live. Pure ⇒ every §1 trap is a unit test. |
| `TargetResolution` | `Resolved(table, columnMap, keys[])` \| `Unavailable(reason)`. |
| `ExportUnavailableReason` | Structured enum + data (`MultipleSourceTables(names)`, `SetOperation`, `IncompletePrimaryKey(missing)`, `NotATable(kind)`, …). App maps → `UiStrings` (rules #1/#6). |
| `SqlStatementBuilder` | Shared by INSERT/UPDATE/future: column selection, identity/computed exclusion, `OVERRIDING SYSTEM VALUE`, NULL→`IS NULL`. **The place a future MERGE/DELETE plugs in.** |
| `InsertScriptExporter` / `UpdateScriptExporter` | `IExporter` — statement assembly + streaming only. Thin by construction. |

### 9.3 Extended existing types (all additive)

- `ExportColumn` — `+ ColumnOrigin? Origin`, `+ SqlValueKind ValueKind`.
- `QueryColumn` — same two (`record` with `init` props ⇒ no call-site breakage).
- `IExportDataSource` — `+ ResultOrigin Origin { get; }`. **Every source must answer**, so a new grid
  cannot silently omit provenance — the compile error is the point (the lesson of gotcha #219: make the
  seam impossible to miss rather than something to remember).
- `ExportFormat` — `+ InsertScript`, `+ UpdateScript` (the slot its docstring already reserves).
- **New:** `FormatAvailability(bool, ExportUnavailableReason?)` — per-format gating. `ExportCapabilities`
  gates scopes; this gates formats, and it is what §5/§6's *disabled-with-a-reason* renders.

### 9.4 Where provenance is captured — lazily (from §1.7)

`GetSchemaTable()` costs ~7 ms, 5.6× the query (§1.7) ⇒ **never on the F5 path.** On first
*Copy as INSERT/UPDATE*, the SQL Editor re-prepares the statement with `CommandBehavior.SchemaOnly`
(~6.6 ms, one prepare, no rows) and caches the result for the lifetime of the result set. The grid
already holds the rows; only the *shape* is re-derived, so re-preparing is safe even if the data changed.

**Lane + locking** (existing rules, not new ones): prepare on the **Data lane** — the attachment that ran
the query — under its `CommandLock` (gotcha #89: one `FbConnection`, one transaction, serialize commands).
**Not** the Metadata lane: a statement may reference an object created but uncommitted in the Data lane's
transaction, which is invisible to another attachment (gotcha #213), so a Metadata-lane prepare would
fail exactly when the user is iterating on new DDL.

### 9.5 What is *not* built

No `DataExportService` (§2 — `ExportService` **is** it; a second one is a parallel implementation).
No per-grid export logic. No second formatter (§8). No second literal writer — `SqlLiteralWriter` is the
one place, and the existing 5-way duplication of `.Replace("'", "''")` is **not** consolidated by this
milestone (§13).

### 9.6 E6 as-built — Table Data through one mechanism

E6 is an **adapter milestone**: no new format, no new safety rule, no new UI concept. Everything the SQL
Editor grid already had is reused; the only genuinely new code is *how a table's provenance is obtained*.

- **The type-classification decision (option 1).** Table Data reuses the **same** schema-table capture the
  SQL Editor uses, and therefore the same `FirebirdResultOriginReader` → `FbDbType` → `SqlValueKind`
  pipeline. There is **no** second, formatted-string type classifier (parsing `ColumnSpec.Type` like
  `"VARCHAR(50)"` was the rejected option 2 — it would be a parallel mechanism for the one thing that must
  not drift, and `ColumnSpec` carries no `FbDbType` anyway).
- **The seam.** `FirebirdTableDetailReader.CaptureDataSchemaTableAsync(table)` runs a `SchemaOnly` prepare
  of `SELECT * FROM "table"` on the reader's **Data lane** (mirroring `FirebirdQueryExecutor
  .CaptureSchemaTableAsync`). No filter/order/paging — provenance does not vary with the rows shown. This
  is the only new Firebird code E6 required (§11's gate: "if E6 needs more than a `ResultOrigin`, the seam
  is wrong" — it needed exactly one small seam to *produce* that `ResultOrigin`).
- **The shape is `DirectTable`, not `Statement`.** The grid IS a table, so signal B is satisfied by
  construction — the resolver's `VetoByShape` returns immediately, nothing is inferred from a statement.
  This is strictly safer than the SQL Editor path (§4). The live probe (§ Section 10) confirms a
  DirectTable UPDATE touches exactly one row for both single and composite PKs, an INSERT preserves a
  `GENERATED ALWAYS` identity, and a view is still refused as not-a-table.
- **One App mechanism.** The SQL Editor's per-VM copy glue was extracted into a shared
  `SqlCopyController` (App/Export) that both grids own — availability flags, disabled-with-reason
  tooltips, `RefreshAvailabilityAsync`, `BuildFormattedAsync` (through the shared `SqlFormatter`), `Reset`.
  It wraps a `SqlCopyCoordinator`, which was generalized to one lazy-capture-and-cache path fed by a
  `Func<CancellationToken, Task<ResultOrigin>>`: the SQL Editor supplies a statement capturer, Table Data a
  DirectTable capturer. Two grids, one controller, one coordinator, one resolver, one builder.
- **Provenance stays lazy and cached (§1.7 / §9.4).** The ~7 ms schema read lands on the first
  Copy-menu-open, never on data load, and is cached for the table tab's life (it never changes with page /
  filter / sort, so the Table Data coordinator never resets).
- **Procedure Results stay `NotATable`** — the correct permanent behaviour, not extended. Their
  `RowBufferExportSource` already defaults to `ResultOrigin.None(NotATable)`; E6 leaves them untouched and
  adds no menu there.
- **View Data** is out of E6's scope (its grid gets no copy menu this milestone). A view reached through a
  DirectTable origin is refused by signal C anyway (`obj.Kind != Table`), so the safety story holds if it
  is ever wired.

**E6 QA fix (2026-07-17) — values delivered as strings refused ("no exact SQL literal").** On a real ERP
DB, copy was refused with *"…value has no exact SQL literal, and EmberTern will not write an
approximation"*. The debugger showed the cause: the row values arrive as **strings** (e.g. an INTEGER PK
as `"10019"`), while `SqlLiteralWriter` is type-driven and rejected anything but the exact CLR container
for each kind. Why an environment hands every cell back as a string is still open (the executor reads via
the driver's typed `GetValue`; probe sections 11/13 confirm normal/domain columns come back typed on the
lab) — but the writer must render a value it *can* render exactly. Fixes, all **strict + §0-safe** (parse
the value's own kind, refuse anything ambiguous — never coerce a pl-PL `1,5` or `15.03.2024` into a
different number/date):
- **wide integers** — `WriteInteger`/`WriteDecimal` accept `Int128`/`UInt128`/`BigInteger` (a
  `NUMERIC(>18,0)` or a value beyond `Int64` is still an integer whose literal is its digits);
- **string-delivered values** — each writer accepts its value as a string parsed under
  **InvariantCulture** (integers: `NumberStyles.Integer`, no thousands; decimals: decimal point only, no
  thousands; floats: `NumberStyles.Float`; **temporals: ISO-only `TryParseExact`**, since ISO is the one
  unambiguous form; booleans: `bool.TryParse`) and re-dispatches through the typed path so every
  representable-ness check still applies.
Pinned by `SqlLiteralWriterTests`. *(The refusal reason is surfaced in the Messages panel by the E6 QA
copy fix below; before it, the guard could return silently.)* **⚠ If a temporal still refuses, its string
is not ISO — capture the exact string and add its format to `SqlLiteralWriter.IsoDateTimeFormats`.**

**E6 QA fix (2026-07-17) — SQL Editor copy did not reach the clipboard.** Manual QA found Copy-as-INSERT/
UPDATE working from Table Data but not from the SQL Editor grid (availability correct, but clicking copied
nothing). A headless repro (`SqlEditorCopyAsSqlTests`, injecting a resolving controller) proved the
VM → `SqlCopyController` → Core → `ClipboardWriteRequested` path is correct, isolating the fault to the SQL
Editor **view**: it uniquely round-tripped the clicked row through `ResolveResultRowIndex(_resultCellRow)`
→ an index → `CurrentResult.Rows[index]`, and when that reference lookup missed (row not captured) it hit a
**silent** `if (rowIndex < 0) return false`. The fix aligns the SQL Editor with the proven Table Data
pattern: `CopyRowAsSqlAsync` now takes the row **object** (no index round-trip), the view sources it from
either capture path (`_resultCellRow ?? _resultGrid.SelectedItem`), and a missing row is **reported**
(`GridCopyNoRow`) rather than silently dropped — no copy path fails without saying why. User-confirmed
fixed (2026-07-17).

---

## §10 Future formats — what this design buys

`MERGE` · `UPSERT` (`UPDATE OR INSERT`) · `DELETE` · JSON · XML · YAML — **none implemented**, all
unblocked:

- **DELETE** — needs only §6's verified key; a `WHERE` builder already proven safe. Nearly free.
- **UPDATE OR INSERT / MERGE** — need the key **and** the SET list; both are `SqlStatementBuilder`'s
  existing outputs. `MERGE … WHEN`'s *formatting* is a known formatter boundary (CLAUDE.md §12), which is
  a layout question, not a generation one.
- **JSON/XML/YAML** — need `SqlValueKind` (typing) but **not** provenance; they are `IExporter`s that
  ignore `Origin`. That they need less is the sign the seam is in the right place.

The extension points are exactly three: **a new `IExporter`** (serialization), **a new `ExportFormat`**
(+ `FormatAvailability`), and — only if a format needs a new fact — **a new `ColumnOrigin` field**. No
format ever touches a grid.

---

## §11 Milestones

One per session, foundation-first, each shipping complete + tested + no-TODO
(`feedback_staged_implementation_contract`). **E1–E4 are invisible to the user** — deliberately: the
correctness core lands and is tested before any UI can invoke it.

| # | Milestone | Deliverable | Gate |
|---|---|---|---|
| **E0** | **Design review** *(this doc)* | §5/§6/§8 decisions + §12 answered | **user sign-off — nothing starts before this** |
| **E1** | `SqlValueKind` + `SqlLiteralWriter` | The §7 correctness core. Pure Core. | unit tests **+ live round-trip vs the lab** for every kind incl. NULL/unicode/quotes/blob/date/time/timestamp/bool/numeric; pl-PL culture pinned |
| **E2** | Provenance: model + capture + resolver | `ColumnOrigin`/`ResultOrigin`/`ResultOriginResolver`; `GetSchemaTable` mapping (Firebird); AST shape veto; **unique-constraint catalog read** (§6.2) | **every §1.2/§1.3 trap is a test**: partial PK, partial unique, UNION-of-different-tables, self-join, duplicate column, procedure, view, aggregate. Hot path unchanged (§1.7) |
| **E3** | INSERT format | `SqlStatementBuilder` + `InsertScriptExporter` + `ExportFormat.InsertScript` + `FormatAvailability` | round-trip vs the lab incl. `GENERATED ALWAYS` (`PRODUCTS`) + computed (§1.4); measure §8's per-statement format cost |
| **E4** | UPDATE format | `UpdateScriptExporter` + key selection (§6.5) | §6.6's decision table is a test table; **no generated UPDATE may match >1 row** — asserted by *executing* it against the lab and counting |
| **E5** | **App UI — SQL Editor grid only** | Context menu + disabled-with-reason + `UiStrings` | first user-visible milestone; both themes; **visual confirmation required** (QA rule) |
| **E6 ✅** | Reuse: Table Data · Procedure Results | Adapters only — zero new format/UI logic (§9.6) | met: E6 needed exactly one seam — `FirebirdTableDetailReader.CaptureDataSchemaTableAsync` — to *produce* a `DirectTable` `ResultOrigin`; everything else reused. Live-probe Section 10 green; **user-confirmed** |

**Status (2026-07-17):** E1–E6 shipped, engine-verified, and **user-confirmed** across the SQL Editor and
Table Data grids — including the follow-up fixes (clipboard row-passing; wide-integer + string-delivered
value rendering). Build 0/0, full suite green (4587). Procedure Results confirmed `NotATable` (unchanged);
View Data deliberately not wired this milestone. **Milestone closed.**

**Deliberately deferred / separate** (§12 Q3/Q4): multi-row + column **selection**; converging the legacy
`BuildCopyText` onto the framework; a target-table **mapping dialog**; any of §10.

**Ordering note.** E1 before E2 because the literal writer is the only part with *no* dependencies and the
highest correctness density. E2 before E3/E4 because both formats are thin clients of the verdict. E5 last
because — per the §15.4 lesson (a recommendation reversed once already) — UI is where the QA cost is, and
it should land on a foundation already proven by test.

---

## §12 Open questions — decisions needed before E1

1. **Unique-key WHERE (§6.2) — in or out of scope?** It needs a new catalog read (constraint → ordered
   column set); `ColumnSpec` has no unique info and `FieldInfo.IsUnique` is per-column and unsafe.
   **Recommendation: PK-only for E1–E5**, unique keys as a follow-up once the shape has proven itself.
   And: is the "all visible columns" opt-in (§6.4) wanted at all, or left out until someone asks?
2. **Views (§4/C).** `V_ACTIVE_CUSTOMERS` is a genuinely updatable single-table view; `V_ORDER_DETAILS`
   is a 4-table join view and is not. Options: (a) refuse all views — simple, honest, loses a real case;
   (b) allow naturally-updatable views (single base table, no aggregate/DISTINCT/UNION); (c) allow any
   view with an `INSTEAD OF` trigger — correct but needs trigger analysis. **Recommendation: (a) for E3,
   revisit with (b)** — a view's updatability is a genuine research question and shouldn't gate this
   milestone.
3. **Selection (§2).** The request's contract ("selected rows, selected columns") describes a capability
   that doesn't exist: no `SelectionMode`, copy targets the right-clicked cell, no column selection
   anywhere. **Recommendation: M1 = the right-clicked row + "all rows in view"**; multi-select is its own
   milestone. Confirm — this may simply be a misremembering of what the grid does today.
4. **The two clipboard paths (§2).** E5 adds INSERT/UPDATE through the framework while Copy Cell/Row stay
   on `BuildCopyText` — a half-and-half menu. Converge first (a pure refactor, zero user value, full
   manual re-verify) or after (conscious, scheduled debt)? **Recommendation: after**, per §15.4 —
   consolidate where it pays, and it pays when selection lands (Q3), since selection would otherwise be
   built twice.
5. **Blob size ceiling (§7).** Above what size do we refuse rather than emit a giant hex literal? Refusing
   is the only §0-safe option (a truncated literal is silent corruption). Proposed: a configurable
   threshold, default ~1 MB.
6. **Casing (§8).** Confirm generated SQL goes through `SqlFormatter` and is therefore **lowercase** —
   or that you want an UPPERCASE option, which is a **formatter** milestone applying everywhere, not an
   export switch.

---

## §13 Boundaries — deliberately not in scope

- **A target-table mapping dialog** (the honest version of §5's placeholder) — a real feature if the
  workflow proves real; not this milestone.
- **In-place grid editing** — where `RDB$DB_KEY` would legitimately be reconsidered (§6.3). Different
  lifetime, different feature.
- **Converging `TraceSqlInliner`** onto `SqlLiteralWriter` (§2) — different input shape (trace strings
  vs typed values). A boundary, not debt. Revisit only if a third value→SQL renderer ever appears; two
  with different inputs is not yet duplication.
- **Consolidating the 5 copies of `.Replace("'", "''")`** (`DdlGenerator` ×2, `SecurityDdlGenerator`,
  `TraceSqlInliner`, `FirebirdDdlReader`) — real duplication, but DDL-generation's concern, and folding
  it in would make this milestone touch every generator.
- **`EXECUTE STATEMENT` / runtime SQL strings** — never a provenance source. Permanent boundary
  (as in Etap 6.9).
- **Script Executor** — no result grid; no export surface. Consistent with S4's deferral.

---

## §14 Gotcha candidates (for `docs/gotchas.md` on completion)

1. **`FbDataReader.GetSchemaTable()`'s `IsKey`/`IsUnique` are per-column *participation* flags, not
   row-identity guarantees — both report `True` on an incomplete composite key.** `select ORDER_ID, QTY
   from ORDER_ITEMS` (PK `ORDER_ID,LINE_NO`) reports `ORDER_ID` IsKey=True; a WHERE built from it hits
   every line of the order. Verify key **completeness** against the catalog; never trust the flag.
   *(measured; §1.2)*
2. **A `UNION` reports only its first leg's provenance.** `select … from CUSTOMERS union all select …
   from PRODUCTS` reports a clean, key-complete `CUSTOMERS` result. Schema metadata **cannot** detect
   this — only the AST can. Any feature inferring a target table from `GetSchemaTable` must veto on
   set operations. *(measured; §1.3)*
3. **`GetSchemaTable()` costs ~7 ms — 5.6× a small query.** Never call it on an execution path; capture
   lazily via a `SchemaOnly` prepare. *(measured; §1.7)*
4. **`ExportValueFormatter` is not usable for SQL generation**: CurrentCulture (pl-PL → comma decimals →
   invalid SQL) and `byte[]` → `(BLOB)` (silent corruption). SQL literals are InvariantCulture with
   explicit temporal formats and `x'…'` blobs. *(measured; §1.5)*
5. **DATE and TIMESTAMP are the same CLR type**; `DateTime.ToString()` silently drops fractional seconds
   and `"o"` emits an ISO `T` that Firebird rejects. Render temporals from the **declared** type with an
   explicit format. *(measured; §1.5)*
6. **`GENERATED ALWAYS AS IDENTITY` rejects a plain INSERT naming the column** — `OVERRIDING SYSTEM
   VALUE` is required (the lab's `PRODUCTS.PRODUCT_ID` is one). *(measured; §1.4)*
