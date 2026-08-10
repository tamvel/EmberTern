# Localization — Core / Firebird stage

The stage after **Localization / App** (which closed 2026-08-09 with the mechanism built and the whole App
layer migrated). Its subject is the messages that live **outside** App: the ones Core and Firebird produce and
the App merely displays.

Architecture: [../design/localization.md](../design/localization.md). Entry point of the stage:
[../design/localization-app-stage-handover.md](../design/localization-app-stage-handover.md).

Etaps are numbered **C0** (audit) and **C1, C2, …** (one producer module each).

---

## C0 — Audit (2026-08-10)

⛔ No code. The etap's product is a measurement and a classification.

### The headline: the inherited number did not survive measurement

The handover carried an inventory of *"≈280 user-visible messages"* across ten places. Three of its ten rows
described something other than what they claimed, and each time it changed the **shape** of the work rather
than only its size:

| Row | Claimed | Measured | Why |
|---|---|---|---|
| `CharsetCatalog` — "charset descriptions" | 8 | **0** | The file contains no description at all. The eight items are Firebird charset **names** (`"UTF8"`, `"WIN1250"`) — class **D**, never translated |
| `Core/Import/**` — "import row errors" | ~20 | **0** | ⭐ The module was already built correctly: `ImportRowError` carries an `ImportErrorKind` **enum**, and `ImportDiagnosticCode`'s own comment reads *"as a code — **never a message**"*. Every prose literal in the module is a `throw new ArgumentException` — a developer contract |
| `FirebirdDiagnostics` | 24 | **0** class A | The class states of itself *"**No UI**; this is infrastructure for manual verification"*. Every consumer is `AppendDebugLog` → `%TEMP%\EmberTern-debug.log`. Class **E** |

**Corrected scope: ~170–190 messages of class A**, not 280.

⚠ One nuance inside the third row, worth keeping: `FirebirdDiagnostics.DecodeIsolationMode` (verbose) stays in
the log, while `FirebirdSessionReader.ShortIsolation` is a **separate** decoder that does reach the grid. The
split is correct; a scan that had grouped them by name would have mis-classified both.

### Four constraints that decide the shape of the migration

1. ⛔ **`Diagnostic` is a `record struct` whose value equality the panel depends on.**
   `DiagnosticsPanelViewModel.cs:106` compares findings to skip a rebuild. `LocalizableMessage` is a record
   whose `IReadOnlyList<object?>` compares by **reference**, so putting one inside `Diagnostic` would make two
   structurally identical diagnostics unequal — the panel would rebuild on every keystroke, silently.
2. ⭐⭐ **`ExecutionSummary` / `ExecutionActivity` build sentences by CONCATENATION**
   (`string.Format("{0} {1} {2}", n, n == 1 ? "row" : "rows", verb)`). Keying `"row"`, `"rows"` and
   `"inserted"` separately produces nonsense in an inflecting language, and Polish has **three** plural forms
   for this shape. The key must cover the whole sentence, and the plural category must be chosen by App.
3. **`ex.Message` is a catch-all channel to the screen.** `DataImportTabViewModel` renders it in eight places.
   Measured consequence: the two `EmberTern.Office` providers throw an `InvalidDataException` carrying
   deliberate, guidance-bearing user text — class **A** in the shape of a `throw`. So "throw ⇒ class B" is
   false in both directions (133 throws, most of them B, a handful A).
4. **The self-arming Core-key guard scanned only the Core assembly**, so a key declared in
   `EmberTern.Firebird` would never have been checked. (Fixed pre-emptively in C1.)

### A second mechanism already existed

`ExportUnavailableReason`, `SqlLiteralWriter`, `ImportDiagnosticCode`, `ImportErrorKind` and `ImportReadiness`
all carry an **enum code** that App maps to `UiStrings`, with comments saying so explicitly. That predates
D‑3 and does the same job.

🔒 **Ratified rule (user, on accepting C0):** an **enum** when the set is closed and finite and App may want to
branch on the kind; **`MessageKey` + arguments** when the message carries dynamic data or is purely
presentational. ⛔ Existing correct enums are **not** migrated onto `MessageKey`.

### Ratified migration order

`SessionHealthAnalyzer` → `QuickInfoEngine` → `FirebirdConnectionService` → `Settings/Export` →
`DiagnosticsEngine` → `ExecutionSummary`/`ExecutionActivity` → `Office ×2` → and only then a decision about
**Performance**, which stays closed.

---

## C1 — `SessionHealthAnalyzer` (2026-08-10) — accepted

The first Core producer on the D‑3 seam. Build 0/0; suite **8 504** (8 280 + 169 + 55, +5); smoke clean;
`Lab/` untouched.

### What migrated, and the boundary that did not

`SessionHealthFinding`'s five text members became `LocalizableMessage` (`Impact` nullable), fed by **16 keys**
in the new `public static class SessionHealthMessages`. Data — the OAT lag, the transaction id, the formatted
age, the isolation label — travels as **arguments**.

⛔ **`SessionHealthVerdict.Headline` deliberately stayed a `string`.** Its two forms are chosen by a COUNT, so
migrating it as it stands would put English's two-way singular/plural split into the catalog as if it were
universal. 🔒 User's decision on accepting C1: **do not design a pluralization mechanism for two messages** —
collect the real set of cases from the later modules first, then design one mechanism for all of them. The
reason is recorded in the code, at the type, together with a ⛔ against "finishing the job" with two keys.

### ⭐⭐ Zero text change — proved, not declared

Every English literal from `git show HEAD` was compared against the resource values. The only differences are
exactly two classes, both expected: the five verdict headlines (not migrated), and three
interpolation→placeholder reshapes (`Age {FormatAge(a)}` → `Age {0}`,
`Tx {holder.TransactionId} · {IsolationLabel(holder)}` → `Tx {0} · {1}`,
`OAT lag {lag:N0} · OST {…}` → `OAT lag {0:N0} · OST {1:N0}`) where **the literal text around the placeholder
is unchanged to the character**. No sentence changed.

### 🔒 Accepted consequence: numbers now follow the reader's culture

`Impact` formatted its count under `InvariantCulture`; routed through `Loc.Format` it formats under
`CurrentCulture`, which that method's own doc declares as the convention (*words follow the language, numbers
follow the machine*). Measured in a test run: **`48,102` → `48 102`** on a Polish machine.

🔒 **Ratified as EXPECTED BEHAVIOUR, not a regression** (user, on accepting C1). ⛔ Do not "fix" it back to
invariant grouping. Every later module routed through `Loc.Format` inherits the same change; gotcha **#354**
carries the general shape.

### ⭐ A pre-existing gap the migration exposed

The language-change chain is `Loc.LanguageChanged` → `MainWindowViewModel` → `tab.RaiseAllPropertiesChanged()`
— and it **stopped at the tab**. Each tab kind hangs its real content off a separate view model
(`SessionManager`, `Debugger`, …) whose bindings are on the *child* object, so a child holding text it resolved
once was never refreshed. `GradeText` and `GapStatusText` — both plain `UiStrings`, both migrated in the App
stage — were already frozen in the startup language **before this etap**.

Fixed by forwarding from `RaiseAllPropertiesChanged`, in the shape of the existing per-kind family
(`UnsavedWork` / `SavableEditor` / `ResolveCommand`). ⚠ It grows one line per module. Gotcha **#353**.

⭐ The warning cards are **rebuilt** rather than notified, because their evidence rows are bound with
`Text="{Binding}"` — the bound object *is* the string, so there is no property on it to re-read.

### The guard that fired, and why splitting it is not weakening it

`EveryLocalizedMember_MatchesItsEnglishEntry` required a `UiStrings` property for **every** catalog key. A Core
key contains dots and therefore cannot be a C# identifier, so no such member can exist — the guard's premise
(*"the member name IS the key"*) was true while the catalog had one owner and stopped being true here.

The catalog is now **partitioned by owner, by reflection** (never by the dot convention — that would be a
second source of truth), and **both** partitions keep orphan protection in both directions:

| Partition | Assertion |
|---|---|
| App | must have a `UiStrings` property *(unchanged)* |
| Core | must have an English entry *(existing)* **+ ⭐ new: an entry nobody declares is an orphan** |

⭐ Planting a renamed key fired **three** guards, and the third is the informative one: a misspelled Core key
falls into the App partition and is caught there too — so there is **no gap between the halves**.

### Verification

- Build 0/0 · three partitions green (8 280 + 169 + 55) · smoke clean · `Lab/` untouched.
- **Plant A** (rename a Core key in the resx) → `EveryCoreMessageKey_HasAnEnglishEntry`,
  `EveryCoreShapedEntry_IsDeclaredByCore` and `EveryLocalizedMember_MatchesItsEnglishEntry` all fired.
- **Plant B** (cache the resolved text in a field — the natural-looking "optimisation") →
  `AWarningCard_FollowsALanguageChange` fired.
- ⚠ `SettingsLoadHealthTests.ConcurrentSaves_NeverLeaveSettingsUnreadable` went red **once** in the first main
  partition run: the known `Parallel.For` flake recorded twice already in CLAUDE.md. Three solo runs and a
  repeated partition run were green. ⛔ Not claimed fixed and not claimed related.

### Tests

Four new in `SessionHealthLocalizationTests` (headless collection — it swaps `Loc`'s global catalog), one new
guard in `LocalizationMechanismTests`, one existing assertion changed.

⭐ The changed assertion is **stronger**: `Assert.Contains("48,102", gc.Impact)` became an assertion on the
**datum** (`Arguments[0] == 48102L` plus the key). It cannot pass for a value that merely formats to a
similar-looking string, and it does not fail on a machine whose number separator differs.

⚠ The tests assert **structure and meaning, never a list of English sentences** — a transcribed sentence list
would be a second copy of the catalog, red on a typo fix and green while the words were frozen (gotcha #333).

### ⚠ Cost carried forward

The headless partition filter grew a **13th** name. Every migrated module whose test touches
`Loc.UseCatalogForVerification` must join `HeadlessCollection` **and** that filter, which is a hand-maintained
list of names — it goes stale exactly as silently as a counter in prose, and the symptom of forgetting is a
rare, misleading cross-test divergence rather than a red test in the same run.

---

## C2 — `QuickInfoEngine` (2026-08-10) — accepted

The second Core producer. Build 0/0; suite **8 509** (8 280 + 174 + 55, +5); smoke clean; `Lab/` untouched.

### The migration is a SPLIT, not a sweep

A Quick Info fact is a `label : value` pair whose two halves have **different owners**, and C2's whole content
is making that structural:

| Half | Owner | Type after C2 |
|---|---|---|
| **Label** — `Nullability`, `Fires`, `Primary key` | EmberTern | `MessageKey` (18 keys in `QuickInfoMessages`) |
| **Value** — `NOT NULL`, `PRIMARY KEY`, `BEFORE INSERT OR UPDATE`, a domain, a type, a count | Firebird | stays `string`, verbatim |

⭐ The reason the value stays is not "out of scope": it is the vocabulary the user reads in every other
Firebird tool, and a card that renamed it would **disagree with the DDL it describes**.

⭐⭐ **Zero text change, proved and cleaner than C1's:** the 18 old label literals and the 18 new resource
values are **byte-identical, zero diff** — with none of C1's placeholder reshaping, because a label carries no
data.

### ⭐⭐ The finding: a type change that a string concatenation swallowed

`QuickInfoView` rendered the label as `new Run(fact.Label + "  ")`. When `Label` changed from `string` to
`MessageKey`, **that line kept compiling** — a record struct in a concatenation resolves through
`ToString()` — and would have put `QuickInfo.Fact.Table` on the hover card. Build green, every other test
green.

⚠ **None of the key-based guards can see it**: they call `Loc.Text` themselves, so they test the catalog, not
the surface. The guard that catches it had to read the **realized control**
(`TheRenderedCard_ShowsResolvedLabels_NeverRawKeys`), and planting the old line back proves it: build
**0 errors**, four of five tests still green, that one red. Gotcha **#355**.

### ⭐ No live-switching work was needed — and that was measured, not assumed

Unlike SessionHealth's cards, which live in a collection, a Quick Info card is **built fresh on every hover and
every completion-row selection** (`SqlCompletionController` builds it lazily; its own comment says *"always
matches the current theme"*). So resolving the label at build time is already live. The tests still pin it,
because "it happens to be rebuilt" is a property that a future caching optimisation would quietly remove.

### ⛔ Deliberately NOT migrated, and this is the open question of C2

**Fact VALUES that are EmberTern's own words rather than Firebird's** — measured, not estimated:

| Value | Sites | Note |
|---|---|---|
| `KindLabel(...)` — 20 object-kind names | 3 | ⭐⭐ **a FIFTH copy of a vocabulary the App stage already consolidated**: `QuickInfoView.KindLabel` maps `SymbolKind` → `UiStrings.ObjectKind*` and is already localized |
| `"Variable"`, `"Cursor"`, `"Common table expression"` | 3 | the same vocabulary, spelled inline |
| `"Input parameter"` / `"Output parameter"` | 1 | parameter direction |
| `"Active"` / `"Inactive"` | 1 | trigger state |
| `"Identity"` / `"Computed"` | 2 | generated kind |
| `"(derived table)"` | 1 | header, not a fact |

⛔ **The right answer is NOT to declare kind keys in Core** — that would be a second copy of words the App
already owns, which is exactly the duplication the App stage removed. The shape that fits is for Core to stop
producing the WORD and hand up the `SymbolKind` as data, which the ratified enum-vs-`MessageKey` rule points
at anyway (a closed enum the App already branches on). ⚠ That changes `QuickInfoFact`'s shape, so it is a
decision, not a tidy-up — recorded here, not taken.

⚠ Consequence while it stands: a Polish reader sees *"Rodzaj: Table"* — the label translated, the kind name
English, **while the metadata tree calls the same thing "Tabela"**. That inconsistency is the cost of the
current boundary and is the argument for resolving it.

### ⛔ Also not migrated: the plural value

`Primary key` → `"1 column"` / `"N columns"`. Its LABEL migrated; its VALUE waits for the shared plural
mechanism, exactly like `SessionHealthVerdict.Headline`. **Running total of plural cases: 5.**

---

## C3 — `FirebirdConnectionService` (2026-08-10) — accepted

The third producer, the first in the **`EmberTern.Firebird`** assembly, and the textbook case for D‑3's
boundary. Build 0/0; suite **8 518** (8 280 + 183 + 55, +9); smoke clean; `Lab/` untouched.

### ⭐⭐ The boundary in one line: our sentence is the KEY, the server's sentence is an ARGUMENT

`Firebird.Connection.Failed` resolves to *"Could not connect to {0}: {1}"* where **`{1}` is whatever Firebird
said, verbatim**. That is what keeps the ratified rule without a judgement call at each site — the wrapper is
ours and is localizable, the engine's words are data and pass through untouched, in any language.

Four keys: `Failed` · `SrpAuthentication` · `UnsupportedServer` · `UnsupportedServerUnknownVersion`.

⚠ The last one is a **separate key rather than the word "unknown" as an argument**, following C1's ratified
shape: substituting a NOUN into a sentence works in English and breaks in a language that inflects, because
the argument cannot know which case the sentence needs.

### ⭐ The exception carries BOTH forms, and that is a deliberate asymmetry

`ConnectionFailedException` gained `LocalizableMessage Localized` **beside** its English `Message`:

| Member | For |
|---|---|
| `Localized` | the three App connection surfaces, resolved at display time |
| `Message` | logs, and any catch-all nobody enumerated |

⭐ Putting a KEY in `Message` would show a raw identifier to whoever hit an unmigrated path; leaving English
there means such a path degrades to **exactly today's behaviour, never worse**. ⚠ The duplication is real and
is **guarded rather than tolerated** — `TheEnglishFallback_SaysExactlyWhatTheLocalizedFormResolvesTo` requires
the two to agree in English, so editing the resource entry alone cannot leave the log speaking an older
wording than the screen. Planted and confirmed red.

⭐⭐ **That guard is also the zero-text-change proof, and it is stronger than a textual diff:** it compares the
localized form against the **untouched** `MapErrorMessage`/`UnsupportedServerMessage`, at runtime, for both
branches and three version cases. The pre-existing `FirebirdConnectionServiceTests` exact-text assertions stay
green unchanged.

### ⛔⛔ `Legacy_Auth` recognition still reads FIREBIRD's text — pinned under a language switch

The refusal arrives with **no SQLSTATE and no GDS code** (measured earlier), so its message text is the only
signal there is. But that text is the engine's, and the engine does not speak the user's language.

⚠ The failure mode this guards against is specific and silent: rewiring the match to a *resolved* EmberTern
string would compare English to a translation and **stop firing the day a second language ships** — invisible
in English, i.e. invisible today. So the assertion is made **while the app is in another language**, not
beside it. Planting `SrpAuthenticationMessage.Contains("Legacy_Auth")` in place of `ex.Message.Contains(...)`
compiles with **0 errors** and turns three tests red, two of them C3's.

### ⭐ The C1 guard extension paid off exactly here

C1 widened `DeclaredCoreMessageKeys()` to scan the **Firebird** assembly as well as Core — pre-emptively,
from the C0 audit's §3.4 gap, before there was anything to catch. C3 is the first producer in that assembly,
and planting a renamed key fires all three catalog guards. ⭐ Nothing had to be changed in the guards for C3.

### ⛔ Deliberately untouched

`FirebirdDiagnostics`' output, `LogConnectionAttempt`, the `DDL-IN-USE` dump and the ten
`throw new InvalidOperationException("No active Firebird connection.")` sites — class **E** and class **B**,
none of them reaches a screen. Translating a developer log would make it *harder* to compare against a user's
report, not easier.

---

## C4a — `ApplicationSettingsStore` (2026-08-10)

⚠⚠ **C4 was scoped as "Settings + Settings/Export" and only the FIRST half was delivered.** The settings
STORE is migrated, verified and green; **`Settings/Export` is not started.** The split is recorded here rather
than smoothed over, because the halves are separable and the second one is larger than it looks. Build 0/0;
suite **8 520** (8 280 + 185 + 55); smoke clean; `Lab/` untouched.

### What was migrated

**18 keys** in `SettingsStoreMessages`, in two families that are deliberately NOT merged:

| Family | Answers |
|---|---|
| `Settings.Load.*` (8) | why the file could not be read |
| `Settings.Refuse.*` (8) | why it will not be overwritten |
| `Settings.Write.*` (2) | a failed write, and another instance holding the lock |

⛔ Several pairs describe the same cause and still read differently in English (*"settings.dat could not be
read: …"* vs *"Refusing to overwrite settings.dat: it could not be read (…)"*). Folding them into one key with
a shared prefix would have changed shipped wording **and** conflated two different moments — a read that
failed versus a write that is being prevented.

### ⭐ The C3 dual-form pattern, applied for a measured reason

Each diagnostic exists as English (`LastLoadDiagnostic` / `LastSaveDiagnostic`) **and** as a
`LocalizableMessage` (`LastLoadMessage` / `LastSaveMessage`), recorded through one setter so they cannot be
set apart.

⚠ **The reason is specific to this module and worth stating:** these sentences are the user-facing half of a
**rule #11** surface — a refusal to overwrite a file that may hold the only copy of every connection and
password — and their exact English is asserted by ~20 existing tests. Retyping them would have forced those
assertions to be rewritten as key comparisons, trading a guard that proves *the user is told the right thing*
for one that proves *a key was chosen*.

⭐⭐ **That choice also produced the strongest zero-text-change proof in the stage so far: those ~20 tests are
UNTOUCHED and green.** Nothing had to be edited to keep them passing, which is what "no shipped wording
changed" means.

### ⭐ One guard is both the anti-drift check and the proof

`EveryLocalizedRefusal_RendersExactlyItsEnglishForm` drives **real scenarios** (a file this build cannot
decrypt, an `.etsettings` export copied over `settings.dat`, unparseable content, a future schema) and
requires each localized form to resolve, in English, to exactly the string the untouched producer built.
⚠ Driven through scenarios rather than a table of expected strings, because a table would be a second copy of
the catalog (gotcha #333). Planted a one-word drift in a resource entry → red.

### ⛔ `SettingsLoadResult` was deliberately NOT given the message

It is a `readonly record struct`, so a `LocalizableMessage` member would degrade its value equality to a
reference comparison of the argument list — **the exact trap the C0 audit measured on `Diagnostic`**. The App
reads `ConnectionProfileStore.SettingsMessage` (forwarded from the store) instead; it already has the store in
hand.

### ⭐ Live switching: one surface needed work, one was already correct — and the difference was measured

| Surface | Verdict |
|---|---|
| Settings Center's save-refusal banner | ⭐ **already correct, by ordering**: `Commit()` → `Apply()` raises `Changed` → `Loc.Apply` switches the language → *then* the message is composed. No subscription, no leak |
| MainWindow's settings-health banner | ⛔ **frozen** — a stored `[ObservableProperty]`, so the existing `OnPropertyChanged(string.Empty)` cannot rebuild its VALUE |

The second is #353 again, and the fix is the same shape: keep the parts, re-compose from the one language
hook. ⚠ **Stated limit:** the re-composition is wired through that single hook and is *not* unit-pinned —
`MainWindowViewModel` is the class the headless suite cannot construct without the known hang risk, so a test
for it would trade a real hazard for a small assurance.

### ⛔⛔ What C4b still has to do — measured, so the next session does not re-audit

`Settings/Export`, ~20 messages across three result types (`SettingsImportInspection`, `SettingsImportResult`,
`SettingsImportApplyResult`), all of which reach the import dialog directly. Notes for whoever takes it:

- Its tests assert exact English too (`SettingsExportFormatTests`), so the **same dual-form pattern applies** —
  this was checked, not assumed.
- ⭐ The `Damaged(detail)` helper composes `"This settings export is damaged: " + detail`. Each becomes ONE
  whole-sentence key: a fixed prefix glued to a fragment cannot be translated into a language that inflects,
  and the fragment is not a sentence in any language.
- The applier **forwards** the store's refusals (`CanSave`, `LastSaveDiagnostic`), so C4a's localized twins
  need threading through it — `CanSave(out string, out LocalizableMessage?)` already exists for that.
- ⚠ A `SettingsPortabilityMessages` key file was written and then **removed** when the self-arming guard
  `EveryCoreMessageKey_HasAnEnglishEntry` went red: keys with no resource entry and no producer are both a
  broken build and a component without a consumer (#233). ⭐ The guard catching a half-finished migration is
  the mechanism working — do not re-create the file until its producers are migrated in the same step.

---

## C4b — `Settings/Export` (2026-08-10)

The second half of C4, and with it the settings surface is migrated end to end. **20 keys** in a new
`SettingsExportMessages`, three result types carrying the C3/C4a dual form, and the two refusals the applier
already forwarded from the store now forwarded in **both** forms. Build 0/0; suite **8 531** (8 280 + 196 + 55);
smoke clean, 0 `FATAL`; `Lab/` untouched.

### What migrated

| Producer | Keys | Note |
|---|---|---|
| `SettingsImportReader.Inspect` | 8 | phase one — everything knowable without a passphrase |
| `SettingsImportReader.Open` | 9 | phase two — the passphrase, then the payload |
| `SettingsExportMigration` | 1 | the envelope ladder's refusal |
| `SettingsImportApplier` | 2 | nothing selected · the recovery copy failed |
| ⭐ **the store's two refusals** | **0** | **forwarded, never restated** |

⭐ **The last row is the one worth reading.** `CanSave(out string, out LocalizableMessage?)` and
`LastSaveMessage` — both added in C4a for exactly this — are threaded through, so the import dialog shows the
**store's own sentence from the store's own key**. A second key saying the same thing would have been two
answers to one question, and the two would have drifted the first time either was reworded.

### ⭐ The `Damaged` family: whole sentences, and the English half still COMPOSED

The producer built `"This settings export is damaged: " + detail` at four sites. Each became **one
whole-sentence key** — a fixed prefix glued to a fragment cannot be translated into a language that inflects,
and *"its payload is not valid JSON (…)"* is not a sentence anywhere.

⭐ **But the English half is still built by the same concatenation**, and that is deliberate: it makes the
equality guard a *proof* that each resource value reproduces the shipped concatenation, rather than a sentence
somebody retyped. Retyping it would have been the one way to change the wording invisibly.

### ⚠⚠ The finding: the dual-form equality proof has an unstated precondition — and a plant that did NOT fire is what found it

Going in, the hazard looked obvious. `Loc.Format` formats arguments under `CurrentCulture` (the ratified #354
convention), an existing test drives an iteration count of `2000000000`, and the English half is
invariant-formatted — so on a Polish machine the localized form would group the digits, the equality guard
would go red here and green in an English CI, and C4a's proof had only survived because its numbers were small
version numbers. That story was written down, reported to the user, and **is false.**

⭐ **Planting the numeric argument left all nine guards green.** A bare `{0}` does **not** apply group
separators to an `int` under `pl-PL`; culture governs the decimal separator and the negative sign, not the
grouping of a `G`-formatted integer. The real lever behind #354's `48 102` is the **`:N0` specifier in the
resource value** — `SessionHealth.Evidence.Gap` reads `OAT lag {0:N0} · OST {1:N0} · Next {2:N0}` — not the
culture of a bare substitution.

⚠ **So the precondition is narrower and more interesting than the guess:** the English half is a *literal in
the producer* while the localized half is a *resource value a translator may edit*, and `{0:N0}` on a
nine-digit count is a reasonable thing for a translator to write. That, not the machine's culture, is what can
make the halves diverge.

**What shipped:** every number echoed from a header or payload field travels as an **invariant-formatted
string** (`Echo(int)`), which makes a format specifier inert and the halves identical by construction. ⭐ It is
also the right answer on its own terms — the sentence says the export *declares* a version or a count, so what
belongs in it is a verbatim echo of the field, the same discipline that keeps a raw server message verbatim.
⛔ This is **not** a reversal of #354: that is about a quantity the reader counts, this is about an echo.

⭐⭐ **And the test had to change with the story.** `TheEnglishAndLocalizedForms_AgreeOnAnyCulture` was written
as a proof about numbers and is not one — it stays green either way. It is kept, honestly relabelled as a broad
invariance sweep (it still covers decimal separators and any future culture-sensitive argument type), and the
numeric case is guarded by `EveryArgument_IsAlreadyFormatted_SoNoFormatSpecifierCanChangeIt`, which serves a
deliberately hostile `{0:N0}` template under `pl-PL` and requires every argument to survive verbatim.
**Re-planted against it: exactly one test red, nine green.** Gotcha **#357**.

### ⛔ One key no scenario can reach — with the reason and a PINNED premise

`Settings.Import.NoMigrationStep` lives in the ladder's `default` arm, which the public reader cannot enter
while `OldestSupportedFormatVersion == CurrentFormatVersion` (both 1): a lower version is refused by check 3
before the ladder runs, and there is no higher one. The key is still declared and produced — the sentence
exists in the product and the arm is live code that a format version 2 will reach.

⭐ It is a **named exemption whose premise is asserted**, not excused: `TheOnlyUnreachableKey_IsStillUnreachable`
pins the version equality, so the day version 2 ships that test fails and asks for a scenario. #322 applied —
guard the premise, not the policy. Planting `CurrentFormatVersion = 2` fires that test **and only that test**.

### ⭐ Live switching: correct by ORDERING and by MODALITY — measured, not assumed

Following #346 (*establish whether the state can occur before building for it*):

| Question | Measured answer |
|---|---|
| Can the language change while the import dialog is open? | **No.** The language preference has exactly one writer — Settings Center's Language row — and the dialog is opened with `ShowDialog` over that very window |
| What if the import itself carries another language? | `SettingsPortability.Apply` calls `_preferences.Reload()` (→ `Changed` → `Loc.Apply`) **before returning**, so the message is composed *after* the switch — the same "correct by ordering" as C4a's Settings Center banner |
| The refusal path? | Returns before `Reload()`, so nothing switched and nothing can be stale |

⇒ **No refresh hook was added, and that is a finding rather than an omission.** ⛔ Recorded in place: if this
dialog ever becomes non-modal the reasoning lapses and the message needs recomposing from the language hook.

⭐ What *is* pinned is the half that matters and can be:
`TheDialog_ResolvesCoresVerdictAtDisplayTime_NotWhenItWasProduced` swaps the catalog between two identical
`PickFile` calls and requires the text to follow it. Planting `Message = inspection.Message` (Core's English,
unresolved) fires that test and only that test.

### Verification

**Seven plants, each firing its own guard and no other:**

| Plant | Fires |
|---|---|
| A numeric argument instead of the invariant echo | ⭐ **nothing**, first time round — the finding above; then exactly `EveryArgument_IsAlreadyFormatted…` |
| B one word changed in a resource value | the equality guard + all four culture cases |
| C the `Damaged` prefix stripped from a value | + `TheDamagedMessages_AreWholeSentences…` |
| D two different sentences pointed at one key | + `EveryDeclaredImportKey_IsExercisedOrNamedUnreachable`, naming the orphan |
| E `CurrentFormatVersion = 2` | `TheOnlyUnreachableKey_IsStillUnreachable`, alone |
| F a key renamed, `.resx` untouched | the three catalog-partition guards in `LocalizationMechanismTests` |
| G the dialog showing Core's English unresolved | the App liveness pin, alone |

⭐ **Zero text change is proved the C4a way — by the existing `SettingsExportFormatTests` /
`SettingsImportApplyTests` assertions being UNTOUCHED and green**, including
`Assert.Equal("This is not an EmberTern settings file.", inspection.Message)` and
`Assert.Contains("Refusing to overwrite", result.Message)` (the forwarded store refusal).

⚠ **Known flake, recorded in both directions as the handover asks:**
`SettingsLoadHealthTests.ConcurrentSaves_NeverLeaveSettingsUnreadable` fired in **3 of 6** main-partition runs
this session (`Assert.Empty() Failure: Collection was not empty`, its documented shape) and was **green 3/3
alone**; three other partition runs reached the full 8 280. ⛔ Not claimed fixed and not claimed related: C4b's
footprint is 7 files — `Settings/Export/*`, `LocalizableMessage.cs` (a doc comment), `Strings.resx`, the import
dialog VM and one new test — and **`ApplicationSettingsStore`, `AtomicWrite` and the cross-process file lock,
which that test exercises, are not among them.** ⚠ The rate looks higher than previously noted; worth watching.

### ⛔ Deliberately out of scope, each measured

- **The EXPORT side.** `SettingsExporter` has exactly two message-bearing throws, both `ArgumentException`
  guards, and `SettingsExportDialogViewModel.CanExport` gates on both conditions — so neither is reachable from
  the UI. The failure wrapper the dialog shows (`SettingsExportFailedFormat`) is already localized in App and
  its inner text is the platform's. ⚠ Not "we skipped it": there is nothing there of class A.
- **A second key for the store's refusals** — forwarded instead (above).
- Pluralization · `KindLabel`/`SymbolKind` · Performance · Data Import — untouched, per the standing decisions.

### ⚠ One truth-pass beyond the migration

`LocalizableMessage`'s own doc still read *"the seam has no PRODUCER in Core yet … ⛔ do not start migrating
Core messages onto it early"* — true when written, false since C1. Left alone it would have told the next
author the opposite of what four accepted etaps did, on the very type they would be using. Corrected in place,
pointing at this file for the ratified order. ⭐ Gotcha #284's shape, on a doc comment attached to the seam
itself.

### ⏭ What C5 has to do

**`DiagnosticsEngine` (ET0001–ET0008), ~8 messages.** ⛔⛔ **Do not put a `LocalizableMessage` into
`Diagnostic`** — it is a `readonly record struct` whose value equality the diagnostics panel relies on to skip
rebuilds (`DiagnosticsPanelViewModel.cs:106`), and a list member degrades that to a reference comparison. The
user's explicit instruction stands: **propose the contract shape first.**

---

## C5 — `DiagnosticsEngine` (2026-08-10)

The first module of the stage whose **contract shape was proposed and ratified before a line was written** — at
the user's instruction, because `Diagnostic` is a `readonly record struct` whose value equality the diagnostics
panel depends on. **9 keys for 8 codes**, `string Message` replaced rather than twinned, and one deliberate
change to a shipped Core type. Build 0/0; suite **8 542** (8 280 + 207 + 55); smoke clean, 0 `FATAL`; `Lab/`
untouched.

### ⭐⭐ The decision the etap turns on: fix the CARRIER, not the struct

`LocalizableMessage` is a positional record over `IReadOnlyList<object?>`, so its synthesized equality compared
the argument list **by reference**. Embedding it in `Diagnostic` naively would have made two structurally
identical findings unequal, and `DiagnosticsPanelViewModel.Update` skips rebuilding its `ObservableCollection`
— keeping the user's selection — precisely by comparing findings. The panel would have churned on **every
debounce tick**, with a green build and no failing test.

The alternative considered was a fixed-arity carrier on `Diagnostic` (`string? Arg0, Arg1`). It was rejected for
reasons that are measurements rather than preferences:

| | |
|---|---|
| ⭐ It fixes the property **where it belongs** | one change serves every future producer (C6 `ExecutionSummary`, Office, Performance) instead of inventing a diagnostics-only argument shape |
| ⭐ No arity ceiling | today's maximum is 2; a ceiling is a defect scheduled for later |
| ⭐⭐ **Measured safe** | grep found **zero** consumers of this type's equality anywhere, so the change can only make more messages equal, never fewer |
| ⭐ One currency | `Diagnostic` stays in the same shape as every other migrated producer, so the App resolves it with the same `Loc.Format` — no second path |

⚠ **The precondition it introduces is guarded, not assumed:** an argument must itself be value-equatable. Every
argument in the codebase is a `string` or an integer (a boxed `int` compares by value — the same discipline C4b's
"arguments are already-formatted data" produces). `NoProducerPassesAnArgumentWithoutValueEquality` reflects over
every produced argument and requires a type that declares its own `Equals(object)` **and** behaves that way
against an independently rebuilt value.

### ⭐ Why the key lives on `Diagnostic` and is not derived from `Category`

`DiagnosticCategory` is a closed enum, and the ratified C0 rule would allow the App to map it to a sentence. It
was rejected on a measurement: **`ET0008` yields two sentences from one category** (`SUSPEND is not valid in a
trigger.` / `… in a function.`), so the map is not one-to-one. ⭐ Which settles the design rather than merely
blocking one option: the **category** says what KIND of problem this is — that is what `QuickFixEngine` switches
on — and the **key** says which SENTENCE. Two questions, two fields, the same split as `SettingKind` /
`SettingValueKind`.

⛔ And `ET0008` gets two keys rather than the noun as an argument, the ratified C3 shape: substituting one of
EmberTern's own nouns into a sentence works in English and breaks in a language that inflects, because the
argument cannot know which case the sentence needs.

### ⭐ No English twin — measured, not a shortcut

C3/C4a/C4b all kept the English half because existing tests pinned the exact wording. Here they do not:
`DiagnosticsEngineTests` asserts `Category`, `Code`, `Severity`, `Start` and `Length` and **never** the message
text (0 occurrences of `.Message`). Only two test sites touched text at all, both fixtures. So `string Message`
**goes away** — the C2 (Quick Info) shape, where the type changes.

⭐ **Zero text change proved mechanically, the C2 way:** the pre-change interpolated literals were extracted,
their interpolations normalised to placeholders, sorted, and diffed against the nine catalog values. **`diff`
empty, 9 == 9.** No permanent table of expected sentences, which would have been a second copy of the catalog
(#333).

### ⚠⚠ Two consumers the proposal's inventory missed — and both were found by the compiler

The inventory reported "exactly two surfaces display the text". It was wrong twice, and in the same way:

| Missed | Why the inventory did not see it |
|---|---|
| `DebugPreflight` (the debugger's launch pre-flight) | the loop variable is `d`, so a grep keyed on the word *diagnostic* could not match it |
| `FirebirdGrammarCorpusTests.AGenuineUnknownVariable_IsStillFlagged` | same reason — `d.Message.Contains(...)` |

⭐ **Both surfaced as build errors the moment the type changed, which is the point worth keeping**: a *type*
change enumerates its own call sites, where a same-typed change (adding a twin property) would have left both
silently rendering English forever. The C4a/C4b dual form has that cost, and here it did not have to be paid.

⚠ The test was not merely repaired but **strengthened**: it now asserts the **argument** (`Arguments[0] ==
"v_amonut"`) instead of searching the rendered sentence — the portable form `localization.md` §4.2 recommends.

⚠ `DebugPreflight` deliberately gets **no** language hook: its items are rebuilt by `PrepareAsync` on every
launch and Restart, and the launch panel is replaced by the session's surfaces once a run starts, so a
pre-flight list never outlives the operation that produced it.

### ⭐⭐ Live switching (ratified W3) — and the trap it exists to avoid

The panel owns **one** subscription to `Loc.LanguageChanged`; `DiagnosticRowViewModel` became an
`ObservableObject` but subscribes to **nothing** — a subscription per row would be one leak per finding.

⛔⛔ **The obvious repair does not work, and this is the finding of the iteration's App half:** rebuilding the
rows and republishing them is **swallowed by `Update`'s `Unchanged` check**, because after a mere language change
the findings genuinely are the same. The optimisation that protects the selection would eat the refresh. So the
hook never touches the collection: it asks each existing row to re-read its own text. No rebuild, no
`CollectionChanged`, no lost selection — asserted together in one test, and **planting the "rebuild and
republish" version fails it with *"the row was never told its text changed"***.

### ⭐ Hover: measured unreachable, so no mechanism

Following #346 — establish whether the state can occur before building for it. The hover card is dismissed by
**`PointerExited` on the `TextView`** (→ `Clear()` → `HideHover()`) **and** by **any `PointerPressed`**. Reaching
the Language radio requires leaving the text view *and* clicking, twice over. ⇒ *"the language changes while a
hover card is open"* cannot happen; the card resolves at build time and gets no hook. ⛔ Recorded in place at the
call site, because the reasoning lapses if either dismissal path is ever removed.

### ⛔ ET0004 is unreachable — a measured finding, not a gap

`UnresolvedParameter` needs an **unresolved** `SymbolReference` whose role is `Parameter`, and no binder path
produces one: `BindParameterToken`'s fallback is `_ => ReferenceRole.Variable`, `BindBareLocal`'s default arm is
`Variable`, the explicit `AddReference(tok, null, Variable)` is `Variable`, and the `Parameter` role is only ever
attached to an already-matched `ParameterSymbol`. ⭐ Corroborated independently: **there is no ET0004 test
anywhere in the suite** — which is what an unreachable category looks like from outside, and nobody had noticed.

The key stays declared and produced (the arm is live code), as a **named exemption with a pinned premise**:
`TheOnlyUnreachableCategory_IsStillUnreachable` drives six PSQL shapes that would produce one if any did and
requires none to. ⚠ Reach stated honestly — a negative over known shapes, not a proof over the whole binder;
enough to make the exemption falsifiable, which is its job (#322).

### Verification

**Six plants, each firing its own guard and no other:**

| Plant | Fires |
|---|---|
| A `LocalizableMessage` back to reference equality | ⭐⭐ **7 tests**, including the two PRE-EXISTING panel-churn tests (`Update_WithUnchangedDiagnostics_DoesNotRebuildTheCollection`, `…_KeepsTheSelection`) |
| B ET0008 as one key with the noun as an argument | the noun guard + the coverage guard |
| C — *(covered by D)* | |
| D the language hook rebuilding and republishing | the live-switching test, naming the exact symptom |
| E an argument without value equality (`char[]`) | `NoProducerPassesAnArgumentWithoutValueEquality`, alone |
| F a key renamed, `.resx` untouched | the coverage guard + the three catalog-partition guards |

⭐ **Plant A is the strongest result in the etap**: the defect the ratified contract was designed to prevent is
caught both by the new guards *and* by two tests that already existed — so the protection is not something C5
invented, it is something C5 kept.

⚠ Known flake `SettingsLoadHealthTests.ConcurrentSaves_NeverLeaveSettingsUnreadable`: 1 of 3 main-partition
runs, the other two reaching the full 8 280. ⛔ Not related — C5's footprint does not include
`ApplicationSettingsStore`, `AtomicWrite` or the cross-process lock.

### Impact on existing contracts

| Element | Impact |
|---|---|
| `Diagnostic` positional signature | 8 production + 6 test construction sites |
| `LocalizableMessage` | `Equals`/`GetHashCode` overridden; ⭐ zero equality consumers before, so behaviour-neutral elsewhere |
| `DiagnosticsEngineTests` | ⭐ **unchanged** — it never asserted message text |
| `QuickFixEngine`, `SquiggleRenderer`, `HoverInfoEngine`, F8 navigation | ⛔ untouched — none reads `Message` |
| `SqlParser` / `ParseResult` | ⛔ untouched (the recovery channel still has no producer) |
| Headless partition filter | +1 name (**18**) |

### ⏭ What C6 has to do

**`ExecutionSummary` / `ExecutionActivity`, ~15 messages.** ⛔ They build sentences by **concatenation**
(`"{0} {1} {2}"` over a count, `"row"`/`"rows"`, and a verb), so unlike C5 they cannot be migrated key-for-key —
the key must cover the whole sentence, and Polish has three plural forms for that shape. ⚠ This is the module
that finally forces the plural mechanism, whose case counter now stands at **5** (C5 added none: ET0006 hedges
with `column(s)` and is translatable as it stands).

---

## C6 — `ExecutionSummary` / `ExecutionActivity` + the plural mechanism (delivered, awaiting acceptance)

**18 keys, 24 catalog entries, 5 plural families.** Contract ratified **before** any code was written (the
order C5 established); variant **W‑B** with R1(a) · R2(a) · R3(a) · R4(a) · R5(b) · R6(a) · R7.

### ⭐⭐ The finding that shaped the etap: three problems, and only one needed a mechanism

The old code did three different wrong things at once, and separating them is what kept the mechanism small.

| | Problem | Answer |
|---|---|---|
| **P1** | **Word order** — `"{n} {row/rows} {inserted}"`, `"{n} {inserted into} {table}"` | ⛔ no mechanism. The ordinary D‑3 rule at the resolution of a SENTENCE: one whole sentence, one key, data as arguments. The translator then owns the order. |
| **P2** | **Fragment concatenation** — `string.Join(" · ", parts)` | ⛔ no mechanism. Each term became a whole clause-key; the separator stayed punctuation (class D). |
| **P3** | **Plural category** — 1 / 2–4 / 5+ / 12–14 / 22 | ✅ **the whole of the new mechanism.** |

⭐ So the mechanism answers exactly one question — *given a key and a count, which variant to resolve* — and
is one 150-line file plus one branch in `Loc.Format`.

### ⭐⭐ The inherited counter of "5 plural cases" did not survive measurement — again

The third inventory in this stage to be wrong, and this time wrong in the direction that would have UNDERSIZED
the design.

| Category | Recorded | Measured |
|---|---|---|
| ternary plural branches in code | 5 | **7** (4 in open modules, 3 in the closed Performance module) |
| `(s)` hedges in the App catalog | — | **30** |
| Core sentences already wrong in English | — | **3** (`"1 rows affected"`) |

⚠ The 30 hedges are the same class of problem, solved by an English convention Polish does not have —
`"8 wiersz(y)"` is not idiomatic and is simply wrong for 1 and 2. ⛔ C6 does not migrate them (ratified R6),
but a mechanism designed for 5 cases would have been undersized by an order of magnitude.
⭐ A seventh case is a different grammar problem entirely: `StaleStatisticsRule`'s `"has"/"have"` is VERB
agreement, not a noun's plural — and it is served by the same "count → category → variant" shape, which is
why the mechanism was scoped to *choose a sentence variant* rather than to *pluralize a noun*.

### The contract as built

- **Core: no contract change.** `LocalizableMessage.Of(key, count)` as before; equality untouched. Whether a
  sentence needs plural forms is a property of the LANGUAGE, so it is declared **per culture in the catalog**
  — Core saying "this message is plural" would be Core asserting grammar it cannot know (ratified R4).
- **Catalog:** a plural key is a family of CLDR-named variants (`key.one`, `key.few`, `key.other`, …).
- **Rule sets are named after GRAMMAR, never a language** (`one-other`, `one-few-many`) — several languages
  share one, so a language name would be false at its second consumer. Each culture names its set in its own
  `.resx` under `Localization.PluralRuleSet`.
- ⭐ **`Loc.Format` probes per CULTURE**, so English can keep a flat entry for a key Polish serves with three
  variants. Fallbacks: exact category → `other` → flat key; none of them throws.
- ⭐ **R3 lives in ONE place:** `LocalizableMessage.TryGetCount` — a method, not a member, so equality is
  unaffected. Two readers asking "where is the count" in two ways is how a dual form drifts (#357).

### ⭐⭐ Zero content change is proved by tests that were NOT touched

`ExecutionSummaryTests` (14) and `ExecutionActivityTests` (8) pin the English wording literally and call the
**no-resolver** overloads, which render through the new `ExecutionEnglish` table. They were not edited.
`ExecutionSummaryLocalizationTests` then requires the CATALOG to reproduce the same strings for every shape ×
every count — two independent bodies of data through one shared composer, so only a wording difference can
make them disagree. ⭐ That also catches a translator adding `{0:N0}`, i.e. #357's hazard, without a test that
has to restate it.

⚠ **One English value changed, deliberately and reported before the contract was accepted:** the driver-total
fallback had no singular at all and rendered `"1 rows affected"`. Giving the key a family is what makes it
translatable; correcting the English is the consequence.

### ⛔ The card stopped binding `TableChange.Verb` — the layout was the defect

The per-table card drew `Count` and `Verb` as two adjacent, differently coloured bindings. That is **English
word order written into the LAYOUT**: Polish says "wstawiono 14 wierszy", with the number in the middle, so no
translation of "inserted" could have produced a correct line. Ratified **R5(b)**: one localized sentence split
around its number (`Loc.FormatParts`), rendered as three bound `Run`s, keeping the accent on the count.
⭐ Side effect: three type-keyed `DataTemplate`s collapsed into one over an App row view model — and the
spacing ratchet demanded its baselines be LOWERED, which is the ratchet working as designed.

### ⚠ Live switching needed plumbing, and it is #353 for the third time

Measured: `WorkspaceTabViewModel.RaiseAllPropertiesChanged` reaches the TAB, not the child view model, and
`ExecInfo` / `ExecInfoCompact` / `QueryStatsText` are stored `[ObservableProperty]` values — a notification
re-reads the same finished English. Three surfaces, three answers:

| Surface | Answer |
|---|---|
| SQL Editor status line | recomposed from the kept `ExecutionSummary` (C4a's `ComposeSettingsHealthMessage` shape) |
| Procedure / Function exec-info panels | `RefreshLocalizedText` — the **fifth** member of the per-kind family |
| Messages LOG | ⛔ **not** recomposed — a timestamped record of what was said then (the "Query N" call) |

⭐ Rather than discover #353 a fourth time, `EveryViewModelThatCanRefreshItsText_IsForwardedFromTheTab` now
arms itself off the TYPES declaring `RefreshLocalizedText`: the next module to migrate a child view model
fails the moment it adds the method and forgets the one line.

### ⚠⚠ A test that passed for two reasons, caught before it was reported

`SwitchingLanguage_RebuildsTheActivityCards` was green whether the rows resolved on read **or** were rebuilt —
both are implemented, so it pinned neither. Split into two named assertions: the row captured *before* the
switch must answer in the new language (no cache), and the collection must hold *different* objects afterwards
(rebuild, which is what actually re-evaluates a binding on an object with no `INotifyPropertyChanged`). Each
half now has its own plant.

### Plants — ten, each firing its own guard

teen exclusion dropped (5 cases, incl. the end-to-end one) · English value drift · missing family category ·
flat entry beside a family · forwarding removed · non-count first argument (9 tests, incl. an **untouched**
`ExecutionSummaryTests` case) · rule set named after a language · whitespace between the runs in the shipped
XAML · row caches its text · refresh does not rebuild.

⚠⚠ **Two process defects paid for during the plants, both worth keeping:**
1. `git checkout` **cannot restore an untracked file** — reverting a plant on `PluralRules.cs` failed exactly
   as gotcha #350 describes. Every subsequent plant reverted by inverse patch, and the untracked files were
   copied to the scratchpad first.
2. A revert whose replacement string is EMPTY re-inserts the line **at position 0** — it put a method call
   above the `using` block of `WorkspaceTabViewModel.cs`. Marker-based patching only.

### Numbers

| | |
|---|---|
| Build | 0 errors / 0 warnings |
| Suite | **8 709** = 8 387 main + **267** grouped + 55 isolated |
| Growth | +53, ⭐ **all in the grouped partition** — the main partition did not move |
| Headless filter | +1 name (**20**) |
| Smoke | clean, no `FATAL` |
| `Lab/` | untouched |

### ⚠ Measured aside: §B.7's rule is not the rule the suite actually follows

The handover says *any test touching `Loc` must join `HeadlessCollection`*. Measured: **~40 test classes read
`UiStrings` from outside it**, including `ProcedureDetailTests` since the App stage. So the rule as written has
never been enforceable, and adding two more classes to the filter would have been arbitrary. What actually
holds is narrower and is what C6 followed: **a test that SWAPS the catalog joins the collection and undoes the
swap in a `finally`.** ⛔ Recorded, not fixed — widening the filter to forty classes is its own decision.

---

## C7 — `Performance` (2026-08-10, `d620cc8`)

⚠⚠ **This section was written AFTER C8, reconstructed from `d620cc8`'s commit message and its diff, because
the etap shipped without one.** Everything below is derivable from those two sources or from the code they
produced; the counts were re-measured rather than transcribed (`64` declared `Perf.*` keys, `68` English
entries, `4` plural families — all confirmed against the shipped files). ⛔ What is **not** recoverable and is
therefore absent: the audit conversation that preceded it, and any plant beyond the five the commit message
records by letter.

The seventh Core producer on D‑3 — and the module the stage had been deferring since C0, where it was listed
as *"a decision about **Performance**, which stays closed"*. The contract was opened by an explicit user
decision after the audit.

### The contract

| | |
|---|---|
| `Finding.Title` | `string` → `LocalizableMessage` |
| `Finding.Explanation` | `string` → `LocalizableMessage?` — ⭐ nullable **because `MessageKey` refuses an empty token**, so "no explanation" cannot be spelled as an empty key |
| `FindingEvidence.Label` | `string` → `MessageKey`; ⚠ **`Value` stays a `string` — it is DATA** |
| `FindingGuidance` | `MessageKey` + `IReadOnlyList<MessageKey>` |
| `Recommendation` | `MessageKey` + `LocalizableMessage?` |
| `PerformanceContext` | ⛔ `OutputVerb` and `OutputRowsLabel` **removed** |
| `PlanNode.IsSubqueryRoot` | new — one owner for the predicate |

⭐ **The migration invented nothing — it carried C1's contract across.** `Finding` and `SessionHealthFinding`
are structural twins, and `SessionWarningViewModel` was already a working consumer pattern. ⚠ `Finding` is a
`record` (a class), so **C5's trap does not apply**: the reference-equality hazard that forced structural
equality onto `LocalizableMessage` needs a value type to bite.

### ⭐⭐ Nine concatenations disappeared, and the plural mechanism took them without a change

Counted in the commit message: the `issue` clause (2), the `" and N sub-quer{y|ies}"` tail (1) plus its
morpheme (2), `has`/`have` (2), the corroboration tail (1), and the noun *"the filtered column"* (1).

C6's rule held exactly as designed: **the LANGUAGE decides whether a sentence needs plural forms.** English
declares **four** families and every other numeric sentence stays flat; Polish can add `.one`/`.few`/`.many`
to any of them **without touching code**.

### The ratified decisions, each with its reason

| | |
|---|---|
| **D‑3** | The output verb stopped being a WORD Core picks and inflects (rule R6 was gluing an English `"s"`) and became a **KEY chosen by a rule on `HasResultSet`** |
| **D‑6** | *"the filtered column"* stopped being a noun substituted into a sentence — **two whole keys**, both English wordings unchanged |
| **D‑4** | `PerformanceVerdict.Headline` **not migrated** — measured that no surface binds it; a named exception with a **pinned premise** (`TheHeadline_IsStillBoundByNoSurface`) |
| **D‑7** | `FindingSeverityHigh/Medium/Low/Info` — four literals in a **view model**, invisible to a guard that scans only `.axaml` (**#337**); ⚠ the twin `DiagnosticRowViewModel` had been localized since the App stage |
| **D‑8** | `PlanInsightSubquery` **deleted from the catalog** — it was a *translatable* entry matched against the ENGINE's text, i.e. **#356 living in the product** |
| **R‑1** (user) | Four separate keys with identical English values — `ReadAmplification.{Table,Statement}`, `RowsRead.{Table,Statement}`. **Different measurement scope, so they are not deduplicated by spelling** |
| **R‑2** (user) | The inherited *"Index A, B have no…"* kept verbatim |

### Zero change to the English — four different proofs for four different shapes

- **Guidance / evidence labels / `SeverityText`** — the diff is **byte-empty**.
- **`Recommendation`** — 7/7 identical.
- **Titles and explanations** — normalised diff, **7 deltas and every one is a placeholder replaced by the
  word it was being substituted with**, with the surrounding text identical to the character.
- **`Headline`** — the proof is the **UNTOUCHED `PerformanceReportTests`**.

⚠ One mechanical detail worth keeping: **the number travels as a raw `long`** (so `TryGetCount` can see it,
per C6's rule R3) and the `N0` grouping moved into the resource value as `{0:N0}` — render byte-identical.
⭐ That is #357's lever used deliberately rather than met by accident.

### ⚠⚠ The plants — 13 runs, and three of the five recorded outcomes were not what the plan expected

| Plant | Outcome |
|---|---|
| **H** | ⭐⭐ **The first G6 did NOT fire.** It transcribed the NAME of the withdrawn entry and asserted a **Core** counter instead of the **App** consumer — **#333 committed inside the guard written against #356.** The guard now drives `NoiseSummary` under a swapped catalog. |
| **B** | ⭐ **Fired nothing, and that is correct.** Under a REPLACEMENT pattern there is no second representation, so a label drift is a legal catalog edit. ⚠ The plant table had inherited the **dual-form** premise from C4b, where it does not apply. |
| **F** | Does not compile — deleting a key is caught by the **compiler**; the orphaned twin (**F′**) is G7's subject instead. |
| **A** | ⭐ Compiles with **0 errors** (**#355**) and fires **G5 + G8**. |
| — | A comment quoting the withdrawn identifier fired the source-scanning guard **twice**; the comments were reworded and **no guard was weakened**. |

⭐ The transferable half is B and H together: **a plant that does not fire is a measurement of the guard's
premise, not a formality** — one showed a guard testing the wrong layer, the other showed a plant written
against a pattern the etap was not using.

### ⚠ What the audit measured wrongly, found by the compiler

`x:DataType` in `PerformancePanelView.axaml` changed by one attribute: a **compiled binding forces it when the
row type changes**. The audit had counted bindings **by name** and therefore did not measure it — the same
shape as C5, where a type change enumerated its own call sites.

### ⭐ #353 for the FOURTH time, fixed in passing

`GradeLine`, `PlanLead`, `NoiseSummary` and `TimingText` were frozen in the startup language **before C7** —
inherited, not introduced. **G9 widens the existing self-arming guard to the GRANDCHILD level**: the panel
hangs under the procedure detail, so declaring `RefreshLocalizedText` on it armed nothing.

### Numbers

| | |
|---|---|
| Keys | **64** declared · **68** English entries (4 plural families) |
| Build | 0 errors / 0 warnings |
| Suite | **8 720** = 8 388 main + **277** grouped + 55 isolated |
| Headless filter | **21** names — ⛔ derive it from the code, do not transcribe |
| Smoke | clean |
| `Lab/` | untouched |

### ⛔ Out of scope and deliberately untouched

5 `Single`/`Multiple` pairs in App · 30 `(s)` hedges · `SessionHealth`/`QuickInfo` plurals · Office (which
became **C8**) · Data Import · the duplicated SQL operator tables · the dead `PerformanceProfiler`.

---

## C8 — `EmberTern.Office` ×2 (2026-08-10, `45829a5`)

The eighth producer on D‑3 and the **first outside Core/Firebird**. Two keys, two sentences — and the two
things worth carrying out of it were both produced by plants, not by writing the migration.

⚠ *Historical note: this section originally recorded that **C7 had no section here and no entry in
CLAUDE.md** — a gap left standing rather than filled with an invented narrative. ⭐ It was closed by a
separate documentation commit, reconstructed only from `d620cc8`; the C7 section is immediately above.*

### The contract

| | |
|---|---|
| `ImportSourceMessages` | `Import.Source.NotReadableXlsx` · `Import.Source.NotReadableXls` |
| `ImportSourceException` | dual form — `Localized` (key + data) beside an English `Message` |
| `InnerException` | the reader library's own words, **kept technical and shown to nobody** |
| `DataImportTabViewModel.Describe(ex)` | a TYPE test ahead of the catch-all; no shared resolver |
| `SetStatus` | **untouched** (ratified D‑3 variant (a)) |

Keys and exception both live in **Office**, following the stage's standing rule *a key lives with its
producer* — and that decision is what forced the guard work below.

### ⭐⭐ The boundary is the opposite of C3's, and it is a measurement

C3 established that the server's own message travels as an **argument**, because it is authoritative. Here the
library's message is **kept out of every user-facing form**, and the reason is not style:

- `DocumentFormat.OpenXml` answers `File contains corrupted data` for a workbook that is **not corrupted** —
  it is merely older than the format its name claims.
- `ExcelDataReader` answers `Invalid file signature`.

The first is not unhelpful, it is **false**. So the rule that generalises is narrower than "wrap foreign
messages": *a foreign message travels as data when it is authoritative, and is suppressed when it is wrong.*
The providers already said so in their own comments — *"Saying so is the honest refusal §0 asks for; passing
the raw message on is not."* C8 only moved the sentence onto a key.

⚠ `InnerException` is asserted **present** by a guard: dropping it would make the same diagnosis unreachable
a second time.

### ⭐⭐ The catalog guard did not see `EmberTern.Office` — and it failed ASYMMETRICALLY

`DeclaredCoreMessageKeys()` scanned Core + Firebird. This is C0 §4's finding for the second time, for a third
assembly — but the shape of the failure is the part worth knowing. Planted (Office removed from the set):

| Guard | Result |
|---|---|
| `EveryCoreShapedEntry_IsDeclaredByCore` | 🔴 red |
| `EveryLocalizedMember_MatchesItsEnglishEntry` | 🔴 red |
| **`EveryCoreMessageKey_HasAnEnglishEntry`** | 🟢 **green** |

The inverse plant (a renamed key, with Office in the set) turns **six** tests red including that third one.
So the guards that go red are the ones whose failure is *visible anyway* — an orphaned resource entry — while
the one whose failure is **silent** (a declared key with no English entry resolves to itself, putting a raw
identifier on screen) is exactly the one that simply stops looking.

⛔ The trap this leaves for a future author: had C8 declared the keys without touching the guard, two red
tests would have demanded attention — and the *obvious* fix is to exempt those two, not to widen the scan.
That would have closed the symptom and left the silent guard blind, in a state that looks resolved.

### ⭐⭐ The surface test was green for two reasons and pinned neither

C8's App-side change is one helper. To pin that it reaches the user, a test pointed the real
`DataImportTabViewModel` at a BIFF workbook wearing an `.xlsx` name and compared the banner with
`Loc.Format(...)`.

**Plant: revert the consumer to `SetStatus(ex.Message, …)`. All nine tests stayed GREEN.**

English is the only shipped language, so `Loc.Format(localized)` and `ex.Message` render the same characters —
*by the dual form's own guarantee*. The test could not tell **"the App resolved the key"** from **"the App
printed the English fallback"**. Same shape as **#357**, and as C6's
`SwitchingLanguage_RebuildsTheActivityCards`.

The fix is to **swap the catalog** (`Loc.UseCatalogForVerification`, undone in `finally`) for one whose answer
no producer's literal can match. After rewriting, the same plant fires exactly that one test.

⭐ Side effect worth naming: the class's membership of `HeadlessCollection` stopped being precautionary and
became **load-bearing** — it is now a test that swaps process-global state, which is the rule C6 measured as
the one the suite actually follows.

### ⚠⚠ A rationale written into the test was FALSE, and the plant disproved it

The comment on `EachProvider_RaisesItsOwnKey` claimed a key swap between the two providers is invisible to the
dual-form check, *"because both halves would still agree with each other"*. Measured: swapping the keys turns
**both** tests red — the English literal stays at the producer while the resolved entry moves, so they
disagree.

The dual-form check sees it **only because these two sentences happen to differ in English**. That is a
property of today's wording, not of the mechanism. So what the per-provider test actually earns is a **correct
diagnosis** — it fails with *"expected `Import.Source.NotReadableXls`"*, naming the defect, where its sibling
reports an English mismatch and sends the reader to the resource file, the one place that is innocent. The
comment was corrected in place rather than deleted.

### The plants — five, and two of them changed the work

| # | Plant | Result |
|---|---|---|
| 1 | renamed key | 6 red (3 mechanism + 3 C8) |
| 2 | Office removed from the guard's scan set | 2 red, ⭐ the third **green** — the asymmetry |
| 3 | resource wording drift (`workbook`→`spreadsheet`) | **exactly 1** red; ⚠ the existing `.xls` pin **passed**, confirming it could never have caught this |
| 4 | keys swapped between providers | 2 red — **disproved the written rationale** |
| 5 | consumer reverted to `ex.Message` | **0 → 1 after the test was rewritten** |

### ⭐ `Describe` is wired at all thirteen bare renders, not at the five reachable ones

Measured: five of the thirteen catch-alls can carry an `ImportSourceException` (source read, inference,
converted preview, the run, the run's backstop), and in practice the source read carries it, because `Open`
fails on the provider's **first** call so the chain never reaches the others.

It is still wired at all thirteen. Keying the decision to a call-site list would encode a **reachability
analysis into the code**: the set will move, nobody will re-measure it, and the failure is silent — an English
sentence in a Polish window (#337's shape). The discriminator is the exception's **type**, so for every other
exception the result is provably the string this module rendered yesterday. The two
`string.Format(…, ex.Message)` composites on Firebird DDL paths are deliberately unchanged.

### ⚠ Proving zero change to the English needed a different instrument than C4a's

C4a and C6 could prove it by leaving existing tests untouched. Here the pre-migration pins were **two
`Assert.Contains` calls on one of the two sentences and nothing at all on the other**, so no existing test
could carry the proof. Mechanically instead:

1. `git diff` of both providers shows the English literals as **context lines**, not `+`/`-`.
2. The dual-form guard chains that literal to the resource entry — read off a **really thrown** exception,
   not a re-derived string.

### The missing pin, added

`.xlsx` had **no test for its refusal at all**, while `.xls` had been pinned since I10. The asymmetry sat in
the *more common* case: I0 §3.5 found the `.xlsx` case on the machine's real spreadsheets. Added.

### ⛔ #353 left standing, deliberately

The banner does not survive a later language change: `SetStatus` stores settled text and the module has no
`RefreshLocalizedText`. Measured scope of fixing it: **31 call sites, 18 of them passing already-settled
`UiStrings` text**, so making the exception path live would leave eighteen frozen statuses beside one that
moves — an inconsistency worse than a uniform absence (R7). That is a decision about `SetStatus`, not a
consequence of migrating two sentences.

### ⚠ Inventory corrections

| Claim | Measured |
|---|---|
| "Office ×2" (C0) | ⭐ **survived** — exactly 2 user-visible literals in 6 production files. The first C0 inventory row that matched. |
| "`ex.Message` … in **eight** places" (C0 §3) | **15** sites read it; **5** reachable from Office; **1** dominant |

### ⚠⚠ A tooling defect paid for on the way

`sed -i` in Git Bash silently rewrote **CRLF → LF across a whole 2 581-line file**, and `git diff --stat`
reported only the intended 39/13 because the repository normalises line endings — **git masked a whole-file
rewrite**. Same family as the App stage's Python `\r\n` damage. Repaired and proved by round trip
(`tr -d '\r'` both sides, inverse substitution, byte-identical apart from the 13 intended tokens); every
touched file re-verified as fully CRLF. ⛔ `sed` is unusable for verification here too — its output stream
drops CR as well. See gotcha **#366**.

### Numbers

| | |
|---|---|
| Build | 0 errors / 0 warnings |
| Suite | **8 730** = 8 389 main + **286** grouped + 55 isolated |
| Growth | +10 (1 main — the `.xlsx` pin; 9 grouped) |
| Headless filter | +1 name (**22**) |
| Smoke | clean, no `FATAL` |
| `Lab/` | untouched |
| Commit | `45829a5` — ⛔ not pushed, not merged |
