# Sprint spójności gridów danych — 2026-08-07

Mały sprint domykający obecny stan produktu, zamówiony przez użytkownika **przed M4 Product Polish** i
wyraźnie **nie** będący M4. Dwa zgłoszenia z codziennego używania aplikacji: wysokość edytora w komórce
Table Data i niekompletne menu kontekstowe gridów danych. Zamknięty jednym commitem na
`feat/product-polish`; build 0/0, suite **7378**, smoke czysty, QA użytkownika **odebrane**.

---

## 1. Wejście — pomiar zamiast wykonania zgłoszenia

Oba punkty dało się wykonać dosłownie w kilkanaście minut. Oba po zmierzeniu okazały się czymś innym niż
opisywało zgłoszenie, i **w obu przypadkach pomiar zmienił zakres**, a nie tylko diagnozę.

**Punkt 1** brzmiał: „edytor w komórce Table Data nadal jest zbyt niski, ma korzystać z tej samej roli
wysokości co pozostałe edytowalne siatki". Dosłowne wykonanie to jedna linia — nadać gridowi klasę
`field-grid`. Ale ta klasa była **celowo** wstrzymywana przez `EditableGridKind.Data`, z uzasadnieniem
zapisanym w trzech miejscach i brzmiącym jak wynik pomiaru:

> ⚠⚠ The height role is deliberately withheld, and that is MEASURED, not cautious: a 24 px minimum on the
> in-cell editor of a data grid grows every row the moment the user enters edit mode, because those grids
> have no ComboBox holding the row open (22–32 px rows). That is the layout shift M2b step 7 measured and
> §13.3 forbids.

**Punkt 2** brzmiał: „ujednolić menu kontekstowe Table Data z gridem wyników SQL". Zdanie zamykające
mówiło jednak coś szerszego — *„chcę, aby użytkownik miał spójne możliwości kopiowania we wszystkich
gridach danych aplikacji"*. Inwentaryzacja przed pisaniem kodu pokazała, że operacji kopiowania brakuje
w **czterech z pięciu** gridów, nie w jednym:

| Grid | Copy cell/row/+hdrs/all | Copy as INSERT/UPDATE | Filter/Exclude/Contains | Export |
|---|---|---|---|---|
| SQL Results | ✅ | ✅ | ✅ | ✅ |
| Table Data | ❌ | ✅ | ✅ | ✅ |
| Procedure Results | ❌ | — | ✅ | ✅ |
| Function Results | ❌ | — | ✅ | ✅ |
| View Data | ❌ | — | ✅ | ✅ |

⭐ Różnica między „jeden grid" a „cztery gridy" jest różnicą w produkcie, nie w implementacji, więc
została **zapytana użytkownikowi, a nie zgadnięta**. Użytkownik wybrał pełny zakres i, osobno, wyodrębnienie
jednego wspólnego formatera zamiast dokładania trzeciej kopii tej samej logiki.

---

## 2. Punkt 1 — reguła bezpieczeństwa, której przesłanki nikt nie sprawdził

**Cytowane uzasadnienie jest prawdziwe o siatce danych „w ogóle" i fałszywe o tej jednej, której
dotyczyło.** `TableDetailTabView.axaml` przypina wierszowi tego gridu **stałą** wysokość:

```xml
<Style Selector="DataGrid.data-edit DataGridRow">
  <Setter Property="Height" Value="32" />
</Style>
```

`Height`, nie `MinHeight` — więc wiersz **nie ma jak urosnąć od zawartości**. Po paddingu komórki `6 2`
(deklarowanym w tym samym widoku) zostaje 28 px na edytor o minimum 24. Reguła nie chroniła przed niczym,
a kosztowała dokładnie zgłoszony defekt.

⭐ **Mechanizm objawu był już zmierzony gdzie indziej i nikt go nie połączył z tym gridem.** Komentarz przy
`DataGridCell TextBox.field-editor` w `ControlStyles.axaml` mówi wprost, że `Stretch` na samym `TextBoxie`
nie wystarczy, bo `DataGridCell` ma `VerticalContentAlignment="Center"` i **centruje** dziecko zamiast je
rozciągać — zmierzone: 12 px w komórce 30 px. To samo działo się w Table Data: 32-pikselowy wiersz z
~12-pikselowym paskiem wejściowym w środku.

### Co zostało zrobione

`EditableGridKind` **usunięty w całości**. Po korekcie oba jego warianty zachowywałyby się identycznie, a
utrzymywanie rozróżnienia „na przyszłą siatkę danych, która mogłaby rosnąć" jest dokładnie tym, czego
zabrania stojąca dyrektywa *„nic nie powstaje, bo mogłoby się kiedyś przydać"*. Zostaje jedno
`Attach(grid)`, jedna reguła wysokości dla każdej edytowalnej siatki.

⚠ **Warunek bezpieczeństwa nie zniknął — zmienił nośnik.** Zamiast wariantu enuma, który *zakłada* że
siatka danych ma niski wiersz, jest teraz warunek zapisany wprost (*siatka przechodząca przez seam ma mieć
wiersz zdolny unieść edytor `Size.Control`*) **i pilnowany na znacznikach produktu**, a nie w prozie:

```
TheTableDataRow_DeclaresAHeightThatCanCarryTheCellEditor
```

— czyta wysokość wiersza z widoku, padding komórki z tego samego widoku i `Size.Control` z `Tokens.axaml`,
i wymaga, żeby po odjęciu paddingu zostało co najmniej tyle, ile prosi rola. Obniżenie wiersza do 26 albo
podniesienie roli do 30 przewraca ten test — czyli dokładnie wtedy, kiedy założenie seamu przestaje
obowiązywać.

⭐⭐ **To jest różnica między pilnowaniem POLITYKI a pilnowaniem PRZESŁANKI**, i jest to najtrwalszy wynik
tego punktu (gotcha **#322**). Poprzedni test — `TableData_IsAttachedAsAData_Grid_NotAsADefinitionOne` —
pilnował polityki, cytował pomiar z M2b kroku 7 i nazywał poprawkę *„the tempting simplification"*. Test,
który asertuje politykę, **dziedziczy każdą niesprawdzoną przesłankę tej polityki**: nie może wykryć, że
warunek wstępny nie zachodzi, bo nigdy na warunek wstępny nie patrzy.

### Pomiar zamiast klasy w liście klas

Nowy test behawioralny mierzy **element, który maluje**, nie listę klas gridu:

```
TheHeightRole_ReachesTheInCellEditor_OfADataShapedGrid
```

— buduje siatkę o wierszu przypiętym na 32 px, wchodzi w edycję Enterem, i sprawdza jednocześnie, że
edytor osiąga `Size.Control` **i że wysokość wiersza się nie zmieniła** (§13.3, Zero Layout Shift).
⚠ Asercja na `Classes.Contains("field-grid")` byłaby kształtem gotchy **#315**: zielona, kiedy produkt jest
zepsuty, bo klasa dowodzi, że znacznik dodano, a nie że styl się rozwiązał i dotarł do edytora, który
siatka tworzy dopiero w momencie wejścia w edycję.

**Zweryfikowane podsadzeniem naruszenia**: przy wstrzymanej roli test pada na `MinHeight` 0 vs 24.

---

## 3. Punkt 2 — jeden formater, cztery gridy

Operacje Copy cell / Copy row / Copy row with headers / Copy all with headers istniały wyłącznie jako
**prywatne składowe `MainWindowViewModel`** (`BuildCopyText` + `FormatRow`/`FormatCell`/`EscapeCell`).
Drugą, niezależną implementacją tych samych czterech operacji jest `TraceMonitorTabViewModel` (inny kształt
wiersza, zostawiona bez zmian). Dołożenie czterech kolejnych kopii dałoby sześć implementacji jednej
konwencji TSV — a rozjazd takiego formatu jest niewidoczny: każdy grid dalej działa, tylko inaczej.

**Wyodrębniony `App/ViewModels/GridCopyText.cs`** — czysty, statyczny, bez Avalonii:

```csharp
GridCopyText.Build(mode, columns, allRows, row, columnIndex)
```

⭐ **`MainWindowViewModel.BuildCopyText` zachował swoją indeksową sygnaturę i tylko deleguje** — dzięki temu
**wszystkie 12 istniejących `CopyGridTests` przeszło bez edycji ani jednego oczekiwanego ciągu.** To jest
dowód bajtowej niezmienności wyjścia gridu SQL, a nie deklaracja.

### Trzy decyzje projektowe, wszystkie ratyfikowane przez użytkownika przy odbiorze

1. **Każdy konsument podaje własny zbiór wierszy** — dlatego `Build` bierze `allRows`, a nie cały
   `QueryResult`. ⚠⚠ Table Data podaje **`EditableRows`**, nie `DataResult.Rows`: wiersz dodany albo
   usunięty w tej sesji istnieje wyłącznie w zapisywalnym mirrorze, więc skopiowanie wyniku wyemitowałoby
   wiersze, których użytkownik już nie widzi, i pominęło te, które dodał — cicho, bo tekst wyglądałby
   poprawnie. Pozostałe gridy podają `Rows` swojego wyniku (dla View Data to bieżąca strona — dane są
   stronicowane serwerowo i nic innego w pamięci nie istnieje; dla Procedure/Function cały zmaterializowany
   wynik, tak samo jak w gridzie SQL).
2. **Pojedyncza KOMÓRKA jest kopiowana dosłownie**, bez spłaszczania. Spłaszczanie tabulatorów i znaków
   nowej linii do spacji (konwencja `ClipboardTextExporter`, bo wklejanie do Excela nie honoruje cudzysłowów)
   obowiązuje przy kopiowaniu **wierszy**, gdzie służy utrzymaniu struktury TSV. W kopii jednej komórki nie
   ma sąsiednich kolumn do wyrównania, a spłaszczenie wielowierszowego `VARCHAR` byłoby cichym uszkodzeniem
   dokładnie tego, o co użytkownik poprosił.
3. **Brak danych do skopiowania ⇒ schowek nietknięty.** `GridCopyText.Build` zwraca `null` dla żądania,
   którego nie umie obsłużyć (brak wyniku, brak wiersza docelowego, nieaktualny indeks kolumny po
   re-fetchu), a `Views/GridClipboard.cs` jest jedynym miejscem, które ten zapis wykonuje — i odmawia.
   Przepuszczenie `null` jako pustego ciągu **zniszczyłoby to, co użytkownik już miał w schowku**, nie
   raportując niczego.

⚠ **Cel to prawoklikniętą KOMÓRKA, nigdy zaznaczenie gridu.** Menu kontekstowe może się otworzyć nad
wierszem, który nie jest zaznaczony — czytanie `SelectedItem` skopiowałoby coś innego niż to, na co
użytkownik wskazał (kształt gotchy #16/#99 o poziom wyżej). Wszystkie cztery widoki już miały przechwycenie
prawokliknięcia na potrzeby menu filtrów; trzy dostały dodatkowe pole `_copyRow`, Table Data korzysta z
istniejącego `_dataNullRow`.

### Kolejność pozycji

Menu wszystkich pięciu gridów są od pozycji kopiowania w dół **identyczne co do pozycji i kolejności**.
Table Data trzyma swoją grupę edycyjną (New row / Delete row / Set NULL) **na górze**, a nie wplecioną —
operacje specyficzne dla modułu są wtedy rozpoznawalne jako takie, a wspólny zestaw czyta się tak samo
wszędzie. Copy as INSERT / UPDATE zostaje tam, gdzie za wierszami stoi jedna tabela; dla widoku, procedury
i funkcji jest **świadomie nieobecne** (współdzielona ścieżka `SqlCopy` raportuje dla nich `NotATable`) —
i to jest zapisane pozytywnie, jako reguła, a nie jako brak.

### Strażnik kryterium odbioru

`DataGridCopyMenuTests` pilnuje tego, o co użytkownik faktycznie poprosił: **każdy grid danych oferuje ten
sam komplet operacji, a różnią się tylko operacje specyficzne dla modułu.** Trzy asercje — komplet
kopiowania, komplet filtrów + eksportu, oraz pozytywna reguła o Copy as SQL. **Zweryfikowany podsadzeniem
naruszenia**: usunięcie jednej pozycji z jednego widoku pada z nazwą tego widoku i tej pozycji.

⚠ Nic nie trzeba było dopisywać do `UiStrings` — wszystkie cztery ciągi (`GridCopyCell`, `GridCopyRow`,
`GridCopyRowWithHeaders`, `GridCopyAllWithHeaders`) istniały od czasu, kiedy powstał grid wyników SQL.

---

## 4. Znalezisko poboczne — strażnik, którego zieleń znaczyła mniej, niż wyglądała

Zmiana sygnatury `Attach` przewróciła `EveryEditableGrid_InAMetadataEditor_GoesThroughTheSeam` na dwóch
widokach, które są **poprawnie podpięte**. Przyczyną było ostatnie ogniwo pomocnika `MentionsGrid`:

```csharp
|| Regex.IsMatch(codeBehind, @"EditableGridBehavior\.Attach\(\s*[A-Za-z0-9_]+\s*,")
   && Regex.IsMatch(codeBehind, @"FindControl<DataGrid>\(""Name""\)\s*is\s*\{")
```

⭐ To nie jest sprawdzenie „czy TA siatka jest podpięta", tylko „czy w tym pliku ktokolwiek wywołuje
`Attach`". Widok z czterema siatkami, z których podpięte są trzy, raportowałby cztery podpięte —
czyli strażnik odpowiadałby „tak" dokładnie w przypadku, dla którego został napisany. ⚠ Nie wyszło to
wcześniej, bo klauzula była **kalibrowana na przypadku przechodzącym, nigdy na padającym**.

Poprawione: pomocnik wylicza wszystkie trzy formy wiązania (pole, zmienna wzorca `is { } x`, gołe
`x:Name`) i wymaga wywołania **na tym identyfikatorze**. Gotcha **#323**.

---

## 5. Weryfikacja

- Build **0/0**.
- Suite **7378** = **7250** (partycja główna) + **73** (headless zbiorcza) + **55** (headless izolowana),
  każda z `--blame-hang`. Przyrost **+18**: 14 `GridCopyTextTests`, 3 `DataGridCopyMenuTests`, 1 nowy
  strażnik przesłanki w `EditableGridSeamTests`. ⚠ Kryterium odbioru jest **łączna liczba**, nie „0
  niepowodzeń" — patrz ostrzeżenie w CLAUDE.md przy sekcji „Tests".
- Smoke: aplikacja startuje, **0 wpisów `FATAL`**.
- **Oba nowe strażniki zweryfikowane podsadzeniem naruszenia**, każdy pada z własną nazwą.
- QA użytkownika: **odebrane** — wysokość edytora w Table Data spójna z pozostałymi edytowalnymi gridami,
  menu wszystkich czterech gridów danych spójne i z kompletem operacji kopiowania.

## 6. Czego ten sprint nie zrobił

- ⛔ **Nie ruszył M4** ani niczego z Product Polish. M4 nadal wymaga własnego, osobnego pozwolenia i
  startuje z `product-polish-m4-next-session.md`.
- ⛔ **Nie ruszył wysokości WIERSZY** żadnej siatki. Rozjazd 34/32/30/22 w siatkach definicji i pozycja
  **Z‑3** (wiersz Table Data vs katalogowe 22) to pytania o **gęstość**, przypisane do M4/§13.3 decyzją
  użytkownika. Naprawiona została wyłącznie wysokość EDYTORA w komórce.
- ⛔ **Nie ujednolicił `TraceMonitorTabViewModel`** z nowym formaterem — jego wiersz ma inny kształt
  (`TraceEventRowViewModel`, nie `object?[]`), więc wspólny formater nie jest tam po prostu podmianą.
  Zapisane jako obserwacja, nie jako dług do spłacenia „przy okazji".
