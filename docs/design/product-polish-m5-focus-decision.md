# M5 / L‑1 — pierścień focus. Materiał decyzyjny

> **🔒 STATUS: DECYZJA RATYFIKOWANA I WDROŻONA (2026-08-10). Wariant 1.** Ten dokument jest odtąd
> **ZAPISEM, nie planem**.
>
> **Ratyfikowane przez użytkownika:** wariant **1** (jedna konwencja `:focus-visible` dla wszystkich
> wariantów + uzupełnienie obu braków) · pierścień `primary` = **`OnAccentBrush`** · strażnik
> w **pełnym** zakresie (wskazanie musi istnieć **i** trzymać próg), zweryfikowany podsadzeniem
> dla każdego wariantu.
>
> ⚠⚠ **JEDNO ODSTĘPSTWO OD ZATWIERDZONEGO RENDERU, wykryte przy wdrożeniu — grubość pierścienia
> `primary` to 1, nie 2.** Render pokazywał 2 px i tak został odebrany, ale grubość ramki wchodzi
> w desired size przycisku, a `ContentPresenter` Fluenta jest wcinany o `BorderThickness` — więc
> 1 → 2 przesuwa treść i rozpycha przycisk o 2 px przy każdym przejściu Tabem. To jest **§13.3 Zero
> Layout Shift** złamane dokładnie w momencie, którego zmiana dotyczy.
> ⭐ **Nic nie tracimy:** ramka o grubości 1 **już tam była** — niosła `AccentBrush`, czyli barwę
> własnego tła, więc była niewidoczna z konstrukcji. Zmiana samej barwy odsłania pierścień, który
> istniał, przy **zerowej** zmianie geometrii. Kontrast 5,29:1 jest ten sam.
>
> ⏸ **Pozostaje QA wizualne użytkownika** — w tym część, której render pokazać nie może: że obwódka
> **nie** pojawia się po kliknięciu myszą, a pojawia po Tabie.
>
> Rendery: `tools/probes/VisualCandidateProbe/out/m5f-focus-{Dark,Light}.png`
> (`dotnet run --project tools/probes/VisualCandidateProbe -- focus`).

---

## 1. ⭐⭐ Audyt opisał L‑1 jako brakujący selektor. Pomiar mówi coś innego

L‑1 brzmiał: *„Focus ring niespójny — `Button.icon`/`.flat` mają `:focus`, `.primary`/`.caption` nie."*
Zmierzone headlessowo (`Focus(NavigationMethod)` + odczyt z `ContentPresentera`):

| wariant | pseudoklasy po **Tab** | pseudoklasy po **kliknięciu** | ramka w stanie focus |
|---|---|---|---|
| `icon` | `:focus`, `:focus-visible` | **`:focus`** | `#007FD4`, grubość 1 |
| `flat` | `:focus`, `:focus-visible` | **`:focus`** | `#007FD4`, grubość 1 |
| `primary` | `:focus`, `:focus-visible` | `:focus` | `#2D6BBF` — ⚠ to jego **własna** ramka akcentu, focus jej nie zmienia |
| `caption` | `:focus`, `:focus-visible` | `:focus` | `Transparent`, **grubość 0** |

Z tego wychodzą **trzy** ustalenia, a nie jedno.

### 1.1 ⛔ Aplikacja ma DWA różne zachowania focusu, zależnie od kontrolki

`:focus` zapala się **także od myszy** — zmierzone: po `NavigationMethod.Pointer` klasa `:focus` jest
obecna, `:focus-visible` nie. Więc `Button.icon` i `Button.flat` pokazują niebieski pierścień
**po kliknięciu myszą** i trzymają go, aż fokus odejdzie.

⚠ A `CheckBox` i `RadioButton` (`ControlThemes.axaml:148` i `:257`) używają **`:focus-visible`**, czyli
reagują wyłącznie na klawiaturę. To nie jest brak selektora — to **dwie różne konwencje w jednym
produkcie**, i użytkownik trafia na nie w tym samym oknie dialogowym.

### 1.2 ⛔ Naiwna poprawka dla `primary` dałaby pierścień NIEWIDOCZNY

`FocusBorderBrush` na tle akcentu: **1,26:1 w Dark, 1,17:1 w Light** — przy progu 3:1 dla znaczącego
elementu nietekstowego. Skopiowanie settera z `Button.flat` **wygląda jak naprawa i nie naprawia nic**.
Widać to na renderze: kolumny „spoczynek" i „focus DZIŚ" są dla tego wariantu nierozróżnialne.

### 1.3 ⛔ Naiwna poprawka dla `caption` byłaby MARTWA

Ten wariant ma `BorderThickness="0"` — świadomy reset (to przyciski paska tytułu, hover sygnalizuje
**tłem**). Setter `BorderBrush` nie namalowałby **niczego**, a bezczynny styl czyta się dla następnej
osoby jak działające zabezpieczenie (ta sama pułapka co settings-center §15.7).

---

## 2. Kandydaci — każdy z tokenu, który ten wariant już zna

| wariant | kandydat | kontrast | próg |
|---|---|---|---|
| `primary` | pierścień **`OnAccentBrush`** (biel — barwa jego własnego tekstu), grubość 2 | **5,29:1** na akcencie | 3:1 ✅ |
| `caption` | **`FocusBorderBrush` jako TŁO** + glif na **`OnAccentBrush`** | tło 3,27 (D) / 3,76 (L); glif 4,21 / 4,53 | 3:1 ✅ |
| `icon`, `flat` | bez zmiany wyglądu — pytanie dotyczy **wyzwalacza**, nie barwy | — | — |

⚠⚠ **`caption` wymaga DWÓCH setterów, nie jednego — i wyszło to dopiero z renderu.** Samo tło zostawia
glif w `ForegroundBrush`, co na niebieskim daje **2,84:1 w Dark**, czyli pod progiem. Wariant „tylko
tło" został odrzucony pomiarem, zanim trafił do tego dokumentu.

⛔ **Żadnej nowej barwy** — wszyscy kandydaci składają się z tokenów już obecnych w palecie.

---

## 3. Warianty do wyboru

### Wariant 1 — pełna spójność: `:focus-visible` wszędzie + brakujące dwa stany

Wszystkie cztery warianty przycisku dostają wskazanie focusu, a **wyzwalaczem staje się
`:focus-visible`** — tak jak już działają `CheckBox`/`RadioButton`.

⭐ **Za:** jedna konwencja w całym produkcie; pierścień przestaje zostawać po kliknięciu myszą
(najczęstsza skarga na focus w aplikacjach desktopowych); zgodne z tym, co produkt **już** robi
w połowie kontrolek — więc to dokończenie istniejącej reguły, nie nowa reguła.
⚠ **Koszt:** zmiana zachowania **widoczna od razu** — dziś kliknięty przycisk ikonowy zostaje
obwiedziony, po zmianie nie. To jest zmiana na lepsze, ale **jest zmianą** i trzeba ją zobaczyć.

### Wariant 2 — tylko uzupełnić brakujące dwa, wyzwalacz bez zmian (`:focus`)

`primary` i `caption` dostają wskazanie, `icon`/`flat` zostają jak są.

⭐ **Za:** najmniejsza zmiana zachowania; nic, co dziś działa, się nie rusza.
⛔ **Przeciw:** utrwala rozjazd z §1.1 — przyciski nadal reagują na mysz, `CheckBox` nadal nie,
i to **w tym samym oknie dialogowym**. Rozwiązuje objaw z audytu, zostawia przyczynę.

### Wariant 3 — tylko ujednolicić wyzwalacz (`:focus-visible`), bez nowych stanów

⭐ **Za:** usuwa realną irytację (pierścień po kliknięciu) najmniejszym kosztem.
⛔ **Przeciw:** `primary` i `caption` nadal **nie mają żadnego wskazania focusu**, czyli nawigacja
klawiaturą po stopce dialogu i po pasku tytułu pozostaje niewidoczna. To jest dostępność, nie kosmetyka.

### Wariant 4 — nic nie zmieniamy, zapis jako nazwany wyjątek

⭐ Legalne (`color-language.md` traktuje wyjątek z powodem jako stan docelowy).
⛔ Koszt: nawigacja klawiaturą po `primary`/`caption` pozostaje bez sygnału, a dwie konwencje focusu
zostają w produkcie na stałe.

---

## 4. Czego ten materiał NIE rozstrzyga

⚠ **Pomiar jest narzędziem diagnostycznym; kryterium odbioru jest ekran (R16).** Liczby mówią, że
pierścień na `primary` jest dziś niewidoczny — nie mówią, czy biały pierścień **wygląda dobrze**
na przycisku akcji.

⚠ **Renderu statycznego NIE da się użyć do oceny wariantu 1**, bo różnica dotyczy WYZWALACZA
(mysz vs klawiatura), a nie wyglądu. Tę część ocenia się wyłącznie na uruchomionej aplikacji.

⚠ **Poza zakresem tej decyzji, ale zmierzone i warte zapisania:** `TextBox` ma własną ścieżkę focusu
przez most (`TextControlBorderBrushFocused` → `FocusBorderColor`), a `DataGrid` własną
(`DataGridCellFocusVisual*`). Ujednolicanie ich **nie jest** częścią L‑1 i nie proponuję tego tutaj.

---

## 5. Pytania do ratyfikacji

1. **Który wariant** — 1 / 2 / 3 / 4?
2. Jeżeli wariant obejmuje `primary`: czy **biały pierścień grubości 2** to właściwy język dla przycisku
   akcji, czy wolisz zobaczyć alternatywę (np. pierścień zewnętrzny poza obrysem)?
3. Czy strażnik ma powstać w tej iteracji? ⭐ Ma czym być: może asertować, że **każdy** wariant
   przycisku zmienia cokolwiek widocznego w stanie focus **i** że to coś trzyma próg 3:1 — czyli
   złapałby dokładnie oba dzisiejsze defekty (martwy setter i niewidoczny pierścień).
