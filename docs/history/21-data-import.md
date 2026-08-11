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

⚠ **Wpisy są zamrożone w swojej chwili.** Liczba testów, „oczekuje potwierdzenia", „następny etap" i podobne
zdania opisują stan **wtedy**, nie teraz — to jest pamiętnik, nie stan bieżący. Stan bieżący opisuje CLAUDE.md,
a obowiązującą architekturę [docs/design/data-import.md](../design/data-import.md). Moduł jest **zamknięty,
zaakceptowany i scalony do `master` (2026-07-27)**, a końcowy zestaw testów to **5856 zielonych**.

- **🔁 DATA IMPORT — THIRD REVIEW ROUND DELIVERED (2026-07-27), awaits the user's visual confirmation; then I12.**
  Branch `feat/data-import`, suite **5851 green** (+5), build 0/0, smoke clean. A full ergonomics audit produced
  more findings; the user selected **four** and explicitly declined the rest — including the `Existing table` /
  `New table` layout, which they re-examined and kept, because the options appearing to the right of each
  variant justify it. **No model, no pipeline, no converter, no validator, no planner, no writer was touched** —
  the fourth etap in a row that has held.
  **⛔ REVERTED THE SAME DAY — U5 STAYS OPEN. The diagnosis below holds; the cure did not.** Honouring the
  floor made the bottom panel give way, the middle of the window grew, and the panel stopped being useful; at
  today's control heights you cannot have both, so this is a density problem in a layout problem's clothes and
  it goes to the UX sprint. Do not re-add the floor. The paragraph is kept because the diagnosis is worth
  having — see the closing table and gotcha #274's postscript.
  **⭐⭐ The diagnosis: it IS a layout defect rather than a matter of taste.** Every band except the work
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
  [data-import.md §3.8](../design/data-import.md); **ten shipped in one closing seam**, two stay
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
  **Read [docs/design/data-import.md](../design/data-import.md) — its „📍 STAN IMPLEMENTACJI" block is the
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

### ⭐⭐ Podział konfiguracja / wynik — lekcja z trzech podejść do jednego objawu

Objaw był przez cały czas ten sam: w wariancie „New table" środek okna jest za ciasny. Trzy podejścia:

1. **Podłoga na wierszu roboczym** (`MinHeight` + zacisk dolnego panelu). Zamykała U5 dosłownie, ale żeby
   podłoga była osiągalna, ustąpić musiał dolny panel — więc jeden panel odzyskał miejsce kosztem drugiego.
   **Cofnięte przez użytkownika po obejrzeniu w działaniu.**
2. **Odłożenie do sprintu UX** — „gdy kontrolki zmaleją, problem zniknie". Prawdziwe, ale to była zgoda na
   życie z wadą, a nie jej rozpoznanie.
3. **Podział odpowiedzialności** — i to była właściwa odpowiedź, postawiona przez użytkownika:

> „Preview jest efektem importu, a nie konfiguracji. Typy i Mapping są elementami konfiguracji. Obecnie
> mieszamy konfigurację z wynikiem i oba panele walczą o tę samą przestrzeń."

To rozstrzyga wszystko, czego dwa poprzednie podejścia nie umiały ruszyć. „Preview after conversion" jest
potrzebny w **obu** wariantach celu i jest **wynikiem** — schodzi do dolnych zakładek, obok Source preview,
Errors i Report, czyli tam, gdzie już mieszkają wszystkie wyniki. Powierzchnia robocza zostaje **w całości
konfiguracji**:

```
Existing table:   [ Mapping — pełna szerokość                    ]
New table:        [ Typy tabeli   │   Mapping ]     (splitter)
dół (obie):       Source preview · Preview after conversion · Errors · Report
```

Siatka typów i podgląd DDL wyszły przy okazji z pasa `Auto` do lewej połowy powierzchni roboczej — a to
właśnie ten pas był najwyższą rzeczą na ekranie i przyczyną pierwotnego objawu. Pas Cel jest znów cienki:
wybór wariantu, nazwa, opcje i linia faktów.

⭐ **Lekcja, która wychodzi poza ten moduł:** gdy dwa panele biją się o miejsce, warto najpierw zapytać, czy
oba należą do tej samej **kategorii odpowiedzialności**. Tutaj jeden był decyzją, drugi jej skutkiem —
i dopóki stały obok siebie, każda odpowiedź w wymiarze „ile pikseli" musiała być kompromisem. Żadna zmiana
wysokości nie naprawiłaby złego przydziału.

⚠ Oraz `ReportTabIndex` przesunął się z 2 na 3 — zaszyty indeks, którego nie przesunie się razem z paskiem
zakładek, wysyła zakończony bieg na złą zakładkę.

#### Dokończenie: DDL wychodzi z powierzchni, a siatka typów odzyskuje pełną wysokość

Pierwszy przebieg podziału zabrał podgląd DDL **razem z typami** do lewej połowy — konsekwentnie (to jest to,
w co typy się kompilują), ale wciąż wewnątrz powierzchni. Przy oglądzie wyszły dwie rzeczy:

⚠ **Błąd układu, i to mój.** `MaxHeight="170"` na scrollerze siatki typów **przetrwał przeprowadzkę**, choć
komentarz obok twierdził, że go nie ma. Objaw był mylący: kontrolka ze `Stretch`, która nie może zająć całej
dostępnej wysokości, zostaje **wyśrodkowana** — więc siatka pływała z pustymi pasami nad i pod sobą, co czytało
się jak „miejsce wciąż zarezerwowane dla DDL". Wszystkie właściwości wyglądały poprawnie. **Lekcja: komentarz
twierdzący, że coś usunięto, nie jest dowodem, że usunięto — sprawdź kod, nie prozę przy nim.**

⭐ **A potem właściwe miejsce dla DDL — po dwóch gorszych odpowiedziach.** Wyjściowy problem był o proporcji:
DDL czyta się **sporadycznie**, ale jego panel komplikował tę kolumnę **na stałe** — siatka typów dzieliła
miejsce z czymś niemal zawsze pustym, a każde pytanie o wysokość musiało uwzględniać ujawnienie, którego nikt
nie otworzył.

**Odpowiedź pierwsza: „Show DDL" otwiera instrukcję w SQL Editorze** jako nowe zapytanie zapisane, przez tę
samą ścieżkę co monitor Trace. Technicznie czysto — jedna ścieżka, dwóch wywołujących, nic nadpisywanego —
i **odrzucone przez użytkownika jako regres UX**:

> „DDL jest elementem konfiguracji importu. Kliknięcie »Show DDL« nie powinno przełączać użytkownika do innego
> modułu ani zmieniać aktywnej zakładki. W czasie konfiguracji importu chcę pozostać w module Data Import."

⭐⭐ **Odpowiedź właściwa: DDL jest piątą zakładką dolnego panelu.** To jest ta sama zasada, która chwilę
wcześniej rozstrzygnęła cały układ, tylko zastosowana konsekwentnie do końca: **góra = konfiguracja, dół =
wyniki i artefakty.** Wygenerowany `CREATE TABLE` jest artefaktem tej konfiguracji, więc mieszka obok Source
preview, Preview after conversion, Errors i Report — nie zajmuje miejsca, dopóki nie zostanie wybrany, i nigdzie
nie nawiguje.

Konsekwencje, które czynią to rozwiązanie prostszym od obu poprzednich:

* **nie ma przycisku, komendy ani stanu** mówiącego, czy DDL jest na ekranie — jest zakładką jak każda inna;
* **jest ŻYWE**: renderuje `CreateTableSql`, liczone z siatki typów przez to samo `ImportNewTable.BuildCreateSql`,
  które wywoła sam bieg. Nie ma czego odświeżać i nie ma jak się rozjechać;
* zakładka pokazuje się **tylko w wariancie „New table"** — w drugim nie ma czego generować, a trwale pusta
  zakładka to obietnica, której nic nie dotrzymuje;
* stoi **na końcu paska**, bo ukryta zakładka i tak zajmuje swój indeks, a zakończony bieg wysuwa Raport
  po indeksie;
* zaznaczanie wystarcza do skopiowania — własny przycisk „Kopiuj" byłby czwartym sposobem na to samo.

⭐ **I ostatnia poprawka, bardziej o spójności aplikacji niż o module:** zakładka renderuje DDL **tą samą
kontrolką, którą EmberTern pokazuje SQL wszędzie indziej** — `AvaloniaEdit.TextEditor` z
`SqlEditorBehavior.AttachReadOnlyHighlighting`, dokładnie jak jedenaście pozostałych podglądów DDL w edytorach
obiektów. Czyli: wspólna warstwa leksykalna (XSHD dla obu palet) **plus** warstwa semantyczna z tego samego
frontendu Lexer → Parser → SemanticModel. Żadnego drugiego renderera SQL — użyty jest istniejący szew.

To nie jest kosmetyka w rozumieniu „ładniej": `SelectableTextBlock` czynił z tej zakładki **jedyne miejsce w
aplikacji, gdzie SQL jest bezbarwny**, a przy szerokiej tabeli `CREATE TABLE` to kilkadziesiąt wierszy, które
bez kolorowania czyta się znacznie gorzej. ⚠ Read-only w mocnym sensie, jaki gwarantuje sam szew:
`AttachReadOnlyHighlighting` **celowo nie podpina** uzupełniania, squiggli ani ergonomii pisania — podgląd nie
może proponować edycji tego, co tylko pokazuje. Numery wierszy wyłączone (to jedna instrukcja, nie dokument).
Tekst jest **wpychany** z VM, nie wiązany — dwukierunkowe wiązanie `TextEditor.Text` jest zawodne i obchodzi je
tak samo każdy inny podgląd DDL; strażnik przed zapisem niezmienionej wartości ma tu dodatkowy sens, bo DDL
przelicza się przy każdej edycji siatki, a ponowne przypisanie tego samego tekstu resetowałoby zaznaczenie
czytającemu pod ręką.

⚠ Jedna pułapka po drodze: `CreateTableSql` liczy się z siatki, ale nazwa tabeli mieszka na **innej**
właściwości, więc bez własnego `NotifyPropertyChangedFor` zakładka pokazywałaby starą nazwę, gdy pole wyżej ma
już nową. Przypięte testem, który sprawdza obie strony — typ i nazwę.

Usunięte jako martwe wraz z pierwszą odpowiedzią: `DataImportEnvironment.OpenSqlInEditor`,
`ShowCreateTableDdlCommand` i uogólnienie `OpenSqlAsSavedQuery` (wróciło do `OnTraceOpenInEditor` — uogólnienie
z jednym wywołującym jest resztką po niedoszłym drugim).

### Co zostaje otwarte po zamknięciu modułu

| Pozycja | Dlaczego zostaje |
|---|---|
| **U4 — gęstość kontrolek** | globalna, nie modułowa: `ControlStyles.axaml` nie ma ani jednego stylu domyślnego dla `TextBox`/`ComboBox`/`CheckBox`/`Button`. Ratyfikowany **sprint UX całego EmberTerna** po module, projektowany z oglądu wszystkich powierzchni naraz |
| ~~**U5 — powierzchnia robocza bez podłogi**~~ | ⭐⭐ **ROZWIĄZANY przez podział odpowiedzialności (2026-07-27), nie przez wysokości** — patrz sekcja niżej. Zapis poniżej zostaje jako przestroga: ⭐ **Rozpoznanie było prawdziwe, lekarstwo zostało cofnięte.** Wiersz roboczy jest jedyną gwiazdką wśród pasów `Auto`, więc w wariancie „New table" kafelek Cel potrafi zdusić Mapowanie i podgląd do zera. Podłoga (`MinHeight` + zacisk dolnego panelu) weszła na jedną rundę przeglądu i **użytkownik ją cofnął po obejrzeniu w działaniu**: żeby ją uszanować, dolny panel musiał ustąpić, przez co środek okna urósł, a dolny panel przestał być użyteczny. Przy dzisiejszych wysokościach kontrolek nie da się mieć obu naraz — **to jest problem gęstości przebrany za problem układu**, więc idzie do sprintu UX, gdzie powierzchnia odzyska ~100 px. ⛔ Nie przywracać podłogi, nie dokładać kolejnej sekcji pionowej |
| Pozostałe życzenia UX z przeglądów I11 | ta sama decyzja użytkownika — do sprintu UX, nie do modułu |
| Kolumna „Podstawa" nie odzyskuje szerokości po ukryciu | proporcje `3*` są wspólne dla nagłówka i wierszy; zwinięcie wymaga konwertera `bool → GridLength`, czyli nowego typu na zamknięciu modułu |
| **Eksport/import profilu do `.json`** | opcjonalny w §6, poza DoD I11: wymaga nowego serializatora w Core i dwóch szwów w widoku — **powiększyłby powierzchnię, którą I11 istniał po to, żeby poświadczyć jako nietkniętą** |
| **Znacznik „zmodyfikowany" przy profilu** | wymaga kanonicznego porównania dwóch `ImportConfiguration`, a rekord porównuje listy przez referencję — mapowanie jest nową instancją po każdym przeliczeniu, więc znacznik zapalałby się natychmiast po wczytaniu. To byłaby zmiana modelu, której I11 dowiódł jako niepotrzebnej |
| **Audyt cichej utraty znaków spoza charsetu połączenia** | zmierzony platformowy defekt (znak spoza charsetu **połączenia** zapisuje się jako `?` bez błędu, nawet do kolumny UTF8). ONE wspólny strażnik, nigdy poprawki per moduł — osobne zadanie architektoniczne |
| **Zawieszenie pełnego zestawu testów** (#94/#226/#261) | osobne zadanie infrastrukturalne; instrument (`--blame-hang`) wskazał podejrzanego, ale jedna obserwacja to za mało, żeby przebudowywać na jej podstawie |
| Grupowanie niezależnych DDL w jednym segmencie | nie ten moduł (Script Executor, §5.1) |

---

## Zapis etapowy przeniesiony z dokumentu projektowego (domknięcie, 2026-07-27)

Poniższe sekcje żyły w `docs/design/data-import.md`, dopóki moduł był w budowie. Po jego zamknięciu dokument
projektowy został sprowadzony do samej architektury, a to — as-built kolejnych etapów, przeglądy wzrokowe,
inwentarze testów, odstępstwa od szkicu i plan etapów — przeniosło się tutaj **bez skrótów**.

## 📍 STAN IMPLEMENTACJI — czytaj to pierwsze (aktualizowane po każdym etapie)

| | |
|---|---|
| **Gałąź** | `feat/data-import` (odbita od `master` @ `d474b42`); **wypchnięta na `origin`**, `private` do dosłania przy najbliższym zamknięciu etapu. Żywe gałęzie repozytorium: `master` + `feat/data-import` |
| **Ostatni commit** | etap **I8** — `b95af9a`, + **poprawka awarii z przeglądu `9f2817a`** (poprzedni: `dc653b1` — domknięcie MVP I0–I7.5). **Wypchnięte na OBA remote'y.** ℹ️ Przy pierwszym zamknięciu etapu `origin` był nieosiągalny (DNS nie rozwiązywał hosta — maszyna poza siecią firmową), a `private` przeszedł: dokładnie ta izolacja awarii, dla której odrzucono wariant z dwoma `pushurl`. Dosłane po powrocie do sieci |
| **Etapy zamknięte** | **I0** (sondy, `5e90435`) · **I1** (modele, konfiguracja, magazyn, czytnik, `77eb997`) · **I2** (konwersja, mapowanie, walidacja, gotowość, `392850f`) · **I3** (pipeline + dry-run + provider, `434daeb`) · **I4** (Firebird + weryfikacja na żywym FB5, `3b31a4d`) · **I5** (`0c5667e` + szew `95ae39e`) · **I6** (`4f2de74`) |
| **✅ I5 — ZAMKNIĘTY (2026-07-26)** | Przegląd wzrokowy dał 5 uwag (U1–U5) + 5 propozycji z autoprzeglądu (U6–U10) + U11 + U12 z drugiego oglądu. **Wszystkie rozstrzygnięte; 10 dostarczonych w szwie domykającym** (§3.8). Układ **zrewidowany i wniesiony w miejsce do §3.1** — gwiazdka na powierzchni roboczej, pas A usunięty, kafelki pionowe z zawsze żywym pickerem, grupy ustawień jako karty. **Układ zaakceptowany przez użytkownika.** Otwarte świadomie: **U4** (gęstość globalna → sprint UX po module) i **U5** (weryfikacja przy I6) |
| **✅ I6 — ZAMKNIĘTY (2026-07-26)** | Sekcja **Cel** (istniejąca tabela) + panel **Mapowanie** + łańcuch przeliczeń rozszerzony o cel i mapowanie. Potwierdzony wzrokowo przez użytkownika; odstępstwo o `COUNT(*)`, nazwy triggerów, orientacja „cel → źródło" i reguły identity — **zaakceptowane bez zmian** |
| **✅ I7 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-26)** | Podgląd po konwersji + pasek poleceń (`Importuj`/F5, `Waliduj`/Ctrl+F5, `Anuluj`/Esc, tryb transakcji, polityka błędów, `ExecutionTimer`) + uruchomienie z postępem i anulowaniem + zakładki **Błędy** i **Raport** + Commit/Rollback w raporcie + eksport raportu przez istniejący framework + „ostatnio użyta" konfiguracja + domknięta zaległość I6 (liczba rekordów przy „opróżnij tabelę"). **KONIEC MVP** |
| **✅ I7.5 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-26)** | Data Import ma **własną transakcję na własnym przyłączeniu** (amendment §4.5): wspólny `FirebirdSessionConnection` wydzielony z Debuggera przez kompozycję, `ImportSessionConnection`, `PendingWorkRegistry` jako **jedyny właściciel** pytania o niezatwierdzoną pracę. Sonda **13/13 ALL PASS** |
| **✅ I8 — DOSTARCZONY (2026-07-27), oczekuje potwierdzenia wzrokowego** | Nowa tabela: `ColumnTypeInferencer` (skan CAŁEGO źródła), `ImportNewTable` (jedyny właściciel „definicja → SQL"), edytowalna siatka typów z kolumną „Podstawa", podgląd DDL, `CREATE` na linii **Ddl** przed pierwszym wierszem, `DROP` przy niepowodzeniu, `IMP0028` |
| **🔴 I8 — POPRAWKA PO PRZEGLĄDZIE (2026-07-27): „Importuj" na nowej tabeli ZAMYKAŁO APLIKACJĘ** | Zgłoszone przez użytkownika; **dwa niezależne defekty, oba w I8**, oba naprawione i zapinowane. Szczegóły w bloku niżej |
| **✅ I8 — DOMKNIĘCIE PO PRZEGLĄDZIE (2026-07-27): nowa tabela pojawia się w drzewie od razu** | Drugie zgłoszenie z ręcznego QA: import przechodził, tabela powstawała, dane wchodziły — ale **Explorer metadanych jej nie pokazywał** do ręcznego odświeżenia. Naprawione **bez 21. wywołania `RefreshAsync()`**: moduł zgłasza fakt (`DataImportEnvironment.TableCreated` / `TableDropped`), a drzewo wstawia/usuwa **jeden liść w miejscu**. Przy okazji, w tej samej sesji, **Warstwa 1** z raportu — patrz blok niżej |
| **✅ I9 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-27)** | XLSX: `EmberTern.Export.Office` → **`EmberTern.Office`** (D1), `XlsxImportProvider` (7 wytycznych REK-6), rozgałęzienie sekcji Format po **`Capabilities`**, `SourceErrorValue` domykający R20. ⭐ **Filar „jeden pipeline dla każdego źródła" utrzymał się: pipeline, konwerter, walidator, mapowanie i writer NIE zostały zmienione.** Potwierdzone wzrokowo przez użytkownika w obu paletach — sprawdzone osobno: wybór arkusza, ukrywanie ustawień właściwych dla CSV/TXT, opcja „traktuj komórki dat jako daty" i pełny przebieg importu XLSX; bez uwag wizualnych i funkcjonalnych |
| **✅ I10 — DOSTARCZONY (2026-07-27), oczekuje potwierdzenia wzrokowego** | Schowek + `.xls` (BIFF8). ⭐ **Filar utrzymał się po raz drugi, tym razem pod większym obciążeniem — bo doszła NOWA ZALEŻNOŚĆ NuGet, a mimo to pipeline, konwerter, walidator, mapowanie i writer znów nie zostały tknięte.** Schowek okazał się już zbudowany (I5 dał przełącznik i pole, `MainWindow` czytanie ze schowka) — etap dołożył mu wyłącznie dowody. Szczegóły w bloku „⭐ I10 as-built" |
| **🏁 SZEW ERGONOMICZNY PO I10 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-27)** | Trzy uwagi użytkownika z przeglądu I10, potraktowane jako domknięcie I10, **nie** jako nowy etap: schowek jako **źródło żywe**, przycisk **Odśwież**, i wymóg, żeby ponowny odczyt przechodził **tym samym łańcuchem** co pierwszy. **Potwierdzone przez użytkownika: bez uwag.** Wprost zaakceptowany kierunek architektoniczny — *„najważniejsze, że nie powstała druga ścieżka odświeżania, tylko wszystko przechodzi przez jeden łańcuch z różnymi powodami uruchomienia (Decision / Refresh)"*. `Ctrl+R` zostaje, **`F5` świadomie NIE dodane** (decyzja użytkownika — `F5` to Import). Szczegóły w bloku „⭐ Szew ergonomiczny po I10" |
| **✅ I11 — ZAMKNIĘTY I ZAAKCEPTOWANY (2026-07-27)** | Nazwane profile. ⭐⭐ **Rachunek §4.8 się zgodził: ANI JEDEN model nie został zmieniony i pipeline nie został tknięty.** `ImportConfiguration`, `ImportProfile`, `ImportPipeline`, konwerter, walidator, planer mapowania i writer — bez jednej linii różnicy. Wczytanie profilu to `ApplyConfiguration` i **nic więcej**, więc profil niezgodny ze światem melduje się w pasku gotowości (IMP0011 / IMP0016), a nie wyjątkiem. Selektor wszedł w **zarezerwowane** miejsce paska B — układ toolbara nie został przebudowany. Szczegóły w bloku „⭐ I11 as-built" |
| **🔁 I11 — SZEW PO PRZEGLĄDZIE (2026-07-27)** | Dwie uwagi użytkownika z oglądu UI, obie zasadne, obie o **wyjściu** z profilu: (1) nie dało się wrócić do pracy **bez** profilu — doszła stała pozycja **„(no profile)"** na czele listy, która **odłącza i ZOSTAWIA decyzje**; (2) nie było jak wyczyścić powierzchni — doszedł przycisk **`Reset`**, który przywraca domyślne **i** odłącza profil. To są świadomie **dwie różne akcje**, nie jedna. Plus trzecia uwaga: **wyraźniejsze oddzielenie** grupy profili od grupy wykonania. Szczegóły w bloku „⭐ I11 as-built" |
| **⛔ DYREKTYWA UŻYTKOWNIKA PRZY ZAMYKANIU I11 (2026-07-27)** | **Nie wracamy już do kosmetyki Data Import.** Użytkownik zamknął etap ze świadomym stwierdzeniem, że nie wszystko wygląda docelowo, ale pozostałe uwagi są **czysto UX** i naturalnie należą do planowanego **globalnego sprintu UX** całej aplikacji (wymiana kontrolek Avalonia, zagęszczenie, ujednolicenie zachowań). Do modułu wracamy **wyłącznie przy rzeczywistym błędzie funkcjonalnym** |
| **🔁 I11 — DRUGI PRZEGLĄD (2026-07-27)** | Pięć uwag o ergonomii powierzchni, wszystkie w granicach modułu. ⭐ Dwie okazały się **defektem, nie kwestią gustu**: chipy `Target` i `Mapping` naprawdę nic nie robiły w zwyczajnych stanach, bo celowały w kontrolkę wyłączoną albo w wiersz, którego nie ma. Poza tym: usunięte zdublowane ostrzeżenie §0.5, `Existing/New table` w jednej siatce ze wspólnymi kolumnami, kolumna `Basis` przycięta z podpowiedzią, tytuł panelu Mapowanie. Szczegóły w bloku „⭐ Drugi przegląd I11" |
| **🔁 I11 — TRZECI PRZEGLĄD (2026-07-27), przygotowanie do scalenia** | Cztery poprawki wybrane przez użytkownika z audytu; **żadna nie tknęła modeli ani pipeline'u.** ⛔ **Poprawka U5 (podłoga wiersza roboczego + zacisk dolnego panelu) została COFNIĘTA tego samego dnia** po obejrzeniu w działaniu — środek okna urósł, a dolny panel przestał być użyteczny; **U5 wraca do otwartych i idzie do sprintu UX**. Zostają trzy: chip `Transaction` otwiera listę (jedyna sekcja, która nie jest pasem powierzchni, więc `BringIntoView` był tam zawsze pusty), wspólna „Podstawa" mówiona **raz dla sekcji** zamiast raz na wiersz, oraz `ImportReadinessReport.Prioritized` — sufit paska nie może już schować błędu blokującego za ostrzeżeniem. Szczegóły w bloku „⭐ Trzeci przegląd I11" |
| **🏁 I12 — DOSTARCZONY (2026-07-27), oczekuje potwierdzenia użytkownika** | Domknięcie: narracja modułu do **[docs/history/21-data-import.md](../history/21-data-import.md)** (~520 wierszy zeszło z CLAUDE.md, który skurczył się o 490 wierszy) · **audyt UI w obu paletach** — zero twardych kolorów, zero lokalnych pędzli, zero `{StaticResource}` na pędzlu, zero lokalnych stylów, 13/13 tokenów w obu paletach, **przypięte testem** żeby audyt był powtarzalny · ⭐ **pomiar na 1 M wierszy**: Manual **14,0 s / 71 437 wierszy/s**, sterta **płaska** (~1 MB przez cały przebieg), Batched 19,6 s i **990 000 z 999 997 przeżywa Rollback** — domyślne I0 (500 / 10 000) **bronią się** · raport uczciwy przy tej skali (numery rekordów, sufit błędów, licznik postępu) · **ostatnia poprawka funkcjonalna**: nieświeża lista tabel po `CREATE` (zgłoszenie dotyczyło pickera; groźniejsza połowa to `IMP0028`, które przestawało widzieć zajętą nazwę) · jawna lista tego, co zostaje otwarte, z powodem przy każdej pozycji |
| **⭐⭐ PODZIAŁ KONFIGURACJA / WYNIK (2026-07-27)** | Ostatnia zmiana układu, postawiona przez użytkownika po dwóch nieudanych podejściach do wysokości: **problem nie leżał w wysokości paneli, tylko w podziale odpowiedzialności.** „Preview after conversion" to **wynik** pipeline'u, nie konfiguracja — zszedł do dolnych zakładek (Source preview · **Preview after conversion** · Errors · Report), gdzie i tak jest potrzebny w obu wariantach celu. Powierzchnia robocza należy odtąd **w całości do konfiguracji**: dla istniejącej tabeli to Mapowanie na pełną szerokość, dla nowej **Typy tabeli \| Mapowanie** ze splitterem, więc proporcję ustawia użytkownik. Siatka typów i podgląd DDL wyprowadzone z pasa `Auto` do powierzchni roboczej — pas Cel jest znów cienki. **Góra = konfiguracja, dół = wyniki i diagnostyka.** U5 rozwiązany bez podłogi i bez nowej sekcji pionowej |
| **Następny krok** | **decyzja użytkownika: scalenie `feat/data-import` do `master`.** Nie ma kolejnego etapu |
| **Testy** | **5846 zielonych**, 0 niepowodzeń (I11 dodał **+45**: 16 do magazynu, 25 w nowym `DataImportProfileTests`, 4 na `ImportTargetFocus` z drugiego przeglądu; szew po I10 dodał +16; I10 +22). ⚠ Uruchamiać **dwiema partycjami** (`ConnectionExpandBindingProbe` osobno) i **zawsze z `--blame-hang --blame-hang-timeout 120s`** — zawieszenie z #94/#226/#261 wystąpiło w tej sesji i instrument NAZWAŁ podejrzanego (`CompletionRow_HighlightsMatchedPrefix`); to zawieszenie **po** zakończeniu testów, nie awaria testu |
| **Weryfikacja na żywo** | `tools/probes/DataImportProbe` (I4) — **20/20 ALL PASS** · `tools/probes/DataImportRunProbe` (I7 + **G z I8** + **H z I9** + **I z I10**) przeciwko FB5 `WI-V5.0.3.1683` — **33/33 ALL PASS**; sekcja I dokłada: detektor proponuje TAB dla wklejenia z Excela (3/3 rekordy zgodne co do 5 pól), wklejenie importuje się **bez pliku na dysku**, `.xls` → tabela istniejąca i → tabela nowa, `#N/A` z BIFF odrzucone przez kolumnę VARCHAR, data z `.xls` wraca z bazy tym samym dniem (2026-05-14), oraz **prawdziwy skoroszyt napisany przez Excela** (`Nadgodziny2.xls`: 3 arkusze, 20 pól, 1073 wiersze, ostatni numer 1074). Wcześniejsze: raport == `SELECT COUNT(*)`, Rollback cofa DELETE razem z wierszami, `Batched` zatwierdza co N i Rollback tego nie cofa, dry-run nie dotyka niczego, kolumna mieszana ląduje jako VARCHAR, `CREATE` widać z drugiego przyłączenia natychmiast (#213), katalog oddaje DOKŁADNIE te typy, o które poprosiliśmy, Rollback cofa wiersze i NIE cofa tabeli — **oraz (I9): arkusz → tabela istniejąca, arkusz → tabela nowa, prawdziwa komórka daty typuje się na `DATE` i wraca z bazy tym samym dniem (2026-04-03), a `#N/A` zostaje odrzucone przez kolumnę VARCHAR** |
| **Build** | 0 ostrzeżeń / 0 błędów (`TreatWarningsAsErrors`) · smoke: aplikacja startuje |
| **Kod w `src/`** | `EmberTern.Core/Import/**` + trzy pliki w `EmberTern.Firebird` + **pięć VM-ów i widok w `EmberTern.App`**. Rdzeń nadal ma zero Avalonia, zero `FirebirdSql`, zero UI. |
| **⭐ Kamień milowy** | **MVP (I0–I7) DOSTARCZONE: CSV/TXT → istniejąca tabela działa end-to-end**, z walidacją, raportem, decyzją transakcyjną i pamięcią ostatniej konfiguracji. **I8 dokłada drugi wariant celu — tabelę, której jeszcze nie ma.** Wszystko dalej (I9–I12) jest przyrostowe. |

### ⭐ I11 as-built — etap, który niczego nie dokładał, tylko sprawdzał rachunek (2026-07-27)

**Werdykt: §4.8 nie zostało po drodze naruszone.** §6 nazwało dowód wprost — *„jeżeli nazwane profile wymagają
zmiany choćby jednego modelu albo przebudowy sekcji UI, znaczy że §4.8 zostało naruszone"*. Nie wymagały.

**Czego NIE trzeba było ruszyć — i to jest właściwy wynik etapu:**

| | |
|---|---|
| `ImportConfiguration` | bez zmian |
| `ImportProfile` | bez zmian — nazwany profil to wiersz tej samej listy, który **ma** `Name`; `IsImplicit` znaczy dokładnie to, co znaczyło od I1 |
| `ImportPipeline`, `ImportValueConverter`, `ImportRowValidator`, `ImportMappingPlanner`, `FirebirdImportWriter` | bez zmian |
| `UserSettings.ImportProfiles`, wersja kontenera `settings.dat` | bez zmian (wersja **nie** podbita — nauka z C3) |
| Układ paska poleceń | bez przebudowy — selektor wszedł w miejsce, które §4.8.4 zarezerwowało w MVP |

**Co trzeba było dopisać — jedno miejsce, zaplanowane w I1.** `ImportProfileStore` dostał operacje na nazwanych
wpisach (`ListNamed` / `GetById` / `SaveNamed` / `NameExists` / `Rename` / `Delete` / `IsReadable`). To **nie jest**
odstępstwo: własny komentarz tej klasy od I1 mówił, że API jest celowo ograniczone do wpisu niejawnego, bo
*„named-profile operations arrive with their UI in etap I11 — adding them now would leave methods nothing calls"*
(gotcha #233). Fasada urosła o czytanie i pisanie wierszy listy, która już istniała; **kształt danych się nie
zmienił**.

⚠ **Rozbieżność do odnotowania:** prompt otwierający sesję wymieniał `ImportProfileStore` wśród rzeczy, których
dotknięcie ma oznaczać „ZATRZYMAJ I ZGŁOŚ". §4.8.4 mówi jednak o UI nazwanych profili jako o *„wyłącznie widoku
nad istniejącym magazynem"*, a sam magazyn zapowiadał te metody na I11. Rozstrzygnięte na korzyść dokumentu
projektowego: **modelem** jest `ImportConfiguration` i `ImportProfile`, a te są nietknięte. Zgłoszone jawnie,
zgodnie z obowiązkiem etapu.

**⭐⭐ Dlaczego niezgodność ze światem nie wymagała ani jednej linii kodu.** Wczytanie profilu to
`ApplyConfiguration(profile.Configuration)` i **nic więcej** — czyli ta sama droga, którą od I7 wraca „ostatnio
użyta" konfiguracja: `Recalculate` → źródło czytane na nowo → cel czytany na nowo z katalogu →
`ImportMappingPlanner` przelicza pod regułą zachowania dowodliwego → pasek gotowości. Skutek: profil wskazujący
usunięty plik daje **IMP0011**, profil wskazujący nieistniejącą tabelę daje **IMP0016**, a mapowanie na
zmienionym źródle jest **przeplanowane po nazwach**, nie odtworzone po pozycjach. W kodzie profili nie ma na to
żadnego warunku — to wypada z tego, że nie ma drugiej ścieżki wczytania. Zapinowane trzema testami.

**⭐ Profil „z przyszłości" jest POKAZANY, a nie ukryty.** §4.8.3 wymaga oznaczenia go jako nieczytelnego z
komunikatem; ukrycie wyglądałoby dla użytkownika dokładnie jak skasowanie zapisanego profilu. Predykat
`ImportProfileStore.IsReadable` jest **jeden** i obsługuje zarówno listę nazwanych, jak i przywracanie wpisu
niejawnego — dwie kopie reguły „za nowy" prędzej czy później zaczęłyby się różnić, a różnica objawiłaby się jako
profil, który lista uznaje za zdatny, a ładowanie odrzuca.

**⭐ Zakres selektora jest POWIEDZIANY NA EKRANIE.** Lista pokazuje profile tego połączenia plus te, które nie są
z żadnym związane; profil zapisany na innym połączeniu **nie jest** oferowany, bo nazywa tabelę, której ta baza
może nie mieć. Ograniczenie, którego użytkownik nie widzi, jest nieodróżnialne od profilu, który zniknął — stąd
zdanie w podpowiedzi selektora.

⚠ **Świadomie NIE zbudowane: eksport/import `.json`.** §6 wymienia go jako **opcjonalny** i nie ma go w DoD.
Wymagałby nowego serializatora w Core i dwóch nowych szwów w widoku, czyli **powiększyłby powierzchnię, o której
ten etap ma orzec, że jej nie ruszył** — a to jest jedyny produkt I11. Konsekwencja obsłużona: podpowiedź
selektora nie obiecuje wymiany plikami. Gałąź `ConnectionId == null` w `ListNamed` **zostaje**, bo to nullowalne
pole zapisanego rekordu — profil, którego żadne zapytanie nie zwraca, jest danymi nieosiągalnymi, nie brakującą
funkcją.

#### ⭐ Trzeci przegląd I11 — cztery poprawki przed scaleniem (2026-07-27)

Pełny audyt powierzchni dał więcej uwag; użytkownik wybrał z niego **cztery** i świadomie odrzucił resztę
(m.in. zmianę układu `Existing table` / `New table` — po ponownej analizie uznał, że opcje pojawiające się po
prawej stronie uzasadniają obecny układ). Żadna poprawka nie tknęła `ImportConfiguration`, pipeline'u,
konwertera, walidatora, planera ani writera.

**1. ⛔ COFNIĘTE po obejrzeniu w działaniu (decyzja użytkownika, 2026-07-27) — U5 wraca do otwartych.**
Diagnoza niżej stoi i jest prawdziwa; cofnięte zostało **lekarstwo**, nie rozpoznanie. Uszanowanie podłogi
wymagało oddania miejsca przez dolny panel, więc środek okna urósł, a dolny panel przestał być użyteczny —
przy obecnych wysokościach kontrolek nie da się mieć obu naraz. Właściwym miejscem naprawy jest **sprint UX**
(U4): gdy kontrolki zmaleją, powierzchnia odzyska ~100 px i problem zniknie bez dokładania czegokolwiek.
⛔ Nie przywracać podłogi i nie rozwiązywać wysokości kolejną sekcją pionową. Zapis zostaje, żeby następna
sesja nie „naprawiła" tego po raz drugi.

**Rozpoznanie, które zostaje w mocy:**
Wszystkie pasy poza roboczym są `Auto`, więc gwiazdka bierze to, co zostanie — a zostać może **nic**. W
wariancie „New table" kafelek Cel jest najwyższym pasem powierzchni (siatka typów plus, na żądanie, podgląd
DDL), a pod nim dolny panel trzyma wysokość bezwzględną: Mapowanie i Podgląd po konwersji **znikały
całkowicie**. To jest dokładnie powód, dla którego chip `Mapping` „wyglądał na martwy" mimo naprawy z drugiego
przeglądu — celował poprawnie w panel o zerowej wysokości.

⚠ **`MinHeight` to tylko połowa.** Podłoga, do której nie da się dojść, nie jest podłogą: gdyby sama deklaracja
weszła bez niczego więcej, przy ciasnej powierzchni siatka wypchnęłaby na zewnątrz pas statusu. Druga połowa
jest w `ApplyBottomPanel` (§ jedyny punkt renormalizacji, #240): **dolny panel jest przycinany** do tego, co
zostaje po pasach `Auto` i po podłodze. Z dwóch pasów walczących o resztę miejsca ustępuje ten, którego
wysokość **wybraliśmy my** — zapisana wartość nigdy nie jest nadpisywana, tylko chwilowo przycięta, więc wraca
sama, gdy kafelek Cel zmaleje.

**2. Chip `Transaction` — otwiera listę.** Nawigował poprawnie od drugiego przeglądu, ale to jedyna „sekcja",
która **nie jest pasem tej powierzchni** — mieszka w pasku poleceń, kilka pikseli nad chipem. Tam
`BringIntoView` jest zawsze pusty, a obwódka focusa na `ComboBoksie` to całość informacji zwrotnej. Otwarcie
listy jest tym, czym „zabierz mnie do tej decyzji" może być dla pickera: pokazuje samą decyzję i **dalej nie
zmienia żadnego ustawienia**.

**3. ⭐ „Podstawa" wspólna dla wszystkich kolumn mówiona jest RAZ.** Po odtworzeniu profilu każdy wiersz
dostawał „from the restored configuration", więc kolumna szeroka jak kolumna nazwy powtarzała jedno zdanie tyle
razy, ile tabela ma kolumn — a linia sekcji, która powinna nieść to raz, była celowo pusta. To ta sama wada,
którą drugi przegląd usunął dla `IMP0018` (jeden fakt powiedziany dwa razy uczy, żeby nie czytać żadnego),
pomnożona przez liczbę wierszy. Reguła jest ogólna, nie dotyczy tylko odtworzenia: **jeżeli podstawa jest
identyczna dla całej siatki, jest faktem o siatce, nie o kolumnie** — schodzi na linię sekcji, a nagłówek
kolumny znika razem z komórkami (nagłówek nad pustą kolumną to właśnie „atrapa"). Gdzie dowód jest naprawdę per
kolumna — a R19 zmierzył, że kolumny mieszane są normą — zostaje przy typie, który tłumaczy.

⚠ **Reszta zostawiona świadomie:** ukryta kolumna nie odzyskuje swojej szerokości (proporcje `3*` są wspólne dla
nagłówka i wierszy, a zwinięcie ich wymagałoby konwertera `bool → GridLength` — nowego typu na zamknięcie
modułu). Po ukryciu nagłówka to czyta się jak margines, nie jak martwa kolumna.

**4. ⭐ Sufit paska gotowości nie może już schować błędu za ostrzeżeniem.** Pasek pokazuje trzy wyniki i chowa
resztę, więc jego kolejność nie jest kwestią smaku — **decyduje o tym, co znika**. Kolejność ewaluacji jest
sekcjami (środowisko → źródło → cel → mapowanie → transakcja): dobra do *czytania*, zła do *cięcia*, bo nowa
tabela **zawsze** podnosi nieblokujące `IMP0018` w sekcji Cel, czyli przed każdym wynikiem mapowania — i w
zwykłej kolejności zabierało ono jeden z trzech widocznych slotów błędom, które tłumaczą, czemu bieg jest
odmówiony.

`ImportReadinessReport.Prioritized` — blokujące najpierw, reszta potem, **stabilnie w obrębie grupy** — leży w
**Core**, nie w powierzchni, z tego samego powodu co `CanValidate`: widok cinający listę własną regułą byłby
drugą opinią o tym, co najważniejsze, i pasek z raportem zaczęłyby się w końcu różnić. Stabilność sortowania
jest tym, co utrzymuje zwinięty pasek **prawdziwym prefiksem** rozwiniętego.

#### ⭐ Drugi przegląd I11 — ergonomia powierzchni (2026-07-27)

Pięć uwag z pełnego oglądu UI, wszystkie w granicach modułu, żadna w Core i żadna w globalnych stylach.

**1 + 2. Chipy paska Ready i klikalne komunikaty — to był DEFEKT, nie kwestia gustu.** Użytkownik napisał, że
`Target`, `Mapping` i `Transaction` „sprawiają wrażenie atrap". Sprawiały, bo dwa z nich **naprawdę nic nie
robiły** w zwyczajnych stanach:

- **Target** celował w `TargetPicker` (lista istniejących tabel), a ta jest **wyłączona**, kiedy wybrany jest
  wariant „nowa tabela". `FirstFocusable` pomija kontrolki nieaktywne, więc zwracał `null` i kliknięcie znikało.
- **Mapping** celował w „wiersz wymagający uwagi", czyli `null`, gdy wszystko jest zmapowane — chip działał
  **wyłącznie wtedy, gdy coś było nie tak**.

Reguła jest teraz jedna dla wszystkich pięciu: **udostępnij sekcję, potem oddaj fokus kontrolce, o której ta
sekcja aktualnie JEST** — niezależnie od tego, czy ma problem. Zielony chip nawiguje tak samo jak czerwony.
Doszło też `BringIntoView()` **przed** `Focus()`: sam fokus niczego nie przewija, co było drugą połową wrażenia
martwoty przy długiej liście kolumn. Każda gałąź ma zejście awaryjne na kontener sekcji, więc rozstrzygnięcie
nie może już dać `null`.

⭐ **„O czym jest sekcja Cel" to decyzja ViewModelu, nie widoku** (`ImportTargetFocus`): zależy od wybranego
wariantu, a widok, który liczyłby to po swojemu, byłby drugą opinią o tym, która połowa sekcji żyje. Pusta nazwa
nowej tabeli ⇒ pole nazwy; nazwa ustalona ⇒ siatka typów, bo tam są decyzje, które zostały. Zapinowane czterema
testami — brak tej odpowiedzi był źródłem całego defektu.

Komunikaty pod chipami używają **tej samej** komendy, więc naprawa objęła je automatycznie. Każde `ReadinessItem`
ma sekcję, a każda z pięciu sekcji prowadzi teraz gdzieś konkretnie — więc wszystkie zostają klikalne; nie ma
komunikatu „bez dokąd", który trzeba by zamienić w zwykły tekst.

**3. Zdublowane ostrzeżenie — jedno zostało.** Zdanie §0.5 („tabela powstaje i jest ZATWIERDZANA przed pierwszym
wierszem") stało w dwóch miejscach jednego ekranu: jako `IMP0018` w pasku gotowości i jako banner pod siatką
typów. Usunięty został **banner**, bo pasek mówi to samo, pochodzi z Core i dodatkowo **nazywa tabelę**. Dwa
sformułowania jednego faktu uczą nie czytać żadnego. ⚠ Świadome ryzyko: pasek przycina listę do trzech pozycji,
więc `IMP0018` może trafić pod „…i N więcej" — gdyby to okazało się realne w praktyce, poprawką jest wzmocnienie
**paska**, nigdy dodanie drugiego zdania.

**4. Existing / New table — jedna siatka zamiast dwóch.** Oba warianty leżały w osobnych `Grid`ach, więc każdy
mierzył szerokość kolumny po **własnej** etykiecie i pola wejściowe zaczynały się w różnych miejscach; między
nimi stała jeszcze linia faktów, dzieląc jeden wybór na pół. Teraz to jedna siatka o dwóch wierszach i wspólnych
kolumnach — pola startują w tym samym `x`, a linia faktów zeszła **pod** parę, gdzie należy do odpowiedzi, nie do
pytania. Dokładnie układ ze szkicu użytkownika, o jeden wiersz niższy niż wcześniej.

**5. Drobiazgi.** Kolumna **Basis** jest jednoliniowa z przycięciem i pełnym tekstem w podpowiedzi: zawijanie
ustawiało wysokość całego wiersza i pozwalało objaśnieniu zdominować siatkę, którą tylko opisuje (§0.6 mówi o
**dostępności** dowodu, nie o tym, że ma być najszerszy na ekranie). Panel **Mapowanie** dostał tytuł — połowa
„Podgląd" już go miała, więc obszar roboczy czytał się jako jedna nierozdzielona powierzchnia z pływającą linią
statusu; oba tytuły mają teraz tę samą wagę (współdzielony `group-header`, użyty ponownie, nie nowy styl), a
dynamiczny licznik został przy nich jako tekst poboczny. Warstw informacji jest o jedną mniej — dzięki punktowi 3.

---

#### ⭐ Szew po przeglądzie I11 — wyjście z profilu (2026-07-27)

Przegląd wzrokowy dał dwie uwagi i obie mówiły o tym samym braku z dwóch stron: **selektor był drogą w jedną
stronę.** Dało się wejść w profil, nie dało się z niego wyjść — ani zostając przy swoich decyzjach, ani
zaczynając od zera. Zwięźle: zbudowałem wybieranie, a nie zbudowałem *nie-wybierania*.

**⭐⭐ To są dwie RÓŻNE akcje i połączenie ich byłoby błędem, nie uproszczeniem.**

| | „(no profile)" — pozycja na liście | `Reset` — przycisk |
|---|---|---|
| Profil | odłączony | odłączony |
| Decyzje na powierzchni | **zostają nietknięte** | **wyczyszczone do domyślnych** |
| Pyta? | nie — nic nie niszczy | tak, gdy jest co stracić |

Gdyby pozycja na liście czyściła powierzchnię, wybranie jej niszczyłoby pracę, o której zniszczenie nikt nie
prosił — reguła #11 wprost. Dlatego pozycja nazywa się **„(no profile)", a nie „domyślna konfiguracja"**:
druga nazwa obiecywałaby przywrócenie domyślnych, którego ta pozycja nie robi. Przywracanie domyślnych ma swój
przycisk i swoją nazwę. Wybranie pozycji mówi to zresztą na pasku komunikatów — *„decyzje na powierzchni są
niezmienione"* — bo „czy to też wyczyści moją pracę" jest jedynym pytaniem, jakie ta pozycja rodzi.

**⭐ `Reset` pyta o EMPTINESS, nie o modyfikację.** „Czy coś się zmieniło od wczytania" wymagałoby kanonicznego
porównania dwóch `ImportConfiguration`, a rekord porównuje listy po referencji — mapowanie jest nową instancją
po każdym przeliczeniu, więc taki test odpowiadałby „zmienione" zawsze i natychmiast. Pytanie zadawane jest
więc odpowiadalne: **czy na powierzchni cokolwiek stoi** (źródło, schowek, tabela docelowa, nazwa nowej tabeli
albo wybrany profil). Na pustej powierzchni `Reset` nie pyta o nic — okno dialogowe bez treści uczy tylko
odruchowego klikania „OK".

⭐ **`Reset` i „Wyczyść" przy nocie o przywróceniu to teraz TEN SAM kod** (`ResetToDefaults`). „Wyczyść" miało
własną kopię tych samych trzech linii; dwa sposoby opróżnienia powierzchni prędzej czy później opróżniałyby jej
różną ilość. Przy okazji „Wyczyść" zaczęło poprawnie odłączać profil, czego wcześniej nie robiło.

⚠ **Usunięte, bo nie miało konsumenta:** właściwość `HasProfiles`. Powstała w pierwszym podejściu i nie była
związana z niczym w XAML-u — czytały ją wyłącznie testy. To ta sama dyscyplina, dla której w tym etapie nie
powstało `MarkUsed`.

**Trzecia uwaga — separacja wizualna — przyjęta i uzasadniona asymetrią.** Pasek ma teraz dwie kreski, ale nie
są równorzędne: ta między grupą profili a grupą wykonania dostała margines **12 px** zamiast 4, bo dzieli
**dwie różne rzeczy** (zarządzanie konfiguracją vs jej uruchomienie), podczas gdy druga dzieli tylko ustawienia
*wewnątrz* grupy wykonania. Profil jest nadrzędny wobec całej konfiguracji importu, więc jego granica ma
czytać się jako mocniejsza z dwóch.

⚠ **Poprawka przy okazji, w granicach modułu:** wspólne potwierdzenie destrukcyjne (`ConfirmRequested`) niosło
tylko treść, a widok dokładał **stały** nagłówek „Opróżnij tabelę przed importem" i **stały** przycisk
„Importuj" — więc pytanie „usunąć utworzoną tabelę?" już wcześniej pojawiało się pod nazwą innej akcji. Zdarzenie
niesie teraz cały `ConfirmRequest`; profile dołożyłyby trzecie i czwarte takie pytanie.

⚠ **Świadomie NIE dodane: znacznik „zmodyfikowany" przy wybranym profilu.** Wymagałby kanonicznego porównania
dwóch `ImportConfiguration`, a rekord porównuje listy (mapowanie, tokeny logiczne) po referencji — po każdym
przeliczeniu mapowanie jest nową instancją, więc znacznik zapalałby się natychmiast po wczytaniu. Zbudowanie
takiego porównania to zmiana modelu, czyli dokładnie to, czego ten etap dowodzi, że nie było potrzebne. Selektor
mówi więc „ten profil wczytano", a nie „powierzchnia równa się temu profilowi"; `Zapisz jako…` podpowiada nazwę
wybranego profilu, więc nadpisanie go jest jednym gestem.

---

### ⭐ Szew ergonomiczny po I10 — schowek jako źródło żywe, „Odśwież", jeden łańcuch (2026-07-27)

Trzy uwagi z przeglądu I10, zgłoszone jako **domknięcie ergonomii gotowego modułu, nie nowa funkcjonalność**.
Użytkownik postawił też warunek architektoniczny: *„nie chciałbym mieć dwóch ścieżek aktualizacji"* — i jeśli
jednego punktu wejścia do łańcucha nie ma, zrobić go **teraz**, przed zamknięciem modułu.

**Odpowiedź na to pytanie brzmi: jeden punkt wejścia BYŁ i jest — `Recalculate` → `RunChainAsync`.** Cały etap
polegał na doprowadzeniu do niego trzech rzeczy, które go omijały albo nie miały jak go uruchomić. Modele,
pipeline, konwerter, walidator, mapowanie i writer — **nie tknięte** (jak w I9 i I10).

| Uwaga | Co było | Co jest |
|---|---|---|
| **1. Schowek ma być źródłem żywym** | Odczyt schowka był **komendą obok łańcucha**: pobierała tekst, przypisywała go do `Source.ClipboardText`, a łańcuch reagował dopiero na zmianę właściwości | Odczyt jest **pierwszym ogniwem łańcucha** (`ReadClipboardIfNeededAsync`). Zakładka otwarta na konfiguracji „Schowek" czyta schowek **sama**; przełączenie Plik → Schowek czyta ponownie; `Ctrl+V` i `Ctrl+R` to ręczne ponowienie |
| **2. Brakuje przycisku „Odśwież"** | Nie było żadnego sposobu uruchomienia łańcucha na życzenie — pozostawało zamknięcie i otwarcie zakładki | Przycisk **Odśwież** w pasku poleceń (ikona `Icon.RefreshCw` — ta sama, której używa drzewo metadanych, Session Manager i Table Data) + `Ctrl+R`. **Nie jest drugą ścieżką**: to `Recalculate(ImportChainTrigger.Refresh)` |
| **3. Odświeżenie musi przebudować cały stan** | Łańcuch przebudowywał wszystko poprawnie — ale **dwa FAKTY o świecie były zaryglowane**: lista tabel czytana raz na zakładkę (`_tablesLoaded`) i schowek czytany tylko przy przypisaniu | `Refresh` **zrzuca oba** i czyta je od nowa, po czym idzie ten sam łańcuch: źródło → analiza → schemat → mapowanie → gotowość → podgląd |

⭐ **Kluczowa decyzja: „powód" jest ARGUMENTEM jednego łańcucha, nie drugim łańcuchem.** `ImportChainTrigger`
ma dwie wartości — `Decision` (zmieniła się decyzja: przelicz na już ustalonych faktach) i `Refresh`
(użytkownik poprosił: zrzuć fakty i przeczytaj je ponownie). To dokładnie ta sama dyscyplina, dzięki której
„Waliduj" jest **innym writerem podanym do jednego `ImportPipeline`**, a nie drugim trybem: różnicy nie ma
gdzie się rozjechać, bo nie ma drugiej ścieżki.

⭐ **Dlaczego „nawet po Ctrl+V nie przeliczało się mapowanie" — i dlaczego to nie był błąd w mapowaniu.**
Tekst schowka docierał przez **przypisanie właściwości**, a `SetProperty` porównuje: identyczna treść to brak
`PropertyChanged`, brak `Changed`, brak łańcucha. Ponowny odczyt tej samej treści przeliczał **zero**. Odkąd
odczyt jest ogniwem, to, co dzieje się dalej, wynika z **tego, że poproszono**, a nie z tego, że wartość się
różni. Gotcha **#271**.

⚠ **Odkryte przy okazji, przez test liczący odczyty: `SourceDescriptor` nie umie powiedzieć „plik, ale jeszcze
nie wybrany".** `BuildSource` zwraca dla tego stanu `Kind == Clipboard`, więc brama oparta na rodzaju źródła
kazałaby **każdej świeżo otwartej zakładce** sięgnąć do schowka i — gdy treść wygląda tabelarycznie — przyjąć
ją jako źródło, **przy zaznaczonym „Plik"**. Brama pyta więc o to, co naprawdę niesie decyzję użytkownika
(`Source.UseFile`, czyli radio). Gotcha **#272**.

⭐ **Automatyczny odczyt przyjmuje treść tylko wtedy, gdy to TABELA — i nie wymyśla na to własnej heurystyki.**
Pierwsze pytanie idzie do `DelimiterDetector`, jedynego właściciela pytania „czy to tekst rozdzielany", więc
„tabelaryczne" znaczy tu to samo, co wszędzie indziej. Drugie pytanie istnieje tylko dlatego, że ten detektor
**świadomie odmawia** wymyślenia separatora dla jednej kolumny — a jedna kolumna wklejona z Excela to nadal
tabela. **Odświeżenie jawne nie pyta o to wcale**: „przeczytaj schowek ponownie" ma jedno uczciwe znaczenie,
więc przyjmuje to, co tam jest, **także pustkę** (powierzchnia mówi wtedy „brak źródła" przez pasek gotowości,
co jest prawdą — trzymanie poprzedniego tekstu pokazywałoby dane, których w nazwanym źródle nie ma).

⚠ **Świadoma granica, zapisana jako test, żeby nie dało jej się „naprawić" przez przypadek:** odświeżenie
**nie proponuje na nowo typów nowej tabeli**, dopóki źródło opisuje te same pola. Typy wyedytowane przez
użytkownika są decyzjami, a nadpisanie decyzji propozycją to jedyna rzecz, której reguła #11 zabrania wprost;
istniejąca reguła i tak wnioskuje ponownie, gdy zmienią się pola albo kultura — czyli wtedy, gdy grunt pod tymi
decyzjami rzeczywiście się poruszył.

**Drobne przy okazji:** `ClipboardReadRequested` przeniesione z **zdarzenia** (podłączanego po konstrukcji) do
`DataImportEnvironment.ReadClipboardAsync` — bo schowek czyta teraz **łańcuch**, więc musi być odpowiadalny już
w pierwszym przebiegu z konstruktora; wybór pliku zostaje zdarzeniem, bo jego jedynym wyzwalaczem jest komenda
użytkownika. `FileFacts` → **`SourceFacts`**: jedna linia faktów dla obu wariantów, a dla schowka niesie
**godzinę odczytu** — to jest ta postać pytania „czy to, co widzę, jest aktualne", na którą źródło żywe umie
odpowiedzieć, i to ona sprawia, że odświeżenie widocznie się potwierdza także wtedy, gdy treść jest identyczna.

### 🔴 I8 — awaria znaleziona w przeglądzie i jej dwie przyczyny (2026-07-27)

**Objaw:** nowa tabela → „Waliduj" przechodzi → „Importuj" → **aplikacja natychmiast się zamyka.**
**Diagnoza zajęła minutę, bo aplikacja loguje własne awarie**: `AppDomain.UnhandledException` zapisuje pełny
ślad stosu do `%TEMP%\EmberTern-debug.log`. Tam stało wprost: `FbException` SQLSTATE **-204 „Table unknown
XXX_GG_TMP_IMPORT_FANTOM"`, rzucone przez `FirebirdImportTargetPreparer.CountRowsAsync`, wywołane z
`ConfirmEmptyAsync`. **Zawsze czytaj ten log jako pierwszy** — nie zaczynaj od ponownego czytania kodu.

**⭐ To, że Waliduj przechodziło, a Importuj się wywalało, było samo w sobie wskazówką:** `emptyFirst` to
`!validation && …`, więc feralna ścieżka istniała wyłącznie w prawdziwym przebiegu.

| # | Defekt | Dlaczego powstał | Poprawka |
|---|---|---|---|
| **1** | **„Opróżnij tabelę przed importem" było WŁĄCZONE dla nowej tabeli.** Przebieg pytał o `COUNT(*)` z tabeli, której jeszcze nie ma — bo `CREATE` leci dopiero po potwierdzeniu | Widok **ukrywa** ten checkbox w wariancie „nowa tabela", ale `BuildBehavior` kopiowało wartość bezwarunkowo. **Ukrycie kontrolki nie wycofuje decyzji, którą ona niesie.** Wartość mogła też przyjść z **przywróconej konfiguracji „ostatnio użytej"** — wtedy użytkownik nigdy jej nie widział | `BuildBehavior` → `EmptyTargetBeforeImport = !IsNewTable && EmptyBeforeImport`. Poprawka **w jedynym miejscu tłumaczącym stan UI na rekord** (§4.8.6); strażnik u konsumenta byłby drugą opinią o tym, czyja to decyzja. Sam „ptaszek" **nie jest kasowany** — powrót do wariantu istniejącej tabeli zastaje go tam, gdzie użytkownik go zostawił |
| **2** | **Wyjątek wyszedł z komendy i zabił proces.** `AsyncRelayCommand` rzuca błąd nieudanej komendy **na dispatcherze**, gdzie nie ma już czego go złapać — więc nieobsłużony wyjątek nie daje złego raportu, tylko kończy aplikację | Klauzule `catch` były **białymi listami TYPÓW** (`InvalidOperationException or TimeoutException …`). ⭐ A ten VM sięga po świat **wyłącznie przez delegaty** z `DataImportEnvironment`, właśnie po to, by żaden typ Firebirda nie dotarł do ViewModelu (reguła #1) — i to wymazanie działa w obie strony: **komponent rozmawiający ze światem przez delegaty nie może wyliczyć wyjątków, które świat rzuca.** Moduł nie mógł nawet *nazwać* `DdlExecutionException` bez złamania własnych warstw | Nowa granica `RunGuardedAsync` + `ReportUnexpected` — łapanie **po POZYCJI (to jest granica), nie po TYPIE**. `OperationCanceledException` osobno i wyżej (anulowanie to decyzja, nie usterka — #253). Rozszerzone na wszystkie 12 styków z delegatami, na pozostałe komendy async oraz na **łańcuch przeliczeń**, który jest *fire-and-forget* i zamieniał ucieczkę w `UnobservedTaskException` |

⚠ **Defekt 2 krył jeszcze jedną, niezauważoną awarię:** `FirebirdDdlExecutor` rzuca `DdlExecutionException`,
którego nie było na żadnej liście — więc **każdy odrzucony `CREATE TABLE` również zamknąłby aplikację**,
zanim ktokolwiek zobaczyłby komunikat serwera. Znalezione przy okazji, nie przez zgłoszenie.

**Testy regresyjne (+11) i dowód, że działają:** obie poprawki zostały **tymczasowo wycofane**, a testy
`ANewTable_NeverCarriesEmptyTheTableFirst_EvenIfItWasTickedEarlier` i
`NoCollaboratorCanTakeTheApplicationDown(failing: "count")` **wywaliły się** — po czym poprawki wróciły.
Test rzuca typ, którego nikt nigdy nie wpisałby na białą listę, **z każdego współpracownika po kolei**
(create · read · count · write · commit · rollback · drop · tables), więc wywali się w dniu, w którym ktoś
znów zawęzi którykolwiek `catch`. Gotchy **#264** i **#265**.

### ✅ I8 — drugie zgłoszenie z przeglądu: nowa tabela nie pojawiała się w drzewie (2026-07-27)

**Objaw:** import przechodzi, tabela powstaje, dane są w środku — ale Explorer metadanych pokazuje ją
dopiero po ręcznym odświeżeniu. **Przyczyna, bez zagadki:** `CREATE TABLE` leci na linii Ddl i **nikt nie
zawiadamiał drzewa**; pozostałe ścieżki DDL robią to jawnie (20 wywołań `Metadata.RefreshAsync()`).

⭐ **Dlaczego nie dopisano dwudziestego pierwszego.** Użytkownik zlecił wcześniej analizę mechanizmu
metadanych ([metadata-refresh-analysis.md](../design/metadata-refresh-analysis.md)) i pomiar pokazał, że pełne
odświeżenie kosztuje **13 zapytań do katalogu (~164 ms) plus ponad sekundę na wątku UI**, gdy jakaś
kategoria jest rozwinięta. Moduł importu **zna nazwę tabeli, którą właśnie utworzył**, więc mówi to
wprost — `DataImportEnvironment.TableCreated` / `TableDropped` — a `MetadataExplorerViewModel` wstawia
albo usuwa **jeden liść w posortowanym miejscu** (`ApplyObjectAddedInPlace` /
`ApplyObjectRemovedInPlace`, wąski precedens obok istniejącego `ApplyTriggerActiveStateInPlace`).
Zmierzone: **1,3 ms i zero okrążeń do bazy**.

⚠ **To NIE jest „Warstwa 2" z raportu** — nie ma tu wspólnego pojęcia zmiany ani protokołu, a pozostałych
20 ścieżek DDL nikt nie ruszał. Uogólnienie to osobny etap infrastrukturalny **po** zamknięciu Data Import.

**Delegat niesie NAZWĘ, nie typ metadanych** — z tego samego powodu, dla którego istnieje cały
`DataImportEnvironment` (reguła #1). I jest **stwierdzeniem faktu** („ta tabela istnieje"), nie poleceniem
(„odśwież się"): kto zna zmianę, ten ją opisuje, a nie każe drugiej stronie odkrywać ją od nowa.

**Zgłoszenie tworzenia idzie zaraz po udanym `CREATE`, nie na końcu przebiegu** — tabela przeżywa nieudany
import (§0.5, gotcha #213), więc jej istnienie nie jest warunkowane wierszami. Symetrycznie: udany `DROP`
po nieudanym imporcie zgłasza usunięcie, żeby drzewo nie oferowało obiektu, którego już nie ma.

**Przy okazji tej samej sesji weszła Warstwa 1 z raportu** (blokada `BeginUpdate/EndUpdate` na ścieżce
ładowania): projekcja pełnego odświeżenia **1 424 ms → 2 ms** przy jednej rozwiniętej kategorii. Szczegóły
i pełne pomiary — [metadata-refresh-analysis.md §7](../design/metadata-refresh-analysis.md).

### ⭐ I10 as-built — pięć rzeczy wartych zapamiętania

1. **⭐⭐ Filar wytrzymał ostrzejszy test, bo tym razem doszła ZALEŻNOŚĆ.** I9 dokładał źródło przy pomocy
   biblioteki, którą projekt już miał; I10 dokłada `ExcelDataReader` — jedyną decyzję NuGetową, jaka w tym
   module jeszcze zapadała. Zależność sięga **dokładnie jednego projektu** (`EmberTern.Office`), a pipeline,
   konwerter, walidator, planer mapowania i writer **znów nie zostały tknięte**. `IImportProvider` ma trzecią
   implementację; App nauczył się jednego: `ProviderFor` ma o jedną gałąź więcej.
2. **⭐ Schowek był już zbudowany — etap dołożył mu wyłącznie dowody.** Przełącznik Plik/Schowek, pole
   `ClipboardText`, komenda `UseClipboardCommand`, delegat `ClipboardReadRequested` i odczyt z `TopLevel`
   w `MainWindow` powstały wcześniej (I5). Nie było czego pisać; było co **sprawdzić** — i to okazało się
   niebanalne, patrz punkt 3. ⚠ Morał na przyszłe etapy: przed napisaniem kodu z listy zakresu warto sprawdzić,
   czy nie stoi już gotowy. Prompt otwierający I10 zapowiadał „brakuje wyłącznie wczytania zawartości schowka" —
   i to akurat też już istniało.
3. **⭐⭐ Detekcja separatora jest krokiem NAD pipeline'em, i pierwsza wersja sondy tego nie odwzorowała.**
   Przypadek I1 podał `AutoDetectDelimiter = true` prosto do `ImportPipeline` i dostał **jedną kolumnę**.
   To nie jest usterka — to §0.4 działające zgodnie z projektem: auto-detekcja **proponuje**, pokazuje podstawę,
   a do konfiguracji trafia wartość **rozstrzygnięta**; provider czyta separator zadeklarowany i nie ma zdania
   o wykrywaniu. Sonda musi robić to, co robi powierzchnia, bo inaczej nie dowodzi niczego o powierzchni.
   ⚠ Uogólnienie: gdy sonda i UI dają różne wyniki na tych samych danych, **najpierw sprawdź, czy sonda
   odtworzyła całą drogę** — dopiero potem szukaj usterki w produkcie.
4. **⭐ Kalendarz Excela dostał JEDNEGO właściciela, bo drugi provider potrzebował go w drugą stronę.**
   `WorkbookCellReader` liczył serial → data u siebie (I9). ExcelDataReader oddaje `DateTime` **już zdekodowany**,
   więc uszanowanie opcji „nie traktuj komórek dat jako daty" znaczy konwersję **z powrotem** na liczbę seryjną.
   Odwrotność napisana obok funkcji prostej, której nie widzi, to dokładnie sposób, w jaki dwie połówki jednego
   kalendarza się rozjeżdżają — stąd `ExcelSerialDate` z `FromSerial` i `ToSerial`, z którego korzystają obaj
   providerzy. Zapinowane: serial 15 to 1900-01-15 w obie strony (`FromOADate(15)` dałoby 1900-01-14).
5. **⭐ Biblioteka sama decyduje „czy to data", więc jej werdykt jest PONAWIANY u nas.** To odwrotne
   niebezpieczeństwo niż w `.xlsx`: nie data przeoczona, tylko data **wymyślona**. `XlsCellReader` pyta
   `SpreadsheetNumberFormats` — jedynego właściciela tej decyzji, który kod formatu **parsuje, a nie
   przeszukuje** (gotcha #268) — więc własny format walutowy `#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]` zostaje
   pieniędzmi także tutaj, choć biblioteka niezależnie uznała go za datę. Przy okazji domknęła się rozbieżność,
   której nikt by nie szukał: komórkę „sama godzina" `.xlsx` oddawał jako `TimeSpan`, a `.xls` jako
   `1899-12-31 12:00` — teraz obaj oddają `TimeSpan`.

⚠ **R8 zmierzone dla `.xls` przed napisaniem providera** (sonda `XlsFormatProbe`, usunięta po zamknięciu etapu,
zgodnie z regułą dla sond jednorazowych). Skoroszyt BIFF8 o 60 000 wierszy × 5 kolumn: sterta **płaska —
26,7 MB przy wierszu 15 000, 30 000, 45 000 i 60 000**. Kształt krzywej jest tu dowodem, a nie sama końcowa
liczba: czytnik materializujący arkusz rośnie z liczbą wierszy. Zatrzymane 19,6 MB to tablica tekstów (SST),
proporcjonalna do liczby **różnych** napisów, nie do liczby wierszy — ta sama własność, którą I9 opisał dla
`SharedStringTable`.

⚠ **Sprostowanie do I9.** I9 zapisał, że `Wynagrodzenie.xlsx` to „stary format pod nową nazwą". Pomiar I10
uściśla: to plik-kontener OLE2 (sygnatura `d0cf11e0`), ale **nie skoroszyt** — czytnik BIFF odpowiada
*„Neither stream 'Workbook' nor 'Book' was found"*. I10 **nie** daje więc możliwości odczytania tego pliku i
komunikat odmowy niczego takiego nie obiecuje.

⚠ **Zmieniony komunikat odmowy w `XlsxImportProvider`.** Do I10 radził „zapisz jako .xlsx", bo stary format był
nieczytelny w ogóle. Teraz jest czytelny, więc pierwsza rada brzmi „zmień rozszerzenie na `.xls`". Odmowa, która
po powstaniu krótszej drogi nadal poleca dłuższą, to komunikat, który po cichu przestał być prawdziwy.

⚠ **Usunięty `UiStrings.ImportFormatNotYetSupportedFormat`.** Każdy rodzaj źródła, jaki powierzchnia potrafi
rozpoznać, ma teraz providera — komunikat o stanie, który nie może już wystąpić, jest gorszy niż jego brak.

### ⭐ I9 as-built — cztery rzeczy warte zapamiętania

1. **⭐⭐ Filar się utrzymał, i to jest jedyny wynik tego etapu, który naprawdę się liczy.** I9 był
   pierwszym etapem dokładającym nowe ŹRÓDŁO, czyli pierwszym realnym testem §1.4. **Pipeline, konwerter,
   walidator, planer mapowania i writer nie zostały tknięte.** Cała wiedza o skoroszycie mieści się w
   `XlsxImportProvider`; wszystko poniżej `IImportProvider` nadal nie wie, że arkusze istnieją. Sonda H
   pokazuje to najdobitniej: sekcja przeprowadza tę samą podróż, co sekcje A–G, i jedynym nowym elementem
   jest provider. Domknęło się przy okazji ryzyko z reguły #2 — `IImportProvider` ma wreszcie **drugą**
   implementację produkcyjną (§4.3 zapowiadał ten stan jako przejściowy).
2. **⭐ Defekt w heurystyce sondy I0, znaleziony i NIE przeniesiony do produkcji.** Sonda pytała
   `code.Contains('d')`. Własny format z prawdziwego pliku użytkownika —
   `#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]`, opisany w I0 jako *„waluta, NIE data"* — odpowiada na to
   **TWIERDZĄCO**, bo `[Red]` zawiera „d". Sonda tego nie wykryła, bo żaden wiersz nie używał tego stylu
   (zmierzone: „komórki będące liczbą z formatem daty: **0**"). W produkcji zamieniłoby to kolumnę z
   pieniędzmi na daty — po cichu, czyli §0.1 w najgorszej postaci. `SpreadsheetNumberFormats` **parsuje**
   kod formatu (literały w cudzysłowach, escapy, sekcje nawiasowe — `[Red]` odrzucone, `[h]` przyjęte jako
   czas), zamiast go przeszukiwać. ⭐ Morał ogólniejszy: **sonda dowodzi tego, co przypadkiem wykonała** —
   jej „PASS" nie jest dowodem poprawności heurystyki, tylko tego, że dane wejściowe jej nie ruszyły.
3. **⭐ R20 domknięte NOŚNIKIEM, nie nową regułą.** `ImportErrorKind.SourceErrorValue` istniał od I2 (z R20
   w komentarzu), `UiStrings` i mapowanie w raporcie też — brakowało wyłącznie wartości, którą provider
   mógłby powiedzieć „ta komórka jest błędem". Nowy `SourceErrorValue` (Core, **świadomie niezależny od
   formatu pliku** — `RawRecord` jest walutą wspólną dla źródeł) plus jedna gałąź w `ImportValueConverter`
   **PRZED** gałęziami typów docelowych. Kolejność jest tu istotą rzeczy: gałąź tekstowa zwraca
   `Ok(text)` bezwarunkowo, więc gdyby odmowa zależała od typu kolumny, `"#N/A"` wylądowałoby w VARCHAR jako
   dane. Sprawdzone na żywym silniku (sonda **H1b**).
4. **⭐ Epoka Excela poniżej 1900-03-01 nie jest epoką OLE.** Serial 1 to w Excelu 1900-01-01, w
   `FromOADate` 1899-12-31, a do tego Excel niesie widmowy **1900-02-29**, którego nigdy nie było. Ślepe
   `FromOADate` przesunęłoby każdą datę ze stycznia i lutego 1900 o dobę — bez słowa. Korekta jest jawna, a
   widmowy dzień **zostaje liczbą**: konwerter odmówi go dla kolumny DATE z uczciwym komunikatem, zamiast
   wymyślić datę, której nie ma w kalendarzu (§0.1).

⚠ **Domknięta luka pomiarowa z I0 (R3).** I0 uczciwie zapisał, że w pliku użytkownika **nie było ani jednej
komórki daty**, więc obsługa dat była zaprojektowana na arkuszu wygenerowanym. W tej sesji produkcyjny
provider przepuścił **osiem prawdziwych skoroszytów z dysku użytkownika**: plik `Fantomy…` odtworzył pomiary
I0 co do joty (kolumna „Nr technologii" = `double×4999 + string×1`), a `Wyceny.xlsx` **ma prawdziwe komórki
dat** — kolumna „Termin", ~450 dat na siedmiu arkuszach, roczniki 2021–2026, odczytane poprawnie. Luka
zamknięta na realnym wyjściu z Excela, nie na wygenerowanym.

⚠ **Znalezione przy okazji, warte zapamiętania:** plik o rozszerzeniu `.xlsx` **nie musi nim być**.
`Wynagrodzenie.xlsx` z dysku użytkownika to stary format pod nową nazwą i `SpreadsheetDocument.Open` odpowiada
`FileFormatException: File contains corrupted data` — czyli „twój plik jest uszkodzony", podczas gdy prawdziwa
odpowiedź brzmi „to stary format, a ta biblioteka go nie czyta" (I0 §3.5). Provider tłumaczy to na zdanie, z
którym da się coś zrobić.

### ⭐ I8 as-built — cztery rzeczy warte zapamiętania

1. **⭐⭐ `ColumnTypeInferencer` NIE MA WŁASNEGO PARSERA.** Każde pytanie „czy ta wartość mogłaby być
   liczbą / datą / logiczną" zadaje **`ImportValueConverter`** — tej samej klasie, która będzie tę wartość
   konwertowała podczas prawdziwego przebiegu, pod tą samą kulturą. To nie jest schludność, tylko jedyny
   sposób, żeby wnioskowanie i konwersja **nie mogły się rozjechać**: wnioskownik z własnym wyobrażeniem o
   tym, jak wygląda liczba, zaproponowałby typ, którego konwerter potem odmawia — czyli dokładnie bombę
   zegarową z R19. Pinuje to test `EveryValueSeen_ConvertsIntoTheTypeThatWasProposedForIt`.
2. **⭐ `ImportNewTable` jest JEDYNYM właścicielem pytania „czym staje się ta definicja kolumny".** Tekst
   typu, który waliduje podgląd, tekst typu w `CREATE TABLE` i tekst typu, który katalog odda po utworzeniu,
   to **ten sam napis** — z jednego wywołania `DdlGenerator.FormatTypeOrDomain` (§4.6: żadnego drugiego
   generatora, żadnego drugiego modelu kolumny). Gdyby powstawały w dwóch miejscach, rozjazd ujawniłby się
   jako **wiersze odrzucane przez tabelę, którą sam moduł zaprojektował**. Sonda **G4** sprawdza to na żywym
   silniku: katalog oddał `VARCHAR(2), VARCHAR(8), NUMERIC(4,2), DATE` — dokładnie to, o co poprosiliśmy.
3. **⭐⭐ Rzutowanie nowej tabeli na `ImportTarget` (`ImportNewTable.Project`) daje „Waliduj" na tabeli,
   której NIE MA — i to jest jego cała wartość.** Dry-run odpowiada na pytanie „czy te wywnioskowane typy
   naprawdę pomieszczą mój plik" **w jedynym momencie, w którym odpowiedź jest jeszcze darmowa**: po `CREATE`
   tabela jest zatwierdzona i poza zasięgiem Rollbacku (§0.5 / #213). Dzięki temu mapowanie, pasek gotowości
   i podgląd po konwersji działają dla nowej tabeli **bez ani jednej gałęzi specjalnej** — z ich punktu
   widzenia tabela, która będzie, i tabela, która jest, to to samo pytanie. Po prawdziwym `CREATE`
   koordynator **odczytuje cel z KATALOGU**, bo rzutowanie jest przewidywaniem, a katalog faktem.
4. **⭐ Wartość z wiodącym zerem NIE jest liczbą — i to jest reguła §0.1, nie reguła parsowania.** `007`
   parsuje się do 7 bez najmniejszego protestu, ale siódemka i ten tekst to **różne dane**: kod pocztowy,
   indeks, numer konta wracają z bazy inne, niż do niej weszły. To wprost reguła #11 („nigdy nie zmieniaj
   tego, czego nie umiesz odtworzyć identycznie"), więc taka kolumna ląduje jako `VARCHAR`. Pojedyncze zero
   (`0`, `0,5`) zostaje liczbą.

⚠ **Trzy granice przyjęte świadomie.** (a) **Nic nie jest wnioskowane jako `NOT NULL`** — brak dziur w pliku,
który mamy, nie mówi nic o następnym, a ograniczenie przeżywa import; zostaje decyzją użytkownika w siatce.
(b) **`SMALLINT` nie jest nigdy proponowany** — jest trafny dla pliku w ręku i niedobry dla następnego, a
różnica względem `INTEGER` nie jest warta odrzuconego wiersza. (c) **`DOUBLE PRECISION` nie jest kandydatem**
— jedyny tekst, który przyjąłby, a `NUMERIC` nie, to notacja wykładnicza, której konwerter i tak nie
przyjmuje; wybór typu przybliżonego dla wartości wyglądających na dokładne gubiłby cyfry po cichu (§0.1).

**Ponowne wnioskowanie** biegnie według reguły zachowania dowodliwego (§4.7): siatka jest przeliczana, gdy
zmienią się **pola źródła** albo **kultura** (jedno i drugie realnie zmienia, jakie typy są poprawne), a poza
tym zostaje nietknięta. Kolumny z **przywróconej konfiguracji są adoptowane takie, jakie są** — nadpisanie ich
propozycją w chwili otwarcia zakładki byłoby defektem „starszy build po cichu okradł profil" w nowym
przebraniu (§4.8.6). Skan jest najdroższym ogniwem łańcucha i jedzie na tym samym anulowalnym tokenie, co
reszta: nowsza edycja porzuca trwający skan, zamiast się z nim ścigać.

### ⭐ I7 as-built — trzy rzeczy warte zapamiętania

1. **PODGLĄD PO KONWERSJI TO PRAWDZIWY IMPORT, nie jego imitacja.** §3.6 obiecuje, że siatka pokazuje
   „dokładnie to, co trafi do bazy" — i ta obietnica jest prawdziwa wyłącznie dlatego, że wypełnia ją
   `ImportPipeline`: ten sam konwerter, walidator, mapowanie i kultura. Powstały do tego **dwa dodatki w Core,
   oba addytywne**: `BoundedImportProvider` (dekorator ograniczający ODCZYT — bo podgląd nie może czytać
   miliona wierszy, żeby pokazać sto) i `PreviewImportWriter` (writer, który wiersze **zatrzymuje** zamiast
   wysyłać). To ta sama dyscyplina, co „Waliduj to inny argument, nie inny tryb", tylko piętro wyżej:
   **inny provider i inny writer, ten sam jeden import.** Prywatna procedura „przekonwertuj na potrzeby
   wyświetlenia" byłaby drugą ścieżką, a druga ścieżka się rozjeżdża — i nikt by tego nie zauważył.
   ⚠ **Wiersz z błędem pokazuje wartości SUROWE i to nie jest półśrodek:** pipeline zatrzymuje wiersz na
   pierwszej złej wartości, więc taki wiersz **nie ma** wartości przekonwertowanych — a surowe są dokładnie
   tym, co użytkownik ma poprawić.
2. **`CanValidate` jest słabsze od `CanRun` — i to decyzja, nie niedopatrzenie.** Otwarta transakcja robocza
   blokuje **import**, ale nie **walidację**: dry-run nie pisze nigdzie, więc zablokowanie go odmawiałoby
   jedynej operacji, która pomaga *właśnie wtedy*, gdy użytkownik zastanawia się, co zrobić z transakcją.
   Reguła mieszka w Core (`ImportReadinessReport.CanValidate`) — „co ten raport dopuszcza" to pytanie tego
   rekordu; rozstrzyganie go w widoku byłoby drugą opinią o gotowości.
3. **`Batched`: `CommitEveryRows` jest PODŁOGĄ, nie dokładną wielokrotnością — zmierzone na żywo.** Commit
   może paść wyłącznie na granicy paczki (`BatchedCommitImportWriter` jest dekoratorem, a nie zmianą w
   `FirebirdImportWriter`, żeby `Manual` i `AutoCommitOnSuccess` biegły bajt w bajt tym samym kodem), więc
   ląduje na **pierwszej granicy paczki na N lub za N**. Przy zmierzonych wartościach domyślnych (paczka 500,
   commit 10 000) trafia dokładnie w 10 000; interwał commitu **mniejszy** od paczki daje jeden commit na
   paczkę. Alternatywą byłoby skrócenie paczki — a I0 zmierzył, że to właśnie rozmiar paczki kosztuje
   przepustowość, podczas gdy częstotliwość commitu jest niemal darmowa. Wygrywa paczka, ugina się commit,
   i jest to powiedziane wprost, bo to liczba, względem której czytany będzie raport.

Dodatkowo: `DataImportEnvironment` zastąpił pięć pozycyjnych delegatów **jednym nazwanym pakietem** —
I7 dokłada sześciu współpracowników, a jedenaście pozycyjnych argumentów w miejscu wywołania to miejsce, w
którym dwa da się zamienić i nic nie zaprotestuje. Nowe w `EmberTern.Firebird`:
`FirebirdImportTargetPreparer` (`COUNT(*)` + `DELETE FROM` na linii **Data**, w transakcji użytkownika — bo
opróżnienie tabeli to dane, nie schemat) i wspomniany `BatchedCommitImportWriter`.

### Zakres pozostały modułu (stan po I7)

| Etap | Co zostało | Blokady / zależności |
|---|---|---|
| ~~**I6**~~ ✅ | sekcja **Cel** (istniejąca tabela) + panel **Mapowanie** + przeliczanie łańcuchowe z anulowaniem | dostarczone; układ rozstrzygnięty przed implementacją, więc I6 wstawił się w gotową ramę |
| ~~**I7**~~ ✅ | Dostarczone — patrz „I7 as-built" wyżej | pas **B** powstał tutaj, razem z zakładkami Błędy/Raport; **KONIEC MVP** |
| ~~**I8**~~ ✅ | Dostarczone — patrz „I8 as-built" wyżej. Ponadto: **`IMP0028 NewTableAlreadyExists`** (zajęta nazwa blokuje **przed** przebiegiem, bo `CREATE` jest pierwszą rzeczą, którą import robi — inaczej zielony pasek i natychmiast surowy błąd serwera) | wstawiło się w kafelek Cel z I6 bez przebudowy |
| ~~**I9**~~ ✅ | Dostarczone — patrz „I9 as-built" wyżej. Ponadto: **`SourceErrorValue`** (nośnik domykający R20) i **`ListSheetsAsync`** na porcie, dzięki czemu powierzchnia pyta o arkusze *dostawcę*, a nie zna typu skoroszytu | `DataImportXlsxProbe` **usunięta** — jej rolę przejął kod produkcyjny + sekcja H w `DataImportRunProbe` |
| **I10** | schowek (App czyta, Core parsuje) + `XlsImportProvider` (BIFF8, nowa zależność) | — |
| **I11** | **nazwane profile (UI)** — selektor, „Zapisz jako…", zmiana nazwy, usuwanie | ⭐ **dowód projektu**: jeżeli wymaga zmiany choćby jednego modelu, §4.8 zostało po drodze naruszone |
| **I12** | domknięcie: `docs/history/`, `docs/gotchas.md`, CLAUDE.md, audyt UI w obu paletach + **1366×768**, pomiar na 1 M wierszy | audyt UI wchłania to, co zostanie z U1–U10 |

### Co fizycznie istnieje po I5

```
src/EmberTern.App/               ⭐ I5 — pierwszy kod, który widać na ekranie
    ViewModels/
        DataImportTabViewModel.cs        ⭐ koordynator: JEDYNY właściciel ImportConfiguration.
                             Para BuildConfiguration/ApplyConfiguration to jedyny punkt tłumaczenia
                             „stan UI ⇄ rekord" (§4.8.6) — sekcje, których jeszcze nie ma (Cel,
                             Mapowanie, Behavior), są PRZEPUSZCZANE bez zmian, więc profil z
                             nowszego builda nie zostanie po cichu okrojony przez starszy.
                             Łańcuch przeliczeń §4.7: leniwy, anulowalny (CTS), kolejność
                             wykrywania KODOWANIE→SEPARATOR (separator szuka się w tekście, który
                             ktoś już zdekodował — inaczej byłaby to zgadywanka na zgadywance)
        ImportSourceSectionViewModel.cs  sekcja Źródło i format; NIE posiada konfiguracji, tylko
                             produkuje i czyta swój wycinek. `SuspendChangeNotifications` — bez
                             tego propozycja detektora restartowałaby łańcuch, który ją wywołał
        ImportReadinessViewModel.cs      pasek gotowości: czysta PROJEKCJA Core'owego
                             `ImportReadiness` — zero własnych decyzji. Kod→zdanie w jednym
                             miejscu (reguła #6); severity mapowane na wspólną tablicę
                             `MessageBanner.BrushKeyFor/GeometryKeyFor` (§9.3), więc pasek i
                             banner nie mogą opisać tej samej rzeczy inaczej
    Views/DataImportTabView.axaml(.cs)   pasy A–H z §3.1 (I5 dostarcza A, C, D, E, G, H);
                             code-behind buduje wyłącznie dynamiczne kolumny podglądu
    Assets/Icons/Actions/import.svg      ⭐ nowa ikona: strzałka wchodząca W TABELĘ. Świadomie NIE
                             `Icon.Download` (taca = „pobierz plik na dysk") — ten moduł wkłada
                             wiersze do TABELI, więc glif rymuje się z `Icon.Table`

src/EmberTern.Firebird/          ⭐ I4 — pierwszy kod modułu dotykający bazy
    FirebirdImportErrorMapper.cs ⭐ FbException → ImportErrorKind **z WEKTORA GDS, nigdy z tekstu**.
                             Klasy jednoznaczne rozstrzyga kod WIODĄCY (skan całego wektora
                             myliłby FK z duplikatem — dzielą element 335545072); jedyny kod
                             wieloznaczny (335544321) rozstrzyga się dopiero dalszym elementem.
                             Wektor obcięcia niesie limit i długość jako LICZBY → do raportu
                             wprost, bez parsowania komunikatu. `Classify` jest CZYSTE, więc
                             pinuje się je zmierzonymi wektorami bez serwera
    FirebirdImportWriter.cs  `FbBatchCommand`; `MultiError` = polityka błędów 1:1 (I0 §2.3);
                             `OVERRIDING SYSTEM VALUE` dla identity ALWAYS; `CommandLock`
                             per paczka (chwytany raz — #98/#120). Linia Data, transakcja
                             robocza użytkownika, **auto-begin, NIGDY auto-commit** (reguła #3)
    FirebirdImportTargetReader.cs  cienki adapter: kolumny z istniejącego
                             `FirebirdMetadataReader.ListColumnsAsync` (jedyny właściciel
                             pytania „jakie kolumny ma ta tabela"), a jedyne, co dokłada, to
                             lista aktywnych triggerów BEFORE INSERT — dekodowana
                             współdzielonym `DecodeTriggerHeader`, bo `RDB$TRIGGER_TYPE` jest
                             bitowe i test `type = 1` przegapiłby trigger wieloakcyjny
                             (zmierzone: nasz labowy ma typ **17**)

src/EmberTern.Core/Import/
    ── I8 ──────────────────────────────────────────────────────────────────────────────────────
    ColumnTypeInferencer.cs  ⭐ propozycja typów ze SKANU CAŁEGO ŹRÓDŁA (REK-7 / R19; limit 1 M).
                             ⭐⭐ NIE MA własnego parsera — każde „czy to mogłaby być liczba"
                             zadaje `ImportValueConverter`, tej samej klasie, która będzie tę
                             wartość konwertowała. Wnioskowanie i konwersja nie mogą się rozjechać,
                             bo jest tylko jedno. §0.3: kandydat musi pasować do KAŻDEJ wartości;
                             pierwsza, do której nie pasuje, go zabija — a dowód (wartość + numer
                             wiersza) zostaje w `ColumnInferenceEvidence`. Wiodące zero to NIE
                             liczba (reguła #11: `007` ≠ 7). Nic nie jest wnioskowane `NOT NULL`
    ImportNewTable.cs        ⭐ JEDYNY właściciel „czym staje się ta definicja kolumny": tekst typu
                             dla podglądu, `CREATE`/`DROP` przez współdzielony `DdlGenerator`
                             (§4.6 — zero drugiego generatora) i `Project()` → `ImportTarget`,
                             dzięki któremu „Waliduj", mapowanie i podgląd działają na tabeli,
                             KTÓREJ JESZCZE NIE MA — w jedynym momencie, w którym odpowiedź jest
                             jeszcze darmowa (§0.5)
    ── I3 ──────────────────────────────────────────────────────────────────────────────────────
    ImportPipeline.cs        ⭐ JEDEN import: kroki 1–7 z §4.4. Nie wie, CO czyta (provider) ani
                             CZY pisze (writer) — „Waliduj" to inny argument, nie inny tryb, więc
                             nie ma drugiej ścieżki, która mogłaby się rozjechać.
                             ⭐ Właściciel okna „indeks w paczce → numer wiersza źródłowego" (D9):
                             raport nigdy nie widzi indeksu paczki. Obie polityki błędów, dławiony
                             postęp, anulowanie. NIE kończy transakcji (reguła #3) i NIE tworzy
                             tabeli (linia Ddl, przed przebiegiem — #213)
    DryRunImportWriter.cs    druga produkcyjna implementacja IImportWriter — **funkcja produktu
                             („Waliduj"), nie atrapa testowa**; to ona sprawia, że I1–I3 dają pełną
                             funkcjonalność bez bazy
    Providers/
        DelimitedTextImportProvider.cs   CSV / TXT / **schowek** — jeden provider, trzy pochodzenia
                             tekstu (§1.5). Rozstrzyga tu token NULL (własność *czytania* pola
                             tekstowego); nie konwertuje niczego więcej. Szerokość schematu = NAJSZERSZY
                             rekord próbki, nie nagłówek — kolumna, której nagłówek nie nazwał, i tak
                             musi być mapowalna
    ── I2 ──────────────────────────────────────────────────────────────────────────────────────
    ImportTargetType.cs      ⭐ JEDYNY właściciel pytania „jakiego typu jest ta kolumna docelowa":
                             sformatowany typ z katalogu → SqlValueKind + Size/Scale/BlobSubType +
                             zakres liczby całkowitej. Używa SqlValueKind „w drugą stronę" (§4.6);
                             zbiór typów NIEOBSŁUGIWANYCH jest celowo IDENTYCZNY jak po stronie
                             eksportu — czego eksport nie umie zapisać, tego import nie wypełnia
    ImportDiagnostics.cs     ImportSeverity · ImportSection · ImportDiagnosticCode (IMP0001–IMP0027)
                             + ImportDiagnostic. Kody, nigdy teksty (reguła #6). JEDEN katalog dla
                             planera i dla gotowości — dwa katalogi mogłyby się rozjechać
    ImportValueConverter.cs  ⭐ ścisła konwersja (§0.1): wartość pewna albo odmowa z powodem.
                             + ImportValueResult (readonly struct — jedna waluta kroków 3 i 4)
    ImportRowValidator.cs    NOT NULL · długość (+ opcjonalne przycięcie) · precyzja/skala ·
                             ⭐ ImportCharsetGuard — reprezentowalność w charsecie POŁĄCZENIA
                                z EncoderExceptionFallback (R1/REK-2, warunek §0)
    ImportMappingPlanner.cs  auto-mapowanie po nazwie · reguła zachowania dowodliwego (§4.7) ·
                             reguła jedynej pary · Diagnose() · Project() (krok 2 pipeline'u)
    ImportReadiness.cs       ⭐ czysta funkcja gotowości (§3.2) + ReadinessItem/Input/Report
    ── I1 ──────────────────────────────────────────────────────────────────────────────────────
    ImportEnums.cs           ImportSourceKind · ImportMode · ImportTransactionMode · ImportErrorPolicy
                             ImportTargetKind · MappingOrigin · DateFieldOrder · LineEndingMode
                             ImportErrorKind  ← kindy klienckie i serwerowe, z komentarzem, dlaczego
                                                niektóre klasy błędów są NIEROZRÓŻNIALNE (I0 §2.6);
                                                I2 dołożył 3 kindy klienckie (niżej)
    ImportOptions.cs         DelimitedOptions · SpreadsheetOptions · ImportCultureOptions
                                                (+ BuildNumberFormat / IsTrueToken / IsFalseToken)
    ImportConfiguration.cs   ⭐ ImportConfiguration + SourceDescriptor · TargetDescriptor
                             ImportColumnDefinition · ColumnMapping · ImportBehaviorOptions
    ImportModels.cs          SourceField · SourceSchema · RawRecord · ImportTarget · ImportRow
                             ImportRowError · ImportProgress · ImportBatchItemResult
                             ImportWriteSummary · ImportOutcome
    ImportContracts.cs       IImportSource · IImportProvider · IImportWriter (+ ImportProviderCapabilities)
    ImportProfile.cs         ImportProfile (encja trwała)
    ImportProfileStore.cs    fasada sekcji settings.dat — GetLastUsed / SaveLastUsed / ClearLastUsed
    Providers/
        DelimitedTextReader.cs   RFC 4180, strumieniowy (ReadAll / ReadSample)
        DelimiterDetector.cs     DelimiterProposal + dowody liczbowe
        EncodingDetector.cs      EncodingProposal + EncodingDetectionBasis + ByteOrderMarkLength
        FileImportSource.cs      IImportSource nad plikiem (+ ReadDetectionSample)
        TextImportSource.cs      IImportSource nad string (schowek)
```

Zmiany w plikach współdzielonych — **obie addytywne, obie zaakceptowane**:
- `Core/Connections/CharsetCatalog.Resolve` → rozpoznaje `UTF16LE` / `UTF16BE`. Jest już **jedynym**
  właścicielem odwzorowania „nazwa charsetu → `Encoding`", więc drugie takie odwzorowanie byłoby rozjazdem.
  Nazwy **nie** weszły do `Supported` (to lista charsetów POŁĄCZENIA, a Firebird takich nie ma).
- `Core/Settings/UserSettings` → `List<Import.ImportProfile> ImportProfiles`. **Wersja schematu
  settings.dat celowo NIE podbita** (podbicie uruchamia ochronę przed downgrade'em i starszy build
  odmówiłby odczytu całego pliku).

### Testy I8 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ColumnTypeInferencerTests` (36) | ⭐⭐ **`EveryValueSeen_ConvertsIntoTheTypeThatWasProposedForIt`** — każda wartość, którą wnioskownik zobaczył, musi przejść przez konwerter na typie, który dla niej zaproponował; gdyby to kiedyś padło, moduł zaprojektowałby tabelę, której własny import nie umie wypełnić (R19 odtworzone) · ⭐ **kolumna mieszana → `VARCHAR` z nazwaniem wartości i WIERSZA, który przesądził** · ⭐ **wiodące zero to nie liczba**, ale pojedyncze zero i `0,5` już tak · skan obejmuje CAŁE źródło (dyskwalifikująca wartość leży za 3. wierszem — próbka by ją przegapiła) · limit bezpieczeństwa mówi, że zadziałał · anulowalność · `INTEGER`→`BIGINT` tylko gdy trzeba, nigdy `SMALLINT` · precyzja i skala z tego, co widziano · liczba szersza niż 18 cyfr → tekst · `0`/`1` czytane jako `INTEGER`, nie jako `BOOLEAN` · długość `VARCHAR` = najdłuższa napotkana wartość · bardzo długi tekst → `BLOB SUB_TYPE 1` · **nic nie jest `NOT NULL`** · nazwy kolumn przez `ImportMappingPlanner.NormalizeName` (więc planer paruje je z powrotem bez gałęzi specjalnej) + rozstrzyganie duplikatów |
| `ImportNewTableTests` (13) | ⭐⭐ **każdy typ, który ten moduł emituje, `ImportTargetType` czyta z powrotem** jako typ, który import umie zapisać — rzutowanie i katalog opisują tę samą kolumnę · ⚠ jedno `Size` z `ImportColumnDefinition` trafia do WŁAŚCIWEJ szuflady `FieldDefinition` (`Size` dla tekstu, `Precision` dla `NUMERIC`) · rozmiar na typie, który go nie bierze, nie wycieka do DDL · `CREATE` z współdzielonego generatora, z cytowaniem nazw · **żadnych wymyślonych ograniczeń** (PK / identity / DEFAULT) · wiersz bez nazwy to nie kolumna · rzutowanie: każda kolumna zapisywalna, zero triggerów |
| `DataImportNewTableTests` (17) | ⭐⭐ **`TheTableIsCreatedBeforeTheFirstRow`** — gotcha #213 jako asercja KOLEJNOŚCI (`create` przed `begin`), bo każde zdanie powierzchni o Rollbacku wynika właśnie z niej · ⭐⭐ **`Validate_AnswersTheQuestion_WithoutCreatingAnything`** — dry-run na rzutowaniu, więc pytanie „czy te typy pomieszczą mój plik" ma odpowiedź, zanim cokolwiek powstanie · ⭐ **writer dostaje cel z KATALOGU, nie rzutowanie** (fikcyjny katalog celowo oddaje `VARCHAR(999)` tam, gdzie rzutowanie mówi `VARCHAR(2)`, więc test nie może przejść przypadkiem) · zajęta nazwa blokuje PRZED przebiegiem i żaden DDL nie leci · nieudany `CREATE` zatrzymuje przebieg, zanim poleci pierwszy wiersz · ⚠ **sprzątanie to dwa skutki i jedno pytanie**: najpierw Rollback, potem `DROP`, a pytanie mówi o obu · odmowa zostawia tabelę · udany import nigdy nie sprząta · domyślnie wyłączone · edycja typu dociera do rekordu i do DDL · **kolumny z przywróconego profilu nie są nadpisywane świeżą propozycją** |

### Testy I6 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `DataImportTabVmTests` (I6, +15) | ⭐ **siatka pokazuje KAŻDĄ kolumnę tabeli**, także tę, której nigdy nie da się zapisać — z powodem, nie przez pominięcie (brakujący wiersz to pytanie, którego użytkownik nie może nawet zadać) · auto-dopasowanie po nazwie **pochodzi z planera**, VM je tylko rysuje · ⭐ **identity ALWAYS zablokowane do jawnego odblokowania** (R10) · ⭐ **reguła jedynej pary sięga też po kolumnę identity — i to jest zamierzone**: Core uznaje ją za mapowalną i podnosi `IMP0007`, więc nic nie dzieje się po cichu · ręczna zmiana **kasuje pochodzenie automatyczne** i dociera do rekordu · „nie importuj" zapisuje się jako **pominięcie**, nie jako brak · ⭐ **inna tabela nie dziedziczy mapowania** · ⭐⭐ **wyczyszczenie CELU czyści siatkę, ale NIE rekord** — inaczej przywrócony profil traciłby parowanie tylko dlatego, że celu jeszcze nie odczytano · lista pól nieużywanych (fixture ma **dwa** zapasowe pola, żeby reguła jedynej pary nie zaliczyła testu przypadkiem) · cel i „opróżnij tabelę" docierają do JEDNEGO rekordu · ⭐ **konfiguracja z NOWĄ tabelą jest przepuszczana bez zmian** (to etap I8) · gotowość widzi wybrany cel · filtr „tylko niezmapowane" zmienia widok, nigdy rekordu |

### Testy I5 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `DataImportTabVmTests` | ⭐⭐ **`Configuration_SurvivesABuildApplyRoundTrip`** — obietnica §4.8.6 na poziomie App: ustawienie dodane prosto do VM-a sekcji byłoby niewidoczne dla profilu, a defekt wyszedłby dopiero w I11 jako „przebuduj powierzchnię"; ten test wywala się pierwszy · ⭐ **sekcje, których jeszcze nie ma, są przepuszczane** (starszy build nie okrada profilu nowszego) · ⭐ **wiersz odstający od WIĘKSZOŚCI jest oznaczany, nie od najszerszego** · detekcja wpisuje wartość ZADEKLAROWANĄ i publikuje dowód; wyłączona — nie rusza niczego · brak pliku i arkusz to **odmowa z powodem**, nie wyjątek i nie cisza · fakty środowiska czytane jako DELEGATY (pasek pokazuje stan teraz, nie z chwili otwarcia) · ⭐ **każdy kod diagnostyczny ma zdanie** (inaczej słownik Core wycieka do użytkownika) · każdy chip ma rozwiązywalny klucz pędzla i geometrii (#250) |

⚠ **Reguła QA projektu:** build 0/0, 5559 zielonych i czysty start aplikacji **nie wystarczają**, żeby nazwać
etap UI zrobionym — i I5 jest tego dowodem. Przegląd wzrokowy **odbył się 2026-07-26** i znalazł pięć rzeczy,
których żaden z 296 testów importu nie mógł znaleźć, bo wszystkie dotyczą **proporcji i przestrzeni**, a nie
stanu: sekcja parametrów zjada pion, dolny panel nie ma splittera, nagłówek pasa A jest zbędny, globalne
kontrolki są za wysokie, a na Full HD formularz nie mieści się w pionie. **Zapisane jako U1–U5 w §3.8.**

### Testy I4 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ImportErrorMapperTests` | **każdy wektor to wektor ZMIERZONY** na żywym FB5, przepisany dosłownie — więc gdy przyszły Firebird zmieni kod, test się wywala i ktoś mierzy ponownie, zamiast po cichu wysłać zły raport; ⭐ trzy klasy dzielące kod wiodący `335544321` rozdzielają się poprawnie; limit i długość czytane **po wartości, nie po pozycji** (w zmierzonym wektorze między dyskryminatorem a liczbami stoi jeszcze jeden kod GDS); ⭐ **skan całego wektora myliłby FK z duplikatem** — dlatego decyduje kod wiodący; ⭐⭐ **samodzielny `UNIQUE INDEX` wiedzie innym kodem niż ograniczenie** (znalezione przebiegiem na żywo, nie w I0) |
| `ImportFirebirdWriterTests` | ⭐ `OVERRIDING SYSTEM VALUE` emitowane **tylko** dla zmapowanej kolumny identity ALWAYS (brak klauzuli = śmierć na pierwszym wierszu, klauzula zbędna = równie źle); cytowanie identyfikatorów z podwojeniem cudzysłowu; ⭐ `MultiError` ↔ `ImportErrorPolicy` 1:1 — odwrócenie tego jest niewidoczne aż do momentu, gdy zmienia to, co przebieg **robi** |
| `tools/probes/DataImportProbe` (żywy FB5) | **20/20 ALL PASS.** 7 klas błędów serwera z właściwym rodzajem **i właściwym numerem wiersza ŹRÓDŁOWEGO** (zły wiersz to zawsze 3. wiersz danych, nigdy 1. — żeby przesunięcie o jeden ani wyciek indeksu paczki nie przeszły przypadkiem) · 3 przypadki dowodzące, że **strażniki klienckie strzelają PIERWSZE** · ⭐ numer wiersza przeżywa **granicę paczki** · 10 000 wierszy zgodnych z `SELECT COUNT(*)` · ⭐ writer **nigdy nie zatwierdza** (Rollback usuwa wszystko) · ⭐ trigger wieloakcyjny znaleziony · charset odmawia po stronie klienta |

⚠ **Trzy klasy serwerowe wymagały triggera, żeby dało się je w ogóle wywołać na żywo** (`IMP_SRV`): klient
waliduje NOT NULL, długość i zakres **przed** round tripem — co jest poprawne i zgodne z §0, ale znaczy, że
bez triggera produkującego te błędy *wewnątrz silnika* gałęzie rozróżniania wektora nie miałyby jak zostać
sprawdzone przeciwko prawdziwemu serwerowi.

### Testy I3 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ImportPipelineTests` | ⭐⭐ **`BatchFailure_IsReportedAgainstTheSourceRow_NotTheBatchIndex`** — writer wywala pozycję 1 **drugiej** paczki, co przy nagłówku i paczce po 2 odpowiada wierszowi źródłowemu **5**; pipeline przepuszczający indeks powiedziałby „wiersz 1" i wysłał użytkownika pod zły wiersz pliku (fixture celowo trzyma obie liczby różne, żeby test nie przeszedł przypadkiem) · ⭐ **dry-run i prawdziwy writer dają identyczny wynik** (inaczej „Waliduj mówi OK" przestaje coś znaczyć) · obcięty wynik paczki (`MultiError=false`) **nie zmyśla werdyktów dla wierszy, których nie spróbowano** · ⭐ **wartość przycięta to OSTRZEŻENIE z oryginałem, nie błąd** i nie zawyża `RowsFailed` · ⭐ **anulowanie nie porzuca wierszy już przyjętych** (ogonowy flush na nieanulowanym tokenie) · limit listy błędów przy dokładnych licznikach · R1 przez cały pipeline · odmowa startu przy pustym mapowaniu i przy mapowaniu na nieistniejącą kolumnę |
| `ImportDelimitedProviderTests` | schemat z nagłówka i bez (etykiety pozycyjne + `HasRealName=false`); ⭐ **szerokość z najszerszego rekordu, nie z nagłówka**; nagłówek pomijany **oknem wierszy, nie przypadkiem szczególnym** (linie bannerowe nad nagłówkiem działają); pole wielolinijkowe to JEDEN rekord, więc numeracja raportu zostaje numeracją pliku; token NULL domyślny i zadeklarowany (bez rozróżniania wielkości liter); strumieniowość i anulowanie; **brak zmyślonej liczby wierszy** |

### Testy I2 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ImportValueConverterTests` | rozpoznanie każdego typu z katalogu + **zbiór typów nieobsługiwanych**; NULL i pole puste; każda szerokość liczby całkowitej i jej zakres; ⭐ **`"1.5"` przy przecinku dziesiętnym to BŁĄD, nie 1,5 i nie 15**; ⭐ **`03.04.2026` czytane wyłącznie w zadeklarowanej kolejności pól** (DMY → 3 kwietnia, MDY → 4 marca); data z godziną do kolumny `DATE` odrzucona; wartości natywne z arkusza; wartość surowa zachowana dla raportu |
| `ImportRowValidatorTests` | ⭐⭐ **`Guard_WithoutTheExceptionFallback_WouldSilentlyCorrupt`** — odtwarza samą korupcję (`Ж` → `?`), więc „uproszczenie" fallbacku wywala test, który mówi, co właśnie włączono z powrotem; ⭐ **charset POŁĄCZENIA decyduje, nie kolumny**; polski tekst w WIN1250 przechodzi (inaczej strażnik zostałby wyłączony w jeden dzień); ⭐ **NOT NULL z DEFAULT-em i tak odrzuca NULL w kolumnie ZMAPOWANEJ**; ⭐ **`1,50` w `NUMERIC(15,1)` NIE jest utratą precyzji** (porównanie po wartości, nie po zapisanej skali); przycinanie tylko na życzenie i zawsze z oryginałem |
| `ImportMappingPlannerTests` | normalizacja nazwy (spacja ≡ podkreślenie), ale **bez zdejmowania diakrytyków**; ⭐ **nazwa niejednoznaczna nie łączy NICZEGO**; ⭐ **reguła jedynej pary nie działa przy dwóch kandydatach z każdej strony**; ⭐ **R16: przestawienie kolumn w pliku — mapowanie idzie za NAZWĄ, nie za pozycją**; pominięcie przeżywa ponowny odczyt; kolumna `COMPUTED`/o typie nieobsługiwanym nigdy nie zmapowana, ale **widoczna z powodem**; `NOT NULL` z DEFAULT-em nie blokuje, gdy jest niezmapowana; `Project` nie dopełnia rekordu poszarpanego |
| `ImportReadinessTests` | pełna macierz blokujące vs ostrzegawcze; ⭐ **wszystkie braki raportowane naraz** (przewaga nad przyciskiem „Dalej"); ⭐ **`NewTableWillBeCommitted` ostrzega, ale NIE blokuje** (§0.5 / #213); ⭐ **wnioski o mapowaniu pochodzą z planera, nie z drugiej analizy**; projekcja na sekcje (`SeverityFor` / `IsSectionRunnable`); każdy bloker wskazuje sekcję |

### Testy I1 (nie upraszczać ich w kolejnych etapach — decyzja użytkownika)

| Plik | Co pinuje |
|---|---|
| `ImportDelimitedReaderTests` (44) | cudzysłowy, podwojone cudzysłowy, separator i łamanie linii w wartości, CR/LF/CRLF + tryby jawne, puste pola, rekordy poszarpane (bez dopełniania), przycinanie tylko poza cudzysłowami, **pominięty pusty wiersz nie przesuwa numeru rekordu** |
| `ImportConfigurationRoundTripTests` (11) | ⭐ **refleksyjny strażnik**: każda zapisywalna właściwość (rekurencyjnie) musi być ustawiona na wartość niedomyślną **i** przetrwać prawdziwy serializator. Porównanie jest **strukturalne**, bo równość rekordów porównuje `IReadOnlyList` przez referencję i przepuściłaby round trip, który zgubił wszystkie elementy |
| `ImportDetectorTests` (20) | propozycje separatora i kodowania **wraz z dowodami**; „plik jest czystym ASCII i nie rozróżnia kodowań" jako jawny wynik |
| `ImportProfileStoreTests` (11) | round trip przez settings.dat, zakres per połączenie, brak klobrowania innych sekcji, **odrzucenie konfiguracji z przyszłej wersji w całości**, brak podbicia wersji schematu |

⚠ **Dwa wyjątki w strażniku refleksyjnym**, oba samo-unieważniające się: `Version` (znacznik schematu —
fixture udający przyszłą wersję byłby nonsensem, pinowane osobno w `ImportProfileStoreTests`) oraz `Mode`
(`ImportMode` ma w v1 jeden element, więc nie istnieje wartość niedomyślna) — ten drugi jest **warunkowy**
i osobny test `ImportMode_StillHasOneMember_OrTheFixtureMustCoverIt` wywala się w dniu, w którym enum
urośnie. **Wyjątku nie wolno rozszerzać na nową właściwość** — jeśli strażnik protestuje, poprawką jest
ustawienie tej właściwości w `Fully()`.

### Odstępstwa od dokumentu przyjęte w trakcie implementacji

| Odstępstwo | Powód | Status |
|---|---|---|
| `TrimWhitespace` **tylko** w `DelimitedOptions`, mimo że szkic v2 §4.8.2 wymieniał je też w `ImportBehaviorOptions` | przycinanie białych znaków jest własnością *czytania pola tekstowego*, a sekcja Format pokazuje je tam; dwa domy dla jednej decyzji to rozdwojenie, przed którym broni zasada jednego właściciela | ✅ zaakceptowane 2026-07-26; **nie dodawać drugiego pola tylko po to, by zgadzało się ze szkicem — dokument ma odzwierciedlać poprawioną architekturę, nie odwrotnie** |
| `IImportProvider` dostaje całą `ImportConfiguration`, nie wybrany obiekt opcji | konfiguracja jest jedyną reprezentacją tego, o co poprosił użytkownik (§4.8.1); provider czyta tylko swój blok | ✅ w ramach swobody „sygnatury poglądowe" z §4.3 |
| `DelimitedTextImportProvider` **nie** powstał w I1 | wiersz I1 w §6 go nie wymienia; pierwszym etapem, który go potrzebuje, jest I3 (pipeline end-to-end na `TextImportSource`) | ✅ **dostarczony w I3.** Reguła #2 jest teraz spełniona dla dwóch z trzech portów: `IImportSource` (plik + tekst) i `IImportWriter` (dry-run + Firebird w I4). `IImportProvider` ma jedną implementację do czasu `XlsxImportProvider` (I9) — przejściowo, zgodnie z §4.3 |
| **I3: `ImportOutcome` +`Warnings` / +`WarningsTruncated`** (właściwości `init`, nie parametry pozycyjne ⇒ zero zmian w istniejących wywołaniach) | §0.2 wymaga, żeby **każdy skrócony wiersz trafił do raportu z oryginalną wartością**, a taki wiersz nie jest ani błędem (wszedł), ani ciszą (dane przepadły). Wrzucenie go do `Errors` zawyżałoby `RowsFailed` o wiersze, którym się udało — czyli raport by kłamał (§0.6) | ✅ addytywne; zgłoszone jako as-built 2026-07-26. Kind pozostaje `ValueTooLong` — przyczyna jest ta sama, a to, **na której liście** wpis leży, mówi, co z nią zrobiono (odmowa vs skrócenie); nowy kind byłby czwartym w dwa etapy bez zysku informacyjnego |
| **I3: `ImportPipeline.RunAsync` bierze `target` i `connectionEncoding`** ponad szkic z §4.4 | `ImportTarget` to **fakt odczytany ze świata**, więc z definicji nie leży w konfiguracji (§4.8.2), a bez charsetu połączenia walidacja R1 nie ma czym się posłużyć. §4.3 nazywa sygnatury „poglądowymi" | ✅ w ramach swobody z §4.3 |
| **I3: `RunAsync` jest `static`** | pipeline nie trzyma stanu między przebiegami (cały stan przebiegu jest lokalny), a §4.3 wymienia go wśród „zwykłych klas", nie portów. Forma wywołania zgadza się ze szkicem `ImportPipeline.RunAsync(...)` z §4.4 | ✅ |
| **I3: `CreatedTable` zwracane jako `null`** | tworzenie tabeli dzieje się na **linii Ddl, przed** przebiegiem (§4.5 / #213), więc pipeline nie ma o nim wiedzy. Koordynator uzupełnia je przez `outcome with { CreatedTable = … }` | ℹ️ do podłączenia w I7/I8 |
| **I2: `ImportErrorKind` +3 kindy klienckie** — `ValueOutOfRange`, `PrecisionWouldBeLost`, `UnsupportedTargetType` | DoD etapu I2 brzmi „**zero cichych konwersji**", a bez nich nie da się go spełnić: liczba poprawna, lecz za duża dla kolumny, zaokrąglenie `1,555` → `1,56` w `NUMERIC(15,2)` i kolumna typu, którego nie umiemy zapisać, nie miały czym być zaraportowane. Zgłoszenie ich jako `NotAnInteger` byłoby **przekłamaniem powodu**. Addytywne: żaden port, przepływ, model ani decyzja się nie zmieniają | ✅ zaakceptowane 2026-07-26 (zgłoszone jako as-built, nie jako zmiana projektu). **Świadomie NIE dodano opcji „zaokrąglij mimo to"** — to byłaby decyzja projektowa, a §0.1 domyślnie nakazuje odmowę |
| **I2: `ImportTargetType` jako osobna klasa**, choć §4.2 jej nie wymienia | §4.6 nakazuje użyć `SqlValueKind` „w drugą stronę" i nie tworzyć drugiego modelu typu. Pytanie „ile znaków ma `VARCHAR(20)`" zadają **cztery** komponenty (konwerter, walidator, planer, gotowość); cztery niezależne wyprowadzenia to dokładnie sposób, w jaki kontrola długości i ostrzeżenie o długości zaczynają mówić użytkownikowi co innego | ✅ zaakceptowane 2026-07-26 — to realizacja „Single Source of Truth" z §1.4, nie nowy byt architektoniczny |
| **I2: `ImportRowValidator` dostaje `ImportBehaviorOptions` + `Encoding`**, a nie tylko `(value, ColumnSpec)` | przycinanie (`TrimTooLongValues`) i charset połączenia to **wejścia** walidacji wymienione wprost w §4.4 krok 4; §4.3 nazywa sygnatury „poglądowymi" | ✅ w ramach swobody z §4.3 |
| **I6: liczba rekordów tabeli NIE jest pokazywana w linii faktów**, mimo że szkic §3.4 ją wymienia | to `SELECT COUNT(*)` przy **każdej** zmianie celu — na dużej tabeli sekundy, na produkcyjnej bazie z 2388 tabelami koszt jest realny. Decyzja, której ta liczba służy („zaraz skasujesz N wierszy"), zapada **przy starcie importu**, więc liczbę odczytamy raz, w I7, tam gdzie jest potrzebna | ✅ zgłoszone jako as-built 2026-07-26; **do wykonania w I7** wraz z potwierdzeniem opróżnienia |
| **I6: wariant „Nowa tabela" nie jest pokazany nawet jako wyłączony przełącznik** | opcja, która wygląda na wybór i prowadzi donikąd, to kłamstwo, którego pasek gotowości nie skoryguje — ta sama zasada, dla której I5 nie zbudował paska poleceń. `TargetDescriptor` z nową tabelą jest mimo to **przepuszczany bez zmian** (pinowane testem) | ✅ dochodzi w I8 |
| **I6: reguła jedynej pary może sparować kolumnę identity `GENERATED ALWAYS`** | pozorna sprzeczność z §3.5 („tylko po jawnym odblokowaniu") jest rozstrzygnięta **w samym Core**: `IsMappable` nie wyklucza identity, a `Diagnose` podnosi wtedy `IMP0007` — *„akcent, nie usterka, ale nigdy po cichu"*. Blokada w UI dotyczy więc sięgania po tę kolumnę **ręcznie**; gdy sparował ją planer, wiersz pokazuje się odblokowany, oznaczony jako „założone", a writer emituje `OVERRIDING SYSTEM VALUE` (I4). Nic nie dzieje się milcząco | ✅ zachowanie zgodne z zamrożonym Core; **żadnej zmiany w Core** — udokumentowane i pinowane testem |
| **I2: `ImportSeverity` / `ImportSection` żyją w Core** | pasek gotowości ma używać **tej samej** mapy `Severity` → pędzel co `MessageBanner` (§9.3), ale `MessageSeverity` mieszka w `App.Controls`, a Core nie może zależeć od App (reguła #1). Core zwraca więc własną trójwartościową severity, a App mapuje ją **jednym przejściem** na `MessageSeverity` i dalej już przez `BrushKeyFor`/`GeometryKeyFor` — druga mapa pędzli nie powstaje | ✅ zgodne z §9.3; do wykonania po stronie App w I5 |

### Stan dokumentów projektowych

| Dokument | Rola | Stan |
|---|---|---|
| `docs/design/data-import.md` (ten) | jedyna architektoniczna prawda modułu | 🔒 **ZAMROŻONY** (v3). Zmiany tylko „w miejscu" i tylko o stan faktyczny |
| `docs/design/data-import-i0-findings.md` | archiwum dowodowe pomiarów I0 | zamknięty, 8 rekomendacji zaakceptowanych |
| `tools/probes/DataImportWriteProbe` | sonda ścieżki zapisu (I0, surowy sterownik) | ✅ **I4 zamknięty — do usunięcia.** Jej rolę przejęła `DataImportProbe`, która sprawdza to samo, ale **kodem produkcyjnym** |
| `tools/probes/DataImportProbe` | ⭐ weryfikacja na żywo **kodu produkcyjnego** (I4) | **trzymać** — to jest regresyjny dowód warstwy Firebirda; uruchamiać po każdej zmianie writera lub mappera |
| ~~`tools/probes/DataImportXlsxProbe`~~ | sonda odczytu `.xlsx` | **usunięta w I9** — zastąpiona kodem produkcyjnym, `XlsxImportProviderTests` i sekcją H `DataImportRunProbe` |
| `docs/history/` + `docs/gotchas.md` + CLAUDE.md (pełny wpis) | narracja i katalog gotch | **planowo w I12** (wiersz „Domknięcie" w §6) |

---


---

### Uwagi z przeglądów wzrokowych — pełny zapis (było §3.8)

### 3.8. ⏳ Uwagi z przeglądu wzrokowego I5 (2026-07-26) — OTWARTE, czekają na decyzję

> **Status: WSZYSTKIE PUNKTY ROZSTRZYGNIĘTE 2026-07-26; U1/U2/U3/U6/U7/U8/U9/U10/U11 DOSTARCZONE w szwie
> domykającym I5.** Punkty **U1–U5** pochodzą z przeglądu użytkownika, **U6–U10** z zamówionego przy tej
> samej okazji autoprzeglądu UX, **U11** wyszło przy projektowaniu układu. Żaden nie zmienił architektury
> modułu — wszystkie dotyczą proporcji, przestrzeni i tego, w którym pasie mieszka dana informacja;
> `ImportConfiguration`, pipeline i podział warstw są nietknięte. Rewizja układu jest wniesiona **w miejscu**
> do §3.1 (z powodem odejścia od makiety v2), a tabela niżej zostaje jako zapis, **co** zgłoszono i **jak**
> to rozstrzygnięto.
>
> **⭐⭐ U5 — ROZWIĄZANY INACZEJ, PRZEZ PODZIAŁ ODPOWIEDZIALNOŚCI (2026-07-27).** Po cofnięciu podłogi
> użytkownik postawił właściwą diagnozę: **problem nie był w wysokościach, tylko w tym, co gdzie mieszka.**
> „Preview after conversion" jest **wynikiem** działania pipeline'u, a nie częścią konfiguracji — a siedział
> w powierzchni roboczej i zabierał połowę jej szerokości Mapowaniu, które jest **decyzją**. Siatka typów,
> też decyzja, siedziała piętro wyżej w pasie `Auto`. Konfiguracja i wynik były przemieszane i biły się o te
> same piksele. Po rozdzieleniu — **góra = konfiguracja importu, dół = wyniki i diagnostyka** — miejsce
> znalazło się samo, bez podłogi i bez ani jednej nowej sekcji pionowej. Szczegóły w §3.1.
>
> **Historyczny zapis próby, która nie wypaliła (zostaje jako przestroga):**
>
> **U5 — poprzednie podejście, COFNIĘTE, decyzja użytkownika z 2026-07-27.** Weryfikacja, na którą czekał, wypadła
> negatywnie: gdy sekcja Cel faktycznie zajęła miejsce — w wariancie „New table", z siatką typów — powierzchnia
> robocza potrafi zejść do zera, bo wiersz `*` nie ma podłogi. **Podłoga została dodana i po obejrzeniu w
> działaniu COFNIĘTA**: żeby ją uszanować, dolny panel musiał ustąpić, przez co środek okna urósł, a dolny
> panel przestał być użyteczny — lekarstwo czytało się gorzej niż choroba. Użytkownik ratyfikował, gdzie leży
> prawdziwa naprawa: **sprint UX całego EmberTerna**, gdzie wysokości kontrolek spadają i powierzchnia odzyskuje
> ~100 px, po których problem znika sam. ⛔ **Nie dodawać podłogi z powrotem i nie odpowiadać na problem
> wysokości kolejną sekcją pionową.**
>
> **Otwarte świadomie poza modułem: U4** (gęstość globalna) **i U5** — oba do sprintu UX.

#### Uwagi użytkownika

| # | Uwaga | Stan faktyczny w kodzie | Zakres |
|---|---|---|---|
| **U1** | **Sekcja parametrów zajmuje za dużo pionu** — podgląd źródła praktycznie nie jest widoczny, przestrzeń robocza znika natychmiast | Rozwinięta sekcja *Źródło i format* to ~14 wierszy kontrolek w dwóch kolumnach (`Parsowanie` ~6 wierszy + 2 dodatkowe siatki, `Kultura danych` 5 wierszy). Kolumny są **niezbalansowane**: lewa jest wyraźnie wyższa, prawa kończy się pustką, a wysokość całości bierze się z wyższej | moduł |
| **U2** | **Dolny panel nie ma splittera** — ma zachowywać się jak dolne panele SQL Editora i Debuggera: przeciąganie wysokości, pełne zwijanie, **zapamiętywanie ostatniej wysokości** | Pas G ma dziś `Height="190"` na sztywno, wiersz `Grid.Row=4` jest `Auto`, `GridSplitter` **nie istnieje**, a zwijanie to sam `IsVisible`. Dodatkowo §3.1 obiecuje zwijanie **podwójnym kliknięciem paska zakładek** — też niezbudowane | moduł |
| **U3** | **Nagłówek „Data Import" (pas A) jest zbędny** — żaden inny moduł EmberTerna go nie ma, tytuł zakładki wystarcza, pion do odzyskania | Pas A pokazuje **wyłącznie** ikonę + tytuł. Uwaga trafia w sedno podwójnie: §3.1 przewidywał w tym pasie także **aktywne połączenie i linię** („SZKOLENIE.FDB · linia Data") — zbudowana została redundantna połowa, informacyjna nie | moduł |
| **U4** | **Gęstość interfejsu całej aplikacji** — globalny styl daje za wysokie `TextBox` / `ComboBox` / `CheckBox` / `Button`, za duże odstępy pionowe i wysokości wierszy formularzy. **Nie rozwiązywać lokalnie** | Potwierdzone w kodzie: `ControlStyles.axaml` **nie ma ani jednego stylu domyślnego** dla `TextBox`, `ComboBox`, `CheckBox`, `RadioButton`, `NumericUpDown` ani gołego `Button` — wszystkie siedzą na wartościach FluentTheme (`MinHeight` 32 px). Zagęszczone są tylko `DataGridRow`/`DataGridCell` i `TabItem`, i to **doraźnie, po jednym**. Czyli precedens istnieje, brakuje uogólnienia | ⛔ **BACKLOG — poza modułem.** Decyzja użytkownika 2026-07-26: osobny **sprint UX całego EmberTerna po zamknięciu Data Import**, projektowany z oglądu wszystkich modułów naraz. **Nie implementować niczego globalnego w etapach importu** |
| **U5** | **Responsywność** — na Full HD część formularza już nie mieści się w pionie; przeanalizować układ I5–I7 pod kątem wykorzystania przestrzeni i **zaproponować zmiany przed implementacją**, jeśli praktyka pokazuje lepsze rozwiązanie niż projekt | Realne: I6 dokłada sekcję **Cel** i panel **Mapowanie**, I7 pas **B** i panel **Podgląd po konwersji** — czyli do pionu, który już jest ciasny, dochodzą cztery obszary. R13 w §7 przewidywał to ryzyko dla 1366×768; przegląd pokazał, że dotyczy już 1920×1080 | moduł, przed I6 |

#### Propozycje z autoprzeglądu UX (nie zamawiane przez uwagi wyżej)

| # | Obserwacja | Propozycja |
|---|---|---|
| **U6** | **Pasek gotowości nie ma sufitu.** §3.2 gwarantuje zwinięcie do jednej linii, gdy wszystko jest zielone — ale przypadek odwrotny (wiele braków) rośnie bez ograniczenia: każdy wynik to własny zawijany wiersz, więc pas potrafi zająć ponad 100 px **na stałe nad** powierzchnią roboczą, i to dokładnie wtedy, gdy użytkownik najbardziej potrzebuje widzieć dane | **Chipy zostają zawsze** (to one realizują ratyfikowaną przewagę „wszystkie braki naraz" — kolorem, w jednej linii), a **lista treści dostaje limit** (np. 2–3 pozycje + „…i 2 dalsze") rozwijany kliknięciem. ⚠ To dotyka obietnicy z §3.2, więc **wymaga jawnej zgody**, nie jest kosmetyką |
| **U7** | **Kolumny `Parsowanie` \| `Kultura danych` są niezbalansowane i marnują szerokość.** Wszystkie pola mają stałe szerokości 70–160 px, a kolumna zajmuje 50% powierzchni — przy 1920 px to ~900 px na kontrolkę szeroką na 110 px. Pion rośnie, poziom stoi pusty | Zagęścić **poziomo, nie pionowo**: pary pól, które zawsze czyta się razem (`Separator dziesiętny` + `Separator tysięcy`, `Format daty` + `Sep. daty` + `Sep. czasu`), postawić w jednym wierszu. Kultura schodzi z 5 wierszy do 2–3, Parsowanie analogicznie. **Zero zmian w VM** — to sam XAML |
| **U8** | **Wiersz poszarpany nie jest oznaczony, choć jest wyliczony.** `ImportSourceRecordRowViewModel.IsRagged` istnieje, jest pinowane testem (i to jednym z ⭐ — „większość, nie najszerszy"), ale **nic go nie maluje**: kolumny podglądu buduje code-behind, marker w gutterze z §3.6 nie powstał | Domknąć sygnał albo świadomie go odłożyć **z datą**. Dziś to dokładnie kształt z gotchy #233 — rzecz przetestowana i niewywoływana wygląda potem jak regresja, a zielony zestaw testów to ukrywa. §3.6 stawia ten marker jako powód, dla którego źle dobrany separator „widać bez czytania" |
| **U9** | **Pas H (status powierzchni) i pas A niosą razem mniej, niż powinny.** Pas A pokazuje tytuł, którego użytkownik nie potrzebuje; pas H to jedna linia tekstu, a §3.1 chciał w pasie A **połączenia i linii transakcyjnej** — faktu, który w module piszącym do bazy jest istotny | Przy usuwaniu pasa A (U3) **przenieść połączenie + linię do pasa H**, obok liczb. Pas H i tak jest miejscem, gdzie w I7 stanie tryb transakcji — jedna linia u dołu odpowiada wtedy na „gdzie to wchodzi i w jakiej transakcji" |
| **U10** | **Skróty z §9.2 nie istnieją**, w tym te, które **mogłyby** działać już dziś: `Ctrl+O` (wybór pliku) i podwójne kliknięcie paska zakładek. Reszta (`F5`, `Ctrl+F5`, `Esc`, `F6`, `Ctrl+1..4`) słusznie czeka na polecenia, których jeszcze nie ma | Dołożyć **tylko te, które mają czym sterować** (`Ctrl+O`, podwójne kliknięcie), resztę zostawić do I7. Skrót do polecenia, którego nie ma, byłby martwym wpięciem — ta sama zasada, dla której I5 nie zbudował pasa B |

#### Kolejność prac — ⭐ ROZSTRZYGNIĘTA PRZEZ UŻYTKOWNIKA 2026-07-26

> **Decyzja: najpierw kończymy moduł Data Import, dopiero potem osobny sprint UX całego EmberTerna.**
> Zasada „jedno zadanie na raz" obowiązująca w projekcie od początku — przy okazji jednego modułu nie
> zaczynamy przebudowy całej aplikacji, bo zakres przestaje być czytelny i nie da się ocenić postępu.
> **To odwraca kolejność, którą wcześniej rekomendowałem („najpierw gęstość globalna"); rekomendacja jest
> nieaktualna i nie należy do niej wracać.**

1. **U4 nie jest zadaniem tego modułu i NIE jest teraz realizowane.** Ląduje w **backlogu projektu** jako
   przyszły **sprint UX całego EmberTerna**, świadomie zaplanowany **po zamknięciu modułu**: sens tego
   sprintu polega na obejrzeniu wszystkich powierzchni **naraz** (SQL Editor, Debugger, Activity Monitor,
   Session Manager, Script Executor, Data Import i pozostałe) i zaprojektowaniu globalnego stylu kontrolek
   na tej podstawie — a nie na wywnioskowaniu go z jednego formularza. **Żadnych zmian w
   `Themes/ControlStyles.axaml` w ramach etapów importu.**
2. **W module poprawiamy wyłącznie to, co NIE wynika z globalnego stylu** — układ, proporcje, ergonomię
   i braki funkcjonalne powierzchni: **U1 (część układowa), U2, U3, U5, U7, U9, U10** (U6 i U8 — patrz
   niżej, czekają na osobną decyzję). Rozróżnienie jest ostre i warto je trzymać: *wysokość pojedynczego
   `ComboBoxa`* to sprint UX, *to, że dwa `ComboBoxy` stoją jeden pod drugim zamiast obok siebie* to ten
   moduł.
3. **U2, U3, U9, U10 tworzą jeden „szew domykający I5"** — są od siebie niezależne i żaden nie czeka na
   nic z zewnątrz.
4. **U5 rozstrzyga się przed I6**, bo dotyczy tego, gdzie w ogóle staną panele Cel i Mapowanie.
5. ⚠ **Konsekwencja przyjęta świadomie:** dostrajając układ modułu przed sprintem UX, dostrajamy go
   względem obecnych, za wysokich kontrolek — po sprincie sekcja Źródło i format odzyska jeszcze ~100 px
   i proporcje warto będzie obejrzeć ponownie. **Audyt UI w I12 i tak obejmuje obie palety oraz
   1366×768**, więc jest naturalnym miejscem tej powtórki.

#### Co dostarczył szew domykający I5 (2026-07-26)

| # | Rozstrzygnięcie | Jak zrealizowane |
|---|---|---|
| **U1 / U7** | zagęszczać **poziomo**, nigdy przez wysokość kontrolek | pary pól czytane razem dzielą wiersz (Kultura: 5 wierszy → 3; Parsowanie: cudzysłów+koniec linii, nagłówek+zakres wierszy, przycinanie+NULL); **zero zmian w `ControlStyles.axaml`** |
| **U2** | splitter + pełne zwijanie + **trwała** wysokość | `GridSplitter` + `ApplyBottomPanel` jako JEDYNY punkt renormalizacji **obu** wierszy (#240); podwójne kliknięcie paska; wysokość i stan zwinięcia w `WorkspaceState.ImportPreviewPanelHeight/Collapsed` — globalnie, jak `ResultsPanelHeight`, **nigdy w `ImportConfiguration`** (§4.8.2) |
| **U3 / U9** | pas A znika, jego fakt idzie do pasa H | pas H: `DestinationStatus` (połączenie + **linia Data**, czytane delegatem) │ liczby |
| **U6** | pasek gotowości dostaje sufit | `VisibleItems` = `Items` przycięte do `CollapsedItemLimit = 3` + „…i kolejne N" z rozwijaniem; **chipy zawsze wszystkie**; kolejność Core'a nietknięta (żadnego drugiego rankingu) |
| **U8** | marker wiersza poszarpanego domknięty **teraz** | kolumna numeru wiersza w podglądzie źródła niesie `⚠` + tooltip dla `IsRagged`; sygnał przestał być martwy (#233) |
| **U10** | tylko skróty, które mają czym sterować | `Ctrl+O` (wybór pliku) + podwójne kliknięcie paska zakładek; `F5`/`Ctrl+F5`/`Esc` czekają na polecenia z I7 |
| **U11** | ⭐ auto-zwijanie „Opcji formatu" po ustaleniu się źródła | zwija się **dopiero po udanym odczycie** (są pola) i **nigdy**, gdy użytkownik rozwinął je ręcznie (`_formatOptionsHeldOpen`) |
| **U12** | „Parsowanie" i „Kultura danych" mają wyglądać jak **grupy**, nie jak etykiety zawieszone między kontrolkami | nowy **współdzielony** idiom w `ControlStyles.axaml`: `Border.settings-group` (wpuszczona karta `BackgroundBrush` + ramka + `CornerRadius`) + `TextBlock.group-header` (mocniejszy od `field-label`, bo nagłówek nazywa **temat**, a `field-label` jedną **wartość**). Zero nowych kolorów, zero zmian metryk kontrolek. Przy okazji usunięty **martwy** `Border.panel` (0 konsumentów w całej aplikacji) — bliźniaczy styl obok nowego to gotowy sposób, żeby ktoś sięgnął po zły |

**Skutek liczbowy** (szacunek z układu, nie pomiar na uruchomionej aplikacji; 1920×1080, ~880 px na
zakładkę): w stanie ustalonym pas E to ~80 px zamiast ~470, a wiersz `*` — czyli przyszłe Mapowanie i
Podgląd — dostaje **~500–540 px**, gdzie dotąd nie miał ani jednego (podgląd źródła był przybity do 190 px).

⚠ **Jedna rzecz w U2 wymaga rozstrzygnięcia projektowego, nie tylko zgody.** „Zapamiętywanie ostatniej
wysokości" ma w aplikacji **dwa** precedensy, nie jeden: Debugger pamięta wysokość **w obrębie sesji**
(pole `_bottomHeight` w widoku), a SQL Editor **trwale** (`WorkspaceState.ResultsPanelHeight`). Zakładka
importu jest **nietrwała** (świadomie pominięta w `SnapshotCurrentTabs`), więc trwała wysokość nie ma gdzie
zamieszkać per zakładka — musiałaby być ustawieniem **globalnym** obok `ResultsPanelHeight`.
**I nie wolno jej włożyć do `ImportConfiguration`**: to preferencja układu, a nie decyzja użytkownika o
imporcie — §4.8.2 wyznacza tę granicę, a strażnik refleksyjny z §4.8.6 i tak zażądałby wtedy, żeby
wysokość panelu jeździła w profilu importu.

---


---

### Plan etapów implementacji — pełny zapis (było §6)

## 6. Podział na etapy implementacji

Zgodnie z kontraktem sesyjnym EmberTerna: **jeden etap = jedna sesja**, każda kończy się `build 0/0`,
zielonymi testami, czystym smoke testem i commitem.

| # | Etap | Zakres | Definition of Done |
|---|---|---|---|
| **I0** | **Sondy pomiarowe + dokument rekomendacji** *(blokujący, bez kodu produkcyjnego)* | **(a) Sondy.** FB5: przygotowany `INSERT` w pętli vs `FbBatchCommand` (jeśli sterownik go ma) — **zmierzyć, nie wywnioskować**; zachowanie przy błędzie wiersza w środku paczki (czy wiadomo, KTÓRY wiersz padł); koszt commitu co N + próg opłacalności; koszt `CommandLock` per paczka. `.xlsx`: daty jako liczby seryjne (`numFmtId`), shared strings vs inline, puste komórki, wykrywanie ostatniego wiersza, **czy `DocumentFormat.OpenXml` czyta strumieniowo bez materializacji arkusza**. Charset: wstawienie znaku spoza WIN1250 — jaki błąd i **na którym etapie** (klient czy serwer), bo od tego zależy, czy walidacja R1 jest wykonalna lokalnie. **(b) Dokument rekomendacji** `docs/design/data-import-i0-findings.md`: wyniki + jawne stwierdzenie, czy pomiary wymagają zmiany projektu. | Sondy jako **jednorazowe** projekty w `tools/probes/` (poza solution, jak `Fb3ClosureProbe`), uruchomione na bazie laboratoryjnej. Wyniki w §11 (log pomiarów) **oraz** w dokumencie rekomendacji. **Zero kodu produkcyjnego, zero zmian w `src/`.** Werdykt „architektura bez zmian" ⇒ zamrożenie (patrz blok na początku dokumentu); werdykt „wymaga zmiany" ⇒ akceptacja użytkownika przed I1. |
| **I1** ✅ **DOSTARCZONY** | Core: modele + **konfiguracja + magazyn** + czytnik tekstu | `ImportModels`, `ImportOptions`, `ImportEnums`, `ImportContracts`, **`ImportConfiguration`, `ImportProfile`, `ImportProfileStore`**, `DelimitedTextReader` (RFC4180: cudzysłowy, escapowane cudzysłowy, pola wielolinijkowe, CRLF/LF/CR), `DelimiterDetector`, `EncodingDetector`, `FileImportSource`, `TextImportSource`. **`ColumnMapping` z identyfikacją po nazwie (§4.8.5).** | Testy czytnika (≥25 przypadków brzegowych) + **`ImportConfigurationRoundTripTests` wraz z testem refleksyjnym** + `ImportProfileStoreTests`. Zero UI. |
| **I2** ✅ **DOSTARCZONY** | Core: konwersja + mapowanie + walidacja + gotowość | `ImportValueConverter` (ścisły, §0), `ImportMappingPlanner` (auto + reguła pary + diagnostyka `IMP*`), `ImportRowValidator` (+ `ImportCharsetGuard`), **`ImportReadiness`**, oraz dwa fundamenty wynikające z zasady jednego właściciela: `ImportTargetType` (§4.6) i `ImportDiagnostics` (katalog `IMP0001–IMP0027`). | Testy: każdy typ Firebirda × wartość poprawna/niejednoznaczna/błędna; pełna macierz gotowości (blokujące vs ostrzegawcze). Zero cichych konwersji. **Spełnione** — +131 testów, w tym pin odtwarzający samą cichą korupcję charsetu. |
| **I3** ✅ **DOSTARCZONY** | Core: pipeline + dry-run | `ImportPipeline` (wejście: `ImportConfiguration`), `DryRunImportWriter`, `ImportOutcome`, postęp, anulowanie, obie polityki błędów — oraz zaległy z I1 `DelimitedTextImportProvider` (bez niego nie ma end-to-endu na `TextImportSource`). | Testy end-to-end na `TextImportSource` + dry-run. **Pełna funkcjonalność bez bazy i bez UI.** **Spełnione** — +39 testów, w tym pin, że raport nazywa wiersz źródłowy, a nie indeks paczki. |
| **I4** ✅ **DOSTARCZONY** | Firebird: writer + odczyt celu + **weryfikacja na żywo** | `FirebirdImportWriter` — **`FbBatchCommand`, paczki po 500, `MultiError` ustawiany z `ImportErrorPolicy` (I0 §2.3)**, `OVERRIDING SYSTEM VALUE`, `CommandLock` per paczka; `FirebirdImportTargetReader` (kolumny + triggery BEFORE INSERT); mapowanie `FbException` → `ImportErrorKind` **na PARZE kodów GDS, nie na `ErrorCode`** (I0/REK-3: `string truncation` / `numeric overflow` / `transliteration` mają identyczny `ErrorCode` 335544321 i SQLSTATE 22000 — rozróżnia je dopiero **drugi** element wektora: 335544914 / 335544916 / 335544565; wektor obcięcia niesie limit i rzeczywistą długość jako liczby → wprost do raportu; **PK i UNIQUE są nierozróżnialne** (oba 335544665) ⇒ raportujemy „naruszenie unikalności", bez udawania precyzji). **Zero parsowania tekstu komunikatu.** | Import 10 k wierszy do tabeli laboratoryjnej — **liczby zgadzają się z `SELECT COUNT(*)`**; `NOT NULL`, PK/UNIQUE, CHECK, FK, za długi tekst, przekroczenie zakresu, transliteracja, znak spoza charsetu połączenia — **każdy daje właściwy `ImportErrorKind` i właściwy numer wiersza źródłowego**. Przypadki bierzemy z `tools/probes/DataImportWriteProbe` (fazy B/E/C), po czym sonda idzie do usunięcia. |
| **I5** ✅ **ZAMKNIĘTY** (etap + szew domykający, §3.8) | App: zakładka + rama powierzchni + sekcja Źródło i format | `WorkspaceTabKind.DataImport`, `Icon.Import`, przycisk toolbara (D6), near-singleton, dopisanie do listy pomijanej w `SnapshotCurrentTabs`, rama (pasy A–H), zwijalne sekcje, **pasek gotowości**, sekcja Źródło i format z dolną zakładką „Podgląd źródła". | Powierzchnia otwiera plik, pokazuje surowe rekordy i gotowość. Obie palety motywu. |
| **I6** ✅ **ZAMKNIĘTY** | App: sekcja Cel (istniejąca tabela) + panel Mapowanie | Wybór tabeli (`SearchableComboBox`, lista z linii **Metadata**), linia faktów (kolumny · klucz główny · **nazwane** triggery BEFORE INSERT), „opróżnij tabelę przed importem"; siatka mapowania **cel → źródło** z auto-dopasowaniem z `ImportMappingPlanner`, diagnostyką per kolumna, blokadami kolumn systemowych **z powodem**, pochodzeniem w słowniku `ValueOrigin`, listą pól nieużywanych, „Dopasuj po pozycji" / „Wyczyść" / „Tylko niezmapowane"; łańcuch §4.7 rozszerzony: **źródło → cel → mapowanie → gotowość**, anulowalny. | Mapowanie ręczne i automatyczne działa; niezgodności widoczne przed importem. |
| **I7** ✅ **DOSTARCZONY** | App: Podgląd + uruchomienie + raport — **pierwszy pełny przebieg** | Podgląd po konwersji (ciągły), `Waliduj`, tryby transakcji, `Importuj`/`F5`, postęp, anulowanie, raport, Commit/Rollback, eksport raportu, **zapis i przywracanie „ostatnio użytej" konfiguracji**. | **Import CSV → istniejąca tabela działa end-to-end na żywej bazie; druga sesja startuje z przywróconą konfiguracją.** Pierwszy etap z realną wartością dla użytkownika. **Spełnione** — +24 testy (5607 zielonych) oraz `tools/probes/DataImportRunProbe` **11/11 ALL PASS** na żywym FB5 (raport == `SELECT COUNT(*)`). |
| **I8** ✅ **DOSTARCZONY** | Nowa tabela | `ColumnTypeInferencer` — **(I0/REK-7) domyślnie skanuje CAŁE źródło**, nie próbkę (limit bezpieczeństwa 1 M wierszy), bo w realnym pliku 2 z 5 kolumn były typowo mieszane (R19); siatka typów w sekcji Cel z **zawsze widoczną liczbą przeanalizowanych wierszy** w kolumnie „Podstawa"; podgląd DDL; wykonanie na linii Ddl; ostrzeżenie o nieodwracalności; opcja `DROP` przy niepowodzeniu. | Import do nieistniejącej tabeli działa; typy zachowawcze i edytowalne; DDL z tego samego generatora; **kolumna mieszana ląduje jako `VARCHAR`, nie jako `INTEGER` z bombą zegarową**. **Spełnione** — +86 testów (5693 zielone) oraz sekcja **G** w `DataImportRunProbe`: **20/20 ALL PASS** na żywym FB5, w tym dowód, że katalog oddaje dokładnie te typy, o które poprosiliśmy, i że Rollback cofa wiersze, a nie tabelę. |
| **I9** | XLSX + zmiana nazwy projektu (D1) | `EmberTern.Export.Office` → **`EmberTern.Office`**; `XlsxImportProvider`; rozgałęzienie sekcji Format po `Capabilities`. **(I0/REK-6) Siedem wiążących wytycznych providera:** (1) **wyłącznie `OpenXmlReader` (SAX)** — DOM bierze 77× więcej pamięci (R8); (2) wartości umieszczane **po `CellReference`** — brakująca komórka środkowa jest NIEOBECNA, nie pusta, więc czytnik pozycyjny przesunąłby resztę wiersza o kolumnę (§0.1); (3) numer wiersza źródłowego **z `Row.RowIndex`** — puste wiersze są nieobecne, własny licznik skłamałby w raporcie (§0.6); (4) data = liczba + `numFmtId` daty (R3); (5) `SharedStringTable` czytana raz — Excel zapisuje teksty jako shared strings (rozmiar ∝ liczbie RÓŻNYCH tekstów); (6) `SheetDimension` **tylko jako wskazówka** postępu (bywa nieobecny); (7) formuła → wartość zbuforowana, a **komórka błędu → błąd wiersza** (+ opcja `ExcelErrorCellsAsNull`, R20). | Import plików z załączonych zrzutów daje identyczne dane. Eksport XLSX bez regresji. Pierwszy realny plik **z datami** obejrzany (luka pomiarowa R3). Po zamknięciu I9 `tools/probes/DataImportXlsxProbe` idzie do usunięcia. |
| **I10** ✅ **DOSTARCZONY** | Schowek + XLS | Schowek (App czyta, Core parsuje — zero nowego parsera; **okazał się już zbudowany w I5**, etap dołożył dowody). `XlsImportProvider` (BIFF8) + zależność **`ExcelDataReader` 3.7.0, MIT** w `EmberTern.Office` (D2/R5) + `XlsCellReader` + wspólny `ExcelSerialDate`. Usunięta odmowa dla `.xls` i martwy już `ImportFormatNotYetSupportedFormat`. | Wklejenie z Excela importuje się bez zapisywania pliku. **Spełnione** — +22 testy (5785 zielonych) oraz sekcja **I** w `DataImportRunProbe`: **33/33 ALL PASS** na żywym FB5, w tym prawdziwy skoroszyt napisany przez Excela. R8 zmierzone przed implementacją: sterta **płaska 26,7 MB** przez 60 000 wierszy BIFF8. |
| **I11** ✅ **DOSTARCZONY** | Nazwane profile (UI) | Selektor profili w zarezerwowanym miejscu paska poleceń, `Zapisz jako…`, zmiana nazwy, usuwanie, opcjonalny eksport `.json`. **Zero zmian w modelach i w pipeline.** | Nazwany profil odtwarza cały import; niezgodności raportowane przez pasek gotowości (§4.8.5). **Spełnione — i to jest wynik etapu: rachunek §4.8 się zgodził.** Ani `ImportConfiguration`, ani `ImportProfile`, ani pipeline, konwerter, walidator, planer mapowania i writer nie zostały zmienione; `ImportProfileStore` urósł o operacje na nazwanych wpisach, które jego własny komentarz z I1 zapowiadał na ten etap. +34 testy (5835 zielonych), w tym trzy dowody §4.8.5: usunięty plik → IMP0011, brak tabeli → IMP0016, zmienione pola źródła → mapowanie **przeplanowane po nazwach**, nie odtworzone po pozycjach. **Eksport `.json` świadomie pominięty** (opcjonalny, poza DoD — powiększyłby powierzchnię, o której etap ma orzec, że jej nie ruszył). |
| **I12** | Domknięcie | Dokumentacja (`docs/history/`, `docs/gotchas.md`, CLAUDE.md w miejscu), audyt UI (obie palety, checklista), pomiar wydajności na 1 M wierszy. | Moduł zamknięty. |

**MVP = I0…I7** (CSV/TXT → istniejąca tabela, z walidacją, raportem i pamięcią ostatniej konfiguracji).
Wszystko dalej jest przyrostowe. **I11 jest dowodem projektu**: jeżeli nazwane profile wymagają zmiany
choćby jednego modelu albo przebudowy sekcji UI, znaczy że §4.8 zostało naruszone po drodze.

---

