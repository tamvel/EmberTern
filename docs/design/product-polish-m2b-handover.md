# Product Polish — M2b — dokument startowy sesji

> ## ✅ M2b — 18 ITERACJI DOSTARCZONYCH (2026-08-02), z rundą proporcji po QA
>
> Kroki 0–5.3 odebrane przez użytkownika; **kroki 5.4–10 oczekują QA wizualnego**.
> Po QA następny etap to **M2c** (sweep de‑lokalizacyjny) — ⛔ **nie zaczynać przed QA.**
>
> Ten dokument zachowany jako **punkt wejścia w M2b i zapis jego architektury**; sekcja §5
> („następna iteracja") jest już historyczna. Stan faktyczny: `product-polish.md` §15.-1.

> **To jest prompt dla Claude'a, nie dla użytkownika.** Punkt wejścia w każdą kolejną sesję M2b —
> pozwala wejść w implementację bez ponownego czytania całej dokumentacji.
>
> **Przeczytaj TEN plik + `docs/design/product-polish.md` §16 (wzorzec `FluentBridge`) i §15.-1
> (tablica stanu).** Reszty §15 nie czytaj na starcie — sięgaj po konkretną sekcję, gdy iteracja jej
> dotyczy. §8 (powierzchnie trwałe) to M3, nie czytaj.
>
> ⛔ `product-polish-m2a-handover.md` jest **zamknięty**. Jego §6 opisuje M2b jednym wierszem
> napisanym, zanim M2b istniał — nie planuj z niego.

---

## 1. Gdzie jesteśmy

| | |
|---|---|
| **Branch** | `feat/product-polish` |
| **Etap** | **M0 ✅ · M1 ✅ · M2a ✅ · M2b ✅ dostarczony** (18 iteracji) |
| **Następny krok** | ⭐ **QA wizualne kroków 5.4–10**, potem **M2c** |
| **Ostatni commit** | `e0a59fd` — M2b kroki 8–10 (runda proporcji po QA) |
| **Baseline** | build 0/0; suite **7084** (7000 + 54 + 30); smoke czysty |

**Specyfikacja etapu (źródło prawdy):** `C:\Users\grzegorz.gronski\Desktop\Product Polish.mdown`
**Dokument etapu:** `docs/design/product-polish.md`

---

## 2. Co zostało dostarczone (nie powtarzać, nie cofać)

| # | Krok | Commit | Co wnosi |
|---|---|---|---|
| 0 | style klasowe czytają katalog | `0bbc745` | dowód, że warstwa tokenów rozwija się w runtime; **bajtowo neutralny** |
| 1 | **`CheckBox`** (RB‑2) | `26243cb` | własny `ControlTheme`, znak 14 px, brak `MinHeight`, `Margin.MarkGap` |
| 2 | **RB‑4** | `a1d607a` | `ElevatedPanelBrush` → `ChromeStrongBrush` + nowy `SurfaceRaisedBrush` (14/14) |
| 3 | **skala Light** | `7975aaa` | `#FCFCFD` + nowa szarość + H‑7; **V‑1 zmierzone** |
| 4 | **`ToolTip`** | `e5b010f` | pierwszy nowy konsument `SurfaceRaised`; styl, nie szablon |
| 5.1 | **`RadioButton`** | `cf23a4c` | rodzeństwo `CheckBoxa`; `Stroke.Hairline`, drugi konsument `MarkGap` |
| 5.1a | koncentryczność kropki | `60f9278` | `UseLayoutRounding=False` na `PART_MarkArea` |
| — | ⭐ **`FluentBridge`** | (w `9ec2c13`) | **decyzja architektoniczna — §16** |
| 5.2 | **`TextBox`** | `9ec2c13` | 32 → 24 px, tekst 14 → 12; pierwsza kontrolka na moście |
| 5.3 | **`ComboBox`** | `3483296` | most zdał próbę skalowania — **zero własnych szablonów** |
| 5.4 | **`Button`** (H‑8) | `267a4b8` | + tokenizacja 4 wariantów; kolejność w pliku jest znacząca |
| 5.5 | **`NumericUpDown`** | `d2a2475` | trzy kontrolki zagnieżdżone; most działa kompozycyjnie |
| 5.6 | **`ToggleButton`** | `ce47aa7` | selektor typu = typ DOKŁADNY; stan checked był już nasz |
| 5.7 | **`Expander`** | `69ceff6` | ⭐ **alias zasobu — trzecia trasa, korekta §16.3** |
| 6 | **`ScrollBar`** (H‑10) | `7ab3d27` | biały uchwyt na białym tle w Light |
| 7 | **DataGrid Standard** (§8.4) | `e95913b` | + `Pad.CellEditor`; test znalazł defekt z kroku 5.2 |
| 8–10 | ⭐ **runda proporcji po QA** | `e0a59fd` | dwie drabiny wysokości · Ustawienia jako panel referencyjny · kolumny Data Import |

Kroki 0–5.3 odebrane przez użytkownika; **5.4–10 oczekują QA**.
Szczegóły: `product-polish.md` §15.1–§15.9.

---

## 3. ⭐⭐ Architektura, którą przyjęliśmy w trakcie — przeczytaj przed pierwszą linią kodu

### 3.1 `FluentBridge` — wzorzec projektu (pełna definicja: `product-polish.md` §16)

> **Nie przestylowujemy Fluenta i nie kopiujemy jego szablonów — PRZEPINAMY GO NA NASZ KATALOG.**

Fluent maluje wnętrza kontrolek z **własnych zasobów nazwanych**; `Themes/FluentBridge.axaml`
podmienia te zasoby na nasze tokeny. Zachowujemy zachowanie frameworka, wygląd bierzemy z katalogu.

**⛔ Bridge nie jest drugim katalogiem tokenów** (reguła użytkownika, zapięta testem
`FluentBridge_ContainsNoLocalValues`): wyłącznie mapowanie, żadnych wartości lokalnych, żadnych
nowych decyzji projektowych.

**⚠⚠ TRZY TRASY, podział wymuszony pomiarem, nie upodobaniem** (pełne uzasadnienie: §16.3):

| Co | Gdzie |
|---|---|
| **metryki** — `MinHeight`, `Padding`, `FontSize`, `BorderThickness` | **setter stylu** w `ControlStyles.axaml`, czytający token — **trasa domyślna** |
| **kolory malowane przez wnętrze szablonu** | **`FluentBridge.axaml`**, `Color="{StaticResource …Color}"` |
| ⭐ **metryka lub barwa, którą szablon trzyma jako WARTOŚĆ LOKALNĄ** | **`FluentBridge.axaml`**, `<StaticResource x:Key="…" ResourceKey="…" />` |
| **wartości, w których Fluent już się z nami zgadza** | **nigdzie** — pinowane testem |

⚠ **Korekta z kroku 5.7:** §16.3 twierdziła pierwotnie, że *„XAML nie potrafi zaaliasować zasobu
skalarnego"*. **Zmierzone: potrafi.** Nie potrafi go **ZŁOŻYĆ** w treści elementu (`<x:Double>` musi
zawierać liczbę). Trasa trzecia jest **wyjątkiem, nie alternatywą** — sięgaj po nią dopiero wtedy, gdy
setter **zmierzalnie przegrał** (wartość lokalna outranks setter). Zmierzone przypadki:
`ExpanderMinHeight`, `ScrollBarThumbBackgroundColor`.

⚠⚠ **Styl typu sięga do CUDZEGO szablonu — sprawdzaj to zawsze.** Krok 5.5 dostał dzięki temu wysokość
wewnętrznego `TextBoxa` `NumericUpDown` za darmo; krok 5.6 tym samym mechanizmem **wyśrodkował
nagłówek `Expandera`**. Jeden mechanizm, dwa znaki.

### 3.2 ⛔ Kiedy WOLNO napisać własny `ControlTemplate` — dwa warunki, oba konieczne

1. rozmiar **nie jest wystawiony jako zasób** Fluenta, **oraz**
2. element do zmiany **nie ma `x:Name`** (selektor trafiałby pozycyjnie i po cichu zmienił cel przy
   aktualizacji Avalonii).

**Spełniły je dokładnie dwie kontrolki: `CheckBox` i `RadioButton`.** ⛔ Nie przepisuj szablonu,
bo „i tak już mamy dwa".

### 3.3 Kolejność wczytywania (`App.axaml`) — dwie pozycje są wymuszone

```
Tokens → Typography → Colors → FluentBridge → IconGeometries → ControlThemes → SearchableComboBox → PickerTemplates
```

`FluentBridge` **po** `Colors` (mapuje przez `StaticResource …Color`); `ControlThemes` **po**
`IconGeometries` (szablon sięga po `{StaticResource Icon.Check}`, a `StaticResource` rozwiązuje się
przy wczytywaniu).

---

## 4. Zasady odbioru — obowiązują każdą iterację

1. **⭐ Zasada nadrzędna M2b (§15.0):** *„Projektujemy kontrolki, na których programista będzie
   komfortowo pracował przez 8 godzin dziennie."* Katalog nie ma wygrać z jakością produktu —
   wartość technicznie poprawna, a w praktyce gorsza, **zatrzymujemy i zgłaszamy** (§4.2.4).
2. **⭐ Komplet stanów, oba motywy** (§15.2.1): normal · hover · aktywny/checked · indeterminate ·
   disabled · focus. Kontrolka z dobrym stanem spoczynkowym **nie jest gotowa**.
3. **⭐ Nowa ROLA powstaje z użycia w kilku komponentach, nie z jednego przypadku.** Dlatego
   `Radius.Control` **nie istnieje**, a `Margin.MarkGap` tak (drugi konsument był znany od razu).
4. **Jedna iteracja = jeden commit**, po nim build 0/0, trzy partycje, smoke, uruchomienie aplikacji
   i ocena użytkownika **zanim ruszy następna**.
5. ⚠ **M2b pracuje w `Themes/` — z JEDNYM wyjątkiem, który sam ma granicę.** Do kroku 7 włącznie
   etap nie dotknął ani jednego widoku. Runda proporcji (kroki 8–10) dotknęła czterech
   (`MainWindow` — klasa `toolbar`; `SettingsWindow` i `GlobalSearchDialog` — klasa `search`;
   `DataImportTabView` — proporcje kolumn) plus jeden konwerter.
   ⭐ **To NIE jest wejście w M2c i licznik to potwierdza:** M2c usuwa **wartości lokalne**
   (`FontSize`, `FontFamily`, `CornerRadius`), a te zmiany żadnej nie dodają — dwie *usuwają*
   (`Padding="10,4"`, `Padding="8,4"` z paska), reszta to nadanie klasy albo układ kolumn.
   `DesignTokenComplianceTests` przeszedł bez zmiany bazy. **Jeżeli licznik drgnie — to jest M2c.**

---

## 5. ⛔ Co dalej — i czego NIE robić

**M2b jest dostarczony w całości.** Kolejność: **QA wizualne kroków 5.4–10 → dopiero potem M2c.**

⛔ **Nie zaczynać M2c przed QA.** M2c usuwa wartości lokalne z **widoków**; jeżeli QA odrzuci którąś
decyzję kroków 5.4–10, poprawka wróci do `Themes/` — a sweep zrobiony wcześniej trzeba by powtórzyć
na zmienionej podstawie. To jest ta sama zasada, którą §15.6.4a nazwał dla zgłoszeń użytkownika:
⛔ **nie strojić do stanu przejściowego.**

**Cztery rzeczy, które trzeba przy QA podnieść samemu, bo każda jest DECYZJĄ, nie pominięciem:**

1. ⛔ **`ScrollBar` — strzałki i geometria celowo nietknięte** (§15.7). Usunięcie strzałek to zmiana
   *funkcjonalna*, a Fluent i tak ukrywa je do najechania. Gdyby mimo to przeszkadzały — osobna
   propozycja, nie doklejka.
2. ⛔ **Kolor komentarzy SQL zostaje** (V‑1, ratyfikowane) — wraca po etapie, w normalnej pracy.
3. 📌 **Nasycenie zaznaczonego wiersza** — odłożone do kroku DataGrid, czyli **teraz jest właściwy
   moment, żeby to ocenić** (siatka jest już docelowa).
5. ⭐ **Drabina AKCJI (22 / 26 / 28) jest nowa i jest do oceny w pierwszej kolejności** — to jedyna
   decyzja tej rundy, w której liczby są moje, a nie wynikające z pomiaru (§15.9.1).
4. 📌 **Badge DEV MODE** — odłożony do **M3.2**, nie do tego QA.

⚠ **Dwa widoki niosą już zbędne obejście** (`ProcedureDetailTabView`, `FunctionDetailTabView`:
`MinHeight="26"` + lokalne zasoby paddingu `Expandera`). Po kroku 5.7 są niepotrzebne — ale to
**M2c**, bo leżą w widoku, a M2b pracuje wyłącznie w `Themes/`.

## 6. Procedura iteracji (`product-polish.md` §16.5)

1. **Sonda headless** — drzewo szablonu + zmierzone `MinHeight` / `Padding` / `FontSize`.
   ⚠ Nie ufaj samej właściwości.
2. **Sprawdź, czy Fluent wystawia potrzebne pokrętła jako zasoby.** Jeżeli tak — §3.2 nie jest
   spełnione i własny szablon jest niedozwolony.
3. **Metryki → setter stylu; kolory → Bridge**, w **obu** motywach.
4. **Test dwutorowy** — metryka odczytana z kontrolki, kolor z części, która faktycznie maluje.
   ⚠ Asercja koloru z samej kontrolki przechodzi, malując po cichu nic.
5. **Sprawdź wariant „w komórce siatki".**
6. **Uruchom aplikację, oceń w komplecie stanów, w obu motywach.**
7. Build → trzy partycje → smoke → commit → QA użytkownika.
8. **Dopisz sekcję as-built do `product-polish.md` §15 i wiersz do tablicy §15.-1 w TYM SAMYM
   commicie** — tak prowadzone są wszystkie dotychczasowe iteracje i to jedyny powód, dla którego
   ten dokument dało się odtworzyć.

---

## 7. Znane ograniczenia i sprawy otwarte — NIE naprawiać przy okazji

| # | Sprawa | Status |
|---|---|---|
| **V‑1** | komentarz SQL `#2E8B57` = **4,14:1** na nowym tle, próg 4,5. §7.3 („zmień tło, nie paletę") **niewykonalne** — na czystej bieli wychodzi 4,25 | ⛔ **kolor ZOSTAJE** (decyzja użytkownika); wraca po etapie, oceniany w normalnej pracy |
| **R‑6 / DPI** | `LayoutTransformControl` **nie symuluje DPI**; `CaptureRenderedFrame()` zwraca `null` (`UseHeadlessDrawing`) | ⚠ weryfikacja 150% **tylko okiem** — nie raportować jako sprawdzone testem |
| **`Radius.Control`** | promień 3 na kwadracie 14 px jest proporcjonalnie większy niż u Fluenta | pytanie wraca **po wszystkich kontrolkach bazowych**, nie wcześniej |
| **nasycenie zaznaczonego wiersza** | zgłoszone przy QA kroku 1 | 📌 **krok DataGrid**, nie wcześniej |
| **badge DEV MODE** | „po uspokojeniu kontrolek jeszcze bardziej rzuca się w oczy" | 📌 **M3.2 (Toolbar)** |
| ⭐ **drabina AKCJI 22 / 26 / 28** | jedyna decyzja rundy 8–10, w której **liczby są moje**, a nie wynikają z pomiaru | ⏳ **pierwsza do oceny przy QA** (§15.9.1) |
| **ocena Application Chrome** | użytkownik sam odmówił zamykania oceny na fragmencie | ⛔ **brama §13.3, po M3** |

**⭐⭐ Wspólny mianownik czterech ostatnich wierszy — zjawisko nazwane w §15.6.4a: uspokojenie
otoczenia PODNOSI głośność wszystkiego, czego jeszcze nie dotknęliśmy.** Element zgłoszony jako
„za głośny" po kolejnym kroku **nie musi być defektem tego kroku** — bywa długiem, który dopiero stał
się widoczny. ⛔ Poprawka wykonana na wpół uspokojonym otoczeniu jest strojeniem do stanu
przejściowego. Odkładanie takich zgłoszeń **nie jest odsuwaniem pracy** — to jedyny moment, w którym
da się ją ocenić uczciwie.

---

## 8. Pułapki warsztatowe — zapłacone, nie powtarzać

- ⚠⚠ **Nigdy nie łącz `dotnet build` i `dotnet test` w jednym poleceniu** — deadlock, użytkownik
  musi przerywać. Osobne wywołania.
- ⚠⚠ **Trzy partycje testów** (`CLAUDE.md` „Tests"): główna, potem `ConnectionExpandBindingProbe`
  **sam**, potem pozostałe cztery klasy headless razem.
- ⚠ **Test headless dołącza do `HeadlessCollection`**, nigdy własny `IClassFixture`
  (#94/#226/#286), i ⛔ **nie konstruuje `MainWindow`** (udokumentowany kształt zawieszający suite).
- ⚠ **`TryFindResource(key, out …)` NIE WIDZI zasobów z `ThemeDictionaries`** — to granica między
  `Tokens`/`Typography` (jedna wartość) a `Colors` (wartość na motyw). Dwie ścieżki wyszukiwania.
- ⚠ **Test różnicy `SurfaceRaised` vs `ChromeStrong` musi być pisany w motywie JASNYM** — w ciemnym
  mają celowo tę samą wartość, więc asercja przechodzi niezależnie od poprawności podpięcia.
  Wariant przywracaj w `finally` (sesja headless jest wspólna).
- ⚠ **`<ResourceDictionary x:Key="Dark">` nazywa ZAKRES MOTYWU, nie zasób.** Dwa moje strażniki
  policzyły go jako klucz i zgłosiły fałszywy alarm. **Ciąg w kształcie klucza nie jest kluczem.**
- ⚠ **Cofanie zasadzonego naruszenia przez `git checkout -- <plik>` kasuje niezacommitowaną pracę
  w tym samym pliku.** Plant cofa się z kopii, nie z gita.
- ⚠ **Zabij `EmberTern.exe` przed przebudową** — blokuje DLL‑e, MSB3021.

---

## 9. ⭐⭐ Reguła prowadząca — przed każdą decyzją implementacyjną

> **Użytkownik, 2026-08-01:** *„Dokument ma prowadzić produkt. Nie produkt dokument."*

Jeżeli znajdziesz rozwiązanie wyraźnie lepsze od zapisanego:

```
1. NIE implementuj gorszego, bo było opisane wcześniej.
2. NIE implementuj lepszego po cichu.
3. Propozycja + uzasadnienie → akceptacja → aktualizacja dokumentu → implementacja.
```

⭐ **`FluentBridge` powstał dokładnie tą ścieżką** — plan zakładał własne szablony dla wszystkich
kontrolek bazowych; sonda `TextBoxa` pokazała coś lepszego, propozycja poszła do użytkownika **przed**
implementacją, została ratyfikowana i **zmieniła architekturę całego M2b**. To jest wzorzec
postępowania, nie wyjątek.

⛔ **Wyjątek: decyzje D1–D12 i wymagania specyfikacji** zmienia wyłącznie użytkownik.
