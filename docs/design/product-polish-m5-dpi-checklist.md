# M5 / DPI 100–200 % — celowana checklista QA

> **Status: materiał do RĘCZNEGO QA użytkownika. Nic tu nie jest zaimplementowane i nic nie jest naprawione.**
> Utworzone 2026-08-10, po zamknięciu §10 · L‑1 · M‑3 · §9.
>
> ⛔ **To NIE jest przegląd całej aplikacji.** Lista jest przypięta do **rzeczywistych zmian metryk z M4
> i M5** — każdy przystanek wskazuje, która iteracja go ruszyła. Miejsca, których żaden etap nie dotknął,
> celowo tu nie występują.
>
> ⚠ Wszystkie liczby niżej są **policzone z tokenów w `Themes/Tokens.axaml` i `Themes/Typography.axaml`**
> (odczyt 2026-08-10), a nie przepisane z prozy. Przelicz je ponownie, jeżeli któryś token się zmieni.
>
> ⭐ **R‑6 jest zaległe od dwóch bloków M4** (oba ruszały metryki, a QA użytkownika ich nie obejmowało),
> a M5 dołożyło trzecią iterację metryk przez **L‑1**. To jest ta zaległość.
> ⭐ **V‑4** (grubość konturu ikon Lucide przy 14 px i skalowaniu) jest rozdziałem **C**.

---

## A. Fakt ramowy: **125 % jest arytmetycznie najgorsze, 200 % jest czyste**

Każdy token metryczny i każda interlinia przeliczone przez 1,25 / 1,5 / 2:

| skala | tokeny lądujące na **ułamku** piksela urządzenia |
|---|---|
| **125 %** | **15 z 28** — `Size.Icon` 14 · `Size.ControlToolbar` 22 · `Size.Row.Grid` 22 · `Size.Row.GridEdit` 30 · `Size.Row.Menu` 22 · `Size.Row.Tab` 26 · `Size.Checkbox` 14 · `Radius.Surface` 3 · `Space.Hair` 2 · `Space.Sm` 6 · ramka 1 px · teksty 10/11/13/14/23 · **wszystkie sześć interlinii** |
| **150 %** | **5** — `Radius.Surface` 3 · ramka 1 px · teksty **11** i **13** i 23 · interlinie 15/17/19 |
| **200 %** | **zero** — wszystko wypada całkowicie |

⭐⭐ **Mechanizm, którego szukamy, jest już udokumentowany w tym projekcie** (M2b / R‑6, `product-polish.md`):
`UseLayoutRounding` przycina **KAŻDY ELEMENT OSOBNO** do piksela urządzenia, więc dwa sąsiadujące elementy
o różnej wysokości lub pozycji **lądują po przeciwnych stronach zaokrąglenia**, a różnica rośnie do całego
fizycznego piksela. **Objaw to nie rozmycie, tylko rozjechanie linii bazowych i krawędzi o 1 px.**

⚠ Z tego samego zapisu wynika, że **użytkownik pracuje na 125 %** — czyli **100 % i 200 % są dla niego
stanami nietypowymi** i tam najłatwiej przeoczyć regresję „bo tak zawsze wyglądało".

---

## B. Trasa — dziesięć przystanków, każdy przypięty do konkretnej zmiany

| # | miejsce | co zmieniło M4/M5 | czego szukać |
|---|---|---|---|
| **1** | pasek tytułu + Metadata Explorer | drabina **`Size.Icon.Toolbar` 16 / `Size.Icon` 14** (M4 gęstość, A‑3) | czy ikony paska (16) i ikony wiersza drzewa (14) **nadal czytają się jako dwa świadome poziomy**, a nie jak przypadkowa różnica; przy 125 % 14 → 17,5 px |
| **2** | paski paginacji — **wyniki SQL · Table Data · edytory funkcji / procedury / widoku** | M4.1 zrównał **18 ikon 16 → 14** | czy **wszystkie pięć pasków wygląda tak samo** — to była cała treść M4.1; rozjazd wróci najpierw tutaj |
| **3** | **Undo / Redo** w pasku narzędzi | M4.1 QA: geometria przesunięta o **2,5 jednostki** | czy strzałki stoją w linii z sąsiadami; przesunięcie 2,5/24 × 14 = **1,46 DIP** i przy każdej skali zaokrągla się inaczej |
| **4** | siatki definicji edytowalne — **Table → Pola · Data Import → Table types · Security Manager** | nowa rola **`Size.Row.GridEdit` = 30** (M4 gęstość, C‑1) | 30 × 1,25 = **37,5** — czy wiersze mają **równą wysokość**, czy co drugi jest o piksel wyższy; czy edytor w komórce nie jest przycięty |
| **5** | nagłówki sekcji w edytorach obiektów | **`Text.SectionHeader` 11 → 12 SemiBold**, interlinia 15 → **17** (M4 typografia) | interlinia 17 jest ułamkowa przy 125 % **i** 150 % — czy nagłówek nie „siada" na tekście pod nim i czy odstęp nad/pod jest symetryczny |
| **6** | **Session Manager** i **Trace Monitor** | M4.3c `Button.seg` (9 segmentów, Trace zwężony o 2 px/stronę) · chevrony 16 → 14 · puste stany 13 → 12 · pole filtra Trace 26 → **24** | czy segmenty mają **równą szerokość i wysokość**; czy pole filtra stoi **w jednej linii** z sąsiadami (to była cała treść Q4 w M4.3) |
| **7** | zakładki **„Zależności"** w dowolnym edytorze obiektu | M4.2b: jedna kontrolka, wiersz **`Size.Row.Tree` = 24** | ⭐ **KONTROLA POZYTYWNA** — 24 wypada całkowicie na **każdej** skali (30 / 36 / 48). Jeżeli *tu* coś się rozjeżdża, przyczyną **nie jest** zaokrąglanie i szukamy gdzie indziej |
| **8** | dialogi **Nowe połączenie · Eksport · Wykonaj procedurę** | M4.4: `GrowingDialogBehavior` z regułą `min` | ⚠ **jedyna arytmetyka ujawniająca się WYŁĄCZNIE poza 100 %**: sufit liczy `workingArea / scaling`, a pozycję `size × scaling`. Sprawdź przy **150 %** i **200 %**, czy dialog **mieści się w ekranie** i czy stopka (Wykonaj/Anuluj) **nie wychodzi pod dolną krawędź** |
| **9** | focus **Tabem** — pasek tytułu, stopka dialogu, `Button.primary` | **L‑1**: pierścień **1 px** na `primary`, tło na `caption` | 1 px × 1,25 = **1,25** — czy pierścień jest **równomierny ze wszystkich czterech stron**, czy nie znika na jednej krawędzi; ⚠ `caption` maluje **tłem**, więc jest odporny — jeśli tam jest źle, to nie DPI |
| **10** | **pusty pasek boczny** (bez połączeń) + puste stany Roles / Membership / Script | **M‑3**: glif `Icon.Plus` **12** + tekst **11**, wyśrodkowane | 12 jest czyste na każdej skali, **11 nie** — czy glif i tekst stoją na **wspólnej linii**; czy wyśrodkowany komunikat nie drga o piksel między skalami |

### Oba motywy — gdzie to ma znaczenie

⚠ DPI jest z natury **niezależne od motywu**, więc nie ma sensu przechodzić całej trasy dwa razy. Motyw
sprawdź tam, gdzie o wyniku decyduje **krawędź lub kontur**, bo kontrast zmienia to, czy piksel w ogóle
widać: **przystanek 9** (pierścień focusu), **przystanek 1 i 3** (kontur ikon) oraz **przystanek 6**
(krawędzie segmentów).

---

## C. V‑4 — grubość konturu ikon Lucide, policzona

Lucide rysuje kreską **2 jednostki w siatce 24**, więc realna grubość zależy od rozmiaru ikony:

| ikona | 100 % | 125 % | 150 % | 200 % |
|---|---|---|---|---|
| **12 px** (`Size.Icon.Sm`) | **1,000** | 1,250 | **1,500** | **2,000** |
| **14 px** (`Size.Icon`) | 1,167 | 1,458 | 1,750 | 2,333 |
| **16 px** (`Size.Icon.Toolbar`) | 1,333 | 1,667 | **2,000** | 2,667 |

⭐⭐ **Znalezisko do sprawdzenia okiem, bo wynika wprost z arytmetyki:** przy **150 %** ikona paska (16)
rysuje **równe 2 px**, a ikona wiersza (14) **1,75 px** — czyli drabina gęstości z M4 przy 150 % robi się
**wyraźniejsza niż przy 100 %**. Pytanie do QA: czy to nadal czyta się jako hierarchia, czy jako niedoróbka.

⚠ **Ikona 14 px nie ma całkowitej grubości konturu przy ŻADNEJ skali** — a to najczęstszy rozmiar
w aplikacji.

---

## D. Co jest znaleziskiem, a co nie

✅ **Znalezisko:**
* ucięty tekst lub kontrolka,
* wiersze o **nierównej wysokości** w obrębie jednej siatki,
* ikona i etykieta na **różnych liniach bazowych**,
* **niepełny pierścień focusu** (brak na jednej krawędzi),
* dialog **wychodzący poza ekran**,
* dwa paski paginacji **różniące się od siebie**.

⛔ **Nie znalezisko:**
* lekko miększy kontur ikony przy 125 % — to **V‑4**, arytmetyka Lucide; **do zapisania, nie do naprawiania
  w tej iteracji**,
* różnica 1 px między 100 % a 125 % w **pojedynczym elemencie bez sąsiada do porównania** — bez odniesienia
  nie da się odróżnić zaokrąglenia od zamierzonej wartości.

---

## E. Świadomie POZA zakresem tej checklisty

⛔ **Nie oglądamy:**
* **16 przejść Fluenta** — nazwany wyjątek, `product-polish.md` §9.1,
* **`ToolTip`** — niezmierzony w §9, osobna sprawa,
* **Z‑3** (wiersz Table Data) — czeka na ustalenie **przyczyny**, nie na pomiar DPI,
* **B1** (prywatne ikony PK/FK/Unique na siatce 14 w `TableDetailTabView`).

---

## F. Wynik QA

> Do wypełnienia po przejściu trasy. ⛔ Dopóki to zostaje puste, **DPI nie jest zamknięte** — zielony build
> i zielona suite nie mówią o skalowaniu nic (R16: kryterium odbioru jest ekran).

**QA wykonane przez użytkownika 2026-08-10.**

| skala | werdykt | znaleziska |
|---|---|---|
| **100 %** | ✅ OK | — |
| **125 %** | ✅ OK | — |
| **150 %** | ⛔ **FAIL** | **Activity Monitor** i **Data Import** nie mieszczą się w dostępnej przestrzeni; część interfejsu jest poza ekranem i **nie ma jak do niej doscrollować** |
| **175 %** | ⛔ **FAIL** | problemy z 150 % pozostają, dodatkowo **przestaje być widoczny dolny pasek aplikacji** |
| **200 %** | — nie testowane | ⚠ Windows na konfiguracji użytkownika **nie udostępnia** tej skali w ustawieniach |

⭐ **Przystanki 1–7, 9 i 10 przeszły** — bez uwag do wierszy, ikon, focusu, dialogów i stanów pustych.
Zgłoszenie dotyczy wyłącznie **przystanku 6** (Activity Monitor) i **Data Importu**, który w trasie
występował pod przystankiem 4 (siatka typów) — ⚠ ale defekt nie jest tym, czego tam szukaliśmy.

⛔⛔ **175 % ZAPISANE JAKO OBSERWACJA, NIE JAKO CEL PROJEKTOWY** (decyzja użytkownika): nie projektujemy
osobnej obsługi tej skali. ⛔ Nie traktujemy też 150 %/175 % jako „do zignorowania, bo użytkownik tego nie
używa" — to jest **rzeczywiste znalezisko QA**.

### F.0 🔒 DECYZJA: ZAPISANE JAKO ISTNIEJĄCE OGRANICZENIE, **BEZ NAPRAWY W M5**

🔒 **Ratyfikowane przez użytkownika 2026-08-10.** Znalezisko z 150 % / 175 % zostaje **długiem technicznym**;
layout Activity Monitora i Data Importu **nie jest zmieniany w M5**.

⭐ **Uzasadnienie użytkownika, i jest merytoryczne:** *„żaden z trzech kierunków nie jest drobną poprawką
DPI, tylko osobną decyzją UX"*. Trzy rozważone kierunki i dlaczego żaden nie mieści się w M5:

| kierunek | koszt / problem |
|---|---|
| przewijanie poziome paska | najtańsze, ale **pasek narzędzi, który trzeba przewijać, jest sam w sobie kiepskim UX** |
| zawijanie do drugiego wiersza (`WrapPanel`) | ⚠ **narusza §13.3 Zero Layout Shift** — pasek zmieniałby wysokość zależnie od szerokości okna |
| redukcja zawartości paska (menu / zwijana sekcja) | jedyne, które usuwa PRZYCZYNĘ, i **najdroższe** — to przeprojektowanie dwóch pasków poleceń |

⛔ **NIE IMPLEMENTOWAĆ** przewijania, `WrapPanela` ani redukcji toolbaru przy okazji innego etapu.
⭐ Rozwiązanie wymaga **osobnego etapu** z własną decyzją produktową. Pomiar niżej jest kompletny —
**przy powrocie do tematu nie trzeba go powtarzać.**

⚠ **DPI jest tym samym ZAMKNIĘTE** dla skal 100 % i 125 % (czyste) oraz 150 % / 175 % (znane ograniczenie,
udokumentowane). Skale powyżej 175 % **nie są testowane** — Windows nie udostępnia ich na konfiguracji
użytkownika.

### F.1 Diagnoza — zachowana w całości, żeby nie powtarzać pomiaru

Pomiar: `VisualCandidateProbe -- fit` → `out/m5-dpi-fit.txt` (prawdziwe widoki z prawdziwymi ViewModelami).

⭐⭐ **Wynik w jednym zdaniu: to NIE jest defekt skalowania, tylko ograniczenie konstrukcyjne tych dwóch
widoków, które skalowanie DPI jedynie ujawnia wcześniej.**

| widok | żąda szerokości | najszerszy poziomy panel | przewijalny w poziomie? |
|---|---|---|---|
| Activity Monitor | **1143 DIP** | **1130 DIP**, 18 dzieci | ⛔ **NIE** |
| Data Import | **1155 DIP** | **1131 DIP**, 20 dzieci | ⛔ **NIE** |
| Script Executor *(odniesienie)* | 1554 DIP | 841 DIP, 13 dzieci | ⛔ nie — ale 841 mieści się wszędzie |

**Dostępna szerokość dla treści zakładki** = ekran / skala − pasek boczny 280 − splitter 4:

| skala | 1920 | 1366 | 2560 |
|---|---|---|---|
| 100 % | 1636 ✅ | **1082 ⛔** | 2276 ✅ |
| 125 % | 1252 ✅ | 809 ⛔ | 1764 ✅ |
| 150 % | **996 ⛔** | 627 ⛔ | 1423 ✅ |
| 175 % | **813 ⛔** | 497 ⛔ | 1179 ✅ |
| 200 % | 676 ⛔ | 399 ⛔ | 996 ⛔ |

⭐ **Przewidywanie z pomiaru odtwarza obserwację co do skali:** na 1920 mieści się przy 100 % i 125 %,
nie mieści przy 150 % i 175 %. To jest dowód, że diagnoza trafia w mechanizm, a nie w objaw.

⛔⛔ **I to samo wyliczenie pokazuje, że problem NIE JEST o DPI:** na laptopie **1366×768 przy 100 %**
zostaje **1082 DIP** — czyli oba widoki nie mieszczą się **bez żadnego skalowania**. ⚠ 1366×768 jest
rozdzielczością, wobec której M4.4 świadomie liczyło sufity dialogów, więc nie jest to przypadek hipotetyczny.

**Wysokość** to osobny objaw i dotyczy **tylko Data Importu**: żąda **739 DIP**, podczas gdy cały obszar
roboczy przy 150 % to **688**, a przy 175 % — **590**, jeszcze przed odjęciem chromy okna (pasek tytułu 36
+ pasek zakładek + pasek statusu 24). Activity Monitor żąda 332 i w pionie problemu nie ma.
⚠ **Hipoteza dla znikającego paska statusu przy 175 %** (nie dowiedziona): treść żąda więcej wysokości, niż
zostaje, więc wiersz treści rozpycha siatkę `MainWindow` poniżej dolnej krawędzi okna i ostatni wiersz —
pasek statusu — wychodzi poza ekran. ⛔ Nie zmierzone bezpośrednio, bo `MainWindow` **nie daje się zbudować
w sesji headless** (udokumentowany kształt wieszający suite).
