# M4 · BLOK TYPOGRAFII (K1–K11 + K2) — materiał decyzyjny

> **🔒 STATUS: DECYZJE RATYFIKOWANE I WDROŻONE (2026‑08‑08). Dokument jest odtąd ZAPISEM, nie planem.**
>
> Użytkownik rozstrzygnął: **A → A‑2 · B → B‑1 · C → K9 na 11, ale K4 ZOSTAJE 13 z uzasadnieniem ·
> D → w całości · K5 skreślone.** As‑built: `product-polish.md` **§19.38**.
>
> ⭐ **Odstępstwo od rekomendacji jest w niej najważniejsze.** Proponowałem sprowadzić `PlanLead` do 12;
> użytkownik odmówił: *„to pojedynczy element pełniący rolę nagłówka planu i nie widzę potrzeby sztucznego
> sprowadzania go do 12 tylko po to, żeby zniknął literał"*. To jest **R12** wypowiedziane wprost — celem
> katalogu jest usunięcie wartości NIEUZASADNIONYCH, a nie wyzerowanie licznika.
>
> ⚠ **Jedna pozycja rekomendacji NIE została wykonana i wymaga osobnej decyzji:** poszerzenie okna licznika
> `FontSize` (§6, „czego rekomendacja nie obejmuje"). Po zbudowaniu okazało się, że ten sam `Measure`
> obsługuje także `FontFamily` — czyli wciąga temat czcionki monospace, ratyfikowany jako backlog sprintu
> UX. Zmiana została **wycofana**, pomiar i uzasadnienie zostały w komentarzu `DesignTokenComplianceTests`.
>
> ⛔ §6 to rekomendacja sprzed decyzji — zachowana jako uzasadnienie, nie jako lista do wykonania.

**Rendery** (oba motywy, `tools/probes/VisualCandidateProbe/out/`):
`m4t-a-naglowek-sekcji` · `m4t-b-pasek-narzedzi` · `m4t-c-trzynastka` · `m4t-d-metryki`
Odtworzenie: `dotnet run --project tools/probes/VisualCandidateProbe -- typo`

---

## §1 Pomiar — i cztery rzeczy, których rejestr nie mówił

### §1.1 ⭐⭐ ZNALEZISKO GŁÓWNE: nagłówek sekcji jest MNIEJSZY od tekstu, który nazywa

`Text.SectionHeader` = **11 SemiBold**. Jego kanoniczny konsument to `TextBlock.group-header` (19 użyć:
Settings Window 11 · Data Import 4 · dwa dialogi ustawień 4). **W każdym z tych 19 użyć stoi bezpośrednio
NAD `TextBlock.field-label`**, który niesie `Text.Application` = **12** Regular (164 użycia w aplikacji).

Komentarz roli mówi wprost: *„Mocniejszy od podpisu pola celowo: podpis nazywa jedną wartość, nagłówek
nazywa temat — przy tej samej wadze konkurują"*. ⚠ **Jest mocniejszy wyłącznie WAGĄ, będąc o stopień
mniejszym.**

⭐ **I to wyjaśnia całą trójkę K3/K6/K8 naraz.** Pięć widoków niezależnie od siebie odmówiło tej roli
i zostało przy **12 SemiBold**:

| wpis | miejsce | ile |
|---|---|---:|
| K3 | Performance: „Findings", „Table access", tytuł karty ustalenia | 3 |
| K6 | nagłówek karty tabeli w `ProcedureDetail` + `FunctionDetail` | 2 |
| K8 | `TextBlock.section` — panel szczegółów Session Managera i Trace Monitora | 2 style / **12 użyć** |

**Populacje są niemal równe: 19 nagłówków przy 11 i 17 przy 12 — w IDENTYCZNYM kontekście** (nagłówek nad
tekstem 12 px). To nie jest pięć przeoczeń wobec jednej reguły; to jedna sytuacja rozstrzygnięta w produkcie
dwa razy, a rejestr zapisał tylko jedną z tych odpowiedzi jako „kolizję".

⭐ Kształt znany: **`Size.Row.Tree` w M3.4a** — token deklarował 20, produkt pokazywał 24, token miał zero
konsumentów i to **token był poprawiany**, bo *„dokument prowadzi produkt, ale prowadzi go tam, gdzie produkt
jest DOBRY"*.

### §1.2 🔴 `Text.Toolbar` ma ZERO konsumentów, a dwa paski mają dwie odpowiedzi

| pasek | co niesie |
|---|---|
| pasek poleceń debuggera (12 przycisków) | `Text.Compact` = **11** |
| pasek edytora SQL · Script Executor | `Text.Compact` = **11** |
| pasmo poleceń Data Import | `Text.Application` = **12** |
| **`Text.Toolbar` = 12** | **0 konsumentów** |

Rola powstała po to, żeby ujednolicić paski narzędzi (jej komentarz: *„Dziś mieszany — to jedna z pozycji,
dla których ta rola w ogóle powstała"*), i **nie opisuje żadnego z nich**. K2 odroczono ze zdaniem *„rola
zostaje bez konsumenta do M3.2"* — M3.2 było i minęło (a M3.2b wycofano w całości), więc rola wisi od tamtej
pory. ⚠ Token bez konsumenta jest nieodróżnialny od regresji (#233).

⭐ Zwróć uwagę, czego rola **duplikuje**: `Text.Compact` opisuje *„chroma — panele, zakładki, **paski**"*.
Czyli `Text.Toolbar` i `Text.Compact` mają tę samą ROLĘ i różnią się wyłącznie liczbą.

### §1.3 ⚠⚠ K9 wskazuje zły element — i to trzecia „zakładka" w tym rejestrze

Rejestr mówi: *„`TabItem` — etykieta **dolnego panelu i pod‑zakładek**, 13 px"*. Zmierzone:

* `TabItem.bottom-tab` → `Text.Compact.Size` (**11**) — już na roli;
* `TabItem.sub-tab` → `Text.Compact.Size` (**11**) — już na roli;
* **13 px zostało na BAZOWYM stylu `TabItem`**, który obsługuje dokładnie **10 zakładek**: osiem
  w `AddFieldDialog` i dwie w `NewTableTabView`.

⭐ §18.R sam ostrzegał, że *„rejestr indeksował po nazwie, a «zakładka» jest nośnikiem dwóch różnych
rzeczy"* — po korekcie z M3.3a okazuje się, że **trzech**: pasek dokumentów, dolny panel / pod‑zakładki,
i teraz zakładki dialogu.

### §1.4 ⚠ K5 nie ma przedmiotu

Rejestr: *„chip wagi ustalenia — promień 3 vs `Radius.Chip` 4 → **`Radius.Surface`**"*, czyli pozycja
została rozwiązana w chwili zapisu. W `PerformancePanelView` nie ma dziś promienia 3 — jest 4 (karta
ustalenia) i 6 (kapsuła postępu, gdzie promień to połowa wysokości, czyli arytmetyka). **Nie ma czego
rozstrzygać** — dokładnie jak przy wycofanym K12.

### §1.5 ⚠ 29 literałów `FontSize` leży poza zasięgiem licznika

`DesignTokenComplianceTests.Measure` skanuje **`Views/` + `Controls/`**. Poza tym oknem:

| miejsce | ile |
|---|---:|
| `Themes/PickerTemplates.axaml` | 12 |
| `Completion/*.cs` (HoverInfoView 6 · QuickInfoView 4 · ParameterHelper 4 · pozostałe 2) | 16 |
| `Sql/SqlSnippetDropTarget.cs` | 1 |

⭐ To jest **#332 w wydaniu typograficznym**: karty hover, Quick Info i Parameter Helper — czyli powierzchnie,
które użytkownik ogląda przy każdym pisaniu SQL — ustawiają rozmiar pisma w C#, gdzie żaden licznik katalogu
nigdy nie zajrzał. Ten sam kształt, co ikona 14 px w `MenuMarkup.cs`, naprawiona w bloku gęstości.

**Stan literałów `FontSize` łącznie: 69** (51 w XAML + 18 w C#) — 9×7 · 10×6 · 11×6 · 12×17 · 13×15.

---

## §2 Pytanie A — nagłówek sekcji (K3 · K6 · K8)

**Render:** `m4t-a-naglowek-sekcji-{Dark,Light}.png` — grupa ustawień i panel szczegółów, w trzech układach.

| # | wariant | co się zmienia | koszt / zysk |
|---|---|---|---|
| **A‑1** | zostaje jak jest | — | dwie odpowiedzi na jedno pytanie zostają na stałe; 17 wartości lokalnych z uzasadnieniem |
| **A‑2** | **rola → 12 SemiBold** | 19 nagłówków `group-header` rośnie o 1 px; 17 wyjątków znika | obietnica roli („mocniejszy od podpisu") staje się prawdą; **mniej gęsto o 1 px na nagłówek** |
| **A‑3** | rola zostaje 11, **`Text.Application` schodzi do 11** | 164 podpisy pól + treść dialogów maleją o 1 px | **najgęstszy wariant**; hierarchia nagłówka odbudowana od dołu |

⚠⚠ **A‑3 jest wariantem, na który wskazuje R18 — i dlatego wymaga TWOJEGO oka, a nie mojego.** Reguła mówi
*„przy równej czytelności wygrywa gęstszy"*, ale warunek „równej czytelności" jest pierwszy, a to jest
**główny stopień pisma aplikacji**: 164 podpisy pól, treść wszystkich dialogów, opisy w Settings Center.
Nie jestem w stanie orzec za Ciebie, czy 11 px jest tu równie czytelne przy ośmiu godzinach pracy — a §0.5
mówi, że „nie wiadomo" jest odpowiedzią odmowną.

---

## §3 Pytanie B — tekst paska narzędzi (K1 · K2)

**Render:** `m4t-b-pasek-narzedzi-{Dark,Light}.png`.

| # | wariant | skutek |
|---|---|---|
| **B‑1** | **`Text.Compact` (11) dla wszystkich pasków; `Text.Toolbar` wycofana** | 3 z 4 pasków już tam są; pasmo importu schodzi 12 → 11; z katalogu znika rola bez konsumenta, która duplikuje `Text.Compact` rolą |
| **B‑2** | `Text.Toolbar` (12) staje się prawdziwa | debugger, edytor SQL i Script Executor rosną 11 → 12; **mniej gęsto na trzech powierzchniach trwałych**, w tym na pasku poleceń debuggera o 12 przyciskach |
| **B‑3** | zostaje jak jest | rola bez konsumenta wisi dalej (#233) |

---

## §4 Pytanie C — 13 px bez roli (K4 · K9)

**Render:** `m4t-c-trzynastka-{Dark,Light}.png`.

**K9 — zakładka dialogu (10 sztuk).** Jedyna zakładka w produkcie, która nie jest na 11. Warianty: **11**
(jak wszystkie pozostałe zakładki) · 12 · zostaje 13.

**K4 — `PlanLead`, wiodąca linia planu w Performance (1 sztuka).** ⚠ Tu 13 px niesie HIERARCHIĘ: linia stoi
nad metrykami przy 12. Warianty: **12** (hierarchia przechodzi na kolor i pozycję — na renderze widać, że
się utrzymuje) · zostaje 13 z zapisanym powodem. ⛔ Nowa rola przy 13 wymagałaby kilku konsumentów (**R3**),
a jest jeden.

---

## §5 Pytanie D — drobne metryki (K7 · K10 · K11)

**Render:** `m4t-d-metryki-{Dark,Light}.png` — z kolumnami **×3**, bo przy 1:1 wszystkie trzy pary wyglądają
identycznie, a *„nie widać różnicy"* i *„render nie pokazuje różnicy"* wyglądają tak samo.

| wpis | dziś | rola | co pokazuje powiększenie |
|---|---|---|---|
| **K7** nagłówek `Expander` w bliźniakach | `MinHeight` 26 | `Size.Control` 24 | różnica **widoczna** przy ×3, podprogowa przy 1:1 |
| **K10** kształt zakładki dolnego panelu | promień 4 | `Radius.Chip` 4 — **wartość ta sama, sporna jest NAZWA** („zakładka nie jest chipem") | ⭐ **żadna** — nie ma czego oglądać, bo liczby są równe |
| **K11** chip transakcji | `Spacing` 5 | `Space.Sm` 6 albo `Space.Xs` 4 | różnica **widoczna** przy ×3 |

⚠ K10 to jedyna pozycja bloku, w której nie chodzi o wygląd, tylko o to, **czy wolno nazwać zakładkę chipem**.
R12 mówi, że błędna rola jest gorsza od wartości lokalnej, więc `Radius.Chip` tam nie wejdzie; zostaje albo
wartość lokalna z powodem (stan dzisiejszy), albo **własna rola o tej samej liczbie**, na co §3.3 katalogu
wprost pozwala („dwie role o tej samej wartości to dwie niezależne decyzje, które mogą się rozejść").

---

## §6 ⭐ Rekomendacja — jedna, na cały blok, do ratyfikacji

> ⛔ To jest propozycja. Decyzje o typografii podejmuje użytkownik.

| pytanie | rekomendacja | dlaczego |
|---|---|---|
| **A** | **A‑2** — `Text.SectionHeader` → **12 SemiBold** | rola dostaje liczbę, którą produkt pokazuje w 17 z 36 przypadków, a jej własna obietnica („mocniejszy od podpisu pola") przestaje być nieprawdziwa. Kształt `Size.Row.Tree` z M3.4a. ⚠ **A‑3 zostawiam Tobie** — jest gęstszy i R18 na niego wskazuje, ale dotyka głównego stopnia pisma aplikacji |
| **B** | **B‑1** — wszystkie paski na `Text.Compact` (11), **`Text.Toolbar` wycofana** | 3 z 4 pasków już tam są · gęstsze (R18) · znika rola, która duplikuje `Text.Compact` rolą i nie ma konsumenta (#233). Zamyka **K1 i K2 naraz** |
| **C** | **K9 → 11** (`Text.Compact`), **K4 → 12** (`Text.Application`) | zakładka dialogu przestaje być jedyną zakładką poza regułą; wiodąca linia planu opiera hierarchię na kolorze i pozycji, co render potwierdza. Oba gęstsze |
| **D** | **K7 → `Size.Control` (24)** · **K11 → `Space.Xs` (4)** · **K10 → nowa rola `Radius.Tab` = 4** | K7 i K11 gęstsze i podprogowe wizualnie (R18 rozstrzyga remis) · K11 zbiega się z paddingiem badge'a DEV MODE, który poziomo już niesie 4 · K10 zamyka się bez zmiany piksela, a §3.3 na taką rolę pozwala |
| **K5** | ⛔ **skreślić z rejestru — brak przedmiotu** | rozwiązane w chwili zapisu, jak wycofane K12 |

**Efekt: rejestr K1–K11 zamyka się w całości.** Po tym bloku nie zostaje ani jedna otwarta kolizja.

**Czego rekomendacja świadomie NIE obejmuje:**

* ⛔ **69 pozostałych literałów `FontSize`** — sweep należy do **M4.3**, tak jak 95 literałów rozmiaru ikony;
* ⛔ **29 literałów poza zasięgiem licznika** (§1.5) — ⭐ ale **rozszerzenie okna licznika o `Completion/`,
  `Sql/` i `Themes/` proponuję zrobić RAZEM z tym blokiem**, bo dopóki go nie ma, „ile zostało" jest liczbą
  nieprawdziwą, a nie po prostu dużą;
* ⛔ **`Font.Code` / `Cascadia Code` vs `Cascadia Mono`** — ratyfikowane jako backlog sprintu UX;
* ⛔ **9 px** (7 użyć, w tym dwa glify) — ⭐ 12 px w edytorach stojących w wierszu siatki i 9 px przy glifach
  to **decyzje kontenera**, nie dryf; §18.0.5/3 ratyfikował je osobno i ten blok ich nie rusza.

---

## §7 ⏸ Otwarte po tym bloku

| # | temat | dokąd |
|---|---|---|
| sweep literałów `FontSize` (69) + rozmiaru ikony (95) | M4.3 |
| **Z‑3** — wiersz Table Data, liczby 40 px nie ma w kodzie | osobno, po decyzjach projektowych |
| migracja ekranów M4.1–M4.4 | po zamknięciu rejestru (**D‑M4‑1**) |
| 150 % DPI (R‑6) | do sprawdzenia okiem po wdrożeniu |

⚠ **Granica narzędzia:** sonda liczy układ **raz**, więc odpowiada na *„jak to wygląda"*, nigdy na *„czy to
się ustala"*. Żadne z pytań A–D nie dotyczy zbieżności. ⚠ Kryterium odbioru pozostaje ekran aplikacji (**R16**).
