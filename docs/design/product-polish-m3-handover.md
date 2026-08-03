# Product Polish — M3 — DOKUMENT STARTOWY (jedyny punkt wejścia)

> **To jest prompt dla Claude'a, nie dla użytkownika.** Wchodzisz we **właściwe M3**: iteracja 0
> (pomiar) jest **ZAKOŃCZONA** — jej pełny zapis to `product-polish.md` **§19.0**. Ten plik jest
> **kompletny**: stan, reguły, plan, procedura i pułapki. Do dokumentu etapu sięgasz po **konkretną**
> sekcję, nie po całość.

---

## 0. Co przeczytać i w jakiej kolejności

| # | Dokument | Status | Kiedy |
|---|---|---|---|
| 1 | **ten plik** | ⭐ **wiążący, punkt wejścia** | zawsze, w całości |
| 2 | `product-polish.md` **§19** | ⭐ **wiążący** — as-built M3, wynik iteracji 0 | zawsze |
| 3 | `product-polish.md` **§8** | ⭐ **wiążący** — model paska zakładek (§8.0–§8.3) i Status Bara 2.0 (§8.4) | M3.1 i M3.3 |
| 4 | ⭐⭐ **`color-language.md`** | 🔒 **WDROŻONY W CAŁOŚCI (2026-08-03) — od teraz dokument REFERENCYJNY, nie plan.** Jedyne źródło prawdy o kolorach; dodajesz przycisk → bierzesz mu rolę z **§6**, a przed każdą zmianą koloru przechodzisz **§0.5**. ⛔ Nie ma tu nic do „dokończenia". ⚠ `product-polish.md` §7.5 **NIE OBOWIĄZUJE** — zastąpione | **przy każdej nowej akcji/ikonie**; §0.5 przy każdej zmianie koloru |
| 5 | `product-polish.md` **§13.3** | ⭐ wiążący — brama jakości po M3 | przed zamknięciem etapu |
| 6 | `product-polish.md` **§17** + **§18.R** | ⭐ wiążący — reguły **R1–R14** (R13 i R14 ratyfikowane w M3.2), rejestr kolizji **K1–K11** | zawsze |
| 7 | `Themes/Tokens.axaml`, `Themes/Typography.axaml` | ⭐ **katalog — źródło prawdy o rolach** | przy każdej iteracji |

⛔ **NIE czytaj na starcie:**
* `product-polish.md` **§15** (21 iteracji M2b) i **§18.1–§18.11** (9 iteracji M2c) — sięgaj po
  **konkretną podsekcję**, gdy dotyczy tego, co właśnie robisz.
* `product-polish-m2a-handover.md`, `product-polish-m2b-handover.md` — **ZAMKNIĘTE, historyczne**.
* `product-polish-m2c-handover.md` — **ZAMKNIĘTY**, ale jego §8 (16 pułapek) jest wciąż aktualny
  i został tu przeniesiony w skrócie. Nie czytaj całości.

**Specyfikacja etapu (nadrzędne źródło):** `C:\Users\grzegorz.gronski\Desktop\Product Polish.mdown`

---

## 1. Stan projektu

| | |
|---|---|
| **Branch** | `feat/product-polish` |
| **Ostatni commit** | dokumentacyjne domknięcie M3.3. ⚠ **Sprawdź `git log --oneline -1` zamiast wierzyć temu wierszowi** — hasze starzeją się tu najszybciej |
| **Etap** | M0–M2c ✅ · **M3: iteracja 0 ✅ · M3.1 ✅ · M3.2 ✅ · 🔒 JĘZYK KOLORÓW ✅ · 🔒 M3.3 PASEK ZAKŁADEK ✅ ODEBRANY 2026-08-03** (§19.22–§19.25). ⭐ **M3.1 ZAMKNIĘTE** · ⭐ **H‑3 ZAMKNIĘTE** · ⭐ **H‑5 ZAMKNIĘTE** (K7) · ⭐ **§7.5 ZAMKNIĘTE** — zastąpione przez `color-language.md` · ⭐ **M‑1 ZAMKNIĘTE wewnątrz M3** (zostały 2 literały, oba w M4.3) |
| **Decyzje DA–DD** | ⭐ **rozstrzygnięte 2026-08-02** — DA: katalog (28 → 24) · **DB: wiersz drzewa ZOSTAJE 24** · DC: likwidacja `AccentIconBrush`/`InfoIconBrush` **odłożona do M4.3/M5** · DD: Commit/Rollback **przechodzą** na `CommitButtonBrush`/`RollbackButtonBrush` |
| **Build** | 0 błędów / 0 ostrzeżeń |
| **Suite** | **7243**, zielony w trzech partycjach (**7132 + 57 + 54**). ⚠ Ten wiersz podawał kiedyś **7138 (7032 + 52 + 54)** — wartość sprzed rundy poprawek odbiorczych (`85c8747`, §21). **Mierz przed cytowaniem** |
| **Smoke** | czysty |
| **Drzewo** | czyste |
| ⭐⭐ **NASTĘPNY KROK** | **M3.4a — Metadata Explorer, wiersz drzewa** (pozycja 15 w planie §10). ⚠ Niesie decyzję **DB**, już rozstrzygniętą: **wiersz ZOSTAJE 24** (`Size.Row.Tree` = 20 to zamiar katalogu, nie opis; zejście do 20 zmieniałoby gęstość najgęstszego widoku aplikacji i wymaga oka użytkownika). ⭐ **§0.1 stawia tę powierzchnię wysoko** — użytkownik patrzy na nią cały dzień. Potem M3.4b → M3b → ⛔ brama §13.3 |
| 🔒 **PASEK ZAKŁADEK ZAMKNIĘTY** | M3.3a + M3.3b + M3.3c **dostarczone i odebrane** (§19.25). ⛔ Nie wracać do niego bez realnego defektu funkcjonalnego |
| **⚠ TRYB PRACY** | ⭐ **R15** (§5): wielkość iteracji idzie za **niepewnością**. ⭐⭐ **Wzorzec z M3.3, potwierdzony przez użytkownika:** podetapy o ustalonej architekturze **idą bez przerwy na odbiór**, a przystanek jest **po całej powierzchni**. Powrót do użytkownika wcześniej: dokument nie rozstrzyga · realny konflikt projektowy · zmiana pogorszyłaby produkt mimo zgodności · decyzja produktowa (np. gęstość, widoczność opcji) |
| ⭐ **PRZED KAŻDYM PODETAPEM** | ⚠⚠ **Sprawdź w KODZIE, czy przedmiot podetapu jeszcze istnieje.** M3.3a wszedł z zakresem, który M3.1a już dostarczyła — **plan etapu starzeje się tak samo cicho jak string i jak komentarz** (#284, pułapki 20/21). Kosztowało to jedną iterację; drugi raz nie musi (§19.22.1) |
| ⛔ **BRAMKA KOLORU** | Przy **każdym** dotknięciu koloru obowiązuje `color-language.md` **§0.5**: *czy użytkownik rozpozna akcję SZYBCIEJ?* „Nie" albo „nie wiadomo" ⇒ zatrzymaj się i wróć z propozycją |
| **⛔⛔ BRAMKA NADRZĘDNA** | **`color-language.md` §0.5** (ratyfikowana 2026-08-03): **przed każdą zmianą koloru odpowiedz, czy użytkownik dzięki niej SZYBCIEJ ROZPOZNA AKCJĘ.** „Nie" albo „nie wiadomo" ⇒ ⛔ zatrzymaj się i wróć z propozycją. Stoi **przed** §6 i przed R14: zgodność z rolą jest warunkiem koniecznym, nigdy wystarczającym, a *„teraz jest zgodne z językiem"* **nie jest odpowiedzią na to pytanie** — to było jedyne uzasadnienie M3.2b. ⭐ Jeśli reguła pogarsza UX — poprawiamy **regułę w dokumencie**, nie bronimy implementacji |
| **⏸ ZAMKNIĘTE PRZEZ R13** | Dług „sekcja 3 toolbara drga przy przełączaniu pod-zakładek" (§19.10.3) **nie wymaga już decyzji** — R13 rozstrzyga go z góry: nie rezerwujemy miejsca na element, którego w danym kontekście nie będzie. Sekcja 3 zostaje jak jest |
| **⏸ DROBIAZG DO WZIĘCIA PO DRODZE** | Wyłączone komórki Size/Scale/SubType/Charset dostały `Stretch`, ale **tło nadal maluje `FluentBridge`** (`TextControlBackgroundDisabled` → `BackgroundColor`), więc setter `Background="Transparent"` go nie zdejmuje. Jeśli po QA nadal widać pudełko — trasa jest przez **Bridge**, nie przez setter (reguła 8 §16). Zapis: §19.8.4 |

### 1.1 Co dostarczyły poprzednie etapy — trzy zdania

* **M2a** zbudowało **katalog** (`Tokens.axaml`, `Typography.axaml`) + strażnika `DesignTokenComplianceTests`
  w kształcie zapadki licznikowej. Zero zmian wizualnych.
* **M2b** **włączyło katalog dla kontrolek bazowych** — 21 iteracji, wzorzec `FluentBridge`,
  cztery decyzje architektoniczne, reguły R1–R11.
* **M2c** **usunęło to, co katalog blokowało** — `FontSize` 605 → 43, `CornerRadius` 37 → 19,
  62 świadome wyjątki z powodem w miejscu, R12. Aplikacja wygląda identycznie.

### 1.2 ⭐ Zakres M3 — ratyfikowany przez użytkownika 2026-08-02

| Podetap | Zakres |
|---|---|
| **M3.1** | **Status Bar 2.0** (§8.4) — rytm pionowy chromy, rail, cztery sekcje, hierarchia, chip transakcji, chipy stanu, sekcja postępu + **jedna** operacja referencyjna |
| **M3.2** | **Toolbar** — stabilny układ (H‑3), semantyka kolorów (§7.5), Commit/Rollback (H‑5), literały (M‑1) |
| **M3.3** | ✅ **ZAMKNIĘTE** — **Pasek zakładek**: dwa tryby, limit wierszy, menu kontekstowe (D5–D9) + wiersze w Settings Center |
| ⭐ **M3.4** | **Metadata Explorer** (§0.1) + przegląd menu kontekstowych ← **TU JESTEŚMY** |
| **M3b** | **Podłączenie wszystkich pozostałych operacji** do infrastruktury paska postępu (D4) |
| **brama** | ⛔ **§13.3** — przegląd czterech powierzchni **jednocześnie**, na żywej bazie, w obu motywach |

Po bramie: **jedno podsumowanie zamykające cały etap** + dokumentacja + handover M4.

---

## 2. ⭐⭐ Po co jest M3 — teza etapu

> **M2b włączył system dla kontrolek. M2c usunął to, co go blokowało. M3 stosuje go po raz pierwszy
> do POWIERZCHNI, których użytkownik nie zamyka.**

Zasada §0.1 (Persistent UI) mówi, że Status Bar, Toolbar, pasek zakładek i Metadata Explorer biją
ekrany otwierane raz dziennie. §0.1.2 dodaje, że to **jedna powierzchnia — Application Chrome — a nie
cztery komponenty**. Iteracja 0 zmierzyła, w jakim stopniu to dziś nieprawda, i odpowiedź jest
mocniejsza, niż zakładał dokument (§3.1).

### 2.1 ⛔ Czym M3 różni się od M2c — warunek odbioru jest ODWRÓCONY

M2c miał DoD *„aplikacja wygląda identycznie"*. **M3 ma zmienić wygląd czterech powierzchni trwałych
i dodaje funkcjonalność** (sekcja postępu, dwa tryby paska zakładek + dwie preferencje, menu
kontekstowe zakładki). To nie jest praca wyłącznie stylistyczna — i dlatego obowiązują dwie rzeczy,
których M2c nie potrzebował:

1. ⚠⚠ **Reguła #11 (nigdy nie trać informacji) wchodzi do zakresu.** Trzy pozycje menu kontekstowego
   zakładki zamykają wiele dokumentów naraz. **Każda musi przejść przez istniejącą bramkę
   Save / Discard / Cancel** — nie obok niej (§8.3). Bramka istnieje i ma dziś **trzy** wejścia;
   menu jest czwartym.
2. ⚠ **Zmiana schematu preferencji jest ADDYTYWNA — `CurrentSchemaVersion` ZOSTAJE 2** (R‑4).
   Bump uruchamia downgrade protection i starsze buildy odrzucą **cały** plik ustawień.

---

## 3. Wynik iteracji 0 — co wiesz przed pierwszą linią kodu

> Pełny zapis z dowodami: `product-polish.md` **§19.0**. Poniżej to, co zmienia decyzje.

### 3.1 ⭐⭐ ZNALEZISKO GŁÓWNE — rytm pionowy Application Chrome nie istnieje w aplikacji

| Powierzchnia | Katalog (M2a) | Rzeczywistość | Konsument tokenu |
|---|---|---|---|
| Pasek tytułu | `Size.TitleBar` **36** | **36** — literał w `RowDefinitions="36,Auto,*,28"` | tylko `Button.caption` (`Height`) |
| Pasek zakładek | `Size.Row.Tab` **26** | **brak deklaracji** — wysokość wynika z treści | ⛔ **zero** |
| Pasek statusu | `Size.StatusBar` **24** | ⚠ **28** — literał | ⚠ tylko `DataImportTabView` |
| Wiersz drzewa | `Size.Row.Tree` **20** | ⚠ **24** — `ListBoxItem.MinHeight` | ⛔ **zero** |
| Wskaźnik zakładki | `Size.TabIndicator` **2** | **2** — literał `RowDefinitions="2,*"` | ⛔ **token nie istnieje** |

**Dwie z czterech liczb są niezgodne z rzeczywistością, a trzy tokeny nie mają ani jednego konsumenta.**
⚠⚠ Najostrzejszy pojedynczy fakt: **`Size.StatusBar` konsumuje belka Data Importu, a nie pasek statusu
aplikacji.**

⭐ **Dlaczego M2c tego nie złapał — i to nie jest zarzut wobec M2c.** Liczniki M2c to `FontSize`,
`CornerRadius` i `FontFamily`. **Wysokości nigdy nie były w żadnym liczniku.** Sweep de-lokalizacyjny
przeszedł przez `MainWindow.axaml` (iteracja 7, 33 → 0) i te literały minął, bo nie należały
do jego przedmiotu.

⚠ **Konsekwencja dla bramy §13.3.** Pytanie kontrolne nr 2 brzmi: *„Czy rytm pionowy (36 / 26 / 24)
czyta się jako hierarchia, czy jako trzy przypadkowe wysokości?"* — **dziś ten rytm nie został ani
razu zastosowany.** M3 jest etapem, w którym powstaje po raz pierwszy; brama go ocenia, a nie weryfikuje.

### 3.2 Dwa otwarte pomiary z §8 — oba rozstrzygnięte NA TAK

| Pomiar | Zapis w dokumencie | Wynik iteracji 0 |
|---|---|---|
| Czas trwania transakcji (§8.4.5) | *„do sprawdzenia w M3; jeśli nie — chip pokazuje sam stan"* | ✅ **da się, tanio, bez `MON$` i bez zmian w Core/Firebird** |
| `IconColor_Query` na rail Trace (§8.4.2) | *„(do weryfikacji)"* | ✅ token istnieje w obu motywach |

**Czas transakcji — mechanizm.** `TransactionService` **nie ma** znacznika czasu (ani `DateTime`, ani
`Stopwatch`). Ale ma zdarzenie `TransactionStateChanged`, które `MainWindowViewModel`
**już subskrybuje** (`:288`, handler `:7270`). Chip mierzy czas **sam, w warstwie App**, zapamiętując
moment przejścia Idle → Active. ⭐ Zero zapytań do serwera, zero round-tripów, zero zmian w Core
i Firebird. Rezerwowy wariant z §8.4.5 („chip pokazuje sam stan") **nie jest potrzebny**.

### 3.3 ⚠⚠ RYZYKO SPOZA DOKUMENTU — chipy stanu nie mają dziś źródła danych

§8.4.3 chce w sekcji 3 chipów **transakcji, Trace i Debuggera**, a §8.4 definiuje czas życia chipa jako
*„trwa, dopóki warunek jest prawdziwy"*. Zmierzone:

| Sygnał | Co naprawdę znaczy |
|---|---|
| `IsTraceMonitorTabActive`, `IsDebuggerTabActive` | ⚠ **„ta zakładka jest wybrana"** — nie „to działa" |
| `TraceMonitorTabViewModel.State` (`TraceSessionState`) | stan faktyczny — ale **na VM zakładki** |
| `DebuggerTabViewModel.Phase` (`DebuggerPhase`) | stan faktyczny — ale **na VM zakładki** |
| agregacja po `WorkspaceTabs` w `MainWindowViewModel` | ⛔ **nie istnieje** — tylko indeksy i `Count` |

⭐ **Istniejący wzorzec się NIE generalizuje.** Pasek statusu pokazuje dziś `ActiveDebugger.StatusText`
i działa wyłącznie dlatego, że dotyczy zakładki **aktywnej** — Avalonia subskrybuje `PropertyChanged`
wzdłuż ścieżki wiązania. Chip ma być prawdziwy, gdy sesja trwa **na innej zakładce**, więc potrzebuje
nowej agregacji **i** ścieżki powiadomień. To realna praca w M3.1, addytywna i wyłącznie w warstwie App.

### 3.4 🔒 §7.5 — ZASTĄPIONE przez `color-language.md`, wdrożone w K1–K7. Zapis wejściowy poniżej

> ⛔ **Tabela „zmiany do wykonania" z §7.5 NIE OBOWIĄZUJE** — M3.2b wykonało ją co do litery i zostało
> wycofane w całości. Zastąpił ją język kolorów, wdrożony i odebrany 2026-08-03 (§19.20).
> ⭐ Stan po wdrożeniu: `Icon.Trash` → 🔴 · `Icon.PlugZap` → ⚪ · `Icon.RefreshCw` → ⚪ ·
> Security Manager → `AccentBrush` (**7 × `AccentBrush`** w pasku, jedna rodzina R‑6).
> ⏸ Ostatni wiersz §7.5 (likwidacja `AccentIconBrush`/`InfoIconBrush`) — decyzja **DC**, → M4.3/M5;
> ⚠ oba tokeny **nadal mają konsumentów** i nie są sierotami.

Zmierzone **przed wdrożeniem** w pasku tytułu: **6 × `AccentBrush`** (narzędzia ogólne) ·
`Icon.Trash` → `WarningIconBrush` · `Icon.PlugZap` → `AccentIconBrush` ·
`Icon.RefreshCw` → `InfoIconBrush` · **10 × `IconColor_*`**. **Liczby zgadzały się z audytem dokładnie.**

* **Uściślenie 1 (kosmetyczne):** „10 przycisków *Nowy X*" to w rzeczywistości **9 kreatorów + 1 narzędzie**
  (Security Manager, `IconColor_Role`). Reguła §7.5 obejmuje oba tak samo — zmienia się opis, nie wniosek.
* **⚠ Uściślenie 2 (zakresowe, wymaga decyzji):** ostatni wiersz §7.5 — *„`AccentIconBrush`, `InfoIconBrush`
  → **zlikwidowane**"* — wygląda na zmianę dwóch linii, a jest zmianą w **24 wystąpieniach / 14 plikach**:
  `SvgIcon.cs`, `DebuggerIcon.cs`, `NavigationController.cs`, **trzy ViewModele trzymające klucz jako string**
  oraz widoki Data Import, Debugger, Performance, Table Detail i Trace Monitor — czyli powierzchnie **M4.3**.

### 3.5 🔒 H‑5 — ZAMKNIĘTE w K7 (2026-08-03). Zapis wejściowy poniżej

> ✅ **Wykonane:** Commit i Rollback stoją na własnych tokenach `CommitButtonBrush` /
> `RollbackButtonBrush`, którym najpierw nadano **wartości per motyw** (§19.17.4) — krok wyszedł
> **neutralny wizualnie**. Poniższy pomiar zostaje jako opis stanu wejściowego.

Audyt: *„titlebar `Button.icon`+`SvgIcon`; **Script Executor** `Button.flat`+tekst"*.

**Zmierzone: Script Executor nie ma przycisków Commit/Rollback.** Drugim modułem jest **Data Import**.
I różnica jest węższa, niż opisano — **oba używają tych samych ikon** (`Icon.Check` / `Icon.Undo`)
**i tych samych pędzli** (`SuccessIconBrush` / `DangerIconBrush`). Różni je wyłącznie wariant przycisku,
a to jest **zgodne z decyzją architektoniczną 4**: chroma niesie ikonę, pasmo raportu niesie etykietę,
wariant niesie kolor, kontekst niesie rozmiar.

⭐⭐ **Prawdziwy defekt:** §7.5 przypisuje Commit → `CommitButtonBrush`, Rollback → `RollbackButtonBrush`.
Oba tokeny są zdefiniowane w **obu** motywach i **nie mają ani jednego konsumenta w całej aplikacji**.
Rollback maluje się dziś `DangerIconBrush` — tokenem kategorii *„operacje nieodwracalne: Drop, Delete,
Stop"*. **Rollback nie jest nieodwracalny w tym sensie**, a §7.5 rozdziela te dwie kategorie celowo.

### 3.6 H‑3 — potwierdzone, ale to DWA różne paski i drugi jest znacznie gorszy

| Pasek | Gdzie | Bramki | Mechanizm przesunięcia |
|---|---|---|---|
| **Pasek tytułu** | `MainWindow.axaml:44–367`, wysokość stała 36 | `HasActiveConnection` (blok), `IsDeveloperModeActive` (×2), `CanExportDdl` (×2) | `ColumnDefinitions="Auto,Auto,*,Auto,Auto"` — kolumna 0 rośnie po połączeniu i **przesuwa poziomo całą kolumnę 1** |
| ⚠⚠ **Toolbar dokumentu** | `MainWindow.axaml:868–1230` | **72 bramki `IsVisible`** | niemal wyłącznie `IsXxxDetailTabActive` — **przełączenie zakładki przebudowuje zawartość paska** |

⚠ Opis audytu (*„górny toolbar przesuwa się"*) jest prawdziwy co do faktu, ale mylący co do osi:
przesunięcie jest **poziome**, nie pionowe — pasek tytułu ma stałe 36 px.
⭐ **Najczęściej odczuwany przypadek to jednak toolbar dokumentu:** 72 bramki w jednym poziomym
`StackPanelu` oznaczają, że przy każdej zmianie rodzaju zakładki przyciski lądują gdzie indziej.
To jest pytanie projektowe M3.2 — *czy pasek ma stałe kotwice sekcji, czy przepływa* — a nie poprawka.

### 3.7 M‑1 — 13 literałów, rozkład na podetapy

| Gdzie | Ile | Podetap |
|---|---|---|
| `MainWindow.axaml` — toolbar połączeń | 7 | **M3.2** |
| `MainWindow.axaml` — przyciski okna (Minimize / Maximize / Close) | 3 | **M3.2** |
| `MainWindow.axaml` — „Close tab" | 1 | **M3.3** |
| `PerformancePanelView`, `SessionManagerTabView` | 2 | ⛔ **poza M3** (M4.3) |

R‑7 przypisała M‑1 w całości do M3.2. Zmierzone: **10 tam trafia, 1 do M3.3, 2 zostają poza etapem.**

✅ **M‑1 ZAMKNIĘTE wewnątrz M3.** M3.2d zdjęło 10 (13 → 3), M3.3a zdjęło ostatni literał paska zakładek
(„Close tab" — stała `UiStrings.TabCloseTooltip` **już istniała**, była tylko nieużywana). **Zostały 2**,
oba świadomie poza etapem: `PerformancePanelView` i `SessionManagerTabView` → **M4.3**.

### 3.8 Co już istnieje i czego NIE trzeba budować

| Potrzeba | Stan |
|---|---|
| Bramka Save / Discard / Cancel dla zamykania | ✅ **CZTERY wejścia od M3.3c**: zamknięcie zakładki, rozłączenie, zamknięcie aplikacji, zamykanie masowe z menu. ⭐ Jej trzy metody pomocnicze przyjmują **ZASIĘG** (`scope == null` = wszystkie) — kolejne wejście na podzbiorze nie wymaga już żadnej zmiany |
| Lista z filtrowaniem dla trybu pojedynczego wiersza (§8.2) | ✅ `Controls/SearchableComboBox.cs` |
| Odświeżenie zakładki (pozycja menu) | ✅ `WorkspaceTabViewModel.RefreshAsync()` (Seam 6d) |
| Style `ContextMenu`/`MenuItem`, `{app:MenuIcon}`, `{app:CommandGesture}` | ✅ Keyboard Manager etap 5 — **zero nowej chromy** |
| Mapowanie severity → pędzel + ikona | ✅ `MessageBanner.BrushKeyFor` / `GeometryKeyFor` (publiczne statyki) |
| Wzorzec preferencji numerycznej (`TabStripMaxRows`) | ✅ `PreferenceRange`, commit na blur/Enter, digits-only na tunelu — `settings-center.md` §17.4/§17.4a |

### 3.9 M3b — inwentarz operacji

**16 ViewModeli** ma własny stan „trwa operacja" (`IsRunning`/`IsBusy`/`IsExecuting`/`IsLoading`).
**Trzy realne ścieżki `IProgress`**: eksport (`Export/ExportService.cs`), wykonanie zapytania
(`MainWindowViewModel:3456`), batch (`:5395`). **Trzy `ProgressBar`** w widokach: `BatchResultsDialog`,
`DataImportTabView`, `ExportDialog`.

⚠ M3.1 dostarcza **sekcję i JEDNĄ operację referencyjną** (wykonanie zapytania SQL — najlepiej
oprzyrządowana). M3b podłącza resztę. Powód rozdzielenia (D4) pozostaje aktualny.

---

## 4. ⛔ Cztery decyzje do podjęcia PRZED implementacją

Iteracja 0 znalazła cztery rozstrzygnięcia, których **nie wolno podjąć po cichu w trakcie**.
Zapisane tutaj, żeby były zadane raz i we właściwym momencie.

| # | Pytanie | Kiedy | Rekomendacja |
|---|---|---|---|
| **DA** | `Size.StatusBar` = 24, rzeczywistość = **28**. Zastosować katalog (28 → 24) czy poprawić katalog (24 → 28)? | **przed M3.1** | zastosować katalog — §8.5 specyfikacji zabrania *wzrostu*, zmniejszenie jest dozwolone, a 36/26/24 to ratyfikowany rytm |
| **DB** | `Size.Row.Tree` = 20, rzeczywistość = **24**. To samo pytanie, ale zmiana dotyka **najgęstszego widoku aplikacji** | **przed M3.4** | ⚠ **wymaga oka użytkownika** — 24 → 20 to realna zmiana gęstości drzewa, nie porządkowanie |
| **DC** | Likwidacja `AccentIconBrush` / `InfoIconBrush` (§7.5) sięga **14 plików**, w tym powierzchni M4.3 | **przed M3.2** | ograniczyć M3.2 do paska narzędzi; likwidację tokenów przenieść do M4.3/M5 **z zapisem powodu** |
| **DD** | Czy Commit/Rollback przechodzą na `CommitButtonBrush` / `RollbackButtonBrush` (§7.5), czy zostają na `SuccessIconBrush` / `DangerIconBrush`? | **przed M3.2** | przejść — dziś Rollback nosi kolor „operacji nieodwracalnej", a nią nie jest; to zmiana wyglądu, więc decyzja użytkownika |

---

## 5. Reguły obowiązujące — R1–R17

⛔ **Zmienia je wyłącznie użytkownik. Nie otwierać ponownie.**

| # | Reguła |
|---|---|
| R1 | *„Projektujemy kontrolki, na których programista pracuje komfortowo 8 godzin dziennie"* |
| R2 | Komponent ocenia się w **komplecie stanów** i w **obu motywach** |
| R3 | Nowa **rola** powstaje z użycia w kilku komponentach, nigdy z jednego przypadku |
| R4 | **`FluentBridge` nie jest drugim katalogiem tokenów** — wyłącznie mapowanie |
| R5 | **Kolor może określać priorytet akcji, ROZMIAR NIE** |
| R6 | **Ustawienia są panelem referencyjnym** |
| R7 | **Nie łatać pojedynczych ekranów** — najpierw reguła Design Systemu |
| R8 | **Kryterium odbioru: „czy wygląda to jak dopracowana aplikacja komercyjna?"** Pomiar jest narzędziem, nie argumentem końcowym |
| R9 | **Domain Picker** — nie ujednolicać szerokości |
| R10 | **Kolor komentarzy SQL zostaje** (V‑1) |
| R11 | **`Size.Row.Grid`** to osobna decyzja produktowa |
| R12 | ⭐ **Celem jest usunięcie NIEUZASADNIONYCH wartości lokalnych, nie wyzerowanie licznika**; **błędna rola jest gorsza od wartości lokalnej** |
| **R13** | ⭐⭐ **NIE REZERWUJEMY MIEJSCA NA ELEMENT, KTÓRY W DANYM KONTEKŚCIE NIGDY SIĘ NIE POJAWI.** Stabilizacja układu ma sens **tylko wtedy, gdy nie pogarsza wykorzystania przestrzeni** — pusta dziura czyta się jako błąd układu, a niewielkie przesunięcie nie. Ratyfikowana 2026-08-02 na odbiorze M3.2a (§19.12) |
| **R14** | ⭐⭐ **KAŻDY KROK MUSI BYĆ EWIDENTNYM ULEPSZENIEM UX SAM W SOBIE.** *„Wolę pięć małych, oczywistych poprawek niż jedną dużą rewolucję."* ⛔ Krok, którego jedynym uzasadnieniem jest *„teraz jest zgodne z regułą"*, jest **zły** — ryzyko niezerowe, zysk zerowy. ⚠ Nie dotyczy kroków **neutralnych wizualnie**. ⚠⚠ **Jako reguła TEMPA zastąpiona przez R15** (2026-08-03); jako kryterium POJEDYNCZEJ zmiany obowiązuje dalej |
| **R15** | ⭐⭐ **WIELKOŚĆ ITERACJI IDZIE ZA NIEPEWNOŚCIĄ, NIE ZA OSTROŻNOŚCIĄ.** Drobne kroki, **dopóki projekt się formuje**; jeden przebieg, **gdy jest zaakceptowany**. *„Nie chcę dalej pracować w tak drobnych iteracjach, zaczyna nas to bardziej spowalniać niż pomagać."* ⚠ Utrzymywanie mikro‑iteracji po ustaniu niepewności jest **własnym trybem porażki** — wygląda na staranność, a kosztuje tempo. Ratyfikowana 2026-08-03 (§19.20.2) |
| **R16** | ⭐⭐ **POMIAR JEST NARZĘDZIEM DIAGNOSTYCZNYM; KRYTERIUM ODBIORU JEST EKRAN.** *„Użytkownik nie patrzy na środki geometryczne elementów — patrzy na efekt optyczny."* ⛔ Konsekwencja twarda: **test, który świeci na zielono przy złym wyglądzie, jest GORSZY niż brak testu** — zamyka temat zamiast go otworzyć; taki test należy **zawęzić do tego, o czym maszyna ma coś sensownego do powiedzenia**, a nie „wzmacniać". ⭐ To R8 rozszerzone na NARZĘDZIA. Ratyfikowana 2026-08-03 (§19.19.4) |
| **R17** | ⭐ **ZGODNOŚĆ Z DOKUMENTEM ≠ SPÓJNOŚĆ PRODUKTU.** Przegląd całej powierzchni jest **osobnym krokiem**, nigdy sumą odbiorów pojedynczych iteracji. ⚠ Dowód empiryczny: w przeglądzie domykającym języka kolorów **dwie pozostałości stały się rozstrzygalne dopiero, gdy patrzyło się na cały pasek naraz** — obie wcześniej odłożone jako „nie wiadomo". Ratyfikowana 2026-08-03 (§19.18) |

**Cztery decyzje architektoniczne M2b (§17.2) — również wiążące:**
1. **`FluentBridge`** — przepinamy Fluenta na nasz katalog; trzy trasy (metryki → setter · kolory
   wnętrza szablonu → Bridge · wartość lokalna szablonu → alias). ⛔ **Nie cofamy Bridge'a.**
2. ⭐ **KONTENER ROZSTRZYGA WIELKOŚĆ, ELEMENT JĄ PRZYJMUJE.**
3. ⭐ **REGUŁA MUSI BYĆ SFORMUŁOWANA POZYTYWNIE** — *„wszystko jest X, chyba że…"* przecieka zawsze.
4. ⭐ **WYSOKOŚĆ BIERZE SIĘ Z KONTEKSTU, NIGDY Z WARIANTU**; wariant niesie kolor.

### 5.1 ⛔ Rejestr kolizji §18.R — status w M3 (ratyfikowany 2026-08-02)

⭐ **Stan: K1–K11.** ⚠ M3.2a dopisało na jedną iterację **K12** (podłoga pary Execute/Cancel) i **wycofało
je razem z mechanizmem**, gdy użytkownik odrzucił samą podłogę (§19.11) — kolizja bez wartości lokalnej
nie ma czego rozstrzygać. Wpis w §18.R został jako zapis ustalenia o `Size.ActionMinWidth`, nie jako dług.
M3.1d dopisało **K11** (chip transakcji, `Spacing` 5 vs `Space.Sm` 6) — pierwszą
kolizję **spoza M2c** i pierwszą dotyczącą **odstępu**, a nie typografii czy promienia. Rejestr okazał się
szerszy niż licznik, który go zrodził. ⚠ Różnica 1 px czyni pokusę „weź po prostu rolę" największą właśnie
tutaj — a wzięcie jej zmieniłoby wygląd **już odebrany przez użytkownika**.

**K1–K14 zostają w rejestrze aż do przeglądu §13.3.** M3.3 przebudowało pasek zakładek i **zachowało
obecne wartości lokalne wraz z uzasadnieniem**, dopisując **K12–K14** (dwa paddingi + margines przycisku
zamykania).

⚠⚠ **KOREKTA Z M3.3a — K9 i K10 NIGDY NIE DOTYCZYŁY TEGO PASKA.** Zmierzone: oba stoją na `TabItem`,
czyli na **dolnym panelu i pod‑zakładkach edytorów**. Pasek zakładek dokumentów nie ma ani `TabItem`, ani
13 px (etykieta na `Text.Compact.Size` = 11), ani żadnego `CornerRadius`. ⭐ Rejestr indeksował po nazwie,
a „zakładka" jest nośnikiem **dwóch różnych rzeczy** (pułapka 19 w wydaniu rejestrowym).

⭐ **K12–K14 idą na §13.3 JAKO JEDNO PYTANIE**, nie trzy: wszystkie zmieniają szerokość zakładki, czyli
**ile zakładek mieści się w wierszu**. To nie jest pytanie o zgodność z katalogiem, tylko o **gęstość
paska** — a ta jest decyzją użytkownika (D6/§8.1 chroni pełną czytelność nazw).

⛔ **Nie rozszerzaj katalogu. Nie usuwaj K9/K10 z rejestru.** Dopiero przegląd §13.3 ma pełny obraz
wszystkich kolizji z całej aplikacji i wtedy zapada decyzja, czy katalog należy rozszerzyć.
Obowiązuje R3 — **nowa rola nie powstaje jako reakcja na pojedynczą iterację ani pojedynczy widok.**

⚠ Nowa kolizja tego samego typu znaleziona w M3 **nie wymaga pytania** — trafia do §18.R i etap
idzie dalej.

---

## 6. ⭐ Procedura jednej iteracji

> **Jedna powierzchnia / jeden spójny fragment = jedna iteracja = jeden commit.**
> Rytm sprawdzony przez 21 iteracji M2b i 9 iteracji M2c.

1. **Przeczytaj cały fragment**, którego dotyczy iteracja. Nie pracuj z grepa.
2. **Zbierz stan faktyczny** — wartości, bramki, konsumentów tokenów, istniejące VM-owe sygnały.
3. **Przedstaw propozycję użytkownikowi PRZED implementacją.** Przy powierzchniach trwałych ten krok
   jest **obowiązkowy w każdej iteracji** (nie tylko pierwszych) — M3 zmienia wygląd, więc propozycja
   jest jedynym momentem, w którym da się to skorygować tanio.
4. **Implementuj.** Rola zamiast literału; wyjątek **wyłącznie** z komentarzem w miejscu (§6.1).
5. **Uruchom aplikację i obejrzyj** — w **obu** motywach.
6. **Build** → **testy (trzy partycje)** → **smoke** → **dokumentacja §19** → **commit**.

### 6.1 Jak dokumentować wyjątek

Komentarz **w miejscu**, w XAML, obok wartości — nie w osobnym rejestrze. Wymagane trzy elementy:
**(a)** że to świadome · **(b)** dlaczego rola nie pasuje · **(c)** kto to rozstrzyga.
Dodatkowo wpis w `product-polish.md` §19 przy danej iteracji, a przy kolizji „rola pasuje funkcją,
ale niesie inną liczbę" — **także wiersz w §18.R**.

### 6.2 Kryteria zakończenia POJEDYNCZEJ iteracji

| # | Warunek |
|---|---|
| 1 | Zakres iteracji zamknięty — żadnej wartości „do dokończenia w następnej" |
| 2 | Każda pozostawiona wartość lokalna ma **komentarz z powodem** |
| 3 | Baza w `DesignTokenComplianceTests` odzwierciedla **stan faktyczny** (strażnik sprawdza obie strony) |
| 4 | Build 0/0 |
| 5 | Testy zielone w trzech partycjach |
| 6 | Smoke czysty + **aplikacja obejrzana w obu motywach** |
| 7 | Wpis w `product-polish.md` §19 z numerem iteracji, wynikiem i odstępstwami |
| 8 | Commit **z kodem i opisem iteracji razem**; push **po akceptacji użytkownika** |

---

## 7. ⛔ Czego M3 NIE robi

1. ⛔ **Nie cofa `FluentBridge`** ani żadnej decyzji architektonicznej M2b.
2. ⛔ **Nie rozszerza katalogu, żeby domknąć kolizję** — K1–K10 czekają na §13.3 (§5.1).
3. ⛔ **Nie wprowadza wartości lokalnej bez uzasadnienia** w miejscu.
4. ⛔ **Nie migruje 18 drzew „Zależności"** — to M4.2b, i **nigdy na `SidebarFlatController`** (§13.2).
5. ⛔ **Nie zmienia Metadata Explorera na inny komponent** — D10, obecny płaski kontroler jest docelowy.
6. ⛔ **Nie przenosi Commit/Rollback do paska statusu** — §8.4.5, chip transakcji **nigdy nie jest przyciskiem**.
7. ⛔ **Nie skraca nazw zakładek** (`MaxWidth`/wielokropek) — D6, uzasadnienie w §8.1.
8. ⛔ **Nie podbija `CurrentSchemaVersion`** — preferencje paska zakładek są addytywne (R‑4).
9. ⛔ **Nie zwiększa wysokości paska statusu** — §8.5 specyfikacji zabrania wprost.
10. ⛔ **Nie zmienia koloru całego paska statusu** — użytkownik odrzucił model Visual Studio.
11. ⛔ **Nie rusza palety składni edytora** (§6.3 — zamrożona) ani `FontFamily` (poza zakresem od M2c).
12. ⛔ **Nie naprawia przy okazji rzeczy spoza zakresu** — mierz, opisz, zapisz do dokumentacji,
    **nie rozwiązuj bez decyzji**.

---

## 8. ⭐ Obowiązkowa kolejność

```
analiza → propozycja (akceptacja) → implementacja → uruchomienie aplikacji + QA w obu motywach
  → dotnet build (0/0)
    → dotnet test (TRZY partycje, osobno)
      → smoke
        → dokumentacja (product-polish.md §19)
          → commit (kod + opis iteracji razem)
            → push na oba remote'y — WYŁĄCZNIE po akceptacji użytkownika
```

⚠⚠ **Nigdy nie łącz `dotnet build` i `dotnet test` w jednym poleceniu — deadlock.**
⚠ **Zabij `EmberTern.exe` przed przebudową** — inaczej MSB3021/MSB3027.

**Trzy partycje testów** (⚠ `ConnectionExpandBindingProbe` biegnie **sam** — hangs, gdy dołączony):

```
--filter "FullyQualifiedName!~ConnectionExpandBindingProbe&FullyQualifiedName!~SettingsCenterViewTests&FullyQualifiedName!~BrandingPresentationTests&FullyQualifiedName!~DesignTokenApplicationTests&FullyQualifiedName!~TabStripPresentationTests"
```
oraz odwrotność z `|`, oraz `ConnectionExpandBindingProbe` osobno.

⚠⚠ **Filtr jest listą nazw i dlatego STARZEJE SIĘ CICHO — nowa klasa headless musi do niego trafić.**
Pominięta, wpada do partycji głównej: nic nie zawiedzie, ale podział przestaje robić to, po co istnieje.
To ta sama pułapka, co niedziałające wykluczenie `ContextMenuPresentationTests` (§18.1.6) — tam nazwa
przestała pasować do czegokolwiek i licznik był o jeden za wysoki przez cały etap.
⚠ **Stan po M3.2a: bez zmian — `ToolbarStabilityTests` istniało przez dwa commity i zostało usunięte
razem z mechanizmem, który pinowało** (odbiór cofnął wszystkie trzy ruchy po stronie toolbara, §19.12).
Partycje mierzą **7031 + 48 + 54 = 7133**, czyli tyle co przed etapem.

⭐ **Kryterium, czy nowa klasa idzie do filtra, jest jedno: czy konstruuje kontrolki Avalonii.**
`TransactionChipTests` (M3.1d) **nie idzie** — pinuje funkcję **statyczną**, więc nie potrzebuje sesji
headless i należy do partycji głównej. Sprawdź to, zamiast zakładać w którąkolwiek stronę: klasa headless
poza filtrem psuje podział po cichu, a klasa niepotrzebnie **w** filtrze zaciemnia liczby partycji.

---

## 9. ⚠ Pułapki — zapłacone, nie płacić drugi raz

### 9.1 Najgroźniejsze dla M3

1. **⚠⚠ `{DynamicResource}` NIE rzuca przy brakującym kluczu.** Literówka jest niewidoczna przy zielonym
   buildzie — właściwość po cichu zostaje przy wartości odziedziczonej. **Nazwy ról bierz z `Tokens.axaml`
   / `Typography.axaml`, nie z pamięci, i po każdej iteracji uruchom aplikację.**
2. **⚠⚠ Wartość lokalna bije setter stylu.** Po jej usunięciu kontrolka **nagle zaczyna słuchać systemu**
   i może wyglądać inaczej. To **ujawniony dług**, nie regresja. **Zgłoś, nie maskuj.**
3. **⚠⚠ Katalog bywa zamiarem, nie opisem.** §3.1 tej listy to trzeci raz, gdy pomiar obalił zapis
   w `Tokens.axaml`. **Zanim zastosujesz liczbę z dokumentu — sprawdź, co stoi w kodzie.**
4. **⚠⚠ Test headless konstruujący `MainWindow` ZAWIESZA SIĘ.** Asercje wykonuj na najtańszej kontrolce,
   która może nieść daną cechę (`new Window()`) — to również **mocniejsza** asercja. Nowa klasa headless
   **dołącza do `HeadlessCollection`**, nigdy nie zakłada własnego `IClassFixture` (#94/#226/#286).
   ⚠ To jest realne ryzyko M3: cztery powierzchnie trwałe żyją w `MainWindow`.
5. **⚠ Goły `TextBlock` dziedziczy 14**, nie 12. Usunięcie `FontSize` z `TextBlocka` to skok w górę.
6. **⚠ Liczba nie wyznacza roli.** Pięć ról przy 11 px, dwie przy 12.
7. ⭐⭐ **NOWA (M3.1e) — SPÓJNOŚĆ ZESTAWU BIJE OPTIMUM POJEDYNCZEGO ELEMENTU.** Runda QA ikony
   debuggera dostarczyła wariantu, który wygrywał w **każdej** mierzonej liczbie (prześwit, margines
   ink, pozycja kropki) i został odrzucony na pierwszy rzut oka, bo Execute i Debug przestały czytać się
   jako rodzina. ⭐ To R8 w najczystszej postaci — **wariant przegrał w jedynym wymiarze, którego nie
   mierzyłem** — i R7 w drugiej połowie: szukałem reguły w obrębie jednego znaku zamiast rodziny.
   ⚠ Praktycznie: zanim „poprawisz" element należący do zestawu (ikona, chip, wariant przycisku),
   **sprawdź, z czym on tworzy rodzinę, i policz koszt po tamtej stronie.**
   ⚠⚠ Drugie dno tej samej rundy: pomiar pokazał, że rodzina była **przybliżeniem utrzymywanym
   ręcznie** (znak nigdy nie był `Icon.Play`), więc powrót „do poprzedniego stanu" odtworzyłby
   podatność. **Gdy relacja ma być trwała, wyraź ją referencją, nie kopią** — `Path.Data =
   {StaticResource Icon.Play}` i test na **tożsamość instancji**, bo kopia o identycznych
   współrzędnych przechodzi każdy test na równość.
8. ⭐⭐ **NOWA (M3.1e) — RAIL I TEKST TO RÓŻNE PROGI KONTRASTU.** §10 daje 3:1 elementowi UI i 4,5:1
   tekstowi, więc **ten sam token bywa poprawny jako rail i niepoprawny jako napis**
   (`DebugCurrentLineBarBrush`: 3,77:1 — rail OK, tekst nie). ⛔ Nie przenoś pędzla między nimi
   „dla spójności" bez policzenia. ⚠ **W repo nie ma strażnika kontrastu** — §10 stawia progi, nic ich
   nie sprawdza; odchyłka nie zawiedzie żadnego testu.
9. ⭐⭐ **NOWA (§19.8) — OBSERWACJA UŻYTKOWNIKA O OBJAWIE JEST WIARYGODNA; JEGO WNIOSEK O PRZYCZYNIE
   I ZASIĘGU TRZEBA ZMIERZYĆ.** Trzy zgłoszenia z rzędu wskazały inną przyczynę niż rzeczywista, zawsze
   w tę samą stronę — *„to od ostatnich zmian"* / *„to lokalne dla tego ekranu"* — a rzeczywiste
   przyczyny były **starsze i szersze**: reguła z innego etapu (C3), zależność od kolejności ładowania,
   zamiar sprzed lat, który dopiero teraz stał się widoczny. ⭐ Dwa razy pomiar **rozszerzył** naprawę
   (7 siatek zamiast jednej, cała historia zamiast jednego wpisu), raz **zamknął temat bez zmiany kodu**.
10. ⚠⚠ **NOWA (§19.8) — „właściwość istnieje w API" ≠ „właściwość działa".** `Inline.BaselineAlignment`
   jest w Avalonii i **nie robi nic** na `Run` (per-run baseline identyczny z nim i bez niego). Wariant
   pułapki §9.2/9, groźniejszy, bo sprawdzenie istnienia API daje FAŁSZYWE potwierdzenie. **Mierz efekt,
   nie obecność.** Wstawiona bezczynna właściwość to martwy kod udający poprawkę.
11. ⚠ **NOWA (§19.8) — `Stretch` zamiast `Height`/`MinHeight`, gdy element ma wypełnić kontener.**
   `Stretch` nie zwiększa `DesiredSize`, więc nie może podnieść wiersza; `MinHeight` w kroku 7 M2b
   urosło wiersz o 2 px. Ta sama decyzja architektoniczna 2, o krok wcześniej.
12. ⚠⚠ **NOWA (§19.9) — PO KAŻDEJ WARSTWIE MIERZ PONOWNIE TEN SAM PARAMETR, KTÓRY BYŁ PRZEDMIOTEM
   ZGŁOSZENIA.** Wysokość edytora w siatce pól miała **trzy** warstwy przyczyn i **każda maskowała
   następną**: wartości lokalne w kodzie budującym kolumnę (biją setter stylu) → `Stretch`, który nie
   działa, bo centruje KOMÓRKA (`DataGridCell.VerticalContentAlignment`) → dopiero `MinHeight` z roli
   `Size.Control`. ⭐ Po warstwie 1 „przyczyna" zniknęła, a objaw został: 12 px bez zmian. **Zniknięcie
   przyczyny nie jest dowodem zniknięcia objawu.**
   ⚠ Przy okazji reguła o zasięgu: setter, który w jednej siatce niczego nie podnosi (bo `ComboBox` już
   wymusza 30 px), w siatce DANYCH urośnie każdy wiersz — stąd klasa `field-editor`, a nie styl globalny.
13. ⭐⭐ **NOWA (M3.1d) — przeniesienie faktu zostawia po sobie „regresję", która nią nie jest.** M3.1d
   odebrało paskowi edytora SQL kropkę stanu i etykietę *„Active Transaction"*, bo fakt *„mam otwartą
   transakcję"* przeszedł do chipa w pasku statusu. **Ubytek w starym właścicielu wygląda dokładnie jak
   defekt** i *„pasek transakcji zgubił kropkę"* jest bardzo wiarygodnym zgłoszeniem. **Dlatego komentarz
   idzie w OBA miejsca — do tego, które fakt oddało, i do tego, które go przejęło** — a nie tylko do
   dokumentacji etapu.
   ⚠ Przy każdej kolejnej sekcji Status Bara zadaj to samo pytanie: **czy ten fakt ma już właściciela
   gdzie indziej i czy tamten właściciel nie jest bramkowany zakładką?** Bramka `IsXxxTabActive` na
   nośniku stanu **globalnego** to defekt §0.1.2, nawet gdy wygląda jak porządek.
14. ⭐⭐ **NOWA (M3.2a, §19.10.5) — POMIAR GEOMETRII MUSI ODTWORZYĆ KONTENER, BO KONTENER JEST CZĘŚCIĄ
   MECHANIZMU.** Pin rezerwacji slotu sekcji 1 wstawiony wprost do okna zmierzył **1024 px** zamiast 43:
   `StackPanel` rozciąga się w kontenerze pionowym, więc test mierzył **rozciąganie zamiast rezerwacji**.
   To wariant pułapki 12, ale ostrzejszy i wart osobnego wpisu z jednego powodu: **wyszła liczba
   absurdalna i dlatego się obroniła.** Gdyby kontener przypadkiem dał wynik prawdopodobny, pin byłby
   **fałszywie zielony** i potwierdzałby kotwicę, której nie ma. ⚠ Praktycznie: przy każdej asercji
   na `Bounds` odtwórz rodzica z produktu (poziomy vs pionowy, `Border.chrome` vs gołe okno) i **zapytaj,
   czy zmierzona liczba w ogóle mogła wyjść z mierzonego mechanizmu** — zanim uznasz zielony za dowód.
   ⭐ To jest też druga połowa decyzji architektonicznej 2 („kontener rozstrzyga wielkość"): skoro
   kontener rozstrzyga, to test bez właściwego kontenera nie mierzy tej wielkości.
15. ⭐⭐ **NOWA (M3.2a, §19.11) — GRUPA SEMANTYCZNA BIJE STABILNOŚĆ POZYCJI.** Odbiór cofnął dwie
   zmiany, które **działały dokładnie tak, jak zaprojektowano** i wygrywały w jedynej wielkości, jaką
   H‑3 dało się zmierzyć — w pikselach przesunięcia. Commit/Rollback przeniesione na prawą krawędź
   miały poprawny argument **zasięgowy**, ale użytkownik nie szuka poleceń według zasięgu, tylko według
   **sąsiedztwa z akcją, którą właśnie wykonał**; autor zmiany sam ich przez chwilę nie znalazł.
   Wspólna podłoga Execute/Cancel usuwała drganie, ale rozciągała akcję główną ponad jej treść —
   ⭐ **R5 od drugiej strony: nieważne, skąd rozmiar pochodzi (z podkreślenia czy z wyrównania), liczy
   się, co komunikuje.** ⚠ Praktycznie: zanim „ustabilizujesz" układ przesuwając element, sprawdź,
   z czym tworzy on grupę **w oczach użytkownika**, i policz koszt po tamtej stronie — odpowiedź
   *„przesuwa się o N px"* jest pełna dopiero razem z *„a czego użytkownik będzie tam szukał"*.
   ⚠⚠ Drugie dno, metodologiczne: **propozycja przed implementacją zadziałała tylko w połowie.**
   Wybór wariantu na podstawie pomiaru był właściwym trybem, ale te dwie zmiany dało się ocenić
   **dopiero na ekranie**. Dla zmian przestawiających elementy w polu widzenia krok 5 procedury
   („uruchom aplikację i obejrzyj") jest **bramką odbioru, nie formalnością na koniec**.
16. ⭐⭐ **NOWA (M3.2a, §19.12) — GDY AUDYT NAZYWA PROBLEM JEDNĄ WIELKOŚCIĄ, TO JEST HIPOTEZA O PROBLEMIE,
   A NIE JEGO DEFINICJA.** H‑3 brzmiało *„toolbar się przesuwa"*, więc pomiar dał liczbę pikseli, a każde
   z trzech rozwiązań tę liczbę zmniejszało — i **każde płaciło inną walutą**: rozmiarem akcji głównej,
   sąsiedztwem poleceń, gęstością układu. Wszystkie trzy działały i wszystkie trzy zostały odrzucone.
   ⚠ Praktycznie: zanim zaczniesz minimalizować wielkość, którą podał audyt, **wypisz, co jeszcze na tej
   powierzchni ma wartość** — i sprawdź, czy któraś z tych rzeczy nie jest ważniejsza. ⭐ Asymetria warta
   zapamiętania: **pusta przestrzeń kosztuje przez cały czas, przesunięcie kosztuje przez chwilę** — bo
   dziury w spoczynku nic nie tłumaczy, a przesunięcie widać tylko w momencie zmiany.

17. ⛔⛔ **NAJWAŻNIEJSZA (M3.2b, §19.14) — REGUŁA OPISUJE TO, CO JUŻ JEST DOBRE; NIE JEST MANDATEM DO
   ZMIANY WSZYSTKIEGO, CO DO NIEJ NIE PASUJE.** Cztery odrzucenia z rzędu, jeden mechanizm: pomiar →
   reguła → **doprowadzenie reguły do końca** → produkt gorszy. Ani razu nie zawiódł pomiar; za każdym
   razem zawiodło przekonanie, że skoro reguła jest prawdziwa, to jej pełne zastosowanie jest ulepszeniem.
   ⚠ **Element niezgodny z regułą bywa wyjątkiem, który DZIAŁA** — Comment/Uncomment miały różne kolory
   na wyraźne życzenie użytkownika, bo ikony są podobne; uznałem to za „kolor niosący fałszywą różnicę".
   ⭐ Praktycznie, przed każdą zmianą wyprowadzoną z reguły zadaj **dwa** pytania: *„czy ten element jest
   niezgodny, bo to błąd — czy dlatego, że ktoś świadomie tak chciał?"* oraz *„co użytkownik traci, jeśli
   się mylę?"*. ⚠⚠ I trzecie, mierzalne: **czy mierzyłem tam, gdzie problem jest, czy tam, gdzie patrzę?**
   §19.14.2 — 91% ikon aplikacji jest już neutralnych, a ja wyciszałem, bo wszystkie kolorowe skupiają
   się w dwóch paskach, czyli dokładnie w moim polu widzenia.
18. ⭐⭐ **NOWA (§19.19) — PUDEŁKO TO NIE FARBA, a oko czyta farbę.** Wysokość `TextBlocka` to
   **INTERLINIA**: linia bazowa leży ok. ¾ wysokości, więc dolna część pudełka to obszar znaków
   schodzących — **w napisie bez schodzących PUSTY**. Farba siedzi wtedy nisko w pudełku i wyrównanie
   `VerticalAlignment="Center"` zostawia **widoczny rozjazd** wobec elementu, którego farba JEST jego
   pudełkiem (kropka, ikona). ⭐ Praktycznie: **korekta optyczna przez `RenderTransform`** (nie margines
   — nie rusza układu i nie przesuwa sąsiadów), wartość **całkowita** (ułamek rozmywa tekst).
   ⚠ Konsekwencja dla `UseLayoutRounding="False"`: to narzędzie dla elementu, **który JEST swoją farbą**
   (koło, tło). Postawione na elemencie z tekstem w środku **pogarsza** — zdjęło badge o pół piksela
   w górę, bo wersaliki leżą wysoko w swoim pudełku i zaokrąglenie w dół tę różnicę nadrabiało.

19. ⭐ **NOWA (§19.17.2) — POMIAR PO NOŚNIKU NIE ODRÓŻNIA ROLI OD STANU.** Inwentarz §20 zliczał
   `SvgIcon` po tokenie, więc glif **stanu** (wiersz podsumowania zmian) i glif **dekoracji** (lupa
   w polu tekstowym) trafiły do tabeli **akcji** — trzy wiersze §8.2 nie przetrwały dokładniejszego
   sprawdzenia. ⭐ Praktycznie: **każdą pozycję inwentarza przepuść przez §2 języka** (*co jest stanem,
   a co akcją*), zanim uznasz ją za robotę do wykonania. Rolę rozstrzyga **kontekst, w którym element
   stoi**, nigdy sam nośnik.

20. ⭐ **NOWA (§19.19.1) — PRZECZYTAJ ZAKRES WCZEŚNIEJSZEGO POMIARU, ZANIM UŻYJESZ GO JAKO ODPOWIEDZI.**
   W miejscu zgłoszenia stał komentarz **„⛔⛔ NIE PRÓBUJ WYŚRODKOWAĆ TEGO W PIONIE"** poparty trzema
   pomiarami — i o mały włos posłużyłby za odpowiedź. Był prawdziwy, ale odpowiadał na **inne pytanie**
   (relacja dwóch runów WEWNĄTRZ `TextBlocka`, nie bloku wobec sąsiada). ⚠ Im bardziej stanowczy
   komentarz, tym większa pokusa potraktowania go jako zamknięcia tematu — sprawdź, **czego dokładnie
   dotyczył**.

21. ⚠ **NOWA (§19.16.2) — NIEAKTUALNY KOMENTARZ UCZY NIEPRAWDY DOKŁADNIE TAK JAK NIEAKTUALNY STRING.**
   Legenda „Warning=delete" w `Colors.axaml` przeżyła zmianę, którą opisywała (osiem edytorów i 131
   pozycji menu dawno przeszło na czerwień) i **wygenerowała cały dryf** naprawiany w K2. To gotcha
   **#284** w komentarzu zamiast w stringu. ⭐ Praktycznie: gdy zmieniasz regułę, **poszukaj miejsc,
   które ją opisują prozą** — build ich nie sprawdzi.

### 9.2 Odziedziczone z M2b (§17.5)

7. **⚠⚠ Arytmetykę wysokości sprawdza się na SUMIE, nie na składniku.** Trzy potknięcia w M2b, trzecie
   **wysłane** i niewidoczne przez pięć iteracji.
8. **⚠⚠ Styl typu sięga do CUDZEGO szablonu — w obie strony.**
9. **⚠ Deklarowana właściwość potrafi kłamać** — `RadioButton` raportował `MinHeight=0`, a żądał 32.
   **Sonduj drzewo, nie czytaj właściwości.**
10. **⚠ Kolejność deklaracji rozstrzyga** między stylami o równej trafności.
11. **⚠ Reguła oparta na tagowaniu wymaga otagowania WSZYSTKICH instancji.** Dwa razy zabrakło jednej.
12. **⚠ Podłoga wyrównuje tylko wtedy, gdy leży POWYŻEJ naturalnej szerokości** etykiet.
13. **⚠ Test potrafi mierzyć nie ten podmiot** i wtedy potwierdza defekt zamiast go łapać.

---

## 10. Plan iteracji

> ⚠ Kolejność wynika z zależności twardych §13.0.1: **Z‑3** (M3.x po M2c ✅), **Z‑4** (M3b po M3.1),
> **Z‑5** (M4.2b po M3.4), **Z‑6** (M4.x po bramie §13.3).

| # | Podetap | Zakres | Wymaga decyzji |
|---|---|---|---|
| ✅ 1 | **M3.1a** | Rytm pionowy chromy — `Size.TabIndicator` (nowy token), podłączenie `Size.TitleBar` / `Size.StatusBar` / `Size.Row.Tab`; **zakładka 30 → 26, szerokość bez podłogi akcji** (§19.1) | **DA** ✅ |
| ✅ 2 | **M3.1b** | Cztery sekcje (§8.4.3) + hierarchia (§8.4.4) + **D3**; ⭐ tożsamość połączenia przeniesiona z paska tytułu (§19.3) | — |
| ✅ 3 | **M3.1c** | Rail (§8.4.1–§8.4.2) + ⭐ **agregacja po `WorkspaceTabs` przeniesiona tu z M3.1e** (§19.4.2) | — |
| ✅ 4 | **M3.1d** | Chip transakcji z czasem (§8.4.5); ⭐ **podział własności: chip = fakt globalny, pasek edytora SQL = licznik lokalny** (§19.5) | — |
| ✅ 5 | **M3.1e** | Chipy Trace / Debugger (znak tożsamości + etykieta); ⭐ **chipy NIE dziedziczą pędzli railu — inny próg kontrastu** · ⛔ ikona debuggera zamknięta, jest teraz referencją do `Icon.Play` (§19.6) | — |
| ✅ 6 | **M3.1f** | Sekcja postępu + operacja referencyjna; ⭐ **infrastruktura dla M3b — oba tryby**, choć operacja referencyjna umie tylko nieokreślony · ⭐ Cancel to **dwa zasięgi jednej komendy**, zamyka lukę bramkowania (§19.7) | — |
| ✅ 6b | **poprawki odbiorcze** | Zamknięte bez osobnej iteracji (decyzja użytkownika): wyrównanie endpointu **zamknięte pomiarem bez zmiany kodu** · bug historii parametrów · pusta kolumna Type przy domenie · wygląd wyłączonych komórek (§19.8) | — |
| ✅ 7 | **M3.2a** | H‑3. ⭐ Model 5 sekcji **już istniał**: gwarantował KOLEJNOŚĆ, nie POZYCJĘ. ⛔⛔ **Z czterech ruchów został JEDEN — Export DDL na koniec paska tytułu (T2).** Odbiór wizualny cofnął podłogę Execute/Cancel, dokowanie Commit/Rollback i rezerwację slotu sekcji 1: ⭐ **GRUPA SEMANTYCZNA BIJE STABILNOŚĆ POZYCJI** · rozmiar z wyrównania czyta się jak deklaracja ważności (R5 od drugiej strony) · ⭐⭐ **R13** — nie rezerwujemy miejsca na element, którego w danym kontekście nie będzie. Wszystkie przesunięcia **świadomie zaakceptowane**. §19.10 + §19.11 + **§19.12** | — |
| ⛔ 8 | ~~**M3.2b**~~ | **WYCOFANA W CAŁOŚCI** (§19.13 + §19.14). Wyprowadziłem regułę z §7.5 i doprowadziłem ją do końca; UX wyszedł gorszy. ⭐ Ocalała jedna rzecz — korekta §7.5: neutralny dla IKONY to `NeutralIconBrush` (brak `Foreground`), nie `ForegroundBrush` | — |
| ✅ 9 | **projekt języka kolorów** | ⭐⭐ **ZAAKCEPTOWANY 2026-08-02 → [`color-language.md`](color-language.md).** Dokument PRODUKTU: cztery niezależne systemy (rodzaj · akcja · tożsamość modułu · hierarchia przycisku), siedem ról R‑1…R‑7, sześć nazwanych wyjątków, reguła rozstrzygająca dla nowych funkcji, plan wdrożenia §11. ⛔ **Nie projektuj go ponownie** | — |
| ✅ **10** | **K1–K7 — WDROŻENIE JĘZYKA** | 🔒 **ZAMKNIĘTE I ODEBRANE 2026-08-03** (§19.15–§19.20). K1 neutralny · K2 destrukcja 🟡→🔴 · K3–K7 jednym przebiegiem · przegląd domykający zamknął **pięć pozostałości** i **wszystkie pytania O‑1…O‑5** · poprawka optyczna paska statusu. **230 ikon, 81 z kolorem, ani jeden przycisk akcji poza językiem.** ⭐ Wyniosło **R15/R16/R17** i pułapki 18–21 | **DD** ✅ |
| ✅ **11** | **M3.2d** | **ZROBIONE** (§19.21). M‑1 — 10 literałów → `UiStrings`, **13 → 3** (1 → M3.3, 2 → M4.3). Zero zmian wizualnych, licznik testów bez zmiany. ⭐ Własne stałe `*Tooltip` zamiast reuse'u etykiet — odwrotność findingu **D6** · ⚠ żaden tooltip nie dostaje gestu (komendy są `Tree`‑scoped, keyboard-manager §14) · ⚠ znalezisko poboczne: **6 sierocych stałych `UiStrings`**, zapisane, świadomie nienaprawione | — |
| ✅ **12** | **M3.3a** | **ZROBIONE, PRZESKALOWANE PRZEZ UŻYTKOWNIKA** (§19.22). ⭐⭐ Zakres z planu (geometria, `Size.Row.Tab`, wskaźnik) **był już dostarczony przez M3.1a** — pułapka 20 na własnym planie. Iteracja domknęła zamiast tego **dług techniczny paska**: 12 → 5 wartości lokalnych, komplet reguł zakładki aktywnej w `ControlStyles.axaml`, ostatni literał M‑1. ⚠⚠ **Przeniesienie stylu ODTWORZYŁO regresję §19.2** — lokalne `Background` biło setter; złapał to nowy test, recepta: oba stany jako setter + kotwica `workspace-tab`. ⚠ **K9/K10 dotyczą `TabItem`, nie tego paska.** Nowe **K12–K14** (paddingi/margines = gęstość paska) → §13.3 | — |
| ✅ **13** | **M3.3b** | **ZROBIONE** (§19.23). Dwa tryby na JEDNYM `ItemsControl` — tryb robią kierunki przewijania `ScrollViewera` · licznik przepełnienia liczy zakładki **niewidoczne**, z rzeczywistego układu · własna kategoria **Tabs** w Settings Center (decyzja użytkownika) · `CurrentSchemaVersion` bez zmian. ⚠ Dwa strażniki Settings Center zadziałały za pierwszym razem | ✅ |
| ✅ **14** | **M3.3c** | **ZROBIONE** (§19.24). Menu 9 pozycji, zero nowej chromy. ⭐⭐ Bramka reguły #11 dostała ZASIĘG (3 → 4 wejścia) — `scope == null` znaczy „wszystkie", więc trzy stare wejścia nietknięte. ⭐ Każda pozycja ma własne `CanExecute`, przeliczane w jednym punkcie przy zmianie kolekcji. ⭐ Reveal ZAZNACZA I PRZEWIJA, a rozwinięcie kategorii jest poczekane (inaczej działałoby dopiero za drugim razem) | — |
| ⭐⭐ **15** | **M3.4a** | **← TU ZACZYNASZ.** Metadata Explorer — wiersz drzewa | **DB** (wiersz **zostaje 24**) |
| 16 | **M3.4b** | Przegląd menu kontekstowych | — |
| 17 | **M3b** | Podłączenie pozostałych operacji do paska postępu (16 VM, 3 ścieżki `IProgress`)<br>⏸ **+ pełna semantyka kolorów railu** — odłożona tu świadomie przez użytkownika, z pomiarem (§19.4.4) | — |
| 18 | ⛔ **brama** | **§13.3** — cztery powierzchnie **jednocześnie**, żywa baza, oba motywy | — |
| 19 | — | Podsumowanie zamykające §19.x + CLAUDE.md + handover M4 + prompt startowy | — |

⚠ **Podział M3.1 na sześć iteracji jest celowy.** Status Bar to najbogatsza sekcja dokumentu (§8.4 ma
siedem podsekcji) i jedyna powierzchnia, którą użytkownik widzi **zawsze**. Jedna iteracja „Status Bar"
byłaby zmianą, której nie da się sensownie odebrać wizualnie.

---

## 11. ⭐⭐ Reguła prowadząca

> **Użytkownik:** *„Dokument ma prowadzić produkt. Nie produkt dokument."*
> **Użytkownik (§15.11):** *„Pomiar nadal jest obowiązkowy. Ale nie jest już celem. Jest tylko narzędziem."*
> **Użytkownik (R12):** *„Dokument ma odzwierciedlać architekturę produktu, a nie zmuszać produkt do
> spełniania wcześniejszych założeń, które zostały obalone pomiarami."*

Jeżeli znajdziesz rozwiązanie wyraźnie lepsze od zapisanego:
**propozycja → akceptacja → aktualizacja dokumentu → implementacja.**
⛔ Ani ciche odstępstwo, ani ślepa zgodność. ⛔ Decyzje D1–D12 i reguły R1–R14 zmienia wyłącznie użytkownik.

⭐ **Dotyczy to w szczególności railu (§8.4.1)**, gdzie użytkownik przyznał wprost swobodę projektową:
wiążące są **wymagania** (rozdzielenie Rail/Chip, cztery sekcje, hierarchia, widoczna transakcja, brak
wzrostu wysokości, zakaz agresywnej zmiany koloru całego paska, kolor nigdy jako jedyny nośnik),
a **nie** konkretna realizacja. ⚠ Zamiana koncepcji wymaga **zapisania powodu w §19** — inaczej za pół
roku nikt nie odróżni świadomej zmiany od tego, że ktoś nie doczytał dokumentu.
