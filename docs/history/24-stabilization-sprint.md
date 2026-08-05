# Sprint stabilizacyjny (S-1 … S-6) — 2026-08-05

Krótki sprint zamówiony przez użytkownika **przed M4 Product Polish**, na sześć rzeczy znalezionych w
codziennym używaniu aplikacji. Wyraźnie **nie** Product Polish: bez zmian architektury „dla porządku", bez
przebudowy UI, bez rozszerzania zakresu. Etapy **E0 → E6**, każdy zamknięty osobnym commitem, buildem 0/0,
pełną suitą i smoke'em.

Gałąź: `feat/stabilization-sprint` (odgałęziona od `feat/product-polish`, żeby QA użytkownika obejmowało
Product Polish i sprint razem).

---

## ⭐⭐ Wynik metodologiczny, który przeżyje ten sprint: DWA z sześciu zgłoszeń nie były tym, co opisywały

To nie retoryka — to najważniejsza rzecz z tego sprintu, i powtórzenie wniosku z bramy §13.3, tylko z innej
strony. Tam **impresja z patrzenia na zrzut** okazała się hipotezą, która myliła się częściej niż trafiała.
Tutaj **precyzyjnie odtwarzalne zgłoszenie** okazało się prawdziwą KORELACJĄ ze źle wskazaną ZMIENNĄ.

| Zgłoszenie | Co się okazało |
|---|---|
| „Zmiana domeny parametru nie zapisuje się przy kompilacji" (S-1b) | **Groźniejsze**: domena ginęła przy CZYTANIU, a Compile przepisywał parametry na typy bazowe i NISZCZYŁ powiązanie z domeną w bazie. Reguła #11. |
| „Tooltip nie działa na `:VariableName`" (S-6) | **Nie ma błędu dwukropka.** Forma z dwukropkiem rozwiązuje się identycznie jak goła. Nie wiązała się samodzielna instrukcja DSQL w ciele PSQL — a dwukropek jest po prostu tym, co w niej stoi. Zmienną był RODZAJ INSTRUKCJI. |

⭐ Wniosek operacyjny: **zgłoszenie mówi, GDZIE użytkownik to zobaczył, nie CO jest zepsute.** W obu
przypadkach odpowiedź dał pomiar (probe nad prawdziwym `SemanticModel`, `isql` przy żywym FB5), a nie czytanie
kodu — czytanie kodu w obu przypadkach *potwierdziłoby* błędną hipotezę, bo szukałem tam, gdzie wskazywał
objaw.

## ⭐ Trzy wspólne przyczyny — sześć zgłoszeń, nie sześć niezależnych napraw

Użytkownik prosił o ocenę, czy część punktów ma wspólny korzeń. Miała, i to zmieniło plan:

1. **S-1a + S-3 — jeden korzeń.** Zbiór „edytowalnych siatek definicji" był NIEJAWNY: był nim *ktokolwiek
   woła `FieldGridColumns.Build`*, bo tam nadawana była klasa niosąca rolę wysokości edytora. Trzy siatki
   deklarujące kolumny w XAML nigdy tam nie trafiały. Naprawą jest **ujawnienie zbioru i pilnowanie go
   maszynowo**; dwa zachowania (Enter, wysokość) to tylko to, co seam niesie.
2. **S-2 — dwie połowy jednego faktu.** Snapshot metadanych nie umiał powiedzieć „jeszcze nie wiem", więc
   diagnostyka czytała brak jako błąd (burza przy otwarciu) — i nikt nie unieważniał cache'u przy odświeżeniu
   (nieaktualność na stałe). ⚠ Drugiej połowy **nie da się** naprawić bez pierwszej: zrzucanie cache'u bez
   rozstrzygalnej gotowości zamieniłoby każde odświeżenie w tę samą burzę.
3. **S-1b + S-6 — ten sam KSZTAŁT** (nie ten sam kod): warstwa gubi informację, którą miała. Czytnik katalogu
   porzucał `RDB$FIELD_SOURCE`; binder zapytań porzucał referencje lokalnych. Oba naprawione u PRODUCENTA, nie
   u konsumenta.

---

## E0 — baza weryfikacji (Lab + czerwone testy)

Rozszerzenie Labu o kształty, których sprint potrzebuje: `SP_DOM_PARAMS` (parametr wejściowy i wyjściowy na
domenie, zwykły obok nich, domena z DEFAULT), `FN_DOM_ARG` (argument + RETURNS na domenie) i — dopisane
później, w E2 — `SP_DBG_SELINTO`.

⚠ **Parametr z DEFAULT musi być OSTATNI** — Firebird odrzuca listę, w której stoi w środku, błędem -204
*„defaults must be last"*. Zmierzone przy pisaniu skryptu; powód zapisany w `setup.sql`, bo kolejność wygląda
na dowolną.

⚠ **Czerwone testy nie weszły w commit E0**, tylko razem ze swoimi poprawkami — reguła „każdy etap kończy się
zielony" jest ważniejsza niż estetyka „najpierw czerwony commit". Czerwień została **zobaczona** przed każdą
naprawą, a tam gdzie test nie mógł istnieć przed API (S-2), rolę tę wziął **zasadzony defekt**.

⚠⚠ **I pierwszy z tych testów PRZESZEDŁ Z OBECNYM DEFEKTEM.** Guard SQL asertował
`Contains("pp.RDB$FIELD_SOURCE")` — a zapytanie już nazywa tę kolumnę, w predykacie JOIN-a, i to właśnie ten
JOIN jest krokiem, który gubi domenę. „Zapytanie wspomina kolumnę" i „odczyt ją niesie" to różne twierdzenia;
asercja dotyczy teraz **listy SELECT**.

## E1 — S-1b: domena parametru nie ginie przy czytaniu

**Zmierzone na żywym FB5, przed napisaniem czegokolwiek:**

| parametr | `RDB$FIELD_SOURCE` | `pp.RDB$NULL_FLAG` | `f.RDB$NULL_FLAG` |
|---|---|---|---|
| `P_CODE D_CODE` | **`D_CODE`** | NULL | NULL |
| `P_PLAIN INTEGER` | `RDB$1` (anonimowa) | NULL | NULL |
| `A D_NAME` (domena NOT NULL) | `D_NAME` | NULL | **1** |
| `C D_CODE NOT NULL` | `D_CODE` | **1** | NULL |

Katalog **trzyma** nazwę domeny, a dwie flagi NULL to dwa różne fakty. `ReadProcedureParamsAsync`,
`ReadFunctionArgsAsync` i `ProcedureParametersSql` nie czytały `RDB$FIELD_SOURCE` — odtwarzały typ z
`RDB$FIELDS`. Domena ginęła przy CZYTANIU, a edytory składają cały `CREATE OR ALTER` z tego, co czytnik
zwrócił.

**Jeden punkt decyzyjny `TypeTextForField`** obok `FormatType` (dwie połowy pytania „co nazywa typ tego
pola"), **`EmitsNotNull`** (źródło nullowalności idzie za źródłem typu), i **`IsUserDomain` przestaje istnieć
w trzech kopiach** — predykat był już zduplikowany, a trzecia kopia miała powstać właśnie tutaj (#302).

⭐ **Domena dociera do siatki przez ISTNIEJĄCE `LoadType`** — jego gałąź „nieznany token bazowy ⇒ to domena"
robi już obie potrzebne rzeczy, w tym `adoptNotNull: false`, które chroni własne `NOT NULL` parametru (§19.8).
Jedna linia, zero nowego mechanizmu.

**Dowód: `tools/probes/DomainSignatureProbe`, 21/21 PASS na FB5.** Z zasadzonym zachowaniem sprzed poprawki:

```
[FAIL] the CATALOG still records D_CODE after the recompile — RDB$3
```

⚠⚠ **A identyczność bajtowa rekonstrukcji PRZESZŁA pod zasadzeniem**, bo oba odczyty były błędne w ten sam
sposób. Asercja round-tripu jest konieczna i **niewystarczająca** — pytać trzeba katalogu.

⚠ Debugger ma potrzebę ODWROTNĄ (typ bazowy, R2 — wstrzyknięta wartość nie może polec na CHECK domeny), więc
oba czytniki dzielą wyłącznie predykat „czy to domena użytkownika". `DebuggerFidelityProbe` 38/38.

⚠ **To zmienia widoczny tekst DDL** dla każdej procedury i funkcji z parametrami domenowymi — w QA wygląda
jak zmiana zachowania, a jest przywróceniem prawdy.

## E2 — S-6: lokalne PSQL wiążą się w osadzonym DSQL

Pomiar zbił hipotezę o dwukropku (patrz tabela u góry). Faktyczne **dwie** luki:

* `BindExpressionReferences` (binder zapytań) nie miał gałęzi dla tokenu `Parameter` — obsługiwał `AS`, nazwy
  kropkowane i gołe. Teraz deleguje do istniejącego `BindParameterToken`.
* Cele `INTO` singletonowego SELECT-a nie wiązał NIKT: `IsCoreEnd` kończy RDZEŃ zapytania na `INTO`, ale SPAN
  węzła `SelectQuery` obejmuje całość (zmierzone `[60,36]`). Pomijamy więc **KLAUZULE**, nie węzeł. ⛔ Nie
  zwężaniem spanu w parserze — span karmi formatter, a zgubienie `INTO` cofnęłoby całą procedurę do verbatim.

⚠ Bramka zasięgu nie potrzebuje własnego warunku: na top-level nie ma lokalnych w zasięgu, więc `:id` zapisuje
się jako nierozwiązane — czyli dokładnie to, czym jest smart parameter — a `IsInRoutineBody` nadal go nie
flaguje.

### ⚠⚠ I to jest etap, w którym poprawka zmieniła DEBUGGER jako skutek uboczny

Zbiór read/write egzekutora cofa się do „wstrzyknij wszystkie lokalne w zasięgu" **dokładnie wtedy, gdy
analizator zwróci pusto** (#238). Przywrócenie referencji ZWĘŻA więc wstrzykiwanie — zmiana w podsystemie
weryfikowanym live fidelity, przyszła bokiem, z poprawki tooltipa.

⭐⭐ **A „ALL PASS" 38 przypadków nic o tym nie mówiło: żadna z 22 prowadzonych procedur nie zawiera
singletonowego `SELECT … INTO`.** Ten kształt żyje w `SP_ADD_ORDER` i `PKG_ORDERS.ORDER_TOTAL`, po których
probe nie kroczy. **Pomiar potrafi odtworzyć MECHANIZM bez odtworzenia STANU** — dokładnie ta sama lekcja co
przy defekcie drzewa, gdzie oba wcześniejsze pomiary nie miały nic zaznaczonego. Stan dopisany jako
`SP_DBG_SELINTO`, przypadek 39: `sim 1 == real 1`, `sim 'SOME' == real 'SOME'`, z asercją, że przypadek jest
**rozstrzygający** (count ≠ 0 — inaczej zła injekcja dałaby wynik, który też wygląda poprawnie).

⚠ Test `SelectInto_SurfacesNoLocalRefs_SoTheFallbackIsInScopeLocals` twierdził odwrotność i został
przepisany: ta pustka nie była cechą instrukcji, tylko dziurą w binderze. Zapisane w miejscu, że obejście
egzekutora kluczuje na **NIEOBECNOŚCI**, a nieobecność nie jest sygnałem rozstrzygalnym.

⚠ Świadomie **nie tknięte**: kolejność w `BindBareReference` (goła nazwa rozwiązuje się do LOKALNEJ przed
kolumną, gdy jest w zasięgu). Firebird rozstrzyga to na rzecz kolumny, więc temat jest wart wrócenia — ale
osobno, z własnym pomiarem, nie jako skutek uboczny.

## E3 — S-2: diagnostyka milczy, dopóki jej wejście się dogrzewa

Kontrakt sam to mówił: `GetColumns` zwraca pustą listę *„when the object is unknown OR has no columns loaded
yet"* — dwa przeciwne fakty, nierozróżnialne **z definicji**. Kolumny dogrzewają się leniwie, więc przy
otwarciu zakładki snapshot zna wszystkie obiekty i żadnej kolumny.

* `ISqlMetadataProvider.KnowsColumns` — **domyślnie `true`**, więc 18 implementacji (16 to atrapy) zachowuje
  się identycznie, a dostawca musi ZGŁOSIĆ SIĘ do przyznania niewiedzy. Domyślne `false` uciszyłoby prawdziwe
  ET0002 wszędzie.
* `AppMetadataSnapshot` odpowiada uczciwie: **słownik** cache'u rozróżnia brak klucza od wpisu pustego.
  Informacja istniała i była wyrzucana o warstwę za wcześnie. ⚠ Wpis PUSTY = ZNANY.
* ⚠ **CTE zwolnione** — jego kolumny pochodzą z własnej projekcji w tekście, więc `ColumnsComplete` JEST jego
  odpowiedzią o gotowość; pytanie snapshotu o nazwę, która nie jest obiektem katalogu, uciszyłoby każdą
  literówkę w CTE na zawsze.
* ⭐ **Parametry przed ciałem** w edytorze procedury i funkcji: w Easy Mode parametry docierają do modelu jako
  ambient symbols, więc ustawienie tekstu ciała jako pierwsze budowało model, w którym żaden `:param` nie mógł
  się rozwiązać. Teraz PIERWSZY model ma już swoje symbole. ⚠ Trigger był poprawny od zawsze.
* ⭐⭐ `SchemaInvalidated` + jeden subskrybent `InvalidateObjectCaches`. Sygnał leci **PRZED** przeładowaniem,
  bo `ObjectsChanged` lecą w trakcie i każdy planuje przebudowę.

⚠ Guard wiringu czyta ŹRÓDŁO, bo defektem jest BRAKUJĄCA SUBSKRYPCJA — nie ma zachowania do sprawdzenia bez
żywego połączenia i kilku otwartych edytorów, czyli kształtu, który w headless konstruuje `MainWindow` i
zawiesza suite. Obie połowy asercjonowane: zdarzenie, którego nikt nie podnosi, i zdarzenie, którego nikt nie
obsługuje, wyglądają z drugiej strony identycznie.

## E4 — S-1a + S-3: jeden seam edytowalnej siatki

`Behaviors/EditableGridBehavior` niesie **gest Enter** i **rolę wysokości edytora**; dziewięć jawnych wywołań
`Attach`, zero automatycznej ścieżki.

⭐ Ratyfikowana reguła UX: **Enter robi to, co kliknięcie w tę komórkę** — jedna reguła dla siatek definicji i
danych (użytkownik odrzucił dwa zachowania), tylko dla komórek edytowalnych.

Zmierzone fakty frameworka (Avalonia 12.0.0, headless): Enter należy do samego `DataGrid`; potrzebny jest
**TUNEL** (w bąbelku jest już obsłużony); **nie ma publicznego „czy edytuję"**, więc bramką jest FOKUS;
`BeginEdit()` sam ustawia fokus; `DataGridCell` nie ma publicznej `Column`, więc komórka lokalizowana jest
przez `SelectedItem` + `DisplayIndex`. Zasadzenie `Bubble` zamiast `Tunnel` wywala 5 z 7 testów.

⚠ Siatka DANYCH dostaje Enter, ale **nie** rolę wysokości — 24 px minimum urosłoby każdy wiersz w chwili
wejścia w edycję (regresja z kroku 7 M2b).

⚠⚠ **Guard nie może kluczować na `IsReadOnly="False"`**: siatka pól Table Detail pisze
`IsReadOnly="{Binding …}"`, a domyślną wartością i tak jest false — skan po tym atrybucie POMIJA dokładnie tę
siatkę, którą zgłoszono (#285). Guard działa odwrotnie i na pierwszym uruchomieniu wskazał 7 siatek, których
nie sklasyfikowałem; wszystkie okazały się `IsReadOnly="True"`, więc literał jest teraz czytany z markupu.

⚠ Jedna asercja testu była **błędna, nie kod**: po Enter wewnątrz osadzonego edytora fokus schodzi dalej, bo
pole nie przyjmuje Return — i tak ma być. Test pinuje teraz, że seam się w to nie miesza.

## E5 — S-4 + S-5

**S-4.** W module był DOKŁADNIE JEDEN `ProgressBar`; drugim był globalny pasek w Status Barze. ⚠⚠ Usunięcie
**odwraca §19.33** („pasek statusu uzupełnia, nigdy nie zastępuje") — decyzja użytkownika po używaniu. Zostaje
tekst postępu i licznik czasu (etykieta w pasku statusu jest ustalona na 120 px, więc szczegół należy do
powierzchni prowadzącej operację). ⚠ Istniejący guard §19.33 sam złapał zmianę i decyzja jest w nim
**przepisana**, nie obejmowana wyjątkiem.

**S-5.** `ClientLibraryPath` usunięte w komplecie. `FbServerType.Default` to czysty managed wire protocol —
żaden `fbclient.dll` nie jest ładowany — a driver czyta `ClientLibrary` tylko w Embedded, którego produkt
nigdy nie wybiera. Pole nie było ignorowane przez przypadek; zapraszało do decyzji bez skutku. ⭐ Ekspander
„Advanced" znika razem z nim, bo to była jego JEDYNA treść. ⚠ Padły też dwie osierocone składowe, których
`TreatWarningsAsErrors` nie łapie (publiczne właściwości). ⚠ `CurrentSchemaVersion` NIE rusza — JSON ignoruje
nieznane składowe, a bump uruchomiłby ochronę przed downgrade.

⭐ Reflection-guard eksportu użył `nameof`, więc usunięcie było **błędem kompilacji** w każdym miejscu, które
trzeba było odwiedzić — najlepszy możliwy tryb awarii.

⚠ Guard „Embedded nie jest nigdzie wybierane" oblał się na pierwszym uruchomieniu **na moim własnym
komentarzu**, który nazywa tryb, którego nieobecność dokumentuje. Predykatem jest teraz PRZYPISANIE.

---

## Stan na koniec sprintu

Build 0/0. Partycje **7232 + 74 + 54**. Smoke czysty. Probe'y: `DomainSignatureProbe` 21/21,
`DebuggerFidelityProbe` 39/39 (nowy przypadek 39).

⚠ **Dwie jednorazowe czerwienie, nieodtworzone i nieuznane za naprawione ani za „niezwiązane":**
`DataImportNewTableTests.ANewTable_NeverCarriesEmptyTheTableFirst…` (raz, w E3) i
`SettingsLoadHealthTests.ConcurrentSaves_NeverLeaveSettingsUnreadable` (raz, w E4 — test z `Parallel.For`).
Każda przeszła samotnie i w dwóch kolejnych pełnych przebiegach; nie mam mechanizmu łączącego je z tymi
etapami. Zapisane jako obserwacja, zgodnie z zasadą „nie gonić nieodtwarzalnego".

## Co zostaje otwarte (każde z powodem)

* **Kolejność w `BindBareReference`** — goła nazwa w zakresie zapytania rozwiązuje się do LOKALNEJ przed
  kolumną; Firebird rozstrzyga na rzecz kolumny. Wart osobnego pomiaru, nie skutku ubocznego.
* **Rozjazd wysokości WIERSZY siatek** (Table Fields 34 · Table Data 32 · Procedure/Function/Trigger 30 ·
  Indeksy/Ograniczenia 22) — decyzja użytkownika: to pytanie o GĘSTOŚĆ, więc należy do M4/§13.3, nie do
  sprintu stabilizacyjnego. Naprawiona została wyłącznie wysokość EDYTORA w komórce.
* **`ET0001` przy niepełnym katalogu** — kategorie ładują się progresywnie, więc obiekt z jeszcze
  niewczytanej kategorii jest „nieznany". Tego sprint nie ruszał (zgłoszenie dotyczyło kolumn i ambientu);
  jeżeli wyjdzie w praktyce, ma ten sam kształt co #317 i rozwiązanie „snapshot umie powiedzieć, czego nie
  wie".
* **Szerokość pasków komend importu** (trzy listy po 170/180 px) — gęstość, czyli M4.

⏸ **M4 Product Polish pozostaje nierozpoczęte i wymaga osobnej zgody użytkownika.**
