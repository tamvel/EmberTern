# Product Polish — M2a — dokument startowy sesji

> **To jest prompt dla Claude'a, nie dla użytkownika.** Punkt wejścia w nową sesję —
> pozwala wejść w implementację bez ponownego czytania całej dokumentacji.
>
> **Przeczytaj TEN plik + `docs/design/product-polish.md` §3, §4, §5, §6, §11.**
> Reszty product-polish.md **nie czytaj na starcie** — §7 (kolory) potrzebna dopiero w M2b,
> §8 (powierzchnie trwałe) w M3.

---

## 1. Gdzie jesteśmy

| | |
|---|---|
| **Branch** | `feat/product-polish` (utworzony z `master`, commit `992fe31`) |
| **Etap** | Product Polish — **M0 ✅ zamknięty, M1 ✅ zaakceptowany przez użytkownika** |
| **Następny krok** | **M2a** — infrastruktura tokenów. Nic więcej. |
| **Stan kodu** | **nietknięty** — dotąd powstał wyłącznie dokument projektowy |
| **Baseline** | build 0/0; suite **7057** zielony w trzech partycjach; smoke czysty |

**Specyfikacja etapu (źródło prawdy):** `C:\Users\grzegorz.gronski\Desktop\Product Polish.mdown`
**Dokument etapu:** `docs/design/product-polish.md`

---

## 2. Co zostało ustalone (nie re-analizować)

### 2.1 Decyzje ratyfikowane przez użytkownika — D1–D12

Pełna lista w `product-polish.md` §2. **Najważniejsze dla M2a:**

| # | Decyzja |
|---|---|
| **D1** | wysokości kontrolek: standard **24**, siatki **22**, akcja główna **28** |
| **D2** | czcionka monospace: **Cascadia Mono** (bez ligatur), jeden token |
| **D12** | dodatki z audytu **nie mogą** ograniczyć żadnego pierwotnego założenia specyfikacji |

Q6 (semantyka kolorów) i Q7 (`#FCFCFD`) — **rozstrzygnięte**, dotyczą M2b, nie M2a.

### 2.2 Zasady nadrzędne

- **§0.1 Persistent UI** — Status Bar, Toolbar, pasek zakładek, Metadata Explorer,
  DataGrid, kontrolki bazowe, menu kontekstowe mają pierwszeństwo przed ekranami
  otwieranymi sporadycznie. Rozstrzyga **kolejność i nakład, nie zakres**.
- **§0.1.1 Tokeny są środkiem, nie celem** — zgodność techniczna jest warunkiem
  koniecznym, **nie wystarczającym**. ⛔ Nigdy nie raportować etapu jako gotowego
  na podstawie samych zielonych testów.
- **§0.1.2 Application Chrome to JEDNA powierzchnia** — dotyczy M3, nie M2a.

### 2.3 Trzy zasady katalogu (product-polish.md §3) — obowiązują od pierwszej linii M2a

1. **Token nazywa ROLĘ, nigdy wartość.** `Pad.Control`, nie `Pad.8x3`.
2. **Avalonia nie liczy w XAML** → dwie warstwy: skalarna (`x:Double`) + złożona
   (`Thickness`/`CornerRadius`, nazwana rolą). Warstwa złożona jest **zamknięta**.
3. **Rola może dzielić wartość z inną rolą i to jest poprawne.** `Text.Compact`,
   `Text.Grid` i `Text.Status` mają wszystkie 11 px. ⛔ **Nie zwijać ich w jedną.**

---

## 3. Zakres M2a — dokładnie to i nic więcej

> **M2a jest WYŁĄCZNIE ADDYTYWNE. Po jego zakończeniu aplikacja wygląda IDENTYCZNIE.**
> Jeżeli cokolwiek zmieniło się wizualnie — zakres został przekroczony.

### 3.1 Co powstaje

| # | Artefakt | Zawartość |
|---|---|---|
| 1 | **`Themes/Tokens.axaml`** | skala odstępów (§4), role `Thickness` (§4.1), wysokości (§5), ikony, promienie, grubości |
| 2 | **`Themes/Typography.axaml`** | 12 ról typograficznych (§6) + `Font.Ui` / `Font.Code` (§6.1) |
| 3 | **`App.axaml`** | rejestracja obu słowników w `MergedDictionaries` |
| 4 | **`DesignTokenComplianceTests`** | test strażniczy (§11) — **z listą wyjątków w stanie „wszystko jest wyjątkiem"** |

### 3.2 Czego M2a NIE robi

⛔ **Żadnego implicit style dla kontrolki.** `TextBox`, `ComboBox`, `CheckBox`, `Button`,
`ScrollBar`, `ToolTip`, `DataGrid` — wszystkie zostają na Fluent. To jest **M2b**.
⛔ **Żadnej zmiany w `Colors.axaml`.** Powierzchnie i kolory (RB‑4, `SurfaceRaisedBrush`,
skala szarości Light, `#FCFCFD`) to **M2b**.
⛔ **Żadnego usuwania wartości lokalnych.** To jest **M2c**.
⛔ **Żadnego dotykania palety składni edytora** — zamrożona (§6.3).
⛔ **Żadnego `ControlTemplate`.**

### 3.3 Kolejność prac w sesji

1. **`Tokens.axaml`** — najpierw warstwa skalarna, potem złożona. Każdy token
   z komentarzem mówiącym, **jaką rolę pełni**, nie jaką ma wartość.
2. **`Typography.axaml`** — 12 ról. ⚠ Sprawdzić, czy w Avalonii 12 da się złożyć
   rolę typograficzną w jeden zasób (`ControlTheme`/`Style` z klasą), czy trzeba
   rozbić na osobne `x:Double` + `FontWeight` + `x:Double` interlinii. **Zmierzyć,
   nie zakładać** — to determinuje sposób konsumpcji w M2b–M4.
3. **Rejestracja w `App.axaml`.**
4. **`DesignTokenComplianceTests`** — napisać, uruchomić, **potwierdzić że failuje
   bez listy wyjątków**, potem wypełnić listę stanem obecnym.
5. Build → suite w trzech partycjach → smoke → **porównanie wizualne przed/po
   (musi być identyczne)**.

---

## 4. Ryzyka i pułapki — przeczytać PRZED pisaniem kodu

### 4.1 ⚠⚠ Partycjonowanie testów — obowiązkowe

Suite **nie biegnie w jednym przebiegu**. Trzy partycje (CLAUDE.md „Tests"):

```bash
dotnet test EmberTern.slnx --filter "FullyQualifiedName!~ConnectionExpandBindingProbe&FullyQualifiedName!~SettingsCenterViewTests&FullyQualifiedName!~ContextMenuPresentationTests&FullyQualifiedName!~BrandingPresentationTests"
```

Potem `ConnectionExpandBindingProbe` **sam** (dyrektywa użytkownika — wisi, gdy biegnie
z innymi headless), i pozostałe trzy klasy headless razem.

⚠ **Nigdy nie łączyć `dotnet build` i `dotnet test` w jednym poleceniu** — deadlock,
użytkownik musi przerywać. Osobne wywołania.

### 4.2 ⚠⚠ Testy headless

`DesignTokenComplianceTests` **prawdopodobnie nie potrzebuje sesji headless** — jeśli
czyta pliki `.axaml` jako tekst, jest zwykłym testem. **Preferować tę wersję.**

Jeśli jednak potrzebna sesja headless:
- **dołącz do `HeadlessCollection`**, nigdy własny `IClassFixture` (gotcha #94/#226/#286),
- ⛔ **nie konstruuj `MainWindow`** — udokumentowany kształt zawieszający suite;
  `BrandingPresentationTests` wisiały, po przepisaniu na gołe `new Window()` biegną w 476 ms.

### 4.3 ⚠ `TreatWarningsAsErrors=true`

Każde ostrzeżenie wywala build. Nieużywany zasób w słowniku ostrzeżenia nie daje,
ale nieużywane pole w teście — tak.

### 4.4 ⚠ Ryzyko R‑2 przeniesione z M2b — warto wiedzieć już teraz

Największym technicznym fragmentem M2b jest **własny `ControlTemplate` dla `CheckBox`**
(`Size.Checkbox = 14`, bo Fluent koduje rozmiar boxa w szablonie, nie w property).
Projektując tokeny w M2a **nie zakładać**, że da się to zrobić samym setterem.

### 4.5 ⚠ Weryfikacja DPI od M2b, nie dopiero w M5

Ryzyko R‑6: token o stałym pikselu okazujący się zły w M5 = przeróbka wszystkiego.
**Sprawdzać 150% na końcu każdego etapu M2b–M3.4.** W M2a nie ma czego sprawdzać
(nic się nie renderuje inaczej), ale token trzeba projektować z tą świadomością.

---

## 5. Definition of Done — M2a

| # | Warunek |
|---|---|
| 1 | `Themes/Tokens.axaml` + `Themes/Typography.axaml` istnieją i są zarejestrowane w `App.axaml` |
| 2 | Każdy token ma komentarz opisujący **rolę**, nie wartość |
| 3 | `DesignTokenComplianceTests` istnieje, jest zielony, a jego lista wyjątków odzwierciedla **stan faktyczny** (czyli na tym etapie jest długa — to jest poprawne) |
| 4 | **Test został sprawdzony przez zasadzenie naruszenia** — dodaj `FontSize` do losowego widoku, potwierdź że test czerwienieje, cofnij |
| 5 | **Build 0 błędów / 0 ostrzeżeń** |
| 6 | **Suite zielony w trzech partycjach** |
| 7 | **Smoke czysty** |
| 8 | ⭐ **Aplikacja wygląda IDENTYCZNIE jak przed M2a** — porównanie w obu motywach. Jakakolwiek różnica = przekroczenie zakresu |
| 9 | `product-polish.md` — dopisana sekcja „as-built M2a" z decyzjami i odstępstwami |
| 10 | Commit na `feat/product-polish`; push na oba remote'y **po akceptacji użytkownika** |

⚠ **Warunek 8 jest tym, który odróżnia M2a od M2b.** M2a buduje system; nie włącza go.

---

## 6. Kolejne etapy (kontekst, nie zakres tej sesji)

```
M2a ──► M2b ──► M2c ──► M3.1 ──► M3.2 ──► M3.3 ──► M3.4 ──► ⛔ BRAMA §13.3 ──► M4.x ──► M5
```

| Etap | Skrót |
|---|---|
| **M2b** | Compact Controls, własny `CheckBox`, DataGrid, scrollbary, `ToolTip`, kolory i powierzchnie (RB‑4, `#FCFCFD`) — **pierwsza duża zmiana wizualna** |
| **M2c** | sweep de‑lokalizacyjny — lista wyjątków testu do minimum |
| **M3.1–M3.4** | Status Bar 2.0 · Toolbar · pasek zakładek · Metadata Explorer |
| **BRAMA** | przegląd czterech powierzchni trwałych pod kątem **odbioru**, nie zgodności. Blokuje M4 |
| **M4.x** | migracja ekranów (dialogi ostatnie — świadomie) |
| **M5** | Final Polish + DPI 100/125/150/200 + inwentarze (terminologia, empty states) |

---

## 7. ⭐⭐ Reguła prowadząca — przeczytaj przed każdą decyzją implementacyjną

> **Użytkownik, 2026-08-01:** *„Dokument ma prowadzić produkt. Nie produkt dokument."*

`product-polish.md` jest źródłem prawdy dla **zakresu i założeń**, ale **nie jest celem**.

Jeżeli w trakcie M2a znajdziesz rozwiązanie wyraźnie lepsze od zapisanego:

```
1. NIE implementuj gorszego, bo było opisane wcześniej.
2. NIE implementuj lepszego po cichu.
3. Propozycja + uzasadnienie → akceptacja → aktualizacja dokumentu → implementacja.
```

**Ciche odstępstwo** odbiera dokumentowi znaczenie. **Ślepa zgodność** odbiera produktowi jakość.
Oba są błędami.

⛔ **Wyjątek: decyzje D1–D12 i wymagania specyfikacji** zmienia wyłącznie użytkownik.

**Gdzie to najpewniej wypłynie w M2a** (na podstawie §3.2 dokumentu — to są miejsca, gdzie katalog
zapisał *intencję*, a nie zmierzoną implementację):
- **sposób złożenia roli typograficznej** — dokument nie przesądza, czy w Avalonii 12 rola może być
  jednym zasobem (`ControlTheme`/klasa), czy musi być rozbita na `x:Double` + `FontWeight` + interlinię.
  **Zmierz i zaproponuj** — ta decyzja determinuje konsumpcję w M2b–M4;
- **granica warstwy złożonej `Thickness`** — 13 ról z §4.1 to propozycja z pomiaru, nie dogmat;
- **forma `DesignTokenComplianceTests`** — czy czyta `.axaml` jako tekst, czy potrzebuje sesji
  headless (preferowana wersja tekstowa, §4.2).

---

## 8. Jedno zdanie na start

**M2a buduje fundament, którego nikt jeszcze nie używa — i to jest cały jego sens.**
Jeżeli w trakcie pojawi się pokusa „przy okazji podłączmy to do `TextBox`" — to jest M2b,
i podłączenie go teraz odbiera M2c możliwość zmierzenia, ile wartości lokalnych faktycznie
blokuje system.
