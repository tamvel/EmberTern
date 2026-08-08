# M4 · D‑M4‑1/D‑M4‑2 — GĘSTOŚĆ WIZUALNA: materiał decyzyjny

> **🔒 STATUS: DECYZJE RATYFIKOWANE, WDROŻONE I ODEBRANE PO QA UŻYTKOWNIKA (2026‑08‑08).**
> Dokument jest odtąd ZAPISEM, nie planem.
>
> Użytkownik rozstrzygnął: **A → A‑3 · B → B‑1 · C → C‑1 (30 px) · D → zgodnie z rekomendacją.**
> As‑built: `product-polish.md` **§19.37**. ⛔ Nie planować z tego dokumentu — jego §6 to rekomendacja
> sprzed decyzji, zachowana jako uzasadnienie, a nie jako lista do wykonania.
>
> ⭐⭐ **Razem z decyzjami padła REGUŁA OBOWIĄZUJĄCA W CAŁYM M4 (R18):** *„jeżeli dwa warianty są równie
> czytelne, wybieramy ten gęstszy. EmberTern jest narzędziem dla deweloperów baz danych, więc priorytetem
> jest ilość informacji widocznych na ekranie bez pogarszania czytelności."* Pełny zapis w handoverze §5.
>
> Materiał powstał na polecenie z 2026‑08‑08 („zacznij od D‑M4‑1 i przygotuj materiał decyzyjny dla całej
> grupy gęstości zgodnie z D‑M4‑2; nie wprowadzaj jeszcze zmian w kodzie"). W fazie materiału wszystko, co
> nazwane „wariantem", żyło w `tools/probes/VisualCandidateProbe`, nigdy w `src/`.

**Rendery** (oba motywy, `tools/probes/VisualCandidateProbe/out/`):

| plik | pytanie |
|---|---|
| `m4-a-ikona-chromy-{Dark,Light}.png` | **A** — rozmiar ikony na czterech powierzchniach chromy |
| `m4-b-wiersz-drzewa-{Dark,Light}.png` | **B** — wiersz drzewa: ikona + odstęp (kolizja **K15**) |
| `m4-c-wysokosc-wiersza-{Dark,Light}.png` | **C** — wysokość wiersza siatek definicji |
| `m4-d-pasek-importu-{Dark,Light}.png` | **D** — podłogi szerokości list w pasku importu |

Odtworzenie: `dotnet run --project tools/probes/VisualCandidateProbe -- density`

---

## §1 ⭐⭐ Pomiar wejściowy — i trzy zapisy, które go nie przetrwały

Wszystkie liczby poniżej są **wzięte z kodu**, nie z dokumentów (pułapka 3: *katalog bywa zamiarem, nie
opisem*). Trzy rzeczy okazały się inne, niż zapisano — i każda zmienia kształt pytania, więc nie są to
korekty kosmetyczne.

### §1.1 🔴 Domyślny rozmiar ikony to **16** i nie pochodzi z katalogu

`IconGeometries.axaml:259` — `ControlTheme` dla `SvgIcon` (i tak samo `DebuggerIcon`, `CreateIcon`) ma
`<Setter Property="Width" Value="16" />`. **Liczba wpisana wprost, nie rola.**

Zmierzony rozkład **355 deklaracji ikon** w aplikacji:

| zadeklarowany rozmiar | ile | uwaga |
|---|---:|---|
| **brak** → renderuje się **16** | **191** | największa grupa; 87 z nich w `MainWindow.axaml` |
| `Width="14"` | 75 | dokładnie wartość `Size.Icon`, ale literałem |
| `Width="15"` | 44 | wiersz drzewa — **K15** |
| `Width="16"` | 16 | redundantne z domyślną |
| `Size.Icon.Sm` (12) | 10 | poprawnie, na roli |
| `Width="12"` | 7 | wartość `Size.Icon.Sm`, literałem |
| `Width="13"` · `"11"` · `"10"` | 5 · 4 · 1 | ogon bez roli |
| **`Size.Icon`** (14) | **2** | pasek zakładek — **jedyny konsument roli** |

⭐⭐ **Siedem renderowanych rozmiarów ikon: 10, 11, 12, 13, 14, 15, 16.**

⚠⚠ **A teraz część, przez którą zapis „`Size.Icon` — 64 literały" opisywał inny problem, niż jest.**
Komentarz roli brzmi: *„Ikona chromy: **toolbar, zakładka, drzewo, wiersz menu**. Wartość domyślna dla nowej
ikony"* i niesie **14**. Zmierzone:

| powierzchnia | rzeczywisty rozmiar | skąd |
|---|---:|---|
| pasek narzędzi (tytuł) | **16** | 28 ikon bez rozmiaru + 3 jawnie |
| pasek narzędzi edytora | **16** | 52 bez rozmiaru + 11 młotków jawnie (2 wyjątki po 14) |
| wiersz zakładki | **14** | `Size.Icon` |
| wiersz drzewa | **15** | literał ×44 |
| wiersz menu kontekstowego | **14** | literał w `MenuMarkup.cs:76` |

⭐ **Rola opisuje jedną z czterech powierzchni, które wymienia, i nie opisuje wartości domyślnej, którą
deklaruje.** To nie jest problem „64 literałów do zamiany" — to rozjazd między katalogiem a produktem
(kształt gotchy #284 przeniesiony z tekstu na metrykę).

### §1.2 🟢 K15 jest **jedną rolą**, nie 112 rozproszonymi literałami

Zapis mówił: *„`Width="15"` i `Spacing="5"` mają w aplikacji 112 wystąpień w 17 plikach"* — i z tego
wyprowadzał wniosek, że zmiana w drzewie rozjechałaby drzewo z resztą aplikacji (**R7**).

Zmierzone: **44 ikony 15 px w 13 plikach, i 41 z nich to WIERSZ DRZEWA** — trzy szablony Metadata Explorera
(folder · połączenie · węzeł metadanych) plus szablon wiersza w **każdym drzewie „Zależności"** w edytorach
obiektów, plus drzewo wyników Global Search. To dokładnie ten sam zbiór 18 drzew, który w **M4.2b** idzie na
wspólny `TreeListView`. **39 z 69 `Spacing="5"` stoi bezpośrednio przy tej ikonie.**

Trzy wyjątki, które **nie** są wierszem drzewa: ikona ostrzeżenia w pasku wyników (`MainWindow.axaml:1837`),
ptaszek w komórce Session Managera, ikona wiersza w Trace Monitorze.

⭐ **Konsekwencja dla decyzji jest odwrotna do zapisanej: zmiana 15 → 14 nie rozprasza, tylko obejmuje
spójnie całą rolę „wiersz drzewa" w całej aplikacji.** Argument R7 nadal obowiązuje — ale przemawia
teraz **za** zmianą całej roli naraz, a nie przeciw niej.

⚠ Do tej samej roli należy **`Button.sidebar-chevron` 20×20** — pole trafienia chevronu, którego komentarz
sam odsyła do §13.3 *„razem z pozostałą gęstością drzewa (K15)"*.

### §1.3 🔴 Z‑3: liczby **40 px nie ma w kodzie**

Brama §13.3 zapisała *„wiersz Table Data 40 px wobec katalogowych 22 i 27 w bliźniaczej siatce"* i użytkownik
ratyfikował: **najpierw ustalić przyczynę.** Ustalona:

* `TableDetailTabView.axaml:46` — `DataGrid.data-edit DataGridRow` → **`Height="32"`**, wartość **stała**
  (nie `MinHeight`), obecna w tym pliku od commita `41e74d8`, czyli **na długo przed bramą**;
* siatka wyników SQL nie ma żadnego własnego stylu wiersza — bierze globalną podłogę `Size.Row.Grid` = **22**,
  a `Pad.Cell` (3+3) plus `Text.Grid.LineHeight` 15 daje 21, więc wiersz wychodzi **22**;
* **w całym `src/` nie ma ani jednej deklaracji wysokości wiersza równej 40 ani 27.**

⚠⚠ **Więc przesłanka Z‑3 nie jest przesłanką o produkcie — jest pomiarem ze zrzutu, którego nie potwierdza
kod.** Zwraca uwagę stosunek: 40 / 32 = 1,25 i 27 / 22 ≈ 1,23, przy czym w tej samej bramie titlebar (36),
wiersz zakładki (26) i pasek statusu (24) zmierzyły się **dokładnie** co do piksela. To wystarcza, żeby
**nie projektować niczego na liczbie 40**, i nie wystarcza, żeby orzec, skąd się wzięła.
⛔ **Z‑3 zostaje otwarte i wymaga ponownego pomiaru na żywej aplikacji** (§7). Nie wchodzi do pytania C.

---

## §2 Pytanie A — rozmiar ikony chromy

**Render:** `m4-a-ikona-chromy-{Dark,Light}.png` — cztery powierzchnie (wiersze) × cztery rozmiary (kolumny).
Kolumna „dziś" pokazuje stan faktyczny każdej powierzchni z osobna (16 / 14 / 15 / 14).

### Warianty

| # | wariant | co się zmienia | koszt |
|---|---|---|---|
| **A‑1** | **wszędzie 14** — „katalog ma rację" | pasek narzędzi 16 → 14 (191 ikon bez rozmiaru + 16 jawnych), drzewo 15 → 14 | największy; **zmienia najczęściej oglądaną powierzchnię aplikacji** |
| **A‑2** | **wszędzie 16** — „produkt ma rację" | zakładka 14 → 16, drzewo 15 → 16, menu 14 → 16 | zmienia **pasek zakładek (M3.3, odebrany)** i **menu kontekstowe (Keyboard Manager, odebrane)** |
| **A‑3** | **dwa poziomy chromy, oba nazwane** | pasek narzędzi zostaje 16 i **dostaje rolę**; wiersz (zakładka · drzewo · menu) = 14; jedyna zmiana widoczna to drzewo 15 → 14 | najmniejszy; katalog zaczyna opisywać produkt |
| **A‑0** | **nie ruszamy nic** | — | rozjazd katalog↔produkt zostaje; kolejna nowa ikona bez rozmiaru dalej dostaje 16 po cichu |

### ⭐ Co przemawia za A‑3, a czego nie widać z samego renderu

**Aplikacja ma już ratyfikowaną dokładnie taką dwupoziomową drabinę — dla KONTROLEK.** `Tokens.axaml`
zapisuje to wprost:

```
POLA (seria, wyrównanie)   →  Size.Control          24
AKCJE (cel, hierarchia)    →  Size.ControlToolbar   22
```

z uzasadnieniem: *„pole stoi w SERII i ma się wyrównywać, przycisk stoi SAMOTNIE i jest CELEM MYSZY"*.
Ikony mają **tę samą strukturę**: ikona w pasku narzędzi jest celem myszy stojącym samotnie, ikona w wierszu
drzewa/zakładki/menu stoi w serii obok tekstu 11 px. ⭐ **A‑3 nie wymyśla nowej zasady — stosuje istniejącą
i przyjętą do drugiego rodzaju elementu.**

⚠ **A‑1 i A‑2 mają wspólną wadę: obie zmieniają powierzchnię, która została już obejrzana i odebrana.**
A‑1 odchudza pasek narzędzi — ten sam, który brama §13.3 oceniła jako *jedyny* niedomagający, po czym M3.5
naprawiło go ikonami `CreateIcon` **narysowanymi i przyjętymi przy 16 px**. A‑2 pogrubia pasek zakładek
i menu, przyjęte w M3.3 i w Keyboard Managerze. To jest pułapka 17: reguła „chroma ma jedną liczbę" jest
prawdziwa, a jej doprowadzenie do końca kosztuje w obu kierunkach.

---

## §3 Pytanie B — wiersz drzewa (K15)

**Render:** `m4-b-wiersz-drzewa-{Dark,Light}.png` — 9 wierszy w skali 1:1 i powiększenie ×3.

| # | wariant | ikona | odstęp | uwaga |
|---|---|---:|---:|---|
| **B‑0** | dziś | 15 | 5 | żadna z liczb nie ma roli |
| **B‑1** | role katalogu | **14** (`Size.Icon`) | **4** (`Space.Xs`) | −2 px treści na wiersz; zamyka K15 |
| **B‑2** | rola + szerszy odstęp | 14 | 6 (`Space.Sm`) | −1 px; ikona i etykieta wyraźniej rozdzielone |

⚠ **Wysokość wiersza NIE jest tu przedmiotem.** `Size.Row.Tree` = 24 jest `MinHeight`, a ikona 15 mieści się
w niej tak samo jak 14 — pytanie jest wyłącznie o **gęstość poziomą** i o **wagę optyczną** ikony obok
etykiety `Text.Compact` (11).

⭐ Co widać na renderze: przy 1:1 różnica B‑0 ↔ B‑1 jest **na granicy dostrzegalności**; przy ×3 ikona 14
czyta się nieco lżej wobec tekstu 11 px. B‑2 rozdziela ikonę od etykiety wyraźniej i przez to nieco
„rozluźnia" wiersz — co jest ruchem w stronę **odwrotną** do gęstości.

⛔ **B nie jest niezależne od A.** Jeśli A rozstrzygnie się na A‑1 lub A‑3, wiersz drzewa idzie na 14
automatycznie i B sprowadza się do wyboru odstępu (4 czy 6). Jeśli na A‑2 — B‑1 przestaje mieć sens.
Dlatego oba renderowane są w jednym materiale (**D‑M4‑2**).

---

## §4 Pytanie C — wysokość wiersza siatek definicji

**Render:** `m4-c-wysokosc-wiersza-{Dark,Light}.png` — ta sama siatka w tym samym oknie 200 px, przy czterech
wysokościach wiersza, plus edytor `ComboBox` w komórce każdej z nich.

### Stan faktyczny — sześć liczb

| siatka | wysokość | edytor w komórce? |
|---|---:|---|
| Pola (`TableDetail #FieldsGrid`) · Nowa tabela | **34** | tak |
| Dane (`TableDetail .data-edit`) | **32** | tak |
| parametry / zmienne / kursory (Procedure · Function · Trigger) | **30** | tak |
| uprawnienia (Security Manager) | **28** | nie |
| `Security .checkbox-grid` | **34** | tak (`CheckBox`) |
| indeksy · ograniczenia (TableDetail) · kolumny (View) | **22** | **nie** |
| pozostałe (wyniki SQL, import, trace, sesje, debugger, pakiet, domena) | 22 (podłoga `Size.Row.Grid`) | nie |

### ⭐⭐ Liczba, która rozstrzyga pytanie

**Wszystkie** siatki definicji deklarują `DataGridCell` `Padding="6 2"` — pion **2 + 2 = 4**. Edytor w komórce
ma `Size.Control` = **24**. Minimalna wysokość wiersza, w której edytor się mieści, wynosi więc

```
4 (padding) + 24 (edytor) = 28
```

a produkt używa **30, 32 i 34**. ⭐ **Trzy liczby na jedno wymaganie, i żadna z nich z tego wymagania nie
wynika.** Render pokazuje `ComboBox` mieszczący się we wszystkich czterech wysokościach, łącznie z 28.

⚠ Wiersz **22** do tego pytania nie należy: te siatki są tylko do odczytu, więc podłoga `Size.Row.Grid`
jest dla nich poprawna. Pytanie dotyczy wyłącznie siatek **edytowalnych**.

### Warianty

| # | wariant | skutek |
|---|---|---|
| **C‑0** | zostaje 34 / 32 / 30 | cztery siatki tej samej rodziny wyglądają na cztery różne decyzje |
| **C‑1** | jedna rola przy **30** | Pola 34 → 30, Dane 32 → 30; parametry bez zmian; 2 px zapasu nad minimum |
| **C‑2** | jedna rola przy **28** | maksymalna gęstość, zero zapasu — każdy przyszły edytor wyższy niż 24 wymusi powrót do tematu |
| **C‑3** | jedna rola przy **32** | Pola 34 → 32, parametry 30 → 32 (**rozluźnienie** trzech siatek) |

⭐ Zysk gęstości, policzony: w oknie 600 px siatka przy 34 pokazuje ~16,9 wiersza, przy 30 — ~19,2, przy
28 — ~20,5. Różnica 34 → 30 to **+2,3 wiersza na ekran**.

⚠ **Pułapka #322 zastosowana do tego pytania:** reguła „siatka edytowalna potrzebuje N px" jest regułą
o KLASIE. Przed wdrożeniem trzeba sprawdzić **każdą** siatkę z osobna, czy jej edytor to na pewno
`Size.Control` 24 — `Security .checkbox-grid` (34) ma `CheckBox`, a nie pole, i może mieć własny powód.

---

## §5 Pytanie D — podłogi szerokości w pasku komend importu

**Render:** `m4-d-pasek-importu-{Dark,Light}.png`.

Zmierzone (sonda, `MinWidth` jak w produkcie):

| wariant | szerokość pasma |
|---|---:|
| dziś — `MinWidth` 170 / 170 / 180 | **695 px** |
| bez podłóg — szerokość z treści | **511 px** |
| wspólna podłoga 140 | 594 px |

Naturalna szerokość każdej listy osobno: **Profile 110 · Transaction 90 · Errors 137**.

⚠ **Dlaczego to w ogóle jest pytanie o gęstość:** pasmo B paska importu to `DockPanel` z `LastChildFill`,
a ostatnim dzieckiem jest **poziomy `StackPanel` przycisków, który się nie ściska, tylko OBCINA** (§19.33 —
tam ten sam mechanizm zjadł przyciski w trakcie importu). Każdy piksel podłogi jest pikselem zabranym
przyciskom.

⭐ **Trzy wnioski z pomiaru, których nie widać bez rozbicia na listy:**
1. `Errors` przy podłodze **180** ma treść **137** — podłoga jest o 43 px za szeroka i nic nie chroni;
2. `Transaction` przy podłodze **170** ma treść **90** — 80 px straty przy zamkniętym, trzyelementowym zbiorze;
3. `ComboBox` mierzy się do **najszerszej pozycji**, nie do zaznaczonej (`Errors` = 137 = *„Skip the row and
   continue"*, choć zaznaczone jest krótsze) — więc dla list o **stałym** zbiorze pozycji szerokość jest
   stabilna i podłoga nie jest potrzebna do stabilności układu.

⚠ **Jeden wyjątek, i ma powód:** zbiór pozycji listy `Profile` jest **zależny od użytkownika** (nazwy
profili), więc bez podłogi jej szerokość zmieniałaby się przy zapisaniu profilu — czyli dokładnie ten ruch
układu, który H‑3 zwalczał w pasku narzędzi.

---

## §6 ⭐ Rekomendacja — do ratyfikacji, nie do wdrożenia

> ⛔ To jest propozycja. **Decyzję o gęstości, typografii i rozmiarach ikon podejmuje użytkownik.**

| pytanie | rekomendacja | dlaczego |
|---|---|---|
| **A** | **A‑3** — dwa nazwane poziomy: pasek narzędzi **16** (nowa rola), wiersz **14** (`Size.Icon`) | stosuje **istniejącą, ratyfikowaną** drabinę „seria vs cel myszy" z `Size.Control`/`Size.ControlToolbar`; nie rusza dwóch powierzchni odebranych w M3.3 i Keyboard Managerze; jedyna zmiana widoczna to drzewo |
| **B** | **B‑1** — 14 + `Space.Xs` 4 | wynika z A‑3; zamyka K15 w całej roli naraz (13 plików, jeden wiersz drzewa), a nie łata jednego ekranu |
| **C** | **C‑1** — jedna rola przy **30** | jedyna wartość, którą trzy siatki już mają; 2 px zapasu nad zmierzonym minimum 28; +2,3 wiersza na ekran w Polach |
| **D** | zdjąć podłogi z `Transaction` i `Errors`, `Profile` na **140** | jedyna lista o zmiennym zbiorze pozycji zostaje ustabilizowana; ~154 px wraca przyciskom |

**Czego rekomendacja świadomie NIE obejmuje:**

* ⛔ **Z‑3** — dopóki liczba 40 nie zostanie potwierdzona na żywej aplikacji, nie ma czego projektować (§1.3);
* ⛔ **`Security .checkbox-grid` (34)** — inny edytor, własny powód, osobne sprawdzenie (#322);
* ⛔ **ogon 10 / 11 / 13 px** (10 ikon) — to pytanie o **role**, nie o gęstość; należy do sweepu M4.3;
* ⛔ **wysokości wierszy 22** w siatkach tylko do odczytu — podłoga jest dla nich poprawna.

**Co decyzja odblokowuje:** po niej rejestr **K1–K15** zostaje z pytaniami wyłącznie typograficznymi
(K1 · K3 · K4 · K6 · K8 — „ile mierzy pasek narzędzi i ile nagłówek sekcji") oraz parą odstępów chipa
(K11 + padding badge'a DEV MODE). Dopiero wtedy zaczyna się migracja ekranów (**D‑M4‑1**).

---

## §7 ⏸ Otwarte po tym materiale

| # | temat | dlaczego nie tutaj |
|---|---|---|
| **Z‑3** | wiersz Table Data — liczba 40 px nie ma pokrycia w kodzie | wymaga pomiaru na żywej aplikacji (skala DPI? inny element?); ratyfikowane „najpierw przyczyna" |
| **R‑6** | 150 % DPI | częściowo nieweryfikowalne headlessowo — sprawdzenie okiem po wdrożeniu |
| **§13.3a.5** | Settings Center jako powierzchnia UX | osobny temat, **D‑M4‑3** |
| **P‑1/P‑2/P‑3** | paleta pod rail | `color-language.md` §9.2 |

⚠ **Granica narzędzia, powtórzona za sondą:** `VisualCandidateProbe` **liczy układ raz**, więc odpowiada na
*„jak to wygląda"*, nigdy na *„czy to się ustala"* (§19.23.9). Żadne z pytań A–D nie dotyczy zbieżności, więc
granica nie boli — ale nie wolno na tej sondzie oprzeć decyzji o czymkolwiek, co reaguje na własny rozmiar.

⚠ **Rendery powstały w sondzie, nie w aplikacji.** Sonda ładuje te same słowniki i ten sam
`ControlStyles.axaml` co `App.axaml` (w M4 dołożono jej brakujący motyw `DataGrid` — bez niego siatka nie
miała szablonu i renderowałaby się jako nic), a geometrie ikon pobiera z zasobów **po kluczu**, nie kopiuje.
Mimo to obraz z sondy jest zapowiedzią, a **kryterium odbioru jest ekran aplikacji** (**R16**).
