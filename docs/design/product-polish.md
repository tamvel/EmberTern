# Product Polish — audyt, ratyfikowane decyzje i katalog Design Tokens

> **STATUS: M0 (audyt) ✅ · M1 (ten katalog) ✅ zaakceptowany 2026-08-01 ·
> M2a (infrastruktura tokenów) ⏳ ZAIMPLEMENTOWANY 2026-08-01, oczekuje na QA wizualne.**
> **Następny krok: M2b — Compact Controls, własny `CheckBox`, powierzchnie i kolory.**
>
> **As-built M2a: §14** (co powstało, decyzje, pomiary dla M2b). Uzupełnienia katalogu wykonane
> w M2a: **§4.2** (ikony, promienie, krawędzie), **§6.4** (jak konsumuje się rolę typograficzną),
> **§11.1** (zapadka licznikowa).
>
> ⚠ `product-polish-m2a-handover.md` jest **zamknięty** — opisuje etap już wykonany.
>
> Specyfikacja etapu: `C:\Users\grzegorz.gronski\Desktop\Product Polish.mdown` — **jest źródłem prawdy**.
> Ten dokument jej nie zastępuje; dopowiada to, czego w niej nie ma, i zapisuje decyzje użytkownika.
> Wszystkie kryteria z §10 i §12 specyfikacji pozostają obowiązujące w całości — żadna propozycja
> z tego dokumentu nie zwalnia z pierwotnego wymagania.

---

## ⭐⭐ REGUŁA PROWADZĄCA CAŁY ETAP

> **Użytkownik, 2026-08-01:**
>
> ## *„Dokument ma prowadzić produkt. Nie produkt dokument."*

Dokumentacja jest **źródłem prawdy dla zakresu i założeń** — nie jest celem samym w sobie.

**Jeżeli podczas implementacji znajdziesz rozwiązanie wyraźnie lepsze od zapisanego:**

```
1. NIE implementuj gorszego tylko dlatego, że zostało opisane wcześniej.
2. NIE implementuj lepszego po cichu.
3. Przedstaw propozycję WRAZ Z UZASADNIENIEM.
4. Po akceptacji — zaktualizuj dokumentację.
5. Dopiero wtedy implementuj.
```

**⚠ Dwa błędy są symetryczne i oba są błędami:**

| Błąd | Skutek |
|---|---|
| **Ciche odstępstwo** | dokument traci znaczenie — za pół roku nikt nie odróżni świadomej decyzji od tego, że ktoś nie doczytał |
| **Ślepa zgodność** | produkt traci jakość — powstaje gorsze rozwiązanie, o którym wiadomo było, że jest gorsze |

⭐ **Ten dokument jest zapisem decyzji, a nie kontraktem do wykonania literalnie.** Każda sekcja
oznaczona ⭐ SWOBODA PROJEKTOWA (np. §8.4.1 — rail) mówi to wprost, ale reguła obowiązuje wszędzie,
także tam, gdzie nie ma takiej adnotacji.

⛔ **Jeden wyjątek: decyzje ratyfikowane D1–D12 (§2) oraz wymagania specyfikacji.** Te podlegają
zmianie wyłącznie przez wyraźną decyzję użytkownika — propozycja jest dopuszczalna, samodzielna
zmiana nie.

---

## §0 Jedno zdanie, które tłumaczy cały etap

**EmberTern nie ma Design Systemu — ma system kolorów i zbiór stylów wariantowych.**

Warstwa kolorów (`Colors.axaml`, podwójny motyw, tokeny semantyczne) jest dojrzała i konsekwentna.
Warstwa komponentów (`Button.icon/.primary/.flat`, `TabItem.bottom-tab/.sub-tab`, `MessageBanner`,
`ContextMenu`, `SvgIcon`) powstała tam, gdzie ktoś świadomie projektował komponent — i widać to.
**Wszystko pomiędzy tymi komponentami stoi na wartościach lokalnych.** Stąd odbiór opisany
w specyfikacji: pojedyncze elementy wyglądają na przemyślane, całość nie.

Pomiar, który to potwierdza: **zero implicit style dla kontrolek bazowych, zero tokenów
niekolorowych, 589 lokalnych `FontSize`, 114 unikalnych `Margin`, 40 `Padding`, 7 rozmiarów ikon,
7 ciągów czcionki monospace.**

---

## §0.1 Zasada nadrzędna etapu — Persistent UI

> **Decyzja użytkownika, 2026-08-01. Ma pierwszeństwo przy każdym konflikcie priorytetów
> wewnątrz etapu.**

Nie wszystkie powierzchnie są warte tyle samo. **Okno dialogowe użytkownik otwiera raz dziennie.
Toolbar, Status Bar i Metadata Explorer ogląda przez wiele godzin.** To one budują pierwsze
wrażenie i tożsamość produktu — i to na nich rozstrzyga się test z §3.5 specyfikacji
(*„po usunięciu logo — komercyjne IDE?"*).

**Siedem powierzchni trwałych, w kolejności wagi:**

1. **Status Bar** — widoczny zawsze, dziś wykorzystany w ~30%
2. **Toolbar** — widoczny zawsze
3. **Pasek zakładek** — widoczny zawsze
4. **DataGrid** — podstawowe narzędzie pracy
5. **Metadata Explorer** — otwarty przez cały czas
6. **Kontrolki bazowe** — obecne na każdym ekranie
7. **Menu kontekstowe** — najczęstsza droga do akcji

**Konsekwencje dla planu (§13):**
- te siedem powierzchni jest obsłużone w **M2b i M3**, czyli w pierwszej połowie etapu,
- **Metadata Explorer zostaje wyciągnięty z M4.2 do M3** — jest powierzchnią trwałą, nie ekranem,
- **dialogi zostają na końcu (M4.4)** i to jest właściwa kolejność, nie zaniedbanie,
- przy sporze o czas: **powierzchnia trwała wygrywa z ekranem otwieranym sporadycznie.**

⚠ To **nie zwalnia** z pokrycia pozostałych ekranów — §10 i §12 specyfikacji obowiązują w całości.
Zasada rozstrzyga **kolejność i nakład**, nie zakres.

### §0.1.1 ⭐ Tokeny są środkiem, nie celem

> **Użytkownik, 2026-08-01:** *„Sukces tego etapu nie będzie polegał na tym, że wszystkie
> kontrolki będą korzystały z poprawnych tokenów. Tokeny są środkiem do osiągnięcia celu.
> Sukces będzie wtedy, gdy po uruchomieniu EmberTerna pierwsze wrażenie będzie takie,
> że użytkownik korzysta z dopracowanego, komercyjnego narzędzia."*

To jest **kryterium nadrzędne wobec całego katalogu z §4–§10**. Zgodność z tokenami jest
warunkiem **koniecznym, nie wystarczającym**. Ekran może mieć każdy token na miejscu, przejść
`DesignTokenComplianceTests` i nadal wyglądać przeciętnie — **i wtedy nie jest gotowy.**

⛔ **Nigdy nie raportować etapu jako zakończonego na podstawie samej zgodności technicznej.**
Zielony build, zielone testy i zerowa lista wyjątków to dowód, że *nie ma dryfu* — nie że
*jest dobrze*. Drugie pytanie zadaje się osobno i odpowiada się na nie patrząc na ekran.

### §0.1.2 ⭐ Application Chrome to JEDNA powierzchnia, nie cztery komponenty

> **Użytkownik, 2026-08-01:** *„Toolbar, Status Bar, pasek zakładek oraz Metadata Explorer
> tworzą razem jedną całość. Chciałbym, aby były projektowane jako jeden spójny Application
> Chrome, a nie jako cztery niezależne komponenty."*

Te cztery elementy otaczają obszar pracy z czterech stron i użytkownik widzi je **jednocześnie,
przez cały czas**. Zaprojektowane osobno — każdy poprawnie — złożą się w cztery poprawne
komponenty i jedną niespójną ramę.

**Konsekwencje wiążące dla M3:**

| Wymóg | Znaczenie |
|---|---|
| **Wspólny rytm pionowy** | wysokości pasków (`Size.TitleBar` 36 · `Size.Row.Tab` 26 · `Size.StatusBar` 24) muszą tworzyć czytelną hierarchię, a nie trzy przypadkowe liczby |
| **Wspólne traktowanie krawędzi** | ta sama grubość, ten sam token, ta sama reguła „gdzie linia jest, a gdzie jej nie ma" na wszystkich czterech granicach |
| **Wspólny język stanu** | kropka połączenia, chip DEV MODE, rail stanu i akcent aktywnej zakładki to **jeden zestaw sygnałów**, nie cztery niezależne pomysły |
| **Wspólna gęstość** | pasek narzędzi i pasek statusu nie mogą mieć różnego odczucia ciasnoty |
| **Jeden przegląd na końcu** | §13.3 — brama jakości po M3, przed M4 |

⚠ **Praktyczna reguła pracy w M3.1–M3.4:** kończąc etap dotyczący jednego z tych czterech
elementów, patrzymy na **wszystkie cztery naraz** — nie na ten, który właśnie zmieniliśmy.
Detal poprawny w izolacji, a niepasujący do pozostałych trzech, jest defektem.

⭐ **To jest miejsce, w którym warto poświęcić dodatkowy czas.** Jeżeli w całym etapie jest
jeden obszar, gdzie nadmiar staranności się zwróci, to właśnie te cztery powierzchnie —
bo one budują pierwsze wrażenie i decydują o teście z §3.5 specyfikacji.

---

## §1 Wyniki audytu (M0)

Wszystkie liczby zmierzone na kodzie 2026-08-01, nie oszacowane.

### §1.1 Stan wyjściowy

| Fakt | Wartość | Źródło |
|---|---|---|
| Implicit style dla `TextBox`/`ComboBox`/`CheckBox`/`Button`/`RadioButton`/`NumericUpDown`/`ToggleButton`/`ScrollBar`/`ToolTip`/`Expander`/`ProgressBar` | **0** — wszystkie na Fluent, `MinHeight=32` | `ControlStyles.axaml` |
| Design tokens niekolorowe | **0** — `Colors.axaml` to wyłącznie kolory | `Colors.axaml` |
| Lokalne `FontSize=` w widokach | **589**, 7 wartości (11×333, 12×153, 10×52, 13×40, 9×7, 14×3, 23×1) | `Views/*.axaml` |
| `FontFamily` monospace | **7 ciągów, ~95 wystąpień, 33 pliki** | axaml + cs |
| Unikalne `Margin` / `Padding` | **114 / 40** | `Views/*.axaml` |
| Rozmiary `SvgIcon` | **7** (14×64, 16×15, 13×4, 11×4, 15×2, 12×2, 10×1) | `Views/*.axaml` |
| `CornerRadius` | **5 wartości** (3×18, 4×11, 5×5, 6×1, 4.5×1) | wszystkie axaml |
| `<Button>` bez `Classes=` | **25 z 329** | `Views/*.axaml` |
| `FontSize` edytorów kodu | **13 (×25) i 12 (×6)** — rozjazd | `Views/*.axaml` |
| `ToolTip.Tip` z literałem zamiast `UiStrings` | **13** | `Views/*.axaml` |
| Widoki z jawnym empty state | **3 z 48** | `Views/*.axaml` |
| `ProgressBar` w aplikacji | **3** | `Views/*.axaml` |
| Pasek zakładek | `WrapPanel`, **bez menu kontekstowego** | `MainWindow.axaml:783` |
| `TreeView` w zakładkach „Zależności" | **18 instancji w 9 edytorach** | `*DetailTabView.axaml` |

### §1.2 Release Blocker

**RB‑1 — Brak warstwy tokenów niekolorowych.** Każdy ekran definiuje własne rozmiary; żadne
kryterium z §10 specyfikacji nie jest osiągalne bez tej warstwy. Rozwiązanie centralne: §2 tego
dokumentu.

**RB‑2 — CheckBox w DataGrid wymusza podwójną wysokość wiersza.** Fluent `CheckBox.MinHeight=32`
przy `DataGridCell.Padding="8,3"`/`FontSize=11` → wiersz tekstowy ~20 px, wiersz z checkboxem ~43 px.
Dotyczy Table Editor → Fields, Data Import → Table types, Security Manager. Specyfikacja §6.4
nazywa to „niedopuszczalne".

**RB‑3 — Kontrolki bazowe na domyślnych Fluent.** Źródłowa przyczyna wrażenia „aplikacja Avalonia"
z §3.7 specyfikacji. Nienaprawialna lokalnie.

**RB‑4 — `ElevatedPanelBrush` pełni dwie sprzeczne role; aktywna zakładka czyta się jako wciśnięta
w Light Theme.** Pełna analiza w §7.1 — to najbardziej subtelny defekt w całym audycie i wymaga
osobnego tokenu, nie zmiany wartości.

### §1.3 High

| # | Problem | Dowód |
|---|---|---|
| H‑1 | Wartości lokalne unieważnią nową warstwę tokenów | 589 `FontSize` + 114 `Margin`; wartość lokalna outranks setter stylu |
| H‑2 | 7 ciągów czcionki monospace | rozstrzygnięte — Cascadia Mono (§3.2) |
| H‑3 | Górny toolbar przesuwa się przy zmianie stanu | `IsVisible` na bloku połączenia, badge DEV MODE, `CanExportDdl`; narusza §13.3 |
| H‑4 | Status Bar wykorzystany w ~30% | 4 kolumny, brak postępu, brak stanów semantycznych, stan połączenia i komunikaty w identycznej wadze wizualnej |
| H‑5 | Commit/Rollback różne w dwóch modułach | titlebar `Button.icon`+`SvgIcon`; Script Executor `Button.flat`+tekst |
| H‑6 | Pasek zakładek bez limitu wierszy | rozstrzygnięte — §3.1 |
| H‑7 | Kontrast nieaktywnych zakładek na granicy w OBU motywach | Light 4,60:1 · Dark 4,52:1 przy `FontSize=11` |
| H‑8 | 25 surowych przycisków Fluent | bez `Classes` → 32 px |
| H‑9 | 7 rozmiarów ikon bez reguły | 10–16 px |
| H‑10 | Paski przewijania bez stylu ⚠ *poza specyfikacją* | 0 implicit style dla `ScrollBar` |

### §1.4 Medium

| # | Problem |
|---|---|
| M‑1 | 13 literałów angielskich w `ToolTip.Tip` zamiast `UiStrings` (narusza regułę arch. #6, blokuje lokalizację) |
| M‑2 | `ToolTip` bez stylu i bez standardu opóźnienia ⚠ *poza specyfikacją* |
| M‑3 | Empty states w 3 z 48 widoków |
| M‑4 | Terminologia `Execute`/`Run` bez słownika i bez strażnika |
| M‑5 | 16 dialogów na `SizeToContent`, tylko 2 z `GrowingDialogBehavior` (gotcha #295) |
| M‑6 | 5 wartości `CornerRadius` bez reguły |
| M‑7 | Brak informacji zwrotnej dla operacji długotrwałych poza 3 miejscami |

### §1.5 Low

**L‑1** Focus ring niespójny — `Button.icon`/`.flat` mają `:focus`, `.primary`/`.caption` nie.
**L‑2** Brak polityki animacji (§9 tego dokumentu).
**L‑3** `Separator` bez implicit style; w toolbarach inline `Border Width=1`.

### §1.6 UX Debt — świadomie odłożone

| # | Pozycja | Uzasadnienie |
|---|---|---|
| D‑1 | Ustawienie czcionki/rozmiaru przez użytkownika | §8.2 specyfikacji wprost wyłącza z etapu; architektura ma być gotowa (§3 tego dokumentu) |
| D‑2 | Tryb gęstości (Comfortable/Compact/Dense) | Etap ustala **jeden** standard; tryb to funkcja |
| D‑3 | Przypinanie zakładek | Decyzja użytkownika: nowa funkcjonalność, nie Product Polish |
| D‑4 | `GridProfiles` — szerokości kolumn zapisywane dopiero przy zamknięciu | Znany dług (settings-center §16.6) |
| D‑5 | Przemianowanie `BackgroundBrush`/`PanelBrush` na nazwy regionowe | Czysta churn bez korzyści dla użytkownika; §7.1 rozwiązuje defekt bez tego |
| D‑6 | Metadata Explorer Layer 2/3 + koszt startu | Ma własny etap |
| D‑7 | Szybki przełącznik dokumentów (quick switcher) | Nawigacja = funkcja, nie polish |

> ⛔ **Migracja Metadata Explorer na inny komponent NIE JEST długiem — jest decyzją.**
> Obecny płaski kontroler jest rozwiązaniem docelowym. Powrót do eksperymentów z drzewem
> jest wykluczony (gotcha #154/#157).

---

## §2 Decyzje ratyfikowane przez użytkownika

Poniższe rozstrzygnięcia są wiążące i **nie podlegają ponownej dyskusji** w trakcie etapu.

| # | Decyzja |
|---|---|
| **D1** | **Wysokości kontrolek — wariant B**: standard **24 px**, w siatkach **22 px**, akcja główna **28 px** |
| **D2** | **Czcionka monospace — Cascadia Mono** (bez ligatur), jeden token |
| **D3** | **Status Bar** — nazwa aplikacji i numer wersji **usunięte**; pozostają w oknie About |
| **D4** | **Pasek postępu** — infrastruktura (M3) rozdzielona od podłączania operacji (M3b) |
| **D5** | **Pasek zakładek — dwa tryby**: wielowierszowy (domyślny) i pojedynczy wiersz z przepełnieniem |
| **D6** | **Bez `MaxWidth`/wielokropka na zakładkach** — patrz uzasadnienie w §8.1 |
| **D7** | **Limit wierszy paska zakładek** konfigurowalny **1–10, domyślnie 3**; po przekroczeniu przewija się **wyłącznie pasek**, nie interfejs |
| **D8** | **Przełącznik trybu i limit wierszy w Settings Center**; menu kontekstowe zakładki niesie skrót do tej konfiguracji |
| **D9** | **Menu kontekstowe zakładek** — 8 pozycji (§8.3); **przypinanie odłożone** jako nowa funkcjonalność |
| **D10** | **Metadata Explorer bez zmian** — obecny komponent jest docelowy |
| **D11** | **Zakładki „Zależności" migrują** na wspólny komponent prezentacyjny — cel to spójność, nie przebudowa architektury |
| **D12** | Dodatkowe usprawnienia z audytu są mile widziane, ale **nie mogą powodować pominięcia ani ograniczenia** żadnego pierwotnego założenia specyfikacji |

---

## §3 Zasady katalogu tokenów

### §3.1 Token nazywa ROLĘ, nigdy wartość

`Pad.Control` — nie `Pad.8x3`. `Space.SectionGap` — nie `Space.12`.

**Powód:** token nazwany wartością to zwykła zmienna globalna na liczbę. Kiedy za pół roku
standardowy padding zmieni się z `8,3` na `8,4`, token `Pad.8x3` albo skłamie, albo trzeba go
przemianować w 300 miejscach — czyli odtworzymy dokładnie ten sam problem, tylko z ładniejszymi
nazwami. Rola przeżywa zmianę wartości.

### §3.2 Avalonia nie liczy w XAML — katalog musi mieć dwie warstwy

`Margin`/`Padding` przyjmują `Thickness`, a XAML nie potrafi złożyć `Thickness` z `x:Double`.
Katalog ma więc:

- **warstwę skalarną** (`x:Double`) — dla `Spacing=`, `Width=`, `Height=`, `FontSize=`,
- **warstwę złożoną** (`Thickness`, `CornerRadius`) — pre-komponowane wartości **nazwane rolą**.

⚠ To jest miejsce, w którym dług odrasta najłatwiej. Dlatego warstwa złożona jest **zamknięta**:
nowy `Thickness` wymaga nowej roli, a nowa rola wymaga uzasadnienia. Egzekwuje to test z §11.

### §3.3 Rola może dzielić wartość z inną rolą i to jest poprawne

`Text.Compact`, `Text.Grid` i `Text.Status` mają dziś **wszystkie 11 px**. To nie jest duplikacja —
to trzy niezależne role, które chwilowo się pokrywają i mogą się rozejść niezależnie.

⛔ **Nie wolno ich „upraszczać" do jednej roli.** Zwinięcie ich odbiera możliwość zmiany rozmiaru
tekstu w siatkach bez ruszania status bara — czyli dokładnie tę elastyczność, dla której
system typografii powstaje.

### §3.4 Gotowość na przyszłe ustawienie czcionki (§8.2 specyfikacji)

Etap **nie dostarcza** opcji zmiany czcionki. Ma jednak zostawić architekturę, która ją umożliwi.
Realizacja: każda rola typograficzna czyta **rodzinę** z jednego z dwóch tokenów bazowych
(`Font.Ui`, `Font.Code`) i **rozmiar** z własnego tokenu skalarnego. Przyszła preferencja podmienia
te dwa tokeny bazowe i skalę — nic więcej.

⛔ Nie budujemy w tym etapie żadnego mechanizmu, który by z tego korzystał (reguła #233 —
komponent bez konsumenta wygląda identycznie jak regresja).

---

## §4 Katalog — skala odstępów

Baza **4 px**, z półstopniami 2 i 6 dla ciasnej chromy (paski narzędzi, wiersze siatek).

| Token | Wartość | Zastosowanie |
|---|---|---|
| `Space.Hair` | 2 | odstęp ikona↔ikona w toolbarze, wewnątrz chipa |
| `Space.Xs` | 4 | ikona↔tekst, wewnątrz wiersza |
| `Space.Sm` | 6 | kontrolka↔kontrolka w poziomie |
| `Space.Md` | 8 | standardowy odstęp w formularzu |
| `Space.Lg` | 12 | między grupami pól |
| `Space.Xl` | 16 | między sekcjami |
| `Space.Xxl` | 20 | wewnętrzny margines okna dialogowego |

**Dlaczego 7 stopni, nie 5 lub 10:** zmierzone 114 unikalnych marginesów redukuje się do tych
siedmiu bez zauważalnej zmiany układu w żadnym z 48 widoków (weryfikacja w M2c). Pięć stopni
wymuszałoby widoczne przeskoki w ciasnej chromie; dziesięć nie zmniejsza swobody na tyle, by
zapobiec ponownemu dryfowi.

### §4.1 Warstwa złożona — role `Thickness`

| Token | Wartość | Rola |
|---|---|---|
| `Pad.Control` | `8,0` | wnętrze `TextBox`/`ComboBox` (wysokość daje `Height`, nie padding) |
| `Pad.Button` | `12,0` | wnętrze przycisku tekstowego |
| `Pad.ButtonIcon` | `6,0` | wnętrze przycisku ikonowego |
| `Pad.Cell` | `8,3` | komórka siatki |
| `Pad.CellCompact` | `6,2` | komórka siatki gęstej (Trace, Sessions) |
| `Pad.MenuItem` | `10,3` | wiersz menu |
| `Pad.Tab` | `10,4` | zakładka |
| `Pad.Panel` | `8` | wnętrze panelu |
| `Pad.Dialog` | `20,16` | wnętrze okna dialogowego |
| `Pad.Group` | `10,8` | wnętrze `Border.settings-group` |
| `Margin.FieldGap` | `0,0,0,8` | pod polem formularza |
| `Margin.SectionGap` | `0,0,0,16` | pod sekcją |
| `Margin.InlineGap` | `0,0,8,0` | między kontrolkami w poziomie |

⚠ Lista jest **zamknięta na starcie M2a**. Każda kolejna pozycja wymaga uzasadnienia rolą,
której nie da się złożyć z istniejących.

⭐ **Uzupełnienie M2a (2026-08-01, zaakceptowane przez użytkownika).** Lista powyżej opisuje
wnętrza i odstępy, ale **nie opisuje krawędzi** — a `BorderThickness` też jest `Thickness`
i też stoi dziś w całości na wartościach lokalnych (133 wystąpienia). Trzy role krawędzi
dołączają do warstwy złożonej w §4.2. Reguła zamknięcia listy **nie zmienia się**: rozszerza
ją wyłącznie zmierzona potrzeba, nigdy przewidywana.

---

## §4.2 Katalog — ikony, promienie, krawędzie

> ⭐ **Sekcja dopisana w M2a.** Audyt notował te trzy obszary jako defekty (H‑9 „7 rozmiarów ikon
> bez reguły", M‑6 „5 wartości `CornerRadius` bez reguły"), a §8.4 użyła raz nazwy `Radius.Sm`,
> ale **żadna tabela ról nie istniała**. M2a musiał je nazwać, żeby `Tokens.axaml` był kompletny.
> Wartości wyprowadzone z pomiaru kodu 2026-08-01, nie z uśrednienia.

### §4.2.1 Rozmiary ikon

| Token | Wartość | Rola |
|---|---|---|
| `Size.Icon` | **14** | ikona chromy — toolbar, zakładka, drzewo, wiersz menu |
| `Size.Icon.Lg` | **16** | ikona o własnej wadze wizualnej — nagłówek, pusty stan, akcja główna |
| `Size.Icon.Sm` | **12** | ikona inline w tekście 11 px — chip, wiersz siatki |

**Dlaczego trzy, nie siedem.** Zmierzone: 14×64, 16×15, 11×5, 13×4, 12×3, 15×2, 10×1.
Rozkład ma **dwa realne skupienia** (14 i 16) i długi ogon czterech wartości użytych łącznie
15 razy — ogon jest dryfem, nie decyzją. Trzecia rola istnieje, bo ikona stojąca obok tekstu
11 px przy 14 px optycznie go przerasta; to jedyny przypadek z ogona, który ma uzasadnienie
własne, a nie „ktoś wpisał inną liczbę".

### §4.2.2 Promienie

| Token | Wartość | Rola |
|---|---|---|
| `Radius.Surface` | **3** | karta, grupa, panel, lista rozwijana, `Border.settings-group` |
| `Radius.Chip` | **4** | odznaka i chip — element o kształcie pigułki |

⭐ **To nie jest pięć wartości do uśrednienia — to dwie role.** Pomiar rozkładu wskazał
jednoznacznie: wszystkie wystąpienia `4`, `4.5`, `5` i `6` siedzą w Trace Monitor, Session
Managerze i pasku agregacji i **wszystkie są chipami**; `3` to wyłącznie powierzchnie. Różnica
nie była przypadkowa — brakowało jej tylko nazwy.

⚠ **Poprawka do §8.4.6:** pasek postępu Status Bara czyta `Radius.Surface`. Nazwa `Radius.Sm`
z pierwszej redakcji tej sekcji nie została nigdy zdefiniowana i **nie wchodzi do katalogu** —
byłaby nazwą wartości, nie roli (§3.1).

### §4.2.3 Krawędzie

| Token | Wartość | Warstwa | Rola |
|---|---|---|---|
| `Stroke.Hairline` | **1** | skalarna | grubość każdej krawędzi i separatora |
| `Border.All` | `1` | złożona | pełna ramka — karta, pole, przycisk konturowy |
| `Border.Top` | `0,1,0,0` | złożona | linia oddzielająca od treści **powyżej** |
| `Border.Bottom` | `0,0,0,1` | złożona | linia oddzielająca od treści **poniżej** |

Trzy role złożone pokrywają **96 ze 133** zmierzonych wystąpień (`0,0,0,1`×44, `0,1,0,0`×30,
`1`×22). Reszta to `0` (×31 — brak krawędzi nie potrzebuje tokenu) i cztery wystąpienia
jednostronnych krawędzi bocznych, które zostają lokalne do czasu, aż M2b lub M3 pokaże dla nich
rolę.

⛔ **`Stroke.Rail` = 2 **świadomie NIE wchodzi do M2a**, mimo że §8.4.2 ratyfikuje rail o stałej
grubości 2 px. Rail jeszcze nie istnieje — token bez konsumenta wygląda identycznie jak regresja
(reguła #233) i nikt nie potrafi zweryfikować, czy jego wartość jest dobra. Wchodzi w **M3.1**,
razem z railem, który go użyje.

### §4.2.4 ⭐ Rola przed wartością — reguła operacyjna

> **Użytkownik, 2026-08-01, przy akceptacji tej sekcji:** *„Traktujmy przede wszystkim role,
> a nie konkretne wartości. Jeżeli podczas M2b okaże się, że np. `Radius.Chip` lub któryś
> z rozmiarów ikon powinien mieć inną wartość — najpierw aktualizujemy dokumentację, a dopiero
> potem implementację."*

Wartości w §4.2 są **zmierzone, nie ratyfikowane**. Zmiana wartości przy zachowanej roli jest
zwykłą decyzją projektową kolejnego etapu i przechodzi normalną ścieżką: propozycja →
dokument → kod. Zmiana **zestawu ról** to co innego — wymaga uzasadnienia rolą, której nie da
się złożyć z istniejących (§4.1).

---

## §5 Katalog — wysokości kontrolek (decyzja D1)

| Token | Wartość | Zastosowanie |
|---|---|---|
| `Size.Control` | **24** | `TextBox`, `ComboBox`, `Button.flat`, `NumericUpDown`, `SearchableComboBox` |
| `Size.ControlPrimary` | **28** | `Button.primary` — Execute, Start debugging, Import |
| `Size.Row.Grid` | **22** | `DataGridRow`, edytory w komórce |
| `Size.Row.Header` | **24** | `DataGridColumnHeader` |
| `Size.Row.Tree` | **20** | wiersz Metadata Explorer, wiersz drzewa zależności |
| `Size.Row.Menu` | **22** | `MenuItem` *(już taki jest — token to formalizuje)* |
| `Size.Row.Tab` | **26** | zakładka dokumentu i zakładka pomocnicza |
| `Size.Checkbox` | **14** | box `CheckBox`/`RadioButton` |
| `Size.TitleBar` | **36** | wysokość paska tytułu *(bez zmian)* |
| `Size.StatusBar` | **24** | wysokość status bara *(§8.5 specyfikacji: nie zwiększać)* |

### §5.1 Weryfikacja arytmetyczna — czy 22 px mieści zawartość

To jest liczba, na której stoi RB‑2, więc sprawdzam ją wprost:

```
wiersz siatki 22 px
  − Pad.Cell góra/dół (3 + 3)          = 16 px na zawartość
  tekst Text.Grid 11 px, line-height 15 → mieści się (16 ≥ 15) ✓
  Size.Checkbox 14 px                   → mieści się (16 ≥ 14) ✓
```

**Wniosek: przy 14 px checkbox mieści się w 22 px wierszu z 1 px zapasu.** RB‑2 znika przez
konstrukcję, nie przez obejście. Dziś ten sam wiersz ma 43 px, bo Fluent wymusza box 20 px
w kontrolce o `MinHeight=32`.

⚠ **Konsekwencja architektoniczna:** `Size.Checkbox=14` wymaga **własnego `ControlTemplate`
dla `CheckBox`**, bo Fluent koduje rozmiar boxa w szablonie, nie w property. Specyfikacja §6.4
wprost to przewiduje: *„Jeżeli jest to ograniczenie Avalonia, należy przygotować własne
rozwiązanie."*

---

## §6 Katalog — typografia

Dziewięć ról z §8.2 specyfikacji, plus trzy, których tam nie ma, a bez których nie da się pokryć
zmierzonego użycia. Każda rola = rodzina + rozmiar + waga + interlinia.

| Rola | Rozmiar | Waga | Interlinia | Rodzina | Zastosowanie | Dziś |
|---|---|---|---|---|---|---|
| `Text.Code` | **13** | Regular | — | `Font.Code` | AvaloniaEdit, 11 podglądów DDL | 13 (×25) **i 12 (×6)** ✗ |
| `Text.Application` | **12** | Regular | 17 | `Font.Ui` | treść, dialogi, etykiety | 12 (×153) |
| `Text.Compact` | **11** | Regular | 15 | `Font.Ui` | chroma, panele, zakładki | 11 (×333) |
| `Text.Grid` | **11** | Regular | 15 | `Font.Ui` | komórki siatek | 11 |
| `Text.GridHeader` | **11** | SemiBold | 15 | `Font.Ui` | nagłówki kolumn | 11 SemiBold ✓ |
| `Text.Status` | **11** | Regular | 15 | `Font.Ui` | status bar | 11 ✓ |
| `Text.Toolbar` | **12** | Regular | 17 | `Font.Ui` | teksty w paskach narzędzi | mieszane ✗ |
| `Text.Caption` | **10** | Regular | 14 | `Font.Ui` | podpisy pomocnicze, chipy skrótów | 10 (×52) |
| `Text.SectionHeader` | **11** | SemiBold | 15 | `Font.Ui` | `group-header` | 11 SemiBold ✓ |
| `Text.Title` | **14** | SemiBold | 19 | `Font.Ui` | nazwa połączenia, tytuł panelu | 14 (×3) |
| `Text.DialogHeader` | **16** | SemiBold | 22 | `Font.Ui` | `h1` | 16 ✓ |
| `Text.Display` | **23** | SemiBold | 30 | `Font.Ui` | wyłącznie okno About | 23 (×1) |

### §6.1 Tokeny bazowe rodzin

| Token | Wartość |
|---|---|
| `Font.Ui` | domyślna systemowa (Inter, jak dziś) |
| `Font.Code` | **`Cascadia Mono, Consolas, Menlo, monospace`** (decyzja D2) |

**7 ciągów → 1.** Wybrana wersja `Mono` (bez ligatur) obowiązuje edytor SQL, debugger, karty hover
i wszystkie podglądy DDL jednocześnie.

### §6.2 Rozstrzygnięcia w tabeli

**`Text.Code` = 13 px, jednoznacznie.** Zmierzono 25 edytorów na 13 i 6 na 12. Edytor kodu
o dwóch rozmiarach w jednej aplikacji jest defektem, nie decyzją. Wybrano 13, bo to wartość
dominująca i to na niej stroił się kontrast palety składni z D15.1.

**`FontSize=9` znika — 7 wystąpień idzie do `Text.Caption` (10).** Wyjątek: dwa wystąpienia to
glify `▶` i `●` (`DebuggerTabView:750`, `PerformancePanelView:253`), gdzie 9 px stroi *rozmiar
znaku*, nie stopień pisma. Te dwa zostają jako lokalne i są odnotowane do weryfikacji w M5.

**Interlinia = ×1,4 dla treści, ×1,36 dla ról 11 px.** Przy 11 px daje 15 px, co w wierszu 22 px
zostawia dokładnie tyle miejsca, ile wymaga §5.1. To nie przypadek — te dwie liczby były
dobierane razem.

### §6.3 ⛔ Wyłączenie zakresu — paleta składni edytora

**Paleta składni SQL/PSQL (D15.1: SQL blue / PSQL violet / type teal / function yellow, oba
motywy, `FirebirdSql.xshd` + `.Light.xshd`) jest ZAMROŻONA.**

System typografii dotyka edytora kodu **wyłącznie** przez `Font.Code` i `Text.Code`. Nie dotyka
kolorów, nie dotyka `SemanticHighlighter`, nie dotyka `EditorDataTypeBrush` ani żadnego tokenu
`Editor*`. Ta praca została zaprojektowana osobno, przeszła dwie rundy QA użytkownika i jej
cofnięcie byłoby regresją, nie polerowaniem.

### §6.4 ⭐ Jak rola jest konsumowana — rozstrzygnięte pomiarem w M2a

Handover §7 zostawił otwarte pytanie, czy w Avalonii 12 rola typograficzna może być **jednym**
zasobem. **Zmierzone na kodzie projektu, nie na dokumentacji frameworka:** mechanizm już istnieje
i działa — `ControlStyles.axaml` ma pięć stylów klasowych, które są rolami w rozumieniu §6:

| Styl klasowy | Odpowiada roli |
|---|---|
| `TextBlock.field-label` (12) | `Text.Application` |
| `TextBlock.shortcut-chip` (10) | `Text.Caption` |
| `TextBlock.title` (14 SemiBold) | `Text.Title` |
| `TextBlock.h1` (16 SemiBold) | `Text.DialogHeader` |
| `TextBlock.group-header` (11 SemiBold) | `Text.SectionHeader` |

**Wniosek — rola ma dwie warstwy, dokładnie jak §3.2:**

1. **warstwa skalarna** (`Text.<Rola>.Size` / `.LineHeight` / `.Weight`, `Font.Ui`, `Font.Code`) —
   jedyna, którą da się skonsumować **wszędzie**: w implicit style kontrolki, w `ControlTemplate`,
   w code-behind, w AvaloniaEdit. To ona powstaje w **M2a**;
2. **warstwa klasowa** (`Classes="text-compact"`) — wygodne złożenie trzech skalarów w jeden
   atrybut, ale **działa tylko dla `TextBlock`**: wnętrze `TextBox`, komórki `DataGrid`
   czy nagłówka kolumny i tak musi czytać skalar w swoim własnym stylu.

⛔ **Warstwa klasowa NIE powstaje w M2a**, i to nie jest niedoróbka. Styl klasowy **jest już
konsumpcją** — a M2a ma zbudować system, nie włączyć go (handover §8). Wchodzi w **M2b**,
do `ControlStyles.axaml`, obok kontrolek, które go użyją — zgodnie z regułą UI #5 z `CLAUDE.md`
(wspólne style mają jeden dom; drugi plik `Styles` wyłącznie dla typografii ten dom rozbija).

---

## §7 Katalog — powierzchnie i kolory

### §7.1 RB‑4 — dlaczego aktywna zakładka czyta się odwrotnie w dwóch motywach

To jest najsubtelniejszy defekt w audycie i warto go opisać dokładnie, bo „oczywista" diagnoza
jest błędna.

**Zmierzone drabiny:**

```
Dark:   Background #1E1E1E  →  Panel #252526  →  Elevated #2D2D2D     (jaśniej, +7 +8)
Light:  Background #F3F3F3  →  Panel #E0E0E0  →  Elevated #D6D6D6     (ciemniej, −19 −10)
```

**Odwrócenie kierunku samo w sobie NIE jest błędem.** Chroma oddala się od powierzchni dokumentu
— w Dark ku jasności, w Light ku ciemności. Tak robi VS Code, Rider i DataGrip. Ta część jest
poprawna i zostaje.

**Błąd polega na tym, że `ElevatedPanelBrush` wykonuje DWIE różne prace:**

| Praca | Znaczenie | Przykłady |
|---|---|---|
| **(a) chroma o stopień dalej od dokumentu** | „to jest jeszcze bardziej chromą" | nagłówki kolumn, pasek tytułu |
| **(b) element unosi się nad swoim kontenerem** | „to pływa nad tym, na czym leży" | aktywna zakładka, popup, menu, lista uzupełniania |

**W Dark obie prace zbiegają się na tej samej wartości. W Light są przeciwne.**

Powód jest fizyczny, nie stylistyczny: *uniesienie* czyta się jako ruch **ku światłu** w każdym
motywie — światło pada z góry, to konwencja starsza od komputerów. W Light theme oddalenie się
od bieli = wejście w cień = **wgłębienie**. Dlatego `TabItem:selected`, opisana w kodzie jako
*„a raised, filled segment"*, w Dark faktycznie się unosi, a w Light wygląda na wciśniętą.
Jeden styl, dwa przeciwne komunikaty.

**Rozwiązanie — jeden nowy token, minimalna zmiana:**

| Token | Zmiana | Konsumenci |
|---|---|---|
| `BackgroundBrush` | bez zmian | — |
| `PanelBrush` | bez zmian | — |
| `ElevatedPanelBrush` → **`ChromeStrongBrush`** | **przemianowanie** (praca *a*) | 20 miejsc |
| **`SurfaceRaisedBrush`** | **NOWY** (praca *b*) | ~8 miejsc |

`SurfaceRaisedBrush` idzie **ku światłu w obu motywach**: Dark `#2D2D2D` (zbiega się z chromą,
jak dziś), Light **`#FFFFFF` + obramowanie 1 px**.

⚠ **Przemianowanie dotyczy tylko tego jednego tokenu i tylko dlatego, że jego nazwa kłamie po
podziale.** `BackgroundBrush`/`PanelBrush` zostają — przemianowanie ich to czysta churn bez
korzyści dla użytkownika (UX Debt D‑5).

### §7.2 Przeprojektowana skala szarości Light

Poza defektem z §7.1 motyw jasny jest po prostu ciężki: `#E0E0E0` chromy i `#BDBDBD` obramowań
rysują siatkę, która konkuruje z treścią (narusza §3.4 specyfikacji — *calm interface*).

| Token | Dziś | Propozycja | Uzasadnienie |
|---|---|---|---|
| `BackgroundBrush` | `#F3F3F3` | **`#FCFCFD`** | ⭐ decyzja Q7 — patrz §7.2.1 |
| `PanelBrush` | `#E0E0E0` | **`#F3F4F6`** | chroma odróżnialna od dokumentu, ale nie dominująca |
| `ChromeStrongBrush` | `#D6D6D6` | **`#E8EAED`** | jeszcze stopień dalej, wciąż spokojnie |
| `SurfaceRaisedBrush` | — | **`#FFFFFF`** + `BorderBrush` | uniesienie ku światłu (§7.1) |
| `BorderBrush` | `#BDBDBD` | **`#D8DBE0`** | obramowanie ma oddzielać, nie rysować |
| `ForegroundBrush` | `#1F1F1F` | **`#1B1D1F`** | kosmetyka |
| `SubtleForegroundBrush` | `#6E6E6E` | **`#5F6570`** | patrz §7.4 |

Dark pozostaje bez zmian poza `SubtleForegroundBrush` (§7.4).

#### §7.2.1 ⭐ Decyzja Q7 — `#FCFCFD`, nie czysta biel

> **Decyzja użytkownika:** *„Aplikacja będzie używana przez wiele godzin dziennie i zależy mi
> bardziej na komforcie pracy niż maksymalnym kontraście."*

Kierunek jest wiążący: **tło dokumentu w Light Theme zbliżone do `#FCFCFD`, nie `#FFFFFF`.**
Użytkownik zostawił swobodę na drobną korektę wartości podczas implementacji — kierunek nie.

**Dlaczego to jest dobra decyzja, a nie kompromis:** czysta biel przy `ForegroundBrush #1B1D1F`
daje kontrast ~17:1, czyli ponad trzykrotność progu AA. Nadmiar kontrastu nie poprawia
czytelności — podnosi jasność całego pola widzenia przy ośmiogodzinnej sesji. `#FCFCFD` obniża
go nieznacznie (~16,5:1), pozostając bezpiecznie ponad każdym progiem z §10, i **odsuwa
powierzchnię dokumentu od bieli na tyle, że `SurfaceRaisedBrush = #FFFFFF` (§7.1) ma dokąd
się unieść.**

⭐ To ostatnie jest niezamierzoną korzyścią i warto ją zapisać: przy `Background = #FFFFFF`
uniesiona powierzchnia nie miałaby jak być jaśniejsza od tła i §7.1 wymagałby innego rozwiązania
(cień albo obramowanie jako jedyny sygnał). **Wybór `#FCFCFD` sprawia, że drabina uniesienia
w Light Theme działa tym samym mechanizmem co w Dark.** Decyzja podjęta z powodów ergonomicznych
okazała się też poprawna architektonicznie.

### §7.3 ⚠ Konsekwencja, którą trzeba zweryfikować, a nie założyć

Zmiana `BackgroundBrush` w Light z `#F3F3F3` na `#FFFFFF` **zmienia tło, na którym stoi cała
paleta składni edytora w motywie jasnym.** Paleta jest zamrożona (§6.3), ale jej *kontrast
względem tła* się przesuwa.

**Wymóg:** przed zamknięciem M2b trzeba przeliczyć kontrast wszystkich czterech barw składni
(`#0F766E` typy, `#795E26` funkcje, `#5D30A6` PSQL, SQL blue) oraz komentarzy (`#2E8B57`)
względem `#FFFFFF` i potwierdzić ≥4,5:1. **Jeśli któraś nie przejdzie — zmieniamy tło, nie
paletę**, bo paleta ma za sobą akceptację użytkownika, a tło jej nie ma.

### §7.4 Kontrast tekstu drugorzędnego — H‑7

Zmierzone dziś: **Light 4,60:1, Dark 4,52:1** — obie na granicy WCAG AA (4,5:1), przy `FontSize=11`,
dla głównego elementu nawigacyjnego narzędzia używanego 8 godzin dziennie.

Specyfikacja (§6.5, §8.9) twierdzi, że problem dotyczy głównie Light. **Pomiar tego nie
potwierdza — Dark jest równie słaby**, po prostu mniej rzuca się w oczy.

| Motyw | Dziś | Propozycja | Kontrast po zmianie |
|---|---|---|---|
| Light | `#6E6E6E` | **`#5F6570`** | **5,32:1** (na `PanelBrush #F3F4F6`) |
| Dark | `#858585` | **`#9AA0A6`** | **6,32:1** (na `BackgroundBrush #1E1E1E`) |

### §7.5 §8.6 — semantyka kolorów (⭐ skorygowana po decyzji użytkownika Q6)

> **Decyzja użytkownika:** *„Porządkujemy semantykę kolorów, ale nie zabijamy charakteru
> interfejsu. Jeżeli kolor występuje, powinien mieć jednoznaczne znaczenie."*
>
> ⚠ **Pierwsza wersja tej sekcji proponowała przeniesienie ikon narzędzi na neutralne
> i była BŁĘDNA.** Dokładniejszy pomiar pokazał, dlaczego — zapisane niżej, bo „ujednolićmy
> kolory ikon w pasku" brzmi rozsądnie i wróci.

#### Pomiar — pasek narzędzi ma JUŻ działający system semantyczny

```
10 przycisków „Nowy X"   → IconColor_Table / _View / _Procedure / _Trigger / _Function /
                           _Generator / _Domain / _Package / _Exception / _Role      ✓ znaczące
 6 narzędzi ogólnych     → AccentBrush                                               ✗
 Icon.Trash (destrukcja) → WarningIconBrush                                          ✗
 Icon.PlugZap            → AccentIconBrush                                           ✗
 Icon.RefreshCw          → InfoIconBrush                                             ✗
```

**Kolory rodzajów obiektów to nie dekoracja — to ten sam język, który użytkownik czyta w drzewie
metadanych i na zakładkach dokumentów przez cały dzień.** To one dają paskowi charakter i one
odpowiadają na pytanie *„dlaczego ten przycisk ma taki kolor?"* zanim ktokolwiek je zada:
bo tabele mają ten kolor wszędzie.

Defekt jest węższy, niż zakładałem: **sześć narzędzi ogólnych świeci na niebiesko i konkuruje
z tym systemem o uwagę**, a trzy tokeny wykonują nakładające się prace.

#### Kontrakt — dwa niezależne systemy + neutralność jako wartość domyślna

**System 1 — KOLOR RODZAJU.** Odpowiada na pytanie *„czego to dotyczy?"*
Tokeny `IconColor_*`. **Zostaje bez żadnej zmiany.** Obowiązuje wszędzie, gdzie element
identyfikuje rodzaj obiektu: drzewo, zakładki, menu, przyciski „Nowy X", nagłówki edytorów.

**System 2 — KOLOR SKUTKU.** Odpowiada na pytanie *„co to zrobi z moimi danymi?"*

| Kategoria | Token | Reguła |
|---|---|---|
| **Primary Action** | `AccentBrush` | **maksymalnie jedna na ekran** — Execute, Start debugging, Import |
| **Success / Commit** | `CommitButtonBrush` | **wyłącznie** zatwierdzenie transakcji |
| **Warning / Rollback** | `RollbackButtonBrush` | wycofanie, odrzucenie zmian |
| **Dangerous** | `DangerIconBrush` | **wyłącznie** operacje nieodwracalne — Drop, Delete, Stop |

**Neutralność.** `ForegroundBrush` dla wszystkiego, co nie odpowiada na żadne z tych dwóch pytań.
⚠ **Neutralny to nie „gorszy" — to poprawna odpowiedź dla narzędzia ogólnego.** Search, Refresh,
Save, Open nie dotyczą konkretnego rodzaju obiektu i nie robią nic nieodwracalnego z danymi.

#### Reguła rozstrzygająca

> **Ikona dostaje kolor, jeśli odpowiada na pytanie „czego to dotyczy?" (rodzaj) albo
> „co to zrobi z moimi danymi?" (skutek). Jeśli na żadne — jest neutralna.**
>
> Kolor rodzaju i kolor skutku nigdy nie występują na tym samym elemencie. Gdyby kiedyś miały —
> **wygrywa skutek**, bo ostrzeżenie o nieodwracalności jest ważniejsze niż informacja o typie.

To jest test na obawę użytkownika (*„dlaczego jeden przycisk jest niebieski, drugi zielony,
a trzeci szary?"*): zielony, bo dotyczy obiektu, który wszędzie indziej też jest zielony;
niebieski, bo to główna akcja tego ekranu; szary, bo to narzędzie ogólne. **Każda odpowiedź jest
jednozdaniowa i sprawdzalna.**

#### Zmiany do wykonania w M2b

| Element | Dziś | Docelowo | Powód |
|---|---|---|---|
| 10 przycisków „Nowy X" | `IconColor_*` | **bez zmian** | działa, jest znaczące, daje charakter |
| 6 narzędzi ogólnych | `AccentBrush` | `ForegroundBrush` | nie są akcją główną; konkurują z kolorami rodzajów |
| `Icon.Trash` (Usuń połączenie) | `WarningIconBrush` | `DangerIconBrush` | operacja nieodwracalna; zgodnie z regułą zapisaną przy Seam 4 |
| `Icon.PlugZap`, `Icon.RefreshCw` | `AccentIconBrush`, `InfoIconBrush` | `ForegroundBrush` | narzędzia ogólne |
| `AccentIconBrush`, `InfoIconBrush` | 2 tokeny | **zlikwidowane** | dublują `AccentBrush` / `SubtleForegroundBrush` |

⭐ **Efekt: pasek narzędzi POZOSTAJE kolorowy** — dziesięć ikon rodzajów, Commit, Rollback i akcje
destrukcyjne nadal niosą barwę. Znika wyłącznie niebieska tapeta, na której te kolory dziś się gubią.
**To jest realizacja §3.4 specyfikacji (*calm interface*) bez utraty tożsamości.**

⚠ Pełna tabela przypisań per przycisk powstaje w M2b i wchodzi do przeglądu — powyżej jest reguła,
nie lista.

---

## §8 Katalog — powierzchnie stale widoczne

> Sekcja realizuje zasadę §0.1. §8.1–§8.3 to pasek zakładek, §8.4–§8.8 to Status Bar.

### §8.0 Pasek zakładek (decyzje D5–D9)

### §8.1 Dlaczego bez `MaxWidth` i wielokropka (decyzja D6)

Propozycja skracania nazw zakładek została **wycofana**, a argument użytkownika potwierdza
dowód z jego własnego środowiska:

> W pasku występują jednocześnie `XXX_GG_WYSTCECHKART_AU99` i `XXX_GG_WYSTCECHKART_BU99`.
> **Różnią się znakiem nr 20.** Przy `MaxWidth≈140` obie wyrenderowałyby się jako
> `XXX_GG_WYSTCECHKA…` — **nierozróżnialnie**.

Obiekty bazodanowe mają wspólne prefiksy z natury (konwencje nazewnicze, moduły, typy wyzwalaczy),
a **różnicujący fragment jest na końcu**. Skracanie od końca niszczy dokładnie tę informację,
dla której pasek istnieje. Dodatkowo identyfikatory Firebird są ograniczone (31 znaków w FB3,
63 w FB4+), więc problem nie jest nieograniczony.

⛔ **Nie proponować tego ponownie.** Zapisane, bo „przytnijmy długie zakładki" brzmi rozsądnie
i wróci.

### §8.2 Tokeny i zachowanie

| Token / ustawienie | Wartość | Uwagi |
|---|---|---|
| `Size.Row.Tab` | 26 px | ikona 14 + `Pad.Tab` |
| `Size.TabIndicator` | 2 px | pasek akcentu aktywnej zakładki *(już taki jest)* |
| `TabStrip.MaxRows` | **1–10, domyślnie 3** | preferencja, `PreferenceRange` |
| `TabStrip.Mode` | `MultiRow` \| `SingleRow` | preferencja, domyślnie `MultiRow` |

**Tryb wielowierszowy (domyślny).** Po przekroczeniu `MaxRows` przewija się **wyłącznie pasek
zakładek** — pionowo, kółkiem myszy. Interfejs pod nim nie drgnie (§13.3 specyfikacji).
**Żadna zakładka nie znika za menu** — to jest istota decyzji użytkownika i odróżnia to
rozwiązanie od Visual Studio.

**Tryb pojedynczego wiersza.** Przewijanie poziome kółkiem + przycisk przepełnienia
**z licznikiem** (`⌄ 12`), który otwiera **listę wszystkich zakładek z filtrowaniem po nazwie**.
Licznik jest istotny: użytkownik widzi, ile dokumentów jest poza ekranem, zamiast się domyślać.
Filtrowana lista realizowana przez **istniejący `SearchableComboBox`** (reuse before create).

W tej postaci tryb B jest ergonomicznie lepszy od VS przy dużej liczbie dokumentów, a nie jego
kopią — co jest zgodne z §14.5 specyfikacji.

### §8.3 Menu kontekstowe zakładki (decyzja D9)

Osiem pozycji. Kolumna „stoi na" pokazuje, że **żadna nie wymaga nowej maszynerii**:

| Pozycja | Stoi na |
|---|---|
| Zamknij | `WorkspaceTabViewModel.CloseCommand` |
| Zamknij pozostałe | agregacja niezapisanej pracy, `MainWindowViewModel:2482` — dziś 3 wejścia, to 4. |
| Zamknij wszystkie | jw. |
| Zamknij zakładki po prawej | jw. |
| Zamknij niezmodyfikowane | `WorkspaceTabViewModel.UnsavedWork` |
| Odśwież | `WorkspaceTabViewModel.RefreshAsync()` *(zbudowane przy Seam 6d)* |
| Kopiuj nazwę obiektu | trywialne |
| Pokaż w Metadata Explorer | selekcja płaskiej listy + przewinięcie do wiersza |
| — separator — | |
| Ustawienia zakładek… | skrót do Settings Center (decyzja D8) |

⚠ **Reguła #11 obowiązuje bezwzględnie:** każda operacja masowego zamykania przechodzi przez
istniejącą bramkę **Save / Discard / Cancel**. „Zamknij wszystkie" nie może po cichu odrzucić
skompilowanej pracy w ośmiu edytorach. Bramka istnieje i jest przetestowana — chodzi o to,
żeby czwarte wejście z niej skorzystało, a nie ją ominęło.

⚠ Menu korzysta z **istniejącego zestawu stylów `ContextMenu`/`MenuItem`** (Keyboard Manager
etap 5) — ikony przez `{app:MenuIcon}`, gesty przez `{app:CommandGesture}`. Zero nowej chromy.

---

### §8.4 Status Bar 2.0 — model

> **Rozszerzone oczekiwanie użytkownika, 2026-08-01:** Status Bar ma być *„jednym z najmocniejszych
> elementów całego interfejsu"* i *„centrum informacji o aktualnym stanie aplikacji"*, a nie
> miejscem na tekst. Wymaga subtelnych kolorów, ikon semantycznych, hierarchii wizualnej oraz
> **wyraźnej informacji o aktywnej transakcji**.
>
> Ograniczenie z §8.5 specyfikacji obowiązuje: **wysokość nie rośnie** (`Size.StatusBar = 24`).

#### ⭐ Rozróżnienie, na którym stoi cały projekt: RAIL vs CHIP

Status Bar niesie dwa różne rodzaje informacji i mieszanie ich jest powodem, dla którego paski
statusu w większości narzędzi są nieczytelne:

| | **Rail** | **Chip** |
|---|---|---|
| Odpowiada na pytanie | **co aplikacja ROBI teraz** | **co jest PRAWDĄ teraz** |
| Nośnik | kolor cienkiego paska | ikona + tekst |
| Liczba naraz | **dokładnie jeden** (priorytetowany) | **dowolnie wiele** (współistnieją) |
| Przykłady | wykonywanie SQL, debugowanie, błąd | aktywna transakcja, Trace, DEV MODE |
| Czas życia | chwilowy | trwa, dopóki warunek jest prawdziwy |

**Aktywna transakcja jest CHIPEM, nie stanem railu** — bo współistnieje ze wszystkim: można mieć
otwartą transakcję i jednocześnie debugować, wykonywać zapytanie albo nie robić nic. Wepchnięcie
jej do railu zmusiłoby do wyboru, którą z dwóch prawdziwych informacji pokazać.

#### §8.4.1 Rail — cienki pasek akcentu

**Propozycja: rail to 2 px pasek wzdłuż GÓRNEJ krawędzi Status Bara, na pełną szerokość okna.**

⭐ **Kluczowa obserwacja: ten pasek już istnieje.** Status Bar ma dziś
`BorderThickness="0,1,0,0"` — linia oddzielająca go od obszaru pracy. Rail **nie dodaje nowego
elementu do interfejsu; nadaje pracę linii, która już tam jest.** W stanie spokoju maluje się
`BorderBrush` i jest zwykłym separatorem. Gdy coś się dzieje — ta sama linia przyjmuje kolor.

To jest dokładnie §3.3 specyfikacji (*every pixel has a purpose*) i §13.6 (*less is more*):
zero nowych pikseli, jeden element więcej znaczy.

**Dlaczego górna krawędź, a nie dolna ani cały pasek:**
- to granica między obszarem pracy a paskiem statusu — wzrok przechodzi przez nią naturalnie,
  odczytanie stanu **nie wymaga odrywania się od kodu**,
- użytkownik wprost odrzucił agresywną zmianę koloru całego paska (model Visual Studio),
- 2 px to **ten sam token co wskaźnik aktywnej zakładki** (`Size.TabIndicator`) — jeden język
  wizualny, nie drugi.

**Rail ma stałą grubość 2 px w każdym stanie** — zmienia się wyłącznie kolor. Zero layout shift
przez konstrukcję (§13.3 specyfikacji).

⚠ Kolor nigdy nie jest jedynym nośnikiem (§10): każdy stan niesie także **ikonę i tekst**
w sekcji stanu. Rail jest wzmocnieniem peryferyjnym — czytelnym kątem oka — a nie komunikatem.

> ⭐ **SWOBODA PROJEKTOWA (użytkownik, 2026-08-01).** Rail jest *propozycją*, nie kontraktem.
> *„Jeżeli podczas implementacji znajdziesz rozwiązanie jeszcze bardziej eleganckie i lepiej
> wpisujące się w całość produktu, nie trzymaj się kurczowo tej koncepcji tylko dlatego,
> że znalazła się w dokumentacji. Najważniejszy jest efekt końcowy."*
>
> **Wiążące pozostają wymagania, nie realizacja:** rozdzielenie Rail/Chip (§8.4), cztery sekcje
> (§8.4.3), hierarchia wizualna (§8.4.4), widoczna transakcja (§8.4.5), brak wzrostu wysokości,
> zakaz agresywnej zmiany koloru całego paska, kolor nigdy jako jedyny nośnik.
>
> ⚠ Zamiana koncepcji wymaga **zapisania powodu w tym miejscu** — inaczej za pół roku nikt nie
> odróżni świadomej zmiany od tego, że ktoś nie doczytał dokumentu.

#### §8.4.2 Stany semantyczne i priorytet

Stany z §8.5 specyfikacji, uporządkowane. **Rail pokazuje stan o najwyższym priorytecie
spośród aktywnych.**

| Priorytet | Stan | Kolor railu | Ikona |
|---|---|---|---|
| 5 | **Error** | `ErrorBrush` | ✕ |
| 4 | **Warning** | `WarningBrush` | ⚠ |
| 3 | **Debug Active** | akcent debuggera | `DebuggerIcon` |
| 2 | **Executing SQL / Loading** | `AccentBrush` | ⟳ |
| 1 | **Trace Active** | `IconColor_Query` *(do weryfikacji)* | ⏺ |
| 0 | **Ready** | `BorderBrush` — **rail jest zwykłym separatorem** | — |

**Success** jest stanem przejściowym: pokazuje się jako krótki impuls `ConnectedBrush`, po czym
rail wraca do stanu leżącego pod spodem. Nie ma własnego priorytetu, bo nigdy nie jest stanem
trwałym.

**Uzasadnienie kolejności:** błąd wygrywa ze wszystkim, bo wymaga reakcji. Debug wygrywa
z wykonywaniem, bo krokowanie *jest* wykonywaniem — pokazanie „wykonuję" podczas sesji
debuggera byłoby prawdziwe i bezużyteczne. Trace przegrywa z wykonywaniem, bo jest tłem pracy,
a nie jej treścią.

#### §8.4.3 Sekcje

Kolejność ustalona przez użytkownika, od lewej:

```
│ ● Szkoleniowa · localhost:3050 · DEV │ ← komunikat ──────────→ │ ⚡ TX · 2 min │ ⏺ │ ▓▓▓░ │
  └─ 1. POŁĄCZENIE (Auto) ────────────┘  └─ 2. KOMUNIKATY (*) ─┘  └─ 3. STAN ──────┘ └ 4 ─┘
```

| # | Sekcja | Szerokość | Zawartość |
|---|---|---|---|
| 1 | **Połączenie** | `Auto` | kropka stanu, nazwa połączenia, serwer:port, `DEV MODE` |
| 2 | **Komunikaty** | `*` | ostatni komunikat aplikacji, z ikoną severity |
| 3 | **Stan** | `Auto` | chipy: transakcja, Trace, Debugger |
| 4 | **Postęp** | `Auto` | pasek postępu + anulowanie |

⭐ **Sekcja połączenia jest skrajnie lewa i to jest wymóg strukturalny, nie estetyczny.**
§8.5 specyfikacji żąda: *„Komunikaty nie powinny zasłaniać informacji o połączeniu."*
Przy kolejności `Auto | * | Auto | Auto` sekcja połączenia **nie może się przesunąć** — rosnący
komunikat zjada wyłącznie elastyczną kolumnę 2. Wymóg jest spełniony przez układ, a nie przez
ostrożność autora kolejnej zmiany.

#### §8.4.4 Hierarchia wizualna

Dziś stan połączenia i komunikaty mają **identyczną wagę wizualną** (`Classes="subtle"`,
`FontSize=11`) — dwie informacje o zupełnie różnym priorytecie wyglądają tak samo. To jest
sedno zarzutu „Status Bar jest tylko miejscem na tekst".

| Element | Rola typograficzna | Kolor |
|---|---|---|
| Nazwa połączenia | `Text.Status` **SemiBold** | `ForegroundBrush` |
| Serwer:port | `Text.Caption` | `SubtleForegroundBrush` |
| Komunikat — Info | `Text.Status` | `SubtleForegroundBrush` |
| Komunikat — Warning / Error | `Text.Status` | `WarningBrush` / `ErrorBrush` + ikona |
| Chip stanu | `Text.Caption` | kolor stanu |

⚠ Mapowanie severity komunikatu na kolor i ikonę **czyta z `MessageBanner.BrushKeyFor` /
`GeometryKeyFor`** — tak jak robi to już log Messages w edytorze SQL. Zero drugiej definicji
severity w aplikacji.

#### §8.4.5 ⭐ Chip transakcji — najważniejsza informacja w pasku

> **Użytkownik:** *„w aplikacji pracującej z bazami danych jest to jedna z najważniejszych
> informacji dla użytkownika"*.

Dostępne dziś: `MainWindowViewModel.IsTransactionActive` / `IsTransactionIdle`
(z `TransactionService`), `TransactionActiveBrush`, `TransactionProfileCatalog`.

**Projekt chipa:**

| Stan | Wygląd |
|---|---|
| Brak transakcji | chip **nieobecny** — brak transakcji to stan domyślny, nie wymaga komunikatu |
| Transakcja aktywna | `⚡ Transakcja · <czas>` w `TransactionActiveBrush`, tooltip z profilem i lane |

⚠ **Czas trwania wymaga weryfikacji przed obietnicą.** Długo otwarta transakcja to realne
ryzyko (blokady, wstrzymana garbage collection) i Session Manager ma już detektor
long-running transaction — czyli dane istnieją. **Czy da się je odczytać tanio i bez odpytywania
`MON$`, jest do sprawdzenia w M3.** Jeśli nie — chip pokazuje sam stan, a czas trafia do UX Debt.
⛔ Nie obiecuję tego w katalogu jako pewnika.

⚠⚠ **Chip transakcji nigdy nie jest przyciskiem.** Commit i Rollback zostają tam, gdzie są —
w toolbarze, pod `F6`/`Shift+F6`. Przeniesienie ich do paska statusu byłoby zmianą sposobu
pracy (§14.1 specyfikacji) i umieszczeniem operacji nieodwracalnej w miejscu, które użytkownik
czyta kątem oka.

#### §8.4.6 Sekcja postępu

Wymagania z §8.5 specyfikacji: postęp procentowy, tryb nieokreślony, możliwość anulowania.

| Element | Projekt |
|---|---|
| Pasek | wysokość **4 px**, `Radius.Surface` (§4.2.2), `AccentBrush` na `ChromeStrongBrush`, szerokość stała **120 px** |
| Tryb nieokreślony | ten sam pasek, animacja przesuwana — **wyłącznie `Opacity`/pozycja, nigdy `Width`** (§9) |
| Anulowanie | `Button.icon` z `Icon.X`, widoczny **tylko** gdy operacja jest anulowalna |
| Etykieta | `Text.Caption`, po lewej stronie paska |

⭐ **Szerokość stała, nie elastyczna.** Pasek postępu rosnący z zawartością przesuwałby chipy
stanu przy każdej operacji — czyli §13.3 rozłożony w czasie. Stała szerokość oznacza, że
pojawienie się postępu przesuwa układ **raz**, o znaną wartość, i nigdy w trakcie trwania operacji.

⚠ **M3 dostarcza sekcję i podłącza JEDNĄ operację referencyjną** (proponuję wykonanie zapytania
SQL — najczęstsza i najlepiej oprzyrządowana). Pozostałe operacje to **M3b** (decyzja D4).
Powód rozdzielenia jest w §1.3/H‑4 i pozostaje aktualny: wspólna infrastruktura postępu dotyka
ViewModeli w całej aplikacji i jest pracą funkcjonalną, nie stylistyczną.

#### §8.4.7 Czego w Status Barze NIE będzie

| Element | Powód |
|---|---|
| Nazwa aplikacji i numer wersji | decyzja D3 — pozostają w About |
| Przyciski Commit / Rollback | §8.4.5 |
| Zmiana koloru całego paska | użytkownik odrzucił model Visual Studio |
| Wzrost wysokości | §8.5 specyfikacji wprost zabrania |
| Więcej niż jeden rail naraz | §8.4.1 — priorytet rozstrzyga |

---

## §9 Katalog — ruch i animacja

Specyfikacja mówi „calm" (§3.4), ale nie stawia liczby. Fluent wnosi własne przejścia.

| Reguła | Wartość |
|---|---|
| Maksymalny czas przejścia | **120 ms** |
| Dozwolone właściwości | **wyłącznie `Opacity` i kolory** (`Background`, `Foreground`, `BorderBrush`) |
| Zakazane | **każde przejście na właściwości wpływającej na układ** — `Width`, `Height`, `Margin`, `Padding` |
| Krzywa | `CubicEaseOut` |

**Powód zakazu:** animacja na wymiarze to §13.3 specyfikacji (*Zero Layout Shift*) rozłożony
w czasie. Element, który „dojeżdża" do swojego rozmiaru, przesuwa sąsiadów przez 120 ms —
subiektywnie to dokładnie ten sam defekt.

---

## §10 Katalog — progi kontrastu

Specyfikacja wymaga „odpowiedniego kontrastu" (§8.5), ale to nie jest testowalne, a §10 wymaga
oceny. Bez liczby motywy rozjadą się ponownie.

| Element | Próg | Norma |
|---|---|---|
| Tekst treści (< 14 px) | **≥ 4,5:1** | WCAG AA |
| Tekst duży (≥ 14 px lub ≥ 12 px SemiBold) | **≥ 3:1** | WCAG AA Large |
| Obramowania i elementy UI niosące znaczenie | **≥ 3:1** | WCAG 2.1 SC 1.4.11 |
| Stan aktywny vs nieaktywny (zakładki, przełączniki) | **≥ 3:1 między sobą** | wymóg własny |

Ostatni wiersz jest wymogiem EmberTerna, nie WCAG: różnica **między** stanem aktywnym
a nieaktywnym musi być odczytywalna, a nie tylko każdy z nich wobec tła. To właśnie ten warunek
łamie dziś Light Theme (§7.1).

⚠ Kolor **nigdy nie jest jedyną informacją** (§8.5 specyfikacji) — stan niesie także ikona,
tekst lub waga pisma.

---

## §11 Egzekwowanie — jak nie odbudować długu

Katalog bez strażnika odrasta. Projekt ma na to twardy precedens: **skrót `Alt+F` wpisany ręcznie
w tooltip przeżył przepięcie komendy na `Ctrl+K` przez cały etap, przy zielonym buildzie
i zielonych testach** (gotcha #284). Wartość skopiowana ręcznie starzeje się po cichu.

**Propozycja: `DesignTokenComplianceTests`** — na wzór `UiStringsShortcutSourceTests`:

1. **Żaden plik w `Views/` ani `Controls/` nie deklaruje `FontSize`** poza jawną listą wyjątków,
   każdy z uzasadnieniem w kodzie testu.
2. **Żaden nie deklaruje `FontFamily`** — rodzina wyłącznie z `Font.Ui`/`Font.Code`.
3. **Żaden nie deklaruje `CornerRadius`** poza listą wyjątków.
4. **Lista wyjątków sama jest weryfikowana** — wyjątek wskazujący na nieistniejący plik lub
   nieaktualny powód wywala test. *(Bez tego lista staje się śmietnikiem — to samo rozwiązanie,
   co w `DocumentMutationContractTests`.)*

**Test powstaje w M2a — przed migracją, nie po.** Wtedy M2c ma mierzalny warunek zakończenia:
lista wyjątków skurczona do uzasadnionego minimum, test zielony.

### §11.1 ⭐ Kształt listy — zapadka licznikowa, nie lista plików (M2a, zaakceptowane)

Punkty 1–4 opisują listę **plików**. Pomiar stanu wyjściowego pokazał, dlaczego to za mało:
**609 wystąpień `FontSize` rozkłada się na 49 plików**, a rekordzista ma ich 86. Lista plików
zwolniłaby `DataImportTabView.axaml` w całości — mógłby dorzucić osiemdziesiąte siódme
wystąpienie bez żadnego sygnału, przy zielonym teście. Dokładnie ta cisza, przed którą test ma
chronić (gotcha #284).

**Dlatego wyjątek to para `plik → liczba wystąpień`, a test pilnuje jej w obie strony:**

| Kierunek | Znaczenie | Reakcja |
|---|---|---|
| liczba **wzrosła** | pojawiła się nowa wartość lokalna | użyj tokenu **albo** świadomie podnieś wartość referencyjną |
| liczba **spadła** | migracja się udała, ale nie została odnotowana | obniż wartość referencyjną — to jest postęp M2c |
| liczba **= 0** | plik jest czysty | usuń wpis; ponowne dopisanie wymaga decyzji |

⭐ **Zapadka wykrywa dryf, nie blokuje decyzji** *(uzupełnienie użytkownika przy akceptacji,
2026-08-01)*. Czerwony test nie znaczy „zrobiłeś źle" — znaczy „nazwij, którą z dwóch rzeczy
robisz". Jeżeli liczba zmienia się **celowo** i zmiana jest opisana w dokumentacji etapu,
aktualizacja wartości referencyjnej jest **prawidłową częścią procesu**, a nie obejściem
strażnika. Ta zasada jest wpisana wprost w komunikat błędu testu — czyta ją ten, kto go zobaczy,
a nie ten, kto akurat czyta ten dokument.

**Konsekwencja dla M2c:** warunek zakończenia przestaje być oceną („lista skurczona do
uzasadnionego minimum") i staje się liczbą — **suma liczników**, którą widać w jednym miejscu.

⚠ **Stan wyjściowy zmierzony w M2a** (`Views/` + `Controls/`, `.axaml` oraz `.axaml.cs`):
`FontSize` **609 / 49 plików** · `FontFamily` **83 / 28** · `CornerRadius` **37 / 13**.
To jest liczba, którą M2c ma sprowadzić do uzasadnionej reszty.

⚠ Nie obejmuję testem `Margin`/`Padding` — są zbyt kontekstowe (rozmieszczenie w układzie to
odpowiedzialność hosta, nie chromy) i test byłby albo dziurawy, albo uciążliwy. Tam narzędziem
jest przegląd w M2c i M5.

---

## §12 Pytania — rozstrzygnięte

**Q6 — semantyka kolorów w pasku narzędzi. ✅ ROZSTRZYGNIĘTE 2026-08-01.**
Użytkownik: *„Porządkujemy semantykę kolorów, ale nie zabijamy charakteru interfejsu."*
Moja pierwotna propozycja (neutralizacja ikon narzędzi) była zbyt radykalna. Dokładniejszy
pomiar pokazał, że pasek **ma już działający system kolorów rodzajów obiektów** i to on daje
charakter. Skorygowany kontrakt w §7.5: dwa systemy (rodzaj + skutek), neutralność jako wartość
domyślna, pasek **pozostaje kolorowy**.

**Q7 — tło Light Theme. ✅ ROZSTRZYGNIĘTE 2026-08-01.**
`#FCFCFD`, nie czysta biel. Uzasadnienie i nieoczekiwana korzyść architektoniczna w §7.2.1.

### Do weryfikacji w trakcie implementacji (nie blokują startu)

| # | Pozycja | Etap |
|---|---|---|
| V‑1 | Kontrast czterech barw składni względem nowego tła `#FCFCFD` (§7.3) | przed zamknięciem M2b |
| V‑2 | Czy czas trwania transakcji da się odczytać tanio, bez odpytywania `MON$` (§8.4.5) | M3 |
| V‑3 | Token koloru railu dla stanu Trace (§8.4.2) — propozycja `IconColor_Query` do potwierdzenia | M3 |
| V‑4 | Grubość konturu ikon Lucide przy 14 px i skalowaniu 125% / 150% (§1.3/H‑9) | M5 |

---

## §13 Plan etapu

| Etap | Zakres | Zmiana widoczna? |
|---|---|---|
| **M0** ✅ | Audyt — §1 tego dokumentu | nie |
| **M1** ✅ | Ten katalog — **zaakceptowany 2026-08-01** | nie |
| **M2a** ⏳ | `Tokens.axaml` + `Typography.axaml` + `DesignTokenComplianceTests`. **Wyłącznie addytywne** — **NASTĘPNY KROK** | **nie** |
| **M2b** | Compact Controls (RB‑3, D1), własny `CheckBox` (RB‑2), DataGrid Standard, scrollbary (H‑10), `ToolTip` (M‑2), powierzchnie i kolory (RB‑4, §7) | **tak, duża** |
| **M2c** | Sweep de‑lokalizacyjny (H‑1) — lista wyjątków do minimum | tak |
| **M3.1** | **Status Bar 2.0** (§8.4) — rail, cztery sekcje, hierarchia, chip transakcji, sekcja postępu + **jedna** operacja referencyjna | tak |
| **M3.2** | Toolbar: stabilny układ (H‑3), semantyka kolorów (§7.5), spójność Commit/Rollback (H‑5) | tak |
| **M3.3** | **Pasek zakładek**: dwa tryby, limit wierszy, menu kontekstowe (D5–D9) + wiersze w Settings Center | tak |
| **M3.4** | **Metadata Explorer** (⭐ wyciągnięty z M4.2 — §0.1) + przegląd menu kontekstowych | tak |
| **M3b** | Podłączenie **pozostałych** operacji do paska postępu (D4) | tak |
| **M4.1** | SQL Editor, Script Executor, Data Import | tak |
| **M4.2** | Edytory obiektów (10) | tak |
| **M4.2b** | **Migracja 18 drzew „Zależności"** na wspólny komponent (D11) | tak |
| **M4.3** | Debugger, Trace, Session Manager, Security Manager, Performance | tak |
| **M4.4** | 16 dialogów + okna + `GrowingDialogBehavior` (M‑5) | tak |
| **M5** | Final Polish: oba motywy, kontrast §10, DPI 100/125/150/200, empty states (M‑3), terminologia + słownik (M‑4), focus (L‑1), animacje (§9) | tak |

### §13.0 Definition of Done — wspólne dla każdego etapu

Etap uznaje się za zakończony, gdy **wszystkie** poniższe są spełnione:

1. **Build 0 błędów / 0 ostrzeżeń** (`TreatWarningsAsErrors=true` w całym solution).
2. **Zielony pełny suite w TRZECH partycjach** (§13.1) — nie w jednym przebiegu.
3. **Smoke test** — aplikacja startuje, łączy się z bazą lab, wykonuje zapytanie.
4. **Oba motywy obejrzane** — Light i Dark, nie tylko ten, w którym się pracowało.
5. **QA wizualne użytkownika** — etap wizualny nie jest zamknięty bez potwierdzenia na żywo.
6. **Dokument zaktualizowany** — sekcja „as-built" dla etapu, z decyzjami i odstępstwami.
7. ⭐ **Pytanie z §0.1.1** — *„czy to wygląda na produkt komercyjny?"* — zadane osobno od
   pytania o zgodność techniczną.

Commit per etap. Push na **oba** remote'y po akceptacji (`origin` + `private`).

### §13.0.1 Zależności między etapami

```
M1 ──► M2a ──► M2b ──► M2c ──┬──► M3.1 ──► M3.2 ──► M3.3 ──► M3.4 ──► ⛔ BRAMA §13.3
                             │                                              │
                             └──────────────────────────────────────────────┴──► M4.x ──► M5
                                                                            
M3.1 ──► M3b   (postęp: infrastruktura przed podłączaniem operacji)
M3.4 ──► M4.2b (język wizualny wiersza drzewa przed ekstrakcją TreeListView)
```

**Zależności twarde — złamanie którejkolwiek zmarnuje pracę:**

| # | Zależność | Powód |
|---|---|---|
| Z‑1 | **M2b po M2a** | style implicit potrzebują tokenów, do których się odwołają |
| Z‑2 | **M2c po M2b** | nie można usunąć wartości lokalnej, zanim styl zacznie dostarczać jej zamiennik — inaczej ekran chwilowo traci wygląd |
| Z‑3 | **M3.x po M2c** | powierzchnie trwałe projektuje się na działającym systemie, nie na częściowo wdrożonym |
| Z‑4 | **M3b po M3.1** | najpierw sekcja postępu, potem podłączanie operacji (decyzja D4) |
| Z‑5 | **M4.2b po M3.4** | Metadata Explorer ustala język wizualny wiersza drzewa; `TreeListView` go ekstrahuje, a nie wymyśla drugi raz |
| Z‑6 | **M4.x po bramie §13.3** | migracja ekranów na ramę, która nie została zaakceptowana, to migracja do poprawki |

**Zależność miękka:** V‑1 (kontrast palety składni wobec `#FCFCFD`) musi być rozstrzygnięta
**przed zamknięciem M2b**, bo to M2b zmienia tło.

### §13.1 ⚠ Ryzyko testowe

Ten etap wygeneruje najwięcej testów headless w historii projektu. Obowiązuje gotcha #94/#226/#286:
nowa klasa headless **dołącza do `HeadlessCollection`**, nigdy nie zakłada własnego `IClassFixture`,
i **nie konstruuje `MainWindow`** (udokumentowany kształt zawieszający suite — `BrandingPresentationTests`
w pierwszej wersji wisiały, po przepisaniu na gołe `new Window()` biegną w 476 ms). Asercje
app‑wide robimy na najtańszej kontrolce, która może je unieść — to również *mocniejsza* asercja.

### §13.2 ⚠ Zależność: `TreeListView` (M4.2b)

Drzewa zależności **nie migrują na `SidebarFlatController`.** Ten kontroler powstał z powodu
**skali** (2389 tabel, 8178 indeksów — gotcha #154/#157) i jest sprzężony z połączeniem,
metadanymi, filtrowaniem, licznikami i indeksem nazw. Drzewa zależności mają kilkanaście węzłów;
migracja kupuje **spójność, nie wydajność** — i to jest wystarczający powód (§3.1 specyfikacji),
ale nie powód, by wciągać je w maszynerię, której nie potrzebują.

Zamiast tego: jeden współdzielony komponent prezentacyjny (płaska `ListBox` + wcięcie wg głębokości
+ chevron + ikona + etykieta + dwuklik), na który przechodzi 18 drzew, a wiersz sidebara zbiega się
do **tego samego języka wizualnego** bez ruszania jego kontrolera. §6.3 specyfikacji wprost na to
pozwala.

⚠ `ListBox` ma inną nawigację klawiaturową niż `TreeView` (brak natywnego ←/→ zwiń/rozwiń).
Trzeba ją odtworzyć — inaczej naruszamy §14.1 specyfikacji.

---

## §13.3 ⛔ BRAMA JAKOŚCI — przegląd Application Chrome po M3, przed M4

> **Wymóg użytkownika, 2026-08-01.** *„Po zakończeniu całego M3 chciałbym, abyś zrobił dodatkowy
> przegląd wyłącznie Toolbara, Status Bara, paska zakładek i Metadata Explorer. Nie pod kątem
> zgodności z dokumentem, ale pod kątem odbioru wizualnego całego produktu."*

**M4 nie zaczyna się, dopóki ta brama nie zostanie przejęta.**

### Czym ten przegląd NIE jest

⛔ To **nie jest** sprawdzenie zgodności z katalogiem. Zgodność jest weryfikowana testami
(`DesignTokenComplianceTests`) i w DoD każdego etapu. Powtarzanie jej tutaj byłoby stratą czasu.

### Czym jest

Przegląd **odbioru wizualnego czterech powierzchni trwałych oglądanych JEDNOCZEŚNIE**, w obu
motywach, na uruchomionej aplikacji z rzeczywistą bazą (Szkoleniowa: 2389 tabel, 1227 procedur —
nie na pustym środowisku).

**Pytania kontrolne:**

1. Czy te cztery elementy wyglądają na **jedną ramę**, czy na cztery poprawne komponenty?
2. Czy rytm pionowy (36 / 26 / 24) czyta się jako hierarchia, czy jako trzy przypadkowe wysokości?
3. Czy krawędzie i separatory podlegają jednej regule?
4. Czy sygnały stanu (kropka połączenia, DEV MODE, rail, akcent zakładki) to jeden zestaw?
5. ⭐ **Czy zrzut ekranu bez logo przeszedłby Marketing Test (§13.13 specyfikacji)?**
6. Czy którykolwiek element wygląda **przeciętnie mimo spełnienia wszystkich założeń**?

### Wynik

| Werdykt | Konsekwencja |
|---|---|
| Wszystkie cztery czytają się jako jedna dopracowana rama | brama otwarta → **M4** |
| Którykolwiek wygląda przeciętnie | **M3.5 — dopracowanie przed M4**, mimo zielonych testów |

⚠ **M3.5 nie jest porażką etapu — jest jego wbudowanym mechanizmem.** §0.1.1 mówi wprost,
że zgodność techniczna jest warunkiem koniecznym, nie wystarczającym. Brama istnieje po to,
żeby ta różnica została wychwycona **przed** migracją 40 ekranów na ramę, która jeszcze
nie jest dobra.

---

## §13.4 ⚠ Ryzyka implementacyjne

Poza ryzykiem testowym (§13.1) i zależnościami (§13.0.1):

| # | Ryzyko | Mitygacja |
|---|---|---|
| **R‑1** | **Wartość lokalna outranks setter stylu** — po M2b część ekranów nie zmieni wyglądu i będzie wyglądać na regresję | To jest przewidziane: M2c istnieje właśnie po to. **Nie „naprawiać" tego doraźnie w M2b** — inaczej powstanie druga warstwa obejść |
| **R‑2** | **Własny `ControlTemplate` dla `CheckBox`/`ScrollBar`** to największy techniczny fragment M2b | Zrobić **jako pierwszy** w M2b — jeśli okaże się droższy, niż zakładam, jest czas na korektę zakresu, a nie na koniec etapu |
| **R‑3** | **Zmiana tła Light (`#F3F3F3` → `#FCFCFD`) przesuwa kontrast palety składni** | V‑1 przed zamknięciem M2b. **Jeśli któraś barwa nie przejdzie 4,5:1 — zmienić tło, nie paletę** (§7.3) |
| **R‑4** | **Preferencje paska zakładek** (`TabStripMode`, `TabStripMaxRows`) trafiają do `Preferences` | **Dodanie właściwości jest addytywne — `CurrentSchemaVersion` ZOSTAJE 2.** Bump uruchamia downgrade protection i starsze buildy odrzucą cały plik (settings-center §5.2.3) |
| **R‑5** | **`TabStripMaxRows` to preferencja NUMERYCZNA** — pierwszy nowy taki przypadek od etapu 6 Settings Center | Obowiązuje cały gotowy wzorzec: `PreferenceRange` (1–10), **commit na BLUR lub ENTER, nigdy per keystroke**, bramka digits‑only na TUNELU, i ⚠⚠ **kontrolka musi mieć swój WIERSZ jako `DataContext`** — na DataContext strony pisze się poprawnie i **nie zapisuje nic** (settings-center §17.4/§17.4a) |
| **R‑6** | **Skalowanie DPI weryfikowane dopiero w M5** (§12 specyfikacji) — token o stałym pikselu okazujący się zły w M5 oznacza przeróbkę wszystkiego | ⭐ **Sprawdzać 150% na końcu KAŻDEGO etapu M2b–M3.4**, nie tylko w M5. Pełna matryca 100/125/150/200 zostaje w M5 |
| **R‑7** | **13 literałów angielskich w tooltipach** (M‑1) nie ma przypisanego etapu | Przypisane: **M3.2** — większość to tooltipy toolbara, więc trafiają tam, gdzie i tak się pracuje |
| **R‑8** | **Terminologia (M‑4) i empty states (M‑3) wymagają INWENTARZA przed poprawką** | M5 zaczyna się od dwóch inwentarzy: słownik pojęć → `docs/design/terminology.md` + test strażniczy; lista 48 widoków z oceną empty state |
| **R‑9** | **Zakres pełzający** — audyt wygenerował propozycje spoza specyfikacji | Decyzja D12: dodatki **nie mogą** ograniczyć żadnego pierwotnego założenia. Przy konflikcie czasu wygrywa specyfikacja, dodatek idzie do UX Debt |
| **R‑10** | **Paleta składni edytora** może zostać przypadkowo dotknięta przy pracy nad typografią | §6.3 — zamrożona. Dotykamy wyłącznie `Font.Code` i `Text.Code` |

---

## §14 As-built — M2a (infrastruktura tokenów)

> **Status: zaimplementowane 2026-08-01, oczekuje na QA wizualne użytkownika.**
> Build 0/0 · suite **7066** zielony w trzech partycjach (6998 + 54 + 14, +9) · smoke czysty.
> ⚠ Warunek 8 z DoD handovera — *„aplikacja wygląda IDENTYCZNIE"* — potwierdza użytkownik, nie test.

### §14.1 Co powstało

| Artefakt | Zawartość |
|---|---|
| `Themes/Tokens.axaml` | warstwa niekolorowa: 7 odstępów · 13 ról `Thickness` (§4.1) · 10 wysokości (§5) · 3 rozmiary ikon · 2 promienie · 1 grubość skalarna + 3 role krawędzi (§4.2) |
| `Themes/Typography.axaml` | 2 rodziny bazowe + 12 ról × (rozmiar · waga · interlinia) = 35 zasobów (§6) |
| `App.axaml` | oba słowniki w `MergedDictionaries`, **przed** `Colors.axaml` |
| `tests/EmberTern.Tests/DesignTokenComplianceTests.cs` | 9 przypadków — zapadka licznikowa (§11.1) + trzy strażniki samego katalogu |

**Zero zmian w widokach.** Jedyna modyfikacja poza nowymi plikami to dwie linie `ResourceInclude`
w `App.axaml`. Żaden token nie ma dziś konsumenta, więc renderowanie nie może się różnić —
to jest argument strukturalny, nie zapewnienie.

### §14.2 Decyzje podjęte w trakcie (zatwierdzone przed implementacją)

1. ⭐ **Katalog uzupełniony o §4.2** — ikony, promienie i krawędzie nie miały tabeli ról, a handover
   wymagał ich w `Tokens.axaml`. Wartości **zmierzone**, nie uśrednione; reguła operacyjna „rola przed
   wartością" w §4.2.4.
2. ⭐ **`Stroke.Rail` = 2 świadomie POMINIĘTY** mimo ratyfikacji w §8.4.2. Rail nie istnieje, a tokenu
   bez konsumenta nikt nie potrafi zweryfikować (reguła #233). Wchodzi w **M3.1**.
   *To jest odstępstwo od pierwotnej propozycji na rzecz węższego zakresu — na wyraźne życzenie
   użytkownika: „katalog rozszerzamy dopiero wtedy, gdy pojawi się realna potrzeba".*
3. ⭐ **Sposób złożenia roli typograficznej rozstrzygnięty POMIAREM** (§6.4): mechanizm już istniał
   w projekcie — pięć stylów klasowych w `ControlStyles.axaml` jest rolami z liczbami wpisanymi na
   sztywno. Wniosek: rola ma dwie warstwy, M2a dostarcza skalarną, klasowa wchodzi w M2b.
4. ⭐ **`Text.<Rola>.Weight` istnieje także dla ról Regular.** Dzięki temu konsumpcja roli jest zawsze
   mechaniczna (te same trzy klucze). Gdyby waga była tylko tam, gdzie odbiega od domyślnej, każde
   użycie roli zaczynałoby się od pytania „czy ta rola ma wagę?" — a pytanie zadawane przy każdym
   użyciu jest źródłem dryfu.
5. ⭐ **`Font.Ui` = `$Default`, nie `"Inter"`.** Rodzina interfejsu ma jedno źródło —
   `Program.cs` → `.WithInterFont()`. Token jest jego **nazwą**, nie drugą kopią.
6. ⭐ **Zapadka licznikowa zamiast listy plików** (§11.1) — z zasadą „wykrywa dryf, nie blokuje
   decyzji" wpisaną **wprost w komunikat błędu testu**, bo czyta go ten, kto zobaczy czerwony test,
   a nie ten, kto akurat czyta ten dokument.
7. ⭐ **Kontrola kolizji kluczy obejmuje CAŁY folder `Themes/`**, nie tylko dwa nowe pliki (decyzja
   użytkownika: to zabezpieczenie fundamentu, nie funkcjonalność M2b). ⚠⚠ **Test musiał rozróżnić
   dwa przypadki, inaczej byłby wręcz błędny:** plik z `ThemeDictionaries` deklaruje każdy klucz
   **dwa razy celowo** — raz dla Dark, raz dla Light (reguła UI #3; `Colors.axaml` to 283 deklaracje
   na 146 kluczy). Reguła „bez duplikatów" obowiązuje więc wyłącznie słowniki bez wariantów, a reguła
   „klucz w jednym pliku" — wszystkie. Napisany bez tego rozróżnienia test zgłosiłby 137 „kolizji"
   w pliku, który jest poprawny, a naturalny odruch — rozluźnić go aż zzielenieje — usunąłby kontrolę,
   o którą chodzi. Zmierzony stan wyjściowy: **0 kolizji** (76 nowych kluczy wobec 247 istniejących).

### §14.3 ⚠ Pomiary, które warto znać w M2b

| Fakt | Konsekwencja |
|---|---|
| Licznik zlicza **deklaracje**, nie wystąpienia słowa | `FontFamily = new FontFamily("…")` to jedna deklaracja, nie dwie. Regex: `\bX\s*=(?!=)` lub `Property="X"` |
| Stan wyjściowy: **609 / 81 / 37** | `FontSize` 49 plików · `FontFamily` 28 · `CornerRadius` 13 |
| `FontFamily` **nie ma uprawnionej reszty** | widok nie ma powodu nazywać rodziny — ta lista ma w M2c zejść do zera, w odróżnieniu od dwóch pozostałych |
| Rekordziści | `DataImportTabView` 86 · `DebuggerTabView` 85 · `PerformancePanelView` 42 — cztery pliki to 41% całości |
| ⚠ Test **nie potrzebuje sesji headless** | czyta `.axaml` jako tekst; biegnie w partycji głównej w 124 ms |
| ⚠ Zasoby `FontWeight` i `FontFamily="$Default"` **kompilują się i ładują** | zweryfikowane buildem *i* startem aplikacji — sama kompilacja XamlIl nie dowodzi, że wartość rozwinie się w runtime |

### §14.4 Czego M2a NIE zrobiło (zgodnie z zakresem)

⛔ Żadnego implicit style kontrolki · żadnej zmiany w `Colors.axaml` · żadnego usuwania wartości
lokalnych · żadnego `ControlTemplate` · **żadnej warstwy klasowej typografii** (§6.4) · żadnego
tokenu bez zmierzonego użycia.

**Pierwszy ruch M2b** wynika wprost z §6.4: pięć istniejących stylów klasowych
(`field-label`, `shortcut-chip`, `title`, `h1`, `group-header`) zaczyna czytać tokeny zamiast
liczb wpisanych na sztywno. To zmiana bajtowo neutralna — i dlatego jest dobrym pierwszym
sprawdzianem, że warstwa skalarna faktycznie działa.
