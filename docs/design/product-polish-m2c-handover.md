# Product Polish — M2c — DOKUMENT STARTOWY (jedyny punkt wejścia)

> **To jest prompt dla Claude'a, nie dla użytkownika.** Wchodzisz w **właściwe M2c**: krok 0
> (inwentarz) jest **ZAKOŃCZONY, ZAAKCEPTOWANY I ZACOMMITOWANY** (`20d4ad6`, 2026-08-02).
> Ten plik jest **kompletny** — zawiera stan, reguły, plan, procedurę i pułapki. Do dokumentu
> etapu sięgasz po **konkretną** sekcję, nie po całość.

---

## 0. Co przeczytać i w jakiej kolejności

| # | Dokument | Status | Kiedy |
|---|---|---|---|
| 1 | **ten plik** | ⭐ **wiążący, punkt wejścia** | zawsze, w całości |
| 2 | `product-polish.md` **§18** | ⭐ **wiążący** — as-built M2c, wynik kroku 0 | zawsze |
| 3 | `product-polish.md` **§17** | ⭐ wiążący — podsumowanie M2b, reguły R1–R11 | zawsze |
| 4 | `product-polish.md` **§16** | ⭐ wiążący — wzorzec `FluentBridge` | przy dotknięciu `Themes/` |
| 5 | `product-polish.md` §3–§6 | referencyjny — zasady katalogu + role | przy wyborze roli |
| 6 | `Themes/Tokens.axaml`, `Themes/Typography.axaml` | ⭐ **katalog — źródło prawdy o rolach** | przy każdej iteracji |

⛔ **NIE czytaj na starcie:**
* `product-polish.md` **§15** — zapis 21 iteracji M2b. Sięgaj po **konkretną podsekcję** dopiero
  wtedy, gdy dotyczy tego, co właśnie robisz.
* `product-polish.md` **§8** — to M3, nie ten etap.
* `product-polish-m2b-handover.md`, `product-polish-m2a-handover.md` — **ZAMKNIĘTE, historyczne**.
  Ich sekcje „co dalej" opisują świat sprzed M2b i wprowadzą Cię w błąd.

**Specyfikacja etapu (nadrzędne źródło):** `C:\Users\grzegorz.gronski\Desktop\Product Polish.mdown`

---

## 1. Stan projektu

| | |
|---|---|
| **Branch** | `feat/product-polish` |
| **Ostatni commit** | *M2c iteracje 5–9 + podsumowanie zamykające* |
| **Wypchnięte** | ⚠ `e388ad7` na oba remote'y; **wszystko od `20d4ad6` w górę jeszcze NIE** (push po akceptacji użytkownika) |
| **Etap** | M0 ✅ · M1 ✅ · M2a ✅ · M2b ✅ · **M2c — WSZYSTKIE 10 KROKÓW WYKONANE** (krok 0 + iteracje 1–9 + domknięcie bazy). Zostaje **jeden pełny odbiór wizualny** (DoD 6) — patrz `product-polish.md` **§18.10** |
| **Liczniki** | `FontSize` **43** (z 605) · `CornerRadius` **19** (z 37) · `FontFamily` **81** (poza zakresem). ⭐ Wszystkie 62 pozostałe to **świadome wyjątki z powodem zapisanym w miejscu** |
| **⭐ Rejestr kolizji** | `product-polish.md` **§18.R** — **10 pozycji (K1–K10)**, drugi właściwy wynik etapu i wejście do przeglądu §13.3 |
| **⛔ Po co jeszcze ten plik** | Nie jest już punktem wejścia do pracy — M2c jest zamknięty. Zostaje jako **zapis reguł i pułapek** (R1–R12, procedura, 16 pułapek), bo M3 pracuje na tych samych powierzchniach |
| **Build** | 0 błędów / 0 ostrzeżeń |
| **Suite** | **7087**, zielony w trzech partycjach (7000 + 54 + 33) — ⚠ zapis „7088 / 54 + 34" poprawiony 2026-08-02 po pomiarze na czystym `HEAD` (§18.1.6); przyczyną była nieistniejąca klasa w filtrze, patrz §8.3/15 |
| **Smoke** | czysty |
| **Drzewo** | czyste |

### 1.1 Co zrobiło M2a
Zbudowało **katalog**: `Themes/Tokens.axaml` (odstępy, wysokości, ikony, promienie, krawędzie),
`Themes/Typography.axaml` (12 ról typograficznych + `Font.Ui` / `Font.Code`) oraz strażnik
`DesignTokenComplianceTests` w kształcie **zapadki licznikowej**. Nie zmieniło ani jednego piksela.

### 1.2 Co zrobiło M2b
**Włączyło katalog dla kontrolek bazowych** — 21 iteracji, 14 commitów: `CheckBox`, `RadioButton`,
`TextBox`, `ComboBox`, `Button` (+4 warianty), `NumericUpDown`, `ToggleButton`, `Expander`,
`ScrollBar`, `ToolTip`, DataGrid Standard, przeprojektowanie skali szarości Light, podział
`ElevatedPanelBrush` → `ChromeStrong` / `SurfaceRaised`.
**Zamknięte pozycje audytu:** RB‑2 · RB‑3 · RB‑4 · H‑7 · H‑8 · H‑10 · M‑2 · §8.4 specyfikacji.
**Otwarte świadomie:** V‑1 (kolor komentarzy SQL zostaje — R10) · **H‑1 = to jest M2c**.

### 1.3 Co zrobił krok 0 M2c (2026-08-02)
Pomiar stanu faktycznego widoków **bez jednej zmiany w zachowaniu aplikacji**. Pełny zapis:
`product-polish.md` §18.0. Skrót w §2 i §3 poniżej.

---

## 2. ⭐ Po co jest M2c — i czego NIE jest celem

> **Teza etapu:** *Wartość lokalna bije setter stylu. Dopóki stoi w widoku, żadna reguła Design
> Systemu nie działa.*

Projekt udowodnił tę tezę **pięć razy**: sześć wariantów `MessageBanner`, `MainWindow.Icon`,
`Foreground` Batch Results, `DangerIconBrush`, a w samym M2b — belka statusu Data Import, której
**żadna reguła systemu nie mogła naprawić**, dopóki widok trzymał własne `FontSize`.

### 2.1 ⭐⭐ R12 — CEL ETAPU, RATYFIKOWANY 2026-08-02

> **Użytkownik:** *„Nie traktuj celem etapu wyzerowania liczników. Celem jest usunięcie
> **nieuzasadnionych** wartości lokalnych. Jeżeli po zakończeniu zostanie niewielka liczba świadomie
> pozostawionych wyjątków z udokumentowanym uzasadnieniem, to M2c nadal będzie uznany za zakończony.
> Dokument ma odzwierciedlać architekturę produktu, a nie zmuszać produkt do spełniania wcześniejszych
> założeń, które zostały obalone pomiarami."*

**R12 jest rozwinięciem R8 o jeden poziom.** R8 mówi „pomiar jest narzędziem, nie argumentem
końcowym"; R12 dodaje: **licznik też jest tylko narzędziem.** Trzy konsekwencje operacyjne:

1. ⛔ **Nie wolno migrować wartości na rolę, która do niej nie pasuje, żeby licznik spadł.**
   **Błędna rola jest GORSZA od wartości lokalnej:** wartość lokalna jest widoczna jako dług,
   a błędna rola udaje, że długu nie ma — i przy pierwszej zmianie katalogu przesuwa ekran,
   o którym nikt już nie pamięta.
2. ⭐ **Warunkiem wyjścia NIE jest liczba, tylko zdanie z powodem przy każdej pozostałej wartości.**
   „605 → N" nie jest oceną etapu.
3. ⭐ **Kiedy pomiar obala zapis — poprawiamy zapis, w miejscu, z datą i powodem.** Dokument opisuje
   produkt, nie odwrotnie.

---

## 3. Wynik kroku 0 — co wiesz przed pierwszą linią kodu

### 3.1 Liczniki (potwierdzone co do sztuki)

| Licznik | Stan | Cel M2c |
|---|---|---|
| `FontSize` | **605** / 49 plików | uzasadniona reszta (~28 znanych wyjątków + to, co znajdziesz) |
| `FontFamily` | **81** / 28 plików | ⛔ **POZA ZAKRESEM — zostaje 81** (§3.5) |
| `CornerRadius` | **37** / 13 plików | **17** migruje, **20** zostaje z powodem (§3.6) |

⚠ Licznik mierzy **wartości lokalne**; `{DynamicResource …}` **nie liczy się**.

### 3.2 ⭐⭐ Rozkład: 605 deklaracji to SIEDEM liczb — ale liczba NIE wyznacza roli

| Wartość | Ile | Rola |
|---|---|---|
| **11** | **345** | ⚠ **PIĘĆ ról** — `Text.Compact` · `Text.Grid` · `Text.GridHeader` · `Text.Status` · `Text.SectionHeader` |
| **12** | **155** | ⚠ **DWIE role** — `Text.Application` · `Text.Toolbar` |
| 10 | 54 | `Text.Caption` |
| 13 | 40 | `Text.Code` — ⚠ tylko 25 z nich to edytor kodu |
| 9 | 7 | ⛔ brak roli |
| 14 | 3 | `Text.Title` |
| 23 | 1 | `Text.Display` |

⭐ **To jest powód, dla którego M2c idzie widok po widoku, a nie automatem.** Podmiana maszynowa
zachowałaby liczbę i wpisała złą rolę — błąd niewidoczny na ekranie **i** niewidoczny w teście.

### 3.3 ⭐⭐ POMIAR, KTÓRY DECYDUJE O MECHANICE — goły `TextBlock` dziedziczy **14**

Sonda headless (Avalonia 12.0.3):
```
Window.FontSize = 14      bare TextBlock = 14      SelectableTextBlock = 14      TextBlock.subtle = 14
TextBox = ComboBox = CheckBox = Button = NumericUpDown = RadioButton = 12   (ze stylu M2b)
```

⚠⚠ **Usunięcie `FontSize` z `TextBlocka` NIE jest neutralne — podnosi tekst do 14.**
Sweep ma **dwa różne ruchy** i pomylenie ich to defekt widoczny gołym okiem:

| Ruch | Kiedy | Efekt |
|---|---|---|
| **USUŃ** | kontrolka dostaje **tę samą** wartość ze stylu M2b | zero zmian |
| **ZAMIEŃ na `{DynamicResource …}`** | wszystko inne — **w szczególności każdy `TextBlock`** | zero zmian |

### 3.4 Cztery koszyki 605 deklaracji

| # | Koszyk | Ile | Działanie | Ryzyko |
|---|---|---|---|---|
| **A** | kontrolka ma już tę wartość ze stylu: `TextBox` 36 · `ComboBox` 18 · `NumericUpDown` 11 · `CheckBox` 6 · `Button` 4 · `RadioButton` 2 | **77** | **usuń** | żadne, dowodliwie |
| **A?** | `DataGrid FontSize="11"` — `DataGridCell` i `DataGridColumnHeader` mają własne settery (11) | 25 | usuń **po weryfikacji** | sprawdź pusty stan / nagłówek grupy |
| **B** | jedna rola wprost: `ae:TextEditor` 13 (25) · `TextBlock` 10 (49) · 23 (1) · 14 (3) | **78** | zamień na rolę | żadne |
| **C** | rola do rozstrzygnięcia per miejsce (całe 11 i 12) | **~390** | zamień na rolę | żadne, jeśli rola trafna |
| **D** | brak roli o tej wartości | **~28** | ⛔ **zostaw + komentarz** | migracja = zmiana wyglądu |

**Rozkład właścicieli (XAML, 585 z 605; reszta to 10 setterów w widokach i 11 wywołań w code-behind):**
```
TextBlock 11 x244   TextBlock 12 x57   TextBlock 10 x49   TextBox 12 x36   TextBox 11 x28
DataGrid 11 x25     ae:TextEditor 13 x25   ComboBox 12 x18   TextBlock 13 x15   Button 11 x13
NumericUpDown 12 x11   CheckBox 11 x9   TextBlock 9 x7   ae:TextEditor 12 x6   CheckBox 12 x6
ComboBox 11 x5   ListBox 11 x4   Button 12 x4   SelectableTextBlock 12 x4   DataGrid 12 x4
TextBlock 14 x3   CheckBox 10 x3   NumericUpDown 11 x3   RadioButton 12 x2   TextBlock 23 x1
```
`Classes="subtle"` niesie **73** wystąpienia 11 px i 12 wystąpień 12 px — największa pojedyncza grupa.

### 3.5 ⛔ `FontFamily` — POZA ZAKRESEM M2c (ratyfikowane)

Token `Font.Code` niesie `Cascadia **Mono**, Consolas, Menlo, monospace`; 65 z 81 wystąpień to
`Cascadia **Code**,Consolas,Menlo,monospace`. **Ani jeden z 81 ciągów nie jest identyczny z tokenem**,
więc reguła „podmień tylko ciąg już identyczny" daje **zero migracji** — arytmetycznie, nie
z ostrożności.

> **Użytkownik:** *„Jeżeli `Cascadia Code` jest dziś świadomą decyzją, to nie zamieniamy jej na `Mono`
> tylko dlatego, że istnieje token."*

⚠ `Cascadia Code` (ligatury) vs `Cascadia Mono` (bez) rozstrzyga **backlogowy sprint UX**, razem
z konsolidacją 7 ciągów / 95 wystąpień / 33 plików (`settings-center.md` §2.7 + §7.1).
⚠ **Konsekwencja przyjęta świadomie: `Font.Code` zostaje tokenem bez konsumenta.** Powód stoi przy
samym tokenie w `Typography.axaml`. ⛔ Nie „naprawiaj" tego.

### 3.6 `CornerRadius` — migruje wyłącznie `3`

§4.2.2 twierdził, że „wszystkie 4 / 4.5 / 5 / 6 są chipami". **Obalone w obie strony** (§18.0.5/2):

* **`4.5` / `5` / `6` (7 wystąpień) to GEOMETRIA, nie chipy.** `Width=10 Height=10 CornerRadius=5`
  i `Width=9 Height=9 CornerRadius=4.5` to **koła**; `Height=12 CornerRadius=6` /
  `Height=10 CornerRadius=5` to **kapsuły** pasków postępu. Promień = połowa boku.
  ⛔ **Nie tokenizujemy geometrii wynikającej z matematyki** — `Radius.Chip` (4) zamieniłby koło
  w kwadrat ze ściętymi rogami.
* **`4` (11 wystąpień) to w większości KARTY** (`BorderThickness="1" Padding="10,8"`, kontenery
  `ClipToBounds`, kafelek wiersza); chipem jest **jedno** wystąpienie (`AggregationBarView`).
  `Radius.Surface` byłoby rolą trafną, ale to 4 → 3 — **decyzja produktowa oddana przeglądowi §13.3**.
* **`0` (2 settery)** — reset, tokenu nie potrzebuje.

→ **Migruje 17 × `CornerRadius="3"` → `Radius.Surface`. Reszta zostaje z komentarzem.**

### 3.7 ⛔ 28 wystąpień `FontSize` bez roli — zostają

| Grupa | Ile | Dlaczego zostaje |
|---|---|---|
| `FontSize="9"` | 7 | 2 to glify (`▶`, `●`) — katalog sam je wyłącza; pozostałe 5 to realna zmiana 9 → 10 |
| `ae:TextEditor FontSize="12"` | 6 | edytory **w wierszu siatki** (kursory/podprogramy Easy, podgląd Global Search, szczegół Trace) — 12 px to gęstość kontenera, nie dryf |
| `TextBlock FontSize="13"` | 15 | 13 px istnieje wyłącznie jako `Text.Code`, a to treść (`ConfirmDialog`, nagłówki Security Managera, linia planu) |

Decyzje „9 → 10", „12 → 13" i „czy 13 px zasługuje na własną rolę" → **przegląd §13.3 / M5**.

### 3.8 ⚠ `ControlStyles.axaml` ma ten sam dług, poza zasięgiem strażnika

Strażnik pomija `Themes/` (*„tam mieszka system"*) — za szerokie założenie. Literały tam, gdzie
powinna stać rola: `TabItem` 13 · `TabItem.bottom-tab` 11 · `TabItem.sub-tab` 11 · `ContextMenu` 12 ·
`MenuItem` 12 · `PART_InputGestureText` 11 · `ListBox.code-action-menu ListBoxItem` 12 ·
`CornerRadius` 4/3/3.5. To **katalog zapisany drugi raz**. → iteracja 9.

---

## 4. Reguły obowiązujące — R1–R12

⛔ **Zmienia je wyłącznie użytkownik. Nie otwierać ponownie.**

| # | Reguła | Gdzie |
|---|---|---|
| R1 | *„Projektujemy kontrolki, na których programista pracuje komfortowo 8 godzin dziennie"* — katalog nie ma wygrać z jakością produktu | §15.0 |
| R2 | Komponent ocenia się w **komplecie stanów** i w **obu motywach** | §15.2.1 |
| R3 | Nowa **rola** powstaje z użycia w kilku komponentach, nigdy z jednego przypadku | §15.2.1 |
| R4 | **`FluentBridge` nie jest drugim katalogiem tokenów** — wyłącznie mapowanie | §16.2 |
| R5 | **Kolor może określać priorytet akcji, ROZMIAR NIE** | §15.10.1 |
| R6 | **Ustawienia są panelem referencyjnym** | §15.9 |
| R7 | **Nie łatać pojedynczych ekranów** — najpierw reguła Design Systemu | §15.10 |
| R8 | **Kryterium odbioru: „czy wygląda to jak dopracowana aplikacja komercyjna?"** Pomiar jest narzędziem, nie argumentem końcowym | §15.11 |
| R9 | **Domain Picker** — nie ujednolicać szerokości | §15.11.4 |
| R10 | **Kolor komentarzy SQL zostaje** (V‑1) | §15.4.3 |
| R11 | **`Size.Row.Grid`** to osobna decyzja produktowa | §15.10.5 |
| **R12** | ⭐ **Celem jest usunięcie NIEUZASADNIONYCH wartości lokalnych, nie wyzerowanie licznika** | **§18.0.8** |

**Cztery decyzje architektoniczne M2b (§17.2) — również wiążące:**
1. **`FluentBridge`** — przepinamy Fluenta na nasz katalog; trzy trasy (metryki → setter · kolory
   wnętrza szablonu → Bridge · wartość lokalna szablonu → alias). Własny `ControlTemplate` wymaga
   dwóch zmierzonych warunków; spełniają je dokładnie dwie kontrolki.
2. ⭐ **KONTENER ROZSTRZYGA WIELKOŚĆ, ELEMENT JĄ PRZYJMUJE.**
3. ⭐ **REGUŁA MUSI BYĆ SFORMUŁOWANA POZYTYWNIE** — *„wszystko jest X, chyba że…"* przecieka zawsze.
4. ⭐ **WYSOKOŚĆ BIERZE SIĘ Z KONTEKSTU, NIGDY Z WARIANTU**; wariant niesie kolor.

---

## 5. ⭐ Sposób migracji — procedura jednego widoku

> **Jeden widok = jedna iteracja = jeden commit.** Rytm sprawdzony przez 21 iteracji M2b i jedyny,
> przy którym QA użytkownika ma sens.

### 5.1 Krok po kroku

1. **Przeczytaj plik w całości.** Nie migruj z grepa — rola bierze się z kontekstu, a kontekst
   widać tylko w strukturze.
2. **Zbierz wszystkie deklaracje** `FontSize` / `CornerRadius` z tego pliku i **przypisz każdej
   koszyk** (A / A? / B / C / D — §3.4).
3. **Przedstaw propozycję użytkownikowi PRZED implementacją** — tabela: linia · element · wartość ·
   koszyk · docelowa rola (albo „zostaje + powód"). ⭐ Ten krok jest obowiązkowy przy pierwszych
   iteracjach; przy kolejnych, gdy reguła jest już ugruntowana, wystarczy zgłosić odstępstwa.
4. **Implementuj:** koszyk A → usuń atrybut; B/C → `{DynamicResource <Rola>.Size}`; D → zostaw
   **i dopisz komentarz XAML z powodem**.
5. **Build** → **testy** → **smoke** → **dokumentacja §18** → **commit**. Kolejność obowiązkowa (§7).

### 5.2 ⭐ Jak podjąć decyzję o roli — reguła sformułowana POZYTYWNIE

| Czym element JEST | Rola |
|---|---|
| komórka / `DataTemplate` kolumny siatki danych | `Text.Grid` |
| nagłówek kolumny siatki | `Text.GridHeader` |
| element dolnego paska statusu okna | `Text.Status` |
| nagłówek sekcji — SemiBold, nazywa temat, nie wartość | `Text.SectionHeader` |
| **element chromy przy 11 px** — panel, pasek, chip, podpis pomocniczy | `Text.Compact` ← **domyślna przy 11** |
| treść czytana świadomie — komunikat, opis, etykieta pola | `Text.Application` |
| tekst w pasku narzędzi przy 12 px | `Text.Toolbar` |
| podpis pomocniczy przy 10 px, chip skrótu | `Text.Caption` |
| edytor kodu **pełnowymiarowy** (13 px) | `Text.Code` |
| tytuł panelu / nazwa połączenia (14 px) | `Text.Title` |
| okno About (23 px) | `Text.Display` |

⚠ **Każdy wiersz mówi, czym element JEST** (decyzja architektoniczna 3). Domyślną odpowiedzią przy
11 px jest `Text.Compact`, a **nie** „coś, co nie jest siatką".
⚠ **Wartość MUSI zostać ta sama.** Jeżeli trafna rola niesie inną liczbę — to jest koszyk **D**.

### 5.3 ⭐ Jak dokumentować wyjątek (koszyk D)

Komentarz **w miejscu**, w XAML, obok wartości — nie w osobnym rejestrze:

```xml
<!-- ⚠ 9 px lokalnie, świadomie (M2c, §18.0.5/3): katalog nie ma roli o tej wartości,
     a `Text.Caption` (10) zmieniłby wygląd. Decyzja „9 → 10" należy do przeglądu §13.3. -->
<TextBlock FontSize="9" … />
```

Wymagane trzy elementy: **(a) że to świadome**, **(b) dlaczego rola nie pasuje**, **(c) kto to
rozstrzyga**. Dodatkowo wpis w `product-polish.md` §18 przy danej iteracji.

---

## 6. ⛔ Czego M2c NIE ROBI

1. ⛔ **Nie zmienia wyglądu.** Rola wchodzi w miejsce wartości **o tej samej wartości**. Różnica
   wizualna to **defekt do ZGŁOSZENIA**, nie do zaakceptowania.
2. ⛔ **Nie dodaje ról „bo pasuje"** (R3 — rola powstaje z kilku konsumentów).
3. ⛔ **Nie wciska istniejącej roli tylko po to, żeby licznik spadł** (R12).
4. ⛔ **Nie rusza `FontFamily`** (§3.5).
5. ⛔ **Nie rusza palety składni edytora** (§6.3 — zamrożona).
6. ⛔ **Nie zaczyna M3** — Status Bar 2.0, Toolbar, pasek zakładek, Metadata Explorer to M3.1–M3.4.
7. ⛔ **Nie zmienia niczego w `Themes/`** poza: (a) dopisaniem roli, jeżeli sweep udowodni jej brak
   **i** znajdzie drugiego konsumenta, (b) iteracją 9 (§3.8).
8. ⛔ **Nie „poprawia przy okazji"** proporcji zamkniętych w M2b (§17.3).
9. ⛔ **Nie rusza `Font.Code` bez konsumenta** — to stan ratyfikowany.

---

## 7. ⭐ Obowiązkowa kolejność w każdej iteracji

```
analiza (czytaj cały plik)
  → propozycja (tabela ról, akceptacja użytkownika)
    → implementacja
      → dotnet build            (0 błędów / 0 ostrzeżeń)
        → dotnet test           (TRZY partycje, osobno — §8.8)
          → smoke               (uruchom aplikację)
            → dokumentacja      (product-polish.md §18, iteracja po iteracji)
              → commit
                → push na oba remote'y — WYŁĄCZNIE po akceptacji użytkownika
```

⚠⚠ **Nigdy nie łącz `dotnet build` i `dotnet test` w jednym poleceniu — deadlock.**
⚠ **Zabij `EmberTern.exe` przed przebudową** — inaczej MSB3021/MSB3027.

### 7.1 Kryteria zakończenia POJEDYNCZEJ iteracji

| # | Warunek |
|---|---|
| 1 | Każda deklaracja w tym pliku ma przypisany koszyk i jest **albo zmigrowana, albo skomentowana** |
| 2 | **Wartości liczbowe nie zmieniły się** — ani jedna |
| 3 | Baza tego pliku w `DesignTokenComplianceTests` **obniżona do stanu faktycznego** (strażnik sprawdza obie strony — zawyżona baza wywala test) |
| 4 | Build 0/0 |
| 5 | Testy zielone w trzech partycjach |
| 6 | Smoke czysty |
| 7 | Wpis w `product-polish.md` §18 z numerem iteracji, wynikiem i ewentualnymi wyjątkami |
| 8 | Commit; push **po akceptacji** |

### 7.2 Definition of Done — CAŁE M2c

| # | Warunek |
|---|---|
| 1 | Każda pozostała wartość lokalna ma **komentarz z powodem** (R12 — nie liczba jest oceną) |
| 2 | Baza w `DesignTokenComplianceTests` odzwierciedla **stan faktyczny** |
| 3 | Build 0/0 |
| 4 | Suite zielony w trzech partycjach |
| 5 | Smoke czysty |
| 6 | ⭐ **Aplikacja wygląda IDENTYCZNIE jak przed M2c** — porównanie w obu motywach |
| 7 | §18 prowadzone iteracja po iteracji |
| 8 | Push na oba remote'y po akceptacji |

⚠ **Warunek 6 odróżnia M2c od M2b.** M2b włączał system; M2c **usuwa to, co go blokuje**, nie
zmieniając wyniku.

---

## 8. ⚠ Pułapki — zapłacone, nie płacić drugi raz

### 8.1 Wykryte w kroku 0 M2c

1. **⚠⚠ Goły `TextBlock` dziedziczy 14, nie 12** (§3.3). Usunięcie `FontSize` z `TextBlocka` to
   skok 11 → 14. **Dwa różne ruchy, nie jeden.**
2. **⚠⚠ Liczba nie wyznacza roli.** Pięć ról przy 11 px, dwie przy 12. Podmiana `sed`-em zachowa
   liczbę i wpisze złą rolę — **niewidoczne na ekranie i niewidoczne w teście**.
3. **⚠ Katalog bywa zamiarem, nie opisem.** Trzy zapisy w `Typography.axaml` i §4.2.2 mówiły
   o stanie, którego pomiar nie potwierdził. **Zanim zastosujesz zdanie z dokumentu — sprawdź je.**
4. **⚠ Wartość, która wygląda na dryf, bywa konsekwencją kontenera.** Sześć edytorów przy 12 px to
   nie „drugi rozmiar kodu", tylko edytory w wierszu siatki.
5. **⚠ Promień bywa arytmetyką, nie rolą.** `CornerRadius` = połowa boku to koło.

### 8.2 Odziedziczone z M2b (§17.5)

6. **⚠⚠ Arytmetykę §5.1 sprawdza się na SUMIE, nie na składniku.** Wiersz 22 − `Pad.Cell` (3+3)
   = **16 px** i wszystko w komórce musi się zmieścić. Trzy potknięcia, trzecie **wysłane**
   i niewidoczne przez pięć iteracji.
7. **⚠⚠ Wartość lokalna bije setter stylu** — teza etapu, ale i pułapka: po jej usunięciu kontrolka
   **nagle zaczyna słuchać systemu** i może wyglądać inaczej. To **ujawniony dług**, nie regresja
   sweepu. **Zgłoś, nie maskuj.**
8. **⚠⚠ Styl typu sięga do CUDZEGO szablonu — w obie strony.** Raz dał wysokość za darmo, raz
   wyśrodkował nagłówek `Expandera`.
9. **⚠ Podłoga wyrównuje tylko wtedy, gdy leży POWYŻEJ naturalnej szerokości** etykiet, które ma
   zrównać (80 przy „Cancel" = 98 było martwym zapisem).
10. **⚠ Deklarowana właściwość potrafi kłamać** — `RadioButton` raportował `MinHeight=0`, a żądał 32.
    **Sonduj drzewo.**
11. **⚠ Kolejność deklaracji rozstrzyga** między stylami o równej trafności — styl bazowy przed
    wariantami.
12. **⚠ Test potrafi mierzyć nie ten podmiot** — świadkiem mapowania Bridge'a jest kontrolka **bez**
    wariantu; ograniczenie mierzy się przeciw ograniczeniu.
13. **⚠ Reguła oparta na tagowaniu wymaga otagowania WSZYSTKICH instancji.** Dwa razy zabrakło jednej.
14. **⚠⚠ `{DynamicResource}` NIE rzuca przy brakującym kluczu** — literówka jest niewidoczna przy
    zielonym buildzie, a właściwość po cichu zostaje przy wartości odziedziczonej.
    **⭐ To jest ryzyko numer jeden tego etapu: sweep może wprowadzić je masowo.** Nazwy ról bierz
    z `Typography.axaml`, nie z pamięci, i po każdej iteracji **uruchom aplikację**.

### 8.3 Infrastruktura testów

15. **⚠⚠ TRZY partycje, `ConnectionExpandBindingProbe` biegnie SAM** (54 zielone, ~9 s); pozostałe
    **trzy** klasy headless razem (**33**, ~2 s); reszta (7000) osobno.
    Filtr: `--filter "FullyQualifiedName!~ConnectionExpandBindingProbe&FullyQualifiedName!~SettingsCenterViewTests&FullyQualifiedName!~BrandingPresentationTests&FullyQualifiedName!~DesignTokenApplicationTests"`
    i odwrotność z `|`.
    ⚠⚠ **POPRAWIONE 2026-08-02 (§18.1.6).** Filtr wymieniał wcześniej **`ContextMenuPresentationTests`** —
    klasę, która **nie istnieje** (jej testy wchłonął `ConnectionExpandBindingProbe`). Jako *wykluczenie*
    nazwa nie szkodzi, bo nie pasuje do niczego — i dlatego nikt nie zauważył, że suma „54 + 34 = 7088"
    jest o jeden za wysoka. Zmierzone na czystym `HEAD`: **7000 + 54 + 33 = 7087**.
16. **⚠ Test headless konstruujący `MainWindow` ZAWIESZA SIĘ.** Asercje wykonuj na najtańszej
    kontrolce, która może nieść daną cechę (`new Window()`), a nowa klasa headless **dołącza do
    `HeadlessCollection`**, nigdy nie zakłada własnego `IClassFixture` (#94/#226/#286).

---

## 9. Plan iteracji — po wynikach inwentarza

| # | Zakres | `FontSize` | Uwagi |
|---|---|---|---|
| ✅ 1 | `DebuggerTabView.axaml` + `.axaml.cs` | 85 + 6 | największe skupisko; 17 `FontFamily` **zostaje** |
| ✅ 2 | `DataImportTabView.axaml` | 82 | dużo koszyka A (`TextBox`/`ComboBox`/`NumericUpDown` 12); 4 × `CornerRadius="3"` migrują |
| ✅ 3 | `PerformancePanelView.axaml` | 42 | zawiera glif 9 px i glif 13 px → koszyk D; `CornerRadius` 6 to kapsuła → zostaje |
| ✅ 4 | `ProcedureDetailTabView` + `FunctionDetailTabView` | 40 + 41 | bliźniacze, ta sama struktura — migrować razem, żeby nie rozjechać |
| ✅ 5 | edytory obiektów: Table 27 · Trigger 22 · View 20 · Package 17 · Domain 16 · Generator 15 · Exception 13 · Index 11 | 141 | grupa; wiele `ae:TextEditor` 13 → `Text.Code` |
| ✅ 6 | monitory: `SessionManager` 26 · `TraceMonitor` 17 · `SecurityManager` 17 | 60 | ⚠ tu siedzi większość geometrii `CornerRadius` (koła, kapsuły) → zostaje |
| ✅ 7 | `MainWindow.axaml` 26 + `Controls/` 7 | 33 | ⭐ powierzchnia trwała (§0.1) — najwyższa staranność |
| ✅ 8 | dialogi — 18 plików po 1–9 | ~50 | ogon |
| ✅ 9 | literały w `ControlStyles.axaml` (§3.8) | ~10 setterów | poza licznikiem, ten sam dług |
| ✅ 10 | podniesienie bazy w `DesignTokenComplianceTests` do stanu faktycznego | — | krok końcowy; powód przy każdej pozostawionej pozycji |

⚠ Kolejność 1–3 wynika z handovera (największe skupiska); 4–8 z pokrewieństwa struktury, bo to ono
decyduje, czy reguła wyboru roli da się zastosować spójnie.

---

## 10. ⭐⭐ Reguła prowadząca

> **Użytkownik:** *„Dokument ma prowadzić produkt. Nie produkt dokument."*
> **Użytkownik (§15.11):** *„Pomiar nadal jest obowiązkowy. Ale nie jest już celem. Jest tylko
> narzędziem."*
> **Użytkownik (§18.0.8, R12):** *„Dokument ma odzwierciedlać architekturę produktu, a nie zmuszać
> produkt do spełniania wcześniejszych założeń, które zostały obalone pomiarami."*

Jeżeli znajdziesz rozwiązanie wyraźnie lepsze od zapisanego:
**propozycja → akceptacja → aktualizacja dokumentu → implementacja.**
⛔ Ani ciche odstępstwo, ani ślepa zgodność. ⛔ Decyzje D1–D12 i reguły R1–R12 zmienia wyłącznie
użytkownik.
