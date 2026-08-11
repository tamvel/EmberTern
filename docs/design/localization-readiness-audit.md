# Localization Readiness Audit — EmberTern

**Data:** 2026-08-09 · **Gałąź:** `master` (po scaleniu Product Polish M0–M5 + pakiet UX po M5)
**Status:** AUDYT. ⛔ Zero zmian w kodzie, zero tłumaczeń, zero nowych mechanizmów.

---

## 0. Metoda i jej granice (przeczytaj to przed liczbami)

Użyto **czterech niezależnych metod**, celowo mierzących różne rzeczy:

| # | Metoda | Co mierzy | Co przeoczyła |
|---|---|---|---|
| M1 | Skan atrybutów XAML (`xaml_audit.py`) | wartość literalna w nazwanym atrybucie widocznym dla użytkownika | atrybut spoza listy; treść elementu |
| M2 | Skan „prozy" w literałach C# (`cs_audit.py`) | literał *wyglądający* jak zdanie angielskie | **pojedyncze słowa PascalCase** |
| M3 | Skan **kształtu użycia** w C# (`cs_audit2.py`) | literał *podawany* czemuś, co go wyświetla (`=> "X"`, `return "X"`, `Text = "X"`, `throw new …("X")`) | literały krótkie/2-znakowe |
| M4 | Skany celowane (`FilePickerFileType`, polskie znaki, `AutomationProperties`, `x:Static`) | konkretne konstrukcje | — |

⭐⭐ **Weryfikacja heurystyki dała wynik, który sam jest znaleziskiem — i to jest powód, dla którego
raport nie brzmi „0 hardcoded strings":**

* **M2 miała realną ślepą plamę.** Odrzucała pojedyncze słowa w kształcie identyfikatora, więc
  **nie zobaczyła `"Parameters"` i `"Returns"`** w `QuickInfoView.cs:200–201` — a to napisy **na
  ekranie**, w karcie Quick Info. M3, pytająca o *kształt użycia* zamiast o *kształt wartości*,
  znalazła **228 trafień, których M2 nie widziała** (123 w `EmberTern.App`).
* **M3 też miała ślepą plamę.** Filtr długości odrzucił `"Excel"` i `"CSV / TXT"` — nazwy filtrów
  w oknie wyboru pliku. Znalazła je dopiero M4 (`grep FilePickerFileType`).

⚠ Wniosek metodologiczny, ważniejszy od pojedynczych liczb: **licznik kluczowany NAZWĄ (atrybutu,
wzorca, kształtu) nie widzi tego samego zbudowanego inaczej** — gotcha **#337** w kształcie
lokalizacyjnym. Trzy metody znalazły trzy różne podzbiory. Liczby niżej należy czytać jako
**dolne ograniczenie**, nie jako komplet.

---

## 1. Localization readiness — tabela

| Obszar | Wynik |
|---|---|
| **XAML hardcoded user-visible** | ⚠ **17** (na 1 260 wartości tekstowych; 1 239 idzie przez `UiStrings`) |
| **C# hardcoded user-visible (App)** | ⛔ **≈184** wystąpień w ~35 plikach |
| **ToolTips** | ⚠ **2** literalne (`PerformancePanelView:296`, `SessionManagerTabView:209`); reszta z `UiStrings` |
| **Dialogi** | ⚠ tytuł + filtry okien wyboru pliku (**5**), placeholdery `NewConnectionDialog` (**2**) |
| **Menus/context menus** | ⚠ **5** pozycji (`MainWindow.axaml:561–577`); pozostałe 367 nagłówków z `UiStrings` |
| **Validation/errors** | ⛔ komunikaty wyjątków w App (**6**) + **cała warstwa błędów w Core/Firebird** |
| **Status/messages** | ⛔ statusy Trace, Verdict, ExecutionDetails, Verdict/rows, TableAccessBar |
| **Settings** | ✅ `SettingsCatalog` (App) w 100 % na `UiStrings` — ⛔ ale statusy błędów z Core są surowe |
| **Accessibility text** | ➖ **0 wystąpień `AutomationProperties.*`** — nie ma czego lokalizować (osobny temat a11y, poza zakresem) |
| **VM-generated UI text** | ⛔ **główne skupisko defektów** — etykiety rodzajów, statusy, jednostki, formaty |
| **External/server messages** | ⚠ świadomie surowe (Firebird), **ale opakowania są po angielsku i w Core** |
| **Existing localization mechanism** | ⛔⛔ **NIE ISTNIEJE** |

---

## 2. Mechanizm języka — stan faktyczny

Zweryfikowane w kodzie, nie z dokumentacji:

| Pytanie | Odpowiedź (zmierzona) |
|---|---|
| Gdzie jest wartość? | `Preferences.Language` → `settings.dat` (`PreferencesStore`) |
| Jakie języki zdefiniowane? | **jeden**: `PreferenceOptions.LanguageEnglish = "en"` |
| Kto ją czyta? | **`SettingsCenterViewModel.cs:719` — i nikt więcej.** Wyłącznie po to, żeby pokazać bieżącą wartość w oknie Settings |
| Jak propaguje się do UI? | **nijak** |
| Czy działa bez restartu? | pytanie bezprzedmiotowe — nie działa w ogóle |
| Jeden centralny mechanizm tłumaczeń? | **nie ma żadnego** |
| `.resx` / `ResourceManager` / `CultureInfo.CurrentUICulture`? | **zero wystąpień** |
| Markup extension lokalizacyjny? | brak (istnieją `{app:MenuIcon}` i `{app:CommandGesture}` — wzorzec do naśladowania) |

⭐ **To nie jest defekt — to zapisana, ratyfikowana decyzja.** `PreferenceOptions.cs:206–214` mówi
wprost: *„EmberTern is NOT prepared for localization… this catalog is deliberately storage-and-validation
ONLY"*, z jawnym zakazem „przygotowywania na zapas" częściowym mechanizmem. Etap LOCALIZATION jest
dokładnie tym momentem, w którym ten zakaz przestaje obowiązywać.

⚠ **Sprostowanie liczby w tym komentarzu:** mówi on o **1 815** `const`. Zmierzone dziś:
**1 992 `public const string` + 41 `static readonly string` = 2 033 składowe** `UiStrings`,
**1 242 użycia `{x:Static}` w 62 plikach `.axaml`** (940 różnych) i **967 różnych odwołań w C#**.
Liczba w prozie zestarzała się o ~10 % (#284 w kształcie licznika).

---

## 3. ARCHITECTURAL RISKS — dwie bariery, obie twarde

### R‑1 ⛔⛔ `const` + `{x:Static}` = wartość wypalona w czasie kompilacji

To jest **bariera numer jeden i nie da się jej obejść tłumaczeniem**:

* `public const string` jest **inline'owany przez kompilator** w każde miejsce użycia. Po kompilacji
  **nie istnieje pole, które można by podmienić** — ani w App, ani w testach.
* `{x:Static}` **nie jest bindingiem**. Zwraca wartość raz, ustawia ją jako wartość lokalną i
  **nigdy nie re-ewaluuje**. Nawet gdyby `const` zamienić na `static` (mutowalne), 1 242 miejsca
  w XAML i tak pokazywałyby wartość z chwili załadowania widoku.

⭐ **Precedens, który pokazuje różnicę:** motyw przełącza się na żywo, bo idzie przez
`{DynamicResource}`, który re-ewaluuje. `{x:Static}` takiej właściwości **nie ma**. To znaczy, że
**wybór między „język po restarcie" a „język na żywo" jest decyzją architektoniczną podejmowaną
TERAZ**, a nie detalem implementacyjnym — i przesądza, czy 1 242 miejsca XAML trzeba ruszyć.

### R‑2 ⛔ Warstwa Core/Firebird produkuje angielską prozę, a nie może sięgnąć do `UiStrings`

Reguła architektury #1 (Core bez zależności od App) sprawia, że **`UiStrings` jest fizycznie
nieosiągalne** dla Core i Firebird — a te warstwy generują teksty, które trafiają na ekran
**dosłownie**:

| Miejsce | Co produkuje | Dowód, że jest widoczne |
|---|---|---|
| `Core/Performance/**` (~75 literałów) | tytuły, wyjaśnienia, rekomendacje i „What to investigate" doradcy wydajności | cała zakładka Performance |
| `Core/Diagnostics/SessionHealthAnalyzer.cs` (23) | `Title`, `Impact`, pytania diagnostyczne | Session Manager, karty zdrowia |
| `Core/Sql/Language/DiagnosticsEngine.cs` (7) | `"Unknown object '{0}'."` … `"Ambiguous column '{0}'."` | squiggle + panel Diagnostics |
| `Core/Sql/Language/QuickInfo/QuickInfoEngine.cs` (~12 z 25) | etykiety faktów: `Nullability`, `Default`, `Key`, `Columns`, `Primary key`, `Parameters` | karta Quick Info |
| `Core/Settings/ApplicationSettingsStore.cs` (39) | `LastLoadDiagnostic` / `health.Diagnostic` | **dowiedzione**: `MainWindowViewModel.cs:8404–8408` wstawia to do `UiStrings.SettingsUnreadableWarningFormat` i pokazuje w `MessageBanner` |
| `Core/Settings/Export/SettingsImportReader.cs` (22) | `"This is not an EmberTern settings file."` itd. | dialog importu (CLAUDE.md §15.8: *„failure text is Core's, shown as-is"*) |
| `Core/Import/**` (~20) | błędy wierszy importu | zakładka Errors |
| `Core/Query/ExecutionSummary.cs` (9) | `"inserted 8 · updated 16 · deleted 8 in 93 ms"` | pasek statusu / Messages |
| `Core/Connections/CharsetCatalog.cs` (8) | opisy zestawów znaków | lista w NewConnectionDialog |
| `Firebird/FirebirdConnectionService.cs` (22) | `"Could not connect to {endpoint}: …"` + **komunikat naprowadzający na SRP** | baner błędu połączenia |
| `Firebird/FirebirdDiagnostics.cs` (24) | komunikaty diagnostyczne | powierzchnie błędów |

**Razem ≈250–300 wystąpień prozy user-visible poza zasięgiem `UiStrings`.**

⚠ To **nie jest** naruszenie reguły #1 — to jej **konsekwencja**, zapisana świadomie
(`MapErrorMessage` ma komentarz tłumaczący, dlaczego mieszka w warstwie Firebird). Ale konsekwencją
jest to, że **lokalizacja nie jest zadaniem wyłącznie warstwy App**, i każdy plan zakładający
„przetłumaczmy `UiStrings`" jest z tego powodu niekompletny **o ~15 % powierzchni**.

### R‑3 ⚠ Ryzyko, którego pyta wprost brief: `if (language == "pl")`

**Dobra wiadomość: dziś nie ma ani jednego takiego warunku** (zmierzone — `Language` ma jednego
czytelnika i nie jest przedmiotem żadnego rozgałęzienia). ⛔ Ryzyko jest **przyszłe**: jeżeli
lokalizację zaczniemy od tłumaczenia zamiast od mechanizmu, naturalnym odruchem będzie właśnie
`EnglishUiStrings` / `PolishUiStrings` albo ternary w miejscu użycia. **Architektura nie broni się
przed tym dziś w żaden sposób** — nie ma testu, nie ma seamu, nie ma zakazu.

### R‑4 ⚠ Terminologia jest już normą — i ma strażnika

`docs/design/terminology.md` + `TerminologyTests` (reguła **R‑8**) ustalają, że
`Drop` = operacja DDL, `Delete` = pozostałe usuwanie, `Remove` = element kolekcji. **Polski musi to
rozróżnienie odwzorować** (uzasadnienie użytkownika: *„EmberTern jest narzędziem dla developerów baz
danych, więc chcę zachować informację o tym, jaka operacja DDL zostanie wykonana"*). ⚠ Istniejący
strażnik **czyta angielskie napisy**; po wprowadzeniu drugiego języka trzeba rozstrzygnąć, czy pilnuje
języka źródłowego, czy każdego (#333: strażnik, który przepisuje przesłankę, pęka, gdy przesłanka się
przenosi).

---

## 4. BLOCKERS — do poprawy PRZED tłumaczeniem

⚠ Poniższe to **lista miejsc**, nie lista pojedynczych stringów; pełne wypisy w
`scratchpad/app_candidates.txt`, `pass2_app.txt`, `core_candidates.txt`.

### B‑1 — XAML (17 wystąpień, wymaga zmiany architektury: **NIE**)

| Plik:linia | Tekst | Dlaczego user-visible | Cel |
|---|---|---|---|
| `Views/DebuggerTabView.axaml:149–231` (10) | `Continue`, `Into`, `Over`, `Out`, `To cursor`, `Suspend`, `Next iter`, `Loop exit`, `Stop`, `Restart` | podpisy przycisków paska debuggera | `UiStrings.Debugger*` |
| `Views/MainWindow.axaml:561–577` (5) | `Connect`, `Disconnect`, `Edit`, `Copy`, `Delete` | pozycje menu kontekstowego połączenia | `UiStrings` |
| `Views/NewConnectionDialog.axaml:35` | `Local development` | placeholder pola „Nazwa" | `UiStrings` |
| `Views/PerformancePanelView.axaml:296` | `Sequential (full) table scan` | tooltip legendy | `UiStrings` |
| `Views/SessionManagerTabView.axaml:188` | `All` | etykieta filtra | `UiStrings` |
| `Views/SessionManagerTabView.axaml:209` | `Transaction gap — how far…` (długie zdanie) | tooltip | `UiStrings` |

⭐ **Nie ruszać** (kategoria C): `MainWindow.axaml:15 Title="EmberTern"` (nazwa własna — choć warto
przepiąć na istniejące `UiStrings.AppTitle` dla jednego źródła), `TraceMonitorTabView.axaml:275
Header=" "` (pusta kolumna-separator), `NewConnectionDialog.axaml:54
PlaceholderText="C:\data\example.fdb"` (przykład ścieżki — techniczny).

### B‑2 — App C# (≈184 wystąpień, ~35 plików, zmiana architektury: **NIE**)

Skupiska, od największego:

| Plik | Ile | Co to jest |
|---|---|---|
| `Completion/QuickInfoView.cs:155,199–225` | 22 | etykiety grup + **18 etykiet rodzajów obiektów** (`Table`, `System table`, `Common table expression`, `Record alias`…) |
| `Completion/SqlCompletionData.cs:301–319` | 19 | etykiety rodzajów w liście IntelliSense |
| `ViewModels/MainWindowViewModel.cs:2089,3061,5180,5362,6515–6527,7934` | 19 | `New folder`, ` (Copy)`, `Trace: `, `(none)`, 13 rzeczowników rodzaju (`table`, `view`…) |
| `ViewModels/MetadataNodeViewModel.cs:293–305` | 13 | `TypeLabel` — etykiety rodzajów w drzewie |
| `ViewModels/ExecutionDetailsViewModel.cs:31–72` | 12 | `prepare `, ` · fetch `, `Plan + timings`, `MON$ (…)`, `Timings: `, `Capture: ` |
| `ViewModels/VerdictViewModel.cs:21–62` | 10 | `Fast`/`Acceptable`/`Needs attention`/`Slow`, `1 row`/` rows`/` rows changed`/` read` |
| `Completion/NavigationController.cs:1180,1207,1381–1387` | 9 | `Loading…` + etykiety rodzajów Peek |
| `ViewModels/TraceEventDetailViewModel.cs:99–140` | 7 | `Trigger event`, `what fired`, `pid `, ` ms`, `reads`, `writes`, `fetches` |
| `ViewModels/TraceLensViewModels.cs:31–82` | 7 | ` min`, `no tx`, `Transaction `, `System events`, formaty |
| `Commands/CommandTip.cs:80–86` | 7 | `Enter`, `Backspace`, `Esc`, `Del`, `Space`, `PageUp`, `PageDown` — ⚠ patrz „decyzja do podjęcia" |
| `ViewModels/TraceMonitorTabViewModel.cs:213–220` | 6 | `Recording`, `Paused`, `Starting…`, `Stopping…`, `Error`, `Stopped` |
| `ViewModels/TableAccessBarViewModel.cs:41–62` | 6 | `seq`, `idx`, `ins`, `upd`, `del` |
| `Export/SqlCopyReasonText.cs:86–90` | 5 | `procedure`, `view`, `function`, `system table`, `not a table` — sklejane w komunikat odmowy |
| `Completion/ParameterHelper.cs:234–304` | 4 | `(procedure)`, `(function)`, formaty |
| `Export/ExportService.cs:61–73` | 4 | komunikaty wyjątków eksportu |
| `Procedure/FunctionDetailTabViewModel` | 4 | `{0} rows read`, ` · {0} read` (po 2 w bliźniakach) |
| `ViewModels/FindingViewModel.cs:45–47` | 3 | `High/Medium/Low confidence` |
| `ViewModels/TableDetailTabViewModel.cs:2076,2328,2492` | 3 | opisy zmian oczekujących (`Add FOREIGN KEY `, `Add index `) |
| `Controls/TableColumnPicker.cs:67,71` | 2 | `Filter tables…`, `Filter columns…` |
| `Views/NewConnectionDialog.axaml.cs:53,57` | 2 | **tytuł okna wyboru pliku** + nazwa filtru `Firebird databases` |
| `Views/MainWindow.axaml.cs:919–920` | 2 | nazwy filtrów `CSV / TXT`, `Excel` |
| `Security/DpapiSecretProtector.cs:39,56` | 2 | `DPAPI secret protection is only available on Windows.` |
| `AddFieldDialogViewModel:363` / `NewTableTabViewModel:476` | 2 | placeholdery `<field>`, `<table>` |
| pozostałe pojedyncze | ~14 | `Controls/SearchableComboBox.cs:118 "Filter…"`, `ConnectionListItemViewModel:23 "(no path)"`, `PerformanceInsight:124 "Sub-query"`, `SessionManagerTabViewModel:112 "what it means"`, `ImportSourceSectionViewModel:234 "ISO (yyyy-MM-dd)"`, `ScriptExecutorTabView.axaml.cs:82 "SQL scripts"`, formaty w `AggregationLineViewModel`, `DependencyGroupNode`, `KeyboardShortcutsViewModel`, `DatabasePropertiesViewModel` … |

⭐⭐ **Wzorzec, który przebija się przez całą tę listę: etykieta RODZAJU OBIEKTU jest zapisana
NIEZALEŻNIE w pięciu miejscach** — `QuickInfoView.KindLabel`, `SqlCompletionData`,
`MetadataNodeViewModel.TypeLabel`, `NavigationController.KindLabel`,
`MainWindowViewModel:6515`. Pięć list tych samych słów, wolnych do rozjechania się. **To jest
naruszenie zasady „jeden właściciel" istniejące już dziś** — lokalizacja tylko je ujawnia,
a tłumaczenie bez konsolidacji dałoby **pięć list po polsku**.

### B‑3 — Core/Firebird (≈250–300 wystąpień, zmiana architektury: **TAK**)

Pełna tabela w §3 / R‑2. **Nie da się tego przenieść do `UiStrings`** bez rozstrzygnięcia, w którą
stronę idzie zależność. Trzy warianty (do decyzji, nie rozstrzygam ich tutaj):

1. **Klucz zamiast tekstu** — Core zwraca `MessageKey` + argumenty, App rozwiązuje. Najczystsze,
   najdroższe, dotyka publicznych kontraktów **zamkniętych modułów** (Data Import ma stojącą
   dyrektywę „wracać tylko po rzeczywisty defekt funkcjonalny").
2. **Katalog w Core** — `Core.Localization` z własnym słownikiem; App czyta ten sam mechanizm.
   Reguła #1 zachowana (Core nadal bez Avalonii), jeden mechanizm dla obu warstw.
3. **Zostawić surowe po angielsku** — świadomy nazwany wyjątek dla komunikatów SERWERA
   (`MapErrorMessage` ma już takie uzasadnienie), ale **nie** dla doradcy wydajności ani diagnostyk
   edytora, które są w 100 % naszą prozą.

### B‑4 — Konsolidacja przed tłumaczeniem (zmiana architektury: **TAK**, mała)

⛔ **Tłumaczenie bez tego kroku utrwali dług:** pięć list etykiet rodzajów (B‑2), dwie listy nazw
klawiszy, cztery kopie `{0} rows read`.

---

## 5. ALREADY READY — co jest zrobione dobrze

* ✅ **`UiStrings` jako jedno miejsce** — 2 033 składowe, **1 239 z 1 260** wartości tekstowych
  w XAML (98,3 %) już przez nie idzie. To jest solidny fundament, nie fasada.
* ✅ **`SettingsCatalog` w 100 % na `UiStrings`** — łącznie z etykietami kategorii i opisami
  ratyfikowanymi w etapie 6.
* ✅ **Skróty klawiszowe nie są wpisywane w tekst** — 41 składowych jest `static readonly` i składa
  gest z `CommandCatalog` przez `CommandTip`; **pilnuje tego `UiStringsShortcutSourceTests`**.
  ⭐ To jest dokładnie ten wzorzec, którego potrzebuje lokalizacja: *tekst tutaj, wartość zmienna
  stamtąd*, plus strażnik na regresję.
* ✅ **Zero polskich napisów w produkcie** — jedyne polskie teksty to komentarze i log
  `TreeDiagnostics` (ukryte narzędzie deweloperskie za `EMBERTERN_TREE_DIAG`, **nie UI**).
* ✅ **`ProseNumbers`** (`Core/Formatting`) już rozwiązuje jeden problem typograficzny prozy
  (niełamliwe separatory liczb) w **jednym** miejscu — obejmie teksty przyszłe automatycznie.
* ✅ **Wzorzec markup extension istnieje** (`{app:MenuIcon}`, `{app:CommandGesture}`,
  `MenuMarkup.cs`) — `{app:Loc}` byłby trzecim, nie pierwszym.
* ✅ **Zero `AutomationProperties`** ⇒ brak długu a11y-tekstowego do lokalizacji (osobno: to
  prawdopodobnie luka dostępności, ale **poza zakresem tego etapu**).
* ✅ **Nowe etapy nie pogorszyły stanu.** Database Properties (najnowszy) — 11 odwołań do
  `UiStrings`, w tym komunikat SRP; Settings — komplet. ⚠ Wyjątki są **starsze**: debugger
  (`DebuggerTabView.axaml`), Trace, Performance — moduły sprzed dyscypliny `UiStrings`.

---

## 6. RECOMMENDATION — odpowiedzi wprost

### 1. Czy możemy od razu przejść do implementacji polskiego?
⛔ **NIE.** I blokerem nie są znalezione stringi — jest nim to, że **mechanizmu lokalizacji nie ma
w ogóle**. Dziś nie istnieje miejsce, do którego można by *wpisać* polskie tłumaczenie: `const` jest
inline'owany przy kompilacji, a `{x:Static}` nie re-ewaluuje. Przetłumaczenie `UiStrings.cs` w
miejscu dałoby aplikację **wyłącznie polską**, bez możliwości powrotu do angielskiego.

### 2. Czy najpierw trzeba wykonać cleanup hardcoded strings?
✅ **TAK — ale to jest KROK DRUGI, nie pierwszy.** Kolejność ma znaczenie:

> **Najpierw mechanizm, potem cleanup, potem tłumaczenie.**

Powód jest praktyczny: cleanup wykonany *przed* rozstrzygnięciem mechanizmu przeniesie ~430
stringów do `const`-owego `UiStrings` — czyli **do miejsca, które i tak trzeba będzie przebudować**,
i zrobi tę pracę dwa razy. Cleanup wykonany *po* rozstrzygnięciu trafia od razu do docelowego
kształtu.

⭐ **Wyjątek: B‑4 (konsolidacja pięciu list etykiet rodzajów) opłaca się zrobić niezależnie i
wcześnie** — to naprawa istniejącego długu „jeden właściciel", wartościowa nawet gdyby lokalizacja
nie doszła do skutku.

### 3. Czy mechanizm lokalizacji wymaga zmian architektonicznych?
⛔ **TAK, i to jest główny wynik audytu.** Trzy rozstrzygnięcia są **decyzjami użytkownika**, bo
zmieniają zakres o rząd wielkości:

| Decyzja | Wariant tani | Wariant pełny |
|---|---|---|
| **D‑1 Kiedy zmienia się język** | **po restarcie** — `UiStrings` z `const` na `static readonly` ładowane raz przy starcie; **1 242 miejsca XAML bez zmian** | **na żywo** — `{app:Loc Key}` zwracający *binding*; trzeba ruszyć 1 242 użycia `{x:Static}` w 62 plikach |
| **D‑2 Nośnik tłumaczeń** | `.resx` (standard .NET, narzędzia, `ResourceManager` gotowy) | własny katalog w stylu `CommandCatalog`/`PreferenceOptions` — ⚠ reguła #6 mówi *„No `AppResources.resx`"*, więc **`.resx` wymaga jawnego uchylenia tej reguły** |
| **D‑3 Co z Core/Firebird** | nazwany wyjątek: komunikaty serwera zostają surowe | Core dostaje własny mechanizm (§B‑3 wariant 2) — bez tego doradca wydajności i diagnostyki edytora zostają po angielsku |

⚠ **D‑1 jest najważniejsza.** Różnica między wariantami to ~1 250 miejsc w XAML. ⭐ Rekomendacja:
**restart wystarczy** — Settings ma już precedens sekcji stosowanej dopiero po starcie
(`Workspaces`, §16.3), a zmiana języka jest operacją wykonywaną raz w życiu instalacji.
⛔ Ale to **propozycja, nie rozstrzygnięcie** — koszt „na żywo" jest znany i mierzalny, więc decyzja
należy do użytkownika.

### 4. Czy po cleanupie dodanie kolejnego języka będzie rzeczywiście proste?
✅ **TAK — pod warunkiem, że mechanizm powstanie przed tłumaczeniem, a nie obok niego.**
Po zamknięciu D‑1/D‑2/D‑3 dodanie trzeciego języka to **jeden wiersz w `PreferenceOptions.Language`
+ jeden plik tłumaczeń**. Zero zmian w widokach, zero zmian w ViewModelach, zero warunków.

⛔ **A jeżeli mechanizm powstanie *po* tłumaczeniu, będzie odwrotnie** — i to jest przewidywalne, nie
hipotetyczne: przy 2 033 stałych i pięciu równoległych listach etykiet rodzajów najkrótszą drogą do
„działającego polskiego" jest `PolishUiStrings` + rozgałęzienie, czyli dokładnie to, czego brief
zakazuje.

---

## 7. Proponowana kolejność etapów (do zatwierdzenia)

| Etap | Zawartość | Produkt |
|---|---|---|
| **L0** | rozstrzygnięcie **D‑1 / D‑2 / D‑3** | decyzje, zero kodu |
| **L1** | mechanizm: nośnik + odczyt + wpięcie w `PreferencesService` + strażnik „żaden nowy `const` user-visible" | działający mechanizm z **jednym** językiem — angielski bez zmiany wyglądu |
| **L2** | konsolidacja **B‑4** (pięć list rodzajów → jedna) | usunięty dług, mniej do przetłumaczenia |
| **L3** | cleanup **B‑1 + B‑2** (XAML 17 + App ≈184) | zero hardcoded w App |
| **L4** | **B‑3** wg decyzji D‑3 (Core/Firebird) | zero hardcoded user-visible w ogóle |
| **L5** | **dopiero tutaj** — tłumaczenie na polski | `pl` jako drugi wiersz katalogu |
| **L6** | QA językowe w obu językach, terminologia wg `terminology.md`, długość napisów w układzie | odbiór |

⚠ **L1 kończy się aplikacją wyglądającą identycznie jak dziś** — i to jest jego zaleta: mechanizm
jest weryfikowalny bajtowo (żaden napis się nie zmienia), zanim ktokolwiek przetłumaczy pierwsze
zdanie.

---

## 8. Materiał źródłowy

Sondy audytowe (poza solucją, w scratchpadzie sesji — nie w repo):
`xaml_audit.py`, `cs_audit.py`, `cs_audit2.py`; wypisy: `app_candidates.txt` (294),
`pass2_app.txt` (123), `core_candidates.txt`, `xaml.json`, `cs.json`.

⚠ Sondy **nie zostały dodane do repo** — jeżeli cleanup ma być mierzalny etapami, warto je
przekształcić w **strażnika testowego** (kształt `TerminologyTests`/`DatePresentationTests`), a nie
utrzymywać jako skrypty; wtedy „ile zostało" jest liczbą, a nie opinią.
