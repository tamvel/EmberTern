# Product Polish — M2c — dokument startowy sesji

> **To jest prompt dla Claude'a, nie dla użytkownika.** Punkt wejścia w M2c — pozwala wejść
> w implementację bez czytania całej historii etapu.
>
> **Przeczytaj TEN plik + `docs/design/product-polish.md` §17 (podsumowanie M2b) i §16 (wzorzec
> `FluentBridge`).** ⛔ Reszty §15 **nie czytaj na starcie** — to zapis 21 iteracji M2b; sięgaj po
> konkretną podsekcję dopiero wtedy, gdy dotyczy tego, co właśnie robisz.
> ⛔ `product-polish-m2b-handover.md` i `-m2a-handover.md` są **zamknięte**.

---

## 1. Gdzie jesteśmy

| | |
|---|---|
| **Branch** | `feat/product-polish` (wypchnięty na oba remote'y) |
| **Etap** | **M0 ✅ · M1 ✅ · M2a ✅ · M2b ✅ ZAMKNIĘTY 2026-08-02** |
| **Ten etap** | **M2c — sweep de‑lokalizacyjny** |
| **Baseline** | build 0/0; suite **7088** (7000 + 54 + 34); smoke czysty |

**Specyfikacja etapu (źródło prawdy):** `C:\Users\grzegorz.gronski\Desktop\Product Polish.mdown`
**Dokument etapu:** `docs/design/product-polish.md`

---

## 2. Po co jest M2c — jednym zdaniem

> **Wartość lokalna bije setter stylu. Dopóki stoi w widoku, żadna reguła Design Systemu nie działa.**

M2a zbudował katalog, M2b włączył go dla kontrolek bazowych — ale **609 lokalnych `FontSize`,
81 `FontFamily` i 37 `CornerRadius`** w widokach wciąż unieważnia go punktowo. M2c to usuwa.

⭐ **To nie jest sprzątanie kosmetyczne.** Projekt udowodnił tę tezę **pięć razy**: sześć wariantów
`MessageBanner`, `MainWindow.Icon`, `Foreground` Batch Results, `DangerIconBrush`, a w samym M2b —
belka statusu Data Import, której **żadna reguła systemu nie mogła naprawić**, dopóki widok trzymał
własne `FontSize` i `VerticalAlignment`.

---

## 3. ⛔ Decyzje RATYFIKOWANE — nie otwierać ponownie

Pełna lista: `product-polish.md` §17.3. Najważniejsze dla M2c:

| # | Reguła |
|---|---|
| **R5** | **Kolor może określać priorytet akcji, ROZMIAR NIE** |
| **R7** | **Nie łatać pojedynczych ekranów** — najpierw reguła Design Systemu, potem ewentualny wyjątek |
| **R8** | **Kryterium odbioru: czy wygląda to jak dopracowana aplikacja komercyjna?** Pomiar jest narzędziem, nie argumentem końcowym |
| **R2** | Komponent ocenia się w komplecie stanów i w **obu motywach** |
| **R3** | Nowa **rola** powstaje z użycia w kilku komponentach, nigdy z jednego przypadku |
| **R4** | **`FluentBridge` nie jest drugim katalogiem tokenów** |
| **R9** | **Domain Picker** — nie ujednolicać szerokości |
| **R10** | **Kolor komentarzy SQL zostaje** (V‑1) |
| **R11** | **`Size.Row.Grid`** to osobna decyzja produktowa |

Dodatkowo obowiązują **cztery decyzje architektoniczne** z §17.2 — w szczególności:
⭐ **kontener rozstrzyga wielkość** · ⭐ **reguła musi być sformułowana pozytywnie**.

---

## 4. Zakres M2c — co dokładnie robi

### 4.1 Cel mierzalny

| Licznik | Stan wejściowy | Cel |
|---|---|---|
| `FontSize` | **605** w 49 plikach | uzasadniona reszta |
| `FontFamily` | **81** w 28 plikach | **0** poza uzasadnionymi |
| `CornerRadius` | **37** w 13 plikach | uzasadniona reszta |

⚠ **Licznik mierzy WARTOŚCI LOKALNE** — odwołanie `{DynamicResource …}` **nie liczy się** (korekta
z §15.11.5). Migracja pojedynczego widoku obniża liczbę realnie, a nie pozornie.

⭐ **„Uzasadniona reszta", nie zero.** Wartość, która ma powód, zostaje **razem z powodem zapisanym
w komentarzu** i z podniesioną bazą w teście — strażnik sam mówi w komunikacie błędu, że świadome
podniesienie bazy jest poprawną częścią procesu.

### 4.2 Trzy największe skupiska (od nich zaczynasz)

| Plik | `FontSize` | `FontFamily` | Uwaga |
|---|---|---|---|
| `Views/DebuggerTabView.axaml` | **85** | **17** | największy; `FontFamily` to monospace — patrz §6 |
| `Views/DataImportTabView.axaml` | **82** | — | już częściowo ruszony w M2b (belka statusu) |
| `Views/PerformancePanelView.axaml` | **42** | — | |

### 4.3 ⭐ `FontFamily` ma osobny status i NIE jest zwykłą wartością lokalną

**81 wystąpień to w większości ciągi monospace.** ⛔ **Ujednolicenie rodziny monospace NIE należy do
M2c** — zostało zmierzone (7 różnych ciągów / 95 wystąpień / 33 pliki) i **przekazane do backlogowego
sprintu UX**, bo rozstrzyga `Cascadia Code` vs `Cascadia Mono` dla edytora, debuggera, kart hover
i jedenastu podglądów DDL **naraz** (`settings-center.md` §2.7 + §7.1).
⭐ **M2c wolno tylko podmienić ciąg na token `Font.Code`, jeżeli ciąg jest już dziś identyczny
z tym, co token niesie.** Gdzie ciągi się różnią — **zostaw i zapisz**, to decyzja typograficzna
tamtego sprintu.

---

## 5. ⛔ Czego M2c NIE ROBI

1. ⛔ **Nie zmienia wyglądu.** To jest sweep **de‑lokalizacyjny**: wartość lokalna znika, a na jej
   miejsce wchodzi **rola o tej samej wartości**. Jeżeli coś zmieniło wygląd — albo trafiłeś
   w niewłaściwą rolę, albo znalazłeś defekt, który trzeba **zgłosić osobno**, a nie naprawić po drodze.
2. ⛔ **Nie dodaje ról „bo pasuje".** Rola powstaje z użycia w kilku komponentach (R3). Wartość, która
   nie pasuje do żadnej roli, **zostaje lokalna razem z uzasadnieniem**.
3. ⛔ **Nie rusza palety składni edytora** (§6.3 — zamrożona).
4. ⛔ **Nie ujednolica rodziny monospace** (§4.3 wyżej).
5. ⛔ **Nie zaczyna M3.** Status Bar 2.0, Toolbar, pasek zakładek i Metadata Explorer to **M3.1–M3.4**.
6. ⛔ **Nie zmienia niczego w `Themes/`** poza dopisaniem roli, jeżeli sweep udowodni, że jej brakuje —
   a wtedy z uzasadnieniem i drugim konsumentem.
7. ⛔ **Nie „poprawia przy okazji"** proporcji zamkniętych w M2b (§17.3).

---

## 6. Kolejność prac

> ⭐ **Jeden widok = jedna iteracja = jeden commit.** Ten rytm sprawdził się przez 21 iteracji M2b
> i jest jedynym, przy którym QA użytkownika ma sens.

1. **Krok 0 — inwentarz.** Zmierz i **zapisz w §18**, jakie wartości faktycznie stoją w widokach:
   które mają rolę w katalogu, które nie mają żadnej, a które są tą samą wartością w kilku miejscach
   (kandydat na rolę). ⚠ **Bez tego kroku sweep będzie zgadywaniem.**
2. **Kroki 1–3 — trzy największe pliki** (§4.2), w kolejności malejącej.
3. **Kroki dalsze — resztę grupami tematycznymi** (edytory obiektów · dialogi · monitory).
4. **Krok końcowy — podniesienie bazy** w `DesignTokenComplianceTests` do stanu faktycznego,
   z powodem przy każdej pozostawionej pozycji.

---

## 7. Definition of Done — M2c

| # | Warunek |
|---|---|
| 1 | Liczniki `FontSize` / `FontFamily` / `CornerRadius` **spadły**, a każda pozostawiona wartość ma **komentarz z powodem** |
| 2 | Baza w `DesignTokenComplianceTests` odzwierciedla **stan faktyczny** (test sprawdza obie strony) |
| 3 | **Build 0 błędów / 0 ostrzeżeń** |
| 4 | **Suite zielony w trzech partycjach** |
| 5 | **Smoke czysty** |
| 6 | ⭐ **Aplikacja wygląda IDENTYCZNIE jak przed M2c** — porównanie w obu motywach. Jakakolwiek różnica to defekt do zgłoszenia, nie do zaakceptowania |
| 7 | Sekcja **as‑built §18** w `product-polish.md`, prowadzona iteracja po iteracji |
| 8 | Commit na `feat/product-polish`; push na oba remote'y **po akceptacji użytkownika** |

⚠ **Warunek 6 jest tym, który odróżnia M2c od M2b.** M2b włączał system; M2c **usuwa to, co go
blokuje**, nie zmieniając wyniku.

---

## 8. ⚠ Pułapki — zapłacone w M2b, nie płacić drugi raz

1. **⚠⚠ Wartość lokalna bije setter stylu** — to teza tego etapu, ale też jego pułapka: po usunięciu
   wartości lokalnej kontrolka **nagle zaczyna słuchać systemu** i może wyglądać inaczej, niż wyglądała.
   To nie jest regresja sweepu — to **ujawniony dług**. Zgłoś, nie maskuj (§15.6.4a).
2. **⚠⚠ Arytmetykę sprawdza się na SUMIE, nie na składniku** (§5.1). Wiersz 22 − `Pad.Cell` (3+3) =
   **16 px** i wszystko w komórce musi się w tym zmieścić.
3. **⚠ Kolejność deklaracji rozstrzyga** między stylami o równej trafności — styl bazowy przed wariantami.
4. **⚠ Styl typu sięga do cudzego szablonu** — sprawdź, czy właśnie coś dostałeś za darmo, czy zepsułeś.
5. **⚠ Deklarowana właściwość potrafi kłamać** — sonduj drzewo.
6. **⚠ Test może mierzyć nie ten podmiot.** Świadkiem mapowania Bridge'a jest kontrolka **bez wariantu**;
   ograniczenie mierzy się przeciw **ograniczeniu**, nie przeciw zamiarowi.
7. **⚠⚠ Nigdy nie łącz `dotnet build` i `dotnet test` w jednym poleceniu** — deadlock.
8. **⚠⚠ Trzy partycje testów**; `ConnectionExpandBindingProbe` biegnie **sam**.
9. **⚠ Zabij `EmberTern.exe` przed przebudową** — inaczej MSB3021/MSB3027.
10. **⚠ `{DynamicResource}` nie rzuca przy brakującym kluczu** — literówka jest niewidoczna przy
    zielonym buildzie. To jest dokładnie ten rodzaj błędu, który sweep może wprowadzić masowo.

---

## 9. ⭐⭐ Reguła prowadząca

> **Użytkownik:** *„Dokument ma prowadzić produkt. Nie produkt dokument."*
> **Użytkownik (§15.11):** *„Pomiar nadal jest obowiązkowy. Ale nie jest już celem. Jest tylko
> narzędziem."*

Jeżeli znajdziesz rozwiązanie wyraźnie lepsze od zapisanego: **propozycja → akceptacja → aktualizacja
dokumentu → implementacja.** ⛔ Ani ciche odstępstwo, ani ślepa zgodność.
⛔ Decyzje D1–D12 i reguły R1–R11 zmienia wyłącznie użytkownik.
