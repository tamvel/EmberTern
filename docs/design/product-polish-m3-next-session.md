# EmberTern — PROMPT STARTOWY: M3b (pasek postępu) i domknięcie M3

> Wklej to jako pierwszą wiadomość nowej sesji. Dokument jest **samowystarczalny w zakresie stanu,
> decyzji, zakresu i planu** — do implementacji sięgniesz jeszcze po dokumenty wskazane w §1.

---

## 0. Jednozdaniowe streszczenie poprzedniej sesji

**M3.4 zamknięte w całości** — wiersz drzewa poszedł na role (M3.4a), eksperyment headless wykluczył
wirtualizację (15b), menu kontekstowe paska bocznego przestały być mnożone przez wirtualizację (M3.4b cz. 1),
a przegląd 32 menu nie znalazł nic do naprawy (cz. 2). **Przy okazji znaleziono i naprawiono kilkuletni
defekt drzewa** — samoczynne przewijanie i zawieszanie aplikacji.

---

## 1. Co przeczytać, zanim napiszesz linijkę kodu

| # | Dokument | Zakres |
|---|---|---|
| 1 | **ten plik** | w całości |
| 2 | ⭐⭐ **`docs/design/product-polish-m3-handover.md`** | **w całości** — stan · reguły **R1–R17** · procedura iteracji · **21 pułapek** · plan §10 |
| 3 | `product-polish.md` **§8.4.6** | model sekcji postępu Status Bara — **wiążący** |
| 4 | `product-polish.md` **§19.7** | as-built M3.1f: co dokładnie dostarczyła infrastruktura i **dlaczego oba tryby** |
| 5 | `src/EmberTern.App/ViewModels/StatusProgressViewModel.cs` | ⭐ **101 linii, przeczytaj CAŁY** — komentarz klasy jest kontraktem M3b i wymienia jego konsumentów po nazwie |
| 6 | `product-polish.md` **§13.3** | ⛔ brama jakości, która czeka **za** M3b |
| 7 | `color-language.md` | **tylko gdy dotykasz koloru** — §6 i ⛔ **§0.5** |

⛔ **Nie czytaj na starcie:** `product-polish.md` §15, §18.x, §19.0–§19.30 (sięgaj po konkretną
podsekcję) · handoverów M2a/M2b/M2c.

---

## 2. Stan projektu

| | |
|---|---|
| **Branch** | `feat/product-polish` |
| **Ostatni commit** | `abb1d9f` (dokumentacja M3.4b cz. 2). ⚠ **Sprawdź `git log --oneline -1` i `git status`** zamiast wierzyć temu wierszowi |
| **Build** | 0 błędów / 0 ostrzeżeń |
| **Suite** | **7271** zielony w trzech partycjach (**7154 + 63 + 54**). ⚠⚠ **Mierz przed cytowaniem** — ta liczba starzeje się najszybciej w całym dokumencie |
| **Smoke** | czysty |
| **Etap** | M0–M2c ✅ · M3: iteracja 0 ✅ · **M3.1 ✅ · M3.2 ✅ · 🔒 język kolorów ✅ · 🔒 M3.3 ✅ · 🔒 M3.4 ✅** |
| ⭐⭐ **START** | **M3b — podłączenie pozostałych operacji do paska postępu** (pozycja 17 w planie §10 handovera) |
| **Po M3b** | ⛔ **brama §13.3**, potem podsumowanie zamykające M3 + handover M4 |

### 2.1 Co zamknęło M3.4 — cztery iteracje

| Podetap | Wynik | Zapis |
|---|---|---|
| **M3.4a** | katalog poszedł za produktem: `Size.Row.Tree` **20 → 24** (decyzja **DB**), `MinHeight` i chevron na role — **token dostał pierwszego konsumenta**; kolizja 1 px → **K15**. Zero zmian wizualnych | §19.26 |
| **krok 15b** | eksperyment headless: prawdziwy `SidebarFlatController` w prawdziwym wirtualizującym `ListBox`ie. **Zero zawieszeń, pozycja przewijania nie ruszyła się ani razu.** Rozdzielił zmienne **A** (`MainWindow` w teście) i **B** (splice) — **B wykluczone** | §19.27 |
| **M3.4b cz. 1** | menu kontekstowe paska bocznego: **jedna instancja na rodzaj węzła** zamiast jednej na wiersz. **~74 % czasu przewijania, 440 → 22 żywych `MenuItem`** | §19.28 |
| **M3.4b cz. 2** | przegląd 32 menu / 154 pozycji / 71 komend — **bez znaleziska wymagającego zmiany** | §19.30 |

---

## 3. ⭐⭐ USTALENIA, KTÓRYCH NIE WOLNO ZGUBIĆ

### 3.1 🐞 Defekt drzewa — przyczyna znaleziona i naprawiona (2026-08-04)

Objaw zgłaszany od lat: rozwinięcie kilku dużych kategorii → **lista sama przewija się w dół** → nie da się
zatrzymać → kliknięcie zawiesza i zamyka proces.

**Przyczyna:** gdy jakiś wiersz jest zaznaczony, Avalonia odkłada na Dispatcher
`SelectingItemsControl.AutoScrollToSelectedItemIfNecessary`; zaznaczony wiersz leży poza oknem realizacji,
a `VirtualizingStackPanel.ScrollIntoView` **nie potrafi skoczyć do nierealizowanego indeksu** — pełznie do
celu **po jednym wierszu (24 px) na cykl Dispatchera**, zagładzając priorytet tła.

**Naprawa:** `AutoScrollToSelectedItem="False"` **wyłącznie na `SidebarList`**, bez żadnych warunków.
⛔ **NIE USUWAĆ** — chroni ją strażnik `SidebarList_DisablesAvaloniaAutoScrollToSelectedItem`, bo ta
właściwość wygląda dokładnie jak coś, co ktoś kiedyś „posprząta": domyślnie `true`, usunięcie **nie psuje
żadnego innego testu**, nie rusza piksela, a defekt wraca dopiero u użytkownika z dużą bazą.
⏸ Jedyna otwarta pozycja: **nawigacja klawiaturą** (strzałki, PageUp/PageDown, Home/End). Gdyby nie
utrzymywała zaznaczenia w widoku — odpowiedzią jest rozwiązanie **dla nawigacji klawiaturą**, ⛔ nigdy powrót
globalnego auto-scrolla (ratyfikowane).
Pełny zapis: `metadata-refresh-analysis.md` **§10–§12**, `product-polish.md` **§19.29**.

### 3.2 🔧 `EMBERTERN_TREE_DIAG` — ukryte narzędzie deweloperskie, ZOSTAJE

Flaga środowiskowa włącza `App/Diagnostics/TreeDiagnostics.cs`: przewijanie + zdarzenia pętlotwórcze +
przebudowy listy + zagnieżdżenie zakresów + głębokość stosu + wyjątki, z własnym plikiem w `%TEMP%`.
⭐ **To ono znalazło przyczynę po latach objawu.** Bez flagi kosztuje **zero** — żadnego pliku, żadnych
subskrypcji. ⛔ Nie usuwać i nie wystawiać w UI. ⭐ Sięgać po nie przy **każdym** defekcie o kształcie
„przewijanie / zaznaczenie / Dispatcher", nie tylko w drzewie.

⚠⚠ **Instrument przy pierwszym uruchomieniu ZABIŁ APLIKACJĘ** — `string.Format` z wyrównaniem `{4,+8:0.0}`
(wyrównanie przyjmuje tylko liczbę całkowitą). Dlatego dziś: **zero formatów złożonych w tej klasie**, jedna
brama `Safe` pomijająca wpis zamiast zatrzymywać aplikację, strażnik reentrancji i **samotest kanału
wyjątków**. ⛔ Nie cofać żadnej z tych trzech rzeczy.

### 3.3 ⏸ HIPOTEZA DO OBSERWACJI — nie ogłaszać jako faktu

Możliwe, że ten sam mechanizm powodował **sporadycznie zawieszający się `ConnectionExpandBindingProbe`**
(#94/#226/#261). Probe wykonuje operacje na drzewie w prawdziwym `MainWindow`; z zaznaczonym wierszem mógł
wpaść w tę samą pętlę, a **zagłodzony Dispatcher wygląda dokładnie tak samo** jak wcześniej podejrzewany
„teardown sesji".

⭐ **Kryterium: jeżeli od 2026-08-04 ten test przestanie się zawieszać, będzie to bardzo mocna przesłanka
wspólnej przyczyny.** ⛔ Nie ogłaszać po kilku zielonych przebiegach — zawieszenie było rzadkie z definicji.
**Obserwować całą suitę i zapisać wynik w OBIE strony.** ⚠ Do tego czasu procedura bez zmian: probe biegnie
**w osobnej partycji**.

### 3.4 ⭐⭐ Trzy lekcje metodologiczne z M3.4

1. **Pomiar syntetyczny odtwarza MECHANIZM, ale nie odtwarza STANU.** M3.4a i 15b **wykluczyły swoje
   hipotezy poprawnie** i oba były ślepe — w żadnym **nic nie było zaznaczone**. ⚠ Zanim uznasz, że pomiar
   wyklucza hipotezę, **wypisz stany, w których defekt występuje u użytkownika, i sprawdź, które z nich twój
   eksperyment odtwarza.**
2. **Pomiar negatywny musi się przedstawić.** Cisza w kategorii `EXC` była **dowodem** tylko dzięki
   samotestowi; bez niego znaczyłaby „albo nic nie poleciało, albo hak nie działa".
3. **Pomiar po nośniku nie odpowiada na pytanie o rolę.** „14 pozycji bez ikony" było błędne — osiem niesie
   ikonę składnią elementową `<MenuItem.Icon>`, a skan liczył atrybut `Icon=`.

---

## 4. ⭐⭐ Zakres M3b — zmierzony 2026-08-04, nie przepisany z planu

> ⚠⚠ **Handover §3.9 mówi „trzy realne ścieżki `IProgress`". To jest NIEAKTUALNE — są CZTERY.**
> Pomiar niżej jest świeży; plan etapu starzeje się tak samo cicho jak string (pułapka 20).

### 4.1 Co już istnieje — infrastruktura z M3.1f

`StatusProgressViewModel` (101 linii, **przeczytaj cały**):

| Element | Znaczenie |
|---|---|
| `Begin(label, cancelCommand = null)` | start operacji; `IsRunning` steruje widocznością sekcji |
| `Report(label)` | tryb **nieokreślony** |
| `Report(label, percent)` | tryb **procentowy** |
| `End()` | koniec; brak operacji to stan domyślny, **nie komunikat** |
| `HasCancel` | czy pokazać przycisk anulowania |

⚠ **Jedyny konsument dzisiaj: `MainWindowViewModel.Progress`** — operacja referencyjna, czyli wykonanie
zapytania SQL. **M3b podłącza resztę.**

### 4.2 Inwentarz źródeł — stan faktyczny

**Cztery ścieżki `IProgress`:**

| # | Ścieżka | Typ | Uwaga |
|---|---|---|---|
| 1 | eksport (`Export/ExportService.cs` + eksportery w Core) | `IProgress<long>` | licznik wierszy |
| 2 | wykonanie zapytania (`MainWindowViewModel.MakeLoadProgress`) | `IProgress<long>` | ✅ **już podłączone** (operacja referencyjna) |
| 3 | batch (`MainWindowViewModel`) | `IProgress<(int Index, string? Error)>` | ma **znaną sumę** → tryb procentowy |
| 4 | ⚠ **import (`Core/Import/ImportPipeline.cs`)** | `IProgress<ImportProgress>` | **czwarta, nieujęta w §3.9** |

**Cztery `ProgressBar` w widokach:** `BatchResultsDialog`, `DataImportTabView`, `ExportDialog`,
`MainWindow` (ten ostatni to sekcja z M3.1f).

**16 ViewModeli** ma własny stan „trwa operacja" (`IsRunning`/`IsBusy`/`IsExecuting`/`IsLoading`):
`BatchResults`, `DataImportTab`, `DomainDetailTab`, `ExceptionDetailTab`, `GeneratorDetailTab`,
`IndexDetailTab`, `MainWindow`, `MetadataExplorer`, `MetadataNode`, `PackageDetailTab`,
`ScriptExecutorTab`, `SecurityManagerTab`, `SourceObjectDetailTab`, `StatusProgress`, `TableDetailTab`,
`ViewDetailTab`.

⚠⚠ **„16 ViewModeli" to NIE jest lista rzeczy do podłączenia.** To lista miejsc, które **mają jakiś stan
zajętości** — a część z nich to zwykłe „ładuję zawartość zakładki", które w pasku statusu byłoby szumem.
⭐ **Pierwszym zadaniem M3b jest ROZSTRZYGNIĘCIE, które z nich to operacja warta pokazania**, a nie
podłączenie wszystkich.

### 4.3 ⭐ Kontrakt, który komentarz `StatusProgressViewModel` już ustala

* ⛔ **Anulowanie: model NIE ma własnej komendy.** Przyjmuje `ICommand` właściciela operacji, więc pasek
  statusu i toolbar naciskają **ten sam obiekt komendy**. ⛔ Nie dodawać drugiej komendy Cancel — powstałby
  drugi właściciel stanu anulowania.
* ⚠ **Ścieżka procentowa NIE MA dziś konsumenta na żywo** — nie zakładać, że jest sprawdzona. Jej
  przewidziani konsumenci są wymienieni po nazwie: `BatchResultsDialog` (`PreparationTotal`) i
  `DataImportTabView` (`ProgressPercent`).
* ⚠⚠ **Model niesie JEDNĄ operację naraz**, i to jest zapisane jako **decyzja projektowa dla M3b**:
  *„co pokazać, gdy biegną dwie"* rozstrzyga się **na komplecie źródeł**, nie zgadywaniem. ⭐ To jest
  najważniejsze pytanie projektowe tego podetapu.

---

## 5. Co ZMIERZYĆ przed implementacją

1. **Które z 16 stanów zajętości to operacja warta pokazania w pasku statusu**, a które to ładowanie
   zawartości zakładki. Kryterium proponowane: *czy użytkownik może w tym czasie robić coś innego i czy
   chce wiedzieć, że to trwa*. ⛔ Nie podłączać wszystkiego dlatego, że się da.
2. **Czy któreś dwie operacje mogą realnie biec jednocześnie** — i które. Od tego zależy odpowiedź na
   pytanie z §4.3. ⚠ SQL Editor blokuje równoległe wykonanie przez `IsExecuting`, ale import, eksport
   i batch **mają własne wątki i własne zakładki**.
3. **Które źródła znają SUMĘ** (→ tryb procentowy), a które nie (→ nieokreślony). Batch i import znają;
   eksport i wykonanie zapytania nie.
4. **Które operacje mają komendę anulowania** i jaki jest jej `CanExecute` — bo to decyduje o `HasCancel`.
5. ⚠ **Czy trzy istniejące `ProgressBar` mają zostać.** Pasek statusu **nie zastępuje** paska w oknie
   dialogowym eksportu ani w zakładce importu — to jest pytanie produktowe, nie porządkowe.

---

## 6. Kolejność prac (propozycja — potwierdź z użytkownikiem)

```
pomiar §5 (bez zmian w kodzie)
  → propozycja: które źródła, jaki tryb, co przy dwóch naraz
    → AKCEPTACJA UŻYTKOWNIKA
      → podłączenie źródeł, po jednym na iterację
        → QA w obu motywach po każdej iteracji
          → dokumentacja §19 + commit
```

⭐ **R15: wielkość iteracji idzie za niepewnością.** Dopóki nie ma odpowiedzi na „co przy dwóch naraz",
idź drobno; po jej ratyfikacji podłączanie kolejnych źródeł to praca powtarzalna i może iść większymi
krokami.

---

## 7. Obowiązkowa kolejność techniczna

```
analiza → (propozycja + AKCEPTACJA) → implementacja
  → uruchomienie aplikacji + QA w obu motywach
    → dotnet build (0/0)
      → dotnet test (TRZY partycje, OSOBNO)
        → smoke
          → dokumentacja (product-polish.md §19 + handover)
            → commit (kod + opis iteracji razem)
              → push na oba remote'y — WYŁĄCZNIE po akceptacji użytkownika
```

⚠⚠ **Nigdy nie łącz `dotnet build` i `dotnet test` w jednym poleceniu — deadlock.**
⚠ **Zabij `EmberTern.exe` przed przebudową** — inaczej MSB3021/MSB3027.

**Trzy partycje** (⚠ `ConnectionExpandBindingProbe` biegnie **sam**):

```
--filter "FullyQualifiedName!~ConnectionExpandBindingProbe&FullyQualifiedName!~SettingsCenterViewTests&FullyQualifiedName!~BrandingPresentationTests&FullyQualifiedName!~DesignTokenApplicationTests&FullyQualifiedName!~TabStripPresentationTests&FullyQualifiedName!~MetadataTreeVirtualizationProbe&FullyQualifiedName!~SharedContextMenuFeasibilityProbe"
```

oraz odwrotność z `|`, oraz `ConnectionExpandBindingProbe` osobno. Stan: **7154 + 63 + 54 = 7271**.
⚠⚠ **Filtr jest listą nazw i starzeje się cicho** — partycja headless ma **siedem** klas; kryterium
dołączenia: *czy klasa konstruuje kontrolki Avalonii*.

---

## 8. ⚠ Pułapki najgroźniejsze dla M3b (pełna lista: handover §9)

1. ⚠⚠ **`{DynamicResource}` NIE rzuca przy brakującym kluczu** — literówka jest niewidoczna przy zielonym
   buildzie. Nazwy ról bierz z `Tokens.axaml`, nie z pamięci.
2. ⚠⚠ **Wartość lokalna bije setter stylu.** Po jej usunięciu kontrolka zaczyna słuchać systemu — to
   **ujawniony dług**, nie regresja. Zgłoś, nie maskuj.
3. ⚠⚠ **Test headless konstruujący `MainWindow` ZAWIESZA suite.** Asercje rób na najtańszej kontrolce;
   nowa klasa headless **dołącza do `HeadlessCollection`** i **do filtra partycji**.
4. ⭐⭐ **Przeniesienie faktu zostawia po sobie „regresję", która nią nie jest** (pułapka 13). M3.1d odebrało
   paskowi edytora SQL kropkę stanu, bo fakt przeszedł do chipa — i *„pasek transakcji zgubił kropkę"* jest
   bardzo wiarygodnym zgłoszeniem. ⚠ **M3b jest tego pełen**: każde źródło, które zacznie raportować do
   paska statusu, może stracić coś u siebie. **Komentarz idzie w OBA miejsca** — do tego, które fakt oddało,
   i do tego, które go przejęło.
5. ⭐⭐ **Zapytaj przy każdym źródle: czy ten fakt ma już właściciela gdzie indziej i czy tamten właściciel
   nie jest bramkowany zakładką?** Bramka `IsXxxTabActive` na nośniku stanu **globalnego** to defekt §0.1.2,
   nawet gdy wygląda jak porządek.
6. ⚠ **R13: nie rezerwuj miejsca na element, którego w danym kontekście nie będzie.** Sekcja postępu jest
   niewidoczna, gdy nic nie trwa — ⛔ to jest stan domyślny, nie komunikat „brak operacji".
7. ⛔ **Pułapka 17: reguła opisuje to, co jest dobre.** *„Wszystkie 16 ViewModeli mają stan zajętości, więc
   wszystkie powinny raportować"* jest dokładnie tym błędem, który wycofał M3.2b w całości.

---

## 9. ⛔ Czego M3b NIE robi

1. ⛔ **Nie zmienia `StatusProgressViewModel` w model wielu operacji naraz** bez ratyfikowanej odpowiedzi na
   pytanie z §4.3.
2. ⛔ **Nie dodaje drugiej komendy Cancel** — model przyjmuje komendę właściciela (§4.3).
3. ⛔ **Nie usuwa istniejących `ProgressBar`** z eksportu/importu/batcha bez decyzji produktowej (§5.5).
4. ⛔ **Nie zwiększa wysokości paska statusu** — §8.5 specyfikacji zabrania wprost.
5. ⛔ **Nie rozszerza katalogu, żeby domknąć kolizję** — **K1–K15** czekają na §13.3 (R3).
6. ⛔ **Nie wraca do paska zakładek, drzewa ani menu** bez realnego defektu funkcjonalnego — M3.3 i M3.4
   zamknięte.
7. ⛔ **Nie rusza `AutoScrollToSelectedItem`** (§3.1) ani `TreeDiagnostics` (§3.2).
8. ⛔ **Nie naprawia przy okazji rzeczy spoza zakresu** — zmierz, opisz, zapisz, nie rozwiązuj bez decyzji.
9. ⏸ **Nie zaczyna pełnej semantyki kolorów railu** bez decyzji — odłożona tu świadomie przez użytkownika,
   z pomiarem (§19.4.4); rozstrzygnąć **razem z** sekcją postępu, bo oba mówią „coś się dzieje".

---

## 10. ⏸ Otwarte pozycje całego etapu

| # | Co | Gdzie rozstrzygane |
|---|---|---|
| **DC** | likwidacja `AccentIconBrush` / `InfoIconBrush` (24 wystąpienia / 14 plików) | M4.3 / M5 |
| **K1–K15** | rejestr kolizji (§18.R); ⭐ **K12–K14 jako JEDNO pytanie o gęstość paska**, **K15 jako JEDNO o gęstość drzewa** | brama **§13.3** |
| **V‑1** | kontrast koloru komentarzy SQL (4,14:1) — ratyfikowany, że zostaje | rewizja w użyciu |
| **R‑6 (DPI)** | ⚠ częściowo **nieweryfikowalne headlessowo** — sprawdzić **150 % okiem** | brama §13.3 |
| ⏸ | nawigacja klawiaturą w drzewie po §3.1 | QA użytkownika |
| ⏸ | hipoteza o zawieszającym się teście (§3.3) | obserwacja suity |
| ⏸ | przycisk/licznik przepełnienia paska zakładek (§8.2) | do zaplanowania |
| ⏸ | 6 sierocych stałych `UiStrings` · role `Pad.Tab`, `Size.Icon.Lg` bez konsumentów · 64 literały `Size.Icon` + 112 literałów ikona/odstęp | §13.3 / M4.3 |
| ⏸ | pełna semantyka kolorów railu | **M3b** |

---

## 11. ⭐⭐ Reguła prowadząca

> **Użytkownik:** *„Dokument ma prowadzić produkt. Nie produkt dokument."*
> **Użytkownik (R16):** *„Potraktuj pomiary jako narzędzie diagnostyczne, a nie kryterium zakończenia
> zadania. Kryterium odbioru jest wygląd na ekranie."*

Jeżeli znajdziesz rozwiązanie wyraźnie lepsze od zapisanego:
**propozycja → akceptacja → aktualizacja dokumentu → implementacja.**
⛔ Ani ciche odstępstwo, ani ślepa zgodność. ⛔ Decyzje D1–D12 i reguły R1–R17 zmienia wyłącznie użytkownik.

⭐ **M3.4 dało trzy dowody, że to działa w praktyce:** przeskalowanie M3.4a (plan był nieaktualny),
zatrzymanie się przed naprawą kosztu menu i pokazanie pomiaru zamiast implementacji, oraz **cofnięcie
własnej hipotezy o mechanizmie zawieszenia po pomiarze, który ją obalił**. Za każdym razem wygrał pomiar,
nie przekonanie.
