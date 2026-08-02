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

> **⚠ STATUS: §7.1 (RB‑4), §7.2 (skala Light) i §7.4 (H‑7) SĄ JUŻ DOSTARCZONE** — kroki 2 i 3 M2b
> (`a1d607a`, `7975aaa`). Ta sekcja pozostaje zapisem **projektu i uzasadnienia**; ⭐ **liczby
> wykonawcze — inwentarz konsumentów, ostateczne wartości hex, wynik V‑1 — czytaj z §15.3 i §15.4**,
> bo pomiar wykonawczy skorygował część z nich (m.in. inwentarz §7.1.1: karta Peek Frame debuggera
> jest powierzchnią pływającą, nie chromą, więc podział wyszedł 14/14, a nie 20/12).
> §7.3 i §7.5 nie są jeszcze dostarczone — §7.3 zamknęła decyzja użytkownika (kolor komentarzy
> zostaje, §15.4.3), §7.5 przypisane do **M3.2**.

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

#### §7.1.1 ⚠ Zmierzony zakres RB‑4 — węższy, niż sugeruje słowo „zakładka" (M2b)

**Zakładki DOKUMENTU nie są objęte tym defektem i nie wymagają zmiany.** Pomiar
(`MainWindow.axaml:790`): aktywna zakładka dokumentu maluje się `BackgroundBrush` na `PanelBrush`
nieaktywnych — czyli *„zlewa się z dokumentem pod sobą"*, idiom VS Code, plus 2 px akcentu u góry.
Po przeprojektowaniu skali szarości (§7.2) aktywna stanie się **jaśniejsza** od nieaktywnych
w Light — zadziała poprawnie sama z siebie, bez ingerencji.

Defekt dotyczy **`TabItem.bottom-tab:selected` i `TabItem.sub-tab:selected`** (zakładki panelu
wyników i zakładki pomocnicze edytorów) oraz powierzchni pływających.

**Zmierzone 33 użycia `ElevatedPanelBrush` w 17 plikach; 12 to praca (b):**

| Praca (b) — `SurfaceRaisedBrush` | Praca (a) — `ChromeStrongBrush` |
|---|---|
| `aecc\|CompletionListBox` · `ListBox.code-action-menu` · `ContextMenu` | `DataGridColumnHeader` · pasek tytułu |
| `TabItem.bottom-tab:selected` · `TabItem.sub-tab:selected` | `Border.sidebar-rail:pointerover` |
| `PickerTemplates` · `SearchableComboBox` (popupy) | nagłówki paneli: Performance, Procedure, Function |
| `QuickInfoView` · `ParameterHelper` · `LanguageExpansionController` · `NavigationController` ×3 | Trace · Sessions · Data Import ×3 · AggregationBar · Debugger |

⭐ **Wniosek, który warto zapamiętać poza tym defektem:** podział na dwie prace okazał się
sprawdzalny mechanicznie — „czy ten element pływa nad swoim kontenerem?" ma jednoznaczną
odpowiedź dla każdego z 33 miejsc. To dlatego rozwiązaniem jest **nowy token**, a nie nowa
wartość: gdyby granica była nieostra, żadna wartość by jej nie przecięła.

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

Zmiana `BackgroundBrush` w Light z `#F3F3F3` na **`#FCFCFD`** (§7.2.1) **zmienia tło, na którym
stoi cała paleta składni edytora w motywie jasnym.** Paleta jest zamrożona (§6.3), ale jej
*kontrast względem tła* się przesuwa.

> ⚠ **Poprawka redakcyjna (M2b):** ta sekcja mówiła `#FFFFFF`, bo powstała **przed** decyzją Q7.
> Wartością obowiązującą jest `#FCFCFD`. Nie jest to zmiana ustalenia — jest to usunięcie zdania,
> które unieważniła późniejsza decyzja, a które wysłałoby weryfikację V‑1 na złe tło.

**Wymóg:** przed zamknięciem M2b trzeba przeliczyć kontrast wszystkich czterech barw składni
(`#0F766E` typy, `#795E26` funkcje, `#5D30A6` PSQL, SQL blue) oraz komentarzy (`#2E8B57`)
względem **`#FCFCFD`** i potwierdzić ≥4,5:1. **Jeśli któraś nie przejdzie — zmieniamy tło, nie
paletę**, bo paleta ma za sobą akceptację użytkownika, a tło jej nie ma.

> ### ⛔ WYKONANE W M2b, KROKU 3 — i reguła powyżej okazała się NIEWYKONALNA dla jednego koloru
>
> Przechodzą wszystkie barwy poza **komentarzem `#2E8B57`: 4,14:1**. Ucieczka *„zmień tło"* tu nie
> działa — ten kolor na **czystej bieli** daje **4,25:1**, więc żaden zapas w tle nie sięga progu.
> Reguła milcząco zakładała, że tło ma zawsze margines; jeden kolor pokazał, że nie ma.
> ⚠ Nie jest to regresja: dziś było **3,83:1**, więc zmiana tła sytuację *poprawiła*.
> Pełne wyniki: §15.4.1.
>
> **⭐ ROZSTRZYGNIĘCIE UŻYTKOWNIKA (2026-08-01) — zasada szersza niż ten jeden kolor:**
>
> > *„Nie chciałbym zmieniać koloru komentarzy wyłącznie po to, aby osiągnąć formalny próg
> > kontrastu. Paleta została wcześniej świadomie zaprojektowana i zaakceptowana. Wolałbym
> > najpierw zobaczyć edytor po zakończeniu całego etapu, a dopiero później ocenić praktyczną
> > czytelność podczas normalnej pracy."*
>
> **Kolor komentarzy ZOSTAJE bez zmian.** Temat wraca **po zakończeniu etapu**, oceniany
> w normalnej pracy, a nie liczbą.
>
> ⭐ **Dlaczego to jest spójne z §10, a nie wyjątkiem od niego:** §10 to **próg projektowy**, czyli
> narzędzie do wykrywania rzeczy przeoczonych — a nie nadrzędne kryterium odbioru. Nadrzędne jest
> §0.1.1: *tokeny (i progi) są środkiem, nie celem*. Element **celowo cichy**, który przy pomiarze
> wypada 0,36 poniżej progu, jest dokładnie tym przypadkiem, w którym liczba wymaga potwierdzenia
> okiem, zanim uruchomi zmianę ratyfikowanej palety. ⛔ To **nie** jest licencja na ignorowanie §10
> gdzie indziej: tam, gdzie pomiar wykrył realny problem (H‑7, tekst drugorzędny 3,86:1), zmiana
> weszła bez dyskusji.

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

> ⚠ **Etap: M3.2, nie M2b.** Wiersz M2b w §13 wymienia ogólnie „powierzchnie i kolory (§7)", ale
> ten sam §13 przypisuje semantykę kolorów do **M3.2 (Toolbar)** — i tam jest jej miejsce, bo cała
> ta sekcja opisuje pracę na pasku narzędzi. M2b realizuje z §7 wyłącznie **§7.1 (RB‑4)**,
> **§7.2 (skala szarości Light)** i **§7.4 (kontrast tekstu drugorzędnego)**.

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

---

## §15 As-built — M2b (Compact Controls, powierzchnie, kolory)

> **Status: W TOKU.** Etap prowadzony małymi, zamkniętymi krokami — po każdym build 0/0,
> trzy partycje, smoke, commit. Kolejność i uzasadnienie: patrz plan przyjęty 2026-08-01.

### §15.-1 Tablica stanu M2b (aktualizowana po każdej iteracji)

| # | Krok | Commit | QA użytkownika | Sekcja |
|---|---|---|---|---|
| 0 | style klasowe czytają katalog (bajtowo neutralne) | `0bbc745` | n/d — brak zmiany wizualnej | §15.1 |
| 1 | **`CheckBox`** (RB‑2) | `26243cb` | ✅ zaliczone | §15.2 |
| 2 | **RB‑4** — `ChromeStrong` / `SurfaceRaised` | `a1d607a` | ✅ (ocena odłożona do kroku 3) | §15.3 |
| 3 | skala szarości Light (§7.2) + H‑7 (§7.4) + V‑1 | `7975aaa` | ✅ zaliczone | §15.4 |
| 4 | **`ToolTip`** (M‑2) | `e5b010f` | ✅ zaliczone | §15.5 |
| 5.1 | **`RadioButton`** | `cf23a4c` | ⚠ zgłoszenie → 5.1a | §15.6.1 |
| 5.1a | koncentryczność kropki (odpowiedź na zgłoszenie) | `60f9278` | ✅ zaliczone | §15.6.1a |
| — | **⭐ `FluentBridge` — decyzja architektoniczna** | (w `9ec2c13`) | ✅ ratyfikowana | §15.6.3 · **§16** |
| 5.2 | **`TextBox`** — pierwsza kontrolka na moście | `9ec2c13` | ✅ zaliczone | §15.6.4 |
| 5.3 | **`ComboBox`** — próba skalowania mostu | `3483296` | ✅ zaliczone | §15.6.5 |
| 5.4 | **`Button`** (H‑8) + tokenizacja 4 wariantów | `267a4b8` | ⏳ oczekuje | §15.6.6 |
| 5.5 | **`NumericUpDown`** — trzy kontrolki zagnieżdżone | `d2a2475` | ⏳ oczekuje | §15.6.7 |
| 5.6 | **`ToggleButton`** | `ce47aa7` | ⏳ oczekuje | §15.6.8 |
| 5.7 | **`Expander`** + ⭐ alias zasobu (korekta §16.3) | `69ceff6` | ⏳ oczekuje | §15.6.9 |
| 6 | **`ScrollBar`** (H‑10) | — | ⏳ oczekuje | §15.7 |

**Krok 5 (kontrolki bazowe) — ZAKOŃCZONY.**
**Pozostało w M2b:** DataGrid Standard (§8.4 specyfikacji) — ostatni krok etapu.

⚠ **Stan bieżący suite: 7075** (7000 + 54 + 21). Każda iteracja dokłada 1–2 testy; licznik
w `CLAUDE.md` („Tests") jest aktualizowany w tym samym commicie co iteracja.

### §15.0 ⭐ Zasada nadrzędna M2b (użytkownik, 2026-08-01)

> *„Nie projektujemy możliwie najmniejszych kontrolek. Projektujemy kontrolki, na których
> programista będzie komfortowo pracował przez 8 godzin dziennie."*

**To jest kryterium nadrzędne wobec liczb z §5.** Jeżeli wartość z katalogu jest technicznie
poprawna, ale w praktyce wygląda lub pracuje się na niej gorzej — zatrzymujemy się i zgłaszamy
propozycję (§4.2.4), zamiast dowozić zgodność. ⛔ **Katalog nie ma wygrać z jakością produktu.**

### §15.1 Krok 0 — dowód, że warstwa tokenów działa (bez zmiany wizualnej)

Pięć istniejących stylów klasowych (`field-label`, `shortcut-chip`, `title`, `h1`, `group-header`)
plus `Border.settings-group` czytają teraz katalog zamiast liczb wpisanych na sztywno.
**Bajtowo neutralne** — te same wartości; celem jest dowód, nie zmiana.

⭐ **`Border.settings-group` jest w tym kroku świadomie.** Pięć stylów tekstowych dowodzi wyłącznie,
że rozwijają się `x:Double` i `FontWeight`; warstwa złożona (`Thickness`, `CornerRadius`) to inny
mechanizm — a to ją §3.2 wskazuje jako miejsce, w którym dług odrasta najłatwiej. Dowód z jedną
połową byłby dowodem połowicznym.

**⚠⚠ Rozstrzygnięcie: `DynamicResource`, nie `StaticResource` — i powód NIE jest estetyczny.**
Tokeny nie zależą od motywu, więc rozwiązanie statyczne byłoby technicznie wystarczające.
Decyduje §3.4: przyszłe ustawienie czcionki i skali ma podmieniać tokeny bazowe, a wartość
rozwiązana statycznie nie zareagowałaby na podmianę w czasie działania aplikacji. Zostawiamy
warstwę, która to udźwignie — **nie budując mechanizmu, który by z niej korzystał** (reguła #233).

**⭐⭐ Nowy test `DesignTokenApplicationTests` (headless, 2 przypadki) i powód, dla którego musiał
powstać właśnie tu.** `{DynamicResource}` **nie rzuca wyjątku, gdy klucz nie istnieje** — właściwość
po cichu zostaje na wartości domyślnej. Zweryfikowane zasadzeniem: po podmianie klucza na
nieistniejący **build nadal miał 0 błędów**, a `FontSize` cicho spadło z 11 na 12 (domyślna
Avalonii). Objaw byłby widoczny miesiące później jako „na jednym ekranie tekst ma zły rozmiar".
⚠ Test porównuje wartość z **katalogiem**, nie z literałem — pinuje *że token dociera*, nie *jaką
niesie liczbę* (§4.2.4); literał byłby drugą kopią, czyli tym, co ten etap likwiduje.
⚠ Asercja `NotEqual(12)` jest częścią dowodu: 12 to zarówno domyślna Avalonii, jak i realna wartość
innego tokenu — bez tego „wygląda sensownie" udawałoby sukces.

⚠ Klasa dołącza do `HeadlessCollection` (#94/#226/#286) i **nie konstruuje `MainWindow`** — używa
gołych kontrolek w gołym oknie. Partycja headless ma odtąd **pięć** klas; filtr w `CLAUDE.md`
zaktualizowany.

⭐ **Efekt uboczny, który warto znać:** `DesignTokenComplianceTests` z M2a zaczyna w M2b pełnić
drugą rolę — **czujnika zakresu**. M2b pracuje wyłącznie w `Themes/`, a test mierzy
`Views/`+`Controls/`; drgnięcie któregokolwiek licznika oznacza wejście w zakres M2c.

### §15.2 Krok 1 — `CheckBox` (Release Blocker RB‑2)

Pierwsza zmiana wizualna M2b. Zakres celowo wąski: **wyłącznie `CheckBox`** (decyzja użytkownika —
izolacja ryzyka R‑2); `RadioButton` wchodzi jako pierwsza iteracja kroku kontrolek.

**⭐ Własny `ControlTemplate` był konieczny i to zostało ZMIERZONE, nie założone.** Sonda headless na
Avalonii 12.0.3 wypisała drzewo szablonu Fluenta:

```
Grid #RootGrid
  Border #PART_Border
  Grid (BEZ NAZWY)  Height=32          ← wysokość kolumny boxa
    Border #NormalRectangle  W=20 H=20 ← sam box
    Viewbox → Panel 16×16 → Path #CheckGlyph
  ContentPresenter #PART_ContentPresenter
```

Rozmiary to **wartości lokalne wewnątrz szablonu**, a z trzech elementów wymagających zmiany
**nazwany jest tylko jeden**. Selektor po nienazwanym elemencie (`Grid:not(#RootGrid)`) działałby
dziś, ale aktualizacja Avalonii dodająca jeden `Grid` po cichu zmieniłaby cel — a objawem byłaby zła
wysokość wiersza, nie błąd kompilacji. §5 przewidziało to wprost i miało rację.

**⭐ Znak zaznaczenia pochodzi z własnego systemu ikon** (`Icon.Check`, `Icon.Minus`) renderowanego
przez `SvgIcon` — a nie z kopii geometrii Fluenta. Checkbox przestaje być jedynym miejscem
w aplikacji z obcym rysunkiem, a stan nieokreślony niesie tę samą kreskę co menu.

**⚠⚠ NAJWAŻNIEJSZE USTALENIE KROKU — cel kliknięcia rośnie POZIOMO, nie pionowo, i wykrył to
błąd, który przeszedł przez pierwszy test.** Pierwsza wersja szablonu dawała przezroczysty cel
20×20, żeby 14‑pikselowy znak nie był mikroskopijnym celem przy ośmiogodzinnej pracy. Test to
przepuścił, bo porównywał żądaną wysokość z **wysokością wiersza** (22). Prawdziwym ograniczeniem
jest arytmetyka §5.1: wiersz 22 − `Pad.Cell` (3+3) = **16 px na zawartość**. Cel 20 px podniósłby
wiersz do 26 — czyli **ta „ergonomiczna" poprawka przewróciłaby dokładnie ten Release Blocker,
który krok naprawia.**

⭐ Rozwiązanie zachowuje obie rzeczy: **cel rośnie tam, gdzie to nic nie kosztuje** (poziomo do 20 px,
kolumna jest szersza niż znak) i **nie rośnie tam, gdzie kosztowałoby RB‑2**. Asercja porównuje teraz
z **przestrzenią, jaką zostawia komórka**, a nie z wysokością wiersza — bo poprzednia zgadzała się
z szablonem, który defekt przywracał.

⚠ **Lekcja ogólniejsza:** test napisany na tę samą wielkość, na którą patrzy implementacja, potrafi
potwierdzić błąd zamiast go złapać. Asercja musi mierzyć **ograniczenie**, nie **zamiar**.

**⚠ Kontrolka nie ma `MinHeight` i to jest cała treść RB‑2.** Fluent narzucał 32; kontrolka
zaznaczenia nie ma własnej wysokości do narzucenia — ma zmieścić się w wierszu, w którym stoi.

**⭐ Nowa rola w katalogu: `Margin.MarkGap` = `8,0,0,0`** — odstęp między znakiem kontrolki
a jej etykietą. Pierwsze rozszerzenie „zamkniętej" listy §4.1, wykonane procedurą, którą ta sekcja
sama przewiduje. Nie da się jej złożyć z istniejącej `Margin.InlineGap` (`0,0,8,0`), bo tamta jest
prawostronna: właścicielem odstępu jest tam element po lewej. Tutaj musi nim być **etykieta** —
gdyby odstęp należał do znaku, `CheckBox` bez etykiety (kolumna siatki) niósłby 8 px pustego
marginesu i przestałby być wyśrodkowany w komórce. Ten sam odstęp, inny właściciel, inna rola.
⚠ `RadioButton` skonsumuje ją natychmiast — rola nie powstała „na zapas".

**⚠ Nowy plik `Themes/ControlThemes.axaml`** — `ControlTheme` to **struktura** (szablon + stany),
`ControlStyles.axaml` to **style** (warianty klasowe na gotowych szablonach). Podział nie jest nowym
pomysłem: `SearchableComboBox.axaml` i `PickerTemplates.axaml` są dokładnie takimi słownikami
szablonów. ⚠ Wpięty **po** `IconGeometries.axaml`, bo szablon sięga po `{StaticResource Icon.Check}`,
a `StaticResource` rozwiązuje się przy wczytywaniu.

⭐ **Fakt potwierdzający D1 znaleziony po drodze:** `SearchableComboBox` — jedyna kontrolka, którą
ktoś w tym projekcie świadomie zaprojektował — **ma `MinHeight=24`**. Wysokość standardowa z D1 nie
jest liczbą wymyśloną; jest liczbą, do której projekt już raz doszedł sam.

**Do oceny wizualnej użytkownika:** promień boxa czyta `Radius.Surface` (3). Na kwadracie 14 px to
proporcjonalnie więcej niż 3 na 20 px u Fluenta — jeżeli okaże się zbyt okrągły, właściwą odpowiedzią
jest **nowa rola `Radius.Control`**, a nie zmiana `Radius.Surface`, którą dzielą karty i panele.

#### §15.2.1 QA kroku 1 — zaliczone, plus trzy ustalenia wiążące dalej

**Werdykt użytkownika:** *„Checkbox przestał dominować nad zawartością DataGrid. Cała siatka wygląda
znacznie lżej i bardziej profesjonalnie."* RB‑2 zamknięty.

⭐ **Tło boxa w stanie normalnym ZOSTAJE** — użytkownik miał wątpliwość, czy nie jest zbyt ciemne,
i rozstrzygnął ją po obejrzeniu **wszystkich stanów**: dzięki niemu normal, hover/focus i checked mają
wyraźnie odróżnialne poziomy wizualne. ⚠ To jest argument z języka stanów, nie z pojedynczego widoku —
i dlatego jest mocniejszy niż ocena samego stanu spoczynkowego.

**⭐⭐ ZASADA WIĄŻĄCA DLA KAŻDEJ KOLEJNEJ KONTROLKI M2b (użytkownik, 2026-08-01):**

> **Komponent ocenia się w KOMPLECIE STANÓW — normal · hover · checked/aktywny · indeterminate ·
> disabled · focus — i w OBU MOTYWACH.** Wszystkie mają zachować tę samą spójność.

Konsekwencja praktyczna: kontrolka bez pełnego zestawu stanów nie jest gotowa, nawet jeśli stan
spoczynkowy wygląda dobrze. To jest kryterium odbioru każdego kroku od tej pory.

**⛔ `Radius.Control` NIE POWSTAJE TERAZ — decyzja użytkownika, ważniejsza niż sam promień:**

> *„Nowe role powstają dopiero wtedy, gdy wynikają z rzeczywistego użycia w kilku komponentach,
> a nie z pojedynczego przypadku."*

Pytanie wraca **po wykonaniu pozostałych kontrolek bazowych** — wtedy będzie widać, czy kilka
komponentów potrzebuje własnego promienia, czy obecny podział wystarcza. ⚠ To jest ta sama reguła,
którą zastosowaliśmy do `Stroke.Rail` (§4.2.3) i do `BorderThickness` w M2a: **katalog rośnie
z potrzeby, nie z symetrii.** `Margin.MarkGap` przeszła, bo drugi konsument (`RadioButton`) był
znany w chwili dodania; `Radius.Control` drugiego konsumenta jeszcze nie ma.

**📌 Punkt do ponownej oceny w kroku DataGrid (nie teraz):** nasycenie koloru **zaznaczonego wiersza**.
Użytkownik zgłosił to przy okazji QA checkboxa i wprost odłożył: *„po uspokojeniu całego Design
Systemu może się okazać, że warto delikatnie zmniejszyć jego nasycenie"*. ⚠ Nie ruszamy go wcześniej —
ocena nasycenia ma sens dopiero wtedy, gdy tło, obramowania i wiersze wokół są już docelowe.

### §15.3 Krok 2 — RB‑4: rozdzielenie dwóch ról jednego tokenu

Zmiana **strukturalna** przy niezmienionej skali szarości — żeby RB‑4 dało się ocenić w oderwaniu od
przeprojektowania Light (§7.2), które przychodzi w kroku 3.

| | |
|---|---|
| `ElevatedPanelBrush` → **`ChromeStrongBrush`** | praca (a): chroma o stopień dalej od dokumentu — **14 konsumentów** |
| **`SurfaceRaisedBrush`** (nowy) | praca (b): element unosi się nad kontenerem — **14 konsumentów** |

**Wartości:** Dark `#2D2D2D` dla obu (w ciemnym motywie obie prace zbiegają się — §7.1), Light
`ChromeStrong #D6D6D6` bez zmian i `SurfaceRaised #FFFFFF`. ⚠ **W motywie ciemnym ten krok nie zmienia
niczego wizualnie** — cała zmiana jest w Light i dotyczy wyłącznie powierzchni pływających.

⭐ **Podział okazał się rozstrzygalny mechanicznie.** Pytanie *„czy ten element pływa nad swoim
kontenerem?"* ma jednoznaczną odpowiedź dla każdego z 28 miejsc — i to jest właśnie dowód, że
rozwiązaniem musiał być **nowy token**, a nie nowa wartość: gdyby granica była nieostra, żadna wartość
by jej nie przecięła. Wynik 14/14 nie był planowany.

⚠ **Jedna korekta wobec inwentarza z §7.1.1:** karta Peek Frame debuggera
(`DebuggerTabView.axaml.cs`) była tam zaliczona do chromy — jest **powierzchnią pływającą** i przeszła
do `SurfaceRaisedBrush`. Klasyfikacja z pomiaru wymaga otwarcia każdego miejsca, nie tylko nazwy pliku.

**⭐ Nowy strażnik `NoRetiredTokenName_SurvivesAnywhereInTheApplication`** — przemianowanie tokenu nie
jest błędem kompilacji w ŻADNĄ stronę: XAML rozwiązuje brakujący `{DynamicResource}` do niczego,
a wywołania w C# szukają go **po ciągu znaków** (`Brush("…")`) z `?? fallback`, więc pominięte miejsce
po cichu maluje kolor zastępczy. Lista `RetiredTokens` (stara nazwa → następczyni) jest sprawdzana
w całym `EmberTern.App`.

⚠⚠ **Strażnik pominiętego miejsca musi pomijać KOMENTARZE — inaczej zabrania dokumentowania samego
siebie.** Pierwsza wersja zaświeciła się na komentarzu w `Colors.axaml`, który **wyjaśnia** podział
i z konieczności wymienia starą nazwę. Guard dotyczy **użycia**, nie wzmianki; wersja bez tego
rozróżnienia uczy ludzi kasować wyjaśnienie zamiast kodu. ⚠ Wycinanie komentarzy w C# jest celowo
zachowawcze (tylko całe linie `//` i bloki `/* */`) — „tnij od pierwszego `//`" zjadłoby też koniec
każdej linii z `avares://`, a strażnikowi wolno zgłaszać za dużo, nigdy za mało.

**⚠ Przemianowanie złapał też istniejący test** — `ConnectionExpandBindingProbe.DataImportSurface_
EveryThemeToken_ResolvesInBothPalettes` trzyma listę nazw tokenów używanych przez Data Import.
To jest dokładnie ten rodzaj sprzężenia, dla którego warto mieć taki test: nazwa tokenu jest
kontraktem, a nie szczegółem.

⚠ **Pułapka warsztatowa, którą zapłaciłem:** zasadzenie naruszenia cofnięte przez
`git checkout -- <plik>` **skasowało niezacommitowaną zmianę w tym samym pliku** (przemianowanie
w `MainWindow.axaml`). Plant cofa się z kopii pliku, nie z gita, dopóki praca nie jest w commicie.

### §15.4 Krok 3 — skala szarości Light (§7.2) + kontrast tekstu drugorzędnego (§7.4) + V‑1

Zmiana **wartości** na już poprawnej strukturze z kroku 2. Jeden plik, siedem liczb.

| Token | Dziś | Po | Powód |
|---|---|---|---|
| `BackgroundColor` (Light) | `#F3F3F3` | **`#FCFCFD`** | ratyfikowana Q7 (§7.2.1) |
| `PanelColor` | `#E0E0E0` | **`#F3F4F6`** | chroma odróżnialna, nie dominująca |
| `ChromeStrongColor` | `#D6D6D6` | **`#E8EAED`** | stopień dalej, wciąż spokojnie |
| `BorderColor` | `#BDBDBD` | **`#D8DBE0`** | obramowanie ma oddzielać, nie rysować |
| `ForegroundColor` | `#1F1F1F` | **`#1B1D1F`** | kosmetyka |
| `SubtleForegroundColor` (Light) | `#6E6E6E` | **`#5F6570`** | H‑7 |
| `SubtleForegroundColor` (Dark) | `#858585` | **`#9AA0A6`** | H‑7 — patrz niżej |

**⭐ H‑7 zmierzone, a nie przyjęte za specyfikacją.** Specyfikacja twierdziła, że problem dotyczy
głównie Light. Pomiar tego **nie potwierdził**: Light **3,86:1**, Dark **4,52:1** — obie na granicy
albo poniżej AA, przy `FontSize=11`, dla głównego elementu nawigacyjnego. Po zmianie: Light
**5,33:1**, Dark **6,31:1**. ⭐ To jest kolor nieaktywnych zakładek i podpisów pól, czyli dokładnie
tego, co użytkownik zgłosił przy QA kroku 2 jako *„zbyt słabo widoczne"*.

#### §15.4.1 ⛔ V‑1 — wynik, którego §7.3 nie przewidziała

Przeliczone wszystkie barwy składni wobec nowego tła:

| Element (Light) | na `#F3F3F3` | na `#FCFCFD` | AA 4,5 |
|---|---|---|---|
| **Komentarz `#2E8B57`** | 3,83:1 | **4,14:1** | ⛔ **poniżej** |
| Typy `#0F766E` | 4,93 | 5,34 | ✓ |
| Funkcje `#795E26` | 5,50 | 5,95 | ✓ |
| PSQL `#5D30A6` | 7,76 | 8,40 | ✓ |
| SQL `#0033B3` | 8,91 | 9,64 | ✓ |
| Literały / liczby / operatory | — | 6,34 – 8,24 | ✓ |

**Motyw ciemny przechodzi w całości** (4,69 – 11,80) i ten krok go nie dotyka.

**⛔ §7.3 mówi: „jeśli któraś nie przejdzie — zmieniamy tło, nie paletę". Tutaj jest to
NIEWYKONALNE.** Komentarz `#2E8B57` na **czystej bieli** daje **4,25:1** — czyli nawet maksymalne
rozjaśnienie tła nie sięga progu 4,5. Reguła zakładała, że tło ma zawsze dość zapasu; ten jeden
kolor pokazuje, że nie ma.

⚠ **To nie jest regresja wprowadzona przez ten krok** — dziś jest **gorzej** (3,83) i zmiana tła
sytuację *poprawia* o 0,31. Jest to defekt odziedziczony, który dopiero teraz został zmierzony.

⭐ **Decyzja należy do użytkownika, bo paleta jest zamrożona (§6.3) po dwóch rundach jego QA.**
Rozstrzygnięcie zapisane w §15.4.2.

#### §15.4.2 Punkty do oceny wizualnej kroku 3

1. **RB‑4 — ocena odłożona z kroku 2.** Użytkownik: *„aktywna zakładka wyglądała dobrze już
   wcześniej; problemem są nieaktywne, nadal zbyt słabo widoczne"*. Krok 3 adresuje to przez H‑7
   (5,33:1) i przez nową chromę. **Dopiero teraz można uczciwie ocenić efekt końcowy RB‑4.**
2. **⚠ Powierzchnie uniesione w Light są oddzielone BARDZO subtelnie.** Zmierzone:
   `SurfaceRaised #FFFFFF` vs `Panel #F3F4F6` = **1,10:1**, obramowanie `#D8DBE0` vs biel = **1,42:1**.
   §7.1 zakłada, że krawędź to udźwignie. ⚠ Jeżeli popupy i menu okażą się „pływające w niczym",
   właściwą odpowiedzią jest **mocniejsza krawędź albo cień dla powierzchni uniesionych** — nie
   zmiana tła dokumentu, bo ta jest ratyfikowana (Q7).
3. **`SearchableComboBox`** — użytkownik odnotował przy QA kroku 2 brak widocznej różnicy i słusznie
   złożył to na karb nieprzebudowanej jeszcze kolorystyki. Teraz jest przebudowana.

#### §15.4.3 QA kroku 3 — zaliczone; dwa rozstrzygnięcia

**Werdykt użytkownika:** *„Nowe tło było dobrą decyzją. Interfejs wygląda lżej i bardziej
nowocześnie, a nieaktywne zakładki są wyraźnie czytelniejsze. To jest poprawa, którą rzeczywiście
widać."* ⭐ Zwrotnie potwierdza pomiar H‑7: zgłoszona przy QA kroku 2 słaba czytelność nieaktywnych
zakładek była w liczbach kontrastem **3,86:1**, a nie kwestią gustu.

**① Ocena Application Chrome odłożona do końca M3** *(decyzja użytkownika)* — toolbar, status bar
i pasek zakładek będą jeszcze przebudowywane, więc końcowa ocena ramy ma sens dopiero jako całość.
⭐ To jest dokładnie brama §13.3, potwierdzona teraz niezależnie, z praktyki: użytkownik sam odmówił
zamykania oceny na fragmencie.

**② Kolor komentarzy SQL zostaje** — pełne uzasadnienie i wynikająca z niego zasada w §7.3.
Temat wraca po zakończeniu etapu, oceniany w normalnej pracy.

### §15.5 Krok 4 — `ToolTip` (M‑2)

Kontrolka wybrana wcześnie **na życzenie użytkownika**: zużywa najwięcej nowych tokenów przy
najmniejszym promieniu rażenia. Styl (settery), nie własny szablon — Fluent nie blokuje tu niczego.

Tło `SurfaceRaisedBrush` · krawędź `BorderBrush` + `Border.All` · `Radius.Surface` · `Pad.Panel` ·
`Text.Application`.

⭐ **Pierwszy NOWY konsument `SurfaceRaisedBrush` — i najczystszy przypadek tej roli w aplikacji.**
Podpowiedź nie należy do żadnego kontenera; unosi się nad wszystkim. Ten krok jest więc zarazem
sprawdzianem, czy podział z kroku 2 jest **użyteczny**, a nie tylko poprawny.

**⚠ Opóźnienia zostają na wartościach Avalonii i to jest decyzja, nie pominięcie.** Zmierzone:
`ShowDelay` **400 ms**, `BetweenShowDelay` **100 ms**, `Placement` = Pointer. Audyt (M‑2) zarzucał
*brak standardu*, a nie złą wartość — a 400 ms jest wartością właściwą: krócej oznacza podpowiedzi
wyskakujące, gdy kursor tylko **przejeżdża** przez pasek narzędzi, co przy ośmiogodzinnej pracy jest
hałasem. Drugie 100 ms sprawia, że przejście na sąsiedni przycisk pokazuje podpowiedź natychmiast.
⛔ Nie da się ich zresztą ustawić w tym stylu: to właściwości **dołączone, ustawiane na WŁAŚCICIELU**
podpowiedzi, więc `Selector="ToolTip"` ich nie widzi. Zmiana wymagałaby settera na selektorze
uniwersalnym — koszt nieproporcjonalny do wartości, która i tak jest dobra.

⚠ **Rozmiar celowo nietknięty.** Zawartość podpowiedzi to `TextBlock` bez zawijania, więc samo
`MaxWidth` **przycięłoby** długi tekst zamiast go zawinąć. Długie podpowiedzi to osobny, mierzalny
temat — nie efekt uboczny zmiany wyglądu.

**⚠⚠ TEST MUSIAŁ ZOSTAĆ NAPISANY W MOTYWIE JASNYM — i to jest ogólniejsza lekcja o RB‑4.**
W motywie ciemnym `SurfaceRaised` i `ChromeStrong` mają **celowo tę samą wartość** (§7.1), więc
asercja „bierze uniesienie, nie chromę" w Dark przechodzi **niezależnie od tego, czy styl jest
podpięty poprawnie**. Jedynym motywem, w którym ten test może zawieść, jest ten, w którym różnica
istnieje. Wariant jest przywracany w `finally`, bo sesja headless jest wspólna dla całej kolekcji.

⚠ **Drugi pomiar warty zapamiętania: `TryFindResource(key, out …)` NIE WIDZI zasobów z
`ThemeDictionaries`** — zgłasza klucz jako nieistniejący. To jest dokładnie granica między dwoma
słownikami dodanymi w M2a (`Tokens`/`Typography` — jedna wartość, bez wariantu) a `Colors.axaml`
(jedna wartość na motyw). Dwa rodzaje zasobu, dwie ścieżki wyszukiwania; testy mają teraz obie.

### §15.6 Krok 5 — kontrolki bazowe, iteracja po iteracji

Na życzenie użytkownika krok 5 nie jest jedną zmianą, tylko **serią zamkniętych iteracji**: po każdej
większej kontrolce aplikacja jest uruchamiana i oceniana, zanim ruszy następna.

**Pomiar wyjściowy wszystkich kontrolek bazowych** (sonda headless, Avalonia 12.0.3):

| Kontrolka | `MinHeight` | żądana wys. | `Padding` | `FontSize` |
|---|---|---|---|---|
| `RadioButton` | **0** | **32** | `8,0,0,0` | 14 |
| `TextBox` | 32 | 32 | `10,6,6,5` | 14 |
| `ComboBox` | 32 | 32 | `12,5,0,7` | 14 |
| `NumericUpDown` | 32 | 32 | `10,6,6,5` | 14 |
| `Button` | 0 | 29 | `8,5,8,6` | 14 |

⚠ **Dwie rzeczy warte uwagi przed dalszymi iteracjami:** `FontSize` **14** w każdej kontrolce (przy
roli `Text.Application` = 12, więc tekst w polach się zmniejszy — i to jest część efektu „to nie
wygląda już na aplikację Avalonia"), oraz **niesymetryczne paddingi** (`10,6,6,5`, `12,5,0,7`), które
są jednym z powodów, dla których pola nie stoją dziś w jednej linii.

#### §15.6.1 Iteracja 1 — `RadioButton`

⚠ **Ten sam defekt co RB‑2, o jedną kontrolkę dalej — i lepiej zamaskowany.** `MinHeight` wynosi tu
**0**, więc kontrolka wygląda na niewinną; żądana wysokość i tak jest **32 px**, bo wymusza ją
nienazwany element szablonu. Gdyby oceniać po samej właściwości, defektu by tu nie znaleziono.

⭐ **Odejście od Fluenta w jednym miejscu, świadome:** stan zaznaczony to **wypełnienie akcentem
z jasną kropką**, dokładnie jak w `CheckBoxie`, a nie pierścień z kropką w kolorze akcentu.
**Spójność wewnątrz własnego systemu jest ważniejsza niż zgodność z konwencją frameworka** —
zaznaczony przełącznik ma czytać się tak samo niezależnie od tego, czy jest kwadratem czy kółkiem.
To jest ta sama zasada, którą użytkownik sformułował przy QA kroku 1 (komplet stanów, jedna
spójność), zastosowana o poziom wyżej: do rodziny kontrolek.

⚠ **Bez `Radius`:** `Ellipse` jest okręgiem z natury. `Border` + `CornerRadius` wymagałby połowy
rozmiaru znaku — czyli tokenu udającego działanie arytmetyczne.

⭐ **Proporcja kropki przejęta z pomiaru, nie wymyślona:** Fluent ma znak 20 i kropkę 8, czyli **0,4**;
przy znaku 14 daje to 6. Liczba stoi w szablonie, a nie w katalogu, bo jest wewnętrzną proporcją
**rysunku** jednej kontrolki — tym samym rodzajem szczegółu co grubość kreski w geometrii ikony.
Rola powstanie, gdy zażąda jej druga kontrolka (reguła użytkownika z QA kroku 1).

⭐ Pierwszy konsument **`Stroke.Hairline`** (grubość pierścienia to `double`, nie `Thickness`) i drugi
konsument **`Margin.MarkGap`** — roli dodanej w kroku 1 właśnie z myślą o tej kontrolce, więc katalog
nie urósł „na zapas".

##### §15.6.1a Zgłoszenie użytkownika — kropka `RadioButtona` nie wygląda na wyśrodkowaną

Odpowiedzią miał być **pomiar, nie opinia** — i tak został potraktowany.

**Zmierzone trzema niezależnymi metodami** (lokalne `Bounds`, punkty przeliczone do przestrzeni
kontrolki, `RenderedGeometry`): pierścień `3,0,14,14` środek `(10,000; 7,000)`, kropka `7,4,6,6`
środek `(10,000; 7,000)` → **`dx = dy = 0,000`. Geometria jest dokładnie koncentryczna.**

⚠ **Czego NIE udało się zweryfikować i nie wolno tego zaraportować jako sprawdzone:** poziomu
pikseli przy skalowaniu ≠ 100%. Dwie próby zawiodły i obie warto znać:
- **`LayoutTransformControl` NIE symuluje DPI** — skaluje po ułożeniu dziecka, więc `Bounds` wyszły
  identyczne na 1,0 / 1,25 / 1,5 / 1,75 / 2,0. Test, który „przechodzi" na każdej skali, bo nie
  dotyka mechanizmu, którego dotyczy;
- **zrzut pikseli jest niedostępny** — sesja headless używa `UseHeadlessDrawing`, więc
  `CaptureRenderedFrame()` zwraca `null`. Test „powiększeniem" wymagałby renderera Skia.

⭐ **Mechanizm, który mógłby to tłumaczyć, JEST realny — więc został usunięty, zamiast czekać na
dowód.** Zaokrąglanie układu przyciąga **każdy element osobno** do piksela urządzenia: przy 150%
pierścień wypada na 21 px w pozycji 4,5, kropka na 9 px w pozycji 10,5, a niezależne zaokrąglenie
potrafi rozjechać środki o 1 px. `PART_MarkArea` `RadioButtona` ma więc `UseLayoutRounding="False"`.
⭐ **`CheckBox` go NIE dostaje i to jest istota decyzji:** przyciąganie do pikseli pomaga **krawędzi
prostej**, a okręgowi — który i tak jest wygładzany na całym obwodzie — nie ma czego pomóc. Wymiana
kosztu, którego tam nie ma, na ryzyko, które tam jest.

Koncentryczność jest teraz **zapięta testem** — dwie niezależnie wyrównywane figury są współśrodkowe
tylko dopóki nikt nie doda jednej z nich marginesu, innego wyrównania albo nieparzystego rozmiaru.

**QA po poprawce: zaliczone** — użytkownik potwierdził, że kropka wygląda poprawnie. ⭐ **Warto
zapamiętać kształt tego zgłoszenia, bo będzie wracać:** pomiar mówił „geometria jest dokładnie
koncentryczna", a mimo to zgłoszenie było zasadne — bo dotyczyło **rasteryzacji**, warstwy, której
żaden z trzech pomiarów nie dotyka. ⛔ Odpowiedź „zmierzyłem, jest dobrze" byłaby tu formalnie
prawdziwa i praktycznie bezużyteczna. Właściwa kolejność to: **zmierz → nazwij, czego pomiar NIE
obejmuje → jeżeli w tej luce istnieje realny mechanizm, usuń go zamiast czekać na dowód.**

##### §15.6.2 ⛔ Iteracja 2 (`TextBox`) — WSTRZYMANA: pomiar zmienia podejście do całej reszty kroku 5

Sonda szablonu `TextBoxa` pokazała, że tło i krawędź maluje `PART_BorderElement`, a nie sama
kontrolka — czyli settery `Background`/`BorderBrush` na `TextBox` nie mają dokąd trafić. **Ale
kolejna sonda pokazała coś ważniejszego: Fluent wystawia dokładnie te pokrętła, których potrzebujemy,
jako ZASOBY** (zmierzone wartości w motywie ciemnym):

| Klucz Fluenta | Wartość | Odpowiednik w katalogu |
|---|---|---|
| `TextControlThemeMinHeight` | **32** | `Size.Control` (24) |
| `TextControlThemePadding` | **10,6,6,5** | `Pad.Control` (8,0) |
| `TextControlBorderThemeThickness` | `1,1,1,1` | `Border.All` |
| **`ControlCornerRadius`** | `3,3,3,3` | `Radius.Surface` — ⭐ **wspólny dla wielu kontrolek** |
| `TextControlBackground` / `…PointerOver` / `…Focused` | `#66000000` … | `BackgroundBrush` itd. |
| `TextControlBorderBrush` / `…PointerOver` / `…Focused` | `#99ffffff` … | `BorderBrush`, `AccentMutedBrush`, `FocusBorderBrush` |
| `TextControlPlaceholderForeground` | `#99ffffff` | `SubtleForegroundBrush` |
| `ComboBoxBackground`, `ComboBoxBorderBrush` | | |
| `ButtonBackground`, `ButtonBorderBrush`, `ButtonForeground`, `ButtonPadding` | `8,5,8,6` | `Pad.Button` |

**⭐⭐ To otwiera podejście, którego plan nie zakładał: nie przestylowujemy Fluenta — PRZEPINAMY GO
NA NASZ KATALOG.** Zamiast pisać własne szablony dla `TextBox`/`ComboBox`/`Button`, nadpisujemy
klucze, z których Fluent i tak czyta. **Projekt ma na to własny precedens** — tak właśnie rozwiązano
kolory zaznaczenia `TreeViewItem*` i `DataGridCell*` (reguła UI #6 w `CLAUDE.md`).

Propozycja przedstawiona użytkownikowi przed implementacją — rozstrzygnięcie w §15.6.3.

##### §15.6.3 ⭐ RATYFIKOWANE — `FluentBridge.axaml`: przepinamy Fluenta na katalog, zamiast go kopiować

> **⭐ Ta sekcja jest zapisem, JAK doszliśmy do decyzji (pomiar, który ją wymusił, i moment jej
> podjęcia). Kanoniczną definicją wzorca — czym JEST i jak się go stosuje — jest §16.**
> Przy rozbieżności rozstrzyga §16; ta sekcja się nie aktualizuje, bo jest datowana.

> **Decyzja użytkownika, 2026-08-02:** *„Wykorzystujemy mechanizmy Avalonia zamiast je kopiować,
> zachowujemy wszystkie sprawdzone zachowania Fluent, a jednocześnie cała aplikacja pozostaje
> sterowana naszym systemem tokenów."*

**⛔ REGUŁA WIĄŻĄCA — Bridge NIE MOŻE stać się drugim katalogiem tokenów** *(użytkownik)*:

> *„Ma być wyłącznie warstwą mapującą zasoby Fluent na `Tokens.axaml`, `Typography.axaml`
> i `Colors.axaml`. Nie powinny pojawiać się tam lokalne wartości ani nowe decyzje projektowe.
> Wszystkie liczby i role pozostają właścicielami odpowiednich katalogów, a Bridge jedynie
> je tłumaczy."*

###### Ograniczenie XAML‑a, które wyznacza kształt Bridge'a — zmierzone

⚠⚠ **XAML nie potrafi ZAALIASOWAĆ zasobu skalarnego.** `<x:Double x:Key="TextControlThemeMinHeight">`
musi zawierać liczbę; nie da się w tym miejscu napisać „to samo, co `Size.Control`". To samo dotyczy
`Thickness` i `CornerRadius`. Gdyby więc Bridge przejął metryki, **musiałby wpisać liczby — czyli
złamać regułę użytkownika w pierwszej linijce.**

⭐ **Pędzle są wyjątkiem i to one wyznaczają podział:** `<SolidColorBrush Color="{StaticResource
BackgroundColor}" />` jest **odwołaniem**, nie wartością. Stąd kształt warstwy:

| Co | Gdzie | Dlaczego |
|---|---|---|
| **Metryki** — `MinHeight`, `Padding`, `FontSize`, `BorderThickness` | **styl** w `ControlStyles.axaml`, setter czytający token przez `{DynamicResource}` | styl aplikacji ma wyższy priorytet niż `ControlTheme`, a setter **potrafi** odwołać się do tokenu |
| **Kolory malowane przez wnętrze szablonu** (`PART_BorderElement`) | **`FluentBridge.axaml`**, jako `SolidColorBrush Color="{StaticResource …Color}"` | setter na kontrolce tam nie dociera; odwołanie do koloru nie jest wartością lokalną |
| **Wartości, w których Fluent już się z nami zgadza** (`ControlCornerRadius` = 3 = `Radius.Surface`) | **nigdzie** — pinowane testem | wpis powielałby liczbę; test zamienia zbieżność w sprawdzany niezmiennik |

⭐ **To jest lepszy podział niż „Bridge przejmuje wszystko":** Bridge zostaje **wyłącznie warstwą
kolorów**, czyli dokładnie tym, czego nie da się zrobić inaczej — a reguła „bez lokalnych wartości"
staje się **strukturalna, nie pamięciowa**, bo w tym pliku nie ma gdzie wpisać liczby.

###### Egzekwowanie

Reguła użytkownika jest zapięta testem (`FluentBridge_ContainsNoLocalValues`): **każdy wpis
w `FluentBridge.axaml` musi odwoływać się do zasobu** (`{StaticResource …}` / `{DynamicResource …}`).
Wartość wpisana wprost wywala test. ⛔ Bez tego reguła przetrwałaby dokładnie do pierwszego
„tu jest szybciej wpisać kolor".

###### Czego to NIE cofa

⚠ `CheckBox` i `RadioButton` **zostają na własnych szablonach** i nie jest to niekonsekwencja:
u nich rozmiar znaku jest zakodowany **wewnątrz szablonu**, a nie wystawiony jako zasób — czego
dowiódł pomiar w krokach 1 i 5.1. Te dwie kontrolki były **wyjątkiem, nie regułą**, i dopiero sonda
`TextBoxa` to pokazała.

##### §15.6.4 Iteracja 2 — `TextBox` (pierwsza kontrolka na moście)

Metryki przez styl (`Size.Control` 24 · `Pad.Control` · `Text.Application` · `Border.All` ·
`VerticalContentAlignment=Center`), kolory przez `FluentBridge` (tło, krawędź w trzech stanach,
tekst, watermark, zaznaczenie). Wysokość spada **32 → 24**, tekst **14 → 12**.

⚠ **`VerticalContentAlignment=Center` jest konieczne, nie kosmetyczne.** `Pad.Control` ma pion
**zerowy**, bo wysokość ma dawać `Size.Control` — jedna wielkość, jeden właściciel (inaczej padding
i wysokość walczą, a wygrywa większy). Bez wyśrodkowania tekst osiadłby przy górnej krawędzi.

**⚠⚠ EDYTOR W KOMÓRCE SIATKI DOSTAŁ WŁASNĄ REGUŁĘ — i bez niej krok 5.2 zepsułby krok 1.**
`Size.Row.Grid` (22) minus `Pad.Cell` (3+3) zostawia **16 px**. `TextBox` o `MinHeight` 24
podnosiłby **każdy edytowany wiersz o 8 px**, czyli wprowadzałby skok układu przy samym wejściu
w edycję (§13.3 specyfikacji — Zero Layout Shift). §5 przypisuje edytorom w komórce wysokość
**wiersza siatki**, nie kontrolki, więc `DataGridCell TextBox` ma `MinHeight=0`, `Pad.CellCompact`
i rolę `Text.Grid`. ⭐ To jest ten sam kształt błędu, który złapałem w kroku 1 przy celu kliknięcia:
**wartość poprawna dla formularza bywa destrukcyjna dla siatki, a obie kontrolki są tą samą klasą.**

**⭐ Most zweryfikowany pomiarem w obie strony** (`TextBox_TakesItsMetricsFromTheCatalog_AndItsColours
ThroughTheBridge`) — bo obie połowy jadą **innymi trasami i żadna nie dowodzi drugiej**:
metryki docierają setterem stylu, który bije `ControlTheme` Fluenta (stąd `MinHeight` 24 mimo
`TextControlThemeMinHeight` = 32), a kolory **nie mogą** — maluje je `PART_BorderElement` z zasobów
Fluenta, więc jadą Bridge'em. Asercja czyta pędzel **z elementu, który faktycznie maluje**; setter na
`TextBoxie` malowałby po cichu nic.

⚠ **Dwa moje testy najpierw zgłosiły fałszywy alarm i warto wiedzieć dlaczego:**
`<ResourceDictionary x:Key="Dark">` nazywa **zakres motywu**, a nie zasób — a oba strażniki liczyły
go jako klucz („Dark zadeklarowany w dwóch plikach", „Dark nie ma wartości"). Prawda o wyrażeniu
regularnym, bzdura o kodzie. **Ciąg w kształcie klucza nie jest automatycznie kluczem.**

##### §15.6.4a QA iteracji 5.2 — zaliczone; ⭐ pierwsze wystąpienie zjawiska, które będzie wracać

**Werdykt użytkownika:** *„Obawiałem się, że wysokość 24 px okaże się zbyt mała do codziennej pracy,
ale na tym etapie wygląda naturalnie i nie sprawia wrażenia ciasnej. Pasek filtra oraz pola tekstowe
wyglądają nowocześniej i bardziej przypominają komercyjne IDE niż standardowe kontrolki Avalonia."*
⭐ D1 (24 px) potwierdzone w praktyce, nie tylko w katalogu.

**📌 Do ponownej oceny w M3.2 (Toolbar), NIE teraz: badge DEV MODE.** Użytkownik: *„po uspokojeniu
wyglądu kontrolek jeszcze bardziej rzuca się w oczy"*.

⭐⭐ **To jest pierwszy przypadek zjawiska, które w tym etapie będzie się powtarzać i warto je nazwać:
uspokojenie otoczenia PODNOSI głośność wszystkiego, czego jeszcze nie dotknęliśmy.** Badge nie zmienił
się ani o piksel — zmieniło się tło, na którym stoi. Konsekwencje praktyczne:

- **element zgłoszony jako „za głośny" po kolejnym kroku nie musi być defektem tego kroku** — bywa
  długiem, który dopiero stał się widoczny;
- ⛔ **nie wolno reagować na to od razu**, bo poprawka wykonana na wpół uspokojonym otoczeniu jest
  strojeniem do stanu przejściowego. Stąd konsekwentne odkładanie takich zgłoszeń (kolor komentarzy
  §7.3, nasycenie zaznaczonego wiersza §15.2.1, teraz DEV MODE) — to nie jest odsuwanie pracy, tylko
  jedyny moment, w którym da się ją ocenić uczciwie;
- ⭐ **to jest też argument ZA bramą §13.3**: ocena Application Chrome jako całości ma sens dopiero
  wtedy, gdy nic w kadrze nie jest już w stanie przejściowym.

##### §15.6.5 Iteracja 3 — `ComboBox` (most zdał próbę skalowania)

Metryki przez styl (`Size.Control` · `Pad.Control` · `Text.Application` · `Border.All`), kolory przez
Bridge (16 kluczy w obu motywach).

⭐ **Ta iteracja była właściwym testem architektury z §15.6.3 — i most zdał.** `ComboBox` ma
znacznie więcej części szablonu niż `TextBox` (`Background`, `HighlightBackground`, `DropDownOverlay`,
`DropDownGlyph`, `PART_Popup`, `PART_EditableTextBox`) i **nie wymagał ani jednego własnego szablonu**.
Przy pierwotnym planie („własne szablony dla kontrolek bazowych") byłby to najdroższy element kroku 5.

⭐ **`ComboBoxDropDownBackground` → `SurfaceRaisedColor`** — lista rozwijana jest powierzchnią
uniesioną, więc trafia na token z RB‑4 samoistnie. W motywie jasnym to właśnie ta różnica sprawia,
że rozwinięta lista czyta się jako pływająca nad formularzem, a nie jako jego wgłębiona część.
To trzeci naturalny konsument tej roli (po zakładkach panelu i `ToolTipie`) — podział z kroku 2
zaczyna się sam obsługiwać.

⚠ **Tło pola nie zmienia się przy najechaniu ani wciśnięciu — sygnał niesie krawędź.** Fluent zmieniał
tło (`#66000000` → `#99000000`); zmiana tła czyta się jak zmiana stanu **danych**, a nie wskazania
kursorem, i w siatce ustawień dawała efekt „to pole jest jakieś inne".

⚠ **`ComboBoxItem` dostaje `Size.Row.Menu` (22) i `Pad.MenuItem`, a nie wysokość kontrolki.**
Pozycja listy rozwijanej jest wierszem MENU, nie polem formularza: czyta się ją w pionowej serii,
wybiera jednym kliknięciem i nigdy nie edytuje. Rola dzielona z menu kontekstowym jest tu poprawna
(§3.3), a nie oszczędnością na tokenie.

##### §15.6.5a QA iteracji 5.3 — zaliczone

**Werdykt użytkownika: zaakceptowane.** Iteracja zamknięta bez zgłoszeń.

⭐ **Co ta akceptacja właściwie potwierdza — i to jest ważniejsze niż sam `ComboBox`:** była to
pierwsza kontrolka, na której most z §16 mógł się złamać, bo ma **sześć części szablonu** malowanych
z zasobów Fluenta zamiast jednej. Nie złamał się i **nie powstał ani jeden własny szablon.** Od tego
momentu „przepinamy Fluenta zamiast go kopiować" nie jest hipotezą z jednego przypadku (`TextBox`),
tylko wzorcem sprawdzonym na kontrolce złożonej — dlatego dopiero teraz został podniesiony do rangi
wzorca projektowego (§16).

⭐ **Trzeci naturalny konsument `SurfaceRaised` bez planowania.** Lista rozwijana trafiła na token
z RB‑4 dlatego, że *jest* powierzchnią pływającą — nie dlatego, że ktoś ją tam przypisał. Po
zakładkach panelu i `ToolTipie` podział z kroku 2 zaczyna się **obsługiwać sam**, co jest praktycznym
potwierdzeniem, że rozwiązaniem RB‑4 musiał być nowy token, a nie nowa wartość (§15.3).

##### §15.6.6 Iteracja 4 — `Button` (H‑8) + tokenizacja czterech wariantów

Metryki przez styl (`Size.Control` · `Pad.Button` · `Text.Application` · `Border.All` ·
`Radius.Surface`), kolory przez Bridge (12 kluczy w obu motywach).

**⭐ Pierwsza kontrolka M2b, która MIAŁA JUŻ ZAPROJEKTOWANĄ RODZINĘ — i to zmienia rodzaj ryzyka.**
Przy `TextBoxie` i `ComboBoxie` pytanie brzmiało „czy styl dociera". Tutaj brzmi **„czy dociera, nie
spłaszczając wariantu, który celowo się różni"**: `Button.icon`, `.flat`, `.primary` i `.caption` to
cztery świadome decyzje, a audyt H‑8 zmierzył **25 przycisków bez `Classes`** — czyli takich, które
nie należą do żadnej z nich i stały na gołym Fluencie.

⚠ **Kolejność w pliku jest znacząca i to jest jedyna nowa pułapka tego kroku.** W Avalonii przy tej
samej trafności wygrywa setter zadeklarowany **później**, więc styl bazowy `Button` musi stać **przed**
wariantami. Blok bazowy siedzi w sekcji kontrolek (obok `TextBoxa` i `ComboBoxa`), warianty zostają
tam, gdzie były. ⛔ Przeniesienie bazy poniżej `Button.icon` po cichu unieważniłoby cztery warianty —
build zielony, wygląd zrównany do jednego. Zapięte asercją `primary.MinHeight > plain.MinHeight`.

**⭐ Trzy role wysokości dla jednej kontrolki i to nie jest niekonsekwencja.** `Size.Control` (24) jest
**minimum**, `Button.primary` podnosi się do `Size.ControlPrimary` (28), `Button.caption` bierze
`Size.TitleBar` (36). To pierwszy i jedyny konsument `Size.ControlPrimary` — rola istnieje właśnie po
to, żeby hierarchia akcji miała sygnał działający również tam, gdzie kolor niesie już inne znaczenie.

**⚠ Fluent malował te przyciski półprzezroczystą bielą, a stan najechania — CZYSTĄ BIELĄ**
(`#33ffffff` → `White`). Nowa drabina jest **monotoniczna w obu motywach**: `Panel` → `ChromeStrong`
to w Dark rozjaśnienie (`#252526` → `#2D2D2D`), w Light przyciemnienie (`#F3F4F6` → `#E8EAED`).
Kierunek przeciwny, znaczenie to samo — „o stopień bliżej kursora". To ta sama zasada, którą RB‑4
wydobyło z `ElevatedPanelBrush`.

**⚠ Wciśnięcie niesie KRAWĘDŹ, nie trzeci odcień szarości — i to jest decyzja, nie oszczędność.**
Trzeci stopień szarości musiałby sięgnąć po `BorderColor` jako **wypełnienie**, czyli użyć tokenu
wbrew jego roli (§3.1); rola „krawędź" przestałaby wtedy cokolwiek znaczyć. Akcent na krawędzi jest
czytelniejszy i **jest już językiem tej aplikacji** — krok 5.3 ustalił go dla `ComboBoxa`.
⚠ Stan `:disabled` nie zmienia tła; przygaszony jest **tekst**. Zmiana tła czytałaby się jak *inny
rodzaj* przycisku, a nie jak przycisk chwilowo niedostępny.

⚠ **Przycisk w komórce siatki dostaje tę samą regułę co `TextBox`** (§15.6.4): `MinHeight=0`,
`Pad.CellCompact`, `Text.Grid`. Zmierzone **182 kolumny szablonowe** w widokach — to nie jest przypadek
teoretyczny, tylko trzecie wystąpienie tego samego kształtu błędu w tym kroku 5.

**⭐ Cztery warianty zostały przy okazji przepięte na katalog** (`Pad.ButtonIcon`, `Pad.Button`,
`Radius.Chip`, `Radius.Surface`, `Border.All`, `Size.TitleBar`) — dotąd nosiły liczby wpisane wprost.
⚠ `Button.icon` **nadal znosi tło i krawędź** podstawy: ikona w pasku narzędzi ma być samą ikoną,
a afordancję niesie dopiero najechanie. To jedyny wariant, w którym przycisk nie wygląda jak przycisk
w spoczynku — i po tym kroku jest to widoczne jako **wariant chromeless wobec własnej podstawy**,
a nie jako „przycisk zaprojektowany wobec przycisku Fluenta".
⚠ Szerokość 46 px w `Button.caption` **zostaje lokalna** — to konwencja przycisków okna Windows,
a nie rola w katalogu (§4.2.4: rola powstaje z drugiego konsumenta).

Build 0/0; suite **7076** (7000 + 54 + 22); smoke czysty.

##### §15.6.7 Iteracja 5 — `NumericUpDown`

Metryki przez styl, kolory strzałek przez Bridge (11 kluczy `RepeatButton*` w obu motywach).

**⚠⚠ TO NIE JEST JEDNA KONTROLKA, TYLKO TRZY ZAGNIEŻDŻONE — i to jest inny kształt problemu niż
w krokach 5.2–5.4.** `NumericUpDown` opakowuje `ButtonSpinner` (`PART_Spinner`), a ten opakowuje
`TextBox` (`PART_TextBox`) i dwa `RepeatButton`y. **Wysokość 32 wymusza ŚRODKOWA z nich**, więc setter
na `NumericUpDown` sam z siebie nie zmieniłby nic mierzalnego. `ButtonSpinner` dostaje własny implicit
style, a nie selektor `/template/`, bo jest osobnym **typem** kontrolki, nie częścią szablonu.

**⭐ NAJTAŃSZY MOŻLIWY DOWÓD, ŻE MOST DZIAŁA KOMPOZYCYJNIE: wewnętrzny `TextBox` wziął `Size.Control`
już w kroku 5.2, bez ani jednej zmiany w tej iteracji.** Warstwa sięgnęła w głąb szablonu, na który
nikt jej nie kierował — bo styl typu obowiązuje wszędzie, gdzie ten typ wystąpi, także wewnątrz
cudzego szablonu. To jest praktyczna różnica między *przepięciem* frameworka a *skopiowaniem* go:
kopia szablonu `NumericUpDown` musiałaby powtórzyć decyzje `TextBoxa`.

**⚠ Asercja mierzy `DesiredSize`, nie `MinHeight` — i to jest istota tego kroku.** Właściwość można
ustawić poprawnie na kontrolce zewnętrznej, podczas gdy wewnętrzna nadal wymusza starą wysokość;
`MinHeight` pokazywałby wtedy 24, a kontrolka miałaby 32. Ten sam kształt co kłamiące `MinHeight=0`
`RadioButtona` (§15.6.1), o poziom zagnieżdżenia dalej.

**⚠ Strzałki spinnera miały `MinWidth` 34 px — kolumnę SZERSZĄ niż wysokość całej kontrolki po
zmianie (24).** Przy 15 z 18 użyć spinner jest widoczny, więc to nie jest szczegół. Obie są
**nazwanymi** częściami szablonu, więc selektor `/template/` trafia w nie jednoznacznie i §16.4
nie wchodzi w grę — to nie jest własny szablon, tylko setter na nazwanej części (istniejący precedens:
`TextBox.frameless /template/ Border#PART_BorderElement`).

⭐ **Strzałka jest afordancją drugorzędną**, więc w spoczynku nie ma tła i dopiero najechanie ją
wydobywa — ten sam podział, który `Button.icon` realizuje w pasku narzędzi. Kolor tekstu idzie
z `SubtleForeground` na `Foreground` przy najechaniu.

Build 0/0; suite **7077** (7000 + 54 + 23); smoke czysty.

##### §15.6.8 Iteracja 6 — `ToggleButton`

Metryki identyczne z `Button`, kolory stanów neutralnych przez Bridge (12 kluczy w obu motywach).

**⚠ Metryki są powtórzone i to NIE jest niedbałość.** `ToggleButton` **dziedziczy po `Button`**, ale
**selektor typu w Avalonii dopasowuje typ DOKŁADNY** — dlatego w tym samym pliku stoi od dawna
komentarz przy `:is(TextBlock)` dla `SelectableTextBlock`. Styl `Button` z kroku 5.4 go nie widzi.
⛔ **Alternatywa `:is(Button)` byłaby gorsza, nie krótsza:** złapałaby też `RepeatButton` strzałek
spinnera (krok 5.5) i przyciski `ScrollBara`, którym wysokość kontrolki **formularza** jest wprost
szkodliwa — strzałka paska przewijania ma 16 px i ma taka zostać. Dokładność selektora jest tu
zabezpieczeniem, nie ograniczeniem.

**⭐ STAN ZAZNACZONY BYŁ JUŻ POPRAWNY I TO NIE PRZYPADEK — ani nie luka w Bridge'u.**
`SystemAccentColor` jest w tej aplikacji nadpisany naszym akcentem (`Colors.axaml`, wraz z całą
sześciostopniową rampą), więc `ToggleButtonBackgroundChecked` Fluenta rozwiązuje się do `#2D6BBF`
**sam z siebie**. Wpis w Bridge'u powielałby wartość, którą już kontrolujemy — dokładnie ten sam
powód, dla którego nie ma tam `ControlCornerRadius` (§16.3, wiersz trzeci). ⭐ **Zbieżność jest
pinowana testem**, czyli zamieniona w sprawdzany niezmiennik zamiast w komentarz.

Wadliwe były więc wyłącznie **stany neutralne** — ta sama pół-przezroczysta biel co w `Button`,
z czystą bielą przy najechaniu.

⚠ `ToggleButton.icon` (pasek narzędzi, m.in. przełącznik panelu filtra) zachowuje swój chromeless
wygląd i własny stan `:checked` na `SelectionBrush` — wariant dopowiada podstawę, tak jak
`Button.icon` po kroku 5.4.

Build 0/0; suite **7078** (7000 + 54 + 24); smoke czysty.

##### §15.6.9 Iteracja 7 — `Expander` (najbardziej pouczająca kontrolka M2b)

Metryki przez **alias zasobu** (nowa trasa, niżej), kolory przez Bridge (6 kluczy w obu motywach),
plus naprawa regresji, którą wprowadził krok 5.6.

**⚠⚠ KROK 5.6 ZEPSUŁ NAGŁÓWEK `EXPANDERA` I ZOSTAŁO TO ZMIERZONE, NIE PRZEWIDZIANE.** Nagłówek
`Expandera` **JEST `ToggleButtonem`**, więc styl typu z kroku 5.6 sięgnął do środka szablonu Fluenta
i ustawił mu `HorizontalContentAlignment=Center` — nagłówek sekcji wyśrodkowany jak etykieta
przycisku. Sonda: `Pad=12,0,12,0`, `HCA=Center`, `FS=12`.

⭐ **Lekcja ogólniejsza, bo ma dwa znaki:** styl typu obowiązuje **także wewnątrz cudzego szablonu**.
Ta sama właściwość zadziałała **na naszą korzyść** w kroku 5.5 (wewnętrzny `TextBox` `NumericUpDown`
wziął wysokość za darmo) i **przeciwko nam** tutaj. To nie są dwa zjawiska, tylko jedno —
i to jest powód, dla którego §16.5 każe uruchomić aplikację po każdej kontrolce, a nie po całym kroku.
⚠ Naprawa jest selektorem na **nazwanej** części (`Expander /template/ ToggleButton#ExpanderHeader`),
zadeklarowanym **po** stylu `ToggleButton` — przy tej samej trafności wygrywa setter późniejszy.
`Stretch`, nie `Left`: treść nagłówka bywa całym `StackPanelem` (Procedure/Function), więc ma dostać
całą szerokość.

#### §15.6.9a ⭐⭐ POMIAR, KTÓRY OBALIŁ PRZESŁANKĘ §16.3 — XAML **POTRAFI** ZAALIASOWAĆ ZASÓB SKALARNY

**`MinHeight` nagłówka nie dało się naprawić setterem** i to jest przypadek, który wymusił zbadanie
sprawy do końca: szablon Fluenta konsumuje `ExpanderMinHeight` jako **wartość lokalną elementu**,
a wartość lokalna **outranks setter stylu** — nagłówek trzymał 48 px mimo settera z kroku 5.6.
⚠ To ta sama reguła, która w tym projekcie wypłynęła już trzy razy (`MessageBanner`, `MainWindow.Icon`,
`DangerIconBrush`), tym razem po stronie **frameworka**, nie naszej.

Zmierzone na Avalonii 12.0.3 sondą `ProbeAliasScalar`:

```xml
<StaticResource x:Key="ProbeAliasScalar" ResourceKey="Size.Control" />
```
> `found=True  value=24  type=Double`

**⛔ §16.3 twierdziła: „XAML nie potrafi ZAALIASOWAĆ zasobu skalarnego". To jest FAŁSZ i zostało
poprawione w miejscu.** Prawdziwe ograniczenie jest węższe: XAML nie potrafi **ZŁOŻYĆ** zasobu
w treści elementu — `<x:Double>` musi zawierać liczbę. Aliasowanie przez osobny znacznik działa.

**⚠ To NIE unieważnia podziału metryki/kolory i nie zmienia niczego, co już wysłano.** Settery stylu
zostają **domyślną trasą** dla metryk — są widoczne tam, gdzie myśli się o kontrolce, i to one
poprawnie biją `ControlTheme`. Alias jest **trzecią trasą dla jednego zmierzonego przypadku**: gdy
szablon konsumuje zasób skalarny jako wartość lokalną, setter przegrywa i nie ma czym wygrać.

**⭐ Alias spełnia regułę użytkownika LEPIEJ niż liczba, a nie „mimo wszystko":** jest
**odwołaniem**, więc `Size.Control` pozostaje jedynym właścicielem wartości, a Bridge nadal niczego
nie posiada. Strażnik `FluentBridge_ContainsNoLocalValues` przepuszcza go bez zmiany — sam znacznik
jest `StaticResource`. ⚠ Wpisy stoją **poza `ThemeDictionaries`**, bo metryka nie zależy od motywu:
ten sam podział, co między `Tokens.axaml` a `Colors.axaml`.

⚠ **Był to jedyny sposób w ogóle.** Własny `ControlTemplate` **nie wchodził w grę** — §16.4 wymaga,
by rozmiar **nie był** wystawiony jako zasób, a `ExpanderMinHeight` jest wystawiony. Reguła zadziałała
poprawnie: zabroniła przepisania szablonu i zmusiła do znalezienia właściwego mechanizmu.

#### §15.6.9b Reszta iteracji

Nagłówek bierze `Panel` (pasmo klikalne, o stopień od dokumentu — jak przycisk), **treść bierze tło
DOKUMENTU**: to, co rozwinięte, należy do czytanej zawartości, a nie do chromy, która je otwiera.
Chevron idzie na `SubtleForeground` — ta sama rola co strzałka `ComboBoxa` (5.3) i strzałki spinnera
(5.5).

⚠ **Dwa widoki miały już lokalne obejście chunky `Expandera`** — `ProcedureDetailTabView` i
`FunctionDetailTabView` niosą `MinHeight="26"` plus lokalne `ExpanderHeaderPadding`/
`ExpanderContentPadding` w `<Expander.Resources>`. Po tym kroku są **zbędne**, ale zostają: usuwanie
wartości lokalnych z widoków to **M2c**, a ten etap pracuje wyłącznie w `Themes/`. ⭐ To jest dobra
ilustracja, po co M2c ma osobny licznik: obejście, które przestało być potrzebne, wygląda identycznie
jak obejście, które nadal działa.

Build 0/0; suite **7080** (7000 + 54 + 26); smoke czysty.

### §15.7 Krok 6 — `ScrollBar` (H‑10)

Wyłącznie kolory — przez Bridge, 9 kluczy w obu motywach. Bez zmiany geometrii i bez własnego
szablonu.

**⚠ Defekt był NIESYMETRYCZNY WOBEC MOTYWÓW i dlatego audyt zapisał go jako „paski bez stylu",
a nie jako „zły kolor".** Fluent maluje uchwyt pół-przezroczystą bielą, wciśnięcie prawie czystą
(`#33ffffff` → `#66ffffff` → `#99ffffff`). W motywie ciemnym to wygląda poprawnie. **W jasnym jest to
biały uchwyt na tle `#FCFCFD`** — praktycznie niewidoczny, a po kroku 3 (nowa, jaśniejsza skala Light)
jeszcze mniej widoczny niż przed etapem. To jest §15.6.4a raz jeszcze: **uspokojenie otoczenia
podniosło głośność — tu raczej *obniżyło czytelność* — elementu, którego nikt nie ruszał.**

**⭐ TRZY NOWE ROLE W `Colors.axaml` i to jest uzasadniony wyjątek od reguły „rola z drugiego
konsumenta".** Uchwyt jest **wypełnieniem**, a jedyne tokeny o dobrym kontraście w obu motywach są
rolami **tekstu** (`SubtleForeground`) — użycie ich tutaj odebrałoby tamtej roli znaczenie (§3.1).
⚠ Reguła użytkownika mówi o **rolach bez konsumenta** (`Stroke.Rail`, `Radius.Control`); tutaj
konsument istnieje od pierwszej linii — **każdy przewijalny widok w aplikacji**. To jest ta sama
sytuacja co `Margin.MarkGap`: rola powstała z potrzeby, nie z symetrii.

**⭐ Drugi przypadek trasy aliasu (§16.3) i to on pokazuje, że nie jest ona jednorazowa.**
`ScrollBarThumbBackgroundColor` jest **`Color`, nie pędzlem** — nie da się napisać
`Color="{StaticResource …}"`, bo to sam `Color` jest zasobem. Mechanizm zmierzony w kroku 5.7 dla
**metryki** obsłużył więc **barwę**, bez żadnej zmiany reguły.

**⚠ Tor bierze tło DOKUMENTU, nie chromy.** Pasek przewijania przylega do treści, którą przewija,
i ma się z nią zlewać; widoczny ma być **uchwyt** — on niesie informację o pozycji — a nie rynna,
w której się porusza. Strzałki idą na `SubtleForeground`, jak każda afordancja drugorzędna tego etapu
(chevron `ComboBoxa`, strzałki spinnera, chevron `Expandera`) — **cztery kontrolki, jedna reguła**.

**⛔ GEOMETRIA I STRZAŁKI CELOWO NIETKNIĘTE — i to jest decyzja, nie pominięcie.** Rozważane było
usunięcie strzałek (konwencja VS Code / Rider). Odrzucone z dwóch powodów: (a) to **zmiana
funkcjonalna**, nie stylistyczna — użytkownik traci możliwość przewijania o krok kliknięciem,
a audyt H‑10 mówi o *braku stylu*, nie o nadmiarze przycisków; (b) **Fluent już robi rzecz nowoczesną**
— pasek ma stan zwinięty (cienki uchwyt, strzałki ukryte) i rozwija się dopiero pod kursorem, więc
strzałki nie są widoczne w spoczynku. ⚠ Gdyby po QA okazało się, że mimo to przeszkadzają, właściwą
odpowiedzią jest **osobna propozycja** (§15.0), a nie doklejenie jej do kroku o kolorach.

Build 0/0; suite **7081** (7000 + 54 + 27); smoke czysty.

---

## §16 ⭐⭐ `FluentBridge` — WZORZEC PROJEKTOWY EmberTerna (nie ustalenie jednej iteracji)

> **Status: RATYFIKOWANY przez użytkownika 2026-08-02, sprawdzony na dwóch kontrolkach
> (`TextBox` §15.6.4, `ComboBox` §15.6.5). Obowiązuje w całym projekcie, nie tylko w M2b.**

Ta sekcja jest **kanoniczną definicją wzorca**. §15.6.2/§15.6.3 zostają jako zapis *jak do niego
doszliśmy* (pomiar, który go wymusił); tutaj jest to, czym on **jest** i jak się go stosuje.

### §16.1 Zasada w jednym zdaniu

> **Nie przestylowujemy FluentTheme i nie kopiujemy jego szablonów — PRZEPINAMY GO NA NASZ KATALOG.**

Fluent maluje wnętrza kontrolek (`PART_BorderElement` i podobne) z **własnych zasobów nazwanych**.
Zamiast pisać własny `ControlTemplate`, podmieniamy te zasoby na nasze tokeny. Zachowujemy przez to
całe sprawdzone zachowanie frameworka — zaznaczanie tekstu, watermark, walidację, przewijanie,
animacje stanów — a wygląd i tak jest sterowany katalogiem.

⭐ **To nie jest pomysł nowy w tym projekcie, tylko uogólnienie istniejącego.** Dokładnie tak
rozwiązano kolory zaznaczenia `TreeViewItem*` / `DataGridCell*` (reguła UI #6 w `CLAUDE.md`) — tam
zadziałało na kolorach stanu, tu zostało rozciągnięte na całą powierzchnię kontrolek bazowych.

### §16.2 ⛔ REGUŁA WIĄŻĄCA — Bridge nie jest drugim katalogiem tokenów

> **Użytkownik, 2026-08-02:** *„Ma być wyłącznie warstwą mapującą zasoby Fluent na `Tokens.axaml`,
> `Typography.axaml` i `Colors.axaml`. Nie powinny pojawiać się tam lokalne wartości ani nowe
> decyzje projektowe. Wszystkie liczby i role pozostają właścicielami odpowiednich katalogów,
> a Bridge jedynie je tłumaczy."*

Egzekwowane testem **`FluentBridge_ContainsNoLocalValues`**: każdy wpis w `FluentBridge.axaml` musi
być **odwołaniem** do zasobu (`{StaticResource …}` / `{DynamicResource …}`). Wartość wpisana wprost
wywala test. ⛔ Bez tego reguła przetrwałaby dokładnie do pierwszego *„tu jest szybciej wpisać kolor"*.

### §16.3 ⚠⚠ Trzy trasy: metryki / kolory / alias — podział wynika z pomiaru, nie z upodobania

> **⚠⚠ SEKCJA POPRAWIONA W KROKU 5.7 (2026-08-02) — jej pierwotna przesłanka była FAŁSZYWA.**
> Twierdziła: *„XAML nie potrafi ZAALIASOWAĆ zasobu skalarnego"*. **Zmierzone: potrafi** —
> `<StaticResource x:Key="A" ResourceKey="B" />` rozwiązuje się poprawnie (sonda `ProbeAliasScalar`
> → `24`, `Double`, Avalonia 12.0.3, §15.6.9a). Prawdziwe ograniczenie jest **węższe**: XAML nie
> potrafi **ZŁOŻYĆ** zasobu w treści elementu — `<x:Double>` musi zawierać liczbę.
> ⭐ Wniosek podziału **przetrwał korektę przesłanki**, ale zyskał trzecią trasę. To ten sam kształt,
> co §14.3: fałszywa przesłanka z działającym wnioskiem — i dlatego trzeba ją było poprawić,
> a nie zostawić „bo i tak działa".

| Co | Gdzie | Dlaczego |
|---|---|---|
| **Metryki** — `MinHeight`, `Padding`, `FontSize`, `BorderThickness` | **setter stylu** w `ControlStyles.axaml`, czytający token przez `{DynamicResource}` | **trasa domyślna.** Styl aplikacji ma wyższy priorytet niż `ControlTheme` Fluenta, a setter jest widoczny tam, gdzie myśli się o kontrolce |
| **Kolory malowane przez wnętrze szablonu** | **`FluentBridge.axaml`**, jako `SolidColorBrush Color="{StaticResource …Color}"` | setter na kontrolce tam nie dociera — maluje `PART_*`, nie kontrolka |
| ⭐ **Metryka, którą szablon konsumuje jako WARTOŚĆ LOKALNĄ elementu** | **`FluentBridge.axaml`**, jako `<StaticResource x:Key="…" ResourceKey="…" />`, **poza `ThemeDictionaries`** | **wartość lokalna outranks setter stylu**, więc trasa pierwsza nie ma czym wygrać. Zmierzony przypadek: `ExpanderMinHeight` (§15.6.9a) |
| **Wartości, w których Fluent już się z nami zgadza** (`ControlCornerRadius` = 3 = `Radius.Surface`; `ToggleButtonBackgroundChecked` = nasz akcent) | **nigdzie** — pinowane testem | wpis powielałby wartość, którą już kontrolujemy; test zamienia zbieżność w sprawdzany niezmiennik |

⭐ **Alias NIE łamie §16.2 — spełnia ją lepiej niż liczba:** jest **odwołaniem**, więc właścicielem
wartości pozostaje katalog, a Bridge nadal niczego nie posiada. `FluentBridge_ContainsNoLocalValues`
przepuszcza go bez zmiany, bo sam znacznik jest `StaticResource`.

⚠ **Trasa trzecia jest wyjątkiem, nie alternatywą dla pierwszej.** Sięgaj po nią **dopiero wtedy, gdy
setter zmierzalnie przegrał** — nie „na wszelki wypadek". Setter jest czytelniejszy: stoi przy
kontrolce, a nie w słowniku tłumaczeń.

⚠ **Konsekwencja dla testów: trasy sprawdza się osobno.** Metrykę czytaj z kontrolki, kolor
z elementu, który **faktycznie maluje**, a alias — z samego zasobu (`ExpanderMinHeight` == `Size.Control`).
Alias, który przestanie się rozwiązywać, po cichu wróci do wartości Fluenta przy zielonym buildzie.

⚠ **Konsekwencja dla testów: obie połowy jadą innymi trasami i żadna nie dowodzi drugiej.**
Test kontrolki musi sprawdzać **metrykę na kontrolce** i **kolor na elemencie, który faktycznie
maluje** (`PART_BorderElement`). Asercja koloru odczytana z samej kontrolki przechodzi, malując
po cichu nic.

### §16.4 Kiedy WOLNO napisać własny `ControlTemplate` — dwa warunki, oba konieczne

Własny szablon jest **wyjątkiem wymagającym uzasadnienia pomiarem**, nie domyślną odpowiedzią.

1. potrzebna wielkość **nie jest wystawiona jako zasób** Fluenta, tylko zakodowana w szablonie, **oraz**
2. sonda drzewa szablonu pokazuje, że element do zmiany **nie ma nazwy** (`x:Name`), więc selektor
   trafiałby w niego pozycyjnie — a to działa dziś i po cichu zmienia cel przy aktualizacji Avalonii.

**Dotąd spełniły oba warunki dokładnie dwie kontrolki: `CheckBox` (§15.2) i `RadioButton` (§15.6.1)** —
u obu rozmiar znaku jest wartością lokalną wewnątrz szablonu. ⚠ To, że są wyjątkiem, a nie regułą,
**pokazała dopiero sonda `TextBoxa`** — czyli kolejność „najpierw zmierz, potem wybierz mechanizm"
nie jest formalnością.

⛔ **Nie wolno przepisywać szablonu, bo „i tak już mamy dwa własne".** Spójność systemu bierze się
z jednego katalogu tokenów, a nie z jednego mechanizmu dostarczania.

### §16.5 Procedura dla następnej kontrolki

1. **Sonda headless** — wypisz drzewo szablonu i zmierzone `MinHeight` / `Padding` / `FontSize`
   (⚠ nie ufaj samej właściwości: `RadioButton` miał `MinHeight=0` i żądaną wysokość 32).
2. **Sprawdź, czy Fluent wystawia potrzebne pokrętła jako zasoby** — jeżeli tak, §16.4 nie jest
   spełnione i własny szablon jest niedozwolony.
3. **Metryki → setter stylu; kolory → Bridge** (§16.3), w **obu** motywach.
   ⚠ Jeżeli setter **zmierzalnie przegrał** (szablon trzyma wartość lokalną) — dopiero wtedy alias.
3a. ⚠⚠ **Sprawdź, czy styl typu nie sięgnął do CUDZEGO szablonu.** Styl typu obowiązuje wszędzie,
   gdzie ten typ wystąpi — także wewnątrz szablonu innej kontrolki. Krok 5.6 (`ToggleButton`) w ten
   sposób wyśrodkował nagłówek `Expandera`, a krok 5.5 w ten sam sposób dostał wysokość wewnętrznego
   `TextBoxa` za darmo. **Jeden mechanizm, dwa znaki** — sprawdź, który wypadł tym razem.
4. **Test — po jednej asercji na trasę** — metryka z kontrolki, kolor z części malującej, alias
   z zasobu.
5. ⚠ **Sprawdź wariant „w komórce siatki"** — `Size.Row.Grid` (22) − `Pad.Cell` (3+3) = **16 px**,
   więc kontrolka formularza o `MinHeight=24` podnosi każdy edytowany wiersz o 8 px (§15.6.4).
   **Wartość poprawna dla formularza bywa destrukcyjna dla siatki, a to ta sama klasa kontrolki.**
6. **Uruchom aplikację i oceń w komplecie stanów, w obu motywach** (zasada z §15.2.1) — normal ·
   hover · aktywny · disabled · focus.

### §16.6 Rejestracja i kolejność wczytywania (`App.axaml`)

```
Tokens → Typography → Colors → FluentBridge → IconGeometries → ControlThemes → SearchableComboBox → PickerTemplates
```

⚠ **Dwie kolejności są wymuszone, nie kosmetyczne:** `FluentBridge` **po** `Colors` (mapuje przez
`Color="{StaticResource …Color}"`, więc tokeny barw muszą już być wczytane) i `ControlThemes` **po**
`IconGeometries` (szablon `CheckBoxa` sięga po `{StaticResource Icon.Check}`, a `StaticResource`
rozwiązuje się przy wczytywaniu).
