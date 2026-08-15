# EmberTern — Architecture (as built)

**What this document is.** How EmberTern is put together *today*, and why the boundaries are where they are.
It is written against the code, not against the plan: every type named here exists in the repository.

**What it is not.** Not a class index, not a roadmap, not a project history. For *"what is done and what is
next"* read [`docs/current-state.md`](docs/current-state.md); for *"why did this go the way it did"* read
`docs/history/`; for the operating rules read [`CLAUDE.md`](CLAUDE.md).

> ⚠ Where this document mentions a rule (Fidelity §F, rule #11, the localization rule), it states the rule
> because it **shapes the code**. The authoritative wording lives in `CLAUDE.md`.

---

## 1. The product in one paragraph

EmberTern is a desktop developer workbench for Firebird: SQL execution, metadata browsing, per-object editors,
a PSQL debugger, data import/export, trace and session monitoring. It is a **.NET 9 / Avalonia 12.1.1** desktop
application built around one idea — the tool must never be the reason a developer loses work. That single
constraint is why the transaction model, the DDL-change gate, the settings store and the charset guard all look
the way they do.

---

## 2. Solution shape

Five projects, and the dependency arrows only point one way:

```
                    ┌──────────────────┐
                    │  EmberTern.App   │  WinExe · Avalonia · all UI
                    └────────┬─────────┘
             ┌───────────────┼────────────────┐
             ▼               ▼                ▼
   ┌──────────────────┐  ┌────────┐  ┌──────────────────┐
   │ EmberTern.Core   │◄─┤ .Office│  │ EmberTern.Firebird│──► EmberTern.Core
   │ pure domain      │  └────────┘  │ the only Fb* code │
   └──────────────────┘              └──────────────────┘

   tests/EmberTern.Tests ──► Core + Firebird + App
```

| Project | Owns | Must not contain |
|---|---|---|
| **`EmberTern.Core`** (304 files) | Domain model and all pure logic: the SQL/PSQL language front-end, import pipeline, export framework, metadata DTOs, settings model + store, performance rules, workspace state, connection profiles. | ⛔ Any Avalonia type. ⛔ Any `FirebirdSql.*` type. |
| **`EmberTern.Firebird`** (42 files) | Every `Fb*` type in the product: connections, transactions, readers, executors, the debugger's server-side executor, import writer. Readers return **Core DTOs**, never driver types. | ⛔ Any Avalonia type. |
| **`EmberTern.Office`** (9 files) | The **only** place an Office-format NuGet dependency is allowed, in both directions: `DocumentFormat.OpenXml` (XLSX write + streaming SAX read) and `ExcelDataReader` (legacy `.xls`). | ⛔ Anything not about Office file formats. |
| **`EmberTern.App`** (274 files) | Views, ViewModels, theming, commands, localization catalog, editor wiring. | ⛔ Business rules that belong in Core. |
| **`tests/EmberTern.Tests`** | One test project for the whole solution. | — |

`Directory.Build.props` sets `net9.0`, `Nullable=enable` and **`TreatWarningsAsErrors=true`** for every
project, and is the single source of product identity (version, release date, company). ⛔ A version number is
never written in code; `AppInfo` reads it back off the assembly, and two tests enforce that.

⚠ `TreatWarningsAsErrors` also escalates NuGet's `NU1902`/`NU1903`, so **a direct `PackageReference` with a
known advisory fails the build** — a de-facto SCA gate. This is why some packages are *promoted* to direct
references purely to bring them under it (see `tests/EmberTern.Tests.csproj`).

### Why Core has no driver reference

Core defines what a table, column, routine or import row *is*; `EmberTern.Firebird` knows how to obtain one
from a server. The practical payoff is that the entire language front-end, the import pipeline and the settings
store are testable with no database at all — which is why a suite of ~8 850 tests runs in under 30 seconds
without a server.

### Tooling outside the solution

`tools/probes/*` are developer verification programs (live Firebird, a password, sometimes a visual check).
They reference the **real** Core/Firebird assemblies, so a passing probe is a statement about shipped code.
⛔ They are deliberately **not** in `EmberTern.slnx`: the hermetic suite must never need a server.

---

## 3. Connections and transactions — the central domain boundary

This is the boundary most of the product is organised around. EmberTern opens **three physical attachments**
to the same database (`ConnectionRole` in `FirebirdConnectionService`):

| Lane | Carries | Transaction |
|---|---|---|
| **`Data`** | Everything the user runs by hand: SQL editor F5, table-data edits, Execute Procedure. | **THE one user working transaction.** Auto-*begin*, **never** auto-commit, NOWAIT. |
| **`Metadata`** | Read-only catalog browsing. | **None.** Reads use an implicit per-command transaction (`MetadataLane.TransactionForCommand`), so browsing never entangles with the user's transaction. |
| **`Ddl`** | Object-editor Compile only. | Its own autonomous, auto-committed transaction, **WAIT** with a bounded lock timeout. |

Three attachments are *required*, not stylistic: the managed driver allows **one transaction per
`FbConnection`**, so concurrent commands on one connection must be serialized. Each lane therefore owns a
`SemaphoreSlim` command lock, and every command path takes it.

⚠ **The lock accessor is captured into a local once per acquire/release pair.** Re-invoking a lane-resolving
accessor at release time leaks one semaphore and over-releases another if the lane flipped mid-call — a leak
that survives reconnect and is only cleared by restarting the app.

**Hard rules that fall out of this:**

- ⛔ **No autocommit, ever.** Auto-*begin* exists; auto-*commit* has no toggle and no setting.
- ⛔ **The SQL editor is a classic Firebird console**: no routing by statement kind, no hidden second
  transaction. A statement classifier exists and may decide whether to *refresh the UI*; it must never decide
  *where or how* a statement executes.
- **DDL uses WAIT with a bounded timeout wherever it runs.** The cross-attachment `object … is in use` is a
  transient metadata-cache lock that only bites NOWAIT.

### The FB3+ gate

`FirebirdConnectionService` refuses a pre-Firebird-3 server the moment the first attachment opens, before the
Metadata/Ddl lanes are created. The version is parsed by **one** parser (`FirebirdDdlReader.ParseServerMajor`),
and the gate **fails open** on an unparseable version — an already-open connection is far more likely to be a
modern server with an odd banner than a Firebird 2.5.

---

## 4. Data flow — F5, end to end

```
SqlEditor (AvaloniaEdit)
  → MainWindowViewModel: ExecutionRequest { Sql, Parameters, Intent, PreviewLimit, FullSafetyCeiling }
    → FirebirdQueryExecutor (Data lane)
        · TransactionService auto-BEGINs if idle          (never commits)
        · acquires the lane command lock
        · connection.CreateGuardedCommand(sql)            ⭐ charset guard — §10
        · cmd.AddGuardedParameter(...)                    ⭐ charset guard — §10
        · registers fb_cancel_operation on the token      (so Cancel reaches the server)
        → FbDataReader → object?[] rows
  ← QueryResult { Columns, Rows, Elapsed, Truncated, CeilingHit }
→ grid population + ExecutionSummary + messages
```

Two things in that path are easy to get wrong and are therefore fixed by design:

- **Cancellation must reach the server.** A `CancellationToken` alone cannot interrupt a round-trip blocked
  while Firebird computes, because no `await` observes the token until the first row. `FbCommand.Cancel()`
  issues `fb_cancel_operation`; registering it on the token is what makes Cancel work on the first click.
- **Preview vs Full** are one code path with two caps: `PreviewLimit` (→ `Truncated`) and a hard
  `FullSafetyCeiling` (→ `CeilingHit`). The streaming export path deliberately is **not** bounded by the
  ceiling — that ceiling is a grid-memory backstop, and an export streams to disk.

---

## 5. Communication between layers

- **Core/Firebird → App: events on services.** `ActiveConnectionChanged`, `TransactionStateChanged` and friends
  are plain events; ViewModels subscribe directly. ⛔ **No event bus / `IMessenger`** — the rule is that one is
  introduced when 3+ components genuinely need to talk, and that has not happened.
- **Core/Firebird → App: words.** A layer below the App never produces a sentence. It hands up a
  `LocalizableMessage` (a `MessageKey` + arguments) and the App resolves it. See §12.
- **Async only where the user waits** — query execution and connection. Not async everywhere.
- ⛔ **No interfaces without two concrete implementations.** The interfaces that exist earn it: `IExporter`,
  `IExportSink`, `IImportProvider`, `IImportWriter`, `IPerformanceRule`, `ISqlTemplate`, `IDebugExecutor`,
  `ISqlMetadataProvider`. Services (`FirebirdConnectionService`, `TransactionService`,
  `ApplicationSettingsStore`) are direct classes — there is no `IDbProvider` layer.

---

## 6. Shell and UI structure

`MainWindow` hosts a tabbed workspace; each tab is a ViewModel + View pair (SQL editor, ten per-object editors,
Table Data, Security Manager, Activity Monitor, Session Manager, Performance Analysis, Data Import, Script
Executor, Debugger). The three source-backed editors (Procedure, Function, Trigger) share an abstract
`SourceObjectDetailTabViewModel`; the rest implement the same editor contract directly
(`ISavableObjectEditor`, `IUnsavedWorkSource`). ⭐ **Every object editor ships a Revert/Discard beside its
Compile/Save and must confirm before discarding** — an accidental click must never lose uncompiled work. The sidebar is a **flat-list `ListBox`**, not a `TreeView` — a `TreeView` with nested
virtualizing panels cannot do stable random-access scrolling over a large expanded subtree, and filtering
rebuilds the bound collection rather than toggling `IsVisible`.

**Five theme files, each with one job**, and nothing else holds a colour or a metric:

| File | Owns |
|---|---|
| `Themes/Colors.axaml` | The **only** source of colour. `ThemeDictionaries` with `Dark` + `Light`, same key set in both. |
| `Themes/Tokens.axaml` | Non-colour metrics: spacing, heights, radii, icon sizes. No theme dictionaries — a metric does not depend on the theme. |
| `Themes/Typography.axaml` | 12 typography roles + `Font.Ui` / `Font.Code`. |
| `Themes/FluentBridge.axaml` | Repins FluentTheme's own named resources onto our tokens. ⛔ Mapping only, never a second catalog. |
| `Themes/ControlStyles.axaml` | Shared styles and every control **metric** setter. |

⛔ No hardcoded colours anywhere; consumption is `{DynamicResource}` so a theme toggle recolours live.
⭐ A control's size comes from its **context** (a chrome strip, a grid cell, a dialog footer), never from its
variant — a variant carries colour.

**One message surface.** `Controls/MessageBanner` is the IDE's single Info/Success/Warning/Error surface, used
by 21 views. ⛔ A new message on a work surface uses it — never a locally-styled coloured `TextBlock`, and never
a local `Background`/`BorderBrush` on the banner (a local value outranks the shared style and re-opens per-host
divergence). A host sets only `Severity`, `Message`, `IsVisible` and layout margins.

**One command registry.** `App/Commands` is a single declarative table: `CommandCatalog` (with a collision
validator), `CommandRouter` resolving **Editor > Tree > Grid > Tab > Global**, and `CommandTip` as the one
place a gesture becomes text. ⛔ No gesture is typed by hand anywhere — shortcuts, tooltips, chips and all
context menus read the registry, and tests enforce it.

### ViewModels

`CommunityToolkit.Mvvm` source generators; ViewModels hold no Avalonia types (no `IImage`, `Color`,
`Thickness`). Per-kind icons flow as a **key string** (`IconResourceKey`) through a converter, so the VM never
holds a brush.

⚠ A command gated on a computed or collection-derived value needs an explicit change notification on **every**
mutation path — the symptom of getting this wrong is "the feature works but the button stays disabled".

---

## 7. The SQL/PSQL language front-end

Everything the editor does is a **client of one shared model** in `EmberTern.Core.Sql.Language` — pure, no
Avalonia. This replaced seven independent ad-hoc SQL scanners and three divergent keyword lists.

```
        source text
            │
            ▼
   SqlLexer ──► tokens (with trivia)
            │
            ▼
   SqlParser ──► SqlScript (AST)          error-tolerant; retains the token stream
            │
            ▼
   SemanticModel ◄── ISqlMetadataProvider (catalog facts)
            │
            ├─► DiagnosticsEngine ─► squiggles + panel
            ├─► CompletionEngine + CompletionMatcher
            ├─► SignatureHelpEngine
            ├─► QuickInfoEngine / Hover
            ├─► NavigationEngine
            ├─► SemanticHighlight classifier
            ├─► CodeActions (Quick Fixes) ─► TextEditApplier
            ├─► SqlFormatter
            └─► Debugging (HarnessBuilder, StepPlanner, …)  — §9
```

**Three properties make this safe. New work must not break them.**

1. ⭐ **Byte-for-byte round-trip, independent of parsing depth.** The AST is a *structural overlay* on a
   retained token stream; nodes carry only spans. So `ToSourceString() == input` holds for a fully modelled
   statement, a shallow one and a `RawStatement` alike. An under-modelled construct can never lose text — the
   guarantee comes from the token stream, not from the parser being complete.
   The formatter adds a **checked lexeme-preservation invariant**: if a formatted statement's lexeme sequence
   differs from its input, it keeps the statement verbatim.
   ⚠ The failure mode this creates is worth knowing: a formatter that *drops* a lexeme does not look like a
   formatting bug — the net reverts the statement and the feature merely appears to do nothing.
2. ⭐ **One owner per question.** `CompletionEngine` answers *"what is legal at this caret"*;
   `CompletionMatcher` answers *"which of those match what is typed"*. IntelliSense owns **names**, Language
   Completion owns **constructs**, Typing Ergonomics owns **mechanics** — split by vocabulary *and* grammatical
   position, both derived from each owner's own catalog rather than hard-coded.
3. ⭐ **The parser is the single structural source.** Binder and formatter both *consume* the AST; neither
   re-walks tokens for structure. Ordinary expressions stay token fragments by design — that is the structural
   depth boundary, not debt.

**`ISqlMetadataProvider`** is the seam between the pure model and the live catalog. Its surface is deliberately
tiny — `GetColumns`, `GetRoutineParameters`, `AllObjects` — plus **readiness answers** (`KnowsColumns`,
`KnowsRoutineParameters`). The readiness half matters: without it, a lazily-warmed cache reports "no columns"
and the diagnostics engine underlines the whole statement for a moment. ⭐ Any new lazily-warmed fact needs its
own `Knows…` answer.

⚠ A related lesson encoded in the binder: **a false positive and a missing feature can be the same bug**, so it
is fixed at the *resolution* step, never at the reporting step — and such a decision is keyed on the **resolved
catalog target**, never on the AST node shape.

**Attachment.** `SqlEditorBehavior.Attach` is the ONE seam for the editor-intrinsic block (completion,
highlighting, navigation, squiggles, related elements, language completion, typing ergonomics, search). Both
the main SQL editor and the object editors go through it, so a new capability lands in one place. Read-only
surfaces use `AttachReadOnlyHighlighting` / `AttachDdlPreview` instead — ⛔ a read-only surface must never be
given mutating quick fixes.

**Statement splitting** has one owner, `SqlStatementSplitter`, riding the parser's statement boundaries. Its
output *is* the DDL sent to the server, so it is pinned byte-for-byte by a differential test.

---

## 8. Metadata access, DDL generation and change safety

- **Reading**: `FirebirdMetadataReader`, `FirebirdDdlReader`, `FirebirdCatalogReader`, `FirebirdTableDetailReader`
  on the Metadata lane. Readers never open their own transaction; they attach to the caller's active
  transaction when there is one, else the driver's implicit per-command read transaction.
- ⚠ Catalog columns vary by server version (`RDB$IDENTITY_TYPE` is FB3+). Version-gate the query rather than
  issuing a doomed SELECT and catching the exception.
- **Source BLOBs are decoded by us, not by the driver.** `FirebirdDdlReader.DecodeSourceBlob` reads raw bytes
  and tries strict UTF-8 first, falling back to the connection charset — because one database can hold both
  (modern tools write UTF-8, older IBExpert wrote connection-charset bytes).
- **DDL change safety.** A compile cannot overwrite a definition EmberTern cannot prove is the one the editor
  loaded, and a New-object compile cannot overwrite an existing object of the same name. One Core verdict
  (`ObjectChangeSafety`) behind one App gate (`ObjectChangeGate`). ⛔ **No force-overwrite** — the SQL editor is
  the deliberate escape hatch.
- **`.sql` export is UTF-8 without BOM** — `Encoding.UTF8` emits one and breaks the first statement's parse in
  `isql`/IBExpert.

---

## 9. Debugger

Firebird has **no debug API**. EmberTern's debugger is a **client-side PSQL interpreter** whose semantics come
from the server:

```
editor / metadata source
   → SqlParser + SemanticModel                 (control flow comes from the AST)
   → DebugSession            (Core, pure)      stepping, frames, breakpoints, watches
        ├─ StepPlanner                          what "step into/over/out" means next
        ├─ BreakpointSet / DataBreakpointSet    stop policy: condition, hit count, data
        ├─ HarnessBuilder                       builds an anonymous EXECUTE BLOCK per statement
        ├─ ExceptionRouter                      PSQL exception semantics → frame unwinding
        └─ IDebugExecutor  ◄── the only door to the server
                 │
                 ▼
   FirebirdDebugExecutor (Firebird)            runs the harness on DebugSessionConnection
        + FirebirdDebugMetadata                 catalog facts for frames/params/triggers
        + DebugErrorMapper                      server error → classified debug error
```

⭐ **The Fidelity Law (§F) is the architecture, not a slogan.** Every statement executes as an **anonymous
`EXECUTE BLOCK` that never names the routine**, so all semantics — types, coercions, exceptions, generator
side-effects — come from the real engine rather than from a simulator. The interpreter owns only control flow
and variable state. Consequences that show up in the code:

- **One decision point** *"before executing a statement"* (`TryStopBeforeExecuting`) — one breakpoint model
  shared between the session and the VM, so the UI and the engine can never disagree about a stop.
- **Frame savepoints** (`EnterFrameSavepoint` / `LeaveFrameSavepoint` / `RollbackFrameSavepoint`) give a frame
  the rollback semantics PSQL blocks have.
- The session owns **its own attachment and transaction** (`DebugSessionConnection`) — a session is *not* a
  lane, and it must not outlive the profile's connection.
- **Draft model**: Save is the only DB write. Restart begins a session from the *edited* text without saving,
  which is possible precisely because the harness never names the routine.
- ⛔ **No safe mode.** Suppressing a generator or an autonomous transaction would mean refusing to execute
  correct SQL. Instead `DebugPreflight` *warns*: one scan feeds both the launch panel and the running view's
  bar, so they cannot disagree.
- ⭐ **The charset guard closes a fidelity hole here** (§10): the harness carries the user's own PSQL, so a
  character the connection cannot represent would make the debugger execute *different code than the one on
  screen*. It is refused before the driver instead.

---

## 10. Charset guard

**The problem, measured on Firebird 5 + FirebirdClient 10.3.4:** a character the **connection** charset cannot
represent is destroyed **client-side, inside the driver's encoder, before the server sees it**. The server then
receives a byte sequence that is perfectly valid in the declared charset and stores it faithfully — so there is
no exception and no server error, and the server *cannot* help.

⚠⚠ **The symptom is not `?`.** The single-byte code pages carry .NET's `InternalEncoderBestFitFallback`: over
U+0020–U+2FFF in WIN1250, 11 702 characters become `?` but **330 become a different, ordinary-looking
character** (`£`→`L`, `¼`→`1`, `À`→`A`). A procedure body sent as `R = 'Cena £100 ¼ À'` was stored as
`R = 'Cena L100 1 A'`: valid PSQL, compiles, wrong number. WIN1250 is the **default** connection charset.

⭐ **Reads are already safe** — the server refuses to transliterate outward, loudly. This is a **write-side**
concern only.

**The shape of the fix — one seam, not three patches:**

| Piece | Question it answers |
|---|---|
| `CharsetCatalog.Resolve` | *"What do I **decode** these bytes with?"* — bytes we already hold, whose origin we are guessing (a source BLOB, an imported file). ⛔ Unchanged, and ⛔ never used for the guard. |
| `CharsetCatalog.ResolveWireEncoding` | *"What will the driver **encode** my text with?"* — mirrors the driver through supported APIs only. |
| `CharsetRepresentation` | The oracle: `Strict` / `CanRepresent` / `FindFirstUnrepresentable` (character **and** position). |
| `FirebirdCommandGuard` | **The seam.** Every command creation and every parameter bind in the product goes through it. |

⚠ **`NONE` is why the two resolution questions had to be split.** `Resolve` sends every unknown name — including
`NONE`, which the connection dialog offers — down its UTF-8 branch, and UTF-8 short-circuits every
representability check. The driver's `NONE` is the **ANSI code page of the process culture** (cp1250 under
`pl-PL`, cp1251 under `ru-RU`): single-byte, lossy and machine-dependent.

**Guarantees:**

- The check runs **before** the driver encodes anything, and is applied **uniformly** — constant ASCII catalog
  SQL costs ~1.8 µs, so nobody has to judge which call sites are "risky".
- **DDL validates the whole batch before a transaction opens**, so refused source never reaches the server at
  all — *"we never did it"*, not *"we rolled it back"*.
- ⛔ **Refusal, never repair.** Substituting, escaping or silently switching charset would be changing the
  user's data without asking, which is the defect rather than the fix.
- Covered paths: **statement text · bound parameters · DDL/source · import · debugger harness**.
- Messages are localized (§12) and name the character, its code point, its position and the charset.
- **Three `CharsetGuardSeamTests` fail the build** if a raw `CreateCommand`, a direct `CommandText =` or a raw
  `Parameters.AddWithValue` appears outside the seam.

⚠ The driver offers no supported hook for this: its `Charset` type is `internal sealed` and
`FbConnectionStringBuilder` has no strictness knob. Production therefore **mirrors** the driver and a test pins
the mirror against the driver's real encoder — ⛔ production takes no reflective dependency on driver internals.

---

## 11. Settings and `ApplicationSettingsStore`

Every user setting lives in one whole-file DPAPI-encrypted `settings.dat` with a versioned header and
forward-compatible migration. The store is small, and its contract is a **data-integrity guarantee**, not an
implementation detail.

```
LoadWithStatus() → SettingsLoadResult { Settings, Status }
Status ∈ { Missing, Loaded, Unreadable, Corrupt, FutureVersion }

Save(settings)          → cross-process lock → ExistingFileBlocksSave? → SaveCore
Update(mutate)          → cross-process lock → read UNDER it → mutate → SaveCore
```

⭐⭐ **The rule: a failed read must never become defaults that are then written.** Only `Missing` and `Loaded`
permit a write. `Unreadable` / `Corrupt` / `FutureVersion` mean there is a file on disk holding user data this
build cannot interpret — and overwriting data we cannot read is exactly what rule #11 forbids. `Unreadable` in
particular is usually DPAPI on a different Windows account or machine, and **may decrypt perfectly on the right
machine**, which is precisely why it must not be replaced.

⭐ **`Update` exists because read-modify-write across a lock boundary loses data.** A facade doing
`Load() ?? new()` → mutate → `Save()` turns a *transient* read failure into defaults and persists them.
Measured against a concurrent publisher: 182 failed reads, 89 of which wrote defaults, ending with **0 of 5
connection profiles surviving** — profiles and passwords, silently. `Update` takes the cross-process lock,
reads **under it**, mutates and writes via `SaveCore`. ⛔ It is not a retry; the lock's scope removes the
window. `Missing` is the only status that may produce a default aggregate, and `Update` is the only place that
may do so.

⚠ `SaveCore` is private and must not call the public `Save`: that would take the lock twice.

**Preferences** are a separate concern layered on top: `PreferencesService` owns the single live `Preferences`
instance (the store persists the whole object, so two snapshot holders would overwrite each other), every
option a control offers is generated from Core's `PreferenceOptions`, and `App` is the one place a theme is
applied. `WorkspaceState` persists open tabs and layout.

**Settings export/import** is EmberTern's own versioned, always-encrypted `.etsettings` artifact (AES-256-GCM
under a PBKDF2 passphrase key, cleartext header). An import **merges** non-destructively and keeps a
`settings.dat.pre-import-<stamp>` copy. Passwords are opt-in; `WindowBounds`, `ParameterHistory` and
`DebugWatches` never travel, and a reflection guard fails the build when a new field has no recorded decision.

---

## 12. Localization

`.resx` + `ResourceManager`; **English is the neutral set, Polish is complete**. Language changes **live**, with
no restart.

- ⛔ A localized member is never a `const` (the compiler inlines it) nor `static readonly` (frozen in the first
  language) — it is a **property**. In XAML the form is `{app:Loc Key}`, never `{x:Static}`, because `x:Static`
  is not a binding and never re-evaluates.
- **Core/Firebird hand up a `MessageKey` + arguments (`LocalizableMessage`); the App resolves the words.**
  Arguments are *data* and may legitimately be English — a raw Firebird error, a table name. Only the
  surrounding sentence is EmberTern's voice.
- ⭐⭐ **Resolution happens at the moment of display**, never earlier. Resolving in the producer freezes the
  sentence in whatever language was current then.

⚠⚠ **`App/Localization/ErrorText.cs` exists because of a failure mode worth internalising: not a missing
resource entry, but a perfect entry that nothing reads.** An exception carrying a `LocalizableMessage` is
usually *wrapped* on its way out (the charset refusal becomes a `QueryExecutionException` /
`DdlExecutionException` / `DataEditException` so existing error surfaces keep working). Reading `ex.Message` at
the display site then yields the English fallback — which is how a fully translated message reached a Polish
user in English, with a green build and a green suite. `ErrorText.Of` walks the `InnerException` chain and is
the one place an exception becomes display text; a failure that is not ours falls through to `ex.Message`
unchanged, keeping the server's own words.

⭐ **The project rule** (architecture rule 12): every new user-visible text goes through the localization
mechanism in every supported language. A message is finished only when it has been *seen* rendered in Polish,
or pinned by a test that resolves it through the path the UI actually uses.

---

## 13. Modules built on these foundations

Each of these is a client of the boundaries above rather than a parallel stack:

- **Import** — Clipboard / TXT / CSV / XLS / XLSX into an existing or new table. One `ImportPipeline` for every
  source, providers and writers as ports (`IImportProvider` / `IImportWriter`), `ImportConfiguration` as the
  single representation of every user decision, and **the module's own transaction on its own attachment**.
  Deliberately one working surface with collapsible sections, **not** a wizard.
- **Export** — one framework shared by the grids and the SQL export formats (`IExporter` / `IExportSink`), with
  a streaming XLSX writer in `EmberTern.Office`.
- **SQL Data Export (Copy as INSERT/UPDATE)** — DML de-aliased via the server's own provenance; UPDATE only on
  a catalog-verified **complete** primary key, and it refuses with a reason where proof is unavailable.
- **Script Executor** — multi-statement scripts under a caller-controlled transaction; the `Sequenced` mode
  commits after each schema statement, which is what makes a mixed DDL+DML migration runnable.
- **Performance Analysis** — execution plan + measured per-table reads feeding six `IPerformanceRule`
  implementations that produce confidence-scored findings and guidance, **never automatic fixes**.
- **Trace / Session Manager** — live views over the Services Trace API and `MON$`, with two health detectors in
  `Core/Diagnostics/SessionHealthAnalyzer`.
- **Security Manager**, **Database Properties**, **Global Search** — thin surfaces over the readers.

⚠ **Database Properties** writes only **Sweep interval · Forced writes · Reserve space** — exactly those
measured writable online without exclusivity — and sends nothing unless changed (every field is nullable,
`null` = don't touch), because the Services API has no rollback.

---

## 14. Test infrastructure

One test project, ~8 850 tests, **one command**: `dotnet test EmberTern.slnx`.
⛔ Never chain `dotnet build && dotnet test` — they deadlock.

⚠ **The acceptance criterion is the TOTAL, not "0 failures".** A broken headless state once reported *"0
failures, 7 232 total"* while 128 tests silently never started.

**Headless Avalonia.** UI tests share **one** `HeadlessUnitTestSession` per process, through an
`ICollectionFixture` (`HeadlessCollection`, name `headless-avalonia`) — ⛔ never `IClassFixture`, which would
create one session per test *class*. This is not tidiness: AvaloniaEdit builds its caret/editing key bindings as
**static** lists owned by the thread of whichever session first constructs a `TextEditor`.

The collection also **isolates process-global language state**: `Loc.LanguageChanged` is `static`, so every
view model any earlier test built stays subscribed, and the next test to swap the catalog broadcasts into all of
them. `Loc.IsolateSubscribersForVerification()` gives each headless test a clean subscriber list automatically.
⛔ The old three-partition manual split is gone and must not return — it hid two defects for months.

⚠ A test that mutates global language state must join the collection *and* should prefer not to mutate it at
all: most `UiStrings`-reading tests are **outside** the collection and run in parallel. Asserting wording
against the resource sets directly avoids the problem entirely.

### ⚠⚠ The known upstream race — recognise it, do not "fix" it

Roughly **1 full run in 3–8** loses **exactly one** test to an Avalonia infrastructure race. It is **not an
EmberTern defect**. ⭐ **The STACK identifies it, never the test name** (the victim is whichever headless test
dispatched first):

```
System.InvalidOperationException : The calling thread cannot access this object because a different thread owns it.
   at Avalonia.Rendering.DefaultRenderLoop.Add(IRenderLoopTask i)          ← Dispatcher.VerifyAccess()
   at Avalonia.Headless.AvaloniaHeadlessPlatform.Initialize(...)
   at Avalonia.Headless.HeadlessUnitTestSession.EnsureIsolatedApplication()
```

Cause: `EnsureIsolatedApplication` calls the process-wide `Dispatcher.ResetBeforeUnitTests()` on **every**
dispatch; a parallel thread constructing any Avalonia object claims `Dispatcher.UIThread` in that window.
Reproduced deterministically: 149/150 dispatches fail with 4 noise threads, 0/150 without.

**Re-run once.** ⛔ A failure *without* that stack is a real defect, however familiar the test name looks.
⛔ **Five repairs were measured and rejected** — no warm-up, no `Delay`, no retry, no global parallelism
switch-off, no manual partitioning. Full evidence: [`docs/avalonia-headless-session-race.md`](docs/avalonia-headless-session-race.md).

### Guard tests (source tripwires)

Some invariants cannot live in a type system, so they live in tests that scan sources or reflect over types.
They exist where a rule would otherwise decay silently, and each was verified **red** before being accepted:

- `CharsetGuardSeamTests` — no raw command creation, `CommandText =` or `AddWithValue` outside the seam.
- `LocalizationMechanismTests` — no `const`/`static readonly` localized member; every Core message key has an
  English entry; every Core-shaped entry is declared; child view models forward `RefreshLocalizedText`.
- `ConnectionProfileStoreTests` — Embedded mode is selected nowhere (so a client-library setting stays absent).
- `DesignTokenApplicationTests` / `DesignTokenComplianceTests` — no literal where a token exists; the Fluent
  bridge contains no local values. ⚠ `{DynamicResource}` does **not** throw on a missing key, so a typo is
  invisible at build time — that is what these catch.
- `ThirdPartyNoticesTests`, `TerminologyTests`, `DocumentMutationContractTests`, `AppInfoTests` (no version
  number in code).

**Probes** (`tools/probes/`) are the other half of verification: whatever needs a live server or a visual
judgement. `CharsetProbe` and `DebuggerFidelityProbe` in particular assert *simulated == real* against Firebird.

---

## 15. Architectural invariants

The rules that actually constrain new code. Full wording in `CLAUDE.md`.

1. ⭐⭐ **Never lose information (rule #11).** Any feature that generates DDL or modifies user code or DB
   objects must preserve every fragment it does not fully understand, **verbatim 1:1**. **If EmberTern cannot
   prove it reproduces an object identically, it must not modify it automatically** — uncertainty ⇒ do nothing
   or ask. Realised by: the AST round-trip, the formatter's lexeme invariant, `ObjectChangeSafety`, the
   settings-store status rules, and the charset guard.
2. ⭐ **Fidelity (§F).** The debugger's semantics come from the server, via an anonymous harness that never
   names the routine. Nothing may be simulated that the engine can be asked.
3. ⭐ **Every user-visible text is localized**, in every supported language, resolved at display time.
4. ⛔ **A failed settings read never becomes defaults that are written.**
5. ⛔ **No silent conversion at the driver boundary** — refuse and say what and where.
6. ⛔ **No autocommit.**
7. **Core has zero Avalonia and zero driver dependencies**; ViewModels hold no Avalonia types.
8. **One owner per question** — one splitter, one command registry, one message surface, one charset oracle,
   one theme catalog, one `ISqlMetadataProvider` seam.
9. ⛔ **No silent fallbacks as a way around a problem.** Where a fallback exists it is a *decision* with a
   written reason beside it.
10. **A rule that is easy to break gets a guard test**, verified red first.

---

## 16. Deliberate limits

Things that look like gaps and are not:

- **No plugin system, no schema compare, no docking.** Scope decisions, not omissions.
- **`Avalonia.AvaloniaEdit` 12.0.0 sits below the Avalonia core (12.1.1); `Avalonia.Controls.DataGrid` 12.1.2
  sits above it.** ⚠ Neither is a pin: **no 12.1.x AvaloniaEdit and no 12.1.1 DataGrid were ever published.**
  Three independent release cycles, verified against nuget.org — it resolves when upstream ships.
- **`Avalonia.Controls.DataGrid` is officially deprecated upstream** ("bug fixes only"). Migrating is a product
  decision with a large surface, not a framework-update chore.
- **`NONE` remains in the connection charset list** although it is lossy and machine-dependent — an open
  product decision, recorded rather than silently resolved.
- **The read-side "cannot transliterate" message is the server's bare wording.** Reads are *safe* (the server
  refuses loudly); making that refusal friendlier is open work, not a hole.
- **`ClientLibraryPath` is deliberately absent.** EmberTern connects with `FbServerType.Default` — the pure
  managed wire protocol, where `fbclient.dll` is never loaded — so the setting would be a decision that could
  have no effect. Two guards keep it out.
- **The connection error shows the raw server message.** Earlier builds tried to categorise causes and
  mis-hinted on unrelated failures; the server's message is authoritative.
- **Editor folding and editor breadcrumbs are not built.** Both would consume the same AST and need no further
  foundation; they are simply not scheduled. ⚠ **Naming trap:** `Controls/BreadcrumbBar` *does* exist — it is
  the **debugger's call-stack breadcrumb** (frame navigation, routed with the call-stack rows and
  Ctrl+Alt+Up/Down), an entirely different feature that happens to share the word. Grepping for "breadcrumb"
  will find it; folding genuinely has zero occurrences.

---

## 17. Where to read further

| You need | Read |
|---|---|
| Rules, live gotchas, the documentation map | [`CLAUDE.md`](CLAUDE.md) |
| What is done / in progress / next | [`docs/current-state.md`](docs/current-state.md) |
| A familiar-looking bug | [`docs/gotchas.md`](docs/gotchas.md) |
| Editor internals | `docs/design/editor-architecture.md` (+ `-ast-deepening`, `-stage7-diagnostics`, `-quick-fixes`, `-language-expansion`) |
| Debugger behaviour and its contract | `docs/design/firebird-debugger.md` + `-implementation-plan.md` |
| Settings, preferences, export format | `docs/design/settings-center.md` |
| Localization decisions | `docs/design/localization.md` |
| Import (frozen architecture) | `docs/design/data-import.md` |
| UI metrics, tokens, colour | `docs/design/product-polish.md`, `design/color-language.md` |
| Why a decision went the way it did | `docs/history/` (index: `docs/history/README.md`) |
