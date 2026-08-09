# M5 / §10 — kontrast barw severity. Materiał decyzyjny

> **🔒 STATUS: DECYZJA RATYFIKOWANA I WDROŻONA (2026-08-10). Wariant B.** Ten dokument jest odtąd
> **ZAPISEM, nie planem** — czytaj go dla „dlaczego", nie dla „co dalej".
>
> **Ratyfikowane przez użytkownika:**
> 1. **Wariant B** — zmiana trzech wartości tokenów. *„Naprawmy kontrast u źródła, globalnie, zamiast
>    zostawiać wyjątek dla Dark/Error albo zmieniać wagę tekstu wszystkich komunikatów."*
> 2. **§10 sprostowane** — próg 3:1 dla „≥ 12 px SemiBold" zostaje, ale jako **wymóg własny
>    EmberTerna**, jawnie NIE jako „WCAG AA Large".
> 3. **Strażnik w pełnym zakresie** — cała mapa `BrushKeyFor`, 4 severity × 2 powierzchnie × 2 motywy,
>    zweryfikowany podsadzeniem naruszeń.
>
> ⏸ **Pozostaje QA wizualne użytkownika na uruchomionej aplikacji w obu motywach** — to ono zamyka
> iterację, nie zielone testy (§0.1.1).
>
> Rendery: `tools/probes/VisualCandidateProbe/out/m5c-{banner,text}-{Dark,Light}.png`
> (`dotnet run --project tools/probes/VisualCandidateProbe -- severity`).

---

## 1. Co zostało zmierzone

Wszystkie liczby to WCAG 2.x policzone na tokenach z `Themes/Colors.axaml`.

`MessageBanner` maluje **pasek (3 px), ikonę (14) i tekst komunikatu (12 px) tym samym pędzlem
severity**, a jego styl nadaje tło `PanelBrush` **obu** wariantom (`standalone` i `.docked`) — więc
powierzchnia jest jedna, niezależnie od hosta.

| motyw | severity | kontrast na `PanelBrush` | tekst 12 px (próg 4,5) | pasek + ikona (próg 3,0) |
|---|---|---|---|---|
| DARK | Error | **4,26:1** | ⛔ poniżej o 0,24 | ✅ |
| DARK | Warning | 6,91:1 | ✅ | ✅ |
| DARK | Success | 6,79:1 | ✅ | ✅ |
| DARK | Info | 5,80:1 | ✅ | ✅ |
| LIGHT | Error | 4,87:1 | ✅ | ✅ |
| LIGHT | Warning | **3,12:1** | ⛔ **poniżej o 1,38** | ✅ |
| LIGHT | Success | **3,88:1** | ⛔ poniżej o 0,62 | ✅ |
| LIGHT | Info | 5,33:1 | ✅ | ✅ |

Drugi konsument tej samej mapy — **log Messages w edytorze SQL** (`MainWindow.axaml:2034`, ten sam
`BrushKeyFor`, ten sam rozmiar 12 px, ale tło `BackgroundBrush`):

| motyw | Error | Warning | Success |
|---|---|---|---|
| DARK | 4,64 ✅ | 7,53 ✅ | *n/d* |
| LIGHT | 5,23 ✅ | **3,35 ⛔** | *n/d* |

⚠⚠ **SPROSTOWANIE MOJEJ WŁASNEJ TABELI, wykryte dopiero przy pisaniu strażnika.** Pierwsza wersja
podawała tu Success jako **4,16 ⛔ w Light** — i to było **nieprawdziwe**. `QueryMessageViewModel`
ma regułę `ShowSeverityMarker => Severity is Warning or Error`, a `MessageBrushKey` zwraca barwę
severity **tylko dla wiersza problemowego**; Info i Success czytają w logu `ForegroundBrush`
(16,49:1 w Light). ⭐ Policzyłem kontrast pary, która w tym miejscu **nigdy się nie renderuje** —
klasyczne mierzenie tokenu zamiast elementu, który maluje. Wykrył to dopiero moment, w którym
strażnik musiał wskazać KONKRETNĄ właściwość produkcyjną zamiast przepisanej mapy.
⛔ Nie zmienia to decyzji: Success wymagał korekty **z powodu banera**, gdzie renderuje się naprawdę.

⭐ **Metoda się waliduje na znanych punktach:** ta sama funkcja daje `BorderBrush` → **1,60 Dark /
1,35 Light** (dokładnie liczby, które zapisało M3.5 przy `ControlOutlineBrush`) oraz
`ControlOutline` → **3,10 / 3,00** („policzone na progu", jak zapisano) i `AccentBrush` na Panel
w Dark → **2,89** (= znane **P‑2** z `color-language.md` §9.2).

---

## 2. ⭐⭐ Dwa znaleziska, które zmieniają kształt pytania

### 2.1 Defekt dotyczy WYŁĄCZNIE tekstu

Pasek i ikona przechodzą próg 3:1 we **wszystkich ośmiu** kombinacjach. Sygnał severity — to, po czym
użytkownik rozpoznaje rodzaj komunikatu — jest poprawny wszędzie i **nie zależy od barwy tekstu**.

### 2.2 ⛔ To NIE jest defekt `MessageBanner` — poprawka lokalna byłaby naruszeniem R7

| token | konsumentów | z tego maluje **tekst** | maluje ikonę / ramkę / tło |
|---|---|---|---|
| `ErrorBrush` | 30 | ≈ 8 | ≈ 7 |
| `WarningBrush` | 36 | ≈ 9 | ≈ 8 |
| `SuccessIconBrush` | 25 | ≈ 13 | ≈ 6 |

Te same pędzle malują mały tekst m.in. w Script Executorze, Batch Results, Data Import, Performance
i debuggerze. **Baner był miejscem, w którym defekt znaleziono, a nie jego zakresem.**

> ⚠ **Korekta mojego własnego wcześniejszego sformułowania.** W inwentaryzacji nazwałem to
> „naruszeniem na `MessageBanner`". Pomiar zasięgu pokazuje, że to nieprawda.

### 2.3 ⚠ Rodzina severity jest asymetryczna — i to jest osobna informacja

`MessageBanner.BrushKeyFor` zwraca:

| severity | token | rodzaj tokenu |
|---|---|---|
| Error | `ErrorBrush` | semantyczny |
| Warning | `WarningBrush` | semantyczny |
| **Success** | **`SuccessIconBrush`** | ⚠ **ikonowy** |
| Info | `SubtleForegroundBrush` | ogólnotekstowy |

**`SuccessBrush` nie istnieje** — są tylko `SuccessIconColor`/`SuccessIconBrush`. Konsekwencja jest
praktyczna: zmiana barwy Success „dla tekstu" pociąga za sobą **25 konsumentów ikonowych**, i to
sprzężenie istnieje wyłącznie dlatego, że Success nie ma własnego tokenu semantycznego.
⛔ Nie proponuję tworzenia `SuccessBrush` w tej iteracji — to osobna decyzja (§3.3 katalogu: rola
dostaje własną wartość, ale nowy token wymaga uzasadnienia, a jeden konsument to legalizacja
wartości, nie rola).

---

## 3. ⚠⚠ Pułapka w samym §10 — trzeba ją znać PRZED wyborem wariantu

§10 opisuje wiersz *„Tekst duży (≥ 14 px lub ≥ 12 px SemiBold) → ≥ 3:1"* jako **„WCAG AA Large"**.

**To oznaczenie jest nieprawdziwe.** WCAG 2.1 definiuje duży tekst jako **18 pt (24 px)** albo
**14 pt bold (18,7 px)**. Najwyższa rola typograficzna EmberTerna to `Text.Display` = **23 px**, więc
**żadna rola się nie kwalifikuje** i cały ten wiersz jest — jako „WCAG AA Large" — niestosowalny.

⭐ Znaczenie dla decyzji: wariant „zrób tekst SemiBold i zejdź na próg 3:1" spełnia **§10 jak
napisane**, ale **nie spełnia WCAG AA**. Podaję go niżej jako wariant C właśnie po to, żeby ta
różnica była widoczna, a nie schowana w tabeli.

⛔ Niezależnie od wybranego wariantu **§10 wymaga sprostowania w `product-polish.md`** — albo przez
poprawienie etykiety normy, albo przez zapisanie tego progu jako **wymogu własnego EmberTerna**
(jak ostatni wiersz tej samej tabeli, który już tak jest opisany).

---

## 4. Warianty

Kandydaci policzeni **przy progu 4,5:1 na `PanelBrush`** (trudniejsza z dwóch powierzchni).
Metoda: przyciemnianie przez **mnożenie kanałów RGB**, co zachowuje odcień i nasycenie HSV
**dokładnie**. Dla Dark/Error trzeba w drugą stronę (mieszanie z bielą) — odcień zachowany,
⚠ nasycenie nieznacznie spada.

| | token | obecnie | kandydat | Panel | Background |
|---|---|---|---|---|---|
| LIGHT | `WarningBrush` | `#C77800` | **`#A16100`** | 3,12 → **4,52** | 3,35 → 4,85 |
| LIGHT | `SuccessIconBrush` | `#2E8B4F` | **`#2A7E48`** | 3,88 → **4,57** | 4,16 → 4,90 |
| DARK | `ErrorBrush` | `#F44747` | **`#F55252`** | 4,26 → **4,53** | 4,64 → 4,93 |

*(Dark/Warning, Dark/Success, Light/Error są już nad progiem — bez zmiany.)*

### Wariant A — nic nie zmieniamy, zapisujemy nazwany wyjątek

Trzy kombinacje zostają pod progiem, a §10 staje się regułą, której produkt nie spełnia.
⭐ **To jest legalny wybór** — `color-language.md` traktuje wyjątek z zapisanym powodem jako stan
docelowy, nie dług. Koszt: reguła, która nie obowiązuje, przestaje cokolwiek chronić.

### Wariant B — zmiana trzech wartości tokenów *(zmierzony, wyrenderowany)*

⭐ **Za:** naprawia defekt **wszędzie tam, gdzie te pędzle malują mały tekst** (~30 miejsc), a nie
tylko w banerze — czyli zgodnie z R7. Zmiany paska i ikony są nieistotne (wszystkie miały zapas nad
progiem 3:1).
⚠ **Koszt wizualny, widoczny na renderze:** Light/Warning realnie ciemnieje (bursztyn → ciemniejszy
bursztyn-brąz) — to **jedyna** zmiana, którą widać. Light/Success jest ledwie zauważalna, Dark/Error
**wizualnie nierozróżnialna** (delta 0,27).
⚠ Pociąga 25 ikonowych konsumentów `SuccessIconBrush` (§2.3).

**B′ — wariant zawężony:** tylko Light (Warning + Success), Dark/Error zostawiamy.
Uzasadnienie: deficyt 0,24 jest niewidoczny w obie strony. ⚠ Koszt: §10 nadal niespełnione w jednym
miejscu, więc strażnik nie mógłby być bezwarunkowy.

### Wariant C — tekst severity na `SemiBold`, barwy bez zmian

Spełnia **§10 jak napisane** (próg 3:1 dla „≥ 12 px SemiBold”) i realnie poprawia czytelność.
⛔ **Nie spełnia WCAG AA** (§3). ⚠ Zmienia wagę wizualną każdego komunikatu w aplikacji i musiałby
objąć baner + log + ~30 miejsc, żeby był spójny. Widoczny na renderze jako kolumna **C**.

### Wariant D — tekst neutralny (`ForegroundBrush`), kolor zostaje na pasku i ikonie

Kontrast wchodzi na **11,25:1 / 16,49:1** — z ogromnym zapasem, i jest to jedyny wariant, który
rozwiązuje problem *strukturalnie* (§2.1: sygnał i tak niesie pasek + ikona).
⛔⛔ **Odwraca ratyfikowaną decyzję z rundy QA Seam 4**, gdzie tekst dostał barwę severity celowo:
*„komunikat Error czyta się jako błąd w całości, nigdy w połowie czerwony i w połowie neutralny"*.
**Wymagałby jawnej ponownej ratyfikacji** i nie proponuję go jako domyślnego.

---

## 5. Czego ten materiał NIE rozstrzyga

⚠ **Pomiar jest narzędziem diagnostycznym; kryterium odbioru jest ekran (R16).** Liczby mówią, że
Light/Warning jest pod progiem — **nie mówią, że wygląda źle**. Pułapka 17 ostrzega dokładnie przed
tym krokiem: reguła opisuje to, co już jest dobre, a element niezgodny z regułą bywa wyjątkiem,
który działa.

⛔ Obowiązuje brama `color-language.md` **§0.5**: *czy użytkownik rozpozna rzecz SZYBCIEJ?*
Moja odpowiedź, uczciwie: **„tak" dla Light/Warning** (3,12:1 to realnie słaby odczyt małego tekstu
i widać to na renderze), **„nie wiadomo" dla Light/Success**, **„nie" dla Dark/Error** (zmiana
niewidoczna — jej jedyny zysk to zgodność liczby z progiem).
⭐ Zgodnie z §0.5 „nie wiadomo" jest odpowiedzią **odmowną**, więc sam z siebie nie wdrażam nic.

---

## 6. ⭐ As-built — co faktycznie weszło

| plik | zmiana |
|---|---|
| `Themes/Colors.axaml` | trzy wartości + komentarz przy każdej z pomiarem i powodem |
| `Themes/Colors.axaml` | ⚠ komentarze przy **`ActionRunColor`** w OBU słownikach — para rozeszła się w Light |
| `product-polish.md` §10 | sprostowana etykieta normy + nowe **§10.1** (egzekwowanie) |
| `DesignTokenApplicationTests` | 3 nowe teorie × 2 motywy = **6 testów** |
| `VisualCandidateProbe/Severity.cs` | sonda + nagłówek „decyzja zamknięta" |

### 6.1 ⚠ Rzecz, której nie było w wariantach, a wyszła przy wdrożeniu: `ActionRunColor`

W motywie jasnym `ActionRunColor` (rola R‑1 „Uruchom") miał wartość **celowo identyczną**
z `SuccessIconColor`, z komentarzem, który to stwierdzał. Zmiana Success rozjeżdża tę parę.

🔒 **Rozstrzygnięte pomiarem, nie domysłem:** wszystkie **cztery** wystąpienia `ActionRunBrush` to
`SvgIcon` (Debugger, MainWindow ×2, Trace), czyli element nietekstowy — próg **3:1**, który
`#2E8B4F` spełnia z zapasem (3,88:1). ⭐ Więc **`ActionRunColor` zostaje**, a para rozchodzi się
świadomie — i jest to **projekt działający zgodnie z zamysłem**: W4 rozdzielił te tokeny dokładnie
po to, żeby przestrojenie jednej roli nie ruszało drugiej. Komentarze w obu słownikach zapisują
rozejście razem z powodem, więc żaden nie stał się nieprawdziwy.

### 6.2 Weryfikacja

* build **0/0**; suite **8351** = 8193 + **103** (97 + 6) + 55, trzy partycje; smoke czysty.
* ⭐ **Wszystkie trzy strażniki zweryfikowane podsadzeniem naruszenia**, a podsadzenie dało liczby
  **identyczne z pomiarem statycznym** (3,12 na banerze, 3,35 w logu) — dwie niezależne metody
  potwierdziły się nawzajem.
* ⭐⭐ Najmocniejszy pojedynczy wynik podsadzenia: przy cofniętym `#C77800` **testy tekstu padły,
  a test sygnału został ZIELONY** (3,12 > 3,0). To jest dowód, że dwa progi §10 są w strażniku
  naprawdę rozdzielone, a nie zlane w jeden — czyli że test mierzy to, co deklaruje.

---

## 7. Pytanie do ratyfikacji *(zamknięte — odpowiedzi w nagłówku)*

1. **Który wariant** — A / B / B′ / C / D?
2. Jeżeli B lub B′: czy akceptujesz **widoczne przyciemnienie Light/Warning** jako koszt?
3. **§10 wymaga sprostowania niezależnie od wyboru** (§3) — poprawiamy etykietę normy czy zapisujemy
   próg jako wymóg własny EmberTerna?
4. Czy **strażnik kontrastu** ma powstać w tej iteracji (rozszerzenie istniejącego `ContrastRatio`
   z `DesignTokenApplicationTests` na całą mapę `BrushKeyFor` × obie powierzchnie × oba motywy)?
   ⚠ Strażnik da się napisać **tylko** dla wariantu, który faktycznie spełnia próg — przy A i B′
   musiałby zawierać zapisany wyjątek.
