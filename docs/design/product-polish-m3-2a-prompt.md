# EmberTern — M3.2a (H‑3) — PROMPT STARTOWY NOWEJ SESJI

> Wklej to jako pierwszą wiadomość nowej sesji. Dokument jest **samowystarczalny w zakresie stanu
> i decyzji** — do implementacji sięgniesz jeszcze po dwie sekcje wskazane w §1.

---

## 0. Co przeczytać, zanim napiszesz linijkę kodu

| # | Dokument | Zakres |
|---|---|---|
| 1 | **ten plik** | w całości |
| 2 | `docs/design/product-polish-m3-handover.md` | w całości — stan, reguły R1–R12, procedura iteracji, **13 pułapek** |
| 3 | `product-polish.md` **§3.6** *(w handoverze)* + **§1.3/H‑3** | stan wejściowy M3.2a |
| 4 | `product-polish.md` **§19.0–§19.8** | as-built M3 — sięgaj po konkretną podsekcję, nie po całość |
| 5 | `Themes/Tokens.axaml`, `Themes/Typography.axaml` | katalog — **źródło prawdy o rolach** |

⛔ **Nie czytaj na starcie:** §15 (21 iteracji M2b), §18.1–§18.11 (9 iteracji M2c), handoverów M2a/M2b/M2c.

---

## 1. Stan projektu

| | |
|---|---|
| **Branch** | `feat/product-polish` |
| **Build** | 0 błędów / 0 ostrzeżeń |
| **Suite** | **7133** zielony w trzech partycjach (**7031 + 48 + 54**) |
| **Smoke** | czysty · **Drzewo** czyste |
| **Etap** | M0–M2c ✅ · **M3.1 ZAMKNIĘTE** (iteracja 0 + M3.1a–M3.1f, wszystkie odebrane przez użytkownika) |

### ⛔⛔ PIERWSZA RZECZ DO ZROBIENIA: PUSH

**Na obu remote'ach stoi `bca5210` (koniec M3.1d). Trzy commity czekają lokalnie: M3.1e, M3.1f
i poprawki odbiorcze.** Użytkownik świadomie wstrzymał push do czasu sprawdzenia w nowej sesji.

```bash
git push origin feat/product-polish
git push private feat/product-polish
```

⚠ **Najpierw** zweryfikuj build + trzy partycje + smoke, **potem** zapytaj użytkownika o zgodę na push.
⛔ Nigdy nie pushuj bez jego akceptacji — to stała reguła projektu.

---

## 2. Co dostarczyła poprzednia sesja

### 2.1 M3.1e — chipy Trace i Debuggera (§19.6)

Sekcja 3 paska statusu ma komplet chipów: transakcja (M3.1d) + **Debug** + **Trace**, każdy jako
**znak tożsamości + etykieta**. Tooltipy reużywają istniejących `StatusText` obu VM‑ów — zero drugiego
mapowania stanu. Agregacja (`IsDebugSessionLive` / `IsTraceSessionLive`) powstała już w M3.1c.

⚠⚠ **Chipy NIE dziedziczą pędzli railu, i to jest decyzja z pomiaru.** `DebugCurrentLineBarBrush`
(rail debuggera) jest **półprzezroczysty** i na tle paska daje **3,77:1** — jako 2 px rail przechodzi
(próg §10 dla elementu UI to 3:1), jako **tekst 10 px nie** (4,5:1). Chip debuggera bierze
`AccentIconBrush` (5,17 / 4,81), Trace zostaje na `IconColor_Query` (8,03 / 6,58).
⛔ **Nie „ujednolicaj" chipa z railem bez ponownego policzenia kontrastu.**

### 2.2 ⭐⭐ DebuggerIcon — trzy rundy QA i najważniejsza lekcja sesji (§19.6.6)

Chip postawił znak przy 12 px i kropka przerwania zaczęła czytać się jak artefakt renderowania.

* **Runda 1** — kropka w dół. Zrobiła miejsce **kosztem kompozycji**; użytkownik: *„kropka ucieka
  w dół zamiast być naturalnym elementem symbolu"*.
* **Runda 2** — przekomponowany trójkąt. **Mierzył się lepiej w KAŻDEJ liczbie** (prześwit, margines
  ink, pozycja kropki) i został odrzucony na pierwszy rzut oka: Execute i Debug przestały czytać się
  jako rodzina.
* **Runda 3** — pomiar obalił założenie **obu stron**: znak **nigdy nie był** ikoną Execute. Nosił
  własny trójkąt `(6,4)(18,12)(6,20)` wobec `Icon.Play` `(8,5)(19,12)(8,19)`.

⭐ **Rozwiązanie: `Path Data="{StaticResource Icon.Play}"`** — trójkąt nie ma już własnej ścieżki, więc
rozjazd jest **niemożliwy**, a nie tylko niepożądany. Ruszyła się wyłącznie kropka → `(19; 19)`.
Pinowane testem na **tożsamość instancji** (`Assert.Same`), bo kopia o identycznych współrzędnych
przeszłaby test na równość i przywróciłaby tę samą podatność.

⛔⛔ **LEKCJA, KTÓRA OBOWIĄZUJE DALEJ: SPÓJNOŚĆ ZESTAWU BIJE OPTIMUM POJEDYNCZEGO ELEMENTU.** Wariant 2
przegrał w jedynym wymiarze, którego nie mierzyłem. To R8 w najczystszej postaci.
⛔ **Ikona debuggera jest ZAMKNIĘTA.** Jeśli kropka znów będzie potrzebowała miejsca — rusza się
**kropka**; znak bazowy nie jest częścią regulowaną.

### 2.3 M3.1f — sekcja postępu (§19.7)

Sekcja 4 + jedna operacja referencyjna (wykonanie zapytania SQL). `StatusProgressViewModel` obsługuje
**oba tryby** — to **infrastruktura dla M3b** (ratyfikowany podział D4).

* ⚠ **Operacja referencyjna umie tylko tryb nieokreślony** — `IProgress<long>` to licznik wierszy,
  strumieniowy odczyt nie zna sumy. **Ścieżka procentowa nie ma konsumenta NA ŻYWO**; wykonuje ją
  wyłącznie test. Konsumenci istnieją i są policzeni: Batch (`PreparationTotal`) i Data Import
  (`ProgressPercent`) — podłącza ich M3b.
* ⭐ **Model NIE ma własnej komendy Cancel** — przyjmuje `ICommand` właściciela, więc pasek statusu
  i toolbar naciskają **ten sam obiekt**. Zamknęło to realną lukę: `ShowCancelButton` jest bramkowane
  `IsQueryTabActive`, więc przełączenie zakładki **odbierało możliwość anulowania**.
* ⭐ Jeden punkt wpięcia: `Progress.Begin/End` na `OnIsExecutingChanged` — jedynym miejscu, przez które
  przechodzi każde wejście i wyjście z wykonania. Nie da się dodać ścieżki zostawiającej zapalony pasek.
* ⚠⚠ **Fluent nadaje `ProgressBar` `MinWidth=200`**, a Avalonia przycina `Width` przez `MinWidth` —
  bez `MinWidth="0"` deklarowane 120 px renderowałoby się jako 200. **Drugie wystąpienie tej pułapki
  w etapie** (pierwsze: strzałka drzewa metadanych w M2b, 20 → 100).

### 2.4 Trzy poprawki odbiorcze (§19.8)

| Zgłoszenie | Rzeczywista przyczyna |
|---|---|
| „`localhost:3050` opada" | **Nic do naprawy.** Obecne wyrównanie jest najbliższe optycznego środka (0,30 px); `InlineUIContainer` przestrzeliwuje −1,04 px, a `BaselineAlignment` na `Run` jest **ignorowane**. Wrażenie bierze się z wersalików nazwy vs minuskuły endpointu. ⛔ Zamknięte, pomiar w komentarzu przy runie |
| „historia parametrów nie odtwarza wartości — to od wymiany kontrolek" | **C3 z etapu debuggera.** Wpisy sprzed 2026‑07‑25 nie mają `TypeText`, więc dowód zgodności typu odmawia — **po cichu**. Reguła była dla auto‑apply, ale konstruktor i ręczny wybór to jedna ścieżka. Rozdzielone `_seedingHistory` |
| „pusta kolumna Type przy domenie — lokalne dla Variables" | **KOLEJNOŚĆ, i dotyczy 7 siatek.** `LoadType` ustawia `DomainName` pod `_suppressCompose`, a subskrypcja `CollectionChanged` ratowała tylko przypadek „domeny dojechały późno" |

⚠⚠ Przy trzeciej poprawce **realnie zagrożona była reguła #11**: `TypeText` (źródło DDL) musi zostać
nazwą domeny, inaczej kompilacja podmieniłaby domenę na jej rozwinięcie. Pinowane osobnym testem.
Druga pułapka: sync przejmował `NotNull` **domeny**, nadpisując wartość z deklaracji → parametr
`adoptNotNull` (wybór ręką = tak, wczytanie = nie).

---

## 3. ⏸ Otwarte drobiazgi — wziąć po drodze, nie jako osobne iteracje

1. ⭐ **Wysokość edytorów w siatkach pól ZAMKNIĘTA** (§19.9) — `TextBox` zrównany z `ComboBoxem`
   (12 → 24 px, wiersz bez zmian), chroma przeniesiona z kodu do stylu, pinowane testem
   porównującym obie kontrolki **ze sobą**, nie z liczbą.
2. **Wyłączone komórki Size/Scale/SubType/Charset** dostały `Stretch` (wypełniają wiersz), ale
   ⚠ **tło nadal maluje `FluentBridge`** — `TextControlBackgroundDisabled` → `BackgroundColor`, więc
   setter `Background="Transparent"` go nie zdejmuje. **Jeśli po QA nadal widać pudełko, trasa jest
   przez Bridge, nie przez setter** (reguła 8 §16). Zapis: §19.8.4.
3. **QA wizualne trzech ostatnich poprawek** (domeny · wygląd wyłączonych komórek · wysokość edytorów) —
   użytkownik nie oglądał ich na żywo.

## 4. ⏸ Do przeglądu §13.3 — nie ruszać wcześniej

* **Semantyka kolorów aktywności** (SQL / Debugger / Trace / Import — każdy własny kolor). Odłożone
  **dwukrotnie** przez użytkownika, bo wymaga kompletu źródeł, który daje dopiero M3b. §19.4.4 + §19.6.2.
* **`TransactionActiveBrush` w Light = 4,18:1** — poniżej progu §10 dla tekstu. Token współdzielony;
  precedens V‑1 (4,14:1) ratyfikowany do pozostawienia. §19.6.3.
* **Brak strażnika kontrastu w całym repo** — §10 stawia progi, nic ich nie sprawdza. Użytkownik uznał
  za dobry pomysł, ale **osobną pracę infrastrukturalną**. §19.6.3.
* **Wspólna metryka chipów i pasków postępu** — trzy wartości lokalne z powodem: padding badge'a DEV
  MODE (§19.3.4), `Spacing` chipa = **K11** w §18.R, grubość paska postępu 4 px (§19.7.6).
* **⭐ DWIE równoległe implementacje wiersza pola** — `FieldRowViewModel` (tabele) vs
  `ProcedureFieldRowBase` (procedury/funkcje/triggery). Osobne klasy, osobne kolumny, osobna obsługa
  asynchronicznych domen. Użytkownik: *„odnotuj jako dług, nie rozszerzaj zakresu M3.2a"*. §19.8.5.
* **Rejestr kolizji §18.R: K1–K11** — rozstrzygane RAZEM, na pełnym obrazie. ⛔ Katalog zamrożony.

---

## 5. ⭐⭐ MIEJSCE STARTU: M3.2a (H‑3) — stabilny układ

### 5.1 Stan wejściowy — ZMIERZONY w iteracji 0, nie wymaga ponownej analizy

⚠ **To są DWA różne paski i drugi jest znacznie gorszy.**

| Pasek | Gdzie | Bramki | Mechanizm przesunięcia |
|---|---|---|---|
| **Pasek tytułu** | `MainWindow.axaml`, wysokość stała 36 | ⭐ **została JEDNA: `CanExportDdl` (×2)** | `ColumnDefinitions` — kolumna rośnie i przesuwa sąsiadów **poziomo** |
| ⚠⚠ **Toolbar dokumentu** | `MainWindow.axaml` (~`:868–1230`) | **72 bramki `IsVisible`** | niemal wyłącznie `IsXxxDetailTabActive` — **przełączenie zakładki przebudowuje zawartość paska** |

⭐ **M3.1b zmniejszyło problem paska tytułu z trzech przyczyn do jednej** — nazwa połączenia i DEV MODE
przeniosły się do paska statusu (§19.3.2). Zostaje `CanExportDdl`.

⚠ Opis audytu (*„górny toolbar przesuwa się"*) jest prawdziwy co do faktu, ale **mylący co do osi**:
przesunięcie jest **poziome**, nie pionowe — pasek tytułu ma stałe 36 px.

⭐ **Najczęściej odczuwany przypadek to jednak toolbar dokumentu.** 72 bramki w jednym poziomym
`StackPanelu` oznaczają, że przy każdej zmianie rodzaju zakładki przyciski lądują gdzie indziej.

### 5.2 ⛔ To jest PYTANIE PROJEKTOWE, nie poprawka

*Czy pasek ma stałe kotwice sekcji, czy przepływa?* — rozstrzygnięcia **nie ma** i nie wolno go podjąć
po cichu. **Przedstaw propozycję użytkownikowi PRZED implementacją** (procedura §6/3 handovera; przy
powierzchniach trwałych ten krok jest obowiązkowy w każdej iteracji).

Warto w propozycji pokazać: ile sekcji logicznych da się wyodrębnić z tych 72 bramek, co jest wspólne
dla wszystkich rodzajów zakładek, a co swoiste — i **czy stałe kotwice nie zostawią pustych dziur** przy
zakładkach o ubogim zestawie poleceń.

### 5.3 Pozostałe podetapy M3.2 (kolejno po M3.2a)

| # | Zakres | Decyzja |
|---|---|---|
| M3.2b | §7.5 — semantyka kolorów na pasku narzędzi | **DC**: likwidacja `AccentIconBrush`/`InfoIconBrush` **odłożona do M4.3/M5** (sięga 24 wystąpień w 14 plikach) |
| M3.2c | H‑5 — Commit / Rollback | **DD**: **przechodzą** na `CommitButtonBrush`/`RollbackButtonBrush`. ⚠ Audyt nazwał zły moduł: drugim jest **Data Import**, nie Script Executor. Oba tokeny **nie mają dziś ani jednego konsumenta** |
| M3.2d | M‑1 — literały → `UiStrings` | 10 sztuk (7 toolbar połączeń + 3 przyciski okna); 1 idzie do M3.3, 2 zostają poza M3 |

---

## 6. Obowiązkowa kolejność (bez skrótów)

```
analiza → propozycja (AKCEPTACJA) → implementacja → uruchomienie aplikacji + QA w obu motywach
  → dotnet build (0/0)
    → dotnet test (TRZY partycje, OSOBNO)
      → smoke
        → dokumentacja (product-polish.md §19)
          → commit (kod + opis iteracji razem)
            → push na oba remote'y — WYŁĄCZNIE po akceptacji użytkownika
```

⚠⚠ **Nigdy nie łącz `dotnet build` i `dotnet test` w jednym poleceniu — deadlock.**
⚠ **Zabij `EmberTern.exe` przed przebudową** — inaczej MSB3021/MSB3027.

**Trzy partycje** (⚠ `ConnectionExpandBindingProbe` biegnie **sam** — zawiesza się dołączony):

```
--filter "FullyQualifiedName!~ConnectionExpandBindingProbe&FullyQualifiedName!~SettingsCenterViewTests&FullyQualifiedName!~BrandingPresentationTests&FullyQualifiedName!~DesignTokenApplicationTests&FullyQualifiedName!~TabStripPresentationTests"
```
oraz odwrotność z `|`, oraz `ConnectionExpandBindingProbe` osobno. Stan: **7031 + 48 + 54 = 7133**.

⚠⚠ **Filtr jest listą nazw i STARZEJE SIĘ CICHO.** Kryterium: **czy klasa konstruuje kontrolki
Avalonii.** Jeśli tak — dopisz ją do filtra.

---

## 7. ⛔ Czego M3.2a NIE robi

1. ⛔ Nie cofa `FluentBridge` ani żadnej decyzji architektonicznej M2b.
2. ⛔ Nie rozszerza katalogu, żeby domknąć kolizję — K1–K11 czekają na §13.3.
3. ⛔ Nie wprowadza wartości lokalnej **bez uzasadnienia w miejscu**.
4. ⛔ Nie rusza ikony debuggera (§2.2) ani wyrównania endpointu (§2.4) — oba **zamknięte**.
5. ⛔ Nie ujednolica dwóch implementacji wiersza pola (§4) — dług do §13.3.
6. ⛔ Nie likwiduje `AccentIconBrush`/`InfoIconBrush` — decyzja DC, M4.3/M5.
7. ⛔ Nie naprawia przy okazji rzeczy spoza zakresu — **mierz, opisz, zapisz, nie rozwiązuj bez decyzji**.

---

## 8. ⭐⭐ Trzy reguły, które w tej sesji kosztowały najwięcej

1. **SPÓJNOŚĆ ZESTAWU BIJE OPTIMUM POJEDYNCZEGO ELEMENTU.** Wariant wygrywający w każdej mierzonej
   liczbie może przegrać w wymiarze, którego nie mierzysz. Zanim „poprawisz" element należący do
   rodziny — sprawdź, z czym tworzy rodzinę, i policz koszt po tamtej stronie.
2. **OBSERWACJA UŻYTKOWNIKA O OBJAWIE JEST WIARYGODNA; JEGO WNIOSEK O PRZYCZYNIE I ZASIĘGU — DO
   ZMIERZENIA.** Trzy razy z rzędu przyczyna była starsza i szersza niż wskazywana.
3. **„WŁAŚCIWOŚĆ ISTNIEJE W API" ≠ „WŁAŚCIWOŚĆ DZIAŁA".** Sprawdzenie obecności API daje **fałszywe
   potwierdzenie**. Mierz efekt. Bezczynna właściwość to martwy kod udający poprawkę.

> **Reguła prowadząca całego etapu (użytkownik):** *„Dokument ma prowadzić produkt. Nie produkt
> dokument."* · *„Pomiar nadal jest obowiązkowy. Ale nie jest już celem. Jest tylko narzędziem."*
