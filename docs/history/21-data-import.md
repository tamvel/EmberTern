# 21 — Data Import (etapy I0–I12)

Narracja modułu importu danych: co powstawało, w jakiej kolejności i **dlaczego akurat tak**. To jest
„pamiętnik" modułu — bieżący stan opisuje CLAUDE.md, a obowiązującą architekturę
[docs/design/data-import.md](../design/data-import.md) (🔒 zamrożona od I0).

Plik powstał w etapie **I12** (domknięcie), gdy narracja modułu — ~520 wierszy — została **przeniesiona
w całości z CLAUDE.md**, gdzie rosła etap po etapie. Dokładnie ten nawyk kiedyś rozdął CLAUDE.md do
rozmiaru, przy którym samo otwarcie sesji zjadało połowę budżetu kontekstu; sprzątanie dokumentacji
z 2026-07-11 rozdzieliło role, a ten plik jest jego zastosowaniem do Data Importu.

---

## Czym ten moduł jest

Jedna powierzchnia robocza ze zwijalnymi sekcjami — **świadomie NIE kreator**, bo ten sam import
uruchamia się wielokrotnie, a bramką jest **pasek gotowości**, nie przyciski „Dalej". Jeden pipeline dla
każdego źródła (dostawca oddaje `SourceSchema` + `RawRecord`; schowek nie jest drugim parserem).
`ImportConfiguration` jest **jedyną reprezentacją każdej decyzji użytkownika** — stan powierzchni, wejście
pipeline'u i zawartość profilu to ten sam rekord, pilnowany testem odbicia round-trip, który wywala
zestaw, gdy nowe ustawienie go ominie. Wiersze idą do **własnej transakcji modułu** na **własnym
przyłączeniu** (amendment I7.5), a `CREATE TABLE` na linię **Ddl** (gotcha #213 — i powierzchnia mówi na
głos, że Rollback tej tabeli nie usunie).

## Trzy wyniki, które warto znać bez czytania całości

1. **Filar „jeden pipeline dla każdego źródła" wytrzymał trzy próby.** I9 (XLSX), I10 (XLS + schowek) i
   szew po I10 **nie tknęły** pipeline'u, konwertera, walidatora, planera mapowania ani writera. I10 był
   ostrzejszy od I9, bo przyszedł z **zależnością** (ExcelDataReader) — i ta zależność sięgnęła
   dokładnie jednego projektu.
2. **I11 był audytem projektu, nie funkcją — i rachunek się zgodził.** §6 nazwał dowód obalający: „jeżeli
   nazwane profile wymagają zmiany choćby jednego modelu albo przebudowy sekcji UI, §4.8 został po drodze
   naruszony". Nie zmienił się **ani jeden model**.
3. **Milion wierszy przechodzi w 14 sekund, a sterta jest płaska** (I12, na końcu pliku).

---

## Zapis etapowy

Poniżej pełny zapis, przeniesiony z CLAUDE.md bez skrótów. Kolejność jest odwrotna chronologicznie —
najnowsze najwyżej — bo tak rosła.

- **🔁 DATA IMPORT — THIRD REVIEW ROUND DELIVERED (2026-07-27), awaits the user's visual confirmation; then I12.**
  Branch `feat/data-import`, suite **5851 green** (+5), build 0/0, smoke clean. A full ergonomics audit produced
  more findings; the user selected **four** and explicitly declined the rest — including the `Existing table` /
  `New table` layout, which they re-examined and kept, because the options appearing to the right of each
  variant justify it. **No model, no pipeline, no converter, no validator, no planner, no writer was touched** —
  the fourth etap in a row that has held.
  **⭐⭐ U5 IS CLOSED, and it was a layout DEFECT rather than a matter of taste.** Every band except the work
  area is `Auto`, so the star takes what is left — and what is left can be **nothing**. In the „New table"
  variant the Target tile is the tallest band on the surface (type grid + optional DDL preview) and the bottom
  panel below it holds an absolute height, so **Mapping and the converted preview vanished entirely**. That is
  why the `Mapping` chip still „felt dead" after the second review had fixed its navigation: it was resolving
  correctly onto a panel of zero height. ⚠ **`MinHeight` is only half the fix — a floor you cannot reach is not
  a floor.** The other half is in `ApplyBottomPanel` (the one re-normalization point, #240): the bottom panel is
  **clamped** to what remains after the Auto bands and the floor. Of the two bands competing for the remainder,
  the one that yields is the one whose height *we* chose; the stored height is never overwritten, only clamped,
  so it returns by itself when the Target tile shrinks.
  **⭐ The `Transaction` chip now opens its picker.** It navigated correctly already, but it is the one „section"
  that is **not a band of this surface** — it lives in the command bar, a few pixels above its own chip, where
  `BringIntoView` is always a no-op and a `ComboBox`'s focus ring is the entire feedback. Opening the list is
  what „take me to this decision" can mean for a picker, and it still changes no setting.
  **⭐ A basis shared by every column is said ONCE, for the section.** After restoring a profile each row read
  „from the restored configuration", so a column as wide as the column-name column repeated one sentence per
  table column while the section line that should carry it once was deliberately blank — the same defect the
  second review fixed for `IMP0018` (one fact stated twice trains the user to read neither), multiplied by the
  row count. The rule is general: **if the basis is identical for the whole grid it is a fact about the grid,
  not about a column**, so it moves to the section line and the column's header disappears with its cells (a
  header over an empty column is exactly what reads as a dummy). Where the evidence really is per column — R19
  measured mixed columns as the norm — it stays beside the type it explains. ⚠ Deliberately left: the hidden
  column does not reclaim its width (the `3*` proportions are shared by header and rows; collapsing them needs a
  `bool → GridLength` converter, a new type at close-out). With the header gone it reads as margin.
  **⭐ `ImportReadinessReport.Prioritized` — the cap can no longer hide a blocking error behind a warning.** The
  strip shows three findings and hides the rest, so its order is not taste: it decides what disappears.
  Evaluation order is by section, which is right to *read* and wrong to *cut* — a new table **always** raises the
  non-blocking `IMP0018` in the Target section, i.e. ahead of every mapping finding, so it took one of the three
  visible slots from the errors explaining why the run is refused. Blocking first, stable within each group, and
  **in Core** for the same reason `CanValidate` is: a view cutting the list by its own rule would be a second
  opinion about what matters most. Stability is what keeps a collapsed strip a true **prefix** of the expanded one.
  ⚠ Two of the four are view-level and cannot be proven by the suite — per the project's QA rule they are
  „implementation done, awaits visual confirmation", not „fixed".
- **🏁 DATA IMPORT I11 — CLOSED AND USER-ACCEPTED (2026-07-27).** Suite was **5846 green** (+45) at that point.
  Accepted after two review rounds, with the user noting that not everything looks the way they will ultimately
  want it.
  **⛔ STANDING DIRECTIVE FROM THE CLOSE: do NOT return to Data Import cosmetics.** The remaining wishes are
  *purely* UX and belong to the planned **app-wide UX sprint** (Avalonia control replacement, density,
  behavioural consistency) — the same sprint U4/U5 already wait in. Come back to this module **only for a real
  functional defect**. ⚠ This is the one-task-at-a-time rule paying off in the other direction: the module was
  carried to its frozen design without letting a cross-cutting UI concern reopen it.
  **🔁 The visual review found the selector was a ONE-WAY DOOR** — you could enter a profile but not leave it,
  neither keeping your decisions nor starting over. Two additions, and they are deliberately **two different
  actions, not one**: a standing **„(no profile)" row** at the head of the list **detaches and keeps every
  decision**, while a **`Reset`** button **restores the defaults and detaches**. Collapsing them would mean the
  selector silently destroyed work nobody asked it to destroy (rule #11) — which is also why the row is named
  "(no profile)" and **not** "default configuration": that name would promise defaults it does not restore.
  Picking it says so on the message bar, because "will this also clear my work?" is the only question it raises.
  **⭐ `Reset` asks about EMPTINESS, not modification.** "Has anything changed since it loaded" needs a canonical
  comparison of two `ImportConfiguration`s, and a record compares its list members by reference — the mapping is
  a new instance after every recalculation, so that check would answer "changed" always. So it asks the
  answerable question — *is anything on the surface at all* — and on an empty surface it asks nothing, because a
  dialog with no stakes only teaches people to dismiss dialogs. **`Reset` and the „Clear" beside the restore note
  are now the same code** (`ResetToDefaults`); "Clear" had its own copy and, as a side effect of the merge,
  finally detaches the profile too. ⚠ **`HasProfiles` was deleted** — it was bound to nothing in XAML and read
  only by tests, the same discipline that kept `MarkUsed` out of this etap.
  **🔁 A second, full UI review then produced five ergonomics findings — and ⭐⭐ two of them were a DEFECT, not
  a matter of taste.** The user reported the `Target`, `Mapping` and `Transaction` readiness chips as *"feeling
  like dummies"*. Two genuinely did nothing in ordinary states: **Target** resolved to the existing-table picker,
  which Avalonia reports as not-effectively-enabled whenever the new-table variant is chosen, so
  `FirstFocusable` returned null and the click was swallowed; **Mapping** resolved to "the row needing
  attention", which is null once everything is mapped — so it worked *only while something was wrong*. **One
  rule now holds for all five: make the section reachable, then focus the control that section currently IS
  ABOUT** — a green chip navigates exactly like a red one — with `BringIntoView()` **before** `Focus()` (focus
  alone scrolls nothing, which was the other half of the dead feeling on a long column list) and a fallback
  chain that cannot resolve to null. ⭐ **"What is the Target section about" is a VM decision
  (`ImportTargetFocus`)**, because it follows the chosen variant and a view computing it again would be a second
  opinion about which half of the section is live: unnamed new table → the name box, named → the type grid,
  where the remaining decisions are. Four tests pin it; the missing answer was the whole defect. The findings
  under the chips share the same command, so they were fixed by the same change — and since every
  `ReadinessItem` carries a section and all five sections now lead somewhere, none had to be demoted to plain
  text.
  **⚠ Gotcha #273 came out of this and generalises well past the module:** a navigation affordance that resolves
  its target by "first focusable control" silently does nothing wherever that control is disabled, and a
  resolution allowed to return null turns a working feature into a dead one with **no failing signal at all** —
  nothing throws, every property is healthy, the suite stays green. Three rules: a user-facing resolution must
  not be able to produce nothing (fall back to the container); `Focus()` scrolls nothing, so pair it with
  `BringIntoView()`; and "which control is this section about" is a **ViewModel** decision whenever it depends
  on state the VM owns.
  **⭐ One duplicated warning removed.** §0.5's sentence ("the table is created and COMMITTED before the first
  row") stood twice on one screen — as Core's `IMP0018` in the readiness strip *and* as a banner under the type
  grid. The **banner** went: the strip says the same thing, comes from Core and additionally **names the table**,
  and one fact said twice trains the user to read neither. ⚠ Accepted risk, stated: the strip caps at three
  findings, so `IMP0018` can fall under "…and N more" — if that bites, the fix is to make the **strip** louder,
  never to add a second sentence.
  **⭐ `Existing table` / `New table` became ONE grid.** They were two stacked grids, so each measured its own
  radio label and the two inputs started at different x; the facts line sat *between* them, splitting one choice
  in half. Now two rows sharing column definitions — inputs aligned, facts line moved below the pair where it
  belongs to the answer rather than the question — which is the user's own sketch, one row shorter than before.
  Also: **`Basis` is single-line + ellipsis + full text on hover** (wrapping set the whole row's height and let
  the annotation dominate the grid it annotates; §0.6 is about the evidence being *reachable*), and the
  **Mapping panel gained a title** to match the Preview half that already had one, both promoted to the shared
  `group-header` — reused, not a new style, and `ControlStyles.axaml` was not touched.
  **Separation accepted, with an asymmetry that is the point:** the divider between the profile group and the
  execution group went to a 12 px margin while the one further right stays at 4 — they are not peers, because a
  profile is superordinate to the whole import configuration whereas the other divider only separates settings
  *within* execution.
  **⭐⭐ I11 was the design's own audit, not a feature — and §4.8's account balanced.** §6 named the disproof
  ("if named profiles require changing even one model or rebuilding a UI section, §4.8 was violated along the
  way"). **Not one model changed:** `ImportConfiguration`, `ImportProfile`, `ImportPipeline`, the converter, the
  validator, the mapping planner and the writer are byte-identical, the `settings.dat` container version is
  untouched, and the selector went into the slot §4.8.4 **reserved in the MVP** — the toolbar was not rearranged.
  **⭐ The one place that had to be written was the one place I1 said would be.** `ImportProfileStore` gained
  named-entry operations (`ListNamed`/`GetById`/`SaveNamed`/`NameExists`/`Rename`/`Delete`/`IsReadable`); its own
  I1 comment reserved them for I11 precisely because methods nothing calls are indistinguishable from a
  regression (gotcha #233). A named profile is a row of the list that already existed and happens to have a
  `Name` — `IsImplicit` still means exactly what it meant. ⚠ Worth recording: the session prompt listed
  `ImportProfileStore` among the "stop and report" items, while §4.8.4 calls the named-profile UI *"purely a view
  over the existing store"*. Resolved in favour of the design document — the MODELS are `ImportConfiguration` and
  `ImportProfile`, and those are untouched — and reported explicitly, as the etap requires.
  **⭐⭐ A profile that no longer fits the world needed ZERO lines of profile code.** Loading one is
  `ApplyConfiguration(profile.Configuration)` and nothing else — the same call the restored "last used"
  configuration has taken since I7 — so the chain re-reads the source, re-reads the target from the catalog and
  re-plans through `ImportMappingPlanner`. A deleted file therefore surfaces as **IMP0011**, a dropped table as
  **IMP0016**, and a source whose fields moved is **re-planned by name, never restored by position** (the §4.8.5
  point 1 defect). None of that is conditional code; it falls out of there being no second load path. Three tests
  pin it. ⚠ The last of them needed **two** spare source fields: with exactly one unmatched column and one unused
  field the planner's sole-remaining-pair rule fires by design (IMP0008, "assumed") — correct behaviour that
  would have hidden what the test was about. **A fixture that lets a documented rule fire is not a product bug.**
  **⭐ A profile from a newer build is LISTED and refused, not hidden** — hiding it reads to the user as a
  deletion, and half-applying it is §0.7 outright; `ImportProfileStore.IsReadable` is the ONE predicate, shared
  with the implicit restore so the list and the loader cannot disagree about "too new". **⭐ The selector's scope
  is said on screen:** this connection's profiles plus any tied to none; one saved on another connection is not
  offered, because it names a table this database may not have — and a restriction the user cannot see is
  indistinguishable from a profile that vanished.
  ⚠ **Deliberately NOT built, and stated rather than quietly dropped: `.json` export/import** (optional in §6,
  absent from the DoD). It needs a new Core serializer plus two view seams — it would *enlarge the surface this
  etap exists to certify as untouched*. The scope note was reworded so it promises no such exchange.
  ⚠ **Also deliberately not built: a "modified" marker on the selected profile.** It needs a canonical comparison
  of two `ImportConfiguration`s, and a record compares its list members (mapping, boolean tokens) by reference —
  the mapping is a fresh instance after every recalculation, so the marker would light up the instant a profile
  loaded. Building that comparison would be the model change this etap proves was unnecessary.
  ⚠ **Fixed in passing, inside the module:** the shared destructive confirmation carried only a message while the
  view supplied a **fixed** title ("Empty the table before importing") and a **fixed** button ("Import") — so the
  "drop the table this import created?" question was already being asked under another action's name. The event
  now carries the whole `ConfirmRequest`. New shared `TextPromptDialog` (mirrors `ConfirmDialog`'s
  request/VM/close idiom) because the app's only name prompt, `NewFolderDialog`, is folder-worded throughout.
- **🏁 DATA IMPORT — POST-I10 ERGONOMICS SEAM CLOSED AND USER-ACCEPTED (2026-07-27).** Three review findings from I10, scoped by the user as **closing
  I10, not a new etap**: the clipboard as a **live source**, a **Refresh** button, and the requirement that a
  re-read go through **the same chain** as the first read. Confirmed with no findings, and the architectural
  direction endorsed explicitly — *"the important thing is that no second refresh path appeared; everything goes
  through one chain with different reasons for running (Decision / Refresh)"*. `Ctrl+R` stays; **`F5` was
  deliberately NOT added** (user's call — `F5` is Import). Suite **5801 green** (+16), build 0/0, smoke clean. **The models, the pipeline, the converter, the validator, the mapping planner and the writer were not
  touched** — the third time that has held (I9, I10, this).
  **⭐⭐ The user's architectural condition — "I don't want two update paths" — was already met, and the whole
  seam was bringing three things onto the one path.** `Recalculate` → `RunChainAsync` is and was the single
  entry point; what bypassed it was the clipboard (fetched by a command *beside* the chain) and what could not
  reach it was a user asking for a re-read (there was no way to run it at all).
  **⭐ THE REASON IS AN ARGUMENT TO THE ONE CHAIN, NEVER A SECOND CHAIN.** `ImportChainTrigger.Decision` (an
  edit — recompute over the facts already established) vs `.Refresh` (the user asked — drop the cached facts and
  re-read them first). Exactly the discipline that made "Validate" a different **writer** passed to the one
  `ImportPipeline` rather than a second mode: there is nowhere for a refresh to drift from an edit.
  **⭐ Why "even Ctrl+V didn't recompute the mapping" was NOT a mapping defect (gotcha #271).** The clipboard's
  text arrived by **assigning a property**, and `SetProperty` compares — identical text meant no
  `PropertyChanged`, no `Changed`, no chain, so re-reading unchanged content recomputed **nothing**. The read is
  now the chain's **first link**, so what happens next follows from *having been asked*, not from the value
  differing. **General rule: any state whose only refresh trigger is "someone assigns it" cannot be refreshed to
  its current value.**
  **⭐ Refresh re-reads the two FACTS the surface caches** — the clipboard text and the table list (which was
  latched behind `_tablesLoaded` and read once per tab, so "the table blocking my `CREATE` has been dropped" was
  invisible until the tab was reopened). Everything downstream then re-runs unchanged: source → analysis →
  schema → mapping → readiness → preview.
  ⚠ **Found by a test that COUNTED clipboard reads (gotcha #272): `SourceDescriptor` cannot say "a file, but
  none chosen yet"** — `BuildSource` reports `Kind == Clipboard` for that state, so a gate on the source kind
  would have made **every freshly opened tab** read the clipboard and, whenever the content looked tabular,
  adopt it as the source **with the „File" radio still selected**. The gate asks the thing that actually carries
  the user's choice (`Source.UseFile`, i.e. the radio).
  **⭐ The automatic read adopts content only if it IS a table, and invents no heuristic for that:** the first
  question goes to `DelimiterDetector`, the module's one owner of "is this delimited text", so "tabular" means
  here what it means everywhere else; the second question exists only because that detector deliberately refuses
  to invent a separator for a single column, and a single column pasted out of Excel is still a table. **An
  explicit refresh does not consult it at all** — "re-read the clipboard" has one honest meaning, so it adopts
  whatever is there, **including nothing** (readiness then says "no source", which is true; keeping the previous
  text would show data the named source does not hold).
  ⚠ **Deliberate boundary, pinned as a test:** a refresh does **not** re-propose a new table's types while the
  source still describes the same fields — edited types are decisions, and overwriting a decision with a
  proposal is what rule #11 forbids outright; the existing rule re-infers when the fields or the culture move,
  i.e. when the ground under those decisions actually shifted.
  Also: `ClipboardReadRequested` moved from an **event** (wired after construction) to
  `DataImportEnvironment.ReadClipboardAsync`, because the **chain** reads the clipboard and must be able to ask
  before anything could be wired to the finished tab (the file picker stays an event — only a user command drives
  it); and `FileFacts` → **`SourceFacts`**, one facts line for both variants, carrying the clipboard's **read
  time** — the form of "is what I see current?" that a live source can answer, and what makes a refresh visibly
  acknowledge itself when the content is identical.
- **✅ DATA IMPORT I10 — CLIPBOARD + `.xls` DELIVERED (2026-07-27), awaits the user's visual confirmation in
  both palettes.** Branch `feat/data-import`, suite **5785 green**
  (+22), build 0/0, app launches clean, `DataImportRunProbe` **33/33 ALL PASS** on FB5 `WI-V5.0.3.1683`
  (section **I** added).
  **⭐⭐ The pillar held under a harder test than I9's, because this time a DEPENDENCY arrived.** I9 added a
  source using a library the project already had; I10 adds **`ExcelDataReader` 3.7.0 (MIT)** — the one NuGet
  decision left in the module — and it reaches **exactly one project** (`EmberTern.Office`). The pipeline,
  converter, validator, mapping planner and writer were **again not touched**; `IImportProvider` now has a
  third implementation and App learned exactly one thing: `ProviderFor` has one more branch.
  **⭐ The clipboard was already built — the etap added only proof.** The Plik/Schowek toggle, `ClipboardText`,
  `UseClipboardCommand`, the `ClipboardReadRequested` delegate and `MainWindow`'s `TopLevel` read all shipped
  in I5. There was nothing to write; there was something to *verify*, and that turned out to be the
  interesting part (below). ⚠ Worth carrying: **check whether a scope item is already standing before writing
  it** — the opening prompt said "only the clipboard read is missing", and that existed too.
  **⭐⭐ Delimiter detection is a step ABOVE the pipeline, and the first version of the probe case did not
  mirror that.** It passed `AutoDetectDelimiter = true` straight to `ImportPipeline` and got back **one
  column**. That is not a defect — it is §0.4 working as designed: auto-detection **proposes**, shows its
  basis, and the **resolved** value goes into the configuration, so the provider reads a declared delimiter
  and holds no opinion about detection. The probe has to walk the whole road the surface walks or it proves
  nothing about the surface. ⚠ **General rule: when a probe and the UI disagree on the same input, first ask
  whether the probe reproduced the entire path** — only then look for a defect in the product.
  **⭐ Excel's calendar got ONE owner because the second provider needed it in reverse.** `WorkbookCellReader`
  computed serial → date itself (I9). ExcelDataReader hands back an already-decoded `DateTime`, so honouring
  "do not treat date cells as dates" means converting **back** to a serial — and an inverse written beside a
  forward function it cannot see is exactly how two halves of one calendar drift apart. Hence
  `ExcelSerialDate` (`FromSerial` + `ToSerial`), used by both providers. Pinned: serial 15 is 1900-01-15 in
  both directions (`FromOADate(15)` would say 1900-01-14).
  **⭐ The library decides "is this a date" for us, so its verdict is RE-ASKED.** The danger here is the
  inverse of `.xlsx`'s: not a date missed but a date **invented**. `XlsCellReader` asks
  `SpreadsheetNumberFormats` — the one owner of that question, which **parses** a format code rather than
  searching it (gotcha #268) — so the real user's custom currency format `#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]`
  stays money here too, even though the library independently called it a date. It also closed a divergence
  nobody would have gone looking for: a "time only" cell came out of `.xlsx` as a `TimeSpan` and out of `.xls`
  as `1899-12-31 12:00`; both now yield a `TimeSpan`.
  ⚠ **R8 measured BEFORE the provider was written** (throwaway `XlsFormatProbe`, deleted on close). A 60 000-row
  × 5-column BIFF8 sheet: heap **flat at 26.7 MB at rows 15 000 / 30 000 / 45 000 / 60 000**. The *shape of the
  curve* is the proof, not the final number — a reader that materialises the sheet grows with the row count.
  The 19.6 MB retained is the shared-string table, proportional to **distinct** strings rather than rows — the
  same property I9 documented for `SharedStringTable`.
  ⚠ **Correction to I9:** `Wynagrodzenie.xlsx` was recorded as "the old format under the new name". Measured
  in I10: it is an OLE2 container (signature `d0cf11e0`) but **not a workbook** — the BIFF reader answers
  *"Neither stream 'Workbook' nor 'Book' was found"*. I10 therefore does **not** make that file readable, and
  the refusal message promises no such thing. `XlsxImportProvider`'s refusal did change, though: it used to
  advise "Save As", which was the only way out while BIFF was unreadable; it now leads with "rename it to
  `.xls`". A refusal that keeps recommending the long way round after a short one exists has quietly stopped
  being true.
- **🏁 DATA IMPORT I9 — XLSX CLOSED AND USER-ACCEPTED (2026-07-27).** Confirmed visually in both palettes;
  the user checked the sheet picker, the hiding of CSV/TXT-only settings, "treat date cells as dates" and a
  full XLSX run — no visual or functional findings.
  **Next after I9 was I10 (delivered — see above) · then I11 named profiles · I12 close-out. ⛔ User directive on closing I9: no
  further refactors and no architectural changes — the module is carried to the end on the frozen design.**
  Branch `feat/data-import`, suite
  **5763 green** (+46), build 0/0, `DataImportRunProbe` **25/25 ALL PASS** on FB5 `WI-V5.0.3.1683`.
  **⭐⭐ The result that matters is what did NOT change.** I9 was the first etap to add a new SOURCE, and
  therefore the first real test of the "one pipeline for every source" pillar (§1.4). **The pipeline,
  converter, validator, mapping planner and writer were not touched** — all workbook knowledge lives in
  `XlsxImportProvider` (project `EmberTern.Export.Office` → **`EmberTern.Office`**, decision D1). Probe
  section H makes it visible: the same journey as sections A–G, one new provider. It also closed a rule-#2
  debt — `IImportProvider` finally has a **second** production implementation (§4.3 called the single one
  transitional).
  **⭐ A defect in I0's own probe heuristic, found and NOT carried into production.** The probe asked
  `code.Contains('d')`. The custom format from the user's real file — `#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]`,
  which I0 itself labelled *"currency, NOT a date"* — answers **TRUE**, because `[Red]` contains a `d`. The
  probe never noticed because no cell used that style (it measured "numeric cells with a date format: **0**").
  In production that turns a money column into dates, silently — §0.1's worst class. `SpreadsheetNumberFormats`
  **parses** the format code (quoted literals, escapes, bracketed sections — `[Red]` rejected, `[h]` accepted
  as elapsed time) instead of searching it. ⚠ **The general lesson: a probe proves what it happened to
  execute.** Its PASS is not evidence the heuristic is right, only that the input never exercised it.
  **⭐ R20 closed with a CARRIER, not a new rule.** `ImportErrorKind.SourceErrorValue` had existed since I2
  (naming R20 in its own comment), with the UiString and the report mapping already wired — only the value a
  provider could use to say "this cell is an error" was missing. New `SourceErrorValue` (Core, deliberately
  source-neutral — `RawRecord` is the currency shared by every source) plus one branch in
  `ImportValueConverter` **before** the target-type branches. The order is the substance: the text branch
  returns `Ok(text)` unconditionally, so if the refusal depended on the column type, `"#N/A"` would land in a
  VARCHAR as data. Verified on the live engine (probe **H1b**).
  **⚠ I0's owed measurement gap (R3) is closed on REAL Excel output.** I0 honestly recorded that the user's
  file held *no* date cells, so date handling was designed against a generated sheet. The production provider
  was run over **eight real workbooks from the user's disk**: `Fantomy…` reproduced I0's measurements exactly
  (column "Nr technologii" = `double×4999 + string×1`), and **`Wyceny.xlsx` has genuine date cells** — column
  "Termin", ~450 dates across seven sheets, vintages 2021–2026, all read correctly. The round trip is proven
  end to end by probe **H4** (sheet `2026-04-03` == database `2026-04-03`).
  ⚠ Also found in passing: **a file named `.xlsx` need not be one** (`Wynagrodzenie.xlsx` is the old format
  under the new name; `SpreadsheetDocument.Open` says *"File contains corrupted data"*, which reads as "your
  file is damaged" when the truth is "this is BIFF and this library cannot read it"). The provider translates
  it into a sentence the user can act on. And Excel's pre-1900-03-01 epoch is **not** OLE's (serial 1 differs
  by a day, plus the phantom 1900-02-29): the correction is explicit and the phantom day stays a number rather
  than becoming a silently shifted date.
- **🔴 I8 REVIEW FOUND A CRASH — FIXED 2026-07-27, and its two lessons generalise well past this module
  (gotchas #264 / #265).** Symptom: new table → Validate passes → **Import closes the whole application**.
  Diagnosed in a minute from `%TEMP%\EmberTern-debug.log`, which the app's own `AppDomain.UnhandledException`
  hook fills with the full stack trace — **read that log first when a crash is reported, before re-reading any
  code.** Two independent defects, both mine, both pinned:
  **(1) Hiding a control does not retract the decision it carries.** "Empty the table before importing" is
  meaningless for a table about to be created and its checkbox is hidden in that variant — but `BuildBehavior`
  still copied the value, so a tick made on the existing-table variant (or, more likely, arriving inside the
  **restored "last used" configuration**, where the user never saw it) left `true` in the record and the run
  read a row count from a table that did not exist yet. Fixed in the ONE place that turns UI state into the
  record; the tick itself is deliberately not cleared, so switching back finds it where it was left.
  **(2) ⭐ `AsyncRelayCommand` rethrows a faulted command on the dispatcher, so an unhandled exception in a
  `[RelayCommand]` does not produce a bad report — it terminates the process.** The catch clauses were
  allow-lists of exception TYPES, and this VM reaches the world only through `DataImportEnvironment`'s
  delegates precisely so no Firebird type reaches a ViewModel (rule #1) — **that erasure cuts both ways: a
  component that talks to the world through delegates cannot enumerate the exceptions the world throws.** It
  could not even *name* `DdlExecutionException` without breaking its own layering. Now caught **by POSITION
  (this is the boundary), not by TYPE**, with `OperationCanceledException` kept separate and above (#253), at
  all 12 delegate seams, the other async commands, and the fire-and-forget recalculation chain (whose escapes
  became `UnobservedTaskException`). ⚠ This also hid a second, unreported crash: **any refused `CREATE TABLE`
  would have closed the app too**, since `DdlExecutionException` was on no list either.
  **Proof the guards work:** both fixes were temporarily reverted and the two new tests duly failed. The
  regression test throws a type nobody would put on an allow-list, from **every collaborator in turn**, so it
  fails the day anyone narrows a catch again.
- **✅ I8 — NEW TABLE: DELIVERED 2026-07-27, awaits the user's visual confirmation in both palettes.
  Next: I9 XLSX · I10 XLS · I11 named profiles · I12 close-out.** Branch `feat/data-import`, suite **5704
  green** (+97), build 0/0, app launches clean. **Live-verified:** `tools/probes/DataImportRunProbe` gained
  section **G** and runs **20/20 ALL PASS** against FB5 `WI-V5.0.3.1683`.
  **⭐ Four things worth carrying forward.**
  (1) ⭐⭐ **`ColumnTypeInferencer` owns no parser.** Every "could this value be a number / a date / a
  boolean?" is asked of **`ImportValueConverter`** — the same class that will convert the value during the
  real run, under the same culture. Not tidiness: an inferencer with its own idea of what an integer looks
  like would propose a type the converter then refuses, which is exactly R19's timebomb. Pinned by
  `EveryValueSeen_ConvertsIntoTheTypeThatWasProposedForIt`.
  (2) ⭐ **`ImportNewTable` is the ONE owner of "what does this column definition become".** The type text the
  preview validates against, the type text in the `CREATE TABLE`, and the type text the catalog reports
  afterwards are **the same string**, from one `DdlGenerator.FormatTypeOrDomain` call (§4.6 — no second
  generator, no second column model). Two producers would eventually disagree, and the disagreement would
  arrive as **rows rejected by a table the module itself designed**. Probe case **G4** checks it on the live
  engine: the catalog returned `VARCHAR(2), VARCHAR(8), NUMERIC(4,2), DATE` — exactly what was asked for.
  (3) ⭐⭐ **Projecting the new table as an `ImportTarget` (`ImportNewTable.Project`) is what gives "Validate"
  a table that does not exist — and that is its whole value.** The dry run answers "will these inferred types
  actually hold my file?" **at the one moment the answer is still free**: after the `CREATE` the table is
  committed and beyond a Rollback (§0.5 / gotcha #213). Mapping, readiness and the converted preview then
  work on a new table with **no special case at all** — from their side, a table that will exist and one that
  does are the same question. After a real `CREATE` the coordinator **re-reads the target from the CATALOG**:
  the projection is a prediction, the catalog is the fact.
  (4) ⭐ **A value with a leading zero is NOT a number — a §0.1 rule, not a parsing one.** `007` parses as 7
  without complaint, but the seven and the text are **different data**: a postal code, an index, an account
  number comes back different from what went in. That is rule #11 directly, so such a column becomes
  `VARCHAR`. A single leading zero (`0`, `0,5`) stays a number.
  ⚠ **Three deliberate boundaries:** nothing is ever inferred `NOT NULL` (the file in hand having no gaps
  says nothing about the next one, and the constraint outlives the import); **`SMALLINT` is never proposed**
  (right for this file, wrong for the next, and the difference from `INTEGER` is not worth a rejected row);
  and **`DOUBLE PRECISION` is not a candidate** (the only text it would accept and `NUMERIC` would not is
  scientific notation, which the converter refuses anyway — and choosing an approximate type for
  exact-looking values loses digits silently). Also new: **`IMP0028 NewTableAlreadyExists`** blocks a taken
  name *before* the run, because the `CREATE` is the first thing an import does and letting it through would
  mean a green strip followed instantly by a raw server error.
- **🏁 DATA IMPORT MVP IS COMPLETE — I0–I7.5 CLOSED AND USER-ACCEPTED (2026-07-26).**
- **⭐ I7.5 — DATA IMPORT HAS ITS OWN WORKING TRANSACTION (accepted). Ratified amendment to
  design §4.5, the module's "most important decision".** The deciding argument was not UX: while the import
  wrote into THE one user working transaction, its **Commit also persisted whatever the SQL Editor had left
  uncommitted**. A button must do exactly what it says (rule #11 / §0.5), so the possibility was removed
  rather than warned about. **Measured first** (`tools/probes/ImportTransactionIndependenceProbe`, FB5, 8/8):
  the driver refuses a second transaction on one `FbConnection`, but **two attachments give two fully
  independent transactions** — so "another independent transaction" means "another attachment", exactly as the
  debugger has done since D2. **Accepted cost:** a `SELECT` in the SQL Editor will NOT see imported rows before
  the import commits, and a same-row collision now fails immediately (SQLSTATE 40001 in ~28 ms under NOWAIT).
  **As-built, three things worth carrying.** (1) **`FirebirdSessionConnection` was extracted from
  `DebugSessionConnection` by COMPOSITION** — the debugger *holds* one instead of being one, so its public
  surface is byte-identical and its tests + `DebuggerFidelityProbe` stay an untouched regression proof of a
  closed subsystem. `ImportSessionConnection` joins on the same fundament. (2) **`ImportReadiness` stopped
  reporting the console's transaction at all** (IMP0021 retired, number never reused) — which dissolved a
  contradiction the design had carried since I2: §3.2 called an open working transaction BLOCKING while §4.5
  had the writer join one. (3) ⭐ **`PendingWorkRegistry` is the ONE owner of "does the application hold
  anything uncommitted".** The close/disconnect guards used to ask `_transactionService.IsActive` directly;
  with a second transaction that would have become a list of module names in the shell, and the module nobody
  remembers to add is the one that loses data. It is *not* an abstraction built for the future — the app
  already had this exact shape for editor work (`IUnsavedWorkSource`), and it ships with two real sources.
  **Deliberately NOT merged with editor work** (Save/Discard vs Commit/Rollback are different verbs), and the
  **debugger deliberately does not register** (spec §4.4 discards a debug run's writes by contract). ⚠ The
  **toolbar's** Commit stays the console's alone — making it settle the import would re-create the
  cross-module commit in the other direction. Live proof: `DataImportRunProbe` **13/13 ALL PASS**, case F —
  console leaves a row uncommitted, import commits, console rolls back, only the import survives.
  Commits `4bf6cf8` (A) · `019e7f8` (B) · `8766787` (C) · `bb28805` (D).

- **✅ I7 — CLOSED AND ACCEPTED (2026-07-26). The MVP surface:
  CSV/TXT → an existing table now imports end-to-end, with validation, a report, the transaction decision
  and a remembered configuration. Confirmed by the user in both palettes.** Branch `feat/data-import`, suite **5607 green** (+24), build 0/0, app launches clean.
  **Live-verified:** new `tools/probes/DataImportRunProbe` vs FB5 `WI-V5.0.3.1683` — **11/11 ALL PASS**
  (the report's numbers equal `SELECT COUNT(*)`; Rollback undoes the "empty first" DELETE together with the
  rows; the count behind that confirmation is read inside the user's own transaction; `Batched` really does
  commit every N and a later Rollback really cannot take those rows back; a dry run touches nothing).
  **⭐ Three things worth carrying forward.**
  (1) **The converted preview IS the real import, not an imitation of it.** §3.6 promises the grid shows
  "exactly what reaches the database", and that is only true because `ImportPipeline` fills it — same
  converter, validator, mapping and culture. Two additive Core pieces make that possible:
  **`BoundedImportProvider`** (a decorator bounding the READ, so a preview never reads a million rows to show
  a hundred) and **`PreviewImportWriter`** (a writer that KEEPS rows instead of sending them). Same discipline
  as "Validate is another argument, not another mode", one level up: **a different provider and a different
  writer, the same one import.** ⚠ A failed row shows its **RAW** values and that is not a half-measure — the
  pipeline stops a row at its first bad value, so such a row *has* no converted values, and the raw text is
  exactly what the user must fix.
  (2) **`CanValidate` is deliberately weaker than `CanRun`.** An open working transaction blocks the IMPORT
  but not the VALIDATION: a dry run writes nowhere, so refusing it would deny the one operation that helps
  most while the user is deciding what to do about that transaction. The rule lives in Core
  (`ImportReadinessReport.CanValidate`) — "what does this report permit" is that record's question, and a view
  deciding it would be a second opinion on readiness.
  (3) **`Batched`: `CommitEveryRows` is a FLOOR, not an exact multiple — measured live.** A commit can only
  land on a flush boundary (`BatchedCommitImportWriter` is a **decorator**, not a change to
  `FirebirdImportWriter`, so `Manual` and `AutoCommitOnSuccess` run byte-identical code), i.e. at the first
  batch boundary **at or past** N. With the I0-measured defaults (batch 500, commit 10 000) it lands exactly
  on 10 000; a commit interval *smaller* than the batch size yields one commit per batch. Shrinking the batch
  to match was rejected — I0 measured batch size as the thing that costs throughput while commit frequency is
  nearly free. Stated out loud because it is the number the report will be read against.
  Also new: **`DataImportEnvironment`** replaced five positional delegates with one named bundle (I7 adds six
  collaborators, and eleven positional arguments is where two get swapped silently), and
  **`FirebirdImportTargetPreparer`** (`COUNT(*)` + `DELETE FROM` on the **Data** lane inside the user's
  transaction — emptying a table is data, not schema, so it must roll back with the import).
  **I6 as-built — the Target section + the Mapping panel, and it *inserted into* the frame the I5 closing
  seam left rather than restructuring it.** New `ImportTargetSectionViewModel` (table picker fed from the
  **Metadata** lane, a facts line that **names** the BEFORE INSERT triggers rather than counting them — a
  count says something is there, the names say what will rewrite the values, R6 — and "empty the table
  first", off by default because it destroys data) and `ImportMappingPanelViewModel` + `…RowViewModel`
  (orientation **target → source**, because the target column is the side with requirements and the source
  field is the choice). The chain of §4.7 is now **source → target → mapping → readiness**, one cancellable
  sequence. ⚠ **Three things worth carrying forward.** (1) **A locked column is shown WITH its reason, never
  hidden** — a missing row is a question the user cannot even ask; the sentence comes from the ONE
  code→text table the readiness strip uses, so a column's note and the strip cannot drift. (2) **The
  sole-remaining-pair rule can reach an identity `GENERATED ALWAYS` column, and that is Core's ratified
  behaviour, not a bug**: `IsMappable` does not exclude identity and `Diagnose` then raises `IMP0007`
  ("an accent, not a fault — but never silent"), with the writer emitting `OVERRIDING SYSTEM VALUE` (I4). The
  UI lock governs a user reaching for that column *by hand*; when the planner paired it, the row shows
  unlocked and marked "assumed". **No Core change was made.** (3) **A defect caught by its own test:** the
  first cut cleared the record's `Mapping` whenever the target was null — so a restored profile lost its
  pairing merely because the target had not been read back yet. The grid clears; **the record never does**.
  **After I4 the whole ENGINE is built and live-verified** (`tools/probes/DataImportProbe` vs FB5
  `WI-V5.0.3.1683` — **20/20 ALL PASS**); from I5 on the work is the user interface only. Branch
  **`feat/data-import`** (pushed to **both** remotes), suite **5583 green**, build 0/0, app launches clean.
  **✅ I5 — CLOSED, user-confirmed after two visual reviews that produced U1–U12.** The reviews are the
  project's QA rule proving itself: build, green tests and a clean smoke had found **none** of it, because
  every finding is about **proportion and space**, not state. All twelve are settled and recorded in
  [data-import.md §3.8](docs/design/data-import.md); **ten shipped in one closing seam**, two stay
  deliberately open (**U4** global density → the app-wide UX sprint below; **U5** responsiveness →
  re-checked during I6, when Target and Mapping actually occupy space).
  **⭐ The layout was revised and carried into §3.1 in place — the single change that matters is that the
  STAR now belongs to the work, not the configuration.** Until the review the configuration `ScrollViewer`
  was the `*` row and the preview was nailed to 190 px — backwards, and the whole reason the preview was
  "practically invisible"; the work area now takes everything left (~500–540 px where it had none). With it:
  band A **deleted** (no other module carries a header; the one fact it was meant to add — connection + lane
  — moved to band H) · sections became **stacked one-row tiles, not side-by-side panels**, restoring the
  top-down reading order §1.2 names as one of the two things kept from the wizard · and each tile splits by
  **how often a decision changes**, so the file/table picker stays live at all times and only "Format
  options" folds — side-by-side had optimised the *rare* state (both expanded) at the cost of the *constant*
  one, and folding the picker away would have broken §1.2's promise that a repeat import is one `F5`.
  Also: readiness findings capped at 3 + "…and N more" with the chips always intact (U6) · a `GridSplitter`
  with double-click collapse and a **persisted** height (U2) · **U11** — the format options now collapse
  themselves once a source reads, unless the user opened them by hand · the ragged-row marker finally
  painted (U8 — it was computed and pinned by a ⭐ test but nothing rendered it, gotcha #233's shape) ·
  `Ctrl+O` (U10, only shortcuts that have something to drive) · **U12 — a shared settings-group idiom**:
  new `Border.settings-group` (a recessed card) + `TextBlock.group-header` in `ControlStyles.axaml`, because
  two captions floating between control rows read as captions, not as groups; the header outweighs
  `field-label` on purpose (a field caption names one *value*, a group header names a *subject*, and at equal
  weight they compete). It also removed the **dead** `Border.panel` style — zero consumers app-wide, and a
  near-identical sibling beside the new one is how the wrong one gets picked later.
  ⚠ **Two rules the seam established:**
  the panel height lives in `WorkspaceState.ImportPreviewPanelHeight` (global, like `ResultsPanelHeight`,
  because an import tab is deliberately transient) and **must never enter `ImportConfiguration`** — a layout
  preference is not an import decision (§4.8.2), and the reflection guard would otherwise ship a pixel height
  inside saved profiles; and **no module etap touches `Themes/ControlStyles.axaml`** (density = the separate
  sprint). Suite **5568 green** at that point, build 0/0. **The I6 opening prompt said "insert into the
  finished frame, don't rebuild it" — and that is what it did.** A new tool tab (toolbar, beside
  the Script Executor) that imports Clipboard / TXT / CSV / XLSX into an existing or a newly created table.
  **Read [docs/design/data-import.md](docs/design/data-import.md) — its „📍 STAN IMPLEMENTACJI" block is the
  handover, and the architecture is 🔒 FROZEN: from etap I1 on it is implementation only, and an
  implementation discovery that genuinely undermines the design means STOP THE ETAP AND REPORT, never a quiet
  redesign.** Shape worth knowing before touching it: **one working surface with collapsible sections, NOT a
  wizard** (the user runs the same import repeatedly; the gate is a readiness strip modelled on
  `DebugPreflight`, not Next buttons) · **one pipeline for every source** (a provider yields
  `SourceSchema` + `RawRecord`; the clipboard is not a second parser) · **`ImportConfiguration` is the single
  representation of every user decision** — surface state, pipeline input and profile payload are the same
  record, enforced by a reflection round-trip test that fails the suite when a new setting bypasses it ·
  **rows go to the Data lane as the ONE user working transaction, `CREATE TABLE` to the Ddl lane** (gotcha
  #213 — and the UI says out loud that Rollback will not remove that table). Etap I0 measured three
  corrections into the design; the one that generalises beyond this module is that **a character outside the
  CONNECTION charset is silently written as `?`** (see the spawned platform-wide audit below).
  **I2 as-built (Core only, still zero Avalonia / zero `FirebirdSql` / zero UI):** `ImportValueConverter`
  (strict — a value it cannot be *certain* of is a row error, never a guess) · `ImportRowValidator` +
  **`ImportCharsetGuard`** (NOT NULL / length / precision+scale / **connection-charset representability with
  `EncoderExceptionFallback`** — R1's mandatory guard) · `ImportMappingPlanner` (name-proof matching, the
  sole-remaining-pair rule, `Diagnose`, `Project`) · `ImportReadiness` (the §3.2 strip as a pure function).
  Two foundations came out of the one-owner rule: **`ImportTargetType`** — the single answer to "what type is
  this target column", reusing `SqlValueKind` *in the reverse direction* per §4.6, with an Unknown set kept
  **identical to the export side's** — and **`ImportDiagnostics`** (`IMP0001`–`IMP0027`, codes never messages).
  ⚠ **`ImportErrorKind` gained three client-side members** (`ValueOutOfRange`, `PrecisionWouldBeLost`,
  `UnsupportedTargetType`): additive, and forced by I2's own DoD — "zero silent conversions" cannot be met
  while rounding `1.555` into `NUMERIC(15,2)` has no way to be reported. **No "round it anyway" option was
  invented** (that is a design decision, and §0.1 defaults to refusal).
  **I3 as-built:** `ImportPipeline` — the ONE import, which knows neither what it is reading (a provider made
  the `RawRecord`s) nor whether it is writing (**"Validate" is `DryRunImportWriter` passed as an argument, not
  a second mode**, so a dry run cannot drift from the real thing — there is no second path to drift). It owns
  the **"batch index → source row number" window** (decision D9): a batched write reports failures by position
  in the batch, and the report must name the row the user can find in their file, so the translation happens
  once, here, and nothing downstream ever sees a batch index. Plus `DryRunImportWriter` (a product feature, not
  a test double) and the owed `DelimitedTextImportProvider` (CSV / TXT / **clipboard** — one provider, three
  origins; it resolves the NULL token, which is a property of reading a text field). ⚠ **`ImportOutcome` gained
  `Warnings`/`WarningsTruncated`** (init-only ⇒ additive): §0.2 requires every SHORTENED row in the report, and
  such a row is neither an error (it went in) nor silence (data was lost) — folding it into `Errors` would make
  `RowsFailed` count rows that actually succeeded. Two behaviours worth knowing: the tail flush runs on an
  **uncancelled** token so rows the writer already accepted stay attributable (§0.6, the gotcha-#253
  discipline), and the pipeline **refuses to start** rather than guessing when the mapping names a column the
  target does not have.
  **I4 as-built (the first import code that touches a database):** `FirebirdImportWriter` (`FbBatchCommand`;
  **`MultiError` maps 1:1 onto `ImportErrorPolicy`** so the policy is enforced by the server round trip rather
  than re-implemented client-side; `OVERRIDING SYSTEM VALUE` for a mapped ALWAYS identity; `CommandLock` per
  batch; Data lane, the user's working transaction, **auto-begin and never auto-commit**) ·
  `FirebirdImportTargetReader` (a thin adapter — columns come from the existing
  `FirebirdMetadataReader.ListColumnsAsync`, the codebase's one owner of that question; the only thing it adds
  is the BEFORE INSERT trigger list, decoded through the shared `DecodeTriggerHeader` because
  `RDB$TRIGGER_TYPE` is bit-encoded) · **`FirebirdImportErrorMapper` — the GDS-vector classifier.**
  ⚠ **Two live findings worth carrying forward.** (1) **A standalone `CREATE UNIQUE INDEX` violation leads
  with GDS `335544349`, NOT the `335544665` a PK/UNIQUE *constraint* reports** — I0 measured only the
  constraint form, so until the live run the import reported a duplicate key as a generic `ServerError`
  (gotcha #260; the I0 findings table is corrected in place). (2) **The client guards now fire first for
  NOT NULL, length and numeric range**, so those server branches are only reachable through a trigger — which
  is why the lab gained `IMP_SRV` and its `IMP_SRV_BI` trigger, the only way to exercise the
  `335544321` three-way split against a real engine.
  **I5 as-built (the first import code you can see):** `WorkspaceTabKind.DataImport` + a new **`Icon.Import`**
  (an arrow descending INTO a table grid — deliberately not `Icon.Download`, whose tray means "fetch a file to
  disk"; canonical `.svg` + geometry per the D15.2 icon rule) + a toolbar button beside the Script Executor,
  near-singleton, **added to the `SnapshotCurrentTabs` skip list** (omitting it would not merely fail to
  restore the tab — it would fall through and be captured as a `Ddl` tab, the exact bug the Debugger tab had).
  `DataImportTabViewModel` is the **single owner of `ImportConfiguration`**; `BuildConfiguration`/
  `ApplyConfiguration` is the only UI⇄record translation point (§4.8.6), and sections that do not exist yet
  are **passed through unchanged** so a newer build's profile is not quietly degraded by an older one. The
  readiness strip is a **pure projection** of Core's `ImportReadiness` that maps severity through the shared
  `MessageBanner.BrushKeyFor/GeometryKeyFor` table (§9.3) — no second brush map. ⚠ **Two things worth
  carrying:** detection order is **encoding → delimiter** (a delimiter is looked for in text something already
  decoded, so the reverse would be a guess resting on a guess), and the source preview's "ragged row" marker
  compares against the **majority** field count, not the schema's — the schema reports the WIDEST record so
  every column stays mappable, and marking against that inverts the signal (one row with an extra field would
  flag all the good ones). **Next: etap I6** (the Target section + the Mapping panel) — **gated on §3.8
  being settled first.**
---

## I12 — domknięcie modułu (2026-07-27)

Etap, który **nic nie dokłada**: dokumentacja, audyt UI w obu paletach, pomiar na 1 M wierszy, jawna
lista tego, co zostaje otwarte.

### ⭐ Pomiar na 1 M wierszy — pierwszy w historii modułu

I0 wybrał domyślne **batch 500 / commit co 10 000** na małych paczkach; **nikt nigdy nie przepuścił przez
ten moduł miliona wierszy**. Sonda `tools/probes/ImportMillionRowProbe` (jednorazowa, usunięta przy
zamknięciu etapu) przepuściła 36,6 MB CSV do `IMP_TARGET` na żywym FB5 `WI-V5.0.3.1683`.

| | Manual (jedna transakcja) | Batched (commit co 10 000) |
|---|---|---|
| czas | **14,0 s** | 19,6 s |
| przepustowość | **71 437 wierszy/s** | 51 005 wierszy/s |
| przeżyło Rollback | — (commit na końcu) | **990 000 z 999 997** |

**Domyślne bronią się, i to jest jawne stwierdzenie, nie wrażenie.** Batch 500 daje ~71 tys. wierszy/s;
commit co 10 000 kosztuje **40% przepustowości**, ale to cena *trybu*, nie liczby — Batched z definicji
płaci za trwałość częściowego wyniku. 990 000 z 999 997, które przeżyły Rollback, potwierdza na żywo to,
co moduł mówi na głos: interwał commitu jest **podłogą, nie dokładną wielokrotnością** — ostatnie 9 997
wierszy siedziało w niezatwierdzonym ogonie.

**⭐ Kształt krzywej jest dowodem, nie liczba końcowa** (lekcja z R8 w I10). Sterta zarządzana po
wymuszonym pełnym zbieraniu, mierzona co 200 000 wierszy:

```
   200 000   1,1 MB       600 001   1,1 MB       1 000 000   0,6 MB
   400 001   0,7 MB       800 001   1,1 MB
```

**Płaska.** Sterta rosnąca liniowo z liczbą wierszy znaczyłaby, że coś materializuje źródło; ta nie rośnie
w ogóle — `DelimitedTextReader` strumieniuje z konstrukcji (R8), a szczytowy working set całego procesu to
66,6 MB przy pliku 36,6 MB. Pomiar sterty biegnie **osobnym przebiegiem**, bo wymuszanie kolekcji w
przebiegu mierzonym na czas mierzyłoby kolekcje.

**Raport pozostaje uczciwy przy tej skali:** trzy celowo popsute wiersze na znanych pozycjach zostały
zgłoszone co do jednego, z **numerami rekordów**, które użytkownik otworzy w swoim pliku (§0.6); sufit
listy błędów (1 000) nie został ruszony i uczciwie raportuje `ErrorsTruncated = false`; licznik postępu
doszedł do 1 000 000.

⚠ **Pierwsza wersja sondy pokazała rozbieżność +1 i to sonda była w błędzie, nie moduł.**
`DelimitedRecord.RecordNumber` liczy od początku **pliku**, więc drugi wiersz danych jest rekordem 3 —
nagłówek jest rekordem 1. Tak ma być: to jest numer, który użytkownik widzi w edytorze, i kontrakt typu
mówi to wprost. Dokładnie lekcja z I10: **gdy sonda i produkt się nie zgadzają, najpierw sprawdź, czy
sonda odtworzyła całą drogę** — tu: czy używa tej samej numeracji.

### Audyt UI — obie palety

Sprawdzone na wszystkich powierzchniach modułu (źródło, format, cel, mapowanie, podgląd po konwersji,
raport, pasek gotowości, pasek poleceń z selektorem profili, dialog potwierdzenia i dialog nazwy profilu):

| Co | Wynik |
|---|---|
| twarde kolory (`#RRGGBB`, nazwane) | **brak** |
| lokalne definicje pędzli (`<SolidColorBrush>`, `new SolidColorBrush`, `Brushes.X`) | **brak**, także w code-behind i VM |
| `{StaticResource}` na pędzlu | **brak** — wszystko `{DynamicResource}` |
| lokalne bloki `<Style>` w widokach modułu | **brak** — 9 klas współdzielonych z `ControlStyles.axaml` |
| `Themes/ControlStyles.axaml` ruszony w I6–I12 | **nie** (ostatnia zmiana to `settings-group` z I5) |
| wyszukanie pędzla bez `ThemeVariant` (gotcha #250) | **brak** — 5 użyć konwertera, 5 wiązań wariantu |
| tokeny motywu obecne w OBU paletach | **13/13** |

⭐ **Audyt jest powtarzalny, nie jednorazowy.** Mechaniczna połowa jest własnością XAML-a i daje się
przeczytać; druga nie — `{DynamicResource}` rozwiązuje się w czasie działania, per paleta, więc token
dodany do Dark i zapomniany w Light kompiluje się, renderuje w palecie autora i **nie maluje nic** w
drugiej. Dlatego lista trzynastu tokenów jest **przypięta testem**
(`DataImportSurface_EveryThemeToken_ResolvesInBothPalettes`), a nie zostawiona do ponownego grepowania.

### Ostatnia poprawka funkcjonalna — nieświeża lista tabel

Zgłoszona przez użytkownika: tabela utworzona przez import pojawia się w drzewie metadanych, ale **nie na
liście „Existing table" w tej samej zakładce** — dopiero ponowne otwarcie zakładki ją pokazywało.

Potwierdzone i naprawione, ale symptom miał **groźniejszą połowę, której zgłoszenie nie obejmowało**:
`IsNewTableNameTaken` czyta tę samą listę, więc **`IMP0028` przestawał widzieć, że nazwa jest zajęta** —
ponowne uruchomienie tego samego importu pokazywało **zielony pasek gotowości, a zaraz po nim surowy błąd
serwera**, czyli dokładnie stan, któremu IMP0028 ma zapobiegać (mówi to we własnym komentarzu).

Naprawione tak, jak w warstwie wyżej naprawiono drzewo metadanych: **modułowi znana jest nazwa** tabeli,
którą właśnie utworzył, więc `NoteTableExists` / `NoteTableGone` **łatają listę w miejscu**, na
posortowanej pozycji, bez ani jednego zapytania do katalogu. To fakt („ta tabela istnieje"), nigdy
polecenie („odśwież"). Oba objawy przypięte testami, a testy sprawdzone przez **tymczasowe cofnięcie
poprawki** — padły oba.

### Co zostaje otwarte po zamknięciu modułu

| Pozycja | Dlaczego zostaje |
|---|---|
| **U4 — gęstość kontrolek** | globalna, nie modułowa: `ControlStyles.axaml` nie ma ani jednego stylu domyślnego dla `TextBox`/`ComboBox`/`CheckBox`/`Button`. Ratyfikowany **sprint UX całego EmberTerna** po module, projektowany z oglądu wszystkich powierzchni naraz |
| Pozostałe życzenia UX z przeglądów I11 | ta sama decyzja użytkownika — do sprintu UX, nie do modułu |
| Kolumna „Podstawa" nie odzyskuje szerokości po ukryciu | proporcje `3*` są wspólne dla nagłówka i wierszy; zwinięcie wymaga konwertera `bool → GridLength`, czyli nowego typu na zamknięciu modułu |
| **Eksport/import profilu do `.json`** | opcjonalny w §6, poza DoD I11: wymaga nowego serializatora w Core i dwóch szwów w widoku — **powiększyłby powierzchnię, którą I11 istniał po to, żeby poświadczyć jako nietkniętą** |
| **Znacznik „zmodyfikowany" przy profilu** | wymaga kanonicznego porównania dwóch `ImportConfiguration`, a rekord porównuje listy przez referencję — mapowanie jest nową instancją po każdym przeliczeniu, więc znacznik zapalałby się natychmiast po wczytaniu. To byłaby zmiana modelu, której I11 dowiódł jako niepotrzebnej |
| **Audyt cichej utraty znaków spoza charsetu połączenia** | zmierzony platformowy defekt (znak spoza charsetu **połączenia** zapisuje się jako `?` bez błędu, nawet do kolumny UTF8). ONE wspólny strażnik, nigdy poprawki per moduł — osobne zadanie architektoniczne |
| **Zawieszenie pełnego zestawu testów** (#94/#226/#261) | osobne zadanie infrastrukturalne; instrument (`--blame-hang`) wskazał podejrzanego, ale jedna obserwacja to za mało, żeby przebudowywać na jej podstawie |
| Grupowanie niezależnych DDL w jednym segmencie | nie ten moduł (Script Executor, §5.1) |
