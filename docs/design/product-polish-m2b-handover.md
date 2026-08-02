# Product Polish — M2b — dokument startowy sesji

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
| **Etap** | **M0 ✅ · M1 ✅ · M2a ✅ · ⏳ M2b W TOKU** — 9 iteracji dostarczonych i odebranych |
| **Następny krok** | **`Button`** (§5 niżej) |
| **Ostatni commit** | `3483296` — M2b krok 5.3 (`ComboBox`) |
| **Baseline** | build 0/0; suite **7075** (7000 + 54 + 21); smoke czysty |

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

Wszystkie odebrane przez użytkownika. Szczegóły: `product-polish.md` §15.1–§15.6.5a.

---

## 3. ⭐⭐ Architektura, którą przyjęliśmy w trakcie — przeczytaj przed pierwszą linią kodu

### 3.1 `FluentBridge` — wzorzec projektu (pełna definicja: `product-polish.md` §16)

> **Nie przestylowujemy Fluenta i nie kopiujemy jego szablonów — PRZEPINAMY GO NA NASZ KATALOG.**

Fluent maluje wnętrza kontrolek z **własnych zasobów nazwanych**; `Themes/FluentBridge.axaml`
podmienia te zasoby na nasze tokeny. Zachowujemy zachowanie frameworka, wygląd bierzemy z katalogu.

**⛔ Bridge nie jest drugim katalogiem tokenów** (reguła użytkownika, zapięta testem
`FluentBridge_ContainsNoLocalValues`): wyłącznie mapowanie, żadnych wartości lokalnych, żadnych
nowych decyzji projektowych.

**⚠⚠ Podział wymuszony pomiarem, nie upodobaniem — XAML nie potrafi zaaliasować zasobu skalarnego:**

| Co | Gdzie |
|---|---|
| **metryki** — `MinHeight`, `Padding`, `FontSize`, `BorderThickness` | **setter stylu** w `ControlStyles.axaml`, czytający token |
| **kolory malowane przez wnętrze szablonu** | **`FluentBridge.axaml`**, `Color="{StaticResource …Color}"` |
| **wartości, w których Fluent już się z nami zgadza** | **nigdzie** — pinowane testem |

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
5. ⚠ **M2b pracuje wyłącznie w `Themes/`.** `DesignTokenComplianceTests` mierzy `Views/`+`Controls/`
   i jest w tym etapie **czujnikiem zakresu** — drgnięcie licznika oznacza wejście w M2c.

---

## 5. ⭐ Następna iteracja — `Button`

**Dlaczego on:** to ostatnia kontrolka bazowa o dużym zasięgu, a jednocześnie pierwsza, w której
wchodzi w grę **wysokość akcji głównej 28** (D1) obok standardowej 24 — czyli pierwsza, która
sprawdzi, czy `Size.Control` / `Size.Action` są dwiema rolami, czy jedną z wyjątkiem.

**Stan wyjściowy (zmierzony, §15.6):** `MinHeight` **0** · żądana wysokość **29** · `Padding`
`8,5,8,6` · `FontSize` **14**. ⚠ Jak `RadioButton` — deklarowana właściwość nie mówi prawdy.

**Zasoby, które Fluent prawdopodobnie wystawia** (zmierzone przy okazji kroku 5.2, do potwierdzenia
sondą): `ButtonBackground`, `ButtonBorderBrush`, `ButtonForeground`, `ButtonPadding` (`8,5,8,6`).
Jeżeli tak — **własny szablon jest niedozwolony** (§3.2 wyżej).

**⚠ Co odróżnia `Button` od poprzednich iteracji i wymaga decyzji, nie rutyny:**
- w `ControlStyles.axaml` istnieją już **cztery świadomie zaprojektowane warianty** —
  `Button.icon`, `Button.primary`, `Button.flat`, `Button.caption`. **Zmiana stylu bazowego
  wchodzi pod nie wszystkie.** Sprawdź każdy z osobna, zanim uznasz iterację za zamkniętą;
- **`Button.icon`** to kwadratowy cel na ikonę — wysokość formularza może być dla niego zła
  dokładnie tak, jak `MinHeight=24` było złe dla edytora w komórce (§15.6.4);
- **przyciski na pasku tytułu i w stopkach dialogów** mają własne oczekiwania co do wysokości;
- ⚠ **`Button` w komórce siatki** — ten sam test co przy `TextBoxie`: `Size.Row.Grid` (22) −
  `Pad.Cell` (3+3) = **16 px**.

### Kolejność po `Button`

`NumericUpDown` → `ToggleButton` → `Expander` → **DataGrid** → **`ScrollBar` (ostatni)**.
Dopiero po zamknięciu całego M2b można rozpocząć **M2c** (sweep de‑lokalizacyjny).

---

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
