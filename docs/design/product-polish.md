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

> ⚠⚠ **AKAPIT POWYŻEJ ZOSTAŁ OBALONY POMIAREM W M2c (krok 0, 2026-08-02) — czytaj go jako zapis
> tego, co M2a *założyło*, nie jako opis kodu.** Zdanie „wszystkie są chipami" jest nieprawdziwe
> **w obu połowach**, a rozstrzygnięcie jest ratyfikowane (§18.0.5/2):
>
> * **`4.5` / `5` / `6` (7 wystąpień) to nie chipy, tylko GEOMETRIA.** `Width=10 Height=10
>   CornerRadius=5` i `Width=9 Height=9 CornerRadius=4.5` to **koła**; `Height=12 CornerRadius=6`
>   i `Height=10 CornerRadius=5` to **kapsuły** pasków postępu. Promień jest połową boku, czyli
>   wynikiem arytmetyki, a nie decyzją projektową. ⛔ **Nie tokenizujemy geometrii wynikającej
>   z matematyki** — `Radius.Chip` (4) zamieniłby koło w kwadrat ze ściętymi rogami.
> * **`4` (11 wystąpień) to w większości KARTY, nie chipy** — `BorderThickness="1" Padding="10,8"`,
>   kontenery `ClipToBounds`, kafelek wiersza. Chipem jest tam **jedno** wystąpienie
>   (`AggregationBarView`). Rolą trafną byłby `Radius.Surface`, ale to zmiana 4 → 3, więc jest to
>   **decyzja produktowa oddana przeglądowi §13.3**, nie sprzątanie.
>
> ⭐ **Do katalogu to nic nie dodaje i nic z niego nie zabiera** — obie role zostają dokładnie takie,
> jakie są. Zmienia się wyłącznie **zasięg**: M2c migruje `3` → `Radius.Surface` i zostawia resztę
> z uzasadnieniem (R12, §18.0.8).

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

**Neutralność.** ⚠⚠ **SKORYGOWANE POMIAREM W M3.2b — i to JEDYNA rzecz z tamtej iteracji, która
przetrwała jej wycofanie, bo jest faktem o kodzie, nie decyzją projektową. Dla IKONY neutralnym jest
`NeutralIconBrush`, nie `ForegroundBrush`, a realizuje się to przez BRAK `Foreground`.** To dwa różne tokeny i różnica jest
celowa: Dark `#C8CCD2` vs `#D4D4D4`, Light `#3C3C3C` vs `#1B1D1F` — ikona stroke'owana czyta się inaczej
niż litera, więc ma własną wartość neutralną, ustawioną jako domyślna w `ControlTheme` kontrolki
`SvgIcon`. ⛔ Wpisanie `ForegroundBrush` na ikonę rozjechałoby pasek z ikonami całej reszty aplikacji
i byłoby wartością lokalną tam, gdzie rola już odpowiada (reguła 9 UI). Zapis „ForegroundBrush" powyżej
pochodził sprzed pomiaru; §19.13.
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

#### ⛔⛔ Zmiany do wykonania — TA TABELA JEST HISTORYCZNA I NIE OBOWIĄZUJE

⚠⚠ **M3.2b wykonało ją co do litery i zostało WYCOFANE W CAŁOŚCI** (§19.13 + §19.14). Wiersz
*„6 narzędzi ogólnych → neutralne"* okazał się głównym powodem odrzucenia: te sześć przycisków
**otwiera moduły**, co jest osobną rolą, której ta tabela nie znała.

⭐⭐ **Źródłem prawdy o kolorach jest od 2026-08-02 osobny dokument produktu:
[`color-language.md`](color-language.md).** Ta sekcja zostaje jako **jedno z jego wejść** i jako zapis
tego, jak wąsko postawione pytanie („co zrobić z sześcioma przyciskami w pasku tytułu") prowadzi do
odpowiedzi poprawnej lokalnie i złej dla produktu.

| Element | Dziś | ⛔ Zapis historyczny | Co mówi `color-language.md` |
|---|---|---|---|
| 10 przycisków „Nowy X" | `IconColor_*` | bez zmian | ✅ to samo — system **S1**, bez zmian |
| 6 narzędzi ogólnych | `AccentBrush` | ~~neutralne~~ | ⭐ **ZOSTAJĄ KOLOROWE** — rola **R‑6** „wejście do narzędzia" |
| `Icon.Trash` (Usuń połączenie) | `WarningIconBrush` | `DangerIconBrush` | ✅ to samo — rola **R‑4** |
| `Icon.PlugZap` | `AccentIconBrush` | neutralny | ✅ to samo — **R‑7** (nie otwiera modułu) |
| `Icon.RefreshCw` | `InfoIconBrush` | neutralny | ✅ to samo — **R‑7** |
| `AccentIconBrush`, `InfoIconBrush` | 2 tokeny | zlikwidowane | ⏸ **DC** — M4.3/M5, poza językiem |

⭐ **Efekt: pasek narzędzi POZOSTAJE kolorowy** — dziesięć ikon rodzajów, Commit, Rollback i akcje
destrukcyjne nadal niosą barwę. Znika wyłącznie niebieska tapeta, na której te kolory dziś się gubią.
**To jest realizacja §3.4 specyfikacji (*calm interface*) bez utraty tożsamości.**

⚠ Pełna tabela przypisań per przycisk powstaje w M2b i wchodzi do przeglądu — powyżej jest reguła,
nie lista. ⚠⚠ **Ta sekcja opisywała WYŁĄCZNIE pasek tytułu i to było jej ograniczenie, nie kompletność:**
M3.2b znalazło w **toolbarze dokumentu** trzy dalsze naruszenia tego samego kontraktu (Uncomment jako
`Danger`, Comment jako `Info`, Execute procedury jako `Success`) — §19.13.2.

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
| **M3.3** | ✅ **DOSTARCZONE I ODEBRANE 2026-08-03** (§19.22–§19.25). **Pasek zakładek**: dwa tryby, limit wierszy, menu kontekstowe (D5–D9) + wiersze w Settings Center | tak |
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
| 6 | **`ScrollBar`** (H‑10) | `7ab3d27` | ⏳ oczekuje | §15.7 |
| 7 | **DataGrid Standard** (§8.4) + `Pad.CellEditor` | `e95913b` | ⏳ oczekuje | §15.8 |
| 8 | ⭐ **dwie drabiny wysokości** + pasek jednej wysokości | — | ⏳ oczekuje | §15.9.1 |
| 9 | **Ustawienia jako panel referencyjny** | — | ⏳ oczekuje | §15.9.2 |
| 10 | **Data Import** — proporcje kolumn | `e0a59fd` | ⏳ oczekuje | §15.9.3 |
| 11 | ⭐⭐ **kolor niesie priorytet, rozmiar nie** + pasma chromy + bliskość podpisu | `1c7ccc1` | ⏳ oczekuje | §15.10 |
| 12 | **domknięcie pod nowym kryterium** — belka statusu · podłoga szerokości akcji · filtry · picker · korekta licznika | `b568168` | ⏳ oczekuje | §15.11 |
| 13 | ⭐⭐ **reguła przecząca → pozytywna** — regresja strzałki w drzewie + prawdziwa przyczyna nierównych stopek | — | ⏳ oczekuje | §15.12 |

**Krok 5 (kontrolki bazowe) — ZAKOŃCZONY.**
✅ **M2b — WSZYSTKIE KROKI DOSTARCZONE.** Pozostało **QA wizualne użytkownika** (kroków 5.4–7)
i dopiero po nim **M2c** (sweep de‑lokalizacyjny).

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

### §15.8 Krok 7 — DataGrid Standard (§8.4 specyfikacji) — OSTATNI KROK M2b

**⚠ Ten krok nie jest przebudową, tylko DOMKNIĘCIEM — i to jest jego najważniejsza cecha.**
Specyfikacja §8.4 wylicza osiem rzeczy, które standard ma definiować. **Sześć było już zrobionych,
tylko rozproszonych po etapie i wyrażonych LICZBAMI zamiast rolami:**

| §8.4 żąda | Gdzie powstało |
|---|---|
| wysokość checkboxów | krok 1 — własny szablon, `Size.Checkbox` |
| wysokość edytorów | kroki 5.2 i 5.4 — `DataGridCell TextBox` / `Button` |
| zaznaczenie aktywnego wiersza | istniejące `SelectionBrush` na `Rectangle#BackgroundRectangle` |
| zachowanie podczas edycji | konsekwencja dwóch powyższych — edytor bierze wysokość WIERSZA |
| odstępy | `Pad.Cell` — istniał od M2a, tu podpięty |
| wyrównanie tekstu | `VerticalContentAlignment` — dopisane tutaj |
| **wysokość wiersza** | **brakowało — `Size.Row.Grid`** |
| **wysokość nagłówka** | **brakowało — `Size.Row.Header`** |

⚠ Dotąd `DataGridRow` miał `MinHeight="0"`, czyli siatka **nie miała standardu, tylko wynik pomiaru
zawartości**: identyczne dane dawały identyczne wiersze przypadkiem, a nie z decyzji. `MinHeight` jest
**podłogą**, nie sztywną wysokością — wyższa zawartość nadal rozpycha wiersz.
⚠ Nagłówek jest o stopień wyższy od wiersza (24 vs 22) i **to jest cała jego rama**: czyta się jako
nagłówek bez ramki, kreski i bez innego tła niż chroma.

#### §15.8.1 ⭐⭐ TEST ZNALAZŁ DEFEKT, KTÓRY WSZEDŁ W KROKU 5.2 I BYŁ NIEWIDOCZNY PRZEZ PIĘĆ ITERACJI

Asercja §8.4 („nic w komórce nie może rozepchnąć wiersza") **zaświeciła się na czerwono przy pierwszym
uruchomieniu**:

> `TextBox (step 5.2) asks for 18 px inside a cell that leaves 16 px`

**Arytmetyka:** wiersz 22 − `Pad.Cell` (3+3) = **16 px**. Edytor z `Pad.CellCompact` (`6,2`) prosi
o **18** — tekst 14 plus 2+2 pionu. Wiersz rósł więc do 24 **w momencie wejścia w edycję**, czyli
dokładnie ten skok układu, którego zabrania §13.3 specyfikacji (**Zero Layout Shift**).

⭐ **Dlaczego to przetrwało pięć iteracji: krok 5.2 sprawdzał WŁAŚCIWĄ rzecz, ale tylko jedną jej
połowę.** Pilnował, żeby edytor nie miał `MinHeight` kontrolki (24) — i tego dopilnował. Nikt nie
policzył, ile zostaje **po odjęciu paddingu komórki**, bo to wymaga zestawienia **dwóch tokenów
z dwóch różnych kroków**. Dopiero test, którego przedmiotem jest siatka **jako całość**, ma powód
je zestawić.
⚠ **To jest ten sam kształt błędu co w kroku 1** (asercja mierzyła wysokość wiersza zamiast miejsca,
jakie zostawia komórka) — trzeci raz w tym etapie. Wniosek do zapamiętania: **arytmetyka §5.1 musi być
sprawdzana na SUMIE, nie na składniku.**

**Poprawka: nowa rola `Pad.CellEditor` = `6,0`** — druga i ostatnia rola dopisana w M2b, również
procedurą z §4.1 i również wykryta pomiarem, a nie zaplanowana. ⚠ **Nie da się jej złożyć
z `Pad.CellCompact`**: tamta opisuje wnętrze **gęstej komórki** (Trace Monitor, Session Manager) —
treści **czytanej**, nie edytowanej — i jej pion 2 px jest tam poprawny, bo to **ona** jest
właścicielem wysokości. W edytorze właścicielem wysokości jest **wiersz**, więc pion musi zniknąć.
⭐ To jest dokładnie ta sama reguła „jedna wielkość, jeden właściciel", która w kroku 5.2 kazała dać
`Pad.Control` zerowy pion — o jeden poziom zagnieżdżenia dalej. Dwaj konsumenci (`TextBox`, `Button`
w komórce) byli znani w chwili dodania, więc reguła użytkownika o powstawaniu ról jest spełniona.

⚠ **Poziom 6, a nie 8:** krawędź edytora nie ma dociskać tekstu do krawędzi komórki.

⚠ **Linie siatki przez Bridge** (`DataGridGridLinesBrush` → `BorderColor`): Fluent dawał tam
pół-przezroczystą biel, więc w motywie jasnym linie znikały — **ten sam defekt co uchwyt paska
przewijania w kroku 6**, znaleziony przy okazji. Nagłówek i wiersz mają settery stylu, więc ich kolory
**nie** trafiają do Bridge'a (§16.3, wiersz czwarty).

Build 0/0; suite **7082** (7000 + 54 + 28); smoke czysty.

### §15.9 Kroki 8–10 — runda po QA użytkownika (proporcje Design Systemu)

> **Werdykt wyjściowy:** *„M2b jest bardzo dużym krokiem do przodu… Natomiast właśnie teraz, kiedy
> większość kontrolek została ujednolicona, ujawniły się miejsca wymagające dopracowania."*
>
> ⭐ To jest **§15.6.4a w skali całego etapu**: uspokojenie kontrolek podniosło głośność proporcji,
> których wcześniej nie było widać. Użytkownik ratyfikował też ramę tej rundy — **Ustawienia są
> panelem referencyjnym**: proporcje dopracowuje się tam, a potem przenosi na resztę, bo inaczej
> „M2c utrwali drobne niedoskonałości w setkach miejsc".

#### §15.9.1 ⭐⭐ Krok 8 — DWIE DRABINY WYSOKOŚCI ZAMIAST JEDNEJ (korekta kroku 5.4)

> **Użytkownik:** *„nie wszystkie przyciski w aplikacji powinny mieć identyczną wysokość"* —
> toolbar (niskie) · zwykłe przyciski formularzy (trochę wyższe) · główna akcja dialogu (najwyższa).

**Krok 5.4 przyjął milczące założenie, że przycisk jest kontrolką jak każda inna, i dał mu
`Size.Control` (24) — czyli wysokość POLA FORMULARZA. To założenie było błędne** i QA nazwało je
precyzyjniej, niż zrobiłby to katalog: **pole stoi w SERII i ma się wyrównywać; przycisk stoi
SAMOTNIE i jest CELEM MYSZY.** To dwie różne wielkości, które przypadkiem zbiegły się na 24.

| Drabina | Rola | px | Kto |
|---|---|---|---|
| **POLA** | `Size.Control` | 24 | `TextBox`, `ComboBox`, `NumericUpDown` w formularzu |
| **AKCJE** | `Size.ControlToolbar` | **22** | przycisk w pasku narzędzi (chroma) |
| | `Size.ControlProminent` | **26** | stopka dialogu: Close, Cancel, OK |
| | `Size.ControlPrimary` | 28 | akcja główna: Execute, Import |

⚠ **D1 NIE JEST ZMIENIONE.** 24 / 22 / 28 znaczą dokładnie to samo co przedtem; doszła jedna nowa
liczba (26) i **przypisanie ról, o które D1 nie pytało**.
⭐ `Size.ControlProminent` jest **rolą, a nie „wyższym przyciskiem"**, i dowodzi tego drugi konsument
**innego rodzaju**: pole wyszukiwania (§15.9.2). Gdyby konsument był jeden, byłaby to wartość.

**⚠⚠ Pasek narzędzi: KONTENER DEKLARUJE WYSOKOŚĆ SWOICH DZIECI.** Zgłoszenie: *„przycisk może być
wizualnie wyróżniony kolorem, ale nie powinien podnosić całego paska"* — `Button.primary` (28) stojąc
obok przycisków ikonowych (22) rozpychał chromę do swojej wysokości. ⭐ Rozwiązanie to **trzecie
wystąpienie tego samego mechanizmu w tym etapie**: edytor w komórce siatki (§15.6.4), nagłówek
`Expandera` (§15.6.9), teraz pasek — *element wypełnia kontener, a nie rozpycha go*. Hierarchię niesie
dalej **kolor i ikona**; traci tylko wysokość, czyli jedyny nośnik, który psuł sąsiadów.
⚠ Styl `Border.toolbar Button` **musi stać po** `Button.primary` — to właśnie `.primary` ma tu zostać
pokonany. Trzeci raz w etapie, gdy kolejność deklaracji jest treścią, a nie porządkiem.

#### §15.9.2 Krok 9 — Ustawienia jako panel referencyjny

**⭐ Dwa zgłoszenia okazały się JEDNYM defektem.** *„Lista kategorii w Ustawieniach jest zbyt wysoka,
a czcionka o stopień za duża"* i *„lista Saved Queries powinna być bardziej zwarta — to nie formularz,
tylko lista robocza"* mają wspólną przyczynę: **`ListBoxItem` nigdy nie dostał stylu**, więc obie listy
stały na Fluencie (wysoki wiersz, `FontSize` 14). Jeden styl, oba ekrany.
⭐ Rola dzielona z `ComboBoxItem` i wierszem menu jest tu **poprawna** (§3.3): pozycja listy, pozycja
listy rozwijanej i wiersz menu to **ten sam gest** — czytasz w pionowej serii, wybierasz jednym
kliknięciem. ⚠ Pasek boczny jest nietknięty **z konstrukcji**: ma własny styl w `ListBox.Styles`
swojego drzewa, a styl bliższy w drzewie bije styl aplikacji. Metadata Explorer zostaje poza etapem.

**Pole wyszukiwania** (Ustawienia + Global Search) — `Size.ControlProminent` + wyśrodkowanie w pionie.
⚠ Wyśrodkowanie **działa dlatego, że `Pad.Control` ma pion zerowy**: wysokość daje `MinHeight`, więc
tekst ma się gdzie wyśrodkować. To ta sama decyzja z kroku 5.2, tu spłacona.

**⚠⚠ RadioButton — pierwsza wersja poprawki WYWRÓCIŁA RB‑2 i złapał to test.**
Zgłoszenie *„brakuje oddechu"* jest skutkiem kroku 5.1: `RadioButton` stracił wtedy `MinHeight`
(słusznie), więc kolejne opcje stykają się znakami. Pierwsza wersja dała margines **każdemu**
`RadioButtonowi` — a margines wchodzi do `DesiredSize`, więc kontrolka natychmiast przestała mieścić
się w wierszu siatki i `RadioButton_FitsInsideAGridRow_LikeItsCheckBoxSibling` zaświecił się na
czerwono. ⭐ **Guard z kroku 5.1 zadziałał dokładnie tak, jak miał — trzy iteracje po tym, jak
powstał.**
⭐ Poprawka (`ItemsControl RadioButton`) jest zarazem **trafniejsza semantycznie**: rola nazywa odstęp
między **OPCJAMI**, a opcja to przełącznik będący **pozycją listy**. Przełącznik stojący samotnie
(komórka siatki, pole formularza) nie jest opcją. ⚠ `DataGrid` nie jest `ItemsControl`, więc komórki
są poza zasięgiem **z konstrukcji** — bez osobnego wyjątku do zapamiętania.
⭐ Nowa rola `Margin.OptionGap` = `0,0,0,4`; nie da się jej złożyć z `Margin.FieldGap` (`0,0,0,8`),
bo tamta rozdziela **pola**, a opcje jednego wyboru mają czytać się jako **jedna grupa**.

#### §15.9.3 ⭐ Krok 10 — Data Import: przyczyna była OBOK zgłoszenia

Zgłoszone trzy rzeczy: kolumna `NULL` ma dużo wolnego miejsca, `Type` nie mieści nazw typów,
a najważniejsza `Column` ma za mało miejsca.

**⭐⭐ Przyczyna dwóch ostatnich jest jedna i leży gdzie indziej: kolumna `Basis` jest UKRYTA, ale jej
`ColumnDefinition` dalej mierzy `3*`.** Przy imporcie bez per-kolumnowej podstawy **jedna trzecia
siatki stała pusta**, a `Column` i `Type` gniotły się w reszcie. ⚠ To **trzecie wystąpienie tej samej
prawdy w tym projekcie**: *kontener, którego dzieci są zwinięte, NADAL JEST MIERZONY* — dokładnie to
zostawiło puste wcięcie po znaku w pasku tytułu podczas sprintu brandingowego. Różnica: `IsVisible`
nie istnieje na `ColumnDefinition`, więc **reagować musi sama szerokość**.
⭐ Stąd `BoolToStarWidthConverter` — w **warstwie widoku**, bo `GridLength` jest typem Avalonii,
a reguła architektury #1 trzyma je poza VM. VM podaje `bool`; zamiana na szerokość to prezentacja.
⚠ Proporcje przy okazji: `3*,2*` → **`4*,3*`**, `NULL` `50` → **`Auto` z `MinWidth` 40** i checkbox
**wyśrodkowany** (dosunięty do lewej zostawiał pustkę, którą użytkownik odczytał jako szerokość).

Build 0/0; suite **7084** (7000 + 54 + 30); smoke czysty.

### §15.10 Krok 11 — druga runda QA: **przyczyna w systemie, nie na ekranie**

> **Dyrektywa użytkownika, która rządzi całym tym krokiem:** *„Bardzo zależy mi, żebyśmy nie zaczęli
> teraz łatania każdego okna osobno. Jeżeli jakiś ekran wygląda źle, to chcę najpierw znaleźć regułę
> Design Systemu, która za to odpowiada."*
>
> ⭐ **To jest kryterium odbioru tego kroku, a nie preambuła.** Każdy z pięciu punktów QA dostał
> odpowiedź w postaci **reguły**, a liczba zmienionych ekranów jest jej skutkiem ubocznym.

#### §15.10.1 ⛔⛔ RATYFIKOWANE: kolor określa priorytet akcji, ROZMIAR NIE

> **Użytkownik:** *„Kolor może określać priorytet akcji. Rozmiar nie powinien."*

**To odwraca decyzję kroku 8 i to jest poprawne.** Krok 8 dał `Button.primary` własną wysokość
(`Size.ControlPrimary` 28) przy 26 na rodzeństwie — i **to był jeden setter stojący za „Execute jest
większy od Cancel" w KAŻDYM oknie**: 38 przycisków w 26 plikach. Zgłoszenie brzmiało „dialogi są
niespójne", ale poprawianie ich po kolei leczyłoby objaw.
⭐ **Usunięcie jednego settera naprawia wszystkie naraz — i to jest właśnie test na to, czy defekt
został znaleziony w systemie, czy na ekranie.**

⭐ **Powód głębszy niż preferencja:** stopka dialogu to **seria** przycisków w jednym rzędzie, a seria
musi się wyrównywać — dokładnie ta sama racja, dla której pole formularza ma `Size.Control`. Różnica
wysokości w rzędzie nie czyta się jako hierarchia, tylko jako niedbałość układu.
**⛔ `Size.ControlPrimary` WYCOFANY z katalogu** (`RetiredTokens`): rola straciła jedynego konsumenta,
a token bez konsumenta jest nieodróżnialny od regresji (#233).
⚠ `Size.ControlProminent` **26 → 28**: po wycofaniu tamtej to jedyna wysokość akcji poza chromą, więc
przejmuje wartość, która w stopce czytała się dobrze — *„za mały" był Cancel, nie „za duży" Execute*.

**⭐ Wynikowa reguła, jedna dla całej aplikacji: WYSOKOŚĆ PRZYCISKU BIERZE SIĘ Z KONTEKSTU, NIGDY
Z WARIANTU.** Pasmo chromy → `Size.ControlToolbar`; stopka dialogu i formularz → `Size.ControlProminent`;
komórka siatki → wysokość wiersza; pasek tytułu → `Size.TitleBar`. Wariant niesie **kolor**.

#### §15.10.2 Pasmo chromy — klasa nazywa ROLĘ, nie instancję

Zgłoszenie: *„SQL Editor wygląda dobrze, Script Executor już nie — Run jest wyższy niż powinien"*.
⚠ **Przyczyną było to, że krok 8 otagował JEDNO pasmo.** Reguła „kontener deklaruje wysokość swoich
dzieci" jest poprawna, ale działa tylko tam, gdzie kontener jest oznaczony — a użytkownik natychmiast
znalazł nieoznaczony.
⭐ Klasa **`Border.toolbar` → `Border.chrome`**: obejmuje również **dolne paski statusu** (rozjechany
pasek Data Import to ten sam defekt, punkt 4), a te nie są paskami narzędzi. `toolbar` nazywał
**instancję**, `chrome` nazywa **rolę** — pasmo Application Chrome (§0.1.2). Otagowane **wszystkie
osiem** pasm: MainWindow · Script Executor · Data Import (status) · Debugger · Trace Monitor ·
Session Manager · Global Search · Performance.
⛔ **Każde nowe pasmo chromy musi tę klasę dostać** — to jest cena tej reguły i trzeba ją znać.

**⭐ Run pokazuje `F5`, bo to ta sama akcja.** Zgłoszenie *„jeżeli Execute pokazuje skrót, Run również
powinien"* jest wprost regułą jednego źródła: Run to `CommandId.Go`, ta sama komenda co Execute, więc
chip czyta ten sam `ToolbarExecuteHint` (czyli `CommandTip.Gesture` z katalogu komend). ⚠ Nie jest to
skopiowany napis — gest jest zapisany w **jednym** miejscu, więc nie da się ich rozjechać (#284).

#### §15.10.3 ⭐ Podpis należy do swojego pola — dwóch właścicieli jednego odstępu

Zgłoszenie: *„odstęp między napisem Search for a polem jest zbyt duży; label wygląda, jakby należał do
poprzedniej sekcji"*. **Zmierzone: podpis niósł margines 4, a kontener dokładał `Spacing` 10 — razem
14, przy odstępie pole→pole równym 10.** Proporcja **odwrócona**, a oko przypisuje podpis do tego, co
bliżej.
⭐ **Katalog tę regułę już znał** — `Margin.FieldGap` mówi wprost, że *odstęp ma jednego właściciela*.
Tutaj była złamana. Nowa rola `Margin.LabelGap` (2) domyka skalę bliskości:
**podpis (2) < opcje jednej grupy (4) < pola (8)** — trzy odstępy, jedna skala, zapięte asercją
porządku, nie liczb.
⚠ Konsekwencja dla widoków: **kontener trzymający podpis i jego pole nie może dokładać między nie
odstępu**; jeżeli używa `Spacing`, podpis i pole muszą być **jednym dzieckiem** (poprawione w Global
Search).

#### §15.10.4 Kontrolka własna musi czytać ten sam katalog

Zgłoszenie: *„Domain, zwykły ComboBox i CheckBox nie wyglądają jak elementy jednego systemu"*.
⚠ **Przyczyna: `SearchableComboBox` to kontrolka WŁASNA i nigdy nie przeszła przez M2b** — miała
`MinHeight="24"` wpisane na sztywno z czasów sprzed katalogu. Wartość się nie zmienia, **właściciel
tak**. ⭐ Reguła: *kontrolka własna odgrywająca rolę kontrolki bazowej konsumuje ten sam katalog* —
inaczej stoi obok systemu i widać to natychmiast.
⭐ **Pole filtra w pickerze dostało klasę `search`** — filtr **jest** polem wyszukiwania. Rola
`Size.ControlProminent` ma dzięki temu **trzech** konsumentów (Ustawienia · Global Search · każdy
picker + filtr paska bocznego), co ostatecznie czyni ją rolą, a nie wartością jednej kontrolki.

#### §15.10.5 ⭐ CheckBox w siatce — ZMIERZONY, i nie jest przyczyną

Użytkownik poprosił o ponowny pomiar. Sonda headless:

```
CheckBox  Desired = 28 × 14      (znak 14, cel kliknięcia 20 szer., MarkGap 8)
Komórka   Desired = 44 × 20      (14 + Pad.Cell 3+3)
Wiersz    Size.Row.Grid = 22
```

**`CheckBox` prosi o dokładnie 14 px — tyle, ile ma jego znak, i ani piksela więcej.** Nie rozpycha
wiersza: 14 ≤ 16 px, jakie zostawia komórka, a komórka 20 ≤ 22 px podłogi wiersza.
⭐ **Wiersz ma 22 px, bo `Size.Row.Grid` tak mówi** (zadeklarowane w kroku 7), a nie przez `CheckBox`.
⚠ Jeżeli wiersz nadal wydaje się wysoki, **dźwignią jest `Size.Row.Grid`, nie kontrolka** — i to jest
osobna decyzja, bo ta liczba stoi w D1 i rządzi wszystkimi siatkami naraz.

#### §15.10.6 Data Import

Kolumna nazwy `4*` → **`5*`**, `NULL` `MinWidth` 40 → **32** (i tak było już `Auto` + wyśrodkowany
znak). Pasek statusu naprawiony **regułą z §15.10.2**, nie lokalnie: to pasmo chromy, więc jego
przycisk *Clear* bierze wysokość chromy zamiast wysokości stopki dialogu.

Build 0/0; suite **7085** (7000 + 54 + 31); smoke czysty.

### §15.11 Krok 12 — domknięcie M2b pod NOWYM kryterium odbioru

> **⭐⭐ RATYFIKOWANA ZMIANA KRYTERIUM (użytkownik, 2026-08-02) — obowiązuje do końca etapu:**
> *„Pomiar nadal jest obowiązkowy. Ale nie jest już celem. Jest tylko narzędziem. Końcówkę M2b
> chciałbym oceniać przede wszystkim pytaniem: czy wygląda to jak dopracowana aplikacja komercyjna?
> Jeżeli liczby są poprawne, ale coś nadal wygląda przeciętnie albo niespójnie, to znaczy, że trzeba
> poprawić produkt, a nie udowadniać pomiarem, że jest dobrze."*
>
> ⚠ To jest **wzmocnienie §0.1.1** („tokeny są środkiem, nie celem"), a nie nowa zasada — ale
> wzmocnienie istotne: ⛔ **zielony test przestaje być argumentem końcowym.** Pomiar zostaje
> obowiązkowy jako narzędzie diagnozy; przestaje być dowodem jakości.

#### §15.11.1 Belka statusu — kontener wyrównywał PUDEŁKA, a użytkownik czyta TREŚĆ

Zgłoszenie: *„elementy nie wyglądają jak jedna linia, każdy żyje własnym życiem"*.
**Zmierzone:** pasmo `chrome` deklarowało od kroku 11 wysokość **przycisków**, ale **nic nie deklarowało
dla tekstu**. Część `TextBlock`ów miała `VerticalAlignment="Center"`, część nic (czyli `Stretch`, przy
którym tekst osiada u GÓRY), a przycisk niósł jeszcze własny padding i `FontSize="11"` przy tekście 12
— **trzy linie bazowe i dwa rozmiary w jednym pasku**.
⭐ **Reguła, której brakowało: kontener deklarujący wysokość dzieci musi deklarować też ich LINIĘ.**
Inaczej wyrównuje pudełka, a użytkownik widzi treść.
⚠ Wartości lokalne w widoku zostały **usunięte, a nie poprawione** — dopóki tam stały, żadna reguła
systemu nie mogła tej belki naprawić (wartość lokalna bije setter stylu). Belka bierze teraz
`Size.StatusBar` i rolę `Text.Status`.

#### §15.11.2 Stopka dialogu — sama równa wysokość nie wystarczyła

Krok 11 wyrównał wysokości i pary **nadal nie czytały się jako jeden komponent**, bo szerokość brał
**tekst**: krótka etykieta dawała mały przycisk, długa duży. ⭐ Czyli **rozmiar znów niósł informację,
której nieść nie ma** — ta sama diagnoza co w §15.10.1, tylko na drugiej osi.
**Nowa rola `Size.ActionMinWidth` (80) — PODŁOGA, nie szerokość sztywna**: dłuższa etykieta nadal
rozpycha przycisk, krótsza już go nie kurczy. To konwencja stopki dialogu w Windows i macOS.
⚠ Z podłogi **wychodzą** dokładnie te konteksty, które wychodzą też z wysokości: pasmo chromy,
przycisk ikonowy, komórka siatki — inaczej każda ikona w toolbarze niosłaby 80 px powietrza.

#### §15.11.3 Filtr na zakładce Columns — reguła była dobra, brakowało instancji

*„Poprawiłeś pole na zakładce Domain, ale identyczne pole na zakładce Columns zostało po staremu"* —
krok 11 nadał klasę `search` filtrowi w `SearchableComboBox`, a `TableColumnPicker` ma **własne dwa**
(`_tableFilter`, `_columnFilter`). ⚠ **Drugi raz w tym etapie ta sama klasa błędu**: reguła poprawna,
zastosowana w jednym miejscu z kilku (pierwszy raz — pasma chromy, §15.10.2). To jest cena reguł
opartych na tagowaniu i trzeba ją znać.

#### §15.11.4 ⛔ Domain Picker — RATYFIKOWANE: nie ujednolicać szerokości

> **Użytkownik:** *„To nie jest zwykły ComboBox. Ma dwa przyciski: Clear i DropDown. Dlatego naturalne
> jest, że oba są węższe niż pojedynczy przycisk zwykłego ComboBoxa. Dla mnie to jest poprawne."*

⭐ **To jest ważne rozstrzygnięcie o granicy spójności**: system ma ujednolicać to, co decyduje
o przynależności — **wysokość, ikony, padding** — a nie każdy wymiar. Ujednolicona została geometria
przycisków (20×20, oba tak samo) i rozmiar ikon (`Size.Icon.Sm`); **szerokość zostaje wolna**.
⛔ Nie „naprawiać" tego w przyszłości.

#### §15.11.5 ⭐⭐ Strażnik liczył stan DOCELOWY jako dług — korekta znaczenia licznika

Przy tym kroku `DesignTokenComplianceTests` zaświecił się na czerwono i **pokazał defekt w sobie**:
regex liczył **każde** przypisanie `FontSize=`, więc `FontSize="{DynamicResource Text.Status.Size}"` —
czyli dokładnie stan, do którego M2c ma doprowadzić — liczyło się **tak samo jak literał, który
zastąpiło**.
⛔ **Warunek wyjścia M2c był przez to nieosiągalny: w pełni zmigrowany widok raportowałby tę samą
liczbę co nietknięty.** Po korekcie (negatywne wyprzedzenie na `"{`) licznik mierzy to, co mówi jego
nazwa — **wartości lokalne**.
⚠ **Bazy nie są porównywalne z tymi z M2a** i jest to zapisane w samym teście. Pierwszy wiersz, który
spadł z powodu migracji, a nie pomiaru: `DataImportTabView` **86 → 82**.

Build 0/0; suite **7086** (7000 + 54 + 32); smoke czysty.

### §15.12 Krok 13 — regresja w drzewie i prawdziwa przyczyna nierównych stopek

⭐ **Oba zgłoszenia mają JEDNĄ przyczynę i jest nią kształt reguły, a nie żadna liczba.**

#### §15.12.1 ⛔ Reguła sformułowana PRZECZĄCO przecieka zawsze — i przeciekła drugi raz

Styl bazowy `Button` niósł od kroku 8 **geometrię akcji** (`MinHeight` 28 + `MinWidth` 80 +
`Pad.Button`) — czyli **wymiary stopki dialogu narzucone każdemu przyciskowi w aplikacji** — a każdy
przycisk, który akcją nie jest, musiał się z tego **wypisywać** (`Button.icon`, `Border.chrome Button`,
`DataGridCell Button`, `Button.caption`).

**Strzałka rozgałęzienia w drzewie deklaruje własne `Width=20 Height=20 Padding=0`. Avalonia klamruje
`Width` przez `MinWidth`, więc styl bazowy po cichu rozdął ją do 100×28** — stąd strzałka wjechała na
tekst i zniknął odstęp. ⚠ **To była regresja układu, nie estetyka**, i zgłoszenie było trafne.

⭐ **Poprawka jest zmianą KIERUNKU reguły, nie dopisaniem piątego wyjątku:** geometrię akcji niosą
teraz klasy, które akcją **są** — `.primary` i `.flat`. Wszystkie stopki dialogów w tej aplikacji są
nimi konsekwentnie oznaczone (sprawdzone), więc reguła nie traci zasięgu, a **przestaje przeciekać na
przyciski o własnym rozmiarze**. Styl bazowy niesie już tylko to, co jest prawdą o każdym przycisku:
font, promień, grubość krawędzi, wyrównanie treści.
⛔ **Nie wracać tu z `MinHeight`/`MinWidth`** — to jest dokładnie ten setter, który zepsuł drzewo.
⚠ Zapięte testem `AButtonThatDeclaresItsOwnSize_KeepsIt`, który sprawdza **obie** połowy: przycisk
z własnym rozmiarem go zachowuje, a zadeklarowana akcja nadal dostaje podłogę. Sam pierwszy warunek
przechodziłby po zwykłym skasowaniu reguły.

#### §15.12.2 ⭐ Stopki — podłoga leżała PONIŻEJ szerokości etykiety, którą miała wyrównać

Zmierzone przed poprawką:

```
Save   (primary)  =  80 px   ← siada na podłodze
Cancel (flat)     =  98 px   ← 72 tekst + 24 padding + 2 obramowanie
```

**Cała reszta mechanizmu renderowania była identyczna** — sprawdzone kolejno: styl bazowy, `.primary`,
`.flat`, `ContentPresenter`, `Padding` (`12,0` w obu), `BorderThickness` (`1`), `CornerRadius` (`3`),
`FontSize` (12), `HorizontalContentAlignment` (Center). **Jedyną różnicą była szerokość**, a jej
przyczyną to, że podłoga **80** leżała *poniżej* naturalnych **98** px słowa „Cancel": krótsza
etykieta siadała na podłodze, dłuższa ją przekraczała.

⭐ **Zasada, którą warto zapamiętać: podłoga wyrównuje tylko wtedy, gdy leży POWYŻEJ naturalnej
szerokości etykiet, które ma zrównać. Ustawiona niżej jest zapisem martwym** — wygląda jak reguła,
nie robi nic. `Size.ActionMinWidth` **80 → 100**; zmierzone po poprawce: **Save = Cancel = 100×28**.
⚠ To nadal **podłoga**, nie szerokość wspólna: naprawdę długa etykieta („Test connection") rozpycha
się dalej i to jest zamierzone.

⚠ **Trzy asercje trzeba było przy okazji naprawić i wszystkie trzy mierzyły nie ten podmiot:** dwie
porównywały przycisk **bezklasowy** z `.primary` (bezklasowy przestał być akcją), a jedna czytała tło
Bridge'a z `.flat`, które jest **celowo przezroczyste** — czyli mierzyła setter wariantu i nazywała to
dowodem na mapowanie. ⭐ Świadek mapowania musi być przyciskiem **bez wariantu**.

Build 0/0; suite **7087** (7000 + 54 + 33); smoke czysty.
⚠ Zapisane tu było 7088 / „54 + 34"; poprawione 2026-08-02 po pomiarze — §18.1.6.

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

---

## §17 🔒 M2b — PODSUMOWANIE ZAMYKAJĄCE (etap ZAKOŃCZONY 2026-08-02, zaakceptowany przez użytkownika)

> **Werdykt użytkownika:** *„M2b naprawdę zmieniło aplikację na plus i zaczyna wyglądać jak spójny
> produkt… Dla mnie M2b można uznać za zakończone."*
>
> ⭐ **Ostatnie zgłoszenie zostało ZAMKNIĘTE BEZ ZMIANY W KODZIE i to jest dobry wzorzec na przyszłość:**
> *„Save nadal wydaje się odrobinę większy, ale wynika to z tego, że jest przyciskiem Primary z pełnym
> wypełnieniem, podczas gdy Cancel jest Flat. Jeżeli pomiar potwierdza identyczne wymiary, nie chciałbym
> dalej z tym walczyć — na tym etapie łatwo byłoby pogorszyć spójność całego systemu."*
> Wymiary są identyczne co do piksela (100×28); różnica jest **percepcyjna i wynika z wypełnienia**,
> czyli z tego samego sygnału, który ma nieść priorytet. ⛔ Nie „naprawiać" tego.

### §17.1 Co zostało dostarczone — 21 iteracji, 14 commitów

| # | Krok | Commit | Wynik |
|---|---|---|---|
| 0 | style klasowe czytają katalog | `0bbc745` | dowód, że warstwa tokenów rozwija się w runtime; bajtowo neutralny |
| 1 | **`CheckBox`** | `26243cb` | **RB‑2 zamknięty** — znak 14 px, brak `MinHeight`, `Margin.MarkGap` |
| 2 | **RB‑4** | `a1d607a` | `ChromeStrongBrush` + `SurfaceRaisedBrush`, podział 14/14 |
| 3 | **skala szarości Light** | `7975aaa` | `#FCFCFD`, H‑7 domknięte (3,86 → 5,33), **V‑1 zmierzone** |
| 4 | **`ToolTip`** | `e5b010f` | **M‑2 zamknięte**; pierwszy nowy konsument `SurfaceRaised` |
| 5.1 + 5.1a | **`RadioButton`** | `cf23a4c`, `60f9278` | rodzeństwo `CheckBoxa`; koncentryczność zmierzona i zapięta |
| — | ⭐ **`FluentBridge`** | (w `9ec2c13`) | **decyzja architektoniczna etapu — §16** |
| 5.2 | **`TextBox`** | `9ec2c13` | **RB‑3 ruszone** — 32 → 24 px, tekst 14 → 12 |
| 5.3 | **`ComboBox`** | `3483296` | most zdał próbę skalowania — zero własnych szablonów |
| 5.4 | **`Button`** + 4 warianty | `267a4b8` | **H‑8 zamknięte** — 25 surowych przycisków Fluenta |
| 5.5 | **`NumericUpDown`** | `d2a2475` | trzy kontrolki zagnieżdżone; most działa kompozycyjnie |
| 5.6 | **`ToggleButton`** | `ce47aa7` | selektor typu = typ dokładny |
| 5.7 | **`Expander`** | `69ceff6` | ⭐ **trzecia trasa Bridge'a — alias zasobu** |
| 6 | **`ScrollBar`** | `7ab3d27` | **H‑10 zamknięte** — biały uchwyt na białym tle w Light |
| 7 | **DataGrid Standard** | `e95913b` | **§8.4 specyfikacji domknięte** + `Pad.CellEditor` |
| 8–10 | runda proporcji po QA | `e0a59fd` | dwie drabiny wysokości · Ustawienia jako panel referencyjny |
| 11 | druga runda QA | `1c7ccc1` | **kolor niesie priorytet, rozmiar nie** · `Border.chrome` |
| 12 | trzecia runda QA | `b568168` | linia pasma chromy · podłoga szerokości · korekta licznika |
| 13 | czwarta runda QA | `41e0cec` | **reguła przecząca → pozytywna** · regresja drzewa |

**Zamknięte pozycje audytu:** RB‑2 · RB‑3 · RB‑4 · H‑7 · H‑8 · H‑10 · M‑2 · §8.4 specyfikacji.
**Otwarte świadomie:** V‑1 (ratyfikowane — kolor komentarzy zostaje) · H‑1 (to jest **M2c**).

### §17.2 ⭐⭐ Decyzje architektoniczne — cztery, wszystkie przeżyją ten etap

1. **`FluentBridge` (§16)** — nie przestylowujemy Fluenta i nie kopiujemy jego szablonów, tylko
   **przepinamy go na nasz katalog**. Trzy trasy: metryki → setter stylu · kolory malowane przez
   wnętrze szablonu → Bridge · wartość trzymana przez szablon lokalnie → **alias zasobu**.
   Własny `ControlTemplate` wymaga **dwóch zmierzonych warunków** i spełniają je dokładnie dwie
   kontrolki (`CheckBox`, `RadioButton`).
2. **⭐ KONTENER ROZSTRZYGA WIELKOŚĆ, ELEMENT JĄ PRZYJMUJE.** Jeden mechanizm, pięć wystąpień:
   edytor w komórce siatki · nagłówek `Expandera` · pasmo chromy · stopka dialogu · pasek statusu.
   Element wypełnia kontener, a nie rozpycha go.
3. **⭐ REGUŁA MUSI BYĆ SFORMUŁOWANA POZYTYWNIE.** *„Wszystko jest X, chyba że…"* przecieka zawsze —
   przeciekło dwa razy (pasma chromy, strzałka drzewa), za drugim razem jako **regresja układu**.
   Geometria akcji siedzi dziś na klasach, które akcją **są**.
4. **⭐ WYSOKOŚĆ BIERZE SIĘ Z KONTEKSTU, NIGDY Z WARIANTU.** Wariant niesie **kolor**.

### §17.3 ⛔ Reguły ratyfikowane przez użytkownika — NIE otwierać ponownie

| # | Reguła | Gdzie |
|---|---|---|
| R1 | *„Projektujemy kontrolki, na których programista pracuje komfortowo 8 godzin dziennie"* — katalog nie ma wygrać z jakością produktu | §15.0 |
| R2 | Komponent ocenia się w **komplecie stanów** i w **obu motywach** | §15.2.1 |
| R3 | Nowa **rola** powstaje z użycia w kilku komponentach, nigdy z jednego przypadku | §15.2.1 |
| R4 | **Bridge nie jest drugim katalogiem tokenów** — wyłącznie mapowanie | §16.2 |
| R5 | **Kolor może określać priorytet akcji, ROZMIAR NIE** | §15.10.1 |
| R6 | **Ustawienia są panelem referencyjnym** — proporcje dopracowuje się tam, potem przenosi | §15.9 |
| R7 | **Nie łatać pojedynczych ekranów** — najpierw reguła Design Systemu, potem ewentualny wyjątek | §15.10 |
| R8 | **Kryterium odbioru: czy wygląda to jak dopracowana aplikacja komercyjna?** Pomiar jest narzędziem, nie argumentem końcowym | §15.11 |
| R9 | **Domain Picker** — nie ujednolicać szerokości; system ujednolica wysokość, ikony i padding | §15.11.4 |
| R10 | **Kolor komentarzy SQL zostaje** (V‑1) | §15.4.3 |
| R11 | **`Size.Row.Grid`** to osobna decyzja produktowa, nie poprawka `CheckBoxa` | §15.10.5 |

### §17.4 Katalog po M2b — co przybyło, co zniknęło

**Nowe role:** `Margin.MarkGap` · `Pad.CellEditor` · `Margin.OptionGap` · `Margin.LabelGap` ·
`Size.ControlToolbar` · `Size.ControlProminent` · `Size.ActionMinWidth` ·
`ScrollBarThumbColor` (+Hover, +Pressed) · `SurfaceRaisedBrush`.
**Wycofane (z strażnikiem `RetiredTokens`):** `ElevatedPanelBrush`/`Color` · `Size.ControlPrimary`.
⭐ **Skala bliskości**, pinowana jako **porządek**, nie liczby: podpis 2 < opcje 4 < pola 8.
⭐ **Dwie drabiny wysokości:** pola `Size.Control` 24 · akcje 22 (chroma) / 28 (dialog).

### §17.5 ⚠ Wnioski, które kosztowały najwięcej — do zapamiętania poza tym etapem

1. **⚠⚠ Arytmetykę §5.1 sprawdza się na SUMIE, nie na składniku.** Trzy potknięcia, trzecie
   **wysłane** i niewidoczne przez pięć iteracji (edytor w komórce prosił o 18 px przy 16 px miejsca).
   Złapał je dopiero test, którego przedmiotem była siatka **jako całość**.
2. **⚠⚠ Podłoga wyrównuje tylko wtedy, gdy leży POWYŻEJ naturalnej szerokości etykiet.** Ustawiona
   niżej wygląda jak reguła i nie robi nic (80 przy „Cancel" = 98).
3. **⚠⚠ Styl typu sięga do CUDZEGO szablonu — w obie strony.** Raz dał wysokość za darmo
   (`NumericUpDown` → wewnętrzny `TextBox`), raz wyśrodkował nagłówek `Expandera`.
4. **⚠ Deklarowana właściwość potrafi kłamać.** `RadioButton` raportował `MinHeight=0`, a żądał 32.
   **Sonduj drzewo, nie czytaj właściwości.**
5. **⚠ Kolejność deklaracji rozstrzyga między stylami o równej trafności** — trzy razy w etapie była
   treścią, a nie porządkiem.
6. **⚠ Wartość lokalna bije setter stylu.** Dopóki stoi w widoku, żadna reguła systemu nie działa.
   **To jest dokładnie teza M2c.**
7. **⚠ Test potrafi mierzyć nie ten podmiot i wtedy potwierdza defekt zamiast go łapać.** Cztery
   asercje w tym etapie mierzyły zamiar zamiast ograniczenia albo wariant zamiast mapowania.
8. **⚠ Reguła oparta na tagowaniu wymaga otagowania WSZYSTKICH instancji.** Dwa razy zabrakło jednej.

### §17.6 Stan liczbowy na wyjściu

Build **0/0** · suite **7087** (7000 + 54 + 33) · smoke czysty · drzewo czyste.
⚠⚠ Ten wiersz mówił **7088 (54 + 34)** i ta arytmetyka nigdy nie została przemierzona; poprawione
2026-08-02 (§18.1.6) po pomiarze na czystym `HEAD`. Przyczyną był filtr partycji wymieniający klasę
`ContextMenuPresentationTests`, która nie istnieje.
**Liczniki wartości lokalnych** (warunek wyjścia M2c, po korekcie znaczenia z §15.11.5):
`FontSize` **605 / 49 plików** · `FontFamily` **81 / 28** · `CornerRadius` **37 / 13**.

---

## §18 As-built — M2c (sweep de-lokalizacyjny)

### §18.0 Krok 0 — INWENTARZ (2026-08-02)

> **Cel kroku:** zmierzyć, *co faktycznie stoi w widokach*, zanim cokolwiek zostanie ruszone.
> Bez tego sweep jest zgadywaniem (handover §6/1). Ten krok **nie zmienia ani jednej linii kodu**.

#### §18.0.1 Pomiar wejściowy — potwierdzony

`FontSize` **605 / 49 plików** · `FontFamily` **81 / 28** · `CornerRadius` **37 / 13**.
Zgadza się co do sztuki z §17.6 i z bazą w `DesignTokenComplianceTests`.

**⭐ Rozkład wartości jest znacznie węższy, niż sugeruje liczba wystąpień.** 605 deklaracji
`FontSize` to **siedem różnych liczb**:

| Wartość | Wystąpień | Rola w katalogu |
|---|---|---|
| 11 | **345** | ⚠ **pięć ról** — `Text.Compact` · `Text.Grid` · `Text.GridHeader` · `Text.Status` · `Text.SectionHeader` |
| 12 | **155** | ⚠ **dwie role** — `Text.Application` · `Text.Toolbar` |
| 10 | 54 | `Text.Caption` |
| 13 | 40 | `Text.Code` — ⚠ ale tylko 25 z nich to edytor kodu |
| 9 | 7 | ⛔ **brak roli** |
| 14 | 3 | `Text.Title` |
| 23 | 1 | `Text.Display` |

⭐ **To jest dobra wiadomość i zła naraz.** Dobra: nie ma 605 decyzji do podjęcia, tylko siedem
liczb. Zła: **liczba nie wyznacza roli** — przy 11 px pięć ról ma tę samą wartość, więc 345
wystąpień wymaga rozstrzygnięcia *per miejsce*, a nie podmiany maszynowej. To jest dokładnie ten
podział, którego broni §3.3 („dwie role o tej samej liczbie to nie duplikat") — i to on decyduje,
że M2c musi iść widok po widoku, a nie automatem.

#### §18.0.2 ⭐⭐ POMIAR, KTÓRY ZMIENIA MECHANIKĘ CAŁEGO SWEEPU — i obala komentarz w teście

Sonda headless (`Window` + gołe kontrolki, wzorzec `DesignTokenApplicationTests`), Avalonia 12.0.3:

```
Window.FontSize              = 14
bare TextBlock               = 14      SelectableTextBlock = 14      TextBlock.subtle = 14
TextBox = ComboBox = CheckBox = Button = 12   (ze stylu M2b)
```

⚠⚠ **Goły `TextBlock` dziedziczy 14, nie 12.** Komentarz w `DesignTokenApplicationTests`
(*„Avalonia's default TextBlock size is 12"*) jest **fałszywy** — asercja `NotEqual(12d, …)` nadal
działa, ale jej uzasadnienie nie. Do poprawienia razem z pierwszą iteracją.

⭐ **Konsekwencja jest podstawowa: usunięcie `FontSize` z `TextBlocka` NIE JEST neutralne — podnosi
go do 14.** Sweep ma więc **dwa różne ruchy**, a nie jeden, i pomylenie ich jest defektem widocznym
gołym okiem:

| Ruch | Kiedy | Efekt |
|---|---|---|
| **USUŃ** | kontrolka dostaje **tę samą** wartość ze stylu M2b (`TextBox`/`ComboBox`/`CheckBox`/`RadioButton`/`Button`/`NumericUpDown` = 12) | zero zmian |
| **ZAMIEŃ na `{DynamicResource …}`** | wszystko inne, w szczególności każdy `TextBlock` | zero zmian |

#### §18.0.3 Klasyfikacja 605 deklaracji `FontSize` — cztery koszyki

| # | Koszyk | Ile | Działanie | Ryzyko wizualne |
|---|---|---|---|---|
| **A** | **Nadmiarowe** — kontrolka ma już tę wartość ze stylu | **77** | usuń | **żadne, dowodliwie** |
| **A?** | `DataGrid FontSize="11"` — `DataGridCell` i `DataGridColumnHeader` mają własne settery (11) | 25 | usuń **po weryfikacji** | do sprawdzenia (pusty stan, nagłówek grupy) |
| **B** | **Jedna rola, wprost** — `ae:TextEditor` 13 (25) · `TextBlock` 10 (49) · 23 (1) · 14 (3) | **78** | zamień na rolę | żadne |
| **C** | **Rola do rozstrzygnięcia** — wartość zostaje, rola wybierana per miejsce (całe 11 i 12) | **~390** | zamień na rolę | żadne, jeśli rola trafna |
| **D** | **Brak roli o tej wartości** | **~28** | ⛔ decyzja użytkownika | **zmiana wyglądu** |

**Koszyk A, dokładnie:** `TextBox` 36 · `ComboBox` 18 · `NumericUpDown` 11 · `CheckBox` 6 ·
`Button` 4 · `RadioButton` 2. ⭐ To jest jedyna część sweepu, która zmniejsza licznik **nic nie
dopisując** — i jednocześnie dowód, że M2b faktycznie zadziałało.

**Rozkład właścicieli (XAML, 585 z 605; reszta to 10 setterów w widokach i 11 wywołań w code-behind):**

```
TextBlock 11 x244   TextBlock 12 x57   TextBlock 10 x49   TextBox 12 x36   TextBox 11 x28
DataGrid 11 x25     ae:TextEditor 13 x25   ComboBox 12 x18   TextBlock 13 x15   Button 11 x13
NumericUpDown 12 x11   CheckBox 11 x9   TextBlock 9 x7   ae:TextEditor 12 x6   CheckBox 12 x6
ComboBox 11 x5   ListBox 11 x4   Button 12 x4   SelectableTextBlock 12 x4   DataGrid 12 x4
TextBlock 14 x3   CheckBox 10 x3   NumericUpDown 11 x3   RadioButton 12 x2   TextBlock 23 x1
```

Z tego `Classes="subtle"` niesie **73** wystąpienia 11 px i 12 wystąpień 12 px — czyli podpisy
pomocnicze są największą pojedynczą grupą i mają wspólny kształt.

#### §18.0.4 Reguła rozróżniania ról przy 11 i 12 px — propozycja do zatwierdzenia

Ponieważ liczba nie wyznacza roli, potrzebna jest **jedna reguła czytana z KONTEKSTU** (decyzja
architektoniczna §17.2/2 — kontener rozstrzyga). Proponowana, do zastosowania w każdej iteracji:

| Gdzie stoi element | Rola |
|---|---|
| w `DataTemplate` kolumny / komórki siatki | `Text.Grid` |
| w dolnym pasku statusu okna | `Text.Status` |
| nagłówek sekcji (SemiBold, nazywa temat) | `Text.SectionHeader` |
| **wszystko pozostałe przy 11** — panel, pasek, chip, podpis | `Text.Compact` |
| treść czytana świadomie — komunikat, opis, etykieta pola | `Text.Application` |
| tekst **w pasku narzędzi** przy 12 | `Text.Toolbar` |

⚠ Reguła jest sformułowana **pozytywnie** (§17.2/3): każdy wiersz mówi, czym element **jest**.
Domyślną odpowiedzią przy 11 px jest `Text.Compact`, a nie „coś, co nie jest siatką".

#### §18.0.5 🔒 TRZY USTALENIA — WSZYSTKIE TRZY RATYFIKOWANE PRZEZ UŻYTKOWNIKA (2026-08-02)

> **Werdykt użytkownika, przyjęty w całości — trzy razy „zgoda", z jednym wspólnym uzasadnieniem:**
> *„W tym etapie liczy się zachowanie identycznego wyglądu. Zaktualizuj dokumentację tak, aby
> odzwierciedlała stan faktyczny, a nie odwrotnie."*
> *„Nie tokenizujemy geometrii wynikającej z matematyki. Token ma opisywać rolę projektową, nie
> przypadkową wartość liczbową."*
> *„Jeżeli nie istnieje właściwa rola, nie wciskamy istniejącej tylko po to, żeby licznik spadł.
> Wolę uzasadnioną resztę niż błędną migrację."*

Wszystkie trzy to ten sam konflikt: **DoD 6 („wygląd identyczny") kontra zapis w katalogu.**
Katalog powstał w M2a jako *zamiar*, a pomiar M2c pokazuje, że w tych trzech punktach zamiar
oznacza **widoczną zmianę**. Rozstrzygnięcie jest w każdym z nich to samo: **wygrywa pomiar,
a dokument zostaje poprawiony w miejscu.**

**(1) `FontFamily` — M2c nie może zrobić prawie nic, wbrew planowi.**
Handover §4.1 stawia cel „`FontFamily` → 0", a komentarz w teście mówi *„M2c should drive this list
to empty"*. **Pomiar temu przeczy:** token `Font.Code` niesie `Cascadia **Mono**, Consolas, Menlo,
monospace`, a 65 z 81 wystąpień to `Cascadia **Code**,Consolas,Menlo,monospace`. **Ani jeden z 81
ciągów nie jest identyczny z tokenem.** Reguła handovera §4.3 („wolno podmienić tylko ciąg już
identyczny") daje więc **zero migracji**. `Cascadia Code` → `Cascadia Mono` to decyzja typograficzna
oddana backlogowemu sprintowi UX (ligatury), a nie sweep.
→ **Propozycja: `FontFamily` wypada z celów M2c**; 81 zostaje z komentarzem, a §4.1 handovera
i komentarz w teście zostają skorygowane. ⚠ Skutek uboczny: `Font.Code` **pozostaje tokenem bez
konsumenta** (dziś ma zero), czyli w kształcie, przed którym ostrzega reguła #233.

**(2) `CornerRadius` — §4.2.2 pomylił się w OBIE strony.**
Zapis §4.2.2 twierdzi: *„wszystkie wystąpienia 4 / 4.5 / 5 / 6 … są CHIPAMI, 3 to wyłącznie
powierzchnie"*. Zmierzone — nieprawda w obu połowach:

* **4.5 / 5 / 6 (7 wystąpień) to nie chipy, tylko GEOMETRIA**: `Width=10 Height=10 CornerRadius=5`
  i `Width=9 Height=9 CornerRadius=4.5` to **koła**, a `Height=12 CornerRadius=6` /
  `Height=10 CornerRadius=5` to **pigułki** pasków postępu. Promień = połowa boku. Wpisanie tam
  `Radius.Chip` (4) zamienia koło w kwadrat ze ściętymi rogami — zmiana widoczna.
* **4 (11 wystąpień) to w większości KARTY, nie chipy**: `BorderThickness="1" Padding="10,8"`,
  kontenery `ClipToBounds`, kafelek wiersza. Chipem jest tylko `AggregationBarView:55`.
  `Radius.Surface` (3) byłoby rolą trafną, ale zmienia 4 → 3.

→ **Propozycja:** migrować **wyłącznie 17 wystąpień `CornerRadius="3"` → `Radius.Surface`**
(zerowe ryzyko); 7 „geometrycznych" **zostawić lokalnie z komentarzem** (promień pochodzi z rozmiaru
elementu — to arytmetyka, nie rola); **10 kart przy 4 px oddać do przeglądu §13.3**, bo 3-czy-4 dla
karty jest decyzją produktową, nie sprzątaniem. §4.2.2 do skorygowania w miejscu.

**(3) Trzy grupy `FontSize` bez roli o tej wartości (28 wystąpień).**

* **`FontSize="9"` x7** — Typography §6 zapowiada zwinięcie do `Text.Caption` (10). Dwa z nich to
  glify (`▶`, `●`) i sam katalog je wyłącza; pozostałe **pięć** to realna zmiana 9 → 10.
* **`ae:TextEditor FontSize="12"` x6** — katalog ratyfikował **jeden** rozmiar kodu (13) słowami
  *„edytor kodu o dwóch rozmiarach w jednej aplikacji jest defektem, nie decyzją"*. ⚠ Pomiar mówi,
  że te sześć to edytory **w wierszu siatki** (kursory i podprogramy w trybie Easy, podgląd Global
  Search, szczegół Trace) — czyli 12 px jest tam **konsekwencją gęstości wiersza**, a nie dryfem.
  Zamiana na 13 to zmiana wyglądu i możliwy skok układu.
* **`TextBlock FontSize="13"` x15** — 13 px istnieje w katalogu **wyłącznie jako `Text.Code`**,
  a to jest treść (komunikat `ConfirmDialog`/`ChoiceDialog`, nagłówki Security Managera, wiodąca
  linia planu). Brak roli.

→ **Propozycja: wszystkie 28 zostają lokalne z komentarzem**, a decyzje „9 → 10", „12 → 13"
i „czy 13 px zasługuje na własną rolę" idą do przeglądu §13.3 / M5. To jest wprost „uzasadniona
reszta" z handovera §4.1 — i jedyny wariant, który nie łamie DoD 6.

#### §18.0.6 ⚠ Ustalenie poboczne — `ControlStyles.axaml` ma ten sam dług, tylko niewidoczny

Strażnik świadomie pomija `Themes/` (*„tam mieszka system"*). Pomiar pokazuje, że to **za szerokie
założenie**: `ControlStyles.axaml` ma dziś **literały** tam, gdzie powinien czytać rolę —
`TabItem` 13 · `TabItem.bottom-tab` 11 · `TabItem.sub-tab` 11 · `ContextMenu` 12 · `MenuItem` 12 ·
`PART_InputGestureText` 11 · `ListBox.code-action-menu ListBoxItem` 12 · `CornerRadius` 4/3/3.5.
To nie jest „katalog robiący swoje", tylko **katalog zapisany drugi raz** — i dokładnie ten kształt,
który §11 nazywa dryfem, tyle że w pliku wyłączonym z pomiaru.
→ **Propozycja: ująć te ~10 setterów w M2c jako osobną iterację** (`{DynamicResource Text.*}`,
wartości bez zmian). Nie wymaga zmiany strażnika ani nowej roli.

#### §18.0.7 Plan iteracji — PO wynikach inwentarza (zaktualizowany 2026-08-02)

⚠ Kolejność 1–3 wynika z wielkości skupisk (handover §4.2); 4–8 z **pokrewieństwa struktury**, bo to
ono decyduje, czy regułę wyboru roli da się zastosować spójnie, a nie liczba wystąpień.

| # | Zakres | `FontSize` | Co ta iteracja spotka na pewno |
|---|---|---|---|
| **1** | `DebuggerTabView.axaml` + `.axaml.cs` | 85 + 6 | 17 × `FontFamily` **zostaje** (§18.0.5/1); 2 × 9 px glif → koszyk D |
| **2** | `DataImportTabView.axaml` | 82 | dużo koszyka A (`TextBox`/`ComboBox`/`NumericUpDown` przy 12); 4 × `CornerRadius="3"` migrują |
| **3** | `PerformancePanelView.axaml` | 42 | glif 9 px i glif 13 px → koszyk D; `CornerRadius="6"` to **kapsuła** → zostaje |
| **4** | `ProcedureDetailTabView` + `FunctionDetailTabView` | 40 + 41 | bliźniacze — migrować **razem**, inaczej się rozjadą; po 2 edytory 12 px w wierszu siatki → koszyk D |
| **5** | edytory obiektów: Table 27 · Trigger 22 · View 20 · Package 17 · Domain 16 · Generator 15 · Exception 13 · Index 11 | 141 | wiele `ae:TextEditor` 13 → `Text.Code` (koszyk B, mechaniczne) |
| **6** | monitory: `SessionManager` 26 · `TraceMonitor` 17 · `SecurityManager` 17 | 60 | ⚠ tu siedzi **cała geometria** `CornerRadius` (koła 5 / 4.5, kapsuły) → zostaje; 8 × `TextBlock` 13 px → koszyk D |
| **7** | `MainWindow.axaml` 26 + `Controls/` 7 | 33 | ⭐ **powierzchnia trwała** (§0.1) — najwyższa staranność |
| **8** | dialogi — 18 plików po 1–9 | ~50 | ogon; `ConfirmDialog`/`ChoiceDialog` 13 px → koszyk D |
| **9** | literały w `ControlStyles.axaml` (§18.0.6) | ~10 setterów | poza licznikiem, ten sam dług |
| **10** | podniesienie bazy w `DesignTokenComplianceTests` do stanu faktycznego | — | krok końcowy; powód przy każdej pozostawionej pozycji |

⚠ **Iteracje 1–8 mają zaplanowane spotkanie z koszykiem D w siedmiu z ośmiu przypadków.** To nie jest
niepowodzenie sweepu, tylko konsekwencja R12: wyjątek z powodem jest **wynikiem**, nie resztą.

#### §18.0.8 ⭐⭐ R12 — ZMIANA CELU ETAPU, RATYFIKOWANA PRZEZ UŻYTKOWNIKA (2026-08-02)

> **Użytkownik, przy akceptacji inwentarza:** *„Nie traktuj celem etapu wyzerowania liczników.
> Celem jest usunięcie **nieuzasadnionych** wartości lokalnych. Jeżeli po zakończeniu zostanie
> niewielka liczba świadomie pozostawionych wyjątków z udokumentowanym uzasadnieniem, to M2c nadal
> będzie uznany za zakończony. Dokument ma odzwierciedlać architekturę produktu, a nie zmuszać
> produkt do spełniania wcześniejszych założeń, które zostały obalone pomiarami."*

**R12 dołącza do R1–R11 (§17.3) i jest wiążąca poza tym etapem.** Jest bezpośrednim rozwinięciem
R8 („pomiar jest narzędziem, nie argumentem końcowym") o jeden poziom: **licznik też jest tylko
narzędziem.** Trzy konsekwencje, wszystkie operacyjne:

1. ⛔ **Nie wolno migrować wartości na rolę, która do niej nie pasuje, żeby licznik spadł.**
   Błędna rola jest **gorsza** od wartości lokalnej: wartość lokalna jest widoczna jako dług,
   a błędna rola udaje, że długu nie ma — i przy pierwszej zmianie katalogu przesuwa ekran,
   o którym nikt nie pamięta.
2. ⭐ **Warunkiem wyjścia M2c NIE jest liczba, tylko zdanie przy każdej pozostałej wartości.**
   „605 → N" nie jest oceną etapu; oceną jest to, czy **każda** z pozostałych N ma powód zapisany
   na miejscu.
3. ⭐ **Kiedy pomiar obala zapis, poprawiamy zapis — w miejscu, z datą i powodem.** Dokument
   opisuje produkt, nie odwrotnie. Trzy takie korekty ten etap już wykonał (§18.0.9).

#### §18.0.9 Korekty dokumentów wymuszone pomiarem (krok 0)

| Gdzie | Było | Jest | Dowód |
|---|---|---|---|
| **§4.2.2** (ten dokument) | „wszystkie 4 / 4.5 / 5 / 6 są CHIPAMI" | 4.5/5/6 to **geometria** (koła, kapsuły); 4 to w większości **karty** | §18.0.5/2 |
| **`Typography.axaml`**, rola `Text.Caption` | „`FontSize=9` znika — 7 wystąpień wchodzi tutaj" | 9 → 10 to **zmiana wyglądu**; zostaje, decyzja oddana §13.3 | §18.0.5/3 |
| **`Typography.axaml`**, rola `Text.Code` | „13 px jednoznacznie … edytor o dwóch rozmiarach to defekt" | 6 edytorów przy 12 px stoi **w wierszu siatki** — to gęstość, nie dryf | §18.0.5/3 |
| **`Typography.axaml`**, rola `Font.Code` | „Zastępuje 7 rozjechanych ciągów" | **nie zastępuje żadnego w M2c** — token niesie `Mono`, widoki `Code` | §18.0.5/1 |
| **handover §4.1 / §4.3** | „`FontFamily` → 0 poza uzasadnionymi" | `FontFamily` **poza zakresem M2c** w całości | §18.0.5/1 |
| **`DesignTokenComplianceTests`** (komentarz `FontFamilyBaseline`) | „M2c should drive this list to empty" | lista zostaje; powód zapisany | §18.0.5/1 |
| **`DesignTokenApplicationTests`** (komentarz) | „Avalonia's default TextBlock size is 12" | **14** — zmierzone sondą | §18.0.2 |

#### §18.0.10 🔒 KROK 0 — PODSUMOWANIE ZAMYKAJĄCE (zaakceptowany przez użytkownika 2026-08-02, commit `20d4ad6`)

> **Werdykt użytkownika:** *„Dla mnie krok 0 jest zakończony i zaakceptowany."*

Krok 0 nie zmienił ani jednej linii kodu produkcyjnego. Zmienił natomiast **cztery zapisy, na których
etap miał się oprzeć** — i to jest jego właściwy wynik.

##### A. Założenia POTWIERDZONE

| Założenie | Dowód |
|---|---|
| Liczniki z §17.6 są dokładne | `FontSize` 605/49 · `FontFamily` 81/28 · `CornerRadius` 37/13 — zgodne co do sztuki ze strażnikiem |
| Teza etapu („wartość lokalna bije setter stylu") jest prawdziwa | 77 wystąpień to wartości **dokładnie równe** temu, co daje styl M2b — czyli martwe kopie, które i tak wygrywają |
| M2b faktycznie zadziałało | `TextBox`/`ComboBox`/`CheckBox`/`RadioButton`/`Button`/`NumericUpDown` **mierzą 12 px ze stylu**, bez żadnej pomocy widoku |
| Trzy największe skupiska są tam, gdzie wskazywał handover | `DebuggerTabView` 85 · `DataImportTabView` 82 · `PerformancePanelView` 42 |
| Podział ról §3.3 („dwie role o tej samej liczbie to nie duplikat") był słuszny | to właśnie on ratuje sweep przed podmianą maszynową — patrz B/2 |

##### B. Założenia OBALONE — pięć, każde z konsekwencją

1. **⚠⚠ „Goły `TextBlock` ma domyślnie 12 px"** (komentarz w `DesignTokenApplicationTests`).
   **Zmierzone: 14** (dziedziczone z `Window.FontSize`).
   → **Konsekwencja: sweep ma DWA ruchy, nie jeden.** Usunięcie lokalnego `FontSize` z `TextBlocka`
   to skok 11 → 14. Usuwamy tylko tam, gdzie styl daje **tę samą** wartość; wszędzie indziej
   **zamieniamy na odwołanie do roli**. Gdyby ten pomiar nie padł przed pierwszą iteracją, etap
   zacząłby się od masowej regresji typografii przy zielonym buildzie.

2. **⚠⚠ „Sweep jest w większości mechaniczny".**
   **Zmierzone: 605 deklaracji to siedem liczb, ale przy 11 px PIĘĆ ról ma tę samą wartość, a przy
   12 px dwie.** 500 z 605 wystąpień (83%) siedzi w tych dwóch liczbach.
   → **Konsekwencja: liczba nie wyznacza roli.** Podmiana `sed`-em zachowałaby wartość i wpisała złą
   rolę — **błąd niewidoczny na ekranie i niewidoczny w teście**, ujawniający się dopiero przy
   pierwszej zmianie katalogu. To jest formalne uzasadnienie rytmu „jeden widok = jedna iteracja".

3. **⚠⚠ „`FontFamily` da się doprowadzić do zera"** (handover §4.1 + komentarz w strażniku).
   **Zmierzone: token `Font.Code` niesie `Cascadia Mono`, a 65 z 81 wystąpień to `Cascadia Code`.
   Ani jeden z 81 ciągów nie jest identyczny z tokenem.**
   → **Konsekwencja: `FontFamily` wypada z zakresu M2c w całości** — nie z ostrożności, tylko
   arytmetycznie. Reguła „podmień tylko ciąg już identyczny" daje zero migracji.

4. **⚠⚠ „Wszystkie promienie 4 / 4.5 / 5 / 6 to chipy"** (§4.2.2).
   **Zmierzone: nieprawda w obu połowach.** `4.5`/`5`/`6` (7 wystąpień) to **geometria** — koła
   (`10×10`, promień 5) i kapsuły pasków postępu (`Height=12`, promień 6), gdzie promień jest połową
   boku. `4` (11 wystąpień) to w większości **karty**, nie chipy; chipem jest jedno wystąpienie.
   → **Konsekwencja: migruje wyłącznie 17 × `3`.** ⛔ Nie tokenizujemy geometrii wynikającej
   z matematyki — token opisuje rolę projektową, nie przypadkową liczbę.

5. **⚠ „Rozjazd rozmiaru = dryf do usunięcia"** (`Typography.axaml`, role `Text.Code` i `Text.Caption`).
   **Zmierzone: sześć edytorów przy 12 px stoi W WIERSZU SIATKI** (kursory i podprogramy w trybie
   Easy, podgląd Global Search, szczegół Trace), a dwa z siedmiu `FontSize="9"` to **glify**.
   → **Konsekwencja: to nie dryf, tylko decyzja kontenera** — ta sama zasada „kontener rozstrzyga
   wielkość", którą M2b ratyfikował jako decyzję architektoniczną (§17.2/2). Zostają.

⭐ **Wspólny mianownik wszystkich pięciu: katalog M2a był ZAMIAREM, a nie opisem kodu.** Zapisy
powstały, zanim ktokolwiek zderzył je z widokami. To nie jest zarzut wobec M2a — to jest powód,
dla którego krok 0 w ogóle był w planie.

##### C. Decyzje RATYFIKOWANE przez użytkownika

| # | Decyzja | Uzasadnienie użytkownika |
|---|---|---|
| 1 | **`FontFamily` poza zakresem M2c**; `Font.Code` zostaje bez konsumenta | *„Jeżeli `Cascadia Code` jest dziś świadomą decyzją, to nie zamieniamy jej na `Mono` tylko dlatego, że istnieje token."* |
| 2 | **Migruje wyłącznie `CornerRadius="3"`**; geometria i karty zostają | *„Nie tokenizujemy geometrii wynikającej z matematyki. Token ma opisywać rolę projektową, nie przypadkową wartość liczbową."* |
| 3 | **28 wystąpień `FontSize` bez roli zostaje z komentarzem** | *„Jeżeli nie istnieje właściwa rola, nie wciskamy istniejącej tylko po to, żeby licznik spadł. Wolę uzasadnioną resztę niż błędną migrację."* |
| 4 | **Dokumentacja opisuje stan zmierzony** — siedem zapisów poprawionych w miejscu (§18.0.9) | *„Zaktualizuj dokumentację tak, aby odzwierciedlała stan faktyczny, a nie odwrotnie."* |
| 5 | ⭐ **R12 — nowy cel etapu** (§18.0.8) | patrz niżej |

##### D. ⭐⭐ Czym jest R12 i dlaczego powstała

**R12:** *celem M2c jest usunięcie **nieuzasadnionych** wartości lokalnych, a nie wyzerowanie
licznika.*

**Powstała, bo krok 0 pokazał, że oba cele się rozjeżdżają — i to nie na marginesie, tylko na
około 130 wystąpieniach** (81 `FontFamily` + 20 `CornerRadius` + 28 `FontSize`). Przy celu
„licznik → 0" każde z nich domagałoby się migracji, a każda taka migracja **zmieniłaby wygląd
produktu** — czyli złamała DoD 6, warunek, który odróżnia M2c od M2b.

Formalnie R12 jest **rozwinięciem R8 o jeden poziom**. R8 mówi: *pomiar jest narzędziem, nie
argumentem końcowym*. R12 dodaje: **licznik też jest tylko narzędziem** — mierzy dług, ale nie
odróżnia długu od decyzji, więc nie może być kryterium odbioru.

⭐ **Najważniejsza konsekwencja, warta zapamiętania poza tym etapem: BŁĘDNA ROLA JEST GORSZA OD
WARTOŚCI LOKALNEJ.** Wartość lokalna jest widoczna jako dług — strażnik ją liczy, a kolejny
czytelnik ją widzi. Błędna rola **udaje, że długu nie ma**: przechodzi test, obniża licznik,
wygląda na porządek — i przesuwa ekran dopiero przy pierwszej zmianie katalogu, miesiące później,
daleko od przyczyny. To jest dokładnie ten sam kształt co gotcha #284 (skrót wpisany ręcznie
w podpowiedź przeżył zmianę gestu przy zielonym buildzie), tylko o warstwę wyżej.

⚠ **R12 nie rozluźnia etapu — przenosi rygor z liczby na zdanie.** Warunkiem wyjścia jest **powód
zapisany przy każdej pozostawionej wartości**, a to jest wymaganie trudniejsze do spełnienia niż
liczba, bo nie da się go osiągnąć hurtem.

##### E. Stan liczbowy po kroku 0

Build **0/0** · suite **7087** (⚠ patrz §18.1.6 — zapis „7088" był zawyżony o 1; zmierzone) ·
smoke czysty · drzewo czyste.
Liczniki **bez zmian** (krok 0 nie migrował niczego): `FontSize` **605 / 49** ·
`FontFamily` **81 / 28** · `CornerRadius` **37 / 13**.
**Przewidywany stan wyjściowy M2c:** `FontSize` ≈ **28 + reszta znaleziona w iteracjach** ·
`FontFamily` **81** (poza zakresem) · `CornerRadius` **20**.

---

### §18.1 Iteracja 1 — `DebuggerTabView` (2026-08-02)

> **Zakres:** `Views/DebuggerTabView.axaml` (85) + `Views/DebuggerTabView.axaml.cs` (6). Największe
> skupisko w aplikacji. `CornerRadius` w tych plikach **nie występuje** (0/0 — zweryfikowane, plik nie ma
> wpisu w `CornerRadiusBaseline`); `FontFamily` 17 + 2 **nietknięte** (poza zakresem etapu, §18.0.5/1).

#### §18.1.1 Wynik

| Plik | Przed | Po | Zmigrowane | Zostaje z powodem |
|---|---|---|---|---|
| `DebuggerTabView.axaml` | 85 | **4** | 81 | 4 |
| `DebuggerTabView.axaml.cs` | 6 | **1** | 5 | 1 |

Rozkład ról: `Text.Compact` 62 · `Text.Caption` 13 (+1 w code-behind) · `Text.SectionHeader` 2 (+1) ·
`Text.Application` 2 · `Text.Code` 1 · `Text.Grid` 1 · `Text.Compact` w code-behind 3.
**Ani jedna wartość liczbowa się nie zmieniła.**

#### §18.1.2 ⭐ Koszyk A był PUSTY — i to jest wynik pomiaru, nie brak pracy

W tym pliku **żadnej wartości nie dało się po prostu usunąć**. Powód jest systematyczny: **cały widok
debuggera stoi o jeden stopień gęściej niż domyślny styl M2b** — 11 px tam, gdzie styl daje 12 (pola
parametrów, filtr zmiennych, wejście Immediate, warunek breakpointu, wszystkie przyciski paska). To nie
dryf, tylko decyzja D15.3 Seam A („compact parameter row… tighter rows").

⚠ **Konsekwencja dla kolejnych iteracji:** przewidywanie z §18.0.3, że koszyk A zdejmie 77 wystąpień,
dotyczy plików o **domyślnej** gęstości. Tam, gdzie widok ma własną gęstość, koszyk A znika i wszystko
jest podmianą na rolę. Nie zakładaj proporcji z inwentarza per plik.

#### §18.1.3 ⭐⭐ NOWA REGUŁA RATYFIKOWANA — GLIF: funkcja, nie rozmiar

Krok 0 rozstrzygnął tylko glify przy 9 px („brak roli"). Ten plik zawiera 8 znaków-ikon renderowanych
jako tekst (`▾ ▸ ★ ☆ ▶ ◆ ● ± △`) przy 9/10/11/12 px, więc regułę trzeba było postawić.

> **Użytkownik, ratyfikując 2026-08-02:** *„Nie dzielmy glifów według rozmiaru, tylko według funkcji.
> Glif będący częścią tekstu → korzysta z roli tekstowej; glif będący elementem geometrii lub układu →
> zostaje lokalny jako wyjątek. To jest spójne z decyzją dotyczącą `CornerRadius` i nie tworzy sztucznych
> wyjątków."*

| Czym znak JEST | Działanie | Dlaczego |
|---|---|---|
| **część tekstu** — stoi w wierszu obok etykiety i jest wobec niej podrzędny | rola przy **tej samej** wartości | ma skalować się razem z tekstem, który adnotuje; inaczej pierwsza zmiana skali pisma rozjedzie wiersz |
| **element układu / geometrii** — rozmiar wyznacza pudełko, w które znak jest rysowany | **koszyk D** + komentarz | podpięcie pod rolę treści przycięłoby znak przy pierwszej zmianie katalogu |

⭐ Reguła jest sformułowana **pozytywnie** (decyzja architektoniczna §17.2/3) i jest **rozszerzeniem
ratyfikowanej zasady o geometrii `CornerRadius`** (§18.0.5/2) o jeden rodzaj wartości: *token opisuje
rolę projektową, nie zbieżną liczbę.* **Obowiązuje w iteracjach 2–9.**

#### §18.1.4 Pięć wyjątków — każdy z powodem zapisanym W MIEJSCU (R12)

| Miejsce | Wart. | Dlaczego rola nie pasuje | Rozstrzyga |
|---|---|---|---|
| `ParamRowTemplate` — `OriginLabel` („restored"/„assumed") | 9 | katalog nie ma roli o tej wartości; `Text.Caption` (10) zmieniłby wygląd | §13.3 |
| Variables — `★` / `☆` w `Button 18×18 Padding=0` | 12 | **element układu**: rozmiar dobrany do przycisku; `Text.Application` (dziś też 12) przycięłaby znak przy pierwszej zmianie skali | §13.3 |
| Call Stack — `▶` marker bieżącej ramki | 9 | dwa niezależne powody: brak roli przy 9 **i** kolumna o stałej szerokości 14 px | §13.3 |
| `.axaml.cs` — ciało karty Peek Frame (`TextBox`, mono) | 12 | powierzchnia **KODU**, a rola kodu (`Text.Code`) niesie 13 | §13.3 |

⚠ **Ostatni wpis to świadome odrzucenie koszyka A.** Technicznie usunięcie tej linii byłoby dziś
wizualnie neutralne (styl `TextBox` daje dokładnie 12), ale **podpięłoby podgląd kodu pod rolę TREŚCI
przez dziedziczenie** — czyli wprowadziło błędną rolę tylnymi drzwiami. To jest dokładnie kształt, przed
którym ostrzega R12, tyle że o warstwę niżej: rola nie musi być wpisana wprost, żeby była błędna.

#### §18.1.5 ⚠ `Text.Toolbar` (12 px) nie ma konsumenta, bo realny pasek narzędzi mierzy 11

Tablica ról §18.0.4 kieruje „tekst w pasku narzędzi" do `Text.Toolbar` (12). **Pasek poleceń debuggera
stoi na 11** — 11 przycisków `Button.flat` plus ich etykiety. Wpisanie tam `Text.Toolbar` byłoby zmianą
11 → 12, czyli złamaniem DoD 6, więc pasek dostał `Text.Compact` (rola chromy przy 11).

Zmierzone: `Text.Toolbar` **nie ma dziś ANI JEDNEGO konsumenta w całej aplikacji** (`grep -rn` trafia
wyłącznie w `Typography.axaml`).

> **Użytkownik, ratyfikując:** *„Nie twórz sztucznego konsumenta tylko dlatego, że rola istnieje. Jeśli
> obecny toolbar realnie pracuje na 11 px, to M2c nie jest miejscem na zmianę jego wyglądu. Niech
> `Text.Toolbar` pozostanie chwilowo bez konsumenta i wrócimy do tego podczas M3 (Toolbar)."*

⛔ Nie „naprawiać" tego w M2c. Pozycja przechodzi do **M3.2 (Toolbar)** wraz z pytaniem, czy paski
narzędzi mają pracować na 11 czy na 12.

#### §18.1.6 ⚠⚠ Pomiar obalił zapis — suite ma **7087**, nie 7088, a filtr partycji nazywa nieistniejącą klasę

Zapis §17.6 / §18.0-E i handover §1 mówią **7088 = 7000 + 54 + 34**. Zmierzone: **7000 + 54 + 33 = 7087**.

**Sprawdzone, że to NIE pochodzi z tej iteracji** — pomiar wykonano na czystym `HEAD` (`git stash`,
przebudowa, trzy partycje) i dał identyczne 7000 + 33 + 54. Zapis był zawyżony wcześniej.

⭐ **Przyczyna jest w drugim zapisie:** filtr partycji (CLAUDE.md + handover §8.3/15) wymienia **pięć**
klas headless, w tym `ContextMenuPresentationTests` — **taka klasa nie istnieje**. Jej testy zostały
w którymś momencie wchłonięte przez `ConnectionExpandBindingProbe` (mieszka tam dziś
`TheSameMenuOperationAlwaysCarriesTheSameIcon`), a nazwa w filtrze została. Po stronie *wykluczenia*
jest to nieszkodliwe (wyklucza nic), ale arytmetyka „54 + 34" nigdy nie została przemierzona.

→ **Poprawione we wszystkich dokumentach** — na wyraźne polecenie użytkownika przy odbiorze iteracji 1:
*„Jeżeli rzeczywisty wynik to 7087, a nie 7088, to popraw wszystkie miejsca, w których ta liczba występuje.
Dokumentacja ma odzwierciedlać stan faktyczny, a nie historyczne założenia."* Objęte: `CLAUDE.md` (liczba,
lista klas **pięć → cztery**, filtr), handover M2c §1 + §8.3/15 (liczba i filtr), `product-polish.md` §15.11.7
+ §17.6 + §18.0-E, handover M2b §1 (baseline). Handover M2a jest zamknięty, więc jego filtr **zostaje jako
zapis historyczny, ale z ostrzeżeniem „nie kopiuj"** — jest nieaktualny również z drugiego powodu (brakuje
w nim `DesignTokenApplicationTests`).

#### §18.1.7 Mechanizm dla code-behind — `BindFontSize`, bliźniak istniejącego `BindBrush`

W C# nie ma `{DynamicResource}`; odpowiednikiem jest obserwabla zasobu. Plik **miał już ten wzorzec** dla
pędzli, więc rola dostała jednolinijkowego bliźniaka obok:

```csharp
private void BindFontSize(Control control, string roleSizeKey)
    => control.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable(roleSizeKey));
```

⚠ Stoi **poza `#if DEBUG`** (karta Peek Frame jest poza nim, `BindBrush` w środku). ⚠ `Mono(...)`
przestało być `static` — czyta zasób przez `this`, dokładnie jak `BindBrush`. ⚠ Nazwa metody nie wpada
w regex strażnika (`\bFontSize\s*=` nie ma granicy słowa w `BindFontSize`, a `FontSizeProperty` nie ma po
sobie `=`) — sprawdzone licznikiem. **Ten sam pomocnik obsłuży pozostałe wywołania w code-behind
(iteracje 7 i 8).**

#### §18.1.8 ⚠ Zgłoszone, NIE zrobione — `FontWeight` zostaje literałem

Trzy miejsca (`Variables` header, nagłówki grup, nagłówek karty Peek) deklarują `SemiBold` lokalnie, a
rola `Text.SectionHeader` **też** niesie `SemiBold`. Zmigrowano wyłącznie `Size`, bo `FontWeight` nie jest
właściwością liczoną przez strażnika, nie ma go w DoD ani w planie iteracji.

⚠ Warto to widzieć jako dług: `Typography.axaml` definiuje rolę jako **komplet** (rodzina + rozmiar +
waga + interlinia), więc sweep po samym rozmiarze zostawia połowę. **Decyzja o zakresie należy do
użytkownika i dotyczy całego M2c, nie jednego pliku.**

⚠ Rozważono i **odrzucono** dopisanie `FontSize` do klasy `.subtle` w `ControlStyles.axaml` (zdjęłoby
12 deklaracji jednym setterem): klasa jest w tym pliku używana przy 10 **i** przy 11, więc niesie
**kolor**, nie rolę — setter zmieniłby wygląd połowy użyć.

#### §18.1.9 Stan po iteracji 1

Build **0/0** · suite **7087** zielony w trzech partycjach (7000 + 33 + 54) · smoke czysty.
Liczniki: `FontSize` **605 → 519** · `FontFamily` **81** (poza zakresem) · `CornerRadius` **37**.
⚠ Liczba plików zostaje **49** — oba pliki debuggera nadal niosą wartości lokalne (4 i 1), świadomie
i z powodem; pod R12 to jest **wynik**, nie reszta do wyzerowania.

⭐ **Kontrola, która w tym etapie zastępuje „zielony build":** wszystkie 6 użytych kluczy ról
zweryfikowano wobec `Typography.axaml` skryptem (`{DynamicResource}` **nie rzuca** przy literówce —
pułapka #14 z handovera §8.2, największe ryzyko sweepu), a aplikację uruchomiono.
⚠ **Porównanie wizualne w obu motywach należy do QA użytkownika** — narzędzia headless go nie dają.

---

### §18.2 Iteracja 2 — `DataImportTabView` (2026-08-02)

> **Zakres:** `Views/DataImportTabView.axaml` — 82 `FontSize` + **4 `CornerRadius`** (pierwsze spotkanie
> etapu z promieniem). `FontFamily` 2 nietknięte.

#### §18.2.1 Wynik

| Właściwość | Przed | Po | Usunięte | Na rolę | Zostaje z powodem |
|---|---|---|---|---|---|
| `FontSize` | 82 | **4** | **35** | 41 | 4 |
| `CornerRadius` | 4 | **0** | — | 4 | 0 |

Role: `Text.Application` 22 · `Text.Compact` 18 · `Text.Code` 1 · `Radius.Surface` 4.
**Ani jedna wartość liczbowa się nie zmieniła.** Wpis `CornerRadius` dla tego pliku **znika z bazy**.

#### §18.2.2 ⭐ Dokładna odwrotność iteracji 1 — i to potwierdza tezę etapu

W debuggerze koszyk A był **pusty**; tutaj jest **największy w całym etapie: 35 wartości po prostu
usunięto**. Powód jest ten sam, tylko odwrócony — Data Import stoi na **domyślnej** gęstości aplikacji, więc
`ComboBox` (15) · `CheckBox` (6) · `TextBox` (5) · `Button` (4) · `NumericUpDown` (3) · `RadioButton` (2)
niosły dokładnie te 12 px, które daje im styl M2b. To są **martwe kopie**, które i tak wygrywały
z setterem — dowód tezy §2 w jej najczystszej postaci.

⚠ Razem obie iteracje ustalają regułę praktyczną na resztę etapu: **proporcja koszyków wynika z gęstości
widoku, nie z jego wielkości.** Widok o własnej gęstości → same podmiany; widok domyślny → dużo usunięć.

#### §18.2.3 ⚠⚠ STRAŻNIK LICZY RÓWNIEŻ PROZĘ W KOMENTARZU — dwa z 82 nie były wartościami

`Measure` czyta plik **regexem po surowym tekście**; `WithoutComments` istnieje w tej klasie, ale wyłącznie
dla skanu `RetiredTokens`. Komentarz z M2b kroku 12 opisywał naprawiony dług **cytując składnię atrybutu**
i przez to **liczył się jako dwa lokalne `FontSize`** w pliku, w którym ta belka została naprawiona.

⭐ **Złapane dwa razy w jednej iteracji** — bo pierwsza redakcja mojego własnego komentarza do wyjątku
`DataGrid` popełniła dokładnie ten sam błąd i podniosła licznik z 4 na 8. Oba komentarze przeredagowano tak,
żeby mówiły „12 px", a nie cytowały atrybutu.

⚠ **Wniosek dla iteracji 3–9: pisząc uzasadnienie wyjątku, NIE cytuj składni atrybutu.** Rozważono zmianę
`Measure`, żeby pomijała komentarze — **odrzucone**: to zmiana semantyki strażnika dotykająca wszystkich
baz naraz, a nie sprzątanie w widoku. Zgłoszone jako obserwacja do przeglądu §13.3.

#### §18.2.4 ⛔ `Text.Toolbar` NIE dostała konsumenta, choć tu by pasowała

Pasmo poleceń Data Import (band B) ma **trzy elementy tekstowe przy dokładnie 12 px** — licznik postępu,
znacznik otwartej transakcji i etykieta pozycji listy profili. To jest wprost definicja roli z §18.0.4
(„tekst w pasku narzędzi przy 12"), a wartość jest **identyczna**, więc migracja byłaby bezpieczna.

Mimo to poszły na `Text.Application`.

> **Użytkownik, przy odbiorze iteracji 1:** *„`Text.Toolbar` zostawiamy bez zmian. Zostaje bez konsumenta
> do M3.2 zgodnie z wcześniejszą decyzją. Nie twórz sztucznego użycia tylko po to, żeby rola nie była pusta."*

⚠ **To jest odstępstwo od tablicy ról §18.0.4 i jest świadome** — instrukcja użytkownika jest nadrzędna, a
M3.2 dostaje pełny obraz: **pasek debuggera pracuje na 11, pasek Data Import na 12.** Dopiero ta para mówi,
że pytanie „ile mierzy pasek narzędzi w EmberTernie" nie ma dziś jednej odpowiedzi — i że rozstrzygnięcie go
przez wpisanie roli w jednym z dwóch miejsc **pogłębiłoby** rozjazd zamiast go pokazać.

#### §18.2.5 Cztery wyjątki — wszystkie tego samego rodzaju

Cztery `DataGrid` deklarują **12**, a rolą siatki danych jest `Text.Grid` niosąca **11**. Podmiana zmieniłaby
liczbę, usunięcie oddałoby wartość domyślnej wielkości okna (14).

⭐ **Zastane, nie wprowadzone: zadeklarowane 12 jest tam w dużej mierze bezczynne.** Style `DataGridCell`
(11) i `DataGridColumnHeader` (11) wygrywają nad dziedziczeniem, więc **komórki i tak renderują się na 11** —
deklaracja rządzi tylko tym, czego te dwa style nie obejmują. „W dużej mierze bezczynne" to jednak nie
„dowodliwie bezczynne", a M2c nie zmienia wyglądu na podstawie prawdopodobieństwa. Powód stoi przy każdej
z czterech siatek; rozstrzyga przegląd §13.3.

#### §18.2.6 `CornerRadius` — pierwsze zastosowanie ustalenia z kroku 0

Wszystkie cztery wystąpienia to `3` na **kontenerach** (ramka siatki typów, ramka siatki mapowania, ramka
podglądu po konwersji, ramka podglądu DDL) — czyli dokładnie ta jedna grupa, którą krok 0 dopuścił do
migracji (§18.0.5/2). Żadnej geometrii ani karty w tym pliku nie ma, więc wpis znika z bazy w całości.

#### §18.2.7 Stan po iteracji 2

Build **0/0** · suite **7087** zielony w trzech partycjach (7000 + 33 + 54) · smoke czysty.
Liczniki: `FontSize` **519 → 441** · `FontFamily` **81** (poza zakresem) · `CornerRadius` **37 → 33**.
Wszystkie użyte klucze ról zweryfikowane wobec `Typography.axaml` / `Tokens.axaml`; aplikacja uruchomiona.

---

### §18.3 Iteracja 3 — `PerformancePanelView` (2026-08-02)

> **Zakres:** 42 `FontSize` + 4 `CornerRadius`. `FontFamily` 3 nietknięte.

#### §18.3.1 Wynik

| Właściwość | Przed | Po | Na rolę | Zostaje z powodem |
|---|---|---|---|---|
| `FontSize` | 42 | **6** | 36 | 6 |
| `CornerRadius` | 4 | **2** | 2 | 2 |

Role: `Text.Compact` 23 · `Text.Caption` 9 · `Text.Application` 3 · `Text.Title` 1 · `Radius.Surface` 2.
Koszyk A **pusty** (ten panel nie ma ani jednej kontrolki formularza). Wartości bez zmian.

#### §18.3.2 ⭐⭐ TRZECIA POSTAĆ TEGO SAMEGO KONFLIKTU — i to już jest wzorzec, nie przypadek

Trzy iteracje, trzy razy ta sama sytuacja: **rola, która pasuje FUNKCJĄ, niesie inną LICZBĘ.**

| Iteracja | Element | Ma | Rola funkcjonalna | Rozstrzygnięcie |
|---|---|---|---|---|
| 1 | pasek poleceń debuggera | 11 | `Text.Toolbar` = **12** | `Text.Compact` (chroma przy 11) |
| 2 | pasmo poleceń Data Import | 12 | `Text.Toolbar` = 12 ✔ | `Text.Application` — **na wyraźną instrukcję użytkownika** |
| 3 | trzy nagłówki sekcji | 12 + SemiBold | `Text.SectionHeader` = **11** | ⛔ zostają lokalne z powodem |

⭐ **Wniosek, który wychodzi poza ten etap: katalog M2a opisał role przez JEDNĄ wartość każdą, a produkt
używa niektórych z nich w dwóch rozmiarach.** To nie jest dryf do wyprostowania sweepem — to pytanie
projektowe („ile mierzy pasek narzędzi", „ile mierzy nagłówek sekcji"), którego M2c z definicji nie
rozstrzyga, bo każde rozstrzygnięcie zmienia wygląd. Sweep robi tu rzecz najbardziej użyteczną, jaką może:
**zostawia obie strony widoczne** — jedna jako rola, druga jako wartość lokalna z zapisanym powodem — żeby
przegląd §13.3 zobaczył rozjazd zamiast zastanego kompromisu.

⚠ **Nagłówki sekcji są tu najostrzejszym przypadkiem**, bo kanoniczny `TextBlock.group-header` (styl M2b)
czyta `Text.SectionHeader` = 11, a ten panel nigdy z tej klasy nie skorzystał i stoi na 12. Wpisanie
`Text.Application` opisałoby nagłówek jako treść; wpisanie `Text.SectionHeader` zmieniłoby 12 → 11.
Zgodnie z R12 obie odpowiedzi są gorsze od zapisanego powodu.

#### §18.3.3 Sześć wyjątków `FontSize`

| Element | Wart. | Powód |
|---|---|---|
| nagłówki „Findings", „Table access" oraz tytuł karty ustalenia | 12 | nagłówek przy roli nagłówka niosącej 11 (§18.3.2) |
| `PlanLead` — wiodąca linia planu | 13 | treść, a katalog ma przy 13 wyłącznie `Text.Code` (grupa „TextBlock 13 px" z §18.0.5/3) |
| znak oceny `●` | 13 | mark strojony do wiersza (sąsiedni tekst ma 14), brak roli przy 13 dla tekstu |
| znak skanu sekwencyjnego `●` | 9 | brak roli o tej wartości |

#### §18.3.4 `CornerRadius` — obie ratyfikowane kategorie w jednym pliku

Ten widok pokazuje wszystkie trzy przypadki naraz i potwierdza obie połowy korekty §18.0.5/2:
* **kapsuła** — `Height="12" CornerRadius="6"`: promień to połowa wysokości, czyli **arytmetyka**. Zostaje.
* **karta** — obrys + `Padding="8,6"` przy promieniu 4: `Radius.Surface` niesie 3, więc migracja zmieniłaby
  wygląd. Zostaje, decyzja „karta: 3 czy 4" należy do §13.3.
* **powierzchnie** — dwa promienie 3 → `Radius.Surface`.

⚠ **Zgłoszenie do §13.3:** jeden z tych dwóch trójek to funkcjonalnie **chip** (tło w kolorze wagi,
`Padding="4,0"`, etykieta 10 px), a `Radius.Chip` niesie **4**. Poszedł na `Radius.Surface`, bo krok 0
ratyfikował regułę *„migruje wyłącznie `CornerRadius="3"` → `Radius.Surface`"* i bo to jedyny wariant
zachowujący wartość. Wykonanie ratyfikowanej reguły, ale przypadek, którego ona nie przewidziała.

#### §18.3.5 Stan po iteracji 3

Build **0/0** · suite **7087** zielony w trzech partycjach · smoke czysty.
Liczniki: `FontSize` **441 → 405** · `FontFamily` **81** · `CornerRadius` **33 → 31**.

---

### §18.R ⭐⭐ REJESTR KOLIZJI „rola pasuje funkcją, ale niesie inną liczbę"

> **Ratyfikowane przez użytkownika 2026-08-02, po iteracji 3 — wariant „1 + 3":**
> *„Jeżeli rola funkcjonalnie pasuje, ale wymagałaby zmiany liczby, aby zachować wygląd, to na tym etapie
> nie zmieniamy katalogu tylko po to, żeby zmniejszyć liczbę wyjątków. Zostawiamy wartość lokalną
> z uzasadnieniem, zapisujemy ją do rejestru kolizji, a po zakończeniu całego M2c przeglądamy wszystkie
> takie przypadki jednocześnie podczas przeglądu katalogu (§13.3)."*
>
> ⭐ **Zdanie, które ustawia cały etap:** *„M2c nie projektuje nowego systemu typografii. M2c jedynie
> migruje aplikację do systemu, który został zaakceptowany w M2a."* ⛔ **Katalog pozostaje ZAMROŻONY** —
> nowa rola (np. `Text.SectionHeader.Large`) może powstać dopiero jako świadoma decyzja projektowa
> podjęta na pełnym obrazie aplikacji, nigdy jako reakcja na pojedynczą iterację.

**Zasada prowadzenia:** kolejna kolizja tego samego typu **nie wymaga pytania** — trafia tutaj i sweep
idzie dalej. Rejestr jest wejściem do przeglądu §13.3.

| # | Iter. | Element | Ma | Rola funkcjonalna | Co zrobiono |
|---|---|---|---|---|---|
| K1 | 1 | pasek poleceń debuggera (11 przycisków + etykiety) | 11 | `Text.Toolbar` = **12** | `Text.Compact` (chroma przy 11) |
| K2 | 2 | pasmo poleceń Data Import — 3 elementy | 12 | `Text.Toolbar` = 12 ✔ | `Text.Application` — **instrukcja użytkownika: rola zostaje bez konsumenta do M3.2** |
| K3 | 3 | „Findings", „Table access", tytuł karty ustalenia | 12 SemiBold | `Text.SectionHeader` = **11** | ⛔ lokalnie z powodem |
| K4 | 3 | `PlanLead` — wiodąca linia planu | 13 | brak roli tekstowej przy 13 | ⛔ lokalnie z powodem |
| K5 | 3 | chip wagi ustalenia — promień | 3 | `Radius.Chip` = **4** | `Radius.Surface` (ratyfikowana reguła „każde 3 → Surface") |
| K6 | 4 | nagłówek karty tabeli w obu bliźniakach | 12 SemiBold | `Text.SectionHeader` = **11** | ⛔ lokalnie z powodem |
| K7 | 4 | `MinHeight` nagłówka `Expandera` w obu bliźniakach | 26 | `Size.Control` = **24** (przez `ExpanderMinHeight`) | ⛔ lokalnie z powodem |
| K8 | 6 | `TextBlock.section` — nagłówek panelu szczegółów (Session + Trace) | 12 SemiBold | `Text.SectionHeader` = **11** | ⛔ lokalnie z powodem |
| K9 | 9 | `TabItem` — etykieta ⚠ **dolnego panelu i pod‑zakładek**, NIE paska zakładek (`ControlStyles.axaml`) | 13 | brak roli tekstowej przy 13 | ⛔ lokalnie z powodem → **§13.3** |
| K10 | 9 | `TabItem.bottom-tab` / `.sub-tab` — kształt zakładki | promień 4 | `Radius.Chip` = 4, ale zakładka chipem nie jest | ⛔ lokalnie z powodem → **§13.3** |
| K11 | **M3.1d** | chip transakcji — odstęp kropka ↔ tekst (`MainWindow.axaml`) | `Spacing` 5 | `Space.Sm` = **6** | ⛔ lokalnie z powodem → **§13.3** |
| **K12** | **M3.3a** | przycisk aktywujący zakładkę roboczą — `Padding` | 8,4 | `Pad.Tab` = **10,4** | ⛔ lokalnie z powodem → **§13.3** |
| **K13** | **M3.3a** | przycisk zamykania zakładki — `Padding` | 4,2 | `Pad.ButtonIcon` = **6,0** | ⛔ lokalnie z powodem → **§13.3** |
| **K14** | **M3.3a** | przycisk zamykania zakładki — `Margin` prawy | 3 | brak roli (`Space` daje 2/4/6) | ⛔ lokalnie z powodem → **§13.3** |
| **K15** | **M3.4a** | wiersz drzewa — ikona węzła **i** odstęp ikona ↔ etykieta (`MainWindow.axaml`, ×3 szablony) | 15 px · `Spacing` 5 | `Size.Icon` = **14** · `Space.Xs` = **4** | ⛔ lokalnie z powodem → **§13.3** |

⭐⭐ **K12–K14 są pierwszą trójką, którą łączy JEDEN skutek produktowy, a nie jeden rodzaj wielkości:**
wszystkie trzy zmieniają **szerokość zakładki**, czyli **ile zakładek mieści się w wierszu**. To już nie jest
pytanie o zgodność z katalogiem, tylko o **gęstość paska** — a ta jest decyzją użytkownika, bo D6/§8.1 chroni
pełną czytelność nazw, a M3.1a osiągnęło swoje, właśnie **zdejmując** podłogę szerokości. Wzięcie samego
`Pad.Tab` poszerza każdą zakładkę o 4 px.
⚠ **Dlatego idą na §13.3 RAZEM i jako jedno pytanie**, nie trzy — dokładnie tak, jak K11 idzie tam w parze
z paddingiem badge'a DEV MODE. Rozstrzygać je pojedynczo znaczyłoby trzy razy zmienić gęstość paska, nie
oglądając jej ani razu jako całości (**R17**).
⭐⭐ **K15 (M3.4a) POWTARZA TEN SAM KSZTAŁT O JEDNĄ POWIERZCHNIĘ DALEJ — i to jest potwierdzenie, że
K12–K14 nie były przypadkiem.** Ikona 15 px i `Spacing` 5 px w wierszu drzewa to znowu **dwie wielkości
różnych rodzajów** (rozmiar i odstęp) o **jednym skutku produktowym**: razem wyznaczają **gęstość
najgęstszego widoku aplikacji**. Wzięcie obu ról naraz zwęża treść wiersza o 2 px.
⚠ **Trzy powody, dla których nie wolno tego wziąć „przy okazji" M3.4a** — ten sam próg, który zatrzymał
decyzję **DB** przy wysokości wiersza:
1. **to decyzja produktowa, nie porządkowa** — gęstość drzewa użytkownik ogląda cały dzień;
2. ⚠⚠ **to nie jest problem TEGO ekranu**: `Width="15"` i `Spacing="5"` mają w aplikacji **112 wystąpień
   w 17 plikach**, więc zmiana wyłącznie w drzewie byłaby łataniem pojedynczego widoku (**R7**) i **rozjechałaby
   drzewo z resztą aplikacji** — czyli pogorszyła spójność w imię zgodności z katalogiem (**R17**);
3. **R12** — błędna rola jest gorsza od wartości lokalnej: wartość z powodem widać jako dług, rola udaje,
   że długu nie ma.
⭐ Dlatego K15 idzie na §13.3 **jako jedno pytanie o gęstość drzewa**, a sweep tych 112 literałów — jeśli
w ogóle — jest robotą **M4.3**, razem z `Size.Icon` (64 literały, znalezisko M3.3a). Obie listy opisują
**tę samą** app-wide decyzję o rozmiarze ikony i najprawdopodobniej trzeba je zadać razem.

⚠⚠ **K12 ISTNIAŁO PRZEZ JEDNĄ ITERACJĘ I ZOSTAŁO WYCOFANE — wpis zachowany jako zapis, nie jako dług.**
M3.2a dało parze Execute/Cancel wspólną podłogę `MinWidth="156"` i odnotowało kolizję z
`Size.ActionMinWidth` (100). ⛔ **Użytkownik wycofał samą podłogę po obejrzeniu w działającej aplikacji**
(§19.11), więc kolizja przestała mieć przedmiot: nie ma wartości lokalnej, nie ma czego rozstrzygać
na §13.3. ⭐ Warto natomiast zachować ustalenie, które przy tej okazji padło i **obowiązuje niezależnie
od losu podłogi**: `Size.ActionMinWidth` jest w `Tokens.axaml` opatrzona ⛔ *„Chroma, przycisk ikonowy
i komórka siatki jej NIE biorą"*, **a jej wartość 100 leży poniżej naturalnych 156 px przycisku
Execute** — więc gdyby ktoś kiedyś sięgnął po nią „dla porządku" w pasku narzędzi, dostałby martwy zapis
wyglądający na regułę. To ta sama pułapka, którą tamten komentarz opisuje na własnym przykładzie
(80 przy „Cancel" o naturalnych 98).

⚠⚠ **KOREKTA Z M3.3a (2026-08-03): K9 i K10 NIE DOTYCZĄ PASKA ZAKŁADEK ROBOCZYCH.** Rejestr indeksował po
nazwie („zakładka"), a produkt ma **dwa** systemy zakładek: pasek dokumentów to `ItemsControl` + `WrapPanel`
+ szablon `Border`/`Button` (etykieta na `Text.Compact.Size` = 11, **żadnego `CornerRadius`**), natomiast
K9/K10 stoją na `TabItem` — czyli na **dolnym panelu i pod‑zakładkach edytorów**. ⭐ To pułapka 19 w wydaniu
rejestrowym: **nazwa jest nośnikiem dwóch różnych rzeczy**. Instrukcja „K9/K10 zostają" obowiązuje dalej, ale
z innego powodu — nie były w przedmiocie M3.3.

⚠ **K11 jest pierwszą kolizją spoza M2c i pierwszą dotyczącą ODSTĘPU, a nie typografii ani promienia** —
rejestr okazał się szerszy niż licznik, który go zrodził. Różnica to **1 px**, więc pokusa „po prostu
weź rolę" jest tu największa; zamiana zmieniłaby jednak wygląd chipa **już odebranego przez użytkownika**,
a to jest dokładnie ten rodzaj cichej zmiany, przed którym broni R12. ⭐ Pytanie *„czy chipy mają wspólną
metrykę"* jest wspólne z §19.3.4 (padding badge'a DEV MODE) i idzie do §13.3 **razem z nim** — dwa
konsumenty to dopiero moment, w którym R3 pozwala rozważyć rolę.

⚠ **Wzorzec K1/K2/K3/K6 to jedno pytanie zadane cztery razy: ile mierzy pasek narzędzi i ile mierzy
nagłówek sekcji.** Katalog M2a odpowiedział jedną liczbą na każde; produkt używa dwóch. Rozstrzygnięcie
którejkolwiek z tych par **wewnątrz M2c** zmieniłoby wygląd, więc sweep zostawia obie strony widoczne.

---

### §18.4 Iteracja 4 — `ProcedureDetailTabView` + `FunctionDetailTabView` (2026-08-02)

> **Zakres:** bliźniaki, migrowane **razem** (plan §18.0.7). 40 + 41 `FontSize`, po 1 `CornerRadius`.

#### §18.4.1 Wynik

| Plik | `FontSize` | `CornerRadius` |
|---|---|---|
| `ProcedureDetailTabView.axaml` | 40 → **4** | 1 → 1 (karta) |
| `FunctionDetailTabView.axaml` | 41 → **4** | 1 → 1 (karta) |

Po jednym usunięciu (koszyk A: `TextBox` przy 12). Role: `Text.Compact` 24 / 25 · `Text.Grid` 4 ·
`Text.Caption` 4 · `Text.Code` 3. Wartości bez zmian.

#### §18.4.2 ⭐ Dlaczego bliźniaki idą w JEDNEJ iteracji

Oba pliki mają tę samą strukturę co do sekcji i różnią się jednym `CheckBox`em. Migrowane osobno
**rozjechałyby się na pierwszej niejednoznacznej roli** — a rozjazd między dwoma widokami, które użytkownik
czyta jako jeden wzorzec, jest gorszy niż wartość lokalna, bo nie widać go w żadnym pojedynczym pliku.
Wynik potwierdza sens tej decyzji: **cztery wyjątki w każdym, identyczne co do rodzaju.**

#### §18.4.3 `DataGrid` przy 11 — pierwszy raz, gdy deklaracja siatki ZGADZA SIĘ z rolą

W Data Import cztery siatki deklarowały 12 przy roli niosącej 11 i **zostały** (§18.2.5). Tutaj osiem siatek
deklaruje **11** — dokładnie tyle, ile niesie `Text.Grid`. To jest formalnie koszyk **A?** („usuń po
weryfikacji"), ale poszły na **rolę**, nie do usunięcia: podmiana jest tak samo tania, zeruje licznik tak
samo, a **dodatkowo mówi prawdę** — ta siatka ma rozmiar siatki. Usunięcie oddałoby wszystko, czego nie
obejmują style `DataGridCell`/`DataGridColumnHeader`, domyślnej wielkości okna (14).

#### §18.4.4 ⚠⚠ KOREKTA ZAPISU: `MinHeight="26"` w bliźniakach NIE jest redundantne

`CLAUDE.md` opisywał tę wartość jako *„now-redundant local `MinHeight="26"` workaround for Fluent's chunky
`Expander`"* i wskazywał M2c jako miejsce jej usunięcia. **Zmierzone: most `FluentBridge` mapuje
`ExpanderMinHeight` na `Size.Control` = 24**, więc usunięcie obniżyłoby nagłówek o **2 px**.

⭐ Zapis był prawdziwy w połowie: usunięcie faktycznie **należy** do M2c, ale **nie jest neutralne**, a M2c
nie zmienia wyglądu. Wartość zostaje z powodem w miejscu i wchodzi do rejestru jako **K7**; „26 czy 24 dla
nagłówka Expandera" rozstrzyga przegląd §13.3. Zapis w `CLAUDE.md` skorygowany.

#### §18.4.5 Osiem wyjątków — cztery rodzaje, po dwa razy

| Element (w każdym z bliźniaków) | Wart. | Powód |
|---|---|---|
| dwa `ae:TextEditor` w wierszu siatki (kursory / podprogramy Easy) | 12 | gęstość kontenera, nie dryf (§18.0.5/3); `Text.Code` niesie 13 |
| znak rodzaju podprogramu | 9 | brak roli o tej wartości |
| nagłówek karty tabeli, SemiBold | 12 | rola nagłówka niesie 11 → **K6** |
| `CornerRadius` karty aktywności | 4 | `Radius.Surface` niesie 3 → decyzja §13.3 |

#### §18.4.6 Stan po iteracji 4

Build **0/0** · suite **7087** zielony w trzech partycjach · smoke czysty.
Liczniki: `FontSize` **405 → 332** · `FontFamily` **81** · `CornerRadius` **31**.

---

### §18.5 Iteracja 5 — osiem edytorów obiektów (2026-08-02)

> **Zakres:** Table 27 · Trigger 22 · View 20 · Package 17 · Domain 16 · Generator 15 · Exception 13 ·
> Index 11 = **141 `FontSize`**. Żadnego `CornerRadius`.

#### §18.5.1 Wynik: **141 → 0. Pierwsza iteracja bez ani jednego wyjątku.**

31 usunięć (kontrolki formularza przy 12) · 110 na role · **0 wyjątków**. Osiem wpisów **znika z bazy**
(strażnik wymaga usunięcia wpisu, nie wyzerowania go). Wartości bez zmian.

Role: `Text.Compact` 74 · `Text.Code` 12 · `Text.Grid` 12 · `Text.Caption` 12.

#### §18.5.2 ⭐ Dlaczego akurat tu wyszło zero — i co to mówi o poprzednich iteracjach

Te osiem widoków jest zbudowanych z **jednego wzorca**: formularz właściwości + dwa drzewa zależności
(`Header` / `ObjectName` / `FieldName`) + podgląd DDL + ewentualna siatka. Wzorzec jest na tyle
konsekwentny, że **każda wartość miała rolę o dokładnie tej samej liczbie** — 11 dla treści paneli i drzew,
10 dla podpisów podrzędnych, 12 dla pól formularza (ze stylu), 13 dla pełnowymiarowego edytora DDL.

⭐ **Wniosek: wyjątki nie biorą się z wielkości pliku ani z jego wieku, tylko z tego, ile RÓŻNYCH decyzji
projektowych w nim zapadło.** Debugger, Data Import i Performance mają własne paski, karty, chipy, glify
i miary gęstości — i tam siedzą wszystkie kolizje K1–K7. Edytory obiektów nie mają nic własnego; są
najczystszą częścią aplikacji i przeszły mechanicznie.

⚠ To także sprawdzian samego katalogu: **na 141 pozycjach zbudowanych z jednego wzorca katalog M2a
wystarczył w 100%.** Rejestr kolizji nie urósł.

#### §18.5.3 Zweryfikowane, nie założone

Przed migracją sprawdzono skryptem, czy któraś deklaracja nie stoi **wewnątrz `DataGrid.Columns`** — tam
rolą byłby `Text.Grid`, a nie `Text.Compact`, i pomyłka byłaby niewidoczna. **Żadna nie stoi.**

#### §18.5.4 Stan po iteracji 5

Build **0/0** · suite **7087** zielony · smoke czysty.
Liczniki: `FontSize` **332 → 191** · `FontFamily` **81** · `CornerRadius` **31**.

---

### §18.6 Iteracja 6 — monitory (2026-08-02)

> **Zakres:** `SessionManagerTabView` 26 · `TraceMonitorTabView` 17 · `SecurityManagerTabView` 17 =
> **60 `FontSize`**, plus **17 `CornerRadius`** — tu siedzi cała geometria etapu.

#### §18.6.1 Wynik

| Plik | `FontSize` | `CornerRadius` |
|---|---|---|
| `SessionManagerTabView` | 26 → **4** | 9 → 9 (geometria + karty + reset) |
| `TraceMonitorTabView` | 17 → **3** | 6 → 6 (j.w.) |
| `SecurityManagerTabView` | 17 → **9** | 2 → **0** |

44 na role, 16 wyjątków. Wartości bez zmian.

#### §18.6.2 ⭐ Dokładna przeciwwaga iteracji 5 — potwierdza jej wniosek

Iteracja 5 dała **0 wyjątków na 141 pozycjach**, bo osiem edytorów obiektów jest zbudowanych z jednego
wzorca. Monitory są jej przeciwieństwem: **każdy z tych trzech widoków ma własne decyzje projektowe** —
pasek segmentowy (`Button.seg`), karty ostrzeżeń, koła stanu, kapsuły postępu, nagłówki paneli szczegółów
(`TextBlock.section`) — i stąd 16 wyjątków na 60 pozycjach.

⭐ **Ten kontrast jest najlepszym dowodem tezy R12 w całym etapie:** licznik nie mierzy jakości pracy,
tylko to, ile własnych rozstrzygnięć niesie dany widok. 141 → 0 i 60 → 16 to ta sama robota.

#### §18.6.3 ⚠⚠ POMIAR ZNALAZŁ DEFEKT W SAMYM STRAŻNIKU — druga połowa poprawki z M2b kroku 12

Po migracji `SecurityManagerTabView` licznik dalej pokazywał 10 zamiast 9, a `SessionManager` 7 zamiast 4.
Przyczyna nie leżała w widoku, tylko w regexie strażnika:

```
\bFontSize\s*=(?!=)(?!\s*"{)      ← atrybut: odwołanie do zasobu WYŁĄCZONE (M2b krok 12)
Property\s*=\s*"FontSize"          ← setter: liczony BEZWARUNKOWO
```

Czyli **`<Setter Property="FontSize" Value="{DynamicResource Text.Grid.Size}" />` liczyło się dokładnie
tak samo jak `Value="11"`.** To ten sam defekt, który M2b krok 12 naprawił dla atrybutów — i to samo
uzasadnienie, słowo w słowo: *„counting it identically made the stage's exit condition unreachable"*.
Plik z lokalnym stylem **nie mógł osiągnąć stanu docelowego**, choćby był zmigrowany w całości.

⭐ **Poprawka to jeden lookahead** (`(?!\s*Value\s*=\s*"{")`), i **nie osłabia strażnika**: setter
z literałem nadal się liczy. ⚠ Przed M2c żaden setter w `Views/`/`Controls/` nie czytał katalogu, więc
korekta **nie rusza ani jednej bazy poza tymi, które ten etap właśnie migruje** — sprawdzone pomiarem
per plik przed i po.

⚠ To trzeci raz w tym etapie, gdy narzędzie pomiarowe okazało się mniej dokładne niż zakładano
(§18.1.6 liczba testów · §18.2.3 proza w komentarzu · tutaj setter). **Wspólny kształt: licznik mierzył
COŚ INNEGO niż nazwa sugeruje, i za każdym razem widać to dopiero, gdy migracja dociera do granicy.**

#### §18.6.4 Szesnaście wyjątków — cztery rodzaje

| Rodzaj | Ile | Powód |
|---|---|---|
| nagłówki Security Managera przy 13 px | 8 | katalog ma przy 13 wyłącznie rolę kodu (§18.0.5/3) |
| puste stany przy 13 px (Sessions, Trace) | 2 | j.w. |
| `TextBlock.section` 12 px SemiBold (Session + Trace) | 2 | rola nagłówka niesie 11 → **K8** |
| dwa znaki przy 9 px, edytor szczegółu Trace przy 12 px, glif przycisku `Height=18` przy 12 px | 4 | brak roli / gęstość kontenera / element układu |

#### §18.6.5 `CornerRadius` — cała geometria etapu w dwóch plikach

Piętnaście z siedemnastu zostaje, i **wszystkie z tego samego powodu, o którym mówi ratyfikacja
§18.0.5/2**: koła (`10×10` r=5, `9×9` r=4.5), kapsuły pasków postępu (`Height=10` r=5), karty i kontenery
przy 4 (gdzie `Radius.Surface` niesie 3) oraz dwa settery resetujące do 0. Migrują **dwa** — obie
trójki w Security Managerze, w tym jedna jako setter stylu.

#### §18.6.6 Stan po iteracji 6

Build **0/0** · suite **7087** zielony · smoke czysty.
Liczniki: `FontSize` **191 → 152** · `FontFamily` **81** · `CornerRadius` **31 → 30**.

---

### §18.7 Iteracja 7 — `MainWindow` + `Controls/` (2026-08-02)

> **Zakres:** ⭐ **powierzchnia trwała (§0.1)** — `MainWindow` 26 `FontSize` + 1 `CornerRadius`,
> `BreadcrumbBar` 2, `MessageBanner` 2, `TableColumnPicker.cs` 3.

#### §18.7.1 Wynik: **33 → 0, bez ani jednego wyjątku**

Jedno usunięcie (filtr drzewa metadanych — cała linia, bo `FontSize` był jej jedyną treścią),
32 na role, 1 promień na `Radius.Surface`. Wartości bez zmian.

#### §18.7.2 ⭐ Zero wyjątków przy NAJWIĘKSZEJ różnorodności ról w jednym pliku

`MainWindow` użył **sześciu** różnych ról — więcej niż którykolwiek inny plik w etapie:

| Element | Rola |
|---|---|
| **pasek statusu** — stan połączenia, statystyki zapytania, status debuggera, chip wersji | **`Text.Status`** (4 elementy) |
| nazwa aktywnego połączenia w tytule | `Text.Title` |
| plakietka DEV MODE | `Text.Caption` |
| dwa edytory (SQL + podgląd DDL) | `Text.Code` |
| log komunikatów + notka nad wynikami | `Text.Application` |
| reszta chromy — panele, listy, filtry | `Text.Compact` |

⭐ **To jest pierwszy realny konsument `Text.Status` poza belką Data Import** — a więc pierwszy raz,
gdy rola „pasek statusu jest powierzchnią trwałą i musi dać się wyregulować niezależnie" (§3.3
`Typography.axaml`) faktycznie obejmuje **oba** paski statusu w aplikacji. Do dziś pasek `MainWindow`
trzymał cztery własne jedenastki i żadna zmiana katalogu by go nie ruszyła.

⚠ Zero wyjątków nie wzięło się z prostoty pliku, tylko z tego, że **katalog ma rolę dla każdego rodzaju
tekstu, jaki niesie chroma aplikacji**. Kolizje K1–K8 dotyczą wyłącznie miejsc, gdzie widok ma własną
skalę — a chroma `MainWindow` żadnej własnej nie miała.

#### §18.7.3 `Controls/` — trzy komponenty współdzielone, trzy różne idiomy

`BreadcrumbBar` migruje **setter stylu** (`Text.Compact`) i separator `›`, który jest częścią tekstu
(reguła glifów §18.1.3). `MessageBanner` — obie linie treści na `Text.Application`, czyli jedna z ról
dostaje wreszcie **ten** komponent, który jest jedyną powierzchnią komunikatów w IDE.
`TableColumnPicker` jest w C#, więc czyta katalog **idiomem, który ten plik już miał**:
`caption[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("…")` — dokładnie tak, jak od zawsze
czytał `Foreground`. ⭐ Trzeci wariant „jak skonsumować rolę poza XAML-em", obok `BindFontSize` z iteracji 1;
oba są żywe, oba są lokalnym idiomem swojego pliku.

#### §18.7.4 Stan po iteracji 7

Build **0/0** · suite **7087** zielony w trzech partycjach · smoke czysty.
Liczniki: `FontSize` **152 → 114** · `FontFamily` **81** · `CornerRadius` **30 → 28**.

---

### §18.8 Iteracja 8 — ogon dialogów + `TableDetailTabView.axaml.cs` (2026-08-02)

> **Zakres:** 28 plików po 1–11 deklaracji — dialogi, okna narzędziowe, `AggregationBarView`,
> `DiagnosticsPanelView`, `ScriptExecutorTabView`, `GlobalSearchTabView`, `NewTableTabView`
> oraz dwie kontrolki budowane w code-behind Table Detail.

#### §18.8.1 Wynik

**`FontSize` 80 → 4 · `CornerRadius` 9 → 0.** 10 usunięć, 72 na role, 4 wyjątki.
**24 wpisy zdjęte z bazy w całości.** Wartości bez zmian.

#### §18.8.2 ⭐ `Radius.Chip` dostaje swojego jedynego konsumenta — i to bez wyjątku

Krok 0 zmierzył, że z jedenastu promieni przy 4 **chipem jest dokładnie jeden**:
`AggregationBarView` (§18.0.5/2). `Radius.Chip` niesie **4**, więc tutaj — jedyny raz w etapie —
**funkcja i liczba się zgadzają**, i promień przechodzi na rolę bez żadnego zastrzeżenia.

⚠ Warto to zestawić z **K5** (chip Performance przy promieniu 3, gdzie `Radius.Chip` = 4 i migracja
poszła na `Radius.Surface`). Ta sama nazwa roli, dwa różne wyniki, bo **decyduje liczba, nie nazwa** —
dokładnie to, co ratyfikuje R12.

#### §18.8.3 Cztery wyjątki, wszystkie znanego rodzaju

`ConfirmDialog`, `ChoiceDialog` i nagłówek `ForeignKeyDialog` przy **13 px** (katalog ma przy 13
wyłącznie rolę kodu — grupa z §18.0.5/3) oraz podgląd **Global Search** przy 12 px, czyli szósty
i ostatni z edytorów, które krok 0 zmierzył jako stojące **w wierszu siatki**.

⭐ **Tym samym wszystkie sześć edytorów przy 12 px z §18.0.5/3 zostało odnalezione i udokumentowane
w miejscu:** dwa w `ProcedureDetail`, dwa w `FunctionDetail`, szczegół Trace, podgląd Global Search.
Pomiar kroku 0 zgadza się co do sztuki.

#### §18.8.4 Code-behind Table Detail — czwarty idiom konsumpcji roli

Dwie kontrolki edycji w komórce siatki (`TextBox`, `CalendarDatePicker`) budowane w `FuncDataTemplate`
czytają **`Text.Grid`** przez `Bind(…, GetResourceObservable(…))`. To ta sama trasa co `BindFontSize`
w debuggerze, tyle że bez pomocnika — plik nie miał go i jedno wywołanie nie uzasadnia nowego.

#### §18.8.5 Stan po iteracji 8

Build **0/0** · suite **7087** zielony · smoke czysty.
Liczniki: `FontSize` **114 → 43** · `FontFamily` **81** · `CornerRadius` **28 → 19**.
⭐ **Pozostałe 43 `FontSize` i 19 `CornerRadius` to WYŁĄCZNIE świadome wyjątki z powodem zapisanym
w miejscu** — po raz pierwszy w etapie licznik nie zawiera już ani jednej wartości „do zrobienia".

---

### §18.9 Iteracja 9 — literały w `ControlStyles.axaml` (2026-08-02)

> **Zakres:** 7 `FontSize` + 7 `CornerRadius` **poza zasięgiem strażnika** (`Themes/` jest wyłączone —
> „tam mieszka system"). Krok 0 nazwał to *katalogiem zapisanym drugi raz* (§18.0.6).

**Zmigrowane (7):** `ListBox.code-action-menu ListBoxItem` 12 · `ContextMenu` 12 · `MenuItem` 12 →
`Text.Application` · `TabItem.bottom-tab` 11 · `TabItem.sub-tab` 11 · `PART_InputGestureText` 11 →
`Text.Compact` · `MenuItem` PART_LayoutRoot 3 → `Radius.Surface`.

**Zostaje z powodem (7):** `TabItem` 13 (**K9** — brak roli tekstowej przy 13; ⚠ **skorygowane w M3.3a: to
dolny panel i pod‑zakładki, NIE pasek zakładek roboczych** — §18.R) ·
dwa promienie zakładek przy 4 (**K10** — `Radius.Chip` niesie tę samą liczbę, ale zakładka chipem nie
jest) · `ContextMenu` 4 (powierzchnia unosząca się; `Radius.Surface` niesie 3) · kropka filtra 3.5
(geometria: 7×7, promień = połowa boku) · dwa resety do 0.

⭐ **Ta iteracja nie zmienia żadnego licznika** — `Themes/` nigdy nie był mierzony. Jej wartość jest inna:
**usuwa ostatnie miejsce, w którym system opisywał sam siebie drugi raz.** Zmiana `Text.Application` w
katalogu dosięga teraz również menu kontekstowych, a `Text.Compact` — dolnych i pod-zakładek.

---

### §18.10 🔒 M2c — PODSUMOWANIE ZAMYKAJĄCE (2026-08-02)

#### §18.10.1 Wynik liczbowy

| Licznik | Start | Koniec | Co zostało |
|---|---|---|---|
| `FontSize` | **605** / 49 plików | **43** / 13 plików | wyłącznie wyjątki z powodem w miejscu |
| `CornerRadius` | **37** / 13 plików | **19** / 5 plików | geometria, karty i resety |
| `FontFamily` | 81 / 28 | **81** | ⛔ poza zakresem etapu (ratyfikowane, §18.0.5/1) |

**Baza w `DesignTokenComplianceTests` zgadza się z pomiarem co do sztuki** — 43 i 19, plik po pliku.
Build **0/0** · suite **7088** zielony w trzech partycjach (7000 + 54 + 34) · smoke czysty po każdej
z dziewięciu iteracji. **Ani jedna wartość liczbowa w aplikacji się nie zmieniła** — jedyna celowa zmiana
wyglądu w całym etapie to poprawka odbiorcza §18.11, na wyraźne polecenie użytkownika.

#### §18.10.2 ⭐⭐ Czym naprawdę okazał się ten etap

> **R12, ratyfikowana w kroku 0:** *„celem jest usunięcie NIEUZASADNIONYCH wartości lokalnych, nie
> wyzerowanie licznika."*

Dziewięć iteracji dało temu zdaniu **liczbowy kształt: 562 wartości zniknęły, 62 zostały z powodem.**
I rozkład tych 62 nie jest przypadkowy — **wyjątek pojawia się dokładnie tam, gdzie widok podjął własną
decyzję projektową**, a nie tam, gdzie kod jest stary albo plik duży:

| Widok | Wynik | Dlaczego |
|---|---|---|
| osiem edytorów obiektów | 141 → **0** | jeden wzorzec, zero własnych skal |
| `MainWindow` + `Controls/` | 33 → **0** | chroma aplikacji, sześć ról, żadnej własnej skali |
| ogon 28 dialogów | 80 → **4** | zwykłe formularze |
| debugger, Data Import, Performance, monitory | 285 → **30** | własne paski, karty, chipy, glify, gęstości |

⭐ **Licznik nigdy nie mierzył jakości pracy — mierzył liczbę własnych rozstrzygnięć widoku.**

#### §18.10.3 ⭐⭐ Drugi wynik etapu: rejestr kolizji K1–K10

Rejestr §18.R jest **produktem M2c na równi ze sweepem**. Dziesięć pozycji, ale **jedno pytanie zadane
wielokrotnie: katalog M2a opisał każdą rolę JEDNĄ liczbą, a produkt używa niektórych w dwóch.**

* **pasek narzędzi** — debugger 11, Data Import 12 (K1, K2)
* **nagłówek sekcji** — kanoniczny `group-header` 11, ale Performance 12, karty bliźniaków 12, panele
  szczegółów monitorów 12 (K3, K6, K8)
* **rozmiar kodu** — pełnowymiarowy edytor 13, sześć edytorów w wierszu siatki 12 (ratyfikowane jako
  decyzja kontenera, nie dryf)
* **promień** — chip Performance 3 vs `Radius.Chip` 4 (K5), zakładka 4 vs `Radius.Surface` 3 (K10)
* **pojedyncze** — `PlanLead` 13 (K4), `Expander` 26 vs 24 (K7), zakładka robocza 13 (K9)

⛔ **Żadna z nich nie została rozstrzygnięta w M2c i to jest właściwe.** Rozstrzygnięcie którejkolwiek
zmieniłoby wygląd, a M2c migruje do systemu z M2a, nie projektuje nowego.

#### §18.10.4 ⚠⚠ Trzy razy narzędzie pomiarowe okazało się mniej dokładne niż zakładano

| Gdzie | Co mierzyło naprawdę | Skutek |
|---|---|---|
| liczba testów (§18.1.6) | filtr partycji zawierał **nieistniejącą klasę** | suita 7088 → **7087** we wszystkich dokumentach |
| licznik vs komentarz (§18.2.3) | **prozę cytującą składnię atrybutu** | dwa fałszywe trafienia; złapane dwa razy w jednej iteracji |
| licznik vs setter (§18.6.3) | setter czytający katalog liczony jak literał | **poprawka regexu** — druga połowa naprawy z M2b kroku 12 |

⭐ **Wspólny kształt: narzędzie mierzyło COŚ INNEGO niż mówi jego nazwa, i za każdym razem widać to
dopiero, gdy migracja dociera do granicy.** Gdyby M2c szedł automatem, wszystkie trzy przeszłyby
niezauważone przy zielonym buildzie.

#### §18.10.5 Cztery idiomy konsumpcji roli — wszystkie żywe, każdy na swoim miejscu

1. **XAML** — `FontSize="{DynamicResource Text.Compact.Size}"` (dominujący).
2. **Setter stylu** — `<Setter Property="FontSize" Value="{DynamicResource …}" />` (`ControlStyles`,
   `BreadcrumbBar`, style lokalne monitorów).
3. **C# z pomocnikiem** — `BindFontSize(control, "…")` w `DebuggerTabView`, bliźniak istniejącego
   `BindBrush` (6 wywołań uzasadnia pomocnika).
4. **C# bez pomocnika** — `DynamicResourceExtension` przez indekser (`TableColumnPicker`, idiom tego
   pliku) albo `Bind(…, GetResourceObservable(…))` (`TableDetailTabView`, dwa wywołania).

#### §18.10.6 Definition of Done — stan

| # | Warunek | |
|---|---|---|
| 1 | każda pozostała wartość ma komentarz z powodem | ✅ 62 pozycje, powód w miejscu |
| 2 | baza odzwierciedla stan faktyczny | ✅ 43 i 19, zweryfikowane pomiarem |
| 3 | build 0/0 | ✅ |
| 4 | suite zielony w trzech partycjach | ✅ 7000 + 33 + 54 |
| 5 | smoke czysty | ✅ po każdej iteracji |
| 6 | ⭐ **aplikacja wygląda IDENTYCZNIE** | ✅ **odebrane przez użytkownika 2026-08-02** — jedyne zgłoszenie dotyczyło defektu ZASTANEGO, nie migracji (§18.11) |
| 7 | §18 prowadzone iteracja po iteracji | ✅ §18.1–§18.10 + rejestr §18.R |
| 8 | push na oba remote'y | ✅ |

⚠ **Warunek 6 jest jedynym, którego nie potrafię sprawdzić sam** — i jest tym, który odróżnia M2c od M2b.
Po każdej iteracji weryfikowałem skryptem, że **każdy użyty klucz roli istnieje w katalogu**
(`{DynamicResource}` nie rzuca przy literówce, pułapka #14) i uruchamiałem aplikację; to jest maksimum
dostępnego dowodu bez ludzkiego oka.

---

### §18.11 Poprawka odbiorcza — pole wielowierszowe zaczyna tekst od góry (2026-08-02)

> **Zgłoszenie użytkownika przy odbiorze M2c:** *„W polach wielowierszowych (np. zakładka Description
> w edytorze tabeli/wyjątku) tekst jest wyśrodkowany w pionie. To nie wygląda dobrze — przy krótkim opisie
> tekst ląduje w środku pola. Moja wcześniejsza prośba o pionowe wyśrodkowanie dotyczyła wyłącznie pól,
> o których wiemy, że zawsze są jednowierszowe."*

#### §18.11.1 To NIE był efekt sweepu — i to jest istotne dla oceny etapu

Przyczyną jest styl `TextBox` z **M2b**, który wyśrodkowuje pionowo **każde** pole tekstowe. Dla pola
jednowierszowego jest to konieczne, a nie kosmetyczne: `Pad.Control` ma **pion zerowy** (wysokość ma dawać
`Size.Control`, jedna wielkość jeden właściciel), więc bez wyśrodkowania tekst osiadłby na górnej krawędzi
24-pikselowej kontrolki. Ta sama reguła w polu na kilkanaście wierszy zawiesza krótki opis w połowie ramki.

⭐ **M2c niczego tu nie zmienił** — zgłoszenie potwierdza DoD 6 od drugiej strony: wygląd po migracji jest
identyczny, łącznie z zastanym defektem.

#### §18.11.2 Poprawka — jedna reguła, sformułowana pozytywnie

```xml
<Style Selector="TextBox[AcceptsReturn=True]">
  <Setter Property="VerticalContentAlignment" Value="Top" />
</Style>
```

⭐ Selektor własnościowy czyta `AcceptsReturn` **z samej kontrolki**, więc mówi, czym pole **JEST**
(decyzja architektoniczna §17.2/3), a nie „wszystko jest wyśrodkowane, chyba że…". Obejmuje **29 pól
w 21 plikach** i każde następne — żaden widok nie musi o niczym pamiętać i nie ma gdzie zapomnieć.
⚠ **Stoi PO stylu bazowym**: przy równej trafności rozstrzyga kolejność deklaracji (§17.5/5).

#### §18.11.3 Pin zweryfikowany PODŁOŻENIEM NARUSZENIA

`AMultilineTextBox_StartsItsTextAtTheTop_AndASingleLineOneStaysCentred` sprawdza **obie połowy** na realnym
drzewie wizualnym. Ryzyko tej poprawki jest bowiem takie samo jak ryzyko całego etapu: **gdyby selektor nie
pasował, nic by nie zawiodło poza wyglądem.** Test uruchomiono z odwróconą wartością w stylu i **zawiódł
z właściwym komunikatem**, a po przywróceniu przechodzi.

#### §18.11.4 Zakres — celowo minimalny

⛔ Nie ruszono ani stylu bazowego, ani `Pad.Control`, ani żadnego widoku. Poprawka nie jest częścią sweepu
i **nie zmienia żadnego licznika**; jest jedynym miejscem w całym M2c, w którym wygląd zmienia się celowo —
na wyraźne polecenie użytkownika i wyłącznie w klasie pól, której dotyczyło zgłoszenie.

#### §18.11.5 🔒 M2c ZAKOŃCZONE I ODEBRANE

> **Użytkownik:** *„M2c odbieram pozytywnie. Nie znalazłem problemów wynikających z migracji katalogu…
> M2c po tej poprawce uznaję za zakończone."*

**DoD 6 spełniony.** Build **0/0** · suite **7088** zielony w trzech partycjach (7000 + 54 + 34) ·
smoke czysty.

⚠ **Suite ma znowu 7088 — ale z INNEGO powodu niż przed korektą z §18.1.6.** Tam 7088 było błędem
arytmetycznym (filtr wymieniał nieistniejącą klasę, faktyczny stan to 7087); tutaj **7087 + 1 nowy pin**.
Zapisane wprost, bo inaczej następny czytelnik uzna korektę za cofniętą.

---

## §19 As-built — M3 (powierzchnie trwałe)

> **Punkt wejścia do etapu:** [`product-polish-m3-handover.md`](product-polish-m3-handover.md) —
> samowystarczalny: stan, reguły, procedura, pułapki, plan 18 iteracji.
>
> **Zakres M3, ratyfikowany przez użytkownika 2026-08-02:** M3.1 Status Bar 2.0 · M3.2 Toolbar ·
> M3.3 pasek zakładek · M3.4 Metadata Explorer · **M3b** (podłączenie wszystkich pozostałych operacji
> do paska postępu) → ⛔ brama §13.3 → jedno podsumowanie zamykające cały etap.

### §19.0 Iteracja 0 — POMIAR (2026-08-02)

> **Cel:** zmierzyć stan faktyczny czterech powierzchni trwałych i zweryfikować **każde** założenie §8,
> zanim cokolwiek zostanie ruszone. Ten krok **nie zmienia ani jednej linii kodu produkcyjnego.**
>
> **Powód, dla którego w ogóle powstał:** dokument wejściowy M3 nie istniał — M2c zamknął się
> podsumowaniem §18.10 i poprawką odbiorczą §18.11, a jego własny handover przepozycjonował się na
> *„zapis reguł i pułapek"*. Zamiast pisać plan z dokumentu, powtórzyliśmy wzorzec kroku 0 M2c,
> który **obalił pięć założeń**, na których etap miał stanąć.

#### §19.0.1 ⭐⭐ ZNALEZISKO GŁÓWNE — rytm pionowy Application Chrome nigdy nie został zastosowany

| Powierzchnia | Katalog (M2a) | Rzeczywistość | Konsument tokenu |
|---|---|---|---|
| Pasek tytułu | `Size.TitleBar` **36** | **36** — literał `MainWindow.axaml:41` `RowDefinitions="36,Auto,*,28"` | tylko `Button.caption` (`ControlStyles.axaml:710`) |
| Pasek zakładek | `Size.Row.Tab` **26** | **brak deklaracji** — wysokość wynika z treści (`Grid RowDefinitions="2,*"` + `Button Padding="8,4"` + ikona 14) | ⛔ **zero** |
| Pasek statusu | `Size.StatusBar` **24** | ⚠ **28** — literał, ten sam `RowDefinitions` | ⚠ **tylko `DataImportTabView.axaml:1188`** |
| Wiersz drzewa | `Size.Row.Tree` **20** | ⚠ **24** — `ListBoxItem.MinHeight`, `MainWindow.axaml:425` | ⛔ **zero** |
| Wskaźnik zakładki | `Size.TabIndicator` **2** | **2** — literał `RowDefinitions="2,*"`, `MainWindow.axaml:812` | ⛔ **token nie istnieje** |

**Dwie z czterech liczb są niezgodne ze stanem faktycznym, trzy tokeny nie mają ani jednego konsumenta,
a jeden token w ogóle nie został utworzony.**

⚠⚠ **Najostrzejszy pojedynczy fakt: `Size.StatusBar` konsumuje belka Data Importu, a nie pasek statusu
aplikacji.** Belka Data Importu jest zresztą użyciem **poprawnym** (`Classes="chrome"`,
`BorderThickness="0,1,0,0"` — ten sam kształt co pasek statusu), co czyni sytuację czytelniejszą,
nie mniej czytelną: rola została zdefiniowana i zastosowana **wszędzie poza swoim własnym miejscem**.

⭐ **Dlaczego M2c tego nie złapał — i to nie jest zarzut wobec M2c.** Liczniki M2c to `FontSize`,
`CornerRadius` i `FontFamily`. **Wysokości nigdy nie należały do żadnego licznika.** Sweep przeszedł
przez `MainWindow.axaml` w iteracji 7 (33 → 0) i te literały minął, bo nie były jego przedmiotem.
⚠ To jest **czwarty raz** w tym etapie, gdy narzędzie pomiarowe mierzyło coś węższego niż sugeruje
jego nazwa (§18.10.4 zebrało trzy poprzednie) — z tą różnicą, że tu narzędzie było **poprawne**,
a za szerokie było wnioskowanie z jego zielonego wyniku.

⚠⚠ **Konsekwencja dla bramy §13.3.** Pytanie kontrolne nr 2 brzmi: *„Czy rytm pionowy (36 / 26 / 24)
czyta się jako hierarchia, czy jako trzy przypadkowe wysokości?"* — **dziś ten rytm nie istnieje
w działającej aplikacji.** M3 jest etapem, w którym powstaje po raz pierwszy; brama go **ocenia**,
a nie weryfikuje jego zachowanie.

#### §19.0.2 Dwa otwarte pomiary z §8 — oba rozstrzygnięte na TAK

**(a) Czas trwania transakcji (§8.4.5).** Zapis mówił: *„Czy da się je odczytać tanio i bez odpytywania
`MON$`, jest do sprawdzenia w M3. Jeśli nie — chip pokazuje sam stan, a czas trafia do UX Debt."*

Zmierzone: `TransactionService` **nie ma** żadnego znacznika czasu (zero `DateTime`, zero `Stopwatch`).
Ale wystawia zdarzenie `TransactionStateChanged`, które `MainWindowViewModel` **już subskrybuje**
(`:288`, handler `:7270`), oraz `State` / `IsActive` / `IsIdle`.

⭐ **Odpowiedź: da się, i to całkowicie w warstwie App.** Chip zapamiętuje moment przejścia
Idle → Active w istniejącym handlerze i sam mierzy czas. **Zero zapytań do serwera, zero round-tripów,
zero zmian w Core i w `EmberTern.Firebird`.** Wariant rezerwowy z §8.4.5 nie jest potrzebny.

**(b) `IconColor_Query` na rail Trace (§8.4.2, oznaczone *„do weryfikacji"*).** ✅ Token istnieje
w `Colors.axaml` w **obu** motywach.

#### §19.0.3 ⚠⚠ Ryzyko spoza dokumentu — chipy stanu nie mają dziś źródła danych

§8.4.3 chce w sekcji „Stan" chipów **transakcji, Trace i Debuggera**, a tabela Rail/Chip w §8.4 definiuje
czas życia chipa jako *„trwa, dopóki warunek jest prawdziwy"*. Zmierzone:

| Sygnał | Co naprawdę znaczy | Gdzie |
|---|---|---|
| `IsTraceMonitorTabActive` / `IsDebuggerTabActive` | ⚠ **„ta zakładka jest wybrana"** | `MainWindowViewModel:532` / `:551` |
| `TraceMonitorTabViewModel.State` (`TraceSessionState`) | stan faktyczny sesji | **VM zakładki** |
| `DebuggerTabViewModel.Phase` (`DebuggerPhase`) | stan faktyczny sesji | **VM zakładki** |
| agregacja po `WorkspaceTabs` | ⛔ **nie istnieje** — w `MainWindowViewModel` wyłącznie `Count` i indeksy | — |

⭐ **Istniejący wzorzec się nie generalizuje, i warto rozumieć dlaczego.** Pasek statusu pokazuje dziś
`ActiveDebugger.StatusText` i działa **wyłącznie** dlatego, że dotyczy zakładki **aktywnej** — Avalonia
subskrybuje `PropertyChanged` wzdłuż ścieżki wiązania. Chip ma być prawdziwy, gdy sesja trwa
**na innej zakładce**, więc potrzebuje nowej agregacji **oraz** ścieżki powiadomień.

⚠ To jest realna praca w **M3.1e**, addytywna i wyłącznie w warstwie App — ale **nie jest to praca
prezentacyjna** i nie wolno jej oszacować jak wiązania XAML.

#### §19.0.4 §7.5 — potwierdzone co do sztuki, dwa uściślenia

Zmierzone w pasku tytułu (`MainWindow.axaml:118–300`):
**6 × `AccentBrush`** · `Icon.Trash` → `WarningIconBrush` · `Icon.PlugZap` → `AccentIconBrush` ·
`Icon.RefreshCw` → `InfoIconBrush` · **10 × `IconColor_*`** · 22 ikony **bez** `Foreground`.
⭐ **Liczby zgadzają się z audytem M0 dokładnie** — §7.5 jest wiarygodny.

* **Uściślenie 1 (opisowe).** „10 przycisków *Nowy X*" to **9 kreatorów + 1 narzędzie** (Security Manager,
  `IconColor_Role`). Reguła §7.5 obejmuje oba tak samo (*„kolor rodzaju odpowiada na pytanie czego to
  dotyczy"*), więc **wniosek się nie zmienia** — zmienia się zdanie opisujące pomiar.
* **⚠ Uściślenie 2 (zakresowe — wymaga decyzji DC).** Ostatni wiersz tabeli §7.5 — *„`AccentIconBrush`,
  `InfoIconBrush` → **zlikwidowane**"* — czyta się jak zmiana dwóch linii, a jest zmianą
  **w 24 wystąpieniach / 14 plikach**: `SvgIcon.cs`, `DebuggerIcon.cs`, `NavigationController.cs`,
  **trzy ViewModele trzymające klucz jako string** (`FindingViewModel`, `SessionRowViewModel`,
  `VerdictViewModel`) oraz widoki Data Import, Debugger, Performance, Table Detail i Trace Monitor —
  czyli **powierzchnie M4.3**. ⭐ Kształt jest znajomy: **wiersz tabeli opisuje intencję, a nie
  zasięg zmiany.**

#### §19.0.5 ⚠ H‑5 — audyt nazwał zły moduł, a prawdziwy defekt jest gdzie indziej

Zapis audytu (§1.3): *„titlebar `Button.icon`+`SvgIcon`; **Script Executor** `Button.flat`+tekst"*.

**Zmierzone: Script Executor nie ma przycisków Commit/Rollback w ogóle.** Jedyne dwa miejsca to
`MainWindow.axaml:1057–1064` (toolbar dokumentu) i `DataImportTabView.axaml:189–208`. Drugim modułem
jest **Data Import**.

I różnica jest **węższa**, niż opisano — oba używają **tych samych ikon** (`Icon.Check` / `Icon.Undo`)
i **tych samych pędzli** (`SuccessIconBrush` / `DangerIconBrush`). Różni je wyłącznie wariant przycisku:
`icon` w chromie, `flat` + etykieta w paśmie raportu. ⭐ **To jest zgodne z decyzją architektoniczną 4
M2b** — kontener rozstrzyga wielkość, wariant niesie kolor — więc „ujednolicenie" tych dwóch przycisków
byłoby **cofnięciem** ratyfikowanej reguły, nie jej zastosowaniem.

⭐⭐ **Prawdziwy defekt, którego audyt nie nazwał.** Kontrakt „KOLOR SKUTKU" z §7.5 przypisuje
Commit → `CommitButtonBrush`, Rollback → `RollbackButtonBrush`. **Oba tokeny są zdefiniowane w obu
motywach (`Colors.axaml:176–177` i `:455–456`) i nie mają ani jednego konsumenta w całej aplikacji.**
Rollback maluje się dziś `DangerIconBrush` — tokenem kategorii *„wyłącznie operacje nieodwracalne —
Drop, Delete, Stop"*.

⚠ **Rollback nie jest operacją nieodwracalną w tym sensie** — wycofuje niezatwierdzoną pracę, co jest
dokładnie tym, przed czym `DangerIconBrush` ma ostrzegać w innych miejscach. §7.5 rozdziela
*„Warning / Rollback"* i *„Dangerous"* na dwie kategorie **celowo**. → decyzja **DD**.

#### §19.0.6 H‑3 — potwierdzone, ale to DWA różne paski, a drugi jest znacznie gorszy

| Pasek | Gdzie | Bramki `IsVisible` | Mechanizm |
|---|---|---|---|
| **Pasek tytułu** | `MainWindow.axaml:44–367`, wysokość stała **36** | `HasActiveConnection` (blok, `:72`), `IsDeveloperModeActive` (`:103`, `:109`), `CanExportDdl` (×2) | `ColumnDefinitions="Auto,Auto,*,Auto,Auto"` — kolumna 0 rośnie po połączeniu i **przesuwa poziomo całą kolumnę 1** (25 przycisków) |
| ⚠⚠ **Toolbar dokumentu** | `MainWindow.axaml:868–1230` | **72** | niemal wyłącznie `IsXxxDetailTabActive` (Procedure 8 · Function 8 · Trigger 7 · Package 7 · View 4 · Query 4 · Index 4 · …) — **przełączenie rodzaju zakładki przebudowuje zawartość paska** |

⚠ **Opis audytu jest prawdziwy co do faktu, ale mylący co do osi.** Przesunięcie jest **poziome**,
nie pionowe — pasek tytułu ma stałe 36 px i nigdy nie zmienia wysokości.

⭐ **Przypadek odczuwany najczęściej to jednak toolbar dokumentu.** 72 bramki w jednym poziomym
`StackPanelu` oznaczają, że przy każdej zmianie rodzaju zakładki te same operacje lądują pod innym
kursorem. To jest **pytanie projektowe M3.2** — *czy pasek ma stałe kotwice sekcji, czy przepływa* —
a nie poprawka do wykonania po cichu.

#### §19.0.7 M‑1 — 13 literałów, rozkład na podetapy

| Gdzie | Ile | Podetap |
|---|---|---|
| `MainWindow.axaml` — toolbar połączeń (New/Edit/Copy/Delete Connection, Connect, Disconnect, Reconnect) | 7 | **M3.2d** |
| `MainWindow.axaml` — przyciski okna (Minimize, Maximize / Restore, Close) | 3 | **M3.2d** |
| `MainWindow.axaml:848` — „Close tab" | 1 | **M3.3** |
| `PerformancePanelView:277`, `SessionManagerTabView:214` | 2 | ⛔ **poza M3** (M4.3) |

R‑7 przypisała M‑1 w całości do M3.2 z uzasadnieniem *„większość to tooltipy toolbara"*.
Zmierzone: **10 tam trafia, 1 do M3.3, 2 zostają poza etapem** — R‑7 uściślona, nie obalona.

#### §19.0.8 Co już istnieje i czego NIE trzeba budować

| Potrzeba | Stan |
|---|---|
| Bramka Save / Discard / Cancel | ✅ `RequestCloseTabAsync` (`:6514`), `ChoiceRequested` (`:2476`); komentarz `:2482` mówi wprost **„three entry points"** — menu zakładki będzie **czwartym** |
| Lista z filtrowaniem (tryb pojedynczego wiersza, §8.2) | ✅ `Controls/SearchableComboBox.cs` |
| Odświeżenie zakładki (pozycja menu §8.3) | ✅ `WorkspaceTabViewModel.RefreshAsync()` |
| Style `ContextMenu`/`MenuItem`, `{app:MenuIcon}`, `{app:CommandGesture}` | ✅ Keyboard Manager etap 5 — **zero nowej chromy** |
| Severity → pędzel + ikona (§8.4.4) | ✅ `MessageBanner.BrushKeyFor` / `GeometryKeyFor` |
| Wzorzec preferencji numerycznej (`TabStripMaxRows`, R‑5) | ✅ `PreferenceRange` + commit na blur/Enter + digits-only na tunelu (`settings-center.md` §17.4/§17.4a) |
| `CurrentSchemaVersion` | ✅ **2** — preferencje paska zakładek są addytywne, ⛔ **nie podbijać** (R‑4) |

#### §19.0.9 M3b — inwentarz operacji

**16 ViewModeli** ma własny stan „trwa operacja" (`IsRunning` / `IsBusy` / `IsExecuting` / `IsLoading`):
`BatchResults`, `DataImportTab`, `DomainDetail`, `ExceptionDetail`, `ExecutionTimer`, `GeneratorDetail`,
`IndexDetail`, `MainWindow`, `MetadataExplorer`, `MetadataNode`, `PackageDetail`, `ScriptExecutorTab`,
`SecurityManagerTab`, `SourceObjectDetail`, `TableDetail`, `ViewDetail`.

**Trzy realne ścieżki `IProgress`:** eksport (`Export/ExportService.cs`), wykonanie zapytania
(`MainWindowViewModel:3456` + `MakeLoadProgress():6333`), batch (`:5395`).
**Trzy `ProgressBar` w widokach:** `BatchResultsDialog`, `DataImportTabView`, `ExportDialog`.

⚠ Podział D4 zostaje: **M3.1f dostarcza sekcję i JEDNĄ operację referencyjną** (wykonanie zapytania SQL —
jedyna z trzech, która ma i `IProgress`, i próg miękki, i anulowanie), **M3b podłącza resztę.**

#### §19.0.10 ⛔ Cztery decyzje do podjęcia przed implementacją

| # | Pytanie | Kiedy | Rekomendacja |
|---|---|---|---|
| **DA** | `Size.StatusBar` = 24, rzeczywistość **28** — zastosować katalog czy poprawić katalog? | przed **M3.1a** | zastosować katalog (28 → 24): §8.5 specyfikacji zabrania **wzrostu**, zmniejszenie jest dozwolone, a 36/26/24 to ratyfikowany rytm |
| **DB** | `Size.Row.Tree` = 20, rzeczywistość **24** — to samo pytanie, ale na **najgęstszym widoku aplikacji** | przed **M3.4a** | ⚠ **wymaga oka użytkownika** — 24 → 20 to realna zmiana gęstości drzewa, nie porządkowanie |
| **DC** | Likwidacja `AccentIconBrush` / `InfoIconBrush` sięga **14 plików**, w tym powierzchni M4.3 | przed **M3.2b** | ograniczyć M3.2 do paska narzędzi; likwidację przenieść do M4.3/M5 **z zapisem powodu** |
| **DD** | Commit/Rollback → `CommitButtonBrush` / `RollbackButtonBrush`, czy zostają na `SuccessIconBrush` / `DangerIconBrush`? | przed **M3.2c** | przejść — dziś Rollback nosi kolor „operacji nieodwracalnej", a nią nie jest (§19.0.5) |

#### §19.0.11 Stan na wyjściu z iteracji 0

**Zero zmian w kodzie produkcyjnym.** Build **0/0** · suite **7088** · smoke czysty — wszystkie trzy
niezmienione względem `8567ebc`, bo iteracja 0 dotknęła wyłącznie dokumentacji.

**Powstało:** [`product-polish-m3-handover.md`](product-polish-m3-handover.md) (samowystarczalny punkt
wejścia: stan · zakres · reguły R1–R12 · procedura · 13 pułapek · plan 18 iteracji) oraz ta sekcja.

⭐ **Podsumowanie jednym zdaniem: M2c udowodnił, że wartość lokalna blokuje system; iteracja 0 M3
pokazała, że na powierzchniach trwałych problem jest o krok wcześniej — tam system nigdy nie został
podłączony.** Trzy tokeny bez konsumenta, jeden nieutworzony i dwie liczby niezgodne ze stanem
faktycznym to nie dług sweepu, tylko **obszar, którego żaden dotychczasowy licznik nie obejmował**.

---

### §19.1 Iteracja 1 (M3.1a) — rytm pionowy Application Chrome (2026-08-02)

> **Zakres:** podłączenie `Size.TitleBar` / `Size.StatusBar` / `Size.Row.Tab`, nowy token
> `Size.TabIndicator`, reguła kontenera dla paska zakładek. Pierwsza iteracja M3, która **zmienia wygląd**.

#### §19.1.1 Wynik

| Element | Przed | Po | Widoczne? |
|---|---|---|---|
| Pasek tytułu | 36 literał | `Size.TitleBar` (36) | nie |
| Pasek statusu | **28** literał | `Size.StatusBar` (**24**) | **tak** |
| Wiersz zakładki | **30** (wypadkowa) | `Size.Row.Tab` (**26**) | **tak** |
| Szerokość zakładki | **≥ 132** (podłoga) | naturalna | **tak** |
| Wskaźnik zakładki | `2` literał w `RowDefinitions` | `Size.TabIndicator` (2) | nie |

Rytm **36 / 26 / 24** jest po raz pierwszy zastosowany w działającej aplikacji, a nie tylko zapisany
w katalogu.

#### §19.1.2 ⭐⭐ Główne ustalenie: 30 px nigdy nie było decyzją projektową

Sonda headless na dokładnej strukturze szablonu zakładki (`MainWindow.axaml:791–855`) zmierzyła:

```
przed:  ZAKLADKA H=30  W=132     activate H=28 W=108 MinH=28 MinW=100     close H=22
po:     ZAKLADKA H=26  W=natur.  activate H=22 W=natur. MinH=0 MinW=0     close H=22
```

Przycisk aktywujący zakładkę nosi `Classes="flat"` **dla wyglądu**, więc selektor
`Button.primary, Button.flat` (M2b) nadawał mu **geometrię akcji dialogowej**: `MinHeight` 28
(`Size.ControlProminent`) i `MinWidth` 100 (`Size.ActionMinWidth`).

> **⭐ RATYFIKOWANE PRZEZ UŻYTKOWNIKA (2026-08-02), po obejrzeniu obu wariantów:** *„Potwierdzam 26 px.
> Po porównaniu wariantów 30 i 26 widać, że 30 px jest po prostu za wysokie. 26 px wygląda lżej, mieści
> więcej zakładek w wierszu i lepiej wpisuje się w rytm Application Chrome. […] Wcześniejsze 30 px nie
> było świadomą decyzją projektową, tylko skutkiem ubocznym odziedziczonej geometrii przycisku
> (`Button.flat` → `Size.ControlProminent` + `Size.ActionMinWidth`). M3 przywraca właściwą geometrię
> paska zakładek zgodnie z zasadą, że **kontener definiuje rytm, a element go przyjmuje**."*

⭐ **To jest lekcja §17.2/3 w drugim wydaniu.** Bazowy `MinWidth` urósł raz strzałkę drzewa metadanych
z 20 do 100 px i dostała ona wtedy jawną ucieczkę (`Button.sidebar-chevron` ma własne `Width`/`Height`).
**Przycisk zakładki jej nie dostał**, a M2c nie mógł tego zobaczyć — jego liczniki mierzyły wyłącznie
`FontSize` / `CornerRadius` / `FontFamily`.

⚠ **Podłoga szerokości nie była kosmetyką.** `Size.ActionMinWidth` istnieje po to, żeby `Save` i `Cancel`
miały równą szerokość w stopce dialogu. Na pasku zakładek oznaczała, że **każda zakładka zajmuje ≥132 px
niezależnie od długości nazwy** — mniej zakładek w wierszu i szybsze przepełnienie paska wielowierszowego
(D5/D7), czyli działanie **wprost przeciw decyzji D6/§8.1**, która chroni pełną czytelność nazw.

#### §19.1.3 ⚠⚠ Dwie korekty, obie wymuszone POMIAREM w trakcie iteracji

**(a) Pozycja stylu w pliku jest częścią reguły.** Pierwsza wersja reguły kontenera stała zaraz za
`DataGridCell Button`. Pokonywała `Button.flat` (zadeklarowany wyżej), ale **przegrywała z `Button.icon`** —
przycisk zamykania dalej raportował `MinHeight=22`. Reguła musiała trafić **za wszystkie warianty
`Button.*`**, bo przy równej trafności rozstrzyga kolejność deklaracji (§17.5/5). ⛔ Nie przenosić w górę.

**(b) Selektor celuje w `.flat`, a nie w każdy `Button` — zawężenie PO pomiarze, nie z ostrożności.**
Wersja `Border.tab-strip Button` dała poprawne 26 px, ale **zabrała przyciskowi zamykania wysokość chromy**
(`Size.ControlToolbar` = 22 → **16 px**). Reguła naprawiłaby jeden cel kliknięcia i zepsuła drugi.
⭐ Defektem nigdy nie było *„przycisk w pasku ma wysokość"*, tylko *„przycisk zakładki nosi geometrię akcji
dialogowej"*. Przycisk zamykania **jest** ikoną chromy i 22 px to jego właściwa wysokość (R1).
**Zdejmujemy dokładnie to, co nie należy — nie więcej.**

#### §19.1.4 ⚠ Ograniczenie techniczne, które ukształtowało rozwiązanie

`RowDefinition.Height` i `Grid.RowDefinitions` operują na `GridLength`, a tokeny są `x:Double` —
`{DynamicResource}` **nie ma się na co skonwertować** (§3.2: katalog ma dwie warstwy, ale `GridLength`
nie jest żadną z nich). Dlatego **wysokości chromy stoją na `Height` / `MinHeight` ELEMENTÓW**, a wiersze
siatki są `Auto`. ⛔ Nie dodawać trzeciej warstwy katalogu z `GridLength` — byłaby drugą reprezentacją
tej samej liczby.

⚠ **Konsekwencja, którą trzeba było obsłużyć osobno:** wysokość wskaźnika aktywnej zakładki rezerwował
dotąd **sztywny wiersz `2`** w siatce, więc `IsVisible="False"` na dziecku nic nie kosztowało. Odkąd
wysokość niesie `Height` elementu, ukrycie zwinęłoby wiersz do zera i **zakładka aktywna byłaby o 2 px
wyższa od nieaktywnej**. Wskaźnik jest więc **zawsze obecny**, a zmienia się wyłącznie jego tło —
przez istniejącą klasę `active-tab`, czyli regułę sformułowaną pozytywnie (§17.2/3).

#### §19.1.5 Sonda wizualna — dlaczego powstała i dlaczego zostaje

**`tools/probes/TabStripVisualProbe`** renderuje pasek zakładek do PNG w obu motywach przy obu
wysokościach, przez Skia, na **tych samych** słownikach zasobów i tym samym `ControlStyles.axaml`,
których używa aplikacja.

⚠ Powstała, bo żadna istniejąca droga nie dawała obrazu: pasek zakładek jest **pusty bez połączenia
z bazą**, a testowa sesja headless działa z `UseHeadlessDrawing`, gdzie `CaptureRenderedFrame()` zwraca
**null** (nota R‑6). ⭐ Zostaje w repo, bo pytanie *„jak to wygląda przy dwóch wartościach tokenu"*
wróci w M3.3 (tryby paska zakładek) i na przeglądzie §13.3.

#### §19.1.6 Czego iteracja NIE zrobiła

⛔ Nie ruszono `Size.Row.Tree` (decyzja **DB** — wracamy po M3) · nie ruszono kolizji K1–K10 · nie
tknięto `AccentIconBrush`/`InfoIconBrush` (decyzja **DC** — M4.3/M5) · nie tknięto Commit/Rollback
(decyzja **DD** — iteracja M3.2c) · nie zmieniono sekcji, hierarchii ani zawartości paska statusu
(to M3.1b–M3.1f).

#### §19.1.7 Definition of Done

| # | Warunek | |
|---|---|---|
| 1 | zakres iteracji zamknięty | ✅ |
| 2 | każda pozostawiona wartość lokalna ma powód w miejscu | ✅ (nie przybyła żadna) |
| 3 | baza `DesignTokenComplianceTests` odzwierciedla stan faktyczny | ✅ bez zmian — iteracja nie dodała ani nie usunęła literału `FontSize`/`CornerRadius` |
| 4 | build 0/0 | ✅ |
| 5 | testy zielone w trzech partycjach | ✅ **7000 + 34 + 54 = 7088** |
| 6 | smoke + oba motywy | ✅ smoke czysty; oba motywy ocenione na renderach §19.1.5 |
| 7 | wpis w §19 | ✅ ta sekcja |
| 8 | commit; push po akceptacji | ✅ / ⏸ |

⚠ **Warunek 6 był spełniony tylko połowicznie i wyszło to dzień później** — patrz §19.2.

---

### §19.2 Poprawka odbiorcza M3.1a — wskaźnik aktywnej zakładki nie malował się wcale (2026-08-02)

> **Zgłoszenie użytkownika:** *„Aktywna zakładka jest teraz znacznie gorzej widoczna niż wcześniej.
> W poprzedniej wersji niebieski wskaźnik był wyraźniejszy… Nie chcę wracać do wysokości 30 px — 26 px
> zostaje. Chciałbym natomiast, żebyś poprawił widoczność aktywnej zakładki, a nie jej wysokość."*

#### §19.2.1 Odpowiedź na trzy pytania kontrolne — dwa razy „bez zmian", raz „nie malował się w ogóle"

| Pytanie | Odpowiedź |
|---|---|
| Czy grubość i warstwa są te same? | **Tak.** 2 px (`Size.TabIndicator` = dokładnie ta sama liczba, co poprzedni literał), ten sam wiersz 0 tej samej siatki, to samo dziecko malowane nad tłem rodzica. |
| Czy nie został przykryty albo optycznie zmniejszony? | **Nie został przykryty.** Nie był **malowany w ogóle**. |
| Czy kolor/kontrast nadal odpowiada Accentowi? | **Tak** — styl wskazuje `AccentBrush`, bez zmian. |

⭐ **Regresja była binarna, nie stopniowa** — akcent albo się maluje, albo nie. Dlatego **nie zwiększono
ani kontrastu, ani grubości**: po poprawce wskaźnik jest identyczny jak przed M3.1a, a przy wysokości
26 px zamiast 30 px czyta się nawet odrobinę mocniej (2/26 zamiast 2/30).

#### §19.2.2 ⚠⚠ Przyczyna — DOSŁOWNIE teza M2c, popełniona w pierwszej iteracji po jego zamknięciu

M3.1a przeniosło wysokość wskaźnika ze sztywnego wiersza siatki na `Height` elementu, a że wiersz przestał
rezerwować miejsce, `IsVisible` trzeba było zastąpić podmianą tła. Powstało to:

```xml
<Border Classes="tab-indicator" Height="{DynamicResource Size.TabIndicator}"
        Background="Transparent" />          <!-- ⛔ WARTOŚĆ LOKALNA -->
```
```xml
<Style Selector="Border.active-tab Border.tab-indicator">
  <Setter Property="Background" Value="{DynamicResource AccentBrush}" />   <!-- nigdy nie wygrał -->
</Style>
```

**Wartość lokalna bije setter stylu.** Styl był poprawny przez cały czas i nigdy nie miał szansy zadziałać.
Aktywna zakładka została z samą podmianą tła kafelka i pogrubieniem etykiety — czyli dokładnie
*„muszę się chwilę przyglądać"*.

⭐ **To jest teza M2c w jednym zdaniu** (*„dopóki wartość lokalna stoi w widoku, żadna reguła Design
Systemu nie działa"*), popełniona w **pierwszej iteracji po zamknięciu tamtego etapu**. Zapisane bez
łagodzenia, bo to najlepszy możliwy dowód, że reguła nie jest historyczna.

#### §19.2.3 ⚠⚠ Dlaczego przeszło przez WSZYSTKIE bramki — sonda mierzyła inny mechanizm niż produkt

Defekt minął: **build 0/0**, **7088 zielonych testów**, **czysty smoke** i — najważniejsze — **render sondy
wizualnej, na którym wskaźnik był widoczny.** Ostatni punkt jest jedyną prawdziwą lekcją:

```csharp
// sonda M3.1a — BŁĄD
var indicator = new Border { Classes = { "tab-indicator" }, Background = Brushes.Transparent };
if (active)
    indicator.Bind(Border.BackgroundProperty, new DynamicResourceExtension("AccentBrush"));
```

Sonda **wiązała tło bezpośrednio**, zamiast oprzeć się na klasie `active-tab` rodzica i stylu — czyli
**omijała dokładnie tę ścieżkę, którą zmieniała iteracja**. Obraz wychodził poprawny, bo powstawał innym
mechanizmem niż ten, który działa w aplikacji.

⭐ **To jest pułapka 12 (§17.5/7 — *„test potrafi mierzyć nie ten podmiot"*) w najdroższym możliwym
wydaniu: narzędzie zbudowane po to, żeby ocenić zmianę, potwierdziło stan, którego nie było.**
⚠ Sonda została naprawiona: buduje kafelek **wiernie jak XAML** (klasa na rodzicu + styl instancyjny)
i ma przełącznik `PROBE_LOCAL_TRANSPARENT=1`, który odtwarza defekt — renderowanie obu wariantów obok
siebie jest tym, co ostatecznie potwierdziło diagnozę.

#### §19.2.4 Poprawka — oba stany jako setter, w miejscu osiągalnym dla testu

Wskaźnik stracił atrybut `Background`, a **oba** jego stany przeniosły się do `ControlStyles.axaml`:

```xml
<Style Selector="Border.tab-indicator">                      <!-- spoczynek -->
  <Setter Property="Background" Value="Transparent" />
</Style>
<Style Selector="Border.active-tab Border.tab-indicator">    <!-- akcent -->
  <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
</Style>
```

⭐ **Stan spoczynkowy też musi być setterem** — gdyby „przezroczysty" wrócił do widoku jako atrybut,
defekt odtworzyłby się natychmiast i znowu bezgłośnie. Reguła jest sformułowana pozytywnie (§17.2/3):
wskaźnik **zawsze** istnieje i **zawsze** ma tło; zmienia się wyłącznie barwa.

⚠ **Przeniesienie do `ControlStyles.axaml` jest wymuszone testowalnością, nie porządkiem.** Reguła
wewnątrz `Border.Styles` w `MainWindow.axaml` jest **nieosiągalna dla jakiegokolwiek testu** — headless
test konstruujący `MainWindow` zawiesza suite (#94/#226/#286). Pozostałe style `active-tab` (podmiana tła
kafelka, pogrubienie etykiety) zostają w szablonie do czasu, aż **M3.3** skonsoliduje cały pasek.
✅ **Skonsolidowane w M3.3a (§19.22.3)** — i przeniesienie ODTWORZYŁO tę samą regresję jeden poziom wyżej,
bo kafelek niósł lokalne `Background`. Recepta ta sama: oba stany jako setter.

#### §19.2.5 ⭐ Dwa testy, bo żaden osobno by tego nie złapał

| Test | Co pilnuje | Gdzie |
|---|---|---|
| `TabStripPresentationTests.ActiveTabIndicator_PaintsTheAccent_AndAnInactiveOneDoesNot` | **styl istnieje i się rozwiązuje** — na gołym `Window`, przeciw mechanizmowi klas | partycja headless |
| `DesignTokenComplianceTests.TabIndicator_CarriesNoLocalBackground` | **widok nie przykrywa stylu** wartością lokalną | partycja główna |

⚠⚠ **Sam pierwszy test NIE złapałby tej regresji** — styl był poprawny; zawiniła wartość lokalna.
Sam drugi nie wychwyci literówki w kluczu zasobu. **Dwie połówki jednej gwarancji.**
⭐ Drugi zweryfikowany **podłożeniem naruszenia**: z przywróconym `Background="Transparent"` zawodzi
z właściwym komunikatem, po cofnięciu przechodzi.

⚠ **`{DynamicResource}` nadal nie rzuca przy literówce** (pułapka 14), dlatego pierwszy test porównuje
z **katalogiem**, a nie z literałem — i szuka pędzla **z wariantem motywu**, bo `FindResource(key)` bez
wariantu zwraca `UnsetValue` (gotcha #250; kosztowało jeden przebieg).

#### §19.2.6 Stan

Build **0/0** · suite **7090** w trzech partycjach (**7001 + 35 + 54**, +2 nowe testy) · smoke czysty.
⚠ Nowa klasa headless **musiała trafić do filtru partycji** — pominięta wpadłaby do partycji głównej
bez żadnego sygnału błędu (ten sam kształt, co martwe wykluczenie z §18.1.6).
⛔ Wysokości **nie zmieniono**: `Size.Row.Tab` zostaje **26**, rytm 36 / 26 / 24 bez zmian.

---

### §19.3 Iteracja 2 (M3.1b) — cztery sekcje Status Bara, hierarchia, D3 (2026-08-02)

> **Zakres:** §8.4.3 (sekcje) + §8.4.4 (hierarchia wizualna) + decyzja **D3**. Odebrane przez użytkownika
> po trzech rundach QA.

#### §19.3.1 ⭐⭐ H‑4 było GŁĘBSZE, niż mówi audyt — to nie była waga wizualna, tylko JEDNA WŁAŚCIWOŚĆ

Audyt (§1.3/H‑4) opisał defekt jako *„stan połączenia i komunikaty w identycznej wadze wizualnej"*.
Zmierzone: były **tą samą właściwością**.

```csharp
UpdateStatusFromConnection()  →  StatusText = "Connected to X" / "Disconnected"
SetError(ex.Message)          →  StatusText = <treść wyjątku>        // 2 miejsca wywołania
```

Nadpisywały się nawzajem, a całą „severity" niósł `bool IsStatusError`. ⭐ **Żadne stylowanie nie mogło
tego naprawić, bo nie było czego stylować osobno** — sekcje 1 i 2 z §8.4.3 nie mogły powstać przed
rozdzieleniem tej właściwości. To jest właściwa treść tej iteracji; układ kolumn był konsekwencją.

Powstały: **sekcja 1** — `ConnectionDisplayName` / `ConnectionEndpointLabel` (+ istniejące `IsConnected`,
`IsDeveloperModeActive`); **sekcja 2** — `StatusMessage` / `StatusMessageSeverity` (enum, nie bool) +
`StatusMessageBrushKey` / `StatusMessageGeometryKey` czytane z **`MessageBanner`**, czyli z tego samego
mapowania, którym maluje się log Messages w edytorze SQL. ⛔ Zero drugiej definicji severity.

#### §19.3.2 ⭐ Tożsamość połączenia ma jednego właściciela — pasek tytułu stracił blok

> **Decyzja użytkownika (2026-08-02), po przedstawieniu trzech wariantów:** *„Status Bar powinien stać się
> jedynym miejscem prezentacji bieżącego kontekstu połączenia… jedna informacja powinna mieć jednego
> właściciela. To zmniejszy liczbę elementów w najbardziej zatłoczonym wierszu okna i uprości późniejsze
> M3.2… pasek tytułu będzie odpowiadał wyłącznie za nawigację i polecenia."*

Konflikt był realny: §8.4.3 przypisuje nazwę połączenia i DEV MODE **paskowi statusu**, a jedno i drugie
stało wtedy w **pasku tytułu** — na podstawie świadomej, niedawnej decyzji (komentarz w kodzie nazywał je
*„key 'where am I connected' info"*). §0.1.2 („Application Chrome to JEDNA powierzchnia") czytałby dwie
kopie tej samej informacji jako defekt, nie jako redundancję.

Usunięte z paska tytułu: nazwa z podkreśleniem `ConnectedBrush`, badge DEV MODE, separator wewnętrzny
**oraz wiodący separator kolumny 1** — bez bloku po lewej byłby kreską opartą o krawędź okna, czyli tym
samym „chromą po nieistniejącym elemencie", które sprzątał sprint brandingowy. Kolumny paska tytułu
przenumerowane `Auto,Auto,*,Auto,Auto` → `Auto,*,Auto,Auto`.

⭐ **Efekt uboczny, który był jednym z powodów decyzji: z trzech przyczyn H‑3 zniknęły dwie.** Do M3.2a
zostaje `CanExportDdl`.

#### §19.3.3 ⚠⚠ Wyrównanie sekcji 1 — pierwsza hipoteza UPADŁA W POMIARZE

> **Zgłoszenie użytkownika:** *„`localhost:3050` jest delikatnie wyżej niż nazwa połączenia… jakby oba
> napisy nie siedziały na tej samej linii bazowej."*

Naturalna hipoteza — „różne rozmiary, więc linie bazowe się rozjeżdżają" — **jest fałszywa co do skali**:

```
nazwa     top=6,00  h=12  baseline=15,29   (11 px, SemiBold)
endpoint  top=7,00  h=11  baseline=15,45   (10 px)
```

**Różnica linii bazowych to 0,16 px w DIP.** Niewidoczna. Prawdziwa przyczyna jest o krok dalej:
pudełka mają **różne wysokości** (12 vs 11) i **różne pozycje** (6 vs 7), a `UseLayoutRounding` przycina
**KAŻDY ELEMENT OSOBNO** do piksela urządzenia — pomiar zapisany już w M2b przy R‑6. Przy 125% te dwa
topy lądują po przeciwnych stronach zaokrąglenia i różnica rośnie do całego piksela fizycznego.

⛔ **Dlatego naprawą NIE był margines.** Nudge naprawiłby jedno DPI i zepsuł pozostałe — a zgłaszający
pracuje właśnie na 125%, czyli tam, gdzie nudge byłby najbardziej kuszący i najbardziej mylący.

⭐ **Naprawa: nazwa i endpoint to dwa `Run`‑y w JEDNYM `TextBlocku`.** Silnik tekstu układa runy na
wspólnej linii bazowej **z konstrukcji** — jedno pudełko, jedno zaokrąglenie, jedna linia. Zmierzone po
zmianie: **jeden baseline (15,83), odchyłka 0,00**. ⛔ Nie rozdzielać z powrotem na dwa `TextBlocki`.
⚠ `Classes="subtle"` nie działa na `Run`, więc kolor idzie `{DynamicResource}` wprost (nadal motywowalny),
a odstęp niesie separator w `ConnectionEndpointLabel`, bo między runami nie ma `Spacing` kontenera.
Separator `·` odwzorowuje przy okazji makietę z §8.4.3.

⚠ **Świadomie NIE wyrównywane, z liczbami:** tekst badge'a **−0,38 px**, środek badge'a **+0,5 px**
(wysokość 13 w wierszu 24 — zaokrąglenie własnego pudełka), kropka **−0,33 px**. Wszystkie poniżej
piksela i — co ważniejsze — **żadna nie zależy od różnicy rozmiarów czcionek**, więc DPI ich nie
wzmocni tak, jak wzmacniało endpoint. Rozwiązanie strukturalne dla badge'a istnieje
(`InlineUIContainer` w tej samej linii), ale komplikuje bramkowanie widoczności i nie zostało wdrożone
„na wszelki wypadek".

#### §19.3.4 Badge DEV MODE — proporcja, i dlaczego to wartość lokalna

> **Zgłoszenie użytkownika:** *„poziomy padding jest trochę zbyt duży względem pionowego… zmniejsz tylko
> lewy/prawy o 1–2 px."*

`Padding` **6,1 → 4,1**; wysokość i font nietknięte. Wybrany koniec `−2`, bo zgłoszenie dotyczyło
**proporcji** (6:1 przy tekście 10 px Bold), a `5,1` byłby zmianą na granicy dostrzegalności.

⚠ **Zostaje wartością lokalną z powodem, a nie nowym tokenem.** Katalog nie ma roli dla paddingu chipa,
a jedyny drugi element tego rodzaju (`AggregationBarView`, `8,2`) niesie inną wartość — nowa rola albo
zmieniłaby tamten wygląd, albo powstałaby z **jednego** konsumenta wbrew R3. **Wyjątek jest lepszy od
błędnej roli (R12).** ⭐ To już drugi raz w M3, gdy wartość na powierzchni trwałej nie ma roli w katalogu
(pierwszy: `Size.TabIndicator`, utworzony, bo miał drugie zastosowanie — rail §8.4.1). Pytanie *„czy chipy
zasługują na wspólną rolę"* idzie do przeglądu **§13.3**, który zobaczy je wszystkie naraz.

#### §19.3.5 D3 — i test, który trzeba było PRZECELOWAĆ, a nie skasować

`AppVersionChip` usunięty razem z trzema osieroconymi stałymi `UiStrings`
(`StatusBarReady`, `StatusBarConnectedTo`, `StatusBarVersionFormat`).

⚠ **Istniał test, którego przedmiot D3 właśnie zlikwidowało** — `StatusBarShowsTheSameVersionAsAbout`,
napisany po tym, jak użytkownik zobaczył w pasku literał `EmberTern 0.1.0` niezgodny z About.
⭐ **Skasowanie byłoby leniwym odczytem.** Właściwością wartą utrzymania nie jest *„pasek statusu to
renderuje"*, tylko *„wersja trafia na ekran z `AppInfo`, a nie z literału"* — a ta ma po prostu nowy,
jedyny dom. Test przecelowany na **`AboutWindowShowsTheVersionFromAppInfo`**; `AppInfoTests` pilnuje
drugiej połowy (żadnego literału wersji pod `src/`).
⭐ Przy okazji **zszedł z konstruowania `MainWindow`** — udokumentowanego kształtu zawieszającego suite,
i to w klasie, w której hang jest zgłaszany (#94/#226/#286). `AboutWindow` ma bezparametrowy konstruktor
i buduje własny VM, więc asercja jest tańsza **i mocniejsza**.

#### §19.3.6 ⚠⚠ Błąd metody, który kosztował najwięcej — `CopyFromScreen` nie zrzuca OKNA

Próbowałem zweryfikować wygląd zrzutem robionym przez `System.Drawing.Graphics.CopyFromScreen` na
prostokącie okna z `GetWindowRect`. **Ta funkcja kopiuje EKRAN w danych współrzędnych, a nie okno** —
aplikacja była przykryta terminalem, więc dostałem cudzą zawartość i **błędnie zdiagnozowałem, że pasek
statusu w ogóle się nie renderuje**. Zdążyłem podłożyć jaskrawy znacznik diagnostyczny (czerwone tło,
wysokość 60), zanim użytkownik przysłał zrzut pokazujący, że pasek działa poprawnie.

> **Użytkownik:** *„Wszystko było dobrze, pasek był widoczny, niepotrzebnie zaczynasz z tym kombinować,
> lepiej poproś mnie o zrzut."*

⭐ **Reguła operacyjna na resztę etapu: weryfikację wizualną powierzchni trwałych zamawiamy u użytkownika,
nie u siebie.** Sonda renderująca komponent w izolacji (§19.1.5) jest dobra do porównania **wariantów
jednej kontrolki**; do pytania *„jak wygląda aplikacja"* jedynym wiarygodnym źródłem jest zrzut z żywej
aplikacji. ⚠ Znacznik diagnostyczny został cofnięty w całości (zweryfikowane grepem).

#### §19.3.7 Definition of Done

| # | Warunek | |
|---|---|---|
| 1 | zakres iteracji zamknięty | ✅ sekcje · hierarchia · D3 |
| 2 | pozostawione wartości lokalne mają powód w miejscu | ✅ `Padding="4,1"` badge'a |
| 3 | baza `DesignTokenComplianceTests` = stan faktyczny | ✅ bez zmian |
| 4 | build 0/0 | ✅ |
| 5 | testy zielone w trzech partycjach | ✅ **7001 + 35 + 54 = 7090** |
| 6 | smoke + oba motywy | ✅ smoke czysty; QA użytkownika na żywej bazie (Szkoleniowa, 2389 tabel) |
| 7 | wpis w §19 | ✅ ta sekcja |
| 8 | commit; push po akceptacji | ✅ / ⏸ (push po całym M3.1) |

> **Odbiór użytkownika:** *„Wygląda dobrze… Badge DEV MODE ma teraz właściwe proporcje i cała sekcja
> połączenia czyta się naturalnie."* ⛔ Do tej sekcji nie wracamy, chyba że wyjdzie rzeczywista regresja.

---

### §19.4 Iteracja 3 (M3.1c) — rail (2026-08-02)

> **Zakres:** §8.4.1 (rail jako górna krawędź paska) + §8.4.2 (stany i priorytet).
> Odebrane przez użytkownika: *„Executing – rail poprawnie przechodzi na Accent. Error – rail zmienia
> się na czerwony razem z komunikatem."*

#### §19.4.1 Realizacja — rail nie dodaje ani jednego piksela

Rail to **górna krawędź paska statusu**, która była tam zawsze jako separator obszaru roboczego.
`BorderThickness` przeszedł na nowy token **`Border.Rail` (`0,2,0,0`)**, a `BorderBrush` czyta
`RailBrushKey` przez `IconBrushConverter` — ten sam konwerter, którym maluje się severity komunikatu.
Grubość jest **stała w każdym stanie**, więc zmiana stanu nie może przesunąć układu (§13.3 specyfikacji).

⚠ `Border.Rail` musi być osobnym tokenem, mimo że niesie tę samą liczbę co `Size.TabIndicator`:
`BorderThickness` to `Thickness`, a token tamten to `x:Double` — §3.2, katalog ma dwie warstwy i nie
liczy w XAML. Powiązanie zapisane przy obu tokenach.

| Priorytet | Stan | Źródło | Pędzel |
|---|---|---|---|
| 5 | Error | `StatusMessageSeverity` | `ErrorBrush` |
| 4 | Warning | `StatusMessageSeverity` | `WarningBrush` |
| 3 | Debug Active | **nowa agregacja** | `DebugCurrentLineBarBrush` |
| 2 | Executing | `IsExecuting` | `AccentBrush` |
| 1 | Trace Active | **nowa agregacja** | `IconColor_Query` |
| 0 | Ready | — | `BorderBrush` — zwykły separator |

#### §19.4.2 ⭐ Agregacja po `WorkspaceTabs` — przeniesiona z M3.1e, bo rail jest jej pierwszym konsumentem

§19.0.3 zapowiedziało, że chipy Trace/Debugger nie mają źródła danych. Rail potrzebuje **tych samych
dwóch stanów**, więc agregacja powstała tutaj; M3.1e dokłada na nią już tylko chipy. To resekwencjonowanie
wewnątrz M3.1, nie zmiana zakresu.

`IsDebugSessionLive` = jakakolwiek zakładka `Debugger` w fazie `Busy`/`Paused`;
`IsTraceSessionLive` = jakikolwiek `TraceMonitor` w stanie innym niż `Stopped`/`Faulted`.
⚠ To **nie** jest `IsDebuggerTabActive`, które znaczy *„ta zakładka jest wybrana"* — sygnał ma być
prawdziwy, gdy sesja żyje na **innej** zakładce. Podpięcie idzie przez **istniejący** hak
`WorkspaceTabs.CollectionChanged` (Seam 6d) — jeden punkt wiązania, nie ~39 miejsc dodawania zakładek.

⚠⚠ **Odpinanie subskrypcji jest wymogiem POPRAWNOŚCI, nie higieny:** zakładka zamknięta, ale wciąż
podpięta, trzymałaby rail zapalony po sesji, której już nie ma. ⭐ `Clear()` (rozłączenie) raportuje
`Reset` **bez `OldItems`**, więc odpięcie po zdarzeniu jest niewykonalne — stąd własny zbiór
`_railSources`. Bez niego rozłączenie zostawiałoby zapalony rail i wyciek.

#### §19.4.3 ⚠⚠ Strażnik — bo rail nie czyta przez `{DynamicResource}`

`IconBrushConverter` przy nieznanym kluczu zwraca `UnsetValue`, a wtedy `BorderBrush` po cichu zostaje
przy wartości domyślnej. **Literówka albo pędzel zdefiniowany tylko w jednym motywie nie zawiodłyby
buildu, żadnego innego testu ani nie rzuciłyby wyjątku** — rail po prostu przestałby sygnalizować,
w jednym motywie albo w obu. `DesignTokenApplicationTests.RailStateBrush_ResolvesInBothThemes` pinuje
sześć kluczy × dwa motywy; **zweryfikowany podłożeniem literówki** (`IconColor_Querry` → czerwony,
po cofnięciu zielony).

#### §19.4.4 ⏸ ODŁOŻONE DO M3b/M4 — semantyka kolorów railu (uwaga użytkownika + POMIAR)

> **Użytkownik, po QA:** *„aktywny Debugger i zwykłe wykonywanie SQL są bardzo podobnie sygnalizowane
> kolorystycznie… największą wartością tego raila jest możliwość natychmiastowego rozpoznania, co
> w danej chwili robi aplikacja, bez czytania tekstu… Nie proponuję jednak zmieniać tego w M3.1c…
> bardziej widzę to jako temat do późniejszego etapu (M3b/M4), kiedy wszystkie źródła aktywności będą
> już podłączone i będzie można zaprojektować pełną semantykę kolorów zamiast podejmować decyzję tylko
> dla jednego przypadku."*

⭐ **Obserwacja potwierdzona pomiarem — to nie jest wrażenie:**

| Stan | Dark | Light | Uwaga |
|---|---|---|---|
| Executing | `#2D6BBF` | `#2D6BBF` | |
| Debug | `#5A8AC8` (α `E6`) | `#0033B3` (α `CC`) | ⚠ **ta sama rodzina barwna co Accent** — różnica głównie w jasności |
| Trace | `#B0BEC5` | `#455A64` | ⚠ w Light ciemny szaroniebieski — jako sygnał słaby |

⚠⚠ Dodatkowo `DebugCurrentLineBarColor` jest **półprzezroczysty i zaprojektowany jako pasek bieżącej
linii W EDYTORZE** — rail pożycza kolor z innej domeny, co jest dokładnie tym rodzajem cichego
rozjazdu, przed którym broni §7.5.

⭐ **Teza użytkownika o istniejącej ikonografii się broni:** D15.2 wprowadziło `DebugLoopIconBrush`
(teal) **właśnie po to**, żeby nie kolidować z fioletem słów kluczowych PSQL. Reguła *„każdy moduł ma
własną barwę"* jest w projekcie już zapisana — rail jej po prostu nie realizuje.

⛔ **Świadomie NIE rozstrzygane w M3.1c.** Powód jest ten sam, dla którego §18.R nie rozstrzygał kolizji
pojedynczo: **pełna semantyka kolorów aktywności wymaga kompletu źródeł**, a Import danych i pozostałe
operacje podłącza dopiero **M3b**. Decyzja podjęta teraz opierałaby się na dwóch przypadkach z pięciu.
→ **wejście do M3b i do przeglądu §13.3.**

#### §19.4.5 Definition of Done

| # | Warunek | |
|---|---|---|
| 1 | zakres iteracji zamknięty | ✅ rail + agregacja |
| 2 | pozostawione wartości lokalne mają powód | ✅ brak nowych |
| 3 | baza strażnika = stan faktyczny | ✅ bez zmian |
| 4 | build 0/0 | ✅ |
| 5 | testy zielone w trzech partycjach | ✅ **7001 + 41 + 54 = 7096** (+6) |
| 6 | smoke + QA na żywej bazie | ✅ Executing i Error potwierdzone przez użytkownika |
| 7 | wpis w §19 | ✅ ta sekcja |
| 8 | commit; push po akceptacji | ✅ / ⏸ (push po całym M3.1) |

---

### §19.5 Iteracja 4 (M3.1d) — chip transakcji z czasem trwania (2026-08-02)

> **Zakres:** §8.4.5 — chip transakcji w sekcji 3 paska statusu. Odebrane przez użytkownika.

#### §19.5.1 ⭐⭐ TREŚCIĄ TEJ ITERACJI NIE BYŁ CHIP, TYLKO PODZIAŁ WŁASNOŚCI JEDNEGO FAKTU

Chip sam w sobie to kilkanaście linii XAML. Rzeczywista decyzja iteracji jest gdzie indziej i jest
**decyzją użytkownika**: fakt *„mam otwartą transakcję"* miał do tej pory właściciela w pasku nad
wynikami edytora SQL — kropkę stanu (`IsTransactionIdle` / `Active` / `Error`) plus etykietę
*„Active Transaction"*. Ten pasek jest bramkowany `IsQueryTabActive`, więc **fakt o stanie CAŁEJ
aplikacji znikał, gdy tylko użytkownik przechodził do debuggera albo do edytora obiektu** — czyli
dokładnie tam, gdzie dalej pracuje w tej samej transakcji.

> **Użytkownik (2026-08-02), zapytany, czy chip nie będzie redundancją wobec paska edytora:**
> *„to nie jest zbędna redundancja, tylko dwa różne poziomy informacji… przechodząc do debuggera albo
> edytora obiektu nadal chcę wiedzieć, że mam otwartą transakcję — ale licznik instrukcji przestaje być
> istotny, gdy nie pracuję już w SQL Editorze."*

⭐ **To rozstrzygnięcie zamienia dwie kopie w dwa poziomy** i dopiero ono czyni chip zgodnym z §0.1.2
(*„Application Chrome to JEDNA powierzchnia"*), który dwie niezależne prezentacje tego samego stanu
czytałby jako defekt:

| Powierzchnia | Pytanie | Zasięg |
|---|---|---|
| **Chip w pasku statusu** | *„czy mam otwartą transakcję i od jak dawna?"* | **globalny** — widoczny na każdej zakładce |
| **Pasek nad wynikami edytora SQL** | *„ile instrukcji poszło w tej transakcji?"* | **lokalny** — tylko tam, gdzie instrukcje powstają |

Pasek edytora stracił zatem **trzy `Ellipse` stanu i etykietę `TransactionBarActive`**;
`BuildTransactionBarText` zwraca dziś sam licznik (a przy braku instrukcji — pusty łańcuch).
⛔ Kropka i etykieta **nie wracają** — komentarze mówią to w obu miejscach, bo *„pasek transakcji
zgubił kropkę stanu"* jest bardzo wiarygodnie brzmiącą regresją.

⚠ **Stan `Error` został w pasku edytora celowo** (`TransactionBarError`): błąd transakcji jest
komunikatem o tym, co się właśnie stało z **wykonaniem**, a nie trwałym atrybutem sesji — i tam jest
czytany. Chip również przechodzi wtedy na `ErrorBrush`, więc sygnał nie ginie po zmianie zakładki.

#### §19.5.2 Czas trwania — obietnica z §8.4.5 zrealizowana, i to bez dotykania Core

§8.4.5 zapisało czas trwania jako **niepewny**: *„czy da się je odczytać tanio i bez odpytywania `MON$`,
jest do sprawdzenia w M3. Jeśli nie — chip pokazuje sam stan"*. Iteracja 0 rozstrzygnęła to na TAK
(§19.0.2); ta iteracja to realizuje. Wariant rezerwowy **nie był potrzebny**.

Znacznik czasu powstaje i ginie **w jednym miejscu** — w istniejącym handlerze `TransactionStateChanged`,
na już policzonych flagach `becameActive` (`:7527`) i `settled`:

```csharp
if (becameActive) _transactionStartedAt = DateTimeOffset.UtcNow;
if (settled)      _transactionStartedAt = null;
```

⭐ **Chip nie dostał własnej maszyny stanów** — czyta tę, która już rozstrzyga o Commit/Rollback,
o pasku edytora i o railu. Zero zapytań do serwera, zero round-tripów, **zero zmian w `EmberTern.Core`
i w `EmberTern.Firebird`**.

⚠ **Konsekwencja przyjęta świadomie i zapisana w kodzie:** to czas mierzony **zegarem klienta** od chwili,
w której EmberTern otworzył transakcję — nie odczyt z serwera. Dla pytania *„jak długo JA to trzymam"*
jest to właściwa odpowiedź; dla pytania *„od kiedy ta transakcja istnieje na serwerze"* właściwym
narzędziem pozostaje Session Manager i jego detektor long-running transaction.

#### §19.5.3 ⚠ Zgrubność formatu jest DECYZJĄ, nie uproszczeniem

`FormatTransactionDuration` daje `12 s` → `3 min` → `1 h 7 min`. Sekundy i minuty są **obcinane w dół**,
nigdy zaokrąglane — `59 s` nie może przeskoczyć na `1 min`, zanim minuta faktycznie minie.

⭐ **Powód jest ten sam, dla którego pasek nie rośnie:** pasek statusu czyta się **kątem oka**, a
`02:37.4` wymaga *czytania*. Precyzyjny pomiar ma już właściciela — `ExecutionTimer` w toolbarze edytora,
inne pytanie i inna precyzja. ⛔ Nie podbijać dokładności chipa „bo się da".

#### §19.5.4 ⭐ Testowalność wymusiła kształt kodu — funkcja czysta obok timera

Chip składa się z dwóch rzeczy o skrajnie różnej testowalności: z **faktu** (`_transactionStartedAt`)
i z **odświeżania tekstu**, które napędza `DispatcherTimer`. Gotcha **#251** mówi wprost, że wyzwalacz
oparty wyłącznie na timerze jest **nieosiągalny dla testu headless** — więc formatowanie zostało wydzielone
jako **funkcja czysta biorąca `TimeSpan`, a nie zegar**. Timer woła wyłącznie `OnPropertyChanged`;
cała treść, którą warto pinować, leży poza nim. Nowa klasa `TransactionChipTests` — **10 przypadków, bez
jednego `Sleep`** i bez konstruowania `MainWindow` (pułapka §9.1/4), bo funkcja jest statyczna.
⚠ Klasa **nie jest headless**, więc do filtra partycji headless **nie trafia** — sprawdzone, nie założone.

⚠ **Zacisk ujemnego czasu nie jest asekuracją teoretyczną.** Znacznik pochodzi z zegara klienta, więc
korekta NTP albo ręczna zmiana czasu systemowego **w trakcie otwartej transakcji** cofa „teraz" za moment
startu. Chip ma wtedy pokazać `0 s`, a nie `-3 s` ani wyjątek — i to jest osobny, pinowany przypadek.

⚠ **Timer chodzi tylko wtedy, gdy chip jest widoczny.** `UpdateTransactionChipTimer` zatrzymuje go przy
przejściu do Idle; inaczej aplikacja tykałaby co sekundę przez całą sesję po jednej zamkniętej transakcji.
Priorytet `Background` — odświeżenie napisu nigdy nie może konkurować z wpisywaniem tekstu.

#### §19.5.5 Odstępstwa od projektu z §8.4.5 — dwa, oba zapisane

| Projekt §8.4.5 | Jak jest | Dlaczego |
|---|---|---|
| `⚡ Transakcja · <czas>` | **kropka** 7×7 w kolorze stanu + `Transaction · <czas>` | ⚡ jest glifem, a nie ikoną z `IconGeometries.axaml`; ⭐ kropka to ten sam nośnik, który pasek edytora właśnie **oddał** — informacja zmieniła miejsce, nie formę |
| dwa stany (brak / aktywna) | **trzy** — doszedł `Error` na `ErrorBrush` | `TransactionService` ma trzeci stan i był on prezentowany także wcześniej; pominięcie go w chipie oznaczałoby, że globalny nośnik faktu jest **uboższy** niż lokalny, który zastępuje |

⚠ Chip **nie** czyta pędzla przez `{DynamicResource}`, tylko przez `IconBrushConverter` z klucza
`TransactionChipBrushKey` — bo pędzel zależy od stanu. To ta sama ścieżka, którą maluje się rail
(§19.4.3), **z tą samą pułapką**: nieznany klucz daje `UnsetValue`, a kontrolka po cichu zostaje przy
wartości domyślnej. Oba klucze (`TransactionActiveBrush`, `ErrorBrush`) są zdefiniowane w **obu** motywach
— zweryfikowane w `Colors.axaml`.

⚠ **Wartości lokalne pozostawione z powodem w miejscu:** kropka `7×7` (geometria — §18.0.5 ratyfikowało,
że geometrii wychodzącej z arytmetyki nie tokenizujemy) oraz `Spacing="5"`, które jest **kolizją**
(`Space.Sm` = 6, różnica 1 px) i dlatego trafiło do rejestru **§18.R jako K11**. Tekst chipa czyta rolę
`Text.Caption.Size` — bez wyjątku.

#### §19.5.6 Definition of Done

| # | Warunek | |
|---|---|---|
| 1 | zakres iteracji zamknięty | ✅ chip + czas + podział własności z paskiem edytora |
| 2 | pozostawione wartości lokalne mają powód w miejscu | ✅ kropka 7×7, `Spacing="5"` (+ K11 w §18.R) |
| 3 | baza `DesignTokenComplianceTests` = stan faktyczny | ✅ bez zmian — strażnik liczy `FontSize`/`FontFamily`/`CornerRadius`, a chip żadnej z nich lokalnie nie deklaruje |
| 4 | build 0/0 | ✅ |
| 5 | testy zielone w trzech partycjach | ✅ **7011 + 41 + 54 = 7106** (+10, `TransactionChipTests`) |
| 6 | smoke + oba motywy | ✅ QA użytkownika: chip widoczny niezależnie od zakładki, znika po Commit/Rollback |
| 7 | wpis w §19 | ✅ ta sekcja |
| 8 | commit; push po akceptacji | ✅ / ⏸ (push po całym M3.1) |

> **Odbiór użytkownika:** chip pokazuje czas trwania, jest widoczny niezależnie od aktywnej zakładki
> i znika po Commit / Rollback; rozdział odpowiedzialności (Status Bar = stan globalny, edytor SQL =
> licznik lokalny) zaakceptowany.

#### §19.5.7 ⏸ Zapis do przyszłego przeglądu UX — NIE realizować w M3.1

> **Użytkownik, przy odbiorze M3.1c:** semantyka kolorów railu wymaga kompletu źródeł aktywności.

Do §19.4.4 dochodzi obserwacja z tej iteracji: **kolor jest już czwartym nośnikiem w pasku statusu**
(rail, severity komunikatu, chip transakcji, a wkrótce chipy Trace/Debuggera). Propozycja z odbioru —
SQL → Accent, Debugger → własny kolor, Trace → własny, Import → własny — **zostaje zapisana i nie jest
realizowana w M3.1**; wchodzi razem z §19.4.4 do **M3b** i do przeglądu **§13.3**, kiedy widać komplet.

---

### §19.6 Iteracja 5 (M3.1e) — chipy Trace i Debuggera (2026-08-02)

> **Zakres:** §8.4.3 sekcja 3 — prezentacja dwóch pozostałych chipów. Agregacja powstała już w M3.1c
> (§19.4.2), więc iteracja jest wyłącznie prezentacyjna. Odebrana przez użytkownika po trzech rundach QA
> ikony — i to one, a nie chipy, są jej najważniejszą treścią (§19.6.6).

#### §19.6.1 Ten sam podział własności, co w M3.1d — i to jest potwierdzenie, nie powtórzenie

Sekcja 2 pokazuje `ActiveDebugger.StatusText` bramkowane **`IsDebuggerTabActive`**. To znowu *kontekst
aktywnej zakładki* („Paused, linia 14") obok *faktu globalnego* („gdzieś żyje sesja"), czyli dokładnie
układ, który użytkownik ratyfikował w §19.5.1. ⛔ Nie jest to redundancja: bramka czyni tamten nośnik
niewidocznym **dokładnie tam, gdzie fakt globalny zaczyna mieć znaczenie** (§0.1.2).

⭐ **Że ten sam wzorzec wyszedł drugi raz, niezależnie, jest sygnałem o Status Barze jako całości:**
każda sekcja, która ma nieść stan globalny, musi być sprawdzona pod kątem *„czy ten fakt ma już
właściciela i czy tamten nie jest bramkowany zakładką"*. To pytanie wchodzi do procedury M3.1f i dalej.

Tooltipy **reużywają istniejących producentów tekstu** — `DebuggerTabViewModel.StatusText` i
`TraceMonitorTabViewModel.StatusText` (to drugie już mapuje `TraceSessionState` na „Recording · 12/40
events"). ⛔ Zero drugiego mapowania stanu w aplikacji. Etykieta niesie FAKT, tooltip SZCZEGÓŁ.

⚠ Etykiety są **rzeczownikami** („Debug", „Trace"), nie czasownikami: chip mówi, **co jest prawdą**,
a rail mówi, **co się dzieje** (§8.4.1). Gdyby chip mówił „Debugging", dublowałby rolę railu słowem.

#### §19.6.2 ⚠⚠ CHIPY NIE DZIEDZICZĄ PĘDZLI RAILU — i to jest decyzja z pomiaru, nie z estetyki

Naturalny odruch („ten sam stan, ten sam kolor") jest tutaj **błędny**, i to mierzalnie. Kontrast na tle
`PanelBrush`, przy `Text.Caption.Size` = **10 px Normal**, czyli progu §10 = **4,5:1**:

| Pędzel | Dark | Light | Jako tekst 10 px |
|---|---|---|---|
| `DebugCurrentLineBarBrush` (rail debuggera) | **3,77:1** | 5,69:1 | ⛔ **Dark nie przechodzi** |
| `AccentIconBrush` | 5,17:1 | 4,81:1 | ✅ |
| `IconColor_Query` (rail Trace) | 8,03:1 | 6,58:1 | ✅ |

Przyczyna jest strukturalna: `DebugCurrentLineBarColor` jest **półprzezroczysty** (α 0,90 / 0,80), bo
zaprojektowano go jako pasek bieżącej linii **w edytorze**.

⭐⭐ **Wniosek ogólniejszy niż ta iteracja: ten sam token przechodzi jako RAIL i nie przechodzi jako
TEKST.** §10 stawia 3:1 dla elementu UI i 4,5:1 dla tekstu, więc rail przy 3,77 jest poprawny (M3.1c
zostaje bez zmian), a napis o tym samym kolorze już nie. **Zgodność wizualna między railem a chipem nie
jest argumentem, bo to nie są elementy tej samej klasy dostępności.**

Chip debuggera bierze więc `AccentIconBrush` — kolor, który trójkąt `DebuggerIcon` **i tak nosi**, więc
znak i napis czytają się jako jeden obiekt. ⭐ **Ratyfikowane przez użytkownika:** *„Rail i chip pełnią
inną funkcję, więc nie muszą używać identycznego pędzla… nie chciałbym świadomie schodzić poniżej progu
kontrastu dla tekstu tylko po to, żeby chip miał dokładnie ten sam kolor co rail."*

⏸ Pełna semantyka kolorów aktywności (SQL / Debugger / Trace / Import — każdy własny kolor) **pozostaje
odłożona** do M3b i bramy §13.3 (§19.4.4). Użytkownik potwierdził to ponownie przy tej decyzji.

#### §19.6.3 ⚠ Znalezisko o CHIPIE TRANSAKCJI (M3.1d) — zgłoszone, świadomie NIE naprawione

Ten sam pomiar objął chip wysłany iterację wcześniej: `TransactionActiveBrush` daje **7,41:1 w Dark**,
ale **4,18:1 w Light** — poniżej progu 4,5 dla tekstu. (Kropka jest w porządku: element UI, próg 3:1.)

⭐ **Decyzja użytkownika — zapis do §13.3, bez zmiany teraz:** *„To nie jest błąd funkcjonalny ani
regresja, tylko niewielka odchyłka kontrastu w jednym motywie… Nie chciałbym teraz zmieniać
`TransactionActiveColor`, bo to token współdzielony i taka korekta mogłaby wpłynąć również na innych
konsumentów. Lepiej rozstrzygnąć to razem z pełnym przeglądem semantyki kolorów."* Precedens: **V‑1**
(kolor komentarzy SQL, 4,14:1), ratyfikowany do pozostawienia tą samą logiką.

⚠⚠ **W całym repo NIE MA strażnika kontrastu.** §10 stawia progi, §11 mówi o egzekwowaniu, ale nic ich
nie sprawdza — więc ta odchyłka nie mogła zawieść żadnego testu i nie zawiedzie następnej.
⭐ Użytkownik uznał strażnika za dobry pomysł i **osobną pracę infrastrukturalną**, świadomie nie
doklejaną do M3.1e. → wejście do backlogu i do §13.3.

#### §19.6.4 ⭐ Defekt złapany na sobie przed buildem — alias, którego nikt by nie podniósł

Pierwsza wersja miała `ShowDebugChip => IsDebugSessionLive` i `ShowTraceChip => IsTraceSessionLive`,
a `RaiseActivityChanged` podnosiło tylko te drugie. **Chipy nigdy by się nie pojawiły — przy zielonym
buildzie i zielonych testach.**

⭐ Naprawa przez **usunięcie aliasów**, nie przez dodanie czwartego `OnPropertyChanged`: warunek
pokazania jest tu **tożsamy z faktem**, więc druga nazwa nie niosła nic poza ryzykiem. Widok wiąże się
wprost z `IsDebugSessionLive` / `IsTraceSessionLive`.
⚠ Chip transakcji **zachowuje** `ShowTransactionChip`, bo tam warunek jest ZŁOŻONY (`aktywna || błąd`) —
i ta różnica jest zapisana w kodzie, żeby ktoś nie „ujednolicił" jej w którąkolwiek stronę.

#### §19.6.5 `RaiseRailChanged` → `RaiseActivityChanged`

Od tej iteracji ta sama agregacja karmi **dwóch konsumentów o różnych rolach** — rail (jeden stan,
najwyższy priorytet) i chipy (współistniejące fakty). Stara nazwa byłaby historią, nie
odpowiedzialnością (reguła nazewnicza projektu). Podmienione **tylko wywołania**; wzmianka o starej
nazwie została w komentarzu celowo.

#### §19.6.6 ⭐⭐ TRZY RUNDY QA IKONY — I LEKCJA, KTÓRA JEST WAŻNIEJSZA NIŻ IKONA

Chip postawił `DebuggerIcon` przy 12 px, czyli mniejszym niż kiedykolwiek wcześniej (zakładka 14,
toolbar 16). Przy tej skali kropka przerwania **czytała się jak artefakt renderowania**.

**Runda 1 — kropka w dół (16; 15,5) → (16; 20).** Zmierzone: nachodzenie to −1,45 j. prześwitu przy
obrysie 2 px, czyli **0,72 px** dwóch wygładzanych kształtów zlewających się przy 12 px.
> **Użytkownik:** *„kropka jest już odrobinę za nisko… problem wynika z tego, że próbujemy zrobić
> miejsce tylko przesuwając ją w dół."*

⭐ **Trafna diagnoza mechanizmu: miejsce zrobione przez odsunięcie jednego elementu jest miejscem
zabranym kompozycji.** Płaciłem za prześwit jedyną walutą, jaką sobie zostawiłem.

**Runda 2 — przekomponowanie trójkąta** (większy, dosunięty w prawo, kropka pod wierzchołkiem).
Mierzyło się **lepiej** (+2,49 j. i kropka wyżej jednocześnie) i przy okazji zniosło realną wadę: stary
znak miał margines ink **5 j.**, podczas gdy sąsiadujący z nim w tym samym chipie `Icon.Activity` ma
**1 j.** — mark debuggera był optycznie mniejszy od ikon, obok których stoi.
> **Użytkownik odrzucił to na pierwszy rzut oka:** *„zgubiliśmy ich wspólną tożsamość… Execute i Debug
> powinny wyglądać jak rodzina ikon… spójność całego zestawu ikon ma większą wartość niż poprawienie
> jednego szczegółu kosztem charakteru ikony."*

⛔⛔ **TO JEST LEKCJA ETAPU, NIE TYLKO TEJ IKONY: spójność zestawu bije optimum pojedynczego znaku.**
Wariant 2 wygrywał w każdej liczbie, którą umiałem policzyć — prześwit, margines ink, pozycja kropki —
i **przegrywał w jedynym wymiarze, którego nie mierzyłem**. To R8 („pomiar jest narzędziem, nie
argumentem końcowym") w najczystszej postaci, i R7 w drugiej połowie: szukałem reguły w obrębie jednego
znaku zamiast w obrębie rodziny.

**Runda 3 — POMIAR OBALIŁ ZAŁOŻENIE OBU STRON.** Sprawdzone przed cofnięciem: znak **nigdy nie był**
ikoną Execute.

| | geometria |
|---|---|
| `Icon.Play` (Execute) | **(8,5) (19,12) (8,19)** — 11 × 14 |
| `DebuggerIcon` „oryginalny" | **(6,4) (18,12) (6,20)** — 12 × 16 |

⭐ **Rodzina była PRZYBLIŻENIEM utrzymywanym ręcznie**, więc powrót „do poprzedniego kształtu" —
o co użytkownik dosłownie prosił — odtworzyłby podatność, a nie usunął ją. Zasada użytkownika
(*„Debugger to po prostu ikona Execute z dodaną czerwoną kropką"*) była **mocniejsza niż jego własna
instrukcja**, więc wykonana została zasada:

```xml
<Path Data="{StaticResource Icon.Play}" … />
```

Trójkąt **nie ma już własnej ścieżki**. Pokrewieństwo jest **strukturalne**: nie może się rozjechać,
bo nie ma dwóch rzeczy, które mogłyby się rozjechać — a zmiana `Icon.Play` pociąga znak debuggera
automatycznie. Ruszyła się **wyłącznie kropka**: (16; 15,5) → **(19; 19)**, gdzie x = 19 to własny x
wierzchołka, więc przerwanie siedzi na końcu wskaźnika wykonania. Prześwit **+2,66 j. = 1,33 px @12** —
więcej niż w odrzuconym wariancie 2, **bez ruszania znaku bazowego**.

> **Odbiór użytkownika:** *„Debugger jest teraz rzeczywiście wariantem ikony Execute, a nie osobnym
> znakiem, więc cały zestaw ikon odzyskał spójność. To rozwiązanie jest lepsze architektonicznie niż
> dalsze ręczne dostrajanie geometrii."* ⛔ **Temat ikony ZAMKNIĘTY** — nie szlifujemy jej dalej.

⚠ **Reguła na przyszłość, zapisana też przy geometrii:** jeżeli kropka kiedykolwiek znów będzie
potrzebowała miejsca — **rusza się KROPKA**. Znak bazowy nie jest częścią regulowaną.
⚠ Jeden `ControlTheme` obsługuje wszystkie rozmiary (chip 12, zakładka 14, przyciski 16); wariant
per-skala **nie powstał** i nie powinien — dla jednego konsumenta łamałby R3.

#### §19.6.7 Dwa strażniki, oba zweryfikowane podłożeniem wady

**(a) `StatusBarChipBrush_ResolvesInBothThemes_AndIsOpaque`** — cztery pędzle chipów × dwa motywy, plus
wymóg **nieprzezroczystości**. To asercja o kontraście, nie o stylu: pilnuje §19.6.2, bo „ujednolicenie"
chipa z railem wygląda jak porządkowanie, a zeszłoby poniżej progu §10 **bez żadnego sygnału**
(`{DynamicResource}` nie rzuca przy nieznanym kluczu, a przy istniejącym-lecz-półprzezroczystym tym
bardziej). Podłożono `DebugCurrentLineBarBrush` → *„jest półprzezroczysty w motywie Dark (α = 230)"*.

**(b) `DebuggerIcon_IsTheExecuteIcon_ByReferenceNotByCopy`** — porównuje **tożsamość instancji**
geometrii, nie zgodność współrzędnych. ⭐ To celowe i to jest cała jego wartość: podłożono wpisaną
ścieżkę **identyczną co do liczb** i test i tak upadł (*„Values are not the same instance"*). Kopia
o identycznych współrzędnych przeszłaby test na równość i przywróciłaby dokładnie tę podatność, którą
referencja usuwa. ⚠ Build tego nie pokrywa — `{StaticResource}` w `ControlTemplate` rozwiązuje się przy
instancjonowaniu, więc kontrolkę trzeba naprawdę zbudować i pokazać.

⚠ Oba mieszkają w `DesignTokenApplicationTests`, nie w `ConnectionExpandBindingProbe` — istniejąca
w sondzie asercja `new DebuggerIcon()` **nie stosuje szablonu**, więc nie złapałaby zerwanej referencji,
a sonda jest udokumentowanym kształtem zawieszającym suite (§9.1/4 handovera).

#### §19.6.8 Definition of Done

| # | Warunek | |
|---|---|---|
| 1 | zakres iteracji zamknięty | ✅ dwa chipy + zamknięta korekta ikony |
| 2 | pozostawione wartości lokalne mają powód w miejscu | ✅ bez nowych (chipy czytają role; geometria ikony to nie token) |
| 3 | baza `DesignTokenComplianceTests` = stan faktyczny | ✅ bez zmian |
| 4 | build 0/0 | ✅ |
| 5 | testy zielone w trzech partycjach | ✅ **7011 + 46 + 54 = 7111** (+5) |
| 6 | smoke + oba motywy | ✅ QA użytkownika na żywej bazie, trzy rundy ikony |
| 7 | wpis w §19 | ✅ ta sekcja |
| 8 | commit; push po akceptacji | ✅ / ⏸ |

#### §19.6.9 Co ta iteracja zostawia następnym

1. ⭐ **Pytanie kontrolne dla każdej kolejnej sekcji Status Bara:** czy ten fakt ma już właściciela
   i czy tamten nie jest bramkowany zakładką? Dwa razy z rzędu odpowiedź brzmiała „tak" (§19.5.1, §19.6.1).
2. ⚠ **Kontrast: rail i tekst to różne progi.** Nie przenosić pędzla między nimi bez policzenia.
3. ⏸ **Strażnik progów §10** — osobna praca infrastrukturalna, do backlogu i §13.3.
4. ⏸ **`TransactionActiveBrush` w Light 4,18:1** — do §13.3, razem z semantyką kolorów.
5. ⛔ **Ikona debuggera zamknięta.** Rusza się kropka, nigdy znak bazowy.

---

### §19.7 Iteracja 6 (M3.1f) — sekcja postępu + operacja referencyjna (2026-08-02)

> **Zakres:** §8.4.6 — sekcja 4 paska statusu plus **jedna** operacja referencyjna (wykonanie zapytania
> SQL), zgodnie z ratyfikowanym podziałem **D4**. Odebrane przez użytkownika.
> ⭐ **Tym samym M3.1 jest domknięte co do zakresu** — wszystkie cztery sekcje §8.4.3 istnieją.

#### §19.7.1 ⭐ Trzeci raz ten sam podział własności — i tym razem jest już regułą, nie obserwacją

`QueryStatsText` pełniło **dwie role naraz**: w trakcie `„Loading… 12 345 rows"` (POSTĘP), po zakończeniu
`„143 rows in 46 ms"` (WYNIK) — i było bramkowane `IsQueryTabActive`, więc postęp znikał po przełączeniu
zakładki. Dokładnie kształt z §19.5.1 (transakcja) i §19.6.1 (debugger).

> **Użytkownik:** *„Sekcja postępu → pokazuje to, co dzieje się teraz. `QueryStatsText` → pokazuje
> rezultat ostatnio zakończonej operacji… Żadna informacja nie znika ani nie zmienia właściciela
> w zależności od aktywnej zakładki."*

Z **dwunastu** pisarzy `QueryStatsText` przeniesiono **cztery** — te opisujące stan w toku
(`ExecutingStatus`, `CancellingStatus`, dwa liczniki ładowania). Osiem pisarzy wyniku zostało nietkniętych.

⚠ **Jedna konsekwencja wykonana świadomie: `QueryStatsText` jest teraz CZYSZCZONE na starcie wykonania.**
Zostawiony wynik poprzedniego zapytania wisiałby jako „143 rows in 46 ms" obok paska mówiącego, że trwa
coś innego — kłamałby o bieżącej chwili. Po anulowaniu albo błędzie pole jest puste, i to jest uczciwe.

#### §19.7.2 ⚠⚠ Operacja referencyjna NIE POTRAFI pokazać procentu — pomiar czterech ścieżek

| Ścieżka | Tryb | Zna sumę? |
|---|---|---|
| **Wykonanie zapytania SQL** (referencyjna) | **tylko nieokreślony** | ⛔ `IProgress<long>` to licznik wierszy; strumieniowy odczyt nie zna sumy, dopóki nie skończy |
| Export (`ExportDialog`) | tylko nieokreślony (`IsIndeterminate="True"` na stałe) | nie |
| Batch (`BatchResultsDialog`) | oba (`PreparationIsIndeterminate` + `PreparationTotal`) | tak |
| Data Import | oba (`ProgressPercent` 0–100) | tak |

§8.4.6 żąda „postępu procentowego, trybu nieokreślonego i anulowania"; operacja wyznaczona przez D4 może
wyćwiczyć tylko drugie i trzecie.

⭐ **Ratyfikowane: budujemy KOMPLETNĄ infrastrukturę.** Użytkownik: *„Batch i Data Import są już
istniejącymi konsumentami trybu procentowego, więc nie projektujemy czegoś hipotetycznego… Nie chciałbym
budować połowy rozwiązania i wracać do przebudowy tej samej sekcji w M3b tylko dlatego, że pierwsza
operacja nie zna wartości Maximum."*

⚠ **To nie jest złamanie dyrektywy „nic na zapas" (#233)** i warto rozumieć różnicę: #233 broni przed
właściwością, dla której **nie wiadomo, kto ją wywoła**. Tutaj konsumenci istnieją, są policzeni i mają
dziś własne paski procentowe — brakuje wyłącznie podłączenia, które jest zakresem M3b z definicji D4.
⛔ Mimo to **ścieżka procentowa nie ma konsumenta NA ŻYWO**; wykonuje ją wyłącznie test. Nie zakładać,
że jest sprawdzona wizualnie.

#### §19.7.3 ⭐ Anulowanie — dwa zasięgi JEDNEJ komendy, i zamknięcie realnej luki funkcjonalnej

```csharp
ShowCancelButton => IsQueryTabActive && IsExecuting
```

**Przełączenie zakładki w trakcie długiego zapytania ODBIERAŁO możliwość anulowania** — trzeba było
wrócić na zakładkę SQL. To pierwszy przypadek w M3, gdy bramkowanie zakładką ma konsekwencję
**funkcjonalną**, a nie tylko czytelnościową.

⭐ **`StatusProgressViewModel` NIE MA własnej komendy Cancel** — przyjmuje `ICommand` właściciela.
Pasek statusu i toolbar naciskają **ten sam obiekt**, więc `CanExecute`, zatrzask `IsCancelling`
i implementacja są jedne. Użytkownik: *„To są dwa zasięgi tej samej komendy, a nie dwie różne
implementacje."* ⛔ Nie dodawać tu drugiej komendy — powstałby drugi właściciel stanu anulowania.

⚠ **Przycisk w toolbarze ZOSTAJE** (decyzja użytkownika): jest naturalny podczas pracy w edytorze
i użytkownicy są do niego przyzwyczajeni; pasek statusu **uzupełnia**, nie zastępuje.
⚠ To nie łamie §8.4.5 („chip nigdy nie jest przyciskiem") — tamten zakaz dotyczy operacji
**nieodwracalnej**, a anulowanie jest zaworem bezpieczeństwa, odwrotnością Commita.

#### §19.7.4 Jeden punkt wpięcia — `OnIsExecutingChanged`

`Progress.Begin/End` wisi tam, gdzie już wisi `ExecutionTimer`, i z tego samego powodu: `IsExecuting`
jest **jedynym** miejscem, przez które przechodzi każde wejście i wyjście z wykonania — sukces, błąd,
anulowanie i `finally`. ⭐ Dzięki temu **nie da się dodać ścieżki wyjścia, która zostawi zapalony pasek**.
⛔ Nie wołać `Begin`/`End` z gałęzi wykonania.

#### §19.7.5 ⚠⚠ POMIAR, KTÓRY URATOWAŁ „STAŁĄ SZEROKOŚĆ" — Fluent daje `ProgressBar` `MinWidth=200`

Styl niesie `MinWidth="0"` jako zabezpieczenie. **Zweryfikowane przez usunięcie go:**

```
Assert.Equal() Failure  Expected: 120   Actual: 200
```

**Avalonia przycina `Width` przez `MinWidth`**, więc bez tego deklarowana w §8.4.6 stała szerokość
120 px renderowałaby się jako **200 px** — po cichu, przy zielonym buildzie i zielonych pozostałych
testach. ⭐ To ten sam defekt, którym M2b zapłacił strzałkę drzewa metadanych (20 px urosło do 100 przez
`MinWidth` na bazowym `Button`) — **drugie wystąpienie tej samej pułapki w tym etapie**, co czyni ją
kandydatką na stałą pozycję listy kontrolnej przy każdym `Width` na kontrolce Fluenta.

⚠ Stała szerokość nie jest estetyką: pasek rosnący z treścią przesuwałby chipy stanu przy każdej
operacji, czyli §13.3 („Zero Layout Shift") rozłożony w czasie.

#### §19.7.6 Dwie rzeczy świadomie NIE zrobione

**(a) Nie powstał token na grubość paska (4 px).** Jedyny kandydat przy tej liczbie to `Space.Xs`
(odstęp) — użycie go byłoby **błędną rolą**, czyli stanem gorszym niż wartość lokalna (R12); nowa rola
z jednym konsumentem łamałaby R3. Wartość została z powodem w miejscu. Pytanie *„czy paski postępu mają
wspólną metrykę"* idzie do **§13.3** na komplecie czterech (status, Export, Batch, Data Import) — razem
z pytaniem o chipy z §19.3.4 i §18.R/K11.

**(b) Przycisk Cancel nie dostał własnych `Width`/`Height`.** `Button.icon` bierze wysokość chromy
(`Size.ControlToolbar`) i nie ma podłogi szerokości, więc jego rozmiar wyznacza ikona, a wysokość —
pasek, w którym stoi. Nadpisanie byłoby złamaniem **decyzji architektonicznej 2** z M2b („kontener
rozstrzyga wielkość, element ją przyjmuje").

⚠ Styl jest klasą `ProgressBar.status`, **nie** stylem na wszystkich `ProgressBar`: trzy pozostałe żyją
w dialogach i mają inną gęstość. Ujednolicenie to M3b/§13.3, nie doklejka tutaj (R7).

#### §19.7.7 Strażnicy

**`StatusProgressTests`** (12 przypadków, czysta logika, zero Avalonii) opisuje kontrakt modelu **teraz,
gdy konsument jest jeden** — a nie za pięć, gdy będzie już utrwalony. Pinuje w szczególności: tryb
nieokreślony jako bezpieczny domyślny, **przycinanie procentu do 0–100** (producent liczy z dwóch liczb,
z których jedna bywa oszacowana), reset trybu przy kolejnym `Begin` (inaczej zapytanie po imporcie
pokazałoby pasek stojący na wartości tamtej operacji) oraz ⚠ **zwolnienie komendy w `End()`** — to ten
sam kształt, co odpinanie subskrypcji railu w §19.4.2: pasek żyje tak długo jak okno, więc trzymana
komenda utrzymywałaby przy życiu VM zakładki zamkniętej w trakcie operacji.

**`StatusProgressBar_KeepsItsFixedSize_DespiteFluentsMinimums`** — §19.7.5, zweryfikowany usunięciem
zabezpieczenia.

#### §19.7.8 Definition of Done

| # | Warunek | |
|---|---|---|
| 1 | zakres iteracji zamknięty | ✅ sekcja + operacja referencyjna |
| 2 | pozostawione wartości lokalne mają powód w miejscu | ✅ grubość paska `4` (R3/R12) |
| 3 | baza `DesignTokenComplianceTests` = stan faktyczny | ✅ bez zmian |
| 4 | build 0/0 | ✅ |
| 5 | testy zielone w trzech partycjach | ✅ **7023 + 47 + 54 = 7124** (+13) |
| 6 | smoke + oba motywy | ✅ QA użytkownika: postęp przeżywa zmianę zakładki, Cancel działa z obu miejsc jako jedna komenda |
| 7 | wpis w §19 | ✅ ta sekcja |
| 8 | commit; push po akceptacji | ✅ / ⏸ |

#### §19.7.9 ⏸ PRZENIESIONE DO NASTĘPNEJ ITERACJI — pionowe wyrównanie `localhost:3050`

> **Użytkownik, przy odbiorze:** *„Zmieniam zdanie co do `localhost:3050`… powinien być jednak
> wyśrodkowany pionowo względem nazwy bazy i badge'a DEV MODE… obecne wyrównanie do linii bazowej, choć
> typograficznie poprawne, sprawia wrażenie lekkiego opadnięcia."* Wyraźnie oznaczone jako drobny polish,
> **bez zatrzymywania iteracji**.

⚠⚠ **To ODWRACA decyzję z §19.3.3 i ma pułapkę, której nie wolno wdepnąć drugi raz.** Naiwna realizacja —
rozbicie na dwa `TextBlocki` z `VerticalAlignment="Center"` — przywraca **dokładnie** defekt tam
zmierzony: dwa pudełka o różnych wysokościach (12 vs 11) i różnych pozycjach (6 vs 7), a
`UseLayoutRounding` przycina **każdy element osobno**, więc przy **125% — czyli na monitorze
zgłaszającego** — topy lądują po przeciwnych stronach zaokrąglenia i różnica rośnie do całego piksela.

⭐ **Rozwiązanie musi zachować JEDEN `TextBlock`.** Zweryfikowane, że API istnieje:
`Avalonia.Controls.Documents.Inline.BaselineAlignment` — pozwala wyśrodkować mniejszy `Run` **wewnątrz
wspólnego pudełka**, czyli daje efekt optyczny bez oddawania jednego zaokrąglenia na element.
⚠ Istnienie właściwości jest potwierdzone; **jej zachowanie przy tej parze rozmiarów pozostaje do
zmierzenia** przy wdrożeniu. Gdyby nie dała efektu, następnym kandydatem jest korekta w obrębie tego
samego pudełka — **nigdy** dwa osobne `TextBlocki`.

---

### §19.8 Poprawki odbiorcze po M3.1f — trzy defekty i jeden pomiar zamykający temat (2026-08-02)

> **To nie jest iteracja** — zestaw poprawek zebranych przy odbiorze M3.1f, na wyraźną instrukcję
> użytkownika dołączonych do commita następnego kroku, bez zatrzymywania planu. Wspólny mianownik
> całej trójki: **każde zgłoszenie wskazywało inną przyczynę niż rzeczywista**, a pomiar to za
> każdym razem zmienił.

#### §19.8.1 Wyrównanie `localhost:3050` — ZAMKNIĘTE POMIAREM, bez zmiany kodu

> **Użytkownik:** *„Zmieniam zdanie… powinien być jednak wyśrodkowany pionowo… obecne wyrównanie do
> linii bazowej sprawia wrażenie lekkiego opadnięcia."*

To odwracało decyzję z §19.3.3, więc zamiast wykonać, zmierzono **trzy** mechanizmy:

| | środek pudełka | odchyłka od nazwy |
|---|---|---|
| nazwa (11 px SemiBold) | 8,54 | — |
| **endpoint dziś (linia bazowa)** | 8,83 | **+0,30 px** |
| endpoint przez `InlineUIContainer` | 7,50 | **−1,04 px** |

⚠⚠ **`BaselineAlignment="Center"` na `Run` jest w Avalonii IGNOROWANE** — per-run baseline wychodzi
identyczny (8,45) z nim i bez niego. Właściwość **istnieje w API** (sprawdzone w dokumentacji pakietu),
więc pierwszy odruch był taki, że wystarczy ją dopisać; wstawienie jej byłoby **martwym kodem
udającym poprawkę**. To pułapka „deklarowana właściwość potrafi kłamać" (§9.2/9 handovera), tym razem
w wariancie *istnieje, ale nic nie robi*.

⭐ **Wniosek: obecne wyrównanie jest NAJBLIŻSZE optycznego środka ze wszystkiego, co dostępne.**
Wrażenie osiadania bierze się stąd, że nazwy połączeń bywają **wersalikami** („GAL"), a endpoint jest
minuskułą — to różnica wysokości wersalika i x-height przy wspólnej linii bazowej, a nie błąd
wyrównania pudełek, których środki dzieli 0,30 px.

> **Użytkownik, na zamknięcie:** *„nie chcę zmieniać hierarchii typograficznej tylko po to, żeby
> skorygować złudzenie optyczne. Nie zwiększaj rozmiaru endpointu do 11 px."*

⛔ **Temat zamknięty.** Cały pomiar wraz z trzema odrzuconymi mechanizmami stoi w komentarzu **przy tym
runie** — to jedyny trwały efekt rundy i jego sens: następna osoba, która zobaczy „opadający
localhost", zobaczy też liczby i nie przejdzie tej drogi trzeci raz.

#### §19.8.2 🐞 Historia parametrów nie przywracała wartości — przyczyną C3, nie kontrolki

> **Użytkownik:** *„po wybraniu pozycji z historii nie odtwarzają się wartości parametrów…
> Podejrzewam, że to efekt wymiany kontrolek podczas ostatnich zmian."*

**Zmierzone: podejrzenie nietrafione.** Przyczyną jest **C3 z etapu debuggera** (2026-07-25), które
wprowadziło `ParameterValue.TypeText` i dowód zgodności typu:

```csharp
// „A value with no recorded type (legacy history) is never provable."
private bool IsProvablyCompatible(ParameterValue value)
    => value.TypeText is { Length: > 0 } stored && ClassifyKind(stored) == Kind;
```

Wpis z historii użytkownika pochodził z **2026-07-20**, czyli sprzed C3 — nie ma zapisanego typu, więc
`ApplyHistoryValue` zwraca `false`, a `OnSelectedHistoryChanged` **ignoruje ten `bool`**. Odmowa jest
całkowicie cicha: etykieta pokazuje wartości, kontrolki zostają na NULL.

⭐⭐ **Sedno defektu: reguła C3 była projektowana dla zastosowania AUTOMATYCZNEGO** (CLAUDE.md mówi
wprost *„not auto-applied"*), **ale konstruktor zasiewa `SelectedHistory = History[0]`, więc auto-apply
i ręczny wybór to JEDNA ścieżka kodu.** Dowód obejmował więc także jawną decyzję użytkownika, który
właśnie wskazał ten wpis i widzi jego wartości.

**Naprawa: rozdzielenie tych dwóch przypadków znacznikiem `_seedingHistory`.**
Zasiew z konstruktora → dowód typu obowiązuje (ratyfikowana reguła C3 **nietknięta**).
Jawny wybór → dowód nie obowiązuje; zabezpieczeniem zostaje **parsowanie**.
⚠ `LaunchValueCarryOver` (panel startowy debuggera, właściwa powierzchnia C3) używa domyślnego
`requireProvenType: true`, więc jest bez zmian — sprawdzone, że to jedyni pozostali wywołujący.

⭐ Efekt uboczny: naprawia **każdy** wpis sprzed 2026-07-25. Notatka *„self-heals after one run"* była
prawdziwa tylko dla auto-apply — ręczny wybór nie miał jak się uleczyć.

#### §19.8.3 🐞 Kolumna Type pusta przy domenie — KOLEJNOŚĆ, i defekt nie był tam, gdzie go widziano

> **Użytkownik:** *„zmienne oparte na domenie mają poprawnie wybraną domenę, ale kolumna Type jest
> pusta… Parameters działają poprawnie, Fields tabel również… problem jest lokalny dla Variables."*

**Zmierzone: obie lokalizacje nietrafione.**

**(a) Nie jest lokalny dla Variables.** `ProcedureFieldRowBase` obsługuje **7 siatek** — parametry
wejściowe i wyjściowe procedury, argumenty i Result funkcji oraz zmienne procedury, funkcji i triggera.
W zgłoszonej procedurze parametry po prostu nie były oparte na domenie. Test
`Parameter_OnADomain_GetsTheSameTreatment` **upadał przed naprawą** i to jest dowód zasięgu.

**(b) Nie jest brakiem funkcji.** `SyncTypeDisplayFromDomain` istniał i był wołany z **dwóch** miejsc.
⭐⭐ **Przyczyną jest KOLEJNOŚĆ:** `OnDomainNameChanged` wychodzi na `_suppressCompose`, które `LoadType`
właśnie trzyma, gdy ustawia `DomainName`; a subskrypcja `AvailableDomains.CollectionChanged` ratowała
sytuację **wyłącznie wtedy, gdy lista domen dojeżdżała PO zbudowaniu wierszy**. Przy połączeniu,
w którym domeny były już wczytane, kolekcja się nie zmieniała i nie odpalało się **nic**. Stąd
zależność od kolejności, wyglądająca jak defekt jednej zakładki.

**Naprawa: jedna linia w `LoadType`** — wywołanie istniejącego sync po ustawieniu `DomainName`.

⚠⚠ **Reguła #11 była realnie zagrożona i to jest najważniejsza część tej poprawki.**
`SyncTypeDisplayFromDomain` ustawia `BaseType`, ale `ComposeType` zwraca **nazwę domeny** (domena
wygrywa), więc `TypeText` — źródło DDL — pozostaje `T_ID`. Gdyby to się rozjechało, kompilacja
podmieniłaby domenę na jej rozwinięcie i **cicho zerwała powiązanie** zmiennej z domeną. Pinowane
osobnym testem (`…KeepsTheDomainAsTheCanonicalType`).

⚠ **Druga pułapka: `NOT NULL`.** Sync przejmował też `NotNull` domeny, a `From()` ustawia go **przed**
`LoadType`, z samej deklaracji. Bez rozdzielenia otwarcie edytora zmieniałoby zapisany kod użytkownika.
Stąd parametr `adoptNotNull`: **wybór domeny ręką** → przejmuje komplet atrybutów; **wczytanie
deklaracji** → nie rusza `NOT NULL`. ⭐ Poprawiło to przy okazji ścieżkę „domeny dojechały późno", która
miała ten sam problem, tylko rzadziej widoczny.

#### §19.8.4 Wygląd komórek zależnych od typu — ujawniony dług, nie regresja

Siatki pól trzymają w komórkach **zawsze widoczny `TextBox`**, bo `DataGridTextColumn` umie tylko
`IsReadOnly` per KOLUMNA, a bramka jest per WIERSZ (gotcha #83/#124). Gdy typ narzuca domena albo
`TYPE OF`, kolumny Size/Scale/SubType/Charset są **wyłączane** — zamiar zapisany w `FieldGridColumns`
od początku.

⚠ **To zachowanie jest stare; nowa jest tylko jego widoczność.** Przed M2b nieaktywny `TextBox` zlewał
się z tłem; nadanie kontrolce spójnej ramki sprawiło, że zaczął czytać się jak pole edycyjne —
użytkownik zgłosił to jako *„puste prostokąty"*. Klasyczny **ujawniony dług** (pułapka §9.1/2).

Dwa `Style`:
* `DataGridCell TextBox:disabled` → tło i ramka `Transparent`, tekst `SubtleForegroundBrush`.
  ⚠ Zmierzone: `FluentBridge` mapuje `TextControlBackgroundDisabled` na `BackgroundColor`, czyli tło
  maluje **wnętrze szablonu**, a nie setter — więc sam setter `Background` tego nie zdejmuje (reguła 8
  §16: kolory wnętrza szablonu idą przez Bridge). Zapisane, **nie rozwiązane w tej poprawce**.
* `DataGridCell TextBox` → `VerticalAlignment="Stretch"` + `VerticalContentAlignment="Center"`.
  > **Użytkownik:** *„wyglądają trochę jak wąskie »dyski«. Nie wykorzystują wysokości wiersza."*

  ⭐ `Stretch`, a **nie** `Height`: element PRZYJMUJE wysokość od komórki (decyzja architektoniczna 2
  z M2b) i — co ważniejsze — `Stretch` **nie zwiększa `DesiredSize`**, więc nie może podnieść wiersza.
  `MinHeight` w kroku 7 M2b urosło wiersz o 2 px; to jest ten sam błąd o jeden krok wcześniej.

#### §19.8.5 ⏸ Dług architektoniczny do §13.3 — DWIE równoległe implementacje wiersza pola

Analiza §19.8.3 odsłoniła, że **`FieldRowViewModel` (pola tabel) i `ProcedureFieldRowBase`
(procedury / funkcje / triggery) to dwie niezależne realizacje tego samego pomysłu** — osobne klasy,
osobne budowanie kolumn, osobna obsługa asynchronicznego ładowania domen. Objaw ze zgłoszenia dotyczył
tylko drugiej z nich, i tylko dlatego „Fields tabel działały".

> **Użytkownik:** *„nie rozwijaj teraz architektury dwóch ViewModeli… odnotuj to jako dług
> architektoniczny do §13.3. Nie rozszerzaj zakresu M3.2a tylko dlatego, że wyszło przy analizie."*

⛔ Świadomie nie ruszane. → wejście do przeglądu **§13.3**.

#### §19.8.6 ⭐ Co te trzy defekty mówią razem

Wszystkie trzy zgłoszenia wskazywały **inną przyczynę niż rzeczywista**, i za każdym razem w tę samą
stronę: *„to od ostatnich zmian"* / *„to lokalne dla tego ekranu"*. Rzeczywiste przyczyny były
**starsze i szersze** — reguła z innego etapu (C3), zależność od kolejności ładowania i zamiar sprzed
lat, który dopiero teraz stał się widoczny.

⭐ **Reguła praktyczna na resztę etapu: obserwacja użytkownika o OBJAWIE jest wiarygodna; jego wniosek
o PRZYCZYNIE i ZASIĘGU trzeba zmierzyć.** Dwa razy w tej rundzie pomiar rozszerzył naprawę
(7 siatek zamiast jednej, cała historia zamiast jednego wpisu), a raz — zamknął temat bez zmiany kodu.

---

### §19.9 Wysokość edytora w siatce definicji pól — TRZY WARSTWY, każda maskowała następną (2026-08-02)

> **Użytkownik:** *„`TextBox` w DataGrid nadal ma zbyt małą wysokość… W porównaniu z `ComboBoxem`
> w tej samej siatce od razu widać, że nie należą do tej samej rodziny kontrolek… znajdź właściwą
> przyczynę zamiast maskować to kolejną liczbą."*

Instrukcja była trafna co do metody: **dwie pierwsze „naprawy" nic nie dały i dowiedziałbym się o tym
dopiero od użytkownika**, gdyby nie pomiar po każdej z nich. Zmierzone w sondzie budującej prawdziwą
siatkę przez `FieldGridColumns`:

| krok | `VerticalAlignment` | wysokość `TextBox` | komórka |
|---|---|---|---|
| stan zastany | `Center` | **12,00** | 30,00 |
| po usunięciu wartości lokalnych | `Stretch` | **12,00** | 30,00 |
| po `MinHeight` = `Size.Control` | `Stretch` | **24,00** | 30,00 |

#### §19.9.1 ⚠⚠ Warstwa 1 — WARTOŚĆ LOKALNA BIJE SETTER STYLU

`FieldGridColumns.TextEditCol` ustawiał **w kodzie**, na instancji: `VerticalAlignment`,
`VerticalContentAlignment`, `Padding`, `BorderThickness`, `Background`. Wszystkie moje settery
w `DataGridCell TextBox` — łącznie z dodanym wcześniej `Stretch` — były przez nie **przykryte**.

⭐ To ten sam mechanizm, przez który `MessageBanner` dorobił się sześciu wariantów chromy per host,
i ta sama reguła, którą projekt zapisał wtedy: **host ustawia wiązania i zachowanie, styl ustawia
chromę.** Chroma przeniesiona do stylu; w budowniczym kolumny została goła konstrukcja + dwa `Bind`.

#### §19.9.2 ⚠⚠ Warstwa 2 — `Stretch` NIE WYSTARCZA, bo centruje KOMÓRKA

Po warstwie 1 `VA` raportowało już `Stretch`, a wysokość **nadal 12 px przy komórce 30 px**.
Przyczyna leży o poziom wyżej: `DataGridCell` ma `VerticalContentAlignment="Center"`.

⛔ **Tego settera nie wolno odwrócić** — istnieje po to, żeby zwykły TEKST nie osiadał przy górnej
krawędzi komórki (§8.4), i jego komentarz mówi to wprost. Odwrócenie naprawiłoby jedną kontrolkę
i zepsuło wszystkie komórki tekstowe w aplikacji.

#### §19.9.3 ⭐ Warstwa 3 — element ma PROSIĆ o wysokość, tak jak robi to `ComboBox`

`ComboBox` wygląda w tej komórce poprawnie **wyłącznie dzięki `Size.Control` ze swojego stylu** —
jest centrowany dokładnie tak samo, tylko prosi o 24 px. Edytor dostał więc **tę samą rolę**, a nie
dobraną liczbę: `MinHeight` = `Size.Control`.

⚠⚠ **Ale jako KLASA `field-editor`, nie jako styl na wszystkich `DataGridCell TextBox`** — i to jest
warunek bezpieczeństwa, nie estetyka. Siatki definicji pól mają w wierszu `ComboBox`, więc już dziś
mierzą 30 px i minimum 24 niczego nie podniesie (zmierzone). **Siatki DANYCH — Table Data, wyniki
zapytań — `ComboBoxa` nie mają**; tam ten sam setter urósłby każdy wiersz, czyli odtworzyłby dokładnie
regresję z kroku 7 M2b, gdzie edytor prosił o 18 px przy 16 px dostępnych.

#### §19.9.4 Strażnik

`FieldGridEditors_TextBoxAndComboBox_ShareOneHeight` porównuje `TextBox` z `ComboBoxem`
**obok, w tej samej siatce** — nie z liczbą. Przetrwa więc zmianę wartości `Size.Control` i upadnie
dokładnie wtedy, gdy jedna z kontrolek przestanie należeć do rodziny. Druga asercja pilnuje, że
minimum edytora **nie podniosło wiersza**.

#### §19.9.5 ⭐ Wniosek metodologiczny

Trzy warstwy, z których **każda wyglądała na przyczynę i każda maskowała następną**. Gdyby po
warstwie 1 poprzestać na „usunąłem wartości lokalne, teraz styl działa", raport brzmiałby *naprawione*,
a na ekranie nic by się nie zmieniło. ⭐ **Reguła: po każdej warstwie mierz PONOWNIE ten sam parametr,
który był przedmiotem zgłoszenia** — zniknięcie przyczyny nie jest dowodem zniknięcia objawu.

---

### §19.10 Iteracja 7 (M3.2a) — H‑3, stabilny układ paska tytułu i toolbara dokumentu (2026-08-02)

**Zakres:** H‑3 w całości — obie powierzchnie. Wariant **B** (toolbar) + **T2** (pasek tytułu), oba
wybrane przez użytkownika po przedstawieniu pomiaru i trzech wariantów dla każdej powierzchni.

> ⛔⛔ **CZYTAJ RAZEM Z §19.11. Dwa z czterech ruchów tej iteracji zostały WYCOFANE przez użytkownika
> po obejrzeniu w działającej aplikacji** — wspólna podłoga Execute/Cancel (B2) i dokowanie
> Commit/Rollback do prawej (B3). ⭐ Zostały **T2** i **B1**. Pomiary poniżej są nadal aktualne jako
> **opis stanu**; nie są opisem tego, co zostało wdrożone. §19.11 mówi, co i dlaczego odpadło —
> i niesie ważniejszą lekcję niż sama iteracja.

#### §19.10.1 Pomiar — audyt miał rację co do faktu, ale nie co do skali

Metryki wyliczone z katalogu: przycisk ikonowy = `Pad.ButtonIcon` 6+6 + `Size.Icon.Lg` 16 = **28 px**,
`Spacing="6"` → skok **34 px**; separator (`Width` 1 + `Margin` 4,4) = **9 px**, ze skokiem **15 px**.

⭐ **Pasek tytułu miał już tylko JEDNĄ bramkę** — `CanExportDdl` (separator + przycisk). Dwie z trzech
przyczyn wymienionych w §3.6 handovera zabrało M3.1b. Pokazanie tej pary wstawiało **43 px**
i przesuwało wszystkie **dziewięć kreatorów *Nowy X*** — najczęściej klikaną grupę tego paska.

⭐⭐ **Fakt, którego audyt nie nazwał: `CanExportDdl` czyta `SelectedWorkspaceTab`.** To jedyna bramka
**dokumentowa** w pasku, który po decyzji M3.1b odpowiada za nawigację i polecenia **połączenia**.
Przyczyną niestabilności było więc pomieszanie zasięgów, a nie sama bramka.

⭐⭐ **Toolbar dokumentu — model pięciu sekcji JUŻ ISTNIAŁ i to jest najważniejsze ustalenie iteracji.**
`MainWindowViewModel:630–655` deklaruje `[ Mode ] | [ Main ] | [ Collection ] | [ Helper ] | [ Close ]`
ze świadomym zwijaniem separatorów. Czyli **kolejność była zagwarantowana; niezagwarantowana była
POZYCJA** — wszystkie sekcje leżą w jednym poziomym `StackPanelu`, więc szerokość każdej to suma jej
widocznych dzieci. H‑3 nie było brakiem architektury, tylko brakiem geometrii.

**Policzone dla dwóch sąsiadujących rodzajów** (bez aktywnej kolekcji, offset od pierwszego dziecka):

| x | Procedure | Trigger |
|---|---|---|
| 144–172 | Rollback | **Debug** |
| 212–240 | **Execute** | **Comment** |
| 246–274 | **Debug** | **Uncomment** |

Dwie przyczyny się sumowały: Trigger nie ma Commit/Rollback (−68 px — `ShowTransactionButtons` pomija
go celowo, bo Compile triggera auto-commituje) i nie ma Execute (−34 px w sekcji 4).

⚠⚠ **Drugi rodzaj drgania — W OBRĘBIE JEDNEJ ZAKŁADKI — i w odbiorze gorszy.** Zmierzone headless:
**Execute 156 px, Cancel 118 px**. Oba wykluczają się wzajemnie, więc **naciśnięcie F5 przesuwało
o 38 px** sekcje 3 i 4 oraz przycisk zamknięcia i oddawało je po zakończeniu — czyli układ drgał
dokładnie wtedy, gdy użytkownik patrzy na pasek.

#### §19.10.2 Co zostało zrobione

| # | Ruch | Efekt | Los |
|---|---|---|---|
| **T2** | Export DDL + jego separator na **koniec** kolumny 0, za kreatory | pasek tytułu nie ma już **ani jednej** bramki, która cokolwiek przesuwa | ✅ **zostaje** |
| **B1** | sekcja 1 rezerwuje slot **43 px** zawsze, **separator w środku rezerwacji** | akcja główna dokumentu startuje pod tym samym x we wszystkich 12 rodzajach zakładek | ✅ **zostaje** |
| **B2** | Execute i Cancel dostają wspólną podłogę `MinWidth="156"` | koniec przeskoku przy F5 | ⛔ **wycofane** — §19.11 |
| **B3** | Commit / Rollback dokują do **prawej** krawędzi paska | znika największa różnica między rodzajami (68 px) | ⛔ **wycofane** — §19.11 |

⭐ **B1 — separator jest WEWNĄTRZ rezerwacji i to jest istota rozwiązania.** Na zewnątrz
`ToolbarSep1Visible` zabierałby swoje 15 px przy pustej sekcji i kotwica przestałaby trzymać. Kreska
świecąca zawsze byłaby z kolei „kreską po nieistniejącym elemencie" — tym, co sprzątał sprint
brandingowy. Wewnątrz dostajemy oba naraz: **stałą szerokość ORAZ kreskę tylko wtedy, gdy jest co
oddzielać.** ⚠ `MinWidth`, nie `Width` — gdyby dwie bramki stały się prawdziwe naraz, drugi przycisk
ma urosnąć kontener, a nie zostać po cichu przycięty.

⛔ **B3 — WYCOFANE (§19.11).** Argument brzmiał: to nie ruch geometryczny, tylko podział **zasięgu** —
Commit/Rollback były jedyną parą w sekcji 2 mówiącą o **transakcji**, a nie o edytowanym obiekcie, czyli
ta sama linia, którą M3.1d poprowadziło w pasku statusu, tylko po stronie **poleceń** zamiast **faktu**.
⭐ Argument był poprawny **i przegrał z ergonomią**: para tworzy z Execute jedną grupę, której użytkownik
szuka razem. Zapis zachowany, bo pokazuje, jak dobrze uzasadniony ruch może być złym ruchem.

⚠ Ustalenie techniczne z B3, warte zachowania na przyszłość, gdyby cokolwiek miało tam dokować:
`DockPanel` przydziela krawędź **w kolejności deklaracji**, więc pierwsze `Dock="Right"` jest najbardziej
wysunięte i nic nim później nie rusza. Para musiała stać **przed** licznikiem czasu — odwrotna kolejność
dałaby dokładnie ten defekt, który naprawiała: pojawienie się licznika przesuwałoby przyciski w trakcie
wykonania zapytania.

#### §19.10.3 ⛔ Czego iteracja świadomie NIE zrobiła — i dlaczego to nie jest niedoróbka

**Pikselowa tożsamość MIĘDZY rodzajami zakładek nie była celem.** Decyzja użytkownika (2026-08-02):
*„Procedure, Trigger czy Table mają inną semantykę i mogą mieć inny zestaw narzędzi. Ważne jest, żeby
toolbar był stabilny i przewidywalny w obrębie danego typu dokumentu, a nie żeby wszystkie dokumenty
wyglądały identycznie kosztem sztucznych pustych miejsc."*

Wariant pełnych kotwic **został zmierzony, nie odrzucony z góry**: rezerwacja najgorszego przypadku
każdej sekcji to ~**617 px** stałej rezerwy, czyli ~**500 px dziur** na zakładce generatora (3 przyciski).
Pod R8 pasek z dziurami wygląda gorzej niż pasek, który się przesuwa — a żadne komercyjne IDE nie
gwarantuje tu tożsamości pikselowej.

⏸ **Nie ruszona sekcja 3, i to jest jedyny znany dług tej iteracji.** `ShowCollectionEdit`
i `ShowCollectionReorder` zmieniają się przy przełączaniu **pod-zakładek**, czyli **w obrębie jednego
dokumentu** — a więc mieszczą się w kryterium użytkownika. Zmierzone: Edit to 34 px, blok reorder
(separator wewnętrzny + dwa przyciski) to 73 px. Rezerwacja całej sekcji 3 kosztowałaby ~181 px dziury
na zakładkach bez kolekcji, więc rozwiązanie nie jest oczywiste i **nie zostało podjęte po cichu**
(R7 — najpierw reguła, nie łatka na jeden ekran). ⭐ **ROZSTRZYGNIĘTE tego samego dnia przez R13
(§19.12): nie rezerwujemy miejsca na element, którego w danym kontekście nie będzie — więc sekcja 3
zostaje jak jest i nie wymaga już decyzji.**

#### §19.10.4 ⚠⚠ K12 — rola pasowała funkcją i była zakazana wprost (wpis WYCOFANY wraz z B2, §19.11)

`Size.ActionMinWidth` = 100 jest dokładnie tą rolą co do funkcji: *„podłoga, żeby para przycisków się
wyrównała"*. **Nie została użyta, z dwóch niezależnych powodów.** (a) Jej własny komentarz
w `Tokens.axaml` wyklucza chromę wprost: ⛔ *„Chroma, przycisk ikonowy i komórka siatki jej NIE biorą"*.
(b) 100 leży **poniżej** naturalnych 156 px Execute, więc niczego by nie zrównała.

⭐ Punkt (b) jest tą samą pułapką, którą tamten komentarz opisuje **na własnym przykładzie** (80 przy
„Cancel" o naturalnych 98): *podłoga działa wyłącznie wtedy, gdy leży POWYŻEJ naturalnej szerokości
etykiet, które ma zrównać*. Trafiliśmy w nią drugi raz, w innym miejscu, dwa etapy później — co jest
argumentem, żeby ten zapis w `Tokens.axaml` traktować jako regułę, a nie anegdotę.

**Wpis do rejestru §18.R: K12** — `Size.ActionMinWidth` vs podłoga pary Execute/Cancel (100 vs 156,
plus jawne wykluczenie chromy). Rozstrzyga przegląd §13.3.

#### §19.10.5 Strażnik — trzy piny, `ToolbarStabilityTests`

Nowa klasa headless (dołącza do `HeadlessCollection`, **dopisana do filtra partycji**; kryterium
spełnione — konstruuje kontrolki Avalonii).

⛔ **Dwa z trzech pinów zostały usunięte razem z B2** (`ExecuteAndCancel_RenderToTheSameWidth_…`
i `ExecuteCancelFloor_CoversBothVariants`) — pin bez mechanizmu, który opisuje, jest gorszy niż brak
pinu: czyta się jak żywa gwarancja. Zostaje jeden:

1. `ModeSectionSlot_ReservesItsWidth_EvenWhenEveryToggleIsHidden` — ⚠ istnieje, bo *„`MinWidth` jest
   w API"* nie znaczy *„`MinWidth` rezerwuje miejsce na kontenerze bez widocznych dzieci"*
   (pułapka 10). Bez działającej rezerwacji kotwica byłaby **martwym zapisem przy zielonym buildzie**.

⚠⚠ **Pułapka 12 uderzyła w trzeci pin przy pierwszym uruchomieniu i warto to zapisać.** Slot wstawiony
wprost do okna zmierzył **1024 px** — szerokość okna — bo `StackPanel` rozciąga się w pionowym
kontenerze. Test mierzył **rozciąganie zamiast rezerwacji**. Naprawione przez odtworzenie realnego
kontekstu (slot w **poziomym** `StackPanelu`, jak w produkcie). ⭐ Ta sama lekcja co w §19.2: *asercję
robi się przeciw mechanizmowi produktu, nie przeciw zamiarowi* — a różnicę widać było tylko dlatego, że
liczba wyszła absurdalna. **Gdyby wyszła prawdopodobna, pin byłby fałszywie zielony.**

#### §19.10.6 Wartości lokalne pozostawione, każda z powodem

| Wartość | Gdzie | Powód |
|---|---|---|
| `MinWidth="43"` | kontener sekcji 1 | wyliczona z ról (28 + 6 + 9); nie ma roli „szerokość przycisku ikonowego", a tworzenie jej dla jednego konsumenta łamałoby R3 |
| ~~`MinWidth="156"`~~ | ~~Execute + Cancel~~ | ⛔ **wycofana** wraz z B2 (§19.11) |

⭐ **Usunięta** przy okazji jedna wartość lokalna bez uzasadnienia: `Padding="10,4"` na przycisku Cancel
(biła rolę `Pad.Button` — dokładnie kształt, który sprzątało M2c). Wysokości nie zmienia: daje ją
`Border.chrome Button` przez `Size.ControlToolbar`. ⚠ To usunięcie **przetrwało odbiór** — jest
porządkowaniem wartości lokalnej, a nie wymuszaniem geometrii, więc nie należy do tego, co §19.11 cofa.

#### §19.10.7 Wynik iteracji przed odbiorem

Build 0/0 · **7136** zielony (**7031 + 51 + 54**, +3) · smoke czysty. ⛔ **Odbiór wizualny cofnął B2
i B3 — stan końcowy w §19.11.**

---

### §19.11 ⭐⭐ Odbiór M3.2a — GRUPA SEMANTYCZNA BIJE STABILNOŚĆ POZYCJI (2026-08-02)

Użytkownik obejrzał M3.2a w działającej aplikacji i **wycofał dwa z czterech ruchów**. To nie jest
poprawka defektu — obie zmiany działały dokładnie tak, jak zaprojektowano. Jest to **rozstrzygnięcie
o hierarchii wartości**, i dlatego dostaje własną sekcję zamiast wiersza w tabeli.

> **Użytkownik:** *„Wolę, żeby toolbar delikatnie się przesuwał, niż żeby rozbijać naturalne grupy
> akcji. Commit, Rollback i Execute tworzą jedną logiczną grupę i użytkownik intuicyjnie ich tam szuka.
> Po przeniesieniu Commit/Rollback na prawą stronę sam przez chwilę nie mogłem ich znaleźć. […] Execute
> nie powinien mieć sztucznie wymuszonej dużej szerokości. Wygląda ciężko i dominuje toolbar. […]
> Stabilność jest wartością, ale nie ważniejszą od ergonomii."*

#### §19.11.1 Co zostało cofnięte i dlaczego argument „za" był poprawny, a mimo to przegrał

**B3 — dokowanie Commit/Rollback do prawej.** Argument był mocny i nie geometryczny: para jako jedyna
w sekcji 2 mówi o **transakcji**, a nie o edytowanym obiekcie, więc przeniesienie jej odtwarzało tę samą
linię podziału, którą M3.1d poprowadziło w pasku statusu między globalnym chipem a lokalnym licznikiem.
Przy okazji znikała największa różnica między rodzajami zakładek — 68 px.

⭐ **Czego ten argument nie widział: użytkownik nie szuka poleceń według ZASIĘGU, tylko według
SĄSIEDZTWA Z AKCJĄ, którą właśnie wykonał.** Wykonanie zapytania i jego zatwierdzenie to jedna
czynność w dwóch krokach; rozdzielenie ich przez całą szerokość paska kosztuje przy **każdym** użyciu,
a oszczędza 68 px przy zmianie rodzaju zakładki. ⚠ Diagnostyczne jest to, że **autor zmiany sam nie
znalazł przycisków** — nie da się tego wychwycić inaczej niż użyciem.

**B2 — wspólna podłoga szerokości Execute/Cancel.** Mechanizm działał: 38 px drgania przy F5 znikło.
⭐ **Koszt był po stronie, której pomiar nie obejmował: podłoga rozciągała przycisk ponad jego treść,
więc akcja główna rosła bez powodu semantycznego.** To jest **R5 od drugiej strony** — *„kolor może
określać priorytet akcji, ROZMIAR NIE"*. Reguła była pisana przeciw *nadawaniu* rozmiaru dla podkreślenia
ważności; tutaj rozmiar wziął się z **wyrównania**, ale czytał się identycznie: jak deklaracja ważności,
której nikt nie zamierzał złożyć. ⚠ To rozszerza R5 o wniosek, którego w niej nie było: **nieważne,
skąd rozmiar pochodzi — liczy się, co komunikuje.**

#### §19.11.2 ⭐⭐ Lekcja — i jest to trzecie wystąpienie tego samego kształtu w M3

**GRUPA SEMANTYCZNA BIJE STABILNOŚĆ POZYCJI.** Przesunięcie paska jest kosztem akceptowalnym;
rozdzielenie akcji, których użytkownik szuka razem, nie jest.

⚠⚠ To jest ta sama figura, co pułapka 7 (M3.1e — *„spójność zestawu bije optimum pojedynczego
elementu"*), tylko o poziom wyżej: tam przegrał wariant ikony wygrywający w **każdej** mierzonej liczbie,
tu przegrały dwie zmiany wygrywające w jedynej mierzonej wielkości, jaką miało H‑3 — w pikselach
przesunięcia. ⭐ **Wspólny mianownik: mierzyłem to, co dało się zmierzyć, i traktowałem wynik jak
uzasadnienie, choć R8 mówi wprost, że pomiar jest narzędziem, a nie argumentem końcowym.**

⭐ Praktyczna reguła, którą z tego biorę na resztę etapu: **zanim „ustabilizujesz" układ przesuwając
element, sprawdź, z czym on tworzy grupę w oczach użytkownika — i policz koszt po TAMTEJ stronie.**
Odpowiedź „przesuwa się o N px" jest pełna dopiero razem z odpowiedzią „a czego użytkownik będzie
tam szukał".

⚠ Drugie dno, metodologiczne: **propozycja przed implementacją zadziałała tylko w połowie.** Warianty
B i T2 zostały wybrane na podstawie pomiaru i opisu — i to był właściwy tryb — ale **B2 i B3 dało się
ocenić dopiero na ekranie**. Dla zmian, które przestawiają elementy w polu widzenia, opis nie zastępuje
obejrzenia; krok 5 procedury (*„uruchom aplikację i obejrzyj"*) jest tu **bramką odbioru, nie
formalnością na koniec**.

#### §19.11.3 Stan po odbiorze

| Ruch | Stan |
|---|---|
| **T2** — Export DDL na koniec paska tytułu | ✅ zostaje — pasek tytułu bez żadnej bramki przesuwającej |
| **B1** — slot sekcji 1 rezerwowany (43 px) | ✅ zostaje — akcja główna pod tym samym x we wszystkich 12 rodzajach |
| **B2** — wspólna podłoga Execute/Cancel | ⛔ wycofane; drganie 38 px przy F5 **świadomie zaakceptowane** |
| **B3** — Commit/Rollback do prawej | ⛔ wycofane; różnica 68 px między rodzajami **świadomie zaakceptowana** |
| `Padding="10,4"` na Cancel | ✅ usunięcie zostaje — porządkowanie wartości lokalnej, nie geometria |
| **K12** w §18.R | ⛔ wycofane wraz z B2 — kolizja bez wartości lokalnej nie ma przedmiotu |
| dwa piny podłogi w `ToolbarStabilityTests` | ⛔ usunięte wraz z mechanizmem — pin bez mechanizmu czyta się jak żywa gwarancja |

⛔ **Oba komentarze w `MainWindow.axaml` mówią wprost, że przesuwanie jest zaakceptowanym kompromisem,
a nie długiem** — bo *„toolbar się przesuwa"* jest bardzo wiarygodnym zgłoszeniem i bez tego zapisu
wróciłoby jako „defekt do naprawienia" razem z rozwiązaniem, które właśnie odrzucono. To pułapka 13
(*„przeniesienie faktu zostawia po sobie regresję, która nią nie jest"*) w wariancie odwrotnym:
**tu śladu nie zostawia zmiana, tylko jej wycofanie.**

Build 0/0 · **7134** zielony w trzech partycjach (**7031 + 49 + 54**) · smoke czysty.
⚠ **To nie był jeszcze stan końcowy** — drugi odbiór cofnął także B1, patrz §19.12.

---

### §19.12 ⭐⭐ Drugi odbiór M3.2a — R13: nie rezerwujemy miejsca na element, którego nie będzie (2026-08-02)

Po cofnięciu B2 i B3 użytkownik obejrzał pasek ponownie i **wycofał także B1** — ostatni ruch po stronie
toolbara. Powód zobaczył w **SQL Editorze**, gdzie żadna z pięciu bramek sekcji 1 nie jest prawdziwa,
więc rezerwacja 43 px zostawiała pustą przestrzeń przy lewej krawędzi.

> **Użytkownik:** *„Nie chcę kotwicy sekcji 1 za wszelką cenę. Jeżeli w danym rodzaju dokumentu nie ma
> żadnej akcji w tej sekcji, to nie zostawiaj pustej przestrzeni. To wygląda gorzej niż niewielkie
> przesunięcie toolbara. […] Stabilizacja ma sens tylko wtedy, gdy nie pogarsza wykorzystania
> przestrzeni. Pusta dziura w SQL Editorze nie wnosi żadnej wartości i sprawia wrażenie błędu układu."*

#### §19.12.1 ⭐⭐ R13 — reguła ratyfikowana, obowiązuje w całej aplikacji

> **R13: NIE REZERWUJEMY MIEJSCA NA ELEMENT, KTÓRY W DANYM KONTEKŚCIE NIGDY SIĘ NIE POJAWI.**
> Stabilizacja układu ma sens **tylko wtedy, gdy nie pogarsza wykorzystania przestrzeni.**

⭐ Reguła jest **mocniejsza niż przypadek, z którego wyrosła**, i to jest jej wartość: rozstrzyga
z góry każdą przyszłą pokusę „zarezerwujmy slot, żeby nic nie skakało" — w pasku zakładek (M3.3),
w Metadata Explorerze (M3.4), w sekcji postępu (M3b). ⚠ Zamyka też przy okazji wariant A z §19.10.3
w sposób ostateczny: **~500 px dziur to R13 pomnożone przez pięć**, a nie tylko „gorzej pod R8".

⚠ Warto zauważyć asymetrię, którą R13 wprowadza świadomie: **pustej przestrzeni użytkownik nie czyta
jako „tu czasem coś jest", tylko jako błąd** — bo w spoczynku nic jej nie tłumaczy. Przesunięcie ma
odwrotną własność: jest widoczne **tylko w momencie zmiany**, a w spoczynku układ wygląda poprawnie.
⭐ To jest cała różnica kosztów: **dziura kosztuje przez cały czas, przesunięcie kosztuje przez chwilę.**

#### §19.12.2 Bilans M3.2a — trzy ruchy z czterech wycofane, i to jest wynik, nie porażka

| Ruch | Los |
|---|---|
| **T2** — Export DDL na koniec paska tytułu | ✅ **jedyny, który został** — pasek tytułu bez żadnej bramki przesuwającej |
| **B1** — slot sekcji 1 (43 px) | ⛔ wycofane — **R13** |
| **B2** — wspólna podłoga Execute/Cancel | ⛔ wycofane — R5 od drugiej strony |
| **B3** — Commit/Rollback do prawej | ⛔ wycofane — grupa semantyczna bije stabilność pozycji |
| `Padding="10,4"` na Cancel | ✅ usunięcie zostaje — porządkowanie wartości lokalnej |
| `ToolbarStabilityTests` | ⛔ **klasa usunięta w całości** — jej jedyny pozostały pin dotyczył B1 |
| **K12** w §18.R | ⛔ wycofane wraz z B2 |

⭐ **M3.2a kończy się bez ani jednego nowego testu i z jedną przeniesioną kontrolką** — a mimo to jest
najbardziej produktywną iteracją M3 pod względem **reguł**: dała R13, rozszerzyła R5 o wniosek
„nieważne, skąd rozmiar pochodzi", i dołożyła dwie pułapki (14 i 15). ⚠ To jest R8 w praktyce:
*kryterium odbioru brzmi „czy wygląda to jak dopracowana aplikacja komercyjna?"*, a nie „ile rzeczy
udało się zmienić".

#### §19.12.3 ⚠⚠ Wniosek metodologiczny — trzy odrzucenia z rzędu mają jeden wspólny mianownik

Wszystkie trzy ruchy **działały**, każdy usuwał zmierzone drganie, żaden nie miał defektu. Odrzucone
zostały, bo każdy płacił za stabilność **inną walutą niż piksele**: rozmiarem akcji głównej (B2),
sąsiedztwem poleceń (B3), gęstością układu (B1).

⭐ **H‑3 było postawione jako problem geometryczny i dlatego wszystkie moje odpowiedzi były
geometryczne.** Pomiar pokazał, o ile pasek się przesuwa, więc każde rozwiązanie zmniejszało tę liczbę
— i każde robiło to kosztem czegoś, czego audyt nie zmierzył, bo nie umiał. ⚠ Praktycznie, na resztę
etapu: **gdy audyt nazywa problem jedną wielkością, to jest hipoteza o problemie, a nie jego definicja.**
Zanim zaczniesz ją minimalizować, zapytaj, co jeszcze na tej powierzchni ma wartość — i czy przypadkiem
nie jest ważniejsze.

Build 0/0 · **7133** zielony w trzech partycjach (**7031 + 48 + 54**, czyli tyle co przed etapem) ·
smoke czysty.

---

### §19.13 Iteracja 8 (M3.2b) — §7.5, semantyka kolorów ⛔ WYCOFANA W CAŁOŚCI (2026-08-02)

> ⛔⛔ **CAŁA TA ITERACJA ZOSTAŁA COFNIĘTA — czytaj razem z §19.14, która niesie powód i pomiar.**
> Kod wrócił do stanu sprzed M3.2b; zapis zostaje, bo pokazuje **jak poprawne wykonanie ratyfikowanego
> zapisu może dać zły produkt**. ⭐ Jedno ustalenie z tej iteracji **przetrwało i obowiązuje**: korekta
> §7.5 o `NeutralIconBrush` vs `ForegroundBrush` (§19.13.3) — to fakt o kodzie, niezależny od kierunku.

**Poniższy zapis opisuje stan, który NIE ISTNIEJE w produkcie.**


**Zakres:** realizacja ratyfikowanego kontraktu §7.5 na obu paskach. ⚠ Decyzja **DC** obowiązuje:
likwidacja tokenów `AccentIconBrush` / `InfoIconBrush` **nie należy do tej iteracji** (24 wystąpienia
w 14 plikach, w tym powierzchnie M4.3) — M3.2b przestaje ich **używać w paskach**, tokeny żyją dalej.

#### §19.13.1 Pasek tytułu — dokładnie to, co §7.5 przewidział

Pomiar zgodził się z audytem co do sztuki. Wykonane: **6 narzędzi ogólnych** (Activity Monitor ·
Session Manager · Global Search · Script Executor · Data Import · Export DDL) traci `AccentBrush` ·
**Connect** traci `AccentIconBrush` · **Refresh** traci `InfoIconBrush` · **Usuń połączenie**
przechodzi z `WarningIconBrush` na `DangerIconBrush` (operacja nieodwracalna) · **10 × `IconColor_*`
bez zmian**, w tym Security Manager, który jest narzędziem, ale niesie kolor RODZAJU (§3.4/uściślenie 1).

⭐ **Wynik zmierzony po zmianie: pasek tytułu niesie teraz WYŁĄCZNIE dziesięć kolorów rodzaju i jeden
`DangerIconBrush`.** Zniknęła niebieska tapeta, na której te dziesięć kolorów się gubiło — czyli
dokładnie efekt zapowiedziany w §7.5 („pasek POZOSTAJE kolorowy"), a nie wyciszenie paska.

#### §19.13.2 ⭐⭐ Toolbar dokumentu — trzy dalsze naruszenia, których §7.5 nie wymieniło

§7.5 opisywało wyłącznie pasek tytułu. Toolbar dokumentu ma własne odstępstwa i **są ostrzejsze, bo
dotyczą par, które przez kolor mówiły nieprawdę**:

| Element | Było | Dlaczego to defekt |
|---|---|---|
| **Uncomment** (×3) | `DangerIconBrush` | odkomentowanie kodu **nie jest nieodwracalne** — jedno Ctrl+Z; token jest zarezerwowany dla Drop · Delete · Stop |
| **Comment** (×3) | `InfoIconBrush` | razem z powyższym **symetryczna para edycyjna czytała się jako „bezpieczna / groźna"** |
| **Execute procedury / funkcji** (×2) | `SuccessIconBrush` | `Success` w kontrakcie znaczy **wyłącznie zatwierdzenie transakcji**; uruchomienie procedury dzieje się w transakcji użytkownika i cofa je Rollback |

⭐ **Najgorszy z tych trzech jest przypadek Comment/Uncomment i warto wiedzieć dlaczego: kolor niósł
tam rozróżnienie, którego nie ma.** Brak koloru jest neutralny; kolor niosący fałszywą różnicę jest
gorszy niż jego brak, bo użytkownik mu wierzy. ⚠ Zdjęcie koloru nic nie kosztuje w czytelności — obie
ikony mają **różne geometrie** i własne tooltipy, więc kolor nigdy nie był tu jedynym nośnikiem (§8.4).

#### §19.13.3 ⚠⚠ Korekta §7.5 w miejscu — neutralny dla IKONY to nie `ForegroundBrush`

§7.5 mówiło: *„`ForegroundBrush` dla wszystkiego, co nie odpowiada na żadne z tych dwóch pytań"*.
**Zmierzone: to dwa różne tokeny i różnica jest celowa.**

| | Dark | Light |
|---|---|---|
| `ForegroundBrush` (tekst) | `#D4D4D4` | `#1B1D1F` |
| `NeutralIconBrush` (ikona) | `#C8CCD2` | `#3C3C3C` |

Ikona stroke'owana czyta się inaczej niż litera, więc ma własną wartość neutralną — **ustawioną jako
domyślna w `ControlTheme` kontrolki `SvgIcon`**. ⭐ Dlatego neutralizacja polega na **usunięciu
`Foreground`**, nie na podstawieniu innego tokenu: wpisanie `ForegroundBrush` rozjechałoby ikony paska
z ikonami całej reszty aplikacji **i** byłoby wartością lokalną tam, gdzie rola już odpowiada
(reguła 9 UI). §7.5 poprawione w miejscu.

⚠ To trzeci raz w M3, gdy zapis w dokumencie okazał się starszy niż produkt (po `Size.StatusBar`
i `Size.Row.Tree` z iteracji 0) — i pierwszy, w którym **poprawnie brzmiąca nazwa tokenu była pułapką**:
`ForegroundBrush` istnieje, jest sensowny i podstawiony bez pomiaru dałby zielony build oraz subtelnie
inny odcień ikon w jednym pasku.

#### §19.13.4 Poza zakresem, z powodem

| Element | Dlaczego nie tutaj |
|---|---|
| **Commit** (`SuccessIconBrush`) i **Rollback** (`DangerIconBrush`) → `CommitButtonBrush` / `RollbackButtonBrush` | to **decyzja DD** i osobny podetap **M3.2c** (H‑5). ⭐ Oba tokeny docelowe nie mają dziś **ani jednego konsumenta** w całej aplikacji |
| likwidacja `AccentIconBrush` / `InfoIconBrush` | decyzja **DC** → M4.3/M5. Zmierzone: oba mają konsumentów **poza** `MainWindow.axaml` (Data Import, Debugger, Trace, Performance, Table Detail, trzy ViewModele) |
| `OnAccentBrush` na ikonach przycisków `primary` (×12) | to nie kolor semantyczny, tylko **czytelność na wypełnieniu akcentem** — inny wymiar niż §7.5 |

#### §19.13.5 Wynik

Build 0/0 · **7133** zielony w trzech partycjach (**7031 + 48 + 54**) · smoke czysty · **bez nowych
testów**: iteracja nie wprowadza mechanizmu, tylko usuwa wartości lokalne, a strażnikiem poprawności
jest tu reguła zapisana w miejscu, nie asercja.
⛔ **Wycofane w całości po QA — §19.14.**

---

### §19.14 ⭐⭐ Odbiór M3.2b — WZORZEC, KTÓRY TRZEBA PRZERWAĆ, I POMIAR, KTÓRY ODWRACA PROBLEM (2026-08-02)

Użytkownik odrzucił kierunek M3.2b i — ważniejsze — **nazwał powtarzalny wzorzec w trzech ostatnich
iteracjach**. To jest najcenniejsza rzecz, jaka wyszła z M3.2, i dlatego stoi przed opisem samej zmiany.

#### §19.14.1 ⛔⛔ Diagnoza użytkownika — cytat, bo parafraza by go osłabiła

> *„Analiza jest bardzo dobra, pomiary są bardzo dobre, ale później próbujesz doprowadzić regułę do
> logicznej konsekwencji, zamiast jeszcze raz spojrzeć na gotową aplikację. Tak było z przeniesieniem
> Commit/Rollback na prawą stronę, kotwicami zostawiającymi puste miejsca, a teraz z kolorami.
> Za każdym razem argumentacja była logiczna, ale po uruchomieniu aplikacji UX okazywał się gorszy.
> Myślę, że problem leży w założeniu, że musi istnieć jedna uniwersalna reguła."*

⭐ **Cztery odrzucenia z rzędu, jeden mechanizm.** Za każdym razem: pomiar → reguła → **doprowadzenie
reguły do końca** → produkt gorszy. Ani razu nie zawiódł pomiar; za każdym razem zawiodło przekonanie,
że skoro reguła jest prawdziwa, to jej pełne zastosowanie jest ulepszeniem.

⚠ **To jest R8 łamane od strony, której R8 nie przewidywała.** R8 ostrzega przed traktowaniem pomiaru
jako argumentu końcowego — a ja pomiar wykonywałem uczciwie i dopiero **wniosek** z niego rozciągałem
za daleko. ⛔ Nowa formuła, mocniejsza: **reguła opisuje to, co już jest dobre, i nie jest mandatem do
zmiany wszystkiego, co do niej nie pasuje.** Element niezgodny z regułą bywa wyjątkiem, który działa.

#### §19.14.2 ⭐⭐ POMIAR, KTÓRY ODWRACA POSTAWIENIE PROBLEMU

Zmierzone po odrzuceniu, na **całej aplikacji**, a nie na dwóch paskach:

```
442 instancje SvgIcon w widokach
 39 z nich niesie Foreground        ⇒  91 % ikon aplikacji JEST JUŻ NEUTRALNYCH
```

⭐⭐ **To unieważnia sposób, w jaki postawiłem M3.2b.** Pracowałem tak, jakby aplikacja była przekolorowana
i trzeba ją wyciszyć — a wyciszona jest w 91%. Kolor jest **rzadkim wyjątkiem**, nie tapetą. Wrażenie
nadmiaru brało się stąd, że wszystkie kolorowe ikony skupiają się w dwóch paskach, czyli **dokładnie tam,
gdzie patrzyłem**. ⚠ To ta sama pułapka co przy monospace w Settings Center (§2.7): *survey próbkujący
miejsca, które ma powód oglądać, myli lokalne zagęszczenie z rozkładem globalnym.*

#### §19.14.3 ⭐⭐ PRAWDZIWA NIESPÓJNOŚĆ — ta sama akcja ma różne kolory w różnych modułach

Pełny inwentarz 39 kolorowych ikon pokazał defekt, którego §7.5 nie opisuje **i którego moja reguła
by nie naprawiła**:

| Akcja | Ile kolorów | Gdzie |
|---|---|---|
| **uruchom** (`Icon.Play`) | **3** | `OnAccentBrush` (SQL, Script) · `SuccessIconBrush` (procedura, funkcja, Trace) · `AccentIconBrush` (debugger Continue) |
| **edytuj** (`Icon.Pencil`) | **3** | `WarningIconBrush` (Procedure, Function) · `AccentIconBrush` (Data Import) · neutralny (Edit Connection) |
| **odśwież** (`Icon.RefreshCw`) | **3** | `InfoIconBrush` (pasek tytułu, Table) · `AccentIconBrush` (Data Import) · neutralny (Index) |
| **usuń trwale** (`Icon.Trash`) | **2** | `DangerIconBrush` (7×) · `WarningIconBrush` (usuń połączenie, usuń zapytanie) |
| **dodaj** (`Icon.Plus`) | **2** | `SuccessIconBrush` (Procedure, Function) · neutralny (pasek tytułu, sekcja 3) |

⭐ **Użytkownik znalazł jeden z tych przypadków sam, patrząc na ekran** — żółte przyciski przy Saved
Queries. Zmierzone: to `Icon.Trash` i `Icon.ListX` na `WarningIconBrush`, czyli **dwie operacje
destrukcyjne w kolorze ostrzeżenia**, podczas gdy identyczna operacja w pasku dokumentu jest czerwona.

⚠ Spójne już dziś i **nie wymagające niczego**: `Icon.Check` → zawsze `SuccessIconBrush` (5×),
`Icon.Stop` → zawsze `DangerIconBrush` (5×), 10 × `IconColor_*`. ⭐ To są **wzorce do naśladowania,
a nie do ujednolicania** — język kolorów powinien je opisać, nie zmienić.

#### §19.14.4 ⛔ Dlaczego kierunek M3.2b był zły — trzy konkretne straty

| Zmiana | Dlaczego pogorszyła |
|---|---|
| Execute procedury `Success` → neutralny | 🟢 to naturalny kolor „Uruchom". ⭐ **Nie kolidował z zielonym Commitem, bo konteksty są rozłączne i użytkownik ich nie myli** — czego moja reguła („`Success` = wyłącznie commit") nie dopuszczała |
| Comment / Uncomment → oba neutralne | ⚠⚠ **Rozróżnienie było CELOWE i zamówione wcześniej przez użytkownika**: ikony są bardzo podobne, a kolor pozwala je rozpoznać błyskawicznie. Uznałem je za „kolor niosący fałszywą różnicę"; w rzeczywistości niósł różnicę **prawdziwą i użyteczną** — po prostu inną niż moja kategoria |
| 6 narzędzi + Connect + Refresh → neutralne | wyszarzenie bez zysku; ⛔ **żółte Saved Queries — czyli miejsce z naprawdę wątpliwą semantyką — zostały nietknięte**, bo leżały poza dwoma paskami, które mierzyłem |

⭐⭐ **Wniosek, który obowiązuje dalej: w IDE kolor nie koduje wyłącznie „success / danger / neutral".
Koduje także RODZAJ AKCJI, i to jest jego główna praca — pozwala rozpoznać przycisk, zanim się go
przeczyta.** Te dwa systemy nie są sprzeczne, bo działają w rozłącznych kontekstach.

#### §19.14.5 ⭐ Nowe podejście — ratyfikowany przez użytkownika ODWRÓT KOLEJNOŚCI

> **Użytkownik:** *„Nie próbujmy teraz wyprowadzać reguły z istniejących przycisków. Najpierw zdefiniujmy
> język kolorów EmberTerna: jakie role kolorów chcemy mieć, do czego każdy kolor służy, gdzie dopuszczamy
> wyjątki, a dopiero potem przypiszmy do tych ról wszystkie przyciski."*

⛔ **M3.2b nie zostanie wznowione w dawnej postaci.** Kolejność prac jest odwrócona: **projekt języka →
akceptacja → przypisanie przycisków**. §7.5 przestaje być źródłem, z którego się dedukuje — staje się
jednym z wejść do projektu, obok pomiaru z §19.14.2/§19.14.3 i szkicu ról od użytkownika
(🟢 uruchom · 🟢 commit · 🔵 debugger · 🟣 monitoring · 🟡 pomocnicze/specjalne · 🔴 destrukcja/stop).

#### §19.14.6 Stan

Kod wrócony do stanu sprzed M3.2b (`MainWindow.axaml` z commita wycofującego B1). ⭐ Zachowana jedyna
rzecz, która nie zależy od kierunku: **korekta §7.5 o `NeutralIconBrush` vs `ForegroundBrush`** — to
fakt o kodzie, nie decyzja projektowa.

Build 0/0 · **7133** zielony w trzech partycjach (**7031 + 48 + 54**) · smoke czysty.

---

### §19.15 Iteracja 9 (K1) — `ActionRunBrush`, rola R‑1 dostaje własny token (2026-08-03)

> Pierwszy krok planu wdrożenia języka kolorów (`color-language.md` §11.2). ⭐ **Krok NEUTRALNY
> wizualnie** (§11.1) — jedyna klasa kroków, która nie podlega R14, bo nie może pogorszyć UX: nic
> na ekranie się nie zmienia. Odbiór to **pomiar zerowej różnicy**, nie ocena wyglądu.

#### §19.15.1 Co zrobiono

Nowy token roli **R‑1 „Uruchom"** — `ActionRunColor` + `ActionRunBrush` w **obu** słownikach
`Colors.axaml` — podstawiony pod trzy ikonowe wystąpienia tej roli: **Execute procedury** i
**Execute funkcji** (`MainWindow.axaml`) oraz **Start trace** (`TraceMonitorTabView.axaml`).

⚠ **Execute SQL i Run script pozostają nietknięte i to nie jest przeoczenie** — oba są wariantem
**głównym** (`Classes="primary"`, ikona `OnAccentBrush`), więc rolę niesie tam wypełnienie przycisku,
nie ikona (§1.1 + §3.1 języka). Zmierzone: 5 przycisków „Uruchom", z czego 2 na `primary`.

#### §19.15.2 ⭐ Jedno rozstrzygnięcie implementacyjne — własny `Color`, nie alias

Token można było zapisać dwojako. Wybrano **własny klucz koloru z powtórzoną wartością**, nie
`Color="{StaticResource SuccessIconColor}"`, i to jest istota decyzji **W4**:

| Wariant | Skutek |
|---|---|
| alias nad `SuccessIconColor` | ⛔ „Uruchom **to jest** kolor sukcesu" — przestrojenie zieleni Commita **przesuwa po cichu także Execute**. Czyli dokładnie to zlanie ról, które W4 kończy |
| ⭐ własny `ActionRunColor` | dwie role, dwie wartości, dziś celowo równe. Rozdzielenie odcieni w przyszłości to **jedna linijka** i nie dotyka drugiej roli |

Koszt: jedna wartość zapisana dwa razy w katalogu — ⚠ **w katalogu, a nie w widoku**, więc reguła
UI #1 („żadnych kolorów w widokach") nie jest naruszona. Powód jest zapisany komentarzem **w miejscu**,
bo „ujednolicenie tych dwóch linijek przez alias" wygląda jak porządkowanie, a jest cofnięciem W4.

#### §19.15.3 Pomiar odbiorczy — dowód zerowej różnicy wizualnej

| Motyw | `SuccessIconColor` | `ActionRunColor` | Różnica |
|---|---|---|---|
| Dark | `#6DBE7E` | `#6DBE7E` | **brak** |
| Light | `#2E8B4F` | `#2E8B4F` | **brak** |

Konsumenci `ActionRunBrush`: **3** (dokładnie te z planu). Konsumenci `SuccessIconBrush`: **33 → 30**
— pozostają Commit (toolbar, Script Executor, Data Import), Validate, chipy zdrowia sesji, wiersze
INSERT w Trace, werdykt Performance. ⭐ Żadne z nich nie jest rolą R‑1, co jest właśnie tym, co K1
rozdziela.

#### §19.15.4 ⛔ Czego test świadomie NIE przypina

`ActionRunBrush_IsItsOwnRoleToken_InBothThemes` pilnuje **dwóch trwałych** faktów: token rozwiązuje
się w obu motywach (reguła 3 — token w jednym słowniku kompiluje się i nie maluje nic w drugim), oraz
`ActionRunColor` jest **osobnym kluczem**, a nie aliasem (właściwość W4).

⛔ **Nie przypina równości z `SuccessIconColor`, mimo że dziś zachodzi.** Ta równość jest chwilowa
**z założenia** (§7.3 języka: *„na razie ten sam odcień"*), więc przypięta byłaby testem pilnującym,
żeby projekt się nie wydarzył — zablokowałaby dokładnie tę zmianę, dla której token powstał. Zerowa
różnica jest **jednorazowym pomiarem odbiorczym** (§19.15.3), nie inwariantem. ⭐ Reguła ogólna:
*wartość, która ma się rozejść, nie jest asercją — jest pomiarem z datą.*

#### §19.15.5 Stan

Build 0/0 · **7134** zielony w trzech partycjach (**7031 + 49 + 54**, +1) · smoke czysty.
⏸ Następny krok: **K2** — destrukcja 🟡 → 🔴 (`color-language.md` §11.2). ⚠ To już krok **wizualny**,
więc podlega R14 i wymaga obejrzenia na żywo.

---

### §19.16 Iteracja 10 (K2) — destrukcja 🟡 → 🔴 (2026-08-03)

> Pierwszy krok **WIZUALNY** wdrożenia języka, więc pierwszy podlegający **R14** — i pierwszy
> przepuszczony przez nową bramkę **§0.5 języka** (*czy użytkownik szybciej rozpozna akcję?*).

#### §19.16.1 Odpowiedź na pytanie nadrzędne — osobno dla każdego przycisku

| Przycisk | Szybsze rozpoznanie? | Dowód |
|---|---|---|
| **Usuń połączenie** (drzewo) | ✅ **TAK** | ⭐⭐ **Przycisk nie zgadzał się z WŁASNYM menu kontekstowym**: `Delete` w menu tego samego drzewa jest czerwone (`MainWindow.axaml:475/525`), przycisk paska był żółty. Dwie drogi do jednej operacji, dwa kolory, w jednym oknie |
| **Usuń zapytanie** (Saved Queries) | ✅ **TAK** | ten sam wzorzec — menu kontekstowe wiersza czerwone (`:1437`), przycisk nagłówka żółty |
| **Wyczyść wszystkie** (`Icon.ListX`) | ✅ **TAK, najmocniej** | z całej trójki **najdalej idące** (kasuje cały zbiór), a niosło kolor, który w języku EmberTerna znaczy *„uwaga / pauza"* (Pause, Break on exception). **Sygnał był ZANIŻONY względem skutku** — to korekta poprawności, nie tylko spójności |

⚠ **Kontrola pułapki 17 — czy to był świadomy wyjątek?** Nie, i da się to wskazać palcem: pierwotna
legenda w `Colors.axaml` mówiła **„Warning=delete"** — spójny, ale **porzucony** schemat. Osiem
edytorów i 131 pozycji menu przeszło na czerwień, legenda została. ⭐ To dryf z porzuconego systemu,
a nie decyzja o tych trzech przyciskach — brak wpisu w §5 języka jest tu zgodny z historią.

⚠ **Drugie potwierdzenie, z kodu, nie z estetyki: wszystkie trzy operacje już były sklasyfikowane jako
destrukcja** — `IsDestructive = true` w `DeleteWithConfirmationAsync`, `DeleteSavedQueryAsync`
i `ClearAllQueriesAsync`. **Model aplikacji i jej kolor mówiły co innego**; K2 usuwa rozjazd po
stronie, która się myliła.

#### §19.16.2 Co zrobiono

Trzy `WarningIconBrush` → `DangerIconBrush` (`MainWindow.axaml`). Po zmianie **nie ma w produkcie ani
jednego żółtego przycisku usuwania** — pozostałe `WarningIconBrush` to Break on exception (R‑5),
Pause (R‑5) i licznik `UpdateChange` w podsumowaniu zmian (stan, nie akcja).

⚠ **Poprawiona przy okazji legenda w `Colors.axaml`** — i to nie jest kosmetyka: to ona zrodziła dryf,
więc zostawiona kazałaby następnej osobie pomalować kolejny kosz na żółto. ⭐ Kształt gotchy **#284,
tylko w komentarzu zamiast w stringu**: komentarz przeżył zmianę, którą opisywał, i przez to nadal
„uczył" nieprawdy przy zielonym buildzie. Zastąpiona wskazaniem na `color-language.md` jako **jedyne**
źródło znaczeń — ⛔ z zakazem dopisywania tam drugiej legendy, bo to ona rozjedzie się jako następna.

#### §19.16.3 ⭐ Strażnik — warunek, nie licznik

`DestructiveIcon_AlwaysCarriesTheDangerToken` skanuje **źródło** widoków i wymaga, by każdy
`Icon.Trash` / `Icon.ListX` w `SvgIcon` niósł `DangerIconBrush`. **Zweryfikowany przez zasadzenie
naruszenia** — upada z nazwą pliku i nazwą złego tokenu.

⭐ Dowodem nie może być liczba, bo nowy kosz pomalowany „jakoś" przechodzi build i każdy inny test,
a wygląda źle dopiero na cudzym ekranie. ⚠ Skan ma własny bezpiecznik (`scanned >= 8`): regex, który
przestał pasować do widoków, „przechodzi" zawsze — to ta sama klasa błędu co filtr partycji
wskazujący nieistniejącą klasę (§18.1.6).

⚠ **Gdy pojawi się kosz, który celowo ma inny kolor, ten test ma UPAŚĆ** — odpowiedzią jest wpis
w §5 języka, a nie rozluźnienie warunku.

#### §19.16.4 ⏸ Jedno miejsce do obejrzenia w QA

W nagłówku panelu Saved Queries stoją teraz **dwie czerwone ikony obok siebie** (kosz + lista‑X) na
trzy przyciski. Oba są R‑4, więc język jest po stronie tej zmiany, a rozróżnienie „jedno vs wszystkie"
niesie **kształt** — dokładnie odwrotnie niż w wyjątku W‑1, gdzie ikony Comment/Uncomment są zbyt
podobne i to kolor musi je rozróżniać. ⚠ Ale to jest sąd wizualny, nie dokumentowy: jeśli w oknie
czyta się jako ściana czerwieni, poprawiamy **regułę w §5 języka**, nie bronimy implementacji (§0.5).

#### §19.16.5 Stan

Build 0/0 · **7135** zielony w trzech partycjach (**7032 + 49 + 54**, +1) · smoke czysty.
⏸ **Czeka na QA wizualne użytkownika.** Następny krok po akceptacji: **K3** (Edytuj → ⚪).

---

### §19.17 Iteracja 11 (K3–K7) — dokończenie języka kolorów w jednym przebiegu (2026-08-03)

> ⭐ **Zmiana trybu pracy, na polecenie użytkownika:** *„Mamy już zaakceptowany `color-language.md`
> i nie chcę dalej pracować w tak drobnych iteracjach. Potraktuj K2–K7 jako jedną implementację…
> Pełne QA zrobimy dopiero po zakończeniu całego etapu."* Powrót do użytkownika tylko w trzech
> wypadkach: dokument nie rozstrzyga · realny konflikt projektowy · zmiana pogorszyłaby produkt mimo
> zgodności z dokumentem. ⭐ **Dwa z tych trzech wypadków wystąpiły** i są niżej (§19.17.2, §19.17.5).

#### §19.17.1 Wykonane

| Krok | Miejsc | Co |
|---|---|---|
| **K3** Edytuj → ⚪ | **1** | zmiana nazwy profilu w Data Import |
| **K4** Wskaż plik → `AccentBrush` | 1 | wybór pliku źródłowego (Data Import) |
| **K6** Odśwież → ⚪ | 3 | drzewo metadanych · dane tabeli · Data Import |
| **K7** Commit / Rollback → własne tokeny | 6 + 2 tokeny × 2 motywy | toolbar · Script Executor · Data Import |
| **§7.4** Pause → `WarningIconBrush` | 1 | Trace (przewidziane w §11.3 jako „przy okazji") |

#### §19.17.2 ⚠⚠ Pomiar obalił TRZY wiersze §8.2 — i to jest wynik, nie brak

**K5 odpadł w całości, a K3 skurczył się z 3 miejsc do 1.** Przyczyna jest jedna i warto ją zapamiętać:

> ⭐⭐ **Inwentarz §20 zliczał `SvgIcon` po TOKENIE, więc glif STANU trafił do tabeli AKCJI.**

* „Edytuj / Dodaj (Procedure, Function)" to w rzeczywistości wiersze `UpdateChange` / `InsertChange`
  w **karcie podsumowania zmian** („Tabela X: +3 wstawione, ~2 zmienione, −1 usunięty"). To
  **komunikat o stanie**, który §2 języka wyklucza wprost.
* „Szukaj w widoku (Trace)" to **glif wewnątrz pola tekstowego**, nie przycisk. Tu §0.5 odpowiada
  wprost „nie": pociemnienie z `SubtleForegroundBrush` do `NeutralIconBrush` uczyniłoby **dekorację
  głośniejszą od treści**, którą oznacza.
* „Edit Connection" był **już neutralny** — pozycja istniała tylko w tabeli.

⭐ **Lekcja ogólna:** *pomiar po nośniku (ikona + token) nie odróżnia roli od stanu; robi to dopiero
kontekst, w którym element stoi.* §8.2 języka poprawione w miejscu, z tym zdaniem jako konsekwencją
dla następnych prac.

#### §19.17.3 ⭐ K6 okazał się odwrotnością swojej reputacji

Plan oznaczył K6 jako **największe ryzyko** („dotyka paska tytułu, czyli powierzchni, na której M3.2b
zostało odrzucone"). Zmierzone na miejscu — jest przeciwnie, i to jest krok, który **wzmacnia**
niebieski zamiast wygaszać pasek:

W tym samym pasku niebieski niesie rolę **R‑6 „wejście do narzędzia"**: Activity Monitor, Session
Manager, Global Search, Script Executor, Data Import — **pięć przycisków, wszystkie `AccentBrush`**.
Odświeżenie **nie otwiera modułu**; działa na widoku, który już jest na ekranie. Dopóki było
niebieskie, **jeden kolor znaczył w jednym pasku dwie różne rzeczy**.

⛔ **To nie jest to samo co M3.2b:** tamto **odbarwiło sześć wejść do modułów**; ten krok ich nie
dotyka (§11.3 — zostają kolorowe) i zdejmuje kolor z jedynego elementu, który go **udawał**.

#### §19.17.4 ⭐ K7 zaczęty od wartości — i dzięki temu wyszedł neutralny

§7.2 kazało zacząć od nadania tokenom wartości per motyw, nie od podmiany odwołań. Zmierzone:
`CommitButtonForeground` / `RollbackButtonForeground` niosły surowy Material (`#4CAF50` / `#F44336`),
**identyczny w Dark i Light**, wstawiony na zapas i **bez ani jednego konsumenta**.

Nadane wartości = **dostrojone pary `SuccessIconColor` / `DangerIconColor`**, czyli dokładnie to, czym
Commit i Rollback malowały się dotąd. ⇒ **K7 jest krokiem NEUTRALNYM wizualnie** (klasa §11.1),
a role dostają własne tokeny — ten sam zabieg co `ActionRunColor` w K1.

⛔ **Nie wymyślono nowych odcieni**, i to jest decyzja: wymyślenie ich byłoby projektowaniem, którego
dokument nie robi, i wnosiłoby ryzyko kontrastu w motywie jasnym — czyli dokładnie to, przed czym
§7.2 ostrzega. Rozdzielenie odcieni jest teraz możliwe jako osobna, świadoma decyzja.

⭐ Strażnik `TransactionRoleBrush_IsTunedPerTheme` pilnuje **warunku, nie wartości**: powrót do jednej
wartości w obu słownikach jest powrotem defektu i jest niewidoczny dla buildu (token istnieje i
poprawnie się rozwiązuje w obu motywach).

#### §19.17.5 ⏸ Connect — jedyna pozycja §8.2, której świadomie NIE wykonano

§8.2 mówi **Connect → R‑7 ⚪** („nie otwiera modułu, działa na zaznaczeniu") i formalnie ma rację.
**Nie wykonane**, bo bramka §0.5 odpowiada **„nie wiadomo"**, a to jest odpowiedź odmowna:

Connect jest **główną akcją tego paska**, a niebieski jest dziś jedyną rzeczą odróżniającą go od
Edytuj / Kopiuj / Rozłącz / Połącz ponownie. Po zdjęciu koloru lewa część paska staje się jednolicie
szara poza czerwonym koszem — **rozpoznanie może się spowolnić**, a to jest dokładnie mechanizm,
przez który M3.2b zostało odrzucone. ⚠ Zauważalne: **§11 sam nie ponumerował tego wiersza**, choć
ponumerował osiem pozostałych — prawdopodobnie z tego samego powodu.

Zapisane jako **O‑4** w §9 języka, do rozstrzygnięcia w §13.3 **na całym pasku naraz**. Przy okazji
odnotowane **O‑5**: Security Manager niesie `IconColor_Role` (kolor RODZAJU) mimo bycia przyciskiem
R‑6 — możliwy świadomy wyjątek, jedyny taki wśród sześciu narzędzi.

#### §19.17.6 Stan produktu po całym wdrożeniu K1–K7

Zmierzone: **230 `SvgIcon` w widokach, 87 z kolorem.** Wszystkie przyciski akcji są w rolach języka
poza **trzema świadomie otwartymi**: Connect (**O‑4**), Debugger Continue (**O‑1**),
Comment/Uncomment ×4 (**O‑2**). Reszta koloru to `IconColor_*` (S1 — rodzaj), `OnAccentBrush`
(wariant `primary`, S4), chipy stanu i wiersze podsumowań (§2).

⭐ **Ani jednego żółtego przycisku usuwania · ani jednego „Odśwież" w kolorze · jedno „Uruchom" ·
jeden token dla R‑5 · Commit i Rollback na własnych rolach.**

Build 0/0 · **7137** zielony w trzech partycjach (**7032 + 51 + 54**, +2) · smoke czysty.
⏸ **Cały etap czeka teraz na PEŁNE QA WIZUALNE** — zgodnie z ustaleniem oceniamy spójność
kolorystyczną całej aplikacji naraz, w obu motywach, a nie krok po kroku.

---

### §19.18 Przegląd domykający — pięć pozostałości, zero otwartych wyjątków (2026-08-03)

> **Użytkownik po obejrzeniu K1–K7:** *„Kierunek jest dobry i najważniejsze, że nie powtórzyła się
> sytuacja z pierwszego M3.2b… Natomiast mam podobne odczucie jak Ty sam opisałeś przy Connect —
> chyba zostaliśmy zbyt zachowawczy… Chcę mieć poczucie, że język został wdrożony w całym produkcie,
> a nie w 90%."* Zadanie: **nie szukać reguł, szukać wyjątków wartych domknięcia**; wyjątek świadomy
> i poprawiający UX zostaje, pozostałość po starym stanie — do zgodności.

#### §19.18.1 Pięć domkniętych pozostałości

| Element | Było | Jest | Dlaczego to była pozostałość, a nie decyzja |
|---|---|---|---|
| **Security Manager** (pasek tytułu) | `IconColor_Role` | `AccentBrush` | ⭐ **Wskazany przez użytkownika.** Jedyny z sześciu przycisków R‑6, który zamiast koloru ROLI (S2) nosił kolor RODZAJU obiektu (S1) — ślad po czasach, gdy przycisk „dziedziczył" kolor po tym, **o czym** jest. §1.2/3 mówi wprost: przy kolizji rodzaju ze skutkiem **wygrywa skutek** |
| **Connect** (dawne O‑4) | `AccentIconBrush` | ⚪ | ⚠⚠ **Wstrzymane w §19.17 i wykonane teraz — bo zmienił się kontekst, nie zdanie.** Po K6 Connect został **ostatnim** elementem paska na `AccentIconBrush`, obok sześciu `AccentBrush` w roli R‑6. Dwa odcienie niebieskiego znaczące co innego w jednym pasku |
| **Debugger Continue** (dawne O‑1) | `AccentIconBrush` | `ActionRunBrush` | ⭐ **Kolizja dwóch ratyfikacji okazała się pozorna:** D15.2 Seam A chciało **wyróżnienia**, nie akurat niebieskiego — ten wybrano, gdy token R‑1 nie istniał. Continue nadal jest jedynym wyróżnionym przyciskiem tego paska. ⚠ Niebieski jest w debuggerze kolorem **tożsamości modułu**, a W6 zabrania malować nim przycisk akcji |
| **Uncomment ×4** (dawne O‑2) | `DangerIconBrush` | ⚪ | ⛔ **W‑1 ZOSTAJE** — rozróżnienie kolorem było zamówione i działa; zmieniła się para. Czerwień obiecywała nieodwracalność na akcji cofanej **jednym Ctrl+Z**, osłabiając czerwień dokładnie tam, gdzie K2 ją zbudowało |
| **Waliduj** (Data Import) | `SuccessIconBrush` | ⚪ | ⚠ Zmierzona kolizja: **„Waliduj" i „Zatwierdź" niosły w JEDNYM pasku tę samą ikonę `Icon.Check` w tym samym zielonym.** Walidacja niczego nie zapisuje ani nie zatwierdza |

#### §19.18.2 ⚠⚠ Wariant (c) z §9.1 zmierzony i odrzucony

Dokument sam rekomendował dla Comment/Uncomment „parę w obrębie jednego odcienia": `InfoIconBrush` +
ciemniejszy `AccentIconBrush`. **Zmierzone: `#5BA7D0` vs `#5B9BD5` w Dark** (i `#2A7AA8` vs `#2D6BBF`
w Light) — dwanaście jednostek na jednym kanale, jako **kreska ikony nie do odróżnienia**. Wariant
skasowałby wyjątek, który miał chronić.

⭐ Zamiast wymyślać nowy odcień (czego dokument nie robi i o co użytkownik nie prosił) para poszła
w **niebieski vs szary** — rozróżnienie natychmiastowe, zero nowych tokenów, czerwień zwolniona.
⚠ To jedyne miejsce tego przeglądu, gdzie podjąłem decyzję projektową w otwartym pytaniu; reszta to
doprowadzenie do już zapisanego stanu.

#### §19.18.3 Co świadomie ZOSTAŁO — i dlaczego to nie jest dług

* **W‑3** rozłącz cudzą sesję 🔴 — dotyka pracy innego użytkownika.
* **W‑1** Comment 🔵 vs Uncomment ⚪ — zamówione rozróżnienie, teraz uczciwą parą.
* **Wiersze podsumowania zmian** w edytorach Procedure/Function (`+3 / ~2 / −1`), **stan „brak
  ostrzeżeń"** w Session Managerze, **chevrony** i **glif lupy** w polu tekstowym — to **stany
  i dekoracje**, które §2 wyklucza z języka. ⚠ To one wcześniej trafiły do inwentarza §20 jako
  „akcje" (§19.17.2).
* **`AccentIconBrush` i `InfoIconBrush` NIE są sierotami** i nie zostały zlikwidowane (decyzja **DC**
  → M4.3/M5): pierwszy maluje chip stanu debuggera, żarówkę Quick Fix i znak `DebuggerIcon`, drugi
  niesie Comment.

#### §19.18.4 Stan końcowy — pomiar

**230 `SvgIcon` w widokach, 81 z kolorem.** ⭐ **Ani jeden przycisk akcji nie stoi poza językiem.**
Rozkład: `DangerIconBrush` 17 (R‑4) · `OnAccentBrush` 15 (S4) · `AccentBrush` 12 (R‑6) ·
`ActionRunBrush` 4 (R‑1) · `WarningIconBrush` 4 (R‑5 + 2 wiersze stanu) · `InfoIconBrush` 4 (W‑1) ·
`CommitButtonBrush` 3 (R‑2) · `RollbackButtonBrush` 3 (R‑3) · `IconColor_*` 10 (S1) · reszta to
dekoracje i stany.

⚠ Poprawiona przy okazji **lista tokenów Data Importu** w `ConnectionExpandBindingProbe` — wymieniała
`AccentIconBrush`/`SuccessIconBrush`, których moduł już nie maluje, i nie znała `CommitButtonBrush`/
`RollbackButtonBrush`. ⭐ To ten sam kształt co filtr partycji z §18.1.6: **lista nazw starzeje się
cicho, bo nazwa, której nikt nie używa, nadal się rozwiązuje i test przechodzi.**

#### §19.18.5 ⭐ Lekcja, którą ten przegląd potwierdził trzeci raz

> **Zgodność z dokumentem nie jest tym samym co spójność produktu.**

Wszystkie pięć pozostałości było „zgodnych" w tym sensie, że nikt nie zapisał ich jako defektu —
a każdą widać na pierwszy rzut oka na gotowym ekranie. ⚠ Dwie z nich (Connect, Continue) sam
wcześniej **wstrzymałem** jako „nie wiadomo"; obie okazały się rozstrzygalne, gdy patrzyło się na
**cały pasek naraz**, a nie na pojedynczy przycisk. To jest argument za przeglądem całości (§13.3)
jako osobnym krokiem, a nie sumą odbiorów pojedynczych iteracji.

Build 0/0 · **7137** zielony w trzech partycjach (**7032 + 51 + 54**) · smoke czysty.

---

### §19.19 Poprawka odbiorcza — wspólna oś pionowa bloku połączenia (2026-08-03)

> Ostatnia rzecz przed zamknięciem etapu. Użytkownik: *„tekst z nazwą połączenia oraz adresem nie jest
> optycznie wyśrodkowany względem zielonej kropki… kropka wygląda poprawnie, natomiast tekst sprawia
> wrażenie osadzonego kilka pikseli niżej."*

#### §19.19.1 ⚠⚠ To NIE było zgłoszenie, które §19.8 już rozstrzygnęło

W tym samym miejscu stoi bardzo stanowczy komentarz z §19.8: **„⛔⛔ NIE PRÓBUJ WYŚRODKOWAĆ TEGO RUNU
W PIONIE"**, poparty trzema pomiarami. Łatwo było uznać sprawę za zamkniętą — i byłby to błąd, bo tamten
pomiar odpowiadał na **inne pytanie**: relację *endpoint ↔ nazwa* (dwa runy WEWNĄTRZ jednego
`TextBlocka`). Zgłoszenie dotyczyło relacji *tekst ↔ kropka*, czyli **sąsiadujących elementów wiersza**.

⭐ **Lekcja: zakres wcześniejszego pomiaru trzeba przeczytać, zanim się go użyje jako odpowiedzi.**
Komentarz był prawdziwy i nadal obowiązuje; po prostu nie dotyczył tego, o co pytano. Dopisano do niego
zdanie rozgraniczające, żeby następny czytelnik nie uznał tematu za zamknięty w całości.

#### §19.19.2 Pomiar — trzy elementy, trzy osie

Wiersz ma 16 px; współrzędne w układzie wiersza:

| Element | Wysokość | Środek PRZED | Środek PO |
|---|---|---|---|
| kropka | 7 | **7,50** | **8,00** |
| tekst | 16 | 8,00 | 8,00 |
| badge `DEV MODE` | 13 | **8,50** | **8,00** |

⭐ **Przyczyna jest arytmetyczna, nie typograficzna.** `VerticalAlignment="Center"` liczy
`(16 − h) / 2`, więc element o wysokości **nieparzystej** ląduje na połówce piksela — a
`UseLayoutRounding` przycina **każdy element osobno**: kropkę (7) w górę, badge (13) w dół. Tekst ma
16, czyli pełną wysokość wiersza, i nie przesuwa się wcale. Stąd **cały piksel** rozjazdu między
skrajnymi elementami i pół piksela między kropką a tekstem; przy 125 % robi się z tego pełny piksel
urządzenia — i to właśnie było widać.

#### §19.19.3 Poprawka — i dwie drogi, których nie wybrano

**`UseLayoutRounding="False"`** na kropce i na badge'u. ⭐ **Żaden rozmiar się nie zmienia** — a to
było wymaganie wprost ze zgłoszenia (*„kropka wygląda poprawnie"*): problemem jest pozycja, nie
wielkość.

* ⛔ **Nie przez zmianę rozmiarów** (kropka 7→8, badge 13→14): działa arytmetycznie, ale zmienia wygląd
  elementów, na które nikt się nie skarżył.
* ⛔ **Nie marginesem**: nudge trafia w jedno DPI i psuje pozostałe (§19.3.3).

⭐ **Precedens w tym repo:** `PART_MarkArea` w `RadioButton` — *przyciąganie do piksela pomaga PROSTEJ
KRAWĘDZI i nie ma nic do zaoferowania KOŁU*. Kropka jest kołem; badge jest wypełnieniem bez obrysu,
więc pół piksela zbiera antyaliasing, a nie widoczna kreska.

#### §19.19.4 ⛔⛔ DRUGI ODBIÓR — poprawka BYŁA NIEWYSTARCZAJĄCA, a test dowodził złej rzeczy

> **Użytkownik:** *„Rozumiem Twoją analizę i pomiary, ale użytkownik nie patrzy na środki geometryczne
> elementów — patrzy na efekt optyczny… potraktuj pomiary jako narzędzie diagnostyczne, a nie
> kryterium zakończenia zadania. Kryterium odbioru jest wygląd na ekranie."*

⭐⭐ **To jest R8 w najczystszej postaci, jaka wystąpiła w całym etapie — i tym razem po stronie
narzędzia, nie reguły.** Wyrównałem środki **PUDEŁEK**, test świecił na zielono, a ekran był nadal zły.

**Dlaczego pudełko kłamie:** wysokość `TextBlocka` to **INTERLINIA**, a nie wysokość farby. Linia
bazowa leży na 11,83, więc dolne ~4 px to obszar znaków schodzących — w napisie
`Szkoleniowa · localhost:3050` **pusty, bo nie ma tam ani jednego znaku schodzącego**. Farba siedzi
więc wysoko w ascencie i **nisko w pudełku**: środek masy wypada ok. **9,0**, podczas gdy kropka —
której farba **jest** jej pudełkiem — leży na **8,0**. Około piksela, dokładnie jak w zgłoszeniu.

**Poprawka:** `TranslateTransform Y="-1"` na `TextBlocku`. ⚠ `RenderTransform`, nie margines: nie
rusza układu, więc nie wchodzi w interakcję z zaokrąglaniem pudełek i nie przesuwa sąsiadów; wartość
całkowita, więc tekst nie robi się rozmyty.

⚠⚠ **Przy okazji COFNIĘTE `UseLayoutRounding="False"` NA BADGE'U — ono pogarszało wygląd.** Powód
jest pouczający i uogólnia się: **kropka jest jedynym elementem tego wiersza, którego FARBA JEST JEGO
PUDEŁKIEM**, więc dla niej wyrównanie geometryczne jest zarazem optycznym. Badge ma w środku wersaliki
z paddingiem, więc jego farba leży **wysoko** w pudełku — a zaokrąglenie w dół, które „psuło"
geometrię, w rzeczywistości tę różnicę nadrabiało. ⭐ **`UseLayoutRounding="False"` jest narzędziem dla
elementu, który JEST swoją farbą; dla elementu z tekstem w środku trzeba patrzeć na farbę.**

#### §19.19.5 ⭐ Pin został ZAWĘŻONY, bo dowodził złej rzeczy

`StatusBarConnectionBlock_SharesOneVerticalAxis` porównywał środki pudełek trzech elementów —
**przechodził na zielono przy zepsutym ekranie**. ⛔ Test, który świeci na zielono, gdy wygląd jest
zły, jest **gorszy niż brak testu**: zamyka temat, zamiast go otworzyć.

Zastąpiony przez `StatusBarConnectionDot_SitsOnTheRowAxis`, który pilnuje **wyłącznie kropki** — czyli
jedynego elementu, o którym maszyna ma tu coś sensownego do powiedzenia. ⛔ Nie „wzmacniać" go
z powrotem o tekst i badge: ich wyrównanie jest **korektą optyczną, której kryterium odbioru jest
ekran, nie liczba**.

Build 0/0 · **7138** zielony w trzech partycjach (**7032 + 52 + 54**) · smoke czysty.

---

### §19.20 🔒 PODSUMOWANIE ZAMYKAJĄCE — język kolorów wdrożony i odebrany (2026-08-03)

> **Czytaj to przed jakąkolwiek dalszą pracą nad M3.** Zastępuje potrzebę czytania §19.13–§19.19
> w całości.

#### §19.20.1 Co zostało dostarczone

| | |
|---|---|
| **Projekt** | [`color-language.md`](color-language.md) — dokument **produktu**, nie etapu; obowiązuje przy każdej nowej funkcji |
| **Wdrożenie** | K1 (§19.15) · K2 (§19.16) · K3–K7 (§19.17) · przegląd domykający (§19.18) · poprawka odbiorcza paska statusu (§19.19) |
| **Stan** | **230 `SvgIcon` w widokach, 81 z kolorem, ani jeden przycisk akcji poza językiem.** Zero otwartych pytań O‑1…O‑5 |
| **Nowe tokeny** | `ActionRunColor`/`Brush` (R‑1) · wartości per motyw dla `CommitButtonForeground`/`RollbackButtonForeground` (R‑2/R‑3) |
| **Strażnicy** | `DestructiveIcon_AlwaysCarriesTheDangerToken` · `TransactionRoleBrush_IsTunedPerTheme` · `StatusBarConnectionDot_SitsOnTheRowAxis` |
| **Odbiór** | ✅ użytkownik, 2026-08-03. Wypchnięte na oba remote'y |

#### §19.20.2 ⭐⭐ Trzy nowe reguły — R15, R16, R17

Wszystkie trzy padły z ust użytkownika w tej sesji i **dołączają do R1–R14** (handover §5).

| # | Reguła | Skąd |
|---|---|---|
| **R15** | ⭐ **Wielkość iteracji idzie za NIEPEWNOŚCIĄ, nie za ostrożnością.** Drobne kroki, dopóki projekt się formuje; jeden przebieg, gdy jest zaakceptowany | *„Nie chcę dalej pracować w tak drobnych iteracjach, zaczyna nas to bardziej spowalniać niż pomagać"* — po akceptacji `color-language.md` |
| **R16** | ⭐⭐ **Pomiar jest narzędziem DIAGNOSTYCZNYM; kryterium odbioru jest ekran.** Konsekwencja twarda: **test, który świeci na zielono przy złym wyglądzie, jest GORSZY niż brak testu** — zamyka temat zamiast go otworzyć | *„Użytkownik nie patrzy na środki geometryczne elementów — patrzy na efekt optyczny… potraktuj pomiary jako narzędzie diagnostyczne, a nie kryterium zakończenia zadania"* (§19.19.4) |
| **R17** | ⭐ **Zgodność z dokumentem ≠ spójność produktu.** Przegląd całej powierzchni jest **osobnym krokiem**, nigdy sumą odbiorów pojedynczych iteracji | *„Chcę jeszcze raz przejrzeć całość pod kątem spójności produktu, a nie tylko zgodności z dokumentem"* (§19.18) |

⚠ **R16 jest rozszerzeniem R8 na NARZĘDZIA**, nie jego powtórzeniem: R8 mówił, że pomiar nie jest
argumentem końcowym dla *reguły*; R16 mówi to samo o *teście*, który tę regułę pilnuje.

#### §19.20.3 ⚠ Cztery pułapki, za które ta sesja zapłaciła

| # | Pułapka | Koszt |
|---|---|---|
| **18** | ⭐⭐ **Pudełko ≠ farba.** Wysokość `TextBlocka` to INTERLINIA; tekst bez znaków schodzących zostawia dolne ~4 px puste, więc farba leży nisko w pudełku. Wyrównanie pudełek zostawia widoczny rozjazd | dwie rundy odbioru na jednej poprawce (§19.19) |
| **19** | ⭐ **Pomiar po NOŚNIKU nie odróżnia roli od stanu.** Inwentarz zliczał `SvgIcon` po tokenie, więc glif STANU trafił do tabeli AKCJI — trzy wiersze §8.2 nie przetrwały dokładniejszego sprawdzenia | K5 spadł do zera miejsc, K3 z 3 do 1 (§19.17.2) |
| **20** | ⭐ **Zakres wcześniejszego pomiaru trzeba przeczytać, zanim się go użyje jako odpowiedzi.** Stanowcze „⛔⛔ NIE PRÓBUJ" z §19.8 dotyczyło *innego pytania* (runy między sobą), a wyglądało na zamknięcie tematu | o mały włos odesłanie użytkownika z cytatem zamiast poprawki (§19.19.1) |
| **21** | ⚠ **Nieaktualny komentarz uczy nieprawdy dokładnie tak jak nieaktualny string.** Legenda „Warning=delete" w `Colors.axaml` przeżyła zmianę, którą opisywała, i **wygenerowała cały dryf** naprawiany w K2 | kształt gotchy #284, tylko w komentarzu (§19.16.2) |

#### §19.20.4 ⭐ Decyzje architektoniczne, które przeżyją ten etap

1. ⭐⭐ **Rola dostaje WŁASNĄ WARTOŚĆ, nigdy aliasu** — nawet jeśli dziś jest identyczna z inną
   (`ActionRunColor` vs `SuccessIconColor`, K1). Alias re‑sprzęga to, co właśnie rozsprzęgnięto, i robi
   to **po cichu**. Konsekwencja dla testów: **wartość, która ma się rozejść, nie jest asercją — jest
   pomiarem z datą.**
2. ⭐ **`UseLayoutRounding="False"` jest narzędziem dla elementu, który JEST swoją farbą** (koło, tło).
   Dla elementu z tekstem w środku trzeba patrzeć na farbę, nie na pudełko (§19.19.4).
3. ⭐ **Korekta optyczna idzie przez `RenderTransform`, nie przez margines** — nie rusza układu, więc
   nie wchodzi w interakcję z zaokrąglaniem i nie przesuwa sąsiadów.
4. ⭐ **Kolejność „wartości przed odwołaniami"** przy przenoszeniu roli na własny token (§7.2 → K7):
   nadanie wartości per motyw PRZED podmianą konsumentów sprawiło, że krok wyszedł **neutralny
   wizualnie** zamiast wnieść regres kontrastu.

#### §19.20.5 ⏸ Co ten pod‑etap ZOSTAWIA otwarte

⭐ **Nic z języka kolorów.** Otwarte pozostają wyłącznie rzeczy, które nigdy do niego nie należały:

* **DC** — likwidacja `AccentIconBrush` / `InfoIconBrush` → **M4.3/M5**. ⚠ Oba tokeny **mają
  konsumentów** i nie są sierotami (chip debuggera, żarówka Quick Fix, `DebuggerIcon`, Comment).
* **§13.3** — brama jakości po M3, gdzie cztery powierzchnie ogląda się **jednocześnie**. ⭐ Po R17
  jest to krok tym ważniejszy: przegląd domykający języka pokazał, że **dwie pozostałości stały się
  rozstrzygalne dopiero, gdy patrzyło się na cały pasek naraz**.
* **Rejestr kolizji K1–K11** (§18.R) — nadal czeka na §13.3, bez zmian.

---

### §19.21 Iteracja 12 (M3.2d) — M‑1: literały tooltipów → `UiStrings` (2026-08-03)

> **Zakres zamknięty, zero zmian wizualnych, jedna iteracja** — zgodnie z **R15** (wielkość iteracji idzie
> za niepewnością; tutaj niepewności nie ma). Krok czysto porządkowy: teksty są **niezmienione co do bajtu**,
> zmienia się wyłącznie ich miejsce zamieszkania.

#### §19.21.1 Pomiar wejściowy — audyt zgadza się co do jednego

Audyt (§205, pozycja **M‑1**) mówił o **13 literałach angielskich w `ToolTip.Tip`**, a §19.0.7 rozbił je na
podetapy. Przeliczone ponownie przed pierwszą linią kodu, maszynowo, po **całym** `src/EmberTern.App`:

| Gdzie | Ile | Podetap | Stan po iteracji |
|---|---|---|---|
| `MainWindow.axaml` — toolbar połączeń (New / Edit / Copy / Delete / Connect / Disconnect / Reconnect) | 7 | **M3.2d** | ✅ zdjęte |
| `MainWindow.axaml` — przyciski okna (Minimize / Maximize-Restore / Close) | 3 | **M3.2d** | ✅ zdjęte |
| `MainWindow.axaml:862` — „Close tab" (× na zakładce) | 1 | **M3.3** | ⏸ zostaje |
| `PerformancePanelView:277`, `SessionManagerTabView:214` | 2 | ⛔ **poza M3** (M4.3) | ⏸ zostają |

**13 → 3.** Rozkład z §19.0.7 potwierdzony bez poprawki.

#### §19.21.2 ⭐ Dlaczego to WŁASNE stałe `*Tooltip`, a nie reuse istniejących etykiet

W `UiStrings` stoją już `ConnectionConnect = "Connect"`, `ConnectionDisconnect = "Disconnect"`,
`ConnectionNew`, `ConnectionEdit`, `ConnectionDelete` — teksty **identyczne albo bliskie**. Pokusa reuse'u
jest realna i została **odrzucona świadomie**:

* **Etykieta i tooltip odpowiadają na różne pytania** i mogą się rozejść przy pierwszym przeredagowaniu
  (etykieta jest nazwą polecenia, tooltip może opisywać skutek). Jedna stała nie obsłuży obu.
* ⭐ **Odwrotność tego błędu jest już zapisana w projekcie jako defekt:** UX Consistency Pass (Keyboard
  Manager) znalazł **siedem pozycji menu, których `Header` czytał stałą tooltipową** (finding **D6**) — tak
  „Add item" trafiło do menu jako pozycja. Reuse w drugą stronę ma dokładnie ten sam kształt.
* Projekt już stosuje ten podział przy identycznym tekście: `FolderNewTooltip` = `FolderDialogTitle` =
  *„New folder"*, a to **trzy różne stałe**.

#### §19.21.3 ⚠ Żaden z tych tooltipów nie dostaje gestu — i to jest reguła, nie przeoczenie

Sprawdzone w `CommandCatalog`: **żaden** z dziesięciu przycisków nie ma `CommandId`. Trzy z operacji są
osiągalne z klawiatury, ale przez komendy o zasięgu **`Tree`** (`F3` Nowy · `F4` Odśwież · `F8` Usuń), a
`keyboard-manager.md` §14 ratyfikuje: **gest pokazujemy tylko tam, gdzie działa** — tooltip przycisku
toolbara obiecujący `F3` uczyłby nieprawdy poza drzewem. Stałe są więc zwykłymi `const`, nie
`CommandTip.For`, i przechodzą `UiStringsShortcutSourceTests` bez wpisu w allowliście (nie zawierają
tekstu w kształcie gestu).

⚠ **`{x:Static}` jest sprawdzane przy kompilacji** — literówka w nazwie składowej to błąd builda, nie cicha
awaria. To odwrotność pułapki §9.1/1 (`{DynamicResource}` nie rzuca przy brakującym kluczu), i warto o tym
pamiętać: te dwie składnie mają **przeciwne** tryby porażki.

#### §19.21.4 ⚠ Znalezisko poboczne — ZMIERZONE I ZAPISANE, ŚWIADOMIE NIE NAPRAWIONE

Przy szukaniu miejsca dla nowych stałych wyszło, że **sześć istniejących stałych `UiStrings` nie ma ani
jednego konsumenta** w `src/` ani `tests/`:

`ConnectionConnect` · `ConnectionDisconnect` · `ConnectionDelete` · `ConnectionNew` ·
`ConnectionsEmptyHint` · `TabCloseTooltip`

⭐ **`TabCloseTooltip = "Close tab"` jest tu najciekawszy:** stała dla literału z linii 862 **już istnieje**,
tylko widok jej nie używa. Czyli pozycja M‑1 przypisana do M3.3 to prawdopodobnie **jedna podmiana bez nowej
stałej**, a nie pełna robota — do sprawdzenia w M3.3a.

⛔ **Nic z tego nie zostało usunięte.** Zakres M3.2d to *literały w XAML*, a nie *sieroty w `UiStrings`*;
kasowanie sześciu stałych to osobna decyzja (reguła §7/12: mierz, opisz, zapisz — **nie rozwiązuj bez
decyzji**). Naturalne miejsce: przegląd §13.3 albo M4.3.

#### §19.21.5 Wynik

| | |
|---|---|
| Literały `ToolTip.Tip` | **13 → 3** (pozostałe: 1 × M3.3, 2 × M4.3) |
| Nowe stałe | **10** — 7 × `Connection*Tooltip`, 3 × `Window*Tooltip` |
| Zmiana wizualna | **żadna** — teksty niezmienione co do bajtu |
| Build | 0 błędów / 0 ostrzeżeń |
| Testy | **7228** zielone (7118 + 56 + 54) — **bez zmiany licznika**, co jest właściwym wynikiem dla kroku porządkowego |
| Smoke | czysty |

⚠ **Korekta licznika w dokumentach startowych:** prompt startowy M3.2d podawał **7138 (7032 + 52 + 54)**.
To wartość sprzed **rundy poprawek odbiorczych** (§21, commit `85c8747`), która dołożyła 90 testów. Stan
faktyczny przed tą iteracją i po niej: **7228 (7118 + 56 + 54)**. Ta sama pułapka co zawsze — **liczba
trzymana w prozie starzeje się po cichu**; mierz przed cytowaniem.

---

### §19.22 Iteracja 13 (M3.3a) — domknięcie długu paska zakładek (2026-08-03)

> ⭐⭐ **Iteracja PRZESKALOWANA przez użytkownika przed startem** — i to jest jej pierwsza treść.
> Pomiar pokazał, że zakres z planu (*„geometria, `Size.Row.Tab`, wskaźnik"*) **był już dostarczony**;
> użytkownik odrzucił robienie „etapu dla etapu": *„Jeżeli M3.1a faktycznie dostarczyło geometrię M3.3a,
> to nie cofajmy się do planu tylko dlatego, że plan jest nieaktualny."*

#### §19.22.1 ⭐⭐ Plan był nieaktualny, bo M3.1a wciągnęła geometrię paska do rytmu chromy

| Pozycja planu M3.3a | Gdzie faktycznie stoi |
|---|---|
| `Size.Row.Tab` (26) | ✅ `MinHeight` kafelka — **M3.1a**, ratyfikowane przez użytkownika |
| `Size.TabIndicator` (2) | ✅ `Height` wskaźnika — **M3.1a** |
| Wskaźnik, oba stany | ✅ settery w `ControlStyles.axaml` — **§19.2** |
| Podłoga `Size.ActionMinWidth` | ✅ zdjęta regułą kontenera — **M3.1a** |

⚠ **To pułapka 20 w czystej postaci.** Wiersz planu pisano *przed* M3.1a; wcześniejszy zapis był prawdziwy,
tylko odpowiadał na pytanie sprzed dwóch tygodni. ⭐ Wniosek metodyczny, szerszy niż ta iteracja:
**plan etapu też się starzeje — i starzeje się dokładnie tak samo cicho jak string i jak komentarz**
(#284, pułapka 21). Przed każdym podetapem M3 sprawdź w KODZIE, czy jego przedmiot jeszcze istnieje.

#### §19.22.2 ⚠ K9 i K10 nie dotyczą tego paska — to inny system zakładek

Handover zapowiadał, że *„M3.3 zachowuje wartości lokalne, w szczególności K9 i K10"*. Zmierzone: oba wpisy
stoją na **`TabItem`** (`Style Selector="TabItem"` `FontSize=13` oraz `CornerRadius=4` na `.bottom-tab`/
`.sub-tab`) — czyli na **dolnym panelu i pod‑zakładkach edytorów**. Pasek zakładek dokumentów to
`ItemsControl` + `WrapPanel` + szablon `Border`/`Button`: **nie ma tam ani `TabItem`, ani 13 px** (etykieta
stoi na `Text.Compact.Size` = 11), **ani żadnego `CornerRadius`**.

⭐ Instrukcja „K9/K10 zostają" obowiązuje dalej, ale z **innego powodu**: nie są w przedmiocie tej iteracji.
⚠ To ta sama pomyłka rodzaju co pułapka 19 — **nazwa „zakładka" jest nośnikiem dwóch różnych rzeczy**,
a rejestr indeksował po nazwie.

#### §19.22.3 ⭐⭐ NAJWAŻNIEJSZE: przeniesienie stylu ODTWORZYŁO REGRESJĘ §19.2 — i test ją złapał

Przeniesienie dwóch reguł `active-tab` z `Border.Styles` do `ControlStyles.axaml` wyglądało na czystą
przeprowadzkę. **Nie było.** Kafelek zakładki niósł

```xml
Background="{DynamicResource PanelBrush}"   <!-- WARTOŚĆ LOKALNA -->
```

a przeniesiony setter `Border.active-tab { Background = BackgroundBrush }` **przegrywał z nią**. Nowy test
zawiódł natychmiast i konkretnie:

```
Assert.Equal() Failure: Values differ
Expected: #ff1e1e1e      (BackgroundBrush — tło dokumentu)
Actual:   #ff252526      (PanelBrush — tło panelu)
```

⭐ **To jest §19.2 popełniona drugi raz, jeden poziom wyżej** — tam padł wskaźnik, tu padłaby podmiana tła
kafelka. I znów byłoby to **bezgłośne**: build zielony, suite zielona, smoke czysty.
⚠⚠ **Sama zmiana miejsca reguły jest zmianą jej priorytetu.** Reguła w `Border.Styles` szablonu i ta sama
reguła w globalnym arkuszu **nie są tym samym** wobec wartości lokalnej na tym samym elemencie. „Przeniosłem
styl bez zmian" jest zdaniem, którego **nie wolno powiedzieć bez pomiaru**.

**Poprawka to recepta, którą §19.2 już ustaliła: oba stany jako setter** — plus kotwica na klasie komponentu:

```xml
<Style Selector="Border.workspace-tab">              <!-- spoczynek -->
  <Setter Property="Background" Value="{DynamicResource PanelBrush}" />
</Style>
<Style Selector="Border.workspace-tab.active-tab">   <!-- aktywna -->
  <Setter Property="Background" Value="{DynamicResource BackgroundBrush}" />
</Style>
```

⚠ **Kotwica `workspace-tab` nie jest ozdobą.** Bez niej reguła SPOCZYNKOWA (`Background = PanelBrush`)
trafiłaby w **każdy `Border` aplikacji** — czyli stan spoczynkowy zakładki przemalowałby pół produktu.
⭐ Ogólniej: **klasa stanu mówi JAKI stan, klasa komponentu mówi CZEGO** — a przy przenoszeniu reguły do
zasięgu globalnego brak tej drugiej przestaje być stylistyką i staje się defektem.

#### §19.22.4 Co się zmieniło w kodzie

**Neutralne wizualnie — migracja na role:**

| Element | Było | Jest | Δ |
|---|---|---|---|
| ikony zakładki (×2) | `14×14` | `Size.Icon` | 0 |
| ikona zamknięcia | `12×12` | `Size.Icon.Sm` | 0 |
| odstęp ikona ↔ etykieta | `Spacing="6"` | `Space.Sm` | 0 |
| tooltip zamknięcia | `"Close tab"` | `UiStrings.TabCloseTooltip` | 0 |

⭐ **Zakładka jest PIERWSZYM konsumentem roli `Size.Icon`** — a komentarz tej roli w `Tokens.axaml` wymienia
zakładkę **wprost** jako jej miejsce.

**Usunięte jako rzeczywiście redundantne:** `Background="Transparent"` na obu przyciskach (daje je już
`Button.flat` / `Button.icon`).

**Przeniesione do `ControlStyles.axaml`:** `BorderThickness="0"` przycisku aktywującego → trzeci setter
istniejącej reguły kontenera `Border.tab-strip Button.flat` · komplet reguł zakładki aktywnej (tło kafelka,
waga + kontrast etykiety) → obok wskaźnika, który już tam był.

⚠⚠ **`BorderThickness="0"` przycisku ZAMYKANIA zostaje i NIE jest redundantne** — `Button.icon`, w odróżnieniu
od `Button.flat`, **w ogóle nie ustawia `BorderThickness`**. W spoczynku byłoby to niewidoczne
(`BorderBrush="Transparent"`), ale `Button.icon:focus` podmienia pędzel na `FocusBorderBrush`, więc zdjęcie
tej wartości dotknęłoby **stanu fokusu** — a tego iteracja nie mierzyła (**R2**: komponent ocenia się
w komplecie stanów). Zostaje na §13.3 razem z pytaniem o widoczność fokusu na ikonach chromy.

**Zostawione świadomie:** K12/K13/K14 (§18.R) — paddingi i margines, bo zmieniają **gęstość paska**.
Decyzja użytkownika: *„Nie chcę teraz ruszać paddingów i marginesów zakładek. […] Wrócimy do tego przy
bramie §13.3, kiedy będziemy oglądać cały pasek jako całość."*

#### §19.22.5 ⚠ Trzy znaleziska katalogowe — ZAPISANE, nie naprawione

| Rola | Konsumenci | Uwaga |
|---|---|---|
| `Pad.Tab` (10,4) | **0** | jedynym możliwym konsumentem jest ten pasek → K12 |
| `Size.Icon` (14) | **0 → 1** | ⚠ literał `14` występuje w aplikacji **64 ×** |
| `Size.Icon.Lg` (16) | **0** | ⚠ literał `16` występuje **15 ×** |

⭐ To **czwarty i piąty raz**, gdy pomiar obala zapis w `Tokens.axaml` (kształt §3.1: *katalog bywa zamiarem,
nie opisem*). ⛔ **Sweep 64 + 15 literałów jest robotą app‑wide, nie sprawą M3.3a** — idzie do §13.3/M4.3
razem z resztą rejestru. Zakładka bierze rolę, bo komentarz roli ją wymienia; nic poza nią nie tknięto.

#### §19.22.6 Sonda wizualna — poprawiona, bo inaczej potwierdzałaby stan, którego nie ma

`TabStripVisualProbe` wiązała tło kafelka **wprost** (`active ? BackgroundBrush : PanelBrush`) i dodawała
własny styl instancyjny wskaźnika — to była wierna rekonstrukcja **starego** szablonu. Po tej iteracji byłaby
**dokładnie tym błędem, który §19.2 opisała**: obraz poprawny niezależnie od tego, czy styl w produkcie
działa. ⭐ Sonda nie ustawia już **niczego**, co w produkcie pochodzi ze stylu — dostaje wyłącznie klasy
`workspace-tab` / `active-tab` i resztę robi arkusz aplikacji. Rendery w obu motywach: akcent, podniesione
tło i SemiBold na miejscu.

#### §19.22.7 Wynik

| | |
|---|---|
| Wartości lokalne w szablonie zakładki | **12 → 5** (4 na role · 2 usunięte jako redundantne · 3 przeniesione do stylów · **3 zostają w rejestrze** + 1 z powodem R2) |
| Zmiana wizualna | **żadna** — potwierdzone renderami w obu motywach |
| Build | 0 / 0 |
| Testy | **7229** (7118 + **57** + 54), +1 |
| Smoke | czysty |

⭐ **Nowy test `ActiveTab_SwapsItsBackground_AndBoldensItsLabel_WithoutAnyLocalValueInTheTemplate`
zweryfikowany tak, jak wymaga tego projekt — zawiedzeniem przed poprawką**, i to nie z planowanego
podłożenia naruszenia, tylko dlatego, że **naruszenie było prawdziwe**. Test zawiera też asercję
`Assert.NotEqual(panel, document)`: bez niej przechodziłby przy tożsamych tokenach niezależnie od tego, czy
styl zadziałał — **R16**, test zielony przy złym wyglądzie jest gorszy niż brak testu.

---

### §19.23 Iteracja 14 (M3.3b) — dwa tryby paska zakładek + dwie preferencje (2026-08-03)

> ⭐ Pierwsza iteracja M3, która **dodaje funkcjonalność**, a nie tylko zmienia wygląd (§2.1). Jedna zamknięta
> iteracja, zgodnie z **R15** — architektura była ustalona (§8.2, decyzje D5–D8), więc niepewności nie było.

#### §19.23.1 ⭐⭐ Dwa tryby, JEDEN mechanizm — i to nie jest sztuczka, tylko własność `WrapPanela`

Najważniejsza decyzja implementacyjna: w pasku jest **jeden `ItemsControl` i jeden szablon zakładki**, a o
trybie rozstrzygają **wyłącznie kierunki przewijania `ScrollViewera`**. Działa, bo `WrapPanel` zawija się
dokładnie wtedy, gdy dostanie **skończoną** szerokość:

| Tryb | Poziomo | Pionowo | Skutek |
|---|---|---|---|
| **MultiRow** (domyślny) | `Disabled` | `Auto` + `MaxHeight` | szerokość ograniczona ⇒ **zawija**; przewija się SAM PASEK |
| **SingleRow** | `Auto` | `Disabled` | szerokość nieskończona ⇒ **nie zawija nigdy** ⇒ jeden wiersz |

⛔ **Nie rozbijać tego na dwa `ItemsControl`e z osobnymi panelami.** Szablon zakładki ma ~60 linii i niesie
ikonę, etykietę, wskaźnik i przycisk zamykania; zduplikowany, od tej chwili mógłby się rozjechać między
trybami — a rozjazd byłby widoczny dopiero po przełączeniu preferencji, czyli rzadko.

⚠ **`MaxHeight` liczy code-behind, nie XAML**, bo jest **iloczynem roli i preferencji**
(`Size.Row.Tab` × wiersze), a `{DynamicResource}` nie mnoży. ⛔ Nie zakładać trzeciej warstwy katalogu
z gotowymi wysokościami — byłaby drugą reprezentacją tej samej liczby (§19.1.4 rozstrzygnęło to samo
pytanie dla `GridLength`).

#### §19.23.2 ⭐⭐ Licznik przepełnienia liczy ZAKŁADKI NIEWIDOCZNE — ratyfikowane

> **Użytkownik (2026-08-03):** *„Licznik przepełnienia ma pokazywać liczbę zakładek niewidocznych, nie
> całkowitą liczbę otwartych. To jest informacja, której użytkownik potrzebuje w danym momencie."*

⭐ To jest różnica między liczbą, którą **widać wzrokiem**, a jedyną, której **nie widać znikąd**. Ile
dokumentów jest otwartych, mówi sam pasek; ile ich zostało za prawą krawędzią — nic.

⚠ **Dlatego liczy się ją z RZECZYWISTEGO UKŁADU, nie z modelu** (`UpdateTabOverflow`): dla każdego kontenera
sprawdzane jest, czy mieści się w widocznym obszarze `ScrollViewera`. *„Nie mieści się"* jest faktem
o widoku, nie o kolekcji — VM nie ma jak go znać i nie powinien.
⚠ **Zakładka przycięta w połowie liczy się jako niewidoczna**, bo jest tak samo nieczytelna jak ta całkiem
za krawędzią.
⚠ **Zapisane ryzyko:** `ItemsControl` nad `WrapPanelem` **nie wirtualizuje**, więc wszystkie kontenery
istnieją i pomiar jest zupełny. Gdyby kiedykolwiek zaczął wirtualizować, licznik po cichu zrobi się **za
niski** — komentarz przy metodzie mówi to wprost, bo objaw byłby cichy.

⭐ **Reuse before create:** filtrowaną listę niesie **istniejący `SearchableComboBox`** (§8.2 wskazuje go
wprost), a licznik jedzie w jego `SelectionBoxText`. Zero nowej chromy, zero nowego stylu.
⚠ Wybór z listy **natychmiast czyści zaznaczenie**: to jest **wyszukiwarka, nie stan** — zostawione
zaznaczenie uniemożliwiłoby ponowne wybranie tej samej zakładki.

#### §19.23.3 Preferencje — addytywnie, `CurrentSchemaVersion` bez zmian

| Preferencja | Typ | Zakres / wartości | Domyślnie |
|---|---|---|---|
| `TabStripMode` | string | `MultiRow` \| `SingleRow` | **MultiRow** |
| `TabStripMaxRows` | int | `PreferenceRange` **1–10** | **3** |

⚠ **`UserSettings.CurrentSchemaVersion` ZOSTAJE 2** (R‑4) — dodanie właściwości jest addytywne; bump
uruchomiłby downgrade protection i starsze buildy odrzuciłyby **cały** plik ustawień.
⭐ Obie **jadą w eksporcie `.etsettings` za darmo**: są właściwościami `Preferences`, którą sekcja
`Preferences` niesie w całości. Żadna wersja formatu się nie rusza.

⚠ **Minimum to 1, a nie 2, i to jest decyzja.** Jeden wiersz trybu WIELOWIERSZOWEGO to **nie to samo** co
tryb `SingleRow`: nadal zawija i przewija się pionowo, więc nadal **nic nie chowa się za menu** — a
`SingleRow` przewija w bok i przenosi resztę do listy. Użytkownik, który chce *„jeden wiersz, nic ukrytego"*,
musi móc to powiedzieć.
⚠ **`TabStripMaxRows` przeżywa przełączenie trybu tam i z powrotem** — kuszące *„wyzeruj limit, gdy
przestaje obowiązywać"* wyglądałoby na porządek, a użytkownikowi czytałoby się jako utrata ustawień.
Zapięte testem.

#### §19.23.4 ⭐ Własna kategoria „Tabs" w Settings Center — decyzja użytkownika

> *„Zakładki to już osobna powierzchnia aplikacji, nie chcę dalej rozrastać kategorii General. Dodatkowo
> dobrze współgra to z przyszłym skrótem «Ustawienia zakładek…» z D9."*

Szósta kategoria, między Grid a Debugger. ⚠ Wiersz **Maximum rows** zostaje **widoczny i aktywny również
w trybie jednowierszowym** — wartość jest zachowywana, więc wyszarzenie sugerowałoby, że liczba przepadła;
opis wiersza mówi, którego układu dotyczy.

#### §19.23.5 ⚠ Dwa strażniki zadziałały — i to jest ich cała wartość

Iteracja **nie przeszła** za pierwszym razem, w dwóch miejscach, oba zaprojektowane właśnie na to:

| Strażnik | Co powiedział |
|---|---|
| `SettingsCenterVmTests.EveryCategory_HasAPageVisibilityProperty` | 6 kategorii, 5 właściwości widoczności ⇒ wybranie „Tabs" zostawiłoby **pustą prawą stronę** |
| `FormatterStylePreferenceTests.EveryPreference_IsRenderedOrRecordedAsHidden` | nowa preferencja **bez wiersza i bez zapisanego powodu** |

⭐ Oba pochodzą z etapu Settings Center i **oba trafiły dokładnie w to, po co powstały**: *„adding a property
to `Preferences` fails here until the author either gives it a row or records why it has none"*. To jest
dowód, że tamten mechanizm nie był ceremonią.

#### §19.23.6 ⚠ Sonda znowu pokazywała stan, którego nie było — trzeci raz ten sam kształt

Pierwszy render trybu jednowierszowego wyszedł **poprawny, tylko bez przycisku przepełnienia**. Przyczyna:
sonda ładowała sześć słowników zasobów, a `SearchableComboBox.axaml` **nie było wśród nich** — kontrolka bez
`ControlTheme` nie ma szablonu i renderuje się jako **nic**.

⭐ **Reguła, którą to ustanawia: sonda musi ładować te same słowniki co `App.axaml`.** Brakujący słownik
**nie zawodzi** — po cichu usuwa element z obrazu, a obraz nadal wygląda sensownie.
⚠ To trzecie wystąpienie tego samego kształtu w tym pasku: §19.2 (sonda wiązała tło wprost), §19.22.6 (sonda
dodawała własny styl wskaźnika), teraz brakujący słownik. **Za każdym razem narzędzie zbudowane do oceny
zmiany potwierdzało stan, którego nie było.**

#### §19.23.7 Wynik

| | |
|---|---|
| Nowe preferencje | 2 (`Preferences` 14 → 16); **`CurrentSchemaVersion` bez zmian** |
| Nowa kategoria Settings Center | 1 („Tabs", dwa wiersze) |
| Nowe tryby paska | 2, na **jednym** `ItemsControl` i jednym szablonie |
| Build | 0 / 0 |
| Testy | **7231** (7120 + 57 + 54), +2 |
| Smoke | czysty |
| Rendery sondy | MultiRow (limit 3 wierszy) i SingleRow (licznik `5 ⌄`) w **obu** motywach |

#### §19.23.8 ⚠ Runda odbiorcza — trzy uwagi z realnego użycia, jedna przyczyna dwóch z nich

Użytkownik odebrał **MultiRow** („ten kierunek mi się podoba") i zgłosił trzy rzeczy:

| # | Zgłoszenie | Rozstrzygnięcie |
|---|---|---|
| 1 | **SingleRow: pasek przewijania zasłania zakładki** | ⭐ realne i najpoważniejsze — poprawione strukturalnie |
| 2 | **SingleRow: brak przewijania kółkiem** | nowy handler, tylko dla tego trybu |
| 3 | MultiRow: pasek przy prawej krawędzi „praktycznie znika" | ⭐ **ta sama przyczyna co #1** |

**⭐⭐ #1 i #3 miały JEDNĄ przyczynę: `AllowAutoHide`.** Domyślnie `ScrollViewer` trzyma pasek jako
**cienką kreskę leżącą NA treści**, rozwijaną dopiero pod kursorem. Stąd jednocześnie *„zasłania zakładki"*
(bo leży na nich) i *„praktycznie znika"* (bo jest kreską). `AllowAutoHide = false` odbiera obie własności
naraz — pasek ma stałą grubość, więc **da się pod niego zarezerwować miejsce**, i jest widoczny bez
najeżdżania.

⭐ **Dlaczego rezerwacja musiała być kodem, a nie atrybutem:** szablon `ScrollViewera` w FluentTheme daje
`ScrollContentPresenterowi` `RowSpan`/`ColumnSpan` przez **całą** siatkę, więc paski **zawsze** leżą na
treści — nie istnieje właściwość „zarezerwuj miejsce". Rezerwacją jest `Padding` `ScrollViewera`, bo szablon
przekazuje je prezenterowi: treść wsuwa się do środka, pasek zostaje na wysuniętym marginesie.

⭐⭐ **Grubość jest MIERZONA Z SAMEGO PASKA, nie wpisana.** Nasze motywy nie deklarują szerokości paska (to
liczba FluentTheme), a wpisanie `12` byłoby albo martwym literałem, albo — gorzej — sięgnięciem po
`Space.Lg`, bo **akurat też wynosi 12**. To jest pułapka 6 (*liczba nie wyznacza roli*) w najczystszej
postaci: rola odstępu i grubość cudzej kontrolki to dwie różne rzeczy, które dziś mają tę samą wartość.
⚠ Rezerwacja jest **warunkowa** (R13): dopóki pasek się nie pojawia, marginesu nie ma.

⛔ **Kontrastu kciuka NIE ruszono, choć zgłoszenie mówiło o kolorze.** `ScrollBarThumbColor` jest tokenem
**aplikacji**; podniesienie go dla jednego paska byłoby łataniem pojedynczego ekranu (**R7**), a podniesienie
globalne wyszłoby poza etap. ⭐ Okazało się zbędne: problemem nie był kolor, tylko **stan** kontrolki.
To jest §19.14 od dobrej strony — zgłoszenie o objawie wiarygodne, wniosek o przyczynie zmierzony (pułapka 9).

**#2 — kółko.** `ScrollViewer` przewija kółkiem **pionowo i tylko pionowo**, co w trybie wielowierszowym jest
dokładnie oczekiwane — więc tam nie zmieniono **nic**. W jednowierszowym obrót zamienia się na ruch poziomy.
⚠ Handler wisi na **całym pasku** i na **tunelu**: użytkownik kręci tam, gdzie ma kursor, a `ScrollViewer`
w środku sam obsługuje to zdarzenie, więc bąbelkowy handler dostałby je już oznaczone.
⭐ **Krok to ćwiartka widocznego obszaru, nie stała liczba pikseli ani szerokość zakładki** — zakładki mają
różne szerokości (D6/§8.1 nie skraca nazw), więc „jedna zakładka" nie jest jednostką; ułamek widoku skaluje
się sam i nie potrzebuje tokenu.

⚠ **Sonda po raz czwarty musiała gonić produkt** — bez `AllowAutoHide` i bez rezerwacji renderowałaby stan
**sprzed** poprawki. Odtwarza teraz oba kroki, łącznie z `Measure`/`Arrange` przed pomiarem paska (zerowe
`Bounds` dałyby zerową rezerwację, czyli dokładnie defekt).

#### §19.23.9 ⛔⛔ DRUGA RUNDA ODBIORCZA — poprawka #1 BYŁA BŁĘDNA. Najważniejszy wpis tej iteracji

> **Użytkownik:** *„Pasek przewijania nadal zasłania zakładki — problem nie został rozwiązany. […] Mam
> wrażenie, że próbujesz naprawić objaw zamiast układ. SingleRow powinien wyglądać jak normalny pasek kart
> z przewijaniem, a nie jak osobny panel z dodatkowymi elementami."*

**Diagnoza była trafna.** MultiRow odebrany; SingleRow miał trzy defekty i wszystkie wynikały z jednej
decyzji: próbowałem **zmusić cudzą kontrolkę do czegoś, czego jej szablon nie przewiduje**, zamiast zmienić
układ.

**⚠⚠ DLACZEGO `Padding` NIE MÓGŁ ZADZIAŁAĆ — i dlaczego sonda tego nie złapała.** Rezerwacja `Paddingiem`
tworzy **sprzężenie zwrotne**: padding zmienia viewport → viewport zmienia widoczność paska → pasek zmienia
padding. W **sondzie**, gdzie układ liczy się **RAZ**, wychodziło poprawnie i render był przekonujący.
W aplikacji, gdzie układ przelicza się w pętli, **nie ustalało się**.

⭐⭐ **To jest PIĄTE wystąpienie tego samego kształtu w tym pasku — i pierwsze, w którym sonda nie mogła mieć
racji z zasady, a nie przez pomyłkę w niej.** §19.2 (wiązała tło wprost), §19.22.6 (własny styl wskaźnika),
§19.23.6 (brakujący słownik) były **błędami sondy**. Ten jest inny: sonda renderuje **jeden przebieg układu**,
więc defekt, który ujawnia się dopiero w **pętli przeliczeń**, jest poza jej zasięgiem **z konstrukcji**.
⚠ **Reguła: narzędzie, które liczy raz, nie może orzec o zbieżności.** Zapisane w sondzie, w miejscu.

**⭐ POPRAWKA JEST STRUKTURALNA.** Pasek przewijania przestaje być paskiem `ScrollViewera` i staje się
**rodzeństwem zakładek w osobnym wierszu siatki**:

```
Border.tab-strip
  Grid RowDefinitions="*,Auto"
    ScrollViewer  (wiersz 0)  HorizontalScrollBarVisibility = Hidden
    ScrollBar     (wiersz 1)  Orientation = Horizontal
```

`Hidden`, a nie `Auto` — treść dalej dostaje nieskończoną szerokość (więc `WrapPanel` nie zawija), ale
własny pasek `ScrollViewera` się nie pokazuje. **Rodzeństwo w siatce nie ma jak nachodzić na sąsiada — to
własność konstrukcji, nie dobranej liczby.**

⭐⭐ **I dokładnie ten warunek gwarantuje brak nawrotu pętli:** widoczność paska zależy od rozpiętości
**POZIOMEJ**, a jego pojawienie się zmienia wyłącznie wymiar **PIONOWY** (zabiera wysokość wierszowi 0).
Te dwie wielkości są **ortogonalne**, więc sprzężenie nie ma jak powstać. ⛔ Nie wiązać widoczności tego
paska z niczym, na co on sam wpływa.

**⏸ PRZYCISK/LICZNIK PRZEPEŁNIENIA — ODŁOŻONY PRZEZ UŻYTKOWNIKA, nie porzucony.** Wersja na
`SearchableComboBox` była wizualnie wadliwa: rozwinięta lista źle wypozycjonowana, wiersze renderowane przez
`ToString()` zamiast `DisplayTitle` (⚠ zmierzona przyczyna: ustawiłem `DisplayMemberPath`, ale lista popupu
renderuje przez `ItemTemplate`, którego nie podałem), a całość „doklejona" do paska. ⭐ Głębszy błąd był
jednak inny i to jego nazwał użytkownik: **mieszałem układ paska z dodatkowym elementem.** Najpierw SingleRow
ma być normalnym paskiem kart, dopiero potem wraca przepełnienie. Stałe `TabStripOverflow*` w `UiStrings`
zostają — wracają razem z nim (§8.2 nadal go wymaga).

⛔ **Kontrastu kciuka nadal NIE ruszono** i nadal okazało się to zbędne: `AllowAutoHide = false` załatwiło
uwagę o MultiRow, bo problemem był **stan** kontrolki, nie jej barwa (R7 — token jest aplikacji, nie tego
paska).

⏸ **Do obejrzenia na żywej bazie:** przełączanie trybu przy otwartych zakładkach, przewijanie kółkiem
w obu trybach. ⚠⚠ **Sonda pokazuje układ po jednym przebiegu — nie orzeka o zachowaniu w pętli i tej rundy
by nie wychwyciła.** Ocena SingleRow musi zapaść w działającej aplikacji.

✅ **ODEBRANE 2026-08-03:** *„Wygląda już dobrze, to jest kierunek, o jaki mi chodziło."*

#### §19.23.10 ⭐ Trzecia runda — wiersz „Maximum rows" znika w trybie jednowierszowym

> **Użytkownik:** *„Gdy wybrany jest tryb Single row, opcja Maximum rows nie ma żadnego zastosowania, więc
> powinna być automatycznie ukrywana. Dzięki temu interfejs nie pokazuje ustawień, które w danym trybie nic
> nie robią."*

⭐⭐ **To UCHYLA decyzję §19.23.3 tej samej iteracji.** M3.3b świadomie zostawiło wiersz widoczny, argumentując,
że wartość przeżywa przełączenie trybu, więc ukrycie sugerowałoby jej utratę. **Reguła użytkownika jest
prostsza i lepsza:** interfejs nie pokazuje ustawień, które w danym trybie nic nie robią. ⚠ Mój argument
mylił **ukrycie wiersza** z **porzuceniem wartości** — a to dwie różne rzeczy: znika wiersz, nie liczba.

⚠ **Komentarz w `SettingsCatalog.cs` mówił dokładnie odwrotność i został poprawiony w tym samym kroku** —
inaczej byłaby to pułapka 21 (nieaktualny komentarz uczy nieprawdy tak samo jak nieaktualny string), i to
w wydaniu najgorszym z możliwych: uzasadniałby zachowanie, którego już nie ma.

⭐ **Warunek jest KONIUNKCJĄ dwóch niezależnych przyczyn** (`ShowTabStripMaxRows` = tryb ∧ filtr) i mieszka
na stronie, a nie w wierszu. ⛔ Wpisanie odpowiedzi trybu w `IsVisible` wiersza byłoby błędem: `IsVisible`
należy do **wyszukiwarki**, więc szukanie frazy „rows" wskrzeszałoby wiersz, który nie ma zastosowania —
albo przełączenie trybu wskrzeszałoby wiersz odfiltrowany. **Dwa powody ukrycia, żaden nie nadpisuje
drugiego** — zapięte osobnym testem.

⚠⚠ **Test na samą WARTOŚĆ właściwości nie wystarczał i to jest tu najważniejsza część.** Czytana wprost
`ShowTabStripMaxRows` jest poprawna nawet wtedy, gdy nic o niej nie ogłasza, a wiązanie odpytuje ją
**wyłącznie po `PropertyChanged`** — czyli test byłby zielony przy niedziałającym ekranie (**R16**). To ta
sama luka, przez którą w §19.2 poprawny styl nigdy się nie namalował: mechanizm dobry, brakowało sygnału.
⭐ Notyfikacja jest więc asercją, a nie założeniem — **zweryfikowaną podłożeniem naruszenia**: bez
`OnPropertyChanged` w `Commit` test zawodzi z komunikatem *„Zmiana trybu nie ogłosiła ShowTabStripMaxRows —
widok nie odpyta."*

⭐ Drugi test przechodzi **pełny obieg** ustaw → przełącz → wróć i sprawdza wartość **na dysku**: bez tego
„zachowana" znaczyłoby wyłącznie „do zamknięcia okna".

**Stan po trzech rundach:** build 0/0 · **7233** (7122 + 57 + 54, +4) · smoke czysty.

---

### §19.24 Iteracja 15 (M3.3c) — menu kontekstowe zakładki (2026-08-03)

> ⚠⚠ **Podetap objęty regułą #11 najmocniej z całego M3:** trzy pozycje zamykają wiele dokumentów
> jednym kliknięciem. Zamknięcie M3.3 i całego paska zakładek.

#### §19.24.1 ⭐⭐ Bramka dostała ZASIĘG — i to była jedyna zmiana, jakiej potrzebowała

`CollectUnsavedWork`, `HasSavableDirtyEditors` i `SaveDirtyEditorsAsync` iterowały po **wszystkich**
zakładkach, bo trzy dotychczasowe wejścia (zamknięcie zakładki, rozłączenie, zamknięcie aplikacji) zawsze
dotyczą całości. **„Zamknij zakładki po prawej" dotyczy PODZBIORU.** Bez zasięgu czwarte wejście musiałoby
albo **ominąć bramkę**, albo **pytać o pracę w zakładkach, których nie zamyka** — pierwsze jest utratą
danych, drugie kłamstwem.

⭐ **`scope == null` znaczy „wszystkie"**, więc trzy istniejące wejścia są nietknięte — ich kodu nie
trzeba było dotykać, a ich 26 testów przeszło bez zmiany. ⛔ Nie budować drugiej ścieżki „zapisz wiele
zakładek"; ta jest przetestowana od sprintu Save-and-close.

**Bramka jest AGREGUJĄCA, nie „N pytań po kolei"**, i tak być musi: pytanie zadane osiem razy pod rząd nie
jest bramką, tylko przeszkodą, którą użytkownik przeklika bez czytania. Ten sam kształt ma rozłączenie
i zamknięcie aplikacji. ⚠ Komunikat **wymienia zakładki z pracą** — *„kilka zakładek ma niezapisane
zmiany"* nie pozwala podjąć decyzji, a to jest moment, w którym użytkownik ją podejmuje.

⚠ **Po „Zapisz" nie ufamy zgłoszonemu sukcesowi** (lekcja z `RequestCloseTabAsync`): nieudany zapis nie
zamyka **niczego**. Częściowe zamknięcie po nieudanym zapisie byłoby najgorszym wynikiem — część pracy
przepadła, a operacja zgłosiła sukces.

#### §19.24.2 ⚠ Menu czyta ZAKŁADKĘ, nie zaznaczenie

Każda komenda bierze zakładkę **parametrem**. Menu kontekstowe otwiera się nad zakładką, która **nie musi
być aktywna**; czytanie `SelectedWorkspaceTab` zamykałoby cudzy dokument. ⭐ To ten sam kształt, co gotcha
#16/#99 przy siatkach (prawy przycisk nie zaznacza wiersza), tylko o poziom wyżej — i tam też objawem było
działanie na niewłaściwym obiekcie.

#### §19.24.3 ⭐ Bramkowanie pozycji — zgłoszone przez użytkownika PRZED implementacją

> *„Sprawdź, czy wszystkie pozycje poprawnie włączają się i wyłączają zależnie od kontekstu […], żeby nie
> zostawić martwych lub zawsze aktywnych poleceń."*

| Pozycja | Wyłączona, gdy |
|---|---|
| Zamknij | zakładka niezamykalna |
| Zamknij pozostałe | jest jedyną zamykalną |
| Zamknij po prawej | **jest ostatnia** |
| Zamknij niezmodyfikowane | wszystkie zakładki są brudne |
| Zamknij wszystkie | nie ma zamykalnych |
| Odśwież | zakładka jest brudna **albo jej rodzaj się nie odświeża** |
| Kopiuj nazwę obiektu | zakładka narzędziowa (brak nazwy) |
| Pokaż w Explorerze | brak rodzaju/nazwy albo brak połączenia |

⭐ **`WorkspaceTabViewModel.CanRefresh` to PIĄTY członek rodziny per-kind** (obok `UnsavedWork`,
`SavableEditor`, `RefreshAsync`, `ResolveCommand`). Istnieje, bo `RefreshAsync` ma ramię
`_ => Task.CompletedTask`: wywołanie go na zakładce SQL Editora jest **bezpieczne**, ale pozycja menu, która
jest klikalna i nic nie robi, **uczy, że polecenie nie działa** — trwale.

⚠⚠ **Bramkowanie zależy od SKŁADU kolekcji, a `[RelayCommand]` sam z siebie o jej zmianie nic nie wie.**
Bez przeliczenia „Zamknij po prawej" zostałoby aktywne po zamknięciu ostatniej zakładki. Przeliczenie wisi
w **jednym istniejącym punkcie** (`OnWorkspaceTabsChanged`), a nie przy ~39 miejscach dodających zakładkę —
ten sam wzorzec, co subskrypcja Seam 6d. ⭐ To ta sama luka co przy `ShowTabStripMaxRows` (§19.23.10):
wartość czytana wprost byłaby poprawna, a menu i tak pokazywałoby stan sprzed zmiany. **Zapięte testem na
`CanExecuteChanged`, nie na samej wartości.**

#### §19.24.4 ⭐ „Pokaż w Metadata Explorer" — zaznaczenie NIE WYSTARCZA

> **Użytkownik:** *„Powinno nie tylko zaznaczyć obiekt, ale również przewinąć listę tak, żeby był
> widoczny."*

⚠ Przy dwóch tysiącach obiektów zaznaczony wiersz niemal na pewno leży poza ekranem, a **zaznaczenie,
którego nie widać, jest nieodróżnialne od braku reakcji.**

⭐⭐ **Rozwinięcie kategorii musi być POCZEKANE, a nie tylko zażądane.** Ustawienie `IsExpanded` odpala
`LoadGroupAsync` jako *fire and forget* (`_ = _owner.LoadGroupAsync(this)`), więc szukanie liścia zaraz po
tym trafiłoby w kategorię **bez dzieci** — pozycja menu nie robiłaby nic przy pierwszym użyciu i działała
przy drugim, czyli najgorszy możliwy rodzaj usterki. Dlatego ładowanie jest wywołane wprost i oczekiwane.

⚠ **Podział odpowiedzialności:** VM ustala **wiersz**, widok go **pokazuje** — przewijanie jest sprawą
kontrolki, bo lista wirtualizuje. ⚠ `ScrollIntoView` jest **odłożone na `Background`** (kontener dopiero
co powstał), ale **zaznaczenie ustawiane SYNCHRONICZNIE** — odłożone razem przegrałoby z kolejnym
kliknięciem użytkownika (kształt gotchy #221, ta sama korekta co przy nawigacji po diagnostykach).

#### §19.24.5 Zero nowej chromy

Menu stoi na stylach `ContextMenu`/`MenuItem` + `{app:MenuIcon}` + `{app:CommandGesture}` z etapu 5
Keyboard Managera — **nie powstał ani jeden nowy styl**. „Ustawienia zakładek…" otwiera Settings Center
**prosto na kategorii Tabs** (D8); ⚠ nieznane id kategorii znaczy „zostaw domyślną" — skrót ma zaprowadzić
na stronę, a nie zepsuć okno, gdy kategoria kiedyś zmieni nazwę.

#### §19.24.6 Wynik

| | |
|---|---|
| Pozycje menu | 9 (8 + skrót do ustawień), dwa separatory |
| Wejścia do bramki reguły #11 | **3 → 4** |
| Build | 0 / 0 |
| Testy | **7243** (7132 + 57 + 54), **+10** |
| Smoke | czysty |

⏸ **Do odbioru na żywej bazie:** reveal na dużym schemacie (czy przewija), stany menu na pierwszej/ostatniej
zakładce, bramka przy „Zamknij wszystkie" z kilkoma brudnymi edytorami.

---

### §19.25 🔒 PODSUMOWANIE ZAMYKAJĄCE — M3.3 (pasek zakładek) ODEBRANE (2026-08-03)

> **Użytkownik:** *„Odbieram M3.3 jako zakończone. Wszystko działa zgodnie z założeniami."*
> Trzeci z czterech podetapów M3. Zostaje **M3.4** (Metadata Explorer) → **M3b** → ⛔ brama §13.3.

#### §19.25.1 Co dostarczono

| Podetap | Wynik |
|---|---|
| **M3.2d** | M‑1: 13 literałów `ToolTip.Tip` → **3** (1 spadł na M3.3a, 2 zostają M4.3). Zero zmian wizualnych |
| **M3.3a** | Dług techniczny paska: **12 → 5** wartości lokalnych, komplet reguł zakładki aktywnej w `ControlStyles.axaml`, ostatni literał M‑1 |
| **M3.3b** | **Dwa tryby** (`MultiRow` \| `SingleRow`) + **dwie preferencje**, własna kategoria **Tabs** w Settings Center |
| **M3.3c** | **Menu kontekstowe** — 9 pozycji, zero nowej chromy, **czwarte wejście do bramki reguły #11** |

**Stan:** build 0/0 · suite **7243** (7132 + 57 + 54, **+15** w całym M3.3) · smoke czysty ·
`CurrentSchemaVersion` **bez zmian** (R‑4) · zero nowych stylów.

#### §19.25.2 ⭐⭐ Cztery ustalenia, które przeżyją ten podetap

1. ⭐⭐ **ZMIANA MIEJSCA REGUŁY JEST ZMIANĄ JEJ PRIORYTETU.** Przeniesienie stylu `active-tab`
   z `Border.Styles` do arkusza globalnego odtworzyło regresję §19.2 — lokalne `Background` zaczęło bić
   setter, choć „nic się nie zmieniło". ⛔ *„Przeniosłem styl bez zmian"* to zdanie, którego **nie wolno
   powiedzieć bez pomiaru**. Recepta jest z §19.2: **oba stany jako setter**, plus **kotwica klasy
   komponentu** (`workspace-tab`), bo klasa stanu mówi JAKI stan, a klasa komponentu — CZEGO.
2. ⭐⭐ **NARZĘDZIE, KTÓRE LICZY RAZ, NIE MOŻE ORZEC O ZBIEŻNOŚCI.** Rezerwacja miejsca `Paddingiem`
   wychodziła w sondzie poprawnie, bo sonda renderuje jeden przebieg układu; w aplikacji sprzężenie
   `padding → viewport → widoczność → padding` nie ustalało się nigdy. ⭐ Rozwiązaniem jest **struktura,
   nie liczba**: rodzeństwo w osobnym wierszu siatki nie ma jak nachodzić na sąsiada, a widoczność paska
   zależy od wymiaru **ortogonalnego** do tego, na który pasek wpływa.
3. ⭐⭐ **PLAN ETAPU STARZEJE SIĘ TAK SAMO CICHO JAK STRING I JAK KOMENTARZ.** M3.3a wszedł z zakresem,
   który M3.1a już dostarczyła. ⚠ To pułapka 20 zastosowana do **własnej dokumentacji projektu** —
   i dlatego handover ma teraz wiersz *„przed każdym podetapem sprawdź w KODZIE, czy jego przedmiot jeszcze
   istnieje"*. Ta sama runda pokazała, że **K9/K10 nigdy nie dotyczyły tego paska** (stoją na `TabItem`).
4. ⭐⭐ **TEST NA WARTOŚĆ WŁAŚCIWOŚCI NIE JEST TESTEM NA DZIAŁANIE EKRANU.** Dwa razy w tym podetapie
   (`ShowTabStripMaxRows`, bramkowanie menu) właściwość czytana wprost była poprawna, a wiązanie odpytuje
   ją **wyłącznie po `PropertyChanged`** — czyli test byłby zielony przy niedziałającym UI (**R16**).
   ⭐ Notyfikacja jest **asercją**, w obu miejscach **zweryfikowaną podłożeniem naruszenia**.

#### §19.25.3 ⭐ Trzy decyzje użytkownika, które uchyliły moje

Warto zapisać, bo każda była lepsza od tego, co zaproponowałem:

* **Przeskalowanie M3.3a** — *„nie cofajmy się do planu tylko dlatego, że plan jest nieaktualny"*. Zamiast
  robić etap dla etapu, podetap domknął realny dług.
* **Ukrycie „Maximum rows"** w trybie jednowierszowym. Broniłem widoczności, bo wartość przeżywa
  przełączenie trybu — **myliłem ukrycie wiersza z porzuceniem wartości**.
* **„Najpierw układ, potem przycisk przepełnienia"** — *„próbujesz naprawić objaw zamiast układ"*. Trafna
  diagnoza; poprawka strukturalna wyszła z niej, nie z kolejnej iteracji łatania.

#### §19.25.4 ⏸ Co M3.3 zostawia otwarte

| # | Co | Gdzie |
|---|---|---|
| **K12–K14** | paddingi + margines zakładki = **gęstość paska**; idą na §13.3 **jako jedno pytanie** | brama §13.3 |
| **K9/K10** | ⚠ **nie dotyczą tego paska** — stoją na `TabItem` (dolny panel, pod‑zakładki) | brama §13.3 |
| ⏸ **przycisk/licznik przepełnienia** | odłożony przez użytkownika; §8.2 nadal go wymaga. ⚠ Znana usterka: lista popupu renderuje przez `ItemTemplate`, więc sam `DisplayMemberPath` nie wystarczy | do zaplanowania |
| **sieroty w `UiStrings`** | 6 stałych bez konsumenta (w tym `TabCloseTooltip`, już wykorzystany) | §13.3 / M4.3 |
| **`Pad.Tab`, `Size.Icon.Lg`** | role bez konsumentów; `Size.Icon` (14) ma od M3.3a **jednego**, przy **64** literałach w aplikacji | §13.3 / M4.3 |

---

## §20 INWENTARZ AKCJI I KOLORÓW — pomiar całego produktu (2026-08-02)

> ⭐⭐ **To jest POMIAR, nie projekt.** Powstał na wyraźne polecenie użytkownika po wycofaniu M3.2b:
> *„najpierw zrób pełną inwentaryzację wszystkich akcji w aplikacji — nie według modułów, tylko według
> znaczenia — i pokaż, gdzie dana akcja występuje oraz jakiego koloru używa. Dopiero mając taki obraz
> całości będziemy mogli zaprojektować spójny język kolorów."*
> ⛔ **Nie zawiera projektu języka.** Język powstaje osobno, po akceptacji tego obrazu.

### §20.0 Metoda i jej jedno ograniczenie

Zebrane maszynowo ze **wszystkich** widoków i kontrolek: każde wystąpienie ikony sparowane z jej pędzlem
(lub jego brakiem) **oraz z tooltipem/komendą**. ⚠⚠ **Parowanie z tooltipem jest konieczne, bo IKONA ≠
AKCJA:** `Icon.Play` to Execute SQL, Execute procedury, Start trace **i** Continue w debuggerze — cztery
różne akcje o wspólnym znaku; a `Icon.Trash` i `Icon.ListX` to obie „usuń". Grupowanie po ikonie dałoby
inny — i fałszywy — obraz.

### §20.1 Skala

| | |
|---|---|
| Instancje `SvgIcon` w widokach | **442** |
| z tego niesie `Foreground` | **39** ⇒ ⭐ **91 % ikon aplikacji jest już neutralnych** |
| Pozycje menu kontekstowego (`{app:MenuIcon}`) | **131**, z tego **13** z `Brush=DangerIconBrush` |
| Różnych ikon | 81 |
| Ikon renderowanych w **więcej niż jednym** kolorze | **22** |

⭐ **Menu kontekstowe są osobnym, JUŻ SPÓJNYM systemem** i nie są przedmiotem języka: konwencja z etapu 5
Keyboard Managera mówi „neutralnie, wyjątkiem jest destrukcja" i 131 pozycji jej przestrzega. ⛔ Nie
ujednolicać ich z przyciskami — to inna powierzchnia i inna reguła.

### §20.2 ⭐⭐ AKCJE, KTÓRE MAJĄ RÓŻNY KOLOR W RÓŻNYCH MIEJSCACH — to jest defekt

| Akcja | Kolorów | Rozkład |
|---|---|---|
| **Uruchom** | **4** | `OnAccent` (SQL Editor, Script Executor — na przycisku `primary`) · **`Success`** (procedura, funkcja, Trace Start) · **`AccentIcon`** (debugger Continue) |
| **Usuń trwale** | **3** | `Danger` ×8 (Domain, Exception, Generator, Index, Package, profil importu, Procedure, Function) · **`Warning`** (Usuń połączenie, Usuń zapytanie) · **`Warning`** na `Icon.ListX` (Wyczyść wszystkie zapytania) |
| **Odśwież** | **3** | **`Info`** (metadane w pasku tytułu, dane tabeli) · **`AccentIcon`** (Data Import) · neutralny (Generator, Index, Session Manager) |
| **Edytuj / zmień nazwę** | **3** | **`Warning`** (Procedure, Function) · **`AccentIcon`** (profil importu) · neutralny (Edit Connection, zmiana nazwy zapytania, kolekcje, Table Detail) |
| **Otwórz plik** | **2** | `Accent` (Script Executor) · `AccentIcon` (Data Import) |
| **Zapisz** | **2** | `Accent` (Script Executor, Export DDL) · neutralny (debugger Save, opis obiektu ×6) |
| **Dodaj** | **2** | **`Success`** (Procedure, Function) · neutralny (20 pozostałych) |
| **Szukaj** | **2** | `Accent` (Global Search) · neutralny (7 pól filtrowania) |
| **Rozłącz** | **2** | `Danger` (cudza sesja w Session Managerze) · neutralny (własne połączenie) |

⚠⚠ **Najostrzejszy pojedynczy przypadek to „usuń trwale":** ta sama operacja jest czerwona w ośmiu
miejscach i żółta w dwóch. ⭐ **Użytkownik znalazł ją sam, patrząc na ekran** (żółte przyciski przy Saved
Queries) — czyli defekt jest widoczny bez żadnego narzędzia, a mój wcześniejszy pomiar go nie objął,
bo mierzyłem dwa paski zamiast produktu.

### §20.3 ⭐ AKCJE JUŻ SPÓJNE — wzorce do OPISANIA, nie do zmiany

| Akcja | Kolor | Wystąpienia |
|---|---|---|
| **Zatrzymaj** (`Icon.Stop`) | `DangerIconBrush` | **5/5** — SQL Editor, debugger, Data Import, Script Executor, Trace |
| **Zatwierdź transakcję** (`Icon.Check`) | `SuccessIconBrush` | **5/5** — toolbar, Data Import, Script Executor, Session Manager, walidacja importu |
| **Kompiluj** (`Icon.Hammer`) | `OnAccentBrush` na `Button.primary` | **11/11** — wszystkie edytory obiektów |
| **Rodzaj obiektu** (`IconColor_*`) | 10 tokenów | 10/10 — kreatory + Security Manager |
| **Menu kontekstowe** | neutralnie, destrukcja czerwono | 131 pozycji, 13 wyjątków |

### §20.4 ⚠ RÓŻNICE, KTÓRE SĄ ŚWIADOME — i nie wolno ich „naprawić"

| Para | Różnica | Powód |
|---|---|---|
| **Comment / Uncomment** | `Info` vs `Danger` | ⛔ **Zamówione wcześniej przez użytkownika**: ikony są bardzo podobne, a kolor pozwala je rozpoznać błyskawicznie. M3.2b uznało to za defekt i **to był błąd** (§19.14.4) |
| **Rollback vs Revert** (oba `Icon.Undo`) | `Danger` vs neutralny | to **dwie różne akcje**: wycofanie transakcji jest cięższe niż porzucenie edycji w buforze |
| **Rozłącz cudzą sesję vs własne połączenie** | `Danger` vs neutralny | rozłączenie cudzej sesji dotyka pracy innego użytkownika |
| **Ikona na `Button.primary`** | `OnAccentBrush` | ⭐ **to nie jest kolor semantyczny, tylko kontrast na wypełnieniu akcentem.** Kolor niesie tam WARIANT PRZYCISKU, nie ikona — decyzja architektoniczna 4 |

### §20.5 ⚠ Dwa tokeny o nakładającej się pracy

`WarningBrush` (Icon.Exception w pasku, Pause w Trace) i `WarningIconBrush` (Break on exception,
Edytuj ×2, Usuń połączenie, Usuń zapytanie, Wyczyść wszystkie) — **dwie nazwy na jedną rolę**, używane
zamiennie. Do rozstrzygnięcia razem z językiem, nie osobno.

### §20.6 Co z tego wynika dla projektu języka

1. ⭐⭐ **Problemem nie jest liczba kolorów, tylko ich NIESTAŁOŚĆ.** 91% aplikacji jest neutralne;
   defekt polega na tym, że dziewięć akcji ma po 2–4 kolory zależnie od modułu, w którym stoi.
2. ⭐ **Pięć akcji jest już wzorcowo spójnych** — język ma je opisać i utrwalić, a nie zmieniać.
3. ⚠ **Cztery różnice są świadome** i muszą zostać w języku jako **nazwane wyjątki z powodem**,
   inaczej następna iteracja „naprawi" je ponownie.
4. ⚠ **Kolor przycisku `primary` to inny wymiar niż kolor ikony** i musi być w języku rozdzielony,
   inaczej Execute w SQL Editorze i Execute procedury nigdy nie dadzą się porównać.

---

## §21 Runda poprawek odbiorczych przed M3.2d (2026-08-03) — trzy pozostałości M2b/M2c

⚠ **To NIE jest etap Product Polish** — to zamknięcie zgłoszeń z normalnej pracy z aplikacją, wykonane przed
M3.2d. Trafia tutaj, bo trzy z nich są bezpośrednimi pozostałościami migracji na tokeny i mają ten sam kształt:
**wartość poprawna w jednym kontekście, użyta tam, gdzie jej przesłanka nie obowiązuje.** Pełny opis w komentarzach
przy kodzie; tu jest tylko lista i wniosek.

| Zgłoszenie | Przesłanka, która nie obowiązywała | Naprawa |
|---|---|---|
| Tekst przy górnej krawędzi pola wielowierszowego | `Pad.Control` ma pion 0, bo wysokość pola JEDNOWIERSZOWEGO daje `Size.Control`; pole wielowierszowe jest właścicielem własnej wysokości | nowa rola `Pad.ControlMultiline`, ten sam selektor `TextBox[AcceptsReturn=True]` |
| Zbyt niski edytor w siatkach definicji (parametry procedury) | `field-editor` sięga tylko edytorów, które buduje `FieldGridColumns`; Name/Collate/Default/Description to `DataGridTextColumn`, którego `TextBox` tworzy sama siatka | klasa **na siatce** (`field-grid`), nadawana w jednym miejscu — zmierzone 12 → 24 px |
| Nieaktywny młotek ginie w jasnym motywie | `Opacity 0.5` jest bez motywu, a przepuszcza tło paska: biała ikona blaknąca w stronę jasnego tła traci kontrast, w stronę ciemnego go zyskuje | nieprzezroczyste `AccentDisabledBrush` + `OnAccentDisabledBrush`, wartości per motyw |

⭐ **Wniosek metodologiczny, ważniejszy od trzech poprawek.** Dwie z nich wymagały najpierw **zabrania wartości
lokalnych**, żeby styl miał gdzie trafić — dwanaście ikon `SvgIcon` w `MainWindow` miało `Foreground` wpisany
lokalnie, a wartość lokalna bije setter, więc stan `:disabled` nie mógł ich dosięgnąć. To trzeci raz, kiedy ten
mechanizm kosztuje osobne zgłoszenie (`MessageBanner` — sześć wariantów chromy per host; `FieldGridColumns` —
edytor 12 px; teraz nieaktywna ikona). ⛔ **Host ustawia `Data`, rozmiar i wyrównanie. Kolor ustawia styl.**

⚠ Poza zakresem, decyzją użytkownika: lokalne `DataGridRow Height` (22/28/30/34) w ośmiu widokach **zostają** —
część ma pisany powód (CheckBox w wierszu), a pułapka 17 mówi, że reguła opisuje to, co jest dobre, i nie jest
mandatem na zmianę wszystkiego, co do niej nie pasuje.

---

### §19.26 Iteracja 16 (M3.4a) — wiersz Metadata Explorera: katalog idzie za produktem (2026-08-04)

> **Wynik: zero zmian wizualnych, +2 testy (7243 → 7245), jedna korekta katalogu, jeden wpis do rejestru
> — i jedna obalona hipoteza wydajnościowa, która była najważniejszą częścią tej iteracji.**

#### §19.26.1 ⭐⭐ NAJPIERW POMIAR — I OBALIŁ HIPOTEZĘ, KTÓRĄ SAM WCZEŚNIEJ ZAPISAŁEM

Handover §3.7a niósł zmierzonego *kandydata na mechanizm* rzadkiego zawieszenia drzewa:
`SidebarFlatController.OnExpandedChanged` wstawia dzieci **pojedynczo**, a strażnik zbiorczy tej ścieżki
**nie obejmuje — pomija ją** (`if (_suspendDepth > 0) return;`). Fakt jest prawdziwy. Instrukcja brzmiała
jednak: **zmierzyć przed jakąkolwiek zmianą**, i to okazało się decydujące.

Nowa sekcja **B4** w `tools/probes/MetadataPerfProbe` (poza solucją, więc bez wpływu na build i testy)
uruchamia **prawdziwy `SidebarFlatController`** i klika chevron kategorii, której liście już są w pamięci:

| liście | ogon (wiersze pod kategorią) | expand | collapse | powiadomienia | jedna `Rebuild` |
|---|---|---|---|---|---|
| 2400 | 0 | **1,0 ms** | 1,1 ms | 2400 | 0,1 ms |
| 2400 | 3000 | 1,3 ms | 1,5 ms | 2400 | 0,4 ms |
| 2400 | 6000 | **2,3 ms** | 2,7 ms | 2400 | 0,6 ms |
| 5000 | 6000 | 4,8 ms | 7,4 ms | 5000 | 1,3 ms |

⭐ **Dla porównania defekt naprawiony przez Layer 1, na TYCH SAMYCH 2400 liściach: 916,9 ms** (sekcja B tej
samej sondy, przebieg z 2026-08-04 — liczba się nie zmieniła). Czyli ścieżka click-expand jest **Θ(N × ogon)
ze stałą tak małą, że mieści się w jednej klatce**. ⛔ **Nie dołożono tu strażnika**: zysk 2 ms nie
uzasadnia zmiany w mechanizmie, który działa, a §3.7a(c) wprost dopuszcza „brak znaleziska" jako wynik.

⚠⚠ **ZAKRES POMIARU, PODANY WPROST, BO BEZ NIEGO TA TABELA KŁAMIE.** Sonda mierzy **warstwę modelu** —
`ObservableCollection` i algorytm projekcji — **bez Avalonii**. Te 2400 powiadomień `CollectionChanged`
trafia w działającej aplikacji do **wirtualizującego `ListBox`a**, i ta część pozostaje **niezmierzona**.
⭐ A objaw zgłoszony przez użytkownika — *„drzewo samo przewija się w dół"* — jest zachowaniem **panelu**,
nie kolekcji. Pomiar więc **przesunął granicę niewiedzy, nie zamknął tematu**: wykluczył koszt modelu jako
przyczynę zamarcia, a nie wykluczył wirtualizacji ani kotwiczenia przewijania.

⭐ **Reuse before create — instrument już istnieje i nikt go nie musiał budować.**
`App/Diagnostics/ScrollTrace.cs` (`EMBERTERN_SCROLL_DIAG=1`) został napisany dokładnie pod ten objaw
i rozróżnia dwie możliwe przyczyny: *ekstent re-estymowany przez VSP* vs *my przebudowaliśmy drzewo*.
Przy następnym wystąpieniu u użytkownika daje odpowiedź bez zgadywania.

#### §19.26.2 ⚠ Skojarzenie z zawieszającym się testem — hipoteza SŁABNIE, ale nie upada

Użytkownik prosił, żeby sprawdzić, czy zawieszenie drzewa i zawieszenie `ConnectionExpandBindingProbe`
mają wspólny mechanizm — **i wprost, żeby tego nie zakładać**. Po pomiarze:

* **słabnie przesłanka główna** — skoro splice modelu kosztuje 2 ms, „drogi splice" nie tłumaczy zawieszenia;
* **zostaje wcześniejszy, zmierzony trop** (Keyboard Manager etap 5): nazwa testu raportowanego przy
  zawieszeniu **całej suity jest POZYCYJNA**, a podejrzanym jest teardown sesji / zamykanie pętli dispatchera.

⛔ **Nie łączę tych dwóch obserwacji w raporcie.** Eksperyment rozstrzygający — headless `ListBox`
z prawdziwą wirtualizacją i wymuszonym rozwinięciem dużej kategorii — **użytkownik zaakceptował jako
OSOBNY krok po M3.4a**, żeby nie mieszać porządkowania katalogu z eksperymentem, który może nic nie odtworzyć.

#### §19.26.3 Stan zastany wiersza drzewa

| Fakt | Wartość |
|---|---|
| `Size.Row.Tree` w katalogu | **20**, **zero konsumentów** |
| Rzeczywista wysokość | **24** — `MinHeight` w stylu `ListBox.sidebar-list ListBoxItem` |
| Czy 24 naprawdę rządzi? | ✅ **zmierzone, nie założone**: treść mierzy 15 px (ikona 15, `Text.Compact.LineHeight` 15) przy zerowym paddingu pionowym, więc `MinHeight` wygrywa |
| Wartości lokalne w szablonie wiersza | **11** |

⭐ **Decyzja DB była już rozstrzygnięta i to ona wyznaczyła kierunek: wiersz ZOSTAJE 24, poprawiamy KATALOG.**
To jest reguła prowadząca §11 zastosowana dosłownie — *dokument prowadzi produkt, ale prowadzi go tam, gdzie
produkt jest dobry*. Zejście do 20 nie byłoby porządkowaniem, tylko zmianą gęstości najgęstszego widoku
aplikacji. **Pułapka 3 po raz czwarty w tym etapie: katalog bywa zamiarem, nie opisem** (`Size.StatusBar`,
`Size.Row.Tab`, `Pad.Tab`/`Size.Icon`, teraz `Size.Row.Tree`).

#### §19.26.4 Co zrobiono

| # | Zmiana | Skutek wizualny |
|---|---|---|
| 1 | `Size.Row.Tree` **20 → 24**, z zapisanym powodem i wskazaniem decyzji DB | — |
| 2 | `MinHeight="24"` → `{DynamicResource Size.Row.Tree}` — **rola dostaje pierwszego konsumenta** | żaden (ta sama liczba) |
| 3 | Dwie ikony chevronu `12` → `Size.Icon.Sm` (trafienie dokładne; chevron stoi przy tekście 11 px) | żaden |
| 4 | `Padding="2,0"`, pole trafienia chevronu 20×20, szerokość kolumny chevronu — **komentarz z powodem w miejscu** | żaden |
| 5 | Ikona 15 / `Spacing` 5 (×3 szablony) — **komentarz + rejestr K15** | żaden (celowo) |

⚠ **Dlaczego kolumna chevronu 20 px NIE łamie R13** (i dlaczego to jest napisane w kodzie, a nie tylko tutaj):
liść nie ma chevronu, ale jego etykieta musi stać w tej samej kolumnie co etykieta kategorii — inaczej
wcięcie przestaje czytać się jako poziom drzewa. R13 zabrania rezerwować miejsce na element, który
**w danym kontekście nigdy się nie pojawi**; tutaj pojawia się przy każdej kategorii, a pusty slot **niesie
informację o strukturze**. To jest różnica między pustą dziurą a kolumną.

#### §19.26.5 ⚠⚠ DWA STRAŻNIKI I POWÓD, DLA KTÓREGO ŻADEN NIE JEST HEADLESS

`SidebarRowHeight_ComesFromTheTreeRowRole` + `TreeRowRole_CarriesTheHeightTheProductActuallyShows`
(oba w `DesignTokenComplianceTests`, oba **zweryfikowane podłożeniem naruszenia** — upadły z komunikatami
`"24"` vs `"{DynamicResource Size.Row.Tree}"` oraz `"20"` vs `"24"`).

⭐ **Dlaczego czytają ŹRÓDŁO, a nie kontrolkę.** Styl `ListBox.sidebar-list ListBoxItem` mieszka w lokalnym
bloku `<ListBox.Styles>` w `MainWindow.axaml`, więc gołe `new ListBox { Classes = { "sidebar-list" } }` go
**nie zobaczy** — jedyną kontrolką, która go widzi, jest `MainWindow`, a headless test konstruujący
`MainWindow` **zawiesza suite** (pułapka 4).

⛔⛔ **I tu była pokusa warta zapisania: „przenieś ten styl do `ControlStyles.axaml`, żeby dało się go
przetestować".** To jest **dokładnie ruch, który w M3.3a odtworzył regresję §19.2** — *zmiana MIEJSCA reguły
jest zmianą jej PRIORYTETU* — a dodatkowo styl jest **celowo** zawężony do jednej listy, żeby nie dotknąć
listy zapytań zapisanych. ⭐ Odrzucone: **nie przenosimy produktu po to, żeby pasował do narzędzia** (R16 —
pomiar jest narzędziem, nie celem).

⚠ **Co te testy chronią, a czego nie mówią.** Chronią przed dwiema cichymi awariami przy zielonym buildzie:
powrotem literału (#284) i literówką w kluczu — bo `{DynamicResource}` na brakującym kluczu **nie rzuca
wyjątku**, tylko zostawia właściwość przy wartości odziedziczonej (pułapka 1), co tutaj znaczyłoby wiersz
zapadnięty do 15 px. ⛔ **Nie mówią, jak drzewo wygląda.** Kryterium odbioru jest ekran (R8/R16).

⚠ Drugi test nie jest powtórzeniem pierwszego: pierwszy pilnuje, że widok **czyta rolę**, drugi — że rola
niesie **liczbę, która przeszła przez oko użytkownika**. Bez niego „migracja na rolę" byłaby zmianą wysokości
wiersza 24 → 20 przebraną za porządkowanie: zielony build, zielone testy, gęstość zmieniona bez decyzji.

#### §19.26.6 ⭐ STAŁA PROŚBA UŻYTKOWNIKA NA CAŁE M3.4 (2026-08-04)

> *„Podczas M3.4 cały czas miej z tyłu głowy ten stary bug z samoczynnym przewijaniem i zawieszeniem drzewa.
> […] Jeżeli gdziekolwiek trafisz na mechanizm mogący prowadzić do reentrant layoutów, zapętleń powiadomień
> albo walki o pozycję `ScrollViewera`, zatrzymaj się i pokaż mi to przed implementacją. To jest dla mnie
> ważniejsze niż zysk kilku milisekund."*

⭐ **To podnosi stabilność przewijania do rangi kryterium odbioru, na równi z poprawnością i wydajnością** —
i ustawia priorytet między nimi jednoznacznie. ⚠ Praktycznie, dla każdej większej zmiany w Metadata
Explorerze: zapytaj nie tylko *„czy to działa i ile kosztuje"*, ale też *„czy to może zapętlić układ,
powiadomienia albo pozycję `ScrollViewera`"* — i jeśli tak, **zatrzymaj się przed implementacją**.
⭐ To jest §19.23.9 w wydaniu ogólnym: tamten defekt (pasek przewijania paska zakładek) był **sprzężeniem
zwrotnym**, którego sonda licząca jeden przebieg układu **nie mogła wykryć z konstrukcji**. Drzewo ma
tysiące wierszy, wirtualizację i kotwiczenie przewijania — czyli dokładnie te warunki.

#### §19.26.7 Kryteria zakończenia iteracji

| # | Warunek | Stan |
|---|---|---|
| 1 | Zakres zamknięty, nic „do dokończenia w następnej" | ✅ |
| 2 | Każda pozostawiona wartość lokalna ma powód w miejscu | ✅ (padding 2,0 · pole trafienia 20 · K15 ×3) |
| 3 | Baza strażnika odzwierciedla stan faktyczny | ✅ — licznik `DesignTokenComplianceTests` mierzy `FontSize`/`CornerRadius`/`FontFamily`; **wysokości i rozmiary ikon nigdy nie były w żadnym liczniku**, więc baza się nie zmienia (to samo znalezisko, co §19.0/§3.1) |
| 4 | Build 0/0 | ✅ |
| 5 | Testy w trzech partycjach | ✅ **7134 + 57 + 54 = 7245** (+2) |
| 6 | Smoke + aplikacja obejrzana w obu motywach | ✅ smoke · ⏸ **QA wizualne użytkownika** (zmiana jest neutralna z konstrukcji — te same liczby) |
| 7 | Wpis w §19 | ✅ ten |
| 8 | Commit z kodem i opisem | ✅ |

---

### §19.27 Krok 15b — eksperyment headless: wirtualizacja NIE jest mechanizmem zawieszenia (2026-08-04)

> **Wynik: hipoteza obalona po raz drugi, tym razem w warstwie, której M3.4a nie mógł zmierzyć. Cztery
> pomiary, zero zawieszeń, pozycja przewijania nie rusza się sama w żadnym scenariuszu. +4 testy
> (7245 → 7249).**

#### §19.27.1 ⭐⭐ Po co ten krok istniał — i dlaczego to była OSOBNA klasa

M3.4a zmierzyło ścieżkę „rozwiń kliknięciem" w **warstwie modelu** i wyszło 2,3 ms. Ale §19.26.1 zapisało
zakres tego pomiaru wprost: **sonda nie dotykała Avalonii**, a zgłoszony objaw — *„drzewo samo zaczyna
przewijać się w dół"* — jest zachowaniem **panelu**, nie kolekcji. Krok 15b mierzy tę brakującą połowę.

⭐⭐ **Klasa `MetadataTreeVirtualizationProbe` istnieje osobno po to, żeby ROZDZIELIĆ DWIE ZMIENNE, które
dotąd zawsze występowały razem:**

| | Zmienna | Stan przed eksperymentem |
|---|---|---|
| **A** | konstruowanie `MainWindow` w teście headless | zmierzony kształt podatny na zawieszenie — `BrandingPresentationTests` zawieszało się, dopóki budowało `MainWindow`, i schodzi do 476 ms na gołym `new Window()` |
| **B** | inkrementalny splice do wirtualizującej listy | hipoteza §3.7a(a) |

⚠⚠ **`ConnectionExpandBindingProbe` — klasa, którą użytkownik kazał uruchamiać SAMĄ — buduje `MainWindow`
w wielu testach.** Czyli obie zmienne siedzą w niej naraz i żadnej z nich nie da się z niej odczytać.
⛔ **Dlatego dopisanie eksperymentu do tamtej klasy byłoby błędem metodologicznym** — skleiłoby z powrotem
dokładnie to, co ma zostać rozdzielone. Nowa klasa buduje **gołe `Window` + `ListBox`**, więc jej wynik
mówi o **B i tylko o B**.

⚠ Kontener jest częścią mechanizmu (pułapka 14): okno ma skończoną wysokość (600 px = 25 wierszy),
lista ma `VirtualizingStackPanel`, wiersz ma stałą wysokość `Size.Row.Tree`. Bez tego wirtualizacja
w ogóle się nie włącza i test mierzyłby co innego, niż obiecuje nazwa. **Zweryfikowane w samym teście** —
asercja wymaga, żeby `ScrollViewer` i `VirtualizingStackPanel` faktycznie istniały w drzewie wizualnym.

#### §19.27.2 Wyniki — cztery scenariusze, 2 400 liści + 3 000 wierszy rodzeństwa

| Scenariusz | Czas (z układem) | Offset przed → po | Pierwszy zrealizowany wiersz |
|---|---|---|---|
| rozwinięcie przy górze listy | **52,9 ms** | 0,0 → 0,0 | — |
| rozwinięcie, gdy kategoria stoi **nad** viewportem | **42,8 ms** | 1500,0 → **1500,0** | 50 → **50** |
| **pełna re-projekcja** przy przewinięciu w głąb | **46,6 ms** | 40000,0 → **40000,0** | 1333 → **1333** |
| splice inkrementalny vs jedna re-projekcja | **56,5** vs **32,1 ms** | — | — |

⭐ **Żadnego zawieszenia. Pozycja przewijania nie przesunęła się sama ANI RAZU** — także w scenariuszu
najostrzejszym, gdzie 2 400 wierszy wchodzi **nad** tym, na co użytkownik patrzy.

⭐⭐ **Wniosek uboczny, ale mocny: strażnik zbiorczy na tej ścieżce NIC by nie kupił.** Splice inkrementalny
i pojedyncza re-projekcja są tego samego rzędu (dziesiątki ms, przy sporej wariancji między przebiegami —
w jednym przebiegu 52,7 vs 50,8, w innym 56,5 vs 32,1). Panel i tak realizuje kontenery od nowa, więc
zamiana N wstawień na jeden `Reset` **nie jest oszczędnością**. To **niezależnie potwierdza decyzję z M3.4a
o niedokładaniu tam strażnika** — tym razem od strony panelu, a nie modelu.

#### §19.27.3 ⚠ ROZBIEŻNOŚĆ Z ZAPISEM LAYER 1 — odnotowana, świadomie NIEROZSTRZYGNIĘTA

`metadata-refresh-analysis.md` §7 opisuje kompromis Layer 1 jako *„obiekty `SidebarRow` są odtwarzane
i lista przewija się na górę"*. **Pomiar tego nie potwierdza:** pełna `Rebuild` przy offsecie 40 000 px
zostawia offset na 40 000 i pierwszy zrealizowany wiersz na 1333.

⭐ Możliwych wyjaśnień jest kilka i **żadnego nie rozstrzygam bez pomiaru**: (a) „przewinięcie na górę"
w produkcie bierze się z czegoś innego na tej ścieżce niż sama re-projekcja (ponowne nałożenie filtra,
zmiana zaznaczenia, przeniesienie fokusu), (b) dotyczy `ApplyFilterAsync`, a nie `EndUpdate`, (c) opis
w §7 był wnioskiem, nie pomiarem. ⛔ **Nie „poprawiam" żadnego z tych dokumentów na podstawie domysłu** —
to jest **pułapka 20 w czystej postaci** (przeczytaj zakres wcześniejszego pomiaru, zanim użyjesz go jako
odpowiedzi), tylko odwrócona: tym razem to mój pomiar mógłby zostać użyty poza swoim zakresem.

#### §19.27.4 Co to znaczy dla hipotezy o „felernym teście"

| | Stan po kroku 15b |
|---|---|
| **B — inkrementalny splice** | ⛔ **wykluczony w izolacji**: w gołym oknie z prawdziwą wirtualizacją nie zawiesza się, nie rusza przewijania i nie jest droższy od pełnej re-projekcji |
| **A — konstruowanie `MainWindow`** | ⚠ **pozostaje jedynym stojącym podejrzanym** — zgodny z pomiarem `BrandingPresentationTests`, ale **nieudowodniony** |

⭐ **Odpowiedź na pytanie użytkownika z §3.7a(b) brzmi więc: NIE, nie znalazłem wspólnej przyczyny — i to
jest wynik, a nie jego brak.** Stary bug drzewa i zawieszanie się `ConnectionExpandBindingProbe` **nie
dzielą mechanizmu, którego dotyczyła hipoteza**. ⛔ Nadal nie łączę tych dwóch obserwacji w raporcie
i nadal nie twierdzę, że wiem, co zawiesza suitę — **to zostaje osobnym zadaniem infrastrukturalnym**,
zgodnie ze stałą instrukcją użytkownika, żeby nie odciągać nim etapu.

⚠ **Czego eksperyment NIE dowodzi, powiedziane wprost:** szablon wiersza jest uproszczony (sam tekst
o właściwej wysokości), a węzły są syntetyczne. Dowodzi więc, że **mechanizm** jest stabilny — nie że
**produktowe** drzewo jest stabilne. Własnością nośną, którą odtworzono wiernie, jest **jednolita wysokość
wiersza**, bo od niej zależy ekstent i kotwiczenie.

#### §19.27.5 ⭐ Te cztery testy ZOSTAJĄ — i stają się strażnikiem stabilności przewijania

Użytkownik podniósł stabilność przewijania Metadata Explorera do rangi kryterium odbioru na całe M3.4
(§19.26.6). Te testy dokładnie to obsługują: od teraz **każda większa zmiana w drzewie ma maszynową
kontrolę, że rozwinięcie dużej kategorii kończy się w ograniczonym czasie i nie przesuwa pozycji
przewijania samo z siebie**.

⚠ **Granice czasowe są CELOWO hojne (5 s) i to nie jest słabość testu.** Przedmiotem nie jest wydajność —
ta mieszka w sondzie (`MetadataPerfProbe` B4) — tylko *„czy to się kończy"*: zawieszenie objawia się
sekundami albo brakiem powrotu, nie dziesiątkami milisekund. Granica zawężona do zmierzonych 50 ms byłaby
testem migoczącym na CI, czyli **testem, który psuje się z powodów niezwiązanych z jego przedmiotem**.
⭐ To jest **R16 zastosowane od strony konstrukcyjnej**: asercja obejmuje dokładnie tyle, ile maszyna ma
tu sensownego do powiedzenia.

⚠ Test czwarty (pełna re-projekcja) **niczego nie zabrania asercją** poza zakończeniem się — bo gdyby
pozycja przewijania jednak się ruszała, byłby to **udokumentowany kompromis Layer 1**, a jego zmiana jest
decyzją produktową, nie skutkiem ubocznym eksperymentu. Liczba idzie do logu jako **pomiar**.

⚠⚠ Nowa klasa headless dołączyła do `HeadlessCollection` **i do filtra partycji** — filtr jest listą nazw
i starzeje się cicho (§18.1.6). Partycje po kroku 15b: **7134 + 61 + 54 = 7249**.

---

### §19.28 Iteracja 17 (M3.4b, część 1) — menu kontekstowe paska bocznego przestają być mnożone przez wirtualizację (2026-08-04)

> **Wynik: ~74% czasu przewijania najgęstszego widoku aplikacji, 440 → 22 żywych `MenuItem`,
> trzy bloki XAML zmieniają miejsce. Zero nowego mechanizmu. +5 testów (7249 → 7254).**

#### §19.28.1 ⭐⭐ Znalezisko przyszło z INWENTARYZACJI, nie z planu — i dlatego zatrzymałem etap

M3.4b miał być przeglądem 32 menu. Pierwsza komenda inwentaryzacyjna pokazała, że szablon wiersza
`MetadataNodeViewModel` niesie **inline `ContextMenu` z 22 pozycjami**, a ten szablon dostaje **każdy
zrealizowany wiersz** wirtualizowanej listy paska bocznego.

⭐ **Zgodnie ze stałą prośbą użytkownika (§19.26.6) zatrzymałem się i pokazałem to przed implementacją** —
bo to jest mechanizm leżący dokładnie na ścieżce przewijania, czyli w tej samej operacji, w której
zgłoszono stary bug. ⚠ **Nie twierdziłem i nadal nie twierdzę, że to jest przyczyna zawieszenia.**

#### §19.28.2 Pomiar — i decyzja użytkownika, żeby nie mierzyć dalej

Pomiar wstępny (5 000 wierszy, 40 skoków, menu 22 pozycji) pokazał trzy rzeczy:

* ⚠⚠ **wirtualizacja NIE odzyskuje kontenerów w pełni** — szablon budowany jest **1 640 razy** na jedno
  przewinięcie, więc menu jest tworzone i wyrzucane 1 640 razy;
* menu dokłada ~0,23 ms na wiersz, czyli **~40% narzutu** przy gołych pozycjach;
* w każdej chwili żyje **440 obiektów `MenuItem`** (20 wierszy × 22), które przy przewijaniu się wymieniają.

Użytkownik zdecydował: **zmierzyć wyłącznie wariant A** (jedno współdzielone menu), bez rozbudowywania
rozwiązania „tylko dlatego, że znaleźliśmy koszt". Powstał `SharedContextMenuFeasibilityProbe`
odpowiadający na dwa pytania:

| Pytanie | Odpowiedź |
|---|---|
| Czy współdzielone menu działa **bez obchodzenia bindowania**? | ✅ **TAK.** Jedna instancja przypina się do 20/20 wierszy; po otwarciu przejmuje `DataContext` **tego** wiersza (`OBJ_3` → `OBJ_7` → `OBJ_1`), a zwykły `{Binding}` rozwiązuje się poprawnie i **podąża** |
| Jaki jest realny zysk? | menu na wiersz **1237–2619 ms** vs współdzielone **324–504 ms** → **74% czasu przewijania**; żywych `MenuItem` **440 → 22** |

⭐ **Wariancja jest drugą informacją, nie szumem:** wariant per-wiersz waha się 2,1×, współdzielony
mieści się na dużo niższym poziomie. To sygnatura presji alokacyjnej — a ta objawia się **szarpaniem**,
nie równym spowolnieniem.

⚠ **Nośnikiem kontekstu jest dziedziczenie `DataContext`, NIE `PlacementTarget`** (ten przy programowym
`Open` odczytał się jako `null`). ⛔ Nie opierać tu niczego na `PlacementTarget` bez własnego pomiaru.

⭐ **Decyzja użytkownika co do ryzyka `IsVisible`:** *„nie ma sensu budować kolejnej infrastruktury
pomiarowej dla czegoś, co można zweryfikować bezpośrednio na działającej aplikacji"* — więc zmiana weszła,
a przeliczanie widoczności pozycji sprawdzamy w normalnym QA. **To jest R16 zastosowane do samego procesu:
pomiar jest narzędziem, a nie obowiązkowym etapem przed każdą zmianą.**

#### §19.28.3 Co zrobiono

Trzy bloki `<ContextMenu>` przeniesione z `DataTemplate` do `<ListBox.Resources>` z `x:Key`, a szablony
odwołują się do nich przez `ContextMenu="{StaticResource …}"`. **Żadnego kodu w code-behind, żadnego
zachowania, żadnej zmiany w bindingach pozycji.**

| Szablon | Pozycji | Zasób |
|---|---|---|
| `FolderNodeViewModel` | 3 | `SidebarFolderMenu` |
| `ConnectionNodeViewModel` | 11 | `SidebarConnectionMenu` |
| `MetadataNodeViewModel` | **22** | `SidebarMetadataMenu` |

#### §19.28.4 ⭐⭐ KOMPILATOR ZŁAPAŁ RZECZ, KTÓRA JEST ULEPSZENIEM, NIE PRZESZKODĄ

Po przeniesieniu build zgłosił **~30 błędów AVLN2000**: w `DataTemplate` typ kontekstu brał się z jego
`DataType` — **niejawnie i za darmo** — a w zasobach tego rodzica nie ma, więc bindingi kompilowane były
rozwiązywane względem `MetadataExplorerViewModel`.

⭐ **Odpowiedzią jest `x:DataType` na każdym menu, i to jest LEPSZY stan niż wyjściowy:** kontrakt, który
wcześniej wynikał z położenia w drzewie XAML, jest teraz **zadeklarowany wprost i sprawdzany przy
kompilacji**. ⚠⚠ Gdyby bindingi były refleksyjne, ten sam defekt byłby **CISZĄ** — puste menu przy prawym
kliknięciu i zielony build. ⛔ Nie usuwać `x:DataType` „bo się kompiluje".

#### §19.28.5 ⚠⚠ TRZY STRAŻNIKI, KAŻDY ZWERYFIKOWANY PODŁOŻENIEM NARUSZENIA — I JEDEN Z NICH JEST JEDYNĄ SIATKĄ

`SidebarMenuInstancingTests` (partycja główna, czyta źródło):

| Test | Co łapie | Czy build też by to złapał? |
|---|---|---|
| `SidebarRowTemplates_DeclareNoInlineContextMenu` | powrót menu inline do szablonu | ⛔ **NIE** — kompiluje się, działa funkcjonalnie, widać dopiero jako szarpanie u użytkownika z dużą bazą |
| `EverySharedMenuReference_HasItsResource` | odwołanie do nieistniejącego zasobu | ⛔ **NIE — zmierzone**: podłożone `{StaticResource NieMaTakiegoZasobu}` **przeszło build**; nierozwiązany `StaticResource` rzuca dopiero **przy realizacji wiersza**, czyli po połączeniu i rozwinięciu kategorii |
| `EverySharedMenu_DeclaresItsDataType` | brak `x:DataType` | ✅ tak, **dziś** — usunięcie go wywala build; test jest siatką na wypadek bindingów refleksyjnych i mówi to w swoim komentarzu |

⭐ **Środkowy wiersz jest najważniejszy i to jest zmierzony fakt, nie ostrożność:** smoke NIE złapie
literówki w kluczu, bo pusty pasek boczny **nie realizuje ani jednego wiersza metadanych**. Aplikacja
wstaje, wygląda dobrze i wywala się dopiero, gdy użytkownik rozwinie kategorię.

#### §19.28.6 ⚠ Zakres weryfikacji — powiedziany wprost

* ✅ **`SidebarFolderMenu` i `SidebarConnectionMenu` są zweryfikowane maszynowo** — `ConnectionExpandBindingProbe`
  buduje prawdziwy `MainWindow` i realizuje wiersze folderu i połączenia, więc ich `StaticResource` musiał
  się rozwiązać.
* ⏸ **`SidebarMetadataMenu` weryfikuje QA użytkownika** — jego wiersze wymagają połączenia z bazą
  i rozwiniętej kategorii, czego żaden test w tym repo nie robi.
* ⏸ **Przeliczanie `IsVisible` pozycji przy zmianie `DataContext`** — decyzją użytkownika sprawdzane
  na działającej aplikacji, nie kolejnym eksperymentem.

#### §19.28.7 ⏸ Czego ta iteracja NIE zrobiła

⛔ **Właściwy przegląd M3.4b nie został wykonany.** Inwentaryzacja zdążyła zmierzyć stan wejściowy —
**32 `ContextMenu`, 154 `MenuItem`, 140 z ikoną (czyli 14 bez), 27 z gestem z katalogu** — i na tym się
zatrzymała, bo znalezisko wydajnościowe miało pierwszeństwo z mocy stałej prośby użytkownika.
Przegląd (czy 32 menu trzymają poziom menu zakładki z M3.3c: ikona, gest, własne `CanExecute`) jest
**następnym krokiem**.

⚠ **Ten sam wzorzec „menu inline w szablonie wiersza" występuje jeszcze w dwóch miejscach** i **świadomie
ich nie ruszałem**: `SavedQueryViewModel` (lista zapytań zapisanych) i `WorkspaceTabViewModel` (pasek
zakładek). ⭐ Powód jest merytoryczny, nie zakresowy: **żadna z tych list nie jest wirtualizowana ani nie
osiąga tysięcy wierszy**, więc mnożnik, który tu decydował, tam nie istnieje. ⛔ Nie przenosić ich „dla
spójności" bez pomiaru — to byłaby pułapka 17 (reguła opisuje to, co jest dobre; nie jest mandatem).

---

### §19.29 🐞 Przerwa w M3.4b — stary defekt drzewa ma przyczynę i jest naprawiony (2026-08-04)

> **To nie jest iteracja Product Polish**, tylko przerwa w M3.4b wymuszona przez znalezisko. Pełna
> diagnoza, log i pomiary: `metadata-refresh-analysis.md` **§10–§12**. Tutaj wyłącznie to, co dotyczy
> etapu i co musi przeżyć w pamięci projektu.

#### §19.29.1 Przyczyna, w jednym zdaniu

Gdy jakiś wiersz jest zaznaczony, a użytkownik rozwinie dużą kategorię, Avalonia odkłada na Dispatcher
`SelectingItemsControl.AutoScrollToSelectedItemIfNecessary`; zaznaczony wiersz leży poza oknem realizacji,
a `VirtualizingStackPanel.ScrollIntoView` **nie potrafi skoczyć do nierealizowanego indeksu**, więc pełznie
do celu **po jednym wierszu (24 px) na cykl Dispatchera** — zagładzając priorytet tła, przez co aplikacja
przestaje reagować.

**Naprawa: `AutoScrollToSelectedItem="False"` wyłącznie na `SidebarList`.** Nic więcej, świadomie bez
żadnych warunków ani własnego algorytmu przewijania (decyzja użytkownika). Ta lista ma własne, jawne
„pokaż mi ten obiekt", więc drugi automatyczny mechanizm był tu zbędny.

#### §19.29.2 ⭐⭐ TRZY LEKCJE, KTÓRE PRZEŻYWAJĄ TEN DEFEKT

**(1) Pomiar syntetyczny odtwarza MECHANIZM, ale nie odtwarza STANU.** M3.4a (§19.26) i krok 15b (§19.27)
**oba wykluczyły swoją hipotezę poprawnie** — i oba były ślepe, bo **w żadnym nic nie było zaznaczone**.
Zmienna decydująca o całym zjawisku nie występowała w eksperymencie. ⚠ Praktycznie: zanim uznasz, że
pomiar wyklucza hipotezę, **wypisz stany, w których defekt występuje u użytkownika, i sprawdź, które
z nich twój eksperyment odtwarza.**

**(2) Instrument, który ma nie wywalić aplikacji, nie może używać mini-języka wykonywanego w produkcji.**
Pierwsze uruchomienie `TreeDiagnostics` **zabiło aplikację** przez `{4,+8:0.0}` — wyrównanie w formacie
złożonym przyjmuje tylko liczbę całkowitą (§10.6). Build był zielony i pozostałby zielony.
⭐ Najgorsze było to, **co ta awaria zniszczyła**: narzędzie mające złapać cudzy defekt samo stało się
defektem, a log użytkownika opisywał wyłącznie błąd instrumentacji.

**(3) ⭐⭐ POMIAR NEGATYWNY MUSI SIĘ PRZEDSTAWIĆ.** Samotest kanału wyjątków (§10.3) rzuca i łapie
nieszkodliwy wyjątek na starcie. Dzięki temu **cisza w kategorii `EXC` była DOWODEM**, że nic nie
poleciało — a nie dwuznacznością „albo nic nie poleciało, albo hak nie działa". To była jedna z pięciu
rzeczy, o które prosił użytkownik, i jedyna, którą dało się zepsuć **przez samo nierobienie niczego**.

#### §19.29.3 ⛔ Co z tego zostaje na stałe

* **`AutoScrollToSelectedItem="False"` + strażnik** `SidebarList_DisablesAvaloniaAutoScrollToSelectedItem`
  — ⚠ ta właściwość wygląda dokładnie jak coś, co ktoś kiedyś „posprząta": domyślnie `true`, usunięcie nie
  psuje **żadnego innego testu**, nie rusza piksela, a defekt wraca dopiero u użytkownika z dużą bazą.
* **`EMBERTERN_TREE_DIAG` zostaje jako ukryte narzędzie deweloperskie** (decyzja użytkownika). ⭐ To ono
  znalazło przyczynę po dwóch latach istnienia objawu; bez flagi nie kosztuje nic. ⛔ Nie usuwać i nie
  wystawiać w UI. ⭐ Sięgać po nie przy **każdym** defekcie o kształcie „przewijanie / zaznaczenie /
  Dispatcher", nie tylko w drzewie.
* ⏸ **Hipoteza o zawieszającym się teście** (`metadata-refresh-analysis.md` §12) — do obserwacji, nie do
  ogłoszenia.

#### §19.29.4 Wpływ na M3.4

⛔ **Żaden na zakres.** M3.4a i krok 15b zostają jak były — ich wnioski o koszcie splice'u i wirtualizacji
**nadal obowiązują**; ten defekt miał inną przyczynę, której one nie badały. M3.4b część 1 (współdzielone
menu) też zostaje. **Wracamy do M3.4b część 2 — przeglądu 32 menu — bez zmiany planu.**
⭐ Jedno domknięcie po drodze: checklista §3.7a handovera jest **zamknięta w całości** — (a) rzadkie
zawieszenie: **przyczyna znaleziona i naprawiona**; (b) skojarzenie z testem: **hipoteza zapisana do
obserwacji**; (c) przegląd wydajności rozwijania: **wykonany, bez znaleziska wymagającego zmiany**.

#### §19.29.5 ⭐ RATYFIKOWANE — cztery warianty „Close" ZOSTAJĄ BEZ IKON (użytkownik, 2026-08-04)

Menu zakładki z M3.3c ma 4 z 9 pozycji bez ikony: *Close others*, *Close to the right*,
*Close unmodified*, *Close all*. Formalnie nie spełnia to miary postawionej dla tego przeglądu
(140 ze 154 pozycji niesie ikonę).

⛔ **Decyzja: nie dodawać ich na siłę.** Słowa użytkownika: *„Wszystkie cztery pozycje należą do jednej
rodziny operacji i nie widzę dobrych, jednoznacznych ikon, które niosłyby realną wartość. To byłaby raczej
dodatkowa chroma niż poprawa UX."*

⭐ To jest **pułapka 17 rozstrzygnięta na korzyść produktu**: reguła „pozycja menu niesie ikonę" opisuje to,
co jest dobre, i nie jest mandatem do wymyślenia czterech podobnych glifów „zamknij coś", których jedyną
funkcją byłoby zaspokojenie licznika. ⚠ Cztery pozycje jednej rodziny **pod** pozycją `Close` (która ikonę
ma) czytają się jako jej warianty — a cztery pozorne różnice zaciemniłyby to, co dziś jest czytelne.
⏸ Wraca do rozważenia **tylko** jeśli pojawi się naprawdę czytelny zestaw znaków.

#### §19.29.6 ⏸ OBSERWACJA WZMOCNIONA — nadal hipoteza, nie fakt (użytkownik, 2026-08-04)

Po wyłączeniu `AutoScrollToSelectedItem`, na przebiegu **większym** niż ten, który defekt pokazał:

* zniknęła pętla `AutoScrollToSelectedItemIfNecessary` (93 → 0 wystąpień w stosach),
* zniknęły cykliczne przesunięcia **+24 px** (93 → 0),
* **heartbeat Dispatchera przestał zanikać**,
* mimo **większego drzewa** (15 980 vs 13 217 wierszy) i **sześciokrotnie większej liczby zaznaczeń**
  (19 vs 3) problem nie wystąpił.

⚠⚠ **To NADAL nie jest dowód, że sporadycznie zawieszający się `ConnectionExpandBindingProbe` miał tę samą
przyczynę** — i celowo nie jest tak zapisane. To obserwacja do historii projektu.

⭐ **Kryterium rozstrzygające pozostaje bez zmian i nie wymaga żadnej nowej infrastruktury: jeżeli od
2026-08-04 ten test również przestanie się sporadycznie zawieszać, będzie to bardzo mocna przesłanka, że oba
problemy miały wspólną przyczynę.** ⛔ Nie ogłaszać na podstawie kilku zielonych przebiegów — zawieszenie
było rzadkie z definicji. **Obserwować zachowanie całej suity przez dłuższy czas i zapisać wynik w OBIE
strony.** Pełny zapis hipotezy z argumentami za i przeciw: `metadata-refresh-analysis.md` §12.

---

### §19.30 Iteracja 18 (M3.4b część 2) — przegląd 32 menu kontekstowych (2026-08-04)

> **Wynik: BRAK ZNALEZISKA WYMAGAJĄCEGO ZMIANY. Dwie rzeczy, które wyglądały na niespójność, okazały się
> regułami działającymi poprawnie.** Zero zmian w kodzie.

#### §19.30.1 Stan zmierzony

| | Liczba |
|---|---|
| `ContextMenu` | **32** |
| `MenuItem` | **154** |
| bez ikony | **6** |
| komend wiązanych z menu | **71** |

⚠ **Korekta pierwszego pomiaru: „14 bez ikony" było błędne.** Osiem pozycji niesie ikonę **składnią
elementową** `<MenuItem.Icon>` (złożona kontrolka `DebuggerIcon`), której nie łapał skan atrybutu `Icon=`.
⭐ To ta sama pułapka co #285: **pomiar po nośniku nie odróżnia roli od zapisu** — liczyłem atrybut, a nie
„czy pozycja ma ikonę".

Realne 6 bez ikony: **2 kwalifikatory zakresu triggerów** (ikonę niesie pozycja nadrzędna — świadomy wyjątek
odnotowany już przez Keyboard Manager) i **4 warianty „Close"** — ratyfikowane, że zostają (§19.29.5).

#### §19.30.2 ⭐⭐ Pomiar 1 — gesty. Pozorna niespójność okazała się regułą

`DeleteCommand` występuje w czterech menu; **tylko jedno pokazuje gest** (`F8`). Wyglądało to na dryf.

**Sprawdzone w `MetadataExplorerViewModel.ResolveCommand`:** `CommandId.DeleteObject` rozwiązuje się
**wyłącznie** dla `MetadataNodeViewModel { CanDeleteLeaf: true }`. Dla folderu, połączenia i zapisanego
zapytania zwraca `null` — czyli **`F8` tam nie działa**.

⭐ **Więc stan obecny jest POPRAWNY i jest zastosowaniem ratyfikowanej reguły:** *gest pokazuje się TYLKO
tam, gdzie działa* (`keyboard-manager.md` §14). Dopisanie go do menu folderu uczyłoby nieprawdy. To samo
dotyczy `NewObject` (tylko grupa z `SupportsNew`) i `RefreshMetadata` (tylko przez połączenie).

⚠⚠ **Metodologiczne, warte zapamiętania: automatyczne skrzyżowanie po NAZWIE nie odpowiada na to pytanie.**
Menu wiąże komendy ViewModelu (`AddFieldCommand`), a katalog trzyma identyfikatory (`CollectionAdd`);
mapowanie żyje w `ResolveCommand`, nie w nazwach. Ze 154 pozycji nazwa pokryła się **raz**
(`RefreshMetadata`) i to był przypadek. ⛔ Nie budować strażnika na tym skojarzeniu — dawałby fałszywy
spokój.

#### §19.30.3 ⭐ Pomiar 2 — `CanExecute`. Trzy pozycje bez niego, wszystkie poprawnie

`Connect`, `Disconnect` i `Delete` z menu połączenia nie mają `CanExecute`. **I nie powinny go mieć:**
pierwsze dwa są bramkowane `IsVisible="{Binding !IsConnected}"` / `{Binding IsConnected}` — pozycja ukryta
nie da się kliknąć — a usunięcie profilu połączenia jest zawsze dozwolone.

⭐ **`IsVisible` i `CanExecute` to dwa poprawne narzędzia do dwóch różnych sytuacji**, nie gorszy i lepszy
wariant: **ukryj**, gdy pozycja w tym stanie nie ma sensu w ogóle (nie można rozłączyć czegoś, co nie jest
połączone); **wyszarz**, gdy operacja istnieje, ale chwilowo nie jest dostępna — bo znikająca pozycja psuje
pamięć mięśniową użytkownika. Menu zakładki z M3.3c używa `CanExecute` właśnie dlatego, że jego pozycje mają
zostać widoczne.

⚠ **Ograniczenie metody, podane wprost:** wykrywanie `CanExecute` to heurystyka na źródle (okno wokół
deklaracji `[RelayCommand]`), nie analiza semantyczna. Kontrola pozytywna wypadła dobrze — cztery pozycje
menu zakładki, o których z M3.3c wiadomo, że mają `CanExecute`, zostały jako mające je rozpoznane.

#### §19.30.4 ⛔ Czego przegląd świadomie NIE zrobił

* **Nie ujednolicał zestawów pozycji między menu** — Keyboard Manager ratyfikował *„to samo menu oferuje te
  same operacje"*, a nie *„każde menu ma te same pozycje"*.
* **Nie dodawał ikon do wariantów „Close"** (§19.29.5).
* **Nie budował strażnika na skojarzeniu nazw** komend z identyfikatorami katalogu (§19.30.2).

⭐ **Brak znaleziska jest tu wynikiem, nie porażką przeglądu.** Menu przeszły przez etap Keyboard Managera
(32 menu, jeden zestaw stylów, ikony i gesty z jednego źródła) i M3.4b część 1 (współdzielenie instancji);
poziom postawiony przez M3.3c **jest utrzymany**.

---

### §19.31 Iteracja 19 (M3b.1) — import i Script Executor w sekcji postępu (2026-08-04)

> **Zakres:** podłączenie dwóch źródeł postępu z zakładek do sekcji 4 paska statusu (§8.4.6) plus arbitraż,
> którego M3.1f nie potrzebowała, bo miała jedno źródło. ⛔ **Bez dotykania połączenia z bazą** — to jest
> M3b.2, świadomie oddzielone decyzją użytkownika jako mniejszy, łatwiejszy do zweryfikowania krok.
> Build 0/0; suita **7284** w trzech partycjach (7167 + 63 + 54, +13); smoke czysty.

#### §19.31.1 ⚠⚠ POMIAR WEJŚCIOWY OBALIŁ INWENTARZ ETAPU W TRZECH PUNKTACH

Plan (handover §3.9, prompt startowy §4.2) mówił „trzy ścieżki `IProgress`", potem „cztery". **Zmierzone:
jest ich PIĘĆ.**

| # | Ścieżka | Gdzie tworzona | Zna sumę? |
|---|---|---|---|
| 1 | zapytanie SQL | `MainWindowViewModel.cs:6827` | ⛔ nie — już podłączone (operacja referencyjna) |
| 2 | import | `DataImportTabViewModel.cs:957` | ⚠ `EstimatedRows`, **bywa nieznana** |
| 3 | **Script Executor** | `ScriptExecutorTabViewModel.cs:188` | ✅ **ściśle** (`_lastStatements.Count`) |
| 4 | eksport | `ExportDialogViewModel.cs:197` | ⛔ nie |
| 5 | batch | `MainWindowViewModel.cs:5804` | ✅ |

⭐ **`IProgress<ScriptStatementResult>` nie występowało w ŻADNYM inwentarzu** — ani w handoverze, ani
w §19.7.2, ani w prompcie startowym — a jest **jedyną ścieżką w aplikacji ze ścisłą sumą**. To trzeci raz
w M3, gdy plan etapu okazał się nieaktualny wobec kodu (§19.22.1, §3.7a, teraz to): **plan starzeje się tak
samo cicho jak string i jak komentarz** (#284, pułapki 20/21).

⭐⭐ **DRUGIE ZNALEZISKO ZMNIEJSZYŁO ZAKRES O POŁOWĘ: eksport i batch biegną MODALNIE.**
`ExportDialog.ShowAsync` → `dlg.ShowDialog(owner)`, `BatchResultsDialog` → `dialog.ShowDialog(this)`.
Modal blokuje okno główne, więc **cała wartość sekcji postępu tam nie istnieje**: §19.7.3 uzasadniło ją tym,
że operacja przeżywa przełączenie zakładki — a przełączyć się nie można. Gorzej: `HasCancel` dałoby przycisk
**niemożliwy do kliknięcia**, bo leżący w zablokowanym oknie. ⛔ **Nie podłączać ich**; decyzja użytkownika
potwierdziła pomiar.

⚠ **Trzecie: „16 ViewModeli ze stanem zajętości" to nie lista rzeczy do podłączenia.** Czternaście z nich to
`IsLoading` typu „ładuję zawartość tej zakładki", a **każde ma już własny nośnik w miejscu** — jedenaście
stałych `*LoadingHint` w `UiStrings`. Pytanie z pułapki 13 (*„czy ten fakt ma już właściciela?"*) odpowiada
tu samo sobie: ma, i właścicielem jest **to, na co użytkownik patrzy**. ⛔ `PerformancePanelViewModel`
odrzucony osobno — `BuildCallback(CancellationToken.None)`, czyli operacja bez anulowania.

#### §19.31.2 ⭐ Dwie operacje MOGĄ biec naraz — więc arbitraż nie jest hipotetyczny

`DataImportEnvironment.cs:40`: *„Since I7.5 the module owns its own transaction, so the SQL Editor's state is
none of its business"*. Import nie dotyka linii Data, więc **biegnie równolegle z zapytaniem albo skryptem**.
Pytanie, które komentarz `StatusProgressViewModel` odłożył do M3b (*„co pokazać, gdy biegną dwie"*), miało
zatem realny przedmiot.

**Ratyfikowana odpowiedź: jedna operacja naraz, drabinka priorytetów** (użytkownik: *„przeskakiwanie między
zadaniami byłoby mylące, a licznik ukrytych operacji tylko niepotrzebnie komplikowałby UI"*):

| | Stan | Uzasadnienie |
|---|---|---|
| 3 | połączenie / metadane (M3b.2) | dopóki nie skończy, nie działa nic innego |
| 2 | zapytanie **i** skrypt | operacja interaktywna, dopiero co uruchomiona — to jest to, na co użytkownik czeka, i kończy się szybko, oddając sekcję |
| 1 | import | długie tło; ma własną transakcję, własny pasek i własny Cancel w swojej zakładce |

⚠ **Zapytanie i skrypt są JEDNYM szczeblem celowo:** konkurują o linię Data (`RunAsync` odmawia przy otwartej
transakcji), więc nie nakładają się w sposób, dla którego warto wymyślać regułę. ⛔ Reguła dla nieosiągalnego
przypadku byłaby bezczynną gałęzią udającą decyzję projektową — to §15.7 w wydaniu arbitrażowym.

#### §19.31.3 ⭐⭐ JEDEN PISARZ SEKCJI — i to jest cała architektura tej iteracji

M3.1f wołało `Progress.Begin/End` wprost z `OnIsExecutingChanged`, bo źródło było jedno. Przy trzech to
przestaje wystarczać: potrzebny jest punkt, który rozstrzyga, **która** operacja jest widoczna. Od tej
iteracji każde źródło mówi tylko *„przelicz"*, a odpowiedź składa wyłącznie `UpdateProgressSection`.
⛔ `Progress.Begin`/`Report`/`End` nie jest wołane z żadnego innego miejsca — drugi pisarz to drugi właściciel
stanu sekcji.

⭐ **Zakładki NIE dostały referencji do `StatusProgressViewModel`.** Pisarzem został `MainWindowViewModel`,
dokładnie jak przy railu — inaczej dwa VM pisałyby do jednego modelu i arbitraż nie miałby gdzie mieszkać.

⭐⭐ **Seam agregacji nie wymagał budowy, tylko poszerzenia — i przy tym POSZŁA ZA NIM NAZWA.**
`WireRailSource`/`UnwireRailSource`/`_railSources`/`OnRailSourceChanged` → `…ActivitySource`/
`_activitySources`. Powód nie jest kosmetyczny: `RaiseActivityChanged` **od M3.1e nosiło nazwę „aktywność"
właśnie dlatego**, że karmi dwóch konsumentów o różnych rolach; sekcja postępu jest **trzecim**, a resztę
mechanizmu zostawiono przy „rail", czyli przy historii, nie przy odpowiedzialności.
⭐ Ten jeden zbiór subskrypcji jest też tym, co gwarantuje, że **żadne źródło nie przeżyje swojej zakładki**:
zamknięcie zakładki z trwającym importem i rozłączenie (`Reset`, bez `OldItems`) przechodzą tą samą drogą.

⚠ **`RailBrushKey` NIE został tknięty.** Semantyka kolorów railu to M3b.3 — decyzja użytkownika: najpierw
podłączyć wszystkie źródła i zobaczyć rail w realnych scenariuszach, *„jeżeli okaże się, że obecne kolory są
wystarczające, nie ma potrzeby komplikować ich semantyki"*.

#### §19.31.4 ⚠⚠ ROZRÓŻNIENIE „ZMIANA WŁAŚCICIELA" vs „RAPORT TEGO SAMEGO" — i test, który go NIE badał

`Begin` resetuje tryb, procent i komendę; `Report` bez procentu nie rusza ani jednego. Applier musi je
rozróżniać, inaczej zapytanie przejmujące sekcję po skrypcie odziedziczyłoby **pasek stojący na procencie
TAMTEJ operacji**.

⭐⭐ **Pierwsza wersja strażnika przechodziła przy podłożonym naruszeniu — i to jest najważniejsza lekcja
metodologiczna iteracji.** Test gasił skrypt PRZED startem zapytania, więc sekcja przechodziła przez stan
„nic nie trwa", a `End()` resetuje tryb **sam**. Test był zielony z powodu, którego jego nazwa nie opisywała.
Ujawniło to **wyłącznie podłożenie naruszenia** (zapal sekcję bez `Begin`) — bez tego kroku iteracja
zamknęłaby się z pinem, który nie pinuje niczego. To R16 od strony konstrukcji testu: **test zielony przy
złym mechanizmie jest gorszy niż brak testu.**

⭐ Poprawny kształt to **przejście właściciela BEZ PRZERWY**: skrypt biegnie, użytkownik daje F5. Osiągalne
dokładnie dlatego, że drabinka stawia zapytanie nad skryptem. Dopisano też kierunek odwrotny (zapytanie się
kończy, skrypt wciąż trwa → sekcja **nie gaśnie** i wraca do swojego trybu procentowego).

⚠ **Pierwszy plant był ZBYT SZEROKI** i to też warto zapisać: usunięcie `Begin` zabrało razem z trybem
`IsRunning`, więc położyło 7 z 13 testów i nie izolowało tezy. Plant musi kłamać w **jednym** wymiarze —
inaczej nie mówi, który strażnik działa.

#### §19.31.5 Co dostały źródła — trzy właściwości, wszystkie jako LICZBY

`ScriptExecutorTabViewModel`: `CompletedStatementCount` (= sukcesy + porażki) i `RunStatementTotal`,
ustawiane **razem z `_lastStatements`** — rozdzielone mogłyby się rozjechać i mianownik pokazywałby sumę
poprzedniego przebiegu. `DataImportTabViewModel`: `ProgressRowsRead`.

⚠ **Celowo liczby, nie gotowe napisy:** etykietę składa jeden resolver dla wszystkich źródeł, więc format nie
może mieszkać w źródle. Arytmetyka („ile zrobionych") została **przy danych**, żeby pasek statusu nie musiał
wiedzieć, że wykonana instrukcja to „sukces albo porażka, i nic trzeciego".

⭐ **Skrypt jest pierwszym ŻYWYM konsumentem ścieżki procentowej**, która od M3.1f nie miała żadnego — §19.7.2
ostrzegało wprost: *„nie zakładać, że jest sprawdzona"*. Import zostaje trybem nieokreślonym, gdy
`EstimatedRows` nie istnieje; `ProgressRowsRead` liczy się **najbardziej właśnie wtedy**, bo rosnący licznik
jest jedynym dowodem, że coś się posuwa.

#### §19.31.6 ⭐ ETYKIETA NAZYWA OPERACJĘ — i jest krótka Z POMIARU, nie z estetyki

Wymóg użytkownika: przy trzech źródłach etykieta musi jednoznacznie mówić, co jest wykonywane. Trzy nowe
formaty: `StatusProgressQueryRowsFormat`, `StatusProgressScriptFormat`, `StatusProgressImportFormat`
(`ExecutingStatus` = „Executing query…" **już** nazywało operację).

⚠⚠ **Dlaczego każda jest krótka:** pasek statusu ma `ColumnDefinitions="Auto,*,Auto,Auto"`
(`MainWindow.axaml:2095`), więc sekcja 4 rośnie kosztem kolumny gwiazdkowej i **przesuwa chipy stanu w lewo**.
§8.4.6 nadało samemu paskowi stałe 120 px dokładnie z tego powodu; etykieta takiego ograniczenia **nie ma**,
więc ogranicza ją treść. ⛔ Nie dopisywać do niej szczegółu operacji („N read · M written · K failed") —
szczegół należy do powierzchni, która operację prowadzi. To ten sam podział własności, który ratyfikowały
§19.5.1 i §19.7.1: pasek niesie **fakt globalny**, właściciel operacji **szczegół lokalny**.

#### §19.31.7 Strażnicy — 13 przypadków, dwa podłożone naruszenia

`StatusProgressSourcesTests` (partycja **główna** — klasa nie konstruuje kontrolek Avalonii, kryterium
handovera §8). Asercje idą przez **`vm.Progress`, czyli model, który czyta wiązanie**, nie przez wewnętrzny
resolver: przy zmianie właściciela „wybór" i „co sekcja pokazuje" to dwie różne rzeczy (§19.31.4).

| Naruszenie | Złapane przez |
|---|---|
| zapal sekcję bez `Begin` (brak resetu trybu) | `QueryTakingOverFromARunningScript…` + `WhenTheQueryEnds…` — i **tylko** te dwa (11 pozostałych zielonych) |
| odwrócona drabinka (import przed skryptem) | `Script_OutranksImport` + `CancelInTheSection_IsTheOwnersOwnCommand` |

⭐ Drugie naruszenie złapał także strażnik tożsamości komendy — i to jest sensowne: **przy złym właścicielu
sekcja podaje cudzą komendę anulowania.** Ten strażnik asertuje `Assert.Same`, nie „jest jakaś komenda":
§19.7.3 ratyfikowało *„dwa zasięgi tej samej komendy, a nie dwie implementacje"*, a kopia o identycznym
zachowaniu przeszłaby test na równość (pułapka 7).

⚠ **Zdarzył się też fałszywy pomiar warty zapisania:** jeden build z podłożonym naruszeniem zwrócił 1 błąd
(nieosiągalny kod → `TreatWarningsAsErrors`), a testy pobiegły na **starym binarium** i pokazały czerwień
z *poprzedniego* naruszenia. ⭐ Po każdym podłożeniu trzeba sprawdzić `Liczba błędów: 0` **przed** odczytaniem
wyniku testów — inaczej mierzy się artefakt.

#### §19.31.8 ⛔ Czego iteracja świadomie NIE zrobiła

* **Nie tknęła połączenia z bazą** — M3b.2 (osobny krok, decyzja użytkownika).
* **Nie tknęła `RailBrushKey`** — M3b.3.
* **Nie podłączyła eksportu ani batcha** (§19.31.1) i **nie usunęła** ich pasków — precedens §19.7.3:
  pasek statusu **uzupełnia**, nie zastępuje.
* **Nie zmieniła `StatusProgressViewModel`** — jego kontrakt jest ratyfikowany i pinowany 12 testami;
  cała iteracja zmieściła się w warstwie wywołań.
* **Nie zmieniła ani jednej linii XAML** — sekcja 4 wiąże model, więc chroma jest nietknięta.

#### §19.31.9 ⏸ Do QA użytkownika

⚠ Smoke potwierdza wyłącznie, że aplikacja startuje. **Zachowanie wymaga żywej bazy:** czy etykieta nazywa
operację, czy import trwający w tle jest widoczny po przełączeniu zakładki, czy Cancel z paska zatrzymuje
import i skrypt, i czy przejęcie sekcji przez zapytanie w trakcie skryptu czyta się naturalnie.
⏸ **Do sprawdzenia okiem: szerokość etykiety** — czy przy „Running script… 1 234 / 5 678" chipy stanu
przesuwają się na tyle, żeby to przeszkadzało (§19.31.6). Jeśli tak, odpowiedzią jest skrócenie formatu,
nie zmiana układu.

---

### §19.32 Iteracja 20 (M3b.1 A+B+C) — wybór dużego pliku do importu nie blokuje UX (2026-08-04)

> **Zgłoszenie użytkownika:** *„Po wskazaniu dużego pliku `.xlsx` aplikacja na kilka sekund się zamraża, nie
> odświeża UI, a nawet przez chwilę wygląda, jakby zmieniała zoom lub przeskakiwała podczas odmalowywania."*
> **Zakres ratyfikowany jako JEDNA iteracja** — użytkownik odrzucił podział na etapy: *„nie chciałbym robić
> tego w dwóch etapach, gdzie po pierwszym nadal UI będzie się zamrażał, tylko trochę krócej."*
> Build 0/0; suita **7296** w trzech partycjach (7179 + 63 + 54, +12); smoke czysty.
> **Wynik: 17 768 ms → 1 ms** odcinka synchronicznego.

#### §19.32.1 ⚠⚠ PIERWSZY POMIAR ODPOWIEDZIAŁ NA INNE PYTANIE, I UŻYTKOWNIK TO WYCHWYCIŁ

Pierwsza analiza wyceniła **providera** (`ListSheetsAsync`, `ReadSchemaAsync`, tablicę stringów) i była
poprawna co do liczb — ale nie tłumaczyła objawu. Użytkownik odrzucił ją precyzyjnie: *„Mam wrażenie, że
problem leży na granicy pomiędzy zakończeniem OpenFileDialog a pierwszym wyświetleniem podglądu, a nie
w samym odczycie XLSX."*

⭐⭐ **Miał rację, i różnica nie jest niuansem: koszt tłumaczy, dlaczego coś jest WOLNE; nie tłumaczy,
dlaczego UI jest ZABLOKOWANY.** Drugi pomiar dotyczył wątku, nie milisekund — i to on nazwał mechanizm.
⚠ Lekcja metodologiczna: **zmierzenie właściwej rzeczy w niewłaściwym miejscu daje liczby, które wyglądają
na odpowiedź.** Objaw („nie odświeża UI", „przeskakuje przy odmalowywaniu") mówił o wątku UI od początku;
to ja czytałem go jako „wolno".

#### §19.32.2 ⭐⭐ MECHANIZM: CAŁY ŁAŃCUCH BIEGŁ WEWNĄTRZ SETTERA WŁAŚCIWOŚCI

```
BrowseAsync:  Source.FilePath = path          ← zwykłe przypisanie
  └ OnFilePathChanged → RaiseChanged → QueueRecalculate → Recalculate
      └ PendingRecalculation = RunGuardedChainAsync(...)   ← metoda async biegnie INLINE
          ├ clipboard → return · tabele → return           ← oba wracają natychmiast dla pliku
          ├ ReadSourceAsync: ListSheets + detekcja + ReadSchema + podgląd
          └ InferNewTableColumns → Task.Run                ← PIERWSZE oddanie sterowania
```

Metoda `async` biegnie synchronicznie **do pierwszego NIEZAKOŃCZONEGO await**, a
`FileImportSource.OpenStreamAsync`/`OpenTextAsync` zwracają `Task.FromResult(...)` — await na zadaniu już
zakończonym kontynuuje **inline, niezależnie od `ConfigureAwait`**. Zmierzone (`ImportFileOpenProbe`,
identyfikatory wątków przed i po każdym await): **żaden await providera nie zmienia wątku.**

**Dowód na „zablokowany", a nie „wolny":** przed przypisaniem odkładane jest na Dispatcher zadanie
o priorytecie **`Render`**. Nie wykonało się ani razu przez **17 768 ms**; wykonało się w 17 769 ms.
⭐ Okno nie miało **ani jednej okazji na klatkę** — a „przeskakiwanie przy odmalowywaniu" to zachowanie DWM
wobec okna, które przestało pompować komunikaty. ⚠ Tego ostatniego sonda nie mierzy (nie ma okna) i jest to
podane wprost.

#### §19.32.3 ⭐⭐ A — 8,5 s NA PRZECZYTANIE JEDNEGO ATRYBUTU, DWA RAZY NA KAŻDY WYBÓR PLIKU

`RowsFromDimension` chciał wartość `<dimension>`, a robił to przez `worksheetPart.Worksheet` — **akcesor DOM,
który materializuje CAŁY arkusz** do drzewa obiektów, i to **przed** sprawdzeniem, czy element istnieje.
Wołany raz na arkusz w `ListSheetsAsync` i raz na końcu `ReadSchemaAsync`.

| Operacja (300 000 wierszy, 9,2 MB) | Koszt |
|---|---|
| `SpreadsheetDocument.Open` + `Sheets` | 22 ms |
| cała tablica stringów współdzielonych (305 005 pozycji) | 678 ms |
| strumieniowy odczyt 100 wierszy (`OpenXmlReader`) | 22 ms |
| ⛔ **dostęp do `worksheetPart.Worksheet`** | ⛔ **8 546 ms** |
| ✅ **`OpenXmlReader` do `<dimension>`** | ✅ **15 ms** (ta sama wartość `A1:E300001`) |

⭐ **To nie optymalizacja, a naprawa odstępstwa od reguły, którą ta klasa sama deklaruje:** jej doc wymienia
*„SAX not DOM (1)"* jako pierwszą z siedmiu wiążących wytycznych REK‑6 z etapu I0. Jedno miejsce ją po cichu
łamało.

⚠ **Zatrzymanie na `<sheetData>` jest częścią naprawy, nie ozdobą:** bez niego czytelnik przeszedłby przez
wszystkie wiersze pliku BEZ atrybutu, szukając czegoś, czego tam nie ma — zamienilibyśmy jeden drogi
mechanizm na drugi. `<dimension>` poprzedza `<sheetData>` w schemacie, więc dojście do drugiego dowodzi
braku pierwszego. Zmierzone: **13 ms** na pliku bez atrybutu.

**Po samym A: 17 768 → 1 599 ms — i zero klatek nadal.** To jest dokładnie powód, dla którego użytkownik
odrzucił podział na etapy: A bez B to wciąż zablokowany UX, tylko krócej.

#### §19.32.4 ⭐ B — POZA WĄTEK IDZIE TYLKO ODCZYT; KOLEKCJE ZOSTAJĄ NA DISPATCHERZE

Użytkownik postawił warunek: *„żadnego pozornego postępu ani sztucznego `Task.Run`. Przenosimy wyłącznie tę
pracę, która rzeczywiście nie wymaga wątku UI."*

Przeniesione **trzy wywołania providera**: `ListSheetsAsync`, `ReadSchemaAsync` oraz pętla odczytu podglądu.
⛔ Na Dispatcherze zostało wszystko, co dotyka ViewModelu: `Source.ApplyCapabilities`, `_schema`,
`PreviewFields`, `PreviewRows`, `SetStatus`, `PreviewSchemaChanged`.

⭐⭐ **To nie jest nowy wzorzec — to wzorzec, który ten plik już stosuje.**
`InferNewTableColumnsAsync` i `RefreshConvertedPreviewAsync` **od dawna** robią „czytaj poza wątkiem,
publikuj na wątku"; `ReadSourceAsync` był jedynym drogim ogniwem, które zostało poza nim. Stąd
`LoadPreviewAsync` zbiera ograniczoną głowę do zwykłej listy i **potem** publikuje: wcześniej `await foreach`
przeplatał odczyt z `PreviewRows.Add`, więc **kolekcja przypinała odczyt do wątku UI**.

⚠ **Detekcja kodowania i separatora NIE została przeniesiona, świadomie:** czyta ograniczoną próbkę (64 KB),
zmierzone 1–3 ms, i dotyczy wyłącznie plików rozdzielanych, których cała ścieżka to ≤ 32 ms. Przenoszenie
pracy, której koszt nie został zmierzony jako problem, byłoby dokładnie tym „sztucznym `Task.Run`".

#### §19.32.5 C — JEDEN SYGNAŁ NA CAŁĄ DŁUGOŚĆ ŁAŃCUCHA

⚠⚠ **Nie `IsBusy`, i to jest istotne.** `IsBusy` obejmuje wyłącznie `ReadSourceAsync`, a łańcuch ma jeszcze
inferencję typów i podgląd po konwersji, **każde z własną flagą**. Pasek wiązany z `IsBusy` **gasłby
i zapalał się w trakcie jednej operacji** — migotanie zamiast informacji. Nowe `IsRecalculating` podnosi się
w `Recalculate` i gaśnie w **jednym** miejscu: `finally` osłony łańcucha, przez które przechodzi każde
wyjście (sukces, błąd, anulowanie). To ten sam wybór, co `OnIsExecutingChanged` w §19.7.4.

⚠⚠ **Gaszenie jest WARUNKOWE i bez tego byłby defekt:** wyprzedzony łańcuch kończy się **po** starcie
następnego (anulowanie nie jest natychmiastowe), więc bezwarunkowe `false` zgasiłoby pasek dla operacji,
która właśnie się rozpoczęła — objaw: przy szybkiej zmianie ustawień pasek znika, choć praca trwa.
Porównanie po referencji CTS-a odpowiada na pytanie *„czy to nadal moja tura"*.

⭐ **Dwie etykiety, nie jedna:** to samo ogniwo obsługuje schowek, więc „Loading file…" nad odczytem schowka
byłoby nieprawdą — a kłamiąca etykieta jest nieodróżnialna od awarii (gotcha #311). Stąd
`StatusProgressImportReadingFile` / `…ReadingClipboard`, jeden warunek.
⚠ **Bez licznika i bez procentu** — ten odcinek nie zna żadnej sumy. ⚠ **Bez przycisku anulowania**: łańcuch
ma własny CTS, ale użytkownik nie ma dla niego przycisku, a wymyślenie go byłoby dodaniem funkcji pod
pozorem podłączenia postępu.

#### §19.32.6 Wynik

| | odcinek synchroniczny `Source.FilePath = path` | werdykt |
|---|---|---|
| przed | **17 768 ms** | ⛔ ani jednej klatki |
| po A | 1 599 ms | ⛔ ani jednej klatki |
| **po A+B+C** | ✅ **1 ms** | ✅ mieści się w budżecie klatki |

CSV bez zmian i bez problemu: **27–32 ms** (schemat czyta próbkę 200 rekordów, `EstimatedRows` świadomie
`null`). ⭐ Gdyby użytkownik zgłosił zamrożenie na CSV, przyczyna leżałaby gdzie indziej.

#### §19.32.7 Strażnicy — i granica tego, co suita umie ocenić

⚠⚠ **`XlsxImportProvider` NIE MIAŁ ANI JEDNEGO TESTU JEDNOSTKOWEGO** — był weryfikowany wyłącznie sondami
na żywo. Dlatego poprawka A dostała pierwsze cztery (`XlsxDimensionReadTests`): zadeklarowany `<dimension>`
raportowany jako szacunek, jego brak jako `null` (nigdy liczba zmyślona), zgodność `ListSheets` z `ReadSchema`.

| Naruszenie | Złapane przez |
|---|---|
| powrót do `worksheetPart.Worksheet` | `RowsFromDimension_UsesTheSaxReader_NeverTheWorksheetDom` — i **tylko** on; trzy testy behawioralne pozostały zielone |
| podgląd przestaje się ograniczać | `Preview_StopsAtItsBound_EvenWhenTheFileIsLonger` |
| bezwarunkowe gaszenie sygnału | `Recalculating_SurvivesBeingSuperseded_ByANewerChange` |

⭐⭐ **Pierwszy wiersz jest lekcją o granicach testów behawioralnych:** DOM i SAX zwracają **tę samą
wartość**, więc żadna asercja na wyniku nie odróżni 15 ms od 8 546 ms. Jedyne, co je rozróżnia, to
**mechanizm** — dlatego ten jeden strażnik czyta ŹRÓDŁO, i dlatego jest tu uzasadniony, a nie leniwy.

⚠⚠ **Czego suita NIE dowodzi, podane wprost:** że praca zeszła z wątku UI. Nie ma tu okna ani pętli
Dispatchera, a asercja na czasie byłaby testem psującym się z powodów niezwiązanych ze swoim przedmiotem
(R16). Ten dowód daje **sonda**, i to jest jej trwała rola.

#### §19.32.8 ⚠ Trzy potknięcia własne, warte zapisania

1. **Pierwsza wersja sondy generowała plik z *inline strings***, czyli bez tablicy stringów współdzielonych —
   mierzyłaby kształt pliku, którego użytkownik nie ma, i podałaby koszt tablicy jako zero.
2. **Mój plik nie miał `<dimension>`**, więc pomiar mógł dotyczyć wyłącznie ścieżki awaryjnej. Sprawdzone na
   drugim pliku, **z** atrybutem: koszt identyczny, bo dostęp do DOM wyprzedza sprawdzenie atrybutu.
   ⭐ Bez tego kroku wniosek nie generalizowałby się na pliki z Excela.
3. ⭐⭐ **Sonda zawisła na `await Task.Delay` po `SetupWithoutStarting()`** — bieżący wątek JEST wtedy wątkiem
   UI Avalonii, więc kontynuacja poszła na Dispatcher, którego nikt nie pompuje. **To ta sama pułapka, którą
   sonda mierzy, o poziom wyżej.** Zastąpione synchronicznym `Sleep`, z powodem w miejscu.
4. ⚠ **Wypis sondy po naprawie kłamał:** „czy okno dostało klatkę w trakcie settera? NIE" jest prawdą
   i zupełnie myląco przy odcinku 1 ms — klatka nie była potrzebna. Zamienione na **werdykt** z budżetem
   klatki. **Log, który da się przeczytać jako porażkę, jest gorszy niż brak logu.**

#### §19.32.9 ⛔ Czego iteracja NIE zrobiła

* **Nie tknęła `.xls`** (`ExcelDataReader`) — z lektury jego ścieżka jest ograniczona próbką i bierze
  `RowCount` z deklaracji BIFF, bez DOM, ale **nie zmierzyłem tego** (brak dużego pliku `.xls`).
  ⛔ Nie zgaduję; zapisane jako niezmierzone.
* **Nie przeniosła detekcji** (§19.32.4) ani niczego, czego koszt nie został zmierzony.
* **Nie dodała przycisku anulowania** odczytu źródła.
* **Nie tknęła `ImportPipeline`, mapowania, konwertera ani raportu** — architektura modułu (🔒 zamrożona)
  jest nietknięta; zmiany są w providerze, w łańcuchu przeliczania i w resolverze paska statusu.
* **Nie tknęła `StatusProgressViewModel`** ani XAML-a.

#### §19.32.10 ⏸ Do QA użytkownika

Wybór dużego `.xlsx`: okno pozostaje responsywne, w pasku statusu od razu **„Loading file…"**, podgląd
dochodzi po chwili. ⏸ Do sprawdzenia okiem, czy przy szybkiej zmianie ustawień źródła (separator, kodowanie)
etykieta nie migocze — mechanizm jest osłonięty i pinowany testem, ale ⭐ kryterium odbioru jest ekran.
