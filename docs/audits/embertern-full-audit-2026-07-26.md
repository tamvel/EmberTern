# EmberTern — pełny audyt produktu i architektury

**Data:** 2026-07-26  
**Zakres:** przegląd statyczny całego repozytorium, architektury, ścieżek SQL/PSQL/DDL, importu, ustawień, UX oraz testów. Kod produkcyjny nie był zmieniany.  
**Weryfikacja:** `dotnet test` wykonał 5 583 testy: 5 582 zaliczone, 1 niezaliczony. Niepowodzenie `DpapiSecretProtector_RoundTrips` wynikało z braku załadowanego profilu Windows w środowisku audytu (`ProtectedData.Protect`), nie z asercji biznesowej. Skan NuGet wykrył dwie porady High dla `System.IO.Packaging 8.0.0`.

## Executive Summary

EmberTern ma fundament znacznie dojrzalszy niż typowy projekt na tym etapie: sensowny podział `App → Firebird → Core`, wyraźnie zdefiniowaną dyscyplinę transakcyjną, bardzo szeroki zestaw testów oraz wyjątkowo dobrą kulturę dokumentowania decyzji i „gotchas”. Moduł językowy SQL/PSQL, debugger, import i eksport są projektowane jako produkty, a nie luźne funkcje UI. To jest właściwy punkt wyjścia do narzędzia premium dla Firebirda.

Jednocześnie produkt nie powinien jeszcze komunikować pełnej gotowości produkcyjnej. Najważniejsza luka nie polega na jakości parsera, lecz na braku spójnej **bramki bezpieczeństwa zmian**: edytory obiektów wykonują autonomiczne `CREATE OR ALTER` bez wykrywania, czy obiekt zmienił się od załadowania, a debugger nadal może uruchomić kod z `IN AUTONOMOUS TRANSACTION` lub generatorami mimo że skutki mogą przeżyć rollback. To bezpośrednio zagraża najważniejszej zasadzie produktu: aplikacja nie może niepewnie zmieniać kodu lub danych użytkownika.

Priorytet na najbliższe sprinty: najpierw bezpieczeństwo zmian i zależności, potem zamknięcie realnego I7 importu, a dopiero później funkcje premium i większy konfigurator. Nie należy refaktoryzować całego systemu na „clean architecture”; należy wyodrębnić kilka konkretnych kontraktów: `ChangeSafetyGate`, centralny rejestr komend/skrótów, ustawienia wersjonowane oraz obserwowalność produktu.

## Mocne strony projektu

| Obszar | Ocena | Dowód / znaczenie |
|---|---|---|
| Granice warstw | mocna | `Core` nie zależy od Avalonia ani drivera; `Firebird` izoluje protokół i specyfikę serwera; zależności projektów są jednokierunkowe. |
| Transakcje danych | mocna | Jedna transakcja robocza na data lane, jawny `READ COMMITTED + REC_VERSION + NOWAIT`, jawny Commit/Rollback i serializacja poleceń przez `SemaphoreSlim`. |
| Równoległość Firebird | mocna | Oddzielne attachmenty Data, Metadata i DDL mają własne locki; jest kontrolowany degraded mode przy nieudanym drugim/trzecim attachu. |
| Jakość testów | bardzo mocna | 5 583 testy, testy parsera, formattera, importu, kontraktów UI i testy headless; wiele krytycznych decyzji ma testy czyste bez serwera. |
| Import — rdzeń | mocna | Jeden `ImportPipeline`, provider/writer jako porty, `DryRunImportWriter`, restrykcyjna konwersja, walidacja charsetu i przypisanie błędu do wiersza źródłowego. |
| Ochrona ustawień | dobra baza | Jednolity kontener ustawień, DPAPI w produkcji, atomowy zapis, migracje i ochrona przed nadpisaniem pliku z przyszłej wersji. |
| Ergonomia edytora | mocna | AST/semantyka, code actions ze sprawdzeniem driftu, completion, diagnostics, nawigacja, rozbudowany debugger oraz świadome zachowanie transakcji. |
| Design System | obiecująca baza | Wspólne tokeny kolorów, style, ikony, oba motywy i własne kontrolki tworzą bazę pod desktopowy, gęsty interfejs. |

## Krytyczne ryzyka — bramki wydania

### A-01 — P0: kompilacja obiektu może nadpisać nowszą wersję innego programisty

`SourceObjectDetailTabViewModel.ExecuteCompileAsync` przekazuje tekst do `FirebirdDdlExecutor`, a ten wykonuje autonomiczny, automatycznie commitowany batch DDL na osobnym attachmencie. Nie ma porównania wersji/fingerprinted source pobranego podczas `LoadAsync` z aktualną definicją tuż przed kompilacją. Jeśli użytkownik ma otwartą procedurę, trigger lub widok, a inna sesja zmieni go później, jego `Compile` może bez ostrzeżenia wykonać `CREATE OR ALTER` na starym buforze.

To jest ryzyko utraty kodu klienta i naruszenie zasady 100% pewności. Niezależny attachment i `WAIT` rozwiązują blokady, lecz nie konflikt wersji.

**Rekomendacja:** przed każdym zapisem obiektu porównać stabilny fingerprint aktualnej definicji z fingerprintiem z chwili otwarcia. Przy różnicy: zablokować zapis, pobrać aktualne źródło i pokazać porównanie `base / local / database`; „force overwrite” może istnieć wyłącznie jako osobna, wyraźnie opisana decyzja. Ta bramka musi obsłużyć wszystkie edytory obiektów, komentarze, serię rekompilacji i save-and-close.

### A-02 — P0: debugger dopuszcza skutki, których rollback nie cofnie

Debugger prawidłowo ma własny attachment i domyślnie rollbackuje własną transakcję (`DebugSessionConnection`). Jednak `DebugPreflight` wykrywa `IN AUTONOMOUS TRANSACTION`, `GEN_ID` i `NEXT VALUE FOR` jedynie jako nieblokujące ostrzeżenia. Autonomiczna transakcja, generator/sekwencja, zewnętrzne efekty procedur oraz potencjalnie `POST_EVENT` mogą pozostać po zakończeniu debugowania mimo rollbacku sesji.

**Rekomendacja:** wprowadzić tryby bezpieczeństwa debuggera:

- domyślny **Safe simulation**: blokuje start dla znanych nieodwracalnych konstrukcji;
- **Risk acknowledged**: wymaga jawnego potwierdzenia na każdy start i wyświetla listę wykrytych skutków;
- opcjonalny **Disposable database / clone** jako docelowy workflow premium.

Należy też rozbudować analizę o wywołania procedur o nieznanej czystości i połączenia zewnętrzne; nie wolno sugerować, że rollback czyni debugowanie bezpiecznym bezwarunkowo.

### A-03 — P1: utrata całych ustawień przy błędzie DPAPI lub uszkodzonym pliku

`ApplicationSettingsStore.Load()` zwraca `null` zarówno dla świeżej instalacji, jak i dla błędu odszyfrowania/odczytu. Facady następnie wykonują wzorzec `Load() ?? new ApplicationSettings()` i zapisują. `ExistingFileIsFromFuture()` dopuszcza nadpisanie pliku, którego nie da się odszyfrować znanym schematem. W rezultacie zapis dowolnej drobnej preferencji może nadpisać plik zawierający profile połączeń, hasła, workspace, saved queries i watch expressions — np. po skopiowaniu na inny komputer albo po problemie z profilem Windows.

**Rekomendacja:** wprowadzić wynik ładowania rozróżniający `Missing`, `Loaded`, `Unreadable`, `Future`, `Corrupt`. Stan `Unreadable`/`Corrupt` musi blokować zapis do czasu decyzji użytkownika; UI powinno proponować kopię zapasową i „utwórz nowe ustawienia”, nigdy robić tego milcząco.

### A-04 — P1: nie wszystkie mutacje kodu przechodzą przez deklarowaną bramkę bezpieczeństwa

`TextEditApplier` jest poprawnie zaprojektowany: waliduje wszystkie edycje, sprawdza oczekiwany tekst, odmawia przy driftcie i aplikuje atomowo. Jego dokumentacja deklaruje jednak „one owner of every change EmberTern makes to a user document”, co nie jest faktem. Bezpośrednie `TextDocument.Replace` występuje m.in. w formatterze, language expansion, typing ergonomics i completion.

Są to głównie akcje inicjowane przez użytkownika, więc nie jest to dziś automatyczna korupcja. Problemem jest brak jednego kontraktu audytowalności: kolejne funkcje mogą ominąć sprawdzanie świeżości, preview, undo-group, telemetrykę i zasady zgody.

**Rekomendacja:** podzielić jawnie dwa przypadki: `UserTypingEdit` (synchroniczny, lokalny, odwracalny) oraz `AssistedCodeEdit` (musi posiadać expected text, atomowość i powód). Wymusić oba przez jeden interfejs/adaptor dokumentu i test architektoniczny zakazujący bezpośrednich `Replace` poza tymi adapterami.

### A-05 — P1: podatna zależność tranzytywna w dostarczanym produkcie

Skan `dotnet list package --vulnerable --include-transitive` wskazał `System.IO.Packaging 8.0.0` jako **High** dla dwóch porad: [GHSA-f32c-w444-8ppv](https://github.com/advisories/GHSA-f32c-w444-8ppv) i [GHSA-qj66-m88j-hmgj). Zależność przychodzi przez `DocumentFormat.OpenXml 3.1.0` w `EmberTern.Export.Office`.

**Rekomendacja:** w najbliższym patchu podnieść/override’ować bezpieczną wersję zgodnie z poradami, dodać SCA do CI i politykę blokującą nowe High/Critical. Ponieważ obecny moduł Office eksportuje, a nie importuje XLSX, rzeczywista ekspozycja wymaga oddzielnego threat modelu — nie zmienia to faktu, że wydanie nie powinno wysyłać znanej podatnej biblioteki.

## Architektura i rozwój

Architektura jest dobra w wymiarze makro, ale dojrzałość funkcjonalna odsłoniła kilka klas o zbyt wielu odpowiedzialnościach: `MainWindowViewModel` (~357 KB), `TableDetailTabViewModel` (~151 KB), `DebuggerTabViewModel` (~141 KB) i `FirebirdTableDetailReader` (~132 KB). Same rozmiary nie są błędem; są sygnałem, że orkiestracja, polityki biznesowe i stan interfejsu zaczynają się mieszać.

Nie zalecam masowego rozbijania klas według mechanicznych reguł. Zalecam wydzielanie tylko wzdłuż stabilnych granic produktu:

- `ChangeSafetyGate`: fingerprint, diff, conflict, force overwrite, audit decyzji;
- `ConnectionSession`: stan trzech lane’ów, degraded-mode, capabilities i diagnostyka połączeń;
- `CommandRegistry`: globalne, kontekstowe i edytorowe komendy oraz ich skróty;
- `SettingsSchema` + `SettingsHealth`: kontrakt importu/eksportu i rozróżnianie stanu pliku;
- `ObjectEditorCoordinator`: wspólne load/dirty/save/revert/refresh dla editorów obiektów;
- w `MainWindowViewModel` oddzielić kompozycję workspace, routing komend i lifecycle połączeń.

Model transakcyjny jest zasadniczo poprawny: Data jest `NOWAIT`, metadata jest izolowana, a DDL ma limitowane `WAIT`. Główne ryzyka są operacyjne, nie algorytmiczne:

- trzy attachmenty plus sesje debuggera mogą przekroczyć limit połączeń serwera; degraded mode powinien być wyraźnie widoczny w UI;
- pola `DataTransactionProfile` i `MetadataTransactionProfile` są nadal utrwalane, lecz `TransactionService.ResolveActiveProfile()` zawsze wymusza `ReadCommitted`; to martwy/niejednoznaczny kontrakt;
- `WAIT` dla DDL jest ograniczone czasowo, co jest dobre, ale komunikat po konflikcie powinien wskazywać obiekt, timeout i aktywny tryb Developer Mode;
- brak konfliktu wersji DDL (A-01) jest ważniejszy niż ryzyko deadlocku.

## Bezpieczeństwo SQL, PSQL, parsera i formattera

**Dobre praktyki obecne dziś:** parametry są używane w ścieżkach danych, identyfikatory importu są cudzysłowowane przez podwojenie znaku `"`, generator DDL ucieka literały, splitter jest PSQL-aware, a code actions mają drift check. Parser/AST pozostawia nierozpoznane fragmenty jako lossless leaves zamiast „naprawiać” je zgadywaniem.

**Granice wymagające wzmocnienia:**

1. Easy Mode jest transformacją, nie wyłącznie widokiem. Przełączenie Source → Easy parsuje model, a Easy → Source buduje nowy tekst i robi to pod `_suppressDirty`. Jeżeli parser zaakceptuje częściowy, lecz semantycznie niepełny przypadek, użytkownik może otrzymać przeformatowaną/uboższą definicję bez standardowego sygnału modyfikacji. Testy round-trip są wartościowe, ale nie są dowodem dla dowolnego kodu klienta.
2. Formatter świadomie zmienia case i strukturę całego dokumentu. To jest akceptowalne jako jawna komenda, ale dla produktu premium powinien mieć preview/diff dla obiektów bazodanowych, undo jako jedną operację i testy różnicowe na realnym korpusie ERP.
3. SQL editor jest z założenia konsolą wykonującą dowolny SQL użytkownika. Premium safety nie oznacza blokowania SQL, tylko jawny kontekst środowiska, tryb tylko-odczyt dla profili, klasyfikację DDL/DML, podgląd skutków oraz szczególnie czytelne oznaczenie PROD.

## Moduł importu danych

Rdzeń importu jest bardzo dobrze przygotowany pod kolejne providery. Silne decyzje to jeden pipeline, brak dwóch ścieżek „validate/import”, surowe wartości aż do konwertera, walidacja charsetu połączenia oraz raportowanie numeru wiersza źródłowego, a nie indeksu paczki.

**Stan faktyczny repozytorium nie odpowiada założeniu „po I7”.** `docs/design/data-import.md`, `DataImportTabView.axaml` i `DataImportTabView.axaml.cs` jednoznacznie opisują command bar, F5, Validate, Import, tryby transakcji, raport i last-used jako zakres I7 dopiero do wykonania. `DataImportTabViewModel` nie wywołuje `ImportPipeline` ani `FirebirdImportWriter`. W raporcie należy więc oceniać I7 jako gotowy projektowo rdzeń, ale nie jako funkcję dostarczoną.

Przed ukończeniem I7 warto dodać jeden kontrakt: dla każdego błędu infrastrukturalnego providera/writera pipeline musi zakończyć writer i zwrócić raport stanu (albo jasno przekazać „nieznany stan, nie commituj”). Obecny `RunAsync` obsługuje anulowanie, ale wyjątek inny niż `OperationCanceledException` może ominąć `CompleteAsync`, przez co użytkownik nie otrzyma jednolitego końcowego wyniku.

UX importu idzie w dobrą stronę: jedna powierzchnia robocza, readiness zamiast nieprzejrzystego kreatora, mapowanie z wyjaśnieniem źródła decyzji i zachowanie konfiguracji przyszłych wersji. I7 powinien utrzymać ten standard: wynik importu musi pokazywać `read / attempted / written / failed / not attempted`, stan transakcji, listę ostrzeżeń i pojedynczy następny bezpieczny krok — `Commit`, `Rollback` lub „nic nie zapisano”.

## UI / UX i Design System

Kierunek jest właściwy dla narzędzia desktopowego: własny chrome, gęste wiersze, 11–12 px dla danych pomocniczych, 12–13 px dla pracy, ikony SVG/geometrie i tokeny motywu. Interfejs jest już mniej „dotykowy” niż standardowy Fluent Avalonia.

Następny etap powinien być systemowy, nie ekranowy:

- utworzyć **Design Tokens** dla gęstości: kompaktowy/standardowy, wysokość wiersza, rozmiar ikon, odstępy, promienie, stany focus/disabled/danger;
- stworzyć katalog komponentów z przykładami: toolbar group, command button, data grid row, tree row, editor tab, banner, dialog destrukcyjny, status transakcji;
- ustanowić dostępność jako wymaganie: kontrast, focus ring, obsługa klawiatury, skalowanie 100/125/150/200%, test 1366×768 i duże DPI;
- nie rozrzucać nowych wartości `Padding`, `FontSize`, `CornerRadius` po XAML; nowe elementy powinny używać tokenów/klas;
- dodać wyraźny „environment chrome”: nazwa profilu, kolor/znacznik PROD i stan lane’ów zawsze widoczne podczas działań destrukcyjnych.

## Skróty klawiaturowe

Obecne skróty są lokalnie sensowne, ale rozproszone pomiędzy `MainWindow.axaml`, poszczególne widoki i ręczne `KeyDown` w kontrolerach edytora. To utrudni wykrywanie kolizji, konfigurację, lokalizację opisów i późniejszy eksport ustawień.

Rekomendowany jest centralny `CommandRegistry`:

- stabilne `CommandId`, domyślny gesture, zakres (`global`, `workspace`, `editor`, `grid`, `dialog`) i predicate dostępności;
- resolver priorytetów: dialog → aktywny edytor/kontrolka → tab → okno;
- jedna lista używana przez menu, tooltip, command palette i konfigurator;
- walidator kolizji i testy, że każde widoczne polecenie ma opis, skrót lub świadomie go nie ma;
- key handling dla mechaniki edytora (Tab, pairing, completion) pozostaje lokalny, lecz rejestruje zarezerwowane gesty.

## Konfigurator, import/eksport ustawień i lokalizacja

Obecny `ApplicationSettings` jest dobrym agregatem i ma migracje, ale konfigurator powinien być oddzielony od mechanizmu persystencji. Proponowana struktura:

| Grupa | Przykładowe ustawienia |
|---|---|
| Środowisko i bezpieczeństwo | etykieta środowiska, kolor PROD, read-only, potwierdzenia DDL/DML, polityka debuggera |
| Połączenia i transakcje | profile, Dev Mode, timeouty, widoczność lane’ów, zachowanie reconnect |
| Edytor | font, tab/indent, line wrapping, format style, completion, diagnostics |
| Format SQL/PSQL | keyword/identifier case, wcięcia, szerokość, łamanie list, reguły per projekt |
| UI | theme, density, font scale, layout, grid profiles |
| Skróty | rejestr komend, własne gesty, reset/import/export |
| Import/eksport danych | domyślne formaty, encoding, polityka błędów, ograniczenia |
| Prywatność i diagnostyka | telemetryka opt-in, ścieżki logów, redakcja sekretów |
| Język | kultura, fallback, format dat/liczb |

DPAPI `CurrentUser` jest dobrym domyślnym sejfem lokalnym, ale złym formatem eksportowym. Eksport ustawień powinien mieć rozdzielne artefakty: niesekretne preferencje w jawnym, wersjonowanym JSON oraz sekrety tylko w opcjonalnym, zaszyfrowanym pakiecie opartym o hasło/klucz użytkownika, z KDF, autentykacją i jawnym ostrzeżeniem. Nigdy nie eksportować hasła „bo użytkownik wybrał eksport ustawień” bez odrębnej zgody.

## Formatter

Dzisiejszy formatter jest użyteczny, lecz architektura powinna przejść z globalnego `SqlFormatter.Format(string)` do `FormattingProfile` jako niemutowalnego wejścia: `KeywordCase`, `IdentifierCase`, `IndentWidth`, `MaxLineWidth`, `CommaStyle`, `JoinStyle`, `PsqlStyle`, `BlankLinePolicy` oraz wariantów IBExpert.

Ważna zasada: formatter nie może oznaczać „nieznanego” tekstu jako bezpiecznego tylko dlatego, że umie go ztokenizować. Dla każdego formatowania potrzebne są: idempotencja, test semantycznej równoważności tam, gdzie parser to potrafi ocenić, i mechanizm odrzucający/pozostawiający bez zmian fragmenty poza obsługiwanym zakresem. Konfiguracja case powinna być oddzielona od decyzji o formatowaniu struktury.

## Testy przed wydaniem

### Krytyczne

- konflikt wersji DDL: dwa edytory i dwie sesje, zmiana po załadowaniu, blokada bez utraty tekstu;
- debugger: autonomiczna transakcja, generator, trigger BEFORE/AFTER, rollback po błędzie, utrata połączenia, anulowanie i jawny commit;
- import: cancel w trakcie batcha, zerwanie połączenia, wyjątek writera/providera, szczegółowa zgodność raportu z bazą, charset nieobsługujący znaku;
- ustawienia: DPAPI niedostępne, uszkodzona kopia, plik z przyszłej wersji, pełny dysk, jednoczesny zapis, odzyskanie backupu;
- DDL parser/splitter: corpus rzeczywistych procedur ERP, pakiety, quoted identifiers, nietypowe `SET TERM`, komentarze i BLOB source.

### Regresyjne

- golden corpus formattera i Easy Mode: Source → Easy → Source, hash/diff i klasyfikacja „nie obsługujemy”;
- wszystkie skróty w każdym kontekście focusu;
- dark/light, 100–200% DPI, 1366×768, długie nazwy i polskie znaki;
- migracje settings i profile importu z kilku wersji;
- permissions Firebird: read-only user, brak DDL, brak monitoringu, limit attachmentów.

### Wydajnościowe

- metadata tree dla 10k+ obiektów; wyszukiwanie i filtr bez blokowania UI;
- edytor dla 1–5 MB PSQL; diagnostics, formatter i completion z budżetami czasu;
- import 1M+ wierszy: pamięć, throughput, cancel latency, raport ograniczony rozmiarem;
- export strumieniowy i duże BLOB-y; obciążenie Data/Metadata/DDL równolegle;
- debugger: duże pętle, wiele frame’ów, duże zestawy watch/breakpoint.

### Bezpieczeństwa

- CI: NuGet vulnerability/deprecation scan, SBOM, secret scan, CodeQL/Roslyn analyzers;
- fuzz/property tests lexer/parser/splitter/formatter oraz CSV reader;
- SQL identifiers/literal escaping z cudzysłowami, Unicode i payloadami injection;
- testy redakcji haseł w logach, exceptionach i eksportach;
- threat model dla importu plików i debuggerowych efektów ubocznych.

### UX

- zadaniowe testy z programistami Firebird/ERP: znalezienie obiektu, DDL conflict, debugowanie triggera, bezpieczny import, rollback;
- time-to-first-query, time-to-find-error, liczba błędnych commitów/wyborów środowiska;
- audyt klawiatury bez myszy i dostępności ekranem/narratorem.

## Pomysły premium zwiększające wartość produktu

1. **Safety Timeline** — per connection lista „co aplikacja zrobiła / co pozostaje niezatwierdzone”: DDL, import, script, debug i commit. Daje zaufanie oraz łatwe przekazanie kontekstu między programistami.
2. **Schema-aware three-way merge** — konflikt DDL pokazuje różnice Base/Local/Database z rozpoznaniem nagłówka, parametrów, deklaracji i body. To ważniejsza przewaga niż kopiowanie ekranów IBExpert.
3. **Production Guardrails** — profile PROD tylko-odczyt, czasowa sesja write, nazwa środowiska wpisywana przy pierwszej operacji DDL, reguły zespołowe do eksportu/importu.
4. **Replayable debug scenarios** — zapis parametrów, kontekstu triggera, breakpointów i oczekiwań bez haseł; umożliwia przekazywanie błędu ERP między zespołami.
5. **Explainable performance workspace** — plan, statystyki, trace, sargability i rekomendacja połączone w jedną historię „dlaczego to jest wolne”, z linkiem do źródła SQL.
6. **Safe migration script review** — klasyfikacja DDL/DML, zależności, transakcje, preview zmian metadanych oraz plan rollbacku jako raport, nie automatyczny magiczny mechanizm.
7. **Command palette i workflow packs** — wyszukiwalne komendy oraz powtarzalne, podpisywalne przepływy dla ERP (release, diagnostyka klienta, anonymizacja kopii).

## Roadmapa rekomendowanych sprintów

| Sprint | Cel i kryterium zakończenia |
|---|---|
| S0 — Release Safety | Usunięte A-01–A-05: gate konfliktów DDL, bezpieczne tryby debuggera, niekasujące ustawienia, aktualne zależności, centralny audit mutacji. |
| S1 — Import I7 | Prawdziwe Validate/Import/Cancel/raport/Commit/Rollback/last-used w UI; recovery dla błędów infrastruktury; testy end-to-end na FB5. |
| S2 — Product Contracts | `CommandRegistry`, `FormattingProfile`, `SettingsHealth`, widoczny stan lane’ów i środowiska. |
| S3 — UX System | Tokeny gęstości, katalog komponentów, DPI/a11y matrix, command palette, spójna informacja o ryzyku. |
| S4 — Reliability Lab | Kontenerowy Firebird 3/4/5 w CI, corpus ERP SQL/PSQL, fuzzing parsera/CSV, performance budgets. |
| S5 — Premium Workflows | Safety Timeline, scenario debugger, conflict/merge DDL, performance workspace. |
| S6 — Configuration & Distribution | Konfigurator, bezpieczny eksport/import, lokalizacja, instalator/aktualizacja, telemetryka opt-in. |
| S7 — Licensing | Wydzielony `Licensing` adapter: podpisana licencja plikowa, offline validation, feature policy bez mieszania z domeną Firebird. |

## Tabela wszystkich znalezionych problemów

| ID | Priorytet | Wpływ | Rekomendacja |
|---|---|---|---|
| A-01 | P0 | Możliwa utrata nowszego kodu obiektu przy stale editor compile | Fingerprint + three-way conflict gate przed DDL. |
| A-02 | P0 | Debugger może pozostawić dane poza rollbackiem | Safe simulation domyślnie blokująca autonomous/generator/external effects. |
| A-03 | P1 | Możliwa utrata całego `settings.dat` | Rozróżnić brak/nieczytelność; blokada zapisu i recovery UI. |
| A-04 | P1 | Rozproszone ścieżki zmian dokumentu utrudniają gwarancję bezpieczeństwa | Jeden kontrakt mutacji, test architektoniczny i preview dla assisted edits. |
| A-05 | P1 | Znana podatność High w zależności Office | Aktualizacja/override `System.IO.Packaging`, SCA jako gate CI. |
| A-06 | P1 | I7 importu nie jest dostarczony mimo założenia audytu | Traktować I7 jako release gate; nie reklamować importu MVP przed integracją UI. |
| A-07 | P2 | Błąd infrastruktury importu może ominąć ujednolicony końcowy raport | Kontrakt `Complete/Abort` w `finally`, stan „unknown — do not commit”. |
| A-08 | P2 | Rosnące VM-y/readery zwiększają ryzyko regresji i koszt zmian | Wydzielać stabilne koordynatory, bez mechanicznej refaktoryzacji. |
| A-09 | P2 | Utrwalane profile transakcji nie są faktycznie stosowane | Usunąć martwy model albo udokumentować i wdrożyć jego semantykę. |
| A-10 | P2 | Skróty są rozproszone; konfigurator będzie kosztowny | `CommandRegistry` z zakresami, priorytetami i walidacją kolizji. |
| A-11 | P2 | Brak kompletnej automatycznej weryfikacji z prawdziwym Firebirdem w CI | Matryca FB3/4/5, uprawnienia, limity attachów i fault injection. |
| A-12 | P2 | Design System istnieje, ale metryki są nadal częściowo literalami XAML | Tokeny density/typography/spacing i katalog komponentów. |
| A-13 | P2 | `ARCHITECTURE.md` zawiera dawne liczby i opis z innego stanu projektu | Wersjonowany „as-built” po każdym releasie; generowane metryki. |
| A-14 | P3 | Test DPAPI nie jest deterministyczny w środowisku bez profilu Windows | Oznaczyć jako Windows integration test / uruchamiać na agencie z profilem. |

## Instalator i licencje

Obecny podział dobrze pozwala dodać instalator bez naruszania domeny: App jest osobnym executable, Core/Firebird nie zakładają instalatora. Przed dystrybucją potrzebne są jednak: podpisywanie binariów, SBOM, aktualizacja z kontrolą rollbacku, migracje ustawień i kanały release.

Licencję plikową podpisaną kryptograficznie należy zrealizować jako mały, izolowany moduł. Aplikacja powinna zawierać wyłącznie klucz publiczny; walidator zwraca capability/entitlements, nie rozsiewa `if (licensed)` po ViewModelach. Licencja nie powinna blokować dostępu do lokalnych danych ani utrudniać rollbacku/eksportu ustawień.

## Pytania otwarte

1. Czy EmberTern ma oficjalnie wspierać połączenia produkcyjne, czy wymaga osobnego trybu/zasad zespołowych?
2. Jakie efekty uboczne debuggera są akceptowalne poza rollbackiem (generatory, autonomous, UDF/external procedures, e-mail/HTTP przez procedury)?
3. Czy kompilacja obiektu ma mieć możliwość force overwrite i kto może ją użyć?
4. Czy import MVP ma być atomowy w jednej transakcji, czy dopuszcza tryb częściowo zatwierdzony; jak komunikować go na poziomie profilu środowiska?
5. Jaki jest minimalny wspierany Firebird i system Windows oraz polityka aktualizacji NuGet/.NET?
6. Czy pliki eksportu ustawień mają przenosić sekrety, czy wyłącznie niesekretne preferencje?
7. Które rzeczywiste korpusy SQL/PSQL ERP można legalnie zanonimizować i użyć jako stały regression corpus?

## AI Handoff Notes

Projekt jest `App → Firebird → Core`, ma bardzo rozbudowane testy i silną dokumentację decyzji. Nie zaczynaj od ogólnej refaktoryzacji. Najpierw zbadaj i napraw A-01 (optimistic concurrency przed DDL), A-02 (debug safety), A-03 (settings recovery) i A-05 (zależność). Stan importu: engine I1–I6 istnieje, ale UI I7 nie: command bar jest tylko zarezerwowany. Zasada nadrzędna: przy niepewności nie zmieniać kodu ani danych użytkownika; preferuj blokadę, diff i jasny następny krok nad heurystykę. Pełny przebieg testów w tym środowisku: 5582/5583, jedyny fail DPAPI bez profilu Windows.
