# Aktualizacja Avalonia 12.0.3 → 12.1.1

**Status: 🔒 SPRINT ZAMKNIĘTY, ODEBRANY I SCALONY (2026-08-05).** QA wzrokowe użytkownika przeszło bez regresji
w aplikacji i w edytorze; scalony do `feat/product-polish` (`--no-ff`, `42a6b98`) i wypchnięty na oba remote'y.
`master` **nietknięty**. Gałąź techniczna `chore/avalonia-12.1.1` wycofana. Stan po scaleniu: **§12**.
⏸ Trzy rzeczy zostają otwarte, każda z własnym powodem i żadna nie blokuje M4: §9.1 (`DataImportProbe`),
§9.2 (zdublowane numery gotchy), §10.2 (`ProtectedData` 9.0.18). Od tego dokumentu **nie planuje się M4** —
robi to `product-polish-m4-next-session.md`.
Osobny, zamknięty sprint techniczny wykonywany **przed rozpoczęciem M4**,
świadomie **nie mieszany z pracami Product Polish** (decyzja użytkownika, 2026-08-05). Gałąź
`chore/avalonia-12.1.1`, odcięta od `feat/product-polish` i scalana **z powrotem do niej** — zgodnie ze
skorygowaną 2026-08-05 regułą higieny gałęzi: *gałąź odcięta od gałęzi funkcji wraca do TEJ gałęzi, nigdy
do `master`*.

Ten dokument jest **zapisem „przed"** — powstał przed pierwszą zmianą wersji, żeby po QA dało się
rozstrzygnąć, co pochodzi od frameworka, a co od nas. Sekcja §7 jest dziennikiem wykonania i rośnie w miarę
kroków.

---

## 0. Ratyfikowane decyzje (użytkownik, 2026-08-05) — nie relitygować

| # | Decyzja | Uzasadnienie |
|---|---|---|
| D-1 | **Avalonia 12.1.1**, nie 12.1.0 | 12.1.0 ma realną regresję kompilowanych bindingów (§3 R7), naprawioną dopiero w 12.1.1 |
| D-2 | **`Avalonia.Controls.DataGrid` → 12.1.2** | pakiet ma własne repo i własny cykl; trzymanie go na starszym numerze „dla symetrii" kupuje wygląd spójności, a nie spójność — DataGrida 12.1.1 **nie ma** |
| D-3 | **`Avalonia.AvaloniaEdit` zostaje na 12.0.0** do czasu oficjalnej wersji 12.1.x | nie istnieje build pod 12.1; patrz §3 R1 — to największe ryzyko sprintu |
| D-4 | Aktualizacja **przed M4**, jako osobny sprint z własnym QA i własnymi commitami | M4 przepisze wysokości/ikony/odstępy w całej aplikacji; zmiany renderowania trzeba zobaczyć **przed**, nie w trakcie (D-M4-1 + §13.0.1 Z-6) |
| D-5 | Kryterium wycofania: cokolwiek z R1 (edytor) → **cofnij do 12.0.5**, nie do 12.0.3 | 12.0.5 daje 2 z 4 wydań przy zerowej zmianie domyślnych renderowania, zerowym ruchu w compiled bindings i zerowej zmianie hit-testingu |

⛔ **Świadomie NIE w tym sprincie:** `TableView` (mimo deprecation DataGrida), Wayland, `Avalonia.WinUI`,
`Win32Properties.WindowCornerPreference`, migracja tooltipów na `ToolTip.ShouldUseOverlayLayer`. Ostatnie to
**zmiana zachowania**, nie aktualizacja — decyzja o niej należy do kontekstu konkretnej zmiany (D-M4-2/3),
nie do tego kroku.

---

## 1. Co leży pomiędzy

| Wersja | Data | Rodzaj |
|---|---|---|
| 12.0.3 | 2026-05-10 | **nasza wersja wyjściowa** |
| 12.0.4 | 2026-05-28 | patch |
| 12.0.5 | 2026-06-23 | patch |
| 12.1.0 | 2026-07-09 | **minor** |
| 12.1.1 | 2026-07-29 | patch — **cel** |

⚠ **Ani release notes 12.1.0/12.1.1, ani blog nie mają sekcji breaking changes.** Dokument *Breaking changes
in Avalonia 12* dotyczy progu 11 → 12, który przeszliśmy dawno (`WindowDecorations`, usunięte
`TitleBar`/`CaptionButtons`, `UseHarfBuzz`). Zweryfikowane w kodzie: `Program.cs` używa samego
`UsePlatformDetect()` bez jawnej konfiguracji silnika renderowania, więc punkt o `UseHarfBuzz` nas nie
dotyczy.

⭐ **To dobra wiadomość, ale nie gwarancja.** W wydaniu minor niezgodności są **behawioralne, nie
kompilacyjne** — a takich build nie łapie. Cała §3 jest o tym.

---

## 2. Co zyskujemy

### 2.1 Trzy naprawy zawieszeń w headless — i jedna ma kształt naszego objawu

| Wydanie | Zmiana |
|---|---|
| 12.0.4 | `Fix xUnit headless tests losing TestContext on background thread` (#21357) |
| 12.0.5 | `Headless – Add sleep timer option` |
| 12.1.0 | `Fix hang if exception is thrown during headless session app construction` (#21688 / issue #21687) |
| **12.1.1** | **`Fix headless session hang when cleanup throws` (#21781 / issue #21770)** |

Opis #21781, cytat: *„An exception during application cleanup escapes the work item before its completion
source is settled, faults the private consumer task, and leaves current and future Dispatch calls waiting
forever."*

To **ten sam kształt**, co nasze wieloletnie zawieszenie (#94 / #226 / #261): run **wisi zamiast failować**,
po zakończeniu pracy, a raportowana nazwa testu jest **pozycyjna** (ostatni headless w przebiegu). Podejrzany
zapisany w CLAUDE.md — *session teardown / dispatcher-loop shutdown* — i podejrzany z tego PR-a to to samo
miejsce.

⚠⚠ **Nie jest to twierdzenie o przyczynie — nie ma dowodu.** I jest tu pułapka metodologiczna, którą trzeba
zapisać, **zanim** przyjdzie zielony przebieg: od 2026-08-04 mamy **dwie** niezależne kandydatki na tę samą
przyczynę — `AutoScrollToSelectedItem` (`metadata-refresh-analysis.md` §11/§12) i teraz #21781. ⭐ **Zielony
przebieg po aktualizacji przestaje więc rozstrzygać którąkolwiek z nich.** Wynik i tak notujemy w obie strony,
ale bez przypisywania go do jednej przyczyny.

### 2.2 Naprawy trafiające w komponenty, które realnie mamy

- `Fix ListBox crash when scrolling after item removal` (#21838) → sidebar jest wirtualizowanym `ListBox`,
  w którym `SidebarFlatController` robi splice'y pojedynczymi `Insert`/`Remove`. Nasz scenariusz.
- `Allow Grid shared size groups to shrink` (#21837) + `Register Grid assigned definition collections with
  their shared size group` (#21848) → `SearchableComboBox` / `SearchableComboBoxSection` stoją na
  `Grid.SharedSizeGroup`.
- `Position popup using the screen containing the anchor point` (#21750) → wszystkie karty/popupy,
  multi-monitor.
- `Clear TextBox undo history on external text updates` (#21839) + `Initialize TextBox command states in
  constructor` (#21859) → pola dostające tekst z VM (m.in. numeryczne pola Settings Center, gdzie `Commit()`
  echuje sklamrowaną wartość z powrotem).
- `Fix infinite loop in tab stop search with TabNavigation=Once` (#21864).
- `Handle Padding correctly in ScrollContentPresenter` (#21872) → okolica, w której M3.3b spalił rundę
  (Padding jako rezerwacja miejsca na scrollbar = sprzężenie zwrotne). Dla nas raczej „mniej pułapek" niż
  zysk, bo skończyliśmy na siostrzanym pasku.
- `Fix StackOverflow when a NaN offset is set on ScrollViewer` (12.0.5).
- `Fix RenderTargetBitmap losing effects` (12.0.4, #20790) → sondy wizualne.

### 2.3 Wydajność i tekst

`TextRunCache` (12.0.4) · Unicode word segmentation + Unicode v17 (12.0.5) · rework text fallback itemization
(12.1.0) · `Fix line break enumerator infinite loop` (12.1.1) · composition hit testing z per-visual AABB ·
render data jako binarny strumień opcodów · szybsze tworzenie bitmap z pikseli · **rendering w maksymalnym
odświeżaniu monitora na Windows** (koniec sufitu 60 FPS).

Istotne, bo mamy edytor, pięć gridów danych i drzewo kilkunastu tysięcy wierszy.

### 2.4 Higiena dostaw

`Generate a CycloneDX SBOM per published NuGet package` — pasuje wprost do naszej dyscypliny (`NU1902`/`NU1903`
jako brama build'a, `THIRD-PARTY-NOTICES.txt` z testem).

### 2.5 Nowe, świadomie niewykorzystane

`ToolTip.ShouldUseOverlayLayer` (#21830) domyka lukę, którą obeszliśmy ręcznie (gotcha #209 — bare `Popup`
renderuje się niewidocznie, `ClampIntoOverlay`, cztery własne karty w `OverlayLayer`). ⛔ Nie migrujemy na to
w tym sprincie (§0). ⚠ **PR nie podaje wartości domyślnej** — więc trzeba **zmierzyć**, czy tooltipy nie
przeszły do overlay layera domyślnie (§6, punkt 9).

---

## 3. Ryzyka

### R1 — największe: **AvaloniaEdit nie ma buildu pod 12.1**

`Avalonia.AvaloniaEdit` najnowsza wersja to **12.0.0** (NuGet, 2026-04-08), zależność `Avalonia (>= 12.0.0)`.
12.1 nie istnieje.

⚠⚠ **I to jest ryzyko, a nie jego brak:** `>= 12.0.0` spełnia 12.1.1, więc **NuGet nie zaprotestuje, build
będzie 0/0, a testy zielone** — bo nasze testy headless asertują właściwości i pędzle, **nie piksele**.
Automatycznego sygnału nie ma żadnego.

A 12.1.0/12.1.1 ruszają dokładnie ten stos, na którym AvaloniaEdit stoi: `Rework text fallback itemization`,
`TextRunCache`, **`Preserve lines at rounded fractional heights` (#21834)**, `line break enumerator`,
`ScrollContentPresenter` Padding. Na `TextView` i `GetRectsForSegment` siedzi u nas:

- sześć `IBackgroundRenderer`ów (current line, squiggles, related elements, inline values, semantic
  highlighting, search match),
- `BreakpointMargin` z własnym hit-testem,
- `InlineValuesRenderer` — rysuje **w pustym miejscu za końcem linii**, czyli czyta geometrię linii,
- cztery karty w `OverlayLayer`,
- `SqlIndentationStrategy`,
- jedenaście read-only preview DDL.

**To jest QA wzrokowe, nie test — i to jest kryterium wycofania (D-5).**

### R2 — `Fix RequestedThemeVariant default value` (12.1.0, #21624)

Mamy **świadomie** zostawione `RequestedThemeVariant="Dark"` w `App.axaml` jako wartość bootstrapową, z
komentarzem mówiącym, że usunięcie jej daje `ThemeVariant.Default`, czyli motyw **OS** (settings-center §13c).
Naprawa *wartości domyślnej tej właśnie właściwości* ląduje dokładnie tam.

### R3 — zmienione **domyślne** ustawienia renderowania (12.1.0)

`Disable region dirty rect clipping by default` · `Enable stencil buffers by default` · `Composition-aware
geometries and drawing change detection` · per-visual AABB hit testing · `Preserve lines at rounded fractional
heights`.

Nasza warstwa optyczna jest w klasie „1 px decyduje": `UseLayoutRounding="False"` na kropce DEV MODE i na
`PART_MarkArea`, korekty przez `RenderTransform` (pudełko tekstu ≠ jego farba), ikony w siatce 24 jednostek ze
`StrokeThickness=2`, kropka breakpointa 4.5 w `DebuggerIcon`, kreski odstępowane wielokrotnością 1,5 (#287),
badge Ø10 inset 0,5 w `CreateIcon`. ⚠ Wyraźniejsze przy 125 %/150 % — tam `UseLayoutRounding` już raz nas
ugryzł.

### R4 — DataGrid jest **oficjalnie deprecated**

Cytat z opisu pakietu: *„DataGrid is deprecated and only receives bug fixes"*, z rekomendacją `TableView`
(read-only) lub `TreeDataGrid` (edycja); kontrolka została **wyprowadzona z głównego repo Avalonii do
własnego**.

U nas: **53 pliki, 559 wystąpień** w AXAML — pięć gridów danych + gridy definicji + `EditableGridBehavior` na
dziewięciu jawnych `Attach`.

Dla tej aktualizacji to nie blokada, ale dwa fakty trzeba znać: (a) **linia wersji się rozjeżdża** — DataGrid
12.1.2 przy corze 12.1.1, bo DataGrida 12.1.1 nie ma (D-2); (b) strategicznie nasz najbardziej produktowy
komponent stoi na kontrolce w trybie utrzymaniowym. ⛔ **Osobna decyzja, nie temat M4.**

### R5 — hit-testing, `Popup`, `OverlayLayer`

12.1.1: `Fix custom-hit-test optimization` (#21769), `Fix stale _usedCache value ... in compositor` (#21823).
12.1.0: hit testing na per-visual AABB. Mamy własny hit-test w gutterze breakpointów (S-1 naprawiał dokładnie
„margines nie był hit-testowalny") i karty w `OverlayLayer`, które **nie mają** być hit-testowane ani brać
focusu. Plus nieznany default `ShouldUseOverlayLayer`.

### R6 — `FocusManager.CanHaveFocusableChildren` zwraca teraz **false** domyślnie (12.1.0, #21640)

`CommandRouter` rozstrzyga scope **sondując focus**; `LanguageExpansionController` bramkuje się na
`TextArea.IsKeyboardFocusWithin` (#225); `EditableGridBehavior` bramkuje Enter **focusem** — bo, jak zmierzono
w S-1/S-3, `DataGrid` **nie ma publicznego „czy właśnie edytuję"**. Zmiana domyślnej odpowiedzi na pytanie
o focusowalne dzieci jest w tej samej okolicy.

### R7 — compiled bindings: regresja w 12.1.0, **naprawiona w 12.1.1**

12.1.0 wprowadziło (#21617) zmianę psującą binding komendy do **metody** z konkretnym typem parametru — issue
#21833, błąd *„Expected method with no parameters or a single object overload"*, zgłoszony jako regresja
bezpieczeństwa typów. 12.1.1 to cofa: `Allow single parameter with any type when binding to a method` (#21867).

Nas to najprawdopodobniej nie dotyczy (komendy to właściwości generowane przez `[RelayCommand]`, nie metody),
ale ⭐ warto to znać jako fakt o **tempie upstreamu**: 12.1.0 miało realną regresję w kompilowanych bindingach,
a my mamy ~30 `x:DataType` we współdzielonych `ContextMenu`, których cała wartość polega na tym, że kontrakt
jest sprawdzany **przy kompilacji** (M3.4b część 1 — z bindingami refleksyjnymi ten sam defekt byłby ciszą).

### R8 — `Reapply cached binding source values during UpdateTarget` (#21756)

Zmiana w rdzeniu bindingów. Nasza reguła **R16** („test na wartości ≠ test, że ekran działa" — pinujemy
`PropertyChanged`, nie wartość) pracuje dokładnie na tej granicy.

---

## 4. Wpływ na nasze własne komponenty

| Komponent | Powierzchnia API | Realne ryzyko | Czym weryfikujemy |
|---|---|---|---|
| **`CreateIcon`, `SvgIcon`, `DebuggerIcon`** | zero (geometrie + jeden `Path`, `StrokeThickness=2`) | **R3** — rendering/rounding; badge Ø10, glify 18/24 j., disabled chip | oko: 9 rodzajów × 2 motywy × {normal, hover, disabled} × {100 %, 125 %}; `CreateIconContractTests` (czyta źródło) nadal ważny |
| **`EditableGridBehavior`** | **wysokie** — stoi na trzech zmierzonych faktach o *cudzej* implementacji: `DataGrid` claimuje Enter → handler na TUNNELU; brak publicznego „am I editing" → bramka na FOCUSIE; `DataGridCell` bez publicznego `Column` → lokalizacja przez `SelectedItem` + `DisplayIndex` | **R4 + R6** | `EditableGridEnterTests` + 9 miejsc ręcznie |
| **`VisualCandidateProbe`, `TabStripVisualProbe`, `ImportFileOpenProbe`** | własne `Avalonia.Desktop` + `Fonts.Inter` | ⚠⚠ **muszą iść razem z corem** — inaczej po raz **szósty** sonda pokaże stan, którego nie ma. Renderują do bitmapy → **R3** + naprawa `RenderTargetBitmap` mogą zmienić obraz | re-render i porównanie z zachowanym „przed" (materiał do oceny, **nie test**) |
| **`FluentBridge.axaml`** | **wysokie i najmniej osłonięte** — Bridge repinuje **nazwane zasoby Fluenta** i zależy od tego, że *ich* szablon maluje przez te klucze. Jeśli 12.1 zmienił jakikolwiek szablon Fluenta, mapowanie może przestać docierać — a `FluentBridge_ContainsNoLocalValues` tego **nie złapie**, bo sprawdza nasz plik, nie ich szablon | zmiana zachowania bez sygnału z build'a | ta część `DesignTokenApplicationTests`, która czyta **pomalowany element**, nie token (#315) + oko |
| **`ControlThemes.axaml`** (CheckBox, RadioButton) | niskie — własna struktura szablonu, odporna na zmiany w Fluencie | `UseLayoutRounding="False"` na `PART_MarkArea` → **R3** | oko, 4 stany, `ControlOutlineBrush` przy progu 3:1 |
| **`SearchableComboBox`** | `Grid.SharedSizeGroup` | **#21837 + #21848 zmieniają zachowanie shared size** | oko: wyrównanie kolumn w popupie, filtr |
| **Sidebar** (`ListBox`, `AutoScrollToSelectedItem="False"`, współdzielone `ContextMenu` w `Resources`) | średnie | **#21838** + R5 | `MetadataTreeVirtualizationProbe` (bounded time + scroll nie rusza się sam) + `EMBERTERN_TREE_DIAG=1` na żywym drzewie |
| **Tab strip** (siostrzany scrollbar, `AllowAutoHide=false`, grubość mierzona z paska) | średnie | **#21872** | `TabStripPresentationTests` + oko w obu trybach |
| **`MessageBanner`** (2 warianty w stylach) | niskie | — | oko na 2–3 z 23 powierzchni |
| **Karty `OverlayLayer`** (hover, ParameterHelper, code-action, construct hint) | średnie | **R1 + R5 + nieznany default `ShouldUseOverlayLayer`** | oko w edytorze; `ConnectionExpandBindingProbe` sprawdza hint |

---

## 5. Pakiety

| Plik | Pakiet | Z | Na |
|---|---|---|---|
| `src/EmberTern.App` | `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` | 12.0.3 | **12.1.1** |
| `src/EmberTern.App` | `Avalonia.Controls.DataGrid` | 12.0.0 | **12.1.2** (D-2) |
| `src/EmberTern.App` | `Avalonia.AvaloniaEdit` | 12.0.0 | **bez zmian** (D-3) |
| `src/EmberTern.App` | `AvaloniaUI.DiagnosticsSupport` | 2.2.1 | **bez zmian** — Debug-only, nie ships, `>= 11.2.0`; podniesienie do 2.2.3 nie należy do tego sprintu |
| `tests/EmberTern.Tests` | `Avalonia.Headless` | 12.0.3 | **12.1.1** |
| `tools/probes/VisualCandidateProbe` | `Avalonia.Desktop`, `Avalonia.Fonts.Inter` | 12.0.3 | **12.1.1** |
| `tools/probes/TabStripVisualProbe` | `Avalonia.Desktop`, `Avalonia.Fonts.Inter` | 12.0.3 | **12.1.1** |
| `tools/probes/ImportFileOpenProbe` | `Avalonia.Desktop` | 12.0.3 | **12.1.1** |

---

## 6. Plan QA

**Maszynowo (musi być zielone):**

1. build **0/0 w Debug i Release** (precedens: harness log pod `#if DEBUG` — Release potrafi mieć własne błędy)
2. suite **7360** w trzech partycjach, **3 przebiegi**, każdy z `--blame-hang --blame-hang-timeout 120s`
   (zawieszenie jest rzadkie — jeden zielony przebieg nic nie dowodzi)
3. `MetadataTreeVirtualizationProbe` + `SharedContextMenuFeasibilityProbe` — to *ich* temat (#21838, R5)
4. sondy żywe, jako wykluczenie regresji poza UI: `DebuggerFidelityProbe` **39/39** (⚠ w tym dyskryminująca
   sprawa 39, `SP_DBG_SELINTO`), `ChangeSafetyProbe` 19/19, `DataImportProbe` 20/20, `DataImportRunProbe` 33/33
5. smoke: aplikacja startuje

**Wzrokowo — obowiązkowo w obu motywach, na `SZKOLENIE_SQL`, zmaksymalizowane, przy 100 % i 125 %:**

1. **Edytor SQL — R1, priorytet bezwzględny**: current line, squiggles, related elements + parowanie nawiasów,
   inline values, semantic highlighting, gutter breakpointów **i klik w gutter**, cztery karty `OverlayLayer`
   (hover, ParameterHelper, code-action, construct hint), auto-indent i parowanie `begin…end`, panel
   Find/Replace, 11 read-only preview DDL
2. **Debugger**: launch → step → paused marker, Variables (changed-value wash), Error Bar, Call Stack, panel
   dolny, Immediate
3. **Gridy edytowalne**: Enter w 9 miejscach, wysokości wierszy, edytor w komórce (S-1a/S-3)
4. **Sidebar**: expand dużej kategorii — **scroll nie rusza się sam** (ratyfikowane kryterium akceptacji, nie
   „nice to have"), context menu na wirtualizowanym wierszu, filtr
5. **Motyw — R2**: start w Dark, start w Light, przełącznik w titlebarze, import motywu
6. **`SearchableComboBox`** — wyrównanie kolumn w popupie (#21837/#21848)
7. **Tab strip** — single/multi-row, pasek przewijania, overflow
8. **Ikony i kontrolki bazowe** — `CreateIcon` ×9, `DebuggerIcon`, **disabled `Button.icon`** (Z-1),
   `CheckBox`/`RadioButton` (`ControlOutlineBrush`, Z-2), chipy i rail w status barze
9. **Tooltipy** — czy `ShouldUseOverlayLayer` czegoś nie zmienił domyślnie. ⭐ **Zmierzyć, nie założyć** — PR
   nie podaje wartości domyślnej

---

## 7. Dziennik wykonania

### Krok 0 — baseline (2026-08-05) ✅

Zero zmian w kodzie. Gałąź `chore/avalonia-12.1.1` odcięta od `feat/product-polish` (`8d5c510`), drzewo czyste.

| Pomiar | Wynik |
|---|---|
| `dotnet build -c Debug` | **0 ostrzeżeń / 0 błędów** |
| `dotnet build -c Release` | **0 ostrzeżeń / 0 błędów** |
| Partycja główna × 3 | **7232** ✓ ✓ ✓ (10 s / 9 s / 9 s) |
| Partycja headless (7 klas) × 3 | **74** ✓ ✓ ✓ (20 s / 10 s / 8 s) |
| `ConnectionExpandBindingProbe` osobno × 3 | **54** ✓ ✓ ✓ (12 s / 10 s / 8 s) |
| **Razem** | **7360**, zgodnie z CLAUDE.md |
| Zawieszenie w 9 przebiegach | **nie wystąpiło** — `--blame-hang` za każdym razem: *„Wszystkie testy zakończyły działanie, plik sekwencji nie zostanie wygenerowany"* |

⚠ **To nie jest dowód, że zawieszenia nie ma** — jest rzadkie z definicji, a dziewięć zielonych przebiegów
przed zmianą znaczy tylko tyle, że **baseline nie zawiera znanego objawu**. Konsekwencja dla oceny po
aktualizacji: brak zawieszenia po zmianie **nie będzie** dowodem naprawy (§2.1).

**Rendery „przed"** — 18 plików PNG z `VisualCandidateProbe` (10) i `TabStripVisualProbe` (8: SingleRow,
MultiRow, 26px-FIXED w obu motywach), wygenerowane **na 12.0.3 z bieżącego HEAD**, nie odziedziczone po M3.5
(sondy renderowały wtedy także stany kandydatów, których w produkcie nie ma). Zapisane wraz z sumami SHA-256
poza repozytorium (`out/` jest gitignorowane):
`…/scratchpad/before-12.0.3/{visual,tabstrip}/` + `hashes-before.txt`.

### Krok 1 — core w App na 12.1.1 (`cc9c0ee`) ✅

`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter` → 12.1.1. Nic więcej.
Build **0/0 w Debug i Release**. Rozwiązane wersje sprawdzone jawnie (`dotnet list package`): core 12.1.1,
AvaloniaEdit 12.0.0, DataGrid 12.0.0, Headless nadal 12.0.3.

⚠ **Suite świadomie nie uruchamiana w tym kroku:** `Avalonia.Headless` 12.0.3 przy corze 12.1.1 to
konfiguracja, której nie wdrażamy, więc wnioski z niej byłyby o stanie, który nie istnieje.

### Krok 2 — `Avalonia.Headless` na 12.1.1 (`014d61c`) ✅

Build 0/0. Suite **7360** zielona w trzech partycjach (7232 + 74 + 54), każda z `--blame-hang`. Zero zmian
w kodzie produkcyjnym i w testach — te same 7360 asercji przechodzi na nowym runtimie.

⭐ Powód wiązania wersji zapisany **w csproj obok referencji**: Headless twardo zależy od
`Avalonia`/`Fonts.Inter`/`Avalonia.HarfBuzz` w swoim numerze, więc rozjazd albo podnosi core zależnością
przechodnią, albo daje `NU1605`. *Bump core = bump tutaj.*

### Krok 3 — `Avalonia.Controls.DataGrid` na 12.1.2 (`1a8b428`) ✅

Build 0/0. Suite **7360** zielona w trzech partycjach — w tym `EditableGridEnterTests`, czyli asercje stojące
na trzech zmierzonych faktach o implementacji `DataGrid`a (Enter na TUNNELU · bramka na FOCUSIE · komórka
lokalizowana przez `SelectedItem` + `DisplayIndex`). Smoke: aplikacja startuje, 8 s życia procesu, zero
nowych wpisów `FATAL`.

⭐ Rozjazd numerów **i** deprecation pakietu zapisane w csproj obok referencji — razem z powodem, żeby nikt
nie „naprawił" tego zejściem do 12.1.0, i z granicą: migracja na `TableView`/`TreeDataGrid` to osobna decyzja
produktowa, nie temat aktualizacji frameworka i nie temat M4.

### Krok 4 — trzy sondy wizualne na 12.1.1 (`fe54d6a`) ✅ **18/18 renderów bajtowo identycznych**

Wszystkie 18 renderów ma **identyczną sumę SHA-256** z zapisanymi na 12.0.3 — plik po pliku, oba motywy.
Zero ruchu w geometrii, układzie i rozwiązywaniu pędzli po stronie tego, co sondy renderują.

⚠ **Identyczność zweryfikowana ZANIM została uznana za wynik**, bo „identyczne rendery" i „sonda się nie
przebudowała" wyglądają tak samo: `dotnet list package` pokazuje 12.1.1, a `Avalonia.Base.dll` /
`Avalonia.Controls.dll` w `bin/Release` sondy raportują **12.1.1.0**.

⚠⚠ **ZAKRES TEGO WYNIKU, bo bez niego liczba wprowadza w błąd:** `RenderTargetBitmap` idzie ścieżką
natychmiastowej rasteryzacji Skia, **a nie przez kompozytor GPU** — czyli tam, gdzie *nie* żyją zmienione
w 12.1.0 domyślne (`dirty-rect clipping`, `stencil buffers`). Bajtowa identyczność dowodzi więc, że nie
ruszyła się geometria ani warstwa zasobów; **nie dowodzi, że żywe okno maluje się tak samo.** R3 pozostaje
otwarte i należy do QA wzrokowego.

`ImportFileOpenProbe`: build 0/0 (uruchomienie wymaga dużego `.xlsx`, którego nie mamy — stan bez zmian
względem M3b.1).

### Krok 5 — powód przy AvaloniaEdit + gotcha #321 (`a26a382`) ✅

Komentarz przy referencji `Avalonia.AvaloniaEdit` mówi, **czego nie ma**, co trzeba by ponownie sprawdzić
i gdzie jest checklista. ⭐ Bo pinowana starsza wersja **bez** powodu czyta się jako zaniedbanie i zostaje
„posprzątana" przy zielonym buildzie. Gotcha **#321** generalizuje to poza ten pakiet.

⚠ **Sprostowanie zmierzone, nie inkrementowane:** CLAUDE.md deklarował liczbę wpisów gotchy w **dwóch**
miejscach, które nie zgadzały się ze sobą („309, #1–#320" vs „301, #1–#312") i **oba były błędne**. Zmierzone:
**308 wpisów, najwyższy numer 321** — a liczba **nie** jest max−1, bo **numery 303 i 304 są użyte po dwa
razy**, w różnych sekcjach tematycznych. ⛔ Nie przenumerowano: na te numery są odwołania w CLAUDE.md, więc
wybór, który wpis zachowa numer, to decyzja do przedstawienia, nie zmiana do zrobienia po cichu. Kształt #284
o warstwę wyżej.

---

## 8. QA maszynowe — wynik

| Sprawdzenie | Wynik |
|---|---|
| `dotnet build -c Debug` | **0 / 0** |
| `dotnet build -c Release` | **0 / 0** |
| Suite, 3 partycje × **3 przebiegi** | **7360** ✓ dziewięć razy (7232 + 74 + 54), `--blame-hang` za każdym razem bez pliku sekwencji |
| Zawieszenie suite | **nie wystąpiło** w 9 przebiegach po zmianie (ani w 9 przed) |
| `MetadataTreeVirtualizationProbe` | 4/4 — 93 ms / 106 ms / 148 ms / 823 ms, przy limitach 5 s |
| `SharedContextMenuFeasibilityProbe` | 2/2 — 401 ms / 3 s |
| `DebuggerFidelityProbe` (żywy FB5) | **39/39 ALL PASS**, w tym dyskryminująca sprawa 39 (`SP_DBG_SELINTO`) |
| `ChangeSafetyProbe` (żywy FB5) | **ALL PASS**; koszt jednego sprawdzenia 3,0 ms / 1,6 ms |
| `DataImportRunProbe` (żywy FB5) | **ALL PASS — 33 sprawdzenia** |
| `DataImportProbe` | ⚠ **NIE KOMPILUJE SIĘ — i jest to defekt WCZEŚNIEJSZY** (§9) |
| Smoke Debug + Release | aplikacja startuje, ~9 s życia procesu, **0 wpisów `FATAL`** w całym `EmberTern-debug.log` |

⚠ **Czego to QA nie dowodzi, powiedziane wprost:** brak zawieszenia **nie jest** dowodem, że #21781 naprawiło
nasz wieloletni objaw — patrz §2.1 (dwie niezależne kandydatki, a objaw jest rzadki z definicji). I żaden
z powyższych pomiarów nie dotyka **R1**: 7360 asercji przeszłoby również wtedy, gdyby układ tekstu w edytorze
przesunął się o wiersz.

---

## 9. Znalezione po drodze, NIE naprawione — do decyzji

### 9.1 `DataImportProbe` nie kompiluje się (defekt wcześniejszy, nie regresja)

```
tools/probes/DataImportProbe/Program.cs(114,43): error CS1503:
  nie można przekonwertować z „EmberTern.Firebird.TransactionService”
  na „EmberTern.Firebird.ImportSessionConnection”
```

⭐ **Dowiedzione, nie założone:** ten sam błąd, ten sam plik i ta sama kolumna występują na commicie
**`8d5c510`** — czubku `feat/product-polish` sprzed sprintu (zbudowane w osobnym `git worktree`). Sonda
referencuje wyłącznie `EmberTern.Core` i `EmberTern.Firebird` — **ani jednego pakietu Avalonii** — więc
aktualizacja nie mogła jej dotknąć.

**Przyczyna:** etap I7.5 dał modułowi importu własną transakcję na własnym attachmencie
(`ImportSessionConnection`), a `FirebirdImportWriter` przyjmuje odtąd ten typ zamiast `TransactionService`.
Sonda jest **poza solucją**, więc `dotnet build EmberTern.slnx` nigdy jej nie kompiluje — i rozjazd przeszedł
niezauważony przy zielonym buildzie i zielonej suite.

⚠ **To ten sam kształt, co gotcha #321 i #284: narzędzie weryfikacyjne zgniło cicho, bo nic go nie
kompilowało.** Różnica jest istotna: `DataImportProbe` to **20 sprawdzeń modułu zamkniętego jako
„user-accepted"**, czyli dokładnie ten materiał, po który sięgnie się, gdy import kiedyś się zepsuje.

⛔ **Nie naprawiono w tym sprincie z dwóch powodów:** (a) to nie jest defekt aktualizacji, a sprint ma być
zamknięty i przypisywalny; (b) Data Import ma stojącą dyrektywę *„wracać tylko po rzeczywisty defekt
funkcjonalny"*, więc dotknięcie go „przy okazji" byłoby dokładnie tym, czego zakazuje.

**Rekomendacja:** własne, małe zadanie — naprawić wywołanie *oraz* dodać do `tools/probes/README.md`
rozstrzygnięcie, jak sondy poza solucją mają być utrzymywane (skrypt budujący wszystkie, albo świadoma zgoda
na gnicie z zapisanym powodem). ⭐ Warto zapytać przy tym, **ile innych sond jest w tym stanie** — sprint
zbudował tylko cztery.

### 9.3 ⭐⭐ „Ponad 60 nieprzechodzących testów" — PRZYCZYNA USTALONA, NIE JEST TO REGRESJA STANU ODDANEGO

**Zgłoszenie:** po aktualizacji ponad 60 testów jednostkowych nie przechodzi.

**Pomiar w stanie oddanym: nie odtwarza się — trzynaście przebiegów zielonych.** Debug i Release,
partycjonowane i pełne w jednym przebiegu, w tym **po pełnym `clean` z usunięciem `obj/` i `bin/`**
i restore od zera. Za każdym razem **7360 / 7360**.

**Przyczyna, odtworzona:** to **niezgodność wersji `Avalonia.Headless` z corem** — czyli dokładnie **stan
pośredni Kroku 1**, jedyna konfiguracja tego sprintu, której świadomie nie testowałem (`cc9c0ee`: core 12.1.1,
Headless 12.0.3). Zbudowana w osobnym `git worktree` daje:

```
System.TypeLoadException : Method 'SetFrameThemeVariant' in type 'Avalonia.Headless.HeadlessWindowImpl'
from assembly 'Avalonia.Headless, Version=12.0.3.0, …' does not have an implementation.
   at Avalonia.Headless.AvaloniaHeadlessPlatform.HeadlessWindowingPlatform.CreateWindow()
   at Avalonia.Controls.Platform.PlatformManager.CreateWindow()
   at Avalonia.Controls.Window..ctor()
```

Core 12.1.1 dodał `SetFrameThemeVariant` do kontraktu implementacji okna; `Avalonia.Headless` 12.0.3 go nie
implementuje, więc **każdy test konstruujący `Window`** wybucha `TypeLoadException` w konstruktorze. To
uderza wyłącznie w zestaw headless (128 testów) i **nie dotyka ani jednej asercji poza nim** — co zgadza się
z rzędem wielkości zgłoszenia.

⚠ **Build jest przy tym 0/0 i restore nie mówi ani słowa** — `Avalonia.Headless` 12.0.3 wymaga
`Avalonia >= 12.0.3`, co core 12.1.1 spełnia. To gotcha **#321** w drugim wydaniu, tego samego dnia: zakres
`>=` znów zamienił niezgodność w konfigurację wyglądającą na wspieraną.

**⛔⛔ I TU JEST ZNALEZISKO WAŻNIEJSZE OD SAMEJ PRZYCZYNY: TEN SAM ZEPSUTY STAN RAPORTUJE TRZY RÓŻNE RZECZY,
W TYM SUKCES.** Trzy przebiegi tej samej komendy na tym samym commicie:

| Przebieg | Wynik |
|---|---|
| `dotnet test --blame-hang` | **`Powodzenie!` — 0 niepowodzeń, 7232 / łącznie 7232** ⚠ 128 testów **zniknęło z sumy**, a przebieg mówi „sukces" |
| `dotnet test` (bez `--blame-hang`) | **`Niepowodzenie!` — 94 niepowodzenia / 7360** |
| `dotnet test` (bez `--blame-hang`, powtórka) | **zawiesił się** — przerwany po 10 minutach |

⭐ Niedeterminizm ma wyjaśnienie i jest nim… to, co 12.1.1 naprawia: `TypeLoadException` lecący przez
`HeadlessUnitTestSession.DispatchCore` zostawia sesję w stanie nieokreślonym (#21781 — *„leaves current and
future Dispatch calls waiting forever"*). Zepsuta sesja headless może więc dać ciszę, N awarii albo zawis.

⭐⭐ **Wniosek metodologiczny, szerszy niż ten sprint: „0 niepowodzeń" NIE jest kryterium zielonej suite —
kryterium jest SUMA.** Nasza własna, udokumentowana komenda QA (`--blame-hang`) potrafi zaraportować
`Powodzenie!`, gdy **cała partycja headless nie wystartowała**. Liczba 7360 w CLAUDE.md przestaje być
ciekawostką i staje się asercją: jeśli suma jest inna, przebieg nie jest zielony, choćby nic nie zawiodło.
⚠ Moje własne QA tego sprintu przeszło tylko dlatego, że sprawdzałem liczby per partycja **i** sumę w pełnym
przebiegu — nie dlatego, że komenda mnie ochroniła.

**Jak użytkownik mógł to zobaczyć, a ja nie** (do rozstrzygnięcia jedną komendą, niżej): przebieg na commicie
Kroku 1 w trakcie sprintu · albo `--no-build` / `--no-restore` na wyjściu, w którym `Avalonia.Headless.dll`
został jeszcze w wersji 12.0.3 obok nowego core'a · albo `obj/project.assets.json` z przed Kroku 2.

**Rozstrzygające sprawdzenie — jedna komenda:**

```bash
powershell -NoProfile -Command "(Get-Item tests/EmberTern.Tests/bin/Debug/net9.0/Avalonia.Headless.dll).VersionInfo.FileVersion"
```

`12.1.1.0` ⇒ stan poprawny, a zgłoszenie pochodziło z innego stanu. Cokolwiek innego ⇒ to ta przyczyna,
i lekiem jest `dotnet clean` + usunięcie `obj/` i `bin/` + restore (dokładnie to, co zrobiłem przy weryfikacji
i po czym suma wróciła do 7360).

**Rekomendacja — NIE osobny sprint; jedno małe zadanie z dwoma guardami:**

1. **Guard na sumę testów** — asercja, że `Avalonia.Headless` załadowany w procesie testowym ma **tę samą
   wersję co `Avalonia.Base`**. Jeden test, kilka linii, zamienia całą tę klasę awarii w jedną czytelną
   czerwoną asercję zamiast 94 `TypeLoadException` albo — gorzej — cichego sukcesu.
2. **Poprawka komendy QA w CLAUDE.md** — dopisać, że kryterium jest `łącznie: 7360`, nie „0 niepowodzeń".
   ⚠ To jest realna zmiana w dokumentacji procesu, bo obecny zapis daje się spełnić przez przebieg,
   w którym 128 testów nie wystartowało.

⛔ **Czego NIE rekomenduję:** naprawiania czegokolwiek w kodzie produkcyjnym ani w testach — nie ma tam
defektu. Stan oddany jest zielony 13×, w tym po budowie od zera.

### 9.2 Numery gotchy 303 i 304 są zdublowane

Zmierzone przy okazji Kroku 5 (§7). Odwołania w CLAUDE.md w postaci „gotchas #303/#304" są dziś
**niejednoznaczne**. Naprawa = wybór, który wpis zachowa numer, plus aktualizacja odwołań — decyzja, nie
sprzątanie.

---

## 10. Runda dodatkowa po odbiorze (2026-08-05) — dwa pakiety poza Avalonią

QA wzrokowe użytkownika przeszło bez regresji; sprint odebrany. Dwa punkty domknięte po odbiorze.

### 10.1 `AvaloniaUI.DiagnosticsSupport` 2.2.1 → **2.2.3** ✅ ZROBIONE

Build **0/0 w Debug i Release**; suite **7360 / 7360**. ⭐ Zweryfikowane osobno, że warunkowe
`IncludeAssets`/`PrivateAssets` nadal działa: `AvaloniaUI.DiagnosticsSupport.Avalonia.dll` **jest**
w `bin/Debug`, a w `bin/Release` **go nie ma**. Bez tego sprawdzenia bump byłby zmianą tego, co ships,
a nie tylko narzędzia deweloperskiego.

### 10.2 `System.Security.Cryptography.*` — ⛔ **ZOSTAJE, decyzja uzasadniona**

**Pytanie dotyczyło DWÓCH pakietów, nie jednego** — `--outdated` raportuje oba:

| Pakiet | Projekt | Mamy | Najnowsze | Czy ships? |
|---|---|---|---|---|
| `System.Security.Cryptography.ProtectedData` | **EmberTern.App** | 9.0.0 | 10.0.10 | **tak** |
| `System.Security.Cryptography.Xml` | EmberTern.Tests | 8.0.4 | 10.0.10 | nie (`IsPackable=false`) |

**Czy któryś jest dostarczany przez framework? Nie — oba są genuinnie out-of-band.** `ProtectedData` opakowuje
Windows DPAPI i nie ma go w shared framework na .NET Core+ (dlatego referencja istnieje); `Xml` również jest
OOB i u nas jest **security-overridem** przechodniego pinu NPOI, świadomie ustawionym na **załataną** wersję
8.0.x dla nazwanych advisory.

**⚠ Przesłanka „przy .NET 10" nie zachodzi:** `Directory.Build.props` ustawia **`net9.0`**. (CLAUDE.md zapisuje,
że ta linia raz błędnie twierdziła `net10.0` i została skorygowana 2026-07-27 — warto o tym pamiętać, bo
pomyłka jest naturalna.) Pasmo pasujące do net9.0 to **9.0.x**, nie 10.0.x.

**Zmierzone, nie założone:** `dotnet list package --vulnerable --include-transitive` → **zero podatnych
pakietów we wszystkich pięciu projektach**. Więc przejście nie wnosi korzyści bezpieczeństwa — a jako
referencja **bezpośrednia** `ProtectedData` jest dodatkowo pod `NU1902`/`NU1903` przy
`TreatWarningsAsErrors=true`, czyli nowe advisory i tak zerwałoby build (gotcha #278).

**⭐ Argument decydujący jest jednak projektowy, nie wersyjny: `ProtectedData` leży na ścieżce reguły #11.**
Wykonuje `Protect`/`Unprotect` na `settings.dat` — profilach połączeń i hasłach. Podmiana biblioteki
kryptograficznej **przez granicę pasma, bez zmierzonej korzyści**, na powierzchni klasy data-loss, jest
dokładnie tym, czego reguła #11 zabrania.

**⚠⚠ I TU JEST TRZECIA ODPOWIEDŹ, KTÓRĄ NARZĘDZIE UKRYŁO — a bez niej decyzja wygląda na „wszystko albo
nic".** Istnieje **`ProtectedData` 9.0.18** (2026-07-14), czyli **servicing w naszym pasmie**; jesteśmy 18
patchy za nim *wewnątrz własnego pasma*. `dotnet list package --outdated` pokazuje wyłącznie najnowszą wersję
**ogólnie**, więc gdy istnieje nowsze pasmo (10.0.x), **aktualizacja w pasmie jest niewidoczna**. ⭐ Repo ma
już w tym pasmie precedens: `System.IO.Packaging` w EmberTern.Office stoi na **9.0.18** — czyli 9.0.0 jest
niespójne z naszym własnym wzorcem, i nikt tego nie widział, bo raport pokazywał tylko skok przez pasmo.

**Decyzja:** ⛔ **nie przechodzimy do 10.0.10** (żadnej korzyści, TFM inny, reguła #11) · ⏸ **9.0.18 to osobny,
mały krok z własną weryfikacją round-tripu `settings.dat`** (zapis, odczyt, `.bak`, profil z hasłem) — nie
„przy okazji" sprintu Avalonii, dokładnie z tego samego powodu, dla którego AvaloniaEdit dostał własne
zadanie. Powód zapisany **przy `PackageReference`**, nie tylko tutaj. `Xml` 8.0.4 zostaje bez zmian:
test-only, nie ships, brak advisory, a 8.0.4 jest wersją *załataną* dla tych, dla których został podniesiony.

### 10.3 Test dostosowany, nie tropiony dalej — `BrandingPresentationTests` do partycji izolowanej

W trakcie tej rundy `BrandingPresentationTests.EveryWindow_TakesTheApplicationIcon_FromTheOneStyle` zaczął
padać **~1 na 3** w przebiegu zgrupowanym z komunikatem *„The calling thread cannot access this object because
a different thread owns it"* (rodzina gotchy #226). ⚠ To **nie** ta sama sprawa co §9.3 — tam był
`TypeLoadException` z niezgodności wersji; tutaj wyścig w dzielonej sesji headless.

⛔ **Dyrektywa użytkownika, która ustawiła sposób pracy (2026-08-05):** *„skoro aplikacja na nowej wersji nie
wykazuje problemów to moim zdaniem trzeba testy dostosować, a nie w kółko skakać między wersjami"*. Słusznie —
przerwał mi kolejny cykl pomiarów wersji, który nie prowadził do niczego.

**Co zostało zmierzone i odrzucone, żeby nikt tego nie powtarzał:**

| Próba | Wynik |
|---|---|
| `AvaloniaUI.DiagnosticsSupport` 2.2.3 jako podejrzany | ⭐ **oczyszczony** — po zejściu do 2.2.1 test padał dalej (3 z 6) |
| usunięcie `Show()` (zostaje `ApplyTemplate()`) | ⛔ **styl się wtedy nie aplikuje, `Icon` = null** — test przechodziłby, nie dowodząc niczego |
| try/finally + pompowanie dispatchera po `Close()` | ⚠ **nie naprawiło** (dalej 1 z 6); zostawione jako poprawny teardown, z komentarzem mówiącym wprost, że nie jest naprawą |
| uruchomienie klasy **w izolacji** | ✅ **6/6 zielone** |

**Zastosowane:** klasa przechodzi do **partycji izolowanej**, obok `ConnectionExpandBindingProbe` — czyli tam,
gdzie stojąca dyrektywa użytkownika z 2026-08-01 już umieściła jedną fragilną klasę headless. Nowy podział:
**7232 + 73 + 55 = 7360**, każda partycja zweryfikowana **3×**.

⭐ Powód, dla którego to właśnie ta klasa wymaga izolacji, jest konkretny, a nie zabobonny: **jest jedynym
testem headless otwierającym prawdziwe okno platformowe** (`Show()`), więc jedynym, którego obiekt żyje
w `HeadlessWindowingPlatform` dotykanym potem przez sesje pozostałych klas.

⛔ **Nie osłabiono asercji** do „setter istnieje w pliku" — to dokładnie ta wystarczalność, której ten test ma
dowodzić, że jej nie ma (styl kompiluje się niezależnie od tego, czy `Window.Icon` jest styled property).

⏸ **Poza zakresem pytania, odnotowane bez działania:** `--outdated` raportuje też `DocumentFormat.OpenXml`
3.1.0 → 3.5.1, `ExcelDataReader` 3.7.0 → 3.9.0, `System.IO.Packaging` 9.0.18 → 10.0.10,
`Microsoft.NET.Test.Sdk` 17.11.1 → 18.8.1, `NPOI` 2.7.2 → 2.8.0, `SixLabors.ImageSharp` 2.1.11 → **4.0.0**
(dwa pasma major), `xunit` 2.9.2 → 2.9.3, `xunit.runner.visualstudio` 2.8.2 → **3.1.5**. Ta sama analiza
należy się każdemu z osobna; ⭐ w szczególności `ImageSharp` i `xunit.runner.visualstudio` to skoki major
w projekcie testowym, a `Test.Sdk` 18.x zmienia runner — czyli **dokładnie ta warstwa, która w §9.3 okazała
się źródłem nieporozumienia o „ponad 60 testach"**. Nie ruszać bez własnego kroku.

---

## 11. Partycjonowanie suite — stan po sprincie

| Partycja | Filtr | Liczba |
|---|---|---|
| główna | wyklucza wszystkie **osiem** nazw klas headless | **7232** |
| grupa headless | `SettingsCenterViewTests` · `DesignTokenApplicationTests` · `TabStripPresentationTests` · `MetadataTreeVirtualizationProbe` · `SharedContextMenuFeasibilityProbe` · `EditableGridEnterTests` | **73** |
| **izolowana** | `ConnectionExpandBindingProbe` · **`BrandingPresentationTests`** | **55** |
| | **razem** | **7360** |

⚠ **Filtr partycji głównej nadal wyklucza wszystkie osiem nazw** — przeniesienie klasy między partycjami 2 i 3
nie zmienia listy wykluczeń, tylko podział reszty. ⛔ **Grupowy filtr nie może już zawierać
`BrandingPresentationTests`** (dlaczego: §10.3).

⛔⛔ **Kryterium zielonego przebiegu to SUMA, nie „0 niepowodzeń"** — zmierzone w §9.3 i wpisane do CLAUDE.md.

---

## 12. Stan po scaleniu (2026-08-05)

| | |
|---|---|
| Merge | `42a6b98` — `--no-ff` do **`feat/product-polish`**, zgodnie z regułą *„gałąź odcięta od gałęzi funkcji wraca do TEJ gałęzi"* |
| `master` | **nietknięty** — bez zmian, zgodnie z decyzją użytkownika |
| Gałąź techniczna | `chore/avalonia-12.1.1` **wycofana lokalnie** (`git branch -d`, wariant bezpieczny) |
| Remote'y | ⭐ **gałęzi tam nigdy nie było** — nie została wypchnięta przed odbiorem, więc krok „usuń na obu remote'ach" nie miał czego usuwać; sprawdzone `git ls-remote --heads` na `origin` i `private` |
| Weryfikacja **po** scaleniu | build **0/0 w Debug i Release** · **7232 + 73 + 55 = 7360** · smoke Release: proces żyje, **0 wpisów `FATAL`** |
| Push | `feat/product-polish` na **oba** remote'y |

⭐ **Siedem commitów sprintu zostaje osobno** (Krok 0 → dokument + baseline, Kroki 1–5 po jednym, runda
dodatkowa po odbiorze), dlatego merge jest `--no-ff`: łuk sprintu ma pozostać czytelny, a `git bisect` ma
trafiać w pojedynczą zmianę wersji, nie w jeden wielki commit.

**Co ten sprint zostawia po sobie poza numerami wersji** — trzy rzeczy, każda ważniejsza od samej aktualizacji:

1. ⛔⛔ **Kryterium zielonej suite to suma.** Nasza własna komenda QA potrafiła zaraportować `Powodzenie!`,
   gdy 128 testów nie wystartowało (§9.3).
2. ⭐ **Gotcha #321** — zakres zależności `>=` zamienia nietestowaną kombinację w konfigurację wyglądającą na
   wspieraną, a restore i build milczą. Wystąpiła w tym sprincie **dwukrotnie**: jako świadoma decyzja
   (AvaloniaEdit, §3 R1) i jako realna awaria (Headless, §9.3).
3. ⭐ **Raport „outdated" ukrywa aktualizację w pasmie**, gdy istnieje nowsze pasmo — co zamienia decyzję
   o pakiecie w fałszywe „wszystko albo nic" (§10.2).

