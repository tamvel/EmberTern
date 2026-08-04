# EmberTern — PROMPT STARTOWY: M3b.2 (postęp połączenia z bazą) i domknięcie M3

> Wklej to jako pierwszą wiadomość nowej sesji. Dokument jest **samowystarczalny w zakresie stanu,
> decyzji, zakresu i planu** — do implementacji sięgniesz jeszcze po dokumenty wskazane w §1.

---

## 0. Jednozdaniowe streszczenie poprzedniej sesji

**M3b.1 zamknięte** — import i Script Executor raportują do sekcji postępu paska statusu, etykieta nazywa
operację, a przy dwóch operacjach naraz rozstrzyga ratyfikowana drabinka priorytetów. ⭐⭐ **Pomiar wejściowy
obalił inwentarz etapu w trzech punktach**: ścieżek `IProgress` jest **pięć** (brakowało Script Executora —
jedynej ze ścisłą sumą), **eksport i batch biegną modalnie** i wypadają z zakresu na stałe, a „16 ViewModeli"
to lista stanów zajętości, nie lista rzeczy do podłączenia. ⚠⚠ Najważniejszy wynik jest metodologiczny:
**pierwsza wersja strażnika przechodziła przy podłożonym naruszeniu.**

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
| **Ostatni commit** | M3b.3 — analiza railu, zero zmian w kodzie. ⚠ **Sprawdź `git log --oneline -1` i `git status`** zamiast wierzyć temu wierszowi |
| **Build** | 0 błędów / 0 ostrzeżeń |
| **Suite** | **7310** zielony w trzech partycjach (**7193 + 63 + 54**). ⚠⚠ **Mierz przed cytowaniem** — ta liczba starzeje się najszybciej w całym dokumencie |
| **Smoke** | czysty |
| **Etap** | M0–M2c ✅ · M3: iteracja 0 ✅ · **M3.1 ✅ · M3.2 ✅ · 🔒 język kolorów ✅ · 🔒 M3.3 ✅ · 🔒 M3.4 ✅ · 🔒 M3b ✅ (M3b.1 · A+B+C · M3b.1d · M3b.2 · M3b.3 odłożone)** |
| ⭐⭐ **START** | ⛔ **BRAMA §13.3** — cztery powierzchnie trwałe JEDNOCZEŚNIE, żywa baza, oba motywy. ✅ M3b zamknięte w całości |
| **Po bramie** | Podsumowanie zamykające M3 + CLAUDE.md + handover M4 + prompt startowy |

### 2.1 Co zamknęło M3.4 i M3b.1

| Podetap | Wynik | Zapis |
|---|---|---|
| **M3.4a** | katalog poszedł za produktem: `Size.Row.Tree` **20 → 24** (decyzja **DB**), `MinHeight` i chevron na role — **token dostał pierwszego konsumenta**; kolizja 1 px → **K15**. Zero zmian wizualnych | §19.26 |
| **krok 15b** | eksperyment headless: prawdziwy `SidebarFlatController` w prawdziwym wirtualizującym `ListBox`ie. **Zero zawieszeń, pozycja przewijania nie ruszyła się ani razu.** Rozdzielił zmienne **A** (`MainWindow` w teście) i **B** (splice) — **B wykluczone** | §19.27 |
| **M3.4b cz. 1** | menu kontekstowe paska bocznego: **jedna instancja na rodzaj węzła** zamiast jednej na wiersz. **~74 % czasu przewijania, 440 → 22 żywych `MenuItem`** | §19.28 |
| **M3.4b cz. 2** | przegląd 32 menu / 154 pozycji / 71 komend — **bez znaleziska wymagającego zmiany** | §19.30 |
| **M3b.1** | import + Script Executor w sekcji postępu · etykieta **nazywa operację** · **drabinka priorytetów**, jedna operacja naraz. ⭐⭐ Pomiar obalił inwentarz w trzech punktach; **jeden pisarz sekcji**; seam agregacji tylko poszerzony (`WireRailSource` → `WireActivitySource`). ⚠⚠ **Strażnik w pierwszej wersji przechodził przy podłożonym naruszeniu** | §19.31 |

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

## 4. ⭐⭐ Zakres M3b.2 — anatomia połączenia, ZMIERZONA (nie przepisana z planu)

> ⚠⚠ **M3b.1 pokazało, że inwentarz w tym dokumencie był nieaktualny dwa razy z rzędu. Poniższe liczby są
> zmierzone 2026-08-04 na kodzie — ale i tak sprawdź je w kodzie, zanim na nich zbudujesz decyzję.**

### 4.1 Zakres ratyfikowany przez użytkownika

✅ zapytanie SQL (M3.1f) · ✅ Script Executor (M3b.1) · ✅ import (M3b.1) · ⏸ **połączenie + ładowanie
metadanych (M3b.2 — TO JEST ZADANIE)** · ⛔ eksport · ⛔ batch · ⛔ Performance Panel.

⛔⛔ **Eksport i batch są poza zakresem NA STAŁE**, bo biegną w `ShowDialog(owner)`: wartość sekcji (operacja
przeżywa przełączenie zakładki) tam nie istnieje, a `HasCancel` dałoby przycisk nieklikalny w zablokowanym
oknie. Ich własne paski **zostają**. ⛔ Nie wracać do tej decyzji bez nowego pomiaru.

### 4.2 Trzy fazy połączenia i co każda umie raportować

| Faza | Gdzie | Wątek | Co da się pokazać |
|---|---|---|---|
| 1. otwarcie 3 dołączeń | `MainWindowViewModel.ConnectAsync` (`:2735` → `_service.ConnectAsync`) | poza UI (`await`) | tryb **nieokreślony** |
| 2. odtworzenie zakładek | `MainWindowViewModel.LoadWorkspaceFor` (`:2430`) | ⛔⛔ **`private void` — SYNCHRONICZNIE NA UI** | **wyłącznie komunikat** |
| 3. prefetch 13 kategorii | `ConnectionNodeViewModel.LoadCategoriesAsync` (`:147–150`) | ⭐ UI oddaje sterowanie **między kategoriami** | **procent** — `CategoryOrder.Length` |
| 4. `NotifyMetadataReady` | `ConnectionNodeViewModel:163` | modele semantyczne edytorów w `Task.Run` | własny cykl życia każdego edytora |

⭐⭐ **UŻYTKOWNIK ZAAKCEPTOWAŁ OGRANICZENIE FAZY 2 WPROST** (2026-08-04): *„Rozumiem ograniczenie fazy 2
(synchroniczne odtwarzanie zakładek na UI), więc nie oczekuję sztucznie animowanego postępu. Jeżeli dla tej
fazy będzie można pokazać jedynie komunikat typu «Loading workspace...», a procenty pojawią się dopiero
podczas ładowania kategorii metadanych, to jest to dla mnie całkowicie akceptowalne. Najważniejsze jest, żeby
użytkownik miał informację, że aplikacja pracuje."*

⛔ **Nie animować fazy 2 sztucznie** i ⛔ **nie przenosić `LoadWorkspaceFor` poza wątek UI** — to
„deterministyczny load", oznaczony w CLAUDE.md jako ⛔ nie ruszać (diagnostyka musi mieć pełny kontekst
metadanych, inaczej zgłasza poprawne symbole jako błędy).

### 4.3 ⚠⚠ NAJWIĘKSZE RYZYKO, ZNANE Z GÓRY: pasek zapalony na zawsze

`LoadCategoriesAsync` jest wołane jako **`_ = …` (fire-and-forget), z DWÓCH miejsc**
(`ConnectionNodeViewModel:244` i `:263`), a przy nieudanym połączeniu (`ConnectionFailedException` →
`SetError`) **nie nastąpi wcale**. Faza 1 i faza 3 są w **dwóch niezależnych subskrybentach**
`ActiveConnectionChanged` (`MainWindowViewModel:291` i węzeł połączenia), oba przez `Dispatcher.UIThread.Post`
— **nie ma dziś jednego leja obejmującego cały ciąg**.

⭐ To jest dokładnie pułapka §19.7.4: *„nie da się dodać ścieżki wyjścia, która zostawi zapalony pasek"* była
prawdą dla `IsExecuting`, bo tam lej JEST jeden. Tutaj trzeba go **zaprojektować**, nie odziedziczyć.
⚠ Przy okazji: sekcję zapisuje od M3b.1 wyłącznie `UpdateProgressSection` — nowe źródło **musi** wejść tą
samą drogą, ⛔ nie przez `Progress.Begin` z gałęzi połączenia.

### 4.4 Co jest gotowe

| Potrzeba | Stan |
|---|---|
| Resolver + drabinka priorytetów | ✅ `MainWindowViewModel.ResolveProgressSection` — połączenie ma już **najwyższy szczebel** przewidziany w komentarzu; wystarczy dopisać jego gałąź |
| Instrument do pomiaru podziału czasu | ✅ `Diagnostics.PerfTrace.LogCategoryLoad` + `EMBERTERN_PERF_DIAG=1` — stoi dokładnie w fazie 3 |
| Suma dla trybu procentowego | ✅ `CategoryOrder.Length` (13) |
| Zdarzenie końca | ✅ `MetadataExplorerViewModel.MetadataReady` (`:203`) — `MainWindowViewModel` posiada `Metadata`, więc może je subskrybować |

---

## 5. Co ZMIERZYĆ przed implementacją M3b.2

1. **Realny podział czasu między fazy 1 / 2 / 3** istniejącym `EMBERTERN_PERF_DIAG=1` na dużej bazie.
   ⭐ Bez tego nie wiadomo, która faza jest tą, na którą użytkownik faktycznie czeka — a to decyduje, gdzie
   warto pokazać procent, a gdzie wystarczy komunikat.
2. **Kolejność faktycznego przeplotu faz 2 i 3** (dwóch subskrybentów, oba `Dispatcher.Post`) — od tego
   zależy sekwencja etykiet, a nie da się jej wydedukować z kolejności deklaracji.
3. **Wszystkie ścieżki wyjścia z połączenia**: sukces · `ConnectionFailedException` · rozłączenie w trakcie ·
   przełączenie profilu w trakcie. ⚠ Każda musi gasić sekcję; wypisz je **przed** implementacją.
4. **Czy `MetadataReady` wystarcza jako sygnał końca**, czy trzeba też ostatniej kategorii — pamiętając, że
   po `MetadataReady` biegną jeszcze modele semantyczne edytorów w `Task.Run` z własnym cyklem życia.
   ⛔ Nie obiecywać w pasku, że tamto też jest objęte.

---

## 6. Kolejność prac M3b.2 (propozycja — potwierdź z użytkownikiem)

```
pomiar §5 (bez zmian w kodzie)
  → propozycja: sekwencja etykiet + gdzie procent + jak osłonić wyjścia
    → AKCEPTACJA UŻYTKOWNIKA
      → implementacja
        → QA na żywej bazie w obu motywach
          → dokumentacja §19 + commit
```

⭐ **R15: wielkość iteracji idzie za niepewnością.** Tutaj niepewność jest **wysoka** (nowe źródło, brak leja,
faza nieanimowalna), więc drobne kroki są uzasadnione — odwrotnie niż w M3b.1, gdzie mechanizm był ratyfikowany
i jeden przebieg był właściwy.

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

oraz odwrotność z `|`, oraz `ConnectionExpandBindingProbe` osobno. Stan: **7193 + 63 + 54 = 7310**.
⚠⚠ **Filtr jest listą nazw i starzeje się cicho** — partycja headless ma **siedem** klas; kryterium
dołączenia: *czy klasa konstruuje kontrolki Avalonii*.

---

## 8. ⚠ Pułapki najgroźniejsze dla M3b.2 (pełna lista: handover §9)

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
8. ⭐⭐ **NOWA (M3b.1, §19.31.4) — STRAŻNIK MOŻE BYĆ ZIELONY Z POWODU, KTÓREGO JEGO NAZWA NIE OPISUJE.**
   Test *„zapytanie po skrypcie nie dziedziczy procentu"* gasił skrypt PRZED startem zapytania, więc sekcja
   przechodziła przez stan „nic nie trwa", a `End()` resetuje tryb **sam** — test przechodził niezależnie od
   badanego mechanizmu i **przeszedł też z podłożonym naruszeniem**. ⭐ Ujawniło to wyłącznie podłożenie
   naruszenia; poprawny kształt to **przejście właściciela BEZ PRZERWY**. ⚠ Dwa dalsze wnioski: **plant musi
   kłamać w JEDNYM wymiarze** (zbyt szeroki położył 7 z 13 testów i nic nie izolował), oraz **po każdym
   podłożeniu sprawdź `Liczba błędów: 0` przed odczytaniem czerwieni** — jeden plant się nie skompilował,
   testy pobiegły na starym binarium i pokazały czerwień *poprzedniego* naruszenia.

---

## 9. ⛔ Czego M3b.2 NIE robi

1. ⛔ **Nie zmienia `StatusProgressViewModel` w model wielu operacji naraz** — ratyfikowano **jedną operację
   naraz na drabince priorytetów**, bez licznika ukrytych operacji.
2. ⛔ **Nie dodaje drugiej komendy Cancel** — model przyjmuje komendę właściciela.
3. ⛔ **Nie podłącza eksportu ani batcha** (modalne, §4.1) i **nie usuwa** żadnego istniejącego `ProgressBar`.
3b. ⛔ **Nie zapisuje sekcji poza `UpdateProgressSection`** — od M3b.1 pisarz sekcji jest jeden.
4. ⛔ **Nie zwiększa wysokości paska statusu** — §8.5 specyfikacji zabrania wprost.
5. ⛔ **Nie rozszerza katalogu, żeby domknąć kolizję** — **K1–K15** czekają na §13.3 (R3).
6. ⛔ **Nie wraca do paska zakładek, drzewa ani menu** bez realnego defektu funkcjonalnego — M3.3 i M3.4
   zamknięte.
7. ⛔ **Nie rusza `AutoScrollToSelectedItem`** (§3.1) ani `TreeDiagnostics` (§3.2).
8. ⛔ **Nie naprawia przy okazji rzeczy spoza zakresu** — zmierz, opisz, zapisz, nie rozwiązuj bez decyzji.
9. ⏸ **Nie zaczyna pełnej semantyki kolorów railu** — to **M3b.3**, po podłączeniu wszystkich źródeł.
   ⭐ Decyzja użytkownika: *„jeżeli okaże się, że obecne kolory są wystarczające, nie ma potrzeby komplikować
   ich semantyki"* — brak zmiany jest tam dopuszczalnym wynikiem, nie porażką.

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
| ⏸ | pełna semantyka kolorów railu | **M3b.3** — po podłączeniu wszystkich źródeł |
| ⏸ | szerokość etykiety sekcji postępu (czy przesuwa chipy na tyle, żeby przeszkadzało) | QA użytkownika, §19.31.6 |

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
