# Data Import — etap I0: wyniki pomiarów i rekomendacje

**Status: ✅ I0 WYKONANY · WSZYSTKIE 8 REKOMENDACJI ZAAKCEPTOWANE przez użytkownika 2026-07-26 ·
wniesione do [data-import.md](data-import.md) v3, który jest od tej chwili 🔒 ZAMROŻONY.**
Data: 2026-07-26 · Zakres: wyłącznie pomiary, zero kodu produkcyjnego (`src/` 0 zmian).
Rola tego dokumentu po akceptacji: **archiwum dowodowe** — liczby i uzasadnienia, na które powołują się
wstawki „(I0)" w dokumencie projektowym. Nie jest już dokumentem decyzyjnym.

**Ślad akceptacji:** REK-1 przyjęty jako decyzja **D9** (`WriteAsync` przyjmuje wiersz do paczki ·
`FlushBatchAsync` zapisuje paczkę i zwraca wyniki wszystkich jej elementów · pipeline utrzymuje mapowanie
„indeks w paczce → numer wiersza źródłowego" i na tej podstawie buduje raport błędów). REK-2…REK-8 przyjęte
jako decyzja **D10**. Miejsca wniesienia: §6 poniżej.

---

## 0. Werdykt (dla niecierpliwych)

> **Architektura z `data-import.md` stoi. Nie wymaga korekt strukturalnych.**
> Wszystkie trzy filary (jeden pipeline · Core First · Single Source of Truth), trzy porty, model
> transakcyjny, §0 i model konfiguracji/profili przechodzą pomiary bez zmian.
>
> **Jedna rzecz wymaga Twojej decyzji, bo dotyka sygnatury portu `IImportWriter` (§4.3):**
> **REK-1** — paczkowanie (`FbBatchCommand`) jest **16× szybsze** od pętli i — co było warunkiem
> blokującym — **podaje dokładny indeks błędnego wiersza**. Żeby z tego skorzystać, `WriteAsync` nie może
> zwracać wyniku per wiersz, bo w chwili dodania wiersza do paczki błąd jeszcze nie istnieje.
>
> **Jedno ryzyko okazało się GROŹNIEJSZE, niż zakładał dokument** (R1): nieprzedstawialny znak nie jest
> odrzucany — jest **cicho podmieniany na `?`**. Architektura już przewidywała walidację lokalną, więc nie
> zmienia to projektu, ale zmienia jej status z „dobrej praktyki" na **warunek konieczny §0**.
>
> Pozostałe 6 rekomendacji to potwierdzenia i wytyczne implementacyjne — nie zmieniają projektu.

---

## 1. Co i czym zmierzono

Dwie jednorazowe sondy w `tools/probes/`, obie **poza `EmberTern.slnx`**, obie bez referencji do `src/`
(na etapie I0 nie ma jeszcze kodu produkcyjnego — mierzymy **silnik i biblioteki**, dokładnie jak
`Fb3ClosureProbe` mierzy silnik, a nie interpreter):

| Sonda | Pytania | Uruchomienie |
|---|---|---|
| `tools/probes/DataImportWriteProbe` | ścieżka zapisu do Firebirda: przepustowość (W), atrybucja błędu w paczce (B), koszt blokady per paczka (K), rozmiar paczki (S), BLOB w paczce (L), kody błędów (E), obcięcie tekstu (T), charset połączenia (C) | `$env:ET_LAB_PWD=…; dotnet run --project tools\probes\DataImportWriteProbe` |
| `tools/probes/DataImportXlsxProbe` | odczyt `.xlsx`: konstrukcje pułapkowe (G/X), strumieniowość (R), plik z prawdziwego Excela (F), premisa decyzji D2 (`.xls`) | `dotnet run --project tools\probes\DataImportXlsxProbe` (bez serwera i bez hasła) |

**Środowisko:** Firebird **WI-V5.0.3.1683** na `localhost:3050`; sterownik
`FirebirdSql.Data.FirebirdClient 10.3.4`; `DocumentFormat.OpenXml 3.1.0`; .NET 9.
**Baza:** świeża baza tymczasowa `C:\Temp\et_import_probe.fdb` (`DEFAULT CHARACTER SET WIN1250`), tworzona
i **usuwana** przez sondę. `Lab/EmberTern_Lab.fdb` **nietknięta**.
**Pliki `.xlsx`:** dwa generowane w `C:\Temp` (usuwane) + **odczyt struktury** rzeczywistego pliku
`Fantomy - Technologie - Lista dla Streamsoft-1.xlsx` (tego z Twoich zrzutów) — tylko do czytania, tylko
nazwy arkuszy, wymiar, rodzaje komórek i formaty liczbowe.

**Higiena DoD:** `git status` po I0 → `src/` **0 zmian**, `tests/` **0 zmian**, `EmberTern.slnx`
**0 zmian**. Nowe wyłącznie: ten dokument, `data-import.md` i dwa katalogi sond.

---

## 2. Wyniki — ścieżka zapisu (Firebird)

### 2.1. Przepustowość (W) — 50 000 wierszy, tabela 6 kolumn

| Wariant | rows/s | Uwaga |
|---|---:|---|
| W3 — naiwny: nowa komenda na wiersz, bez `Prepare` | **3 586** | wzorzec, którego unikamy |
| W1 — przygotowana komenda + re-bind, 1 transakcja | **7 313** | **2,0× szybszy od naiwnego** |
| W1b — jak W1, ale tabela z PK + indeksem | 6 738 | indeks kosztuje ~8% |
| W2 — jak W1, commit co 10 000 | 7 089 | |
| W2b — jak W1, commit co 1 000 | 7 141 | |
| W2c — jak W1, commit co 100 | 6 983 | |
| **W4 — `FbBatchCommand`, paczki po 1 000** | **105 747** | **14,5× szybszy od W1** |
| W4b — `FbBatchCommand`, paczki po 10 000 | 47 692 | **większa paczka jest GORSZA** |

**Wniosek 1:** przygotowana komenda + re-bind to 2× naiwnej pętli — decyzja projektowa potwierdzona.
**Wniosek 2:** częstotliwość commitu jest **praktycznie darmowa** (commit co 100 wierszy kosztuje 4,5%
względem jednego commitu na 50 000). Tryb `Batched` nie ma ceny wydajnościowej — jego jedyny koszt to
nieatomowość, którą projekt już ujawnia.
**Wniosek 3:** paczkowanie to inna liga (16×), ale **im większa paczka, tym gorzej** powyżej ~1 000.

### 2.2. Rozmiar paczki (S) — 50 000 wierszy

| Paczka | 100 | 250 | **500** | 1 000 | 2 000 | 5 000 | 20 000 |
|---|---:|---:|---:|---:|---:|---:|---:|
| rows/s (przebieg 1) | 101 742 | 119 437 | 116 388 | 110 008 | 92 945 | 66 084 | 32 085 |
| rows/s (przebieg 2) | 98 053 | 115 322 | **121 381** | 116 896 | 89 800 | 65 165 | 33 048 |

**Optimum to 250–1 000; rekomendacja domyślnej wartości: 500.** Powyżej 2 000 wydajność spada liniowo
(przy 20 000 jest już 3,7× gorzej niż w optimum) — prawdopodobnie koszt budowania bufora paczki po
stronie klienta. `BatchBufferSize` domyślnie **16 777 216** (16 MB), więc to nie limit bufora ogranicza.

### 2.3. ⭐ Atrybucja błędu w paczce (B) — warunek blokujący, ODPOWIEDŹ POZYTYWNA

Paczka 5 000 wierszy, wiersz o indeksie **2 500** narusza `NOT NULL`.

| Test | Wynik |
|---|---|
| B0 | `MultiError` domyślnie **False**; `BatchBufferSize` domyślnie 16 MB |
| B1 — `MultiError = false` | **nie rzuca wyjątku**; `AllSuccess=False`, `Count=**2501**` — czyli paczka **zatrzymała się na błędnym wierszu**, a wynik ma tyle elementów, ile prób wykonano; element `[2500]` niesie `FbException` |
| B2 — `MultiError = true` | `Count=**5000**` (== rozmiar paczki), dokładnie **jeden** element nieudany, pod indeksem **2500** — **wyrównanie 1:1 z kolejnością dodawania** |
| B2 — trwałość | w transakcji wylądowało **4 999 z 5 000** — dobre wiersze przechodzą obok błędnego |
| B3 — pętla przygotowana + `try/catch` per wiersz | atrybucja dokładna (wiersz 2500), 4 999 zapisanych, transakcja **przeżywa** błąd; koszt `try/catch`: 6 052 rows/s (~-17% względem W1) |

**To jest kluczowy wynik I0.** Ryzyko R7 zakładało, że paczkowanie może wygrać wydajnościowo, ale przegrać
na poprawności, bo raport (§3.7) wymaga numeru wiersza. Pomiar mówi: **paczka podaje indeks błędnego
wiersza, a `MultiError` odwzorowuje się 1:1 na `ImportErrorPolicy`**:

| `ImportErrorPolicy` | `FbBatchCommand` |
|---|---|
| `StopOnFirstError` (domyślny, D4) | `MultiError = false` — zatrzymuje się **na** błędnym wierszu, `Count` wskazuje który |
| `SkipInvalidRows` | `MultiError = true` — leci dalej, każdy nieudany indeks jest raportowany |

Nie ma tu żadnej utraty semantyki: polityka błędów, którą projekt zdefiniował niezależnie, ma bezpośredni
odpowiednik w sterowniku.

### 2.4. Koszt blokady per paczka (K)

| Wariant (paczka 500) | rows/s |
|---|---:|
| K1 — bez blokady | 116 965 |
| K2 — `SemaphoreSlim` brany i zwalniany **per paczka** (100 par na 50 000 wierszy) | 111 860 |

Pomiar pokazał różnicę 4,36%, ale **ta liczba to szum, nie koszt blokady**: ten sam wariant „paczka 500"
w fazie S dał 116 388 i 121 381 w dwóch przebiegach, czyli rozrzut ~4% występuje bez żadnej blokady.
100 nieobciążonych par `WaitAsync`/`Release` to rzędu mikrosekund wobec ~430 ms pracy — poniżej progu
mierzalności tą metodą. **Wniosek: trzymanie `CommandLock` per paczka jest darmowe** (gotchy
#98/#120/#236 nie wchodzą w konflikt z wydajnością). Uczciwie: nie zmierzyliśmy kosztu blokady, bo jest
mniejszy niż szum — i to jest wystarczająca odpowiedź.

### 2.5. BLOB w paczce (L)

| Test | Wynik |
|---|---|
| L1 — 20 000 znaków do `BLOB SUB_TYPE TEXT`, przygotowana komenda | **PASS** — round-trip bez straty |
| L2 — to samo przez `FbBatchCommand` | **PASS** — paczka przyjmuje BLOB-y, round-trip bez straty |

**Nie jest potrzebna żadna ścieżka awaryjna** dla kolumn BLOB (obawa z planu I0 nie potwierdziła się).
`ColumnTypeInferencer` może więc bezpiecznie proponować `BLOB SUB_TYPE TEXT` dla bardzo długich tekstów.

### 2.6. ⭐ Kody błędów (E) — mapowanie musi czytać WEKTOR GDS, nie `ErrorCode`

| Klasa błędu | SQLSTATE | `ErrorCode` (wiodący GDS) | pełny wektor GDS |
|---|---|---|---|
| `NOT NULL` | 23000 | 335544347 | `[335544347, 0, 0, 335544347]` |
| PK duplikat | 23000 | 335544665 | `[335544665, 0, 0, 335545072, 0, 335544665]` |
| UNIQUE duplikat (**ograniczenie**) | 23000 | **335544665** | `[335544665, 0, 0, 335545072, 0, 335544665]` |
| ⭐ UNIQUE duplikat (**samodzielny `CREATE UNIQUE INDEX`**) — **DOMIAR I4, 2026-07-26** | 23000 | **335544349** | `[335544349, 0, 335545072, 0, 335544349]` |

> ⭐ **Uzupełnienie zmierzone w I4 (2026-07-26) — I0 nie było błędne, było niekompletne.** I0 sprawdziło
> klucz główny i **ograniczenie** `UNIQUE` i słusznie stwierdziło, że są nierozróżnialne. Nie sprawdziło
> natomiast **indeksu unikalnego założonego osobno** (`CREATE UNIQUE INDEX`), który wiedzie **innym kodem**
> — `335544349` (`isc_no_dup`, *„attempt to store duplicate value … in unique index"*). Do czasu tego pomiaru
> import raportował duplikat na takim indeksie jako ogólny `ServerError`. Oba przypadki mapują się na
> `ImportErrorKind.ServerUniqueViolation` — dla użytkownika to jedno zdarzenie („ta wartość już tam jest"),
> a to, który mechanizm je wymusił, nie jest informacją, do czego raport mógłby jej użyć.
> **To jest dokładnie powód, dla którego DoD etapu I4 wymaga przebiegu na żywym silniku, a nie zaufania
> wcześniejszemu pomiarowi.**
| CHECK | 23000 | 335544558 | `[335544558, 0, 0, 335544842, 0, 335544558]` |
| FK — brak rodzica | 23000 | 335544466 | `[335544466, 0, 0, 335544838, 335545072, 0, 335544466]` |
| **tekst za długi** | 22000 | **335544321** | `[335544321, **335544914**, 335545033, **10, 16**, 335544321]` |
| **przekroczenie zakresu liczby** | 22000 | **335544321** | `[335544321, **335544916**, 335544321]` |
| **błąd transliteracji** | 22000 | **335544321** | `[335544321, **335544565**, 335544321]` |

Dwie rzeczy wychodzą z tej tabeli i obie są wiążące dla I4:

1. **Trzy zupełnie różne klasy błędu mają identyczny `ErrorCode` i identyczny SQLSTATE.** „Tekst za
   długi", „liczba poza zakresem" i „nie da się przetransliterować znaku" to `335544321` / `22000`.
   Rozróżnia je **dopiero drugi element wektora GDS** (`335544914` / `335544916` / `335544565`). Gdyby
   mapowanie — wzorem `DebugErrorMapper`, który klucza na wiodącym kodzie — czytało tylko `ErrorCode`,
   raport importu mówiłby „błąd arytmetyczny" na tekst o 6 znaków za długi. To dokładnie ta klasa
   nieprzydatnego komunikatu, którą §8 pkt 10 wyrzuca IBExpertowi.
2. **Wektor niesie użyteczne parametry**: przy obcięciu tekstu są w nim **limit (10) i rzeczywista długość
   (16)** — czyli raport może napisać „26 znaków, limit 20" **z danych serwera**, bez parsowania tekstu
   komunikatu.

Dodatkowo: **PK i UNIQUE są nierozróżnialne po kodzie** (oba `335544665`). Raport powinien mówić
„naruszenie unikalności (klucz główny lub unikalny)" i nie udawać, że wie który — zgodnie z §0.

### 2.7. Tekst za długi (T) — brak cichego obcięcia

40 znaków do `VARCHAR(20)` → **`FbException`, wiersz odrzucony**. Firebird **nigdy nie obcina po cichu**.
Opcja „przytnij wartości łańcuchowe" pozostaje więc czystą wygodą użytkownika (§0.2), a nie zabezpieczeniem
przed czymś, co dzieje się samo. Komunikat jest jednak ogólny (*„arithmetic exception, numeric overflow, or
string truncation"*) — patrz §2.6 pkt 1.

### 2.8. ⭐⭐ Charset połączenia (C) — POTWIERDZONA CICHA PODMIANA

Baza `WIN1250`, tabela z kolumną `WIN1250` i kolumną `UTF8`. Wartość zapisywana jednym połączeniem,
**czytana z powrotem połączeniem UTF8** (jedyny charset zdolny pokazać, co naprawdę zostało zapisane).

| Test | Połączenie | Kolumna | Wartość | Wynik |
|---|---|---|---|---|
| **C1** | WIN1250 | WIN1250 | `Ж` | 🔴 **zapisano `?` — bez żadnego błędu** |
| **C2** | WIN1250 | **UTF8** | `Ж` | 🔴 **zapisano `?` — bez żadnego błędu** |
| **C2b** | WIN1250 | **UTF8** | `中` | 🔴 **zapisano `?` — bez żadnego błędu** |
| C3 | UTF8 | UTF8 | `Ж` | ✅ round-trip bez straty |
| C4 | UTF8 | WIN1250 | `Ж` | ✅ **odrzucone przez serwer** (`FbException`, GDS `335544565`) |
| C5 | WIN1250 | WIN1250 | `ąćęłńóśźż` | ✅ kontrola — round-trip OK |
| C6 | WIN1250 | WIN1250 | `€` | ✅ kontrola — `€` jest w WIN1250 (0x80) |
| C7 | UTF8 | WIN1250 | `€` | ✅ round-trip OK |

**To jest najważniejsze odkrycie I0 i jest gorsze, niż opisywało ryzyko R1.** R1 mówiło: „wiersze
odrzucone w połowie importu". Rzeczywistość: **wiersze przechodzą, a dane są uszkodzone**, bo .NET-owy
`Encoding.GetEncoding(1250)` domyślnie zamienia nieprzedstawialny znak na `?` (fallback zastępczy), i
serwer nigdy nie dowiaduje się, że coś zginęło.

Trzy konsekwencje, które trzeba wypowiedzieć wprost:

1. **Decyduje charset POŁĄCZENIA, nie kolumny.** Test C2 to dowód: kolumna `UTF8` **mogłaby** pomieścić
   `Ж`, ale połączenie `WIN1250` zniszczyło znak, zanim dane dotarły do serwera. Projekt ma to poprawnie —
   §4.4 krok 4 mówi „reprezentowalność w **charsecie połączenia**" — i teraz wiemy, dlaczego to sformułowanie
   jest jedyne prawidłowe.
2. **Serwer nas nie obroni.** Jedyny przypadek, w którym Firebird protestuje (C4), to połączenie UTF8 →
   kolumna WIN1250. Przy połączeniu WIN1250 nie ma żadnego sygnału.
3. **Walidacja lokalna nie jest optymalizacją — jest warunkiem §0.** Bez niej moduł importu jest wektorem
   cichej korupcji danych, czyli narusza regułę #1 projektu.

---

## 3. Wyniki — odczyt `.xlsx`

### 3.1. Jak prezentują się konstrukcje pułapkowe (X)

Wygenerowany arkusz zawierał: nagłówek (2× shared string + 1× inline), liczbę, datę w formacie wbudowanym,
datę w formacie własnym, `Boolean`, formułę z wartością zbuforowaną, komórkę błędu `#N/A`, długi tekst,
liczbę dziesiętną, wiersz z **brakującą komórką środkową** i **przerwę w numeracji wierszy**.

| Konstrukcja | Jak wygląda dla czytnika |
|---|---|
| shared string | `DataType=SharedString`, `CellValue` = **indeks** do `SharedStringTable` |
| inline string | `DataType=InlineString`, **`CellValue` jest NULL** — tekst siedzi w `InlineString/Text` |
| liczba | `DataType` **null** (brak atrybutu), `CellValue` = tekst liczby, InvariantCulture |
| data (format wbudowany) | `DataType` **null**, `CellValue='45000'`, `StyleIndex→CellFormat.NumberFormatId=14` ⇒ `1900-01-01`-owy numer seryjny → `2023-03-15` |
| data (format własny) | jak wyżej, `NumberFormatId=164`, kod formatu `dd\.mm\.yyyy` w `NumberingFormats` |
| `Boolean` | `DataType=Boolean`, `CellValue='1'` / `'0'` |
| formuła | `CellFormula` + `CellValue` = **wartość zbuforowana** (nadaje się do importu) |
| komórka błędu | `DataType=Error`, `CellValue='#N/A'` |
| `SheetDimension` | **NIEOBECNY** w pliku pisanym OpenXml-em (obecny w pliku z Excela — §3.3) |

### 3.2. ⭐ Dwie pułapki, które są wektorami cichego przekłamania

**X4 — brakująca komórka środkowa.** Wiersz z pustą kolumną B daje **`[A3, C3]`** — komórki B **nie ma**,
nie jest „pusta". Czytnik dopisujący wartości pozycyjnie umieściłby zawartość C w kolumnie B, czyli
**przesunąłby całą resztę wiersza o jedną kolumnę** — bez żadnego błędu. To §0.1 w czystej postaci.
⇒ **Provider musi umieszczać wartości po `CellReference`, nigdy po kolejności wystąpienia.**

**X5 — przerwa w numeracji wierszy.** Arkusz z pustymi wierszami 8 i 9 daje `rows = [1,2,3,4,5,6,7,10]` —
puste wiersze są **nieobecne**. ⇒ **Numer wiersza źródłowego musi pochodzić z `Row.RowIndex`, nigdy z
własnego licznika** — inaczej raport błędów wskaże zły wiersz, czyli skłamie (§0.6).

*Uczciwe ograniczenie pomiaru:* X4 dowodzi, że **format to dopuszcza** (i tak trzeba to obsłużyć, bo koszt
obsługi jest zerowy, a koszt braku obsługi to przesunięcie kolumn). Rzeczywisty plik z §3.3 okazał się
w pełni wypełniony, więc na wyjściu z Excela ten przypadek **nie został zaobserwowany** — nie twierdzę,
że został.

### 3.3. Plik z prawdziwego Excela (F) — struktura

`Fantomy - Technologie - Lista dla Streamsoft-1.xlsx`, 305 KB:

| Fakt | Wartość |
|---|---|
| arkusze | `[0] Arkusz1` |
| `SheetDimension` | **`A1:E8724`** — obecny (w przeciwieństwie do pliku pisanego OpenXml-em) |
| tablica shared strings | **8 261** pozycji |
| formaty | 57 wpisów `xf`; jeden własny numFmt `164 = '#,##0\ [$€-1];[Red]\-#,##0\ [$€-1]'` (waluta, **nie data**) |
| rozmiar | 8 724 wiersze / 43 620 komórek (= 8 724 × 5, arkusz w pełni wypełniony) |
| komórki będące liczbą z formatem daty | **0** |
| kolumna A | `SharedString × 8 724` |
| **kolumna B** | **`Number × 8 723` + `SharedString × 1`** |
| kolumna C | `SharedString × 8 724` |
| kolumna D | `SharedString × 8 724` |
| **kolumna E** | **`Number × 5 805` + `SharedString × 2 919`** |

Trzy obserwacje:

1. **Excel zapisuje teksty jako shared strings** — provider **musi** czytać `SharedStringTable`. Jej
   rozmiar jest proporcjonalny do liczby **różnych** tekstów (tu 8 261), nie do liczby wierszy.
2. **`SheetDimension` istnieje w plikach z Excela** — nadaje się jako **wskazówka** do oszacowania liczby
   wierszy (pasek postępu), ale nigdy jako prawda (brakuje go w plikach generowanych programowo).
3. ⭐ **Dwie z pięciu kolumn są typowo MIESZANE.** Kolumna B („Nr technologii") ma 8 723 liczby i **jeden**
   tekst; kolumna E ma 5 805 liczb i 2 919 tekstów. To realne dane użytkownika, nie przypadek
   laboratoryjny — i to jest empiryczne uzasadnienie §0.3 (przy niejednoznaczności wygrywa `VARCHAR`).
   Ma to też ostrzejszą konsekwencję dla I8 — patrz REK-7.

### 3.4. Strumieniowość (R) — 100 000 wierszy × 5 kolumn

| Metoda | czas | przyrost sterty |
|---|---:|---:|
| `OpenXmlReader` (SAX), wiersz po wierszu | 1,97 s | **3,9 MB** |
| `Worksheet.Descendants<Cell>()` (DOM) | 2,52 s | **300,5 MB** |

Ta sama liczba komórek (500 000), **77× mniej pamięci**. ⇒ **Provider musi używać `OpenXmlReader`.**
Ryzyko R8 jest realne i ma jednoznaczne rozwiązanie: DOM na pliku 1 M wierszy zająłby ~3 GB.

### 3.5. Premisa decyzji D2 (`.xls`)

`SpreadsheetDocument.Open` na prawdziwym pliku BIFF (`Nadgodziny2.xls`, 360 KB) →
**`FileFormatException: File contains corrupted data.`**
⇒ **D2 potwierdzone**: `DocumentFormat.OpenXml` nie czyta `.xls`, obsługa wymaga innej biblioteki.
Odłożenie `.xls` poza MVP było trafne, a komunikat „zapisz jako .xlsx" jest jedyną uczciwą odpowiedzią do
czasu etapu I10.

---

## 4. Rekomendacje

Każda pozycja: **co zmierzono → co z tego wynika → czy zmienia projekt.**

### REK-1 ⭐ — paczkowanie jako domyślna ścieżka zapisu. **WYMAGA TWOJEJ DECYZJI** (dotyka §4.3)

**Pomiar:** paczka jest 16× szybsza od pętli przygotowanej (105 747 vs 7 313 rows/s przy optimum 500:
~121 000), **podaje dokładny indeks błędnego wiersza** (B2), przyjmuje BLOB-y (L2), a `MultiError`
odwzorowuje się 1:1 na obie polityki błędów.

**Co z tego wynika:** `FirebirdImportWriter` powinien pisać paczkami (domyślnie 500 wierszy), a nie pętlą
wiersz po wierszu. Dla 1 M wierszy to różnica **~2,3 minuty vs ~8 sekund**.

**Dlaczego to dotyka projektu:** §4.3 szkicuje port jako

```
WriteAsync(ImportRow) → ImportWriteResult    // Ok | RowError
```

W paczce **w chwili dodania wiersza błąd jeszcze nie istnieje** — pojawia się przy wysłaniu paczki. Wynik
per wiersz w tej sygnaturze jest więc nie do utrzymania.

**Proponowana korekta (minimalna):**

```
WriteAsync(ImportRow) → void                  // wiersz przyjęty do bieżącej paczki
FlushBatchAsync() → IReadOnlyList<ImportRowError>   // błędy tej paczki, z numerami wierszy ŹRÓDŁOWYCH
CompleteAsync()   → ImportWriteSummary
```

`ImportPipeline` trzyma okno `indeks w paczce → SourceRowNumber` (wielkości paczki, czyli 500 pozycji) i
tłumaczy indeksy sterownika na numery wierszy źródłowych, zanim cokolwiek dotrze do raportu. Reszta
architektury **bez zmian**: `DryRunImportWriter` implementuje ten sam kontrakt (jego `FlushBatchAsync`
zwraca błędy walidacji, których i tak nie wysyła), polityki błędów zostają, raport zostaje, §0.6 zostaje.

**Alternatywa, jeśli tej zmiany nie chcesz:** zostajemy przy pętli przygotowanej (7 313 rows/s) i port jest
bez zmian. Wtedy import 1 M wierszy trwa ~2,3 minuty zamiast ~8 sekund. Nie jest to katastrofa — to
świadomy wybór prostszego kontraktu za cenę 16×.

**Rekomendacja: przyjąć korektę.** Rekomendacja opiera się na tym, że warunek blokujący (atrybucja wiersza)
został spełniony — bez niego odpowiedź byłaby odwrotna.

### REK-2 ⭐ — walidacja charsetu staje się obowiązkowa. **Potwierdza projekt, podnosi wagę R1**

**Pomiar:** C1/C2/C2b — nieprzedstawialny znak jest **cicho zamieniany na `?`**, bez błędu, także wtedy,
gdy kolumna docelowa (UTF8) mogłaby go pomieścić.

**Co z tego wynika (trzy konkrety, wszystkie mieszczą się w istniejącej architekturze):**
1. `ImportRowValidator` **musi** sprawdzać każdą wartość tekstową względem **kodowania połączenia** (nie
   kolumny) — przez `Encoding.GetEncoding(<charset połączenia>)` z fallbackiem **rzucającym wyjątek**
   (`EncoderExceptionFallback`), nigdy domyślnym zastępczym. To jedno ustawienie odróżnia „wykryjemy" od
   „uszkodzimy".
2. Nowa wartość `ImportErrorKind.NotRepresentableInConnectionCharset` — już zapowiedziana w R1, teraz z
   dowodem, że jest niezbędna.
3. **`ImportReadiness` dostaje pozycję ostrzegawczą**, gdy charset połączenia nie jest UTF8 **i** próbka
   źródła zawiera znaki spoza niego — czyli użytkownik dowiaduje się **przed** importem, z podpowiedzią
   „połącz się w UTF8". `Waliduj` (dry-run) wykrywa to na całym pliku.

**Zmiana projektu: NIE.** §4.4 krok 4 i R1 już to przewidują; zmienia się tylko status z „warto" na
„warunek §0" oraz treść R1 (z „wiersze odrzucone" na „cicha korupcja").

### REK-3 — mapowanie błędów czyta wektor GDS. **Bez zmian w architekturze, wiążące dla I4**

**Pomiar:** §2.6 — `string truncation` / `numeric overflow` / `transliteration` mają **identyczny**
`ErrorCode` (335544321) i SQLSTATE (22000); rozróżnia je drugi element wektora GDS. Wektor obcięcia niesie
limit i rzeczywistą długość. PK i UNIQUE są po kodzie nierozróżnialne.

**Co z tego wynika:** `FbException → ImportErrorKind` klucza na **parze (wiodący GDS, kolejny GDS)**, nie
na `ErrorCode`. Nadal **zero parsowania tekstu** — dyscyplina `DebugErrorMapper` obowiązuje, tylko klucz
jest bogatszy. Parametry z wektora (limit, długość) idą do raportu jako liczby. PK/UNIQUE raportujemy jako
„naruszenie unikalności", bez udawania precyzji.

### REK-4 — brak cichego obcięcia potwierdzony. **Bez zmian**

Firebird odrzuca tekst za długi. Opcja „przytnij" pozostaje wygodą opt-in (§0.2), a nie osłoną przed
zachowaniem silnika.

### REK-5 — domyślne wartości liczbowe. **Bez zmian w architekturze**

| Ustawienie | Rekomendacja | Podstawa |
|---|---|---|
| rozmiar paczki | **500** | optimum 250–1 000; spadek 3,7× przy 20 000 (§2.2) |
| `Batched` — commit co N | **10 000** | commit jest praktycznie darmowy (§2.1), więc N wybieramy dla czytelności raportu, nie dla wydajności |
| próg ostrzeżenia „długa transakcja" | **100 000 wierszy** (jak w R4) | przy 121 k rows/s to ~1 s pracy, więc ostrzeżenie dotyczy czasu życia transakcji, nie czasu importu |
| `CommandLock` | brany **per paczka** | koszt poniżej progu mierzalności (§2.4) |

### REK-6 — wytyczne dla providera XLSX (I9). **Bez zmian w architekturze**

1. **`OpenXmlReader` (SAX), nigdy DOM** — 77× mniej pamięci (§3.4).
2. **Wartości umieszczane po `CellReference`** — brakująca komórka środkowa jest nieobecna, nie pusta
   (§3.2, X4).
3. **Numer wiersza źródłowego z `Row.RowIndex`** — puste wiersze są nieobecne (§3.2, X5).
4. **Data = liczba + `numFmtId`** będący formatem daty (wbudowane 14–22, 45–47, albo własny kod
   zawierający `y`/`d`/`h`/`s`). Opcja „traktuj komórki dat jako daty" zostaje; przy niejednoznaczności
   wartość idzie jako liczba **i podgląd to pokazuje**. *Uczciwie: w pliku użytkownika nie było ani jednej
   komórki daty, więc obsługa dat jest zaprojektowana na podstawie arkusza wygenerowanego, nie na wyjściu
   z Excela.*
5. **`SharedStringTable` czytana raz** — Excel zapisuje teksty jako shared strings (§3.3).
6. **`SheetDimension` tylko jako wskazówka** do oszacowania postępu — bywa nieobecny.
7. **Formuła: brać wartość zbuforowaną**; **komórka błędu (`#N/A`) to nie tekst** — musi zostać **błędem
   wiersza**, a nie wartością `"#N/A"` wstawioną do `VARCHAR`. Proponuję dodatkową, **addytywną** opcję w
   `ImportBehaviorOptions`: „komórki błędu Excela importuj jako NULL" (domyślnie wyłączona ⇒ błąd wiersza).
   To jedno pole w istniejącym rekordzie — bez zmian w architekturze.

### REK-7 — wnioskowanie typów: skanuj całość, nie próbkę. **Bez zmian w architekturze, zmiana domyślnej polityki I8**

**Pomiar:** w Twoim własnym pliku kolumna B ma 8 723 liczby i **1** tekst, kolumna E — 5 805 liczb i
2 919 tekstów (§3.3).

**Co z tego wynika:** wnioskowanie z próbki 240 wierszy zaproponowałoby dla kolumny B `INTEGER`, a import
padłby na jednym wierszu z 8 724 — po utworzeniu i **zatwierdzeniu** tabeli (§4.5), czyli w najgorszym
możliwym momencie. Rekomendacja: **domyślnie skanować całe źródło** przy wnioskowaniu typów (plik i tak
jest czytany dwukrotnie: raz do schematu/podglądu, raz do importu), z limitem bezpieczeństwa (np. 1 M
wierszy) i **zawsze podaną liczbą przeanalizowanych wierszy** w kolumnie „Podstawa". Kolumna mieszana
spada do `VARCHAR` — §0.3 już tak mówi, pomiar tylko pokazuje, jak często to się dzieje w praktyce.

### REK-8 — D2 potwierdzone. **Bez zmian**

OpenXml odrzuca `.xls` (`FileFormatException`). Plan I10 i komunikat „zapisz jako .xlsx" zostają.

---

## 5. Czy architektura wymaga korekt?

| Element projektu | Werdykt |
|---|---|
| Trzy filary (jeden pipeline · Core First · Single Source of Truth) | ✅ bez zmian |
| `IImportSource`, `IImportProvider` | ✅ bez zmian |
| **`IImportWriter` — sygnatura** | ⚠️ **REK-1: korekta semantyki `WriteAsync`/`FlushBatchAsync` — do Twojej akceptacji** |
| `ImportPipeline` — kolejność i etapy (§4.4) | ✅ bez zmian; dochodzi okno „indeks w paczce → numer wiersza źródłowego" **wewnątrz** kroku 5 (jeśli REK-1) |
| Model transakcyjny (§4.5): linia Data / Ddl / Metadata, #213 | ✅ bez zmian; commit okazał się darmowy, co czyni `Batched` tanim |
| `ImportConfiguration` + profile (§4.8) | ✅ bez zmian |
| `ImportReadiness` | ✅ bez zmian; dochodzi jedna pozycja ostrzegawcza (charset) — dane, nie architektura |
| `ImportValueConverter` / `ImportRowValidator` | ✅ bez zmian; REK-2 podnosi walidację charsetu do warunku §0 |
| `ColumnTypeInferencer` | ✅ bez zmian; REK-7 zmienia domyślny **zakres próbki** |
| §0 (siedem konsekwencji) | ✅ **wzmocnione pomiarami** — §0.1 i §0.2 mają teraz twarde dowody (C1/C2, X4, X5) |
| Ryzyka R1, R3, R7, R8 | ✅ zmierzone; R1 wymaga **przeredagowania na „cichą korupcję"**, R7 rozstrzygnięte na korzyść paczek, R8 potwierdzone z rozwiązaniem |
| Etapy I1–I12 | ✅ bez zmian w kolejności ani zakresie |

**Podsumowanie: architektura może zostać ostatecznie zamrożona przed I1** — pod warunkiem rozstrzygnięcia
REK-1 (jedna sygnatura portu). Wszystkie pozostałe rekomendacje to uszczegółowienia, które wchodzą do
`data-import.md` jako doprecyzowania faktów, nie zmiany decyzji.

---

## 6. Gdzie to zostało wniesione (✅ wykonane 2026-07-26)

| Miejsce w `data-import.md` | Zmiana | Stan |
|---|---|---|
| nagłówek + historia wersji | v3, blok „🔒 DOKUMENT ZAMROŻONY" | ✅ |
| §4.3 (kontrakty) | nowa semantyka `WriteAsync` / `FlushBatchAsync` + akapit „Dlaczego `WriteAsync` NIE zwraca wyniku wiersza" + rola `DryRunImportWriter` (REK-1) | ✅ |
| §4.4 krok 4 | walidacja reprezentowalności w charsecie **połączenia** z `EncoderExceptionFallback` (REK-2) | ✅ |
| §4.4 krok 5–6 | okno „indeks w paczce → `SourceRowNumber`"; `MultiError` ↔ `ImportErrorPolicy` 1:1 (REK-1) | ✅ |
| §4.5 | tabela zmierzonych wartości domyślnych: paczka 500, `Batched` co 10 000, próg 100 k, `CommandLock` per paczka (REK-5) | ✅ |
| §4.8.2 | `ImportBehaviorOptions.ExcelErrorCellsAsNull` (addytywne, REK-6) | ✅ |
| §6 I4 | `FbBatchCommand` + `MultiError` z polityki; mapowanie błędów na **parze kodów GDS**; PK/UNIQUE nierozróżnialne; DoD rozszerzone o wszystkie klasy błędów (REK-3) | ✅ |
| §6 I8 | domyślnie **pełny skan** źródła przy wnioskowaniu typów + widoczna liczba wierszy (REK-7) | ✅ |
| §6 I9 | **siedem wiążących wytycznych** providera XLSX (REK-6) + DoD „pierwszy realny plik z datami" | ✅ |
| §7 R1 | przeredagowane na **cichą korupcję** (prawdopodobieństwo „pewne", skutek „naruszenie reguły #1") | ✅ |
| §7 R3 | uzupełnione mechanizmem `numFmtId` + jawnie zapisana **luka pomiarowa** (brak dat w próbce) | ✅ |
| §7 R7 | **rozstrzygnięte**: paczka wygrywa, bo spełniła warunek atrybucji wiersza | ✅ |
| §7 R8 | liczby 3,9 MB vs 300,5 MB (77×) + nakaz SAX | ✅ |
| §7 R19, R20 | **nowe ryzyka z pomiarów**: kolumny mieszane w realnych danych; komórka błędu Excela | ✅ |
| §10 D9, D10 | ślad decyzyjny akceptacji REK-1 oraz REK-2…REK-8 | ✅ |
| §11 log pomiarów | liczby z §2 i §3 | ✅ |
| §11 checklista | wiersz „Weryfikuj Firebirda, nie wnioskuj" — co pomiar skorygował | ✅ |
| §12 / §9.5 (zakres MVP, punkty rozszerzeń) | **bez zmian** — I0 niczego nie przesunął w zakresie | ✅ |

---

## 7. Losy sond

Zgodnie z `tools/probes/README.md` („usuń albo zarchiwizuj, gdy pytanie zostało rozstrzygnięte —
wartością sondy jest wynik, a wynik należy do dokumentu projektowego"):

- **`DataImportWriteProbe`** — **zachować do etapu I4**. Jego fazy B/E/C są dokładnie tym, co
  `FirebirdImportWriter` musi odtworzyć; po I4 te przypadki żyją jako testy jednostkowe mapowania kodów +
  weryfikacja na bazie laboratoryjnej, i wtedy sonda idzie do usunięcia.
- **`DataImportXlsxProbe`** — **zachować do etapu I9**. Faza X jest gotową specyfikacją zachowania
  providera; faza R jest argumentem za SAX, który warto móc powtórzyć po zmianie wersji OpenXml.
- Oba wpisać do tabeli „Current probes" w `tools/probes/README.md` przy pierwszym commicie I1 (albo teraz,
  jeśli commitujemy I0 osobno).
