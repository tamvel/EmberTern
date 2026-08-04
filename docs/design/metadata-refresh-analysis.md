# Mechanizm metadanych — analiza, pomiary i rekomendacja

> **📍 STATUS (2026-07-27, po wykonaniu): WARSTWA 1 ZROBIONA, OBJAW 1 NAPRAWIONY. Warstwa 2 i Warstwa 3
> pozostają na osobny etap infrastrukturalny po zamknięciu Data Import.** Co dokładnie weszło, z nowymi
> pomiarami i z jedną decyzją użytkownika, która odwróciła rekomendację §5 — **patrz §7 na końcu**.
> Sekcje 0–6 zostawiono jak były: to jest zapis analizy sprzed zmiany i punkt odniesienia dla pomiarów.

**Zlecone przez użytkownika 2026-07-27, przed etapem I9. Dokument ANALITYCZNY — zero zmian w implementacji.**
Powstał, bo objawy zgłoszone przy okazji Data Import wyglądały na problem szerszy niż jeden moduł, a
użytkownik wprost odrzucił „doklejanie kolejnego `Refresh()`" na rzecz poprawy u źródła.

Wszystkie liczby pochodzą z pomiaru, nie z lektury kodu — narzędzie: `tools/probes/MetadataPerfProbe`.

---

## 0. Wniosek w trzech zdaniach

**Baza nie jest wąskim gardłem.** Odczyt całego katalogu schematu o rozmiarze produkcyjnym (2 400 tabel)
kosztuje **~172 ms** — i dzieje się poza wątkiem UI. Kosztem, który użytkownik odczuwa jako zawieszenie, jest
**projekcja drzewa na płaską listę sidebara: ~1 142 ms na wątku UI przy jednej rozwiniętej kategorii**,
ponieważ podmiana liści kategorii jest operacją **kwadratową** względem liczby obiektów. Ten sam kod zawiera
już gotowe zabezpieczenie przed tym zjawiskiem (`BeginUpdate`/`EndUpdate`) — założone wyłącznie na ścieżce
filtrowania, a nie na ścieżce ładowania.

---

## 1. Odpowiedzi na zadane pytania

### 1.1. Jak wygląda przepływ budowania i odświeżania drzewa

```
POŁĄCZENIE
  ConnectionNodeViewModel.OnIsConnectedChanged
    └─ _ = LoadCategoriesAsync()                     ← fire-and-forget, nie blokuje połączenia
         ├─ tworzy 13 węzłów kategorii (CategoryOrder)
         └─ dla KAŻDEJ z 13 kategorii, SEKWENCYJNIE:
              await MetadataExplorerViewModel.LoadGroupAsync(kategoria)
                ├─ FirebirdMetadataReader.ListAsync(kind)      ← 1 zapytanie, PEŁNA lista
                ├─ group.SetLeaves(...)                        ← ⚠ tworzy N VM-ów, N× Children.Add
                ├─ group.MarkLoaded()                          ← od tej chwili IsLoaded = true
                └─ RaiseObjectsChanged()                       ← sygnał do edytorów (koalescowany)
         └─ NotifyMetadataReady()                              ← pełna przebudowa modelu semantycznego

ODŚWIEŻENIE (ręczne albo po DDL)
  MetadataExplorerViewModel.RefreshAsync()
    └─ dla KAŻDEJ kategorii, SEKWENCYJNIE:
         IsLoaded || IsExpanded ?  LoadGroupAsync(...)   ← pełna lista + pełna podmiana liści
                                :  LoadCountAsync(...)   ← samo COUNT(*)
    ├─ InvalidateNameCache()
    └─ ApplyFilterAsync()                                ← pełna reprojekcja (Rows.Clear + wstaw wszystko)
```

⚠ **Kluczowa obserwacja o gałęzi `IsLoaded || IsExpanded`.** Komentarz przy `RefreshAsync` opisuje model
leniwy („kategoria jest albo ZAŁADOWANA, albo pokazuje tylko licznik"), ale prefetch przy połączeniu ładuje
**wszystkie** kategorie i każda dostaje `MarkLoaded()`. W praktyce więc **gałąź `LoadCountAsync` nigdy nie
jest wykonywana**, a każde odświeżenie to **pełny odczyt całego katalogu**. Dwa miejsca opisują dwa różne
modele; wygrywa prefetch.

### 1.2. Czy za każdym razem wykonywany jest pełny odczyt całego katalogu

**Tak.** Zarówno przy połączeniu, jak i przy każdym odświeżeniu: 13 zapytań, każde zwracające pełną listę
nazw swojej kategorii. Nie ma pojęcia „co się zmieniło" — jest wyłącznie „przeczytaj wszystko jeszcze raz".

**Wywołań `Metadata.RefreshAsync()` jest w aplikacji 20.** Każda operacja DDL (compile, drop, rename,
recompile grupy, commit transakcji, która ruszyła schemat) uruchamia pełną przebudowę. To jest dokładnie ten
wzorzec, o którym użytkownik napisał, że nie chce go dalej powielać — i miał rację, bo dodanie 21. wywołania
dla Data Import rozwiązałoby objaw 1 kosztem pogłębienia przyczyny objawu 2.

### 1.3. Co dzieje się na wątku UI

| Operacja | Wątek | Uwaga |
|---|---|---|
| `ListAsync` / `CountAsync` — I/O do bazy | **pula wątków** | prawdziwie asynchroniczne, nie blokuje |
| kontynuacje po `await` | **UI** | wszędzie `ConfigureAwait(true)` — świadome, VM-y muszą być na UI |
| tworzenie N `MetadataNodeViewModel` | **UI** | ~2 400 obiektów na kategorię; koszt pomijalny |
| ⚠ **`SetLeaves` → `Children.Clear()` + N× `Add`** | **UI** | ⭐ **to jest koszt — patrz §2** |
| ⚠ **reprojekcja `SidebarFlatController`** | **UI** | wywoływana **raz na każdy `Add`** |
| `ApplyFilterAsync` → `EndUpdate` → `Rebuild()` | **UI** | pełny `Rows.Clear()` + ponowne wstawienie |

### 1.4. Czy istnieje cache i kiedy jest unieważniany

Cache **obiektowego** katalogu **nie istnieje**. Są trzy rzeczy, które go przypominają, ale nim nie są:

| Mechanizm | Co trzyma | Unieważnienie |
|---|---|---|
| `MetadataNodeViewModel.IsLoaded` + `_allLeaves` | listę liści kategorii | całkowita podmiana przy każdym `LoadGroupAsync` |
| `MetadataExplorerViewModel._nameCache` | indeks nazw dla filtra / type-ahead | `InvalidateNameCache()` przy każdym `RefreshAsync` |
| `_objectsGeneration` | licznik pokoleń dla edytorów | inkrementowany przy każdym załadowaniu kategorii |

Czyli: dane są trzymane, ale **jedyną operacją unieważnienia jest „wyrzuć wszystko i przeczytaj od nowa"**.
Nie ma protokołu „ten jeden obiekt jest nieaktualny".

### 1.5. Czy da się odświeżać tylko zmieniony fragment

**Tak — i w kodzie istnieje już działający precedens.**
`MetadataExplorerViewModel.ApplyTriggerActiveStateInPlace` (linie 217–248) aktualizuje stan aktywności
triggerów **w miejscu**, bez `RefreshAsync`. Jego komentarz nazywa dokładnie te korzyści, o które chodzi:

> *„No collection change → no reproject, so the sidebar keeps its scroll position, selection, and expanded
> groups (the whole point: single/batch trigger ops no longer make the tree jump)."*

Ktoś już raz zdiagnozował ten problem i rozwiązał go dla jednego przypadku. Brakuje **uogólnienia**: pojęcia
„dodano / usunięto / zmieniono obiekt X rodzaju Y" jako pierwszorzędnej rzeczy, którą operacja DDL zgłasza,
a drzewo stosuje punktowo.

### 1.6. Główny koszt startu i ręcznego odświeżania

**Ręczne odświeżanie: zmierzone i rozstrzygnięte — to projekcja, nie baza.** Szczegóły w §2.

**Start aplikacji: nie rozstrzygnięty, i mówię to wprost.** Zmierzyłem dwa podejrzane składniki i **żaden nie
tłumaczy widocznej fazy budowania**: katalog to ~172 ms poza wątkiem UI, a projekcja przy **zwiniętych**
kategoriach to **5 ms** (reprojekcja ma wczesne wyjście, gdy właściciel nie jest rozwinięty — patrz §2).
Zostają kandydaci, których nie zmierzyłem: przebudowa modelu semantycznego + „warm" w otwartych edytorach po
`NotifyMetadataReady`, oraz odtwarzanie zakładek przestrzeni roboczej. **Rekomendowany następny krok jest
tani i już wbudowany** — patrz §5.

---

## 2. ⭐ Pomiar rozstrzygający: projekcja jest kwadratowa

### Mechanizm

`MetadataNodeViewModel.SetLeaves` podmienia liście przez `Children.Clear()`, a potem **`Children.Add(leaf)`
po jednym**. `Children` to `ObservableCollection`, więc każdy `Add` emituje `CollectionChanged`, który łapie
`SidebarFlatController.OnChildrenChanged` — a ten, **gdy kategoria jest rozwinięta**, robi:

```csharp
int i = IndexOfNode(owner);        // liniowe przeszukanie CAŁEJ listy Rows
RemoveDescendants(i, row.Depth);   // usuwa WSZYSTKIE dotychczasowe liście, po jednym
foreach (var child in ChildrenOf(owner))
    at = InsertNode(at, child, ...);   // wstawia WSZYSTKIE z powrotem, po jednym
```

Czyli przy N obiektach: N zdarzeń × pełne przepięcie bloku N liści = **Θ(N²) operacji na liście i Θ(N²)
powiadomień do kontrolki ListBox** — wszystko na wątku UI.

### Liczby (pomiar prawdziwym `SidebarFlatController`, bez Avalonii)

| liści | dziś (ms) | operacji na wierszach | powiadomień | pod istniejącą blokadą `BeginUpdate` (ms) |
|---:|---:|---:|---:|---:|
| 100 | 3,1 | 10 100 | 10 000 | 0,2 |
| 250 | 11,0 | 62 750 | 62 500 | 0,1 |
| 500 | 41,3 | 250 500 | 250 000 | 0,1 |
| 1 000 | 213,1 | 1 001 000 | 1 000 000 | 0,4 |
| **2 400** | **878,5** | **5 762 400** | **5 760 000** | **0,4** |

Kwadratowość widać wprost: 4× więcej obiektów → ~16× dłużej.

### Pełne odświeżenie, tak jak je odczuwa użytkownik

| rozwiniętych kategorii | koszt projekcji jednego `RefreshAsync` |
|---:|---:|
| 0 | **5 ms** |
| 1 | **1 142 ms** |
| 2 | **1 433 ms** |

**To jest zgłoszone „przywieszenie".** Występuje tylko przy rozwiniętej kategorii, co dokładnie zgadza się z
objawem: użytkownik ma otwarte Tabele, usuwa tabelę → `RefreshAsync` → ponad sekunda na wątku UI.

⭐ **Zabezpieczenie już istnieje w tym samym pliku.** `BeginUpdate`/`EndUpdate` wstrzymuje reprojekcję na czas
masowej zmiany i przelicza raz. Jego komentarz nazywa problem po imieniu — *„an O(n²) storm"* — ale założono
je **wyłącznie** wokół filtrowania (`MetadataExplorerViewModel:598`). Ścieżka ładowania go nie używa.

---

## 3. Pomiar drugi: katalog

Schemat testowy: 2 400 tabel, 200 widoków, 400 procedur, 1 200 triggerów (FB5 `WI-V5.0.3.1683`).

| kategoria | obiektów | `COUNT(*)` ms | pełna lista ms |
|---|---:|---:|---:|
| Table | 2 400 | 3,7 | 32,0 |
| View | 200 | 3,0 | 6,0 |
| Procedure | 400 | 2,3 | 4,8 |
| Trigger | 1 200 | 2,2 | 13,8 |
| Domain | 0 | 7,2 | **79,1** |
| User | 5 | **48,3** | 21,3 |
| Index | 0 | 3,0 | 18,9 |
| pozostałe (6) | 0–56 | ~1,6–2,9 | ~1,4–4,5 |
| **RAZEM (13)** | | **~80 ms** | **~172 ms** |

Jedno okrążenie do katalogu kosztuje **~2,7 ms** — to podłoga dla dowolnego odświeżenia punktowego.

**Wnioski.**
- Pełny odczyt katalogu jest **tani** i rośnie liniowo. Stosunek „tylko liczniki" do „pełne listy" to zaledwie
  **2,2×**, więc rezygnacja z prefetchu sama w sobie niewiele by dała.
- Dwie kategorie odstają i są warte uwagi niezależnie: **Domain** (79 ms — `RDB$FIELDS` zawiera anonimową
  domenę podkładową dla **każdej kolumny w bazie**, więc rośnie z liczbą kolumn, nie domen) i **User**
  (48 ms — `SEC$USERS` to wywołanie do bazy bezpieczeństwa).

⚠ **Granica tego pomiaru, powiedziana wprost:** schemat testowy ma mało indeksów, generatorów i domen. Realna
baza ERP ma ich tysiące, więc **172 ms to podłoga, nie prawda o Państwa bazie**. Kształt wniosku (katalog
liniowy i tani, projekcja kwadratowa i droga) się nie zmienia, bo dzieli je ponad rząd wielkości.

---

## 4. Dlaczego nowa tabela się nie pojawia (objaw 1)

Nie ma tu żadnej zagadki i **nie jest to problem wydajnościowy**: Data Import wykonuje `CREATE TABLE` na linii
Ddl i **nigdy nie zawiadamia drzewa**. Pozostałe ścieżki DDL robią to jawnie (20 wywołań `RefreshAsync`), a
SQL Editor przez zaczep na zatwierdzeniu transakcji (`if (settled && schemaChanged)`).

**Nie rekomenduję dopisania 21. wywołania.** Byłoby to poprawne w minutę i pogłębiłoby przyczynę objawu 2:
utworzenie jednej tabeli kosztowałoby ponad sekundę zamrożonego UI i skok drzewa. Właściwe rozwiązanie jest w
§5, Warstwa 2, i przy okazji jest **tańsze** niż pełne odświeżenie.

---

## 5. Rekomendacja

Trzy warstwy, celowo rozdzielone: pierwsza to naprawa błędu, druga to brakujące pojęcie architektoniczne,
trzecia to higiena. **Każda ma wartość osobno** — nie trzeba brać ich w komplecie.

### Warstwa 1 — założyć istniejącą blokadę na ścieżkę ładowania *(mały zakres, największy zysk)*

Podmiana liści to masowa mutacja i powinna być objęta tym samym `BeginUpdate`/`EndUpdate`, którym objęte jest
filtrowanie. **Zmierzony efekt: 1 142 ms → ~5 ms.**

- To **nie jest zmiana architektury** — to zastosowanie istniejącego zabezpieczenia w drugim miejscu.
- Ryzyko niskie i znane: `EndUpdate` robi pełną reprojekcję, więc lista przewinie się na górę. Dlatego
  Warstwa 1 **nie zastępuje** Warstwy 2 — usuwa zawieszenie, nie usuwa „skakania" drzewa.
- Naturalne domknięcie: `SetLeaves` mógłby wymieniać zawartość jednym powiadomieniem zamiast N.

### Warstwa 2 — ⭐ wprowadzić „co się zmieniło" jako pierwszorzędne pojęcie *(to jest naprawa u źródła)*

Dziś aplikacja umie powiedzieć drzewu wyłącznie *„coś się stało, przeczytaj wszystko"*. Brakuje pojęcia
zmiany: rodzaj + nazwa + `Added` / `Removed` / `Altered`. Operacja DDL zgłasza taką zmianę, a eksplorator
**stosuje ją punktowo**: wstawia jeden liść w posortowane miejsce, usuwa jeden, poprawia licznik kategorii.

- **Precedens już działa** — `ApplyTriggerActiveStateInPlace` robi dokładnie to dla stanu aktywności triggerów
  i jego komentarz wymienia zyski: zachowane przewinięcie, zaznaczenie i rozwinięcie. Warstwa 2 to
  uogólnienie tego, co ktoś już raz odkrył.
- **Koszt:** jedno wstawienie do listy zamiast 5,76 mln operacji. Bez okrążenia do bazy, bo operacja DDL **wie**,
  co zrobiła.
- **To rozwiązuje objaw 1 właściwie:** Data Import zgłasza „dodano tabelę X" zamiast wywoływać `Refresh()`.
- **Pełne odświeżenie zostaje** — jako jawne polecenie użytkownika i awaryjne wyjście, gdy zmiana nie jest
  znana (skrypt, zewnętrzna modyfikacja bazy). Przestaje być domyślną reakcją na wszystko.
- ⚠ **Uczciwa granica:** ten model zakłada, że aplikacja wie, co zrobiła. Dla Script Executora, `EXECUTE
  STATEMENT` i zmian z zewnątrz nadal potrzebne jest pełne odświeżenie — i to jest w porządku, bo po Warstwie 1
  będzie ono tanie.

### Warstwa 3 — higiena, do rozważenia niezależnie

1. **Uzgodnić prefetch z `RefreshAsync`.** Dziś jedno miejsce ładuje wszystko, a drugie opisuje model leniwy,
   którego gałąź `LoadCountAsync` jest martwa. Niezależnie od wybranego modelu — powinien być jeden.
2. **Kategoria Domain** kosztuje 79 ms, bo `RDB$FIELDS` zawiera anonimową domenę na każdą kolumnę bazy.
   Warunek `NOT STARTING WITH 'RDB$'` jest już po stronie serwera; to koszt skanu, nie transferu. Wart
   sprawdzenia na realnej bazie — może okazać się największą pojedynczą pozycją.
3. **Kategoria User** (48 ms) odpytuje bazę bezpieczeństwa. Można ją ładować leniwie, bo prawie nikt jej nie
   rozwija.

### Kolejność, którą rekomenduję

**Warstwa 1 → pomiar na realnej bazie (§6) → Warstwa 2.** Warstwa 1 jest tania i zdejmuje ból natychmiast;
pomiar na realnej bazie ustali resztę kosztu startu, zanim zapadną decyzje projektowe; Warstwa 2 jest właściwą
naprawą i zasługuje na własny etap z własnym projektem, a nie na doklejenie do I9.

**Nie rekomenduję** dopisywania odświeżenia po imporcie jako osobnej poprawki. Objaw 1 poczeka na Warstwę 2 —
jest kosmetyczny (ręczne odświeżenie działa), a naprawiony punktowo utrwaliłby wzorzec, który jest przyczyną.

---

## 6. Czego NIE zmierzyłem i jak to domknąć

**Koszt startu na realnej bazie pozostaje nieustalony.** Nie zgaduję — aplikacja ma już wbudowany instrument,
wystarczy go włączyć:

```bash
set EMBERTERN_PERF_DIAG=1
```

Po uruchomieniu i połączeniu w `%TEMP%\EmberTern-debug.log` pojawią się linie:

- `PERF [category-load] … countFetchMs=… managedHeapKB=…` — całkowity czas prefetchu wszystkich kategorii,
- `PERF [group-load] kind=… leaves=… loadMs=…` — czas na kategorię.

Jeżeli suma `group-load` jest zbliżona do zmierzonych ~172 ms, koszt startu leży **poza** mechanizmem
metadanych (najpewniej w przebudowie modelu semantycznego edytorów po `NotifyMetadataReady`) i tam należy
szukać dalej. Jeżeli jest wielokrotnie większa — decyduje rozmiar katalogu i wtedy sensu nabiera Warstwa 3.

**Narzędzie pomiarowe:** `tools/probes/MetadataPerfProbe` (poza rozwiązaniem, jak pozostałe sondy). Buduje
własną bazę scratch na ścieżce ASCII (#149), nigdy nie dotyka bazy laboratoryjnej. Część B nie wymaga serwera.

---

## 7. Co zostało wykonane (2026-07-27)

Zakres sesji był węższy niż cały raport i został wyznaczony przez użytkownika: **Warstwa 1 + naprawa
objawu 1 + ponowny pomiar**. Warstwa 2 (pojęcie „co się zmieniło" jako pierwszorzędne) i Warstwa 3
(higiena) **nie zostały wykonane** i czekają na osobny etap infrastrukturalny po zamknięciu Data Import.

### 7.1. ⭐ Decyzja użytkownika, która odwróciła rekomendację §5

Raport rekomendował, żeby objaw 1 (nowa tabela nie pojawia się w drzewie) **poczekał na Warstwę 2**, bo
jest kosmetyczny. Użytkownik tę rekomendację **odrzucił**: to zwykły błąd UX i ma być naprawiony teraz —
ale **bez** dopisywania 21. wywołania `RefreshAsync()`.

Obie połowy tego polecenia dało się spełnić naraz, bo moduł importu **zna nazwę tabeli, którą właśnie
utworzył**. Zamiast kazać drzewu przeczytać wszystko od nowa, mówi mu, co się stało — i drzewo wstawia
jeden liść w posortowane miejsce. To jest **wąski, jednoobiektowy precedens** dokładnie tej samej myśli,
którą Warstwa 2 ma uogólnić, a nie sama Warstwa 2: nie ma tu żadnego wspólnego pojęcia zmiany, żadnego
protokołu, żadnej zmiany w pozostałych 20 ścieżkach DDL. Te zostają na etap infrastrukturalny.

### 7.2. Co się zmieniło w kodzie

| Miejsce | Zmiana |
|---|---|
| `MetadataExplorerViewModel.BeginSidebarBulkUpdate` / `EndSidebarBulkUpdate` | Istniejąca blokada `SidebarFlatController.BeginUpdate/EndUpdate` udostępniona poza ścieżkę filtrowania. |
| `MetadataExplorerViewModel.LoadGroupAsync` | Podmiana liści + ponowne nałożenie filtra objęte blokadą. To jest **cała Warstwa 1** — jedno miejsce, przez które przechodzi każde ładowanie kategorii. |
| `MetadataExplorerViewModel.RefreshAsync` | Cała pętla 13 kategorii objęta blokadą (zagnieżdżanie jest bezpieczne), więc jedno odświeżenie = jedna reprojekcja zamiast trzynastu. |
| `ConnectionNodeViewModel.LoadCategoriesAsync` | To samo dla prefetchu przy połączeniu. |
| `MetadataNodeViewModel.InsertLeafInPlace` / `RemoveLeafInPlace` / `HasLeaf` | Wstawienie/usunięcie **jednego** liścia w posortowanym miejscu, z poszanowaniem aktywnego filtra. |
| `MetadataExplorerViewModel.ApplyObjectAddedInPlace` / `ApplyObjectRemovedInPlace` | Punktowa aktualizacja drzewa: liść, licznik `(N)`, licznik dopasowań filtra, indeks nazw i sygnał `ObjectsChanged` dla edytorów. |
| `DataImportEnvironment.TableCreated` / `TableDropped` | Moduł importu **zgłasza fakt** („ta tabela istnieje"), nie wydaje polecenia („odśwież się"). |

⚠ **Świadomy koszt, dokładnie ten, który §5 zapowiadał:** `EndUpdate` robi pełną reprojekcję, więc obiekty
wierszy są tworzone od nowa i lista wraca na górę. Nie jest to nowe zachowanie przy odświeżaniu — `RefreshAsync`
i tak kończyło się `ApplyFilterAsync`, czyli pełną reprojekcją — a przy połączeniu nie ma czego zachowywać.
Ujawnił to jeden istniejący test (`ConnectionExpandBindingProbe.AutoExpandOnConnect_ReflectedInFlatList`),
który trzymał **referencję** do wiersza sprzed połączenia; test pobiera teraz wiersz ponownie, bo bada
odwzorowanie stanu, a nie tożsamość obiektu. **Skakanie drzewa usuwa dopiero Warstwa 2.**

### 7.3. Pomiar po zmianie (`tools/probes/MetadataPerfProbe`, ta sama maszyna, ten sam schemat)

**Projekcja pełnego odświeżenia — to jest liczba, o którą chodziło:**

| rozwiniętych kategorii | PRZED (ms) | PO (ms) |
|---:|---:|---:|
| 0 | 6 | **2** |
| 1 | **1 424** | **2** |
| 2 | **1 733** | **4** |

Sekundowe zawieszenie UI przy odświeżaniu z rozwiniętą kategorią **przestało istnieć**. (Poprzedni pomiar
dla jednej kategorii dał 1 142 ms, dziś 1 424 ms — ta sama wielkość, zwykły rozrzut obciążonej maszyny;
istotne jest, że wynik po zmianie to jednostki milisekund niezależnie od liczby rozwiniętych kategorii.)

**Jeden dodany obiekt, dwie drogi:**

|  | koszt projekcji | odczyt katalogu |
|---|---:|---|
| pełne `RefreshAsync` (już pod blokadą) | 1,8 ms na kategorię | **13 zapytań, ~164 ms** |
| wstawienie liścia w miejscu (to, co weszło) | **1,3 ms** | **żadnego** |

**Katalog, pomiar powtórzony:** 13 kategorii, `COUNT`-only 58 ms · pełne listy **164 ms** (poprzednio 172 ms),
jedno okrążenie ~3,0 ms. Bez zmian — bo w katalogu nic nie zmieniano.

⚠ **Uczciwa granica tej naprawy:** wstawienie jednego liścia to jedno zdarzenie kolekcji, ale
`SidebarFlatController` na każdą zmianę dzieci przepina **cały** blok liści właściciela — pomiar pokazał
4 803 powiadomienia na jedno wstawienie do kategorii z 2 400 obiektami. To jest **liniowe** (dawniej byłoby
5,76 mln), więc nie boli, ale prawdziwie przyrostowe wstawienie jednego wiersza należy do Warstwy 2 razem
z zachowaniem przewinięcia.

### 7.4. Czy start aplikacji się poprawił — **nie, i to jest zmierzone, nie zgadnięte**

Nie poprawił się w sposób odczuwalny i nie mógł: **przy połączeniu wszystkie kategorie są zwinięte**
(`RestoreExpandState` przywraca rozwinięcie folderów i połączeń, **nie kategorii**), a dla zwiniętej
kategorii reprojekcja ma wczesne wyjście — wiersz „0 rozwiniętych kategorii" w tabeli wyżej to 6 ms → 2 ms.
Warstwa 1 zdejmuje ból **odświeżania**, nie **startu**.

**Koszt startu pozostaje nierozstrzygnięty** — dokładnie tak, jak mówi §6, i tam jest instrument
(`EMBERTERN_PERF_DIAG=1`). Zostaje jako zadanie do przyszłego sprintu Metadata Explorer; główni kandydaci
to przebudowa modelu semantycznego edytorów po `NotifyMetadataReady` i odtwarzanie zakładek przestrzeni
roboczej, a nie mechanizm metadanych.

### 7.5. Co zostaje do sprintu Metadata Explorer

1. **Warstwa 2** — „co się zmieniło" jako pierwszorzędne pojęcie: uogólnienie punktowej aktualizacji na
   wszystkie 20 ścieżek DDL, przyrostowe przepięcie jednego wiersza w projekcji, i zachowanie przewinięcia
   oraz zaznaczenia (to, czego Warstwa 1 nie robi).
2. **Warstwa 3** — uzgodnić prefetch z `RefreshAsync` (martwa gałąź `LoadCountAsync`), kategoria `Domain`
   (79 ms, skan `RDB$FIELDS`), kategoria `User` (odpytuje bazę bezpieczeństwa).
3. **Koszt startu** — zmierzyć na realnej bazie instrumentem z §6 i dopiero wtedy decydować.

### 7.6. ⚠⚠ ŚCIEŻKA, KTÓREJ TEN DOKUMENT NIE ZMIERZYŁ — rozwinięcie KLIKNIĘCIEM (dopisane 2026-08-03)

> Powód dopisania: użytkownik zgłosił rzadkie (2–3 razy przez cały okres używania) **zawieszenie
> aplikacji przy rozwijaniu dużej kategorii**, poprzedzone tym, że **drzewo samo przewija się w dół**.
> Pełny zapis zgłoszenia i plan sprawdzenia: `product-polish-m3-handover.md` **§3.7a**.

⭐ **§2 tego dokumentu mierzył `OnChildrenChanged`** — czyli ścieżkę, którą idzie `SetLeaves` przy
ŁADOWANIU i ODŚWIEŻANIU kategorii. To ona była Θ(N²) i to ją naprawiła Warstwa 1.

⚠ **Istnieje druga ścieżka i nie została zmierzona:** `SidebarFlatController.OnExpandedChanged`, którą
idzie **rozwinięcie kliknięciem** na kategorii **już załadowanej**. Ona również wstawia liście
**pojedynczo**, a strażnik zbiorczy jej **nie obejmuje — pomija ją** (`if (_suspendDepth > 0) return;`),
bo przy operacjach zbiorczych projekcję i tak domyka `EndUpdate → Rebuild`.

**Szacowany koszt (do zweryfikowania pomiarem, nie przyjmować na wiarę):**

| | `OnChildrenChanged` przed Warstwą 1 | `OnExpandedChanged` (dziś) |
|---|---|---|
| powiadomienia `CollectionChanged` | **Θ(N²)** — 5 760 000 przy N=2 400 | **Θ(N)** — po jednym na liść |
| przesunięcia w `List<T>` | Θ(N²) | **Θ(N × ogon)** — każdy `Insert` przesuwa to, co stoi ZA kategorią |

⭐ Czyli **wyraźnie taniej niż naprawiony defekt, ale nieporównanie drożej niż jedna `Rebuild`.**
⛔ **Nie „naprawiać" tego przed pomiarem.** Może się okazać, że koszt jest pomijalny, a przyczyną
zgłoszonego zawieszenia jest co innego — kotwiczenie przewijania w wirtualizującym `ListBox`ie albo
`Dispatcher.Post` w `MetadataNodeViewModel.OnIsExpandedChanged`. **Instrument istnieje**
(`tools/probes/MetadataPerfProbe`, schemat 2 400 tabel); brakuje przypadku „rozwiń kliknięciem".

---

## 8. ⭐⭐ POMIAR WYKONANY (M3.4a, 2026-08-04) — ścieżka „rozwiń kliknięciem" NIE jest mechanizmem zawieszenia

> Sekcja 7 kończyła się zapisem: *„brakuje przypadku «rozwiń kliknięciem»"* i wprost zakazywała
> naprawiania go przed pomiarem. Pomiar wykonano. **Zakaz okazał się słuszny — hipoteza upadła.**

Przypadek **B4** dopisany do `tools/probes/MetadataPerfProbe` uruchamia **prawdziwy
`SidebarFlatController`** i przełącza `IsExpanded` kategorii, której liście **już są w pamięci** (czyli
ścieżka omijająca strażnika zbiorczego, bo `Children` się nie zmienia). Kolumna `ogon` to liczba wierszy
stojących **pod** kategorią — każdy `Insert`/`RemoveAt` je przesuwa.

| liście | ogon | expand | collapse | powiadomienia | jedna `Rebuild` (dla skali) |
|---|---|---|---|---|---|
| 2400 | 0 | **1,0 ms** | 1,1 ms | 2400 | 0,1 ms |
| 2400 | 3000 | 1,3 ms | 1,5 ms | 2400 | 0,4 ms |
| 2400 | 6000 | **2,3 ms** | 2,7 ms | 2400 | 0,6 ms |
| 5000 | 6000 | 4,8 ms | 7,4 ms | 5000 | 1,3 ms |

**Szacunek z sekcji 7 potwierdził się co do KSZTAŁTU** — Θ(N) powiadomień, Θ(N × ogon) przesunięć,
widoczne w kolumnie `ogon`: 0 → 6000 podnosi koszt z 1,0 do 2,3 ms. **Ale stała jest tak mała, że całość
mieści się w jednej klatce.** Dla porównania defekt naprawiony przez Warstwę 1, na tych samych 2 400
liściach: **916,9 ms** (sekcja 2, przebieg powtórzony 2026-08-04 — liczba niezmieniona).

### 8.1 ⛔ Decyzja: nie dokładamy tu strażnika

Zysk 2 ms nie uzasadnia zmiany w mechanizmie, który działa. Zapis w sekcji 7 przewidywał ten wynik
(*„może się okazać, że koszt jest pomijalny"*) i to jest **poprawny rezultat analizy**, nie jej brak.

### 8.2 ⚠⚠ Zakres pomiaru — bez tego akapitu tabela wyżej wprowadza w błąd

Sonda mierzy **warstwę modelu**: `ObservableCollection` i algorytm projekcji, **bez Avalonii**.
W działającej aplikacji te **2 400 powiadomień `CollectionChanged`** trafia do **wirtualizującego
`ListBox`a**, i **ta część pozostaje niezmierzona**.

⭐ A zgłoszony objaw — *„drzewo samo zaczyna przewijać się w dół"* — jest zachowaniem **panelu**, nie
kolekcji. Pomiar więc **przesunął granicę niewiedzy, nie zamknął tematu**:

| Wykluczone | Nadal otwarte |
|---|---|
| koszt projekcji jako przyczyna zamarcia UI | kotwiczenie przewijania w wirtualizującym `ListBox`ie |
| | re-estymacja ekstentu przez `VirtualizingStackPanel` przy N pojedynczych wstawieniach |
| | `Dispatcher.Post` w `MetadataNodeViewModel.OnIsExpandedChanged` |

### 8.3 ⭐ Następny krok jest zaplanowany i ma instrument

**Zaakceptowany przez użytkownika jako osobny krok po M3.4a:** eksperyment headless z prawdziwym
`ListBox`em i wirtualizacją, wymuszający rozwinięcie dużej kategorii. Jeżeli odtworzy zawieszenie
**deterministycznie**, długoletni „felerny test" `ConnectionExpandBindingProbe` staje się **testem
regresyjnym prawdziwego defektu**. Jeżeli nie odtworzy — hipoteza upada i **ten wynik też należy zapisać**.
⚠ Ryzyko: klasa headless konstruująca `MainWindow` zawiesza suite (#94/#226/#286) — asercje na najtańszej
kontrolce, dołączenie do `HeadlessCollection` **i do filtra partycji**.

⭐ **Instrument na żywą aplikację istnieje i nie trzeba nic budować:** `App/Diagnostics/ScrollTrace.cs`
(`EMBERTERN_SCROLL_DIAG=1`) rozróżnia *ekstent re-estymowany przez VSP* od *my przebudowaliśmy drzewo*.
Napisano go dokładnie pod ten objaw.

### 8.4 ⚠ Warstwy 2 i 3 pozostają otwarte, bez zmian

Ten pomiar niczego w nich nie rozstrzyga. Warstwa 2 (pierwszorzędne „co się zmieniło" na wszystkich
ścieżkach DDL + zachowanie przewijania i zaznaczenia), Warstwa 3 (higiena zapytań) i **niezmierzony koszt
startu** czekają na własny etap wydajnościowy po M3. ⛔ M3.4 ich nie dotyka.
