# Aktualizacja Avalonia 12.0.3 → 12.1.1

**Status: SPRINT W TOKU.** Osobny, zamknięty sprint techniczny wykonywany **przed rozpoczęciem M4**,
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
