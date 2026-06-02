# EmberTern — Architektura

## 1. Cel projektu

EmberTern to nowoczesny desktopowy workbench dla deweloperów pracujących z bazą Firebird — głównie programistów ERP i backendu, którzy codziennie piszą SQL, procedury, triggery i pracują z metadanymi oraz transakcjami. Filozofia produktu: **mniej funkcji, lepszy workflow**; jakość codziennej pracy ważniejsza niż liczba checkboxów. Narzędzie jest **świadome transakcji od pierwszej sekundy** — nigdy nie commituje za użytkownika.

## 2. Stack technologiczny

| Warstwa | Technologia | Uzasadnienie |
|---|---|---|
| Runtime | **.NET 9** | Najnowszy LTS-track, natywne wsparcie dla `CodePagesEncodingProvider` (krytyczne dla WIN1250/1252 — typowe charsety polskich ERP-ów) bez dodatkowych NuGetów. |
| UI | **Avalonia 12.0.3** | Cross-platform XAML, dojrzały FluentTheme, działa identycznie pod Windows/Linux/macOS. WPF byłby tylko-Windows; MAUI zbyt zorientowany na mobile. |
| Wzorzec UI | **CommunityToolkit.Mvvm 8.4.2** | `[ObservableProperty]` + `[RelayCommand]` redukuje boilerplate MVVM o ~70% w stosunku do ręcznych implementacji `INotifyPropertyChanged`. |
| Edytor SQL | **AvaloniaEdit 12.0.0** | Numerowanie linii, podświetlanie składni (XSHD), wirtualizacja długich dokumentów, gotowy `CompletionWindow` dla autocomplete. |
| Driver bazy | **FirebirdSql.Data.FirebirdClient 10.3.4** | Oficjalny, w pełni zarządzany (managed) — brak zależności od `fbclient.dll`. |
| Format pliku solution | **`.slnx`** | .NET 10 default; krótszy i czytelniejszy niż klasyczny `.sln`. |
| Testy | **xUnit** | 281 testów, brak żadnego mockującego frameworka — szwy testowe budowane przez parametryzację metod, nie przez interfejsy. |

## 3. Architektura warstwowa

Projekt dzieli się na trzy projekty, **w jednym kierunku zależności**: `App → Firebird → Core`.

### 3.1. `EmberTern.Core` (17 plików)
Czysta logika domenowa. **Zero zależności od Avalonia, zero od Firebird drivera.** Zawiera:
- Modele profili połączeń, katalog charsetów, JSON store (`%APPDATA%\EmberTern\connections.json`).
- DTOs wyniku zapytania (`QueryResult`, `QueryColumn`).
- Enum `MetadataObjectKind` (13 kategorii: Tables, Views, Procedures, Triggers, Functions, Generators, Domains, Packages, Exceptions, Roles, Users, Indices, SystemTables) i rekord `MetadataObject`.
- Persystencja workspace (geometria okna, otwarte taby per-połączenie, saved queries).
- Czysty formatter SQL i pomocnicze parsery (autocomplete: ekstrakcja słowa pod kursorem, rozwiązywanie aliasów `FROM x AS a`).

**Dlaczego rozdzielenie?** Cała logika domenowa testowalna w izolacji bez Avalonia ani Firebirda. Formatter i parser SQL ma 47 testów jednostkowych bez najmniejszego wystąpienia bazy danych.

### 3.2. `EmberTern.Firebird` (12 plików)
Integracja z driverem. Cztery bezpośrednie klasy (brak interfejsów):
- `FirebirdConnectionService` — jedna aktywna `FbConnection`; event `ActiveConnectionChanged`.
- `FirebirdQueryExecutor` — uruchamia zapytania, limit 5000 wierszy, automatyczny `BEGIN TRANSACTION` jeśli nie ma aktywnej.
- `TransactionService` — jedna aktywna `FbTransaction`; manualne commit/rollback.
- `FirebirdMetadataReader` + `FirebirdDdlReader` — odczyt katalogu (`RDB$*`) i rekonstrukcja DDL, każdy odczyt we własnej krótkotrwałej transakcji niezależnej od pracującej transakcji użytkownika.

**Dlaczego rozdzielenie?** Wszystkie szczegóły specyficzne dla Firebirda (peculiarności kodowań BLOB SUB_TYPE TEXT, parsowanie `ServerVersion`, mapowanie typów Firebird→.NET) są tu odizolowane. Warstwa UI nigdy nie widzi `FbException` — łapiemy i opakowujemy w domenowe `MetadataReadException` / `ConnectionFailedException`.

### 3.3. `EmberTern.App` (30 plików C# + 6 plików AXAML)
Warstwa prezentacji. ViewModels, Views, motywy, słowniki UI.
- Główne ViewModele: `MainWindowViewModel`, `MetadataExplorerViewModel`, `ConnectionNodeViewModel`, `MetadataNodeViewModel`, `WorkspaceTabViewModel`, `SavedQueryViewModel`.
- Style i kolory: `Themes/Colors.axaml` (Dark + Light) + `Themes/ControlStyles.axaml`.
- Brandowanie: ikona EXE, ikona okna, logo w pasku tytułowym.

ViewModele nie zawierają typów Avalonia (brak `IBrush`, `Color`, `Thickness`). Komunikacja View→VM odbywa się przez eventy (`ConfirmationRequested`, `ClipboardWriteRequested`) lub callbacki funkcyjne (selekcja w edytorze).

## 4. Kluczowe decyzje architektoniczne

| Decyzja | Uzasadnienie |
|---|---|
| **Brak autocommit. Nigdy.** Auto-*begin* tak (zgodnie z workflow IBExpert), ale commit/rollback zawsze manualnie. | Audytowani devowie ERP muszą widzieć, kiedy commitują. Wypadek typu „myślałem że to dry-run” nie ma prawa się zdarzyć. |
| **Transaction-aware by default.** Pasek stanu transakcji widoczny zawsze, kropka kolorowa (szary/bursztyn/czerwony), licznik instrukcji w aktywnej transakcji. | Zgubienie kontekstu transakcyjnego w narzędziach DB to klasyczne źródło incydentów produkcyjnych. |
| **Workspace persistence per-connection.** Każdy profil połączenia ma własny zestaw tabów, własny aktywny tab, własne saved queries. | Devowie ERP pracują równolegle z 3-4 środowiskami (DEV/UAT/PROD-replica). Wspólny workspace mieszałby SQL między klientami. |
| **Zero Avalonia w `Core`.** | Cała domena testowalna w izolacji. Zamiana frameworka UI (gdyby kiedyś) nie wymaga przepisania logiki. |
| **Brak interfejsów bez dwóch implementacji.** Wszystkie serwisy (`ConnectionService`, `QueryExecutor`, `TransactionService`) to konkretne klasy. | Reguła „abstrakcja na zapas” jest najczęstszą formą over-engineeringu. Pojawi się druga implementacja → wyciągniemy interfejs. |
| **DataGrid Avalonia (wirtualizowany), nie własny `ItemsControl`.** | Wynik 5000 wierszy × 50 kolumn musi przewijać się płynnie. Bez wirtualizacji to ~250k kontrolek w drzewie wizualnym. |
| **Dark + Light od pierwszego dnia.** Każdy nowy kolor trafia do obu słowników. Zero hardkodowanych kolorów w widokach — tylko `{DynamicResource}`. | Doklejenie motywu jasnego post-factum kosztuje 10× więcej niż utrzymywanie obu od początku. |
| **Brak `Utils/`, `Helpers/`, `Common/`.** | Brak naturalnego domu dla klasy → źle podzielona struktura. |
| **Lokalizacja przez `UiStrings` (static class), nie przez `.resx`.** | Pojedyncze miejsce z autocomplete IDE, łatwe `x:Static` z XAML. Pełna i18n nie jest celem V1. |

## 5. Przepływ danych — od kliknięcia „Execute” do wyników

```
   [Użytkownik] ── klik ▶ Execute ─┐
                                   ▼
   [MainWindow.axaml]    KeyBinding F5 / Ctrl+Enter / ▶
                                   │
                                   ▼
   [MainWindowViewModel]  ExecuteQueryCommand → ExecuteQueryAsync()
        │   ResolveActiveSql()  ← zaznaczenie? cały tekst?
        │   IsExecuting = true   → toolbar przełącza ▶ na ⏹
        ▼
   [FirebirdQueryExecutor.ExecuteAsync(sql, ct)]
        │   BeginTransactionAsync()    jeśli brak aktywnej tx
        │   FbCommand → FbDataReader
        │   pętla odczytu z limitem 5000 wierszy
        │   wyjątek → ConnectionFailedException / OperationCanceledException
        ▼
   [QueryResult { Columns, Rows, Elapsed, Truncated, RecordsAffected }]
        │
        ▼
   [MainWindowViewModel]
        │   CurrentResult = result
        │   CurrentResultVersionTag = Guid (force rebuild kolumn)
        │   QueryStatsText = „50 rows in 125 ms”
        │   Messages.Add(Info „...”)
        │   BottomPanelTab = Results | Messages
        ▼
   [MainWindow code-behind] PopulateResultGrid()
        │   buduje DataGridColumns dynamicznie
        │   _resultGrid.ItemsSource = result.Rows  (object?[])
        ▼
   [Avalonia DataGrid]  wirtualizacja wierszy → render
        ▼
   [Użytkownik] widzi wyniki + status w pasku dolnym
                + kropka transakcji bursztynowa + licznik instrukcji
```

## 6. Statystyki projektu

| Metryka | Wartość |
|---|---|
| Projekty w solution | 4 (Core, Firebird, App, Tests) |
| Pliki C# | 88 |
| Pliki AXAML | 6 |
| Testy jednostkowe | **281** (wszystkie zielone) |
| Pokrycie warstw przez testy | Core: pełne; Firebird: SQL/predykaty przez `InternalsVisibleTo`; App: ViewModels (bez UI) |
| Ukończonych milestone'ów | 14 (M1-M6 V1 + 8 milestone'ów polish/V1.1) |
| Zależności runtime | 3 NuGety produkcyjne |
| Konfiguracja kompilatora | `Nullable=enable`, `TreatWarningsAsErrors=true` |

## 7. Co zostało zbudowane

**Połączenia.** Manager profili (CRUD z duplikowaniem), test połączenia, obsługa custom `fbclient.dll`, mapowanie błędów na czytelne komunikaty, charset catalog z 13 mapowaniami Firebird→.NET Encoding.

**Edytor SQL.** Podświetlanie składni (Firebird XSHD, dwa warianty: dark + light, swap na zmianę motywu), numerowanie linii, autoformatter (Alt+F, lowercase wszystkich identyfikatorów, łamanie ≤120 znaków, idempotentny), autocomplete (Ctrl+Space + auto-trigger od 3 znaków, schema objects + keywords, dot-completion `ALIAS.` → kolumny tabeli z parserem aliasów `FROM x AS a, y JOIN z`), execute na zaznaczeniu lub całości, double-click na nazwę obiektu → otwiera DDL.

**Wykonywanie zapytań.** Limit 5000 wierszy, cancel w trakcie, automatyczny BEGIN, separacja błędów wykonania od błędów połączenia, status w pasku dolnym, log Messages z timestampami.

**Transakcje.** Pasek stanu z kropką (idle/active/error), licznik instrukcji w aktywnej tx, manualne Commit/Rollback w głównym toolbarze, modal-confirm przy disconnect z aktywną transakcją.

**Eksplorator metadanych.** Drzewo IBExpert-style (połączenie → 13 kategorii → leafy), lazy-load per kategoria z placeholderem rozwijającym chevron, eager-load liczników (`Tables (2356)`), filtr po nazwie, refresh, virtualizacja drzewa (testowane na 2356 tabelach), DDL preview per kategoria (tabele z FK/PK/UNIQUE/INDEX, widoki, procedury, triggery z dekodowaniem bitfieldów, funkcje, sekwencje z bieżącą wartością, role, wyjątki, tabele systemowe).

**Workspace.** Persystencja per-połączenie: zestaw otwartych tabów (Query + DDL), tekst SQL, aktywny tab, lista zapisanych zapytań, ostatnio aktywne połączenie. Geometria okna z sanity-check względem ekranów (brak utraty okna przy odpięciu monitora).

**Saved Queries Panel.** Lista zapisanych zapytań per-połączenie, automatyczne nazewnictwo „Query N”, live edit (zmiana w edytorze zapisuje do aktywnego saved query), CRUD z confirm-dialogiem.

**UI/UX.** Custom titlebar (jeden 36px pasek zamiast OS chrome + nagłówek aplikacji), drag, double-click → maximize, kontrolki okna w stylu VS Code, ujednolicony kolor selekcji (TreeView/DataGrid/ListBox/TextEditor), zebra-stripes w DataGrid, copy-to-clipboard z grida (4 tryby), per-kind kolorowe ikony glyphami w drzewie metadanych i tabach.

## 8. Świadomie wykluczone z V1

| Funkcja | Powód wykluczenia |
|---|---|
| **System pluginów** | Premature flexibility. Stabilizujemy core, potem ewentualnie. Każdy plugin API to kontrakt do utrzymywania w nieskończoność. |
| **Autocommit (jakakolwiek forma)** | Naruszenie hard-rule. Cały produkt jest pozycjonowany wokół „wiesz, kiedy commitujesz”. |
| **Web UI / wersja przeglądarkowa** | Desktopowi devowie chcą lokalnego workflowu — szybkość, brak latencji sieciowej do edytora, integracja z systemowymi schowkami i plikami. |
| **AI assistant / Copilot dla SQL** | Skupienie na workflow podstawowym. AI-assist może przyjść po V2, ale nie kosztem stabilności fundamentów. |
| **Debugger procedur** | Wymaga współpracy serwera Firebird (brak natywnego protokołu debug). Realizowalne w V3+, nie w V1. |
| **Schema compare / migrations** | Osobna duża domena. Konkurencja (Red Gate, ApexSQL) ma na to dedykowane produkty. EmberTern nie celuje w ten segment w V1. |
| **Docking / multi-pane layout (a-la Visual Studio)** | Złożoność implementacji rośnie wykładniczo. Prosty layout (sidebar + workspace + bottom panel) pokrywa 95% przypadków użycia. |
| **Event bus / IMessenger** | Brak 3+ komponentów wymagających szerokiej komunikacji. Direct events na serwisach wystarczają. |
| **`Utils/`, `Helpers/`, abstrakcje na zapas** | Sygnał, że struktura jest źle podzielona. Każda klasa ma naturalny dom w istniejących projektach. |
| **Pełna i18n (resx + fallback chain)** | Aplikacja anglojęzyczna w V1, lokalna dla zespołu PL. Wprowadzenie pełnej i18n zamrozi stringi za API zasobów — koszt utrzymania niewspółmierny do korzyści. |

---

*Dokument odzwierciedla stan na 2026-05-31, po ukończonym V1 + Explorer Redesign + V1.1 Workspace Persistence + Per-Connection Workspace + SQL Editor UX + Saved Queries Panel + Visual polish + Autocomplete + Double-click Open DDL.*
