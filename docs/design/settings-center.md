# Settings Center & SQL Formatter Profiles — sprint design document

**🔒 STATUS: ETAP 1 CLOSED — design ACCEPTED AND CLOSED by the user 2026-07-29, all 11 decisions
ratified (§9). Nothing implemented; no production code touched. Etap 2 is cleared to start.**
Branch: `feat/settings-center`.

**Start here:** §9 for the decisions, §10 for the etap plan, §2 for the measured facts that constrain
everything else. The four **⭐ measured findings in §2** are the ones that will cost a future session
time if skipped — especially §2.2 (the formatter has no casing decision point) and §2.3 (localization is
not built, so §6.2's Language row is deliberately storage-only).

This document is the sprint's single source, in the standard set by
[`keyboard-manager.md`](keyboard-manager.md) and [`hamburger-navigation.md`](hamburger-navigation.md):
measured facts first, then the inventory, then the architecture, then the decisions — now ratified in §9.

⚠ **§9 is the authority on every decision. Where an earlier section recommended something the user
decided differently, the ratified text is in place and the recommendation is marked superseded — do not
re-litigate it from the reasoning that led to it.** One recommendation was overridden (Language, §6.2)
and the reasoning behind it is retained only because it names a real future cost, not because it is
still an open question.

---

## 0. What this sprint is, in one line

EmberTern has **settings everywhere and a settings surface nowhere** — 7 store facades over one
encrypted file, ~40 persisted preferences, ~15 hard-coded constants that are settings in waiting, and
one titlebar button whose choice is forgotten on exit. This sprint gives them one home, gives the
formatter its first two user-owned style decisions, and makes the configuration portable between
machines.

⚠ **Formatter *profiles* are out of scope despite the title** — exactly two casing settings ship
(§6.4/§7.11). The title is the sprint's working name, kept for continuity.

⚠ **The hamburger sprint already shipped the door: `Settings…` is a disabled `MenuItem` at
[`MainWindow.axaml:139-142`](../../src/EmberTern.App/Views/MainWindow.axaml:139) with the tooltip
*"Not available yet"*.** That is the one existing UI element this sprint replaces.

---

## 1. Method

The user's instruction was audit-first, and the audit was run against the code, not against
recollection: every claim below carries a file and line, and several of them **contradict what the
comments in that same code say**. Four findings changed the shape of the design and one of them
falsifies a premise in the sprint request itself (§2.3).

Where a comment and the code disagreed, the code won and the discrepancy is recorded — the same rule
the 2026-07-27 hardening sprint applied to the external audit
([`feedback-verify-external-analysis`](../../CLAUDE.md)).

---

## 2. Measured facts that constrain the design

### 2.1 ⭐ The theme is not "reset on restart" — it is **never saved**, and there is no code that would save it

The user reported: *"zmiana motywu działa tylko do zamknięcia programu"* and asked for the cause
without a fix. The cause is not a failing write. **There is no write.**

Two lines are the whole story:

| Where | What it does |
|---|---|
| [`App.axaml:4`](../../src/EmberTern.App/App.axaml:4) | `RequestedThemeVariant="Dark"` — hard-coded on the `<Application>` element, applied on every launch |
| [`MainWindow.axaml.cs:1564-1576`](../../src/EmberTern.App/Views/MainWindow.axaml.cs:1564) | `OnThemeToggleClick` flips `Application.Current.RequestedThemeVariant` — **in memory, and nothing else** |

Verified absent: no read of a persisted theme anywhere at startup, no theme field in
`ApplicationSettings` / `UserSettings` / `WorkspaceState`, no `theme` key in `settings.dat`'s schema. A
project-wide grep for `RequestedThemeVariant` returns exactly the two sites above.

So this is **a missing feature, not a broken one** — which matters, because a "fix" is not a one-line
repair to an existing path; it is a new persisted preference, a new read at startup, and a decision
about *where* that value lives (§5.3). It also brushes against architecture rule #1, which says the
theme toggle lives in code-behind *on purpose* — see decision **Q5**.

⚠ **A trap to avoid when this is implemented:** `App.axaml`'s hard-coded `Dark` is what makes the
window paint before any store is read. Removing it without supplying a value first gives Avalonia
`ThemeVariant.Default`, which follows the **OS** theme — a silent behaviour change for every existing
user, and one that reads exactly like a regression. The startup order has to be: read the stored
value, then assign, and keep `Dark` as the fallback when nothing is stored.

### 2.2 ⭐ The formatter has **no casing decision point**, and cannot tell a keyword from an identifier

"Keywords: Upper/Lower + Identifiers: Upper/Lower" reads like two booleans. In this codebase it is
neither two booleans nor one place.

**(a) Casing is applied at ~9 independent sites**, most of them inline literals:

| Site | Code |
|---|---|
[`SqlFormatter.cs:1493-1497`](../../src/EmberTern.Core/Sql/SqlFormatter.cs:1493) | `MaybeLowercaseWord(SqlToken)` — `Identifier or Keyword => ToLowerInvariant()`
[`SqlFormatter.cs:1513-1516`](../../src/EmberTern.Core/Sql/SqlFormatter.cs:1513) | `MaybeLowercase(FToken)` — `FKind.Word => ToLowerInvariant()`
`:512`, `:579`, `:922`, `:1252`, `:2014` | five further direct `ToLowerInvariant()` calls on token text
`:1604` | a `ToUpperInvariant()` — but for *comparison* (`up == "BEGIN"`), not output

**(b) The formatter's own token kind collapses the distinction the setting needs.**
[`SqlFormatter.cs:360`](../../src/EmberTern.Core/Sql/SqlFormatter.cs:360):

```csharp
private enum FKind { Word, Number, String, QuotedIdent, LineComment, BlockComment, Punctuation }
```

`FKind.Word` **is keywords + identifiers + named parameters together**, and the AST-level path at
`:1495` fuses `Identifier or Keyword` just as firmly. Neither emitter can answer *"is this token a
keyword?"* — so the two requested settings are not separable without first introducing that
classification. The vocabulary to do it already exists and is already the single authority
(`FirebirdSyntax.IsKeyword`, the catalog the lexer and the XSHD drift-guard share), so this is a
re-classification at `Flatten` time, not a new keyword list.

**(c) The formatter's design note already promised this sprint.**
[`SqlFormatter.cs:43-46`](../../src/EmberTern.Core/Sql/SqlFormatter.cs:43): *"one opinionated
IBExpert-inspired layout, lowercase-all… **A configurable style profile is deferred to the future
application configurator**; the single default lives in the constants below."* This sprint is that
configurator, and the constants block at `:52-56` is where its style values were parked.

**(d) ⚠ The §0 safety net's own comparison is coupled to the casing policy — and survives, but its
stated reason does not.** [`SqlFormatter.cs:2010-2013`](../../src/EmberTern.Core/Sql/SqlFormatter.cs:2010),
inside the lexeme net that guarantees no token is ever lost:

```csharp
// Words are lowercased on output → compare case-insensitively.
TokenKind.Keyword or TokenKind.Identifier or TokenKind.Parameter
    => new Lexeme(LexClass.Word, t.Text.ToLowerInvariant()),
```

The comparison **stays correct** under any casing setting — it lowercases both sides, so uppercase
output still compares equal. But the *justification written on it* becomes false the moment output can
be uppercase, and a future reader could "simplify" it back to a case-sensitive compare on the strength
of that comment and silently disarm §0 for every keyword. The comment must be corrected in the same
etap that makes casing configurable.

**(e) ⚠ Identifier casing is the §0-sensitive half, and it is already handled — do not re-solve it.**
Firebird folds an unquoted identifier to upper, so re-casing one is semantically free; a **quoted**
identifier is case-sensitive and re-casing it changes which object is named. Both emitters already
pass `QuotedIdent` / `TokenKind.QuotedIdentifier` through verbatim (`:1496`, `:1516`), which is exactly
the rule §0 requires. The new setting must be applied *inside* that existing guard, never around it.

### 2.3 ⭐ EmberTern is **not** prepared for localization — the sprint request's premise is wrong

The request states *"Program jest już przygotowany pod lokalizację"* (the program is already prepared
for localization). Measured, it is not, and the gap is structural rather than incidental:

| Measurement | Value |
|---|---|
`public const string` members in `UiStrings` | **1 815** |
`public static readonly string` members | 39 |
`.axaml` files binding `{x:Static app:UiStrings.…}` | 42 |
`.resx` / `.resources` files in the repo | **0** |
`CultureInfo.CurrentUICulture` / `NeutralResourcesLanguage` / satellite-assembly usage | **0** |

A C# `const` is **inlined into every call site by the compiler**. There is no field left at runtime to
reassign, so 1 815 of the app's 1 854 strings are *physically* not swappable — not "not wired up yet",
but not present as storage. And because `x:Static` produces no change notification, even the 39
`static readonly` members would not refresh a visible window if something did reassign them.

This is not an oversight: **architecture rule #6 forbids `AppResources.resx`** and names `UiStrings` as
the deliberate replacement. Localization was never designed for; the rule pointed away from it.

Consequences the sprint must accept:

- Adding Polish is **its own milestone**, not a later dropdown entry: convert ~1 854 members from
  `const` to an indexed, notifying lookup; revisit 42 XAML files; decide whether switching language
  requires a restart (with `x:Static` gone, a markup extension could refresh live — that is a design
  choice, not a given). Scoped in §8.
- **The Language row itself ships in this sprint** — ratified **Q4**, overriding my recommendation. §6.2
  is the as-designed text; what makes it honest rather than decorative is that its *storage* is real and
  read from day one, and only its *effect* waits for §8.

⚠ **The measurement above is the reason §8 exists and is not optional reading.** "Language is a
dropdown away" is the natural inference from seeing the row in the window, and it is wrong by three
orders of magnitude — 1 854 members, 42 XAML files, and one test guard that stops working (§8/4). A
future session that reads §6.2 without §2.3 will underestimate Polish badly.

### 2.4 ⭐ Export/Import is already architected — the seam was reserved for it by name

This is the sprint's happiest finding. Two files anticipate this exact requirement:

[`SecretProtector.cs:10-13`](../../src/EmberTern.Core/Security/SecretProtector.cs:10):
> *"This is the foundation the upcoming ApplicationSettings store and the **planned configuration
> export/import** will share: each one takes the same SecretProtector rather than re-deriving crypto."*

[`EncryptionSchemes.cs:22-27`](../../src/EmberTern.Core/Security/EncryptionSchemes.cs:22):
```
// ---- Reserved for future milestones (NOT implemented — do not emit yet) ---------
// When one of these lands, add the protector and register it in
// ApplicationSettingsStore.ResolveProtector, then start writing it as the scheme:
//   "aes256-passphrase" — portable, passphrase-derived key (config export/import).
```

And the registration point named there already exists and already has the right shape —
[`ApplicationSettingsStore.ResolveProtector`](../../src/EmberTern.Core/Settings/ApplicationSettingsStore.cs:237),
whose comment reads *"this is the seam where future schemes (AES, passphrase export/import) get
registered"*.

So export/import needs **no new crypto plumbing**: a scheme implementation, a content filter, and a UI.
The DPAPI non-portability the user identified as the blocker is real and is
[documented as intentional](../../src/EmberTern.Core/Security/EncryptionSchemes.cs:18) — *"Not portable
across machines/accounts by design"* — which is exactly why a second, portable scheme was reserved
rather than DPAPI being loosened.

⚠ **It does, however, need its own versioned envelope — the `settings.dat` container is not enough.**
`SettingsFileContainer`'s header carries a container version and a scheme, which is the right *pattern*
(header cleartext, payload encrypted) but not the right *content*: an export additionally needs an export
format version, an app version and KDF parameters, and its versioning must move independently of
`settings.dat`'s. Ratified in §6.3 — **an earlier draft of this section said "reuse the existing envelope,
no new format", and that was too optimistic in exactly the way §2.2 was about the formatter.**

⚠ One reserved-comment detail that is now **deliberately unused**: `ResolveProtector` accepts a plaintext
(`none`) payload *"e.g. a dev/exported file opened by a DPAPI build"*. Since every export is encrypted
(**Q3**), that path will never carry an export. It stays as-is for the legacy-headerless `settings.dat`
case it also serves — but do not read that comment as sanctioning an unencrypted export.

### 2.5 `Save` refuses over a file it cannot read — Settings Center inherits this, and must say so

Since the 2026-07-27 hardening sprint,
[`ApplicationSettingsStore.Save`](../../src/EmberTern.Core/Settings/ApplicationSettingsStore.cs:264)
**silently declines** to write whenever the file on disk is unreadable, corrupt, or from a newer build
(`ExistingFileBlocksSave`), because the values being written would be defaults standing in for data
still sitting in that file. Its own doc-comment: *"Refusal is the feature."*

Two consequences for a settings surface:

1. **A settings dialog can appear to accept a change and persist nothing.** Every other writer in the
   app is an incidental one (a column resized, a procedure run) where silence is right. A dialog whose
   explicit purpose is *"change this setting"* is the one place where silence is wrong — Settings Center
   must surface the refusal (§5.5).
2. The app already has the vocabulary: `MainWindowViewModel.CaptureSettingsHealth`
   ([`:7259`](../../src/EmberTern.App/ViewModels/MainWindowViewModel.cs:7259)) reads
   `store.CheckSettingsHealth()` → `LoadWithStatus()` and shows a docked shared `MessageBanner`.
   Settings Center reuses that banner and that status, and adds **no second health mechanism**.

### 2.6 There is no "settings" section in `settings.dat` — there are seven facades and no preferences owner

[`ApplicationSettings`](../../src/EmberTern.Core/Settings/ApplicationSettings.cs) has four members:
`Connections`, `Folders`, `Workspace`, `UserSettings`. Seven facades read and write slices of it:

`ConnectionProfileStore` · `FolderStore` · `WorkspaceStore` · `GridProfileStore` ·
`ParameterHistoryStore` · `WatchStore` · `ImportProfileStore`

⚠ **Not one of them owns a scalar preference.** `UserSettings` holds four *lists*
(`GridProfiles`, `ParameterHistory`, `DebugWatches`, `ImportProfiles`) and nothing else — so today
there is literally nowhere for "theme = Light" to go. Meanwhile the scalar preferences that *do* exist
(§3.1) all live in `WorkspaceState`, beside window bounds and tab lists, because that is the only
class that ever accepted one.

This is the single largest architectural item in the sprint, and §5.2 addresses it.

### 2.7 The monospace font has **four** divergent strings across ten files, not three

CLAUDE.md records this as *"three copies"*. Measured, it is worse:

| String | Sites |
|---|---|
`"Cascadia Code,Consolas,Menlo,monospace"` | `HoverInfoView` ×3, `AddFieldDialog` ×4 |
`"Cascadia Mono, Consolas, Menlo, monospace"` | `LanguageExpansionController`, `NavigationController` ×2, `ParameterHelper` |
`"Cascadia Code,Consolas,Courier New,monospace"` | `SqlSnippetDropTarget` |
(+ `ThirdPartyNoticesWindow`, per the hamburger sprint) | |

These are not four spellings of one font — **`Cascadia Code` and `Cascadia Mono` are different
typefaces** (Code has programming ligatures, Mono does not), and the fallback chains differ too. So the
app already renders monospace text inconsistently. This is the prerequisite for any font setting, and
it is why §7.1 recommends splitting consolidation from configuration.

---

## 3. Inventory — every setting that exists today

### 3.1 Persisted in `settings.dat`

**`WorkspaceState`** — [`WorkspaceState.cs`](../../src/EmberTern.Core/Workspace/WorkspaceState.cs).
The de-facto preferences bag (§2.6). ⭐ marks a genuine global preference currently with no UI beyond
the gesture that sets it:

| Field | Kind | Belongs in Settings Center? |
|---|---|---|
`WindowBounds` (X/Y/W/H/State) | machine-dependent layout | no — restored automatically |
`Workspaces` (per-connection tabs, SQL, saved queries) | user content | no — but §7.5 governs *whether* it restores |
`LastActiveConnectionId` | session state | no |
⭐ `QueryPanelVisible` | layout preference | no — toggled in place, correct there |
⭐ `SidebarWidth` / `SidebarCollapsed` | layout | no — dragged in place |
⭐ `ResultsPanelHeight` / `ResultsMaximized` | layout | no |
⭐ `BottomPanelTabIndex` | last-viewed tab | no |
⭐ `ProcedureEasyMode` / `ViewEasyMode` / `TriggerEasyMode` / `FunctionEasyMode` | **real preference** — seeds newly opened editors | **candidate** (§7.6) |
⭐ `ImportPreviewPanelHeight` / `ImportPreviewPanelCollapsed` | layout | no |

The four `*EasyMode` flags are the interesting row: their own comments call them a "hybrid model" whose
job is to *seed* the mode for newly opened objects. That is a default, i.e. a setting — but it is set
implicitly by the last toggle, which most users will never connect to the behaviour they see.

**`UserSettings`** — four lists, all data rather than preference:
`GridProfiles` (per-grid column order/width/auto-fit) · `ParameterHistory` · `DebugWatches` ·
`ImportProfiles`.

⚠ **`GridProfile`'s comment is stale.** It claims *"no consumer wires it yet"*, but
[`MainWindow.axaml.cs:611`](../../src/EmberTern.App/Views/MainWindow.axaml.cs:611) assigns
`GridLayoutBehavior.Store` and the behavior saves on change and on window close. Grid layout
persistence is **live**, which makes its hard-coded default (`AutoFitColumns = true`, at
[`GridLayoutBehavior.cs:157`](../../src/EmberTern.App/Behaviors/GridLayoutBehavior.cs:157)) a real
setting with no way to change it (§7.4).

**`ConnectionProfile`** — per-connection, edited in the connection dialog. Correctly **not** global:
`Host` · `Port` · `DatabasePath` · `Username` · `Password` · `Charset` · `Dialect` ·
`ClientLibraryPath` · `DeveloperMode` · `DataTransactionProfile` · `MetadataTransactionProfile`.

⚠ Two notes. `Charset` defaults to `WIN1250` and is the input to the deferred platform-wide
[silent charset data-loss audit](../../CLAUDE.md) — Settings Center must not touch it, and must not
become the place that audit gets quietly half-solved. And the two `*TransactionProfile` fields are
**enforced away**: `TransactionService` hard-enforces `ReadCommitted`, which is why the status chips
now read `TransactionService.EnforcedProfile`. Surfacing them as configurable would re-create the lie
the hardening sprint removed.

### 3.2 Live UI controls that are really settings

| Control | Where | Persisted? | Verdict |
|---|---|---|---|
**Theme toggle** | titlebar button | **no** (§2.1) | keep the button, add the setting, persist it |
`Settings…` | hamburger menu | — | **this sprint replaces it** |
Execution mode (Preview / Full) | SQL editor toolbar | per-session | keep in place — a per-execution choice |
Session Manager auto-refresh | in-tab picker | per-tab | keep in place; its *default* is not worth a setting |
Debugger isolation | launch panel → Advanced | per-launch | keep, add a global **default** (§7.3) |
Developer Mode | connection dialog | per-connection | keep per-connection |
Grid auto-fit / column order | grid gestures | per-grid | keep; add a global **default** (§7.4) |

### 3.3 Hard-coded constants that are settings in waiting

| Constant | Value | File |
|---|---|---|
`ExecutionDefaults.PreviewLimit` | 5 000 | [`ExecutionDefaults.cs:15`](../../src/EmberTern.Core/Query/ExecutionDefaults.cs:15) |
`ExecutionDefaults.FullSoftThreshold` | 250 000 | `:24` |
`ExecutionDefaults.FullSafetyCeiling` | 1 000 000 | `:19` |
`TableDetailTabViewModel.DataPreviewRowLimit` | 200 | `:25` — **duplicated** in `ViewDetailTabViewModel:32` |
`SqlFormatter.MaxLineWidth` | 120 | `:55` |
`SqlFormatter.PsqlIndentSize` | 2 | `:56` |
`SqlFormatter` casing | lowercase-all | §2.2 |
`GridLayoutBehavior` default auto-fit | `true` | `:157` |
`EditorLanguageService.ParseDebounce` | 300 ms | `:36` |
`NavigationController.HoverDwell` | 350 ms | `:74` |
`SqlCompletionController.AutoPopupDelay` | 250 ms | `:55` |
`SqlCompletionContext.AutoTriggerMinLength` | 3 | `:10` |
`ImportConfiguration.DefaultBatchSize` / `DefaultCommitEveryRows` | 500 / 10 000 | `:226`, `:229` |

⭐ **`ExecutionDefaults` was written for this sprint.** Its class comment: *"Hardcoded for now — there
is deliberately no user configuration yet. They live here (never as scattered literals) so that moving
them into user settings later, **when EmberTern's configuration system is designed**, is a one-line
change at the call site."* And the claim checks out: the limits travel as values on
`ExecutionRequest`, so nothing reads a global. This is the lowest-cost, highest-value optional setting
in the sprint (§7.2).

The four editor timing constants are deliberately **not** proposed — see §7.8.

---

## 4. Where the user cannot configure and arguably should

Ranked by how often it would actually matter:

1. **Theme** — set every session, remembered never (§2.1). The clearest gap in the app.
2. **Preview row limit** — 5 000 is a taste-and-hardware trade-off, and the class that holds it says so.
3. **Whether the workspace restores on startup** — always on, no opt-out. A user who wants a clean
   start has no way to ask for one, and a stale restored tab set is a recurring annoyance class.
4. **Formatter casing** — the sprint's explicit ask; `lowercase-all` is one house style asserted over
   everyone (many Firebird/ERP shops write `SELECT` upper).
5. **Table/View data page size** — 200 rows, duplicated in two view models, no UI.
6. **Debugger default isolation** — recorded as a user wish in the D4 UX backlog (*"move
   transaction-isolation config to global Settings"*), answered in D15.3 only by moving it into an
   Advanced disclosure. The wish itself is still open.
7. **Editor / monospace font** — inconsistent today (§2.7) and unchangeable.
8. **Settings portability** — the sprint's explicit ask; DPAPI makes a second machine a fresh install.

---

## 5. Architecture proposal

### 5.1 Surface shape: a modal window, not a workspace tab

Settings Center should be a **window opened from the hamburger**, in the same family as `About` and
`Keyboard Shortcuts` — not a `WorkspaceTabKind`.

Reasoning, from this codebase rather than from convention: a workspace tab would automatically acquire
workspace persistence (`SnapshotCurrentTabs`), dirty tracking (`IUnsavedWork`), the three-way close
guard, `RefreshAsync` dispatch, and a `ResolveCommand` arm — five per-kind families it would have to be
threaded into (or explicitly excluded from, as the debugger and import tabs each had to be), for a
surface the user visits rarely and never edits *work* in. The transient-tool tabs each needed an
explicit skip-list entry; a settings tab would need the same, which is a cost with no matching benefit.

Layout: the standard two-pane settings shape — a category list on the left, the selected category's
page on the right, and a **search box above the category list** (§5.4). Chrome per the UI styling
rules: `BackgroundBrush` / `ForegroundBrush` on the root, `Classes="h1"` headers,
`Classes="field-label"` captions, `Button.primary` / `Button.flat`.

⚠ **The window is where the backlogged app-wide UX/density sprint will bite hardest.** Settings Center
is nothing *but* ordinary form controls — `TextBox`, `ComboBox`, `CheckBox`, `NumericUpDown` — which is
exactly the set with [no implicit style at all](../../src/EmberTern.App/Themes/ControlStyles.axaml) and
therefore on FluentTheme's 32 px defaults. This sprint must **not** fix that globally (standing
instruction: a module etap delivers the module). It should be built plainly, and it will be one of the
UX sprint's best test surfaces.

### 5.2 ⭐ The one real architectural change: `UserSettings` gains a `Preferences` object

Today there is nowhere to put a scalar preference (§2.6). The proposal is one new class and one new
facade — deliberately the smallest change that stops `WorkspaceState` being the default dumping ground:

```
ApplicationSettings
├── Connections            (unchanged)
├── Folders                (unchanged)
├── Workspace              (unchanged — layout + session state stays here)
└── UserSettings
    ├── GridProfiles       (unchanged)
    ├── ParameterHistory   (unchanged)
    ├── DebugWatches       (unchanged)
    ├── ImportProfiles     (unchanged)
    └── Preferences        ← NEW: the scalar user-preference object
        ├── Theme                  : string   ("Dark" | "Light")            — etap 2/3
        ├── Language               : string   ISO code, "en" today (§6.2)   — etap 2/3
        ├── FormatterKeywordCase   : string   ("Lower" | "Upper")           — etap 4
        ├── FormatterIdentifierCase: string   ("Lower" | "Upper")           — etap 4
        └── … (each approved §7 setting is one more scalar)                 — etap 6
```

Five properties of this shape are load-bearing:

1. **Additive — no schema bump.** An older `settings.dat` deserializes `Preferences` as a default
   instance. `ApplicationSettingsStore.CurrentSchemaVersion` stays **2**, for the reason the C3
   milestone already established: a bump trips downgrade protection and makes older builds refuse the
   *whole file*. `ParameterValue.TypeText` and `ImportProfiles` both set this precedent explicitly.
2. **Strings, not Avalonia enums.** `Theme` is `"Dark"`/`"Light"`, never `ThemeVariant` — architecture
   rule #1 (Core has zero Avalonia dependencies). `WorkspaceState.WindowBounds.WindowState` is the
   existing precedent: *"Stored as string … so Core stays free of any Avalonia enum dependency."*
3. **The 8th facade, matching the 7 that exist.** `PreferencesStore` follows `WatchStore` /
   `GridProfileStore` exactly — `Load()`, mutate its slice, `Save()`. No new persistence mechanism, and
   `Save`'s refusal (§2.5) protects it for free.
4. **A defaults contract.** Every scalar's C# initializer **is** its default, so "restore defaults"
   is `new Preferences()` and cannot drift from a separately-maintained table. Formalised as a binding
   rule in §5.2.1.
5. **Layout stays in `WorkspaceState`.** Panel heights and window bounds are not preferences the user
   goes looking for; moving them would be a migration with no user-visible gain. The dividing line —
   *if the user would look for it in a settings dialog, it is a `Preference`; if they set it by
   dragging or clicking the thing itself, it is `WorkspaceState`* — is what keeps the new class from
   becoming the next dumping ground.

#### 5.2.1 ⭐ RATIFIED — `Preferences` is a self-sufficient contract; the store only validates

**User, 2026-07-29, binding on etap 2.** Two responsibilities, one owner each:

| | Owns | Never does |
|---|---|---|
**`Preferences`** (the model) | **Safe defaults.** Every property carries a valid value from its own initializer, so a freshly constructed instance is **always** correct and needs no initialization step. | validate; normalize; know about files |
**`PreferencesStore`** (the facade) | **Validation and normalization of what was read from the file** — an unrecognised value is replaced with the model's default (unknown language → `"en"`, unknown theme → `"Dark"`). | supply defaults of its own |

**The invariant this buys: `new Preferences()` is valid, unconditionally.** Every consumer, test and
"restore defaults" path can rely on it without a null check or a bootstrap call.

Four consequences that are easy to violate while technically honouring the rule:

1. ⭐ **No property may be "nullable meaning unset".** A `string?` whose null means *not chosen yet* pushes
   the default decision to whoever reads it — and there will eventually be more than one reader, which is
   how one default becomes three. Each property has a real value or it does not belong here.
2. ⭐ **Normalization is silent and TOTAL, not rejection.** The store never refuses to load because a value
   was bad; it corrects it and continues. Every field is valid when `Load` returns, always. A settings file
   is not a document to be validated at the user — it is state to be brought into a usable shape.
3. **Normalizing on read does not rewrite the file.** The correction lives in memory and reaches disk only
   if something later saves for its own reasons. Do **not** add a "repair the file on load" write — `Load`
   writing is precisely the shape audit A-03 was about, and §2.5's `Save` refusal exists to stop it.
4. ⭐ **The rule is directly testable, and the test is the point** — `Validate(new Preferences())` must
   return a value **equal to** `new Preferences()`. That single assertion pins model and store against each
   other, and it fails the build the day someone adds a property whose initializer the validator would
   reject (e.g. a `Language = "pl"` default while the catalog still has one row). Without it, that drift
   is invisible: both halves look correct in isolation.

⚠ **`Language` is validated from day one even though nothing consumes it** (§5.3, §6.2). It is the one
property with no reader, which makes it the one most likely to be left unvalidated *"until it matters"* —
and §8 is far enough away that a bad value would be well established by the time it does.

### 5.3 Consuming a preference: read once at startup, apply through the existing owner

Every consumer already has an owner; the preference feeds that owner, and no consumer learns about the
store:

| Preference | Read by | Applied through |
|---|---|---|
Theme | `App.OnFrameworkInitializationCompleted` (before the window shows) | `Application.RequestedThemeVariant` — the existing property |
Language | Settings Center, resolved against the language catalog | **nothing yet** — the value is stored and validated; §8 is what will consume it (§6.2) |
Formatter casing | the caller that invokes `SqlFormatter.Format` | a `FormatterStyle` argument (§6.4) |
Row limits | the call site filling `ExecutionRequest` | the value already on the request |
Grid auto-fit default | `GridLayoutBehavior.LoadProfile` | the existing `GridProfile` default |

⚠ `Language` is the one row in this table with no downstream consumer, and that is the ratified design
(**Q4**), not an unfinished wire. It must still be *validated* on read — an unknown code falls back to
English (§6.2/4) — because a stored value nothing consumes is exactly the kind of field that silently
accumulates garbage until the milestone that finally reads it.

⚠ **`SqlFormatter` must not read a global.** It is a `static` pure class with a documented "pure — no
Avalonia, no Firebird driver — and offline unit-testable" contract; reading ambient configuration from
inside it would make its output depend on process state and would break the §0 differential tests,
which format the same input twice and compare bytes. The style travels **in**, as a parameter with a
default — see §6.4.

### 5.4 Search: filter the category list and the settings within it

One search box, filtering across every setting's label, its category name, and a small keyword set per
setting (so *"colour"* finds Theme and *"case"* finds the formatter rows). Matching should reuse
[`CompletionMatcher`](../../src/EmberTern.Core/Sql/Language/Completion/) only if its prefix-first
philosophy fits; it almost certainly does **not** — completion is a prediction engine that deliberately
refuses a `Contains` fallback, whereas settings search is a *search* engine where substring matching is
the whole point. **Recommendation: a plain case-insensitive `Contains` over the label + keywords, and
an explicit note in the code saying why it is not `CompletionMatcher`** — otherwise a future reader
will "unify" them and break one of the two.

The setting metadata this needs (label, category, keywords) is the same table the UI renders, so it is
one declarative list built once at type-init — **the `CommandCatalog` / `LanguageConstructCatalog`
pattern**, which is this project's established answer to "one declarative table, many readers".

### 5.5 Saving: apply immediately, and say so when the store refuses

**Apply-on-change, no OK/Cancel.** Precedents: Security Manager applies immediately; the connection
dialog is the app's only OK/Cancel settings-ish surface and it edits a *record*, not preferences.
Apply-on-change also means the theme radio does what a theme radio should — repaint the app as you
click it.

But (§2.5) `Save` can refuse silently. So `PreferencesStore.Save` must report whether it wrote, and
Settings Center must show a docked shared `MessageBanner` (`Warning`) when it did not — reusing
`CheckSettingsHealth` and the existing banner, adding no second mechanism. **This is not a nicety: a
settings dialog that appears to accept a change and persists nothing is the worst possible place for
that silence.**

"Restore defaults" is per-page, and is `new Preferences()`'s value for that page's fields (§5.2/4).

### 5.6 Commands: no `CommandId` yet, for the reason the hamburger sprint ratified

Opening Settings Center gets **no** `CommandDescriptor`, on the rule
[`keyboard-manager.md`](keyboard-manager.md) established and the hamburger sprint applied to its own
four rows: *a command earns an id only when a shared surface must speak about it.* Nothing lists it and
it has no shortcut. It earns one when a Command Palette exists — a one-line addition then.

⚠ **Do not give Settings Center `Ctrl+,`.** It is a plausible-looking convention, but the ratified
shortcut map assigns F-keys to *frequent* operations and Settings is by design rarely used; adding an
unratified gesture would also have to pass the collision validator and appear in Keyboard Shortcuts,
which means shipping a key the user never chose.

### 5.7 Keyboard Shortcuts stays where it is — for now

The Keyboard Shortcuts window is a read-only projection of `CommandCatalog` and already has a hamburger
row. It should **not** be duplicated as a Settings page (that is two lists of shortcuts, the exact
drift the keyboard sprint eliminated). When a **shortcut editor** is built it becomes a Settings
category and the hamburger row points there — the registry is already the single source that makes that
cheap.

---

## 6. The four required areas

### 6.1 General → Theme

Two radio buttons (Dark / Light) bound to `Preferences.Theme`, applied live.

The titlebar toggle **stays** — a one-click theme flip is good UX and users have it in muscle memory.
It simply also persists now. Both write the same preference, so they cannot disagree.

⚠ Follow §2.1's startup-order trap. And note this touches architecture rule #1's *"Theme toggle lives
in code-behind on purpose — single button, no value routing through VM"*: the toggle can stay in
code-behind, but persistence means code-behind now calls a store. See **Q5**.

**Deliberately not offered: "Follow system theme".** It is one more state to test in every screenshot
and QA pass, the app has exactly two hand-tuned dictionaries rather than a computed palette, and no one
has asked for it.

### 6.2 General → Language — ⭐ RATIFIED: ships now, with English as the only value

**Decision (user, 2026-07-29, overriding my recommendation — do not re-litigate):** the Language row
ships in this sprint. The stated rationale is the design one, and it is the right one: Language is part
of a *complete* Settings Center, and having the row and its storage in place now means **the window
layout is not rebuilt when Polish arrives**. My §2.3 objection was about the mechanism being absent, not
about the layout, and layout churn is a real cost I had not weighed.

**What ships**, and the shape that makes it honest rather than decorative:

1. **A normal, enabled `ComboBox`** with one item, `English`. Not disabled and not tooltipped as
   unavailable — the user's explicit position is that this is a real parameter, not a placeholder, so
   presenting it as broken would misrepresent it.
2. ⭐ **`Preferences.Language : string` is written and read from day one** — an ISO code (`"en"`), stored,
   round-tripped, and resolved at startup against the available-languages list. It is a *live* preference
   whose value happens to have one legal setting today. This is the difference between "a row wired to
   nothing" and "a row wired to a list with one entry", and it is what keeps the hamburger rule satisfied
   in spirit: the row does exactly what it says, completely.
3. ⭐ **The available languages come from a one-row declarative catalog**, not from XAML items. Adding
   Polish is then **one row plus §8's foundation** — no window change, no VM change, no binding change,
   which is precisely the outcome the user asked for. Same pattern as `CommandCatalog` /
   `LanguageConstructCatalog` (§5.4).
4. **An unknown stored code falls back to English** rather than throwing or blanking the box — the case
   that arises when a user opens a downgraded build after Polish ships, or imports a settings file from a
   newer one (§6.3).

⚠ **The one thing that must not happen: no partial localization machinery.** It is tempting to "prepare"
by introducing a `CultureInfo`, a resource lookup for a handful of strings, or a `{app:L}` extension used
in one window. That would leave the app with two string mechanisms and 1 815 `const`s still inlined —
the worst of both, and exactly the "parallel implementation" the reuse rules forbid. The catalog and the
stored code are the whole scope; the mechanism is §8, in one piece.

⚠ **Do not offer "follow system language".** No mechanism to honour it, and it would need the same
resolution rules twice.

### 6.3 General → Import / Export Settings

⭐ **RATIFIED (user, 2026-07-29 — supersedes my Q3 recommendation): this is EmberTern's own versioned
export format, not JSON as a public contract. EVERY export is encrypted, whether or not it contains
passwords.** The rationale is the design one and it is the stronger argument: **one uniform format means
the user never chooses a variant, and we can add data to the export later without changing its
behaviour.** My "plain JSON when no credentials" recommendation optimised for inspectability, which is a
developer's convenience, and it would have made every future addition ask "does this change which
variant we write?" — a question that never has to be asked now.

#### 6.3.1 The format — cleartext header, encrypted payload

**⚠ The header must be cleartext and the payload encrypted, and this is not a compromise of the "always
encrypted" rule — it is what makes versioning work at all.** If the whole file were opaque, a future
build could not distinguish *"this is a v1 export, migrate it"* from *"wrong passphrase"* from
*"corrupt"*. It would have to infer the structure after decrypting, which is precisely the guessing the
user asked to eliminate. So the version has to be readable **before** the passphrase is applied.

This is the shape [`SettingsFileContainer`](../../src/EmberTern.Core/Settings/SettingsFileContainer.cs)
already has — `TryParse` reads the header before anything is decrypted — so the export envelope extends
an established pattern rather than inventing one.

| Header field (cleartext) | Job |
|---|---|
⭐ `Magic` — **first bytes of the file** | **Identity.** `EMBERTERN-SETTINGS`. Answers *"is this even our file?"* before any parsing, versioning or passphrase prompt. §6.3.1a |
`ExportFormatVersion` | ⭐ **the machine-readable contract.** Drives migration on import. Authoritative. |
`AppVersion` | ⭐ **diagnostics only** — see the rule in §6.3.2. Never drives logic. |
`EncryptionScheme` | `aes256-passphrase` (§2.4's reserved name) |
`Kdf` + `Iterations` + `Salt` | KDF parameters. **Not secret** — storing them is standard practice and is what lets a future build with different defaults still read an old file. Salt is per-file and random. |

Payload (encrypted): the section list and the sections themselves.

##### 6.3.1a ⭐ The magic — added by the user, 2026-07-29

**`EMBERTERN-SETTINGS`, as the literal first bytes of the file.** Chosen over `ETSET` because the header
is cleartext and therefore *will* be opened in a text editor by someone whose export won't import: a
self-documenting first line turns "I can't open this file" into a self-answering situation, and costs
~13 bytes.

Three rules make it do its job rather than become a second version field:

1. ⭐ **The magic is IDENTITY and never changes. `ExportFormatVersion` is the CONTRACT and does.** If a
   future format change also bumped the magic, an old file would fail the identity check and be reported
   as *"not an EmberTern settings file"* instead of *"an older export, migrating"* — destroying exactly
   the diagnostic value the magic was added for. **Never version the magic.** (Same shape as the
   `AppVersion` rule in §6.3.2: one field per job.)
2. ⭐ **Check it before loading the file into memory.** Read the first bytes from the stream, not after a
   `ReadAllText` — otherwise accidentally picking a 2 GB file costs a full read before rejection. This is
   the practical half of the user's rationale and it is easy to lose by writing the obvious code.
3. ⭐ **A binary file must yield "not an EmberTern settings file", never an unhandled decoding
   exception.** A ZIP begins `PK\x03\x04` and a PDF `%PDF-`; read as text these produce replacement
   characters or throw depending on the path taken. Compare **bytes**, and guard the read — the whole
   point is a clean rejection message, so a crash here would be the one failure mode worse than the
   unclear message it replaced.

⚠ **`settings.dat` does not get a magic, and that is not an inconsistency.** The export is a file *the
user picks in a file dialog*, so mistaken identity is a real, frequent case. `settings.dat` lives at a
path we control and is never chosen by hand, so a magic would buy nothing — and adding one would change
that file's format, forcing a container-version bump and a legacy-read path for zero user-visible gain.

**Mode: AES-256-GCM**, i.e. authenticated encryption — not CBC. The reason is a behaviour, not a
preference: GCM's authentication tag makes a **wrong passphrase fail cleanly as an authentication
failure**, distinguishable from a genuinely damaged file. Without it, a wrong passphrase yields garbage
that then fails JSON parsing, and the user is told "corrupt file" when the truth is "wrong passphrase".
That is the same distinction `SettingsLoadStatus` draws between `Corrupt` and `Unreadable`, and for the
same reason — **the two have different prognoses and the user's next action differs.**

⚠ **File extension is EmberTern's own** (e.g. `.etsettings`), never `.json`. The extension is part of
the "this is our artifact, not a public document" decision; a `.json` file invites hand-editing of a
file that is neither editable nor readable.

⚠ **The section list lives in the encrypted payload, not the header.** It is tempting to put it in the
header so the import UI can preview contents before asking for a passphrase, but a cleartext
*"contains: Connections, Passwords"* advertises what is worth attacking. The flow is therefore: enter
passphrase → **then** see contents → choose sections → import. That is also better UX, since a choice
you cannot yet act on is not worth presenting.

⚠ **The passphrase is unrecoverable, and the UI must say so at the point of entry.** A passphrase-derived
key means a forgotten passphrase makes the file permanently unreadable — there is no reset and no back
door, by design. This is a consequence of the ratified decision, not an argument against it; it just has
to be stated where the user types it rather than discovered later.

#### 6.3.2 ⭐ Two version numbers, two jobs — never confuse them

**`ExportFormatVersion` governs migration. `AppVersion` is diagnostics and must never be branched on.**

Keying behaviour to a version *string* is the shape gotcha **#289** already burned this project on (a
guard keyed to a value's current contents). `AppVersion` exists so a support conversation or a bug
report can say *"this file came from 0.5.0"* — the moment any code does `if (AppVersion < …)`, the format
has two competing contracts and the weaker one wins by accident.

`AppVersion` is written from [`AppInfo`](../../src/EmberTern.App/AppInfo.cs), never as a literal —
CLAUDE.md's ⛔ *"never write a version number in code"* rule, with its two `AppInfoTests` guards, applies
here exactly as it does to the About window.

**⚠ There is a third version in play and it is not redundant.** The payload still carries
`ApplicationSettings.SchemaVersion` (§6.3.3's table). They version different things and move
independently: `ExportFormatVersion` governs *the envelope and which sections exist*;
`SchemaVersion` governs *the shape of `ApplicationSettings` itself* and is already handled by
[`MigrateToCurrentVersion`](../../src/EmberTern.Core/Settings/ApplicationSettingsStore.cs:432)'s
existing ladder. **Do not collapse them into one** — a future session will be tempted, and it would
couple "we added a section to the export" to "the settings shape changed", forcing a schema bump that
[trips downgrade protection and makes older builds refuse the whole `settings.dat`](../../src/EmberTern.Core/Settings/ApplicationSettingsStore.cs:185).

#### 6.3.3 Migration and refusal — reuse the three-axis protection that exists

Import applies the same discipline `LoadWithStatus` already applies to `settings.dat`:

- **Older `ExportFormatVersion` → migrate explicitly**, through a stepwise ladder where each step
  upgrades by exactly one version and is independent of the others — the pattern
  `MigrateToCurrentVersion` documents and this sprint should copy rather than reinvent.
- **Newer → refuse**, with a message naming the version. Same downgrade protection, same reason: we
  cannot understand fields we have never seen, and a partial import is worse than none (rule #11).
- **Unknown `EncryptionScheme` → refuse.** `ResolveProtector` already returns null for this case.
- **Authentication failure → "wrong passphrase"**, not "corrupt" (§6.3.1).

##### ⭐ The order of these checks is itself a design decision

Each check must run before the one below it, and the sequence is what makes every message honest and
distinct — the same "classify by cause, because the causes have different prognoses" discipline
`LoadWithStatus` applies to `settings.dat`:

| # | Check | Failure message |
|---|---|---|
1 | `Magic` (from the stream, before loading — §6.3.1a) | *"This is not an EmberTern settings file."* |
2 | `ExportFormatVersion` newer than supported | *"Written by a newer EmberTern build (format vN)."* |
3 | `ExportFormatVersion` older | *(no failure — migrate)* |
4 | `EncryptionScheme` unknown | *"Unsupported encryption scheme."* |
5 | **← the passphrase is requested only here** | |
6 | GCM authentication | *"Wrong passphrase."* |

⭐ **The non-obvious win the magic delivers: steps 1–4 all resolve BEFORE the user is asked for a
passphrase.** Without them the flow would prompt for a credential, fail authentication, and report
*"wrong passphrase"* — when the real answer was *"you picked a PDF"* or *"this file is from a newer
build"*. **Never ask for a credential that cannot possibly work**; a passphrase prompt is an implicit
claim that the file is readable given the right one.

⚠ Corollary for whoever implements 5b: the passphrase dialog must not be the *entry point* to import. The
file is validated first, then the passphrase is requested. Wiring it the other way round is the natural
shape if the dialog is built first, and it silently discards every distinct message above.

#### 6.3.4 What travels

The classification the user asked for, per field:

| Content | Export? | Why |
|---|---|---|
`Preferences` (theme, formatter, …) | ✅ **yes** | the definition of a portable user setting |
`GridProfiles` | ✅ yes | column layouts are preference, and grid ids are stable strings |
`Folders` | ✅ yes | the user's own organisation of their connections |
Connections **minus** `Password` and `ClientLibraryPath` | ✅ yes | host/port/db/user/charset/dialect are the profile's substance and are usually identical on a second machine |
`ConnectionProfile.Password` | ⚠ **opt-in only** | see below — decision **Q2** |
`ConnectionProfile.ClientLibraryPath` | ❌ no | a local filesystem path, and only meaningful in Embedded mode |
`WorkspaceState.WindowBounds` | ❌ no | monitor geometry; importing it can place the window off-screen |
`Workspaces` (tabs, SQL text, saved queries) | ⚠ **separate opt-in** | this is *work*, not settings — and it can be large. Decision **Q6** |
`ParameterHistory` | ❌ no | execution history, not settings; keyed to connection ids |
`DebugWatches` | ❌ no | same |
`ImportProfiles` | ⚠ opt-in | genuinely reusable configuration, but embeds **source file paths** — machine-dependent in part |
`SchemaVersion` (the settings shape) | ✅ yes | migrated by the existing ladder; **distinct from `ExportFormatVersion`** — §6.3.2 |

**Passwords.** Inside `settings.dat` a password is plaintext within a DPAPI-encrypted blob, which is safe
because DPAPI is bound to the account. An export is by definition *not* so bound, so exporting passwords
means writing credentials in a form that travels. Ratified (**Q2**): **omit passwords by default; explicit
opt-in checkbox**, whose label states that the file will contain database credentials.

⭐ **The "always encrypted" decision simplified this rule — record the simplification, don't carry the
dead clause.** Q2 originally also said *"never export passwords into an unencrypted file — refuse that
combination"*. With every export encrypted (§6.3), **that combination cannot arise**, so the refusal is
unreachable and must not be implemented: an unreachable guard reads as a real safety net to the next
person, who then reasons about a state the code cannot enter. The passphrase requirement is no longer
something the password checkbox *adds* — it is unconditional — so the checkbox is now purely a content
decision. This is what "one uniform format" buys beyond consistency: one fewer state to reason about.

**Import must be non-destructive and must refuse rather than guess** (rule #11): show what the file
contains, let the user choose which sections to take, merge connections by `Id` (a re-import updates
rather than duplicating), and preserve the current `settings.dat` as `.pre-import-<stamp>` — reusing
`SaveOverUnreadableFile`'s existing "rename aside, never delete" pattern.

**Also here:** a small **"Open settings folder"** button. Cheap, and now genuinely useful — the
hardening sprint made `settings.dat.bak` and `settings.dat.unreadable-<stamp>` real artifacts a user may
need to reach, with no path shown anywhere in the UI.

### 6.4 SQL Formatter → Keywords / Identifiers casing

**Exactly two settings, as asked.** Both default to `Lower`, so the shipped default behaviour is
byte-identical to today.

**Implementation shape**, driven by §2.2:

1. A pure Core `FormatterStyle` record (`KeywordCase`, `IdentifierCase`) with a `Default` instance whose
   values reproduce today's output.
2. `SqlFormatter.Format(sql, FormatterStyle? style = null)` — **a parameter with a default, never an
   ambient read** (§5.3), so every existing call site and every §0 differential test compiles and
   behaves unchanged.
3. **One casing decision point.** All nine sites in §2.2(a) collapse into a single method that takes the
   token's classification and the style. This is the etap's real work and its real benefit: the
   formatter currently has no such point at all.
4. **The keyword/identifier split** comes from `FirebirdSyntax.IsKeyword` — the catalog the lexer and
   the XSHD drift-guard already share — applied where `FKind` is assigned in `Flatten`. No second
   keyword list (the mistake the language-expansion sprint's §9.1 one-owner rule exists to prevent).
5. **Quoted identifiers stay verbatim** — apply the setting inside the existing `QuotedIdent` guard,
   never around it (§2.2e).
6. **Correct the §0 net's comment** at `SqlFormatter.cs:2011` in the same etap (§2.2d). The code is
   safe; the comment would license someone to break it.

**Verification is not optional here.** The formatter is a §0 / rule-#11 surface. The etap's DoD must
include: the existing differential round-trip and idempotency suites green **under both casing
settings**, and a test proving the default settings produce output identical to today's.

**Deliberately not offered** (the user's "don't design a dozen options" instruction, and I agree):
`MaxLineWidth`, `PsqlIndentSize`, indent style, comma placement, keyword alignment. `MaxLineWidth` is
the only one with a real argument (ultrawide monitors) and it can arrive alone if asked for. Named
formatter *profiles* are §8.

⚠ **Open question Q1: does identifier casing also govern generated DDL?**
[`DdlGenerator.PresentIdentifier`](../../src/EmberTern.Core/Metadata/DdlGenerator.cs:951) uppercases
regular identifiers and uppercases type names, and CLAUDE.md records that as *deliberately distinct*
from `SqlFormatter`. My recommendation: **no** — `SqlFormatter` reformats *the user's text*,
`DdlGenerator` composes *new DDL for the database*, and folding them would let a Lower setting emit
`create procedure foo` into the catalog. But a user who sets "Identifiers: Lower" may reasonably expect
it everywhere, so this is the user's call.

---

## 7. My own proposals, with the justification the user asked for

Each answers: *why worth it · global? · in Settings Center? · defer or Premium?*

### 7.1 Editor / monospace font family + size — **prerequisite now, setting later**
*Why:* the app already renders monospace text in two different typefaces (§2.7), and font size is the
single most-requested accessibility setting in any code editor. *Global:* yes. *Settings Center:* yes.
*Defer:* **the setting, yes — the consolidation, no.** Typography is explicitly the backlogged app-wide
UX sprint's territory, and a font-family setting with four divergent hard-coded strings behind it would
configure some surfaces and not others. **Recommendation: this sprint collapses the four strings into
one theme token (a bug fix, and small); the UX sprint adds the setting on top.**

### 7.2 Execution row limits — **yes, ship it (highest-value optional setting)**
*Why:* `PreviewLimit = 5000` is a taste-and-hardware trade-off asserted for everyone, and
`ExecutionDefaults`' own comment says moving it to user settings is *"a one-line change at the call
site"* — verified true, because the limits already travel as values on `ExecutionRequest` (§3.3).
*Global:* yes. *Settings Center:* yes, an `Editor / Execution` page. *Defer:* no.
⚠ Expose `PreviewLimit` and `FullSoftThreshold`; leave **`FullSafetyCeiling`** alone — it is a memory
backstop, not a preference, and a user who raises it to 50 M gets an out-of-memory crash instead of a
truncated grid. Configuring a safety limit defeats it.

### 7.3 Debugger default transaction isolation — **yes**
*Why:* it closes a **recorded** user wish (D4 UX backlog: *"move transaction-isolation config to global
Settings (show only params at launch)"*), which D15.3 answered only by hiding it in an Advanced
disclosure. The launch panel keeps the per-session override; this sets what it opens with.
*Global:* yes. *Settings Center:* yes. *Defer:* no — cheap, and it retires a backlog item.

### 7.4 Grid: default auto-fit columns — **yes, low priority**
*Why:* grid layout persistence is live (§3.1) and its default is a hard-coded `true` in a behavior
class; users who prefer fixed widths must re-fight it on every new grid. *Global:* yes (a default; each
grid still overrides). *Settings Center:* yes, on the Grid page. *Defer:* it is the weakest of the
"yes" items — drop it first if the sprint needs trimming.

### 7.5 Restore workspace on startup (on / off) — **yes**
*Why:* restore is unconditional today with no opt-out, and a stale restored tab set is a real recurring
annoyance. *Global:* yes. *Settings Center:* yes, General. *Defer:* no — one boolean consulted at one
existing call site.
⚠ It must gate **restore**, never **capture**: if it stopped capturing, turning the setting back on
would restore a workspace from whenever it was last disabled. Keep saving, choose whether to read.

### 7.6 Default editor mode (Source / Easy) for Procedure / View / Trigger / Function — **yes, and it fixes a latent oddity**
*Why:* the four `*EasyMode` flags already exist as seeds for newly opened editors (§3.1), but they are
set implicitly by whatever the user last toggled — so opening a procedure in Easy mode because of
something you did to a *different* procedure yesterday looks like a bug. Making them explicit turns an
invisible side effect into a stated preference. *Global:* yes, already are. *Settings Center:* yes.
*Defer:* no — the storage exists; this is only a UI over it.

### 7.7 Table / View data page size — **yes, and it deduplicates a constant**
*Why:* 200 rows, hard-coded **twice** (`TableDetailTabViewModel:25`, `ViewDetailTabViewModel:32`).
*Global:* yes. *Settings Center:* yes, Grid page. *Defer:* no. Small, and it removes a duplicate pair
that can drift.

### 7.8 Editor timing constants (debounce / hover dwell / auto-popup / trigger length) — **NO, and deliberately so**
*Why not:* these four are **tuned values, not preferences.** `HoverDwell = 350 ms` was chosen as part
of the Unified Hover milestone's noise budget; `AutoTriggerMinLength = 3` is load-bearing for the
prefix-first completion philosophy; `ParseDebounce = 300 ms` is balanced against model-rebuild cost. A
user who sets debounce to 0 would experience the editor as broken and would rightly report it as our
bug. Exposing a tuning constant transfers a design decision to someone with no information to make it.
**Recommendation: not now, not Premium — never, unless a specific complaint arrives that a specific
value causes.**

### 7.9 Suppressible confirmation dialogs ("don't ask again") — **NO**
*Why not:* the confirmations guard **rule #11** — discard uncompiled work, delete an object, roll back
a transaction, close a dirty tab. They exist because a group recompile once destroyed input-parameter
defaults (gotcha #175). A "don't ask again" checkbox is a setting whose entire function is to disarm the
paramount law, and it will be ticked once, in a hurry, and remembered by no one.
**This is the one thing on this list I would push back on if asked for.**

### 7.10 Import batch size / commit interval defaults — **NO, for now**
*Why not:* the values (500 / 10 000) are **measured optima** from etap I0, `ImportConfiguration`
already carries them per-profile so any single import can override, and Data Import is closed under a
standing directive not to return to it for anything but a functional defect. A global default would add
a third place the same number lives. *Revisit* only if a real workload complains.

### 7.11 Later / Premium

⚠ **This is a record of what was deliberately NOT built, and why — not a roadmap** (§9.1). Nothing here
is scheduled, and an item does not become in-scope by appearing on this list. The document title says
"SQL Formatter **Profiles**" because that was the sprint's working name; profiles themselves are the
first entry below and are **out of scope**.

- **Named formatter profiles** (`Lowercase-all` / `Uppercase keywords` / a custom set) — the natural
  growth of §6.4 once two settings prove insufficient. The `ImportProfile` pattern is the model. Not now.
- **Shortcut editor** — a Settings category writing user overrides that `CommandCatalog` merges. The
  registry is already the single source; this is the keyboard sprint's stated end state. A separate stage.
- **Per-connection overrides of global preferences** — plausible for row limits (a slow remote DB), but
  no one has asked and it doubles the resolution rules for every setting. Not now.
- **Settings sync / cloud** — a Premium-shaped feature. `aes256-passphrase` (§6.3) is the foundation it
  would build on, which is another reason to implement the scheme properly rather than ad hoc.

---

## 8. Localization — its own milestone; the Language row does **not** shorten it

⚠ **Read this together with §6.2.** Etap 3 ships the Language row, its stored ISO code and its catalog —
and that is genuinely all it ships. **The row existing does not move this milestone forward by one
line.** Anyone estimating "add Polish" from the presence of a working dropdown will be wrong by orders
of magnitude, which is exactly why the scope is written down here before the row exists rather than
after:

1. Replace 1 815 `const` + 39 `static readonly` members with an indexed, notifying lookup.
2. Replace `{x:Static app:UiStrings.X}` in 42 `.axaml` files with a localization markup extension
   (`{app:L Key}`) — or accept restart-to-switch and keep `x:Static`.
3. Decide the storage format. ⚠ Architecture rule #6 forbids `AppResources.resx`; that rule was written
   against `.resx` *as a string-organisation habit*, not against localization, so reviving it needs the
   user's explicit decision rather than a quiet exception.
4. Keep the `UiStringsShortcutSourceTests` guard working — it currently keys on `const`-ness to prove a
   gesture is not hand-typed (gotcha #284). **Removing `const` removes the guard's discriminator**, so
   the guard needs a new one *before* the migration, or the keyboard sprint's central invariant silently
   stops being enforced.

Point 4 is the kind of thing that is cheap to handle deliberately and expensive to discover late.

---

## 9. ⭐ RATIFIED DECISIONS (user, 2026-07-29) — do not re-litigate

The design was reviewed and accepted. One recommendation was overridden (**Q4**); the rest were
accepted as recommended. This table is the authority — where §§2–8 reasoned toward a different answer,
the ratified answer wins and the section has been amended in place.

| # | Question | ⭐ Ratified |
|---|---|---|
**Q1** | Does `Identifiers: Upper/Lower` also govern `DdlGenerator` (Easy-mode generated DDL, always UPPER today)? | **No.** The formatter reformats the user's text; `DdlGenerator` composes new DDL for the catalog. They stay distinct, as CLAUDE.md already records. |
**Q2** | Does Export include connection **passwords**? | **Omit by default; explicit opt-in checkbox.** ⚠ Amended by **Q3**: the "refuse an unencrypted export containing credentials" clause is now **unreachable and must not be implemented** (§6.3.4). |
**Q3** | Export format when no secrets are included | ⭐ **REVISED — overrides my recommendation. EVERY export is encrypted, always**, and the file is **EmberTern's own versioned format**, not JSON as a public contract. Rationale: one uniform format, no variant for the user to choose, and future data can be added without changing behaviour. Cleartext **header** (format version + app version + scheme + KDF params) over an encrypted **payload**; AES-256-GCM; own file extension. §6.3. |
**Q4** | ⚠ Ship the single-item **Language** row? | ⭐ **YES — overrides my recommendation.** Ships now, enabled, English only. Rationale: completeness of the Settings Center, and **not rebuilding the window layout when Polish lands**. As-designed in §6.2; storage live from day one; Polish remains §8's own milestone. |
**Q5** | Theme persistence means the code-behind toggle writes to a store — acceptable under architecture rule #1? | **Yes.** Rule #1's concern is no Avalonia types in VMs; a `string` preference respects it. The toggle stays in code-behind. |
**Q6** | Does Export offer **Workspaces** (tabs, SQL text, saved queries)? | **Yes — separate opt-in, off by default.** It is work rather than settings, and it can be large. |
**Q7** | Settings Center as a **window** rather than a workspace tab? | **Window** — §5.1. |
**Q8** | Apply-on-change, no OK/Cancel? | **Yes** — §5.5, with the refusal banner (§2.5). |
**Q9** | Which §7 proposals are in scope? | **In: 7.2** (row limits, excl. `FullSafetyCeiling`), **7.3** (debugger isolation default), **7.5** (workspace restore), **7.6** (Source/Easy default), **7.7** (page size). **Prerequisite only: 7.1** (font consolidation — the *setting* stays with the UX sprint). **Trim first: 7.4.** **Out: 7.8, 7.9, 7.10.** |
**Q10** | Format identity — how does the importer know it is our file? | ⭐ **ADDED BY THE USER.** A constant **magic**, `EMBERTERN-SETTINGS`, as the literal first bytes. Rejects a mistakenly-picked ZIP/PDF before parsing, versioning or any passphrase prompt. **Never versioned** (identity ≠ contract), checked from the stream before loading, byte-compared. §6.3.1a + the ordered checks in §6.3.3. |
**Q11** | Is etap 5 one etap or two? | ⭐ **Two, as the user split it: 5a** = container, encryption, versioning, migrations, tests (Core, no UI); **5b** = export/import UI, section selection, passphrase. §10. |
**Q12** | Where do defaults live, and who validates? | ⭐ **ADDED BY THE USER, binding on etap 2.** **`Preferences` is a self-sufficient contract** — every property valid from its own initializer, so `new Preferences()` is always usable with no initialization. **`PreferencesStore` only validates and normalizes what it read from the file** (unknown value → the model's default) and supplies no defaults of its own. Validation applies to `Language` from day one despite having no consumer. §5.2.1. |

### 9.1 ⭐ Standing directive — no features "for the future"

**User, on accepting this design:** nothing is added because it might be wanted later. No *Check for
updates*, no telemetry, no experimental-features toggle, no diagnostics switches — **and no additional
formatter options beyond the two in §6.4.**

**Language (§6.2) is the sole exception, and only because Polish is already planned.** That is what
makes it an exception rather than a precedent: the test is *"is the next step scheduled?"*, not *"would
this be useful someday?"*

Consequences, so this is enforceable rather than aspirational:

- §7.11's *Later / Premium* list is a record of **what was deliberately not built and why**. It is not a
  roadmap, and an item does not become in-scope by being on it.
- The §7 "out" items (7.8 timing constants, 7.9 suppressible confirmations, 7.10 import defaults) stay
  out. **7.9 in particular is a decision, not a gap** — a "don't ask again" checkbox exists only to
  disarm rule #11.
- A settings page must not ship a control whose backing behaviour does not exist. Language is the one
  approved instance, and it is approved *with* live storage and a real catalog, not as a stub.

---

## 10. Proposed etap order

Each etap ends build 0/0, tests green, smoke clean, and committable — and each is one session.

| Etap | Scope | Why here |
|---|---|---|
**1** | *(this document)* audit + design | ✅ **done — accepted 2026-07-29** |
**2** | Core foundation: `Preferences` (incl. `Theme`, `Language`, the two formatter cases) + `PreferencesStore` (8th facade) + the language catalog + defaults contract + tests. **No UI.** | pure Core, additive, no schema bump; everything else depends on it |
**3** | Settings Center window: shell, category list, search, apply-on-change, refusal banner — hosting the **complete General page: Theme + Language**. Fixes §2.1 end to end (persist + read at startup + the `App.axaml` trap). | Theme is the user's clearest gap and proves the surface; Language ships **with it** so the General page is laid out once and never rebuilt (⭐ **Q4**'s whole rationale) |
**4** | SQL Formatter: `FormatterStyle`, the **one** casing decision point, the keyword/identifier split via `FirebirdSyntax.IsKeyword`, the §0 comment correction, differential + idempotency suites green **under both settings**. | the largest single piece of work (§2.2); isolated so its §0 risk is not entangled with UI |
**5a** | ⭐ **Core — the format itself.** Magic + versioned cleartext header, `aes256-passphrase` (AES-256-GCM) protector registered in `ResolveProtector`, KDF params, the migration ladder, the ordered check sequence (§6.3.3), and tests. **No UI.** | needs etap 2's shape settled to know what it serialises; pure Core and fully testable without a window, which is what makes the split worth making |
**5b** | ⭐ **UI — export/import experience.** The content filter (§6.3.4), section selection, the passphrase flow (§6.3.3's corollary — validate first, prompt second), the non-destructive import with `.pre-import-<stamp>`, "Open settings folder". | the format is settled and provable before any dialog exists |
**6** | The approved §7 settings (**Q9**) — each a scalar on `Preferences` plus one page row. | additive; naturally last, and trimmable without blocking anything |

⭐ **Language moved into etap 3 (from "unplanned") as a direct consequence of Q4.** Putting it in etap 6
would have laid out the General page twice — the exact churn the decision exists to avoid. Its Core
storage and catalog therefore land in **etap 2**, alongside `Theme`.

**Font-family consolidation (§7.1)** is small and independent — fold it into whichever etap has room, or
run it standalone. Note it is a *consolidation*, not a setting (**Q9**).

**Polish (§8)** is explicitly **not** in this plan. Etap 3 ships the row; §8 is its own milestone.

---

## 11. Explicitly out of scope

- **Anything speculative** — see the standing directive in **§9.1**. No update check, no telemetry, no
  experimental toggles, no formatter options beyond §6.4's two. Language is the one scheduled exception.
- **Polish / the localization mechanism** — §8, its own milestone. Etap 3 ships the row only.

- **App-wide control density / typography** — the backlogged UX sprint's, by standing instruction, even
  though Settings Center is entirely made of the affected controls (§5.1).
- **The platform-wide charset audit** — deferred by decision; Settings Center must not become the place
  it gets half-solved (§3.1).
- **Surfacing `Data`/`MetadataTransactionProfile`** — enforced away by `TransactionService`; exposing
  them re-creates the lie the hardening sprint removed (§3.1).
- **Any Data Import change** — standing directive (§7.10).
- **The full-suite hang** (#94/#226/#261) — its own infrastructure task.
